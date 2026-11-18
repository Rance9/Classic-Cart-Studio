// Classic Cart Studio console backup and restore engine tests: partial restore (Backup Repair, D6). GPL-3.0-or-later. Simulated consoles.
using CcsBackup.Device;
using CcsBackup.Engine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        public static void PartialRestore()
        {
            var s = SettingsIn(NewDir("partial"), false);
            var c = Console_("modified"); var a = Backup(c, s);
            var nandd0 = Sha(SimulatedConsole.Dev(c, "nandd"));
            using (var f = new FileStream(SimulatedConsole.Dev(c, "nandd"), FileMode.Open, FileAccess.Write)) { f.Position = 512; f.Write(new byte[] { 5, 5, 5 }, 0, 3); }
            File.Copy(Path.Combine(Fx, "data-later.img"), SimulatedConsole.Dev(c, "nandc"), true);
            var nandcNow = Sha(SimulatedConsole.Dev(c, "nandc"));
            var con = RecoverySessions.OpenSimulated(c, ImageKind.Restore); var ctx = Ctx(s, ImageKind.Restore);
            var (p, r) = RestoreEngine.Prepare(con, a.PrimaryPath, ctx, new HashSet<string> { "nandd" });
            Check(r == null && p.Partial && p.Writable && p.Steps.Single(x => x.Target == "nandd").Action == "write" && p.Steps.Single(x => x.Target == "nandc").Action == "skipped" && p.Notes.Any(n => n.Contains("not chosen")),
                "partial restore: only the chosen part is written; the other differing part is listed as skipped (" + r?.Problem + ")");
            var o = RestoreEngine.Execute(con, p, p.Digest, p.Nonce, ctx);
            Check(o.Ok && o.Written.SequenceEqual(new[] { "nandd" }) && Sha(SimulatedConsole.Dev(c, "nandd")) == nandd0 && Sha(SimulatedConsole.Dev(c, "nandc")) == nandcNow,
                "restored: the chosen part is the backup's again, the skipped part is untouched (" + o.Problem + ")");
            var boot = File.ReadAllBytes(SimulatedConsole.Dev(c, "boot2")); boot[4096] ^= 1; File.WriteAllBytes(SimulatedConsole.Dev(c, "boot2"), boot);
            var con2 = RecoverySessions.OpenSimulated(c, ImageKind.Restore);
            var (p2, r2) = RestoreEngine.Prepare(con2, a.PrimaryPath, ctx, new HashSet<string> { "boot2" });
            Check(p2 != null && !p2.Writable && p2.Refusals.Any(x => x.Contains("together")), "the startup software is never restored without the system part (" + string.Join("; ", p2?.Refusals ?? new List<string>()) + r2?.Problem + ")");
            var (p3, _) = RestoreEngine.Prepare(con2, a.PrimaryPath, ctx, new HashSet<string> { "nandz" });
            Check(p3 != null && !p3.Writable && p3.Refusals.Any(x => x.Contains("not in this backup")), "a part that is not in the backup cannot be chosen");
        }
    }
}
