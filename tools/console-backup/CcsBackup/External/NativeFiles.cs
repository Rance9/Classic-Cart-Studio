// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Reading a folder tree on a USB drive or SD card through Windows' ordinary file access, read only, without following links:
//   - every folder and file is opened by its "\\?\" path (no length limit, names with a trailing dot or space kept exactly) WITH the
//     open-the-link-itself flag, so a junction or symbolic link (a reparse point) is never followed: it is recorded and skipped;
//   - each opened item's final path, as Windows resolves it (GetFinalPathNameByHandle, by volume name), must be exactly the expected path
//     under the source's root: an item replaced by a link after the folder was listed, or reached through one, is refused;
//   - a folder is listed through its own open handle (GetFileInformationByHandleEx), with each child's size, times (creation, last write,
//     last access and change, as Windows reports them), attributes and extended-attribute size;
//   - files are opened for reading, denying other programs write access while they are read (when another program already has a file open
//     for writing, it is opened allowing that and checked for change before and after);
//   - nothing is ever opened for writing, and no attribute or time is changed. (Windows itself may update a file's last-access date when it
//     is read, depending on its own setting; the times recorded are the ones from before the read.)
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CcsBackup.External
{
    public sealed class DirItem
    {
        public string Name; public long Size, Created, Accessed, Modified, Changed; public uint Attributes, EaSize;
        public bool IsFolder => (Attributes & 0x10) != 0;
        public bool IsLink => (Attributes & 0x400) != 0;
    }

    public sealed class Stat { public long Size, Created, Accessed, Modified, Changed; public uint Attributes; public uint VolumeSerial; public int Links; }

    public static class NativeFiles
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetFileInformationByHandleEx(SafeFileHandle h, int cls, byte[] buf, int size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetFileInformationByHandle(SafeFileHandle h, byte[] info);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern int GetFinalPathNameByHandle(SafeFileHandle h, StringBuilder path, int len, int flags);
        const uint GenericRead = 0x80000000, ListDirectory = 0x1, ReadAttributes = 0x80, Synchronize = 0x100000;
        const uint ShareRead = 1, ShareAll = 7, OpenExisting = 3;
        const uint BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000, SequentialScan = 0x08000000;
        const int FileBasicInfo = 0, FileStandardInfo = 1, FileFullDirectoryInfo = 14, FileFullDirectoryRestartInfo = 15;
        const int NoMoreFiles = 18, SharingViolation = 32, VolumeNameGuid = 1;

        /// <summary>the "\\?\" form of a full path (a network path becomes "\\?\UNC\...")</summary>
        public static string Long(string full)
        {
            if (full.StartsWith(@"\\?\", StringComparison.Ordinal)) return full;
            if (full.StartsWith(@"\\", StringComparison.Ordinal)) return @"\\?\UNC\" + full.Substring(2);
            return @"\\?\" + full;
        }

        public static SafeFileHandle OpenFolder(string longPath)
        {
            var h = CreateFile(longPath, ListDirectory | ReadAttributes | Synchronize, ShareAll, IntPtr.Zero, OpenExisting, BackupSemantics | OpenReparsePoint, IntPtr.Zero);
            if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            return h;
        }

        /// <summary>a file opened for reading; shared: true when another program had it open for writing, so it was opened allowing that</summary>
        public static SafeFileHandle OpenFile(string longPath, out bool shared)
        {
            shared = false;
            var h = CreateFile(longPath, GenericRead, ShareRead, IntPtr.Zero, OpenExisting, OpenReparsePoint | SequentialScan, IntPtr.Zero);
            if (!h.IsInvalid) return h;
            int e = Marshal.GetLastWin32Error();
            if (e != SharingViolation) throw new Win32Exception(e);
            h = CreateFile(longPath, GenericRead, ShareAll, IntPtr.Zero, OpenExisting, OpenReparsePoint | SequentialScan, IntPtr.Zero);
            if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            shared = true; return h;
        }

        /// <summary>the item's path as Windows resolves it, by volume name ("\\?\Volume{...}\folder\file"); null when Windows does not say</summary>
        public static string FinalPath(SafeFileHandle h)
        {
            var sb = new StringBuilder(1024); int n = GetFinalPathNameByHandle(h, sb, sb.Capacity, VolumeNameGuid);
            if (n > sb.Capacity) { sb = new StringBuilder(n + 1); n = GetFinalPathNameByHandle(h, sb, sb.Capacity, VolumeNameGuid); }
            return n <= 0 || n > sb.Capacity ? null : sb.ToString();
        }

        public static Stat Of(SafeFileHandle h)
        {
            var b = new byte[40]; var st = new byte[24]; var bh = new byte[52];
            if (!GetFileInformationByHandleEx(h, FileBasicInfo, b, b.Length)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!GetFileInformationByHandleEx(h, FileStandardInfo, st, st.Length)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var s = new Stat { Created = BitConverter.ToInt64(b, 0), Accessed = BitConverter.ToInt64(b, 8), Modified = BitConverter.ToInt64(b, 16), Changed = BitConverter.ToInt64(b, 24), Attributes = BitConverter.ToUInt32(b, 32), Size = BitConverter.ToInt64(st, 8), Links = BitConverter.ToInt32(st, 16) };
            if (GetFileInformationByHandle(h, bh)) s.VolumeSerial = BitConverter.ToUInt32(bh, 28);
            return s;
        }

        /// <summary>every child of an open folder (not "." or ".."), as Windows lists it</summary>
        public static List<DirItem> List(SafeFileHandle folder)
        {
            var list = new List<DirItem>(); var buf = new byte[64 * 1024]; bool first = true;
            while (true)
            {
                if (!GetFileInformationByHandleEx(folder, first ? FileFullDirectoryRestartInfo : FileFullDirectoryInfo, buf, buf.Length))
                {
                    int e = Marshal.GetLastWin32Error(); if (e == NoMoreFiles) break; throw new Win32Exception(e);
                }
                first = false; int p = 0;
                while (true)
                {
                    int next = BitConverter.ToInt32(buf, p); int nameLen = BitConverter.ToInt32(buf, p + 60);
                    var name = Encoding.Unicode.GetString(buf, p + 68, nameLen);
                    if (name != "." && name != "..")
                        list.Add(new DirItem { Name = name, Created = BitConverter.ToInt64(buf, p + 8), Accessed = BitConverter.ToInt64(buf, p + 16), Modified = BitConverter.ToInt64(buf, p + 24), Changed = BitConverter.ToInt64(buf, p + 32), Size = BitConverter.ToInt64(buf, p + 40), Attributes = BitConverter.ToUInt32(buf, p + 56), EaSize = BitConverter.ToUInt32(buf, p + 64) });
                    if (next == 0) break; p += next;
                    if (p >= buf.Length) break;
                }
            }
            return list;
        }

        /// <summary>the attribute words people see ("read-only, hidden"); compression, encryption and sparse are named too</summary>
        public static string AttributeWords(uint a)
        {
            var l = new List<string>();
            if ((a & 0x1) != 0) l.Add("read-only"); if ((a & 0x2) != 0) l.Add("hidden"); if ((a & 0x4) != 0) l.Add("system"); if ((a & 0x20) != 0) l.Add("archive");
            if ((a & 0x200) != 0) l.Add("sparse"); if ((a & 0x800) != 0) l.Add("compressed"); if ((a & 0x4000) != 0) l.Add("encrypted"); if ((a & 0x1000) != 0) l.Add("offline");
            return string.Join(", ", l);
        }

        public static DateTime? Time(long fileTime) => fileTime <= 0 || fileTime > DateTime.MaxValue.Ticks - 504911232000000000L ? (DateTime?)null : DateTime.FromFileTimeUtc(fileTime);
    }
}
