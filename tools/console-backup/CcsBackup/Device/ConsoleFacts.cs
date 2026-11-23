// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// What a console is, as the recovery image's "ccsbr identify" reports it, and the checks that decide whether this engine supports it:
// the model and storage layout, the console's own identity, and the state of its startup software (stock, or modified with or without
// the genuine original kernel kept in hakchi's spare slot). Nothing here guesses: an unknown model, layout or image is refused.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CcsBackup.Device
{
    public sealed class PartitionEntry
    {
        public string Device, Label; public long Blocks512;   // as the pinned flash tool prints its table (device:label:512-byte blocks)
        public long Bytes => Blocks512 * 512;
    }

    public sealed class ConsoleIdentity
    {
        public readonly Dictionary<string, string> Raw = new Dictionary<string, string>();
        public string Hwid => Get("hwid"); public string Image => Get("image"); public string Profile => Get("profile");
        public string Kernel => Get("kernel"); public string NandInfo => Get("nandinfo"); public string Board => Get("board");
        public string Region => Get("region"); public string OemVersion => Get("oem_version");
        /// <summary>the supported console model this board and region answer with (ConsoleModels.For), or null</summary>
        public ConsoleModel Model => ConsoleModels.For(Board, Region);
        public string SystemMounted => Get("system_mounted"); public string StorageMounts => Get("storage_mounts");
        public string ActiveMd5 => Get("active_md5"); public long ActiveSize => Long("active_size");
        public int ActiveHakchiEntries => (int)Long("active_hakchi_entries");
        public bool Slot68Valid => Get("slot68") == "valid"; public string Slot68Md5 => Get("slot68_md5"); public long Slot68Size => Long("slot68_size");
        public List<PartitionEntry> Table = new List<PartitionEntry>();
        public Dictionary<string, long> KernelKiB = new Dictionary<string, long>();
        public string Get(string k) => Raw.TryGetValue(k, out var v) ? v : null;
        long Long(string k) => long.TryParse(Get(k), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : -1;
        /// <summary>a short label that names the console without its full identity (shown in the interface and in file names)</summary>
        public string ShortTag => HwidTag(Hwid);
        public static string HwidTag(string hwid) { using (var h = System.Security.Cryptography.SHA256.Create()) return Hex.Of(h.ComputeHash(System.Text.Encoding.ASCII.GetBytes(NormalizeHwid(hwid)))).Substring(0, 8); }
        /// <summary>the console's identity in one form whatever reported it (the recovery image prints four spaced words, hakchi's worker
        /// command one run of hex): lowercase hex without spaces</summary>
        public static string NormalizeHwid(string hwid) => (hwid ?? "").Replace(" ", "").ToLowerInvariant();
        public static bool SameConsole(string a, string b) => !string.IsNullOrEmpty(NormalizeHwid(a)) && NormalizeHwid(a) == NormalizeHwid(b);

        public static ConsoleIdentity Parse(string text)
        {
            var id = new ConsoleIdentity();
            foreach (var line in (text ?? "").Split('\n'))
            {
                int eq = line.IndexOf('='); if (eq <= 0) continue;
                var k = line.Substring(0, eq).Trim(); var v = line.Substring(eq + 1).Trim();
                if (Regex.IsMatch(k, "^[a-z0-9_]{1,40}$") && !id.Raw.ContainsKey(k) && v.Length <= 4096) id.Raw[k] = v;
            }
            if (id.Get("ccsbr") != "1" || id.Get("end") != "1") throw new ConsoleRefusedException("unreadable", "The console's answer was incomplete. Unplug the USB cable and start again.");
            foreach (var t in (id.Get("parttable") ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var m = Regex.Match(t, "^(mbr|nand[a-z]):([A-Za-z0-9_-]*):([0-9]{1,12})$");
                if (m.Success) id.Table.Add(new PartitionEntry { Device = m.Groups[1].Value, Label = m.Groups[2].Value, Blocks512 = long.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) });
            }
            foreach (var t in (id.Get("partitions") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var m = Regex.Match(t, "^(nand[a-z]?):([0-9]{1,12})$");
                if (m.Success && !id.KernelKiB.ContainsKey(m.Groups[1].Value)) id.KernelKiB[m.Groups[1].Value] = long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            }
            return id;
        }
    }

    public sealed class ConsoleRefusedException : Exception
    {
        public readonly string Code;
        public ConsoleRefusedException(string code, string text) : base(text) { Code = code; }
    }

    /// <summary>The genuine stock kernels of each supported model, by region where the model is told apart by it: Hakchi2 CE 3.9.3
    /// Shared.CorrectKernels (MD5; NES, SNES_USA, SNES_EUR), with the SHA-256 where it has been observed independently (an NES Classic's
    /// release v1.0.7, 2026-10-09; a North American Super NES Classic's release v2.0.14, 2026-11-18). A kernel is stock only for its own
    /// model and region; an unknown checksum is unknown, never stock.</summary>
    public static class KnownKernels
    {
        public static readonly Dictionary<string, string> NesStockMd5 = new Dictionary<string, string>
        {
            ["5cfdca351484e7025648abc3b20032ff"] = "kernel-dp-nes-release-v1.0.2-0-g99e37e1",
            ["07bfb800beba6ef619c29990d14b5158"] = "kernel-dp-nes-release-v1.0.3-0-gc4c703b",
            ["90eec1e2b4f00e53dc2dd53a9e7334c1"] = "kernel-dp-nes-release-v1.0.7-0-g4ea4041",
        };
        public static readonly Dictionary<string, string> SnesUsaStockMd5 = new Dictionary<string, string>
        {
            ["5296e64818bf2d1dbdc6b594f3eefd17"] = "kernel-dp-snesusa-release-v2.0.7-0-geb2b275",
            ["449b711238575763c6701f5958323d48"] = "kernel-dp-snesusa-release-v2.0.13-0-g9dca6c5",
            ["228967ab1035a347caa9c880419df487"] = "kernel-dp-snesusa-release-v2.0.14-0-gd8b65c6",
        };
        public static readonly Dictionary<string, string> SnesEurStockMd5 = new Dictionary<string, string>
        {
            ["d76c2a091ebe7b4614589fc6954653a5"] = "kernel-dp-sneseur-release-v2.0.7-0-geb2b275",
            ["c2b57b550f35d64d1c6ce66f9b5180ce"] = "kernel-dp-sneseur-release-v2.0.13-0-g9dca6c5",
            ["0f890bc78cbd9ede43b83b015ba4c022"] = "kernel-dp-sneseur-release-v2.0.14-0-gd8b65c6",
        };
        public static readonly Dictionary<string, string> Sha256ByMd5 = new Dictionary<string, string>
        {
            ["90eec1e2b4f00e53dc2dd53a9e7334c1"] = "dae9eec3e7ad25379484f7e7db4be7d09b1135b0f1f3c8cb326f8da97187b521",
            ["228967ab1035a347caa9c880419df487"] = "a358326433d7bb5d1093d313fd7656f8706b493972cea446fd926437704f8911",
        };
        static readonly Dictionary<string, string> none = new Dictionary<string, string>();
        /// <summary>the stock kernels of a model (and region, for a model told apart by it); null model: the NES Classic's (the meaning of
        /// a backup or identity that names no other model)</summary>
        public static IReadOnlyDictionary<string, string> StockFor(ConsoleModel model, string region)
            => model == null || model.IsNes ? NesStockMd5 : model == ConsoleModels.Snes ? (region == "USA" ? SnesUsaStockMd5 : region == "EUR" ? SnesEurStockMd5 : none) : none;
        public static bool IsStockFor(ConsoleModel model, string region, string md5) => md5 != null && StockFor(model, region).ContainsKey(md5);
        public static string ReleaseFor(ConsoleModel model, string region, string md5) => md5 != null && StockFor(model, region).TryGetValue(md5, out var r) ? r : null;
        /// <summary>a stock NES Classic kernel (callers that know the console's model use IsStockFor)</summary>
        public static bool IsStock(string md5) => md5 != null && NesStockMd5.ContainsKey(md5);
        /// <summary>a stock MD5 whose SHA-256 is known must also match it; one whose SHA-256 is not known is accepted on its MD5 (stated)</summary>
        public static bool ShaAgrees(string md5, string sha256) => !Sha256ByMd5.TryGetValue(md5 ?? "", out var want) || want == sha256;
    }

    public sealed class Classification
    {
        public string Kind;            // "stock" or "modified"
        public bool ActiveIsStock;     // the running startup software is a genuine stock kernel with nothing of hakchi in it
        public bool OriginalKnown;     // a genuine original kernel is in this snapshot (the active one on stock, hakchi's spare slot otherwise)
        public string OriginalSource;  // "active" or "slot68"
        public string OriginalRelease; // the stock release name
        public string Text;
    }

    public static class Support
    {
        public const string RecoveryKernel = "3.4.113.29-madmonkey";        // the kernel of hakchi 3.9.3's recovery image, which the CCS images keep
        public const string NandInfo = "2048 64 4096";                      // the NES Classic layout's nandinfo (each model's: ConsoleModel.NandInfo)
        public const long PhysicalBytes = 536870912;                        // 536,870,912 bytes, both supported models

        /// <summary>Checks everything a backup needs before the first byte is read; returns the partitions to capture (by device). operation:
        /// what the session is for (ConsoleModel.Refusal): every console operation starts with a backup, so "backup" is always needed too.
        /// The model comes from the board and REGION the console's own software reports, never from the console chosen in the window.</summary>
        public static List<PartitionEntry> CheckForBackup(ConsoleIdentity id, string expectedImage, string operation = "backup")
        {
            if (string.IsNullOrEmpty(expectedImage) || id.Image != expectedImage) throw new ConsoleRefusedException("wrong-image", "The console is not running the Classic Cart Studio backup program that was just sent. Unplug the USB cable and start again.");
            if (id.Kernel != RecoveryKernel) throw new ConsoleRefusedException("wrong-image", "The console answered with an unexpected program. Nothing was read.");
            if (!Regex.IsMatch(id.Hwid ?? "", "^[0-9a-f]{8}( [0-9a-f]{8}){3}$") || id.Hwid == "00000000 00000000 00000000 00000000") throw new ConsoleRefusedException("no-identity", "The console's own identity could not be read, so its backup could not be matched to it later. Nothing was read.");
            var model = id.Model;
            if (model == null) throw new ConsoleRefusedException("unsupported-model", "This console is not an NES Classic Edition or a North American or European Super NES Classic Edition, the consoles Classic Cart Studio supports (board " + (id.Board ?? "unknown") + ", region " + (id.Region ?? "unknown") + "). Nothing was read.");
            var why = model.Refusal("backup") ?? (operation == "backup" ? null : model.Refusal(operation));
            if (why != null) throw new ConsoleRefusedException("unsupported-operation", why + " Nothing was read.");
            if (id.NandInfo != model.NandInfo) throw new ConsoleRefusedException("unsupported-layout", "This console's built-in storage is not the layout Classic Cart Studio knows for the " + model.Name + " (" + (id.NandInfo ?? "unknown") + "). Nothing was read.");
            var labels = new HashSet<string>(model.Labels);
            if (!string.IsNullOrEmpty(id.StorageMounts)) throw new ConsoleRefusedException("storage-mounted", "Part of the console's storage is in use, which the backup program never does. Nothing was read. Unplug the USB cable.");
            var sm = id.SystemMounted ?? "";
            if (sm.Length > 0 && !Regex.IsMatch(sm, "^/dev/(mapper/root-crypt|dm-0):squashfs:ro(,|$)")) throw new ConsoleRefusedException("storage-mounted", "The console's system storage is open in a way the backup program never uses. Nothing was read.");
            var parts = id.Table.Where(p => p.Device != "mbr").ToList();
            if (parts.Count < 3 || parts.Count > 6) throw new ConsoleRefusedException("unsupported-layout", "The console's storage is divided in a way Classic Cart Studio does not know. Nothing was read.");
            var devs = new HashSet<string>();
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p.Device != "nand" + (char)('a' + i) || !devs.Add(p.Device)) throw new ConsoleRefusedException("unsupported-layout", "The console's storage table is not in the expected order. Nothing was read.");
                if (!labels.Contains(p.Label)) throw new ConsoleRefusedException("unsupported-layout", "The console's storage has a part Classic Cart Studio does not know (" + p.Label + "). Nothing was read.");
                if (!id.KernelKiB.TryGetValue(p.Device, out var kib) || kib * 2 != p.Blocks512) throw new ConsoleRefusedException("unsupported-layout", "The console's storage table and its running system disagree about " + p.Device + ". Nothing was read.");
                if (p.Bytes <= 0 || p.Bytes > PhysicalBytes) throw new ConsoleRefusedException("unsupported-layout", "A part of the console's storage has an impossible size. Nothing was read.");
            }
            if (!parts.Any(p => p.Label == "rootfs") || !parts.Any(p => p.Label == "data" || p.Label == "rootfs_data")) throw new ConsoleRefusedException("unsupported-layout", "The console's system or data storage was not found. Nothing was read.");
            if (parts.Sum(p => p.Bytes) > PhysicalBytes) throw new ConsoleRefusedException("unsupported-layout", "The console's storage table adds up to more than its storage. Nothing was read.");
            if (id.ActiveMd5 == null || id.ActiveMd5 == "none" || id.ActiveSize <= 0) throw new ConsoleRefusedException("no-kernel", "The console's startup software could not be read. Nothing was read.");
            return parts;
        }

        /// <summary>the state of the console's startup software, judged against its own model's (and region's) stock kernels</summary>
        public static Classification Classify(ConsoleIdentity id, string activeSha256 = null, string slot68Sha256 = null)
        {
            var c = new Classification(); var m = id.Model; var rg = id.Region;
            c.ActiveIsStock = KnownKernels.IsStockFor(m, rg, id.ActiveMd5) && id.ActiveHakchiEntries == 0 && (activeSha256 == null || KnownKernels.ShaAgrees(id.ActiveMd5, activeSha256));
            bool slotStock = id.Slot68Valid && KnownKernels.IsStockFor(m, rg, id.Slot68Md5) && (slot68Sha256 == null || KnownKernels.ShaAgrees(id.Slot68Md5, slot68Sha256));
            if (c.ActiveIsStock) { c.OriginalKnown = true; c.OriginalSource = "active"; c.OriginalRelease = KnownKernels.ReleaseFor(m, rg, id.ActiveMd5); }
            else if (slotStock) { c.OriginalKnown = true; c.OriginalSource = "slot68"; c.OriginalRelease = KnownKernels.ReleaseFor(m, rg, id.Slot68Md5); }
            c.Kind = c.ActiveIsStock && !id.Slot68Valid ? "stock" : "modified";
            if (c.ActiveIsStock && !id.Slot68Valid) c.Text = "Original Nintendo software (" + Short(c.OriginalRelease) + ").";
            else if (c.ActiveIsStock) c.Text = "Original Nintendo startup software, with a spare copy of a kernel kept by earlier modification software.";
            else if (c.OriginalKnown) c.Text = "Modified startup software (as installed by console modification software). The original Nintendo kernel (" + Short(c.OriginalRelease) + ") was found in its spare slot and is included.";
            else c.Text = "Modified startup software. The original Nintendo kernel was not found on the console, so this backup cannot serve as the console's original recovery baseline.";
            return c;
        }

        static string Short(string release) { var m = Regex.Match(release ?? "", "release-(v[0-9.]+)"); return m.Success ? "release " + m.Groups[1].Value : "unknown release"; }
    }
}
