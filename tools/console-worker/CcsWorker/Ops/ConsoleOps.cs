// Classic Cart Studio console transport worker. GPL-3.0-or-later.
// The console operations K3 needs, each one fixed shell text over IConsoleShell with checked, single-quoted paths. Only applets the
// console's own stock BusyBox 1.24.1 has are used (its mv has no -T, and "mv a b" moves a INTO b when b resolves to a folder: no
// operation ever renames onto an existing path, and the pointer's mv -f replaces a regular file it checked in the same command);
// "hakchi" only for identification and hakchi's own module install/uninstall.
// identify follows Hakchi2 CE 3.9.3 hakchi.cs Shell_OnConnected (commit 25bfcfd; GPL-3.0): the same commands, the same version
// minimums and the same CanInteract rule. Modified by Rance9, 2026: the running kernel is read from /proc (the stock BusyBox has no
// uname); nothing is cached or changed on the console.
using CcsWorker.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsWorker.Ops
{
    public sealed class ConsoleOps
    {
        public const int CommandTimeoutMs = 20000;      // the longest silence a command may keep
        public const int SimpleTimeoutMs = 2000;        // upstream ExecuteSimple
        public const int ModuleTimeoutMs = 60000;
        public const int ProgressStep = 256 * 1000;
        const int MaxCommand = 32000;

        readonly IConsoleShell sh;
        readonly string packageRoot;
        readonly Func<bool> cancelled;
        readonly Action<long, long> progress;

        public ConsoleOps(IConsoleShell shell, string packageRoot, Func<bool> cancelled = null, Action<long, long> progress = null)
        {
            sh = shell; this.packageRoot = packageRoot; this.cancelled = cancelled ?? (() => false); this.progress = progress ?? ((a, b) => { });
        }

        /// <summary>tests only: the method packages' pins (the shipped worker always uses ConsolePath.MethodPackages)</summary>
        internal IDictionary<string, string> MethodPins = ConsolePath.MethodPackages;

        static string Q(string p) => ConsolePath.Q(p);

        sealed class Run
        {
            public int Code; public byte[] Out; public string Err; public bool Overflow;
            public string Text => Encoding.UTF8.GetString(Out);
        }

        Run Exec(string command, Stream stdin = null, int maxOut = 1 << 20, int timeoutMs = CommandTimeoutMs)
        {
            var o = new BoundedStream(maxOut); var e = new BoundedStream(8192);
            int code = sh.Execute(command, stdin, o, e, timeoutMs);
            return new Run { Code = code, Out = o.ToArray(), Err = Encoding.UTF8.GetString(e.ToArray()).Trim(), Overflow = o.Overflow };
        }

        string Simple(string command) => Encoding.UTF8.GetString(ExecBytes(command)).Trim();
        byte[] ExecBytes(string command) { var r = Exec(command, null, 65536, SimpleTimeoutMs); return r.Out; }
        void CheckCancel() { if (cancelled()) throw new OperationCanceledException("cancelled between commands"); }

        // the real folders from the write root down to the path's parent: none may be a link (writing through one would land elsewhere)
        static string Chain(string path)
        {
            var root = ConsolePath.RootOf(path); var parent = ConsolePath.Parent(path); var list = new List<string>();
            if (root == null || path == root) list.Add(parent);
            else for (var d = parent; ; d = ConsolePath.Parent(d)) { list.Insert(0, d); if (d == root) break; }
            return "for q in " + string.Join(" ", list.Select(Q)) + "; do if [ -L \"$q\" ] || [ ! -d \"$q\" ]; then echo 'parent-not-folder' >&2; exit 4; fi; done; ";
        }

        static OpException Fail(Run r, string code, string what) => new OpException(code, what + (r.Err.Length > 0 ? " (" + r.Err + ")" : ""));
        static bool Hex64(string s) => s != null && s.Length == 64 && s.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');

        // ---------------------------------------------------------------- identification (hakchi.cs Shell_OnConnected)
        public Dictionary<string, object> Identify()
        {
            var r = new Dictionary<string, object>();
            r["md_partitioning"] = Exec("hakchi ismdPartitioning", null, 65536, SimpleTimeoutMs).Code == 0; CheckCancel();
            bool memboot = Exec("source /hakchi/config; [ \"$cf_memboot\" = \"y\" ]", null, 65536, SimpleTimeoutMs).Code == 0; r["minimal_memboot"] = memboot; CheckCancel();
            r["hwid"] = Simple("hakchi hwid").Replace(" ", ""); CheckCancel();
            r["kernel"] = Simple("cat /proc/sys/kernel/osrelease"); CheckCancel();
            string boot = null, kernelVersion = null, script = null, system = null; bool? custom = null, canInteract = null, versionFile = null;
            if (!memboot)
            {
                versionFile = Simple("[ -f /var/version ] && echo \"yes\"") == "yes"; CheckCancel();
                if (versionFile == true)
                {
                    var v = Exec("source /var/version && echo \"$bootVersion $kernelVersion $hakchiVersion\"", null, 65536, SimpleTimeoutMs);
                    if (v.Code != 0) throw Fail(v, "shell-error", "/var/version could not be read");
                    var parts = v.Text.Trim().Split(' ');
                    if (parts.Length < 3) throw new OpException("shell-error", "/var/version did not give three versions");
                    boot = parts[0]; kernelVersion = parts[1]; script = parts[2];
                }
                else { boot = "1.0.0"; kernelVersion = "3.4.112-00"; script = "v1.0.0-000"; }   // upstream: "severely outdated"
                canInteract = versionFile == true && !(Version(kernelVersion) < new Version(3, 4, 113) || Version(boot) < new Version(1, 0, 2)) && !(Version(script) < new Version(1, 0, 4, 118));
                CheckCancel();
                if (canInteract == true)
                {
                    system = Simple("hakchi eval 'echo \"$sftype-$sfregion\"'"); CheckCancel();
                    custom = Simple("hakchi currentFirmware") != "_nand_";
                }
            }
            r["system"] = system; r["boot_version"] = boot; r["kernel_version"] = kernelVersion; r["hakchi_version"] = script; r["script_version"] = script;
            r["custom_firmware"] = custom; r["can_interact"] = canInteract; r["version_file"] = versionFile;
            return r;
        }

        public static Version Version(string ver)   // hakchi.cs ConvertVersion
        {
            var m = Regex.Match(ver ?? "", @"(?:\d+[\.-]){2,3}(?:\d+)+");
            return m.Success ? new Version(m.Value.Replace("-", ".")) : new Version(0, 0, 0, 0);
        }

        // ---------------------------------------------------------------- reading
        public Dictionary<string, object> Df(string path)
        {
            path = ConsolePath.Read(path);
            var r = Exec("df -k " + Q(path), null, 65536);
            if (r.Code != 0) throw Fail(r, "shell-error", "df failed");
            var lines = r.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (lines.Count < 2) throw new OpException("shell-error", "df gave no figures");
            var tok = string.Join(" ", lines.Skip(1)).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);   // BusyBox wraps a long device name
            if (tok.Length < 4 || !long.TryParse(tok[1], out var blocks) || !long.TryParse(tok[3], out var avail)) throw new OpException("shell-error", "df's figures could not be read");
            return new Dictionary<string, object> { ["free_bytes"] = avail * 1024, ["total_bytes"] = blocks * 1024 };
        }

        public Dictionary<string, object> Stat(string path)
        {
            path = ConsolePath.Read(path);
            var r = Exec("p=" + Q(path) + "; if [ -L \"$p\" ]; then echo link; readlink \"$p\"; elif [ -d \"$p\" ]; then echo dir; elif [ -f \"$p\" ]; then echo file; wc -c < \"$p\"; elif [ -e \"$p\" ]; then echo other; else echo none; fi", null, 65536);
            var lines = r.Text.Split('\n');
            var type = lines[0].Trim();
            if (r.Code != 0 || !new[] { "link", "dir", "file", "other", "none" }.Contains(type)) throw Fail(r, "shell-error", "stat failed");
            var d = new Dictionary<string, object> { ["type"] = type == "dir" ? "dir" : type };
            if (type == "file" && lines.Length > 1 && long.TryParse(lines[1].Trim(), out var size)) d["size"] = size;
            if (type == "link" && lines.Length > 1) d["target"] = lines[1].TrimEnd('\r');
            return d;
        }

        public Dictionary<string, object> List(string path)
        {
            path = ConsolePath.Read(path);
            var r = Exec("p=" + Q(path) + "; if [ -L \"$p\" ]; then echo not-a-folder; exit 3; elif [ -d \"$p\" ]; then echo folder; ls -1a \"$p\"; elif [ -e \"$p\" ]; then echo not-a-folder; exit 3; else echo none; fi", null, 4 << 20);
            var lines = r.Text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            if (r.Code == 3) throw new OpException("not-a-folder", path + " is not a folder");
            if (r.Code != 0 || r.Overflow) throw Fail(r, r.Overflow ? "too-many" : "shell-error", "listing failed");
            if (lines[0] == "none") return new Dictionary<string, object> { ["names"] = new List<object>(), ["exists"] = false };
            var names = lines.Skip(1).Where(n => n.Length > 0 && n != "." && n != "..").ToList();
            if (names.Count > 4096) throw new OpException("too-many", "more than 4,096 entries in " + path);
            return new Dictionary<string, object> { ["names"] = names.Cast<object>().ToList(), ["exists"] = true };
        }

        /// <summary>Classic Cart Studio's whole data area as it is now, read only, for the backup prerequisite (Admission.SameCcsData): every
        /// folder, every file's SHA-256 and every link's target under /var/lib/ccs, in the backup inventory's terms ("/ccs/..."). The
        /// stock BusyBox has no find: a shell function walks the folders (links are never followed). Anything that cannot be read or told
        /// apart exactly (a name with a tab or a line break, an unfinished listing) is a Problem, which refuses the change.</summary>
        public CcsBackup.Engine.LiveTree Tree()
        {
            const string root = "/var/lib/ccs", data = "/var/lib";
            var r = Exec("w(){ local d=\"$1\" n; for n in \"$d\"/* \"$d\"/.[!.]* \"$d\"/..?*; do [ -e \"$n\" ] || [ -L \"$n\" ] || continue; " +
                         "if [ -L \"$n\" ]; then printf 'l\\t%s\\t%s\\n' \"$n\" \"$(readlink \"$n\")\"; elif [ -d \"$n\" ]; then printf 'd\\t%s\\n' \"$n\"; w \"$n\" || return 1; " +
                         "elif [ -f \"$n\" ]; then set -- $(sha256sum \"$n\") || return 1; printf 'f\\t%s\\t%s\\n' \"$n\" \"$1\"; else printf 'o\\t%s\\n' \"$n\"; fi; done; }; " +
                         "R=" + Q(root) + "; if [ -L \"$R\" ]; then echo link; elif [ -d \"$R\" ]; then echo tree; printf 'd\\t%s\\n' \"$R\"; w \"$R\" && echo end; else echo none; fi", null, 32 << 20, 600000);
            if (r.Overflow) return CcsBackup.Engine.LiveTree.Unreadable("more than 32 MB of listing");
            if (r.Code != 0) return CcsBackup.Engine.LiveTree.Unreadable("the listing ended with status " + r.Code);
            var lines = r.Text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
            var t = new CcsBackup.Engine.LiveTree();
            if (lines.Count == 0) return CcsBackup.Engine.LiveTree.Unreadable("no answer");
            if (lines[0] == "none" && lines.Count == 1) return t;   // no Classic Cart Studio data on the console (nothing to lose)
            if (lines[0] == "link") return CcsBackup.Engine.LiveTree.Unreadable(root + " is a link");
            if (lines[0] != "tree" || lines[lines.Count - 1] != "end") return CcsBackup.Engine.LiveTree.Unreadable("the listing did not finish");
            foreach (var l in lines.Skip(1).Take(lines.Count - 2))
            {
                var f = l.Split('\t');
                var p = f.Length > 1 ? f[1] : null;
                if (p == null || !(p == root || p.StartsWith(root + "/", StringComparison.Ordinal))) return CcsBackup.Engine.LiveTree.Unreadable("an entry could not be told apart: " + Show(l));
                var key = p.Substring(data.Length);
                string v = f[0] == "d" && f.Length == 2 ? "d" : f[0] == "o" && f.Length == 2 ? "o" : f[0] == "l" && f.Length == 3 ? "l:" + f[2] : f[0] == "f" && f.Length == 3 && Hex64(f[2]) ? "f:" + f[2] : null;
                if (v == null || t.Entries.ContainsKey(key)) return CcsBackup.Engine.LiveTree.Unreadable("an entry could not be told apart: " + Show(l));
                t.Entries[key] = v;
            }
            return t;
        }
        static string Show(string l) => l.Length > 80 ? l.Substring(0, 80) + "..." : l;

        public Dictionary<string, object> Sha256(IList<object> paths)
        {
            if (paths == null || paths.Count == 0 || paths.Count > 256) throw new OpException("bad-args", "paths: 1 to 256 console paths");
            var list = paths.Select(ConsolePath.Read).ToList();
            var hashes = new Dictionary<string, object>();
            var batch = new List<string>(); int len = 0;
            void Flush()
            {
                if (batch.Count == 0) return; CheckCancel();
                var r = Exec("for p in " + string.Join(" ", batch.Select(Q)) + "; do if [ -f \"$p\" ] && [ ! -L \"$p\" ]; then sha256sum \"$p\" || echo \"- $p\"; else echo \"- $p\"; fi; done", null, 1 << 20, 120000);
                if (r.Code != 0) throw Fail(r, "shell-error", "sha256sum failed");
                foreach (var line in r.Text.Split('\n'))
                {
                    var t = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (t.Length < 2) continue;
                    var p = t[t.Length - 1];
                    if (batch.Contains(p)) hashes[p] = Hex64(t[0]) ? t[0] : null;
                }
                foreach (var p in batch) if (!hashes.ContainsKey(p)) throw new OpException("shell-error", "no answer for " + p);
                batch.Clear(); len = 0;
            }
            foreach (var p in list) { if (len + p.Length + 3 > MaxCommand) Flush(); batch.Add(p); len += p.Length + 3; }
            Flush();
            return new Dictionary<string, object> { ["hashes"] = hashes };
        }

        public Dictionary<string, object> Read(string path, long maxBytes)
        {
            path = ConsolePath.Read(path);
            if (maxBytes < 0 || maxBytes > 1000000) throw new OpException("bad-args", "max_bytes: 0 to 1,000,000");
            var r = Exec("p=" + Q(path) + "; if [ -f \"$p\" ] && [ ! -L \"$p\" ]; then n=$(wc -c < \"$p\"); if [ \"$n\" -gt " + maxBytes + " ]; then echo \"$n\" >&2; exit 4; fi; cat \"$p\"; else exit 3; fi", null, (int)maxBytes + 1);
            if (r.Code == 3) throw new OpException("not-a-file", path + " is not a regular file");
            if (r.Code == 4 || r.Overflow) throw new OpException("too-large", path + " is larger than " + maxBytes + " bytes");
            if (r.Code != 0) throw Fail(r, "shell-error", "reading failed");
            return new Dictionary<string, object> { ["b64"] = Convert.ToBase64String(r.Out), ["size"] = (long)r.Out.Length };
        }

        // chunked reads (a file of any size, a bounded piece at a time): the size as wc counts it in the same command, then BusyBox dd's
        // whole 4 KB blocks from the offset. A file that changes between two pieces is the caller's to detect (size, then SHA-256)
        public const int ChunkUnit = 4096, ChunkMax = 262144;
        public Dictionary<string, object> ReadChunk(string path, long offset, long length)
        {
            path = ConsolePath.Read(path);
            if (offset < 0 || offset % ChunkUnit != 0 || offset > 64L * 1000 * 1000 * 1000) throw new OpException("bad-args", "offset: 0 or more, a multiple of 4,096");
            if (length < ChunkUnit || length > ChunkMax || length % ChunkUnit != 0) throw new OpException("bad-args", "length: 4,096 to 262,144, a multiple of 4,096");
            var r = Exec("p=" + Q(path) + "; if [ -f \"$p\" ] && [ ! -L \"$p\" ]; then wc -c < \"$p\" && dd if=\"$p\" bs=" + ChunkUnit + " skip=" + offset / ChunkUnit + " count=" + length / ChunkUnit + " 2>/dev/null; else exit 3; fi", null, (int)length + 64);
            if (r.Code == 3) throw new OpException("not-a-file", path + " is not a regular file");
            if (r.Code != 0) throw Fail(r, "shell-error", "reading failed");
            int nl = Array.IndexOf(r.Out, (byte)'\n');
            if (nl < 0 || nl > 40 || !long.TryParse(Encoding.ASCII.GetString(r.Out, 0, nl).Trim(), out var size) || size < 0) throw new OpException("shell-error", "the file's size could not be read");
            var data = new byte[r.Out.Length - nl - 1]; Buffer.BlockCopy(r.Out, nl + 1, data, 0, data.Length);
            if (r.Overflow || data.Length > length) throw new OpException("shell-error", "more bytes than asked for came back");
            return new Dictionary<string, object> { ["b64"] = Convert.ToBase64String(data), ["size"] = (long)data.Length, ["file_size"] = size, ["offset"] = offset, ["eof"] = offset + data.Length >= size };
        }

        // the console's own sync: written data reaches storage before the next step relies on it. drop_caches=1 then drops the clean page
        // cache (the only /proc write; fixed text) so that a following sha256 reads from storage rather than from memory. Neither is a
        // power-loss proof: it is the strongest ordering the console's tools offer
        public Dictionary<string, object> Sync(bool dropCaches)
        {
            var r = Exec("sync || exit 5" + (dropCaches ? "; echo 1 > /proc/sys/vm/drop_caches || exit 6" : ""), null, 4096, ModuleTimeoutMs);
            if (r.Code == 5) throw Fail(r, "sync-failed", "sync failed");
            if (r.Code != 0 && r.Code != 6) throw Fail(r, "shell-error", "sync failed");
            var d = new Dictionary<string, object> { ["synced"] = true, ["dropped"] = dropCaches && r.Code == 0 };
            if (r.Code == 6) d["drop_error"] = r.Err.Length > 0 ? Bound(r.Err, 200) : "the page cache was not dropped";
            return d;
        }

        // ---------------------------------------------------------------- writing
        byte[] Source(Dictionary<string, object> a, out string what)
        {
            if (a.TryGetValue("b64", out var b) && b is string s)
            {
                if (a.ContainsKey("source")) throw new OpException("bad-args", "give source or b64, not both");
                try { what = "data"; return Convert.FromBase64String(s); } catch (FormatException) { throw new OpException("bad-args", "b64 is not base64"); }
            }
            if (a.TryGetValue("source", out var src) && src is string rel)
            {
                if (packageRoot == null) throw new OpException("no-package", "no package folder was given to this worker");
                if (rel.Length == 0 || rel.Length > 400 || rel.StartsWith("/") || rel.Contains("\\") || rel.Contains(":") || rel.Split('/').Any(c => c.Length == 0 || c == "." || c == ".."))
                    throw new OpException("path-refused", "source must be a plain relative path inside the package");
                var root = Path.GetFullPath(packageRoot).TrimEnd('\\') + "\\";
                var full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', '\\')));
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new OpException("path-refused", "source is outside the package");
                if (!File.Exists(full)) throw new OpException("not-found", "the package has no " + rel);
                if (new FileInfo(full).Length > 64L * 1000 * 1000) throw new OpException("too-large", rel + " is larger than 64 MB");
                what = rel; return File.ReadAllBytes(full);
            }
            throw new OpException("bad-args", "source or b64 is required");
        }

        static string Mode(object o)
        {
            var m = o as string;
            if (m != "0644" && m != "0755") throw new OpException("bad-args", "mode: 0644 or 0755");
            return m.Substring(1);
        }

        public Dictionary<string, object> Put(Dictionary<string, object> a)
        {
            var path = ConsolePath.Write(a.TryGetValue("path", out var p) ? p : null);
            var mode = Mode(a.TryGetValue("mode", out var m) ? m : null);
            var want = a.TryGetValue("sha256", out var h) ? h as string : null;
            if (!Hex64(want)) throw new OpException("bad-args", "sha256: 64 lowercase hex digits");
            var data = Source(a, out var what);
            if (Sha(data) != want) throw new OpException("source-mismatch", what + " is not the expected bytes (its SHA-256 differs)");
            var guard = Chain(path) + "p=" + Q(path) + "; if [ -e \"$p\" ] || [ -L \"$p\" ]; then echo exists >&2; exit 3; fi; ";
            var pre = Exec(guard + "exit 0", null, 4096);
            if (pre.Code == 3) throw new OpException("exists", path + " already exists (nothing was written)");
            if (pre.Code == 4) throw new OpException("parent-not-folder", "a folder on the way to " + path + " is missing or is a link");
            if (pre.Code != 0) throw Fail(pre, "shell-error", "the check before writing failed");
            CheckCancel();
            var input = new SourceStream(data, cancelled, progress);
            var r = Exec("set -C; " + guard + "cat > \"$p\" || exit 5; chmod " + mode + " \"$p\" || exit 6; sha256sum \"$p\"", input, 65536, CommandTimeoutMs);
            if (r.Code == 3) throw new OpException("exists", path + " appeared before it was written (nothing was written)");
            if (r.Code == 4) throw new OpException("parent-not-folder", "a folder on the way to " + path + " is missing or is a link");
            if (r.Code == 5 || r.Code == 6) throw Fail(r, "write-failed", "writing " + path + " failed; a partial file may remain there");
            if (r.Code != 0) throw Fail(r, "shell-error", "writing " + path + " failed");
            var got = r.Text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (input.Stopped) throw new OpException("cancelled", "cancelled after " + input.Sent + " of " + data.Length + " bytes; the partial file stays at " + path);
            return new Dictionary<string, object> { ["sha256"] = got, ["bytes"] = (long)data.Length, ["match"] = got == want };
        }

        public Dictionary<string, object> Mkdir(string path)
        {
            path = ConsolePath.Write(path);
            var r = Exec(Chain(path) + "p=" + Q(path) + "; if [ -e \"$p\" ] || [ -L \"$p\" ]; then echo exists >&2; exit 3; fi; mkdir \"$p\"", null, 4096);
            if (r.Code == 3) throw new OpException("exists", path + " already exists");
            if (r.Code == 4) throw new OpException("parent-not-folder", "a folder on the way to " + path + " is missing or is a link");
            if (r.Code != 0) throw Fail(r, "write-failed", "mkdir failed");
            return new Dictionary<string, object>();
        }

        public Dictionary<string, object> Rmdir(string path)
        {
            path = ConsolePath.Write(path);
            var r = Exec(Chain(path) + "p=" + Q(path) + "; if [ -d \"$p\" ] && [ ! -L \"$p\" ]; then rmdir \"$p\" || exit 5; else echo not-a-folder >&2; exit 3; fi", null, 4096);
            if (r.Code == 3) throw new OpException("not-a-folder", path + " is not a real folder");
            if (r.Code == 4) throw new OpException("parent-not-folder", "a folder on the way to " + path + " is missing or is a link");
            if (r.Code == 5) throw Fail(r, "not-empty", path + " was not removed (not empty)");
            if (r.Code != 0) throw Fail(r, "shell-error", "rmdir failed");
            return new Dictionary<string, object>();
        }

        public Dictionary<string, object> Rename(string from, string to)
        {
            from = ConsolePath.Write(from); to = ConsolePath.Write(to);
            if (ConsolePath.RootOf(from) != ConsolePath.RootOf(to)) throw new OpException("path-refused", "a rename stays inside one place");
            var r = Exec(Chain(from) + Chain(to) + "a=" + Q(from) + "; b=" + Q(to) + "; if [ -L \"$a\" ] || { [ ! -f \"$a\" ] && [ ! -d \"$a\" ]; }; then echo source >&2; exit 3; fi; if [ -e \"$b\" ] || [ -L \"$b\" ]; then echo exists >&2; exit 5; fi; mv \"$a\" \"$b\" || exit 6", null, 4096);
            if (r.Code == 3) throw new OpException("not-found", from + " is not a regular file or real folder");
            if (r.Code == 4) throw new OpException("parent-not-folder", "a folder on the way is missing or is a link");
            if (r.Code == 5) throw new OpException("exists", to + " already exists (nothing was renamed)");
            if (r.Code != 0) throw Fail(r, "write-failed", "rename failed");
            return new Dictionary<string, object>();
        }

        public Dictionary<string, object> Remove(string path, string sha)
        {
            path = ConsolePath.Write(path);
            if (!Hex64(sha)) throw new OpException("bad-args", "sha256: 64 lowercase hex digits");
            var r = Exec(Chain(path) + "p=" + Q(path) + "; if [ -L \"$p\" ]; then echo link; elif [ -f \"$p\" ]; then set -- $(sha256sum \"$p\"); if [ \"$1\" = '" + sha + "' ]; then rm -f \"$p\" && echo removed; else echo hash-differs; fi; elif [ -e \"$p\" ]; then echo not-a-file; else echo absent; fi", null, 4096);
            if (r.Code == 4) throw new OpException("parent-not-folder", "a folder on the way to " + path + " is missing or is a link");
            var reason = r.Text.Trim();
            if (r.Code != 0 || !new[] { "removed", "hash-differs", "not-a-file", "link", "absent" }.Contains(reason)) throw Fail(r, "shell-error", "remove failed");
            return new Dictionary<string, object> { ["removed"] = reason == "removed", ["reason"] = reason };
        }

        public static readonly Regex PointerValue = new Regex("^[0-9a-f]{16}\\.[0-9a-f]{8}$");
        // expect (optional): "absent" (nothing may be at the path) or the SHA-256 the current pointer file must have, checked in the same
        // command before the replacement; otherwise pointer-changed and nothing is changed
        public Dictionary<string, object> Pointer(string path, string value, string expect = null)
        {
            path = ConsolePath.Write(path);
            if (value == null || !PointerValue.IsMatch(value)) throw new OpException("bad-args", "value: <16 hex>.<8 hex>");
            if (expect != null && expect != "absent" && !Hex64(expect)) throw new OpException("bad-args", "expect: \"absent\" or 64 lowercase hex digits");
            var tmp = path + "." + Rand8() + ".new";
            var check = expect == null ? "" : expect == "absent" ? "if [ -e \"$p\" ] || [ -L \"$p\" ]; then echo pointer-changed >&2; exit 8; fi; "
                : "if [ -L \"$p\" ] || [ ! -f \"$p\" ]; then echo pointer-changed >&2; exit 8; fi; set -- $(sha256sum \"$p\"); if [ \"$1\" != '" + expect + "' ]; then echo pointer-changed >&2; exit 8; fi; ";
            var r = Exec("set -C; " + Chain(path) + "p=" + Q(path) + "; t=" + Q(tmp) + "; " + check + "if [ -L \"$p\" ] || { [ -e \"$p\" ] && [ ! -f \"$p\" ]; }; then echo not-a-pointer >&2; exit 3; fi; if [ -e \"$t\" ] || [ -L \"$t\" ]; then exit 5; fi; printf '%s\\n' '" + value + "' > \"$t\" || exit 6; mv -f \"$t\" \"$p\" || { rm -f \"$t\"; exit 7; }; cat \"$p\"", null, 4096);
            if (r.Code == 8) throw new OpException("pointer-changed", path + " is not what this operation expected (" + (expect == "absent" ? "something is there" : "its content changed") + "); nothing was changed");
            if (r.Code == 3) throw new OpException("not-a-pointer", path + " is a link, a folder or something else (nothing was changed)");
            if (r.Code == 4) throw new OpException("parent-not-folder", "a folder on the way to " + path + " is missing or is a link");
            if (r.Code != 0) throw Fail(r, "write-failed", "the pointer was not written");
            return new Dictionary<string, object> { ["value"] = r.Text.Trim() };
        }

        // ---------------------------------------------------------------- hakchi's own package handling (b0030_package)
        // The install method of the CCS Console Workbench's shared policy (tools/console-nucleus): CCS Mod ("ccs-mod") or CCS Nucleus
        // ("ccs-nucleus"), only the exact builds pinned in ConsolePath.MethodPackages, installed with hakchi's own pack_install (the package's
        // install script checks that every file it would copy is free or its own, hakchi copies them, the script records them and adds the
        // menu entry) and removed with hakchi's pack_uninstall (the package's own removal: only its unchanged files). The earlier start-up
        // module "ccs-home" (one overlay S81clover-mcp, which hakchi's script/removed deletes at every start) is only read and removed.
        bool Method(string name) => name != null && MethodPins.ContainsKey(name);

        public Dictionary<string, object> ModuleInstall(Dictionary<string, object> a)
        {
            var name = a.TryGetValue("name", out var n) ? n as string : null;
            if (!Method(name)) throw new OpException("bad-args", "name: ccs-mod or ccs-nucleus (the earlier ccs-home module is never installed)");
            if (!(a.TryGetValue("hmod", out var hv) && hv is Dictionary<string, object> h)) throw new OpException("bad-args", "hmod: an object");
            var want = h.TryGetValue("sha256", out var hs) ? hs as string : null;
            if (want != MethodPins[name]) throw new OpException("not-pinned", name + ": only the pinned build of the package is installed");
            var hmod = Source(h, out var what);
            if (Sha(hmod) != want) throw new OpException("source-mismatch", what + " is not the pinned package");
            var dir = "/tmp/ccs/hmod-" + Rand8(); var file = dir + "/" + name + ".hmod";
            CheckCancel();
            var t = Exec("set -C; r='/tmp/ccs'; d=" + Q(dir) + "; if [ -L \"$r\" ]; then exit 4; fi; if [ ! -d \"$r\" ]; then mkdir \"$r\" || exit 4; fi; if [ -e \"$d\" ] || [ -L \"$d\" ]; then exit 3; fi; mkdir \"$d\" && cat > \"$d/" + name + ".hmod\" && sha256sum \"$d/" + name + ".hmod\"", new MemoryStream(hmod), 65536);
            if (t.Code != 0) throw Fail(t, "write-failed", "the package could not be sent to the console");
            if (t.Text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() != want) throw new OpException("readback-mismatch", "the package arrived damaged; it was not installed");
            CheckCancel();
            var r = Exec("hakchi pack_install " + Q(file) + "; r=$?; rm -rf " + Q(dir) + "; exit $r", null, 1 << 20, ModuleTimeoutMs);
            var output = Bound(r.Text + (r.Err.Length > 0 ? "\n" + r.Err : ""), 4096);
            if (r.Code != 0) throw new OpException("module-failed", "hakchi pack_install answered " + r.Code + ": " + output);
            // the package's own install script ends with one line "result postinstall ok ..." (or refuses: then hakchi keeps no uninstall list)
            var post = r.Text.Replace("\r", "").Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("result postinstall ", StringComparison.Ordinal) || l.StartsWith("result preinstall ", StringComparison.Ordinal));
            var list = UninstallList(name);
            if (post == null || !post.StartsWith("result postinstall ok", StringComparison.Ordinal) || !list.Item1) throw new OpException("module-failed", name + " was not installed: " + (post ?? output));
            return new Dictionary<string, object> { ["output"] = output, ["postinstall"] = post, ["uninstall_list"] = list.Item2, ["hmod_sha256"] = want };
        }

        public Dictionary<string, object> ModuleUninstall(string name)
        {
            if (name != ConsolePath.ModuleName && !Method(name)) throw new OpException("bad-args", "name: ccs-mod, ccs-nucleus or the earlier ccs-home");
            var r = Exec("hakchi pack_uninstall " + Q(name), null, 1 << 20, ModuleTimeoutMs);
            var output = Bound(r.Text + (r.Err.Length > 0 ? "\n" + r.Err : ""), 4096);
            if (r.Code != 0) throw new OpException("module-failed", "hakchi pack_uninstall answered " + r.Code + ": " + output);
            if (UninstallList(name).Item1) throw new OpException("module-failed", "hakchi still lists " + name + " after its pack_uninstall: " + output);
            return new Dictionary<string, object> { ["output"] = output };
        }

        /// <summary>CCS Mod's optional start at power-on, by CCS Mod's own command on the console ("ccs-mod autostart on|off", tools/console-nucleus
        /// DESIGN.md section 3): on writes its start script from the package's template only where nothing else is and delegates to the
        /// console's own start; off removes it while it is still the one CCS Mod wrote. Only while hakchi lists CCS Mod; the program run is the
        /// one hakchi's overlay holds (the installer checks it is the pinned package's, unchanged, before it asks). Its one result line; anything
        /// but its plain success is an error</summary>
        public Dictionary<string, object> ModStart(string state)
        {
            if (state != "on" && state != "off") throw new OpException("bad-args", "state: on or off");
            if (!UninstallList("ccs-mod").Item1) throw new OpException("not-installed", "CCS Mod is not installed (hakchi does not list it)");
            CheckCancel();
            // CCS_ROOT is empty on a console (the program's own place, less its path): the same text runs in the tests' stand-in folder
            var r = Exec("p='/var/lib/hakchi/rootfs/bin/ccs-mod'; if [ -L \"$p\" ] || [ ! -f \"$p\" ]; then exit 4; fi; CCS_ROOT=\"${p%/var/lib/hakchi/rootfs/bin/ccs-mod}\" sh \"$p\" autostart " + state, null, 65536, ModuleTimeoutMs);
            if (r.Code == 4) throw new OpException("not-a-file", "CCS Mod's program is not a plain file");
            var output = Bound(r.Text + (r.Err.Length > 0 ? "\n" + r.Err : ""), 4096);
            var line = r.Text.Replace("\r", "").Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("result autostart ", StringComparison.Ordinal));
            if (line != "result autostart ok " + state) throw new OpException("module-failed", "CCS Mod's own command answered: " + (line ?? output));
            return new Dictionary<string, object> { ["result"] = line, ["output"] = output };
        }

        Tuple<bool, string> UninstallList(string name)
        {
            var r = Exec("u=" + Q(ConsolePath.UninstallDir + "/uninstall-" + name) + "; if [ -L \"$u\" ]; then echo link; exit 3; elif [ -f \"$u\" ]; then echo present; cat \"$u\"; else echo absent; fi", null, 65536);
            if (r.Code == 3) throw new OpException("not-a-file", "hakchi's uninstall list for " + name + " is a link");
            if (r.Code != 0 || r.Overflow) throw Fail(r, "shell-error", "the uninstall list could not be read");
            var text = r.Text; var nl = text.IndexOf('\n');
            var head = (nl < 0 ? text : text.Substring(0, nl)).Trim();
            return Tuple.Create(head == "present", head == "present" && nl >= 0 ? text.Substring(nl + 1) : "");
        }

        static readonly Regex RmLine = new Regex("\"\\$rootfs/(?:\\./)?([^\"]+)\"");
        static readonly Regex FilesLine = new Regex("^([0-9a-f]{64}) (0[0-7]{3}) ([A-Za-z0-9._/-]{1,200})$");
        /// <summary>the earlier module: the files its uninstall list names, with their SHA-256 now. A method package: hakchi lists it or not, its
        /// VERSION's package id, and every file its own FILES list names (and the list itself), with the SHA-256 each has now (null: missing)</summary>
        public Dictionary<string, object> ModuleFiles(string name)
        {
            if (name != ConsolePath.ModuleName && !Method(name)) throw new OpException("bad-args", "name: ccs-mod, ccs-nucleus or the earlier ccs-home");
            var list = UninstallList(name);
            var files = new Dictionary<string, object>(); var unreadable = new List<object>(); var ignored = new List<object>();
            if (!list.Item1) return new Dictionary<string, object> { ["files"] = files, ["present"] = false };
            if (Method(name))
            {
                var m = name.Substring(4); var etc = ConsolePath.HakchiRootfs + "/etc/ccs/" + m;
                Dictionary<string, object> ReadOr(string q) { try { return Read(q, 65536); } catch (OpException ex) when (ex.Code == "not-a-file") { return new Dictionary<string, object>(); } }
                var ver = ReadOr(etc + "/VERSION"); var fl = ReadOr(etc + "/FILES");
                string package = null;
                if (ver.TryGetValue("b64", out var vb) && vb is string vs)
                    foreach (var l in Encoding.UTF8.GetString(Convert.FromBase64String(vs)).Split('\n')) if (l.StartsWith("package=", StringComparison.Ordinal) && Hex64(l.Substring(8).Trim())) package = l.Substring(8).Trim();
                var paths = new List<string>();
                if (fl.TryGetValue("b64", out var fb) && fb is string fs)
                    foreach (var l in Encoding.UTF8.GetString(Convert.FromBase64String(fs)).Split('\n'))
                    {
                        var fm = FilesLine.Match(l.Trim()); if (!fm.Success) continue;
                        var p = "/" + fm.Groups[3].Value;
                        if (ConsolePath.Plain(p) && (ConsolePath.ReadExact.Contains(ConsolePath.HakchiRootfs + p) || p.StartsWith("/etc/ccs/" + m + "/", StringComparison.Ordinal))) paths.Add(p); else unreadable.Add(p);
                    }
                if (fl.ContainsKey("b64")) paths.Add("/etc/ccs/" + m + "/FILES");
                CheckCancel();
                var hashes = paths.Count > 0 ? (Dictionary<string, object>)Sha256(paths.Select(p => (object)(ConsolePath.HakchiRootfs + p)).ToList())["hashes"] : new Dictionary<string, object>();
                foreach (var p in paths) files[p] = hashes[ConsolePath.HakchiRootfs + p];
                foreach (string p in unreadable) files[p] = null;
                return new Dictionary<string, object> { ["files"] = files, ["present"] = true, ["package"] = package, ["unreadable"] = unreadable };
            }
            var lpaths = new List<string>();
            foreach (var line in list.Item2.Split('\n'))
                if (line.TrimStart().StartsWith("rm -f ", StringComparison.Ordinal))
                    foreach (Match mm in RmLine.Matches(line))
                    {
                        var p = "/" + mm.Groups[1].Value;
                        // pack_install lists its own "uninstall" file (find runs after it is created), which transfer_default never copies
                        if (p == "/uninstall") { if (!ignored.Contains(p)) ignored.Add(p); continue; }
                        if (!lpaths.Contains(p)) lpaths.Add(p);
                    }
            var readable = lpaths.Where(p => ConsolePath.Plain(p) && ConsolePath.ReadExact.Contains(ConsolePath.HakchiRootfs + p)).ToList();
            CheckCancel();
            var lh = readable.Count > 0 ? (Dictionary<string, object>)Sha256(readable.Select(p => (object)(ConsolePath.HakchiRootfs + p)).ToList())["hashes"] : new Dictionary<string, object>();
            foreach (var p in lpaths)
            {
                if (readable.Contains(p)) files[p] = lh[ConsolePath.HakchiRootfs + p];
                else { files[p] = null; unreadable.Add(p); }
            }
            return new Dictionary<string, object> { ["files"] = files, ["present"] = true, ["unreadable"] = unreadable, ["ignored"] = ignored };
        }

        // ---------------------------------------------------------------- helpers
        public static string Sha(byte[] b) { using (var s = SHA256.Create()) return string.Concat(s.ComputeHash(b).Select(x => x.ToString("x2"))); }
        static string Rand8() { var b = new byte[4]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b); return string.Concat(b.Select(x => x.ToString("x2"))); }
        static string Bound(string s, int n) => s.Length <= n ? s.Trim() : s.Substring(0, n) + "...";

        /// <summary>The bytes a put sends: 64 KB at a time, progress every 256 KB, and at a cancel the input simply ends there.</summary>
        sealed class SourceStream : Stream
        {
            readonly byte[] data; readonly Func<bool> cancelled; readonly Action<long, long> progress; long pos, lastReport;
            public bool Stopped; public long Sent => pos;
            public SourceStream(byte[] d, Func<bool> c, Action<long, long> p) { data = d; cancelled = c; progress = p; }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (Stopped || pos >= data.Length) { if (!Stopped && lastReport != pos) { lastReport = pos; progress(pos, data.Length); } return 0; }
                if (cancelled()) { Stopped = true; return 0; }
                int n = (int)Math.Min(Math.Min(count, 65536), data.Length - pos);
                Buffer.BlockCopy(data, (int)pos, buffer, offset, n); pos += n;
                if (pos - lastReport >= ProgressStep || pos == data.Length) { lastReport = pos; progress(pos, data.Length); }
                return n;
            }
            public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
            public override long Length => data.Length; public override long Position { get => pos; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        }
    }

    /// <summary>Keeps at most max bytes; the rest is counted and dropped.</summary>
    public sealed class BoundedStream : Stream
    {
        readonly MemoryStream ms = new MemoryStream(); readonly int max;
        public bool Overflow; public long Total;
        public BoundedStream(int max) { this.max = max; }
        public override void Write(byte[] b, int o, int c)
        {
            lock (ms) { Total += c; int keep = (int)Math.Max(0, Math.Min(c, max - ms.Length)); if (keep > 0) ms.Write(b, o, keep); if (keep < c) Overflow = true; }
        }
        public byte[] ToArray() { lock (ms) return ms.ToArray(); }
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => Total; public override long Position { get => Total; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
    }
}
