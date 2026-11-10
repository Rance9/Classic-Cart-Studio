// Classic Cart Studio console transport worker. GPL-3.0-or-later.
// Derived from Hakchi2 CE 3.9.3 SshClient/SshClientWrapper.cs (commit 25bfcfd; GPL-3.0): Connect (SshClient, root, empty password,
// port 22) and Execute (CreateCommand, CommandTimeout, BeginExecute with the output streams, SendData, EndExecute, ExitStatus).
// Modified by Rance9, 2026: constructed only by RealTransports.Open (Gate G1 closed in code: nothing here runs in this version);
// the input is sent in chunks and then ended with an EOF on the command's channel; SSH.NET exceptions become typed shell failures.
using Renci.SshNet;
using Renci.SshNet.Common;
using System;
using System.IO;
using System.Net.Sockets;
using System.Reflection;

namespace CcsWorker.Shell
{
    internal sealed class SshNetSession : ISshSession
    {
        readonly SshClient client;

        internal SshNetSession(string host, int port)
        {
            client = new SshClient(host, port, "root", "");
            client.Connect();
        }

        public bool IsConnected => client.IsConnected;

        public int Run(string command, Stream stdin, Stream stdout, Stream stderr, int timeoutMs)
        {
            try
            {
                using (var cmd = client.CreateCommand(command))
                {
                    // SSH.NET's timeout covers the whole command (clovershell's is silence): its input is allowed 100 KB/s on top
                    long size = 0; if (stdin != null) try { size = stdin.Length; } catch (NotSupportedException) { }
                    if (timeoutMs > 0) cmd.CommandTimeout = TimeSpan.FromMilliseconds(timeoutMs + size / 100);
                    var r = cmd.BeginExecute(null, null, stdout, stderr);
                    if (stdin != null)
                    {
                        var buf = new byte[65536]; int n;
                        while ((n = stdin.Read(buf, 0, buf.Length)) > 0) cmd.SendData(buf, 0, n);
                        // the pinned SSH.NET fork has no public end-of-input call: the command channel's own SendEof
                        var ch = typeof(SshCommand).GetField("_channel", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(cmd);
                        ch?.GetType().GetMethod("SendEof", BindingFlags.Instance | BindingFlags.Public)?.Invoke(ch, null);
                    }
                    cmd.EndExecute(r);
                    return cmd.ExitStatus;
                }
            }
            catch (SshOperationTimeoutException ex) { throw new ShellTimeoutException("the SSH command timed out (" + ex.Message + ")"); }
            catch (SshConnectionException ex) { throw new ShellLostException("the SSH connection ended (" + ex.Message + ")", ex); }
            catch (SocketException ex) { throw new ShellLostException("the SSH connection ended (" + ex.Message + ")", ex); }
        }

        public void Disconnect() { if (client.IsConnected) client.Disconnect(); }
        public void Dispose() { client.Dispose(); }
    }
}
