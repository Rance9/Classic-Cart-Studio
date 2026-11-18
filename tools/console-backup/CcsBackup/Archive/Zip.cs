// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// A standard ZIP writer and a strict ZIP reader, written for the console backup format (Archive/BackupFormat.cs; FORMAT.md):
//  - ZipWriter streams each member once (stored, or deflate for small text), hashing it as it goes; the local header is written first
//    and its CRC-32 and sizes are filled in afterwards (the file is seekable), so no data descriptor is needed. ZIP64 records are used
//    when a size, an offset or the member count needs them (or always, when asked: the tests use that). Names are UTF-8 (bit 11).
//    Members carry Unix mode 0644 regular-file attributes. Nothing is ever a link or a folder entry. When asked (ChunkSize), each
//    member's stored bytes are also hashed in fixed-size chunks as they stream (the repair map). Reserve places a stored member of a
//    fixed size now and Fill writes its bytes later (the repair map's early descriptor, written last but placed first). LocalHeader
//    and WriteDirectory are the writer's exact rules, shared with the repair code (Repair/), so a rebuilt index follows them too.
//  - ZipReader accepts only what such a writer produces, and refuses everything else BEFORE any member is used: a trailing or leading
//    byte outside the archive, a gap or an overlap between members, two members with one name (ignoring case), an encrypted, linked,
//    folder or data-descriptor member, any method other than stored or deflate, a local header that disagrees with the central
//    directory, more than 256 members, a member name outside what the caller allows. A member is read with a hard stop at its declared
//    size, and its CRC-32 is checked at its end.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace CcsBackup.Archive
{
    public sealed class ZipEntryInfo
    {
        public string Name; public ushort Method; public uint Crc; public long Size, CompressedSize, HeaderOffset, DataOffset;
        public ushort Flags; public uint ExternalAttributes; public ushort MadeBy;
        public bool Zip64Local; public List<string> Chunks;   // the local header carries ZIP64 sizes; the stored bytes' chunk hashes (ZipWriter.ChunkSize)
    }

    public sealed class ZipFormatException : Exception { public ZipFormatException(string m) : base(m) { } }

    public sealed class ZipWriter : IDisposable
    {
        public const uint UnixRegular0644 = (0x8000u | 0x1A4u) << 16;
        public const ushort WriterMadeBy = 0x0300 | 45;
        readonly Stream s; readonly bool forceZip64; readonly ushort dosTime, dosDate;
        readonly List<ZipEntryInfo> entries = new List<ZipEntryInfo>();
        readonly HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<ZipEntryInfo> reserved = new HashSet<ZipEntryInfo>();
        bool open, finished;
        /// <summary>above 0: every member's stored bytes are also hashed in chunks of this many bytes (ZipEntryInfo.Chunks)</summary>
        public int ChunkSize;
        public ushort DosTime => dosTime; public ushort DosDate => dosDate; public bool ForcedZip64 => forceZip64;

        public ZipWriter(Stream seekable, DateTime stamp, bool forceZip64 = false)
        {
            if (!seekable.CanSeek || !seekable.CanWrite) throw new ArgumentException("the ZIP writer needs a seekable, writable file");
            s = seekable; this.forceZip64 = forceZip64;
            var t = stamp.Year < 1980 ? new DateTime(1980, 1, 1) : stamp;
            dosTime = (ushort)(t.Hour << 11 | t.Minute << 5 | t.Second / 2); dosDate = (ushort)((t.Year - 1980) << 9 | t.Month << 5 | t.Day);
        }

        public IReadOnlyList<ZipEntryInfo> Entries => entries;

        /// <summary>Writes one member from whatever write() puts into the given stream (the stream hashes and counts). compress: deflate
        /// (small text only). mayExceed4G: reserves the ZIP64 sizes in the local header. Returns the member's facts and the hashes.</summary>
        public (ZipEntryInfo info, string sha256, string md5) Add(string name, bool compress, Action<Stream> write, bool mayExceed4G = false, Action<long> progress = null, Func<bool> cancelled = null)
        {
            if (finished || open) throw new InvalidOperationException("the ZIP writer is not ready for a member");
            if (!names.Add(name)) throw new InvalidOperationException("a member named " + name + " already exists");
            open = true;
            var nb = Encoding.UTF8.GetBytes(name);
            bool z64local = forceZip64 || mayExceed4G;
            var e = new ZipEntryInfo { Name = name, Method = (ushort)(compress ? 8 : 0), HeaderOffset = s.Position, Flags = 0x0800, ExternalAttributes = UnixRegular0644, MadeBy = WriterMadeBy, Zip64Local = z64local };
            var w = new BinaryWriter(s, Encoding.UTF8, true);
            w.Write(0x04034b50u); w.Write((ushort)(z64local ? 45 : 20)); w.Write(e.Flags); w.Write(e.Method); w.Write(dosTime); w.Write(dosDate);
            w.Write(0u); w.Write(z64local ? 0xFFFFFFFFu : 0u); w.Write(z64local ? 0xFFFFFFFFu : 0u);
            w.Write((ushort)nb.Length); w.Write((ushort)(z64local ? 20 : 0)); w.Write(nb);
            if (z64local) { w.Write((ushort)1); w.Write((ushort)16); w.Write(0L); w.Write(0L); }
            w.Flush();
            e.DataOffset = s.Position;
            var chunks = ChunkSize > 0 ? new ChunkHasher(ChunkSize) : null;
            var counter = new CountingStream(s, chunks);
            HashingSink sink;
            if (compress)
            {
                using (var def = new DeflateStream(counter, CompressionLevel.Optimal, true))
                { sink = new HashingSink(def, progress, cancelled); write(sink); sink.Finish(); }
            }
            else { sink = new HashingSink(counter, progress, cancelled); write(sink); sink.Finish(); }
            e.Crc = sink.Crc; e.Size = sink.Count; e.CompressedSize = counter.Count; e.Chunks = chunks?.Finish();
            if (!z64local && (e.Size >= 0xFFFFFFFFL || e.CompressedSize >= 0xFFFFFFFFL)) throw new IOException("member " + name + " is 4 GB or larger; it must be added with mayExceed4G");
            long end = s.Position;
            s.Position = e.HeaderOffset + 14;
            w.Write(e.Crc);
            if (z64local) { s.Position = e.HeaderOffset + 30 + nb.Length + 4; w.Write(e.Size); w.Write(e.CompressedSize); }
            else { w.Write((uint)e.CompressedSize); w.Write((uint)e.Size); }
            w.Flush(); s.Position = end;
            entries.Add(e); open = false;
            return (e, sink.Sha256, sink.Md5);
        }

        /// <summary>Places a stored member of exactly size bytes here (zeros for now); Fill gives it its bytes before Finish. Its chunk
        /// hashes are not taken (it is the member that records the others').</summary>
        public ZipEntryInfo Reserve(string name, long size)
        {
            if (finished || open) throw new InvalidOperationException("the ZIP writer is not ready for a member");
            if (size < 0 || size >= 0xFFFFFFFFL) throw new ArgumentException("a reserved member is smaller than 4 GB");
            if (!names.Add(name)) throw new InvalidOperationException("a member named " + name + " already exists");
            var e = new ZipEntryInfo { Name = name, Method = 0, HeaderOffset = s.Position, Flags = 0x0800, ExternalAttributes = UnixRegular0644, MadeBy = WriterMadeBy, Size = size, CompressedSize = size, Zip64Local = forceZip64 };
            var h = LocalHeader(e, dosTime, dosDate); s.Write(h, 0, h.Length);
            e.DataOffset = s.Position;
            var zero = new byte[1 << 16];
            for (long left = size; left > 0;) { int n = (int)Math.Min(zero.Length, left); s.Write(zero, 0, n); left -= n; }
            entries.Add(e); reserved.Add(e);
            return e;
        }

        /// <summary>The reserved member's bytes (exactly its reserved size; the caller pads), its CRC-32 put into its local header.</summary>
        public (string sha256, string md5) Fill(ZipEntryInfo e, byte[] content)
        {
            if (finished || open || !reserved.Contains(e)) throw new InvalidOperationException("not a reserved member");
            if (content.Length != e.Size) throw new ArgumentException("a reserved member is filled with exactly its reserved size");
            var h = new HashingSink(null); h.Write(content, 0, content.Length); h.Finish();
            e.Crc = h.Crc; long end = s.Position;
            s.Position = e.DataOffset; s.Write(content, 0, content.Length);
            s.Position = e.HeaderOffset + 14; var w = new BinaryWriter(s, Encoding.UTF8, true); w.Write(e.Crc); w.Flush();
            s.Position = end; reserved.Remove(e);
            return (h.Sha256, h.Md5);
        }

        /// <summary>a member's local header exactly as this writer leaves it (after its CRC-32 and sizes are filled in)</summary>
        public static byte[] LocalHeader(ZipEntryInfo e, ushort dosTime, ushort dosDate)
        {
            var nb = Encoding.UTF8.GetBytes(e.Name); var m = new MemoryStream(); var w = new BinaryWriter(m);
            w.Write(0x04034b50u); w.Write((ushort)(e.Zip64Local ? 45 : 20)); w.Write(e.Flags); w.Write(e.Method); w.Write(dosTime); w.Write(dosDate);
            w.Write(e.Crc); w.Write(e.Zip64Local ? 0xFFFFFFFFu : (uint)e.CompressedSize); w.Write(e.Zip64Local ? 0xFFFFFFFFu : (uint)e.Size);
            w.Write((ushort)nb.Length); w.Write((ushort)(e.Zip64Local ? 20 : 0)); w.Write(nb);
            if (e.Zip64Local) { w.Write((ushort)1); w.Write((ushort)16); w.Write(e.Size); w.Write(e.CompressedSize); }
            w.Flush(); return m.ToArray();
        }

        public void Finish()
        {
            if (finished) return;
            if (open) throw new InvalidOperationException("a member is still open");
            if (reserved.Count > 0) throw new InvalidOperationException("a reserved member was never filled");
            WriteDirectory(s, entries, dosTime, dosDate, forceZip64);
            finished = true;
        }

        /// <summary>The member index (central directory) and the end records for these entries, written at the stream's position.</summary>
        public static void WriteDirectory(Stream s, IList<ZipEntryInfo> entries, ushort dosTime, ushort dosDate, bool forceZip64)
        {
            var w = new BinaryWriter(s, Encoding.UTF8, true);
            long cdStart = s.Position;
            foreach (var e in entries)
            {
                var nb = Encoding.UTF8.GetBytes(e.Name);
                bool zs = forceZip64 || e.Size >= 0xFFFFFFFFL || e.CompressedSize >= 0xFFFFFFFFL, zo = forceZip64 || e.HeaderOffset >= 0xFFFFFFFFL;
                var extra = new MemoryStream(); var xw = new BinaryWriter(extra);
                if (zs || zo) { xw.Write((ushort)1); xw.Write((ushort)((zs ? 16 : 0) + (zo ? 8 : 0))); if (zs) { xw.Write(e.Size); xw.Write(e.CompressedSize); } if (zo) xw.Write(e.HeaderOffset); }
                xw.Flush();
                w.Write(0x02014b50u); w.Write(e.MadeBy); w.Write((ushort)(zs || zo ? 45 : 20)); w.Write(e.Flags); w.Write(e.Method); w.Write(dosTime); w.Write(dosDate);
                w.Write(e.Crc); w.Write(zs ? 0xFFFFFFFFu : (uint)e.CompressedSize); w.Write(zs ? 0xFFFFFFFFu : (uint)e.Size);
                w.Write((ushort)nb.Length); w.Write((ushort)extra.Length); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0);
                w.Write(e.ExternalAttributes); w.Write(zo ? 0xFFFFFFFFu : (uint)e.HeaderOffset); w.Write(nb); w.Write(extra.ToArray());
            }
            long cdSize = s.Position - cdStart;
            bool z64 = forceZip64 || entries.Count >= 0xFFFF || cdStart >= 0xFFFFFFFFL || cdSize >= 0xFFFFFFFFL;
            if (z64)
            {
                long rec = s.Position;
                w.Write(0x06064b50u); w.Write(44L); w.Write((ushort)(0x0300 | 45)); w.Write((ushort)45); w.Write(0u); w.Write(0u);
                w.Write((long)entries.Count); w.Write((long)entries.Count); w.Write(cdSize); w.Write(cdStart);
                w.Write(0x07064b50u); w.Write(0u); w.Write(rec); w.Write(1u);
            }
            w.Write(0x06054b50u); w.Write((ushort)0); w.Write((ushort)0);
            w.Write((ushort)(z64 ? 0xFFFF : entries.Count)); w.Write((ushort)(z64 ? 0xFFFF : entries.Count));
            w.Write(z64 ? 0xFFFFFFFFu : (uint)cdSize); w.Write(z64 ? 0xFFFFFFFFu : (uint)cdStart); w.Write((ushort)0);
            w.Flush(); s.Flush();
        }

        public void Dispose() { }

        sealed class CountingStream : Stream
        {
            readonly Stream inner; readonly ChunkHasher chunks; public long Count;
            public CountingStream(Stream i, ChunkHasher c = null) { inner = i; chunks = c; }
            public override void Write(byte[] b, int o, int c) { inner.Write(b, o, c); chunks?.Write(b, o, c); Count += c; }
            public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
            public override long Length => Count; public override long Position { get => Count; set => throw new NotSupportedException(); }
            public override void Flush() => inner.Flush();
            public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
            public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
        }
    }

    public sealed class ZipReader : IDisposable
    {
        public const int MaxMembers = 256;
        readonly Stream s; readonly bool own;
        public IReadOnlyList<ZipEntryInfo> Entries { get; private set; }
        public long CentralDirectoryOffset { get; private set; }

        /// <summary>Reads and checks the whole structure; allowedName decides each member name (the format's own list).</summary>
        public ZipReader(Stream seekable, Func<string, bool> allowedName, bool ownStream = false)
        {
            s = seekable; own = ownStream;
            if (!s.CanSeek || !s.CanRead) throw new ZipFormatException("the backup file cannot be read from start to end");
            Entries = Parse(allowedName ?? (n => true));
        }

        public static ZipReader Open(string path, Func<string, bool> allowedName) =>
            new ZipReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan), allowedName, true);

        ZipReader(Stream seekable, List<ZipEntryInfo> located) { s = seekable; own = false; Entries = located; }

        /// <summary>A reader over parts that Backup Repair's part finder located and checked in a file whose own table of contents (its
        /// member index) is damaged (Repair/Examination.cs: found through the repair map, a lenient read of the index or a walk of the part
        /// headers, each part's bytes checked against the backup's trusted records). No index is read here: each part is read through the
        /// place the part finder gives, never more than its size, with its CRC-32 checked at its end, exactly as a member of an intact file.</summary>
        public static ZipReader OfLocatedParts(Stream seekable, IEnumerable<ZipEntryInfo> parts)
        {
            if (!seekable.CanSeek || !seekable.CanRead) throw new ZipFormatException("the backup file cannot be read from start to end");
            return new ZipReader(seekable, parts.ToList());
        }

        static ushort U16(byte[] b, int o) => (ushort)(b[o] | b[o + 1] << 8);
        static uint U32(byte[] b, int o) => (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24);
        static long U64(byte[] b, int o) => (long)U32(b, o) | (long)U32(b, o + 4) << 32;

        byte[] ReadAt(long pos, int n)
        {
            if (pos < 0 || pos + n > s.Length) throw new ZipFormatException("a ZIP record lies outside the file");
            var b = new byte[n]; s.Position = pos; int got = 0;
            while (got < n) { int r = s.Read(b, got, n - got); if (r <= 0) throw new ZipFormatException("the file ended inside a ZIP record"); got += r; }
            return b;
        }

        List<ZipEntryInfo> Parse(Func<string, bool> allowed)
        {
            long len = s.Length;
            if (len < 22) throw new ZipFormatException("not a ZIP file (too short)");
            int tail = (int)Math.Min(len, 22 + 65535);
            var t = ReadAt(len - tail, tail);
            int eocd = -1;
            for (int i = tail - 22; i >= 0; i--)
                if (U32(t, i) == 0x06054b50 && i + 22 + U16(t, i + 20) == tail) { eocd = i; break; }
            if (eocd < 0) throw new ZipFormatException("not a ZIP file, or bytes follow its end record");
            long eocdPos = len - tail + eocd;
            if (U16(t, eocd + 20) != 0) throw new ZipFormatException("the archive has a comment (not one Classic Cart Studio writes)");
            if (U16(t, eocd + 4) != 0 || U16(t, eocd + 6) != 0) throw new ZipFormatException("a multi-part archive is not supported");
            long count = U16(t, eocd + 10), cdSize = U32(t, eocd + 12), cdOff = U32(t, eocd + 16), cdEnd = eocdPos;
            if (count == 0xFFFF || cdSize == 0xFFFFFFFF || cdOff == 0xFFFFFFFF)
            {
                if (eocdPos < 20) throw new ZipFormatException("the ZIP64 locator is missing");
                var loc = ReadAt(eocdPos - 20, 20);
                if (U32(loc, 0) != 0x07064b50 || U32(loc, 4) != 0 || U32(loc, 16) != 1) throw new ZipFormatException("the ZIP64 locator is missing or names another disk");
                long recPos = U64(loc, 8);
                var rec = ReadAt(recPos, 56);
                if (U32(rec, 0) != 0x06064b50 || U64(rec, 4) != 44 || U32(rec, 16) != 0 || U32(rec, 20) != 0) throw new ZipFormatException("the ZIP64 end record is not valid");
                if (recPos + 56 != eocdPos - 20) throw new ZipFormatException("bytes lie between the ZIP64 records");
                count = U64(rec, 32); cdSize = U64(rec, 40); cdOff = U64(rec, 48); cdEnd = recPos;
                if (U64(rec, 24) != count) throw new ZipFormatException("the ZIP64 member counts disagree");
            }
            if (count > MaxMembers) throw new ZipFormatException("the archive has more members than a console backup can (" + count + ")");
            if (cdOff + cdSize != cdEnd) throw new ZipFormatException("bytes lie between the member index and the end of the archive");
            if (cdSize > 4 * 1024 * 1024) throw new ZipFormatException("the member index is too large");
            var cd = ReadAt(cdOff, (int)cdSize);
            var list = new List<ZipEntryInfo>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); int p = 0;
            for (long k = 0; k < count; k++)
            {
                if (p + 46 > cd.Length || U32(cd, p) != 0x02014b50) throw new ZipFormatException("the member index is damaged");
                var e = new ZipEntryInfo
                {
                    MadeBy = U16(cd, p + 4), Flags = U16(cd, p + 8), Method = U16(cd, p + 10), Crc = U32(cd, p + 16),
                    CompressedSize = U32(cd, p + 20), Size = U32(cd, p + 24), ExternalAttributes = U32(cd, p + 38), HeaderOffset = U32(cd, p + 42)
                };
                int nl = U16(cd, p + 28), xl = U16(cd, p + 30), cl = U16(cd, p + 32);
                if (U16(cd, p + 6) > 45) throw new ZipFormatException("a member needs a newer ZIP version than supported");
                if (U16(cd, p + 34) != 0) throw new ZipFormatException("a member names another disk");
                if (p + 46 + nl + xl + cl > cd.Length) throw new ZipFormatException("the member index is damaged");
                var nameBytes = new byte[nl]; Buffer.BlockCopy(cd, p + 46, nameBytes, 0, nl);
                if (nameBytes.Any(c => c < 0x20 || c > 0x7E)) throw new ZipFormatException("a member name has characters a console backup never uses");
                e.Name = Encoding.ASCII.GetString(nameBytes);
                int xp = p + 46 + nl, xe = xp + xl;
                while (xp + 4 <= xe)
                {
                    int tag = U16(cd, xp), sz = U16(cd, xp + 2); int q = xp + 4;
                    if (q + sz > xe) throw new ZipFormatException("a member's extra field is damaged");
                    if (tag == 1)
                    {
                        if (e.Size == 0xFFFFFFFF) { if (q + 8 > xp + 4 + sz) throw new ZipFormatException("ZIP64 size missing"); e.Size = U64(cd, q); q += 8; }
                        if (e.CompressedSize == 0xFFFFFFFF) { if (q + 8 > xp + 4 + sz) throw new ZipFormatException("ZIP64 size missing"); e.CompressedSize = U64(cd, q); q += 8; }
                        if (e.HeaderOffset == 0xFFFFFFFF) { if (q + 8 > xp + 4 + sz) throw new ZipFormatException("ZIP64 offset missing"); e.HeaderOffset = U64(cd, q); q += 8; }
                    }
                    xp += 4 + sz;
                }
                if (e.Size == 0xFFFFFFFF || e.CompressedSize == 0xFFFFFFFF || e.HeaderOffset == 0xFFFFFFFF) throw new ZipFormatException("a ZIP64 value is missing");
                p += 46 + nl + xl + cl;
                CheckEntry(e, allowed);
                if (!seen.Add(e.Name)) throw new ZipFormatException("two members have the same name: " + e.Name);
                list.Add(e);
            }
            if (p != cd.Length) throw new ZipFormatException("the member index has extra bytes");
            // local headers: each agrees with the index; members tile the file from offset 0 to the index without a gap or an overlap
            long expect = 0;
            foreach (var e in list.OrderBy(x => x.HeaderOffset))
            {
                if (e.HeaderOffset != expect) throw new ZipFormatException(e.HeaderOffset < expect ? "two members overlap" : "unexplained bytes lie before a member");
                var lh = ReadAt(e.HeaderOffset, 30);
                if (U32(lh, 0) != 0x04034b50) throw new ZipFormatException("a member's local header is missing");
                int nl = U16(lh, 26), xl = U16(lh, 28);
                if (U16(lh, 6) != e.Flags || U16(lh, 8) != e.Method) throw new ZipFormatException("a member's local header disagrees with the index");
                var ln = ReadAt(e.HeaderOffset + 30, nl + xl);
                if (Encoding.ASCII.GetString(ln, 0, nl) != e.Name) throw new ZipFormatException("a member's local name disagrees with the index");
                long lcs = U32(lh, 18), lus = U32(lh, 22);
                if (lcs == 0xFFFFFFFF || lus == 0xFFFFFFFF)
                {
                    int xp = nl; bool found = false;
                    while (xp + 4 <= nl + xl) { int tag = U16(ln, xp), sz = U16(ln, xp + 2); if (tag == 1 && sz >= 16) { lus = U64(ln, xp + 4); lcs = U64(ln, xp + 12); found = true; } xp += 4 + sz; }
                    if (!found) throw new ZipFormatException("a member's ZIP64 local sizes are missing");
                }
                if (U32(lh, 14) != e.Crc || lcs != e.CompressedSize || lus != e.Size) throw new ZipFormatException("a member's local sizes or checksum disagree with the index (" + e.Name + ")");
                e.Zip64Local = U32(lh, 18) == 0xFFFFFFFF || U32(lh, 22) == 0xFFFFFFFF;
                e.DataOffset = e.HeaderOffset + 30 + nl + xl;
                expect = e.DataOffset + e.CompressedSize;
            }
            if (expect != cdOff) throw new ZipFormatException(expect < cdOff ? "unexplained bytes lie before the member index" : "a member overlaps the member index");
            CentralDirectoryOffset = cdOff;
            return list;
        }

        static void CheckEntry(ZipEntryInfo e, Func<string, bool> allowed)
        {
            if ((e.Flags & 0x0001) != 0) throw new ZipFormatException("an encrypted member is not allowed");
            if ((e.Flags & 0x0008) != 0) throw new ZipFormatException("a member with a data descriptor is not one this format writes");
            if ((e.Flags & 0x0040) != 0) throw new ZipFormatException("strong encryption is not allowed");
            if (e.Method != 0 && e.Method != 8) throw new ZipFormatException("member " + e.Name + " uses an unsupported compression method");
            if (e.Method == 0 && e.Size != e.CompressedSize) throw new ZipFormatException("a stored member's sizes disagree");
            if (e.Name.Length == 0 || e.Name.EndsWith("/") || e.Name.StartsWith("/") || e.Name.Contains("\\") || e.Name.Contains(":") || e.Name.Contains("//"))
                throw new ZipFormatException("member name not allowed: " + e.Name);
            foreach (var seg in e.Name.Split('/')) if (seg == "." || seg == ".." || seg.Length == 0) throw new ZipFormatException("member name not allowed: " + e.Name);
            int host = e.MadeBy >> 8; uint unixType = (e.ExternalAttributes >> 16) & 0xF000;
            if (host == 3 && unixType != 0 && unixType != 0x8000) throw new ZipFormatException("member " + e.Name + " is a link or special file, not a regular file");
            if ((e.ExternalAttributes & 0x10) != 0) throw new ZipFormatException("member " + e.Name + " is a folder");
            if (!allowed(e.Name)) throw new ZipFormatException("member not part of a console backup: " + e.Name);
        }

        public ZipEntryInfo Find(string name) => Entries.FirstOrDefault(e => e.Name == name);

        /// <summary>The member's bytes, never more than its declared size; ends with a CRC-32 check (an IOException on a mismatch).</summary>
        public Stream OpenMember(ZipEntryInfo e)
        {
            Stream raw = new SubStream(s, e.DataOffset, e.CompressedSize);
            if (e.Method == 8) raw = new DeflateStream(raw, CompressionMode.Decompress);
            return new CheckedStream(raw, e.Size, e.Crc, e.Name);
        }

        /// <summary>A stored member as a seekable window of the file (random access without extracting it).</summary>
        public Stream StoredWindow(ZipEntryInfo e)
        {
            if (e.Method != 0) throw new ZipFormatException("member " + e.Name + " is compressed; it cannot be opened in place");
            return new SubStream(s, e.DataOffset, e.Size);
        }

        /// <summary>A member's bytes as stored in the file (compressed when it is deflated), without a check: the repair map's chunks.</summary>
        public Stream RawWindow(ZipEntryInfo e) => new SubStream(s, e.DataOffset, e.CompressedSize);

        /// <summary>A member's local header as it is in the file (name and extra field included).</summary>
        public byte[] LocalHeaderBytes(ZipEntryInfo e) => ReadAt(e.HeaderOffset, (int)(e.DataOffset - e.HeaderOffset));

        public void Dispose() { if (own) s.Dispose(); }

        sealed class CheckedStream : Stream
        {
            readonly Stream inner; readonly long size; readonly uint crc; readonly string name; readonly Crc32 c = new Crc32(); long pos; bool checkedEnd;
            public CheckedStream(Stream i, long size, uint crc, string name) { inner = i; this.size = size; this.crc = crc; this.name = name; }
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
                if (checkedEnd) return; checkedEnd = true;
                var one = new byte[1];
                if (inner.Read(one, 0, 1) > 0) throw new IOException("member " + name + " holds more data than it declares");
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
