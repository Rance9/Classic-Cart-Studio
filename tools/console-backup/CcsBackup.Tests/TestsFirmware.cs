// Classic Cart Studio console backup and restore engine tests: Console Composer's Browse for Firmware (an owner's system image as a CCS
// Nucleus choice; FirmwareEngine). GPL-3.0-or-later. Simulated consoles only; the real-console boundary (Gate.FirmwareWrites) stays closed.
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
        static Dictionary<string, object> SimRegistry(string c) { var p = Path.Combine(c, "nucleus-registry.json"); return File.Exists(p) ? Json.Parse(File.ReadAllText(p)).O("options") : new Dictionary<string, object>(); }

        public static void Firmware()
        {
            var s = SettingsIn(NewDir("firmware"), false);
            var img = Path.Combine(Work, "owner-root-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".hsqs");
            var root = SystemRoot(NewDir("firmware-root"), "nes-own", "gzip", Clover("dp-nes", "USA")); File.WriteAllBytes(img, root); var sha = Sha(root);   // a real system root: the add checks which system it holds
            var junk = Path.Combine(Work, "not-a-root.hsqs"); File.WriteAllBytes(junk, Cipher(4096));
            var c = SmallConsole("modified", true);
            var (p0, r0) = FirmwareEngine.Prepare(RecoverySessions.OpenSimulated(c, ImageKind.Firmware), Ctx(s, ImageKind.Firmware), "add", null, "My Other System", img);
            Check(p0 == null && r0.Problem.Contains("CCS Nucleus is not installed") && Writes(c).Count == 0, "CCS Nucleus not installed: adding a system image is refused (" + r0?.Problem + ")");
            SetInstalled(c, "nucleus", "ccs-nucleus");
            var (pj, rj) = FirmwareEngine.Prepare(RecoverySessions.OpenSimulated(c, ImageKind.Firmware), Ctx(s, ImageKind.Firmware), "add", null, "Junk", junk);
            Check(pj == null && rj.Problem.Contains("SquashFS") && Writes(c).Count == 0, "a file that is not a system root (its real content checked): refused (" + rj?.Problem + ")");
            var (pl, rl) = FirmwareEngine.Prepare(RecoverySessions.OpenSimulated(c, ImageKind.Firmware), Ctx(s, ImageKind.Firmware), "add", null, "Bad \"name\"", img);
            Check(pl == null && rl.State == "refused", "a chooser name with characters the console does not show: refused");
            var cfg = Json.Parse(File.ReadAllText(Path.Combine(c, "console.json"))); var j0 = new JObj(); foreach (var kv in cfg) j0.Add(kv.Key, kv.Value); j0.Set("data_free_bytes", 8000000L + 4095);
            var cfgKeep = File.ReadAllText(Path.Combine(c, "console.json")); File.WriteAllText(Path.Combine(c, "console.json"), Json.Write(j0));
            var (pf, rf) = FirmwareEngine.Prepare(RecoverySessions.OpenSimulated(c, ImageKind.Firmware), Ctx(s, ImageKind.Firmware), "add", null, "My Other System", img);
            Check(pf == null && rf.Problem.Contains("Not enough room") && rf.Problem.Contains("4.1 KB") && rf.Problem.Contains("MB") && !rf.Problem.Contains("KiB"), "the image and the kept reserve do not fit the measured free space: refused, in decimal units (" + rf?.Problem + ")");
            File.WriteAllText(Path.Combine(c, "console.json"), cfgKeep);

            var con = RecoverySessions.OpenSimulated(c, ImageKind.Firmware); var ctx = Ctx(s, ImageKind.Firmware);
            var (p, r) = FirmwareEngine.Prepare(con, ctx, "add", null, "My Other System", img);
            Check(r == null && p.Id == FirmwareEngine.DefaultId(sha) && p.Steps.SequenceEqual(new[] { "rput", "rimport" }) && p.ImageSha256 == sha && p.ImageBytes == root.Length && p.ProtectiveArchive.Session == con.SessionId
                  && p.Notes.Any(n => n.Contains("never to hakchi's firmware folder") && n.Contains("only when you choose it")),
                "add: the image's size and SHA-256 bound in the plan, sent then imported, after a complete backup made in this session; the plan says it starts only when chosen (" + r?.Problem + ")");
            // the image on the PC changes after the review: refused, nothing written, the confirmation unused
            var keep = File.ReadAllBytes(img); var changed = (byte[])keep.Clone(); changed[2000] ^= 0x01; File.WriteAllBytes(img, changed); var nonce = p.Nonce;
            var m1 = FirmwareEngine.Execute(con, p, p.Digest, nonce, ctx);
            Check(m1.State == "refused" && m1.Problem.Contains("changed after the review") && Writes(c).Count == 0 && p.Nonce == nonce && m1.RecordFile == null, "the chosen image changed after the review: refused, nothing written, no record, the confirmation unused (" + m1.Problem + ")");
            File.WriteAllBytes(img, keep);
            File.Move(p.ProtectivePath, p.ProtectivePath + ".withheld");
            var m2 = FirmwareEngine.Execute(con, p, p.Digest, nonce, ctx);
            Check(m2.State == "refused" && m2.Problem.Contains("missing") && Writes(c).Count == 0, "the backup made for it moved aside: refused, nothing written (" + m2.Problem + ")");
            File.Move(p.ProtectivePath + ".withheld", p.ProtectivePath);
            // interrupted between its two steps, then finished by a new connection without writing the image twice
            File.WriteAllText(Path.Combine(c, "faults.json"), "{\"prep\":{\"step\":\"rimport\"}}");
            var o1 = FirmwareEngine.Execute(con, p, p.Digest, nonce, ctx);
            Check(o1.State == "stopped" && o1.Done.SequenceEqual(new[] { "rput" }) && !SimRegistry(c).ContainsKey(p.Id) && Record.Load(o1.RecordFile).S("state") == "stopped", "the import fails after the image was sent: stopped, the registry unchanged, recorded (" + o1.Problem + ")");
            File.Delete(Path.Combine(c, "faults.json")); Gone(p.ProtectivePath);
            var con2 = RecoverySessions.OpenSimulated(c, ImageKind.Firmware);
            var (p2, r2) = FirmwareEngine.Prepare(con2, ctx, "add", null, "My Other System", img);
            var o2 = r2 != null ? null : FirmwareEngine.Execute(con2, p2, p2.Digest, p2.Nonce, ctx);
            var reg = SimRegistry(c); var opt = reg.ContainsKey(p.Id) ? reg[p.Id] as Dictionary<string, object> : null;
            var ph = o2 == null ? new List<Dictionary<string, object>>() : Record.Load(o2.RecordFile).A("phases").OfType<Dictionary<string, object>>().ToList();
            Check(o2 != null && o2.State == "added" && Writes(c).SequenceEqual(new[] { "rput " + p.Id, "rimport " + p.Id }) && ph.Any(x => x.S("step") == "rput" && x.S("state") == "already-there")
                  && opt?.S("kind") == "owner-root" && opt?.S("sha256") == sha && Sha(Path.Combine(c, "nucleus-roots", p.Id + ".hsqs")) == sha,
                "a new connection finishes it: the image already received is not written again; CCS Nucleus lists it with its SHA-256 (" + (r2?.Problem ?? o2?.Problem) + "; " + string.Join(", ", Writes(c)) + ")");
            Check(o2 != null && FirmwareEngine.Execute(con2, p2, p2.Digest, p2.Nonce, ctx).State == "refused", "the same confirmation cannot run twice");
            Gone(p2?.ProtectivePath);
            var (pd, rd) = FirmwareEngine.Prepare(RecoverySessions.OpenSimulated(c, ImageKind.Firmware), ctx, "add", "another-id", "Again", img);
            Check(pd == null && rd.Problem.Contains("already one of CCS Nucleus's choices"), "the same image again under another name: refused, nothing to do (" + rd?.Problem + ")");

            // removing a choice: the reverse; a changed image file is never deleted
            var keepRoot = File.ReadAllBytes(Path.Combine(c, "nucleus-roots", p.Id + ".hsqs"));
            var con3 = RecoverySessions.OpenSimulated(c, ImageKind.Firmware);
            var (p3, r3) = FirmwareEngine.Prepare(con3, ctx, "remove", p.Id, null, null);
            Check(r3 == null && p3.Steps.SequenceEqual(new[] { "rdrop" }) && p3.ImageSha256 == sha, "remove: one step, bound to the image's SHA-256 as the registry lists it (" + r3?.Problem + ")");
            var bad = (byte[])keepRoot.Clone(); bad[100] ^= 0x22; File.WriteAllBytes(Path.Combine(c, "nucleus-roots", p.Id + ".hsqs"), bad);
            var o3 = FirmwareEngine.Execute(con3, p3, p3.Digest, p3.Nonce, ctx);
            Check(o3.State == "stopped" && SimRegistry(c).ContainsKey(p.Id) && File.ReadAllBytes(Path.Combine(c, "nucleus-roots", p.Id + ".hsqs")).SequenceEqual(bad), "the image file was changed by something else: nothing is removed (the choice stays, the file is kept) (" + o3.Problem + ")");
            File.WriteAllBytes(Path.Combine(c, "nucleus-roots", p.Id + ".hsqs"), keepRoot); Gone(p3.ProtectivePath);
            var con4 = RecoverySessions.OpenSimulated(c, ImageKind.Firmware);
            var (p4, _) = FirmwareEngine.Prepare(con4, ctx, "remove", p.Id, null, null);
            var o4 = FirmwareEngine.Execute(con4, p4, p4.Digest, p4.Nonce, ctx);
            Check(o4.State == "removed" && !SimRegistry(c).ContainsKey(p.Id) && !File.Exists(Path.Combine(c, "nucleus-roots", p.Id + ".hsqs")) && SimRegistry(c).ContainsKey("native"), "removed: the choice and Nucleus's copy of the image are gone; the console's own system stays a choice (" + o4.Problem + ")");
            Gone(p4.ProtectivePath);
            var (p5, r5) = FirmwareEngine.Prepare(RecoverySessions.OpenSimulated(c, ImageKind.Firmware), ctx, "remove", "native", null, null);
            Check(p5 == null && r5.State == "refused", "the console's own system can never be removed as a choice");
            GoneConsole(c); GoneBackups(s); Gone(img, junk);
            try { RecoverySessions.OpenReal(ImageKind.Firmware, null, () => false); Check(false, "a real session to add a system image opened"); }
            catch (GateClosedException ex) { Check(!CcsBackup.Gate.FirmwareWrites && ex.Message.Contains("not available"), "a real console: closed in code, before any USB activity (" + ex.Message + ")"); }
        }
    }
}
