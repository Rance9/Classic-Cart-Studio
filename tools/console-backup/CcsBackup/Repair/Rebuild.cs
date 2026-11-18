// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The one way a repair makes a new backup file (ZipIndexRecovery, Merge, Donor): every byte of the new file comes from a place whose
// recorded checksum it matches, or from the backup's own records, and the result must pass the full check (Verifier) before it gets its
// name. Nothing is ever written to a file that was read.
//   Plan    for each part of the backup, in its recorded order: its header (the exact bytes recorded in the repair map; else the header of
//           a copy whose content verifies; else rebuilt by the writer's rule, said so), then its bytes, piece by piece (1,048,576-byte
//           pieces, each from the first file whose piece matches the recorded piece checksum: the damaged file, a copy of the same backup,
//           another backup of the same console only where its piece is byte-identical by checksum), or whole (a part whose checksum,
//           size and stored form match exactly). The early descriptor, when it is damaged in every file, is rebuilt from the chunk map and
//           the manifest and used only when its SHA-256 equals the one the manifest recorded for it (then it is the original bytes). Then
//           the manifest, as it is in a copy where it is intact (it is never rebuilt: its bytes are recorded nowhere else). Anything
//           without such a source is unrecoverable, and then nothing is written at all.
//   Write   <name> (repaired <date>).zip.partial, created new; every piece re-hashed as it is copied (a source that changed since it was
//           checked stops the repair); the table of contents written again by the writer's own rule (ZipWriter.WriteDirectory); flushed.
//   Check   Verifier.VerifyFile from scratch, with every part's recorded SHA-256 as the expected value; then the whole file's SHA-256
//           compared with the original file's recorded one when it is known (byte-for-byte identical, or a new container, said exactly).
//   Publish renamed to .zip, never over a file. Cancel or any failure: the .partial file is deleted; no partial output is left.
using CcsBackup.Archive;
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace CcsBackup.Repair
{
    public sealed class RepairOptions
    {
        public Func<bool> Cancelled = () => false;
        public Action<EngineProgress> Progress = _ => { };
        /// <summary>the original file's SHA-256 and size as recorded when it was made (RecordedIdentity.Find): only to say whether the new
        /// file is byte-for-byte that file. A record never stands in for checking anything.</summary>
        public string RecordedSha256; public long RecordedBytes = -1;
        /// <summary>save the report (JSON and text) next to what was made; a canceled run saves nothing</summary>
        public bool SaveReport = true;
        internal void Say(string phase, string text, long done = 0, long total = 0) => Progress?.Invoke(new EngineProgress { Phase = phase, Text = text, Done = done, Total = total });
    }

    public sealed class RepairResult
    {
        /// <summary>repaired | copied | salvaged | not-needed | not-repaired | refused | canceled | failed</summary>
        public string Outcome;
        public string OutputPath, ReportJson, ReportText; public RepairReport Report; public Verification OutputCheck;
        /// <summary>a complete backup file that passed the full check was made (a repaired backup or a verified copy); never a salvage</summary>
        public bool RestorePoint;
        /// <summary>second copy only: same | different | unknown (the physical drive of the copy and of the backup)</summary>
        public string SameDrive;
        public string Text => Report?.Summary;
    }

    public static class RecordedIdentity
    {
        /// <summary>the SHA-256 and size the backup's record (in the records folder) says its file had when it was made; null when there is
        /// no readable record. History, never proof: used only to compare a repaired file with.</summary>
        public static (string sha256, long bytes)? Find(Settings s, string backupId)
        {
            if (s == null || string.IsNullOrEmpty(backupId) || backupId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            var d = Record.Load(Path.Combine(s.RecordsFolder, "backup-" + backupId + ".json"));
            var p = d?.O("primary"); var sha = p?.S("sha256"); long b = p?.L("bytes") ?? -1;
            if (d?.S("id") != backupId || !Hex.IsHex(sha, 64) || b <= 0) return null;
            return (sha, b);
        }
    }

    internal sealed class Piece
    {
        public BackupFile From; public long Offset, Length; public string Expect; public byte[] Literal; public string Member, How; public int Index = -1;
    }

    internal sealed class RebuildPlan
    {
        public readonly List<Piece> Pieces = new List<Piece>(); public readonly List<ZipEntryInfo> Entries = new List<ZipEntryInfo>();
        public readonly List<string> RebuiltHeaders = new List<string>(), RecordedHeaders = new List<string>(), Regenerated = new List<string>();
        public long Length; public bool Complete = true;
    }

    internal static class Rebuild
    {
        static ushort U16(byte[] b, int o) => (ushort)(b[o] | b[o + 1] << 8);
        static uint U32(byte[] b, int o) => (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24);
        static long U64(byte[] b, int o) => (long)U32(b, o) | (long)U32(b, o + 4) << 32;
        internal static byte[] FromHex(string h) { var b = new byte[h.Length / 2]; for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(h.Substring(2 * i, 2), 16); return b; }
        static string N(long x) => x.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>a header this repair will write, read back into an index entry (its sizes from the ZIP64 field when it has one)</summary>
        static ZipEntryInfo EntryOf(string name, byte[] h, long at)
        {
            long cs = U32(h, 18), us = U32(h, 22); int nl = U16(h, 26), xl = U16(h, 28); bool z64 = false;
            for (int p = 30 + nl; p + 4 <= 30 + nl + xl;) { int tag = U16(h, p), sz = U16(h, p + 2); if (tag == 1 && sz >= 16 && (cs == 0xFFFFFFFF || us == 0xFFFFFFFF)) { us = U64(h, p + 4); cs = U64(h, p + 12); z64 = true; } p += 4 + sz; }
            return new ZipEntryInfo { Name = name, Method = U16(h, 8), Flags = U16(h, 6), Crc = U32(h, 14), Size = us, CompressedSize = cs, HeaderOffset = at, DataOffset = at + h.Length, ExternalAttributes = ZipWriter.UnixRegular0644, MadeBy = ZipWriter.WriterMadeBy, Zip64Local = z64 };
        }

        /// <summary>every checked member of every file, for parts kept twice (the original kernel) or unchanged in another backup</summary>
        static IEnumerable<(BackupFile f, MemberCheck c)> All(IList<BackupFile> files) => files.SelectMany(f => f.Checks.Values.Where(c => c.At != null).Select(c => (f, c)));

        internal static RebuildPlan Plan(TrustedMap map, IList<BackupFile> copies, IList<BackupFile> donors, RepairReport rep, Func<BackupFile, string> source)
        {
            var plan = new RebuildPlan(); int C = RepairMap.ChunkSize; long pos = 0;
            var everyone = copies.Concat(donors).ToList();
            void Lost(string what, string why, long a = -1, long b = -1) { plan.Complete = false; rep.Lost(what, why, a, b); }
            if (map.Manifest == null) Lost("the manifest (the backup's list of contents, the last part of the file)", "it is damaged or missing in every file given, and its exact bytes are recorded nowhere else, so a complete backup cannot be made; Salvage can still export the parts and files that verify");
            foreach (var r in map.Members)
            {
                // the early descriptor damaged in every copy: its bytes follow from the other records, and the manifest's SHA-256 proves them
                if (r.Path == BackupFormat.DescriptorName && !copies.Any(f => f.Check(r.Path) is MemberCheck dc && dc.Verified && dc.HeaderOk))
                {
                    var g = RegenerateDescriptor(map, everyone);
                    if (g != null)
                    {
                        plan.Entries.Add(EntryOf(r.Path, g.Value.header, pos));
                        plan.Pieces.Add(new Piece { Literal = g.Value.header, Member = r.Path, How = "rebuilt from the backup's other records" });
                        plan.Pieces.Add(new Piece { Literal = g.Value.data, Member = r.Path, How = "rebuilt from the backup's other records" });
                        plan.Regenerated.Add(r.Path); pos += g.Value.header.Length + g.Value.data.Length;
                        continue;
                    }
                }
                // the header
                byte[] header = null; string hhow = null;
                if (r.Map != null) { header = FromHex(r.Map.LocalHeader); hhow = "the exact header recorded in the backup's repair map"; plan.RecordedHeaders.Add(r.Path); }
                else
                {
                    var own = copies.Select(f => (f, c: f.Check(r.Path))).FirstOrDefault(x => x.c != null && x.c.Verified && x.c.HeaderOk);
                    if (own.c != null) { header = own.c.At.Header; hhow = "its header in " + source(own.f) + ", which agrees with its verified content"; }
                    else
                    {
                        var any = All(everyone).FirstOrDefault(x => x.c.Verified && x.c.Sha256 == r.Sha256 && x.c.Md5 == r.Md5);
                        if (any.c != null)
                        {
                            header = ZipWriter.LocalHeader(new ZipEntryInfo { Name = r.Path, Method = (ushort)any.c.At.Method, Crc = any.c.Crc, Size = any.c.At.Size, CompressedSize = any.c.At.CompressedSize, Flags = 0x0800, Zip64Local = map.Zip64 }, map.DosTime, map.DosDate);
                            hhow = "rebuilt by Classic Cart Studio's ZIP rules from its verified content (the original header bytes are not recorded anywhere)"; plan.RebuiltHeaders.Add(r.Path);
                        }
                    }
                }
                if (header == null)
                {
                    Lost(r.Path, "no file given holds this part with its recorded checksum (SHA-256 " + r.Sha256 + ")");
                    pos += r.Map != null ? r.Map.DataEnd - r.Map.HeaderOffset : Math.Max(0, r.Size);
                    continue;
                }
                var e = EntryOf(r.Path, header, pos);
                if (r.Map != null && (r.Map.HeaderOffset != pos || e.CompressedSize != r.Map.CompressedSize) && plan.Complete)
                    Lost(r.Path, "its recorded place in the file (offset " + N(r.Map.HeaderOffset) + ") cannot be reproduced");
                if (r.Size >= 0 && e.Size != r.Size) Lost(r.Path, "its header and its recorded size disagree");
                plan.Entries.Add(e);
                plan.Pieces.Add(new Piece { Literal = header, Member = r.Path, How = hhow });
                // the bytes
                var chunks = r.Map?.Chunks;
                if (chunks != null && (chunks.Count > 0 || e.CompressedSize == 0))
                {
                    for (int i = 0; i < chunks.Count; i++)
                    {
                        long off = (long)i * C, len = Math.Min(C, e.CompressedSize - off);
                        Piece p = null;
                        foreach (var f in copies) { var c = f.Check(r.Path); if (c?.PieceOk != null && i < c.PieceOk.Length && c.PieceOk[i]) { p = new Piece { From = f, Offset = c.At.DataOffset + off }; break; } }
                        if (p == null)
                            foreach (var (f, c) in All(everyone))
                            {
                                // the same piece of the same part in another file whose bytes there hash to this part's recorded piece checksum, or
                                // a part kept with exactly this content elsewhere (same checksum, size and stored form)
                                bool samePart = c.Path == r.Path && c.At.Method == e.Method && c.Pieces != null && i < c.Pieces.Length && c.Pieces[i] == chunks[i] && c.At.CompressedSize - off >= len;
                                bool sameContent = c.Verified && c.Sha256 == r.Sha256 && c.At.Method == e.Method && c.At.CompressedSize == e.CompressedSize && c.Pieces != null && i < c.Pieces.Length && c.Pieces[i] == chunks[i];
                                if (samePart || sameContent) { p = new Piece { From = f, Offset = c.At.DataOffset + off, How = c.Path == r.Path ? null : " (its part " + c.Path + ", which holds the same bytes)" }; break; }
                            }
                        if (p == null)
                        {
                            bool other = donors.Any(d => d.Check(r.Path) is MemberCheck dc && dc.Pieces != null && i < dc.Pieces.Length && dc.Pieces[i] != null);
                            Lost(r.Path + ", piece " + (i + 1) + " of " + chunks.Count, "no file given holds this piece with its recorded checksum" + (other ? " (the other backups hold a different version of it, which changed between the backups, so it is not this backup's content)" : ""), pos + header.Length + off, pos + header.Length + off + len);
                            continue;
                        }
                        p.Length = len; p.Expect = chunks[i]; p.Member = r.Path; p.Index = i;
                        plan.Pieces.Add(p);
                    }
                }
                else
                {
                    // no piece checksums recorded (a backup made before the repair map, or the map's own early copy): the whole part, from a file
                    // where it verifies, with the same stored form
                    var w = copies.Select(f => (f, c: f.Check(r.Path))).Where(x => x.c != null).Concat(All(everyone))
                        .FirstOrDefault(x => x.c.Verified && (r.Sha256 == null ? x.c.Path == r.Path : x.c.Sha256 == r.Sha256 && x.c.Md5 == r.Md5) && x.c.At.Method == e.Method && x.c.At.CompressedSize == e.CompressedSize && x.c.At.Size == e.Size);
                    if (w.c == null) Lost(r.Path, "no file given holds this part with its recorded checksum (SHA-256 " + (r.Sha256 ?? "unknown") + ")", pos + header.Length, pos + header.Length + e.CompressedSize);
                    else plan.Pieces.Add(new Piece { From = w.f, Offset = w.c.At.DataOffset, Length = e.CompressedSize, Expect = w.c.RawSha256, Member = r.Path, How = w.c.Path == r.Path ? null : " (its part " + w.c.Path + ", which holds the same bytes)" });
                }
                pos += header.Length + e.CompressedSize;
            }
            if (map.Manifest != null)
            {
                var L = map.ManifestFrom.At[BackupFormat.ManifestName];
                var e = EntryOf(BackupFormat.ManifestName, L.Header, pos);
                plan.Entries.Add(e);
                plan.Pieces.Add(new Piece { Literal = L.Header, Member = BackupFormat.ManifestName, How = "its header in " + source(map.ManifestFrom) });
                plan.Pieces.Add(new Piece { Literal = map.ManifestFrom.ManifestRaw, Member = BackupFormat.ManifestName, How = source(map.ManifestFrom) });
                pos += L.Header.Length + map.ManifestFrom.ManifestRaw.Length;
            }
            plan.Length = pos;
            return plan;
        }

        /// <summary>The early descriptor's exact bytes from the backup's other records: the chunk map (every member before it), the chunk
        /// map's own member as it is in a file where it verifies, and the manifest (identity). Accepted only when their SHA-256 equals the one
        /// the manifest recorded for the descriptor and they fit exactly before the first member; otherwise null.</summary>
        static (byte[] header, byte[] data)? RegenerateDescriptor(TrustedMap map, IList<BackupFile> files)
        {
            var rec = map.Find(BackupFormat.DescriptorName); var cmRec = map.Find(BackupFormat.ChunkMapName); var cm = map.ChunkMap; var man = map.Manifest;
            if (rec?.Sha256 == null || cmRec?.Sha256 == null || cm == null || man == null || cm.Members.Count == 0) return null;
            var hold = files.Select(f => f.Check(BackupFormat.ChunkMapName)).FirstOrDefault(c => c != null && c.Verified && c.HeaderOk && c.Sha256 == cmRec.Sha256 && c.Pieces != null && c.Pieces.All(p => p != null));
            if (hold == null) return null;
            var con = man.O("console"); var L = hold.At; long at = cm.Members.Max(m => m.DataEnd);
            var d = new MapDoc { Role = "descriptor", BackupId = man.S("backup_id"), Simulated = man.B("simulated"), CreatedUtc = man.S("created_utc"), CompletedUtc = man.S("completed_utc"), Board = con?.S("board"), Hwid = con?.S("hwid"), Tag = con?.S("tag"), DosTime = cm.DosTime, DosDate = cm.DosDate, Zip64 = cm.Zip64 };
            d.Members.AddRange(cm.Members);
            d.Members.Add(new MapMember { Path = BackupFormat.ChunkMapName, HeaderOffset = at, DataOffset = at + L.Header.Length, Method = L.Method, Crc = hold.Crc, Size = L.Size, CompressedSize = L.CompressedSize, Sha256 = cmRec.Sha256, Md5 = cmRec.Md5, LocalHeader = Hex.Of(L.Header), Chunks = hold.Pieces.ToList() });
            byte[] data; try { data = d.Bytes(RepairMap.DescriptorReserve); } catch (IOException) { return null; }
            using (var h = FastHash.Sha256()) if (Hex.Of(h.ComputeHash(data)) != rec.Sha256) return null;
            var crc = new Crc32(); crc.Update(data, 0, data.Length);
            var header = ZipWriter.LocalHeader(new ZipEntryInfo { Name = BackupFormat.DescriptorName, Method = 0, Crc = crc.Value, Size = data.Length, CompressedSize = data.Length, Flags = 0x0800, Zip64Local = cm.Zip64 }, cm.DosTime, cm.DosDate);
            if (header.Length + data.Length != cm.Members[0].HeaderOffset) return null;
            return (header, data);
        }

        /// <summary>the plan written to a new .partial file: every piece re-hashed as it is copied; the table of contents by the writer's rule</summary>
        internal static void Write(RebuildPlan plan, TrustedMap map, string partial, RepairOptions opt)
        {
            using (var o = Durable.CreateNew(partial))
            {
                var buf = new byte[Streams.Buffer]; long done = 0;
                foreach (var p in plan.Pieces)
                {
                    if (opt.Cancelled()) throw new OperationCanceledException();
                    if (p.Literal != null) { o.Write(p.Literal, 0, p.Literal.Length); done += p.Literal.Length; continue; }
                    using (var sha = FastHash.Sha256())
                    {
                        p.From.S.Position = p.Offset; long left = p.Length;
                        while (left > 0)
                        {
                            int n = p.From.S.Read(buf, 0, (int)Math.Min(buf.Length, left));
                            if (n <= 0) throw new RepairException(p.From.Label + " ended early while it was being read");
                            sha.TransformBlock(buf, 0, n, null, 0); o.Write(buf, 0, n); left -= n; done += n;
                            if (opt.Cancelled()) throw new OperationCanceledException();
                            opt.Say("write", "Writing the repaired backup...", done, plan.Length);
                        }
                        sha.TransformFinalBlock(new byte[0], 0, 0);
                        if (p.Expect != null && Hex.Of(sha.Hash) != p.Expect) throw new RepairException(p.From.Label + " changed while it was being read (" + p.Member + "); nothing was kept");
                    }
                }
                if (o.Position != plan.Length) throw new RepairException("the repaired file came out at an unexpected size");
                ZipWriter.WriteDirectory(o, plan.Entries, map.DosTime, map.DosDate, map.Zip64);
                Durable.Flush(o);
            }
        }

        /// <summary>consecutive pieces of one part from one file, as report regions</summary>
        internal static void Regions(RebuildPlan plan, RepairReport rep, Func<BackupFile, string> source)
        {
            long at = 0; Piece first = null; long start = 0, end = 0; int count = 0;
            void Flush()
            {
                if (first == null) return;
                int all = RepairMap.ChunkCount(plan.Entries.First(x => x.Name == first.Member).CompressedSize);
                var what = first.Member + (first.Index < 0 || all == 1 && count == 1 ? "" : count == all ? " (all " + count + " pieces)" : count == 1 ? " (piece " + (first.Index + 1) + " of " + all + ")" : " (pieces " + (first.Index + 1) + " to " + (first.Index + count) + " of " + all + ")");
                rep.Region(what, source(first.From) + first.How, start, end, first.Index < 0 ? "its recorded SHA-256" : "each piece's recorded SHA-256");
                first = null;
            }
            foreach (var p in plan.Pieces)
            {
                long len = p.Literal?.Length ?? p.Length;
                if (p.Literal != null) Flush();
                else if (first != null && p.From == first.From && p.Member == first.Member && p.Index == first.Index + count && first.Index >= 0 && p.How == first.How) { end = at + len; count++; }
                else { Flush(); first = p; start = at; end = at + len; count = 1; }
                at += len;
            }
            Flush();
        }
    }

    public sealed class RepairException : Exception { public RepairException(string m) : base(m) { } }

    /// <summary>The common run of the routes that make a new backup file (index, merge, donor, supply).</summary>
    internal static class Runner
    {
        static string When(string utc) => DateTime.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t.ToLocalTime().ToString("MMMM d, yyyy 'at' h:mm tt", CultureInfo.GetCultureInfo("en-US")) : "an unknown date";

        /// <summary>supplied (kind "supply" only): for a recorded startup software part, a file the person chose (KernelSupply)</summary>
        internal static RepairResult Run(string kind, IList<string> copyPaths, IList<string> donorPaths, string outputFolder, RepairOptions opt, IList<(string member, string file)> supplied = null)
        {
            opt = opt ?? new RepairOptions();
            var title = kind == "index" ? "Rebuild the backup's table of contents (ZIP index)" : kind == "merge" ? "Make one good backup file from two damaged copies" : kind == "supply" ? "Supply a missing or damaged startup software file" : "Repair damaged parts from other backups of the same console";
            var rep = new RepairReport(kind, title); var res = new RepairResult { Report = rep };
            var files = new List<BackupFile>(); var donors = new List<BackupFile>(); var supplies = new List<BackupFile>(); string partial = null;
            var before = new Dictionary<string, (long len, DateTime t)>(StringComparer.OrdinalIgnoreCase);
            RepairResult End(string outcome, string summary)
            {
                rep.Outcome = res.Outcome = outcome; rep.Summary = summary; rep.FinishedUtc = DateTime.UtcNow;
                foreach (var f in files.Concat(donors).Concat(supplies)) f.Dispose();
                foreach (var kv in before) { var fi = new FileInfo(kv.Key); bool same = fi.Exists && fi.Length == kv.Value.len && fi.LastWriteTimeUtc == kv.Value.t; rep.Originals.Add(new JObj { { "file", kv.Key }, { "unchanged", same } }); }
                if (partial != null && File.Exists(partial)) { try { File.Delete(partial); } catch (IOException ex) { rep.Errors.Add("The unfinished file " + partial + " could not be removed: " + ex.Message); } }
                if (opt.SaveReport && outcome != "canceled" && outputFolder != null && copyPaths.Count > 0)
                {
                    try { var stem = Path.GetFileNameWithoutExtension(copyPaths[0]) + " (repair report " + DateTime.Now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture) + ")"; (res.ReportJson, res.ReportText) = rep.Save(outputFolder, stem); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { rep.Errors.Add("The report could not be saved: " + ex.Message); }
                }
                return res;
            }
            try
            {
                if (copyPaths == null || copyPaths.Count == 0) return End("refused", "No backup file was given.");
                if (kind == "merge" && copyPaths.Count < 2) return End("refused", "Two copies of the same backup are needed to combine them.");
                if (kind == "donor" && (donorPaths == null || donorPaths.Count == 0)) return End("refused", "No other backup of this console was given.");
                if (kind == "supply" && (supplied == null || supplied.Count == 0 || supplied.Any(x => string.IsNullOrEmpty(x.file)))) return End("refused", "No file was chosen to supply. Nothing was written.");
                if (kind != "supply") supplied = null;
                var all = copyPaths.Concat(donorPaths ?? new string[0]).Select(Path.GetFullPath)
                    .Concat((supplied ?? new List<(string, string)>()).Select(x => Path.GetFullPath(x.file)).Distinct(StringComparer.OrdinalIgnoreCase)).ToList();   // one supplied file may serve two parts with the same bytes
                if (all.Distinct(StringComparer.OrdinalIgnoreCase).Count() != all.Count) return End("refused", "The same file was given twice.");
                foreach (var p in all) if (!File.Exists(p)) return End("refused", "The file " + p + " was not found.");
                if (string.IsNullOrEmpty(outputFolder)) return End("refused", "No folder was chosen for the repaired backup.");
                Directory.CreateDirectory(outputFolder);
                foreach (var p in all) { var fi = new FileInfo(p); before[p] = (fi.Length, fi.LastWriteTimeUtc); }
                for (int i = 0; i < copyPaths.Count; i++)
                    files.Add(BackupFile.Open(copyPaths[i], kind == "merge" ? "copy " + (i + 1) + " (" + Path.GetFileName(copyPaths[i]) + ")" : i == 0 ? "the damaged backup file" : "its copy " + Path.GetFileName(copyPaths[i])));
                foreach (var d in donorPaths ?? new string[0]) donors.Add(BackupFile.Open(d, "another backup (" + Path.GetFileName(d) + ")"));
                var map = TrustedMap.Build(files, out var problem, out var conflict);
                rep.Original = RepairReport.Identity(files[0], map, "damaged backup");
                if (map == null)
                {
                    foreach (var f in files.Skip(1)) rep.Others.Add(RepairReport.Identity(f, null, "copy of the same backup"));
                    rep.Lost("the whole backup", problem);
                    if (kind == "supply") return End("refused", "No trustworthy record of this backup's parts survives, so a supplied file cannot be checked against anything. Nothing was written. (" + problem + ")");
                    return End(conflict || files.Any(f => f.Refusals.Count > 0 || f.Foreign != null) ? "refused" : "not-repaired", problem);
                }
                // other backups: only of the same console (and as simulated or real as this one); each with its own trusted records
                var dmaps = new Dictionary<BackupFile, TrustedMap>();
                foreach (var d in donors.ToList())
                {
                    var dm = TrustedMap.Build(new[] { d }, out var dp);
                    if (dm == null) { rep.Skip(d.Label, "its own list of contents could not be trusted (" + dp + ")"); donors.Remove(d); continue; }
                    if (!map.SameConsole(dm)) { rep.Skip(d.Label, "it is a backup of another console" + (dm.Simulated != map.Simulated ? " (or a simulated one)" : "") + "; parts are only ever taken from backups of the same console"); donors.Remove(d); continue; }
                    d.Label = (dm.BackupId == map.BackupId ? "another copy of the same backup" : "the backup made " + When(dm.CompletedUtc)) + " (" + Path.GetFileName(d.Path) + ")";
                    dmaps[d] = dm;
                }
                long total = files.Concat(donors).Sum(f => f.Length), seen = 0;
                foreach (var f in files) { opt.Say("examine", "Checking " + f.Label + "...", seen, total); f.Examine(map, opt.Cancelled, n => { seen += n; opt.Say("examine", "Checking " + f.Label + "...", seen, total); }); }
                foreach (var d in donors) { opt.Say("examine", "Checking " + d.Label + "...", seen, total); d.Examine(dmaps[d], opt.Cancelled, n => { seen += n; opt.Say("examine", "Checking " + d.Label + "...", seen, total); }); }
                rep.Original = RepairReport.Identity(files[0], map, "damaged backup");
                foreach (var f in files.Skip(1)) rep.Others.Add(RepairReport.Identity(f, map, "copy of the same backup"));
                foreach (var d in donors) rep.Others.Add(RepairReport.Identity(d, dmaps[d], "another backup of the same console"));
                rep.Methods.Add("The backup's own records were trusted only as far as they agree: " + map.Source + ".");
                var whole = files.FirstOrDefault(f => f.Complete);
                if (whole != null)
                    return End("not-needed", whole == files[0] && kind != "merge" ? "The backup file is not damaged: its table of contents is intact and every part matches its recorded checksum. Nothing was written."
                        : whole.Label + " is complete: its table of contents is intact and every part matches its recorded checksum. Nothing was written; Make a Second Copy can copy it.");
                if (kind == "index") rep.Methods.Add(files[0].IndexIntact ? "The table of contents (ZIP index) is intact, so it does not need rebuilding." : "The parts were found through " + files[0].LocatedBy + ", with every length checked; nothing inside a part is ever read as a part header.");
                if (kind == "merge") rep.Methods.Add("Each 1,048,576-byte piece of each part was taken from whichever copy holds it with its recorded checksum" + (map.HasPieces ? "." : " (this backup was made before piece checksums existed, so whole parts were taken from the copy where they verify)."));
                if (kind == "donor") rep.Methods.Add("Damaged parts or pieces were taken from other backups of the same console only where those bytes have exactly the recorded checksum of this backup's part or piece. A part that changed between the backups (a newer save, for example) is never used: it would not be this backup's content.");
                if (kind == "supply")
                {
                    var why = KernelSupply.Admit(map, files, supplied, supplies, rep, opt);
                    if (why != null) return End("refused", why + " Nothing was written.");
                }
                Func<BackupFile, string> source = f => f == files[0] ? (kind == "merge" ? f.Label : "the damaged file itself") : f.Label;
                var plan = Rebuild.Plan(map, files, donors.Concat(supplies).ToList(), rep, source);
                foreach (var d in donors) if (!plan.Pieces.Any(p => p.From == d)) rep.Skip(d.Label, "none of its parts or pieces is byte-identical to what this backup is missing (checked by checksum)");
                foreach (var d in supplies) if (!plan.Pieces.Any(p => p.From == d)) rep.Skip(d.Label, "not needed: what it holds was found intact elsewhere in the files given");
                Report(plan, map, files, rep, source);
                if (!plan.Complete)
                {
                    var data = rep.Unrecoverable.Any(u => ((string)u["what"]).StartsWith("restore/"));
                    if (data) rep.Notes.Add("A part that cannot be recovered byte for byte is never rebuilt from its files: Salvage can export every file inside the backup whose recorded checksum still matches, and the damaged part remains unrepaired.");
                    rep.Notes.Add("No file was made: a backup missing any part is never offered as a backup to restore.");
                    return End("not-repaired", "The backup could not be repaired: " + rep.Unrecoverable.Count + " " + (rep.Unrecoverable.Count == 1 ? "piece or part has" : "pieces or parts have") + " no source with its recorded checksum. Nothing was written.");
                }
                // write, check, publish
                var stem = Path.GetFileNameWithoutExtension(copyPaths[0]) + " (repaired " + DateTime.Now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture) + ")";
                partial = CaptureEngine.Unique(outputFolder, stem, BackupFormat.Partial);
                opt.Say("write", "Writing the repaired backup...", 0, plan.Length);
                Rebuild.Write(plan, map, partial, opt);
                opt.Say("verify", "Checking the repaired backup...");
                var expected = map.Members.Where(m => m.Sha256 != null).ToDictionary(m => m.Path, m => m.Sha256);
                var v = Verifier.VerifyFile(partial, expected, opt.Cancelled, (d, t) => opt.Say("verify", "Checking the repaired backup...", d, t));
                res.OutputCheck = v;
                if (!v.Ok) { rep.Errors.AddRange(v.Problems); return End("failed", "The repaired file did not pass the full check, so it was deleted: " + v.Problems[0]); }
                if (v.Manifest?.S("backup_id") != map.BackupId) return End("failed", "The repaired file names another backup; it was deleted.");
                var final = partial.Substring(0, partial.Length - ".partial".Length);
                Durable.Publish(partial, final); partial = null; res.OutputPath = final; res.RestorePoint = true;
                bool? identical = opt.RecordedSha256 == null ? (bool?)null : v.ArchiveSha256 == opt.RecordedSha256 && (opt.RecordedBytes < 0 || v.Bytes == opt.RecordedBytes);
                var rebuilt = plan.RebuiltHeaders.Count > 0 ? " and the headers of " + string.Join(", ", plan.RebuiltHeaders) + " (rebuilt by Classic Cart Studio's ZIP rules)" : "";
                string container = identical == true ? "This file is byte-for-byte identical to the original backup file as it was made: its SHA-256 equals the one recorded then."
                    : identical == false ? "Every part matches its recorded checksum, but this file is not byte-for-byte the original backup file (its SHA-256 differs from the one recorded when the backup was made). Written again by this repair: the table of contents (ZIP index) at the end of the file" + rebuilt + ". It is a new file with its own identity; the parts a restore uses are proven by their own checksums."
                    : "Every part matches its recorded checksum" + (plan.RecordedHeaders.Count > 0 && plan.RebuiltHeaders.Count == 0 ? ", and every part's header is the exact copy recorded in the backup's repair map" : "") + ". Written again by this repair: the table of contents (ZIP index) at the end of the file" + rebuilt + ". Without the original file's recorded checksum, it cannot be proven that this file is byte-for-byte the original; it is a new file with its own identity.";
                rep.Output = new JObj { { "path", final }, { "bytes", v.Bytes }, { "sha256", v.ArchiveSha256 }, { "backup_id", map.BackupId }, { "restore_ready", v.RestoreReady }, { "recovery_baseline", v.RecoveryBaseline }, { "repair_map", v.RepairMap }, { "identical_to_original", identical },
                    { "checked", "It passed the full backup check from scratch (every part, its checksums and the console facts" + (v.RepairMap ? ", and its repair map" : "") + ")" + (v.RestoreReady ? "; restore-ready" : "") + (v.RecoveryBaseline ? ", a recovery baseline" : "") + "." }, { "container", container } };
                if (identical == null) rep.Unknowns.Add("Whether the new file is byte-for-byte the original file: no recorded checksum of the original file was given.");
                if (plan.RebuiltHeaders.Count > 0) rep.Unknowns.Add("Whether the rebuilt part headers equal the original ones byte for byte (their content is proven; the original header bytes were recorded nowhere).");
                rep.Unknowns.Add("What damaged the original file: a repair reads files; it cannot tell the cause. Keep the damaged file until you no longer need it; it was not changed.");
                rep.Notes.Add("The damaged file is unchanged and still damaged. The new file is a separate backup file; check its place in your backup list before relying on it.");
                return End("repaired", "A repaired backup file was made and passed the full check: " + Path.GetFileName(final) + ".");
            }
            catch (OperationCanceledException) { rep.Canceled = true; return End("canceled", "Canceled. Nothing was kept, and the files that were read are unchanged."); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is RepairException)
            { rep.Errors.Add(ex.Message); return End("failed", "The repair stopped: " + ex.Message + ". Nothing was kept, and the files that were read were not changed."); }
        }

        /// <summary>what each part needed, in plain words, and the damaged ranges for a later parity layer</summary>
        static void Report(RebuildPlan plan, TrustedMap map, IList<BackupFile> files, RepairReport rep, Func<BackupFile, string> source)
        {
            var primary = files[0];
            foreach (var f in files)
                foreach (var c in f.Checks.Values.Where(x => x.At != null && x.PieceOk != null))
                    for (int i = 0; i < c.PieceOk.Length; i++)
                        if (!c.PieceOk[i]) { long a = c.At.DataOffset + (long)i * RepairMap.ChunkSize; rep.Parity.Add(new JObj { { "file", f.Label }, { "part", c.Path }, { "piece", (long)i }, { "from", a }, { "to", Math.Min(a + RepairMap.ChunkSize, c.At.DataEnd) } }); }
            if (!plan.Complete)
            {
                rep.Notes.Add("Every other part and piece has a source whose bytes match its recorded checksum; only what is listed under \"Could not be recovered\" is missing.");
                return;
            }
            Rebuild.Regions(plan, rep, source);
            if (!primary.IndexIntact) rep.Repair("the table of contents (ZIP index)", "written again by Classic Cart Studio's ZIP rules from the parts found through " + (primary.LocatedBy ?? "the other copies"));
            foreach (var p in plan.Regenerated)
                rep.Repair(p, "damaged in every file given; rebuilt from the backup's other records (its chunk map and manifest). Its SHA-256 equals the one the manifest recorded for it, so these are its original bytes");
            foreach (var r in map.Members)
            {
                var c = primary.Check(r.Path); var mine = plan.Pieces.Where(p => p.Member == r.Path && p.Literal == null).ToList();
                if (c != null && c.Verified && c.HeaderOk) continue;
                var froms = mine.GroupBy(p => source(p.From) + (p.How ?? "")).Select(g => (g.First().Index < 0 ? "the whole part" : g.Count() + (g.Count() == 1 ? " piece" : " pieces")) + " from " + g.Key).ToList();
                if (c != null && c.Verified && !c.HeaderOk) { rep.Repair(r.Path + " (its header)", plan.Pieces.First(p => p.Member == r.Path && p.Literal != null).How); continue; }
                var where = source(primary).Replace(" itself", "");
                if (froms.Count > 0) rep.Repair(r.Path, (c == null || c.At == null ? "missing from " : c.Status == "incomplete" ? "cut off in " : "damaged in ") + where + "; taken: " + string.Join("; ", froms));
            }
        }
    }
}
