// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CCS HOME's own package on the console, placed by the CCS Console Workbench (both hosts) when it adds CCS Mod, the only add-on that
// provides CCS HOME (CCS Nucleus can start it, but never installs it), so CCS HOME is there without a full Studio or a Hakchi PC program.
// The package is a console package as Classic Cart Studio builds it (tools/companion/native_home/console_package.py, layout "mod": its
// leave item, Choose Another Menu, goes to the startup chooser when installed, otherwise Original Menu): the release-built one this engine carries (tools/console-nucleus/build_home_package.py: the Included Games, embedded beside the
// console programs as ram/ccs-home/) or a package folder the caller names (Classic Cart Studio's own collection, tests).
// It goes to the console AFTER the preparation image runs (ram/ccsprep hput, huse): the FEL image limit applies to that image only. The
// layout on the console is Classic Cart Studio's installer's (tools/companion/console_engine/ccs_install.py), so the app and the Workbench
// read each other's copies the same way:
//   /var/lib/ccs/pkg/<version>.<attempt>/   the copy (version: the first 16 hex of its MANIFEST.json's SHA-256)
//   /var/lib/ccs/pkg/current                the copy in use ("<copy>\n"), replaced in one rename
//   /var/lib/ccs/install/v-<copy>.json      the copy's record (its files and their SHA-256): what makes the copy this program's
//   /var/lib/ccs/install/attempt-<a>.json   an installation in progress (removed by the switch)
//   /var/lib/ccs/data/IDENTITY.json         the saves' identity (written only when absent; existing saves are never replaced)
// The transfer is one archive (an uncompressed ustar stream: pkg/<every file of the package>, rec/<the three records>) whose size and
// SHA-256 are bound in the reviewed plan; the console receives it into RAM, checks both, unpacks it beside the copy in use and checks every
// file against the copy's record before anything is switched (hput); the switch is its own step (huse). The records are written exactly as
// ccs_install.py writes them (Python's json.dumps, sorted keys, indent 1), which the console reads back line by line.
using CcsBackup.Device;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsBackup.Engine
{
    public sealed class HomePackageException : Exception { public HomePackageException(string m) : base(m) { } }

    public sealed class HomePackage
    {
        public static readonly string[] Steps = { "hput", "huse", "hdrop" };
        public const string Schema = "ccs-console-package/1";
        public const long RamMargin = 16000000;               // RAM kept free beside the received archive (ccsprep hput checks it again)
        public const long MaxArchive = 200000000, MaxTotal = 400000000, MaxFile = 64000000; public const int MaxFiles = 4000;
        static readonly Regex Rel = new Regex("^[A-Za-z0-9_-][A-Za-z0-9._-]{0,119}(/[A-Za-z0-9_-][A-Za-z0-9._-]{0,119}){0,7}$");
        public static readonly Regex CopyRe = new Regex("^[0-9a-f]{16}\\.[0-9a-f]{8}$");

        public sealed class Entry { public string Rel, Sha256, Mode; public long Size; }
        public string Layout, Label, Origin, ManifestSha256, Version; public byte[] Manifest; public long TotalBytes; public int Games, Playable;
        public Dictionary<string, string> StateIdentity;       // what the package's player writes into its saves (the installer's STATE_IDENTITY)
        public List<Entry> Files = new List<Entry>();
        Func<string, byte[]> read;

        /// <summary>the shared storage accounting's input for sending this package beside the copy in use: its files (each in whole blocks)
        /// against the console's free space as its df reports it (the reserved blocks already left out)</summary>
        public static Dictionary<string, object> SpaceInput(HomePackage p, long free, string profile = StorageSpec.NesModel) => new Dictionary<string, object>
        {
            ["profile"] = profile, ["storage"] = new Dictionary<string, object> { ["source"] = "measured", ["available_bytes"] = free },
            ["items"] = new object[] { new Dictionary<string, object> { ["category"] = "home", ["label"] = "CCS HOME", ["files"] = p.Files.Select(f => (object)new Dictionary<string, object> { ["bytes"] = f.Size }).ToArray() } },
        };

        public static string StepLabel(string s) => s == "hput" ? "CCS HOME's package sent to the console and checked there (the HOME in use stays until the switch)"
            : s.StartsWith("hdrop:", StringComparison.Ordinal) ? "An earlier CCS HOME version removed (" + s.Substring(6) + "), after the new one is in use and read back" : "CCS HOME switched to the new package";

        /// <summary>CCS HOME's layout: CCS Mod provides CCS HOME (its leave item, Choose Another Menu, goes to CCS Nucleus's chooser when it is installed, else to Original Menu)</summary>
        public const string ModLayout = "mod";
        /// <summary>the package this build carries for a layout; null when this build does not carry one</summary>
        public static HomePackage Embedded(string layout)
        {
            var m = RecoveryImages.HomeResource(layout + ".MANIFEST.json"); if (m == null) return null;
            return Parse(m, f => RecoveryImages.HomeResource(f.Sha256 + ".blob"), "embedded", layout);
        }

        /// <summary>a console package folder (MANIFEST.json and its files), for the layout given</summary>
        public static HomePackage Folder(string dir, string layout)
        {
            string mf = Path.Combine(dir ?? "", "MANIFEST.json");
            if (string.IsNullOrEmpty(dir) || !File.Exists(mf)) throw new HomePackageException("The HOME package folder has no MANIFEST.json.");
            var full = Path.GetFullPath(dir);
            return Parse(File.ReadAllBytes(mf), f => { var p = Path.GetFullPath(Path.Combine(full, f.Rel.Replace('/', Path.DirectorySeparatorChar))); return p.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(p) ? File.ReadAllBytes(p) : null; }, full, layout);
        }

        static HomePackage Parse(byte[] manifest, Func<Entry, byte[]> reader, string origin, string layout)
        {
            Dictionary<string, object> m;
            try { m = Json.Parse(Encoding.UTF8.GetString(manifest)); } catch (FormatException) { throw new HomePackageException("The HOME package's MANIFEST.json cannot be read."); }
            if (m.S("schema") != Schema) throw new HomePackageException("This is not a Classic Cart Studio console package.");
            if (m.S("layout") != layout) throw new HomePackageException("The HOME package was made for " + (m.S("layout") ?? "another place") + ", not for " + (layout == ModLayout ? "CCS Mod" : layout) + ".");
            if (m.O("console_paths")?.S("pkg") != "/tmp/ccs/pkg") throw new HomePackageException("The HOME package was made for another place on the console.");
            var fs = m.O("files"); if (fs == null || fs.Count == 0 || fs.Count > MaxFiles) throw new HomePackageException("The HOME package's file list is missing or too long.");
            var p = new HomePackage { Layout = layout, Origin = origin, Manifest = manifest, Label = m.S("label") ?? "HOME", Games = (int)m.L("games", 0), Playable = (int)m.L("playable", 0) };
            using (var h = SHA256.Create()) p.ManifestSha256 = Hex.Of(h.ComputeHash(manifest));
            p.Version = p.ManifestSha256.Substring(0, 16);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in fs.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var info = kv.Value as Dictionary<string, object>; var rel = kv.Key;
                if (!Rel.IsMatch(rel) || rel == "MANIFEST.json" || !seen.Add(rel) || info == null) throw new HomePackageException("The HOME package lists a file it may not carry: " + Display(rel));
                var mode = info.S("mode") ?? "0o644"; var sha = info.S("sha256"); var size = info.L("size");
                if (mode != "0o644" && mode != "0o755" || mode == "0o755" && !rel.Split('/').Reverse().Skip(1).Contains("bin")) throw new HomePackageException(rel + ": mode " + mode + " is not allowed.");
                if (!Hex.IsHex(sha, 64) || size < 0 || size > MaxFile) throw new HomePackageException(rel + " is not listed with its size and SHA-256.");
                p.Files.Add(new Entry { Rel = rel, Sha256 = sha, Size = size, Mode = mode == "0o755" ? "0755" : "0644" });
            }
            p.Files.Add(new Entry { Rel = "MANIFEST.json", Sha256 = p.ManifestSha256, Size = manifest.LongLength, Mode = "0644" });
            p.TotalBytes = p.Files.Sum(f => f.Size);
            if (p.TotalBytes > MaxTotal) throw new HomePackageException("The HOME package is larger than the console can hold.");
            if (!p.Files.Any(f => f.Rel == "bin/ccs-player-armv7" && f.Mode == "0755") || !p.Files.Any(f => f.Rel == "session.json")) throw new HomePackageException("The HOME package has no console player or session.");
            // the player's saves name its platform, core and core version (gp.h): "fceumm <version>" in the manifest's player record
            var core = (m.O("player")?.S("core") ?? "").Split(' ');
            if (core.Length != 2 || core[0] != "fceumm" || !Regex.IsMatch(core[1], "^[0-9a-f]{6,40}$")) throw new HomePackageException("The HOME package does not name its game player's core.");
            p.StateIdentity = new Dictionary<string, string> { { "platform", "linux-armv7" }, { "core", core[0] }, { "core_version", core[1] } };
            p.read = rel => { var f = p.Files.First(x => x.Rel == rel); if (rel == "MANIFEST.json") return manifest; var b = reader(f); if (b == null || b.LongLength != f.Size) throw new HomePackageException(rel + " is missing from the HOME package."); using (var h = SHA256.Create()) if (Hex.Of(h.ComputeHash(b)) != f.Sha256) throw new HomePackageException(rel + " differs from the HOME package's list."); return b; };
            return p;
        }

        static string Display(string s) => s == null ? "(none)" : s.Length > 60 ? s.Substring(0, 60) + "..." : s;

        /// <summary>every file read once and checked against the manifest (size and SHA-256); the first that differs throws</summary>
        public byte[] Read(string rel) => read(rel);
        public string Describe => Label + ": " + Playable + " of " + Games + " game" + (Games == 1 ? "" : "s") + ", " + Compose.Units.Size(TotalBytes);
    }

    /// <summary>the archive one transfer sends (hput), built once per plan; its bytes are what the plan binds by size and SHA-256</summary>
    public sealed class HomeArchive
    {
        public string Copy, Attempt, Sha256, Version, ManifestSha256, Layout; public long Size, Total; public byte[] Bytes;
        public JObj ToJson() => new JObj { { "copy", Copy }, { "layout", Layout }, { "manifest_sha256", ManifestSha256 }, { "archive_sha256", Sha256 }, { "archive_bytes", Size }, { "package_bytes", Total }, { "size", Compose.Units.Size(Total) } };
        public string DigestLine() => Copy + " " + Layout + " " + ManifestSha256 + " " + Sha256 + " " + Size.ToString(CultureInfo.InvariantCulture) + " " + Total.ToString(CultureInfo.InvariantCulture);

        /// <summary>attempt: 8 hex (a new installation's own, or an unfinished one's to finish it); prior: the copy in use ("absent": none);
        /// installed: the copy record's installed_utc (this PC's time when the copy was first planned, yyyy-MM-ddTHH:mm:ssZ; the console has no
        /// clock to trust), or null for a record without one. An unfinished attempt is finished with its record's own value, so the record is
        /// the same bytes (ccsprep hput compares it)</summary>
        public static HomeArchive Build(HomePackage p, string attempt, string hwid, string prior, string installed = null)
        {
            if (installed != null && !InstalledRe.IsMatch(installed)) throw new ArgumentException("installed: yyyy-MM-ddTHH:mm:ssZ");
            if (!Hex.IsHex(attempt, 8)) throw new ArgumentException("attempt: 8 hex digits");
            var a = new HomeArchive { Attempt = attempt, Version = p.Version, Copy = p.Version + "." + attempt, ManifestSha256 = p.ManifestSha256, Layout = p.Layout, Total = p.TotalBytes };
            var files = new SortedDictionary<string, object>(StringComparer.Ordinal); foreach (var f in p.Files) files[f.Rel] = f.Sha256;
            var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            // the records, exactly as ccs_install.py writes them; "plan" names the package (a re-review of the same attempt makes the same copy record)
            var vd = new SortedDictionary<string, object>(StringComparer.Ordinal) { { "schema", "ccs-owned-copy/1" }, { "copy", a.Copy }, { "plan", "home-" + p.ManifestSha256 }, { "version", p.Version }, { "attempt", attempt }, { "manifest_sha256", p.ManifestSha256 }, { "files", files }, { "previous", prior == "absent" ? null : prior } };
            if (installed != null) vd["installed_utc"] = installed;
            var vrec = PyJson.Record(vd);
            var arec = PyJson.Record(new SortedDictionary<string, object>(StringComparer.Ordinal) { { "schema", "ccs-install-attempt/1" }, { "attempt", attempt }, { "plan", "home-" + p.ManifestSha256 }, { "version", p.Version }, { "copy", a.Copy }, { "kind", "workbench" },
                { "device", new SortedDictionary<string, object>(StringComparer.Ordinal) { { "hwid", ConsoleIdentity.NormalizeHwid(hwid) } } }, { "started", now },
                { "prior", new SortedDictionary<string, object>(StringComparer.Ordinal) { { "pointer", prior == "absent" ? null : prior } } } });
            var irec = PyJson.Record(new SortedDictionary<string, object>(StringComparer.Ordinal) { { "schema", "ccs-console-data/1" }, { "state_identity", new SortedDictionary<string, object>(p.StateIdentity.ToDictionary(x => x.Key, x => (object)x.Value), StringComparer.Ordinal) },
                { "created", now }, { "note", "saves, slots and settings made on this console by Classic Cart Studio's console player" } });
            var ms = new MemoryStream(); var tar = new Ustar(ms);
            tar.Dir("pkg/");
            foreach (var d in p.Files.SelectMany(f => { var parts = f.Rel.Split('/'); return Enumerable.Range(1, parts.Length - 1).Select(k => string.Join("/", parts.Take(k))); }).Distinct().OrderBy(x => x, StringComparer.Ordinal)) tar.Dir("pkg/" + d + "/");
            foreach (var f in p.Files.OrderBy(x => x.Rel, StringComparer.Ordinal)) tar.File("pkg/" + f.Rel, p.Read(f.Rel), f.Mode == "0755" ? 0x1ED : 0x1A4);
            tar.Dir("rec/");
            tar.File("rec/attempt-" + attempt + ".json", arec, 0x1A4); tar.File("rec/v-" + a.Copy + ".json", vrec, 0x1A4); tar.File("rec/IDENTITY.json", irec, 0x1A4);
            tar.End();
            a.Bytes = ms.ToArray(); a.Size = a.Bytes.LongLength;
            if (a.Size > HomePackage.MaxArchive) throw new HomePackageException("The HOME package is larger than one transfer can carry.");
            using (var h = SHA256.Create()) a.Sha256 = Hex.Of(h.ComputeHash(a.Bytes));
            return a;
        }

        public static string NewAttempt() { var b = new byte[4]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b); return Hex.Of(b); }
        /// <summary>a copy record's installed_utc</summary>
        public static readonly Regex InstalledRe = new Regex("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$");
        public static string Now() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    /// <summary>the HOME steps of a plan (Preparation, MethodEngine): what the console holds now (ccsprep mstate) decides them; run and
    /// verified the same way for both</summary>
    public static class HomeSteps
    {
        public sealed class Decision { public HomeArchive Archive; public string Prior, Refusal; public List<string> Steps = new List<string>(); public string Text; }

        /// <summary>no steps (this exact package is the copy in use), "huse" alone (it was placed and its switch did not finish), or "hput",
        /// "huse"; a refusal when the console's HOME folder is not this program's to change, the saves were made by another player build, an
        /// earlier installation of another package did not finish, or the room or RAM does not suffice</summary>
        public static Decision Plan(HomePackage p, MethodState st, string hwid, string profile = StorageSpec.NesModel)
        {
            var d = new Decision();
            Decision No(string why) { d.Refusal = why; d.Steps.Clear(); return d; }
            if (p == null) return No("This build does not carry CCS HOME's package, so the method would have no HOME to start. Nothing was changed.");
            if (st.HomeLink) return No("A link is on the way to Classic Cart Studio's folder on the console; nothing is written through it.");
            if (st.HomePointer == "other") return No("The console's pkg/current is not a HOME this program recorded; it is kept, and nothing is written.");
            if (st.HomePointer != "absent" && !st.HomeCopies.ContainsKey(st.HomePointer)) return No("The HOME the console uses now has no record of this program; it is kept, and nothing is written.");
            if (st.HomeIdentity != null)
            {
                Dictionary<string, object> idj = null; try { idj = Json.Parse(Encoding.UTF8.GetString(st.HomeIdentity)); } catch (FormatException) { }
                var have = idj?.O("state_identity");
                if (idj?.S("schema") != "ccs-console-data/1" || have == null) return No("The saves' identity file on the console cannot be read; the saves are kept and nothing is written.");
                if (have.Count != p.StateIdentity.Count || p.StateIdentity.Any(kv => have.S(kv.Key) != kv.Value))
                    return No("The saves on the console were made by another game player build (" + string.Join(" ", have.Select(kv => kv.Value as string)) + "); this HOME's player could not load them. Nothing is written.");
            }
            d.Prior = st.HomePointer;
            // the date in the copy record of an attempt being finished: its own record's (none in it: none), or now when it has no record yet
            string Installed(string copy) => st.HomeCopies.ContainsKey(copy) ? (st.HomeInfo.TryGetValue(copy, out var hi) ? hi.installed : null) : HomeArchive.Now();
            var mine = st.HomeAttempts.Where(a => a.Value == p.Version + "." + a.Key).Select(a => a.Key).ToList();
            var others = st.HomeAttempts.Keys.Except(mine).ToList();
            if (others.Count > 0 || mine.Count > 1) return No("An earlier installation of a HOME on this console did not finish (" + string.Join(", ", st.HomeAttempts.Select(a => a.Key + ": " + a.Value)) + "). It is kept as it is; finish it with the same HOME package, or collect diagnostics.");
            if (st.HomeInUse(p.ManifestSha256))
            {
                var inUse = st.HomePointer.Substring(17);
                if (mine.Count == 0) { d.Text = "CCS HOME is already this package on the console."; return d; }
                if (mine[0] != inUse) return No("An earlier installation of this HOME did not finish beside the copy in use. It is kept; collect diagnostics.");
                d.Archive = HomeArchive.Build(p, inUse, hwid, d.Prior, Installed(p.Version + "." + inUse)); d.Steps.Add("huse"); d.Text = "The switch to CCS HOME's package was not finished; it is finished now (nothing is sent again)."; return d;
            }
            if (st.Free < 0) return No("The console's free space could not be read. Nothing was changed.");
            var room = StorageSpec.Account(HomePackage.SpaceInput(p, st.Free, profile));
            if (!(bool)room["write_ok"]) return No(Capacity.Why(room, out _, out _) + " Nothing was changed.");
            d.Archive = mine.Count == 1 ? HomeArchive.Build(p, mine[0], hwid, d.Prior, Installed(p.Version + "." + mine[0])) : HomeArchive.Build(p, HomeArchive.NewAttempt(), hwid, d.Prior, HomeArchive.Now());
            if (st.Ram >= 0 && st.Ram < d.Archive.Size + HomePackage.RamMargin) return No("The console's memory cannot hold CCS HOME's package for the transfer (" + Compose.Units.Size(st.Ram) + " free, " + Compose.Units.Size(d.Archive.Size + HomePackage.RamMargin) + " needed).");
            d.Steps.Add("hput"); d.Steps.Add("huse");
            d.Text = "CCS HOME (" + p.Describe + ") is sent to the console in this session, checked file by file and then switched to; " + (d.Prior == "absent" ? "the console had no CCS HOME before." : "the HOME it replaces stays on the console.");
            return d;
        }

        /// <summary>one HOME step: hput streams the plan's archive (its size and SHA-256 are the arguments; the console checks both)</summary>
        public static WriteResult Run(RecoveryConsole con, WriteAuthority auth, string step, HomeArchive a, string prior) =>
            step == "hput" ? con.PrepStepStream("hput", auth, new[] { a.Copy, a.Size.ToString(CultureInfo.InvariantCulture), a.Sha256, a.Total.ToString(CultureInfo.InvariantCulture) }, new MemoryStream(a.Bytes, false), a.Size)
            : step.StartsWith("hdrop:", StringComparison.Ordinal) ? con.PrepStep("hdrop", auth, new[] { step.Substring(6), a.Copy })   // an earlier copy, only while this one is in use
            : con.PrepStep("huse", auth, new[] { a.Copy, prior });

        /// <summary>a step of CCS HOME's: hput, huse, or hdrop:COPY (an earlier copy removed after the switch)</summary>
        public static bool IsStep(string s) => HomePackage.Steps.Contains(s) || s.StartsWith("hdrop:", StringComparison.Ordinal) && HomePackage.CopyRe.IsMatch(s.Substring(6));
        /// <summary>the write target a step needs (hdrop:COPY: hdrop)</summary>
        public static string Target(string s) => s.StartsWith("hdrop:", StringComparison.Ordinal) ? "hdrop" : s;

        /// <summary>the step's answer is exactly this archive's ("ok COPY SHA", "ok COPY", or "ok already ..." when it was already done)</summary>
        public static bool Answer(WriteResult r, string step, HomeArchive a) => step.StartsWith("hdrop:", StringComparison.Ordinal)
            ? r.Ok && Regex.IsMatch(r.Line ?? "", "^result hdrop ok( already)? " + Regex.Escape(step.Substring(6)) + "$")
            : r.Ok && Regex.IsMatch(r.Line ?? "", "^result " + step + " ok( already)? " + Regex.Escape(a.Copy) + (step == "hput" ? " " + a.Sha256 : "") + "$");

        /// <summary>the console read again after the steps: this copy in use, recorded with this package's MANIFEST.json, nothing unfinished</summary>
        public static bool InUse(MethodState after, HomeArchive a) => after.Ok && after.HomePointer == a.Copy && after.HomeInUse(a.ManifestSha256) && !after.HomeAttempts.ContainsKey(a.Attempt);
    }

    /// <summary>a ustar stream as hakchi's BusyBox tar reads it: entries in the order written, owner 0:0, a fixed time, no links</summary>
    sealed class Ustar
    {
        const long Mtime = 1763164800; readonly Stream s;
        public Ustar(Stream s) { this.s = s; }
        public void Dir(string name) => Entry(name, '5', 0x1ED, null);
        public void File(string name, byte[] data, int mode) => Entry(name, '0', mode, data);
        public void End() => s.Write(new byte[1024], 0, 1024);
        void Entry(string name, char type, int mode, byte[] data)
        {
            var nb = Encoding.ASCII.GetBytes(name); if (nb.Length > 100) throw new HomePackageException("A HOME package path is too long for the transfer: " + name);
            var h = new byte[512];
            Array.Copy(nb, h, nb.Length);
            Put(h, 100, Oct(mode, 7)); Put(h, 108, Oct(0, 7)); Put(h, 116, Oct(0, 7)); Put(h, 124, Oct(data?.LongLength ?? 0, 11)); Put(h, 136, Oct(Mtime, 11));
            for (int i = 148; i < 156; i++) h[i] = (byte)' ';
            h[156] = (byte)type; Put(h, 257, "ustar"); Put(h, 263, "00"); Put(h, 265, "root"); Put(h, 297, "root");
            int sum = 0; foreach (var b in h) sum += b; Put(h, 148, Oct(sum, 6)); h[154] = 0; h[155] = (byte)' ';
            s.Write(h, 0, 512);
            if (data == null || data.Length == 0) return;
            s.Write(data, 0, data.Length); int pad = (512 - data.Length % 512) % 512; s.Write(new byte[pad], 0, pad);
        }
        static string Oct(long v, int digits) => Convert.ToString(v, 8).PadLeft(digits, '0');
        static void Put(byte[] h, int at, string v) { var b = Encoding.ASCII.GetBytes(v); Array.Copy(b, 0, h, at, b.Length); }
    }

    /// <summary>Python's json.dumps(obj, sort_keys=True, indent=1) + "\n" for string/null/dict values: the installer's record format</summary>
    static class PyJson
    {
        public static byte[] Record(SortedDictionary<string, object> o) { var sb = new StringBuilder(); W(sb, o, 0); sb.Append('\n'); return Encoding.ASCII.GetBytes(sb.ToString()); }
        static void W(StringBuilder sb, object o, int ind)
        {
            if (o == null) { sb.Append("null"); return; }
            if (o is string s) { Str(sb, s); return; }
            var d = o as IDictionary<string, object>; if (d == null) throw new ArgumentException("records carry strings and objects only");
            if (d.Count == 0) { sb.Append("{}"); return; }
            sb.Append("{\n"); int k = 0;
            foreach (var kv in d.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                sb.Append(' ', ind + 1); Str(sb, kv.Key); sb.Append(": "); W(sb, kv.Value, ind + 1); sb.Append(++k < d.Count ? ",\n" : "\n");
            }
            sb.Append(' ', ind).Append('}');
        }
        static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                if (c == '"') sb.Append("\\\""); else if (c == '\\') sb.Append("\\\\"); else if (c == '\n') sb.Append("\\n"); else if (c == '\r') sb.Append("\\r"); else if (c == '\t') sb.Append("\\t");
                else if (c == '\b') sb.Append("\\b"); else if (c == '\f') sb.Append("\\f"); else if (c < 0x20 || c > 0x7f) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
            }
            sb.Append('"');
        }
    }
}
