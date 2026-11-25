// Classic Cart Studio console backup and restore engine tests: USB and SD storage backups (External/). GPL-3.0-or-later.
// Every source here is a temporary folder tree or a drive image FILE built by these tests (a FAT image written by FatImage below, the
// fixtures' ext4 image); no real USB drive, SD card or disk is ever read or written, and nothing needs administrator permission. Groups:
//   storage-format   the format is told apart from console backups; each verifier refuses the other's files; the scheduled check covers both
//   storage-fat      the FAT reader and the partition table reader on built images (long names, case twins, a fragmented file, a broken chain)
//   storage-files    files and folders mode: capture, ZIP64, bounded lists, links not followed, unsafe names, unreadable and changed files,
//                    refused destinations, verification and tampering, browsing, search and verified export
//   storage-large    one file over 4 GB (a sparse file) through capture, ZIP64, verification and the FAT32 4 GB restore rule
//   storage-image    whole drive image mode: an MBR image with FAT32, ext4 and an exFAT boot sector; index, browse, export, GPT, superfloppy
//   storage-repair   second copy, merge, borrowing from other backups of the same storage, truncation, salvage, recovery data
//   storage-restore  file restore plans (create, same, keep, replace) and their execution; whole-drive restores onto drive image files
//   storage-protect  the "back up these volumes now, verified" entry point, a connected drive among them (on a stand-in PC: TestsRawDisk.cs)
using CcsBackup.Archive;
using CcsBackup.Browse;
using CcsBackup.Engine;
using CcsBackup.External;
using CcsBackup.Library;
using CcsBackup.Repair;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        // ------------------------------------------------------------------ builders
        sealed class FNode { public string Name; public byte[] Data; public bool Dir, Frag; public List<FNode> Kids = new List<FNode>(); public List<uint> Chain; public string Short; }

        /// <summary>a FAT16 or FAT32 file system image written by these tests from the FAT specification (independently of the reader):
        /// long names for anything that is not an upper-case 8.3 name, a file whose name contains "frag" stored in non-adjacent clusters</summary>
        internal static byte[] FatImage(bool fat32, long totalBytes, IList<(string path, byte[] data)> files, string label = "CCSTEST", int spc = 1)
        {
            const int bps = 512; long tot = totalBytes / bps; int reserved = fat32 ? 32 : 4, nf = 2, rootEntries = fat32 ? 0 : 512, rootSecs = rootEntries * 32 / bps, ent = fat32 ? 4 : 2;
            long fatSz = 1; for (int i = 0; i < 20; i++) { long cl = (tot - reserved - nf * fatSz - rootSecs) / spc; fatSz = ((cl + 2) * ent + bps - 1) / bps; }
            long clusters = (tot - reserved - nf * fatSz - rootSecs) / spc, dataStart = reserved + nf * fatSz + rootSecs; int cb = spc * bps;
            if (fat32 ? clusters < 65525 : clusters < 4085 || clusters >= 65525) throw new ArgumentException("size does not fit the FAT kind");
            var img = new byte[totalBytes]; var fat = new uint[clusters + 2]; uint eoc = fat32 ? 0x0FFFFFFFu : 0xFFFFu; fat[0] = fat32 ? 0x0FFFFFF8u : 0xFFF8u; fat[1] = eoc; uint next = 2;
            var root = new FNode { Dir = true };
            foreach (var (path, data) in files)
            {
                var segs = path.Trim('/').Split('/'); var cur = root;
                for (int i = 0; i < segs.Length; i++)
                {
                    bool leaf = i == segs.Length - 1; var k = cur.Kids.FirstOrDefault(x => x.Name == segs[i]);
                    if (k == null) { k = new FNode { Name = segs[i], Dir = !leaf || data == null, Data = leaf ? data : null, Frag = segs[i].Contains("frag") }; cur.Kids.Add(k); }
                    cur = k;
                }
            }
            bool Plain83(string n) { var p = n.Split('.'); return p.Length <= 2 && p[0].Length >= 1 && p[0].Length <= 8 && (p.Length == 1 || p[1].Length <= 3) && n.All(c => c == '.' || char.IsUpper(c) || char.IsDigit(c) || c == '_') && n.Count(c => c == '.') <= 1; }
            int Lfn(string n) => Plain83(n) ? 0 : (n.Length + 12) / 13;
            List<uint> Alloc(long bytes, bool frag) { int n = (int)Math.Max(1, (bytes + cb - 1) / cb); var l = new List<uint>(); for (int i = 0; i < n; i++) { if (frag && i > 0) next++; l.Add(next++); } for (int i = 0; i < l.Count; i++) fat[l[i]] = i + 1 < l.Count ? l[i + 1] : eoc; return l; }
            long Off(uint c) => (dataStart + (long)(c - 2) * spc) * bps;
            void Put(List<uint> chain, byte[] d) { for (int i = 0; i < chain.Count; i++) { int n = (int)Math.Min(cb, d.Length - (long)i * cb); if (n > 0) Buffer.BlockCopy(d, i * cb, img, (int)Off(chain[i]), n); } }
            // directories first, top down (so ".." is known), then files
            void Dirs(FNode d, bool isRoot)
            {
                int count = d.Kids.Sum(k => 1 + Lfn(k.Name)) + (isRoot ? 0 : 2) + 1;
                if (!isRoot || fat32) d.Chain = Alloc(count * 32L, false);
                foreach (var k in d.Kids.Where(x => x.Dir)) Dirs(k, false);
            }
            Dirs(root, true);
            void Files(FNode d) { foreach (var k in d.Kids) { if (k.Dir) Files(k); else if (k.Data.Length > 0) { k.Chain = Alloc(k.Data.Length, k.Frag); Put(k.Chain, k.Data); } } }
            Files(root);
            ushort date = (ushort)((2026 - 1980) << 9 | 11 << 5 | 20), time = (ushort)(12 << 11 | 34 << 5 | 28);
            void Entry(MemoryStream m, string sn, byte attr, uint cl, uint size)
            {
                var e = new byte[32]; Encoding.ASCII.GetBytes(sn.PadRight(11)).CopyTo(e, 0); e[11] = attr;
                BitConverter.GetBytes(time).CopyTo(e, 14); BitConverter.GetBytes(date).CopyTo(e, 16); BitConverter.GetBytes(date).CopyTo(e, 18);
                BitConverter.GetBytes((ushort)(cl >> 16)).CopyTo(e, 20); BitConverter.GetBytes(time).CopyTo(e, 22); BitConverter.GetBytes(date).CopyTo(e, 24); BitConverter.GetBytes((ushort)cl).CopyTo(e, 26); BitConverter.GetBytes(size).CopyTo(e, 28);
                m.Write(e, 0, 32);
            }
            byte[] Listing(FNode d, uint self, uint parent, bool isRoot)
            {
                var m = new MemoryStream(); var used = new HashSet<string>();
                if (!isRoot) { Entry(m, ".", 0x10, self, 0); Entry(m, "..", 0x10, parent, 0); }
                if (isRoot && label != null) Entry(m, label, 0x08, 0, 0);
                foreach (var k in d.Kids)
                {
                    string sn;
                    if (Plain83(k.Name)) { var p = k.Name.Split('.'); sn = p[0].PadRight(8) + (p.Length > 1 ? p[1] : "").PadRight(3); }
                    else
                    {
                        var dot = k.Name.LastIndexOf('.'); string Clean(string s) => new string(s.ToUpperInvariant().Where(c => c >= 'A' && c <= 'Z' || c >= '0' && c <= '9').ToArray());
                        var b = Clean(dot > 0 ? k.Name.Substring(0, dot) : k.Name); var x = Clean(dot > 0 ? k.Name.Substring(dot + 1) : ""); if (b.Length == 0) b = "X";
                        int t = 1; do { var tail = "~" + t++; sn = (b.Length > 8 - tail.Length ? b.Substring(0, 8 - tail.Length) : b) + tail; sn = sn.PadRight(8) + (x.Length > 3 ? x.Substring(0, 3) : x).PadRight(3); } while (used.Contains(sn) || d.Kids.Any(o => o != k && Plain83(o.Name) && (o.Name.Split('.')[0].PadRight(8) + (o.Name.Contains('.') ? o.Name.Split('.')[1] : "").PadRight(3)) == sn));
                        byte sum = 0; foreach (var c in Encoding.ASCII.GetBytes(sn)) sum = (byte)(((sum & 1) << 7) + (sum >> 1) + c);
                        int n = Lfn(k.Name); var chars = k.Name.ToCharArray().Concat(new[] { '\0' }).ToList(); while (chars.Count < n * 13) chars.Add('￿');
                        for (int s = n; s >= 1; s--)
                        {
                            var e = new byte[32]; e[0] = (byte)(s | (s == n ? 0x40 : 0)); e[11] = 0x0F; e[13] = sum; int ci = (s - 1) * 13;
                            for (int q = 0; q < 5; q++) BitConverter.GetBytes(chars[ci + q]).CopyTo(e, 1 + 2 * q);
                            for (int q = 0; q < 6; q++) BitConverter.GetBytes(chars[ci + 5 + q]).CopyTo(e, 14 + 2 * q);
                            for (int q = 0; q < 2; q++) BitConverter.GetBytes(chars[ci + 11 + q]).CopyTo(e, 28 + 2 * q);
                            m.Write(e, 0, 32);
                        }
                    }
                    used.Add(sn); k.Short = sn;
                    Entry(m, sn, (byte)(k.Dir ? 0x10 : 0x20), k.Chain?[0] ?? 0, k.Dir ? 0 : (uint)k.Data.Length);
                }
                return m.ToArray();
            }
            void Lists(FNode d, uint parent, bool isRoot)
            {
                var self = d.Chain?[0] ?? 0; var b = Listing(d, self, isRoot ? 0 : parent, isRoot);
                if (isRoot && !fat32) Buffer.BlockCopy(b, 0, img, (int)((reserved + nf * fatSz) * bps), b.Length); else Put(d.Chain, b);
                foreach (var k in d.Kids.Where(x => x.Dir)) Lists(k, isRoot && !fat32 ? 0 : self, false);
            }
            Lists(root, 0, true);
            // the boot sector and the tables
            var bs = new byte[512]; bs[0] = 0xEB; bs[1] = 0x58; bs[2] = 0x90; Encoding.ASCII.GetBytes("MSWIN4.1").CopyTo(bs, 3);
            BitConverter.GetBytes((ushort)bps).CopyTo(bs, 11); bs[13] = (byte)spc; BitConverter.GetBytes((ushort)reserved).CopyTo(bs, 14); bs[16] = (byte)nf; BitConverter.GetBytes((ushort)rootEntries).CopyTo(bs, 17);
            if (tot < 65536 && !fat32) BitConverter.GetBytes((ushort)tot).CopyTo(bs, 19); else BitConverter.GetBytes((uint)tot).CopyTo(bs, 32);
            bs[21] = 0xF8; BitConverter.GetBytes((ushort)63).CopyTo(bs, 24); BitConverter.GetBytes((ushort)255).CopyTo(bs, 26);
            if (fat32) { BitConverter.GetBytes((uint)fatSz).CopyTo(bs, 36); BitConverter.GetBytes(2u).CopyTo(bs, 44); BitConverter.GetBytes((ushort)1).CopyTo(bs, 48); BitConverter.GetBytes((ushort)6).CopyTo(bs, 50); bs[64] = 0x80; bs[66] = 0x29; BitConverter.GetBytes(0x1234ABCDu).CopyTo(bs, 67); Encoding.ASCII.GetBytes((label ?? "NO NAME").PadRight(11)).CopyTo(bs, 71); Encoding.ASCII.GetBytes("FAT32   ").CopyTo(bs, 82); }
            else { BitConverter.GetBytes((ushort)fatSz).CopyTo(bs, 22); bs[36] = 0x80; bs[38] = 0x29; BitConverter.GetBytes(0x5678EF01u).CopyTo(bs, 39); Encoding.ASCII.GetBytes((label ?? "NO NAME").PadRight(11)).CopyTo(bs, 43); Encoding.ASCII.GetBytes("FAT16   ").CopyTo(bs, 54); }
            bs[510] = 0x55; bs[511] = 0xAA; Buffer.BlockCopy(bs, 0, img, 0, 512);
            if (fat32) { Buffer.BlockCopy(bs, 0, img, 6 * 512, 512); BitConverter.GetBytes(0x41615252u).CopyTo(img, 512); BitConverter.GetBytes(0x61417272u).CopyTo(img, 512 + 484); BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(img, 512 + 488); BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(img, 512 + 492); BitConverter.GetBytes(0xAA550000u).CopyTo(img, 512 + 508); }
            for (int c = 0; c < nf; c++) for (long i = 0; i < fat.Length; i++) { long at = (reserved + c * fatSz) * bps + i * ent; if (fat32) BitConverter.GetBytes(fat[i]).CopyTo(img, (int)at); else BitConverter.GetBytes((ushort)fat[i]).CopyTo(img, (int)at); }
            return img;
        }

        static void Mbr(byte[] disk, uint sig, params (byte type, long lba, long sectors)[] parts)
        {
            BitConverter.GetBytes(sig).CopyTo(disk, 440);
            for (int i = 0; i < parts.Length; i++) { int e = 446 + 16 * i; disk[e + 4] = parts[i].type; BitConverter.GetBytes((uint)parts[i].lba).CopyTo(disk, e + 8); BitConverter.GetBytes((uint)parts[i].sectors).CopyTo(disk, e + 12); }
            disk[510] = 0x55; disk[511] = 0xAA;
        }

        static byte[] Rnd(int n, int seed) { var b = new byte[n]; new Random(seed).NextBytes(b); return b; }
        static string ShaB(byte[] b) { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(b)); }

        /// <summary>the test drive image: MBR, FAT32 (36 MiB, files below), the fixtures' ext4 image (64 MiB), an exFAT boot sector (2 MiB)</summary>
        static (string path, List<(string, byte[])> fatFiles) DiskA(string dir, int seed = 1)
        {
            var files = new List<(string, byte[])> { ("/README.TXT", Encoding.ASCII.GetBytes("hello from a FAT32 partition\r\n")), ("/Long File Name.txt", Rnd(5000, seed)), ("/games/Twin.txt", Encoding.ASCII.GetBytes("lower")), ("/games/TWIN.TXT", Encoding.ASCII.GetBytes("upper")),
                ("/games/frag-split.bin", Rnd(9000, seed + 1)), ("/games/deep/ünicode 名前.bin", Rnd(70000, seed + 2)), ("/empty", null), ("/hakchi/games/CLV-Z-ABCDE/CLV-Z-ABCDE.desktop", Encoding.ASCII.GetBytes("[Desktop Entry]\nName=Test Game\nCode=CLV-Z-ABCDE\nExec=/bin/fceumm /var/games/CLV-Z-ABCDE/game.nes\n")), ("/hakchi/games/CLV-Z-ABCDE/game.nes", Concat(Encoding.ASCII.GetBytes("NES\x1a"), Rnd(16380, seed + 3))) };
            var fat = FatImage(true, 36L << 20, files, "CCSFAT32");
            var ext = File.ReadAllBytes(Path.Combine(Fx, "data-modern.img"));
            long fatLba = 2048, extLba = fatLba + fat.Length / 512, exLba = extLba + ext.Length / 512; long total = (exLba + 4096) * 512;
            var disk = new byte[total]; Buffer.BlockCopy(fat, 0, disk, (int)(fatLba * 512), fat.Length); Buffer.BlockCopy(ext, 0, disk, (int)(extLba * 512), ext.Length);
            var ex = new byte[512]; ex[0] = 0xEB; ex[1] = 0x76; ex[2] = 0x90; Encoding.ASCII.GetBytes("EXFAT   ").CopyTo(ex, 3); BitConverter.GetBytes(0xC0FFEE11u).CopyTo(ex, 100); ex[510] = 0x55; ex[511] = 0xAA; Buffer.BlockCopy(ex, 0, disk, (int)(exLba * 512), 512);
            Mbr(disk, 0xCC5A0001u + (uint)seed, (0x0C, fatLba, fat.Length / 512), (0x83, extLba, ext.Length / 512), (0x07, exLba, 4096));
            var p = Path.Combine(dir, "drive-a-" + seed + ".img"); File.WriteAllBytes(p, disk);
            return (p, files);
        }
        static byte[] Concat(byte[] a, byte[] b) => a.Concat(b).ToArray();

        internal static Settings StorageSettings(string dir, bool second) => SettingsIn(dir, second);

        static string Tree(string dir)
        {
            var src = Path.Combine(dir, "src"); Directory.CreateDirectory(Path.Combine(src, "dir", "sub")); Directory.CreateDirectory(Path.Combine(src, "empty"));
            File.WriteAllText(Path.Combine(src, "a.txt"), "alpha"); File.WriteAllBytes(Path.Combine(src, "dir", "b.bin"), Rnd(3_000_000, 7)); File.WriteAllText(Path.Combine(src, "dir", "sub", "c.txt"), "charlie");
            File.WriteAllBytes(Path.Combine(src, "big.bin"), Rnd(20_000_000, 8));
            File.WriteAllText(NativeFiles.Long(Path.Combine(src, "trailing dot.")), "a name Windows cannot create through ordinary paths");
            File.WriteAllText(NativeFiles.Long(Path.Combine(src, "CON.txt")), "a reserved device name");
            var hid = Path.Combine(src, "hidden.txt"); File.WriteAllText(hid, "hidden and read-only"); File.SetAttributes(hid, FileAttributes.Hidden | FileAttributes.ReadOnly);
            File.SetLastWriteTimeUtc(Path.Combine(src, "a.txt"), new DateTime(2019, 3, 4, 5, 6, 7, 890, DateTimeKind.Utc)); File.SetCreationTimeUtc(Path.Combine(src, "a.txt"), new DateTime(2018, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            var outside = Path.Combine(dir, "outside"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "secret.txt"), "must never be captured through the link");
            var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + Path.Combine(src, "link") + "\" \"" + outside + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }); p.StandardOutput.ReadToEnd(); p.WaitForExit();
            return src;
        }

        static StorageOutcome StRun(string path, Settings s, string mode = "files", Action<StorageOptions> tune = null, List<string> folders = null)
        {
            var o = new StorageOptions { Settings = s, Entry = "tests", ToolName = "CcsBackup.Tests" }; tune?.Invoke(o);
            return StorageBackup.Run(new[] { new StorageRequest { Path = path, Mode = mode, Folders = folders } }, o);
        }

        static void StFlip(string file, long at, int n = 64) { using (var f = new FileStream(file, FileMode.Open, FileAccess.ReadWrite)) { f.Position = at; var b = new byte[n]; f.Read(b, 0, n); for (int i = 0; i < n; i++) b[i] ^= 0x5A; f.Position = at; f.Write(b, 0, n); } }
        static ZipEntryInfo StEntry(string backup, string name) { using (var z = ZipReader.Open(backup, ExternalFormat.Allowed, ExternalFormat.Limits)) return z.Entries.First(e => e.Name == name); }

        // ------------------------------------------------------------------ storage-format
        public static void StorageFormat()
        {
            var dir = NewDir("st-format"); var s = StorageSettings(Path.Combine(dir, "loc"), false); var src = Tree(dir);
            var o = StRun(src, s);
            Check(o.Ok && o.PrimaryPath != null && Path.GetDirectoryName(o.PrimaryPath) == StorageBackup.Folder(s), "a storage backup is written to the backup location's own USB and SD Backups folder (" + o.Problem + ")");
            Check(ExternalFormat.Is(o.PrimaryPath), "the file is recognized as a USB and SD backup by its first members");
            var con = Backup(Console_("modified-noorig"), s);
            Check(con.Ok && !ExternalFormat.Is(con.PrimaryPath), "a console backup is never recognized as a USB and SD backup");
            Check(!Verifier.VerifyFile(o.PrimaryPath).Ok, "the console backup's verifier refuses a storage backup (its member names are not on the console format's list)");
            Check(!StorageVerifier.Verify(con.PrimaryPath).Ok, "the storage verifier refuses a console backup");
            Check(!History.List(s).Any(h => (h.PrimaryPath ?? "").Contains(ExternalFormat.FolderName)) && !BackupList.List(s).Any(r => r.Path.Contains(ExternalFormat.FolderName)), "console backup lists never show storage backups as console backups");
            Check(AnyBackup.Verify(o.PrimaryPath).Ok && AnyBackup.Verify(con.PrimaryPath).Ok, "the shared dispatch checks each kind with its own verifier");
            var rep = Path.Combine(dir, "reports");
            Check(ScheduledCheck.RunCheck(s, rep).Count == 0, "the scheduled check passes a folder holding both kinds");
            var copy = Path.Combine(StorageBackup.Folder(s), "damaged copy.zip"); File.Copy(o.PrimaryPath, copy); StFlip(copy, new FileInfo(copy).Length / 2);
            var probs = ScheduledCheck.RunCheck(s, rep);
            Check(probs.Count == 1 && probs[0].StartsWith("damaged copy.zip"), "the scheduled check reports a damaged storage backup by name (" + string.Join(" | ", probs) + ")");
            File.Delete(copy);
            var lst = StorageLibrary.List(s);
            Check(lst.Count == 1 && lst[0].Status == "complete" && lst[0].Sources.Count == 1, "the storage list shows the backup as complete and checked (" + string.Join(",", lst.Select(x => x.Status)) + ")");
            // the reader's limits: unchanged for console backups (256 members), larger only where this format asks for them
            var many = new MemoryStream(); var zw = new ZipWriter(many, DateTime.Now, true); for (int i = 0; i < 300; i++) zw.Add("m/" + i.ToString("D3") + ".bin", false, x => x.WriteByte(1), true); zw.Finish();
            bool consoleRefused = false; try { new ZipReader(many, n => true); } catch (ZipFormatException) { consoleRefused = true; }
            Check(consoleRefused && new ZipReader(many, n => true, false, ExternalFormat.Limits).Entries.Count == 300, "a 300-member archive is refused under the console backup's limits and read under this format's");
        }

        // ------------------------------------------------------------------ storage-fat
        public static void StorageFat()
        {
            var dir = NewDir("st-fat");
            var (disk, files) = DiskA(dir);
            using (var f = File.OpenRead(disk))
            {
                var lay = PartitionTable.Read(f);
                Check(lay.Scheme == "mbr" && lay.Parts.Count == 3, "the MBR's three partitions are read (" + lay.Scheme + ", " + lay.Parts.Count + ")");
                Check(lay.Parts[0].FileSystem == "FAT32" && lay.Parts[0].Label == "CCSFAT32" && lay.Parts[0].Browsable, "partition 1 is FAT32 by its own boot sector, with its label");
                Check(lay.Parts[1].FileSystem == "ext4" && lay.Parts[1].Browsable, "partition 2 is ext4 by its superblock (" + lay.Parts[1].FileSystem + ")");
                Check(lay.Parts[2].FileSystem == "exFAT" && !lay.Parts[2].Browsable && lay.Parts[2].NotBrowsable.Contains("cannot list or browse"), "partition 3 is exFAT: named, kept, and plainly not browsable");
                var fat = new Fat(f, lay.Parts[0].Offset, lay.Parts[0].Length); var list = fat.List();
                var names = list.Select(x => x.Path).ToList();
                Check(names.Contains("/Long File Name.txt") && names.Contains("/games/deep/ünicode 名前.bin") && names.Contains("/empty"), "long names (Unicode included) and an empty folder are listed");
                Check(names.Contains("/games/Twin.txt") && names.Contains("/games/TWIN.TXT"), "two names that differ only in case are both listed");
                foreach (var (p, data) in files.Where(x => x.Item2 != null))
                {
                    var e = list.FirstOrDefault(x => x.Path == p); var m = new MemoryStream(); if (e != null) fat.ReadFile(e, m);
                    Check(e != null && e.Problem == null && m.ToArray().SequenceEqual(data), "FAT file " + p + " reads back exactly (" + data.Length + " bytes)");
                }
                var frag = list.First(x => x.Name == "frag-split.bin");
                Check(fat.Extents(frag).Count > 1, "a file in non-adjacent clusters has several extents (" + fat.Extents(frag).Count + ")");
                Check(list.First(x => x.Name == "README.TXT").Modified == new DateTime(2026, 11, 20, 12, 34, 56), "a FAT time is kept exactly as recorded (local, no time zone)");
            }
            // a broken chain: one file's FAT entry made to loop; that file is unreadable with the reason, the rest still list
            var bad = Path.Combine(dir, "loop.img"); var img = FatImage(false, 16L << 20, new List<(string, byte[])> { ("/A.BIN", Rnd(3000, 4)), ("/B.BIN", Rnd(2000, 5)) }, "LOOP");
            using (var ms = new MemoryStream(img)) { var fat = new Fat(ms, 0, img.Length); var a = fat.List().First(x => x.Name == "A.BIN"); int fatAt = 4 * 512; BitConverter.GetBytes((ushort)a.FirstCluster).CopyTo(img, fatAt + 2 * (int)(a.FirstCluster + 2)); }
            File.WriteAllBytes(bad, img);
            using (var f = File.OpenRead(bad))
            {
                var lay = PartitionTable.Read(f); var fat = new Fat(f, 0, f.Length); var list = fat.List();
                Check(lay.Scheme == "none" && lay.Parts[0].FileSystem == "FAT16", "a FAT16 file system without a partition table is read as the whole drive");
                Check(list.First(x => x.Name == "A.BIN").Problem != null && list.First(x => x.Name == "B.BIN").Problem == null, "a looping cluster chain makes that file unreadable with the reason; the others are read (" + list.First(x => x.Name == "A.BIN").Problem + ")");
            }
            // GPT: one partition pointing at a FAT16 file system
            var gdisk = new byte[20L << 20]; var g16 = FatImage(false, 16L << 20, new List<(string, byte[])> { ("/GPT.TXT", Encoding.ASCII.GetBytes("gpt")) }, "GPTFAT"); Buffer.BlockCopy(g16, 0, gdisk, 2048 * 512, g16.Length);
            gdisk[510] = 0x55; gdisk[511] = 0xAA; gdisk[446 + 4] = 0xEE; Encoding.ASCII.GetBytes("EFI PART").CopyTo(gdisk, 512); BitConverter.GetBytes(2L).CopyTo(gdisk, 512 + 72); BitConverter.GetBytes(128u).CopyTo(gdisk, 512 + 80); BitConverter.GetBytes(128u).CopyTo(gdisk, 512 + 84);
            Guid.NewGuid().ToByteArray().CopyTo(gdisk, 512 + 56); Guid.NewGuid().ToByteArray().CopyTo(gdisk, 1024); Guid.NewGuid().ToByteArray().CopyTo(gdisk, 1024 + 16); BitConverter.GetBytes(2048L).CopyTo(gdisk, 1024 + 32); BitConverter.GetBytes(2048L + g16.Length / 512 - 1).CopyTo(gdisk, 1024 + 40);
            using (var ms = new MemoryStream(gdisk)) { var lay = PartitionTable.Read(ms); Check(lay.Scheme == "gpt" && lay.Parts.Count == 1 && lay.Parts[0].FileSystem == "FAT16" && lay.Parts[0].Offset == 2048 * 512, "a GPT drive's partition is found and read by its own boot sector"); }
            // a partition entry outside the drive is listed with the reason, never read
            var odd = new byte[4L << 20]; Mbr(odd, 1, (0x0C, 2048, 1_000_000)); using (var ms = new MemoryStream(odd)) { var lay = PartitionTable.Read(ms); Check(lay.Parts.Count == 1 && lay.Parts[0].Problem != null && !lay.Parts[0].Browsable, "a partition entry that lies outside the drive is listed, not read"); }
        }

        // ------------------------------------------------------------------ storage-files
        public static void StorageFiles()
        {
            var dir = NewDir("st-files"); var s = StorageSettings(Path.Combine(dir, "loc"), true); var src = Tree(dir);
            var est = StorageBackup.Estimate(new[] { new StorageRequest { Path = src } }, StorageBackup.Folder(s), true, 10);
            var fileSizes = Directory.GetFiles(NativeFiles.Long(src), "*", SearchOption.AllDirectories).Where(x => !x.Contains("\\link\\")).Sum(x => new FileInfo(x).Length);
            Check(est.DataBytes == fileSizes && est.Files == 7, "the estimate is the actual selected bytes (" + est.DataBytes + " of " + fileSizes + ", " + est.Files + " files), the link's target not counted");
            Check(est.CopyBytes == est.BackupBytes && est.ParityBytes > 0 && est.WorkspaceBytes == est.BackupBytes && est.SizeNote.Contains("only the files you chose") && !est.SizeNote.Contains("1 GB"), "the estimate names the additional copy, recovery data and repair workspace, and explains used files versus a whole image");
            // a file held open by another program that denies reading: unreadable, the scope incomplete, never Protected
            StorageOutcome o;
            using (new FileStream(Path.Combine(src, "dir", "sub", "c.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                o = StRun(src, s);
            Check(o.Ok && !o.ScopeComplete && !o.Protected && o.Sources[0].Unreadable == 1 && o.Sources[0].Problems.Any(p => p.StartsWith("/dir/sub/c.txt")), "a file another program holds is listed as not read; the backup file is verified but the scope is incomplete and it is not protection (" + o.Problem + ")");
            var v = StorageVerifier.Verify(o.PrimaryPath);
            Check(v.Ok && !v.ScopeComplete, "the file passes its full check and says its scope is incomplete");
            o = StRun(src, s);
            Check(o.Ok && o.Protected && o.State == "complete" && File.Exists(o.ArchivePath) && o.Verification.Ok, "the same folder unlocked: a complete, verified backup with a verified additional copy (" + o.Problem + o.CopyProblem + ")");
            v = StorageVerifier.Verify(o.PrimaryPath);
            var rows = v.Inventory["s01"]; var byPath = rows.ToDictionary(r => r.S("path"), r => r);
            Check(v.Ok && v.FilesChecked == 7, "every file's SHA-256 is checked against its list in the verification pass (" + v.FilesChecked + ")");
            Check(byPath.ContainsKey("/link") && byPath["/link"].S("type") == "link" && !byPath.Keys.Any(k => k.Contains("secret")), "the junction is recorded as a link and never followed (its target's file is not in the backup)");
            Check(byPath["/trailing dot."].B("windows_name") == false && byPath["/CON.txt"].B("windows_name") == false && byPath["/trailing dot."].S("state") == "ok", "names Windows cannot create again are captured exactly and marked");
            Check(byPath["/empty"].S("type") == "folder" && byPath["/big.bin"].S("member").Contains("/files/file-") && byPath["/a.txt"].S("member").Contains("/files/pack-"), "an empty folder is kept; a large file has its own member, small files are packed");
            Check((byPath["/hidden.txt"].L("attributes") & 3) == 3 && byPath["/a.txt"].O("times").L("modified") == new DateTime(2019, 3, 4, 5, 6, 7, 890, DateTimeKind.Utc).ToFileTimeUtc(), "attributes and exact times are recorded");
            using (var z = ZipReader.Open(o.PrimaryPath, ExternalFormat.Allowed, ExternalFormat.Limits)) Check(z.Entries.All(e => e.Zip64Local), "every member is in the ZIP64 form (" + z.Entries.Count + " members)");
            // tampering: one byte of a packed file
            var t = Path.Combine(dir, "tampered.zip"); File.Copy(o.PrimaryPath, t); var pe = StEntry(t, byPath["/dir/b.bin"].S("member")); StFlip(t, pe.DataOffset + byPath["/dir/b.bin"].L("offset") + 1000, 1);
            var tv = StorageVerifier.Verify(t); Check(!tv.Ok, "a changed byte inside a pack fails the full check (" + tv.Problems.FirstOrDefault() + ")");
            // browse, search, by type and verified export
            var br = BackupContents.Open(o.PrimaryPath, s);
            Check(br.Ok && br.Contents.StorageLabel != null && br.Contents.Entries.Any(e => e.Name == "c.txt" && e.HashTrusted), "the Backup Browser opens a storage backup after its full check (" + br.Problem + ")");
            var c = br.Contents;
            Check(c.Search("charlie").Count == 0 && c.Search("c.txt").Count == 1 && c.Children("s01").Any(e => e.Name == "dir"), "search by name and the All files tree work");
            var exp = Path.Combine(dir, "export"); Directory.CreateDirectory(exp);
            var ex = Exporter.Export(c, ExportRequest.Folder(c, c.Find("s01"), exp));
            var outDir = ex.Folder;
            Check(ex.State == "complete" && ex.Files.Count == 7, "a verified export of the whole source (" + ex.State + ", " + ex.Files.Count + " files, " + string.Join("; ", ex.NotExported.Select(n => n.Item2)) + ")");
            Check(ex.Files.All(x => Sha(Path.Combine(outDir, x.RelativePath)) == x.Sha256), "every exported file matches its recorded checksum on disk");
            Check(ex.Files.Any(x => x.Entry.Name == "CON.txt" && !x.RelativePath.EndsWith("\\CON.txt")), "a reserved device name is exported under a name Windows accepts");
            c.Dispose();
            // the destination inside a selected folder, and a whole drive onto itself, are refused
            var inside = StRun(src, new Settings { PrimaryFolder = Path.Combine(src, "dir"), Path = Path.Combine(dir, "x.json") });
            Check(inside.State == "refused" && inside.Problem.Contains("inside a folder being backed up"), "a backup location inside a selected folder is refused (" + inside.Problem + ")");
            // the source is held: while a file is read, no other program can open it for writing
            bool refused = false;
            var held = StRun(src, s, tune: op => op.DuringFile = rel => { if (rel == "/dir/b.bin") try { using (new FileStream(Path.Combine(src, "dir", "b.bin"), FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { } } catch (IOException) { refused = true; } });
            Check(refused && held.Protected, "while a file is copied, another program cannot open it for writing (the source is held consistent)");
            // a program that already had a file open for writing changes it while it is copied: detected, the scope incomplete
            StorageOutcome changed;
            using (var other = new FileStream(Path.Combine(src, "dir", "b.bin"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
                changed = StRun(src, s, tune: op => op.DuringFile = rel => { if (rel == "/dir/b.bin") { other.Seek(0, SeekOrigin.End); other.Write(new byte[] { 1, 2, 3, 4 }, 0, 4); other.Flush(true); } });
            Check(changed.Ok && !changed.Protected && changed.Sources[0].Changed == 1, "a file another program changes while it is copied is detected; the scope is incomplete (" + string.Join("; ", changed.Sources.SelectMany(x => x.Problems)) + ")");
            var cv = StorageVerifier.Verify(changed.PrimaryPath); var cb = BackupContents.Open(changed.PrimaryPath, s);
            Check(cv.Ok && cb.Ok && cb.Contents.Entries.First(e => e.Name == "b.bin").Unreadable != null, "the changed file is listed and marked; it cannot be exported as a good copy");
            cb.Contents?.Dispose();
            // many small files: bounded list chunks
            var many = Path.Combine(dir, "many"); Directory.CreateDirectory(many); for (int i = 0; i < 9000; i++) File.WriteAllText(Path.Combine(many, "f" + i.ToString("D5") + ".txt"), "n" + i);
            var mo = StRun(many, s);
            using (var z = ZipReader.Open(mo.PrimaryPath, ExternalFormat.Allowed, ExternalFormat.Limits))
            {
                var inv = z.Entries.Where(e => e.Name.Contains("/inventory/")).ToList();
                Check(mo.Protected && inv.Count >= 3 && StorageVerifier.Verify(mo.PrimaryPath).Inventory["s01"].Count == 9000, "9,000 files are listed in " + inv.Count + " bounded chunks of at most 4,000 entries");
            }
            // chosen folders only
            var part = StRun(src, s, folders: new List<string> { "/dir/sub" });
            var pv = StorageVerifier.Verify(part.PrimaryPath);
            Check(part.Protected && pv.Inventory["s01"].Select(r => r.S("path")).OrderBy(x => x).SequenceEqual(new[] { "/dir", "/dir/sub", "/dir/sub/c.txt" }), "only the chosen folder is backed up, with the folders above it");
        }

        // ------------------------------------------------------------------ storage-large
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr i, int isz, IntPtr o, int osz, out int ret, IntPtr ov);
        public static void StorageLarge()
        {
            var dir = NewDir("st-large"); var s = StorageSettings(Path.Combine(dir, "loc"), false); var src = Path.Combine(dir, "src"); Directory.CreateDirectory(src);
            var big = Path.Combine(src, "over-4-GB.bin"); long size = 4_400_000_000L;
            try
            {
                using (var f = new FileStream(big, FileMode.CreateNew, FileAccess.ReadWrite))
                {
                    DeviceIoControl(f.SafeFileHandle, 0x000900C4, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);   // sparse: the zeros take no disk space
                    f.SetLength(size); f.Position = size - 5; f.Write(Encoding.ASCII.GetBytes("tail!"), 0, 5); f.Position = 123456789; f.Write(Encoding.ASCII.GetBytes("middle"), 0, 6);
                }
                string sha; using (var h = FastHash.Sha256()) using (var f = File.OpenRead(big)) sha = Hex.Of(h.ComputeHash(f));
                File.WriteAllText(Path.Combine(src, "small.txt"), "small");
                var sw = Stopwatch.StartNew(); var o = StRun(src, s);
                var v = o.Ok ? StorageVerifier.Verify(o.PrimaryPath) : null;
                var row = v?.Inventory["s01"].FirstOrDefault(r => r.S("path") == "/over-4-GB.bin");
                Check(o.Protected && row != null && row.S("sha256") == sha && row.L("size") == size, "a 4,400,000,000-byte file is backed up whole and its SHA-256 recorded (" + sw.Elapsed.TotalSeconds.ToString("0") + " s, " + o.Problem + ")");
                using (var z = ZipReader.Open(o.PrimaryPath, ExternalFormat.Allowed, ExternalFormat.Limits))
                { var e = z.Entries.First(x => x.Name == row.S("member")); Check(e.Size == size && e.Zip64Local && new FileInfo(o.PrimaryPath).Length > 4_294_967_296L, "its member is larger than 4 GB, in the ZIP64 form, and the file's offsets past 4 GB read back"); }
                Check(v.Ok && v.FilesChecked == 2, "the full check streams it and checks its SHA-256 (" + v.FilesChecked + " files)");
                // restoring it onto a FAT32 drive is refused for that file by name (FAT32 holds files up to 4 GB)
                var tgt = Path.Combine(dir, "target"); Directory.CreateDirectory(tgt);
                var keep = StorageRestore.TargetVolume; StorageRestore.TargetVolume = p => { var id = Devices.ForFolder(p); id.FileSystem = "FAT32"; return id; };
                try
                {
                    var plan = StorageRestore.PlanFiles(o.PrimaryPath, "s01", null, tgt, "merge");
                    var it = plan.Items.FirstOrDefault(x => x.Path == "/over-4-GB.bin");
                    Check(plan.Problem == null && it?.Action == "not-restored" && it.Why.Contains("4 GB") && plan.Items.First(x => x.Path == "/small.txt").Action == "create", "a FAT32 target refuses the file over 4 GB by name; the other file is planned (" + it?.Why + ")");
                }
                finally { StorageRestore.TargetVolume = keep; }
                File.Delete(o.PrimaryPath);
            }
            finally { try { File.Delete(big); } catch (Exception) { } }
        }

        // ------------------------------------------------------------------ storage-image
        public static void StorageImage()
        {
            var dir = NewDir("st-image"); var s = StorageSettings(Path.Combine(dir, "loc"), false);
            var (disk, files) = DiskA(dir);
            var est = StorageBackup.Estimate(new[] { new StorageRequest { Path = disk, Mode = "image" } }, StorageBackup.Folder(s), false, 0);
            Check(est.ImageBytes == new FileInfo(disk).Length && est.SizeNote.Contains("as large as the whole drive"), "a whole drive image's estimate is the whole drive's size, said plainly");
            var o = StRun(disk, s, "image");
            Check(o.Protected && o.Sources[0].Summary["regions"] is long n && n == 2, "a 103 MiB drive image is backed up as two 64 MiB regions (" + o.Problem + ")");
            var v = StorageVerifier.Verify(o.PrimaryPath);
            var lay = v.Layouts["s01"]; var idx = v.Index["s01"];
            Check(v.Ok && lay.O("layout").S("scheme") == "mbr" && lay.O("layout").A("partitions").Count == 3, "the layout record names the MBR and its three partitions");
            foreach (var (p, data) in files.Where(x => x.Item2 != null))
            {
                var r = idx.FirstOrDefault(x => x.L("partition") == 1 && x.S("path") == p);
                Check(r != null && r.S("sha256") == ShaB(data), "the index records " + p + " with its SHA-256, worked out from the stored image");
            }
            // the fixture's own listing was made by e2fsprogs (debugfs) with each file's SHA-256: an independent check of the ext4 index
            var listing = Json.Parse(File.ReadAllText(Path.Combine(Fx, "listing-data-modern.json"))).A("entries").OfType<Dictionary<string, object>>().Where(x => x.S("type") == "f" && x.S("sha256") != null).ToList();
            var extRows = idx.Where(x => x.L("partition") == 2 && x.S("type") == "file").ToDictionary(x => x.S("path"), x => x.S("sha256"));
            Check(listing.Count > 0 && listing.All(x => extRows.TryGetValue(x.S("path"), out var h) && h == x.S("sha256")) && extRows.Count == listing.Count, "every ext4 file is indexed with the SHA-256 e2fsprogs recorded for it (" + extRows.Count + " files)");
            Check(!idx.Any(x => x.L("partition") == 3) && ((List<object>)lay.O("layout").A("partitions")).OfType<Dictionary<string, object>>().Single(x => x.L("number") == 3).B("browsable") == false, "the exFAT partition is raw only: nothing is listed from it");
            // browse, by game (hakchi's layout on the drive), export one file and the whole image
            var br = BackupContents.Open(o.PrimaryPath, s); var c = br.Contents;
            Check(br.Ok && c.Find("s01:p1") != null && c.Find("s01:p3")?.StateWord == "raw only", "the Backup Browser lists the partitions, the exFAT one marked raw only (" + br.Problem + ")");
            Check(c.Games.Any(g => g.Entries.Any(e => e.Name == "game.nes")), "hakchi's game folder on the drive is grouped By game (" + string.Join(", ", c.Games.Select(g => g.Title)) + ")");
            var exp = Path.Combine(dir, "export"); Directory.CreateDirectory(exp);
            var sel = new Selection(); sel.Add(c.Entries.First(e => e.Name == "frag-split.bin")); sel.Add(c.Find("s01:image"));
            var ex = Exporter.Export(c, ExportRequest.Selected(c, sel, exp));
            var fragOut = ex.Files.FirstOrDefault(x => x.Entry.Name == "frag-split.bin"); var imgOut = ex.Files.FirstOrDefault(x => x.Entry.Id == "s01:image");
            Check(ex.State == "complete" && fragOut != null && Sha(Path.Combine(ex.Folder, fragOut.RelativePath)) == ShaB(files.First(x => x.Item1 == "/games/frag-split.bin").Item2), "a fragmented file is exported from its extents and matches");
            Check(imgOut != null && Sha(Path.Combine(ex.Folder, imgOut.RelativePath)) == Sha(disk), "the whole drive image exports byte for byte, its checksum worked out from the verified regions");
            c.Dispose();
            // superfloppy FAT16 image
            var sf = Path.Combine(dir, "floppy.img"); File.WriteAllBytes(sf, FatImage(false, 16L << 20, new List<(string, byte[])> { ("/SAVE.SAV", Rnd(8192, 11)) }, "FLOPPY"));
            var so = StRun(sf, s, "image"); var sv = StorageVerifier.Verify(so.PrimaryPath);
            Check(so.Protected && sv.Layouts["s01"].O("layout").S("scheme") == "none" && sv.Index["s01"].Any(x => x.S("path") == "/SAVE.SAV"), "a drive formatted without a partition table is indexed as one file system");
            // the image file is held while it is read: no other program can open it for writing
            bool refused = false;
            var ch = StRun(sf, s, "image", op => op.DuringFile = w => { try { using (new FileStream(sf, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { } } catch (IOException) { refused = true; } });
            Check(ch.Protected && refused, "while a drive image file is read, no other program can open it for writing");
            // an image file another program already has open for writing cannot be read as a consistent image: refused, nothing kept
            StorageOutcome busy;
            using (new FileStream(sf, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) busy = StRun(sf, s, "image");
            Check(!busy.Ok && busy.PartialPath == null, "a drive image file that another program is writing is not copied (" + busy.State + ": " + busy.Problem + ")");
            // two sources in one backup: the consistency text never calls them one snapshot
            var two = StorageBackup.Run(new[] { new StorageRequest { Path = sf, Mode = "image" }, new StorageRequest { Path = Tree(Path.Combine(dir, "t2")) } }, new StorageOptions { Settings = s, SecondCopy = false });
            Check(two.Protected && two.ConsistencyText.Contains("one after another") && two.ConsistencyText.Contains("not one snapshot"), "two sources: each one's time is named and they are never called one snapshot (" + two.ConsistencyText + ")");
        }

        // ------------------------------------------------------------------ storage-repair
        public static void StorageRepairs()
        {
            var dir = NewDir("st-repair"); var s = StorageSettings(Path.Combine(dir, "loc"), false); var src = Tree(dir);
            var a = StRun(src, s); var good = a.PrimaryPath; var orig = Sha(good);
            var v = StorageVerifier.Verify(good); var big = v.Inventory["s01"].First(r => r.S("path") == "/big.bin"); var be = StEntry(good, big.S("member"));
            // a second copy: checked in full first, copied, read back
            var cdir = Path.Combine(dir, "copies"); Directory.CreateDirectory(cdir);
            var sc = StorageRepair.MakeSecondCopy(good, cdir);
            Check(sc.Outcome == "copied" && Sha(sc.OutputPath) == orig, "a verified second copy of a storage backup (" + sc.Text + ")");
            // a damaged copy: repaired from the good copy into a new file identical to the original
            var ddir = Path.Combine(dir, "damaged"); Directory.CreateDirectory(ddir); var dmg = Path.Combine(ddir, Path.GetFileName(good)); File.Copy(good, dmg); StFlip(dmg, be.DataOffset + 5_000_000);
            var dsha = Sha(dmg);
            Check(!StorageVerifier.Verify(dmg).Ok, "the damaged copy fails its full check");
            Check(StorageRepair.MakeSecondCopy(dmg, cdir).Outcome == "refused", "a damaged backup is never copied as a second copy");
            var outd = Path.Combine(dir, "out"); Directory.CreateDirectory(outd);
            var alone = StorageRepair.Rebuild(new[] { dmg }, null, outd);
            Check(alone.Outcome == "not-repaired" && alone.OutputPath == null && Directory.GetFiles(outd, "*.zip").Length == 0, "alone, the damaged piece has no source: nothing is written (" + alone.Text + ")");
            var m = StorageRepair.Rebuild(new[] { dmg, sc.OutputPath }, null, outd, new RepairOptions { RecordedSha256 = orig });
            Check(m.Outcome == "repaired" && Sha(m.OutputPath) == orig && Sha(dmg) == dsha && StorageVerifier.Verify(m.OutputPath).Ok && (bool?)m.Report.Output["identical_to_original"] == true, "merged with its copy: a new file, byte-for-byte the original; the damaged file unchanged (" + m.Text + ")");
            // borrowing from another backup of the same unchanged folder (its large file's pieces are the same)
            var b = StRun(src, s);
            var bor = StorageRepair.Rebuild(new[] { dmg }, new[] { b.PrimaryPath }, outd);
            Check(bor.Outcome == "repaired" && StorageVerifier.Verify(bor.OutputPath).Ok && bor.Report.Repaired.Any(r => ((string)r["how"]).Contains("other backups of the same storage")), "a damaged piece is borrowed from another backup of the same storage where it has exactly the recorded checksum (" + bor.Text + ")");
            // a piece that changed between the backups is never used
            File.WriteAllBytes(Path.Combine(src, "big.bin"), Rnd(20_000_000, 99)); var cdiff = StRun(src, s);
            var nb = StorageRepair.Rebuild(new[] { dmg }, new[] { cdiff.PrimaryPath }, outd);
            Check(nb.Outcome == "not-repaired", "a newer backup whose file changed is never used to repair the older one (" + nb.Text + ")");
            var other = StRun(Tree(Path.Combine(dir, "other")), s);
            var ob = StorageRepair.Rebuild(new[] { dmg }, new[] { other.PrimaryPath }, outd);
            Check(ob.Outcome == "not-repaired" && ob.Report.Skipped.Any(x => ((string)x["why"]).Contains("other storage")), "a backup of other storage is never used as a source");
            // a truncated file: its end (index and manifest) lost; repaired only with a copy that still has the manifest
            var tr = Path.Combine(ddir, "truncated.zip"); File.Copy(good, tr); using (var f = new FileStream(tr, FileMode.Open)) f.SetLength(f.Length - 2000);
            var t1 = StorageRepair.Rebuild(new[] { tr }, null, outd);
            Check(t1.Outcome == "not-repaired" && t1.Text.Contains("manifest"), "a file whose manifest is lost everywhere is not repaired (its bytes are recorded nowhere else)");
            var t2 = StorageRepair.Rebuild(new[] { tr, sc.OutputPath }, null, outd, new RepairOptions { RecordedSha256 = orig });
            Check(t2.Outcome == "repaired" && Sha(t2.OutputPath) == orig, "with a copy, the truncated file is rebuilt exactly");
            // salvage from a damaged file alone: every file except the damaged one is exported, checked
            var sal = Path.Combine(dir, "salvage"); Directory.CreateDirectory(sal);
            var sv = StorageRepair.Salvage(new[] { dmg }, sal);
            var got = sv.OutputPath == null ? new string[0] : Directory.GetFiles(sv.OutputPath, "*", SearchOption.AllDirectories).Where(x => !x.Contains("Salvage report")).ToArray();
            Check(sv.Outcome == "salvaged" && got.Length == 6 && !got.Any(x => x.EndsWith("big.bin")) && sv.Report.Unrecoverable.Any(u => ((string)u["what"]).Contains("big.bin")), "salvage exports the 6 intact files and names the damaged one (" + sv.Text + ")");
            // an image: a region damaged in one backup is borrowed from another backup of the same unchanged region
            var (disk, _) = DiskA(dir, 3); var i1 = StRun(disk, s, "image");
            using (var f = new FileStream(disk, FileMode.Open)) { f.Position = 2048 * 512 + 40000; f.WriteByte(0x42); }   // region 1 changes, region 2 does not
            var i2 = StRun(disk, s, "image"); var r2 = StEntry(i2.PrimaryPath, ExternalFormat.Region("s01", 2)); var r1 = StEntry(i2.PrimaryPath, ExternalFormat.Region("s01", 1));
            var id2 = Path.Combine(ddir, "image2.zip"); File.Copy(i2.PrimaryPath, id2); StFlip(id2, r2.DataOffset + 7_000_000);
            var ir = StorageRepair.Rebuild(new[] { id2 }, new[] { i1.PrimaryPath }, outd, new RepairOptions { RecordedSha256 = Sha(i2.PrimaryPath) });
            Check(ir.Outcome == "repaired" && Sha(ir.OutputPath) == Sha(i2.PrimaryPath), "a damaged region of a drive image is borrowed from an older backup where that region is unchanged");
            var id3 = Path.Combine(ddir, "image3.zip"); File.Copy(i2.PrimaryPath, id3); StFlip(id3, r1.DataOffset + 2048 * 512 + 39990);
            var ir3 = StorageRepair.Rebuild(new[] { id3 }, new[] { i1.PrimaryPath }, outd);
            Check(ir3.Outcome == "not-repaired", "the region that changed between the backups is never borrowed");
            // recovery data (PAR2), when the parity library is beside the tests
            if (!Par2Native.Available) { Console.WriteLine("note storage-repair: recovery data not run (" + Par2Native.UnavailableReason + ")"); return; }
            var p = StorageRepair.Protect(good, 10);
            Check(p.Ok && Parity.FilesOf(good).Count >= 2, "recovery data is made for a checked storage backup (" + p.Text + ")");
            var pd = Path.Combine(dir, "pd"); Directory.CreateDirectory(pd); var pc = Path.Combine(pd, Path.GetFileName(good)); File.Copy(good, pc); foreach (var x in Parity.FilesOf(good)) File.Copy(x, Path.Combine(pd, Path.GetFileName(x)));
            StFlip(pc, be.DataOffset + 1_000_000, 3000);
            var pr = StorageRepair.RebuildFromParity(pc);
            Check(pr.Ok && Sha(pr.OutputPath) == orig && pr.OutputCheck.Ok, "a damaged storage backup is rebuilt from its recovery data into a new file that passes the storage check");
        }

        // ------------------------------------------------------------------ storage-restore
        public static void StorageRestores()
        {
            var dir = NewDir("st-restore"); var s = StorageSettings(Path.Combine(dir, "loc"), false); var src = Tree(dir);
            var o = StRun(src, s); var tgt = Path.Combine(dir, "target"); Directory.CreateDirectory(tgt);
            var plan = StorageRestore.PlanFiles(o.PrimaryPath, "s01", null, tgt, "merge");
            int Files(FileRestorePlan x, string a) => x.Items.Count(i => i.Kind == "file" && i.Action == a);
            Check(plan.Problem == null && Files(plan, "create") == 7 && plan.Items.Any(i => i.Path == "/link" && i.Action == "not-restored") && plan.MetadataText.Contains("Not restored"), "a merge plan into an empty folder creates every file; the link is never created; what is restored is said (" + plan.Problem + ")");
            Check(StorageRestore.RestoreFiles(plan, "wrong", plan.Nonce).Written == 0, "a plan whose digest is not the reviewed one writes nothing");
            plan = StorageRestore.PlanFiles(o.PrimaryPath, "s01", null, tgt, "merge");
            var r = StorageRestore.RestoreFiles(plan, plan.Digest, plan.Nonce);
            Check(r.Ok && r.Written == 7 && Sha(Path.Combine(tgt, "dir", "b.bin")) == Sha(Path.Combine(src, "dir", "b.bin")), "every file is restored, checked and read back (" + r.Text + ")");
            Check(File.GetLastWriteTimeUtc(Path.Combine(tgt, "a.txt")) == new DateTime(2019, 3, 4, 5, 6, 7, 890, DateTimeKind.Utc) && (File.GetAttributes(Path.Combine(tgt, "hidden.txt")) & FileAttributes.Hidden) != 0, "the recorded times and attributes are restored");
            Check(File.Exists(Path.Combine(tgt, "trailing dot_")) && plan.Notes.Any(n => n.Contains("trailing dot.")), "a name Windows cannot create is restored under a safe name and the change is listed");
            Check(StorageRestore.RestoreFiles(plan, plan.Digest, plan.Nonce).Written == 0, "a confirmation is used once");
            var again = StorageRestore.PlanFiles(o.PrimaryPath, "s01", null, tgt, "merge");
            Check(Files(again, "same") == 7 && again.Count("create") == 0, "planned again: every file is already there with the same contents");
            File.WriteAllText(Path.Combine(tgt, "a.txt"), "changed since");
            var mg = StorageRestore.PlanFiles(o.PrimaryPath, "s01", null, tgt, "merge"); var rp = StorageRestore.PlanFiles(o.PrimaryPath, "s01", null, tgt, "replace");
            Check(mg.Items.First(i => i.Path == "/a.txt").Action == "keep" && rp.Items.First(i => i.Path == "/a.txt").Action == "replace", "a different file there now: kept in a merge, replaced in a replace");
            var rr = StorageRestore.RestoreFiles(rp, rp.Digest, rp.Nonce);
            Check(rr.Ok && rr.Replaced == 1 && File.ReadAllText(Path.Combine(tgt, "a.txt")) == "alpha" && File.ReadAllText(Path.Combine(rr.ReplacedFolder, "a.txt")) == "changed since", "a replaced file is first moved, unchanged, into the Replaced by restore folder");
            var late = StorageRestore.PlanFiles(o.PrimaryPath, "s01", new[] { "/dir" }, Path.Combine(dir, "late"), "merge");
            Check(late.Problem != null, "a target folder that does not exist is refused");
            Directory.CreateDirectory(Path.Combine(dir, "late")); late = StorageRestore.PlanFiles(o.PrimaryPath, "s01", new[] { "/dir" }, Path.Combine(dir, "late"), "merge");
            File.SetLastWriteTimeUtc(o.PrimaryPath, DateTime.UtcNow.AddHours(1));
            Check(StorageRestore.RestoreFiles(late, late.Digest, late.Nonce).Written == 0 && Files(late, "create") == 2, "a backup file that changed after its plan was made: nothing is restored");
            Check(StorageRestore.PlanFiles(o.PrimaryPath, "s01", null, Environment.GetFolderPath(Environment.SpecialFolder.Windows), "merge").Problem != null, "Windows' own folder is never a restore target");
            // a whole drive image onto drive image files (a connected drive as the target: storage-raw, on stand-in drives)
            var (disk, _) = DiskA(dir, 5); var io = StRun(disk, s, "image"); var bk = io.PrimaryPath;
            string HeadSha(string p, long n) { using (var f = File.OpenRead(p)) using (var h = SHA256.Create()) { var b = new byte[n]; f.Read(b, 0, (int)n); return Hex.Of(h.ComputeHash(b)); } }
            var backing = Path.Combine(dir, "larger image file.img"); File.WriteAllBytes(backing, Rnd(1_000_000, 3)); using (var f = new FileStream(backing, FileMode.Open)) f.SetLength(200_000_000);
            var lp = StorageRestore.PlanImage(bk, "s01", new DiskTarget { ImageFile = backing });
            Check(lp.Refusal == null && lp.ProtectFirst && lp.Review.Any(x => x.Contains("lost")) && lp.Review.Any(x => x.Contains("larger than the image")), "an existing, larger image file as the target gets a review of exactly what is lost (" + lp.Refusal + ")");
            var lr = StorageRestore.RestoreImage(lp, lp.Digest, lp.Nonce, s);
            Check(lr.Ok && HeadSha(backing, new FileInfo(disk).Length) == Sha(disk) && new FileInfo(backing).Length == 200_000_000 && lr.ProtectionBackup != null, "the image is written and read back region by region; the rest of the target file is left as it was (" + lr.Text + ")");
            Check(StorageRestore.PlanImage(bk, "s01", new DiskTarget { ImageFile = bk }).Refusal != null && StorageRestore.PlanImage(bk, "s01", new DiskTarget { ImageFile = disk }).Refusal != null, "the backup itself and its source image file are refused as targets");
            var fresh = Path.Combine(dir, "restored.img"); var np = StorageRestore.PlanImage(bk, "s01", new DiskTarget { ImageFile = fresh });
            var nr = StorageRestore.RestoreImage(np, np.Digest, np.Nonce, s);
            Check(nr.Ok && Sha(fresh) == Sha(disk) && !np.ProtectFirst, "restored into a new drive image file: byte for byte the source");
            var ep = StorageRestore.PlanImage(bk, "s01", new DiskTarget { ImageFile = backing });
            var er = StorageRestore.RestoreImage(ep, ep.Digest, ep.Nonce, s);
            Check(ep.ProtectFirst && er.Ok && er.ProtectionBackup != null && StorageVerifier.Verify(er.ProtectionBackup).Ok, "an existing image file as the target is backed up first (a checked whole drive image backup), then written");
            var dp = StorageRestore.PlanImage(bk, "s01", new DiskTarget { DiskNumber = 3 }, new RawPc { Sys = 0 });
            Check(dp.Refusal != null && dp.Refusal.Contains("not connected"), "a connected drive that is not there is refused at the review (" + dp.Refusal + ")");
            var files = StorageRestore.PlanFiles(bk, "s01", new[] { "/partition 1/games" }, tgt, "merge");
            Check(files.Problem == null && files.Items.Count(i => i.Kind == "file" && i.Action == "create") == 4 && files.Items.Any(i => i.Path.EndsWith("TWIN.TXT") && i.Rel.Contains("(2)") || i.Path.EndsWith("Twin.txt") && i.Rel.Contains("(2)")), "files can be restored from a drive image's index; two names differing only in case are kept apart on Windows");
        }

        // ------------------------------------------------------------------ storage-protect
        public static void StorageProtect()
        {
            var dir = NewDir("st-protect"); var s = StorageSettings(Path.Combine(dir, "loc"), true); var src = Tree(dir);
            var o = StorageBackup.BackUpVolumesNow(new[] { new StorageRequest { Path = src } }, s, "before a test change");
            Check(o.Protected && o.Verification.Ok && File.Exists(o.PrimaryPath) && File.Exists(o.ArchivePath) && StorageVerifier.Verify(o.PrimaryPath).Manifest.S("purpose") == "before a test change", "the entry point makes a verified backup of exactly the requested storage, with its purpose and an additional copy");
            StorageOutcome lk;
            using (new FileStream(Path.Combine(src, "a.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) lk = StorageBackup.BackUpVolumesNow(new[] { new StorageRequest { Path = src } }, s, "x");
            Check(lk.Ok && !lk.Protected && lk.Sources[0].Problems.Any(p => p.Contains("a.txt")), "a volume not captured completely is not protection: Protected is false and the file is named");
            var missing = StorageBackup.BackUpVolumesNow(new[] { new StorageRequest { Path = Path.Combine(dir, "not there") } }, s, "x");
            Check(missing.State == "refused" && !missing.Protected, "a drive that is not connected is refused, not skipped");
            // a recovery set: the console backup and the storage backup for one change, named together, read one after the other
            var con = Backup(Console_("modified-noorig"), s);
            var set = RecoverySet.Make(s, con.PrimaryPath, o.PrimaryPath, "before a test change");
            Check(set.Ok && File.Exists(set.RecordFile) && set.ConsistencyText.Contains("not one snapshot") && set.ConsistencyText.Contains("console " + con.Identity.ShortTag) && RecoverySet.Check(set.RecordFile).Ok, "a recovery set names both checked backups and says they were read one after the other (" + set.Problem + ")");
            Check(!RecoverySet.Make(s, con.PrimaryPath, lk.PrimaryPath, "x").Ok, "a storage backup that is not complete protection is refused in a recovery set");
            var moved = o.PrimaryPath + ".moved"; File.Move(o.PrimaryPath, moved);
            Check(!RecoverySet.Check(set.RecordFile).Ok, "a recovery set whose storage backup is gone no longer checks");
            File.Move(moved, o.PrimaryPath);
            // a connected drive copied whole through the administrator step, on a stand-in PC (storage-raw has every case)
            var saved = (RawDiskClient.Launcher, RawDiskClient.Program, RawDiskClient.TempRoot, StorageBackup.DiskQueries, RawDiskHost.ConnectMs);
            try
            {
                var (pc, l) = RawSetup(dir); var (img, _) = DiskA(dir, 7); pc.Add(7, img, "SD", "CARD-7");
                var raw = StorageBackup.BackUpVolumesNow(new[] { new StorageRequest { Mode = "image", DiskNumber = 7 } }, s, "before a test change");
                Check(raw.Protected && raw.Sources[0].Identity.Kind == SourceKind.Disk && StorageVerifier.Verify(raw.PrimaryPath).Ok && l.Starts == 1, "a connected SD card read whole is complete protection, through one administrator step (" + raw.Problem + ")");
                l.Decline = true; var no = StorageBackup.BackUpVolumesNow(new[] { new StorageRequest { Mode = "image", DiskNumber = 7 } }, s, "x");
                Check(no.State == "refused" && !no.Protected && no.ElevationDeclined, "a refused permission prompt is never protection");
                var gone = StorageBackup.BackUpVolumesNow(new[] { new StorageRequest { Mode = "image", DiskNumber = 8 } }, s, "x");
                Check(gone.State == "refused" && !gone.Protected && gone.Problem.Contains("no longer connected"), "a connected drive that is not there is refused, not skipped");
            }
            finally { (RawDiskClient.Launcher, RawDiskClient.Program, RawDiskClient.TempRoot, StorageBackup.DiskQueries, RawDiskHost.ConnectMs) = saved; }
        }
    }
}
