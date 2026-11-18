// Classic Cart Studio console backup and restore engine tests: PAR2 parity for a finished backup and the one-file copy (Repair/Parity.cs).
// GPL-3.0-or-later. Needs the parity library: ccs-par2.dll beside this test program (build_host.sh --dll-only); skipped without it.
using CcsBackup.Archive;
using CcsBackup.Repair;
using System;
using System.IO;
using System.Linq;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        public static void ParityRuns()
        {
            if (!Par2Native.Available) throw new SkipException("no parity library (put ccs-par2.dll beside the test program): " + Par2Native.UnavailableReason);
            var dir = NewDir("parity"); var s = SettingsIn(Path.Combine(dir, "folders"), false);
            var c = Console_("stock"); var a = Backup(c, s);
            var bak = a.PrimaryPath; var orig = Sha(bak);
            var p = Parity.Protect(bak, 10);
            Check(p.Ok && File.Exists(Parity.IndexOf(bak)) && File.Exists(Parity.RecordOf(bak)) && Sha(bak) == orig && Parity.FilesOf(bak).Count >= 2,
                "parity made for a complete backup (index and volume beside it), checked at once and recorded; the backup unchanged (" + p.Text + ")");
            var rec = Json.Parse(File.ReadAllText(Parity.RecordOf(bak)));
            Check(rec.S("schema") == Parity.RecordSchema && rec.S("backup_sha256") != null && rec.L("percent") == 10 && rec.L("recovery_blocks") > 0 && rec.S("check") == "Ok", "the record names the backup, the parameters and the check result");
            Check(!Parity.Protect(bak, 10).Ok, "parity is never made twice over existing parity files (nothing replaced)");
            Check(Parity.Check(bak).Ok, "an intact backup matches its parity");
            // a damaged copy (with its parity beside it): rebuilt into a new file that passes the full check; the damaged file unchanged
            var dmg = Path.Combine(dir, "damaged"); Directory.CreateDirectory(dmg);
            var copy = Path.Combine(dmg, Path.GetFileName(bak)); File.Copy(bak, copy);
            foreach (var f in Parity.FilesOf(bak)) File.Copy(f, Path.Combine(dmg, Path.GetFileName(f)));
            using (var f = new FileStream(copy, FileMode.Open, FileAccess.ReadWrite)) { foreach (var at in new[] { 5_000_000L, 40_000_000L }) { f.Position = at; var b = new byte[3000]; f.Read(b, 0, b.Length); for (int i = 0; i < b.Length; i++) b[i] ^= 0x5A; f.Position = at; f.Write(b, 0, b.Length); } }
            var dsha = Sha(copy);
            Check(!Verifier.VerifyFile(copy).Ok && Parity.Check(copy).Report.Status == Par2Status.DamagedRepairable, "the damaged copy fails its full check and its parity says it can be rebuilt");
            var r = Parity.Rebuild(copy);
            Check(r.Ok && r.OutputPath != null && Sha(r.OutputPath) == orig && Sha(copy) == dsha && r.OutputCheck.Ok, "rebuilt from the parity into a new file identical to the original backup; the damaged file is unchanged (" + r.Text + ")");
            // the one-file copy: made, unpacked into a new folder (checked part by part, against the parity and in full)
            var tdir = Path.Combine(dir, "transport"); Directory.CreateDirectory(tdir);
            var t = Parity.MakeTransport(bak, tdir);
            Check(t.Ok && File.Exists(t.OutputPath), "a one-file copy with parity is made (" + t.Text + ")");
            var un = Path.Combine(dir, "unpacked"); Directory.CreateDirectory(un);
            var u = Parity.Unpack(t.OutputPath, un);
            Check(u.Ok && Sha(u.OutputPath) == orig, "unpacked: the backup matches its parity and passes its full check (" + u.Text + ")");
            Check(!Parity.Unpack(t.OutputPath, un).Ok && Sha(u.OutputPath) == orig, "unpacking again into the same folder writes nothing over the files already there");
            // a damaged one-file copy is refused before anything is kept
            var bad = Path.Combine(dir, "bad.zip"); File.Copy(t.OutputPath, bad);
            using (var f = new FileStream(bad, FileMode.Open, FileAccess.ReadWrite)) { f.Position = 10_000_000; f.WriteByte(0x00); f.Position = 10_000_001; f.WriteByte(0xFF); }
            var un2 = Path.Combine(dir, "unpacked2"); Directory.CreateDirectory(un2);
            var u2 = Parity.Unpack(bad, un2);
            Check(u2.Ok && Sha(u2.OutputPath) == orig && u2.Text.Contains("Damaged on the way"), "a one-file copy damaged on the way is unpacked and the backup rebuilt from its parity, identical to the original (" + u2.Text + ")");
            Directory.Delete(dir, true);
        }
    }
}
