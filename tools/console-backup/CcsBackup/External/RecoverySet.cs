// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// A recovery set: the console backup and the USB and SD backup made for one change (another task's protective backup of a console and
// the external storage it may change), named together in one record so that they can later be restored as one clearly identified set.
// Both files are checked from scratch when the set is made (each by its own verifier); the record keeps each file's SHA-256, its backup
// ID, its console or storage identity and when each was read, and says plainly that they were read one after the other: never one
// snapshot of everything at a single moment. The record is history in <records folder>\recovery-set-<id>.json, never authority: every
// use checks both files again. A set whose storage backup is not complete protection (StorageOutcome.Protected) is refused.
using CcsBackup.Archive;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CcsBackup.External
{
    public sealed class RecoverySetResult { public bool Ok; public string Problem, RecordFile, Id, ConsistencyText; }

    public static class RecoverySet
    {
        public const string Schema = "ccs-recovery-set/1";

        /// <summary>names a verified console backup and a verified, complete USB and SD backup as one recovery set for purpose</summary>
        public static RecoverySetResult Make(Settings s, string consoleBackup, string storageBackup, string purpose, Func<bool> cancelled = null)
        {
            var r = new RecoverySetResult();
            var cv = Verifier.VerifyFile(consoleBackup, null, cancelled);
            if (!cv.Ok) { r.Problem = "The console backup did not pass its check: " + cv.Problems[0]; return r; }
            var sv = StorageVerifier.Verify(storageBackup, null, cancelled);
            if (!sv.Ok) { r.Problem = "The USB and SD backup did not pass its check: " + sv.Problems[0]; return r; }
            if (!sv.ScopeComplete) { r.Problem = "The USB and SD backup does not hold everything that was selected, so it is not protection for that storage."; return r; }
            var cm = cv.Manifest; var made = DateTime.Parse(cm.S("created_utc"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal); var done = DateTime.Parse(cm.S("completed_utc"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            string W(DateTime t) => t.ToLocalTime().ToString("MMMM d, yyyy 'at' h:mm:ss tt", CultureInfo.GetCultureInfo("en-US"));
            var parts = new List<string> { "the console's built-in storage (console " + cv.ConsoleTag + ") from " + W(made) + " to " + W(done) };
            foreach (var x in sv.SourceList)
                parts.Add(x.S("label") + " from " + W(DateTime.Parse(x.S("started_utc"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)) + " to " + W(DateTime.Parse(x.S("finished_utc"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)));
            r.ConsistencyText = "This recovery set was read one part after another, not at one moment: " + string.Join("; ", parts) + ". It is not one snapshot of everything at a single moment: a change to one part after it was read is not in this set.";
            r.Id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                var rec = Record.Start(s.RecordsFolder, r.Id, "recovery-set", new JObj
                {
                    { "set_schema", Schema }, { "purpose", purpose }, { "consistency", r.ConsistencyText },
                    { "console_backup", new JObj { { "file", Path.GetFullPath(consoleBackup) }, { "sha256", cv.ArchiveSha256 }, { "bytes", cv.Bytes }, { "backup_id", cm.S("backup_id") }, { "console_tag", cv.ConsoleTag }, { "created_utc", cm.S("created_utc") }, { "completed_utc", cm.S("completed_utc") } } },
                    { "storage_backup", new JObj { { "file", Path.GetFullPath(storageBackup) }, { "sha256", sv.ArchiveSha256 }, { "bytes", sv.Bytes }, { "backup_id", sv.BackupId },
                        { "sources", sv.SourceList.Select(x => (object)new JObj { { "id", x.S("id") }, { "label", x.S("label") }, { "key", x.O("identity")?.S("key") }, { "mode", x.S("mode") }, { "started_utc", x.S("started_utc") }, { "finished_utc", x.S("finished_utc") } }).ToList() } } },
                });
                rec.State("complete"); r.RecordFile = rec.File; r.Ok = true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { r.Problem = "The recovery set's record could not be written: " + ex.Message; }
            return r;
        }

        /// <summary>a recovery set's record, with both files checked again now (Ok only when both still pass and match the record)</summary>
        public static RecoverySetResult Check(string recordFile, Func<bool> cancelled = null)
        {
            var r = new RecoverySetResult { RecordFile = recordFile }; var d = Record.Load(recordFile);
            if (d?.S("set_schema") != Schema) { r.Problem = "This is not a recovery set's record."; return r; }
            r.Id = d.S("id"); r.ConsistencyText = d.S("consistency");
            var c = d.O("console_backup"); var st = d.O("storage_backup");
            var cv = Verifier.VerifyFile(c?.S("file"), null, cancelled);
            if (!cv.Ok || cv.ArchiveSha256 != c.S("sha256")) { r.Problem = "The set's console backup is missing, damaged or not the file it named."; return r; }
            var sv = StorageVerifier.Verify(st?.S("file"), null, cancelled);
            if (!sv.Ok || sv.ArchiveSha256 != st.S("sha256")) { r.Problem = "The set's USB and SD backup is missing, damaged or not the file it named."; return r; }
            r.Ok = true; return r;
        }
    }
}
