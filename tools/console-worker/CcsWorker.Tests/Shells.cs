// Classic Cart Studio console transport worker tests. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Test shells: ScriptedShell (records each command and its input; answers from a script), FakeSshSession (an ISshSession), and
// BusyboxShell (the console's own stock BusyBox 1.24.1 and hakchi's module functions under qemu-arm in WSL, in a disposable folder:
// console paths in a command are moved into the folder and moved back in what it prints). None of them can reach a device.
using CcsWorker.Shell;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CcsWorker.Tests
{
    sealed class ScriptedShell : IConsoleShell
    {
        public readonly List<string> Commands = new List<string>();
        public readonly List<byte[]> Inputs = new List<byte[]>();
        public Func<string, byte[], Tuple<int, string, string>> Answer = (c, i) => Tuple.Create(0, "", "");
        public int ReadDelayMs;
        public string Kind => "scripted";
        public bool IsOnline => true;
        public int Execute(string command, Stream stdin, Stream stdout, Stream stderr, int timeoutMs)
        {
            Commands.Add(command);
            var ms = new MemoryStream();
            if (stdin != null) { var b = new byte[65536]; int n; while ((n = stdin.Read(b, 0, b.Length)) > 0) { ms.Write(b, 0, n); if (ReadDelayMs > 0) Thread.Sleep(ReadDelayMs); } }
            Inputs.Add(ms.ToArray());
            var r = Answer(command, ms.ToArray());
            var o = Encoding.UTF8.GetBytes(r.Item2); stdout?.Write(o, 0, o.Length);
            var e = Encoding.UTF8.GetBytes(r.Item3); stderr?.Write(e, 0, e.Length);
            return r.Item1;
        }
        public void Disconnect() { }
        public void Dispose() { }
    }

    sealed class FakeSshSession : ISshSession
    {
        public bool Connected = true;
        public Func<string, byte[], Tuple<int, string, string>> Answer = (c, i) => Tuple.Create(0, "", "");
        public Exception Throw;
        public byte[] LastInput;
        public bool IsConnected => Connected;
        public int Run(string command, Stream stdin, Stream stdout, Stream stderr, int timeoutMs)
        {
            if (Throw != null) { var t = Throw; Throw = null; throw t; }
            var ms = new MemoryStream(); stdin?.CopyTo(ms); LastInput = ms.ToArray();
            var r = Answer(command, LastInput);
            var o = Encoding.UTF8.GetBytes(r.Item2); stdout?.Write(o, 0, o.Length);
            var e = Encoding.UTF8.GetBytes(r.Item3); stderr?.Write(e, 0, e.Length);
            return r.Item1;
        }
        public void Disconnect() { Connected = false; }
        public void Dispose() { Connected = false; }
    }

    sealed class BusyboxShell : IConsoleShell
    {
        static readonly Regex ConsolePath = new Regex("'(/var/lib|/tmp/ccs|/etc/init\\.d/S81clover-mcp|/var/version)");
        readonly string scripts, work; int n; volatile bool disposed;
        public readonly string Sim;
        public readonly List<string> Commands = new List<string>();
        public string Kind => "busybox-fidelity";
        public bool IsOnline => !disposed;

        public static string Wsl(string win) => "/mnt/" + char.ToLowerInvariant(win[0]) + win.Substring(2).Replace('\\', '/');

        BusyboxShell(string scripts, string work, string sim) { this.scripts = scripts; this.work = work; Sim = sim; }

        /// <summary>A fresh disposable folder in WSL, or SkipException when WSL, qemu-arm or the extracted console files are not here.</summary>
        public static BusyboxShell Create()
        {
            var scripts = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fidelity");
            var work = Path.Combine(@"C:\NES\work\cs\console-worker\fidelity-work", Guid.NewGuid().ToString("N").Substring(0, 8));
            if (!File.Exists(Path.Combine(scripts, "setup.sh"))) throw new SkipException("fidelity scripts are missing");
            var r = Wsl(new[] { "bash", Wsl(Path.Combine(scripts, "setup.sh")) }, null, 300000);
            var sim = r.Item2.Trim().Split('\n').LastOrDefault()?.Trim();
            if (r.Item1 != 0 || sim == null || !sim.StartsWith("/tmp/ccsfid-")) throw new SkipException("WSL/qemu-arm/console files unavailable (" + (r.Item1 == 3 ? "unavailable" : r.Item3.Trim()) + ")");
            Directory.CreateDirectory(work);
            return new BusyboxShell(scripts, work, sim);
        }

        static Tuple<int, string, string> Wsl(string[] args, Stream stdin, int timeoutMs, Stream stdout = null)
        {
            var psi = new ProcessStartInfo("wsl.exe") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            psi.Arguments = "-d Ubuntu -- " + string.Join(" ", args.Select(a => a.Contains(" ") ? "\"" + a + "\"" : a));
            using (var p = Process.Start(psi))
            {
                var outBuf = new MemoryStream(); var errBuf = new MemoryStream();
                var to = new Thread(() => p.StandardOutput.BaseStream.CopyTo(outBuf)); var te = new Thread(() => p.StandardError.BaseStream.CopyTo(errBuf));
                to.Start(); te.Start();
                try { if (stdin != null) stdin.CopyTo(p.StandardInput.BaseStream); p.StandardInput.Close(); } catch (IOException) { }
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } throw new ShellTimeoutException("the fidelity command did not finish"); }
                to.Join(); te.Join();
                var err = Encoding.UTF8.GetString(errBuf.ToArray()).Replace("\0", "");
                err = string.Join("\n", err.Split('\n').Where(l => !l.Contains("wsl:") && !l.Contains(".wslconfig")));
                stdout?.Write(outBuf.ToArray(), 0, (int)outBuf.Length);
                return Tuple.Create(p.ExitCode, Encoding.UTF8.GetString(outBuf.ToArray()), err);
            }
        }

        public int Execute(string command, Stream stdin, Stream stdout, Stream stderr, int timeoutMs)
        {
            if (disposed) throw new ShellLostException("the fidelity folder is gone");
            Commands.Add(command);
            var text = ConsolePath.Replace(command, m => "'" + Sim + m.Groups[1].Value);
            var file = Path.Combine(work, "cmd-" + (++n) + ".sh");
            File.WriteAllText(file, text + "\n", new UTF8Encoding(false));
            var raw = new MemoryStream();
            var r = Wsl(new[] { "bash", Wsl(Path.Combine(scripts, "run.sh")), Sim, Wsl(file) }, stdin, Math.Max(timeoutMs, 120000), raw);
            var o = Back(raw.ToArray());
            stdout?.Write(o, 0, o.Length);
            var e = Encoding.UTF8.GetBytes(r.Item3.Replace(Sim, "")); stderr?.Write(e, 0, e.Length);
            return r.Item1;
        }

        byte[] Back(byte[] b)   // the folder's prefix out of what the command printed (the console's own paths again)
        {
            var key = Encoding.ASCII.GetBytes(Sim); var o = new MemoryStream();
            for (int i = 0; i < b.Length; i++)
            {
                if (i + key.Length <= b.Length && b[i] == key[0] && key.Select((k, j) => b[i + j] == k).All(x => x)) { i += key.Length - 1; continue; }
                o.WriteByte(b[i]);
            }
            return o.ToArray();
        }

        /// <summary>A file inside the folder, by console path (test checks).</summary>
        public Tuple<int, string, string> Raw(string shellText) { var file = Path.Combine(work, "raw-" + (++n) + ".sh"); File.WriteAllText(file, shellText.Replace("$SIM", Sim) + "\n", new UTF8Encoding(false)); return Wsl(new[] { "bash", Wsl(Path.Combine(scripts, "run.sh")), Sim, Wsl(file) }, null, 120000); }

        public void Disconnect() { }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            Wsl(new[] { "bash", Wsl(Path.Combine(scripts, "teardown.sh")), Sim }, null, 60000);
            try { Directory.Delete(work, true); } catch { }
        }
    }
}
