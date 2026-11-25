// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The Backup Browser's first screen: every backup file in the configured folders (Library.Settings: the first-copy folder and the
// second-copy folder), one row per file, with an honest status and the date it was last verified:
//   Complete    a finished backup file (.zip). Its check state says whether a full check is recorded for it as it is now (Verified),
//               whether it changed since its last full check, or that no full check is recorded here (Not checked: a file copied in from
//               elsewhere, or one whose check was recorded with other backup locations). Reading its small description (its manifest)
//               is never a check: the words people see (StatusText, CheckText) say Verified only for a full check.
//   Incomplete  an unfinished file (.zip.partial), or a backup whose record never reached a finished file.
//   Damaged     a file that failed its check (here, or when it was made), or is not a readable console backup at all.
//   Salvage     a file that failed its check but whose data storage still has files that can be trusted one by one: either its data
//               storage image still matches a trusted checksum (the write-once record made while it was captured, or its own manifest when
//               the two do not disagree), or the image is damaged but its file list still matches its trusted checksum and at least one
//               file's own bytes, read from the damaged image, still match the checksum that list recorded for it (Repair.Salvage's
//               per-file proof). Files can be checked and exported one by one (BackupContents.OpenSalvage). It is never a complete backup.
//               A file whose own table of contents (its ZIP index) is damaged is checked through Backup Repair's part finder (the repair map,
//               a lenient read of the index or a walk of the part headers, Repair/Examination.cs): the same rules apply to the parts it finds,
//               and the check log keeps how they were found, so the row can name the backup even though the file itself cannot be peeked.
//   Missing     a backup whose record says it was finished, but whose file is no longer where it was saved.
// Listing is read-only: file names, sizes and times, the operation records (Library.History), each finished file's small manifest and the
// check log. Last verified dates are never invented: they are the time a full check (Verifier) actually passed, either when the backup was
// made (its record) or later on this PC (the check log, written only by Check, the scheduled check and BackupContents.Open). A date belongs to the file as it was
// then: a file whose size or time changed since shows "Changed since it was last checked" until it is checked again.
// The check log, <first-copy folder>\.ccs-records\browser-checks.json, is history, never authority: every use of a backup checks it again.
using CcsBackup.Archive;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CcsBackup.Browse
{
    public enum BrowseStatus { Complete, Incomplete, Damaged, Salvage, Missing }
    public enum CheckState { Verified, NotChecked, ChangedSinceCheck, Failed }

    public sealed class BackupListEntry
    {
        public string BackupId, Copy, Path, ConsoleTag, StateText, Detail, LastVerifiedBy;
        public BrowseStatus Status; public CheckState Check = CheckState.NotChecked;
        public DateTime? MadeUtc, LastVerifiedUtc, LastCheckUtc; public long Bytes; public bool Simulated;
        public List<string> IntactParts = new List<string>(), DamagedParts = new List<string>();
        /// <summary>a damaged data storage image whose files were checked one by one at the last check: how many still match their recorded
        /// checksums, of how many files with a recorded checksum (-1: not checked that way)</summary>
        public int FilesVerified = -1, FilesChecked = -1;
        /// <summary>its table of contents (ZIP index) was damaged at the last check; FoundBy: how Backup Repair's part finder found its parts
        /// (null when no trustworthy list of its parts survives)</summary>
        public bool IndexDamaged; public string FoundBy;
        public bool CanOpen => Status == BrowseStatus.Complete && File.Exists(Path);
        public bool CanOpenSalvage => Status == BrowseStatus.Salvage && File.Exists(Path);
        /// <summary>the file's state in the words every list uses (the Dashboard, the Backup Browser, Backup Repair): the result of a full
        /// check, never what its description says about itself. Verified only for a full check recorded for the file as it is now.</summary>
        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case BrowseStatus.Complete: return Check == CheckState.Verified ? "Verified" : Check == CheckState.ChangedSinceCheck ? "Changed since last check" : "Not checked";
                    case BrowseStatus.Incomplete: return "Incomplete"; case BrowseStatus.Damaged: return "Damaged";
                    case BrowseStatus.Salvage: return "Damaged: some files can still be saved"; default: return "Missing";
                }
            }
        }
        static string When(DateTime utc) => utc.ToLocalTime().ToString("MMMM d, yyyy 'at' h:mm tt", CultureInfo.GetCultureInfo("en-US"));
        /// <summary>the last full check that passed: when, and whether it ran as the backup was made or later here; a file with none recorded in
        /// these backup locations' records says so (never "never checked": another settings file may hold its check)</summary>
        public string LastVerifiedText => LastVerifiedUtc == null ? "No full check recorded here" : "Last full check: " + When(LastVerifiedUtc.Value) + (LastVerifiedBy == "made" ? " (when it was made)" : "");
        /// <summary>one line under a backup's date in every list: what the last full check found, and when</summary>
        public string CheckText
        {
            get
            {
                switch (Status)
                {
                    case BrowseStatus.Missing: return "The file is no longer where it was saved.";
                    case BrowseStatus.Incomplete: return "The backup did not finish; nothing in this file is used.";
                    case BrowseStatus.Damaged: case BrowseStatus.Salvage: return "Its last full check found damage" + (LastCheckUtc != null ? " (" + When(LastCheckUtc.Value) + ")." : ".");
                }
                if (Check == CheckState.Verified) return LastVerifiedText + ".";
                if (Check == CheckState.ChangedSinceCheck) return LastVerifiedUtc != null ? LastVerifiedText + ". The file has changed since then; check it again before relying on it." : "The file has changed since its last check; check it again before relying on it.";
                return "No full check of this file is recorded here. Check the File runs one.";
            }
        }
        public override string ToString() => Copy + ": " + Path + " (" + StatusText + ")";
    }

    public static class BackupList
    {
        static readonly HashSet<string> Published = new HashSet<string> { "published", "copying", "complete", "complete-one-copy" };

        /// <summary>every backup file in the configured folders, newest first (read-only)</summary>
        public static List<BackupListEntry> List(Settings s)
        {
            var rows = new List<BackupListEntry>(); var log = CheckLog.Load(s);
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(BackupListEntry r) { if (r.Path == null || done.Add(r.Path)) rows.Add(r); }
            foreach (var h in History.List(s))
            {
                var rec = h.RecordData; var state = rec?.S("state"); var prim = rec?.O("primary"); var arc = rec?.O("archive");
                if (rec == null && h.Id.StartsWith("partial:", StringComparison.Ordinal)) { Add(Unfinished(h, h.PartialPath, "Unfinished file", "This file was never finished, so it is not a backup. Nothing in it is used.")); continue; }
                // a file that cannot be read as a backup (its table of contents is damaged, or it is not one): its last check, when it was made
                // through Backup Repair's part finder for exactly this file, still says what it is and what can be saved (FileRow)
                if (rec == null && h.Status == BackupStatus.Damaged) { Add(h.PrimaryPath != null && File.Exists(h.PrimaryPath) ? FileRow(h, h.PrimaryPath, "File", log, null, -1, null) : new BackupListEntry { BackupId = h.Id, Copy = "File", Path = h.PrimaryPath, Status = BrowseStatus.Damaged, Check = CheckState.Failed, Detail = h.Problem, Bytes = h.Bytes, MadeUtc = h.CreatedUtc }); continue; }
                bool published = state != null && Published.Contains(state);
                if (h.PrimaryPath != null)
                {
                    if (File.Exists(h.PrimaryPath)) Add(FileRow(h, h.PrimaryPath, rec == null ? "File" : "First copy", log, prim == null ? null : Parse(prim.S("verified_utc")), prim?.L("bytes") ?? -1, prim?.S("mtime_utc")));
                    else if (published) Add(Gone(h, h.PrimaryPath, "First copy", "The backup was finished and saved here, but the file is no longer there."));
                }
                var arcFinal = rec != null ? arc?.S("final") : h.ArchivePath;
                if (arcFinal != null)
                {
                    if (File.Exists(arcFinal)) Add(FileRow(h, arcFinal, rec == null ? "File" : "Second copy", log, arc == null ? null : Parse(arc.S("verified_utc")), arc?.L("bytes") ?? -1, null));
                    else if (state == "complete") Add(Gone(h, arcFinal, "Second copy", "The additional copy was finished and saved here, but the file is no longer there."));
                }
                if (rec != null)
                {
                    var pp = prim?.S("partial");
                    if (!published)
                    {
                        if (pp != null && File.Exists(pp)) Add(state == "damaged" ? Damaged(h, pp, rec.S("problem")) : Unfinished(h, pp, "First copy (unfinished)", "The backup did not finish (" + Words(state) + "), so this file is not a backup." + (rec.S("problem") != null ? " " + rec.S("problem") : "")));
                        else Add(Unfinished(h, pp, "Record only", "The backup did not finish (" + Words(state) + ") and its unfinished file is no longer there."));
                    }
                    var ap = arc?.S("partial");
                    if (ap != null && File.Exists(ap) && (arcFinal == null || !File.Exists(arcFinal))) Add(Unfinished(h, ap, "Second copy (unfinished)", arc.S("problem") ?? "The second copy was not finished."));
                }
            }
            // every other backup file in the folders (copies brought in from elsewhere, even of a backup listed above): never left out
            foreach (var folder in new[] { s.PrimaryFolder, s.ArchiveFolder }.Where(x => !string.IsNullOrEmpty(x) && Directory.Exists(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var f in Directory.GetFiles(folder, "*" + BackupFormat.Extension).Where(f => !done.Contains(f)))
                    Add(FileRow(new HistoryEntry { Id = "file:" + f }, f, "File", log, null, -1, null));
                foreach (var f in Directory.GetFiles(folder, "*" + BackupFormat.Partial).Where(f => !done.Contains(f)))
                    Add(Unfinished(new HistoryEntry { Id = "partial:" + f, CreatedUtc = File.GetCreationTimeUtc(f) }, f, "Unfinished file", "This file was never finished, so it is not a backup. Nothing in it is used."));
            }
            return rows.OrderByDescending(r => r.MadeUtc ?? DateTime.MinValue).ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Checks one backup file completely now (nothing in it is changed), records the result with the real time in the check
        /// log, and returns its refreshed row. A canceled check records nothing.</summary>
        public static BackupListEntry Check(Settings s, string path, Func<bool> cancelled = null, Action<long, long> progress = null)
        {
            string canceled = null;
            try
            {
                using (var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096))
                    Record(s, path, Verifier.VerifyFile(path, null, cancelled, progress), cancelled);
            }
            catch (OperationCanceledException) { canceled = "The check was canceled; nothing was recorded."; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { canceled = "The file could not be checked: " + ex.Message; }
            var row = List(s).FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase))
                      ?? (File.Exists(path) ? FileRow(new HistoryEntry { Id = History.Peek(path)?.S("backup_id") ?? "file:" + path }, path, "File", CheckLog.Load(s), null, -1, null) : new BackupListEntry { Path = path, Copy = "File", Status = BrowseStatus.Missing, Detail = "The file was not found." });
            if (canceled != null) row.Detail = canceled;
            return row;
        }

        /// <summary>records a full check of a console backup file that just ran (Check the File, or the scheduled check) in the check log, so
        /// every list shows it as the file's last full check: passed, or what can still be trusted in it (SalvageCheck)</summary>
        public static void Record(Settings s, string path, Verification v, Func<bool> cancelled = null)
        {
            if (v == null) return;
            if (v.Ok) { CheckLog.Record(s, path, "verified", v, null); return; }
            var a = SalvageCheck.Assess(path, v, s, cancelled); CheckLog.Record(s, path, a.Salvageable ? "salvage" : "damaged", v, a);
        }

        static BackupListEntry FileRow(HistoryEntry h, string path, string copy, CheckLog log, DateTime? recVerified, long recBytes, string recMtime)
        {
            var fi = new FileInfo(path); var mtime = fi.LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture);
            var r = new BackupListEntry { BackupId = h.Id, Copy = copy, Path = path, Bytes = fi.Length, ConsoleTag = h.ConsoleTag, MadeUtc = h.CreatedUtc, Simulated = h.Simulated, StateText = h.Kind };
            var lg = log.Find(path);
            if (r.ConsoleTag == null || r.MadeUtc == null)
            {
                var m = History.Peek(path);
                // a file whose table of contents is damaged cannot be peeked into: its last check (made through the part finder, for exactly this
                // size and time) still names the backup and its console
                var l0 = lg?.O("last_check");
                if (m == null && l0?.O("index") != null && l0.L("bytes") == fi.Length && l0.S("mtime_utc") == mtime)
                    m = new Dictionary<string, object> { { "backup_id", l0.S("backup_id") }, { "completed_utc", l0.S("made_utc") }, { "simulated", l0.B("simulated") }, { "console", new Dictionary<string, object> { { "tag", l0.S("console_tag") } } } };
                if (m == null) { r.Status = BrowseStatus.Damaged; r.Check = CheckState.Failed; r.Detail = "This file is not a readable console backup."; r.MadeUtc = fi.LastWriteTimeUtc; return r; }
                r.ConsoleTag = m.O("console")?.S("tag"); r.Simulated = m.B("simulated"); r.StateText = m.O("state")?.S("text"); r.MadeUtc = Parse(m.S("completed_utc")) ?? fi.LastWriteTimeUtc; if (r.BackupId == null || r.BackupId.StartsWith("file:", StringComparison.Ordinal)) r.BackupId = m.S("backup_id") ?? r.BackupId;
            }
            // the last verified date: the newest full check that passed, here or when the backup was made
            var lv = lg?.O("last_verified"); var logVerified = Parse(lv?.S("utc"));
            if (logVerified != null && (recVerified == null || logVerified > recVerified)) { r.LastVerifiedUtc = logVerified; r.LastVerifiedBy = "check"; }
            else if (recVerified != null) { r.LastVerifiedUtc = recVerified; r.LastVerifiedBy = "made"; }
            var lc = lg?.O("last_check"); r.LastCheckUtc = Parse(lc?.S("utc"));
            bool logSame = lc != null && lc.L("bytes") == fi.Length && lc.S("mtime_utc") == mtime;
            bool recSame = recVerified != null && recBytes == fi.Length && (recMtime != null ? recMtime == mtime : fi.LastWriteTimeUtc <= recVerified.Value.AddSeconds(2));
            if (logSame && (recVerified == null || r.LastCheckUtc >= recVerified || !recSame))
            {
                var res = lc.S("result");
                foreach (var x in lc.A("intact") ?? new List<object>()) r.IntactParts.Add(x as string);
                foreach (var x in lc.A("damaged") ?? new List<object>()) r.DamagedParts.Add(x as string);
                if (res == "verified") { r.Status = BrowseStatus.Complete; r.Check = CheckState.Verified; }
                else
                {
                    r.Status = res == "salvage" ? BrowseStatus.Salvage : BrowseStatus.Damaged; r.Check = CheckState.Failed; r.Detail = lc.S("problem");
                    var ix = lc.O("index");
                    if (ix != null)
                    {
                        r.IndexDamaged = true; r.FoundBy = ix.S("found_by");
                        r.Detail = "Its table of contents (the ZIP index) is damaged" + (r.FoundBy != null ? "; its parts were found through " + r.FoundBy + " and checked against the backup's own records (" + r.IntactParts.Count + " intact, " + r.DamagedParts.Count + " damaged or missing)." : ", and no trustworthy list of its parts survives.");
                    }
                    var fl = lc.O("files");
                    if (fl != null && fl.S("problem") == null) { r.FilesVerified = (int)fl.L("verified"); r.FilesChecked = (int)fl.L("checked"); r.Detail = (r.Detail == null ? "" : r.Detail.TrimEnd('.') + ". ") + "Its data storage image is damaged; " + r.FilesVerified + " of its " + r.FilesChecked + " files still match the checksums recorded when the backup was made" + (r.FilesVerified > 0 ? " and can be copied." : "."); }
                    else if (fl?.S("problem") != null) r.Detail = (r.Detail == null ? "" : r.Detail.TrimEnd('.') + ". ") + fl.S("problem");
                }
            }
            else if (recSame) { r.Status = BrowseStatus.Complete; r.Check = CheckState.Verified; }
            else if (lc != null || recVerified != null) { r.Status = BrowseStatus.Complete; r.Check = CheckState.ChangedSinceCheck; r.Detail = "The file has changed since it was last checked. Check it again before relying on it."; }
            else { r.Status = BrowseStatus.Complete; r.Check = CheckState.NotChecked; r.Detail = "No full check of this file is recorded here."; }
            return r;
        }

        /// <summary>the row for one backup file anywhere on this PC, listed in the backup locations or not (a file opened from another folder):
        /// the same rules and words as the list, read-only, without running a check and without adding the file to any list or folder</summary>
        public static BackupListEntry Row(Settings s, string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try { var listed = List(s).FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)); if (listed != null) return listed; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            if (!File.Exists(path)) return new BackupListEntry { Path = path, Copy = "File", Status = BrowseStatus.Missing, Detail = "The file was not found." };
            if (path.EndsWith(BackupFormat.Partial, StringComparison.OrdinalIgnoreCase))
                return Unfinished(new HistoryEntry { Id = "partial:" + path, CreatedUtc = File.GetCreationTimeUtc(path) }, path, "Unfinished file", "This file was never finished, so it is not a backup. Nothing in it is used.");
            return FileRow(new HistoryEntry { Id = "file:" + path }, path, "File", CheckLog.Load(s), null, -1, null);
        }

        static BackupListEntry Gone(HistoryEntry h, string path, string copy, string why) => new BackupListEntry { BackupId = h.Id, Copy = copy, Path = path, Status = BrowseStatus.Missing, Check = CheckState.NotChecked, Detail = why, ConsoleTag = h.ConsoleTag, MadeUtc = h.CreatedUtc, Simulated = h.Simulated, StateText = h.Kind };
        static BackupListEntry Unfinished(HistoryEntry h, string path, string copy, string why) => new BackupListEntry { BackupId = h.Id, Copy = copy, Path = path, Status = BrowseStatus.Incomplete, Check = CheckState.NotChecked, Detail = why, ConsoleTag = h.ConsoleTag, MadeUtc = h.CreatedUtc, Simulated = h.Simulated, Bytes = path != null && File.Exists(path) ? new FileInfo(path).Length : 0 };
        static BackupListEntry Damaged(HistoryEntry h, string path, string why) => new BackupListEntry { BackupId = h.Id, Copy = "First copy (unfinished)", Path = path, Status = BrowseStatus.Damaged, Check = CheckState.Failed, Detail = "It did not pass its check when it was made: " + why, ConsoleTag = h.ConsoleTag, MadeUtc = h.CreatedUtc, Simulated = h.Simulated, Bytes = File.Exists(path) ? new FileInfo(path).Length : 0 };
        static string Words(string state) => state == "canceled" ? "it was canceled" : state == "stopped" ? "it stopped" : "it ended while " + (state ?? "starting");
        internal static DateTime? Parse(string s) => s != null && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : (DateTime?)null;
    }

    /// <summary>The check log: per backup file, the last full check (result, real time, the file's size and time then) and the last one
    /// that passed. Written atomically; a failure to write it never changes a result.</summary>
    internal sealed class CheckLog
    {
        public const string FileName = "browser-checks.json", Schema = "ccs-backup-checks/1";
        readonly List<Dictionary<string, object>> rows;
        CheckLog(List<Dictionary<string, object>> r) { rows = r; }
        static string PathOf(Settings s) => s == null ? null : System.IO.Path.Combine(s.RecordsFolder, FileName);

        public static CheckLog Load(Settings s)
        {
            var p = PathOf(s); var d = p != null && File.Exists(p) ? Library.Record.Load(p) : null;
            return new CheckLog(d?.S("schema") == Schema ? (d.A("files") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList() : new List<Dictionary<string, object>>());
        }

        public Dictionary<string, object> Find(string path) => rows.LastOrDefault(r => string.Equals(r.S("path"), path, StringComparison.OrdinalIgnoreCase));

        public static void Record(Settings s, string path, string result, Verification v, SalvageCheck.Assessment a)
        {
            var p = PathOf(s); if (p == null) return;
            try
            {
                var fi = new FileInfo(path); var now = Library.Record.Now(); var mtime = fi.LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture);
                var log = Load(s); var old = log.Find(path);
                var check = new JObj { { "utc", now }, { "result", result }, { "bytes", fi.Length }, { "mtime_utc", mtime }, { "backup_id", v?.Manifest?.S("backup_id") }, { "archive_sha256", v?.ArchiveSha256 } };
                if (result != "verified") check.Set("problem", v?.Problems.FirstOrDefault()).Set("intact", a?.Intact.OrderBy(x => x, StringComparer.Ordinal).Cast<object>().ToList() ?? new List<object>()).Set("damaged", a?.Damaged.OrderBy(x => x, StringComparer.Ordinal).Cast<object>().ToList() ?? new List<object>());
                if (result != "verified" && a != null && !a.DataIntact && (a.Files != null || a.FilesProblem != null))
                    check.Set("files", new JObj { { "verified", (long)a.FilesVerified }, { "checked", (long)a.FilesChecked }, { "problem", a.Files == null ? a.FilesProblem : null } });
                if (result != "verified" && a?.Parts != null)
                {
                    check.Set("index", new JObj { { "damaged", true }, { "found_by", a.FoundBy }, { "problem", a.IndexProblem } });
                    check.Set("console_tag", a.Manifest?.O("console")?.S("tag")).Set("made_utc", a.Manifest?.S("completed_utc")).Set("simulated", a.Manifest?.B("simulated") ?? false).Set("backup_id", a.Manifest?.S("backup_id"));
                }
                if (result != "verified" && a?.Parts == null && a?.Problem != null && a.Problem.StartsWith("its table of contents", StringComparison.Ordinal)) check.Set("index", new JObj { { "damaged", true }, { "found_by", null }, { "problem", a.Problem } });
                object verified = result == "verified" ? new JObj { { "utc", now }, { "bytes", fi.Length }, { "mtime_utc", mtime }, { "archive_sha256", v.ArchiveSha256 } } : (object)old?.O("last_verified");
                var row = new JObj { { "path", System.IO.Path.GetFullPath(path) }, { "last_check", check }, { "last_verified", verified } };
                var all = log.rows.Where(r => !string.Equals(r.S("path"), path, StringComparison.OrdinalIgnoreCase)).Cast<object>().ToList(); all.Add(row);
                Directory.CreateDirectory(s.RecordsFolder);
                try { new DirectoryInfo(s.RecordsFolder).Attributes |= FileAttributes.Hidden; } catch { }
                Durable.WriteAtomic(p, Json.Write(new JObj { { "schema", Schema }, { "note", "When each backup file was last checked on this PC, and the result. History only: every use checks the file again." }, { "files", all } }));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }
    }

    /// <summary>What can still be trusted in a backup file that failed its full check: each member's checksum is compared with the trusted
    /// one (the write-once capture record and the backup's own manifest; a member they disagree about is not trusted). The data storage and
    /// the file list are read again when the full check did not get to them. When the data storage image fails its own checksum but the
    /// file list is intact, every file the list names is checked one by one (Repair.Salvage.CheckFiles: its bytes, read from the damaged
    /// image as it is, against the size and SHA-256 the list recorded), so a damaged image whose files mostly survive is not written off.
    /// The list of files comes only from the verified file list, never from the damaged image's own directories.</summary>
    public static class SalvageCheck
    {
        public sealed class Assessment
        {
            public Dictionary<string, object> Manifest; public string DataMember, Problem; public bool DataIntact;
            /// <summary>the file's own table of contents (ZIP index) is damaged: the parts Backup Repair's part finder located (Parts, each
            /// checked against the trusted records: Intact or Damaged), how it found them (FoundBy) and why the index could not be read;
            /// ManifestFromMap: the manifest itself is lost and its facts come from the repair map</summary>
            public List<ZipEntryInfo> Parts; public string IndexProblem, FoundBy; public bool ManifestFromMap;
            public readonly Dictionary<string, (string sha256, long size, string source)> Trusted = new Dictionary<string, (string, long, string)>(StringComparer.Ordinal);
            public readonly HashSet<string> Intact = new HashSet<string>(StringComparer.Ordinal), Damaged = new HashSet<string>(StringComparer.Ordinal), Unchecked = new HashSet<string>(StringComparer.Ordinal);
            /// <summary>the data storage image damaged, its file list intact: each file of the list checked on its own (folders are not
            /// items); null when that was not possible, with FilesProblem saying why in plain words</summary>
            public List<Repair.SalvageItem> Files; public string FilesProblem;
            public int FilesVerified => Files?.Count(i => i.Status == "verified") ?? 0;
            /// <summary>the regular files the list names (links and special files have no contents to check)</summary>
            public int FilesChecked => Files?.Count(i => i.Status != "not-exportable") ?? 0;
            /// <summary>some files of the data storage can still be trusted: the whole image, or at least one file on its own</summary>
            public bool Salvageable => DataIntact || FilesVerified > 0;
        }

        public static Assessment Assess(string path, Verification v, Settings s, Func<bool> cancelled = null)
        {
            var a = new Assessment();
            ZipReader z;
            try { z = ZipReader.Open(path, BackupFormat.Allowed); }
            catch (Exception ex) when (ex is ZipFormatException || ex is IOException || ex is UnauthorizedAccessException) { return FromParts(path, ex.Message, s, cancelled); }
            using (z)
            {
                a.Manifest = v?.Manifest;
                if (a.Manifest == null)
                {
                    var me = z.Find(BackupFormat.ManifestName);
                    if (me != null && me.Size <= BackupFormat.MaxSize(me.Name)) try { using (var st = z.OpenMember(me)) using (var r = new StreamReader(st)) a.Manifest = Json.Parse(r.ReadToEnd()); } catch (Exception ex) when (ex is IOException || ex is FormatException || ex is ArgumentException || ex is InvalidOperationException) { }
                }
                if (a.Manifest != null && (a.Manifest.S("format") != BackupFormat.Format || a.Manifest.S("schema") != BackupFormat.Schema)) a.Manifest = null;
                if (a.Manifest == null) a.Problem = "the backup's own list of its parts is damaged";
                foreach (var mm in (a.Manifest?.A("members") ?? new List<object>()).OfType<Dictionary<string, object>>())
                    if (mm.S("path") != null && Hex.IsHex(mm.S("sha256"), 64)) a.Trusted[mm.S("path")] = (mm.S("sha256"), mm.L("size"), "the backup's own list");
                MergeCaptureRecord(a, s, path, a.Manifest?.S("backup_id"));
                var dev = (a.Manifest?.O("layout")?.A("partitions") ?? new List<object>()).OfType<Dictionary<string, object>>().FirstOrDefault(p => p.S("label") == "data" || p.S("label") == "rootfs_data")?.S("device");
                a.DataMember = dev == null ? null : "restore/" + dev + ".img";
                foreach (var kv in a.Trusted)
                {
                    var ze = z.Find(kv.Key);
                    if (ze == null || ze.Size != kv.Value.size) { a.Damaged.Add(kv.Key); continue; }
                    string got = null;
                    if (v != null && v.Members.TryGetValue(kv.Key, out var vm) && vm.size == ze.Size) got = vm.sha256;
                    else if (v != null && v.Members.Count > 0) { a.Damaged.Add(kv.Key); continue; }   // the full check read every member and could not read this one
                    else if (kv.Key == a.DataMember || kv.Key == BackupFormat.InventoryName)
                    {
                        try { var h = new HashingSink(null, null, cancelled); using (var st = z.OpenMember(ze)) Streams.Copy(st, h, -1, cancelled); h.Finish(); got = h.Sha256; }
                        catch (Exception ex) when (ex is IOException || ex is InvalidDataException) { a.Damaged.Add(kv.Key); continue; }
                    }
                    if (got == null) a.Unchecked.Add(kv.Key); else if (got == kv.Value.sha256) a.Intact.Add(kv.Key); else a.Damaged.Add(kv.Key);
                }
                a.DataIntact = a.DataMember != null && a.Intact.Contains(a.DataMember);
                if (!a.DataIntact && a.DataMember != null) CheckFiles(a, z, cancelled);
            }
            return a;
        }

        /// <summary>the write-once capture record's checksums join the trusted ones; a member the two disagree about is trusted by neither</summary>
        static void MergeCaptureRecord(Assessment a, Settings s, string path, string backupId)
        {
            var rec = CaptureRecord(s, path, backupId);
            foreach (var mm in (rec?.A("members") ?? new List<object>()).OfType<Dictionary<string, object>>())
            {
                var p = mm.S("path"); var sha = mm.S("sha256"); if (p == null || !Hex.IsHex(sha, 64)) continue;
                if (a.Trusted.TryGetValue(p, out var t) && (t.sha256 != sha || t.size != mm.L("size"))) { a.Trusted.Remove(p); a.Unchecked.Add(p); continue; }   // they disagree: neither is trusted
                a.Trusted[p] = (sha, mm.L("size"), t.source == null ? "the record written while the backup was made" : "the backup's own list and the record written while it was made");
            }
        }

        /// <summary>A file whose table of contents (its ZIP index) cannot be read: Backup Repair's part finder (Repair/Examination.cs: the
        /// repair map's early list of parts, a lenient read of the index, or a walk of the part headers with checked lengths) locates each
        /// part, and one pass over the file checks every located part against the trusted records (the manifest and the repair map, in
        /// agreement, and the write-once capture record). Only parts whose bytes match are intact; the rest are damaged or missing. Nothing is
        /// written: the file is only read.</summary>
        static Assessment FromParts(string path, string zipProblem, Settings s, Func<bool> cancelled)
        {
            var a = new Assessment { IndexProblem = zipProblem };
            Repair.BackupFile bf;
            try { bf = Repair.BackupFile.Open(path, "the backup file"); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { a.Problem = "the file could not be read (" + ex.Message + ")"; return a; }
            using (bf)
            {
                var map = Repair.TrustedMap.Build(new[] { bf }, out var why);
                if (map == null) { a.Problem = "its table of contents (ZIP index) is damaged (" + zipProblem.TrimEnd('.') + "), and " + (why ?? "no trustworthy list of its parts survives").TrimEnd('.'); return a; }
                bf.Examine(map, cancelled, null);
                a.FoundBy = bf.LocatedBy;
                a.Manifest = map.Manifest;
                if (a.Manifest == null)
                {
                    // the manifest is lost; the repair map (made with it, in agreement) names the backup and its console
                    a.ManifestFromMap = true;
                    a.Manifest = new Dictionary<string, object> { { "format", BackupFormat.Format }, { "schema", BackupFormat.Schema }, { "backup_id", map.BackupId }, { "simulated", map.Simulated }, { "completed_utc", map.CompletedUtc },
                        { "console", new Dictionary<string, object> { { "tag", map.Tag }, { "hwid", map.Hwid }, { "board", map.Board } } } };
                }
                foreach (var r in map.Members) if (r.Sha256 != null) a.Trusted[r.Path] = (r.Sha256, r.Size, r.RecordedBy == "the manifest" ? "the backup's own list" : r.RecordedBy);
                MergeCaptureRecord(a, s, path, map.BackupId);
                a.Parts = new List<ZipEntryInfo>();
                foreach (var r in map.Members)
                {
                    var mc = bf.Check(r.Path);
                    if (mc?.At == null) { if (a.Trusted.ContainsKey(r.Path)) a.Damaged.Add(r.Path); continue; }   // not found in the file
                    var L = mc.At; bool trusted = a.Trusted.TryGetValue(r.Path, out var t);
                    bool ok = mc.Verified && (!trusted || t.sha256 == mc.Sha256 && (t.size < 0 || t.size == L.Size));
                    a.Parts.Add(new ZipEntryInfo { Name = r.Path, Method = (ushort)L.Method, Crc = ok ? mc.Crc : L.Crc, Size = L.Size, CompressedSize = L.CompressedSize, HeaderOffset = L.HeaderOffset, DataOffset = L.DataOffset, Flags = L.Flags });
                    if (!ok) a.Damaged.Add(r.Path); else if (trusted) a.Intact.Add(r.Path); else a.Unchecked.Add(r.Path);
                }
                if (bf.At.TryGetValue(BackupFormat.ManifestName, out var ml))   // the manifest as found (listed as damaged when it is lost)
                    a.Parts.Add(new ZipEntryInfo { Name = BackupFormat.ManifestName, Method = (ushort)ml.Method, Crc = ml.Crc, Size = ml.Size, CompressedSize = ml.CompressedSize, HeaderOffset = ml.HeaderOffset, DataOffset = ml.DataOffset, Flags = ml.Flags });
                if (map.Manifest == null) a.Damaged.Add(BackupFormat.ManifestName);
                var z = ZipReader.OfLocatedParts(bf.S, a.Parts);
                var dev = (a.Manifest.O("layout")?.A("partitions") ?? new List<object>()).OfType<Dictionary<string, object>>().FirstOrDefault(p => p.S("label") == "data" || p.S("label") == "rootfs_data")?.S("device");
                if (dev == null && a.Intact.Contains(BackupFormat.InventoryName))   // no layout without the manifest: the file list names its partition
                    try { using (var st = z.OpenMember(z.Find(BackupFormat.InventoryName))) dev = BackupContents.InventoryPartition(st); } catch (Exception ex) when (ex is IOException || ex is FormatException || ex is ArgumentException || ex is InvalidOperationException || ex is InvalidDataException) { }
                if (dev != null && !System.Text.RegularExpressions.Regex.IsMatch(dev, "^nand[a-f]$")) dev = null;
                a.DataMember = dev == null ? null : "restore/" + dev + ".img";
                a.DataIntact = a.DataMember != null && a.Intact.Contains(a.DataMember);
                if (!a.DataIntact && a.DataMember != null) CheckFiles(a, z, cancelled);
            }
            return a;
        }

        /// <summary>the files of a damaged data storage image, one by one, through the intact file list (the proof Backup Repair's Copy Out
        /// uses). Nothing is derived from the damaged bytes: a file is verified only by the checksum recorded when the backup was made.</summary>
        static void CheckFiles(Assessment a, ZipReader z, Func<bool> cancelled)
        {
            var dz = z.Find(a.DataMember); var iz = z.Find(BackupFormat.InventoryName);
            if (dz == null && a.Parts != null && a.Damaged.Contains(a.DataMember)) { a.FilesProblem = "The data storage image was not found in the file (its table of contents is damaged), so none of its files can be read."; return; }
            if (iz == null || !a.Trusted.ContainsKey(BackupFormat.InventoryName)) { a.FilesProblem = "This backup has no file list that can be trusted, so none of the files inside its damaged data storage can be checked."; return; }
            if (!a.Intact.Contains(BackupFormat.InventoryName)) { a.FilesProblem = "The backup's file list is damaged too, so none of the files inside its damaged data storage can be checked."; return; }
            if (dz == null) { a.FilesProblem = "The data storage image is missing from the file, so none of its files can be read."; return; }
            if (dz.Method != 0) { a.FilesProblem = "The data storage image is not stored in a way that can be read in place, so its files cannot be checked."; return; }
            List<Dictionary<string, object>> entries;
            try { using (var st = z.OpenMember(iz)) entries = BackupContents.InventoryEntries(st); }
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is ArgumentException || ex is InvalidOperationException || ex is InvalidDataException)
            { a.FilesProblem = "The backup's file list could not be read (" + ex.Message.TrimEnd('.') + "), so none of the files inside its damaged data storage can be checked."; return; }
            if (!entries.Any(e => e.S("type") == "f")) { a.FilesProblem = "The backup's file list names no files (when the backup was made, its data storage could not be read as a file system), so nothing inside the damaged image can be checked one by one."; return; }
            Ext4.Ext4Reader fs;
            try { fs = new Ext4.Ext4Reader(z.StoredWindow(dz)); }
            catch (Ext4.Ext4Exception ex) { a.FilesProblem = "The file system inside the damaged data storage image cannot be read (" + ex.Message + "), so none of its files can be checked."; return; }
            var proof = "its SHA-256 matches the one in the backup's file list (" + BackupFormat.InventoryName + "), whose own SHA-256 matches " + a.Trusted[BackupFormat.InventoryName].source;
            a.Files = Repair.Salvage.CheckFiles(fs, entries, false, proof, a.DataMember, cancelled);
        }

        /// <summary>the write-once record of the capture that made this file (its member checksums as they arrived from the console)</summary>
        static Dictionary<string, object> CaptureRecord(Settings s, string path, string backupId)
        {
            if (s == null || !Directory.Exists(s.RecordsFolder)) return null;
            foreach (var f in Directory.GetFiles(s.RecordsFolder, "backup-*.json"))
            {
                var d = Library.Record.Load(f); if (d == null) continue;
                bool same = backupId != null && d.S("id") == backupId
                            || new[] { d.O("primary")?.S("final"), d.O("primary")?.S("partial"), d.O("archive")?.S("final") }.Any(p => p != null && string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                if (same) return d;
            }
            return null;
        }
    }
}
