// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Console Browser's live views: the console's data storage as a read-only seekable stream. Each chunk is one bounded read request to the
// controlled worker (which holds the read-only console session and refuses anything outside the data storage); the answers come back on
// the worker's reader thread, so a read never waits on the window's own thread. A small cache keeps the most recent chunks.
using CcsBackup;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace CcsBackup.Ui
{
    public sealed class WorkerRangeStream : Stream
    {
        const int Chunk = 1 << 20, Keep = 16;
        static readonly TimeSpan Wait = TimeSpan.FromMinutes(2);
        readonly WorkerClient worker; readonly long length;
        readonly Dictionary<long, byte[]> cache = new Dictionary<long, byte[]>(); readonly LinkedList<long> order = new LinkedList<long>();
        readonly Dictionary<string, (ManualResetEventSlim done, Dictionary<string, object> answer)> waiting = new Dictionary<string, (ManualResetEventSlim, Dictionary<string, object>)>();
        readonly object gate = new object(); long pos;
        public long Requests { get; private set; }

        public WorkerRangeStream(WorkerClient worker, long length)
        {
            this.worker = worker; this.length = length;
            worker.RangeData += Answer;
        }

        void Answer(Dictionary<string, object> d)
        {
            lock (waiting) { var id = d.S("id"); if (id != null && waiting.TryGetValue(id, out var w)) { waiting[id] = (w.done, d); w.done.Set(); } }
        }

        byte[] Get(long idx)
        {
            if (cache.TryGetValue(idx, out var b)) { order.Remove(idx); order.AddFirst(idx); return b; }
            long off = idx * Chunk; long n = Math.Min(Chunk, length - off) / 4096 * 4096;
            if (n <= 0) throw new IOException("the end of the console's data storage cannot be read in whole blocks");
            var id = Guid.NewGuid().ToString("N"); var ev = new ManualResetEventSlim(false);
            lock (waiting) waiting[id] = (ev, null);
            try
            {
                worker.Send(new JObj { { "cmd", "range" }, { "id", id }, { "offset", off }, { "length", n } }); Requests++;
                if (!ev.Wait(Wait) || !worker.Running && !ev.IsSet) throw new IOException("the console did not answer (is the USB cable still connected?)");
                Dictionary<string, object> a; lock (waiting) a = waiting[id].answer;
                if (a == null || !a.B("ok")) throw new IOException(a?.S("problem") ?? "the console did not answer");
                b = Convert.FromBase64String(a.S("data") ?? "");
                if (b.Length != n) throw new IOException("the console sent a different amount of data than was asked for");
            }
            finally { lock (waiting) waiting.Remove(id); ev.Dispose(); }
            cache[idx] = b; order.AddFirst(idx);
            if (order.Count > Keep) { cache.Remove(order.Last.Value); order.RemoveLast(); }
            return b;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (gate)
            {
                if (pos >= length) return 0; count = (int)Math.Min(count, length - pos); int done = 0;
                while (done < count)
                {
                    var c = Get((pos + done) / Chunk); int at = (int)((pos + done) % Chunk); int k = Math.Min(count - done, c.Length - at);
                    if (k <= 0) break; Buffer.BlockCopy(c, at, buffer, offset + done, k); done += k;
                }
                pos += done; return done;
            }
        }
        public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
        public override long Length => length; public override long Position { get => pos; set => pos = value; }
        public override long Seek(long o, SeekOrigin s) => pos = s == SeekOrigin.Begin ? o : s == SeekOrigin.Current ? pos + o : length + o;
        public override void Flush() { }
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) worker.RangeData -= Answer; base.Dispose(disposing); }
    }
}
