// Classic Cart Studio console engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Developer Mode's test boot: an Android boot image that is NOT one of Classic Cart Studio's console programs, started from the console's
// RAM by the same FEL sequence as RamBootLoader (Hakchi2 CE 3.9.3's memboot: fes1, the image at 0x47400000, u-boot with "boota 47400000"),
// with two differences:
//   - the image is checked against the console's own startup-image layout (StartupImage.Inspect), the layout both the console's genuine
//     kernel and hakchi's kernel use, instead of a Classic Cart Studio manifest; and
//   - the console's SID (its hardware id) is read with FEL_UPLOAD (reads only) when the connection opens, so the engine can refuse a
//     console that is not the one the reviewed plan names before anything is written to its memory (TestBootSession).
// Putting an image into RAM writes nothing to the console's storage. The image's own code, once it starts, controls the console and can
// write anything; that limit is stated by the engine and the window, never hidden by this loader.
using CcsConsole.Usb;
using FelLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CcsConsole.RamBoot
{
    /// <summary>An Android boot image (header version 0) checked against the console's startup-image layout: what it is, and every reason
    /// it cannot be a startup image of this console (Problems; empty when it can).</summary>
    public sealed class StartupImage
    {
        // the layout of the console's genuine kernels and of hakchi 3.9.3's (read from both): 2,048-byte pages, kernel at 0x40008000,
        // ramdisk at 0x41000000, kernel tags at 0x40000100, no second stage, header version 0, an ARM zImage kernel
        public const uint Page = 2048, KernelAddr = 0x40008000, RamdiskAddr = 0x41000000, TagsAddr = 0x40000100;
        public const uint KernelRoom = RamdiskAddr - KernelAddr;   // the kernel file must end below the ramdisk it is copied next to
        public const uint ZImageMagic = 0x016F2818;

        public uint KernelSize, RamdiskSize, SecondSize; public long Bytes, Padded; public string Cmdline, RamdiskFormat, Sha256, Md5;
        public bool? KeyInRamdisk;   // the console's key file ("key-file") in a ramdisk this loader can list (gzip, LZO or plain cpio; StartupFiles.cs); null: not checked
        public bool HakchiKeyFallback;   // an /init that takes the key from the console's own startup software (hakchi's recovery /init does)
        public readonly List<string> Problems = new List<string>();
        public bool Ok => Problems.Count == 0;
        public string[] Words => (Cmdline ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        /// <summary>the command line with any key it carries replaced (hakchi-key-file=base64:… holds the console's key: never shown or recorded)</summary>
        public string MaskedCmdline => string.Join(" ", Words.Select(Mask));
        public static string Mask(string w) => w.StartsWith("hakchi-key-file=", StringComparison.Ordinal) ? "hakchi-key-file=(the console's key, not shown)" : w;

        static uint U(byte[] b, int o) => BitConverter.ToUInt32(b, o);

        public static StartupImage Inspect(byte[] raw)
        {
            var s = new StartupImage();
            if (raw == null || raw.Length < Page || Encoding.ASCII.GetString(raw, 0, 8) != "ANDROID!") { s.Problems.Add("it is not an Android boot image"); return s; }
            uint ks = U(raw, 8), ka = U(raw, 12), rs = U(raw, 16), ra = U(raw, 20), ss = U(raw, 24), ta = U(raw, 32), ps = U(raw, 36), ver = U(raw, 40);
            s.KernelSize = ks; s.RamdiskSize = rs; s.SecondSize = ss;
            if (ps != Page) { s.Problems.Add("its page size (" + ps + ") is not the console's (2,048)"); return s; }
            if (ver != 0) s.Problems.Add("it is a newer boot image version (" + ver + ") than the console's startup reads (version 0)");
            if (ka != KernelAddr || ra != RamdiskAddr || ta != TagsAddr) s.Problems.Add("its load addresses differ from the console's startup images (kernel 0x40008000, ramdisk 0x41000000, tags 0x40000100)");
            if (ss != 0) s.Problems.Add("it carries a second-stage loader, which the console's startup does not use");
            if (ks == 0) { s.Problems.Add("it has no kernel"); return s; }
            if (ks > KernelRoom) s.Problems.Add("its kernel (" + ks.ToString("N0", CultureInfo.InvariantCulture) + " bytes) would overlap the ramdisk in the console's memory (at most " + KernelRoom.ToString("N0", CultureInfo.InvariantCulture) + " bytes)");
            long Pages(long n) => (n + ps - 1) / ps;
            s.Bytes = ps * (1 + Pages(ks) + Pages(rs) + Pages(ss));
            s.Padded = (s.Bytes + Fel.sector_size - 1) / Fel.sector_size * Fel.sector_size;
            if (s.Bytes > raw.Length) { s.Problems.Add("it is shorter than its header says (" + raw.Length.ToString("N0", CultureInfo.InvariantCulture) + " bytes, " + s.Bytes.ToString("N0", CultureInfo.InvariantCulture) + " described)"); return s; }
            for (long i = s.Bytes; i < raw.Length; i++) if (raw[i] != 0) { s.Problems.Add("it has bytes after its last part that its header does not describe"); break; }
            if (s.Padded > RamImage.ImageMax) s.Problems.Add("it is larger than the 32 MiB the console's memory start can take");
            int nul = Array.IndexOf(raw, (byte)0, 64, 512);
            if (nul < 0) s.Problems.Add("its command line has no end");
            else
            {
                s.Cmdline = Encoding.ASCII.GetString(raw, 64, nul - 64);
                if (s.Cmdline.Any(ch => ch < 0x20 || ch > 0x7E)) s.Problems.Add("its command line is not plain text");
            }
            for (int i = 48; i < 64; i++) if (raw[i] != 0 && (raw[i] < 0x20 || raw[i] > 0x7E)) { s.Problems.Add("its name field is not plain text"); break; }
            for (int i = 608; i < 1632; i++) if (raw[i] != 0) { s.Problems.Add("it uses the extended command line, which the console's startup does not read"); break; }
            var kernel = Part(raw, ps, ks); var ramdisk = Part(raw, ps * (1 + Pages(ks)), rs); var second = Part(raw, ps * (1 + Pages(ks) + Pages(rs)), ss);
            if (ks < 0x30 || U(kernel, 0x24) != ZImageMagic || U(kernel, 0x2C) < U(kernel, 0x28) || U(kernel, 0x2C) - U(kernel, 0x28) > ks) s.Problems.Add("its kernel is not an ARM zImage (the kind the console's startup runs)");
            using (var h = SHA1.Create())
            {   // mkbootimg's id: SHA-1 over each part and its length (the console's genuine kernels and hakchi's carry it)
                foreach (var p in new[] { kernel, ramdisk, second }) { h.TransformBlock(p, 0, p.Length, null, 0); var l = BitConverter.GetBytes((uint)p.Length); h.TransformBlock(l, 0, 4, null, 0); }
                h.TransformFinalBlock(new byte[0], 0, 0);
                if (!h.Hash.SequenceEqual(raw.Skip(576).Take(20))) s.Problems.Add("its own checksum does not match its parts (it is damaged, or was changed after it was built)");
            }
            s.RamdiskFormat = rs == 0 ? "none" : Format(ramdisk);
            if (s.RamdiskFormat == null) s.Problems.Add("its ramdisk is not in a format a kernel unpacks (gzip, xz, LZO, LZMA, LZ4, bzip2 or plain cpio)");
            if (s.RamdiskFormat == "gzip" || s.RamdiskFormat == "cpio" || s.RamdiskFormat == "lzo") ScanRamdisk(s, ramdisk);
            using (var h = SHA256.Create()) s.Sha256 = Hex(h.ComputeHash(raw, 0, (int)Math.Min(raw.Length, s.Bytes)));
            using (var h = MD5.Create()) s.Md5 = Hex(h.ComputeHash(raw, 0, (int)Math.Min(raw.Length, s.Bytes)));
            return s;
        }

        static string Hex(byte[] b) => BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
        static byte[] Part(byte[] raw, long at, uint n) { var b = new byte[n]; if (n > 0) Array.Copy(raw, at, b, 0, n); return b; }
        static bool Starts(byte[] b, params int[] m) => b.Length >= m.Length && m.Select((x, i) => b[i] == x).All(x => x);
        static string Format(byte[] r) => Starts(r, 0x1F, 0x8B) ? "gzip" : Starts(r, 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00) ? "xz" : Starts(r, 0x89, 0x4C, 0x5A, 0x4F) ? "lzo"
            : Starts(r, 0x5D, 0x00, 0x00) ? "lzma" : Starts(r, 0x02, 0x21, 0x4C, 0x18) ? "lz4" : Starts(r, 0x42, 0x5A, 0x68) ? "bzip2" : Starts(r, 0x30, 0x37, 0x30, 0x37, 0x30) ? "cpio" : null;

        /// <summary>lists a gzip, LZO or plain cpio (newc) ramdisk (the shared bounded reader, Ramdisk in StartupFiles.cs): whether it holds
        /// the console's key file, and whether its /init is one that reads the key from the console's own startup software (hakchi's:
        /// "sunxi-flash ramdisk"); in memory, never extracted to disk; a ramdisk that cannot be read leaves it not checked</summary>
        static void ScanRamdisk(StartupImage s, byte[] ramdisk)
        {
            try
            {
                var rd = Ramdisk.Decode(ramdisk);
                s.KeyInRamdisk = rd.Entries.Any(e => e.Path == "/key-file" && e.TypeCode == 'f' && e.Size > 0);
                var init = rd.Entries.FirstOrDefault(e => e.Path == "/init" && e.TypeCode == 'f' && e.Size > 0);
                if (init != null && Encoding.ASCII.GetString(rd.Data, (int)init.DataAt, (int)init.Size).Contains("sunxi-flash ramdisk")) s.HakchiKeyFallback = true;
                Array.Clear(rd.Data, 0, rd.Data.Length);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is NotSupportedException || ex is FormatException || ex is ArgumentException || ex is IOException) { s.KeyInRamdisk = null; }
        }
    }

    /// <summary>One FEL connection for a test boot: opened (exactly one FEL device, the NES Classic's board id, its SID read: nothing
    /// written), then, only if the caller still wants it, the image sent and started. Never throws from Boot: errors come back in the result.</summary>
    public sealed class TestBootSession : IDisposable
    {
        readonly IFelUsb usb; readonly Fel fel; bool sent;
        public string Device { get; private set; }
        /// <summary>the console's hardware id as the console itself prints it (four 32-bit words, as "sntool devmem 0x01c23800 32 | hexdump")</summary>
        public string Sid { get; private set; }
        public int DramInitSleepMs { get => fel.DramInitSleepMs; set => fel.DramInitSleepMs = value; }

        TestBootSession(IFelUsb usb, Fel fel) { this.usb = usb; this.fel = fel; }

        public static string SidText(byte[] b) => b == null || b.Length != 16 ? null : string.Join(" ", Enumerable.Range(0, 4).Select(i => BitConverter.ToUInt32(b, 4 * i).ToString("x8")));

        /// <summary>null with why when there is not exactly one console in FEL mode, it is not an NES Classic or its SID cannot be read</summary>
        public static TestBootSession Open(IUsbDeviceList devices, Func<IFelUsb> transport, byte[] fes1, byte[] ubootSd, out string why)
        {
            var found = FelDiscovery.FindFel(devices);
            if (found.Count == 0) { why = "no console in FEL mode was found (USB 1F3A:EFE8 with its driver)"; return null; }
            if (found.Count > 1) { why = "more than one device in FEL mode is connected; connect only the NES Classic"; return null; }
            var usb = transport(); var fel = new Fel(usb) { Fes1Bin = fes1, UBootBin = ubootSd };
            try
            {
                if (!fel.Open(out why)) { usb.Dispose(); return null; }
                if (fel.UbootCommandOffset < 0) { why = "u-boot has no bootcmd variable"; fel.Dispose(); return null; }
                var s = new TestBootSession(usb, fel) { Device = found[0].ToString(), Sid = SidText(fel.ReadSid()) };
                if (s.Sid == null) { why = "the console's hardware id could not be read"; s.Dispose(); return null; }
                why = null; return s;
            }
            catch (Exception ex) { why = "the console did not answer: " + ex.Message; try { fel.Dispose(); } catch { } return null; }
        }

        /// <summary>the SID read again on this connection (reads only)</summary>
        public string ReadSid() { try { return SidText(fel.ReadSid()); } catch (Exception) { return null; } }

        /// <summary>DRAM through fes1, the padded image at 0x47400000, then u-boot with "boota 47400000"; once per connection</summary>
        public RamBootResult Boot(byte[] padded, Action<RamBootProgress> progress)
        {
            var res = new RamBootResult { Device = Device }; var t0 = DateTime.UtcNow; var stage = RamBootStage.DramInit;
            void Report(RamBootStage st, string m, long done = 0, long total = 0) => progress?.Invoke(new RamBootProgress { Stage = st, Message = m, Done = done, Total = total });
            if (sent) { res.Error = "this connection already started an image"; res.FailedAt = stage; return res; }
            if (padded == null || padded.Length == 0 || padded.Length % Fel.sector_size != 0 || padded.Length > RamImage.ImageMax) { res.Error = "the image is not a padded startup image within 32 MiB"; res.FailedAt = stage; return res; }
            sent = true;
            try
            {
                Report(stage, "initializing the console's memory (fes1)"); fel.InitDram(true);
                stage = RamBootStage.Transfer; long total = padded.Length, done = 0;
                Report(stage, "transferring the image to RAM at 0x47400000", 0, total);
                fel.WriteMemory(Fel.transfer_base_m, padded, (a, c) => { done = Math.Min(total, done + Fel.MaxBulkSize); Report(RamBootStage.Transfer, null, done, total); });
                res.Transferred = true;
                stage = RamBootStage.BootCommand; Report(stage, "starting the image (" + RamImage.BootCommand + ")");
                fel.RunUbootCmd(RamImage.BootCommand);
                res.BootCommandSent = true; res.Elapsed = DateTime.UtcNow - t0; Report(RamBootStage.Done, RamBootResult.NotProof);
            }
            catch (Exception ex) { res.Error = ex.Message; res.FailedAt = stage; res.Elapsed = DateTime.UtcNow - t0; Report(RamBootStage.Failed, ex.Message); }
            return res;
        }

        public void Dispose() { try { fel.Dispose(); } catch { } try { usb.Dispose(); } catch { } }
    }
}
