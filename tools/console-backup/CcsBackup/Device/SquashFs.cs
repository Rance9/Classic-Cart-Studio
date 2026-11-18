// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// A small read-only SquashFS 4.0 reader for the system-image checks (Developer Mode's system part, Console Composer's owner system images)
// and the Backup Browser (a system image held in a backup, listed and read file by file): what the image is (its superblock), every
// entry with its metadata (type, permissions, owner and group from the id table, modification time, inode number, link count, size, link
// target) and a file's bytes, read from a seekable stream with bounded reads. It reads gzip/zlib (the console's own system part's) and xz
// with the LZMA2 filter (hakchi's own system dumps, Xz.cs). It never writes and never extracts anything to disk. Another compression
// (lzma, lzo, lz4, zstd) is reported, not read: this build has no decoder for it, so such an image's contents cannot be listed here.
// Bounds: a metadata block is at most 8 KiB, a folder's listing at most 4 MB, at most MaxEntries entries and MaxDepth folder levels per
// image, a folder never contains itself, names are single path parts (no "/", ".", ".." or repeats within a folder), and every data block
// must expand to exactly what the file needs: a damaged or hostile image is an InvalidDataException, never an out-of-range read or an
// endless walk.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace CcsBackup.Device
{
    public sealed class SquashFsReader
    {
        public const ushort Gzip = 1, XzId = 4;
        public const int MaxEntries = 200000, MaxDepth = 64;
        public const long MaxFile = 4000000000L;   // the largest file this reader streams (a system part is at most 512 MiB)
        public static readonly string[] Compressors = { "", "gzip", "lzma", "lzo", "xz", "lz4", "zstd" };
        readonly Stream s;
        public uint Inodes, BlockSize, FragCount, ModifiedTime; public ushort Compression, Flags, IdCount; public ulong RootRef, BytesUsed, IdTable, InodeTable, DirTable, FragTable;
        public string CompressionName => Compression < Compressors.Length ? Compressors[Compression] : "unknown";
        public bool CanRead => Compression == Gzip || Compression == XzId;
        readonly Dictionary<long, (byte[] data, long next)> metaCache = new Dictionary<long, (byte[], long)>();
        uint[] ids;

        public sealed class Entry { public int Type; public string Name; public byte[] Raw; public long Ref; }

        /// <summary>one entry of a listing: Type is the basic SquashFS type (1 folder, 2 file, 3 link, 4 block device, 5 character device,
        /// 6 named pipe, 7 socket); Path is absolute inside the image ("/etc/clover/REGION")</summary>
        public sealed class Node
        {
            public string Path, Name, LinkTarget; public int Type, Mode, Uid, Gid, Links; public uint InodeNumber; public long Size, Mtime, Ref;
            public char TypeCode => Type == 1 ? 'd' : Type == 2 ? 'f' : Type == 3 ? 'l' : Type == 4 ? 'b' : Type == 5 ? 'c' : Type == 6 ? 'p' : Type == 7 ? 's' : '?';
        }

        /// <summary>the superblock (throws InvalidDataException when it is not SquashFS 4.0 with consistent table positions)</summary>
        public SquashFsReader(Stream s)
        {
            this.s = s; var b = At(0, 96);
            if (b == null || Encoding.ASCII.GetString(b, 0, 4) != "hsqs") throw new InvalidDataException("not a SquashFS file system");
            Inodes = U32(b, 4); ModifiedTime = U32(b, 8); BlockSize = U32(b, 12); FragCount = U32(b, 16); Compression = U16(b, 20); Flags = U16(b, 24); IdCount = U16(b, 26);
            if (U16(b, 28) != 4 || U16(b, 30) != 0) throw new InvalidDataException("not SquashFS version 4.0");
            if (BlockSize < 4096 || BlockSize > 1048576 || (BlockSize & (BlockSize - 1)) != 0 || BlockSize != 1u << U16(b, 22)) throw new InvalidDataException("a damaged superblock (block size)");
            RootRef = U64(b, 32); BytesUsed = U64(b, 40); IdTable = U64(b, 48); InodeTable = U64(b, 64); DirTable = U64(b, 72); FragTable = U64(b, 80);
            if (BytesUsed < 96 || BytesUsed > (ulong)s.Length || InodeTable >= DirTable || DirTable >= BytesUsed) throw new InvalidDataException("a damaged superblock (sizes or table positions)");
        }

        static uint U32(byte[] b, int o) => BitConverter.ToUInt32(b, o);
        static ushort U16(byte[] b, int o) => BitConverter.ToUInt16(b, o);
        static ulong U64(byte[] b, int o) => BitConverter.ToUInt64(b, o);

        byte[] At(long at, int n)
        {
            if (at < 0 || n < 0 || at + n > s.Length) return null;
            var b = new byte[n]; s.Position = at; int got = 0;
            while (got < n) { int r = s.Read(b, got, n - got); if (r <= 0) return null; got += r; }
            return b;
        }

        byte[] Inflate(byte[] z, int max)
        {
            if (Compression == XzId) return Xz.Decode(z, max);
            if (Compression != Gzip) throw new NotSupportedException("its compression (" + CompressionName + ") cannot be read by this build");
            if (z.Length < 2 || (z[0] & 0x0F) != 8) throw new InvalidDataException("a damaged compressed block");
            using (var d = new DeflateStream(new MemoryStream(z, 2, z.Length - 2), CompressionMode.Decompress))
            {
                var o = new MemoryStream(); var buf = new byte[8192]; int n;
                while ((n = d.Read(buf, 0, buf.Length)) > 0) { o.Write(buf, 0, n); if (o.Length > max) throw new InvalidDataException("a block larger than allowed"); }
                return o.ToArray();
            }
        }

        /// <summary>one metadata block at an absolute position: its bytes and the position of the next (decoded once, kept while bounded)</summary>
        byte[] Meta(long at, out long next)
        {
            if (metaCache.TryGetValue(at, out var hit)) { next = hit.next; return hit.data; }
            var h = At(at, 2) ?? throw new InvalidDataException("metadata outside the file system");
            int len = U16(h, 0) & 0x7FFF; bool raw = (U16(h, 0) & 0x8000) != 0;
            if (len == 0 || len > 8192) throw new InvalidDataException("a damaged metadata block");
            var d = At(at + 2, len) ?? throw new InvalidDataException("metadata outside the file system");
            next = at + 2 + len;
            byte[] m;
            try { m = raw ? d : Inflate(d, 8192); }
            catch (InvalidDataException) { throw new InvalidDataException("a damaged metadata block"); }
            if (m.Length > 8192) throw new InvalidDataException("a damaged metadata block");
            if (metaCache.Count >= 4096) metaCache.Clear();   // at most 32 MB of decoded metadata kept at once
            metaCache[at] = (m, next);
            return m;
        }

        /// <summary>n bytes of a metadata table from (block position, offset), crossing blocks as needed</summary>
        byte[] MetaRead(ulong table, long block, int offset, int n)
        {
            if (n < 0) throw new InvalidDataException("a damaged metadata reference");
            var o = new byte[n]; int got = 0; long at = (long)table + block;
            if (at < 0 || at >= (long)BytesUsed) throw new InvalidDataException("metadata outside the file system");
            while (got < n)
            {
                var m = Meta(at, out var next);
                if (offset > m.Length) throw new InvalidDataException("a damaged metadata reference");
                int take = Math.Min(n - got, m.Length - offset); Array.Copy(m, offset, o, got, take); got += take; offset = 0; at = next;
                if (got < n && next >= (long)BytesUsed) throw new InvalidDataException("metadata outside the file system");
            }
            return o;
        }

        /// <summary>an inode's bytes (enough for every type this reader uses: header, sizes and up to the block list)</summary>
        byte[] Inode(ulong reference, int n) => MetaRead(InodeTable, (long)(reference >> 16), (int)(reference & 0xFFFF), n);

        /// <summary>a folder's location in the folder table and the byte count of its listing</summary>
        (long start, int off, long size) DirOf(ulong dirRef)
        {
            var i = Inode(dirRef, 40); int type = U16(i, 0);
            if (type == 1) return (U32(i, 16), U16(i, 26), U16(i, 24));
            if (type == 8) return (U32(i, 24), U16(i, 34), U32(i, 20));
            throw new InvalidDataException("not a folder");
        }

        List<Entry> List(ulong dirRef)
        {
            var (start, off, size) = DirOf(dirRef);
            var list = new List<Entry>(); if (size <= 3) return list;
            if (size > 4 * 1024 * 1024) throw new InvalidDataException("a folder larger than allowed");
            var d = MetaRead(DirTable, start, off, (int)size - 3); int p = 0;
            while (p + 12 <= d.Length)
            {
                uint count = U32(d, p) + 1, istart = U32(d, p + 4); p += 12;
                if (count > 256) throw new InvalidDataException("a damaged folder");
                for (uint k = 0; k < count && p + 8 <= d.Length; k++)
                {
                    int eoff = U16(d, p), etype = U16(d, p + 4), nsize = U16(d, p + 6) + 1; p += 8;
                    if (p + nsize > d.Length) throw new InvalidDataException("a damaged folder");
                    list.Add(new Entry { Type = etype, Name = Encoding.UTF8.GetString(d, p, nsize), Ref = (long)istart << 16 | (uint)eoff }); p += nsize;
                }
            }
            return list;
        }

        /// <summary>the entry at an absolute path ("/sbin/init"), following no links; null when it is not there</summary>
        public Entry Find(string path)
        {
            ulong cur = RootRef; Entry e = null;
            foreach (var part in path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (e != null && e.Type != 1) return null;
                e = List(cur).Find(x => x.Name == part); if (e == null) return null;
                cur = (ulong)e.Ref;
            }
            return e;
        }

        /// <summary>a link's target</summary>
        public string LinkTarget(Entry e) => LinkTarget(e.Ref);

        string LinkTarget(long reference)
        {
            var i = Inode((ulong)reference, 24); int t = U16(i, 0); if (t != 3 && t != 10) return null;
            int n = (int)U32(i, 20); if (n <= 0 || n > 4096) return null;
            return Encoding.UTF8.GetString(Inode((ulong)reference, 24 + n), 24, n);
        }

        /// <summary>a path with its "." and ".." parts resolved inside the image (never above its root)</summary>
        static string Resolve(string path)
        {
            var parts = new List<string>();
            foreach (var p in path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)) { if (p == ".") continue; if (p == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; } parts.Add(p); }
            return "/" + string.Join("/", parts);
        }

        /// <summary>a regular file's bytes (at most max); a link is followed once, within the image</summary>
        public byte[] ReadFile(string path, int max, int depth = 0)
        {
            var e = Find(path); if (e == null) return null;
            if (e.Type == 3 && depth == 0) { var t = LinkTarget(e); if (t == null) return null; return ReadFile(Resolve(t.StartsWith("/") ? t : path.Substring(0, path.LastIndexOf('/') + 1) + t), max, 1); }
            if (e.Type != 2) return null;
            var o = new MemoryStream(); ReadFile(e.Ref, o, max); return o.ToArray();
        }

        /// <summary>the owner or group id an inode's index names (the id table)</summary>
        int Id(int index)
        {
            if (ids == null)
            {
                if (IdCount == 0 || IdTable + 8 > BytesUsed) throw new InvalidDataException("a damaged id table");
                var t = new uint[IdCount]; int blocks = (IdCount * 4 + 8191) / 8192;
                var ptrs = At((long)IdTable, 8 * blocks) ?? throw new InvalidDataException("a damaged id table");
                for (int k = 0; k < IdCount; k++)
                {
                    var v = MetaRead(U64(ptrs, 8 * (k / 2048)), 0, k % 2048 * 4, 4);
                    t[k] = U32(v, 0);
                }
                ids = t;
            }
            if (index < 0 || index >= ids.Length) throw new InvalidDataException("an owner that is not in the id table");
            return (int)ids[index];
        }

        /// <summary>an entry's metadata from its inode (the header, then the type's own fields)</summary>
        public Node Describe(long reference, string path, string name)
        {
            var h = Inode((ulong)reference, 16); int type = U16(h, 0);
            var n = new Node { Path = path, Name = name, Ref = reference, Mode = U16(h, 2) & 0xFFF, Uid = Id(U16(h, 4)), Gid = Id(U16(h, 6)), Mtime = U32(h, 8), InodeNumber = U32(h, 12), Links = 1 };
            if (type < 1 || type > 14) return n;   // Type 0: an entry of a kind this reader does not know, listed as it is and never read
            n.Type = type > 7 ? type - 7 : type;
            var i = Inode((ulong)reference, 56);
            switch (type)
            {
                case 1: n.Links = (int)U32(i, 20); break;
                case 8: n.Links = (int)U32(i, 16); break;
                case 2: n.Size = U32(i, 28); break;
                case 9: n.Size = (long)U64(i, 24); n.Links = (int)U32(i, 40); break;
                case 3: case 10: n.Links = (int)U32(i, 16); n.LinkTarget = LinkTarget(reference); n.Size = n.LinkTarget == null ? 0 : Encoding.UTF8.GetByteCount(n.LinkTarget); if (n.LinkTarget == null) throw new InvalidDataException("a damaged link"); break;
                default: n.Links = (int)U32(i, 16); break;   // devices, pipes and sockets: their link count first
            }
            if (n.Size < 0 || n.Size > MaxFile) throw new InvalidDataException("a file larger than allowed");
            return n;
        }

        /// <summary>Every entry under the root, depth first, in name order (a hard link appears once per name, with the same inode number).
        /// cancelled: checked between entries.</summary>
        public List<Node> ListAll(Func<bool> cancelled = null, int maxEntries = MaxEntries)
        {
            var all = new List<Node>(); var visiting = new HashSet<long>();
            void Walk(long dir, string path, int depth)
            {
                if (depth > MaxDepth) throw new InvalidDataException("folders nest deeper than allowed");
                if (!visiting.Add(dir)) throw new InvalidDataException("a folder contains itself");
                var kids = List((ulong)dir); var names = new HashSet<string>(StringComparer.Ordinal);
                kids.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                foreach (var k in kids)
                {
                    if (cancelled != null && cancelled()) throw new OperationCanceledException();
                    if (k.Name.Length == 0 || k.Name == "." || k.Name == ".." || k.Name.IndexOf('/') >= 0 || k.Name.IndexOf('\0') >= 0 || !names.Add(k.Name)) throw new InvalidDataException("a damaged folder (a name that is not one path part)");
                    if (all.Count >= maxEntries) throw new InvalidDataException("more entries than allowed");
                    var n = Describe(k.Ref, path + "/" + k.Name, k.Name);
                    all.Add(n);
                    if (n.Type == 1) Walk(k.Ref, n.Path, depth + 1);
                }
                visiting.Remove(dir);
            }
            Walk((long)RootRef, "", 0);
            return all;
        }

        /// <summary>Streams a regular file's bytes (holes as zeros) into sink, block by block; at most max bytes. Every block must expand to
        /// exactly what the file needs from it (a damaged block is an InvalidDataException).</summary>
        public void ReadFile(long reference, Stream sink, long max, Func<bool> cancelled = null)
        {
            var h = Inode((ulong)reference, 56); int type = U16(h, 0); long blocks, size; uint frag, foff; int hdr;
            if (type == 2) { blocks = U32(h, 16); frag = U32(h, 20); foff = U32(h, 24); size = U32(h, 28); hdr = 32; }
            else if (type == 9) { blocks = (long)U64(h, 16); size = (long)U64(h, 24); frag = U32(h, 44); foff = U32(h, 48); hdr = 56; }
            else throw new InvalidDataException("not a regular file");
            if (size < 0 || size > max || size > MaxFile) throw new InvalidDataException("a file larger than allowed");
            long full = frag == 0xFFFFFFFF ? (size + BlockSize - 1) / BlockSize : size / BlockSize;
            if (full > 4L * 1000 * 1000) throw new InvalidDataException("a file with more blocks than allowed");
            var sizes = Inode((ulong)reference, hdr + (int)(4 * full)); long at = blocks, written = 0; byte[] zero = null;
            for (long k = 0; k < full; k++)
            {
                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                uint w = U32(sizes, hdr + 4 * (int)k); int len = (int)(w & 0xFFFFFF); bool raw = (w & 0x1000000) != 0;
                int need = (int)Math.Min(BlockSize, size - written);
                if (len == 0) { zero = zero ?? new byte[BlockSize]; sink.Write(zero, 0, need); written += need; continue; }   // a hole
                if (len > BlockSize) throw new InvalidDataException("a damaged data block size");
                var d = At(at, len) ?? throw new InvalidDataException("data outside the file system"); at += len;
                byte[] b;
                try { b = raw ? d : Inflate(d, (int)BlockSize); }
                catch (InvalidDataException) { throw new InvalidDataException("a damaged data block"); }
                if (b.Length < need) throw new InvalidDataException("a damaged data block (it is shorter than the file needs)");
                sink.Write(b, 0, need); written += need;
            }
            if (written < size)
            {
                if (frag == 0xFFFFFFFF || frag >= FragCount) throw new InvalidDataException("a damaged fragment reference");
                var ptr = At((long)FragTable + 8 * (frag / 512), 8) ?? throw new InvalidDataException("a damaged fragment table");
                var fe = MetaRead(U64(ptr, 0), 0, (int)(frag % 512) * 16, 16);
                uint w = U32(fe, 8); int len = (int)(w & 0xFFFFFF); bool raw = (w & 0x1000000) != 0;
                if (len == 0 || len > BlockSize) throw new InvalidDataException("a damaged fragment");
                var d = At((long)U64(fe, 0), len) ?? throw new InvalidDataException("data outside the file system");
                byte[] b;
                try { b = raw ? d : Inflate(d, (int)BlockSize); }
                catch (InvalidDataException) { throw new InvalidDataException("a damaged fragment"); }
                int need = (int)(size - written); if (foff > b.Length || foff + need > b.Length) throw new InvalidDataException("a damaged fragment");
                sink.Write(b, (int)foff, need); written += need;
            }
        }
    }
}
