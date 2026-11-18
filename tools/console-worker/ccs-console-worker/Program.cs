// Classic Cart Studio console transport worker. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
//   ccs-console-worker [--package FOLDER]      the protocol on stdin/stdout (CcsWorker.Protocol.WorkerServer); FOLDER: the console
//                                              package the backend staged (put and module-install read sources only inside it)
//   ccs-console-worker --admit-backup ...      the backup prerequisite for one maintenance operation (AdmitBackup below)
// No other option exists: never a transport, host, port or device. Diagnostics go to stderr.
using CcsWorker.Protocol;
using System;
using System.IO;

namespace CcsWorker.App
{
    static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--admit-backup") return AdmitBackup(args);
            string package = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--package" && i + 1 < args.Length && package == null) { package = args[++i]; continue; }
                Console.Error.WriteLine("usage: ccs-console-worker [--package FOLDER]");
                return 2;
            }
            if (package != null && !Directory.Exists(package)) { Console.Error.WriteLine("ccs-console-worker: the package folder does not exist"); return 2; }
            return new WorkerServer(Console.OpenStandardInput(), Console.OpenStandardOutput(), package).Run();
        }

        // --admit-backup --hwid H --operation OP --subject S [--settings FILE]: the backend's backup prerequisite before it starts a maintenance
        // run (console_home.Dispatcher). Prints one JSON object (CcsBackup.Engine.Admission.Admit: this console's backups verified from
        // scratch); exit 0 admitted, 1 refused, 2 usage. The run's own worker checks the admission again before its first write ("admit").
        static int AdmitBackup(string[] a)
        {
            string Arg(string k) { int i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
            var hwid = Arg("--hwid"); var op = Arg("--operation");
            if (string.IsNullOrEmpty(hwid) || string.IsNullOrEmpty(op)) { Console.Error.WriteLine("usage: ccs-console-worker --admit-backup --hwid H --operation OP --subject S [--settings FILE]"); return 2; }
            var r = CcsBackup.Engine.Admission.Admit(CcsBackup.Library.Settings.Load(Arg("--settings")), hwid, op, Arg("--subject") ?? "");
            Console.Out.Write(CcsBackup.Json.Write(r.ToJson())); Console.Out.Flush();
            return r.Ok ? 0 : 1;
        }
    }
}
