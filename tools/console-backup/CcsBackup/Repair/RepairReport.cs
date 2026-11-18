// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The plain report every repair route returns (and saves next to what it made, as JSON and as text): which files were read and what
// they are (name, size, SHA-256 as read, backup, console), the new file or folder made, how (the methods), which byte ranges were
// checked and taken from where, what was repaired, skipped and could not be recovered, errors, cancellation and what remains unknown.
// It never says a damaged backup is complete, and a salvage report always says that its folder is not a backup. "parity" lists the
// damaged byte ranges of each file in absolute offsets, in the repair map's 1,048,576-byte pieces, for a parity layer added later.
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CcsBackup.Repair
{
    public sealed class RepairReport
    {
        public const string Schema = "ccs-console-backup-repair-report/1";
        public readonly string Kind, Title;
        /// <summary>repaired | copied | salvaged | not-needed | not-repaired | refused | canceled | failed</summary>
        public string Outcome = "not-started", Summary;
        public readonly DateTime StartedUtc = DateTime.UtcNow; public DateTime FinishedUtc;
        public JObj Original, Output; public readonly List<JObj> Others = new List<JObj>(), Originals = new List<JObj>();
        public readonly List<string> Methods = new List<string>(), Errors = new List<string>(), Unknowns = new List<string>(), Notes = new List<string>();
        public readonly List<JObj> Regions = new List<JObj>(), Repaired = new List<JObj>(), Skipped = new List<JObj>(), Unrecoverable = new List<JObj>(), Parity = new List<JObj>();
        public bool Canceled;

        public RepairReport(string kind, string title) { Kind = kind; Title = title; }

        static string N(long x) => x.ToString("N0", CultureInfo.InvariantCulture);
        internal static string Range(long from, long to) => from < 0 ? "" : "bytes " + N(from) + " to " + N(to - 1);

        public void Repair(string what, string how) => Repaired.Add(new JObj { { "what", what }, { "how", how } });
        public void Skip(string what, string why) => Skipped.Add(new JObj { { "what", what }, { "why", why } });
        public void Lost(string what, string why, long from = -1, long to = -1)
        {
            var j = new JObj { { "what", what }, { "why", why } };
            if (from >= 0) { j.Set("from", from); j.Set("to", to); }
            Unrecoverable.Add(j);
        }
        public void Region(string what, string from, long start, long end, string checkedBy) => Regions.Add(new JObj { { "what", what }, { "from", from }, { "output_from", start }, { "output_to", end }, { "bytes", end - start }, { "checked_by", checkedBy } });

        /// <summary>a file that was read: what it is and how it is</summary>
        public static JObj Identity(BackupFile f, TrustedMap m, string role) => new JObj
        {
            { "role", role }, { "label", f.Label }, { "file", System.IO.Path.GetFileName(f.Path) }, { "folder", System.IO.Path.GetDirectoryName(f.Path) }, { "bytes", f.Length },
            { "modified_utc", f.ModifiedUtc.ToString("o", CultureInfo.InvariantCulture) }, { "sha256_as_read", f.Sha256 },
            { "backup_id", m?.BackupId ?? f.Manifest?.S("backup_id") ?? f.Descriptor?.BackupId }, { "console_tag", m?.Tag ?? f.Manifest?.O("console")?.S("tag") ?? f.Descriptor?.Tag },
            { "made_utc", m?.CompletedUtc ?? f.Manifest?.S("completed_utc") ?? f.Descriptor?.CompletedUtc },
            { "located_by", f.LocatedBy }, { "condition", f.Condition() },
        };

        public JObj ToJson() => new JObj
        {
            { "schema", Schema }, { "kind", Kind }, { "title", Title }, { "outcome", Outcome }, { "summary", Summary }, { "canceled", Canceled },
            { "started_utc", StartedUtc.ToString("o", CultureInfo.InvariantCulture) }, { "finished_utc", FinishedUtc == default(DateTime) ? null : FinishedUtc.ToString("o", CultureInfo.InvariantCulture) },
            { "original", Original }, { "other_files", Others }, { "output", Output },
            { "methods", Methods }, { "verified_regions", Regions }, { "repaired", Repaired }, { "skipped", Skipped }, { "unrecoverable", Unrecoverable },
            { "errors", Errors }, { "unknowns", Unknowns }, { "notes", Notes }, { "originals_after", Originals },
            { "parity", new JObj { { "piece_size", (long)RepairMap.ChunkSize }, { "damaged", Parity } } },
        };

        /// <summary>decimal units from bytes (Units.Gb), and the exact count below 1 KB</summary>
        static string Size(long b) => b < 1000 ? N(b) + (b == 1 ? " byte" : " bytes") : Units.Gb(b);
        static string Who(JObj j) => j == null ? "" : j["file"] + " (" + Size((long)j["bytes"]) + ", " + N((long)j["bytes"]) + " bytes" + (j["sha256_as_read"] != null ? ", SHA-256 " + j["sha256_as_read"] : "") + ") in " + j["folder"];

        public string ToText()
        {
            var t = new StringBuilder();
            void L(string s = "") => t.Append(s).Append("\r\n");
            L("Classic Cart Studio backup repair report"); L(Title); L();
            L("Result: " + (Summary ?? Outcome)); L();
            L("Started:  " + StartedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC");
            if (FinishedUtc != default(DateTime)) L("Finished: " + FinishedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC");
            if (Canceled) L("Canceled before it finished. Nothing it was making was kept.");
            L();
            if (Original != null)
            {
                L("The backup file: " + Who(Original));
                if (Original["backup_id"] != null) L("  Backup " + Original["backup_id"] + " of console " + Original["console_tag"] + ", made " + Original["made_utc"] + " (UTC)");
                L("  Condition: " + Original["condition"]);
            }
            foreach (var o in Others) { L(o["label"] + ": " + Who(o)); if (o["backup_id"] != null) L("  Backup " + o["backup_id"] + " of console " + o["console_tag"] + ", made " + o["made_utc"] + " (UTC)"); L("  Condition: " + o["condition"]); }
            L();
            if (Output != null)
            {
                L((Kind == "salvage" ? "New folder: " : "New file: ") + Output["path"]);
                if (Output["bytes"] != null) L("  " + Size((long)Output["bytes"]) + " (" + N((long)Output["bytes"]) + " bytes)" + (Output["sha256"] != null ? ", SHA-256 " + Output["sha256"] : ""));
                if (Output["checked"] != null) L("  " + Output["checked"]);
                if (Output["container"] != null) L("  " + Output["container"]);
            }
            else L(Kind == "salvage" ? "No folder was made." : "No new file was made.");
            if (Methods.Count > 0) { L(); L("How:"); foreach (var m in Methods) L("  - " + m); }
            if (Regions.Count > 0) { L(); L("Checked and taken (every byte below matched its recorded checksum when it was read):"); foreach (var r in Regions) L("  - " + r["what"] + ": " + Size((long)r["bytes"]) + " from " + r["from"] + " (" + r["checked_by"] + ")"); }
            if (Repaired.Count > 0) { L(); L("Repaired:"); foreach (var r in Repaired) L("  - " + r["what"] + ": " + r["how"]); }
            if (Skipped.Count > 0) { L(); L("Not used:"); foreach (var r in Skipped) L("  - " + r["what"] + ": " + r["why"]); }
            if (Unrecoverable.Count > 0) { L(); L("Could not be recovered:"); foreach (var r in Unrecoverable) L("  - " + r["what"] + (r["from"] != null ? " (" + Range((long)r["from"], (long)r["to"]) + ")" : "") + ": " + r["why"]); }
            if (Errors.Count > 0) { L(); L("Errors:"); foreach (var e in Errors) L("  - " + e); }
            if (Notes.Count > 0) { L(); foreach (var n in Notes) L(n); }
            if (Unknowns.Count > 0) { L(); L("Not known:"); foreach (var u in Unknowns) L("  - " + u); }
            if (Originals.Count > 0)
            {
                L();
                L(Originals.All(o => (bool)o["unchanged"]) ? "Every file that was read was opened for reading only, and its size and time are the same afterward."
                    : "WARNING: a file that was read changed while this ran (another program?): " + string.Join(", ", Originals.Where(o => !(bool)o["unchanged"]).Select(o => o["file"])));
            }
            return t.ToString();
        }

        /// <summary>both forms written next to what was made (never over a file); returns their paths</summary>
        public (string json, string text) Save(string folder, string stem)
        {
            Directory.CreateDirectory(folder);
            for (int i = 1; i < 1000; i++)
            {
                var b = System.IO.Path.Combine(folder, stem + (i == 1 ? "" : " (" + i + ")"));
                if (File.Exists(b + ".json") || File.Exists(b + ".txt")) continue;
                Durable.WriteAtomic(b + ".json", Json.Write(ToJson())); Durable.WriteAtomic(b + ".txt", ToText());
                return (b + ".json", b + ".txt");
            }
            throw new IOException("too many reports with the same name in " + folder);
        }
    }
}
