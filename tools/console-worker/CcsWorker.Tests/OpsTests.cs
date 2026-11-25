// Classic Cart Studio console transport worker tests. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The operations over a scripted shell: the exact command text of each (a change to any template fails here first), the parse of
// recorded outputs in the console's formats, the path policy, the source policy and the module archive.
using CcsWorker.Ops;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsWorker.Tests
{
    static class OpsTests
    {
        /// <summary>a stand-in package for the scripted shell (its bytes are pinned for that ConsoleOps only; the shipped pins are the real
        /// builds: tools/companion/test_ccs_install.py keeps ConsolePath.MethodPackages equal to build_images.METHOD_SHAS)</summary>
        static readonly byte[] TestHmod = Encoding.ASCII.GetBytes("a stand-in CCS Mod package (tests only)");
        static ConsoleOps PinnedOps(CcsWorker.Shell.IConsoleShell s) => new ConsoleOps(s, null) { MethodPins = new Dictionary<string, string> { { "ccs-mod", ConsoleOps.Sha(TestHmod) }, { "ccs-nucleus", new string('9', 64) } } };
        static Tuple<int, string, string> Uninstalled(ref bool installed) { installed = false; return R(0, "uninstalling ccs-mod...\nresult remove ok keep\npackage ccs-mod uninstalled\n"); }

        static Dictionary<string, object> A(params object[] kv) { var d = new Dictionary<string, object>(); for (int i = 0; i < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1]; return d; }
        static Tuple<int, string, string> R(int c, string o = "", string e = "") => Tuple.Create(c, o, e);
        const string Chain3 = "for q in '/var/lib/ccs' '/var/lib/ccs/pkg' '/var/lib/ccs/pkg/v'; do if [ -L \"$q\" ] || [ ! -d \"$q\" ]; then echo 'parent-not-folder' >&2; exit 4; fi; done; ";
        const string ChainCcs = "for q in '/var/lib/ccs'; do if [ -L \"$q\" ] || [ ! -d \"$q\" ]; then echo 'parent-not-folder' >&2; exit 4; fi; done; ";
        const string ChainVarLib = "for q in '/var/lib'; do if [ -L \"$q\" ] || [ ! -d \"$q\" ]; then echo 'parent-not-folder' >&2; exit 4; fi; done; ";
        static readonly string H1 = new string('a', 64), H2 = new string('b', 64);

        static void Golden(ScriptedShell s, int i, string want) { T.True(s.Commands.Count > i, "command " + i + " ran"); if (s.Commands[i] != want) throw new Exception("command " + i + " differs:\n  want: " + want + "\n  got:  " + s.Commands[i]); }

        public static void Register()
        {
            T.Add("ops", "path policy: plain absolute paths inside the read and write places only", () =>
            {
                foreach (var bad in new[] { null, "", "var/lib/ccs/x", "/var/lib/ccs/../x", "/var/lib/ccs/./x", "/var/lib/ccs//x", "/var/lib/ccs/x/", "/var/lib/ccs/a b", "/var/lib/ccs/a'b", "/var/lib/ccs/a;b",
                                             "/var/lib/ccs/$x", "/var/lib/ccs/a\nb", "/var/lib/ccsX/a", "/etc/passwd", "/var/saves/x", "/usr/share/games/x", "/var/lib/hakchi/rootfs/etc/x", "/var/lib/ccs/" + new string('a', 520) })
                    T.Eq("path-refused", T.Code(() => ConsolePath.Write(bad), "write " + bad), "write " + bad);
                foreach (var ok in new[] { "/var/lib/ccs", "/var/lib/ccs/pkg/0123456789abcdef.00112233/bin/x", "/tmp/ccs", "/tmp/ccs/hmod-1/a.hmod" }) T.Eq(ok, ConsolePath.Write(ok), "write " + ok);
                foreach (var ok in new[] { "/var/lib/hakchi/hmod/uninstall-ccs-home", "/var/lib/hakchi/rootfs/etc/init.d/S81clover-mcp", "/etc/init.d/S81clover-mcp", "/var/version", "/var/lib",
                                           "/var/lib/hakchi/rootfs/bin/ccs-mod", "/var/lib/hakchi/rootfs/etc/ccs/mod/FILES", "/var/lib/hakchi/rootfs/etc/preinit.d/p7060_ccsnucleus" }) T.Eq(ok, ConsolePath.Read(ok), "read " + ok);
                foreach (var bad in new[] { "/var/lib/hakchi/rootfs/bin/ccs-mod", "/var/lib/hakchi/rootfs/etc/ccs/mod/FILES" }) T.Eq("path-refused", T.Code(() => ConsolePath.Write(bad), "write " + bad), "the method's files are read, never written: " + bad);
                foreach (var bad in new[] { "/var/lib/hakchi/rootfs/etc/passwd", "/etc/shadow", "/var/lib/clover", "/var/saves", "/" }) T.Eq("path-refused", T.Code(() => ConsolePath.Read(bad), "read " + bad), "read " + bad);
                T.Eq("path-refused", T.Code(() => ConsolePath.Write("/var/lib/hakchi/hmod/uninstall-ccs-home"), "hakchi's list is read only"), "hakchi's list");
            });

            T.Add("ops", "stat, list, sha256, read, df: exact command text and the parse of the console's output", () =>
            {
                var s = new ScriptedShell(); var ops = new ConsoleOps(s, null);
                s.Answer = (c, i) => R(0, "link\n../elsewhere\n"); var st = ops.Stat("/var/lib/ccs/pkg/current");
                Golden(s, 0, "p='/var/lib/ccs/pkg/current'; if [ -L \"$p\" ]; then echo link; readlink \"$p\"; elif [ -d \"$p\" ]; then echo dir; elif [ -f \"$p\" ]; then echo file; wc -c < \"$p\"; elif [ -e \"$p\" ]; then echo other; else echo none; fi");
                T.Eq("link", st["type"] as string, "type"); T.Eq("../elsewhere", st["target"] as string, "target");
                s.Answer = (c, i) => R(0, "file\n    1234\n"); st = ops.Stat("/var/lib/ccs/x"); T.Eq("file", st["type"] as string, "file"); T.Eq(1234L, (long)st["size"], "size (BusyBox pads wc)");
                s.Answer = (c, i) => R(0, "none\n"); T.Eq("none", ops.Stat("/var/lib/ccs/y")["type"] as string, "none");
                s.Answer = (c, i) => R(0, "folder\n.\n..\n0123456789abcdef.00112233\ncurrent\nsomeone-elses\n"); var ls = ops.List("/var/lib/ccs/pkg");
                Golden(s, 3, "p='/var/lib/ccs/pkg'; if [ -L \"$p\" ]; then echo not-a-folder; exit 3; elif [ -d \"$p\" ]; then echo folder; ls -1a \"$p\"; elif [ -e \"$p\" ]; then echo not-a-folder; exit 3; else echo none; fi");
                T.True(((List<object>)ls["names"]).SequenceEqual(new object[] { "0123456789abcdef.00112233", "current", "someone-elses" }), "names"); T.Eq(true, (bool)ls["exists"], "exists");
                s.Answer = (c, i) => R(0, "none\n"); ls = ops.List("/var/lib/ccs/pkg"); T.Eq(0, ((List<object>)ls["names"]).Count, "absent: no names"); T.Eq(false, (bool)ls["exists"], "absent");
                s.Answer = (c, i) => R(3, "not-a-folder\n"); T.Eq("not-a-folder", T.Code(() => ops.List("/var/lib/ccs/pkg/current"), "a link"), "not a folder");
                s.Answer = (c, i) => R(0, H1 + "  /var/lib/ccs/a\n- /var/lib/ccs/b\n"); var hs = (Dictionary<string, object>)ops.Sha256(new List<object> { "/var/lib/ccs/a", "/var/lib/ccs/b" })["hashes"];
                Golden(s, 6, "for p in '/var/lib/ccs/a' '/var/lib/ccs/b'; do if [ -f \"$p\" ] && [ ! -L \"$p\" ]; then sha256sum \"$p\" || echo \"- $p\"; else echo \"- $p\"; fi; done");
                T.Eq(H1, hs["/var/lib/ccs/a"] as string, "hash a"); T.True(hs["/var/lib/ccs/b"] == null, "b not a regular file");
                s.Answer = (c, i) => R(0, "{\"x\":1}\n"); var rd = ops.Read("/var/lib/ccs/x", 100);
                Golden(s, 7, "p='/var/lib/ccs/x'; if [ -f \"$p\" ] && [ ! -L \"$p\" ]; then n=$(wc -c < \"$p\"); if [ \"$n\" -gt 100 ]; then echo \"$n\" >&2; exit 4; fi; cat \"$p\"; else exit 3; fi");
                T.Eq("{\"x\":1}\n", Encoding.UTF8.GetString(Convert.FromBase64String((string)rd["b64"])), "content"); T.Eq(8L, (long)rd["size"], "size");
                s.Answer = (c, i) => R(4, "", "5000"); T.Eq("too-large", T.Code(() => ops.Read("/var/lib/ccs/x", 100), "too large"), "too large");
                s.Answer = (c, i) => R(0, "Filesystem           1K-blocks      Used Available Use% Mounted on\n/dev/mapper/a-very-long-device-name\n                        123456     23456    100000  19% /var/lib\n");
                var df = ops.Df("/var/lib"); Golden(s, 9, "df -k '/var/lib'");
                T.Eq(102400000L, (long)df["free_bytes"], "free bytes"); T.Eq(126418944L, (long)df["total_bytes"], "total bytes");
                s.Answer = (c, i) => R(0, "Filesystem           1K-blocks      Used Available Use% Mounted on\n/dev/nandc               98304     40000     58304  41% /var/lib\n");
                T.Eq(58304L * 1024, (long)ops.Df("/var/lib")["free_bytes"], "one-line df");
            });

            T.Add("ops", "menu-refs: the copies' names checked before any command; the walk's answer read line by line; anything not told apart is not complete", () =>
            {
                var s = new ScriptedShell(); var o = new ConsoleOps(s, null);
                const string N = "pkg/0123456789abcdef.00112233", G = "games/0123456789abcdef.44556677";
                foreach (var bad in new[] { new object[0], new object[] { "pkg/x" }, new object[] { "../pkg/0123456789abcdef.00112233" }, new object[] { N, N }, new object[] { "other/0123456789abcdef.00112233" },
                                            new object[] { N + "'" }, new object[] { "pkg/0123456789abcdef.00112233/x" }, Enumerable.Range(0, 65).Select(i => (object)("pkg/0123456789abcdef." + i.ToString("x8"))).ToArray() })
                    T.Eq("bad-args", T.Code(() => o.MenuRefs(bad.ToList()), "bad names"), "bad names: " + string.Join(",", bad));
                T.Eq(0, s.Commands.Count, "nothing ran for bad names");
                string entry = "/var/lib/hakchi/games/nes-usa/000/CLV-L-A B/CLV-L-A B.desktop", usb = "/media/hakchi/games/nes-usa/CLV-L-C/CLV-L-C.desktop";
                string good = "root\t/var/lib/hakchi\tlisted\nref\t" + N + "\t" + H1 + "\t" + entry + "\nend\t/var/lib/hakchi\nroot\t/media/hakchi\tlisted\nref\t" + G + "\t" + H2 + "\t" + usb + "\nend\t/media/hakchi\ndone\n";
                s.Answer = (c, i) => R(0, good);
                var r = o.MenuRefs(new List<object> { N, G });
                T.True((bool)r["complete"], "complete"); var refs = ((List<object>)r["refs"]).Cast<Dictionary<string, object>>().ToList();
                T.Eq(2, refs.Count, "two entries"); T.Eq(entry, refs[0]["path"] as string, "a path with a space kept whole"); T.Eq(N, refs[0]["name"] as string, "its name"); T.Eq(H2, refs[1]["sha256"] as string, "its hash");
                T.Eq("listed", ((Dictionary<string, object>)r["roots"])["/media/hakchi"] as string, "the USB drive's folder was read");
                var cmd = s.Commands[0];
                T.True(cmd.Contains("'ccs/" + N + "' 'ccs/" + G + "'") && cmd.Contains("for R in '/var/lib/hakchi' '/media/hakchi'; do"), "the names and both roots, quoted, in the command");
                T.True(!Regex.IsMatch(cmd, "\\b(find|grep|awk|sed)\\b"), "only the shell and the stock BusyBox's applets (it has no find or grep)");
                T.True(cmd.Contains("if [ -L \"$n\" ]; then case \"$n\" in *.desktop)") && cmd.Contains("*) k \"$n\";;") && cmd.Contains("elif [ -d \"$n\" ]; then w \"$n\";"), "links are read only as entries, never walked into; any other link is checked");
                T.True(cmd.Contains("done < '/proc/mounts'") && cmd.Contains("SQ='/var/squashfs'") && cmd.Contains("*,ro,*)") && cmd.Contains("realpath"), "a folder link counts as read only inside a root walked here or the system image mounted read-only");
                T.Eq(0, ((List<object>)r["linked"]).Count, "no link reported");
                var incomplete = new Dictionary<string, Tuple<int, string>>
                {
                    { "an unreadable entry", Tuple.Create(0, good.Replace("end\t/var/lib/hakchi\n", "unreadable\t/var/lib/hakchi/games/x.desktop\nend\t/var/lib/hakchi\n")) },
                    { "a folder link whose entries are not read", Tuple.Create(0, good.Replace("end\t/var/lib/hakchi\n", "linked\t/var/lib/hakchi/games/linked\nend\t/var/lib/hakchi\n")) },
                    { "no done line", Tuple.Create(0, good.Replace("done\n", "")) },
                    { "a root that is a link", Tuple.Create(0, good.Replace("root\t/media/hakchi\tlisted", "root\t/media/hakchi\tlink").Replace("end\t/media/hakchi\n", "")) },
                    { "a root that is not a folder", Tuple.Create(0, good.Replace("root\t/media/hakchi\tlisted", "root\t/media/hakchi\tother").Replace("end\t/media/hakchi\n", "")) },
                    { "a listed root that did not end", Tuple.Create(0, good.Replace("end\t/media/hakchi\n", "")) },
                    { "a root missing", Tuple.Create(0, "root\t/var/lib/hakchi\tlisted\nend\t/var/lib/hakchi\ndone\n") },
                    { "a line that cannot be told apart", Tuple.Create(0, good.Replace("done\n", "ref\tpkg/zz\t" + H1 + "\t/x\ndone\n")) },
                    { "a hash that is not one", Tuple.Create(0, good.Replace(H1, "-")) },
                    { "a line after done", Tuple.Create(0, good + "ref\t" + N + "\t" + H1 + "\t/late\n") },
                    { "a failed command", Tuple.Create(1, good) },
                };
                foreach (var kv in incomplete) { s.Answer = (c, i) => R(kv.Value.Item1, kv.Value.Item2); T.Eq(false, (bool)o.MenuRefs(new List<object> { N, G })["complete"], kv.Key); }
                s.Answer = (c, i) => R(0, good.Replace("end\t/var/lib/hakchi\n", "linked\t/var/lib/hakchi/games/linked\nend\t/var/lib/hakchi\n"));
                T.Eq("/var/lib/hakchi/games/linked", string.Join(",", ((List<object>)o.MenuRefs(new List<object> { N })["linked"]).Cast<string>()), "the link is named");
                s.Answer = (c, i) => R(0, "root\t/var/lib/hakchi\tlisted\nend\t/var/lib/hakchi\nroot\t/media/hakchi\tabsent\ndone\n");
                r = o.MenuRefs(new List<object> { N }); T.True((bool)r["complete"] && ((List<object>)r["refs"]).Count == 0, "no USB drive and no entry naming it: complete, nothing named");
            });

            T.Add("ops", "sha256: long lists are split so every command stays well inside one clovershell frame", () =>
            {
                var s = new ScriptedShell(); s.Answer = (c, i) => R(0, string.Concat(Regex.Matches(c, "'([^']+)'").Cast<Match>().Select(m => H2 + "  " + m.Groups[1].Value + "\n")));
                var paths = Enumerable.Range(0, 256).Select(i => (object)("/var/lib/ccs/pkg/0123456789abcdef.00112233/roms/" + new string('r', 150) + i)).ToList();
                var hs = (Dictionary<string, object>)new ConsoleOps(s, null).Sha256(paths)["hashes"];
                T.Eq(256, hs.Count, "every path answered"); T.True(s.Commands.Count >= 2 && s.Commands.All(c => c.Length < 33000), "split into " + s.Commands.Count + " commands under 33,000 characters");
                T.Eq("bad-args", T.Code(() => new ConsoleOps(s, null).Sha256(Enumerable.Range(0, 257).Select(i => (object)"/var/lib/ccs/a").ToList()), "257"), "more than 256");
            });

            T.Add("ops", "put: a check, then the guarded write (noclobber, real folders on the way), the mode and the hash read back", () =>
            {
                var data = Encoding.UTF8.GetBytes("hello console\n"); var sha = ConsoleOps.Sha(data);
                var s = new ScriptedShell(); s.Answer = (c, i) => c.Contains("cat > ") ? R(0, ConsoleOps.Sha(i) + "  /var/lib/ccs/pkg/v/f\n") : R(0);
                var r = new ConsoleOps(s, null).Put(A("path", "/var/lib/ccs/pkg/v/f", "b64", Convert.ToBase64String(data), "mode", "0755", "sha256", sha));
                var guard = Chain3 + "p='/var/lib/ccs/pkg/v/f'; if [ -e \"$p\" ] || [ -L \"$p\" ]; then echo exists >&2; exit 3; fi; ";
                Golden(s, 0, guard + "exit 0");
                Golden(s, 1, "set -C; " + guard + "cat > \"$p\" || exit 5; chmod 755 \"$p\" || exit 6; sha256sum \"$p\"");
                T.True(T.Same(data, s.Inputs[1]), "the bytes on stdin"); T.Eq(0, s.Inputs[0].Length, "the check has no input");
                T.Eq(sha, r["sha256"] as string, "hash"); T.Eq(true, (bool)r["match"], "match"); T.Eq((long)data.Length, (long)r["bytes"], "bytes");
                s = new ScriptedShell(); s.Answer = (c, i) => R(3, "", "exists");
                T.Eq("exists", T.Code(() => new ConsoleOps(s, null).Put(A("path", "/var/lib/ccs/f", "b64", Convert.ToBase64String(data), "mode", "0644", "sha256", sha)), "exists"), "exists");
                T.Eq(1, s.Commands.Count, "nothing streamed after the check refused");
                s = new ScriptedShell(); s.Answer = (c, i) => R(4, "", "parent-not-folder");
                T.Eq("parent-not-folder", T.Code(() => new ConsoleOps(s, null).Put(A("path", "/var/lib/ccs/f", "b64", Convert.ToBase64String(data), "mode", "0644", "sha256", sha)), "parent"), "parent");
                s = new ScriptedShell(); s.Answer = (c, i) => c.Contains("cat > ") ? R(0, H1 + "  /var/lib/ccs/f\n") : R(0);
                var bad = new ConsoleOps(s, null).Put(A("path", "/var/lib/ccs/f", "b64", Convert.ToBase64String(data), "mode", "0644", "sha256", sha));
                T.Eq(false, (bool)bad["match"], "a damaged readback is reported, not hidden");
                s = new ScriptedShell(); s.Answer = (c, i) => c.Contains("cat > ") ? R(5, "", "No space left on device") : R(0);
                T.Eq("write-failed", T.Code(() => new ConsoleOps(s, null).Put(A("path", "/var/lib/ccs/f", "b64", Convert.ToBase64String(data), "mode", "0644", "sha256", sha)), "full"), "write-failed");
            });

            T.Add("ops", "put sources: only inside the package, the bytes must match the given SHA-256, no command before that", () =>
            {
                var pkg = Path.Combine(Path.GetTempPath(), "ccs-worker-src-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(Path.Combine(pkg, "bin"));
                try
                {
                    File.WriteAllBytes(Path.Combine(pkg, "bin", "p"), new byte[] { 1, 2, 3 }); File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(pkg), "outside-" + Path.GetFileName(pkg)), new byte[] { 9 });
                    var s = new ScriptedShell(); var ops = new ConsoleOps(s, pkg); var sha = ConsoleOps.Sha(new byte[] { 1, 2, 3 });
                    foreach (var src in new[] { "../outside-" + Path.GetFileName(pkg), "/etc/passwd", "C:\\Windows\\win.ini", "bin\\p", "bin//p", "./bin/p", "" })
                        T.Eq("path-refused", T.Code(() => ops.Put(A("path", "/var/lib/ccs/f", "source", src, "mode", "0644", "sha256", sha)), "source " + src), "source " + src);
                    T.Eq("not-found", T.Code(() => ops.Put(A("path", "/var/lib/ccs/f", "source", "bin/q", "mode", "0644", "sha256", sha)), "missing"), "missing");
                    T.Eq("source-mismatch", T.Code(() => ops.Put(A("path", "/var/lib/ccs/f", "source", "bin/p", "mode", "0644", "sha256", H1)), "mismatch"), "mismatch");
                    T.Eq("bad-args", T.Code(() => ops.Put(A("path", "/var/lib/ccs/f", "source", "bin/p", "mode", "4755", "sha256", sha)), "setuid"), "mode 4755");
                    T.Eq("bad-args", T.Code(() => ops.Put(A("path", "/var/lib/ccs/f", "source", "bin/p", "b64", "AQID", "mode", "0644", "sha256", sha)), "both"), "source and b64");
                    T.Eq("no-package", T.Code(() => new ConsoleOps(s, null).Put(A("path", "/var/lib/ccs/f", "source", "bin/p", "mode", "0644", "sha256", sha)), "no package"), "no package");
                    T.Eq(0, s.Commands.Count, "no command ran for any refusal");
                    s.Answer = (c, i) => c.Contains("cat > ") ? R(0, ConsoleOps.Sha(i) + "  x\n") : R(0);
                    T.Eq(true, (bool)ops.Put(A("path", "/var/lib/ccs/f", "source", "bin/p", "mode", "0644", "sha256", sha))["match"], "a good source");
                }
                finally { Directory.Delete(pkg, true); foreach (var f in Directory.GetFiles(Path.GetDirectoryName(pkg), "outside-ccs-worker-src-*")) File.Delete(f); }
            });

            T.Add("ops", "mkdir, rmdir, rename, remove: exact text, refusals and the remove reasons", () =>
            {
                var s = new ScriptedShell(); var ops = new ConsoleOps(s, null);
                ops.Mkdir("/var/lib/ccs");
                Golden(s, 0, ChainVarLib + "p='/var/lib/ccs'; if [ -e \"$p\" ] || [ -L \"$p\" ]; then echo exists >&2; exit 3; fi; mkdir \"$p\"");
                ops.Rmdir("/var/lib/ccs/pkg/v/x");
                Golden(s, 1, Chain3 + "p='/var/lib/ccs/pkg/v/x'; if [ -d \"$p\" ] && [ ! -L \"$p\" ]; then rmdir \"$p\" || exit 5; else echo not-a-folder >&2; exit 3; fi");
                ops.Rename("/var/lib/ccs/pkg/v/a", "/var/lib/ccs/pkg/v/b");
                Golden(s, 2, Chain3 + Chain3 + "a='/var/lib/ccs/pkg/v/a'; b='/var/lib/ccs/pkg/v/b'; if [ -L \"$a\" ] || { [ ! -f \"$a\" ] && [ ! -d \"$a\" ]; }; then echo source >&2; exit 3; fi; if [ -e \"$b\" ] || [ -L \"$b\" ]; then echo exists >&2; exit 5; fi; mv \"$a\" \"$b\" || exit 6");
                T.Eq("path-refused", T.Code(() => ops.Rename("/var/lib/ccs/a", "/tmp/ccs/a"), "across places"), "rename across places");
                s.Answer = (c, i) => R(0, "removed\n"); var rm = ops.Remove("/var/lib/ccs/f", H1);
                Golden(s, 3, ChainCcs + "p='/var/lib/ccs/f'; if [ -L \"$p\" ]; then echo link; elif [ -f \"$p\" ]; then set -- $(sha256sum \"$p\"); if [ \"$1\" = '" + H1 + "' ]; then rm -f \"$p\" && echo removed; else echo hash-differs; fi; elif [ -e \"$p\" ]; then echo not-a-file; else echo absent; fi");
                T.Eq(true, (bool)rm["removed"], "removed");
                foreach (var why in new[] { "hash-differs", "not-a-file", "link", "absent" })
                {
                    s.Answer = (c, i) => R(0, why + "\n"); var x = ops.Remove("/var/lib/ccs/f", H1);
                    T.Eq(false, (bool)x["removed"], why); T.Eq(why, x["reason"] as string, "reason " + why);
                }
                s.Answer = (c, i) => R(5, "", "Directory not empty"); T.Eq("not-empty", T.Code(() => ops.Rmdir("/var/lib/ccs/pkg"), "not empty"), "not empty");
                s.Answer = (c, i) => R(5, "", ""); T.Eq("exists", T.Code(() => ops.Rename("/var/lib/ccs/a", "/var/lib/ccs/b"), "onto existing"), "rename onto existing");
                s.Answer = (c, i) => R(3, "", ""); T.Eq("exists", T.Code(() => ops.Mkdir("/var/lib/ccs/x"), "exists"), "mkdir onto existing");
                T.Eq("bad-args", T.Code(() => ops.Remove("/var/lib/ccs/f", "abc"), "short hash"), "remove needs a hash");
            });

            T.Add("ops", "pointer: value format, a fresh temporary name, refusal when current is a link or a folder", () =>
            {
                var s = new ScriptedShell(); s.Answer = (c, i) => R(0, "0123456789abcdef.00112233\n");
                var ops = new ConsoleOps(s, null);
                T.Eq("0123456789abcdef.00112233", ops.Pointer("/var/lib/ccs/pkg/current", "0123456789abcdef.00112233")["value"] as string, "read back");
                var want = "^set -C; " + Regex.Escape("for q in '/var/lib/ccs' '/var/lib/ccs/pkg'; do if [ -L \"$q\" ] || [ ! -d \"$q\" ]; then echo 'parent-not-folder' >&2; exit 4; fi; done; p='/var/lib/ccs/pkg/current'; t='/var/lib/ccs/pkg/current.")
                    + "[0-9a-f]{8}" + Regex.Escape(".new'; if [ -L \"$p\" ] || { [ -e \"$p\" ] && [ ! -f \"$p\" ]; }; then echo not-a-pointer >&2; exit 3; fi; if [ -e \"$t\" ] || [ -L \"$t\" ]; then exit 5; fi; printf '%s\\n' '0123456789abcdef.00112233' > \"$t\" || exit 6; mv -f \"$t\" \"$p\" || { rm -f \"$t\"; exit 7; }; cat \"$p\"") + "$";
                T.True(Regex.IsMatch(s.Commands[0], want), "pointer text: " + s.Commands[0]);
                ops.Pointer("/var/lib/ccs/pkg/current", "0123456789abcdef.00112233"); T.True(s.Commands[0] != s.Commands[1], "a different temporary name each time");
                foreach (var v in new[] { "0123456789abcdef", "0123456789ABCDEF.00112233", "0123456789abcdef.0011223", "x; rm -rf /", null }) T.Eq("bad-args", T.Code(() => ops.Pointer("/var/lib/ccs/pkg/current", v), "value " + v), "value " + v);
                s.Answer = (c, i) => R(3, "", "not-a-pointer"); T.Eq("not-a-pointer", T.Code(() => ops.Pointer("/var/lib/ccs/pkg/current", "0123456789abcdef.00112233"), "link"), "refused");
            });

            T.Add("ops", "identify: hakchi.cs's commands in its order and its version rule", () =>
            {
                var s = new ScriptedShell(); s.Answer = (c, i) =>
                    c == "hakchi ismdPartitioning" ? R(1) : c.StartsWith("source /hakchi/config") ? R(1) : c == "hakchi hwid" ? R(0, "12 34 56 78 9a bc de f0 01 23 45 67 89 ab cd ef\n") :
                    c == "cat /proc/sys/kernel/osrelease" ? R(0, "3.4.113.29-madmonkey\n") : c == "[ -f /var/version ] && echo \"yes\"" ? R(0, "yes\n") :
                    c.StartsWith("source /var/version") ? R(0, "1.0.3 3.4.113 v1.0.4-126\n") : c.StartsWith("hakchi eval") ? R(0, "nes-usa\n") : c == "hakchi currentFirmware" ? R(0, "_nand_\n") :
                    c == "cat /etc/clover/boardtype" ? R(0, "dp-nes\n") : c == "cat /etc/clover/REGION" ? R(0, "EUR_USA\n") : c.EndsWith("done < /proc/mounts") ? R(0, "/dev/nandc ext4\n") : R(127, "", "?");
                var r = new ConsoleOps(s, null).Identify();
                T.True(s.Commands.SequenceEqual(new[] { "hakchi ismdPartitioning", "source /hakchi/config; [ \"$cf_memboot\" = \"y\" ]", "hakchi hwid", "cat /proc/sys/kernel/osrelease", "[ -f /var/version ] && echo \"yes\"",
                    "source /var/version && echo \"$bootVersion $kernelVersion $hakchiVersion\"", "hakchi eval 'echo \"$sftype-$sfregion\"'", "hakchi currentFirmware",
                    "cat /etc/clover/boardtype", "cat /etc/clover/REGION", "while read -r d m t o x; do [ \"$m\" = /var/lib ] && echo \"$d $t\"; done < /proc/mounts" }), "commands: " + string.Join(" | ", s.Commands));
                T.Eq("dp-nes", r["board"] as string, "board, as the console's own software names it"); T.Eq("EUR_USA", r["region"] as string, "region"); T.Eq("/dev/nandc ext4", r["data_mount"] as string, "where its data storage is mounted");
                T.Eq("nes-usa", r["system"] as string, "system"); T.Eq("3.4.113.29-madmonkey", r["kernel"] as string, "running kernel"); T.Eq("1.0.3", r["boot_version"] as string, "boot");
                T.Eq("v1.0.4-126", r["hakchi_version"] as string, "hakchi"); T.Eq("123456789abcdef00123456789abcdef", r["hwid"] as string, "hwid without spaces");
                T.Eq(false, (bool)r["custom_firmware"], "stock NAND firmware"); T.Eq(true, (bool)r["can_interact"], "can interact"); T.Eq(false, (bool)r["md_partitioning"], "md"); T.Eq(false, (bool)r["minimal_memboot"], "memboot");
                var s2 = new ScriptedShell(); s2.Answer = (c, i) => c.StartsWith("[ -f /var/version ]") ? R(1) : c == "hakchi hwid" ? R(0, "00") : R(1);
                var r2 = new ConsoleOps(s2, null).Identify();
                T.Eq(false, (bool)r2["can_interact"], "no /var/version: severely outdated"); T.True(r2["system"] == null && r2["board"] == null, "no system or board asked"); T.True(!s2.Commands.Any(c => c.StartsWith("hakchi eval") || c.Contains("/etc/clover")), "nothing more asked");
                var s3 = new ScriptedShell(); s3.Answer = (c, i) => c.StartsWith("source /hakchi/config") ? R(0) : R(0, "x");
                var r3 = new ConsoleOps(s3, null).Identify(); T.Eq(true, (bool)r3["minimal_memboot"], "memboot"); T.Eq(4, s3.Commands.Count, "a minimal memboot stops after hwid and the kernel");
                T.True(ConsoleOps.Version("v1.0.4-118") >= new Version(1, 0, 4, 118) && ConsoleOps.Version("3.4.112-00") < new Version(3, 4, 113), "ConvertVersion");
            });

            T.Add("ops", "method package install/uninstall/files: only the pinned CCS Mod / CCS Nucleus builds, sent, checked, hakchi's pack_install, its own answer and list", () =>
            {
                var hm = TestHmod; var sha = ConsoleOps.Sha(hm);
                T.True(ConsolePath.MethodPackages.Count == 3 && ConsolePath.MethodPackages.Values.All(v => Regex.IsMatch(v, "^[0-9a-f]{64}$")), "the shipped worker pins exactly three packages");
                const string list = "# CCS Mod for Hakchi: removal\nreturn 0\n\n# auto-generated\nrm -f \"$rootfs/./uninstall\" \"$rootfs/./bin/ccs-mod\"\n";
                var s = new ScriptedShell(); var installed = true;
                s.Answer = (c, i) => c.Contains("cat > \"$d/ccs-mod.hmod\"") ? R(0, ConsoleOps.Sha(i) + "  /tmp/ccs/hmod-x/ccs-mod.hmod\n") : c.StartsWith("hakchi pack_install") ? R(0, "installing ccs-mod...\nresult preinstall ok\nresult postinstall ok in-charge; entry ok /var/lib/hakchi/games/000/CLV-H-CCSMD\npackage ccs-mod installed\n")
                    : c.StartsWith("u=") ? R(0, installed ? "present\n" + list : "absent\n") : c.StartsWith("hakchi pack_uninstall") ? Uninstalled(ref installed) : R(0);
                var ops = PinnedOps(s);
                T.Eq("not-pinned", T.Code(() => new ConsoleOps(s, null).ModuleInstall(A("name", "ccs-mod", "hmod", A("b64", Convert.ToBase64String(hm), "sha256", sha))), "the shipped pins"), "with the shipped pins, only the real build is installed");
                var r = ops.ModuleInstall(A("name", "ccs-mod", "hmod", A("b64", Convert.ToBase64String(hm), "sha256", sha)));
                T.True(Regex.IsMatch(s.Commands[0], "^set -C; r='/tmp/ccs'; d='/tmp/ccs/hmod-[0-9a-f]{8}'; if \\[ -L \"\\$r\" \\]; then exit 4; fi; if \\[ ! -d \"\\$r\" \\]; then mkdir \"\\$r\" \\|\\| exit 4; fi; if \\[ -e \"\\$d\" \\] \\|\\| \\[ -L \"\\$d\" \\]; then exit 3; fi; mkdir \"\\$d\" && cat > \"\\$d/ccs-mod.hmod\" && sha256sum \"\\$d/ccs-mod.hmod\"$"), "transfer: " + s.Commands[0]);
                var dir = Regex.Match(s.Commands[0], "/tmp/ccs/hmod-[0-9a-f]{8}").Value;
                T.Eq("hakchi pack_install '" + dir + "/ccs-mod.hmod'; r=$?; rm -rf '" + dir + "'; exit $r", s.Commands[1], "hakchi's own pack_install");
                T.True(T.Same(hm, s.Inputs[0]), "the package's own bytes were streamed"); T.Eq("result postinstall ok in-charge; entry ok /var/lib/hakchi/games/000/CLV-H-CCSMD", r["postinstall"] as string, "the package's own answer");
                T.True(((string)r["uninstall_list"]).Contains("auto-generated"), "hakchi's uninstall list returned");
                ops.ModuleUninstall("ccs-mod"); T.Eq("hakchi pack_uninstall 'ccs-mod'", s.Commands[s.Commands.Count - 2], "pack_uninstall (then the list read again)");
                T.Eq("not-pinned", T.Code(() => ops.ModuleInstall(A("name", "ccs-mod", "hmod", A("b64", Convert.ToBase64String(new byte[] { 1, 2 }), "sha256", ConsoleOps.Sha(new byte[] { 1, 2 })))), "another package"), "only the pinned build");
                T.Eq("not-pinned", T.Code(() => ops.ModuleInstall(A("name", "ccs-nucleus", "hmod", A("b64", Convert.ToBase64String(hm), "sha256", sha))), "the other name"), "a package under the other method's name");
                T.Eq("bad-args", T.Code(() => ops.ModuleInstall(A("name", "ccs-home", "hmod", A("b64", "AA==", "sha256", ConsoleOps.Sha(new byte[1])))), "the earlier module"), "the earlier start-up module is never installed");
                T.Eq("bad-args", T.Code(() => ops.ModuleUninstall("clovercon"), "another package"), "only CCS's own packages are removed");
                T.Eq("source-mismatch", T.Code(() => ops.ModuleInstall(A("name", "ccs-mod", "hmod", A("b64", Convert.ToBase64String(new byte[] { 1 }), "sha256", sha))), "bytes that are not the pinned ones"), "the bytes must be the pinned ones");
                var n0 = s.Commands.Count; s.Answer = (c, i) => c.Contains("cat > ") ? R(0, H1 + "  x\n") : R(0);
                T.Eq("readback-mismatch", T.Code(() => ops.ModuleInstall(A("name", "ccs-mod", "hmod", A("b64", Convert.ToBase64String(hm), "sha256", sha))), "damaged"), "a damaged transfer is not installed");
                T.True(!s.Commands.Skip(n0).Any(c => c.StartsWith("hakchi pack_install")), "pack_install not run after a damaged transfer");
                s.Answer = (c, i) => c.Contains("cat > ") ? R(0, ConsoleOps.Sha(i) + "  x\n") : c.StartsWith("hakchi pack_install") ? R(0, "result preinstall refused nothing was copied\npackage ccs-mod installed\n") : c.StartsWith("u=") ? R(0, "absent\n") : R(0);
                T.Eq("module-failed", T.Code(() => ops.ModuleInstall(A("name", "ccs-mod", "hmod", A("b64", Convert.ToBase64String(hm), "sha256", sha))), "refused by its own script"), "the package's own refusal is a failure, not an installation");
            });

            T.Add("ops", "nucleus-start: CCS Nucleus's own start command, exact text, only while hakchi lists CCS Nucleus; anything but its success is a failure", () =>
            {
                var s = new ScriptedShell(); string answer = "result start ok default=ccs-home chooser=always home=on\n"; bool listed = true;
                s.Answer = (c, i) => c.StartsWith("u=") ? R(0, listed ? "present\n# CCS Nucleus\n" : "absent\n") : c.StartsWith("p='/var/lib/hakchi/rootfs/bin/ccs-nucleus'") ? R(answer.Contains(" ok ") ? 0 : 17, answer) : R(0);
                var ops = new ConsoleOps(s, null);
                var r = ops.NucleusStart("ccs-home", "always", "on"); T.Eq("result start ok default=ccs-home chooser=always home=on", r["result"] as string, "set");
                Golden(s, 1, "p='/var/lib/hakchi/rootfs/bin/ccs-nucleus'; if [ -L \"$p\" ] || [ ! -f \"$p\" ]; then exit 4; fi; CCS_ROOT=\"${p%/var/lib/hakchi/rootfs/bin/ccs-nucleus}\" sh \"$p\" start ccs-home always on default 10");
                answer = "result start refused CCS HOME is not offered\n";
                T.Eq("module-failed", T.Code(() => ops.NucleusStart("ccs-home", "never", "off"), "refused"), "its own refusal is a failure");
                var n = s.Commands.Count; listed = false;
                T.Eq("not-installed", T.Code(() => ops.NucleusStart("native", "never", "off"), "not listed"), "refused while hakchi does not list CCS Nucleus");
                T.True(!s.Commands.Skip(n).Any(c => c.Contains(" start ")), "and its command is never run then");
                T.Eq("bad-args", T.Code(() => ops.NucleusStart("native; reboot", "never", "off"), "text"), "only the listed words: never shell text");
            });

            T.Add("ops", "collections: the console program for Hakchi games, only the pinned program and library from the folder named by the pin; its output and result line", () =>
            {
                var s = new ScriptedShell(); string answer = "ok /var/lib/hakchi/games/nes-usa/ccs-0123456789/CLV-H-ABCDE/CLV-H-ABCDE.desktop\nresult status ok journal=valid files=1 changed=0 missing=0 index=none\n"; int code = 0;
                var dir = ConsolePath.CollectionsDir;
                s.Answer = (c, i) => c.StartsWith("p='" + dir + "/ccs-collections'") ? R(code, answer) : R(0);
                var ops = new ConsoleOps(s, null);
                var r = ops.Collections("status", null); T.Eq("result status ok journal=valid files=1 changed=0 missing=0 index=none", r["result"] as string, "its result line");
                T.True(((string)r["output"]).Contains("CLV-H-ABCDE.desktop"), "its whole output");
                T.True(dir == "/var/lib/ccs/integration/program-" + ConsolePath.CollectionsProgram["ccs-collections"].Substring(0, 16), "the folder is named by the program's pin");
                Golden(s, 0, "p='" + dir + "/ccs-collections'; l='" + dir + "/lib.sh'; for f in \"$p\" \"$l\"; do if [ -L \"$f\" ] || [ ! -f \"$f\" ]; then exit 4; fi; done; set -- $(sha256sum \"$p\"); [ \"$1\" = '"
                    + ConsolePath.CollectionsProgram["ccs-collections"] + "' ] || exit 5; set -- $(sha256sum \"$l\"); [ \"$1\" = '" + ConsolePath.CollectionsProgram["lib.sh"] + "' ] || exit 5; CCS_ROOT=\"${p%" + dir + "/ccs-collections}\" sh \"$p\" status");
                answer = "result apply ok entries=3 wrote=9 removed=0 generation=1\n";
                r = ops.Collections("apply", "/var/lib/ccs/integration/stage/0a1b2c3d"); T.True(s.Commands[s.Commands.Count - 1].EndsWith(" apply '/var/lib/ccs/integration/stage/0a1b2c3d'"), "the stage, quoted");
                answer = "result apply refused the index changed since it was read (nothing was changed)\n"; code = 17;
                r = ops.Collections("apply", "/var/lib/ccs/integration/stage/0a1b2c3d"); T.True(((string)r["result"]).StartsWith("result apply refused"), "a refusal is its result line (the caller decides)");
                code = 5; T.Eq("not-installed", T.Code(() => ops.Collections("status", null), "not the pinned files"), "a program that is not the pinned one is never run");
                code = 0; answer = "no result\n"; T.Eq("module-failed", T.Code(() => ops.Collections("where", null), "no result line"), "an answer without its result line is a failure");
                var n = s.Commands.Count;
                T.Eq("bad-args", T.Code(() => ops.Collections("apply", "/var/lib/ccs/data"), "stage"), "a stage only under integration/stage");
                T.Eq("bad-args", T.Code(() => ops.Collections("apply", "/var/lib/ccs/integration/stage/0a1b2c3d; reboot"), "text"), "never shell text");
                T.Eq("bad-args", T.Code(() => ops.Collections("status", "/var/lib/ccs/integration/stage/0a1b2c3d"), "extra"), "a stage only for check and apply");
                T.Eq("bad-args", T.Code(() => ops.Collections("rm", null), "command"), "only its five commands");
                T.Eq(n, s.Commands.Count, "nothing was run for a refused request");
            });

            T.Add("ops", "method package files: hakchi's list, the package's VERSION and FILES, every listed file's hash in hakchi's overlay; the earlier module's list", () =>
            {
                var files = "1111111111111111111111111111111111111111111111111111111111111111 0755 bin/ccs-mod\n2222222222222222222222222222222222222222222222222222222222222222 0644 etc/ccs/mod/lib.sh\n3333333333333333333333333333333333333333333333333333333333333333 0644 etc/passwd\n";
                var s = new ScriptedShell();
                s.Answer = (c, i) => c.StartsWith("u=") ? R(0, "present\n# CCS Mod\n") : c.Contains("/etc/ccs/mod/VERSION") && c.StartsWith("p=") ? R(0, "schema=ccs-method-package/1\npackage=" + new string('a', 64) + "\n")
                    : c.Contains("/etc/ccs/mod/FILES") && c.StartsWith("p=") ? R(0, files) : c.StartsWith("for p in") ? R(0, new string('1', 64) + "  /var/lib/hakchi/rootfs/bin/ccs-mod\n- /var/lib/hakchi/rootfs/etc/ccs/mod/lib.sh\n" + new string('9', 64) + "  /var/lib/hakchi/rootfs/etc/ccs/mod/FILES\n") : R(0);
                var ops = new ConsoleOps(s, null); var mf = ops.ModuleFiles("ccs-mod"); var f = (Dictionary<string, object>)mf["files"];
                T.Eq(true, (bool)mf["present"], "present"); T.Eq(new string('a', 64), mf["package"] as string, "its package id");
                T.Eq(new string('1', 64), f["/bin/ccs-mod"] as string, "its program's hash"); T.True(f.ContainsKey("/etc/ccs/mod/lib.sh") && f["/etc/ccs/mod/lib.sh"] == null, "a missing file reads null");
                T.True(f.ContainsKey("/etc/passwd") && f["/etc/passwd"] == null && ((List<object>)mf["unreadable"]).Contains("/etc/passwd"), "a listed path outside the method's places is reported, never read");
                T.True(!s.Commands.Any(c => c.Contains("'/var/lib/hakchi/rootfs/etc/passwd'")), "and no command named it");
                s.Answer = (c, i) => R(0, "absent\n"); T.Eq(false, (bool)ops.ModuleFiles("ccs-nucleus")["present"], "not listed: absent");
                // the earlier start-up module: only read (its list) and removed
                var s81 = new string('5', 64);
                s.Answer = (c, i) => c.StartsWith("u=") ? R(0, "present\n\n# auto-generated\nrm -f \"$rootfs/./uninstall\" \"$rootfs/./etc/init.d/S81clover-mcp\"\n") : c.StartsWith("for p in") ? R(0, s81 + "  /var/lib/hakchi/rootfs/etc/init.d/S81clover-mcp\n") : R(0);
                var lf = ops.ModuleFiles("ccs-home"); var lff = (Dictionary<string, object>)lf["files"];
                T.Eq(1, lff.Count, "one file"); T.Eq(s81, lff["/etc/init.d/S81clover-mcp"] as string, "its hash"); T.True(((List<object>)lf["ignored"]).SequenceEqual(new object[] { "/uninstall" }), "pack_install's own uninstall entry ignored");
            });

            T.Add("ops", "read in pieces: exact text, the size line, whole files below, above and at multiples of the old 1 MB limit, past the end", () =>
            {
                var s = new ScriptedShell(); var ops = new ConsoleOps(s, null);
                s.Answer = (c, i) => R(0, "    0\n"); ops.ReadChunk("/var/lib/ccs/data/saves/x/slot1.state", 8192, 262144);
                Golden(s, 0, "p='/var/lib/ccs/data/saves/x/slot1.state'; if [ -f \"$p\" ] && [ ! -L \"$p\" ]; then wc -c < \"$p\" && dd if=\"$p\" bs=4096 skip=2 count=64 2>/dev/null; else exit 3; fi");
                foreach (var size in new[] { 999999, 1000001, 1048576, 2600000, 4096, 1 })
                {
                    var file = new string(Enumerable.Range(0, size).Select(k => (char)('a' + (k * 7 + k / 13) % 26)).ToArray());   // ASCII: the scripted shell answers text
                    s = new ScriptedShell(); ops = new ConsoleOps(s, null);
                    s.Answer = (c, i) =>
                    {
                        var m = Regex.Match(c, "skip=(\\d+) count=(\\d+)"); long off = long.Parse(m.Groups[1].Value) * 4096, n = long.Parse(m.Groups[2].Value) * 4096;
                        return R(0, "  " + size + "\n" + (off >= size ? "" : file.Substring((int)off, (int)Math.Min(n, size - off))));
                    };
                    var got = new MemoryStream(); long offset = 0; int pieces = 0; Dictionary<string, object> r;
                    do
                    {
                        r = ops.ReadChunk("/var/lib/ccs/data/f", offset, 262144); var b = Convert.FromBase64String((string)r["b64"]);
                        T.Eq((long)size, (long)r["file_size"], "file size " + size); T.Eq(offset, (long)r["offset"], "offset"); T.Eq((long)b.Length, (long)r["size"], "size");
                        got.Write(b, 0, b.Length); offset += b.Length; pieces++;
                    } while (!(bool)r["eof"] && pieces < 100);
                    T.Eq(ConsoleOps.Sha(Encoding.ASCII.GetBytes(file)), ConsoleOps.Sha(got.ToArray()), "the whole " + size + "-byte file in " + pieces + " pieces");
                    T.Eq((size + 262143) / 262144, pieces, "pieces for " + size);
                    var past = ops.ReadChunk("/var/lib/ccs/data/f", ((size + 4095) / 4096 + 3) * 4096L, 4096);
                    T.Eq(0L, (long)past["size"], "past the end: no bytes"); T.Eq(true, (bool)past["eof"], "past the end: eof"); T.Eq((long)size, (long)past["file_size"], "past the end: the size");
                }
                s = new ScriptedShell(); ops = new ConsoleOps(s, null);
                foreach (var bad in new[] { Tuple.Create(-4096L, 4096L), Tuple.Create(100L, 4096L), Tuple.Create(0L, 0L), Tuple.Create(0L, 4095L), Tuple.Create(0L, 4097L), Tuple.Create(0L, 266240L), Tuple.Create(0L, 262145L) })
                    T.Eq("bad-args", T.Code(() => ops.ReadChunk("/var/lib/ccs/data/f", bad.Item1, bad.Item2), "offset " + bad.Item1 + " length " + bad.Item2), "offset " + bad.Item1 + " length " + bad.Item2);
                T.Eq("path-refused", T.Code(() => ops.ReadChunk("/var/saves/x", 0, 4096), "stock saves"), "only this program's places");
                T.Eq(0, s.Commands.Count, "no command ran for any refusal");
                s.Answer = (c, i) => R(3); T.Eq("not-a-file", T.Code(() => ops.ReadChunk("/var/lib/ccs/data/link", 0, 4096), "a link"), "a link or a folder is refused");
                s.Answer = (c, i) => R(0, "8192\n" + new string('x', 8192)); T.Eq("shell-error", T.Code(() => ops.ReadChunk("/var/lib/ccs/data/f", 0, 4096), "too many"), "more bytes than asked");
                s.Answer = (c, i) => R(0, "garbage without a size"); T.Eq("shell-error", T.Code(() => ops.ReadChunk("/var/lib/ccs/data/f", 0, 4096), "no size"), "no size line");
                s.Answer = (c, i) => R(1, "", "I/O error"); T.Eq("shell-error", T.Code(() => ops.ReadChunk("/var/lib/ccs/data/f", 0, 4096), "failed"), "a failed read");
            });

            T.Add("ops", "sync: exact text with and without the page cache dropped; a failed drop is reported, not hidden", () =>
            {
                var s = new ScriptedShell(); var ops = new ConsoleOps(s, null);
                var r = ops.Sync(false); Golden(s, 0, "sync || exit 5"); T.Eq(true, (bool)r["synced"], "synced"); T.Eq(false, (bool)r["dropped"], "not dropped");
                r = ops.Sync(true); Golden(s, 1, "sync || exit 5; echo 1 > /proc/sys/vm/drop_caches || exit 6"); T.Eq(true, (bool)r["dropped"], "dropped");
                s.Answer = (c, i) => R(6, "", "Permission denied"); r = ops.Sync(true);
                T.Eq(true, (bool)r["synced"], "synced although"); T.Eq(false, (bool)r["dropped"], "the drop failed"); T.True(((string)r["drop_error"]).Contains("Permission denied"), "why");
                s.Answer = (c, i) => R(5, "", "I/O error"); T.Eq("sync-failed", T.Code(() => ops.Sync(false), "sync failed"), "sync failed");
            });

            T.Add("ops", "pointer expect: absent or the current SHA-256, checked in the same command; pointer-changed changes nothing", () =>
            {
                var s = new ScriptedShell(); s.Answer = (c, i) => R(0, "0123456789abcdef.00112233\n"); var ops = new ConsoleOps(s, null);
                const string head = "^set -C; for q in '/var/lib/ccs' '/var/lib/ccs/pkg'; do if \\[ -L \"\\$q\" \\] \\|\\| \\[ ! -d \"\\$q\" \\]; then echo 'parent-not-folder' >&2; exit 4; fi; done; p='/var/lib/ccs/pkg/current'; t='/var/lib/ccs/pkg/current\\.[0-9a-f]{8}\\.new'; ";
                const string tail = "if \\[ -L \"\\$p\" \\] \\|\\| \\{ \\[ -e \"\\$p\" \\] && \\[ ! -f \"\\$p\" \\]; \\}; then echo not-a-pointer >&2; exit 3; fi; ";
                ops.Pointer("/var/lib/ccs/pkg/current", "0123456789abcdef.00112233", "absent");
                T.True(Regex.IsMatch(s.Commands[0], head + Regex.Escape("if [ -e \"$p\" ] || [ -L \"$p\" ]; then echo pointer-changed >&2; exit 8; fi; ") + tail), "absent: " + s.Commands[0]);
                ops.Pointer("/var/lib/ccs/pkg/current", "0123456789abcdef.00112233", H1);
                T.True(Regex.IsMatch(s.Commands[1], head + Regex.Escape("if [ -L \"$p\" ] || [ ! -f \"$p\" ]; then echo pointer-changed >&2; exit 8; fi; set -- $(sha256sum \"$p\"); if [ \"$1\" != '" + H1 + "' ]; then echo pointer-changed >&2; exit 8; fi; ") + tail), "sha: " + s.Commands[1]);
                ops.Pointer("/var/lib/ccs/pkg/current", "0123456789abcdef.00112233");
                T.True(Regex.IsMatch(s.Commands[2], head + tail), "no expectation: the earlier text");
                s.Answer = (c, i) => R(8, "", "pointer-changed");
                T.Eq("pointer-changed", T.Code(() => ops.Pointer("/var/lib/ccs/pkg/current", "0123456789abcdef.00112233", "absent"), "something there"), "absent expected, something there");
                T.Eq("pointer-changed", T.Code(() => ops.Pointer("/var/lib/ccs/pkg/current", "0123456789abcdef.00112233", H2), "changed"), "another content");
                int n = s.Commands.Count;
                foreach (var bad in new[] { "ABSENT", "abc", H1.ToUpperInvariant(), H1 + "'; rm -rf /; '", "" }) T.Eq("bad-args", T.Code(() => ops.Pointer("/var/lib/ccs/pkg/current", "0123456789abcdef.00112233", bad), "expect " + bad), "expect " + bad);
                T.Eq(n, s.Commands.Count, "no command for a bad expectation");
            });
        }
    }
}
