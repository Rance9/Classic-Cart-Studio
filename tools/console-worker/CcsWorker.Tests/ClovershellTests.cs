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
    static class ClovershellTests
    {
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
