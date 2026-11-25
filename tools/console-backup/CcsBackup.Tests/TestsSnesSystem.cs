// Classic Cart Studio console backup and restore tests. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The Super NES Classic's system-level operations (group "snes-system"), on simulated consoles of its own model (board dp-shvc, REGION USA
// or EUR) with the boot-stage shapes a Super NES Classic's backup carries (boot0 32,768 bytes; its own u-boot 573,440 bytes without a
// nandinfo block): first-time preparation with the pinned hakchi, Return to Original Software, Console Rescue of the startup stages and
// Developer Mode (startup software, system part, test boot), each through the engine directly and through the worker the window talks
// to. Refusals: another region's kernel, the Super Famicom's region, a table that does not put the system on nandb and the data storage
// on nandc, another model's backup with this console's identity, a stale session or plan, missing or changed evidence, and failures
// that stop with nothing retried. The stock cases need a genuine stock Super NES Classic kernel (--snes-kernel, read locally) and the
// model-mismatch cases a genuine NES Classic kernel (--kernel); without them those parts are skipped and said so. Simulated consoles show
// what Classic Cart Studio's engine does with what a console reports; they are not the console's hardware.
using CcsBackup.Archive;
using CcsBackup.Compose;
using CcsBackup.Device;
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        const string SsUsa14 = "228967ab1035a347caa9c880419df487";
        /// <summary>a Super NES Classic's storage with its data part sized to the 403,456 KiB stock data fixture (a data image without hakchi's
        /// files, as an original console's is); the other parts as a North American console reported them, its UDISK smaller so the table fits
        /// the 536,870,912-byte storage (no engine code fixes a part's size: the console's own table is read)</summary>
        static readonly (string dev, string label, long bytes)[] SsPrepLayout =
            { ("nanda", "boot", 3072L * 1024), ("nandb", "rootfs", 65536L * 1024), ("nandc", "data", 403456L * 1024), ("nandd", "private", 1024L * 1024), ("nande", "UDISK", 32000L * 1024) };

        static Action<Stream> SsPhy(params (long block, byte[] img)[] put) => s =>
        {
            SimulatedConsole.Pattern(s, Support.PhysicalBytes, "phy");
            foreach (var p in put) { s.Position = p.block * 0x20000; s.Write(p.img, 0, p.img.Length); }
            s.Position = s.Length;
        };

        /// <summary>a simulated Super NES Classic (board dp-shvc): kinds stock | modified (the original kernel in the spare slot, hakchi's files)
        /// | modified-noorig; its boot stages shaped as a Super NES Classic's backup holds them</summary>
        static string SsConsole(string kind, string region = "USA", string data = "data-modern.img", (string, string, long)[] layout = null, string hwid = HwidA)
        {
            var dir = NewDir("snes-sys-" + kind + "-" + region);
            var mod = File.ReadAllBytes(Path.Combine(Fx, "kernel-modified.img"));
            var files = new Dictionary<string, Action<Stream>>(); byte[] active; int entries;
            switch (kind)
            {
                case "stock": active = SnesStockKernel(); entries = 0; files["phy"] = SsPhy((0x30, active)); break;
                case "modified": { var orig = SnesStockKernel(); active = mod; entries = 3; files["boot2-68"] = st => st.Write(orig, 0, orig.Length); files["phy"] = SsPhy((0x30, active), (0x68, orig)); break; }
                case "modified-noorig": active = mod; entries = 3; files["phy"] = SsPhy((0x30, active)); break;
                default: throw new ArgumentException(kind);
            }
            var dataPath = Path.Combine(Fx, data);
            files["nandc"] = st => { using (var f = File.OpenRead(dataPath)) f.CopyTo(st); };
            files["boot2"] = st => st.Write(active, 0, active.Length);
            files["boot0"] = st => { var b = BootImage.Synthetic(true, 32768, "snes-boot0"); st.Write(b, 0, b.Length); };
            files["boot1"] = st => { var b = BootImage.Synthetic(false, 573440, "snes-uboot"); st.Write(b, 0, b.Length); };
            SimulatedConsole.Create(Path.Combine(dir, "c"), hwid, (layout ?? SnesLayout).ToList(), files, entries, region, "release-v2.0.14-0-gd8b65c6", "dp-shvc", "2048 64 4096");
            return Path.Combine(dir, "c");
        }

        /// <summary>one worker request as the window sends it (WorkerHost.RunOn in this process), answering each event with answer's line (or
        /// nothing); every event until the result. A console lock held elsewhere ("busy") is waited for.</summary>
        static List<Dictionary<string, object>> SsDrive(string request, Settings s, Func<Dictionary<string, object>, string> answer)
        {
            for (int attempt = 0; ; attempt++)
            {
                var input = new QueueStream(); var output = new QueueStream(); var enc = new UTF8Encoding(false);
                var th = new Thread(() => { try { WorkerHost.RunOn(input, output, "tests", "CcsBackup.Tests", "test", s.Path); } finally { output.End(); } }) { IsBackground = true }; th.Start();
                void Line(string l) { var b = enc.GetBytes(l + "\n"); input.Write(b, 0, b.Length); }
                Line(request);
                var evs = new List<Dictionary<string, object>>(); string line; var rd = new StreamReader(output, enc);
                while ((line = rd.ReadLine()) != null)
                {
                    var ev = Json.Parse(line); evs.Add(ev);
                    if (ev.S("event") == "result") break;
                    var a = answer(ev); if (a != null) Line(a);
                }
                input.End(); th.Join(120000);
                if (evs.LastOrDefault()?.S("state") != "busy" || attempt >= 90) return evs;
                Thread.Sleep(2000);
            }
        }
        static string SsLine(JObj o) => Json.Write(o).Replace("\n", " ");
        static Dictionary<string, object> SsResult(List<Dictionary<string, object>> evs) => evs.LastOrDefault(e => e.S("event") == "result");
        static string SsReq(string cmd, string console, JObj extra = null)
        {
            var o = new JObj { { "cmd", cmd }, { "console", "simulated" }, { "folder", console } };
            if (extra != null) foreach (var kv in extra) o.Add(kv.Key, kv.Value);
            return SsLine(o);
        }

        public static void SnesSystem()
        {
            Part("the four system operations are available for the Super NES Classic, from the registry", () =>
            {
                var ops = new[] { "preparation", "return", "rescue", "developer" };
                Check(ops.All(o => Capabilities.Allowed("capability", "snes-classic-" + o) && ConsoleModels.Snes.Refusal(o) == null && ConsoleModels.Nes.Refusal(o) == null),
                      "preparation, return, startup-stage repair and Developer Mode: supported capabilities of the Super NES Classic (the registry), and the NES Classic's as before");
                var jpn = ConsoleIdentity.Parse("ccsbr=1\nboard=dp-shvc\nregion=JPN\nend=1\n");
                Check(ops.All(o => (ConsoleModels.RefusalFor(jpn, o) ?? "").Contains("not a North American or European Super NES Classic Edition")), "the Super Famicom's region (JPN) is refused for each of them");
                var t1 = ConsoleIdentity.Parse("ccsbr=1\nparttable=mbr:mbr:32 nanda:boot:6144 nandb:rootfs:131072 nandc:data:614400 nandd:private:2048 nande:UDISK:128000\nend=1\n");
                var t2 = ConsoleIdentity.Parse("ccsbr=1\nparttable=mbr:mbr:32 nanda:boot:6144 nandb:rootfs:131072 nandc:private:614400 nandd:data:2048 nande:UDISK:128000\nend=1\n");
                var t3 = ConsoleIdentity.Parse("ccsbr=1\nparttable=mbr:mbr:32 nanda:rootfs:6144 nandb:boot:131072 nandc:data:614400 nandd:private:2048 nande:UDISK:128000\nend=1\n");
                Check(Support.HakchiLayout(t1) == null && (Support.HakchiLayout(t2) ?? "").Contains("data: nandd") && (Support.HakchiLayout(t3) ?? "").Contains("system: nanda"),
                      "the table a North American Super NES Classic reported puts its system on nandb and its data storage on nandc, as hakchi's installation and the console programs expect; another arrangement is refused by name");
            });

            // ------------------------------------------------------------------ preparation
            string prepared = null, snapshot = null; Settings ps = null;
            Part("preparation of a stock Super NES Classic with the pinned hakchi", () =>
            {
                ps = SettingsIn(NewDir("snes-prep"), false);
                var c = SsConsole("stock", data: "data-stock.img", layout: SsPrepLayout);
                var (p0, r0, _, _, _) = PrepPlanFor(c, ps);
                Check(p0 == null && r0.Problem.StartsWith("backup-required") && Writes(c).Count == 0, "no backup of this console yet: refused before anything (" + r0?.Problem + ")");
                var b = Backup(c, ps); var bv = b.Ok ? Verifier.VerifyFile(b.PrimaryPath) : null;
                Check(b.Ok && bv.Ok && bv.Model == ConsoleModels.Snes && bv.RecoveryBaseline && bv.OriginalSource == "active" && bv.Members["reference/boot0.bin"].size == 32768 && bv.Members["reference/boot1.bin"].size == 573440,
                      "its first backup is its original recovery baseline: the North American stock kernel running, both startup stages as complete images (boot0 32,768 bytes, its own u-boot 573,440 bytes) (" + b.Problem + ")");
                Poke(c, "nandd", 4096, 0x37);   // a change after the baseline: the preparation's own capture must hold it
                var (p, r, con, ctx, adm) = PrepPlanFor(c, ps);
                var cv = p?.CurrentBackup == null ? null : Verifier.VerifyFile(p.CurrentBackup);
                Check(p != null && p.State == "original" && p.Steps.SequenceEqual(Preparation.Order) && cv != null && cv.Ok && cv.Model == ConsoleModels.Snes && MemberSha(p.CurrentBackup, "restore/nandd.img") == Sha(SimulatedConsole.Dev(c, "nandd")),
                      "the plan: hakchi 3.9.3's four steps in its order (the kernel last), bound to a complete backup of this Super NES Classic captured in this session (" + (r?.Problem ?? p?.Refusal) + ")");
                Check(p.Notes.Any(n => n.Contains("its 21 games and their saves stay")) && !p.Notes.Any(n => n.Contains("30 games")), "the plan's words name this model's 21 built-in games, never the NES Classic's 30");
                var reCon = RecoverySessions.OpenSimulated(c, ImageKind.Prepare);
                Check(Preparation.Execute(reCon, p, p.Digest, p.Nonce, ctx, () => UsePrep(ps, adm, reCon, p)).State == "refused" && Writes(c).Count == 0, "the console connected again after the review (a stale session): the plan is refused, nothing written");
                Check(Preparation.Execute(con, p, Other(p.Digest), p.Nonce, ctx, () => UsePrep(ps, adm, con, p)).State == "refused" && Writes(c).Count == 0 && p.Nonce != null, "a confirmation for another plan: refused, nothing written, the confirmation unused");
                File.Move(p.CurrentBackup, p.CurrentBackup + ".withheld");
                var mp = Preparation.Execute(con, p, p.Digest, p.Nonce, ctx, () => UsePrep(ps, adm, con, p));
                Check(mp.State == "refused" && mp.Problem.Contains("missing") && Writes(c).Count == 0 && p.Nonce != null && !File.Exists(Path.Combine(ps.RecordsFolder, "admission-" + adm.TicketId + ".used")),
                      "the protective backup made in this session moved aside: refused, nothing written, neither the confirmation nor the admission used (" + mp.Problem + ")");
                File.Move(p.CurrentBackup + ".withheld", p.CurrentBackup);
                var keep = File.ReadAllBytes(p.CurrentBackup); var flip = (byte[])keep.Clone(); flip[flip.Length / 2] ^= 1; File.WriteAllBytes(p.CurrentBackup, flip);
                var cp = Preparation.Execute(con, p, p.Digest, p.Nonce, ctx, () => UsePrep(ps, adm, con, p));
                Check(cp.State == "refused" && Writes(c).Count == 0 && p.Nonce != null, "the protective backup changed by one byte: refused, nothing written (" + cp.Problem + ")");
                File.WriteAllBytes(p.CurrentBackup, keep);
                // the console identified differently at the review: the Super Famicom's region, and a table hakchi's steps cannot use
                var cfgPath = Path.Combine(c, "console.json"); var cfg = File.ReadAllText(cfgPath);
                SetCfg(c, "region", "JPN");
                var (pj, rj, _, _, _) = PrepPlanFor(c, ps);
                Check(pj == null && rj.Problem.Contains("not an NES Classic Edition or a North American or European Super NES Classic Edition") && Writes(c).Count == 0, "the same console reporting the Super Famicom's region: refused before anything is read (" + rj?.Problem + ")");
                File.WriteAllText(cfgPath, cfg);
                var before = History.List(ps).Count;
                SetCfg(c, "parttable", "mbr:mbr:32 nanda:boot:6144 nandb:rootfs:131072 nandc:private:806912 nandd:data:2048 nande:UDISK:64000");
                var (pl, rl, _, _, _) = PrepPlanFor(c, ps);
                Check(pl == null && rl.Problem.Contains("data: nandd") && Writes(c).Count == 0 && History.List(ps).Count == before, "a table whose data storage is not on nandc: refused before any capture or write (" + rl?.Problem + ")");
                File.WriteAllText(cfgPath, cfg);
                // through the worker, as the window asks: a new session, its own capture and admission, the plan, the confirmation
                var evs = SsDrive(SsReq("prepare-plan", c), ps, ev => ev.S("event") == "prep-plan" ? SsLine(new JObj { { "cmd", "prepare-confirm" }, { "digest", ev.O("plan").S("digest") }, { "nonce", ev.O("plan").S("nonce") } }) : null);
                var res = SsResult(evs); var plan = evs.FirstOrDefault(e => e.S("event") == "prep-plan")?.O("plan");
                Check(res?.S("state") == "prepared" && plan?.S("state") == "original" && Writes(c).SequenceEqual(new[] { "prep files", "prep uboot", "prep slot68", "prep kernel" }),
                      "worker: prepared, each step once in hakchi's order and read back (" + res?.S("problem") + "; " + string.Join(", ", Writes(c)) + ")");
                Check(File.ReadAllBytes(SimulatedConsole.Dev(c, "boot2-68")).SequenceEqual(SnesStockKernel()) && File.ReadAllBytes(SimulatedConsole.Dev(c, "boot1")).SequenceEqual(SimulatedConsole.SimHakchiUboot),
                      "the console's own original kernel is in the spare slot (the way back) and hakchi's u-boot is the bootloader");
                var rec = Record.Load(res?.S("record") ?? ""); var ra = rec?.O("after"); var rb = rec?.O("before");
                Check(rec?.S("state") == "prepared" && ra?.S("slot68_md5") == SsUsa14 && rb?.S("active_md5") == SsUsa14 && rb?.S("snapshot") != null && Verifier.VerifyFile(rb.S("snapshot")).Model == ConsoleModels.Snes,
                      "the preparation record keeps this Super NES Classic's pre-preparation snapshot and the state it left (original kernel " + SsUsa14.Substring(0, 8) + "... in the spare slot)");
                Check(Verifier.VerifyFile(b.PrimaryPath).Ok, "the baseline backup is unchanged and verified");
                File.Delete(Path.Combine(c, "writes.log"));
                prepared = c; snapshot = rb?.S("snapshot");
            });

            Part("preparation refusals: another region's kernel, another model's backup, a step that fails", () =>
            {
                // the North American kernel on a console that reports EUR: not its stock kernel, so no backup is an original baseline
                var se = SettingsIn(NewDir("snes-prep-eur"), false); var ce = SsConsole("stock", "EUR", "data-stock.img", SsPrepLayout);
                var be = Backup(ce, se); var (pe, re, _, _, _) = PrepPlanFor(ce, se);
                Check(be.Ok && !Verifier.VerifyFile(be.PrimaryPath).RecoveryBaseline && pe == null && re.Problem.Contains("recovery baseline") && Writes(ce).Count == 0,
                      "a European Super NES Classic running the North American kernel: its backup is a current-state backup, never an original baseline, so preparation is refused (" + re?.Problem + ")");
                GoneConsole(ce); GoneBackups(se);
                // a step fails: stopped there, nothing later written, nothing retried, the record says where
                var sf = SettingsIn(NewDir("snes-prep-fail"), false); var cf = SsConsole("stock", data: "data-stock.img", layout: SsPrepLayout); Backup(cf, sf);
                File.WriteAllText(Path.Combine(cf, "faults.json"), "{\"prep\":{\"step\":\"uboot\"}}");
                var (pf, rf, conf, ctxf, admf) = PrepPlanFor(cf, sf);
                var of = pf == null ? null : Preparation.Execute(conf, pf, pf.Digest, pf.Nonce, ctxf, () => UsePrep(sf, admf, conf, pf));
                Check(of != null && of.State == "stopped" && of.Done.SequenceEqual(new[] { "files" }) && Writes(cf).SequenceEqual(new[] { "prep files" }) && !File.Exists(SimulatedConsole.Dev(cf, "boot2-68"))
                      && Record.Load(of.RecordFile)?.S("state") == "stopped", "the bootloader step fails: stopped there, the spare slot and kernel never written, nothing retried, recorded as stopped (" + (of?.Problem ?? rf?.Problem) + ")");
                GoneConsole(cf); GoneBackups(sf);
            });

            Part("preparation refuses another model's backup with this console's identity", () =>
            {
                // a made-up case (two consoles never share a hardware id): an NES Classic backup with this identity is the oldest baseline
                var sm = SettingsIn(NewDir("snes-prep-model"), false);
                var nes = Console_("stock", HwidA); var nb = Backup(nes, sm); GoneConsole(nes);
                var cm = SsConsole("stock", data: "data-stock.img", layout: SsPrepLayout); Backup(cm, sm);
                var (pm, rm, _, _, _) = PrepPlanFor(cm, sm);
                Check(nb.Ok && pm == null && rm.Problem.Contains("baseline backup is of NES Classic Edition") && Writes(cm).Count == 0, "the admitted baseline is an NES Classic's: refused before anything is written (" + rm?.Problem + ")");
                GoneConsole(cm); GoneBackups(sm);
            });

            // ------------------------------------------------------------------ return to original software
            Part("Return to Original Software on the prepared Super NES Classic", () =>
            {
                if (prepared == null) { Check(false, "no prepared Super NES Classic to return"); return; }
                var c = prepared; var s = ps; var sv = Verifier.VerifyFile(snapshot);
                var table0 = sv.Manifest.O("layout").S("partition_table");
                Check(sv.Ok && sv.Model == ConsoleModels.Snes && Table(c) != table0, "prepared: the table tuned by hakchi's installation; the snapshot taken in the preparation session is the way back");
                var cfgPath = Path.Combine(c, "console.json"); var cfg = File.ReadAllText(cfgPath);
                // the same console reporting another region: its original kernel is not that region's stock kernel
                SetCfg(c, "region", "EUR");
                var (pr, rr) = ReturnEngine.Prepare(RecoverySessions.OpenSimulated(c, ImageKind.Return), Ctx(s, ImageKind.Return));
                Check(pr == null && rr.Problem.Contains("of this console's model and region") && Writes(c).Count == 0, "the console reporting EUR while its snapshot runs the North American kernel: refused, nothing written (" + rr?.Problem + ")");
                File.WriteAllText(cfgPath, cfg);
                // a newer preparation record whose snapshot is another model's backup with this identity (made up): refused by the model check
                var sn = SettingsIn(NewDir("snes-return-nes"), false); var nesc = Console_("modified-noorig", HwidA); var nesb = Backup(nesc, sn); GoneConsole(nesc);
                var recFile = Directory.GetFiles(s.RecordsFolder, "prepare-*.json").Select(f => (f, Record.Load(f))).Where(x => x.Item2?.S("state") == "prepared").OrderByDescending(x => x.Item2.S("updated_utc"), StringComparer.Ordinal).First();
                var fake = recFile.Item2; var fb = (Dictionary<string, object>)fake["before"]; fb["snapshot"] = nesb.PrimaryPath; fb["snapshot_sha256"] = Sha(nesb.PrimaryPath); fake["updated_utc"] = "9999-12-31T00:00:00.0000000Z";
                var fakeFile = Path.Combine(s.RecordsFolder, "prepare-zzzzmodel.json"); File.WriteAllText(fakeFile, Json.Write(fake));
                var (pm, rm) = ReturnEngine.Prepare(RecoverySessions.OpenSimulated(c, ImageKind.Return), Ctx(s, ImageKind.Return));
                Check(pm == null && rm.Problem.Contains("is of NES Classic Edition, not of this console's model (Super NES Classic Edition)") && Writes(c).Count == 0, "a snapshot that is an NES Classic's backup: refused, nothing written (" + rm?.Problem + ")");
                File.Delete(fakeFile); GoneBackups(sn);
                // the plan and its evidence
                var con = RecoverySessions.OpenSimulated(c, ImageKind.Return); var ctx = Ctx(s, ImageKind.Return);
                var (plan, refused) = ReturnEngine.Prepare(con, ctx);
                Check(refused == null && plan != null && plan.Steps.SequenceEqual(ReturnEngine.Order) && plan.KernelMd5 == SsUsa14 && plan.UbootWantMd5 == sv.Members["reference/boot1.bin"].md5 && plan.UbootWantSize == 573440
                      && plan.Notes.Any(n => n.Contains("the 21 games")) && !plan.Notes.Any(n => n.Contains("30 games")),
                      "the return plan: hakchi's uninstall in its order, back to this console's own North American kernel and its own 573,440-byte u-boot from the snapshot; its words name the 21 games (" + refused?.Problem + ")");
                var pv = Verifier.VerifyFile(plan.ProtectivePath);
                Check(pv.Ok && pv.Model == ConsoleModels.Snes && plan.ProtectiveArchive.Session == con.SessionId, "the console as it is now is backed up first, in this session, as a Super NES Classic");
                Check(ReturnEngine.Execute(RecoverySessions.OpenSimulated(c, ImageKind.Return), plan, plan.Digest, plan.Nonce, ctx).State == "refused" && Writes(c).Count == 0, "another connection (a stale session): refused, nothing written");
                var nonce = plan.Nonce; File.Move(plan.ProtectivePath, plan.ProtectivePath + ".withheld");
                var m1 = ReturnEngine.Execute(con, plan, plan.Digest, nonce, ctx);
                Check(m1.State == "refused" && m1.Problem.Contains("missing") && Writes(c).Count == 0 && plan.Nonce == nonce, "the protective backup moved aside: refused, nothing written, the confirmation unused (" + m1.Problem + ")");
                File.Move(plan.ProtectivePath + ".withheld", plan.ProtectivePath);
                // the bootloader arrives differently in the console's memory: stopped at the staging, nothing written to storage at all
                File.WriteAllText(Path.Combine(c, "faults.json"), "{\"ret_stage_corrupt\":true}");
                var so = ReturnEngine.Execute(con, plan, plan.Digest, nonce, ctx);
                Check(so.State == "stopped" && Writes(c).Count == 0 && Record.Load(so.RecordFile)?.S("state") == "stopped" && File.ReadAllBytes(SimulatedConsole.Dev(c, "boot1")).SequenceEqual(SimulatedConsole.SimHakchiUboot),
                      "the bootloader received differently in the console's memory: stopped before any write, recorded, nothing retried (" + so.Problem + ")");
                File.Delete(Path.Combine(c, "faults.json"));
                // through the worker: a new session, its own protective backup, the confirmation
                var evs = SsDrive(SsReq("return-plan", c), s, ev => ev.S("event") == "return-plan" ? SsLine(new JObj { { "cmd", "return-confirm" }, { "digest", ev.O("plan").S("digest") }, { "nonce", ev.O("plan").S("nonce") } }) : null);
                var res = SsResult(evs);
                Check(res?.S("state") == "returned" && Writes(c).SequenceEqual(new[] { "ret kernel", "ret uboot", "ret files", "ret table" }),
                      "worker: returned, each step once in hakchi's uninstall order and read back (" + res?.S("problem") + "; " + string.Join(", ", Writes(c)) + ")");
                Check(Md5(File.ReadAllBytes(SimulatedConsole.Dev(c, "boot2"))) == SsUsa14 && Md5(File.ReadAllBytes(SimulatedConsole.Dev(c, "boot1"))) == sv.Members["reference/boot1.bin"].md5 && Table(c) == table0 && !File.Exists(Path.Combine(c, "prepared-files.txt")),
                      "the console's own North American kernel and its own u-boot are back, hakchi's files are gone and the table reads as before preparing");
                // session B: the unchanged restore of the snapshot, after the restart
                var bcon = RecoverySessions.OpenSimulated(c, ImageKind.Restore); var bctx = Ctx(s, ImageKind.Restore);
                var (bp, br) = RestoreEngine.Prepare(bcon, snapshot, bctx);
                var bo = bp == null || bp.Refusals.Count > 0 ? null : RestoreEngine.Execute(bcon, bp, bp.Digest, bp.Nonce, bctx);
                Check(bo != null && bo.Ok && new[] { "nanda", "nandb", "nandc", "nandd", "nande" }.All(t => MemberSha(snapshot, "restore/" + t + ".img") == Sha(SimulatedConsole.Dev(c, t))),
                      "then Console Restore puts every storage part back exactly as the snapshot holds it (" + (br?.Problem ?? bo?.Problem ?? string.Join("; ", bp?.Refusals ?? new List<string>())) + ")");
                GoneConsole(c); GoneBackups(s);
            });

            // ------------------------------------------------------------------ Console Rescue
            Part("Console Rescue of a Super NES Classic's startup stages", () =>
            {
                var s = SettingsIn(NewDir("snes-rescue"), false); var c = SsConsole("modified-noorig"); var src = Backup(c, s);
                var good0 = File.ReadAllBytes(SimulatedConsole.Dev(c, "boot0")); var good1 = File.ReadAllBytes(SimulatedConsole.Dev(c, "boot1"));
                Check(src.Ok && BootImage.Check(good1, false) == null && good1.Length == 573440 && BootImage.Check(good0, true) == null, "a Super NES Classic's u-boot shape (573,440 bytes, no nandinfo block) and boot0 are complete images to the same checks ccsboot makes");
                var bad1 = (byte[])good1.Clone(); bad1[70000] ^= 0x10; File.WriteAllBytes(SimulatedConsole.Dev(c, "boot1"), bad1);
                var bad0 = (byte[])good0.Clone(); bad0[3000] ^= 0x01; File.WriteAllBytes(SimulatedConsole.Dev(c, "boot0"), bad0);
                var con = RecoverySessions.OpenSimulated(c, ImageKind.Rescue); var ctx = Ctx(s, ImageKind.Rescue);
                var dg = RescueEngine.Diagnose(con); var ack = RescueEngine.AcknowledgmentFor(new[] { "boot0", "boot1" }, dg);
                Check(dg.Damaged("boot1") && dg.Damaged("boot0"), "both damaged stages are diagnosed (" + dg.Text + ")");
                // another model's backup with this identity (made up): never a source of startup stages
                var sn = SettingsIn(NewDir("snes-rescue-nes"), false); var nesc = Console_("modified-noorig", HwidA); var nesb = Backup(nesc, sn); GoneConsole(nesc);
                var (np, nr) = RescueEngine.Prepare(con, ctx, nesb.PrimaryPath, new[] { "boot0", "boot1" }, ack);
                Check(np == null && nr.Problem.Contains("is of NES Classic Edition, not of this console's model") && Writes(c).Count == 0, "an NES Classic backup with this console's identity never supplies its startup stages (" + nr?.Problem + ")");
                GoneBackups(sn);
                var other = SsConsole("modified-noorig", hwid: HwidB); var ob = Backup(other, SettingsIn(NewDir("snes-rescue-other"), false)); GoneConsole(other);
                var (op, or) = RescueEngine.Prepare(con, ctx, ob.PrimaryPath, new[] { "boot1" }, RescueEngine.AcknowledgmentFor(new[] { "boot1" }, dg));
                Check(op == null && or.Problem.Contains("different console") && Writes(c).Count == 0, "another Super NES Classic's backup: refused, no cross-console boot transplant (" + or?.Problem + ")");
                var cfgPath = Path.Combine(c, "console.json"); var cfg = File.ReadAllText(cfgPath); SetCfg(c, "region", "JPN");
                var (jp, jr) = RescueEngine.Prepare(RecoverySessions.OpenSimulated(c, ImageKind.Rescue), ctx, src.PrimaryPath, new[] { "boot1" }, RescueEngine.AcknowledgmentFor(new[] { "boot1" }, dg));
                Check(jp == null && jr.Problem.Contains("not a North American or European Super NES Classic Edition") && Writes(c).Count == 0, "the Super Famicom's region: refused (" + jr?.Problem + ")");
                File.WriteAllText(cfgPath, cfg);
                var (plan, refused) = RescueEngine.Prepare(con, ctx, src.PrimaryPath, new[] { "boot0", "boot1" }, ack);
                Check(refused == null && plan != null && plan.Steps.SequenceEqual(new[] { "stage1", "burn1", "stage0", "burn0" }) && plan.Stages.Single(x => x.Stage == "boot1").WantMd5 == Md5(good1) && plan.Stages.Single(x => x.Stage == "boot1").Size == 573440,
                      "the plan: this console's own u-boot first, then its boot0, both from its own verified backup (" + refused?.Problem + ")");
                Check(RescueEngine.Execute(RecoverySessions.OpenSimulated(c, ImageKind.Rescue), plan, plan.Digest, plan.Nonce, ctx).State == "refused" && Writes(c).Count == 0, "another connection: refused, nothing written");
                File.Move(src.PrimaryPath, src.PrimaryPath + ".withheld");
                var m1 = RescueEngine.Execute(con, plan, plan.Digest, plan.Nonce, ctx);
                Check(m1.State == "refused" && Writes(c).Count == 0 && plan.Nonce != null, "the source backup moved aside: refused, nothing written (" + m1.Problem + ")");
                File.Move(src.PrimaryPath + ".withheld", src.PrimaryPath);
                File.WriteAllText(Path.Combine(c, "faults.json"), "{\"rescue_readback_differs\":true}");
                var o1 = RescueEngine.Execute(con, plan, plan.Digest, plan.Nonce, ctx);
                Check(o1.State == "stopped" && Writes(c).SequenceEqual(new[] { "rescue burn1" }) && File.ReadAllBytes(SimulatedConsole.Dev(c, "boot0")).SequenceEqual(bad0) && Record.Load(o1.RecordFile)?.O("after_stop") != null,
                      "the u-boot reads back differently: stopped there, boot0 NOT written, nothing retried, the stages read again and recorded (" + o1.Problem + ")");
                File.Delete(Path.Combine(c, "faults.json")); File.Delete(Path.Combine(c, "writes.log"));
                File.WriteAllBytes(SimulatedConsole.Dev(c, "boot1"), bad1);
                // through the worker: the diagnosis, the choice, the plan, the confirmation
                var evs = SsDrive(SsReq("rescue", c), s, ev =>
                    ev.S("event") == "rescue-diagnosis" ? SsLine(new JObj { { "cmd", "rescue-choose" }, { "stages", new List<object> { "boot0", "boot1" } }, { "backup", src.PrimaryPath }, { "ack", ev.S("acknowledgment") } })
                    : ev.S("event") == "rescue-plan" ? SsLine(new JObj { { "cmd", "rescue-confirm" }, { "digest", ev.O("plan").S("digest") }, { "nonce", ev.O("plan").S("nonce") } }) : null);
                var res = SsResult(evs);
                Check(res?.S("state") == "rescued" && File.ReadAllBytes(SimulatedConsole.Dev(c, "boot1")).SequenceEqual(good1) && File.ReadAllBytes(SimulatedConsole.Dev(c, "boot0")).SequenceEqual(good0) && Writes(c).SequenceEqual(new[] { "rescue burn1", "rescue burn0" }),
                      "worker: rescued, this console's own u-boot then boot0 written once each and read back (" + res?.S("problem") + "; " + string.Join(", ", Writes(c)) + ")");
                GoneConsole(c); GoneBackups(s);
            });

            // ------------------------------------------------------------------ Developer Mode
            Part("Developer Mode on a Super NES Classic: restore point, startup software, system part, test boot", () =>
            {
                var dir = NewDir("snes-developer"); var s = SettingsIn(dir, false);
                var c = SsConsole("stock");
                var A = Squash(dir, "snes-system-a", "gzip", "dp-shvc"); var B = Squash(dir, "snes-system-b", "gzip", "dp-shvc"); var N = Squash(dir, "nes-system", "gzip", "dp-nes");
                var sysDev = SimulatedConsole.Dev(c, "nandb");
                using (var f = new FileStream(sysDev, FileMode.Open, FileAccess.Write)) { var e = SimulatedConsole.SimulatedSystemCipher(A); f.Write(e, 0, e.Length); }
                var rawA = File.ReadAllBytes(sysDev);
                var a = Backup(c, s);
                var con = RecoverySessions.OpenSimulated(c, ImageKind.Files); var ctx = Ctx(s, ImageKind.Files);
                var (rp, rpw) = RestorePoints.Establish(con, a.PrimaryPath, null, "developer");
                Check(rp != null && new[] { RestorePoints.Kernel, RestorePoints.SystemPart, RestorePoints.UserFiles, RestorePoints.BootStages, RestorePoints.Layout }.All(rp.Covers.Contains),
                      "Developer Mode's restore point: this Super NES Classic's own verified backup, covering its startup software, system part, files, startup stages and layout (" + rpw + ")");
                var filesOn = Capabilities.Allowed("capability", "snes-classic-files");
                var (xp, xw) = RestorePoints.Establish(con, a.PrimaryPath, null, "files");
                Check(filesOn ? xp != null : xp == null && (xw ?? "").Contains("Expert Mode"), "Expert Mode's restore point follows its own capability (snes-classic-files " + (filesOn ? "supported" : "reserved") + "), never Developer Mode's (" + xw + ")");
                var sn = SettingsIn(NewDir("snes-developer-nes"), false); var nesc = Console_("modified-noorig", HwidA); var nesb = Backup(nesc, sn); GoneConsole(nesc);
                var (np, nw) = RestorePoints.Establish(con, nesb.PrimaryPath, null, "developer");
                Check(np == null && nw.Contains("is of NES Classic Edition, not of this console's model"), "an NES Classic backup with this console's identity is never its restore point (" + nw + ")");
                GoneBackups(sn);
                // the startup software
                var img = DevImage("root=/dev/nandb decrypt ro console=ttyS0,115200 dev-test=1", 300000, 21, Ramdisk(("key-file", "K"), ("init", "#!/bin/sh\n")));
                var (kp, kw) = DeveloperEngine.PlanKernel(con, rp, img, null);
                Check(kp != null && kp.NowMd5 == SsUsa14 && kp.CmdlineDifferences.Contains("added: dev-test=1") && kp.Dependencies.Any(d => d.Contains("key file in its own ramdisk")),
                      "a startup software plan against this console's own North American kernel: its differences and key dependency stated (" + kw + ")");
                Check(DeveloperEngine.FlashKernel(RecoverySessions.OpenSimulated(c, ImageKind.Files), kp, kp.Digest, kp.Nonce, ctx).State == "refused" && Md5(File.ReadAllBytes(SimulatedConsole.Dev(c, "boot2"))) == SsUsa14, "another connection cannot use the plan; nothing written");
                var ko = DeveloperEngine.FlashKernel(con, kp, kp.Digest, kp.Nonce, ctx);
                Check(ko.Ok && File.ReadAllBytes(SimulatedConsole.Dev(c, "boot2")).SequenceEqual(img), "the startup software written once and read back (" + ko.Problem + ")");
                File.WriteAllText(Path.Combine(c, "faults.json"), "{\"dev_readback_differs\":true}");
                var (kp2, _) = DeveloperEngine.PlanKernel(con, rp, DevImage("root=/dev/nandb ro", 200000, 22), null);
                var ko2 = DeveloperEngine.FlashKernel(con, kp2, kp2.Digest, kp2.Nonce, ctx);
                Check(ko2.State == "stopped" && ko2.Problem.Contains("Console Restore with your restore point"), "a startup write that reads back differently stops, is not written again, and points to the way back (" + ko2.Problem + ")");
                File.Delete(Path.Combine(c, "faults.json"));
                File.WriteAllBytes(SimulatedConsole.Dev(c, "boot2"), SnesStockKernel());   // its own startup software again (the system plan reads it)
                // the system part: a Super NES Classic system image (board dp-shvc) through the console's own encryption; an NES Classic's refused
                var (sp, sw) = DeveloperSystem.Plan(con, rp, B, "b.hsqs", null);
                Check(sp != null && sp.Board == "dp-shvc" && sp.Encryption == "crypt", "a system image of this model (board dp-shvc) is planned through the console's own encryption (" + sw + ")");
                var (bp, bw) = DeveloperSystem.Plan(con, rp, N, "nes.hsqs", null);
                Check(bp == null && bw.Contains("for another console"), "an NES Classic system image (board dp-nes) is refused as this console's system part (" + bw + ")");
                var so = sp == null ? null : DeveloperSystem.Flash(con, sp, sp.Digest, sp.Nonce, ctx);
                Check(so != null && so.Ok && SimulatedConsole.SimulatedSystemCipher(File.ReadAllBytes(sysDev).Take(B.Length).ToArray()).SequenceEqual(B), "the system part written once through the console's encryption and read back (" + so?.Problem + ")");
                // the test boot: this console's own genuine startup software from its restore point (the console known only by its hardware id)
                var devBefore = DevHashes(c);
                using (var t = DeveloperTestBoot.OpenSimulated(c))
                {
                    var (tp, tw) = TestBootEngine.Point(t, a.PrimaryPath, null);
                    var (own, ow) = tp == null ? (null, tw) : TestBootEngine.Plan(t, tp, TestBootEngine.OwnStartup, null, null, null);
                    Check(own != null && own.Facts.RamdiskFormat == "lzo" && own.Facts.KeyInRamdisk == true && own.Md5 == SsUsa14 && own.Cmdline.Contains("root=/dev/nandb"),
                          "the Super NES Classic's own stock startup software fits the console's startup layout: its LZO ramdisk holds the console's key file, root=/dev/nandb (" + ow + ")");
                    var go = own == null ? null : TestBootEngine.Execute(t, own, own.Digest, own.Nonce, Ctx(s));
                    Check(go != null && go.Ok && File.ReadAllBytes(Path.Combine(c, "ram-test-boot.bin")).SequenceEqual(own.Image) && DevHashes(c).OrderBy(x => x.Key).SequenceEqual(devBefore.OrderBy(x => x.Key)),
                          "sent once to the console's memory; nothing written to its storage (" + go?.Problem + ")");
                    File.Delete(Path.Combine(c, "ram-test-boot.bin"));
                }
                // through the worker: a Developer Mode session's restore point, and the test boot
                var evs = SsDrive(SsReq("files", c, new JObj { { "scope", "dev" } }), s, ev => ev.S("event") == "files-ready" ? SsLine(new JObj { { "cmd", "restore-point" }, { "backup", a.PrimaryPath } }) : ev.S("event") == "restore-point" ? SsLine(new JObj { { "cmd", "end" } }) : null);
                var rpe = evs.FirstOrDefault(e => e.S("event") == "restore-point");
                Check(rpe != null && rpe.B("ok") && SsResult(evs)?.S("state") == "ended", "worker: a Developer Mode session on a Super NES Classic takes its restore point (" + rpe?.S("problem") + ")");
                var evu = SsDrive(SsReq("files", c, new JObj { { "scope", "user" } }), s, ev => ev.S("event") == "files-ready" ? SsLine(new JObj { { "cmd", "restore-point" }, { "backup", a.PrimaryPath } }) : ev.S("event") == "restore-point" ? SsLine(new JObj { { "cmd", "end" } }) : null);
                var rpu = evu.FirstOrDefault(e => e.S("event") == "restore-point");
                Check(rpu != null && rpu.B("ok") == filesOn, "worker: an Expert Mode session's restore point follows Expert Mode's own capability (" + (rpu?.S("problem") ?? "taken") + ")");
                var evt = SsDrive(SsReq("test-boot", c, new JObj { { "class", TestBootEngine.OwnStartup }, { "backup", a.PrimaryPath } }), s, ev => ev.S("event") == "test-boot-plan" ? SsLine(new JObj { { "cmd", "test-boot-confirm" }, { "digest", ev.O("plan").S("digest") }, { "nonce", ev.O("plan").S("nonce") } }) : null);
                Check(SsResult(evt)?.S("state") == "sent" && File.Exists(Path.Combine(c, "ram-test-boot.bin")), "worker: the test boot of this console's own startup software is planned, confirmed and sent (" + SsResult(evt)?.S("problem") + ")");
                // the way back: Console Restore with the restore point puts the system part back
                var rcon = RecoverySessions.OpenSimulated(c, ImageKind.Restore); var rctx = Ctx(s, ImageKind.Restore);
                var (rplan, rref) = RestoreEngine.Prepare(rcon, a.PrimaryPath, rctx);
                var ro = rplan == null || !rplan.Writable ? null : RestoreEngine.Execute(rcon, rplan, rplan.Digest, rplan.Nonce, rctx);
                Check(ro != null && ro.State == "restored" && File.ReadAllBytes(sysDev).SequenceEqual(rawA), "the way back: Console Restore with the restore point brings the system part back exactly (" + (rref?.Problem ?? ro?.Problem) + ")");
                GoneConsole(c); GoneBackups(s);
            });
        }
    }
}
