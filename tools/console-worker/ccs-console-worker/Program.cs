// Classic Cart Studio console transport worker. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
//   ccs-console-worker [--package FOLDER]      the protocol on stdin/stdout (CcsWorker.Protocol.WorkerServer); FOLDER: the console
//                                              package the backend staged (put and module-install read sources only inside it)
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
    }
}
