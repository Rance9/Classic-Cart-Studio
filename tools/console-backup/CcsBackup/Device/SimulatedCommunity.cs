// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// A simulated console's community mods (tests and the simulated mode): ram/ccscomm's steps with the same checks and answers, and, at the
// console's next normal start (NormalStart), hakchi's pack_install and pack_uninstall of a real .hmod. Folder layout beside the storage:
//   hakchi-transfer/          hakchi's transfer folder (shared with CCS Mod and CCS Nucleus; a community package is its real .hmod bytes)
//   hakchi-rootfs/            hakchi's root overlay (/var/lib/hakchi/rootfs): what an installed package's files became; a link is a file
//                             holding "ccs-simulated-link:" and its target
//   hakchi-uninstall/         hakchi's uninstall lists of community packages (/var/lib/hakchi/hmod/uninstall-NAME): the package's own
//                             uninstall script, then "# auto-generated" and one rm -f line per file, as pack_install writes them
//   clover-profiles/          the console's profiles (/var/lib/clover/profiles) for a mod's settings there
//   ccs-data/community/kept/  Classic Cart Studio's kept settings (NAME/<place> and NAME.list)
// At the next normal start, a package's own uninstall script runs only its rm -f / rm -rf lines under $rootfs (the simulation runs no other
// script). faults.json: "prep": {"step": "cput"|"cunlist"|"ckeep"|"cforget"|"crestore"} fails that step after it began;
// "community_not_installed": [NAME…] makes hakchi leave those packages uninstalled (as its blacklist does).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsBackup.Device
{
    public sealed partial class SimulatedConsole
    {
        public const string LinkMark = "ccs-simulated-link:";
        static partial void CommunityInstall(string folder, string name, string file, ref bool installs);
        static partial void CommunityUninstall(string folder, string name);
        string RootfsDir => Path.Combine(Folder, "hakchi-rootfs");
        static string UninstallDir(string folder) => Path.Combine(folder, "hakchi-uninstall");
        string KeptDir => Path.Combine(HomeDir, "community", "kept");

        /// <summary>a place on the data storage (hakchi/rootfs/… or clover/profiles/…) as the simulated console keeps it</summary>
        static string DataPath(string folder, string place)
        {
            if (place.StartsWith("hakchi/rootfs/", StringComparison.Ordinal)) return Path.Combine(folder, "hakchi-rootfs", place.Substring(14).Replace('/', Path.DirectorySeparatorChar));
            if (place.StartsWith("clover/profiles/", StringComparison.Ordinal)) return Path.Combine(folder, "clover-profiles", place.Substring(16).Replace('/', Path.DirectorySeparatorChar));
            return null;
        }

        int CommVerb(string step, string[] a, Stream stdin, Stream stdout)
        {
            void Res(string t) => Out(stdout, "result " + step + " " + t + "\n");
            bool fail = Fault()?.O("prep")?.S("step") == step;
            bool Name(string n) => Engine.CommunityCatalog.PackageOk(n);
            var installed = (Packages(Folder).A("packages") ?? new List<object>()).OfType<string>().ToList();
            switch (step)
            {
                case "cstate":
                    {
                        if (a.Length == 0 || a.Length > 32 || !a.All(Name)) { Res("refused request"); return 17; }
                        var sb = new StringBuilder();
                        foreach (var n in a)
                        {
                            var u = Path.Combine(UninstallDir(Folder), "uninstall-" + n);
                            sb.Append("cpkg=" + n + " " + (installed.Contains(n) ? "listed " + (File.Exists(u) ? ShaFile(u) + " " + File.ReadAllLines(u).Length : "none 0") : "absent"));
                            var kl = Path.Combine(KeptDir, n + ".list");
                            sb.Append(" kept=" + (File.Exists(kl) ? File.ReadAllLines(kl).Count(l => l.Length > 0) + " " + ShaFile(kl) : "none") + "\n");
                        }
                        Out(stdout, sb.ToString()); Res("ok"); return 0;
                    }
                case "cverify":
                    {
                        if (a.Length != 1 || !Name(a[0])) { Res("refused request"); return 17; }
                        var u = Path.Combine(UninstallDir(Folder), "uninstall-" + a[0]);
                        if (!installed.Contains(a[0]) || !File.Exists(u)) { Res("failed not-installed"); return 17; }
                        var paths = Generated(File.ReadAllText(u)).Where(x => x.op == "rm").Select(x => x.path).ToList();
                        var sb = new StringBuilder();
                        foreach (var p in paths)
                        {
                            var f = Path.Combine(RootfsDir, p.Replace('/', Path.DirectorySeparatorChar));
                            string state = !File.Exists(f) ? (Directory.Exists(f) ? "other" : "absent") : File.ReadAllText(f).StartsWith(LinkMark, StringComparison.Ordinal) ? "link" : ShaFile(f);
                            sb.Append("cfile=" + state + " " + p + "\n");
                        }
                        Out(stdout, sb.ToString()); Res("ok " + paths.Count); return 0;
                    }
                case "cput":
                    {
                        if (a.Length != 5 || !Name(a[0]) || a[1] != "0" && a[1] != "1" || !Regex.IsMatch(a[2], "^[1-9][0-9]{0,8}$") || !Regex.IsMatch(a[3], "^[0-9a-f]{64}$") || !Regex.IsMatch(a[4], "^[1-9][0-9]{0,9}$")) { Res("refused request"); return 17; }
                        if (HakchiOnData() == "none") { Res("refused hakchi is not installed on the data storage"); return 17; }
                        long size = long.Parse(a[2], CultureInfo.InvariantCulture), bytes = long.Parse(a[4], CultureInfo.InvariantCulture); string want = a[3];
                        var t = Path.Combine(TransferDir, a[1] == "1" ? "hmod" : "", a[0] + ".hmod");
                        byte[] Receive() { var got = new MemoryStream(); var buf = new byte[1 << 16]; int k; while (got.Length < size && stdin != null && (k = stdin.Read(buf, 0, (int)Math.Min(buf.Length, size - got.Length))) > 0) got.Write(buf, 0, k); return got.ToArray(); }
                        if (File.Exists(t)) { if (ShaFile(t) == want) { Receive(); Res("ok already " + want); return 0; } Res("refused another " + a[0] + ".hmod already waits in the transfer folder"); return 17; }
                        if (FreeBytes() < size + bytes + 8000000) { Res("refused not enough free space (" + FreeBytes() + " bytes free)"); return 17; }
                        Out(stdout, "began cput\n"); if (fail) { Res("failed write (not written again)"); return 17; }
                        var b = Receive(); string sh; using (var h = SHA256.Create()) sh = Hex.Of(h.ComputeHash(b));
                        if (b.LongLength != size || sh != want || b.Length < 2 || b[0] != 0x1f || b[1] != 0x8b) { Res("refused received-mismatch " + sh + " " + b.Length); return 17; }
                        Directory.CreateDirectory(Path.GetDirectoryName(t)); File.WriteAllBytes(t, b); Log("cput " + a[0]);
                        var back = ShaFile(t); if (back != want) { Res("failed readback " + back); return 17; }
                        Res("ok " + want); return 0;
                    }
                case "cunlist":
                    {
                        if (a.Length < 1 || a.Length > 8 || !a.All(Name) || a.Distinct().Count() != a.Length) { Res("refused request"); return 17; }
                        if (HakchiOnData() == "none") { Res("refused hakchi is not installed on the data storage"); return 17; }
                        var name = string.Join(" ", a); string want; using (var h = SHA256.Create()) want = Hex.Of(h.ComputeHash(Encoding.ASCII.GetBytes(name + "\n")));
                        var t = Path.Combine(TransferDir, "uninstall");
                        if (File.Exists(t)) { if (ShaFile(t) == want) { Res("ok already " + want); return 0; } Res("refused another removal list already waits in the transfer folder"); return 17; }
                        Out(stdout, "began cunlist\n"); if (fail) { Res("failed write (not written again)"); return 17; }
                        Directory.CreateDirectory(TransferDir); File.WriteAllText(t, name + "\n"); Log("cunlist " + name);
                        var back = ShaFile(t); if (back != want) { Res("failed readback " + back); return 17; }
                        Res("ok " + want); return 0;
                    }
                case "ckeep":
                    {
                        if (a.Length < 2 || a.Length > 17 || !Name(a[0]) || !a.Skip(1).All(p => Engine.CommunityCatalog.SettingRe.IsMatch(p) && !p.Split('/').Any(x => x == ".." || x == "."))) { Res("refused request"); return 17; }
                        var list = new StringBuilder(); var files = new List<(string place, string file)>();
                        foreach (var p in a.Skip(1).OrderBy(x => x, StringComparer.Ordinal))
                        {
                            var f = DataPath(Folder, p);
                            if (f == null || !File.Exists(f)) { if (f != null && Directory.Exists(f)) { Res("refused " + p + " is not a plain file"); return 17; } continue; }
                            if (File.ReadAllText(f).StartsWith(LinkMark, StringComparison.Ordinal)) { Res("refused " + p + " is not a plain file"); return 17; }
                            list.Append(ShaFile(f) + "  " + p + "\n"); files.Add((p, f));
                        }
                        if (files.Count == 0) { Res("ok none 0"); return 0; }
                        string want; using (var h = SHA256.Create()) want = Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(list.ToString())));
                        var kl = Path.Combine(KeptDir, a[0] + ".list"); var kd = Path.Combine(KeptDir, a[0]);
                        if (File.Exists(kl)) { if (ShaFile(kl) == want) { Res("ok already " + want + " " + files.Count); return 0; } Res("refused settings kept earlier for " + a[0] + " are still there (nothing was changed)"); return 17; }
                        Out(stdout, "began ckeep\n"); if (fail) { Res("failed write (not written again)"); return 17; }
                        if (Directory.Exists(kd)) Directory.Delete(kd, true);
                        foreach (var (place, f) in files) { var d = Path.Combine(kd, place.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(d)); File.Copy(f, d); }
                        File.WriteAllText(kl, list.ToString()); Log("ckeep " + a[0]);
                        foreach (var (place, f) in files) if (ShaFile(Path.Combine(kd, place.Replace('/', Path.DirectorySeparatorChar))) != ShaFile(f)) { Res("failed readback"); return 17; }
                        Res("ok " + ShaFile(kl) + " " + files.Count); return 0;
                    }
                case "cforget":
                    {
                        if (a.Length != 1 || !Name(a[0])) { Res("refused request"); return 17; }
                        var kl = Path.Combine(KeptDir, a[0] + ".list"); var kd = Path.Combine(KeptDir, a[0]);
                        if (!File.Exists(kl) && !Directory.Exists(kd)) { Res("ok already"); return 0; }
                        Out(stdout, "began cforget\n"); if (fail) { Res("failed remove (not tried again)"); return 17; }
                        if (File.Exists(kl)) File.Delete(kl); if (Directory.Exists(kd)) Directory.Delete(kd, true); Log("cforget " + a[0]);
                        Res("ok"); return 0;
                    }
                case "crestore":
                    {
                        if (a.Length != 1 || !Name(a[0])) { Res("refused request"); return 17; }
                        if (!installed.Contains(a[0])) { Res("refused " + a[0] + " is not installed"); return 17; }
                        var kl = Path.Combine(KeptDir, a[0] + ".list"); var kd = Path.Combine(KeptDir, a[0]);
                        if (!File.Exists(kl)) { Res("refused nothing is kept for " + a[0]); return 17; }
                        Out(stdout, "began crestore\n"); if (fail) { Res("failed write (not written again)"); return 17; }
                        int put = 0, skipped = 0;
                        foreach (var l in File.ReadAllLines(kl).Where(x => x.Length > 66))
                        {
                            var sha = l.Substring(0, 64); var place = l.Substring(66); var live = DataPath(Folder, place); var keep = Path.Combine(kd, place.Replace('/', Path.DirectorySeparatorChar));
                            if (live != null && File.Exists(live) && !File.ReadAllText(live).StartsWith(LinkMark, StringComparison.Ordinal) && ShaFile(live) == sha) { put++; continue; }   // already back exactly as kept
                            if (live == null || File.Exists(live) || Directory.Exists(live)) { skipped++; continue; }
                            Directory.CreateDirectory(Path.GetDirectoryName(live)); File.Copy(keep, live);
                            if (ShaFile(live) != sha) { Res("failed readback " + place); return 17; }
                            put++;
                        }
                        if (skipped == 0) { File.Delete(kl); Directory.Delete(kd, true); }
                        Log("crestore " + a[0] + " " + put + " " + skipped); Res("ok " + put + " " + skipped); return 0;
                    }
            }
            Res("refused unknown step"); return 2;
        }

        /// <summary>the lines pack_install generated after "# auto-generated": rm -f "$rootfs/./PATH" … and rmdir … "$rootfs/./DIR" …</summary>
        static List<(string op, string path)> Generated(string text)
        {
            var o = new List<(string, string)>(); bool gen = false;
            foreach (var l in text.Replace("\r", "").Split('\n'))
            {
                if (l == "# auto-generated") { gen = true; continue; }
                if (!gen) continue;
                var op = l.StartsWith("rm -f ", StringComparison.Ordinal) ? "rm" : l.StartsWith("rmdir ", StringComparison.Ordinal) ? "rmdir" : null; if (op == null) continue;
                foreach (Match m in Regex.Matches(l, "\"\\$rootfs/\\./([^\"]+)\"")) o.Add((op, m.Groups[1].Value));
            }
            return o;
        }

        /// <summary>NormalStart: hakchi's pack_install of a real .hmod (a CCS package stand-in is not one): its payload copied into the root
        /// overlay, its uninstall list written; faults.json "community_not_installed" leaves it out</summary>
        static partial void CommunityInstall(string folder, string name, string file, ref bool installs)
        {
            var b = File.ReadAllBytes(file); if (b.Length < 2 || b[0] != 0x1f || b[1] != 0x8b) return;
            var f = Path.Combine(folder, "faults.json");
            if (File.Exists(f) && (Json.Parse(File.ReadAllText(f)).A("community_not_installed") ?? new List<object>()).OfType<string>().Contains(name)) { installs = false; return; }
            var ents = Untar(b); var root = Path.Combine(folder, "hakchi-rootfs"); var own = "";
            var files = new List<string>(); var dirs = new List<string>();
            foreach (var (n, type, data, link) in ents)
            {
                var p = n; while (p.StartsWith("./", StringComparison.Ordinal)) p = p.Substring(2); p = p.TrimEnd('/'); if (p.Length == 0) continue;
                if (!p.Contains("/") && p == "uninstall" && type != '5') { own = Encoding.UTF8.GetString(data); continue; }
                if (!p.Contains("/") && Engine.CommunityArchive.IsControl(p)) continue;
                var dst = Path.Combine(root, p.Replace('/', Path.DirectorySeparatorChar));
                if (type == '5') { Directory.CreateDirectory(dst); dirs.Add(p); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.WriteAllBytes(dst, type == '2' ? Encoding.UTF8.GetBytes(LinkMark + link) : data); files.Add(p);
            }
            var un = new StringBuilder(own); un.Append("\n# auto-generated\n");
            if (files.Count > 0) un.Append("rm -f " + string.Join(" ", files.Concat(new[] { "uninstall" }).Select(x => "\"$rootfs/./" + x + "\"")) + "\n");
            if (dirs.Count > 0) un.Append("rmdir --ignore-fail-on-non-empty " + string.Join(" ", dirs.OrderByDescending(x => x.Length).Select(x => "\"$rootfs/./" + x + "\"")) + "\n");
            Directory.CreateDirectory(UninstallDir(folder)); File.WriteAllText(Path.Combine(UninstallDir(folder), "uninstall-" + name), un.ToString());
        }

        /// <summary>NormalStart: hakchi's pack_uninstall: the package's own rm lines under $rootfs (the only part of a script simulated), then
        /// its generated list (files, then empty folders)</summary>
        static partial void CommunityUninstall(string folder, string name)
        {
            var u = Path.Combine(UninstallDir(folder), "uninstall-" + name); if (!File.Exists(u)) return;
            var text = File.ReadAllText(u).Replace("\r", ""); var root = Path.Combine(folder, "hakchi-rootfs");
            foreach (var l in text.Split(new[] { "\n# auto-generated\n" }, StringSplitOptions.None)[0].Split('\n'))
            {
                var m = Regex.Match(l.Trim(), "^rm -r?f \"\\$rootfs/([^\"]+)\"$"); if (!m.Success || m.Groups[1].Value.Split('/').Contains("..")) continue;
                var p = Path.Combine(root, m.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(p)) File.Delete(p); else if (Directory.Exists(p) && l.Contains("-rf")) Directory.Delete(p, true);
            }
            foreach (var (op, p) in Generated(text).OrderBy(x => x.op == "rm" ? 0 : 1))
            {
                var q = Path.Combine(root, p.Replace('/', Path.DirectorySeparatorChar));
                if (op == "rm" && File.Exists(q)) File.Delete(q);
                if (op == "rmdir" && Directory.Exists(q) && !Directory.EnumerateFileSystemEntries(q).Any()) Directory.Delete(q);
            }
            File.Delete(u);
        }

        static List<(string name, char type, byte[] data, string link)> Untar(byte[] gz)
        {
            var o = new List<(string, char, byte[], string)>(); var m = new MemoryStream();
            using (var z = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress)) z.CopyTo(m);
            var b = m.ToArray(); int p = 0; string longName = null;
            while (p + 512 <= b.Length)
            {
                if (b.Skip(p).Take(512).All(x => x == 0)) break;
                string Cs(int at, int len) { int n = 0; while (n < len && b[p + at + n] != 0) n++; return Encoding.UTF8.GetString(b, p + at, n); }
                var name = Cs(0, 100); var prefix = Cs(345, 155); if (prefix.Length > 0 && Cs(257, 6).StartsWith("ustar")) name = prefix + "/" + name;
                var size = Convert.ToInt64(Cs(124, 12).Trim(' ', '\0').Length == 0 ? "0" : Cs(124, 12).Trim(' ', '\0'), 8); var t = (char)b[p + 156]; var link = Cs(157, 100);
                var data = new byte[size]; Array.Copy(b, p + 512, data, 0, size); p += 512 + (int)((size + 511) / 512 * 512);
                if (t == 'L') { longName = Encoding.UTF8.GetString(data).TrimEnd('\0'); continue; }
                o.Add((longName ?? name, t == '\0' ? '0' : t, data, link)); longName = null;
            }
            return o;
        }
    }
}
