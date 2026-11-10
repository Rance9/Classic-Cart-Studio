// Classic Cart Studio console transport worker tests. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The protocol server in this process over anonymous pipes (stdin/stdout as the backend sees them), or the built worker as a process.
using CcsWorker.Protocol;
using CcsWorker.Shell;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace CcsWorker.Tests
{
    sealed class Harness : IDisposable
    {
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        readonly AnonymousPipeServerStream inW, outR; readonly AnonymousPipeClientStream inR, outW;
        readonly Thread server, reader;
        public readonly BlockingCollection<string> Lines = new BlockingCollection<string>();
        public int ExitCode = -1;
        int seq;

        /// <summary>opener: what "connect" reaches; by default TestOpeners.Never (a test never reaches a console; the closed-gate tests pass
        /// RealTransports.Open themselves, only while its gate is closed). writes: whether the attached test shell may be changed (the
        /// production server asks Gate.PhysicalWriteEnabled instead).</summary>
        public Harness(IConsoleShell attached = null, string package = null, Func<string, IConsoleShell> opener = null, bool writes = true)
        {
            inW = new AnonymousPipeServerStream(PipeDirection.Out); inR = new AnonymousPipeClientStream(PipeDirection.In, inW.ClientSafePipeHandle);
            outR = new AnonymousPipeServerStream(PipeDirection.In); outW = new AnonymousPipeClientStream(PipeDirection.Out, outR.ClientSafePipeHandle);
            var s = new WorkerServer(inR, outW, package, opener ?? TestOpeners.Never, attached, () => writes);
            server = new Thread(() => { ExitCode = s.Run(); try { outW.Dispose(); } catch { } }) { IsBackground = true }; server.Start();
            reader = new Thread(() => { var r = new StreamReader(outR, new UTF8Encoding(false)); string l; while ((l = r.ReadLine()) != null) Lines.Add(l); Lines.CompleteAdding(); }) { IsBackground = true };
            reader.Start();
        }

        public void SendRaw(string line) { var b = Encoding.UTF8.GetBytes(line + "\n"); inW.Write(b, 0, b.Length); inW.Flush(); }
        public void SendBytes(byte[] b) { inW.Write(b, 0, b.Length); inW.Flush(); }
        public string Send(string cmd, object args = null)
        {
            var id = "r" + (++seq);
            SendRaw(Json.Serialize(new Dictionary<string, object> { ["v"] = 1, ["id"] = id, ["cmd"] = cmd, ["args"] = args ?? new Dictionary<string, object>() }));
            return id;
        }
        public Dictionary<string, object> Next(int timeoutMs = 30000)
        {
            if (!Lines.TryTake(out var l, timeoutMs)) throw new Exception("no reply within " + timeoutMs + " ms");
            return (Dictionary<string, object>)Json.DeserializeObject(l);
        }
        /// <summary>Progress events of id, then its one terminal event.</summary>
        public Dictionary<string, object> Terminal(string id, List<Dictionary<string, object>> progress = null, int timeoutMs = 60000)
        {
            while (true)
            {
                var e = Next(timeoutMs);
                if ((e["id"] as string) != id) throw new Exception("a reply for another request: " + Json.Serialize(e));
                var ev = e["event"] as string;
                if (ev == "progress") { progress?.Add(e); continue; }
                return e;
            }
        }
        public Dictionary<string, object> Call(string cmd, object args = null, int timeoutMs = 60000) => Terminal(Send(cmd, args), null, timeoutMs);
        public static Dictionary<string, object> Res(Dictionary<string, object> e) { if ((e["event"] as string) != "result") throw new Exception("expected a result: " + Json.Serialize(e)); return (Dictionary<string, object>)e["result"]; }
        public static string Err(Dictionary<string, object> e) => (e["event"] as string) == "error" ? e["code"] as string : "(result) " + Json.Serialize(e);
        public void CloseInput() { inW.Dispose(); }
        public bool Join(int ms) => server.Join(ms);
        public void Dispose() { try { inW.Dispose(); } catch { } server.Join(5000); }
    }

    sealed class WorkerProcess : IDisposable
    {
        public static string Exe => Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\ccs-console-worker\Release\net48\ccs-console-worker.exe"));
        public readonly Process P; public readonly List<string> Out = new List<string>(); public readonly StringBuilder Err = new StringBuilder();
        readonly Thread to, te;
        public WorkerProcess(params string[] args)
        {
            var psi = new ProcessStartInfo(Exe, string.Join(" ", args)) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            psi.StandardOutputEncoding = new UTF8Encoding(false);
            P = Process.Start(psi);
            to = new Thread(() => { string l; while ((l = P.StandardOutput.ReadLine()) != null) lock (Out) Out.Add(l); }) { IsBackground = true }; to.Start();
            te = new Thread(() => { string l; while ((l = P.StandardError.ReadLine()) != null) lock (Err) Err.AppendLine(l); }) { IsBackground = true }; te.Start();
        }
        public void Write(string s) { var b = new UTF8Encoding(false).GetBytes(s); P.StandardInput.BaseStream.Write(b, 0, b.Length); P.StandardInput.BaseStream.Flush(); }
        public bool WaitExit(int ms) { var ok = P.WaitForExit(ms); if (ok) { to.Join(2000); te.Join(2000); } return ok; }
        public void Dispose() { try { if (!P.HasExited) P.Kill(); } catch { } P.Dispose(); }
    }
}
