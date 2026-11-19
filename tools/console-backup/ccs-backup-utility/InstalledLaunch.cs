// CCS Console Workbench: installed mode. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Classic Cart Studio for Windows carries this same executable (byte for byte the one released on its own) in its installation folder,
// as workbench\CCS-Console-Workbench.exe beside ClassicCartStudio.exe, and starts it as a program of its own with a small launch context:
//   CCS-Console-Workbench.exe --studio-launch --studio-exe <that installation's ClassicCartStudio.exe> --studio-version N.N.N
//                             --studio-channel preview|production [--prepared-home FOLDER] [--settings FILE] [--simulated-console FOLDER]
// Every value is checked: Studio's program must be the ClassicCartStudio.exe of the installation that holds this executable, the version
// N.N.N, the channel one of the two words, every path complete; nothing else may be on the command line. A context that fails a check is
// refused with a short notice: it never falls back to the portable mode. A valid one opens the window in installed mode: preferences saved
// automatically to the Workbench settings file the window inside Classic Cart Studio always used (Settings.DefaultPath; --settings names
// another one for tests), HOME Designer's preview drawn from the HOME package Studio prepared last, and Studio's own identity for the one
// Classic Cart Studio update. The window, its workers and its close belong to this program: Studio closing or ending does not end them.
// One installed window per Windows user session: the first installed start holds the mutex InstalledMutex for its whole life and answers a
// named pipe that only this user can open (network clients are denied); a later installed start hands its context to it (that window comes
// forward and uses a changed prepared-HOME folder from then on) and exits. Nothing in the open window is closed, replaced or discarded.
// Portable starts are not affected: each is its own window. Every window and worker still shares the one console lock (ConsoleLock).
// Classic Cart Studio's setup and uninstall refuse to change the installation while InstalledMutex, InUseMutex or the console lock exists
// (tools/companion/installer.iss): InUseMutex is held by every window of the program installed with Studio, also one started from that file
// without Studio's context (a portable window), so Setup never replaces the file under an open window.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CcsBackup.Utility
{
    sealed class InstalledLaunch
    {
        public const string Flag = "--studio-launch";
        /// <summary>the installed window's workers carry this (with --backup-worker): their console lock writes its note of who holds it, as the
        /// window inside Classic Cart Studio always did</summary>
        public const string WorkerFlag = "--installed";
        public const string InstalledMutex = @"Local\CCS.ConsoleWorkbench.Installed";
        public const string InUseMutex = @"Local\CCS.ConsoleWorkbench.InstalledFile";
        public const string WorkbenchFolder = "workbench", StudioProgram = "ClassicCartStudio.exe";
        const string Schema = "ccs-workbench-handover/1";
        const int MaxMessage = 8192, MaxPath = 1024;
        static readonly Regex Ver = new Regex(@"^(0|[1-9]\d{0,3})\.(0|[1-9]\d{0,4})\.(0|[1-9]\d{0,5})$", RegexOptions.CultureInvariant);
        static readonly string[] Values = { "--studio-exe", "--studio-version", "--studio-channel", "--prepared-home", "--settings", "--simulated-console" };

        public string StudioExe, StudioVersion, StudioChannel, PreparedHome, SettingsFile, SimulatedConsole;
        Mutex mutex;

        /// <summary>a launch context this program will not use: Message says why, in words for the person</summary>
        public sealed class Refused : Exception { public Refused(string why) : base(why) { } }

        public const string RefusedTitle = "CCS Console Workbench could not open";
        public static string RefusedText(string why) => "Classic Cart Studio started CCS Console Workbench with information it cannot use: " + why
            + ". Nothing was changed. Close this, then open Workbench again from Classic Cart Studio. If it happens again, run the Classic Cart Studio installer again to repair it; your backups and settings are kept.";

        /// <summary>null for a start without --studio-launch (portable); the checked context otherwise. self: this program's own file.</summary>
        public static InstalledLaunch FromArgs(string[] args, string self)
        {
            if (args == null || Array.IndexOf(args, Flag) < 0) return null;
            var v = new Dictionary<string, string>(StringComparer.Ordinal); bool flag = false;
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a == Flag) { if (flag) throw new Refused("an option is given twice"); flag = true; continue; }
                if (Array.IndexOf(Values, a) < 0) throw new Refused("an option it does not know");
                if (v.ContainsKey(a)) throw new Refused("an option is given twice");
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) throw new Refused("an option without its value");
                v[a] = args[++i];
            }
            string Get(string k) => v.TryGetValue(k, out var x) ? x : null;
            var c = new InstalledLaunch { StudioVersion = Get("--studio-version"), StudioChannel = Get("--studio-channel") };
            if (Get("--studio-exe") == null || c.StudioVersion == null || c.StudioChannel == null) throw new Refused("a required part is missing");
            if (!Ver.IsMatch(c.StudioVersion)) throw new Refused("its version is not a version number");
            if (c.StudioChannel != "preview" && c.StudioChannel != "production") throw new Refused("its release channel is not one Workbench knows");
            // this program must be the Workbench of a Classic Cart Studio installation: <installation>\workbench\<this file>
            var me = Full(self) ?? throw new Refused("Workbench could not read where it is installed");
            var dir = Path.GetDirectoryName(me);
            if (!string.Equals(Path.GetFileName(dir), WorkbenchFolder, StringComparison.OrdinalIgnoreCase)) throw new Refused("this copy of Workbench is not part of a Classic Cart Studio installation");
            var studio = Path.Combine(Path.GetDirectoryName(dir), StudioProgram);
            c.StudioExe = Full(Get("--studio-exe"));
            if (c.StudioExe == null || !string.Equals(c.StudioExe, studio, StringComparison.OrdinalIgnoreCase) || !File.Exists(studio)) throw new Refused("the program that started it is not the Classic Cart Studio installed with this Workbench");
            c.StudioExe = studio;
            if (Get("--prepared-home") != null && (c.PreparedHome = Full(Get("--prepared-home"))) == null) throw new Refused("a folder is not a complete path");
            if (Get("--settings") != null && (c.SettingsFile = Full(Get("--settings"))) == null) throw new Refused("a file is not a complete path");
            if (Get("--simulated-console") != null && ((c.SimulatedConsole = Full(Get("--simulated-console"))) == null || !Directory.Exists(c.SimulatedConsole))) throw new Refused("the simulated console's folder is not there");
            return c;
        }

        /// <summary>a complete local or network path (C:\… or \\server\share\…), normalized; null for anything else</summary>
        static string Full(string p)
        {
            if (string.IsNullOrEmpty(p) || p.Length > MaxPath || p.Any(ch => ch < 32 || ch == '"' || ch == '<' || ch == '>' || ch == '|' || ch == '?' || ch == '*')) return null;
            bool drive = p.Length >= 3 && char.IsLetter(p[0]) && p[1] == ':' && (p[2] == '\\' || p[2] == '/');
            bool unc = p.StartsWith(@"\\", StringComparison.Ordinal) && !p.StartsWith(@"\\?\", StringComparison.Ordinal) && !p.StartsWith(@"\\.\", StringComparison.Ordinal);
            if (!drive && !unc) return null;
            try { var f = Path.GetFullPath(p); f = f.Length > 3 ? f.TrimEnd('\\', '/') : f; return Long(f); } catch (Exception) { return null; }
        }

        /// <summary>an existing file's or folder's long name (Program Files rather than PROGRA~1), so the same place compares equal
        /// however it was written; anything else as it is</summary>
        static string Long(string p)
        {
            var sb = new StringBuilder(1024); var n = GetLongPathNameW(p, sb, sb.Capacity);
            return n > 0 && n < sb.Capacity ? sb.ToString() : p;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern int GetLongPathNameW(string shortPath, StringBuilder longPath, int size);

        /// <summary>the options the installed window gives the Workbench window (BackupWorkspace): installed mode, never portable</summary>
        public void Apply(CcsBackup.Ui.WorkspaceOptions o)
        {
            o.HostedInFullApp = true; o.Portable = false;
            o.SettingsPath = SettingsFile;   // null: Settings.DefaultPath, the file the window inside Classic Cart Studio always used
            o.SimulatedConsole = SimulatedConsole; o.PreparedHomePackage = PreparedHome;
            o.Studio = new CcsBackup.Ui.StudioLaunch { Exe = StudioExe, Version = StudioVersion, Channel = StudioChannel };
            o.WorkerArgs = new List<string> { CcsBackup.Engine.WorkerHost.Flag, WorkerFlag };
        }

        // ------------------------------------------------------------------ one installed window per user session
        static string PipeName
        {
            get
            {
                uint session = 0; try { session = (uint)System.Diagnostics.Process.GetCurrentProcess().SessionId; } catch (Exception) { }
                return "CCS.ConsoleWorkbench.Installed." + WindowsIdentity.GetCurrent().User.Value + "." + session;
            }
        }

        /// <summary>true: this start is the installed window (it holds InstalledMutex until it exits). false: an installed window was already open;
        /// this context was handed to it (or, when it could not be reached, the person was told) and this start must end without a window.</summary>
        public bool TakeWindowOrHandOver(string self, Action<string, string> notice)
        {
            var until = DateTime.UtcNow.AddSeconds(15);
            while (true)
            {
                var m = new Mutex(true, InstalledMutex, out bool created);
                if (created) { mutex = m; return true; }
                m.Dispose();
                try { if (HandOver(self)) return false; } catch (Exception) { }
                if (DateTime.UtcNow > until) break;
                Thread.Sleep(300);   // the open window may be starting or just closing: try again
            }
            notice("CCS Console Workbench is already open", "CCS Console Workbench is already open on this PC, but it did not answer. Switch to its window with the taskbar. If it is busy with a console task, wait for the task to finish.");
            return false;
        }

        /// <summary>a handle to InUseMutex for a window of the program installed with Classic Cart Studio (<installation>\workbench\, beside
        /// ClassicCartStudio.exe), in any mode; null for any other copy. Keep it for as long as the window runs.</summary>
        public static Mutex MarkInUse(string self)
        {
            try
            {
                var dir = Path.GetDirectoryName(Full(self) ?? "");
                if (dir != null && string.Equals(Path.GetFileName(dir), WorkbenchFolder, StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(Path.GetDirectoryName(dir), StudioProgram)))
                    return new Mutex(false, InUseMutex);
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>keeps InstalledMutex for as long as this process runs</summary>
        public void Keep() => GC.KeepAlive(mutex);

        string Message() => Schema + "\n" + string.Join("\n", new[] { ("--studio-exe", StudioExe), ("--studio-version", StudioVersion), ("--studio-channel", StudioChannel),
            ("--prepared-home", PreparedHome), ("--settings", SettingsFile), ("--simulated-console", SimulatedConsole) }.Where(x => x.Item2 != null).Select(x => x.Item1 + "\t" + x.Item2));

        /// <summary>the context to the open installed window, which must be this same program; true when it took it</summary>
        bool HandOver(string self)
        {
            using (var p = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None))
            {
                p.Connect(1500);
                p.ReadMode = PipeTransmissionMode.Message;
                if (!GetNamedPipeServerProcessId(p.SafePipeHandle.DangerousGetHandle(), out uint pid)) return false;
                var other = ImagePath((int)pid); var mine = ImagePath(System.Diagnostics.Process.GetCurrentProcess().Id) ?? self;
                if (other == null || !string.Equals(other, mine, StringComparison.OrdinalIgnoreCase)) return false;   // only this installation's Workbench is told anything
                AllowSetForegroundWindow((int)pid);   // its window may come forward over this start's caller
                var b = Encoding.UTF8.GetBytes(Message());
                if (b.Length > MaxMessage) return false;
                p.Write(b, 0, b.Length); p.Flush();
                var r = new byte[16]; int n = 0;
                var t = p.ReadAsync(r, 0, r.Length);
                if (!t.Wait(5000)) return false;
                n = t.Result;
                return n == 2 && r[0] == (byte)'o' && r[1] == (byte)'k';
            }
        }

        /// <summary>the installed window's side: each later installed start's context, checked like a command line (same rules, this program's
        /// own place), arrives on the window's thread through arrived. A message that is too long, malformed or fails a check is ignored.</summary>
        public void Serve(string self, Action<InstalledLaunch> arrived)
        {
            var name = PipeName;
            var t = new Thread(() =>
            {
                int failures = 0;
                while (failures < 50)
                {
                    try
                    {
                        using (var s = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous, MaxMessage, 64, Security()))
                        {
                            s.WaitForConnection();
                            // one message, read within a few seconds (a client that sends nothing cannot hold the window's line open)
                            var buf = new byte[MaxMessage + 1]; int got = 0, n;
                            do { var rt = s.ReadAsync(buf, got, buf.Length - got); if (!rt.Wait(5000)) break; n = rt.Result; got += n; } while (n > 0 && !s.IsMessageComplete && got < buf.Length);
                            InstalledLaunch ctx = null;
                            if (got <= MaxMessage && s.IsMessageComplete) ctx = Parse(Encoding.UTF8.GetString(buf, 0, got), self);
                            if (ctx != null) { arrived(ctx); s.Write(new[] { (byte)'o', (byte)'k' }, 0, 2); s.Flush(); try { s.WaitForPipeDrain(); } catch (Exception) { } }
                        }
                        failures = 0;
                    }
                    catch (Exception) { failures++; Thread.Sleep(200); }
                }
            }) { IsBackground = true, Name = "installed window" };
            t.Start();
        }

        static InstalledLaunch Parse(string text, string self)
        {
            var lines = text.Split('\n');
            if (lines.Length < 2 || lines[0] != Schema) return null;
            var args = new List<string> { Flag };
            foreach (var l in lines.Skip(1))
            {
                var tab = l.IndexOf('\t'); if (tab <= 0) return null;
                args.Add(l.Substring(0, tab)); args.Add(l.Substring(tab + 1));
            }
            try { return FromArgs(args.ToArray(), self); } catch (Refused) { return null; }
        }

        /// <summary>this user only (read, write, and the server's own new instances); network clients denied</summary>
        static PipeSecurity Security()
        {
            var ps = new PipeSecurity(); var me = WindowsIdentity.GetCurrent().User;
            ps.SetAccessRuleProtection(true, false);
            ps.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
            ps.AddAccessRule(new PipeAccessRule(me, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance | PipeAccessRights.Synchronize, AccessControlType.Allow));
            ps.SetOwner(me);
            return ps;
        }

        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint pid);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder name, ref int size);

        /// <summary>a process's program file (null when it cannot be read: another user's process, or gone)</summary>
        static string ImagePath(int pid)
        {
            var h = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
            if (h == IntPtr.Zero) return null;
            try { var sb = new StringBuilder(1024); int size = sb.Capacity; return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString(0, size) : null; }
            finally { CloseHandle(h); }
        }
    }
}
