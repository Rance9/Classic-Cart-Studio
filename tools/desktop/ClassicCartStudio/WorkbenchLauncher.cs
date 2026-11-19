// CCS Console Workbench, as Classic Cart Studio for Windows carries it: the same single-file program released on its own (byte for byte,
// one per Windows architecture; Setup installs the one for this PC), installed as workbench\CCS-Console-Workbench.exe beside this program.
// Studio starts it as a program of its own with a small launch context (tools/console-backup/ccs-backup-utility/InstalledLaunch.cs checks
// it): this program's own file, Studio's version and channel, the HOME package Studio prepared last (HOME Designer's preview) and, for tests,
// the Workbench settings file and a simulated console. The arguments are passed as a list (each quoted the way Windows splits a command
// line), never through a shell. Nothing here waits for the Workbench or ends it: it keeps running when Studio closes, owns its own close,
// its console tasks and its USB driver question, and comes forward when Studio opens it again (one installed window per user session).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ClassicCartStudio
{
    internal static class WorkbenchLauncher
    {
        public const string Folder = "workbench", Exe = "CCS-Console-Workbench.exe";
        /// <summary>the installed Workbench: workbench\CCS-Console-Workbench.exe in this program's folder</summary>
        public static string ExePath => Path.Combine(App.AppDir, Folder, Exe);

        /// <summary>the Workbench's command line for this start (installed mode)</summary>
        public static List<string> Arguments(string[] startupArgs)
        {
            var a = new List<string> { "--studio-launch", "--studio-exe", Process.GetCurrentProcess().MainModule.FileName, "--studio-version", App.Version, "--studio-channel", App.Channel,
                "--prepared-home", Path.GetFullPath(Path.Combine(App.DataRoot, "native-home", "current")) };   // HOME Designer's preview with the person's own games
            var sim = App.SimulatedConsoleArg(startupArgs); if (sim != null) { a.Add("--simulated-console"); a.Add(Path.GetFullPath(sim)); }
            var set = App.BackupSettingsArg(startupArgs); if (set != null) { a.Add("--settings"); a.Add(Path.GetFullPath(set)); }
            return a;
        }

        /// <summary>starts the installed Workbench (or brings its open window forward); null when it started, else words for the person</summary>
        public static string Open(string[] startupArgs)
        {
            var exe = ExePath;
            if (!File.Exists(exe)) return "CCS Console Workbench is missing from this installation. Run the Classic Cart Studio installer again to repair it; your collections, backups and settings are kept.";
            var psi = new ProcessStartInfo(exe, CommandLine(Arguments(startupArgs))) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) };
            AllowSetForegroundWindow(-1);   // ASFW_ANY: the Workbench (or its open window) may come to the front over Studio
            using (var p = Process.Start(psi)) { App.Log("console workbench: started " + exe + " (pid " + p?.Id + ")"); }   // not waited for, never ended by Studio
            return null;
        }

        /// <summary>one command line from a list of arguments, each quoted as CommandLineToArgvW reads it back</summary>
        public static string CommandLine(IEnumerable<string> args) => string.Join(" ", args.Select(Quote));

        static string Quote(string s)
        {
            var sb = new StringBuilder("\""); int bs = 0;
            foreach (var c in s ?? "")
            {
                if (c == '\\') { bs++; continue; }
                if (c == '"') { sb.Append('\\', bs * 2 + 1).Append('"'); bs = 0; continue; }
                sb.Append('\\', bs).Append(c); bs = 0;
            }
            return sb.Append('\\', bs * 2).Append('"').ToString();
        }

        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
    }
}
