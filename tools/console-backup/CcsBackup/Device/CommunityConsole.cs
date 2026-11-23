// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Community Mods on the console: the steps of ram/ccscomm (preparation sessions only), as the engine asks for them. The engine never builds
// shell text from anything but these step names and arguments checked here against fixed patterns (a hakchi package name, a hex digest,
// a number, a setting's place); a package's bytes come only as the exact number of input bytes its SHA-256 names.
//   cstate NAME...       read-only: for each package, whether hakchi lists it (its uninstall list's SHA-256 and lines) and the settings
//                        Classic Cart Studio keeps for it (their list's SHA-256 and count)
//   cverify NAME         read-only: each file hakchi's uninstall list names for the package, with its SHA-256 now (or absent, link)
//   cput NAME TIER SIZE SHA BYTES   the package (exactly SIZE bytes from the PC, SHA-256 SHA) placed in hakchi's transfer folder as
//                        NAME.hmod (TIER 1: in its hmod/ folder, installed after the others), BYTES the unpacked size for the space check
//   cunlist NAME...      hakchi's removal list naming these packages in this order (never one of Classic Cart Studio's own)
//   ckeep NAME PATH...   the mod's settings and favorites copied to Classic Cart Studio's own folder before its own removal deletes them
//   cforget NAME         that kept copy removed (only Classic Cart Studio's own copy; never the mod's files)
//   crestore NAME        the kept copy put back where nothing is now (an existing file is never replaced)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsBackup.Device
{
    /// <summary>one package as ccscomm cstate reports it</summary>
    public sealed class CommunityPackageState
    {
        public string Package; public bool Listed; public string UninstallSha; public int UninstallLines; public int KeptCount; public string KeptSha;
        public bool Kept => KeptSha != null;
        public string Line => "cpkg=" + Package + " " + (Listed ? "listed " + UninstallSha + " " + UninstallLines : "absent") + " kept=" + (Kept ? KeptCount + " " + KeptSha : "none");
    }

    public sealed partial class RecoveryConsole
    {
        public const string CommunityHelper = "/ccs/bin/ccscomm";
        public static readonly string[] CommunityWriteSteps = { "cput", "cunlist", "ckeep", "cforget", "crestore" };
        static bool PackageArg(string s) => Engine.CommunityCatalog.PackageOk(s);
        static bool SettingArg(string s) => s != null && Engine.CommunityCatalog.SettingRe.IsMatch(s) && !s.Split('/').Any(p => p == ".." || p == ".");

        string CommText(string args, out int code)
        {
            if (Profile != SessionProfile.Prepare) throw new InvalidOperationException("Only a preparation session reads or changes community mods.");
            var o = new MemoryStream(); var e = new MemoryStream();
            code = shell.Execute(CommunityHelper + " " + args, null, o, e, ReadSilenceMs);
            if (o.Length > 4000000) throw new IOException("the console's answer was unexpectedly large");
            return Encoding.UTF8.GetString(o.ToArray());
        }

        /// <summary>ccscomm cstate (read-only): each named package's state; null when the console did not answer completely</summary>
        public Dictionary<string, CommunityPackageState> CommunityState(IEnumerable<string> packages)
        {
            var names = packages.Distinct(StringComparer.Ordinal).ToList();
            if (names.Count == 0) return new Dictionary<string, CommunityPackageState>(StringComparer.Ordinal);
            if (names.Count > 32 || !names.All(PackageArg)) throw new ArgumentException("bad community state request");
            var t = CommText("cstate " + string.Join(" ", names), out var code);
            var lines = t.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (code != 0 || lines.LastOrDefault() != "result cstate ok") return null;
            var r = new Dictionary<string, CommunityPackageState>(StringComparer.Ordinal);
            foreach (var l in lines)
            {
                var m = Regex.Match(l, "^cpkg=([A-Za-z0-9._+-]{1,64}) (?:(listed) ([0-9a-f]{64}|none) ([0-9]{1,7})|absent) kept=(?:([0-9]{1,4}) ([0-9a-f]{64})|none)$");
                if (!m.Success) continue;
                var n = m.Groups[1].Value; if (!names.Contains(n)) return null;
                r[n] = new CommunityPackageState { Package = n, Listed = m.Groups[2].Success, UninstallSha = m.Groups[3].Success && m.Groups[3].Value != "none" ? m.Groups[3].Value : null,
                    UninstallLines = m.Groups[4].Success ? int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) : 0, KeptCount = m.Groups[5].Success ? int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture) : 0, KeptSha = m.Groups[6].Success ? m.Groups[6].Value : null };
            }
            return names.All(r.ContainsKey) ? r : null;
        }

        /// <summary>ccscomm cverify (read-only): the files hakchi's uninstall list names for an installed package, each with its SHA-256 now or
        /// "absent", "link" or "other"; null when the package is not installed or the answer was incomplete</summary>
        public List<(string state, string path)> CommunityVerify(string package)
        {
            if (!PackageArg(package)) throw new ArgumentException("bad community check request");
            var t = CommText("cverify " + package, out var code);
            var lines = t.Replace("\r", "").Split('\n').Where(l => l.Length > 0).ToList();
            var m = Regex.Match(lines.LastOrDefault() ?? "", "^result cverify ok ([0-9]{1,6})$");
            if (code != 0 || !m.Success) return null;
            var r = new List<(string, string)>();
            foreach (var l in lines)
            {
                var f = Regex.Match(l, "^cfile=([0-9a-f]{64}|absent|link|other) (.+)$");
                if (f.Success) r.Add((f.Groups[1].Value, f.Groups[2].Value));
            }
            return r.Count == int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) ? r : null;
        }

        /// <summary>one ccscomm write step, admitted by the authority for this console. cput takes exactly size bytes of input (the package);
        /// the other steps take none.</summary>
        public WriteResult CommunityStep(string step, WriteAuthority authority, string[] args, Stream input = null, long size = 0)
        {
            if (Profile != SessionProfile.Prepare) throw new InvalidOperationException("Only a preparation session changes community mods.");
            if (!CommunityWriteSteps.Contains(step) || args == null) throw new ArgumentException("not a community step: " + step);
            bool ok;
            switch (step)
            {
                case "cput": ok = args.Length == 5 && PackageArg(args[0]) && (args[1] == "0" || args[1] == "1") && Regex.IsMatch(args[2], "^[1-9][0-9]{0,8}$") && Hex.IsHex(args[3], 64) && Regex.IsMatch(args[4], "^[1-9][0-9]{0,9}$")
                                  && input != null && args[2] == size.ToString(CultureInfo.InvariantCulture); break;
                case "cunlist": ok = args.Length >= 1 && args.Length <= 8 && args.All(PackageArg) && args.Distinct().Count() == args.Length && input == null; break;
                case "ckeep": ok = args.Length >= 2 && args.Length <= 17 && PackageArg(args[0]) && args.Skip(1).All(SettingArg) && input == null; break;
                default: ok = args.Length == 1 && PackageArg(args[0]) && input == null; break;
            }
            if (!ok) throw new ArgumentException("bad community step request");
            if (authority == null) throw new InvalidOperationException("No community change was admitted.");
            authority.Require(step, Hwid);
            var wr = new WriteResult { Target = step }; var o = new MemoryStream(); var e = new MemoryStream(); int code;
            try { code = shell.Execute(CommunityHelper + " " + step + " " + string.Join(" ", args), input != null ? new LimitedStream(input, size) : null, o, e, WriteSilenceMs); }
            catch (Exception ex) { authority.Revoke(step + ": " + ex.Message); wr.Began = true; wr.Problem = "the connection ended during " + step + ": " + ex.Message; return wr; }
            var lines = Encoding.UTF8.GetString(o.ToArray()).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            wr.Began = lines.Any(l => l == "began " + step);
            wr.Line = lines.LastOrDefault(l => l.StartsWith("result " + step + " "));
            wr.Ok = code == 0 && wr.Line != null && Regex.IsMatch(wr.Line, "^result " + step + " ok( |$)");
            if (!wr.Ok) { wr.Problem = wr.Line != null ? wr.Line.Substring(("result " + step + " ").Length) : "no result from the console (status " + code + ")"; authority.Revoke(step + ": " + wr.Problem); }
            return wr;
        }
    }
}
