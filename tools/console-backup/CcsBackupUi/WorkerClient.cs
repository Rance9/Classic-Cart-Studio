// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The window's side of the controlled worker (CcsBackup.Engine.WorkerHost): starts this same executable with --backup-worker, sends one
// operation and its follow-ups (cancel, confirm, finish, quit) as JSON lines, and hands every event to the window on its own thread. The
// worker is an ordinary child process: it is not killed when the window closes (the window asks it to stop at a safe boundary instead),
// and it exits when its operation has ended.
using CcsBackup;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Threading;

namespace CcsBackup.Ui
{
    public sealed class WorkerClient : IDisposable
    {
        readonly Process p; readonly StreamWriter input; readonly Dispatcher ui; readonly object sendLock = new object();
        public event Action<Dictionary<string, object>> Event;
        /// <summary>Console Browser's read answers, delivered on the reader thread (a waiting read never depends on the window's thread)</summary>
        public event Action<Dictionary<string, object>> RangeData;
        public event Action<int> Exited;
        /// <summary>each request as it is sent (on the thread that sends it): the window notes the person's quit, which ends the session</summary>
        public event Action<JObj> Sending;
        public bool Running => !p.HasExited;
        public int Pid => p.Id;

        public WorkerClient(string exe, IEnumerable<string> args, Dispatcher ui)
        {
            this.ui = ui;
            var psi = new ProcessStartInfo(exe, string.Join(" ", Quote(args))) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = new UTF8Encoding(false) };
            p = Process.Start(psi);
            input = CcsBackup.Engine.WorkerHost.RequestWriter(p.StandardInput.BaseStream);   // UTF-8, as the worker reads (any folder name)
            new Thread(Read) { IsBackground = true, Name = "worker events" }.Start();
            new Thread(() => { try { p.StandardError.ReadToEnd(); } catch { } }) { IsBackground = true }.Start();
        }

        // Windows command-line quoting: a quoted argument's trailing backslashes are doubled so they do not escape the closing quote
        static IEnumerable<string> Quote(IEnumerable<string> a)
        {
            foreach (var s in a)
            {
                if (s.Length > 0 && s.IndexOfAny(new[] { ' ', '\t', '"', '&' }) < 0) { yield return s; continue; }
                var t = s.Replace("\"", "\\\""); int n = t.Length - t.TrimEnd('\\').Length;
                yield return "\"" + t + new string('\\', n) + "\"";
            }
        }

        void Read()
        {
            try
            {
                string line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    Dictionary<string, object> d; try { d = Json.Parse(line); } catch { continue; }
                    if (d.S("event") == "range-data") { RangeData?.Invoke(d); continue; }
                    ui.BeginInvoke(new Action(() => Event?.Invoke(d)));
                }
            }
            catch { }
            try { p.WaitForExit(); } catch { }
            var code = p.HasExited ? p.ExitCode : -1;
            ui.BeginInvoke(new Action(() => Exited?.Invoke(code)));
        }

        public void Send(JObj req)
        {
            Sending?.Invoke(req);
            lock (sendLock) { try { if (!p.HasExited) input.WriteLine(CcsBackup.Engine.WorkerHost.RequestLine(req)); } catch { } }
        }
        public void Cancel() => Send(new JObj { { "cmd", "cancel" } });
        /// <summary>the window is going away: the worker sees its input end and stops at its next safe boundary</summary>
        public void Detach() { try { lock (sendLock) input.Close(); } catch { } }
        public void Dispose() { Detach(); }
    }
}
