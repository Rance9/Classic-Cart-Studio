// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// This PC's drives, through Windows' own functions.
//   WindowsDiskQueries    reads what Windows reports and nothing on the drives (the window uses it without administrator permission, and
//                         the elevated step uses it again): every disk device interface Windows lists (SetupDiGetClassDevs, the disk
//                         interface class) with its number (IOCTL_STORAGE_GET_DEVICE_NUMBER); each drive's maker, model, serial number, how
//                         it is connected and whether its media is removable (IOCTL_STORAGE_QUERY_PROPERTY) and its size and sector size
//                         (IOCTL_DISK_GET_DRIVE_GEOMETRY_EX), each asked of the drive opened with no read or write access; the volumes with
//                         a part on a drive (FindFirstVolume, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, GetVolumePathNamesForVolumeName,
//                         GetVolumeInformation); whether a path or a folder above it is a link, junction or mount point (FindFirstFile's
//                         reparse tag: a "name surrogate" names another place); a process's program file (QueryFullProcessImageName).
//   WindowsRawDiskSystem  the elevated step's own access: one drive opened by the device path Windows resolved (never a path from a
//                         request) for reading, or for reading and writing with write-through and no caching, in sector-aligned pieces from
//                         an aligned buffer; its exact length (IOCTL_DISK_GET_LENGTH_INFO); its volumes locked (FSCTL_LOCK_VOLUME) and, before a
//                         write, dismounted (FSCTL_DISMOUNT_VOLUME), as Windows requires before a program may write over a volume; after a
//                         write, Windows asked to read the drive's partition table again (IOCTL_DISK_UPDATE_PROPERTIES). It can be made only
//                         through Open, right after the StorageRawReads / StorageDiskWrites check (Gate.cs).
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace CcsBackup.External
{
    public interface IRawDisk : IDisposable
    {
        /// <summary>what this opened drive reports now (through the handle itself)</summary>
        DiskIdentity Identity();
        long Length { get; }
        int SectorBytes { get; }
        /// <summary>reads up to count bytes at offset (both whole sectors); returns how many were read (0 at the end)</summary>
        int Read(long offset, byte[] buffer, int count);
        /// <summary>writes exactly count bytes at offset (both whole sectors), or throws</summary>
        void Write(long offset, byte[] buffer, int count);
        void Flush();
        /// <summary>asks Windows to read the drive's partition table again</summary>
        void Refresh();
    }

    public interface IVolumeLock : IDisposable
    {
        /// <summary>the volumes that could not be locked (or dismounted), each with Windows' reason; empty when every one was</summary>
        List<string> NotLocked { get; }
    }

    public interface IRawDiskSystem : IDiskQueries
    {
        string Kind { get; }
        bool Elevated { get; }
        string SelfProgram { get; }
        IRawDisk Open(DiskEntry d, bool write);
        IVolumeLock Lock(IList<DiskVolume> volumes, bool dismount);
    }

    public sealed class WindowsDiskQueries : IDiskQueries
    {
        public static readonly WindowsDiskQueries Instance = new WindowsDiskQueries();
        internal static readonly Guid DiskInterface = new Guid("53f56307-b6bf-11d0-94f2-00a0c91efb8b");
        static readonly string[] BusNames = { "Unknown", "SCSI", "ATAPI", "ATA", "1394", "SSA", "Fibre", "USB", "RAID", "iSCSI", "SAS", "SATA", "SD", "MMC", "Virtual", "FileBackedVirtual", "Spaces", "NVMe", "SCM", "UFS" };

        public List<DiskEntry> Disks()
        {
            var list = new List<DiskEntry>(); var g = DiskInterface;
            var set = N.SetupDiGetClassDevsW(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);   // DIGCF_PRESENT | DIGCF_DEVICEINTERFACE
            if (set == IntPtr.Zero || set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                for (uint i = 0; i < 1024; i++)
                {
                    var data = new N.SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf(typeof(N.SP_DEVICE_INTERFACE_DATA)) };
                    if (!N.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref data)) break;
                    N.SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out int need, IntPtr.Zero);
                    if (need <= 0 || need > 65536) continue;
                    var buf = Marshal.AllocHGlobal(need);
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (!N.SetupDiGetDeviceInterfaceDetailW(set, ref data, buf, need, out _, IntPtr.Zero)) continue;
                        var path = Marshal.PtrToStringUni(buf + 4);
                        if (!IsDiskPath(path)) continue;
                        using (var h = N.CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                        {
                            if (h.IsInvalid) continue;
                            var o = new byte[12];
                            if (N.DeviceIoControl(h, N.IoctlStorageGetDeviceNumber, null, 0, o, o.Length, out int got, IntPtr.Zero) && got >= 8)
                                list.Add(new DiskEntry { Number = BitConverter.ToInt32(o, 4), Path = path });
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { N.SetupDiDestroyDeviceInfoList(set); }
            return list;
        }

        /// <summary>a disk device interface path as SetupAPI gives it ("\\?\...#{53f56307-...}")</summary>
        internal static bool IsDiskPath(string p) => p != null && p.StartsWith(@"\\?\", StringComparison.Ordinal) && p.IndexOf("{" + DiskInterface.ToString("D") + "}", StringComparison.OrdinalIgnoreCase) > 0 && p.IndexOf('/') < 0;

        public DiskIdentity Describe(DiskEntry d)
        {
            if (d == null || !IsDiskPath(d.Path)) return null;
            using (var h = N.CreateFileW(d.Path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (h.IsInvalid) return null;
                var id = DescribeHandle(h, d.Path);
                if (id != null && id.Number != d.Number) return null;   // the interface no longer has that number
                return id;
            }
        }

        internal static DiskIdentity DescribeHandle(SafeFileHandle h, string path)
        {
            var id = new DiskIdentity { Path = path };
            var n = new byte[12];
            if (!N.DeviceIoControl(h, N.IoctlStorageGetDeviceNumber, null, 0, n, n.Length, out int ng, IntPtr.Zero) || ng < 8) return null;
            id.Number = BitConverter.ToInt32(n, 4);
            var q = new byte[12]; var o = new byte[4096];   // STORAGE_PROPERTY_QUERY: StorageDeviceProperty (0), PropertyStandardQuery (0)
            if (N.DeviceIoControl(h, N.IoctlStorageQueryProperty, q, q.Length, o, o.Length, out int got, IntPtr.Zero) && got >= 32)
            {
                string At(int off) { int p = BitConverter.ToInt32(o, off); if (p <= 0 || p >= got) return null; int e = p; while (e < got && o[e] != 0) e++; var s = Encoding.ASCII.GetString(o, p, e - p).Trim(); return s.Length == 0 ? null : s; }
                id.Vendor = At(12); id.Product = At(16); id.Serial = At(24);
                int bus = BitConverter.ToInt32(o, 28); id.Bus = bus >= 0 && bus < BusNames.Length ? BusNames[bus] : "Other";
                id.RemovableMedia = o[10] != 0;
            }
            var g = new byte[256];
            if (N.DeviceIoControl(h, N.IoctlDiskGeometryEx, null, 0, g, g.Length, out int gg, IntPtr.Zero) && gg >= 32)
            { id.SectorBytes = BitConverter.ToInt32(g, 20); id.Bytes = BitConverter.ToInt64(g, 24); }
            return id;
        }

        public int[] DisksOf(string path) => Repair.Drives.PhysicalDisks(path);
        public int SystemDisk() => Devices.SystemDisk();

        public List<DiskVolume> VolumesOn(int disk)
        {
            var list = new List<DiskVolume>(); var sb = new StringBuilder(1024);
            var f = N.FindFirstVolumeW(sb, sb.Capacity);
            if (f == new IntPtr(-1)) return list;
            try
            {
                do
                {
                    var name = sb.ToString();
                    var ext = Extents(name.TrimEnd('\\'));
                    if (ext != null && ext.Any(x => x.disk == disk))
                    {
                        var v = new DiskVolume { Name = name, Bytes = ext.Where(x => x.disk == disk).Sum(x => x.length), SpansOtherDisks = ext.Any(x => x.disk != disk) };
                        var mb = new char[4096];
                        if (N.GetVolumePathNamesForVolumeNameW(name, mb, mb.Length, out int ml)) v.Mounts = new string(mb, 0, Math.Max(0, ml)).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                        var lab = new StringBuilder(261); var fs = new StringBuilder(261);
                        if (N.GetVolumeInformationW(name, lab, lab.Capacity, out _, out _, out _, fs, fs.Capacity)) { v.Label = lab.ToString(); v.FileSystem = fs.ToString(); }
                        list.Add(v);
                    }
                    sb.Clear();
                }
                while (N.FindNextVolumeW(f, sb, sb.Capacity));
            }
            finally { N.FindVolumeClose(f); }
            return list;
        }

        static List<(int disk, long length)> Extents(string volume)
        {
            using (var h = N.CreateFileW(volume, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (h.IsInvalid) return null;
                var buf = new byte[8 + 24 * 32];
                if (!N.DeviceIoControl(h, N.IoctlVolumeDiskExtents, null, 0, buf, buf.Length, out _, IntPtr.Zero)) return null;
                int n = BitConverter.ToInt32(buf, 0); if (n <= 0 || n > 32) return null;
                return Enumerable.Range(0, n).Select(i => (BitConverter.ToInt32(buf, 8 + 24 * i), BitConverter.ToInt64(buf, 8 + 24 * i + 16))).ToList();
            }
        }

        public bool Redirected(string path)
        {
            string p;
            try { p = Path.GetFullPath(path); } catch (Exception) { return true; }
            if (p.StartsWith(@"\\", StringComparison.Ordinal)) return true;   // a network or device path is never a place for these files
            var root = Path.GetPathRoot(p);
            for (var cur = p.TrimEnd('\\'); cur != null && cur.Length > root.TrimEnd('\\').Length; cur = Path.GetDirectoryName(cur))
            {
                var h = N.FindFirstFileW(cur, out var fd);
                if (h == new IntPtr(-1)) continue;   // not there (yet): nothing to follow
                N.FindClose(h);
                if ((fd.dwFileAttributes & 0x400) != 0 && (fd.dwReserved0 & 0x20000000) != 0) return true;   // FILE_ATTRIBUTE_REPARSE_POINT, a name surrogate
            }
            return false;
        }

        public string ProgramOf(int processId)
        {
            var h = N.OpenProcess(0x1000, false, processId);   // PROCESS_QUERY_LIMITED_INFORMATION
            if (h == IntPtr.Zero) return null;
            try { var sb = new StringBuilder(32768); int n = sb.Capacity; return N.QueryFullProcessImageNameW(h, 0, sb, ref n) ? sb.ToString(0, n) : null; }
            finally { N.CloseHandle(h); }
        }
    }

    /// <summary>The elevated step's access to one drive and its volumes. Made only through Open, right after the boundary check.</summary>
    public sealed class WindowsRawDiskSystem : IRawDiskSystem
    {
        WindowsRawDiskSystem() { }

        /// <summary>the only way to open a drive whole on this PC</summary>
        internal static IRawDiskSystem Open()
        {
            if (!Gate.StorageRawReads && !Gate.StorageDiskWrites) throw new GateClosedException(Gate.StorageRawText, "a whole drive");
            return new WindowsRawDiskSystem();
        }

        public string Kind => "windows";
        public bool Elevated
        {
            get
            {
                using (var id = WindowsIdentity.GetCurrent())
                    return N.GetTokenInformation(id.Token, 20, out int e, 4, out _) && e != 0;   // TokenElevation
            }
        }
        public string SelfProgram => Process.GetCurrentProcess().MainModule.FileName;
        public List<DiskEntry> Disks() => WindowsDiskQueries.Instance.Disks();
        public DiskIdentity Describe(DiskEntry d) => WindowsDiskQueries.Instance.Describe(d);
        public int[] DisksOf(string path) => WindowsDiskQueries.Instance.DisksOf(path);
        public int SystemDisk() => WindowsDiskQueries.Instance.SystemDisk();
        public List<DiskVolume> VolumesOn(int disk) => WindowsDiskQueries.Instance.VolumesOn(disk);
        public bool Redirected(string path) => WindowsDiskQueries.Instance.Redirected(path);
        public string ProgramOf(int processId) => WindowsDiskQueries.Instance.ProgramOf(processId);

        public IRawDisk Open(DiskEntry d, bool write)
        {
            if (d == null || !WindowsDiskQueries.IsDiskPath(d.Path)) throw new IOException("not a drive's device path");
            uint access = 0x80000000u | (write ? 0x40000000u : 0u);                         // GENERIC_READ (| GENERIC_WRITE)
            uint flags = 0x20000000u | (write ? 0x80000000u : 0u);                          // FILE_FLAG_NO_BUFFERING (| FILE_FLAG_WRITE_THROUGH)
            var h = N.CreateFileW(d.Path, access, 3, IntPtr.Zero, 3, flags, IntPtr.Zero);
            if (h.IsInvalid) { var e = Marshal.GetLastWin32Error(); h.Dispose(); throw new Win32Exception(e); }
            return new Disk(h, d.Path);
        }

        public IVolumeLock Lock(IList<DiskVolume> volumes, bool dismount)
        {
            var l = new VolumeLock();
            foreach (var v in volumes ?? new List<DiskVolume>())
            {
                var h = N.CreateFileW(v.Name.TrimEnd('\\'), 0xC0000000u, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (h.IsInvalid) { l.NotLocked.Add(v.Display + ": " + new Win32Exception(Marshal.GetLastWin32Error()).Message); h.Dispose(); continue; }
                bool locked = false; int err = 0;
                for (int i = 0; i < 20 && !locked; i++)
                {
                    locked = N.DeviceIoControl(h, N.FsctlLockVolume, null, 0, null, 0, out _, IntPtr.Zero);
                    if (!locked) { err = Marshal.GetLastWin32Error(); System.Threading.Thread.Sleep(250); }
                }
                if (!locked) { l.NotLocked.Add(v.Display + ": " + new Win32Exception(err).Message); h.Dispose(); continue; }
                l.Handles.Add(h);
                if (dismount && !N.DeviceIoControl(h, N.FsctlDismountVolume, null, 0, null, 0, out _, IntPtr.Zero))
                    l.NotLocked.Add(v.Display + " (dismount): " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }
            return l;
        }

        sealed class VolumeLock : IVolumeLock
        {
            public readonly List<SafeFileHandle> Handles = new List<SafeFileHandle>();
            public List<string> NotLocked { get; } = new List<string>();
            public void Dispose()
            {
                foreach (var h in Handles) { try { N.DeviceIoControl(h, N.FsctlUnlockVolume, null, 0, null, 0, out _, IntPtr.Zero); } catch (Exception) { } h.Dispose(); }
                Handles.Clear();
            }
        }

        sealed class Disk : IRawDisk
        {
            const int BufferBytes = 4 << 20;
            readonly SafeFileHandle h; readonly string path; IntPtr buf;
            public long Length { get; } public int SectorBytes { get; }
            public Disk(SafeFileHandle handle, string path)
            {
                h = handle; this.path = path;
                var li = new byte[8];
                if (!N.DeviceIoControl(h, N.IoctlDiskGetLengthInfo, null, 0, li, li.Length, out int got, IntPtr.Zero) || got < 8) { var e = Marshal.GetLastWin32Error(); h.Dispose(); throw new Win32Exception(e); }
                Length = BitConverter.ToInt64(li, 0);
                var id = WindowsDiskQueries.DescribeHandle(h, path); SectorBytes = id?.SectorBytes > 0 ? id.SectorBytes : 512;
                buf = N.VirtualAlloc(IntPtr.Zero, (UIntPtr)BufferBytes, 0x3000, 0x04);   // page-aligned, as reading and writing without caching need
                if (buf == IntPtr.Zero) { h.Dispose(); throw new OutOfMemoryException("no buffer for the drive"); }
            }
            public DiskIdentity Identity() => WindowsDiskQueries.DescribeHandle(h, path);
            void Aligned(long offset, int count)
            {
                if (offset < 0 || offset % SectorBytes != 0 || count <= 0 || count % SectorBytes != 0 || count > BufferBytes) throw new ArgumentException("not whole sectors");
                if (!N.SetFilePointerEx(h, offset, out _, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            public int Read(long offset, byte[] buffer, int count)
            {
                if (offset >= Length) return 0;
                count = (int)Math.Min(count, Length - offset); Aligned(offset, count);
                if (!N.ReadFile(h, buf, count, out int got, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
                Marshal.Copy(buf, buffer, 0, got); return got;
            }
            public void Write(long offset, byte[] buffer, int count)
            {
                Aligned(offset, count); Marshal.Copy(buffer, 0, buf, count);
                if (!N.WriteFile(h, buf, count, out int put, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (put != count) throw new IOException("the drive took " + put + " of " + count + " bytes");
            }
            public void Flush() { if (!N.FlushFileBuffers(h)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
            public void Refresh() { N.DeviceIoControl(h, N.IoctlDiskUpdateProperties, null, 0, null, 0, out _, IntPtr.Zero); }
            public void Dispose() { if (buf != IntPtr.Zero) { N.VirtualFree(buf, UIntPtr.Zero, 0x8000); buf = IntPtr.Zero; } h.Dispose(); }
        }
    }

    static class N
    {
        public const uint IoctlStorageGetDeviceNumber = 0x002D1080, IoctlStorageQueryProperty = 0x002D1400, IoctlDiskGeometryEx = 0x000700A0, IoctlDiskGetLengthInfo = 0x0007405C,
            IoctlVolumeDiskExtents = 0x00560000, IoctlDiskUpdateProperties = 0x00070140, FsctlLockVolume = 0x00090018, FsctlUnlockVolume = 0x0009001C, FsctlDismountVolume = 0x00090020;

        [StructLayout(LayoutKind.Sequential)] public struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WIN32_FIND_DATAW
        {
            public uint dwFileAttributes; public System.Runtime.InteropServices.ComTypes.FILETIME c, a, w; public uint nFileSizeHigh, nFileSizeLow, dwReserved0, dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
        }

        [DllImport("setupapi.dll", SetLastError = true)] public static extern IntPtr SetupDiGetClassDevsW(ref Guid cls, IntPtr enumerator, IntPtr hwnd, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid cls, uint index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", SetLastError = true)] public static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr devInfo);
        [DllImport("setupapi.dll", SetLastError = true)] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int returned, IntPtr overlapped);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr FindFirstVolumeW(StringBuilder name, int len);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool FindNextVolumeW(IntPtr h, StringBuilder name, int len);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool FindVolumeClose(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool GetVolumePathNamesForVolumeNameW(string volume, [Out] char[] names, int len, out int returned);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool GetVolumeInformationW(string root, StringBuilder name, int nameLen, out uint serial, out uint maxComp, out uint flags, StringBuilder fs, int fsLen);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr FindFirstFileW(string name, out WIN32_FIND_DATAW data);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool FindClose(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr h);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr VirtualAlloc(IntPtr at, UIntPtr size, uint type, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool VirtualFree(IntPtr at, UIntPtr size, uint type);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool SetFilePointerEx(SafeFileHandle h, long distance, out long position, uint method);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool ReadFile(SafeFileHandle h, IntPtr buffer, int count, out int read, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool WriteFile(SafeFileHandle h, IntPtr buffer, int count, out int written, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool FlushFileBuffers(SafeFileHandle h);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetNamedPipeClientProcessId(SafeHandle pipe, out int pid);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetNamedPipeServerProcessId(SafeHandle pipe, out int pid);
    }
}
