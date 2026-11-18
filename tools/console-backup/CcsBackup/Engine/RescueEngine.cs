// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Console Rescue for the console's boot stages: this console's own boot0 and/or boot1 (u-boot) written back from a
// verified backup of THIS console, in the rescue image (ram/ccsrescue, ccsboot), under its own write boundary Gate.RescueWrites.
//   1 diagnose   read-only: each stage read by ccsboot and by the image's flash tool: its MD5, or ccsboot's typed result (ram/ccsrescue):
//                invalid (read, checksum or structure wrong), noimage (read, no recognizable image), unreadable (the driver answers, a
//                block cannot be read) or nodriver (the NAND driver did not answer: nothing is known, and no repair is ever offered on
//                that basis). The flash tool says only its MD5 or "damaged". A black screen alone is never taken as a damaged stage.
//   2 source     a backup of this console (same hardware id), verified from scratch, holding complete valid images of the chosen stages
//                (BootImage.Check), not recorded as damaged in that backup. Another console's backup, a downloaded file, a stage from
//                another console or a generic board image are refused: no cross-console boot transplant, no geometry guessed.
//   3 protect    a backup of the console as it is now, in this session. A stage the diagnosis found damaged is kept as read (or recorded as
//                unreadable) and that backup is never a recovery baseline; the owner acknowledges, in the exact words shown, what could
//                not be preserved. Everything else must read cleanly.
//   4 plan       per stage: what is on the console now (MD5 or "damaged"), the exact bytes to write (size, MD5, SHA-256), the expected
//                readback (the same bytes; for boot0 the driver's own boot0 writer may adjust NAND parameters, and any difference stops
//                and is reported); both backups bound by hash (Evidence); a one-time confirmation for exactly this plan
//   5 recheck    at confirmation, before anything is used up or written: the console, both backups held open read-only and verified
//                again, the stage bytes read from the held source; then the operation record; no record, no step
//   6 write      boot1 first, then boot0 (the stage the console starts with last): stage into RAM, checked there; burn once; read back;
//                the first failure stops everything; nothing is retried, rolled back or written automatically afterwards. The writer stops at
//                a block it cannot read before writing it; only a stage diagnosed unreadable, and acknowledged as such, is written by its
//                explicit unreadable mode (each block still read back). After a stop, the stages are read again, read-only, and reported.
//   7 verify     the diagnosis again: every repaired stage must read back exactly as planned. The owner then switches the console off and
//                on (a cold start): only that, and the normal HOME, games and saves, show the repair worked.
// Never written here: the kernel, the storage table, the data storage, the physical NAND, another console's stages, custom boot code.
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
    public sealed class RescueDiagnosis
    {
        public string Boot0, Boot1, Boot1FlashTool; public long Boot0Size, Boot1Size;
        /// <summary>the typed words ccsboot's reader can give instead of an MD5 (ram/ccsrescue); nodriver is never a stage's condition</summary>
        public static readonly string[] DamageWords = { "invalid", "noimage", "unreadable" };
        public static bool IsMd5(string s) => s != null && s.Length == 32 && s.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
        public string StateOf(string stage) => stage == "boot0" ? Boot0 : Boot1;
        public bool Damaged(string stage) => DamageWords.Contains(StateOf(stage));
        public bool Unreadable(string stage) => StateOf(stage) == "unreadable";
        /// <summary>the driver did not answer while a stage was read: nothing about the stages is known</summary>
        public bool NoDriver => Boot0 == "nodriver" || Boot1 == "nodriver";
        /// <summary>the two boot1 readers do not say the same thing: a valid image by one and damaged by the other, or two MD5s</summary>
        public bool ReadersDisagree => IsMd5(Boot1) ? Boot1FlashTool != Boot1 : Damaged("boot1") && Boot1FlashTool != "damaged";
        public List<string> DamagedStages => new[] { "boot0", "boot1" }.Where(Damaged).ToList();
        public static string Words(string state) => IsMd5(state) ? "reads as a valid image" : state == "invalid" ? "damaged (its checksum or structure does not hold)" : state == "noimage" ? "damaged (no startup image is there)"
            : state == "unreadable" ? "cannot be read" : state == "nodriver" ? "not known (the console's storage driver did not answer)" : state;
        public string Text => "First stage (boot0): " + Words(Boot0) + ". Bootloader (boot1, u-boot): " + Words(Boot1) + (ReadersDisagree ? " (the console's two readers disagree)" : "") + ".";
        public JObj ToJson() => new JObj { { "boot0", Boot0 }, { "boot0_size", Boot0Size }, { "boot1", Boot1 }, { "boot1_flash_tool", Boot1FlashTool }, { "boot1_size", Boot1Size } };
    }

    public sealed class RescueStage { public string Stage, NowMd5, WantMd5, WantSha256; public long Size; public bool Change => NowMd5 != WantMd5; }

    public sealed class RescuePlan
    {
        public string Hwid, ConsoleTag, Session, Digest, Nonce, Refusal, AcknowledgedText; public DateTime ExpiresUtc; public bool Simulated;
        public BoundArchive SourceArchive, ProtectiveArchive; public RescueDiagnosis Diagnosis;
        public List<RescueStage> Stages = new List<RescueStage>(); public List<string> Notes = new List<string>(), Unpreserved = new List<string>();
        public List<string> Steps => Stages.Where(s => s.Change).SelectMany(s => new[] { "stage" + s.Stage.Substring(4), "burn" + s.Stage.Substring(4) }).ToList();
        public JObj ToJson() => new JObj
        {
            { "console_tag", ConsoleTag }, { "diagnosis", Diagnosis?.ToJson() }, { "evidence", new List<object> { SourceArchive?.ToJson(), ProtectiveArchive?.ToJson() } },
            { "stages", Stages.Select(s => (object)new JObj { { "stage", s.Stage }, { "now", s.NowMd5 }, { "want_md5", s.WantMd5 }, { "want_sha256", s.WantSha256 }, { "bytes", s.Size }, { "change", s.Change } }).ToList() },
            { "steps", Steps }, { "not_preserved", Unpreserved }, { "acknowledged", AcknowledgedText }, { "notes", Notes }, { "refusal", Refusal },
            { "digest", Digest }, { "nonce", Nonce }, { "expires_utc", ExpiresUtc.ToString("o", CultureInfo.InvariantCulture) },
        };
    }

    public sealed class RescueOutcome { public string State, Problem, RecordFile; public RescuePlan Plan; public List<string> Done = new List<string>(); public bool Ok => State == "rescued"; }

    public static class RescueEngine
    {
        public static readonly string[] StageOrder = { "boot1", "boot0" };
        public static string StageLabel(string s) => s == "boot0" ? "the first startup stage (boot0)" : "the bootloader (boot1, u-boot)";

        /// <summary>the exact words the owner types when the protective backup cannot hold a damaged stage as it was; a stage that cannot be
        /// read at all is named as such, because it is then written without a comparison first (each block still read back)</summary>
        public static string AcknowledgmentFor(IEnumerable<string> damaged, RescueDiagnosis dg = null)
        {
            var list = damaged.ToList(); var unread = dg == null ? new List<string>() : list.Where(dg.Unreadable).ToList();
            var s = "I understand that " + string.Join(" and ", list.Select(StageLabel)) + " cannot be saved as it was";
            return unread.Count == 0 ? s : s + ", and that " + string.Join(" and ", unread.Select(StageLabel)) + " will be written without being read first";
        }

        public static RescueDiagnosis Diagnose(RecoveryConsole con)
        {
            var d = con.RescueDiagnose();
            long L(string k) => d.TryGetValue(k, out var v) && long.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
            return new RescueDiagnosis { Boot0 = d["boot0"], Boot1 = d["boot1"], Boot1FlashTool = d["boot1_flash_tool"], Boot0Size = L("boot0_size"), Boot1Size = L("boot1_size") };
        }

        /// <summary>Phases 1 to 4 (no write but the protective backup on the PC). stages: "boot0", "boot1" or both. acknowledgment: the exact
        /// words of AcknowledgmentFor when a stage is damaged (null otherwise).</summary>
        public static (RescuePlan plan, RescueOutcome refused) Prepare(RecoveryConsole con, EngineContext ctx, string sourceBackup, IEnumerable<string> stages, string acknowledgment)
        {
            var o = new RescueOutcome();
            RescueOutcome Refuse(string why) { o.State = "refused"; o.Problem = why; return o; }
            if (con.Profile != SessionProfile.Rescue) return (null, Refuse("The console is not in a rescue session."));
            var want = (stages ?? new string[0]).Distinct().ToList();
            if (want.Count == 0 || want.Any(s => s != "boot0" && s != "boot1")) return (null, Refuse("Choose the startup stage to repair: the first stage (boot0), the bootloader (boot1) or both."));
            ConsoleIdentity id;
            try { id = con.Identify(); }
            catch (ConsoleRefusedException ex) { return (null, Refuse(ex.Message)); }
            if (string.IsNullOrEmpty(id.Hwid)) return (null, Refuse("The console's identity could not be read, so no backup can be matched to it."));
            ctx.Say("diagnose", "Checking the console's startup stages...");
            RescueDiagnosis dg;
            try { dg = Diagnose(con); } catch (ConsoleRefusedException ex) { return (null, Refuse(ex.Message)); }
            // a driver that did not answer, or readers that disagree, never justify a repair: the stage's condition is not known
            var blocked = Blocked(dg); if (blocked != null) return (null, Refuse(blocked));
            // 2 source: this console's own verified backup with complete, valid stage images
            if (string.IsNullOrEmpty(sourceBackup) || !File.Exists(sourceBackup)) return (null, Refuse("Choose a backup of this console to take the startup stage from."));
            ctx.Say("validate", "Checking the backup...");
            var v = Verifier.VerifyFile(sourceBackup, null, ctx.Cancelled);
            if (!v.Ok) return (null, Refuse("This backup did not pass its check: " + v.Problems[0]));
            if (!ConsoleIdentity.SameConsole(v.Hwid, id.Hwid)) return (null, Refuse("This backup belongs to a different console (console " + v.ConsoleTag + "). A startup stage is only ever taken from a backup of the same console."));
            if (v.Simulated != con.Simulated) return (null, Refuse("A simulated console's backup and a real console are never mixed."));
            var p = new RescuePlan { Hwid = id.Hwid, ConsoleTag = id.ShortTag, Session = con.SessionId, Simulated = con.Simulated, Diagnosis = dg, SourceArchive = BoundArchive.From("backup the startup stage comes from", sourceBackup, v) };
            foreach (var s in StageOrder.Where(want.Contains))
            {
                if (v.DamagedComponents.ContainsKey(s)) return (null, Refuse("This backup recorded " + StageLabel(s) + " as damaged when it was made, so it cannot supply it."));
                var bytes = StageBytes(sourceBackup, s, v, out var why);
                if (bytes == null) return (null, Refuse("This backup does not hold a complete copy of " + StageLabel(s) + " (" + why + ")."));
                p.Stages.Add(new RescueStage { Stage = s, NowMd5 = dg.StateOf(s), WantMd5 = Md5(bytes), WantSha256 = Sha(bytes), Size = bytes.Length });
            }
            if (!p.Stages.Any(s => s.Change)) { p.Refusal = "The chosen startup stages already match this backup exactly: nothing needs to be written."; return (p, null); }
            // 3 protect: the console as it is now; a damaged stage kept as read, and acknowledged in exact words
            var damaged = dg.DamagedStages;
            if (damaged.Count > 0)
            {
                var need = AcknowledgmentFor(damaged, dg);
                if (acknowledgment != need) return (null, Refuse("A damaged startup stage cannot be saved as it was. To continue, type exactly: " + need));
                p.AcknowledgedText = need;
            }
            ctx.Say("protect", "Backing up the console as it is now, before anything changes...");
            var prot = CaptureEngine.Run(con, ctx, new CaptureOptions { DamagedStages = new HashSet<string>(damaged) });
            if (!prot.Ok || prot.Verification == null || !prot.Verification.Ok) { o.State = "stopped"; o.Problem = "The backup of the console as it is now did not complete (" + (prot.Problem ?? prot.State) + "), so nothing was written."; return (null, o); }
            var pv = prot.Verification;
            if (!ConsoleIdentity.SameConsole(pv.Hwid, id.Hwid) || pv.Simulated != con.Simulated || !pv.RestoreReady) { o.State = "refused"; o.Problem = "The backup made just now is not a complete backup of this console, so nothing was written."; return (null, o); }
            foreach (var kv in pv.DamagedComponents) p.Unpreserved.Add(StageLabel(kv.Key) + (kv.Value == "unreadable" ? ": could not be read at all" : ": kept only as read (damaged)"));
            p.ProtectiveArchive = BoundArchive.From("backup made just now, before the repair", prot.PrimaryPath, pv, con.SessionId);
            p.Notes.Add("Only " + string.Join(" and ", p.Stages.Where(s => s.Change).Select(s => StageLabel(s.Stage))) + " " + (p.Stages.Count(s => s.Change) > 1 ? "are" : "is") + " written, from the backup of this console made " + When(v.Manifest.S("completed_utc")) + ". The kernel, the storage and everything else stay as they are.");
            foreach (var s in p.Stages.Where(x => x.Change && x.NowMd5 == "unreadable"))
                p.Notes.Add(Cap(StageLabel(s.Stage)) + " cannot be read, so it is written without comparing it first: every block is still read back after it is written, and the first block that does not read back exactly stops the repair.");
            if (p.Stages.Any(s => s.Change && s.Stage == "boot0")) p.Notes.Add("The first stage is written through the console's own boot0 writer, which may adjust it for this console's storage. It must read back exactly as in your backup; if it does not, the repair stops there and says so.");
            p.Notes.Add("Afterwards, switch the console off and on again. Only a normal start, with your games and saves, shows that the repair worked.");
            p.Nonce = Hex.Of(Guid.NewGuid().ToByteArray()); p.ExpiresUtc = DateTime.UtcNow + RestoreEngine.ConfirmWindow;
            p.Digest = DigestOf(p);
            return (p, null);
        }

        /// <summary>why no repair can be offered on this diagnosis (null when one can): the driver did not answer, or the readers disagree</summary>
        public static string Blocked(RescueDiagnosis dg)
        {
            if (dg.NoDriver) return "The console's storage driver did not answer while the startup stages were read, so their condition is not known. Nothing is repaired on that basis. Unplug the console, start it in its recovery mode again and check again.";
            if (dg.ReadersDisagree) return "The console's two readers do not agree about the bootloader (boot1): " + Words2(dg) + ". Console Rescue writes only when both agree. Nothing was changed.";
            return null;
        }
        static string Cap(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);
        static string Words2(RescueDiagnosis dg) => "ccsboot says it " + RescueDiagnosis.Words(dg.Boot1) + ", the flash tool " + (dg.Boot1FlashTool == "damaged" ? "says it is damaged" : RescueDiagnosis.IsMd5(dg.Boot1FlashTool) ? "reads a valid image" : dg.Boot1FlashTool);
        static string When(string utc) => DateTime.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t.ToLocalTime().ToString("MMMM d, yyyy h:mm tt", CultureInfo.GetCultureInfo("en-US")) : "on a date not recorded";
        static string Md5(byte[] b) { using (var h = MD5.Create()) return Hex.Of(h.ComputeHash(b)); }
        static string Sha(byte[] b) { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(b)); }

        static string DigestOf(RescuePlan p)
        {
            var sb = new StringBuilder("ccs-rescue-plan/1\n").Append(ConsoleIdentity.NormalizeHwid(p.Hwid)).Append('\n').Append(p.Session).Append('\n')
                .Append(p.SourceArchive?.DigestLine()).Append('\n').Append(p.ProtectiveArchive?.DigestLine()).Append('\n').Append(p.AcknowledgedText).Append('\n');
            foreach (var s in p.Stages) sb.Append(s.Stage).Append(' ').Append(s.NowMd5).Append(' ').Append(s.WantMd5).Append(' ').Append(s.WantSha256).Append(' ').Append(s.Size.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(p.Nonce);
            using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        /// <summary>a stage's bytes from the backup (reference/boot0.bin or boot1.bin): its recorded size and hashes, and a complete valid image</summary>
        static byte[] StageBytes(string backup, string stage, Verification v, out string why)
        {
            why = null; var name = "reference/" + stage + ".bin";
            if (!v.Members.TryGetValue(name, out var m)) { why = "no " + stage + " member"; return null; }
            try
            {
                using (var z = ZipReader.Open(backup, BackupFormat.Allowed))
                {
                    var e = z.Find(name); if (e == null || e.Size != m.size || e.Size <= 0 || e.Size > BootImage.Boot1Max) { why = "its size is not a startup stage's"; return null; }
                    var b = new byte[e.Size]; using (var st = z.OpenMember(e)) { int n = 0, k; while (n < b.Length && (k = st.Read(b, n, b.Length - n)) > 0) n += k; if (n != b.Length) { why = "it could not be read completely"; return null; } }
                    if (Md5(b) != m.md5 || Sha(b) != m.sha256) { why = "its bytes differ from the backup's record of them"; return null; }
                    why = BootImage.Check(b, stage == "boot0");
                    return why == null ? b : null;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException) { why = "it could not be read: " + ex.Message; return null; }
        }

        /// <summary>Phases 5 to 7: only with the plan's own digest and nonce, in its session, before it expires, once.</summary>
        public static RescueOutcome Execute(RecoveryConsole con, RescuePlan plan, string digest, string nonce, EngineContext ctx)
        {
            var o = new RescueOutcome { Plan = plan };
            RescueOutcome Refuse(string why) { o.State = "refused"; o.Problem = why; return o; }
            if (plan == null || plan.Refusal != null) return Refuse(plan?.Refusal ?? "No plan.");
            if (digest != plan.Digest || nonce != plan.Nonce || plan.Nonce == null || DigestOf(plan) != plan.Digest) return Refuse("The confirmation does not match the reviewed repair.");
            if (DateTime.UtcNow > plan.ExpiresUtc) return Refuse("The confirmation is more than 10 minutes old. Review the repair again.");
            if (con.SessionId != plan.Session || plan.ProtectiveArchive?.Session != plan.Session) return Refuse("The console was connected again after the review. Review the repair again. Nothing was changed.");
            var id = con.Identify();
            if (!ConsoleIdentity.SameConsole(id.Hwid, plan.Hwid)) return Refuse("A different console is connected now. Nothing was changed.");
            var dg = Diagnose(con);
            if (dg.NoDriver) return Refuse("The console's storage driver did not answer, so the startup stages cannot be checked again. Nothing was changed.");
            foreach (var s in plan.Stages.Where(x => x.Change))
            { if (dg.StateOf(s.Stage) != s.NowMd5) return Refuse(StageLabel(s.Stage) + " changed since the review. Review the repair again. Nothing was changed."); }
            using (var hold = Evidence.Hold.Open(plan.SourceArchive?.Path, plan.ProtectiveArchive?.Path))
            {
                if (hold.Problem != null) return Refuse("A backup the repair relies on cannot be used (" + hold.Problem + "). Nothing was changed; review the repair again.");
                ctx.Say("validate", "Checking both backups again...");
                var why = Evidence.Recheck(plan.SourceArchive, plan.Hwid, con.Simulated, null, ctx.Cancelled) ?? Evidence.Recheck(plan.ProtectiveArchive, plan.Hwid, con.Simulated, con.SessionId, ctx.Cancelled);
                if (why != null) return Refuse(why + " Nothing was changed; review the repair again.");
                var sv = Verifier.VerifyFile(plan.SourceArchive.Path, null, ctx.Cancelled); var bytes = new Dictionary<string, byte[]>();
                foreach (var s in plan.Stages.Where(x => x.Change))
                {
                    var b = StageBytes(plan.SourceArchive.Path, s.Stage, sv, out var bw);
                    if (b == null || Md5(b) != s.WantMd5 || Sha(b) != s.WantSha256 || b.Length != s.Size) return Refuse(StageLabel(s.Stage) + " could not be taken from the backup exactly as reviewed (" + (bw ?? "different bytes") + "). Nothing was changed.");
                    bytes[s.Stage] = b;
                }
                Record rec;
                try { rec = Record.Start(ctx.Settings.RecordsFolder, Guid.NewGuid().ToString("N").Substring(0, 12), "rescue", new JObj { { "plan", plan.ToJson() }, { "console_tag", plan.ConsoleTag }, { "simulated", con.Simulated } }); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Refuse("The repair's record could not be saved in " + ctx.Settings.RecordsFolder + " (" + ex.Message + "), so nothing was written."); }
                plan.Nonce = null;   // used up only now
                o.RecordFile = rec.File; var phases = new List<object>();
                var auth = new WriteAuthority(plan.Digest, plan.Hwid, plan.Steps.ToArray(), () => con.Simulated || Gate.RescueWrites, Gate.RescueText);
                rec.State("writing"); con.Card("writing");
                RescueOutcome Stop(string step, string reason)
                {
                    auth.Revoke(step + ": " + reason); rec.State("stopped", reason); con.Card("stopped"); o.State = "stopped";
                    // read-only reconciliation: what each stage reads as now (a partly written stage is reported as it reads, never assumed)
                    string now = null;
                    try { var a = Diagnose(con); rec.Set("after_stop", a.ToJson()).Save(); now = a.Text; } catch (Exception ex) when (ex is ConsoleRefusedException || ex is IOException) { now = "The stages could not be read again (" + ex.Message + ")."; }
                    var partly = Regex.Match(reason ?? "", @"stopped ([1-9][0-9]*)"); var part = partly.Success ? " " + partly.Groups[1].Value + " block(s) of it were written before the stop, so it may be partly written." : "";
                    o.Problem = "The repair stopped at " + step + " (" + reason + ")." + part + " Nothing more was changed and nothing was retried. Stages repaired before: " + (o.Done.Count == 0 ? "none" : string.Join(", ", o.Done.Select(StageLabel))) + ". Read again just now: " + now + " Both backups are unchanged. Do not switch the console off until you have read the next steps on the PC.";
                    return o;
                }
                foreach (var s in plan.Stages.Where(x => x.Change))
                {
                    var k = s.Stage.Substring(4);
                    foreach (var step in new[] { "stage" + k, "burn" + k })
                    {
                        if (ctx.Cancelled() && step.StartsWith("stage")) { rec.State("stopped", "canceled between stages"); o.State = "stopped"; o.Problem = "Canceled before " + StageLabel(s.Stage) + ". Stages repaired: " + (o.Done.Count == 0 ? "none" : string.Join(", ", o.Done.Select(StageLabel))) + "."; return o; }
                        ctx.Say("rescue", (step.StartsWith("stage") ? "Sending " : "Writing ") + StageLabel(s.Stage) + "... (do not unplug the console)");
                        phases.Add(new JObj { { "step", step }, { "state", "began" }, { "at", Record.Now() } }); rec.Set("phases", phases).Save();
                        WriteResult r;
                        try { r = step.StartsWith("stage") ? con.RescueStep(step, auth, new[] { s.Size.ToString(CultureInfo.InvariantCulture), s.WantMd5 }, bytes[s.Stage]) : con.RescueStep(step, auth, new[] { s.NowMd5, s.WantMd5 }); }
                        catch (GateClosedException ex) { rec.State("refused", ex.Message); o.State = "refused"; o.Problem = ex.Message; return o; }
                        phases.Add(new JObj { { "step", step }, { "state", !r.Ok ? "failed" : step.StartsWith("stage") ? "checked-in-memory" : "written-and-read-back" }, { "detail", r.Line ?? r.Problem }, { "at", Record.Now() } }); rec.Set("phases", phases).Save();
                        ctx.At("rescue-" + step);
                        if (!r.Ok) return Stop(step, r.Problem);
                        var got = Regex.Replace(r.Line ?? "", "^result " + Regex.Escape(step) + " ok ?", "");
                        if (step.StartsWith("stage") && got != s.WantMd5 + " " + s.Size.ToString(CultureInfo.InvariantCulture)) return Stop(step, "the console received " + got + ", not the reviewed stage");
                        if (step.StartsWith("burn") && got != s.WantMd5) return Stop(step, StageLabel(s.Stage) + " reads back as " + got + ", not as planned");
                    }
                    o.Done.Add(s.Stage);
                }
                var after = Diagnose(con); var idAfter = con.Identify();
                bool ok = ConsoleIdentity.SameConsole(idAfter.Hwid, plan.Hwid) && plan.Stages.All(s => (s.Stage == "boot0" ? after.Boot0 : after.Boot1) == s.WantMd5 && (s.Stage != "boot1" || after.Boot1FlashTool == s.WantMd5));
                rec.Set("after", after.ToJson());
                if (!ok) { rec.State("stopped", "the startup stages do not read back as planned"); o.State = "stopped"; o.Problem = "Every step reported success, but the startup stages do not read back as planned. Their state is not verified; nothing more was changed."; return o; }
                rec.State("rescued"); con.Card("done"); o.State = "rescued"; return o;
            }
        }
    }
}
