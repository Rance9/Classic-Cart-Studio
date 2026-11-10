// Classic Cart Studio console transport worker tests. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
//   CcsWorker.Tests                                  runs every test (gate first), prints one line each and "RESULT passed/total"
//   CcsWorker.Tests --only CATEGORY[,CATEGORY]       gate, protocol, clovershell, ssh, ops, fidelity
//   CcsWorker.Tests --serve-busybox [--package DIR]  test only: the worker protocol on stdin/stdout with a BusyBox fidelity shell
//                                                    already attached (a disposable WSL folder, removed at the end); its "connect" goes
//                                                    to TestOpeners.Never, whatever Gate G1 says, so nothing here can reach a device
// Exit code: 0 all passed, 1 a failure, 2 all ran passed but some were skipped (a skip is never a pass).
using CcsWorker.Protocol;
using CcsWorker.Shell;
using System;
using System.Linq;

namespace CcsWorker.Tests
{
    static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--serve-busybox") return Serve(args);
            string[] only = null;
            for (int i = 0; i < args.Length; i++) if (args[i] == "--only" && i + 1 < args.Length) only = args[++i].Split(',');
            GateTests.Register(); ProtocolTests.Register(); ClovershellTests.Register(); SshTests.Register(); OpsTests.Register(); FidelityTests.Register();
            int pass = 0, fail = 0, skip = 0;
            var per = T.All.Select(t => t.Item1).Distinct().ToDictionary(c => c, c => new int[3]);
            foreach (var t in T.All)
            {
                if (only != null && !only.Contains(t.Item1)) continue;
                var t0 = DateTime.UtcNow;
                try { t.Item3(); pass++; per[t.Item1][0]++; Console.WriteLine("ok    [{0}] {1} ({2:0.0} s)", t.Item1, t.Item2, (DateTime.UtcNow - t0).TotalSeconds); }
                catch (SkipException e) { skip++; per[t.Item1][2]++; Console.WriteLine("SKIP  [{0}] {1}: {2}", t.Item1, t.Item2, e.Message); }
                catch (Exception e) { fail++; per[t.Item1][1]++; Console.WriteLine("FAIL  [{0}] {1}: {2}", t.Item1, t.Item2, e.Message); }
                Console.Out.Flush();
            }
            foreach (var kv in per) if (only == null || only.Contains(kv.Key)) Console.WriteLine("  {0,-12} {1} passed, {2} failed, {3} skipped", kv.Key, kv.Value[0], kv.Value[1], kv.Value[2]);
            Console.WriteLine("RESULT {0}/{1}{2}", pass, pass + fail + skip, skip > 0 ? " (" + skip + " skipped)" : "");
            return fail > 0 ? 1 : skip > 0 ? 2 : 0;
        }

        static int Serve(string[] args)
        {
            string package = null;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--package" && i + 1 < args.Length) { package = args[++i]; continue; }
                Console.Error.WriteLine("usage: CcsWorker.Tests --serve-busybox [--package DIR]"); return 2;
            }
            BusyboxShell sh;
            try { sh = BusyboxShell.Create(); } catch (SkipException e) { Console.Error.WriteLine("fidelity shell unavailable: " + e.Message); return 3; }
            try { return new WorkerServer(Console.OpenStandardInput(), Console.OpenStandardOutput(), package, TestOpeners.Never, sh, () => true).Run(); }
            finally { sh.Dispose(); }
        }
    }
}
