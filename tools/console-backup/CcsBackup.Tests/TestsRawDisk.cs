// Classic Cart Studio console backup and restore engine tests: a connected USB drive or SD card read or written whole (External/RawDisk*.cs).
// GPL-3.0-or-later. No real drive is ever listed, opened, read or written here and nothing asks Windows for administrator permission: the
// PC is a stand-in (RawPc) whose drives are files these tests build, Workbench's administrator step runs on a thread of this process
// (RawDiskHost.Execute with that stand-in, told it is elevated), and the window and the step talk through a real named pipe of this
// process. The only real Windows function used is the link check on a junction made in the test folder. Group:
//   storage-raw   the request file, its single use and every refusal of the step (not elevated, a changed, swapped or moved request, a
//                 link, an old request, another program's request, no window, a replay, a window without the secret, a pipe owned by
//                 another process) and of the window (a refused permission prompt, a process other than the one started, a step without the
//                 secret); the target checks (gone, another drive, not USB or SD, the drive Windows runs from, a drive holding the backup or
//                 its location, unknown, spanning, no media, look-alikes); a whole drive copied into a storage backup (index, region
//                 checksums, nothing written to it, volumes in use, a read error, cancel) and restored onto another drive (read back from
//                 the drive, its record); cancel before and during the write; an interrupted, failing or differing write; a drive or backup
//                 that changed after the review; a last part sector; sector sizes that differ; the source drive itself.
using CcsBackup.Engine;
using CcsBackup.External;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        // ------------------------------------------------------------------ the stand-in PC
        internal sealed class RawDrive
        {
            public DiskIdentity Id; public string File; public List<DiskVolume> Volumes = new List<DiskVolume>();
            public bool LocksFail; public long FailReadAt = -1, FailWriteAt = -1, KillWriteAt = -1, FlipReadbackAt = -1;
            public long Reads, Writes; public int Opens, WriteOpens, Locked, Dismounted, Refreshed;
            public Action<long> AfterWrite, AfterRead;
        }

        internal sealed class RawPc : IRawDiskSystem
        {
            public string Kind => "fake"; public bool Elevated { get; set; } = true; public string SelfProgram { get; set; }
            public readonly Dictionary<int, RawDrive> Drives = new Dictionary<int, RawDrive>();
            public int Sys; public int[] Default = { 0 };
            public readonly List<(string prefix, int[] disks)> Of = new List<(string, int[])>();
            public readonly HashSet<string> Redirects = new HashSet<string>(StringComparer.OrdinalIgnoreCase), Unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public static string PathOf(int n, string serial) => @"\\?\usbstor#disk&ven_test&prod_drive_" + n + "#" + (serial ?? "none") + "&0#{53f56307-b6bf-11d0-94f2-00a0c91efb8b}";
            public RawDrive Add(int n, string file, string bus = "USB", string serial = "SERIAL", int sector = 512, long bytes = -1, string vendor = "Test", string product = "Drive")
            {
                var d = new RawDrive { File = file, Id = new DiskIdentity { Number = n, Path = PathOf(n, serial), Vendor = vendor, Product = product, Serial = serial, Bus = bus, RemovableMedia = bus != "SATA", Bytes = bytes >= 0 ? bytes : new FileInfo(file).Length, SectorBytes = sector } };
                Drives[n] = d; return d;
            }
            public List<DiskEntry> Disks() => Drives.Select(kv => new DiskEntry { Number = kv.Key, Path = kv.Value.Id.Path }).ToList();
            public DiskIdentity Describe(DiskEntry e) => Drives.TryGetValue(e.Number, out var x) && x.Id.Path == e.Path ? CloneId(x.Id) : null;
            public int[] DisksOf(string path)
            {
                var f = Path.GetFullPath(path);
                if (Unknown.Any(u => f.StartsWith(u, StringComparison.OrdinalIgnoreCase))) return null;
                foreach (var (p, ds) in Of.OrderByDescending(x => x.prefix.Length)) if (f.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return ds;
                return Default;
            }
            public int SystemDisk() => Sys;
            public List<DiskVolume> VolumesOn(int n) => Drives.TryGetValue(n, out var x) ? x.Volumes.ToList() : new List<DiskVolume>();
            public bool Redirected(string path) { var f = Path.GetFullPath(path); return Redirects.Any(r => f.StartsWith(r, StringComparison.OrdinalIgnoreCase)); }
            public string ProgramOf(int pid) => pid == Process.GetCurrentProcess().Id ? SelfProgram : null;
            public IRawDisk Open(DiskEntry e, bool write) { var x = Drives[e.Number]; x.Opens++; if (write) x.WriteOpens++; return new FakeRawDisk(x, write); }
            public IVolumeLock Lock(IList<DiskVolume> vols, bool dismount)
            {
                var l = new FakeLock();
                foreach (var v in vols)
                {
                    var owner = Drives.Values.FirstOrDefault(d => d.Volumes.Any(x => x.Name == v.Name));
                    if (owner == null) continue;
                    if (owner.LocksFail) { l.NotLocked.Add(v.Display + ": Access is denied (simulated: another program has it open)"); continue; }
                    owner.Locked++; if (dismount) owner.Dismounted++;
                }
                return l;
            }
            sealed class FakeLock : IVolumeLock { public List<string> NotLocked { get; } = new List<string>(); public void Dispose() { } }
        }

        internal static DiskIdentity CloneId(DiskIdentity id) => DiskIdentity.FromJson(Json.Parse(Json.Write(id.ToJson())));

        sealed class FakeRawDisk : IRawDisk
        {
            readonly RawDrive d; readonly bool write; readonly FileStream f;
            public FakeRawDisk(RawDrive d, bool write) { this.d = d; this.write = write; f = new FileStream(d.File, FileMode.Open, write ? FileAccess.ReadWrite : FileAccess.Read, FileShare.ReadWrite); }
            public DiskIdentity Identity() => CloneId(d.Id);
            public long Length => d.Id.Bytes; public int SectorBytes => d.Id.SectorBytes;
            void Whole(long o, int c) { if (o < 0 || o % SectorBytes != 0 || c <= 0 || c % SectorBytes != 0) throw new ArgumentException("not whole sectors (simulated drive)"); }
            public int Read(long o, byte[] b, int c)
            {
                if (o >= Length) return 0;
                c = (int)Math.Min(c, Length - o); Whole(o, c);
                if (d.FailReadAt >= 0 && o <= d.FailReadAt && d.FailReadAt < o + c) throw new IOException("Data error (cyclic redundancy check). (simulated)");
                f.Position = o; int got = 0; while (got < c) { int r = f.Read(b, got, c - got); if (r <= 0) break; got += r; }
                if (d.FlipReadbackAt >= 0 && d.Writes > 0 && o <= d.FlipReadbackAt && d.FlipReadbackAt < o + got) b[d.FlipReadbackAt - o] ^= 0xFF;
                d.Reads += got; d.AfterRead?.Invoke(d.Reads); return got;
            }
            public void Write(long o, byte[] b, int c)
            {
                if (!write) throw new IOException("the drive is not open for writing");
                Whole(o, c);
                if (d.KillWriteAt >= 0 && o + c > d.KillWriteAt) throw new InvalidOperationException("the administrator step was ended (simulated)");
                if (d.FailWriteAt >= 0 && o + c > d.FailWriteAt) throw new IOException("The device is not ready. (simulated)");
                f.Position = o; f.Write(b, 0, c); d.Writes += c; d.AfterWrite?.Invoke(d.Writes);
            }
            public void Flush() => f.Flush(true);
            public void Refresh() => d.Refreshed++;
            public void Dispose() => f.Dispose();
        }

        internal sealed class ThreadLauncher : IRawDiskLauncher
        {
            public RawPc Pc; public RawDiskPolicy Policy = RawDiskPolicy.UsbAndSd; public bool Decline; public int? ReportPid; public int Starts; public string LastArgs;
            /// <summary>something else answers instead of the step (an impostor), given the step's arguments</summary>
            public Func<string[], int> Instead;
            public readonly List<ThreadProc> Started = new List<ThreadProc>();
            public IRawDiskProcess Start(string arguments)
            {
                Starts++; LastArgs = arguments;
                if (Decline) throw new CcsBackup.Driver.ElevationCanceledException();
                var a = SplitArgs(arguments); var p = new ThreadProc { Pid = ReportPid ?? Process.GetCurrentProcess().Id };
                var instead = Instead;
                p.T = new Thread(() => { try { p.Code = instead != null ? instead(a) : RawDiskHost.Execute(a, Pc, Policy); } catch (Exception ex) { p.Code = 1; p.Error = ex; } }) { IsBackground = true, Name = "raw-disk step (test)" };
                p.T.Start(); Started.Add(p); return p;
            }
        }

        internal sealed class ThreadProc : IRawDiskProcess
        {
            public Thread T; public int Pid, Code; public Exception Error;
            public int Id => Pid; public bool HasExited => !T.IsAlive; public int ExitCode => Code;
            public bool WaitForExit(int ms) => T.Join(ms);
            public void Dispose() { }
        }

        internal static string[] SplitArgs(string s)
        {
            var l = new List<string>(); var sb = new StringBuilder(); bool q = false, any = false;
            foreach (var c in s)
            {
                if (c == '"') { q = !q; any = true; continue; }
                if (c == ' ' && !q) { if (any || sb.Length > 0) l.Add(sb.ToString()); sb.Clear(); any = false; continue; }
                sb.Append(c);
            }
            if (any || sb.Length > 0) l.Add(sb.ToString());
            return l.ToArray();
        }

        static (RawPc pc, ThreadLauncher l) RawSetup(string dir)
        {
            var pc = new RawPc { SelfProgram = Path.Combine(dir, "CCS-Console-Workbench.exe") };
            var l = new ThreadLauncher { Pc = pc };
            RawDiskClient.Launcher = _ => l; RawDiskClient.Program = () => pc.SelfProgram;
            RawDiskClient.TempRoot = Path.Combine(dir, "tmp"); Directory.CreateDirectory(RawDiskClient.TempRoot);
            StorageBackup.DiskQueries = pc; RawDiskHost.ConnectMs = 3000;
            return (pc, l);
        }

        static string HeadSha(string file, long n)
        {
            using (var f = File.OpenRead(file)) using (var h = SHA256.Create())
            {
                var b = new byte[1 << 20]; long left = n;
                while (left > 0) { int r = f.Read(b, 0, (int)Math.Min(b.Length, left)); if (r <= 0) break; h.TransformBlock(b, 0, r, null, 0); left -= r; }
                h.TransformFinalBlock(new byte[0], 0, 0); return Hex.Of(h.Hash);
            }
        }

        static string TailSha(string file, long from)
        {
            using (var f = File.OpenRead(file)) using (var h = SHA256.Create()) { f.Position = from; return Hex.Of(h.ComputeHash(f)); }
        }

        static string NewTargetFile(string dir, string name, long bytes, int seed)
        {
            var p = Path.Combine(dir, name);
            using (var f = new FileStream(p, FileMode.CreateNew)) { var b = Rnd(1 << 20, seed); for (long at = 0; at < bytes; at += b.Length) f.Write(b, 0, (int)Math.Min(b.Length, bytes - at)); }
            return p;
        }

        // ------------------------------------------------------------------ storage-raw
        public static void StorageRaw()
        {
            var saved = (RawDiskClient.Launcher, RawDiskClient.Program, RawDiskClient.TempRoot, StorageBackup.DiskQueries, RawDiskHost.ConnectMs, RawDiskHost.ConnectPipe);
            try { RawGate(); RawRequests(); RawTargets(); RawCopyAndRestore(); }
            finally { (RawDiskClient.Launcher, RawDiskClient.Program, RawDiskClient.TempRoot, StorageBackup.DiskQueries, RawDiskHost.ConnectMs, RawDiskHost.ConnectPipe) = saved; }
        }

        /// <summary>the compiled code: the real drive access and Windows' runas for it are made in exactly the places that need them</summary>
        static void RawGate()
        {
            Check(CcsBackup.Gate.StorageRawReads && CcsBackup.Gate.StorageDiskWrites, "the StorageRawReads and StorageDiskWrites boundaries are open in this version (no test reads or writes a real drive)");
            var asms = new List<System.Reflection.Assembly> { typeof(RawDiskHost).Assembly };
            var ui = FindBuilt("CcsBackupUi", "CcsBackupUi.dll"); if (ui != null) asms.Add(System.Reflection.Assembly.LoadFrom(ui));
            const System.Reflection.BindingFlags All = System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var sysT = typeof(WindowsRawDiskSystem); var runT = typeof(RunasRawDiskLauncher);
            var guarded = new HashSet<System.Reflection.MethodBase>(sysT.GetConstructors(All).Concat<System.Reflection.MethodBase>(runT.GetConstructors(All)).Concat(new[] { sysT.GetMethods(All).Single(m => m.Name == "Open" && m.IsStatic), runT.GetMethods(All).Single(m => m.Name == "Open" && m.IsStatic) }));
            var sites = new List<string>(); var execs = new List<string>(); int methods = 0;
            foreach (var a in asms)
                foreach (var t in SafeTypes(a))
                    foreach (var mb in t.GetMethods(All).Cast<System.Reflection.MethodBase>().Concat(t.GetConstructors(All)))
                    {
                        methods++;
                        foreach (var target in Calls(mb))
                        {
                            if (guarded.Contains(target) && !(target.IsConstructor && mb.Name == "Open" && mb.DeclaringType == target.DeclaringType))
                                sites.Add((mb.DeclaringType.DeclaringType ?? mb.DeclaringType).Name + "." + (mb.DeclaringType.DeclaringType != null ? "(lambda)" : mb.Name) + " -> " + target.DeclaringType.Name + "." + target.Name);
                            if (target.DeclaringType == typeof(RawDiskHost) && target.Name == "Execute") execs.Add(mb.DeclaringType.Name + "." + mb.Name);
                        }
                    }
            var want = new[] { "RawDiskClient.(lambda) -> RunasRawDiskLauncher.Open", "RawDiskHost.Run -> WindowsRawDiskSystem.Open" };
            Check(methods > 1000 && sites.OrderBy(x => x).SequenceEqual(want) && execs.SequenceEqual(new[] { "RawDiskHost.Run" }) && sysT.GetConstructors(All).All(c => c.IsPrivate) && runT.GetConstructors(All).All(c => c.IsPrivate),
                  "compiled code (" + methods + " methods): the drive opened whole is made only by the elevated mode's Run, the runas start only by the window's launcher, and only Run calls the step's Execute (" + string.Join("; ", sites) + ")");
            Check(Ldstrs(runT.GetMethod("Start")).Contains("runas"), "the compiled launcher asks Windows for 'runas'");
            Check(RawDiskHost.Flag == "--raw-disk" && CcsBackup.Driver.DriverActionHost.Flag != RawDiskHost.Flag, "the whole-drive step has its own command-line mode, apart from the driver step");
        }

        /// <summary>the step's request, its single use, and both sides' checks of who is at the other end</summary>
        static void RawRequests()
        {
            var dir = NewDir("raw-req"); var (pc, l) = RawSetup(dir);
            var disk = Path.Combine(dir, "drive5.img"); File.WriteAllBytes(disk, Rnd(3 << 20, 51)); var d5 = pc.Add(5, disk);
            RawDiskRequest NewReq(Action<RawDiskRequest> tune = null)
            {
                var r = new RawDiskRequest { Kind = "read", OperationId = Guid.NewGuid().ToString("N"), Nonce = Hex.Of(RawFrame.Random32()), Pipe = "ccs-raw-disk-" + Guid.NewGuid().ToString("N"),
                    WindowPid = Process.GetCurrentProcess().Id, WindowProgram = pc.SelfProgram, CreatedUtc = DateTime.UtcNow, Target = CloneId(d5.Id), RegionSize = 1 << 20 };
                tune?.Invoke(r); return r;
            }
            (string file, string sha) Req(Action<RawDiskRequest> tune = null) => RawDiskRequestFile.Write(NewReq(tune), RawDiskClient.TempRoot);
            int Exec(params string[] a) => RawDiskHost.Execute(a, pc, RawDiskPolicy.UsbAndSd);
            const string F = RawDiskHost.Flag;

            var (f1, s1) = Req(); pc.Elevated = false;
            Check(Exec(F, f1, s1) == RawDiskExit.NotElevated && File.Exists(f1) && d5.Opens == 0, "without administrator permission the step stops before reading anything, its request included");
            pc.Elevated = true;
            Check(Exec(F) == RawDiskExit.BadRequest && Exec(F, f1) == RawDiskExit.BadRequest && Exec(F, f1, "abc") == RawDiskExit.BadRequest && Exec("--other", f1, s1) == RawDiskExit.BadRequest && File.Exists(f1),
                  "an incomplete command line is refused without reading the request");
            var (f2, s2) = Req(); File.AppendAllText(f2, " ");
            Check(Exec(F, f2, s2) == RawDiskExit.BadRequest && !File.Exists(f2), "a request changed after the window wrote it (its SHA-256 differs) is refused, and used up");
            var (f3, _) = Req();
            Check(Exec(F, f3, s1) == RawDiskExit.BadRequest, "another request file put in place of the window's is refused");
            var elsewhere = Path.Combine(dir, "elsewhere"); Directory.CreateDirectory(elsewhere); var f4 = Path.Combine(elsewhere, RawDiskRequestFile.FileName); File.Copy(f1, f4);
            Check(Exec(F, f4, s1) == RawDiskExit.BadRequest && File.Exists(f4), "a request outside a request folder of its own is refused, not read");
            var (f5, s5) = Req(); pc.Redirects.Add(Path.GetDirectoryName(f5));
            Check(Exec(F, f5, s5) == RawDiskExit.BadRequest && File.Exists(f5), "a request reached through a link, junction or mount point is refused, not read");
            pc.Redirects.Clear();
            Check(Exec(F, Path.Combine(Path.GetFileName(Path.GetDirectoryName(f5)), RawDiskRequestFile.FileName), s5) == RawDiskExit.BadRequest, "a request path that is not a full path is refused");
            // the real link check (FindFirstFile's reparse tag) on a junction made here, around a folder of this test only
            var real = Path.Combine(dir, "real-folder"); Directory.CreateDirectory(real); File.WriteAllText(Path.Combine(real, "x.txt"), "x"); var jn = Path.Combine(dir, "junction-folder");
            var mk = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + jn + "\" \"" + real + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }); mk.StandardOutput.ReadToEnd(); mk.WaitForExit();
            Check(WindowsDiskQueries.Instance.Redirected(Path.Combine(jn, "x.txt")) && WindowsDiskQueries.Instance.Redirected(jn) && !WindowsDiskQueries.Instance.Redirected(Path.Combine(real, "x.txt")) && !WindowsDiskQueries.Instance.Redirected(Path.Combine(real, "not there yet.zip")),
                  "Windows' own reparse tag tells a path through a junction from an ordinary one (also for a file not made yet)");
            var (f6, s6) = Req(r => r.WindowProgram = @"C:\Other\Program.exe");
            Check(Exec(F, f6, s6) == RawDiskExit.BadRequest, "a request made by another program is refused");
            var (f7, s7) = Req(r => r.CreatedUtc = DateTime.UtcNow.AddHours(-1));
            Check(Exec(F, f7, s7) == RawDiskExit.BadRequest, "an old request is refused");
            var bad = new List<Action<RawDiskRequest>> { r => r.Nonce = "1234", r => r.OperationId = "x", r => r.Pipe = "some-other-pipe", r => r.Target = null, r => r.Kind = "erase", r => r.Target.Bytes = 0, r => r.RegionSize = 1000,
                r => { r.Kind = "write"; r.Image = null; }, r => r.Protect.Add(new ProtectedPath(@"relative\path", "x")) };
            Check(bad.All(t => { var (f, s) = Req(t); return Exec(F, f, s) == RawDiskExit.BadRequest; }) && d5.Opens == 0, "each malformed request is refused (" + bad.Count + " kinds) and no drive is opened");
            var (f8, s8) = Req();
            Check(Exec(F, f8, s8) == RawDiskExit.PeerRefused && !File.Exists(f8) && d5.Opens == 0, "with no window listening the step reads its request, finds no pipe and stops: nothing opened");
            Check(Exec(F, f8, s8) == RawDiskExit.BadRequest, "the same command line again finds no request: a request is used once");

            // the step facing a window of the test's own making
            (int code, string note) Against(Func<FrameChannelProxy, RawDiskRequest, string> window, Action<RawDiskRequest> tune = null)
            {
                var r = NewReq(tune);
                using (var srv = new NamedPipeServerStream(r.Pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                {
                    var (file, sha) = RawDiskRequestFile.Write(r, RawDiskClient.TempRoot); int code = -1;
                    var t = new Thread(() => code = Exec(F, file, sha)) { IsBackground = true }; t.Start();
                    var ar = srv.BeginWaitForConnection(null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(10000)) { t.Join(10000); return (code, "no connection"); }
                    srv.EndWaitForConnection(ar);
                    string note; try { note = window(new FrameChannelProxy(srv), r); } catch (Exception ex) { note = "window threw " + ex.Message; }
                    srv.Dispose(); t.Join(15000); return (code, note);
                }
            }
            var good = Against((ch, r) =>
            {
                var mine = RawFrame.Random32(); ch.Send(RawFrame.Challenge, mine);
                var h = RawFrame.P(ch.Take().payload); var ok = RawFrame.Same(h.S("proof"), RawFrame.Proof(r.Nonce, "helper", r.OperationId, mine));
                var theirs = Enumerable.Range(0, 32).Select(i => Convert.ToByte(h.S("challenge").Substring(2 * i, 2), 16)).ToArray();
                ch.Send(RawFrame.Accept, RawFrame.J(new JObj { { "proof", RawFrame.Proof(r.Nonce, "window", r.OperationId, theirs) } }));
                var ready = ch.Take(); ch.Send(RawFrame.Cancel); var res = RawFrame.P(ch.Take().payload);
                return (ok ? "proof ok" : "proof wrong") + "|" + ready.type + "|" + res?.S("state");
            });
            Check(good.code == RawDiskExit.CanceledNothingWritten && good.note == "proof ok|" + RawFrame.Ready + "|canceled" && d5.Reads == 0 && d5.Writes == 0,
                  "the step proves it holds the request's secret, checks the drive, says it is ready, and with a cancel before the go-ahead reads nothing (" + good.note + ")");
            var wrong = Against((ch, r) =>
            {
                var mine = RawFrame.Random32(); ch.Send(RawFrame.Challenge, mine); var h = RawFrame.P(ch.Take().payload);
                var theirs = Enumerable.Range(0, 32).Select(i => Convert.ToByte(h.S("challenge").Substring(2 * i, 2), 16)).ToArray();
                ch.Send(RawFrame.Accept, RawFrame.J(new JObj { { "proof", RawFrame.Proof(Hex.Of(RawFrame.Random32()), "window", r.OperationId, theirs) } }));
                return "sent a wrong proof";
            });
            Check(wrong.code == RawDiskExit.PeerRefused && d5.Opens == 1, "a window that cannot prove it holds the secret is refused before any drive is checked or opened");
            var silent = Against((ch, r) => "closed without a challenge");
            Check(silent.code == RawDiskExit.PeerRefused, "a window that never challenges the step is refused");
            var realConnect = RawDiskHost.ConnectPipe;
            RawDiskHost.ConnectPipe = (n, ms) => { var x = realConnect(n, ms); return (x.stream, x.serverPid + 1); };
            var other = Against((ch, r) => "owned by another process");
            RawDiskHost.ConnectPipe = realConnect;
            Check(other.code == RawDiskExit.PeerRefused && d5.Opens == 1, "a pipe owned by a process other than the request's window is refused");

            // the window facing a refused prompt, a process other than the one started, and an impostor without the secret
            var t5 = CloneId(d5.Id);
            int dirs0 = Directory.GetDirectories(RawDiskClient.TempRoot).Length;
            l.Decline = true; var dec = RawDiskClient.OpenRead(t5, 1 << 20, null, null); l.Decline = false;
            Check(dec.Declined && dec.Refusal == RawDiskClient.DeclinedText("read") && dec.Process == null,
                  "a refused permission prompt: the window says so plainly; nothing was started (" + dec.Refusal + ")");
            dec.Dispose();
            Check(Directory.GetDirectories(RawDiskClient.TempRoot).Length == dirs0, "the window removes its request folder when the session ends");
            l.ReportPid = Process.GetCurrentProcess().Id + 7; var pid = RawDiskClient.OpenRead(t5, 1 << 20, null, null); l.ReportPid = null;
            Check(pid.Refusal != null && pid.Refusal.StartsWith("A program other than"), "a process other than the one Windows started is refused at the pipe (" + pid.Refusal + ")");
            pid.Dispose();
            l.Instead = a =>
            {
                var r = RawDiskRequest.Parse(File.ReadAllBytes(a[1]));   // an impostor that can read the request but not prove the secret it would need
                var c = new NamedPipeClientStream(".", r.Pipe, PipeDirection.InOut); c.Connect(3000);
                var ch = new FrameChannelProxy(c); var chal = ch.Take().payload;
                ch.Send(RawFrame.Hello, RawFrame.J(new JObj { { "operation_id", r.OperationId }, { "proof", RawFrame.Proof(Hex.Of(RawFrame.Random32()), "helper", r.OperationId, chal) }, { "challenge", Hex.Of(RawFrame.Random32()) } }));
                Thread.Sleep(500); c.Dispose(); return 0;
            };
            var imp = RawDiskClient.OpenRead(t5, 1 << 20, null, null); l.Instead = null;
            Check(imp.Refusal != null && imp.Refusal.StartsWith("Workbench could not confirm"), "a step that cannot prove it holds the secret is refused by the window (" + imp.Refusal + ")");
            imp.Dispose();
            Check(d5.Reads == 0 && d5.Writes == 0, "after every refusal above, nothing was read from or written to the drive");
        }

        /// <summary>a stand-in for the window's side of the pipe in these tests</summary>
        internal sealed class FrameChannelProxy
        {
            readonly Stream s; public FrameChannelProxy(Stream s) { this.s = s; }
            public void Send(byte t, byte[] p = null) => RawFrame.Write(s, t, p ?? new byte[0], p?.Length ?? 0);
            public (byte type, byte[] payload) Take() { var f = RawFrame.Read(s); if (f == null) throw new IOException("the step ended the pipe"); return f.Value; }
        }

        /// <summary>RawDiskTarget.Check on the stand-in PC: every reason a drive is never a target</summary>
        static void RawTargets()
        {
            var dir = NewDir("raw-targets"); var (pc, _) = RawSetup(dir);
            pc.Add(0, null, "USB", "SYS-0", bytes: 64_000_000_000); pc.Sys = 0;
            pc.Add(1, null, "SATA", "SATA-1", bytes: 500_000_000_000);
            pc.Add(2, null, "USB", "HOLD-2", bytes: 32_000_000_000);
            pc.Add(3, null, "USB", null, bytes: 16_000_000_000, product: "Twin"); pc.Add(4, null, "USB", null, bytes: 16_000_000_000, product: "Twin");
            pc.Add(6, null, "SD", "EMPTY", bytes: 0);
            var span = pc.Add(7, null, "USB", "SPAN-7", bytes: 8_000_000_000); span.Volumes.Add(new DiskVolume { Name = @"\\?\Volume{77}\", Label = "BIG", FileSystem = "NTFS", Bytes = 8_000_000_000, SpansOtherDisks = true });
            var ok = pc.Add(8, null, "SD", "CARD-8", bytes: 32_000_000_000); ok.Volumes.Add(new DiskVolume { Name = @"\\?\Volume{88}\", Mounts = { @"F:\" }, Label = "CARD", FileSystem = "FAT32", Bytes = 32_000_000_000 });
            pc.Add(9, null, "FileBackedVirtual", "VHD-9", bytes: 1_000_000_000);
            var backupDir = Path.Combine(dir, "on drive 2"); pc.Of.Add((Path.GetFullPath(backupDir), new[] { 2 }));
            var lost = Path.Combine(dir, "unknown place"); pc.Unknown.Add(Path.GetFullPath(lost));
            var protect = new[] { new ProtectedPath(Path.Combine(backupDir, "backup.zip"), "the backup being restored") };
            TargetCheck C(DiskIdentity want, IEnumerable<ProtectedPath> p = null) => RawDiskTarget.Check(pc, RawDiskPolicy.UsbAndSd, want, p ?? protect);
            DiskIdentity Id(int n) => CloneId(pc.Drives[n].Id);
            var c8 = C(Id(8));
            Check(c8.Ok && c8.Now.Serial == "CARD-8" && c8.Volumes.Count == 1 && c8.Entry.Path == pc.Drives[8].Id.Path, "an SD card that passes every check: its identity and volumes, its device path as Windows lists it");
            var gone = Id(8); gone.Number = 42; gone.Path = RawPc.PathOf(42, "CARD-8");
            Check(C(gone).Refusal == RawDiskRefusal.Gone, "a drive Windows no longer lists is refused");
            var swapped = Id(8); swapped.Serial = "ANOTHER";
            var sw = C(swapped); Check(sw.Refusal == RawDiskRefusal.NotSame && sw.Text.Contains("serial number"), "another drive now at that number (its serial number differs) is refused: " + sw.Text);
            var resized = Id(8); resized.Bytes = 31_000_000_000;
            Check(C(resized).Refusal == RawDiskRefusal.NotSame, "a drive whose size is not the reviewed size is refused");
            Check(C(Id(1)).Refusal == RawDiskRefusal.NotUsbOrSd && C(Id(9)).Refusal == RawDiskRefusal.NotUsbOrSd, "an internal (SATA) drive and a virtual hard disk are never targets: only USB drives and SD cards");
            Check(RawDiskTarget.Check(pc, RawDiskPolicy.WithVirtualForTests(), Id(9), protect).Ok && !RawDiskPolicy.UsbAndSd.Allows("FileBackedVirtual"), "the test machines' policy alone also allows virtual disks; the product's never does");
            Check(C(Id(0)).Refusal == RawDiskRefusal.SystemDisk, "the drive Windows runs from is refused, even when connected by USB");
            Check(C(Id(2)).Refusal == RawDiskRefusal.HoldsProtected && C(Id(2)).Text.Contains("the backup being restored"), "a drive holding the backup is refused, naming it");
            Check(C(Id(8), new[] { new ProtectedPath(Path.Combine(lost, "x"), "your backup location") }).Refusal == RawDiskRefusal.UnknownDrive, "a protected place whose drive cannot be determined refuses every target");
            Check(C(Id(3)).Refusal == RawDiskRefusal.LookAlike, "two drives with the same maker, model and size and no serial number cannot be told apart: refused");
            Check(C(Id(6)).Refusal == RawDiskRefusal.NoMedia, "a card reader with no card (no size) is refused");
            Check(C(Id(7)).Refusal == RawDiskRefusal.Spans, "a drive with a volume that spans another drive is refused");
            pc.Sys = -1; Check(C(Id(8)).Refusal == RawDiskRefusal.UnknownDrive, "when Windows does not say which drive it runs from, nothing is a target"); pc.Sys = 0;
            var always = RawDiskTarget.Always(@"C:\Program Files\Workbench\CCS-Console-Workbench.exe");
            Check(always.Count == 2 && always.Any(x => x.What == "Windows") && always.Any(x => x.What.StartsWith("this program")), "Windows and this program's own file are always protected");
            var d = Id(8); var j = DiskIdentity.FromJson(Json.Parse(Json.Write(d.ToJson())));
            var img = new RawImagePlan { Backup = @"C:\x\b.zip", BackupBytes = 300, ImageBytes = 2 << 20, SectorBytes = 512, RegionSize = 1 << 20, Regions = { (10L, 1L << 20, new string('a', 64)), (20L, 1L << 20, new string('b', 64)) } };
            var back = RawImagePlan.FromJson(Json.Parse(Json.Write(img.ToJson())));
            Check(back?.Regions != null && back.Regions.Count == 2 && back.Regions[1] == (20L, 1L << 20, new string('b', 64)) && back.BackupBytes == 300, "a write's image plan keeps every region through its request file (the reader's nested arrays)");
            Check(d.Differs(j) == null && d.ToSource().Kind == SourceKind.Disk && d.ToSource().Key == j.ToSource().Key && d.Display == "SD card Test Drive, 32.0 GB", "a drive identity keeps every field through its record (" + d.Display + ")");
        }

        /// <summary>a whole drive copied into a storage backup, restored onto another drive, and every way either can stop</summary>
        static void RawCopyAndRestore()
        {
            var dir = NewDir("raw-flow"); var (pc, l) = RawSetup(dir); var s = StorageSettings(Path.Combine(dir, "loc"), false);
            var (disk, _) = DiskA(dir, 21); long len = new FileInfo(disk).Length; var diskSha = Sha(disk);
            var src = pc.Add(5, disk, "USB", "SRC-5"); src.Volumes.Add(new DiskVolume { Name = @"\\?\Volume{55}\", Mounts = { @"E:\" }, Label = "CCSFAT32", FileSystem = "FAT32", Bytes = 36L << 20 });
            StorageOutcome Copy(int n = 5, Action<StorageOptions> tune = null, DiskIdentity chosen = null)
            {
                var o = new StorageOptions { Settings = s, SecondCopy = false, Entry = "tests", ToolName = "CcsBackup.Tests" }; tune?.Invoke(o);
                return StorageBackup.Run(new[] { new StorageRequest { Mode = "image", DiskNumber = n, Disk = chosen ?? (pc.Drives.ContainsKey(n) ? CloneId(pc.Drives[n].Id) : null) } }, o);
            }
            int Files() => Directory.GetFiles(StorageBackup.Folder(s)).Length;

            // ---- copied whole
            var o1 = Copy();
            var v = o1.Ok ? StorageVerifier.Verify(o1.PrimaryPath) : null;
            Check(o1.Ok && o1.ScopeComplete && o1.Protected && v != null && v.Ok, "a connected USB drive is copied whole into a checked storage backup through the administrator step (" + o1.Problem + ")");
            string joined = null;
            if (v != null)
            {
                using (var z = File.OpenRead(o1.PrimaryPath)) using (var h = SHA256.Create())
                {
                    var lay = v.Layouts["s01"]; int nreg = (int)lay.L("regions"); var b = new byte[1 << 20];
                    for (int i = 1; i <= nreg; i++) { var e = v.Entries.First(x => x.Name == ExternalFormat.Region("s01", i)); z.Position = e.DataOffset; long left = e.Size; while (left > 0) { int r = z.Read(b, 0, (int)Math.Min(b.Length, left)); h.TransformBlock(b, 0, r, null, 0); left -= r; } }
                    h.TransformFinalBlock(new byte[0], 0, 0); joined = Hex.Of(h.Hash);
                }
                var id = SourceIdentity.FromJson(v.SourceList[0].O("identity"));
                Check(joined == diskSha && v.Layouts["s01"].L("bytes") == len && id.Kind == SourceKind.Disk && id.DiskSerial == "SRC-5" && id.SectorBytes == 512,
                      "the stored regions are the drive's bytes exactly, and the backup names the drive (kind disk, its serial number, its sector size)");
                Check(v.Index["s01"].Any(x => x.S("path") == "/README.TXT") && v.Index["s01"].Any(x => x.S("path") != null && x.S("path").Contains("Twin.txt")), "the drive's FAT32 files are listed in the backup's index, as for an image file");
                Check(o1.ConsistencyText.Contains("volumes were locked"), "the backup says the drive's volumes were locked while it was read: " + o1.ConsistencyText);
            }
            Check(src.Writes == 0 && src.WriteOpens == 0 && src.Locked >= 1 && src.Dismounted == 0, "nothing was written to the drive; its volume was locked for the read, not dismounted");
            Check(Directory.GetDirectories(RawDiskClient.TempRoot).Length == 0, "the request was used up and its folder is gone");

            // ---- every way a copy is refused or stops
            int before = Files();
            l.Decline = true; var dcl = Copy(); l.Decline = false;
            Check(dcl.State == "refused" && dcl.ElevationDeclined && dcl.Problem.StartsWith("Windows did not give administrator permission") && Files() == before, "a refused permission prompt: nothing copied, no file made (" + dcl.Problem + ")");
            src.LocksFail = true; var iu = Copy(); src.LocksFail = false;
            Check(iu.Ok && !iu.ScopeComplete && !iu.Protected && iu.Sources[0].Problems.Any(p => p.Contains("another program had the drive open")) && iu.ConsistencyText.Contains("could not all be locked"),
                  "volumes another program has open: the copy is checked, but never complete protection, and says why (" + iu.ConsistencyText + ")");
            before = Files(); src.FailReadAt = 70L << 20; var re = Copy(); src.FailReadAt = -1;
            Check(re.State == "failed" && re.Problem.Contains("could not read the drive") && Files() == before, "a read error part way: the backup stops, its unfinished file is removed (" + re.Problem + ")");
            before = Files(); var cr = Copy(tune: o => o.Cancelled = () => src.Reads > (40L << 20));
            Check(cr.State == "canceled" && Files() == before, "canceled while the drive is read: no backup is kept");
            pc.Of.Add((Path.GetFullPath(s.PrimaryFolder), new[] { 5 })); var self = Copy(); pc.Of.Clear();
            Check(self.State == "refused" && self.Problem.Contains("backup location is on the drive being copied"), "a drive holding the backup location is never copied onto itself");
            var other = CloneId(src.Id); other.Serial = "NOT-THIS-ONE"; var oc = Copy(chosen: other);
            Check(oc.State == "refused" && oc.Problem.Contains("not the one you chose"), "a drive that is not the one chosen (its serial number differs) is refused before anything is asked");
            var sata = pc.Add(11, disk, "SATA", "SATA-11"); int starts = l.Starts; var sc = Copy(11);
            Check(sc.State == "refused" && sc.Problem.Contains("not a USB drive or SD card") && l.Starts == starts, "an internal drive is refused in the window, before Windows is asked for anything");
            pc.Drives.Remove(11);
            var two = StorageBackup.Run(new[] { new StorageRequest { Mode = "image", DiskNumber = 5 }, new StorageRequest { Mode = "image", DiskNumber = 6 } }, new StorageOptions { Settings = s, SecondCopy = false });
            Check(two.State == "refused" && two.Problem.Contains("one connected drive whole"), "one backup copies one connected drive whole");

            // ---- restored onto another drive
            var bk = o1.PrimaryPath; long tsize = 160_000_000;
            var tf = NewTargetFile(dir, "drive6.img", tsize, 61); var tgt = pc.Add(6, tf, "SD", "CARD-6");
            tgt.Volumes.Add(new DiskVolume { Name = @"\\?\Volume{66}\", Mounts = { @"F:\" }, Label = "OLDCARD", FileSystem = "exFAT", Bytes = tsize - 4_000_000 });
            ImageRestorePlan Plan(int n = 6, DiskIdentity id = null, string backup = null) => StorageRestore.PlanImage(backup ?? bk, "s01", new DiskTarget { DiskNumber = n, Disk = id ?? CloneId(pc.Drives[n].Id) }, pc, null, StorageRestore.Protected(s));
            var tailBefore = TailSha(tf, len);
            var p = Plan();
            Check(p.Refusal == null && p.TargetDisk.Serial == "CARD-6" && p.TargetText.Contains("serial number CARD-6") && p.TargetText.Contains("SD card slot") && p.Review.Any(x => x.Contains("is lost") && x.Contains("does not back it up first"))
                  && p.Review.Any(x => x.Contains("F:") && x.Contains("OLDCARD")) && p.Review.Any(x => x.Contains("larger than the image")) && !p.SameAsSource,
                  "the review names the card (maker, model, size, serial number, how it is connected), what is on it now and what is lost (" + p.Refusal + ")");
            var rr = StorageRestore.RestoreImage(p, p.Digest, p.Nonce, s);
            var rec = Directory.GetFiles(s.RecordsFolder, "disk-restore-*.json").Select(Record.Load).ToList();
            Check(rr.Ok && rr.DiskState == "complete" && HeadSha(tf, len) == diskSha && TailSha(tf, len) == tailBefore && tgt.Dismounted == 1 && tgt.Refreshed == 1 && tgt.Reads >= len,
                  "the image is written onto the card and read back from the card; its rest is untouched; its volume was dismounted and Windows asked to read its partition table again (" + rr.Text + ")");
            Check(rec.Count == 1 && rec[0].S("state") == "complete" && rec[0].O("result")?.S("state") == "complete", "the restore's record says complete");
            Check(!StorageRestore.RestoreImage(p, p.Digest, p.Nonce, s).Ok && tgt.WriteOpens == 1, "a reviewed plan is carried out once");
            Check(StorageRestore.UnfinishedDiskRestores(s).Count == 0, "nothing unfinished is reported after a complete restore");

            // ---- the ways a restore stops, with exactly what is known
            string Fill() { using (var f = new FileStream(tf, FileMode.Open)) { var b = Rnd(1 << 20, 62); for (long at = 0; at < tsize; at += b.Length) f.Write(b, 0, (int)Math.Min(b.Length, tsize - at)); } return Sha(tf); }
            StorageRestoreResult Restore(Func<bool> cancel = null, ImageRestorePlan plan = null) { var x = plan ?? Plan(); return x.Refusal != null ? new StorageRestoreResult { Text = x.Refusal } : StorageRestore.RestoreImage(x, x.Digest, x.Nonce, s, cancel); }
            var sha0 = Fill();
            l.Decline = true; var d1 = Restore(); l.Decline = false;
            Check(d1.DiskState == "declined" && d1.NothingWritten && !d1.Ok && Sha(tf) == sha0 && d1.Text.StartsWith("Windows did not give administrator permission"), "a refused permission prompt: nothing written to the card (" + d1.Text + ")");
            var c0 = Restore(() => true);
            Check(c0.DiskState == "canceled" && c0.NothingWritten && Sha(tf) == sha0, "canceled before the go-ahead: nothing written (" + c0.Text + ")");
            bool stop = false; long writesBefore = tgt.Writes; tgt.AfterWrite = n => { if (n - writesBefore > (40L << 20)) stop = true; };   // counted from this restore's start
            var cm = Restore(() => stop); tgt.AfterWrite = null;
            Check(cm.DiskState == "canceled-incomplete" && !cm.NothingWritten && !cm.Ok && cm.Bytes > (40L << 20) && cm.Bytes < len && Sha(tf) != sha0 && cm.Text.Contains("cannot be relied on"),
                  "canceled while writing: the card is reported incomplete, with how much was written (" + cm.Text + ")");
            var unf = StorageRestore.UnfinishedDiskRestores(s);
            Check(unf.Count == 1 && unf[0].state == "canceled-incomplete" && unf[0].target.Contains("CARD-6"), "the unfinished restore is listed until that card is restored completely");
            Fill(); tgt.KillWriteAt = 30L << 20; var ki = Restore(); tgt.KillWriteAt = -1;
            Check(ki.DiskState == "interrupted" && !ki.NothingWritten && !ki.Ok && ki.Text.Contains("may hold part of the image"), "the administrator step ended part way through the write: reported as an incomplete card, never as finished or unchanged (" + ki.Text + ")");
            Fill(); tgt.FailWriteAt = 20L << 20; var fw = Restore(); tgt.FailWriteAt = -1;
            Check(fw.DiskState == "stopped-incomplete" && fw.Text.Contains("The device is not ready") && fw.Text.Contains("Nothing is retried"), "a write error: writing stops, says where and why, and nothing is retried (" + fw.Text + ")");
            Fill(); tgt.FlipReadbackAt = 5L << 20; var rb = Restore(); tgt.FlipReadbackAt = -1;
            Check(rb.DiskState == "read-back-differs" && !rb.Ok && rb.Problems.Contains("Region 1 reads back differently from the backup."), "a region that reads back differently from the card: not called done (" + rb.Text + ")");
            var shaL = Fill(); tgt.LocksFail = true; var lk = Restore(); tgt.LocksFail = false;
            Check(lk.DiskState == "refused" && lk.NothingWritten && lk.Text.Contains("Another program is using the drive") && Sha(tf) == shaL, "another program using the card: refused, nothing written (" + lk.Text + ")");
            var sha1 = Sha(tf); var ps = Plan(); tgt.Id.Serial = "SWAPPED"; var sw = Restore(plan: ps); tgt.Id.Serial = "CARD-6";
            Check(sw.DiskState == "refused" && sw.NothingWritten && Sha(tf) == sha1 && sw.Text.Contains("serial number"), "another card put in after the review: the step refuses it, nothing written (" + sw.Text + ")");
            // the backup changed after the review while keeping its size and time: the step finds the region that no longer matches
            var bcopy = Path.Combine(dir, "copy of backup.zip"); File.Copy(bk, bcopy); var pc2 = Plan(backup: bcopy);
            var r2 = StorageVerifier.Verify(bcopy).Entries.First(x => x.Name == ExternalFormat.Region("s01", 2)); var t0 = File.GetLastWriteTimeUtc(bcopy);
            StFlip(bcopy, r2.DataOffset + 1000); File.SetLastWriteTimeUtc(bcopy, t0);
            var bc = Restore(plan: pc2);
            Check(bc.DiskState == "stopped-incomplete" && bc.Text.Contains("Region 2 of the backup does not match its recorded checksum"), "a backup whose region changed after the review is caught while writing: the step stops there (" + bc.Text + ")");
            var done = Restore();
            Check(done.Ok && HeadSha(tf, len) == diskSha && StorageRestore.UnfinishedDiskRestores(s).Count == 0, "a complete restore onto the card afterward: the earlier unfinished ones are no longer listed");
            // the window's record when the window itself stops while writing: the record still says writing
            var stale = Record.Start(s.RecordsFolder, "20991231-000000-deadbeef", "disk-restore", new JObj { { "target", CloneId(tgt.Id).ToJson() }, { "target_text", "SD card Test Drive, serial number CARD-6" } }); stale.State("writing");
            Check(StorageRestore.UnfinishedDiskRestores(s).Any(x => x.state == "writing"), "a record left saying writing (the window closed during a write) is listed as unfinished");
            File.Delete(stale.File);

            // ---- refusals at the review
            pc.Sys = 6; Check(Plan().Refusal?.Contains("Windows runs from") == true, "the drive Windows runs from is refused at the review"); pc.Sys = 0;
            pc.Of.Add((Path.GetFullPath(Path.GetDirectoryName(bk)), new[] { 6 })); var hb = Plan(); pc.Of.Clear();
            Check(hb.Refusal?.Contains("the backup being restored") == true, "a card holding the backup is refused: " + hb.Refusal);
            pc.Of.Add((Path.GetFullPath(s.RecordsFolder), new[] { 6 })); var hl = Plan(); pc.Of.Clear();
            Check(hl.Refusal?.Contains("Workbench's records") == true, "a card holding Workbench's records (in the backup location) is refused: " + hl.Refusal);
            var smallFile = NewTargetFile(dir, "drive7.img", 50_000_000, 71); pc.Add(7, smallFile, "USB", "SMALL-7");
            Check(Plan(7).Refusal?.Contains("smaller") == true, "a drive smaller than the image is refused");
            var bigSector = NewTargetFile(dir, "drive8.img", 160_006_144, 81); pc.Add(8, bigSector, "USB", "4K-8", sector: 4096);
            Check(Plan(8).Refusal?.Contains("4,096-byte sectors") == true, "a drive with 4,096-byte sectors is refused for an image made with 512-byte sectors: " + Plan(8).Refusal);
            pc.Add(12, tf, "SATA", "SATA-12"); Check(Plan(12).Refusal?.Contains("not a USB drive or SD card") == true, "an internal drive is refused at the review"); pc.Drives.Remove(12);

            // ---- onto the drive the backup was made from: allowed, and named so
            File.WriteAllBytes(disk, Rnd((int)Math.Min(len, 8 << 20), 91).Concat(new byte[len - Math.Min(len, 8 << 20)]).ToArray());
            var sp = Plan(5);
            Check(sp.Refusal == null && sp.SameAsSource && sp.Review.Any(x => x.Contains("looks like the drive the backup was made from")), "restoring onto the drive the backup was made from is allowed after the review says so");
            var sr = Restore(plan: sp);
            Check(sr.Ok && Sha(disk) == diskSha, "the drive the backup was made from is put back exactly as it was copied");

            // ---- an image whose end falls inside a sector: the drive's own bytes after it are kept
            var odd = Path.Combine(dir, "odd.img"); var oddBytes = Rnd(3_000_100, 101); File.WriteAllBytes(odd, oddBytes);
            var oo = StorageBackup.Run(new[] { new StorageRequest { Path = odd, Mode = "image" } }, new StorageOptions { Settings = s, SecondCopy = false });
            var tf9 = Path.Combine(dir, "drive9.img"); File.WriteAllBytes(tf9, Enumerable.Repeat((byte)0xEE, 4_000_256).ToArray()); pc.Add(9, tf9, "USB", "ODD-9");
            var p9 = StorageRestore.PlanImage(oo.PrimaryPath, "s01", new DiskTarget { DiskNumber = 9, Disk = CloneId(pc.Drives[9].Id) }, pc, null, StorageRestore.Protected(s));
            var r9 = p9.Refusal == null ? StorageRestore.RestoreImage(p9, p9.Digest, p9.Nonce, s) : new StorageRestoreResult { Text = p9.Refusal };
            var got9 = File.ReadAllBytes(tf9);
            Check(r9.Ok && got9.Take(oddBytes.Length).SequenceEqual(oddBytes) && got9.Skip(oddBytes.Length).All(b => b == 0xEE), "an image ending inside a sector: written exactly, and the drive's bytes after it in that sector kept (" + r9.Text + ")");
            Check(Directory.GetDirectories(RawDiskClient.TempRoot).Length == 0, "every request folder is gone after these sessions");
        }
    }
}
