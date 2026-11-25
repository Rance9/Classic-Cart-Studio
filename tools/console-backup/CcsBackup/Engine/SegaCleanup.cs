// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The Sega Mini menu pictures clean-up: the eligible pictures for the Sega Mini menu (Browse/SegaPictures.cs) removed from this console's
// data storage, and exactly those files put back, in the files image (ram/ccsfiles, scope "user": hakchi/games/), under the STRICT policy
// and its own boundary Gate.CleanupWrites. Optional: nothing happens unless the person reviews the list and confirms it.
//   remove    the console identified (its model's "files" capability); a complete backup of the console as it is now, made and verified in
//             this session (it keeps every picture, byte for byte); the eligible list worked out from THAT backup by the shared rule, each
//             file read from it (SHA-256) and found exactly so on the console now (stat); one-time confirmation of exactly that list
//   put back  from the removal's record: the backup made before the removal verified again and still the file the record names; each
//             removed file read from it and checked against the record; put back only where the console has no file at that name and the
//             game's entry is still there (anything else is left alone, with why); a backup of the console as it is now, made in this
//             session; one-time confirmation
//   execute   the console and the backups held read-only and verified again; each file checked again on the console (a change since the
//             review stops before anything is used up or written); the record saved first; then each file once (a removal: removed,
//             synced, checked absent; a put back: staged, written to a new name, renamed, synced, read back); the first failure stops
// The person's PC files (another program's picture library or its sync folder) are never read or changed here.
using CcsBackup.Archive;
using CcsBackup.Browse;
using CcsBackup.Device;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CcsBackup.Engine
{
    public sealed class SegaCleanupPlan
    {
        public string Kind, Hwid, ConsoleTag, Session, Digest, Nonce, Refusal, RemovalRecord; public DateTime ExpiresUtc; public bool Simulated;
        public BoundArchive Protective, Source;     // Source: put back only, the backup made before the removal
        public List<PlannedFileChange> Changes = new List<PlannedFileChange>(); public List<string> Kept = new List<string>(), Notes = new List<string>();
        public JObj Account;
        public JObj ToJson() => new JObj
        {
            { "kind", Kind }, { "console_tag", ConsoleTag }, { "title", SegaPictures.Title }, { "explanation", SegaPictures.Explanation },
            { "evidence", new[] { Source, Protective }.Where(x => x != null).Select(x => (object)x.ToJson()).ToList() },
            { "changes", Changes.Select(c => (object)new JObj { { "op", c.Op }, { "path", c.Path }, { "sha256", c.Op == "del" ? c.OldState : c.NewSha256 }, { "bytes", c.Op == "del" ? c.OldBytes : c.Bytes } }).ToList() },
            { "bytes", Changes.Sum(c => c.Op == "del" ? c.OldBytes : c.Bytes) }, { "kept", Kept }, { "notes", Notes }, { "account", Account },
            { "removal_record", RemovalRecord }, { "refusal", Refusal }, { "digest", Digest }, { "nonce", Nonce }, { "expires_utc", ExpiresUtc.ToString("o", CultureInfo.InvariantCulture) },
        };
    }

    public static class SegaCleanup
    {
        public const string RemoveKind = "sega-pictures", PutBackKind = "sega-pictures-put-back";
        /// <summary>what Console Composer and a private restore package say for a picture for the Sega Mini menu they leave out</summary>
        public const string NotWritten = "a picture for the Sega Mini menu: Workbench does not write these (Hakchi2 CE makes them again if you sync with it)";
        /// <summary>a file Console Composer and private restore packages never write: a picture for the Sega Mini menu in hakchi's game folders</summary>
        public static bool NeverWritten(string path) => path != null && path.StartsWith("hakchi/games/", StringComparison.Ordinal) && SegaPictures.LooksLike(path.Substring(path.LastIndexOf('/') + 1));
        static string Sha(byte[] b) { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(b)); }

        static (BoundArchive prot, string why) Protect(RecoveryConsole con, EngineContext ctx, ConsoleIdentity id, string role)
        {
            ctx.Say("protect", "Backing up the console as it is now, before anything changes...");
            var prot = CaptureEngine.Run(con, ctx);
            if (!prot.Ok || prot.Verification == null || !prot.Verification.Ok || !ConsoleIdentity.SameConsole(prot.Verification.Hwid, id.Hwid) || !prot.Verification.RestoreReady)
                return (null, "The backup of the console as it is now did not complete (" + (prot.Problem ?? prot.State) + "), so nothing was changed.");
            return (BoundArchive.From(role, prot.PrimaryPath, prot.Verification, con.SessionId), null);
        }

        /// <summary>the removal: the eligible pictures as the backup made now in this session shows them, each found exactly so on the console</summary>
        public static (SegaCleanupPlan plan, string why) PlanRemove(RecoveryConsole con, EngineContext ctx)
        {
            if (con.Profile != SessionProfile.Files) return (null, "The console is not in a file session.");
            var id = con.Identify(); var mw = ConsoleModels.RefusalFor(id, "files") ?? ConsoleData.Refusal(id); if (mw != null) return (null, mw + " Nothing was changed.");
            var p = new SegaCleanupPlan { Kind = RemoveKind, Hwid = id.Hwid, ConsoleTag = id.ShortTag, Session = con.SessionId, Simulated = con.Simulated };
            var (prot, pwhy) = Protect(con, ctx, id, "backup made just now, before the pictures are removed"); if (prot == null) return (null, pwhy);
            p.Protective = prot;
            var open = BackupContents.Open(prot.Path, ctx.Settings, ctx.Cancelled);
            if (!open.Ok) return (null, "The backup made just now could not be opened to list the pictures (" + open.Problem + "). Nothing was changed.");
            using (var c = open.Contents)
            {
                var set = SegaPictures.Find(c.Entries, d => { var b = c.ReadVerified(d, 64 * 1024, out _, ctx.Cancelled); return b == null ? null : Encoding.UTF8.GetString(b); }, !c.Salvage && c.DataProblem == null);
                foreach (var (e, why) in set.Kept) p.Kept.Add(e.ImagePath.Substring(1) + ": " + why);
                foreach (var s in set.Eligible)
                {
                    var b = c.ReadVerified(s.Entry, FilesEngine.MaxFileBytes, out var rwhy, ctx.Cancelled);
                    if (b == null) return (null, s.Path + " could not be read from the backup made just now (" + rwhy + "). Nothing was changed.");
                    var sha = Sha(b);
                    if (s.Sha256 != null && s.Sha256 != sha) return (null, s.Path + ": its bytes in the backup made just now differ from that backup's own list. Nothing was changed.");
                    var (state, n) = con.FileStat("user", s.Path);
                    if (state != sha || n != b.LongLength) return (null, s.Path + " changed on the console while it was being backed up. Nothing was changed; check the console again.");
                    p.Changes.Add(new PlannedFileChange { Op = "del", Path = s.Path, OldState = sha, OldBytes = b.LongLength, Bytes = 0 });
                }
                var profile = (id.Model ?? ConsoleModels.Nes).Id;
                long? avail = c.DataFreeBytes != null && c.DataReservedBytes != null ? Math.Max(0, c.DataFreeBytes.Value - c.DataReservedBytes.Value) : (long?)null;
                p.Account = SegaPictures.Account(set, profile, avail, c.DataBlockBytes);
            }
            p.Notes.Add(SegaPictures.Explanation);
            if (p.Changes.Count == 0) { p.Refusal = "No pictures for the Sega Mini menu that Workbench can remove are on this console." + (p.Kept.Count > 0 ? " " + Units.Count(p.Kept.Count, "picture") + " of that kind stay, each with the reason." : ""); return (p, null); }
            p.Notes.Add(SegaPictures.Recreated);
            p.Notes.Add("The backup made just now keeps every one of them; Put Them Back puts exactly these files back.");
            p.Nonce = Hex.Of(Guid.NewGuid().ToByteArray()); p.ExpiresUtc = DateTime.UtcNow + RestoreEngine.ConfirmWindow; p.Digest = DigestOf(p);
            return (p, null);
        }

        /// <summary>putting back exactly the files a removal's record names, from the backup made before that removal</summary>
        public static (SegaCleanupPlan plan, string why) PlanPutBack(RecoveryConsole con, EngineContext ctx, string recordFile)
        {
            if (con.Profile != SessionProfile.Files) return (null, "The console is not in a file session.");
            Dictionary<string, object> rec;
            try { rec = Json.Parse(File.ReadAllText(recordFile)); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException || ex is ArgumentException) { return (null, "The removal's record could not be read (" + ex.Message + "). Nothing was changed."); }
            var rp = rec.O("plan");
            if (rec.S("kind") != RemoveKind || rp == null || rp.S("kind") != RemoveKind) return (null, "That record is not a removal of pictures for the Sega Mini menu. Nothing was changed.");
            var removed = new HashSet<string>((rec.A("changes") ?? new List<object>()).OfType<Dictionary<string, object>>().Where(x => x.S("state") == "removed-tombstone").Select(x => x.S("path")), StringComparer.Ordinal);
            var planned = (rp.A("changes") ?? new List<object>()).OfType<Dictionary<string, object>>().Where(x => x.S("op") == "del" && removed.Contains(x.S("path"))).ToList();
            if (planned.Count == 0) return (null, "That removal's record names no file it removed. Nothing was changed.");
            var ev = (rp.A("evidence") ?? new List<object>()).OfType<Dictionary<string, object>>().FirstOrDefault();
            var srcPath = ev?.S("path");
            if (srcPath == null || !File.Exists(srcPath)) return (null, "The backup made before the removal is missing (" + srcPath + "), so the pictures cannot be put back from it. Nothing was changed.");
            var id = con.Identify(); var mw = ConsoleModels.RefusalFor(id, "files") ?? ConsoleData.Refusal(id); if (mw != null) return (null, mw + " Nothing was changed.");
            var v = Verifier.VerifyFile(srcPath, null, ctx.Cancelled);
            if (!v.Ok) return (null, "The backup made before the removal no longer passes its check (" + v.Problems[0] + "). Nothing was changed.");
            if (v.ArchiveSha256 != ev.S("sha256")) return (null, "The backup made before the removal is not the file its record names (it changed). Nothing was changed.");
            if (!ConsoleIdentity.SameConsole(v.Hwid, id.Hwid)) return (null, "That removal was made on a different console. Nothing was changed.");
            if (v.Simulated != con.Simulated) return (null, "A simulated console's backup and a real console are never mixed.");
            var p = new SegaCleanupPlan { Kind = PutBackKind, Hwid = id.Hwid, ConsoleTag = id.ShortTag, Session = con.SessionId, Simulated = con.Simulated, RemovalRecord = recordFile };
            p.Source = BoundArchive.From("backup made before the pictures were removed", srcPath, v);
            foreach (var x in planned)
            {
                var path = x.S("path"); var want = x.S("sha256");
                if (!FilesEngine.PathOk(path) || !FilesEngine.InScope("user", path) || !path.StartsWith("hakchi/games/", StringComparison.Ordinal) || !SegaPictures.LooksLike(path.Substring(path.LastIndexOf('/') + 1)))
                    return (null, "The removal's record names a file this does not put back (" + path + "). Nothing was changed.");
                var b = ComposerEngine.FileOf(srcPath, path, out var why);
                if (b == null) { p.Kept.Add(path + ": not put back, " + why + "."); continue; }
                var sha = Sha(b);
                if (sha != want) { p.Kept.Add(path + ": not put back, its bytes in the backup differ from the removal's record."); continue; }
                var folder = path.Substring(0, path.LastIndexOf('/')); var desk = folder + "/" + folder.Substring(folder.LastIndexOf('/') + 1) + ".desktop";
                var (fs, _) = con.FileStat("user", folder); var (ds, _) = con.FileStat("user", desk);
                if (fs != "folder" || !Hex.IsHex(ds, 64)) { p.Kept.Add(path + ": not put back, its game's entry is no longer on the console."); continue; }
                var (state, n) = con.FileStat("user", path);
                if (state == sha) { p.Notes.Add(path + " is already back on the console."); continue; }
                if (state != "absent") { p.Kept.Add(path + ": not put back, " + (Hex.IsHex(state, 64) ? "a different file has that name now; it stays." : "a " + state + " has that name now.")); continue; }
                p.Changes.Add(new PlannedFileChange { Op = "put", Path = path, Data = b, Bytes = b.LongLength, NewSha256 = sha, OldState = "absent" });
            }
            if (p.Changes.Count == 0) { p.Refusal = "Nothing needs to be put back on this console." + (p.Kept.Count > 0 ? " Some files could not be put back; each says why." : ""); return (p, null); }
            p.Notes.Add(SegaPictures.Explanation);
            var (prot, pwhy) = Protect(con, ctx, id, "backup made just now, before the pictures are put back"); if (prot == null) return (null, pwhy);
            p.Protective = prot;
            var room = Capacity.Check(prot.Path, p.Changes, out var need, out var have); if (room != null) return (null, room + " Nothing was changed.");
            p.Nonce = Hex.Of(Guid.NewGuid().ToByteArray()); p.ExpiresUtc = DateTime.UtcNow + RestoreEngine.ConfirmWindow; p.Digest = DigestOf(p);
            return (p, null);
        }

        static string DigestOf(SegaCleanupPlan p)
        {
            var sb = new StringBuilder("ccs-sega-pictures-plan/1\n").Append(p.Kind).Append('\n').Append(ConsoleIdentity.NormalizeHwid(p.Hwid)).Append('\n').Append(p.Session).Append('\n');
            sb.Append(p.Source?.DigestLine()).Append('\n').Append(p.Protective?.DigestLine()).Append('\n').Append(p.RemovalRecord).Append('\n');
            foreach (var c in p.Changes) sb.Append(c.Op).Append(' ').Append(c.Path).Append(' ').Append(c.NewSha256 ?? "-").Append(' ').Append(c.Bytes.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(c.OldState).Append(' ').Append(c.OldBytes.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(p.Nonce);
            using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        public static FileOutcome Execute(RecoveryConsole con, SegaCleanupPlan plan, string digest, string nonce, EngineContext ctx)
        {
            var o = new FileOutcome();
            FileOutcome Refuse(string why) { o.State = "refused"; o.Problem = why; return o; }
            if (plan == null || plan.Refusal != null) return Refuse(plan?.Refusal ?? "No plan.");
            if (digest != plan.Digest || nonce != plan.Nonce || plan.Nonce == null || DigestOf(plan) != plan.Digest) return Refuse("The confirmation does not match the reviewed list. Nothing was changed.");
            if (DateTime.UtcNow > plan.ExpiresUtc) return Refuse("The confirmation is more than 10 minutes old. Review the list again. Nothing was changed.");
            if (con.SessionId != plan.Session || plan.Protective?.Session != plan.Session) return Refuse("The console was connected again after the review. Review the list again. Nothing was changed.");
            var id = con.Identify();
            if (!ConsoleIdentity.SameConsole(id.Hwid, plan.Hwid)) return Refuse("A different console is connected now. Nothing was changed.");
            using (var hold = Evidence.Hold.Open(new[] { plan.Source, plan.Protective }.Where(x => x != null).Select(x => x.Path).ToArray()))
            {
                if (hold.Problem != null) return Refuse("A backup the review relies on cannot be used (" + hold.Problem + "). Nothing was changed.");
                if (plan.Source != null) { var sw = Evidence.Recheck(plan.Source, plan.Hwid, con.Simulated, null, ctx.Cancelled); if (sw != null) return Refuse(sw + " Nothing was changed."); }
                var pw = Evidence.Recheck(plan.Protective, plan.Hwid, con.Simulated, con.SessionId, ctx.Cancelled); if (pw != null) return Refuse(pw + " Nothing was changed.");
                foreach (var c in plan.Changes)
                {
                    var (state, _) = con.FileStat("user", c.Path);
                    if (state != c.OldState) return Refuse(c.Path + " changed on the console after the review. Review the list again. Nothing was changed.");
                }
                Record rec;
                try { rec = Record.Start(ctx.Settings.RecordsFolder, Guid.NewGuid().ToString("N").Substring(0, 12), plan.Kind, new JObj { { "plan", plan.ToJson() }, { "console_tag", plan.ConsoleTag }, { "simulated", con.Simulated } }); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Refuse("The record could not be saved in " + ctx.Settings.RecordsFolder + " (" + ex.Message + "), so nothing was changed."); }
                plan.Nonce = null;
                o.RecordFile = rec.File; var log = new List<object>();
                var auth = new WriteAuthority(plan.Digest, plan.Hwid, plan.Changes.Select(c => c.Op + ":" + c.Path).ToArray(), () => con.Simulated || Gate.CleanupWrites, Gate.CleanupText);
                rec.State("writing"); con.Card("writing");
                foreach (var c in plan.Changes)
                {
                    if (ctx.Cancelled()) { rec.State("stopped", "canceled between files"); o.State = "stopped"; o.Problem = "Canceled. Files changed: " + (o.Done.Count == 0 ? "none" : string.Join(", ", o.Done)) + "."; return o; }
                    ctx.Say("cleanup", (c.Op == "del" ? "Removing " : "Putting back ") + c.Path + "...");
                    log.Add(new JObj { { "op", c.Op }, { "path", c.Path }, { "state", "began" }, { "at", Record.Now() } }); rec.Set("changes", log).Save();
                    WriteResult r;
                    try { r = con.FileStep(c.Op, "user", c.Path, c.NewSha256, c.OldState, c.Op == "put" ? c.Data : null, auth); }
                    catch (GateClosedException ex) { rec.State("refused", ex.Message); o.State = "refused"; o.Problem = ex.Message; return o; }
                    log.Add(new JObj { { "op", c.Op }, { "path", c.Path }, { "state", r.Ok ? (c.Op == "del" ? "removed-tombstone" : "written-and-read-back") : "failed" }, { "sha256", c.Op == "del" ? c.OldState : c.NewSha256 }, { "detail", r.Line ?? r.Problem }, { "at", Record.Now() } }); rec.Set("changes", log).Save();
                    if (!r.Ok)
                    {
                        rec.State("stopped", r.Problem); con.Card("stopped"); o.State = "stopped";
                        o.Problem = "Stopped at " + c.Path + " (" + r.Problem + "). Nothing more was changed and nothing was retried. Files changed before: " + (o.Done.Count == 0 ? "none" : string.Join(", ", o.Done)) + ". The backup made just now holds the console as it was.";
                        return o;
                    }
                    o.Done.Add(c.Path);
                }
                rec.State("changed"); con.Card("done"); o.State = "changed"; return o;
            }
        }
    }
}
