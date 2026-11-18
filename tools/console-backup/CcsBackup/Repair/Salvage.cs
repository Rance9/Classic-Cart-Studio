// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Salvage: what can still be taken out of a damaged backup (one file, or several copies of the same backup), when it cannot be repaired.
// The backup stays damaged and is never changed; what is exported is a folder of ordinary files, never a backup: it cannot be restored to
// a console, has no backup name, and nothing here ever calls it complete or restore-ready.
//   Survey  every part of the backup (restore images, references, the file list, README, the .ccs export, the manifest and the repair
//           map) with its status: "verified" only when its bytes match a SURVIVING record: its SHA-256 in the manifest or the repair map
//           (whole, or each 1,048,576-byte piece from whichever copy holds it), the manifest by its CRC-32 and format (no SHA-256 of the
//           manifest exists anywhere), the repair map by its own check. Then every regular file on the console's data storage, when the
//           file list (inventory/data-files.json) itself verifies: a file is "verified" when its bytes, read from the data storage image
//           (each piece from a copy where it matches, else the damaged bytes as they are), match the SHA-256 the file list recorded for
//           it; a file the list has no checksum for is verified only through a data storage image that verifies whole. No checksum is
//           ever made up for anything: what has no surviving record is listed with the reason and not exported. Links and special files
//           are listed, not copied.
//   Export  the selected verified items into a NEW folder "<backup> (salvaged files <date>)": parts under "backup parts", files under
//           "data storage files" (names made safe for Windows, never outside the folder); each written as .partial, checked against its
//           record while it is written, then renamed; the report (JSON and text) inside. Cancel or failure removes the folder.
// The survey keeps the files open for reading (no other program can change them) until it is disposed.
using CcsBackup.Archive;
using CcsBackup.Engine;
using CcsBackup.Ext4;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace CcsBackup.Repair
{
    public sealed class SalvageItem
    {
        /// <summary>part (a member of the backup file) | file (a regular file on the console's data storage)</summary>
        public string Kind;
        /// <summary>the part's name in the backup, or the file's path on the data storage ("/x" is /var/lib/x on the console)</summary>
        public string Path;
        public long Size;
        /// <summary>verified | damaged | incomplete | not-found | no-recorded-checksum | not-exportable</summary>
        public string Status;
        /// <summary>how its content is proven (the chain of recorded checksums), or why it cannot be</summary>
        public string Trust;
        /// <summary>the recorded SHA-256 it is checked against (null when it is proven another way: see Trust)</summary>
        public string Sha256;
        public string Source, ExportedTo;
        public bool Exportable => Status == "verified";
        internal List<Piece> Pieces; internal Rec Rec; internal int Method; internal byte[] Literal; internal uint Inode;
    }

    public sealed class SalvageSurvey : IDisposable
    {
        public string Problem, Summary; public TrustedMap Map;
        /// <summary>plain words: which route can help (nothing damaged; a repaired file can be made; or only other files can help)</summary>
        public string Advice;
        /// <summary>each file given, as it was found (its condition in plain words)</summary>
        public IEnumerable<(string file, string condition)> Conditions => Files.Select(f => (f.Path, f.Condition()));
        public readonly List<SalvageItem> Items = new List<SalvageItem>();
        public readonly List<string> Notes = new List<string>();
        internal readonly List<BackupFile> Files = new List<BackupFile>();
        internal readonly Dictionary<string, (long len, DateTime t)> Before = new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);
        internal Stream Image; internal Ext4Reader Fs;
        public IEnumerable<SalvageItem> Exportable => Items.Where(i => i.Exportable);
        public void Dispose() { foreach (var f in Files) f.Dispose(); Files.Clear(); }
    }

    public static class Salvage
    {
        static string N(long x) => x.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>Checks one damaged backup (and any copies of the same backup) and lists everything that can be exported, and what cannot
        /// and why. Nothing is written. Dispose the survey when done (it keeps the files open for reading).</summary>
        public static SalvageSurvey Survey(IList<string> copies, RepairOptions opt = null)
        {
            opt = opt ?? new RepairOptions();
            var s = new SalvageSurvey();
            if (copies == null || copies.Count == 0) { s.Problem = "No backup file was given."; return s; }
            var all = copies.Select(System.IO.Path.GetFullPath).ToList();
            if (all.Distinct(StringComparer.OrdinalIgnoreCase).Count() != all.Count) { s.Problem = "The same file was given twice."; return s; }
            foreach (var p in all) if (!File.Exists(p)) { s.Problem = "The file " + p + " was not found."; return s; }
            foreach (var p in all) { var fi = new FileInfo(p); s.Before[p] = (fi.Length, fi.LastWriteTimeUtc); }
            try { Check(s, all, opt); }
            catch (OperationCanceledException) { s.Dispose(); s.Items.Clear(); s.Map = null; s.Problem = "Canceled."; s.Summary = "Canceled before the check finished; nothing was written."; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { s.Dispose(); s.Items.Clear(); s.Map = null; s.Problem = "A file could not be read: " + ex.Message; s.Summary = s.Problem; }
            return s;
        }

        static void Check(SalvageSurvey s, List<string> all, RepairOptions opt)
        {
            for (int i = 0; i < all.Count; i++) s.Files.Add(BackupFile.Open(all[i], i == 0 ? "the damaged backup file" : "copy " + (i + 1) + " (" + System.IO.Path.GetFileName(all[i]) + ")"));
            var map = TrustedMap.Build(s.Files, out var problem);
            if (map == null) { s.Problem = problem; s.Summary = "Nothing can be exported: " + problem; return; }
            s.Map = map;
            long total = s.Files.Sum(f => f.Length), seen = 0;
            foreach (var f in s.Files) f.Examine(map, opt.Cancelled, n => { seen += n; opt.Say("examine", "Checking " + f.Label + "...", seen, total); });
            // the parts
            foreach (var r in map.Members)
            {
                var it = new SalvageItem { Kind = "part", Path = r.Path, Size = r.Size, Sha256 = r.Sha256, Rec = r };
                var proof = r.Sha256 != null ? "its SHA-256 matches the one recorded in " + r.RecordedBy : "its own check (the repair map records its own checksum; the manifest is lost)";
                var whole = s.Files.Select(f => (f, c: f.Check(r.Path))).FirstOrDefault(x => x.c != null && x.c.Verified);
                if (whole.c != null)
                {
                    it.Status = "verified"; it.Trust = proof; it.Source = whole.f.Label; it.Method = whole.c.At.Method; it.Size = whole.c.At.Size;
                    it.Pieces = new List<Piece> { new Piece { From = whole.f, Offset = whole.c.At.DataOffset, Length = whole.c.At.CompressedSize, Expect = whole.c.RawSha256, Member = r.Path } };
                }
                else if (r.Map?.Chunks != null && r.Map.Chunks.Count > 0 && r.Sha256 != null)
                {
                    var pieces = new List<Piece>(); int missing = 0;
                    for (int i = 0; i < r.Map.Chunks.Count; i++)
                    {
                        var hit = s.Files.Select(f => (f, c: f.Check(r.Path))).FirstOrDefault(x => x.c?.PieceOk != null && i < x.c.PieceOk.Length && x.c.PieceOk[i]);
                        if (hit.c == null) { missing++; continue; }
                        long off = (long)i * RepairMap.ChunkSize;
                        pieces.Add(new Piece { From = hit.f, Offset = hit.c.At.DataOffset + off, Length = Math.Min(RepairMap.ChunkSize, r.Map.CompressedSize - off), Expect = r.Map.Chunks[i], Member = r.Path, Index = i });
                    }
                    if (missing == 0) { it.Status = "verified"; it.Trust = "each 1,048,576-byte piece matches its recorded checksum (taken from " + string.Join(" and ", pieces.Select(p => p.From.Label).Distinct()) + "), and the whole part is checked against its recorded SHA-256 when it is exported"; it.Source = string.Join(" and ", pieces.Select(p => p.From.Label).Distinct()); it.Pieces = pieces; it.Method = r.Map.Method; it.Size = r.Map.Size; }
                    else { it.Status = Worst(s.Files, r.Path); it.Trust = missing + " of its " + r.Map.Chunks.Count + " pieces of 1,048,576 bytes match their recorded checksum in no file given"; }
                }
                else { it.Status = Worst(s.Files, r.Path); it.Trust = "no file given holds it with its recorded checksum" + (r.Map == null ? " (this backup has no piece checksums for it)" : ""); }
                s.Items.Add(it);
            }
            if (map.Manifest != null)
                s.Items.Add(new SalvageItem { Kind = "part", Path = BackupFormat.ManifestName, Status = "verified", Trust = "its CRC-32 and format check (no SHA-256 of the manifest is recorded anywhere)", Source = map.ManifestFrom.Label,
                    Literal = map.ManifestFrom.Content(map.ManifestFrom.At[BackupFormat.ManifestName], out _, out _) });
            DataFiles(s, opt);
            int parts = s.Items.Count(i => i.Kind == "part");
            s.Advice = s.Files.Any(f => f.Complete) ? "This backup is not damaged" + (s.Files.Count > 1 ? " (at least one of these files is complete)" : "") + "; nothing needs repairing."
                : map.Manifest != null && s.Items.Where(i => i.Kind == "part").All(i => i.Exportable) ? "Every part of this backup can still be recovered: a repaired backup file can be made from " + (s.Files.Count > 1 ? "these copies (Merge)." : "this file (Rebuild the table of contents).")
                : "Some parts are damaged or missing in every file given. Another copy of this backup (Merge), or another backup of this console made while those parts were the same (Repair from other backups), may still repair it. Salvage exports what is intact now.";
            s.Summary = s.Items.Count(i => i.Kind == "part" && i.Exportable) + " of " + parts + " parts of the backup and " + s.Items.Count(i => i.Kind == "file" && i.Exportable) + " of " + s.Items.Count(i => i.Kind == "file") + " files on the console's data storage can be exported with a surviving recorded checksum."
                + (s.Files.Any(f => f.Complete) ? "" : " The backup itself stays damaged.");
        }

        static string Worst(IList<BackupFile> files, string path)
        {
            var st = files.Select(f => f.Check(path)?.Status).Where(x => x != null).ToList();
            return st.Contains("damaged") ? "damaged" : st.Contains("incomplete") ? "incomplete" : "not-found";
        }

        /// <summary>the bytes of a part from its planned pieces, each checked; deflated parts unpacked; null with the reason</summary>
        static byte[] PartBytes(SalvageItem it, out string why)
        {
            why = null; var m = new MemoryStream();
            foreach (var p in it.Pieces)
            {
                var b = p.From.ReadUpTo(p.Offset, p.Length);
                using (var h = FastHash.Sha256()) if (b.Length != p.Length || p.Expect != null && Hex.Of(h.ComputeHash(b)) != p.Expect) { why = "it changed since it was checked"; return null; }
                m.Write(b, 0, b.Length);
            }
            if (it.Method == 0) return m.ToArray();
            try { m.Position = 0; using (var d = new DeflateStream(m, CompressionMode.Decompress)) { var o = new MemoryStream(); d.CopyTo(o); return o.ToArray(); } }
            catch (InvalidDataException) { why = "it cannot be unpacked"; return null; }
        }

        /// <summary>the files of the data storage, through the verified file list and the (possibly damaged) data storage image</summary>
        static void DataFiles(SalvageSurvey s, RepairOptions opt)
        {
            var map = s.Map;
            var inv = s.Items.FirstOrDefault(i => i.Path == BackupFormat.InventoryName);
            if (inv == null) { s.Notes.Add("This backup has no list of the files on the console's data storage, so no file inside it can be checked."); return; }
            if (!inv.Exportable) { s.Notes.Add("The list of files on the console's data storage (" + BackupFormat.InventoryName + ") is damaged, so no file inside the data storage can be checked; none is offered. The data storage image's own status is listed with the parts."); return; }
            Dictionary<string, object> doc;
            try { var b = PartBytes(inv, out var why); if (b == null) { s.Notes.Add("The file list could not be read: " + why + "."); return; } doc = Json.Parse(new UTF8Encoding(false, true).GetString(b)); }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidOperationException || ex is DecoderFallbackException) { s.Notes.Add("The file list could not be read: " + ex.Message); return; }
            var dev = doc.S("partition");
            var lay = map.Manifest?.O("layout")?.A("partitions")?.OfType<Dictionary<string, object>>().FirstOrDefault(p => p.S("label") == "data" || p.S("label") == "rootfs_data");
            if (lay != null) dev = lay.S("device");
            var imgRec = dev == null ? null : map.Find("restore/" + dev + ".img");
            var entries = (doc.A("entries") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
            if (imgRec == null) { s.Notes.Add("The data storage image is not part of this backup's records, so its files cannot be read."); return; }
            var img = s.Items.First(i => i.Path == imgRec.Path);
            bool imageOk = img.Exportable;
            s.Image = PatchedImage.For(imgRec, s.Files, out var patchedNote);
            if (s.Image == null) { s.Notes.Add("The data storage image (" + imgRec.Path + ") is not in any file given, so its files cannot be read."); return; }
            if (!imageOk) s.Notes.Add("The data storage image (" + imgRec.Path + ") is damaged: " + patchedNote + " Its files are read from it as it is, and each is offered only when it matches the checksum the file list recorded for it. The image itself remains unrepaired.");
            try { s.Fs = new Ext4Reader(s.Image); }
            catch (Ext4Exception ex) { s.Notes.Add("The data storage's file system cannot be read from the damaged image (" + ex.Message + "), so no file inside it can be checked."); return; }
            var listProof = "its SHA-256 matches the one in the backup's file list (" + BackupFormat.InventoryName + "), whose own SHA-256 matches " + inv.Rec.RecordedBy;
            s.Items.AddRange(CheckFiles(s.Fs, entries, imageOk, listProof, imgRec.Path, opt.Cancelled, (k, n) => opt.Say("examine", "Checking the files on the console's data storage...", k, n)));
        }

        /// <summary>The per-file proof, shared with the Backup Browser (BackupContents on a backup whose data storage image fails its whole
        /// check): every entry of the backup's verified file list, in its order, never the image's own directories. A regular file is
        /// "verified" only when its bytes, read through fs from the image as it is, have exactly the size and SHA-256 the file list recorded
        /// for it, or (no checksum of its own) when the image itself verifies whole (imageOk); folders are skipped; links and special files are
        /// "not-exportable". The image's metadata only locates bytes: what it points at is accepted only by the recorded checksum.</summary>
        internal static List<SalvageItem> CheckFiles(Ext4Reader fs, IList<Dictionary<string, object>> entries, bool imageOk, string listProof, string source, Func<bool> cancelled, Action<int, int> progress = null)
        {
            var items = new List<SalvageItem>(); int k = 0;
            foreach (var e in entries)
            {
                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                var path = e.S("path"); var type = e.S("type"); if (path == null || type == "d") continue;
                var it = new SalvageItem { Kind = "file", Path = path, Size = Math.Max(0, e.L("size")), Sha256 = e.S("sha256"), Source = source };
                if (type == "l") { it.Status = "not-exportable"; it.Trust = "a link to " + e.S("target") + " (links are listed, not copied)"; items.Add(it); continue; }
                if (type != "f") { it.Status = "not-exportable"; it.Trust = "a special file (type " + type + "), listed, not copied"; items.Add(it); continue; }
                long ino = e.L("inode");
                if (ino <= 0 || ino > uint.MaxValue) { it.Status = "not-found"; it.Trust = "the file list does not say where it is"; items.Add(it); continue; }
                it.Inode = (uint)ino;
                if (it.Sha256 == null || !Hex.IsHex(it.Sha256, 64))
                {
                    it.Sha256 = null;
                    if (imageOk) { it.Status = "verified"; it.Trust = "read from the data storage image, whose SHA-256 matches the recorded one (the file list keeps no checksum of its own for this file" + (e.S("unreadable") != null ? ": " + e.S("unreadable") : "") + ")"; }
                    else { it.Status = "no-recorded-checksum"; it.Trust = "the file list keeps no checksum for it and the data storage image around it is damaged, so its content cannot be confirmed"; }
                    items.Add(it); continue;
                }
                try
                {
                    if (fs.FileSize(fs.Inode(it.Inode)) != it.Size) { it.Status = "damaged"; it.Trust = "its size in the damaged image differs from the size the file list recorded"; items.Add(it); continue; }   // never read past the recorded size
                    var h = new HashingSink(null, null, cancelled); fs.ReadFile(it.Inode, h, it.Size); h.Finish();
                    if (h.Sha256 == it.Sha256 && h.Count == it.Size) { it.Status = "verified"; it.Trust = listProof; }
                    else { it.Status = "damaged"; it.Trust = "its bytes in the damaged image do not match the checksum the file list recorded"; }
                }
                catch (Ext4Exception ex) { it.Status = "damaged"; it.Trust = "it cannot be read from the damaged image (" + ex.Message + ")"; }
                items.Add(it);
                if (++k % 50 == 0) progress?.Invoke(k, entries.Count);
            }
            return items;
        }

        static readonly HashSet<string> Reserved = new HashSet<string>(new[] { "CON", "PRN", "AUX", "NUL" }.Concat(Enumerable.Range(1, 9).SelectMany(i => new[] { "COM" + i, "LPT" + i })), StringComparer.OrdinalIgnoreCase);

        /// <summary>a console path made safe as a Windows path under root (characters Windows refuses become _), never outside root</summary>
        internal static string SafePath(string root, string consolePath)
        {
            var segs = consolePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Select(seg =>
            {
                var b = new StringBuilder(); foreach (var ch in seg) b.Append(ch < 0x20 || "<>:\"\\|?*".IndexOf(ch) >= 0 ? '_' : ch);
                var r = b.ToString(); if (r.EndsWith(".") || r.EndsWith(" ")) r = r.TrimEnd('.', ' ') + "_";
                if (r.Length == 0 || r == "." || r == ".." || Reserved.Contains(r.Split('.')[0])) r = "_" + r;
                return r;
            }).ToArray();
            var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(new[] { root }.Concat(segs).ToArray()));
            var rootFull = System.IO.Path.GetFullPath(root).TrimEnd('\\') + "\\";
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) throw new IOException("a file name would land outside the export folder");
            return full;
        }

        /// <summary>Exports the selected verified items (all verified items when select is null) into a NEW folder under outputParent. Never a
        /// backup; the damaged backup is not changed. Cancel or failure removes the folder.</summary>
        public static RepairResult Export(SalvageSurvey s, string outputParent, RepairOptions opt = null, Func<SalvageItem, bool> select = null)
        {
            opt = opt ?? new RepairOptions();
            var rep = new RepairReport("salvage", "Export what can still be trusted from a damaged backup"); var res = new RepairResult { Report = rep };
            string folder = null; bool made = false;
            RepairResult End(string outcome, string summary)
            {
                rep.Outcome = res.Outcome = outcome; rep.Summary = summary; rep.FinishedUtc = DateTime.UtcNow;
                foreach (var kv in s.Before) { var fi = new FileInfo(kv.Key); rep.Originals.Add(new JObj { { "file", kv.Key }, { "unchanged", fi.Exists && fi.Length == kv.Value.len && fi.LastWriteTimeUtc == kv.Value.t } }); }
                if (outcome != "salvaged" && made) { try { Directory.Delete(folder, true); res.OutputPath = null; } catch (IOException ex) { rep.Errors.Add("The unfinished folder " + folder + " could not be removed: " + ex.Message); } }
                if (opt.SaveReport && outcome != "canceled")
                {
                    try { (res.ReportJson, res.ReportText) = outcome == "salvaged" ? rep.Save(folder, "Salvage report") : rep.Save(outputParent, System.IO.Path.GetFileNameWithoutExtension(s.Files.FirstOrDefault()?.Path ?? "backup") + " (salvage report)"); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { rep.Errors.Add("The report could not be saved: " + ex.Message); }
                }
                return res;
            }
            try
            {
                if (s == null || s.Files.Count == 0) return End("refused", "There is nothing to export (no survey of a backup).");
                rep.Original = RepairReport.Identity(s.Files[0], s.Map, "damaged backup");
                foreach (var f in s.Files.Skip(1)) rep.Others.Add(RepairReport.Identity(f, s.Map, "copy of the same backup"));
                if (s.Map == null) { rep.Lost("the whole backup", s.Problem); return End("not-repaired", "Nothing can be exported: " + s.Problem); }
                if (string.IsNullOrEmpty(outputParent)) return End("refused", "No folder was chosen for the exported files.");
                Directory.CreateDirectory(outputParent);
                var stem = System.IO.Path.GetFileNameWithoutExtension(s.Files[0].Path) + " (salvaged files " + DateTime.Now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture) + ")";
                for (int i = 1; i < 1000 && folder == null; i++) { var c = System.IO.Path.Combine(outputParent, stem + (i == 1 ? "" : " (" + i + ")")); if (!Directory.Exists(c) && !File.Exists(c)) folder = c; }
                if (folder == null) return End("refused", "Too many exports with the same name in that folder.");
                Directory.CreateDirectory(folder); made = true; res.OutputPath = folder;
                rep.Methods.Add("Each item was taken only with a surviving recorded checksum: " + s.Map.Source + " for the parts; the backup's file list for the files on the data storage. Nothing without one was exported.");
                rep.Methods.Add("Each exported file was written, checked against its record while it was written, and only then given its name.");
                var chosen = s.Items.Where(i => i.Exportable && (select == null || select(i))).ToList();
                long total = chosen.Sum(i => Math.Max(0, i.Size)), done = 0, bytes = 0; var buf = new byte[Streams.Buffer];
                foreach (var it in chosen)
                {
                    if (opt.Cancelled()) throw new OperationCanceledException();
                    var dest = it.Kind == "part" ? SafePath(System.IO.Path.Combine(folder, "backup parts"), it.Path) : SafePath(System.IO.Path.Combine(folder, "data storage files"), it.Path);
                    for (int n = 2; File.Exists(dest) || File.Exists(dest + ".partial"); n++) dest = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dest), System.IO.Path.GetFileNameWithoutExtension(dest) + " (" + n + ")" + System.IO.Path.GetExtension(dest));
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest));
                    var tmp = dest + ".partial"; string bad = null; HashingSink h;
                    using (var o = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20))
                    {
                        long at0 = done;
                        h = new HashingSink(o, w => opt.Say("export", "Exporting " + it.Path + "...", at0 + w, total), opt.Cancelled);
                        try
                        {
                            if (it.Literal != null) h.Write(it.Literal, 0, it.Literal.Length);
                            else if (it.Kind == "part" && it.Method == 0)
                                foreach (var p in it.Pieces)
                                {
                                    using (var ph = FastHash.Sha256())
                                    {
                                        p.From.S.Position = p.Offset; long left = p.Length;
                                        while (left > 0) { int got = p.From.S.Read(buf, 0, (int)Math.Min(buf.Length, left)); if (got <= 0) { bad = "the file ended while it was being read"; break; } ph.TransformBlock(buf, 0, got, null, 0); h.Write(buf, 0, got); left -= got; }
                                        ph.TransformFinalBlock(new byte[0], 0, 0);
                                        if (bad == null && p.Expect != null && Hex.Of(ph.Hash) != p.Expect) bad = "it changed since it was checked";
                                    }
                                    if (bad != null) break;
                                }
                            else if (it.Kind == "part") { var b = PartBytes(it, out bad); if (b != null) h.Write(b, 0, b.Length); }
                            else s.Fs.ReadFile(it.Inode, h);
                        }
                        catch (Ext4Exception ex) { bad = "it could not be read (" + ex.Message + ")"; }
                        h.Finish(); o.Flush(true);
                    }
                    if (bad == null && it.Sha256 != null && h.Sha256 != it.Sha256) bad = "its bytes did not match its record when it was written";
                    if (bad == null && it.Kind == "part" && it.Sha256 == null && it.Literal == null && MapDoc.Parse(File.ReadAllBytes(tmp), out _) == null) bad = "its own check did not match when it was written";
                    if (bad != null) { File.Delete(tmp); rep.Lost(it.Path, "not exported: " + bad); it.Status = "damaged"; continue; }
                    Durable.Publish(tmp, dest); it.ExportedTo = dest; bytes += h.Count; done += Math.Max(0, it.Size);
                    rep.Region(it.Path, it.Source ?? "the backup", 0, h.Count, it.Trust);
                }
                foreach (var it in s.Items.Where(i => !i.Exportable))
                    if (it.Status == "not-exportable") rep.Skip(it.Path, it.Trust); else rep.Lost(it.Path, it.Status.Replace('-', ' ') + ": " + it.Trust);
                foreach (var it in s.Items.Where(i => i.Exportable && select != null && !select(i))) rep.Skip(it.Path, "not selected");
                foreach (var n in s.Notes) rep.Notes.Add(n);
                rep.Notes.Add("This folder is not a backup and cannot be restored to a console. The backup file is unchanged and is still damaged.");
                rep.Unknowns.Add("The content of anything listed under \"Could not be recovered\": it has no surviving recorded checksum to confirm it, so it was not exported.");
                rep.Unknowns.Add("What damaged the backup file: salvage reads files; it cannot tell the cause.");
                rep.Output = new JObj { { "path", folder }, { "bytes", bytes }, { "items", (long)chosen.Count(i => i.ExportedTo != null) },
                    { "checked", "Every exported file matched its record when it was written. This folder is not a backup: it cannot be restored and is never offered as one." } };
                int exported = chosen.Count(i => i.ExportedTo != null);
                return End("salvaged", exported + " " + (exported == 1 ? "item" : "items") + " (" + Units.Gb(bytes) + ") were exported to " + System.IO.Path.GetFileName(folder) + ". " + rep.Unrecoverable.Count + " could not be recovered. The backup itself stays damaged.");
            }
            catch (OperationCanceledException) { rep.Canceled = true; return End("canceled", "Canceled. The unfinished folder was removed; the backup is unchanged."); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Ext4Exception)
            { rep.Errors.Add(ex.Message); return End("failed", "The export stopped: " + ex.Message + ". The unfinished folder was removed; the backup is unchanged."); }
        }
    }

    /// <summary>A data storage image read as one stream from the files given: each 1,048,576-byte piece from a file where it matches its
    /// recorded checksum, else from a file where it is present (as damaged as it is), else zeros. Only a file's own recorded checksum
    /// decides whether something read through it is trusted.</summary>
    sealed class PatchedImage : Stream
    {
        readonly (BackupFile f, long off)[] at; readonly long len; long pos;
        PatchedImage((BackupFile, long)[] at, long len) { this.at = at; this.len = len; }

        internal static PatchedImage For(Rec r, IList<BackupFile> files, out string note)
        {
            note = null;
            var held = files.Select(f => (f, c: f.Check(r.Path))).Where(x => x.c?.At != null && x.c.At.Method == 0).ToList();
            if (held.Count == 0) return null;
            long size = r.Size >= 0 ? r.Size : held[0].c.At.Size; int n = RepairMap.ChunkCount(size);
            var at = new (BackupFile, long)[n]; int good = 0, present = 0;
            for (int i = 0; i < n; i++)
            {
                long off = (long)i * RepairMap.ChunkSize;
                var v = held.FirstOrDefault(x => x.c.Verified || x.c.PieceOk != null && i < x.c.PieceOk.Length && x.c.PieceOk[i]);
                if (v.c != null) { at[i] = (v.f, v.c.At.DataOffset + off); good++; continue; }
                var p = held.FirstOrDefault(x => x.c.At.DataOffset + off < x.f.Length);
                if (p.c != null) { at[i] = (p.f, p.c.At.DataOffset + off); present++; }
            }
            if (r.Map?.Chunks != null) note = good + " of its " + n + " pieces match their recorded checksums; " + present + " are damaged, and " + (n - good - present) + " are missing from every file (read as zeros).";
            else note = "this backup has no piece checksums, so the image is read as it is in " + held[0].f.Label + ".";
            return new PatchedImage(at, size);
        }

        public override int Read(byte[] b, int o, int c)
        {
            if (pos >= len) return 0;
            int piece = (int)(pos / RepairMap.ChunkSize); long within = pos % RepairMap.ChunkSize;
            int n = (int)Math.Min(c, Math.Min(RepairMap.ChunkSize - within, len - pos));
            var (f, off) = at[piece];
            int got = 0;
            if (f != null) { f.S.Position = off + within; while (got < n) { int r = f.S.Read(b, o + got, n - got); if (r <= 0) break; got += r; } }
            if (got < n) Array.Clear(b, o + got, n - got);
            pos += n; return n;
        }
        public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
        public override long Length => len;
        public override long Position { get => pos; set { if (value < 0 || value > len) throw new IOException("outside the image"); pos = value; } }
        public override long Seek(long o, SeekOrigin origin) { Position = origin == SeekOrigin.Begin ? o : origin == SeekOrigin.Current ? pos + o : len + o; return pos; }
        public override void Flush() { }
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
}
