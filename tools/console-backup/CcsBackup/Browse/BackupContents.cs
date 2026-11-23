// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// One backup opened for browsing, read-only, with no console and no transport involved. Open checks the file from scratch first (the
// engine's Verifier: every member against the manifest, the console facts, the whole file's SHA-256) and keeps it open for reading only
// for as long as it is browsed, so no program can change, rename or delete it between the check and any later read. Then:
//   - every member of the backup file becomes an entry (storage images are identified by what they hold; the data storage is browsed);
//   - every entry of the data storage's ext4 file system becomes an entry with its exact metadata (the modification time with its
//     nanoseconds and epoch bits, as the console's clock recorded it; times before 2016 or after the backup itself are flagged);
//   - each file's SHA-256 comes from the backup's file list (inventory/data-files.json, recorded when the backup was made) when it has one;
//     otherwise it is worked out on request by reading the file through the verified data storage image ("Derived"), and a complete set of
//     such checksums is kept beside the backup's records as a separate derived index (derived-index-<archive>.json), bound to the
//     verified file's SHA-256 and the data image's: never written into the backup, never a retrofitted historical checksum;
//   - the Classifier gives each entry its By-type category and, when documented structures say so, its game;
//   - system images, the console's own system part and the nested CCS HOME package are listed with what is inside them (BackupNested.cs);
//     the startup images' parts and ramdisk files, and the encrypted system part decrypted with a key a startup image in the same backup
//     carries, read only and in memory (BackupStartup.cs).
// OpenSalvage opens a backup that fails its full check when something in it still matches a trusted checksum (the write-once capture
// record or the backup's own manifest, when they do not disagree); members that no longer match are listed as damaged and cannot be
// exported. A backup whose table of contents (its ZIP index) is damaged is opened the same way through the parts Backup Repair's part
// finder locates and checks (SalvageCheck.Assess; Repair/Examination.cs): the file is only read, nothing is rebuilt or written, and it is
// never a restore point. Its data storage:
//   - the image still matches its trusted checksum: browsed as above, every export checked;
//   - the image is damaged but the file list is intact: the entries are the file list's own (names, kinds and metadata as recorded), never
//     the damaged image's directories, and each file is readable only when its bytes, read from the damaged image, still match the size
//     and SHA-256 the list recorded for it (SalvageCheck: Repair.Salvage's per-file proof). The rest are listed as damaged or as not
//     checkable, with the reason; no checksum is ever worked out from the damaged image;
//   - the file list is damaged too, or the image holds no file system this version reads: nothing inside it is offered (its exact image
//     is still a member, damaged or not).
// A salvage open is never a complete backup and has no restore role.
using CcsBackup.Archive;
using CcsBackup.Ext4;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace CcsBackup.Browse
{
    public sealed class OpenResult
    {
        public BackupContents Contents; public string Problem; public bool Canceled; public Verification Verification;
        public readonly List<string> Problems = new List<string>();
        public bool Ok => Contents != null;
    }

    public sealed partial class BackupContents : IDisposable
    {
        const long EarlierHashLimit = 64_000_000;   // backups made before every file was hashed in full left larger files without a checksum

        public const string DerivedSchema = "ccs-derived-file-index/1";
        public static readonly DateTime EarliestPlausible = new DateTime(2016, 1, 1, 0, 0, 0, DateTimeKind.Utc);   // the NES Classic did not exist before
        public const string ClockText = "File dates come from the console's own clock, which may not have been set correctly; they are shown exactly as recorded.";

        public string Path { get; }
        public bool Salvage { get; private set; }
        public Verification Verification { get; private set; }
        public string BackupId, ConsoleTag, StateText, CompletedUtc, ArchiveSha256, DataMember, DataMemberSha256, DataProblem, FileSystemCondition, ConsoleModel, ConsoleBoard, ConsoleRegion;
        /// <summary>the console model this backup is of (null: not one this version supports); the NES save converter is offered only for an NES
        /// Classic's saves, never another model's (their saves are kept and exported exactly as they are)</summary>
        public Device.ConsoleModel Model;
        public bool NesSaves => Model == null || Model.IsNes;
        /// <summary>a damaged backup opened for what can be saved: the check's first problem as it was found (for Technical details)</summary>
        public string DamageDetail;
        public bool Simulated;
        public readonly List<string> Notes = new List<string>();
        /// <summary>the data storage's own file system header as the backup holds it (total, free blocks with the reserve included, the
        /// reserve, the block size, in bytes); null when its header could not be read</summary>
        public long? DataTotalBytes, DataFreeBytes, DataReservedBytes, DataBlockBytes;
        void DataHeader(Ext4Reader fs) { long bs = fs.BlockSize; DataTotalBytes = fs.BlocksCount * bs; DataFreeBytes = fs.FreeBlocks * bs; DataReservedBytes = fs.ReservedBlocks * bs; DataBlockBytes = bs; }
        public IReadOnlyList<BrowseEntry> Entries { get; private set; }
        public IReadOnlyList<GameGroup> Games { get; private set; }
        public string DerivedIndexPath { get; private set; }

        readonly Settings settings; FileStream hold;
        Ext4Reader liveFs;   // a console's data storage read live (Console Browser); null for a backup file
        public bool Live => liveFs != null;
        readonly Dictionary<string, BrowseEntry> byId = new Dictionary<string, BrowseEntry>(StringComparer.Ordinal);
        readonly Dictionary<string, List<BrowseEntry>> children = new Dictionary<string, List<BrowseEntry>>(StringComparer.Ordinal);
        readonly Dictionary<string, GameGroup> gamesByKey = new Dictionary<string, GameGroup>(StringComparer.Ordinal);
        readonly Dictionary<string, ZipEntryInfo> zip = new Dictionary<string, ZipEntryInfo>(StringComparer.Ordinal);
        readonly Dictionary<uint, List<BrowseEntry>> byInode = new Dictionary<uint, List<BrowseEntry>>();   // the data storage's files by inode (hard links share one)
        readonly object gate = new object();

        BackupContents(string path, Settings s, FileStream hold) { Path = path; settings = s; this.hold = hold; }

        // ------------------------------------------------------------------ opening
        /// <summary>Opens a backup for browsing after it passes its full check. settings (optional): where the check is recorded (its date
        /// shows in the backup list) and where a derived index is kept. Nothing in the backup file is ever changed.</summary>
        public static OpenResult Open(string path, Settings settings = null, Func<bool> cancelled = null, Action<long, long> progress = null)
        {
            if (External.ExternalFormat.Is(path)) return OpenStorage(path, settings, cancelled, progress);   // a USB and SD backup (BackupContents.Storage.cs)
            var r = new OpenResult();
            var hold = Hold(path, r); if (hold == null) return r;
            try
            {
                var v = Verifier.VerifyFile(path, null, cancelled, progress);
                r.Verification = v;
                if (!v.Ok)
                {
                    r.Problems.AddRange(v.Problems);
                    r.Problem = "The backup did not pass its check, so it is not opened as a complete backup: " + v.Problems[0];
                    try { var a = SalvageCheck.Assess(path, v, settings, cancelled); CheckLog.Record(settings, path, a.Salvageable ? "salvage" : "damaged", v, a); } catch (OperationCanceledException) { }
                    hold.Dispose(); return r;
                }
                var c = new BackupContents(path, settings, hold) { Verification = v };
                c.Build(v.Manifest, v.Members.ToDictionary(k => k.Key, k => (k.Value.sha256, k.Value.size)), null, cancelled);
                c.ArchiveSha256 = v.ArchiveSha256;
                CheckLog.Record(settings, path, "verified", v, null);
                c.LoadDerivedIndex();
                r.Contents = c; return r;
            }
            catch (OperationCanceledException) { hold.Dispose(); r.Canceled = true; r.Problem = "Opening the backup was canceled. Nothing was changed."; return r; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ZipFormatException || ex is Ext4Exception || ex is FormatException)
            { hold.Dispose(); r.Problem = "The backup could not be opened: " + ex.Message; return r; }
        }

        /// <summary>Opens a backup that fails its full check in salvage mode: only when at least one member still matches a trusted
        /// checksum. A damaged data storage image offers each file whose own bytes still match the file list's checksum. Every file is
        /// still checked before it is exported; damaged members and files cannot be exported.</summary>
        public static OpenResult OpenSalvage(string path, Settings settings = null, Func<bool> cancelled = null, Action<long, long> progress = null)
        {
            var r = new OpenResult();
            if (External.ExternalFormat.Is(path)) { r.Problem = "This is a USB and SD backup: what can be saved from it is exported with Save What Can Be Saved under USB and SD Backup."; return r; }
            var hold = Hold(path, r); if (hold == null) return r;
            try
            {
                var v = Verifier.VerifyFile(path, null, cancelled, progress);
                r.Verification = v;
                if (v.Ok) { hold.Dispose(); r.Problem = "This backup passes its full check; open it normally."; return r; }
                var a = SalvageCheck.Assess(path, v, settings, cancelled);
                r.Problems.AddRange(v.Problems);
                CheckLog.Record(settings, path, a.Salvageable ? "salvage" : "damaged", v, a);
                if (a.Manifest == null || a.Intact.Count == 0) { hold.Dispose(); r.Problem = "Nothing in this backup can still be checked against a trusted checksum" + (a.Problem != null ? " (" + a.Problem + ")" : "") + ", so nothing can be offered from it."; return r; }
                var c = new BackupContents(path, settings, hold) { Verification = v, Salvage = true, parts = a.Parts, FoundBy = a.FoundBy };
                var trusted = a.Trusted.Where(kv => !a.Damaged.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => (kv.Value.sha256, kv.Value.size));
                c.Build(a.Manifest, trusted, a, cancelled);
                c.DamageDetail = v.Problems[0];
                c.Notes.Insert(0, "This backup did not pass its full check, so it is not a complete backup and cannot be restored. Only files that can still be checked against a trusted checksum are offered.");
                if (a.Parts != null) c.Notes.Insert(1, "Its table of contents is damaged, so its parts were found another way, each checked against the backup's own records. The file is only read: nothing was rebuilt or written.");
                if (a.ManifestFromMap) c.Notes.Insert(a.Parts != null ? 2 : 1, "The backup's own list of its parts (the manifest at its end) is lost; its repair map, made at the same time, gives every part's place and checksum.");
                r.Contents = c; return r;
            }
            catch (OperationCanceledException) { hold.Dispose(); r.Canceled = true; r.Problem = "Opening the backup was canceled. Nothing was changed."; return r; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ZipFormatException || ex is Ext4Exception || ex is FormatException)
            { hold.Dispose(); r.Problem = "The backup could not be opened: " + ex.Message; return r; }
        }

        static FileStream Hold(string path, OpenResult r)
        {
            try { return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            { r.Problem = ex is FileNotFoundException || ex is DirectoryNotFoundException ? "The backup file was not found." : "The backup file could not be opened for reading (" + ex.Message.TrimEnd('.') + ")."; return null; }
        }

        FileStream Read() => new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);

        void Build(Dictionary<string, object> m, Dictionary<string, (string sha256, long size)> trusted, SalvageCheck.Assessment salvage, Func<bool> cancelled)
        {
            BackupId = m.S("backup_id"); ConsoleTag = m.O("console")?.S("tag"); StateText = m.O("state")?.S("text"); CompletedUtc = m.S("completed_utc"); Simulated = m.B("simulated");
            ConsoleModel = m.O("console")?.S("model"); ConsoleBoard = m.O("console")?.S("board"); ConsoleRegion = m.O("console")?.S("region");
            Model = Device.ConsoleModels.OfManifest(m);
            var all = new List<BrowseEntry>();
            using (var f = Read())
            {
                var z = Zip(f);
                foreach (var ze in z.Entries) zip[ze.Name] = ze;
                var dev = (m.O("layout")?.A("partitions") ?? new List<object>()).OfType<Dictionary<string, object>>().FirstOrDefault(p => p.S("label") == "data" || p.S("label") == "rootfs_data")?.S("device");
                DataMember = dev == null ? salvage?.DataMember : "restore/" + dev + ".img";   // without the manifest's layout: the part its file list names (SalvageCheck)
                // the members, under virtual folders for their name prefixes
                foreach (var ze in z.Entries.OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    var name = ze.Name; int slash = name.IndexOf('/');
                    if (slash > 0 && !byId.ContainsKey("z:" + name.Substring(0, slash)))
                        Add(all, new BrowseEntry { Id = "z:" + name.Substring(0, slash), ParentId = "", Name = name.Substring(0, slash), Source = EntrySource.VirtualFolder, Kind = EntryKind.Folder, TypeCode = 'd', Category = Category.SystemFiles, CategoryReason = "A folder inside the backup file." });
                    var e = new BrowseEntry { Id = "m:" + name, ParentId = slash > 0 ? "z:" + name.Substring(0, slash) : "", Name = slash > 0 ? name.Substring(slash + 1) : name, Source = EntrySource.Member, Kind = EntryKind.File, TypeCode = 'f', Member = name, Size = ze.Size, Mode = 0x1A4, Owner = "backup" };
                    if (salvage != null && salvage.Damaged.Contains(name)) { e.StateWord = "damaged"; e.Unreadable = "This part of the backup no longer matches its recorded checksum (damaged)."; e.HashOrigin = HashOrigin.Unreadable; e.HashText = e.Unreadable; }
                    else if (trusted.TryGetValue(name, out var t) && t.size == ze.Size) { e.Sha256 = t.sha256; e.HashOrigin = HashOrigin.Manifest; e.HashText = salvage == null ? "Checked against the backup's own list when the backup was opened." : "Checksum from the backup's surviving records (each export is checked against it)."; }
                    else if (name == BackupFormat.ManifestName && salvage == null) { var h = new HashingSink(null); using (var s = z.OpenMember(ze)) Streams.Copy(s, h, -1, cancelled); h.Finish(); e.Sha256 = h.Sha256; e.HashOrigin = HashOrigin.Derived; e.HashText = "Worked out from the verified backup file (the backup's own list cannot list itself)."; }
                    else { e.HashOrigin = HashOrigin.None; e.HashText = "No trusted checksum is known for this part."; if (salvage != null) { e.StateWord = "cannot be checked"; e.Unreadable = "This part of the backup cannot be checked against a trusted checksum."; } }   // not damaged: unchecked
                    MemberCategory(e);
                    if (e.IsImage) e.ImageType = ImageTypeOf(z, ze);
                    if (name == DataMember) e.ImageNote = salvage == null || salvage.DataIntact ? "The data storage (games, saves, settings): browsed file by file below."
                        : salvage.Files != null ? "The data storage (games, saves, settings), damaged as a whole: its files are listed below from the backup's file list, each checked on its own."
                        : "The data storage (games, saves, settings), damaged: none of its files can be checked one by one." + (salvage.FilesProblem != null ? " " + salvage.FilesProblem : "");
                    Add(all, e);
                }
                // a part the backup's records name that the part finder did not find in a file whose table of contents is damaged
                foreach (var name in (salvage?.Damaged ?? new HashSet<string>()).Where(n => !zip.ContainsKey(n) && BackupFormat.Allowed(n)).OrderBy(n => n, StringComparer.Ordinal))
                {
                    int slash = name.IndexOf('/');
                    if (slash > 0 && !byId.ContainsKey("z:" + name.Substring(0, slash)))
                        Add(all, new BrowseEntry { Id = "z:" + name.Substring(0, slash), ParentId = "", Name = name.Substring(0, slash), Source = EntrySource.VirtualFolder, Kind = EntryKind.Folder, TypeCode = 'd', Category = Category.SystemFiles, CategoryReason = "A folder inside the backup file." });
                    var e = new BrowseEntry { Id = "m:" + name, ParentId = slash > 0 ? "z:" + name.Substring(0, slash) : "", Name = slash > 0 ? name.Substring(slash + 1) : name, Source = EntrySource.Member, Kind = EntryKind.File, TypeCode = 'f', Member = name, Mode = 0x1A4, Owner = "backup",
                        Size = salvage.Trusted.TryGetValue(name, out var tm) ? Math.Max(0, tm.size) : 0, StateWord = "missing", HashOrigin = HashOrigin.Unreadable };
                    e.Unreadable = e.HashText = "This part is missing from the file: the backup's records name it, but it was not found where the file's damaged table of contents leaves it.";
                    MemberCategory(e); e.ImageNote = null;
                    Add(all, e);
                }
                // the data storage, file by file
                var dataEntries = new List<BrowseEntry>();
                ZipEntryInfo dz = DataMember != null && zip.TryGetValue(DataMember, out var dzz) ? dzz : null;
                bool dataOk = dz != null && dz.Method == 0 && (salvage == null || salvage.Intact.Contains(DataMember)) && (salvage == null || !salvage.Damaged.Contains(DataMember));
                if (dz == null) DataProblem = "This backup has no data storage image.";
                else if (dz.Method != 0) DataProblem = "The data storage image is not stored in a way that can be read in place.";
                else if (!dataOk) DataProblem = salvage?.Files != null ? "The data storage image in this backup is damaged as a whole, so it is not read as one; each of its files is checked on its own against the backup's file list."
                    : salvage?.FilesProblem ?? "The data storage image in this backup is damaged or cannot be checked, so its files cannot be read safely.";
                Dictionary<string, (string sha, long size, long inode, string type)> inv = null; List<Dictionary<string, object>> invEntries = null;
                bool invTrusted = zip.ContainsKey(BackupFormat.InventoryName) && (salvage == null || salvage.Intact.Contains(BackupFormat.InventoryName));
                if (invTrusted) { try { using (var s = z.OpenMember(zip[BackupFormat.InventoryName])) invEntries = InventoryEntries(s); inv = HashesOf(invEntries); } catch (Exception ex) when (ex is IOException || ex is FormatException || ex is ArgumentException || ex is InvalidOperationException) { Notes.Add("The backup's file list could not be read (" + ex.Message + "); checksums are worked out from the data storage instead."); } }
                if (dataOk)
                {
                    try
                    {
                        var fs = new Ext4Reader(new SubStream(f, dz.DataOffset, dz.Size));
                        FileSystemCondition = fs.Condition; DataHeader(fs);
                        var list = fs.List();
                        DateTime? made = ParseUtc(CompletedUtc);
                        foreach (var x in list)
                        {
                            if (cancelled != null && cancelled()) throw new OperationCanceledException();
                            dataEntries.Add(DataEntry(fs, x, inv, made));
                        }
                        if (inv != null && inv.Keys.Except(list.Select(x => x.Path)).Any()) Notes.Add("The backup's file list names files the data storage does not hold; those names are not used.");
                    }
                    catch (Ext4Exception ex) { DataProblem = "The data storage could not be read as a file system on this PC (" + ex.Message + "); its exact image is still in the backup."; dataEntries.Clear(); }
                }
                else if (salvage != null && invEntries != null)
                {
                    // the data image as a whole fails its checksum: its entries come only from the verified file list (with the metadata it
                    // recorded), never from the damaged image's own directories, and a file is readable only when its own bytes still match the
                    // checksum the list recorded for it (salvage.Files, the per-file proof); everything else is listed with the reason
                    var proof = new Dictionary<string, Repair.SalvageItem>(StringComparer.Ordinal);
                    foreach (var it in salvage.Files ?? new List<Repair.SalvageItem>()) if (!proof.ContainsKey(it.Path)) proof[it.Path] = it;
                    DateTime? made = ParseUtc(CompletedUtc);
                    foreach (var x in invEntries) dataEntries.Add(ListedEntry(x, salvage.Files == null ? null : proof, salvage.FilesProblem, made));
                    int ok = dataEntries.Count(e => e.Kind == EntryKind.File && e.Unreadable == null), files = dataEntries.Count(e => e.Kind == EntryKind.File);
                    Notes.Add(salvage.Files == null ? (salvage.FilesProblem ?? "The data storage image is damaged, so none of its files can be checked.") + " Its files are listed from the backup's file list."
                        : "The data storage image is damaged as a whole. Each of its files was checked on its own: " + ok + " of " + files + " still match the checksums recorded when the backup was made and can be copied; the others are marked and cannot be.");
                }
                foreach (var e in dataEntries) Add(all, e);
                bool proven = !dataOk && salvage?.Files != null && dz != null && dz.Method == 0 && dataEntries.Any(e => e.Kind == EntryKind.File && e.Unreadable == null);
                Ext4Reader dataFs = null;
                if (dataOk && dataEntries.Count > 0 || proven)
                {
                    // reads for the Classifier: only files that are readable (a damaged image: only files whose bytes matched their checksum)
                    var fs = dataFs = new Ext4Reader(new SubStream(f, dz.DataOffset, dz.Size));
                    Games = Classifier.Run(dataEntries, (e, max) => { if (e.Unreadable != null || e.Kind != EntryKind.File || e.Size > max) return null; try { return fs.ReadAll(e.Inode, Math.Min(max, e.Size)); } catch (Ext4Exception) { return null; } },
                                                         (e, n) => { if (e.Unreadable != null || e.Kind != EntryKind.File) return null; return Head(fs, e.Inode, n); });
                }
                else if (salvage != null && invEntries != null && dataEntries.Count > 0)
                    Games = Classifier.Run(dataEntries, (e, max) => null, (e, n) => null);   // by the recorded paths only: nothing in the damaged image is read
                else
                {
                    foreach (var e in dataEntries) { e.Category = Category.EverythingElse; e.CategoryReason = "Its kind cannot be checked: the data storage is not readable."; }
                    Games = new List<GameGroup>();
                }
                // system images, the system part and the CCS HOME package, with what is inside them (BackupNested.cs)
                var gl = Games.ToList(); Nest(all, z, f, m, dataFs, gl, cancelled); Games = gl;
            }
            foreach (var g in Games) gamesByKey[g.Key] = g;
            Entries = all;
            foreach (var e in all)
            {
                if (!children.TryGetValue(e.ParentId ?? "", out var l)) children[e.ParentId ?? ""] = l = new List<BrowseEntry>(); l.Add(e);
                if (e.Source == EntrySource.DataFile && e.Kind == EntryKind.File) { if (!byInode.TryGetValue(e.Inode, out var li)) byInode[e.Inode] = li = new List<BrowseEntry>(); li.Add(e); }
            }
            if (DataMember != null && zip.ContainsKey(DataMember) && byId.TryGetValue("m:" + DataMember, out var de)) DataMemberSha256 = de.Sha256;
        }

        void Add(List<BrowseEntry> all, BrowseEntry e) { if (byId.ContainsKey(e.Id)) return; byId[e.Id] = e; all.Add(e); }

        /// <summary>One entry of the verified file list, for a data storage image that fails its whole checksum: the name, kind and metadata
        /// as the list recorded them (whole seconds; nothing is read from the damaged image's inodes or directories). A regular file is
        /// readable only when proof says its bytes still match the list's checksum; proof null: no file could be checked (problem says why).</summary>
        BrowseEntry ListedEntry(Dictionary<string, object> x, Dictionary<string, Repair.SalvageItem> proof, string problem, DateTime? made)
        {
            var path = x.S("path"); int cut = path.LastIndexOf('/'); var parent = path.Substring(0, cut); var t = x.S("type") ?? "?";
            int mode = 0; try { mode = Convert.ToInt32(x.S("mode") ?? "0", 8); } catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is OverflowException) { }
            var e = new BrowseEntry
            {
                Id = "d:" + path, ParentId = parent.Length == 0 ? "m:" + DataMember : "d:" + parent, Name = path.Substring(cut + 1), Source = EntrySource.DataFile, Member = DataMember,
                ImagePath = path, ConsolePath = "/var/lib" + path, Kind = t == "f" ? EntryKind.File : t == "d" ? EntryKind.Folder : t == "l" ? EntryKind.Link : EntryKind.Special, TypeCode = t.Length == 1 ? t[0] : '?',
                Mode = mode, Uid = (int)x.L("uid"), Gid = (int)x.L("gid"), Links = (int)x.L("links"), Inode = (uint)Math.Max(0, Math.Min(uint.MaxValue, x.L("inode"))), LinkTarget = x.S("target"), Owner = Inventory.OwnerOf(path),
            };
            e.Size = e.Kind == EntryKind.File || e.Kind == EntryKind.Link ? Math.Max(0, x.L("size")) : 0;
            if (x.ContainsKey("mtime")) { e.MtimeSeconds = (int)(uint)x.L("mtime"); DateCheck(e, made); }
            if (e.Kind != EntryKind.File) return e;
            Repair.SalvageItem it = null;
            if (proof != null) proof.TryGetValue(path, out it);
            if (it != null && it.Status == "verified" && it.Sha256 != null) { e.Sha256 = it.Sha256; e.HashOrigin = HashOrigin.Inventory; e.HashText = "Checked on its own: its bytes in the damaged data storage image still match the checksum recorded when the backup was made."; return e; }
            e.HashOrigin = HashOrigin.Unreadable;
            if (it == null) { e.StateWord = "cannot be checked"; e.Unreadable = (problem ?? "The data storage image it lives in is damaged.") + " It is listed by the backup's file list but cannot be checked or copied."; }
            else if (it.Status == "damaged") { e.StateWord = "damaged"; e.Unreadable = "Damaged: " + it.Trust + ". It cannot be copied."; }
            else if (it.Status == "no-recorded-checksum") { e.StateWord = "cannot be checked"; e.Unreadable = "The backup's file list keeps no checksum for it and the data storage image around it is damaged, so its content cannot be confirmed or copied."; }
            else { e.StateWord = "cannot be checked"; e.Unreadable = "It cannot be checked: " + it.Trust + "."; }
            e.HashText = e.Unreadable;
            return e;
        }

        BrowseEntry DataEntry(Ext4Reader fs, Ext4Entry x, Dictionary<string, (string sha, long size, long inode, string type)> inv, DateTime? made)
        {
            int cut = x.Path.LastIndexOf('/'); var parent = x.Path.Substring(0, cut);
            var e = new BrowseEntry
            {
                Id = "d:" + x.Path, ParentId = parent.Length == 0 ? "m:" + DataMember : "d:" + parent, Name = x.Path.Substring(cut + 1), Source = EntrySource.DataFile, Member = DataMember,
                ImagePath = x.Path, ConsolePath = "/var/lib" + x.Path, TypeCode = x.Type, Mode = x.Mode, Uid = x.Uid, Gid = x.Gid, Links = x.Links, Inode = x.Inode, LinkTarget = x.LinkTarget, Allocated = x.Allocated,
                Kind = x.Type == 'f' ? EntryKind.File : x.Type == 'd' ? EntryKind.Folder : x.Type == 'l' ? EntryKind.Link : EntryKind.Special, Owner = Inventory.OwnerOf(x.Path),
            };
            e.Size = e.Kind == EntryKind.File || e.Kind == EntryKind.Link ? x.Size : 0;
            if (x.Unreadable) e.Unreadable = "It cannot be read on this PC (" + (x.Note ?? "unreadable") + ").";
            ExactTime(fs, e, x, made);
            if (e.Kind == EntryKind.File)
            {
                if (e.Unreadable != null) { e.HashOrigin = HashOrigin.Unreadable; e.HashText = e.Unreadable; }
                else if (inv != null && inv.TryGetValue(x.Path, out var iv) && iv.sha != null && iv.size == x.Size && iv.inode == x.Inode) { e.Sha256 = iv.sha; e.HashOrigin = HashOrigin.Inventory; e.HashText = "Checksum recorded when the backup was made."; }
                else if (liveFs != null) { e.HashOrigin = HashOrigin.None; e.HashText = "Worked out from the console when the file is previewed or exported."; }
                else { e.HashOrigin = HashOrigin.None; e.HashText = x.Size > EarlierHashLimit && inv != null ? "The backup did not record a checksum for this large file; it is worked out from the checked data storage before it is exported." : "Not worked out yet; it is worked out from the checked data storage before it is exported."; }
            }
            return e;
        }

        /// <summary>Console Browser: the console's data storage, read live through a read-only session (data is the partition as a seekable
        /// stream; every read goes to the console). The same three views, search, preview and checked export as a backup. Checksums are worked
        /// out from the console when a file is previewed or exported; the exported copy is read back on the PC and compared.</summary>
        /// <summary>physicalModel: the model the check-up identified (ConsoleModels.ById; null: an NES Classic's handling)</summary>
        public static OpenResult OpenLive(Stream data, string consoleTag, string stateText, Func<bool> cancelled = null, string physicalModel = null)
        {
            var r = new OpenResult();
            try
            {
                var c = new BackupContents("Console " + (consoleTag ?? "unknown"), null, null) { ConsoleTag = consoleTag, StateText = stateText, BackupId = "live", DataMember = "live-data" };
                c.liveFs = new Ext4Reader(data); c.FileSystemCondition = c.liveFs.Condition; c.DataHeader(c.liveFs);
                var all = new List<BrowseEntry>();
                c.Add(all, new BrowseEntry { Id = "m:" + c.DataMember, ParentId = "", Name = "Data storage (/var/lib)", Source = EntrySource.VirtualFolder, Kind = EntryKind.Folder, TypeCode = 'd', Category = Category.SystemFiles, CategoryReason = "The console's data storage: games, saves, settings and anything else on it." });
                var data0 = new List<BrowseEntry>();
                foreach (var x in c.liveFs.List()) { if (cancelled != null && cancelled()) throw new OperationCanceledException(); data0.Add(c.DataEntry(c.liveFs, x, null, null)); }
                foreach (var e in data0) c.Add(all, e);
                var fs = c.liveFs;
                c.Games = Classifier.Run(data0, (e, max) => { if (e.Unreadable != null || e.Size > max) return null; try { lock (c.gate) return fs.ReadAll(e.Inode, max); } catch (Ext4Exception) { return null; } },
                                               (e, n) => { if (e.Unreadable != null) return null; lock (c.gate) return Head(fs, e.Inode, n); });
                foreach (var g in c.Games) c.gamesByKey[g.Key] = g;
                c.Entries = all;
                foreach (var e in all)
                {
                    if (!c.children.TryGetValue(e.ParentId ?? "", out var l)) c.children[e.ParentId ?? ""] = l = new List<BrowseEntry>(); l.Add(e);
                    if (e.Source == EntrySource.DataFile && e.Kind == EntryKind.File) { if (!c.byInode.TryGetValue(e.Inode, out var li)) c.byInode[e.Inode] = li = new List<BrowseEntry>(); li.Add(e); }
                }
                c.Notes.Add("Read live from the console in a read-only session. Nothing on the console is changed.");
                c.Model = Device.ConsoleModels.ById(physicalModel);
                r.Contents = c; return r;
            }
            catch (OperationCanceledException) { r.Canceled = true; r.Problem = "Reading the console was canceled. Nothing was changed."; return r; }
            catch (Exception ex) when (ex is IOException || ex is Ext4Exception) { r.Problem = "The console's data storage could not be read: " + ex.Message; return r; }
        }

        static void MemberCategory(BrowseEntry e)
        {
            var n = e.Member;
            if (n.StartsWith("restore/", StringComparison.Ordinal) || n == BackupFormat.OriginalKernelName || n.StartsWith("reference/", StringComparison.Ordinal))
            {
                e.Category = Category.SystemFiles; e.IsImage = !n.EndsWith(".txt", StringComparison.Ordinal);
                e.CategoryReason = n.StartsWith("restore/boot2", StringComparison.Ordinal) ? "The console's startup software, exactly as read (what a restore puts back)."
                    : n == BackupFormat.OriginalKernelName ? "The console's original Nintendo startup software, kept on its own."
                    : n.StartsWith("restore/", StringComparison.Ordinal) ? "One part of the console's built-in storage, exactly as read (what a restore puts back)."
                    : n == "reference/partition-table.txt" ? "The console's storage table as printed (kept for reference)." : "Kept for reference only; never written to a console.";
                e.ImageNote = n.StartsWith("reference/", StringComparison.Ordinal) ? "Kept as one exact copy for reference." : "Kept as one exact image; its files are not listed here (this version lists the files of the data storage only).";
            }
            else if (n == BackupFormat.ExportName) { e.Category = Category.Games; e.CategoryReason = "The console's Classic Cart Studio HOME as a .ccs collection package (it opens with Restore a Backup)."; }
            else { e.Category = Category.EverythingElse; e.CategoryReason = "Part of the backup file itself: its description."; }
        }

        static string ImageTypeOf(ZipReader z, ZipEntryInfo ze)
        {
            if (ze.Method != 0 || ze.Size < 2048) return "Not recognized";
            var b = new byte[(int)Math.Min(4096, ze.Size)];
            using (var s = z.StoredWindow(ze)) { int got = 0; while (got < b.Length) { int n = s.Read(b, got, b.Length - got); if (n <= 0) break; got += n; } }
            if (Encoding.ASCII.GetString(b, 0, 8) == "ANDROID!") return "Startup software (an Android boot image)";
            if (Encoding.ASCII.GetString(b, 4, 7) == "eGON.BT") return "Bootloader (first stage)";
            if (Encoding.ASCII.GetString(b, 4, 6) == "uboot\0") return "Bootloader (second stage)";
            if (b.Length >= 1082 && b[1080] == 0x53 && b[1081] == 0xEF) return "ext2/3/4 file system";
            if (b[0] == 'h' && b[1] == 's' && b[2] == 'q' && b[3] == 's') return "SquashFS file system";
            if (b[510] == 0x55 && b[511] == 0xAA && (Encoding.ASCII.GetString(b, 54, 3) == "FAT" || Encoding.ASCII.GetString(b, 82, 3) == "FAT")) return "FAT file system";
            if (b.All(x => x == 0)) return "Empty at the start (zeros)";
            return "Not recognized (not a file system this version reads; it may be encrypted)";
        }

        // the exact modification time: the 32-bit signed seconds, plus the extra field's epoch bits and nanoseconds when the inode has them
        static void ExactTime(Ext4Reader fs, BrowseEntry e, Ext4Entry x, DateTime? made)
        {
            long sec = (int)(uint)x.Mtime; int ns = 0;
            try
            {
                var ino = fs.Inode(x.Inode);
                if (ino.Length >= 0x8C && 128 + BitConverter.ToUInt16(ino, 0x80) >= 0x8C)
                {
                    uint extra = BitConverter.ToUInt32(ino, 0x88); sec += (long)(extra & 3) << 32; ns = (int)(extra >> 2);
                    if (ns >= 1000000000) { ns = 0; e.DateNote = "The recorded time has an invalid fraction of a second; whole seconds are shown."; }
                }
            }
            catch (Ext4Exception) { }
            e.MtimeSeconds = sec; e.MtimeNanoseconds = ns;
            DateCheck(e, made);
        }

        /// <summary>the honest note for a recorded date the console's clock got wrong (or none at all)</summary>
        static void DateCheck(BrowseEntry e, DateTime? made)
        {
            long sec = e.MtimeSeconds ?? 0; int ns = e.MtimeNanoseconds;
            var t = e.MtimeUtc;
            if (t == null) e.DateNote = "The recorded date cannot be shown as a calendar date.";
            else if (sec == 0 && ns == 0) e.DateNote = "No date was recorded (the console's clock read zero).";
            else if (t.Value < EarliestPlausible) e.DateNote = "Earlier than the NES Classic existed: the console's clock was not set correctly.";
            else if (made != null && t.Value > made.Value.AddDays(1)) e.DateNote = "Later than the backup was made: the console's clock was wrong.";
        }

        static DateTime? ParseUtc(string s) => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : (DateTime?)null;

        sealed class HeadSink : Stream
        {
            public readonly byte[] Buf; public int Got; public HeadSink(int n) { Buf = new byte[n]; }
            public override void Write(byte[] b, int o, int c) { int k = Math.Min(c, Buf.Length - Got); Buffer.BlockCopy(b, o, Buf, Got, k); Got += k; if (Got >= Buf.Length) throw new Full(); }
            public sealed class Full : Exception { }
            public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
            public override long Length => Got; public override long Position { get => Got; set => throw new NotSupportedException(); }
            public override void Flush() { } public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
            public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException();
        }

        static byte[] Head(Ext4Reader fs, uint inode, int n)
        {
            var h = new HeadSink(n);
            try { fs.ReadFile(inode, h); } catch (HeadSink.Full) { } catch (Ext4Exception) { return null; }
            var b = new byte[h.Got]; Buffer.BlockCopy(h.Buf, 0, b, 0, h.Got); return b;
        }

        /// <summary>the backup's file list: path -> (SHA-256 or null, size, inode), read with bounded depth from a member of at most 64 MB</summary>
        internal static Dictionary<string, (string sha, long size, long inode, string type)> InventoryHashes(Stream s) => HashesOf(InventoryEntries(s));

        static Dictionary<string, (string sha, long size, long inode, string type)> HashesOf(List<Dictionary<string, object>> entries)
        {
            var map = new Dictionary<string, (string, long, long, string)>(StringComparer.Ordinal);
            foreach (var e in entries)
            {
                var sha = e.S("type") == "f" ? e.S("sha256") : null; if (sha != null && !Hex.IsHex(sha, 64)) sha = null;
                map[e.S("path")] = (sha, e.L("size"), e.L("inode"), e.S("type"));
            }
            return map;
        }

        /// <summary>the backup's file list as its entries, in its order (each with an absolute path, no "." or ".." step and no path twice),
        /// read with bounded depth from a member of at most 64 MB</summary>
        internal static List<Dictionary<string, object>> InventoryEntries(Stream s) => InventoryEntries(InventoryDoc(s));

        /// <summary>the storage part the backup's file list was read from ("nandc"), or null</summary>
        internal static string InventoryPartition(Stream s) => InventoryDoc(s).S("partition");

        static Dictionary<string, object> InventoryDoc(Stream s)
        {
            string text; using (var r = new StreamReader(s, Encoding.UTF8)) text = r.ReadToEnd();
            var d = text.Length <= Json.MaxText ? Json.Parse(text) : new JavaScriptSerializer { MaxJsonLength = (int)BackupFormat.MaxSize(BackupFormat.InventoryName), RecursionLimit = 32 }.DeserializeObject(text) as Dictionary<string, object>;
            if (d == null || d.S("schema") != "ccs-console-inventory/1") throw new FormatException("not the backup's file list");
            return d;
        }

        static List<Dictionary<string, object>> InventoryEntries(Dictionary<string, object> d)
        {
            var list = new List<Dictionary<string, object>>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in (d.A("entries") ?? new List<object>()).OfType<Dictionary<string, object>>())
            {
                var p = e.S("path"); if (p == null || !p.StartsWith("/", StringComparison.Ordinal) || p.Length < 2 || p.EndsWith("/", StringComparison.Ordinal)) continue;
                if (p.Split('/').Skip(1).Any(x => x.Length == 0 || x == "." || x == "..") || !seen.Add(p)) continue;
                list.Add(e);
            }
            return list;
        }

        // ------------------------------------------------------------------ views and search
        public BrowseEntry Find(string id) => id != null && byId.TryGetValue(id, out var e) ? e : null;
        public GameGroup Game(string key) => key != null && gamesByKey.TryGetValue(key, out var g) ? g : null;
        /// <summary>All files: the entries directly inside a folder entry ("" for the top: the backup file's own members and folders)</summary>
        public IReadOnlyList<BrowseEntry> Children(string parentId) => children.TryGetValue(parentId ?? "", out var l) ? l : new List<BrowseEntry>();
        public IEnumerable<BrowseEntry> Descendants(BrowseEntry folder) { foreach (var k in Children(folder.Id)) { yield return k; foreach (var d in Descendants(k)) yield return d; } }
        /// <summary>By type: every entry of a category that is not a folder (files, links and special files; nothing is left out)</summary>
        public List<BrowseEntry> ByType(Category c) => Entries.Where(e => e.Kind != EntryKind.Folder && e.Category == c).ToList();
        public Dictionary<Category, (int count, long bytes)> Counts()
        {
            var d = Categories.All.ToDictionary(c => c, c => (0, 0L));
            foreach (var e in Entries.Where(x => x.Kind != EntryKind.Folder)) { var v = d[e.Category]; d[e.Category] = (v.Item1 + 1, v.Item2 + (e.Kind == EntryKind.File ? e.Size : 0)); }
            return d;
        }
        public string GameTitle(BrowseEntry e) => Game(e?.GameKey)?.Title;

        /// <summary>case-insensitive search by name, path, game, category and type over the given entries (all of them by default)</summary>
        public List<BrowseEntry> Search(string text, IEnumerable<BrowseEntry> scope = null)
        {
            var q = (text ?? "").Trim(); var src = scope ?? Entries;
            if (q.Length == 0) return src.ToList();
            bool Has(string s) => s != null && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
            return src.Where(e => Has(e.Name) || Has(e.DisplayPath) || Has(e.Member) || Has(Categories.Label(e.Category)) || Has(e.ImageType) || Has(e.LinkTarget)
                                || e.GameKey != null && gamesByKey.TryGetValue(e.GameKey, out var g) && (Has(g.Title) || Has(g.Code) || g.OtherTitles.Any(Has))).ToList();
        }

        /// <summary>By game, searched: a game matches by its name (any recorded name), code or identity, or by any of its files' names</summary>
        public List<GameGroup> SearchGames(string text)
        {
            var q = (text ?? "").Trim(); if (q.Length == 0) return Games.ToList();
            bool Has(string s) => s != null && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
            return Games.Where(g => Has(g.Title) || Has(g.Code) || Has(g.ContentSha256) || g.OtherTitles.Any(Has) || g.Entries.Any(e => Has(e.Name) || Has(e.DisplayPath))).ToList();
        }

        // ------------------------------------------------------------------ reading
        /// <summary>streams an entry's exact bytes into sink (bounded buffers; a member through its CRC-checked reader, a data file through
        /// the ext4 reader on the data storage image inside the held file)</summary>
        public void CopyTo(BrowseEntry e, Stream sink, Func<bool> cancelled = null)
        {
            if (e == null || e.Kind != EntryKind.File) throw new InvalidOperationException("only files have bytes to read");
            if (e.Unreadable != null) throw new IOException(e.Unreadable);
            if (e.Source == EntrySource.StorageFile) { CopyStorage(e, sink, cancelled); return; }   // a USB and SD backup (BackupContents.Storage.cs)
            if (liveFs != null)
            {
                if (e.Source != EntrySource.DataFile) throw new IOException("only the console's files can be read");
                lock (gate) { try { liveFs.ReadFile(e.Inode, sink); } catch (Ext4Exception ex) { throw new IOException("the file could not be read from the console (" + ex.Message + ")"); } }
                return;
            }
            if (e.Source == EntrySource.ImageFile || e.Source == EntrySource.PackageFile || e.Source == EntrySource.StartupPart || e.Source == EntrySource.RamdiskFile) { CopyNested(e, sink, cancelled); return; }
            using (var f = Read())
            {
                if (e.Source == EntrySource.Member)
                {
                    var z = Zip(f);
                    using (var s = z.OpenMember(z.Find(e.Member))) Streams.Copy(s, sink, -1, cancelled);
                }
                else
                {
                    var dz = zip[DataMember];
                    try { var fs = new Ext4Reader(new SubStream(f, dz.DataOffset, dz.Size)); fs.ReadFile(e.Inode, sink, e.Size); }   // never more than its recorded size
                    catch (Ext4Exception ex) { throw new IOException("the file could not be read from the data storage (" + ex.Message + ")"); }
                }
            }
        }

        /// <summary>The checksum an export or a preview is checked against: the trusted one when known, otherwise worked out now by reading
        /// the file through the verified data storage image (and remembered for this backup, hard links included). null: not readable.</summary>
        public string ExpectedHash(BrowseEntry e, Func<bool> cancelled = null, Action<long> progress = null)
        {
            if (e == null || e.Kind != EntryKind.File || e.Unreadable != null) return null;
            if (e.Source == EntrySource.StorageFile) return StorageHash(e, cancelled, progress);   // a USB and SD backup (BackupContents.Storage.cs)
            if (e.HashTrusted) return e.Sha256;
            if (e.Source == EntrySource.ImageFile || e.Source == EntrySource.PackageFile || e.Source == EntrySource.StartupPart || e.Source == EntrySource.RamdiskFile) return NestedHash(e, cancelled, progress);
            if (e.Source != EntrySource.DataFile) return null;   // a member without a trusted checksum is never given one here
            var h = new HashingSink(null, progress, cancelled);
            CopyTo(e, h, cancelled); h.Finish();
            if (h.Count != e.Size) throw new IOException("the file read back with a different size than the file system records");
            lock (gate)
                foreach (var x in (byInode.TryGetValue(e.Inode, out var same) ? same : new List<BrowseEntry> { e }).Where(x => !x.HashTrusted && x.Unreadable == null))
                {
                    x.Sha256 = h.Sha256; x.HashOrigin = HashOrigin.Derived; x.BoundTo = Live ? "the console, read live" : DataMember; x.BoundToSha256 = Live ? null : DataMemberSha256;
                    x.HashText = Live ? "Worked out from the console during this session." : Salvage ? "Worked out from this backup's data storage, which still matches its trusted checksum." : "Worked out from the backup's verified data storage (the backup did not record one for this file).";
                }
            return h.Sha256;
        }

        /// <summary>files that still have no trusted checksum (readable files only)</summary>
        public List<BrowseEntry> MissingHashes() => Entries.Where(e => e.Kind == EntryKind.File && e.Unreadable == null && !e.HashTrusted).ToList();
        public bool HashesComplete => MissingHashes().Count == 0;

        /// <summary>Works out every missing checksum (each inode once; the data storage's files first, then what is inside system images and
        /// the package, each through its holder), with progress (bytes done, total) and cancel. Only a complete run is kept as the derived
        /// index beside the backup's records (the data storage's files; checksums inside a system image or the package are worked out again
        /// when needed); a canceled one is never shown as complete.</summary>
        public int DeriveMissingHashes(Func<bool> cancelled = null, Action<long, long> progress = null)
        {
            var miss = MissingHashes();
            var todo = miss.Where(e => e.Source == EntrySource.DataFile).GroupBy(e => e.Inode).Select(g => g.First())
                .Concat(miss.Where(e => e.Source == EntrySource.ImageFile).GroupBy(e => e.Container.Id + "|" + e.Inode).Select(g => g.First()))
                .Concat(miss.Where(e => e.Source == EntrySource.PackageFile)).Concat(miss.Where(e => e.Source == EntrySource.StartupPart))
                .Concat(miss.Where(e => e.Source == EntrySource.RamdiskFile).GroupBy(e => e.Links > 1 ? e.Container.Id + "|" + e.Inode : e.Id).Select(g => g.First())).ToList();
            long total = todo.Sum(e => e.Size), done = 0; int n = 0;
            foreach (var e in todo)
            {
                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                long before = done;
                if (!e.HashTrusted)
                    try { ExpectedHash(e, cancelled, k => progress?.Invoke(before + k, total)); }
                    catch (IOException) when (e.Source != EntrySource.DataFile) { }   // a file inside an image that cannot be read stays without one (never shown as complete)
                done += e.Size; n++;
            }
            if (!MissingHashes().Any(e => e.Source == EntrySource.DataFile)) SaveDerivedIndex();   // the data storage's files complete (the index holds only theirs)
            return n;
        }

        /// <summary>a file's bytes for a preview, only when they match its checksum and fit within max (decoding is the caller's, with its own
        /// pixel and time limits; nothing is ever run). why: the reason when null is returned.</summary>
        public byte[] ReadVerified(BrowseEntry e, long max, out string why, Func<bool> cancelled = null)
        {
            why = null;
            if (e == null || e.Kind != EntryKind.File) { why = "Only files can be previewed."; return null; }
            if (e.Unreadable != null) { why = e.Unreadable; return null; }
            if (e.Private != null) { why = e.Private; return null; }   // key material is never shown
            if (e.Size > max) { why = "It is too large to preview (" + Engine.Units.Gb(e.Size) + "); it can still be exported."; return null; }
            try
            {
                var want = ExpectedHash(e, cancelled);
                if (want == null) { why = "No trusted checksum is known for it, so it is not shown."; return null; }
                var m = new MemoryStream(); var h = new HashingSink(m, null, cancelled); CopyTo(e, h, cancelled); h.Finish();
                if (h.Sha256 != want) { why = "Its bytes do not match its checksum, so it is not shown."; return null; }
                return m.ToArray();
            }
            catch (IOException ex) { why = "It could not be read: " + ex.Message; return null; }
        }

        /// <summary>what kind of picture the bytes are, by their signature (png, jpeg, gif, bmp), or null</summary>
        public static string ImageKindOf(byte[] b)
        {
            if (b == null || b.Length < 8) return null;
            if (b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G' && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return "png";
            if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpeg";
            if (b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8') return "gif";
            if (b[0] == 'B' && b[1] == 'M') return "bmp";
            return null;
        }

        // ------------------------------------------------------------------ the derived index (beside the records; never in the backup)
        string DerivedPath() => settings == null || ArchiveSha256 == null ? null : System.IO.Path.Combine(settings.RecordsFolder, "derived-index-" + ArchiveSha256.Substring(0, 16) + ".json");

        void SaveDerivedIndex()
        {
            if (Live) return;
            var p = DerivedPath(); if (p == null || Salvage) return;
            var rows = Entries.Where(e => e.Source == EntrySource.DataFile && e.Kind == EntryKind.File && (e.HashOrigin == HashOrigin.Derived || e.HashOrigin == HashOrigin.DerivedIndex))
                              .GroupBy(e => e.Inode).Select(g => (object)new JObj { { "inode", (long)g.Key }, { "size", g.First().Size }, { "sha256", g.First().Sha256 }, { "paths", g.Select(x => (object)x.ImagePath).ToList() } }).ToList();
            if (rows.Count == 0) return;
            var doc = new JObj
            {
                { "schema", DerivedSchema }, { "note", "Checksums of files the backup's own file list did not record, worked out on this PC from the verified backup. The backup file itself is unchanged; these are not checksums from the time the backup was made." },
                { "backup_file", Path }, { "backup_id", BackupId }, { "archive_sha256", ArchiveSha256 }, { "data_member", DataMember }, { "data_member_sha256", DataMemberSha256 },
                { "made_utc", Record.Now() }, { "complete", true }, { "files", rows },
            };
            try { Directory.CreateDirectory(settings.RecordsFolder); Durable.WriteAtomic(p, Json.Write(doc)); DerivedIndexPath = p; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Notes.Add("The worked-out checksums could not be kept for next time (" + ex.Message + ")."); }
        }

        void LoadDerivedIndex()
        {
            var p = DerivedPath(); if (p == null || !File.Exists(p)) return;
            var d = Record.Load(p);
            if (d == null || d.S("schema") != DerivedSchema || d.S("archive_sha256") != ArchiveSha256 || d.S("data_member") != DataMember || d.S("data_member_sha256") != DataMemberSha256 || !d.B("complete")) return;
            var byInode = new Dictionary<long, (string sha, long size)>();
            foreach (var r in (d.A("files") ?? new List<object>()).OfType<Dictionary<string, object>>()) { var sha = r.S("sha256"); if (Hex.IsHex(sha, 64)) byInode[r.L("inode")] = (sha, r.L("size")); }
            foreach (var e in MissingHashes().Where(e => e.Source == EntrySource.DataFile))
                if (byInode.TryGetValue(e.Inode, out var v) && v.size == e.Size) { e.Sha256 = v.sha; e.HashOrigin = HashOrigin.DerivedIndex; e.HashText = "Worked out earlier from this same verified backup (kept beside the backup's records); every export is still checked against it."; }
            DerivedIndexPath = p;
        }

        public void Dispose() { lock (gate) { hold?.Dispose(); hold = null; ClearKeys(); } }
    }
}
