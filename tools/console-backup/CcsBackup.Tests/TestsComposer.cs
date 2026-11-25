// Classic Cart Studio console backup and restore engine tests: Console Composer's execution (rebuild from this console's backups;
// migration from another console's backup), strict policy. GPL-3.0-or-later. Simulated consoles only (changes through debugfs in WSL).
using CcsBackup.Archive;
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
        public static void ComposerRuns()
        {
            SimulatedConsole.FileStore = new DebugfsFileStore();
            try
            {
                var s = SettingsIn(NewDir("composer"), false);
                var c = Console_("modified"); var a = Backup(c, s);
                const string save = "clover/profiles/0/CLV-P-NAAAE/save.sav";
                var con0 = RecoverySessions.OpenSimulated(c, ImageKind.Files); var orig = con0.FileStat("compose", save);
                SimulatedConsole.FileStore.Write(c, save, Encoding.ASCII.GetBytes("a later save that is lost"));
                var boots = new[] { "boot0", "boot1", "boot2" }.ToDictionary(t => t, t => Sha(SimulatedConsole.Dev(c, t)));
                // rebuild: the save as it was in this console's backup
                var con = RecoverySessions.OpenSimulated(c, ImageKind.Files); var ctx = Ctx(s, ImageKind.Files);
                var items = new List<ComposeItem> { new ComposeItem { SourceBackup = a.PrimaryPath, SourcePath = save }, new ComposeItem { SourceBackup = a.PrimaryPath, SourcePath = "hakchi/rootfs/etc/preinit" } };
                var (p, why) = ComposerEngine.Plan(con, ctx, "rebuild", items);
                Check(p != null && p.Refusal == null && p.Changes.Count == 1 && p.Changes[0].Path == save && p.Changes[0].NewSha256 == orig.state && p.Skipped.Any(x => x.StartsWith("hakchi/rootfs")) && p.Protective?.Session == con.SessionId,
                    "rebuild: the chosen save from this console's backup; a system file is listed as not moved; a backup of the console now is made in this session first (" + why + ")");
                var keep = p.Nonce; File.Move(a.PrimaryPath, a.PrimaryPath + ".withheld");
                var m = ComposerEngine.Execute(con, p, p.Digest, keep, ctx);
                Check(m.State == "refused" && p.Nonce == keep && con.FileStat("compose", save).state != orig.state, "the source backup moved aside after the review: refused, nothing written, the confirmation not used (" + m.Problem + ")");
                File.Move(a.PrimaryPath + ".withheld", a.PrimaryPath);
                var o = ComposerEngine.Execute(con, p, p.Digest, keep, ctx);
                Check(o.Ok && con.FileStat("compose", save).state == orig.state && Verifier.VerifyFile(p.Protective.Path).Ok, "rebuilt: the save is back as in the backup; the backup made just now still holds the later save (" + o.Problem + ")");
                Check(boots.All(kv => Sha(SimulatedConsole.Dev(c, kv.Key)) == kv.Value), "the startup stages and kernel were never written");
                // wrong source for the kind
                var other = Console_("modified", HwidB); var ob = Backup(other, SettingsIn(NewDir("composer-other"), false));
                var (r1, w1) = ComposerEngine.Plan(con, ctx, "rebuild", new[] { new ComposeItem { SourceBackup = ob.PrimaryPath, SourcePath = save } });
                var (r2, w2) = ComposerEngine.Plan(con, ctx, "migrate", new[] { new ComposeItem { SourceBackup = a.PrimaryPath, SourcePath = save } });
                Check(r1 == null && w1.Contains("another console") && r2 == null && w2.Contains("this console"), "rebuild takes only this console's backups; migration only another console's (" + w1 + " / " + w2 + ")");
                // migration: another console's save and Classic Cart Studio data; this console keeps its own startup and identity
                SimulatedConsole.FileStore.Write(other, save, Encoding.ASCII.GetBytes("the other console's save"));
                var ob2 = Backup(other, SettingsIn(NewDir("composer-other2"), false));
                var con2 = RecoverySessions.OpenSimulated(c, ImageKind.Files);
                var (mp, mw) = ComposerEngine.Plan(con2, ctx, "migrate", new[] { new ComposeItem { SourceBackup = ob2.PrimaryPath, SourcePath = save }, new ComposeItem { SourceBackup = ob2.PrimaryPath, SourcePath = "hakchi/rootfs/etc/preinit" } });
                var mo = mp == null ? null : ComposerEngine.Execute(con2, mp, mp.Digest, mp.Nonce, ctx);
                Check(mo != null && mo.Ok && con2.FileStat("compose", save).bytes == "the other console's save".Length && mp.Notes.Any(n => n.Contains("keeps its own startup")) && mp.Skipped.Count == 1,
                    "migrated: the other console's save moved; its system files did not; this console keeps its own startup and identity (" + (mw ?? mo?.Problem) + ")");
                Check(boots.All(kv => Sha(SimulatedConsole.Dev(c, kv.Key)) == kv.Value) && ConsoleIdentity.SameConsole(con2.Identify().Hwid, HwidA), "after migration this console's startup stages, kernel and identity are its own");
                Check(CcsBackup.Gate.ConsoleSessions && CcsBackup.Gate.ComposerWrites && RecoverySessions.Refusal(ImageKind.Files) == null, "a real console: Console Composer's boundary is open in this version (asked of the boundary itself: no test opens a real session or lists USB devices)");
            }
            finally { SimulatedConsole.FileStore = null; }
        }
    }
}
