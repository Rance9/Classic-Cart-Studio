// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The USB and SD storage backup file (ccs-storage-backup/1): one standard ZIP per backup, separate from a console backup (Archive/
// BackupFormat.cs) and never accepted where a console backup is expected (its member names are not on that format's list, and the console
// format's are not on this one's). It holds one or more storage sources: a USB drive or SD card (a Windows volume, or folders on one), or
// a whole drive image file. Each source is captured in one of two modes:
//   files   "Files and folders" (the default): the selected used files read through Windows' ordinary file access, with their sizes,
//           times and attributes. Small files are packed one after another into pack members; a file of 16,000,000 bytes or more is a
//           member of its own (so an unchanged large file has the same piece checksums in every backup that holds it). The list of what
//           was captured is kept in chunks of at most 4,000 entries (inventory/files-NNNNNN.json, each after the payload it describes).
//   image   "Whole drive image" (advanced): every byte of the drive, its partition table, file systems and free space (which can hold
//           deleted or private data), in fixed-size region members (image/region-NNNNNN.bin, 64 MiB unless the drive is very large),
//           each stored once, plus a file system index (index/layout.json and index/files-NNNNNN.json): each file's place in the image
//           (its extents), size, times and SHA-256 worked out from the stored image when the backup was made, for browsing and
//           exporting a file without unpacking the image and for repairing one region.
// Every member is in the ZIP64 form. Members:
//   README.txt                          what the file is, in plain words (first)
//   sNN/files/pack-NNNNNN.bin           files mode: small files, one after another (stored)
//   sNN/files/file-NNNNNN.bin           files mode: one large file (stored)
//   sNN/inventory/files-NNNNNN.json     files mode: up to 4,000 entries (files, folders, links not followed, what could not be read) (deflated)
//   sNN/image/region-NNNNNN.bin         image mode: one region of the drive (stored)
//   sNN/index/layout.json               image mode: the partition table, each partition's file system and whether it is browsable (deflated)
//   sNN/index/files-NNNNNN.json         image mode: up to 4,000 entries of the file system index (deflated)
//   repair/map-NNNNNN.json              the records of the members before it that no earlier map holds: place, exact local header, CRC-32,
//                                       sizes, SHA-256, MD5 and the SHA-256 of every 1,048,576-byte piece (stored; its own check_sha256)
//   repair/map-NNNNNN-copy.json         the same bytes again, every map's copy together near the end (a damaged start or end of the file
//                                       leaves one of the two)
//   ccs-storage-backup.json             the manifest, last: the backup, its sources and their identities, the scope and time of each
//                                       source (in files mode also the folders chosen, "selected"; backups made before this field
//                                       was added do not have it), and every map with its place, size and checksums
// sNN is the source's number (s01 to s16). Every member except the maps and the manifest is in exactly one map; every map is in the
// manifest. Nothing records a checksum of the whole file inside it (that is in the operation record beside the backups).
using CcsBackup.Archive;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsBackup.External
{
    public static class ExternalFormat
    {
        public const string Format = "classic-cart-studio-storage-backup";
        public const string Schema = "ccs-storage-backup/1";
        public const string ManifestName = "ccs-storage-backup.json";
        public const string ReadmeName = "README.txt";
        public const string MapFormat = "classic-cart-studio-storage-backup-map", MapSchema = "ccs-storage-backup-map/1";
        public const string Extension = ".zip", Partial = ".zip.partial";
        /// <summary>the subfolder of the backup location (and of the additional-copy location) that holds USB and SD backups, so console
        /// backups and storage backups are never listed or checked as each other</summary>
        public const string FolderName = "USB and SD Backups";
        public const int ChunkSize = Repair.RepairMap.ChunkSize;
        public const long RegionSize = 64L * 1024 * 1024;
        public const long OwnMemberAt = 16_000_000;        // a file this large or larger is a member of its own
        public const long PackTarget = 256L * 1000 * 1000;  // a pack is closed once it holds this many bytes
        public const int InventoryChunk = 4000;             // entries per inventory or index chunk
        public const int MaxSources = 16;
        public const long MaxEntries = 2_000_000;           // entries per source (files, folders, links): a larger tree is refused, not cut short
        public const long MapRecordBudget = 2_000_000;      // about how many bytes of member records one map holds before the next map starts

        static readonly Regex AllowedRe = new Regex(@"^(README\.txt|ccs-storage-backup\.json|repair/map-[0-9]{6}(-copy)?\.json|s[0-9]{2}/(files/(pack|file)-[0-9]{6}\.bin|inventory/files-[0-9]{6}\.json|image/region-[0-9]{6}\.bin|index/layout\.json|index/files-[0-9]{6}\.json))$", RegexOptions.CultureInvariant);
        public static bool Allowed(string name) => name != null && AllowedRe.IsMatch(name);

        /// <summary>the reader's limits for this format: a drive can hold many files, so many members (each still a fixed-pattern name)</summary>
        public static readonly ZipIndexLimits Limits = new ZipIndexLimits { MaxMembers = 4_000_000, MaxIndexBytes = 512L * 1024 * 1024 };

        public static bool IsMap(string name) => name != null && name.StartsWith("repair/map-", StringComparison.Ordinal);
        public static bool IsMapCopy(string name) => IsMap(name) && name.EndsWith("-copy.json", StringComparison.Ordinal);
        public static string MapName(int index, bool copy) => "repair/map-" + index.ToString("D6", CultureInfo.InvariantCulture) + (copy ? "-copy" : "") + ".json";
        public static string SourceId(int n) => "s" + n.ToString("D2", CultureInfo.InvariantCulture);
        public static string Pack(string src, int n) => src + "/files/pack-" + n.ToString("D6", CultureInfo.InvariantCulture) + ".bin";
        public static string FileMember(string src, int n) => src + "/files/file-" + n.ToString("D6", CultureInfo.InvariantCulture) + ".bin";
        public static string Inventory(string src, int n) => src + "/inventory/files-" + n.ToString("D6", CultureInfo.InvariantCulture) + ".json";
        public static string Region(string src, int n) => src + "/image/region-" + n.ToString("D6", CultureInfo.InvariantCulture) + ".bin";
        public static string Layout(string src) => src + "/index/layout.json";
        public static string Index(string src, int n) => src + "/index/files-" + n.ToString("D6", CultureInfo.InvariantCulture) + ".json";
        /// <summary>the source a member belongs to ("s01"), or null for README, the maps and the manifest</summary>
        public static string SourceOf(string name) => name != null && name.Length > 4 && name[0] == 's' && name[3] == '/' ? name.Substring(0, 3) : null;
        public static bool IsJson(string name) => name != null && name.EndsWith(".json", StringComparison.Ordinal);
        public static bool IsPayload(string name) => name != null && name.EndsWith(".bin", StringComparison.Ordinal);

        /// <summary>the largest each member may expand to: the JSON members within the JSON reader's own limit; payload members are as large
        /// as the drive they come from</summary>
        public static long MaxSize(string name)
        {
            if (name == ReadmeName) return 64L * 1000;
            if (IsJson(name)) return Json.MaxText;
            return 1L << 50;
        }

        /// <summary>the region size for a drive of this many bytes: 64 MiB, doubled until the drive has at most 65,536 regions</summary>
        public static long RegionSizeFor(long bytes)
        {
            long r = RegionSize; while (bytes / r >= 65536) r *= 2; return r;
        }

        public static string FileStem(DateTime local, string label) =>
            "USB and SD Backup " + local.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture) + " (" + Words.Component((label ?? "").Replace("\"", "").Replace(":", ""), 40).TrimEnd(' ', '(') + ")";

        /// <summary>true when the file is a USB and SD storage backup: its first member is README.txt and the next member's name is one of
        /// this format's, or (a file whose start is damaged) its member index names this format's manifest. Reads a few bytes only; never a
        /// verification.</summary>
        public static bool Is(string path)
        {
            try
            {
                using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096))
                {
                    var h = new byte[30 + 64];
                    if (f.Read(h, 0, h.Length) < 30 + 10 || BitConverter.ToUInt32(h, 0) != 0x04034b50) return IndexNamesManifest(f);
                    int nl = BitConverter.ToUInt16(h, 26), xl = BitConverter.ToUInt16(h, 28);
                    if (nl != ReadmeName.Length || Encoding.ASCII.GetString(h, 30, nl) != ReadmeName) return IndexNamesManifest(f);
                    // the README's compressed size: in its ZIP64 extra field (every member of this format has one)
                    long cs = BitConverter.ToUInt32(h, 18);
                    if (cs == 0xFFFFFFFF && xl >= 20) cs = BitConverter.ToInt64(h, 30 + nl + 4 + 8);
                    long next = 30 + nl + xl + cs; if (next + 30 > f.Length) return IndexNamesManifest(f);
                    f.Position = next; var n = new byte[30 + 64]; if (f.Read(n, 0, n.Length) < 30 || BitConverter.ToUInt32(n, 0) != 0x04034b50) return IndexNamesManifest(f);
                    int nl2 = BitConverter.ToUInt16(n, 26); if (nl2 <= 0 || nl2 > 64) return false;
                    return Allowed(Encoding.ASCII.GetString(n, 30, nl2));
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException) { return false; }
        }

        static bool IndexNamesManifest(FileStream f)
        {
            // the last 64 KB: the manifest's name in the member index (its central directory entry)
            long n = Math.Min(f.Length, 65536 + 22); if (n < 22) return false;
            f.Position = f.Length - n; var t = new byte[n]; int got = 0; while (got < n) { int r = f.Read(t, got, (int)n - got); if (r <= 0) break; got += r; }
            var name = Encoding.ASCII.GetBytes(ManifestName);
            for (int i = 0; i + 46 + name.Length <= got; i++)
                if (t[i] == 0x50 && t[i + 1] == 0x4b && t[i + 2] == 1 && t[i + 3] == 2 && BitConverter.ToUInt16(t, i + 28) == name.Length && t.Skip(i + 46).Take(name.Length).SequenceEqual(name)) return true;
            return false;
        }

        public static string Readme(Dictionary<string, object> m)
        {
            var lines = new List<string>
            {
                "Classic Cart Studio USB and SD backup",
                "",
                "A copy of USB or SD storage made on this PC by CCS Console Workbench. It is not a backup of a console's built-in storage, and",
                "it is never used as one.",
                "",
                "Made:      " + m.S("created_utc") + " (UTC, when it started)",
                "Made with: " + m.O("tool")?.S("name") + " " + m.O("tool")?.S("version"),
                "",
                "What is inside",
            };
            foreach (var s in (m.A("sources") ?? new List<object>()).OfType<Dictionary<string, object>>())
                lines.Add("  " + s.S("id") + "  " + s.S("label") + ": " + (s.S("mode") == "image" ? "the whole drive, as an image" : "the selected files and folders"));
            lines.AddRange(new[]
            {
                "",
                "Open it with CCS Console Workbench (USB and SD Backup) to check it, browse it, copy files out of it, repair it or restore",
                "from it. Its files are packed inside it with lists that record each file's checksum; unpacking it with a ZIP program shows",
                "those packs, not the files themselves.",
                "",
                "Keep it private: it holds everything that was on the storage it copied" + ((m.A("sources") ?? new List<object>()).OfType<Dictionary<string, object>>().Any(s => s.S("mode") == "image")
                    ? ", and a whole drive image also holds its free space, which can contain deleted files." : "."),
            });
            return string.Join("\r\n", lines) + "\r\n";
        }
    }

    /// <summary>Plain-word helpers shared by the storage backup code.</summary>
    public static class Words
    {
        static readonly HashSet<string> Reserved = new HashSet<string>(new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Concat(Enumerable.Range(0, 10).SelectMany(i => new[] { "COM" + i, "LPT" + i })).Concat(new[] { "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³" }), StringComparer.OrdinalIgnoreCase);

        /// <summary>a name Windows can store as one path component (characters it refuses become _, no trailing dot or space, no reserved
        /// device name), at most max characters; never empty, "." or ".."</summary>
        public static string Component(string name, int max = 120)
        {
            var b = new StringBuilder(); foreach (var ch in name ?? "") b.Append(ch < 0x20 || "<>:\"/\\|?*".IndexOf(ch) >= 0 ? '_' : ch);
            var r = b.ToString(); if (r.Length > max) r = r.Substring(0, max);
            if (r.EndsWith(".") || r.EndsWith(" ")) r = r.TrimEnd('.', ' ') + "_";
            if (r.Length == 0 || r == "." || r == "..") r = "_" + r;
            var stem = r.Split('.')[0].TrimEnd(' ');
            if (Reserved.Contains(stem)) r = "_" + r;
            return r;
        }

        /// <summary>true when Windows can create this exact name in a folder (Component would leave it unchanged)</summary>
        public static bool WindowsSafe(string name) => name != null && name.Length > 0 && Component(name, int.MaxValue) == name;

        public static string When(string utc) => DateTime.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
            ? t.ToLocalTime().ToString("MMMM d, yyyy 'at' h:mm tt", CultureInfo.GetCultureInfo("en-US")) : "an unknown time";
        public static string Utc(DateTime t) => t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        public static string N(long x) => x.ToString("N0", CultureInfo.InvariantCulture);
        public static string Count(long n, string one, string many = null) => N(n) + " " + (n == 1 ? one : many ?? one + "s");
    }
}
