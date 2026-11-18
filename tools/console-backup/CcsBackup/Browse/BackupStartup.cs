// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The Backup Browser inside a backup's startup images and its encrypted system part (BackupContents, continued). Read-only, offline,
// in memory: nothing is extracted, decrypted or written to disk.
//   Startup images  restore/boot2-active.img (the startup software in use), restore/boot2-slot68.img (hakchi's spare slot, when the console
//                   has one) and recovery/original-kernel.img (the original Nintendo kernel CCS keeps), each an Android boot image
//                   (CcsConsole.RamBoot.BootParts): a container of the console's kernel and its ramdisk (and a second stage when it has
//                   one), not a bootloader (boot0 and boot1 are separate startup stages) and not itself a file system. Its parts are
//                   listed under it, and the ramdisk's files (its cpio archive, decoded from LZO, gzip or not compressed) under the
//                   ramdisk, each with the metadata the archive records. Another ramdisk compression is named and not decoded.
//   The system key  hakchi's own startup (its pinned /init) and the original Nintendo one open the encrypted system part with
//                   "cryptsetup open --type plain --cipher aes-xts-plain --key-file key-file", the key coming from a hakchi-key-file=
//                   word on the command line or a key-file in a stored ramdisk. Each startup image in the backup is looked in, the
//                   command line first. A key's bytes stay in this object's memory (systemKeys), are never shown or written to any list
//                   or record, and are cleared when the backup is closed. The key file itself is listed (it is a file of the ramdisk) as
//                   private: it is never previewed, and this separate entry is exported only when it is itself selected (alone or with
//                   other selected files); a folder, a type, a search or a game skips it. That does not take the key out of anything
//                   else: a whole startup image, its ramdisk and the raw NAND images in reference/ are exported exactly as the backup
//                   holds them (CanHoldKey), so they can still contain the key. No key is assumed to belong only to this console.
//   The system part the console's own system part (the layout's "rootfs" part, restore/nandb.img) as the console stores it. When a key
//                   from a startup image in the same backup decrypts it (Device/XtsView.cs) to a valid SquashFS superblock, it is browsed
//                   like any system image through that read-only decrypted view: every entry listed, previewed and exported with its
//                   checksum worked out on this PC and bound to the part's recorded SHA-256 and the key's startup image's (KeyFrom). A
//                   key that decrypts it to anything else is not used. Without a key that opens it (none in the backup, a damaged or
//                   unchecked startup image, an unsupported ramdisk compression, or a cipher or layout that does not decrypt to a file
//                   system) it stays one whole image with the exact reason, exportable exactly as it is stored. Browsing never makes
//                   anything a restore point.
// Trust: a startup image's parts and files are read only when the image itself matches the backup's checksum (in a salvage open: still
// matches a trusted checksum); a key is taken only from such an image, and the system part is decrypted only when it matches too.
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsConsole.RamBoot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace CcsBackup.Browse
{
    public sealed partial class BackupContents
    {
        /// <summary>the startup images a backup can hold, in the order the system key is looked for in them</summary>
        internal static readonly string[] StartupMembers = { "restore/boot2-active.img", "restore/boot2-slot68.img", BackupFormat.OriginalKernelName };
        public const string KeyPrivateText = "The console's system key: the key its startup software opens the encrypted system part with. Its bytes are never shown here. Exporting a folder, a type, a search or a game skips this separate key file; it is exported only when you select it itself. Whole startup images, ramdisks and raw NAND images can still contain the key. Keep any copy private.";

        /// <summary>a whole file that can contain the system key and is exported exactly as the backup holds it: a startup image (by its
        /// member name or as identified when the backup was opened), a startup image's ramdisk, or a raw NAND image in reference/ (the
        /// physical image holds the startup software; the logical one holds the start of the storage it is kept in). Exports never change
        /// such a file; the export's result and list say it can contain the key.</summary>
        public static bool CanHoldKey(BrowseEntry e) => e != null && e.Kind == EntryKind.File && (
            e.Source == EntrySource.Member && (StartupMembers.Contains(e.Member) || (e.ImageType ?? "").StartsWith("Startup software", StringComparison.Ordinal)
                                               || e.Member == "reference/nand-physical.bin" || e.Member == "reference/nand-logical-head.bin")
            || e.Source == EntrySource.StartupPart && e.Name.StartsWith("ramdisk", StringComparison.Ordinal));

        sealed class KeySource { public BrowseEntry Member; public byte[] Key; public string FoundIn, Why; }
        readonly List<KeySource> keySources = new List<KeySource>();
        /// <summary>the system part's id -> the key that opened it and the startup image it came from (memory only; cleared on Dispose)</summary>
        readonly Dictionary<string, (byte[] key, BrowseEntry from, string foundIn)> systemKeys = new Dictionary<string, (byte[], BrowseEntry, string)>(StringComparer.Ordinal);

        static byte[] ReadAll(Stream s, long n)
        {
            var b = new byte[n]; int got = 0;
            while (got < n) { int k = s.Read(b, got, (int)Math.Min(1 << 20, n - got)); if (k <= 0) throw new IOException("the part ends early"); got += k; }
            return b;
        }

        static string RamdiskExtension(string fmt) => fmt == "lzo" ? ".cpio.lzo" : fmt == "gzip" ? ".cpio.gz" : fmt == "cpio" ? ".cpio" : fmt == "xz" ? ".cpio.xz" : fmt == "lzma" ? ".cpio.lzma"
            : fmt == "lz4" ? ".cpio.lz4" : fmt == "bzip2" ? ".cpio.bz2" : fmt == "zstd" ? ".cpio.zst" : "";

        // ------------------------------------------------------------------ startup images
        /// <summary>one startup image (a member): its parts and its ramdisk's files under it, and where the system key is in it</summary>
        void OpenStartup(BrowseEntry x, ZipReader z, List<BrowseEntry> all)
        {
            var ks = new KeySource { Member = x }; keySources.Add(ks);
            string raw = x.HashTrusted ? " The whole image can be exported exactly as the backup holds it, checked against its checksum." : "";
            var ze = zip.TryGetValue(x.Member, out var zz) ? zz : null;
            if (!x.HashTrusted || ze == null) { ks.Why = "no trusted checksum is known for it, so nothing in it is used"; x.ImageNote = "No trusted checksum is known for it, so nothing inside it is offered."; return; }
            if (ze.Method != 0) { ks.Why = "it is stored compressed in the backup, so it is not read in place"; x.ImageNote = "An Android boot image stored compressed in the backup, so its parts are not read in place." + raw; return; }
            if (ze.Size > BackupFormat.MaxSize(x.Member)) { ks.Why = "it is larger than a startup image can be"; return; }
            byte[] img; using (var s = z.StoredWindow(ze)) img = ReadAll(s, ze.Size);
            var parts = BootParts.Parse(img.Length >= 2048 ? img.Take(4096).ToArray() : img, img.Length);
            if (!parts.Ok)
            {
                ks.Why = "its parts cannot be read (" + parts.Problem + ")";
                x.ImageType = "Startup software (an Android boot image; its parts are not read)";
                x.ImageNote = "An Android boot image whose parts this version does not read: " + parts.Problem + "." + raw; return;
            }
            BrowseEntry Part(string name, long at, long size, string why) => new BrowseEntry
            {
                Id = "b:" + x.Id + "|" + name, ParentId = x.Id, Name = name, Source = EntrySource.StartupPart, Kind = EntryKind.File, TypeCode = 'f', Member = x.Member, Container = x,
                InnerPath = "/" + name, InnerRef = at, NestedPath = x.DisplayPath + "/" + name, Size = size, Mode = 0x1A4, Owner = "startup image", Category = Category.SystemFiles, CategoryReason = why,
                HashOrigin = HashOrigin.None, HashText = "Not worked out yet: it is worked out on this PC from the startup image, which matches its checksum, when it is previewed or exported.",
                DateNote = "A startup image records no date for its parts.",
            };
            var stored = new byte[parts.RamdiskSize]; Buffer.BlockCopy(img, (int)parts.RamdiskAt, stored, 0, stored.Length);
            var fmt = parts.RamdiskSize == 0 ? "none" : BootParts.Format(stored); parts.RamdiskFormat = fmt;
            Add(all, Part("kernel", parts.KernelAt, parts.KernelSize, "The startup image's kernel: the program the console's startup runs."));
            BrowseEntry rpart = null;
            if (parts.RamdiskSize > 0)
            {
                rpart = Part("ramdisk" + RamdiskExtension(fmt), parts.RamdiskAt, parts.RamdiskSize, "The startup image's ramdisk: the small startup file system the kernel unpacks into memory (" + (fmt == null ? "its compression is not recognized" : fmt == "cpio" ? "not compressed" : BootParts.FormatName(fmt) + "-compressed") + ").");
                Add(all, rpart);
            }
            if (parts.SecondSize > 0) Add(all, Part("second-stage", parts.SecondAt, parts.SecondSize, "The startup image's second stage."));
            Ramdisk rd = null; string rwhy = null;
            if (rpart == null) rwhy = "it has no ramdisk";
            else
                try { rd = Ramdisk.Decode(stored); }
                catch (InvalidDataException ex) { rwhy = "its ramdisk is damaged (" + ex.Message + ")"; }
                catch (NotSupportedException ex) { rwhy = ex.Message; }
            var key = SystemKey.Find(parts, rd, rwhy);
            if (key.Found) { ks.Key = (byte[])key.Use().Clone(); ks.FoundIn = key.FoundIn; } else ks.Why = key.Why;
            key.Clear();
            int listed = 0;
            if (rd != null) { listed = RamdiskEntries(x, rpart, rd, all); Array.Clear(rd.Data, 0, rd.Data.Length); rd.Data = null; }
            else if (rpart != null) { rpart.IsImage = true; rpart.ImageType = "Ramdisk (" + (fmt == null ? "not recognized" : BootParts.FormatName(fmt)) + ")"; rpart.ImageNote = "Its files are not listed: " + rwhy + ". It can be exported as one exact file."; }
            if (rd != null) { rpart.IsImage = true; rpart.ImageType = "Ramdisk (" + BootParts.FormatName(fmt) + " cpio archive)"; rpart.ImageNote = "The startup file system the kernel unpacks into memory: its " + listed + " entries are listed under it, decoded on this PC from the startup image, which matches its checksum. A file's checksum is worked out from it when the file is previewed or exported." + (ks.FoundIn == "the ramdisk's key-file" ? " It holds the system key file; an exported copy of the whole ramdisk carries the key, so keep such a copy private." : ""); }
            x.ImageType = "Startup software (an Android boot image: kernel and ramdisk" + (rd != null ? " with " + listed + " entries" : "") + ")";
            x.ImageNote = "An Android boot image: a container of the console's kernel and its ramdisk (the small startup file system the kernel unpacks into memory). It is the console's startup software, not a bootloader (the console's first startup stages are separate parts) and not itself a file system. Its parts are listed under it"
                + (rd != null ? " and its ramdisk's files under the ramdisk" : " (" + rwhy + ", so its files are not listed)") + ", read on this PC from the backup's copy, which matches its checksum. Its page size is " + parts.Page + " bytes; its command line: " + (parts.MaskedCmdline.Length == 0 ? "(none)" : parts.MaskedCmdline) + "."
                + (ks.Key != null ? " It carries the system key (" + ks.FoundIn + "), which is never shown; an exported copy of this image" + (ks.FoundIn == "the ramdisk's key-file" ? " or of its ramdisk" : "") + " carries the key too, so keep such copies private." : "") + raw;
            Array.Clear(stored, 0, stored.Length); Array.Clear(img, 0, img.Length);   // the image's bytes (the key among them) are read again when a part is
        }

        /// <summary>the ramdisk's files under its part, with the archive's metadata; folders the archive only implies are added as folders</summary>
        int RamdiskEntries(BrowseEntry x, BrowseEntry rpart, Ramdisk rd, List<BrowseEntry> all)
        {
            string Id(string p) => "r:" + rpart.Id + "|" + p;
            var have = new HashSet<string>(rd.Entries.Select(c => c.Path), StringComparer.Ordinal); int n = 0;
            void Implied(string p)
            {
                if (p.Length == 0 || have.Contains(p)) return;
                have.Add(p); int cut = p.LastIndexOf('/'); Implied(p.Substring(0, cut));
                Add(all, new BrowseEntry { Id = Id(p), ParentId = cut == 0 ? rpart.Id : Id(p.Substring(0, cut)), Name = p.Substring(cut + 1), Source = EntrySource.RamdiskFile, Kind = EntryKind.Folder, TypeCode = 'd', Member = x.Member, Container = rpart, InnerPath = p, NestedPath = rpart.DisplayPath + p, Owner = "ramdisk", Category = Category.SystemFiles, CategoryReason = "A folder the ramdisk's archive implies (it lists files inside it, not the folder itself)." });
                n++;
            }
            foreach (var c in rd.Entries)
            {
                int cut = c.Path.LastIndexOf('/'); var parent = c.Path.Substring(0, cut); Implied(parent);
                var e = new BrowseEntry
                {
                    Id = Id(c.Path), ParentId = parent.Length == 0 ? rpart.Id : Id(parent), Name = c.Name, Source = EntrySource.RamdiskFile, Member = x.Member, Container = rpart, InnerPath = c.Path, NestedPath = rpart.DisplayPath + c.Path,
                    TypeCode = c.TypeCode, Mode = c.Mode & 0xFFF, Uid = c.Uid, Gid = c.Gid, Links = c.Links, Inode = c.Ino, LinkTarget = c.LinkTarget, Owner = "ramdisk",
                    Kind = c.TypeCode == 'd' ? EntryKind.Folder : c.TypeCode == 'f' ? EntryKind.File : c.TypeCode == 'l' ? EntryKind.Link : EntryKind.Special,
                    MtimeSeconds = c.Mtime, DateSource = "as recorded in the ramdisk", Category = Category.SystemFiles, CategoryReason = "In the ramdisk of the startup image " + x.Name + ".",
                };
                e.Size = e.Kind == EntryKind.File || e.Kind == EntryKind.Link ? c.Size : 0;
                if (c.Mtime == 0) e.DateNote = "No date was recorded in the ramdisk.";
                if (c.Problem != null) e.Unreadable = c.Problem;
                else if (c.TypeCode == '?') e.Unreadable = "An entry of a kind this version does not know; it is listed, not read.";
                if (e.Kind == EntryKind.File)
                {
                    if (e.Unreadable != null) { e.HashOrigin = HashOrigin.Unreadable; e.HashText = e.Unreadable; }
                    else { e.HashOrigin = HashOrigin.None; e.HashText = "Not worked out yet: it is worked out on this PC from the ramdisk, which is read from the startup image that matches its checksum, when it is previewed or exported."; }
                    if (c.Path == "/key-file") { e.Private = KeyPrivateText; e.CategoryReason = "The console's system key file, in the ramdisk of the startup image " + x.Name + "."; }
                    if (c.Links > 1) { var k = rpart.Id + "|" + c.Ino; if (!byNestedInode.TryGetValue(k, out var l)) byNestedInode[k] = l = new List<BrowseEntry>(); l.Add(e); }
                }
                Add(all, e); n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ the encrypted system part
        /// <summary>the system part, when it is not a file system as it is stored: decrypted on this PC with a key from a startup image in this
        /// backup, read only, when what that key decrypts it to is a valid SquashFS superblock; otherwise one whole image with the reason</summary>
        void OpenEncryptedSystem(BrowseEntry e, ZipReader z, List<BrowseEntry> all, Dictionary<string, GameGroup> games, Func<bool> cancelled)
        {
            string raw = e.Unreadable == null && e.HashTrusted ? " It can be exported as one exact image, checked against the backup's checksum." : "";
            var ze = zip.TryGetValue(e.Member, out var zz) ? zz : null;
            e.ImageType = "System part (not a file system as it is stored)";
            if (!e.HashTrusted || ze == null) { e.ImageNote = "The console's system part, exactly as the console stores it. No trusted checksum is known for it, so it is not decrypted or read."; return; }
            if (ze.Method != 0) { e.ImageNote = "The console's system part, stored compressed in the backup, so it is not read in place." + raw; return; }
            if (e.Size % XtsView.Sector != 0) { e.ImageNote = "The console's system part, exactly as the console stores it. It is not a whole number of 512-byte sectors, so it is not decrypted here." + raw; return; }
            var tried = new List<string>(); var seen = new List<byte[]>();
            foreach (var k in keySources)
            {
                if (k.Key == null) { tried.Add(k.Member.Member + ": " + k.Why); continue; }
                var same = seen.FindIndex(o => o.SequenceEqual(k.Key));
                if (same >= 0) { tried.Add(k.Member.Member + ": the same key as above"); continue; }
                seen.Add(k.Key);
                string why;
                try { using (var v = new XtsView(z.StoredWindow(ze), k.Key)) { new SquashFsReader(v); why = null; } }
                catch (InvalidDataException ex) { why = ex.Message == "not a SquashFS file system" ? "decrypted with its key (" + k.FoundIn + "), it is not a SquashFS file system" : "decrypted with its key (" + k.FoundIn + "), it starts as a SquashFS file system but is " + ex.Message; }
                catch (IOException ex) { why = "it could not be read (" + ex.Message + ")"; }
                if (why != null) { tried.Add(k.Member.Member + ": " + why); continue; }
                systemKeys[e.Id] = ((byte[])k.Key.Clone(), k.Member, k.FoundIn);
                e.KeyFrom = k.Member.Member; e.KeyFromSha256 = k.Member.Sha256;
                OpenSystemImage(e, () => new XtsView(z.StoredWindow(ze), systemKeys[e.Id].key), all, games, cancelled, true);
                return;
            }
            foreach (var m in StartupMembers.Where(n => !keySources.Any(k => k.Member.Member == n)))
                tried.Add(m + ": " + (byId.TryGetValue("m:" + m, out var me) && me.Unreadable != null ? char.ToLowerInvariant(me.Unreadable[0]) + me.Unreadable.Substring(1).TrimEnd('.') + ", so its key is not used" : "not in this backup"));
            bool anyKey = keySources.Any(k => k.Key != null);
            e.ImageNote = "The console's system part, exactly as the console stores it (what a restore puts back). It is not a file system as it is stored; the NES Classic's own system part is encrypted with a key its startup software carries. "
                + (anyKey ? "No key in this backup decrypts it to a file system, so its files are not listed: " : "No key that could open it was found in this backup's startup software, so its files are not listed: ")
                + string.Join("; ", tried) + "." + raw;
        }

        /// <summary>the decrypted view of a system part this backup opened with a key (null: it is read as it is stored)</summary>
        Stream DecryptedView(BrowseEntry holder, Stream stored) => systemKeys.TryGetValue(holder.Id, out var k) ? new XtsView(stored, k.key) : stored;

        /// <summary>the bytes of a startup image's part, or of a file in its ramdisk</summary>
        void CopyStartup(BrowseEntry e, Stream f, Stream sink, Func<bool> cancelled)
        {
            var part = e.Source == EntrySource.StartupPart ? e : e.Container;
            var member = part.Container; var z = Zip(f); var ze = z.Find(member.Member) ?? throw new IOException("the startup image is not in the backup file");
            using (var s = z.StoredWindow(ze))
            {
                if (part.InnerRef < 0 || part.InnerRef + part.Size > s.Length) throw new IOException("the part lies outside the startup image");
                s.Position = part.InnerRef;
                if (e.Source == EntrySource.StartupPart) { Streams.Copy(new SubStream(s, part.InnerRef, part.Size), sink, part.Size, cancelled); return; }
                var stored = ReadAll(s, part.Size); Ramdisk rd;
                try { rd = Ramdisk.Decode(stored); }
                catch (Exception ex) when (ex is InvalidDataException || ex is NotSupportedException) { throw new IOException("the ramdisk could not be decoded (" + ex.Message + ")"); }
                try
                {
                    var c = rd.Entries.FirstOrDefault(x => x.Path == e.InnerPath) ?? throw new IOException("it is not in the ramdisk");
                    if (c.Size != e.Size) throw new IOException("it reads back with a different size than when the backup was opened");
                    rd.CopyFile(c, sink);
                }
                finally { Array.Clear(rd.Data, 0, rd.Data.Length); rd.Data = null; }
            }
        }

        void ClearKeys()
        {
            foreach (var k in keySources) if (k.Key != null) Array.Clear(k.Key, 0, k.Key.Length);
            foreach (var k in systemKeys.Values) Array.Clear(k.key, 0, k.key.Length);
            keySources.Clear(); systemKeys.Clear();
        }
    }
}
