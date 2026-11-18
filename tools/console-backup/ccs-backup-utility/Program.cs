// CCS Console Workbench (standalone). Copyright (C) 2026 Rance9. GPL-3.0-or-later. (Earlier versions: CCS Console Backup & Restore Utility.)
//   CCS-Console-Workbench.exe [--settings FILE]                the window
//   ... --backup-worker [--settings FILE]                      the controlled worker the window starts for one console operation
//   ... --driver-action REQUEST                                the USB driver step, started by the window through Windows with administrator
//                                                              permission (closed in this version: it exits at once and changes nothing)
//   ... --simulated-console FOLDER [--settings FILE]           the window with a simulated console (tests and demonstrations only)
//   ... --scheduled-check --settings FILE --report FOLDER     the optional scheduled backup check (offline; a window only when it finds a problem)
//   ... --version
//   ... --engine-info                                          the shared libraries inside it and its recovery images (SHA-256; reads only)
//   ... --design-compile --design F --games F --out DIR --result F   HOME Designer's one compiler (CcsBackup.Compose.DesignCompileCli)
//   ... --home-render-selftest PACK --work FOLDER [--renderer EXE]   the HOME render check of a HOME package's theme defaults (one line per
//                                                              screen with its frame hash); no design, nothing written outside FOLDER
//   ... --parity-selftest FOLDER [--parity-selftest-bytes N]  a PAR2 round trip through the native host's parity functions on a synthetic
//                                                              file (8 MB unless N), written only inside FOLDER (new or empty); JSON lines
// No installer, service, startup entry, file association, shortcut or updater, and no AppData: ordinary preferences stay in memory unless
// the person saves them to a settings file of their own (then --settings FILE); backups, their records and every operation journal live
// in the backup folders the person chooses. Windows may keep its own records of programs it runs.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;

namespace CcsBackup.Utility
{
    static class Program
    {
        public const string Name = "CCS Console Workbench";

        [STAThread]
        static int Main(string[] args)
        {
            HostedTargetFramework();                                        // before anything reads the target framework
            AppDomain.CurrentDomain.AssemblyResolve += Embedded.Resolve;   // before anything touches the embedded assemblies
            return Run(args);
        }

        /// <summary>Started by the Workbench's native host (tools/console-backup/native-host), this program is loaded from memory and has no
        /// entry assembly, so .NET Framework cannot read its TargetFrameworkAttribute and would give the process .NET Framework 4.0's
        /// compatibility defaults (legacy path handling with the 260-character limit, WPF without per-monitor DPI scaling, ...). This gives
        /// the default domain this program's own target framework, as a normal start does, through the runtime's private
        /// AppDomain.SetTargetFrameworkName (the method the runtime itself uses at start-up). Does nothing when started normally.</summary>
        static void HostedTargetFramework()
        {
            try
            {
                if (Assembly.GetEntryAssembly() != null || AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName != null) return;
                var tf = (System.Runtime.Versioning.TargetFrameworkAttribute)Attribute.GetCustomAttribute(typeof(Program).Assembly, typeof(System.Runtime.Versioning.TargetFrameworkAttribute));
                var set = typeof(AppDomain).GetMethod("SetTargetFrameworkName", BindingFlags.Instance | BindingFlags.NonPublic);
                if (tf != null && set != null) set.Invoke(AppDomain.CurrentDomain, new object[] { tf.FrameworkName });
            }
            catch (Exception) { }   // never stop the program over this; the self-test reports the target framework in effect
        }

        static string Arg(string[] a, string k) { int i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
        static string Version => typeof(Program).Assembly.GetName().Version.ToString(3);

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static int Run(string[] args)
        {
            // the standalone writes nothing to AppData in any mode: its console lock is the named mutex alone (no note of who holds it),
            // also when its worker is started directly without --portable
            CcsBackup.Engine.ConsoleLock.WriteInfo = false;
            // the elevated USB driver step (this same file started again by Windows with administrator permission): before anything else, no
            // window; closed in this version by the DriverChanges boundary (CcsBackup/Driver/DriverActionHost.cs)
            if (args.Contains(CcsBackup.Driver.DriverActionHost.Flag)) return CcsBackup.Driver.DriverActionHost.Run(args);
            if (args.Contains("--version")) { Console.WriteLine(Version); return 0; }
            if (args.Contains("--engine-info")) { Console.Write(CcsBackup.Engine.ComponentReport.Describe(Name, Version, CcsBackup.Engine.ComponentReport.Embedded(typeof(Program).Assembly))); return 0; }
            if (args.Contains(CcsBackup.Engine.WorkerHost.Flag)) return CcsBackup.Engine.WorkerHost.Run(args, "workbench", Name, Version);
            if (args.Contains(CcsBackup.Compose.DesignCompileCli.Flag)) return CcsBackup.Compose.DesignCompileCli.Run(args);   // HOME Designer's compiler, as the app's package builder calls it
            if (args.Contains("--home-render-selftest"))
            {   // the HOME Designer's render check through this process's own renderer (in the single-file host: the one linked into it), theme
                // defaults only: one line per screen with its frame's SHA-256, to compare with ccs-sim-core.exe; reads PACK, writes only WORK
                var pack = Arg(args, "--home-render-selftest"); var work = Arg(args, "--work"); var exe = Arg(args, "--renderer");
                if (pack == null || work == null) return 2;
                var r = CcsBackup.Compose.HomeRenderers.Find(exe).Check(pack, null, new CcsBackup.Compose.RenderCheckOptions { WorkDir = work, Frames = false });
                Console.Out.WriteLine("{\"renderer\":\"" + r.Renderer + "\",\"ok\":" + (r.Ok ? "true" : "false") + ",\"states\":" + r.States.Count + ",\"report_sha256\":\"" + r.ReportSha256 + "\"}");
                foreach (var st in r.States) Console.Out.WriteLine(st.Theme + " " + st.State + " " + st.DefaultSha256);
                return r.Ok ? 0 : 1;
            }
            if (args.Contains("--parity-selftest")) return CcsBackup.Repair.Par2Native.SelfTest(Arg(args, "--parity-selftest"), Console.Out, long.TryParse(Arg(args, "--parity-selftest-bytes"), out var n) && n > 0 ? n : 8_000_000);
            if (args.Contains(CcsBackup.Library.ScheduledCheck.Flag))
            {   // the person's scheduled check: their settings file, their report folder; nothing else is read or written
                var sf = Arg(args, "--settings"); var rf = Arg(args, "--report");
                if (sf == null || rf == null || !File.Exists(sf)) return 2;
                var problems = CcsBackup.Library.ScheduledCheck.RunCheck(CcsBackup.Library.Settings.Load(sf), rf);
                if (problems.Count > 0) MessageBox.Show("A scheduled check of your NES Classic console backups found a problem:\n\n" + string.Join("\n", problems.Take(8)) + (problems.Count > 8 ? "\n…" : "") + "\n\nThe report is in " + rf + ". Open CCS Console Workbench (Backup Repair) to check and repair.", Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return problems.Count > 0 ? 1 : 0;
            }
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            string notice = null;
            using (var s = typeof(CcsBackup.Ui.BackupWorkspace).Assembly.GetManifestResourceStream("NOTICE")) if (s != null) using (var r = new StreamReader(s)) notice = r.ReadToEnd();
            var opt = new CcsBackup.Ui.WorkspaceOptions
            {
                HostedInFullApp = false, Portable = true, ProductName = Name, Version = Version, WorkerExe = Process.GetCurrentProcess().MainModule.FileName,
                SettingsPath = Arg(args, "--settings"), SimulatedConsole = Arg(args, "--simulated-console"), NoticeText = notice,
                Wordmark = Png("pack://application:,,,/CcsBackupUi;component/brand-wordmark.png"), AppIcon = Png("pack://application:,,,/CcsBackupUi;component/app.ico"),
            };
            var w = new CcsBackup.Ui.BackupWorkspace(opt);
            return app.Run(w);
        }

        static System.Windows.Media.ImageSource Png(string uri) { try { var b = new BitmapImage(); b.BeginInit(); b.UriSource = new Uri(uri); b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit(); b.Freeze(); return b; } catch { return null; } }
    }

    /// <summary>The managed DLLs this executable carries as resources ("asm/&lt;name&gt;.dll"), loaded from memory when first needed:
    /// nothing is extracted to disk. Only names it carries are answered.</summary>
    static class Embedded
    {
        public static Assembly Resolve(object sender, ResolveEventArgs e)
        {
            var name = new AssemblyName(e.Name).Name;
            var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
            if (loaded != null) return loaded;
            using (var s = typeof(Embedded).Assembly.GetManifestResourceStream("asm/" + name + ".dll"))
            {
                if (s == null) return null;
                var b = new byte[s.Length]; int got = 0; while (got < b.Length) { int n = s.Read(b, got, b.Length - got); if (n <= 0) break; got += n; }
                return Assembly.Load(b);
            }
        }
    }
}
