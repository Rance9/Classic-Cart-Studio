// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Restore Console Backup: returns the SAME console to the state captured in a backup file, including everything on its built-in storage
// that has nothing to do with Classic Cart Studio. Phases (the restore failure matrix, RESTORE-FAILURE-MATRIX.md, describes each one's
// interruption consequences):
//   1 validate     the backup file verified from scratch (Verifier): restore-ready, not simulated for a real console
//   2 admit        the console identified afresh in the restore session: the same console (identity), a supported and UNCHANGED
//                  storage layout and the same bootloaders; anything else is refused before any write
//   3 protect      a complete, verified backup of the console as it is now (CaptureEngine): a new file, never over another; no
//                  protective backup, no restore
//   4 plan         what differs, part by part (by checksum) and, for the data storage, file by file (the two backups' file lists):
//                  what reverts, what is removed (added since), what returns; a digest binds the plan to the backup, the protective
//                  backup and the console
//   5 confirm      a one-time confirmation for exactly this plan (Confirm); nothing is written without it
//   6 write        each differing part once, in order (the other storage parts, the data storage, hakchi's spare startup slot, the startup
//                  software last), each received and checked by the console before or as it is written and read back after; the first
//                  failure stops every later write (no second attempt, no automatic rollback)
//   7 readback     every written part read again by the console and compared, the console identified again
// Normal Classic Cart Studio maintenance never uses this authority: only this engine builds a restore WriteAuthority, and only from a
// confirmed plan.
using CcsBackup.Archive;
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
    public sealed class RestoreStep { public string Target, Label, Action, BackupSha256, BackupMd5, CurrentSha256, Member; public long Size; }

    public sealed class RestorePlan
    {
        public string BackupPath, BackupSha256, BackupId, Hwid, ConsoleTag, Session, Digest, Nonce; public DateTime CreatedUtc, ExpiresUtc;
        /// <summary>the backup restored from and the protective backup made in this session, as the review saw them (Evidence)</summary>
        public BoundArchive BackupArchive, ProtectiveArchive;
        public string ProtectivePath => ProtectiveArchive?.Path;
        public string BackupWhen, BackupStateText, CurrentStateText;
        /// <summary>the storage part the backup's table names as the system (rootfs), restored only together with the startup software</summary>
        public string SystemTarget = "nandb";
        public List<RestoreStep> Steps = new List<RestoreStep>();
        public List<string> Notes = new List<string>(), Refusals = new List<string>();
        public int FilesAdded, FilesRemoved, FilesChanged; public List<string> AddedSample = new List<string>(), RemovedSample = new List<string>();
        public bool Simulated;
        public bool Writable => Refusals.Count == 0 && Steps.Any(s => s.Action == "write");
        public bool Partial;
        public JObj ToJson() => new JObj
        {
            { "backup", BackupPath }, { "backup_sha256", BackupSha256 }, { "backup_id", BackupId }, { "protective_backup", ProtectivePath }, { "console_tag", ConsoleTag },
            { "evidence", new List<object> { BackupArchive?.ToJson(), ProtectiveArchive?.ToJson() } },
            { "digest", Digest }, { "nonce", Nonce }, { "expires_utc", ExpiresUtc.ToString("o", CultureInfo.InvariantCulture) }, { "backup_when", BackupWhen },
            { "backup_state", BackupStateText }, { "current_state", CurrentStateText }, { "simulated", Simulated },
            { "steps", Steps.Select(s => (object)new JObj { { "target", s.Target }, { "label", s.Label }, { "action", s.Action }, { "size", s.Size }, { "backup_sha256", s.BackupSha256 }, { "current_sha256", s.CurrentSha256 } }).ToList() },
            { "files_added_since", FilesAdded }, { "files_removed_since", FilesRemoved }, { "files_changed_since", FilesChanged }, { "added_sample", AddedSample }, { "removed_sample", RemovedSample },
            { "notes", Notes }, { "refusals", Refusals }, { "partial", Partial },
        };
    }

    public sealed class RestoreOutcome
    {
        public string State, Problem; public RestorePlan Plan; public BackupOutcome Protective; public List<JObj> Phases = new List<JObj>(); public string RecordFile;
        public List<string> Written = new List<string>(), Partial = new List<string>(), Verified = new List<string>();
        public bool Ok => State == "restored";
    }

    public static class RestoreEngine
    {
        /// <summary>the NES Classic's write order (a backup whose table has no labels keeps it); OrderFor derives the same order from any
        /// labeled table: the system part, the boot part, the other parts, the data storage last, then the spare and the active startup</summary>
        public static readonly string[] WriteOrder = { "nandb", "nanda", "nandd", "nande", "nandc", "boot2-68", "boot2" };
        static readonly string[] RoleOrder = { "rootfs", "boot", "private", "UDISK" };
        public static List<string> OrderFor(IEnumerable<Dictionary<string, object>> partitions)
        {
            var parts = (partitions ?? Enumerable.Empty<Dictionary<string, object>>()).Select(x => (dev: x.S("device"), label: x.S("label"))).ToList();
            if (parts.Count == 0 || parts.Any(x => string.IsNullOrEmpty(x.dev) || string.IsNullOrEmpty(x.label))) return WriteOrder.ToList();
            bool IsData(string l) => l == "data" || l == "rootfs_data";
            var o = new List<string>();
            foreach (var role in RoleOrder) o.AddRange(parts.Where(x => x.label == role).Select(x => x.dev).OrderBy(d => d, StringComparer.Ordinal));
            o.AddRange(parts.Where(x => !RoleOrder.Contains(x.label) && !IsData(x.label)).Select(x => x.dev).OrderBy(d => d, StringComparer.Ordinal));
            o.AddRange(parts.Where(x => IsData(x.label)).Select(x => x.dev).OrderBy(d => d, StringComparer.Ordinal));
            o.Add("boot2-68"); o.Add("boot2");
            return o;
        }
        public static readonly TimeSpan ConfirmWindow = TimeSpan.FromMinutes(10);

        static Func<bool> Boundary(RecoveryConsole con) => () => con.Simulated || Gate.RestoreWrites;

        /// <summary>Phases 1 to 4. The console must already be in a restore session. Returns the plan (with Refusals when it cannot run),
        /// or a refused/stopped outcome. No write happens here.</summary>
        /// <summary>only: a partial restore (Backup Repair, D6): just these storage parts are written; every other differing part is listed
        /// as skipped. The startup software (boot2) and the system part go together, never one without the other. null: the whole restore.</summary>
        public static (RestorePlan plan, RestoreOutcome refused) Prepare(RecoveryConsole con, string backupPath, EngineContext ctx, ISet<string> only)
        {
            var (plan, refused) = Prepare(con, backupPath, ctx);
            if (plan == null || only == null) return (plan, refused);
            var sys = plan.SystemTarget;
            if (only.Contains("boot2") != only.Contains(sys) && plan.Steps.Any(s => (s.Target == "boot2" || s.Target == sys) && s.Action == "write"))
                plan.Refusals.Add("The startup software and the system part are restored together or not at all: one without the other may not start.");
            foreach (var s in plan.Steps.Where(x => x.Action == "write" && !only.Contains(x.Target))) { s.Action = "skipped"; plan.Notes.Add(s.Label + " differs from the backup but was not chosen: it stays as it is now."); }
            if (only.Any(t => !plan.Steps.Any(s => s.Target == t))) plan.Refusals.Add("A chosen part is not in this backup.");
            if (!plan.Steps.Any(s => s.Action == "write") && plan.Refusals.Count == 0) plan.Notes.Add("Nothing chosen differs from the backup: nothing needs to be written.");
            plan.Partial = true; plan.Digest = DigestOf(plan);
            return (plan, null);
        }

        public static (RestorePlan plan, RestoreOutcome refused) Prepare(RecoveryConsole con, string backupPath, EngineContext ctx)
        {
            var o = new RestoreOutcome();
            if (con.Profile != SessionProfile.Restore) { o.State = "refused"; o.Problem = "The console is not in a restore session."; return (null, o); }
            ctx.Say("validate", "Checking the backup file...");
            var v = Verifier.VerifyFile(backupPath, null, ctx.Cancelled, (d, t) => ctx.Say("validate", "Checking the backup file...", d, t));
            if (!v.Ok) { o.State = "refused"; o.Problem = "This backup did not pass its check, so it cannot be restored: " + v.Problems[0]; return (null, o); }
            if (!v.RestoreReady) { o.State = "refused"; o.Problem = "This backup does not hold everything a restore needs."; return (null, o); }
            if (v.Simulated && !con.Simulated) { o.State = "refused"; o.Problem = "This backup was made from a simulated console; it can never be restored to a real one."; return (null, o); }
            if (!v.Simulated && con.Simulated) { o.State = "refused"; o.Problem = "A real console's backup is not restored to a simulated console."; return (null, o); }
            ctx.Say("admit", "Checking the console...");
            ConsoleIdentity id;
            try { id = con.Identify(); Support.CheckForBackup(id, ctx.RecoveryImageId, "restore"); }
            catch (ConsoleRefusedException ex) { o.State = "refused"; o.Problem = ex.Message; return (null, o); }
            if (v.Model == null || v.Model != (id.Model ?? ConsoleModels.Nes)) { o.State = "refused"; o.Problem = "This backup is of " + (v.Model?.Name ?? "a console this version does not support") + ", not of this console's model (" + (id.Model ?? ConsoleModels.Nes).Name + "): one kind of console's backup is never put on another kind. Nothing was changed."; return (null, o); }
            var m = v.Manifest; var plan = new RestorePlan { BackupPath = backupPath, BackupSha256 = v.ArchiveSha256, BackupId = m.S("backup_id"), Hwid = id.Hwid, ConsoleTag = id.ShortTag, Simulated = con.Simulated, CreatedUtc = DateTime.UtcNow, Session = con.SessionId };
            plan.BackupArchive = BoundArchive.From("backup to restore", backupPath, v);
            plan.BackupWhen = m.S("completed_utc"); plan.BackupStateText = m.O("state")?.S("text");
            if (!ConsoleIdentity.SameConsole(id.Hwid, v.Hwid)) { o.State = "refused"; o.Problem = "This backup belongs to a different console (console " + v.ConsoleTag + "); the connected console is console " + id.ShortTag + ". A backup is only ever restored to its own console."; return (null, o); }
            if (id.Get("parttable") != m.O("layout")?.S("partition_table")) { o.State = "refused"; o.Problem = "The console's storage is now divided differently than when this backup was made. Changing how the storage is divided is not something Classic Cart Studio does, so this restore is not available."; return (null, o); }
            // 3 protect: the console as it is now, a complete verified backup (it also reads the bootloaders to compare)
            ctx.Say("protect", "Backing up the console as it is now, before anything changes...");
            var prot = CaptureEngine.Run(con, ctx);
            if (!prot.Ok) { o.State = "stopped"; o.Problem = "The backup of the console as it is now did not complete (" + (prot.Problem ?? prot.State) + "), so nothing was restored and nothing was written."; o.Protective = prot; return (null, o); }
            var pv = prot.Verification; var cur = pv.Manifest;
            if (pv == null || !pv.Ok || !ConsoleIdentity.SameConsole(pv.Hwid, id.Hwid) || pv.Simulated != con.Simulated || !pv.RestoreReady) { o.State = "stopped"; o.Problem = "The backup of the console as it is now is not a complete, verified backup of this console, so nothing was restored and nothing was written."; o.Protective = prot; return (null, o); }
            plan.ProtectiveArchive = BoundArchive.From("protective backup made just before restoring", prot.PrimaryPath, pv, con.SessionId); plan.CurrentStateText = prot.Classification?.Text;
            // bootloaders: never written here; they must be the same
            foreach (var r in new[] { "reference/boot0.bin", "reference/boot1.bin" })
            {
                if (!v.Members.TryGetValue(r, out var a) || !pv.Members.TryGetValue(r, out var b)) { plan.Refusals.Add("The console's bootloaders could not be compared."); continue; }
                if (a.sha256 != b.sha256) plan.Refusals.Add("The console's bootloader (" + (r.Contains("boot0") ? "first stage" : "second stage, u-boot") + ") has changed since this backup was made. Writing a bootloader is not something this restore does, so it is not available.");
            }
            // 4 plan, part by part
            var mparts = (m.O("layout")?.A("partitions") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
            plan.SystemTarget = mparts.FirstOrDefault(x => x.S("label") == "rootfs")?.S("device") ?? "nandb";
            foreach (var t in OrderFor(mparts))
            {
                string member = t == "boot2" ? "restore/boot2-active.img" : t == "boot2-68" ? "restore/boot2-slot68.img" : "restore/" + t + ".img";
                bool inBackup = v.Members.ContainsKey(member), now = pv.Members.ContainsKey(member);
                if (!inBackup && !now) continue;
                var label = Units.TargetLabel(t, (m.O("layout").A("partitions") ?? new List<object>()).OfType<Dictionary<string, object>>().Select(p => new PartitionEntry { Device = p.S("device"), Label = p.S("label"), Blocks512 = p.L("bytes") / 512 }).ToList());
                if (!inBackup)
                {
                    if (t == "boot2-68")
                    {
                        bool genuine = cur.O("state")?.S("original_source") == "slot68" || KnownKernels.IsStockFor(pv.Model, pv.Region, pv.Members[member].md5);
                        if (genuine) { plan.Notes.Add("The console now keeps a spare copy of the original Nintendo kernel (it was empty in this backup). That copy is left in place: it is a safety copy, and removing it is not part of a restore."); continue; }
                        plan.Refusals.Add("The console's spare startup slot holds software that was not there when this backup was made, and this restore cannot remove it."); continue;
                    }
                    plan.Refusals.Add("Part " + t + " exists now but not in the backup."); continue;
                }
                var bm = v.Members[member];
                var step = new RestoreStep { Target = t, Label = label, Member = member, BackupSha256 = bm.sha256, BackupMd5 = bm.md5, Size = bm.size, CurrentSha256 = now ? pv.Members[member].sha256 : null };
                step.Action = now && pv.Members[member].sha256 == bm.sha256 ? "same" : "write";
                plan.Steps.Add(step);
            }
            if (plan.Steps.Any(s => s.Target == "boot2" && s.Action == "write") && !v.Members.ContainsKey("restore/boot2-active.img")) plan.Refusals.Add("The backup has no startup software.");
            Diff(plan, backupPath, prot.PrimaryPath);
            if (!plan.Steps.Any(s => s.Action == "write") && plan.Refusals.Count == 0) plan.Notes.Add("The console already matches this backup: nothing needs to be written.");
            plan.Nonce = Hex.Of(Guid.NewGuid().ToByteArray()) ; plan.ExpiresUtc = DateTime.UtcNow + ConfirmWindow;
            plan.Digest = DigestOf(plan);
            return (plan, null);
        }

        public static string DigestOf(RestorePlan p)
        {
            var sb = new StringBuilder();
            sb.Append(p.Partial ? "ccs-restore-plan/2 partial\n" : "ccs-restore-plan/2\n").Append(p.BackupSha256).Append('\n').Append(p.Hwid).Append('\n').Append(p.Session).Append('\n').Append(p.BackupArchive?.DigestLine()).Append('\n').Append(p.ProtectiveArchive?.DigestLine()).Append('\n').Append(p.Nonce).Append('\n');
            foreach (var s in p.Steps) sb.Append(s.Target).Append(' ').Append(s.Action).Append(' ').Append(s.BackupSha256).Append(' ').Append(s.CurrentSha256 ?? "-").Append('\n');
            using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        static void Diff(RestorePlan p, string backupPath, string protectivePath)
        {
            var a = Files(backupPath); var b = Files(protectivePath);
            if (a == null || b == null) { p.Notes.Add("The file-by-file comparison of the data storage is not available; the whole data storage is still returned exactly to the backup."); return; }
            foreach (var kv in b) { if (!a.ContainsKey(kv.Key)) { p.FilesAdded++; if (p.AddedSample.Count < 12) p.AddedSample.Add("/var/lib" + kv.Key); } else if (a[kv.Key] != kv.Value) p.FilesChanged++; }
            foreach (var kv in a) if (!b.ContainsKey(kv.Key)) { p.FilesRemoved++; if (p.RemovedSample.Count < 12) p.RemovedSample.Add("/var/lib" + kv.Key); }
        }

        static Dictionary<string, string> Files(string backup)
        {
            try
            {
                using (var z = ZipReader.Open(backup, BackupFormat.Allowed))
                {
                    var e = z.Find(BackupFormat.InventoryName); if (e == null) return null;
                    Dictionary<string, object> d; using (var st = z.OpenMember(e)) using (var r = new StreamReader(st)) d = Json.Parse(r.ReadToEnd());
                    var map = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var x in (d.A("entries") ?? new List<object>()).OfType<Dictionary<string, object>>())
                        map[x.S("path")] = x.S("type") + ":" + x.S("mode") + ":" + x.L("uid") + ":" + x.L("gid") + ":" + (x.S("sha256") ?? x.S("target") ?? x.L("size").ToString(CultureInfo.InvariantCulture));
                    return map;
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>Phases 5 to 7: only with the plan's own digest and nonce, before it expires, once. Writes in order; stops at the first
        /// failure; never writes a part twice; reads back everything written and identifies the console again.</summary>
        public static RestoreOutcome Execute(RecoveryConsole con, RestorePlan plan, string confirmDigest, string confirmNonce, EngineContext ctx)
        {
            var o = new RestoreOutcome { Plan = plan };
            if (plan == null || confirmDigest != plan.Digest || confirmNonce != plan.Nonce || DigestOf(plan) != plan.Digest) { o.State = "refused"; o.Problem = "The confirmation does not match the reviewed restore. Review it again."; return o; }
            if (DateTime.UtcNow > plan.ExpiresUtc) { o.State = "refused"; o.Problem = "The confirmation is more than 10 minutes old. Review the restore again."; return o; }
            if (plan.Refusals.Count > 0) { o.State = "refused"; o.Problem = plan.Refusals[0]; return o; }
            if (plan.Nonce == null) { o.State = "refused"; o.Problem = "This restore was already confirmed once."; return o; }
            if (con.SessionId != plan.Session) { o.State = "refused"; o.Problem = "The console was connected again after the review, so the protective backup from that review no longer shows its state. Review the restore again. Nothing was written."; return o; }
            var id = con.Identify();
            if (!ConsoleIdentity.SameConsole(id.Hwid, plan.Hwid)) { o.State = "refused"; o.Problem = "A different console is connected now. Nothing was written."; return o; }
            // the backup restored from and the protective backup: held open read-only from here to the end (nothing can change, replace or
            // delete them) and verified again from scratch; any problem refuses with the confirmation still unused and nothing written
            using (var hold = Evidence.Hold.Open(plan.BackupPath, plan.ProtectivePath))
            {
                if (hold.Problem != null) { o.State = "refused"; o.Problem = "A backup this restore relies on cannot be used (" + hold.Problem + "). Nothing was written; review the restore again."; return o; }
                ctx.Say("validate", "Checking the backup and the protective backup again...");
                var why = Evidence.Recheck(plan.BackupArchive, plan.Hwid, con.Simulated, null, ctx.Cancelled) ?? Evidence.Recheck(plan.ProtectiveArchive, plan.Hwid, con.Simulated, con.SessionId, ctx.Cancelled);
                if (why != null) { o.State = "refused"; o.Problem = why + " Nothing was written; review the restore again."; return o; }
                Record rec;
                try { rec = Record.Start(ctx.Settings.RecordsFolder, Guid.NewGuid().ToString("N").Substring(0, 12), "restore", new JObj { { "plan", plan.ToJson() }, { "console_tag", plan.ConsoleTag }, { "simulated", plan.Simulated } }); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { o.State = "refused"; o.Problem = "The restore's record could not be saved in " + ctx.Settings.RecordsFolder + " (" + ex.Message + "), so nothing was written."; return o; }
                plan.Nonce = null;   // used up only now: a second Execute with the same plan is refused above
                return ExecuteHeld(con, plan, ctx, o, rec);
            }
        }

        static RestoreOutcome ExecuteHeld(RecoveryConsole con, RestorePlan plan, EngineContext ctx, RestoreOutcome o, Record rec)
        {
            o.RecordFile = rec.File;
            void Phase(string name, string state, string detail = null) { var j = new JObj { { "phase", name }, { "state", state }, { "at", Record.Now() }, { "detail", detail } }; o.Phases.Add(j); rec.Set("phases", o.Phases.Cast<object>().ToList()).Save(); }
            var auth = new WriteAuthority(plan.Digest, plan.Hwid, plan.Steps.Where(s => s.Action == "write").Select(s => s.Target).ToArray(), Boundary(con), Gate.RestoreText);
            rec.State("writing"); ctx.At("restore-writing"); con.Card("writing");
            using (var f = new FileStream(plan.BackupPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            using (var z = new ZipReader(f, BackupFormat.Allowed))
            {
                foreach (var s in plan.Steps.Where(x => x.Action == "write"))
                {
                    if (ctx.Cancelled()) { Phase("write " + s.Target, "not-started", "canceled before this part"); rec.State("stopped", "Canceled between parts."); o.State = "stopped"; o.Problem = "Canceled. Parts already written: " + (o.Written.Count == 0 ? "none" : string.Join(", ", o.Written)) + ". The console's state is not verified."; return o; }
                    var e = z.Find(s.Member);
                    Phase("write " + s.Target, "began");
                    ctx.Say("write", "Writing " + s.Label + "... (do not unplug the console)", 0, s.Size);
                    WriteResult wr;
                    try
                    {
                        Stream src = e.Method == 0 ? z.StoredWindow(e) : z.OpenMember(e);
                        wr = con.Write(s.Target, new ProgressRead(src, n => ctx.Say("write", "Writing " + s.Label + "... (do not unplug the console)", n, s.Size)), s.Size, s.BackupMd5, auth);
                    }
                    catch (GateClosedException ex) { Phase("write " + s.Target, "refused", ex.Message); rec.State("refused", ex.Message); o.State = "refused"; o.Problem = ex.Message; return o; }
                    ctx.At("restore-wrote-" + s.Target);
                    if (!wr.Ok)
                    {
                        if (wr.Began) o.Partial.Add(s.Target);
                        Phase("write " + s.Target, wr.Began ? "failed-after-start" : "refused-before-start", wr.Problem);
                        rec.State("stopped", wr.Problem); con.Card("stopped");
                        o.State = "stopped";
                        o.Problem = (wr.Began ? "Writing " + s.Label + " did not complete or did not check out (" + wr.Problem + ")." : "The console refused to write " + s.Label + " (" + wr.Problem + ").") +
                                    " Every later write was stopped. Parts fully written before: " + (o.Written.Count == 0 ? "none" : string.Join(", ", o.Written)) + ". The console's state is NOT verified. Nothing was retried. Your backup and the protective backup made just before are unchanged.";
                        return o;
                    }
                    o.Written.Add(s.Target); Phase("write " + s.Target, "written-and-read-back", wr.ReadbackMd5);
                }
            }
            // 7 readback of everything written, by the console, and its identity again
            rec.State("reading-back"); ctx.Say("readback", "Reading everything back from the console...");
            foreach (var s in plan.Steps.Where(x => x.Action == "write"))
            {
                var d = con.Digest(s.Target);
                bool ok = d.ok && d.md5 == s.BackupMd5 && (s.Target.StartsWith("boot2") ? d.bytes >= s.Size : d.bytes == s.Size);
                if (s.Target.StartsWith("nand") && d.bytes != s.Size) ok = false;
                Phase("readback " + s.Target, ok ? "matches" : "differs", d.md5);
                if (ok) o.Verified.Add(s.Target);
            }
            var after = con.Identify();
            bool idOk = ConsoleIdentity.SameConsole(after.Hwid, plan.Hwid) && after.ActiveMd5 == (plan.Steps.FirstOrDefault(x => x.Target == "boot2")?.BackupMd5 ?? after.ActiveMd5);
            if (o.Verified.Count != o.Written.Count || !idOk)
            {
                rec.State("stopped", "readback differs"); o.State = "stopped";
                o.Problem = "Everything was written, but reading it back did not match for: " + string.Join(", ", o.Written.Except(o.Verified)) + ". The console's state is NOT verified. Nothing more was written.";
                return o;
            }
            rec.State("restored"); ctx.At("restore-complete"); con.Card("done");
            o.State = "restored";
            return o;
        }

        sealed class ProgressRead : Stream
        {
            readonly Stream s; readonly Action<long> p; long n;
            public ProgressRead(Stream s, Action<long> p) { this.s = s; this.p = p; }
            public override int Read(byte[] b, int o, int c) { int r = s.Read(b, o, c); n += Math.Max(0, r); p?.Invoke(n); return r; }
            public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
            public override long Length => s.Length; public override long Position { get => n; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        }
    }
}
