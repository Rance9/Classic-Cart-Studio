// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The strict ZIP reader for Console Composer's files: a Shared HOME Design (.ccshome) someone sent, and a private restore package. The
// console backup keeps its own reader (Archive/ZipReader: fixed member names, at most 256 members); these formats need limits per format
// and a named reason for every hostile archive, so the interface can say exactly why a file was refused. Everything below is decided
// from the archive's index and local headers BEFORE any member is decompressed or decoded:
//  - limits: archive bytes, member count, each member's declared size (per name), the total declared size, and for compressed members
//    the ratio of declared to compressed size (decompression bombs). Reading a member stops at its declared size and checks its CRC-32.
//  - names: not empty, UTF-8 only when the archive says so (no guessed code pages; no second "Unicode path" name in an extra field), no
//    control characters, no absolute, drive, UNC or device paths (CON, NUL, COM1, ...), no alternate data streams (':'), no backslash,
//    no '.' or '..' segment, no trailing dot or space, no folder entries; two names that are the same, or the same ignoring case, or the
//    same after Unicode compatibility normalization, are refused (zip.duplicate / zip.collision); then the format's own allowlist.
//  - members: no encryption, no data descriptors, stored or deflate only, no links or reparse points (Unix file type, DOS attributes),
//    no comment; members tile the file from offset 0 to the index with no gap, overlap or trailing byte; each local header agrees with
//    the index (name bytes, flags, method, CRC-32, sizes).
// Writing uses the backup's ZipWriter (stored or deflate, ASCII names, regular files only).
using CcsBackup.Archive;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace CcsBackup.Compose
{
    public sealed class ZipLimits
    {
        public string What = "file";
        public int MaxMembers = 64, MaxRatio = 100;
        public long MaxArchiveBytes = 64000000, MaxTotalBytes = 64000000;
        public Func<string, bool> Allowed = n => true;
        public Func<string, long> MaxMemberBytes = n => 1000000;
        public Func<string, bool> MustBeStored = n => false;
    }

    public sealed class SafeZip : IDisposable
    {
        readonly Stream s; readonly bool own; readonly ZipLimits lim;
        public IReadOnlyList<ZipEntryInfo> Entries { get; private set; }

        SafeZip(Stream seekable, ZipLimits limits, bool ownStream) { s = seekable; lim = limits; own = ownStream; }

        public static SafeZip Open(string path, ZipLimits limits)
        {
            var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            try { return Open(f, limits, true); } catch { f.Dispose(); throw; }
        }

        public static SafeZip Open(Stream seekable, ZipLimits limits, bool ownStream = false)
        {
            if (!seekable.CanSeek || !seekable.CanRead) throw new ComposeException("zip.not-zip", "the file cannot be read from start to end");
            var z = new SafeZip(seekable, limits ?? new ZipLimits(), ownStream);
            z.Entries = z.Parse();
            return z;
        }

        ComposeException E(string code, string m) => new ComposeException(code, "This " + lim.What + " was refused: " + m + ".");
        static ushort U16(byte[] b, int o) => (ushort)(b[o] | b[o + 1] << 8);
        static uint U32(byte[] b, int o) => (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24);
        static long U64(byte[] b, int o) => (long)U32(b, o) | (long)U32(b, o + 4) << 32;

        byte[] ReadAt(long pos, int n)
        {
            if (pos < 0 || n < 0 || pos + n > s.Length) throw E("zip.damaged", "a ZIP record lies outside the file");
            var b = new byte[n]; s.Position = pos; int got = 0;
            while (got < n) { int r = s.Read(b, got, n - got); if (r <= 0) throw E("zip.damaged", "the file ended inside a ZIP record"); got += r; }
            return b;
        }

        sealed class Raw { public ZipEntryInfo E; public byte[] NameBytes; public bool UnicodeExtra; }

        List<ZipEntryInfo> Parse()
        {
            long len = s.Length;
            if (len > lim.MaxArchiveBytes) throw E("zip.too-large", "it is " + Units.Size(len) + "; at most " + Units.Size(lim.MaxArchiveBytes) + " is accepted");
            if (len < 22) throw E("zip.not-zip", "it is not a ZIP file");
            int tail = (int)Math.Min(len, 22 + 65535);
            var t = ReadAt(len - tail, tail);
            int eocd = tail - 22;
            if (U32(t, eocd) != 0x06054b50 || U16(t, eocd + 20) != 0)
            {
                for (int k = tail - 22; k >= 0; k--) if (U32(t, k) == 0x06054b50) throw E("zip.trailing", "bytes follow the end of the archive, or it has a comment");
                throw E("zip.not-zip", "it is not a ZIP file");
            }
            long eocdPos = len - 22;
            if (U16(t, eocd + 4) != 0 || U16(t, eocd + 6) != 0) throw E("zip.multi", "a multi-part archive is not supported");
            long count = U16(t, eocd + 10), cdSize = U32(t, eocd + 12), cdOff = U32(t, eocd + 16), cdEnd = eocdPos;
            if (U16(t, eocd + 8) != count) throw E("zip.damaged", "its member counts disagree");
            if (count == 0xFFFF || cdSize == 0xFFFFFFFF || cdOff == 0xFFFFFFFF)
            {
                if (eocdPos < 20) throw E("zip.damaged", "the ZIP64 locator is missing");
                var loc = ReadAt(eocdPos - 20, 20);
                if (U32(loc, 0) != 0x07064b50 || U32(loc, 4) != 0 || U32(loc, 16) != 1) throw E("zip.damaged", "the ZIP64 locator is missing or names another disk");
                long recPos = U64(loc, 8);
                var rec = ReadAt(recPos, 56);
                if (U32(rec, 0) != 0x06064b50 || U64(rec, 4) != 44 || U32(rec, 16) != 0 || U32(rec, 20) != 0) throw E("zip.damaged", "the ZIP64 end record is not valid");
                if (recPos + 56 != eocdPos - 20) throw E("zip.gap", "bytes lie between the ZIP64 records");
                count = U64(rec, 32); cdSize = U64(rec, 40); cdOff = U64(rec, 48); cdEnd = recPos;
                if (U64(rec, 24) != count) throw E("zip.damaged", "its ZIP64 member counts disagree");
            }
            if (count > lim.MaxMembers) throw E("zip.count", "it has " + count + " members; at most " + lim.MaxMembers + " are accepted");
            if (cdSize > 16 * 1000 * 1000) throw E("zip.index", "its member index is too large");
            if (cdOff < 0 || cdOff + cdSize != cdEnd) throw E("zip.gap", "bytes lie between the member index and the end of the archive");
            var cd = ReadAt(cdOff, (int)cdSize);
            var raws = new List<Raw>(); int p = 0;
            for (long k = 0; k < count; k++)
            {
                if (p + 46 > cd.Length || U32(cd, p) != 0x02014b50) throw E("zip.damaged", "its member index is damaged");
                var e = new ZipEntryInfo
                {
                    MadeBy = U16(cd, p + 4), Flags = U16(cd, p + 8), Method = U16(cd, p + 10), Crc = U32(cd, p + 16),
                    CompressedSize = U32(cd, p + 20), Size = U32(cd, p + 24), ExternalAttributes = U32(cd, p + 38), HeaderOffset = U32(cd, p + 42)
                };
                int nl = U16(cd, p + 28), xl = U16(cd, p + 30), cl = U16(cd, p + 32);
                if (U16(cd, p + 6) > 45) throw E("zip.version", "a member needs a newer ZIP version than supported");
                if (U16(cd, p + 34) != 0) throw E("zip.multi", "a member names another disk");
                if (p + 46 + nl + xl + cl > cd.Length) throw E("zip.damaged", "its member index is damaged");
                if (cl != 0) throw E("zip.comment", "a member has a comment");
                var r = new Raw { E = e, NameBytes = new byte[nl] }; Buffer.BlockCopy(cd, p + 46, r.NameBytes, 0, nl);
                int xp = p + 46 + nl, xe = xp + xl;
                while (xp + 4 <= xe)
                {
                    int tag = U16(cd, xp), sz = U16(cd, xp + 2); int q = xp + 4;
                    if (q + sz > xe) throw E("zip.damaged", "a member's extra field is damaged");
                    if (tag == 0x7075) r.UnicodeExtra = true;
                    if (tag == 1)
                    {
                        if (e.Size == 0xFFFFFFFF) { if (q + 8 > xp + 4 + sz) throw E("zip.damaged", "a ZIP64 size is missing"); e.Size = U64(cd, q); q += 8; }
                        if (e.CompressedSize == 0xFFFFFFFF) { if (q + 8 > xp + 4 + sz) throw E("zip.damaged", "a ZIP64 size is missing"); e.CompressedSize = U64(cd, q); q += 8; }
                        if (e.HeaderOffset == 0xFFFFFFFF) { if (q + 8 > xp + 4 + sz) throw E("zip.damaged", "a ZIP64 offset is missing"); e.HeaderOffset = U64(cd, q); q += 8; }
                    }
                    xp += 4 + sz;
                }
                if (e.Size == 0xFFFFFFFF || e.CompressedSize == 0xFFFFFFFF || e.HeaderOffset == 0xFFFFFFFF || e.Size < 0 || e.CompressedSize < 0 || e.HeaderOffset < 0) throw E("zip.damaged", "a ZIP64 value is missing");
                p += 46 + nl + xl + cl;
                raws.Add(r);
            }
            if (p != cd.Length) throw E("zip.damaged", "its member index has extra bytes");

            // 1. each member on its own: kind, method, name
            foreach (var r in raws)
            {
                var e = r.E;
                if ((e.Flags & 0x0001) != 0 || (e.Flags & 0x0040) != 0) throw E("zip.encrypted", "it has an encrypted member");
                if ((e.Flags & 0x0008) != 0) throw E("zip.descriptor", "a member uses a data descriptor (not a format Classic Cart Studio writes)");
                if (e.Method != 0 && e.Method != 8) throw E("zip.method", "a member uses an unsupported compression method");
                if (e.Method == 0 && e.Size != e.CompressedSize) throw E("zip.damaged", "a stored member's sizes disagree");
                if (r.UnicodeExtra) throw E("zip.name-extra", "a member carries a second name in an extra field");
                bool utf8 = (e.Flags & 0x0800) != 0;
                if (!utf8 && r.NameBytes.Any(c => c >= 0x80)) throw E("zip.name-encoding", "a member name is not marked as UTF-8");
                try { e.Name = new UTF8Encoding(false, true).GetString(r.NameBytes); }
                catch (ArgumentException) { throw E("zip.name-encoding", "a member name is not valid UTF-8"); }
                var (code, why) = NameProblem(e.Name);
                if (code != null) throw E(code, "member \"" + Display.Line(e.Name, 80) + "\" " + why);
                int host = e.MadeBy >> 8; uint unixType = (e.ExternalAttributes >> 16) & 0xF000;
                if (host == 3 && unixType == 0x4000 || (e.ExternalAttributes & 0x10) != 0) throw E("zip.folder", "member \"" + Display.Line(e.Name, 80) + "\" is a folder entry");
                if (host == 3 && unixType != 0 && unixType != 0x8000) throw E("zip.link", "member \"" + Display.Line(e.Name, 80) + "\" is a link or a special file, not a regular file");
                if ((e.ExternalAttributes & 0x400) != 0) throw E("zip.link", "member \"" + Display.Line(e.Name, 80) + "\" is a reparse point (a link), not a regular file");
            }
            // 2. two names that a file system or a person could confuse
            var exact = new HashSet<string>(StringComparer.Ordinal); var folded = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in raws)
            {
                var n = r.E.Name;
                if (!exact.Add(n)) throw E("zip.duplicate", "two members are named \"" + Display.Line(n, 80) + "\"");
                var key = Fold(n);
                if (folded.TryGetValue(key, out var other)) throw E("zip.collision", "members \"" + Display.Line(other, 80) + "\" and \"" + Display.Line(n, 80) + "\" have the same name ignoring case or Unicode form");
                folded[key] = n;
            }
            // 3. the format's own list of members
            foreach (var r in raws) if (!lim.Allowed(r.E.Name)) throw E("zip.unlisted", "member \"" + Display.Line(r.E.Name, 80) + "\" is not part of a " + lim.What);
            // 4. sizes, before anything is decompressed
            long total = 0;
            foreach (var r in raws)
            {
                var e = r.E; long max = lim.MaxMemberBytes(e.Name);
                if (e.Size > max) throw E("zip.size", "member \"" + e.Name + "\" would be " + Units.Size(e.Size) + "; at most " + Units.Size(max) + " is accepted");
                if (e.Method == 8 && (e.CompressedSize == 0 ? e.Size > 0 : e.Size / e.CompressedSize > lim.MaxRatio))
                    throw E("zip.ratio", "member \"" + e.Name + "\" expands more than " + lim.MaxRatio + " times (a decompression bomb)");
                if (e.Method == 8 && lim.MustBeStored(e.Name)) throw E("zip.method", "member \"" + e.Name + "\" must be stored, not compressed");
                total += e.Size;
                if (total > lim.MaxTotalBytes) throw E("zip.total", "its members would expand to more than " + Units.Size(lim.MaxTotalBytes));
            }
            // 5. local headers agree with the index; members tile the file from offset 0 to the index
            long expect = 0;
            foreach (var r in raws.OrderBy(x => x.E.HeaderOffset))
            {
                var e = r.E;
                if (e.HeaderOffset != expect) throw E(e.HeaderOffset < expect ? "zip.overlap" : "zip.gap", e.HeaderOffset < expect ? "two members overlap" : "unexplained bytes lie before a member");
                var lh = ReadAt(e.HeaderOffset, 30);
                if (U32(lh, 0) != 0x04034b50) throw E("zip.damaged", "a member's local header is missing");
                int nl = U16(lh, 26), xl = U16(lh, 28);
                if (U16(lh, 6) != e.Flags || U16(lh, 8) != e.Method) throw E("zip.mismatch", "a member's local header disagrees with the index");
                var ln = ReadAt(e.HeaderOffset + 30, nl + xl);
                if (nl != r.NameBytes.Length || !ln.Take(nl).SequenceEqual(r.NameBytes)) throw E("zip.mismatch", "a member's local name disagrees with the index");
                long lcs = U32(lh, 18), lus = U32(lh, 22);
                int xp = nl;
                while (xp + 4 <= nl + xl)
                {
                    int tag = U16(ln, xp), sz = U16(ln, xp + 2);
                    if (xp + 4 + sz > nl + xl) throw E("zip.damaged", "a member's local extra field is damaged");
                    if (tag == 0x7075) throw E("zip.name-extra", "a member carries a second name in an extra field");
                    if (tag == 1 && sz >= 16 && (lcs == 0xFFFFFFFF || lus == 0xFFFFFFFF)) { lus = U64(ln, xp + 4); lcs = U64(ln, xp + 12); }
                    xp += 4 + sz;
                }
                if (U32(lh, 14) != e.Crc || lcs != e.CompressedSize || lus != e.Size) throw E("zip.mismatch", "a member's local sizes or checksum disagree with the index (" + e.Name + ")");
                e.DataOffset = e.HeaderOffset + 30 + nl + xl;
                expect = e.DataOffset + e.CompressedSize;
            }
            if (expect != cdOff) throw E(expect < cdOff ? "zip.gap" : "zip.overlap", expect < cdOff ? "unexplained bytes lie before the member index" : "a member overlaps the member index");
            return raws.Select(r => r.E).ToList();
        }

        static readonly HashSet<string> Devices = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "CLOCK$",
            "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
            "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
        };

        /// <summary>(code, reason) when a member name is unsafe on any system it could be unpacked on; (null, null) when it is safe</summary>
        public static (string code, string why) NameProblem(string n)
        {
            if (string.IsNullOrEmpty(n)) return ("zip.name", "has no name");
            if (Encoding.UTF8.GetByteCount(n) > 255) return ("zip.name", "has a name that is too long");
            // (a name decoded from strict UTF-8 has no unpaired surrogate) control characters, and invisible formatting such as a
            // right-to-left override that would show "gpj.exe" as "exe.jpg"
            if (n.Any(c => c < 0x20 || c == 0x7F || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format))
                return ("zip.name", "has a control or invisible formatting character in its name");
            if (n.StartsWith("//") || n.StartsWith("\\\\") || n.StartsWith("/\\") || n.StartsWith("\\/")) return ("zip.unc", "is a network (UNC) path");
            if (n.Length >= 2 && n[1] == ':' && (n[0] >= 'A' && n[0] <= 'Z' || n[0] >= 'a' && n[0] <= 'z')) return ("zip.drive", "is a path on a drive");
            if (n[0] == '/' || n[0] == '\\') return ("zip.absolute", "is an absolute path");
            if (n.Contains('\\')) return ("zip.backslash", "uses a backslash");
            if (n.Contains(':')) return ("zip.stream", "names an alternate data stream (':')");
            if (n.EndsWith("/")) return ("zip.folder", "is a folder entry");
            foreach (var seg in n.Split('/'))
            {
                if (seg == "..") return ("zip.traversal", "leads outside the archive's folder ('..')");
                if (seg == "." || seg.Length == 0) return ("zip.path", "has an empty or '.' path part");
                var stem = seg.Split('.')[0].TrimEnd(' ');
                if (Devices.Contains(stem)) return ("zip.device", "names a Windows device (" + stem + ")");
                if (seg.EndsWith(".") || seg.EndsWith(" ")) return ("zip.path", "has a path part that ends with a dot or a space");
                if (seg.IndexOfAny(new[] { '<', '>', '"', '|', '?', '*' }) >= 0) return ("zip.path", "has a character Windows does not allow in a name");
            }
            return (null, null);
        }

        static string Fold(string n)
        {
            string k;
            try { k = n.Normalize(NormalizationForm.FormKC); } catch (ArgumentException) { k = n; }
            return k.ToUpperInvariant();
        }

        public ZipEntryInfo Find(string name) => Entries.FirstOrDefault(e => e.Name == name);

        /// <summary>the member's bytes (at most max), CRC-32 checked; a member larger than max is refused before anything is read</summary>
        public byte[] ReadAll(ZipEntryInfo e, long max)
        {
            if (e.Size > max || e.Size > int.MaxValue) throw E("zip.size", "member \"" + e.Name + "\" is larger than " + Units.Size(max));
            var b = new byte[e.Size];
            try
            {
                using (var m = OpenMember(e))
                {
                    int got = 0;
                    while (got < b.Length) { int r = m.Read(b, got, b.Length - got); if (r <= 0) throw new IOException("member " + e.Name + " ends early"); got += r; }
                    if (m.Read(new byte[1], 0, 1) != 0) throw new IOException("member " + e.Name + " holds more data than it declares");
                }
            }
            catch (IOException ex) { throw E("zip.damaged", ex.Message); }
            catch (InvalidDataException ex) { throw E("zip.damaged", "member " + e.Name + " cannot be decompressed (" + ex.Message + ")"); }
            return b;
        }

        /// <summary>the member's bytes as a stream: never more than its declared size; ends with a CRC-32 check (an IOException)</summary>
        public Stream OpenMember(ZipEntryInfo e)
        {
            Stream raw = new SubStream(s, e.DataOffset, e.CompressedSize);
            if (e.Method == 8) raw = new DeflateStream(raw, CompressionMode.Decompress);
            return new Checked(raw, e.Size, e.Crc, e.Name);
        }

        /// <summary>a stored member as a seekable window of the file (random access without extracting it); its CRC is not checked here</summary>
        public Stream StoredWindow(ZipEntryInfo e)
        {
            if (e.Method != 0) throw E("zip.method", "member " + e.Name + " is compressed; it cannot be opened in place");
            return new SubStream(s, e.DataOffset, e.Size);
        }

        public void Dispose() { if (own) s.Dispose(); }

        sealed class Checked : Stream
        {
            readonly Stream inner; readonly long size; readonly uint crc; readonly string name; readonly Crc32 c = new Crc32(); long pos; bool ended;
            public Checked(Stream i, long size, uint crc, string name) { inner = i; this.size = size; this.crc = crc; this.name = name; }
            public override int Read(byte[] b, int o, int n)
            {
                if (pos >= size) { End(); return 0; }
                if (n > size - pos) n = (int)(size - pos);
                int got = inner.Read(b, o, n);
                if (got <= 0) throw new IOException("member " + name + " ends before its declared size");
                c.Update(b, o, got); pos += got;
                if (pos == size) End();
                return got;
            }
            void End()
            {
                if (ended) return; ended = true;
                if (inner.Read(new byte[1], 0, 1) > 0) throw new IOException("member " + name + " holds more data than it declares");
                if (c.Value != crc) throw new IOException("member " + name + " fails its CRC-32 check");
            }
            public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
            public override long Length => size; public override long Position { get => pos; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int n) => throw new NotSupportedException();
            protected override void Dispose(bool d) { if (d) inner.Dispose(); base.Dispose(d); }
        }
    }
}
