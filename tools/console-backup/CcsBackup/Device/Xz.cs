// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// A small read-only .xz decoder for the SquashFS reader: one xz stream at a time, held in memory, with the LZMA2 filter alone (what
// squashfs-tools writes with -comp xz and no -Xbcj, and what hakchi's own system dumps use). Written from the .xz file format
// specification (1.1.0) and the LZMA/LZMA2 descriptions; it checks the stream header, the block header, the block's check (none, CRC32,
// CRC64 or SHA-256), the index and the footer, and never produces more than the caller allows. Any other filter (the branch converters,
// delta) is refused by name, not guessed at.
using System;
using System.IO;
using System.Security.Cryptography;

namespace CcsBackup.Device
{
    public static class Xz
    {
        static readonly byte[] Magic = { 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00 };
        static readonly uint[] Crc32Table = MakeCrc32(); static readonly ulong[] Crc64Table = MakeCrc64();

        static uint[] MakeCrc32() { var t = new uint[256]; for (uint i = 0; i < 256; i++) { uint c = i; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[i] = c; } return t; }
        static ulong[] MakeCrc64() { var t = new ulong[256]; for (ulong i = 0; i < 256; i++) { ulong c = i; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xC96C5795D7870F42UL ^ (c >> 1) : c >> 1; t[i] = c; } return t; }
        public static uint Crc32(byte[] b, int o, int n) { uint c = 0xFFFFFFFF; for (int i = 0; i < n; i++) c = Crc32Table[(c ^ b[o + i]) & 0xFF] ^ (c >> 8); return ~c; }
        static ulong Crc64(byte[] b, int o, int n) { ulong c = ulong.MaxValue; for (int i = 0; i < n; i++) c = Crc64Table[(c ^ b[o + i]) & 0xFF] ^ (c >> 8); return ~c; }

        static InvalidDataException Bad(string why) => new InvalidDataException("a damaged xz block (" + why + ")");
        static uint U32(byte[] b, int o) => BitConverter.ToUInt32(b, o);

        static ulong Vli(byte[] b, ref int p, int end)
        {
            ulong v = 0;
            for (int i = 0; i < 9; i++)
            {
                if (p >= end) throw Bad("a size runs past its header");
                byte x = b[p++]; v |= (ulong)(x & 0x7F) << (7 * i);
                if ((x & 0x80) == 0) { if (i > 0 && x == 0) throw Bad("a size is not in its shortest form"); return v; }
            }
            throw Bad("a size is too long");
        }

        /// <summary>one whole .xz stream (exactly the bytes given, no stream padding) decoded; at most max bytes come out</summary>
        public static byte[] Decode(byte[] z, int max)
        {
            if (z == null || z.Length < 32) throw Bad("too short");
            for (int i = 0; i < 6; i++) if (z[i] != Magic[i]) throw Bad("not an xz stream");
            if (z[6] != 0 || z[7] > 0x0F || Crc32(z, 6, 2) != U32(z, 8)) throw Bad("its stream header");
            int check = z[7]; int checkSize = check == 0 ? 0 : check == 1 ? 4 : check == 4 ? 8 : check == 10 ? 32 : -1;
            if (checkSize < 0) throw Bad("a check type this version does not know");
            // the stream footer: CRC32, backward size, the same flags, "YZ"
            int f = z.Length - 12;
            if (z[f + 10] != 0x59 || z[f + 11] != 0x5A || z[f + 8] != z[6] || z[f + 9] != z[7] || Crc32(z, f + 4, 6) != U32(z, f)) throw Bad("its stream footer");
            long indexSize = ((long)U32(z, f + 4) + 1) * 4; long indexAt = f - indexSize;
            if (indexAt < 12) throw Bad("its index position");
            // the block header
            int p = 12; if (z[p] == 0) throw Bad("a stream with no block");
            int hsize = (z[p] + 1) * 4; if (p + hsize > indexAt || Crc32(z, p, hsize - 4) != U32(z, p + hsize - 4)) throw Bad("its block header");
            int hend = p + hsize - 4; byte flags = z[p + 1]; if ((flags & 0x3C) != 0) throw Bad("reserved block flags");
            int q = p + 2; long csize = -1, usize = -1;
            if ((flags & 0x40) != 0) csize = (long)Vli(z, ref q, hend);
            if ((flags & 0x80) != 0) usize = (long)Vli(z, ref q, hend);
            int filters = (flags & 3) + 1; int dictByte = -1;
            for (int k = 0; k < filters; k++)
            {
                ulong id = Vli(z, ref q, hend); ulong psize = Vli(z, ref q, hend);
                if ((long)psize > hend - q) throw Bad("a filter's properties run past its header");
                if (id != 0x21) throw new InvalidDataException("an xz filter this version cannot read (" + FilterName(id) + "): its contents cannot be checked here");
                if (k != filters - 1 || psize != 1) throw Bad("the LZMA2 filter's place or properties");
                dictByte = z[q]; q += 1;
            }
            for (; q < hend; q++) if (z[q] != 0) throw Bad("its block header padding");
            if (dictByte < 0 || dictByte > 40) throw Bad("the LZMA2 dictionary size");
            if (usize > max) throw new InvalidDataException("a block larger than allowed");
            // the compressed data, the block padding and the check
            int dataAt = p + hsize;
            var (outBytes, used) = Lzma2(z, dataAt, (int)indexAt, max);
            if (csize >= 0 && used != csize) throw Bad("its compressed size");
            if (usize >= 0 && outBytes.Length != usize) throw Bad("its uncompressed size");
            int after = dataAt + used; while ((after - p) % 4 != 0) { if (after >= indexAt || z[after] != 0) throw Bad("its block padding"); after++; }
            if (after + checkSize != indexAt) throw Bad("what follows the block");
            if (check == 1 && Crc32(outBytes, 0, outBytes.Length) != U32(z, after)) throw Bad("its CRC32 does not match");
            if (check == 4 && Crc64(outBytes, 0, outBytes.Length) != BitConverter.ToUInt64(z, after)) throw Bad("its CRC64 does not match");
            if (check == 10) using (var h = SHA256.Create()) { var d = h.ComputeHash(outBytes); for (int i = 0; i < 32; i++) if (d[i] != z[after + i]) throw Bad("its SHA-256 does not match"); }
            // the index: exactly this one block
            int ip = (int)indexAt, iend = f - 4; if (z[ip] != 0 || Crc32(z, ip, (int)indexSize - 4) != U32(z, f - 4)) throw Bad("its index");
            ip++; if (Vli(z, ref ip, iend) != 1) throw Bad("its index does not list one block");
            ulong unpadded = Vli(z, ref ip, iend), uncompressed = Vli(z, ref ip, iend);
            if ((long)unpadded != hsize + used + checkSize || (long)uncompressed != outBytes.Length) throw Bad("its index does not match the block");
            for (; ip < iend; ip++) if (z[ip] != 0) throw Bad("its index padding");
            return outBytes;
        }

        static string FilterName(ulong id) => id == 3 ? "delta" : id == 4 ? "x86 branch converter" : id == 5 ? "PowerPC branch converter" : id == 6 ? "IA-64 branch converter" : id == 7 ? "ARM branch converter" : id == 8 ? "ARM-Thumb branch converter" : id == 9 ? "SPARC branch converter" : id == 0x0A ? "ARM64 branch converter" : "filter 0x" + id.ToString("X");

        /// <summary>LZMA2 chunks from z[at] (not past end): the bytes they produce and how many input bytes they took (through the end marker)</summary>
        static (byte[] data, int used) Lzma2(byte[] z, int at, int end, int max)
        {
            // as the format asks: the first chunk resets the dictionary; a dictionary reset needs new properties in the next LZMA chunk;
            // the first LZMA chunk resets the state
            var o = new Out(max); var lz = new Lzma(); int p = at; bool needDict = true, needProps = true;
            while (true)
            {
                if (p >= end) throw Bad("the LZMA2 data has no end");
                byte c = z[p++];
                if (c == 0x00) break;
                if (c == 0x01 || c >= 0xE0) { o.ResetDict(); needDict = false; needProps = true; }
                else if (needDict) throw Bad("an LZMA2 chunk before its dictionary reset");
                if (c < 0x80)
                {
                    if (c > 0x02) throw Bad("an LZMA2 chunk type");
                    if (p + 2 > end) throw Bad("an LZMA2 chunk header");
                    int n = (z[p] << 8 | z[p + 1]) + 1; p += 2;
                    if (p + n > end) throw Bad("an LZMA2 chunk runs past the block");
                    o.Copy(z, p, n); p += n; continue;
                }
                if (p + 4 > end) throw Bad("an LZMA2 chunk header");
                int unpacked = ((c & 0x1F) << 16 | z[p] << 8 | z[p + 1]) + 1, packed = (z[p + 2] << 8 | z[p + 3]) + 1; p += 4;
                if (c >= 0xC0) { if (p >= end) throw Bad("an LZMA2 chunk header"); lz.SetProps(z[p++]); needProps = false; }
                else if (needProps) throw Bad("an LZMA2 chunk before its properties");
                if (c >= 0xA0) lz.ResetState(); else if (lz.NeedStateReset) throw Bad("an LZMA2 chunk before its state reset");
                if (p + packed > end) throw Bad("an LZMA2 chunk runs past the block");
                lz.Chunk(z, p, packed, o, unpacked); p += packed;
            }
            return (o.ToArray(), p - at);
        }

        /// <summary>the output, which is also the dictionary (one block is at most the caller's limit)</summary>
        sealed class Out
        {
            byte[] b = new byte[65536]; public int Pos, DictStart; readonly int max;
            public Out(int max) { this.max = max; }
            public void ResetDict() => DictStart = Pos;
            void Room(int n) { if ((long)Pos + n > max) throw new InvalidDataException("a block larger than allowed"); if (Pos + n > b.Length) Array.Resize(ref b, Math.Max(b.Length * 2, Pos + n)); }
            public void Put(byte x) { Room(1); b[Pos++] = x; }
            public void Copy(byte[] s, int o, int n) { Room(n); Buffer.BlockCopy(s, o, b, Pos, n); Pos += n; }
            public byte Back(uint dist) => dist < (uint)(Pos - DictStart) ? b[Pos - 1 - (int)dist] : (byte)0;   // dist 0: the last byte
            public bool Has(uint dist) => dist < (uint)(Pos - DictStart);
            public void Repeat(uint dist, int len) { Room(len); int from = Pos - 1 - (int)dist; for (int i = 0; i < len; i++) b[Pos++] = b[from + i]; }
            public byte[] ToArray() { var r = new byte[Pos]; Buffer.BlockCopy(b, 0, r, 0, Pos); return r; }
        }

        /// <summary>the LZMA decoder: a range decoder over one chunk's bytes, its probabilities and state kept between chunks as LZMA2 asks</summary>
        sealed class Lzma
        {
            const int States = 12, PosStatesMax = 16, LenToPos = 4, EndPosModel = 14, FullDistances = 128, AlignBits = 4;
            int lc, lp, pb; ushort[] lit;
            readonly ushort[] isMatch = new ushort[States * PosStatesMax], isRep = new ushort[States], isRepG0 = new ushort[States], isRepG1 = new ushort[States], isRepG2 = new ushort[States],
                isRep0Long = new ushort[States * PosStatesMax], posSlot = new ushort[LenToPos * 64], posSpecial = new ushort[FullDistances - EndPosModel + 1], align = new ushort[1 << AlignBits];
            readonly Len len = new Len(), repLen = new Len();
            int state; uint rep0, rep1, rep2, rep3; public bool NeedStateReset = true;
            // the range decoder
            byte[] z; int p, end; uint range, code;

            public void SetProps(byte d)
            {
                if (d >= 9 * 5 * 5) throw Bad("the LZMA properties");
                lc = d % 9; d /= 9; lp = d % 5; pb = d / 5;
                if (lc + lp > 4) throw Bad("the LZMA2 properties (lc + lp above 4)");
                lit = new ushort[0x300 << (lc + lp)];
            }

            public void ResetState()
            {
                if (lit == null) throw Bad("an LZMA2 chunk before its properties");
                foreach (var a in new[] { lit, isMatch, isRep, isRepG0, isRepG1, isRepG2, isRep0Long, posSlot, posSpecial, align }) for (int i = 0; i < a.Length; i++) a[i] = 1024;
                len.Reset(); repLen.Reset(); state = 0; rep0 = rep1 = rep2 = rep3 = 0; NeedStateReset = false;
            }

            byte Next() { if (p >= end) throw Bad("the LZMA data ends early"); return z[p++]; }
            void Normalize() { if (range < 1u << 24) { range <<= 8; code = code << 8 | Next(); } }

            int Bit(ushort[] probs, int i)
            {
                uint bound = (range >> 11) * probs[i];
                if (code < bound) { range = bound; probs[i] += (ushort)((2048 - probs[i]) >> 5); Normalize(); return 0; }
                range -= bound; code -= bound; probs[i] -= (ushort)(probs[i] >> 5); Normalize(); return 1;
            }

            int Tree(ushort[] probs, int at, int bits) { int m = 1; for (int i = 0; i < bits; i++) m = m << 1 | Bit(probs, at + m); return m - (1 << bits); }
            int Reverse(ushort[] probs, int at, int bits) { int m = 1, s = 0; for (int i = 0; i < bits; i++) { int b = Bit(probs, at + m); m = m << 1 | b; s |= b << i; } return s; }
            uint Direct(int bits) { uint r = 0; for (int i = 0; i < bits; i++) { range >>= 1; uint t = code >= range ? 1u : 0u; if (t == 1) code -= range; r = r << 1 | t; Normalize(); } return r; }

            sealed class Len
            {
                public readonly ushort[] choice = new ushort[2], low = new ushort[PosStatesMax << 3], mid = new ushort[PosStatesMax << 3], high = new ushort[256];
                public void Reset() { foreach (var a in new[] { choice, low, mid, high }) for (int i = 0; i < a.Length; i++) a[i] = 1024; }
            }

            int DecodeLen(Len l, int posState)
            {
                if (Bit(l.choice, 0) == 0) return Tree(l.low, posState << 3, 3);
                if (Bit(l.choice, 1) == 0) return 8 + Tree(l.mid, posState << 3, 3);
                return 16 + Tree(l.high, 0, 8);
            }

            /// <summary>one LZMA chunk: packed bytes z[at..at+packed), exactly unpacked bytes out</summary>
            public void Chunk(byte[] zz, int at, int packed, Out o, int unpacked)
            {
                z = zz; p = at; end = at + packed;
                if (Next() != 0) throw Bad("the range decoder's first byte");
                code = 0; for (int i = 0; i < 4; i++) code = code << 8 | Next(); range = 0xFFFFFFFF;
                if (code == range) throw Bad("the range decoder's start");
                int stop = o.Pos + unpacked; int pbMask = (1 << pb) - 1, lpMask = (1 << lp) - 1;
                while (o.Pos < stop)
                {
                    int pos = o.Pos - o.DictStart, posState = pos & pbMask;
                    if (Bit(isMatch, state * PosStatesMax + posState) == 0)
                    {
                        int prev = o.Has(0) ? o.Back(0) : 0;
                        int bas = 0x300 * (((pos & lpMask) << lc) + (prev >> (8 - lc)));
                        int sym = 1;
                        if (state < 7) { while (sym < 0x100) sym = sym << 1 | Bit(lit, bas + sym); }
                        else
                        {
                            if (!o.Has(rep0)) throw Bad("a match before the dictionary start");
                            int mb = o.Back(rep0);
                            while (sym < 0x100)
                            {
                                int mbit = (mb >> 7) & 1; mb <<= 1;
                                int b = Bit(lit, bas + 0x100 + (mbit << 8) + sym); sym = sym << 1 | b;
                                if (mbit != b) { while (sym < 0x100) sym = sym << 1 | Bit(lit, bas + sym); break; }
                            }
                        }
                        o.Put((byte)sym); state = state < 4 ? 0 : state < 10 ? state - 3 : state - 6;
                        continue;
                    }
                    int n;
                    if (Bit(isRep, state) == 1)
                    {
                        if (!o.Has(0)) throw Bad("a repeat with an empty dictionary");
                        if (Bit(isRepG0, state) == 0)
                        {
                            if (Bit(isRep0Long, state * PosStatesMax + posState) == 0)
                            {
                                state = state < 7 ? 9 : 11; if (!o.Has(rep0)) throw Bad("a match before the dictionary start"); o.Put(o.Back(rep0)); continue;
                            }
                        }
                        else
                        {
                            uint d;
                            if (Bit(isRepG1, state) == 0) d = rep1;
                            else { if (Bit(isRepG2, state) == 0) d = rep2; else { d = rep3; rep3 = rep2; } rep2 = rep1; }
                            rep1 = rep0; rep0 = d;
                        }
                        n = DecodeLen(repLen, posState); state = state < 7 ? 8 : 11;
                    }
                    else
                    {
                        rep3 = rep2; rep2 = rep1; rep1 = rep0;
                        n = DecodeLen(len, posState); state = state < 7 ? 7 : 10;
                        int slot = Tree(posSlot, Math.Min(n, LenToPos - 1) << 6, 6);
                        if (slot < 4) rep0 = (uint)slot;
                        else
                        {
                            int direct = (slot >> 1) - 1; uint dist = (uint)(2 | (slot & 1)) << direct;
                            if (slot < EndPosModel) dist += (uint)Reverse(posSpecial, (int)dist - slot - 1, direct);
                            else { dist += Direct(direct - AlignBits) << AlignBits; dist += (uint)Reverse(align, 0, AlignBits); }
                            rep0 = dist;
                        }
                        if (rep0 == 0xFFFFFFFF) throw Bad("an end marker inside an LZMA2 chunk");
                    }
                    n += 2;
                    if (!o.Has(rep0)) throw Bad("a match before the dictionary start");
                    if (o.Pos + n > stop) throw Bad("a match past the chunk's size");
                    o.Repeat(rep0, n);
                }
                if (p != end || code != 0) throw Bad("the chunk's compressed size or its end");
            }
        }
    }
}
