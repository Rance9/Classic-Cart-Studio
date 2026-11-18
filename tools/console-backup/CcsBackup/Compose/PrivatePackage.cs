// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The private built restore package (schema ccs-private-restore/1; COMPOSER-FORMATS.md): one self-contained standard ZIP that Console
// Composer builds for ONE destination console. It holds the owner's own files (payload/<role>/<sha256><ext>, each stored), a README
// that says it is private, and ccs-private-restore.json (last) binding every payload member by role, size, SHA-256 and provenance (which
// backup and member, or which file or public build it came from), the destination console, the exact target plan and the shared plan it
// was built from, if any. It is labeled PRIVATE in its manifest, its README and its file name; ShareGuard refuses it on every share path,
// and no shared plan or design can carry it or its payload. It is not a console backup and never replaces one.
//
// Roles are decided by content, never by a name or an extension (PrivateRoles.Check): a SquashFS root by its superblock, an encrypted
// system partition by being whole partition-sized ciphertext that is neither SquashFS nor ext4, an ext4 file system by its superblock and
// root folder (Ext4Reader), a kernel by its Android boot image header and declared size, boot0/boot1 by their eGON/uboot headers and
// checksums (BootImage.Check), an NES game by its iNES/NES 2.0 header and exact size, a battery save by its size (and not being a game,
// an archive or a save state), a mod by its gzip + tar (ustar) structure, file system metadata by its own JSON schema, a nested .ccs by
// being a Classic Cart Studio collection package. Ordinary composed restores never write boot0, boot1, the storage table or the
// console's identity: boot stages here are evidence ("reference") or the destination console's own repair material, which must come
// from a backup of that same console ("same-console-repair", routed through Rescue, never through an ordinary composed write).
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsBackup.Ext4;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsBackup.Compose
{
    public static class PrivateRoles
    {
        public sealed class Role { public string Id, Dir, Ext, Text, Place; public long Max; }
        static Role R(string id, string dir, string ext, long max, string text, string place) => new Role { Id = id, Dir = dir, Ext = ext, Max = max, Text = text, Place = place };
        const string Env = @"environment:[a-z][a-z0-9-]{0,15}/";
        public static readonly Dictionary<string, Role> All = new[]
        {
            R("root-squashfs", "system", ".hsqs", 536870912, "a system root file system (SquashFS)", "^" + Env + "root$"),
            R("encrypted-partition", "partitions", ".img", 536870912, "the encrypted system partition", "^partition:system$"),
            R("filesystem-image", "filesystems", ".img", 536870912, "a data file system image (ext4)", "^partition:(data|private|udisk)$"),
            R("kernel", "kernels", ".img", 32L * 1024 * 1024, "a kernel boot image", "^(kernel:(active|spare)|" + Env + "kernel)$"),
            R("boot0", "boot", ".boot0", BootImage.Boot0Max, "the first boot stage (boot0)", "^(reference|same-console-repair)$"),
            R("boot1", "boot", ".boot1", BootImage.Boot1Max, "the second boot stage (boot1, U-Boot)", "^(reference|same-console-repair)$"),
            R("game-nes", "games", ".nes", 64000000, "an NES game file", "^" + Env + "games$"),
            R("save-battery", "saves", ".sav", 131072, "a battery save", "^" + Env + "saves$"),
            R("mod-overlay", "mods", ".hmod", 512000000, "a mod package", "^" + Env + "mods$"),
            R("fs-metadata", "metadata", ".json", 16000000, "file system metadata", "^" + Env + "metadata$"),
            R("ccs-collection", "collections", ".ccs", 2000000000, "a Classic Cart Studio collection package", "^" + Env + "collection$"),
            // Console Composer's own files: the exact path on the console's data storage it goes to (only what Composer may write)
            R("data-file", "files", ".bin", Engine.FilesEngine.MaxFileBytes, "a file for the console's data storage", "^data:(clover/profiles|hakchi/games|ccs)/[^\\:*?\"<>|\u0000-\u001f]{1,240}$"),
        }.ToDictionary(r => r.Id);

        public static string MemberName(string role, string sha) => "payload/" + All[role].Dir + "/" + sha + All[role].Ext;

        static uint U32(byte[] b, int o) => (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24);
        static ushort U16(byte[] b, int o) => (ushort)(b[o] | b[o + 1] << 8);
        static ulong U64(byte[] b, int o) => U32(b, o) | (ulong)U32(b, o + 4) << 32;
        static byte[] Head(Stream s, long at, int n)
        {
            if (at < 0 || at + n > s.Length) return null;
            var b = new byte[n]; s.Position = at; int got = 0;
            while (got < n) { int r = s.Read(b, got, n - got); if (r <= 0) return null; got += r; }
            return b;
        }

        /// <summary>null when the content really is that role; otherwise why not, in plain words</summary>
        public static string Check(string role, Stream s)
        {
            if (!All.TryGetValue(role ?? "", out var r)) return "is of an unknown kind (" + role + ")";
            if (s.Length > r.Max) return "is larger than " + r.Text + " can be (" + Units.Size(r.Max) + ")";
            if (s.Length == 0) return "is empty";
            try
            {
                switch (role)
                {
                    case "root-squashfs": return SquashFs(s);
                    case "encrypted-partition": return Encrypted(s);
                    case "filesystem-image": return Ext4Fs(s);
                    case "kernel": return Kernel(s);
                    case "boot0": case "boot1": { var b = Head(s, 0, (int)s.Length); var why = BootImage.Check(b, role == "boot0"); return why == null ? null : "is not a valid " + (role == "boot0" ? "boot0" : "boot1") + " image: " + why; }
                    case "game-nes": return Nes(s);
                    case "save-battery": return Save(s);
                    case "mod-overlay": return Hmod(s);
                    case "fs-metadata": return FsMeta(s);
                    case "ccs-collection": return Ccs(s);
                    case "data-file": return null;   // any content; its place is checked (a game file target is checked as one when applied)
                }
            }
            catch (IOException ex) { return "could not be read (" + ex.Message + ")"; }
            catch (InvalidDataException) { return "is damaged (it cannot be decompressed)"; }
            return "is of an unknown kind";
        }

        static string SquashFs(Stream s)
        {
            var b = Head(s, 0, 96); if (b == null) return "is too short to be a SquashFS file system";
            if (Encoding.ASCII.GetString(b, 0, 4) != "hsqs") return "is not a SquashFS file system (no hsqs superblock)";
            if (U16(b, 28) != 4 || U16(b, 30) != 0) return "is a SquashFS version this program does not support (" + U16(b, 28) + "." + U16(b, 30) + ")";
            uint bs = U32(b, 12); int log = U16(b, 22);
            if (log < 12 || log > 20 || bs != 1u << log) return "has a damaged SquashFS superblock (block size)";
            if (U16(b, 20) < 1 || U16(b, 20) > 6) return "has a damaged SquashFS superblock (compression)";
            if (U32(b, 4) == 0 || U16(b, 26) == 0) return "has a damaged SquashFS superblock (no files)";
            ulong used = U64(b, 40), none = ulong.MaxValue;
            if (used < 96 || used > (ulong)s.Length) return "is shorter than its SquashFS superblock says (" + used + " bytes used)";
            ulong ino = U64(b, 64), dir = U64(b, 72), ids = U64(b, 48);
            if (ino < 96 || dir <= ino || dir >= used || ids >= used || ids <= dir) return "has a damaged SquashFS superblock (table positions)";
            foreach (var t in new[] { U64(b, 56), U64(b, 80), U64(b, 88) }) if (t != none && t >= used) return "has a damaged SquashFS superblock (table positions)";
            return null;
        }

        /// <summary>plain dm-crypt has no header: what can be checked without the console's key is that the image is partition-sized,
        /// is not a readable file system, and that its first 65,536 bytes are ciphertext-like (byte frequencies close to uniform)</summary>
        static string Encrypted(Stream s)
        {
            if (s.Length < 1024 * 1024 || s.Length % 4096 != 0) return "is not the size of a storage partition";
            var b = Head(s, 0, 65536);
            if (Encoding.ASCII.GetString(b, 0, 4) == "hsqs") return "is not encrypted (it is a readable SquashFS file system)";
            if (U16(b, 1080) == 0xEF53) return "is not encrypted (it is an ext4 file system)";
            var c = new int[256]; foreach (var x in b) c[x]++;
            double chi = c.Sum(k => (k - 256.0) * (k - 256.0) / 256.0);
            return chi > 400 ? "does not look encrypted (its first 65,536 bytes are not ciphertext-like)" : null;
        }

        static string Ext4Fs(Stream s)
        {
            try
            {
                var r = new Ext4Reader(s); var root = r.Inode(2);
                return (U16(root, 0) & 0xF000) != 0x4000 ? "is an ext4 file system without a root folder" : null;
            }
            catch (Ext4Exception ex) { return "is not a usable ext4 file system: " + ex.Message; }
        }

        static string Kernel(Stream s)
        {
            var b = Head(s, 0, 2048); if (b == null) return "is too short to be a kernel boot image";
            if (Encoding.ASCII.GetString(b, 0, 8) != "ANDROID!") return "is not a kernel boot image (no ANDROID! header)";
            uint page = U32(b, 36);
            if (U32(b, 8) == 0) return "is a boot image without a kernel";
            if (page < 2048 || page > 16384 || (page & (page - 1)) != 0) return "has a damaged boot image header (page size)";
            long size = Verifier.BootImageSize(b);
            if (size < 0 || size > s.Length) return "is shorter than its boot image header says";
            return null;
        }

        static string Nes(Stream s)
        {
            var h = Head(s, 0, 16); if (h == null) return "is too short to be an NES game file";
            if (!(h[0] == 'N' && h[1] == 'E' && h[2] == 'S' && h[3] == 0x1A)) return "is not an NES game file with a header (iNES)";
            bool nes2 = (h[7] & 0x0C) == 0x08;
            long Size(int lsb, int msb, long unit) => !nes2 ? lsb * unit : msb == 0x0F ? (1L << (lsb >> 2)) * ((lsb & 3) * 2 + 1) : ((long)msb << 8 | (uint)lsb) * unit;
            long prg = Size(h[4], h[9] & 0x0F, 16384), chr = Size(h[5], h[9] >> 4, 8192), trainer = (h[6] & 4) != 0 ? 512 : 0, expect = 16 + trainer + prg + chr;
            if (prg == 0) return "declares no program data";
            bool pc10 = (h[7] & 2) != 0 && (s.Length == expect + 8192 || s.Length == expect + 8192 + 16 || s.Length == expect + 8192 + 32);
            return s.Length == expect || pc10 ? null : "is not the size its header declares (" + expect.ToString("N0", CultureInfo.InvariantCulture) + " bytes declared, " + s.Length.ToString("N0", CultureInfo.InvariantCulture) + " found)";
        }

        static string Save(Stream s)
        {
            long n = s.Length;
            if (n < 1024 || n > 131072 || (n & (n - 1)) != 0) return "is " + n.ToString("N0", CultureInfo.InvariantCulture) + " bytes; a battery save is a power of two from 1,024 to 131,072 bytes";
            var h = Head(s, 0, 4);
            if (h[0] == 'N' && h[1] == 'E' && h[2] == 'S' && h[3] == 0x1A) return "is a game file, not a save";
            if (Encoding.ASCII.GetString(h) == "FCSX") return "is a suspend point (a save state), not a battery save";
            if (h[0] == 0x1F && h[1] == 0x8B) return "is compressed data (a save state or an archive), not a battery save";
            if (h[0] == 'P' && h[1] == 'K' && h[2] == 3 && h[3] == 4) return "is an archive, not a battery save";
            return null;
        }

        static string Hmod(Stream s)
        {
            var h = Head(s, 0, 3); byte[] tar;
            if (h[0] == 0x1F && h[1] == 0x8B && h[2] == 8)
            {
                s.Position = 0; tar = new byte[512]; int got = 0;
                using (var gz = new GZipStream(new SubStream(s, 0, s.Length), CompressionMode.Decompress))
                    while (got < 512) { int r = gz.Read(tar, got, 512 - got); if (r <= 0) return "is a gzip file too short to hold a mod"; got += r; }
            }
            else tar = Head(s, 0, 512);
            if (tar == null || Encoding.ASCII.GetString(tar, 257, 5) != "ustar") return "is not a mod package (a tar or tar.gz archive)";
            if (tar[0] == 0) return "is a mod package without files";
            long sum = 0; for (int i = 0; i < 512; i++) sum += i >= 148 && i < 156 ? 32 : tar[i];
            var oct = Encoding.ASCII.GetString(tar, 148, 8).Trim('\0', ' ');
            long want; try { want = Convert.ToInt64(oct, 8); } catch (Exception) { return "is a mod package with a damaged first entry"; }
            return want == sum ? null : "is a mod package with a damaged first entry (checksum)";
        }

        static string FsMeta(Stream s)
        {
            var b = Head(s, 0, (int)s.Length);
            JObj d;
            try { d = StrictJson.ParseObject(b, new JsonLimits { MaxBytes = 16000000, MaxDepth = 8, MaxValues = 2000000, MaxString = 4096 }, "The metadata"); }
            catch (ComposeException ex) { return "is not file system metadata: " + ex.Message; }
            if (d["schema"] as string != "ccs-fs-metadata/1") return "is not file system metadata (schema ccs-fs-metadata/1)";
            var es = Doc.Arr(d["entries"]); if (es == null || es.Count > 200000) return "has no entry list, or more than 200,000 entries";
            foreach (var x in es)
            {
                var e = x as JObj; if (e == null) return "has an entry that is not an object";
                var p = e["path"] as string; if (p == null || SafeZip.NameProblem(p).code != null) return "has an entry with a path that is not a safe relative path";
                var t = e["type"] as string; if (t != "f" && t != "d" && t != "l") return "has an entry of an unknown type";
                foreach (var (k, max) in new[] { ("mode", 4095L), ("uid", 65535L), ("gid", 65535L), ("size", long.MaxValue) }) if (!(e[k] is long v) || v < 0 || v > max) return "has an entry with a damaged " + k;
                if (e.Has("sha256") && !Sha.Is(e["sha256"] as string)) return "has an entry with a damaged fingerprint";
                if (t == "l" && !(e["target"] is string)) return "has a link without a target";
            }
            return null;
        }

        static string Ccs(Stream s)
        {
            var lim = new ZipLimits { What = "collection package", MaxMembers = 50000, MaxArchiveBytes = 2000000000, MaxTotalBytes = 8000000000, MaxRatio = 400, MaxMemberBytes = n => 2000000000 };
            try
            {
                using (var z = SafeZip.Open(new SubStream(s, 0, s.Length), lim))
                {
                    var m = z.Find("ccs-manifest.json"); if (m == null) return "is not a Classic Cart Studio collection package (no ccs-manifest.json)";
                    var d = StrictJson.ParseObject(z.ReadAll(m, 64000000), new JsonLimits { MaxBytes = 64000000, MaxDepth = 16, MaxValues = 4000000, MaxString = 1000000 }, "The collection package's manifest");
                    if (d["format"] as string != "classic-cart-studio-package" || d["schema"] as string != "ccs-package/2" || !(d["kind"] as string == "collection" || d["kind"] as string == "studio") || Doc.Arr(d["files"]) == null)
                        return "is not a Classic Cart Studio collection package (ccs-package/2)";
                    return null;
                }
            }
            catch (ComposeException ex) { return "is not a usable collection package: " + ex.Message; }
        }
    }

    public sealed class PrivateMemberSource
    {
        public string Role, Place, Path; public byte[] Bytes; public JObj Provenance;
        internal Stream Open() => Bytes != null ? (Stream)new MemoryStream(Bytes, false) : new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
    }

    public sealed class PrivatePackageSpec
    {
        public string DestinationHwid, InstallPolicy, StartingPoint, ToolName = "Classic Cart Studio", ToolVersion = "";
        public List<(string id, string kind)> Environments = new List<(string, string)>();
        public JObj CompositionRef;   // {id, revision, sha256} of the shared plan it was built from, or null
        public List<PrivateMemberSource> Members = new List<PrivateMemberSource>();
    }

    public sealed class PrivatePackageInfo
    {
        public sealed class Member { public string Name, Role, Sha256; public long Size; public JObj Provenance; public List<string> Places = new List<string>(); }
        public JObj Manifest; public string PackageId, DestinationHwid, DestinationTag, Label; public List<Member> Members = new List<Member>();
    }

    public static class PrivatePackage
    {
        public const string Format = "classic-cart-studio-private-restore", Schema = "ccs-private-restore/1", ManifestName = "ccs-private-restore.json", ReadmeName = "README-PRIVATE.txt";
        public const string Label = "PRIVATE: this console's own files and identity. Never share this file.", TypeName = "Private Restore Package";
        public const long MaxArchiveBytes = 4000000000L, MaxManifestBytes = 16000000;
        static readonly Regex Payload = new Regex(@"^payload/(system|partitions|filesystems|kernels|boot|games|saves|mods|metadata|collections|files)/[0-9a-f]{64}\.(hsqs|img|boot0|boot1|nes|sav|hmod|json|ccs|bin)$", RegexOptions.CultureInvariant);
        static readonly Regex Hwid = new Regex("^[0-9a-f]{8}( [0-9a-f]{8}){3}$", RegexOptions.CultureInvariant);

        public static string FileStem(DateTime local, string consoleTag) => "PRIVATE NES Classic Restore Package " + local.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture) + " (console " + consoleTag + ")";

        public const string Readme =
            "PRIVATE - Classic Cart Studio private restore package\r\n\r\n" +
            "This file holds your own console's files (system software, games, saves, settings) and names the one console it was built for.\r\n" +
            "Keep it private. Do not post it, send it or share it: it contains your console's identity and your own files.\r\n" +
            "Classic Cart Studio never shares it: Share Composition and HOME design sharing refuse this file.\r\n\r\n" +
            "Open it with Console Composer in CCS Console Workbench to check it or to apply it to the console it was built for. It is not a\r\n" +
            "console backup and does not replace one; keep your console backups.\r\n";

        static ComposeException Refuse(string code, string why) => new ComposeException(code, "This private restore package was refused: " + why + ".");

        static ZipLimits Limits => new ZipLimits
        {
            What = "private restore package", MaxMembers = 4096, MaxArchiveBytes = MaxArchiveBytes, MaxTotalBytes = MaxArchiveBytes, MaxRatio = 100,
            Allowed = n => n == ManifestName || n == ReadmeName || Payload.IsMatch(n),
            MaxMemberBytes = n => n == ManifestName ? MaxManifestBytes : n == ReadmeName ? 64000 : PrivateRoles.All.Values.Where(r => n.StartsWith("payload/" + r.Dir + "/") && n.EndsWith(r.Ext)).Select(r => r.Max).DefaultIfEmpty(0).Max(),
            MustBeStored = n => n.StartsWith("payload/"),
        };

        /// <summary>builds the package: every source is checked for its role and hashed first, written once (the hash written must equal
        /// the hash checked: a file that changes meanwhile stops the build), the finished file is opened and verified in full, and only
        /// then moved into place. Nothing is ever overwritten.</summary>
        public static PrivatePackageInfo Write(string outPath, PrivatePackageSpec spec)
        {
            var ext = Path.GetExtension(outPath).ToLowerInvariant();
            if (ext == DesignProfile.Extension || ext == Composition.Extension) throw Refuse("private.extension", "a private package is never saved under a shared file type (" + ext + ")");
            if (File.Exists(outPath)) throw Refuse("private.exists", "a file with that name already exists");
            if (!Hwid.IsMatch(spec.DestinationHwid ?? "")) throw Refuse("private.destination", "the destination console's identity is missing");
            var checkedMembers = new List<(PrivateMemberSource src, string sha, long size)>();
            foreach (var m in spec.Members)
            {
                if (!PrivateRoles.All.ContainsKey(m.Role ?? "")) throw Refuse("private.role", "a member has an unknown role (" + m.Role + ")");
                using (var s = m.Open())
                {
                    var why = PrivateRoles.Check(m.Role, s);
                    if (why != null) throw Refuse("private.role", (m.Path != null ? "\"" + System.IO.Path.GetFileName(m.Path) + "\"" : "a member") + " was given as " + PrivateRoles.All[m.Role].Text + " but " + why);
                    s.Position = 0; checkedMembers.Add((m, Sha.Of(s), s.Length));
                }
            }
            var manifest = new JObj
            {
                { "format", Format }, { "schema", Schema }, { "private", true }, { "label", Label }, { "package_id", NewId.Make() },
                { "created_utc", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) }, { "tool", new JObj { { "name", spec.ToolName }, { "version", spec.ToolVersion } } },
                { "destination", new JObj { { "hwid", spec.DestinationHwid }, { "tag", ConsoleIdentity.HwidTag(spec.DestinationHwid) } } },
            };
            if (spec.CompositionRef != null) manifest["composition"] = spec.CompositionRef;
            var members = new List<object>(); var components = new List<object>(); var written = new Dictionary<string, string>();
            foreach (var (src, sha, size) in checkedMembers)
            {
                var name = PrivateRoles.MemberName(src.Role, sha);
                components.Add(new JObj { { "role", src.Role }, { "sha256", sha }, { "place", src.Place } });
                if (written.ContainsKey(name)) continue;
                written[name] = sha;
                members.Add(new JObj { { "name", name }, { "role", src.Role }, { "size", size }, { "sha256", sha }, { "provenance", src.Provenance ?? new JObj { { "kind", "user-file" } } } });
            }
            manifest["target_plan"] = new JObj
            {
                { "install_policy", spec.InstallPolicy }, { "starting_point", spec.StartingPoint },
                { "environments", spec.Environments.Select(e => (object)new JObj { { "id", e.id }, { "kind", e.kind } }).ToList() }, { "components", components },
            };
            manifest["members"] = members;
            var tmp = outPath + ".partial";
            try
            {
                using (var f = new FileStream(tmp, FileMode.CreateNew, FileAccess.ReadWrite))
                {
                    var w = new ZipWriter(f, DateTime.Now);
                    var rb = Encoding.UTF8.GetBytes(Readme); w.Add(ReadmeName, true, s => s.Write(rb, 0, rb.Length));
                    var done = new HashSet<string>();
                    foreach (var (src, sha, size) in checkedMembers)
                    {
                        var name = PrivateRoles.MemberName(src.Role, sha); if (!done.Add(name)) continue;
                        string got;
                        using (var s = src.Open()) got = w.Add(name, false, o => Streams.Copy(s, o)).sha256;
                        if (got != sha) throw Refuse("private.changed", "\"" + (src.Path ?? name) + "\" changed while the package was being built; nothing was saved");
                    }
                    var mb = Json.Bytes(manifest); w.Add(ManifestName, true, s => s.Write(mb, 0, mb.Length));
                    w.Finish(); f.Flush(true);
                }
                var info = Open(tmp, true);
                File.Move(tmp, outPath);
                return info;
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        /// <summary>one member's bytes, checked against its SHA-256 (for applying a package that Open has already checked)</summary>
        public static byte[] ReadMember(string path, PrivatePackageInfo.Member m)
        {
            using (var z = SafeZip.Open(path, Limits))
            {
                var e = z.Find(m.Name) ?? throw Refuse("private.missing", "member " + m.Name + " is not in the file");
                byte[] b; using (var s = z.OpenMember(e)) { var ms = new MemoryStream(); s.CopyTo(ms); b = ms.ToArray(); }
                if (Sha.Of(b) != m.Sha256) throw Refuse("private.hash", "member " + m.Name + " is not the file its fingerprint names (changed or damaged)");
                return b;
            }
        }

        /// <summary>opens and checks a private package: archive safety, the manifest (private label, destination, plan, members), every
        /// payload member listed and bound (and, with verifyPayload, its SHA-256, CRC-32 and real role)</summary>
        public static PrivatePackageInfo Open(string path, bool verifyPayload = true)
        {
            using (var z = SafeZip.Open(path, Limits))
            {
                var me = z.Find(ManifestName) ?? throw Refuse("private.manifest", "it has no " + ManifestName);
                var d = StrictJson.ParseObject(z.ReadAll(me, MaxManifestBytes), new JsonLimits { MaxBytes = (int)MaxManifestBytes, MaxDepth = 16, MaxValues = 1000000, MaxString = 4096 }, "The package's manifest");
                if (d["format"] as string != Format || d["schema"] as string != Schema) throw Refuse("private.format", "it is not a " + Schema + " package");
                if (!(d["private"] is bool pr && pr) || !(d["label"] is string lb && lb.StartsWith("PRIVATE"))) throw Refuse("private.label", "it is not labeled private");
                var dest = d["destination"] as JObj; var hw = dest?["hwid"] as string;
                if (hw == null || !Hwid.IsMatch(hw) || dest["tag"] as string != ConsoleIdentity.HwidTag(hw)) throw Refuse("private.destination", "it does not name its destination console consistently");
                var info = new PrivatePackageInfo { Manifest = d, PackageId = d["package_id"] as string, DestinationHwid = hw, DestinationTag = dest["tag"] as string, Label = d["label"] as string };
                if (!NewId.Is(info.PackageId)) throw Refuse("private.format", "its package id is damaged");
                var byName = new Dictionary<string, PrivatePackageInfo.Member>();
                foreach (var x in Doc.Arr(d["members"]) ?? throw Refuse("private.format", "it has no member list"))
                {
                    var m = x as JObj ?? throw Refuse("private.format", "a member entry is damaged");
                    var mm = new PrivatePackageInfo.Member { Name = m["name"] as string, Role = m["role"] as string, Sha256 = m["sha256"] as string, Size = Doc.Int(m["size"]), Provenance = m["provenance"] as JObj };
                    if (!PrivateRoles.All.ContainsKey(mm.Role ?? "") || !Sha.Is(mm.Sha256) || mm.Name != PrivateRoles.MemberName(mm.Role, mm.Sha256)) throw Refuse("private.format", "member " + Display.Line(mm.Name, 80) + " is not named by its role and fingerprint");
                    if (mm.Provenance == null || !(mm.Provenance["kind"] is string pk && new[] { "console-backup", "user-file", "public-build", "ccs-built" }.Contains(pk))) throw Refuse("private.provenance", "member " + mm.Name + " does not say where it came from");
                    if (byName.ContainsKey(mm.Name)) throw Refuse("private.format", "member " + mm.Name + " is listed twice");
                    var e = z.Find(mm.Name) ?? throw Refuse("private.missing", "member " + mm.Name + " is listed but not in the file");
                    if (e.Size != mm.Size) throw Refuse("private.size", "member " + mm.Name + " is not the size the manifest says");
                    byName[mm.Name] = mm; info.Members.Add(mm);
                }
                foreach (var e in z.Entries) if (e.Name != ManifestName && e.Name != ReadmeName && !byName.ContainsKey(e.Name)) throw Refuse("private.unlisted", "member " + e.Name + " is not listed in the manifest");
                // the exact target plan: every component names a member of its role, in a place that role may go; every member is used
                var plan = d["target_plan"] as JObj ?? throw Refuse("private.plan", "it has no target plan");
                if (!Composition.InstallPolicies.Contains(plan["install_policy"] as string) || !Composition.StartingPoints.Contains(plan["starting_point"] as string)) throw Refuse("private.plan", "its target plan names no known install policy or starting point");
                var envIds = new HashSet<string>((Doc.Arr(plan["environments"]) ?? new List<object>()).OfType<JObj>().Select(e => e["id"] as string).Where(i => i != null));
                var used = new HashSet<string>();
                foreach (var x in Doc.Arr(plan["components"]) ?? throw Refuse("private.plan", "its target plan lists no components"))
                {
                    var c = x as JObj ?? throw Refuse("private.plan", "a component is damaged");
                    string role = c["role"] as string, sha = c["sha256"] as string, place = c["place"] as string;
                    if (!PrivateRoles.All.TryGetValue(role ?? "", out var r) || !Sha.Is(sha) || !byName.TryGetValue(PrivateRoles.MemberName(role, sha), out var mm)) throw Refuse("private.plan", "a component names no member of its role");
                    if (place == null || !Regex.IsMatch(place, r.Place)) throw Refuse("private.place", Display.Line(place, 60) + " is not a place " + r.Text + " can go");
                    if (role == "data-file" && !(Engine.FilesEngine.PathOk(place.Substring(5)) && Engine.FilesEngine.InScope("compose", place.Substring(5)))) throw Refuse("private.place", Display.Line(place, 60) + " is not a path Console Composer may write");
                    var em = Regex.Match(place, "^environment:([a-z][a-z0-9-]{0,15})/"); if (em.Success && !envIds.Contains(em.Groups[1].Value)) throw Refuse("private.place", "a component names an environment the plan does not have");
                    if (place == "same-console-repair" && !(mm.Provenance["kind"] as string == "console-backup" && mm.Provenance["console_tag"] as string == info.DestinationTag))
                        throw Refuse("private.boot-transplant", "boot stage repair material must come from a backup of the destination console itself (no boot stage of another console is ever written)");
                    mm.Places.Add(place); used.Add(mm.Name);
                }
                foreach (var mm in info.Members) if (!used.Contains(mm.Name)) throw Refuse("private.unexplained", "member " + mm.Name + " is not used by the target plan");
                if (verifyPayload)
                    foreach (var mm in info.Members)
                    {
                        var e = z.Find(mm.Name);
                        string sha;
                        try { using (var s = z.OpenMember(e)) sha = Sha.Of(s); }
                        catch (IOException ex) { throw Refuse("private.damaged", ex.Message); }
                        if (sha != mm.Sha256) throw Refuse("private.hash", "member " + mm.Name + " is not the file its fingerprint names (changed or damaged)");
                        using (var w = z.StoredWindow(e)) { var why = PrivateRoles.Check(mm.Role, w); if (why != null) throw Refuse("private.role", "member " + mm.Name + " " + why); }
                    }
                return info;
            }
        }
    }
}
