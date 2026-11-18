// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The Backup Browser's model (no UI here; the window builds on it). One uniform entry for everything a backup holds:
//   - every member of the backup file itself (the storage images, the reference copies, the file list, the .ccs export, the README and
//     the manifest), with virtual folders for the member name prefixes (restore/, reference/, ...);
//   - every entry inside the captured data storage image (mounted at /var/lib on the console): files, folders, links and special files,
//     read with the engine's own read-only ext4 reader;
//   - every entry inside a system image the backup holds (a SquashFS console system root: a file on the data storage such as CCS Nucleus's
//     /ccs/nucleus/roots/*.hsqs or hakchi's /hakchi/firmware/*.hsqs, or a storage part that is one), and every file of the nested CCS HOME
//     package (CCS/Console-HOME.ccs), each under the entry that holds it (its Container; BackupNested.cs);
//   - the console's own system part (nandb) decrypted on this PC with the key a startup image in the same backup carries, read only, and
//     listed the same way when what it decrypts to is a valid SquashFS file system (BackupStartup.cs);
//   - every part of a startup image (an Android boot image: its kernel, its ramdisk, a second stage) and every file of its ramdisk.
// Each entry carries its metadata (size, permissions, owner, group, inode, link count, the exact modification time as it was recorded),
// its SHA-256 and where that checksum comes from (HashOrigin: recorded when the backup was made, worked out on this PC from a verified
// parent and bound to that parent's checksum, or none), its By-type category with the reason (Classifier) and, when documented structures
// link it to a game, the game it belongs to (GameGroup). Nothing is dropped: an entry that no rule recognizes is "Everything else". Links
// and special files are entries too: they are described, never followed or created on the PC.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CcsBackup.Browse
{
    public enum Category { Games, Saves, SuspendPoints, Artwork, ModsAndEmulators, Settings, SystemFiles, EverythingElse }
    /// <summary>ImageFile: inside a system image (its Container); PackageFile: inside the nested CCS HOME package (its Container);
    /// StartupPart: a part of a startup image (its Container: the member); RamdiskFile: a file of a startup image's ramdisk (its Container:
    /// the ramdisk part)</summary>
    public enum EntrySource { Member, DataFile, VirtualFolder, ImageFile, PackageFile, StartupPart, RamdiskFile }
    public enum EntryKind { File, Folder, Link, Special }
    /// <summary>where an entry's SHA-256 comes from; only Manifest, Inventory, PackageList, Derived and DerivedIndex are trusted for a
    /// verified export. Recorded when the backup was made: Manifest (the backup's own list), Inventory (the backup's file list),
    /// PackageList (the nested package's own list of its files, inside a part that matches the backup's checksum). Derived and DerivedIndex:
    /// worked out on this PC by reading the file through a verified parent, never a checksum from the time the backup was made (BoundTo).</summary>
    public enum HashOrigin { None, Manifest, Inventory, Derived, DerivedIndex, Unreadable, PackageList }

    public static class Categories
    {
        public static readonly Category[] All = (Category[])Enum.GetValues(typeof(Category));
        public static string Label(Category c)
        {
            switch (c)
            {
                case Category.Games: return "Games"; case Category.Saves: return "Saves"; case Category.SuspendPoints: return "Suspend points";
                case Category.Artwork: return "Artwork"; case Category.ModsAndEmulators: return "Mods and emulators"; case Category.Settings: return "Settings";
                case Category.SystemFiles: return "System files"; default: return "Everything else";
            }
        }
    }

    public sealed class BrowseEntry
    {
        public string Id, ParentId, Name;
        public EntrySource Source; public EntryKind Kind; public char TypeCode;   // TypeCode: the ext4 type letter (f d l c b p s ?), 'f' for members
        public string Member;        // the backup member this entry is (Source Member) or lives in (Source DataFile: the data storage image)
        public string ImagePath;     // inside the data storage image ("/clover/profiles/0/..."), DataFile only
        public string ConsolePath;   // where it is on the console ("/var/lib/clover/profiles/0/..."), DataFile only
        public long Size; public int Mode, Uid, Gid, Links; public uint Inode; public string Owner;
        public string LinkTarget;    // a link's target as written on the console (never followed)
        public string Unreadable;    // why its bytes cannot be read on the PC (null: readable)
        public string StateWord;     // a short label beside its name when it cannot be copied ("damaged", "cannot be checked"); null otherwise
        // the modification time exactly as the console's file system recorded it (seconds since 1970 and nanoseconds), by the console's clock
        public long? MtimeSeconds; public int MtimeNanoseconds; public string DateNote;
        public string Sha256; public HashOrigin HashOrigin; public string HashText;
        public Category Category = Category.EverythingElse; public string CategoryReason = "No rule recognizes it.";
        public string GameKey, Role, GameRelative; public int SuspendSet = -1;
        public bool IsImage; public string ImageType, ImageNote;   // a storage image (member) or system image (file): what it holds and whether it is browsed file by file
        public string ImageSystem;   // a system image identified by hakchi's own rules (SystemImage.cs): "NES Classic system (USA)", with its recorded version
        public bool IsVirtual => Source == EntrySource.VirtualFolder;
        // an entry inside a system image or the nested package: the entry that holds it, its path inside (InnerPath: "/etc/clover/REGION" in
        // an image, "roms/x.nes" in the package), and how it is found again (InnerRef: the image's inode reference; Embedded: a picture or
        // design carried inside collection.json); NestedPath: the holder's path joined with the inner path
        public BrowseEntry Container; public string InnerPath, NestedPath, Embedded; public long InnerRef;
        // a checksum worked out on this PC (Derived, DerivedIndex): the verified parent it was read through and that parent's SHA-256
        public string BoundTo, BoundToSha256;
        // where the date comes from (null: the console's clock)
        public string DateSource;
        // a file inside the console's system part decrypted on this PC: the startup image whose key opened it and that image's SHA-256
        // (the key itself is never in an entry)
        public string KeyFrom, KeyFromSha256;
        // key material (the console's system key file): why its bytes are never shown; this separate entry is exported only when it is
        // itself selected. Whole files that can contain the key (BackupContents.CanHoldKey) are ordinary files and export as they are.
        public string Private;

        /// <summary>the path people see: the console path for files from the data storage, the holder's path and the path inside it for
        /// files in a system image or the package, the member name for parts of the backup file</summary>
        public string DisplayPath => ConsolePath ?? NestedPath ?? Member ?? Name;

        /// <summary>where it is in the backup file: a member, a member and a path inside it, and so on through each holder</summary>
        public string Locator => Embedded != null ? Container.Locator + ":collection.json#" + Embedded : Container != null ? Container.Locator + ":" + InnerPath : Source == EntrySource.DataFile ? Member + ":" + ImagePath : Member ?? Name;

        /// <summary>the checksum's provenance in a few plain words (shown and recorded beside every exported file)</summary>
        public string ChecksumKind => HashOrigin == HashOrigin.Manifest || HashOrigin == HashOrigin.Inventory || HashOrigin == HashOrigin.PackageList ? "recorded when the backup was made"
            : (HashOrigin == HashOrigin.Derived || HashOrigin == HashOrigin.DerivedIndex) && Sha256 != null ? "worked out on this PC from a verified parent"
            : HashOrigin == HashOrigin.Unreadable ? "none (it cannot be checked)" : "none yet";

        /// <summary>the modification time in UTC (null when there is none or it cannot be shown as a date)</summary>
        public DateTime? MtimeUtc
        {
            get
            {
                if (MtimeSeconds == null) return null;
                long s = MtimeSeconds.Value; if (s < -62135596800L || s > 253402300799L) return null;
                return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(s * TimeSpan.TicksPerSecond + MtimeNanoseconds / 100);
            }
        }

        public string DateText => MtimeUtc == null ? "No date" : MtimeUtc.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC (" + (DateSource ?? "console clock") + ")";
        public bool HashTrusted => Sha256 != null && (HashOrigin == HashOrigin.Manifest || HashOrigin == HashOrigin.Inventory || HashOrigin == HashOrigin.PackageList || HashOrigin == HashOrigin.Derived || HashOrigin == HashOrigin.DerivedIndex);
        public override string ToString() => DisplayPath;
    }

    /// <summary>one game and everything a documented structure links to it: its game files, battery saves, suspend-point sets, artwork,
    /// settings and other files found in its folders</summary>
    public sealed class GameGroup
    {
        public string Key, Title, TitleSource, Code, ContentSha256, Kind; public bool TitleKnown; public List<string> OtherTitles = new List<string>();
        public readonly List<BrowseEntry> Entries = new List<BrowseEntry>();
        public BrowseEntry BoxArt;
        public IEnumerable<BrowseEntry> GameFiles => Entries.Where(e => e.Role == "game-file" || e.Role == "game-entry");
        public IEnumerable<BrowseEntry> BatterySaves => Entries.Where(e => e.Role == "battery-save");
        public IEnumerable<BrowseEntry> Artwork => Entries.Where(e => e.Category == Category.Artwork);
        public IEnumerable<BrowseEntry> SettingsFiles => Entries.Where(e => e.Role == "save-details" || e.Role == "settings");
        public IEnumerable<BrowseEntry> OtherFiles => Entries.Where(e => e.Role == "other");
        /// <summary>the copies of its game file inside the nested CCS HOME package (CCS/Console-HOME.ccs)</summary>
        public IEnumerable<BrowseEntry> PackageCopies => Entries.Where(e => e.Role == "package-copy");
        /// <summary>suspend points by set (1-4; 0: Classic Cart Studio's automatic continuation from older versions); companion files stay together</summary>
        public SortedDictionary<int, List<BrowseEntry>> SuspendPoints
        {
            get
            {
                var d = new SortedDictionary<int, List<BrowseEntry>>();
                foreach (var e in Entries.Where(x => x.Role == "suspend-point")) { if (!d.TryGetValue(e.SuspendSet, out var l)) d[e.SuspendSet] = l = new List<BrowseEntry>(); l.Add(e); }
                return d;
            }
        }
        /// <summary>the newest console-clock date among its saves and suspend points (labeled as the console's clock, which may be wrong)</summary>
        public DateTime? LatestSaveUtc => Entries.Where(e => e.Role == "battery-save" || e.Role == "suspend-point").Select(e => e.MtimeUtc).Where(t => t != null).DefaultIfEmpty(null).Max();
        public long Bytes => Entries.Where(e => e.Kind == EntryKind.File).Sum(e => e.Size);
        public override string ToString() => Title;
    }

    /// <summary>the people's multi-selection of entries (by id), in any view</summary>
    public sealed class Selection
    {
        readonly HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        public int Count => ids.Count;
        public bool Contains(BrowseEntry e) => e != null && ids.Contains(e.Id);
        public void Add(BrowseEntry e) { if (e != null) ids.Add(e.Id); }
        public void AddRange(IEnumerable<BrowseEntry> es) { foreach (var e in es) Add(e); }
        public void Remove(BrowseEntry e) { if (e != null) ids.Remove(e.Id); }
        public bool Toggle(BrowseEntry e) { if (e == null) return false; if (!ids.Remove(e.Id)) { ids.Add(e.Id); return true; } return false; }
        public void Clear() => ids.Clear();
        public List<BrowseEntry> Entries(BackupContents c) => ids.Select(c.Find).Where(e => e != null).OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
    }
}
