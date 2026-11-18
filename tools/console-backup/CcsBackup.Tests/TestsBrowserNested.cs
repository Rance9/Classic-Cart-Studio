// Classic Cart Studio console backup and restore engine tests: the Backup Browser inside system images, the console's own system part and
// the nested CCS HOME package; checksum provenance; damaged parents with and without per-file proof; a backup whose table of contents (ZIP
// index) is damaged, opened through Backup Repair's part finder. GPL-3.0-or-later. Simulated consoles only (files on this PC); nothing opens
// a console session of any kind. The system images are built by squashfs-tools in WSL (gzip, xz and lz4) and written into a copy of the
// 64 MB "modern" data fixture with e2fsprogs' debugfs. Expected listings come from unsquashfs (an independent reader), expected bytes from
// the files the images were built from, the package's checksums from Python's zipfile; never from the engine's own readers.
using CcsBackup.Archive;
using CcsBackup.Browse;
using CcsBackup.Device;
using CcsBackup.Library;
using CcsBackup.Repair;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        static readonly byte[] TinyPng = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00, 0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 };

        /// <summary>one system image as built: its bytes, the regular files it was built from (path inside: bytes) and unsquashfs's listing</summary>
        sealed class BuiltImage { public byte[] Bytes; public Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>(StringComparer.Ordinal); public Dictionary<string, (string perm, int uid, int gid, string size, DateTime when, string target)> Listing; }

        /// <summary>A system image built by mksquashfs in WSL from files written here (staged), with a hard link, a start-program link, a
        /// named pipe, a character device and a file owned by another user and group added in WSL; listed by unsquashfs -lln (UTC).</summary>
        static BuiltImage NestedImage(string dir, string name, string comp, Dictionary<string, byte[]> files, bool extras, string options = "")
        {
            var stage = Path.Combine(dir, name + "-stage"); var img = Path.Combine(dir, name + ".hsqs"); var lst = Path.Combine(dir, name + ".lln");
            foreach (var kv in files) { var p = Path.Combine(stage, kv.Key.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllBytes(p, kv.Value); }
            var sh = new StringBuilder();
            sh.Append("T=$(mktemp -d)\ncp -r '" + W(stage) + "' \"$T/tree\"\nmkdir -p \"$T/tree/dev\" \"$T/tree/run\" \"$T/tree/sbin\"\n");
            sh.Append("touch -d '2016-05-01 12:34:56 UTC' \"$T/tree/etc/clover/REGION\"\n");
            var pseudo = "";
            if (extras)
            {
                sh.Append("ln \"$T/tree/bin/busybox\" \"$T/tree/bin/sh-hard\"\nln -s ../bin/busybox \"$T/tree/sbin/init\"\nmkfifo \"$T/tree/run/fifo\"\n");
                pseudo = " -p '/dev/console c 600 0 0 5 1' -p '/etc/owned f 640 1000 100 echo owned'";
            }
            else sh.Append("ln -s ../bin/busybox \"$T/tree/sbin/init\"\n");
            sh.Append("/usr/bin/mksquashfs \"$T/tree\" '" + W(img) + "' -comp " + comp + " -noappend -no-xattrs -quiet" + options + pseudo + " > /dev/null\n");
            sh.Append("TZ=UTC /usr/bin/unsquashfs -lln '" + W(img) + "' > '" + W(lst) + "'\nrm -rf \"$T\"\n");
            RunWsl(dir, name + "-build", sh.ToString());
            if (!File.Exists(img) || !File.Exists(lst)) throw new SkipException("squashfs-tools in WSL did not write " + name);
            var b = new BuiltImage { Bytes = File.ReadAllBytes(img), Listing = new Dictionary<string, (string, int, int, string, DateTime, string)>(StringComparer.Ordinal) };
            foreach (var kv in files) b.Files[kv.Key] = kv.Value;
            if (extras) { b.Files["/bin/sh-hard"] = files["/bin/busybox"]; b.Files["/etc/owned"] = Encoding.ASCII.GetBytes("owned\n"); }
            var re = new Regex(@"^(\S{10}) (\d+)/(\d+)\s+(\d+|\d+,\s*\d+) (\d{4}-\d{2}-\d{2} \d{2}:\d{2}) squashfs-root(.*)$");
            foreach (var line in File.ReadAllLines(lst))
            {
                var m = re.Match(line); if (!m.Success || m.Groups[6].Value.Length == 0) continue;
                var path = m.Groups[6].Value; string target = null; int arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
                if (m.Groups[1].Value[0] == 'l' && arrow > 0) { target = path.Substring(arrow + 4); path = path.Substring(0, arrow); }
                b.Listing[path] = (m.Groups[1].Value, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), m.Groups[4].Value, DateTime.ParseExact(m.Groups[5].Value, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal), target);
            }
            return b;
        }

        /// <summary>an ls -l permission string as the mode bits it names (and its type letter)</summary>
        static (char type, int mode) Perm(string p)
        {
            int m = 0; int Bit(int i, char on, int v) => p[i] == on ? v : 0;
            m |= Bit(1, 'r', 0x100) | Bit(2, 'w', 0x80) | (p[3] == 'x' || p[3] == 's' ? 0x40 : 0) | (p[3] == 's' || p[3] == 'S' ? 0x800 : 0);
            m |= Bit(4, 'r', 0x20) | Bit(5, 'w', 0x10) | (p[6] == 'x' || p[6] == 's' ? 0x8 : 0) | (p[6] == 's' || p[6] == 'S' ? 0x400 : 0);
            m |= Bit(7, 'r', 0x4) | Bit(8, 'w', 0x2) | (p[9] == 'x' || p[9] == 't' ? 0x1 : 0) | (p[9] == 't' || p[9] == 'T' ? 0x200 : 0);
            return (p[0] == '-' ? 'f' : p[0], m);
        }

        static Dictionary<string, byte[]> NesRoot(string board, string region, string seed)
        {
            var desk = "[Desktop Entry]\nType=Application\nExec=/bin/clover-kachikachi-wr /usr/share/games/nes/kachikachi/CLV-P-NAAAE/CLV-P-NAAAE.nes --volume 75\nPath=/var/lib/clover/profiles/0/CLV-P-NAAAE\nName=Super Mario Bros.\nIcon=/usr/share/games/nes/kachikachi/CLV-P-NAAAE/CLV-P-NAAAE.png\n\n[X-CLOVER Game]\nCode=CLV-P-NAAAE\n";
            var rom = NesGame(2, 1);
            return new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["/etc/clover/boardtype"] = Encoding.ASCII.GetBytes(board + "\n"), ["/etc/clover/REGION"] = Encoding.ASCII.GetBytes(region + "\n"), ["/etc/clover/VERSION"] = Encoding.ASCII.GetBytes("release-v2.0.14-0-g" + seed + "\n"),
                ["/etc/issue"] = Encoding.ASCII.GetBytes("Welcome\n"), ["/bin/busybox"] = BusyboxStandIn(seed),
                ["/usr/share/games/nes/kachikachi/CLV-P-NAAAE/CLV-P-NAAAE.desktop"] = Encoding.ASCII.GetBytes(desk),
                ["/usr/share/games/nes/kachikachi/CLV-P-NAAAE/CLV-P-NAAAE.nes"] = rom, ["/usr/share/games/nes/kachikachi/CLV-P-NAAAE/CLV-P-NAAAE.png"] = TinyPng,
            };
        }

        public static void BrowserNested()
        {
            var dir = NewDir("browser-nested"); var s = SettingsIn(dir, false); string con = null;
            var exp = Path.Combine(dir, "export"); Directory.CreateDirectory(exp);
            try
            {
                // ---------------------------------------------------------------- the images, the data storage that holds them, the backup
                var nes = NestedImage(dir, "nes-gzip", "gzip", NesRoot("dp-nes", "USA", "nes"), true);
                var snesFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal)
                {
                    ["/etc/clover/boardtype"] = Encoding.ASCII.GetBytes("dp-shvc\n"), ["/etc/clover/REGION"] = Encoding.ASCII.GetBytes("EUR\n"), ["/etc/clover/VERSION"] = Encoding.ASCII.GetBytes("release-v9.9.9-0-gsnes\n"),
                    ["/bin/busybox"] = BusyboxStandIn("snes"), ["/usr/share/games/CLV-U-ABCDE/CLV-U-ABCDE.desktop"] = Encoding.ASCII.GetBytes("[Desktop Entry]\nExec=/bin/clover-canoe-shvc-wr -rom /usr/share/games/CLV-U-ABCDE/CLV-U-ABCDE.sfrom\nName=Test Quest\n\n[X-CLOVER Game]\nCode=CLV-U-ABCDE\n"),
                    ["/usr/share/games/CLV-U-ABCDE/CLV-U-ABCDE.sfrom"] = Cipher(70000),
                };
                var snes = NestedImage(dir, "snes-xz", "xz", snesFiles, false);
                var lz4 = NestedImage(dir, "nes-lz4", "lz4", NesRoot("dp-nes", "USA", "lz4"), false);
                var broken = (byte[])snes.Bytes.Clone(); var sb = new SquashFsReader(new MemoryStream(snes.Bytes)); broken[(int)sb.InodeTable + 20] ^= 0x40;   // xz: its block check makes the damage certain to be found
                File.WriteAllBytes(Path.Combine(dir, "broken.hsqs"), broken);
                // an entry of a kind no SquashFS reader knows: an image whose inode table is stored uncompressed (-noI), one inode's type
                // changed in place to 15 (the format's types are 1 to 14)
                var odd = NestedImage(dir, "odd-gzip", "gzip", NesRoot("dp-nes", "USA", "odd"), false, " -noI");
                var oddR = new SquashFsReader(new MemoryStream(odd.Bytes)); var issue = oddR.Find("/etc/issue");
                long ia = (long)oddR.InodeTable + (issue.Ref >> 16) + 2 + (issue.Ref & 0xFFFF);
                Check((BitConverter.ToUInt16(odd.Bytes, (int)oddR.InodeTable) & 0x8000) != 0 && BitConverter.ToUInt16(odd.Bytes, (int)ia) == 2, "the crafted image's inode table is stored as it is, so one inode's type can be changed in place");
                odd.Bytes[ia] = 15; odd.Bytes[ia + 1] = 0; File.WriteAllBytes(Path.Combine(dir, "odd-gzip.hsqs"), odd.Bytes);
                var img = Path.Combine(dir, "data-images.img"); File.Copy(Path.Combine(Fx, "data-modern.img"), img);
                var cmds = Path.Combine(dir, "add-images.debugfs");
                File.WriteAllText(cmds, "mkdir /hakchi/firmware\ncd /hakchi/firmware\nwrite " + W(Path.Combine(dir, "snes-xz.hsqs")) + " snes-eur.hsqs\nwrite " + W(Path.Combine(dir, "nes-lz4.hsqs")) + " new-lz4.hsqs\nwrite " + W(Path.Combine(dir, "broken.hsqs")) + " broken.hsqs\n"
                    + "write " + W(Path.Combine(dir, "odd-gzip.hsqs")) + " odd.hsqs\nmkdir /ccs/nucleus\nmkdir /ccs/nucleus/roots\ncd /ccs/nucleus/roots\nwrite " + W(Path.Combine(dir, "nes-gzip.hsqs")) + " owner-1.hsqs\n");
                var dbg = RunWsl(dir, "add-images", "/usr/sbin/debugfs -w -f '" + W(cmds) + "' '" + W(img) + "' 2>&1");
                Check(!Regex.IsMatch(dbg, "error|invalid|not found|usage|could not", RegexOptions.IgnoreCase), "five system images written into a copy of the data fixture with debugfs (" + dbg.Trim().Replace("\n", " | ") + ")");
                con = Console_("modified-noorig", HwidA, img, SmallLayout); File.Delete(img);
                var a = Backup(con, s); var A = a.PrimaryPath;
                Check(a.Ok && Verifier.VerifyFile(A).Ok, "the backup holding the system images is made and verifies (" + a.State + " " + a.Problem + ")");
                var inv = InventoryOf(A).ToDictionary(e => e.S("path"));
                const string Own = "/ccs/nucleus/roots/owner-1.hsqs", Snes = "/hakchi/firmware/snes-eur.hsqs", Lz4 = "/hakchi/firmware/new-lz4.hsqs", Broken = "/hakchi/firmware/broken.hsqs";
                Check(inv[Own].S("sha256") == Sha(nes.Bytes) && inv[Snes].S("sha256") == Sha(snes.Bytes) && inv[Broken].S("sha256") == Sha(broken), "the backup's file list recorded each image file's own SHA-256 when it was made (the parent checksums a derived one is bound to)");
                var keep = (Sha(A), new FileInfo(A).Length, File.GetLastWriteTimeUtc(A));
                // CCS_BROWSE_SAMPLE=<folder>: keep a copy of this backup there, and one with its table of contents damaged, for the window's
                // page renderer (about 640 MB each; nothing else is kept)
                var sample = Environment.GetEnvironmentVariable("CCS_BROWSE_SAMPLE");
                if (!string.IsNullOrEmpty(sample))
                {
                    Directory.CreateDirectory(sample); var copyA = Path.Combine(sample, Path.GetFileName(A)); File.Copy(A, copyA, true);
                    var copyD = Path.Combine(sample, "Damaged table of contents (sample).zip"); File.Copy(A, copyD, true); DamageIndex(copyD);
                }

                NestedViews(s, A, nes, snes, lz4, broken, exp);
                using (var c = BackupContents.Open(A, s).Contents)
                {
                    var oddE = c.Find("d:/hakchi/firmware/odd.hsqs"); var inOdd = c.Entries.Where(e => e.Container == oddE).ToList(); var un = inOdd.FirstOrDefault(e => e.InnerPath == "/etc/issue");
                    Check(inOdd.Count == odd.Listing.Count && un != null && un.TypeCode == '?' && un.Kind == EntryKind.Special && un.Unreadable != null && un.Category == Category.SystemFiles && c.ByType(Category.SystemFiles).Contains(un)
                          && inOdd.Where(e => e != un && e.Kind == EntryKind.File).All(e => c.ExpectedHash(e) == Sha(odd.Files[e.InnerPath])),
                        "an entry of a kind this version does not know stays listed as it is (" + un?.Unreadable + "); the image's other files still read exactly");
                    var ux = Exporter.Export(c, One(c, un, exp));
                    Check(ux.Ok && ux.Described.Count == 1 && ux.Files.Count == 0, "exporting it describes it in the export's list; nothing is made for it");
                }
                NestedDamagedParent(s, A, nes, exp);
                NestedDamagedIndex(s, A, nes, dir, exp);

                Check(Sha(A) == keep.Item1 && new FileInfo(A).Length == keep.Item2 && File.GetLastWriteTimeUtc(A) == keep.Item3, "the backup is byte for byte unchanged after every open, preview, export and the damaged copies' cases (SHA-256, size and time)");

                // ---------------------------------------------------------------- a console whose system part is a plain SquashFS image
                using (var f = new FileStream(SimulatedConsole.Dev(con, "nandb"), FileMode.Open, FileAccess.Write)) { f.Write(nes.Bytes, 0, nes.Bytes.Length); }
                File.Delete(A);
                var b = Backup(con, s); var B = b.PrimaryPath;
                var ob = BackupContents.Open(B, s);
                if (ob.Ok) using (var c = ob.Contents)
                    {
                        var sp = c.Find("m:restore/nandb.img"); var kids = c.Entries.Where(e => e.Container == sp).ToList();
                        Check(sp.ImageType.StartsWith("System part (SquashFS, gzip): NES Classic system (USA)") && kids.Count == nes.Listing.Count && kids.All(e => e.Source == EntrySource.ImageFile),
                            "a system part that is a plain SquashFS image is browsed like any other system image (" + sp.ImageType + "; " + kids.Count + " entries)");
                        var region = kids.First(e => e.InnerPath == "/etc/clover/REGION");
                        Check(c.ExpectedHash(region) == Sha(nes.Files["/etc/clover/REGION"]) && region.BoundToSha256 == sp.Sha256 && region.BoundTo == "restore/nandb.img", "a file in it: its checksum is worked out from the part and bound to the part's own recorded SHA-256 (" + region.HashText + ")");
                    }
                else Check(false, "backup B did not open: " + ob.Problem);
                File.Delete(B);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                if (con != null) try { Directory.Delete(Path.GetDirectoryName(con), true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        // ------------------------------------------------------------------ the intact backup: images, system part, package, views, export
        static void NestedViews(Settings s, string A, BuiltImage nes, BuiltImage snes, BuiltImage lz4, byte[] broken, string exp)
        {
            var o = BackupContents.Open(A, s);
            Check(o.Ok, "the backup opens after its full check (" + o.Problem + ")"); if (!o.Ok) return;
            using (var c = o.Contents)
            {
                var own = c.Find("d:/ccs/nucleus/roots/owner-1.hsqs"); var sn = c.Find("d:/hakchi/firmware/snes-eur.hsqs"); var l4 = c.Find("d:/hakchi/firmware/new-lz4.hsqs"); var br = c.Find("d:/hakchi/firmware/broken.hsqs");
                var inOwn = c.Entries.Where(e => e.Container == own).ToDictionary(e => e.InnerPath);
                // ---- the gzip NES system: identified, every entry listed with the metadata unsquashfs reads
                Check(own.IsImage && own.ImageType == "System image (SquashFS, gzip): NES Classic system (USA)" && own.ImageSystem == "NES Classic system (USA), version release-v2.0.14-0-gnes" && own.HashOrigin == HashOrigin.Inventory,
                    "a gzip system image in CCS Nucleus's folder: identified by hakchi's rules with its recorded version (" + own.ImageType + "; " + own.ImageSystem + ")");
                int bad = 0; var badList = new List<string>();
                foreach (var kv in nes.Listing)
                {
                    if (!inOwn.TryGetValue(kv.Key, out var e)) { bad++; badList.Add(kv.Key + " missing"); continue; }
                    var (t, mode) = Perm(kv.Value.perm);
                    bool ok = e.TypeCode == t && e.Mode == mode && e.Uid == kv.Value.uid && e.Gid == kv.Value.gid && e.MtimeUtc.HasValue && e.MtimeUtc.Value.ToString("yyyy-MM-dd HH:mm") == kv.Value.when.ToString("yyyy-MM-dd HH:mm")
                              && ((t != 'f' && t != 'l') || e.Size.ToString(CultureInfo.InvariantCulture) == kv.Value.size) && (t != 'l' || e.LinkTarget == kv.Value.target);
                    if (!ok) { bad++; badList.Add(kv.Key + " " + e.TypeCode + " " + Convert.ToString(e.Mode, 8) + " " + e.Uid + "/" + e.Gid + " " + e.Size + " " + e.MtimeUtc); }
                }
                Check(inOwn.Count == nes.Listing.Count && bad == 0, "All files: its " + nes.Listing.Count + " entries, each with the type, permissions, owner, group, size, date and link target unsquashfs reads (" + bad + " differ: " + string.Join("; ", badList.Take(4)) + ")");
                var bb = inOwn["/bin/busybox"]; var hard = inOwn["/bin/sh-hard"]; var owned = inOwn["/etc/owned"];
                Check(bb.Inode == hard.Inode && bb.Links == 2 && hard.Links == 2 && inOwn["/run/fifo"].Kind == EntryKind.Special && inOwn["/run/fifo"].TypeCode == 'p' && inOwn["/dev/console"].TypeCode == 'c' && inOwn["/sbin/init"].Kind == EntryKind.Link && owned.Uid == 1000 && owned.Gid == 100,
                    "a hard link shares its inode (2 links), a named pipe, a character device and a link are listed as what they are; another owner and group come from the image's id table");
                Check(c.Children(own.Id).Select(e => e.Name).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(nes.Listing.Keys.Where(k => k.LastIndexOf('/') == 0).Select(k => k.Substring(1)).OrderBy(x => x, StringComparer.Ordinal))
                      && inOwn["/etc/clover/REGION"].DisplayPath == "/var/lib/ccs/nucleus/roots/owner-1.hsqs/etc/clover/REGION" && inOwn["/etc/clover/REGION"].Locator == "restore/nandc.img:/ccs/nucleus/roots/owner-1.hsqs:/etc/clover/REGION",
                    "the tree: the image's top folders open under its file; each entry's path shows the image it is in (" + inOwn["/etc/clover/REGION"].DisplayPath + ")");
                // ---- the xz SNES system
                var inSnes = c.Entries.Where(e => e.Container == sn).ToList();
                Check(sn.ImageType == "System image (SquashFS, xz): SNES Classic system (Europe)" && sn.ImageSystem.Contains("release-v9.9.9-0-gsnes") && inSnes.Count == snes.Listing.Count,
                    "an xz system image in hakchi's firmware folder: an SNES Classic system (Europe), every entry listed (" + sn.ImageType + "; " + inSnes.Count + " of " + snes.Listing.Count + ")");
                // ---- unsupported compression and a damaged image: whole items with the reason, raw export
                Check(l4.IsImage && l4.ImageType == "SquashFS file system (lz4)" && l4.ImageNote.Contains("decoder for lz4") && l4.ImageNote.Contains("gzip and xz") && c.Children(l4.Id).Count == 0 && l4.HashTrusted,
                    "lz4: kept whole, with exactly what is missing (" + l4.ImageNote + ")");
                Check(br.IsImage && br.ImageType.Contains("damaged") && br.ImageNote.Contains("cannot be read") && c.Children(br.Id).Count == 0 && br.HashTrusted,
                    "a damaged image (its inode table): kept whole with the reason, nothing inside it shown (" + br.ImageNote + ")");
                var rx = Exporter.Export(c, One(c, l4, exp)); var rb = Exporter.Export(c, One(c, br, exp));
                Check(rx.Ok && Sha(Path.Combine(rx.Folder, rx.Files[0].RelativePath)) == Sha(lz4.Bytes) && rb.Ok && Sha(Path.Combine(rb.Folder, rb.Files[0].RelativePath)) == Sha(broken),
                    "both export as one exact file, checked against the backup's file list (raw export)");
                // ---- the console's own system part
                var sys = c.Find("m:restore/nandb.img");
                Check(sys.IsImage && sys.ImageType == "System part (not a file system as it is stored)" && sys.ImageNote.Contains("It is not a file system as it is stored") && sys.ImageNote.Contains("restore/boot2-active.img: ") && !sys.ImageNote.Contains("never leaves") && c.Children(sys.Id).Count == 0 && sys.HashTrusted,
                    "a system part that no key in the backup opens is listed with each startup image's exact reason, never as impossible to open (" + sys.ImageNote + ")");
                var rs = Exporter.Export(c, One(c, sys, exp));
                Check(rs.Ok && rs.Files[0].Sha256 == sys.Sha256 && new FileInfo(Path.Combine(rs.Folder, rs.Files[0].RelativePath)).Length == sys.Size, "and exports as one exact image (" + Engine.Units.Gb(sys.Size) + ")");
                // ---- checksums inside an image: worked out from the verified image, bound to its recorded checksum
                var region = inOwn["/etc/clover/REGION"];
                Check(region.HashOrigin == HashOrigin.None && !region.HashTrusted && region.ChecksumKind == "none yet", "before it is read, a file inside an image has no checksum: the image keeps none for each file");
                var rh = c.ExpectedHash(region);
                Check(rh == Sha(nes.Files["/etc/clover/REGION"]) && region.HashOrigin == HashOrigin.Derived && region.BoundTo == own.DisplayPath && region.BoundToSha256 == own.Sha256 && own.Sha256 == Sha(nes.Bytes) && region.HashText.Contains("not a checksum recorded when the backup was made"),
                    "worked out on this PC, equal to the file it was built from, bound to the image's own SHA-256 from the backup's file list (" + region.HashText + ")");
                c.ExpectedHash(bb);
                Check(hard.HashOrigin == HashOrigin.Derived && hard.Sha256 == Sha(nes.Files["/bin/busybox"]), "a hard link gets the same worked-out checksum");
                var png = inOwn["/usr/share/games/nes/kachikachi/CLV-P-NAAAE/CLV-P-NAAAE.png"];
                var pb = c.ReadVerified(png, 1000000, out var why);
                Check(pb != null && BackupContents.ImageKindOf(pb) == "png" && Sha(pb) == Sha(TinyPng), "preview: a picture inside an image is shown only after its bytes match their checksum (" + why + ")");
                // ---- the three views and search
                var smbImg = c.Games.FirstOrDefault(g => g.Key == "img:" + own.Id + "|CLV-P-NAAAE"); var quest = c.Games.FirstOrDefault(g => g.Code == "CLV-U-ABCDE");
                Check(smbImg != null && smbImg.Title == "Super Mario Bros." && smbImg.Kind.Contains("system image") && smbImg.GameFiles.Select(e => e.Name).OrderBy(x => x).SequenceEqual(new[] { "CLV-P-NAAAE.desktop", "CLV-P-NAAAE.nes" }) && smbImg.BoxArt?.Name == "CLV-P-NAAAE.png"
                      && quest != null && quest.Title == "Test Quest" && quest.GameFiles.Any(e => e.Name == "CLV-U-ABCDE.sfrom"),
                    "By game: each image's games, from their own entries (Exec=, Icon=, Name=), separate from the console's own (" + string.Join(", ", c.Games.Where(g => g.Key.StartsWith("img:")).Select(g => g.Title + " / " + g.Kind)) + ")");
                Check(png.Category == Category.Artwork && c.ByType(Category.Artwork).Contains(png) && inOwn["/etc/clover/boardtype"].Category == Category.SystemFiles && c.Counts().Values.Sum(v => v.count) == c.Entries.Count(e => e.Kind != EntryKind.Folder),
                    "By type: inside the images too, every file, link and special file is in exactly one category");
                var found = c.Search("REGION").Where(e => e.Source == EntrySource.ImageFile).Select(e => e.Container.Name).OrderBy(x => x).ToList();
                Check(found.SequenceEqual(new[] { "odd.hsqs", "owner-1.hsqs", "snes-eur.hsqs" }) && c.SearchGames("test quest").Count == 1 && c.Search("owner-1.hsqs/etc").Count > 0, "search finds files inside the images, by name, path and game (" + string.Join(", ", found) + ")");
                // ---- export: a selection from two images (the same inner path in each), a game bundle, the whole image's files, a type
                var sel = new Selection(); sel.Add(region); sel.Add(inSnes.First(e => e.InnerPath == "/etc/clover/REGION")); sel.Add(bb); sel.Add(inOwn["/sbin/init"]); sel.Add(inOwn["/run/fifo"]);
                var x = Exporter.Export(c, ExportRequest.Selected(c, sel, exp, ExportLayout.OriginalPaths));
                var r1 = Path.Combine(x.Folder, "Files inside owner-1.hsqs", "etc", "clover", "REGION"); var r2 = Path.Combine(x.Folder, "Files inside snes-eur.hsqs", "etc", "clover", "REGION");
                var xm = Json.Parse(File.ReadAllText(x.ManifestPath)); var rows = xm.A("files").OfType<Dictionary<string, object>>().ToList();
                var rrow = rows.First(f => f.S("original_path") == region.DisplayPath);
                Check(x.Ok && File.Exists(r1) && File.Exists(r2) && Sha(r1) == Sha(nes.Files["/etc/clover/REGION"]) && Sha(r2) == Sha(snes.Files["/etc/clover/REGION"]) && File.GetLastWriteTimeUtc(r1) == new DateTime(2016, 5, 1, 12, 34, 56, DateTimeKind.Utc),
                    "a selection: the same path from two images lands in each image's own folder, checked, with the date the image recorded (" + x.Summary + ")");
                Check(rrow.S("checksum_from") == "derived" && rrow.S("checksum_kind") == "worked out on this PC from a verified parent" && rrow.O("checksum_bound_to")?.S("parent_sha256") == own.Sha256 && rrow.O("checksum_bound_to")?.S("parent") == own.DisplayPath
                      && rrow.S("in_backup") == region.Locator && rrow.O("console_date").S("source") == "as recorded in the system image" && xm.A("links_and_special_files").Count == 2,
                    "its export list records where each checksum comes from and what it is bound to; the link and the pipe are described only");
                var y = Exporter.Export(c, ExportRequest.Selected(c, sel, exp, ExportLayout.OriginalPaths));
                Check(y.Ok && y.Folder != x.Folder && y.Files.Select(f => f.RelativePath).SequenceEqual(x.Files.Select(f => f.RelativePath)) && Sha(r1) == Sha(nes.Files["/etc/clover/REGION"]), "the same export again: a new folder, the same names, the first untouched");
                var gb = Exporter.Export(c, ExportRequest.GameBundle(c, smbImg, exp));
                Check(gb.Ok && File.Exists(Path.Combine(gb.Folder, "Game files", "CLV-P-NAAAE.nes")) && Sha(Path.Combine(gb.Folder, "Game files", "CLV-P-NAAAE.nes")) == Sha(nes.Files["/usr/share/games/nes/kachikachi/CLV-P-NAAAE/CLV-P-NAAAE.nes"]) && File.Exists(Path.Combine(gb.Folder, "Artwork", "CLV-P-NAAAE.png")),
                    "a game bundle from inside an image: its game file and picture by role (" + gb.Summary + ")");
                var art = Exporter.Export(c, ExportRequest.WholeCategory(c, Category.Artwork, exp)); var artIn = c.ByType(Category.Artwork).Where(e => e.Container != null).ToList();
                var artRows = Json.Parse(File.ReadAllText(art.ManifestPath)).A("files").OfType<Dictionary<string, object>>().ToList();
                Check(art.Ok && artIn.Count >= 3 && artIn.All(e => art.Files.Any(f => f.Entry == e)) && artRows.Where(f => f.S("in_backup").Contains(".hsqs:")).All(f => f.S("checksum_from") == "derived" && f.O("checksum_bound_to") != null)
                      && art.Files.Where(f => f.Entry.Container != null).All(f => Sha(Path.Combine(art.Folder, f.RelativePath)) == f.Sha256),
                    "Export All of This Type (Artwork): the pictures inside the images and the package are exported with the console's own, each checked and its checksum's origin recorded (" + art.Summary + ")");
                var all = new ExportRequest { Destination = exp, Scope = ExportScope.Folder, Layout = ExportLayout.OriginalPaths, FolderName = "Files inside " + own.Name, ScopeText = "Everything inside " + own.DisplayPath + "." };
                all.Entries.AddRange(c.Children(own.Id));
                var ax = Exporter.Export(c, all);
                int regular = nes.Listing.Count(kv => kv.Value.perm[0] == '-');
                Check(ax.Ok && ax.Files.Count == regular && ax.Files.All(f => Sha(Path.Combine(ax.Folder, f.RelativePath)) == Sha(nes.Files[f.Entry.InnerPath])) && ax.Described.Count == nes.Listing.Count(kv => "lpcbs".IndexOf(kv.Value.perm[0]) >= 0),
                    "Files inside the image: all " + regular + " files exported, each equal to the file it was built from; links, the pipe and the device described (" + ax.Summary + ")");
                // ---- the nested CCS HOME package
                var pk = c.Find("m:" + BackupFormat.ExportName); var inPk = c.Entries.Where(e => e.Container == pk && e.Kind == EntryKind.File).ToList();
                var ccs = Path.Combine(exp, "Console-HOME.ccs"); File.WriteAllBytes(ccs, Member(A, BackupFormat.ExportName));
                var py = Py("import zipfile,hashlib,sys\nz=zipfile.ZipFile(r'" + ccs + "')\nfor i in z.infolist(): print(i.filename, len(z.read(i)), hashlib.sha256(z.read(i)).hexdigest())\n");
                var pyRows = py == null ? null : py.Value.output.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim().Split(' ')).Where(p => p.Length == 3).ToDictionary(p => p[0], p => (long.Parse(p[1]), p[2]));
                var plain = inPk.Where(e => e.Embedded == null).ToList();
                Check(pk.ImageType == "Classic Cart Studio collection package (.ccs)" && pyRows != null && plain.Count == pyRows.Count && plain.All(e => pyRows.TryGetValue(e.InnerPath, out var r) && r.Item1 == e.Size && (e.Sha256 == null || e.Sha256 == r.Item2)),
                    "the CCS HOME package: every file Python's zipfile reads in it is listed under it, with its size (" + plain.Count + " files)");
                var roms = plain.Where(e => e.InnerPath.StartsWith("roms/")).ToList(); var man = plain.First(e => e.InnerPath == "ccs-manifest.json");
                Check(roms.Count > 0 && roms.All(e => e.HashOrigin == HashOrigin.PackageList && e.Sha256 == pyRows[e.InnerPath].Item2 && e.Category == Category.Games) && man.HashOrigin == HashOrigin.Derived && man.Sha256 == pyRows["ccs-manifest.json"].Item2 && man.BoundToSha256 == pk.Sha256
                      && plain.First(e => e.InnerPath == "collection.json").Category == Category.Settings,
                    "its game files carry the checksums its own list recorded (equal to Python's); its list's own checksum is worked out and bound to the package's recorded one");
                var pic = inPk.FirstOrDefault(e => e.Embedded == "startup");
                var picBytes = pic == null ? null : c.ReadVerified(pic, 1000000, out var pw);
                Check(pic != null && pic.HashOrigin == HashOrigin.PackageList && picBytes != null && Sha(picBytes) == pic.Sha256 && BackupContents.ImageKindOf(picBytes) == "jpeg" && pic.Category == Category.Artwork && pic.ParentId == plain.First(e => e.InnerPath == "collection.json").Id + "#",
                    "the startup picture collection.json carries is a file of its own under it, checked against the SHA-256 the collection recorded (" + pic?.Name + ")");
                var copyOwner = roms.Select(e => c.Game(e.GameKey)).FirstOrDefault(g => g != null && g.GameFiles.Any(f => f.Source == EntrySource.DataFile));
                var pb2 = copyOwner == null ? null : Exporter.Export(c, ExportRequest.GameBundle(c, copyOwner, exp));
                Check(copyOwner != null && copyOwner.PackageCopies.Count() == 1 && pb2.Ok && Directory.GetFiles(Path.Combine(pb2.Folder, "In Console-HOME.ccs")).Length == 1,
                    "By game: the package's copy of a game file joins the game's group; its bundle has it under \"In Console-HOME.ccs\" (" + copyOwner?.Title + ")");
                var pall = new ExportRequest { Destination = exp, Scope = ExportScope.Folder, Layout = ExportLayout.OriginalPaths, FolderName = "Files inside " + pk.Name, ScopeText = "Everything inside the package." };
                pall.Entries.AddRange(c.Children(pk.Id));
                var px = Exporter.Export(c, pall); var pxm = Json.Parse(File.ReadAllText(px.ManifestPath)).A("files").OfType<Dictionary<string, object>>().ToList();
                // (the carried picture's full path here is longer than an export keeps, so it may be in the export's "Long names" folder)
                var picRow = pxm.FirstOrDefault(f => f.S("in_backup") == pic.Locator); var picAt = picRow == null ? null : Path.Combine(px.Folder, picRow.S("exported_as"));
                Check(px.Ok && px.Files.Count == inPk.Count && pxm.Count(f => f.S("checksum_from") == "packagelist") == inPk.Count(e => e.HashOrigin == HashOrigin.PackageList) && picAt != null && File.Exists(picAt) && Sha(picAt) == pic.Sha256
                      && (picRow.S("exported_as") == Path.Combine("Files inside Console-HOME.ccs", "Inside collection.json", pic.Name) || picRow.S("exported_as").StartsWith("Long names\\", StringComparison.Ordinal)),
                    "Files inside the package: every file exported and checked, the carried picture beside collection.json (" + px.Summary + "; " + picRow?.S("exported_as") + ")");
                // ---- a complete set of checksums
                c.DeriveMissingHashes();
                Check(c.HashesComplete && c.DerivedIndexPath == null && !Directory.EnumerateFiles(s.RecordsFolder, "derived-index-*").Any(), "every checksum inside the images is worked out; nothing is kept as a derived index (they are worked out again when needed)");
            }
        }

        // ------------------------------------------------------------------ a damaged data storage image: an image with and without per-file proof
        static void NestedDamagedParent(Settings s, string A, BuiltImage nes, string exp)
        {
            var d = Path.Combine(s.PrimaryFolder, "images damaged.zip"); File.Copy(A, d);
            try
            {
                using (var p = new StoredImagePatch(d, "restore/nandc.img")) { p.Flip(p.DataAt("/hakchi/firmware/snes-eur.hsqs") + 300); }
                var row = BackupList.Check(s, d);
                Check(row.Status == BrowseStatus.Salvage && row.DamagedParts.Contains("restore/nandc.img"), "the data storage image damaged where the SNES image lies: salvage (" + row.Detail + ")");
                var so = BackupContents.OpenSalvage(d, s);
                if (!so.Ok) { Check(false, "the salvage open failed: " + so.Problem); return; }
                using (var c = so.Contents)
                {
                    var own = c.Find("d:/ccs/nucleus/roots/owner-1.hsqs"); var sn = c.Find("d:/hakchi/firmware/snes-eur.hsqs");
                    var kids = c.Entries.Where(e => e.Container == own).ToList();
                    Check(own.Unreadable == null && own.HashOrigin == HashOrigin.Inventory && kids.Count == nes.Listing.Count && own.ImageType.StartsWith("System image (SquashFS, gzip)"),
                        "with per-file proof: the image file still matches the checksum the file list recorded, so it is browsed (" + kids.Count + " entries)");
                    var region = kids.First(e => e.InnerPath == "/etc/clover/REGION");
                    Check(c.ExpectedHash(region) == Sha(nes.Files["/etc/clover/REGION"]) && region.BoundToSha256 == own.Sha256 && region.BoundToSha256 == Sha(nes.Bytes),
                        "a file inside it: worked out and bound to the image's recorded checksum, never to the damaged data storage (" + region.HashText + ")");
                    var x = Exporter.Export(c, One(c, region, exp));
                    Check(x.Ok && Json.Parse(File.ReadAllText(x.ManifestPath)).O("backup").B("salvage"), "it exports from the damaged backup, and the export's list says it came from a salvage");
                    Check(sn.Unreadable != null && sn.StateWord == "damaged" && !c.Entries.Any(e => e.Container == sn) && sn.ImageNote != null && sn.ImageNote.Contains("keeps no checksum of each of its files") && c.ExpectedHash(sn) == null,
                        "without per-file proof: the damaged image file is listed as damaged; nothing inside it is shown or given a checksum (" + sn.ImageNote + ")");
                }
                Check(!Directory.EnumerateFiles(s.RecordsFolder, "derived-index-*").Any(), "no derived checksums are kept for a damaged backup");
            }
            finally { File.Delete(d); }
        }

        // ------------------------------------------------------------------ a damaged table of contents, reached from the browser through the part finder
        static void NestedDamagedIndex(Settings s, string A, BuiltImage nes, string dir, string exp)
        {
            var d = Path.Combine(s.PrimaryFolder, "index damaged.zip"); File.Copy(A, d); DamageIndex(d);
            var outDir = Path.Combine(dir, "rebuilt"); Directory.CreateDirectory(outDir);
            try
            {
                var dSha = FastSha(d); var dTime = File.GetLastWriteTimeUtc(d); var before = Directory.GetFiles(s.PrimaryFolder).Select(Path.GetFileName).OrderBy(x => x).ToList();
                Check(!Verifier.VerifyFile(d).Ok && BackupContents.Open(d, s).Problem.Contains("did not pass"), "a damaged table of contents: the backup fails its check and is never opened as a complete backup");
                var row = BackupList.Check(s, d);
                Check(row.Status == BrowseStatus.Salvage && row.IndexDamaged && row.FoundBy != null && row.FoundBy.Contains("repair map") && row.CanOpenSalvage && !row.CanOpen && row.Detail.Contains("table of contents"),
                    "the list: damaged table of contents, its parts found by the part finder, and what can be saved can be opened (" + row.StatusText + ": " + row.Detail + ")");
                var so = BackupContents.OpenSalvage(d, s);
                if (!so.Ok) { Check(false, "the salvage open through the part finder failed: " + so.Problem); return; }
                using (var c = so.Contents)
                {
                    var members = c.Entries.Where(e => e.Source == EntrySource.Member).ToList();
                    var own = c.Find("d:/ccs/nucleus/roots/owner-1.hsqs"); var save = c.Find("d:/clover/profiles/0/CLV-P-NAAAE/cartridge.sram");
                    Check(c.Salvage && c.FoundBy.Contains("repair map") && c.Notes.Any(n => n.Contains("table of contents")) && members.Where(e => e.Member != BackupFormat.ManifestName).All(e => e.HashTrusted && e.Unreadable == null) && c.DataProblem == null,
                        "opened in place through the part finder: every part found and checked against the backup's own records; the data storage is intact and browsed (" + c.Notes[1] + ")");
                    var kids = c.Entries.Where(e => e.Container == own).ToList(); var region = kids.FirstOrDefault(e => e.InnerPath == "/etc/clover/REGION");
                    Check(save != null && save.HashOrigin == HashOrigin.Inventory && kids.Count == nes.Listing.Count && region != null && c.ExpectedHash(region) == Sha(nes.Files["/etc/clover/REGION"]),
                        "its files and the system images inside it are browsed exactly as in the intact backup");
                    var sel = new Selection(); sel.Add(save); sel.Add(region); sel.Add(c.Find("m:restore/nandb.img"));
                    var x = Exporter.Export(c, ExportRequest.Selected(c, sel, exp));
                    Check(x.Ok && x.Files.Count == 3, "a save, a file inside an image and a whole part export and check from it (" + x.Summary + ")");
                }
                var after = Directory.GetFiles(s.PrimaryFolder).Select(Path.GetFileName).OrderBy(x => x).ToList();
                Check(FastSha(d) == dSha && File.GetLastWriteTimeUtc(d) == dTime && after.SequenceEqual(before) && !Verifier.VerifyFile(d).Ok,
                    "browsing it wrote nothing: the damaged file is unchanged, still fails its check (never a restore point), and no file was added beside it");
                // Rebuild from the browser's damaged-backup page: a new file in a folder the person chose, then opened normally
                var r = ZipIndexRecovery.Run(d, outDir);
                Check(r.Outcome == "repaired" && r.OutputPath != null && Path.GetDirectoryName(r.OutputPath) == outDir && FastSha(d) == dSha, "Rebuild: a new backup file in the chosen folder; the damaged file is unchanged (" + r.Text + ")");
                var ro = BackupContents.Open(r.OutputPath, s);
                Check(ro.Ok && !ro.Contents.Salvage && ro.Contents.Entries.Any(e => e.Container?.Id == "d:/ccs/nucleus/roots/owner-1.hsqs"), "the rebuilt backup passes its full check and opens normally, system images included");
                ro.Contents?.Dispose();
                File.Delete(r.OutputPath); File.Delete(d);

                // ---- the table of contents and the data storage image both damaged: each file still checked on its own, through the parts found
                File.Copy(A, d); using (var p = new StoredImagePatch(d, "restore/nandc.img")) p.Flip(p.DataAt("/hakchi/firmware/snes-eur.hsqs") + 300); DamageIndex(d);
                var r2 = BackupList.Check(s, d);
                Check(r2.Status == BrowseStatus.Salvage && r2.IndexDamaged && r2.DamagedParts.Contains("restore/nandc.img") && r2.FilesVerified > 0 && r2.FilesVerified < r2.FilesChecked,
                    "the table of contents and the data storage image both damaged: the part finder finds the parts, and the image's files are checked one by one (" + r2.Detail + ")");
                var s2 = BackupContents.OpenSalvage(d, s);
                if (s2.Ok) using (var c = s2.Contents)
                    {
                        var own = c.Find("d:/ccs/nucleus/roots/owner-1.hsqs"); var sn = c.Find("d:/hakchi/firmware/snes-eur.hsqs");
                        var region = c.Entries.FirstOrDefault(e => e.Container == own && e.InnerPath == "/etc/clover/REGION");
                        Check(own.Unreadable == null && region != null && c.ExpectedHash(region) == Sha(nes.Files["/etc/clover/REGION"]) && region.BoundToSha256 == Sha(nes.Bytes) && sn.StateWord == "damaged" && !c.Entries.Any(e => e.Container == sn),
                            "through the parts found: the intact image is browsed by its own recorded checksum, the damaged one is kept whole and closed");
                    }
                else Check(false, "the salvage open (both damaged) failed: " + s2.Problem);
                File.Delete(d);

                // ---- the table of contents and the manifest both damaged: the repair map, made with it, still names every part
                File.Copy(A, d); Flip(d, EntryOf(A, BackupFormat.ManifestName).DataOffset + 10); DamageIndex(d);
                var r3 = BackupList.Check(s, d);
                Check(r3.Status == BrowseStatus.Salvage && r3.IndexDamaged && r3.DamagedParts.Contains(BackupFormat.ManifestName) && r3.ConsoleTag != null, "the manifest lost as well: salvage, the backup and its console still named from the repair map (" + r3.Detail + ")");
                var s3 = BackupContents.OpenSalvage(d, s);
                if (s3.Ok) using (var c = s3.Contents)
                    {
                        var save = c.Find("d:/clover/profiles/0/CLV-P-NAAAE/cartridge.sram"); var man = c.Find("m:" + BackupFormat.ManifestName);
                        Check(c.Notes.Any(n => n.Contains("manifest at its end) is lost")) && c.DataMember == "restore/nandc.img" && save != null && save.HashOrigin == HashOrigin.Inventory && c.ExpectedHash(save) != null
                              && man != null && man.StateWord == "damaged" && c.Entries.Any(e => e.Container?.Id == "d:/ccs/nucleus/roots/owner-1.hsqs"),
                            "opened through the repair map: the data storage (named by its file list) and the images inside it browse; the lost manifest is listed as damaged");
                    }
                else Check(false, "the salvage open (manifest lost) failed: " + s3.Problem);
            }
            finally { if (File.Exists(d)) File.Delete(d); try { Directory.Delete(outDir, true); } catch (IOException) { } }
        }
    }
}
