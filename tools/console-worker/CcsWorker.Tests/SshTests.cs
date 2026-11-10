// Classic Cart Studio console transport worker tests. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
using CcsWorker.Shell;
using System;
using System.IO;
using System.Text;

namespace CcsWorker.Tests
{
    static class SshTests
    {
        public static void Register()
        {
            T.Add("ssh", "exit status, both streams and the whole input reach the command", () =>
            {
                var f = new FakeSshSession(); f.Answer = (c, i) => Tuple.Create(c == "ok" ? 0 : 5, "out:" + i.Length, "err");
                using (var s = new SshShell(f))
                {
                    var o = new MemoryStream(); var e = new MemoryStream();
                    T.Eq(0, s.Execute("ok", new MemoryStream(new byte[70000]), o, e, 1000), "exit 0");
                    T.Eq("out:70000", Encoding.UTF8.GetString(o.ToArray()), "stdout"); T.Eq("err", Encoding.UTF8.GetString(e.ToArray()), "stderr");
                    T.Eq(5, s.Execute("other", null, null, null, 1000), "exit 5"); T.Eq("ssh", s.Kind, "kind");
                }
            });

            T.Add("ssh", "a timeout stays a timeout; the session stays connected", () =>
            {
                var f = new FakeSshSession { Throw = new ShellTimeoutException("slow") };
                using (var s = new SshShell(f)) { T.Throws<ShellTimeoutException>(() => s.Execute("x", null, null, null, 100), "timeout"); T.True(s.IsOnline, "still online"); }
            });

            T.Add("ssh", "an error with the session gone is a lost connection, and every later command too", () =>
            {
                var f = new FakeSshSession(); f.Connected = false;
                var s = new SshShell(f);
                T.True(!s.IsOnline, "offline when the session is not connected");
                T.Throws<ShellLostException>(() => s.Execute("x", null, null, null, 100), "refused while not connected");
                f.Connected = true;
                f.Answer = (c, i) => { f.Connected = false; throw new InvalidOperationException("broken pipe"); };
                T.Throws<ShellLostException>(() => s.Execute("x", null, null, null, 100), "lost");
                f.Connected = true; T.Throws<ShellLostException>(() => s.Execute("y", null, null, null, 100), "still lost after a reconnect of the fake (no silent reuse)");
            });

            T.Add("ssh", "a lost connection reported by the session passes through; disconnect ends the session", () =>
            {
                var f = new FakeSshSession { Throw = new ShellLostException("reset") };
                var s = new SshShell(f);
                T.Throws<ShellLostException>(() => s.Execute("x", null, null, null, 100), "lost"); T.True(!s.IsOnline, "offline");
                var g = new FakeSshSession(); var s2 = new SshShell(g); s2.Disconnect(); T.True(!g.Connected && !s2.IsOnline, "disconnected");
            });
        }
    }
}
