// Classic Cart Studio console backup and restore engine tests: the repair map and the repair routes (index, second copy and merge, other
// backups of the same console, salvage). GPL-3.0-or-later. Every backup here is made from a small simulated console (about 640 MB per
// backup: the physical image is always 536,870,912 bytes) and every damaged copy and output is deleted as soon as its case is done; the
// group's folders are removed at the end.
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsBackup.Engine;
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
        static string FastSha(string file) { using (var h = FastHash.Sha256()) using (var f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20)) return Hex.Of(h.ComputeHash(f)); }
        static void Flip(string file, long at, byte x = 0x5A) { using (var f = new FileStream(file, FileMode.Open, FileAccess.ReadWrite)) { f.Position = at; int b = f.ReadByte(); f.Position = at; f.WriteByte((byte)(b ^ x)); } }
        static ZipEntryInfo EntryOf(string zip, string name) { using (var z = ZipReader.Open(zip, BackupFormat.Allowed)) return z.Find(name); }
        /// <summary>damages a backup's table of contents (the ZIP index) only: its first entry's signature and bytes spread through it</summary>
        static void DamageIndex(string zip)
        {
            long cdo, len = new FileInfo(zip).Length; using (var z = ZipReader.Open(zip, BackupFormat.Allowed)) cdo = z.CentralDirectoryOffset;
            Flip(zip, cdo); for (long at = cdo + 12; at < len - 22; at += 37) Flip(zip, at);
        }
        static List<string> Names(string zip) { using (var z = ZipReader.Open(zip, BackupFormat.Allowed)) return z.Entries.OrderBy(e => e.HeaderOffset).Select(e => e.Name).ToList(); }

        /// <summary>a small console (64 MiB data storage with real files), and a ZIP local header pattern inside its user storage image that
        /// names a part this kind of backup never has (recovery/original-kernel.img): it must never be taken as a part</summary>
        static string RepairConsole()
        {
            var lay = new[] { ("nanda", "boot", 3072L * 1024), ("nandb", "rootfs", 4096L * 1024), ("nandc", "data", 65536L * 1024), ("nandd", "private", 1024L * 1024), ("nande", "UDISK", 2048L * 1024) };
            var c = Console_("modified-noorig", HwidA, Path.Combine(Fx, "data-modern.img"), lay);
            using (var f = new FileStream(SimulatedConsole.Dev(c, "nande"), FileMode.Open, FileAccess.Write))
            {
                f.Position = 8192; var w = new BinaryWriter(f); var name = Encoding.ASCII.GetBytes("recovery/original-kernel.img");
                w.Write(0x04034b50u); w.Write((ushort)20); w.Write((ushort)0x0800); w.Write((ushort)0); w.Write(0u); w.Write(0u); w.Write(16u); w.Write(16u); w.Write((ushort)name.Length); w.Write((ushort)0); w.Write(name); w.Write(new byte[16]); w.Flush();
            }
            return c;
        }

        public static void Repair()
        {
            var dir = NewDir("repair"); var s = SettingsIn(dir, false); var outDir = Path.Combine(dir, "out"); Directory.CreateDirectory(outDir);
            string con = null;
            string Copy(string src, string name) { var p = Path.Combine(dir, name + ".zip"); File.Copy(src, p); return p; }
            void Gone(params string[] fs) { foreach (var f in fs) if (f != null && File.Exists(f)) File.Delete(f); }
            void ClearOut() { foreach (var f in Directory.GetFiles(outDir)) File.Delete(f); foreach (var d in Directory.GetDirectories(outDir)) Directory.Delete(d, true); }
            bool NoBackupOut() => !Directory.EnumerateFiles(outDir, "*.zip*", SearchOption.AllDirectories).Any();
            // CCS_REPAIR_SAMPLES=<folder>: keep a copy of some report texts there to read (a few KB; nothing else is kept)
            var samples = Environment.GetEnvironmentVariable("CCS_REPAIR_SAMPLES");
            void Sample(RepairResult r, string name) { if (!string.IsNullOrEmpty(samples) && r?.Report != null) { Directory.CreateDirectory(samples); File.WriteAllText(Path.Combine(samples, name + ".txt"), r.Report.ToText()); File.WriteAllText(Path.Combine(samples, name + ".json"), Json.Write(r.Report.ToJson())); } }
            string P(IEnumerable<string> x) => string.Join("; ", x).Length > 300 ? string.Join("; ", x).Substring(0, 300) + "..." : string.Join("; ", x);
            try
            {
                // ---------------------------------------------------------------- the backups: new format, an older-format one, a later one
                con = RepairConsole();
                var a1 = Backup(con, s); var A1 = a1.PrimaryPath;
                var l1 = Backup(con, s, ctx => ctx.LegacyFormat = true); var L1 = l1.PrimaryPath;
                using (var f = new FileStream(SimulatedConsole.Dev(con, "nandd"), FileMode.Open, FileAccess.Write)) SimulatedConsole.Pattern(f, 1024 * 1024, "a later save");
                var a2 = Backup(con, s); var A2 = a2.PrimaryPath;
                var v1 = Verifier.VerifyFile(A1); var rec1 = RecordedIdentity.Find(s, a1.Id); var recL = RecordedIdentity.Find(s, l1.Id);
                Check(a1.Ok && v1.Ok && v1.RepairMap && rec1 != null && rec1.Value.sha256 == v1.ArchiveSha256, "a new backup carries repair information, and all of it matches the file (" + P(v1.Problems) + ")");
                var names1 = Names(A1);
                Check(names1.First() == BackupFormat.DescriptorName && names1[names1.Count - 2] == BackupFormat.ChunkMapName && names1.Last() == BackupFormat.ManifestName, "the early descriptor is the first part; the chunk map is just before the manifest, which stays last");
                var desc = MapDoc.Parse(Member(A1, BackupFormat.DescriptorName), out var dp);
                Check(desc != null && desc.Find("restore/nandc.img")?.Chunks.Count == 64 && desc.Find(BackupFormat.ManifestName) == null && desc.Find(BackupFormat.ChunkMapName) != null,
                    "the descriptor lists every part but itself and the manifest, with 64 piece checksums for the 67,108,864-byte data storage (" + dp + ")");
                var vl = Verifier.VerifyFile(L1);
                Check(l1.Ok && vl.Ok && !vl.RepairMap && !Names(L1).Any(n => n.StartsWith("repair/")) && recL != null, "a backup in the earlier format (no repair information) still verifies (" + P(vl.Problems) + ")");
                Check(Verifier.VerifyFile(A2).Ok, "the later backup of the same console verifies");
                var nl = ZipIndexRecovery.Run(L1, outDir);
                Check(nl.Outcome == "not-needed" && NoBackupOut(), "an intact backup needs no repair, and nothing is written (" + nl.Text + ")");
                ClearOut();
                // a repair map edited to agree with nothing: the full check refuses the backup
                var bad = Copy(A1, "map-edited");
                { var e = EntryOf(A1, BackupFormat.DescriptorName); var txt = Encoding.UTF8.GetString(Member(A1, BackupFormat.DescriptorName)); int at = txt.IndexOf("\"chunks\": [\"", StringComparison.Ordinal) + 12; Flip(bad, e.DataOffset + at, 0x01); }
                var vb = Verifier.VerifyFile(bad);
                Check(!vb.Ok, "a damaged repair map makes the backup fail its check (" + P(vb.Problems) + ")");
                Gone(bad);
                var full = new RepairOptions { RecordedSha256 = rec1.Value.sha256, RecordedBytes = rec1.Value.bytes };

                // ---------------------------------------------------------------- the table of contents damaged (the repair map finds the parts)
                var c1 = Copy(A1, "index-damaged"); DamageIndex(c1);
                var c1Sha = FastSha(c1);
                Check(!Verifier.VerifyFile(c1).Ok, "a damaged table of contents (ZIP index) makes the backup fail its check");
                var r1 = ZipIndexRecovery.Run(c1, outDir, full);
                Check(r1.Outcome == "repaired" && r1.RestorePoint && r1.OutputCheck.Ok && r1.OutputCheck.RepairMap && r1.OutputCheck.ArchiveSha256 == rec1.Value.sha256 && (bool?)r1.Report.Output["identical_to_original"] == true,
                    "index rebuilt from the repair map: a new file that passes the full check and is byte-for-byte the original as recorded (" + r1.Text + ")");
                Check(Names(r1.OutputPath).SequenceEqual(names1) && !Names(r1.OutputPath).Contains("recovery/original-kernel.img"), "the rebuilt file has exactly the recorded parts; the header pattern inside the user storage image is not a part");
                Check(FastSha(c1) == c1Sha && r1.Report.Originals.All(o => (bool)o["unchanged"]), "the damaged file is unchanged (SHA-256 before and after)");
                Check(r1.ReportText != null && File.ReadAllText(r1.ReportText).Contains("byte-for-byte identical") && File.Exists(r1.ReportJson) && Json.Parse(File.ReadAllText(r1.ReportJson)).S("outcome") == "repaired",
                    "the plain report (text and JSON) names the original and the new file and says it is identical: " + Path.GetFileName(r1.ReportText));
                var py = Py("import zipfile\nz=zipfile.ZipFile(r'" + r1.OutputPath + "')\nprint(z.testzip(), len(z.infolist()))\n");
                Check(py == null || py.Value.output.Trim() == "None " + names1.Count, "Python's zipfile reads and tests the rebuilt file independently: " + py?.output.Trim());
                Sample(r1, "index-rebuilt"); ClearOut();
                // the early descriptor damaged as well: the walk finds the parts; the descriptor's bytes follow from the other records
                Flip(c1, EntryOf(A1, BackupFormat.DescriptorName).DataOffset + 1000);
                var r1b = ZipIndexRecovery.Run(c1, outDir, full);
                Check(r1b.Outcome == "repaired" && r1b.OutputCheck.ArchiveSha256 == rec1.Value.sha256 && r1b.Report.Repaired.Any(x => (string)x["what"] == BackupFormat.DescriptorName) && r1b.Report.Original["located_by"].ToString().Contains("walk"),
                    "index AND early descriptor damaged: parts found by the walk, the descriptor rebuilt from the chunk map and manifest and proven by the manifest's SHA-256; byte-for-byte the original (" + r1b.Text + ")");
                Sample(r1b, "index-and-descriptor"); ClearOut(); Gone(c1);

                // ---------------------------------------------------------------- the end of the file cut off
                var phyE = EntryOf(A1, "reference/nand-physical.bin");
                var c2 = Copy(A1, "truncated"); using (var f = new FileStream(c2, FileMode.Open, FileAccess.Write)) f.SetLength(phyE.DataOffset + phyE.CompressedSize - 300000);
                var c2Sha = FastSha(c2);
                var r2 = ZipIndexRecovery.Run(c2, outDir, full);
                Check(r2.Outcome == "not-repaired" && !r2.RestorePoint && NoBackupOut() && r2.Report.Unrecoverable.Any(u => ((string)u["what"]).Contains("manifest")),
                    "end of the file cut off: no backup is made from it (its manifest is lost), and the report says why (" + r2.Text + ")");
                using (var sv = Salvage.Survey(new[] { c2 }))
                {
                    var nandc = sv.Items.First(i => i.Path == "restore/nandc.img"); var phy = sv.Items.First(i => i.Path == "reference/nand-physical.bin");
                    Check(sv.Map != null && sv.Map.Source.Contains("repair map") && nandc.Exportable && !phy.Exportable && phy.Status == "incomplete" && !sv.Items.First(i => i.Path == BackupFormat.InventoryName).Exportable,
                        "salvage of the cut-off file: the parts before the cut verify through the early descriptor; the cut part and everything after it do not (" + sv.Summary + ")");
                    var ex = Salvage.Export(sv, outDir);
                    var got = Path.Combine(ex.OutputPath ?? "", "backup parts", "restore", "nandc.img");
                    Check(ex.Outcome == "salvaged" && !ex.RestorePoint && File.Exists(got) && FastSha(got) == v1.Members["restore/nandc.img"].sha256 && NoBackupOut() && File.ReadAllText(ex.ReportText).Contains("not a backup"),
                        "the verified parts are exported into a new folder (never a backup file), each exactly its recorded bytes; the report says the folder is not a backup (" + ex.Text + ")");
                }
                ClearOut();

                // ---------------------------------------------------------------- two copies damaged in different places: merged
                var inv = Json.Parse(Encoding.UTF8.GetString(Member(A1, BackupFormat.InventoryName)));
                var victim = inv.A("entries").OfType<Dictionary<string, object>>().First(e => e.S("type") == "f" && e.S("sha256") != null && e.L("size") >= 8192 && e.L("links") == 1);
                var dataE = EntryOf(A1, "restore/nandc.img"); long blockAt;
                using (var fs0 = File.OpenRead(A1)) { var x4 = new Ext4Reader(new SubStream(fs0, dataE.DataOffset, dataE.Size)); blockAt = x4.Blocks(x4.Inode((uint)victim.L("inode"))).First(b => b != 0) * x4.BlockSize; }
                int piece = (int)((blockAt + 100) / RepairMap.ChunkSize);
                var c3 = Copy(A1, "head-damaged");
                Flip(c3, EntryOf(A1, BackupFormat.DescriptorName).DataOffset + 500); Flip(c3, EntryOf(A1, "restore/nanda.img").DataOffset + 4096); Flip(c3, dataE.DataOffset + blockAt + 200);
                var c3Sha = FastSha(c3);
                var plain = new RepairOptions();
                var r3 = Merge.Run(new[] { c2, c3 }, outDir, plain);
                Check(r3.Outcome == "repaired" && r3.OutputCheck.Ok && r3.OutputCheck.ArchiveSha256 == rec1.Value.sha256, "two copies damaged in different places (one cut off, one damaged at its start and in its data storage): one good file, byte-for-byte the original (" + r3.Text + ")");
                Check((string)r3.Report.Output["container"] != null && ((string)r3.Report.Output["container"]).Contains("cannot be proven") && r3.Report.Repaired.Count > 0 && r3.Report.Regions.Count > 0 && r3.Report.Parity.Count > 0,
                    "without the original's recorded checksum the report says identity cannot be proven, and lists what was repaired, from where, and the damaged ranges");
                Check(FastSha(c2) == c2Sha && FastSha(c3) == c3Sha, "both damaged copies are unchanged (SHA-256 before and after)"); Sample(r3, "merge-repaired");
                ClearOut();
                long seenW = 0;
                var cancel = new RepairOptions { Progress = p => { if (p.Phase == "write") seenW = p.Done; }, Cancelled = () => seenW > 100L * 1000 * 1000 };
                var rc = Merge.Run(new[] { c2, c3 }, outDir, cancel);
                Check(rc.Outcome == "canceled" && rc.Report.Canceled && !Directory.EnumerateFileSystemEntries(outDir).Any(), "Cancel while the merged file is written: canceled, and nothing is left in the folder (" + rc.Text + ")");

                // ---------------------------------------------------------------- two copies damaged in the same place: unrecoverable
                var c5 = Copy(A1, "data-damaged"); Flip(c5, dataE.DataOffset + blockAt + 100);
                var r5 = Merge.Run(new[] { c3, c5 }, outDir, plain);
                Check(r5.Outcome == "not-repaired" && NoBackupOut() && r5.Report.Unrecoverable.Any(u => ((string)u["what"]) == "restore/nandc.img, piece " + (piece + 1) + " of 64"),
                    "two copies damaged in the same piece: not repaired, nothing written, the piece named as unrecoverable (" + P(r5.Report.Unrecoverable.Select(u => (string)u["what"])) + ")");
                Sample(r5, "merge-unrecoverable"); ClearOut(); Gone(c2, c3);

                // ---------------------------------------------------------------- salvage: files inside a damaged data storage
                using (var sv = Salvage.Survey(new[] { c5 }))
                {
                    var files = sv.Items.Where(i => i.Kind == "file").ToList(); var vf = files.FirstOrDefault(i => i.Path == victim.S("path"));
                    int withSha = inv.A("entries").OfType<Dictionary<string, object>>().Count(e => e.S("type") == "f" && e.S("sha256") != null);
                    Check(!sv.Items.First(i => i.Path == "restore/nandc.img").Exportable && vf != null && vf.Status == "damaged" && files.Count(i => i.Exportable && i.Sha256 != null) == withSha - 1,
                        "salvage: the damaged data storage stays damaged; the one file whose bytes changed is refused; the other " + (withSha - 1) + " files with a recorded checksum verify (" + sv.Summary + ")");
                    Check(files.Where(i => i.Sha256 == null && i.Status == "verified").Count() == 0, "a file without its own recorded checksum is not offered from a damaged image (no checksum is made up)");
                    var ex = Salvage.Export(sv, outDir, null, i => i.Kind == "file");
                    var one = files.First(i => i.Exportable && i.Size > 0);
                    Check(ex.Outcome == "salvaged" && one.ExportedTo != null && FastSha(one.ExportedTo) == one.Sha256 && vf.ExportedTo == null && ex.Report.Unrecoverable.Any(u => (string)u["what"] == vf.Path) && NoBackupOut(),
                        "the verified files are exported (each exactly its recorded bytes), the damaged one is listed as not recovered (" + ex.Text + ")");
                    Sample(ex, "salvage-files");
                }
                ClearOut();

                // ---------------------------------------------------------------- other backups of the same console
                var rx = Merge.Run(new[] { A1, A2 }, outDir);
                Check(rx.Outcome == "refused" && NoBackupOut(), "two different backups given as copies of one backup: refused, nothing written (" + rx.Text + ")");
                ClearOut();
                var c5Sha = FastSha(c5); var a2Sha = FastSha(A2);
                var r6 = Donor.Run(c5, new[] { A2 }, outDir, full);
                Check(r6.Outcome == "repaired" && r6.OutputCheck.ArchiveSha256 == rec1.Value.sha256 && r6.Report.Repaired.Any(x => ((string)x["how"]).Contains("the backup made")),
                    "a damaged piece of the data storage taken from the later backup, where that piece is unchanged (its recorded checksum matches): byte-for-byte the original (" + r6.Text + ")");
                Check(FastSha(c5) == c5Sha && FastSha(A2) == a2Sha, "the damaged file and the other backup are unchanged");
                Sample(r6, "donor-repaired"); ClearOut(); Gone(c5);
                var c7 = Copy(A1, "save-damaged"); Flip(c7, EntryOf(A1, "restore/nandd.img").DataOffset + 4096);
                var r7 = Donor.Run(c7, new[] { A2 }, outDir, full);
                Check(r7.Outcome == "not-repaired" && NoBackupOut() && r7.Report.Unrecoverable.Any(u => ((string)u["what"]).StartsWith("restore/nandd.img")),
                    "the damaged part changed in the later backup (a newer save): it is NOT used, and nothing is written (" + r7.Text + ")");
                ClearOut();
                Sample(r7, "donor-refused-newer-save"); var r8 = Donor.Run(c7, new[] { A2, L1 }, outDir, full);
                Check(r8.Outcome == "repaired" && r8.OutputCheck.ArchiveSha256 == rec1.Value.sha256 && r8.Report.Repaired.Any(x => ((string)x["how"]).Contains(Path.GetFileName(L1))),
                    "the same part from an earlier-format backup made before the save changed (whole-part checksum and size identical): repaired, byte-for-byte the original (" + r8.Text + ")");
                ClearOut();
                var sc = SecondCopy.Make(c7, outDir);
                Check(sc.Outcome == "refused" && !sc.RestorePoint && NoBackupOut(), "a damaged backup is not copied as a second copy (" + sc.Text + ")");
                ClearOut(); Gone(c7);

                // ---------------------------------------------------------------- earlier-format backups: no repair map
                var lc1 = Copy(L1, "legacy-index-damaged"); DamageIndex(lc1);
                using (var b = BackupFile.Open(lc1, "x"))
                    Check(!b.IndexIntact && b.LocatedBy.Contains("walk") && !b.At.ContainsKey("recovery/original-kernel.img") && b.At.Count == Names(L1).Count, "earlier format, damaged index: the walk through the part headers finds exactly the recorded parts; the header pattern inside an image is never read as a part");
                var r9 = ZipIndexRecovery.Run(lc1, outDir, new RepairOptions { RecordedSha256 = recL.Value.sha256, RecordedBytes = recL.Value.bytes });
                Check(r9.Outcome == "repaired" && r9.OutputCheck.Ok && !r9.OutputCheck.RepairMap && r9.OutputCheck.ArchiveSha256 == recL.Value.sha256, "earlier format: the index rebuilt from the walk; byte-for-byte the original (" + r9.Text + ")");
                ClearOut(); Gone(lc1);
                var lc2 = Copy(L1, "legacy-header-damaged"); Flip(lc2, EntryOf(L1, "restore/nande.img").HeaderOffset + 1);
                var r10 = ZipIndexRecovery.Run(lc2, outDir, new RepairOptions { RecordedSha256 = recL.Value.sha256, RecordedBytes = recL.Value.bytes });
                Check(r10.Outcome == "repaired" && r10.OutputCheck.ArchiveSha256 == recL.Value.sha256 && r10.Report.Repaired.Any(x => ((string)x["what"]).Contains("restore/nande.img")),
                    "earlier format, a damaged part header with an intact index: found through the index, the header rebuilt by the writer's rules; byte-for-byte the original (" + r10.Text + ")");
                ClearOut(); Gone(lc2);

                // ---------------------------------------------------------------- crafted archives: refused, nothing written or extracted
                foreach (var (name, script) in new[] {
                    ("traversal", "z.writestr('../evil.txt', b'x'*100); z.writestr('ccs-console-backup.json', b'{}')"),
                    ("duplicate", "z.writestr('README.txt', b'a'); z.writestr('README.txt', b'b'); z.writestr('ccs-console-backup.json', b'{}')") })
                {
                    var p = Path.Combine(dir, name + ".zip");
                    var pc = Py("import zipfile,warnings\nwarnings.simplefilter('ignore')\nz=zipfile.ZipFile(r'" + p + "','w')\n" + script + "\nz.close()\n");
                    if (pc == null) { Check(false, "Python is required for the crafted-archive cases"); break; }
                    var rr = ZipIndexRecovery.Run(p, outDir);
                    Check(rr.Outcome == "refused" && NoBackupOut() && !Directory.EnumerateFiles(dir, "evil.txt", SearchOption.AllDirectories).Any() && !File.Exists(Path.Combine(Path.GetDirectoryName(dir), "evil.txt")),
                        "a crafted archive (" + name + ") is refused as a source; nothing is written or extracted (" + rr.Text + ")");
                    using (var sv = Salvage.Survey(new[] { p })) Check(sv.Map == null && !sv.Exportable.Any(), "salvage offers nothing from it (" + sv.Problem + ")");
                    ClearOut();
                }

                // ---------------------------------------------------------------- a second copy
                var dest = Path.Combine(dir, "second");
                var s1 = SecondCopy.Make(A1, dest);
                Check(s1.Outcome == "copied" && s1.RestorePoint && FastSha(s1.OutputPath) == rec1.Value.sha256 && s1.SameDrive != "different" && s1.Report.Notes.Any(n => n.Contains("physical drive")),
                    "a verified second copy, byte-for-byte the backup; the same-drive answer is honest (" + s1.SameDrive + ": " + s1.Report.Notes.FirstOrDefault() + ")");
                Check(Drives.Compare(A1, @"\\no-such-server\share", out var dt) == "unknown" && dt.Contains("could not be determined"), "a network folder: whether it is the same drive \"could not be determined\"");
                File.Delete(s1.OutputPath);
                long seenC = 0;
                var s2 = SecondCopy.Make(A1, dest, new RepairOptions { Progress = p => { if (p.Phase == "copy") seenC = p.Done; }, Cancelled = () => seenC > 50L * 1000 * 1000 });
                Check(s2.Outcome == "canceled" && !Directory.EnumerateFiles(dest, "*.zip*").Any(), "Cancel while copying: canceled, the unfinished copy is gone (" + s2.Text + ")");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                if (con != null) try { Directory.Delete(Path.GetDirectoryName(con), true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
