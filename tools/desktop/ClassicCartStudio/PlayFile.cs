// Classic Cart Studio for Windows — ClassicCartStudio.exe --play "C:\Games\My Game.nes": one game file in the game player, by itself.
// Copyright (C) 2026 Rance9. GPL-3.0-or-later.
//
//   ClassicCartStudio.exe --play FILE [--quiet]
//
// FILE is one game file (.nes with its header, or a file without one whose content a record knows); a relative path is read from the folder
// the command runs in; quote a path with spaces. The game opens in Classic Cart Studio's own game player window and nothing else: no
// Classic Cart Studio window (an open one is left as it is: not brought forward, not used), no HOME, nothing added to the Game Library or a
// collection. The file is read as it is at that moment, every time (a file rebuilt under the same name runs its new bytes) and is never
// written, moved or held open. The game's saves and its game and controller settings are the ones every way of playing that game uses (by
// its content), in the app's data folder (CCS_STUDIO_DATA when set). The command waits until the player window closes and then exits with:
//   0  the player closed normally (also at its start choice) 3  the file cannot be played (not there, not readable, not an NES game file)
//   1  the player ended with an error                        4  a game is already open in the player (it is left as it is)
//   2  the command was not used as above                     5  the player could not start (a missing or damaged installation)
// A failure is also shown in a message window, unless --quiet is given, and written to the command window when there is one. This program is
// a Windows program: in a command window, `start "" /wait ClassicCartStudio.exe --play FILE` waits for it and sets ERRORLEVEL; in PowerShell,
// `(Start-Process ClassicCartStudio.exe -ArgumentList '--play','"FILE"' -Wait -PassThru).ExitCode`.
// The player itself runs from the backend program (ClassicCartStudioBackend.exe --play-file, companion_app.py / play.py): the same game player,
// session and saves as Play in Classic Cart Studio; no network server is started for it and no local web address can ask for it.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;

namespace ClassicCartStudio
{
    internal static class PlayFile
    {
        public const int Ok = 0, Error = 1, Usage = 2, FileProblem = 3, Busy = 4, StartProblem = 5;

        /// <summary>the whole --play command: true when these arguments ask for it (then code is its exit code)</summary>
        public static bool Wanted(string[] args) => Array.IndexOf(args, "--play") >= 0;

        public static int Run(string[] args)
        {
            bool quiet = Array.IndexOf(args, "--quiet") >= 0;
            var rest = Array.FindAll(args, a => a != "--quiet");
            if (rest.Length != 2 || rest[0] != "--play" || string.IsNullOrWhiteSpace(rest[1]) || rest[1].StartsWith("--"))
                return Fail(Usage, "Use: ClassicCartStudio.exe --play \"C:\\Games\\Game.nes\" (one game file).", quiet);
            string file;
            try { file = Path.GetFullPath(rest[1].Trim()); }
            catch (Exception) { return Fail(Usage, "\"" + rest[1] + "\" is not a file path.", quiet); }
            if (!File.Exists(file)) return Fail(FileProblem, "There is no game file at " + file + ".", quiet);

            var psi = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = App.AppDir, RedirectStandardOutput = true, RedirectStandardError = true };
            var exe = Path.Combine(App.AppDir, Backend.CompanionExe);
            if (File.Exists(exe)) { psi.FileName = exe; psi.Arguments = "--play-file \"" + file + "\""; }
            else if (App.Dev && Environment.GetEnvironmentVariable("CCS_BACKEND_SOURCE") is string src && File.Exists(Path.Combine(src, "companion_app.py")))
            { psi.FileName = "python"; psi.Arguments = "-u \"" + Path.Combine(src, "companion_app.py") + "\" --play-file \"" + file + "\""; psi.WorkingDirectory = src; }
            else return Fail(StartProblem, "A program file of Classic Cart Studio is missing. Reinstall Classic Cart Studio; your collections are kept.", quiet);
            psi.Environment["CCS_HOST_PID"] = Process.GetCurrentProcess().Id.ToString();   // the player closes when this command is ended
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CCS_STUDIO_DATA"))) psi.Environment["CCS_STUDIO_DATA"] = App.DataRoot;   // as a full path
            psi.Environment.Remove("CCS_HOST_TOKEN");
            App.Log("play file: " + Path.GetFileName(file));
            string line = null;
            try
            {
                using (var p = Process.Start(psi))
                {
                    p.ErrorDataReceived += (s, e) => { }; p.BeginErrorReadLine();
                    string l; while ((l = p.StandardOutput.ReadLine()) != null) if (l.StartsWith("{")) line = l;
                    p.WaitForExit();
                    var m = line == null ? null : Regex.Match(line, "^\\{\"exit\": (\\d+), \"message\": \"((?:[^\"\\\\]|\\\\.)*)\"\\}$");
                    int code = m != null && m.Success ? int.Parse(m.Groups[1].Value) : (p.ExitCode >= 0 && p.ExitCode <= 5 ? p.ExitCode : StartProblem);
                    var msg = m != null && m.Success ? Unescape(m.Groups[2].Value) : (code == Ok ? "" : "The game player could not start.");
                    App.Log("play file: exit " + code);
                    return code == Ok ? Ok : Fail(code, msg, quiet);
                }
            }
            catch (Exception e) { return Fail(StartProblem, "The game player could not start (" + e.Message + ").", quiet); }
        }

        /// <summary>the command refused before it starts (exit code 2): nothing runs, and nothing is written (not even the log: the data folder
        /// is not known)</summary>
        public static int Refuse(string[] args, string message) => Fail(Usage, message, Array.IndexOf(args, "--quiet") >= 0, false);

        static string Unescape(string s) => Regex.Unescape(s);

        static int Fail(int code, string message, bool quiet, bool log = true)
        {
            if (log) App.Log("play file: " + message);
            if (AttachConsole(-1)) { try { using (var e = new StreamWriter(Console.OpenStandardError())) e.WriteLine("Classic Cart Studio: " + message); } catch (Exception) { } }
            if (!quiet) MessageBox.Show(message, Program.ProductName, MessageBoxButton.OK, code == Busy ? MessageBoxImage.Information : MessageBoxImage.Warning);
            return code;
        }

        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
    }
}
