// Classic Cart Studio console engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace CcsConsole.RamBoot
{
    /// <summary>
    /// A RAM image folder as built by tools/hardware/h1ram/build_h1_ram.py: MANIFEST.json (schema ccs-h1-ram-image/2),
    /// SHA256SUMS, the Android boot image, fes1.bin and uboot-sd.bin. Every check happens before any USB device is opened.
    /// </summary>
    public sealed class RamImage
    {
        public const string Schema = "ccs-h1-ram-image/2";
        // the FEL boot files are hakchi 3.9.3's, pinned (fes1: hakchi.exe's embedded resource; u-boot: hakchi.hmod with the SD swap)
        public const string Fes1Sha256 = "d78688b70e022c53e6a49a18994f59946fda7791d22bc0e6409ff630553b491c";
        public const string UbootSdSha256 = "3bfc4a56417aade7d3fa3afcf3d2344b07ea38d9f42de504eda3014844f86b5b";
        public const uint ImageBase = 0x47400000, ImageMax = 0x2000000, Sector = 0x20000;
        public const string BootCommand = "boota 47400000";

        public string Folder { get; private set; }
        public byte[] Image { get; private set; }          // padded to the FEL sector size, as MembootFel does
        public byte[] Fes1 { get; private set; }
        public byte[] UbootSd { get; private set; }
        public string ImageSha256 { get; private set; }
        public uint ImageBytes { get; private set; }       // the boot image's own size (header arithmetic, Shared.CalcKernelSize)
        public string Cmdline { get; private set; }
        public string Variant { get; private set; }        // MANIFEST.json "variant" (the recovery images: ccs-backup, ccs-restore, ccs-prepare)

        public static string Sha256(byte[] b) { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-", "").ToLowerInvariant(); }

        /// <summary>loads and validates; throws RamImageException with a user-facing reason</summary>
        public static RamImage Load(string folder)
        {
            var full = Path.GetFullPath(folder);
            return FromSource(full, n => File.Exists(Path.Combine(full, n)) ? File.ReadAllBytes(Path.Combine(full, n)) : null);
        }

        /// <summary>the same checks for an image whose files come from elsewhere (an executable's embedded resources: nothing is written
        /// to disk to load it); read returns a file's bytes, or null when it is missing. origin only names the source in messages.</summary>
        public static RamImage FromSource(string origin, Func<string, byte[]> read)
        {
            var img = new RamImage { Folder = origin };
            var manBytes = read("MANIFEST.json"); var sumBytes = read("SHA256SUMS");
            if (manBytes == null || sumBytes == null) throw new RamImageException("MANIFEST.json or SHA256SUMS missing in " + origin);
            Dictionary<string, object> man;
            try { man = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(manBytes)); }
            catch (Exception ex) { throw new RamImageException("MANIFEST.json is not valid JSON: " + ex.Message); }
            if (!(man.TryGetValue("schema", out var sc) && (sc as string) == Schema)) throw new RamImageException("MANIFEST.json schema is not " + Schema);
            var fel = man.TryGetValue("fel", out var fo) ? fo as Dictionary<string, object> : null;
            if (fel == null || (fel["image_base"] as string) != "0x47400000" || (fel["boot_command"] as string) != BootCommand || (fel["board_id"] as string) != "0x00166700")
                throw new RamImageException("MANIFEST.json FEL parameters differ from the NES Classic RAM boot (0x47400000, \"" + BootCommand + "\", board 0x00166700)");
            img.Variant = man.TryGetValue("variant", out var vo) ? vo as string : null;
            var imageName = man.TryGetValue("image", out var io) ? io as string : null;
            if (string.IsNullOrEmpty(imageName) || imageName.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) throw new RamImageException("MANIFEST.json has no plain image file name");
            var sums = new Dictionary<string, string>();
            foreach (var line in Encoding.UTF8.GetString(sumBytes).Split((char)10))
            {
                var parts = line.Split(new[] { "  " }, 2, StringSplitOptions.None);
                if (parts.Length == 2) sums[parts[1].Trim()] = parts[0].Trim().ToLowerInvariant();
            }
            byte[] Checked(string name, string pinned)
            {
                var b = read(name);
                if (b == null) throw new RamImageException(name + " is missing");
                var h = Sha256(b);
                if (!sums.TryGetValue(name, out var listed) || listed != h) throw new RamImageException(name + " does not match SHA256SUMS");
                var outputs = man["outputs"] as Dictionary<string, object>; var o = outputs != null && outputs.ContainsKey(name) ? outputs[name] as Dictionary<string, object> : null;
                if (o == null || (o["sha256"] as string) != h) throw new RamImageException(name + " does not match MANIFEST.json");
                if (pinned != null && h != pinned) throw new RamImageException(name + " is not the expected hakchi 3.9.3 file (SHA-256 " + h + ")");
                return b;
            }
            var raw = Checked(imageName, null);
            img.Fes1 = Checked("fes1.bin", Fes1Sha256);
            img.UbootSd = Checked("uboot-sd.bin", UbootSdSha256);
            if (Encoding.ASCII.GetString(img.UbootSd).IndexOf("bootcmd=", StringComparison.Ordinal) < 0) throw new RamImageException("uboot-sd.bin has no bootcmd variable");
            // Android boot image v0: header checks, then upstream's size arithmetic (Shared.CalcKernelSize) and sector padding (MembootFel)
            if (raw.Length < 2048 || Encoding.ASCII.GetString(raw, 0, 8) != "ANDROID!") throw new RamImageException("not an Android boot image");
            uint U(int o) => BitConverter.ToUInt32(raw, o);
            uint ksize = U(8), kaddr = U(12), rsize = U(16), raddr = U(20), ssize = U(24), page = U(36), dtsize = U(40);
            if (page != 2048 || kaddr != 0x40008000 || raddr != 0x41000000) throw new RamImageException("boot image layout differs from hakchi 3.9.3's (page 2048, kernel 0x40008000, ramdisk 0x41000000)");
            ulong pages = 1 + (ksize + (ulong)page - 1) / page + (rsize + (ulong)page - 1) / page + (ssize + (ulong)page - 1) / page + (dtsize + (ulong)page - 1) / page;
            ulong size = pages * page;
            if (size > (ulong)raw.Length) throw new RamImageException("boot image is truncated (" + raw.Length + " bytes, header says " + size + ")");
            if (size > ImageMax) throw new RamImageException("boot image is larger than the 32 MiB FEL transfer area");
            img.Cmdline = Encoding.ASCII.GetString(raw, 64, 512).TrimEnd('\0');
            var words = img.Cmdline.Split(' ');
            if (!words.Contains("hakchi-shell") || words.Contains("hakchi-clovershell")) throw new RamImageException("boot image command line must select the recovery shell (hakchi-shell) and not clovershell");
            ulong padded = (size + Sector - 1) / Sector * Sector;
            if (padded > ImageMax) throw new RamImageException("padded image exceeds the FEL transfer area");
            var buf = new byte[padded]; Array.Copy(raw, buf, (int)Math.Min((ulong)raw.Length, padded));
            img.Image = buf; img.ImageBytes = (uint)size; img.ImageSha256 = Sha256(raw);
            return img;
        }
    }

    public sealed class RamImageException : Exception { public RamImageException(string m) : base(m) { } }
}
