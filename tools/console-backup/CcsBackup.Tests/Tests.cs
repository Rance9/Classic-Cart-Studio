// Classic Cart Studio console backup and restore engine tests. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsBackup.Engine;
using CcsBackup.Ext4;
using CcsBackup.Library;
using CcsWorker.Shell;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        // ------------------------------------------------------------------ the physical boundaries
        public static void Gate()
        {
            // this version: every boundary constant is open. Nothing here lists or opens a USB device: no test asks for a real session
            // (RecoverySessions.OpenReal, DeveloperTestBoot.OpenReal) or sends the worker a request for a real console; the boundaries are asked
            // directly (RecoverySessions.Refusal, DeveloperTestBoot.Refusal touch no device), and the per-write checks run on simulated shells.
            var gates = typeof(CcsBackup.Gate).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(bool)).ToList();
            var named = new[] { "ConsoleSessions", "RestoreWrites", "PreparationWrites", "ReturnWrites", "RescueWrites", "ExpertWrites", "DeveloperWrites", "DeveloperTestBoot", "ComposerWrites", "CleanupWrites", "MethodWrites", "FirmwareWrites", "DriverChanges", "StorageRawReads", "StorageDiskWrites", "CommunityWrites" };
            Check(gates.Count == named.Length && named.All(n => gates.Any(f => f.Name == n)) && gates.All(f => (bool)f.GetRawConstantValue()),
                  "Gate.cs has exactly the 16 named boundary constants and every one is open in this version (closed: " + string.Join(", ", gates.Where(f => !(bool)f.GetRawConstantValue()).Select(f => f.Name)) + ")");
            foreach (var k in (ImageKind[])Enum.GetValues(typeof(ImageKind)))
                Check(RecoverySessions.Refusal(k) == null, "a real " + k + " session passes the boundaries in this version (its own checks, plan and confirmation still apply)");
            Check(RecoverySessions.KindOf("backup") == ImageKind.Backup && RecoverySessions.KindOf("browse") == ImageKind.Backup
                  && new[] { "restore-prepare", "prepare-plan", "return-plan", "method-plan", "firmware-plan", "community", "rescue", "files", "compose", "home-design", "sega-pictures", "no-such-request" }.All(x => RecoverySessions.KindOf(x) != ImageKind.Backup),
                  "only Back Up Console and the Console Browser check-up use the backup image (which has no write verb); every other request opens another kind");
            Check(DeveloperTestBoot.Refusal() == null, "a real test boot passes its own boundary in this version (its plan and confirmation still apply)");
            // a restore session of a console that is not simulated (here a simulated shell, so only PC files change): the boundary is asked per
            // write, with the reviewed plan's targets and the console's identity; a target outside the plan or another console is refused before
            // anything is sent, and a closed boundary still refuses
            var dir = NewDir("gate"); var c = Console_("modified-noorig");
            var shell = new SimulatedConsole(c); SimulatedConsole.Boot(c, RecoveryImages.IdOf(ImageKind.Restore).id, SessionProfile.Restore);
            var real = new RecoveryConsole(shell, SessionProfile.Restore, false); real.Identify();
            var md5 = Md5(new byte[1024 * 1024]);
            foreach (var (why, a) in new[] { ("a target outside the reviewed plan", new WriteAuthority("x", real.Hwid, new[] { "nande" }, () => CcsBackup.Gate.RestoreWrites, CcsBackup.Gate.RestoreText)),
                                             ("another console", new WriteAuthority("x", HwidB, new[] { "nandd" }, () => CcsBackup.Gate.RestoreWrites, CcsBackup.Gate.RestoreText)),
                                             ("a boundary that is closed", new WriteAuthority("x", real.Hwid, new[] { "nandd" }, () => false, CcsBackup.Gate.RestoreText)) })
            {
                try { real.Write("nandd", new MemoryStream(new byte[1024 * 1024]), 1024 * 1024, md5, a); Check(false, "a restore write with " + why + " must be refused"); }
                catch (Exception ex) when (ex is GateClosedException || ex is InvalidOperationException) { Check(!File.Exists(Path.Combine(c, "writes.log")), "a write with " + why + " is refused before anything is sent (" + ex.Message + ")"); }
            }
            var auth = new WriteAuthority("x", real.Hwid, new[] { "nandd" }, () => CcsBackup.Gate.RestoreWrites, CcsBackup.Gate.RestoreText);
            real.Write("nandd", new MemoryStream(new byte[1024 * 1024]), 1024 * 1024, md5, auth);
            Check(File.Exists(Path.Combine(c, "writes.log")), "with the boundary open, the reviewed plan's own target on the same console is written (here: the simulated shell's file)");
            // a backup session cannot write at all
            c = Console_("modified-noorig");   // a fresh console: the backup session's own
            var bshell = new SimulatedConsole(c); SimulatedConsole.Boot(c, RecoveryImages.IdOf(ImageKind.Backup).id, SessionProfile.Backup);
            var b = new RecoveryConsole(bshell, SessionProfile.Backup, true); b.Identify();
            try { b.Write("nandd", new MemoryStream(new byte[16]), 16, new string('0', 32), new WriteAuthority("x", b.Hwid, new[] { "nandd" }, () => true, "")); Check(false, "a backup session must not write"); }
            catch (InvalidOperationException) { Check(!File.Exists(Path.Combine(c, "writes.log")), "a backup session refuses any write before anything is sent"); }
            var o = new MemoryStream(); int code = bshell.Execute(RecoveryConsole.Helper + " write nandd 16 " + new string('0', 32), new MemoryStream(new byte[16]), o, o, 1000);
            Check(code == 13, "the backup image itself refuses the write verb (status 13)");
            // the typed protocol never sends anything but a verb and a fixed target
            try { b.Read("../../etc/passwd", new MemoryStream(), -1, null, null); Check(false, "unknown targets must be refused"); }
            catch (ArgumentException) { Check(true, "a target outside the fixed list is refused by the PC before anything is sent"); }
        }

        // ------------------------------------------------------------------ the ZIP layer
        public static void Zip()
        {
            var dir = NewDir("zip");
            foreach (var z64 in new[] { false, true })
            {
                var p = Path.Combine(dir, "t" + (z64 ? "64" : "32") + ".zip");
                var payload = new byte[3 * 1024 * 1024]; new Random(5).NextBytes(payload);
                using (var f = new FileStream(p, FileMode.CreateNew, FileAccess.ReadWrite))
                {
                    var w = new ZipWriter(f, new DateTime(2026, 11, 14, 8, 0, 0), z64);
                    w.Add("restore/nandc.img", false, s => s.Write(payload, 0, payload.Length));
                    w.Add("README.txt", true, s => { var b = Encoding.UTF8.GetBytes(new string('x', 5000)); s.Write(b, 0, b.Length); });
                    w.Finish();
                }
                using (var r = ZipReader.Open(p, BackupFormat.Allowed))
                {
                    var e = r.Find("restore/nandc.img"); var m = new MemoryStream(); using (var st = r.OpenMember(e)) st.CopyTo(m);
                    Check(m.ToArray().SequenceEqual(payload) && r.Entries.Count == 2, "ZIP" + (z64 ? "64" : "") + ": written and read back exactly");
                    var win = new MemoryStream(); r.StoredWindow(e).CopyTo(win); Check(win.ToArray().SequenceEqual(payload), "a stored member opens in place (random access, no extraction)");
                }
                var py = Py("import zipfile,hashlib,sys\nz=zipfile.ZipFile(r'" + p + "')\nprint(z.testzip(), len(z.infolist()), hashlib.sha256(z.read('restore/nandc.img')).hexdigest(), any(i.flag_bits & 8 for i in z.infolist()))\n");
                if (py == null) Check(false, "Python (--python) is required for the independent ZIP check");
                else Check(py.Value.output.Trim() == "None 2 " + Sha(payload) + " False", "Python's zipfile reads the " + (z64 ? "ZIP64 " : "") + "file independently: " + py.Value.output.Trim());
            }
            // hostile and malformed archives, made by Python's zipfile (an independent writer) and by patching bytes
            var cases = new List<(string name, string py)>
            {
                ("traversal", "z.writestr('../evil.txt', b'x')"),
                ("absolute", "z.writestr('/etc/passwd', b'x')"),
                ("backslash", "i=zipfile.ZipInfo('x'); i.filename='restore\\\\nandc.img'; z.writestr(i, b'x')"),
                ("unlisted name", "z.writestr('restore/other.bin', b'x')"),
                ("duplicate", "z.writestr('README.txt', b'a'); z.writestr('README.txt', b'b')"),
                ("duplicate by case", "z.writestr('README.txt', b'a'); z.writestr('readme.TXT', b'b')"),
                ("symlink member", "i=zipfile.ZipInfo('README.txt'); i.create_system=3; i.external_attr=(0o120777<<16); z.writestr(i, b'/etc/passwd')"),
                ("folder member", "z.writestr('restore/', b'')"),
                ("bzip2 member", "z.writestr(zipfile.ZipInfo('README.txt'), b'x'*100, compress_type=zipfile.ZIP_BZIP2)"),
                ("data descriptor", "pass"),
            };
            foreach (var c in cases)
            {
                var p = Path.Combine(dir, c.name.Replace(' ', '-') + ".zip");
                var py = Py("import zipfile,io\nz=zipfile.ZipFile(r'" + p + "','w')\n" + (c.name == "data descriptor" ? "f=z.open('README.txt','w'); f.write(b'x'*10); f.close()\n" : c.py + "\n") + "z.close()\n" + (c.name == "data descriptor" ? "" : ""));
                if (py == null) { Check(false, "Python is required for the hostile-archive cases"); break; }
                if (c.name == "data descriptor") { var b = File.ReadAllBytes(p); b[6] |= 8; for (int i = 0; i < b.Length - 4; i++) if (b[i] == 0x50 && b[i + 1] == 0x4b && b[i + 2] == 1 && b[i + 3] == 2) { b[i + 8] |= 8; break; } File.WriteAllBytes(p, b); }
                try { using (ZipReader.Open(p, BackupFormat.Allowed)) { } Check(false, "hostile archive accepted: " + c.name); }
                catch (ZipFormatException ex) { Check(true, "refused before use: " + c.name + " (" + ex.Message + ")"); }
            }
            // byte-level damage of a valid archive
            var good = Path.Combine(dir, "t32.zip"); var gb = File.ReadAllBytes(good);
            void Bad(string name, byte[] bytes) { var p = Path.Combine(dir, name + ".zip"); File.WriteAllBytes(p, bytes); try { using (var r = ZipReader.Open(p, BackupFormat.Allowed)) { foreach (var e in r.Entries) using (var st = r.OpenMember(e)) st.CopyTo(Stream.Null); } Check(false, "damaged archive accepted: " + name); } catch (Exception ex) when (ex is ZipFormatException || ex is IOException || ex is InvalidDataException) { Check(true, "refused: " + name + " (" + ex.Message + ")"); } }
            Bad("trailing-bytes", gb.Concat(new byte[] { 1, 2, 3 }).ToArray());
            Bad("leading-bytes", new byte[] { 0x4d, 0x5a, 0, 0 }.Concat(gb).ToArray());
            var flip = (byte[])gb.Clone(); flip[200] ^= 0x40; Bad("payload-bit-flip (CRC)", flip);
            var trunc = gb.Take(gb.Length - 30).ToArray(); Bad("truncated", trunc);
            // overlapping members: the second local header offset pointed at the first
            var ov = (byte[])gb.Clone();   // the second member's index entry pointed at the first member's local header
            { int seen = 0; for (int i = 0; i < ov.Length - 46; i++) if (ov[i] == 0x50 && ov[i + 1] == 0x4b && ov[i + 2] == 1 && ov[i + 3] == 2 && ++seen == 2) { ov[i + 42] = 0; ov[i + 43] = 0; ov[i + 44] = 0; ov[i + 45] = 0; break; } }
            Bad("overlapping-members", ov);
            // a declared size far beyond the format's limit (a decompression bomb by declaration)
            var bomb = Path.Combine(dir, "bomb.zip");
            var pyb = Py("import zipfile\nz=zipfile.ZipFile(r'" + bomb + "','w',zipfile.ZIP_DEFLATED)\nz.writestr('ccs-console-backup.json', b'0'*(5*1000*1000))\nz.close()\n");
            if (pyb != null) { var v = Verifier.VerifyFile(bomb); Check(!v.Ok && v.Problems.Any(x => x.Contains("larger than allowed")), "a member that expands past its limit is refused before it is read: " + string.Join("; ", v.Problems)); }
        }

        // ------------------------------------------------------------------ the read-only ext4 reader against e2fsprogs' own listing
        public static void Ext4()
        {
            foreach (var n in new[] { "data-stock", "data-modified", "data-later", "data-modern" })
            {
                var listing = Json.Parse(File.ReadAllText(Path.Combine(Fx, "listing-" + n + ".json")));
                var want = (listing.A("entries") ?? new List<object>()).OfType<Dictionary<string, object>>().ToDictionary(e => e.S("path"));
                List<Ext4Entry> got; InventoryResult inv;
                using (var f = File.OpenRead(Path.Combine(Fx, n + ".img"))) { var r = new Ext4Reader(f); got = r.List(); f.Position = 0; inv = Inventory.Build(f, "nandc"); }
                var gotMap = got.ToDictionary(e => e.Path);
                Check(gotMap.Keys.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(want.Keys.OrderBy(x => x, StringComparer.Ordinal)), n + ": the same " + want.Count + " entries as debugfs (names with spaces, accents and symbols included)");
                int metaBad = 0, shaBad = 0, linkBad = 0;
                var invByPath = (inv.Document["entries"] as List<object>).OfType<JObj>().ToDictionary(e => (string)e["path"]);
                foreach (var kv in want)
                {
                    if (!gotMap.TryGetValue(kv.Key, out var g)) continue;
                    var w = kv.Value;
                    if (g.Type.ToString() != w.S("type") || Convert.ToString(g.Mode, 8).PadLeft(4, '0') != w.S("mode") || g.Uid != w.L("uid") || g.Gid != w.L("gid") || g.Inode != w.L("inode") || g.Links != w.L("links") || (g.Type == 'f' && g.Size != w.L("size"))) metaBad++;
                    if (w.S("target") != null && g.LinkTarget != w.S("target")) linkBad++;
                    if (w.S("sha256") != null && (string)invByPath[kv.Key]["sha256"] != w.S("sha256")) shaBad++;
                }
                Check(metaBad == 0, n + ": type, permissions, owner, group, inode, link count and size agree with debugfs for every entry (" + metaBad + " differ)");
                Check(linkBad == 0, n + ": link targets agree (relative, absolute and dangling links are kept as written)");
                Check(shaBad == 0, n + ": every regular file's SHA-256 agrees with debugfs' own read of it");
                var hl = got.Where(e => e.Path.EndsWith("settings.json") || e.Path.EndsWith("settings-hardlink.json")).ToList();
                Check(hl.Count == 2 && hl[0].Inode == hl[1].Inode && hl[0].Links == 2, n + ": the hard link is one inode with two names");
            }
            using (var f = File.OpenRead(Path.Combine(Fx, "data-modified.img")))
            {
                var inv = Inventory.Build(f, "nandc"); var by = (JObj)inv.Summary["files_by_owner"];
                Check(inv.HakchiPresent && inv.CcsPresent && (int)by["classic_cart_studio"] > 0 && (int)by["hakchi"] > 0 && (int)by["nintendo"] > 0 && (int)by["other"] > 0, "the inventory tells Classic Cart Studio's, hakchi's, Nintendo's and other files apart, and lists all of them");
                Check(inv.ExternalNotes.Any(x => x.Contains("USB")), "the captured hakchi settings produce the USB-storage exclusion note");
                var other = ((List<object>)inv.Document["entries"]).OfType<JObj>().First(e => (string)e["path"] == "/ccs-install-attempt.json");
                Check((string)other["owner"] == "other", "a file named like a Classic Cart Studio record outside its folder is not counted as Classic Cart Studio's");
            }
            // a damaged image is an error, never an endless walk or an out-of-range read
            var bad = File.ReadAllBytes(Path.Combine(Fx, "data-modern.img")).Take(8 * 1024 * 1024).ToArray();
            try { using (var m = new MemoryStream(bad)) new Ext4Reader(m).List(); Check(false, "a truncated file system must be refused"); }
            catch (Ext4Exception ex) { Check(true, "a truncated file system is refused with a reason: " + ex.Message); }
        }
    }
}
