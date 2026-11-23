// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Where backups go and what is remembered about them.
//   Settings     the two destinations (primary, and the second copy or none), in %LOCALAPPDATA%\CCS Console Backup\settings.json,
//                shared by Classic Cart Studio and the Utility; defaults: Documents\CCS Console Backups and its "Second Copy" folder
//                (and the earlier CCS HOME versions to keep on the console, earlier_home_versions: one unless it is set, 0 to 9)
//   Record       one durable JSON file per operation in <primary>\.ccs-records\ (WriteAtomic each time its state moves on): what was
//                started, what each step found, where the files are. A record is history, never authority: nothing is trusted because a
//                record says "verified" (every use checks the file again; Verifier).
//   Library      the history: every backup file, unfinished file and record in the two folders, each with an honest status.
using CcsBackup.Archive;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CcsBackup.Library
{
    public sealed class Settings
    {
        public string PrimaryFolder, ArchiveFolder;   // ArchiveFolder null or empty: one copy only
        public string Path;
        /// <summary>the standalone Workbench's ordinary preferences: kept in memory unless the person chooses a settings file of their own
        /// (then Path is that file; the window remembers which file, LastSettingsFile)</summary>
        public bool InMemory;
        /// <summary>the console family the Workbench window last used (its profile id; null: the window's default). A window preference
        /// only: never a console's identity, firmware or an approval.</summary>
        public string ConsoleProfile;
        /// <summary>how many earlier CCS HOME versions the console keeps when CCS HOME is updated (Settings > CCS HOME > Earlier HOME
        /// versions); null: the default. The console's own backups on this PC are kept apart and are never affected.</summary>
        public int? EarlierHomeVersions;
        /// <summary>the number in force: the setting when it is a valid number, otherwise the storage specification's default (one)</summary>
        public int EarlierHomeKeep => Engine.StorageSpec.EarlierKeep(EarlierHomeVersions.HasValue ? (object)(long)EarlierHomeVersions.Value : null);
        /// <summary>fields a settings file holds that this version does not use (a newer version's): kept as they are and written back</summary>
        readonly List<KeyValuePair<string, object>> other = new List<KeyValuePair<string, object>>();
        static readonly string[] Known = { "schema", "primary_folder", "archive_folder", "console_profile", "earlier_home_versions" };
        public static Settings Portable(string primary, string archive) => new Settings { InMemory = true, PrimaryFolder = string.IsNullOrWhiteSpace(primary) || !System.IO.Path.IsPathRooted(primary) ? DefaultPrimary : primary, ArchiveFolder = string.IsNullOrWhiteSpace(archive) || !System.IO.Path.IsPathRooted(archive) ? null : archive };
        public static string DefaultPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCS Console Backup", "settings.json");
        public static string DefaultPrimary => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CCS Console Backups");

        public static Settings Load(string path = null)
        {
            path = path ?? DefaultPath;
            var s = new Settings { Path = path, PrimaryFolder = DefaultPrimary, ArchiveFolder = System.IO.Path.Combine(DefaultPrimary, "Second Copy") };
            try
            {
                if (File.Exists(path))
                {
                    var d = Json.Parse(File.ReadAllText(path));
                    var p = d.S("primary_folder"); if (!string.IsNullOrWhiteSpace(p) && System.IO.Path.IsPathRooted(p)) s.PrimaryFolder = p;
                    if (d.ContainsKey("archive_folder")) { var a = d.S("archive_folder"); s.ArchiveFolder = string.IsNullOrWhiteSpace(a) || !System.IO.Path.IsPathRooted(a) ? null : a; }
                    var cp = d.S("console_profile"); if (!string.IsNullOrWhiteSpace(cp)) s.ConsoleProfile = cp;
                    var ev = d.L("earlier_home_versions", -1); if (ev >= 0 && ev == Engine.StorageSpec.EarlierKeep(ev)) s.EarlierHomeVersions = (int)ev;
                    foreach (var kv in d) if (Array.IndexOf(Known, kv.Key) < 0) s.other.Add(kv);
                }
            }
            catch (Exception) { }
            return s;
        }

        public void Save() { if (InMemory) return; Write(Path); }
        /// <summary>writes these settings to a file (the same schema; a newer version's fields kept)</summary>
        public void Write(string file)
        {
            var j = new JObj { { "schema", "ccs-console-backup-settings/1" }, { "primary_folder", PrimaryFolder }, { "archive_folder", ArchiveFolder ?? "" } };
            if (!string.IsNullOrEmpty(ConsoleProfile)) j.Add("console_profile", ConsoleProfile);
            if (EarlierHomeVersions.HasValue) j.Add("earlier_home_versions", (long)EarlierHomeVersions.Value);
            foreach (var kv in other) j.Add(kv.Key, kv.Value);
            Durable.WriteAtomic(file, Json.Write(j));
        }
        /// <summary>a copy (the same file, values and kept fields)</summary>
        public Settings Copy() { var c = new Settings { PrimaryFolder = PrimaryFolder, ArchiveFolder = ArchiveFolder, Path = Path, InMemory = InMemory, ConsoleProfile = ConsoleProfile, EarlierHomeVersions = EarlierHomeVersions }; c.other.AddRange(other); return c; }
        public string RecordsFolder => System.IO.Path.Combine(PrimaryFolder, ".ccs-records");
    }

    public sealed class Record
    {
        public readonly string File; public readonly JObj Data;
        Record(string file, JObj data) { File = file; Data = data; }
        public static Record Start(string folder, string id, string kind, JObj fields)
        {
            Directory.CreateDirectory(folder);
            try { new DirectoryInfo(folder).Attributes |= FileAttributes.Hidden; } catch { }
            var file = Path.Combine(folder, kind + "-" + id + ".json");
            if (System.IO.File.Exists(file)) throw new IOException("a record for this operation already exists");
            var d = new JObj { { "schema", "ccs-console-backup-record/1" }, { "id", id }, { "kind", kind }, { "state", "started" }, { "created_utc", Now() }, { "updated_utc", Now() } };
            foreach (var kv in fields ?? new JObj()) d.Set(kv.Key, kv.Value);
            var r = new Record(file, d); r.Save(); return r;
        }
        public static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        public Record Set(string k, object v) { Data.Set(k, v); return this; }
        public void State(string state, string problem = null) { Data.Set("state", state); if (problem != null) Data.Set("problem", problem); Save(); }
        public void Save() { Data.Set("updated_utc", Now()); Durable.WriteAtomic(File, Json.Write(Data)); }
        public static Dictionary<string, object> Load(string file) { try { return Json.Parse(System.IO.File.ReadAllText(file)); } catch { return null; } }
    }

    public enum BackupStatus { Verified, NeedsVerification, Changed, Incomplete, Damaged, Missing }

    public sealed class HistoryEntry
    {
        public string Id, ConsoleTag, PrimaryPath, ArchivePath, PartialPath, StateText, Kind, Problem; public DateTime? CreatedUtc, VerifiedUtc;
        public BackupStatus Status; public long Bytes; public bool Simulated, RestoreReady, RecoveryBaseline, TwoCopies, SameDisk;
        public Dictionary<string, object> RecordData;
        public string When => CreatedUtc.HasValue ? CreatedUtc.Value.ToLocalTime().ToString("dddd, MMMM d, yyyy 'at' h:mm tt", CultureInfo.GetCultureInfo("en-US")) : "Unknown time";
        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case BackupStatus.Verified: return TwoCopies ? "Verified, two copies" : "Verified, one copy";
                    case BackupStatus.NeedsVerification: return "Needs verification";
                    case BackupStatus.Changed: return "Changed since it was checked";
                    case BackupStatus.Incomplete: return "Incomplete";
                    case BackupStatus.Damaged: return "Damaged";
                    default: return "File not found";
                }
            }
        }
    }

    public static class History
    {
        /// <summary>Everything in the two folders, newest first. Reads only file names, sizes, times, the records and each backup's small
        /// manifest; never treats any of them as a verified result (Verified needs a complete record AND the file unchanged since).</summary>
        public static List<HistoryEntry> List(Settings s)
        {
            var byId = new Dictionary<string, HistoryEntry>();
            var ownPartials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // unfinished files a record accounts for (its own, or its second copy's)
            HistoryEntry E(string id) { if (!byId.TryGetValue(id, out var e)) byId[id] = e = new HistoryEntry { Id = id, Status = BackupStatus.NeedsVerification }; return e; }
            if (Directory.Exists(s.RecordsFolder))
                foreach (var f in Directory.GetFiles(s.RecordsFolder, "backup-*.json"))
                {
                    var d = Record.Load(f); if (d == null || d.S("id") == null) continue;
                    var e = E(d.S("id")); e.RecordData = d; e.ConsoleTag = d.S("console_tag"); e.Simulated = d.B("simulated");
                    if (DateTime.TryParse(d.S("created_utc"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var c)) e.CreatedUtc = c;
                    var prim = d.O("primary"); e.PrimaryPath = prim?.S("final"); e.PartialPath = prim?.S("partial");
                    e.ArchivePath = d.O("archive")?.S("final"); e.Kind = d.S("state_text"); e.Problem = d.S("problem");
                    foreach (var pp in new[] { e.PartialPath, d.O("archive")?.S("partial") }) if (pp != null) ownPartials.Add(pp);
                    e.RestoreReady = d.B("restore_ready"); e.RecoveryBaseline = d.B("recovery_baseline");
                    if (DateTime.TryParse(prim?.S("verified_utc"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var v)) e.VerifiedUtc = v;
                    var state = d.S("state");
                    bool finalExists = e.PrimaryPath != null && File.Exists(e.PrimaryPath);
                    switch (state)
                    {
                        case "complete":
                        case "complete-one-copy":
                            if (!finalExists) { e.Status = BackupStatus.Missing; break; }
                            var fi = new FileInfo(e.PrimaryPath);
                            e.Bytes = fi.Length;
                            bool same = fi.Length == prim.L("bytes") && fi.LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture) == prim.S("mtime_utc");
                            e.Status = same ? BackupStatus.Verified : BackupStatus.Changed;
                            e.TwoCopies = state == "complete" && e.ArchivePath != null && File.Exists(e.ArchivePath);
                            e.SameDisk = d.O("archive")?.B("same_disk") ?? false;
                            break;
                        case "damaged": e.Status = BackupStatus.Damaged; break;
                        case "published":
                        case "copying":
                            e.Status = finalExists ? BackupStatus.NeedsVerification : BackupStatus.Incomplete; break;
                        default:   // started, reading, captured, checking, checked, canceled, stopped
                            e.Status = finalExists ? BackupStatus.NeedsVerification : BackupStatus.Incomplete; break;
                    }
                    e.StateText = state;
                }
            foreach (var folder in new[] { s.PrimaryFolder, s.ArchiveFolder }.Where(x => !string.IsNullOrEmpty(x) && Directory.Exists(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var f in Directory.GetFiles(folder, "*" + BackupFormat.Partial))
                {
                    if (ownPartials.Contains(f)) continue;
                    var e = E("partial:" + f); e.PartialPath = f; e.Status = BackupStatus.Incomplete; e.Bytes = new FileInfo(f).Length; e.CreatedUtc = File.GetCreationTimeUtc(f);
                }
                foreach (var f in Directory.GetFiles(folder, "*" + BackupFormat.Extension))
                {
                    if (byId.Values.Any(e => string.Equals(e.PrimaryPath, f, StringComparison.OrdinalIgnoreCase) || string.Equals(e.ArchivePath, f, StringComparison.OrdinalIgnoreCase))) continue;
                    var m = Peek(f);
                    var id = m?.S("backup_id") ?? ("file:" + f);
                    var e = E(id);
                    if (e.PrimaryPath == null) e.PrimaryPath = f; else if (e.ArchivePath == null) e.ArchivePath = f;
                    e.Bytes = new FileInfo(f).Length; e.Status = e.RecordData == null ? BackupStatus.NeedsVerification : e.Status;
                    if (m != null) { e.ConsoleTag = m.O("console")?.S("tag"); e.Simulated = m.B("simulated"); e.Kind = m.O("state")?.S("text"); if (DateTime.TryParse(m.S("completed_utc"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var c)) e.CreatedUtc = c; }
                    else { e.CreatedUtc = File.GetLastWriteTimeUtc(f); e.Problem = "This file is not a readable console backup."; e.Status = BackupStatus.Damaged; }
                }
            }
            return byId.Values.OrderByDescending(e => e.CreatedUtc ?? DateTime.MinValue).ToList();
        }

        /// <summary>the manifest of a backup file, read through the strict reader (structure only; nothing is trusted from it)</summary>
        public static Dictionary<string, object> Peek(string file)
        {
            try
            {
                using (var z = ZipReader.Open(file, BackupFormat.Allowed))
                {
                    var e = z.Find(BackupFormat.ManifestName); if (e == null || e.Size > BackupFormat.MaxSize(e.Name)) return null;
                    using (var st = z.OpenMember(e)) using (var r = new StreamReader(st)) return Json.Parse(r.ReadToEnd());
                }
            }
            catch (Exception) { return null; }
        }
    }
}
