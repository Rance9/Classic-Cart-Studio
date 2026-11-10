// Classic Cart Studio console transport worker tests. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Gate G1: every connect is refused before any device, socket or discovery; the real pipe and session are constructed nowhere but
// RealTransports, whose compiled code (the gate constants are false) contains no reference to them at all. These run first: the
// loaded-assembly check needs a process that has not yet reflected over the worker's types. The two tests that call the real opener do
// so only while its gate is closed: in a build whose gate is open (the activation rehearsal) that code is compiled out and they skip,
// and every other worker server here connects through TestOpeners.Never.
using CcsWorker.Protocol;
using CcsWorker.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace CcsWorker.Tests
{
    static class GateTests
    {
        static readonly string[] Real = { "LibUsbDotNet", "Renci.SshNet" };
        static bool Loaded(string name) => AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name.StartsWith(name, StringComparison.Ordinal));

        public static void Register()
        {
            T.Add("gate", "the gate constants are false (compile-time constants)", () =>
            {
                foreach (var f in new[] { "PhysicalInspectEnabled", "PhysicalWriteEnabled" })
                {
                    var fi = typeof(Gate).GetField(f, BindingFlags.Public | BindingFlags.Static);
                    T.True(fi != null && fi.IsLiteral, f + " is a const");
                    T.Eq(false, (bool)fi.GetRawConstantValue(), f);
                }
            });

            T.Add("gate", "connect is physical-disabled for every transport and argument shape (real opener)", () =>
            {
                if (Gate.PhysicalInspectEnabled) throw new SkipException("the gate is open in this build: a test never calls the real opener");
                using (var h = new Harness(opener: RealTransports.Open))
                {
                    var kinds = new object[] { "clovershell", "ssh", "fel", "usb", "rndis", "fake", "", null, 7, true, new object[0], new Dictionary<string, object>() };
                    foreach (var k in kinds)
                        foreach (var extra in new[] { null, new Dictionary<string, object> { ["host"] = "169.254.13.37", ["port"] = 22, ["force"] = true, ["gate"] = "open", ["device"] = "USB\\VID_1F3A&PID_EFE8" } })
                        {
                            var a = new Dictionary<string, object> { ["transport"] = k };
                            if (extra != null) foreach (var kv in extra) a[kv.Key] = kv.Value;
                            T.Eq("physical-disabled", Harness.Err(h.Call("connect", a)), "connect " + Harness.Json.Serialize(k));
                        }
                    T.Eq("physical-disabled", Harness.Err(h.Call("connect")), "connect without arguments");
                    T.Eq("not-connected", Harness.Err(h.Call("stat", new Dictionary<string, object> { ["path"] = "/var/lib/ccs" })), "nothing is connected afterwards");
                }
            });

            T.Add("gate", "no USB or SSH assembly was loaded by the refusals (nothing reached LibUsbDotNet or SSH.NET)", () =>
            {
                if (Gate.PhysicalInspectEnabled) throw new SkipException("the gate is open in this build: a test never calls the real opener");
                foreach (var n in Real) T.True(!Loaded(n), n + " is not loaded");
                var e = T.Throws<GateClosedException>(() => RealTransports.Open("clovershell"), "RealTransports.Open");
                T.True(e.Message.Contains("Gate G1"), "the refusal names the gate");
                foreach (var n in Real) T.True(!Loaded(n), n + " is still not loaded after a direct call");
            });

            T.Add("gate", "source: the real pipe and session are built only in RealTransports, the gate is its first statement", () =>
            {
                var src = Sources();
                T.True(src.Count > 8, "the worker's sources are embedded");
                foreach (var kv in src)
                {
                    var f = kv.Key; var t = Regex.Replace(kv.Value, @"//[^\n]*", "");
                    if (f != "src/Shell/RealTransports.cs") { T.True(!t.Contains("new LibUsbBulkPipe("), f + " builds no LibUsbBulkPipe"); T.True(!t.Contains("new SshNetSession("), f + " builds no SshNetSession"); }
                    if (f != "src/Shell/LibUsbBulkPipe.cs") T.True(!Regex.IsMatch(t, @"\bUsbDevice\b|\bUsbRegistry\b|OpenUsbDevice"), f + " uses no USB API");
                    if (f != "src/Shell/SshNetSession.cs") T.True(!Regex.IsMatch(t, @"new SshClient\(|\bRenci\.SshNet\b"), f + " uses no SSH.NET API");
                    T.True(!Regex.IsMatch(t, @"\bTcpClient\b|\bUdpClient\b|new Socket\(|\bDns\.|MulticastOption|TcpListener"), f + " opens no socket and queries no DNS");
                    T.True(!Regex.IsMatch(t, @"Environment\.GetEnvironmentVariable"), f + " reads no environment variable");
                }
                var rt = Regex.Replace(src["src/Shell/RealTransports.cs"], @"//[^\n]*", "");
                var body = rt.Substring(rt.IndexOf("Open(string kind)", StringComparison.Ordinal));
                body = body.Substring(body.IndexOf('{') + 1).TrimStart();
                T.True(body.StartsWith("if (!Gate.PhysicalInspectEnabled) throw new GateClosedException("), "the gate check (a connection is the inspect tier) is the first statement of Open");
                var ws = Regex.Replace(src["src/Protocol/WorkerServer.cs"], @"//[^\n]*", "");
                T.True(ws.Contains("if (WriteCommands.Contains(cmd) && !writesOpen()) throw new GateClosedException(") && ws.IndexOf("WriteCommands.Contains(cmd)", StringComparison.Ordinal) < ws.IndexOf("if (shell == null)", StringComparison.Ordinal),
                       "each write command asks the write tier before the session is used");
                T.True(ws.Contains("RealTransports.Open, null, () => Gate.PhysicalWriteEnabled)"), "the production server's write tier is the gate constant");
            });

            T.Add("gate", "a test's worker server never reaches a console: its connect goes to TestOpeners.Never, whatever the gate", () =>
            {
                using (var h = new Harness())
                    foreach (var k in new object[] { "clovershell", "ssh", null })
                    {
                        var e = h.Call("connect", new Dictionary<string, object> { ["transport"] = k });
                        T.Eq("physical-disabled", Harness.Err(e), "connect " + Harness.Json.Serialize(k));
                        T.True((e["text"] as string).Contains(TestOpeners.NeverText), "refused by the test opener, not the gate: " + e["text"]);
                    }
                foreach (var n in Real) T.True(!Loaded(n), n + " is not loaded");
            });

            T.Add("gate", "the write tier is asked per command: with writes closed every changing command is refused before the console, reads are not", () =>
            {
                var sh = new ScriptedShell(); sh.Answer = (c, i) => Tuple.Create(0, "", "");
                using (var h = new Harness(sh, writes: false))
                {
                    var hash = new string('a', 64);
                    var writes = new Dictionary<string, Dictionary<string, object>>
                    {
                        ["put"] = new Dictionary<string, object> { ["path"] = "/var/lib/ccs/x", ["mode"] = "0644", ["sha256"] = hash, ["b64"] = "" },
                        ["mkdir"] = new Dictionary<string, object> { ["path"] = "/var/lib/ccs/d" }, ["rmdir"] = new Dictionary<string, object> { ["path"] = "/var/lib/ccs/d" },
                        ["rename"] = new Dictionary<string, object> { ["from"] = "/var/lib/ccs/a", ["to"] = "/var/lib/ccs/b" },
                        ["remove"] = new Dictionary<string, object> { ["path"] = "/var/lib/ccs/x", ["sha256"] = hash },
                        ["pointer"] = new Dictionary<string, object> { ["path"] = "/var/lib/ccs/pkg/current", ["value"] = "v1" },
                        ["module-install"] = new Dictionary<string, object> { ["name"] = "ccs-home", ["files"] = new Dictionary<string, object>() },
                        ["module-uninstall"] = new Dictionary<string, object> { ["name"] = "ccs-home" },
                    };
                    T.True(writes.Keys.OrderBy(k => k).SequenceEqual(Protocol.WorkerServer.WriteCommands.OrderBy(k => k)), "every write command is tried");
                    foreach (var kv in writes) T.Eq("physical-disabled", Harness.Err(h.Call(kv.Key, kv.Value)), kv.Key + " with writes closed");
                    T.Eq(0, sh.Commands.Count, "no console command was run for a refused write: " + string.Join(" | ", sh.Commands));
                    var read = h.Call("stat", new Dictionary<string, object> { ["path"] = "/var/lib/ccs" });
                    T.True(Harness.Err(read) != "physical-disabled", "a read is not refused by the write tier: " + Harness.Json.Serialize(read));
                    T.True(sh.Commands.Count > 0, "the read reached the console");
                }
            });

            T.Add("gate", "the production worker server asks the write tier before anything else (closed: physical-disabled even with no console)", () =>
            {
                if (Gate.PhysicalWriteEnabled) throw new SkipException("the write tier is open in this build");
                using (var p = new WorkerProcess())
                {
                    p.Write("{\"v\":1,\"id\":\"w\",\"cmd\":\"mkdir\",\"args\":{\"path\":\"/var/lib/ccs/x\"}}\n{\"v\":1,\"id\":\"r\",\"cmd\":\"stat\",\"args\":{\"path\":\"/var/lib/ccs\"}}\n{\"v\":1,\"id\":\"end\",\"cmd\":\"shutdown\"}\n");
                    T.True(p.WaitExit(30000), "exits after shutdown");
                    var w = (Dictionary<string, object>)Harness.Json.DeserializeObject(p.Out[0]); T.Eq("physical-disabled", w["code"] as string, "mkdir: the write tier first");
                    var r = (Dictionary<string, object>)Harness.Json.DeserializeObject(p.Out[1]); T.Eq("not-connected", r["code"] as string, "stat: no console");
                }
            });

            T.Add("gate", "compiled code: no method refers to the real pipe, the SSH session, LibUsbDotNet or SSH.NET except those two classes", () =>
            {
                var asm = typeof(Gate).Assembly; var bad = new List<string>();
                var realTypes = new[] { "CcsWorker.Shell.LibUsbBulkPipe", "CcsWorker.Shell.SshNetSession" };
                foreach (var t in asm.GetTypes())
                {
                    if (realTypes.Contains(t.FullName)) continue;
                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Cast<MethodBase>()
                                  .Concat(t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)))
                    {
                        byte[] il; try { il = m.GetMethodBody()?.GetILAsByteArray(); } catch { continue; }
                        if (il == null) continue;
                        for (int i = 0; i + 4 < il.Length; i++)
                        {
                            if (il[i] != 0x28 && il[i] != 0x6F && il[i] != 0x73 && il[i] != 0xD0 && il[i] != 0x7B && il[i] != 0x7E) continue;
                            int tok = BitConverter.ToInt32(il, i + 1); int kind = tok >> 24;
                            if (kind != 0x06 && kind != 0x0A && kind != 0x04 && kind != 0x01 && kind != 0x02) continue;
                            MemberInfo r; try { r = t.Module.ResolveMember(tok, t.IsGenericType ? t.GetGenericArguments() : null, m.IsGenericMethod ? m.GetGenericArguments() : null); } catch { continue; }
                            var dt = r as Type ?? r.DeclaringType; if (dt == null) continue;
                            var an = dt.Assembly.GetName().Name;
                            if (realTypes.Contains(dt.FullName) || an.StartsWith("LibUsbDotNet") || an == "Renci.SshNet") bad.Add(t.Name + "." + m.Name + " -> " + dt.FullName);
                        }
                    }
                }
                T.True(bad.Count == 0, "references: " + string.Join("; ", bad.Distinct()));
            });

            T.Add("gate", "the worker executable takes no transport, host, port or device option", () =>
            {
                foreach (var a in new[] { "--transport ssh", "--host 169.254.13.37", "--device x", "--gate open", "--package C:\\nonexistent-ccs-folder", "--package" })
                    using (var p = new WorkerProcess(a))
                    {
                        T.True(p.WaitExit(20000), "exits: " + a);
                        T.Eq(2, p.P.ExitCode, "exit code for " + a);
                        T.Eq(0, p.Out.Count, "no protocol output for " + a);
                    }
            });
        }

        public static Dictionary<string, string> Sources()
        {
            var asm = typeof(GateTests).Assembly; var d = new Dictionary<string, string>();
            foreach (var n in asm.GetManifestResourceNames().Where(n => n.StartsWith("src/") && n.EndsWith(".cs")))
                using (var r = new StreamReader(asm.GetManifestResourceStream(n))) d[n.Replace('\\', '/')] = r.ReadToEnd();
            return d;
        }
    }
}
