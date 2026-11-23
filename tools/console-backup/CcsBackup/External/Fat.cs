// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// A read-only FAT12/FAT16/FAT32 reader for a file system inside a whole drive image (written from Microsoft's FAT specification, version
// 1.03, and the long-name (VFAT) entries it describes). It lists every folder, file and long name, gives each file's exact bytes and its
// extents (where its clusters lie in the image, merged into runs), and never writes. Untrusted input: every number is checked against the
// file system's own size, a cluster chain that loops, leaves the data area or ends before the file does is reported for that file (it is
// listed as unreadable with the reason), the whole tree is limited to ExternalFormat.MaxEntries entries and 64 folder levels, and the
// file allocation table is read only when it fits in 512 MB of memory.
// FAT records times in the local time of whatever device wrote them, without a time zone: they are kept exactly as recorded and labeled so.
// The creation time has 10-millisecond steps, the modification time 2-second steps and the last-access time a date only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CcsBackup.External
{
    public sealed class FatEntry
    {
        public string Path, Name, ShortName; public bool Directory; public long Size; public uint FirstCluster; public byte Attributes;
        /// <summary>the recorded times as written (local time of the writing device, no time zone); null when the field is empty or invalid</summary>
        public DateTime? Modified, Created, Accessed;
        public string Problem;   // why its bytes cannot be read (a damaged cluster chain); null when they can
    }

    public sealed class FatException : Exception { public FatException(string m) : base(m) { } }

    public sealed class Fat
    {
        readonly Stream s; readonly long baseOff, length;
        public readonly string Kind; public readonly int BytesPerSector, SectorsPerCluster, ClusterBytes;
        readonly long reserved, fatCount, fatSectors, rootEntries, totalSectors, rootStart, dataStart; readonly uint rootCluster; public readonly long Clusters;
        byte[] fat;
        public string Label;

        public static bool ValidBpb(byte[] b)
        {
            if (b == null || b.Length < 512) return false;
            int bps = BitConverter.ToUInt16(b, 11), spc = b[13], res = BitConverter.ToUInt16(b, 14), nf = b[16];
            if (bps != 512 && bps != 1024 && bps != 2048 && bps != 4096) return false;
            if (spc == 0 || (spc & (spc - 1)) != 0 || res == 0 || nf < 1 || nf > 4) return false;
            long tot = BitConverter.ToUInt16(b, 19); if (tot == 0) tot = BitConverter.ToUInt32(b, 32);
            long fsz = BitConverter.ToUInt16(b, 22); if (fsz == 0) fsz = BitConverter.ToUInt32(b, 36);
            return tot > 0 && fsz > 0;
        }

        /// <summary>FAT12, FAT16 or FAT32, by the count of clusters (the specification's only rule), from a valid boot sector</summary>
        public static string KindOf(byte[] b)
        {
            int bps = BitConverter.ToUInt16(b, 11), spc = b[13], res = BitConverter.ToUInt16(b, 14), nf = b[16], re = BitConverter.ToUInt16(b, 17);
            long tot = BitConverter.ToUInt16(b, 19); if (tot == 0) tot = BitConverter.ToUInt32(b, 32);
            long fsz = BitConverter.ToUInt16(b, 22); if (fsz == 0) fsz = BitConverter.ToUInt32(b, 36);
            long rootSec = (re * 32L + bps - 1) / bps; long data = tot - (res + nf * fsz + rootSec); long cl = data / spc;
            return cl < 4085 ? "FAT12" : cl < 65525 ? "FAT16" : "FAT32";
        }

        public Fat(Stream stream, long offset, long len)
        {
            s = stream; baseOff = offset; length = len;
            var b = Read(0, 512);
            if (!ValidBpb(b)) throw new FatException("its boot sector is not a valid FAT boot sector");
            Kind = KindOf(b); BytesPerSector = BitConverter.ToUInt16(b, 11); SectorsPerCluster = b[13]; ClusterBytes = BytesPerSector * SectorsPerCluster;
            reserved = BitConverter.ToUInt16(b, 14); fatCount = b[16]; rootEntries = BitConverter.ToUInt16(b, 17);
            totalSectors = BitConverter.ToUInt16(b, 19); if (totalSectors == 0) totalSectors = BitConverter.ToUInt32(b, 32);
            fatSectors = BitConverter.ToUInt16(b, 22); if (fatSectors == 0) fatSectors = BitConverter.ToUInt32(b, 36);
            if (totalSectors * BytesPerSector > len) throw new FatException("the file system says it is larger than its partition");
            long rootSec = (rootEntries * 32 + BytesPerSector - 1) / BytesPerSector;
            rootStart = reserved + fatCount * fatSectors; dataStart = rootStart + rootSec;
            Clusters = (totalSectors - dataStart) / SectorsPerCluster;
            if (Clusters <= 0) throw new FatException("it has no data area");
            if (Kind == "FAT32") { rootCluster = BitConverter.ToUInt32(b, 44); if (rootCluster < 2 || rootCluster >= Clusters + 2) throw new FatException("its root folder's cluster is outside the file system"); }
            long fatBytes = fatSectors * BytesPerSector;
            if (fatBytes > 512L * 1024 * 1024) throw new FatException("its allocation table is too large for this version to read (" + Words.N(fatBytes) + " bytes)");
            fat = Read(reserved * BytesPerSector, (int)fatBytes);
            int lo = Kind == "FAT32" ? 0x47 : 0x2B; if (b[lo - 5] == 0x29) { var l = Encoding.ASCII.GetString(b, lo, 11).Trim(); if (l.Length > 0 && l != "NO NAME") Label = l; }
        }

        byte[] Read(long pos, int n)
        {
            if (pos < 0 || pos + n > length) throw new FatException("a structure lies outside the file system");
            var b = new byte[n]; s.Position = baseOff + pos; int got = 0;
            while (got < n) { int r = s.Read(b, got, n - got); if (r <= 0) throw new FatException("the image ended inside the file system"); got += r; }
            return b;
        }

        uint Next(uint c)
        {
            if (Kind == "FAT32") { long o = c * 4L; if (o + 4 > fat.Length) return 0x0FFFFFF7; return BitConverter.ToUInt32(fat, (int)o) & 0x0FFFFFFF; }
            if (Kind == "FAT16") { long o = c * 2L; if (o + 2 > fat.Length) return 0xFFF7; return BitConverter.ToUInt16(fat, (int)o); }
            { long o = c + c / 2; if (o + 2 > fat.Length) return 0xFF7; int v = BitConverter.ToUInt16(fat, (int)o); return (uint)((c & 1) != 0 ? v >> 4 : v & 0xFFF); }
        }
        bool End(uint v) => Kind == "FAT32" ? v >= 0x0FFFFFF8 : Kind == "FAT16" ? v >= 0xFFF8 : v >= 0xFF8;
        bool Bad(uint v) => Kind == "FAT32" ? v == 0x0FFFFFF7 : Kind == "FAT16" ? v == 0xFFF7 : v == 0xFF7;

        /// <summary>the clusters of a chain in order; a chain that loops, leaves the data area or meets a bad-cluster mark throws</summary>
        public List<uint> Chain(uint first, long maxClusters)
        {
            var l = new List<uint>(); var seen = new HashSet<uint>(); uint c = first;
            while (true)
            {
                if (c < 2 || c >= Clusters + 2) throw new FatException("a cluster chain leaves the data area (cluster " + c + ")");
                if (!seen.Add(c)) throw new FatException("a cluster chain loops back on itself");
                l.Add(c); if (l.Count > maxClusters) throw new FatException("a cluster chain is longer than its file");
                var n = Next(c);
                if (End(n)) return l;
                if (Bad(n) || n == 0) throw new FatException("a cluster chain is broken (a free or bad-cluster mark inside it)");
                c = n;
            }
        }

        long ClusterOffset(uint c) => (dataStart + (long)(c - 2) * SectorsPerCluster) * BytesPerSector;

        /// <summary>every folder and file, the root's contents first, each folder's in its order</summary>
        public List<FatEntry> List(Func<bool> cancelled = null)
        {
            var all = new List<FatEntry>(); var stack = new Stack<(string path, uint cluster, int depth)>(); var dirsSeen = new HashSet<uint>();
            Dir("", Kind == "FAT32" ? rootCluster : 0, 0, all, stack, dirsSeen);
            while (stack.Count > 0)
            {
                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                var (p, c, d) = stack.Pop();
                Dir(p, c, d, all, stack, dirsSeen);
            }
            return all;
        }

        void Dir(string path, uint cluster, int depth, List<FatEntry> all, Stack<(string, uint, int)> stack, HashSet<uint> seen)
        {
            byte[] data;
            if (cluster == 0 && Kind != "FAT32") data = Read(rootStart * BytesPerSector, (int)(rootEntries * 32));
            else
            {
                if (!seen.Add(cluster)) return;   // a folder reached twice (a damaged file system) is listed once
                var ch = Chain(cluster, 65536);
                var m = new MemoryStream(); foreach (var c in ch) { var b = Read(ClusterOffset(c), ClusterBytes); m.Write(b, 0, b.Length); }
                data = m.ToArray();
            }
            var lfn = new List<(int seq, string part)>(); byte lfnSum = 0;
            var kids = new List<FatEntry>();
            for (int o = 0; o + 32 <= data.Length; o += 32)
            {
                byte first = data[o]; if (first == 0) break;
                byte attr = data[o + 11];
                if (first == 0xE5) { lfn.Clear(); continue; }
                if ((attr & 0x3F) == 0x0F)
                {
                    int seq = first & 0x1F; if ((first & 0x40) != 0) { lfn.Clear(); lfnSum = data[o + 13]; }
                    var chars = new byte[26]; Buffer.BlockCopy(data, o + 1, chars, 0, 10); Buffer.BlockCopy(data, o + 14, chars, 10, 12); Buffer.BlockCopy(data, o + 28, chars, 22, 4);
                    var part = Encoding.Unicode.GetString(chars); int z = part.IndexOf('\0'); if (z >= 0) part = part.Substring(0, z);
                    lfn.Add((seq, part)); continue;
                }
                if ((attr & 0x08) != 0) { if (path == "" && Label == null) Label = Encoding.ASCII.GetString(data, o, 11).Trim(); lfn.Clear(); continue; }
                var raw = new byte[11]; Buffer.BlockCopy(data, o, raw, 0, 11); if (raw[0] == 0x05) raw[0] = 0xE5;
                var bas = Encoding.GetEncoding(437).GetString(raw, 0, 8).TrimEnd(' '); var ext = Encoding.GetEncoding(437).GetString(raw, 8, 3).TrimEnd(' ');
                if ((data[o + 12] & 0x08) != 0) bas = bas.ToLowerInvariant(); if ((data[o + 12] & 0x10) != 0) ext = ext.ToLowerInvariant();
                var shortName = ext.Length > 0 ? bas + "." + ext : bas;
                string name = shortName;
                if (lfn.Count > 0)
                {
                    byte sum = 0; for (int i = 0; i < 11; i++) sum = (byte)(((sum & 1) << 7) + (sum >> 1) + data[o + i]);
                    if (sum == lfnSum && lfn.Select(x => x.seq).OrderBy(x => x).SequenceEqual(Enumerable.Range(1, lfn.Count))) name = string.Concat(lfn.OrderBy(x => x.seq).Select(x => x.part));
                }
                lfn.Clear();
                if (name == "." || name == "..") continue;
                var e = new FatEntry
                {
                    Name = name, ShortName = shortName, Path = path + "/" + name, Attributes = attr, Directory = (attr & 0x10) != 0, Size = BitConverter.ToUInt32(data, o + 28),
                    FirstCluster = (uint)(BitConverter.ToUInt16(data, o + 26) | (Kind == "FAT32" ? BitConverter.ToUInt16(data, o + 20) << 16 : 0)),
                    Modified = DosTime(BitConverter.ToUInt16(data, o + 24), BitConverter.ToUInt16(data, o + 22), 0),
                    Created = DosTime(BitConverter.ToUInt16(data, o + 16), BitConverter.ToUInt16(data, o + 14), data[o + 13]),
                    Accessed = DosTime(BitConverter.ToUInt16(data, o + 18), 0, 0),
                };
                if (e.Directory) e.Size = 0;
                if (e.Name.Length == 0 || e.Name.IndexOf('/') >= 0 || e.Name.IndexOf('\0') >= 0) continue;   // a name a FAT file system never holds: not listed
                kids.Add(e);
            }
            foreach (var e in kids)
            {
                if (all.Count >= ExternalFormat.MaxEntries) throw new FatException("the file system holds more than " + Words.N(ExternalFormat.MaxEntries) + " entries");
                all.Add(e);
                if (e.Directory)
                {
                    if (depth + 1 > 64) { e.Problem = "it is more than 64 folders deep; what is inside it is not listed"; continue; }
                    if (e.FirstCluster == 0) continue;
                    stack.Push((e.Path, e.FirstCluster, depth + 1));
                }
                else if (e.Size > 0)
                {
                    try { var ch = Chain(e.FirstCluster, (e.Size + ClusterBytes - 1) / ClusterBytes); if ((long)ch.Count * ClusterBytes < e.Size) e.Problem = "its cluster chain ends before the file does"; }
                    catch (FatException ex) { e.Problem = ex.Message; }
                }
            }
        }

        static DateTime? DosTime(ushort date, ushort time, byte tenMs)
        {
            if (date == 0) return null;
            int y = 1980 + (date >> 9), mo = (date >> 5) & 15, d = date & 31, h = time >> 11, mi = (time >> 5) & 63, sec = (time & 31) * 2;
            if (mo < 1 || mo > 12 || d < 1 || d > DateTime.DaysInMonth(y, mo) || h > 23 || mi > 59 || sec > 59) return null;
            return new DateTime(y, mo, d, h, mi, sec, DateTimeKind.Unspecified).AddMilliseconds(tenMs * 10);
        }

        /// <summary>where the file's bytes lie, in the stream this reader reads (its partition's place included), as merged runs</summary>
        public List<(long offset, long length)> Extents(FatEntry e)
        {
            var runs = new List<(long, long)>(); if (e.Directory || e.Size == 0) return runs;
            if (e.Problem != null) throw new FatException(e.Problem);
            long left = e.Size;
            foreach (var c in Chain(e.FirstCluster, (e.Size + ClusterBytes - 1) / ClusterBytes))
            {
                long n = Math.Min(left, ClusterBytes); long at = baseOff + ClusterOffset(c);
                if (runs.Count > 0 && runs[runs.Count - 1].Item1 + runs[runs.Count - 1].Item2 == at) runs[runs.Count - 1] = (runs[runs.Count - 1].Item1, runs[runs.Count - 1].Item2 + n);
                else runs.Add((at, n));
                left -= n; if (left == 0) break;
            }
            if (left > 0) throw new FatException("its cluster chain ends before the file does");
            return runs;
        }

        public void ReadFile(FatEntry e, Stream sink, Func<bool> cancelled = null)
        {
            var buf = new byte[1 << 20];
            foreach (var (off, len) in Extents(e))
            {
                long left = len; s.Position = off;
                while (left > 0)
                {
                    if (cancelled != null && cancelled()) throw new OperationCanceledException();
                    int r = s.Read(buf, 0, (int)Math.Min(buf.Length, left)); if (r <= 0) throw new FatException("the image ended inside a file");
                    sink.Write(buf, 0, r); left -= r;
                }
            }
        }

        /// <summary>a recorded FAT time as written ("2026-11-20T14:03:22.10"), with no time zone because FAT records none; null stays null</summary>
        public static string Local(DateTime? t) => t?.ToString("yyyy-MM-dd'T'HH:mm:ss.ff", System.Globalization.CultureInfo.InvariantCulture);

        public static string AttributeWords(byte a)
        {
            var l = new List<string>(); if ((a & 1) != 0) l.Add("read-only"); if ((a & 2) != 0) l.Add("hidden"); if ((a & 4) != 0) l.Add("system"); if ((a & 0x20) != 0) l.Add("archive");
            return string.Join(", ", l);
        }
    }
}
