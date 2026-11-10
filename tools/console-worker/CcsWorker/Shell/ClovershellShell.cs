// Classic Cart Studio console transport worker. GPL-3.0-or-later.
// Derived from Hakchi2 CE 3.9.3 Clovershell/ClovershellConnection.cs and Clovershell/ExecConnection.cs (commit 25bfcfd; GPL-3.0;
// clusterrr and the Hakchi2 CE / Team Shinkansen authors): the frame format (command, argument, 16-bit length, data), the command codes,
// kill-all and drain at connect, PING/PONG, EXEC_NEW_REQ matched by command text, 8 KiB stdin frames with the flow-status back-off, the
// empty stdin frame as end of input, EXEC_RESULT as the exit code.
// Modified by Rance9, 2026: the USB device is behind IBulkPipe (opened only by RealTransports, Gate G1); a byte-stream packet parser in
// place of LibUsbDotNet's DataReceived event; no telnet shell listener and no background reconnect; a silent command is told apart from
// a silent console by a ping (timeout versus connection lost); the command on a timeout is killed (EXEC_KILL).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace CcsWorker.Shell
{
    public sealed class ClovershellShell : IConsoleShell
    {
        internal enum Cmd : byte
        {
            PING = 0, PONG = 1, SHELL_NEW_REQ = 2, SHELL_NEW_RESP = 3, SHELL_IN = 4, SHELL_OUT = 5, SHELL_CLOSED = 6, SHELL_KILL = 7, SHELL_KILL_ALL = 8,
            EXEC_NEW_REQ = 9, EXEC_NEW_RESP = 10, EXEC_PID = 11, EXEC_STDIN = 12, EXEC_STDOUT = 13, EXEC_STDERR = 14, EXEC_RESULT = 15, EXEC_KILL = 16,
            EXEC_KILL_ALL = 17, EXEC_STDIN_FLOW_STAT = 18, EXEC_STDIN_FLOW_STAT_REQ = 19
        }
        public const int MaxPayload = 0xFFFF;   // the frame's 16-bit length
        const int StdinChunk = 8 * 1024;

        sealed class Exec
        {
            public readonly string Command; public readonly Stream Stdin, Stdout, Stderr;
            public volatile int Id = -1; public volatile bool Finished; public int Result; public volatile int StdinQueue;
            public DateTime LastData = DateTime.UtcNow; public volatile Exception StdinError; public volatile bool StdinDone;
            public Exec(string c, Stream i, Stream o, Stream e) { Command = c; Stdin = i; Stdout = o; Stderr = e; }
        }

        readonly IBulkPipe pipe;
        readonly object writeLock = new object();
        readonly List<Exec> pending = new List<Exec>();
        readonly Exec[] execs = new Exec[256];
        readonly Thread reader;
        volatile bool online, closing;
        volatile byte[] lastPong;
        volatile string lostWhy;

        public string Kind => "clovershell";
        public bool IsOnline => online;

        public ClovershellShell(IBulkPipe pipe)
        {
            this.pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
            online = true;
            Write(Cmd.SHELL_KILL_ALL, 0, null, 0, 0);   // upstream: kill all other sessions and drop all output
            Write(Cmd.EXEC_KILL_ALL, 0, null, 0, 0);
            var drain = new byte[65536];
            for (int i = 0; i < 256 && pipe.Read(drain, 0, drain.Length, 50) > 0; i++) { }
            reader = new Thread(ReadLoop) { IsBackground = true, Name = "clovershell reader" };
            reader.Start();
            if (Ping(1500) < 0) { Dispose(); throw new ShellLostException("clovershell did not answer a ping"); }
        }

        void Lose(string why) { if (lostWhy == null) lostWhy = why; online = false; }

        void ReadLoop()
        {
            var buf = new byte[65536]; var acc = new byte[4 * 65536]; int n = 0;
            try
            {
                while (!closing)
                {
                    int got = pipe.Read(buf, 0, buf.Length, 100);
                    if (got <= 0) continue;
                    if (n + got > acc.Length) Array.Resize(ref acc, Math.Max(acc.Length * 2, n + got));
                    Buffer.BlockCopy(buf, 0, acc, n, got); n += got;
                    int pos = 0;
                    while (n - pos >= 4)
                    {
                        int len = acc[pos + 2] | acc[pos + 3] << 8;
                        if (n - pos < 4 + len) break;
                        Proceed((Cmd)acc[pos], acc[pos + 1], acc, pos + 4, len);
                        pos += 4 + len;
                    }
                    if (pos > 0) { Buffer.BlockCopy(acc, pos, acc, 0, n - pos); n -= pos; }
                }
            }
            catch (Exception ex) { if (!closing) Lose("the console's USB connection ended (" + ex.Message + ")"); }
        }

        void Proceed(Cmd cmd, byte arg, byte[] data, int pos, int len)
        {
            Exec c;
            switch (cmd)
            {
                case Cmd.PONG:
                    var p = new byte[len]; Buffer.BlockCopy(data, pos, p, 0, len); lastPong = p; break;
                case Cmd.EXEC_NEW_RESP:
                    var text = Encoding.UTF8.GetString(data, pos, len);
                    lock (pending)
                    {
                        c = pending.LastOrDefault(x => x.Command == text && x.Id < 0);
                        if (c == null) break;
                        pending.Remove(c); execs[arg] = c; c.Id = arg;
                    }
                    break;
                case Cmd.EXEC_STDOUT:
                    c = execs[arg]; if (c == null) break;
                    c.Stdout?.Write(data, pos, len); c.LastData = DateTime.UtcNow; break;
                case Cmd.EXEC_STDERR:
                    c = execs[arg]; if (c == null) break;
                    c.Stderr?.Write(data, pos, len); c.LastData = DateTime.UtcNow; break;
                case Cmd.EXEC_RESULT:
                    c = execs[arg]; if (c == null || len < 1) break;
                    c.Result = data[pos]; c.Finished = true; break;
                case Cmd.EXEC_STDIN_FLOW_STAT:
                    c = execs[arg]; if (c == null || len < 8) break;
                    c.StdinQueue = data[pos] | data[pos + 1] << 8 | data[pos + 2] << 16 | data[pos + 3] << 24; break;
            }
        }

        void Write(Cmd cmd, byte arg, byte[] data, int pos, int len)
        {
            if (!online) throw new ShellLostException(lostWhy ?? "clovershell is offline");
            if (len > MaxPayload) throw new ArgumentException("a clovershell frame carries at most 65,535 bytes");
            var b = new byte[len + 4];
            b[0] = (byte)cmd; b[1] = arg; b[2] = (byte)(len & 0xFF); b[3] = (byte)(len >> 8 & 0xFF);
            if (len > 0) Buffer.BlockCopy(data, pos, b, 4, len);
            try { lock (writeLock) pipe.Write(b, 0, b.Length, 1000); }
            catch (Exception ex) { Lose("writing to the console failed (" + ex.Message + ")"); throw new ShellLostException(lostWhy, ex); }
        }

        /// <summary>Round trip in milliseconds, or -1 when the console does not answer.</summary>
        public int Ping(int timeoutMs = 1000)
        {
            var data = new byte[4]; new Random().NextBytes(data); lastPong = null;
            var t0 = DateTime.UtcNow;
            try { Write(Cmd.PING, 0, data, 0, 4); } catch (ShellLostException) { return -1; }
            while ((DateTime.UtcNow - t0).TotalMilliseconds < timeoutMs)
            {
                var p = lastPong;
                if (p != null && p.SequenceEqual(data)) return (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                if (!online) return -1;
                Thread.Sleep(5);
            }
            return -1;
        }

        public int Execute(string command, Stream stdin, Stream stdout, Stream stderr, int timeoutMs)
        {
            if (!online) throw new ShellLostException(lostWhy ?? "clovershell is offline");
            var text = Encoding.UTF8.GetBytes(command);
            if (text.Length > MaxPayload) throw new ArgumentException("the command is longer than one clovershell frame");
            var c = new Exec(command, stdin, stdout, stderr);
            lock (pending) pending.Add(c);
            Thread stdinThread = null;
            try
            {
                Write(Cmd.EXEC_NEW_REQ, 0, text, 0, text.Length);
                var t0 = DateTime.UtcNow;
                while (c.Id < 0)
                {
                    Thread.Sleep(5);
                    if (!online) throw new ShellLostException(lostWhy ?? "clovershell went offline");
                    if ((DateTime.UtcNow - t0).TotalMilliseconds > 2500) throw new ShellTimeoutException("the console did not accept the command (exec request timeout)");
                }
                c.LastData = DateTime.UtcNow;
                if (stdin != null) { stdinThread = new Thread(() => StdinLoop(c, timeoutMs)) { IsBackground = true, Name = "clovershell stdin" }; stdinThread.Start(); }
                while (!c.Finished)
                {
                    Thread.Sleep(5);
                    if (!online) throw new ShellLostException(lostWhy ?? "clovershell went offline");
                    if (c.StdinError is ShellLostException le) throw le;
                    if (timeoutMs > 0 && !c.Finished && (DateTime.UtcNow - c.LastData).TotalMilliseconds > timeoutMs)
                    {
                        if (Ping(1000) < 0) { Lose("the console stopped answering"); throw new ShellLostException(lostWhy); }
                        try { Write(Cmd.EXEC_KILL, (byte)c.Id, null, 0, 0); } catch (ShellLostException) { }
                        throw new ShellTimeoutException("the command was silent for longer than " + timeoutMs + " ms (it was stopped)");
                    }
                }
                stdinThread?.Join(2000);
                if (c.StdinError != null) throw new ShellException("reading the command's input failed (" + c.StdinError.Message + ")", c.StdinError);
                return c.Result;
            }
            finally
            {
                lock (pending) { pending.Remove(c); if (c.Id >= 0 && execs[c.Id] == c) execs[c.Id] = null; }
            }
        }

        void StdinLoop(Exec c, int timeoutMs)
        {
            try
            {
                var buffer = new byte[StdinChunk];
                while (online && !c.Finished)
                {
                    int l = c.Stdin.Read(buffer, 0, buffer.Length);
                    if (l <= 0) break;
                    Write(Cmd.EXEC_STDIN, (byte)c.Id, buffer, 0, l);
                    c.LastData = DateTime.UtcNow;
                    if (c.StdinQueue > 32 * 1024)
                    {
                        var t0 = DateTime.UtcNow;
                        while (c.StdinQueue > 16 * 1024 && online && !c.Finished)
                        {
                            Thread.Sleep(50);
                            Write(Cmd.EXEC_STDIN_FLOW_STAT_REQ, (byte)c.Id, null, 0, 0);
                            if (timeoutMs > 0 && (DateTime.UtcNow - t0).TotalMilliseconds > timeoutMs) break;   // the wait loop reports the silence
                        }
                        c.LastData = DateTime.UtcNow;
                    }
                }
                if (online && !c.Finished) Write(Cmd.EXEC_STDIN, (byte)c.Id, null, 0, 0);   // end of input
                if (c.StdinQueue > 0 && online && !c.Finished) { Thread.Sleep(50); Write(Cmd.EXEC_STDIN_FLOW_STAT_REQ, (byte)c.Id, null, 0, 0); }
                c.StdinDone = true;
            }
            catch (Exception ex)
            {
                c.StdinError = ex;
                if (!(ex is ShellLostException)) try { if (online && !c.Finished) Write(Cmd.EXEC_STDIN, (byte)c.Id, null, 0, 0); } catch (ShellLostException) { }   // end the input so the command ends
            }
        }

        public void Disconnect() { Dispose(); }

        public void Dispose()
        {
            if (closing) return;
            closing = true; online = false; if (lostWhy == null) lostWhy = "disconnected";
            try { pipe.Dispose(); } catch { }
            if (reader != null && Thread.CurrentThread != reader) reader.Join(1000);
        }
    }
}
