// Classic Cart Studio console backup and restore engine tests: CCS Mod and CCS Nucleus through hakchi's transfer folder (the preparation's
// install method, and Console Restore's removal and change). GPL-3.0-or-later. Simulated consoles only (files on this PC); the real-console
// boundaries (Gate.PreparationWrites, Gate.MethodWrites) stay closed. SimulatedConsole.NormalStart stands in for the console's next normal
// start (hakchi's p7080_hmods); tools/console-nucleus/test_nucleus.py runs hakchi's own p7080 with the real packages under qemu-arm.
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        static string Transfer(string c) => Path.Combine(c, "hakchi-transfer");
        static List<string> Waiting(string c) => Directory.Exists(Transfer(c)) ? Directory.GetFiles(Transfer(c)).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToList() : new List<string>();
        static (List<string> packages, string policy) Installed(string c)
        {
            var p = Path.Combine(c, "hakchi-packages.json"); if (!File.Exists(p)) return (new List<string>(), "none");
            var j = Json.Parse(File.ReadAllText(p)); return ((j.A("packages") ?? new List<object>()).OfType<string>().ToList(), j.S("policy") ?? "none");
        }
        /// <summary>disk: each simulated console and each capture of it is about 1 GB; what a check no longer needs is deleted at once</summary>
        static void Gone(params string[] files) { foreach (var f in files) if (f != null && File.Exists(f)) File.Delete(f); }
        static void GoneConsole(string c) { try { Directory.Delete(Path.GetDirectoryName(c), true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        static void GoneBackups(Settings s) { foreach (var d in new[] { s.PrimaryFolder, s.ArchiveFolder }) if (d != null && Directory.Exists(d)) foreach (var f in Directory.GetFiles(d)) File.Delete(f); }
        /// <summary>a smaller simulated console (its data storage the 64 MB "modern" fixture), for the cases that do not read hakchi's files
        /// from a backup; hakchi: the preparation's marker of hakchi's files on its data storage (what ccsprep mstate reports)</summary>
        static string SmallConsole(string kind, bool hakchi)
        {
            var lay = Layout.Select(x => x.dev == "nandc" ? (x.dev, x.label, 65536L * 1024) : x).ToArray();
            var c = Console_(kind, HwidA, Path.Combine(Fx, "data-modern.img"), lay);
            if (hakchi) File.WriteAllText(Path.Combine(c, "prepared-files.txt"), "hakchi v1.0.4-126 files installed (simulated)\n");
            return c;
        }
        static void SetInstalled(string c, string policy, params string[] packages) => File.WriteAllText(Path.Combine(c, "hakchi-packages.json"), Json.Write(new JObj { { "packages", packages.Cast<object>().ToList() }, { "policy", policy } }));

        /// <summary>home: the CCS HOME package the method gets (null: the release-built one this build carries)</summary>
        static (PrepPlan plan, PrepOutcome refused, RecoveryConsole con, EngineContext ctx, AdmissionResult adm) PrepPlanWith(string console, Settings s, string policy, HomePackage home = null, bool modStart = false)
        {
            var con = RecoverySessions.OpenSimulated(console, ImageKind.Prepare); var ctx = Ctx(s, ImageKind.Prepare);
            var id = con.Identify();
            var adm = Admission.Admit(s, id.Hwid, "prepare", Subj, null, true, con.SessionId);
            if (!adm.Ok) return (null, new PrepOutcome { State = "refused", Problem = adm.Code + ": " + adm.Text }, con, ctx, adm);
            var (p, r) = Preparation.Prepare(con, ctx, adm, policy, home, modStart);
            return (p, r, con, ctx, adm);
        }
        static string HomeIn(string c) => File.Exists(Path.Combine(c, "ccs-data", "pkg", "current")) ? File.ReadAllText(Path.Combine(c, "ccs-data", "pkg", "current")).Trim() : null;

        /// <summary>the method added to a console already prepared with the pinned hakchi (Preparation reads hakchi's files from the backup
        /// made in the session, so this case takes the full-size "modified" data fixture: about 3 GB of disk while it runs)</summary>
        public static void MethodAlready()
        {
            var s = SettingsIn(NewDir("method-already"), false);
            // ---- a console already prepared with the pinned hakchi: only the method's package (no kernel, bootloader or system write)
            var c3 = Console_("modified"); Backup(c3, s);
            var (p3, r3, con3, ctx3, adm3) = PrepPlanWith(c3, s, "nucleus");
            Check(p3 != null && p3.State == "already" && p3.Steps.SequenceEqual(new[] { "method", "hput", "huse" }) && p3.Text.Contains("without changing the kernel, bootloader or system") && p3.Home != null && p3.Home.Layout == "nucleus" && p3.HomePrior == "absent",
                "already prepared, CCS Nucleus chosen: its package, then the release-built CCS HOME package in CCS Nucleus's layout (sent, then switched to); no kernel, bootloader or system step (" + (p3?.Text ?? r3?.Problem) + ")");
            // the console's hakchi state changes after the review (something else placed in its transfer folder): refused, nothing written
            Directory.CreateDirectory(Transfer(c3)); File.WriteAllText(Path.Combine(Transfer(c3), "other.hmod"), "another program's package\n");
            var m3 = Preparation.Execute(con3, p3, p3.Digest, p3.Nonce, ctx3, () => UsePrep(s, adm3, con3, p3));
            Check(m3.State == "refused" && m3.Problem.Contains("changed since the review") && !File.Exists(Path.Combine(c3, "writes.log")) && p3.Nonce != null && !File.Exists(Path.Combine(s.RecordsFolder, "admission-" + adm3.TicketId + ".used")),
                "the transfer folder changed after the review: refused before anything, neither the confirmation nor the admission used (" + m3.Problem + ")");
            Gone(p3.CurrentBackup);
            var (pW, _, _, _, _) = PrepPlanWith(c3, s, "nucleus"); Gone(pW?.CurrentBackup);
            Check(pW != null && pW.Refusal != null && pW.Refusal.Contains("other.hmod") && pW.Steps.Count == 0, "with another package waiting there for the next start, installing a method is refused, with the reason (" + pW?.Refusal + ")");
            Directory.Delete(Transfer(c3), true);
            (p3, r3, con3, ctx3, adm3) = PrepPlanWith(c3, s, "nucleus");
            var o3 = Preparation.Execute(con3, p3, p3.Digest, p3.Nonce, ctx3, () => UsePrep(s, adm3, con3, p3));
            Check(o3.State == "method-scheduled" && Writes(c3).SequenceEqual(new[] { "method ccs-nucleus", "hput " + p3.Home.Copy, "huse " + p3.Home.Copy }) && Waiting(c3).SequenceEqual(new[] { "ccs-nucleus.hmod" }) && Record.Load(o3.RecordFile).S("state") == "method-scheduled"
                && HomeIn(c3) == p3.Home.Copy && Record.Load(o3.RecordFile).O("after")?.O("home")?.S("copy") == p3.Home.Copy,
                "only CCS Nucleus's package and CCS HOME's package were written (no kernel, bootloader or table); CCS HOME is in use at once, the method waits for the next start; the record is not a preparation record (" + o3.Problem + "; " + string.Join(", ", Writes(c3)) + ")");
            Gone(p3.CurrentBackup);
            SimulatedConsole.NormalStart(c3);
            Check(Installed(c3).packages.SequenceEqual(new[] { "ccs-nucleus" }) && Installed(c3).policy == "nucleus", "the next normal start installs CCS Nucleus, which is then in charge");
            var (p3b, _, _, _, _) = PrepPlanWith(c3, s, "nucleus"); Gone(p3b?.CurrentBackup);
            Check(p3b != null && p3b.State == "already" && p3b.Steps.Count == 0 && p3b.Text.Contains("is installed too") && p3b.Notes.Any(n => n.Contains("already this package")), "preparing again with the same method and the same HOME: nothing to do");
            // the HOME package removed from the console by someone (only its pointer): the method stays, only CCS HOME is placed again
            File.Delete(Path.Combine(c3, "ccs-data", "pkg", "current"));
            var (p3h, r3h, con3h, ctx3h, adm3h) = PrepPlanWith(c3, s, "nucleus");
            var o3h = p3h == null ? null : Preparation.Execute(con3h, p3h, p3h.Digest, p3h.Nonce, ctx3h, () => UsePrep(s, adm3h, con3h, p3h)); Gone(p3h?.CurrentBackup);
            Check(p3h != null && p3h.Steps.SequenceEqual(new[] { "hput", "huse" }) && p3h.Text.Contains("only CCS HOME's package") && o3h.State == "home-placed" && HomeIn(c3) != null && Record.Load(o3h.RecordFile).S("state") == "home-placed",
                "CCS Nucleus installed but no HOME in use: only CCS HOME's package is placed (the copy already on the console is sent again under a new attempt, never written over) (" + (p3h?.Text ?? r3h?.Problem) + "; " + o3h?.State + ")");
            var (p3c, _, _, _, _) = PrepPlanWith(c3, s, "mod"); Gone(p3c?.CurrentBackup);
            Check(p3c != null && p3c.Refusal != null && p3c.Refusal.Contains("Console Restore") && p3c.Steps.Count == 0, "CCS Nucleus is installed and CCS Mod chosen: refused here; changing between them is Console Restore's (" + p3c?.Refusal + ")");
            GoneConsole(c3); GoneBackups(s);

        }

        public static void Method()
        {
            var modSha = Methods.Sha256("ccs-mod"); var nucSha = Methods.Sha256("ccs-nucleus");
            Check(Hex.IsHex(modSha, 64) && Hex.IsHex(nucSha, 64) && modSha != nucSha, "the preparation image carries both install-method packages, pinned (its MANIFEST.json: ccs-mod " + modSha?.Substring(0, 12) + ", ccs-nucleus " + nucSha?.Substring(0, 12) + ")");
            var dir = NewDir("method"); var s = SettingsIn(dir, false);

            // ---- the preparation installs the chosen method: a stock console, CCS Mod
            var c = Console_("stock"); Backup(c, s);   // (the 413 MB stock data fixture: the small one carries hakchi's files)
            var (sp, sr) = MethodEngine.Prepare(RecoverySessions.OpenSimulated(c, ImageKind.Method), Ctx(s, ImageKind.Method), "remove", "mod");
            Check(sp == null && sr.Problem.Contains("Hakchi2 CE 3.9.3") && !File.Exists(Path.Combine(c, "writes.log")), "Console Restore's method change on a console without hakchi: refused before anything (" + sr?.Problem + ")");
            var (bad, badR, _, _, _) = PrepPlanWith(c, s, "something-else");
            Check(bad == null && badR.State == "refused" && !File.Exists(Path.Combine(c, "writes.log")), "an unknown install method is refused before anything (" + badR?.Problem + ")");
            var homeMod = HomePackage.Folder(MakeHome("mod", "m1"), "mod");
            var (p, r, con, ctx, adm) = PrepPlanWith(c, s, "mod", homeMod);
            Check(p != null && p.State == "original" && p.Steps.SequenceEqual(new[] { "files", "uboot", "slot68", "method", "hput", "huse", "kernel" }) && p.Package == "ccs-mod" && p.PackageSha256 == modSha && p.MethodBefore != null && p.MethodBefore.Hakchi == "none"
                && p.Home != null && p.Home.ManifestSha256 == homeMod.ManifestSha256 && p.HomePrior == "absent",
                "stock console, CCS Mod chosen: hakchi's four steps with CCS Mod's package and CCS HOME's package (sent, then switched to) before the kernel (the kernel stays last) (" + string.Join(",", p?.Steps ?? new List<string>()) + "; " + r?.Problem + ")");
            Check(Json.Parse(Json.Write(p.ToJson())).O("home")?.S("archive_sha256") == p.Home.Sha256 && p.Notes.Any(n => n.Contains("CCS HOME") && n.Contains("checked file by file")), "the plan names CCS HOME's package by its archive's SHA-256 and says how it is placed");
            var hd = p.Digest; var hs = p.Home; p.Home = HomeArchive.Build(HomePackage.Folder(MakeHome("mod", "other"), "mod"), hs.Attempt, HwidA, "absent");
            Check(Preparation.Execute(con, p, hd, p.Nonce, ctx).State == "refused" && !File.Exists(Path.Combine(c, "writes.log")), "CCS HOME's archive is part of the plan's digest: another package after the review is refused, nothing written");
            p.Home = hs;
            Check(p.Notes.Any(n => n.Contains("transfer folder") && n.Contains("next normal start")) && Json.Parse(Json.Write(p.ToJson())).O("package")?.S("sha256") == modSha && Json.Parse(Json.Write(p.ToJson())).S("policy") == "mod", "the plan says where the package goes and when hakchi installs it, and names the package by its SHA-256");
            var pDigest = p.Digest; var pol = p.Policy; p.Policy = "nucleus";
            Check(Preparation.Execute(con, p, pDigest, p.Nonce, ctx).State == "refused" && !File.Exists(Path.Combine(c, "writes.log")), "the method is part of the plan's digest: a plan changed after the review is refused, nothing written");
            p.Policy = pol;
            AdmissionResult use = null;
            var o = Preparation.Execute(con, p, p.Digest, p.Nonce, ctx, () => use = UsePrep(s, adm, con, p));
            Check(use.Ok && o.State == "prepared" && Writes(c).SequenceEqual(new[] { "prep files", "prep uboot", "prep slot68", "method ccs-mod", "hput " + p.Home.Copy, "huse " + p.Home.Copy, "prep kernel" }), "prepared: every step once, in order, CCS Mod's package and CCS HOME's package placed before the kernel (" + o.Problem + "; " + string.Join(", ", Writes(c)) + ")");
            Check(Waiting(c).SequenceEqual(new[] { "ccs-mod.hmod" }) && Record.Load(o.RecordFile).O("after")?.O("method")?.S("sha256") == modSha && Record.Load(o.RecordFile).S("state") == "prepared" && HomeIn(c) == p.Home.Copy
                && Record.Load(o.RecordFile).O("after")?.O("home")?.S("manifest_sha256") == homeMod.ManifestSha256,
                "CCS Mod's package waits in hakchi's transfer folder; CCS HOME's package is the one in use; the preparation record names both");
            var started = SimulatedConsole.NormalStart(c);
            Check(started.SequenceEqual(new[] { "install ccs-mod", "transfer folder removed; restart" }) && Installed(c).packages.SequenceEqual(new[] { "ccs-mod" }) && Installed(c).policy == "mod" && Waiting(c).Count == 0,
                "the next normal start: hakchi installs CCS Mod, which is then in charge (" + string.Join("; ", started) + ")");
            Check(Record.Load(o.RecordFile).S("state") == "prepared" && Record.Load(o.RecordFile).O("before")?.S("snapshot") == p.CurrentBackup, "the preparation record keeps its state and the pre-preparation snapshot, which Return to Original Software looks for");
            GoneConsole(c); GoneBackups(s); s = SettingsIn(NewDir("method-2"), false);

            // a failure of the method step stops before the kernel: the console keeps starting its original system
            var c2 = Console_("stock"); Backup(c2, s);
            File.WriteAllText(Path.Combine(c2, "faults.json"), "{\"prep\":{\"step\":\"method\"}}");
            var (p2, _, con2, ctx2, adm2) = PrepPlanWith(c2, s, "nucleus", HomePackage.Folder(MakeHome("nucleus", "n1"), "nucleus"));
            var o2 = Preparation.Execute(con2, p2, p2.Digest, p2.Nonce, ctx2, () => UsePrep(s, adm2, con2, p2));
            Check(o2.State == "stopped" && o2.Done.SequenceEqual(new[] { "files", "uboot", "slot68" }) && Writes(c2).SequenceEqual(new[] { "prep files", "prep uboot", "prep slot68" }) && File.ReadAllBytes(SimulatedConsole.Dev(c2, "boot2")).SequenceEqual(StockKernel()),
                "the package step fails: stopped there; the kernel is NOT written, so the console still starts its original system; nothing retried (" + o2.Problem + ")");
            GoneConsole(c2); GoneBackups(s); s = SettingsIn(NewDir("method-3"), false);
            // CCS HOME's transfer fails: stopped before the kernel too; the method's package waits, no HOME is switched to
            var c4 = Console_("stock"); Backup(c4, s);
            File.WriteAllText(Path.Combine(c4, "faults.json"), "{\"prep\":{\"step\":\"hput\"}}");
            var (p4, _, con4, ctx4, adm4) = PrepPlanWith(c4, s, "mod", HomePackage.Folder(MakeHome("mod", "m4", 512), "mod"));
            var o4 = Preparation.Execute(con4, p4, p4.Digest, p4.Nonce, ctx4, () => UsePrep(s, adm4, con4, p4));
            Check(o4.State == "stopped" && o4.Done.SequenceEqual(new[] { "files", "uboot", "slot68", "method" }) && Writes(c4).Count == 5 && Writes(c4)[4] == "hput " + p4.Home.Copy + " INTERRUPTED" && HomeIn(c4) == null && File.ReadAllBytes(SimulatedConsole.Dev(c4, "boot2")).SequenceEqual(StockKernel()) && o4.Problem.Contains("finishes this installation"),
                "CCS HOME's transfer fails: stopped there; no HOME is switched to and the kernel is NOT written; the result says how it is finished (" + o4.Problem + ")");
            GoneConsole(c4); GoneBackups(s); s = SettingsIn(NewDir("method-4"), false);

            // ---- Console Restore: remove a method, change from one to the other (MethodEngine)
            var sM = SettingsIn(NewDir("method-restore"), false);
            var cm = SmallConsole("modified", true); SetInstalled(cm, "mod", "ccs-mod");
            var (np, nr) = MethodEngine.Prepare(RecoverySessions.OpenSimulated(cm, ImageKind.Method), Ctx(sM, ImageKind.Method), "remove", "nucleus");
            Check(np == null && nr.State == "refused" && nr.Problem.Contains("not installed") && Writes(cm).Count == 0, "removing a method that is not installed: refused (" + nr?.Problem + ")");
            var (xp, xr) = MethodEngine.Prepare(RecoverySessions.OpenSimulated(cm, ImageKind.Method), Ctx(sM, ImageKind.Method), "delete", "mod");
            Check(xp == null && xr.State == "refused", "an unknown operation: refused");
            var mcon = RecoverySessions.OpenSimulated(cm, ImageKind.Method); var mctx = Ctx(sM, ImageKind.Method);
            var (mp, mr) = MethodEngine.Prepare(mcon, mctx, "remove", "mod");
            var mpv = mp == null ? null : Verifier.VerifyFile(mp.ProtectivePath);
            Check(mr == null && mp.Steps.SequenceEqual(new[] { "unlist" }) && mp.ListPackage == "ccs-mod" && mpv != null && mpv.Ok && mp.ProtectiveArchive.Session == mcon.SessionId && mp.Before.Installed("ccs-mod"),
                "remove CCS Mod: one step (hakchi's removal list), after a complete backup made in this same session (" + mr?.Problem + ")");
            Check(MethodEngine.Execute(mcon, mp, Other(mp.Digest), mp.Nonce, mctx).State == "refused" && Writes(cm).Count == 0, "a confirmation for another plan: refused, nothing written");
            Check(MethodEngine.Execute(RecoverySessions.OpenSimulated(cm, ImageKind.Method), mp, mp.Digest, mp.Nonce, mctx).State == "refused" && Writes(cm).Count == 0, "the console connected again after the review: refused, nothing written");
            var prot = mp.ProtectivePath; var nonce = mp.Nonce;
            File.Move(prot, prot + ".withheld");
            var mm = MethodEngine.Execute(mcon, mp, mp.Digest, nonce, mctx);
            Check(mm.State == "refused" && mm.Problem.Contains("missing") && Writes(cm).Count == 0 && mp.Nonce == nonce && mm.RecordFile == null, "the backup made for the change moved aside: refused, nothing written, no record, the confirmation not used (" + mm.Problem + ")");
            File.Move(prot + ".withheld", prot);
            SetInstalled(cm, "mod", "ccs-mod", "other-package");
            var mc = MethodEngine.Execute(mcon, mp, mp.Digest, nonce, mctx);
            Check(mc.State == "refused" && mc.Problem.Contains("changed since the review") && Writes(cm).Count == 0 && mp.Nonce == nonce, "the console's hakchi packages changed after the review: refused, nothing written (" + mc.Problem + ")");
            SetInstalled(cm, "mod", "ccs-mod");
            var mo = MethodEngine.Execute(mcon, mp, mp.Digest, nonce, mctx);
            var mrec = Record.Load(mo.RecordFile);
            Check(mo.State == "scheduled" && Writes(cm).SequenceEqual(new[] { "unlist ccs-mod" }) && Waiting(cm).SequenceEqual(new[] { "uninstall" }) && File.ReadAllText(Path.Combine(Transfer(cm), "uninstall")) == "ccs-mod\n" && mrec.S("state") == "scheduled" && mrec.O("plan") != null,
                "removal scheduled: hakchi's removal list naming ccs-mod waits for the next start; the record (saved before the write) shows it (" + mo.Problem + ")");
            Check(MethodEngine.Execute(mcon, mp, mp.Digest, mp.Nonce, mctx).State == "refused", "the same confirmation cannot run twice");
            var st2 = SimulatedConsole.NormalStart(cm);
            Check(st2.SequenceEqual(new[] { "uninstall ccs-mod", "transfer folder removed; restart" }) && Installed(cm).packages.Count == 0 && Installed(cm).policy == "none", "the next normal start: hakchi runs CCS Mod's own removal; no method is in charge (" + string.Join("; ", st2) + ")");
            GoneConsole(cm); GoneBackups(sM);

            // change: CCS Mod -> CCS Nucleus, interrupted between its two writes, then finished by a new connection (never written twice)
            var cx = SmallConsole("modified", true); SetInstalled(cx, "mod", "ccs-mod");
            File.WriteAllText(Path.Combine(cx, "faults.json"), "{\"prep\":{\"step\":\"unlist\"}}");
            var xcon = RecoverySessions.OpenSimulated(cx, ImageKind.Method); var xctx = Ctx(sM, ImageKind.Method);
            var homeNuc = HomePackage.Folder(MakeHome("nucleus", "x1"), "nucleus");
            var (cp, cr) = MethodEngine.Prepare(xcon, xctx, "change", "nucleus", homeNuc);
            Check(cr == null && cp.Steps.SequenceEqual(new[] { "method", "unlist", "hput", "huse" }) && cp.Package == "ccs-nucleus" && cp.PackageSha256 == nucSha && cp.ListPackage == "ccs-mod" && cp.Home.Layout == "nucleus",
                "change to CCS Nucleus: its package first, then the removal list for CCS Mod (an interruption between them leaves both installed, the old one in charge; never neither), then CCS HOME in CCS Nucleus's layout (" + cr?.Problem + ")");
            var co = MethodEngine.Execute(xcon, cp, cp.Digest, cp.Nonce, xctx);
            Check(co.State == "stopped" && co.Done.SequenceEqual(new[] { "method" }) && Waiting(cx).SequenceEqual(new[] { "ccs-nucleus.hmod" }) && Record.Load(co.RecordFile).S("state") == "stopped", "the removal list's write fails: stopped there, nothing retried; the package already written waits, recorded (" + co.Problem + ")");
            File.Delete(Path.Combine(cx, "faults.json"));
            Gone(cp.ProtectivePath);
            var ycon = RecoverySessions.OpenSimulated(cx, ImageKind.Method);
            var (cp2, cr2) = MethodEngine.Prepare(ycon, xctx, "change", "nucleus", homeNuc);
            Check(cr2 == null && cp2 != null && cp2.Before.Waiting.ContainsKey("ccs-nucleus.hmod"), "a new connection: the package waiting there is this change's own, so the change can be finished (" + cr2?.Problem + ")");
            var co2 = MethodEngine.Execute(ycon, cp2, cp2.Digest, cp2.Nonce, xctx);
            var phases = Record.Load(co2.RecordFile).A("phases").OfType<Dictionary<string, object>>().ToList();
            Check(co2.State == "scheduled" && Writes(cx).SequenceEqual(new[] { "method ccs-nucleus", "unlist ccs-mod", "hput " + cp2.Home.Copy, "huse " + cp2.Home.Copy }) && phases.Any(x => x.S("step") == "method" && x.S("state") == "already-waiting") && Waiting(cx).SequenceEqual(new[] { "ccs-nucleus.hmod", "uninstall" })
                && HomeIn(cx) == cp2.Home.Copy && Record.Load(co2.RecordFile).O("after")?.O("home")?.S("layout") == "nucleus",
                "finished: the package is not written again (it already waited), the removal list is written once, CCS HOME in CCS Nucleus's layout is in use (" + co2.Problem + "; " + string.Join(", ", Writes(cx)) + ")");
            Gone(cp2.ProtectivePath);
            var st3 = SimulatedConsole.NormalStart(cx);
            Check(st3.SequenceEqual(new[] { "uninstall ccs-mod", "install ccs-nucleus", "transfer folder removed; restart" }) && Installed(cx).packages.SequenceEqual(new[] { "ccs-nucleus" }) && Installed(cx).policy == "nucleus",
                "the next normal start: hakchi removes CCS Mod first, then installs CCS Nucleus, which is in charge (" + string.Join("; ", st3) + ")");
            // and back: CCS Nucleus -> CCS Mod
            var bcon = RecoverySessions.OpenSimulated(cx, ImageKind.Method);
            var nucCopy = HomeIn(cx);
            var (bp, br) = MethodEngine.Prepare(bcon, xctx, "change", "mod", HomePackage.Folder(MakeHome("mod", "x2"), "mod"));
            var bo = br != null ? null : MethodEngine.Execute(bcon, bp, bp.Digest, bp.Nonce, xctx);
            SimulatedConsole.NormalStart(cx); Gone(bp?.ProtectivePath);
            Check(bo != null && bo.Ok && Installed(cx).packages.SequenceEqual(new[] { "ccs-mod" }) && Installed(cx).policy == "mod" && HomeIn(cx) == bp.Home.Copy && bp.HomePrior == nucCopy && Directory.Exists(Path.Combine(cx, "ccs-data", "pkg", nucCopy)),
                "change back to CCS Mod through the same route, CCS HOME in CCS Mod's layout switched to; the earlier copy is kept (" + (br?.Problem ?? bo?.Problem) + ")");
            var (bp2, br2) = MethodEngine.Prepare(RecoverySessions.OpenSimulated(cx, ImageKind.Method), xctx, "change", "mod");
            Check(bp2 == null && br2.Problem.Contains("already installed"), "changing to the method already installed: refused (" + br2?.Problem + ")");
            // something else waiting in the transfer folder: refused (it is not this change's own)
            Directory.CreateDirectory(Transfer(cx)); File.WriteAllText(Path.Combine(Transfer(cx), "uninstall"), "someone-else\n");
            var (wp, wr) = MethodEngine.Prepare(RecoverySessions.OpenSimulated(cx, ImageKind.Method), xctx, "remove", "mod");
            Check(wp == null && wr.Problem.Contains("already waits") && wr.Problem.Contains("uninstall"), "another removal list already waiting for the next start: refused (" + wr?.Problem + ")");
            Directory.Delete(Transfer(cx), true);
            GoneConsole(cx); GoneBackups(sM);

            // the real console stays behind its own closed boundary
            try { RecoverySessions.OpenReal(ImageKind.Method, null, () => false); Check(false, "a real method session opened"); }
            catch (GateClosedException ex) { Check(!CcsBackup.Gate.MethodWrites && !CcsBackup.Gate.PreparationWrites && ex.Message.Contains("not available"), "a real console: closed in code, before any USB activity (" + ex.Message + ")"); }
            var real = new WriteAuthority("x", HwidA, new[] { "unlist" }, () => CcsBackup.Gate.MethodWrites, CcsBackup.Gate.MethodText);
            try { real.Require("unlist", HwidA); Check(false, "a real method write was admitted"); }
            catch (GateClosedException) { Check(true, "a method write on a real console is refused by its boundary"); }
        }
    }
}
