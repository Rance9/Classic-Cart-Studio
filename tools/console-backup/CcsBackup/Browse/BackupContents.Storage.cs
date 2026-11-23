// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// A USB and SD storage backup (External/ExternalFormat.cs) opened in the Backup Browser, read-only, with the same views as a console
// backup: By game, By type, All files and search, and the same verified export (Exporter). The file is checked from scratch first
// (StorageVerifier) and held open for reading only while it is browsed. Then:
//   - each source is a folder at the top ("USB drive GAMES (E:)"); a files-and-folders source holds its files and folders as its list
//     recorded them (names, sizes, times, attributes and each file's SHA-256 from when the backup was made); a file that could not be read
//     or changed while it was copied is listed and marked, and cannot be exported;
//   - a whole drive image source holds the drive's image as one file ("Whole drive image.img", its checksum worked out from the verified
//     regions on request), and a folder per partition with the files of its index (FAT and ext file systems; exFAT, NTFS and unknown ones
//     are named and not listed);
//   - every file is read from its place in the backup (a pack and its offset, or its extents in the regions) and exported only when its
//     bytes match the SHA-256 the backup recorded for it;
//   - the Classifier's rules (Classifier.cs) give By type and, where a drive holds hakchi's layout (hakchi/games/...), By game, per source.
// Storage dates come from the drive's own times as Windows reported them (files and folders), or as the file system recorded them (a drive
// image: FAT records local time with no time zone).
using CcsBackup.Archive;
using CcsBackup.External;
using CcsBackup.Ext4;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CcsBackup.Browse
{
    public sealed partial class BackupContents
    {
        /// <summary>a USB and SD storage backup is open (not a console backup): what it is, in plain words; null for a console backup</summary>
        public string StorageLabel { get; private set; }
        public StorageVerification StorageCheck { get; private set; }
        public const string StorageClockText = "File dates are the drive's own, as Windows reported them when the backup was made (a drive image: as its file system recorded them; FAT records local time with no time zone). They are shown exactly as recorded.";
        /// <summary>where this backup's file dates come from, in plain words (a console's clock, or a drive's own times)</summary>
        public string DatesText => StorageLabel != null ? StorageClockText : ClockText;
        sealed class StorageSource { public string Id, Mode; public long RegionSize, Bytes; public long[] RegionData; }
        readonly Dictionary<string, StorageSource> storageSources = new Dictionary<string, StorageSource>(StringComparer.Ordinal);
        readonly Dictionary<string, long[][]> storageExtents = new Dictionary<string, long[][]>(StringComparer.Ordinal);

        /// <summary>Opens a USB and SD storage backup for browsing after it passes its full check.</summary>
        public static OpenResult OpenStorage(string path, Settings settings = null, Func<bool> cancelled = null, Action<long, long> progress = null)
        {
            var r = new OpenResult();
            var hold = Hold(path, r); if (hold == null) return r;
            try
            {
                var v = StorageVerifier.Verify(path, null, cancelled, progress);
                r.Verification = v.AsVerification();
                if (!v.Ok)
                {
                    r.Problems.AddRange(v.Problems);
                    r.Problem = "The backup did not pass its check, so it is not opened as a complete backup: " + v.Problems[0];
                    hold.Dispose(); return r;
                }
                var c = new BackupContents(path, settings, hold) { Verification = r.Verification, StorageCheck = v, ArchiveSha256 = v.ArchiveSha256 };
                c.BuildStorage(v, cancelled);
                r.Contents = c; return r;
            }
            catch (OperationCanceledException) { hold.Dispose(); r.Canceled = true; r.Problem = "Opening the backup was canceled. Nothing was changed."; return r; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ZipFormatException || ex is FormatException)
            { hold.Dispose(); r.Problem = "The backup could not be opened: " + ex.Message; return r; }
        }

        static long? UnixOf(long fileTime, out int ns)
        {
            ns = 0; if (fileTime <= 0) return null;
            long t = fileTime - 116444736000000000L; long s = Math.DivRem(t, 10000000L, out long rem); if (rem < 0) { s--; rem += 10000000L; }
            ns = (int)(rem * 100); return s;
        }

        void BuildStorage(StorageVerification v, Func<bool> cancelled)
        {
            var m = v.Manifest; BackupId = m.S("backup_id"); CompletedUtc = m.S("completed_utc");
            var srcs = v.SourceList;
            StorageLabel = srcs.Count == 1 ? srcs[0].S("label") : srcs.Count + " USB and SD sources";
            StateText = m.O("scope")?.S("text");
            var all = new List<BrowseEntry>(); var games = new List<GameGroup>();
            foreach (var x in v.Entries) zip[x.Name] = x;
            if (!m.O("scope").B("complete")) Notes.Add("Not everything selected was copied as it was when this backup was made: files that could not be read or changed while they were copied are marked and cannot be copied out.");
            var cons = m.O("scope")?.S("consistency"); if (cons != null) Notes.Add(cons);
            foreach (var s in srcs)
            {
                var id = s.S("id"); var mode = s.S("mode"); var label = s.S("label");
                var top = new BrowseEntry { Id = id, ParentId = "", Name = label + (mode == "image" ? " (whole drive image)" : ""), Member = id, Source = EntrySource.VirtualFolder, Kind = EntryKind.Folder, TypeCode = 'd', Category = Category.EverythingElse, CategoryReason = "One source of this USB and SD backup." };
                Add(all, top);
                var data = new List<BrowseEntry>();
                if (mode == "files")
                {
                    storageSources[id] = new StorageSource { Id = id, Mode = mode };
                    foreach (var r in v.Inventory.TryGetValue(id, out var rows) ? rows : new List<Dictionary<string, object>>())
                    {
                        var p = r.S("path"); var type = r.S("type"); int slash = p.LastIndexOf('/');
                        var parent = slash <= 0 ? id : id + ":" + p.Substring(0, slash);
                        var e = new BrowseEntry { Id = id + ":" + p, ParentId = parent, Name = p.Substring(slash + 1), Source = EntrySource.StorageFile, Container = top, InnerPath = p, NestedPath = label + p, ImagePath = p, Owner = "storage",
                            Kind = type == "folder" ? EntryKind.Folder : type == "link" ? EntryKind.Link : EntryKind.File, TypeCode = type == "folder" ? 'd' : type == "link" ? 'l' : 'f', Size = Math.Max(0, r.L("size", 0)), Mode = (int)r.L("attributes", 0) };
                        var t = r.O("times"); var mt = UnixOf(t?.L("modified", 0) ?? 0, out int ns); if (mt != null) { e.MtimeSeconds = mt; e.MtimeNanoseconds = ns; e.DateSource = "the drive's own time, as Windows reported it"; }
                        if (e.Kind == EntryKind.Link) { e.LinkTarget = "(a link: listed, not followed)"; }
                        if (e.Kind == EntryKind.File)
                        {
                            e.Member = r.S("member"); e.InnerRef = r.L("offset", 0);
                            if (r.S("state") == "ok") { e.Sha256 = r.S("sha256"); e.HashOrigin = HashOrigin.Inventory; e.HashText = "Recorded when the backup was made, from the file's own bytes."; }
                            else { e.Unreadable = r.S("state") == "changed" ? "It changed while it was copied (" + r.S("problem") + "), so this copy is not offered as a good copy." : "It could not be read when the backup was made (" + r.S("problem") + ")."; e.StateWord = r.S("state") == "changed" ? "changed" : "not copied"; e.HashOrigin = HashOrigin.Unreadable; e.HashText = e.Unreadable; }
                            if (r.L("ea_bytes", 0) > 0) e.ImageNote = "It had extended attributes on the drive (" + r.L("ea_bytes", 0) + " bytes); they are not kept.";
                        }
                        Add(all, e); data.Add(e);
                    }
                }
                else
                {
                    v.Layouts.TryGetValue(id, out var lay); long R = lay.L("region_size"), bytes = lay.L("bytes"); int n = (int)lay.L("regions");
                    storageSources[id] = new StorageSource { Id = id, Mode = mode, RegionSize = R, Bytes = bytes, RegionData = Enumerable.Range(1, n).Select(i => zip[ExternalFormat.Region(id, i)].DataOffset).ToArray() };
                    Add(all, new BrowseEntry { Id = id + ":image", ParentId = id, Name = "Whole drive image.img", Source = EntrySource.StorageFile, Container = top, InnerPath = "/Whole drive image.img", NestedPath = label + ": the whole drive image", ImagePath = "/", Kind = EntryKind.File, TypeCode = 'f', Size = bytes, Member = id + "/image", InnerRef = -1, IsImage = true, ImageType = "A whole drive image", ImageNote = "Every byte of the drive, free space included (it can hold deleted or private data). Its checksum is worked out from the backup's verified regions when it is exported.", HashOrigin = HashOrigin.None, HashText = "Worked out from the backup's verified regions when it is exported.", Category = Category.SystemFiles, CategoryReason = "The whole drive, as one image file." });
                    var parts = (lay.O("layout")?.A("partitions") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
                    var rows = v.Index.TryGetValue(id, out var ir) ? ir : new List<Dictionary<string, object>>();
                    foreach (var p in parts)
                    {
                        var pn = p.L("number"); var pid = id + ":p" + pn;
                        var pe = new BrowseEntry { Id = pid, ParentId = id, Name = "Partition " + pn + " (" + p.S("file_system") + (p.S("label") != null ? " \"" + p.S("label") + "\"" : "") + ")", Member = pid, Container = top, InnerPath = "/partition " + pn, Source = EntrySource.VirtualFolder, Kind = EntryKind.Folder, TypeCode = 'd', Category = Category.EverythingElse, CategoryReason = "A partition of the drive image.",
                            ImageNote = p.B("browsable") ? null : p.S("not_browsable") };
                        if (!p.B("browsable")) { Notes.Add(label + ", partition " + pn + ": " + p.S("not_browsable")); pe.StateWord = "raw only"; }
                        if (p.S("note") != null) Notes.Add(label + ", partition " + pn + ": " + p.S("note"));
                        Add(all, pe);
                        foreach (var r in rows.Where(x => x.L("partition") == pn))
                        {
                            var path = r.S("path"); var type = r.S("type"); int slash = path.LastIndexOf('/');
                            var e = new BrowseEntry { Id = pid + ":" + path, ParentId = slash <= 0 ? pid : pid + ":" + path.Substring(0, slash), Name = path.Substring(slash + 1), Source = EntrySource.StorageFile, Container = pe, InnerPath = path, NestedPath = label + ", partition " + pn + path, ImagePath = path, Owner = "storage",
                                Kind = type == "folder" ? EntryKind.Folder : type == "link" ? EntryKind.Link : type == "file" ? EntryKind.File : EntryKind.Special, TypeCode = type == "folder" ? 'd' : type == "link" ? 'l' : type == "file" ? 'f' : '?', Size = Math.Max(0, r.L("size", 0)), Mode = (int)r.L("mode", r.L("attributes", 0)), Uid = (int)r.L("uid", 0), Gid = (int)r.L("gid", 0), LinkTarget = r.S("link_target") };
                            if (r.L("mtime", -1) >= 0) { e.MtimeSeconds = r.L("mtime"); e.DateSource = "the file system's own time"; }
                            else if (DateTime.TryParse(r.S("modified"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var fm)) { e.MtimeSeconds = (long)(fm - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; e.DateSource = "the time FAT recorded, in the local time of whatever wrote it (FAT records no time zone)"; }
                            if (e.Kind == EntryKind.File)
                            {
                                if (r.S("problem") != null || r.S("sha256") == null) { e.Unreadable = "It cannot be read from the image (" + (r.S("problem") ?? "no checksum was recorded") + ")."; e.StateWord = "cannot be read"; e.HashOrigin = HashOrigin.Unreadable; e.HashText = e.Unreadable; }
                                else
                                {
                                    e.Sha256 = r.S("sha256"); e.HashOrigin = HashOrigin.Inventory; e.HashText = "Worked out from the stored image when the backup was made, through the file's extents.";
                                    storageExtents[e.Id] = (r.A("extents") ?? new List<object>()).Select(x => ((IEnumerable<object>)x).Select(Convert.ToInt64).ToArray()).ToArray();
                                    e.Member = id + "/image";
                                }
                            }
                            Add(all, e); data.Add(e);
                        }
                    }
                }
                // By type and By game: the Classifier's rules on this source's paths
                var g = Classifier.Run(data, (e, max) => { if (e.Unreadable != null || e.Kind != EntryKind.File || e.Size > max) return null; try { var ms = new MemoryStream(); CopyStorage(e, ms, cancelled); return ms.ToArray(); } catch (IOException) { return null; } },
                                             (e, k) => { if (e.Unreadable != null || e.Kind != EntryKind.File) return null; try { var ms = new MemoryStream(); CopyStorage(e, ms, cancelled, Math.Min(k, e.Size)); return ms.ToArray(); } catch (IOException) { return null; } });
                foreach (var gg in g)
                {
                    if (games.Any(x => x.Key == gg.Key)) { var nk = gg.Key + "@" + id; foreach (var e in gg.Entries) e.GameKey = nk; gg.Key = nk; }
                    games.Add(gg);
                }
            }
            Games = games;
            foreach (var gg in games) gamesByKey[gg.Key] = gg;
            Entries = all;
            foreach (var e in all) { if (!children.TryGetValue(e.ParentId ?? "", out var l)) children[e.ParentId ?? ""] = l = new List<BrowseEntry>(); l.Add(e); }
        }

        /// <summary>a storage entry's bytes (at most max): a files-mode file from its pack, an image file through its extents, or the whole
        /// image through the regions</summary>
        void CopyStorage(BrowseEntry e, Stream sink, Func<bool> cancelled, long max = long.MaxValue)
        {
            var src = ExternalFormat.SourceOf(e.Member ?? "") ?? e.Id.Substring(0, 3);
            if (!storageSources.TryGetValue(src, out var s)) throw new IOException("its source is not in this backup");
            long want = Math.Min(max, e.Size);
            using (var f = Read())
            {
                if (s.Mode == "files")
                {
                    if (!zip.TryGetValue(e.Member, out var ze) || e.InnerRef < 0 || e.InnerRef + e.Size > ze.Size) throw new IOException("its place in the backup is not valid");
                    var w = new SubStream(f, ze.DataOffset + e.InnerRef, e.Size); Streams.Copy(w, sink, want, cancelled); return;
                }
                var view = new RegionView(f, s.RegionSize, s.RegionData, s.Bytes);
                if (e.InnerRef == -1 && e.Id.EndsWith(":image", StringComparison.Ordinal)) { Streams.Copy(view, sink, want, cancelled); return; }
                if (!storageExtents.TryGetValue(e.Id, out var ext)) throw new IOException("its place in the drive image is not recorded");
                var zero = new byte[1 << 16]; long left = want;
                foreach (var x in ext)
                {
                    if (left <= 0) break; long n = Math.Min(left, x[1]);
                    if (x[0] < 0) { for (long k = n; k > 0;) { int c = (int)Math.Min(zero.Length, k); sink.Write(zero, 0, c); k -= c; } }
                    else { view.Position = x[0]; Streams.Copy(view, sink, n, cancelled); }
                    left -= n;
                }
            }
        }

        /// <summary>the checksum a storage entry's export is checked against: recorded for files; worked out from the verified regions for
        /// the whole drive image (only after the backup passed its full check)</summary>
        string StorageHash(BrowseEntry e, Func<bool> cancelled, Action<long> progress)
        {
            if (e.HashTrusted) return e.Sha256;
            if (e.InnerRef != -1 || !e.Id.EndsWith(":image", StringComparison.Ordinal) || StorageCheck == null || !StorageCheck.Ok) return null;
            var h = new HashingSink(null, progress, cancelled); CopyStorage(e, h, cancelled); h.Finish();
            e.Sha256 = h.Sha256; e.HashOrigin = HashOrigin.Derived; e.BoundTo = Path; e.BoundToSha256 = ArchiveSha256;
            e.HashText = "Worked out from the backup's verified regions (the backup records each region's checksum, not the whole image's).";
            return h.Sha256;
        }
    }
}
