// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Pictures for the Sega Mini menu: the composite picture Hakchi2 CE 3.9.3 makes beside a game's or a folder's own pictures
// (Apps/NesMenuElementBase.cs: <icon file name>_mdmini<its extension>; Apps/NesMenuFolder.cs: <code>_mdmini.png) and copies to the console
// with the game. The NES and SNES Classic's own menus do not use it; a Sega Mini menu started on the console does. Classic Cart Studio and
// CCS Console Workbench never write one. This is the one rule for which existing files may be offered for removal and which are only shown:
//   eligible  a regular file (not a link) with one name (its stored data not shared with another name), directly in a hakchi game or
//             folder entry (/hakchi/games/.../<folder>/ holding <folder>.desktop), named exactly as Hakchi2 CE names that entry's picture
//             for the Sega Mini menu, readable, and named by no menu entry (.desktop file) on the data storage
//   kept      every other file whose name ends in _mdmini.png on the data storage: its ownership is unknown or shared, so it stays, with why
// There is no wildcard: a file is offered only by this rule, one by one. Each is counted once (by its stored data, the inode).
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CcsBackup.Browse
{
    public sealed class SegaPicture
    {
        public BrowseEntry Entry; public string Path;    // Path: on the data storage, without the leading slash ("hakchi/games/...")
        public long Bytes, Allocated; public uint Inode; public string Sha256, Game;
    }

    public sealed class SegaPictureSet
    {
        public readonly List<SegaPicture> Eligible = new List<SegaPicture>();
        public readonly List<(BrowseEntry entry, string why)> Kept = new List<(BrowseEntry, string)>();
        public bool Complete = true; public string Incomplete;
        public long Bytes => Eligible.Sum(p => p.Bytes);
        /// <summary>the space the files take on the data storage, each stored file once; null when any is not known</summary>
        public long? Allocated => Eligible.Any(p => p.Allocated < 0) ? (long?)null : Eligible.Sum(p => p.Allocated);
    }

    public static class SegaPictures
    {
        public const string Role = "sega-composite";
        public const string Title = "Pictures for the Sega Mini menu";
        public const string Explanation = "These pictures are only used if you start a Sega Mini menu on this console.";
        public const string Recreated = "A later sync by another program, such as Hakchi2 CE, may make them again.";
        public const string Suffix = "_mdmini";

        /// <summary>the names Hakchi2 CE gives an entry's picture for the Sega Mini menu: from its icon file (a game) and from its code (a folder)</summary>
        public static HashSet<string> NamesFor(string code, string icon)
        {
            var s = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(code)) s.Add(code + Suffix + ".png");
            if (!string.IsNullOrEmpty(icon) && icon.IndexOf('/') < 0)
            {
                var ext = System.IO.Path.GetExtension(icon); var stem = System.IO.Path.GetFileNameWithoutExtension(icon);
                if (stem.Length > 0 && ext.Length > 0) s.Add(stem + Suffix + ext);
            }
            return s;
        }

        /// <summary>a name that looks like such a picture (shown in the kept list when the rule does not offer it)</summary>
        public static bool LooksLike(string name) => name != null && name.EndsWith(Suffix + ".png", StringComparison.OrdinalIgnoreCase);

        /// <summary>the eligible and kept pictures among a classified data storage's entries. desktopText reads a .desktop file (null: it
        /// cannot be read, and then no picture is offered, because a menu entry might name it). complete: the listing is the whole data
        /// storage (a damaged listing offers nothing).</summary>
        public static SegaPictureSet Find(IEnumerable<BrowseEntry> data, Func<BrowseEntry, string> desktopText, bool complete = true)
        {
            var set = new SegaPictureSet();
            var all = data.Where(e => e.Source == EntrySource.DataFile && e.ImagePath != null).ToList();
            var looks = all.Where(e => LooksLike(e.Name)).OrderBy(e => e.ImagePath, StringComparer.Ordinal).ToList();
            if (looks.Count == 0) return set;
            if (!complete) { set.Complete = false; set.Incomplete = "the backup's list of the data storage is not complete"; }
            // every value of every menu entry on the data storage: a picture an entry names is in use there
            var named = new List<string>(); string unreadable = null;
            foreach (var d in all.Where(e => e.Kind == EntryKind.File && e.Name.EndsWith(".desktop", StringComparison.Ordinal)))
            {
                var t = desktopText(d);
                if (t == null) { unreadable = unreadable ?? d.ImagePath; continue; }
                foreach (var line in t.Split('\n')) { var i = line.IndexOf('='); if (i > 0) named.Add(line.Substring(i + 1)); }
            }
            if (unreadable != null) { set.Complete = false; set.Incomplete = set.Incomplete ?? "a menu entry could not be read (" + unreadable + ")"; }
            var inodes = new HashSet<uint>();
            foreach (var e in looks)
            {
                string why = null;
                if (!set.Complete) why = "Not offered: " + set.Incomplete + ", so whether a menu uses it is not known.";
                else if (e.Kind != EntryKind.File || e.TypeCode != 'f') why = "Kept: it is a " + (e.Kind == EntryKind.Link ? "link" : e.Kind == EntryKind.Folder ? "folder" : "special file") + ", not a regular file.";
                else if (e.Role != Role) why = e.Role == "box-art" ? "Kept: a menu entry shows it as its picture." : "Kept: it is not where Hakchi2 CE puts a game's or folder's picture for the Sega Mini menu, or not named as it names one, so who it belongs to is not known.";
                else if (!e.ImagePath.StartsWith("/hakchi/games/", StringComparison.Ordinal)) why = "Kept: it is outside hakchi's game folders.";
                else if (e.Links != 1) why = "Kept: its stored data is shared with another name (" + e.Links + " names).";
                else if (e.Unreadable != null) why = "Kept: it cannot be read from the backup (" + e.Unreadable + ").";
                else if (named.Any(v => v.IndexOf(e.Name, StringComparison.Ordinal) >= 0)) why = "Kept: a menu entry names it.";
                else if (!FilesEngine.PathOk(e.ImagePath.Substring(1))) why = "Kept: its name is not one Workbench changes.";
                else if (!inodes.Add(e.Inode)) why = "Kept: its stored data is counted with another name already.";
                if (why != null) { set.Kept.Add((e, why)); continue; }
                set.Eligible.Add(new SegaPicture { Entry = e, Path = e.ImagePath.Substring(1), Bytes = e.Size, Allocated = e.Allocated, Inode = e.Inode, Sha256 = e.Sha256, Game = e.GameKey });
            }
            return set;
        }

        /// <summary>the shared storage account of removing the eligible pictures (StorageSpec: each stored file once, by its inode), with the
        /// data storage's figures: measured ones when given, otherwise the model's dated reference</summary>
        public static JObj Account(SegaPictureSet set, string profile, long? available, long? block)
        {
            var storage = available == null ? null : new Dictionary<string, object> { ["source"] = "measured", ["available_bytes"] = available.Value };
            if (storage != null && block != null) storage["block_bytes"] = block.Value;
            return StorageSpec.Account(new Dictionary<string, object>
            {
                ["profile"] = profile, ["storage"] = storage, ["items"] = new object[0],
                ["removed_after"] = set.Eligible.Select(p => (object)new Dictionary<string, object> { ["label"] = p.Path, ["allocated_bytes"] = p.Allocated >= 0 ? (object)p.Allocated : null, ["key"] = "inode:" + p.Inode }).ToArray(),
            });
        }
    }
}
