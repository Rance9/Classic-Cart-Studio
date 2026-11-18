// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// What a (possibly damaged) backup file still holds, found and checked without trusting the file. The file is opened for reading only
// (other programs may read it, none may write it while it is open) and is never changed.
//  1. Where its members are (Locate), first source that works:
//       the member index, when the strict reader (ZipReader) accepts the whole file;
//       the repair map's early descriptor at offset 0 (its CRC-32 and its own check), whose records give every member's place;
//       a lenient read of the member index (an intact index after damaged local headers: older backups have no descriptor);
//       a walk of the local headers from offset 0 with checked lengths: each next header must start exactly where the previous member
//       ends, so bytes inside a member (a "PK" pattern inside a storage image) are never read as a header.
//     Bounded: at most 256 members, names only from the format's fixed list (no traversal, no folders), no duplicates ignoring case
//     (a duplicate refuses the file), no overlaps, ZIP64 sizes only from a well-formed extra field, nothing taken past the file's end.
//  2. The trusted records (TrustedMap): the manifest (its CRC-32, format and schema), the descriptor and the chunk map (CRC-32 and own
//     check). When several survive, in one file or across copies of one backup, they must agree exactly, or nothing is trusted.
//  3. One pass over the whole file (Examine): its SHA-256 as it is now, and every located member's bytes against the records (size,
//     SHA-256, MD5, its local header, and the SHA-256 of each 1,048,576-byte piece when the map records pieces).
// A record says what the bytes were; only bytes that hash to it are ever used.
using CcsBackup.Archive;
using CcsBackup.Device;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CcsBackup.Repair
{
    /// <summary>Where one member lies in one file.</summary>
    public sealed class Located
    {
        public string Name; public long HeaderOffset, DataOffset, CompressedSize, Size; public int Method; public uint Crc; public ushort Flags;
        public byte[] Header;   // the local header bytes as found in the file (null when the file ends before them)
        public bool Zip64Local;
        public long DataEnd => DataOffset + CompressedSize;
    }

    /// <summary>One member of one file checked against the trusted records.</summary>
    public sealed class MemberCheck
    {
        public string Path; public Located At;
        /// <summary>verified | damaged | incomplete (the file ends inside it) | not-found</summary>
        public string Status = "not-found";
        public string Sha256, Md5, RawSha256; public uint Crc; public long Available;   // RawSha256: of its stored bytes (compressed when deflated)
        public bool HeaderOk;     // its local header equals the recorded one, or (without a record) agrees with its verified content
        public string[] Pieces;   // SHA-256 of each 1,048,576-byte piece present in full (null where the file ends first)
        public bool[] PieceOk;    // each piece against the recorded piece checksum (null when none is recorded)
        public string Note;
        public bool Verified => Status == "verified";
    }

    /// <summary>The trusted record of one member (from the manifest and/or the repair map).</summary>
    public sealed class Rec
    {
        public string Path; public long Size = -1; public string Sha256, Md5;
        public MapMember Map;   // place, exact local header and piece checksums, when a repair map survives
        public string RecordedBy;
    }

    public sealed class BackupFile : IDisposable
    {
        public string Path, Label; public long Length; public DateTime ModifiedUtc; internal FileStream S;
        public bool IndexIntact; public string IndexProblem, LocatedBy, WalkStop;
        public string Foreign;   // the walk met a part name that a console backup never has (a crafted or foreign ZIP, or damage)
        public readonly Dictionary<string, Located> At = new Dictionary<string, Located>(StringComparer.Ordinal);
        public readonly List<string> Refusals = new List<string>();   // what makes the file refused as a source (a duplicate name, an overlap)
        public Dictionary<string, object> Manifest; public byte[] ManifestRaw; public string ManifestProblem;
        public MapDoc Descriptor, ChunkMap; public string DescriptorProblem, ChunkMapProblem;
        public readonly Dictionary<string, MemberCheck> Checks = new Dictionary<string, MemberCheck>(StringComparer.Ordinal);
        public string Sha256;   // the whole file as read by Examine
        public bool Examined;

        public static BackupFile Open(string path, string label)
        {
            var f = new BackupFile { Path = System.IO.Path.GetFullPath(path), Label = label };
            f.S = new FileStream(f.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess);
            f.Length = f.S.Length; f.ModifiedUtc = File.GetLastWriteTimeUtc(f.Path);
            f.Locate();
            return f;
        }

        public void Dispose() { S?.Dispose(); S = null; }

        /// <summary>A file the person chose for one recorded part (KernelSupply): read as that part's stored bytes from its first byte, never
        /// as a backup, and checked in one pass against the part's record (size, SHA-256, MD5 and each piece checksum when recorded). It is
        /// "verified" only when all of them match; it has no header of its own (the part's header comes from the backup's records). The file
        /// is open for reading only (no program can change it) until it is disposed.</summary>
        internal static BackupFile Supplied(string path, string label, Rec r, Func<bool> cancelled)
        {
            var f = new BackupFile { Path = System.IO.Path.GetFullPath(path), Label = label, LocatedBy = "a file supplied for " + r.Path };
            f.S = new FileStream(f.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
            f.Length = f.S.Length; f.ModifiedUtc = File.GetLastWriteTimeUtc(f.Path);
            var L = new Located { Name = r.Path, HeaderOffset = 0, DataOffset = 0, CompressedSize = f.Length, Size = f.Length, Method = 0 };
            var mc = new MemberCheck { Path = r.Path, At = L, Status = "damaged" }; f.At[r.Path] = L; f.Checks[r.Path] = mc; f.Examined = true;
            if (r.Size >= 0 && f.Length != r.Size || f.Length > BackupFormat.MaxSize(r.Path)) { mc.Note = "its size differs from the recorded size"; return f; }   // never read a file of the wrong size
            var content = new ContentHash(); var pieces = new ChunkHasher(RepairMap.ChunkSize); var buf = new byte[Streams.Buffer]; long pos = 0;
            f.S.Position = 0;
            while (pos < f.Length)
            {
                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                int n = f.S.Read(buf, 0, (int)Math.Min(buf.Length, f.Length - pos)); if (n <= 0) break;
                content.Write(buf, 0, n); pieces.Write(buf, 0, n); pos += n;
            }
            mc.Available = pos;
            if (pos != f.Length) { mc.Status = "incomplete"; mc.Note = "it ended early while it was read"; return f; }
            (mc.Sha256, mc.Md5, mc.Crc) = content.Finish(); mc.RawSha256 = f.Sha256 = mc.Sha256;
            mc.Pieces = pieces.Finish().ToArray();
            var rec = r.Map?.Chunks;
            if (rec != null) { mc.PieceOk = new bool[rec.Count]; for (int i = 0; i < rec.Count; i++) mc.PieceOk[i] = i < mc.Pieces.Length && mc.Pieces[i] == rec[i]; }
            bool piecesOk = rec == null || rec.Count == mc.Pieces.Length && mc.PieceOk.All(x => x);
            mc.Status = mc.Sha256 == r.Sha256 && mc.Md5 == r.Md5 && piecesOk ? "verified" : "damaged";
            if (!mc.Verified) mc.Note = "its bytes do not match the recorded checksum";
            return f;
        }

        public MemberCheck Check(string path) => Checks.TryGetValue(path, out var c) ? c : null;
        public bool Complete => Examined && IndexIntact && Checks.Count > 0 && Checks.Values.All(c => c.Verified && c.HeaderOk) && Manifest != null;

        // ---------------------------------------------------------------- reading
        static ushort U16(byte[] b, int o) => (ushort)(b[o] | b[o + 1] << 8);
        static uint U32(byte[] b, int o) => (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24);
        static long U64(byte[] b, int o) => (long)U32(b, o) | (long)U32(b, o + 4) << 32;

        /// <summary>up to n bytes at pos (fewer when the file ends first)</summary>
        internal byte[] ReadUpTo(long pos, long n)
        {
            if (pos < 0 || pos >= Length || n <= 0) return new byte[0];
            var b = new byte[(int)Math.Min(n, Length - pos)]; S.Position = pos; int got = 0;
            while (got < b.Length) { int r = S.Read(b, got, b.Length - got); if (r <= 0) break; got += r; }
            if (got < b.Length) Array.Resize(ref b, got);
            return b;
        }

        static string Shown(string name) => new string(name.Select(c => c < 0x20 || c > 0x7E ? '?' : c).ToArray());

        /// <summary>One local header at pos, every field checked (null with the reason; "end" when the member index starts there).</summary>
        internal Located HeaderAt(long pos, out string why)
        {
            why = null;
            if (pos < 0 || pos + 30 > Length) { why = "the file ends where a part's header belongs (offset " + pos.ToString("N0", CultureInfo.InvariantCulture) + ")"; return null; }
            var h = ReadUpTo(pos, 30); uint sig = U32(h, 0);
            if (sig != 0x04034b50) { why = sig == 0x02014b50 || sig == 0x06054b50 || sig == 0x06064b50 ? "end" : "no part header where one belongs (offset " + pos.ToString("N0", CultureInfo.InvariantCulture) + ")"; return null; }
            ushort flags = U16(h, 6), method = U16(h, 8); uint crc = U32(h, 14); long cs = U32(h, 18), us = U32(h, 22); int nl = U16(h, 26), xl = U16(h, 28);
            if ((flags & 0x0049) != 0) { why = "a part is encrypted or written in a way a backup never is (offset " + pos.ToString("N0", CultureInfo.InvariantCulture) + ")"; return null; }
            if (method != 0 && method != 8) { why = "a part uses a compression a backup never uses"; return null; }
            if (nl == 0 || nl > 255 || xl > 64) { why = "a part's header has impossible lengths (offset " + pos.ToString("N0", CultureInfo.InvariantCulture) + ")"; return null; }
            if (pos + 30 + nl + xl > Length) { why = "the file ends inside a part's header"; return null; }
            var rest = ReadUpTo(pos + 30, nl + xl);
            var name = Encoding.ASCII.GetString(rest, 0, nl);
            if (rest.Take(nl).Any(c => c < 0x20 || c > 0x7E) || !BackupFormat.Allowed(name)) { why = "a part named \"" + Shown(name) + "\", which is not part of a console backup"; return null; }
            bool z64 = false;
            for (int p = nl; p + 4 <= nl + xl;)
            {
                int tag = U16(rest, p), sz = U16(rest, p + 2);
                if (p + 4 + sz > nl + xl) { why = "a part's header has a damaged extra field"; return null; }
                if (tag == 1 && (cs == 0xFFFFFFFF || us == 0xFFFFFFFF)) { if (sz < 16) { why = "a part's ZIP64 sizes are missing"; return null; } us = U64(rest, p + 4); cs = U64(rest, p + 12); z64 = true; }
                p += 4 + sz;
            }
            if (cs == 0xFFFFFFFF || us == 0xFFFFFFFF) { why = "a part's ZIP64 sizes are missing"; return null; }
            if (us < 0 || cs < 0 || us > BackupFormat.MaxSize(name) || method == 0 && cs != us) { why = "a part's sizes are impossible (" + name + ")"; return null; }
            return new Located { Name = name, HeaderOffset = pos, DataOffset = pos + 30 + nl + xl, CompressedSize = cs, Size = us, Method = method, Crc = crc, Flags = flags, Header = h.Concat(rest).ToArray(), Zip64Local = z64 };
        }

        void Locate()
        {
            // 1. the member index, strictly
            try
            {
                var z = new ZipReader(S, BackupFormat.Allowed);
                foreach (var e in z.Entries)
                    At[e.Name] = new Located { Name = e.Name, HeaderOffset = e.HeaderOffset, DataOffset = e.DataOffset, CompressedSize = e.CompressedSize, Size = e.Size, Method = e.Method, Crc = e.Crc, Flags = e.Flags, Header = z.LocalHeaderBytes(e), Zip64Local = e.Zip64Local };
                IndexIntact = true; LocatedBy = "the file's table of contents (its ZIP index)";
            }
            catch (Exception ex) when (ex is ZipFormatException || ex is IOException) { IndexProblem = ex.Message; }
            // 2. the repair map's early descriptor, first in the file
            var head = HeaderAt(0, out var hw);
            if (head != null && head.Name == BackupFormat.DescriptorName && head.Method == 0)
            {
                var b = ReadUpTo(head.DataOffset, head.CompressedSize);
                var c = new Crc32(); c.Update(b, 0, b.Length);
                if (b.Length != head.CompressedSize) DescriptorProblem = "the file ends inside it";
                else if (c.Value != head.Crc) DescriptorProblem = "it is damaged (its CRC-32 does not match)";
                else { var d = MapDoc.Parse(b, out var dp); if (d == null || d.Role != "descriptor") DescriptorProblem = dp ?? "it has the wrong role"; else Descriptor = d; }
            }
            else DescriptorProblem = head == null ? "not readable at the start of the file (" + hw + ")" : "this backup was made before repair information existed";
            if (!IndexIntact)
            {
                if (Descriptor != null) FromDescriptor(head);
                else if (!LenientIndex()) Walk();
            }
            ReadManifest(); ReadChunkMap();
        }

        void FromDescriptor(Located head)
        {
            At[BackupFormat.DescriptorName] = head;
            foreach (var m in Descriptor.Members)
                At[m.Path] = new Located { Name = m.Path, HeaderOffset = m.HeaderOffset, DataOffset = m.DataOffset, CompressedSize = m.CompressedSize, Size = m.Size, Method = m.Method, Crc = m.Crc, Flags = 0x0800, Header = ReadUpTo(m.HeaderOffset, m.LocalHeader.Length / 2), Zip64Local = Descriptor.Zip64 };
            long last = Descriptor.Members.Count == 0 ? head.DataEnd : Descriptor.Members.Max(x => x.DataEnd);
            var mh = HeaderAt(last, out var why);
            if (mh != null && mh.Name == BackupFormat.ManifestName) At[mh.Name] = mh;
            else ManifestProblem = "the manifest is not where the repair map says the parts end (" + (mh != null ? "found " + mh.Name : why) + ")";
            LocatedBy = "the backup's own repair map (its early copy of the list of parts)";
        }

        /// <summary>the member index read without trusting the local headers: an index that is intact while some headers are damaged</summary>
        bool LenientIndex()
        {
            try
            {
                int tail = (int)Math.Min(Length, 22 + 65535); if (Length < 22) return false;
                var t = ReadUpTo(Length - tail, tail); int eocd = -1;
                for (int i = tail - 22; i >= 0; i--) if (U32(t, i) == 0x06054b50 && i + 22 + U16(t, i + 20) == tail) { eocd = i; break; }
                if (eocd < 0) return false;
                long eocdPos = Length - tail + eocd;
                long count = U16(t, eocd + 10), cdSize = U32(t, eocd + 12), cdOff = U32(t, eocd + 16);
                if (count == 0xFFFF || cdSize == 0xFFFFFFFF || cdOff == 0xFFFFFFFF)
                {
                    if (eocdPos < 20) return false;
                    var loc = ReadUpTo(eocdPos - 20, 20); if (U32(loc, 0) != 0x07064b50) return false;
                    var rec = ReadUpTo(U64(loc, 8), 56); if (rec.Length < 56 || U32(rec, 0) != 0x06064b50) return false;
                    count = U64(rec, 32); cdSize = U64(rec, 40); cdOff = U64(rec, 48);
                }
                if (count <= 0 || count > ZipReader.MaxMembers || cdSize > 4 * 1000 * 1000 || cdOff < 0 || cdOff + cdSize > eocdPos) return false;
                var cd = ReadUpTo(cdOff, cdSize); if (cd.Length != cdSize) return false;
                var list = new List<Located>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); int p = 0;
                for (long k = 0; k < count; k++)
                {
                    if (p + 46 > cd.Length || U32(cd, p) != 0x02014b50) return false;
                    var L = new Located { Flags = U16(cd, p + 8), Method = U16(cd, p + 10), Crc = U32(cd, p + 16), CompressedSize = U32(cd, p + 20), Size = U32(cd, p + 24), HeaderOffset = U32(cd, p + 42) };
                    int nl = U16(cd, p + 28), xl = U16(cd, p + 30), cl = U16(cd, p + 32);
                    if (nl == 0 || p + 46 + nl + xl + cl > cd.Length) return false;
                    var nb = cd.Skip(p + 46).Take(nl).ToArray();
                    if (nb.Any(c => c < 0x20 || c > 0x7E)) return false;
                    L.Name = Encoding.ASCII.GetString(nb);
                    if (!BackupFormat.Allowed(L.Name)) { Refusals.Add("its table of contents names a part that is not part of a console backup (" + Shown(L.Name) + ")"); return false; }
                    if (!seen.Add(L.Name)) { Refusals.Add("its table of contents names one part twice (" + L.Name + ")"); return false; }
                    bool cz = false;
                    for (int xp = p + 46 + nl; xp + 4 <= p + 46 + nl + xl;)
                    {
                        int tag = U16(cd, xp), sz = U16(cd, xp + 2); int q = xp + 4; if (q + sz > p + 46 + nl + xl) return false;
                        if (tag == 1)
                        {
                            if (L.Size == 0xFFFFFFFF && q + 8 <= xp + 4 + sz) { L.Size = U64(cd, q); q += 8; cz = true; }
                            if (L.CompressedSize == 0xFFFFFFFF && q + 8 <= xp + 4 + sz) { L.CompressedSize = U64(cd, q); q += 8; cz = true; }
                            if (L.HeaderOffset == 0xFFFFFFFF && q + 8 <= xp + 4 + sz) { L.HeaderOffset = U64(cd, q); q += 8; }
                        }
                        xp += 4 + sz;
                    }
                    if (L.Size == 0xFFFFFFFF || L.CompressedSize == 0xFFFFFFFF || L.HeaderOffset == 0xFFFFFFFF || L.Method != 0 && L.Method != 8 || (L.Flags & 0x0049) != 0 || L.Size > BackupFormat.MaxSize(L.Name) || L.Method == 0 && L.Size != L.CompressedSize) return false;
                    // the member's own local header, when it is readable and names it, is taken whole (it sits next to the bytes; an index
                    // entry may be the damaged part); otherwise the index entry, the data placed by the writer's rule after a header
                    var lh = HeaderAt(L.HeaderOffset, out _);
                    if (lh != null && lh.Name == L.Name) L = lh;
                    else { L.Zip64Local = cz; L.DataOffset = L.HeaderOffset + 30 + nl + (cz ? 20 : 0); L.Header = ReadUpTo(L.HeaderOffset, L.DataOffset - L.HeaderOffset); }
                    list.Add(L); p += 46 + nl + xl + cl;
                }
                long end = 0;
                foreach (var L in list.OrderBy(x => x.HeaderOffset)) { if (L.HeaderOffset < end) { Refusals.Add("its table of contents places two parts over each other"); return false; } end = L.DataEnd; }
                if (end > cdOff) return false;
                foreach (var L in list) At[L.Name] = L;
                LocatedBy = "the file's table of contents (read without trusting the damaged part headers)";
                return true;
            }
            catch (IOException) { return false; }
        }

        /// <summary>local headers from offset 0, each next one exactly where the previous member ends (never a search for a signature)</summary>
        void Walk()
        {
            long pos = 0; var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (At.Count < ZipReader.MaxMembers)
            {
                var L = HeaderAt(pos, out var why);
                if (L == null) { if (why != "end") WalkStop = why; if (why.StartsWith("a part named")) Foreign = why; break; }
                if (!seen.Add(L.Name)) { Refusals.Add("it holds two parts with the same name (" + L.Name + ")"); At.Clear(); break; }
                At[L.Name] = L;
                if (L.DataEnd > Length) { WalkStop = "the file ends inside " + L.Name; break; }
                pos = L.DataEnd;
                if (L.Name == BackupFormat.ManifestName) break;   // the manifest is the last part
            }
            LocatedBy = "a walk through the part headers from the start of the file";
        }

        static byte[] Inflate(byte[] raw, int method, long size)
        {
            if (method == 0) return raw;
            using (var d = new DeflateStream(new MemoryStream(raw), CompressionMode.Decompress))
            {
                var m = new MemoryStream(); var b = new byte[1 << 16]; int n;
                while ((n = d.Read(b, 0, b.Length)) > 0) { m.Write(b, 0, n); if (m.Length > size) break; }
                return m.ToArray();
            }
        }

        /// <summary>a small deflated or stored member's content, checked against its CRC-32 and size (null with the reason)</summary>
        internal byte[] Content(Located L, out string why, out byte[] raw)
        {
            why = null; raw = null;
            if (L.CompressedSize > 64L * 1000 * 1000 || L.Size > 64L * 1000 * 1000) { why = "it is larger than this check reads"; return null; }
            raw = ReadUpTo(L.DataOffset, L.CompressedSize);
            if (raw.Length != L.CompressedSize) { why = "the file ends inside it"; return null; }
            byte[] b;
            try { b = Inflate(raw, L.Method, L.Size); } catch (InvalidDataException) { why = "it cannot be unpacked (damaged)"; return null; }
            var c = new Crc32(); c.Update(b, 0, b.Length);
            if (b.Length != L.Size || c.Value != L.Crc) { why = "it is damaged (its CRC-32 or size does not match)"; return null; }
            return b;
        }

        void ReadManifest()
        {
            if (!At.TryGetValue(BackupFormat.ManifestName, out var L)) { ManifestProblem = ManifestProblem ?? "the manifest (the list of contents at the end of the file) was not found" + (WalkStop != null ? ": " + WalkStop : ""); return; }
            var b = Content(L, out var why, out var raw);
            if (b == null) { ManifestProblem = "the manifest cannot be used: " + why; return; }
            try
            {
                var m = Json.Parse(new UTF8Encoding(false, true).GetString(b));
                if (m.S("format") != BackupFormat.Format || m.S("schema") != BackupFormat.Schema) { ManifestProblem = "the manifest is not a Classic Cart Studio console backup's"; return; }
                Manifest = m; ManifestRaw = raw;
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidOperationException || ex is DecoderFallbackException) { ManifestProblem = "the manifest is not readable"; }
        }

        void ReadChunkMap()
        {
            if (!At.TryGetValue(BackupFormat.ChunkMapName, out var L)) { ChunkMapProblem = "not found"; return; }
            var b = Content(L, out var why, out _);
            if (b == null) { ChunkMapProblem = why; return; }
            var d = MapDoc.Parse(b, out var p); if (d == null || d.Role != "chunk-map") ChunkMapProblem = p ?? "it has the wrong role"; else ChunkMap = d;
        }

        // ---------------------------------------------------------------- the one pass
        /// <summary>SHA-256 (CNG), MD5 and CRC-32 of what is written: a member's content</summary>
        sealed class ContentHash
        {
            readonly SHA256 sha = FastHash.Sha256(); readonly MD5 md5 = MD5.Create(); readonly Crc32 crc = new Crc32();
            public void Write(byte[] b, int o, int c) { sha.TransformBlock(b, o, c, null, 0); md5.TransformBlock(b, o, c, null, 0); crc.Update(b, o, c); }
            public (string sha256, string md5, uint crc) Finish() { sha.TransformFinalBlock(new byte[0], 0, 0); md5.TransformFinalBlock(new byte[0], 0, 0); return (Hex.Of(sha.Hash), Hex.Of(md5.Hash), crc.Value); }
            public static (string sha256, string md5, uint crc) Of(byte[] b) { var h = new ContentHash(); h.Write(b, 0, b.Length); return h.Finish(); }
        }
        sealed class Region { public Rec R; public Located L; public ChunkHasher Pieces; public ContentHash Content; public MemoryStream Raw; public long Fed; }

        /// <summary>Reads the whole file once: its SHA-256 now, and every located member against the trusted records.</summary>
        public void Examine(TrustedMap map, Func<bool> cancelled, Action<long> progress)
        {
            var regions = new List<Region>();
            foreach (var r in map.Members)
            {
                var mc = new MemberCheck { Path = r.Path }; Checks[r.Path] = mc;
                if (!At.TryGetValue(r.Path, out var L)) { mc.Note = "not found in this file"; continue; }
                mc.At = L;
                if (r.Size >= 0 && L.Size != r.Size || r.Map != null && (L.CompressedSize != r.Map.CompressedSize || L.Method != r.Map.Method)) { mc.Status = "damaged"; mc.Note = "its size in this file differs from the recorded size"; continue; }
                regions.Add(new Region { R = r, L = L, Pieces = new ChunkHasher(RepairMap.ChunkSize), Content = L.Method == 0 ? new ContentHash() : null, Raw = L.Method == 0 ? null : new MemoryStream() });
            }
            var whole = FastHash.Sha256();
            var buf = new byte[Streams.Buffer]; S.Position = 0; long pos = 0;
            regions = regions.OrderBy(x => x.L.DataOffset).ToList();
            while (pos < Length)
            {
                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                int n = S.Read(buf, 0, (int)Math.Min(buf.Length, Length - pos)); if (n <= 0) break;
                whole.TransformBlock(buf, 0, n, null, 0);
                foreach (var g in regions)
                {
                    long a = Math.Max(pos, g.L.DataOffset), b = Math.Min(pos + n, g.L.DataEnd);
                    if (a >= b) continue;
                    int o = (int)(a - pos), c = (int)(b - a);
                    g.Pieces.Write(buf, o, c); g.Content?.Write(buf, o, c);
                    if (g.Raw != null) { if (g.Raw.Length + c > BackupFormat.MaxSize(g.L.Name) + 1) g.Raw = null; else g.Raw.Write(buf, o, c); }
                    g.Fed += c;
                }
                pos += n; progress?.Invoke(n);
            }
            whole.TransformFinalBlock(new byte[0], 0, 0); Sha256 = Hex.Of(whole.Hash); whole.Dispose();
            foreach (var g in regions)
            {
                var mc = Checks[g.R.Path]; mc.Available = g.Fed;
                bool full = g.Fed == g.L.CompressedSize;
                var all = g.Pieces.Finish(); int count = RepairMap.ChunkCount(g.L.CompressedSize);
                int have = full ? all.Count : (int)(g.Fed / RepairMap.ChunkSize);
                mc.Pieces = new string[count]; for (int i = 0; i < have && i < count; i++) mc.Pieces[i] = all[i];
                var rec = g.R.Map?.Chunks;
                if (rec != null && rec.Count == count) { mc.PieceOk = new bool[count]; for (int i = 0; i < count; i++) mc.PieceOk[i] = mc.Pieces[i] != null && mc.Pieces[i] == rec[i]; }
                if (!full) { mc.Status = "incomplete"; mc.Note = "the file ends inside it (" + g.Fed.ToString("N0", CultureInfo.InvariantCulture) + " of " + g.L.CompressedSize.ToString("N0", CultureInfo.InvariantCulture) + " bytes present)"; continue; }
                if (g.Content != null) { (mc.Sha256, mc.Md5, mc.Crc) = g.Content.Finish(); mc.RawSha256 = mc.Sha256; }
                else if (g.Raw != null)
                {
                    var raw = g.Raw.ToArray(); mc.RawSha256 = ContentHash.Of(raw).sha256;
                    try { var b = Inflate(raw, g.L.Method, g.L.Size); if (b.Length == g.L.Size) (mc.Sha256, mc.Md5, mc.Crc) = ContentHash.Of(b); }
                    catch (InvalidDataException) { }
                }
                bool contentOk = mc.Sha256 != null && (g.R.Sha256 != null ? mc.Sha256 == g.R.Sha256 && mc.Md5 == g.R.Md5 : SelfChecked(g.R.Path));
                mc.Status = contentOk ? "verified" : "damaged";
                if (!contentOk) mc.Note = mc.Sha256 == null ? "it cannot be unpacked (damaged)" : "its bytes do not match the recorded checksum";
                mc.HeaderOk = g.R.Map != null ? mc.At.Header != null && Hex.Of(mc.At.Header) == g.R.Map.LocalHeader
                    : HeaderAgrees(mc.At.Header, g.R.Path, contentOk ? mc.Crc : (uint?)null, g.L);
            }
            Examined = true;
        }

        /// <summary>a member without a recorded SHA-256 (the repair map itself when the manifest is lost): verified by its own check</summary>
        bool SelfChecked(string path) => path == BackupFormat.DescriptorName ? Descriptor != null : path == BackupFormat.ChunkMapName && ChunkMap != null;

        /// <summary>without a recorded header: the header found in the file names this member and carries its verified CRC-32 and sizes</summary>
        static bool HeaderAgrees(byte[] h, string name, uint? crc, Located L)
        {
            if (h == null || h.Length < 30 || crc == null || U32(h, 0) != 0x04034b50) return false;
            int nl = U16(h, 26); if (30 + nl > h.Length || Encoding.ASCII.GetString(h, 30, nl) != name) return false;
            return U32(h, 14) == crc.Value && U16(h, 8) == L.Method && (U32(h, 18) == 0xFFFFFFFF || U32(h, 18) == L.CompressedSize) && (U32(h, 22) == 0xFFFFFFFF || U32(h, 22) == L.Size);
        }

        /// <summary>plain words for the report: how this file is</summary>
        public string Condition()
        {
            if (Refusals.Count > 0) return "refused as a source: " + string.Join("; ", Refusals);
            var parts = new List<string>();
            parts.Add(IndexIntact ? "its table of contents (ZIP index) is intact" : "its table of contents (ZIP index) is damaged (" + IndexProblem + ")" + (LocatedBy != null ? "; its parts were found through " + LocatedBy : ""));
            if (Foreign != null) parts.Add("it is not a console backup made by Classic Cart Studio: " + Foreign);
            if (Manifest == null) parts.Add(ManifestProblem);
            if (!Examined) { parts.Add("its parts could not be checked against a trustworthy list of contents"); return string.Join("; ", parts); }
            var bad = Checks.Values.Where(c => !c.Verified).ToList();
            parts.Add(bad.Count == 0 ? "every part matches its recorded checksum" : bad.Count + " of " + Checks.Count + " parts do not match their recorded checksum or are missing (" + string.Join(", ", bad.Select(c => c.Path + ": " + c.Status)) + ")");
            var hb = Checks.Values.Where(c => c.Verified && !c.HeaderOk).Select(c => c.Path).ToList();
            if (hb.Count > 0) parts.Add("damaged part headers: " + string.Join(", ", hb));
            return string.Join("; ", parts);
        }
    }

    /// <summary>The records trusted for one backup: its manifest and repair map from one or more copies of it, all in agreement.</summary>
    public sealed class TrustedMap
    {
        public string BackupId, Hwid, Tag, Board, CreatedUtc, CompletedUtc, Source; public bool Simulated;
        public Dictionary<string, object> Manifest; public BackupFile ManifestFrom;
        public MapDoc Descriptor, ChunkMap;
        public List<Rec> Members = new List<Rec>();   // every member except the manifest, in file order
        public ushort DosTime, DosDate; public bool Zip64;
        public bool HasPieces => Members.Any(m => m.Map?.Chunks != null && m.Map.Chunks.Count > 0);
        public Rec Find(string path) => Members.FirstOrDefault(m => m.Path == path);

        static string ManifestKey(Dictionary<string, object> m) =>
            m.S("backup_id") + "\n" + m.O("console")?.S("hwid") + "\n" + m.B("simulated") + "\n" + string.Join("\n", (m.A("members") ?? new List<object>()).OfType<Dictionary<string, object>>()
                .Select(r => r.S("path") + "|" + r.L("size") + "|" + r.S("sha256") + "|" + r.S("md5")).OrderBy(x => x, StringComparer.Ordinal));

        /// <summary>null (with the reason) when no record survives, or the records that survive disagree</summary>
        public static TrustedMap Build(IList<BackupFile> copies, out string problem) => Build(copies, out problem, out _);

        /// <summary>conflict: records survive but disagree (different backups given as copies of one, or an altered file): a refusal</summary>
        public static TrustedMap Build(IList<BackupFile> copies, out string problem, out bool conflict)
        {
            problem = null; conflict = false;
            var usable = copies.Where(c => c.Refusals.Count == 0).ToList();
            var mans = usable.Where(c => c.Manifest != null).ToList();
            var descs = usable.Where(c => c.Descriptor != null).Select(c => c.Descriptor).ToList();
            var cmaps = usable.Where(c => c.ChunkMap != null).Select(c => c.ChunkMap).ToList();
            if (mans.Count == 0 && descs.Count == 0 && cmaps.Count == 0)
            {
                problem = "No trustworthy list of this backup's contents survives: " + string.Join(" ", copies.Select(c => c.Label + ": " + (c.Refusals.Count > 0 ? "refused (" + string.Join("; ", c.Refusals) + ")" : (c.ManifestProblem ?? "no manifest") + "; repair map: " + (c.DescriptorProblem ?? "?")) + "."));
                return null;
            }
            if (mans.Select(c => ManifestKey(c.Manifest)).Distinct().Count() > 1) { conflict = true; problem = "The files' lists of contents disagree, so they are not copies of one backup (or one was altered). Nothing was used."; return null; }
            if (descs.Select(d => d.Check).Distinct().Count() > 1 || cmaps.Select(d => d.Check).Distinct().Count() > 1) { conflict = true; problem = "The files' repair maps disagree, so they are not copies of one backup (or one was altered). Nothing was used."; return null; }
            var t = new TrustedMap { Descriptor = descs.FirstOrDefault(), ChunkMap = cmaps.FirstOrDefault() };
            if (t.Descriptor != null && t.ChunkMap != null && !t.ChunkMap.SameRecords(t.Descriptor, out var why)) { conflict = true; problem = "The backup's two repair maps disagree (" + why + "). Nothing was used."; return null; }
            var map = t.Descriptor ?? t.ChunkMap;
            if (mans.Count > 0)
            {
                t.Manifest = mans[0].Manifest; t.ManifestFrom = mans[0];
                var m = t.Manifest; var con = m.O("console");
                t.BackupId = m.S("backup_id"); t.Hwid = con?.S("hwid"); t.Tag = con?.S("tag"); t.Board = con?.S("board"); t.Simulated = m.B("simulated"); t.CreatedUtc = m.S("created_utc"); t.CompletedUtc = m.S("completed_utc");
                foreach (var r in (m.A("members") ?? new List<object>()).OfType<Dictionary<string, object>>())
                {
                    var p = r.S("path");
                    if (!BackupFormat.Allowed(p) || p == BackupFormat.ManifestName || t.Find(p) != null || !Hex.IsHex(r.S("sha256"), 64) || !Hex.IsHex(r.S("md5"), 32)) { conflict = true; problem = "The backup's manifest lists a part it cannot have, or one twice. Nothing was used."; return null; }
                    t.Members.Add(new Rec { Path = p, Size = r.L("size"), Sha256 = r.S("sha256"), Md5 = r.S("md5"), RecordedBy = "the manifest" });
                }
                if (map != null)
                {
                    if (map.BackupId != t.BackupId || map.Hwid != t.Hwid || map.Simulated != t.Simulated) { conflict = true; problem = "The backup's manifest and its repair map name different backups. Nothing was used."; return null; }
                    foreach (var mm in map.Members)
                    {
                        var r = t.Find(mm.Path);
                        if (r == null || r.Sha256 != mm.Sha256 || r.Md5 != mm.Md5 || r.Size != mm.Size) { conflict = true; problem = "The backup's manifest and its repair map disagree about " + mm.Path + ". Nothing was used."; return null; }
                    }
                    if (t.Descriptor != null && t.Members.Select(x => x.Path).Where(p => p != BackupFormat.DescriptorName).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(t.Descriptor.Members.Select(x => x.Path).OrderBy(x => x, StringComparer.Ordinal)) == false)
                    { conflict = true; problem = "The backup's manifest and its early list of parts list different parts. Nothing was used."; return null; }
                }
                t.Source = map == null ? "the manifest" : "the manifest and the repair map (in agreement)";
            }
            else
            {
                t.BackupId = map.BackupId; t.Hwid = map.Hwid; t.Tag = map.Tag; t.Board = map.Board; t.Simulated = map.Simulated; t.CreatedUtc = map.CreatedUtc; t.CompletedUtc = map.CompletedUtc;
                foreach (var mm in map.Members) t.Members.Add(new Rec { Path = mm.Path, Size = mm.Size, Sha256 = mm.Sha256, Md5 = mm.Md5, RecordedBy = "the repair map" });
                if (t.Descriptor != null) t.Members.Insert(0, new Rec { Path = BackupFormat.DescriptorName, RecordedBy = "its own check" });
                t.Source = "the repair map (the manifest at the end of the file is lost)";
            }
            foreach (var r in t.Members)
            {
                r.Map = t.Descriptor?.Find(r.Path) ?? t.ChunkMap?.Find(r.Path);
                if (t.Descriptor == null && r.Path == BackupFormat.ChunkMapName && r.Sha256 == null) r.RecordedBy = "its own check";
            }
            // file order: the recorded places, else where the copies hold them
            long Place(Rec r)
            {
                if (r.Path == BackupFormat.DescriptorName) return -1;
                if (r.Map != null) return r.Map.HeaderOffset;
                var at = usable.Select(c => c.At.TryGetValue(r.Path, out var l) ? l : null).FirstOrDefault(l => l != null);
                return at?.HeaderOffset ?? long.MaxValue;
            }
            t.Members = t.Members.OrderBy(Place).ToList();
            if (map != null) { t.DosTime = map.DosTime; t.DosDate = map.DosDate; t.Zip64 = map.Zip64; }
            else
            {
                var h = usable.SelectMany(c => c.At.Values).FirstOrDefault(l => l.Header != null && l.Header.Length >= 30 && l.Header[0] == 0x50 && l.Header[1] == 0x4b && l.Header[2] == 3 && l.Header[3] == 4);
                if (h != null) { t.DosTime = (ushort)(h.Header[10] | h.Header[11] << 8); t.DosDate = (ushort)(h.Header[12] | h.Header[13] << 8); t.Zip64 = h.Zip64Local; }
            }
            if (t.BackupId == null || t.Hwid == null) { problem = "The backup's records do not name the backup and its console. Nothing was used."; return null; }
            return t;
        }

        /// <summary>the same console (the backup's recorded identity)</summary>
        public bool SameConsole(TrustedMap o) => o != null && ConsoleIdentity.SameConsole(Hwid, o.Hwid) && Simulated == o.Simulated;
    }
}
