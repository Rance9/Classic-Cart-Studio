// Classic Cart Studio console transport worker. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Every console path is checked here before any shell text is made from it: absolute, plain characters only (so it can always be
// single-quoted), no empty, "." or ".." part, and inside the places this program may read or write.
using System;
using System.Linq;

namespace CcsWorker.Ops
{
    public sealed class OpException : Exception
    {
        public readonly string Code;
        public OpException(string code, string text) : base(text) { Code = code; }
    }

    public static class ConsolePath
    {
        public static readonly string[] WriteRoots = { "/var/lib/ccs", "/tmp/ccs" };
        // the install method's files in hakchi's overlay are read (CCS Mod / CCS Nucleus: their program, records and start files), never written
        public static readonly string[] ReadRoots = { "/var/lib/ccs", "/tmp/ccs", "/var/lib/hakchi/hmod", "/var/lib/hakchi/rootfs/etc/ccs" };
        public static readonly string[] ReadExact = { "/var/lib/hakchi/rootfs/etc/init.d/S81clover-mcp", "/etc/init.d/S81clover-mcp", "/var/version", "/var/lib",
            "/var/lib/hakchi/rootfs/bin/ccs-mod", "/var/lib/hakchi/rootfs/bin/ccs-nucleus", "/var/lib/hakchi/rootfs/etc/preinit.d/p7060_ccsnucleus",
            "/var/lib/hakchi/rootfs/etc/preinit.d/p7071_ccsnucleus", "/var/lib/hakchi/rootfs/etc/init.d/S81ccs-nucleus", "/var/lib/hakchi/rootfs/etc/init.d/S81ccs-mod" };
        /// <summary>the earlier start-up module (read and removed only: hakchi deletes its one file at every start)</summary>
        public const string ModuleName = "ccs-home";
        public const string ModuleFile = "/etc/init.d/S81clover-mcp";
        /// <summary>the CCS Mod and CCS Nucleus packages this worker installs: only these exact builds (tools/console-nucleus/build_packages.py,
        /// the same bytes the CCS Console Workbench's preparation image carries: tools/console-backup/ram/build_images.py METHOD_SHAS; a test
        /// keeps the two lists equal)</summary>
        public static readonly System.Collections.Generic.Dictionary<string, string> MethodPackages = new System.Collections.Generic.Dictionary<string, string>
        {
            { "ccs-mod", "0274a29108416151a7673aac147ae4dc870f725dea66cd1dd6127fcdc9fda662" },
            { "ccs-nucleus", "88bb6c841ee6547c954c55d2bb9b266b6482a47c9cff60cbddd410ea33da1e2e" },
        };
        public const string HakchiRootfs = "/var/lib/hakchi/rootfs";
        public const string UninstallDir = "/var/lib/hakchi/hmod";

        static bool Under(string p, string root) => p == root || p.StartsWith(root + "/", StringComparison.Ordinal);

        public static bool Plain(string p)
        {
            if (string.IsNullOrEmpty(p) || p.Length > 512 || p[0] != '/' || p.EndsWith("/")) return false;
            if (p.Any(ch => !(ch >= 'a' && ch <= 'z' || ch >= 'A' && ch <= 'Z' || ch >= '0' && ch <= '9' || ch == '.' || ch == '_' || ch == '/' || ch == '-'))) return false;
            return p.Substring(1).Split('/').All(c => c.Length > 0 && c != "." && c != "..");
        }

        public static string Write(object o)
        {
            var p = o as string;
            if (!Plain(p) || !WriteRoots.Any(r => Under(p, r))) throw new OpException("path-refused", "not a place this program may write: " + Show(p));
            return p;
        }

        public static string Read(object o)
        {
            var p = o as string;
            if (!Plain(p) || !(ReadRoots.Any(r => Under(p, r)) || ReadExact.Contains(p))) throw new OpException("path-refused", "not a place this program may read: " + Show(p));
            return p;
        }

        public static string Parent(string p) { var i = p.LastIndexOf('/'); return i <= 0 ? "/" : p.Substring(0, i); }
        public static string RootOf(string p) => WriteRoots.FirstOrDefault(r => Under(p, r));
        public static string Q(string p) => "'" + p + "'";   // safe: Plain() admits no quote, space or shell character
        static string Show(string p) => p == null ? "(none)" : p.Length > 80 ? p.Substring(0, 80) + "..." : p;
    }
}
