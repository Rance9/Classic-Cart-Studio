// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Checks a closed storage backup file from scratch, the same way every time it is used (right after it is made, when it is opened, before
// a restore, a second copy or recovery data, and at every scheduled check). Nothing is taken on trust from the file, its name or a record:
//  1. the strict ZIP structure (Archive/ZipReader with this format's member names and limits); README first, the manifest last;
//  2. the manifest (format, schema, its sources and maps), every map (its own check, the same backup, its copy byte for byte the same) and
//     every member against its map record: place, exact local header, method, CRC-32 and sizes; every member except the maps and the
//     manifest recorded in exactly one map, and the list of their names equal to the manifest's checksum of it;
//  3. the lists: every inventory entry (files mode) with a safe path and a place inside its own source's payload; every index entry (image
//     mode) with extents inside the drive; the regions tiling the drive exactly; a source said to be complete has no entry that is not;
//  4. one pass over the whole file, in order: every member's CRC-32, SHA-256, MD5 and the SHA-256 of each 1,048,576-byte piece against its
//     record (and, right after a capture, against what was computed while it was written), and every file of a files-mode source against
//     the SHA-256 its inventory recorded (read from its place in its pack); the whole file's SHA-256 in the same pass.
// A file of an image source is checked through its region's checksums here (its own SHA-256, worked out from the stored image when the
// backup was made, is checked again by every export and restore). The result lists every problem; Ok only with none.
using CcsBackup.Archive;
using CcsBackup.Repair;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CcsBackup.External
{
    public sealed class StorageVerification
    {
        public readonly List<string> Problems = new List<string>();
        public bool Ok => Problems.Count == 0;
        public Dictionary<string, object> Manifest; public string ArchiveSha256, BackupId; public long Bytes; public bool ScopeComplete;
        public readonly Dictionary<string, (string sha256, string md5, long size)> Members = new Dictionary<string, (string, string, long)>(StringComparer.Ordinal);
        public readonly Dictionary<string, MapMember> Records = new Dictionary<string, MapMember>(StringComparer.Ordinal);
        public readonly Dictionary<string, List<Dictionary<string, object>>> Inventory = new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.Ordinal);
        public readonly Dictionary<string, List<Dictionary<string, object>>> Index = new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.Ordinal);
        public readonly Dictionary<string, Dictionary<string, object>> Layouts = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        public List<ZipEntryInfo> Entries = new List<ZipEntryInfo>();
        public long FilesChecked;
        public void Fail(string p) { if (Problems.Count < 200 && !Problems.Contains(p)) Problems.Add(p); }
        public List<Dictionary<string, object>> SourceList => (Manifest?.A("sources") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();

        /// <summary>the same result in the console backup's terms (for the shared second copy and recovery data steps): never restore-ready</summary>
        public Verification AsVerification()
        {
            var v = new Verification { Manifest = Manifest, ArchiveSha256 = ArchiveSha256, Bytes = Bytes };
            foreach (var p in Problems) v.Fail(p);
            foreach (var kv in Members) v.Members[kv.Key] = kv.Value;
            return v;
        }
    }

    public static class StorageVerifier
    {
        sealed class Content
        {
            public readonly SHA256 Sha = FastHash.Sha256(); public readonly MD5 Md5 = MD5.Create(); public readonly Crc32 Crc = new Crc32();
            public void Write(byte[] b, int o, int c) { Sha.TransformBlock(b, o, c, null, 0); Md5.TransformBlock(b, o, c, null, 0); Crc.Update(b, o, c); }
            public (string sha, string md5, uint crc) Finish() { Sha.TransformFinalBlock(new byte[0], 0, 0); Md5.TransformFinalBlock(new byte[0], 0, 0); var r = (Hex.Of(Sha.Hash), Hex.Of(Md5.Hash), Crc.Value); Sha.Dispose(); Md5.Dispose(); return r; }
        }
        sealed class Span { public long From, To; public string Want, Path; public SHA256 H; }
        sealed class Run { public ZipEntryInfo E; public ChunkHasher Pieces; public Content C; public MemoryStream Raw; public long Fed; public List<Span> Files; public int Next; }

        public static StorageVerification Verify(string path, IDictionary<string, string> expected = null, Func<bool> cancelled = null, Action<long, long> progress = null)
        {
            var v = new StorageVerification();
            if (!File.Exists(path)) { v.Fail("The backup file was not found."); return v; }
            try
            {
                using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
                {
                    ZipReader z;
                    try { z = new ZipReader(f, ExternalFormat.Allowed, false, ExternalFormat.Limits); }
                    catch (ZipFormatException ex) { v.Fail("The file is not a valid USB and SD backup: " + ex.Message); return v; }
                    var order = z.Entries.OrderBy(e => e.HeaderOffset).ToList(); v.Entries = order;
                    var by = order.ToDictionary(e => e.Name, StringComparer.Ordinal);
                    foreach (var e in order) if (e.Size > ExternalFormat.MaxSize(e.Name)) { v.Fail("Member " + e.Name + " is larger than this format allows."); return v; }
                    if (order.Count < 3 || order[0].Name != ExternalFormat.ReadmeName || order[order.Count - 1].Name != ExternalFormat.ManifestName) { v.Fail("The file does not start with its README and end with its manifest, so it was not finished the way Workbench finishes it."); return v; }
                    byte[] Small(ZipEntryInfo e) { using (var s = z.OpenMember(e)) { var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); } }
                    // 2. the manifest and the maps
                    Dictionary<string, object> man;
                    try { man = Json.Parse(new UTF8Encoding(false, true).GetString(Small(by[ExternalFormat.ManifestName]))); }
                    catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is FormatException || ex is ArgumentException || ex is DecoderFallbackException) { v.Fail("The backup's manifest is not readable: " + ex.Message); return v; }
                    v.Manifest = man; v.BackupId = man.S("backup_id");
                    if (man.S("format") != ExternalFormat.Format || man.S("schema") != ExternalFormat.Schema) { v.Fail("This file is not a USB and SD backup this version reads (format or schema)."); return v; }
                    if (v.BackupId == null) { v.Fail("The manifest does not name the backup."); return v; }
                    var sources = v.SourceList; var ids = new HashSet<string>(StringComparer.Ordinal);
                    if (sources.Count < 1 || sources.Count > ExternalFormat.MaxSources) { v.Fail("The manifest lists no source, or more than a backup can hold."); return v; }
                    for (int i = 0; i < sources.Count; i++) if (sources[i].S("id") != ExternalFormat.SourceId(i + 1) || (sources[i].S("mode") != "files" && sources[i].S("mode") != "image")) { v.Fail("The manifest's list of sources is not valid."); return v; }
                    foreach (var x in sources) ids.Add(x.S("id"));
                    var listed = (man.A("maps") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
                    var mapBytes = new Dictionary<int, byte[]>();
                    foreach (var l in listed)
                    {
                        var p = l.S("path");
                        if (!ExternalFormat.IsMap(p) || !by.TryGetValue(p, out var me)) { v.Fail("The manifest lists a map that is missing from the file (" + p + ")."); continue; }
                        if (me.HeaderOffset != l.L("header_offset") || me.Size != l.L("size") || me.Method != 0 || me.Crc.ToString("x8") != l.S("crc32")) { v.Fail("A map is not where, or not what, the manifest says (" + p + ")."); continue; }
                        byte[] b; try { b = Small(me); } catch (Exception ex) when (ex is IOException || ex is InvalidDataException) { v.Fail("Map " + p + " could not be read: " + ex.Message); continue; }
                        var m = StorageMap.Parse(b, out var why);
                        if (m == null) { v.Fail("Map " + p + " is not valid: " + why + "."); continue; }
                        if (m.BackupId != v.BackupId || m.Index != (int)l.L("map") || l.B("copy") != ExternalFormat.IsMapCopy(p) || ExternalFormat.MapName(m.Index, l.B("copy")) != p) { v.Fail("Map " + p + " belongs to another backup or place."); continue; }
                        if (mapBytes.TryGetValue(m.Index, out var other)) { if (!other.SequenceEqual(b)) v.Fail("Map " + m.Index + " and its copy differ."); continue; }
                        mapBytes[m.Index] = b;
                        foreach (var mm in m.Members)
                        {
                            if (v.Records.ContainsKey(mm.Path)) { v.Fail("Member " + mm.Path + " is recorded in two maps."); continue; }
                            v.Records[mm.Path] = mm;
                        }
                    }
                    foreach (var e in order.Where(x => ExternalFormat.IsMap(x.Name))) if (!listed.Any(l => l.S("path") == e.Name)) v.Fail("Map " + e.Name + " is not listed in the manifest.");
                    if (mapBytes.Count != listed.Select(l => l.L("map")).Distinct().Count() || listed.Count != 2 * mapBytes.Count) v.Fail("The manifest does not list every map exactly once with its copy.");
                    if (!v.Ok) return v;
                    var content = order.Where(e => !ExternalFormat.IsMap(e.Name) && e.Name != ExternalFormat.ManifestName).ToList();
                    foreach (var e in content)
                    {
                        if (!v.Records.TryGetValue(e.Name, out var r)) { v.Fail("Member " + e.Name + " is not recorded in any map."); continue; }
                        if (r.HeaderOffset != e.HeaderOffset || r.DataOffset != e.DataOffset || r.Method != e.Method || r.Crc != e.Crc || r.Size != e.Size || r.CompressedSize != e.CompressedSize) { v.Fail("Member " + e.Name + " is not where, or not what, its map says."); continue; }
                        if (Hex.Of(z.LocalHeaderBytes(e)) != r.LocalHeader) v.Fail("Member " + e.Name + "'s header differs from its map's record.");
                        var src = ExternalFormat.SourceOf(e.Name); if (src != null && !ids.Contains(src)) v.Fail("Member " + e.Name + " belongs to a source the manifest does not list.");
                    }
                    foreach (var r in v.Records.Keys) if (!by.ContainsKey(r)) v.Fail("Member " + r + " is recorded in a map but missing from the file.");
                    using (var h = SHA256.Create()) if (Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", content.Select(e => e.Name))))) != man.S("member_names_sha256") || content.Count != man.L("members")) v.Fail("The file's list of members differs from the one its manifest recorded.");
                    if (!v.Ok) return v;
                    // 3. the lists
                    var spans = new Dictionary<string, List<Span>>(StringComparer.Ordinal);
                    bool scopeAll = true;
                    foreach (var s in sources)
                    {
                        var id = s.S("id"); var mode = s.S("mode"); bool claimsComplete = s.B("complete"); bool anyNotOk = false;
                        if (mode == "files")
                        {
                            var rows = new List<Dictionary<string, object>>();
                            foreach (var e in content.Where(x => ExternalFormat.SourceOf(x.Name) == id && x.Name.Contains("/inventory/")))
                            {
                                Dictionary<string, object> d;
                                try { d = Json.Parse(new UTF8Encoding(false, true).GetString(Small(e))); } catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is FormatException || ex is ArgumentException || ex is DecoderFallbackException) { v.Fail("The list " + e.Name + " is not readable."); continue; }
                                if (d.S("schema") != "ccs-storage-inventory/1" || d.S("source") != id) { v.Fail("The list " + e.Name + " is not this source's."); continue; }
                                rows.AddRange((d.A("entries") ?? new List<object>()).OfType<Dictionary<string, object>>());
                            }
                            var paths = new HashSet<string>(StringComparer.Ordinal);
                            foreach (var r in rows)
                            {
                                var p = r.S("path"); var type = r.S("type");
                                if (!SafePath(p) || !paths.Add(p) || (type != "file" && type != "folder" && type != "link")) { v.Fail("A list of " + id + " holds an entry with a path or type that is not allowed (" + Shown(p) + ")."); continue; }
                                if (type != "file") continue;
                                var st = r.S("state"); if (st != "ok") anyNotOk = true;
                                var mem = r.S("member"); long off = r.L("offset"), size = r.L("size");
                                if (st != "ok" && st != "changed" && st != "unreadable") { v.Fail("A file's state is not valid (" + Shown(p) + ")."); continue; }
                                if (mem == null || ExternalFormat.SourceOf(mem) != id || !mem.Contains("/files/") || !by.TryGetValue(mem, out var pe) || off < 0 || size < 0 || off + size > pe.Size || mem.Contains("/file-") && (off != 0 || size != pe.Size)) { v.Fail("A file's place in the backup is not valid (" + Shown(p) + ")."); continue; }
                                var want = r.S("sha256");
                                if (st != "unreadable" && !Hex.IsHex(want, 64)) { v.Fail("A file has no valid checksum (" + Shown(p) + ")."); continue; }
                                if (st == "unreadable") continue;
                                if (!spans.TryGetValue(mem, out var l)) spans[mem] = l = new List<Span>();
                                l.Add(new Span { From = pe.DataOffset + off, To = pe.DataOffset + off + size, Want = want, Path = p });
                            }
                            v.Inventory[id] = rows;
                        }
                        else
                        {
                            if (!by.TryGetValue(ExternalFormat.Layout(id), out var le)) { v.Fail("The drive image " + id + " has no layout record."); continue; }
                            Dictionary<string, object> lay;
                            try { lay = Json.Parse(new UTF8Encoding(false, true).GetString(Small(le))); } catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is FormatException || ex is ArgumentException || ex is DecoderFallbackException) { v.Fail("The layout record of " + id + " is not readable."); continue; }
                            v.Layouts[id] = lay;
                            long bytes = lay.L("bytes"), R = lay.L("region_size"), n = lay.L("regions");
                            var regs = content.Where(x => ExternalFormat.SourceOf(x.Name) == id && x.Name.Contains("/image/")).ToList();
                            bool tiled = R >= ExternalFormat.RegionSize && n == regs.Count && n == (bytes + R - 1) / R && bytes > 0;
                            for (int i = 0; tiled && i < regs.Count; i++) tiled = regs[i].Name == ExternalFormat.Region(id, i + 1) && regs[i].Size == Math.Min(R, bytes - i * R);
                            if (!tiled) { v.Fail("The regions of the drive image " + id + " do not cover the drive exactly."); continue; }
                            var rows = new List<Dictionary<string, object>>();
                            foreach (var e in content.Where(x => ExternalFormat.SourceOf(x.Name) == id && x.Name.Contains("/index/files-")))
                            {
                                Dictionary<string, object> d;
                                try { d = Json.Parse(new UTF8Encoding(false, true).GetString(Small(e))); } catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is FormatException || ex is ArgumentException || ex is DecoderFallbackException) { v.Fail("The index " + e.Name + " is not readable."); continue; }
                                if (d.S("schema") != "ccs-storage-index/1" || d.S("source") != id) { v.Fail("The index " + e.Name + " is not this source's."); continue; }
                                rows.AddRange((d.A("entries") ?? new List<object>()).OfType<Dictionary<string, object>>());
                            }
                            foreach (var r in rows)
                            {
                                if (!SafePath(r.S("path"))) { v.Fail("The index of " + id + " holds a path that is not allowed (" + Shown(r.S("path")) + ")."); continue; }
                                if (r.S("type") != "file" || r.S("problem") != null) continue;
                                long size = r.L("size"), sum = 0;
                                foreach (var x in r.A("extents") ?? new List<object>())
                                {
                                    var pr = (x as IEnumerable<object>)?.ToList() ?? (x as object[])?.ToList();
                                    if (pr == null || pr.Count != 2) { sum = -1; break; }
                                    long off = Convert.ToInt64(pr[0]), elen = Convert.ToInt64(pr[1]);
                                    if (elen <= 0 || off < -1 || off >= 0 && off + elen > bytes) { sum = -1; break; }
                                    sum += elen;
                                }
                                if (sum != size || !Hex.IsHex(r.S("sha256"), 64)) v.Fail("A file in the index of " + id + " has no valid place or checksum (" + Shown(r.S("path")) + ").");
                            }
                            v.Index[id] = rows;
                            if (s.O("summary")?.S("changed") != null) anyNotOk = true;
                        }
                        if (claimsComplete && anyNotOk) v.Fail("The manifest calls source " + id + " complete, but its lists say otherwise.");
                        scopeAll &= claimsComplete;
                    }
                    v.ScopeComplete = scopeAll && man.O("scope")?.B("complete") == true;
                    if (man.O("scope")?.B("complete") != scopeAll) v.Fail("The manifest's own summary of what is complete does not match its sources.");
                    if (!v.Ok) return v;
                    // 4. one pass over the whole file
                    var runs = order.Select(e => new Run { E = e, Pieces = new ChunkHasher(ExternalFormat.ChunkSize), C = e.Method == 0 ? new Content() : null, Raw = e.Method == 0 ? null : new MemoryStream(),
                        Files = spans.TryGetValue(e.Name, out var sl) ? sl.OrderBy(x => x.From).ToList() : null }).ToList();
                    var whole = FastHash.Sha256(); var buf = new byte[Streams.Buffer]; long pos = 0, len = f.Length; int k = 0;
                    f.Position = 0;
                    while (pos < len)
                    {
                        if (cancelled != null && cancelled()) throw new OperationCanceledException();
                        int n = f.Read(buf, 0, (int)Math.Min(buf.Length, len - pos)); if (n <= 0) throw new IOException("the file ended early while it was read");
                        whole.TransformBlock(buf, 0, n, null, 0);
                        long end = pos + n;
                        while (k < runs.Count && runs[k].E.DataOffset + runs[k].E.CompressedSize <= pos) k++;
                        for (int j = k; j < runs.Count && runs[j].E.DataOffset < end; j++)
                        {
                            var g = runs[j]; long a = Math.Max(pos, g.E.DataOffset), b = Math.Min(end, g.E.DataOffset + g.E.CompressedSize);
                            if (a >= b) continue;
                            int o = (int)(a - pos), c = (int)(b - a);
                            g.Pieces.Write(buf, o, c); g.C?.Write(buf, o, c); g.Raw?.Write(buf, o, c); g.Fed += c;
                            if (g.Files != null)
                            {
                                while (g.Next < g.Files.Count && g.Files[g.Next].To <= a) { Close(g.Files[g.Next], v); g.Next++; }
                                for (int q = g.Next; q < g.Files.Count && g.Files[q].From < b; q++)
                                {
                                    var sp = g.Files[q]; long fa = Math.Max(a, sp.From), fb = Math.Min(b, sp.To); if (fa >= fb) continue;
                                    if (sp.H == null) sp.H = FastHash.Sha256();
                                    sp.H.TransformBlock(buf, (int)(fa - pos), (int)(fb - fa), null, 0);
                                }
                            }
                        }
                        pos = end; progress?.Invoke(pos, len);
                    }
                    whole.TransformFinalBlock(new byte[0], 0, 0); v.ArchiveSha256 = Hex.Of(whole.Hash); v.Bytes = len; whole.Dispose();
                    foreach (var g in runs)
                    {
                        var e = g.E; var pieces = g.Pieces.Finish();
                        if (g.Files != null) for (; g.Next < g.Files.Count; g.Next++) Close(g.Files[g.Next], v);
                        string sha, md5; uint crc;
                        if (g.C != null) (sha, md5, crc) = g.C.Finish();
                        else
                        {
                            try { using (var d = new DeflateStream(new MemoryStream(g.Raw.ToArray()), CompressionMode.Decompress)) { var c = new Content(); var b2 = new byte[1 << 16]; int r; long total = 0; while ((r = d.Read(b2, 0, b2.Length)) > 0) { c.Write(b2, 0, r); total += r; if (total > ExternalFormat.MaxSize(e.Name)) break; } (sha, md5, crc) = c.Finish(); } }
                            catch (InvalidDataException) { v.Fail("Member " + e.Name + " cannot be unpacked (it is damaged)."); continue; }
                        }
                        v.Members[e.Name] = (sha, md5, e.Size);
                        if (crc != e.Crc) { v.Fail("Member " + e.Name + " fails its CRC-32 check: the file has changed or is damaged."); continue; }
                        if (v.Records.TryGetValue(e.Name, out var r0))
                        {
                            if (sha != r0.Sha256 || md5 != r0.Md5) v.Fail("Member " + e.Name + " does not match its checksum: the file has changed or is damaged.");
                            else if (!pieces.SequenceEqual(r0.Chunks)) v.Fail("A piece of member " + e.Name + " does not match its recorded checksum.");
                        }
                        if (expected != null && expected.TryGetValue(e.Name, out var x) && x != sha) v.Fail("Member " + e.Name + " differs from what was written while the backup was made.");
                    }
                    var ml = listed.ToDictionary(l => l.S("path"), StringComparer.Ordinal);
                    foreach (var kv in ml) if (v.Members.TryGetValue(kv.Key, out var got) && (got.sha256 != kv.Value.S("sha256") || got.md5 != kv.Value.S("md5"))) v.Fail("Map " + kv.Key + " does not match the checksum its manifest recorded.");
                    if (expected != null) foreach (var key in expected.Keys) if (!v.Members.ContainsKey(key)) v.Fail("Member " + key + " that was written is missing from the file.");
                }
            }
            catch (FormatException ex) { v.Fail("The backup's lists are not readable: " + ex.Message); }
            catch (IOException ex) { v.Fail("The backup file could not be read: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { v.Fail("The backup file could not be opened: " + ex.Message); }
            return v;
        }

        static void Close(Span sp, StorageVerification v)
        {
            if (sp.H == null && sp.To > sp.From) return;   // not reached (a short file was already reported)
            var h = sp.H ?? FastHash.Sha256(); h.TransformFinalBlock(new byte[0], 0, 0); var got = Hex.Of(h.Hash); h.Dispose(); sp.H = null;
            v.FilesChecked++;
            if (got != sp.Want) v.Fail("The file " + Shown(sp.Path) + " does not match the checksum its list recorded.");
            sp.To = long.MinValue;   // closed
        }

        /// <summary>a path as the lists keep it: "/" then names separated by "/", none empty, "." or "..", no control characters, no "\"</summary>
        public static bool SafePath(string p)
        {
            if (p == null || p.Length < 2 || p[0] != '/' || p.EndsWith("/") || p.Length > 32767) return false;
            foreach (var seg in p.Substring(1).Split('/')) if (seg.Length == 0 || seg == "." || seg == ".." || seg.Any(ch => ch < 0x20 || ch == '\\')) return false;
            return true;
        }

        static string Shown(string p) => p == null ? "no path" : new string(p.Select(c => c < 0x20 ? '?' : c).ToArray());
    }
}
