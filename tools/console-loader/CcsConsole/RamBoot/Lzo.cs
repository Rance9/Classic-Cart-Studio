// Classic Cart Studio console engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// LZO1X decoding for the console's startup ramdisks (the NES Classic's own kernels carry their ramdisk as an lzop file). An original
// implementation written from the documented formats, no third-party code:
//   - the LZO1X bitstream as the Linux kernel documents it (Documentation/staging/lzo.rst: "LZO stream format as understood by Linux's LZO
//     decompressor"), version 0 streams (the kind lzop writes);
//   - the lzop file around it (magic 89 4C 5A 4F 00 0D 0A 1A 0A, its header and its blocks, each with its stored and decoded sizes and the
//     checksums its flags announce), the framing the kernel's own unpacker reads (lib/decompress_unlzo.c).
// Every read is bounds-checked (input, output and look-behind); the caller gives the most it accepts. Every checksum the file carries is
// checked (the header's, and each block's Adler-32 or CRC-32 of its decoded and of its stored bytes); a file whose flags announce a filter
// or a feature this decoder does not read is refused by name, never guessed at. Nothing is executed or written to disk.
using System;
using System.IO;

namespace CcsConsole.RamBoot
{
    public static class Lzo
    {
        public static readonly byte[] Magic = { 0x89, 0x4C, 0x5A, 0x4F, 0x00, 0x0D, 0x0A, 0x1A, 0x0A };
        const uint AdlerD = 0x1, AdlerC = 0x2, Extra = 0x40, Multipart = 0x400, Filter = 0x800, HeaderCrc = 0x1000, CrcD = 0x100, CrcC = 0x200;
        /// <summary>the largest decoded block lzop writes (and the kernel accepts 256 KiB of it); bounded for every block</summary>
        public const int MaxBlock = 64 * 1024 * 1024;

        public static bool IsLzop(byte[] b) { if (b == null || b.Length < Magic.Length) return false; for (int i = 0; i < Magic.Length; i++) if (b[i] != Magic[i]) return false; return true; }

        /// <summary>Decodes an lzop file (as a kernel's ramdisk carries it) into at most max bytes. Throws InvalidDataException with the exact
        /// reason (damaged, a checksum that does not match, or a feature by name that this decoder does not read).</summary>
        public static byte[] DecodeLzop(byte[] src, long max)
        {
            if (!IsLzop(src)) throw new InvalidDataException("it is not an lzop file");
            int p = Magic.Length;
            int Need(int n) { if (n < 0 || p + n > src.Length) throw new InvalidDataException("the lzop file ends inside its header or a block"); int at = p; p += n; return at; }
            uint Be32() { int at = Need(4); return (uint)(src[at] << 24 | src[at + 1] << 16 | src[at + 2] << 8 | src[at + 3]); }
            int Be16() { int at = Need(2); return src[at] << 8 | src[at + 1]; }
            int hstart = p;
            int version = Be16(); Be16();   // the lzop version, then the LZO library version
            if (version < 0x0940) throw new InvalidDataException("it is an lzop file from a version older than 0.94 (" + version.ToString("x4") + "), whose header this decoder does not read");
            Be16();   // the lzop version needed to unpack it
            int method = src[Need(1)]; Need(1);   // method, level
            if (method < 1 || method > 3) throw new InvalidDataException("it is compressed with lzop method " + method + ", not LZO1X");
            uint flags = Be32();
            if ((flags & Filter) != 0) throw new InvalidDataException("it uses an lzop filter, which this decoder does not read");
            if ((flags & Multipart) != 0) throw new InvalidDataException("it is a multi-part lzop file, which this decoder does not read");
            if ((flags & Extra) != 0) throw new InvalidDataException("it carries an lzop extra field, which this decoder does not read");
            Be32(); Be32(); Be32();   // mode, mtime low, mtime high
            int nameLen = src[Need(1)]; Need(nameLen);
            uint hsum = Checksum(src, hstart, p - hstart, (flags & HeaderCrc) != 0);
            if (Be32() != hsum) throw new InvalidDataException("its lzop header checksum does not match (the file is damaged)");
            var outp = new MemoryStream();
            for (int block = 0; ; block++)
            {
                uint dlen = Be32();
                if (dlen == 0) break;
                if (dlen > MaxBlock) throw new InvalidDataException("block " + block + " says it decodes to " + dlen + " bytes, more than an lzop block holds");
                if (outp.Length + dlen > max) throw new InvalidDataException("it decodes to more than " + max + " bytes, more than this version reads from a ramdisk");
                uint slen = Be32();
                if (slen == 0 || slen > dlen) throw new InvalidDataException("block " + block + " has a stored size (" + slen + ") that does not fit its decoded size (" + dlen + ")");
                uint? ad = null, cd = null, ac = null, cc = null;
                if ((flags & AdlerD) != 0) ad = Be32();
                if ((flags & CrcD) != 0) cd = Be32();
                if ((flags & AdlerC) != 0) ac = slen < dlen ? Be32() : ad;
                if ((flags & CrcC) != 0) cc = slen < dlen ? Be32() : cd;
                int at = Need((int)slen);
                if (ac != null && Adler32(src, at, (int)slen) != ac) throw new InvalidDataException("block " + block + "'s stored bytes do not match their Adler-32 checksum (the file is damaged)");
                if (cc != null && Crc32(src, at, (int)slen) != cc) throw new InvalidDataException("block " + block + "'s stored bytes do not match their CRC-32 checksum (the file is damaged)");
                byte[] d;
                if (slen == dlen) { d = new byte[dlen]; Buffer.BlockCopy(src, at, d, 0, (int)dlen); }
                else
                {
                    d = new byte[dlen];
                    int got = Lzo1x(src, at, (int)slen, d);
                    if (got != dlen) throw new InvalidDataException("block " + block + " decodes to " + got + " bytes, not the " + dlen + " its header says");
                }
                if (ad != null && Adler32(d, 0, d.Length) != ad) throw new InvalidDataException("block " + block + "'s decoded bytes do not match their Adler-32 checksum (the file is damaged)");
                if (cd != null && Crc32(d, 0, d.Length) != cd) throw new InvalidDataException("block " + block + "'s decoded bytes do not match their CRC-32 checksum (the file is damaged)");
                outp.Write(d, 0, d.Length);
            }
            return outp.ToArray();
        }

        static uint Checksum(byte[] b, int at, int n, bool crc) => crc ? Crc32(b, at, n) : Adler32(b, at, n);

        /// <summary>Decodes one LZO1X block of n bytes at src[at] into dst (exactly its length at most); returns the decoded length. Every read
        /// is bounds-checked: a damaged block throws InvalidDataException, never reads or writes outside the arrays.</summary>
        public static int Lzo1x(byte[] src, int at, int n, byte[] dst)
        {
            int ip = at, end = at + n, op = 0, olen = dst.Length;
            if (n < 3) throw new InvalidDataException("an LZO block is too short");
            byte In() { if (ip >= end) throw new InvalidDataException("an LZO block ends early"); return src[ip++]; }
            void Lit(int k)
            {
                if (k > end - ip) throw new InvalidDataException("an LZO block ends inside its literal bytes");
                if (k > olen - op) throw new InvalidDataException("an LZO block decodes to more than its stated size");
                Buffer.BlockCopy(src, ip, dst, op, k); ip += k; op += k;
            }
            void Copy(int dist, int len)
            {
                if (dist <= 0 || dist > op) throw new InvalidDataException("an LZO block refers to bytes before its start");
                if (len > olen - op) throw new InvalidDataException("an LZO block decodes to more than its stated size");
                int from = op - dist;
                for (int i = 0; i < len; i++) dst[op++] = dst[from++];   // byte by byte: a copy may overlap what it writes
            }
            int Long(int t, int add) { if (t != 0) return t; int z = 0; byte b; while ((b = In()) == 0) { z += 255; if (z > 1 << 28) throw new InvalidDataException("an LZO length is too long"); } return add + z + b; }

            int state = 0;   // literals copied by the previous instruction: 0, 1-3, or 4 (four or more)
            int t = In();
            if (t == 17 && n >= 5 && src[ip] != 0) throw new InvalidDataException("it is an LZO stream of a later bitstream version, which this decoder does not read");
            if (t > 17) { int k = t - 17; Lit(k); state = k < 4 ? k : 4; t = In(); }
            for (;;)
            {
                if (t < 16)
                {
                    if (state == 0)
                    {   // a run of four or more literals
                        int k = 3 + Long(t, 15); Lit(k); state = 4; t = In(); continue;
                    }
                    int h = In();
                    if (state == 4) Copy((h << 2) + (t >> 2) + 2049, 3);   // three bytes from 2..3 kB back
                    else Copy((h << 2) + (t >> 2) + 1, 2);                 // two bytes from within 1 kB
                    state = t & 3;
                }
                else if (t < 32)
                {   // up to 48 kB back, or the end of the stream
                    int len = 2 + Long(t & 7, 7);
                    int lo = In(), hi = In(); int d = (hi << 6) | (lo >> 2);
                    int dist = 16384 + ((t & 8) << 11) + d;
                    if (dist == 16384)
                    {
                        if (ip != end) throw new InvalidDataException("an LZO block has bytes after its end mark");
                        return op;
                    }
                    Copy(dist, len); state = lo & 3;
                }
                else if (t < 64)
                {   // within 16 kB
                    int len = 2 + Long(t & 31, 31);
                    int lo = In(), hi = In(); Copy(((hi << 6) | (lo >> 2)) + 1, len); state = lo & 3;
                }
                else
                {   // 3-8 bytes within 2 kB
                    int h = In(); int len = t >= 128 ? 5 + ((t >> 5) & 3) : 3 + ((t >> 5) & 1);
                    Copy((h << 3) + ((t >> 2) & 7) + 1, len); state = t & 3;
                }
                if (state > 0 && state < 4) { Lit(state); }
                t = In();
            }
        }

        public static uint Adler32(byte[] b, int at, int n)
        {
            uint a = 1, s = 0; int i = at, end = at + n;
            while (i < end) { int k = Math.Min(5552, end - i); for (int j = 0; j < k; j++) { a += b[i++]; s += a; } a %= 65521; s %= 65521; }
            return s << 16 | a;
        }

        static uint[] crcTable;
        public static uint Crc32(byte[] b, int at, int n)
        {
            var tab = crcTable;
            if (tab == null)
            {
                tab = new uint[256];
                for (uint i = 0; i < 256; i++) { uint c = i; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; tab[i] = c; }
                crcTable = tab;
            }
            uint crc = 0xFFFFFFFF; for (int i = at; i < at + n; i++) crc = tab[(crc ^ b[i]) & 0xFF] ^ (crc >> 8);
            return ~crc;
        }
    }
}
