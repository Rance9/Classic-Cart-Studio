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
                foreach (var ok in new[] { "/var/lib/hakchi/hmod/uninstall-ccs-home", "/var/lib/hakchi/rootfs/etc/init.d/S81clover-mcp", "/etc/init.d/S81clover-mcp", "/var/version", "/var/lib" }) T.Eq(ok, ConsolePath.Read(ok), "read " + ok);
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
                    c.StartsWith("source /var/version") ? R(0, "1.0.3 3.4.113 v1.0.4-126\n") : c.StartsWith("hakchi eval") ? R(0, "nes-usa\n") : c == "hakchi currentFirmware" ? R(0, "_nand_\n") : R(127, "", "?");
                var r = new ConsoleOps(s, null).Identify();
                T.True(s.Commands.SequenceEqual(new[] { "hakchi ismdPartitioning", "source /hakchi/config; [ \"$cf_memboot\" = \"y\" ]", "hakchi hwid", "cat /proc/sys/kernel/osrelease", "[ -f /var/version ] && echo \"yes\"",
                    "source /var/version && echo \"$bootVersion $kernelVersion $hakchiVersion\"", "hakchi eval 'echo \"$sftype-$sfregion\"'", "hakchi currentFirmware" }), "commands: " + string.Join(" | ", s.Commands));
                T.Eq("nes-usa", r["system"] as string, "system"); T.Eq("3.4.113.29-madmonkey", r["kernel"] as string, "running kernel"); T.Eq("1.0.3", r["boot_version"] as string, "boot");
                T.Eq("v1.0.4-126", r["hakchi_version"] as string, "hakchi"); T.Eq("123456789abcdef00123456789abcdef", r["hwid"] as string, "hwid without spaces");
                T.Eq(false, (bool)r["custom_firmware"], "stock NAND firmware"); T.Eq(true, (bool)r["can_interact"], "can interact"); T.Eq(false, (bool)r["md_partitioning"], "md"); T.Eq(false, (bool)r["minimal_memboot"], "memboot");
                var s2 = new ScriptedShell(); s2.Answer = (c, i) => c.StartsWith("[ -f /var/version ]") ? R(1) : c == "hakchi hwid" ? R(0, "00") : R(1);
                var r2 = new ConsoleOps(s2, null).Identify();
                T.Eq(false, (bool)r2["can_interact"], "no /var/version: severely outdated"); T.True(r2["system"] == null, "no system asked"); T.True(!s2.Commands.Any(c => c.StartsWith("hakchi eval")), "nothing more asked");
                var s3 = new ScriptedShell(); s3.Answer = (c, i) => c.StartsWith("source /hakchi/config") ? R(0) : R(0, "x");
                var r3 = new ConsoleOps(s3, null).Identify(); T.Eq(true, (bool)r3["minimal_memboot"], "memboot"); T.Eq(4, s3.Commands.Count, "a minimal memboot stops after hwid and the kernel");
                T.True(ConsoleOps.Version("v1.0.4-118") >= new Version(1, 0, 4, 118) && ConsoleOps.Version("3.4.112-00") < new Version(3, 4, 113), "ConvertVersion");
            });

            T.Add("ops", "module archive: the same bytes every time; ustar entries owned by root, time 0, valid checksums, mode kept", () =>
            {
                var s81 = Encoding.UTF8.GetBytes("#!/bin/sh\necho ccs\n");
                var files = new SortedDictionary<string, KeyValuePair<byte[], int>> { ["/etc/init.d/S81clover-mcp"] = new KeyValuePair<byte[], int>(s81, 0x1ED) };
                var a = Hmod.Build(files); var b = Hmod.Build(files); T.True(T.Same(a, b), "deterministic");
                var tar = new MemoryStream(); using (var z = new GZipStream(new MemoryStream(a), CompressionMode.Decompress)) z.CopyTo(tar);
                var t = tar.ToArray(); var names = new List<string>(); int pos = 0;
                while (pos + 512 <= t.Length && t[pos] != 0)
                {
                    var h = t.Skip(pos).Take(512).ToArray(); var name = Encoding.ASCII.GetString(h, 0, 100).TrimEnd('\0'); names.Add(name);
                    int sum = h.Select((x, i) => i >= 148 && i < 156 ? 32 : x).Sum(); T.Eq(Convert.ToString(sum, 8).PadLeft(6, '0'), Encoding.ASCII.GetString(h, 148, 6), "checksum of " + name);
                    T.Eq("0000000", Encoding.ASCII.GetString(h, 108, 7), "uid"); T.Eq("00000000000", Encoding.ASCII.GetString(h, 136, 11), "mtime"); T.Eq("ustar", Encoding.ASCII.GetString(h, 257, 5), "magic");
                    long size = Convert.ToInt64(Encoding.ASCII.GetString(h, 124, 11), 8);
                    if (name.EndsWith("S81clover-mcp")) { T.Eq("0000755", Encoding.ASCII.GetString(h, 100, 7), "mode"); T.True(T.Same(s81, t.Skip(pos + 512).Take((int)size).ToArray()), "content"); }
                    pos += 512 + (int)((size + 511) / 512 * 512);
                }
                T.True(names.SequenceEqual(new[] { "./etc/", "./etc/init.d/", "./etc/init.d/S81clover-mcp" }), "entries: " + string.Join(",", names));
            });

            T.Add("ops", "module install/uninstall/files: transfer, hakchi's pack_install, the uninstall list as pack_install writes it", () =>
            {
                var s81 = Encoding.UTF8.GetBytes("#!/bin/sh\necho ccs\n"); var sha = ConsoleOps.Sha(s81);
                const string list = "\n# auto-generated\nrm -f \"$rootfs/./uninstall\" \"$rootfs/./etc/init.d/S81clover-mcp\"\nrmdir --ignore-fail-on-non-empty \"$rootfs/./etc/init.d\" \"$rootfs/./etc\"\n";
                var s = new ScriptedShell();
                s.Answer = (c, i) => c.Contains("cat > \"$d/ccs-home.hmod\"") ? R(0, ConsoleOps.Sha(i) + "  /tmp/ccs/hmod-x/ccs-home.hmod\n") : c.StartsWith("hakchi pack_install") ? R(0, "installing ccs-home...\npackage ccs-home installed\n")
                    : c.StartsWith("u=") ? R(0, "present\n" + list) : c.StartsWith("for p in") ? R(0, sha + "  /var/lib/hakchi/rootfs/etc/init.d/S81clover-mcp\n") : R(0, "package ccs-home uninstalled\n");
                var ops = new ConsoleOps(s, null);
                var r = ops.ModuleInstall(A("name", "ccs-home", "files", A("/etc/init.d/S81clover-mcp", A("b64", Convert.ToBase64String(s81), "mode", "0755", "sha256", sha))));
                T.True(Regex.IsMatch(s.Commands[0], "^set -C; r='/tmp/ccs'; d='/tmp/ccs/hmod-[0-9a-f]{8}'; if \\[ -L \"\\$r\" \\]; then exit 4; fi; if \\[ ! -d \"\\$r\" \\]; then mkdir \"\\$r\" \\|\\| exit 4; fi; if \\[ -e \"\\$d\" \\] \\|\\| \\[ -L \"\\$d\" \\]; then exit 3; fi; mkdir \"\\$d\" && cat > \"\\$d/ccs-home.hmod\" && sha256sum \"\\$d/ccs-home.hmod\"$"), "transfer: " + s.Commands[0]);
                var dir = Regex.Match(s.Commands[0], "/tmp/ccs/hmod-[0-9a-f]{8}").Value;
                T.Eq("hakchi pack_install '" + dir + "/ccs-home.hmod'; r=$?; rm -rf '" + dir + "'; exit $r", s.Commands[1], "pack_install");
                T.True(T.Same(Hmod.Build(new SortedDictionary<string, KeyValuePair<byte[], int>> { ["/etc/init.d/S81clover-mcp"] = new KeyValuePair<byte[], int>(s81, 0x1ED) }), s.Inputs[0]), "the module archive was streamed");
                T.True(((string)r["uninstall_list"]).Contains("S81clover-mcp"), "uninstall list returned"); T.True(((string)r["output"]).Contains("installed"), "output");
                var mf = ops.ModuleFiles("ccs-home"); var files = (Dictionary<string, object>)mf["files"];
                T.Eq(1, files.Count, "one file"); T.Eq(sha, files["/etc/init.d/S81clover-mcp"] as string, "its hash"); T.Eq(true, (bool)mf["present"], "present");
                T.True(((List<object>)mf["ignored"]).SequenceEqual(new object[] { "/uninstall" }), "pack_install's own uninstall entry ignored");
                T.Eq("for p in '/var/lib/hakchi/rootfs/etc/init.d/S81clover-mcp'; do if [ -f \"$p\" ] && [ ! -L \"$p\" ]; then sha256sum \"$p\" || echo \"- $p\"; else echo \"- $p\"; fi; done", s.Commands.Last(), "hash of the overlay file");
                ops.ModuleUninstall("ccs-home"); T.Eq("hakchi pack_uninstall 'ccs-home'", s.Commands.Last(), "pack_uninstall");
                s.Answer = (c, i) => c.StartsWith("u=") ? R(0, "present\nrm -f \"$rootfs/./etc/init.d/S81clover-mcp\" \"$rootfs/./etc/passwd\"\n") : R(0, sha + "  /var/lib/hakchi/rootfs/etc/init.d/S81clover-mcp\n");
                mf = ops.ModuleFiles("ccs-home"); files = (Dictionary<string, object>)mf["files"];
                T.True(files.ContainsKey("/etc/passwd") && files["/etc/passwd"] == null && ((List<object>)mf["unreadable"]).Contains("/etc/passwd"), "a foreign path in the list is reported, never read");
                s.Answer = (c, i) => R(0, "absent\n"); mf = ops.ModuleFiles("ccs-home"); T.Eq(false, (bool)mf["present"], "absent"); T.Eq(0, ((Dictionary<string, object>)mf["files"]).Count, "no files");
                T.Eq("bad-args", T.Code(() => ops.ModuleUninstall("clovercon"), "another module"), "only ccs-home");
                T.Eq("path-refused", T.Code(() => ops.ModuleInstall(A("name", "ccs-home", "files", A("/etc/passwd", A("b64", "AA==", "mode", "0644", "sha256", ConsoleOps.Sha(new byte[1]))))), "other file"), "only S81");
                s.Answer = (c, i) => c.Contains("cat > ") ? R(0, H1 + "  x\n") : R(0);
                T.Eq("readback-mismatch", T.Code(() => ops.ModuleInstall(A("name", "ccs-home", "files", A("/etc/init.d/S81clover-mcp", A("b64", Convert.ToBase64String(s81), "mode", "0755", "sha256", sha)))), "damaged"), "a damaged transfer is not installed");
                T.True(!s.Commands.Last().StartsWith("hakchi pack_install"), "pack_install not run after a damaged transfer");
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
