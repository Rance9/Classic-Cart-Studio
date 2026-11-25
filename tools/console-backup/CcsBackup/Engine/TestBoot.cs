// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Developer Mode's TEST BOOT: a startup image started from the console's memory, without writing it (or anything else) to the console's
// storage. Its own connection: the console in its boot ROM's USB mode (FEL), the image sent by the first-party loader
// (CcsConsole.RamBoot.TestBootSession: hakchi 3.9.3's memboot sequence), behind Gate.ConsoleSessions AND its own Gate.DeveloperTestBoot.
//   classes      own-startup  this console's own startup software, taken from the restore point (its recorded bytes, checked); its system
//                             then runs as it always does and saves games and settings as usual
//                image        a startup image file (Android boot image) in the console's own startup layout (StartupImage.Inspect: page
//                             size, load addresses, version, sizes, zImage kernel, its own checksum, command line, ramdisk format)
//                Classic Cart Studio's own read-only session is the only memory session that runs "with nothing installed on your
//                console" and writes nothing (Console Browser; ClassifyRamImage). Every other image's code controls the console once it
//                starts and can write its storage, startup stages or keys: loading it into RAM, checking its header or Workbench writing
//                nothing does not stop that. The plan says so; it never says "nothing written" for it.
//   identity     the console's hardware id (its SID, read over FEL: reads only) must be the restore point's console before anything is sent
//   protection   the SESSION RESTORE POINT policy (Developer Mode): a verified backup of this console holding everything a way back needs
//                (startup software, system part, files, startup stages); the console's storage table cannot be read in this mode, so
//                Console Restore checks it when the restore point is used. No fresh backup is forced; later changes are not protected.
//   plan         the image's facts, its command-line differences from the restore point's startup software (a key it carries never
//                shown), its dependencies (the console's key for its system part), a one-time confirmation for exactly these bytes
//   execute      the confirmation, the SID read again, the image hashed again, the restore point held and checked again, the record saved
//                first (no record, nothing sent); the image sent once, never again on this connection. Only the TV shows whether it started.
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsBackup.Library;
using CcsConsole.RamBoot;
using CcsConsole.Usb;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CcsBackup.Engine
{
    /// <summary>a console in its boot ROM's USB mode, as a test boot sees it: its hardware id, and one image sent to its memory</summary>
    public interface ITestBootTarget : IDisposable
    {
        bool Simulated { get; }
        /// <summary>this connection (a reconnection is a new one: a plan belongs to its connection)</summary>
        string SessionId { get; }
        /// <summary>the console's hardware id read now (reads only); null when it cannot be read</summary>
        string ReadSid();
        /// <summary>the padded image to the console's memory and started; null when sent, otherwise what stopped it</summary>
        string Send(byte[] padded, Action<string, long, long> progress);
    }

    public static class DeveloperTestBoot
    {
        /// <summary>why a real console's test boot cannot start in this version (the closed boundary's own sentence), or null: Gate.ConsoleSessions
        /// and Gate.DeveloperTestBoot. Reads nothing and touches no device; the window asks it before it starts the task.</summary>
        public static GateClosedException Refusal()
        {
            if (!Gate.ConsoleSessions) return new GateClosedException(Gate.SessionsText, "open a test-boot connection");
            if (!Gate.DeveloperTestBoot) return new GateClosedException(Gate.DeveloperTestBootText, "test boot");
            return null;
        }

        /// <summary>a real console: Gate.ConsoleSessions and Gate.DeveloperTestBoot first, as constants. Then exactly one console in FEL mode
        /// (an NES Classic or a Super NES Classic: the same chip answers there), its SID read; which model it is comes from the restore
        /// point's backup, whose model must allow Developer Mode (Point).</summary>
        public static ITestBootTarget OpenReal()
        {
            if (!Gate.ConsoleSessions) throw new GateClosedException(Gate.SessionsText, "open a test-boot connection");
            if (!Gate.DeveloperTestBoot) throw new GateClosedException(Gate.DeveloperTestBootText, "test boot");
            var boot = RecoveryImages.Load(ImageKind.Files);   // only its pinned fes1 and u-boot (hakchi 3.9.3's, the same in every console program)
            var s = TestBootSession.Open(new LibUsbDeviceList(), () => new LibUsbFelUsb(), boot.Fes1, boot.UbootSd, out var why);
            if (s == null) throw new ConsoleRefusedException("fel", "The console could not be reached in its USB mode (" + why + "). Nothing was sent.");
            return new FelTarget(s);
        }

        public static ITestBootTarget OpenSimulated(string folder) => new SimulatedTarget(folder);

        /// <summary>the real adapter over a FEL connection (tests construct it over a simulated FEL device)</summary>
        internal sealed class FelTarget : ITestBootTarget
        {
            readonly TestBootSession s;
            internal FelTarget(TestBootSession s) { this.s = s; }
            public bool Simulated => false;
            public string SessionId { get; } = Guid.NewGuid().ToString("N");
            public string ReadSid() => s.ReadSid();
            public string Send(byte[] padded, Action<string, long, long> progress)
            {
                var r = s.Boot(padded, p => progress?.Invoke(p.Message, p.Done, p.Total));
                return r.Error == null && r.BootCommandSent ? null : (r.Error ?? "the start command was not sent") + (r.Transferred ? " (after the image reached the console's memory)" : "");
            }
            public void Dispose() => s.Dispose();
        }

        sealed class SimulatedTarget : ITestBootTarget
        {
            readonly string folder;
            public SimulatedTarget(string folder) { this.folder = Path.GetFullPath(folder); if (!File.Exists(Path.Combine(this.folder, "console.json"))) throw new ConsoleRefusedException("no-console", "This folder is not a simulated console."); }
            public bool Simulated => true;
            public string SessionId { get; } = Guid.NewGuid().ToString("N");
            public string ReadSid() => SimulatedConsole.TestBootSid(folder);
            public string Send(byte[] padded, Action<string, long, long> progress) { progress?.Invoke("transferring the image to RAM at 0x47400000", padded.Length, padded.Length); return SimulatedConsole.TestBoot(folder, padded); }
            public void Dispose() { }
        }
    }

    public sealed class TestBootPlan
    {
        public string Class, Hwid, ConsoleTag, Session, Origin, Sha256, Md5, Digest, Nonce, Cmdline; public long Bytes, Padded; public DateTime ExpiresUtc; public bool Simulated;
        public SessionRestorePoint Point; public byte[] Image; public StartupImage Facts;
        public List<string> Differences = new List<string>(), Dependencies = new List<string>(), Notes = new List<string>();
        public string Notice => Class == TestBootEngine.OwnStartup ? TestBootEngine.OwnNotice : TestBootEngine.ImageNotice;
        public JObj ToJson() => new JObj
        {
            { "class", Class }, { "origin", Origin }, { "console_tag", ConsoleTag }, { "sha256", Sha256 }, { "md5", Md5 }, { "bytes", Bytes }, { "sent_bytes", Padded },
            { "kernel_bytes", (long)(Facts?.KernelSize ?? 0) }, { "ramdisk_bytes", (long)(Facts?.RamdiskSize ?? 0) }, { "ramdisk_format", Facts?.RamdiskFormat }, { "cmdline", Cmdline },
            { "cmdline_differences", Differences }, { "dependencies", Dependencies }, { "notes", Notes }, { "notice", Notice }, { "nothing_written_claim", false },
            { "restore_point", Point?.ToJson() }, { "restore_point_notice", SessionRestorePoint.NoticeText },
            { "digest", Digest }, { "nonce", Nonce }, { "expires_utc", ExpiresUtc.ToString("o", CultureInfo.InvariantCulture) },
        };
    }

    public sealed class TestBootOutcome { public string State, Problem, RecordFile; public bool Ok => State == "sent"; }

    public static class TestBootEngine
    {
        public const string OwnStartup = "own-startup", ImageFile = "image";
        public const string ImageNotice = "Code in this image controls the console once it starts. It can write the console's storage, startup stages or keys, and Workbench cannot stop that: loading it into memory and checking it does not make it safe, even though Workbench itself writes nothing to the storage.";
        public const string OwnNotice = "This starts your console's own startup software from memory instead of from its storage. Its system then runs as it always does: it saves games and settings to the console as usual.";
        /// <summary>what a way back for code that controls the console needs from the restore point</summary>
        public static readonly string[] Needs = { RestorePoints.Kernel, RestorePoints.SystemPart, RestorePoints.UserFiles, RestorePoints.BootStages };

        static string Sha(byte[] b) { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(b)); }

        /// <summary>the restore point for this connection: a verified backup of the console whose hardware id the console reports now</summary>
        public static (SessionRestorePoint point, string why) Point(ITestBootTarget t, string backup, Func<bool> cancelled)
        {
            var sid = t.ReadSid(); if (sid == null) return (null, "The console's hardware id could not be read. Nothing was sent.");
            return RestorePoints.EstablishFor(sid, t.Simulated, t.SessionId, null, backup, cancelled, "developer");   // the backup's own model must allow Developer Mode (the console's cannot be read here)
        }

        /// <summary>exactly n bytes of a member of a verified backup, checked against the backup's own record of it (size, SHA-256, MD5)</summary>
        internal static byte[] Member(string backup, string name, long max, out string why)
        {
            why = null;
            try
            {
                using (var z = ZipReader.Open(backup, BackupFormat.Allowed))
                {
                    var me = z.Find(BackupFormat.ManifestName); var e = z.Find(name);
                    if (me == null || e == null) { why = "the backup has no " + name; return null; }
                    Dictionary<string, object> m; using (var st = z.OpenMember(me)) using (var r = new StreamReader(st, Encoding.UTF8)) m = Json.Parse(r.ReadToEnd());
                    var rec = (m.A("members") ?? new List<object>()).OfType<Dictionary<string, object>>().FirstOrDefault(x => x.S("path") == name);
                    if (rec == null || rec.L("size") != e.Size || e.Size <= 0 || e.Size > max) { why = "its record of " + name + " does not fit"; return null; }
                    var b = new byte[e.Size]; using (var st = z.OpenMember(e)) { int n = 0, k; while (n < b.Length && (k = st.Read(b, n, b.Length - n)) > 0) n += k; if (n != b.Length) { why = name + " could not be read completely"; return null; } }
                    string md5; using (var h = MD5.Create()) md5 = Hex.Of(h.ComputeHash(b));
                    if (Sha(b) != rec.S("sha256") || md5 != rec.S("md5")) { why = name + " differs from the backup's record of it"; return null; }
                    return b;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is ZipFormatException || ex is FormatException) { why = "it could not be read: " + ex.Message; return null; }
        }

        /// <summary>the command-line words one image adds or removes compared with another (a key either carries is never shown)</summary>
        public static List<string> Differences(string now, string want)
        {
            var a = new HashSet<string>((now ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(StartupImage.Mask));
            var b = new HashSet<string>((want ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(StartupImage.Mask));
            return a.Except(b).OrderBy(x => x, StringComparer.Ordinal).Select(w => "removed: " + w).Concat(b.Except(a).OrderBy(x => x, StringComparer.Ordinal).Select(w => "added: " + w)).ToList();
        }

        /// <summary>what a startup image needs from the console that this check can or cannot confirm (the console's key for its system part)</summary>
        public static List<string> KeyDependencies(StartupImage f)
        {
            var d = new List<string>();
            if (!f.Words.Contains("decrypt")) return d;
            if (f.Words.Any(w => w.StartsWith("hakchi-key-file=", StringComparison.Ordinal))) d.Add("It opens the console's system part with the key its command line carries.");
            else if (f.KeyInRamdisk == true) d.Add("It opens the console's system part with the key file in its own ramdisk.");
            else if (f.HakchiKeyFallback) d.Add("It opens the console's system part with the key it reads from the console's own startup software (as hakchi's startup does).");
            else if (f.KeyInRamdisk == false) d.Add("It asks to open the console's system part, but its ramdisk holds no key file and its startup does not read one from the console: the system part will not open, and the console will not start its system with it.");
            else d.Add("It asks to open the console's system part. Whether it has the console's key could not be checked (its ramdisk is " + f.RamdiskFormat + "-compressed): without the key, the system part will not open.");
            return d;
        }

        public static (TestBootPlan plan, string why) Plan(ITestBootTarget t, SessionRestorePoint point, string cls, byte[] file, string origin, Func<bool> cancelled)
        {
            if (cls != OwnStartup && cls != ImageFile) return (null, "Choose what to start: your restore point's own startup software or a startup image file.");
            if (point == null) return (null, "Choose a restore point first: a confirmed backup of this console.");
            if (point.Session != t.SessionId) return (null, "The console was connected again, so this session needs its restore point chosen again.");
            var sid = t.ReadSid(); if (sid == null || !ConsoleIdentity.SameConsole(sid, point.Hwid)) return (null, "The console connected now is not the restore point's console. Nothing was sent.");
            foreach (var need in Needs) if (!point.Covers.Contains(need)) return (null, RestoreWhy(need));
            var own = Member(point.Archive.Path, "restore/boot2-active.img", 32L * 1024 * 1024, out var mwhy);
            if (own == null) return (null, "The restore point's startup software could not be used: " + mwhy + ".");
            var image = cls == OwnStartup ? own : file;
            if (image == null || image.LongLength > 64L * 1024 * 1024) return (null, "Choose a startup image file (an Android boot image) of at most " + Units.Gb(64L * 1024 * 1024) + ".");
            var f = StartupImage.Inspect(image);
            if (!f.Ok) return (null, (cls == OwnStartup ? "Your restore point's startup software cannot be started from memory: " : "This file cannot be started on the console: ") + string.Join("; ", f.Problems) + ".");
            var p = new TestBootPlan { Class = cls, Origin = cls == OwnStartup ? "the restore point's startup software (" + point.DateText + ")" : origin, Hwid = point.Hwid, ConsoleTag = point.ConsoleTag, Session = t.SessionId, Simulated = t.Simulated, Point = point, Facts = f, Sha256 = f.Sha256, Md5 = f.Md5, Bytes = f.Bytes, Padded = f.Padded, Cmdline = f.MaskedCmdline };
            p.Image = new byte[f.Padded]; Array.Copy(image, p.Image, f.Bytes);
            var ownFacts = StartupImage.Inspect(own);
            if (cls == ImageFile)
            {
                if (f.Sha256 == ownFacts.Sha256) p.Notes.Add("This file is the restore point's own startup software.");
                p.Differences.AddRange(Differences(ownFacts.Cmdline, f.Cmdline));
                p.Dependencies.AddRange(KeyDependencies(f));
                if (!f.Words.Contains("ro")) p.Dependencies.Add("Its command line does not ask for the system part read-only (\"ro\").");
                p.Dependencies.Add("Its kernel must run on this console and fit in memory once unpacked (below the ramdisk at 0x41000000); that cannot be checked from the file.");
            }
            p.Notes.Add("Workbench writes nothing to the console's storage for a test boot: the image goes to the console's memory only, once.");
            p.Notes.Add("To end the test, switch the console off. Started normally, it uses its own startup software from its storage" + (cls == ImageFile ? ", unless the image's code changed it." : "."));
            p.Notes.Add("If the console's storage or startup changes, connect it in recovery mode and use Console Restore with your restore point (" + point.DateText + "), or Console Rescue for its startup stages.");
            p.Notes.Add(point.Banner);
            p.Nonce = Hex.Of(Guid.NewGuid().ToByteArray()); p.ExpiresUtc = DateTime.UtcNow + RestoreEngine.ConfirmWindow; p.Digest = DigestOf(p);
            return (p, null);
        }

        static string RestoreWhy(string need) => need == RestorePoints.BootStages ? "This restore point does not hold this console's startup stages (boot0 and boot1) as Console Rescue needs them, so code that could change them is not started under it."
            : need == RestorePoints.SystemPart ? "This restore point does not hold the console's system part, so code that could change it is not started under it."
            : need == RestorePoints.Kernel ? "This restore point does not hold the console's startup software, so it cannot be test-started or brought back."
            : "This restore point cannot bring back the console's files, so code that could change them is not started under it.";

        static string DigestOf(TestBootPlan p)
        {
            var s = "ccs-dev-test-boot-plan/1\n" + p.Class + "\n" + ConsoleIdentity.NormalizeHwid(p.Hwid) + "\n" + p.Session + "\n" + p.Point?.Archive?.DigestLine() + "\n" + p.Sha256 + " " + p.Md5 + " " + p.Bytes.ToString(CultureInfo.InvariantCulture) + " " + p.Padded.ToString(CultureInfo.InvariantCulture) + "\n" + p.Nonce;
            using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(s)));
        }

        public static TestBootOutcome Execute(ITestBootTarget t, TestBootPlan plan, string digest, string nonce, EngineContext ctx)
        {
            var o = new TestBootOutcome();
            TestBootOutcome Refuse(string why) { o.State = "refused"; o.Problem = why; return o; }
            if (plan == null) return Refuse("No plan.");
            if (digest != plan.Digest || nonce != plan.Nonce || plan.Nonce == null || DigestOf(plan) != plan.Digest) return Refuse("The confirmation does not match the reviewed test boot. Nothing was sent.");
            if (DateTime.UtcNow > plan.ExpiresUtc) return Refuse("The confirmation is more than 10 minutes old. Review the test boot again. Nothing was sent.");
            if (t.SessionId != plan.Session || t.Simulated != plan.Simulated) return Refuse("The console was connected again after the review. Nothing was sent.");
            var sid = t.ReadSid(); if (sid == null || !ConsoleIdentity.SameConsole(sid, plan.Hwid)) return Refuse("The console connected now is not the one the test boot was reviewed for. Nothing was sent.");
            if (plan.Image == null || plan.Image.LongLength != plan.Padded) return Refuse("The image changed since the review. Nothing was sent.");
            var trimmed = new byte[plan.Bytes]; Array.Copy(plan.Image, trimmed, plan.Bytes);
            if (Sha(trimmed) != plan.Sha256) return Refuse("The image changed since the review. Nothing was sent.");
            if (!t.Simulated && !(Gate.ConsoleSessions && Gate.DeveloperTestBoot)) return Refuse(Gate.DeveloperTestBootText);
            using (var hold = Evidence.Hold.Open(plan.Point?.Archive?.Path))
            {
                if (hold.Problem != null) return Refuse("The restore point cannot be used (" + hold.Problem + "). Nothing was sent.");
                var rc = Evidence.Recheck(plan.Point.Archive, plan.Hwid, t.Simulated, null, ctx.Cancelled); if (rc != null) return Refuse(rc + " Nothing was sent.");
                Record rec;
                try { rec = Record.Start(ctx.Settings.RecordsFolder, Guid.NewGuid().ToString("N").Substring(0, 12), "developer-test-boot", new JObj { { "plan", plan.ToJson() }, { "console_tag", plan.ConsoleTag }, { "simulated", t.Simulated } }); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Refuse("The test boot record could not be saved (" + ex.Message + "), so nothing was sent."); }
                plan.Nonce = null; o.RecordFile = rec.File;
                rec.State("sending");
                var why = t.Send(plan.Image, (m, d, n) => ctx.Say("test-boot", m ?? "Sending the image to the console's memory...", d, n));
                if (why != null) { rec.State("stopped", why); o.State = "stopped"; o.Problem = "The image was not started (" + why + "). Workbench wrote nothing to the console's storage and sends nothing more on this connection. Switch the console off to end."; return o; }
                rec.Set("result", "sent; only the TV shows whether it started").State("sent");
                o.State = "sent"; return o;
            }
        }
    }
}
