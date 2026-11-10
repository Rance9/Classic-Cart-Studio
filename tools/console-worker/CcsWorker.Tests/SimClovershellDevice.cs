// Classic Cart Studio console transport worker tests. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The device side of clovershell on an injected IBulkPipe, byte for byte in the upstream frame format (command, argument, 16-bit
// little-endian length, data): PING/PONG, kill-all, EXEC_NEW_REQ answered with an id and the command text, stdin frames until an empty
// one, the flow status, stdout/stderr frames each ended by an empty frame, EXEC_RESULT. Faults: silent, gone, a backlog.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace CcsWorker.Tests
{
    sealed class SimClovershellDevice : CcsWorker.Shell.IBulkPipe
    {
        public Func<string, Func<byte[]>, Tuple<int, byte[], byte[]>> Handler = (c, i) => Tuple.Create(0, new byte[0], new byte[0]);
        public volatile bool Silent;            // answers nothing (a console that stopped)
        public volatile bool Gone;              // the USB device is gone: reads and writes fail
        public volatile bool SilentAfterExec;   // goes silent right after accepting a command
        public int BacklogUntilRequests;        // the queue reports full until the host has asked for its status that many times
        int statusRequests;
        public int DeviceChunk = 16000;
        public readonly List<Tuple<byte, byte, int>> HostFrames = new List<Tuple<byte, byte, int>>();
        public int MaxHostPayload, MaxDevicePayload, StdinFrames;
        public readonly List<int> Killed = new List<int>();

        readonly BlockingCollection<byte[]> toHost = new BlockingCollection<byte[]>();
        byte[] pendingRead; int pendingOff;
        readonly MemoryStream inbuf = new MemoryStream();
        readonly Dictionary<int, Exec> execs = new Dictionary<int, Exec>();
        int nextId = 1;

        sealed class Exec { public int Id; public string Command; public MemoryStream Stdin = new MemoryStream(); public ManualResetEventSlim Eof = new ManualResetEventSlim(); public volatile bool Killed; }

        void Send(byte cmd, byte arg, byte[] data, int off, int len)
        {
            if (Silent || Gone) return;
            var b = new byte[len + 4]; b[0] = cmd; b[1] = arg; b[2] = (byte)(len & 0xFF); b[3] = (byte)(len >> 8);
            if (len > 0) Buffer.BlockCopy(data, off, b, 4, len);
            lock (this) MaxDevicePayload = Math.Max(MaxDevicePayload, len);
            toHost.Add(b);
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMs)
        {
            if (Gone) throw new IOException("device removed");
            if (pendingRead == null)
            {
                if (!toHost.TryTake(out pendingRead, Silent ? Math.Min(timeoutMs, 20) : timeoutMs)) return 0;   // silent: what was sent before still arrives
                pendingOff = 0;
            }
            int n = Math.Min(count, pendingRead.Length - pendingOff);
            Buffer.BlockCopy(pendingRead, pendingOff, buffer, offset, n); pendingOff += n;
            if (pendingOff >= pendingRead.Length) pendingRead = null;
            return n;
        }

        public void Write(byte[] buffer, int offset, int count, int timeoutMs)
        {
            if (Gone) throw new IOException("device removed");
            lock (inbuf)
            {
                inbuf.Write(buffer, offset, count);
                var a = inbuf.ToArray(); int pos = 0;
                while (a.Length - pos >= 4)
                {
                    int len = a[pos + 2] | a[pos + 3] << 8;
                    if (a.Length - pos < 4 + len) break;
                    var data = new byte[len]; Buffer.BlockCopy(a, pos + 4, data, 0, len);
                    Frame(a[pos], a[pos + 1], data);
                    pos += 4 + len;
                }
                inbuf.SetLength(0); inbuf.Write(a, pos, a.Length - pos);
            }
        }

        void Frame(byte cmd, byte arg, byte[] data)
        {
            lock (this) { HostFrames.Add(Tuple.Create(cmd, arg, data.Length)); MaxHostPayload = Math.Max(MaxHostPayload, data.Length); }
            switch (cmd)
            {
                case 0: Send(1, 0, data, 0, data.Length); break;   // PING -> PONG
                case 9:   // EXEC_NEW_REQ
                    var e = new Exec { Id = nextId++, Command = Encoding.UTF8.GetString(data) };
                    lock (execs) execs[e.Id] = e;
                    Send(10, (byte)e.Id, data, 0, data.Length);
                    if (SilentAfterExec) { Silent = true; return; }
                    new Thread(() => Run(e)) { IsBackground = true }.Start();
                    break;
                case 12:   // EXEC_STDIN
                    Exec x; lock (execs) execs.TryGetValue(arg, out x);
                    if (x == null) break;
                    if (data.Length == 0) x.Eof.Set(); else { lock (x.Stdin) x.Stdin.Write(data, 0, data.Length); StdinFrames++; FlowStat(arg, false); }
                    break;
                case 19: FlowStat(arg, true); break;   // EXEC_STDIN_FLOW_STAT_REQ
                case 16: lock (execs) if (execs.TryGetValue(arg, out var k)) { k.Killed = true; Killed.Add(arg); } break;   // EXEC_KILL
            }
        }

        void FlowStat(byte id, bool request)
        {
            int q; lock (this) { if (request) statusRequests++; q = statusRequests < BacklogUntilRequests ? 40000 : 0; }
            var d = new byte[8]; BitConverter.GetBytes(q).CopyTo(d, 0); BitConverter.GetBytes(65536).CopyTo(d, 4);
            Send(18, id, d, 0, 8);
        }

        void Run(Exec e)
        {
            Func<byte[]> stdin = () => { e.Eof.Wait(); lock (e.Stdin) return e.Stdin.ToArray(); };
            var r = Handler(e.Command, stdin);
            if (e.Killed) return;
            Chunks(13, (byte)e.Id, r.Item2); Chunks(14, (byte)e.Id, r.Item3);
            Send(15, (byte)e.Id, new[] { (byte)r.Item1 }, 0, 1);
        }

        void Chunks(byte cmd, byte id, byte[] b)
        {
            for (int o = 0; o < b.Length; o += DeviceChunk) Send(cmd, id, b, o, Math.Min(DeviceChunk, b.Length - o));
            Send(cmd, id, null, 0, 0);
        }

        public void Dispose() { }
    }
}
