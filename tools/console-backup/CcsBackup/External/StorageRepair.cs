// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Repairing USB and SD storage backups. Every route reads the files it is given and writes only something NEW; a damaged file is never
// changed, and a new backup file gets its name only after it passes the full check (StorageVerifier) from scratch.
//   Protect / CheckParity / RebuildFromParity   recovery data (PAR2) for a checked backup, checking against it, and a damaged backup
//                rebuilt from it into a new file: the shared Repair/Parity.cs code, which checks a storage backup with its own verifier.
//   SecondCopy   the shared Repair/SecondCopy.cs (checked in full first, copied, read back and compared; never a damaged file).
//   Rebuild      one new, complete backup file from a damaged one: each member's local header from its map record, each 1,048,576-byte
//                piece from whichever file holds it with its recorded SHA-256: the damaged file itself, other copies of the same backup,
//                or other backups of the SAME storage (the same identity key) where a piece has exactly that checksum (an unchanged
//                region of a drive image, an unchanged large file, a pack that is byte for byte the same). Maps from wherever they verify
//                (a map or its copy); the manifest from a copy where it is intact (it is never rebuilt: its bytes are recorded nowhere
//                else). Anything without such a source makes the repair stop and write nothing.
//   Salvage      when a backup cannot be repaired: every file whose bytes, read through the backup's surviving records (each piece from a
//                file where it matches its recorded checksum), still have the SHA-256 its list recorded, exported into a NEW folder that is
//                never a backup. Nothing without a surviving recorded checksum is exported.
// Members are found in a damaged file through its member index when the strict reader accepts it, else by walking the local headers from
// the start (each next header exactly where the previous member ends; names only from this format's list; ZIP64 sizes from a well-formed
// extra field; nothing past the file's end).
using CcsBackup.Archive;
using CcsBackup.Engine;
using CcsBackup.Library;
using CcsBackup.Repair;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace CcsBackup.External
{
    /// <summary>One storage backup file as found (possibly damaged), open for reading only.</summary>
    public sealed class StorageFile : IDisposable
    {
        public string Path, Label, IndexProblem, LocatedBy, ManifestProblem; public long Length; internal FileStream S;
        public bool IndexIntact; public Dictionary<string, object> Manifest; public byte[] ManifestRaw, ManifestHeader;
        public readonly Dictionary<string, Located> At = new Dictionary<string, Located>(StringComparer.Ordinal);
        public readonly Dictionary<int, StorageMap> Maps = new Dictionary<int, StorageMap>();
        public readonly Dictionary<int, byte[]> MapBytes = new Dictionary<int, byte[]>();
        public string BackupId => Manifest?.S("backup_id") ?? Maps.Values.FirstOrDefault()?.BackupId;
        public HashSet<string> SourceKeys => new HashSet<string>((Manifest?.A("sources") ?? new List<object>()).OfType<Dictionary<string, object>>().Select(x => x.O("identity")?.S("key")).Where(k => k != null), StringComparer.Ordinal);

        public static StorageFile Open(string path, string label)
        {
            var f = new StorageFile { Path = System.IO.Path.GetFullPath(path), Label = label };
            f.S = new FileStream(f.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess); f.Length = f.S.Length;
            try
            {
                var z = new ZipReader(f.S, ExternalFormat.Allowed, false, ExternalFormat.Limits);
                foreach (var e in z.Entries) f.At[e.Name] = new Located { Name = e.Name, HeaderOffset = e.HeaderOffset, DataOffset = e.DataOffset, CompressedSize = e.CompressedSize, Size = e.Size, Method = e.Method, Crc = e.Crc, Flags = e.Flags, Header = z.LocalHeaderBytes(e), Zip64Local = e.Zip64Local };
                f.IndexIntact = true; f.LocatedBy = "the file's table of contents (its ZIP index)";
            }
            catch (Exception ex) when (ex is ZipFormatException || ex is IOException) { f.IndexProblem = ex.Message; f.Walk(); }
            f.ReadRecords();
            return f;
        }

        public void Dispose() { S?.Dispose(); S = null; }

        internal byte[] ReadUpTo(long pos, long n)
        {
            if (pos < 0 || pos >= Length || n <= 0) return new byte[0];
            var b = new byte[(int)Math.Min(n, Length - pos)]; S.Position = pos; int got = 0;
            while (got < b.Length) { int r = S.Read(b, got, b.Length - got); if (r <= 0) break; got += r; }
            if (got < b.Length) Array.Resize(ref b, got);
            return b;
        }

        void Walk()
        {
            long pos = 0; var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (pos + 30 <= Length && At.Count < 4_000_000)
            {
                var h = ReadUpTo(pos, 30); if (h.Length < 30 || BitConverter.ToUInt32(h, 0) != 0x04034b50) break;
                int nl = BitConverter.ToUInt16(h, 26), xl = BitConverter.ToUInt16(h, 28); if (nl == 0 || nl > 64 || xl > 64) break;
                var rest = ReadUpTo(pos + 30, nl + xl); if (rest.Length < nl + xl) break;
                var name = Encoding.ASCII.GetString(rest, 0, nl); if (!ExternalFormat.Allowed(name) || !seen.Add(name)) break;
                long cs = BitConverter.ToUInt32(h, 18), us = BitConverter.ToUInt32(h, 22); bool z64 = false;
                for (int p = nl; p + 4 <= nl + xl;) { int tag = BitConverter.ToUInt16(rest, p), sz = BitConverter.ToUInt16(rest, p + 2); if (tag == 1 && sz >= 16) { us = BitConverter.ToInt64(rest, p + 4); cs = BitConverter.ToInt64(rest, p + 12); z64 = true; } p += 4 + sz; }
                ushort method = BitConverter.ToUInt16(h, 8);
                if (cs < 0 || us < 0 || cs == 0xFFFFFFFF || (method != 0 && method != 8) || method == 0 && cs != us) break;
                var L = new Located { Name = name, HeaderOffset = pos, DataOffset = pos + 30 + nl + xl, CompressedSize = cs, Size = us, Method = method, Crc = BitConverter.ToUInt32(h, 14), Flags = BitConverter.ToUInt16(h, 6), Header = h.Concat(rest).ToArray(), Zip64Local = z64 };
                At[name] = L; pos = L.DataEnd;
                if (name == ExternalFormat.ManifestName) break;
            }
            LocatedBy = "a walk through the part headers from the start of the file";
        }

        internal byte[] Content(Located L, out string why)
        {
            why = null;
            if (L.CompressedSize > Json.MaxText * 2L || L.Size > Json.MaxText) { why = "it is larger than its kind can be"; return null; }
            var raw = ReadUpTo(L.DataOffset, L.CompressedSize); if (raw.Length != L.CompressedSize) { why = "the file ends inside it"; return null; }
            byte[] b = raw;
            if (L.Method == 8) { try { using (var d = new DeflateStream(new MemoryStream(raw), CompressionMode.Decompress)) { var m = new MemoryStream(); d.CopyTo(m); b = m.ToArray(); } } catch (InvalidDataException) { why = "it cannot be unpacked"; return null; } }
            var c = new Crc32(); c.Update(b, 0, b.Length); if (b.Length != L.Size || c.Value != L.Crc) { why = "it is damaged (its CRC-32 does not match)"; return null; }
            return b;
        }

        void ReadRecords()
        {
            if (At.TryGetValue(ExternalFormat.ManifestName, out var me))
            {
                var b = Content(me, out var why);
                if (b == null) ManifestProblem = "the manifest cannot be used: " + why;
                else
                {
                    try { var m = Json.Parse(new UTF8Encoding(false, true).GetString(b)); if (m.S("format") == ExternalFormat.Format && m.S("schema") == ExternalFormat.Schema) { Manifest = m; ManifestRaw = ReadUpTo(me.DataOffset, me.CompressedSize); ManifestHeader = me.Header; } else ManifestProblem = "the manifest is not a USB and SD backup's"; }
                    catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is DecoderFallbackException) { ManifestProblem = "the manifest is not readable"; }
                }
            }
            else ManifestProblem = "the manifest (the list of contents at the end of the file) was not found";
            foreach (var kv in At.Where(x => ExternalFormat.IsMap(x.Key)))
            {
                var b = Content(kv.Value, out _); if (b == null) continue;
                var m = StorageMap.Parse(b, out _); if (m == null || Maps.ContainsKey(m.Index)) continue;
                Maps[m.Index] = m; MapBytes[m.Index] = b;
            }
        }

        /// <summary>the SHA-256 of each 1,048,576-byte piece of [from, from+len) as it is in this file (null where the file ends first)</summary>
        internal string[] Pieces(long from, long len, Func<bool> cancelled)
        {
            int n = RepairMap.ChunkCount(len); var r = new string[n]; var buf = new byte[ExternalFormat.ChunkSize];
            using (var h = FastHash.Sha256())
                for (int i = 0; i < n; i++)
                {
                    if (cancelled != null && cancelled()) throw new OperationCanceledException();
                    long off = from + (long)i * ExternalFormat.ChunkSize; int want = (int)Math.Min(ExternalFormat.ChunkSize, len - (long)i * ExternalFormat.ChunkSize);
                    if (off + want > Length) break;
                    S.Position = off; int got = 0; while (got < want) { int k = S.Read(buf, got, want - got); if (k <= 0) break; got += k; }
                    if (got < want) break;
                    r[i] = Hex.Of(h.ComputeHash(buf, 0, want));
                }
            return r;
        }
    }

    /// <summary>The records trusted for one backup, from one or more copies of it: the manifest, every map, every member's record.</summary>
    public sealed class StorageRecords
    {
        public string BackupId, Problem; public Dictionary<string, object> Manifest; public StorageFile ManifestFrom;
        public readonly Dictionary<int, (byte[] bytes, StorageMap map)> Maps = new Dictionary<int, (byte[], StorageMap)>();
        public readonly List<MapMember> Members = new List<MapMember>();
        public List<Dictionary<string, object>> Listings => (Manifest?.A("maps") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
        public ushort DosTime, DosDate;

        public static StorageRecords Build(IList<StorageFile> copies)
        {
            var t = new StorageRecords();
            var ids = copies.Select(c => c.BackupId).Where(x => x != null).Distinct().ToList();
            if (ids.Count == 0) { t.Problem = "No trustworthy list of this backup's contents survives in the files given: " + string.Join(" ", copies.Select(c => c.Label + ": " + (c.ManifestProblem ?? "no readable map") + ".")); return t; }
            if (ids.Count > 1) { t.Problem = "The files given are not copies of one backup (they name different backups). Nothing was used."; return t; }
            t.BackupId = ids[0];
            var mans = copies.Where(c => c.Manifest != null).ToList();
            if (mans.Select(c => Hex.Of(SHA256.Create().ComputeHash(c.ManifestRaw))).Distinct().Count() > 1) { t.Problem = "The copies' manifests differ, so they are not copies of one backup (or one was altered). Nothing was used."; return t; }
            if (mans.Count > 0) { t.Manifest = mans[0].Manifest; t.ManifestFrom = mans[0]; }
            foreach (var c in copies)
                foreach (var kv in c.Maps)
                {
                    if (kv.Value.BackupId != t.BackupId) continue;
                    if (t.Maps.TryGetValue(kv.Key, out var have)) { if (!have.bytes.SequenceEqual(c.MapBytes[kv.Key])) { t.Problem = "Two copies of map " + kv.Key + " differ. Nothing was used."; return t; } continue; }
                    t.Maps[kv.Key] = (c.MapBytes[kv.Key], kv.Value);
                }
            if (t.Manifest != null)
                foreach (var l in t.Listings)
                    if (t.Maps.TryGetValue((int)l.L("map"), out var m)) using (var h = SHA256.Create()) if (Hex.Of(h.ComputeHash(m.bytes)) != l.S("sha256")) { t.Problem = "A map does not match the checksum the manifest recorded for it. Nothing was used."; return t; }
            foreach (var m in t.Maps.OrderBy(x => x.Key)) t.Members.AddRange(m.Value.map.Members);
            var first = t.Maps.Values.Select(x => x.map).FirstOrDefault(); if (first != null) { t.DosTime = first.DosTime; t.DosDate = first.DosDate; }
            return t;
        }

        public MapMember Find(string path) => Members.FirstOrDefault(m => m.Path == path);
    }

    public static class StorageRepair
    {
        // ------------------------------------------------------------------ recovery data and the second copy (the shared code)
        public static ParityResult Protect(string backup, int percent, Action<string, long, long> progress = null, Func<bool> cancelled = null)
        {
            if (!ExternalFormat.Is(backup)) return new ParityResult { Text = "This file is not a USB and SD backup." };
            if (cancelled != null && cancelled()) return new ParityResult { Text = "Canceled; no recovery data was made." };
            return Parity.Protect(backup, percent, progress, CancellationToken.None);
        }
        public static ParityResult CheckParity(string backup, Action<string, long, long> progress = null) => Parity.Check(backup, progress);
        public static ParityResult RebuildFromParity(string backup, Action<string, long, long> progress = null) => Parity.Rebuild(backup, progress);
        public static RepairResult MakeSecondCopy(string backup, string folder, RepairOptions opt = null) => SecondCopy.Make(backup, folder, opt);

        // ------------------------------------------------------------------ the rebuild
        sealed class Part { public string Name; public byte[] Literal; public StorageFile From; public long Offset, Length; public string Expect; }

        /// <summary>A new, complete backup file in outputFolder from the damaged copies of one backup and other backups of the same storage
        /// (donors). Everything given is only read; nothing is written unless every byte has a source with its recorded checksum.</summary>
        public static RepairResult Rebuild(IList<string> copies, IList<string> donors, string outputFolder, RepairOptions opt = null)
        {
            opt = opt ?? new RepairOptions();
            var rep = new RepairReport("storage-repair", "Repair a damaged USB and SD backup"); var res = new RepairResult { Report = rep };
            var files = new List<StorageFile>(); var dons = new List<StorageFile>(); string partial = null;
            var before = new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);
            RepairResult End(string outcome, string summary)
            {
                rep.Outcome = res.Outcome = outcome; rep.Summary = summary; rep.FinishedUtc = DateTime.UtcNow;
                foreach (var f in files.Concat(dons)) f.Dispose();
                foreach (var kv in before) { var fi = new FileInfo(kv.Key); rep.Originals.Add(new JObj { { "file", kv.Key }, { "unchanged", fi.Exists && fi.Length == kv.Value.Item1 && fi.LastWriteTimeUtc == kv.Value.Item2 } }); }
                if (partial != null && File.Exists(partial)) { try { File.Delete(partial); } catch (IOException ex) { rep.Errors.Add("The unfinished file could not be removed: " + ex.Message); } }
                if (opt.SaveReport && outcome != "canceled" && outputFolder != null && copies?.Count > 0)
                    try { (res.ReportJson, res.ReportText) = rep.Save(outputFolder, System.IO.Path.GetFileNameWithoutExtension(copies[0]) + " (repair report " + DateTime.Now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture) + ")"); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { rep.Errors.Add("The report could not be saved: " + ex.Message); }
                return res;
            }
            try
            {
                if (copies == null || copies.Count == 0) return End("refused", "No backup file was given.");
                var all = copies.Concat(donors ?? new string[0]).Select(System.IO.Path.GetFullPath).ToList();
                if (all.Distinct(StringComparer.OrdinalIgnoreCase).Count() != all.Count) return End("refused", "The same file was given twice.");
                foreach (var p in all) if (!File.Exists(p)) return End("refused", "The file " + p + " was not found.");
                if (string.IsNullOrEmpty(outputFolder)) return End("refused", "No folder was chosen for the repaired backup.");
                Directory.CreateDirectory(outputFolder);
                foreach (var p in all) { var fi = new FileInfo(p); before[p] = (fi.Length, fi.LastWriteTimeUtc); }
                for (int i = 0; i < copies.Count; i++) files.Add(StorageFile.Open(copies[i], i == 0 ? "the damaged backup file" : "its copy " + System.IO.Path.GetFileName(copies[i])));
                var t = StorageRecords.Build(files);
                rep.Original = Identity(files[0], t);
                foreach (var f in files.Skip(1)) rep.Others.Add(Identity(f, t));
                if (t.Problem != null) { rep.Lost("the whole backup", t.Problem); return End(t.BackupId == null ? "not-repaired" : "refused", t.Problem); }
                {
                    var v0 = StorageVerifier.Verify(files[0].Path, null, opt.Cancelled, (d, n) => opt.Say("examine", "Checking the backup file...", d, n));
                    if (v0.Ok) return End("not-needed", "The backup file is not damaged: it passes its full check, every part and every file list matching its records. Nothing was written.");
                }
                if (t.Manifest == null) { rep.Lost("the manifest", "it is damaged or missing in every file given, and its exact bytes are recorded nowhere else"); rep.Notes.Add("Salvage can still export every file whose recorded checksum survives."); return End("not-repaired", "The backup could not be repaired: its manifest (the list of contents at the end of the file) is damaged in every file given. Nothing was written."); }
                var keys = files[0].SourceKeys;
                foreach (var d in donors ?? new string[0])
                {
                    var df = StorageFile.Open(d, "another backup (" + System.IO.Path.GetFileName(d) + ")");
                    if (df.Manifest == null) { rep.Skip(df.Label, "its own manifest is not readable, so it cannot be shown to be of the same storage"); df.Dispose(); continue; }
                    if (df.BackupId == t.BackupId) df.Label = "another copy of the same backup (" + System.IO.Path.GetFileName(d) + ")";
                    else if (!df.SourceKeys.Overlaps(keys)) { rep.Skip(df.Label, "it is a backup of other storage; pieces are only ever taken from backups of the same USB drive, SD card or drive image"); df.Dispose(); continue; }
                    dons.Add(df);
                }
                rep.Methods.Add("The backup's own records were used: its manifest and its maps (each map's own check, and its checksum in the manifest).");
                rep.Methods.Add("Each 1,048,576-byte piece was taken only from a file where it has exactly its recorded SHA-256: the damaged file, a copy of it, or another backup of the same storage.");
                // which maps are missing in every file given
                foreach (var l in t.Listings.Where(l => !l.B("copy")))
                    if (!t.Maps.ContainsKey((int)l.L("map"))) rep.Lost("map " + l.L("map") + " (the records of part of the backup)", "it and its copy are damaged in every file given");
                if (rep.Unrecoverable.Count > 0) return End("not-repaired", "The backup could not be repaired: some of its records are damaged in every file given. Nothing was written. Salvage can export what still matches.");
                // every piece's sources: the copies at the recorded places, then donors' pieces by checksum
                long total = files.Concat(dons).Sum(f => f.Length), seen = 0;
                var have = new Dictionary<string, (StorageFile f, long off)>(StringComparer.Ordinal);
                foreach (var d in dons)
                    foreach (var L in d.At.Values.Where(x => ExternalFormat.IsPayload(x.Name)))
                    {
                        var ps = d.Pieces(L.DataOffset, L.CompressedSize, opt.Cancelled); seen += L.CompressedSize; opt.Say("examine", "Checking " + d.Label + "...", seen, total);
                        for (int i = 0; i < ps.Length; i++) if (ps[i] != null) { long len = Math.Min(ExternalFormat.ChunkSize, L.CompressedSize - (long)i * ExternalFormat.ChunkSize); var key = ps[i] + ":" + len; if (!have.ContainsKey(key)) have[key] = (d, L.DataOffset + (long)i * ExternalFormat.ChunkSize); }
                    }
                var parts = new List<Part>(); var entries = new List<ZipEntryInfo>(); long pos = 0; int fromDonor = 0, fromCopies = 0;
                var layout = t.Members.Select(m => (m.HeaderOffset, kind: "member", m, l: (Dictionary<string, object>)null))
                    .Concat(t.Listings.Select(l => (HeaderOffset: l.L("header_offset"), kind: "map", m: (MapMember)null, l))).OrderBy(x => x.HeaderOffset).ToList();
                var copyPieces = new Dictionary<(StorageFile, string), string[]>();
                foreach (var x in layout)
                {
                    if (opt.Cancelled()) throw new OperationCanceledException();
                    if (x.HeaderOffset != pos) { rep.Lost(x.kind == "map" ? x.l.S("path") : x.m.Path, "its recorded place cannot be reproduced"); break; }
                    if (x.kind == "map")
                    {
                        var (bytes, _) = t.Maps[(int)x.l.L("map")]; var crc = new Crc32(); crc.Update(bytes, 0, bytes.Length);
                        var e = new ZipEntryInfo { Name = x.l.S("path"), Method = 0, Crc = crc.Value, Size = bytes.Length, CompressedSize = bytes.Length, Flags = 0x0800, ExternalAttributes = ZipWriter.UnixRegular0644, MadeBy = ZipWriter.WriterMadeBy, Zip64Local = true, HeaderOffset = pos };
                        var h = ZipWriter.LocalHeader(e, t.DosTime, t.DosDate); e.DataOffset = pos + h.Length;
                        parts.Add(new Part { Name = e.Name, Literal = h }); parts.Add(new Part { Name = e.Name, Literal = bytes }); entries.Add(e); pos = e.DataOffset + bytes.Length; continue;
                    }
                    var m = x.m; var hb = FromHex(m.LocalHeader);
                    var me = new ZipEntryInfo { Name = m.Path, Method = (ushort)m.Method, Crc = m.Crc, Size = m.Size, CompressedSize = m.CompressedSize, Flags = 0x0800, ExternalAttributes = ZipWriter.UnixRegular0644, MadeBy = ZipWriter.WriterMadeBy, Zip64Local = true, HeaderOffset = pos, DataOffset = pos + hb.Length };
                    if (me.DataOffset != m.DataOffset) { rep.Lost(m.Path, "its recorded header does not fit its recorded place"); break; }
                    parts.Add(new Part { Name = m.Path, Literal = hb }); entries.Add(me);
                    for (int i = 0; i < m.Chunks.Count; i++)
                    {
                        long off = (long)i * ExternalFormat.ChunkSize, len = Math.Min(ExternalFormat.ChunkSize, m.CompressedSize - off);
                        Part p = null;
                        foreach (var f in files)
                        {
                            if (!copyPieces.TryGetValue((f, m.Path), out var ps)) { ps = f.Pieces(m.DataOffset, m.CompressedSize, opt.Cancelled); copyPieces[(f, m.Path)] = ps; seen += m.CompressedSize; opt.Say("examine", "Checking the damaged backup...", seen, total); }
                            if (i < ps.Length && ps[i] == m.Chunks[i]) { p = new Part { Name = m.Path, From = f, Offset = m.DataOffset + off, Length = len, Expect = m.Chunks[i] }; if (f != files[0]) fromCopies++; break; }
                        }
                        if (p == null && have.TryGetValue(m.Chunks[i] + ":" + len, out var dh)) { p = new Part { Name = m.Path, From = dh.f, Offset = dh.off, Length = len, Expect = m.Chunks[i] }; fromDonor++; }
                        if (p == null) { rep.Lost(m.Path + ", piece " + (i + 1) + " of " + m.Chunks.Count, "no file given holds this piece with its recorded checksum", m.DataOffset + off, m.DataOffset + off + len); continue; }
                        parts.Add(p);
                    }
                    foreach (var f in files) copyPieces.Remove((f, m.Path));
                    pos = me.DataOffset + m.CompressedSize;
                }
                if (rep.Unrecoverable.Count > 0)
                {
                    rep.Notes.Add("No file was made: a backup missing any piece is never offered as a backup. Salvage can export every file whose recorded checksum still matches.");
                    return End("not-repaired", "The backup could not be repaired: " + rep.Unrecoverable.Count + " " + (rep.Unrecoverable.Count == 1 ? "piece has" : "pieces have") + " no source with its recorded checksum. Nothing was written.");
                }
                // the manifest, as it is in a copy where it is intact
                var mf = t.ManifestFrom; var mh = mf.ManifestHeader; var mraw = mf.ManifestRaw;
                var ml = mf.At[ExternalFormat.ManifestName];
                var mze = new ZipEntryInfo { Name = ExternalFormat.ManifestName, Method = (ushort)ml.Method, Crc = ml.Crc, Size = ml.Size, CompressedSize = ml.CompressedSize, Flags = 0x0800, ExternalAttributes = ZipWriter.UnixRegular0644, MadeBy = ZipWriter.WriterMadeBy, Zip64Local = true, HeaderOffset = pos, DataOffset = pos + mh.Length };
                parts.Add(new Part { Name = mze.Name, Literal = mh }); parts.Add(new Part { Name = mze.Name, Literal = mraw }); entries.Add(mze); long length = mze.DataOffset + mraw.Length;
                // write, check, publish
                var stem = System.IO.Path.GetFileNameWithoutExtension(copies[0]) + " (repaired " + DateTime.Now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture) + ")";
                partial = CaptureEngine.Unique(outputFolder, stem, ExternalFormat.Partial);
                using (var o = Durable.CreateNew(partial))
                {
                    var buf = new byte[Streams.Buffer]; long done = 0;
                    foreach (var p in parts)
                    {
                        if (opt.Cancelled()) throw new OperationCanceledException();
                        if (p.Literal != null) { o.Write(p.Literal, 0, p.Literal.Length); done += p.Literal.Length; continue; }
                        using (var sha = FastHash.Sha256())
                        {
                            p.From.S.Position = p.Offset; long left = p.Length;
                            while (left > 0) { int n = p.From.S.Read(buf, 0, (int)Math.Min(buf.Length, left)); if (n <= 0) throw new RepairException(p.From.Label + " ended early while it was read"); sha.TransformBlock(buf, 0, n, null, 0); o.Write(buf, 0, n); left -= n; done += n; opt.Say("write", "Writing the repaired backup...", done, length); }
                            sha.TransformFinalBlock(new byte[0], 0, 0);
                            if (Hex.Of(sha.Hash) != p.Expect) throw new RepairException(p.From.Label + " changed while it was read (" + p.Name + "); nothing was kept");
                        }
                    }
                    if (o.Position != length) throw new RepairException("the repaired file came out at an unexpected size");
                    ZipWriter.WriteDirectory(o, entries, t.DosTime, t.DosDate, true); Durable.Flush(o);
                }
                opt.Say("verify", "Checking the repaired backup...");
                var v = StorageVerifier.Verify(partial, t.Members.ToDictionary(m => m.Path, m => m.Sha256), opt.Cancelled, (d, n) => opt.Say("verify", "Checking the repaired backup...", d, n));
                if (!v.Ok) { rep.Errors.AddRange(v.Problems); return End("failed", "The repaired file did not pass the full check, so it was deleted: " + v.Problems[0]); }
                var final = partial.Substring(0, partial.Length - ".partial".Length);
                Durable.Publish(partial, final); partial = null; res.OutputPath = final; res.RestorePoint = true;
                bool? identical = opt.RecordedSha256 == null ? (bool?)null : v.ArchiveSha256 == opt.RecordedSha256;
                if (fromCopies > 0) rep.Repair("pieces of the backup", fromCopies + " piece(s) taken from its other copies, each with its recorded checksum");
                if (fromDonor > 0) rep.Repair("pieces of the backup", fromDonor + " piece(s) taken from other backups of the same storage, where those bytes have exactly this backup's recorded checksum");
                if (!files[0].IndexIntact) rep.Repair("the table of contents (ZIP index)", "written again by Workbench's ZIP rules from the recorded places of every part");
                rep.Output = new JObj { { "path", final }, { "bytes", v.Bytes }, { "sha256", v.ArchiveSha256 }, { "backup_id", t.BackupId }, { "identical_to_original", identical },
                    { "checked", "It passed the full check from scratch: every part, every piece and every file list against the backup's records." },
                    { "container", identical == true ? "It is byte-for-byte the original backup file: its SHA-256 equals the one recorded when it was made." : "Every part matches its recorded checksum; it is a new file with its own identity" + (identical == false ? " (its SHA-256 differs from the original file's)." : ".") } };
                rep.Unknowns.Add("What damaged the original file: a repair reads files; it cannot tell the cause.");
                rep.Notes.Add("The damaged file is unchanged and still damaged. The new file is a separate backup file.");
                return End("repaired", "A repaired backup file was made and passed the full check: " + System.IO.Path.GetFileName(final) + ".");
            }
            catch (OperationCanceledException) { rep.Canceled = true; return End("canceled", "Canceled. Nothing was kept, and the files that were read are unchanged."); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is RepairException)
            { rep.Errors.Add(ex.Message); return End("failed", "The repair stopped: " + ex.Message + ". Nothing was kept, and the files that were read were not changed."); }
        }

        static byte[] FromHex(string h) { var b = new byte[h.Length / 2]; for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(h.Substring(2 * i, 2), 16); return b; }

        static JObj Identity(StorageFile f, StorageRecords t) => new JObj
        {
            { "role", "storage backup" }, { "label", f.Label }, { "file", System.IO.Path.GetFileName(f.Path) }, { "folder", System.IO.Path.GetDirectoryName(f.Path) }, { "bytes", f.Length },
            { "backup_id", f.BackupId ?? t?.BackupId }, { "located_by", f.LocatedBy }, { "condition", (f.IndexIntact ? "its table of contents (ZIP index) is intact" : "its table of contents is damaged (" + f.IndexProblem + "); its parts were found through " + f.LocatedBy) + (f.Manifest == null ? "; " + f.ManifestProblem : "") },
        };

        // ------------------------------------------------------------------ salvage
        public sealed class SalvageFile { public string Source, Path, Status, Trust, Sha256; public long Size; internal Func<Stream, bool> Copy; public string ExportedTo; }

        /// <summary>Every file of the damaged backup (and its copies) whose bytes still have the SHA-256 its surviving list recorded, ready to
        /// export; what cannot be checked is listed with the reason. Reads only. Dispose the files afterwards (Export does).</summary>
        public static (List<SalvageFile> files, List<string> notes, List<StorageFile> open, StorageRecords records) Survey(IList<string> copies, Func<bool> cancelled = null)
        {
            var open = copies.Select((p, i) => StorageFile.Open(p, i == 0 ? "the damaged backup file" : "copy " + (i + 1))).ToList();
            var t = StorageRecords.Build(open); var notes = new List<string>(); var list = new List<SalvageFile>();
            if (t.Problem != null) { notes.Add(t.Problem); return (list, notes, open, t); }
            // a member read piece by piece from the first file where each piece matches (else not readable)
            byte[] Member(MapMember m)
            {
                if (m.CompressedSize > Json.MaxText * 2L) return null;
                var o = new MemoryStream();
                for (int i = 0; i < m.Chunks.Count; i++)
                {
                    long off = (long)i * ExternalFormat.ChunkSize; int len = (int)Math.Min(ExternalFormat.ChunkSize, m.CompressedSize - off); byte[] good = null;
                    foreach (var f in open) { var b = f.ReadUpTo(m.DataOffset + off, len); if (b.Length == len) using (var h = FastHash.Sha256()) if (Hex.Of(h.ComputeHash(b)) == m.Chunks[i]) { good = b; break; } }
                    if (good == null) return null; o.Write(good, 0, good.Length);
                }
                var raw = o.ToArray(); if (m.Method == 0) return raw;
                try { using (var d = new DeflateStream(new MemoryStream(raw), CompressionMode.Decompress)) { var x = new MemoryStream(); d.CopyTo(x); return x.ToArray(); } } catch (InvalidDataException) { return null; }
            }
            var patchedCache = new Dictionary<string, PatchedMember>(StringComparer.Ordinal);
            PatchedMember Patched(MapMember m) { if (!patchedCache.TryGetValue(m.Path, out var x)) patchedCache[m.Path] = x = new PatchedMember(m, open); return x; }
            foreach (var m in t.Members.Where(x => x.Path.Contains("/inventory/")))
            {
                var b = Member(m); if (b == null) { notes.Add("The list " + m.Path + " is damaged in every file given, so the files it describes cannot be checked."); continue; }
                Dictionary<string, object> d; try { d = Json.Parse(new UTF8Encoding(false, true).GetString(b)); } catch (Exception) { notes.Add("The list " + m.Path + " is not readable."); continue; }
                var src = d.S("source");
                foreach (var r in (d.A("entries") ?? new List<object>()).OfType<Dictionary<string, object>>().Where(x => x.S("type") == "file"))
                {
                    var sf = new SalvageFile { Source = src, Path = r.S("path"), Size = r.L("size"), Sha256 = r.S("sha256") };
                    if (!StorageVerifier.SafePath(sf.Path)) continue;
                    if (r.S("state") != "ok") { sf.Status = "not-exportable"; sf.Trust = "it was not copied unchanged when the backup was made (" + (r.S("problem") ?? r.S("state")) + ")"; list.Add(sf); continue; }
                    var pm = t.Find(r.S("member")); long off = r.L("offset");
                    if (pm == null || off < 0 || off + sf.Size > pm.Size || pm.Method != 0) { sf.Status = "not-found"; sf.Trust = "its place in the backup is not recorded"; list.Add(sf); continue; }
                    sf.Copy = sink => { var s = Patched(pm); s.Position = off; var h = new HashingSink(sink, null, cancelled); Streams.Copy(s, h, sf.Size, cancelled); h.Finish(); return h.Sha256 == sf.Sha256; };
                    sf.Status = "check"; sf.Trust = "its SHA-256 recorded in the backup's list (" + m.Path + "), whose own checksum is in the backup's map";
                    list.Add(sf);
                }
            }
            foreach (var lm in t.Members.Where(x => x.Path.EndsWith("/index/layout.json", StringComparison.Ordinal)))
            {
                var src = ExternalFormat.SourceOf(lm.Path); var b = Member(lm);
                if (b == null) { notes.Add("The layout record of the drive image " + src + " is damaged, so its files cannot be read."); continue; }
                var lay = Json.Parse(new UTF8Encoding(false, true).GetString(b)); long R = lay.L("region_size"), bytes = lay.L("bytes");
                var regs = Enumerable.Range(1, (int)lay.L("regions")).Select(i => t.Find(ExternalFormat.Region(src, i))).ToList();
                if (regs.Any(x => x == null)) { notes.Add("The drive image " + src + " is missing region records."); continue; }
                PatchedRegions patched = null;
                foreach (var im in t.Members.Where(x => ExternalFormat.SourceOf(x.Path) == src && x.Path.Contains("/index/files-")))
                {
                    var ib = Member(im); if (ib == null) { notes.Add("The index " + im.Path + " is damaged in every file given."); continue; }
                    var d = Json.Parse(new UTF8Encoding(false, true).GetString(ib));
                    foreach (var r in (d.A("entries") ?? new List<object>()).OfType<Dictionary<string, object>>().Where(x => x.S("type") == "file"))
                    {
                        var sf = new SalvageFile { Source = src, Path = "/partition " + r.L("partition") + r.S("path"), Size = r.L("size"), Sha256 = r.S("sha256") };
                        if (!StorageVerifier.SafePath(r.S("path"))) continue;
                        if (r.S("problem") != null || sf.Sha256 == null) { sf.Status = "not-exportable"; sf.Trust = "it could not be read when the backup was made"; list.Add(sf); continue; }
                        var ext = (r.A("extents") ?? new List<object>()).Select(x => ((IEnumerable<object>)x).Select(Convert.ToInt64).ToArray()).ToList();
                        sf.Copy = sink =>
                        {
                            if (patched == null) patched = new PatchedRegions(regs, R, open);
                            {
                                var view = new RegionView(patched, R, regs.Select((x, i) => (long)i * R).ToList(), bytes);
                                var h = new HashingSink(sink, null, cancelled); var zero = new byte[65536];
                                foreach (var e in ext) { if (e[0] < 0) { for (long left = e[1]; left > 0;) { int n = (int)Math.Min(zero.Length, left); h.Write(zero, 0, n); left -= n; } } else { view.Position = e[0]; Streams.Copy(view, h, e[1], cancelled); } }
                                h.Finish(); return h.Sha256 == sf.Sha256;
                            }
                        };
                        sf.Status = "check"; sf.Trust = "its SHA-256 recorded in the backup's index (" + im.Path + "), read from the drive image's regions";
                        list.Add(sf);
                    }
                }
            }
            return (list, notes, open, t);
        }

        /// <summary>Exports every file that still matches its recorded SHA-256 into a NEW folder "&lt;backup&gt; (salvaged files &lt;date&gt;)" under
        /// outputParent (each written as .partial, checked while it is written, then renamed); never a backup. The files given stay unchanged.</summary>
        public static RepairResult Salvage(IList<string> copies, string outputParent, RepairOptions opt = null)
        {
            opt = opt ?? new RepairOptions();
            var rep = new RepairReport("storage-salvage", "Export what can still be trusted from a damaged USB and SD backup"); var res = new RepairResult { Report = rep };
            string folder = null;
            var (files, notes, open, t) = Survey(copies, opt.Cancelled);
            try
            {
                foreach (var n in notes) rep.Notes.Add(n);
                if (t.Problem != null) { rep.Lost("the whole backup", t.Problem); return Done("not-repaired", "Nothing can be exported: " + t.Problem); }
                if (string.IsNullOrEmpty(outputParent)) return Done("refused", "No folder was chosen for the exported files.");
                Directory.CreateDirectory(outputParent);
                var stem = System.IO.Path.GetFileNameWithoutExtension(copies[0]) + " (salvaged files " + DateTime.Now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture) + ")";
                for (int i = 1; i < 1000 && folder == null; i++) { var c = System.IO.Path.Combine(outputParent, stem + (i == 1 ? "" : " (" + i + ")")); if (!Directory.Exists(c) && !File.Exists(c)) folder = c; }
                Directory.CreateDirectory(folder); res.OutputPath = folder;
                long bytes = 0; int ok = 0;
                foreach (var f in files)
                {
                    if (opt.Cancelled()) throw new OperationCanceledException();
                    if (f.Copy == null) { rep.Lost(f.Source + f.Path, f.Status.Replace('-', ' ') + ": " + f.Trust); continue; }
                    var dest = System.IO.Path.Combine(new[] { folder, f.Source }.Concat(f.Path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Select(x => Words.Component(x))).ToArray());
                    if (!System.IO.Path.GetFullPath(dest).StartsWith(System.IO.Path.GetFullPath(folder) + "\\", StringComparison.OrdinalIgnoreCase)) { rep.Lost(f.Path, "its name would land outside the export folder"); continue; }
                    for (int n = 2; File.Exists(NativeFiles.Long(dest)) || File.Exists(NativeFiles.Long(dest + ".partial")); n++) dest = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dest), System.IO.Path.GetFileNameWithoutExtension(dest) + " (" + n + ")" + System.IO.Path.GetExtension(dest));
                    bool match; string ioProblem = null; var tmp = NativeFiles.Long(dest + ".partial");   // long paths: a deep folder of a drive stays exportable
                    try { Directory.CreateDirectory(NativeFiles.Long(System.IO.Path.GetDirectoryName(dest))); using (var o = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write)) { match = f.Copy(o); o.Flush(true); } }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { match = false; ioProblem = ex.Message; }
                    if (!match) { try { File.Delete(tmp); } catch (Exception) { } f.Status = ioProblem != null ? "not-written" : "damaged"; rep.Lost(f.Source + f.Path, ioProblem != null ? "it could not be written to the export folder (" + ioProblem.TrimEnd('.') + ")" : "its bytes in the files given do not match the checksum its list recorded"); continue; }
                    Durable.Publish(tmp, NativeFiles.Long(dest)); f.ExportedTo = dest; f.Status = "verified"; ok++; bytes += f.Size;
                    rep.Region(f.Source + f.Path, "the backup", 0, f.Size, f.Trust);
                }
                rep.Notes.Add("This folder is not a backup and cannot be restored as one. The backup file is unchanged and is still damaged.");
                rep.Output = new JObj { { "path", folder }, { "bytes", bytes }, { "items", (long)ok }, { "checked", "Every exported file matched its recorded SHA-256 when it was written." } };
                return Done("salvaged", ok + " file(s) (" + Units.Gb(bytes) + ") were exported to " + System.IO.Path.GetFileName(folder) + ". " + rep.Unrecoverable.Count + " could not be recovered. The backup itself stays damaged.");
            }
            catch (OperationCanceledException) { rep.Canceled = true; if (folder != null) try { Directory.Delete(folder, true); } catch (IOException) { } res.OutputPath = null; return Done("canceled", "Canceled. The unfinished folder was removed; the backup is unchanged."); }
            finally { foreach (var f in open) f.Dispose(); }
            RepairResult Done(string outcome, string summary)
            {
                rep.Outcome = res.Outcome = outcome; rep.Summary = summary; rep.FinishedUtc = DateTime.UtcNow;
                if (opt.SaveReport && outcome == "salvaged") try { (res.ReportJson, res.ReportText) = rep.Save(folder, "Salvage report"); } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { rep.Errors.Add(ex.Message); }
                return res;
            }
        }

        /// <summary>a member read as one stream from the files given: each piece from a file where it matches its recorded checksum, else from
        /// the first file as it is (only a recorded per-file checksum decides whether anything read through it is trusted)</summary>
        sealed class PatchedMember : Stream
        {
            readonly MapMember m; readonly IList<StorageFile> files; readonly (StorageFile f, long off)[] at; long pos;
            public PatchedMember(MapMember m, IList<StorageFile> files)
            {
                this.m = m; this.files = files; at = new (StorageFile, long)[m.Chunks.Count];
                for (int i = 0; i < at.Length; i++)
                {
                    long off = m.DataOffset + (long)i * ExternalFormat.ChunkSize; int len = (int)Math.Min(ExternalFormat.ChunkSize, m.CompressedSize - (long)i * ExternalFormat.ChunkSize);
                    var hit = files.FirstOrDefault(f => { var b = f.ReadUpTo(off, len); if (b.Length != len) return false; using (var h = FastHash.Sha256()) return Hex.Of(h.ComputeHash(b)) == m.Chunks[i]; });
                    at[i] = (hit ?? files[0], off);
                }
            }
            public override int Read(byte[] b, int o, int c)
            {
                if (pos >= m.Size || c <= 0) return 0;
                int k = (int)(pos / ExternalFormat.ChunkSize); long within = pos % ExternalFormat.ChunkSize;
                int n = (int)Math.Min(c, Math.Min(ExternalFormat.ChunkSize - within, m.Size - pos));
                var x = at[k]; var got = x.f.ReadUpTo(x.off + within, n); Buffer.BlockCopy(got, 0, b, o, got.Length); if (got.Length < n) Array.Clear(b, o + got.Length, n - got.Length);
                pos += n; return n;
            }
            public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
            public override long Length => m.Size;
            public override long Position { get => pos; set { if (value < 0 || value > m.Size) throw new IOException("outside the member"); pos = value; } }
            public override long Seek(long o, SeekOrigin so) { Position = so == SeekOrigin.Begin ? o : so == SeekOrigin.Current ? pos + o : m.Size + o; return pos; }
            public override void Flush() { }
            public override void SetLength(long v) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        }

        /// <summary>the regions of a damaged image as one "file" whose region k starts at k*R (for RegionView), each read through PatchedMember</summary>
        sealed class PatchedRegions : Stream
        {
            readonly PatchedMember[] regs; readonly long R; long pos;
            public PatchedRegions(IList<MapMember> regions, long regionSize, IList<StorageFile> files) { regs = regions.Select(x => new PatchedMember(x, files)).ToArray(); R = regionSize; }
            public override int Read(byte[] b, int o, int c)
            {
                int k = (int)(pos / R); if (k >= regs.Length) return 0;
                regs[k].Position = pos % R; int n = regs[k].Read(b, o, (int)Math.Min(c, R - pos % R)); pos += n; return n;
            }
            public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
            public override long Length => (long)regs.Length * R;
            public override long Position { get => pos; set => pos = value; }
            public override long Seek(long o, SeekOrigin so) { pos = so == SeekOrigin.Begin ? o : so == SeekOrigin.Current ? pos + o : Length + o; return pos; }
            public override void Flush() { }
            public override void SetLength(long v) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        }
    }
}
