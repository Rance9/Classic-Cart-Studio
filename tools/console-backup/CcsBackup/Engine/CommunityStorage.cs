// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Community Mods: the USB and SD backup a change needs when a mod writes a USB drive or SD card used with the console. Workbench never
// writes that storage itself; the mod does, on the console, once it is installed. So before such a mod is installed, updated or removed,
// the person chooses a USB and SD backup of that storage, made with USB and SD Backup on this PC (External/StorageBackup.cs), and the
// change is bound to it:
//   Bind      the file verified from scratch (External/StorageVerifier.cs): undamaged, everything that was selected copied (scope
//             complete), and every place the mod's record names (external_storage.locations, console paths under /media, where hakchi
//             mounts a USB drive or SD card) inside what it holds: a whole drive image holds everything on that drive; a files backup holds
//             a place when the folders chosen for it (recorded in the backup since this version) include it. Every place must be on ONE
//             drive, and that drive must be a USB drive or SD card (or a drive image file). The binding keeps the file's path, SHA-256,
//             size, backup ID, the storage's own identity (its name, label, size, file system and serial number as Windows reported them
//             when the backup was made) and when it was made; it goes into the plan's digest.
//   Recheck   at the confirmation, under the hold (nothing can change the file meanwhile), immediately before the first write: the file
//             verified again from scratch and still exactly the bound one, still covering every place.
//   Identity  the console session cannot read that storage: the console's one USB port is the one connected to this PC (a USB drive
//             cannot be attached to it at the same time), and the console programs Workbench uses never read a USB drive or SD card. So
//             Workbench cannot check that the backup is of the storage the console uses: the review shows the backup's own identity and
//             the person confirms it (the confirmation is required before anything is written). What was and was not checked is said
//             plainly.
using CcsBackup.External;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CcsBackup.Engine
{
    /// <summary>a USB and SD backup bound to a community change</summary>
    public sealed class CommunityStorageBinding
    {
        public string Path, Sha256, BackupId, CreatedUtc, CompletedUtc, DriveKey, Display, Label, FileSystem, VolumeSerial, Mode; public long Bytes, StorageBytes;
        public List<string> Locations = new List<string>();
        /// <summary>each place: the source that holds it and how ("the whole drive image" or "the folder ... chosen")</summary>
        public List<(string location, string source, string how)> Cover = new List<(string, string, string)>();
        public string When => CommunityStorage.Local(CompletedUtc ?? CreatedUtc);
        /// <summary>the storage the backup is of, in plain words for the review</summary>
        public string IdentityText => Display + (StorageBytes > 0 ? ", " + Compose.Units.Size(StorageBytes) : "") + (string.IsNullOrEmpty(FileSystem) ? "" : ", " + FileSystem)
            + (string.IsNullOrEmpty(VolumeSerial) ? "" : ", serial number " + VolumeSerial) + ". Backed up on " + When + ".";
        public string DigestLine() => string.Join("|", "storage", Path, Sha256, Bytes.ToString(CultureInfo.InvariantCulture), BackupId, CompletedUtc, DriveKey, Mode, string.Join(",", Locations), string.Join(",", Cover.Select(c => c.location + ">" + c.source)));
        public JObj ToJson() => new JObj
        {
            { "role", "USB and SD backup of the storage the mod changes" }, { "path", Path }, { "sha256", Sha256 }, { "bytes", Bytes }, { "backup_id", BackupId },
            { "created_utc", CreatedUtc }, { "completed_utc", CompletedUtc }, { "made", When }, { "storage", Display }, { "label", Label }, { "storage_bytes", StorageBytes },
            { "file_system", FileSystem }, { "volume_serial", VolumeSerial }, { "drive_key", DriveKey }, { "mode", Mode }, { "identity_text", IdentityText },
            { "locations", Locations.Cast<object>().ToList() }, { "cover", Cover.Select(c => (object)new JObj { { "location", c.location }, { "source", c.source }, { "how", c.how } }).ToList() },
            { "checked", CommunityStorage.CheckedText }, { "not_checked", CommunityStorage.NotCheckedText },
        };
    }

    public static class CommunityStorage
    {
        public const string CheckedText = "Workbench checked this backup file from scratch: it is undamaged, everything chosen for it was copied, and it holds every place the mod changes. It is checked again when you confirm.";
        public const string NotCheckedText = "Workbench cannot check that this backup is of the USB drive or SD card you use with this console: the console's USB port is the one connected to this computer, and Workbench never reads or writes that storage through the console. Confirm it yourself.";
        public const string ConfirmText = "This backup is of the USB drive or SD card I use with this console.";
        public const string NeedConfirm = "Confirm that the USB and SD backup is of the USB drive or SD card you use with this console. Nothing was changed.";

        /// <summary>a storage source that is a USB drive or SD card (or a drive image file)</summary>
        static bool IsUsbOrSd(SourceIdentity id) => id != null && (id.Kind == SourceKind.ImageFile || id.Kind == SourceKind.Disk && (id.BusType == "USB" || id.BusType == "SD" || id.BusType == "MMC") || (id.Kind == SourceKind.Volume || id.Kind == SourceKind.Folder) && id.Removable);
        /// <summary>a source's identity as the backup recorded it; tests only: a temporary folder standing in for a USB drive's root (no test
        /// ever uses a real drive)</summary>
        internal static Func<SourceIdentity, SourceIdentity> Seen = id => id;

        internal static string Local(string utc) => DateTime.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
            ? t.ToLocalTime().ToString("dddd, MMMM d, yyyy 'at' h:mm tt", CultureInfo.GetCultureInfo("en-US")) : "an unrecorded time";

        /// <summary>where a console place is on the drive: "/media/hakchi/x" -> "/hakchi/x" (hakchi mounts the USB drive or SD card at /media)</summary>
        public static string OnDrive(string location)
        {
            var p = (location ?? "").TrimEnd('/');
            return p.StartsWith("/media/", StringComparison.Ordinal) ? p.Substring("/media".Length) : null;
        }

        /// <summary>the drive a source is on, without the folder: two sources with the same key are on the same storage</summary>
        static string DriveOf(SourceIdentity id) => id.Kind == SourceKind.ImageFile ? "image|" + id.Key
            : "drive|" + (id.DiskSerial ?? "") + "|" + (id.Vendor ?? "") + "|" + (id.Product ?? "") + "|" + id.DiskBytes.ToString(CultureInfo.InvariantCulture) + "|" + (id.VolumeSerial ?? "") + "|" + (id.FileSystem ?? "") + "|" + id.TotalBytes.ToString(CultureInfo.InvariantCulture);

        static string Win2Posix(string rel) { var p = (rel ?? "\\").Replace('\\', '/').TrimEnd('/'); return p.Length == 0 ? "/" : p.StartsWith("/") ? p : "/" + p; }
        static bool Under(string path, string folder) => folder == "/" || string.Equals(path, folder, StringComparison.OrdinalIgnoreCase) || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);

        /// <summary>which source holds a place on the drive, and how; null when none does (why: the reason in plain words)</summary>
        static (Dictionary<string, object> src, SourceIdentity id, string how) Holder(StorageVerification v, string onDrive, out string why)
        {
            why = null; bool noSelection = false;
            foreach (var s in v.SourceList)
            {
                var id = SourceIdentity.FromJson(s.O("identity")); if (id == null) continue; id = Seen(id);
                if (s.S("mode") == "image") return (s, id, "the whole drive image " + s.S("id"));
                var root = id.Kind == SourceKind.Folder ? Win2Posix(id.RelativePath) : "/";
                if (!Under(onDrive, root)) continue;
                var rel = root == "/" ? onDrive : onDrive.Length == root.Length ? "/" : onDrive.Substring(root.Length);
                var sel = (s.A("selected") ?? new List<object>()).OfType<string>().ToList();
                if (s.A("selected") == null) { noSelection = true; continue; }
                var rows = v.Inventory.TryGetValue(s.S("id"), out var r) ? r : new List<Dictionary<string, object>>();
                // a chosen folder is listed in the backup as a folder (the selection the manifest names is what was copied)
                bool listed(string f) => f == "/" || rows.Any(x => x.S("type") == "folder" && string.Equals(x.S("path"), f, StringComparison.OrdinalIgnoreCase));
                var f0 = sel.FirstOrDefault(f => Under(rel, f) && listed(f));
                if (f0 != null) return (s, id, f0 == "/" ? "everything on " + (root == "/" ? "the drive" : "the folder " + root) : "the folder " + (root == "/" ? "" : root) + f0 + " and everything in it");
            }
            why = noSelection ? "the backup does not record which folders were chosen for it (it was made with an earlier version of Workbench)" : "it is not among the folders the backup holds";
            return (null, null, null);
        }

        /// <summary>the chosen backup verified from scratch and bound, or why it cannot be used (plain words; ends with "Nothing was changed.")</summary>
        public static (CommunityStorageBinding binding, string problem) Bind(string file, IList<string> locations, Func<bool> cancelled = null) => Bind(file, locations, cancelled, out _);

        public static (CommunityStorageBinding binding, string problem) Bind(string file, IList<string> locations, Func<bool> cancelled, out StorageVerification verification)
        {
            verification = null;
            var places = string.Join(", ", locations);
            if (string.IsNullOrWhiteSpace(file)) return (null, "This mod also changes a USB drive or SD card used with the console (" + places + "). Choose a USB and SD backup of that storage, made with USB and SD Backup on this PC, before it is installed, updated or removed. Nothing was changed.");
            if (!File.Exists(file)) return (null, "The chosen USB and SD backup is not there (" + file + "). Nothing was changed.");
            var v = StorageVerifier.Verify(file, null, cancelled); verification = v;
            if (!v.Ok) return (null, "The chosen USB and SD backup did not pass its check (" + v.Problems[0] + "). Repair it with USB and SD Backup, or choose another. Nothing was changed.");
            if (!v.ScopeComplete) return (null, "The chosen USB and SD backup does not hold everything that was chosen for it (some files could not be read or changed while they were copied), so it is not protection for that storage. Make a new USB and SD backup. Nothing was changed.");
            var man = v.Manifest;
            var b = new CommunityStorageBinding { Path = System.IO.Path.GetFullPath(file), Sha256 = v.ArchiveSha256, Bytes = v.Bytes, BackupId = v.BackupId, CreatedUtc = man.S("created_utc"), CompletedUtc = man.S("completed_utc"), Locations = locations.ToList() };
            SourceIdentity drive = null; string driveKey = null; var modes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var loc in locations)
            {
                var onDrive = OnDrive(loc);
                if (onDrive == null) return (null, "Workbench cannot tell where " + loc + " is on a USB drive or SD card, so it cannot check a backup of it. Nothing was changed.");
                var (src, id, how) = Holder(v, onDrive, out var why);
                if (src == null) return (null, "The chosen USB and SD backup does not hold " + loc + " (on the drive: " + onDrive + "): " + why + "." + (why.Contains("earlier version") ? " Make a new USB and SD backup of that drive with this version." : " Back up that folder, or the whole drive, with USB and SD Backup.") + " Nothing was changed.");
                if (!IsUsbOrSd(id)) return (null, "The chosen backup is of " + (id.Display ?? "a folder") + ", which is not a USB drive or SD card. Choose a backup of the USB drive or SD card you use with this console. Nothing was changed.");
                var key = DriveOf(id);
                if (driveKey != null && key != driveKey) return (null, "The places this mod changes are in the chosen backup on more than one drive (" + drive.Display + " and " + id.Display + "). Choose a backup of the one USB drive or SD card you use with this console. Nothing was changed.");
                driveKey = key; drive = drive ?? id; modes.Add(src.S("mode"));
                b.Cover.Add((loc, src.S("label") ?? id.Display, how));
            }
            b.DriveKey = driveKey; b.Display = drive.Display; b.Label = drive.Label; b.FileSystem = drive.FileSystem; b.VolumeSerial = drive.VolumeSerial;
            b.StorageBytes = drive.Kind == SourceKind.Disk && drive.DiskBytes > 0 ? drive.DiskBytes : drive.TotalBytes; b.Mode = string.Join("+", modes.OrderBy(x => x, StringComparer.Ordinal));
            return (b, null);
        }

        /// <summary>null when the file (held by the caller) is still exactly the bound backup and still covers every place; otherwise the reason.
        /// verification: the new check (for the recovery set)</summary>
        public static string Recheck(CommunityStorageBinding b, Func<bool> cancelled, out StorageVerification verification)
        {
            verification = null;
            if (b == null || string.IsNullOrEmpty(b.Path) || !Hex.IsHex(b.Sha256, 64)) return "The plan does not name its USB and SD backup completely.";
            if (!File.Exists(b.Path)) return "The USB and SD backup is missing (" + b.Path + ").";
            var (again, why) = Bind(b.Path, b.Locations, cancelled, out verification);
            if (again == null) return why.Replace(" Nothing was changed.", "");
            if (again.Sha256 != b.Sha256 || again.Bytes != b.Bytes || again.BackupId != b.BackupId) return "The USB and SD backup changed after the review.";
            if (again.DigestLine() != b.DigestLine()) return "The USB and SD backup does not match what the review saw.";
            return null;
        }
    }
}
