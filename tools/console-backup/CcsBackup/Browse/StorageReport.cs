// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Backup Browser -> Storage: how the console's data storage was used when a backup was made, worked out on this PC from the opened backup
// alone (no console, nothing written to the backup). Every figure says where it comes from:
//   recorded   the data storage's own file system header as the backup holds it (total, free blocks, the reserve, the block size): what the
//              console's storage recorded at that moment, not a reading of the console now
//   measured   each file's length and the space its inode records as allocated, read from the backup's data storage image
//   estimated  a file whose allocation cannot be read (a damaged image listed from the backup's file list): its length in whole blocks
//   unknown    what cannot be worked out; a damaged or incomplete inventory gives no exact total
// Each stored file counts once: hard links (several names, one inode) are one file. The files of system images, startup images and the
// CCS HOME package inside the backup are part of the parts that hold them and are never counted a second time; the backup file's own parts
// are listed apart, because the backup's size is not the console's storage (the raw reference and the logical parts hold the same chip).
// CCS HOME's copies (/var/lib/ccs/pkg/<copy>) are listed with the one the console used (its pkg/current) and the earlier ones kept beside it,
// each with its collection name and games (its MANIFEST.json), when it was installed and the copy before it (its record install/v-<copy>.json)
// and whether an entry of Original Menu names it; and the next CCS HOME update as the setting (Settings > CCS HOME > Earlier HOME versions)
// would make it on this storage: an update the size of the copy in use, sent beside it, the earlier versions the shared retention rule keeps
// and the ones it would remove after the switch, counted by the shared accounting (StorageSpec.Account) with the backup's recorded figures.
// Pictures for the Sega Mini menu (SegaPictures.cs): the files the shared rule offers for removal, with their bytes and the space removing
// them would give back (each stored file once), and the ones of that kind that stay, each with why.
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CcsBackup.Browse
{
    public sealed class StorageReport
    {
        public sealed class Row { public string Label; public int Files; public long Bytes, Allocated; public int Estimated; }
        public sealed class HomeCopy
        {
            public string Copy; public bool InUse; public int Files; public long Bytes, Allocated; public string Collection; public long? MtimeSeconds;
            public bool Recorded; public string Installed, Previous, Named = "no"; public long? Games;   // its record (this program's), its MANIFEST.json, Original Menu's entries
        }
        public sealed class Part { public string Name, What; public long Bytes; public bool Image; }

        public string Backup, ConsoleTag, ConsoleModel, ConsoleBoard, Made, BackupFile;
        public int EarlierKeep = StorageSpec.EarlierKeep(null);     // Settings > CCS HOME > Earlier HOME versions, for the next update's figures
        public bool Partial; public readonly List<string> PartialWhy = new List<string>();
        public long? Total, Free, Reserved, Block, Available, Used;   // recorded: the data storage's header (Available: free outside the reserve; Used: total minus free)
        public int Files, ExtraNames, Folders, Links, Special, Unreadable;
        public long Bytes, Allocated, FolderAllocated; public int EstimatedFiles;
        public long? OtherUse;                                      // recorded use not in a file or folder's own allocation (inode tables, journal, the file system's records)
        public int HashRecorded, HashOther;
        public readonly List<Row> Categories = new List<Row>(), Owners = new List<Row>();
        public readonly List<BrowseEntry> Largest = new List<BrowseEntry>();
        public readonly List<HomeCopy> HomeCopies = new List<HomeCopy>();
        public string HomeCurrent;                                  // the copy pkg/current names ("none", "unreadable" or null: no pointer)
        public readonly List<Part> Parts = new List<Part>();
        public int NestedFiles; public readonly List<string> NestedIn = new List<string>();
        public SegaPictureSet Sega = new SegaPictureSet(); public JObj SegaAccount;   // the Sega Mini menu pictures row

        static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
        public const string HomeDir = "/ccs/pkg/";

        /// <summary>the report for an opened backup (read only: entries and the header the backup holds; the HOME pointer is read through
        /// its verified checksum). Nothing is written anywhere.</summary>
        public static StorageReport Build(BackupContents c, int largest = 25, int? earlierKeep = null) =>
            Compute(c.Entries, c.DataTotalBytes, c.DataFreeBytes, c.DataReservedBytes, c.DataBlockBytes, c.Salvage, c.DataProblem, e => c.ReadVerified(e, SmallMax, out _),
                    new StorageReport { Backup = c.BackupId, ConsoleTag = c.ConsoleTag, ConsoleModel = c.ConsoleModel, ConsoleBoard = c.ConsoleBoard, Made = c.CompletedUtc, BackupFile = c.Path,
                                        EarlierKeep = StorageSpec.EarlierKeep(earlierKeep.HasValue ? (object)(long)earlierKeep.Value : null) }, largest);
        /// <summary>the largest small file read for the report (a HOME pointer, a copy record, a MANIFEST.json, an entry of Original Menu)</summary>
        public const long SmallMax = 4000000;

        /// <summary>the report from a backup's entries and the data storage header it holds (readSmall: a small file's verified bytes, or null)</summary>
        internal static StorageReport Compute(IEnumerable<BrowseEntry> entries, long? total, long? free, long? reserved, long? block, bool salvage, string dataProblem,
                                              Func<BrowseEntry, byte[]> readSmall, StorageReport r, int largest = 25)
        {
            r.Total = total; r.Free = free; r.Reserved = reserved; r.Block = block;
            if (r.Free != null && r.Reserved != null) r.Available = Math.Max(0, r.Free.Value - r.Reserved.Value);
            if (r.Total != null && r.Free != null) r.Used = r.Total.Value - r.Free.Value;
            if (salvage) { r.Partial = true; r.PartialWhy.Add("The backup is damaged: its data storage is listed from its file list, and only files that still match their checksums are known exactly."); }
            if (dataProblem != null) { r.Partial = true; r.PartialWhy.Add(dataProblem); }
            if (r.Total == null) { r.Partial = true; r.PartialWhy.Add("The data storage's own header could not be read from this backup, so its total, free space and reserve are not known."); }
            var all = entries.ToList(); var data = all.Where(e => e.Source == EntrySource.DataFile).ToList();
            if (data.Count == 0 && dataProblem == null && !salvage) { r.Partial = true; r.PartialWhy.Add("No files of the data storage could be listed."); }
            long bs = r.Block ?? StorageSpec.DefaultBlockBytes;   // a damaged image's header unread: the specification's block size (labeled: estimated)
            // each stored file once (a hard link's other names are counted apart, never their bytes)
            var files = new List<BrowseEntry>(); var byInode = new HashSet<uint>();
            foreach (var e in data)
            {
                switch (e.Kind)
                {
                    case EntryKind.Folder: r.Folders++; if (e.Allocated >= 0) r.FolderAllocated += e.Allocated; continue;
                    case EntryKind.Link: r.Links++; if (e.Allocated > 0) r.FolderAllocated += e.Allocated; continue;   // a long link target takes a block
                    case EntryKind.Special: r.Special++; continue;
                }
                if (e.Inode != 0 && !byInode.Add(e.Inode)) { r.ExtraNames++; continue; }
                files.Add(e);
            }
            long Alloc(BrowseEntry e) => e.Allocated >= 0 ? e.Allocated : StorageSpec.Rounded(e.Size, bs);
            foreach (var e in files)
            {
                r.Files++; r.Bytes += e.Size; r.Allocated += Alloc(e); if (e.Allocated < 0) r.EstimatedFiles++;
                if (e.Unreadable != null) r.Unreadable++;
                if (e.HashTrusted) r.HashRecorded++; else r.HashOther++;
            }
            if (r.EstimatedFiles > 0) { r.Partial = true; r.PartialWhy.Add(r.EstimatedFiles.ToString("N0", En) + " file(s) could not be read from the data storage image; their space is estimated from their length in whole blocks."); }
            if (r.Used != null && !r.Partial) r.OtherUse = r.Used.Value - r.Allocated - r.FolderAllocated;
            foreach (var g in files.GroupBy(e => Browse.Categories.Label(e.Category)).OrderByDescending(g => g.Sum(Alloc)))
                r.Categories.Add(new Row { Label = g.Key, Files = g.Count(), Bytes = g.Sum(e => e.Size), Allocated = g.Sum(Alloc), Estimated = g.Count(e => e.Allocated < 0) });
            foreach (var g in files.GroupBy(e => OwnerLabel(e.Owner)).OrderByDescending(g => g.Sum(Alloc)))
                r.Owners.Add(new Row { Label = g.Key, Files = g.Count(), Bytes = g.Sum(e => e.Size), Allocated = g.Sum(Alloc), Estimated = g.Count(e => e.Allocated < 0) });
            r.Largest.AddRange(files.OrderByDescending(Alloc).ThenBy(e => e.ImagePath, StringComparer.Ordinal).Take(largest));
            // CCS HOME's copies and the one in use
            var ptr = data.FirstOrDefault(e => e.Kind == EntryKind.File && e.ImagePath == HomeDir + "current");
            if (ptr != null)
            {
                var b = ptr.Unreadable == null && ptr.Size <= 200 ? readSmall(ptr) : null;
                var t = b == null ? null : Encoding.ASCII.GetString(b).Trim();
                r.HomeCurrent = t == null ? "unreadable" : t.Length == 0 ? "none" : t;
            }
            foreach (var g in files.Where(e => e.ImagePath != null && e.ImagePath.StartsWith(HomeDir, StringComparison.Ordinal) && e.ImagePath.IndexOf('/', HomeDir.Length) > 0)
                                   .GroupBy(e => e.ImagePath.Substring(HomeDir.Length, e.ImagePath.IndexOf('/', HomeDir.Length) - HomeDir.Length)).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var hc = new HomeCopy { Copy = g.Key, InUse = g.Key == r.HomeCurrent, Files = g.Count(), Bytes = g.Sum(e => e.Size), Allocated = g.Sum(Alloc), MtimeSeconds = g.Max(e => e.MtimeSeconds) };
                r.HomeCopies.Add(hc);
            }
            // each copy's record, name and games, and which copies an entry of Original Menu names (its .desktop files in hakchi's folders;
            // entries the console program recorded on a USB drive or SD card are not in the backup: unknown)
            var byPath = new Dictionary<string, BrowseEntry>(StringComparer.Ordinal);
            foreach (var e in files) if (e.ImagePath != null && !byPath.ContainsKey(e.ImagePath)) byPath[e.ImagePath] = e;
            byte[] Small(string path) => byPath.TryGetValue(path, out var se) && se.Unreadable == null && se.Size <= SmallMax ? readSmall(se) : null;
            var named = new HashSet<string>(StringComparer.Ordinal); bool namedUnknown = false;
            foreach (var e in files.Where(e => e.ImagePath != null && e.ImagePath.StartsWith("/hakchi/", StringComparison.Ordinal) && e.ImagePath.EndsWith(".desktop", StringComparison.Ordinal)))
            {
                var b = e.Unreadable == null && e.Size <= 65536 ? readSmall(e) : null;
                if (b == null) { namedUnknown = true; continue; }
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(Encoding.UTF8.GetString(b), "ccs/pkg/([0-9a-f]{16}\\.[0-9a-f]{8})/")) named.Add(m.Groups[1].Value);
            }
            var own = Small("/ccs/owned/published.own");
            if (own != null && System.Text.RegularExpressions.Regex.IsMatch(Encoding.UTF8.GetString(own), "(?m)^file=[0-9a-f]{64} [0-7]{4} /media/")) namedUnknown = true;
            foreach (var h in r.HomeCopies)
            {
                h.Named = named.Contains(h.Copy) ? "yes" : namedUnknown ? "unknown" : "no";
                var rb = Small("/ccs/install/v-" + h.Copy + ".json");
                if (rb != null)
                {
                    try
                    {
                        var rj = Json.Parse(Encoding.UTF8.GetString(rb));
                        if (rj.S("schema") == "ccs-owned-copy/1" && rj.S("copy") == h.Copy)
                        {
                            h.Recorded = true; var pv = rj.S("previous"); if (pv != null && HomePackage.CopyRe.IsMatch(pv)) h.Previous = pv;
                            var iu = rj.S("installed_utc"); if (iu != null && HomeArchive.InstalledRe.IsMatch(iu)) h.Installed = iu;
                        }
                    }
                    catch (FormatException) { }
                }
                var mb = Small(HomeDir + h.Copy + "/MANIFEST.json");
                if (mb != null)
                {
                    try { var mj = Json.Parse(Encoding.UTF8.GetString(mb)); h.Collection = mj.S("label"); var gl = mj.L("games"); if (gl >= 0) h.Games = gl; }
                    catch (FormatException) { }
                }
            }
            // the backup file's own parts (containers) and what is inside them (never counted again)
            foreach (var e in all.Where(e => e.Source == EntrySource.Member && e.Kind == EntryKind.File).OrderBy(e => e.Name, StringComparer.Ordinal))
                r.Parts.Add(new Part { Name = e.Member ?? e.Name, What = e.ImageType ?? e.CategoryReason, Bytes = e.Size, Image = e.IsImage });
            var nested = all.Where(e => e.Container != null && e.Kind == EntryKind.File).ToList();
            r.NestedFiles = nested.Count; r.NestedIn.AddRange(nested.Select(e => e.Container.DisplayPath).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal));
            // pictures for the Sega Mini menu (SegaPictures: the shared rule; removing them counted once each by the shared accounting)
            r.Sega = SegaPictures.Find(entries, e => { var b = readSmall(e); return b == null ? null : Encoding.UTF8.GetString(b); }, !salvage && dataProblem == null);
            r.SegaAccount = r.Sega.Eligible.Count == 0 ? null : SegaPictures.Account(r.Sega, r.Profile, r.Available, r.Block);
            return r;
        }

        /// <summary>a file's space: as its inode records it, or (not readable) its length in whole blocks, estimated</summary>
        public long AllocationOf(BrowseEntry e) => e.Allocated >= 0 ? e.Allocated : StorageSpec.Rounded(e.Size, Block ?? StorageSpec.DefaultBlockBytes);

        static string OwnerLabel(string owner)
        {
            switch (owner)
            {
                case "classic-cart-studio": return "Classic Cart Studio";
                case "hakchi": return "Hakchi and its mods";
                case "nintendo": return "The console's own data (saves and settings included)";
                default: return "Other programs and unknown files";
            }
        }

        static string Mb(long? b) => b == null ? "not known" : Units.Gb(b.Value) + " (" + b.Value.ToString("N0", En) + " bytes)";

        /// <summary>the storage profile of the backup's console (the specification's references): its board, else its model's name</summary>
        public string Profile => ConsoleBoard == "dp-nes" || ConsoleBoard == null && ConsoleModel == "NES Classic Edition" ? "nes-classic-edition"
                               : ConsoleBoard == "dp-shvc" ? "snes-classic-edition" : null;

        /// <summary>the next CCS HOME update as this storage and the setting would make it (null: no copy in use): an update the size of the copy
        /// in use is sent beside it (the peak), then the earlier copies the shared retention rule does not keep are removed. A copy is assumed
        /// unchanged here; the console checks each against its record again before it removes one</summary>
        public sealed class NextUpdate
        {
            public int Keep; public string Current; public JObj Retention, Account; public List<string> Remove;
            public string Status => Account["status"] as string;
            public long? Peak => N("peak_bytes"); public long? Steady => N("steady_bytes"); public long? Available => N("available_bytes");
            long? N(string k) => Account[k] is long v ? v : Account[k] is int i ? i : (long?)null;
        }
        public NextUpdate NextHomeUpdate()
        {
            var cur = HomeCopies.FirstOrDefault(h => h.InUse); if (cur == null) return null;
            const string next = "next-update";
            var items = HomeCopies.Select(h => (object)new Dictionary<string, object> { ["copy"] = h.Copy, ["owned"] = h.Recorded, ["unchanged"] = true, ["referenced"] = h.Named != "no", ["previous"] = h.Previous }).ToList();
            items.Add(new Dictionary<string, object> { ["copy"] = next, ["owned"] = true, ["unchanged"] = true });
            var ret = StorageSpec.Retention(new Dictionary<string, object> { ["keep"] = (long)EarlierKeep, ["current"] = next, ["prior"] = cur.Copy, ["copies"] = items.ToArray() });
            var remove = ((List<object>)ret["remove"]).Cast<string>().ToList();
            var storage = Free != null && Reserved != null ? new Dictionary<string, object> { ["source"] = "measured", ["free_bytes"] = Free.Value, ["reserved_bytes"] = Reserved.Value, ["block_bytes"] = Block ?? StorageSpec.DefaultBlockBytes } : null;
            var acc = new Dictionary<string, object>
            {
                ["profile"] = Profile ?? "unknown", ["storage"] = storage,
                ["items"] = new object[] { new Dictionary<string, object> { ["category"] = "home", ["label"] = "the next CCS HOME update (the size of the copy in use)", ["estimate"] = new Dictionary<string, object> { ["logical_bytes"] = cur.Bytes, ["allocated_bytes"] = cur.Allocated } } },
                ["removed_after"] = HomeCopies.Where(h => remove.Contains(h.Copy)).Select(h => (object)new Dictionary<string, object> { ["label"] = h.Copy, ["allocated_bytes"] = h.Allocated }).ToArray(),
            };
            return new NextUpdate { Keep = EarlierKeep, Current = cur.Copy, Retention = ret, Account = StorageSpec.Account(acc), Remove = remove };
        }

        static string CopyLine(HomeCopy h) => h.Copy + (h.InUse ? " (in use)" : " (an earlier copy kept on the console)") + ": " + (h.Collection ?? "collection name not known")
            + (h.Games != null ? ", " + h.Games.Value.ToString("N0", En) + " game(s)" : "") + ", installed " + (h.Installed ?? "date not known") + (h.Recorded ? "" : ", no record of this program's")
            + (h.Named == "yes" ? ", named by an entry of Original Menu" : h.Named == "unknown" ? ", Original Menu's entries on a USB drive or SD card may name it" : "")
            + "; " + Mb(h.Bytes) + " long, " + Mb(h.Allocated) + " on the storage, " + h.Files.ToString("N0", En) + " file(s)";
        static string When(string utc)
        {
            DateTime t; return DateTime.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t) ? t.ToString("MMMM d, yyyy 'at' HH:mm 'UTC'", En) : "at a time not known";
        }

        /// <summary>the plain text report (what Export Report writes)</summary>
        public string Text()
        {
            var sb = new StringBuilder();
            void L(string s = "") => sb.Append(s).Append("\r\n");
            L("Storage report: console data storage in a backup");
            L("Backup: " + (Backup ?? "not known") + " of console " + (ConsoleTag ?? "not known") + (ConsoleModel != null ? " (" + ConsoleModel + ")" : "") + ", made " + When(Made));
            L("Backup file: " + BackupFile);
            L("Worked out on this PC from the backup alone. Nothing was written to the backup or to any console.");
            L();
            if (Partial) { L("PARTIAL: these figures are not an exact total."); foreach (var w in PartialWhy) L("  - " + w); L(); }
            L("Data storage, as its own header recorded it when the backup was made (not a reading of the console now):");
            L("  Total:                           " + Mb(Total));
            L("  Free (the reserve included):     " + Mb(Free));
            L("  Kept in reserve by the system:   " + Mb(Reserved));
            L("  Available outside the reserve:   " + Mb(Available));
            L("  In use:                          " + Mb(Used));
            L("  Block size:                      " + (Block == null ? "not known" : Block.Value.ToString("N0", En) + " bytes"));
            L();
            L("Files, each stored file counted once (" + Files.ToString("N0", En) + " files" + (ExtraNames > 0 ? "; " + ExtraNames.ToString("N0", En) + " more name(s) of the same files, counted once" : "") + "):");
            L("  Their length:                    " + Mb(Bytes));
            L("  Space allocated to them:         " + Mb(Allocated) + (EstimatedFiles > 0 ? " (" + EstimatedFiles.ToString("N0", En) + " estimated)" : " (measured)"));
            L("  Folders (" + Folders.ToString("N0", En) + ") and links (" + Links.ToString("N0", En) + "):        " + Mb(FolderAllocated));
            L("  Other use of the storage:        " + (OtherUse == null ? "not known" : Mb(OtherUse) + " (the file system's own records and journal)"));
            L("  Checksums recorded when the backup was made: " + HashRecorded.ToString("N0", En) + " file(s); worked out on this PC when needed: " + HashOther.ToString("N0", En) + (Unreadable > 0 ? "; cannot be read on this PC: " + Unreadable.ToString("N0", En) : ""));
            L();
            L("By type:"); foreach (var x in Categories) L("  " + x.Label.PadRight(36) + Mb(x.Allocated) + ", " + x.Files.ToString("N0", En) + " file(s)");
            L();
            L("By who added them:"); foreach (var x in Owners) L("  " + x.Label.PadRight(56) + Mb(x.Allocated) + ", " + x.Files.ToString("N0", En) + " file(s)");
            L();
            L("CCS HOME on the console" + (HomeCurrent == null ? ": no pointer (pkg/current) in this backup" : ", pkg/current names: " + HomeCurrent) + ":");
            if (HomeCopies.Count == 0) L("  no CCS HOME copy");
            foreach (var h in HomeCopies) L("  " + CopyLine(h));
            var nu = NextHomeUpdate();
            if (nu != null)
            {
                L("The next CCS HOME update, keeping " + EarlierKeep.ToString(En) + " earlier version(s) (Settings > CCS HOME > Earlier HOME versions), for an update the size of the copy in use:");
                L("  Sent beside the copy in use (the peak):  " + Mb(nu.Peak) + " needed, " + Mb(nu.Available) + " available: " + nu.Status);
                L("  Removed after the switch:                " + (nu.Remove.Count == 0 ? "none" : string.Join(", ", nu.Remove)));
                L("  After the update:                        " + Mb(nu.Steady) + " needed");
                L("  Each earlier copy is checked against its record on the console before it is removed; a copy that differs, or that an entry of Original Menu names, is kept.");
            }
            L();
            L("Largest files:"); foreach (var e in Largest) L("  " + (Mb(AllocationOf(e)) + (e.Allocated >= 0 ? "" : " estimated")).PadRight(40) + " " + e.DisplayPath);
            L();
            L("The backup file's own parts (they hold the console's storage areas; together they are larger than the console's storage, because the raw reference copy and the separate parts hold the same chip):");
            foreach (var p in Parts) L("  " + p.Name.PadRight(36) + Mb(p.Bytes));
            if (NestedFiles > 0) L("  Files inside system images, startup images and packages (" + NestedFiles.ToString("N0", En) + " listed so far) belong to the parts that hold them and are not counted again.");
            L();
            L("MB = 1,000,000 bytes.");
            return sb.ToString();
        }

        /// <summary>writes the text report into a NEW folder inside the chosen folder, the export's way: the destination checked, a new file
        /// created (never written over), flushed, read back from the disk and compared with its SHA-256 before it gets its name</summary>
        public static ExportResult Export(StorageReport r, string destination)
        {
            var res = new ExportResult();
            var why = Exporter.CheckDestination(destination); if (why != null) { res.Problem = why; return res; }
            var bytes = new UTF8Encoding(false).GetBytes(r.Text()); string sha; using (var h = SHA256.Create()) sha = Hex.Of(h.ComputeHash(bytes));
            try
            {
                var root = Exporter.NewExportFolder(System.IO.Path.GetFullPath(destination).TrimEnd('\\'), "Storage Report - " + SafeNames.Component(System.IO.Path.GetFileNameWithoutExtension(r.BackupFile ?? "backup"), 70));
                res.Folder = root;
                var final = System.IO.Path.Combine(root, "storage-report.txt"); var tmp = final + ".part";
                using (var f = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(bytes, 0, bytes.Length); f.Flush(true); }
                string back; using (var f = new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.None)) using (var h = SHA256.Create()) back = Hex.Of(h.ComputeHash(f));
                if (back != sha) { File.Delete(tmp); res.Problem = "The report read back from the disk did not match what was written; it was removed."; return res; }
                File.Move(tmp, final);
                res.Bytes = bytes.LongLength; res.State = "complete"; res.ManifestPath = final;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { res.Problem = "The report could not be written there (" + ex.Message + ")."; }
            return res;
        }
    }
}
