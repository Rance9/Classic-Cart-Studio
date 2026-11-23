// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Console Composer's execution: chosen files from one or more verified backups put on THIS console's data storage, in the files image
// (ram/ccsfiles, scope "compose": clover/profiles/, hakchi/games/, ccs/), under the STRICT policy and its own boundary Gate.ComposerWrites:
//   rebuild   sources: backups of this console (any dates); each chosen file is taken from the backup named for it
//   migrate   source: a backup of ANOTHER console. Only its user files and Classic Cart Studio data move; this console keeps its own startup
//             software, bootloaders, storage table and identity (none of those is ever written here). What cannot move is listed.
//   plan      every source verified from scratch and bound (Evidence); each file read from the source's data storage and checked against
//             that backup's own file list (SHA-256) when it has one; the console's current state of each target read (stat); a complete
//             backup of the console as it is now, made in this session and bound with the session; one-time confirmation
//   execute   the console, every source and the protective backup held read-only and verified again; the record saved first; then each
//             file once (stage, compare, new name, rename, sync, read back); the first failure stops every later change
// A shared composition file (Compose/) only describes choices: it can never authorize a write or stand in for any of this.
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsBackup.Ext4;
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
    public sealed class ComposeItem { public string SourceBackup, SourcePath, TargetPath; }

    public sealed class ComposePlan
    {
        public string Kind, Hwid, ConsoleTag, Session, Digest, Nonce, Refusal; public DateTime ExpiresUtc; public bool Simulated;
        public List<BoundArchive> Sources = new List<BoundArchive>(); public BoundArchive Protective;
        public List<PlannedFileChange> Changes = new List<PlannedFileChange>(); public List<string> Skipped = new List<string>(), Notes = new List<string>();
        public JObj ToJson() => new JObj
        {
            { "kind", Kind }, { "console_tag", ConsoleTag }, { "evidence", Sources.Concat(new[] { Protective }).Where(x => x != null).Select(x => (object)x.ToJson()).ToList() },
            { "changes", Changes.Select(c => (object)new JObj { { "op", c.Op }, { "path", c.Path }, { "new_sha256", c.NewSha256 }, { "bytes", c.Bytes }, { "old", c.OldState } }).ToList() },
            { "skipped", Skipped }, { "notes", Notes }, { "refusal", Refusal }, { "digest", Digest }, { "nonce", Nonce }, { "expires_utc", ExpiresUtc.ToString("o", CultureInfo.InvariantCulture) },
        };
    }

    public static class ComposerEngine
    {
        static string Sha(byte[] b) { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(b)); }

        /// <summary>a file from a verified backup's data storage, checked against that backup's own file list when it has one</summary>
        internal static byte[] FileOf(string backup, string path, out string why)
        {
            why = null;
            try
            {
                var m = History.Peek(backup);
                var dev = (m?.O("layout")?.A("partitions") ?? new List<object>()).OfType<Dictionary<string, object>>().FirstOrDefault(x => x.S("label") == "data" || x.S("label") == "rootfs_data")?.S("device");
                using (var z = ZipReader.Open(backup, BackupFormat.Allowed))
                {
                    var e = dev == null ? null : z.Find("restore/" + dev + ".img"); if (e == null || e.Method != 0) { why = "the backup has no data storage"; return null; }
                    var fs = new Ext4Reader(z.StoredWindow(e)); var ino = fs.Lookup("/" + path);
                    if (ino == null) { why = "not in that backup"; return null; }
                    if (!fs.IsRegular(ino.Value)) { why = "not a regular file in that backup"; return null; }
                    if (fs.FileSize(fs.Inode(ino.Value)) > FilesEngine.MaxFileBytes) { why = "larger than " + Units.Gb(FilesEngine.MaxFileBytes); return null; }
                    var b = fs.ReadAll(ino.Value, FilesEngine.MaxFileBytes);
                    var inv = z.Find(BackupFormat.InventoryName);
                    if (inv != null)
                    {
                        Dictionary<string, object> d; using (var st = z.OpenMember(inv)) using (var r = new StreamReader(st)) d = Json.Parse(r.ReadToEnd());
                        var row = (d.A("entries") ?? new List<object>()).OfType<Dictionary<string, object>>().FirstOrDefault(x => x.S("path") == "/" + path);
                        if (row != null && row.S("sha256") != null && row.S("sha256") != Sha(b)) { why = "its bytes differ from the backup's own file list"; return null; }
                    }
                    return b;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is Ext4Exception || ex is InvalidDataException) { why = "it could not be read: " + ex.Message; return null; }
        }

        /// <summary>kind: "rebuild" (sources are this console's backups) or "migrate" (one source, another console's backup)</summary>
        public static (ComposePlan plan, string why) Plan(RecoveryConsole con, EngineContext ctx, string kind, IList<ComposeItem> items)
        {
            if (con.Profile != SessionProfile.Files) return (null, "The console is not in a file session.");
            if (kind != "rebuild" && kind != "migrate") return (null, "Unknown composition.");
            if (items == null || items.Count == 0) return (null, "Nothing was chosen.");
            var id = con.Identify(); var mw = ConsoleModels.RefusalFor(id, "composition"); if (mw != null) return (null, mw + " Nothing was changed.");
            var p = new ComposePlan { Kind = kind, Hwid = id.Hwid, ConsoleTag = id.ShortTag, Session = con.SessionId, Simulated = con.Simulated };
            var verified = new Dictionary<string, Verification>(StringComparer.OrdinalIgnoreCase);
            foreach (var src in items.Select(i => i.SourceBackup).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(src) || !File.Exists(src)) return (null, "A source backup is missing: " + src);
                var v = Verifier.VerifyFile(src, null, ctx.Cancelled);
                if (!v.Ok) return (null, Path.GetFileName(src) + " did not pass its check: " + v.Problems[0]);
                if (v.Simulated != con.Simulated) return (null, "A simulated console's backup and a real console are never mixed.");
                if (v.Model == null || v.Model != (id.Model ?? ConsoleModels.Nes)) return (null, Path.GetFileName(src) + " is a backup of " + (v.Model?.Name ?? "a console this version does not support") + ", not of this console's model (" + (id.Model ?? ConsoleModels.Nes).Name + "): one kind of console's data is never put on another kind.");
                bool same = ConsoleIdentity.SameConsole(v.Hwid, id.Hwid);
                if (kind == "rebuild" && !same) return (null, Path.GetFileName(src) + " is a backup of another console. Use \"Set Up Like My Other Console\" for that.");
                if (kind == "migrate" && same) return (null, Path.GetFileName(src) + " is a backup of this console. Use \"Rebuild from My Backups\" for that.");
                verified[src] = v; p.Sources.Add(BoundArchive.From(kind == "migrate" ? "source backup of another console" : "source backup", src, v));
            }
            if (kind == "migrate" && p.Sources.Count != 1) return (null, "Migration takes one source console's backup at a time.");
            var targets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var it in items)
            {
                var target = it.TargetPath ?? it.SourcePath;
                if (!FilesEngine.PathOk(it.SourcePath ?? "") || !FilesEngine.PathOk(target)) { p.Skipped.Add((it.SourcePath ?? "?") + ": not a file name this accepts"); continue; }
                if (!FilesEngine.InScope("compose", target)) { p.Skipped.Add(target + ": not something Console Composer moves (only saves and settings, games added with hakchi, and Classic Cart Studio's data)"); continue; }
                if (SegaCleanup.NeverWritten(target)) { p.Skipped.Add(target + ": " + SegaCleanup.NotWritten); continue; }
                if (!targets.Add(target)) return (null, target + " is chosen twice.");
                var bytes = FileOf(it.SourceBackup, it.SourcePath, out var why);
                if (bytes == null) { p.Skipped.Add(it.SourcePath + ": " + why); continue; }
                var (state, n) = con.FileStat("compose", target);
                if (state == "folder" || state == "link" || state == "special") { p.Skipped.Add(target + ": a " + state + " on this console"); continue; }
                var sha = Sha(bytes);
                if (state == sha) { p.Notes.Add(target + " is already the same on this console."); continue; }
                p.Changes.Add(new PlannedFileChange { Op = "put", Path = target, Data = bytes, Bytes = bytes.LongLength, NewSha256 = sha, OldState = state, OldBytes = n });
            }
            if (p.Changes.Count == 0) { p.Refusal = "Nothing needs to change on this console." + (p.Skipped.Count > 0 ? " Some choices could not be used." : ""); return (p, null); }
            if (kind == "migrate") p.Notes.Add("This console keeps its own startup software, bootloaders, storage table and identity; only the files listed move.");
            if (p.Changes.Any(c => c.OldState != "absent")) p.Notes.Add(Units.Count(p.Changes.Count(c => c.OldState != "absent"), "file") + " on this console will be replaced; the backup made just now keeps them.");
            // protect: the console as it is now, in this session (strict policy)
            ctx.Say("protect", "Backing up the console as it is now, before anything changes...");
            var prot = CaptureEngine.Run(con, ctx);
            if (!prot.Ok || prot.Verification == null || !prot.Verification.Ok || !ConsoleIdentity.SameConsole(prot.Verification.Hwid, id.Hwid) || !prot.Verification.RestoreReady)
                return (null, "The backup of the console as it is now did not complete (" + (prot.Problem ?? prot.State) + "), so nothing was written.");
            p.Protective = BoundArchive.From("backup made just now, before composing", prot.PrimaryPath, prot.Verification, con.SessionId);
            var room = Capacity.Check(prot.PrimaryPath, p.Changes, out var need, out var have); if (room != null) return (null, room + " Nothing was written.");
            p.Notes.Add("Space: about " + Units.Gb(need) + " of the " + Units.Gb(have) + " available on the console's data storage.");
            p.Nonce = Hex.Of(Guid.NewGuid().ToByteArray()); p.ExpiresUtc = DateTime.UtcNow + RestoreEngine.ConfirmWindow; p.Digest = DigestOf(p);
            return (p, null);
        }

        static string DigestOf(ComposePlan p)
        {
            var sb = new StringBuilder("ccs-compose-plan/1\n").Append(p.Kind).Append('\n').Append(ConsoleIdentity.NormalizeHwid(p.Hwid)).Append('\n').Append(p.Session).Append('\n');
            foreach (var s in p.Sources) sb.Append(s.DigestLine()).Append('\n');
            sb.Append(p.Protective?.DigestLine()).Append('\n');
            foreach (var c in p.Changes) sb.Append(c.Path).Append(' ').Append(c.NewSha256).Append(' ').Append(c.Bytes.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(c.OldState).Append('\n');
            sb.Append(p.Nonce);
            using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        public static FileOutcome Execute(RecoveryConsole con, ComposePlan plan, string digest, string nonce, EngineContext ctx)
        {
            var o = new FileOutcome();
            FileOutcome Refuse(string why) { o.State = "refused"; o.Problem = why; return o; }
            if (plan == null || plan.Refusal != null) return Refuse(plan?.Refusal ?? "No plan.");
            if (digest != plan.Digest || nonce != plan.Nonce || plan.Nonce == null || DigestOf(plan) != plan.Digest) return Refuse("The confirmation does not match the reviewed composition.");
            if (DateTime.UtcNow > plan.ExpiresUtc) return Refuse("The confirmation is more than 10 minutes old. Review the composition again.");
            if (con.SessionId != plan.Session || plan.Protective?.Session != plan.Session) return Refuse("The console was connected again after the review. Review the composition again. Nothing was changed.");
            var id = con.Identify();
            if (!ConsoleIdentity.SameConsole(id.Hwid, plan.Hwid)) return Refuse("A different console is connected now. Nothing was changed.");
            using (var hold = Evidence.Hold.Open(plan.Sources.Select(s => s.Path).Concat(new[] { plan.Protective.Path }).ToArray()))
            {
                if (hold.Problem != null) return Refuse("A backup the composition relies on cannot be used (" + hold.Problem + "). Nothing was changed.");
                foreach (var s in plan.Sources) { var why = Evidence.Recheck(s, s.Hwid, con.Simulated, null, ctx.Cancelled); if (why != null) return Refuse(why + " Nothing was changed."); }
                var pw = Evidence.Recheck(plan.Protective, plan.Hwid, con.Simulated, con.SessionId, ctx.Cancelled); if (pw != null) return Refuse(pw + " Nothing was changed.");
                Record rec;
                try { rec = Record.Start(ctx.Settings.RecordsFolder, Guid.NewGuid().ToString("N").Substring(0, 12), "compose", new JObj { { "plan", plan.ToJson() }, { "console_tag", plan.ConsoleTag }, { "simulated", con.Simulated } }); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Refuse("The composition's record could not be saved (" + ex.Message + "), so nothing was written."); }
                plan.Nonce = null;
                o.RecordFile = rec.File; var log = new List<object>();
                var auth = new WriteAuthority(plan.Digest, plan.Hwid, plan.Changes.Select(c => "put:" + c.Path).ToArray(), () => con.Simulated || Gate.ComposerWrites, Gate.ComposerText);
                rec.State("writing"); con.Card("writing");
                foreach (var c in plan.Changes)
                {
                    if (ctx.Cancelled()) { rec.State("stopped", "canceled between files"); o.State = "stopped"; o.Problem = "Canceled. Files written: " + (o.Done.Count == 0 ? "none" : string.Join(", ", o.Done)) + "."; return o; }
                    ctx.Say("compose", "Writing " + c.Path + "...");
                    WriteResult r;
                    try { r = con.FileStep("put", "compose", c.Path, c.NewSha256, c.OldState, c.Data, auth); }
                    catch (GateClosedException ex) { rec.State("refused", ex.Message); o.State = "refused"; o.Problem = ex.Message; return o; }
                    log.Add(new JObj { { "path", c.Path }, { "state", r.Ok ? "written-and-read-back" : "failed" }, { "detail", r.Line ?? r.Problem }, { "at", Record.Now() } }); rec.Set("changes", log).Save();
                    if (!r.Ok) { rec.State("stopped", r.Problem); con.Card("stopped"); o.State = "stopped"; o.Problem = "Stopped at " + c.Path + " (" + r.Problem + "). Nothing more was changed and nothing was retried. The backup made just now holds the console as it was."; return o; }
                    o.Done.Add(c.Path);
                }
                rec.State("changed"); con.Card("done"); o.State = "changed"; return o;
            }
        }
    }
}
