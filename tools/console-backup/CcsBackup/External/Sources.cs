// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// USB drives and SD cards as backup sources, identified by their OWN identity: never as a console. A replaceable drive is not a console's
// NAND, and nothing here ever names a console.
//   SourceIdentity   what Windows reports about the storage (all read without administrator permission, nothing written):
//                    the volume: its label, file system, serial number, size, free space, cluster size and Windows volume name
//                    (GetVolumeInformation, GetDiskFreeSpaceEx, GetVolumeNameForVolumeMountPoint);
//                    the drive it is on: its number, how it is connected (USB, SD, MMC, ...), maker, model and serial number as the drive
//                    reports them, its size and sector size (IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, IOCTL_STORAGE_QUERY_PROPERTY,
//                    IOCTL_DISK_GET_DRIVE_GEOMETRY_EX on the drive opened with no read or write access);
//                    for a drive image FILE: its size and what its first sectors say (the partition table's disk signature or GPT disk
//                    identifier, the first file system's volume serial number).
//                    Key: a short checksum of the stable parts (drive serial, model, size, volume serial and file system; for a folder,
//                    also its path on the volume): two backups with the same Key are of the same storage (Repair borrows only between those).
//   Devices          the USB drives and SD cards connected now (removable volumes, or volumes on a drive connected by USB, SD or MMC);
//                    the drive that holds Windows; which drives hold a given file.
using CcsBackup.Repair;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CcsBackup.External
{
    public enum SourceKind { Folder, Volume, ImageFile, Disk }

    public sealed class SourceIdentity
    {
        public SourceKind Kind;
        public string Root;            // the folder or the volume's root ("E:\"), the image file's path, or "\\.\PhysicalDriveN"
        public string RelativePath;    // a folder: its path on its volume ("\games"); a whole volume: "\"
        public string Label, FileSystem, VolumeSerial, VolumeName;
        public long TotalBytes = -1, FreeBytes = -1, ClusterBytes = -1;
        public bool CaseSensitive, Removable;
        public int DiskNumber = -1; public string BusType, Vendor, Product, DiskSerial; public long DiskBytes = -1; public int SectorBytes = -1;
        public string ImageSignature;  // an image file: its partition table's disk signature or GPT identifier, and its first volume's serial
        public string Key;

        public string Display
        {
            get
            {
                var what = BusType == "SD" || BusType == "MMC" ? "SD card" : Kind == SourceKind.ImageFile ? "Drive image file" : Kind == SourceKind.Folder && !Removable ? "Folder" : "USB drive";
                var name = !string.IsNullOrWhiteSpace(Label) ? Label.Trim() : Kind == SourceKind.ImageFile ? Path.GetFileName(Root) : (Vendor + " " + Product).Trim();
                var letter = Kind == SourceKind.Volume || Kind == SourceKind.Folder ? Root?.Length >= 2 && Root[1] == ':' ? " (" + Root.Substring(0, 2) + ")" : "" : "";
                // a folder: its own name and the drive it is on (its whole path is in the identity, not in every name people read)
                if (Kind == SourceKind.Folder && RelativePath != null && RelativePath != "\\")
                    return "Folder \"" + RelativePath.TrimEnd('\\').Substring(RelativePath.TrimEnd('\\').LastIndexOf('\\') + 1) + "\" on " + (Removable ? what.ToLowerInvariant() : "drive") + (string.IsNullOrEmpty(name) ? "" : " " + name) + letter;
                return what + (string.IsNullOrEmpty(name) ? "" : " " + name) + letter;
            }
        }

        public JObj ToJson() => new JObj
        {
            { "kind", Kind.ToString().ToLowerInvariant() }, { "key", Key }, { "display", Display }, { "root", Root }, { "relative_path", RelativePath },
            { "label", Label }, { "file_system", FileSystem }, { "volume_serial", VolumeSerial }, { "volume_name", VolumeName },
            { "total_bytes", TotalBytes }, { "free_bytes", FreeBytes }, { "cluster_bytes", ClusterBytes }, { "case_sensitive", CaseSensitive }, { "removable", Removable },
            { "disk", new JObj { { "number", (long)DiskNumber }, { "bus", BusType }, { "vendor", Vendor }, { "product", Product }, { "serial", DiskSerial }, { "bytes", DiskBytes }, { "sector_bytes", (long)SectorBytes } } },
            { "image_signature", ImageSignature },
        };

        public static SourceIdentity FromJson(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var k = d.S("kind"); var disk = d.O("disk");
            return new SourceIdentity
            {
                Kind = k == "volume" ? SourceKind.Volume : k == "imagefile" ? SourceKind.ImageFile : k == "disk" ? SourceKind.Disk : SourceKind.Folder,
                Key = d.S("key"), Root = d.S("root"), RelativePath = d.S("relative_path"), Label = d.S("label"), FileSystem = d.S("file_system"), VolumeSerial = d.S("volume_serial"), VolumeName = d.S("volume_name"),
                TotalBytes = d.L("total_bytes"), FreeBytes = d.L("free_bytes"), ClusterBytes = d.L("cluster_bytes"), CaseSensitive = d.B("case_sensitive"), Removable = d.B("removable"),
                DiskNumber = (int)(disk?.L("number") ?? -1), BusType = disk?.S("bus"), Vendor = disk?.S("vendor"), Product = disk?.S("product"), DiskSerial = disk?.S("serial"), DiskBytes = disk?.L("bytes") ?? -1, SectorBytes = (int)(disk?.L("sector_bytes") ?? -1),
                ImageSignature = d.S("image_signature"),
            };
        }

        internal void MakeKey()
        {
            var t = Kind == SourceKind.ImageFile
                ? "image|" + TotalBytes + "|" + ImageSignature
                : "drive|" + (DiskSerial ?? "") + "|" + (Vendor ?? "") + "|" + (Product ?? "") + "|" + DiskBytes + "|vol|" + (VolumeSerial ?? "") + "|" + (FileSystem ?? "") + "|" + TotalBytes
                  + (Kind == SourceKind.Folder ? "|path|" + (RelativePath ?? "").ToUpperInvariant() : "");
            using (var h = SHA256.Create()) Key = Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(t))).Substring(0, 24);
        }
    }

    public static class Devices
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetVolumePathName(string file, StringBuilder volume, int len);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetVolumeNameForVolumeMountPoint(string mount, StringBuilder name, int len);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetVolumeInformation(string root, StringBuilder name, int nameLen, out uint serial, out uint maxComp, out uint flags, StringBuilder fs, int fsLen);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetDiskFreeSpace(string root, out uint sectorsPerCluster, out uint bytesPerSector, out uint freeClusters, out uint totalClusters);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetDiskFreeSpaceEx(string root, out ulong freeToCaller, out ulong total, out ulong free);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int returned, IntPtr overlapped);
        const uint IoctlVolumeDiskExtents = 0x00560000, IoctlStorageQueryProperty = 0x002D1400, IoctlDiskGeometryEx = 0x000700A0, ShareReadWrite = 3, OpenExisting = 3;

        static readonly string[] Bus = { "Unknown", "SCSI", "ATAPI", "ATA", "1394", "SSA", "Fibre", "USB", "RAID", "iSCSI", "SAS", "SATA", "SD", "MMC", "Virtual", "FileBackedVirtual", "Spaces", "NVMe", "SCM", "UFS" };

        /// <summary>what Windows reports about the volume holding path (a volume root or a folder on it); nothing is opened for reading</summary>
        public static SourceIdentity ForFolder(string path)
        {
            var full = Path.GetFullPath(path);
            var id = new SourceIdentity { Kind = SourceKind.Folder, Root = full };
            var vp = new StringBuilder(1024);
            if (GetVolumePathName(full, vp, vp.Capacity))
            {
                var root = vp.ToString(); var rel = "\\" + full.Substring(Math.Min(full.Length, root.Length)).TrimEnd('\\');
                id.RelativePath = rel == "\\" ? "\\" : rel;
                if (id.RelativePath == "\\") { id.Kind = SourceKind.Volume; id.Root = root; }
                Volume(root, id);
                var disks = Drives.PhysicalDisks(root);
                if (disks != null && disks.Length == 1) { id.DiskNumber = disks[0]; Disk(disks[0], id); }
                id.Removable = SafeDriveType(root) == DriveType.Removable || id.BusType == "USB" || id.BusType == "SD" || id.BusType == "MMC";
            }
            id.MakeKey();
            return id;
        }

        static DriveType SafeDriveType(string root) { try { return new DriveInfo(root).DriveType; } catch (Exception) { return DriveType.Unknown; } }

        static void Volume(string root, SourceIdentity id)
        {
            var name = new StringBuilder(261); var fs = new StringBuilder(261);
            if (GetVolumeInformation(root, name, name.Capacity, out var serial, out _, out _, fs, fs.Capacity))
            { id.Label = name.ToString(); id.FileSystem = fs.ToString(); id.VolumeSerial = serial.ToString("X8", CultureInfo.InvariantCulture); }
            // Windows opens names on its volumes without regard to case (a folder marked case-sensitive is rare and is not used here): two
            // names that differ only in case are found by the capture and recorded as a collision instead
            if (GetDiskFreeSpaceEx(root, out _, out var total, out var free)) { id.TotalBytes = (long)total; id.FreeBytes = (long)free; }
            if (GetDiskFreeSpace(root, out var spc, out var bps, out _, out _)) id.ClusterBytes = (long)spc * bps;
            var vn = new StringBuilder(1024); if (GetVolumeNameForVolumeMountPoint(root, vn, vn.Capacity)) id.VolumeName = vn.ToString();
        }

        /// <summary>the drive's own description (no read or write access is asked for)</summary>
        static void Disk(int n, SourceIdentity id)
        {
            try
            {
                using (var h = CreateFile(@"\\.\PhysicalDrive" + n, 0, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) return;
                    var q = new byte[12];   // STORAGE_PROPERTY_QUERY: StorageDeviceProperty (0), PropertyStandardQuery (0)
                    var o = new byte[4096];
                    if (DeviceIoControl(h, IoctlStorageQueryProperty, q, q.Length, o, o.Length, out int got, IntPtr.Zero) && got >= 36)
                    {
                        string At(int off) { int p = BitConverter.ToInt32(o, off); if (p <= 0 || p >= got) return null; int e = p; while (e < got && o[e] != 0) e++; var s = Encoding.ASCII.GetString(o, p, e - p).Trim(); return s.Length == 0 ? null : s; }
                        id.Vendor = At(12); id.Product = At(16); id.DiskSerial = At(24);
                        int bus = BitConverter.ToInt32(o, 28); id.BusType = bus >= 0 && bus < Bus.Length ? Bus[bus] : "Other";
                        id.Removable = o[10] != 0;
                    }
                    var g = new byte[256];
                    if (DeviceIoControl(h, IoctlDiskGeometryEx, null, 0, g, g.Length, out int gg, IntPtr.Zero) && gg >= 32)
                    { id.SectorBytes = BitConverter.ToInt32(g, 20); id.DiskBytes = BitConverter.ToInt64(g, 24); }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }

        /// <summary>a drive image file: its size and what its first sectors say (read only)</summary>
        public static SourceIdentity ForImage(string file)
        {
            var full = Path.GetFullPath(file);
            var id = new SourceIdentity { Kind = SourceKind.ImageFile, Root = full, RelativePath = null, SectorBytes = 512 };
            using (var f = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 4096))
            {
                id.TotalBytes = id.DiskBytes = f.Length;
                try
                {
                    var layout = PartitionTable.Read(f);
                    id.ImageSignature = layout.Signature + "|" + string.Join(",", layout.Parts.Select(p => p.Serial ?? "-"));
                    id.FileSystem = layout.Parts.FirstOrDefault()?.FileSystem; id.Label = layout.Parts.Select(p => p.Label).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
                }
                catch (Exception ex) when (ex is IOException || ex is FormatException) { id.ImageSignature = "unreadable"; }
            }
            id.MakeKey();
            return id;
        }

        /// <summary>the USB drives and SD cards connected now, one per volume with a drive letter (never the drive Windows runs from)</summary>
        public static List<SourceIdentity> Connected()
        {
            var list = new List<SourceIdentity>(); int sys = SystemDisk();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady || d.DriveType == DriveType.Network || d.DriveType == DriveType.CDRom || d.DriveType == DriveType.Ram) continue;
                    var id = ForFolder(d.RootDirectory.FullName);
                    if (id.DiskNumber >= 0 && id.DiskNumber == sys) continue;
                    if (!id.Removable) continue;
                    list.Add(id);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            }
            return list;
        }

        /// <summary>the number of the drive that holds Windows (-1 when Windows does not say)</summary>
        public static int SystemDisk()
        {
            var d = Drives.PhysicalDisks(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            return d != null && d.Length > 0 ? d[0] : -1;
        }

        /// <summary>the drives holding a file or folder (null: cannot be determined)</summary>
        public static int[] DisksOf(string path) => Drives.PhysicalDisks(path);
    }
}
