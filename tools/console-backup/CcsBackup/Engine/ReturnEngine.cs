// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Return to Original Software: the way back from Classic Cart Studio's own first-time preparation (Preparation), for the console it
// prepared, without the Hakchi2 CE PC program. Two sessions, because the console's storage driver reads the storage table only when it
// starts:
//   A (this engine, in the preparation image, its own write boundary Gate.ReturnWrites)
//     1 find      the newest record of a preparation Classic Cart Studio completed on this console: the pre-preparation snapshot it took
//                 in its own session and the exact state it left (startup software, spare slot, bootloader, storage table)
//     2 check     the snapshot verified from scratch (this console, restore-ready, the original Nintendo kernel running in it); the console
//                 identified afresh and EXACTLY in that recorded state; anything else (another modification since, another kernel or
//                 bootloader, a storage table changed by something else, another console, an altered or missing backup) is refused
//     3 protect   a complete, verified backup of the console as it is now, in this session (saves made since preparing are in it)
//     4 plan      Hakchi2 CE 3.9.3's uninstall as checked steps in its order (ccsprep), with ONE difference: the bootloader written is
//                 this console's own, from the snapshot (a complete u-boot image whose bytes the plan names), not the flash tool's
//                 "default" (hakchi pipes that word into sntool 5833d00, whose source is not public: what it writes cannot be known before
//                 writing). The plan binds both backups (Evidence.BoundArchive: path, SHA-256, size, completion, console, coverage, and
//                 for the protective backup this session) and the bootloader's bytes; a one-time confirmation for exactly this plan
//     5 recheck   at the confirmation, before anything is used up or written: both backups held open read-only (nothing can change,
//                 replace or delete them until the writes end) and verified again from scratch; the bootloader read from the held
//                 snapshot. Missing, changed, unreadable, incomplete, another console's or another session's: refused, nothing written,
//                 the confirmation still unused. The operation record is saved before the first step; no record, no step.
//     6 write     ret-stage (the bootloader into the console's RAM, checked there; nothing written to storage), then each step once; the
//                 console checks that what it replaces is what was reviewed; the first failure stops every later step
//     7 verify    each step's readback compared with the snapshot: the original kernel, this console's original bootloader (both of
//                 the console's readers must agree), its original storage table
//   B (RestoreEngine, unchanged, after the console restarted into recovery mode): Restore Console Backup with the snapshot. With the
//     bootloader and storage table back, its strict checks pass, and it returns every storage part (games, saves, other programs' files)
//     to the snapshot, after its own protective backup.
// Nothing is automatic: no retry, no rollback, no erase, format or repartition beyond hakchi's own shrink, no bootloader but this
// console's own from its snapshot.
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
using System.Text.RegularExpressions;

namespace CcsBackup.Engine
{
    public sealed class ReturnPlan
    {
        public string Hwid, ConsoleTag, Session, Record, SnapshotWhen, Digest, Nonce, Refusal;
        public BoundArchive SnapshotArchive, ProtectiveArchive;
        public string KernelMd5, UbootNowMd5, UbootWantMd5, UbootWantSha256, TableNow, TableWant; public long UbootWantSize; public DateTime ExpiresUtc; public bool Simulated;
        public string Snapshot => SnapshotArchive?.Path; public string SnapshotSha256 => SnapshotArchive?.Sha256; public string ProtectivePath => ProtectiveArchive?.Path;
        public List<string> Steps = new List<string>(), Notes = new List<string>();
        public JObj ToJson() => new JObj
        {
            { "console_tag", ConsoleTag }, { "record", Record }, { "snapshot", Snapshot }, { "snapshot_sha256", SnapshotSha256 }, { "snapshot_when", SnapshotWhen }, { "protective_backup", ProtectivePath },
            { "evidence", new List<object> { SnapshotArchive?.ToJson(), ProtectiveArchive?.ToJson() } },
            { "bootloader", new JObj { { "source", "reference/boot1.bin of the backup made before preparing" }, { "md5", UbootWantMd5 }, { "sha256", UbootWantSha256 }, { "bytes", UbootWantSize } } },
            { "steps", Steps }, { "notes", Notes }, { "refusal", Refusal }, { "digest", Digest }, { "nonce", Nonce }, { "expires_utc", ExpiresUtc.ToString("o", CultureInfo.InvariantCulture) },
        };
    }

    public sealed class ReturnOutcome { public string State, Problem, Snapshot; public List<string> Done = new List<string>(); public ReturnPlan Plan; public string RecordFile; public bool Ok => State == "returned"; }

    public static class ReturnEngine
    {
        public static readonly string[] Order = { "ret-stage", "ret-kernel", "ret-uboot", "ret-files", "ret-table" };
        public static string StepLabel(string s) => s == "ret-stage" ? "this console's original bootloader sent to the console's memory and checked there (nothing written)"
            : s == "ret-kernel" ? "the original Nintendo kernel back as the startup software (from the spare slot)" : s == "ret-uboot" ? "this console's original bootloader (u-boot), from the backup made before preparing"
            : s == "ret-files" ? "hakchi's files removed from the data storage" : "the storage layout back to its original division (as hakchi's own uninstall does)";
        static readonly Regex Md5Re = new Regex("^[0-9a-f]{32}$");

        /// <summary>the newest completed Classic Cart Studio preparation of this console with its before/after state (null: none)</summary>
        static (string file, Dictionary<string, object> rec) LastPreparation(Settings s, string tag, bool simulated)
        {
            if (!Directory.Exists(s.RecordsFolder)) return (null, null);
            return Directory.GetFiles(s.RecordsFolder, "prepare-*.json").Select(f => (f, Library.Record.Load(f)))
                .Where(x => x.Item2 != null && x.Item2.S("state") == "prepared" && x.Item2.S("console_tag") == tag && x.Item2.B("simulated") == simulated && x.Item2.O("before") != null && x.Item2.O("after") != null)
                .OrderByDescending(x => x.Item2.S("updated_utc"), StringComparer.Ordinal).FirstOrDefault();
        }

        /// <summary>Phases 1 to 4 (no write to the console; the protective backup on the PC).</summary>
        public static (ReturnPlan plan, ReturnOutcome refused) Prepare(RecoveryConsole con, EngineContext ctx)
        {
            var o = new ReturnOutcome();
            if (con.Profile != SessionProfile.Prepare) { o.State = "refused"; o.Problem = "The console is not in a preparation session."; return (null, o); }
            ConsoleIdentity id;
            try { id = con.Identify(); Support.CheckForBackup(id, ctx.RecoveryImageId, "return"); }
            catch (ConsoleRefusedException ex) { o.State = "refused"; o.Problem = ex.Message; return (null, o); }
            var (file, rec) = LastPreparation(ctx.Settings, id.ShortTag, con.Simulated);
            if (rec == null) { o.State = "refused"; o.Problem = "No preparation by Classic Cart Studio is recorded for this console (or it was made by an earlier version, which did not keep what the way back needs). Returning it is not something this version does; Workbench can still back it up."; return (null, o); }
            var before = rec.O("before"); var after = rec.O("after");
            var p = new ReturnPlan { Hwid = id.Hwid, ConsoleTag = id.ShortTag, Session = con.SessionId, Record = file, Simulated = con.Simulated };
            string snapPath = before.S("snapshot"), snapSha = before.S("snapshot_sha256");
            ReturnOutcome Refuse(string why) { o.State = "refused"; o.Problem = why; return o; }
            // 2 check: the snapshot, from scratch
            if (string.IsNullOrEmpty(snapPath) || !File.Exists(snapPath)) return (null, Refuse("The backup made before preparing (" + (snapPath ?? "not recorded") + ") is not there. Returning the console needs it."));
            ctx.Say("validate", "Checking the backup made before preparing...");
            var v = Verifier.VerifyFile(snapPath, null, ctx.Cancelled);
            if (!v.Ok || v.ArchiveSha256 != snapSha) return (null, Refuse("The backup made before preparing no longer passes its check or was changed since. Returning the console needs it exactly."));
            if (!ConsoleIdentity.SameConsole(v.Hwid, id.Hwid)) return (null, Refuse("The backup made before preparing belongs to a different console."));
            if (v.Simulated != con.Simulated) return (null, Refuse("A simulated console's backup and a real console are never mixed."));
            if (!v.RestoreReady || !v.Members.TryGetValue("restore/boot2-active.img", out var sk) || !v.Members.TryGetValue("reference/boot1.bin", out var sb)) return (null, Refuse("The backup made before preparing does not hold everything the way back needs."));
            p.SnapshotArchive = BoundArchive.From("backup made before preparing", snapPath, v);
            p.KernelMd5 = sk.md5; p.UbootWantMd5 = sb.md5; p.UbootWantSha256 = sb.sha256; p.UbootWantSize = sb.size; p.TableWant = v.Manifest.O("layout")?.S("partition_table"); p.SnapshotWhen = v.Manifest.S("completed_utc");
            // this console's own bootloader, as the snapshot holds it: the exact bytes the way back writes, known now, before anything
            if (Boot1Of(snapPath, p, out var bWhy) == null) return (null, Refuse("The backup made before preparing does not hold a complete copy of this console's own bootloader (" + bWhy + "). The way back writes only that copy, so it is refused before anything changes; Workbench can still back the console up."));
            if (!KnownKernels.IsStock(p.KernelMd5) ||   // an NES Classic's: a Super NES Classic's return is not available yet (snes-classic-return)
                before.S("active_md5") != p.KernelMd5) return (null, Refuse("The backup made before preparing does not show the original Nintendo kernel running. The way back returns only to that."));
            if (string.IsNullOrEmpty(p.TableWant)) return (null, Refuse("The backup made before preparing does not record the storage table."));
            // the console now: exactly as the preparation left it
            p.UbootNowMd5 = after.S("boot1_md5"); p.TableNow = after.S("parttable");
            var b1 = con.Digest("boot1");
            var diff = new List<string>();
            if (id.ActiveMd5 != after.S("active_md5") || id.ActiveHakchiEntries <= 0) diff.Add("its startup software");
            if (!id.Slot68Valid || id.Slot68Md5 != after.S("slot68_md5") || id.Slot68Md5 != p.KernelMd5) diff.Add("the original kernel's copy in the spare slot");
            if (!b1.ok || !Md5Re.IsMatch(p.UbootNowMd5 ?? "") || b1.md5 != p.UbootNowMd5) diff.Add("its bootloader");
            if (id.Get("parttable") != p.TableNow) diff.Add("its storage table");
            if (diff.Count > 0) return (null, Refuse("This console is not exactly as Classic Cart Studio's preparation left it (" + string.Join(", ", diff) + " changed since). Returning it is refused: something else modified it, and the way back is only for the state Classic Cart Studio made. Workbench can still back it up."));
            // 3 protect: the console as it is now, in this session
            ctx.Say("protect", "Backing up the console as it is now, before anything changes...");
            var prot = CaptureEngine.Run(con, ctx);
            if (!prot.Ok || prot.Verification == null || !prot.Verification.Ok) { o.State = "stopped"; o.Problem = "The backup of the console as it is now did not complete (" + (prot.Problem ?? prot.State) + "), so nothing was written."; return (null, o); }
            if (!ConsoleIdentity.SameConsole(prot.Verification.Hwid, id.Hwid) || prot.Verification.Simulated != con.Simulated || !prot.Verification.RestoreReady) { o.State = "refused"; o.Problem = "The backup made just now is not a complete backup of this console, so nothing was written."; return (null, o); }
            p.ProtectiveArchive = BoundArchive.From("backup made just now, before returning", prot.PrimaryPath, prot.Verification, con.SessionId);
            p.Steps.AddRange(Order);
            p.Notes.Add("This undoes Classic Cart Studio's preparation the way Hakchi2 CE 3.9.3's own uninstall does: Nintendo's kernel, hakchi's files removed, the storage table divided as before. The bootloader written is this console's own, exactly as the backup made before preparing holds it (" + p.UbootWantSize.ToString("N0", CultureInfo.GetCultureInfo("en-US")) + " bytes), not the flash tool's default.");
            p.Notes.Add("Then the console restarts, and Restore a Backup puts back its storage exactly as in the backup made before preparing (" + When(p.SnapshotWhen) + "): Nintendo's HOME, the 30 games and every save as they were then, and anything other programs had added. Classic Cart Studio's HOME is gone after that.");
            p.Notes.Add("Saves made since preparing are in the backup just made, not on the console after the restore.");
            p.Notes.Add("Both backups are checked again when you confirm and stay locked until the return ends; if either one is missing or changed, nothing is written.");
            p.Nonce = Hex.Of(Guid.NewGuid().ToByteArray()); p.ExpiresUtc = DateTime.UtcNow + RestoreEngine.ConfirmWindow;
            p.Digest = DigestOf(p);
            return (p, null);
        }

        static string When(string utc) => DateTime.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t.ToLocalTime().ToString("MMMM d, yyyy h:mm tt", CultureInfo.GetCultureInfo("en-US")) : "a date not recorded";

        static string DigestOf(ReturnPlan p)
        {
            using (var h = SHA256.Create())
                return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes("ccs-return-plan/2\n" + ConsoleIdentity.NormalizeHwid(p.Hwid) + "\n" + p.Session + "\n" + p.SnapshotArchive?.DigestLine() + "\n" + p.ProtectiveArchive?.DigestLine() + "\n" +
                    p.KernelMd5 + "\n" + p.UbootNowMd5 + "\n" + p.UbootWantMd5 + " " + p.UbootWantSha256 + " " + p.UbootWantSize.ToString(CultureInfo.InvariantCulture) + "\n" + p.TableNow + "\n" + p.TableWant + "\n" + string.Join(",", p.Steps) + "\n" + p.Nonce)));
        }

        /// <summary>the snapshot's reference/boot1.bin, read and checked: exactly the plan's size and hashes, and a complete u-boot image
        /// (as ccsboot checks it on the console); null with the reason otherwise</summary>
        static byte[] Boot1Of(string snapshot, ReturnPlan p, out string why)
        {
            why = null;
            try
            {
                using (var z = ZipReader.Open(snapshot, BackupFormat.Allowed))
                {
                    var e = z.Find("reference/boot1.bin"); if (e == null) { why = "no bootloader member"; return null; }
                    if (e.Size != p.UbootWantSize || e.Size <= 0 || e.Size > BootImage.Boot1Max) { why = "its size is not a bootloader's"; return null; }
                    var b = new byte[e.Size]; using (var st = z.OpenMember(e)) { int n = 0, k; while (n < b.Length && (k = st.Read(b, n, b.Length - n)) > 0) n += k; if (n != b.Length) { why = "it could not be read completely"; return null; } }
                    using (var md5 = MD5.Create()) using (var sha = SHA256.Create())
                        if (Hex.Of(md5.ComputeHash(b)) != p.UbootWantMd5 || Hex.Of(sha.ComputeHash(b)) != p.UbootWantSha256) { why = "its bytes differ from the backup's record of them"; return null; }
                    why = BootImage.Check(b, false);
                    return why == null ? b : null;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException) { why = "it could not be read: " + ex.Message; return null; }
        }

        /// <summary>Phases 5 to 7: only with the plan's own digest and nonce, in its session, before it expires, once. Every check that can
        /// refuse runs before the confirmation is used up and before the first step: the console, both backups (held open read-only from
        /// here to the last step and verified again from scratch), the bootloader's bytes and the operation record.</summary>
        public static ReturnOutcome Execute(RecoveryConsole con, ReturnPlan plan, string digest, string nonce, EngineContext ctx)
        {
            var o = new ReturnOutcome { Plan = plan, Snapshot = plan?.Snapshot };
            ReturnOutcome Refuse(string why) { o.State = "refused"; o.Problem = why; return o; }
            if (plan == null || plan.Refusal != null) return Refuse(plan?.Refusal ?? "No plan.");
            if (digest != plan.Digest || nonce != plan.Nonce || plan.Nonce == null || DigestOf(plan) != plan.Digest) return Refuse("The confirmation does not match the reviewed return.");
            if (DateTime.UtcNow > plan.ExpiresUtc) return Refuse("The confirmation is more than 10 minutes old. Review the return again.");
            if (con.SessionId != plan.Session || plan.ProtectiveArchive?.Session != plan.Session) return Refuse("The console was connected again after the review. Review the return again. Nothing was changed.");
            var id = con.Identify();
            if (!ConsoleIdentity.SameConsole(id.Hwid, plan.Hwid)) return Refuse("A different console is connected now. Nothing was changed.");
            using (var hold = Evidence.Hold.Open(plan.Snapshot, plan.ProtectivePath))
            {
                if (hold.Problem != null) return Refuse("A backup the return relies on cannot be used (" + hold.Problem + "). Nothing was changed; review the return again.");
                ctx.Say("validate", "Checking both backups again...");
                var why = Evidence.Recheck(plan.SnapshotArchive, plan.Hwid, con.Simulated, null, ctx.Cancelled) ?? Evidence.Recheck(plan.ProtectiveArchive, plan.Hwid, con.Simulated, con.SessionId, ctx.Cancelled);
                if (why != null) return Refuse(why + " Nothing was changed; review the return again.");
                var boot1 = Boot1Of(plan.Snapshot, plan, out var bWhy);
                if (boot1 == null) return Refuse("This console's own bootloader could not be taken from the backup made before preparing (" + bWhy + "). Nothing was changed.");
                Library.Record rec;
                try { rec = Library.Record.Start(ctx.Settings.RecordsFolder, Guid.NewGuid().ToString("N").Substring(0, 12), "return", new JObj { { "plan", plan.ToJson() }, { "console_tag", plan.ConsoleTag }, { "simulated", con.Simulated } }); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Refuse("The return's record could not be saved in " + ctx.Settings.RecordsFolder + " (" + ex.Message + "), so nothing was written."); }
                plan.Nonce = null;   // used up only now: everything above refuses without using it
                o.RecordFile = rec.File; var phases = new List<object>();
                var auth = new WriteAuthority(plan.Digest, plan.Hwid, plan.Steps.ToArray(), () => con.Simulated || Gate.ReturnWrites, Gate.ReturnText);
                rec.State("writing"); con.Card("writing");
                ReturnOutcome Stop(string step, string reason)
                {
                    auth.Revoke(step + ": " + reason); rec.State("stopped", reason); con.Card("stopped"); o.State = "stopped";
                    o.Problem = "Returning stopped at " + StepLabel(step) + " (" + reason + "). Nothing more was changed and nothing was retried. Steps done before: " + (o.Done.Count == 0 ? "none" : string.Join(", ", o.Done.Select(StepLabel))) + ". The backup made before preparing and the one made just now are unchanged.";
                    return o;
                }
                foreach (var s in plan.Steps)
                {
                    if (ctx.Cancelled()) { rec.State("stopped", "canceled between steps"); o.State = "stopped"; o.Problem = "Canceled. Steps done: " + (o.Done.Count == 0 ? "none" : string.Join(", ", o.Done.Select(StepLabel))) + ". The console's state is not verified."; return o; }
                    ctx.Say("prepare", "Returning: " + StepLabel(s) + "... (do not unplug the console)");
                    phases.Add(new JObj { { "step", s }, { "state", "began" }, { "at", Library.Record.Now() } }); rec.Set("phases", phases).Save();
                    WriteResult r;
                    try
                    {
                        r = s == "ret-stage" ? con.PrepStep(s, auth, new[] { plan.UbootWantSize.ToString(CultureInfo.InvariantCulture), plan.UbootWantMd5 }, boot1)
                          : s == "ret-kernel" ? con.PrepStep(s, auth, new[] { plan.KernelMd5 })
                          : s == "ret-uboot" ? con.PrepStep(s, auth, new[] { plan.UbootNowMd5, plan.UbootWantMd5 })
                          : con.PrepStep(s, auth);
                    }
                    catch (GateClosedException ex) { rec.State("refused", ex.Message); o.State = "refused"; o.Problem = ex.Message; return o; }
                    phases.Add(new JObj { { "step", s }, { "state", !r.Ok ? "failed" : s == "ret-stage" ? "checked-in-memory" : "done-and-read-back" }, { "detail", r.Line ?? r.Problem }, { "at", Library.Record.Now() } }); rec.Set("phases", phases).Save();
                    ctx.At("return-" + s);
                    if (!r.Ok) return Stop(s, r.Problem);
                    var got = Regex.Replace(r.Line ?? "", "^result " + Regex.Escape(s) + " ok( already)? ?", "");
                    if (s == "ret-stage" && got != plan.UbootWantMd5 + " " + plan.UbootWantSize.ToString(CultureInfo.InvariantCulture)) return Stop(s, "the console received " + got + ", not the reviewed bootloader");
                    if (s == "ret-kernel" && got != plan.KernelMd5) return Stop(s, "the startup software reads back as " + got + ", not the original kernel");
                    if (s == "ret-uboot" && got != plan.UbootWantMd5) return Stop(s, "the bootloader reads back as " + got + ", not this console's original (" + plan.UbootWantMd5 + ")");
                    if (s == "ret-table") { var tb = con.Identify().Get("parttable"); if (tb != plan.TableWant) return Stop(s, "the storage table reads back as \"" + tb + "\", not as in the backup made before preparing"); }
                    if (s != "ret-stage") o.Done.Add(s);
                }
                var after = con.Identify(); var b1 = con.Digest("boot1");
                if (!ConsoleIdentity.SameConsole(after.Hwid, plan.Hwid) || after.ActiveMd5 != plan.KernelMd5 || after.Get("parttable") != plan.TableWant || !b1.ok || b1.md5 != plan.UbootWantMd5)
                { rec.State("stopped", "the console does not read back as returned"); o.State = "stopped"; o.Problem = "Every step reported success, but the console does not read back as returned. Its state is not verified; nothing more was changed."; return o; }
                rec.Set("after", new JObj { { "active_md5", after.ActiveMd5 }, { "parttable", after.Get("parttable") }, { "boot1_md5", b1.md5 } });
                rec.State("returned"); con.Card("done"); o.State = "returned"; return o;
            }
        }
    }
}
