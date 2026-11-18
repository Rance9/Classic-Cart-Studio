// Classic Cart Studio console engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// What a console startup image is made of, read only, in memory, bounded (shared by the Backup Browser and Developer Mode's checks):
//   - BootParts: an Android boot image (header version 0, the container both the NES Classic's own kernels and hakchi's use): its header
//     (name, page size, command line), its kernel (an ARM zImage), its ramdisk (the compressed startup file system the kernel unpacks into
//     memory) and its second stage when it has one, each by its place in the image. It is a container of parts, not a bootloader (boot0
//     and boot1/U-Boot are separate stages of the console) and not itself a file system.
//   - Ramdisk: the ramdisk decoded (gzip, LZO through Lzo.cs, or a plain archive) and its files listed from the cpio "newc" archive inside
//     it (the format the kernel's initramfs unpacker reads), with their metadata. Other compressions (xz, LZMA, LZ4, bzip2, zstd) are named
//     and not decoded here.
//   - SystemKey: the key the console's startup uses to open its encrypted system part, where hakchi's own startup looks for it (its /init:
//     a "hakchi-key-file=base64:" word on the command line, or a file named key-file in a stored ramdisk). The key's bytes are returned only
//     to the caller's memory; nothing here prints, records or writes them. A key file is used only when it is exactly the 32 bytes the
//     system part's cipher takes (cryptsetup's plain mode with aes-xts-plain and its default 256-bit key: AES-128 in XTS mode). Finding a key
//     proves nothing on its own: the caller must check what it decrypts to.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace CcsConsole.RamBoot
{
    public sealed class BootParts
    {
        public int Page; public uint Version, KernelSize, RamdiskSize, SecondSize; public string Name, Cmdline;
        public long KernelAt, RamdiskAt, SecondAt, Described;
        public string RamdiskFormat;   // gzip, lzo, cpio, xz, lzma, lz4, bzip2, zstd, none, or null (not recognized)
        public string Problem;         // why its parts cannot be read (null: they can)
        public bool Ok => Problem == null;
        /// <summary>the command line with any key it carries replaced (never shown or recorded)</summary>
        public string MaskedCmdline => string.Join(" ", (Cmdline ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(StartupImage.Mask));
        public bool CmdlineCarriesKey => (Cmdline ?? "").Split(' ').Any(w => w.StartsWith("hakchi-key-file=", StringComparison.Ordinal));

        public static string FormatName(string f) => f == "lzo" ? "LZO" : f == "gzip" ? "gzip" : f == "cpio" ? "not compressed" : f == "xz" ? "xz" : f == "lzma" ? "LZMA" : f == "lz4" ? "LZ4" : f == "bzip2" ? "bzip2" : f == "zstd" ? "zstd" : f ?? "not recognized";

        /// <summary>the parts of an Android boot image of length bytes, from its first page (at least 2,048 bytes); and the first bytes of its
        /// ramdisk to name its compression (ramdiskHead may be null)</summary>
        public static BootParts Parse(byte[] head, long length)
        {
            var b = new BootParts();
            if (head == null || head.Length < 2048 || Encoding.ASCII.GetString(head, 0, 8) != "ANDROID!") { b.Problem = "it is not an Android boot image"; return b; }
            uint U(int o) => BitConverter.ToUInt32(head, o);
            b.KernelSize = U(8); b.RamdiskSize = U(16); b.SecondSize = U(24); uint ps = U(36); b.Version = U(40);
            if (ps != 2048 && ps != 4096 && ps != 8192 && ps != 16384) { b.Problem = "its page size (" + ps + ") is not one an Android boot image uses"; return b; }
            if (b.Version != 0) { b.Problem = "it is boot image header version " + b.Version + ", which this version does not read (the console's startup uses version 0)"; return b; }
            b.Page = (int)ps;
            long Pages(long n) => (n + ps - 1) / ps;
            b.KernelAt = ps; b.RamdiskAt = ps * (1 + Pages(b.KernelSize)); b.SecondAt = ps * (1 + Pages(b.KernelSize) + Pages(b.RamdiskSize));
            b.Described = ps * (1 + Pages(b.KernelSize) + Pages(b.RamdiskSize) + Pages(b.SecondSize));
            int n0 = Array.IndexOf(head, (byte)0, 48, 16); b.Name = Encoding.ASCII.GetString(head, 48, (n0 < 0 ? 64 : n0) - 48);
            int nul = Array.IndexOf(head, (byte)0, 64, 512); b.Cmdline = Encoding.ASCII.GetString(head, 64, (nul < 0 ? 576 : nul) - 64);
            if (b.KernelSize == 0) { b.Problem = "it has no kernel"; return b; }
            if (b.Described > length) { b.Problem = "it is shorter (" + length + " bytes) than its header describes (" + b.Described + " bytes): it is cut off or damaged"; return b; }
            return b;
        }

        /// <summary>the compression of a ramdisk, by its first bytes</summary>
        public static string Format(byte[] r)
        {
            bool S(params int[] m) => r != null && r.Length >= m.Length && m.Select((x, i) => r[i] == x).All(x => x);
            return r == null || r.Length == 0 ? "none" : S(0x1F, 0x8B) ? "gzip" : Lzo.IsLzop(r) ? "lzo" : S(0x30, 0x37, 0x30, 0x37, 0x30) ? "cpio" : S(0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00) ? "xz"
                : S(0x5D, 0x00, 0x00) ? "lzma" : S(0x02, 0x21, 0x4C, 0x18) ? "lz4" : S(0x42, 0x5A, 0x68) ? "bzip2" : S(0x28, 0xB5, 0x2F, 0xFD) ? "zstd" : null;
        }
    }

    /// <summary>one entry of a ramdisk's cpio (newc) archive</summary>
    public sealed class CpioEntry
    {
        public string Path, Name, LinkTarget, Problem; public int Mode, Uid, Gid, Links; public uint Ino, DevMajor, DevMinor, RdevMajor, RdevMinor; public long Mtime, Size, DataAt;
        public char TypeCode => Type(Mode);
        public static char Type(int mode) { switch (mode & 0xF000) { case 0x8000: return 'f'; case 0x4000: return 'd'; case 0xA000: return 'l'; case 0x2000: return 'c'; case 0x6000: return 'b'; case 0x1000: return 'p'; case 0xC000: return 's'; default: return '?'; } }
    }

    public sealed class Ramdisk
    {
        public const long MaxDecoded = 64_000_000;   // the console's own ramdisks decode to about 1.5 MB
        public const int MaxEntries = 20000;
        public string Format; public byte[] Data; public readonly List<CpioEntry> Entries = new List<CpioEntry>(); public int Replaced;

        /// <summary>decodes a stored ramdisk (gzip, LZO or a plain archive) and lists its cpio archive. Throws InvalidDataException (damaged,
        /// with the reason) or NotSupportedException (a compression or archive this version does not read, by name).</summary>
        public static Ramdisk Decode(byte[] stored)
        {
            var r = new Ramdisk { Format = BootParts.Format(stored) };
            switch (r.Format)
            {
                case "lzo": r.Data = Lzo.DecodeLzop(stored, MaxDecoded); break;
                case "gzip": r.Data = Gunzip(stored); break;
                case "cpio": r.Data = stored; break;
                case "none": throw new NotSupportedException("it has no ramdisk");
                case null: throw new NotSupportedException("its ramdisk is not in a format a kernel unpacks");
                default: throw new NotSupportedException("its ramdisk is " + BootParts.FormatName(r.Format) + "-compressed, which this version does not decode");
            }
            r.List();
            return r;
        }

        static byte[] Gunzip(byte[] stored)
        {
            try
            {
                using (var g = new GZipStream(new MemoryStream(stored, false), CompressionMode.Decompress))
                {
                    var o = new MemoryStream(); var buf = new byte[1 << 16]; int n;
                    while ((n = g.Read(buf, 0, buf.Length)) > 0) { o.Write(buf, 0, n); if (o.Length > MaxDecoded) throw new InvalidDataException("it decodes to more than " + MaxDecoded + " bytes, more than this version reads from a ramdisk"); }
                    return o.ToArray();
                }
            }
            catch (Exception ex) when (ex is IOException && !(ex is InvalidDataException)) { throw new InvalidDataException("its gzip data is damaged (" + ex.Message.TrimEnd('.') + ")"); }
        }

        void List()
        {
            var b = Data; long p = 0; var byPath = new Dictionary<string, int>(StringComparer.Ordinal); var all = new List<CpioEntry>();
            while (true)
            {
                if (p + 110 > b.Length) throw new InvalidDataException("its file archive ends without its last entry (TRAILER!!!)");
                var magic = Encoding.ASCII.GetString(b, (int)p, 6);
                if (magic != "070701" && magic != "070702") throw new InvalidDataException(magic.StartsWith("0707", StringComparison.Ordinal) ? "its file archive is a cpio format other than newc, which this version does not read" : "its file archive is damaged at byte " + p);
                uint F(int i)
                {
                    uint v = 0;
                    for (int k = 0; k < 8; k++) { char c = (char)b[p + 6 + 8 * i + k]; int d = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1; if (d < 0) throw new InvalidDataException("its file archive is damaged at byte " + p); v = v << 4 | (uint)d; }
                    return v;
                }
                var e = new CpioEntry { Ino = F(0), Mode = (int)F(1), Uid = (int)F(2), Gid = (int)F(3), Links = (int)F(4), Mtime = F(5), Size = F(6), DevMajor = F(7), DevMinor = F(8), RdevMajor = F(9), RdevMinor = F(10) };
                uint ns = F(11), check = F(12);
                if (ns == 0 || ns > 4096 || p + 110 + ns > b.Length) throw new InvalidDataException("its file archive has a damaged name at byte " + p);
                if (b[p + 110 + ns - 1] != 0) throw new InvalidDataException("its file archive has a name without its end at byte " + p);
                var raw = Encoding.UTF8.GetString(b, (int)(p + 110), (int)ns - 1);
                long data = (p + 110 + ns + 3) & ~3L;
                if (raw == "TRAILER!!!") break;
                if (data + e.Size > b.Length) throw new InvalidDataException("its file archive is cut off inside " + raw);
                e.DataAt = data;
                if (magic == "070702") { uint sum = 0; for (long i = data; i < data + e.Size; i++) sum += b[i]; if (sum != check) throw new InvalidDataException("the file " + raw + " in its archive does not match its checksum"); }
                p = (data + e.Size + 3) & ~3L;
                if (all.Count >= MaxEntries) throw new InvalidDataException("its file archive holds more than " + MaxEntries + " entries");
                var path = raw; while (path.StartsWith("./", StringComparison.Ordinal)) path = path.Substring(2); path = path.TrimStart('/');
                if (path.Length == 0 || path == ".") continue;   // the archive's own top folder
                if (path.Split('/').Any(x => x.Length == 0 || x == "." || x == "..")) { e.Problem = "Its name in the archive (" + raw + ") has an empty, \".\" or \"..\" step, so it is listed, not read."; path = "(unsafe name) " + raw.Replace('/', '∕'); }
                e.Path = "/" + path; e.Name = e.Path.Substring(e.Path.LastIndexOf('/') + 1);
                if (e.TypeCode == 'l') e.LinkTarget = e.Size <= 4096 ? Encoding.UTF8.GetString(b, (int)data, (int)e.Size) : null;
                if (byPath.TryGetValue(e.Path, out int at)) { all[at] = e; Replaced++; }   // the kernel unpacks a later entry of the same name over an earlier one
                else { byPath[e.Path] = all.Count; all.Add(e); }
            }
            // hard links: every name of one file (nlink 2 or more) has the bytes of the entry that carries them (newc stores them once)
            foreach (var g in all.Where(x => x.TypeCode == 'f' && x.Links > 1).GroupBy(x => (x.DevMajor, x.DevMinor, x.Ino)))
            {
                var carrier = g.LastOrDefault(x => x.Size > 0); if (carrier == null) continue;
                foreach (var x in g.Where(x => x.Size == 0)) { x.Size = carrier.Size; x.DataAt = carrier.DataAt; }
            }
            Entries.AddRange(all);
        }

        /// <summary>a file's bytes (a regular file, read from the decoded archive)</summary>
        public void CopyFile(CpioEntry e, Stream sink)
        {
            if (e.TypeCode != 'f' || e.Problem != null) throw new IOException("only regular files with safe names are read");
            sink.Write(Data, (int)e.DataAt, (int)e.Size);
        }
    }

    /// <summary>the key the console's startup opens its system part with, from one startup image: where it was found, or why none was</summary>
    public sealed class SystemKey
    {
        public const int Bytes = 32;   // cryptsetup plain mode, aes-xts-plain, default 256-bit key: two AES-128 keys (XTS)
        internal byte[] key;
        public string FoundIn, Why;    // FoundIn: "the ramdisk's key-file" or "the command line"; Why: when there is none
        public bool Found => key != null;
        /// <summary>the key's bytes, for the caller's memory only: never printed, recorded, written or sent</summary>
        public byte[] Use() => key;
        public void Clear() { if (key != null) Array.Clear(key, 0, key.Length); key = null; }

        /// <summary>where hakchi's own /init looks: a hakchi-key-file=base64: word on the command line first, then a key-file in the ramdisk.
        /// ramdisk: the decoded ramdisk (null with ramdiskWhy when it could not be listed).</summary>
        public static SystemKey Find(BootParts parts, Ramdisk ramdisk, string ramdiskWhy)
        {
            var k = new SystemKey();
            var word = (parts?.Cmdline ?? "").Split(' ').FirstOrDefault(w => w.StartsWith("hakchi-key-file=base64:", StringComparison.Ordinal));
            if (word != null)
            {
                byte[] b = null; try { b = Convert.FromBase64String(word.Substring("hakchi-key-file=base64:".Length)); } catch (FormatException) { }
                if (b != null && b.Length == Bytes) { k.key = b; k.FoundIn = "the command line"; return k; }
                if (b != null) Array.Clear(b, 0, b.Length);
                k.Why = b == null ? "its command line names a key that is not valid Base64" : "its command line carries a key of " + b.Length + " bytes, not the " + Bytes + " the system part's cipher takes";
            }
            if (ramdisk == null) { k.Why = (k.Why != null ? k.Why + "; " : "") + (ramdiskWhy ?? "it has no ramdisk that could be listed"); return k; }
            var f = ramdisk.Entries.FirstOrDefault(e => e.Path == "/key-file");
            if (f == null) { k.Why = (k.Why != null ? k.Why + "; " : "") + "its ramdisk (" + BootParts.FormatName(ramdisk.Format) + ") holds no key-file"; return k; }
            if (f.TypeCode != 'f') { k.Why = "its ramdisk's key-file is not a regular file"; return k; }
            if (f.Size != Bytes) { k.Why = "its ramdisk's key-file is " + f.Size + " bytes, not the " + Bytes + " the system part's cipher takes"; return k; }
            k.key = new byte[Bytes]; Buffer.BlockCopy(ramdisk.Data, (int)f.DataAt, k.key, 0, Bytes); k.FoundIn = "the ramdisk's key-file";
            return k;
        }
    }
}
