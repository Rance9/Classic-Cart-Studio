// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// A read-only ext2/3/4 reader for a captured data partition image (a seekable stream: the stored member inside the backup file). It lists
// every entry with its functional metadata (type, permissions, owner, group, size, times, link count, inode, link target) and reads file
// contents, so the PC can describe what a backup holds (Classic Cart Studio's files, other programs' files, unknown files) and build the
// convenience .ccs export. It never writes and never needs the journal: an image whose journal still has to be replayed is reported as
// such (the capture is exact either way; nothing here repairs it). Supports extents and block maps, linear and hashed directories
// (read linearly), fast and slow symbolic links; inline data and encrypted entries are listed as unreadable. Bounds are checked on every
// read: a damaged or hostile image is an error, never an out-of-range read or an endless walk.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CcsBackup.Ext4
{
    public sealed class Ext4Entry
    {
        public string Path; public char Type; public int Mode; public int Uid, Gid; public long Size; public long Mtime; public int Links; public uint Inode;
        public string LinkTarget; public bool Unreadable; public string Note;
    }

    public sealed class Ext4Exception : Exception { public Ext4Exception(string m) : base(m) { } }

    public sealed class Ext4Reader
    {
        readonly Stream s; readonly long size;
        public readonly int BlockSize; public readonly uint InodesPerGroup, InodeCount, GroupCount; public readonly int InodeSize, DescSize;
        public readonly uint FeatureCompat, FeatureIncompat, FeatureRoCompat; public readonly ushort State; public readonly string VolumeName, LastMounted;
        public readonly long BlocksCount, FreeBlocks, ReservedBlocks; public readonly uint FreeInodes; readonly uint firstDataBlock, blocksPerGroup;
        public const int MaxEntries = 200000, MaxDepth = 64;

        public Ext4Reader(Stream seekable)
        {
            s = seekable; size = s.Length;
            var sb = Read(1024, 1024);
            if (U16(sb, 0x38) != 0xEF53) throw new Ext4Exception("the data partition is not an ext2/3/4 file system");
            InodeCount = U32(sb, 0); BlocksCount = U32(sb, 4); firstDataBlock = U32(sb, 0x14);
            int log = (int)U32(sb, 0x18); if (log > 6) throw new Ext4Exception("unsupported block size");
            BlockSize = 1024 << log; blocksPerGroup = U32(sb, 0x20); InodesPerGroup = U32(sb, 0x28);
            State = U16(sb, 0x3A); FeatureCompat = U32(sb, 0x5C); FeatureIncompat = U32(sb, 0x60); FeatureRoCompat = U32(sb, 0x64);
            InodeSize = U32(sb, 0x4C) >= 1 ? U16(sb, 0x58) : 128;
            ReservedBlocks = U32(sb, 8); FreeBlocks = U32(sb, 0x0C); FreeInodes = U32(sb, 0x10);   // as the file system itself records them
            if ((FeatureIncompat & 0x80) != 0) { BlocksCount |= (long)U32(sb, 0x150) << 32; ReservedBlocks |= (long)U32(sb, 0x154) << 32; FreeBlocks |= (long)U32(sb, 0x158) << 32; }
            DescSize = (FeatureIncompat & 0x80) != 0 ? Math.Max(32, (int)U16(sb, 0xFE)) : 32;
            VolumeName = Ascii(sb, 0x78, 16); LastMounted = Ascii(sb, 0x88, 64);
            if (blocksPerGroup == 0 || InodesPerGroup == 0 || InodeSize < 128 || InodeSize > BlockSize) throw new Ext4Exception("the data partition's file system header is damaged");
            GroupCount = (uint)((BlocksCount - firstDataBlock + blocksPerGroup - 1) / blocksPerGroup);
            if ((long)BlocksCount * BlockSize > size) throw new Ext4Exception("the file system is larger than the captured partition");
        }

        /// <summary>clean (state bit 1), or the journal still records an unfinished change (needs_recovery, incompat 0x4)</summary>
        public string Condition => (FeatureIncompat & 0x4) != 0 ? "needs-recovery" : (State & 1) != 0 ? "clean" : "not-cleanly-unmounted";

        static ushort U16(byte[] b, int o) => (ushort)(b[o] | b[o + 1] << 8);
        static uint U32(byte[] b, int o) => (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24);
        static string Ascii(byte[] b, int o, int n) { int e = o; while (e < o + n && b[e] != 0) e++; return Encoding.ASCII.GetString(b, o, e - o); }

        byte[] Read(long pos, int n)
        {
            if (pos < 0 || n < 0 || pos + n > size) throw new Ext4Exception("a file system structure points outside the partition");
            var b = new byte[n]; s.Position = pos; int got = 0;
            while (got < n) { int r = s.Read(b, got, n - got); if (r <= 0) throw new Ext4Exception("the partition ended early"); got += r; }
            return b;
        }
        byte[] Block(long blk) { if (blk <= 0 || blk >= BlocksCount) throw new Ext4Exception("a block number is outside the file system"); return Read(blk * BlockSize, BlockSize); }

        public byte[] Inode(uint ino)
        {
            if (ino < 1 || ino > InodeCount) throw new Ext4Exception("an inode number is outside the file system");
            uint g = (ino - 1) / InodesPerGroup, idx = (ino - 1) % InodesPerGroup;
            long descBlock = firstDataBlock + 1; long descPos = descBlock * BlockSize + (long)g * DescSize;
            var d = Read(descPos, DescSize);
            long table = U32(d, 8); if (DescSize >= 64) table |= (long)U32(d, 0x28) << 32;
            return Read(table * BlockSize + (long)idx * InodeSize, InodeSize);
        }

        /// <summary>the data blocks of an inode, in file order (0 = a hole)</summary>
        public List<long> Blocks(byte[] ino)
        {
            var list = new List<long>(); uint flags = U32(ino, 0x20);
            long sizeBytes = U32(ino, 4) | (long)U32(ino, 0x6C) << 32; long nblocks = (sizeBytes + BlockSize - 1) / BlockSize;
            if (nblocks > BlocksCount) throw new Ext4Exception("a file claims more blocks than the file system has");
            if ((flags & 0x10000000) != 0) throw new Ext4Exception("inline data");
            if ((flags & 0x80000) != 0)
            {
                var root = new byte[60]; Buffer.BlockCopy(ino, 0x28, root, 0, 60);
                Extents(root, 0, list, nblocks, 0);
                while (list.Count < nblocks) list.Add(0);
                if (list.Count > nblocks) list.RemoveRange((int)nblocks, list.Count - (int)nblocks);
                return list;
            }
            for (int i = 0; i < 12 && list.Count < nblocks; i++) list.Add(U32(ino, 0x28 + 4 * i));
            int per = BlockSize / 4;
            void Indirect(long blk, int level)
            {
                if (list.Count >= nblocks) return;
                if (blk == 0) { long hole = level == 1 ? per : level == 2 ? (long)per * per : (long)per * per * per; for (long k = 0; k < hole && list.Count < nblocks; k++) list.Add(0); return; }
                var b = Block(blk);
                for (int i = 0; i < per && list.Count < nblocks; i++) { long x = U32(b, 4 * i); if (level == 1) list.Add(x); else Indirect(x, level - 1); }
            }
            Indirect(U32(ino, 0x28 + 48), 1); Indirect(U32(ino, 0x28 + 52), 2); Indirect(U32(ino, 0x28 + 56), 3);
            return list;
        }

        void Extents(byte[] node, int depthGuard, List<long> outList, long nblocks, int o)
        {
            if (depthGuard > 8) throw new Ext4Exception("extent tree too deep");
            if (U16(node, o) != 0xF30A) throw new Ext4Exception("damaged extent tree");
            int entries = U16(node, o + 2), depth = U16(node, o + 6);
            if (entries > (node.Length - o - 12) / 12) throw new Ext4Exception("damaged extent tree");
            for (int i = 0; i < entries; i++)
            {
                int e = o + 12 + 12 * i;
                if (depth == 0)
                {
                    long lblk = U32(node, e); int len = U16(node, e + 4); bool uninit = len > 32768; if (uninit) len -= 32768;
                    long start = (long)U16(node, e + 6) << 32 | U32(node, e + 8);
                    if (lblk > nblocks) continue;
                    while (outList.Count < lblk) outList.Add(0);
                    for (int k = 0; k < len && outList.Count < nblocks; k++) outList.Add(uninit ? 0 : start + k);
                }
                else
                {
                    long child = (long)U16(node, e + 8) << 32 | U32(node, e + 4);
                    Extents(Block(child), depthGuard + 1, outList, nblocks, 0);
                }
            }
        }

        public long FileSize(byte[] ino) => U32(ino, 4) | (long)U32(ino, 0x6C) << 32;

        /// <summary>streams a regular file's content (holes as zeros) into sink; at most max bytes</summary>
        public void ReadFile(uint inode, Stream sink, long max = long.MaxValue)
        {
            var ino = Inode(inode); long left = FileSize(ino);
            if (left > max) throw new Ext4Exception("the file is larger than allowed here");
            var zero = new byte[BlockSize];
            foreach (var b in Blocks(ino))
            {
                if (left <= 0) break;
                int n = (int)Math.Min(BlockSize, left);
                sink.Write(b == 0 ? zero : Block(b), 0, n); left -= n;
            }
        }

        public byte[] ReadAll(uint inode, long max) { var m = new MemoryStream(); ReadFile(inode, m, max); return m.ToArray(); }

        /// <summary>a regular file as a read-only seekable stream (holes read as zeros; the same bytes ReadFile streams), for a file system
        /// image held inside this one (the Backup Browser's system images); its block list is read once</summary>
        public Stream OpenFile(uint inode)
        {
            var ino = Inode(inode);
            if ((U16(ino, 0) & 0xF000) != 0x8000) throw new Ext4Exception("not a regular file");
            return new FileView(this, Blocks(ino), FileSize(ino));
        }

        sealed class FileView : Stream
        {
            readonly Ext4Reader fs; readonly List<long> blocks; readonly long len; long pos;
            public FileView(Ext4Reader fs, List<long> blocks, long len) { this.fs = fs; this.blocks = blocks; this.len = len; }
            public override int Read(byte[] b, int o, int c)
            {
                if (pos >= len || c <= 0) return 0;
                int k = (int)(pos / fs.BlockSize), within = (int)(pos % fs.BlockSize);
                int n = (int)Math.Min(c, Math.Min(fs.BlockSize - within, len - pos));
                long blk = k < blocks.Count ? blocks[k] : 0;
                if (blk == 0) Array.Clear(b, o, n);
                else { var data = fs.Block(blk); Buffer.BlockCopy(data, within, b, o, n); }
                pos += n; return n;
            }
            public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
            public override long Length => len;
            public override long Position { get => pos; set { if (value < 0) throw new IOException("before the start of the file"); pos = value; } }
            public override long Seek(long o, SeekOrigin origin) { Position = origin == SeekOrigin.Begin ? o : origin == SeekOrigin.Current ? pos + o : len + o; return pos; }
            public override void Flush() { }
            public override void SetLength(long v) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        }

        IEnumerable<(string name, uint ino, byte type)> Dir(byte[] ino)
        {
            var size = FileSize(ino); long seen = 0;
            foreach (var blk in Blocks(ino))
            {
                if (seen >= size) yield break;
                seen += BlockSize; if (blk == 0) continue;
                var b = Block(blk); int p = 0;
                while (p + 8 <= BlockSize)
                {
                    uint child = U32(b, p); int rec = U16(b, p + 4), nl = b[p + 6]; byte ft = b[p + 7];
                    if (rec < 8 || p + rec > BlockSize || nl > rec - 8) break;
                    if (child != 0 && nl > 0)
                    {
                        var name = Encoding.UTF8.GetString(b, p + 8, nl);
                        if (name != "." && name != "..") yield return (name, child, ft);
                    }
                    p += rec;
                }
            }
        }

        /// <summary>every entry under the root, depth first, in name order (a hard link appears once per name, with the same inode)</summary>
        public List<Ext4Entry> List()
        {
            var all = new List<Ext4Entry>(); var visiting = new HashSet<uint>();
            void Walk(uint dino, string path, int depth)
            {
                if (depth > MaxDepth) throw new Ext4Exception("folders nest deeper than allowed");
                if (!visiting.Add(dino)) throw new Ext4Exception("a folder contains itself");
                var children = Dir(Inode(dino)).OrderBy(c => c.name, StringComparer.Ordinal).ToList();
                foreach (var c in children)
                {
                    if (all.Count >= MaxEntries) throw new Ext4Exception("more entries than allowed");
                    var e = Describe(c.ino, path + "/" + c.name);
                    all.Add(e);
                    if (e.Type == 'd' && !e.Unreadable) Walk(c.ino, e.Path, depth + 1);
                }
                visiting.Remove(dino);
            }
            Walk(2, "", 0);
            return all;
        }

        public Ext4Entry Describe(uint inode, string path)
        {
            var ino = Inode(inode); int mode = U16(ino, 0);
            var e = new Ext4Entry
            {
                Path = path, Inode = inode, Mode = mode & 0xFFF, Uid = U16(ino, 2) | U16(ino, 0x78) << 16, Gid = U16(ino, 0x18) | U16(ino, 0x7A) << 16,
                Size = FileSize(ino), Mtime = U32(ino, 0x10), Links = U16(ino, 0x1A)
            };
            switch (mode & 0xF000)
            {
                case 0x8000: e.Type = 'f'; break; case 0x4000: e.Type = 'd'; break; case 0xA000: e.Type = 'l'; break;
                case 0x2000: e.Type = 'c'; break; case 0x6000: e.Type = 'b'; break; case 0x1000: e.Type = 'p'; break; case 0xC000: e.Type = 's'; break;
                default: e.Type = '?'; e.Unreadable = true; e.Note = "unknown type"; break;
            }
            uint flags = U32(ino, 0x20);
            if ((flags & 0x800) != 0) { e.Unreadable = true; e.Note = "encrypted"; }
            else if ((flags & 0x10000000) != 0) { e.Unreadable = true; e.Note = "inline data (listed, not read on the PC)"; }
            else if (e.Type == 'l')
            {
                if (e.Size < 60 && (flags & 0x80000) == 0 && U32(ino, 0x1C) == 0) e.LinkTarget = Encoding.UTF8.GetString(ino, 0x28, (int)e.Size);
                else if (e.Size <= 4096) { try { e.LinkTarget = Encoding.UTF8.GetString(ReadAll(inode, 4096)); } catch (Ext4Exception x) { e.Unreadable = true; e.Note = x.Message; } }
            }
            return e;
        }

        /// <summary>resolves a path inside the file system without following links; null when absent</summary>
        public uint? Lookup(string path)
        {
            uint cur = 2;
            foreach (var seg in path.Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var ino = Inode(cur); if ((U16(ino, 0) & 0xF000) != 0x4000) return null;
                var hit = Dir(ino).Where(c => c.name == seg).Select(c => (uint?)c.ino).FirstOrDefault();
                if (hit == null) return null; cur = hit.Value;
            }
            return cur;
        }

        public bool IsRegular(uint inode) => (U16(Inode(inode), 0) & 0xF000) == 0x8000;
    }
}
