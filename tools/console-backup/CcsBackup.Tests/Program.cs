// Classic Cart Studio console backup and restore engine tests. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
//   CcsBackup.Tests.exe --fixtures DIR --work DIR [--kernel FILE] [--python EXE] [--utility EXE] [--only a,b] [--real-driver-store]
//                       [--owner-system DIR --owner-decrypted FILE]   (the private browser-owner-system group: TestsBrowserSystem.cs)
// DIR: tools/console-backup/tests/make_fixtures.py's output. FILE: a genuine stock NES Classic kernel (MD5 on Hakchi2 CE's list) for the
// stock-console cases; without it those cases are SKIPPED (a skip is never a pass). Every console here is a SimulatedConsole folder on
// the PC; nothing opens a USB device or a network connection. Prints one line per check and "RESULT passed/total"; exit 0 only when
// every check ran and passed (2 when some were skipped). --real-driver-store lets the "driver-real" group read this PC's USB driver state
// (read only; it prints counts); without it that group is SKIPPED.
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
using System.Security.Cryptography;
using System.Text;

namespace CcsBackup.Tests
{
    sealed class SkipException : Exception { public SkipException(string m) : base(m) { } }

    static partial class Program
    {
        static int pass, fail, skip; static readonly List<string> failures = new List<string>();
        internal static string Fx, Work, KernelPath, SnesKernelPath, Python, Utility; internal static bool RealDriverStore;
        internal static void Check(bool ok, string what) { if (ok) pass++; else { fail++; failures.Add(what); } Console.WriteLine((ok ? "ok   " : "FAIL ") + what); }
        static string Arg(string[] a, string k) { int i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }

        static int Main(string[] args)
        {
            // inside Windows Sandbox only (each refuses anywhere else): this program as the whole-drive administrator step, and the whole run
            if (args.Contains(CcsBackup.External.RawDiskHost.Flag)) return Tests.SandboxStep(args);
            if (args.Contains("--raw-disk-sandbox")) return Tests.SandboxRun(args);
            Fx = Arg(args, "--fixtures"); Work = Arg(args, "--work"); KernelPath = Arg(args, "--kernel"); SnesKernelPath = Arg(args, "--snes-kernel"); Python = Arg(args, "--python"); Utility = Arg(args, "--utility"); RealDriverStore = args.Contains("--real-driver-store");
            Tests.OwnerSystemDir = Arg(args, "--owner-system"); Tests.OwnerDecrypted = Arg(args, "--owner-decrypted");
            var only = (Arg(args, "--only") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (Fx == null || Work == null || !Directory.Exists(Fx)) { Console.Error.WriteLine("usage: CcsBackup.Tests --fixtures DIR --work DIR [--kernel FILE] [--python EXE] [--utility EXE] [--only ...]"); return 2; }
            Directory.CreateDirectory(Work);
            var groups = new List<(string name, Action run)>
            {
                ("gate", Tests.Gate), ("zip", Tests.Zip), ("ext4", Tests.Ext4), ("capture", Tests.Capture), ("memory", Tests.Memory), ("faults", Tests.Faults),
                ("verify", Tests.VerifyAndHistory), ("admission", Tests.AdmissionCases), ("restore", Tests.Restore), ("prepare", Tests.Prepare), ("return", Tests.Return), ("method", Tests.Method), ("method-already", Tests.MethodAlready), ("snes", Tests.Snes), ("snes-system", Tests.SnesSystem), ("snes-data", Tests.SnesData), ("sega", Tests.Sega), ("home", Tests.Home), ("home-chain", Tests.HomeChain), ("firmware", Tests.Firmware), ("system-image", Tests.SystemImages), ("rescue", Tests.Rescue), ("files", Tests.Files), ("console-browser", Tests.ConsoleBrowserCases), ("schedule", Tests.Schedule), ("composer", Tests.ComposerRuns), ("package", Tests.PackageRuns), ("partial", Tests.PartialRestore), ("developer", Tests.Developer), ("largefiles", Tests.LargeFiles), ("export", Tests.Export), ("browser", Tests.Browser), ("compose", Tests.Compose), ("lifecycle", Tests.Lifecycle),
                ("saves", () => { Tests.SavesFormat(); Tests.SavesImport(); }), ("flash-save", Tests.FlashSave), ("data-only", Tests.DataOnly), ("community", Tests.Community), ("platform", Tests.Platform),
                ("driver", Tests.Driver), ("driver-real", Tests.DriverReal), ("driver-package", Tests.DriverPackageFiles), ("driver-adapter", Tests.DriverAdapter), ("driver-gate", Tests.DriverGate),
                ("repair", Tests.Repair), ("browser-salvage", Tests.BrowserSalvage), ("browser-nested", Tests.BrowserNested), ("browser-system", Tests.BrowserSystem), ("browser-owner-system", Tests.BrowserOwnerSystem), ("supply", Tests.Supply),
                ("parity", Tests.ParityRuns),
                ("designer", Tests.Designer),
                ("updates", Tests.Updates),
                ("storage-format", Tests.StorageFormat), ("storage-fat", Tests.StorageFat), ("storage-files", Tests.StorageFiles), ("storage-image", Tests.StorageImage),
                ("storage-repair", Tests.StorageRepairs), ("storage-restore", Tests.StorageRestores), ("storage-protect", Tests.StorageProtect), ("storage-large", Tests.StorageLarge),
                ("storage-spec", Tests.StorageSpecGroup), ("storage-report", Tests.StorageReportGroup), ("storage-raw", Tests.StorageRaw),
                ("identity-names", Tests.IdentityNames),
            };
            foreach (var g in groups)
            {
                if (only.Length > 0 && !only.Contains(g.name)) continue;
                if (g.name == "driver-real" && !RealDriverStore && !only.Contains(g.name)) continue;   // never part of a default run: it reads this PC
                if (g.name == "home-chain" && !only.Contains(g.name)) continue;
                if (g.name == "storage-large" && !only.Contains(g.name)) continue;   // only when asked: it writes a file over 4 GB (twice) and takes minutes   // only when asked: it hands the engine's archive to the qemu chain tests
                Console.WriteLine("== " + g.name);
                var sw = Stopwatch.StartNew();
                try { g.run(); }
                catch (SkipException ex) { skip++; Console.WriteLine("SKIP " + g.name + ": " + ex.Message); }
                catch (Exception ex) { fail++; failures.Add(g.name + " threw " + ex.GetType().Name); Console.WriteLine("FAIL " + g.name + " threw " + ex); }
                Console.WriteLine("   (" + g.name + " " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s)");
            }
            Console.WriteLine();
            foreach (var f in failures) Console.WriteLine("FAILED: " + f);
            Console.WriteLine("RESULT " + pass + "/" + (pass + fail) + (skip > 0 ? " (" + skip + " group(s) skipped)" : ""));
            return fail > 0 ? 1 : skip > 0 ? 2 : 0;
        }

        // ------------------------------------------------------------------ helpers shared by the tests
        /// <summary>a digest that is certainly not this one (its first hex digit changed; never the same digit)</summary>
        internal static string Other(string digest) => (digest[0] == '0' ? "1" : "0") + digest.Substring(1);
        internal static string NewDir(string name) { var d = Path.Combine(Work, name + "-" + Guid.NewGuid().ToString("N").Substring(0, 6)); Directory.CreateDirectory(d); return d; }
        internal static byte[] StockKernel()
        {
            if (KernelPath == null || !File.Exists(KernelPath)) throw new SkipException("no genuine stock kernel file was given (--kernel): the stock-console cases need one");
            var b = File.ReadAllBytes(KernelPath);
            using (var h = MD5.Create()) if (!KnownKernels.IsStock(Hex.Of(h.ComputeHash(b)))) throw new SkipException("the --kernel file is not a genuine stock NES Classic kernel");
            return b;
        }
        /// <summary>a genuine stock Super NES Classic kernel (--snes-kernel, read locally, never copied anywhere), for the SNES stock cases</summary>
        internal static byte[] SnesStockKernel()
        {
            if (SnesKernelPath == null || !File.Exists(SnesKernelPath)) throw new SkipException("no genuine stock Super NES Classic kernel file was given (--snes-kernel): the SNES stock cases need one");
            var b = File.ReadAllBytes(SnesKernelPath);
            using (var h = MD5.Create()) if (!KnownKernels.IsStockFor(ConsoleModels.Snes, "USA", Hex.Of(h.ComputeHash(b))) && !KnownKernels.IsStockFor(ConsoleModels.Snes, "EUR", Hex.Of(h.ComputeHash(b)))) throw new SkipException("the --snes-kernel file is not a genuine stock Super NES Classic kernel");
            return b;
        }
        /// <summary>a Super NES Classic's storage as a North American console reported it (2026-11-21: nanda 3,145,728 bytes, nandb 67,108,864,
        /// nandc 314,572,800, nandd 1,048,576, nande 65,536,000), its data part sized to the 64 MB data fixture; the labels are the vocabulary the
        /// pinned flash tool uses (a console's own table is what an identity check reads)</summary>
        internal static readonly (string dev, string label, long bytes)[] SnesLayout =
            { ("nanda", "boot", 3072L * 1024), ("nandb", "rootfs", 65536L * 1024), ("nandc", "data", 65536L * 1024), ("nandd", "private", 1024L * 1024), ("nande", "UDISK", 64000L * 1024) };
        /// <summary>a simulated Super NES Classic (board dp-shvc): kinds stock | modified (original kernel in slot 68) | modified-noorig</summary>
        internal static string SnesConsole(string kind, string region = "USA", string nandinfo = "2048 64 4096", string hwid = HwidA, (string, string, long)[] layout = null)
        {
            var dir = NewDir("snes-" + kind + "-" + region);
            var mod = File.ReadAllBytes(Path.Combine(Fx, "kernel-modified.img"));
            var files = new Dictionary<string, Action<Stream>>(); byte[] active; int entries;
            switch (kind)
            {
                case "stock": active = SnesStockKernel(); entries = 0; files["phy"] = Phy((0x30, active)); break;
                case "modified": { var orig = SnesStockKernel(); active = mod; entries = 3; files["boot2-68"] = FromBytes(orig); files["phy"] = Phy((0x30, active), (0x68, orig)); break; }
                case "modified-noorig": active = mod; entries = 3; files["phy"] = Phy((0x30, active)); break;
                default: throw new ArgumentException(kind);
            }
            files["nandc"] = FromFile(Path.Combine(Fx, "data-modern.img"));
            files["boot2"] = FromBytes(active);
            files["boot0"] = st => { var b = BootImage.Synthetic(true, 24576, "boot0"); st.Write(b, 0, b.Length); };
            files["boot1"] = st => { var b = BootImage.Synthetic(false, 757760, "uboot", 4096); st.Write(b, 0, b.Length); };
            SimulatedConsole.Create(Path.Combine(dir, "c"), hwid, (layout ?? SnesLayout).ToList(), files, entries, region, "release-v2.0.14-0-gd8b65c6", "dp-shvc", nandinfo);
            return Path.Combine(dir, "c");
        }
        internal static readonly (string dev, string label, long bytes)[] Layout =
            { ("nanda", "boot", 3072L * 1024), ("nandb", "rootfs", 19456L * 1024), ("nandc", "data", 403456L * 1024), ("nandd", "private", 1024L * 1024), ("nande", "UDISK", 13312L * 1024) };
        internal const string HwidA = "0461aaaa 88180000 1e670000 6c118000", HwidB = "0461bbbb 88180000 1e670000 6c118000";

        static Action<Stream> FromFile(string p) => s => { using (var f = File.OpenRead(p)) f.CopyTo(s); };
        static Action<Stream> FromBytes(byte[] b) => s => s.Write(b, 0, b.Length);
        static Action<Stream> Phy(params (long block, byte[] img)[] put) => s =>
        {
            SimulatedConsole.Pattern(s, Support.PhysicalBytes, "phy");
            foreach (var p in put) { s.Position = p.block * 0x20000; s.Write(p.img, 0, p.img.Length); }
            s.Position = s.Length;
        };

        /// <summary>kinds: stock | modified (original kernel in slot 68) | modified-noorig | stock-small</summary>
        internal static string Console_(string kind, string hwid = HwidA, string dataImage = null, (string, string, long)[] layout = null, int hakchiEntries = -1)
        {
            var dir = NewDir("console-" + kind);
            var mod = File.ReadAllBytes(Path.Combine(Fx, "kernel-modified.img"));
            var files = new Dictionary<string, Action<Stream>>();
            byte[] active; int entries;
            switch (kind)
            {
                case "stock": active = StockKernel(); entries = 0; files["nandc"] = FromFile(dataImage ?? Path.Combine(Fx, "data-stock.img")); files["phy"] = Phy((0x30, active)); break;
                case "modified": { var orig = StockKernel(); active = mod; entries = 3; files["boot2-68"] = FromBytes(orig); files["nandc"] = FromFile(dataImage ?? Path.Combine(Fx, "data-modified.img")); files["phy"] = Phy((0x30, active), (0x68, orig)); break; }
                case "modified-noorig": active = mod; entries = 3; files["nandc"] = FromFile(dataImage ?? Path.Combine(Fx, "data-modified.img")); files["phy"] = Phy((0x30, active)); break;
                default: throw new ArgumentException(kind);
            }
            files["boot2"] = FromBytes(active);
            files["boot0"] = s => { var b = BootImage.Synthetic(true, 24576, "boot0"); s.Write(b, 0, b.Length); };   // valid images, as the console's are
            files["boot1"] = s => { var b = BootImage.Synthetic(false, 757760, "uboot", 4096); s.Write(b, 0, b.Length); };
            var lay = (layout ?? Layout).ToList();
            var data = files.ContainsKey("nandc") ? files["nandc"] : null;
            var dl = lay.FirstOrDefault(x => x.Item2 == "data");
            if (data != null && dl.Item1 != "nandc") { files.Remove("nandc"); files[dl.Item1] = data; }
            SimulatedConsole.Create(Path.Combine(dir, "c"), hwid, lay, files, hakchiEntries >= 0 ? hakchiEntries : entries);
            return Path.Combine(dir, "c");
        }

        internal static Settings SettingsIn(string dir, bool second = true)
        {
            var s = new Settings { Path = Path.Combine(dir, "settings.json"), PrimaryFolder = Path.Combine(dir, "backups"), ArchiveFolder = second ? Path.Combine(dir, "backups-second") : null };
            s.Save(); return s;
        }

        internal static EngineContext Ctx(Settings s, ImageKind k = ImageKind.Backup)
        {
            var (id, sha) = RecoveryImages.IdOf(k);
            return new EngineContext { Settings = s, Entry = "tests", ToolName = "CcsBackup.Tests", RecoveryImageId = id, RecoveryImageSha256 = sha };
        }

        internal static BackupOutcome Backup(string console, Settings s, Action<EngineContext> tune = null)
        {
            var con = RecoverySessions.OpenSimulated(console, ImageKind.Backup);
            var ctx = Ctx(s); tune?.Invoke(ctx);
            return CaptureEngine.Run(con, ctx);
        }

        internal static string Sha(string file) { using (var h = SHA256.Create()) using (var f = File.OpenRead(file)) return Hex.Of(h.ComputeHash(f)); }
        internal static string Sha(byte[] b) { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(b)); }
        internal static string Md5(byte[] b) { using (var h = MD5.Create()) return Hex.Of(h.ComputeHash(b)); }

        internal static (int code, string output) Run(string exe, string args, string stdin = null, int timeoutMs = 600000)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = stdin != null, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            {
                if (stdin != null) { p.StandardInput.Write(stdin); p.StandardInput.Close(); }
                var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return (-1, "timeout"); }
                return (p.ExitCode, o.Result + e.Result);
            }
        }

        /// <summary>Python's own zipfile/ccs_package reading a file (an independent reader); null when no Python was given</summary>
        internal static (int code, string output)? Py(string script)
        {
            if (Python == null || !File.Exists(Python)) return null;
            var f = Path.Combine(Work, "py-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".py"); File.WriteAllText(f, script);
            return Run(Python, "\"" + f + "\"");
        }
    }
}
