// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The Backup Browser inside the parts of a backup that hold other files (BackupContents, continued). Read-only, offline, nothing is ever
// extracted to disk: every file is read in place through the backup file the browser holds open.
//   System images   a SquashFS console system root, wherever the backup holds one: a file on the data storage whose bytes start as one
//                   (CCS Nucleus's own roots, /var/lib/ccs/nucleus/roots/*.hsqs; hakchi's firmware folder, /var/lib/hakchi/firmware/*.hsqs;
//                   anything else that is one), or a storage part of the backup that is one. Supported: SquashFS 4.0 with gzip or xz
//                   (LZMA2) contents (Device/SquashFs.cs). Each is identified by hakchi's own rules (Engine/SystemImage.cs: NES Classic,
//                   Famicom Mini or SNES Classic system, its region and recorded version) and every entry inside it is listed under it
//                   with its metadata. Another compression, an image whose superblock or file list cannot be read, or a part that is not a
//                   file system stays one whole item with the exact reason and what is missing (a decoder, the console's key); a whole item
//                   that matches its checksum can still be exported exactly as the backup holds it.
//   The system part the console's own system part (the layout's "rootfs" part) is stored as the console reads it from its storage. A
//                   plain SquashFS system part is browsed like any other system image. An encrypted one (the NES Classic's own) is
//                   decrypted on this PC, read only, with the system key a startup image in the same backup carries, and browsed the same
//                   way when what it decrypts to is a valid SquashFS file system; otherwise it stays one exact image with the precise
//                   reason (BackupStartup.cs, which also lists every startup image's parts and ramdisk files).
//   The package     CCS/Console-HOME.ccs, the console's CCS HOME as a .ccs collection package (Ext4/CcsExport.cs), read with the strict
//                   package reader (Compose/SafeZip.cs) straight from the backup file: its games, saves, artwork, the collection and its
//                   settings, and the startup picture and HOME design collection.json carries, each a file of its own.
// Checksums, and where each comes from:
//   recorded        a file of the package that its own list (ccs-manifest.json) records with its size and SHA-256 (PackageList), and the
//                   startup picture and design collection.json records the same way: recorded when the backup was made, inside a part
//                   that matches the backup's checksum;
//   derived         a file of a system image (an image keeps no checksum of each of its files), and a package file its list does not
//                   record: worked out on this PC by reading it through its holder, only when the holder's own checksum is trusted
//                   (recorded, or itself worked out from the verified data storage), and bound to that holder's path and SHA-256 (BoundTo,
//                   BoundToSha256). Never written into the backup, never presented as a checksum from the time the backup was made;
//   none            a holder that is damaged or cannot be checked is never opened: a damaged backup offers a system image only when the
//                   image file itself still matches the checksum the backup's file list recorded for it (the per-file proof).
using CcsBackup.Archive;
using CcsBackup.Compose;
using CcsBackup.Device;
using CcsBackup.Engine;
using CcsBackup.Ext4;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CcsBackup.Browse
{
    public sealed partial class BackupContents
    {
        /// <summary>at most this many entries inside system images and the package of one backup (each image also has its own limit)</summary>
        public const int MaxNestedEntries = 400000;
        const long MaxListJson = 64000000;
        const string Pkg = "the Classic Cart Studio HOME package";
        static readonly Regex Hex64Re = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant), SlotRe = new Regex(@"^slot([1-4])\.ccsstate$", RegexOptions.CultureInvariant);
        internal static readonly ZipLimits PackageLimits = new ZipLimits
        {
            What = "collection package", MaxMembers = 50000, MaxArchiveBytes = BackupFormat.MaxSize(BackupFormat.ExportName), MaxTotalBytes = 8000000000, MaxRatio = 400,
            MaxMemberBytes = n => 2000000000, Allowed = n => true,   // every safe name is listed; one a package never has is shown as unexpected
        };

        List<ZipEntryInfo> parts;   // a backup whose table of contents is damaged: the parts Backup Repair's part finder located and checked
        public string FoundBy { get; private set; }
        int nested;
        readonly Dictionary<string, List<BrowseEntry>> byNestedInode = new Dictionary<string, List<BrowseEntry>>(StringComparer.Ordinal);

        /// <summary>the backup file's parts: through its own table of contents, or (damaged) through the places the part finder checked</summary>
        ZipReader Zip(Stream f) => parts == null ? new ZipReader(f, BackupFormat.Allowed) : ZipReader.OfLocatedParts(f, parts);

        // ------------------------------------------------------------------ finding the holders
        /// <summary>Every system image, the system part's explanation and the package, with what is inside each (called once by Build, with the
        /// backup file open). data: the data storage's reader (null when none of its files can be read).</summary>
        void Nest(List<BrowseEntry> all, ZipReader z, Stream f, Dictionary<string, object> m, Ext4Reader data, List<GameGroup> games, Func<bool> cancelled)
        {
            var sysDev = (m?.O("layout")?.A("partitions") ?? new List<object>()).OfType<Dictionary<string, object>>().FirstOrDefault(p => p.S("label") == "rootfs")?.S("device");
            var sysMember = sysDev == null ? null : "restore/" + sysDev + ".img";
            var keys = games.ToDictionary(g => g.Key, StringComparer.Ordinal);
            var members = all.Where(x => x.Source == EntrySource.Member && x.Kind == EntryKind.File).ToList();
            // the startup images first (each one's parts and ramdisk files, and where its system key is), the named ones in the order the key
            // is looked for in them
            bool IsStartup(BrowseEntry e) => e.IsImage && e.Unreadable == null && !e.Member.StartsWith("reference/", StringComparison.Ordinal) && (e.ImageType ?? "").StartsWith("Startup software", StringComparison.Ordinal);
            foreach (var e in members.Where(IsStartup).OrderBy(e => Array.IndexOf(StartupMembers, e.Member) is int i && i >= 0 ? i : StartupMembers.Length).ThenBy(e => e.Member, StringComparer.Ordinal).ToList())
            {
                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                OpenStartup(e, z, all);
            }
            foreach (var e in members)
            {
                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                if (e.Member == BackupFormat.ExportName) { OpenPackage(e, z, all, keys); continue; }
                if (!e.IsImage || e.Unreadable != null || e.Member == DataMember || e.Member.StartsWith("reference/", StringComparison.Ordinal) || IsStartup(e)) continue;   // a damaged or missing part keeps its own note
                var ze = zip.TryGetValue(e.Member, out var zz) ? zz : null;
                var t = e.ImageType ?? "";
                if (t == "SquashFS file system" && ze != null && ze.Method == 0)
                    OpenSystemImage(e, () => z.StoredWindow(ze), all, keys, cancelled, e.Member == sysMember);
                else if (e.Member == sysMember && !t.StartsWith("SquashFS", StringComparison.Ordinal) && !t.StartsWith("ext2", StringComparison.Ordinal) && !t.StartsWith("FAT", StringComparison.Ordinal) && !t.StartsWith("Empty", StringComparison.Ordinal))
                    OpenEncryptedSystem(e, z, all, keys, cancelled);
                else MemberImageNote(e, e.Member == sysMember);
            }
            // an image file in a damaged data storage that has no per-file proof: never opened (nothing inside it could be checked)
            foreach (var e in all.Where(x => x.Source == EntrySource.DataFile && x.Kind == EntryKind.File && x.Unreadable != null && x.Name.EndsWith(".hsqs", StringComparison.OrdinalIgnoreCase)))
                e.ImageNote = "A system image file that cannot be checked against the checksum the backup's file list recorded for it, so nothing inside it is offered: a system image keeps no checksum of each of its files, so only its own recorded checksum could prove what is inside it.";
            if (data != null)
                foreach (var e in all.Where(x => x.Source == EntrySource.DataFile && x.Kind == EntryKind.File && x.Unreadable == null && x.Size >= 96).ToList())
                {
                    if (cancelled != null && cancelled()) throw new OperationCanceledException();
                    byte[] head; try { head = Head(data, e.Inode, 4); } catch (Ext4Exception) { head = null; }
                    if (head == null || head.Length < 4 || head[0] != 'h' || head[1] != 's' || head[2] != 'q' || head[3] != 's') continue;
                    if (!e.HashTrusted && e.HashOrigin != HashOrigin.None) continue;
                    OpenSystemImage(e, () => data.OpenFile(e.Inode), all, keys, cancelled, false);
                }
            games.Clear(); games.AddRange(keys.Values.Where(g => g.Entries.Count > 0).OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Key, StringComparer.Ordinal));
        }

        /// <summary>what a storage part that is not browsed holds, and exactly what is missing to list its files</summary>
        static void MemberImageNote(BrowseEntry e, bool systemPart)
        {
            var t = e.ImageType ?? "";
            string raw = e.Unreadable == null && e.HashTrusted ? " It can be exported as one exact image, checked against the backup's checksum." : "";
            if (systemPart && t.StartsWith("Empty", StringComparison.Ordinal)) { e.ImageNote = "The console's system part. It starts with zeros: no file system is recognized in it, and it is not decrypted, so no files are listed from it." + raw; return; }
            if (t.StartsWith("Bootloader", StringComparison.Ordinal)) e.ImageNote = "A startup stage of the console (a bootloader): a program, not a file system or a container of parts, so it has no files to list." + raw;
            else if (t.StartsWith("ext2", StringComparison.Ordinal)) e.ImageNote = "An ext2/3/4 file system that is not the data storage. This version lists the files of the data storage and of system images only, so its files are not listed here." + raw;
            else if (t.StartsWith("FAT", StringComparison.Ordinal)) e.ImageNote = "A FAT file system. This version has no FAT reader, which is what is missing to list its files." + raw;
            else if (t.StartsWith("Empty", StringComparison.Ordinal)) e.ImageNote = "It starts with zeros: no file system is recognized in it, so no files are listed from it." + raw;
            else if (t.StartsWith("SquashFS", StringComparison.Ordinal)) e.ImageNote = "A SquashFS file system that is not stored in a way it can be read in place, so its files are not listed." + raw;
            else e.ImageNote = "Not a file system this version recognizes (it may be encrypted, or raw data), so its files are not listed." + raw;
        }

        // ------------------------------------------------------------------ system images
        /// <summary>One system image (x: the file on the data storage or the storage part that is one): identified, then listed entry by
        /// entry under it; or kept whole with the reason. open: a new seekable stream of the image's exact bytes.</summary>
        void OpenSystemImage(BrowseEntry x, Func<Stream> open, List<BrowseEntry> all, Dictionary<string, GameGroup> games, Func<bool> cancelled, bool systemPart)
        {
            x.IsImage = true;
            bool dec = systemKeys.TryGetValue(x.Id, out var key);
            string raw = x.HashTrusted ? " The whole image can be exported exactly as the backup holds it" + (dec ? " (encrypted)" : "") + ", checked against its checksum." : "";
            string what = systemPart ? "the console's system part" : "this system image";
            string decText = dec ? "Decrypted on this PC, read only and in memory, with the system key in " + key.from.Member + " (" + key.foundIn + "), which matches its checksum, through the cipher the console's own startup uses (aes-xts-plain, 512-byte sectors); what it decrypts to was checked as a SquashFS file system before anything was listed. Nothing decrypted is written anywhere, and the exact encrypted image is what a restore puts back. " : "";
            Stream st; SquashFsReader sq;
            try { st = open(); sq = new SquashFsReader(st); }
            catch (Exception ex) when (ex is InvalidDataException || ex is Ext4Exception || ex is IOException)
            {
                x.ImageType = "Not a readable system image";
                x.ImageNote = "It starts as a SquashFS file system, but it is " + ex.Message + ", so its files cannot be listed." + raw; return;
            }
            if (!sq.CanRead)
            {
                x.ImageType = "SquashFS file system (" + sq.CompressionName + ")";
                x.ImageNote = "A SquashFS file system whose contents are compressed with " + sq.CompressionName + ". This version reads gzip and xz only, so its files are not listed: what is missing is a decoder for " + sq.CompressionName + " contents." + raw; return;
            }
            SystemImageInfo id;
            try { id = SystemImage.Identify(st); }
            catch (Exception ex) when (ex is Ext4Exception || ex is IOException) { id = new SystemImageInfo { Why = "could not be read (" + ex.Message + ")" }; }
            List<SquashFsReader.Node> nodes;
            try { nodes = sq.ListAll(cancelled, Math.Min(SquashFsReader.MaxEntries, MaxNestedEntries - nested)); }
            catch (Exception ex) when (ex is InvalidDataException || ex is NotSupportedException || ex is Ext4Exception || ex is IOException)
            {
                x.ImageType = (systemPart ? "System part (" + (dec ? "encrypted; " : "") + "SquashFS, " : "SquashFS file system (") + sq.CompressionName + "), damaged";
                x.ImageSystem = id.Ok ? id.Text + (id.Version != null ? ", version " + id.Version : "") : null;
                x.ImageNote = decText + "Its list of files cannot be read (" + ex.Message + "), so none of its files is shown: " + what + " keeps no checksum of each of its files, and nothing in a list that cannot be read is offered." + raw; return;
            }
            nested += nodes.Count;
            x.ImageSystem = id.Ok ? id.Text + (id.Version != null ? ", version " + id.Version : "") : null;
            x.ImageType = (systemPart ? "System part" : "System image") + (dec ? " (encrypted; SquashFS, " : " (SquashFS, ") + sq.CompressionName + ")" + (id.Ok ? ": " + id.Text : "");
            x.ImageNote = (id.Ok ? "A " + x.ImageSystem + ", identified by the rules hakchi itself applies. " : "A SquashFS file system; it " + id.Why + ". ") + decText
                + "Its " + nodes.Count + " entries are listed under it, read on this PC from the backup's copy of it" + (x.HashTrusted ? ", which matches its checksum" : "") + ". The image keeps no checksum of each of its files: a file's checksum is worked out from the " + (dec ? "decrypted " : "") + "image when it is previewed or exported, and bound to the image's own checksum" + (dec ? " (as it is stored, encrypted) and to the startup image the key came from" : "") + "." + raw;
            var list = new List<BrowseEntry>();
            foreach (var n in nodes)
            {
                int cut = n.Path.LastIndexOf('/'); var parent = n.Path.Substring(0, cut);
                var e = new BrowseEntry
                {
                    Id = "s:" + x.Id + "|" + n.Path, ParentId = parent.Length == 0 ? x.Id : "s:" + x.Id + "|" + parent, Name = n.Name, Source = EntrySource.ImageFile, Member = x.Member,
                    Container = x, InnerPath = n.Path, InnerRef = n.Ref, NestedPath = x.DisplayPath + n.Path, TypeCode = n.TypeCode, Mode = n.Mode, Uid = n.Uid, Gid = n.Gid, Links = n.Links, Inode = n.InodeNumber,
                    Kind = n.Type == 1 ? EntryKind.Folder : n.Type == 2 ? EntryKind.File : n.Type == 3 ? EntryKind.Link : EntryKind.Special, LinkTarget = n.LinkTarget, Owner = "system image",
                    MtimeSeconds = n.Mtime, DateSource = "as recorded in the system image", Category = Category.SystemFiles, CategoryReason = "Part of the system image " + x.Name + ".",
                };
                e.Size = e.Kind == EntryKind.File || e.Kind == EntryKind.Link ? n.Size : 0;
                if (n.Mtime == 0) e.DateNote = "No date was recorded in the system image.";
                if (e.Kind == EntryKind.File) { e.HashOrigin = HashOrigin.None; e.HashText = "Not worked out yet: it is worked out on this PC from the " + (dec ? "decrypted system part" : "system image") + (x.HashTrusted ? ", which matches its checksum," : "") + " when it is previewed or exported. The image keeps no checksum of each of its files."; }
                if (dec) { e.KeyFrom = key.from.Member; e.KeyFromSha256 = key.from.Sha256; }
                if (n.Type == 0) { e.TypeCode = '?'; e.Unreadable = "An entry of a kind this version does not know; it is listed, not read."; }
                list.Add(e); Add(all, e);
                if (e.Kind == EntryKind.File) { var k = x.Id + "|" + e.Inode; if (!byNestedInode.TryGetValue(k, out var l)) byNestedInode[k] = l = new List<BrowseEntry>(); l.Add(e); }
            }
            try { ClassifyImage(x, list, sq, games); } catch (Exception ex) when (ex is InvalidDataException || ex is NotSupportedException || ex is Ext4Exception || ex is IOException) { }
        }

        /// <summary>games inside a system image: a folder X under /usr/share/games/ holding X.desktop (the stock systems' own layout, the same
        /// rule as hakchi's game folders: its .desktop's Name=, Code=, Exec= and Icon=; a game file is also recognized by its NES header)</summary>
        static void ClassifyImage(BrowseEntry x, List<BrowseEntry> es, SquashFsReader sq, Dictionary<string, GameGroup> games)
        {
            var kids = es.GroupBy(e => e.InnerPath.Substring(0, e.InnerPath.LastIndexOf('/'))).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            byte[] Read(BrowseEntry e, long max) { if (e.Kind != EntryKind.File || e.Size > max) return null; var ms = new MemoryStream(); sq.ReadFile(e.InnerRef, ms, max); return ms.ToArray(); }
            foreach (var d in es.Where(e => e.Kind == EntryKind.Folder && e.InnerPath.StartsWith("/usr/share/games/", StringComparison.Ordinal)).OrderBy(e => e.InnerPath.Count(c => c == '/')))
            {
                if (!kids.TryGetValue(d.InnerPath, out var inside)) continue;
                var desk = inside.FirstOrDefault(k => k.Kind == EntryKind.File && k.Name == d.Name + ".desktop"); if (desk == null || desk.GameKey != null) continue;
                var b = Read(desk, 64000); var f = Classifier.Desktop(b == null ? null : Encoding.UTF8.GetString(b));
                var code = f.TryGetValue("Code", out var c0) && c0.Trim().Length > 0 ? c0.Trim() : d.Name;
                var key = "img:" + x.Id + "|" + code;
                if (!games.TryGetValue(key, out var g))
                {
                    var title = f.TryGetValue("Name", out var nm) && nm.Trim().Length > 0 ? nm.Trim() : null;
                    if (title != null && title.Length > 200) title = title.Substring(0, 200);
                    var stock = title == null && Classifier.StockGames.TryGetValue(code, out var st) ? st : null;
                    games[key] = g = new GameGroup { Key = key, Code = code, Title = title ?? stock ?? code, TitleKnown = title != null || stock != null, TitleSource = title != null ? "the game's entry in the system image (" + desk.DisplayPath + ")" : stock != null ? "the NES Classic's own game list" : "no name is recorded for it", Kind = "Game in the system image " + x.Name };
                }
                string Local(string v)
                {
                    if (string.IsNullOrEmpty(v)) return null;
                    foreach (var pre in new[] { d.InnerPath + "/", "/var/games/" + d.Name + "/" })
                        if (v.StartsWith(pre, StringComparison.Ordinal) && v.Length > pre.Length && v.IndexOf('/', pre.Length) < 0) return v.Substring(pre.Length);
                    return null;
                }
                var exec = new HashSet<string>(StringComparer.Ordinal);
                if (f.TryGetValue("Exec", out var ex)) foreach (var t in ex.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)) { var n = Local(t.Trim('"', '\'')); if (n != null) exec.Add(n); }
                var icon = f.TryGetValue("Icon", out var ic) ? Local(ic.Trim()) : null;
                var art = new HashSet<string>(StringComparer.Ordinal) { code + ".png", code + "_small.png", code + "_original.png", code + "_spine.png", code + "_logo.png" };
                void Set(BrowseEntry e, Category cat, string why, string role) { e.Category = cat; e.CategoryReason = why; e.GameKey = key; e.Role = role; e.GameRelative = e.InnerPath.Substring(d.InnerPath.Length + 1); if (e.Kind != EntryKind.Folder) g.Entries.Add(e); }
                IEnumerable<BrowseEntry> Under(string dir) { if (kids.TryGetValue(dir, out var l)) foreach (var k in l) { yield return k; if (k.Kind == EntryKind.Folder) foreach (var u in Under(k.InnerPath)) yield return u; } }
                foreach (var e in Under(d.InnerPath))
                {
                    bool direct = e.InnerPath.LastIndexOf('/') == d.InnerPath.Length;
                    if (e == desk) { Set(e, Category.Games, "The game's entry in the system image (its name, how it starts, its picture).", "game-entry"); continue; }
                    if (direct && e.Kind == EntryKind.File)
                    {
                        if (exec.Contains(e.Name)) { Set(e, Category.Games, "The game file its entry starts.", "game-file"); continue; }
                        var h = e.Size >= 4 ? Read(e, 4096)?.Take(4).ToArray() : null;
                        if (e.Size <= 4096 && h != null && h.Length == 4 && (h[0] == 'N' && h[1] == 'E' && h[2] == 'S' || h[0] == 'F' && h[1] == 'D' && h[2] == 'S') && h[3] == 0x1A) { Set(e, Category.Games, "An NES game file (it starts with the NES game file header).", "game-file"); continue; }
                        if (icon == e.Name) { Set(e, Category.Artwork, "The game's picture (its entry shows it).", "box-art"); continue; }
                        if (art.Contains(e.Name)) { Set(e, Category.Artwork, "One of the game's pictures (the system's own picture names).", "artwork"); continue; }
                    }
                    Set(e, Category.EverythingElse, "In this game's folder in the system image; its kind is not recognized.", e.Kind == EntryKind.Folder ? "folder" : "other");
                }
                d.Category = Category.Games; d.CategoryReason = "The game's folder in the system image."; d.GameKey = key; d.Role = "folder";
                g.Entries.Sort((a, c) => string.CompareOrdinal(a.InnerPath, c.InnerPath));
                g.BoxArt = g.Entries.FirstOrDefault(e => e.Role == "box-art") ?? g.Entries.FirstOrDefault(e => e.Category == Category.Artwork);
            }
        }

        // ------------------------------------------------------------------ the CCS HOME package
        /// <summary>CCS/Console-HOME.ccs: every file in it under it, with the checksum its own list records; the startup picture and HOME
        /// design that collection.json carries as files of their own</summary>
        void OpenPackage(BrowseEntry x, ZipReader z, List<BrowseEntry> all, Dictionary<string, GameGroup> games)
        {
            if (x.Unreadable != null) { x.ImageNote = "It is damaged, so nothing inside it is offered: its own list of its files is inside it and cannot be trusted either."; return; }
            if (!x.HashTrusted) { x.ImageNote = "No trusted checksum is known for it, so nothing inside it is offered."; return; }
            var ze = zip.TryGetValue(x.Member, out var zz) ? zz : null;
            if (ze == null) return;
            if (ze.Method != 0) { x.ImageNote = "It is stored compressed in the backup, so its files are not read in place; it can be exported as one file."; return; }
            SafeZip pz; Dictionary<string, object> man = null; string manWhy = null; byte[] manBytes = null;
            try { pz = SafeZip.Open(z.StoredWindow(ze), PackageLimits); }
            catch (Exception ex) when (ex is ComposeException || ex is IOException) { x.ImageNote = "Its files cannot be listed: " + ex.Message + " It can still be exported as one file, checked against the backup's checksum."; return; }
            var me = pz.Find("ccs-manifest.json");
            if (me == null) manWhy = "it has no list of its files (ccs-manifest.json)";
            else
            {
                try { manBytes = pz.ReadAll(me, MaxListJson); man = BigJson(Encoding.UTF8.GetString(manBytes)); }
                catch (Exception ex) when (ex is ComposeException || ex is FormatException || ex is ArgumentException || ex is InvalidOperationException) { manWhy = "its list of its files cannot be read (" + ex.Message.TrimEnd('.') + ")"; }
                if (man != null && (man.S("format") != "classic-cart-studio-package" || man.S("schema") != "ccs-package/2")) { manWhy = "its list of its files is not a Classic Cart Studio package's (ccs-package/2)"; man = null; }
            }
            var listed = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
            foreach (var r in (man?.A("files") ?? new List<object>()).OfType<Dictionary<string, object>>()) { var p = r.S("path"); if (p != null && !listed.ContainsKey(p)) listed[p] = r; }
            nested += pz.Entries.Count;
            if (nested > MaxNestedEntries) { x.ImageNote = "It holds more files than this version lists (" + pz.Entries.Count + "); it can be exported as one file."; return; }
            x.ImageType = "Classic Cart Studio collection package (.ccs)";
            x.ImageNote = "The console's CCS HOME as a .ccs package: its " + pz.Entries.Count + " files are listed under it, read in place from the backup's copy, which matches its checksum." + (manWhy != null ? " Its own list of files cannot be used (" + manWhy + "), so no file in it has a recorded checksum; each is worked out from the package when it is exported." : " Each file is checked against the checksum the package's own list recorded when the backup was made.");
            var folders = new HashSet<string>(StringComparer.Ordinal);
            string FolderId(string p) => "c:" + x.Id + "|" + p + "/";
            foreach (var pe in pz.Entries.OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                var segs = pe.Name.Split('/'); string at = "";
                for (int i = 0; i < segs.Length - 1; i++)
                {
                    var next = at.Length == 0 ? segs[i] : at + "/" + segs[i];
                    if (folders.Add(next)) Add(all, new BrowseEntry { Id = FolderId(next), ParentId = at.Length == 0 ? x.Id : FolderId(at), Name = segs[i], Source = EntrySource.PackageFile, Kind = EntryKind.Folder, TypeCode = 'd', Member = x.Member, Container = x, InnerPath = next, NestedPath = x.DisplayPath + "/" + next, Category = Category.EverythingElse, CategoryReason = "A folder inside " + Pkg + "." });
                    at = next;
                }
                var e = new BrowseEntry
                {
                    Id = "c:" + x.Id + "|" + pe.Name, ParentId = at.Length == 0 ? x.Id : FolderId(at), Name = segs[segs.Length - 1], Source = EntrySource.PackageFile, Kind = EntryKind.File, TypeCode = 'f',
                    Member = x.Member, Container = x, InnerPath = pe.Name, NestedPath = x.DisplayPath + "/" + pe.Name, Size = pe.Size, Mode = 0x1A4, Owner = "package",
                };
                if (pe.Name == "ccs-manifest.json" && manBytes != null) Bind(e, x, Sha.Of(manBytes), "Worked out on this PC from the package, which matches the backup's checksum (the package's own list cannot list itself).");
                else if (listed.TryGetValue(pe.Name, out var r) && Hex64Re.IsMatch(r.S("sha256") ?? "") && r.L("size", -1) == pe.Size)
                { e.Sha256 = r.S("sha256"); e.HashOrigin = HashOrigin.PackageList; e.HashText = "Recorded in the package's own list of its files (ccs-manifest.json) when the backup was made; the package itself matches the backup's checksum. Every export is checked against it."; }
                else { e.HashOrigin = HashOrigin.None; e.HashText = (man != null ? "The package's own list does not record it" : "The package's own list of its files cannot be used") + "; its checksum is worked out on this PC from the package (which matches the backup's checksum) when it is previewed or exported."; }
                PackageCategory(e, listed.TryGetValue(pe.Name, out var row) ? row : null, games);
                Add(all, e);
                if (pe.Name == "collection.json") Embedded(e, x, pz, pe, all);
            }
        }

        static Dictionary<string, object> BigJson(string text) => text.Length <= Json.MaxText ? Json.Parse(text)
            : new JavaScriptSerializer { MaxJsonLength = (int)MaxListJson * 2, RecursionLimit = 32 }.DeserializeObject(text) as Dictionary<string, object> ?? throw new FormatException("not a JSON object");

        /// <summary>a derived checksum: worked out on this PC through the verified holder, and bound to it</summary>
        static void Bind(BrowseEntry e, BrowseEntry holder, string sha, string text)
        {
            e.Sha256 = sha; e.HashOrigin = HashOrigin.Derived; e.BoundTo = holder.DisplayPath; e.BoundToSha256 = holder.Sha256; e.HashText = text;
        }

        /// <summary>a file of the package, by the package format's own paths (tools/companion/CCS-FORMAT.md); its game, by the identity its
        /// list records (a game file's content SHA-256; a save folder's name)</summary>
        static void PackageCategory(BrowseEntry e, Dictionary<string, object> row, Dictionary<string, GameGroup> games)
        {
            var n = e.InnerPath; var parts = n.Split('/');
            void Game(string sha, string title, string rel)
            {
                if (!Hex64Re.IsMatch(sha ?? "")) return;
                var key = "ccs:" + sha;
                if (!games.TryGetValue(key, out var g))
                    games[key] = g = new GameGroup { Key = key, ContentSha256 = sha, Title = title ?? "Unknown game " + sha.Substring(0, 16), TitleKnown = title != null, TitleSource = title != null ? "the package's own list (" + e.Container.DisplayPath + ")" : "no name is recorded for it", Kind = "Classic Cart Studio game (in " + e.Container.Name + ")" };
                e.GameKey = key; e.Role = "package-copy"; e.GameRelative = rel; g.Entries.Add(e);
            }
            if (n == "ccs-manifest.json") { e.Category = Category.EverythingElse; e.CategoryReason = "The package's own list of its files."; }
            else if (n == "collection.json") { e.Category = Category.Settings; e.CategoryReason = "The collection: its games in order, their exact versions and its HOME settings."; }
            else if (n == "studio.json") { e.Category = Category.Settings; e.CategoryReason = "A Full Studio Backup's lists and settings."; }
            else if (n == "README.txt") { e.Category = Category.EverythingElse; e.CategoryReason = "The package's description."; }
            else if (parts.Length == 2 && parts[0] == "roms")
            {
                e.Category = Category.Games; e.CategoryReason = "A game file the package carries (its exact bytes).";
                var t = row?.S("title")?.Trim(); Game(row?.S("content_sha256"), string.IsNullOrEmpty(t) ? null : t.Length > 200 ? t.Substring(0, 200) : t, parts[1]);
            }
            else if (parts.Length == 2 && parts[0] == "art") { e.Category = Category.Artwork; e.CategoryReason = "Artwork the collection carries."; }
            else if (parts.Length == 3 && parts[0] == "progress" && Hex64Re.IsMatch(parts[1]))
            {
                var f = parts[2]; Match mm;
                if (f == "cart.sav" || f.StartsWith("cart-set-aside-", StringComparison.Ordinal) && f.EndsWith(".sav", StringComparison.Ordinal)) { e.Category = Category.Saves; e.CategoryReason = "A battery save the package carries."; }
                else if ((mm = SlotRe.Match(f)).Success) { e.Category = Category.SuspendPoints; e.CategoryReason = "A suspend point (save slot " + mm.Groups[1].Value + ") the package carries."; e.SuspendSet = int.Parse(mm.Groups[1].Value); }
                else if (f == "continue.ccsstate") { e.Category = Category.SuspendPoints; e.CategoryReason = "An older version's automatic continuation the package carries."; e.SuspendSet = 0; }
                else if (f == "last-used.json" || f == "slot-names.json" || f == "game.json") { e.Category = Category.Settings; e.CategoryReason = "Details of the game's saves the package carries."; }
                else { e.Category = Category.EverythingElse; e.CategoryReason = "In a game's saves inside the package; its kind is not recognized."; }
                Game(parts[1], null, f);
            }
            else if (parts[0] == "custom") { var low = n.ToLowerInvariant(); e.Category = low.EndsWith(".png") || low.EndsWith(".jpg") || low.EndsWith(".jpeg") ? Category.Artwork : Category.EverythingElse; e.CategoryReason = "Custom artwork or text the package carries."; }
            else { e.Category = Category.EverythingElse; e.CategoryReason = "Not one of the files a collection package has (unexpected); it is listed as it is."; }
        }

        /// <summary>the startup picture and the HOME design collection.json carries (Base64 inside it), each a file of its own under it</summary>
        void Embedded(BrowseEntry coll, BrowseEntry x, SafeZip pz, ZipEntryInfo ce, List<BrowseEntry> all)
        {
            Dictionary<string, object> home;
            try { home = BigJson(Encoding.UTF8.GetString(pz.ReadAll(ce, MaxListJson))).O("ccs_collection")?.O("home"); }
            catch (Exception ex) when (ex is ComposeException || ex is FormatException || ex is ArgumentException || ex is InvalidOperationException) { return; }
            const string Carried = "Inside collection.json";
            void One(string kind, Dictionary<string, object> o, string name, Category cat, string why)
            {
                if (o == null) return;
                byte[] b; try { b = Convert.FromBase64String(o.S("data") ?? ""); } catch (FormatException) { b = null; }
                if (b == null || b.Length == 0) return;
                if (!byId.ContainsKey(coll.Id + "#"))
                    Add(all, new BrowseEntry { Id = coll.Id + "#", ParentId = x.Id, Name = Carried, Source = EntrySource.PackageFile, Kind = EntryKind.Folder, TypeCode = 'd', Member = x.Member, Container = x, InnerPath = Carried, NestedPath = x.DisplayPath + "/" + Carried, Category = Category.Settings, CategoryReason = "What collection.json carries inside it, each as a file of its own." });
                var e = new BrowseEntry
                {
                    Id = coll.Id + "#" + kind, ParentId = coll.Id + "#", Name = name, Source = EntrySource.PackageFile, Kind = EntryKind.File, TypeCode = 'f', Member = x.Member, Container = x, InnerPath = Carried + "/" + name, Embedded = kind,
                    NestedPath = x.DisplayPath + "/" + Carried + "/" + name, Size = b.Length, Mode = 0x1A4, Owner = "package", Category = cat, CategoryReason = why,
                };
                var sha = o.S("sha256");
                if (Hex64Re.IsMatch(sha ?? "")) { e.Sha256 = sha; e.HashOrigin = HashOrigin.PackageList; e.HashText = "Recorded in the collection (collection.json) when the backup was made; the package itself matches the backup's checksum. Every export is checked against it."; }
                else { e.HashOrigin = HashOrigin.None; e.HashText = "The collection does not record its checksum; it is worked out on this PC from the package when it is previewed or exported."; }
                Add(all, e);
            }
            var sc = home?.O("startup_custom");
            One("startup", sc, "startup picture.jpg", Category.Artwork, "The collection's own startup picture, carried inside collection.json.");
            var d = home?.O("design"); var dn = (d?.S("name") ?? "").Trim();
            One("design", d, (dn.Length == 0 ? "HOME design" : SafeNames.Component(dn, 60)) + ".ccshome", Category.Settings, "The collection's HOME design (.ccshome), carried inside collection.json.");
        }

        // ------------------------------------------------------------------ reading what is inside
        /// <summary>the exact bytes of a holder (a storage part, or a file on the data storage) as a seekable stream over the backup file</summary>
        Stream HolderStream(Stream f, BrowseEntry holder)
        {
            if (holder.Source == EntrySource.Member) { var z = Zip(f); var ze = z.Find(holder.Member) ?? throw new IOException("the part is not in the backup file"); return DecryptedView(holder, z.StoredWindow(ze)); }
            if (holder.Source == EntrySource.DataFile)
            {
                var dz = zip[DataMember]; var fs = new Ext4Reader(new SubStream(f, dz.DataOffset, dz.Size)); var st = fs.OpenFile(holder.Inode);
                if (st.Length != holder.Size) throw new IOException("the system image's size in the data storage differs from the size recorded for it");
                return st;
            }
            throw new IOException("it is not held in a part this version reads");
        }

        /// <summary>streams the bytes of an entry inside a system image or the package (CopyTo)</summary>
        void CopyNested(BrowseEntry e, Stream sink, Func<bool> cancelled)
        {
            try
            {
                using (var f = Read())
                {
                    if (e.Source == EntrySource.StartupPart || e.Source == EntrySource.RamdiskFile) { CopyStartup(e, f, sink, cancelled); return; }
                    if (e.Source == EntrySource.ImageFile)
                    {
                        var sq = new SquashFsReader(HolderStream(f, e.Container));
                        sq.ReadFile(e.InnerRef, sink, e.Size, cancelled);
                        return;
                    }
                    var pz = SafeZip.Open(HolderStream(f, e.Container), PackageLimits);
                    var pe = pz.Find(e.Embedded != null ? "collection.json" : e.InnerPath) ?? throw new IOException("it is not in the package");
                    if (e.Embedded == null) { using (var s = pz.OpenMember(pe)) Streams.Copy(s, sink, -1, cancelled); return; }
                    var home = BigJson(Encoding.UTF8.GetString(pz.ReadAll(pe, MaxListJson))).O("ccs_collection")?.O("home");
                    var o = e.Embedded == "startup" ? home?.O("startup_custom") : home?.O("design");
                    var b = Convert.FromBase64String(o?.S("data") ?? "");
                    if (b.Length != e.Size) throw new IOException("it reads back with a different size than when the backup was opened");
                    sink.Write(b, 0, b.Length);
                }
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is NotSupportedException || ex is Ext4Exception || ex is ComposeException || ex is FormatException || ex is ZipFormatException)
            { throw new IOException("it could not be read from " + (e.Source == EntrySource.ImageFile ? "the system image" : e.Source == EntrySource.PackageFile ? "the package" : "the startup image") + " (" + ex.Message.TrimEnd('.') + ")"); }
        }

        /// <summary>the checksum of an entry inside a system image or the package that its holder keeps none for: worked out now by reading it
        /// through the holder, only when the holder's own checksum is trusted (worked out first when it is not yet), and bound to it</summary>
        string NestedHash(BrowseEntry e, Func<bool> cancelled, Action<long> progress)
        {
            var holder = e.Container;
            if (holder == null || holder.Unreadable != null) return null;
            var hsha = holder.HashTrusted ? holder.Sha256 : ExpectedHash(holder, cancelled);
            if (hsha == null) return null;
            var h = new HashingSink(null, progress, cancelled);
            CopyTo(e, h, cancelled); h.Finish();
            if (h.Count != e.Size) throw new IOException("the file read back with a different size than its holder records");
            bool dec = systemKeys.TryGetValue(holder.Id, out var key);
            var what = e.Source == EntrySource.ImageFile ? (dec ? "the system part " + holder.DisplayPath : "the system image " + holder.DisplayPath)
                : e.Source == EntrySource.PackageFile ? "the package " + holder.DisplayPath : e.Source == EntrySource.StartupPart ? "the startup image " + holder.DisplayPath : "the ramdisk " + holder.DisplayPath;
            var from = holder.HashOrigin == HashOrigin.Inventory ? "recorded in the backup's file list" : holder.HashOrigin == HashOrigin.Manifest ? "recorded in the backup's own list"
                : holder.Source == EntrySource.StartupPart ? "worked out from its startup image, which matches the backup's checksum" : "worked out from the backup's verified data storage";
            var text = "Worked out on this PC from " + what + " (its SHA-256 " + hsha.Substring(0, 16) + "…, " + (dec ? "as it is stored, encrypted, " : "") + from + ")"
                + (dec ? ", decrypted on this PC with the key in " + key.from.Member + " (its SHA-256 " + key.from.Sha256.Substring(0, 16) + "…, which matches the backup's checksum)" : "") + ". It is not a checksum recorded when the backup was made.";
            lock (gate)
                foreach (var x in ((e.Source == EntrySource.ImageFile || e.Source == EntrySource.RamdiskFile) && byNestedInode.TryGetValue(holder.Id + "|" + e.Inode, out var same) && same.Contains(e) ? same : new List<BrowseEntry> { e }).Where(x => !x.HashTrusted && x.Unreadable == null))
                { x.Sha256 = h.Sha256; x.HashOrigin = HashOrigin.Derived; x.BoundTo = holder.DisplayPath; x.BoundToSha256 = hsha; x.HashText = text; if (dec) { x.KeyFrom = key.from.Member; x.KeyFromSha256 = key.from.Sha256; } }
            return h.Sha256;
        }
    }
}
