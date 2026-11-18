// Classic Cart Studio console backup and restore engine tests: Console Composer's private restore package (built offline from a verified
// backup, applied to the console it names under the strict policy). GPL-3.0-or-later. Simulated consoles only (debugfs in WSL).
using CcsBackup.Compose;
using CcsBackup.Device;
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.IO;
using System.Linq;
using System.Text;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        public static void PackageRuns()
        {
            SimulatedConsole.FileStore = new DebugfsFileStore();
            try
            {
                var dir = NewDir("package"); var s = SettingsIn(Path.Combine(dir, "folders"), false);
                var c = Console_("modified"); var a = Backup(c, s);
                const string save = "clover/profiles/0/CLV-P-NAAAE/save.sav";
                var con0 = RecoverySessions.OpenSimulated(c, ImageKind.Files); var orig = con0.FileStat("compose", save);
                var pkg = Path.Combine(dir, PrivatePackage.FileStem(DateTime.Now, "test") + ".zip");
                var info = PackageComposer.Build(new[] { (a.PrimaryPath, save) }, a.PrimaryPath, "ccs-mod", "rebuild-from-backups", pkg, "test");
                Check(info.Members.Count == 1 && info.Members[0].Role == "data-file" && info.Members[0].Places.Single() == "data:" + save && ConsoleIdentity.SameConsole(info.DestinationHwid, HwidA),
                    "built offline: one data file with its exact place, for the console the backup came from");
                Check(ShareGuard.Classify(pkg) == FileRole.PrivateRestorePackage && Throws(() => ShareGuard.RequireShareable(pkg)), "the package is recognized as private and is never shareable");
                Check(Throws(() => PackageComposer.Build(new[] { (a.PrimaryPath, "hakchi/rootfs/etc/preinit") }, a.PrimaryPath, "ccs-mod", "rebuild-from-backups", Path.Combine(dir, "x.zip"), "test")),
                    "a file Console Composer does not move (a system overlay file) is refused when building");
                // apply: the later save is replaced by the packaged one, after a backup of the console now
                SimulatedConsole.FileStore.Write(c, save, Encoding.ASCII.GetBytes("a later save that is lost"));
                var boots = new[] { "boot0", "boot1", "boot2" }.ToDictionary(t => t, t => Sha(SimulatedConsole.Dev(c, t)));
                var con = RecoverySessions.OpenSimulated(c, ImageKind.Files); var ctx = Ctx(s, ImageKind.Files);
                var (p, why) = PackageComposer.Plan(con, ctx, pkg);
                Check(p != null && p.Refusal == null && p.Changes.Count == 1 && p.Changes[0].NewSha256 == orig.state && p.Protective?.Session == con.SessionId,
                    "plan: the packaged save replaces the later one; a backup of the console now is made in this session first (" + why + ")");
                // the package changed after the review: refused before anything, confirmation not used
                var keep = p.Nonce; var bak = pkg + ".orig"; File.Copy(pkg, bak);
                using (var f = new FileStream(pkg, FileMode.Open, FileAccess.ReadWrite)) { f.Seek(-1, SeekOrigin.End); f.WriteByte(0x20); }
                var m = PackageComposer.Execute(con, p, p.Digest, keep, ctx);
                Check(m.State == "refused" && p.Nonce == keep && con.FileStat("compose", save).state != orig.state, "the package changed after the review: refused, nothing written, the confirmation not used (" + m.Problem + ")");
                File.Delete(pkg); File.Move(bak, pkg);
                var o = PackageComposer.Execute(con, p, p.Digest, keep, ctx);
                Check(o.Ok && con.FileStat("compose", save).state == orig.state, "applied: the save is the packaged one, read back on the console (" + o.Problem + ")");
                Check(boots.All(kv => Sha(SimulatedConsole.Dev(c, kv.Key)) == kv.Value), "the startup stages and kernel were never written");
                // another console: refused before any backup or write
                var other = Console_("modified", HwidB);
                var conB = RecoverySessions.OpenSimulated(other, ImageKind.Files);
                var (pb, wb) = PackageComposer.Plan(conB, Ctx(SettingsIn(Path.Combine(dir, "other"), false), ImageKind.Files), pkg);
                Check(pb == null && wb.Contains("not the console connected now"), "a package for another console is refused before anything (" + wb + ")");
                // migration package: built from console B's backup for console A (destination from A's backup)
                SimulatedConsole.FileStore.Write(other, save, Encoding.ASCII.GetBytes("the other console's save"));
                var ob = Backup(other, SettingsIn(Path.Combine(dir, "other2"), false));
                var mpk = Path.Combine(dir, "migrate.zip");
                Check(Throws(() => PackageComposer.Build(new[] { (ob.PrimaryPath, save) }, ob.PrimaryPath, "ccs-mod", "like-my-other-console", mpk, "test")), "a migration package needs a backup of the OTHER console as its destination");
                var mi = PackageComposer.Build(new[] { (ob.PrimaryPath, save) }, a.PrimaryPath, "ccs-nucleus", "like-my-other-console", mpk, "test");
                var con2 = RecoverySessions.OpenSimulated(c, ImageKind.Files);
                var (mp, mw) = PackageComposer.Plan(con2, ctx, mpk);
                var mo = mp == null ? null : PackageComposer.Execute(con2, mp, mp.Digest, mp.Nonce, ctx);
                Check(mo != null && mo.Ok && con2.FileStat("compose", save).bytes == "the other console's save".Length && ConsoleIdentity.SameConsole(con2.Identify().Hwid, HwidA),
                    "migration package: the other console's save on this console; this console keeps its identity (" + (mw ?? mo?.Problem) + ")");
                // a hand-made package that names a system path is refused when opened
                Check(Throws(() => PrivatePackage.Write(Path.Combine(dir, "bad.zip"), new PrivatePackageSpec { DestinationHwid = info.DestinationHwid, InstallPolicy = "ccs-mod", StartingPoint = "rebuild-from-backups",
                    Members = { new PrivateMemberSource { Role = "data-file", Place = "data:hakchi/rootfs/etc/preinit", Bytes = new byte[] { 1 } } } })), "a data file aimed at a system path is refused (place rules)");
            }
            finally { SimulatedConsole.FileStore = null; }
        }

        static bool Throws(Action a) { try { a(); return false; } catch (ComposeException) { return true; } }
    }
}
