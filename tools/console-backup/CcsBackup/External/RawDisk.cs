// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// A connected USB drive or SD card read or written WHOLE, sector by sector, on this PC. Never a console and never a console's storage: a
// console is changed only by the console operations, through the console's own program in its memory. Windows lets a program open a drive
// whole only with administrator permission, so the window (never elevated itself) asks Windows once per operation for this same program to
// run again with administrator permission in its narrow --raw-disk mode (RawDiskHost.cs). That step reads or writes exactly the one drive
// the reviewed request names, checks it again itself, and ends; the window and the step talk through a pipe only they can use
// (RawDiskClient.cs). Raw drive access is kept apart from every console storage operation: nothing here is used by them, and they never
// reach this.
//   DiskIdentity     what identifies one drive: its number now, its device path as Windows resolves it, maker, model, serial number, how
//                    it is connected (USB, SD, MMC, ...), whether its media is removable, its size and its sector size. Compared field by
//                    field before anything is read or written, again through the opened drive right before reading or writing, and after.
//   RawDiskPolicy    which drives can ever be read or written whole: connected by USB, SD or MMC.
//   RawDiskTarget    the checks every target passes, in the window (early, to say why) and again in the elevated step (the one that
//                    counts): the drive is still there and still the same one, it is a USB drive or SD card, it is not the drive Windows
//                    runs from, and it holds neither this program, the request, the backup being restored, its recovery data, nor the
//                    backup location; no volume on it spans another drive; another drive that cannot be told apart from it is refused.
//   IRawDiskSystem   the PC's drives: what Windows reports about them (DiskQueries, reading nothing on the drives), and, in the elevated
//                    step only, a drive opened for reading or writing and its volumes locked (WindowsRawDiskSystem.cs).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CcsBackup.External
{
    /// <summary>one drive as Windows lists it now: its number and its device path</summary>
    public sealed class DiskEntry
    {
        public int Number; public string Path;
        public override string ToString() => "drive " + Number + " (" + Path + ")";
    }

    public sealed class DiskIdentity
    {
        public int Number = -1;
        public string Path, Vendor, Product, Serial, Bus;
        public bool RemovableMedia;
        public long Bytes = -1; public int SectorBytes = -1;

        public bool IsSdCard => Bus == "SD" || Bus == "MMC";
        /// <summary>how people see it: "USB drive SanDisk Cruzer, 32.0 GB" (the serial number is shown beside it, not in it)</summary>
        public string Display => (IsSdCard ? "SD card" : Bus == "USB" ? "USB drive" : "Drive") + (string.IsNullOrWhiteSpace(Name) ? "" : " " + Name) + (Bytes > 0 ? ", " + Engine.Units.Gb(Bytes) : "");
        public string Name => ((Vendor ?? "") + " " + (Product ?? "")).Trim();
        public string BusText => Bus == "USB" ? "USB" : Bus == "SD" ? "SD card slot" : Bus == "MMC" ? "MMC card slot" : string.IsNullOrEmpty(Bus) ? "not reported" : Bus;

        /// <summary>the first field in which two identities of what should be one drive differ ("its serial number"), or null when the same</summary>
        public string Differs(DiskIdentity o)
        {
            if (o == null) return "it is not there";
            if (Number != o.Number) return "its number";
            if (!string.Equals(Path ?? "", o.Path ?? "", StringComparison.OrdinalIgnoreCase)) return "its device path";
            if ((Serial ?? "") != (o.Serial ?? "")) return "its serial number";
            if ((Vendor ?? "") != (o.Vendor ?? "") || (Product ?? "") != (o.Product ?? "")) return "its maker or model";
            if ((Bus ?? "") != (o.Bus ?? "")) return "how it is connected";
            if (RemovableMedia != o.RemovableMedia) return "whether its media is removable";
            if (Bytes != o.Bytes) return "its size";
            if (SectorBytes != o.SectorBytes) return "its sector size";
            return null;
        }

        public JObj ToJson() => new JObj { { "number", (long)Number }, { "path", Path }, { "vendor", Vendor }, { "product", Product }, { "serial", Serial }, { "bus", Bus },
            { "removable_media", RemovableMedia }, { "bytes", Bytes }, { "sector_bytes", (long)SectorBytes } };
        public static DiskIdentity FromJson(Dictionary<string, object> d) => d == null ? null : new DiskIdentity
        {
            Number = (int)d.L("number", -1), Path = d.S("path"), Vendor = d.S("vendor"), Product = d.S("product"), Serial = d.S("serial"), Bus = d.S("bus"),
            RemovableMedia = d.B("removable_media"), Bytes = d.L("bytes", -1), SectorBytes = (int)d.L("sector_bytes", -1),
        };

        /// <summary>the same drive as a backup source's identity (kind Disk), with its key</summary>
        public SourceIdentity ToSource()
        {
            var s = new SourceIdentity { Kind = SourceKind.Disk, Root = Path, DiskNumber = Number, BusType = Bus, Vendor = Vendor, Product = Product, DiskSerial = Serial,
                DiskBytes = Bytes, TotalBytes = Bytes, SectorBytes = SectorBytes, Removable = RemovableMedia || Bus == "USB" || IsSdCard };
            s.MakeKey(); return s;
        }

        public override string ToString() => Display + (Serial != null ? ", serial " + Serial : "") + ", connected by " + BusText;
    }

    /// <summary>a volume with a part on a drive: its Windows volume name, its drive letters or folders, label and file system</summary>
    public sealed class DiskVolume
    {
        public string Name; public List<string> Mounts = new List<string>(); public string Label, FileSystem; public long Bytes;
        public bool SpansOtherDisks;
        public string Display => (Mounts.Count > 0 ? string.Join(", ", Mounts.Select(m => m.TrimEnd('\\'))) + " " : "") + (string.IsNullOrEmpty(Label) ? "" : "\"" + Label + "\" ")
            + "(" + (string.IsNullOrEmpty(FileSystem) ? "no file system Windows recognizes" : FileSystem) + (Bytes > 0 ? ", " + Engine.Units.Gb(Bytes) : "") + ")";
        public JObj ToJson() => new JObj { { "name", Name }, { "mounts", Mounts.Cast<object>().ToList() }, { "label", Label }, { "file_system", FileSystem }, { "bytes", Bytes }, { "spans_other_disks", SpansOtherDisks } };
        public static DiskVolume FromJson(Dictionary<string, object> d) => d == null ? null : new DiskVolume
        { Name = d.S("name"), Mounts = (d.A("mounts") ?? new List<object>()).OfType<string>().ToList(), Label = d.S("label"), FileSystem = d.S("file_system"), Bytes = d.L("bytes", 0), SpansOtherDisks = d.B("spans_other_disks") };
    }

    /// <summary>a file or folder whose drive is never a target ("the backup being restored")</summary>
    public sealed class ProtectedPath
    {
        public string Path, What;
        public ProtectedPath(string path, string what) { Path = path; What = what; }
        public JObj ToJson() => new JObj { { "path", Path }, { "what", What } };
        public static ProtectedPath FromJson(Dictionary<string, object> d) => d == null || d.S("path") == null ? null : new ProtectedPath(d.S("path"), d.S("what") ?? "a file Workbench needs");
    }

    /// <summary>which drives can be read or written whole. Only connected by USB, SD or MMC: never an internal drive, a network or virtual
    /// drive, or storage pooled across drives.</summary>
    public sealed class RawDiskPolicy
    {
        readonly HashSet<string> buses;
        RawDiskPolicy(params string[] b) { buses = new HashSet<string>(b, StringComparer.Ordinal); }
        public bool Allows(string bus) => bus != null && buses.Contains(bus);
        public IEnumerable<string> Buses => buses;
        /// <summary>the product's one policy</summary>
        public static readonly RawDiskPolicy UsbAndSd = new RawDiskPolicy("USB", "SD", "MMC");
        /// <summary>the disposable test machine's virtual hard disks as well (test programs only; nothing in the product uses it)</summary>
        internal static RawDiskPolicy WithVirtualForTests() => new RawDiskPolicy("USB", "SD", "MMC", "Virtual", "FileBackedVirtual");
    }

    public enum RawDiskRefusal { None, Gone, Ambiguous, NotSame, NotUsbOrSd, SystemDisk, HoldsProtected, UnknownDrive, Spans, NoMedia, LookAlike }

    public sealed class TargetCheck
    {
        public RawDiskRefusal Refusal; public string Text; public DiskEntry Entry; public DiskIdentity Now; public List<DiskVolume> Volumes = new List<DiskVolume>();
        public bool Ok => Refusal == RawDiskRefusal.None;
    }

    public interface IDiskQueries
    {
        /// <summary>every drive Windows lists now, with its number and device path (nothing opened for reading or writing)</summary>
        List<DiskEntry> Disks();
        /// <summary>what Windows reports about the drive, read without any access to its contents; null when it cannot be asked</summary>
        DiskIdentity Describe(DiskEntry d);
        /// <summary>the drives holding a file or folder (null: cannot be determined)</summary>
        int[] DisksOf(string path);
        /// <summary>the drive Windows runs from (-1 when Windows does not say)</summary>
        int SystemDisk();
        /// <summary>the volumes with a part on the drive</summary>
        List<DiskVolume> VolumesOn(int disk);
        /// <summary>true when the path, or a folder above it, is a link, junction or mount point (a reparse point that names another place)</summary>
        bool Redirected(string path);
        /// <summary>the program file of a running process (null when it cannot be read)</summary>
        string ProgramOf(int processId);
    }

    public static class RawDiskTarget
    {
        /// <summary>every check a drive passes before anything on it is read or written whole; wanted: the identity that was reviewed</summary>
        public static TargetCheck Check(IDiskQueries q, RawDiskPolicy policy, DiskIdentity wanted, IEnumerable<ProtectedPath> protect)
        {
            var c = new TargetCheck();
            TargetCheck No(RawDiskRefusal r, string text) { c.Refusal = r; c.Text = text; return c; }
            if (wanted == null || wanted.Number < 0) return No(RawDiskRefusal.Gone, "No drive was chosen.");
            var all = q.Disks();
            var hits = all.Where(d => d.Number == wanted.Number).ToList();
            if (hits.Count == 0) return No(RawDiskRefusal.Gone, "That drive is no longer connected: Windows lists no drive " + wanted.Number + " now.");
            if (hits.Count > 1) return No(RawDiskRefusal.Ambiguous, "Windows lists more than one drive with the number " + wanted.Number + ", so Workbench cannot be sure which one you chose.");
            c.Entry = hits[0];
            var now = c.Now = q.Describe(c.Entry);
            if (now == null) return No(RawDiskRefusal.Gone, "Windows does not describe drive " + wanted.Number + " now (it may have been disconnected).");
            if (now.Bytes <= 0) return No(RawDiskRefusal.NoMedia, "Drive " + wanted.Number + " reports no size: there may be no card in the reader.");
            var diff = wanted.Differs(now);
            if (diff != null) return No(RawDiskRefusal.NotSame, "The drive connected as drive " + wanted.Number + " is not the one you chose: " + diff + " is different now (" + now + ").");
            if (!policy.Allows(now.Bus)) return No(RawDiskRefusal.NotUsbOrSd, "That drive is not a USB drive or SD card: Windows reports it as connected by " + now.BusText + ". Only USB drives and SD cards are read or written whole.");
            if (q.SystemDisk() < 0) return No(RawDiskRefusal.UnknownDrive, "Windows does not say which drive it runs from, so this drive cannot be shown to be a different one.");
            if (now.Number == q.SystemDisk()) return No(RawDiskRefusal.SystemDisk, "That is the drive Windows runs from. It is never read or written whole.");
            foreach (var p in protect ?? Enumerable.Empty<ProtectedPath>())
            {
                if (string.IsNullOrEmpty(p.Path)) continue;
                var ds = q.DisksOf(p.Path);
                if (ds == null) return No(RawDiskRefusal.UnknownDrive, "Which drive holds " + p.What + " cannot be determined, so this drive cannot be shown to be a different one.");
                if (ds.Contains(now.Number)) return No(RawDiskRefusal.HoldsProtected, "That drive holds " + p.What + ". Choose another drive.");
            }
            if (string.IsNullOrEmpty(now.Serial))
            {
                foreach (var o in all.Where(d => d.Number != now.Number))
                {
                    var x = q.Describe(o);
                    if (x != null && x.Bytes == now.Bytes && (x.Vendor ?? "") == (now.Vendor ?? "") && (x.Product ?? "") == (now.Product ?? "") && (x.Bus ?? "") == (now.Bus ?? ""))
                        return No(RawDiskRefusal.LookAlike, "Another connected drive has the same maker, model and size, and neither reports a serial number, so Workbench cannot tell them apart. Disconnect the other drive and try again.");
                }
            }
            c.Volumes = q.VolumesOn(now.Number) ?? new List<DiskVolume>();
            var span = c.Volumes.FirstOrDefault(v => v.SpansOtherDisks);
            if (span != null) return No(RawDiskRefusal.Spans, "The volume " + span.Display + " on that drive also spans another drive, so the drive cannot be read or written on its own.");
            return c;
        }

        /// <summary>the paths whose drives are never targets in every case: Windows itself, this program's file</summary>
        public static List<ProtectedPath> Always(string program)
        {
            var l = new List<ProtectedPath>();
            var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(win)) l.Add(new ProtectedPath(win, "Windows"));
            if (!string.IsNullOrEmpty(program)) l.Add(new ProtectedPath(program, "this program (CCS Console Workbench)"));
            return l;
        }
    }

    /// <summary>Exit codes of the --raw-disk step. The window never trusts the code alone: what happened travels through the pipe, and an
    /// interrupted write is reported from what the window knows (it gave the go-ahead, the step never said it finished).</summary>
    public static class RawDiskExit
    {
        public const int Done = 0, Canceled = 1223;
        public const int GateClosed = 0x43440001, BadRequest = 0x43440002, NotElevated = 0x43440003, PeerRefused = 0x43440004, TargetRefused = 0x43440005,
            Stopped = 0x43440006, CanceledNothingWritten = 0x43440007, CanceledIncomplete = 0x43440008, ReadBackDiffers = 0x43440009, Unexpected = 0x4344000A, InUse = 0x4344000B;

        public static string Text(int code)
        {
            switch (code)
            {
                case Done: return "The administrator step finished.";
                case GateClosed: return Gate.StorageRawText;
                case BadRequest: return "The administrator step did not accept Workbench's request, so it read and wrote nothing.";
                case NotElevated: return "The administrator step ran without administrator permission, so it read and wrote nothing.";
                case PeerRefused: return "The administrator step could not confirm it was talking to this Workbench window, so it read and wrote nothing.";
                case TargetRefused: return "The administrator step refused the drive, so it read and wrote nothing.";
                case InUse: return "Another program is using the drive, so the administrator step wrote nothing.";
                case CanceledNothingWritten: return "Canceled before anything was written.";
            }
            return "The administrator step ended with code 0x" + code.ToString("X8", CultureInfo.InvariantCulture) + ".";
        }
    }
}
