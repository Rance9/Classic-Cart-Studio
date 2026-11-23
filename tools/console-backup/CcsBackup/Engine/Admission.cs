// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The backup prerequisite for Classic Cart Studio's maintenance writes (preparing a console, installing, updating, repairing or removing
// its HOME, recovering an attempt). Used in two places with this one implementation: the app's backend asks for an admission before it
// starts a maintenance run (the console worker's --admit-backup mode), and the console worker checks the admission again itself before
// its first write command (Use). There is no setting, environment variable, request field or "continue anyway" that skips it.
//   Admit   finds this console's backups and VERIFIES them from scratch (Verifier): the oldest one that passes, is not simulated, names
//           this exact console and is a recovery baseline (the original kernel present and placed) is the baseline. The admission is a
//           single-use ticket for exactly one operation, subject and console, valid for 10 minutes, written to the records folder.
//   Use     the worker's own check right before writing: the ticket exists, matches the operation, subject and the console identity
//           the worker has just read, has not expired and has not been used; it names a complete baseline (and the current-state
//           evidence its operation needs), and every one of those backup files is verified again from scratch; only then is it marked
//           used (a new file, created exclusively), so it can never admit a second run. A ticket's text is never itself evidence: a
//           missing, empty, mistyped or contradictory baseline or current entry refuses the change before any write is allowed.
// Current state (NeedsCurrent: an operation that can replace or remove things made after a backup) is proven for the operation itself,
// never by a backup's age or Classic Cart Studio's own change history (normal play and other programs write without it):
//   HOME removal with data, recovery (the console worker; it writes only under /var/lib/ccs): the newest verified backup's file list for
//           Classic Cart Studio's data area (/var/lib/ccs, inventory "/ccs") must equal what the worker reads on the console right then,
//           on the same connection that will write (LiveTree): every folder, every file's SHA-256, every link. A save made since, a file
//           added or removed, a list that could not be read: refused ("back up again"), nothing written.
//   preparation (a recovery session): a complete backup captured in that same session just before (SessionCapture), bound to the
//           ticket's session; the console runs nothing else meanwhile, and a reconnection is a new session.
// The change history (NoteChange) is kept as a record and an early hint only: a recorded change newer than every backup refuses early;
// no history, or one that cannot be read, proves nothing either way.
// Ages are reported as they are: a sound original baseline does not expire, and it is never said to include saves made after it.
// DATA-ONLY admission, a separate type: a console already running a Hakchi setup whose backups hold no
// original Nintendo material (no recovery baseline) can still have Classic Cart Studio's DATA added, updated, repaired or removed with
// its NEWEST backup alone when that backup verifies from scratch as complete (restore-ready) for this exact console in its modified
// state, AND the console's data area still equals that backup at the moment of writing (LiveTree, read by the worker on the same
// connection). It never admits preparation; the firmware, bootloader, Console Rescue and return-to-original policies are other engines
// and never read these tickets. The ticket says which type it is (policy); its text names what is not possible without the original
// material: returning the console to Nintendo's original software.
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CcsBackup.Engine
{
    public sealed class AdmissionResult
    {
        public bool Ok, DataOnly; public string Code, Text, TicketId, TicketFile; public string BaselinePath, CurrentPath; public DateTime? BaselineUtc, CurrentUtc;
        public JObj ToJson() => new JObj { { "ok", Ok }, { "code", Code }, { "text", Text }, { "data_only", DataOnly }, { "ticket", TicketId }, { "baseline", BaselinePath }, { "baseline_utc", BaselineUtc?.ToString("o", CultureInfo.InvariantCulture) }, { "current", CurrentPath }, { "current_utc", CurrentUtc?.ToString("o", CultureInfo.InvariantCulture) } };
    }

    /// <summary>What the console worker read on the console just now for Classic Cart Studio's data area, in the backup inventory's
    /// terms: path ("/ccs/...") -> "d", "f:SHA-256", "l:target" or "o"; Problem when it could not be read completely.</summary>
    public sealed class LiveTree
    {
        public readonly Dictionary<string, string> Entries = new Dictionary<string, string>(StringComparer.Ordinal);
        public string Problem;
        public static LiveTree Unreadable(string why) => new LiveTree { Problem = why };
    }

    /// <summary>A complete backup captured in the preparation session itself, just before the plan (Preparation.Prepare).</summary>
    public sealed class SessionCapture
    {
        public readonly string Path, Sha256, Session;
        public SessionCapture(string path, string sha256, string session) { Path = path; Sha256 = sha256; Session = session; }
    }

    public static class Admission
    {
        public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
        public const string Schema = "ccs-maintenance-admission/2";
        public const string CcsRoot = "/ccs";   // the data partition is /var/lib on the console: /var/lib/ccs, everything the HOME worker can write
        public static readonly string[] Operations = { "prepare", "install", "update", "repair", "uninstall", "uninstall-data", "recover" };
        /// <summary>operations that can replace or remove content created after the baseline (saves, other programs' files)</summary>
        public static bool NeedsCurrent(string op) => op == "prepare" || op == "uninstall-data" || op == "recover";
        /// <summary>the operations whose current state the console worker proves on the console (LiveTree); preparation's is a capture</summary>
        public static bool NeedsLiveTree(string op) => op == "uninstall-data" || op == "recover";
        /// <summary>the operations a data-only admission can admit: Classic Cart Studio's data only (never "prepare")</summary>
        public static readonly string[] DataOnlyOperations = { "install", "update", "repair", "uninstall", "uninstall-data", "recover" };
        /// <summary>whether the worker reads the console's data list before its first write: every data operation (a data-only ticket needs
        /// it; Use decides which tickets compare it)</summary>
        public static bool MayNeedLiveTree(string op) => DataOnlyOperations.Contains(op);
        /// <summary>whether this console's backup history records a verified backup holding its original Nintendo material (a recovery
        /// baseline). A review uses it to say, before anything proceeds, that returning to Nintendo's original software is unavailable; the
        /// operations themselves check their evidence from scratch, never from this record.</summary>
        public static bool HasRecordedOriginal(Settings s, string consoleTag) => !string.IsNullOrEmpty(consoleTag) && History.List(s).Any(e => e.ConsoleTag == consoleTag && e.RecoveryBaseline && e.Status == BackupStatus.Verified);
        public const string OriginalUnavailableTitle = "Returning to Nintendo's original software is unavailable";
        public const string OriginalUnavailableText = "Workbench can back up your current Hakchi setup and add the changes you choose. It cannot return this console to Nintendo's original software without the original material from your own backup.";

        static string ChangesFile(Settings s, string tag) => Path.Combine(s.RecordsFolder, "console-" + tag + "-changes.json");
        static readonly Regex TicketRe = new Regex("^[0-9a-f]{32}$"), ShaRe = new Regex("^[0-9a-f]{64}$");

        /// <summary>records that a maintenance write began on this console (before its first write): a record and an early hint only</summary>
        public static void NoteChange(Settings s, string hwid, string op)
        {
            var tag = ConsoleIdentity.HwidTag(hwid); var f = ChangesFile(s, tag);
            var d = File.Exists(f) ? Record.Load(f) : null; var list = d?.A("changes") ?? new List<object>();
            list.Add(new JObj { { "operation", op }, { "at", Record.Now() } });
            Durable.WriteAtomic(f, Json.Write(new JObj { { "schema", "ccs-console-changes/1" }, { "console_tag", tag }, { "changes", list.Skip(Math.Max(0, list.Count - 200)).ToList() } }));
        }

        /// <summary>the newest recorded change: null when there is no history or it cannot be read (neither proves "no change")</summary>
        static DateTime? LastChange(Settings s, string tag)
        {
            var d = Record.Load(ChangesFile(s, tag)); if (d == null) return null;
            DateTime? best = null;
            foreach (var c in (d.A("changes") ?? new List<object>()).OfType<Dictionary<string, object>>())
                if (DateTime.TryParse(c.S("at"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) && (best == null || t > best)) best = t;
            return best;
        }

        static bool Utc(string s, out DateTime t) => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t);
        static string When(DateTime? t) => t?.ToLocalTime().ToString("MMMM d, yyyy h:mm tt", CultureInfo.GetCultureInfo("en-US"));
        static JObj Ref(string path, Verification v, DateTime? when) => new JObj { { "path", path }, { "sha256", v.ArchiveSha256 }, { "backup_id", v.Manifest?.S("backup_id") }, { "completed_utc", when?.ToString("o", CultureInfo.InvariantCulture) } };

        /// <summary>session: the recovery session preparation runs in (its current state is captured there; null for the HOME worker's operations)</summary>
        /// <summary>preview: the same checks and the same answer (which type, its text), but no ticket is written (a review shows it before
        /// anything proceeds; the change itself always asks again)</summary>
        public static AdmissionResult Admit(Settings s, string hwid, string op, string subject, Func<bool> cancelled = null, bool simulatedConsole = false, string session = null, bool preview = false)
        {
            var r = new AdmissionResult();
            if (!Operations.Contains(op)) return Refuse(r, "bad-operation", "Unknown maintenance operation.");
            if (string.IsNullOrEmpty(hwid)) return Refuse(r, "no-console", "Check the console first.");
            if (op == "prepare" && string.IsNullOrEmpty(session)) return Refuse(r, "bad-operation", "Preparation is admitted only inside its own console session.");
            var tag = ConsoleIdentity.HwidTag(hwid);
            var candidates = History.List(s).Where(e => e.ConsoleTag == tag && e.Simulated == simulatedConsole && (e.PrimaryPath != null || e.ArchivePath != null)).ToList();
            if (candidates.Count == 0) return Refuse(r, "backup-required", "Back up this console first: Back Up Console makes a complete backup of its built-in storage, and changes are only made to a console that has one.");
            Verification baseline = null, current = null; string bPath = null, cPath = null; DateTime? bWhen = null, cWhen = null;
            Verification Check(HistoryEntry e, out string path, out DateTime when)
            {
                path = null; when = DateTime.MinValue;
                foreach (var p in new[] { e.PrimaryPath, e.ArchivePath }.Where(x => x != null && File.Exists(x)))
                {
                    var v = Verifier.VerifyFile(p, null, cancelled);
                    if (!v.Ok || v.Simulated != simulatedConsole || !ConsoleIdentity.SameConsole(v.Hwid, hwid)) continue;
                    Utc(v.Manifest.S("completed_utc"), out when);
                    path = p; return v;
                }
                return null;
            }
            // the baseline: the OLDEST backup that verifies as a complete recovery baseline (the console's original state, kept immutable)
            foreach (var e in candidates.OrderBy(x => x.CreatedUtc ?? DateTime.MaxValue))
            {
                var v = Check(e, out var p, out var w);
                if (v != null && v.RecoveryBaseline) { baseline = v; bPath = p; bWhen = w; break; }
            }
            bool dataOnly = false;
            if (baseline == null)
            {
                if (!DataOnlyOperations.Contains(op)) return Refuse(r, "backup-required", "None of this console's backups passed its check as a complete recovery baseline (with the original Nintendo kernel). Make a new backup, then try again.");
                // data-only: only the NEWEST backup can stand for the console now (an older one says nothing about later changes)
                var newest = candidates.OrderByDescending(x => x.CreatedUtc ?? DateTime.MinValue).First();
                var v = Check(newest, out var p, out var w);
                if (v == null || !v.RestoreReady || v.StateKind != "modified")
                    return Refuse(r, "current-backup-required", "This console's backups hold no original Nintendo material, so Classic Cart Studio's data can be added only with a complete backup of the console as it is now. Back up the console now, then try again.");
                current = v; cPath = p; cWhen = w; dataOnly = true;
                var last0 = LastChange(s, tag);   // an early hint only
                if (last0 != null && cWhen != null && last0.Value > cWhen.Value) return Refuse(r, "current-backup-required", "Classic Cart Studio changed this console after your newest backup. Back up the console again now, then try again.");
            }
            // the current state's backup for the HOME worker's removal and recovery: the NEWEST verified backup; whether it still matches
            // the console is checked on the console when the worker is about to write (Use, LiveTree)
            if (NeedsLiveTree(op) && !dataOnly)
            {
                foreach (var e in candidates.OrderByDescending(x => x.CreatedUtc ?? DateTime.MinValue))
                {
                    var v = Check(e, out var p, out var w);
                    if (v != null) { current = v; cPath = p; cWhen = w; break; }
                }
                if (current == null) return Refuse(r, "current-backup-required", "This change can replace or remove things made since your last backup. Back up the console again now, then try again.");
                var last = LastChange(s, tag);   // an early hint only (no history proves nothing)
                if (last != null && cWhen != null && last.Value > cWhen.Value) return Refuse(r, "current-backup-required", "Classic Cart Studio changed this console after your newest backup. Back up the console again now, then try again.");
            }
            r.Ok = true; r.Code = dataOnly ? "admitted-data-only" : "admitted"; r.DataOnly = dataOnly; r.BaselinePath = bPath; r.BaselineUtc = bWhen; r.CurrentPath = cPath; r.CurrentUtc = cWhen;
            if (preview)
            {
                r.Text = dataOnly ? OriginalUnavailableTitle + ": " + OriginalUnavailableText : "Your backups include this console's original Nintendo material.";
                return r;
            }
            r.TicketId = Guid.NewGuid().ToString("N");
            r.TicketFile = Path.Combine(s.RecordsFolder, "admission-" + r.TicketId + ".json");
            var ticket = new JObj
            {
                { "schema", Schema }, { "ticket", r.TicketId }, { "operation", op }, { "subject", subject ?? "" }, { "hwid", hwid }, { "simulated", simulatedConsole },
                { "issued_utc", Record.Now() }, { "expires_utc", (DateTime.UtcNow + Ttl).ToString("o", CultureInfo.InvariantCulture) },
                { "policy", dataOnly ? "data-only-current" : "recovery-baseline" },
                { "baseline", dataOnly ? null : (object)Ref(bPath, baseline, bWhen) },
                { "current", NeedsLiveTree(op) || dataOnly ? (object)Ref(cPath, current, cWhen) : null },
                { "current_proof", op == "prepare" ? "session-capture" : NeedsLiveTree(op) || dataOnly ? "console-data-list" : "not-needed" },
                { "session", op == "prepare" ? session : null },
            };
            Durable.WriteAtomic(r.TicketFile, Json.Write(ticket));
            if (dataOnly) { r.Text = "Admitted with your complete backup of " + When(cWhen) + " of the console as it is now, checked against the console again before anything is written. " + OriginalUnavailableTitle + ": " + OriginalUnavailableText; return r; }
            r.Text = "Admitted with the verified backup of " + When(bWhen) + (cPath != null ? " and the backup of " + When(cWhen) + ", checked against the console again before anything is written" : op == "prepare" ? "; the console's current state is backed up in this session before anything is written" : "") + ".";
            return r;
        }

        /// <summary>The worker's check before its first write, without current-state evidence: enough for install, update, repair and
        /// keep-data removal; an operation that needs the console's current state is refused (no proof was given).</summary>
        public static AdmissionResult Use(Settings s, string ticketId, string op, string subject, string freshHwid, Func<bool> cancelled = null, bool simulatedConsole = false)
            => Use(s, ticketId, op, subject, freshHwid, cancelled, simulatedConsole, null, null);

        /// <summary>The worker's check before its first write. freshHwid: the identity the worker has just read from the console; live: what
        /// the console worker read in Classic Cart Studio's data area just now (HOME removal with data, recovery); capture: the backup made
        /// in the preparation session (preparation).</summary>
        public static AdmissionResult Use(Settings s, string ticketId, string op, string subject, string freshHwid, Func<bool> cancelled, bool simulatedConsole, LiveTree live, SessionCapture capture)
        {
            var r = new AdmissionResult();
            if (string.IsNullOrEmpty(ticketId) || !TicketRe.IsMatch(ticketId)) return Refuse(r, "backup-required", "No backup admission was given for this change.");
            var f = Path.Combine(s.RecordsFolder, "admission-" + ticketId + ".json"); var d = Record.Load(f);
            if (d == null || d.S("ticket") != ticketId) return Refuse(r, "backup-required", "The backup admission was not found.");
            if (d.S("schema") != Schema) return Refuse(r, "backup-required", "The backup admission was made by an earlier version. Check the console again.");
            if (d.S("operation") != op || (d.S("subject") ?? "") != (subject ?? "")) return Refuse(r, "admission-mismatch", "The backup admission was made for a different change.");
            if (!ConsoleIdentity.SameConsole(d.S("hwid"), freshHwid)) return Refuse(r, "wrong-console", "The backup admission was made for a different console.");
            if (!Utc(d.S("expires_utc"), out var exp) || DateTime.UtcNow > exp || !Utc(d.S("issued_utc"), out var issued) || issued > DateTime.UtcNow.AddMinutes(1) || exp - issued > Ttl + TimeSpan.FromMinutes(1))
                return Refuse(r, "admission-expired", "The backup admission is more than 10 minutes old. Check the console again.");
            var used = Path.Combine(s.RecordsFolder, "admission-" + ticketId + ".used");
            if (File.Exists(used)) return Refuse(r, "admission-used", "This backup admission was already used.");
            if (!(d.TryGetValue("simulated", out var sim) && sim is bool sb && sb == simulatedConsole)) return Refuse(r, "backup-changed", "The backup admission does not say whether it is for a simulated console.");
            // the ticket's type: a recovery baseline (required for every change, complete, verified again from scratch) or data-only
            var policy = d.S("policy") ?? "recovery-baseline";
            if (policy != "recovery-baseline" && policy != "data-only-current") return Refuse(r, "backup-required", "The backup admission is of an unknown type. Nothing was changed.");
            bool dataOnly = policy == "data-only-current";
            if (dataOnly && !DataOnlyOperations.Contains(op)) return Refuse(r, "backup-required", "A data-only admission cannot admit this change. Nothing was changed.");
            Verification bv = null; string bPath = null; DateTime? bWhen = null;
            if (!dataOnly)
            {
                string bWhy; (bv, bPath, bWhen, bWhy) = Reverify(d, "baseline", freshHwid, simulatedConsole, cancelled);
                if (bWhy != null) return Refuse(r, bv == null && bPath == null ? "backup-required" : "backup-changed", bWhy);
                if (!bv.RecoveryBaseline) return Refuse(r, "backup-changed", "The backup this change relies on is not a complete recovery baseline. Nothing was changed.");
            }
            else if (d.TryGetValue("baseline", out var bo) && bo != null) return Refuse(r, "backup-changed", "A data-only admission names a baseline. Nothing was changed.");
            string cPath = null; DateTime? cWhen = null;
            if (NeedsLiveTree(op) || dataOnly)
            {
                if (d.S("current_proof") != "console-data-list") return Refuse(r, "current-backup-required", "The backup admission does not say how the console's current state is checked. Check the console again.");
                var (cv, cp, cw, cWhy) = Reverify(d, "current", freshHwid, simulatedConsole, cancelled);
                if (cWhy != null) return Refuse(r, cv == null && cp == null ? "current-backup-required" : "backup-changed", cWhy.Replace("baseline", "current-state"));
                if (dataOnly && (!cv.RestoreReady || cv.StateKind != "modified")) return Refuse(r, "backup-changed", "The backup this data-only change relies on is not a complete backup of the console as it is now. Nothing was changed.");
                if (live == null) return Refuse(r, "current-unchecked", "The console's Classic Cart Studio data could not be compared with your newest backup, so nothing was changed.");
                if (live.Problem != null) return Refuse(r, "current-unchecked", "The console's Classic Cart Studio data could not be read completely (" + live.Problem + "), so it could not be compared with your newest backup. Nothing was changed.");
                var why = SameCcsData(cp, live);
                if (why != null) return Refuse(r, "current-backup-required", "The console changed since your newest backup (" + why + "). Back up the console again, then try again. Nothing was changed.");
                cPath = cp; cWhen = cw;
            }
            else if (op == "prepare")
            {
                if (d.S("current_proof") != "session-capture" || string.IsNullOrEmpty(d.S("session"))) return Refuse(r, "current-backup-required", "The preparation admission does not name its session. Check the console again.");
                if (capture == null || capture.Session != d.S("session")) return Refuse(r, "current-backup-required", "The console's current state was not backed up in this session, so nothing was changed.");
                if (string.IsNullOrEmpty(capture.Path) || capture.Path == bPath || !ShaRe.IsMatch(capture.Sha256 ?? "")) return Refuse(r, "current-backup-required", "The backup of the console's current state is not a separate, complete backup. Nothing was changed.");
                var cv = Verifier.VerifyFile(capture.Path, null, cancelled);
                if (!cv.Ok || cv.ArchiveSha256 != capture.Sha256 || !ConsoleIdentity.SameConsole(cv.Hwid, freshHwid) || cv.Simulated != simulatedConsole) return Refuse(r, "backup-changed", "The backup of the console's current state is missing, changed or no longer passes its check. Nothing was changed.");
                if (!Utc(cv.Manifest.S("completed_utc"), out var cw) || cw < issued.AddSeconds(-1)) return Refuse(r, "current-backup-required", "The backup of the console's current state was not made in this session. Nothing was changed.");
                cPath = capture.Path; cWhen = cw;
            }
            try { using (new FileStream(used, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16, FileOptions.WriteThrough)) { } }
            catch (IOException) { return Refuse(r, "admission-used", "This backup admission was already used."); }
            NoteChange(s, freshHwid, op);
            r.Ok = true; r.Code = dataOnly ? "admitted-data-only" : "admitted"; r.DataOnly = dataOnly; r.TicketId = ticketId; r.BaselinePath = bPath; r.BaselineUtc = bWhen; r.CurrentPath = cPath; r.CurrentUtc = cWhen;
            r.Text = "The backup prerequisite was checked again by the console worker.";
            return r;
        }

        /// <summary>one required backup entry of a ticket, complete and verified again: (verification, path, completed, null) or a reason</summary>
        static (Verification v, string path, DateTime? when, string why) Reverify(Dictionary<string, object> d, string key, string hwid, bool simulated, Func<bool> cancelled)
        {
            var name = key == "baseline" ? "baseline" : "current-state";
            if (!d.TryGetValue(key, out var o) || !(o is Dictionary<string, object> b)) return (null, null, null, "The backup admission does not name a " + name + " backup. Nothing was changed.");
            var path = b.TryGetValue("path", out var po) ? po as string : null; var sha = b.TryGetValue("sha256", out var so) ? so as string : null; var at = b.TryGetValue("completed_utc", out var ao) ? ao as string : null;
            if (string.IsNullOrEmpty(path) || !System.IO.Path.IsPathRooted(path) || sha == null || !ShaRe.IsMatch(sha) || at == null || !Utc(at, out var named))
                return (null, null, null, "The backup admission's " + name + " entry is incomplete. Nothing was changed.");
            if (!File.Exists(path)) return (null, path, null, "The " + name + " backup this change relies on is missing. Nothing was changed.");
            var v = Verifier.VerifyFile(path, null, cancelled);
            if (!v.Ok || v.ArchiveSha256 != sha || !ConsoleIdentity.SameConsole(v.Hwid, hwid) || v.Simulated != simulated) return (v, path, null, "The " + name + " backup this change relies on is missing, changed or no longer passes its check. Nothing was changed.");
            if (!Utc(v.Manifest.S("completed_utc"), out var made) || Math.Abs((made - named).TotalSeconds) > 1) return (v, path, null, "The backup admission's " + name + " entry does not match its backup file. Nothing was changed.");
            return (v, path, made, null);
        }

        /// <summary>null when the backup's list of /ccs equals the live list exactly; otherwise the first difference, in plain words</summary>
        public static string SameCcsData(string backup, LiveTree live)
        {
            if (live.Problem != null) return "its Classic Cart Studio data could not be read completely: " + live.Problem;
            var saved = CcsDataOf(backup, out var problem);
            if (saved == null) return "the backup's file list could not be read" + (problem != null ? ": " + problem : "");
            foreach (var kv in live.Entries.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (!saved.TryGetValue(kv.Key, out var was)) return "/var/lib" + kv.Key + " was added";
                if (was != kv.Value) return "/var/lib" + kv.Key + " is different";
            }
            foreach (var kv in saved.OrderBy(x => x.Key, StringComparer.Ordinal)) if (!live.Entries.ContainsKey(kv.Key)) return "/var/lib" + kv.Key + " was removed";
            return null;
        }

        /// <summary>a backup's inventory entries for Classic Cart Studio's data area: path -> "d", "f:SHA-256", "l:target", "o" (null: unreadable)</summary>
        public static Dictionary<string, string> CcsDataOf(string backup, out string problem)
        {
            problem = null;
            try
            {
                using (var z = ZipReader.Open(backup, BackupFormat.Allowed))
                {
                    var e = z.Find(BackupFormat.InventoryName); if (e == null) { problem = "no file list"; return null; }
                    Dictionary<string, object> d; using (var st = z.OpenMember(e)) using (var rd = new StreamReader(st)) d = Json.Parse(rd.ReadToEnd());
                    if (d.O("summary")?.TryGetValue("readable", out var ro) == true && ro is bool rb && !rb) { problem = "the data storage was not readable as a file system"; return null; }
                    var map = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var x in (d.A("entries") ?? new List<object>()).OfType<Dictionary<string, object>>())
                    {
                        var p = x.S("path"); if (p == null || !(p == CcsRoot || p.StartsWith(CcsRoot + "/", StringComparison.Ordinal))) continue;
                        var t = x.S("type");
                        map[p] = t == "d" ? "d" : t == "l" ? "l:" + (x.S("target") ?? "") : t == "f" ? "f:" + (x.S("sha256") ?? "?") : "o";
                    }
                    return map;
                }
            }
            catch (Exception ex) { problem = ex.Message; return null; }
        }

        static AdmissionResult Refuse(AdmissionResult r, string code, string text) { r.Ok = false; r.Code = code; r.Text = text; return r; }
    }
}
