// Classic Cart Studio console backup and restore engine tests: capture, memory, faults, verification and history. GPL-3.0-or-later.
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
        static Dictionary<string, string> DeviceHashes(string console) =>
            Directory.GetFiles(Path.Combine(console, "dev")).ToDictionary(f => Path.GetFileName(f), f => Sha(f));

        static byte[] Member(string zip, string name) { using (var z = ZipReader.Open(zip, BackupFormat.Allowed)) { var m = new MemoryStream(); using (var s = z.OpenMember(z.Find(name))) s.CopyTo(m); return m.ToArray(); } }
        static string MemberSha(string zip, string name) { using (var z = ZipReader.Open(zip, BackupFormat.Allowed)) { var h = new HashingSink(null); using (var s = z.OpenMember(z.Find(name))) s.CopyTo(h); return h.Sha256; } }

        // shared between groups (made once)
        static string stockConsole, stockBackup, modConsole, modBackup; static Settings capSettings;

        public static void Capture()
        {
            var dir = NewDir("capture"); capSettings = SettingsIn(dir);
            // 1. a stock console
            stockConsole = Console_("stock");
            var before = DeviceHashes(stockConsole);
            var o = Backup(stockConsole, capSettings);
            Check(o.State == "complete" && o.TwoCopies && o.SameDisk, "stock: backup complete with two verified copies, reported as on the same disk (" + o.State + " " + o.Problem + ")");
            stockBackup = o.PrimaryPath;
            Check(DeviceHashes(stockConsole).SequenceEqual(before) && !File.Exists(Path.Combine(stockConsole, "writes.log")), "stock: the console was only read (every storage part unchanged, no write)");
            var v = Verifier.VerifyFile(o.PrimaryPath);
            Check(v.Ok && v.RestoreReady && v.RecoveryBaseline && v.StateKind == "stock" && v.OriginalSource == "active", "stock: restore-ready, a recovery baseline, original kernel = the active one (" + string.Join("; ", v.Problems) + ")");
            Check(Sha(o.PrimaryPath) == Sha(o.ArchivePath), "stock: the second copy is byte-identical to the first");
            foreach (var t in new[] { "nanda", "nandb", "nandc", "nandd", "nande" })
                Check(MemberSha(o.PrimaryPath, "restore/" + t + ".img") == before[t], "stock: restore/" + t + ".img is exactly the console's " + t);
            Check(MemberSha(o.PrimaryPath, "reference/nand-physical.bin") == before["phy"] && MemberSha(o.PrimaryPath, "restore/boot2-active.img") == before["boot2"], "stock: the physical image and the startup software are exact");
            Check(Member(o.PrimaryPath, BackupFormat.OriginalKernelName).SequenceEqual(StockKernel()), "stock: recovery/original-kernel.img is the genuine stock kernel");
            var m = v.Manifest;
            Check(m.O("scope").S("text") == BackupFormat.ScopeText && m.O("export").S("status") == "none", "stock: the scope says built-in storage only; no Classic Cart Studio HOME, so no .ccs export (" + m.O("export").S("reason") + ")");
            var hist = History.List(capSettings).FirstOrDefault(e => e.PrimaryPath == o.PrimaryPath);
            Check(hist != null && hist.Status == BackupStatus.Verified && hist.TwoCopies, "stock: the history shows it Verified, two copies");
            var pz = Py("import zipfile\nz=zipfile.ZipFile(r'" + o.PrimaryPath + "')\nprint(z.testzip(), sorted(i.filename for i in z.infolist())[0], len(z.infolist()))\n");
            Check(pz == null || pz.Value.output.Trim().StartsWith("None CCS/") || pz.Value.output.Trim().StartsWith("None README"), "stock: a standard ZIP that Python's zipfile reads and tests: " + pz?.output.Trim());
            // 2. a modified console with the original kernel in hakchi's spare slot
            modConsole = Console_("modified");
            var o2 = Backup(modConsole, capSettings);
            modBackup = o2.PrimaryPath;
            var v2 = Verifier.VerifyFile(o2.PrimaryPath);
            Check(o2.Ok && v2.Ok && v2.StateKind == "modified" && v2.OriginalSource == "slot68" && v2.RecoveryBaseline, "modified: complete; the original kernel found in the spare slot; a recovery baseline (" + string.Join("; ", v2.Problems) + ")");
            Check(!Member(o2.PrimaryPath, "restore/boot2-active.img").SequenceEqual(Member(o2.PrimaryPath, BackupFormat.OriginalKernelName)) && Member(o2.PrimaryPath, BackupFormat.OriginalKernelName).SequenceEqual(StockKernel()), "modified: the active (modified) kernel and the original kernel are distinct members; the modified one is not called corrupt");
            Check(v2.Manifest.O("export").S("status") == "included", "modified: the console's Classic Cart Studio HOME is exported as CCS/Console-HOME.ccs");
            var ds = v2.Manifest.O("data_partition")?.O("summary");
            var nModified = Json.Parse(File.ReadAllText(Path.Combine(Fx, "listing-data-modified.json"))).A("entries").Count;
            Check(ds != null && ds.L("entries") == nModified && v2.Manifest.O("scope").A("external_storage").Any(x => ((string)x).Contains("USB")), "modified: all " + nModified + " data-storage entries (as debugfs lists them) are described, with the USB-storage note");
            // 3. modified without the original kernel: kept, restore-ready, but not a recovery baseline
            var o3 = Backup(Console_("modified-noorig"), capSettings);
            var v3 = Verifier.VerifyFile(o3.PrimaryPath);
            Check(o3.Ok && v3.Ok && v3.RestoreReady && !v3.RecoveryBaseline && v3.Manifest.A("recovery_baseline_missing").Any(), "no original kernel: the snapshot is kept and restore-ready, but it is NOT a recovery baseline (stated)");
            // 4. refusals before any read: nothing in the folder afterwards except earlier files
            void Refused(string what, Action<string> edit, string expect)
            {
                var c = Console_("modified-noorig"); edit(c);
                var d = NewDir("refuse"); var s = SettingsIn(d);
                var r = Backup(c, s);
                Check(r.State == "refused" && r.Problem.Contains(expect) && !Directory.Exists(s.PrimaryFolder) || r.State == "refused" && r.Problem.Contains(expect) && !Directory.EnumerateFiles(s.PrimaryFolder).Any(), what + ": refused before reading (" + r.Problem + ")");
            }
            void Cfg(string c, string k, object v0) { var p = Path.Combine(c, "console.json"); var d = Json.Parse(File.ReadAllText(p)); var j = new JObj(); foreach (var kv in d) j.Add(kv.Key, kv.Value); j.Set(k, v0); File.WriteAllText(p, Json.Write(j)); }
            Refused("another console model", c => Cfg(c, "board", "dp-hvc"), "not an NES Classic");
            Refused("an unknown storage layout", c => Cfg(c, "nandinfo", "4096 64 4096"), "layout");
            Refused("storage in use", c => File.WriteAllText(Path.Combine(c, "faults.json"), "{\"storage_mounts\":\"/dev/nandc:/var/lib:rw;\"}"), "in use");
            Refused("no readable identity", c => Cfg(c, "hwid", "00000000 00000000 00000000 00000000"), "identity");
            Refused("an unknown partition", c => Cfg(c, "parttable", "mbr:mbr:32 nanda:boot:6144 nandb:rootfs:38912 nandc:secret:806912 nandd:private:2048 nande:UDISK:26624"), "does not know");
            var cw = Console_("modified-noorig"); var dw = NewDir("refuse-image"); var sw = SettingsIn(dw);
            SimulatedConsole.Boot(cw, "some-other-image", SessionProfile.Backup);
            var wr = CaptureEngine.Run(new RecoveryConsole(new SimulatedConsole(cw), SessionProfile.Backup, true), Ctx(sw));
            Check(wr.State == "refused" && wr.Problem.Contains("not running"), "a console running another program is refused (" + wr.Problem + ")");
            // 5. two backups in the same minute get unique names; an earlier unfinished file is never replaced
            var u1 = Backup(modConsole, capSettings);
            Check(u1.Ok && u1.PrimaryPath != modBackup && File.Exists(modBackup), "a second backup never replaces the first (" + Path.GetFileName(u1.PrimaryPath) + ")");
        }

        public static void Memory()
        {
            if (stockConsole == null) throw new SkipException("needs the capture group");
            var dir = NewDir("memory"); var s = SettingsIn(dir, false);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var me = Process.GetCurrentProcess(); me.Refresh(); long baseBytes = me.PrivateMemorySize64, peak = baseBytes; bool run = true;
            var sampler = new Thread(() => { while (run) { me.Refresh(); peak = Math.Max(peak, me.PrivateMemorySize64); Thread.Sleep(25); } }) { IsBackground = true }; sampler.Start();
            var o = Backup(stockConsole, s); run = false; sampler.Join();
            long streamed = new FileInfo(o.PrimaryPath).Length;
            Check(o.State == "complete-one-copy" && streamed > 900L * 1000 * 1000, "a full-size backup streamed " + Units.Gb(streamed) + " (one copy, as set)");
            Check(peak - baseBytes < 300L * 1000 * 1000, "memory stayed bounded while " + Units.Gb(streamed) + " streamed: private bytes grew by " + Units.Gb(peak - baseBytes) + " at most");
        }

        public static void Faults()
        {
            if (modConsole == null) throw new SkipException("needs the capture group");
            BackupOutcome F(string fault, Action<EngineContext> tune = null, bool second = false, string console = null)
            {
                var c = console ?? Console_("modified-noorig");
                if (fault != null) File.WriteAllText(Path.Combine(c, "faults.json"), fault);
                var d = NewDir("fault"); var s = SettingsIn(d, second);
                var o = Backup(c, s, tune); o.RecordFile = o.RecordFile ?? ""; return o;
            }
            void Incomplete(BackupOutcome o, string what, string expect)
            {
                var st = o.RecordFile.Length > 0 ? Record.Load(o.RecordFile)?.S("state") : null;
                Check((o.State == "stopped" || o.State == "canceled") && o.Problem.Contains(expect) && File.Exists(o.PartialPath) && !File.Exists(o.PartialPath.Replace(".partial", "")) && (st == "stopped" || st == "canceled"),
                    what + ": " + o.State + ", the unfinished file keeps its .partial name, no final file, record " + st + " (" + o.Problem + ")");
            }
            Incomplete(F("{\"read\":{\"target\":\"nandc\",\"mode\":\"disconnect\",\"after\":50000000}}"), "the USB connection ends while the data storage is read", "stopped");
            Incomplete(F("{\"read\":{\"target\":\"phy\",\"mode\":\"short\",\"after\":100000000}}"), "the console's read of the raw storage ends short", "read error");
            Incomplete(F("{\"read\":{\"target\":\"nandb\",\"mode\":\"corrupt\",\"after\":1000}}"), "a byte changes on the way to the PC", "differ");
            Incomplete(F("{\"read\":{\"target\":\"nandd\",\"mode\":\"status\",\"after\":0}}"), "the console reports a read error", "read error");
            Incomplete(F("{\"read\":{\"target\":\"nande\",\"mode\":\"lie\",\"after\":0}}"), "the console's own check disagrees with the bytes received", "differ");
            long n = 0; Incomplete(F(null, ctx => { ctx.Cancelled = () => Interlocked.Read(ref n) > 0; ctx.Progress = p => { if (p.Done > 200L * 1000 * 1000) Interlocked.Exchange(ref n, 1); }; }), "Cancel while reading", "Canceled");
            Incomplete(F(null, ctx => ctx.WrapFile = (w, s) => w == "primary" ? new FailingStream(s, 120L * 1000 * 1000, "There is not enough space on the disk.") : s), "the PC's disk fills up", "not enough space");
            var oc = F(null, ctx => ctx.WrapFile = (w, s) => w == "copy" ? new FailingStream(s, 30L * 1000 * 1000, "The device is not ready.") : s, true);
            var rc = Record.Load(oc.RecordFile);
            Check(oc.State == "complete-one-copy" && oc.CopyProblem.Contains("not ready") && rc.S("state") == "complete-one-copy" && File.Exists(oc.PrimaryPath) && Verifier.VerifyFile(oc.PrimaryPath).Ok, "the second copy fails: the first copy is complete and verified, and the result says only one copy exists (" + oc.CopyProblem + ")");
            var he = History.List(Settings.Load(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(oc.RecordFile))), "settings.json"))).FirstOrDefault(e => e.PrimaryPath == oc.PrimaryPath);
            Check(he != null && he.Status == BackupStatus.Verified && !he.TwoCopies && he.StatusText == "Verified, one copy", "the history says \"Verified, one copy\", never two");
            // a folder that cannot be written: refused before anything is read
            var d2 = NewDir("denied"); var s2 = SettingsIn(d2, false); Directory.CreateDirectory(s2.PrimaryFolder);
            var user = Environment.UserDomainName + "\\" + Environment.UserName;
            Run("icacls", "\"" + s2.PrimaryFolder + "\" /deny \"" + user + "\":(OI)(CI)(W,WD,AD)");
            try
            {
                var c2 = Console_("modified-noorig"); var o2 = Backup(c2, s2);
                Check(o2.State == "refused" && o2.Problem.Contains("cannot be written"), "a backup folder this account cannot write to is refused before anything is read (" + o2.Problem + ")");
            }
            finally { Run("icacls", "\"" + s2.PrimaryFolder + "\" /remove:d \"" + user + "\""); }
        }

        sealed class FailingStream : Stream
        {
            readonly Stream s; readonly long after; readonly string msg; long n;
            public FailingStream(Stream s, long after, string msg) { this.s = s; this.after = after; this.msg = msg; }
            public override void Write(byte[] b, int o, int c) { if (n + c > after) throw new IOException(msg); s.Write(b, o, c); n += c; }
            public override bool CanRead => s.CanRead; public override bool CanSeek => s.CanSeek; public override bool CanWrite => true;
            public override long Length => s.Length; public override long Position { get => s.Position; set => s.Position = value; }
            public override void Flush() => s.Flush(); public override int Read(byte[] b, int o, int c) => s.Read(b, o, c);
            public override long Seek(long o, SeekOrigin so) => s.Seek(o, so); public override void SetLength(long v) => s.SetLength(v);
        }

        public static void VerifyAndHistory()
        {
            if (modBackup == null) throw new SkipException("needs the capture group");
            var dir = NewDir("tamper");
            // a copy of a good backup, then damage: one byte inside a stored member
            var p = Path.Combine(dir, "copy.zip"); File.Copy(modBackup, p);
            using (var z = ZipReader.Open(p, BackupFormat.Allowed)) { var e = z.Find("restore/nandd.img"); z.Dispose(); using (var f = new FileStream(p, FileMode.Open, FileAccess.ReadWrite)) { f.Position = e.DataOffset + 4096; int b = f.ReadByte(); f.Position = e.DataOffset + 4096; f.WriteByte((byte)(b ^ 1)); } }
            var v = Verifier.VerifyFile(p);
            Check(!v.Ok && v.Problems.Any(x => x.Contains("nandd") && (x.Contains("CRC") || x.Contains("checksum"))), "one changed byte inside a member is found (" + string.Join("; ", v.Problems) + ")");
            // the manifest rewritten to claim more than the contents show (an editable "verified"-like flag)
            var p2 = Path.Combine(dir, "claims.zip");
            var py = Py("import zipfile,json\nsrc=zipfile.ZipFile(r'" + modBackup + "')\ndst=zipfile.ZipFile(r'" + p2 + "','w')\nfor i in src.infolist():\n  b=src.read(i.filename)\n  if i.filename=='ccs-console-backup.json':\n    m=json.loads(b); m['state']['kind']='stock'; m['console']['tag']='00000000'; b=json.dumps(m).encode()\n  dst.writestr(i, b)\ndst.close()\n");
            if (py == null) Check(false, "Python is required for the rewritten-manifest case");
            else { var v2 = Verifier.VerifyFile(p2); Check(!v2.Ok, "a manifest that claims another state or console is refused (" + string.Join("; ", v2.Problems) + ")"); }
            // a member removed (manifest unchanged)
            var p3 = Path.Combine(dir, "missing.zip");
            var py3 = Py("import zipfile\nsrc=zipfile.ZipFile(r'" + modBackup + "')\ndst=zipfile.ZipFile(r'" + p3 + "','w')\nfor i in src.infolist():\n  if i.filename!='restore/nanda.img': dst.writestr(i, src.read(i.filename))\ndst.close()\n");
            if (py3 != null) { var v3 = Verifier.VerifyFile(p3); Check(!v3.Ok && v3.Problems.Any(x => x.Contains("nanda")), "a missing restore member is refused (" + string.Join("; ", v3.Problems) + ")"); }
            // the history: Changed after the file is altered (its record says complete; that is history, not authority)
            var s = SettingsIn(NewDir("hist"), false);
            var c = Console_("modified-noorig"); var o = Backup(c, s);
            using (var f = new FileStream(o.PrimaryPath, FileMode.Open, FileAccess.ReadWrite)) { f.Position = 5000000; int b = f.ReadByte(); f.Position = 5000000; f.WriteByte((byte)(b ^ 0x80)); }
            File.SetLastWriteTimeUtc(o.PrimaryPath, DateTime.UtcNow.AddMinutes(1));
            var h = History.List(s).First(e => e.PrimaryPath == o.PrimaryPath);
            Check(h.Status == BackupStatus.Changed, "a backup changed after its check shows \"Changed since it was checked\", not Verified");
            // records that say "complete" for a file whose bytes were changed but whose time was put back: the history may say Verified,
            // but every use verifies the file again and refuses it
            var rd = Record.Load(o.RecordFile); var j = new JObj(); foreach (var kv in rd) j.Add(kv.Key, kv.Value);
            var prim = (Dictionary<string, object>)rd["primary"]; var pj = new JObj(); foreach (var kv in prim) pj.Add(kv.Key, kv.Value);
            pj.Set("mtime_utc", File.GetLastWriteTimeUtc(o.PrimaryPath).ToString("o", System.Globalization.CultureInfo.InvariantCulture)); j.Set("primary", pj);
            Durable.WriteAtomic(o.RecordFile, Json.Write(j));
            var h2 = History.List(s).First(e => e.PrimaryPath == o.PrimaryPath);
            var v4 = Verifier.VerifyFile(o.PrimaryPath);
            Check(h2.Status == BackupStatus.Verified && !v4.Ok, "an edited record can make the history say Verified, but the file itself fails verification at use (" + string.Join("; ", v4.Problems).Substring(0, Math.Min(120, string.Join("; ", v4.Problems).Length)) + ")");
            // a .partial file is listed as Incomplete
            var pp = Path.Combine(s.PrimaryFolder, "NES Classic Backup 2026-11-14 0000 (console abcdef12).zip.partial"); File.WriteAllBytes(pp, new byte[1000]);
            Check(History.List(s).Any(e => e.PartialPath == pp && e.Status == BackupStatus.Incomplete), "an unfinished file found in the folder is listed as Incomplete");
            // a ZIP with no record (copied in from elsewhere) needs verification
            var foreign = Path.Combine(s.PrimaryFolder, "copied in.zip"); File.Copy(modBackup, foreign);
            Check(History.List(s).Any(e => (e.PrimaryPath == foreign || e.ArchivePath == foreign) && e.Status == BackupStatus.NeedsVerification), "a backup file without a record is listed as Needs verification");
        }
    }
}
