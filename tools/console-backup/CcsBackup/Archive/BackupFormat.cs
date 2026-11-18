// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The console backup file (FORMAT.md): one standard ZIP per backup, schema ccs-console-backup/1. Member names are fixed (Allowed); each
// member has one role: "restore" (the authoritative bytes a restore puts back), "recovery" (the genuine original kernel, standalone),
// "reference" (the physical NAND image, the bootloaders and the partition table: kept, never written), "inventory" (the data
// partition's file list, read on the PC), "export" (a convenience .ccs package of the console's Classic Cart Studio HOME), "text"
// (README.txt) and "repair" (the repair map: repair/descriptor.json first and repair/chunk-map.json near the end, in backups made since
// they were added; older backups without them stay valid). The manifest, ccs-console-backup.json, is the last member and lists every
// other member with its size and hashes.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CcsBackup.Archive
{
    public static class BackupFormat
    {
        public const string Format = "classic-cart-studio-console-backup";
        public const string Schema = "ccs-console-backup/1";
        public const string ManifestName = "ccs-console-backup.json";
        public const string ReadmeName = "README.txt";
        public const string InventoryName = "inventory/data-files.json";
        public const string ExportName = "CCS/Console-HOME.ccs";
        public const string OriginalKernelName = "recovery/original-kernel.img";
        public const string DescriptorName = "repair/descriptor.json", ChunkMapName = "repair/chunk-map.json";
        public const string Extension = ".zip", Partial = ".zip.partial";
        public const string ScopeText = "Backs up your console's built-in storage. Games and saves on USB or SD storage are not included.";

        static readonly Regex AllowedRe = new Regex(@"^(README\.txt|ccs-console-backup\.json|restore/(boot2-active|boot2-slot68|nand[a-f])\.img|recovery/original-kernel\.img|reference/(boot0\.bin|boot1\.bin|nand-logical-head\.bin|nand-physical\.bin|partition-table\.txt)|inventory/data-files\.json|CCS/Console-HOME\.ccs|repair/(descriptor|chunk-map)\.json)$", RegexOptions.CultureInvariant);
        public static bool Allowed(string name) => name != null && AllowedRe.IsMatch(name);

        /// <summary>console target -> member name and role</summary>
        public static (string name, string role) MemberFor(string target)
        {
            switch (target)
            {
                case "boot2": return ("restore/boot2-active.img", "restore");
                case "boot2-68": return ("restore/boot2-slot68.img", "restore");
                case "boot0": return ("reference/boot0.bin", "reference");
                case "boot1": return ("reference/boot1.bin", "reference");
                case "nand": return ("reference/nand-logical-head.bin", "reference");
                case "phy": return ("reference/nand-physical.bin", "reference");
                case "parttable": return ("reference/partition-table.txt", "reference");
            }
            if (Regex.IsMatch(target ?? "", "^nand[a-f]$")) return ("restore/" + target + ".img", "restore");
            throw new ArgumentException("no member for " + target);
        }

        /// <summary>the largest each member may expand to (resource limits before anything is read)</summary>
        public static long MaxSize(string name)
        {
            if (name == ManifestName) return 4L * 1000 * 1000;
            if (name == DescriptorName) return 1000L * 1000;
            if (name == ChunkMapName) return 4L * 1000 * 1000;
            if (name == ReadmeName) return 64L * 1000;
            if (name == InventoryName) return 64L * 1000 * 1000;
            if (name == ExportName) return 2L * 1000 * 1000 * 1000;
            if (name == "reference/partition-table.txt") return 64L * 1000;
            if (name.EndsWith("boot2-active.img") || name.EndsWith("boot2-slot68.img") || name == OriginalKernelName || name.EndsWith("boot0.bin") || name.EndsWith("boot1.bin")) return 32L * 1024 * 1024;
            if (name == "reference/nand-logical-head.bin") return 64L * 1024 * 1024;
            return 536870912L;   // a partition or the physical image: never more than the whole NAND
        }

        public const long MaxTotal = 3L * 1000 * 1000 * 1000;

        public static string FileStem(DateTime local, string consoleTag, bool simulated) =>
            (simulated ? "SIMULATED " : "") + "NES Classic Backup " + local.ToString("yyyy-MM-dd HHmm", System.Globalization.CultureInfo.InvariantCulture) + " (console " + consoleTag + ")";

        public static string Readme(Dictionary<string, object> m, bool repairMap = false)
        {
            var c = m.O("console"); var st = m.O("state");
            var lines = new List<string>
            {
                "Classic Cart Studio console backup" + (m.B("simulated") ? " (SIMULATED: made from a simulated console on a PC, not from hardware)" : ""),
                "",
                ScopeText,
                "",
                "Made:      " + m.S("completed_utc") + " (UTC)",
                "Console:   NES Classic Edition, console " + c.S("tag") + ", region " + c.S("region") + ", system " + c.S("oem_version"),
                "State:     " + st.S("text"),
                "Made with: " + m.O("tool").S("name") + " " + m.O("tool").S("version"),
                "",
                "What this file is",
                "  A complete copy of the console's built-in storage at the time above, made by reading it from a Classic Cart Studio",
                "  program running in the console's memory (nothing was written to the console). Open it with CCS Console Workbench (on",
                "  its own, or in Classic Cart Studio for Windows) to check it, browse it or restore it to THIS console. Restoring is only",
                "  offered for the same console, and only when its storage is still laid out the same way.",
                "",
                "What is inside",
                "  restore/     the startup software and each part of the built-in storage, exactly as read: what a restore puts back",
                "  recovery/    the console's original Nintendo kernel, on its own" + (st.B("original_kernel") ? "" : " (NOT found on this console: see the manifest)"),
                "  reference/   the raw NAND image, the bootloaders and the storage table: kept as evidence, never written to a console",
                "  inventory/   the list of files on the data storage (games, saves, settings, other programs), read on the PC",
                "  CCS/         Console-HOME.ccs, the console's Classic Cart Studio HOME as an ordinary .ccs package, when there is one",
            };
            if (repairMap) lines.Add("  repair/      a second list of everything here, with checksums of every piece, so a damaged copy can be repaired");
            lines.AddRange(new[]
            {
                "  " + ManifestName + "   every file above with its size and checksums",
                "",
                "Unpacking this file with a ZIP program is fine for looking at it; it does not restore a console. Keep it private: it",
                "contains your console's identity and everything on it.",
            });
            return string.Join("\r\n", lines) + "\r\n";
        }
    }
}
