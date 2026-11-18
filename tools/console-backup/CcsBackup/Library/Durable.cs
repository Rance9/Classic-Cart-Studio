// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Durable file steps the records and the publishing order rely on. Each one is complete on disk before it returns, so a process that is
// closed, crashes or is ended after it leaves a known state, and one that is ended before it leaves the earlier state:
//   WriteAtomic   a small file replaced whole: a new temporary file written through and flushed, then moved over the old one with
//                 MOVEFILE_WRITE_THROUGH (a reader sees the old or the new file, never a part)
//   CreateNew     a new file that must not exist (an output is never written over)
//   Publish       a finished file renamed to its final name, never over an existing file
//   Flush         FlushFileBuffers on a written file before it is checked or published
// A move Windows refuses only because another program has the file open for a moment (access denied, a sharing or lock violation: seen on
// this PC in a OneDrive-synced folder, 3 of 400 record saves, never in a local folder) is tried again a few times, about 2.3 seconds in
// all, before the same refusal is reported; nothing else is retried, and the temporary file stays until the move succeeds or fails.
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CcsBackup.Library
{
    public static class Durable
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool MoveFileEx(string from, string to, int flags);
        const int ReplaceExisting = 0x1, WriteThrough = 0x8;

        public static void WriteAtomic(string path, byte[] bytes)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path)); Directory.CreateDirectory(dir);
            var tmp = Path.Combine(dir, "." + Path.GetFileName(path) + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            using (var f = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { f.Write(bytes, 0, bytes.Length); f.Flush(true); }
            if (!Move(tmp, path, ReplaceExisting | WriteThrough, out var e)) { try { File.Delete(tmp); } catch { } throw new IOException("could not save " + Path.GetFileName(path) + ": " + new Win32Exception(e).Message); }
        }

        const int AccessDenied = 5, SharingViolation = 32, LockViolation = 33;
        static readonly int[] RetryWaitsMs = { 50, 100, 200, 300, 400, 500, 750 };
        /// <summary>number of moves that succeeded only after Windows first refused them for a moment (for the tests and probes)</summary>
        public static int RetriedMoves;

        /// <summary>MoveFileEx, tried again after a brief refusal by another program holding the file (see the header); error: the last one</summary>
        static bool Move(string from, string to, int flags, out int error)
        {
            for (int i = 0; ; i++)
            {
                if (MoveFileEx(from, to, flags)) { if (i > 0) System.Threading.Interlocked.Increment(ref RetriedMoves); error = 0; return true; }
                error = Marshal.GetLastWin32Error();
                if (i >= RetryWaitsMs.Length || (error != AccessDenied && error != SharingViolation && error != LockViolation)) return false;
                System.Threading.Thread.Sleep(RetryWaitsMs[i]);
            }
        }

        public static void WriteAtomic(string path, string text) => WriteAtomic(path, new UTF8Encoding(false).GetBytes(text));

        public static FileStream CreateNew(string path) => new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 1 << 20, FileOptions.None);

        public static void Publish(string from, string to)
        {
            if (File.Exists(to)) throw new IOException("a file named " + Path.GetFileName(to) + " already exists; nothing was replaced");
            if (!Move(from, to, WriteThrough, out var e)) throw new IOException("could not give the backup its final name: " + new Win32Exception(e).Message);
        }

        public static void Flush(FileStream f) { f.Flush(true); }

        /// <summary>SHA-256 of a closed file, read independently from the start (no cached digest)</summary>
        public static (string sha256, long bytes) HashFile(string path, Func<bool> cancelled = null, Action<long> progress = null)
        {
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
            {
                var h = new HashingSink(null, progress, cancelled);
                Streams.Copy(f, h, -1, cancelled); h.Finish(); return (h.Sha256, h.Count);
            }
        }

        public static long FreeBytes(string folder)
        {
            try { var root = Path.GetPathRoot(Path.GetFullPath(folder)); return new DriveInfo(root).AvailableFreeSpace; } catch { return -1; }
        }

        /// <summary>the volume a folder is on (its root and serial number), to tell whether two copies share one disk</summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetVolumePathName(string file, StringBuilder volume, int len);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetVolumeInformation(string root, StringBuilder name, int nameLen, out uint serial, out uint maxComp, out uint flags, StringBuilder fs, int fsLen);
        public static string VolumeId(string folder)
        {
            try
            {
                var sb = new StringBuilder(260);
                if (!GetVolumePathName(Path.GetFullPath(folder), sb, sb.Capacity)) return null;
                if (!GetVolumeInformation(sb.ToString(), null, 0, out var serial, out _, out _, null, 0)) return sb.ToString().ToUpperInvariant();
                return sb.ToString().ToUpperInvariant() + "#" + serial.ToString("x8");
            }
            catch { return null; }
        }
    }
}
