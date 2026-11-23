// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The USB and SD backups on this PC: every file in the backup location's "USB and SD Backups" folder and in the additional-copy location's,
// with an honest status, read-only (file names, sizes and times, the operation records, each file's manifest through the strict reader,
// and the check log). Nothing here is ever treated as a verified result: Complete means a finished file whose record says it was checked
// and that has not changed since, or one whose last check on this PC passed; every use checks the file again.
//   Check   a full check now (StorageVerifier), recorded with the real time in <records folder>\storage-checks.json (history, not authority).
using CcsBackup.Archive;
using CcsBackup.Browse;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CcsBackup.External
{
    public sealed class StorageBackupRow
    {
        public string Path, Copy, BackupId, Label, Detail, Purpose; public DateTime? MadeUtc, LastVerifiedUtc; public long Bytes;
        /// <summary>complete | incomplete-scope | unfinished | damaged | not-checked | changed | missing</summary>
        public string Status;
        public bool? ScopeComplete; public List<(string label, string mode)> Sources = new List<(string, string)>();
        public bool CanOpen => (Status == "complete" || Status == "incomplete-scope" || Status == "not-checked" || Status == "changed") && File.Exists(Path);
        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case "complete": return "Complete · checked";
                    case "incomplete-scope": return "Checked · not everything was copied";
                    case "not-checked": return "Not checked yet";
                    case "changed": return "Changed since it was checked";
                    case "unfinished": return "Unfinished: not a backup";
                    case "damaged": return "Damaged";
                    default: return "File not found";
                }
            }
        }
    }

    public static class StorageLibrary
    {
        const string LogName = "storage-checks.json", LogSchema = "ccs-storage-checks/1";

        public static List<StorageBackupRow> List(Settings s)
        {
            var rows = new List<StorageBackupRow>(); var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var log = LoadLog(s);
            var recs = new List<Dictionary<string, object>>();
            if (Directory.Exists(s.RecordsFolder)) foreach (var f in Directory.GetFiles(s.RecordsFolder, "storage-*.json")) { var d = Record.Load(f); if (d?.S("kind") == "storage") recs.Add(d); }
            foreach (var d in recs)
            {
                var prim = d.O("primary"); var final = prim?.S("final"); var partial = prim?.S("partial"); var state = d.S("state");
                bool published = state == "published" || state == "copying" || state == "complete" || state == "complete-one-copy";
                if (final != null && File.Exists(final)) { rows.Add(Row(final, "First copy", d, log)); done.Add(final); }
                else if (published && final != null) rows.Add(new StorageBackupRow { Path = final, Copy = "First copy", BackupId = d.S("id"), Label = d.S("label"), Status = "missing", MadeUtc = BackupList.Parse(d.S("created_utc")), Detail = "The backup was finished and saved here, but the file is no longer there." });
                else if (!published && partial != null) { rows.Add(new StorageBackupRow { Path = partial, Copy = "Unfinished", BackupId = d.S("id"), Label = d.S("label"), Status = "unfinished", MadeUtc = BackupList.Parse(d.S("created_utc")), Bytes = File.Exists(partial) ? new FileInfo(partial).Length : 0, Detail = "The backup did not finish (" + (state == "canceled" ? "it was canceled" : "it stopped while " + state) + "), so it is not a backup." + (d.S("problem") != null ? " " + d.S("problem") : "") }); done.Add(partial); }
                var arc = d.O("archive")?.S("final"); if (arc != null && File.Exists(arc)) { rows.Add(Row(arc, "Additional copy", d, log)); done.Add(arc); }
            }
            foreach (var folder in new[] { StorageBackup.Folder(s), StorageBackup.SecondFolder(s) }.Where(x => x != null && Directory.Exists(x)))
            {
                foreach (var f in Directory.GetFiles(folder, "*" + ExternalFormat.Extension).Where(x => !done.Contains(x))) { rows.Add(Row(f, "File", null, log)); done.Add(f); }
                foreach (var f in Directory.GetFiles(folder, "*" + ExternalFormat.Partial).Where(x => !done.Contains(x)))
                    rows.Add(new StorageBackupRow { Path = f, Copy = "Unfinished", Status = "unfinished", MadeUtc = File.GetCreationTimeUtc(f), Bytes = new FileInfo(f).Length, Detail = "This file was never finished, so it is not a backup. Nothing in it is used." });
            }
            return rows.OrderByDescending(r => r.MadeUtc ?? DateTime.MinValue).ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>the manifest of a storage backup through the strict reader (structure only; nothing is trusted from it)</summary>
        public static Dictionary<string, object> Peek(string file)
        {
            try
            {
                using (var z = ZipReader.Open(file, ExternalFormat.Allowed, ExternalFormat.Limits))
                {
                    var e = z.Entries.FirstOrDefault(x => x.Name == ExternalFormat.ManifestName); if (e == null || e.Size > Json.MaxText) return null;
                    using (var st = z.OpenMember(e)) using (var r = new StreamReader(st)) { var m = Json.Parse(r.ReadToEnd()); return m.S("format") == ExternalFormat.Format ? m : null; }
                }
            }
            catch (Exception) { return null; }
        }

        static StorageBackupRow Row(string path, string copy, Dictionary<string, object> rec, List<Dictionary<string, object>> log)
        {
            var fi = new FileInfo(path); var mtime = fi.LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture);
            var r = new StorageBackupRow { Path = path, Copy = copy, Bytes = fi.Length, BackupId = rec?.S("id"), Label = rec?.S("label"), Purpose = rec?.S("purpose"), MadeUtc = BackupList.Parse(rec?.S("created_utc")) };
            var m = Peek(path);
            if (m == null) { r.Status = "damaged"; r.Detail = "This file is not a readable USB and SD backup."; r.MadeUtc = r.MadeUtc ?? fi.LastWriteTimeUtc; return r; }
            r.BackupId = m.S("backup_id"); r.MadeUtc = BackupList.Parse(m.S("created_utc")) ?? r.MadeUtc; r.ScopeComplete = m.O("scope")?.B("complete"); r.Purpose = r.Purpose ?? m.S("purpose");
            foreach (var x in (m.A("sources") ?? new List<object>()).OfType<Dictionary<string, object>>()) r.Sources.Add((x.S("label"), x.S("mode")));
            r.Label = r.Label ?? (r.Sources.Count == 1 ? r.Sources[0].label : r.Sources.Count + " sources");
            var lg = log.LastOrDefault(x => string.Equals(x.S("path"), path, StringComparison.OrdinalIgnoreCase));
            var prim = rec?.O("primary"); bool recSame = prim != null && prim.S("verified_utc") != null && prim.L("bytes") == fi.Length && prim.S("mtime_utc") == mtime;
            bool logSame = lg != null && lg.L("bytes") == fi.Length && lg.S("mtime_utc") == mtime;
            if (logSame && lg.S("result") != "verified") { r.Status = "damaged"; r.Detail = lg.S("problem"); return r; }
            if (logSame || recSame)
            {
                r.Status = r.ScopeComplete == false ? "incomplete-scope" : "complete";
                r.LastVerifiedUtc = logSame ? BackupList.Parse(lg.S("utc")) : BackupList.Parse(prim.S("verified_utc"));
                if (r.Status == "incomplete-scope") r.Detail = "Not everything selected was copied as it was (files that could not be read or changed while they were copied). The rest can be used.";
                return r;
            }
            r.Status = lg != null || prim != null ? "changed" : "not-checked";
            r.Detail = r.Status == "changed" ? "The file has changed since it was last checked. Check it again before relying on it." : "This file has not been checked on this PC yet.";
            return r;
        }

        static List<Dictionary<string, object>> LoadLog(Settings s)
        {
            var p = Path.Combine(s.RecordsFolder, LogName); var d = File.Exists(p) ? Record.Load(p) : null;
            return d?.S("schema") == LogSchema ? (d.A("rows") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList() : new List<Dictionary<string, object>>();
        }

        /// <summary>checks one storage backup fully now and records the result (a canceled check records nothing)</summary>
        public static StorageVerification Check(Settings s, string path, Func<bool> cancelled = null, Action<long, long> progress = null)
        {
            var v = StorageVerifier.Verify(path, null, cancelled, progress);
            if (cancelled != null && cancelled()) return v;
            try
            {
                var fi = new FileInfo(path); var rows = LoadLog(s).Where(x => !string.Equals(x.S("path"), path, StringComparison.OrdinalIgnoreCase)).Cast<object>().ToList();
                rows.Add(new JObj { { "path", path }, { "bytes", fi.Length }, { "mtime_utc", fi.LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture) }, { "utc", Record.Now() }, { "result", v.Ok ? "verified" : "damaged" }, { "problem", v.Ok ? null : v.Problems[0] }, { "sha256", v.ArchiveSha256 } });
                Directory.CreateDirectory(s.RecordsFolder); Durable.WriteAtomic(Path.Combine(s.RecordsFolder, LogName), Json.Write(new JObj { { "schema", LogSchema }, { "rows", rows } }));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            return v;
        }

        /// <summary>the SHA-256 and size the backup's record says its file had when it was made (to say whether a repaired file is byte-for-byte it)</summary>
        public static (string sha256, long bytes)? RecordedIdentity(Settings s, string backupId)
        {
            if (s == null || backupId == null || backupId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            var d = Record.Load(Path.Combine(s.RecordsFolder, "storage-" + backupId + ".json")); var p = d?.O("primary");
            return p?.S("sha256") != null ? (p.S("sha256"), p.L("bytes")) : ((string, long)?)null;
        }
    }
}
