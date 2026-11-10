// Classic Cart Studio console transport worker. GPL-3.0-or-later.
// Derived from Hakchi2 CE 3.9.3 SshClient/SshClientWrapper.cs (commit 25bfcfd; GPL-3.0; the Hakchi2 CE / Team Shinkansen authors):
// one SSH command per call with stdout/stderr streams, the input sent on the same channel, a command timeout, the exit status.
// Modified by Rance9, 2026: the SSH session is behind ISshSession (the real one, SshNetSession, is opened only by RealTransports,
// Gate G1); no mDNS/DNS listeners and no background reconnect; the input is ended (EOF) so commands that read to the end finish;
// failures are typed (timeout, connection lost).
using System;
using System.IO;

namespace CcsWorker.Shell
{
    public interface ISshSession : IDisposable
    {
        bool IsConnected { get; }
        /// <summary>Runs one command; stdin (when given) is sent and then ended. Throws ShellTimeoutException or ShellLostException.</summary>
        int Run(string command, Stream stdin, Stream stdout, Stream stderr, int timeoutMs);
        void Disconnect();
    }

    public sealed class SshShell : IConsoleShell
    {
        readonly ISshSession session;
        volatile bool lost;
        public SshShell(ISshSession session) { this.session = session ?? throw new ArgumentNullException(nameof(session)); }
        public string Kind => "ssh";
        public bool IsOnline => !lost && session.IsConnected;

        public int Execute(string command, Stream stdin, Stream stdout, Stream stderr, int timeoutMs)
        {
            if (!IsOnline) throw new ShellLostException("the SSH connection is closed");
            try { return session.Run(command, stdin, stdout, stderr, timeoutMs); }
            catch (ShellTimeoutException) { throw; }
            catch (ShellLostException) { lost = true; throw; }
            catch (Exception ex) when (!session.IsConnected) { lost = true; throw new ShellLostException("the SSH connection ended (" + ex.Message + ")", ex); }
        }

        public void Disconnect() { lost = true; try { session.Disconnect(); } catch { } }
        public void Dispose() { Disconnect(); session.Dispose(); }
    }
}
