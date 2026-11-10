// Classic Cart Studio console transport worker. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The worker protocol, version 1 (docs/console-install-architecture.md section 3; the same envelope as console_engine/worker.py):
// newline-delimited JSON on stdin/stdout. A request {"v":1,"id":…,"cmd":…,"args":{…}} gets zero or more progress events and then
// exactly one result or error. One request at a time; only "cancel" is read while an operation runs. stdout carries protocol lines
// only. The worker never takes a transport, host, port, device or shell text from its client: "connect" goes to RealTransports, which
// Gate G1 closes in code.
using CcsWorker.Ops;
using CcsWorker.Shell;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace CcsWorker.Protocol
{
    public sealed class WorkerServer
    {
        public const int ProtocolVersion = 1;
        public const int MaxLine = 1000000;
        public static readonly string[] Commands = { "capabilities", "connect", "identify", "df", "stat", "list", "sha256", "read", "put", "mkdir", "rmdir", "rename", "remove", "pointer",
            "module-install", "module-uninstall", "module-files", "sync", "cancel", "disconnect", "shutdown" };
        // additions inside protocol 1 (a client checks for them in capabilities): read with offset/length in pieces, sync (with the page
        // cache dropped for a readback from storage), pointer with the state it expects to replace
        public static readonly string[] Features = { "read-chunks", "sync", "pointer-expect", "write-tier" };
        // the commands that change the console: each asks the write tier, whatever the session was opened for ("write-tier" above), so an
        // inspect session never writes. sync is not one: it writes no file (it makes earlier writes persistent and, for a read-back, may
        // drop the clean page cache), and the verification after an installation, a read, asks for it
        public static readonly string[] WriteCommands = { "put", "mkdir", "rmdir", "rename", "remove", "pointer", "module-install", "module-uninstall" };
        public static readonly string[] Upstream = { "hakchi:25bfcfd04b8ed12f7d513dc87c2a0f7096d38ebc", "sshnet:32181aeb4ce69e8f07e5e1d585fd592a44017bc1", "libusbdotnet:2.2.29" };

        sealed class Line { public Dictionary<string, object> Req; public object Id; public string Error, Text; public bool Eof; }

        readonly Stream input; readonly Stream output; readonly string packageRoot;
        readonly Func<string, IConsoleShell> opener; readonly Func<bool> writesOpen;
        readonly object outLock = new object();
        readonly BlockingCollection<Line> queue = new BlockingCollection<Line>();
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
        IConsoleShell shell; string brokenWhy;
        volatile bool busy, cancelRequested;

        public WorkerServer(Stream input, Stream output, string packageRoot) : this(input, output, packageRoot, RealTransports.Open, null, () => Gate.PhysicalWriteEnabled) { }

        /// <summary>For the test project: its own opener (one that never reaches a console) and a shell already attached (a scripted,
        /// simulated or BusyBox shell in a disposable folder), whose writes the test allows or refuses (writesOpen).</summary>
        internal WorkerServer(Stream input, Stream output, string packageRoot, Func<string, IConsoleShell> opener, IConsoleShell attached, Func<bool> writesOpen)
        {
            this.input = input; this.output = output; this.packageRoot = packageRoot; this.opener = opener; shell = attached; this.writesOpen = writesOpen ?? (() => Gate.PhysicalWriteEnabled);
        }

        public int Run()
        {
            var reader = new Thread(ReadLoop) { IsBackground = true, Name = "protocol reader" };
            reader.Start();
            foreach (var line in queue.GetConsumingEnumerable())
            {
                if (line.Eof) break;
                if (Handle(line)) break;
            }
            try { shell?.Disconnect(); } catch { }
            shell = null;
            return 0;
        }

        // ---------------------------------------------------------------- reading (bounded; cancel is answered here at once)
        void ReadLoop()
        {
            var buf = new byte[65536]; var cur = new MemoryStream(); bool discarding = false;
            try
            {
                int n;
                while ((n = input.Read(buf, 0, buf.Length)) > 0)
                {
                    int start = 0;
                    for (int i = 0; i < n; i++)
                    {
                        if (buf[i] != (byte)'\n') continue;
                        Append(cur, buf, start, i - start, ref discarding); start = i + 1;
                        if (discarding) Enqueue(new Line { Error = "too-large", Text = "a request line may be at most 1,000,000 bytes" });
                        else if (cur.Length > 0) Parsed(Encoding.UTF8.GetString(cur.ToArray()).TrimEnd('\r'));
                        cur.SetLength(0); discarding = false;
                    }
                    Append(cur, buf, start, n - start, ref discarding);
                }
            }
            catch (Exception) { }
            if (busy) cancelRequested = true;   // end of input: the running operation is cancelled where it may be
            queue.Add(new Line { Eof = true });
        }

        static void Append(MemoryStream cur, byte[] b, int o, int c, ref bool discarding)
        {
            if (discarding || c <= 0) return;
            if (cur.Length + c > MaxLine) { discarding = true; cur.SetLength(0); return; }
            cur.Write(b, o, c);
        }

        void Parsed(string text)
        {
            if (text.Trim().Length == 0) return;
            Dictionary<string, object> req;
            try { req = json.DeserializeObject(text) as Dictionary<string, object>; }
            catch (Exception) { Enqueue(new Line { Error = "bad-json", Text = "the request is not JSON" }); return; }
            if (req == null) { Enqueue(new Line { Error = "bad-json", Text = "the request is not a JSON object" }); return; }
            req.TryGetValue("id", out var id);
            if (!(req.TryGetValue("v", out var v) && v is int iv && iv == ProtocolVersion)) { Enqueue(new Line { Id = id, Error = "bad-version", Text = "protocol version 1 required" }); return; }
            if (busy && req.TryGetValue("cmd", out var c) && c as string == "cancel")
            {
                cancelRequested = true;
                Send(new Dictionary<string, object> { ["id"] = id, ["event"] = "result", ["result"] = new Dictionary<string, object> { ["accepted"] = true } });
                return;
            }
            Enqueue(new Line { Req = req, Id = id });
        }

        void Enqueue(Line l) { try { queue.Add(l); } catch (InvalidOperationException) { } }

        // ---------------------------------------------------------------- writing
        void Send(Dictionary<string, object> ev)
        {
            var o = new Dictionary<string, object> { ["v"] = ProtocolVersion };
            foreach (var kv in ev) o[kv.Key] = kv.Value;
            var b = new UTF8Encoding(false).GetBytes(json.Serialize(o) + "\n");
            lock (outLock) { output.Write(b, 0, b.Length); output.Flush(); }
        }
        void Result(object id, Dictionary<string, object> r) => Send(new Dictionary<string, object> { ["id"] = id, ["event"] = "result", ["result"] = r });
        void Error(object id, string code, string text) => Send(new Dictionary<string, object> { ["id"] = id, ["event"] = "error", ["code"] = code, ["text"] = text });

        // ---------------------------------------------------------------- one request; true ends the worker
        bool Handle(Line line)
        {
            if (line.Error != null) { Error(line.Id, line.Error, line.Text); return false; }
            var req = line.Req; var id = line.Id;
            var cmd = req.TryGetValue("cmd", out var c) ? c as string : null;
            req.TryGetValue("args", out var ao);
            var a = ao == null ? new Dictionary<string, object>() : ao as Dictionary<string, object>;
            if (cmd == null || !Commands.Contains(cmd) || a == null) { Error(id, "bad-command", "unknown command or arguments"); return false; }
            cancelRequested = false; busy = true;
            try
            {
                switch (cmd)
                {
                    case "capabilities": Result(id, Capabilities()); return false;
                    case "connect":
                        var kind = a.TryGetValue("transport", out var t) ? t as string : null;
                        var s = opener(kind);   // Gate G1: RealTransports refuses before any device, socket or discovery
                        shell = s; brokenWhy = null; Result(id, new Dictionary<string, object> { ["transport"] = s.Kind }); return false;
                    case "cancel": Result(id, new Dictionary<string, object> { ["accepted"] = false, ["text"] = "no operation is running" }); return false;
                    case "disconnect": try { shell?.Disconnect(); } catch { } shell = null; brokenWhy = null; Result(id, new Dictionary<string, object>()); return false;
                    case "shutdown": Result(id, new Dictionary<string, object>()); return true;
                }
                if (WriteCommands.Contains(cmd) && !writesOpen()) throw new GateClosedException("write " + cmd);   // the write tier, for this command, before anything else
                if (shell == null) throw new OpException("not-connected", "no console is connected (" + Gate.Text + ")");
                if (brokenWhy != null || !shell.IsOnline) throw new OpException("connection-lost", brokenWhy ?? "the console connection ended");
                var ops = new ConsoleOps(shell, packageRoot, () => cancelRequested, (done, total) => Send(new Dictionary<string, object> { ["id"] = id, ["event"] = "progress", ["bytes"] = done, ["total"] = total }));
                Dictionary<string, object> r;
                switch (cmd)
                {
                    case "identify": r = ops.Identify(); break;
                    case "df": r = ops.Df(Str(a, "path")); break;
                    case "stat": r = ops.Stat(Str(a, "path")); break;
                    case "list": r = ops.List(Str(a, "path")); break;
                    case "sha256": r = ops.Sha256(a.TryGetValue("paths", out var ps) && ps is object[] arr ? arr.ToList() : (ps as System.Collections.ArrayList)?.Cast<object>().ToList()); break;
                    case "read":
                        if (a.ContainsKey("offset") || a.ContainsKey("length")) r = ops.ReadChunk(Str(a, "path"), Long(a, "offset"), Long(a, "length"));
                        else r = ops.Read(Str(a, "path"), a.TryGetValue("max_bytes", out var mb) && (mb is int || mb is long) ? Convert.ToInt64(mb) : -1);
                        break;
                    case "sync": r = ops.Sync(a.TryGetValue("drop_caches", out var dc) && dc is bool b && b); break;
                    case "put": r = ops.Put(a); break;
                    case "mkdir": r = ops.Mkdir(Str(a, "path")); break;
                    case "rmdir": r = ops.Rmdir(Str(a, "path")); break;
                    case "rename": r = ops.Rename(Str(a, "from"), Str(a, "to")); break;
                    case "remove": r = ops.Remove(Str(a, "path"), Str(a, "sha256")); break;
                    case "pointer":
                        if (a.ContainsKey("expect") && !(a["expect"] is string)) throw new OpException("bad-args", "expect: \"absent\" or a SHA-256");
                        r = ops.Pointer(Str(a, "path"), Str(a, "value"), Str(a, "expect")); break;
                    case "module-install": r = ops.ModuleInstall(a); break;
                    case "module-uninstall": r = ops.ModuleUninstall(Str(a, "name")); break;
                    case "module-files": r = ops.ModuleFiles(Str(a, "name")); break;
                    default: throw new OpException("bad-command", "unknown command");
                }
                Result(id, r);
            }
            catch (GateClosedException ex) { Error(id, "physical-disabled", ex.Message); }
            catch (OpException ex) { Error(id, ex.Code, ex.Message); }
            catch (OperationCanceledException ex) { Error(id, "cancelled", ex.Message); }
            catch (ShellTimeoutException ex) { brokenWhy = "an earlier command timed out: " + ex.Message; Error(id, "timeout", ex.Message); }
            catch (ShellLostException ex) { brokenWhy = ex.Message; Error(id, "connection-lost", ex.Message); }
            catch (Exception ex) { Error(id, "worker-error", ex.GetType().Name + ": " + ex.Message); }
            finally { busy = false; cancelRequested = false; }
            return false;
        }

        static string Str(Dictionary<string, object> a, string k) => a.TryGetValue(k, out var v) ? v as string : null;
        static long Long(Dictionary<string, object> a, string k) => a.TryGetValue(k, out var v) && (v is int || v is long) ? Convert.ToInt64(v) : throw new OpException("bad-args", k + ": a whole number");

        Dictionary<string, object> Capabilities()
        {
            string sha = null, ver = null;
            try
            {
                var exe = Process.GetCurrentProcess().MainModule.FileName; sha = ConsoleOps.Sha(File.ReadAllBytes(exe));
                ver = typeof(WorkerServer).Assembly.GetName().Version.ToString();
            }
            catch { }
            return new Dictionary<string, object>
            {
                ["protocol"] = ProtocolVersion, ["worker"] = "ccs-console-worker", ["version"] = ver, ["sha256"] = sha,
                ["transports"] = new List<object> { "clovershell", "ssh" },
                ["tiers"] = new Dictionary<string, object> { ["inspect"] = Gate.PhysicalInspectEnabled, ["install"] = Gate.PhysicalWriteEnabled },
                ["ops"] = Commands.Cast<object>().ToList(), ["features"] = Features.Cast<object>().ToList(),
                ["limits"] = new Dictionary<string, object> { ["read_max_bytes"] = 1000000, ["chunk_unit"] = ConsoleOps.ChunkUnit, ["chunk_max"] = ConsoleOps.ChunkMax },
                ["upstream"] = Upstream.ToDictionary(u => u.Split(':')[0], u => (object)u.Split(':')[1]),
                ["package"] = packageRoot != null && Directory.Exists(packageRoot),
            };
        }
    }
}
