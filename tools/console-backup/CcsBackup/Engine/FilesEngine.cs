// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Expert Mode (inside Console Browser) and Developer Mode file changes on the console's data storage, in the files image (ram/ccsfiles),
// under the SESSION RESTORE POINT policy (RestorePoint.cs), never under a strict operation's admission:
//   scope "user"  Expert Mode: user files only: clover/profiles/ (Nintendo HOME's saves and settings), hakchi/games/ (games added with
//                 hakchi: game files and artwork), ccs/data/ (Classic Cart Studio's saves and settings). Its own boundary Gate.ExpertWrites.
//   scope "dev"   Developer Mode: any regular file on the data storage, including hakchi's system overlay (hakchi/rootfs/); never a link,
//                 a folder, a device, or anything outside the data storage. Its own boundary Gate.DeveloperWrites. Kernel and system images,
//                 and the RAM boot of a developer image, are separate typed operations; custom boot0/boot1/partition-table writes do not
//                 exist in this version.
//   plan          the restore point checked (this console, this connection, verified again, covers user files); each change's path checked
//                 here and again on the console; the console's current state of each file read (stat); a one-time confirmation for exactly
//                 these changes. No fresh full backup per change: later changes are knowingly unprotected, as the restore point's notice says.
//   execute       the restore point verified again and held read-only; the operation record saved first (no record, no change); each change
//                 once: staged in RAM, the file checked to be what the review saw, written to a new name, synced, renamed, synced, read back
//                 (a deletion: removed, synced, checked absent, recorded as a tombstone); the first failure stops every later change.
// Copying a game file does not by itself add it to a console menu: a menu entry needs its own data, which this mode does not invent.
using CcsBackup.Device;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsBackup.Engine
{
    public sealed class FileChange { public string Op, Path; public byte[] Data; public static FileChange Put(string path, byte[] data) => new FileChange { Op = "put", Path = path, Data = data }; public static FileChange Delete(string path) => new FileChange { Op = "del", Path = path }; }

    public sealed class PlannedFileChange { public string Op, Path, NewSha256, OldState; public long Bytes, OldBytes; public byte[] Data; }

    public sealed class FilePlan
    {
        public string Scope, Hwid, ConsoleTag, Session, Digest, Nonce; public DateTime ExpiresUtc; public bool Simulated; public SessionRestorePoint Point;
        public List<PlannedFileChange> Changes = new List<PlannedFileChange>(); public List<string> Notes = new List<string>();
        public string[] Targets => Changes.Select(c => c.Op + ":" + c.Path).ToArray();
        public JObj ToJson() => new JObj
        {
            { "scope", Scope }, { "console_tag", ConsoleTag }, { "restore_point", Point?.ToJson() }, { "notice", SessionRestorePoint.NoticeText },
            { "changes", Changes.Select(c => (object)new JObj { { "op", c.Op }, { "path", c.Path }, { "new_sha256", c.NewSha256 }, { "bytes", c.Bytes }, { "old", c.OldState }, { "old_bytes", c.OldBytes } }).ToList() },
            { "notes", Notes }, { "digest", Digest }, { "nonce", Nonce }, { "expires_utc", ExpiresUtc.ToString("o", CultureInfo.InvariantCulture) },
        };
    }

    public sealed class FileOutcome { public string State, Problem, RecordFile; public List<string> Done = new List<string>(); public bool Ok => State == "changed"; }

    public static class FilesEngine
    {
        public const long MaxFileBytes = 256L * 1000 * 1000;
        public static readonly string[] UserRoots = { "clover/profiles/", "hakchi/games/", "ccs/data/" };
        static readonly Regex PathRe = new Regex("^[A-Za-z0-9._+() -]+(/[A-Za-z0-9._+() -]+)*$");

        /// <summary>the same path rule ccsfiles applies on the console: relative to the data storage, plain names, no "." or ".." parts</summary>
        public static bool PathOk(string p) => p != null && p.Length <= 1000 && PathRe.IsMatch(p) && !("/" + p + "/").Contains("/../") && !("/" + p + "/").Contains("/./") && !p.EndsWith(".ccs-new", StringComparison.Ordinal);
        public static readonly string[] ComposeRoots = { "clover/profiles/", "hakchi/games/", "ccs/" };
        public static bool InScope(string scope, string p) => scope == "dev" || scope == "user" && UserRoots.Any(r => p.StartsWith(r, StringComparison.Ordinal) && p.Length > r.Length)
            || scope == "compose" && ComposeRoots.Any(r => p.StartsWith(r, StringComparison.Ordinal) && p.Length > r.Length);
        public static string Quote(string p) => "'" + p + "'";
        static string Sha(byte[] b) { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(b)); }

        public static (FilePlan plan, string why) Plan(RecoveryConsole con, SessionRestorePoint point, string scope, IList<FileChange> changes, Func<bool> cancelled)
        {
            if (con.Profile != SessionProfile.Files) return (null, "The console is not in a file session.");
            if (scope != "user" && scope != "dev") return (null, "Unknown mode.");
            var rp = RestorePoints.Require(point, con, RestorePoints.UserFiles, cancelled);
            if (rp != null) return (null, rp);
            if (changes == null || changes.Count == 0) return (null, "No changes were chosen.");
            if (changes.Select(c => c.Path).Distinct(StringComparer.Ordinal).Count() != changes.Count) return (null, "A file appears more than once in the changes.");
            var p = new FilePlan { Scope = scope, Hwid = point.Hwid, ConsoleTag = point.ConsoleTag, Session = con.SessionId, Simulated = con.Simulated, Point = point };
            foreach (var c in changes)
            {
                if (!PathOk(c.Path)) return (null, "\"" + c.Path + "\" is not a file name this mode accepts (plain names inside the console's data storage).");
                if (!InScope(scope, c.Path)) return (null, "\"" + c.Path + "\" is not a user file (games, saves, artwork and settings). Developer Mode is a separate area.");
                if (c.Op != "put" && c.Op != "del") return (null, "Unknown change.");
                if (c.Op == "put" && (c.Data == null || c.Data.LongLength > MaxFileBytes)) return (null, "\"" + c.Path + "\": no content, or larger than " + Units.Gb(MaxFileBytes) + ".");
                var (state, bytes) = con.FileStat(scope, c.Path);
                if (state == "folder" || state == "link" || state == "special") return (null, "\"" + c.Path + "\" is a " + (state == "folder" ? "folder" : state == "link" ? "link" : "special file") + " on the console; only regular files are changed.");
                if (c.Op == "del" && state == "absent") return (null, "\"" + c.Path + "\" is not on the console.");
                var data = c.Data;
                if (c.Op == "put" && scope == "user" && point.Model.IsNes) { var (d2, note, refusal) = SaveDrop(c.Path, data); if (refusal != null) return (null, refusal); if (note != null) p.Notes.Add(note); data = d2; }
                else if (c.Op == "put" && scope == "user" && IsBatteryName(c.Path)) p.Notes.Add("\"" + c.Path + "\" is written exactly as you chose it: Workbench does not convert the " + point.Model.Name + "'s saves.");
                p.Changes.Add(new PlannedFileChange { Op = c.Op, Path = c.Path, Data = data, Bytes = c.Op == "put" ? data.LongLength : 0, NewSha256 = c.Op == "put" ? Sha(data) : null, OldState = state, OldBytes = bytes });
            }
            if (p.Changes.Any(c => c.Op == "put" && c.Path.StartsWith("hakchi/games/", StringComparison.Ordinal))) p.Notes.Add("Copying a game file does not by itself add it to the console's menu: a menu entry needs its own data.");
            p.Notes.Add(point.Banner);
            p.Nonce = Hex.Of(Guid.NewGuid().ToByteArray()); p.ExpiresUtc = DateTime.UtcNow + RestoreEngine.ConfirmWindow; p.Digest = DigestOf(p);
            return (p, null);
        }

        /// <summary>Expert's typed save drop (the same converter as Backup Browser): a file put at one of the console's own battery-save names
        /// is written as it is only when it already is in the console's save format; a plain battery save is converted only for a game whose
        /// entry is confirmed on a real console, and anything else is refused with the reason (nothing is guessed or written blind). A suspend
        /// point is never taken for a battery save, and a Classic Cart Studio saved state never goes into a battery-save file.</summary>
        /// <summary>a file at one of the console's own battery-save names, in its HOME saves (not a suspend point)</summary>
        static bool IsBatteryName(string path) => path.StartsWith("clover/profiles/", StringComparison.Ordinal) && !Saves.StockSave.IsSuspendPointPath(path) && Saves.StockWrapper.BatteryNames.Contains(path.Substring(path.LastIndexOf('/') + 1));

        static (byte[] data, string note, string refusal) SaveDrop(string path, byte[] data)
        {
            if (!path.StartsWith("clover/profiles/", StringComparison.Ordinal) || Saves.StockSave.IsSuspendPointPath(path)) return (data, null, null);
            var name = path.Substring(path.LastIndexOf('/') + 1);
            if (!Saves.StockWrapper.BatteryNames.Contains(name)) return (data, null, null);
            var src = Saves.SaveSource.FromConsolePath("/var/lib/" + path, null, "a file chosen on this computer");
            var ins = Saves.StockSave.Inspect(data, src);
            if (ins.Recognized) return (data, null, null);
            if (ins.Kind != "not-recognized") return (null, null, "\"" + path + "\" was not changed: " + ins.Text);
            var game = new Saves.GameIdentity { ConsoleCode = src.ConsoleCode };
            var entry = Saves.BatteryTable.For(game);
            if (entry == null) return (null, null, "\"" + path + "\" was not changed: this game has no battery-save entry in this version, so a plain battery save is not converted for it (nothing is guessed).");
            var conv = Saves.StockSave.ConvertToStock(data, game, entry, src);
            if (!conv.Ok) return (null, null, "\"" + path + "\" was not changed: " + conv.Text);
            if (!conv.ForConsole) return (null, null, "\"" + path + "\" was not changed: this game's save format is not yet confirmed on a real console, so a converted save is never put on one.");
            return (conv.Output, "\"" + path + "\": " + conv.Text, null);
        }

        static string DigestOf(FilePlan p)
        {
            var sb = new StringBuilder("ccs-files-plan/1\n").Append(p.Scope).Append('\n').Append(ConsoleIdentity.NormalizeHwid(p.Hwid)).Append('\n').Append(p.Session).Append('\n').Append(p.Point?.Archive?.DigestLine()).Append('\n');
            foreach (var c in p.Changes) sb.Append(c.Op).Append(' ').Append(c.Path).Append(' ').Append(c.NewSha256 ?? "-").Append(' ').Append(c.Bytes.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(c.OldState).Append('\n');
            sb.Append(p.Nonce);
            using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        public static FileOutcome Execute(RecoveryConsole con, FilePlan plan, string digest, string nonce, EngineContext ctx)
        {
            var o = new FileOutcome();
            FileOutcome Refuse(string why) { o.State = "refused"; o.Problem = why; return o; }
            if (plan == null) return Refuse("No plan.");
            if (digest != plan.Digest || nonce != plan.Nonce || plan.Nonce == null || DigestOf(plan) != plan.Digest) return Refuse("The confirmation does not match the reviewed changes.");
            if (DateTime.UtcNow > plan.ExpiresUtc) return Refuse("The confirmation is more than 10 minutes old. Review the changes again.");
            if (con.SessionId != plan.Session) return Refuse("The console was connected again after the review. Review the changes again. Nothing was changed.");
            var id = con.Identify();
            if (!ConsoleIdentity.SameConsole(id.Hwid, plan.Hwid)) return Refuse("A different console is connected now. Nothing was changed.");
            using (var hold = Evidence.Hold.Open(plan.Point?.Archive?.Path))
            {
                if (hold.Problem != null) return Refuse("The restore point cannot be used (" + hold.Problem + "). Nothing was changed.");
                var rp = RestorePoints.Require(plan.Point, con, RestorePoints.UserFiles, ctx.Cancelled);
                if (rp != null) return Refuse(rp + " Nothing was changed.");
                Record rec;
                try { rec = Record.Start(ctx.Settings.RecordsFolder, Guid.NewGuid().ToString("N").Substring(0, 12), plan.Scope == "dev" ? "developer-files" : "expert-files", new JObj { { "plan", plan.ToJson() }, { "console_tag", plan.ConsoleTag }, { "simulated", con.Simulated } }); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Refuse("The change record could not be saved in " + ctx.Settings.RecordsFolder + " (" + ex.Message + "), so nothing was changed."); }
                plan.Nonce = null;
                o.RecordFile = rec.File; var log = new List<object>();
                var auth = new WriteAuthority(plan.Digest, plan.Hwid, plan.Targets, plan.Scope == "dev" ? (Func<bool>)(() => con.Simulated || Gate.DeveloperWrites) : () => con.Simulated || Gate.ExpertWrites, plan.Scope == "dev" ? Gate.DeveloperText : Gate.ExpertText);
                rec.State("writing"); con.Card("writing");
                foreach (var c in plan.Changes)
                {
                    if (ctx.Cancelled()) { rec.State("stopped", "canceled between changes"); o.State = "stopped"; o.Problem = "Canceled. Changes made: " + (o.Done.Count == 0 ? "none" : string.Join(", ", o.Done)) + "."; return o; }
                    ctx.Say("files", (c.Op == "put" ? "Writing " : "Removing ") + c.Path + "...");
                    log.Add(new JObj { { "op", c.Op }, { "path", c.Path }, { "state", "began" }, { "at", Record.Now() } }); rec.Set("changes", log).Save();
                    WriteResult r;
                    try { r = con.FileStep(c.Op, plan.Scope, c.Path, c.NewSha256, c.OldState, c.Op == "put" ? c.Data : null, auth); }
                    catch (GateClosedException ex) { rec.State("refused", ex.Message); o.State = "refused"; o.Problem = ex.Message; return o; }
                    log.Add(new JObj { { "op", c.Op }, { "path", c.Path }, { "state", r.Ok ? (c.Op == "del" ? "removed-tombstone" : "written-and-read-back") : "failed" }, { "old", c.OldState }, { "new_sha256", c.NewSha256 }, { "detail", r.Line ?? r.Problem }, { "at", Record.Now() } }); rec.Set("changes", log).Save();
                    if (!r.Ok)
                    {
                        rec.State("stopped", r.Problem); con.Card("stopped"); o.State = "stopped";
                        o.Problem = "Stopped at " + c.Path + " (" + r.Problem + "). Nothing more was changed and nothing was retried. Changes made before: " + (o.Done.Count == 0 ? "none" : string.Join(", ", o.Done)) + ". " + SessionRestorePoint.NoticeText;
                        return o;
                    }
                    o.Done.Add(c.Path);
                }
                rec.State("changed"); con.Card("done"); o.State = "changed"; return o;
            }
        }
    }
}
