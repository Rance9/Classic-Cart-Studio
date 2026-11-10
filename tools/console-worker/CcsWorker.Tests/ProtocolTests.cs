// Classic Cart Studio console transport worker tests. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
using CcsWorker.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace CcsWorker.Tests
{
    static class ProtocolTests
    {
        static Dictionary<string, object> A(params object[] kv) { var d = new Dictionary<string, object>(); for (int i = 0; i < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1]; return d; }

        public static void Register()
        {
            T.Add("protocol", "capabilities: protocol 1, the closed tiers, transports, every command, the pinned upstream", () =>
            {
                using (var h = new Harness())
                {
                    var e = h.Call("capabilities"); var r = Harness.Res(e);
                    T.Eq(1, (int)e["v"], "envelope version");
                    T.Eq(1, (int)r["protocol"], "protocol"); T.Eq("ccs-console-worker", r["worker"] as string, "worker");
                    var tiers = (Dictionary<string, object>)r["tiers"]; T.Eq(false, (bool)tiers["inspect"], "inspect tier"); T.Eq(false, (bool)tiers["install"], "install tier");
                    T.True(((object[])r["transports"]).SequenceEqual(new object[] { "clovershell", "ssh" }), "transports");
                    T.True(Protocol.WorkerServer.Commands.All(c => ((object[])r["ops"]).Contains(c)), "every command listed");
                    var up = (Dictionary<string, object>)r["upstream"]; T.True(((string)up["hakchi"]).StartsWith("25bfcfd") && ((string)up["sshnet"]).StartsWith("32181aeb") && (string)up["libusbdotnet"] == "2.2.29", "upstream identities");
                    T.True(r["sha256"] is string s && s.Length == 64, "own SHA-256");
                    T.Eq(false, (bool)r["package"], "no package folder");
                }
            });

            T.Add("protocol", "bad requests: not JSON, not an object, wrong version, unknown command, arguments not an object", () =>
            {
                using (var h = new Harness())
                {
                    h.SendRaw("{nope"); var e = h.Next(); T.Eq("bad-json", e["code"] as string, "not JSON"); T.True(e["id"] == null, "no id");
                    h.SendRaw("[1,2]"); T.Eq("bad-json", h.Next()["code"] as string, "an array");
                    h.SendRaw("{\"v\":2,\"id\":\"x\",\"cmd\":\"capabilities\"}"); e = h.Next(); T.Eq("bad-version", e["code"] as string, "version 2"); T.Eq("x", e["id"] as string, "id echoed");
                    h.SendRaw("{\"id\":\"y\",\"cmd\":\"capabilities\"}"); T.Eq("bad-version", h.Next()["code"] as string, "no version");
                    h.SendRaw("{\"v\":1,\"id\":\"z\",\"cmd\":\"format-nand\"}"); e = h.Next(); T.Eq("bad-command", e["code"] as string, "unknown command"); T.Eq("z", e["id"] as string, "id echoed");
                    h.SendRaw("{\"v\":1,\"id\":\"w\",\"cmd\":\"stat\",\"args\":[1]}"); T.Eq("bad-command", h.Next()["code"] as string, "args not an object");
                    h.SendRaw(""); h.SendRaw("   ");
                    T.Eq("result", h.Call("capabilities")["event"] as string, "still serving after bad lines (blank lines ignored)");
                }
            });

            T.Add("protocol", "a 1.2 MB line is too-large and the next request is still answered (process)", () =>
            {
                using (var p = new WorkerProcess())
                {
                    p.Write("{\"v\":1,\"id\":\"big\",\"cmd\":\"stat\",\"args\":{\"path\":\"" + new string('a', 1200000) + "\"}}\n");
                    p.Write("{\"v\":1,\"id\":\"after\",\"cmd\":\"capabilities\"}\n{\"v\":1,\"id\":\"end\",\"cmd\":\"shutdown\"}\n");
                    T.True(p.WaitExit(30000), "exits after shutdown"); T.Eq(0, p.P.ExitCode, "exit code");
                    T.Eq(3, p.Out.Count, "three replies");
                    var a = (Dictionary<string, object>)Harness.Json.DeserializeObject(p.Out[0]); T.Eq("too-large", a["code"] as string, "too-large");
                    var b = (Dictionary<string, object>)Harness.Json.DeserializeObject(p.Out[1]); T.Eq("after", b["id"] as string, "next request answered"); T.Eq("result", b["event"] as string, "as a result");
                }
            });

            T.Add("protocol", "stdout carries protocol lines only; stdin EOF ends the worker with exit 0 (process)", () =>
            {
                using (var p = new WorkerProcess())
                {
                    // connect only while the gate is closed: a test never sends it to the production worker once its gate is open (it would connect)
                    var connect = Gate.PhysicalInspectEnabled ? "" : "{\"v\":1,\"id\":2,\"cmd\":\"connect\",\"args\":{\"transport\":\"ssh\"}}\n";
                    p.Write("{\"v\":1,\"id\":1,\"cmd\":\"capabilities\"}\nnot json\n" + connect + "{\"v\":1,\"id\":3,\"cmd\":\"identify\"}\n{\"v\":1,\"id\":4,\"cmd\":\"put\",\"args\":{\"path\":\"/etc/passwd\"}}\n");
                    p.P.StandardInput.Close();
                    T.True(p.WaitExit(30000), "exits at end of input"); T.Eq(0, p.P.ExitCode, "exit code");
                    T.Eq(connect.Length > 0 ? 5 : 4, p.Out.Count, "one line per request");
                    foreach (var l in p.Out) { var o = (Dictionary<string, object>)Harness.Json.DeserializeObject(l); T.Eq(1, (int)o["v"], "v"); T.True(o.ContainsKey("id") && o.ContainsKey("event"), "envelope: " + l); }
                    var codes = p.Out.Skip(1).Select(l => ((Dictionary<string, object>)Harness.Json.DeserializeObject(l))["code"] as string).ToList();
                    var want = new List<string> { "bad-json" }; if (connect.Length > 0) want.Add("physical-disabled");
                    want.Add("not-connected"); want.Add(Gate.PhysicalWriteEnabled ? "not-connected" : "physical-disabled");   // put: the write tier is asked first (k3e)
                    T.True(codes.SequenceEqual(want), "codes: " + string.Join(",", codes));
                }
            });

            T.Add("protocol", "cancel while idle is not accepted; disconnect then an operation is not-connected; shutdown answers then exits", () =>
            {
                var sh = new ScriptedShell(); sh.Answer = (c, i) => Tuple.Create(0, "dir\n", ""); var h = new Harness(sh);
                T.Eq(false, (bool)Harness.Res(h.Call("cancel"))["accepted"], "idle cancel");
                T.Eq("result", h.Call("stat", A("path", "/var/lib/ccs"))["event"] as string, "an op with the attached shell");
                Harness.Res(h.Call("disconnect"));
                T.Eq("not-connected", Harness.Err(h.Call("stat", A("path", "/var/lib/ccs"))), "after disconnect");
                Harness.Res(h.Call("shutdown")); T.True(h.Join(5000), "the server ended"); T.Eq(0, h.ExitCode, "exit code");
            });

            T.Add("protocol", "cancel during a put stops at a chunk boundary: progress events, accepted, then error cancelled", () =>
            {
                var pkg = Path.Combine(Path.GetTempPath(), "ccs-worker-pkg-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(pkg);
                try
                {
                    var data = new byte[3 * 1000 * 1000]; new Random(5).NextBytes(data); File.WriteAllBytes(Path.Combine(pkg, "big.bin"), data);
                    var sh = new ScriptedShell { ReadDelayMs = 40 };
                    sh.Answer = (c, i) => c.Contains("cat > ") ? Tuple.Create(0, Ops.ConsoleOps.Sha(i) + "  /var/lib/ccs/x\n", "") : Tuple.Create(0, "", "");
                    using (var h = new Harness(sh, pkg))
                    {
                        var id = h.Send("put", A("path", "/var/lib/ccs/x", "source", "big.bin", "mode", "0644", "sha256", Ops.ConsoleOps.Sha(data)));
                        var first = h.Next(); T.Eq("progress", first["event"] as string, "a progress event first");
                        h.Send("cancel");
                        var events = new List<Dictionary<string, object>>();
                        Dictionary<string, object> acc = null, term = null;
                        while (term == null) { var e = h.Next(); if (e["id"] as string != id) acc = e; else if (e["event"] as string == "progress") events.Add(e); else term = e; }
                        T.True(acc != null && (bool)Harness.Res(acc)["accepted"], "cancel accepted");
                        T.Eq("cancelled", Harness.Err(term), "the put ended as cancelled");
                        T.True(sh.Inputs.Last().Length < data.Length && sh.Inputs.Last().Length % 65536 == 0, "stopped at a 64 KB boundary (" + sh.Inputs.Last().Length + " bytes sent)");
                        T.True(((string)term["text"]).Contains("partial file stays at /var/lib/ccs/x"), "says where the partial file is");
                        T.Eq("result", h.Call("capabilities")["event"] as string, "serving again");
                    }
                }
                finally { Directory.Delete(pkg, true); }
            });

            T.Add("protocol", "requests are answered one at a time, in order", () =>
            {
                var sh = new ScriptedShell(); sh.Answer = (c, i) => { Thread.Sleep(150); return Tuple.Create(0, "dir\n", ""); };
                using (var h = new Harness(sh))
                {
                    var a = h.Send("stat", A("path", "/var/lib/ccs")); var b = h.Send("stat", A("path", "/tmp/ccs"));
                    var e1 = h.Next(); var e2 = h.Next();
                    T.Eq(a, e1["id"] as string, "first reply"); T.Eq(b, e2["id"] as string, "second reply");
                }
            });

            T.Add("protocol", "a timeout answers timeout and leaves the connection unusable; a lost connection answers connection-lost", () =>
            {
                var sh = new ThrowingShell { Next = new ShellTimeoutException("silent") };
                using (var h = new Harness(sh))
                {
                    T.Eq("timeout", Harness.Err(h.Call("stat", A("path", "/var/lib/ccs"))), "timeout");
                    T.Eq("connection-lost", Harness.Err(h.Call("stat", A("path", "/var/lib/ccs"))), "unusable afterwards");
                }
                sh = new ThrowingShell { Next = new ShellLostException("gone") };
                using (var h = new Harness(sh))
                {
                    T.Eq("connection-lost", Harness.Err(h.Call("list", A("path", "/var/lib/ccs"))), "lost");
                    T.Eq("connection-lost", Harness.Err(h.Call("list", A("path", "/var/lib/ccs"))), "still lost");
                }
            });

            T.Add("protocol", "additions inside protocol 1: features listed; read with offset/length, sync, pointer expect dispatched; bad shapes refused", () =>
            {
                var sh = new ScriptedShell();
                sh.Answer = (c, i) => c.Contains(" dd if=") ? Tuple.Create(0, "5000\n" + new string('z', 4096), "") : c.StartsWith("sync") ? Tuple.Create(0, "", "") : c.Contains("printf") ? Tuple.Create(0, "0123456789abcdef.00112233\n", "") : Tuple.Create(0, "{}\n", "");
                using (var h = new Harness(sh))
                {
                    var caps = Harness.Res(h.Call("capabilities"));
                    T.True(((object[])caps["features"]).SequenceEqual(new object[] { "read-chunks", "sync", "pointer-expect", "write-tier" }), "features");
                    var lim = (Dictionary<string, object>)caps["limits"]; T.Eq(262144, (int)lim["chunk_max"], "chunk max"); T.Eq(1000000, (int)lim["read_max_bytes"], "whole-file read limit unchanged");
                    var r = Harness.Res(h.Call("read", A("path", "/var/lib/ccs/data/f", "offset", 0, "length", 4096)));
                    T.Eq(4096, (int)r["size"], "piece size"); T.Eq(5000, (int)r["file_size"], "file size"); T.Eq(false, (bool)r["eof"], "not the end");
                    T.Eq("{}\n", Encoding.UTF8.GetString(Convert.FromBase64String((string)Harness.Res(h.Call("read", A("path", "/var/lib/ccs/x", "max_bytes", 100)))["b64"])), "a whole-file read as before");
                    T.Eq("bad-args", Harness.Err(h.Call("read", A("path", "/var/lib/ccs/data/f", "offset", "0", "length", 4096))), "offset as text");
                    T.Eq("bad-args", Harness.Err(h.Call("read", A("path", "/var/lib/ccs/data/f", "offset", 0))), "length missing");
                    T.Eq("bad-args", Harness.Err(h.Call("read", A("path", "/var/lib/ccs/data/f", "offset", 0, "length", 4096.5))), "length not whole");
                    T.Eq(true, (bool)Harness.Res(h.Call("sync", A("drop_caches", true)))["dropped"], "sync with the cache dropped");
                    T.Eq("sync || exit 5; echo 1 > /proc/sys/vm/drop_caches || exit 6", sh.Commands.Last(), "drop_caches true: the worker's own text");
                    Harness.Res(h.Call("sync", A("drop_caches", "yes"))); T.Eq("sync || exit 5", sh.Commands.Last(), "only a JSON true drops the cache");
                    T.Eq("0123456789abcdef.00112233", Harness.Res(h.Call("pointer", A("path", "/var/lib/ccs/pkg/current", "value", "0123456789abcdef.00112233", "expect", "absent")))["value"] as string, "pointer with expect");
                    T.True(sh.Commands.Last().Contains("exit 8"), "the expectation reached the command");
                    T.Eq("bad-args", Harness.Err(h.Call("pointer", A("path", "/var/lib/ccs/pkg/current", "value", "0123456789abcdef.00112233", "expect", 5))), "expect not text");
                }
            });

            T.Add("protocol", "stdin EOF in the middle of an operation cancels it and the worker ends", () =>
            {
                var sh = new ScriptedShell(); sh.Answer = (c, i) => { Thread.Sleep(300); return Tuple.Create(0, "none\n", ""); };
                var h = new Harness(sh);
                h.Send("stat", A("path", "/var/lib/ccs")); h.CloseInput();
                T.True(h.Join(10000), "ended"); T.Eq(0, h.ExitCode, "exit code");
            });
        }

        sealed class ThrowingShell : IConsoleShell
        {
            public Exception Next;
            public string Kind => "throwing"; public bool IsOnline => true;
            public int Execute(string c, Stream i, Stream o, Stream e, int t) { var x = Next; if (x != null) throw x; return 0; }
            public void Disconnect() { } public void Dispose() { }
        }
    }
}
