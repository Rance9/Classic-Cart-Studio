// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Export from an opened backup to a folder the person chose: a selection, search results, a whole By-type category, one or more game
// bundles, or a folder of the All files tree. The backup is only read. Each export goes into a NEW folder inside the chosen one, named
// with the request's FolderName: a suggestion that fits what is exported (ExportNames: the game's title for one game's files, the kind,
// the folder or the search; never a console's name or ID), which the person can change before the export starts. Its name is made safe
// and unique (" (2)", " (3)" ... when the name is taken; never an existing folder), and every name inside it is rebuilt by
// SafeNames/ExportPaths, so nothing from the backup can
// reach outside it (no "..", absolute, UNC, device or drive paths, no alternate data streams, no reserved device names) and nothing is
// ever written over: files are created new, and a folder the export needs is checked to be a real folder, not a link or junction.
// For every file:
//   1. its expected SHA-256 is the trusted one (the backup's manifest or file list, or worked out through the verified data storage
//      image; BackupContents.ExpectedHash). Without a trusted checksum a file is not exported;
//   2. it is streamed (bounded buffers, any size, files above 64,000,000 bytes included) into a new temporary file in its final folder,
//      hashed on the way, flushed and closed; the bytes read must match the expected checksum and size;
//   3. the temporary file is read back from the disk and hashed again; only an exact match is renamed to its final name;
//   4. its modification time is set to the console's recorded time when Windows can store it; a date the console's clock got wrong (before
//      2016, after the backup was made, or zero) is still set as recorded and reported, and one Windows cannot store is reported.
// A cancel, a read error or any mismatch removes that file's temporary copy; the file and every file not reached are listed as not
// exported, and the export is never reported complete. Links and special files (FIFOs, devices, sockets) are written into the manifest as
// descriptions only: nothing is created for them on the PC and a link is never followed. Hard links are exported once per name.
// The manifest (export-manifest.json, always written, also after a cancel) lists the backup it came from, what was asked for (the
// selection, the search results or the whole category, so the three are never confused), every file with its original path, its place in
// the export, size, SHA-256, where that checksum comes from (recorded when the backup was made, or worked out on this PC and bound to the
// verified parent it was read through: checksum_bound_to) and its date, and everything that was not exported and why. A file inside a
// system image or the nested CCS HOME package goes under "Files inside <its holder>" (by game when a game groups it), by its path there.
// The console's separate system key file (in a startup image's ramdisk, BrowseEntry.Private) is exported only when it is itself one of the
// entries asked for in a selection; with a folder, a type, a search or a game it is skipped and listed as withheld, with the reason. Skipping
// it does not take the key out of anything else: whole startup images, ramdisks and raw NAND images (BackupContents.CanHoldKey) are exported
// exactly as the backup holds them, and the summary and the manifest's system_key say they can contain the key. Neither ever says an export
// holds no key.
using CcsBackup.Ext4;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsBackup.Browse
{
    public enum ExportScope { Selection, SearchResults, WholeCategory, Game, Folder }
    public enum ExportLayout { ByGame, OriginalPaths }

    public sealed class ExportRequest
    {
        public string Destination, FolderName, ScopeText, Search; public ExportScope Scope; public ExportLayout Layout = ExportLayout.ByGame;
        public Category? Category; public int ShownCount = -1, CategoryCount = -1;
        public readonly List<BrowseEntry> Entries = new List<BrowseEntry>();

        /// <summary>Export All of This Type: every entry of the category, whatever the view shows</summary>
        public static ExportRequest WholeCategory(BackupContents c, Category cat, string destination)
        {
            var all = c.ByType(cat); var r = new ExportRequest { Destination = destination, Scope = ExportScope.WholeCategory, Category = cat, CategoryCount = all.Count, ShownCount = all.Count };
            r.Entries.AddRange(all); r.ScopeText = "All " + all.Count + " item(s) in " + Categories.Label(cat) + " (the whole category, not filtered)."; r.FolderName = ExportNames.Kind(cat); return r;
        }

        /// <summary>only what a search (in a category, or everywhere) shows</summary>
        public static ExportRequest SearchResults(BackupContents c, Category? cat, string search, IEnumerable<BrowseEntry> shown, string destination, ExportLayout layout = ExportLayout.ByGame)
        {
            var list = shown.ToList(); var r = new ExportRequest { Destination = destination, Scope = ExportScope.SearchResults, Category = cat, Search = search, ShownCount = list.Count, Layout = layout };
            r.CategoryCount = cat == null ? c.Entries.Count(e => e.Kind != EntryKind.Folder) : c.ByType(cat.Value).Count;
            r.Entries.AddRange(list);
            r.ScopeText = "Only the " + list.Count + " search result(s) for \"" + search + "\"" + (cat == null ? "" : " in " + Categories.Label(cat.Value)) + " (filtered: " + list.Count + " of " + r.CategoryCount + ").";
            r.FolderName = ExportNames.Matching(cat, search); return r;
        }

        /// <summary>only the entries the person selected (folders bring everything inside them)</summary>
        public static ExportRequest Selected(BackupContents c, Selection sel, string destination, ExportLayout layout = ExportLayout.ByGame)
        {
            var list = sel.Entries(c); var r = new ExportRequest { Destination = destination, Scope = ExportScope.Selection, Layout = layout, ShownCount = list.Count };
            r.Entries.AddRange(list); r.ScopeText = "Only the " + list.Count + " selected item(s)" + (list.Any(e => e.Kind == EntryKind.Folder) ? ", with everything inside the selected folders." : "."); r.FolderName = ExportNames.ForEntries(c, list); return r;
        }

        /// <summary>one entry on its own (Export This File, Export This Image, Export the Key File), with its place as stored</summary>
        public static ExportRequest One(BackupContents c, BrowseEntry e, string destination)
        {
            var r = new ExportRequest { Destination = destination, Scope = ExportScope.Selection, Layout = ExportLayout.OriginalPaths, ShownCount = 1, ScopeText = "One item: " + e.DisplayPath + "." };
            r.Entries.Add(e); r.FolderName = ExportNames.ForEntries(c, r.Entries); return r;
        }

        /// <summary>everything inside a file that holds files (the data storage image, a system image, a startup image, the CCS HOME
        /// package), with their original folders</summary>
        public static ExportRequest Inside(BackupContents c, BrowseEntry holder, string destination)
        {
            var r = new ExportRequest { Destination = destination, Scope = ExportScope.Folder, Layout = ExportLayout.OriginalPaths, ScopeText = "Everything inside " + holder.DisplayPath + ", with its original folders." };
            r.FolderName = holder.Member == c.DataMember && holder.Source == EntrySource.Member ? "Console files" : "Files inside " + holder.Name;
            r.Entries.AddRange(c.Children(holder.Id)); return r;
        }

        /// <summary>one game's bundle: its game files, battery saves, suspend points, artwork, settings and other files</summary>
        public static ExportRequest GameBundle(BackupContents c, GameGroup g, string destination)
        {
            var r = new ExportRequest { Destination = destination, Scope = ExportScope.Game, FolderName = ExportNames.Title(g), ShownCount = g.Entries.Count };
            r.Entries.AddRange(g.Entries); r.ScopeText = "Everything found for " + g.Title + " (" + g.Entries.Count + " item(s))."; return r;
        }

        /// <summary>several games' bundles, each in its own folder</summary>
        public static ExportRequest GameBundles(BackupContents c, IEnumerable<GameGroup> games, string destination)
        {
            var gs = games.ToList(); var r = new ExportRequest { Destination = destination, Scope = ExportScope.Game, FolderName = gs.Count == 1 ? ExportNames.Title(gs[0]) : ExportNames.SeveralGames, ShownCount = gs.Count };
            foreach (var g in gs) r.Entries.AddRange(g.Entries);
            r.ScopeText = "Everything found for " + gs.Count + " game(s), each in its own folder."; return r;
        }

        /// <summary>All files: one folder (or member) with its original layout</summary>
        public static ExportRequest Folder(BackupContents c, BrowseEntry folder, string destination)
        {
            var r = new ExportRequest { Destination = destination, Scope = ExportScope.Folder, Layout = ExportLayout.OriginalPaths, FolderName = ExportNames.FolderOf(folder) };
            r.Entries.Add(folder); r.ScopeText = "Everything in " + folder.DisplayPath + ", with its original folders."; return r;
        }
    }

    /// <summary>The name an export's new folder is offered with (ExportRequest.FolderName): what is being exported, in plain words. One
    /// game's files: the game's title; one kind: the kind ("Saves"); one folder: its name; a search: what it matched; otherwise a neutral
    /// name. Never a console's name or ID, a backup file's name or a selection mode. The person can change it before the export starts;
    /// a name already used in the chosen folder gets " (2)", " (3)" ... (Exporter.FolderNameFor), and nothing is ever written over.</summary>
    public static class ExportNames
    {
        public const string Neutral = "Backup files", SeveralGames = "Games";
        const int Max = 60;

        /// <summary>a game's name for its folder: its title, its code when the title is not known</summary>
        public static string Title(GameGroup g) => g == null ? "Game files" : g.TitleKnown && !string.IsNullOrWhiteSpace(g.Title) ? g.Title : g.Code ?? "Game files";

        /// <summary>a kind of file as a folder name ("Everything else" reads as "Other files")</summary>
        public static string Kind(Category c) => c == Category.EverythingElse ? "Other files" : Categories.Label(c);

        /// <summary>search results: the kind they were found in and what was searched for ("Saves matching mario")</summary>
        public static string Matching(Category? c, string search)
        {
            var s = (search ?? "").Trim(); if (s.Length > 30) s = s.Substring(0, 30).TrimEnd();
            return (c == null ? "Files" : Kind(c.Value)) + (s.Length > 0 ? " matching " + s : "");
        }

        /// <summary>a folder of the All files tree: its own name (a part's folder such as "restore" included)</summary>
        public static string FolderOf(BrowseEntry folder) => string.IsNullOrWhiteSpace(folder?.Name) ? Neutral : folder.Name.TrimEnd('/');

        /// <summary>what fits these entries: one game, several games, one kind, one folder or one file; otherwise the neutral name</summary>
        public static string ForEntries(BackupContents c, IList<BrowseEntry> entries)
        {
            var list = (entries ?? new List<BrowseEntry>()).Where(e => e != null).ToList();
            if (list.Count == 0) return Neutral;
            var games = list.Select(e => e.GameKey).Distinct().ToList();
            if (games.Count == 1 && games[0] != null && c?.Game(games[0]) != null) return Title(c.Game(games[0]));
            if (games.All(k => k != null && c?.Game(k) != null)) return SeveralGames;
            if (list.Count == 1)
            {
                var e = list[0];
                if (e.Kind == EntryKind.Folder) return FolderOf(e);
                var n = e.Name ?? ""; var stem = System.IO.Path.GetFileNameWithoutExtension(n);
                return string.IsNullOrWhiteSpace(stem) ? (string.IsNullOrWhiteSpace(n) ? Neutral : n) : stem;
            }
            var files = list.Where(e => e.Kind != EntryKind.Folder).ToList();
            if (files.Count == list.Count && files.Select(e => e.Category).Distinct().Count() == 1) return Kind(files[0].Category);
            var parents = list.Select(e => e.ParentId).Distinct().ToList();
            if (parents.Count == 1 && parents[0] != null) { var p = c?.Find(parents[0]); if (p != null && p.Kind == EntryKind.Folder && !string.IsNullOrWhiteSpace(p.Name)) return FolderOf(p); }
            return Neutral;
        }

        /// <summary>a name the person typed (or the suggestion), as the folder will be named: made safe and at most 60 characters; null when
        /// nothing usable is left</summary>
        public static string Clean(string typed)
        {
            var t = (typed ?? "").Trim(); if (t.Length == 0) return null;
            var safe = SafeNames.Component(t, Max);
            return t.Trim('.', ' ').Length == 0 ? null : safe;
        }
    }

    public sealed class ExportProgress { public int ItemsDone, ItemsTotal; public long BytesDone, BytesTotal; public string Current, Phase; }

    public sealed class ExportedFile { public BrowseEntry Entry; public string RelativePath, Sha256, DateNote; public long Size; public bool DateSet; }

    public sealed class ExportResult
    {
        public string State = "refused", Problem, Folder, ManifestPath;   // State: complete | incomplete | canceled | refused
        public bool Ok => State == "complete";
        public readonly List<ExportedFile> Files = new List<ExportedFile>();
        public readonly List<(BrowseEntry entry, string reason)> NotExported = new List<(BrowseEntry, string)>();
        public readonly List<BrowseEntry> Described = new List<BrowseEntry>();   // links and special files: in the manifest only
        public readonly List<BrowseEntry> Withheld = new List<BrowseEntry>();    // separate key files not themselves selected: in the manifest only, never their bytes
        public readonly List<string> Folders = new List<string>(), Leftovers = new List<string>();
        public long Bytes;
        public IEnumerable<ExportedFile> DateNotes => Files.Where(f => f.DateNote != null);
        /// <summary>exported whole files that can contain the system key (startup images, ramdisks, raw NAND images), exactly as the backup holds them</summary>
        public List<ExportedFile> KeyHolders => Files.Where(f => BackupContents.CanHoldKey(f.Entry)).ToList();
        string KeyText
        {
            get
            {
                int h = KeyHolders.Count;
                return (Withheld.Count == 1 ? "; the separate system key file was skipped (select the key file itself to export it)"
                        : Withheld.Count > 1 ? "; the " + Withheld.Count + " separate system key files were skipped (select a key file itself to export it)" : "")
                    + (h > 0 ? "; " + h + " exported file(s) are whole startup images, ramdisks or raw NAND images, which can contain the system key: keep this export private" : "");
            }
        }
        public string Summary => State == "refused" ? Problem
            : State == "complete" ? Files.Count + " file(s) exported and checked (" + Engine.Units.Gb(Bytes) + ")" + (Described.Count > 0 ? "; " + Described.Count + " link(s) or special file(s) described in the export's list" : "")
                + KeyText + "."
            : (State == "canceled" ? "Export canceled. " : "Export not complete. ") + Files.Count + " file(s) were exported and checked; " + NotExported.Count + " item(s) were not exported (listed in the export's list)" + KeyText + ".";
    }

    public static class Exporter
    {
        public const string ManifestName = "export-manifest.json", Schema = "ccs-backup-export/1";
        public const int MaxPath = 240, MaxDestination = 150;
        static readonly DateTime WindowsEpoch = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        sealed class Skip : Exception { public Skip(string m) : base(m) { } }

        /// <summary>null when the destination can be used; otherwise why not, in plain words</summary>
        public static string CheckDestination(string dest)
        {
            if (string.IsNullOrWhiteSpace(dest)) return "Choose a folder to export to.";
            if (dest.StartsWith(@"\\", StringComparison.Ordinal) || dest.StartsWith("//", StringComparison.Ordinal)) return "Choose a folder on one of this PC's drives (network and device paths are not used for exports).";
            if (!Regex.IsMatch(dest, @"^[A-Za-z]:\\")) return "Choose a folder by its full path on one of this PC's drives.";
            if (dest.IndexOf(':', 2) >= 0 || dest.IndexOfAny(new[] { '*', '?', '"', '<', '>', '|' }) >= 0) return "The folder path is not valid.";
            string full; try { full = Path.GetFullPath(dest); } catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { return "The folder path is not valid."; }
            if (!string.Equals(full.TrimEnd('\\'), dest.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return "Write the folder's path out in full (without \".\" or \"..\" steps).";
            if (!Directory.Exists(full)) return "The folder does not exist.";
            if (full.TrimEnd('\\').Length > MaxDestination) return "The folder's path is too long for the files inside it; choose a folder closer to the top of the drive.";
            return null;
        }

        public static ExportResult Export(BackupContents c, ExportRequest r, Func<bool> cancelled = null, Action<ExportProgress> progress = null)
        {
            cancelled = cancelled ?? (() => false); var res = new ExportResult();
            var why = CheckDestination(r?.Destination); if (why != null) { res.Problem = why; return res; }
            var dest = Path.GetFullPath(r.Destination).TrimEnd('\\');
            // what is exported: the entries asked for, folders with everything inside them, each once, in a fixed order
            var seen = new HashSet<string>(StringComparer.Ordinal); var items = new List<BrowseEntry>();
            void Take(BrowseEntry e) { if (e == null || !seen.Add(e.Id)) return; items.Add(e); if (e.Kind == EntryKind.Folder) foreach (var d in c.Descendants(e)) Take(d); }
            foreach (var e in r.Entries) Take(e);
            if (r.Layout == ExportLayout.ByGame) items.RemoveAll(e => e.Kind == EntryKind.Folder && e.GameKey != null);
            items = items.OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
            if (items.Count == 0) { res.Problem = "Nothing is chosen to export."; return res; }
            // where each goes
            var alloc = new ExportPaths(); alloc.Reserve("", ManifestName, "export-manifest");
            var gameKeys = items.Where(e => e.GameKey != null && c.Game(e.GameKey) != null).Select(e => e.GameKey).Distinct().ToList();
            // one game's files (its bundle, or a selection of only its files): the new folder already carries the game's name, so no second
            // folder of that name inside it
            bool single = (r.Scope == ExportScope.Game || r.Scope == ExportScope.Selection) && gameKeys.Count == 1 && items.All(e => e.GameKey == gameKeys[0]);
            var gameNames = GameFolderNames(c, gameKeys);
            var plan = items.Select(e => (e, rel: Place(c, r, e, alloc, gameNames, single))).ToList();
            long need = plan.Where(p => p.e.Kind == EntryKind.File && p.e.Unreadable == null).Sum(p => p.e.Size) + 1000000;
            long free = Durable.FreeBytes(dest);
            if (free >= 0 && free < need) { res.Problem = "There is not enough free space in that folder: about " + Engine.Units.Gb(need) + " is needed and " + Engine.Units.Gb(free) + " is free. Nothing was exported."; return res; }
            string root;
            try { root = NewFolder(dest, ExportNames.Clean(r.FolderName) ?? ExportNames.Neutral); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { res.Problem = "The export folder could not be created there (" + ex.Message + "). Nothing was exported."; return res; }
            res.Folder = root;
            for (int i = 0; i < plan.Count; i++)   // a path that would be too long for Windows goes into "Long names", shortened (its original path stays in the manifest)
                if (root.Length + 1 + plan[i].rel.Length > MaxPath)
                {
                    var name = plan[i].e.Kind == EntryKind.Folder ? null : plan[i].e.Name;
                    var lf = alloc.Folder("", "Long names", "long-names");
                    plan[i] = (plan[i].e, name == null ? null : alloc.File(lf, SafeNames.Component(name, Math.Max(16, MaxPath - root.Length - lf.Length - 12)), plan[i].e.Id + "#long"));
                }
            long total = plan.Where(p => p.e.Kind == EntryKind.File).Sum(p => p.e.Size), done = 0; int k = 0; bool stop = false;
            foreach (var (e, rel) in plan)
            {
                k++;
                if (stop || cancelled()) { stop = true; res.NotExported.Add((e, "Canceled before it was exported.")); continue; }
                progress?.Invoke(new ExportProgress { ItemsDone = k - 1, ItemsTotal = plan.Count, BytesDone = done, BytesTotal = total, Current = e.DisplayPath, Phase = "export" });
                if (e.Kind == EntryKind.Folder)
                {
                    if (rel == null) { res.NotExported.Add((e, "Its path is too long to recreate as a folder on Windows.")); continue; }
                    try { Folder(root, rel); res.Folders.Add(rel); } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Skip) { res.NotExported.Add((e, "Its folder could not be created: " + ex.Message)); }
                    continue;
                }
                if (e.Kind != EntryKind.File) { res.Described.Add(e); continue; }
                if (e.Private != null && !(r.Scope == ExportScope.Selection && r.Entries.Contains(e))) { res.Withheld.Add(e); continue; }
                string tmp = null; long before = done;
                try
                {
                    if (e.Unreadable != null) throw new Skip(e.Unreadable);
                    var dir = Folder(root, Path.GetDirectoryName(rel) ?? "");
                    var expected = c.ExpectedHash(e, cancelled, n => progress?.Invoke(new ExportProgress { ItemsDone = k - 1, ItemsTotal = plan.Count, BytesDone = before, BytesTotal = total, Current = e.DisplayPath, Phase = "checksum" }));
                    if (expected == null) throw new Skip("No trusted checksum is known for it, so it cannot be exported as checked.");
                    var full = Path.Combine(root, rel);
                    tmp = Path.Combine(dir, "~ccs-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".part");
                    HashingSink h;
                    using (var f = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
                    {
                        h = new HashingSink(f, n => progress?.Invoke(new ExportProgress { ItemsDone = k - 1, ItemsTotal = plan.Count, BytesDone = before + n, BytesTotal = total, Current = e.DisplayPath, Phase = "export" }), cancelled);
                        c.CopyTo(e, h, cancelled); h.Finish(); f.Flush(true);
                    }
                    if (h.Count != e.Size || h.Sha256 != expected) throw new Skip("The bytes read from the backup do not match its checksum, so it was not exported.");
                    var back = Durable.HashFile(tmp, cancelled);
                    if (back.bytes != e.Size || back.sha256 != expected) throw new Skip("The copy written on this PC reads back differently, so it was not kept.");
                    if (File.Exists(full) || Directory.Exists(full)) throw new Skip("Something with its name appeared in the export folder; nothing was replaced.");
                    File.Move(tmp, full); tmp = null;
                    var (set, note) = SetTime(full, e);
                    res.Files.Add(new ExportedFile { Entry = e, RelativePath = rel, Sha256 = expected, Size = e.Size, DateSet = set, DateNote = note }); res.Bytes += e.Size;
                }
                catch (OperationCanceledException) { stop = true; res.NotExported.Add((e, "Canceled while it was being exported; its unfinished copy was removed.")); }
                catch (Skip ex) { res.NotExported.Add((e, ex.Message)); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Ext4Exception || ex is InvalidOperationException) { res.NotExported.Add((e, "It could not be exported: " + ex.Message)); }
                finally { if (tmp != null) { try { File.Delete(tmp); } catch (Exception) { } if (File.Exists(tmp)) res.Leftovers.Add(tmp); } }
                done = before + e.Size;
            }
            res.State = stop ? "canceled" : res.NotExported.Count > 0 ? "incomplete" : "complete";
            progress?.Invoke(new ExportProgress { ItemsDone = plan.Count, ItemsTotal = plan.Count, BytesDone = done, BytesTotal = total, Phase = "done" });
            try { res.ManifestPath = WriteManifest(c, r, res, root, plan.Count); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { res.Problem = "The export's list could not be written (" + ex.Message + ")."; if (res.State == "complete") res.State = "incomplete"; }
            return res;
        }

        // ------------------------------------------------------------------ places
        static Dictionary<string, string> GameFolderNames(BackupContents c, List<string> keys)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var k in keys) { var g = c.Game(k); d[k] = g.TitleKnown ? g.Title : g.Code ?? ("Unknown game " + (g.ContentSha256 ?? "").Substring(0, Math.Min(16, (g.ContentSha256 ?? "").Length))); }
            foreach (var same in d.GroupBy(kv => SafeNames.Component(kv.Value), StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1).ToList())
                foreach (var kv in same.ToList()) { var g = c.Game(kv.Key); d[kv.Key] = kv.Value + " (" + (g.Code ?? (g.ContentSha256 ?? "").Substring(0, Math.Min(16, (g.ContentSha256 ?? "").Length))) + ")"; }
            return d;
        }

        public static string RoleFolder(BrowseEntry e)
        {
            switch (e.Role)
            {
                case "game-file": case "game-entry": return "Game files";
                case "battery-save": return "Battery saves";
                case "suspend-point": return e.SuspendSet > 0 ? "Suspend point " + e.SuspendSet : "Automatic continuation";
                case "box-art": case "artwork": return "Artwork";
                case "save-details": return "Settings";
                case "package-copy": return "In " + (e.Container?.Name ?? "the package");
                default: return e.Category == Category.Artwork ? "Artwork" : e.Category == Category.Settings ? "Settings" : "Other files";
            }
        }

        static string Place(BackupContents c, ExportRequest r, BrowseEntry e, ExportPaths a, Dictionary<string, string> gameNames, bool single)
        {
            string parent = "";
            void Dir(string desired, string key) { parent = a.Folder(parent, desired, key); }
            var g = c.Game(e.GameKey);
            if (r.Layout == ExportLayout.ByGame && g != null && e.Kind != EntryKind.Folder)
            {
                var gk = "g:" + g.Key;
                if (!single) Dir(gameNames[g.Key], gk);
                var role = RoleFolder(e); var key = gk + "|" + role; Dir(role, key);
                var segs = (e.GameRelative ?? e.Name).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segs.Length == 0) segs = new[] { e.Name };
                for (int i = 0; i < segs.Length - 1; i++) { key += "/" + segs[i]; Dir(segs[i], key); }
                return a.File(parent, segs[segs.Length - 1], e.Id);
            }
            if (e.Container != null)
            {
                // inside a system image or the package: under "Files inside <its holder>", by its path there (one folder per holder)
                Dir("Files inside " + (e.Container.Container != null ? e.Container.Container.Name + " " + e.Container.Name : e.Container.Name), "x:" + e.Container.Id);
                var segs = e.InnerPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries); var key = "x:" + e.Container.Id;
                for (int i = 0; i < segs.Length - 1; i++) { key += "/" + segs[i]; Dir(segs[i], key); }
                return e.Kind == EntryKind.Folder ? a.Folder(parent, segs[segs.Length - 1], key + "/" + segs[segs.Length - 1]) : a.File(parent, segs[segs.Length - 1], e.Id);
            }
            if (e.Source == EntrySource.DataFile)
            {
                Dir("Console files", "d:");
                var segs = e.ImagePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries); var key = "d:";
                for (int i = 0; i < segs.Length - 1; i++) { key += "/" + segs[i]; Dir(segs[i], key); }
                return e.Kind == EntryKind.Folder ? a.Folder(parent, segs[segs.Length - 1], "d:" + e.ImagePath) : a.File(parent, segs[segs.Length - 1], e.Id);
            }
            Dir("Backup parts", "m:");
            var ms = (e.IsVirtual ? e.Name : e.Member).Split('/'); var mk = "m:";
            for (int i = 0; i < ms.Length - 1; i++) { mk += "/" + ms[i]; Dir(ms[i], mk); }
            return e.Kind == EntryKind.Folder ? a.Folder(parent, ms[ms.Length - 1], "m:/" + string.Join("/", ms)) : a.File(parent, ms[ms.Length - 1], e.Id);
        }

        /// <summary>a new, empty folder inside dest (never an existing one), for an export that writes its own files (the Storage report)</summary>
        internal static string NewExportFolder(string dest, string name) => NewFolder(dest, name);

        /// <summary>the name the export's new folder would get in dest now: the cleaned name, or "name (2)", "name (3)" ... when something
        /// there already has it (the export makes the same choice when it starts, and never uses an existing folder). Null when the name or
        /// the destination cannot be used. Nothing is created.</summary>
        public static string FolderNameFor(string dest, string name)
        {
            var clean = ExportNames.Clean(name); if (clean == null || CheckDestination(dest) != null) return null;
            var d = Path.GetFullPath(dest).TrimEnd('\\');
            for (int n = 1; n < 10000; n++)
            {
                var nm = n == 1 ? clean : SafeNames.WithSuffix(clean, n, true);
                var p = Path.Combine(d, nm);
                if (!Directory.Exists(p) && !File.Exists(p)) return nm;
            }
            return null;
        }

        static string NewFolder(string dest, string name)
        {
            for (int n = 1; n < 10000; n++)
            {
                var p = Path.Combine(dest, n == 1 ? name : SafeNames.WithSuffix(name, n, true));
                if (Directory.Exists(p) || File.Exists(p)) continue;
                Directory.CreateDirectory(p);
                var a = File.GetAttributes(p);
                if ((a & FileAttributes.ReparsePoint) != 0 || Directory.EnumerateFileSystemEntries(p).Any()) continue;   // someone else's folder appeared at that moment
                return p;
            }
            throw new IOException("too many exports with the same name");
        }

        /// <summary>the folder at rel inside root, created when missing; every step must be a real folder (not a link or a junction) inside root</summary>
        static string Folder(string root, string rel)
        {
            var cur = root;
            foreach (var seg in rel.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!SafeNames.IsSafe(seg)) throw new Skip("An unsafe name was refused.");
                cur = Path.Combine(cur, seg);
                if (!Path.GetFullPath(cur).StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) throw new Skip("A path outside the export folder was refused.");
                if (!Directory.Exists(cur)) { if (File.Exists(cur)) throw new Skip("A file stands where the export needs a folder."); Directory.CreateDirectory(cur); }
                var at = File.GetAttributes(cur);
                if ((at & FileAttributes.ReparsePoint) != 0 || (at & FileAttributes.Directory) == 0) throw new Skip("A link stands where the export needs a folder; nothing was written through it.");
            }
            return cur;
        }

        static (bool set, string note) SetTime(string full, BrowseEntry e)
        {
            var t = e.MtimeUtc;
            if (t == null) return (false, e.DateNote ?? "No date was recorded for it; it has the date it was exported.");
            if (t.Value < WindowsEpoch) return (false, "Its console date (" + e.DateText + ") is earlier than Windows can store; it has the date it was exported.");
            try
            {
                File.SetLastWriteTimeUtc(full, t.Value);
                var back = File.GetLastWriteTimeUtc(full);
                return (true, e.DateNote ?? (back == t.Value ? null : "Windows stored its date less precisely than the console recorded it."));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) { return (false, "Its date could not be set (" + ex.Message + ")."); }
        }

        static object Time(BrowseEntry e) => e.MtimeSeconds == null ? null : new JObj
        {
            { "utc", e.MtimeUtc?.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture) }, { "seconds", e.MtimeSeconds.Value }, { "nanoseconds", (long)e.MtimeNanoseconds },
            { "source", e.DateSource ?? "the console's clock" }, { "note", e.DateNote },
        };

        static string WriteManifest(BackupContents c, ExportRequest r, ExportResult res, string root, int planned)
        {
            var holders = res.KeyHolders;
            var doc = new JObj
            {
                { "schema", Schema }, { "state", res.State }, { "complete", res.State == "complete" }, { "made_utc", Record.Now() },
                { "tool", new JObj { { "engine", Engine.EngineInfo.Engine }, { "version", Engine.EngineInfo.Version } } },
                { "backup", new JObj { { "file", c.Path }, { "backup_id", c.BackupId }, { "console", c.ConsoleTag }, { "made_utc", c.CompletedUtc }, { "archive_sha256", c.ArchiveSha256 }, { "simulated", c.Simulated }, { "salvage", c.Salvage } } },
                { "asked_for", new JObj { { "kind", r.Scope == ExportScope.WholeCategory ? "whole-category" : r.Scope == ExportScope.SearchResults ? "search-results" : r.Scope == ExportScope.Selection ? "selection" : r.Scope == ExportScope.Game ? "game-bundle" : "folder" },
                    { "text", r.ScopeText }, { "category", r.Category == null ? null : Categories.Label(r.Category.Value) }, { "search", r.Search }, { "shown", (long)r.ShownCount }, { "category_total", (long)r.CategoryCount }, { "items", (long)planned } } },
                { "dates", BackupContents.ClockText },
                { "checks", "Every file was read from the backup, compared with its expected SHA-256, written, read back from this PC's disk and compared again before it got its name." },
                { "files", res.Files.Select(f => (object)new JObj
                    { { "original_path", f.Entry.DisplayPath }, { "in_backup", f.Entry.Locator }, { "exported_as", f.RelativePath }, { "size", f.Size }, { "sha256", f.Sha256 },
                      { "checksum_from", f.Entry.HashOrigin.ToString().ToLowerInvariant() }, { "checksum_kind", f.Entry.ChecksumKind }, { "checksum_text", f.Entry.HashText },
                      { "checksum_bound_to", f.Entry.BoundTo == null ? null : new JObj { { "parent", f.Entry.BoundTo }, { "parent_sha256", f.Entry.BoundToSha256 },
                          { "decrypted_with_key_from", f.Entry.KeyFrom }, { "key_source_sha256", f.Entry.KeyFromSha256 } } },
                      { "game", c.GameTitle(f.Entry) }, { "category", Categories.Label(f.Entry.Category) },
                      { "console_date", Time(f.Entry) }, { "date_set", f.DateSet }, { "date_note", f.DateNote }, { "mode", Convert.ToString(f.Entry.Mode, 8).PadLeft(4, '0') }, { "uid", f.Entry.Uid }, { "gid", f.Entry.Gid },
                      { "hard_links", f.Entry.Links > 1 ? (object)(long)f.Entry.Links : null } }).ToList() },
                { "links_and_special_files", res.Described.Select(e => (object)new JObj
                    { { "original_path", e.DisplayPath }, { "kind", e.Kind == EntryKind.Link ? "link" : TypeName(e.TypeCode) }, { "target", e.LinkTarget }, { "console_date", Time(e) },
                      { "note", e.Kind == EntryKind.Link ? "Described here only: a link is never followed or created on this PC." : "Described here only: a special file has no contents to export." } }).ToList() },
                { "folders", res.Folders.Cast<object>().ToList() },
                { "not_exported", res.NotExported.Select(x => (object)new JObj { { "original_path", x.entry.DisplayPath }, { "reason", x.reason } }).ToList() },
                { "withheld", res.Withheld.Select(e => (object)new JObj { { "original_path", e.DisplayPath }, { "reason", WithheldReason } }).ToList() },
                { "system_key", res.Withheld.Count == 0 && holders.Count == 0 ? null : new JObj {
                    { "separate_key_files_skipped", (long)res.Withheld.Count },
                    { "whole_files_that_can_contain_it", holders.Select(f => (object)f.Entry.DisplayPath).ToList() },
                    { "note", holders.Count > 0 ? HoldersNote : SkippedOnlyNote } } },
                { "unfinished_copies_left", res.Leftovers.Cast<object>().ToList() },
            };
            var p = Path.Combine(root, ManifestName); var b = Json.Bytes(doc);
            using (var f = new FileStream(p, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(b, 0, b.Length); f.Flush(true); }
            return p;
        }

        public const string WithheldReason = "This is the separate system key file. It is skipped when a folder, a type, search results or a game is exported, and exported only when it is selected itself. Whole startup images, ramdisks and raw NAND images are exported exactly as the backup holds them and can still contain the key.";
        public const string HoldersNote = "The whole files listed here are exported exactly as the backup holds them: startup images, ramdisks or raw NAND images. They can contain the console's system key. Keep this export private.";
        public const string SkippedOnlyNote = "Only the separate key file entries listed under withheld were skipped. The other exported files were not searched for the key.";

        static string TypeName(char t) => t == 'p' ? "named pipe (FIFO)" : t == 'c' ? "character device" : t == 'b' ? "block device" : t == 's' ? "socket" : "unknown kind";
    }
}
