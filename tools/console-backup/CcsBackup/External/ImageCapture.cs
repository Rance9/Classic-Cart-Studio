// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Whole drive image mode (ExternalFormat.cs): every byte of the drive, in region members, then a file system index made from the regions
// AS STORED (read back from the file being written), so the index describes exactly what the backup holds.
//   Regions   the drive read from its first byte to its last in fixed-size regions (ExternalFormat.RegionSizeFor), each one member,
//             hashed while it streams (and in 1,048,576-byte pieces for the maps). The source is a drive image file the person chose,
//             opened so that no other program can write to it while it is read; its size and last-write time are compared before and
//             after (a change makes the backup's scope incomplete). A connected drive read whole needs administrator permission and is
//             behind the closed StorageRawReads boundary (RawDisk.cs).
//   Index     the layout (PartitionTable) and, for each FAT12/16/32 or ext2/3/4 file system, every folder, file and link with its size,
//             times, attributes or mode and owner, and for each regular file its extents (where its bytes lie in the drive image; a hole in
//             an ext file is -1, read as zeros) and its SHA-256, read through those extents from the stored regions. exFAT and NTFS file
//             systems, and those not recognized, are kept exactly and named "raw only": their files are not listed (PartitionInfo).
//   RegionView  the stored regions of one source read as one drive (the index, the Backup Browser, restore and salvage use it).
using CcsBackup.Archive;
using CcsBackup.Engine;
using CcsBackup.Ext4;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CcsBackup.External
{
    /// <summary>One source's regions in a backup file, read as one drive (read-only, seekable). pieces (optional): for a damaged file, the
    /// place to read each region from instead (Repair's patched reading).</summary>
    public sealed class RegionView : Stream
    {
        readonly Stream s; readonly long regionSize, len; readonly long[] dataAt; long pos;
        public RegionView(Stream file, long regionSize, IList<long> regionDataOffsets, long length) { s = file; this.regionSize = regionSize; dataAt = regionDataOffsets.ToArray(); len = length; }
        public override int Read(byte[] b, int o, int c)
        {
            if (pos >= len || c <= 0) return 0;
            int k = (int)(pos / regionSize); long within = pos % regionSize;
            int n = (int)Math.Min(c, Math.Min(regionSize - within, len - pos));
            s.Position = dataAt[k] + within; int got = 0;
            while (got < n) { int r = s.Read(b, o + got, n - got); if (r <= 0) throw new EndOfStreamException("the backup file ended inside a region"); got += r; }
            pos += n; return n;
        }
        public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
        public override long Length => len;
        public override long Position { get => pos; set { if (value < 0 || value > len) throw new IOException("outside the drive image"); pos = value; } }
        public override long Seek(long o, SeekOrigin origin) { Position = origin == SeekOrigin.Begin ? o : origin == SeekOrigin.Current ? pos + o : len + o; return pos; }
        public override void Flush() { }
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    public static class ImageCapture
    {
        /// <summary>the regions and the index of one image source; true when the source stayed unchanged and every region was read whole</summary>
        internal static bool Write(StorageWriter w, string src, string imagePath, StorageOptions opt, FileCapture.Counters c, JObj summary)
        {
            bool clean = true; long len; DateTime t0; var regions = new List<ZipEntryInfo>(); long R;
            using (var f = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
            {
                len = f.Length; t0 = File.GetLastWriteTimeUtc(imagePath); R = ExternalFormat.RegionSizeFor(len);
                var buf = new byte[Streams.Buffer];
                for (long at = 0, k = 1; at < len; at += R, k++)
                {
                    long n = Math.Min(R, len - at);
                    var e = w.Add(ExternalFormat.Region(src, (int)k), false, s =>
                    {
                        long left = n; f.Position = at;
                        while (left > 0)
                        {
                            if (opt.Cancelled()) throw new OperationCanceledException();
                            int r = f.Read(buf, 0, (int)Math.Min(buf.Length, left)); if (r <= 0) throw new IOException("the drive image became shorter while it was read");
                            s.Write(buf, 0, r); left -= r; c.Done += r; opt.Say("read", "Copying the drive image...", c.Done, c.Total);
                        }
                    }, opt.Cancelled);
                    regions.Add(e); w.MaybeMap();
                    opt.DuringFile?.Invoke("region " + k);
                }
            }
            var fi = new FileInfo(imagePath);
            if (fi.Length != len || fi.LastWriteTimeUtc != t0) { clean = false; c.Problems.Add("the drive image file changed while it was read"); summary.Set("changed", "The drive image file changed while it was read, so this copy may mix two states of it."); }
            c.Bytes += len;
            // the index, from the regions as they are now in the file being written
            w.File.Flush();
            using (var rf = new FileStream(w.File.Name, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.RandomAccess))
            {
                var view = new RegionView(rf, R, regions.Select(x => x.DataOffset).ToList(), len);
                var layout = PartitionTable.Read(view);
                var chunk = new List<JObj>(); int idxNo = 0; long chunkBytes = 0, files = 0, folders = 0, unreadable = 0;
                void Flush(bool force)
                {
                    if (chunk.Count == 0 || !force && chunk.Count < ExternalFormat.InventoryChunk && chunkBytes < 3_000_000) return;
                    w.AddJson(ExternalFormat.Index(src, ++idxNo), new JObj { { "schema", "ccs-storage-index/1" }, { "source", src }, { "chunk", (long)idxNo }, { "entries", chunk.ToList() } }, opt.Cancelled);
                    chunk.Clear(); chunkBytes = 0; w.MaybeMap();
                }
                void Add(JObj j, int extents) { chunk.Add(j); chunkBytes += 300 + extents * 40L; Flush(false); }
                var notes = new Dictionary<int, string>(); var journal = new Dictionary<int, string>();
                foreach (var p in layout.Parts.Where(x => x.Browsable))
                {
                    try
                    {
                        if (p.FileSystem.StartsWith("FAT", StringComparison.Ordinal))
                        {
                            var fat = new Fat(view, p.Offset, p.Length); if (p.Label == null) p.Label = fat.Label;
                            foreach (var e in fat.List(opt.Cancelled))
                            {
                                var j = new JObj { { "partition", (long)p.Number }, { "type", e.Directory ? "folder" : "file" }, { "path", e.Path }, { "short_name", e.ShortName }, { "attributes", (long)e.Attributes },
                                    { "modified", Fat.Local(e.Modified) }, { "created", Fat.Local(e.Created) }, { "accessed", Fat.Local(e.Accessed) } };
                                if (e.Directory) { folders++; Add(j, 0); continue; }
                                j.Set("size", e.Size);
                                if (e.Problem != null) { j.Set("problem", e.Problem); unreadable++; Add(j, 0); continue; }
                                try
                                {
                                    var ext = fat.Extents(e); var h = new HashingSink(null, null, opt.Cancelled); fat.ReadFile(e, h, opt.Cancelled); h.Finish();
                                    j.Set("extents", ext.Select(x => (object)new List<object> { x.offset, x.length }).ToList()); j.Set("sha256", h.Sha256); files++;
                                    Add(j, ext.Count);
                                }
                                catch (FatException ex) { j.Set("problem", ex.Message); unreadable++; Add(j, 0); }
                            }
                        }
                        else
                        {
                            var fs = new Ext4Reader(new SubStream(view, p.Offset, p.Length)); if (p.Label == null && !string.IsNullOrEmpty(fs.VolumeName)) p.Label = fs.VolumeName;
                            // never replayed here: the image is read exactly as it was stored
                            if (fs.Condition == "needs-recovery") journal[p.Number] = "Its journal records a change that was not finished when the drive was copied; its files are listed exactly as the image holds them, without finishing that change.";
                            foreach (var e in fs.List())
                            {
                                if (opt.Cancelled()) throw new OperationCanceledException();
                                var type = e.Type == 'f' ? "file" : e.Type == 'd' ? "folder" : e.Type == 'l' ? "link" : "special";
                                var j = new JObj { { "partition", (long)p.Number }, { "type", type }, { "path", e.Path }, { "mode", (long)e.Mode }, { "uid", (long)e.Uid }, { "gid", (long)e.Gid }, { "mtime", e.Mtime }, { "links", (long)e.Links }, { "inode", (long)e.Inode } };
                                if (e.Type == 'l') j.Set("link_target", e.LinkTarget);
                                if (e.Type == 'd') { folders++; Add(j, 0); continue; }
                                if (e.Type != 'f') { j.Set("note", "a special file (" + e.Type + "): listed, not copied"); Add(j, 0); continue; }
                                j.Set("size", e.Size);
                                if (e.Unreadable) { j.Set("problem", e.Note ?? "it cannot be read on the PC"); unreadable++; Add(j, 0); continue; }
                                try
                                {
                                    var blocks = fs.Blocks(fs.Inode(e.Inode)); var ext = new List<(long off, long len)>(); long left = e.Size;
                                    foreach (var b in blocks)
                                    {
                                        if (left <= 0) break; long n = Math.Min(fs.BlockSize, left); long at = b == 0 ? -1 : p.Offset + b * fs.BlockSize;
                                        if (ext.Count > 0 && (at < 0 ? ext[ext.Count - 1].off < 0 : ext[ext.Count - 1].off >= 0 && ext[ext.Count - 1].off + ext[ext.Count - 1].len == at)) ext[ext.Count - 1] = (ext[ext.Count - 1].off, ext[ext.Count - 1].len + n);
                                        else ext.Add((at, n));
                                        left -= n;
                                    }
                                    var h = new HashingSink(null, null, opt.Cancelled); fs.ReadFile(e.Inode, h, e.Size); h.Finish();
                                    j.Set("extents", ext.Select(x => (object)new List<object> { x.off, x.len }).ToList()); j.Set("sha256", h.Sha256); files++;
                                    Add(j, ext.Count);
                                }
                                catch (Ext4Exception ex) { j.Set("problem", ex.Message); unreadable++; Add(j, 0); }
                            }
                        }
                    }
                    catch (Exception ex) when (ex is FatException || ex is Ext4Exception || ex is IOException)
                    { notes[p.Number] = "Its files could not be listed: " + ex.Message + ". The partition is kept exactly in the image."; }
                }
                Flush(true);
                var lj = layout.ToJson();
                foreach (var pj in ((List<object>)lj["partitions"]).OfType<JObj>()) { var n = (long)pj["number"]; if (notes.TryGetValue((int)n, out var note)) { pj.Set("browsable", false); pj.Set("not_browsable", note); } if (journal.TryGetValue((int)n, out var jn)) pj.Set("note", jn); }
                w.AddJson(ExternalFormat.Layout(src), new JObj { { "schema", "ccs-storage-layout/1" }, { "source", src }, { "bytes", len }, { "region_size", R }, { "regions", (long)regions.Count }, { "index_chunks", (long)idxNo }, { "layout", lj } }, opt.Cancelled);
                w.MaybeMap();
                summary.Set("files", files); summary.Set("folders", folders); summary.Set("unreadable_files", unreadable); summary.Set("region_size", R); summary.Set("regions", (long)regions.Count);
                summary.Set("layout", layout.Scheme); summary.Set("partitions", layout.Parts.Select(p => (object)(p.Number + ": " + p.FileSystem + (p.Label != null ? " \"" + p.Label + "\"" : "") + ", " + Units.Gb(p.Length) + (p.Browsable && !notes.ContainsKey(p.Number) ? "" : " (raw only: " + (notes.TryGetValue(p.Number, out var nn) ? nn : p.NotBrowsable) + ")"))).ToList());
                c.Files += files; c.Folders += folders;
            }
            return clean;
        }
    }
}
