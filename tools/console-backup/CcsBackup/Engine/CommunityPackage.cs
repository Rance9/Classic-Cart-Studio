// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Community Mods: admitting an author's package (a Hakchi .hmod: a gzip-compressed tar archive, which hakchi unpacks on the console with
// tar -xzf and installs with pack_install).
//   Pin       the file must be exactly the approved one: its size and SHA-256 as the catalog record names them. It comes from the author's
//             release address (downloaded only into a folder the person chose) or from a file the person already has on this PC; any
//             other bytes are refused and never become a new approved checksum.
//   Archive   read on this PC, never unpacked to disk: every member's name is checked (no absolute path, no "..", no "." or empty part, no
//             back slash, no Windows device name or character Windows cannot hold, no two names that differ only in case, nothing written
//             through a link); links only relative and inside the package; no hard link to anything but an earlier file of the package;
//             no device, FIFO or other special member; at most 20,000 members and the record's unpacked size; no package inside the package;
//             no file of a kind that holds a game or a console's system file; every installed file inside the places the record says the
//             package writes. Its own scripts (install, uninstall, transfer) are named for the review: hakchi runs them with full control
//             of the console. Checking the pin proves the file is the approved one, not that the package is safe: it does not limit what
//             the package's own code can do.
//   Choice    which entries a change adds, updates and removes, in one plan: dependencies selected with their reason, the variant for the
//             installed and chosen combination, conflicts refused, a base never removed under a package that needs it, the packages an
//             author's install removes by itself listed, and the places hakchi installs them in order (a package whose dependency is
//             added in the same change waits in the transfer folder's hmod/ folder, which hakchi installs after the transfer folder).
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace CcsBackup.Engine
{
    public sealed class HmodMember { public string Path, Type, Link, Sha256; public long Size; }

    public sealed class HmodInventory
    {
        public readonly List<HmodMember> Members = new List<HmodMember>();
        public long UnpackedBytes; public string Problem;
        /// <summary>the package's own scripts hakchi runs (install, uninstall, transfer: top level)</summary>
        public readonly List<string> Scripts = new List<string>();
        public bool Ok => Problem == null;
        /// <summary>the files hakchi copies into its root overlay (not the scripts or the top-level read-me files hakchi deletes first)</summary>
        public IEnumerable<HmodMember> Payload => Members.Where(m => m.Type != "dir" && !CommunityArchive.IsControl(m.Path));
        /// <summary>a digest of the payload files (path and SHA-256), as the console later reads them back</summary>
        public string PayloadDigest()
        {
            var sb = new StringBuilder("ccs-hmod-payload/1\n");
            foreach (var m in Payload.Where(x => x.Type == "file").OrderBy(x => x.Path, StringComparer.Ordinal)) sb.Append(m.Sha256).Append(' ').Append(m.Path).Append('\n');
            using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }
    }

    public static class CommunityArchive
    {
        public const int MaxMembers = 20000, MaxName = 255, MaxDepth = 24;
        public const long MaxUnpacked = 1000000000;
        static readonly string[] Devices = { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        /// <summary>kinds of file that hold a game or a console's system file: never inside a listed package</summary>
        static readonly string[] GameKinds = { ".nes", ".fds", ".unf", ".unif", ".qd", ".sfc", ".smc", ".fig", ".swc", ".gb", ".gbc", ".gba", ".n64", ".z64", ".v64", ".nds", ".gen", ".smd", ".sms", ".gg", ".pce", ".a26", ".iso", ".cue", ".chd", ".cdi", ".gdi", ".wad", ".hsqs" };
        static readonly string[] SystemNames = { "disksys.rom", "kernel.img", "boot.img", "uboot.bin", "u-boot.bin", "boot0.bin", "boot1.bin" };

        /// <summary>a top-level file hakchi does not copy into its root overlay: its scripts and the read-me files it deletes first</summary>
        public static bool IsControl(string p)
        {
            if (p.Contains("/")) return false;
            var l = p.ToLowerInvariant();
            return l == "install" || l == "uninstall" || l == "transfer" || l == "readme" || l.StartsWith("readme.", StringComparison.Ordinal) || l.EndsWith(".txt", StringComparison.Ordinal) || l.EndsWith(".md", StringComparison.Ordinal);
        }

        /// <summary>one name part, as Windows and the console can both hold it; null or why not</summary>
        public static string PartProblem(string s)
        {
            if (s.Length == 0) return "an empty name part";
            if (s == "." || s == "..") return "a \"" + s + "\" name part";
            foreach (var ch in s) if (ch < ' ' || ch == '\u007f' || "<>:\"|?*\\".IndexOf(ch) >= 0) return "a character Windows cannot hold in a name";
            if (s.EndsWith(".") || s.EndsWith(" ")) return "a name ending in a dot or a space";
            var b = s.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            if (Devices.Contains(b)) return "a Windows device name (" + s + ")";
            return null;
        }

        /// <summary>reads the package (gzip, then tar) from stream: every member checked as above; record: the places it may write (null:
        /// names and kinds only); maxUnpacked: the record's unpacked size</summary>
        public static HmodInventory Inspect(Stream gz, CommunityRecord record = null, long maxUnpacked = MaxUnpacked)
        {
            var inv = new HmodInventory();
            HmodInventory Bad(string why) { inv.Problem = why; return inv; }
            var head = new byte[2]; if (gz.Read(head, 0, 2) != 2 || head[0] != 0x1f || head[1] != 0x8b) return Bad("it is not a Hakchi package (a .hmod is a gzip-compressed archive)");
            gz.Position = 0;
            var seen = new Dictionary<string, HmodMember>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var z = new GZipStream(gz, CompressionMode.Decompress, true))
                {
                    var t = new Bounded(z, Math.Min(maxUnpacked, MaxUnpacked) * 2 + 64L * 1024 * 1024);
                    var hdr = new byte[512]; string longName = null, longLink = null; Dictionary<string, string> pax = null; int zeros = 0;
                    while (true)
                    {
                        if (!Full(t, hdr)) return Bad("the archive ends in the middle of a member");
                        if (hdr.All(x => x == 0)) { if (++zeros == 2) break; continue; }
                        if (zeros > 0) return Bad("the archive has an empty block in the middle");
                        long sum = 0; for (int i = 0; i < 512; i++) sum += i >= 148 && i < 156 ? 32 : hdr[i];
                        if (Octal(hdr, 148, 8) != sum) return Bad("a member's header is damaged (its checksum differs)");
                        char type = (char)hdr[156]; long size = Octal(hdr, 124, 12);
                        if (size < 0) return Bad("a member's size cannot be read");
                        if (type == 'L' || type == 'K' || type == 'x' || type == 'g')
                        {
                            if (size > 65536) return Bad("a member's extended header is too large");
                            var data = Read(t, size); if (data == null) return Bad("the archive ends in an extended header");
                            if (type == 'L') longName = Encoding.UTF8.GetString(data).TrimEnd('\0');
                            else if (type == 'K') longLink = Encoding.UTF8.GetString(data).TrimEnd('\0');
                            else
                            {
                                var p = Pax(data); if (p == null) return Bad("an extended header cannot be read");
                                if (type == 'g') { if (p.ContainsKey("path") || p.ContainsKey("linkpath") || p.ContainsKey("size")) return Bad("a global extended header changes names or sizes"); }
                                else pax = p;
                            }
                            continue;
                        }
                        var name = longName ?? (Cstr(hdr, 345, 155).Length > 0 && Cstr(hdr, 257, 6).StartsWith("ustar") ? Cstr(hdr, 345, 155) + "/" + Cstr(hdr, 0, 100) : Cstr(hdr, 0, 100));
                        var link = longLink ?? Cstr(hdr, 157, 100);
                        if (pax != null) { if (pax.TryGetValue("path", out var pp)) name = pp; if (pax.TryGetValue("linkpath", out var pl)) link = pl; if (pax.TryGetValue("size", out var ps)) { if (!long.TryParse(ps, NumberStyles.None, CultureInfo.InvariantCulture, out size)) return Bad("an extended header's size cannot be read"); } }
                        longName = longLink = null; pax = null;
                        string kind = type == '0' || type == '\0' || type == '7' ? "file" : type == '5' ? "dir" : type == '2' ? "link" : type == '1' ? "hardlink" : null;
                        if (kind == null) return Bad("it holds a member that is not a file, folder or link (" + Show(name) + ")");
                        if (kind != "file" && kind != "hardlink" && size != 0) return Bad("a folder or link carries data (" + Show(name) + ")");
                        // the name: "./" in front is hakchi's tar's own form; the archive's root folder itself is skipped
                        if (Encoding.UTF8.GetByteCount(name) > MaxName * 4) return Bad("a member's name is too long");
                        var n = name; while (n.StartsWith("./", StringComparison.Ordinal)) n = n.Substring(2);
                        if (kind == "dir") n = n.TrimEnd('/');
                        if (n.Length == 0 || n == ".") { if (kind == "dir") { Skip(t, size); continue; } return Bad("a file without a name"); }
                        if (n.StartsWith("/") || n.Contains("\\") || (n.Length > 1 && n[1] == ':')) return Bad("a member's name is an absolute path (" + Show(name) + ")");
                        var parts = n.Split('/');
                        if (parts.Length > MaxDepth) return Bad("a member is too deep in folders (" + Show(name) + ")");
                        foreach (var part in parts) { var why = PartProblem(part); if (why != null) return Bad("a member's name has " + why + " (" + Show(name) + ")"); }
                        if (Encoding.UTF8.GetByteCount(n) > MaxName) return Bad("a member's name is too long (" + Show(name) + ")");
                        if (seen.TryGetValue(n, out var dup)) { if (dup.Type == "dir" && kind == "dir") { Skip(t, size); continue; } return Bad(dup.Path == n ? "a name appears twice (" + Show(n) + ")" : "two names differ only in upper and lower case (" + Show(dup.Path) + ", " + Show(n) + ")"); }
                        // nothing is written through a link or a file: every folder on the way is a folder (or not named yet)
                        for (int i = 1; i < parts.Length; i++)
                        {
                            var up = string.Join("/", parts.Take(i));
                            if (seen.TryGetValue(up, out var u) && u.Type != "dir") return Bad("a member is written through a link or a file (" + Show(n) + ")");
                        }
                        if (Path.GetFileName(n).EndsWith(".hmod", StringComparison.OrdinalIgnoreCase)) return Bad("it carries another package inside it (" + Show(n) + ")");
                        var ext = Path.GetExtension(n).ToLowerInvariant(); var file = Path.GetFileName(n).ToLowerInvariant();
                        if (kind == "file" && (GameKinds.Contains(ext) || SystemNames.Contains(file))) return Bad("it carries a file of a kind that holds a game or a console's system file (" + Show(n) + ")");
                        var m = new HmodMember { Path = n, Type = kind, Size = kind == "file" ? size : 0 };
                        if (kind == "link")
                        {
                            if (link.Length == 0 || link.StartsWith("/") || link.Contains("\\") || link.Contains(":")) return Bad("a link points outside the package (" + Show(n) + " -> " + Show(link) + ")");
                            var depth = parts.Length - 1;
                            foreach (var lp in link.Split('/')) { if (lp == "..") { if (--depth < 0) return Bad("a link points outside the package (" + Show(n) + " -> " + Show(link) + ")"); } else if (lp != "." && lp.Length > 0) depth++; }
                            m.Link = link;
                        }
                        if (kind == "hardlink")
                        {
                            var target = link; while (target.StartsWith("./", StringComparison.Ordinal)) target = target.Substring(2);
                            if (!seen.TryGetValue(target, out var tm) || tm.Type != "file" || tm.Path != target) return Bad("a hard link names something that is not an earlier file of the package (" + Show(n) + ")");
                            m.Link = target; m.Sha256 = tm.Sha256; m.Size = 0;
                        }
                        if (kind == "file")
                        {
                            if (inv.UnpackedBytes + size > maxUnpacked) return Bad("it unpacks to more than the approved " + Compose.Units.Size(maxUnpacked));
                            using (var h = SHA256.Create())
                            {
                                var buf = new byte[1 << 16]; long left = size;
                                while (left > 0) { int got = t.Read(buf, 0, (int)Math.Min(buf.Length, left)); if (got <= 0) return Bad("the archive ends in the middle of a file"); h.TransformBlock(buf, 0, got, null, 0); left -= got; }
                                h.TransformFinalBlock(new byte[0], 0, 0); m.Sha256 = Hex.Of(h.Hash);
                            }
                            long pad = (512 - size % 512) % 512; Skip(t, pad);
                            inv.UnpackedBytes += size;
                        }
                        seen[n] = m; inv.Members.Add(m);
                        if (inv.Members.Count > MaxMembers) return Bad("it has more than " + MaxMembers.ToString("N0", CultureInfo.InvariantCulture) + " members");
                    }
                }
            }
            catch (InvalidDataException) { return Bad("the archive is damaged (it cannot be decompressed)"); }
            catch (TooLargeException) { return Bad("it unpacks to more than the approved size"); }
            catch (IOException ex) { return Bad("it could not be read (" + ex.Message + ")"); }
            // its own scripts, and what hakchi's uninstall would refuse to do
            foreach (var s in new[] { "install", "uninstall", "transfer" }) if (inv.Members.Any(m => m.Path == s && m.Type == "file")) inv.Scripts.Add(s);
            if (inv.Members.Any(m => IsControl(m.Path) && m.Type != "file")) return Bad("a script or read-me name at the top is not a plain file");
            // every installed file and link inside the places the record names
            if (record != null)
            {
                foreach (var m in inv.Payload)
                    if (!Covered(m.Path, record.PayloadPaths)) return Bad("it installs " + Show(m.Path) + ", outside the places its record says it writes");
                foreach (var m in inv.Members.Where(x => x.Type == "dir"))
                    if (!Covered(m.Path, record.PayloadPaths) && !record.PayloadPaths.Any(p => p.StartsWith(m.Path + "/", StringComparison.Ordinal))) return Bad("it has the folder " + Show(m.Path) + ", outside the places its record says it writes");
            }
            return inv;
        }

        /// <summary>a path inside the record's places: a place ending in "/" is a folder (everything in it), otherwise one exact file</summary>
        public static bool Covered(string path, IEnumerable<string> places) => places.Any(p => p.EndsWith("/") ? path.StartsWith(p, StringComparison.Ordinal) || path == p.TrimEnd('/') : path == p);

        /// <summary>the uninstall script says hakchi may not remove the package ("no-uninstall")</summary>
        public static bool NoUninstall(Stream gz)
        {
            // read again only for the one script (small): kept apart from Inspect, which hashes and never keeps file bytes
            gz.Position = 0;
            using (var z = new GZipStream(gz, CompressionMode.Decompress, true))
            {
                var t = new Bounded(z, MaxUnpacked * 2); var hdr = new byte[512];
                while (Full(t, hdr) && !hdr.All(x => x == 0))
                {
                    long size = Octal(hdr, 124, 12); var name = Cstr(hdr, 0, 100); while (name.StartsWith("./", StringComparison.Ordinal)) name = name.Substring(2);
                    char type = (char)hdr[156];
                    if ((type == '0' || type == '\0') && name == "uninstall" && size <= 1000000)
                    {
                        var data = Read(t, size); return data != null && Encoding.UTF8.GetString(data).Replace("\r", "").Split('\n').Any(l => l == "no-uninstall");
                    }
                    Skip(t, size + (512 - size % 512) % 512);
                }
            }
            return false;
        }

        // ---- tar helpers
        sealed class TooLargeException : Exception { }
        sealed class Bounded : Stream
        {
            readonly Stream inner; long left;
            public Bounded(Stream s, long max) { inner = s; left = max; }
            public override int Read(byte[] b, int o, int c) { int n = inner.Read(b, o, c); left -= n; if (left < 0) throw new TooLargeException(); return n; }
            public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { } public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        }
        static bool Full(Stream s, byte[] b) { int n = 0, k; while (n < b.Length && (k = s.Read(b, n, b.Length - n)) > 0) n += k; return n == b.Length; }
        static byte[] Read(Stream s, long size)
        {
            var b = new byte[size]; if (!Full(s, b)) return null;
            long pad = (512 - size % 512) % 512; Skip(s, pad); return b;
        }
        static void Skip(Stream s, long n) { var buf = new byte[4096]; while (n > 0) { int k = s.Read(buf, 0, (int)Math.Min(buf.Length, n)); if (k <= 0) throw new IOException("the archive ends early"); n -= k; } }
        static long Octal(byte[] h, int at, int len)
        {
            if ((h[at] & 0x80) != 0) { long v = h[at] & 0x7f; for (int i = 1; i < len; i++) v = (v << 8) | h[at + i]; return v; }   // GNU base-256
            long r = 0; bool any = false;
            for (int i = at; i < at + len; i++) { byte c = h[i]; if (c == 0 || c == ' ') { if (any) break; continue; } if (c < '0' || c > '7') return -1; r = r * 8 + (c - '0'); any = true; }
            return r;
        }
        static string Cstr(byte[] h, int at, int len) { int n = 0; while (n < len && h[at + n] != 0) n++; return Encoding.UTF8.GetString(h, at, n); }
        static Dictionary<string, string> Pax(byte[] d)
        {
            var r = new Dictionary<string, string>(); int p = 0;
            while (p < d.Length && d[p] != 0)
            {
                int sp = Array.IndexOf(d, (byte)' ', p); if (sp < 0) return null;
                if (!int.TryParse(Encoding.ASCII.GetString(d, p, sp - p), NumberStyles.None, CultureInfo.InvariantCulture, out var len) || len <= 0 || p + len > d.Length) return null;
                var kv = Encoding.UTF8.GetString(d, sp + 1, p + len - sp - 2); int eq = kv.IndexOf('='); if (eq <= 0) return null;
                r[kv.Substring(0, eq)] = kv.Substring(eq + 1); p += len;
            }
            return r;
        }
        static string Show(string s) => s.Length > 120 ? s.Substring(0, 120) + "…" : s;
    }

    /// <summary>a package file admitted for one variant of one entry: exactly the approved bytes, and an archive that passed every check</summary>
    public sealed class AdmittedPackage
    {
        public CommunityRecord Record; public CommunityVariant Variant; public string File; public HmodInventory Inventory; public DateTime WriteUtc;
        public JObj ToJson() => new JObj { { "id", Record.Id }, { "name", Record.Name }, { "version", Record.Version }, { "variant", Variant.Variant }, { "package", Variant.Package }, { "file", File },
            { "bytes", Variant.Bytes }, { "sha256", Variant.Sha256 }, { "unpacked_bytes", Inventory.UnpackedBytes }, { "scripts", Inventory.Scripts.Cast<object>().ToList() }, { "payload_digest", Inventory.PayloadDigest() } };
    }

    public static class CommunityAdmission
    {
        public const string NoFileText = "Connect to download this mod, or choose the approved file already on your PC.";

        /// <summary>the file must be exactly this variant's approved release file, and its archive must pass every check: the admitted
        /// package, or null and why not, in plain words</summary>
        public static (AdmittedPackage pkg, string problem) Admit(CommunityRecord rec, CommunityVariant v, string file)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return (null, NoFileText);
            try
            {
                using (var f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (f.Length != v.Bytes) return (null, "The chosen file is not the approved " + rec.Name + " " + rec.Version + " file: it has " + f.Length.ToString("N0", CultureInfo.InvariantCulture) + " bytes, the approved file " + v.Bytes.ToString("N0", CultureInfo.InvariantCulture) + ". Nothing was installed.");
                    string sha; using (var h = SHA256.Create()) sha = Hex.Of(h.ComputeHash(f));
                    if (sha != v.Sha256) return (null, "The chosen file is not the approved " + rec.Name + " " + rec.Version + " file: its fingerprint (SHA-256) differs. A different file is never accepted in its place. Nothing was installed.");
                    f.Position = 0;
                    var inv = CommunityArchive.Inspect(f, rec, v.UnpackedBytes);
                    if (!inv.Ok) return (null, "The approved " + rec.Name + " file cannot be installed: " + inv.Problem + ". Nothing was installed.");
                    if (inv.Scripts.Contains("uninstall") && CommunityArchive.NoUninstall(f)) return (null, "The approved " + rec.Name + " file says hakchi may never remove it, so Workbench does not install it. Nothing was installed.");
                    return (new AdmittedPackage { Record = rec, Variant = v, File = Path.GetFullPath(file), Inventory = inv, WriteUtc = System.IO.File.GetLastWriteTimeUtc(file) }, null);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return (null, "The chosen file could not be read (" + ex.Message + "). Nothing was installed."); }
        }

        /// <summary>the same file read again at the moment of writing: still exactly the admitted bytes</summary>
        public static string Recheck(AdmittedPackage p)
        {
            var (again, why) = Admit(p.Record, p.Variant, p.File);
            if (again == null) return why;
            return again.Inventory.PayloadDigest() == p.Inventory.PayloadDigest() ? null : "The package file changed after the review.";
        }
    }

    /// <summary>a permitted picture: used only when its bytes are the approved ones</summary>
    public static class CommunityAsset
    {
        public static string Check(byte[] b, CommunityShot s)
        {
            if (b == null || b.LongLength != s.Bytes) return "the picture is not the approved one (its size differs)";
            using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(b)) == s.Sha256 ? null : "the picture is not the approved one (its fingerprint differs)";
        }
    }

    /// <summary>downloading an approved release file from the author's address into a folder the person chose (never a hidden cache): the
    /// bytes go to a temporary name, are checked against the pin and only then get the release file's name; anything else is deleted and
    /// refused, never accepted as a new checksum</summary>
    public static class CommunityDownload
    {
        /// <summary>opens the address for reading (the product: HTTPS through the system's web stack; tests: their own source)</summary>
        public static Func<string, Stream> Open = url =>
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var rq = (HttpWebRequest)WebRequest.Create(url); rq.AllowAutoRedirect = true; rq.MaximumAutomaticRedirections = 5; rq.Timeout = 60000; rq.ReadWriteTimeout = 60000;
            rq.UserAgent = "CCS-Console-Workbench";
            var rs = (HttpWebResponse)rq.GetResponse();
            if (rs.ResponseUri.Scheme != Uri.UriSchemeHttps) { rs.Close(); throw new IOException("the address was redirected away from https"); }
            return rs.GetResponseStream();
        };

        public static (string file, string problem) Fetch(CommunityRecord rec, CommunityVariant v, string folder, Action<long, long> progress = null, Func<bool> cancelled = null)
        {
            if (string.IsNullOrEmpty(folder) || !Path.IsPathRooted(folder) || !Directory.Exists(folder)) return (null, "Choose a folder on this PC for the downloaded file.");
            var final = Path.Combine(folder, v.FileName); var part = final + ".part";
            if (File.Exists(final))
            {
                var (have, _) = CommunityAdmission.Admit(rec, v, final);
                if (have != null) return (final, null);   // the approved file is already there (an earlier download)
                return (null, "A different file named " + v.FileName + " is already in that folder. Choose another folder, or move that file away.");
            }
            try
            {
                using (var src = Open(v.Url))
                using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var h = SHA256.Create())
                {
                    var buf = new byte[1 << 16]; long n = 0; int k;
                    while ((k = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (cancelled != null && cancelled()) throw new OperationCanceledException();
                        n += k; if (n > v.Bytes) throw new IOException("the author's address sent more bytes than the approved file has");
                        dst.Write(buf, 0, k); h.TransformBlock(buf, 0, k, null, 0); progress?.Invoke(n, v.Bytes);
                    }
                    h.TransformFinalBlock(new byte[0], 0, 0);
                    if (n != v.Bytes || Hex.Of(h.Hash) != v.Sha256) throw new IOException(n != v.Bytes ? "the download has " + n.ToString("N0", CultureInfo.InvariantCulture) + " bytes, the approved file " + v.Bytes.ToString("N0", CultureInfo.InvariantCulture) : "the downloaded file's fingerprint (SHA-256) is not the approved one");
                    dst.Flush(true);
                }
                File.Move(part, final);
                return (final, null);
            }
            catch (OperationCanceledException) { TryDelete(part); return (null, "The download was canceled. Nothing was kept."); }
            catch (Exception ex) when (ex is IOException || ex is WebException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is UriFormatException)
            {
                TryDelete(part);
                return (null, "The file at the author's address is not the approved " + rec.Name + " " + rec.Version + " file, or it could not be downloaded (" + ex.Message + "). The partial download was deleted and nothing was installed. A changed file is never accepted in place of the approved one.");
            }
        }
        static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
