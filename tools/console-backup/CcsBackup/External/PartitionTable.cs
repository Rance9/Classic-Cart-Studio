// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// A drive's layout read from its first sectors (an image file, or the regions of a whole drive image inside a backup), read only:
//   - a GPT (the "EFI PART" header in the second sector: its disk identifier and up to 256 partition entries), or
//   - an MBR (the four entries of the first sector, and the logical partitions an extended partition chains, at most 128), or
//   - no partition table: a file system that starts in the first sector (a "superfloppy", as many SD cards and USB sticks are formatted).
// For each partition: what its first sectors say it holds, by the file system's own signature, never by the partition's type code alone:
// FAT12/FAT16/FAT32 (a valid BIOS parameter block), exFAT ("EXFAT" in its boot sector), NTFS ("NTFS" in its boot sector), ext2/ext3/ext4
// (the superblock's magic number 0xEF53 and its feature flags), or not known. Each partition's place must lie inside the drive, or it is
// listed with the reason and not read. FAT and ext file systems can be browsed inside a whole drive image (Fat.cs, Ext4/Ext4Reader.cs);
// exFAT and NTFS cannot in this version: their whole image is kept exactly, but its files are not listed (said plainly).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CcsBackup.External
{
    public sealed class PartitionInfo
    {
        public int Number; public long Offset, Length; public string Scheme, Type, FileSystem, Label, Serial, Problem;
        /// <summary>files inside it can be listed and read from the image in this version (FAT12/16/32, ext2/3/4)</summary>
        public bool Browsable => Problem == null && (FileSystem == "FAT12" || FileSystem == "FAT16" || FileSystem == "FAT32" || FileSystem == "ext2" || FileSystem == "ext3" || FileSystem == "ext4");
        /// <summary>why its files are not listed, in plain words (null when they are)</summary>
        public string NotBrowsable => Browsable ? null : Problem != null ? Problem
            : FileSystem == "exFAT" || FileSystem == "NTFS" ? "Its file system (" + FileSystem + ") is kept exactly in the image, but this version cannot list or browse the files inside an " + FileSystem + " image. To copy its files one by one, back it up with Files and Folders instead."
            : "Its file system is not one this version recognizes; it is kept exactly in the image, but its files are not listed.";
        public JObj ToJson() => new JObj { { "number", (long)Number }, { "offset", Offset }, { "length", Length }, { "scheme", Scheme }, { "type", Type }, { "file_system", FileSystem }, { "label", Label }, { "serial", Serial }, { "browsable", Browsable }, { "not_browsable", NotBrowsable } };
    }

    public sealed class DriveLayout
    {
        public string Scheme = "none", Signature; public long Bytes; public readonly List<PartitionInfo> Parts = new List<PartitionInfo>(); public readonly List<string> Notes = new List<string>();
        public JObj ToJson() => new JObj { { "scheme", Scheme }, { "signature", Signature }, { "bytes", Bytes }, { "partitions", Parts.Select(p => (object)p.ToJson()).ToList() }, { "notes", Notes } };
    }

    public static class PartitionTable
    {
        const int Sector = 512;

        static byte[] At(Stream s, long pos, int n)
        {
            if (pos < 0 || pos + n > s.Length) return null;
            var b = new byte[n]; s.Position = pos; int got = 0;
            while (got < n) { int r = s.Read(b, got, n - got); if (r <= 0) return null; got += r; }
            return b;
        }
        static ushort U16(byte[] b, int o) => BitConverter.ToUInt16(b, o);
        static uint U32(byte[] b, int o) => BitConverter.ToUInt32(b, o);
        static long U64(byte[] b, int o) => BitConverter.ToInt64(b, o);
        static string Guid(byte[] b, int o) { var g = new byte[16]; Buffer.BlockCopy(b, o, g, 0, 16); return new Guid(g).ToString(); }

        public static DriveLayout Read(Stream s)
        {
            var d = new DriveLayout { Bytes = s.Length };
            var mbr = At(s, 0, Sector);
            if (mbr == null) { d.Notes.Add("The drive is shorter than one sector."); return d; }
            // a file system in the first sector, with no partition table
            var whole = Detect(s, 0, s.Length);
            if (whole.FileSystem != null && whole.FileSystem != "unknown" && (whole.FileSystem.StartsWith("ext", StringComparison.Ordinal) || IsBootSector(mbr)))
            { d.Scheme = "none"; whole.Number = 1; whole.Scheme = "none"; whole.Type = "whole drive"; d.Parts.Add(whole); d.Signature = "none:" + whole.Serial; return d; }
            var gpt = At(s, Sector, Sector);
            if (gpt != null && Encoding.ASCII.GetString(gpt, 0, 8) == "EFI PART") { Gpt(s, gpt, d); return d; }
            if (mbr[510] != 0x55 || mbr[511] != 0xAA) { d.Notes.Add("No partition table and no file system this version recognizes was found at the start of the drive."); return d; }
            d.Scheme = "mbr"; d.Signature = "mbr:" + U32(mbr, 440).ToString("X8", CultureInfo.InvariantCulture);
            int n = 0;
            for (int i = 0; i < 4; i++)
            {
                int e = 446 + 16 * i; byte type = mbr[e + 4]; long lba = U32(mbr, e + 8), count = U32(mbr, e + 12);
                if (type == 0 || count == 0) continue;
                if (type == 0x05 || type == 0x0F || type == 0x85) { Logical(s, lba, d, ref n); continue; }
                Add(s, d, ++n, "mbr", "0x" + type.ToString("X2", CultureInfo.InvariantCulture), lba * Sector, count * Sector);
            }
            return d;
        }

        static void Logical(Stream s, long extStart, DriveLayout d, ref int n)
        {
            long ebr = extStart;
            for (int k = 0; k < 128; k++)
            {
                var b = At(s, ebr * Sector, Sector);
                if (b == null || b[510] != 0x55 || b[511] != 0xAA) { d.Notes.Add("A logical partition's table is damaged or outside the drive; the logical partitions after it are not listed."); return; }
                byte type = b[446 + 4]; long lba = U32(b, 446 + 8), count = U32(b, 446 + 12);
                if (type != 0 && count != 0) Add(s, d, ++n, "mbr-logical", "0x" + type.ToString("X2", CultureInfo.InvariantCulture), (ebr + lba) * Sector, count * Sector);
                long next = U32(b, 462 + 8); if (b[462 + 4] == 0 || next == 0) return;
                long nx = extStart + next; if (nx <= ebr) { d.Notes.Add("A logical partition chain points backwards; it is not followed further."); return; }
                ebr = nx;
            }
            d.Notes.Add("More than 128 logical partitions; the rest are not listed.");
        }

        static void Gpt(Stream s, byte[] h, DriveLayout d)
        {
            d.Scheme = "gpt"; d.Signature = "gpt:" + Guid(h, 56);
            long table = U64(h, 72); uint count = U32(h, 80), size = U32(h, 84);
            if (count == 0 || count > 256 || size < 128 || size > 1024 || table < 2) { d.Notes.Add("The GPT header's partition list is not one this version reads (" + count + " entries of " + size + " bytes)."); return; }
            var t = At(s, table * Sector, (int)(count * size));
            if (t == null) { d.Notes.Add("The GPT partition list lies outside the drive."); return; }
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                int o = (int)(i * size);
                if (t.Skip(o).Take(16).All(x => x == 0)) continue;
                long first = U64(t, o + 32), last = U64(t, o + 40);
                var name = Encoding.Unicode.GetString(t, o + 56, 72).TrimEnd('\0');
                var p = Add(s, d, ++n, "gpt", Guid(t, o), first * Sector, (last - first + 1) * Sector);
                if (string.IsNullOrEmpty(p.Label) && name.Length > 0) p.Label = name;
            }
        }

        static PartitionInfo Add(Stream s, DriveLayout d, int n, string scheme, string type, long off, long len)
        {
            PartitionInfo p;
            if (off <= 0 || len <= 0 || off + len > s.Length) p = new PartitionInfo { Problem = "Its place in the partition table lies outside the drive (from byte " + Words.N(off) + ", " + Words.N(len) + " bytes), so it is not read." };
            else p = Detect(s, off, len);
            p.Number = n; p.Scheme = scheme; p.Type = type; p.Offset = off; p.Length = len;
            if (p.FileSystem == null) p.FileSystem = "unknown";
            d.Parts.Add(p); return p;
        }

        static bool IsBootSector(byte[] b) => (b[0] == 0xEB && b[2] == 0x90 || b[0] == 0xE9) && (Fat.ValidBpb(b) || Encoding.ASCII.GetString(b, 3, 8) == "EXFAT   " || Encoding.ASCII.GetString(b, 3, 8) == "NTFS    ");

        /// <summary>what the first sectors at off say the partition holds (never its type code)</summary>
        public static PartitionInfo Detect(Stream s, long off, long len)
        {
            var p = new PartitionInfo { Offset = off, Length = len };
            var b = At(s, off, Sector);
            if (b == null) { p.Problem = "It could not be read."; return p; }
            var oem = Encoding.ASCII.GetString(b, 3, 8);
            if (oem == "EXFAT   ") { p.FileSystem = "exFAT"; p.Serial = U32(b, 100).ToString("X8", CultureInfo.InvariantCulture); return p; }
            if (oem == "NTFS    ") { p.FileSystem = "NTFS"; p.Serial = U64(b, 0x48).ToString("X16", CultureInfo.InvariantCulture); return p; }
            if (Fat.ValidBpb(b) && b[510] == 0x55 && b[511] == 0xAA)
            {
                var kind = Fat.KindOf(b);
                p.FileSystem = kind;
                int o = kind == "FAT32" ? 0x43 : 0x27;
                if (b[o - 1] == 0x29) { p.Serial = U32(b, o).ToString("X8", CultureInfo.InvariantCulture); var l = Encoding.ASCII.GetString(b, o + 4, 11).Trim(); if (l.Length > 0 && l != "NO NAME") p.Label = l; }
                return p;
            }
            var sb = At(s, off + 1024, 256);
            if (sb != null && U16(sb, 56) == 0xEF53)
            {
                uint compat = U32(sb, 92), incompat = U32(sb, 96), ro = U32(sb, 100);
                p.FileSystem = (incompat & 0x2C0) != 0 || (ro & 0x8) != 0 || (incompat & 0x40) != 0 ? "ext4" : (compat & 0x4) != 0 ? "ext3" : "ext2";
                p.Serial = Guid(sb, 104); var l = Encoding.ASCII.GetString(sb, 120, 16).TrimEnd('\0').Trim(); if (l.Length > 0) p.Label = l;
                return p;
            }
            p.FileSystem = "unknown";
            return p;
        }
    }
}
