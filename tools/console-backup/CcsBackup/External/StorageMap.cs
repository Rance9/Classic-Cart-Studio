// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The storage backup's maps (ExternalFormat.cs): repair/map-NNNNNN.json, written as the capture goes (after the members it records, so the
// start of a file whose end is lost still has its records), and its copy repair/map-NNNNNN-copy.json with the same bytes, all copies
// together near the end. A map records, for every member written since the previous map (except maps and the manifest): its name, the
// place of its local header and of its data, its method, CRC-32, sizes, SHA-256, MD5, its exact local header (hex) and the SHA-256 of each
// 1,048,576-byte piece of its stored bytes; and check_sha256, the SHA-256 of a canonical text of all of that (CanonicalText), so a map is
// proven undamaged by itself. A map says what the bytes were; every use reads the bytes again.
//   StorageWriter   the capture side: members added through it are recorded, a map is written when enough records wait (or when asked),
//                   and Finish writes the last map, every map's copy and the manifest (which lists every map with its place and checksums).
using CcsBackup.Archive;
using CcsBackup.Repair;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CcsBackup.External
{
    public sealed class StorageMap
    {
        public string BackupId, Check; public int Index; public ushort DosTime, DosDate; public int ChunkSize = ExternalFormat.ChunkSize;
        public readonly List<MapMember> Members = new List<MapMember>();
        static string I(long x) => x.ToString(CultureInfo.InvariantCulture);

        public string CanonicalText()
        {
            var sb = new StringBuilder();
            sb.Append(ExternalFormat.MapSchema).Append('\n').Append(BackupId).Append('\n').Append(I(Index)).Append('\n').Append(I(DosTime)).Append(' ').Append(I(DosDate)).Append(' ').Append(I(ChunkSize)).Append('\n');
            foreach (var m in Members)
            {
                sb.Append(m.Path).Append('|').Append(I(m.HeaderOffset)).Append('|').Append(I(m.DataOffset)).Append('|').Append(I(m.Method)).Append('|').Append(m.Crc.ToString("x8"))
                  .Append('|').Append(I(m.Size)).Append('|').Append(I(m.CompressedSize)).Append('|').Append(m.Sha256).Append('|').Append(m.Md5).Append('|').Append(m.LocalHeader).Append('\n');
                for (int i = 0; i < m.Chunks.Count; i++) sb.Append(' ').Append(I(i)).Append(' ').Append(m.Chunks[i]).Append('\n');
            }
            return sb.ToString();
        }

        public byte[] Bytes()
        {
            using (var h = SHA256.Create()) Check = Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(CanonicalText())));
            return Json.Bytes(new JObj
            {
                { "format", ExternalFormat.MapFormat }, { "schema", ExternalFormat.MapSchema }, { "backup_id", BackupId }, { "map", (long)Index },
                { "about", "The records of the members before this map: where each lies in the file, its exact header, its checksums and the checksum of each 1,048,576-byte piece. The same bytes are kept again near the end of the file." },
                { "zip", new JObj { { "dos_time", (long)DosTime }, { "dos_date", (long)DosDate }, { "zip64", true } } }, { "chunk_size", (long)ChunkSize },
                { "members", Members.Select(m => (object)new JObj { { "path", m.Path }, { "header_offset", m.HeaderOffset }, { "data_offset", m.DataOffset }, { "method", (long)m.Method }, { "crc32", m.Crc.ToString("x8") },
                    { "size", m.Size }, { "compressed_size", m.CompressedSize }, { "sha256", m.Sha256 }, { "md5", m.Md5 }, { "local_header", m.LocalHeader }, { "chunks", m.Chunks } }).ToList() },
                { "check_sha256", Check },
            });
        }

        /// <summary>untrusted bytes: null (with the reason) unless every field is well formed and check_sha256 agrees</summary>
        public static StorageMap Parse(byte[] content, out string problem)
        {
            problem = null;
            try
            {
                if (content == null || content.Length == 0 || content.Length > Json.MaxText) { problem = "it is missing or too large"; return null; }
                var d = Json.Parse(new UTF8Encoding(false, true).GetString(content));
                if (d.S("format") != ExternalFormat.MapFormat || d.S("schema") != ExternalFormat.MapSchema) { problem = "it is not a storage backup map this version reads"; return null; }
                var zip = d.O("zip");
                var m = new StorageMap { BackupId = d.S("backup_id"), Index = (int)d.L("map"), Check = d.S("check_sha256"), ChunkSize = (int)d.L("chunk_size"), DosTime = (ushort)(zip?.L("dos_time") ?? 0), DosDate = (ushort)(zip?.L("dos_date") ?? 0) };
                if (m.BackupId == null || m.Index < 1 || zip == null || !Hex.IsHex(m.Check, 64) || m.ChunkSize != ExternalFormat.ChunkSize) { problem = "a field is missing or not valid"; return null; }
                long end = 0; var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var x in (d.A("members") ?? new List<object>()).OfType<Dictionary<string, object>>())
                {
                    var mm = new MapMember
                    {
                        Path = x.S("path"), HeaderOffset = x.L("header_offset"), DataOffset = x.L("data_offset"), Method = (int)x.L("method"), Size = x.L("size"), CompressedSize = x.L("compressed_size"),
                        Sha256 = x.S("sha256"), Md5 = x.S("md5"), LocalHeader = x.S("local_header"), Chunks = x.A("chunks")?.Select(c => c as string).ToList(),
                    };
                    var crc = x.S("crc32");
                    if (!ExternalFormat.Allowed(mm.Path) || mm.Path == ExternalFormat.ManifestName || ExternalFormat.IsMap(mm.Path) || !seen.Add(mm.Path)) { problem = "it lists a member name this format never records, or one twice"; return null; }
                    if (!Hex.IsHex(crc, 8) || !Hex.IsHex(mm.Sha256, 64) || !Hex.IsHex(mm.Md5, 32) || mm.LocalHeader == null || mm.LocalHeader.Length % 2 != 0 || mm.LocalHeader.Length < 60 || mm.LocalHeader.Length > 2 * 400 || !Hex.IsHex(mm.LocalHeader, mm.LocalHeader.Length))
                    { problem = "a member's checksums or header are not well formed (" + mm.Path + ")"; return null; }
                    mm.Crc = uint.Parse(crc, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    if (mm.Method != 0 && mm.Method != 8 || mm.Size < 0 || mm.CompressedSize < 0 || mm.Size > ExternalFormat.MaxSize(mm.Path) || mm.Method == 0 && mm.Size != mm.CompressedSize
                        || mm.HeaderOffset < end || mm.DataOffset != mm.HeaderOffset + mm.LocalHeader.Length / 2) { problem = "a member's place or size is impossible (" + mm.Path + ")"; return null; }
                    end = mm.DataEnd;
                    if (mm.Chunks == null || mm.Chunks.Count != RepairMap.ChunkCount(mm.CompressedSize) || mm.Chunks.Any(c => !Hex.IsHex(c, 64))) { problem = "a member's piece checksums are not well formed (" + mm.Path + ")"; return null; }
                    m.Members.Add(mm);
                }
                string check; using (var h = SHA256.Create()) check = Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(m.CanonicalText())));
                if (check != m.Check) { problem = "its own check does not match (it is damaged)"; return null; }
                return m;
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidOperationException || ex is OverflowException || ex is DecoderFallbackException) { problem = "it is not readable (" + ex.Message + ")"; return null; }
        }
    }

    /// <summary>One map as the manifest lists it.</summary>
    public sealed class MapListing { public string Path, Sha256, Md5; public long HeaderOffset, Size; public int Index; public bool Copy; public uint Crc; public int Members; }

    /// <summary>The capture side of a storage backup file: members, maps, their copies and the manifest, in that order.</summary>
    public sealed class StorageWriter
    {
        readonly FileStream fs; readonly ZipWriter z; readonly string backupId;
        readonly List<MapMember> pending = new List<MapMember>(); long pendingBytes; int mapIndex;
        readonly List<(MapListing listing, byte[] bytes)> maps = new List<(MapListing, byte[])>();
        /// <summary>every member written and its SHA-256 as computed while it streamed (the verifier compares the closed file with these)</summary>
        public readonly Dictionary<string, string> Expected = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly List<ZipEntryInfo> Entries = new List<ZipEntryInfo>();
        public long Written => fs.Position;
        public FileStream File => fs;

        public StorageWriter(FileStream file, string id, DateTime stamp)
        {
            fs = file; backupId = id;
            z = new ZipWriter(fs, stamp, forceZip64: true) { ChunkSize = ExternalFormat.ChunkSize };
        }

        /// <summary>one member, streamed and hashed (and recorded for the next map)</summary>
        public ZipEntryInfo Add(string name, bool compress, Action<Stream> write, Func<bool> cancelled = null, Action<long> progress = null)
        {
            if (!ExternalFormat.Allowed(name) || ExternalFormat.IsMap(name) || name == ExternalFormat.ManifestName) throw new ArgumentException("not a member this writer adds: " + name);
            var (e, sha, md5) = z.Add(name, compress, write, true, progress, cancelled);
            Expected[name] = sha; Entries.Add(e);
            var mm = new MapMember { Path = e.Name, HeaderOffset = e.HeaderOffset, DataOffset = e.DataOffset, Method = e.Method, Crc = e.Crc, Size = e.Size, CompressedSize = e.CompressedSize, Sha256 = sha, Md5 = md5, LocalHeader = Hex.Of(ZipWriter.LocalHeader(e, z.DosTime, z.DosDate)), Chunks = e.Chunks };
            pending.Add(mm); pendingBytes += 300 + 70L * mm.Chunks.Count;
            return e;
        }

        public void AddJson(string name, object doc, Func<bool> cancelled = null) { var b = Json.Bytes(doc); Add(name, true, s => s.Write(b, 0, b.Length), cancelled); }

        /// <summary>writes a map when enough records wait (force: whatever waits)</summary>
        public void MaybeMap(bool force = false)
        {
            if (pending.Count == 0 || !force && pendingBytes < ExternalFormat.MapRecordBudget) return;
            var m = new StorageMap { BackupId = backupId, Index = ++mapIndex, DosTime = z.DosTime, DosDate = z.DosDate }; m.Members.AddRange(pending);
            var bytes = m.Bytes();
            var listing = WriteMap(ExternalFormat.MapName(m.Index, false), bytes); listing.Index = m.Index; listing.Members = m.Members.Count;
            maps.Add((listing, bytes)); pending.Clear(); pendingBytes = 0;
        }

        MapListing WriteMap(string name, byte[] bytes)
        {
            var (e, sha, md5) = z.Add(name, false, s => s.Write(bytes, 0, bytes.Length), true);
            Expected[name] = sha; Entries.Add(e);
            return new MapListing { Path = name, Sha256 = sha, Md5 = md5, HeaderOffset = e.HeaderOffset, Size = e.Size, Crc = e.Crc, Copy = ExternalFormat.IsMapCopy(name) };
        }

        /// <summary>the last map, every map's copy, then the manifest (given the maps' listings to include), the member index; flushed</summary>
        public void Finish(Func<List<MapListing>, JObj> manifest)
        {
            MaybeMap(true);
            var all = maps.Select(x => x.listing).ToList();
            foreach (var (listing, bytes) in maps.ToList()) { var c = WriteMap(ExternalFormat.MapName(listing.Index, true), bytes); c.Index = listing.Index; c.Members = listing.Members; all.Add(c); }
            var man = Json.Bytes(manifest(all));
            if (man.Length > Json.MaxText) throw new IOException("the backup's manifest would be larger than this format allows");
            var (me, msha, _) = z.Add(ExternalFormat.ManifestName, true, s => s.Write(man, 0, man.Length), true);
            Expected[ExternalFormat.ManifestName] = msha; Entries.Add(me);
            z.Finish(); fs.Flush(true);
        }

        public static JObj ListingJson(MapListing l) => new JObj { { "path", l.Path }, { "map", (long)l.Index }, { "copy", l.Copy }, { "header_offset", l.HeaderOffset }, { "size", l.Size }, { "crc32", l.Crc.ToString("x8") }, { "sha256", l.Sha256 }, { "md5", l.Md5 }, { "members", (long)l.Members } };
    }
}
