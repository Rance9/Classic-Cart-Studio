// Classic Cart Studio console backup and restore engine tests: per-file salvage in the Backup Browser (a damaged data storage image whose
// files are checked one by one against the backup's file list) and supplying a startup software file (D7). GPL-3.0-or-later. Simulated
// consoles only (files on this PC); nothing opens a console session of any kind. Every backup here comes from the small repair console
// (about 640 MB each: the physical image is always 536,870,912 bytes); every damaged copy and output is deleted as soon as its case is done.
// Expected checksums come from the backup's own file list as made at capture time (and, for the fixture files, from the files themselves),
// never from the engine's reader of the damaged copy.
using CcsBackup.Archive;
using CcsBackup.Browse;
using CcsBackup.Ext4;
using CcsBackup.Library;
using CcsBackup.Repair;
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
        static readonly (string, string, long)[] SmallLayout = { ("nanda", "boot", 3072L * 1024), ("nandb", "rootfs", 4096L * 1024), ("nandc", "data", 65536L * 1024), ("nandd", "private", 1024L * 1024), ("nande", "UDISK", 2048L * 1024) };

        static List<Dictionary<string, object>> InventoryOf(string zip) => (Json.Parse(Encoding.UTF8.GetString(Member(zip, BackupFormat.InventoryName))).A("entries") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();

        /// <summary>byte edits inside the data storage image as it is stored in a backup file (tests only): places are found with the engine's
        /// ext4 reader on the stored image, then written straight into the backup file</summary>
        sealed class StoredImagePatch : IDisposable
        {
            readonly FileStream f; readonly long off; readonly Ext4Reader r; readonly long first;
            public StoredImagePatch(string zip, string member)
            {
                var e = EntryOf(zip, member); off = e.DataOffset; f = new FileStream(zip, FileMode.Open, FileAccess.ReadWrite);
                r = new Ext4Reader(new SubStream(f, off, e.Size)); first = BitConverter.ToUInt32(Read(1024, 1024), 0x14);
            }
            byte[] Read(long pos, int n) { var b = new byte[n]; f.Position = off + pos; int g = 0; while (g < n) { int k = f.Read(b, g, n - g); if (k <= 0) throw new EndOfStreamException(); g += k; } return b; }
            void Write(long pos, byte[] b) { f.Position = off + pos; f.Write(b, 0, b.Length); }
            public uint Ino(string p) => r.Lookup(p) ?? throw new InvalidOperationException("not in the image: " + p);
            long InodeAt(uint ino)
            {
                uint g = (ino - 1) / r.InodesPerGroup, i = (ino - 1) % r.InodesPerGroup;
                var d = Read((first + 1) * r.BlockSize + (long)g * r.DescSize, r.DescSize); long table = BitConverter.ToUInt32(d, 8); if (r.DescSize >= 64) table |= (long)BitConverter.ToUInt32(d, 0x28) << 32;
                return table * r.BlockSize + (long)i * r.InodeSize;
            }
            /// <summary>the image offset of a file's first data block</summary>
            public long DataAt(string p) => r.Blocks(r.Inode(Ino(p))).First(b => b != 0) * r.BlockSize;
            /// <summary>the image offset of a block no listed file uses, in the second half of the image</summary>
            public long FreeAt(IEnumerable<string> files)
            {
                var used = new HashSet<long>(); foreach (var p in files) foreach (var b in r.Blocks(r.Inode(Ino(p)))) used.Add(b);
                for (long b = r.BlocksCount - 1; b > r.BlocksCount / 2; b--) if (!used.Contains(b)) return b * r.BlockSize;
                throw new InvalidOperationException("no free block");
            }
            public void Flip(long imagePos) { var b = Read(imagePos, 1); b[0] ^= 0x5A; Write(imagePos, b); }
            public void SetSize(string p, long size) => Write(InodeAt(Ino(p)) + 4, BitConverter.GetBytes((uint)size));
            public void BreakSuperblock() => Flip(1024 + 0x38);   // the ext magic number
            /// <summary>renames a directory entry in place (same length): only the damaged image's own directory says the new name</summary>
            public void RenameEntry(string p, string name)
            {
                int cut = p.LastIndexOf('/'); var parent = p.Substring(0, cut); var old = Encoding.UTF8.GetBytes(p.Substring(cut + 1)); var nb = Encoding.UTF8.GetBytes(name);
                if (nb.Length != old.Length) throw new ArgumentException("same length only");
                var child = Ino(p);
                foreach (var blk in r.Blocks(r.Inode(parent.Length == 0 ? 2u : Ino(parent))))
                {
                    if (blk == 0) continue; var b = Read(blk * r.BlockSize, r.BlockSize); int q = 0;
                    while (q + 8 <= r.BlockSize)
                    {
                        uint ci = BitConverter.ToUInt32(b, q); int rec = BitConverter.ToUInt16(b, q + 4), nl = b[q + 6]; if (rec < 8) break;
                        if (ci == child && nl == old.Length && b.Skip(q + 8).Take(nl).SequenceEqual(old)) { Write(blk * r.BlockSize + q + 8, nb); return; }
                        q += rec;
                    }
                }
                throw new InvalidOperationException("entry not found: " + p);
            }
            public void Dispose() { f.Flush(true); f.Dispose(); }
        }

        // ================================================================== per-file salvage in the Backup Browser
        public static void BrowserSalvage()
        {
            var dir = NewDir("browser-salvage"); var s = SettingsIn(dir, false); string con = null, fat = null;
            var exp = Path.Combine(dir, "export"); Directory.CreateDirectory(exp);
            string Copy(string src, string name) { var p = Path.Combine(s.PrimaryFolder, name + ".zip"); File.Copy(src, p); return p; }
            try
            {
                con = RepairConsole(); var a = Backup(con, s); var A = a.PrimaryPath;
                Check(a.Ok && Verifier.VerifyFile(A).Ok, "the backup to damage is made and verifies (" + a.State + " " + a.Problem + ")");
                var inv = InventoryOf(A);
                var withSha = inv.Where(e => e.S("type") == "f" && e.S("sha256") != null).ToList(); var regular = inv.Count(e => e.S("type") == "f");
                var shaOf = withSha.ToDictionary(e => e.S("path"), e => e.S("sha256"));
                const string Save = "/clover/profiles/0/CLV-P-NAAAE/cartridge.sram", Settings0 = "/clover/profiles/0/settings.json", Other = "/clover/profiles/0/CLV-P-NAACE/cartridge.sram";
                var startup = withSha.First(e => e.S("path").Contains("/startup/startup-custom-")).S("path");

                // ---------------------------------------------------------------- 1. one changed byte in a block no file uses
                var c1 = Copy(A, "unrelated sector");
                using (var p = new StoredImagePatch(c1, "restore/nandc.img")) p.Flip(p.FreeAt(withSha.Select(e => e.S("path"))) + 777);
                var c1Sha = FastSha(c1);
                var row = BackupList.Check(s, c1);
                Check(row.Status == BrowseStatus.Salvage && row.CanOpenSalvage && !row.CanOpen && row.DamagedParts.Contains("restore/nandc.img") && row.FilesVerified == withSha.Count && row.FilesChecked == regular && row.Detail.Contains("can be copied"),
                    "an unrelated changed byte in the data storage image: the backup is damaged (" + row.StatusText + "), but all " + row.FilesVerified + " of " + row.FilesChecked + " files with a recorded checksum still match it (" + row.Detail + ")");
                var o1 = BackupContents.Open(c1, s);
                Check(!o1.Ok && o1.Problem.Contains("did not pass"), "it is never opened as a complete backup (" + o1.Problem + ")");
                var sv1 = BackupContents.OpenSalvage(c1, s);
                Check(sv1.Ok, "opened in salvage mode (" + sv1.Problem + ")");
                if (sv1.Ok) using (var c = sv1.Contents)
                    {
                        var data = c.Entries.Where(e => e.Source == EntrySource.DataFile).ToDictionary(e => e.ImagePath);
                        var files = data.Values.Where(e => e.Kind == EntryKind.File).ToList();
                        Check(c.Salvage && c.Find("m:restore/nandc.img").Unreadable != null && data.Count == inv.Count && inv.All(e => data.ContainsKey(e.S("path"))),
                            "every entry of the backup's file list is listed (" + data.Count + "); the damaged image itself stays marked damaged and cannot be exported");
                        Check(files.Where(e => shaOf.ContainsKey(e.ImagePath)).All(e => e.Unreadable == null && e.HashOrigin == HashOrigin.Inventory && e.Sha256 == shaOf[e.ImagePath])
                              && files.Where(e => !shaOf.ContainsKey(e.ImagePath)).All(e => e.Unreadable != null && !e.HashTrusted) && !files.Any(e => e.HashOrigin == HashOrigin.Derived || e.HashOrigin == HashOrigin.DerivedIndex),
                            "each file with a recorded checksum is readable with exactly that checksum; a file without one is not offered; no checksum is worked out from the damaged image");
                        var noSha = files.FirstOrDefault(e => !shaOf.ContainsKey(e.ImagePath));
                        Check(c.MissingHashes().Count(e => e.Source == EntrySource.DataFile) == 0 && (noSha == null || c.ExpectedHash(noSha) == null), "nothing in the damaged data storage is left to work out a checksum for, and asking for one is refused" + (noSha != null ? " (" + noSha.ImagePath + ")" : ""));
                        var startupKids = c.MissingHashes().Where(e => e.Source != EntrySource.DataFile).ToList();
                        Check(startupKids.All(e => (e.Source == EntrySource.StartupPart || e.Source == EntrySource.RamdiskFile) && c.Find("m:" + e.Member).HashTrusted && c.Find("m:" + e.Member).Unreadable == null)
                              && startupKids.Where(e => e.Source == EntrySource.StartupPart).All(e => c.ExpectedHash(e) != null && e.BoundToSha256 == c.Find("m:" + e.Member).Sha256),
                            "the other files still without a checksum are the startup images' parts and ramdisk files, inside startup images that still match their checksums; theirs are worked out from those images only (" + startupKids.Count + ")");
                        var smb = c.Games.FirstOrDefault(g => g.Code == "CLV-P-NAAAE");
                        Check(smb != null && smb.Title == "Super Mario Bros." && smb.BatterySaves.Single().ImagePath == Save && c.Games.Count >= 4,
                            "By game: the games are found from the verified files (" + string.Join(", ", c.Games.Select(g => g.Title)) + ")");
                        var counts = c.Counts();
                        Check(c.ByType(Category.Saves).Count(e => e.Name == "cartridge.sram") == 3 && counts.Values.Sum(v => v.count) == c.Entries.Count(e => e.Kind != EntryKind.Folder),
                            "By type: every entry in one category (" + string.Join(", ", Categories.All.Select(k => Categories.Label(k) + " " + counts[k].count)) + ")");
                        var roots = c.Children("m:restore/nandc.img").Select(e => e.Name).OrderBy(x => x, StringComparer.Ordinal).ToList();
                        Check(roots.SequenceEqual(inv.Select(e => e.S("path")).Where(x => x.LastIndexOf('/') == 0).Select(x => x.Substring(1)).OrderBy(x => x, StringComparer.Ordinal)),
                            "All files: the data storage's top folders, from the file list (" + string.Join(", ", roots) + ")");
                        Check(c.Search("CARTRIDGE.SRAM").Count == 3 && c.SearchGames("mario").Any(g => g.Code == "CLV-P-NAAAE"), "search works over the salvaged files");
                        var pic = c.ReadVerified(data[startup], 1000000, out var why);
                        Check(pic != null && BackupContents.ImageKindOf(pic) == "jpeg" && Sha(pic) == shaOf[startup], "preview: the startup picture is shown only after its bytes match its recorded checksum (" + why + ")");
                        var whole = Exporter.Export(c, ExportRequest.WholeCategory(c, Category.Saves, exp));
                        var wm = Json.Parse(File.ReadAllText(whole.ManifestPath));
                        bool same = whole.Files.All(f => Sha(Path.Combine(whole.Folder, f.RelativePath)) == shaOf[f.Entry.ImagePath]);
                        Check(whole.Ok && whole.Files.Count == c.ByType(Category.Saves).Count && same && wm.O("backup").B("salvage") && wm.A("files").OfType<Dictionary<string, object>>().All(f => f.S("checksum_from") == "inventory"),
                            "Export All of This Type (Saves) from the damaged backup: every save exported, each equal to the checksum recorded at capture; the export's list says it came from a salvage (" + whole.Summary + ")");
                        var sel = new Selection(); sel.Add(data[Settings0]); sel.Add(data["/hakchi/config"]);
                        var sx = Exporter.Export(c, ExportRequest.Selected(c, sel, exp, ExportLayout.OriginalPaths));
                        Check(sx.Ok && sx.Files.Count == 2, "a selection of salvaged files exports and checks (" + sx.Summary + ")");
                        var gb = Exporter.Export(c, ExportRequest.GameBundle(c, smb, exp));
                        Check(gb.Ok && File.Exists(Path.Combine(gb.Folder, "Battery saves", "cartridge.sram")), "a game bundle exports from the damaged backup (" + gb.Summary + ")");
                        var di = Exporter.Export(c, One(c, c.Find("m:restore/nandc.img"), exp));
                        Check(di.State == "incomplete" && di.Files.Count == 0 && di.NotExported[0].reason.Contains("damaged"), "the damaged image itself is refused with its reason (" + di.NotExported.FirstOrDefault().reason + ")");
                    }
                Check(FastSha(c1) == c1Sha && !Verifier.VerifyFile(c1).Ok && !Directory.EnumerateFiles(s.RecordsFolder, "derived-index-*").Any(),
                    "the damaged backup is byte for byte unchanged, still fails its check (never a restore point), and no derived checksums were kept for it");
                File.Delete(c1);

                // ---------------------------------------------------------------- 2. a file whose own bytes changed
                var c2 = Copy(A, "changed file");
                using (var p = new StoredImagePatch(c2, "restore/nandc.img")) p.Flip(p.DataAt(Save) + 100);
                var r2 = BackupList.Check(s, c2);
                Check(r2.Status == BrowseStatus.Salvage && r2.FilesVerified == withSha.Count - 1, "one file's bytes changed: still salvage, " + r2.FilesVerified + " of " + r2.FilesChecked + " files match (" + r2.Detail + ")");
                var sv2 = BackupContents.OpenSalvage(c2, s);
                if (sv2.Ok) using (var c = sv2.Contents)
                    {
                        var bad = c.Find("d:" + Save); var good = c.Find("d:" + Other);
                        Check(bad.Unreadable != null && bad.StateWord == "damaged" && !bad.HashTrusted && bad.Sha256 == null && bad.Category == Category.Saves && c.Games.First(g => g.Code == "CLV-P-NAAAE").BatterySaves.Contains(bad),
                            "the changed file is listed as damaged, in its place by its recorded path, with no checksum of its own (" + bad.Unreadable + ")");
                        Check(c.ExpectedHash(bad) == null && c.ReadVerified(bad, 1000000, out var w2) == null && w2.Contains("Damaged"), "it is never shown, and no checksum is worked out from its damaged bytes");
                        Check(good.Unreadable == null && good.HashOrigin == HashOrigin.Inventory, "the other saves still check out");
                        var x = Exporter.Export(c, ExportRequest.WholeCategory(c, Category.Saves, exp));
                        Check(x.State == "incomplete" && x.Files.Count == c.ByType(Category.Saves).Count - 1 && x.NotExported.Single().entry == bad && x.NotExported.Single().reason.Contains("Damaged"),
                            "exporting all saves: every other save is exported and checked; the damaged one is listed as not exported with its reason (" + x.Summary + ")");
                    }
                else Check(false, "the salvage open failed: " + sv2.Problem);
                File.Delete(c2);

                // ---------------------------------------------------------------- 3. the file list unavailable as well
                var c3 = Copy(A, "no file list");
                using (var p = new StoredImagePatch(c3, "restore/nandc.img")) p.Flip(p.FreeAt(withSha.Select(e => e.S("path"))) + 99);
                Flip(c3, EntryOf(A, BackupFormat.InventoryName).DataOffset + 40);
                var r3 = BackupList.Check(s, c3);
                Check(r3.Status == BrowseStatus.Damaged && !r3.CanOpenSalvage && r3.DamagedParts.Contains(BackupFormat.InventoryName) && r3.Detail.Contains("file list is damaged"),
                    "the data storage image and its file list both damaged: Damaged, with nothing offered from the data storage (" + r3.Detail + ")");
                var sv3 = BackupContents.OpenSalvage(c3, s);
                if (sv3.Ok) using (var c = sv3.Contents)
                        Check(!c.Entries.Any(e => e.Source == EntrySource.DataFile) && c.DataProblem.Contains("file list") && c.Entries.Where(e => e.Source == EntrySource.Member && e.HashTrusted).Any(),
                            "opened for its other parts only: no file from the damaged image is listed or trusted without its list (" + c.DataProblem + ")");
                else Check(false, "the salvage open failed: " + sv3.Problem);
                File.Delete(c3);

                // ---------------------------------------------------------------- 4. damaged file system metadata
                var c4 = Copy(A, "metadata damaged");
                using (var p = new StoredImagePatch(c4, "restore/nandc.img")) { p.SetSize(Other, 9000); p.RenameEntry(Settings0, "settingz.json"); }
                var r4 = BackupList.Check(s, c4);
                Check(r4.Status == BrowseStatus.Salvage && r4.FilesVerified == withSha.Count - 1, "an inode's size and a directory entry's name changed in the image: " + r4.FilesVerified + " of " + r4.FilesChecked + " files still match (" + r4.Detail + ")");
                var sv4 = BackupContents.OpenSalvage(c4, s);
                if (sv4.Ok) using (var c = sv4.Contents)
                    {
                        var sized = c.Find("d:" + Other); var renamed = c.Find("d:" + Settings0);
                        Check(sized.StateWord == "damaged" && sized.Unreadable.Contains("size") && sized.Size == 8192, "the file whose inode claims another size is damaged; it keeps its recorded size and is never read past it (" + sized.Unreadable + ")");
                        Check(renamed != null && renamed.Unreadable == null && renamed.Sha256 == shaOf[Settings0] && c.Find("d:/clover/profiles/0/settingz.json") == null && !c.Search("settingz").Any(),
                            "the renamed directory entry: the file is listed by its recorded name and still verifies; the damaged directory's name is never shown");
                    }
                else Check(false, "the salvage open failed: " + sv4.Problem);
                File.Delete(c4);
                var c5 = Copy(A, "superblock damaged");
                using (var p = new StoredImagePatch(c5, "restore/nandc.img")) p.BreakSuperblock();
                var r5 = BackupList.Check(s, c5);
                Check(r5.Status == BrowseStatus.Damaged && !r5.CanOpenSalvage && r5.Detail.Contains("cannot be read"), "the image's file system header damaged: nothing inside can be checked, so it is Damaged (" + r5.Detail + ")");
                var sv5 = BackupContents.OpenSalvage(c5, s);
                if (sv5.Ok) using (var c = sv5.Contents)
                    {
                        var files = c.Entries.Where(e => e.Source == EntrySource.DataFile && e.Kind == EntryKind.File).ToList();
                        Check(files.Count == regular && files.All(e => e.Unreadable != null && e.StateWord == "cannot be checked" && !e.HashTrusted) && files.All(e => c.ExpectedHash(e) == null),
                            "its " + files.Count + " files stay listed from the file list, each marked as not checkable and not exportable (" + files[0].Unreadable + ")");
                        var x = Exporter.Export(c, ExportRequest.WholeCategory(c, Category.Saves, exp));
                        Check(x.Files.Count == 0 && x.State == "incomplete", "nothing is exported from it (" + x.Summary + ")");
                    }
                else Check(false, "the salvage open failed: " + sv5.Problem);
                File.Delete(c5); File.Delete(A);

                // ---------------------------------------------------------------- 5. a data storage this version cannot read as a file system
                fat = Path.Combine(dir, "fat.img");
                using (var f = new FileStream(fat, FileMode.CreateNew)) { Device.SimulatedConsole.Pattern(f, 65536L * 1024, "not ext"); f.Position = 54; f.Write(Encoding.ASCII.GetBytes("FAT16   "), 0, 8); f.Position = 510; f.WriteByte(0x55); f.WriteByte(0xAA); f.Position = 1024 + 0x38; f.WriteByte(0); f.WriteByte(0); }
                var fc = Console_("modified-noorig", HwidA, fat, SmallLayout); var fb = Backup(fc, s); var F = fb.PrimaryPath;
                Directory.Delete(Path.GetDirectoryName(fc), true);
                Check(fb.Ok && InventoryOf(F).Count == 0, "a backup whose data storage is not a file system this version reads (" + fb.State + " " + fb.Problem + ")");
                var fo = BackupContents.Open(F, s);
                if (fo.Ok) using (var c = fo.Contents)
                    {
                        var dm = c.Find("m:restore/nandc.img");
                        Check(c.DataProblem.Contains("could not be read as a file system") && dm.ImageType == "FAT file system" && dm.HashTrusted && !c.Entries.Any(e => e.Source == EntrySource.DataFile),
                            "opened: no files are listed from it (" + c.DataProblem + "); its exact image is identified (" + dm.ImageType + ") and is still exportable as one checked file");
                    }
                else Check(false, "the backup did not open: " + fo.Problem);
                var fd = Copy(F, "unreadable damaged");
                Flip(fd, EntryOf(F, "restore/nandc.img").DataOffset + 40000000);
                var rf = BackupList.Check(s, fd);
                Check(rf.Status == BrowseStatus.Damaged && !rf.CanOpenSalvage && rf.Detail.Contains("names no files"), "damaged: nothing to check one by one, so Damaged (" + rf.Detail + ")");
                File.Delete(fd); File.Delete(F);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                if (con != null) try { Directory.Delete(Path.GetDirectoryName(con), true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        // ================================================================== D7: supply a startup software file
        public static void Supply()
        {
            var dir = NewDir("supply"); var s = SettingsIn(dir, false); var outDir = Path.Combine(dir, "out"); Directory.CreateDirectory(outDir); string con = null, mcon = null;
            string Copy(string src, string name) { var p = Path.Combine(dir, name + ".zip"); File.Copy(src, p); return p; }
            bool NoOut() => !Directory.EnumerateFileSystemEntries(outDir).Any(f => !f.EndsWith(".txt", StringComparison.Ordinal) && !f.EndsWith(".json", StringComparison.Ordinal));
            void ClearOut() { foreach (var f in Directory.GetFiles(outDir)) File.Delete(f); }
            const string Active = "restore/boot2-active.img";
            // the files offered are copies in this test's own folder (the shared fixtures are never opened for anything but reading)
            string Local(string name) { var p = Path.Combine(dir, name); File.Copy(Path.Combine(Fx, name), p); return p; }
            var mod = Local("kernel-modified.img"); var other = Local("kernel-other.img"); var rom = Local("rom-1.nes");
            try
            {
                con = RepairConsole(); var a = Backup(con, s); var A = a.PrimaryPath; var rec = RecordedIdentity.Find(s, a.Id);
                var l = Backup(con, s, ctx => ctx.LegacyFormat = true); var L = l.PrimaryPath; var recL = RecordedIdentity.Find(s, l.Id);
                Check(a.Ok && l.Ok && rec != null && Sha(File.ReadAllBytes(mod)) == Verifier.VerifyFile(A).Members[Active].sha256, "two backups (with and without repair information); the startup software in use is the fixture kernel");
                var full = new RepairOptions { RecordedSha256 = rec.Value.sha256, RecordedBytes = rec.Value.bytes };
                var ok = KernelSupply.Run(A, new[] { (Active, mod) }, outDir);
                Check(ok.Outcome == "not-needed" && NoOut(), "an intact backup: nothing is needed and nothing is written (" + ok.Text + ")");
                ClearOut();

                var c = Copy(A, "kernel damaged"); var e = EntryOf(A, Active); Flip(c, e.DataOffset + 5000); Flip(c, e.DataOffset + 140000);
                var cSha = FastSha(c); var modSha = FastSha(mod); var modTime = File.GetLastWriteTimeUtc(mod);
                Check(!Verifier.VerifyFile(c).Ok && ZipIndexRecovery.Run(c, outDir).Outcome == "not-repaired" && NoOut(), "the startup software damaged in the backup's only copy: the other repair routes have no source for it");
                ClearOut();
                var parts = KernelSupply.Parts(c);
                var pa = parts.Parts.FirstOrDefault(x => x.Member == Active);
                Check(parts.Problem == null && pa != null && pa.State == "damaged" && pa.Size == new FileInfo(mod).Length && pa.Sha256 == Sha(File.ReadAllBytes(mod)) && parts.Parts.Count == 1,
                    "the parts the backup records: the startup software in use, damaged, with its recorded size and SHA-256 (" + string.Join(", ", parts.Parts.Select(x => x.Member + " " + x.State)) + ")");

                // refusals: nothing is written, every file read is unchanged
                var wrongHash = KernelSupply.Run(c, new[] { (Active, other) }, outDir);
                Check(wrongHash.Outcome == "refused" && NoOut() && wrongHash.Text.Contains("SHA-256") && wrongHash.Text.Contains("not the file this backup recorded"), "a file of the right size with other bytes: refused (" + wrongHash.Text + ")");
                var wrongSize = KernelSupply.Run(c, new[] { (Active, rom) }, outDir);
                Check(wrongSize.Outcome == "refused" && NoOut() && wrongSize.Text.Contains("40,976 bytes"), "a file of another size: refused before it is read (" + wrongSize.Text + ")");
                var wrongRole = KernelSupply.Run(c, new[] { ("restore/nandc.img", mod) }, outDir);
                Check(wrongRole.Outcome == "refused" && NoOut() && wrongRole.Text.Contains("only for a startup software part"), "a file offered for another kind of part: refused (" + wrongRole.Text + ")");
                var notRecorded = KernelSupply.Run(c, new[] { ("restore/boot2-slot68.img", mod) }, outDir);
                Check(notRecorded.Outcome == "refused" && NoOut() && notRecorded.Text.Contains("do not list"), "a part this backup does not record: refused, nothing to check against (" + notRecorded.Text + ")");
                var itself = KernelSupply.Run(c, new[] { (Active, c) }, outDir);
                Check(itself.Outcome == "refused" && NoOut(), "the backup itself offered as the file: refused (" + itself.Text + ")");
                Check(FastSha(c) == cSha && FastSha(mod) == modSha && File.GetLastWriteTimeUtc(mod) == modTime, "after every refusal the backup and the offered files are unchanged");
                ClearOut();

                // interrupted output: canceled while the new file is written, then while it is checked; the file supplied is held unchangeable
                long seenW = 0; bool heldAgainstWrites = false;
                var cw = KernelSupply.Run(c, new[] { (Active, mod) }, outDir, new RepairOptions
                {
                    Progress = p => { if (p.Phase == "write") { seenW = p.Done; if (!heldAgainstWrites && p.Done > 1000000) try { using (new FileStream(mod, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { } } catch (IOException) { heldAgainstWrites = true; } } },
                    Cancelled = () => seenW > 100L * 1000 * 1000,
                });
                Check(cw.Outcome == "canceled" && cw.Report.Canceled && !Directory.EnumerateFileSystemEntries(outDir).Any(), "canceled while the new backup is written: nothing is left in the folder (" + cw.Text + ")");
                Check(heldAgainstWrites, "while it runs, the file supplied is open for reading only: another program cannot open it to write");
                bool verifying = false;
                var cv = KernelSupply.Run(c, new[] { (Active, mod) }, outDir, new RepairOptions { Progress = p => { if (p.Phase == "verify") verifying = true; }, Cancelled = () => verifying });
                Check(cv.Outcome == "canceled" && !Directory.EnumerateFileSystemEntries(outDir).Any(), "canceled while the new backup is checked: the unfinished file is removed (" + cv.Text + ")");
                Check(FastSha(c) == cSha && FastSha(mod) == modSha, "the backup and the file supplied are unchanged after both cancels");

                // accepted: a new, verified backup, byte-for-byte the original
                var r = KernelSupply.Run(c, new[] { (Active, mod) }, outDir, full);
                Check(r.Outcome == "repaired" && r.RestorePoint && r.OutputCheck.Ok && r.OutputCheck.ArchiveSha256 == rec.Value.sha256 && (bool?)r.Report.Output["identical_to_original"] == true,
                    "the exact file supplied: a new backup that passes the full check and is byte-for-byte the original as recorded (" + r.Text + ")");
                Check(r.Report.Repaired.Any(x => (string)x["what"] == Active && ((string)x["how"]).Contains("the file you supplied")) && r.Report.Others.Any(o => (string)o["role"] == "supplied file" && (string)o["sha256_as_read"] == modSha)
                      && r.Report.Methods.Any(m => m.Contains("only because its size")) && r.Report.Unknowns.Any(u => u.Contains("Where the supplied file came from")),
                    "the report names the file supplied, what it filled in, why it was accepted and what stays unknown");
                Check(FastSha(c) == cSha && FastSha(mod) == modSha && File.GetLastWriteTimeUtc(mod) == modTime && r.Report.Originals.All(o => (bool)o["unchanged"]) && r.Report.Originals.Count == 2,
                    "the damaged backup and the file supplied are unchanged (SHA-256 and time); the report says so for both");
                File.Delete(r.OutputPath); ClearOut(); File.Delete(c);

                // an earlier-format backup (no repair map): the part's header is rebuilt by the writer's rules from the verified content
                var lc = Copy(L, "legacy kernel damaged"); Flip(lc, EntryOf(L, Active).DataOffset + 7000);
                var rl = KernelSupply.Run(lc, new[] { (Active, mod) }, outDir, new RepairOptions { RecordedSha256 = recL.Value.sha256, RecordedBytes = recL.Value.bytes });
                Check(rl.Outcome == "repaired" && rl.OutputCheck.Ok && !rl.OutputCheck.RepairMap, "an earlier-format backup: repaired from the file supplied and passes the full check (" + rl.Text + "; identical to the original: " + rl.Report.Output["identical_to_original"] + ")");
                File.Delete(rl.OutputPath); ClearOut();
                // no trusted record: its manifest damaged too (an earlier-format backup has no repair map to fall back on)
                Flip(lc, EntryOf(L, BackupFormat.ManifestName).DataOffset + 10);
                var nr = KernelSupply.Run(lc, new[] { (Active, mod) }, outDir);
                Check(nr.Outcome == "refused" && NoOut() && nr.Text.Contains("No trustworthy record"), "no surviving record of the backup's parts: refused, nothing to check against (" + nr.Text + ")");
                var np = KernelSupply.Parts(lc);
                Check(np.Problem != null && np.Parts.Count == 0, "and the parts page says so (" + np.Problem + ")");
                File.Delete(lc); ClearOut();
                File.Delete(A); File.Delete(L);

                // a backup with two different startup software files: the original Nintendo kernel kept twice, the startup software in use is
                // hakchi's. The wrong one of this backup's own files is refused for the other part; the right one fills both copies.
                var stock = Path.Combine(dir, "stock-kernel.img"); File.WriteAllBytes(stock, StockKernel());
                mcon = Console_("modified", HwidA, Path.Combine(Fx, "data-modern.img"), SmallLayout); var m = Backup(mcon, s); var M = m.PrimaryPath; var recM = RecordedIdentity.Find(s, m.Id);
                var vm = Verifier.VerifyFile(M);
                Check(m.Ok && vm.Ok && vm.Members.ContainsKey(BackupFormat.OriginalKernelName) && vm.Members.ContainsKey("restore/boot2-slot68.img"), "a modified console's backup with the original kernel and the spare slot (" + m.State + ")");
                var mc = Copy(M, "original kernel damaged");
                Flip(mc, EntryOf(M, BackupFormat.OriginalKernelName).DataOffset + 1000); Flip(mc, EntryOf(M, "restore/boot2-slot68.img").DataOffset + 2000);
                var mp = KernelSupply.Parts(mc);
                Check(mp.Parts.Count == 3 && mp.Parts.Single(x => x.Member == Active).State == "intact" && mp.Parts.Where(x => x.Member != Active).All(x => x.State == "damaged"),
                    "the parts: the startup software in use intact, the original kernel and the spare slot damaged (" + string.Join(", ", mp.Parts.Select(x => x.Member + " " + x.State)) + ")");
                var wm = KernelSupply.Run(mc, new[] { (BackupFormat.OriginalKernelName, mod) }, outDir);
                Check(wm.Outcome == "refused" && NoOut() && wm.Text.Contains("It is this backup's " + Active), "this backup's startup software in use offered as its original kernel: refused, and named for what it is (" + wm.Text + ")");
                var intact = KernelSupply.Run(mc, new[] { (Active, mod) }, outDir);
                Check(intact.Outcome == "refused" && NoOut() && intact.Text.Contains("is intact"), "a file for a part that is intact: refused, nothing needs supplying (" + intact.Text + ")");
                var rm = KernelSupply.Run(mc, new[] { (BackupFormat.OriginalKernelName, stock) }, outDir, new RepairOptions { RecordedSha256 = recM.Value.sha256, RecordedBytes = recM.Value.bytes });
                Check(rm.Outcome == "repaired" && rm.OutputCheck.Ok && rm.OutputCheck.RecoveryBaseline == vm.RecoveryBaseline && rm.OutputCheck.ArchiveSha256 == recM.Value.sha256
                      && rm.Report.Repaired.Count(x => ((string)x["how"]).Contains("the file you supplied")) == 2,
                    "the original kernel supplied: one file fills both damaged copies; the new backup is byte-for-byte the original (" + rm.Text + ")");
                File.Delete(rm.OutputPath); File.Delete(mc); File.Delete(M); File.Delete(stock);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                foreach (var x in new[] { con, mcon }) if (x != null) try { Directory.Delete(Path.GetDirectoryName(x), true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
