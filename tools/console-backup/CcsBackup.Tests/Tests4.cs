// Classic Cart Studio console backup and restore engine tests: first-time preparation. GPL-3.0-or-later.
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        static (PrepPlan plan, PrepOutcome refused, RecoveryConsole con, EngineContext ctx, AdmissionResult adm) PrepPlanFor(string console, Settings s)
        {
            var con = RecoverySessions.OpenSimulated(console, ImageKind.Prepare); var ctx = Ctx(s, ImageKind.Prepare);
            var id = con.Identify();
            var adm = Admission.Admit(s, id.Hwid, "prepare", Subj, null, true, con.SessionId);
            if (!adm.Ok) return (null, new PrepOutcome { State = "refused", Problem = adm.Code + ": " + adm.Text }, con, ctx, adm);
            var (p, r) = Preparation.Prepare(con, ctx, adm);
            return (p, r, con, ctx, adm);
        }

        const string Subj = "ccs-prepare/hakchi-" + Preparation.PinnedHakchiVersion;
        static AdmissionResult UsePrep(Settings s, AdmissionResult adm, RecoveryConsole con, PrepPlan p) => Admission.Use(s, adm.TicketId, "prepare", Subj, con.Hwid, null, true, null, new SessionCapture(p.CurrentBackup, p.CurrentSha256, con.SessionId));

        public static void Prepare()
        {
            var dir = NewDir("prepare"); var s = SettingsIn(dir, false);
            // a stock console: refused without a backup made now
            var c = Console_("stock");
            var (p0, r0, _, _, _) = PrepPlanFor(c, s);
            Check(p0 == null && r0.Problem.StartsWith("backup-required"), "no backup of this console: preparation is refused before anything (" + r0?.Problem + ")");
            var b = Backup(c, s);
            // a save made since that backup: the console's state now is what preparation protects (captured in its own session)
            using (var f = new FileStream(SimulatedConsole.Dev(c, "nandd"), FileMode.Open, FileAccess.Write)) { f.Position = 4096; f.Write(new byte[] { 7, 7, 7, 7 }, 0, 4); }
            var (p, r, con, ctx, adm) = PrepPlanFor(c, s);
            Check(p != null && p.CurrentBackup != null && p.CurrentBackup != b.PrimaryPath && Verifier.VerifyFile(p.CurrentBackup).Ok && MemberSha(p.CurrentBackup, "restore/nandd.img") == Sha(SimulatedConsole.Dev(c, "nandd")) && MemberSha(p.CurrentBackup, "restore/nandd.img") != MemberSha(b.PrimaryPath, "restore/nandd.img"),
                "the plan is bound to a complete backup captured in this preparation session: it holds the change made since the earlier backup");
            Check(!Admission.Use(s, adm.TicketId, "prepare", Subj, con.Hwid, null, true).Ok, "the worker's check without that capture (the old way): refused");
            Check(!Admission.Use(s, adm.TicketId, "prepare", Subj, con.Hwid, null, true, null, new SessionCapture(p.CurrentBackup, p.CurrentSha256, "another-session")).Ok, "a capture named for another session: refused");
            Check(!Admission.Use(s, adm.TicketId, "prepare", Subj, con.Hwid, null, true, null, new SessionCapture(b.PrimaryPath, b.Verification.ArchiveSha256, con.SessionId)).Ok, "an older backup offered as this session's capture: refused");
            var reCon = RecoverySessions.OpenSimulated(c, ImageKind.Prepare);
            Check(Preparation.Execute(reCon, p, p.Digest, p.Nonce, ctx).State == "refused" && !File.Exists(Path.Combine(c, "writes.log")), "the console connected again after the review (a new session): the plan is refused, nothing written");
            (p, r, con, ctx, adm) = PrepPlanFor(c, s);
            Check(p != null && p.State == "original" && p.Steps.SequenceEqual(Preparation.Order) && p.Notes.Any(n => n.Contains("30 games")), "stock console with a backup made now: the four steps in hakchi's order, kernel last (" + string.Join(",", p?.Steps ?? new List<string>()) + ")");
            Check(Preparation.Execute(con, p, Other(p.Digest), p.Nonce, ctx).State == "refused" && !File.Exists(Path.Combine(c, "writes.log")), "a confirmation for another plan: refused, nothing changed");
            // the backup made in this session goes missing after the review: refused, nothing written, the admission not used up
            File.Move(p.CurrentBackup, p.CurrentBackup + ".withheld");
            var mp = Preparation.Execute(con, p, p.Digest, p.Nonce, ctx, () => UsePrep(s, adm, con, p));
            Check(mp.State == "refused" && mp.Problem.Contains("missing") && !File.Exists(Path.Combine(c, "writes.log")) && p.Nonce != null && !File.Exists(Path.Combine(s.RecordsFolder, "admission-" + adm.TicketId + ".used")),
                "the backup made for the preparation moved aside after the review: refused, nothing written, neither the confirmation nor the admission used (" + mp.Problem + ")");
            File.Move(p.CurrentBackup + ".withheld", p.CurrentBackup);
            AdmissionResult use = null;
            var o = Preparation.Execute(con, p, p.Digest, p.Nonce, ctx, () => use = UsePrep(s, adm, con, p));
            var log = File.ReadAllLines(Path.Combine(c, "writes.log")).Select(l => l.Substring(l.IndexOf(' ') + 1)).ToList();
            Check(use.Ok && o.State == "prepared" && log.SequenceEqual(new[] { "prep files", "prep uboot", "prep slot68", "prep kernel" }), "prepared: each step once, in order, read back (" + o.Problem + "; " + string.Join(", ", log) + ")");
            Check(File.ReadAllBytes(SimulatedConsole.Dev(c, "boot2-68")).SequenceEqual(StockKernel()), "the original Nintendo kernel is in the spare slot afterwards (the way back)");
            Check(Preparation.Execute(con, p, p.Digest, p.Nonce, ctx).State == "refused", "the same confirmation cannot prepare twice");
            Check(History.List(s).Count(e => e.Status == BackupStatus.Verified) == 3 && Verifier.VerifyFile(b.PrimaryPath).Ok && Verifier.VerifyFile(p.CurrentBackup).Ok, "the backups made before preparing (the earlier one and the two in-session captures) are unchanged and verified");
            var prec = Record.Load(o.RecordFile); var before = prec.O("before"); var afterRec = prec.O("after");
            Check(before?.S("snapshot") == p.CurrentBackup && before?.S("parttable") != null && afterRec?.S("active_md5") == Md5(File.ReadAllBytes(SimulatedConsole.Dev(c, "boot2"))) && afterRec?.S("boot1_md5") == Md5(File.ReadAllBytes(SimulatedConsole.Dev(c, "boot1"))) && afterRec?.S("parttable") != null,
                "the preparation record keeps the pre-preparation snapshot and the exact state preparation left (startup software, bootloader, storage table)");
            // a step that fails stops every later step; nothing is retried
            var c2 = Console_("stock"); Backup(c2, s);
            File.WriteAllText(Path.Combine(c2, "faults.json"), "{\"prep\":{\"step\":\"uboot\"}}");
            var (p2, _, con2, ctx2, adm2) = PrepPlanFor(c2, s);
            var o2 = Preparation.Execute(con2, p2, p2.Digest, p2.Nonce, ctx2, () => UsePrep(s, adm2, con2, p2));
            var log2 = File.Exists(Path.Combine(c2, "writes.log")) ? File.ReadAllLines(Path.Combine(c2, "writes.log")).Select(l => l.Substring(l.IndexOf(' ') + 1)).ToList() : new List<string>();
            Check(o2.State == "stopped" && o2.Done.SequenceEqual(new[] { "files" }) && log2.SequenceEqual(new[] { "prep files" }) && !File.Exists(SimulatedConsole.Dev(c2, "boot2-68")), "the u-boot step fails: stopped there; the spare slot and kernel are NOT written afterwards; nothing retried (" + o2.Problem + ")");
            var rec = Record.Load(o2.RecordFile);
            Check(rec.S("state") == "stopped" && rec.A("phases").OfType<Dictionary<string, object>>().Any(x => x.S("step") == "uboot" && x.S("state") == "failed"), "the preparation record shows where it stopped");
            // a console already prepared with the pinned hakchi: nothing to do
            var c3 = Console_("modified"); Backup(c3, s);
            var (p3, r3, con3, ctx3, _) = PrepPlanFor(c3, s);
            Check(p3 != null && p3.State == "already" && p3.Steps.Count == 0, "already prepared with hakchi " + Preparation.PinnedHakchiVersion + " (its files, its kernel, the original in the spare slot): nothing to install again (" + (p3?.Text ?? r3?.Problem) + ")");
            Check(Preparation.Execute(con3, p3, p3.Digest, p3.Nonce, ctx3).State == "already-prepared" && !File.Exists(Path.Combine(c3, "writes.log")), "confirming it changes nothing");
            // another modification without the original kernel: refused
            var c4 = Console_("modified-noorig"); Backup(c4, s);
            var (p4, r4, _, _, _) = PrepPlanFor(c4, s);
            Check(p4 == null && r4.Problem.Contains("recovery baseline") || p4 != null && p4.State == "unsupported" && p4.Refusal != null && p4.Steps.Count == 0, "a console modified by other software without the original kernel: preparation refused, with the reason (" + (p4?.Refusal ?? r4?.Problem) + ")");
            // the Utility has no preparation image and its worker refuses the request (capability profile below the window)
            Check(RecoveryImages.Embedded(ImageKind.Prepare) && RecoveryImages.Embedded(ImageKind.Rescue) && RecoveryImages.Embedded(ImageKind.Backup) && RecoveryImages.Embedded(ImageKind.Restore), "the shared engine carries every console program (backup, restore, preparation with its way back, rescue): the same bytes in both hosts");
        }
    }
}
