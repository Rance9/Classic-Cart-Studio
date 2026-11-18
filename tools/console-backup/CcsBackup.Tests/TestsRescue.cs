// Classic Cart Studio console backup and restore engine tests: Console Rescue for the boot stages (D-181). GPL-3.0-or-later.
// Simulated consoles only (files on this PC); the real-console boundary (Gate.RescueWrites) stays closed.
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
        public static void Rescue()
        {
            var dir = NewDir("rescue"); var s = SettingsIn(dir, false);
            var c = Console_("stock"); var src = Backup(c, s);
            var sv = Verifier.VerifyFile(src.PrimaryPath);
            var good1 = File.ReadAllBytes(SimulatedConsole.Dev(c, "boot1")); var good0 = File.ReadAllBytes(SimulatedConsole.Dev(c, "boot0"));
            Check(sv.Ok && sv.DamagedComponents.Count == 0 && sv.Members["reference/boot1.bin"].md5 == Md5(good1), "a normal backup holds both startup stages as complete images");
            // a healthy console: the diagnosis reads both stages; asking to repair them finds nothing to write
            var con0 = RecoverySessions.OpenSimulated(c, ImageKind.Rescue); var ctx0 = Ctx(s, ImageKind.Rescue);
            var d0 = RescueEngine.Diagnose(con0);
            Check(d0.Boot0 == Md5(good0) && d0.Boot1 == Md5(good1) && d0.DamagedStages.Count == 0, "diagnosis of a healthy console: both stages read as valid images (read only)");
            var (np, nr) = RescueEngine.Prepare(con0, ctx0, src.PrimaryPath, new[] { "boot0", "boot1" }, null);
            Check(nr == null && np != null && np.Refusal != null && np.Refusal.Contains("already match") && Writes(c).Count == 0, "the stages already match the backup: nothing to write (" + (np?.Refusal ?? nr?.Problem) + ")");
            Check(RescueEngine.Execute(con0, np, np.Digest, np.Nonce, ctx0).State == "refused" && Writes(c).Count == 0, "and confirming that plan writes nothing");
            // boot1 damaged (one changed byte): diagnosed as damaged; the repair needs the exact acknowledgment
            var bad1 = (byte[])good1.Clone(); bad1[5000] ^= 0x20; File.WriteAllBytes(SimulatedConsole.Dev(c, "boot1"), bad1);
            var con = RecoverySessions.OpenSimulated(c, ImageKind.Rescue); var ctx = Ctx(s, ImageKind.Rescue);
            var d1 = RescueEngine.Diagnose(con);
            Check(d1.Damaged("boot1") && !d1.Damaged("boot0") && d1.Boot1 == "invalid" && d1.Boot1FlashTool == "damaged", "a bootloader with one changed byte is diagnosed as damaged: invalid (its checksum), not unreadable (" + d1.Text + ")");
            var (ap, ar) = RescueEngine.Prepare(con, ctx, src.PrimaryPath, new[] { "boot1" }, null);
            var ack = RescueEngine.AcknowledgmentFor(new[] { "boot1" });
            Check(ap == null && ar.Problem.Contains(ack) && Writes(c).Count == 0, "a damaged stage cannot be saved as it was: refused until the owner types the exact acknowledgment (" + ar?.Problem + ")");
            Check(RescueEngine.Prepare(con, ctx, src.PrimaryPath, new[] { "boot1" }, "I understand").plan == null, "an approximate acknowledgment is not accepted");
            // a source of another console, and a missing source: refused before anything
            var other = Console_("stock", HwidB); var ob = Backup(other, SettingsIn(NewDir("rescue-other"), false));
            var (op, or) = RescueEngine.Prepare(con, ctx, ob.PrimaryPath, new[] { "boot1" }, ack);
            Check(op == null && or.Problem.Contains("different console") && Writes(c).Count == 0, "another console's backup never supplies a startup stage (" + or?.Problem + ")");
            // the plan: this console's own boot1 from its backup; the protective backup keeps the damaged stage as read and is no baseline
            var (plan, refused) = RescueEngine.Prepare(con, ctx, src.PrimaryPath, new[] { "boot1" }, ack);
            Check(refused == null && plan != null && plan.Refusal == null && plan.Steps.SequenceEqual(new[] { "stage1", "burn1" }) && plan.Stages.Single().NowMd5 == "invalid" && plan.Stages.Single().WantMd5 == Md5(good1),
                "the plan writes only the bootloader, from this console's backup, over the damaged one (" + refused?.Problem + ")");
            var pv = Verifier.VerifyFile(plan.ProtectiveArchive.Path);
            Check(pv.Ok && pv.DamagedComponents.TryGetValue("boot1", out var st1) && st1 == "invalid" && !pv.RecoveryBaseline && pv.RestoreReady && MemberSha(plan.ProtectiveArchive.Path, "reference/boot1.bin") == Sha(bad1),
                "the protective backup keeps the damaged bootloader exactly as read, says so, and is never a recovery baseline");
            Check(plan.Unpreserved.Count == 1 && plan.AcknowledgedText == ack, "the plan lists what could not be preserved and the acknowledgment given");
            // evidence problems at confirmation: refused, nothing written, confirmation unused
            var keep = plan.Nonce;
            Check(RescueEngine.Execute(con, plan, Other(plan.Digest), keep, ctx).State == "refused" && Writes(c).Count == 0, "a confirmation for another plan: refused, nothing written");
            File.Move(plan.ProtectiveArchive.Path, plan.ProtectiveArchive.Path + ".withheld");
            var m1 = RescueEngine.Execute(con, plan, plan.Digest, keep, ctx);
            Check(m1.State == "refused" && m1.Problem.Contains("missing") && Writes(c).Count == 0 && plan.Nonce == keep, "the protective backup moved aside: refused, nothing written, confirmation unused (" + m1.Problem + ")");
            File.Move(plan.ProtectiveArchive.Path + ".withheld", plan.ProtectiveArchive.Path);
            File.Move(src.PrimaryPath, src.PrimaryPath + ".withheld");
            var m2 = RescueEngine.Execute(con, plan, plan.Digest, keep, ctx);
            Check(m2.State == "refused" && Writes(c).Count == 0 && plan.Nonce == keep, "the source backup moved aside: refused, nothing written (" + m2.Problem + ")");
            File.Move(src.PrimaryPath + ".withheld", src.PrimaryPath);
            Check(RescueEngine.Execute(RecoverySessions.OpenSimulated(c, ImageKind.Rescue), plan, plan.Digest, keep, ctx).State == "refused" && Writes(c).Count == 0, "the console connected again after the review: refused");
            var o = RescueEngine.Execute(con, plan, plan.Digest, keep, ctx);
            Check(o.Ok && Writes(c).SequenceEqual(new[] { "rescue burn1" }) && File.ReadAllBytes(SimulatedConsole.Dev(c, "boot1")).SequenceEqual(good1) && File.ReadAllBytes(SimulatedConsole.Dev(c, "boot0")).SequenceEqual(good0),
                "rescued: this console's own bootloader written once and read back by both readers; the first stage untouched (" + o.Problem + ")");
            var rec = Record.Load(o.RecordFile);
            Check(rec.S("state") == "rescued" && rec.A("phases").OfType<Dictionary<string, object>>().Any(x => x.S("step") == "stage1" && x.S("state") == "checked-in-memory") && rec.O("after") != null, "the rescue record lists the staging, the write and the stages read back afterwards");
            Check(RescueEngine.Execute(con, plan, plan.Digest, keep, ctx).State == "refused", "the same confirmation cannot run twice");
            // both stages damaged: boot1 first, then boot0; a readback that differs stops right there
            var c2 = Console_("stock"); var s2 = SettingsIn(NewDir("rescue-both"), false); var src2 = Backup(c2, s2);
            var g0 = File.ReadAllBytes(SimulatedConsole.Dev(c2, "boot0")); var b0 = (byte[])g0.Clone(); b0[100] ^= 1; File.WriteAllBytes(SimulatedConsole.Dev(c2, "boot0"), b0);
            var g1 = File.ReadAllBytes(SimulatedConsole.Dev(c2, "boot1")); var b1 = (byte[])g1.Clone(); b1[100] ^= 1; File.WriteAllBytes(SimulatedConsole.Dev(c2, "boot1"), b1);
            File.WriteAllText(Path.Combine(c2, "faults.json"), "{\"rescue_readback_differs\":true}");
            var con2 = RecoverySessions.OpenSimulated(c2, ImageKind.Rescue); var ctx2 = Ctx(s2, ImageKind.Rescue);
            var ack2 = RescueEngine.AcknowledgmentFor(new[] { "boot0", "boot1" });
            var (p2, r2) = RescueEngine.Prepare(con2, ctx2, src2.PrimaryPath, new[] { "boot0", "boot1" }, ack2);
            Check(r2 == null && p2.Steps.SequenceEqual(new[] { "stage1", "burn1", "stage0", "burn0" }), "both stages: the bootloader first, the first stage last (" + r2?.Problem + "; " + string.Join(",", p2?.Steps ?? new List<string>()) + ")");
            var o2 = RescueEngine.Execute(con2, p2, p2.Digest, p2.Nonce, ctx2);
            Check(o2.State == "stopped" && o2.Done.Count == 0 && Writes(c2).SequenceEqual(new[] { "rescue burn1" }) && File.ReadAllBytes(SimulatedConsole.Dev(c2, "boot0")).SequenceEqual(b0) && o2.Problem.Contains("failed readback"),
                "the bootloader reads back differently after its write: stopped there, the first stage NOT written, nothing retried, said plainly (" + o2.Problem + ")");
            File.Delete(Path.Combine(c2, "faults.json"));
            // a backup that recorded a stage as damaged cannot supply it
            var (p3, r3) = RescueEngine.Prepare(RecoverySessions.OpenSimulated(c2, ImageKind.Rescue), Ctx(s2, ImageKind.Rescue), p2.ProtectiveArchive.Path, new[] { "boot0" }, ack2);
            Check(p3 == null && r3.Problem.Contains("recorded"), "a backup that recorded the first stage as damaged never supplies it (" + r3?.Problem + ")");
            // typed diagnosis (Codex review F1): an unreadable stage is written only by the explicit, acknowledged unreadable mode; a driver that
            // does not answer, or readers that disagree, never justify a repair; a writer that stops partway is reported as read again
            var c3 = Console_("stock"); var s3 = SettingsIn(NewDir("rescue-unread"), false); var src3 = Backup(c3, s3);
            var g3 = File.ReadAllBytes(SimulatedConsole.Dev(c3, "boot1")); var x3 = (byte[])g3.Clone(); x3[7000] ^= 4; File.WriteAllBytes(SimulatedConsole.Dev(c3, "boot1"), x3);
            File.WriteAllText(Path.Combine(c3, "unreadable-boot1"), "");
            var con3 = RecoverySessions.OpenSimulated(c3, ImageKind.Rescue); var ctx3 = Ctx(s3, ImageKind.Rescue);
            var d3 = RescueEngine.Diagnose(con3);
            Check(d3.Boot1 == "unreadable" && d3.Unreadable("boot1") && d3.Damaged("boot1") && !d3.ReadersDisagree, "a bootloader whose blocks cannot be read is diagnosed as unreadable, a typed condition of its own (" + d3.Text + ")");
            var ack3 = RescueEngine.AcknowledgmentFor(new[] { "boot1" }, d3);
            Check(ack3 != ack && ack3.Contains("written without being read first"), "its acknowledgment names that it is written without being read first: " + ack3);
            Check(RescueEngine.Prepare(con3, ctx3, src3.PrimaryPath, new[] { "boot1" }, ack).plan == null && Writes(c3).Count == 0, "the ordinary damaged-stage acknowledgment does not admit an unreadable stage");
            var (p4, r4) = RescueEngine.Prepare(con3, ctx3, src3.PrimaryPath, new[] { "boot1" }, ack3);
            Check(r4 == null && p4.Stages.Single().NowMd5 == "unreadable" && p4.Notes.Any(n => n.Contains("without comparing it first")), "the plan for it is typed (now: unreadable) and says it is written without a comparison, each block read back (" + r4?.Problem + ")");
            var o4 = RescueEngine.Execute(con3, p4, p4.Digest, p4.Nonce, ctx3);
            Check(o4.Ok && Writes(c3).SequenceEqual(new[] { "rescue burn1 unreadable" }) && File.ReadAllBytes(SimulatedConsole.Dev(c3, "boot1")).SequenceEqual(g3) && !File.Exists(Path.Combine(c3, "unreadable-boot1")),
                "the unreadable bootloader written once by the explicit unreadable mode and read back by both readers (" + o4.Problem + ")");
            var c5 = Console_("stock"); var s5 = SettingsIn(NewDir("rescue-nodriver"), false); var src5 = Backup(c5, s5);
            var x5 = File.ReadAllBytes(SimulatedConsole.Dev(c5, "boot1")); x5[300] ^= 2; File.WriteAllBytes(SimulatedConsole.Dev(c5, "boot1"), x5);
            File.WriteAllText(Path.Combine(c5, "faults.json"), "{\"nand_driver_fails\":true}");
            var con5 = RecoverySessions.OpenSimulated(c5, ImageKind.Rescue); var d5 = RescueEngine.Diagnose(con5);
            var (p5, r5) = RescueEngine.Prepare(con5, Ctx(s5, ImageKind.Rescue), src5.PrimaryPath, new[] { "boot1" }, RescueEngine.AcknowledgmentFor(new[] { "boot1" }, d5));
            Check(d5.NoDriver && p5 == null && r5.Problem.Contains("did not answer") && RescueEngine.Blocked(d5) != null && Writes(c5).Count == 0, "the storage driver did not answer: nothing is diagnosed as damaged and no repair is offered (" + r5?.Problem + ")");
            File.WriteAllText(Path.Combine(c5, "faults.json"), "{\"readers_disagree\":true}");
            var con6 = RecoverySessions.OpenSimulated(c5, ImageKind.Rescue); var d6 = RescueEngine.Diagnose(con6);
            var (p6, r6) = RescueEngine.Prepare(con6, Ctx(s5, ImageKind.Rescue), src5.PrimaryPath, new[] { "boot1" }, RescueEngine.AcknowledgmentFor(new[] { "boot1" }, d6));
            Check(d6.ReadersDisagree && p6 == null && r6.Problem.Contains("do not agree") && Writes(c5).Count == 0, "the two readers disagree (ccsboot: invalid, the flash tool: a valid image): no repair (" + r6?.Problem + ")");
            File.WriteAllText(Path.Combine(c5, "faults.json"), "{\"burn_stops\":{\"step\":\"burn1\",\"blocks\":1}}");
            var con7 = RecoverySessions.OpenSimulated(c5, ImageKind.Rescue); var ctx7 = Ctx(s5, ImageKind.Rescue); var d7 = RescueEngine.Diagnose(con7);
            var (p7, r7) = RescueEngine.Prepare(con7, ctx7, src5.PrimaryPath, new[] { "boot1" }, RescueEngine.AcknowledgmentFor(new[] { "boot1" }, d7));
            var o7 = RescueEngine.Execute(con7, p7, p7.Digest, p7.Nonce, ctx7); var rec7 = Record.Load(o7.RecordFile);
            Check(r7 == null && o7.State == "stopped" && o7.Problem.Contains("1 block(s)") && o7.Problem.Contains("Read again just now") && Writes(c5).SequenceEqual(new[] { "rescue burn1 stopped 1" }) && rec7.O("after_stop") != null,
                "the writer stopped after one block: reported as possibly partly written, the stages read again (read only) and recorded, nothing retried (" + o7.Problem + ")");
            // a rescue session writes no storage part, and the real console stays behind its own closed boundary
            try { con.Write("nandc", new MemoryStream(new byte[4]), 4, Md5(new byte[4]), new WriteAuthority("x", con.Hwid, new[] { "nandc" }, () => true, "t")); Check(false, "a rescue session wrote a storage part"); }
            catch (InvalidOperationException) { Check(true, "a rescue session cannot write a storage part (only its two stage steps)"); }
            try { RecoverySessions.OpenReal(ImageKind.Rescue, null, () => false); Check(false, "a real rescue session opened"); }
            catch (GateClosedException ex) { Check(!CcsBackup.Gate.RescueWrites && ex.Message.Contains("not available"), "a real console: closed in code, before any USB activity (" + ex.Message + ")"); }
        }
    }
}
