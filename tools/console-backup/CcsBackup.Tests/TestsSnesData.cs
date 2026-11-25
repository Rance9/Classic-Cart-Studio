// Classic Cart Studio console backup and restore tests. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The Super NES Classic's data-level changes (group "snes-data"), on simulated consoles of its own layout (board dp-shvc, REGION USA or EUR):
// Expert Mode's file changes (its saves written exactly as chosen, never through the NES Classic's converter), Console Composer and private
// restore packages (only between consoles of this model and, from one console to another, of the same region), CCS Mod and CCS Nucleus
// through hakchi's package folder, HOME designs, an owner's system image as a CCS Nucleus choice in both directions (a Super NES Classic
// system on an NES Classic, an NES Classic system on a Super NES Classic), community mods for this model only, and the Sega Mini menu
// pictures clean-up. Each refuses a console of another region of the same board (the Super Famicom's JPN), a console whose data storage is
// not where the console programs write, and another model's material; a backup restores, and startup stages are taken, only on the same
// console. Changes reach the simulated data storage through debugfs in WSL; nothing here opens a real session or lists USB devices.
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
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        /// <summary>a Super NES Classic stand-in whose storage table puts the data storage on another part (labels swapped)</summary>
        static readonly (string dev, string label, long bytes)[] SnesSwapped =
            { ("nanda", "boot", 3072L * 1024), ("nandb", "rootfs", 65536L * 1024), ("nandc", "UDISK", 65536L * 1024), ("nandd", "private", 1024L * 1024), ("nande", "data", 64000L * 1024) };
        /// <summary>a Super NES Classic with hakchi's files on its data storage (what ccsprep mstate reports as prepared)</summary>
        static string SnesPrepared(string region = "USA", string hwid = HwidA)
        {
            var c = SnesConsole("modified-noorig", region, hwid: hwid);
            File.WriteAllText(Path.Combine(c, "prepared-files.txt"), "hakchi v1.0.4-126 files installed (simulated)\n");
            return c;
        }
        /// <summary>a small NES Classic (the 64 MB data fixture)</summary>
        static string SmallNes(string hwid, bool hakchi = false)
        {
            var lay = Layout.Select(x => x.dev == "nandc" ? (x.dev, x.label, 65536L * 1024) : x).ToArray();
            var c = Console_("modified-noorig", hwid, Path.Combine(Fx, "data-modern.img"), lay);
            if (hakchi) File.WriteAllText(Path.Combine(c, "prepared-files.txt"), "hakchi v1.0.4-126 files installed (simulated)\n");
            return c;
        }

        public static void SnesData()
        {
            SimulatedConsole.FileStore = new DebugfsFileStore();
            try { SnesDataRuns(); }
            finally { SimulatedConsole.FileStore = null; }
        }

        static void SnesDataRuns()
        {
            Part("the operations available for the Super NES Classic, and the data storage they write", () =>
            {
                var ops = new[] { "files", "composition", "addons", "firmware", "community" };
                Check(ops.All(o => ConsoleModels.Snes.Refusal(o) == null) && ops.All(o => Capabilities.Allowed("capability", "snes-classic-" + o)) && !Capabilities.Allowed("game-system", "snes")
                      && !Capabilities.Allowed("save-adapter", "snes-classic-stock-battery") && !Capabilities.Allowed("capability", "snes-stock-save-conversion"),
                      "file changes, Console Composer, the add-ons, system images and community mods are available for the Super NES Classic; its games stay unplayable on the PC and its saves are never converted");
                var jpn = ConsoleIdentity.Parse("ccsbr=1\nboard=dp-shvc\nregion=JPN\nend=1\n");
                Check(ops.All(o => ConsoleModels.RefusalFor(jpn, o) != null), "a dp-shvc console of the Super Famicom's region (JPN) is refused for each of them");
                ConsoleIdentity Table(string t) => ConsoleIdentity.Parse("ccsbr=1\nboard=dp-shvc\nregion=USA\nparttable=" + t + "\nend=1\n");
                var ok = Table("mbr::32768 nanda:boot:6144 nandb:rootfs:131072 nandc:data:614400 nandd:private:2048 nande:UDISK:128000");
                var moved = Table("mbr::32768 nanda:boot:6144 nandb:rootfs:131072 nandc:UDISK:614400 nandd:private:2048 nande:data:128000");
                var two = Table("mbr::32768 nanda:boot:6144 nandb:rootfs:131072 nandc:data:614400 nandd:private:2048 nande:rootfs_data:128000");
                Check(ConsoleData.Refusal(ok) == null && ConsoleData.Refusal(moved) != null && ConsoleData.Refusal(two) != null && ConsoleData.Refusal(Table("")) != null,
                      "the data storage must be the one part the console programs mount (nandc, labeled data): another part, two data parts or no table are refused (" + ConsoleData.Refusal(moved) + ")");
                Check(ConsoleData.RegionRefusal(ConsoleModels.Snes, "USA", "EUR") != null && ConsoleData.RegionRefusal(ConsoleModels.Snes, "EUR", "EUR") == null && ConsoleData.RegionRefusal(ConsoleModels.Snes, null, null) != null
                      && ConsoleData.RegionRefusal(ConsoleModels.Nes, "EUR_USA", "USA") == null,
                      "a Super NES Classic's files move from one console to another only within one region; the NES Classic's handling is unchanged");
            });

            Part("Expert Mode on a Super NES Classic", () =>
            {
                var s = SettingsIn(NewDir("snes-files"), false); var c = SnesConsole("modified-noorig"); var b = Backup(c, s);
                var con = RecoverySessions.OpenSimulated(c, ImageKind.Files); var ctx = Ctx(s, ImageKind.Files);
                var nes = SmallNes(HwidA); var nb = Backup(nes, SettingsIn(NewDir("snes-files-nes"), false));
                var (np, nw) = RestorePoints.Establish(con, nb.PrimaryPath, null);
                Check(np == null && (nw ?? "").Contains("not of this console's model"), "an NES Classic's backup (even with this console's identity) is never a Super NES Classic's restore point (" + nw + ")");
                var (rp, why) = RestorePoints.Establish(con, b.PrimaryPath, null);
                Check(rp != null && rp.Model == ConsoleModels.Snes && rp.Covers.Contains(RestorePoints.UserFiles) && rp.Banner.Contains(SessionRestorePoint.NoticeText),
                      "this console's own verified backup becomes the session's restore point, with its date and notice (" + why + ")");
                if (rp == null) { GoneConsole(c); GoneConsole(nes); return; }
                const string game = "hakchi/games/snes-usa/CLV-H-ZZZZZ/CLV-H-ZZZZZ.sfrom", sram = "clover/profiles/0/CLV-P-SAAAE/cartridge.sram";
                var gameBytes = Encoding.ASCII.GetBytes(new string('g', 6000)); var raw = new byte[8192]; raw[0] = 0x5A; raw[8191] = 0xA5;
                var (sp, sw) = FilesEngine.Plan(con, rp, "user", new[] { FileChange.Put(game, gameBytes), FileChange.Put(sram, raw) }, null);
                Check(sp != null && sp.Changes.Count == 2 && sp.Changes[1].NewSha256 == Sha(raw) && sp.Notes.Any(n => n.Contains("written exactly as you chose it") && n.Contains("Super NES Classic Edition")),
                      "a game in hakchi's own game folder for this model and a battery save: the save goes exactly as chosen (never through the NES Classic's converter), and the review says so (" + sw + ")");
                var (op, ow) = FilesEngine.Plan(con, rp, "user", new[] { FileChange.Put("hakchi/rootfs/etc/motd", new byte[] { 1 }) }, null);
                Check(op == null && ow.Contains("not a user file"), "the system overlay stays outside Expert Mode on this model too (" + ow + ")");
                var o = FilesEngine.Execute(con, sp, sp.Digest, sp.Nonce, ctx);
                Check(o.Ok && con.FileStat("user", game).state == Sha(gameBytes) && con.FileStat("user", sram).state == Sha(raw) && Record.Load(o.RecordFile).S("state") == "changed",
                      "written and read back on the console, recorded (" + o.Problem + ")");
                var (p2, _) = FilesEngine.Plan(con, rp, "user", new[] { FileChange.Put(sram, new byte[] { 9 }) }, null);
                SimulatedConsole.FileStore.Write(c, sram, new byte[] { 7, 7 });
                var o2 = FilesEngine.Execute(con, p2, p2.Digest, p2.Nonce, ctx);
                Check(o2.State == "stopped" && o2.Problem.Contains("changed since the review") && con.FileStat("user", sram).bytes == 2, "a save changed after the review is not overwritten: stopped, nothing retried (" + o2.Problem + ")");
                var sw2 = SnesConsole("modified-noorig", layout: SnesSwapped);
                var (pw, ww) = RestorePoints.Establish(RecoverySessions.OpenSimulated(sw2, ImageKind.Files), b.PrimaryPath, null);
                Check(pw == null && (ww ?? "").Contains("data storage"), "a console whose data storage is on another part: no restore point, so no file change (" + ww + ")");
                GoneConsole(c); GoneConsole(nes); GoneConsole(sw2); GoneBackups(s);
            });

            Part("Console Composer and private restore packages on a Super NES Classic", () =>
            {
                var s = SettingsIn(NewDir("snes-compose"), false); var ctx = Ctx(s, ImageKind.Files);
                var c = SnesConsole("modified-noorig"); const string save = "clover/profiles/0/CLV-P-SAAAE/cartridge.sram";
                SimulatedConsole.FileStore.Write(c, save, Encoding.ASCII.GetBytes("this console's save"));
                var a = Backup(c, s); var orig = RecoverySessions.OpenSimulated(c, ImageKind.Files).FileStat("compose", save);
                SimulatedConsole.FileStore.Write(c, save, Encoding.ASCII.GetBytes("a later save"));
                var boots = new[] { "boot0", "boot1", "boot2" }.ToDictionary(t => t, t => Sha(SimulatedConsole.Dev(c, t)));
                var con = RecoverySessions.OpenSimulated(c, ImageKind.Files);
                var (p, why) = ComposerEngine.Plan(con, ctx, "rebuild", new[] { new ComposeItem { SourceBackup = a.PrimaryPath, SourcePath = save } });
                var o = p == null ? null : ComposerEngine.Execute(con, p, p.Digest, p.Nonce, ctx);
                Check(o != null && o.Ok && con.FileStat("compose", save).state == orig.state && p.Protective?.Session == con.SessionId && boots.All(kv => Sha(SimulatedConsole.Dev(c, kv.Key)) == kv.Value),
                      "rebuild from this console's own backup: the save is back, after a backup made in this session; startup stages and kernel untouched (" + (why ?? o?.Problem) + ")");
                // another Super NES Classic of the same region: its save moves; of the other region: refused; an NES Classic's: refused
                var usa2 = SnesConsole("modified-noorig", "USA", hwid: HwidB); SimulatedConsole.FileStore.Write(usa2, save, Encoding.ASCII.GetBytes("the other console's save"));
                var eur = SnesConsole("modified-noorig", "EUR", hwid: HwidB); var nes = SmallNes(HwidB);
                var ub = Backup(usa2, SettingsIn(NewDir("snes-compose-usa2"), false)); var eb = Backup(eur, SettingsIn(NewDir("snes-compose-eur"), false)); var nb = Backup(nes, SettingsIn(NewDir("snes-compose-nes"), false));
                var con2 = RecoverySessions.OpenSimulated(c, ImageKind.Files);
                var (mp, mw) = ComposerEngine.Plan(con2, ctx, "migrate", new[] { new ComposeItem { SourceBackup = ub.PrimaryPath, SourcePath = save } });
                var mo = mp == null ? null : ComposerEngine.Execute(con2, mp, mp.Digest, mp.Nonce, ctx);
                Check(mo != null && mo.Ok && con2.FileStat("compose", save).bytes == "the other console's save".Length && ConsoleIdentity.SameConsole(con2.Identify().Hwid, HwidA),
                      "migration from another North American Super NES Classic: its save moved; this console keeps its own identity (" + (mw ?? mo?.Problem) + ")");
                int f0 = Directory.GetFiles(s.PrimaryFolder).Length;
                var (e1, ew) = ComposerEngine.Plan(RecoverySessions.OpenSimulated(c, ImageKind.Files), ctx, "migrate", new[] { new ComposeItem { SourceBackup = eb.PrimaryPath, SourcePath = save } });
                var (n1, nw) = ComposerEngine.Plan(RecoverySessions.OpenSimulated(c, ImageKind.Files), ctx, "migrate", new[] { new ComposeItem { SourceBackup = nb.PrimaryPath, SourcePath = save } });
                Check(e1 == null && ew.Contains("different regions") && n1 == null && nw.Contains("never put on another kind") && Directory.GetFiles(s.PrimaryFolder).Length == f0,
                      "a European Super NES Classic's files onto a North American one, or an NES Classic's: refused before any backup or write (" + ew + " / " + nw + ")");
                // a stale plan: the protective backup changed after the review
                var (sp, _) = ComposerEngine.Plan(RecoverySessions.OpenSimulated(c, ImageKind.Files), ctx, "rebuild", new[] { new ComposeItem { SourceBackup = a.PrimaryPath, SourcePath = save } });
                if (sp != null && sp.Refusal == null)
                {
                    File.Move(sp.Protective.Path, sp.Protective.Path + ".aside");
                    var so = ComposerEngine.Execute(RecoverySessions.OpenSimulated(c, ImageKind.Files), sp, sp.Digest, sp.Nonce, ctx);
                    Check(so.State == "refused" && sp.Nonce != null, "a plan from another connection, its backup moved aside: refused, nothing written (" + so.Problem + ")");
                    File.Move(sp.Protective.Path + ".aside", sp.Protective.Path);
                }
                // private restore packages: built only from backups of the destination's model and region
                var outPath = Path.Combine(NewDir("snes-private"), "package.zip");
                string Build(string src, string dest) { try { PackageComposer.Build(new[] { (src, save) }, dest, "none", "like-my-other-console", outPath, "test"); return null; } catch (ComposeException ex) { return ex.Message; } }
                var bn = Build(nb.PrimaryPath, a.PrimaryPath); var be = Build(eb.PrimaryPath, a.PrimaryPath);
                Check(bn != null && bn.Contains("never put on another kind") && be != null && be.Contains("different regions") && !File.Exists(outPath),
                      "a private restore package from an NES Classic's or another region's backup for a Super NES Classic: not built (" + bn + " / " + be + ")");
                var swc = SnesConsole("modified-noorig", layout: SnesSwapped);
                var (dp, dw) = ComposerEngine.Plan(RecoverySessions.OpenSimulated(swc, ImageKind.Files), ctx, "rebuild", new[] { new ComposeItem { SourceBackup = a.PrimaryPath, SourcePath = save } });
                Check(dp == null && dw.Contains("data storage"), "a console whose data storage is on another part: refused before any backup (" + dw + ")");
                foreach (var x in new[] { c, usa2, eur, nes, swc }) GoneConsole(x);
                GoneBackups(s);
            });

            Part("the same-console and same-model rules hold for restore, rescue and Composer", () =>
            {
                var s = SettingsIn(NewDir("snes-same"), false); var ctx = Ctx(s, ImageKind.Restore);
                var snes = SnesConsole("modified-noorig", hwid: HwidA); var nes = SmallNes(HwidA); var nb = Backup(nes, SettingsIn(NewDir("snes-same-nes"), false));
                int f0 = Directory.Exists(s.PrimaryFolder) ? Directory.GetFiles(s.PrimaryFolder).Length : 0;
                var (rp, rr) = RestoreEngine.Prepare(RecoverySessions.OpenSimulated(snes, ImageKind.Restore), nb.PrimaryPath, ctx);
                var (cp, cw) = ComposerEngine.Plan(RecoverySessions.OpenSimulated(snes, ImageKind.Files), Ctx(s, ImageKind.Files), "rebuild", new[] { new ComposeItem { SourceBackup = nb.PrimaryPath, SourcePath = "clover/profiles/0/CLV-P-NAAAE/save.sav" } });
                var (qp, qr) = RescueEngine.Prepare(RecoverySessions.OpenSimulated(snes, ImageKind.Rescue), Ctx(s, ImageKind.Rescue), nb.PrimaryPath, new[] { "boot1" }, null);
                Check(rp == null && (rr?.Problem ?? "").Contains("never put on another kind") && cp == null && cw.Contains("never put on another kind") && qp == null && qr != null
                      && Writes(snes).Count == 0 && (Directory.Exists(s.PrimaryFolder) ? Directory.GetFiles(s.PrimaryFolder).Length : 0) == f0,
                      "an NES Classic's backup, even one naming this console's identity, is never restored, composed or used for startup stages on a Super NES Classic: nothing written (" + rr?.Problem + " / " + cw + " / " + qr?.Problem + ")");
                GoneConsole(snes); GoneConsole(nes); GoneBackups(s);
            });

            Part("CCS Mod and CCS Nucleus on a Super NES Classic", () =>
            {
                var s = SettingsIn(NewDir("snes-method"), false); var ctx = Ctx(s, ImageKind.Method);
                var c = SnesPrepared();
                var cur = MethodEngine.Current(OpenMethod(c), ctx);
                Check(cur.refusal == null && cur.st != null && cur.st.Packages.Count == 0 && cur.id?.Model == ConsoleModels.Snes, "the add-ons read on a prepared Super NES Classic, before anything is chosen: none yet (" + Flat(cur.refusal) + ")");
                var home = HomePackage.Folder(MakeHome("mod", "snes1"), "mod");
                var con = OpenMethod(c);
                var (p, r) = MethodEngine.Prepare(con, ctx, Choice(true, "direct", "ccs-home"), home);
                Check(r == null && p.Steps.SequenceEqual(new[] { "method:ccs-mod", "method:ccs-nucleus", "nstart", "hput", "huse" }) && p.StartLine == "ccs-home never on default 10" && Verifier.VerifyFile(p.ProtectivePath).Ok
                      && p.ProtectiveArchive.Session == con.SessionId,
                      "CCS Mod and CCS Nucleus opening CCS HOME straight away: both packages, the start, then CCS HOME's package, after a complete backup made in this session (" + (r?.Problem ?? Listed(p?.Steps)) + ")");
                if (p == null) { GoneConsole(c); return; }
                var nonce = p.Nonce; SetInstalled(c, "other-package");
                var mc = MethodEngine.Execute(con, p, p.Digest, nonce, ctx); SetInstalled(c);
                Check(mc.State == "refused" && mc.Problem.Contains("changed since the review") && Writes(c).Count == 0 && p.Nonce == nonce, "the console's packages changed after the review: refused, nothing written (" + mc.Problem + ")");
                // interrupted at the start setting (the console's answer is a failure): stopped there, nothing retried, the record left open;
                // a new connection's plan finishes it without placing the packages twice
                File.WriteAllText(Path.Combine(c, "faults.json"), "{\"prep\": {\"step\": \"nstart\"}}");
                var oi = MethodEngine.Execute(con, p, p.Digest, nonce, ctx); File.Delete(Path.Combine(c, "faults.json")); Gone(p.ProtectivePath);
                Check(oi.State == "stopped" && oi.Done.SequenceEqual(new[] { "method:ccs-mod", "method:ccs-nucleus" }) && Record.Load(oi.RecordFile).S("state") == "stopped" && HomeIn(c) == null && StartIn(c) == "none",
                      "interrupted at CCS Nucleus's start setting: stopped there, nothing retried, its record stopped; CCS HOME's package not sent (" + oi.Problem + ")");
                con = OpenMethod(c); (p, r) = MethodEngine.Prepare(con, ctx, Choice(true, "direct", "ccs-home"), home);
                var o = MethodEngine.Execute(con, p, p.Digest, p.Nonce, ctx);
                var ph = Record.Load(o.RecordFile).A("phases").OfType<Dictionary<string, object>>().Where(x => x.S("state") != "began").ToList();
                Check(o.State == "scheduled" && ph.Take(2).All(x => x.S("state") == "already-done"), "a new connection finishes it: the two packages already waiting are not placed again (" + (r?.Problem ?? o.Problem) + ")");
                Check(o.State == "scheduled" && Writes(c).SequenceEqual(new[] { "method ccs-mod", "method ccs-nucleus", "nstart ccs-home never on default 10", "hput " + p.Home.Copy, "huse " + p.Home.Copy })
                      && Waiting(c).SequenceEqual(new[] { "ccs-mod.hmod", "ccs-nucleus.hmod" }) && HomeIn(c) == p.Home.Copy && StartIn(c) == "ccs-home never on default 10",
                      "written in that order and read back: CCS HOME in use, both packages waiting for the next start, CCS HOME opening first (" + o.Problem + "; " + Listed(Writes(c)) + ")");
                Gone(p.ProtectivePath);
                var st = SimulatedConsole.NormalStart(c);
                Check(Sorted(Installed(c)).SequenceEqual(new[] { "ccs-mod", "ccs-nucleus" }), "the next normal start installs both (" + Listed(st) + ")");
                // the chooser, with the menu used last; then everything removed again (the chooser first)
                var con2 = OpenMethod(c); var (q, qr) = MethodEngine.Prepare(con2, ctx, Choice(true, "chooser", "ccs-home", "last"), home);
                var qo = q == null ? null : MethodEngine.Execute(con2, q, q.Digest, q.Nonce, ctx); Gone(q?.ProtectivePath);
                Check(qo?.State == "scheduled" && Waiting(c).SequenceEqual(new[] { "hmod/ccs-nucleus-chooser.hmod" }) && StartIn(c) == "ccs-home always on last 10",
                      "the chooser added (the menu used last after its countdown): its package waits in hmod/, the start reads back (" + (qr?.Problem ?? qo?.Problem) + ")");
                SimulatedConsole.NormalStart(c);
                var con3 = OpenMethod(c); var (x, xr) = MethodEngine.Prepare(con3, ctx, Choice(false));
                var xo = x == null ? null : MethodEngine.Execute(con3, x, x.Digest, x.Nonce, ctx); Gone(x?.ProtectivePath);
                Check(xo?.State == "scheduled" && x.Remove.SequenceEqual(new[] { "ccs-nucleus-chooser", "ccs-nucleus", "ccs-mod" }) && File.ReadAllText(Path.Combine(Transfer(c), "uninstall")) == "ccs-nucleus-chooser ccs-nucleus ccs-mod\n",
                      "all removed: one removal list, the chooser before CCS Nucleus (" + (xr?.Problem ?? xo?.Problem) + ")");
                var st3 = SimulatedConsole.NormalStart(c);
                Check(Installed(c).Count == 0 && HomeIn(c) == p.Home.Copy, "the next normal start runs each package's own removal; CCS HOME's package and saves stay (" + Listed(st3) + ")");
                // another region of the same board, and a data storage elsewhere: refused before any backup
                var jpn = SnesConsole("modified-noorig", "JPN"); var swc = SnesConsole("modified-noorig", layout: SnesSwapped);
                File.WriteAllText(Path.Combine(swc, "prepared-files.txt"), "hakchi v1.0.4-126 files installed (simulated)\n");
                int f0 = BackupFiles(s);
                var (j, jr) = MethodEngine.Prepare(OpenMethod(jpn), ctx, Choice(true));
                var (w, wr) = MethodEngine.Prepare(OpenMethod(swc), ctx, Choice(true));
                Check(j == null && jr.Problem.Contains("JPN") && w == null && wr.Problem.Contains("data storage") && BackupFiles(s) == f0 && Writes(jpn).Count == 0 && Writes(swc).Count == 0,
                      "a Super Famicom (JPN) console, or a console whose data storage is elsewhere: refused before any backup or write (" + jr.Problem + " / " + wr.Problem + ")");
                // HOME designs belong to CCS HOME: the same model checks before any backup
                var (dj, djw) = DesignApplyEngine.PlanRevert(RecoverySessions.OpenSimulated(jpn, ImageKind.Files), Ctx(s, ImageKind.Files), "defaults");
                Check(dj == null && djw.Contains("JPN") && BackupFiles(s) == f0, "a HOME design change on a Super Famicom (JPN) console: refused before any backup (" + djw + ")");
                foreach (var y in new[] { c, jpn, swc }) GoneConsole(y);
                GoneBackups(s);
            });

            Part("system images as CCS Nucleus choices, in both directions", () =>
            {
                var s = SettingsIn(NewDir("snes-firmware"), false); var dir = NewDir("snes-firmware-roots");
                var snesImg = Path.Combine(dir, "snes.hsqs"); File.WriteAllBytes(snesImg, SystemRoot(dir, "snes-own", "gzip", Clover("dp-shvc", "USA")));
                var nesImg = Path.Combine(dir, "nes.hsqs"); File.WriteAllBytes(nesImg, SystemRoot(dir, "nes-own", "gzip", Clover("dp-nes", "EUR_USA")));
                var eurImg = Path.Combine(dir, "snes-eur.hsqs"); File.WriteAllBytes(eurImg, SystemRoot(dir, "snes-eur", "gzip", Clover("dp-shvc", "EUR")));
                // an SNES Classic system on an NES Classic: kept as it was (its controller note, its HOME saves kept apart); CCS Nucleus started
                // directly there, so the startup chooser (the only way a second system starts) comes in the same reviewed change, first
                var n = SmallNes(HwidA, true); SetInstalled(n, Methods.Nucleus); SetStart(n, "native never off default 10");
                var nctx = Ctx(s, ImageKind.Firmware); var ncon = RecoverySessions.OpenSimulated(n, ImageKind.Firmware);
                var (np, nr) = FirmwareEngine.Prepare(ncon, nctx, "add", null, "My SNES System", snesImg);
                var no = np == null ? null : FirmwareEngine.Execute(ncon, np, np.Digest, np.Nonce, nctx); Gone(np?.ProtectivePath);
                Check(no?.State == "added" && np.System.Platform == "snes" && np.Notes.Any(x => x.Contains("X, Y, L and R")) && np.Notes.Any(x => x.Contains("apart from the NES Classic system's"))
                      && SimRegistry(n).ContainsKey(np.Id) && (SimRegistry(n)[np.Id] as Dictionary<string, object>)?.S("kind") == "owner-root",
                      "an owner's SNES Classic system image is added as a CCS Nucleus choice on an NES Classic, with its notes, as before (" + (nr?.Problem ?? no?.Problem) + ")");
                Check(no?.State == "added" && np.Steps.SequenceEqual(new[] { "method:" + Methods.Chooser, "nstart", "rput", "rimport" }) && np.StartLine == "native always off default 10"
                      && Writes(n).SequenceEqual(new[] { "method " + Methods.Chooser, "nstart native always off default 10", "rput " + np.Id, "rimport " + np.Id }) && Waiting(n).SequenceEqual(new[] { "hmod/" + Methods.Chooser + ".hmod" })
                      && StartIn(n) == "native always off default 10" && np.Notes.Any(x => x.Contains("startup chooser")),
                      "without the startup chooser, adding a second system adds the chooser in the same reviewed change, first (its package waits for the next start; the chooser shows at start, nothing else of the start changes) (" + Listed(Writes(n)) + ")");
                SimulatedConsole.NormalStart(n);
                Check(Sorted(Installed(n)).SequenceEqual(new[] { Methods.Nucleus, Methods.Chooser }), "the next normal start installs the chooser beside CCS Nucleus");
                // an NES Classic system on a Super NES Classic, and another region's SNES Classic system
                var c = SnesPrepared(); SetInstalled(c, Methods.Nucleus, Methods.Chooser); SetStart(c, "native always off default 10");
                var ctx = Ctx(s, ImageKind.Firmware); var con = RecoverySessions.OpenSimulated(c, ImageKind.Firmware);
                var (p, r) = FirmwareEngine.Prepare(con, ctx, "add", null, "My NES System", nesImg);
                var o = p == null ? null : FirmwareEngine.Execute(con, p, p.Digest, p.Nonce, ctx); Gone(p?.ProtectivePath);
                Check(o?.State == "added" && p.System.Platform == "nes" && p.Notes.Any(x => x.Contains("apart from the SNES Classic system's")) && !p.Notes.Any(x => x.Contains("X, Y, L and R"))
                      && Writes(c).SequenceEqual(new[] { "rput " + p.Id, "rimport " + p.Id }) && Sha(Path.Combine(c, "nucleus-roots", p.Id + ".hsqs")) == Sha(nesImg),
                      "an owner's NES Classic system image is added as a CCS Nucleus choice on a Super NES Classic: sent, imported and read back, with the note for this direction (" + (r?.Problem ?? o?.Problem) + ")");
                var (e, er) = FirmwareEngine.Prepare(RecoverySessions.OpenSimulated(c, ImageKind.Firmware), ctx, "add", null, "My Other SNES", eurImg); Gone(e?.ProtectivePath);
                Check(e != null && e.System.Platform == "snes" && e.System.Region == "eur" && !e.Notes.Any(x => x.Contains("X, Y, L and R") || x.Contains("apart from")),
                      "another region's SNES Classic system on a Super NES Classic: planned with no note (the same kind of system and controller) (" + er?.Problem + ")");
                var con2 = RecoverySessions.OpenSimulated(c, ImageKind.Firmware); var (d, dr) = FirmwareEngine.Prepare(con2, ctx, "remove", p.Id, null, null);
                var dout = d == null ? null : FirmwareEngine.Execute(con2, d, d.Digest, d.Nonce, ctx); Gone(d?.ProtectivePath);
                Check(dout?.State == "removed" && !SimRegistry(c).ContainsKey(p.Id) && SimRegistry(c).ContainsKey("native"), "removed again: the choice and its image gone; the console's own system stays a choice (" + (dr?.Problem ?? dout?.Problem) + ")");
                var jpn = SnesConsole("modified-noorig", "JPN"); File.WriteAllText(Path.Combine(jpn, "prepared-files.txt"), "hakchi v1.0.4-126 files installed (simulated)\n");
                var (jp, jr) = FirmwareEngine.Prepare(RecoverySessions.OpenSimulated(jpn, ImageKind.Firmware), ctx, "add", null, "My SNES System", snesImg);
                Check(jp == null && jr.Problem.Contains("JPN") && Writes(jpn).Count == 0, "a Super Famicom (JPN) console: refused before any backup (" + jr?.Problem + ")");
                foreach (var y in new[] { n, c, jpn }) GoneConsole(y);
                GoneBackups(s);
            });

            Part("community mods on a Super NES Classic", () =>
            {
                var dir = NewDir("snes-community"); var set = FixtureSet().Where(f => f.Id == "wifi" || f.Id == "menu-x").ToList();
                var recs = set.Select(RecordJson).ToList(); ((JObj)recs.First(x => (string)x["id"] == "menu-x")["compatible"]).Set("consoles", new List<object> { "nes-classic" });
                var cat = CommunityCatalog.Parse(CatalogBytes(3, recs)); var files = FixtureFiles(dir, set);
                var s = SettingsIn(NewDir("snes-community-run"), false); var ctx = Ctx(s, ImageKind.Community);
                var c = SnesPrepared(); ClearLedgers(s);
                var (n1, r1) = CommunityEngine.Prepare(OpenComm(c), ctx, cat, Change(Pick(files, "menu-x"), new[] { "menu-x" }));
                Check(n1 == null && r1.Problem.Contains("not for this console's model") && Writes(c).Count == 0, "a mod whose record lists only the NES Classic: refused on a Super NES Classic, nothing written (" + r1?.Problem + ")");
                var con = OpenComm(c);
                var (p, r) = CommunityEngine.Prepare(con, ctx, cat, Change(Pick(files, "wifi"), new[] { "wifi" }));
                var o = p == null ? null : CommunityEngine.Execute(con, p, p.Digest, p.Nonce, ctx);
                Check(o?.State == "scheduled" && Writes(c).SequenceEqual(new[] { "cput fx_wifi" }) && Waiting(c).SequenceEqual(new[] { "fx_wifi.hmod" }) && Sha(Path.Combine(Transfer(c), "fx_wifi.hmod")) == cat.Find("wifi").DefaultVariant.Sha256,
                      "a mod listed for this model (a local test package made for this run): planned after a backup made in this session and placed for hakchi, the exact approved bytes (" + (r?.Problem ?? o?.Problem) + ")");
                Gone(p?.ProtectivePath);
                var st = SimulatedConsole.NormalStart(c); var rd = CommunityEngine.Read(OpenComm(c), ctx, cat);
                Check(Installed(c).SequenceEqual(new[] { "fx_wifi" }) && rd.Family == "snes-classic" && rd.Ledger.Any(m => m.Id == "wifi" && m.State == CommunityLedger.Installed),
                      "the next normal start installs it; the next connection reads its files back as the approved ones (" + Listed(st) + ")");
                var con2 = OpenComm(c); var (q, qr) = CommunityEngine.Prepare(con2, ctx, cat, Change(new Dictionary<string, string>(), remove: new[] { "wifi" }));
                var qo = q == null ? null : CommunityEngine.Execute(con2, q, q.Digest, q.Nonce, ctx); Gone(q?.ProtectivePath);
                SimulatedConsole.NormalStart(c);
                Check(qo?.State == "scheduled" && Installed(c).Count == 0, "removed again through hakchi's own removal (" + (qr?.Problem ?? qo?.Problem) + ")");
                GoneConsole(c); GoneBackups(s);
            });

            Part("the worker's add-on request on a Super NES Classic, as both Workbench hosts send it", () =>
            {
                var s = SettingsIn(NewDir("snes-worker"), false);
                var c = SnesPrepared(); SetInstalled(c, Methods.Mod, Methods.Nucleus); SetStart(c, "native never on default 10");
                var req = new JObj { { "cmd", "method-plan" }, { "console", "simulated" }, { "folder", c }, { "choose", true } };
                int f0 = BackupFiles(s), filesAtState = -1, writesAtState = -1; Dictionary<string, object> plan = null;
                var evs = WorkerTalk(req, s, ev =>
                    ev.S("event") == "addon-state" ? Fn(() => { filesAtState = BackupFiles(s); writesAtState = Writes(c).Count; return Target("method-target", Choice(true, "direct", "ccs-home")); })
                    : ev.S("event") == "method-plan" ? Confirm("method-confirm", plan = ev.O("plan")) : null);
                var ws = Ev(evs, "addon-state"); var wr = Last(evs);
                Check(ws != null && ws.S("situation") == "hakchi" && SameChoice(ws.O("current"), Choice(true, "direct")) && filesAtState == f0 && writesAtState == 0,
                      "worker: the Super NES Classic's add-ons are reported first, before any backup or write (" + Flat(ws == null ? wr?.S("problem") ?? wr?.S("text") : ws.S("situation")) + ")");
                Check(plan != null && Strings(plan, "steps").SequenceEqual(new[] { "nstart" }) && wr?.S("state") == "scheduled" && Writes(c).SequenceEqual(new[] { "nstart ccs-home never on default 10" }) && StartIn(c) == "ccs-home never on default 10",
                      "worker: planned after the backup and confirmed with the plan's own code: CCS HOME opens at power-on on the Super NES Classic (" + Listed(Strings(plan, "steps")) + "; " + wr?.S("state") + " " + wr?.S("problem") + ")");
                Gone(wr?.S("protective"));
                var jpn = SnesConsole("modified-noorig", "JPN"); File.WriteAllText(Path.Combine(jpn, "prepared-files.txt"), "hakchi v1.0.4-126 files installed (simulated)\n");
                var jr = Last(WorkerTalk(new JObj { { "cmd", "method-plan" }, { "console", "simulated" }, { "folder", jpn }, { "choose", true } }, s, ev => ev.S("event") == "addon-state" ? new JObj { { "cmd", "quit" } } : null));
                Check(jr?.S("state") == "refused" && (jr?.S("problem") ?? "").Contains("JPN") && Writes(jpn).Count == 0, "worker: a Super Famicom (JPN) console is refused before anything (" + jr?.S("problem") + ")");
                GoneConsole(c); GoneConsole(jpn); GoneBackups(s);
            });

            Part("the Sega Mini menu pictures on a Super NES Classic", () =>
            {
                var s = SettingsIn(NewDir("snes-sega"), false); var ctx = Ctx(s, ImageKind.Files);
                var none = SnesConsole("modified-noorig");
                var (n0, nw) = SegaCleanup.PlanRemove(RecoverySessions.OpenSimulated(none, ImageKind.Files), ctx);
                Check(n0 != null && n0.Refusal != null && n0.Refusal.Contains("No pictures") && n0.Changes.Count == 0 && Writes(none).Count == 0, "no eligible picture on the console: nothing to remove, nothing written (" + (nw ?? n0?.Refusal) + ")");
                var c = SnesConsole("modified-noorig"); const string g = "hakchi/games/snes-usa/CLV-H-AAAAA";
                var pic = Encoding.ASCII.GetBytes(new string('m', 7000));
                // a game added with hakchi for this model: its entry (launched by the console's own Super NES emulator), its picture, and the picture
                // Hakchi2 CE made from it for the Sega Mini menu
                SimulatedConsole.FileStore.Write(c, g + "/CLV-H-AAAAA.desktop", Encoding.ASCII.GetBytes("[Desktop Entry]\nType=Application\nExec=/bin/clover-canoe-shvc-wr /var/games/CLV-H-AAAAA/CLV-H-AAAAA.sfrom\nIcon=/var/games/CLV-H-AAAAA/CLV-H-AAAAA.png\nCode=CLV-H-AAAAA\nName=A Game\n"));
                SimulatedConsole.FileStore.Write(c, g + "/CLV-H-AAAAA.png", Encoding.ASCII.GetBytes("main cover")); SimulatedConsole.FileStore.Write(c, g + "/CLV-H-AAAAA_mdmini.png", pic);
                var con = RecoverySessions.OpenSimulated(c, ImageKind.Files);
                var (p, why) = SegaCleanup.PlanRemove(con, ctx);
                var o = p == null || p.Refusal != null ? null : SegaCleanup.Execute(con, p, p.Digest, p.Nonce, ctx);
                Check(o != null && o.Ok && p.Changes.Count == 1 && p.Changes[0].Path == g + "/CLV-H-AAAAA_mdmini.png" && con.FileStat("user", g + "/CLV-H-AAAAA_mdmini.png").state == "absent"
                      && con.FileStat("user", g + "/CLV-H-AAAAA.png").bytes == "main cover".Length,
                      "an eligible picture in hakchi's game folder for this model: removed after the review and a backup made in this session; the game's own cover stays (" + (why ?? p?.Refusal ?? o?.Problem) + ")");
                GoneConsole(none); GoneConsole(c); GoneBackups(s);
            });
        }
    }
}
