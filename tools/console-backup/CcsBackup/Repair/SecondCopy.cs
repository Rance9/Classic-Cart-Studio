// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Second copies.
//   SecondCopy.Make  a verified copy of a good backup in a folder the person chooses: the backup checked in full first (a damaged one is
//                    not copied: the repair routes make a new good file from it), copied as <name>.zip.partial (created new, hashed
//                    while it is written), flushed, read back in full and compared, then renamed (never over a file). Which physical
//                    drive each file is on is asked of Windows (the volume's disk extents): same drive, different drive, or "could not be
//                    determined" (a network folder, or Windows does not say), said plainly; a copy on the same drive does not protect
//                    against that drive failing. Cancel or failure deletes the unfinished copy. The copy's state is its own: it is
//                    "copied" only when it is complete and matches, and the first copy's state is never changed by it.
//   Merge.Run        one new good backup file from two (or more) damaged copies of the same backup, each 1,048,576-byte piece taken from
//                    whichever copy holds it with its recorded checksum (Rebuild.cs). Damaged copies are never overwritten.
using CcsBackup.Archive;
using CcsBackup.Engine;
using CcsBackup.Library;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace CcsBackup.Repair
{
    public static class Merge
    {
        /// <summary>A new, complete backup file in outputFolder from two (or more) damaged copies of the SAME backup. Pieces damaged in every
        /// copy are unrecoverable, and then nothing is written. The copies are only read.</summary>
        public static RepairResult Run(IList<string> copies, string outputFolder, RepairOptions options = null) =>
            Runner.Run("merge", copies, new List<string>(), outputFolder, options);
    }

    /// <summary>Which physical drive a file or folder is on, as Windows reports it.</summary>
    public static class Drives
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetVolumePathName(string file, StringBuilder volume, int len);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetVolumeNameForVolumeMountPoint(string mount, StringBuilder name, int len);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr inBuf, int inSize, byte[] outBuf, int outSize, out int returned, IntPtr overlapped);
        const uint IoctlVolumeDiskExtents = 0x00560000, ShareReadWrite = 3, OpenExisting = 3;

        /// <summary>the disk numbers the volume holding path spans (sorted), or null when that cannot be determined (a network folder,
        /// a path Windows cannot resolve, or a volume that does not report its disks). Needs no administrator rights: the volume is opened
        /// without read or write access.</summary>
        public static int[] PhysicalDisks(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (full.StartsWith(@"\\", StringComparison.Ordinal) && !full.StartsWith(@"\\?\", StringComparison.Ordinal)) return null;
                var vp = new StringBuilder(1024); if (!GetVolumePathName(full, vp, vp.Capacity)) return null;
                var vn = new StringBuilder(1024); if (!GetVolumeNameForVolumeMountPoint(vp.ToString(), vn, vn.Capacity)) return null;
                using (var h = CreateFile(vn.ToString().TrimEnd('\\'), 0, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) return null;
                    var buf = new byte[8 + 24 * 32];
                    if (!DeviceIoControl(h, IoctlVolumeDiskExtents, IntPtr.Zero, 0, buf, buf.Length, out _, IntPtr.Zero)) return null;
                    int n = BitConverter.ToInt32(buf, 0); if (n <= 0 || n > 32) return null;
                    return Enumerable.Range(0, n).Select(i => BitConverter.ToInt32(buf, 8 + 24 * i)).Distinct().OrderBy(x => x).ToArray();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is NotSupportedException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException) { return null; }
        }

        /// <summary>same | different | unknown, with the plain sentence for it</summary>
        public static string Compare(string a, string b, out string text)
        {
            var x = PhysicalDisks(a); var y = PhysicalDisks(b);
            if (x == null || y == null) { text = "Whether the copy is on the same physical drive as the backup could not be determined."; return "unknown"; }
            if (x.Intersect(y).Any()) { text = "The copy is on the same physical drive as the backup. If that drive fails, both copies can be lost; a copy on another drive protects better."; return "same"; }
            text = "The copy is on a different physical drive from the backup."; return "different";
        }
    }

    public static class SecondCopy
    {
        /// <summary>A verified copy of a good backup file in destinationFolder (a new name when one is taken); the backup is only read.</summary>
        public static RepairResult Make(string source, string destinationFolder, RepairOptions opt = null)
        {
            opt = opt ?? new RepairOptions();
            var rep = new RepairReport("second-copy", "Make a second copy"); var res = new RepairResult { Report = rep }; string partial = null;
            long len0 = -1; DateTime t0 = default(DateTime);
            RepairResult End(string outcome, string summary)
            {
                rep.Outcome = res.Outcome = outcome; rep.Summary = summary; rep.FinishedUtc = DateTime.UtcNow;
                if (partial != null && File.Exists(partial)) { try { File.Delete(partial); } catch (IOException ex) { rep.Errors.Add("The unfinished copy " + partial + " could not be removed: " + ex.Message); } }
                if (len0 >= 0) { var fi = new FileInfo(source); rep.Originals.Add(new JObj { { "file", source }, { "unchanged", fi.Exists && fi.Length == len0 && fi.LastWriteTimeUtc == t0 } }); }
                if (opt.SaveReport && outcome == "copied")
                {
                    try { (res.ReportJson, res.ReportText) = rep.Save(destinationFolder, Path.GetFileNameWithoutExtension(res.OutputPath) + " (copy report)"); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { rep.Errors.Add("The report could not be saved: " + ex.Message); }
                }
                return res;
            }
            try
            {
                if (string.IsNullOrEmpty(source) || !File.Exists(source)) return End("refused", "The backup file was not found.");
                if (string.IsNullOrEmpty(destinationFolder)) return End("refused", "No folder was chosen for the copy.");
                source = Path.GetFullPath(source); var fi0 = new FileInfo(source); len0 = fi0.Length; t0 = fi0.LastWriteTimeUtc;
                Directory.CreateDirectory(destinationFolder);
                // 1. the backup itself, checked in full
                opt.Say("verify", "Checking the backup before copying it...");
                var v = External.AnyBackup.Verify(source, null, opt.Cancelled, (d, t) => opt.Say("verify", "Checking the backup before copying it...", d, t));
                rep.Original = new JObj { { "role", "backup" }, { "label", "the backup file" }, { "file", Path.GetFileName(source) }, { "folder", Path.GetDirectoryName(source) }, { "bytes", fi0.Length }, { "sha256_as_read", v.ArchiveSha256 },
                    { "backup_id", v.Manifest?.S("backup_id") }, { "console_tag", v.ConsoleTag }, { "made_utc", v.Manifest?.S("completed_utc") }, { "condition", v.Ok ? "it passed the full check" + (v.RestoreReady ? " and is restore-ready" : "") : "it did not pass its check: " + string.Join(" ", v.Problems) } };
                if (!v.Ok) return End("refused", "This backup did not pass its check, so it was not copied (" + v.Problems[0] + "). A damaged backup is repaired into a new file instead (Repair).");
                // 2. where the copy goes
                res.SameDrive = Drives.Compare(source, destinationFolder, out var driveText);
                rep.Notes.Add(driveText);
                long free = Durable.FreeBytes(destinationFolder);
                if (free >= 0 && free < v.Bytes + CaptureEngine.Reserve) return End("refused", "There is not enough free space in the chosen folder: about " + Units.Gb(v.Bytes + CaptureEngine.Reserve) + " is needed and " + Units.Gb(free) + " is free. Nothing was copied.");
                // 3. copied (hashed on the way), flushed, read back in full and compared
                partial = CaptureEngine.Unique(destinationFolder, Path.GetFileNameWithoutExtension(source), BackupFormat.Partial);
                var h = new HashingSink(null);
                using (var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
                using (var dst = Durable.CreateNew(partial))
                {
                    var buf = new byte[Streams.Buffer]; long done = 0; int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (opt.Cancelled()) throw new OperationCanceledException();
                        h.Write(buf, 0, n); dst.Write(buf, 0, n); done += n; opt.Say("copy", "Copying...", done, v.Bytes);
                    }
                    Durable.Flush(dst);
                }
                h.Finish();
                if (h.Sha256 != v.ArchiveSha256 || h.Count != v.Bytes) return End("failed", "The backup file changed while it was being copied; the copy was deleted.");
                opt.Say("copy", "Reading the copy back...");
                var back = Durable.HashFile(partial, opt.Cancelled, d => opt.Say("copy", "Reading the copy back...", d, v.Bytes));
                if (back.sha256 != v.ArchiveSha256 || back.bytes != v.Bytes) return End("failed", "The copy did not read back the same as the backup, so it was deleted. Try another folder or drive.");
                var final = partial.Substring(0, partial.Length - ".partial".Length);
                Durable.Publish(partial, final); partial = null;
                res.OutputPath = final; res.RestorePoint = true;
                rep.Methods.Add("The backup was checked in full first (every part, its checksums and the console facts).");
                rep.Methods.Add("It was copied byte for byte, the copy flushed to the drive, then read back in full: its SHA-256 equals the checked backup's.");
                rep.Output = new JObj { { "path", final }, { "bytes", back.bytes }, { "sha256", back.sha256 }, { "backup_id", v.Manifest?.S("backup_id") }, { "same_drive", res.SameDrive },
                    { "checked", "It is byte-for-byte the backup, which passed the full check" + (v.RestoreReady ? " and is restore-ready" : "") + "." }, { "container", "The same file, byte for byte (the same SHA-256)." } };
                return End("copied", "A verified second copy was made: " + Path.GetFileName(final) + ". " + driveText);
            }
            catch (OperationCanceledException) { rep.Canceled = true; return End("canceled", "Canceled. The unfinished copy was deleted; the backup is unchanged."); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { rep.Errors.Add(ex.Message); return End("failed", "The copy could not be made: " + ex.Message + ". The unfinished copy was deleted; the backup is unchanged."); }
        }
    }
}
