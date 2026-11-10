// Classic Cart Studio console transport worker. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Derived from Hakchi2 CE 3.9.3 ISystemShell.cs (25bfcfd; GPL-3.0): the one shell call everything uses, with streams and a timeout.
// Modified by Rance9, 2026: only Execute/IsOnline/Disconnect/Kind remain; failures are typed (timeout, connection lost).
using System;
using System.IO;

namespace CcsWorker.Shell
{
    public interface IConsoleShell : IDisposable
    {
        string Kind { get; }
        bool IsOnline { get; }
        /// <summary>Runs one shell command; returns its exit code. timeoutMs: the longest silence allowed (0: none).</summary>
        int Execute(string command, Stream stdin, Stream stdout, Stream stderr, int timeoutMs);
        void Disconnect();
    }

    public class ShellException : Exception { public ShellException(string m) : base(m) { } public ShellException(string m, Exception inner) : base(m, inner) { } }
    public sealed class ShellTimeoutException : ShellException { public ShellTimeoutException(string m) : base(m) { } }
    public sealed class ShellLostException : ShellException { public ShellLostException(string m) : base(m) { } public ShellLostException(string m, Exception inner) : base(m, inner) { } }
}
