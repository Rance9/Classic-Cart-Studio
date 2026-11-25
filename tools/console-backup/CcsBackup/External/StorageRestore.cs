// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Restoring from a USB and SD backup. Two separate operations, each with a plan the person reviews first and confirms once (a digest of the
// exact plan and a single-use confirmation); the backup is checked from scratch before the plan is made and its file is checked unchanged
// again at the moment of writing, and every byte written is checked against the SHA-256 the backup recorded for it.
//   Restore files   chosen files (or all) of one source into a folder on a large enough, compatible drive. The plan says, for every file:
//                   create (not there now), same (already there with the same SHA-256: left alone), keep (merge: a different file with
//                   that name stays as it is and the backup's is not written), replace (replace: the file there now is first MOVED into a
//                   "Replaced by restore <date>" folder at the restore's top, never deleted), or not restored with the reason (a folder
//                   where a file belongs, a file over 4 GB for a FAT32 drive, a name or path Windows cannot create: such names are made
//                   safe and the change listed). What is restored: contents, last-write and creation times, read-only, hidden, system and
//                   archive attributes; what is not: permissions and owners, links, extended attributes, alternate data streams. Each file
//                   is written to a new temporary name, checked while it is written and read back, then renamed.
//   Restore a whole drive image   a separate, destructive operation: every byte of the image written onto a target drive or drive image
//                   file of at least its size, then read back and compared region by region. The plan names the target exactly (what Windows
//                   reports about it, its size, its layout now) and what is lost. Refused: the drive Windows runs from, any drive holding the
//                   backup, its recovery data or the backup location, a target that is too small, and the backup or its source image file
//                   themselves. A drive image file as the target is protected first (a whole drive image backup of it, checked) unless it
//                   is new. A connected USB drive or SD card as the target (RestoreDisk) is checked the same way as a whole-drive read
//                   (RawDiskTarget: still the same drive, a USB drive or SD card, never one that cannot be told apart from another, sectors of
//                   the image's size); its review names it (maker, model, size, serial number, how it is connected, the volumes on it now)
//                   and says that what is on it is lost and is not backed up first; one that looks like the drive the backup was made from
//                   is named so. Windows asks for administrator permission once; Workbench's administrator step checks the drive again,
//                   locks and dismounts its volumes, reads its identity once more through the opened drive, writes only after the window's
//                   go-ahead, reads every region back from the drive, and the operation record (kind "disk-restore") says "writing" from the
//                   go-ahead until the result is known, so an interrupted write is never taken for a finished or untouched drive. This is
//                   never a write to a console: a console's storage is restored only by Console Restore from a console backup.
using CcsBackup.Engine;
using CcsBackup.Library;
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
    public sealed class RestoreItem
    {
        public string Path, Rel, Kind, Action, Why, Sha256; public long Size; public bool Renamed;
        public long Created = -1, Modified = -1; public uint Attributes;
        internal Func<Stream, Func<bool>, string> Copy;   // writes the backup's bytes, returns their SHA-256
    }

    public sealed class FileRestorePlan
    {
        public string Backup, BackupSha256, BackupId, Source, SourceLabel, Target, Mode, TargetFileSystem, Problem, Digest, Nonce;
        public long BackupBytes, Bytes, NeedBytes, FreeBytes = -1; public DateTime BackupMtime;
        public readonly List<RestoreItem> Items = new List<RestoreItem>();
        public readonly List<string> Notes = new List<string>();
        public string MetadataText;
        public int Count(string action) => Items.Count(i => i.Action == action);
    }

    public sealed class StorageRestoreResult
    {
        public bool Ok; public string Text; public int Written, Same, Kept, Replaced, NotRestored; public long Bytes; public string ReplacedFolder, ProtectionBackup;
        /// <summary>a connected drive as the target: the write's state (RawDiskWriteResult.State), and whether nothing was written to it</summary>
        public string DiskState; public bool NothingWritten;
        public readonly List<string> Problems = new List<string>();
    }

    /// <summary>a whole-drive restore target: a drive image file, or a connected drive by its number (with its identity when it was chosen)</summary>
    public sealed class DiskTarget
    {
        public string ImageFile; public int DiskNumber = -1; public DiskIdentity Disk;
        public override string ToString() => ImageFile != null ? "the drive image file " + ImageFile : "drive " + DiskNumber;
    }

    public sealed class ImageRestorePlan
    {
        public string Backup, BackupSha256, BackupId, Source, SourceLabel, SourceKey, Refusal, Digest, Nonce, TargetText; public DiskTarget Target;
        public long ImageBytes, TargetBytes, BackupBytes; public DateTime BackupMtime; public bool NewFile, ProtectFirst; public DriveLayout TargetLayout;
        /// <summary>a connected drive as the target: its identity and volumes as reviewed, the image's sector size, and whether it looks like
        /// the drive the backup was made from</summary>
        public DiskIdentity TargetDisk; public List<DiskVolume> TargetVolumes = new List<DiskVolume>(); public int ImageSectorBytes = 512; public bool SameAsSource;
        public readonly List<string> Review = new List<string>();
        internal long RegionSize; internal List<(long dataOffset, long size, string sha)> Regions;
    }

    public static class StorageRestore
    {
        static readonly HashSet<string> Issued = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>what Windows reports about the drive a file restore writes to (tests: a stand-in drive's file system)</summary>
        internal static Func<string, SourceIdentity> TargetVolume = Devices.ForFolder;
        static string NewNonce() { var b = new byte[16]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b); var n = Hex.Of(b); lock (Issued) Issued.Add(n); return n; }
        static bool UseNonce(string n) { lock (Issued) return n != null && Issued.Remove(n); }
        static string Sha(string text) { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(text))); }

        static bool Under(string path, string folder)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            var a = System.IO.Path.GetFullPath(path).TrimEnd('\\') + "\\"; var b = System.IO.Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            return a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------ files
        public static FileRestorePlan PlanFiles(string backup, string sourceId, IList<string> paths, string target, string mode, Func<bool> cancelled = null)
        {
            var p = new FileRestorePlan { Backup = System.IO.Path.GetFullPath(backup), Source = sourceId, Target = target, Mode = mode };
            if (mode != "merge" && mode != "replace") { p.Problem = "Choose whether files already there are kept or replaced."; return p; }
            if (string.IsNullOrEmpty(target) || !Directory.Exists(target)) { p.Problem = "Choose an existing folder to restore into."; return p; }
            target = p.Target = System.IO.Path.GetFullPath(target);
            foreach (var sys in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
                if (!string.IsNullOrEmpty(sys) && Under(target, sys)) { p.Problem = "Files are never restored into Windows' own folders or its program folders. Choose another folder."; return p; }
            if (Under(target, System.IO.Path.GetDirectoryName(p.Backup))) { p.Problem = "Choose a folder outside the folder that holds the backup."; return p; }
            var v = StorageVerifier.Verify(p.Backup, null, cancelled);
            if (!v.Ok) { p.Problem = "The backup did not pass its check, so nothing is restored from it: " + v.Problems[0]; return p; }
            var fi = new FileInfo(p.Backup); p.BackupSha256 = v.ArchiveSha256; p.BackupId = v.BackupId; p.BackupBytes = fi.Length; p.BackupMtime = fi.LastWriteTimeUtc;
            var src = v.SourceList.FirstOrDefault(x => x.S("id") == sourceId);
            if (src == null) { p.Problem = "The backup has no source " + sourceId + "."; return p; }
            p.SourceLabel = src.S("label");
            var vol = TargetVolume(target); p.TargetFileSystem = vol.FileSystem; p.FreeBytes = vol.FreeBytes;
            bool fat = vol.FileSystem != null && vol.FileSystem.StartsWith("FAT", StringComparison.OrdinalIgnoreCase);
            var want = paths == null || paths.Count == 0 ? null : new HashSet<string>(paths, StringComparer.Ordinal);
            bool Chosen(string path) => want == null || want.Any(w => path == w || path.StartsWith(w.TrimEnd('/') + "/", StringComparison.Ordinal));
            var files = new List<(string path, string kind, long size, string sha, long created, long modified, uint attr, Func<Stream, Func<bool>, string> copy, string why)>();
            if (src.S("mode") == "files")
            {
                foreach (var r in v.Inventory[sourceId])
                {
                    var path = r.S("path"); if (!Chosen(path)) continue; var type = r.S("type"); var t = r.O("times");
                    if (type == "link") { files.Add((path, "link", 0, null, -1, -1, 0, null, "a link: listed in the backup, never followed or created")); continue; }
                    if (type == "folder") { files.Add((path, "folder", 0, null, t?.L("created", -1) ?? -1, t?.L("modified", -1) ?? -1, (uint)r.L("attributes", 0), null, null)); continue; }
                    if (r.S("state") != "ok") { files.Add((path, "file", r.L("size"), null, -1, -1, 0, null, "it was not copied unchanged when the backup was made (" + r.S("problem") + ")")); continue; }
                    var member = r.S("member"); long off = r.L("offset"), size = r.L("size"); var ze = v.Entries.First(x => x.Name == member);
                    files.Add((path, "file", size, r.S("sha256"), t?.L("created", -1) ?? -1, t?.L("modified", -1) ?? -1, (uint)r.L("attributes", 0),
                        (sink, c) => { using (var f = new FileStream(p.Backup, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16)) { var h = new HashingSink(sink, null, c); Streams.Copy(new SubStream(f, ze.DataOffset + off, size), h, size, c); h.Finish(); return h.Sha256; } }, null));
                }
            }
            else
            {
                v.Layouts.TryGetValue(sourceId, out var lay); long R = lay.L("region_size"), bytes = lay.L("bytes"); int n = (int)lay.L("regions");
                var offs = Enumerable.Range(1, n).Select(i => v.Entries.First(x => x.Name == ExternalFormat.Region(sourceId, i)).DataOffset).ToList();
                foreach (var r in v.Index[sourceId])
                {
                    var path = "/partition " + r.L("partition") + r.S("path"); if (!Chosen(path)) continue; var type = r.S("type");
                    if (type == "folder") { files.Add((path, "folder", 0, null, -1, -1, 0, null, null)); continue; }
                    if (type != "file") { files.Add((path, type, 0, null, -1, -1, 0, null, "a " + type + ": listed in the backup, not created")); continue; }
                    if (r.S("problem") != null || r.S("sha256") == null) { files.Add((path, "file", r.L("size"), null, -1, -1, 0, null, "it could not be read when the backup was made")); continue; }
                    var ext = (r.A("extents") ?? new List<object>()).Select(x => ((IEnumerable<object>)x).Select(Convert.ToInt64).ToArray()).ToList(); long size = r.L("size");
                    long mt = r.L("mtime", -1) >= 0 ? DateTime.FromFileTimeUtc(116444736000000000L + r.L("mtime") * 10000000L).ToFileTimeUtc() : DateTime.TryParse(r.S("modified"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var lt) ? lt.ToFileTimeUtc() : -1;
                    files.Add((path, "file", size, r.S("sha256"), -1, mt, (uint)(r.L("attributes", 0) & 0x27), (sink, c) =>
                    {
                        using (var f = new FileStream(p.Backup, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
                        {
                            var view = new RegionView(f, R, offs, bytes); var h = new HashingSink(sink, null, c); var zero = new byte[65536];
                            foreach (var e in ext) { if (e[0] < 0) { for (long left = e[1]; left > 0;) { int k = (int)Math.Min(zero.Length, left); h.Write(zero, 0, k); left -= k; } } else { view.Position = e[0]; Streams.Copy(view, h, e[1], c); } }
                            h.Finish(); return h.Sha256;
                        }
                    }, null));
                }
            }
            if (files.Count == 0) { p.Problem = "Nothing chosen is in this backup."; return p; }
            // target names: made safe for Windows, case twins kept apart, never outside the target
            var used = new Dictionary<string, string>(StringComparer.Ordinal); var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var x in files.OrderBy(x => x.path, StringComparer.Ordinal))
            {
                var segs = x.path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries); var safe = segs.Select(s => Words.Component(s, 255)).ToList();
                var parentKey = string.Join("\\", segs.Take(segs.Length - 1)); var parentRel = used.TryGetValue(parentKey, out var pr) ? pr : string.Join("\\", safe.Take(safe.Count - 1));
                var rel = (parentRel.Length > 0 ? parentRel + "\\" : "") + safe[safe.Count - 1];
                bool renamed = !safe.SequenceEqual(segs) || parentRel != string.Join("\\", segs.Take(segs.Length - 1));
                for (int k = 2; taken.Contains(rel); k++) { rel = (parentRel.Length > 0 ? parentRel + "\\" : "") + System.IO.Path.GetFileNameWithoutExtension(safe[safe.Count - 1]) + " (" + k + ")" + System.IO.Path.GetExtension(safe[safe.Count - 1]); renamed = true; }
                used[string.Join("\\", segs)] = rel; taken.Add(rel);
                var it = new RestoreItem { Path = x.path, Rel = rel, Kind = x.kind, Size = x.size, Sha256 = x.sha, Created = x.created, Modified = x.modified, Attributes = x.attr, Copy = x.copy, Renamed = renamed };
                var full = System.IO.Path.Combine(target, rel);
                if (x.why != null) { it.Action = "not-restored"; it.Why = x.why; }
                else if (!System.IO.Path.GetFullPath(full).StartsWith(target.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) { it.Action = "not-restored"; it.Why = "its name would land outside the chosen folder"; }
                else if (full.Length > 32000) { it.Action = "not-restored"; it.Why = "its path is longer than Windows allows"; }
                else if (x.kind == "folder") it.Action = Directory.Exists(NativeFiles.Long(full)) ? "same" : File.Exists(NativeFiles.Long(full)) ? "not-restored" : "create";
                else if (fat && x.size >= 4294967296L) { it.Action = "not-restored"; it.Why = "the chosen drive is " + vol.FileSystem + ", which holds files up to 4 GB; this file is " + Units.Gb(x.size); }
                else if (Directory.Exists(NativeFiles.Long(full))) { it.Action = "not-restored"; it.Why = "a folder with its name is there now"; }
                else if (File.Exists(NativeFiles.Long(full)))
                {
                    string now; using (var f = new FileStream(NativeFiles.Long(full), FileMode.Open, FileAccess.Read, FileShare.Read)) { var h = new HashingSink(null, null, cancelled); f.CopyTo(h); h.Finish(); now = h.Sha256; }
                    it.Action = now == x.sha ? "same" : mode == "merge" ? "keep" : "replace";
                    if (it.Action == "keep") it.Why = "a different file with its name is there now and is kept (merge)";
                }
                else it.Action = "create";
                if (it.Action == "not-restored" && it.Why == null) it.Why = "a file with its name is there now where a folder belongs";
                if (renamed && it.Action != "not-restored") p.Notes.Add(x.path + " is restored as " + rel + " (a name Windows cannot create as it was, or the same name differing only in case).");
                p.Items.Add(it);
            }
            long cluster = Math.Max(4096, vol.ClusterBytes);
            p.Bytes = p.Items.Where(i => i.Action == "create" || i.Action == "replace").Sum(i => i.Size);
            p.NeedBytes = p.Items.Where(i => i.Action == "create" || i.Action == "replace").Sum(i => (i.Size + cluster - 1) / cluster * cluster) + 1_000_000;
            if (p.FreeBytes >= 0 && p.NeedBytes > p.FreeBytes) { p.Problem = "There is not enough free space there: about " + Units.Gb(p.NeedBytes) + " is needed and " + Units.Gb(p.FreeBytes) + " is free."; return p; }
            p.MetadataText = "Restored: each file's contents, its last-write and creation times and its read-only, hidden, system and archive attributes" + (fat ? " (FAT keeps times in 2-second steps)" : "") + ". Not restored: permissions and owners, links, extended attributes and alternate data streams" + (src.S("mode") == "image" ? ", and the owner, group and permissions an ext file system records" : "") + ".";
            if (mode == "replace" && p.Count("replace") > 0) p.Notes.Add("Files that would be replaced are first moved, unchanged, into a folder named \"Replaced by restore\" with the date, at the top of the chosen folder. Nothing is deleted.");
            p.Digest = Sha(string.Join("\n", new[] { p.Backup, p.BackupSha256, p.Source, p.Target, p.Mode }.Concat(p.Items.Select(i => i.Path + "|" + i.Rel + "|" + i.Action + "|" + i.Sha256))));
            p.Nonce = NewNonce();
            return p;
        }

        public static StorageRestoreResult RestoreFiles(FileRestorePlan p, string digest, string nonce, Func<bool> cancelled = null, Action<EngineProgress> progress = null)
        {
            var r = new StorageRestoreResult(); cancelled = cancelled ?? (() => false);
            if (p == null || p.Problem != null) { r.Text = "There is no plan to carry out."; return r; }
            if (digest != p.Digest || !UseNonce(nonce)) { r.Text = "This plan was not confirmed as reviewed (or its confirmation was already used). Review it again."; return r; }
            var fi = new FileInfo(p.Backup);
            if (!fi.Exists || fi.Length != p.BackupBytes || fi.LastWriteTimeUtc != p.BackupMtime) { r.Text = "The backup file changed after the plan was made, so nothing was restored. Review it again."; return r; }
            string replacedRoot = null; long total = p.Bytes, done = 0;
            foreach (var it in p.Items.Where(i => i.Kind == "folder" && i.Action == "create")) Directory.CreateDirectory(NativeFiles.Long(System.IO.Path.Combine(p.Target, it.Rel)));
            foreach (var it in p.Items.Where(i => i.Kind == "file"))
            {
                if (cancelled()) { r.Problems.Add("Canceled before " + it.Path + "."); r.NotRestored++; continue; }
                if (it.Action == "same") { r.Same++; continue; }
                if (it.Action == "keep") { r.Kept++; continue; }
                if (it.Action != "create" && it.Action != "replace") { r.NotRestored++; continue; }
                var full = NativeFiles.Long(System.IO.Path.Combine(p.Target, it.Rel)); var tmp = full + ".ccs-restore-partial";
                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
                    string got; long before = done;
                    using (var o = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
                    { got = it.Copy(new ProgressSink(o, n => progress?.Invoke(new EngineProgress { Phase = "restore", Text = "Restoring " + it.Path + "...", Done = before + n, Total = total })), cancelled); o.Flush(true); }
                    if (got != it.Sha256) throw new IOException("its bytes in the backup do not match their recorded checksum");
                    using (var b = new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.Read)) { var h = new HashingSink(null); b.CopyTo(h); h.Finish(); if (h.Sha256 != it.Sha256 || h.Count != it.Size) throw new IOException("the restored copy reads back differently"); }
                    if (it.Action == "replace")
                    {
                        if (replacedRoot == null) { replacedRoot = System.IO.Path.Combine(p.Target, "Replaced by restore " + DateTime.Now.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture)); Directory.CreateDirectory(NativeFiles.Long(replacedRoot)); r.ReplacedFolder = replacedRoot; }
                        var keep = NativeFiles.Long(System.IO.Path.Combine(replacedRoot, it.Rel)); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(keep));
                        File.SetAttributes(full, File.GetAttributes(full) & ~FileAttributes.ReadOnly); File.Move(full, keep); r.Replaced++;
                    }
                    File.Move(tmp, full);
                    if (it.Created > 0) File.SetCreationTimeUtc(full, DateTime.FromFileTimeUtc(it.Created));
                    if (it.Modified > 0) File.SetLastWriteTimeUtc(full, DateTime.FromFileTimeUtc(it.Modified));
                    var a = (FileAttributes)(it.Attributes & 0x27); if (a != 0) File.SetAttributes(full, a);
                    r.Written++; r.Bytes += it.Size; done = before + it.Size;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is OperationCanceledException)
                { try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { } r.NotRestored++; r.Problems.Add(it.Path + ": " + ex.Message); }
            }
            foreach (var it in p.Items.Where(i => i.Kind == "folder" && i.Action == "create" && i.Modified > 0)) { try { Directory.SetLastWriteTimeUtc(NativeFiles.Long(System.IO.Path.Combine(p.Target, it.Rel)), DateTime.FromFileTimeUtc(it.Modified)); } catch (Exception) { } }
            r.NotRestored += p.Count("not-restored");
            r.Ok = r.Problems.Count == 0 && !cancelled();
            r.Text = Words.Count(r.Written, "file") + " restored (" + Units.Gb(r.Bytes) + "), each checked against its recorded checksum and read back. " + (r.Same > 0 ? Words.Count(r.Same, "file") + " already there with the same contents. " : "") + (r.Kept > 0 ? Words.Count(r.Kept, "different file") + " kept as " + (r.Kept == 1 ? "it was" : "they were") + ". " : "") + (r.Replaced > 0 ? Words.Count(r.Replaced, "file") + " replaced; the earlier versions are in " + System.IO.Path.GetFileName(r.ReplacedFolder) + ". " : "") + (r.NotRestored > 0 ? Words.Count(r.NotRestored, "item") + " not restored (see the list)." : "");
            return r;
        }

        sealed class ProgressSink : Stream
        {
            readonly Stream o; readonly Action<long> p; long n;
            public ProgressSink(Stream o, Action<long> p) { this.o = o; this.p = p; }
            public override void Write(byte[] b, int off, int c) { o.Write(b, off, c); n += c; p?.Invoke(n); }
            public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true; public override long Length => n;
            public override long Position { get => n; set => throw new NotSupportedException(); }
            public override void Flush() => o.Flush(); public override int Read(byte[] b, int off, int c) => throw new NotSupportedException();
            public override long Seek(long off, SeekOrigin so) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException();
        }

        /// <summary>the places whose drives a whole-drive write never touches: the backup location, the additional-copy location and the records</summary>
        public static List<ProtectedPath> Protected(Settings s)
        {
            var l = new List<ProtectedPath>();
            if (s == null) return l;
            if (!string.IsNullOrEmpty(s.PrimaryFolder)) l.Add(new ProtectedPath(s.PrimaryFolder, "your backup location"));
            if (!string.IsNullOrEmpty(s.ArchiveFolder)) l.Add(new ProtectedPath(s.ArchiveFolder, "your additional-copy location"));
            if (!string.IsNullOrEmpty(s.RecordsFolder)) l.Add(new ProtectedPath(s.RecordsFolder, "Workbench's records"));
            return l;
        }

        // ------------------------------------------------------------------ a whole drive image
        public static ImageRestorePlan PlanImage(string backup, string sourceId, DiskTarget target, IDiskQueries facts = null, Func<bool> cancelled = null, IEnumerable<ProtectedPath> alsoProtect = null)
        {
            facts = facts ?? StorageBackup.DiskQueries;
            var p = new ImageRestorePlan { Backup = System.IO.Path.GetFullPath(backup), Source = sourceId, Target = target };
            string Refuse(string why) { p.Refusal = why; return why; }
            var v = StorageVerifier.Verify(p.Backup, null, cancelled);
            if (!v.Ok) { Refuse("The backup did not pass its check, so nothing is restored from it: " + v.Problems[0]); return p; }
            var src = v.SourceList.FirstOrDefault(x => x.S("id") == sourceId);
            if (src == null || src.S("mode") != "image") { Refuse("Only a whole drive image can be restored as a whole drive. Restore files instead."); return p; }
            var fi = new FileInfo(p.Backup); p.BackupSha256 = v.ArchiveSha256; p.BackupId = v.BackupId; p.BackupBytes = fi.Length; p.BackupMtime = fi.LastWriteTimeUtc; p.SourceLabel = src.S("label");
            var sid = SourceIdentity.FromJson(src.O("identity")); p.SourceKey = sid?.Key;
            var lay = v.Layouts[sourceId]; p.ImageBytes = lay.L("bytes"); p.RegionSize = lay.L("region_size");
            p.Regions = Enumerable.Range(1, (int)lay.L("regions")).Select(i => { var e = v.Entries.First(x => x.Name == ExternalFormat.Region(sourceId, i)); return (e.DataOffset, e.Size, v.Members[e.Name].sha256); }).ToList();
            var inputs = new List<string> { p.Backup }.Concat(Parity.FilesOf(p.Backup)).ToList();
            if (target == null || target.ImageFile == null && target.DiskNumber < 0) { Refuse("Choose a target drive or drive image file."); return p; }
            if (target.ImageFile != null)
            {
                var t = System.IO.Path.GetFullPath(target.ImageFile);
                if (inputs.Any(x => string.Equals(System.IO.Path.GetFullPath(x), t, StringComparison.OrdinalIgnoreCase))) { Refuse("The target is the backup itself (or its recovery data). Choose another file."); return p; }
                if (sid?.Kind == SourceKind.ImageFile && string.Equals(System.IO.Path.GetFullPath(sid.Root), t, StringComparison.OrdinalIgnoreCase)) { Refuse("The target is the drive image file the backup was made from. A whole drive image is never restored onto its own source: choose a separate file."); return p; }
                if (Directory.Exists(t)) { Refuse("The target is a folder; choose a file."); return p; }
                p.NewFile = !File.Exists(t); p.TargetBytes = p.NewFile ? p.ImageBytes : new FileInfo(t).Length;
                if (!p.NewFile && p.TargetBytes < p.ImageBytes) { Refuse("The target file is smaller (" + Units.Gb(p.TargetBytes) + ") than the drive image (" + Units.Gb(p.ImageBytes) + ")."); return p; }
                if (!p.NewFile) using (var f = new FileStream(t, FileMode.Open, FileAccess.Read, FileShare.Read)) p.TargetLayout = PartitionTable.Read(f);
                p.ProtectFirst = !p.NewFile;
                p.TargetText = p.NewFile ? "a new drive image file, " + t : "the drive image file " + t + " (" + Units.Gb(p.TargetBytes) + ")";
            }
            else
            {
                if (!Gate.StorageDiskWrites) { Refuse(Gate.StorageDiskText + " Restore onto a drive image file instead, or restore the files."); return p; }
                int n = target.DiskNumber; var wanted = target.Disk;
                if (wanted == null) { var e = facts.Disks().FirstOrDefault(d => d.Number == n); wanted = e == null ? null : facts.Describe(e); }
                if (wanted == null) { Refuse("Drive " + n + " is not connected now."); return p; }
                var protect = inputs.Select(x => new ProtectedPath(x, string.Equals(x, p.Backup, StringComparison.OrdinalIgnoreCase) ? "the backup being restored" : "the backup's recovery data")).Concat(alsoProtect ?? Enumerable.Empty<ProtectedPath>())
                    .Concat(RawDiskTarget.Always(RawDiskClient.Program())).ToList();
                var tc = RawDiskTarget.Check(facts, StorageBackup.WindowPolicy, wanted, protect);
                if (!tc.Ok) { Refuse(tc.Text); return p; }
                var id = p.TargetDisk = tc.Now; p.TargetVolumes = tc.Volumes;
                p.TargetBytes = id.Bytes; if (p.TargetBytes < p.ImageBytes) { Refuse("The drive is smaller (" + Units.Gb(p.TargetBytes) + ") than the drive image (" + Units.Gb(p.ImageBytes) + ")."); return p; }
                p.ImageSectorBytes = sid?.SectorBytes > 0 ? sid.SectorBytes : 512;
                if (id.SectorBytes != p.ImageSectorBytes) { Refuse("The drive uses " + id.SectorBytes.ToString("N0", CultureInfo.InvariantCulture) + "-byte sectors and the image " + p.ImageSectorBytes.ToString("N0", CultureInfo.InvariantCulture) + "-byte sectors, so its partitions would not line up on it. Choose another drive, or restore the files."); return p; }
                p.SameAsSource = sid != null && sid.Kind == SourceKind.Disk && id.ToSource().Key == sid.Key;
                p.TargetText = id.Display + (id.Serial != null ? ", serial number " + id.Serial : ", no serial number reported") + ", connected by " + id.BusText;
                p.Review.Add("Everything on " + id.Display + " from its first byte up to " + Units.Gb(p.ImageBytes) + " is replaced by the drive image of " + p.SourceLabel + ". What is on that part of the drive now is lost: Workbench does not back it up first. To keep it, cancel and back the drive up first.");
                if (p.TargetBytes > p.ImageBytes) p.Review.Add("The drive is larger than the image: its last " + Units.Gb(p.TargetBytes - p.ImageBytes) + " stay as they are, and its partitions keep the image's sizes.");
                p.Review.Add(p.TargetVolumes.Count == 0 ? "Windows shows no volume on the drive now." : "On the drive now: " + string.Join("; ", p.TargetVolumes.Select(v => v.Display)) + ".");
                var letters = p.TargetVolumes.SelectMany(v => v.Mounts).Where(m => m.Length <= 3).Select(m => m.TrimEnd('\\')).ToList();
                if (letters.Count > 0) p.Review.Add("While it is written, " + string.Join(" and ", letters) + " go" + (letters.Count == 1 ? "es" : "") + " away; afterward Windows shows the volumes the image holds.");
                if (p.SameAsSource) p.Review.Add("This looks like the drive the backup was made from (the same maker, model, serial number and size). Restoring onto it puts back what the backup holds and replaces what is on it now.");
                p.Review.Add("Windows asks for administrator permission once for this restore. Then the drive is checked again, written, and read back from the drive.");
            }
            if (p.TargetDisk == null)
            {
                p.Review.Add(p.NewFile ? "A new file of " + Units.Gb(p.ImageBytes) + " is made, holding the drive image of " + p.SourceLabel + ". Nothing that exists now is replaced."
                    : "Every byte of the target from its start to " + Units.Gb(p.ImageBytes) + " is replaced by the drive image of " + p.SourceLabel + ". What is on that part of the target now is lost" + (p.ProtectFirst ? " (it is backed up first, as a whole drive image, and checked)" : "") + ".");
                if (p.TargetBytes > p.ImageBytes) p.Review.Add("The target is larger than the image: the last " + Units.Gb(p.TargetBytes - p.ImageBytes) + " stay as they are, and the drive's partitions keep the image's sizes.");
                if (p.TargetLayout != null) p.Review.Add("On the target now: " + (p.TargetLayout.Parts.Count == 0 ? "no partition or file system this version recognizes" : string.Join("; ", p.TargetLayout.Parts.Select(x => "partition " + x.Number + ", " + x.FileSystem + (x.Label != null ? " \"" + x.Label + "\"" : "") + ", " + Units.Gb(x.Length)))) + ".");
            }
            p.Digest = Sha(string.Join("\n", p.Backup, p.BackupSha256, p.Source, p.TargetText, p.ImageBytes.ToString(CultureInfo.InvariantCulture), p.TargetDisk == null ? "" : Json.Write(p.TargetDisk.ToJson())));
            p.Nonce = NewNonce();
            return p;
        }

        public static StorageRestoreResult RestoreImage(ImageRestorePlan p, string digest, string nonce, Settings settings, Func<bool> cancelled = null, Action<EngineProgress> progress = null)
        {
            var r = new StorageRestoreResult(); cancelled = cancelled ?? (() => false);
            if (p == null || p.Refusal != null) { r.Text = p?.Refusal ?? "There is no plan to carry out."; return r; }
            if (digest != p.Digest || !UseNonce(nonce)) { r.Text = "This plan was not confirmed as reviewed (or its confirmation was already used). Review it again."; return r; }
            var fi = new FileInfo(p.Backup);
            if (!fi.Exists || fi.Length != p.BackupBytes || fi.LastWriteTimeUtc != p.BackupMtime) { r.Text = "The backup file changed after the plan was made, so nothing was written. Review it again."; r.NothingWritten = true; return r; }
            if (p.Target.ImageFile == null) return RestoreDisk(p, settings, cancelled, progress);
            string file = p.Target.ImageFile;
            if (p.ProtectFirst)
            {
                progress?.Invoke(new EngineProgress { Phase = "protect", Text = "Backing up the target first..." });
                var pb = StorageBackup.Run(new[] { new StorageRequest { Path = file, Mode = "image" } }, new StorageOptions { Settings = settings, SecondCopy = false, Purpose = "Before a whole drive image was restored onto it", Cancelled = cancelled });
                if (!pb.Protected) { r.Text = "The target could not be backed up first, so nothing was written: " + (pb.Problem ?? "its backup is not complete"); return r; }
                r.ProtectionBackup = pb.PrimaryPath;
            }
            try
            {
                using (var src = new FileStream(p.Backup, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
                using (var dst = new FileStream(file, p.NewFile ? FileMode.CreateNew : FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1 << 20, FileOptions.WriteThrough))
                {
                    if (p.NewFile) dst.SetLength(p.ImageBytes);
                    var buf = new byte[Streams.Buffer]; long done = 0;
                    for (int i = 0; i < p.Regions.Count; i++)
                    {
                        if (cancelled()) throw new OperationCanceledException();
                        var (off, size, sha) = p.Regions[i]; long at = i * p.RegionSize;
                        using (var h = FastHash.Sha256())
                        {
                            src.Position = off; dst.Position = at; long left = size;
                            while (left > 0) { int n = src.Read(buf, 0, (int)Math.Min(buf.Length, left)); if (n <= 0) throw new IOException("the backup ended early"); h.TransformBlock(buf, 0, n, null, 0); dst.Write(buf, 0, n); left -= n; done += n; progress?.Invoke(new EngineProgress { Phase = "write", Text = "Writing the drive image...", Done = done, Total = p.ImageBytes * 2 }); }
                            h.TransformFinalBlock(new byte[0], 0, 0); if (Hex.Of(h.Hash) != sha) throw new IOException("a region of the backup does not match its recorded checksum; the target is incomplete");
                        }
                    }
                    dst.Flush(true);
                    for (int i = 0; i < p.Regions.Count; i++)
                    {
                        var (_, size, sha) = p.Regions[i];
                        using (var h = FastHash.Sha256())
                        {
                            dst.Position = i * p.RegionSize; long left = size;
                            while (left > 0) { int n = dst.Read(buf, 0, (int)Math.Min(buf.Length, left)); if (n <= 0) throw new IOException("the target ended early"); h.TransformBlock(buf, 0, n, null, 0); left -= n; done += n; progress?.Invoke(new EngineProgress { Phase = "readback", Text = "Reading the target back...", Done = done, Total = p.ImageBytes * 2 }); }
                            h.TransformFinalBlock(new byte[0], 0, 0); if (Hex.Of(h.Hash) != sha) { r.Problems.Add("Region " + (i + 1) + " reads back differently from the backup."); }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { r.Text = "Canceled while writing: the target holds part of the image and part of what was there before. Restore again, or restore the protective backup" + (r.ProtectionBackup != null ? " (" + System.IO.Path.GetFileName(r.ProtectionBackup) + ")" : "") + "."; return r; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { r.Text = "The restore stopped: " + ex.Message + ". The target may hold part of the image; nothing is retried." + (r.ProtectionBackup != null ? " Its earlier contents are in " + System.IO.Path.GetFileName(r.ProtectionBackup) + "." : ""); return r; }
            r.Ok = r.Problems.Count == 0; r.Bytes = p.ImageBytes;
            r.Text = r.Ok ? "The drive image was written to " + p.TargetText + " and read back: every region matches the backup's recorded checksum." : "The drive image was written, but " + r.Problems.Count + " region(s) read back differently: the target cannot be relied on.";
            return r;
        }

        /// <summary>a connected USB drive or SD card as the target: one administrator step writes and reads back, under an operation record</summary>
        static StorageRestoreResult RestoreDisk(ImageRestorePlan p, Settings settings, Func<bool> cancelled, Action<EngineProgress> progress)
        {
            var r = new StorageRestoreResult();
            if (!Gate.StorageDiskWrites || p.TargetDisk == null) { r.Text = Gate.StorageDiskText; r.NothingWritten = true; return r; }
            var id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Record rec;
            try
            {
                rec = Record.Start(settings.RecordsFolder, id, "disk-restore", new JObj { { "target", p.TargetDisk.ToJson() }, { "target_text", p.TargetText }, { "backup", p.Backup }, { "backup_sha256", p.BackupSha256 },
                    { "source", p.Source }, { "source_label", p.SourceLabel }, { "image_bytes", p.ImageBytes } });
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { r.Text = "Workbench could not write its record of this restore (" + ex.Message + "), so nothing was written."; r.NothingWritten = true; return r; }
            rec.State("asking");
            var image = new RawImagePlan { Backup = p.Backup, BackupBytes = p.BackupBytes, BackupMtimeUtc = p.BackupMtime, ImageBytes = p.ImageBytes, SectorBytes = p.ImageSectorBytes, RegionSize = p.RegionSize, Regions = p.Regions.ToList() };
            var protect = Protected(settings); protect.AddRange(Parity.FilesOf(p.Backup).Select(x => new ProtectedPath(x, "the backup's recovery data")));
            progress?.Invoke(new EngineProgress { Phase = "elevate", Text = "Asking Windows for administrator permission to write the drive..." });
            var w = RawDiskClient.Write(p.TargetDisk, image, protect, cancelled,
                (phase, d, t) => progress?.Invoke(new EngineProgress { Phase = phase, Text = phase == "readback" ? "Reading the drive back..." : "Writing the drive image...", Done = phase == "readback" ? t + d : d, Total = t * 2 }),
                () => rec.State("writing"));
            rec.Set("result", new JObj { { "state", w.State }, { "text", w.Text }, { "written", w.Written }, { "checked", w.Checked }, { "bad_regions", w.BadRegions.Cast<object>().ToList() }, { "exit_code", w.ExitCode.HasValue ? (object)(long)w.ExitCode.Value : null } });
            rec.State(w.State);
            r.DiskState = w.State; r.NothingWritten = w.NothingWritten; r.Ok = w.Ok; r.Bytes = w.Written;
            r.Text = w.Ok ? "The drive image was written to " + p.TargetText + " and read back from the drive: every region matches the backup's recorded checksum." : w.Text;
            foreach (var b in w.BadRegions) r.Problems.Add("Region " + b + " reads back differently from the backup.");
            return r;
        }

        /// <summary>whole-drive restores onto a connected drive that did not finish as written and read back, newest first, unless a later one
        /// onto the same drive did (the drive may hold part of an image)</summary>
        public static List<(DateTime? when, string target, string state, string text)> UnfinishedDiskRestores(Settings s)
        {
            var all = new List<(DateTime? when, string key, string target, string state, string text)>();
            if (s == null || string.IsNullOrEmpty(s.RecordsFolder) || !Directory.Exists(s.RecordsFolder)) return new List<(DateTime?, string, string, string)>();
            foreach (var f in Directory.GetFiles(s.RecordsFolder, "disk-restore-*.json"))
            {
                var d = Record.Load(f); if (d?.S("kind") != "disk-restore") continue;
                var t = DiskIdentity.FromJson(d.O("target")); var key = t == null ? f : t.ToSource().Key;
                all.Add((Browse.BackupList.Parse(d.S("created_utc")), key, d.S("target_text") ?? "a drive", d.S("state"), d.O("result")?.S("text")));
            }
            var bad = new HashSet<string> { "writing", "canceled-incomplete", "stopped-incomplete", "interrupted", "written-not-checked", "read-back-differs" };
            return all.Where(x => bad.Contains(x.state) && !all.Any(y => y.key == x.key && y.state == "complete" && (y.when ?? DateTime.MinValue) > (x.when ?? DateTime.MinValue)))
                      .OrderByDescending(x => x.when ?? DateTime.MinValue).Select(x => (x.when, x.target, x.state, x.text)).ToList();
        }
    }
}
