// Classic Cart Studio console backup engine: PAR2 parity through the CCS Console Workbench's native host. Copyright (C) 2026 Rance9.
// GPL-3.0-or-later.
// The standalone Workbench is a native executable (tools/console-backup/native-host) that runs this managed engine from memory and has
// par2cmdline's libpar2 (GPL-2.0-or-later) linked into it. Before the managed entry point runs, the host stores the address of its ABI
// table (native-host/src/ccs_host_abi.h) as the AppDomain data item "CcsNativeHost.Abi"; this class reads it, checks the table's magic,
// version and sizes, and calls the three functions through delegates. Nothing is extracted and no DLL is loaded by name.
// Classic Cart Studio for Windows has no native host: it installs the same parity code as ccs-par2.dll beside the app (build_host.sh --dll;
// never extracted), and this class loads that file from the app's own folder by full path and reads the same table through its export.
// Anywhere else (the tests, the managed executable started directly) neither is present and every call returns NotHosted with a plain
// sentence, never an exception.
// Each call is synchronous: run it on a background thread. Progress arrives on that same thread; cancel through the token.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace CcsBackup.Repair
{
    public enum Par2Status
    {
        Ok = 0, DamagedRepairable = 1, DamagedNotRepairable = 2, ParityUnusable = 3, RepairFailed = 4, Canceled = 5, BadArgument = 6,
        IoError = 7, OutOfMemory = 8, InternalError = 9, NotNeeded = 10, Busy = 11,
        /// <summary>not a host status: this process was not started by the Workbench's native host</summary>
        NotHosted = 100,
    }

    public enum Par2Stage { ReadingData = 1, Computing = 2, Writing = 3, LoadingParity = 4, ScanningData = 5, Repairing = 6, CheckingOutput = 7 }

    /// <summary>what one call found or did (the host's ccs_par2_report)</summary>
    public sealed class Par2Report
    {
        public Par2Status Status; public int LibraryResult = -1;
        public uint BlockSize, SourceBlocks, RecoveryBlocks, RecoveryFiles, DamagedBlocks, AvailableBlocks;
        public long SourceBytes, ParityBytes;
        public string Message;
        public bool Ok => Status == Par2Status.Ok;
        public override string ToString() => Status + ": " + Message;
    }

    public static class Par2Native
    {
        public const string AbiDataName = "CcsNativeHost.Abi";
        public const string NotHostedText = "Parity protection (PAR2) needs the standalone CCS Console Workbench or Classic Cart Studio for Windows with its parity library (ccs-par2.dll).";
        public const string DllName = "ccs-par2.dll";
        const ulong Magic = 0x3154534f48534343UL;   // "CCSHOST1"
        const uint AbiVersion = 1;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int ProgressFn(IntPtr context, int stage, int permille);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        delegate int CreateFn([MarshalAs(UnmanagedType.LPWStr)] string data, [MarshalAs(UnmanagedType.LPWStr)] string par2, uint pct, uint volumes,
                              ulong memory, ProgressFn progress, IntPtr context, IntPtr cancel, ref NativeReport report);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        delegate int VerifyFn([MarshalAs(UnmanagedType.LPWStr)] string par2, [MarshalAs(UnmanagedType.LPWStr)] string data, ulong memory,
                              ProgressFn progress, IntPtr context, IntPtr cancel, ref NativeReport report);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        delegate int RepairFn([MarshalAs(UnmanagedType.LPWStr)] string par2, [MarshalAs(UnmanagedType.LPWStr)] string data,
                              [MarshalAs(UnmanagedType.LPWStr)] string output, ulong memory, ProgressFn progress, IntPtr context, IntPtr cancel,
                              ref NativeReport report);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NativeReport
        {
            public uint size; public int status; public int library_result;
            public uint block_size, source_blocks, recovery_blocks, recovery_files, damaged_blocks, available_blocks, reserved;
            public ulong source_bytes, parity_bytes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)] public string message;
        }

        // the table (ccs_host_abi): magic u64 @0, version u32 @8, size u32 @12, report_size u32 @16, reserved u32 @20, then pointers
        // host_version @24, host_arch @+1, par2_version @+2, par2_create @+3, par2_verify @+4, par2_repair @+5
        sealed class Abi { public IntPtr Table; public bool FromLibrary; public string HostVersion, HostArch, Par2Version; public CreateFn Create; public VerifyFn Verify; public RepairFn Repair; }
        /// <summary>true when the parity functions come from the app's installed ccs-par2.dll rather than the single-file host</summary>
        public static bool FromLibrary => abi.Value?.FromLibrary == true;
        static readonly Lazy<Abi> abi = new Lazy<Abi>(Find, LazyThreadSafetyMode.ExecutionAndPublication);
        static string whyNot;

        static Abi Find()
        {
            object d = null;
            try { d = AppDomain.CurrentDomain.GetData(AbiDataName); } catch { }
            IntPtr t; bool lib = false;
            if (d is long address && address != 0) t = new IntPtr(address);
            else { t = TableFromAppLibrary(); lib = true; if (t == IntPtr.Zero) { whyNot = NotHostedText; return null; } }
            try
            {
                // version 2 appended the HOME renderer after these fields (HomeRenderNative reads them); the parity part is unchanged
                if ((ulong)Marshal.ReadInt64(t, 0) != Magic || (uint)Marshal.ReadInt32(t, 8) < AbiVersion) { whyNot = NotHostedText + " (this host's parity interface is a different version)"; return null; }
                int p = IntPtr.Size;
                if ((uint)Marshal.ReadInt32(t, 12) < 24 + 6 * p || (uint)Marshal.ReadInt32(t, 16) != Marshal.SizeOf(typeof(NativeReport)))
                { whyNot = NotHostedText + " (this host's parity interface has a different layout)"; return null; }
                IntPtr F(int i) => Marshal.ReadIntPtr(t, 24 + i * p);
                var a = new Abi
                {
                    Table = t, FromLibrary = lib, HostVersion = Marshal.PtrToStringUni(F(0)), HostArch = Marshal.PtrToStringUni(F(1)), Par2Version = Marshal.PtrToStringUni(F(2)),
                };
                if (F(3) == IntPtr.Zero || F(4) == IntPtr.Zero || F(5) == IntPtr.Zero) { whyNot = NotHostedText + " (this host was built without parity)"; return null; }
                a.Create = (CreateFn)Marshal.GetDelegateForFunctionPointer(F(3), typeof(CreateFn));
                a.Verify = (VerifyFn)Marshal.GetDelegateForFunctionPointer(F(4), typeof(VerifyFn));
                a.Repair = (RepairFn)Marshal.GetDelegateForFunctionPointer(F(5), typeof(RepairFn));
                return a;
            }
            catch (Exception ex) { whyNot = NotHostedText + " (" + ex.GetType().Name + ")"; return null; }
        }

        /// <summary>true in the standalone Workbench (the native host passed its parity functions)</summary>
        public static bool Available => abi.Value != null;
        /// <summary>a plain sentence when not Available, else null</summary>
        public static string UnavailableReason { get { var _ = abi.Value; return _ == null ? whyNot : null; } }
        public static string HostVersion => abi.Value?.HostVersion;
        public static string HostArchitecture => abi.Value?.HostArch;
        public static string LibraryVersion => abi.Value?.Par2Version;

        /// <summary>Makes parity for ONE finished file (the protected inner ZIP): "&lt;par2Path&gt;" (the index) and its volume files beside it.
        /// redundancyPercent 1..100; volumes 1..32; memoryLimit bytes (0 = 256 MB). Nothing is changed when it fails or is canceled.</summary>
        public static Par2Report Create(string dataPath, string par2Path, int redundancyPercent, int volumes = 1, long memoryLimit = 0,
                                        Action<Par2Stage, int> progress = null, CancellationToken cancel = default)
        {
            var a = abi.Value; if (a == null) return NotHosted();
            if (redundancyPercent < 1 || redundancyPercent > 100) throw new ArgumentOutOfRangeException(nameof(redundancyPercent));
            return Call(progress, cancel, (ProgressFn fn, IntPtr c, ref NativeReport r) => a.Create(dataPath, par2Path, (uint)redundancyPercent, (uint)Math.Max(volumes, 0), (ulong)Math.Max(memoryLimit, 0), fn, IntPtr.Zero, c, ref r), out _);
        }

        /// <summary>Checks ONE protected file against its parity (reads only).</summary>
        public static Par2Report Verify(string par2Path, string dataPath, long memoryLimit = 0, Action<Par2Stage, int> progress = null, CancellationToken cancel = default)
        {
            var a = abi.Value; if (a == null) return NotHosted();
            return Call(progress, cancel, (ProgressFn fn, IntPtr c, ref NativeReport r) => a.Verify(par2Path, dataPath, (ulong)Math.Max(memoryLimit, 0), fn, IntPtr.Zero, c, ref r), out _);
        }

        /// <summary>Rebuilds a damaged file into the NEW file outputPath (which must not exist); the damaged file and the parity are only read.</summary>
        public static Par2Report Repair(string par2Path, string dataPath, string outputPath, long memoryLimit = 0, Action<Par2Stage, int> progress = null,
                                        CancellationToken cancel = default)
        {
            var a = abi.Value; if (a == null) return NotHosted();
            return Call(progress, cancel, (ProgressFn fn, IntPtr c, ref NativeReport r) => a.Repair(par2Path, dataPath, outputPath, (ulong)Math.Max(memoryLimit, 0), fn, IntPtr.Zero, c, ref r), out _);
        }

        delegate int Invoke(ProgressFn fn, IntPtr cancelCell, ref NativeReport report);

        static Par2Report Call(Action<Par2Stage, int> progress, CancellationToken cancel, Invoke invoke, out int rc)
        {
            var nr = new NativeReport { size = (uint)Marshal.SizeOf(typeof(NativeReport)), message = "" };
            // the cancel flag lives in unmanaged memory the host polls; the token's registration is disposed before it is freed
            IntPtr cell = Marshal.AllocHGlobal(4);
            Marshal.WriteInt32(cell, 0);
            ProgressFn fn = (ctx, stage, permille) =>
            {
                try { progress?.Invoke((Par2Stage)stage, permille); } catch { return 1; }   // an exception must not cross into native code
                return cancel.IsCancellationRequested ? 1 : 0;
            };
            try
            {
                using (cancel.Register(() => Marshal.WriteInt32(cell, 1)))
                    rc = invoke(fn, cell, ref nr);
            }
            finally
            {
                GC.KeepAlive(fn);
                Marshal.FreeHGlobal(cell);
            }
            return new Par2Report
            {
                Status = (Par2Status)nr.status, LibraryResult = nr.library_result, BlockSize = nr.block_size, SourceBlocks = nr.source_blocks,
                RecoveryBlocks = nr.recovery_blocks, RecoveryFiles = nr.recovery_files, DamagedBlocks = nr.damaged_blocks, AvailableBlocks = nr.available_blocks,
                SourceBytes = (long)nr.source_bytes, ParityBytes = (long)nr.parity_bytes, Message = nr.message ?? "",
            };
        }

        static Par2Report NotHosted() => new Par2Report { Status = Par2Status.NotHosted, Message = UnavailableReason ?? NotHostedText };

        // ---------- the hosted self-test (a diagnostic: "--parity-selftest FOLDER" writes only inside FOLDER, which must be new or empty) ----------

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryExW(string path, IntPtr reserved, uint flags);
        const uint LoadSearchDllDir = 0x100, LoadSearchSystem32 = 0x800;   // the library's own folder (for nothing: it imports only Windows DLLs) and System32

        /// <summary>the app's installed parity library (ccs-par2.dll in the app's own folder, by full path), or IntPtr.Zero</summary>
        static IntPtr TableFromAppLibrary()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DllName);
                if (!File.Exists(path)) return IntPtr.Zero;
                var h = LoadLibraryExW(path, IntPtr.Zero, LoadSearchDllDir | LoadSearchSystem32); if (h == IntPtr.Zero) return IntPtr.Zero;
                var f = GetProcAddress(h, "ccs_host_get_abi");
                return f == IntPtr.Zero ? IntPtr.Zero : ((GetAbiFn)Marshal.GetDelegateForFunctionPointer(f, typeof(GetAbiFn)))();
            }
            catch (Exception) { return IntPtr.Zero; }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false)] static extern IntPtr GetProcAddress(IntPtr module, string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr GetAbiFn();

        /// <summary>the table found by the executable's export instead of AppDomain data (a cross-check of the same pointer; IntPtr.Zero when
        /// the process executable has no such export, as when the managed executable runs directly)</summary>
        public static IntPtr TableByExport()
        {
            var f = GetProcAddress(GetModuleHandleW(null), "ccs_host_get_abi");
            return f == IntPtr.Zero ? IntPtr.Zero : ((GetAbiFn)Marshal.GetDelegateForFunctionPointer(f, typeof(GetAbiFn)))();
        }

        /// <summary>Round trip on a synthetic file: create parity, verify intact, damage the file, verify detects it, rebuild into a new file,
        /// compare hashes, plus the refusal and cancel paths. One JSON object per line on output; returns 0 when every check passed.</summary>
        public static int SelfTest(string folder, TextWriter output, long sizeBytes = 8_000_000, int redundancyPercent = 10)
        {
            int failed = 0;
            void Line(string json) { output.WriteLine(json); output.Flush(); }
            void Check(string name, bool ok, string detail) { if (!ok) failed++; Line("{\"check\":\"" + name + "\",\"ok\":" + (ok ? "true" : "false") + ",\"detail\":\"" + Esc(detail) + "\"}"); }
            string R(Par2Report r) => r.Status + " | " + r.Message + " | blocks " + r.SourceBlocks + " x " + r.BlockSize + ", recovery " + r.RecoveryBlocks + ", damaged " + r.DamagedBlocks + ", parity bytes " + r.ParityBytes;

            var proc = Process.GetCurrentProcess();
            Line("{\"process_image\":\"" + Esc(proc.MainModule.FileName) + "\",\"entry_assembly_null\":" + (System.Reflection.Assembly.GetEntryAssembly() == null ? "true" : "false") +
                 ",\"apartment\":\"" + Thread.CurrentThread.GetApartmentState() + "\",\"clr\":\"" + Environment.Version + "\",\"is64\":" + (Environment.Is64BitProcess ? "true" : "false") +
                 ",\"process_arch\":\"" + RuntimeInformation.ProcessArchitecture + "\",\"os_arch\":\"" + RuntimeInformation.OSArchitecture +
                 "\",\"target_framework\":\"" + Esc(AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName ?? "(none)") +
                 "\",\"legacy_path_handling\":" + Switch("Switch.System.IO.UseLegacyPathHandling") + ",\"block_long_paths\":" + Switch("Switch.System.IO.BlockLongPaths") + "}");
            var byData = abi.Value?.Table ?? IntPtr.Zero; var byExport = TableByExport();
            Check("abi-appdomain-data", Available, Available ? "table 0x" + byData.ToString("x") + ", host " + HostVersion + " " + HostArchitecture + ", " + LibraryVersion : UnavailableReason);
            if (FromLibrary) Check("abi-library-cross-check", byData == TableFromAppLibrary(), "the table from " + DllName + " beside the app (no native host in this process)");
            else Check("abi-export-cross-check", byExport != IntPtr.Zero && byExport == byData, "export table 0x" + byExport.ToString("x"));
            if (!Available) return 1;

            if (string.IsNullOrEmpty(folder) || !Path.IsPathRooted(folder)) { Check("folder", false, "give a full path to a new or empty folder"); return 2; }
            if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) { Check("folder-empty", false, folder + " is not empty"); return 1; }
            Directory.CreateDirectory(folder);
            string data = Path.Combine(folder, "protected.zip"), par2 = data + ".par2", rebuilt = Path.Combine(folder, "rebuilt.zip");
            if (sizeBytes < 100_000) sizeBytes = 100_000;
            Synthetic(data, sizeBytes, false);
            string original = Sha(data);

            var stages = new System.Collections.Generic.List<string>();
            var sw = Stopwatch.StartNew();
            var c = Create(data, par2, redundancyPercent, 1, 0, (s, p) => { var k = s.ToString(); if (stages.Count == 0 || stages[stages.Count - 1] != k) stages.Add(k); });
            Check("create", c.Ok && File.Exists(par2) && c.RecoveryBlocks > 0, R(c) + " | " + sw.ElapsedMilliseconds + " ms | stages " + string.Join(">", stages));
            var files = Directory.GetFiles(folder, "protected.zip*.par2").Select(Path.GetFileName).OrderBy(n => n).ToArray();
            Check("create-files", files.Length == 2 && c.ParityBytes == files.Sum(f => new FileInfo(Path.Combine(folder, f)).Length), string.Join(", ", files));
            Check("create-refuses-existing", Create(data, par2, redundancyPercent).Status == Par2Status.BadArgument, "a second create into the same names");

            stages.Clear(); sw.Restart();
            var v = Verify(par2, data, 0, (s, p) => { var k = s.ToString(); if (stages.Count == 0 || stages[stages.Count - 1] != k) stages.Add(k); });
            Check("verify-intact", v.Status == Par2Status.Ok && v.DamagedBlocks == 0, R(v) + " | " + sw.ElapsedMilliseconds + " ms | stages " + string.Join(">", stages));
            Check("repair-not-needed", Repair(par2, data, rebuilt).Status == Par2Status.NotNeeded && !File.Exists(rebuilt), "intact data: nothing written");

            // damage three separate regions (fewer damaged blocks than recovery blocks)
            using (var f = new FileStream(data, FileMode.Open, FileAccess.ReadWrite))
                foreach (long at in new long[] { 1000, sizeBytes / 2, sizeBytes - 5000 }) { f.Position = at; f.Write(new byte[] { 0xde, 0xad, 0xbe, 0xef }, 0, 4); }
            string damaged = Sha(data); var damagedTime = File.GetLastWriteTimeUtc(data);
            var d = Verify(par2, data);
            Check("verify-detects-damage", d.Status == Par2Status.DamagedRepairable && d.DamagedBlocks >= 1 && d.DamagedBlocks <= 3, R(d));

            stages.Clear(); sw.Restart();
            var rep = Repair(par2, data, rebuilt, 0, (s, p) => { var k = s.ToString(); if (stages.Count == 0 || stages[stages.Count - 1] != k) stages.Add(k); });
            Check("repair-to-new-file", rep.Ok && File.Exists(rebuilt), R(rep) + " | " + sw.ElapsedMilliseconds + " ms | stages " + string.Join(">", stages));
            Check("repaired-hash-matches-original", File.Exists(rebuilt) && Sha(rebuilt) == original, "original " + original + ", rebuilt " + (File.Exists(rebuilt) ? Sha(rebuilt) : "missing"));
            Check("damaged-input-unchanged", Sha(data) == damaged && File.GetLastWriteTimeUtc(data) == damagedTime, "the damaged file was only read");
            Check("no-leftovers", Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).OrderBy(n => n).SequenceEqual(new[] { "protected.zip", "protected.zip.par2", files[1], "rebuilt.zip" }.OrderBy(n => n)),
                  string.Join(", ", Directory.GetFileSystemEntries(folder).Select(Path.GetFileName)));
            Check("repair-refuses-existing-output", Repair(par2, data, rebuilt).Status == Par2Status.BadArgument, "the output name already exists");

            // too much damage for the parity
            var heavy = Path.Combine(folder, "heavy.zip");
            Synthetic(heavy, sizeBytes, true);
            var hc = Create(heavy, heavy + ".par2", 5);
            var hv = Verify(heavy + ".par2", heavy);
            Check("verify-not-repairable", hc.Ok && hv.Status == Par2Status.Ok, "parity made after the change is intact: " + R(hv));
            using (var f = new FileStream(heavy, FileMode.Open, FileAccess.ReadWrite)) for (long at = 0; at < sizeBytes; at += 2048) { f.Position = at; f.WriteByte(0); }
            var hn = Verify(heavy + ".par2", heavy);
            Check("verify-reports-not-repairable", hn.Status == Par2Status.DamagedNotRepairable, R(hn));
            Check("repair-not-repairable-writes-nothing", Repair(heavy + ".par2", heavy, Path.Combine(folder, "heavy-rebuilt.zip")).Status == Par2Status.DamagedNotRepairable
                  && !File.Exists(Path.Combine(folder, "heavy-rebuilt.zip")) && !Directory.GetDirectories(folder).Any(), "no output, no working folder");

            // cancel: during create (the token fires at the first progress report) and before verify
            var cancelData = Path.Combine(folder, "cancel.zip"); Synthetic(cancelData, sizeBytes, false);
            using (var cts = new CancellationTokenSource())
            {
                var cc = Create(cancelData, cancelData + ".par2", 20, 1, 0, (s, p) => { if (s == Par2Stage.Computing) cts.Cancel(); }, cts.Token);
                Check("cancel-create", cc.Status == Par2Status.Canceled && !Directory.GetFiles(folder, "cancel.zip*.par2").Any(), R(cc));
            }
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Check("cancel-verify", Verify(par2, data, 0, null, cts.Token).Status == Par2Status.Canceled, "canceled before it started");
            }
            Check("wrong-name-refused", Verify(par2, cancelData).Status == Par2Status.BadArgument, "a file not named as recorded");
            Line("{\"selftest\":\"" + (failed == 0 ? "passed" : "failed") + "\",\"failed\":" + failed + ",\"peak_working_set\":" + Process.GetCurrentProcess().PeakWorkingSet64 + "}");
            return failed == 0 ? 0 : 1;
        }

        /// <summary>deterministic pseudo-random bytes (xorshift32), written in 1 MB pieces; altered: every 2,048th byte inverted</summary>
        static void Synthetic(string path, long size, bool altered)
        {
            uint x = 2463534242; var buf = new byte[1 << 20]; long at = 0;
            using (var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                while (at < size)
                {
                    int n = (int)Math.Min(buf.Length, size - at);
                    for (int i = 0; i < n; i++) { x ^= x << 13; x ^= x >> 17; x ^= x << 5; buf[i] = (byte)x; if (altered && (at + i) % 2048 == 0) buf[i] ^= 0xff; }
                    f.Write(buf, 0, n); at += n;
                }
        }

        static string Switch(string name) => AppContext.TryGetSwitch(name, out bool on) ? (on ? "true" : "false") : "null";

        static string Sha(string path) { using (var h = SHA256.Create()) using (var s = File.OpenRead(path)) return string.Concat(h.ComputeHash(s).Select(b => b.ToString("x2"))); }
        static string Esc(string s)
        {
            var b = new StringBuilder();
            foreach (var ch in s ?? "") { if (ch == '"' || ch == '\\') b.Append('\\').Append(ch); else if (ch < 0x20) b.Append("\\u").Append(((int)ch).ToString("x4")); else b.Append(ch); }
            return b.ToString();
        }
    }
}
