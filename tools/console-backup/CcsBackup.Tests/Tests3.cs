// Classic Cart Studio console backup and restore engine tests: admission, restore, export, process lifecycle. GPL-3.0-or-later.
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsBackup.Engine;
using CcsBackup.Ext4;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        /// <summary>a backup flagged as from a real console, made through the same engine from a simulated console's files (the admission
        /// refuses simulated backups; this is how its accepting path is exercised without hardware)</summary>
        static BackupOutcome RealFlagged(string console, Settings s)
        {
            SimulatedConsole.Boot(console, RecoveryImages.IdOf(ImageKind.Backup).id, SessionProfile.Backup);
            return CaptureEngine.Run(new RecoveryConsole(new SimulatedConsole(console), SessionProfile.Backup, false), Ctx(s));
        }

        public static void AdmissionCases()
        {
            var dir = NewDir("admit"); var s = SettingsIn(dir, false);
            var r0 = Admission.Admit(s, HwidA, "install", "plan1");
            Check(!r0.Ok && r0.Code == "backup-required", "no backup of this console: refused, \"Back up this console first\" (" + r0.Text + ")");
            var sim = Backup(Console_("modified"), s);
            var r1 = Admission.Admit(s, HwidA, "install", "plan1");
            Check(sim.Ok && !r1.Ok, "a simulated console's backup never admits a change to a real console (" + r1.Code + ")");
            var noorig = RealFlagged(Console_("modified-noorig"), s);
            var r2 = Admission.Admit(s, HwidA, "install", "plan1");
            Check(noorig.Ok && !r2.Ok && r2.Code == "backup-required", "a backup without the original kernel is not a recovery baseline: refused (" + r2.Text + ")");
            var good = RealFlagged(Console_("modified"), s);
            var r3 = Admission.Admit(s, HwidA, "install", "plan1");
            Check(good.Ok && r3.Ok && r3.BaselinePath == good.PrimaryPath && r3.TicketId != null, "a verified recovery-baseline backup of this console admits the install (" + r3.Text + ")");
            Check(!Admission.Admit(s, HwidB, "install", "plan1").Ok, "another console's identity: refused");
            // the worker's own check
            var u0 = Admission.Use(s, r3.TicketId, "update", "plan1", HwidA); Check(!u0.Ok && u0.Code == "admission-mismatch", "used for a different operation: refused");
            var u1 = Admission.Use(s, r3.TicketId, "install", "plan2", HwidA); Check(!u1.Ok && u1.Code == "admission-mismatch", "used for a different plan: refused");
            var u2 = Admission.Use(s, r3.TicketId, "install", "plan1", HwidB); Check(!u2.Ok && u2.Code == "wrong-console", "used on another console: refused");
            var u3 = Admission.Use(s, r3.TicketId, "install", "plan1", HwidA); Check(u3.Ok, "the matching worker check passes (it verifies the backup file again)");
            var u4 = Admission.Use(s, r3.TicketId, "install", "plan1", HwidA); Check(!u4.Ok && u4.Code == "admission-used", "the same admission used a second time (a replay): refused");
            Check(!Admission.Use(s, "0123456789abcdef0123456789abcdef", "install", "plan1", HwidA).Ok && !Admission.Use(s, null, "install", "plan1", HwidA).Ok, "an unknown or missing admission: refused");
            // expiry
            var r4 = Admission.Admit(s, HwidA, "update", "plan3");
            var tf = r4.TicketFile; var t = Json.Parse(File.ReadAllText(tf)); var tj = new JObj(); foreach (var kv in t) tj.Add(kv.Key, kv.Value); tj.Set("expires_utc", DateTime.UtcNow.AddMinutes(-1).ToString("o")); File.WriteAllText(tf, Json.Write(tj));
            Check(Admission.Use(s, r4.TicketId, "update", "plan3", HwidA).Code == "admission-expired", "an admission older than 10 minutes: refused");
            // the backup changes after admission
            var r5 = Admission.Admit(s, HwidA, "repair", "plan4");
            var keep = Path.Combine(dir, "keep.zip"); File.Copy(good.PrimaryPath, keep);
            using (var f = new FileStream(good.PrimaryPath, FileMode.Open, FileAccess.ReadWrite)) { f.Position = 9000000; int b = f.ReadByte(); f.Position = 9000000; f.WriteByte((byte)(b ^ 4)); }
            var u5 = Admission.Use(s, r5.TicketId, "repair", "plan4", HwidA);
            Check(!u5.Ok && u5.Code == "backup-changed", "the backup file changed between admission and the write: refused (" + u5.Text + ")");
            File.Delete(good.PrimaryPath); File.Copy(keep, good.PrimaryPath);
            // the ticket's text is never itself evidence (Codex cbr1 F1): a ticket with no backup at all, or an incomplete or contradictory
            // one, is refused before it is consumed (the original probe's case: the production arguments, no backup file anywhere)
            string Ticket(string op, string body) { var id = Guid.NewGuid().ToString("N"); File.WriteAllText(Path.Combine(s.RecordsFolder, "admission-" + id + ".json"), "{\"schema\":\"ccs-maintenance-admission/2\",\"ticket\":\"" + id + "\",\"operation\":\"" + op + "\",\"subject\":\"p\",\"hwid\":\"" + HwidA + "\",\"simulated\":false,\"issued_utc\":\"" + DateTime.UtcNow.ToString("o") + "\",\"expires_utc\":\"" + DateTime.UtcNow.AddMinutes(8).ToString("o") + "\"" + body + "}"); return id; }
            bool Unused(string id) => !File.Exists(Path.Combine(s.RecordsFolder, "admission-" + id + ".used"));
            var bRef = "{\"path\":" + Json.Write(good.PrimaryPath) + ",\"sha256\":\"" + good.Verification.ArchiveSha256 + "\",\"completed_utc\":\"" + DateTime.Parse(good.Verification.Manifest.S("completed_utc"), null, System.Globalization.DateTimeStyles.AdjustToUniversal).ToString("o") + "\"}";
            foreach (var (label, body, op) in new[] {
                ("no baseline and no current entry, no backup file (Codex's probe)", "", "uninstall-data"), ("no baseline entry (an install)", "", "install"),
                ("a baseline that is not an object", ",\"baseline\":\"" + good.PrimaryPath.Replace("\\", "\\\\") + "\"", "install"), ("a baseline without its SHA-256", ",\"baseline\":{\"path\":" + Json.Write(good.PrimaryPath) + "}", "install"),
                ("a baseline with a relative path", ",\"baseline\":{\"path\":\"x.zip\",\"sha256\":\"" + new string('a', 64) + "\",\"completed_utc\":\"2026-11-14T00:00:00Z\"}", "install"),
                ("a baseline whose time contradicts its file", ",\"baseline\":{\"path\":" + Json.Write(good.PrimaryPath) + ",\"sha256\":\"" + good.Verification.ArchiveSha256 + "\",\"completed_utc\":\"2020-01-01T00:00:00Z\"}", "install"),
                ("a removal with data whose current entry is missing", ",\"baseline\":" + bRef + ",\"current_proof\":\"console-data-list\"", "uninstall-data") })
            {
                var id = Ticket(op, body);
                var u = Admission.Use(s, id, op, "p", HwidA, null, false);
                Check(!u.Ok && (u.Code == "backup-required" || u.Code == "backup-changed" || u.Code == "current-backup-required") && Unused(id), label + ": refused, the ticket not consumed (" + u.Code + ": " + u.Text + ")");
            }
            var old = Guid.NewGuid().ToString("N"); File.WriteAllText(Path.Combine(s.RecordsFolder, "admission-" + old + ".json"), "{\"schema\":\"ccs-maintenance-admission/1\",\"ticket\":\"" + old + "\",\"operation\":\"install\",\"subject\":\"p\",\"hwid\":\"" + HwidA + "\",\"issued_utc\":\"" + DateTime.UtcNow.ToString("o") + "\",\"expires_utc\":\"" + DateTime.UtcNow.AddMinutes(8).ToString("o") + "\",\"baseline\":" + bRef + "}");
            Check(!Admission.Use(s, old, "install", "p", HwidA).Ok, "a ticket in the earlier version's form: refused");
            var okT = Ticket("install", ",\"baseline\":" + bRef + ",\"current_proof\":\"not-needed\"");
            Check(Admission.Use(s, okT, "install", "p", HwidA).Ok, "the same ticket WITH its complete baseline: admitted (the refusals above are about the missing evidence)");
            // removing CCS data or recovering: the current state is proven on the console when the worker is about to write (LiveTree):
            // the newest backup's list of Classic Cart Studio's data area must equal what the console has now
            void NoHistory() { foreach (var h in Directory.GetFiles(s.RecordsFolder, "console-*-changes.json")) File.Delete(h); }   // each admitted write records a change (the early hint)
            NoHistory(); var r6 = Admission.Admit(s, HwidA, "uninstall-data", "rev1");
            var saved = Admission.CcsDataOf(r6.CurrentPath ?? "", out var why6);
            Check(r6.Ok && r6.CurrentPath == good.PrimaryPath && saved != null && saved.Keys.Any(k => k.StartsWith("/ccs/")), "Admit names the newest verified backup as the one to compare (" + r6.Text + "; " + (saved?.Count.ToString() ?? why6) + " entries under /ccs)");
            LiveTree Live(Action<Dictionary<string, string>> change = null) { var l = new LiveTree(); foreach (var kv in saved) l.Entries[kv.Key] = kv.Value; change?.Invoke(l.Entries); return l; }
            var fileKey = saved.Keys.First(k => saved[k].StartsWith("f:"));
            foreach (var (label, live, code) in new[] {
                ("a game saved since the backup (a file's content differs)", Live(e => e[fileKey] = "f:" + new string('0', 64)), "current-backup-required"),
                ("a new file since (a new save)", Live(e => e["/ccs/data/saves/new-since.sav"] = "f:" + new string('1', 64)), "current-backup-required"),
                ("a file removed since", Live(e => e.Remove(fileKey)), "current-backup-required"),
                ("the console's list could not be read", LiveTree.Unreadable("the listing did not finish"), "current-unchecked"),
                ("no list at all (the worker gave none)", (LiveTree)null, "current-unchecked") })
            {
                var a = Admission.Admit(s, HwidA, "uninstall-data", "rev1");
                var u = Admission.Use(s, a.TicketId, "uninstall-data", "rev1", HwidA, null, false, live, null);
                Check(!u.Ok && u.Code == code && Unused(a.TicketId), label + ": refused, nothing written (" + u.Text + ")");
            }
            var a6 = Admission.Admit(s, HwidA, "uninstall-data", "rev1");
            var u6 = Admission.Use(s, a6.TicketId, "uninstall-data", "rev1", HwidA, null, false, Live(), null);
            Check(u6.Ok && u6.CurrentPath == good.PrimaryPath && !Unused(a6.TicketId), "the console's data area exactly as in the newest backup: the removal is admitted (a valid protected operation)");
            Check(!Admission.Use(s, a6.TicketId, "uninstall-data", "rev1", HwidA, null, false, Live(), null).Ok, "the same admission again (a reconnection replaying it): refused");
            NoHistory(); var a7 = Admission.Admit(s, HwidA, "recover", "attempt1");
            Check(a7.Ok && !Admission.Use(s, a7.TicketId, "recover", "attempt1", HwidA).Ok, "a recovery checked without the console's list (the old way): refused");
            // the change history proves nothing either way: none, or unreadable, still needs the console's list; a recorded change newer than
            // every backup refuses early
            NoHistory();
            var a8 = Admission.Admit(s, HwidA, "uninstall-data", "rev1");
            Check(a8.Ok && !Admission.Use(s, a8.TicketId, "uninstall-data", "rev1", HwidA, null, false, Live(e => e[fileKey] = "f:" + new string('2', 64)), null).Ok, "no change history at all: the console's list still decides (a save since: refused)");
            File.WriteAllText(Path.Combine(s.RecordsFolder, "console-" + ConsoleIdentity.HwidTag(HwidA) + "-changes.json"), "{ not json");
            var a9 = Admission.Admit(s, HwidA, "uninstall-data", "rev1");
            Check(a9.Ok && !Admission.Use(s, a9.TicketId, "uninstall-data", "rev1", HwidA, null, false, Live(e => e[fileKey] = "f:" + new string('3', 64)), null).Ok, "an unreadable change history is not evidence of no change: the console's list still decides");
            Admission.NoteChange(s, HwidA, "install"); Thread.Sleep(50);
            var r10 = Admission.Admit(s, HwidA, "uninstall-data", "rev1");
            Check(!r10.Ok && r10.Code == "current-backup-required", "a change Classic Cart Studio recorded after the newest backup: refused early (" + r10.Text + ")");
            var r7b = Admission.Admit(s, HwidA, "update", "plan5");
            Check(r7b.Ok && r7b.CurrentPath == null, "an update (CCS-owned files only) still needs only the immutable baseline, which does not expire");
        }
        public static void Restore()
        {
            var dir = NewDir("restore"); var s = SettingsIn(dir, false);
            var c = Console_("modified");
            var snap = Backup(c, s);
            Check(snap.Ok, "the snapshot to return to is made (" + snap.State + ")");
            var snapNandc = MemberSha(snap.PrimaryPath, "restore/nandc.img"); var snapNandd = MemberSha(snap.PrimaryPath, "restore/nandd.img");
            // later use: the data storage changes (saves, a new folder, a removed file), the private part changes
            File.Copy(Path.Combine(Fx, "data-later.img"), SimulatedConsole.Dev(c, "nandc"), true);
            using (var f = new FileStream(SimulatedConsole.Dev(c, "nandd"), FileMode.Open, FileAccess.Write)) { f.Position = 100; f.Write(new byte[] { 9, 9, 9 }, 0, 3); }
            var bootBefore = Sha(SimulatedConsole.Dev(c, "boot2"));
            var con = RecoverySessions.OpenSimulated(c, ImageKind.Restore);
            var ctx = Ctx(s, ImageKind.Restore);
            var (plan, refused) = RestoreEngine.Prepare(con, snap.PrimaryPath, ctx);
            Check(refused == null && plan != null && plan.Writable, "the restore plan is ready (" + refused?.Problem + ")");
            Check(plan.Steps.Where(x => x.Action == "write").Select(x => x.Target).OrderBy(x => x).SequenceEqual(new[] { "nandc", "nandd" }) && plan.Steps.Single(x => x.Target == "boot2").Action == "same",
                "only what differs is written: the data and private storage; the unchanged startup software is not touched");
            Check(plan.FilesAdded >= 3 && plan.FilesRemoved == 1 && plan.FilesChanged >= 2 && plan.AddedSample.Any(x => x.Contains("added since")), "the plan says what reverts: " + plan.FilesAdded + " things added since are removed, " + plan.FilesRemoved + " removed since comes back, " + plan.FilesChanged + " changed return");
            Check(plan.ProtectivePath != null && Verifier.VerifyFile(plan.ProtectivePath).Ok, "a verified protective backup of the current state was made first");
            // the protective backup holds the later state
            using (var z = ZipReader.Open(plan.ProtectivePath, BackupFormat.Allowed)) { var e = z.Find(BackupFormat.InventoryName); using (var st = z.OpenMember(e)) using (var r = new StreamReader(st)) Check(r.ReadToEnd().Contains("added since/folder/new file.txt"), "the protective backup contains what was added after the snapshot"); }
            // confirmation: wrong digest, wrong nonce
            Check(RestoreEngine.Execute(con, plan, Other(plan.Digest), plan.Nonce, ctx).State == "refused", "a confirmation for another plan: refused, nothing written");
            Check(RestoreEngine.Execute(con, plan, plan.Digest, "nope", ctx).State == "refused", "a confirmation without this plan's one-time code: refused");
            Check(!File.Exists(Path.Combine(c, "writes.log")), "nothing was written by the refused confirmations");
            // the protective backup (the only copy of the later state) or the backup restored from goes missing or changes after the
            // review: refused before anything is written, with the confirmation still unused
            var prot = plan.ProtectivePath; var keepNonce = plan.Nonce;
            File.Move(prot, prot + ".withheld");
            var rq1 = RestoreEngine.Execute(con, plan, plan.Digest, keepNonce, ctx);
            Check(rq1.State == "refused" && rq1.Problem.Contains("missing") && !File.Exists(Path.Combine(c, "writes.log")) && plan.Nonce == keepNonce && rq1.RecordFile == null,
                "the protective backup moved aside after the review: the restore is refused, nothing written, no record, the confirmation not used (" + rq1.Problem + ")");
            File.Move(prot + ".withheld", prot);
            var srcBytes = File.ReadAllBytes(snap.PrimaryPath); var bad = (byte[])srcBytes.Clone(); bad[bad.Length / 3] ^= 0x02; File.WriteAllBytes(snap.PrimaryPath, bad);
            var rq2 = RestoreEngine.Execute(con, plan, plan.Digest, keepNonce, ctx);
            Check(rq2.State == "refused" && !File.Exists(Path.Combine(c, "writes.log")) && plan.Nonce == keepNonce, "the backup to restore changed by one byte after the review: refused, nothing written (" + rq2.Problem + ")");
            File.WriteAllBytes(snap.PrimaryPath, srcBytes);
            Check(RestoreEngine.Execute(RecoverySessions.OpenSimulated(c, ImageKind.Restore), plan, plan.Digest, keepNonce, ctx).State == "refused" && !File.Exists(Path.Combine(c, "writes.log")) && plan.Nonce == keepNonce,
                "the console connected again after the review (another session): refused, nothing written");
            var o = RestoreEngine.Execute(con, plan, plan.Digest, plan.Nonce, ctx);
            Check(o.State == "restored" && o.Written.OrderBy(x => x).SequenceEqual(new[] { "nandc", "nandd" }) && o.Verified.Count == 2, "restored: both parts written, read back by the console and identified again (" + o.Problem + ")");
            Check(Sha(SimulatedConsole.Dev(c, "nandc")) == snapNandc && Sha(SimulatedConsole.Dev(c, "nandd")) == snapNandd && Sha(SimulatedConsole.Dev(c, "boot2")) == bootBefore, "the console's storage is byte for byte the snapshot's");
            Check(RestoreEngine.Execute(con, plan, plan.Digest, plan.Nonce, ctx).State == "refused", "the same confirmation cannot run the restore a second time");
            // independent comparison: e2fsprogs lists the restored data storage exactly as the snapshot's fixture (files, metadata, links),
            // and what was added since is gone
            var lt = NewDir("restored-listing"); File.Copy(SimulatedConsole.Dev(c, "nandc"), Path.Combine(lt, "restored.img"));
            var py = Py("import sys,json\nsys.path.insert(0, r'" + Path.Combine(Fx, "..") + "')\nsys.path.insert(0, r'" + Environment.GetEnvironmentVariable("CCS_FIXTURE_TOOLS") + "')\nimport make_fixtures as m\nm.listing(r'" + lt + "','restored')\na=json.load(open(r'" + Path.Combine(lt, "listing-restored.json") + "'))['entries']\nb=json.load(open(r'" + Path.Combine(Fx, "listing-data-modified.json") + "'))['entries']\nk=lambda e:(e['path'],e['type'],e['mode'],e['uid'],e['gid'],e['size'] if e['type']=='f' else 0,e['links'],e['target'],e['sha256'])\nprint(sorted(map(k,a))==sorted(map(k,b)), len(a), any('added since' in e['path'] for e in a))\n");
            var nSnap = Json.Parse(File.ReadAllText(Path.Combine(Fx, "listing-data-modified.json"))).A("entries").Count;
            Check(py != null && py.Value.output.Trim() == "True " + nSnap + " False", "independent check (debugfs): the restored data storage lists exactly the snapshot's " + nSnap + " entries with their metadata, and nothing added since: " + py?.output.Trim());
            // refusals before any write
            var cB = Console_("modified", HwidB);
            var conB = RecoverySessions.OpenSimulated(cB, ImageKind.Restore);
            var (pB, rB) = RestoreEngine.Prepare(conB, snap.PrimaryPath, Ctx(s, ImageKind.Restore));
            Check(pB == null && rB.State == "refused" && rB.Problem.Contains("different console") && !File.Exists(Path.Combine(cB, "writes.log")), "another console's backup: refused before anything (" + rB.Problem + ")");
            var lay = Layout.Select(x => x.dev == "nande" ? (x.dev, x.label, 12288L * 1024) : x.dev == "nandc" ? (x.dev, x.label, 403456L * 1024) : x).ToArray();
            var cL = Console_("modified", HwidA, null, lay);
            var (pL, rL) = RestoreEngine.Prepare(RecoverySessions.OpenSimulated(cL, ImageKind.Restore), snap.PrimaryPath, Ctx(s, ImageKind.Restore));
            Check(pL == null && rL.Problem.Contains("divided differently"), "the storage now divided differently: refused, with the reason (" + rL.Problem + ")");
            // a bootloader that is valid but different (another u-boot build): the plan names it and is not writable
            var cU = Console_("modified"); { var other = BootImage.Synthetic(false, 757760, "another-uboot", 4096); using (var f = new FileStream(SimulatedConsole.Dev(cU, "boot1"), FileMode.Open, FileAccess.Write)) f.Write(other, 0, other.Length); }
            var (pU, rU) = RestoreEngine.Prepare(RecoverySessions.OpenSimulated(cU, ImageKind.Restore), snap.PrimaryPath, Ctx(s, ImageKind.Restore));
            Check(pU != null && !pU.Writable && pU.Refusals.Any(x => x.Contains("bootloader")) && RestoreEngine.Execute(RecoverySessions.OpenSimulated(cU, ImageKind.Restore), pU, pU.Digest, pU.Nonce, Ctx(s, ImageKind.Restore)).State == "refused", "a changed bootloader: the restore is not available (bootloaders are never written)");
            // a bootloader that is damaged (its checksum fails): the backup of the console as it is now cannot read it, so the restore stops
            // before anything is written and says why (Console Rescue is the way to a damaged startup stage)
            var cD = Console_("modified"); using (var f = new FileStream(SimulatedConsole.Dev(cD, "boot1"), FileMode.Open, FileAccess.Write)) f.WriteByte(0x42);
            var (pD, rD) = RestoreEngine.Prepare(RecoverySessions.OpenSimulated(cD, ImageKind.Restore), snap.PrimaryPath, Ctx(s, ImageKind.Restore));
            Check(pD == null && rD != null && rD.State == "stopped" && !File.Exists(Path.Combine(cD, "writes.log")), "a damaged bootloader: the backup of the console now cannot be made, so the restore stops before any write (" + rD?.Problem + ")");
            // a stock snapshot restored onto the same console after a modification with the same layout: the spare slot keeps the original copy
            var c2 = Console_("modified-noorig");
            var snap2 = Backup(c2, s);
            File.Copy(Path.Combine(Fx, "kernel-other.img"), SimulatedConsole.Dev(c2, "boot2"), true);
            File.WriteAllBytes(SimulatedConsole.Dev(c2, "boot2-68"), File.ReadAllBytes(Path.Combine(Fx, "kernel-modified.img")));
            var (p2, r2) = RestoreEngine.Prepare(RecoverySessions.OpenSimulated(c2, ImageKind.Restore), snap2.PrimaryPath, Ctx(s, ImageKind.Restore));
            Check(p2 != null && !p2.Writable && p2.Refusals.Any(x => x.Contains("spare startup slot")), "software added to the spare slot since the snapshot that is not the original kernel: refused (cannot be removed by a restore)");
            // faults while writing: the first failure stops every later write; no second attempt
            var c3 = Console_("modified"); var snap3 = Backup(c3, s);
            File.Copy(Path.Combine(Fx, "kernel-other.img"), SimulatedConsole.Dev(c3, "boot2"), true);
            File.Copy(Path.Combine(Fx, "data-later.img"), SimulatedConsole.Dev(c3, "nandc"), true);
            File.WriteAllText(Path.Combine(c3, "faults.json"), "{\"write\":{\"target\":\"nandc\",\"mode\":\"disconnect\"}}");
            var con3 = RecoverySessions.OpenSimulated(c3, ImageKind.Restore); var ctx3 = Ctx(s, ImageKind.Restore);
            var (p3, _) = RestoreEngine.Prepare(con3, snap3.PrimaryPath, ctx3);
            var o3 = RestoreEngine.Execute(con3, p3, p3.Digest, p3.Nonce, ctx3);
            var log3 = File.ReadAllText(Path.Combine(c3, "writes.log"));
            Check(o3.State == "stopped" && o3.Partial.Contains("nandc") && !log3.Contains("write boot2 ") && o3.Problem.Contains("NOT verified"), "the connection ends while the data storage is written: stopped, the startup software NOT written afterwards, state not verified (" + o3.Problem + ")");
            var rec3 = Record.Load(o3.RecordFile);
            Check(rec3.S("state") == "stopped" && rec3.A("phases").OfType<Dictionary<string, object>>().Any(ph => ph.S("phase") == "write nandc" && ph.S("state") == "failed-after-start"), "the restore record shows the phase where it stopped");
            var c4 = Console_("modified"); var snap4 = Backup(c4, s);
            File.Copy(Path.Combine(Fx, "data-later.img"), SimulatedConsole.Dev(c4, "nandc"), true);
            File.WriteAllText(Path.Combine(c4, "faults.json"), "{\"write\":{\"target\":\"nandc\",\"mode\":\"corrupt\"}}");
            var con4 = RecoverySessions.OpenSimulated(c4, ImageKind.Restore); var ctx4 = Ctx(s, ImageKind.Restore);
            var (p4, _) = RestoreEngine.Prepare(con4, snap4.PrimaryPath, ctx4);
            var o4 = RestoreEngine.Execute(con4, p4, p4.Digest, p4.Nonce, ctx4);
            Check(o4.State == "stopped" && o4.Problem.Contains("readback"), "a readback that differs after writing: stopped, nothing retried (" + o4.Problem + ")");
            // the backup file changed after the plan was reviewed
            var c5 = Console_("modified"); var snap5 = Backup(c5, s); File.Copy(Path.Combine(Fx, "data-later.img"), SimulatedConsole.Dev(c5, "nandc"), true);
            var con5 = RecoverySessions.OpenSimulated(c5, ImageKind.Restore); var ctx5 = Ctx(s, ImageKind.Restore);
            var (p5, _) = RestoreEngine.Prepare(con5, snap5.PrimaryPath, ctx5);
            using (var f = new FileStream(snap5.PrimaryPath, FileMode.Open, FileAccess.ReadWrite)) { f.Position = f.Length - 100; f.WriteByte(0); }
            var o5 = RestoreEngine.Execute(con5, p5, p5.Digest, p5.Nonce, ctx5);
            Check(o5.State == "refused" && !File.Exists(Path.Combine(c5, "writes.log")), "the backup file changed after review: refused, nothing written");
        }

        public static void Export()
        {
            if (modBackup == null) throw new SkipException("needs the capture group");
            var dir = NewDir("export"); var ccs = Path.Combine(dir, "Console-HOME.ccs"); File.WriteAllBytes(ccs, Member(modBackup, BackupFormat.ExportName));
            var m = Verifier.VerifyFile(modBackup).Manifest.O("export");
            Check(m.S("status") == "included" && m.L("games_exported") == 2 && m.A("left_out").Count == 1, "the export holds the 2 games whose exact ROM files are on the console and lists the 1 left out");
            var comp = Environment.GetEnvironmentVariable("CCS_COMPANION_DIR");
            var py = Py("import sys\nsys.path.insert(0, r'" + comp + "')\nimport ccs_package as c\ni=c.inspect(r'" + ccs + "')\nprint(i['ok'], i['kind'], i['games'], i['rom_count'], sorted(r['version_id'][:2] for r in i['roms']), i['data']['ccs_collection']['name'])\n");
            Check(py != null && py.Value.output.Trim().StartsWith("True collection 2 2"), "the existing .ccs reader (tools/companion/ccs_package.py) accepts it as a complete collection package: " + py?.output.Trim());
            // D-174: the HOME's startup screen goes in as a Studio's .ccs carries it: the collection's own picture, its bytes exactly the console's
            var st = Py("import sys, base64, hashlib\nsys.path.insert(0, r'" + comp + "')\nimport ccs_package as c\nh=c.inspect(r'" + ccs + "')['data']['ccs_collection']['home']\ncu=h['startup_custom']; b=base64.b64decode(cu['data'])\n" +
                        "print(h['startup_image'] == cu['id'] == 'custom-' + cu['sha256'][:16], hashlib.sha256(b).hexdigest() == cu['sha256'], b[:2] == bytes([255, 216]), cu['name'], sorted(k for k in h))\n");
            Check(st != null && st.Value.output.Trim() == "True True True Living room ['startup_custom', 'startup_image']", "the export carries the console HOME's startup screen: its own picture with its exact bytes, and nothing else of the HOME's look: " + st?.output.Trim());
            Check(m.O("startup").S("choice")?.StartsWith("custom-") == true && m.O("startup").B("own_picture"), "the export's description names the startup screen it carries");
            // its import, through the route the Windows app's Restore a Backup uses (ccs_package.import_roms: verify again, then hand each ROM
            // to the Studio's stores; a recording stand-in for the stores here), separately from any console restore
            var imp = Py("import sys\nsys.path.insert(0, r'" + comp + "')\nimport ccs_package as c\ngot=[]\n" +
                         "def ingest(data, fname, srcn, note, origin):\n    k = c.identify_bytes(data)['content_sha256']; got.append(k); return {'entry': {'id': 'p_' + k[:16]}}\n" +
                         "r=c.import_roms(r'" + ccs + "', None, ingest=ingest)\nprint(len(r['imported']), len(r['rejected']), len(got), len(set(got)))\n");
            Check(imp != null && imp.Value.output.Trim() == "2 0 2 2", "imported through the existing route (ccs_package.import_roms): its 2 games' ROM files verified again and handed over, none rejected: " + imp?.output.Trim());
        }

        public static void Lifecycle()
        {
            if (Utility == null || !File.Exists(Utility)) throw new SkipException("no built Utility executable was given (--utility)");
            LifecycleTests.Run();
        }
    }
}
