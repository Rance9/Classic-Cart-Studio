// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// One console operation at a time on this computer, whichever program runs it (Classic Cart Studio's backup worker, its console worker
// for the HOME, the Utility): a named mutex every one of them takes for the whole operation. Windows releases it when the holder ends,
// even by a crash (the next holder sees it abandoned and is told). A small file beside the settings names the holder for the message.
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace CcsBackup.Engine
{
    public sealed class ConsoleLock : IDisposable
    {
        public const string MutexName = @"Local\CCS.ConsoleOperation";
        readonly Mutex m; bool held;
        public bool PreviousHolderEnded { get; private set; }
        public static string InfoPath => Path.Combine(Path.GetDirectoryName(Library.Settings.DefaultPath), "console-operation.json");
        /// <summary>false in the standalone Workbench: the lock is the named mutex alone, with no information file in AppData (another program
        /// that is busy is then named "another program")</summary>
        public static bool WriteInfo = true;

        ConsoleLock(Mutex m) { this.m = m; }

        /// <summary>null when another program holds it (busy names it, when known)</summary>
        public static ConsoleLock TryAcquire(string owner, string operation, out string busy)
        {
            busy = null; var mx = new Mutex(false, MutexName); var l = new ConsoleLock(mx);
            try { l.held = mx.WaitOne(0); }
            catch (AbandonedMutexException) { l.held = true; l.PreviousHolderEnded = true; }
            if (!l.held)
            {
                mx.Dispose();
                try { var d = Json.Parse(File.ReadAllText(InfoPath)); busy = d.S("owner") + " (" + d.S("operation") + ", started " + d.S("started_utc") + ")"; } catch { busy = "another program"; }
                return null;
            }
            if (WriteInfo) try { Library.Durable.WriteAtomic(InfoPath, Json.Write(new JObj { { "owner", owner }, { "operation", operation }, { "pid", Process.GetCurrentProcess().Id }, { "started_utc", Library.Record.Now() } })); } catch { }
            return l;
        }

        public static bool IsBusy() { using (var mx = new Mutex(false, MutexName)) { try { if (mx.WaitOne(0)) { mx.ReleaseMutex(); return false; } return true; } catch (AbandonedMutexException) { mx.ReleaseMutex(); return false; } } }

        public void Dispose()
        {
            if (!held) return; held = false;
            if (WriteInfo) try { File.Delete(InfoPath); } catch { }
            try { m.ReleaseMutex(); } catch { }
            m.Dispose();
        }
    }
}
