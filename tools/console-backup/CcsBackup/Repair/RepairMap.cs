// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The repair map (FORMAT.md, "Repair information"): two small members of every backup made since they were added, so a damaged copy
// can still be identified, checked piece by piece and repaired from what it recorded itself, never from a guess:
//   repair/descriptor.json  the FIRST member (placed when the capture starts, filled when it ends): the backup's identity and a copy of
//                           the member list with every member's place in the file, its exact local header, CRC-32, sizes, SHA-256, MD5
//                           and the SHA-256 of each 1,048,576-byte chunk of its stored bytes. Every member except itself and the manifest.
//   repair/chunk-map.json   the same kind of document just before the manifest, listing every member written before it except the
//                           descriptor: a second copy of the member records and chunk hashes that a damaged start of the file cannot take.
// Neither records a hash of the whole file or of the other's container bytes (nothing circular): the manifest lists both with their
// SHA-256 like any member, and each carries check_sha256, the SHA-256 of a canonical text of its own fields (CanonicalText), so a copy
// whose manifest is gone can still prove its map was not damaged. A map only says what the bytes WERE; it never stands in for checking
// them (Verifier recomputes every chunk; the repair code re-hashes every byte it takes).
// Chunk size 1,048,576 bytes (2^20): the physical NAND image and every partition of the console's original layout are whole numbers of
// these, a chunk is eight 131,072-byte NAND erase blocks and 256 ext4 blocks of 4,096 bytes, so a damaged chunk names whole console
// blocks and whole file system blocks; it is a multiple of the 4,096-byte disk sector; a 1 GB backup needs about 1,000 chunks (a map of
// about 70 KB). A parity set added later can use slices of the same size and the absolute file offsets recorded here.
using CcsBackup.Archive;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CcsBackup.Repair
{
    /// <summary>One member as a repair map records it.</summary>
    public sealed class MapMember
    {
        public string Path, Sha256, Md5, LocalHeader;   // LocalHeader: lowercase hex of the exact local header bytes
        public long HeaderOffset, DataOffset, Size, CompressedSize; public int Method; public uint Crc;
        public List<string> Chunks;   // SHA-256 of each ChunkSize piece of the stored bytes (null: not recorded)
        public long DataEnd => DataOffset + CompressedSize;
    }

    /// <summary>A repair map document (the descriptor or the chunk map), parsed and self-checked.</summary>
    public sealed class MapDoc
    {
        public string Role, BackupId, Board, Hwid, Tag, CreatedUtc, CompletedUtc, Check; public bool Simulated;
        public ushort DosTime, DosDate; public bool Zip64; public int ChunkSize = RepairMap.ChunkSize;
        public List<MapMember> Members = new List<MapMember>();

        static string I(long x) => x.ToString(CultureInfo.InvariantCulture);

        /// <summary>The text check_sha256 is taken over: every field, one line each, in a fixed order (FORMAT.md).</summary>
        public string CanonicalText()
        {
            var sb = new StringBuilder();
            sb.Append(RepairMap.Schema).Append('\n').Append(Role).Append('\n').Append(BackupId).Append('\n').Append(Simulated ? "simulated" : "console").Append('\n')
              .Append(Board).Append('\n').Append(Hwid).Append('\n').Append(Tag).Append('\n').Append(CreatedUtc).Append('\n').Append(CompletedUtc).Append('\n')
              .Append(I(DosTime)).Append(' ').Append(I(DosDate)).Append(' ').Append(Zip64 ? "1" : "0").Append(' ').Append(I(ChunkSize)).Append('\n');
            foreach (var m in Members)
            {
                sb.Append(m.Path).Append('|').Append(I(m.HeaderOffset)).Append('|').Append(I(m.DataOffset)).Append('|').Append(I(m.Method)).Append('|').Append(m.Crc.ToString("x8"))
                  .Append('|').Append(I(m.Size)).Append('|').Append(I(m.CompressedSize)).Append('|').Append(m.Sha256).Append('|').Append(m.Md5).Append('|').Append(m.LocalHeader).Append('\n');
                if (m.Chunks != null) for (int i = 0; i < m.Chunks.Count; i++) sb.Append(' ').Append(I(i)).Append(' ').Append(m.Chunks[i]).Append('\n');
            }
            return sb.ToString();
        }

        public static string Sha(string text) { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(text))); }

        public JObj ToJson() => new JObj
        {
            { "format", RepairMap.Format }, { "schema", RepairMap.Schema }, { "role", Role },
            { "about", Role == "descriptor"
                ? "An early copy of this backup's member list, placed first in the file, so a copy whose end is damaged can still be identified and checked. It never replaces the manifest (ccs-console-backup.json)."
                : "A second copy of the checksums of every 1,048,576-byte piece of the members before it, so a copy whose start is damaged can still be checked piece by piece." },
            { "backup_id", BackupId }, { "simulated", Simulated }, { "created_utc", CreatedUtc }, { "completed_utc", CompletedUtc },
            { "console", new JObj { { "board", Board }, { "hwid", Hwid }, { "tag", Tag } } },
            { "zip", new JObj { { "dos_time", (long)DosTime }, { "dos_date", (long)DosDate }, { "zip64", Zip64 } } },
            { "chunk_size", (long)ChunkSize },
            { "members", Members.Select(m => (object)new JObj { { "path", m.Path }, { "header_offset", m.HeaderOffset }, { "data_offset", m.DataOffset }, { "method", (long)m.Method }, { "crc32", m.Crc.ToString("x8") },
                { "size", m.Size }, { "compressed_size", m.CompressedSize }, { "sha256", m.Sha256 }, { "md5", m.Md5 }, { "local_header", m.LocalHeader }, { "chunks", m.Chunks ?? new List<string>() } }).ToList() },
            { "check_sha256", Check },
        };

        /// <summary>the document's bytes, padded with spaces (and a final newline) to padTo when it is above 0</summary>
        public byte[] Bytes(int padTo = 0)
        {
            Check = Sha(CanonicalText());
            var b = Json.Bytes(ToJson());
            if (padTo <= 0) return b;
            if (b.Length > padTo) throw new System.IO.IOException("the repair map does not fit its reserved place (" + b.Length.ToString("N0", CultureInfo.InvariantCulture) + " bytes)");
            var o = new byte[padTo]; Buffer.BlockCopy(b, 0, o, 0, b.Length);
            for (int i = b.Length; i < padTo; i++) o[i] = (byte)' ';
            if (padTo > b.Length) o[padTo - 1] = (byte)'\n';
            return o;
        }

        /// <summary>Reads untrusted bytes: null (with the reason) unless every field is well formed and check_sha256 agrees.</summary>
        public static MapDoc Parse(byte[] content, out string problem)
        {
            problem = null;
            try
            {
                if (content == null || content.Length == 0 || content.Length > BackupFormat.MaxSize(BackupFormat.ChunkMapName)) { problem = "it is missing or too large"; return null; }
                var d = Json.Parse(new UTF8Encoding(false, true).GetString(content));
                if (d.S("format") != RepairMap.Format || d.S("schema") != RepairMap.Schema) { problem = "it is not a repair map this version reads"; return null; }
                var con = d.O("console"); var zip = d.O("zip");
                var m = new MapDoc
                {
                    Role = d.S("role"), BackupId = d.S("backup_id"), Simulated = d.B("simulated"), CreatedUtc = d.S("created_utc"), CompletedUtc = d.S("completed_utc"),
                    Board = con?.S("board"), Hwid = con?.S("hwid"), Tag = con?.S("tag"), Check = d.S("check_sha256"),
                    DosTime = (ushort)(zip?.L("dos_time") ?? 0), DosDate = (ushort)(zip?.L("dos_date") ?? 0), Zip64 = zip?.B("zip64") ?? false, ChunkSize = (int)d.L("chunk_size"),
                };
                if (m.Role != "descriptor" && m.Role != "chunk-map") { problem = "its role is unknown"; return null; }
                if (m.ChunkSize != RepairMap.ChunkSize) { problem = "its piece size is not " + RepairMap.ChunkSize.ToString("N0", CultureInfo.InvariantCulture) + " bytes"; return null; }
                if (m.BackupId == null || m.Hwid == null || m.Tag == null || zip == null || !Hex.IsHex(m.Check, 64)) { problem = "a field is missing"; return null; }
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long end = 0;
                foreach (var x in (d.A("members") ?? new List<object>()).OfType<Dictionary<string, object>>())
                {
                    var mm = new MapMember
                    {
                        Path = x.S("path"), HeaderOffset = x.L("header_offset"), DataOffset = x.L("data_offset"), Method = (int)x.L("method"), Size = x.L("size"), CompressedSize = x.L("compressed_size"),
                        Sha256 = x.S("sha256"), Md5 = x.S("md5"), LocalHeader = x.S("local_header"), Chunks = x.A("chunks")?.Select(c => c as string).ToList(),
                    };
                    var crc = x.S("crc32");
                    if (!BackupFormat.Allowed(mm.Path) || mm.Path == BackupFormat.ManifestName || !seen.Add(mm.Path)) { problem = "it lists a member name a backup never has, or one twice"; return null; }
                    if (!Hex.IsHex(crc, 8) || !Hex.IsHex(mm.Sha256, 64) || !Hex.IsHex(mm.Md5, 32) || mm.LocalHeader == null || mm.LocalHeader.Length % 2 != 0 || mm.LocalHeader.Length < 60 || mm.LocalHeader.Length > 2 * 400 || !Hex.IsHex(mm.LocalHeader, mm.LocalHeader.Length))
                    { problem = "a member's checksums or header are not well formed (" + mm.Path + ")"; return null; }
                    mm.Crc = uint.Parse(crc, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    if (mm.Method != 0 && mm.Method != 8 || mm.Size < 0 || mm.CompressedSize < 0 || mm.Size > BackupFormat.MaxSize(mm.Path) || mm.Method == 0 && mm.Size != mm.CompressedSize
                        || mm.HeaderOffset < end || mm.DataOffset != mm.HeaderOffset + mm.LocalHeader.Length / 2) { problem = "a member's place or size is impossible (" + mm.Path + ")"; return null; }
                    end = mm.DataEnd;
                    if (mm.Chunks == null || mm.Chunks.Count != RepairMap.ChunkCount(mm.CompressedSize) || mm.Chunks.Any(c => !Hex.IsHex(c, 64))) { problem = "a member's piece checksums are not well formed (" + mm.Path + ")"; return null; }
                    m.Members.Add(mm);
                }
                if (Sha(m.CanonicalText()) != m.Check) { problem = "its own check does not match (it is damaged)"; return null; }
                return m;
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidOperationException || ex is OverflowException || ex is DecoderFallbackException) { problem = "it is not readable (" + ex.Message + ")"; return null; }
        }

        public MapMember Find(string path) => Members.FirstOrDefault(x => x.Path == path);

        /// <summary>the same backup and the same member records (role aside)</summary>
        public bool SameRecords(MapDoc o, out string why)
        {
            why = null;
            if (o.BackupId != BackupId || o.Hwid != Hwid || o.Simulated != Simulated) { why = "they name different backups"; return false; }
            foreach (var m in Members)
            {
                var x = o.Find(m.Path); if (x == null) continue;
                if (x.Sha256 != m.Sha256 || x.Md5 != m.Md5 || x.Size != m.Size || x.CompressedSize != m.CompressedSize || x.HeaderOffset != m.HeaderOffset || x.Crc != m.Crc || x.LocalHeader != m.LocalHeader || !x.Chunks.SequenceEqual(m.Chunks))
                { why = "they disagree about " + m.Path; return false; }
            }
            return true;
        }
    }

    public static class RepairMap
    {
        public const string Format = "classic-cart-studio-console-backup-repair-map", Schema = "ccs-console-backup-repair/1";
        public const string DescriptorName = BackupFormat.DescriptorName, ChunkMapName = BackupFormat.ChunkMapName;
        public const int ChunkSize = 1 << 20;   // 1,048,576 bytes: see the header comment
        /// <summary>the descriptor's reserved size: the largest backup the format allows (3 GB, about 2,900 chunks of about 68 bytes in the
        /// JSON) and 256 members of about 700 bytes each fit with room to spare</summary>
        public const int DescriptorReserve = 400000;

        public static int ChunkCount(long bytes) => (int)((bytes + ChunkSize - 1) / ChunkSize);

        /// <summary>A map of the given members as the writer wrote them (their local headers rebuilt by the writer's own rule, which the
        /// verifier compares with the file), with their hashes from the manifest rows.</summary>
        public static MapDoc Build(string role, Dictionary<string, object> manifest, ZipWriter z, IEnumerable<ZipEntryInfo> members, IDictionary<string, (string sha256, string md5)> hashes)
        {
            var con = manifest.O("console");
            var d = new MapDoc
            {
                Role = role, BackupId = manifest.S("backup_id"), Simulated = manifest.B("simulated"), CreatedUtc = manifest.S("created_utc"), CompletedUtc = manifest.S("completed_utc"),
                Board = con?.S("board"), Hwid = con?.S("hwid"), Tag = con?.S("tag"), DosTime = z.DosTime, DosDate = z.DosDate, Zip64 = z.ForcedZip64,
            };
            foreach (var e in members)
            {
                if (e.Chunks == null) throw new InvalidOperationException("member " + e.Name + " was written without piece checksums");
                d.Members.Add(new MapMember
                {
                    Path = e.Name, HeaderOffset = e.HeaderOffset, DataOffset = e.DataOffset, Method = e.Method, Crc = e.Crc, Size = e.Size, CompressedSize = e.CompressedSize,
                    Sha256 = hashes[e.Name].sha256, Md5 = hashes[e.Name].md5, LocalHeader = Hex.Of(ZipWriter.LocalHeader(e, z.DosTime, z.DosDate)), Chunks = e.Chunks,
                });
            }
            return d;
        }

        /// <summary>Verifier step 4b, for a backup that has repair information (none: nothing to check; older backups stay valid). kept:
        /// the two members' bytes; chunks: every member's piece checksums recomputed from the file's stored bytes.</summary>
        public static void Check(Verification v, Dictionary<string, object> manifest, ZipReader z, IDictionary<string, byte[]> kept, IDictionary<string, List<string>> chunks)
        {
            var de = z.Find(DescriptorName); var ce = z.Find(ChunkMapName); var me = z.Find(BackupFormat.ManifestName);
            if (de == null && ce == null) return;
            if (de == null || ce == null) { v.Fail("The backup's repair information is incomplete (one of its two parts is missing)."); return; }
            if (de.HeaderOffset != 0 || de.Method != 0) { v.Fail("The backup's repair information is not where it belongs (first in the file)."); return; }
            var d = MapDoc.Parse(kept.TryGetValue(DescriptorName, out var db) ? db : null, out var dp);
            if (d == null || d.Role != "descriptor") { v.Fail("The backup's early member list is not readable: " + (dp ?? "it has the wrong role") + "."); return; }
            var c = MapDoc.Parse(kept.TryGetValue(ChunkMapName, out var cb) ? cb : null, out var cp);
            if (c == null || c.Role != "chunk-map") { v.Fail("The backup's piece checksums are not readable: " + (cp ?? "they have the wrong role") + "."); return; }
            var order = z.Entries.OrderBy(x => x.HeaderOffset).ToList();
            var head = z.LocalHeaderBytes(de); ushort dt = (ushort)(head[10] | head[11] << 8), dd = (ushort)(head[12] | head[13] << 8);
            var con = manifest.O("console");
            foreach (var (doc, want, what) in new[] { (d, order.Where(x => x != de && x != me).ToList(), "early member list"), (c, order.Where(x => x != de && x.HeaderOffset < ce.HeaderOffset).ToList(), "piece checksums") })
            {
                string Bad(string why) => "The backup's repair information (" + what + ") does not match its contents: " + why + ".";
                if (doc.BackupId != manifest.S("backup_id") || doc.Simulated != manifest.B("simulated") || doc.Hwid != con?.S("hwid") || doc.Tag != con?.S("tag") || doc.Board != con?.S("board")
                    || doc.CreatedUtc != manifest.S("created_utc") || doc.CompletedUtc != manifest.S("completed_utc")) { v.Fail(Bad("it names another backup or console")); continue; }
                if (doc.DosTime != dt || doc.DosDate != dd || doc.Zip64 != de.Zip64Local) { v.Fail(Bad("the file's time stamp or ZIP64 use differs")); continue; }
                if (doc.Members.Count != want.Count) { v.Fail(Bad("it lists " + doc.Members.Count + " members, the file has " + want.Count)); continue; }
                for (int i = 0; i < want.Count; i++)
                {
                    var mm = doc.Members[i]; var e = want[i];
                    if (mm.Path != e.Name || mm.HeaderOffset != e.HeaderOffset || mm.DataOffset != e.DataOffset || mm.Method != e.Method || mm.Crc != e.Crc || mm.Size != e.Size || mm.CompressedSize != e.CompressedSize)
                    { v.Fail(Bad("the place, size or CRC-32 of " + e.Name + " differs")); break; }
                    if (!v.Members.TryGetValue(e.Name, out var got) || got.sha256 != mm.Sha256 || got.md5 != mm.Md5) { v.Fail(Bad("the checksum of " + e.Name + " differs")); break; }
                    if (Hex.Of(z.LocalHeaderBytes(e)) != mm.LocalHeader) { v.Fail(Bad("the local header of " + e.Name + " differs")); break; }
                    if (!chunks.TryGetValue(e.Name, out var ch) || !ch.SequenceEqual(mm.Chunks)) { v.Fail(Bad("a piece of " + e.Name + " differs")); break; }
                }
            }
            if (!c.SameRecords(d, out var why)) v.Fail("The backup's two copies of its repair information disagree: " + why + ".");
            if (v.Ok) v.RepairMap = true;
        }
    }
}
