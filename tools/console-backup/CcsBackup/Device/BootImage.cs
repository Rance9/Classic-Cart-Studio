// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The NES Classic's boot-stage images as ccsboot (ram/ccsboot/ccsboot.c) checks them, so the PC refuses before a session what the console
// would refuse: boot0 ("eGON.BT" header) and boot1/u-boot ("uboot" header, optionally followed by one nandinfo block), each with its
// length in the header and its checksum correct (the header and checksum rules of hakchi's sntool, GPL-3.0). Nothing is ever fixed.
using System;
using System.Text;

namespace CcsBackup.Device
{
    public static class BootImage
    {
        public const int Boot0Max = 32 * 1024, Boot1Max = 0x20000 * 0x80 - 0x100000;
        const uint Stamp = 0x5f0a6c39u, NandInfoStamp = 0xae15bc34u, NandInfoMagic = 0xaa55a5a5u;

        static uint U32(byte[] d, int o) => (uint)(d[o] | d[o + 1] << 8 | d[o + 2] << 16 | d[o + 3] << 24);
        static bool Is(byte[] d, int at, string s) { if (at + 4 + s.Length > d.Length) return false; for (int i = 0; i < s.Length; i++) if (d[at + 4 + i] != (byte)s[i]) return false; return true; }
        static bool NandInfo(byte[] d, int at) => at + 4 <= d.Length && U32(d, at) == NandInfoMagic;
        static int LOffset(byte[] d, int at) => Is(d, at, "eGON.BT") ? 4 : Is(d, at, "uboot\0") ? 5 : NandInfo(d, at) ? 1 : 0;
        static int COffset(byte[] d, int at) => Is(d, at, "eGON.BT") ? 3 : Is(d, at, "uboot\0") ? 3 : NandInfo(d, at) ? 2 : 0;
        static uint Size(byte[] d, int at) { var o = LOffset(d, at); return o == 0 || at + 4 * o + 4 > d.Length ? 0 : U32(d, at + 4 * o); }
        static uint Sum(byte[] d, int at) { var o = COffset(d, at); return o == 0 || at + 4 * o + 4 > d.Length ? 0 : U32(d, at + 4 * o); }
        static uint BlockSum(byte[] d, int at, uint len)
        {
            uint c = (NandInfo(d, at) ? NandInfoStamp : Stamp) - Sum(d, at);
            for (uint i = 0; i < len / 4; i++) c += U32(d, at + (int)(4 * i));
            return c - Sum(d, at);
        }

        /// <summary>null when d is a complete, valid image of that stage; otherwise why not (as ccsboot check)</summary>
        public static string Check(byte[] d, bool boot0)
        {
            if (d == null || d.Length < 32 || d.Length % 4 != 0 || LOffset(d, 0) == 0 || NandInfo(d, 0)) return "not a boot image (no eGON.BT/uboot header)";
            if (boot0 != Is(d, 0, "eGON.BT")) return boot0 ? "not a boot0 (eGON.BT) image" : "not a boot1 (uboot) image";
            uint l = Size(d, 0);
            if (l < 32 || l % 4 != 0 || l > d.Length) return "bad length in header";
            if (BlockSum(d, 0, l) != 0) return "checksum does not match";
            if (l < d.Length)
            {
                int t = (int)l;
                if (d.Length - t < 16 || !NandInfo(d, t)) return "bytes after the image that are not a nandinfo block";
                uint nl = Size(d, t);
                if (nl < 16 || nl % 4 != 0 || t + nl != d.Length) return "nandinfo block length does not reach the end exactly";
                if (BlockSum(d, t, nl) != 0) return "nandinfo checksum does not match";
            }
            if (d.Length > (boot0 ? Boot0Max : Boot1Max)) return "larger than its area";
            return null;
        }

        /// <summary>a valid synthetic image (simulated consoles and tests only): kind boot0 or boot1, size a multiple of 4; boot1 may carry
        /// a nandinfo block of nandInfo bytes after it</summary>
        public static byte[] Synthetic(bool boot0, int size, string seed, int nandInfo = 0)
        {
            var d = new byte[size + nandInfo];
            var s = Encoding.ASCII.GetBytes(seed ?? "ccs");
            for (int i = 0; i < d.Length; i++) d[i] = (byte)(s[i % s.Length] + i / 251);
            void Put(int o, uint v) { d[o] = (byte)v; d[o + 1] = (byte)(v >> 8); d[o + 2] = (byte)(v >> 16); d[o + 3] = (byte)(v >> 24); }
            Put(0, 0x0e0000eau);
            if (boot0) { Encoding.ASCII.GetBytes("eGON.BT0").CopyTo(d, 4); Put(16, (uint)size); }
            else { Encoding.ASCII.GetBytes("uboot\0").CopyTo(d, 4); Put(20, (uint)size); }
            Put(12, 0); uint w = 0; for (int i = 0; i < size / 4; i++) w += U32(d, 4 * i); Put(12, w + Stamp);
            if (nandInfo > 0)
            {
                Put(size, NandInfoMagic); Put(size + 4, (uint)nandInfo); Put(size + 8, 0);
                uint n = 0; for (int i = 0; i < nandInfo / 4; i++) n += U32(d, size + 4 * i); Put(size + 8, n + NandInfoStamp);
            }
            return d;
        }
    }
}
