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
        public static readonly string[] ReadRoots = { "/var/lib/ccs", "/tmp/ccs", "/var/lib/hakchi/hmod" };
        public static readonly string[] ReadExact = { "/var/lib/hakchi/rootfs/etc/init.d/S81clover-mcp", "/etc/init.d/S81clover-mcp", "/var/version", "/var/lib" };
        public const string ModuleName = "ccs-home";
        public const string ModuleFile = "/etc/init.d/S81clover-mcp";
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
