// Classic Cart Studio console backup and restore engine tests: Return to Original Software after Classic Cart Studio's own preparation.
// GPL-3.0-or-later. Simulated consoles only (files on this PC); the real-console boundary (Gate.ReturnWrites) stays closed.
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        static List<string> Writes(string c) => File.Exists(Path.Combine(c, "writes.log")) ? File.ReadAllLines(Path.Combine(c, "writes.log")).Select(l => l.Substring(l.IndexOf(' ') + 1)).ToList() : new List<string>();
        static string Table(string c) => Json.Parse(File.ReadAllText(Path.Combine(c, "console.json"))).S("parttable");
        static void Poke(string c, string dev, long at, byte v) { using (var f = new FileStream(SimulatedConsole.Dev(c, dev), FileMode.Open, FileAccess.Write)) { f.Position = at; f.WriteByte(v); } }

        /// <summary>a stock console with a baseline, then Classic Cart Studio's preparation (the in-session snapshot is the way back).
        /// beforePrepare: a change to the console after its baseline, before the preparation's own snapshot.</summary>
        static (string console, string snapshot, Settings s) Prepared(Settings s, Action<string> beforePrepare = null)
        {
            var c = Console_("stock"); Backup(c, s);
            Poke(c, "nandd", 2048, 0x42);   // a save made after that backup: in the preparation's own snapshot, not in the baseline
            beforePrepare?.Invoke(c);
            var (p, r, con, ctx, adm) = PrepPlanFor(c, s);
            if (p == null) throw new Exception("preparation plan: " + r?.Problem);
            var o = Preparation.Execute(con, p, p.Digest, p.Nonce, ctx, () => UsePrep(s, adm, con, p));
            if (!o.Ok) throw new Exception("preparation: " + o.Problem);
            File.Delete(Path.Combine(c, "writes.log"));
            return (c, p.CurrentBackup, s);
        }

        public static void Return()
        {
            var dir = NewDir("return"); var s = SettingsIn(dir, false);
            var (c, snap, _) = Prepared(s);
            var sv = Verifier.VerifyFile(snap);
            var table0 = sv.Manifest.O("layout").S("partition_table");
            Check(sv.Ok && Table(c) != table0 && File.ReadAllBytes(SimulatedConsole.Dev(c, "boot1")).SequenceEqual(SimulatedConsole.SimHakchiUboot) && File.Exists(Path.Combine(c, "prepared-files.txt")),
                "prepared: hakchi's u-boot, its kernel and files, and the storage table tuned as hakchi's mod_install does (the snapshot taken in the preparation session is the way back)");
            // the documented gap: the plain restore of the pre-preparation snapshot never writes a bootloader or the storage table
            var (gp, gr) = RestoreEngine.Prepare(RecoverySessions.OpenSimulated(c, ImageKind.Restore), snap, Ctx(s, ImageKind.Restore));
            Check(gr != null && gr.State == "refused" && gr.Problem.Contains("divided differently") || gp != null && gp.Refusals.Any(x => x.Contains("bootloader")),
                "after preparing, Restore Console Backup alone refuses the backup made before preparing (storage table and bootloader changed) (" + (gr?.Problem ?? string.Join("; ", gp?.Refusals ?? new List<string>())) + ")");
            // later play on the prepared console: a save after preparing
            Poke(c, "nandd", 4096, 0x77);
            // refusals before anything is written
            var sN = SettingsIn(NewDir("return-none"), false);   // its own records: every simulated console here has the same identity
            var fresh = Console_("stock"); Backup(fresh, sN);
            var (fp, fr) = ReturnEngine.Prepare(RecoverySessions.OpenSimulated(fresh, ImageKind.Return), Ctx(sN, ImageKind.Return));
            Check(fp == null && fr.State == "refused" && fr.Problem.Contains("No preparation") && Writes(fresh).Count == 0, "a console Classic Cart Studio never prepared: refused (" + fr?.Problem + ")");
            var keep = Path.Combine(dir, "boot2.keep"); File.Copy(SimulatedConsole.Dev(c, "boot2"), keep, true);
            var other = File.ReadAllBytes(keep); other[3000] ^= 0x10; File.WriteAllBytes(SimulatedConsole.Dev(c, "boot2"), other);
            var (mp, mr) = ReturnEngine.Prepare(RecoverySessions.OpenSimulated(c, ImageKind.Return), Ctx(s, ImageKind.Return));
            Check(mp == null && mr.Problem.Contains("not exactly as Classic Cart Studio's preparation left it") && mr.Problem.Contains("startup software") && Writes(c).Count == 0, "the startup software changed since (another modification): refused, nothing written (" + mr?.Problem + ")");
            File.Copy(keep, SimulatedConsole.Dev(c, "boot2"), true);
            var tb = Table(c); var cfg = Json.Parse(File.ReadAllText(Path.Combine(c, "console.json")));
            var keepSnap = snap + ".keep"; File.Copy(snap, keepSnap, true);
            using (var f = new FileStream(snap, FileMode.Open, FileAccess.ReadWrite)) { f.Position = 5000000; int b = f.ReadByte(); f.Position = 5000000; f.WriteByte((byte)(b ^ 1)); }
            var (ap, ar) = ReturnEngine.Prepare(RecoverySessions.OpenSimulated(c, ImageKind.Return), Ctx(s, ImageKind.Return));
            Check(ap == null && ar.State == "refused" && Writes(c).Count == 0, "the backup made before preparing was altered: refused (" + ar?.Problem + ")");
            File.Copy(keepSnap, snap, true); File.Move(snap, snap + ".gone");
            var (np, nr) = ReturnEngine.Prepare(RecoverySessions.OpenSimulated(c, ImageKind.Return), Ctx(s, ImageKind.Return));
            Check(np == null && nr.Problem.Contains("is not there"), "the backup made before preparing is missing: refused (" + nr?.Problem + ")");
            File.Move(snap + ".gone", snap); File.Delete(keepSnap);
            // the return, session A
            var con = RecoverySessions.OpenSimulated(c, ImageKind.Return); var ctx = Ctx(s, ImageKind.Return);
            var (plan, refused) = ReturnEngine.Prepare(con, ctx);
            Check(refused == null && plan != null && plan.Steps.SequenceEqual(ReturnEngine.Order) && plan.Snapshot == snap, "the return plan: hakchi's uninstall as checked steps, in its order (" + refused?.Problem + ")");
            var pv = plan == null ? null : Verifier.VerifyFile(plan.ProtectivePath);
            Check(pv != null && pv.Ok && MemberSha(plan.ProtectivePath, "restore/nandd.img") == Sha(SimulatedConsole.Dev(c, "nandd")), "the console as it is now is backed up first, in the same session (the save made after preparing is in it)");
            Check(plan.Steps.First() == "ret-stage" && plan.UbootWantMd5 == sv.Members["reference/boot1.bin"].md5 && plan.ProtectiveArchive.Session == con.SessionId && plan.ProtectiveArchive.Sha256 == pv.ArchiveSha256 && plan.SnapshotArchive.Sha256 == sv.ArchiveSha256,
                "the plan names this console's own bootloader from the snapshot (not the flash tool's default) and binds both backups by hash, the new one to this session");
            Check(ReturnEngine.Execute(con, plan, Other(plan.Digest), plan.Nonce, ctx).State == "refused" && Writes(c).Count == 0, "a confirmation for another plan: refused, nothing written");
            Check(ReturnEngine.Execute(RecoverySessions.OpenSimulated(c, ImageKind.Return), plan, plan.Digest, plan.Nonce, ctx).State == "refused" && Writes(c).Count == 0, "the console connected again after the review: refused, nothing written");
            // the protective backup made in this session is the only copy of what changed since preparing: every way it can go wrong
            // between the review and the confirmation refuses before anything is written, and the confirmation stays usable
            var prot = plan.ProtectivePath; var nonce = plan.Nonce; var protBytes = File.ReadAllBytes(prot);
            File.Move(prot, prot + ".withheld");
            var m1 = ReturnEngine.Execute(con, plan, plan.Digest, nonce, ctx);
            Check(m1.State == "refused" && m1.Problem.Contains("missing") && Writes(c).Count == 0 && plan.Nonce == nonce && m1.RecordFile == null,
                "Codex's reproducer: the protective backup moved aside after the review: refused, nothing written, no record, the confirmation not used (" + m1.Problem + ")");
            File.Move(prot + ".withheld", prot);
            var flipped = (byte[])protBytes.Clone(); flipped[flipped.Length / 2] ^= 0x01; File.WriteAllBytes(prot, flipped);
            var m2 = ReturnEngine.Execute(con, plan, plan.Digest, nonce, ctx);
            Check(m2.State == "refused" && Writes(c).Count == 0 && plan.Nonce == nonce, "the protective backup changed by one byte: refused, nothing written (" + m2.Problem + ")");
            var otherConsole = Console_("stock", HwidB); var ob = Backup(otherConsole, SettingsIn(NewDir("return-other"), false));
            File.Copy(ob.PrimaryPath, prot, true);
            var m3 = ReturnEngine.Execute(con, plan, plan.Digest, nonce, ctx);
            Check(m3.State == "refused" && Writes(c).Count == 0 && plan.Nonce == nonce, "the protective backup replaced by another console's verified backup: refused, nothing written (" + m3.Problem + ")");
            File.WriteAllBytes(prot, protBytes);
            File.Move(plan.Snapshot, plan.Snapshot + ".withheld");
            var m4 = ReturnEngine.Execute(con, plan, plan.Digest, nonce, ctx);
            Check(m4.State == "refused" && m4.Problem.Contains("missing") && Writes(c).Count == 0 && plan.Nonce == nonce, "the backup made before preparing moved aside: refused, nothing written (" + m4.Problem + ")");
            File.Move(plan.Snapshot + ".withheld", plan.Snapshot);
            using (var hold = Evidence.Hold.Open(plan.Snapshot, prot))
            {
                bool moved = false, written = false, deleted = false;
                try { File.Move(prot, prot + ".swap"); moved = true; File.Move(prot + ".swap", prot); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                try { using (new FileStream(prot, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) written = true; } catch (IOException) { } catch (UnauthorizedAccessException) { }
                try { File.Delete(plan.Snapshot); deleted = !File.Exists(plan.Snapshot); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                Check(hold.Problem == null && !moved && !written && !deleted, "while the return holds its backups, they cannot be renamed, opened for writing or deleted (the check and the writes see the same files)");
            }
            var o = ReturnEngine.Execute(con, plan, plan.Digest, nonce, ctx);
            var snapBoot1 = sv.Members["reference/boot1.bin"].md5;
            Check(o.Ok && Writes(c).SequenceEqual(new[] { "ret kernel", "ret uboot", "ret files", "ret table" }), "with the evidence intact again, the same confirmation returns the console: each step once, in hakchi's uninstall order, read back (" + o.Problem + "; " + string.Join(", ", Writes(c)) + ")");
            Check(Md5(File.ReadAllBytes(SimulatedConsole.Dev(c, "boot2"))) == sv.Members["restore/boot2-active.img"].md5 && Md5(File.ReadAllBytes(SimulatedConsole.Dev(c, "boot1"))) == snapBoot1 && Table(c) == table0 && !File.Exists(Path.Combine(c, "prepared-files.txt")),
                "the original kernel and this console's own bootloader are back, hakchi's files are gone and the storage table reads as before preparing");
            Check(ReturnEngine.Execute(con, plan, plan.Digest, plan.Nonce, ctx).State == "refused", "the same confirmation cannot run twice");
            var rec = Record.Load(o.RecordFile);
            Check(rec.S("state") == "returned" && rec.A("phases").OfType<Dictionary<string, object>>().Count(x => x.S("state") == "done-and-read-back") == 4 && rec.A("phases").OfType<Dictionary<string, object>>().Any(x => x.S("step") == "ret-stage" && x.S("state") == "checked-in-memory")
                  && rec.O("plan").A("evidence").OfType<Dictionary<string, object>>().Count(x => x.S("sha256") != null) == 2,
                "the return record lists the staged bootloader, every step as written and read back, and both backups by hash");
            // session B: the console restarted (the driver reads the original table), then the unchanged restore with all its checks
            var bcon = RecoverySessions.OpenSimulated(c, ImageKind.Restore); var bctx = Ctx(s, ImageKind.Restore);
            var (bp, br) = RestoreEngine.Prepare(bcon, snap, bctx);
            Check(br == null && bp != null && bp.Refusals.Count == 0 && bp.Writable, "after the restart, Restore Console Backup accepts the backup made before preparing: the same storage table and bootloaders again (" + (br?.Problem ?? string.Join("; ", bp?.Refusals ?? new List<string>())) + ")");
            var bo = RestoreEngine.Execute(bcon, bp, bp.Digest, bp.Nonce, bctx);
            var parts = new[] { "nanda", "nandb", "nandc", "nandd", "nande" };
            Check(bo.Ok && parts.All(t => !sv.Members.ContainsKey("restore/" + t + ".img") || MemberSha(snap, "restore/" + t + ".img") == Sha(SimulatedConsole.Dev(c, t))),
                "restored: every storage part is byte for byte the backup made before preparing (Nintendo's system, the 30 games, every save and other programs' files as they were then) (" + bo.Problem + ")");
            Check(Md5(File.ReadAllBytes(SimulatedConsole.Dev(c, "boot2"))) == sv.Members["restore/boot2-active.img"].md5 && Md5(File.ReadAllBytes(SimulatedConsole.Dev(c, "boot1"))) == snapBoot1 && bp.Notes.Any(n => n.Contains("spare copy of the original")),
                "the original kernel runs; the original kernel's spare copy is left in place, as the restore says (a safety copy)");
            var inv = sv.Members.ContainsKey(BackupFormat.InventoryName);
            Check(inv && Verifier.VerifyFile(snap).Ok && Verifier.VerifyFile(plan.ProtectivePath).Ok, "the backup made before preparing and the one from the return session are unchanged and verified (the save made after preparing stays available in the latter)");
            // the snapshot does not hold a complete bootloader image: refused before anything (the bytes to write are not known)
            var sB = SettingsIn(NewDir("return-noboot1"), false);
            // (a backup made by an earlier reader that did not check the stage: the simulated console's lenient reads stand in for it)
            var (cB, _, _) = Prepared(sB, x => { File.WriteAllBytes(SimulatedConsole.Dev(x, "boot1"), Encoding.ASCII.GetBytes("NOT A U-BOOT IMAGE, 32 BYTES....")); File.WriteAllText(Path.Combine(x, "faults.json"), "{\"lenient_stage_reads\":true}"); });
            var (pB, rB) = ReturnEngine.Prepare(RecoverySessions.OpenSimulated(cB, ImageKind.Return), Ctx(sB, ImageKind.Return));
            Check(pB == null && rB.State == "refused" && rB.Problem.Contains("complete copy of this console's own bootloader") && Writes(cB).Count == 0,
                "the backup made before preparing has no complete bootloader image: the return is refused before anything is written (" + rB?.Problem + ")");
            // the bootloader is received differently by the console: ret-stage refuses, nothing is written at all
            var s2 = SettingsIn(NewDir("return-stage"), false);
            var (c2, snap2, _) = Prepared(s2);
            File.WriteAllText(Path.Combine(c2, "faults.json"), "{\"ret_stage_corrupt\":true}");
            var con2 = RecoverySessions.OpenSimulated(c2, ImageKind.Return); var ctx2 = Ctx(s2, ImageKind.Return);
            var (plan2, _) = ReturnEngine.Prepare(con2, ctx2);
            var o2 = ReturnEngine.Execute(con2, plan2, plan2.Digest, plan2.Nonce, ctx2);
            Check(o2.State == "stopped" && o2.Done.Count == 0 && Writes(c2).Count == 0 && o2.Problem.Contains("received-mismatch"),
                "the console receives the bootloader with a changed byte: the staging step refuses, nothing is written to the console (" + o2.Problem + ")");
            // the bootloader write itself fails its readback (a hardware failure): stopped right after it, said plainly, nothing retried
            var s4 = SettingsIn(NewDir("return-uboot"), false);
            var (c4, snap4, _) = Prepared(s4);
            File.WriteAllText(Path.Combine(c4, "faults.json"), "{\"uboot_readback_differs\":true}");
            var con4 = RecoverySessions.OpenSimulated(c4, ImageKind.Return); var ctx4 = Ctx(s4, ImageKind.Return);
            var (plan4, _) = ReturnEngine.Prepare(con4, ctx4);
            var o4 = ReturnEngine.Execute(con4, plan4, plan4.Digest, plan4.Nonce, ctx4);
            Check(o4.State == "stopped" && o4.Done.SequenceEqual(new[] { "ret-kernel" }) && Writes(c4).SequenceEqual(new[] { "ret kernel", "ret uboot" }) && o4.Problem.Contains("failed readback") && File.Exists(Path.Combine(c4, "prepared-files.txt")),
                "the bootloader reads back differently after its write: stopped there, hakchi's files and the table untouched, nothing retried, said plainly (" + o4.Problem + ")");
            File.Delete(Path.Combine(c4, "faults.json"));
            var (gp4, gr4) = RestoreEngine.Prepare(RecoverySessions.OpenSimulated(c4, ImageKind.Restore), snap4, Ctx(s4, ImageKind.Restore));
            Check(gr4 != null || gp4.Refusals.Count > 0, "and the restore of the backup made before preparing stays refused after that failure (it never writes a bootloader)");
            // a step that fails stops every later step
            var s3 = SettingsIn(NewDir("return-fail"), false);
            var (c3, _, _) = Prepared(s3);
            File.WriteAllText(Path.Combine(c3, "faults.json"), "{\"prep\":{\"step\":\"ret-kernel\"}}");
            var con3 = RecoverySessions.OpenSimulated(c3, ImageKind.Return); var ctx3 = Ctx(s3, ImageKind.Return);
            var (plan3, _) = ReturnEngine.Prepare(con3, ctx3);
            var o3 = ReturnEngine.Execute(con3, plan3, plan3.Digest, plan3.Nonce, ctx3);
            Check(o3.State == "stopped" && o3.Done.Count == 0 && Writes(c3).Count == 0 && Record.Load(o3.RecordFile).S("state") == "stopped", "the kernel step fails: stopped there, nothing after it, nothing retried, recorded (" + o3.Problem + ")");
            // the real console stays behind its own closed boundary
            try { RecoverySessions.OpenReal(ImageKind.Return, null, () => false); Check(false, "a real return session opened"); }
            catch (GateClosedException ex) { Check(!CcsBackup.Gate.ReturnWrites && ex.Message.Contains("not available"), "a real console: closed in code, before any USB activity (" + ex.Message + ")"); }
        }
    }
}
