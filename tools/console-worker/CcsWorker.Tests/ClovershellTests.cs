// Classic Cart Studio console transport worker tests. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
using CcsWorker.Ops;
using CcsWorker.Shell;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace CcsWorker.Tests
{
    // An input stream whose first read waits for the test (a caller's slow or stuck input): it then returns one byte, the end of
    // input, or an error, so a read that completes after its command ended is tested by the test's ordering, not by thread timing.
    sealed class HeldInput : Stream
    {
        public enum Then { Data, Eof, Error }
        readonly Then then; volatile bool done;
        public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(), Release = new ManualResetEventSlim(), Returned = new ManualResetEventSlim();
        public HeldInput(Then t) { then = t; }
        public override int Read(byte[] b, int o, int n)
        {
            if (done) return 0;
            Entered.Set(); Release.Wait(); done = true; Returned.Set();
            if (then == Then.Error) throw new IOException("the input failed late");
            if (then == Then.Eof) return 0;
            b[o] = 42; return 1;
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long x, SeekOrigin y) => throw new NotSupportedException();
        public override void SetLength(long x) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int n) => throw new NotSupportedException();
    }

    static class ClovershellTests
    {
        // Frames for command id (stdin data or end of input, status requests) written after its EXEC_KILL.
        static int AfterKill(SimClovershellDevice d, int id)
        {
            var f = d.Frames(); int k = f.FindLastIndex(x => x.Item1 == 16 && x.Item2 == id);
            if (k < 0) throw new Exception("no EXEC_KILL for id " + id);
            return f.Skip(k + 1).Count(x => (x.Item1 == 12 || x.Item1 == 19) && x.Item2 == id);
        }
        static bool Within(Func<bool> f, int ms) => SpinWait.SpinUntil(f, ms);

        static Tuple<int, byte[], byte[]> R(int code, string o, string e = "") => Tuple.Create(code, Encoding.UTF8.GetBytes(o), Encoding.UTF8.GetBytes(e));
        static int Run(ClovershellShell s, string cmd, Stream stdin, out string o, out string e, int timeout = 5000)
        {
            var so = new MemoryStream(); var se = new MemoryStream(); int c = s.Execute(cmd, stdin, so, se, timeout);
            o = Encoding.UTF8.GetString(so.ToArray()); e = Encoding.UTF8.GetString(se.ToArray()); return c;
        }

        public static void Register()
        {
            T.Add("clovershell", "connect: kill-all for shells and execs, then a ping answered", () =>
            {
                var d = new SimClovershellDevice();
                using (var s = new ClovershellShell(d))
                {
                    T.True(s.IsOnline, "online");
                    var first = d.HostFrames.Take(3).Select(f => f.Item1).ToArray();
                    T.True(first.SequenceEqual(new byte[] { 8, 17, 0 }), "SHELL_KILL_ALL, EXEC_KILL_ALL, PING (got " + string.Join(",", first) + ")");
                    T.True(s.Ping() >= 0, "a later ping");
                }
            });

            T.Add("clovershell", "a silent device at connect is refused (connection lost)", () =>
            {
                var d = new SimClovershellDevice { Silent = true };
                T.Throws<ShellLostException>(() => new ClovershellShell(d), "connect to a silent device");
            });

            T.Add("clovershell", "exec: stdout, stderr and the exit code; the command text matched to its id", () =>
            {
                var d = new SimClovershellDevice(); d.Handler = (c, i) => c == "echo hi" ? R(0, "hi\n") : R(3, "", "bad: " + c);
                using (var s = new ClovershellShell(d))
                {
                    T.Eq(0, Run(s, "echo hi", null, out var o, out var e), "exit code"); T.Eq("hi\n", o, "stdout");
                    T.Eq(3, Run(s, "false x", null, out o, out e), "exit code 3"); T.Eq("bad: false x", e, "stderr");
                    T.Eq(0, Run(s, "echo hi", null, out o, out e), "again"); T.Eq("hi\n", o, "stdout again");
                }
            });

            T.Add("clovershell", "large stdin (300 KB) in 8 KiB frames, ended by an empty frame, bytes exact", () =>
            {
                var data = new byte[300 * 1000]; new Random(1).NextBytes(data); byte[] got = null;
                var d = new SimClovershellDevice(); d.Handler = (c, i) => { got = i(); return R(0, ConsoleOps.Sha(got) + "\n"); };
                using (var s = new ClovershellShell(d))
                {
                    T.Eq(0, Run(s, "cat > x && sha256sum x", new MemoryStream(data), out var o, out var e), "exit code");
                    T.True(got != null && T.Same(data, got), "the device got the exact bytes");
                    T.Eq(ConsoleOps.Sha(data), o.Trim(), "readback");
                    var stdin = d.HostFrames.Where(f => f.Item1 == 12).ToList();
                    T.True(stdin.All(f => f.Item3 <= 8192), "stdin frames carry at most 8 KiB");
                    T.Eq((data.Length + 8191) / 8192, stdin.Count(f => f.Item3 > 0), "data frames");
                    T.Eq(0, stdin.Last().Item3, "the last stdin frame is empty (end of input)");
                }
            });

            T.Add("clovershell", "flow control: a reported backlog makes the sender ask for the queue status and wait", () =>
            {
                var data = new byte[120 * 1000]; new Random(2).NextBytes(data); byte[] got = null;
                var d = new SimClovershellDevice { BacklogUntilRequests = 3 }; d.Handler = (c, i) => { got = i(); return R(0, ""); };
                using (var s = new ClovershellShell(d))
                {
                    T.Eq(0, Run(s, "cat > y", new MemoryStream(data), out _, out _), "exit code");
                    T.True(T.Same(data, got), "exact bytes");
                    T.True(d.HostFrames.Count(f => f.Item1 == 19) >= 2, "EXEC_STDIN_FLOW_STAT_REQ sent while the queue was full");
                }
            });

            // Late flow reports, held by the simulated console and released by the test (no dependence on thread timing): the sender may
            // run at most MaxUnreported ahead of the last report the reader applied, asks for one, waits, and resumes once one is applied.
            int bound = ClovershellShell.MaxUnreported / (8 * 1024);
            T.Add("clovershell", "flow control: with the console's reports late, the sender stops after 64 KiB not yet reported, asks, and resumes when a report arrives", () =>
            {
                var data = new byte[200 * 1000]; new Random(5).NextBytes(data); byte[] got = null; int code = -1; Exception err = null;
                var d = new SimClovershellDevice { HoldReports = true }; d.Handler = (c, i) => { got = i(); return R(0, ""); };
                using (var s = new ClovershellShell(d))
                {
                    var t = new Thread(() => { try { code = Run(s, "cat > z", new MemoryStream(data), out _, out _); } catch (Exception ex) { err = ex; } }) { IsBackground = true };
                    t.Start();
                    T.True(d.StatusRequested.Wait(5000), "the sender asked for a report");
                    T.Eq(bound, d.StdinFrames, "frames sent before it stopped (64 KiB in 8 KiB frames)");
                    Thread.Sleep(300);
                    T.Eq(bound, d.StdinFrames, "nothing more sent while no report was applied (watched for 300 ms)");
                    d.ReleaseReports();
                    T.True(t.Join(5000), "the command completed after the reports arrived");
                    T.True(err == null, "no error (" + (err == null ? "" : err.GetType().Name + ": " + err.Message) + ")");
                    T.Eq(0, code, "exit code"); T.True(T.Same(data, got), "exact bytes");
                }
            });

            T.Add("clovershell", "flow control: the console gone while the sender waits for a report is a lost connection, and nothing more is sent", () =>
            {
                var data = new byte[200 * 1000]; new Random(6).NextBytes(data); Exception err = null;
                var d = new SimClovershellDevice { HoldReports = true }; d.Handler = (c, i) => { i(); return R(0, ""); };
                using (var s = new ClovershellShell(d))
                {
                    var t = new Thread(() => { try { Run(s, "cat > z", new MemoryStream(data), out _, out _); } catch (Exception ex) { err = ex; } }) { IsBackground = true };
                    t.Start();
                    T.True(d.StatusRequested.Wait(5000), "the sender is waiting for a report");
                    d.Gone = true;
                    T.True(t.Join(5000), "the command ended");
                    T.True(err is ShellLostException, "lost connection (got " + (err == null ? "no error" : err.GetType().Name) + ")");
                    T.Eq(bound, d.StdinFrames, "no stdin frame after the bound"); T.True(!s.IsOnline, "offline");
                }
            });

            T.Add("clovershell", "flow control: a console that answers pings but never reports: the silence timeout stops the command and the sender stops with it", () =>
            {
                var data = new byte[200 * 1000]; new Random(7).NextBytes(data);
                var d = new SimClovershellDevice { HoldReports = true }; d.Handler = (c, i) => { i(); return R(0, ""); };
                using (var s = new ClovershellShell(d))
                {
                    T.Throws<ShellTimeoutException>(() => Run(s, "cat > z", new MemoryStream(data), out _, out _, 400), "timeout");
                    T.True(d.Frames().Any(f => f.Item1 == 16), "EXEC_KILL sent"); T.True(s.IsOnline, "still online");
                    T.True(Within(() => s.DroppedFrames > 0, 3000), "the waiting sender woke and its next status request was refused at the device boundary");
                    T.Eq(0, AfterKill(d, d.Killed.Single()), "no frame of any kind for the command after its EXEC_KILL"); T.Eq(bound, d.StdinFrames, "never past the bound");
                    T.True(d.Frames().Count(f => f.Item1 == 12 && f.Item3 == 0) == 0, "no end-of-input frame for the stopped command");
                }
            });

            T.Add("clovershell", "flow control: a console that keeps reporting a full queue gets nothing more; the silence timeout stops the command", () =>
            {
                var data = new byte[200 * 1000]; new Random(8).NextBytes(data);
                var d = new SimClovershellDevice { BacklogUntilRequests = int.MaxValue }; d.Handler = (c, i) => { i(); return R(0, ""); };
                using (var s = new ClovershellShell(d))
                {
                    T.Throws<ShellTimeoutException>(() => Run(s, "cat > z", new MemoryStream(data), out _, out _, 400), "timeout");
                    T.True(d.Frames().Any(f => f.Item1 == 16), "EXEC_KILL sent"); T.True(s.IsOnline, "still online");
                    T.True(Within(() => s.DroppedFrames > 0, 3000), "the paused sender's next status request was refused at the device boundary");
                    T.Eq(0, AfterKill(d, d.Killed.Single()), "no frame of any kind for the command after its EXEC_KILL");
                    int n = d.StdinFrames; T.True(n <= bound, "at most the unreported bound was sent into the full queue (" + n + " frames)");
                    T.True(d.Frames().Count(f => f.Item1 == 19) >= 2, "it kept asking for the queue status while waiting");
                }
            });

            // The command's lifecycle at the device boundary (Codex v1p F1): a send whose read, pause or wait for the write lock completes
            // after the command ended writes nothing, whatever it was going to send.
            foreach (var mode in new[] { HeldInput.Then.Data, HeldInput.Then.Eof, HeldInput.Then.Error })
                T.Add("clovershell", "lifecycle: an input read that returns after its command timed out and was killed sends nothing (it returns " + mode + ")", () =>
                {
                    var d = new SimClovershellDevice(); d.Handler = (c, i) => { i(); return R(0, ""); };
                    var p = new HeldInput(mode); Exception err = null;
                    using (var s = new ClovershellShell(d))
                    {
                        var t = new Thread(() => { try { Run(s, "cat > late", p, out _, out _, 400); } catch (Exception ex) { err = ex; } }) { IsBackground = true };
                        t.Start();
                        T.True(p.Entered.Wait(3000), "the input read started");
                        T.True(t.Join(4000), "the command returned while its input read was still held");
                        T.True(err is ShellTimeoutException, "a timeout (got " + (err == null ? "none" : err.GetType().Name) + ")");
                        int id = d.Killed.Single(), before = s.DroppedFrames;
                        p.Release.Set();
                        T.True(Within(() => s.DroppedFrames > before, 3000), "the late send reached the device boundary and was refused");
                        T.Eq(0, AfterKill(d, id), "no frame for the command after its EXEC_KILL");
                        T.Eq(0, d.Frames().Count(f => f.Item1 == 12 && f.Item2 == id), "no stdin frame of any kind for it (data or end of input)");
                        T.True(s.IsOnline, "still online");
                    }
                });

            T.Add("clovershell", "lifecycle: a late send waiting for the write lock while its command's EXEC_KILL is being written sends nothing", () =>
            {
                var d = new SimClovershellDevice { HoldKill = new ManualResetEventSlim() }; d.Handler = (c, i) => { i(); return R(0, ""); };
                var p = new HeldInput(HeldInput.Then.Data); var queued = new ManualResetEventSlim(); Exception err = null;
                using (var s = new ClovershellShell(d))
                {
                    s.BeforeCommandWrite = () => { if (p.Returned.IsSet) queued.Set(); };
                    var t = new Thread(() => { try { Run(s, "cat > late", p, out _, out _, 400); } catch (Exception ex) { err = ex; } }) { IsBackground = true };
                    t.Start();
                    T.True(p.Entered.Wait(3000), "the input read started");
                    T.True(d.KillWriting.Wait(4000), "the timeout's EXEC_KILL is being written (the console holds the write)");
                    p.Release.Set();
                    T.True(queued.Wait(3000), "the late send reached the device boundary while the EXEC_KILL write held the lock");
                    T.True(t.IsAlive, "the command had not returned yet");
                    d.HoldKill.Set();
                    T.True(t.Join(3000), "the command returned"); T.True(err is ShellTimeoutException, "a timeout");
                    T.True(Within(() => s.DroppedFrames > 0, 3000), "the queued send was refused once it had the lock");
                    int id = d.Killed.Single();
                    T.Eq(0, AfterKill(d, id), "EXEC_KILL is the command's last frame");
                    T.Eq(0, d.Frames().Count(f => f.Item1 == 12 && f.Item2 == id), "no stdin frame for it");
                }
            });

            T.Add("clovershell", "lifecycle: the console gives the next command a killed command's id: the killed command's late input never reaches it", () =>
            {
                var d = new SimClovershellDevice { ReuseIds = true };
                var bRunning = new ManualResetEventSlim(); var bFinish = new ManualResetEventSlim();
                d.Handler = (c, i) => { i(); if (c == "cat > b") { bRunning.Set(); bFinish.Wait(5000); } return R(0, "done\n"); };
                var p = new HeldInput(HeldInput.Then.Data); var bIn = Encoding.ASCII.GetBytes("BBBB");
                using (var s = new ClovershellShell(d))
                {
                    T.Throws<ShellTimeoutException>(() => Run(s, "cat > a", p, out _, out _, 400), "the first command timed out (its input read still held)");
                    int idA = d.Killed.Single();
                    int code = -1; string o = null; Exception err = null;
                    var tb = new Thread(() => { try { code = Run(s, "cat > b", new MemoryStream(bIn), out o, out _, 5000); } catch (Exception ex) { err = ex; } }) { IsBackground = true };
                    tb.Start();
                    T.True(bRunning.Wait(3000), "the second command is running");
                    int before = s.DroppedFrames;
                    p.Release.Set();
                    T.True(Within(() => s.DroppedFrames > before, 3000), "the first command's late send was refused");
                    bFinish.Set();
                    T.True(tb.Join(5000), "the second command completed"); T.True(err == null, "no error (" + err?.Message + ")");
                    T.Eq(0, code, "its exit code"); T.Eq("done\n", o, "its output");
                    var b = d.Started.Single(x => x.Item2 == "cat > b");
                    T.Eq(idA, b.Item1, "the console gave the second command the first one's id");
                    T.True(T.Same(bIn, b.Item3), "the second command received exactly its own input (" + b.Item3.Length + " bytes)");
                }
            });

            T.Add("clovershell", "large stdout (200 KB) over many device frames, exact", () =>
            {
                var big = string.Concat(Enumerable.Range(0, 20000).Select(i => "line " + i.ToString("00000") + "\n")).Substring(0, 200000);
                var d = new SimClovershellDevice(); d.Handler = (c, i) => R(0, big);
                using (var s = new ClovershellShell(d)) { Run(s, "cat big", null, out var o, out _); T.Eq(big, o, "stdout"); T.True(d.MaxDevicePayload <= 0xFFFF, "frames within 64 KiB"); }
            });

            T.Add("clovershell", "a silent command (the console still answers pings) times out and is killed; the console stays online", () =>
            {
                var d = new SimClovershellDevice(); d.Handler = (c, i) => { Thread.Sleep(c == "sleep" ? 2500 : 0); return R(0, "x"); };
                using (var s = new ClovershellShell(d))
                {
                    T.Throws<ShellTimeoutException>(() => Run(s, "sleep", null, out _, out _, 400), "timeout");
                    T.True(d.HostFrames.Any(f => f.Item1 == 16), "EXEC_KILL sent"); T.True(s.IsOnline, "still online");
                }
            });

            T.Add("clovershell", "a console that stops answering mid-command is a lost connection", () =>
            {
                var d = new SimClovershellDevice(); d.Handler = (c, i) => { Thread.Sleep(5000); return R(0, ""); };
                using (var s = new ClovershellShell(d))
                {
                    d.SilentAfterExec = true;
                    T.Throws<ShellLostException>(() => Run(s, "anything", null, out _, out _, 400), "lost");
                    T.True(!s.IsOnline, "offline"); T.Throws<ShellLostException>(() => Run(s, "echo", null, out _, out _), "later commands");
                }
            });

            T.Add("clovershell", "the USB device gone mid-command is a lost connection", () =>
            {
                var d = new SimClovershellDevice(); d.Handler = (c, i) => { Thread.Sleep(3000); return R(0, ""); };
                using (var s = new ClovershellShell(d))
                {
                    new Thread(() => { Thread.Sleep(200); d.Gone = true; }) { IsBackground = true }.Start();
                    T.Throws<ShellLostException>(() => Run(s, "x", null, out _, out _, 10000), "lost");
                    T.True(!s.IsOnline, "offline");
                }
            });

            T.Add("clovershell", "frame bound: a command longer than one frame is refused before anything is sent", () =>
            {
                var d = new SimClovershellDevice();
                using (var s = new ClovershellShell(d))
                {
                    int before = d.HostFrames.Count;
                    T.Throws<ArgumentException>(() => Run(s, new string('x', 70000), null, out _, out _), "too long");
                    T.Eq(before, d.HostFrames.Count, "no frame sent"); T.True(d.MaxHostPayload <= 0xFFFF, "host frames within the bound");
                }
            });

            T.Add("clovershell", "the operations over clovershell: put streams the bytes and reads the hash back", () =>
            {
                var data = new byte[150 * 1000]; new Random(3).NextBytes(data); byte[] got = null;
                var d = new SimClovershellDevice();
                d.Handler = (c, i) => c.Contains("cat > ") ? R(0, ConsoleOps.Sha(got = i()) + "  /var/lib/ccs/a\n") : R(0, "");
                using (var s = new ClovershellShell(d))
                {
                    var r = new ConsoleOps(s, null).Put(new System.Collections.Generic.Dictionary<string, object> { ["path"] = "/var/lib/ccs/a", ["b64"] = Convert.ToBase64String(data), ["mode"] = "0644", ["sha256"] = ConsoleOps.Sha(data) });
                    T.Eq(true, (bool)r["match"], "match"); T.True(T.Same(data, got), "bytes");
                }
            });
        }
    }
}
