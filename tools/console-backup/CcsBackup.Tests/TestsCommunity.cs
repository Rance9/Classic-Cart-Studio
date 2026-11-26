// Classic Cart Studio console backup and restore engine tests: Community Mods (Engine/CommunityCatalog.cs, CommunityPackage.cs,
// CommunityEngine.cs; Device/CommunityConsole.cs, SimulatedCommunity.cs). GPL-3.0-or-later. Every package and catalog here is a private
// fixture the test makes (small synthetic .hmod archives, a fixture catalog signed with a key made for the run); none is a real author's
// package and none goes into the catalog this build carries. Simulated consoles only (no test opens a real session). The USB and SD backups
// are made here from a temporary folder standing in for a USB drive's root and from a drive image file the test writes; no test reads a
// real drive. SimulatedConsole.NormalStart stands in for hakchi's p7080_hmods (pack_uninstall, then pack_install of the real archive).
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using CcsBackup.External;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        // ------------------------------------------------------------------ fixture archives
        internal sealed class TarEnt { public string Name; public char Type = '0'; public byte[] Data = new byte[0]; public string Link = ""; public string PaxPath; public bool Long; public bool BadSum; }
        internal static TarEnt TF(string name, string text) => new TarEnt { Name = name, Data = Encoding.UTF8.GetBytes(text) };
        internal static TarEnt TD(string name) => new TarEnt { Name = name, Type = '5' };
        static TarEnt TL(string name, string target) => new TarEnt { Name = name, Type = '2', Link = target };
        static TarEnt TH(string name, string target) => new TarEnt { Name = name, Type = '1', Link = target };

        static void Header(MemoryStream o, string name, char type, long size, string link, bool badSum = false)
        {
            var h = new byte[512];
            void Put(int at, string s, int len) { var b = Encoding.UTF8.GetBytes(s); Array.Copy(b, 0, h, at, Math.Min(b.Length, len)); }
            Put(0, name, 100); Put(100, type == '5' ? "0000755" : "0000644", 8); Put(108, "0000000", 8); Put(116, "0000000", 8);
            Put(124, Convert.ToString(size, 8).PadLeft(11, '0'), 12); Put(136, "14713377144", 12); Put(156, type.ToString(), 1); Put(157, link ?? "", 100);
            Put(257, "ustar", 6); Put(263, "00", 2); Put(265, "root", 32); Put(297, "root", 32);
            for (int i = 148; i < 156; i++) h[i] = 32;
            long sum = h.Sum(x => (long)x); if (badSum) sum += 7;
            Put(148, Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ", 8);
            o.Write(h, 0, 512);
        }
        static void Body(MemoryStream o, byte[] d) { o.Write(d, 0, d.Length); var pad = (512 - d.Length % 512) % 512; o.Write(new byte[pad], 0, pad); }
        internal static byte[] Tar(params TarEnt[] ents)
        {
            var o = new MemoryStream();
            foreach (var e in ents)
            {
                var name = e.Name;
                if (e.Long || Encoding.UTF8.GetByteCount(name) > 100) { var d = Encoding.UTF8.GetBytes(name + "\0"); Header(o, "././@LongLink", 'L', d.Length, ""); Body(o, d); name = name.Substring(0, Math.Min(99, name.Length)); }
                if (e.PaxPath != null) { var body = " path=" + e.PaxPath + (char)10; int len = body.Length + 1; while ((len.ToString() + body).Length != len) len++; var d = Encoding.UTF8.GetBytes(len + body); Header(o, "PaxHeaders/x", 'x', d.Length, ""); Body(o, d); }
                Header(o, name, e.Type, e.Type == '0' ? e.Data.Length : 0, e.Link, e.BadSum);
                if (e.Type == '0') Body(o, e.Data);
            }
            o.Write(new byte[1024], 0, 1024);
            return o.ToArray();
        }
        internal static byte[] CGz(byte[] b) { var o = new MemoryStream(); using (var z = new GZipStream(o, CompressionLevel.Optimal, true)) z.Write(b, 0, b.Length); return o.ToArray(); }
        internal static byte[] Hmod(params TarEnt[] e) => CGz(Tar(e));

        /// <summary>a fixture mod's package: a tool, a start script and a folder with its default settings; its own uninstall deletes the
        /// person's settings (as the audited packages' own removals do)</summary>
        static byte[] FixtureHmod(string pkg, string version, string extra = null) => Hmod(TD("./"), TD("./bin"), TF("./bin/" + pkg, "#!/bin/sh\necho " + pkg + " " + version + "\n"),
            TD("./etc"), TD("./etc/init.d"), TF("./etc/init.d/S81" + pkg, "#!/bin/sh\n" + pkg + " &\n"), TD("./etc/" + pkg), TF("./etc/" + pkg + "/defaults.cfg", "volume=5\n" + (extra ?? "")),
            TF("./install", "#!/bin/sh\necho installing " + pkg + "\n"), TF("./uninstall", "rm -f \"$rootfs/etc/" + pkg + "/settings.cfg\"\nrm -f \"$rootfs/etc/" + pkg + "/favorites.txt\"\n"), TF("./readme.md", pkg + " " + version + "\n"));

        // ------------------------------------------------------------------ fixture catalog
        sealed class ModFx { public string Id, Name, Section, Package, Version = "1.0.0", Launch = "service", Program, Return = "Returns to Original Menu."; public int Order; public byte[] Hmod; public string[] Depends = new string[0]; public (string id, string text)[] Conflicts = new (string, string)[0]; public JObj Compat; public bool External, Alpha; public (string v, string sha)[] From = new (string, string)[0]; public string UpdateMethod = "install-over"; public (string pkg, string name)[] Removes = new (string, string)[0]; }
        static string ShaOf(byte[] b) => Program.Sha(b);
        /// <summary>the CCS Nucleus and chooser packages 0.11.11 shipped (an earlier version for the update cases)</summary>
        const string OldNucleusPin = "0074f2ec6bba1f7f329a3eafdfac4beaef3681e87802680e38aa435043eef2b7", OldChooserPin = "1c6abd0f646332ccd59300d85cb427333aa39e72792d68cf895c3756ed6d3a27";
        static JObj RecordJson(ModFx f)
        {
            var variants = new List<object> { new JObj { { "variant", "standard" }, { "package", f.Package }, { "url", "https://mods.example.org/" + f.Id + "/" + f.Package + ".hmod" }, { "file_name", f.Package + ".hmod" }, { "bytes", (long)f.Hmod.Length }, { "sha256", ShaOf(f.Hmod) }, { "unpacked_bytes", 100000L } } };
            if (f.Compat != null) variants.Add(f.Compat);
            return new JObj
            {
                { "schema", "ccs-community-record/1" }, { "id", f.Id }, { "record_version", 1 }, { "name", f.Name }, { "section", f.Section }, { "order", f.Order }, { "version", f.Version }, { "alpha", f.Alpha },
                { "summary", "A fixture made by the tests." }, { "applies", "For: Original Menu." }, { "warnings", f.Alpha ? new List<object> { "May stop working after a game is closed." } : new List<object>() },
                { "author", new JObj { { "name", "Fixture Author" }, { "credits", new List<object> { "Fixture Upstream" } }, { "project_url", "https://mods.example.org/" + f.Id }, { "support_url", "https://mods.example.org/" + f.Id + "/support" } } },
                { "license", new JObj { { "name", "GPL-3.0-or-later" }, { "url", "https://mods.example.org/" + f.Id + "/LICENSE" }, { "source_url", "https://mods.example.org/" + f.Id + "/source" } } },
                { "variants", variants },
                { "compatible", new JObj { { "consoles", new List<object> { "nes-classic", "snes-classic" } }, { "regions", new List<object>() }, { "hakchi", CommunityCatalog.RequiredHakchi }, { "menus", new List<object> { "original-menu" } } } },
                { "depends", f.Depends.Cast<object>().ToList() }, { "conflicts", f.Conflicts.Select(c => (object)new JObj { { "id", c.id }, { "text", c.text } }).ToList() },
                { "removes_on_install", f.Removes.Select(r => (object)new JObj { { "package", r.pkg }, { "name", r.name } }).ToList() },
                { "external_storage", new JObj { { "writes", f.External }, { "locations", f.External ? new List<object> { "/media/hakchi/" + f.Package } : new List<object>() }, { "text", f.External ? "Keeps its collection on the USB drive." : null } } },
                { "writes", new JObj { { "payload", new List<object> { "bin/" + f.Package, "etc/init.d/S81" + f.Package, "etc/" + f.Package + "/" } }, { "scripts", new List<object>() } } },
                { "target", "original-menu" }, { "launch", new JObj { { "kind", f.Launch }, { "return", f.Return }, { "program", f.Program } } }, { "companion", null },
                { "update", new JObj { { "method", f.UpdateMethod }, { "from", f.From.Select(x => (object)new JObj { { "version", x.v }, { "sha256", x.sha } }).ToList() } } },
                { "removal", new JObj { { "settings", new List<object> { new JObj { { "path", "hakchi/rootfs/etc/" + f.Package + "/settings.cfg" }, { "kind", "settings" } }, new JObj { { "path", "hakchi/rootfs/etc/" + f.Package + "/favorites.txt" }, { "kind", "favorites" } } } }, { "text", "Its own removal deletes its settings." } } },
            };
        }
        static byte[] CatalogBytes(int generation, IEnumerable<JObj> records) => Json.Bytes(new JObj { { "schema", CommunityCatalog.Schema }, { "generation", generation }, { "issued", "2026-11-20" }, { "sections", new List<object> { "menus", "utilities", "themes", "emulators" } }, { "entries", records.Cast<object>().ToList() } });

        /// <summary>the fixture set: the reference set's shape (names only; synthetic packages and authors), plus a deeper chain and a mod
        /// that writes external storage</summary>
        static List<ModFx> FixtureSet()
        {
            const string RaText = "These two RetroArch packages use the same program files. Choose one for this system.";
            var opt = new ModFx { Id = "options", Name = "Options Menu", Section = "utilities", Order = 10, Package = "fx_options", Hmod = FixtureHmod("fx_options", "2.1.1") };
            var compat = FixtureHmod("fx_options_compat", "2.1.1");
            opt.Compat = new JObj { { "variant", "compat" }, { "package", "fx_options_compat" }, { "url", "https://mods.example.org/options/fx_options_compat.hmod" }, { "file_name", "fx_options_compat.hmod" }, { "bytes", (long)compat.Length }, { "sha256", ShaOf(compat) }, { "unpacked_bytes", 100000L },
                { "use_when", new List<object> { "retroarch-k" } }, { "reason", "Uses its own copy of a system library, because the chosen RetroArch build replaces it." } };
            return new List<ModFx>
            {
                new ModFx { Id = "wifi", Name = "Wi-Fi", Section = "utilities", Order = 30, Package = "fx_wifi", Hmod = FixtureHmod("fx_wifi", "10-17-18") },
                new ModFx { Id = "remapper", Name = "Gamepad Remapping Tool", Section = "utilities", Order = 20, Package = "fx_remap", Hmod = FixtureHmod("fx_remap", "2.0.0"), Depends = new[] { "options" } },
                opt,
                new ModFx { Id = "themes", Name = "Theme Selector", Section = "themes", Order = 10, Package = "fx_themes", Hmod = FixtureHmod("fx_themes", "3.0.0"), Depends = new[] { "options", "font-remount" }, Removes = new[] { ("fx_randomizer", "Theme Randomizer"), ("fx_music", "Advanced Music Hack") } },
                new ModFx { Id = "retroarch-k", Name = "RetroArch, Build K", Section = "emulators", Order = 10, Package = "fx_ra_k", Hmod = FixtureHmod("fx_ra_k", "1.8.4"), Conflicts = new[] { ("retroarch-d", RaText) }, Alpha = true },
                new ModFx { Id = "retroarch-d", Name = "RetroArch, Build D", Section = "emulators", Order = 20, Package = "fx_ra_d", Hmod = FixtureHmod("fx_ra_d", "1.22.2"), Conflicts = new[] { ("retroarch-k", RaText) } },
                new ModFx { Id = "menu-x", Name = "Fixture Menu", Section = "menus", Order = 10, Package = "fx_menu", Hmod = FixtureHmod("fx_menu", "1.0.0"), Launch = "launcher", Program = "bin/fx_menu", Return = "Choose Exit in its menu to return to the startup chooser." },
                new ModFx { Id = "ext", Name = "Fixture USB Mod", Section = "utilities", Order = 40, Package = "fx_ext", Hmod = FixtureHmod("fx_ext", "1.0.0"), External = true },
                new ModFx { Id = "chain-a", Name = "Chain A", Section = "utilities", Order = 50, Package = "fx_chain_a", Hmod = FixtureHmod("fx_chain_a", "1.0.0") },
                new ModFx { Id = "chain-b", Name = "Chain B", Section = "utilities", Order = 60, Package = "fx_chain_b", Hmod = FixtureHmod("fx_chain_b", "1.0.0"), Depends = new[] { "chain-a" } },
                new ModFx { Id = "chain-c", Name = "Chain C", Section = "utilities", Order = 70, Package = "fx_chain_c", Hmod = FixtureHmod("fx_chain_c", "1.0.0"), Depends = new[] { "chain-b" } },
            };
        }
        /// <summary>the fixture set's package files written to a folder (the approved files "already on the PC"): id -> file</summary>
        static Dictionary<string, string> FixtureFiles(string dir, List<ModFx> set)
        {
            var d = new Dictionary<string, string>();
            foreach (var f in set) { var p = Path.Combine(dir, f.Package + ".hmod"); File.WriteAllBytes(p, f.Hmod); d[f.Id] = p; }
            return d;
        }
        static Dictionary<string, string> Pick(Dictionary<string, string> all, params string[] ids) => ids.ToDictionary(i => i, i => all[i]);
        static CommunityChange Change(Dictionary<string, string> files, string[] install = null, string[] remove = null, string[] update = null, string[] restore = null, bool removeSettings = false)
        {
            var c = new CommunityChange { Install = (install ?? new string[0]).ToList(), Remove = (remove ?? new string[0]).ToList(), Update = (update ?? new string[0]).ToList(), RestoreSettings = (restore ?? new string[0]).ToList(), RemoveSettings = removeSettings };
            foreach (var kv in files) c.Files[kv.Key] = kv.Value;
            return c;
        }
        static RecoveryConsole OpenComm(string c) => RecoverySessions.OpenSimulated(c, ImageKind.Community);
        static string Rootfs(string c, string p) => Path.Combine(c, "hakchi-rootfs", p.Replace('/', Path.DirectorySeparatorChar));
        static string Kept(string c, string pkg) => Path.Combine(c, "ccs-data", "community", "kept", pkg + ".list");
        /// <summary>every simulated console here has the same hardware id, so each part starts with no ledger from an earlier part's console</summary>
        static void ClearLedgers(Settings s) { if (Directory.Exists(s.RecordsFolder)) foreach (var f in Directory.GetFiles(s.RecordsFolder, "community-ledger-*.json")) File.Delete(f); }

        public static void Community()
        {
            var dir = NewDir("community");
            var set = FixtureSet();
            var cat = CommunityCatalog.Parse(CatalogBytes(5, set.Select(RecordJson)));
            var files = FixtureFiles(dir, set);

            Part("the catalog this build carries", () =>
            {
                var (ship, why) = CommunityCatalog.Shipping();
                Check(ship != null && why == null && ship.Empty && ship.Generation >= 1 && CommunityCatalog.SectionOrder.SequenceEqual(new[] { "menus", "utilities", "themes", "emulators" }),
                      "the catalog carried in this build reads and lists no mod: no author has agreed to be listed, so nothing is offered (" + (why ?? "generation " + ship?.Generation) + ")");
                var src = Path.Combine(Path.GetDirectoryName(typeof(Tests).Assembly.Location), "..", "..", "..", "..", "..", "..", "tools", "community", "ccs-community-catalog.json");
                Check(!File.Exists(src) || File.ReadAllBytes(src).SequenceEqual(ship.Bytes), "the catalog in this build is byte for byte the repository's tools/community/ccs-community-catalog.json (the website reads the same file)");
                Check(CommunityCatalogTrust.RemoteUrl == null && CommunityCatalogTrust.PinnedKey == null && CommunityCatalogTrust.VerifyUpdate(ship.Bytes, new byte[64], ship).problem == CommunityCatalogTrust.NoKeyText,
                      "no catalog address and no signing key are set in this version: no fetched catalog is ever accepted (" + CommunityCatalogTrust.NoKeyText + ")");
            });

            Part("records: every field checked, sections in the catalog's own order", () =>
            {
                Check(cat.Entries.Count == set.Count && cat.Find("options").Variants.Count == 2 && cat.Find("options").DefaultVariant.Package == "fx_options" && cat.Find("themes").RemovesOnInstall.Count == 2,
                      "the fixture catalog reads: " + cat.Entries.Count + " entries, the Options Menu entry with its standard and compat variants");
                Check(cat.Section("utilities").Select(e => e.Name).SequenceEqual(new[] { "Options Menu", "Gamepad Remapping Tool", "Wi-Fi", "Fixture USB Mod", "Chain A", "Chain B", "Chain C" })
                      && cat.Section("menus").Single().Id == "menu-x" && cat.Section("emulators").Select(e => e.Id).SequenceEqual(new[] { "retroarch-k", "retroarch-d" }),
                      "each section lists its entries in the catalog's own order, never alphabetically (Utilities: " + string.Join(", ", cat.Section("utilities").Select(e => e.Name)) + ")");
                string Refused(Action<JObj> change)
                {
                    var r = RecordJson(set[0]); change(r);
                    try { CommunityCatalog.Parse(CatalogBytes(1, new[] { r })); return null; } catch (CommunityCatalogException ex) { return ex.Message; }
                }
                var cases = new (string what, Action<JObj> change, string expect)[]
                {
                    ("an unknown field", r => r.Set("install_command", "sh -c x"), "unknown field"),
                    ("the author's permission", r => r.Set("permission", "granted"), "stay private"),
                    ("device-check results", r => r.Set("tested_on", "NES"), "stay private"),
                    ("an id with capitals", r => r.Set("id", "Wifi"), "stable id"),
                    ("a section outside the four", r => r.Set("section", "games"), "section"),
                    ("an address that is not https", r => ((JObj)r["author"]).Set("project_url", "http://mods.example.org/x"), "https"),
                    ("an address with a user name", r => ((JObj)r["license"]).Set("url", "https://user:pw@mods.example.org/x"), "https"),
                    ("a SHA-256 that is not 64 hex digits", r => ((JObj)((List<object>)r["variants"])[0]).Set("sha256", "ABC"), "SHA-256"),
                    ("a release address not ending in its file", r => ((JObj)((List<object>)r["variants"])[0]).Set("url", "https://mods.example.org/x/other.hmod"), "file's name"),
                    ("Classic Cart Studio's own package name", r => { var v = (JObj)((List<object>)r["variants"])[0]; v.Set("package", "ccs-mod"); v.Set("file_name", "ccs-mod.hmod"); v.Set("url", "https://mods.example.org/x/ccs-mod.hmod"); }, "cannot be used"),
                    ("no default variant", r => ((JObj)((List<object>)r["variants"])[0]).Set("use_when", new List<object> { "x" }), "variant"),
                    ("another Hakchi build", r => ((JObj)r["compatible"]).Set("hakchi", "v1.0.3-100"), "3.9.3"),
                    ("a write place with ..", r => ((JObj)r["writes"]).Set("payload", new List<object> { "etc/../../x" }), "relative path"),
                    ("a setting outside the data places", r => ((JObj)r["removal"]).Set("settings", new List<object> { new JObj { { "path", "ccs/data/x" }, { "kind", "settings" } } }), "hakchi/rootfs"),
                    ("external storage without saying where", r => ((JObj)r["external_storage"]).Set("writes", true), "does not say where"),
                    ("a control character in text", r => r.Set("summary", "line" + (char)1 + "two"), "control characters"),
                    ("an unknown update method", r => ((JObj)r["update"]).Set("method", "script"), "update method"),
                    ("a menu that starts by itself at power-on", r => r.Set("section", "menus"), "never by itself at power-on"),
                    ("a launcher without its program", r => ((JObj)r["launch"]).Set("kind", "launcher"), "does not name its program"),
                    ("a program that is not among its places", r => { var l = (JObj)r["launch"]; l.Set("kind", "launcher"); l.Set("program", "bin/other"); }, "not among the places"),
                    ("a program path with ..", r => { var l = (JObj)r["launch"]; l.Set("kind", "launcher"); l.Set("program", "bin/../x"); }, "plain relative path"),
                    ("a program for a service", r => ((JObj)r["launch"]).Set("program", "bin/fx_wifi"), "only a launcher"),
                    ("a USB place outside /media", r => { var e = (JObj)r["external_storage"]; e.Set("writes", true); e.Set("locations", new List<object> { "/var/lib/x" }); e.Set("text", "x"); }, "under /media/"),
                    ("USB places without writing there", r => ((JObj)r["external_storage"]).Set("locations", new List<object> { "/media/hakchi/x" }), "says it does not write there"),
                };
                foreach (var (what, change, expect) in cases) { var m = Refused(change); Check(m != null && m.Contains(expect), "a record with " + what + " refuses the whole catalog (" + (m ?? "accepted") + ")"); }
                string Whole(byte[] b) { try { CommunityCatalog.Parse(b); return null; } catch (CommunityCatalogException ex) { return ex.Message; } }
                var r0 = RecordJson(set[0]); var r1 = RecordJson(set[1]); r1.Set("order", r0["order"]); r1.Set("section", r0["section"]);
                Check((Whole(CatalogBytes(1, new[] { RecordJson(set[0]), RecordJson(set[0]) })) ?? "").Contains("two entries have the id") && (Whole(CatalogBytes(1, new[] { r0, r1 })) ?? "").Contains("share the place")
                      && (Whole(Encoding.UTF8.GetBytes("{\"schema\":\"ccs-community-catalog/1\",\"generation\":1,\"issued\":\"2026-11-20\",\"sections\":[\"themes\",\"menus\",\"utilities\",\"emulators\"],\"entries\":[]}")) ?? "").Contains("in that order")
                      && Whole(Encoding.UTF8.GetBytes("not json")) != null,
                      "two entries with one id or one place, the sections in another order, or text that is not JSON: refused");
            });

            Part("a fetched catalog: signature, generation and unchanged pins", () =>
            {
                using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
                using (var other = ECDsa.Create(ECCurve.NamedCurves.nistP256))
                {
                    var pub = CommunityCatalogTrust.KeyText(key.ExportParameters(false));
                    var next = CatalogBytes(6, set.Select(RecordJson)); var sig = key.SignData(next, HashAlgorithmName.SHA256);
                    var (good, gp) = CommunityCatalogTrust.VerifyUpdate(next, sig, cat, pub);
                    var (b64, bp) = CommunityCatalogTrust.VerifyUpdate(next, Encoding.ASCII.GetBytes(Convert.ToBase64String(sig) + "\n"), cat, pub);
                    Check(good != null && gp == null && good.Generation == 6 && b64 != null, "a newer catalog signed with the pinned key is accepted (raw or base64 signature)");
                    var tampered = (byte[])next.Clone(); tampered[tampered.Length / 2] ^= 1;
                    var (t1, tp) = CommunityCatalogTrust.VerifyUpdate(tampered, sig, cat, pub);
                    var (t2, op) = CommunityCatalogTrust.VerifyUpdate(next, other.SignData(next, HashAlgorithmName.SHA256), cat, pub);
                    var (t3, sp) = CommunityCatalogTrust.VerifyUpdate(next, new byte[10], cat, pub);
                    Check(t1 == null && tp.Contains("does not match") && t2 == null && op.Contains("does not match") && t3 == null && sp.Contains("not a P-256 signature"),
                          "a changed byte, another key's signature or a malformed signature: refused, the catalog not used (" + tp + ")");
                    var old = CatalogBytes(5, set.Select(RecordJson)); var (t4, gen) = CommunityCatalogTrust.VerifyUpdate(old, key.SignData(old, HashAlgorithmName.SHA256), cat, pub);
                    var moved = set.Select(RecordJson).ToList(); ((JObj)((List<object>)moved[0]["variants"])[0]).Set("sha256", new string('9', 64));
                    var swap = CatalogBytes(7, moved); var (t5, pin) = CommunityCatalogTrust.VerifyUpdate(swap, key.SignData(swap, HashAlgorithmName.SHA256), cat, pub);
                    var fut = Encoding.UTF8.GetString(CatalogBytes(8, set.Select(RecordJson))).Replace("2026-11-20", DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd")); var fb = Encoding.UTF8.GetBytes(fut);
                    var (t6, fp) = CommunityCatalogTrust.VerifyUpdate(fb, key.SignData(fb, HashAlgorithmName.SHA256), cat, pub);
                    Check(t4 == null && gen.Contains("not newer") && t5 == null && pin.Contains("never accepted for the same version") && t6 == null && fp.Contains("future"),
                          "an older or equal generation, a different file for an approved version (never a new checksum by itself), or a date in the future: refused (" + pin + ")");
                }
            });

            Part("archives: names, links, kinds and sizes", () =>
            {
                var rec = cat.Find("options");
                string Problem(byte[] gz, CommunityRecord r = null, long max = CommunityArchive.MaxUnpacked) => CommunityArchive.Inspect(new MemoryStream(gz), r, max).Problem;
                var ok = CommunityArchive.Inspect(new MemoryStream(set.First(f => f.Id == "options").Hmod), rec);
                Check(ok.Ok && ok.Scripts.SequenceEqual(new[] { "install", "uninstall" }) && ok.Payload.Count(m => m.Type == "file") == 3 && ok.Members.All(m => !m.Path.StartsWith(".")),
                      "a fixture package passes: its scripts named for the review, its three installed files inside the record's places (" + ok.Problem + ")");
                var bad = new (string what, byte[] gz, string expect)[]
                {
                    ("an absolute path", Hmod(TF("/etc/passwd", "x")), "absolute path"),
                    ("a .. part", Hmod(TF("etc/../../x", "x")), "\"..\" name part"),
                    ("a back slash", Hmod(TF("etc\\x", "x")), "absolute path"),
                    ("a drive letter", Hmod(TF("c:x", "x")), "absolute path"),
                    ("a Windows device name", Hmod(TF("etc/fx_options/CON.cfg", "x")), "Windows device name"),
                    ("a device name with digits", Hmod(TF("etc/fx_options/com1", "x")), "Windows device name"),
                    ("a character Windows cannot hold", Hmod(TF("etc/fx_options/a?b", "x")), "Windows cannot hold"),
                    ("a name ending in a dot", Hmod(TF("etc/fx_options/x.", "x")), "ending in a dot"),
                    ("two names differing only in case", Hmod(TF("etc/fx_options/A.cfg", "1"), TF("etc/fx_options/a.cfg", "2")), "upper and lower case"),
                    ("a name twice", Hmod(TF("etc/fx_options/a.cfg", "1"), TF("etc/fx_options/a.cfg", "2")), "appears twice"),
                    ("an absolute link", Hmod(TL("etc/fx_options/l", "/etc/shadow")), "outside the package"),
                    ("a link out of the package", Hmod(TD("etc"), TD("etc/fx_options"), TL("etc/fx_options/l", "../../../x")), "outside the package"),
                    ("a file written through a link", Hmod(TL("etc/fx_options/l", "sub"), TF("etc/fx_options/l/x", "x")), "through a link"),
                    ("a hard link to something not in the package", Hmod(TH("etc/fx_options/h", "etc/passwd")), "hard link"),
                    ("a device member", Hmod(new TarEnt { Name = "etc/fx_options/dev", Type = '3' }), "not a file, folder or link"),
                    ("a FIFO member", Hmod(new TarEnt { Name = "etc/fx_options/fifo", Type = '6' }), "not a file, folder or link"),
                    ("a package inside the package", Hmod(TF("etc/fx_options/inner.hmod", "x")), "another package"),
                    ("a game file", Hmod(TF("etc/fx_options/game.nes", "NES" + (char)26)), "holds a game"),
                    ("a console system file", Hmod(TF("etc/fx_options/disksys.rom", "x")), "system file"),
                    ("a damaged header", Hmod(new TarEnt { Name = "etc/fx_options/x", Data = new byte[1], BadSum = true }), "checksum"),
                    ("not gzip", Tar(TF("etc/fx_options/x", "x")), "gzip"),
                    ("a cut-off archive", CGz(Tar(TF("etc/fx_options/x", new string('a', 2000))).Take(1100).ToArray()), "ends"),
                };
                foreach (var (what, gz, expect) in bad) { var p = Problem(gz); Check(p != null && p.Contains(expect), "a package with " + what + " is refused (" + (p ?? "accepted") + ")"); }
                var outside = Problem(Hmod(TF("etc/shadow", "x")), rec); var big = Problem(Hmod(TF("etc/fx_options/x", new string('a', 5000))), rec, 1000);
                var inside = CommunityArchive.Inspect(new MemoryStream(Hmod(TD("etc"), TD("etc/fx_options"), TF("etc/fx_options/a.sh", "x"), TL("etc/fx_options/b", "a.sh"), TH("etc/fx_options/c", "etc/fx_options/a.sh"))), rec);
                var pax = Problem(Hmod(new TarEnt { Name = "etc/fx_options/ok", Data = new byte[1], PaxPath = "../../evil" }));
                var longName = CommunityArchive.Inspect(new MemoryStream(Hmod(new TarEnt { Name = "etc/fx_options/" + new string('n', 120) + ".cfg", Data = new byte[3], Long = true })), rec);
                Check(outside != null && outside.Contains("outside the places") && big != null && big.Contains("more than") && inside.Ok && pax != null && pax.Contains("..") && longName.Ok && longName.Members.Last().Path.Length > 100,
                      "a file outside the record's places or past its unpacked size is refused; a relative link and a hard link inside the package pass; a long name is read whole, and a name changed by an extended header is checked too ("
                      + outside + "; " + big + "; " + pax + ")");
                var many = Enumerable.Range(0, CommunityArchive.MaxMembers + 1).Select(i => new TarEnt { Name = "etc/fx_options/f" + i, Type = '5' }).ToArray();
                Check((Problem(Hmod(many)) ?? "").Contains("more than"), "more than 20,000 members: refused");
                var noun = Hmod(TF("uninstall", "no-uninstall\n"), TF("etc/fx_options/a", "x"));
                Check(CommunityArchive.NoUninstall(new MemoryStream(noun)) && !CommunityArchive.NoUninstall(new MemoryStream(set[0].Hmod)), "a package whose uninstall says hakchi may never remove it is recognized");
            });

            Part("admission by exact pin; downloads only into a chosen folder", () =>
            {
                var r = cat.Find("wifi"); var v = r.DefaultVariant;
                var (a, why) = CommunityAdmission.Admit(r, v, files["wifi"]);
                var wrongSize = Path.Combine(dir, "wifi-short.hmod"); File.WriteAllBytes(wrongSize, set.First(f => f.Id == "wifi").Hmod.Take(100).ToArray());
                var wrongSha = Path.Combine(dir, "wifi-other.hmod"); var b = (byte[])set.First(f => f.Id == "wifi").Hmod.Clone(); b[b.Length - 1] ^= 1; File.WriteAllBytes(wrongSha, b);
                var (s1, w1) = CommunityAdmission.Admit(r, v, wrongSize); var (s2, w2) = CommunityAdmission.Admit(r, v, wrongSha); var (s3, w3) = CommunityAdmission.Admit(r, v, null);
                Check(a != null && why == null && a.Inventory.Ok && s1 == null && w1.Contains("bytes") && s2 == null && w2.Contains("fingerprint") && w2.Contains("never accepted") && s3 == null && w3 == CommunityAdmission.NoFileText,
                      "the approved file is admitted; another size or another SHA-256 is refused and never accepted in its place; no file: \"" + w3 + "\"");
                var copy = Path.Combine(dir, "wifi-copy.hmod"); File.Copy(files["wifi"], copy); var (ac, _) = CommunityAdmission.Admit(r, v, copy);
                File.WriteAllBytes(copy, b);
                Check(ac != null && CommunityAdmission.Recheck(ac) != null, "the same file changed after it was admitted: refused when checked again at the write (" + CommunityAdmission.Recheck(ac) + ")");
                // downloads: from a stand-in for the author's address (no network), only into a chosen folder
                var good = set.First(f => f.Id == "wifi").Hmod; byte[] serve = good; var asked = new List<string>();
                var keep = CommunityDownload.Open;
                CommunityDownload.Open = url => { asked.Add(url); return new MemoryStream(serve); };
                try
                {
                    var into = NewDir("community-downloads");
                    var (f1, p1) = CommunityDownload.Fetch(r, v, into);
                    var (f0, p0) = CommunityDownload.Fetch(r, v, "relative-folder");
                    var into2 = NewDir("community-downloads2"); serve = b; var (f2, p2) = CommunityDownload.Fetch(r, v, into2);
                    var into3 = NewDir("community-downloads3"); serve = good.Concat(new byte[] { 1 }).ToArray(); var (f3, p3) = CommunityDownload.Fetch(r, v, into3);
                    var into4 = NewDir("community-downloads4"); File.WriteAllBytes(Path.Combine(into4, v.FileName), b); serve = good; var (f4, p4) = CommunityDownload.Fetch(r, v, into4);
                    Check(f1 != null && Sha(f1) == v.Sha256 && asked.First() == v.Url && f0 == null && p0.Contains("Choose a folder"),
                          "a download goes only from the record's own address into the folder the person chose, and is kept under the release file's name only once it matches; no chosen folder: refused (" + p0 + ")");
                    Check(f2 == null && p2.Contains("deleted") && Directory.GetFiles(into2).Length == 0 && f3 == null && Directory.GetFiles(into3).Length == 0 && f4 == null && p4.Contains("different file"),
                          "different bytes or more bytes than the approved file: the partial download deleted, nothing kept, no new checksum accepted; a different file already in the folder is never replaced (" + p2 + ")");
                }
                finally { CommunityDownload.Open = keep; }
                var shot = new CommunityShot { Bytes = 3, Sha256 = Sha(new byte[] { 1, 2, 3 }) };
                Check(CommunityAsset.Check(new byte[] { 1, 2, 3 }, shot) == null && CommunityAsset.Check(new byte[] { 1, 2, 4 }, shot) != null, "a permitted picture is used only with its approved bytes");
            });

            Part("one plan: dependencies, variants, conflicts, removals and order", () =>
            {
                var none = new List<string>();
                CommunityResolution R(IEnumerable<string> pkgs, string[] install = null, string[] remove = null, IEnumerable<CommunityInstalled> ledger = null, string[] update = null) =>
                    CommunityResolver.Resolve(cat, ledger, pkgs.ToList(), Change(new Dictionary<string, string>(), install, remove, update));
                var rm = R(none, new[] { "remapper" });
                Check(rm.Ok && rm.Feedback.Contains("Gamepad Remapping Tool needs Options Menu, so Options Menu has been selected too.") && rm.Items.Select(i => i.Record.Id).SequenceEqual(new[] { "remapper", "options" })
                      && rm.Items.First(i => i.Record.Id == "remapper").Tier == 1 && rm.Items.First(i => i.Record.Id == "options").Tier == 0 && rm.Placed.First().Record.Id == "options",
                      "the remapper selects Options Menu and says why; Options Menu waits in the transfer folder, the remapper in its hmod/ folder (installed after it)");
                var rm2 = R(new[] { "fx_options" }, new[] { "remapper" });
                Check(rm2.Ok && rm2.Feedback.Count == 0 && rm2.Items.Single().Tier == 0, "with Options Menu already installed, the remapper alone, in the transfer folder");
                var th = R(none, new[] { "themes" });
                Check(!th.Ok && th.Refusal.Contains("Theme Selector needs font-remount") && th.Refusal.Contains("cannot install") && !th.Items.Any(i => i.Record.Id == "wifi"),
                      "Theme Selector needs Options Menu and Font Remount; Font Remount is not listed, so Theme Selector cannot be installed (never Wi-Fi in its place) (" + th.Refusal + ")");
                var ra = R(new[] { "fx_ra_k" }, new[] { "retroarch-d" }); var ra2 = R(none, new[] { "retroarch-k", "retroarch-d" });
                Check(!ra.Ok && ra.Refusal.StartsWith("These two RetroArch packages use the same program files. Choose one for this system.") && !ra2.Ok && ra2.Refusal.StartsWith("These two RetroArch packages use the same program files."),
                      "the two RetroArch builds conflict, installed or chosen together: \"" + ra.Refusal + "\"");
                var swap = R(new[] { "fx_ra_k" }, new[] { "retroarch-d" }, new[] { "retroarch-k" });
                Check(swap.Ok && swap.Unlist.SequenceEqual(new[] { "fx_ra_k" }) && swap.Placed.Single().Record.Id == "retroarch-d", "replacing one RetroArch with the other in one reviewed change: the installed one removed, the other placed");
                var withK = R(none, new[] { "options", "retroarch-k" });
                Check(withK.Ok && withK.Items.First(i => i.Record.Id == "options").Variant.Variant == "compat" && withK.Feedback.Any(f => f.StartsWith("Options Menu: Uses its own copy")),
                      "Options Menu chosen beside the RetroArch build that replaces a library: its compat variant, with the reason (" + string.Join(" | ", withK.Feedback) + ")");
                var sw = R(new[] { "fx_options" }, new[] { "retroarch-k" });
                var swi = sw.Items.FirstOrDefault(i => i.Op == "switch");
                Check(sw.Ok && swi != null && swi.OldPackage == "fx_options" && swi.Variant.Package == "fx_options_compat" && swi.KeepSettings && sw.Unlist.SequenceEqual(new[] { "fx_options" }),
                      "the standard Options Menu installed and that RetroArch added: Options Menu changes to its compat version in the same plan, its settings kept (" + swi?.Reason + ")");
                var keepBase = R(new[] { "fx_options", "fx_remap" }, null, new[] { "options" }); var both = R(new[] { "fx_options", "fx_remap" }, null, new[] { "options", "remapper" });
                Check(!keepBase.Ok && keepBase.Refusal == "Gamepad Remapping Tool needs Options Menu. Remove Gamepad Remapping Tool too, or keep Options Menu." && both.Ok && both.Unlist.SequenceEqual(new[] { "fx_remap", "fx_options" }),
                      "Options Menu cannot go while the remapper stays; removing both lists the remapper first (" + keepBase.Refusal + ")");
                var chain = R(none, new[] { "chain-c" });
                Check(!chain.Ok && chain.Refusal.Contains("start the console once"), "a dependency chain deeper than the two places hakchi installs from: refused with the order to follow (" + chain.Refusal + ")");
                var ups = R(none, new[] { "wifi" });
                Check(ups.Ok && ups.UpstreamRemovals.Count == 0, "a mod whose own install removes nothing lists no removals");
                var thr = CommunityResolver.Resolve(CommunityCatalog.Parse(CatalogBytes(5, set.Where(f => f.Id != "themes").Select(RecordJson).Concat(new[] { Fn(() => { var t = RecordJson(set.First(f => f.Id == "themes")); t.Set("depends", new List<object> { "options" }); return t; }) }))),
                    null, new List<string> { "fx_options", "fx_randomizer" }, Change(new Dictionary<string, string>(), new[] { "themes" }));
                Check(thr.Ok && thr.UpstreamRemovals.Single().StartsWith("Installing Theme Selector removes Theme Randomizer and Advanced Music Hack if they are installed (on this console now: Theme Randomizer)"),
                      "the packages an author's own install removes are listed before confirmation, naming those on the console now: " + thr.UpstreamRemovals.FirstOrDefault());
                var delisted = new CommunityInstalled { Id = "gone", Name = "Delisted Mod", Package = "fx_gone", Version = "1.0.0", Record = Json.Parse(Json.Write(Fn(() => { var g = RecordJson(new ModFx { Id = "gone", Name = "Delisted Mod", Section = "utilities", Order = 99, Package = "fx_gone", Hmod = FixtureHmod("fx_gone", "1") }); return g; }))) };
                var dIn = R(none, new[] { "gone" }, null, new[] { delisted }); var dOut = R(new[] { "fx_gone" }, null, new[] { "gone" }, new[] { delisted });
                Check(!dIn.Ok && dIn.Refusal.Contains("no longer listed") && dOut.Ok && dOut.Unlist.SequenceEqual(new[] { "fx_gone" }),
                      "a delisted mod cannot be installed again, but a copy Workbench installed stays manageable and can be removed (" + dIn.Refusal + ")");
                var ledgerWifi = new CommunityInstalled { Id = "wifi", Name = "Wi-Fi", Package = "fx_wifi", Sha256 = new string('1', 64) };
                var noUp = R(new[] { "fx_wifi" }, null, null, new[] { ledgerWifi }, new[] { "wifi" });
                Check(!noUp.Ok && noUp.Refusal.Contains("No approved update") && !CommunityResolver.UpdateAvailable(cat, ledgerWifi), "an update only to a newly approved pin that names the installed one as its predecessor; otherwise none is offered (" + noUp.Refusal + ")");
                var ext = R(none, new[] { "ext" });
                Check(ext.Ok && ext.External.SequenceEqual(new[] { "/media/hakchi/fx_ext" }), "a mod that writes external storage is marked for the external backup it needs");
            });

            // ---- on simulated consoles: plans, confirmation, the write boundary's checks, the journal, removal and rollback
            var s = SettingsIn(NewDir("community-run"), false); var ctx = Ctx(s, ImageKind.Community);
            var catFile = Path.Combine(dir, "fixture-catalog.json"); File.WriteAllBytes(catFile, cat.Bytes);
            Part("install: plan, confirmation, rechecks, next start and reconciliation", () =>
            {
                var c = SmallConsole("modified", true); ClearLedgers(s);
                var f0 = BackupFiles(s);
                var (n1, r1) = CommunityEngine.Prepare(OpenComm(c), ctx, cat, Change(Pick(files, "wifi")));
                var (n2, r2) = CommunityEngine.Prepare(OpenComm(c), ctx, cat, Change(new Dictionary<string, string>(), new[] { "wifi" }));
                var (n3, r3) = CommunityEngine.Prepare(OpenComm(c), ctx, cat, Change(Pick(files, "ext"), new[] { "ext" }));
                Check(n1 == null && r1.Problem.Contains("Choose the mods") && n2 == null && r2.Problem.Contains(CommunityAdmission.NoFileText) && n3 == null && r3.Problem.Contains("Choose a USB and SD backup of that storage") && BackupFiles(s) == f0 && Writes(c).Count == 0,
                      "nothing chosen, no file for the mod, or a mod that writes a USB drive or SD card without a USB and SD backup chosen: refused before any backup or write (" + r2.Problem + "; " + r3.Problem + ")");
                var con = OpenComm(c);
                var (p, r) = CommunityEngine.Prepare(con, ctx, cat, Change(Pick(files, "remapper", "options"), new[] { "remapper" }));
                var pj = p == null ? null : Json.Parse(Json.Write(p.ToJson()));
                Check(r == null && p.Steps.SequenceEqual(new[] { "cput:fx_options", "cput:fx_remap" }) && p.Packages.Count == 2 && Verifier.VerifyFile(p.ProtectivePath).Ok && p.ProtectiveArchive.Session == con.SessionId
                      && p.Notes.Contains("Gamepad Remapping Tool needs Options Menu, so Options Menu has been selected too.") && p.Notes.Contains(CommunityEngine.PrivilegeNote) && pj.S("operation") == "community" && Writes(c).Count == 0,
                      "install the remapper: Options Menu selected with its reason; both packages planned after a complete backup made in this session; the review says what the pin does and does not prove (" + Flat(r?.Problem ?? p.Text) + ")");
                var nonce = p.Nonce;
                Check(CommunityEngine.Execute(con, p, Other(p.Digest), nonce, ctx).State == "refused" && CommunityEngine.Execute(con, p, p.Digest, Other(nonce), ctx).State == "refused"
                      && CommunityEngine.Execute(OpenComm(c), p, p.Digest, nonce, ctx).State == "refused" && Writes(c).Count == 0 && p.Nonce == nonce,
                      "another plan's confirmation, another one-time code, or a new connection after the review: refused, nothing written, the confirmation not used");
                var exp = p.ExpiresUtc; var dg = p.Digest; var digestOf = typeof(CommunityEngine).GetMethod("DigestOf", BindingFlags.NonPublic | BindingFlags.Static);
                p.ExpiresUtc = DateTime.UtcNow.AddMinutes(-1); p.Digest = (string)digestOf.Invoke(null, new object[] { p });
                var me = CommunityEngine.Execute(con, p, p.Digest, nonce, ctx); p.ExpiresUtc = exp; p.Digest = dg;
                var pf = files["options"]; var saved = File.ReadAllBytes(pf); var b = (byte[])saved.Clone(); b[b.Length - 1] ^= 1; File.WriteAllBytes(pf, b);
                var mf = CommunityEngine.Execute(con, p, p.Digest, nonce, ctx); File.WriteAllBytes(pf, saved);
                var prot = p.ProtectivePath; File.Move(prot, prot + ".aside"); var mm = CommunityEngine.Execute(con, p, p.Digest, nonce, ctx); File.Move(prot + ".aside", prot);
                SetInstalled(c, "someone-else"); var mc = CommunityEngine.Execute(con, p, p.Digest, nonce, ctx); SetInstalled(c);
                Check(me.State == "refused" && me.Problem.Contains("10 minutes") && mf.State == "refused" && mf.Problem.Contains("Options Menu") && mm.State == "refused" && mm.Problem.Contains("missing") && mc.State == "refused" && mc.Problem.Contains("changed since the review")
                      && Writes(c).Count == 0 && p.Nonce == nonce,
                      "at the write boundary: an expired confirmation, a package file changed after the review, the backup moved aside, or the console's packages changed: each refused, nothing written (" + mf.Problem + "; " + mc.Problem + ")");
                var o = CommunityEngine.Execute(con, p, p.Digest, nonce, ctx);
                var rec = Record.Load(o.RecordFile); var ledger = CommunityLedger.Load(s, con.Identify().ShortTag);
                Check(o.State == "scheduled" && Writes(c).SequenceEqual(new[] { "cput fx_options", "cput fx_remap" }) && Waiting(c).SequenceEqual(new[] { "fx_options.hmod", "hmod/fx_remap.hmod" })
                      && Sha(Path.Combine(Transfer(c), "fx_options.hmod")) == cat.Find("options").DefaultVariant.Sha256 && rec.S("state") == "scheduled" && rec.A("phases").Count == 4
                      && ledger.Count == 2 && ledger.All(m => m.State == CommunityLedger.Waiting && m.BackupPath == p.ProtectivePath && m.Payload.Count == 3),
                      "confirmed: Options Menu waits in the transfer folder and the remapper in hmod/ (the exact approved bytes); the record shows every step; the PC's ledger names both, waiting, with the backup made before (" + o.Problem + ")");
                Check(CommunityEngine.Execute(con, p, p.Digest, p.Nonce, ctx).State == "refused" && Writes(c).Count == 2, "the same confirmation cannot run twice");
                // the next start, then a new connection reconciles: hakchi's listing is not enough, the files are read back
                var st = SimulatedConsole.NormalStart(c);
                var rd = CommunityEngine.Read(OpenComm(c), ctx, cat); var l2 = rd.Ledger;
                Check(st.SequenceEqual(new[] { "install fx_options", "install fx_remap", "transfer folder removed; restart" }) && Installed(c).SequenceEqual(new[] { "fx_options", "fx_remap" }) && File.Exists(Rootfs(c, "bin/fx_remap"))
                      && l2.All(m => m.State == CommunityLedger.Installed && m.Check.StartsWith("Installed: 3 of 3 files are the approved ones")),
                      "the next normal start installs Options Menu, then the remapper; the next connection reads each one's files back and finds the approved ones (" + string.Join(" | ", l2.Select(m => m.Check)) + ")");
                File.WriteAllText(Rootfs(c, "etc/fx_remap/defaults.cfg"), "changed by the mod\n"); File.Delete(Rootfs(c, "bin/fx_remap"));
                var l3 = CommunityEngine.Read(OpenComm(c), ctx, cat).Ledger.First(m => m.Id == "remapper");
                Check(l3.State == CommunityLedger.Incomplete && l3.Check.Contains("1 changed since") && l3.Check.Contains("1 missing"), "a file changed since and a file missing are told apart, never a false installed state (" + l3.Check + ")");
                Gone(p.ProtectivePath); GoneConsole(c); GoneBackups(s);
            });

            Part("an interrupted install, and an install hakchi did not do", () =>
            {
                var c = SmallConsole("modified", true); ClearLedgers(s);
                File.WriteAllText(Path.Combine(c, "faults.json"), "{\"prep\":{\"step\":\"cput\"}}");
                var con = OpenComm(c);
                var (p, r) = CommunityEngine.Prepare(con, ctx, cat, Change(Pick(files, "wifi", "menu-x"), new[] { "wifi", "menu-x" }));
                var o = CommunityEngine.Execute(con, p, p.Digest, p.Nonce, ctx);
                Check(o.State == "stopped" && o.Done.Count > 0 && o.Done.All(d => !d.StartsWith("cput")) && o.Problem.StartsWith("The mods could not be installed. It stopped at:") && o.Problem.Contains("Nothing more was written and nothing was retried") && o.Problem.Contains("Connect again")
                      && Record.Load(o.RecordFile).S("state") == "stopped" && CommunityLedger.Load(s, con.Identify().ShortTag).Count == 0,
                      "the first package's write fails (after the startup chooser the menu needs was placed): stopped there, nothing retried, the record says so and the ledger names nothing as installed; the failure says what is true and the safe next step (" + o.Problem + ")");
                File.Delete(Path.Combine(c, "faults.json")); Gone(p.ProtectivePath);
                var con2 = OpenComm(c);
                var (p2, _) = CommunityEngine.Prepare(con2, ctx, cat, Change(Pick(files, "wifi", "menu-x"), new[] { "wifi", "menu-x" }));
                var o2 = CommunityEngine.Execute(con2, p2, p2.Digest, p2.Nonce, ctx); Gone(p2.ProtectivePath);
                Check(p2.Steps.SequenceEqual(new[] { "method:ccs-nucleus", "method:ccs-nucleus-chooser", "cput:fx_menu", "cput:fx_wifi" }) && Record.Load(o2.RecordFile).A("phases").OfType<Dictionary<string, object>>().Count(x => x.S("state") == "already-done") == 2,
                      "reviewed again, the interrupted change is finished: the start and the menu's choice already done are not planned again, the add-ons already waiting are not written twice (" + string.Join(", ", p2.Steps) + ")");
                File.WriteAllText(Path.Combine(c, "faults.json"), "{\"community_not_installed\":[\"fx_menu\"]}");
                var st = SimulatedConsole.NormalStart(c);
                var ld = CommunityEngine.Read(OpenComm(c), ctx, cat).Ledger;
                Check(o2.State == "scheduled" && st.Contains("install fx_menu not installed") && ld.First(m => m.Id == "menu-x").State == CommunityLedger.Incomplete && ld.First(m => m.Id == "menu-x").Check.Contains("it was not installed")
                      && ld.First(m => m.Id == "wifi").State == CommunityLedger.Installed,
                      "the next start leaves one package uninstalled (as hakchi's blacklist would): the next connection marks it not installed, the other installed; the start alone is never taken as proof (" + ld.First(m => m.Id == "menu-x").Check + ")");
                // a removal stops after its first step; a new connection finishes it without writing that step again
                File.WriteAllText(Rootfs(c, "etc/fx_wifi/settings.cfg"), "network=home\n");
                File.WriteAllText(Path.Combine(c, "faults.json"), "{\"prep\":{\"step\":\"cunlist\"}}");
                var con3 = OpenComm(c); var n0 = Writes(c).Count;
                var (p3, _) = CommunityEngine.Prepare(con3, ctx, cat, Change(new Dictionary<string, string>(), null, new[] { "wifi" }));
                var o3 = CommunityEngine.Execute(con3, p3, p3.Digest, p3.Nonce, ctx); Gone(p3.ProtectivePath);
                File.Delete(Path.Combine(c, "faults.json"));
                var con4 = OpenComm(c);
                var (p4, r4) = CommunityEngine.Prepare(con4, ctx, cat, Change(new Dictionary<string, string>(), null, new[] { "wifi" }));
                var o4 = p4 == null ? null : CommunityEngine.Execute(con4, p4, p4.Digest, p4.Nonce, ctx);
                var ph = o4 == null ? new List<Dictionary<string, object>>() : Record.Load(o4.RecordFile).A("phases").OfType<Dictionary<string, object>>().ToList();
                Check(o3.State == "stopped" && o3.Done.SequenceEqual(new[] { "ckeep:fx_wifi" }) && o4?.State == "scheduled" && ph.Any(x => x.S("step") == "ckeep:fx_wifi" && x.S("state") == "already-done")
                      && Writes(c).Skip(n0).SequenceEqual(new[] { "ckeep fx_wifi", "cunlist fx_wifi" }) && p4.Notes.Any(x => x.Contains("kept earlier for Wi-Fi are on the console")),
                      "a removal list's write fails after the settings were kept: stopped there; a new connection finishes it, the kept copy not written again and the removal list written once (" + o3.Problem + "; " + (o4?.Problem ?? r4?.Problem) + ")");
                Gone(p4?.ProtectivePath); GoneConsole(c); GoneBackups(s);
            });

            Part("normal removal keeps settings; removal with them; putting them back", () =>
            {
                var c = SmallConsole("modified", true); ClearLedgers(s);
                var con = OpenComm(c);
                var (p, _) = CommunityEngine.Prepare(con, ctx, cat, Change(Pick(files, "options", "wifi"), new[] { "options", "wifi" }));
                CommunityEngine.Execute(con, p, p.Digest, p.Nonce, ctx); Gone(p.ProtectivePath); SimulatedConsole.NormalStart(c);
                File.WriteAllText(Rootfs(c, "etc/fx_options/settings.cfg"), "language=en\n"); File.WriteAllText(Rootfs(c, "etc/fx_options/favorites.txt"), "game-1\n");
                var sum = Sha(Rootfs(c, "etc/fx_options/settings.cfg"));
                con = OpenComm(c); var n0 = Writes(c).Count;
                var (q, qr) = CommunityEngine.Prepare(con, ctx, cat, Change(new Dictionary<string, string>(), null, new[] { "options" }));
                var o = q == null ? null : CommunityEngine.Execute(con, q, q.Digest, q.Nonce, ctx);
                Check(qr == null && q.Steps.SequenceEqual(new[] { "ckeep:fx_options", "cunlist" }) && q.Text.Contains("Their settings and favorites are kept") && o.State == "scheduled"
                      && Writes(c).Skip(n0).SequenceEqual(new[] { "ckeep fx_options", "cunlist fx_options" }) && File.Exists(Kept(c, "fx_options")) && File.ReadAllLines(Kept(c, "fx_options")).Length == 2,
                      "normal Remove: the mod's settings and favorites copied to Classic Cart Studio's own folder first, then hakchi's removal list; read back (" + Flat(qr?.Problem ?? q.Text) + ")");
                Gone(q.ProtectivePath);
                var st = SimulatedConsole.NormalStart(c);
                var ld = CommunityEngine.Read(OpenComm(c), ctx, cat).Ledger;
                Check(st.SequenceEqual(new[] { "uninstall fx_options", "transfer folder removed; restart" }) && !File.Exists(Rootfs(c, "etc/fx_options/settings.cfg")) && !File.Exists(Rootfs(c, "bin/fx_options"))
                      && File.Exists(Kept(c, "fx_options")) && Installed(c).SequenceEqual(new[] { "fx_wifi" }) && ld.Count == 1 && ld[0].Id == "wifi",
                      "the next start runs the mod's own removal, which deletes its settings; Workbench's kept copy stays; the other mod is untouched; the ledger forgets the removed mod");
                // installed again: the kept settings put back where nothing is (never over an existing file)
                con = OpenComm(c);
                var (i2, _) = CommunityEngine.Prepare(con, ctx, cat, Change(Pick(files, "options"), new[] { "options" }));
                CommunityEngine.Execute(con, i2, i2.Digest, i2.Nonce, ctx); Gone(i2.ProtectivePath); SimulatedConsole.NormalStart(c);
                File.WriteAllText(Rootfs(c, "etc/fx_options/favorites.txt"), "made after\n");
                con = OpenComm(c);
                var (rs, rr) = CommunityEngine.Prepare(con, ctx, cat, Change(new Dictionary<string, string>(), restore: new[] { "options" }));
                var ro = rs == null ? null : CommunityEngine.Execute(con, rs, rs.Digest, rs.Nonce, ctx);
                Check(rr == null && rs.Steps.SequenceEqual(new[] { "crestore:fx_options" }) && ro.State == "scheduled" && Sha(Rootfs(c, "etc/fx_options/settings.cfg")) == sum && File.ReadAllText(Rootfs(c, "etc/fx_options/favorites.txt")) == "made after\n"
                      && File.Exists(Kept(c, "fx_options")),
                      "putting back the kept settings: the settings file returns; the favorites file made since is not replaced, so the kept copy stays (" + Flat(rr?.Problem) + "; " + ro?.Problem + ")");
                Gone(rs.ProtectivePath);
                // removing with "Also remove this mod's settings and favorites": no copy kept, the earlier copy removed
                con = OpenComm(c); var n1 = Writes(c).Count;
                var (x, xr) = CommunityEngine.Prepare(con, ctx, cat, Change(new Dictionary<string, string>(), null, new[] { "options" }, removeSettings: true));
                var xo = x == null ? null : CommunityEngine.Execute(con, x, x.Digest, x.Nonce, ctx);
                Check(xr == null && x.Steps.SequenceEqual(new[] { "cforget:fx_options", "cunlist" }) && xo.State == "scheduled" && !File.Exists(Kept(c, "fx_options")) && Writes(c).Skip(n1).SequenceEqual(new[] { "cforget fx_options", "cunlist fx_options" })
                      && x.Notes.Any(n => n.Contains("removed with it (you chose that)")),
                      "Also remove this mod's settings and favorites: nothing kept, and the copy kept earlier removed too (only Classic Cart Studio's own copy) (" + Flat(xr?.Problem ?? x.Text) + ")");
                Gone(x.ProtectivePath); GoneConsole(c); GoneBackups(s);
            });

            Part("a community menu: the startup chooser comes in the same reviewed change; the menu is a choice there; removal keeps the chooser", () =>
            {
                var cid = CommunityMenus.ChoiceId("menu-x");
                Dictionary<string, object> Opt(string cc, string oid) { var f = Path.Combine(cc, "nucleus-registry.json"); return File.Exists(f) ? Json.Parse(File.ReadAllText(f)).O("options")?.O(oid) : null; }
                void Cfg(string cc, string k, object v) { var cj = Path.Combine(cc, "console.json"); var j0 = Json.Parse(File.ReadAllText(cj)); var j = new JObj(); foreach (var kv in j0) j.Add(kv.Key, kv.Value); j.Set(k, v); File.WriteAllText(cj, Json.Write(j)); }
                void Registry(string cc, JObj options) { var f = Path.Combine(cc, "nucleus-registry.json"); if (options == null) { if (File.Exists(f)) File.Delete(f); } else File.WriteAllText(f, Json.Write(new JObj { { "options", options } })); }
                // 1. a console without CCS Nucleus: CCS Nucleus, its chooser and its start come first, then the menu's choice and its package
                var c = SmallConsole("modified", true); ClearLedgers(s);
                var con = OpenComm(c);
                var (p, r) = CommunityEngine.Prepare(con, ctx, cat, Change(Pick(files, "menu-x"), new[] { "menu-x" }));
                Check(r == null && p.Addons.SequenceEqual(new[] { "ccs-nucleus", "ccs-nucleus-chooser" }) && p.StartLine == "native always off default 10"
                      && p.Steps.SequenceEqual(new[] { "method:ccs-nucleus", "method:ccs-nucleus-chooser", "nstart", "nmadd:" + cid, "cput:fx_menu" })
                      && p.ChooserTitle == "This addition uses the startup chooser" && p.ChooserText == "Adding Fixture Menu will turn on CCS Nucleus's startup chooser and add the parts it needs. Your games, saves and other mods stay."
                      && p.Text.StartsWith("Add CCS Nucleus and CCS Nucleus's chooser, so the startup chooser can start Fixture Menu.") && p.MenusAdded.Single().Program == "/var/lib/hakchi/rootfs/bin/fx_menu"
                      && p.Notes.Any(n => n.Contains("Original Menu starts in 10 seconds")) && p.Notes.Any(n => n.StartsWith("Fixture Menu becomes a choice in the startup chooser.") && n.Contains("to choose another menu")) && Writes(c).Count == 0,
                      "a menu on a console without CCS Nucleus: one review names every piece it adds (CCS Nucleus, its chooser, the chooser shown at start with Original Menu after 10 seconds, the menu's choice), nothing written yet (" + Flat(r?.Problem ?? p.Text) + ")");
                var o = CommunityEngine.Execute(con, p, p.Digest, p.Nonce, ctx);
                var ch = Opt(c, cid);
                Check(o.State == "scheduled" && Waiting(c).SequenceEqual(new[] { "ccs-nucleus.hmod", "fx_menu.hmod", "hmod/ccs-nucleus-chooser.hmod" }) && StartIn(c) == "native always off default 10"
                      && ch?.S("kind") == "community" && ch.S("package") == "fx_menu" && ch.S("program") == "/var/lib/hakchi/rootfs/bin/fx_menu" && Encoding.ASCII.GetString(Convert.FromBase64String(ch.S("label64"))) == "Fixture Menu"
                      && Writes(c).Take(4).SequenceEqual(new[] { "method ccs-nucleus", "method ccs-nucleus-chooser", "nstart native always off default 10", "nmadd " + cid }),
                      "confirmed: CCS Nucleus and its chooser wait for the next start, the chooser shows at start, and the menu is a choice in CCS Nucleus's list, read back (" + o.Problem + ")");
                Gone(p.ProtectivePath);
                var st = SimulatedConsole.NormalStart(c);
                var ld = CommunityEngine.Read(OpenComm(c), ctx, cat).Ledger.Single();
                Check(Installed(c).Contains("ccs-nucleus") && Installed(c).Contains("ccs-nucleus-chooser") && Installed(c).Contains("fx_menu") && ld.State == CommunityLedger.Installed && ld.ChoiceId == cid,
                      "the next start installs CCS Nucleus, the menu and the chooser; the ledger names the menu's choice (" + string.Join("; ", st) + ")");
                // the menu removed: its choice goes; the chooser and what opens at power-on stay
                con = OpenComm(c); var n0 = Writes(c).Count;
                var (q, qr) = CommunityEngine.Prepare(con, ctx, cat, Change(new Dictionary<string, string>(), null, new[] { "menu-x" }));
                var qo = q == null ? null : CommunityEngine.Execute(con, q, q.Digest, q.Nonce, ctx);
                Check(qr == null && q.Steps.SequenceEqual(new[] { "ckeep:fx_menu", "nmdrop:" + cid, "cunlist" }) && q.Addons.Count == 0 && q.StartLine == null && qo.State == "scheduled" && Opt(c, cid) == null
                      && StartIn(c) == "native always off default 10" && Installed(c).Contains("ccs-nucleus-chooser") && q.Notes.Any(x => x.Contains("The startup chooser itself stays")) && Writes(c).Skip(n0).Contains("nmdrop " + cid),
                      "removing the menu removes its choice from the startup chooser; the chooser and what opens at power-on stay (" + Flat(qr?.Problem ?? q.Text) + ")");
                Gone(q?.ProtectivePath); GoneConsole(c); GoneBackups(s);

                // 2. CCS Nucleus opening CCS HOME straight away: only its chooser is added; what opens when nothing is chosen stays CCS HOME
                var c2 = SmallConsole("modified", true); ClearLedgers(s);
                SetInstalled(c2, "ccs-mod", "ccs-nucleus"); SetStart(c2, "ccs-home never on default 10");
                var (p2, r2) = CommunityEngine.Prepare(OpenComm(c2), ctx, cat, Change(Pick(files, "menu-x"), new[] { "menu-x" }));
                Check(r2 == null && p2.Addons.SequenceEqual(new[] { "ccs-nucleus-chooser" }) && p2.StartLine == "ccs-home always on default 10" && p2.Steps.SequenceEqual(new[] { "method:ccs-nucleus-chooser", "nstart", "nmadd:" + cid, "cput:fx_menu" })
                      && p2.Notes.Any(n => n.Contains("CCS HOME starts in 10 seconds")),
                      "CCS Nucleus opening CCS HOME straight away: its chooser is added and shown at start, CCS HOME still what starts when nothing is chosen, never left hidden (" + Flat(r2?.Problem ?? string.Join(", ", p2.Steps)) + ")");
                Gone(p2?.ProtectivePath);
                // the chooser already there but set not to show at start: the person's setting is kept and the review says where the menu is
                SetInstalled(c2, "ccs-mod", "ccs-nucleus", "ccs-nucleus-chooser");
                var (p3, r3) = CommunityEngine.Prepare(OpenComm(c2), ctx, cat, Change(Pick(files, "menu-x"), new[] { "menu-x" }));
                Check(r3 == null && p3.Addons.Count == 0 && p3.StartLine == null && p3.ChooserTitle == null && p3.Steps.SequenceEqual(new[] { "nmadd:" + cid, "cput:fx_menu" }) && p3.Notes.Any(n => n.Contains("your choice, kept")),
                      "the chooser already there: only the menu's choice and its package; a chooser set not to show at start stays so, and the review says where the menu is (" + Flat(r3?.Problem ?? string.Join(", ", p3.Steps)) + ")");
                Gone(p3?.ProtectivePath);
                // an earlier CCS Nucleus, which cannot list a community menu: updated in the same reviewed change, named in the review
                SimulatedConsole.SetAddonInstalled(c2, "ccs-nucleus", OldNucleusPin); var w0 = Writes(c2).Count;
                var (p4, r4) = CommunityEngine.Prepare(OpenComm(c2), ctx, cat, Change(Pick(files, "menu-x"), new[] { "menu-x" }));
                var p4j = p4 == null ? null : Json.Parse(Json.Write(p4.ToJson()));
                Check(r4 == null && p4.Updated.SequenceEqual(new[] { "ccs-nucleus" }) && p4.Addons.Count == 0 && p4.Steps.SequenceEqual(new[] { "method:ccs-nucleus", "nmadd:" + cid, "cput:fx_menu" })
                      && p4.Text.Contains("Update CCS Nucleus to this version") && p4.Notes.Any(n => n.StartsWith("CCS Nucleus on this console is an earlier version, which cannot list a community menu, so this change updates it.") && n.Contains("are kept"))
                      && (p4j.A("updates") ?? new List<object>()).Count == 1 && Writes(c2).Count == w0,
                      "an earlier CCS Nucleus on the console: the same review updates it to this version (named, its settings and choices kept), then adds the menu; nothing written yet (" + Flat(r4?.Problem ?? string.Join(", ", p4.Steps)) + ")");
                Gone(p4?.ProtectivePath); SimulatedConsole.SetAddonInstalled(c2, "ccs-nucleus", null);
                // a mod that is not a menu never adds anything of CCS Nucleus
                SetInstalled(c2); File.Delete(Path.Combine(c2, "nucleus-start.txt"));
                var (p5, r5) = CommunityEngine.Prepare(OpenComm(c2), ctx, cat, Change(Pick(files, "wifi"), new[] { "wifi" }));
                Check(r5 == null && p5.Addons.Count == 0 && p5.StartLine == null && p5.MenusAdded.Count == 0 && p5.Steps.SequenceEqual(new[] { "cput:fx_wifi" }), "a mod that is not a menu adds nothing of CCS Nucleus: nothing is added silently");
                Gone(p5?.ProtectivePath);
                // another kind of choice already has the menu's id: refused
                SetInstalled(c2, "ccs-nucleus", "ccs-nucleus-chooser"); SetStart(c2, "native always off default 10");
                Registry(c2, new JObj { { "native", new JObj { { "kind", "native" } } }, { cid, new JObj { { "kind", "owner-root" }, { "sha256", new string('b', 64) }, { "bytes", 1000L } } } });
                var (p6, r6) = CommunityEngine.Prepare(OpenComm(c2), ctx, cat, Change(Pick(files, "menu-x"), new[] { "menu-x" }));
                Check(p6 == null && r6.Problem.Contains("already has the id"), "another kind of choice with the menu's id is never replaced (" + r6?.Problem + ")");
                // CCS Nucleus's list changes after the review: refused at the confirmation, nothing written
                Registry(c2, null);
                var con7 = OpenComm(c2); var (p7, r7) = CommunityEngine.Prepare(con7, ctx, cat, Change(Pick(files, "menu-x"), new[] { "menu-x" }));
                SetOwnImage(c2); var w7 = Writes(c2).Count;
                var o7 = p7 == null ? null : CommunityEngine.Execute(con7, p7, p7.Digest, p7.Nonce, ctx);
                Check(r7 == null && o7.State == "refused" && o7.Problem.Contains("changed since the review") && Writes(c2).Count == w7 && p7.Nonce != null,
                      "CCS Nucleus's list of choices changed after the review: refused at the confirmation, nothing written, the confirmation not used up (" + o7?.Problem + ")");
                Gone(p7?.ProtectivePath); Registry(c2, null);
                // the package's write fails after the menu's choice was added: a new review places the package only, never the choice twice
                File.WriteAllText(Path.Combine(c2, "faults.json"), "{\"prep\":{\"step\":\"cput\"}}");
                var con8 = OpenComm(c2); var (p8, _) = CommunityEngine.Prepare(con8, ctx, cat, Change(Pick(files, "menu-x"), new[] { "menu-x" }));
                var o8 = CommunityEngine.Execute(con8, p8, p8.Digest, p8.Nonce, ctx); Gone(p8.ProtectivePath); File.Delete(Path.Combine(c2, "faults.json"));
                var con9 = OpenComm(c2); var (p9, r9) = CommunityEngine.Prepare(con9, ctx, cat, Change(Pick(files, "menu-x"), new[] { "menu-x" }));
                var o9 = p9 == null ? null : CommunityEngine.Execute(con9, p9, p9.Digest, p9.Nonce, ctx);
                Check(o8.State == "stopped" && o8.Done.SequenceEqual(new[] { "nmadd:" + cid }) && r9 == null && p9.Steps.SequenceEqual(new[] { "cput:fx_menu" }) && o9.State == "scheduled" && Opt(c2, cid)?.S("package") == "fx_menu",
                      "the package's write fails after the menu's choice was added: stopped; reviewed again, only the package is placed (" + o8.Problem + ")");
                Gone(p9?.ProtectivePath);
                // the chooser removed later in Console Restore: its review says what happens to the community menus
                SimulatedConsole.NormalStart(c2);
                var (mp, mr) = MethodEngine.Prepare(OpenMethod(c2), Ctx(s, ImageKind.Method), Choice(false, "direct"));
                Check(mr == null && mp.Notes.Any(n => n.StartsWith("Your community menus stay installed and stay listed in CCS Nucleus.")),
                      "removing the startup chooser in Add-ons and Startup: the review says the community menus stay and how they start without it (" + Flat(mr?.Problem) + ")");
                Gone(mp?.ProtectivePath); GoneConsole(c2); GoneBackups(s);

                // 3. a Super NES Classic: the same rule (the chooser and its start come in the same change, then the menu's choice and package)
                var c3 = SnesPrepared(); ClearLedgers(s); SetInstalled(c3, "ccs-nucleus"); SetStart(c3, "native never off default 10");
                var con3 = OpenComm(c3);
                var (s1, sr1) = CommunityEngine.Prepare(con3, ctx, cat, Change(Pick(files, "menu-x"), new[] { "menu-x" }));
                var so = s1 == null ? null : CommunityEngine.Execute(con3, s1, s1.Digest, s1.Nonce, ctx);
                Check(sr1 == null && s1.Addons.SequenceEqual(new[] { "ccs-nucleus-chooser" }) && s1.StartLine == "native always off default 10" && so.State == "scheduled"
                      && Waiting(c3).SequenceEqual(new[] { "fx_menu.hmod", "hmod/ccs-nucleus-chooser.hmod" }) && Opt(c3, cid)?.S("kind") == "community" && StartIn(c3) == "native always off default 10",
                      "a Super NES Classic: the same reviewed change adds the startup chooser, shows it at start and lists the menu as a choice (" + Flat(sr1?.Problem ?? so?.Problem) + ")");
                Gone(s1?.ProtectivePath); GoneConsole(c3); GoneBackups(s);
            });

            Part("a mod that changes a USB drive or SD card: its USB and SD backup chosen, checked, bound and kept with the console's backup", () =>
            {
                var c = SmallConsole("modified", true); ClearLedgers(s);
                var sd = NewDir("community-usb");
                // a USB drive's files (a temporary folder standing in for its root) and a folder of this computer
                var root = Path.Combine(sd, "usb-root"); Directory.CreateDirectory(Path.Combine(root, "hakchi", "fx_ext")); Directory.CreateDirectory(Path.Combine(root, "saves"));
                File.WriteAllText(Path.Combine(root, "hakchi", "fx_ext", "collection.txt"), "a fixture collection\n"); File.WriteAllText(Path.Combine(root, "saves", "a.sav"), "save\n");
                var pcf = Path.Combine(sd, "pc-folder"); Directory.CreateDirectory(Path.Combine(pcf, "hakchi", "fx_ext")); File.WriteAllText(Path.Combine(pcf, "hakchi", "fx_ext", "collection.txt"), "x\n");
                bool Is(SourceIdentity id, string folder) => id.Root != null && Path.GetFullPath(id.Root).TrimEnd('\\').Equals(Path.GetFullPath(folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                var keepSeen = CommunityStorage.Seen;
                CommunityStorage.Seen = id =>
                {
                    if (Is(id, root)) { id.Kind = SourceKind.Volume; id.RelativePath = "\\"; id.Removable = true; id.BusType = "USB"; id.Label = "FIXTUREUSB"; }
                    else if (Is(id, pcf)) { id.Kind = SourceKind.Volume; id.RelativePath = "\\"; id.Removable = false; id.BusType = "SATA"; id.Label = "PCDRIVE"; }
                    return id;
                };
                try
                {
                    var good = StRun(root, s, "files", null, new List<string> { "/hakchi" });
                    var saves = StRun(root, s, "files", null, new List<string> { "/saves" });
                    var img = Path.Combine(sd, "usb.img"); File.WriteAllBytes(img, FatImage(false, 16L << 20, new List<(string, byte[])> { ("/hakchi/fx_ext/collection.txt", Encoding.ASCII.GetBytes("an image\n")) }, "USBIMAGE"));
                    var whole = StRun(img, s, "image");
                    StorageOutcome part; using (new FileStream(Path.Combine(root, "hakchi", "fx_ext", "collection.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) part = StRun(root, s, "files", null, new List<string> { "/hakchi" });
                    var pc = StRun(pcf, s, "files");
                    var sel = (StorageVerifier.Verify(good.PrimaryPath).SourceList.First().A("selected") ?? new List<object>()).OfType<string>().ToList();
                    Check(good.Protected && saves.Protected && whole.Protected && part.PrimaryPath != null && !part.ScopeComplete && pc.Protected && sel.SequenceEqual(new[] { "/hakchi" }),
                          "fixture USB and SD backups made: the folder /hakchi, the folder /saves, a whole drive image, one that could not copy a file, one of a folder of this computer; a files backup records the folders chosen (" + good.Problem + whole.Problem + ")");
                    string Refusal(string backup) { var (pp, rr) = CommunityEngine.Prepare(OpenComm(c), ctx, cat, Fn(() => { var x = Change(Pick(files, "ext"), new[] { "ext" }); x.StorageBackup = backup; return x; })); Gone(pp?.ProtectivePath); return rr?.Problem ?? "(accepted)"; }
                    var f0 = BackupFiles(s);
                    var none = Refusal(null);
                    var dmg = Path.Combine(sd, "damaged.zip"); File.Copy(good.PrimaryPath, dmg); StFlip(dmg, new FileInfo(dmg).Length / 3);
                    var bad = Refusal(dmg); var inc = Refusal(part.PrimaryPath); var miss = Refusal(saves.PrimaryPath); var notUsb = Refusal(pc.PrimaryPath); var gone = Refusal(Path.Combine(sd, "not-there.zip"));
                    // a files backup as versions before this one wrote it (no record of the folders chosen): its coverage cannot be told
                    StorageBackup.RecordSelected = false; StorageOutcome old;
                    try { old = StRun(root, s, "files", null, new List<string> { "/hakchi" }); } finally { StorageBackup.RecordSelected = true; }
                    var oldWhy = Refusal(old.PrimaryPath);
                    Check(old.Protected && StorageVerifier.Verify(old.PrimaryPath).SourceList.First().A("selected") == null && oldWhy.Contains("does not record which folders were chosen for it (it was made with an earlier version of Workbench)") && oldWhy.Contains("Nothing was changed.")
                          && BackupFiles(s) == f0 && Writes(c).Count == 0,
                          "a files backup made before backups recorded their chosen folders: still a good backup, but refused for this with its plain reason, before any backup or write (" + oldWhy + ")");
                    Check(none.Contains("Choose a USB and SD backup of that storage") && bad.Contains("did not pass its check") && inc.Contains("does not hold everything that was chosen") && miss.Contains("does not hold /media/hakchi/fx_ext")
                          && notUsb.Contains("not a USB drive or SD card") && gone.Contains("is not there") && BackupFiles(s) == f0 && Writes(c).Count == 0,
                          "refused before the console's backup and before any write: no backup chosen; a damaged one; one that is not complete; one that does not hold the place the mod changes; one of a folder of this computer; one that is not there ("
                          + none + " | " + bad + " | " + inc + " | " + miss + " | " + notUsb + ")");
                    // a good files backup: bound, shown with its own identity; the person's confirmation is required
                    var con = OpenComm(c);
                    var (p, r) = CommunityEngine.Prepare(con, ctx, cat, Fn(() => { var x = Change(Pick(files, "ext"), new[] { "ext" }); x.StorageBackup = good.PrimaryPath; return x; }));
                    var pj = p == null ? null : Json.Parse(Json.Write(p.ToJson()));
                    Check(r == null && p.Storage.Sha256 == good.Sha256 && p.Storage.Display.Contains("FIXTUREUSB") && p.Storage.Cover.Single().how == "the folder /hakchi and everything in it" && p.Storage.IdentityText.Contains("Backed up on ")
                          && pj.O("storage")?.S("not_checked") == CommunityStorage.NotCheckedText && pj.O("storage")?.S("checked") == CommunityStorage.CheckedText && pj.S("storage_confirm") == CommunityStorage.ConfirmText && p.Notes.Any(n => n.Contains("recovery set")),
                          "a backup of the folders that hold the place: bound to the plan with its own identity (" + p?.Storage?.IdentityText + "), what was and was not checked said plainly (" + Flat(r?.Problem) + ")");
                    var nc = CommunityEngine.Execute(con, p, p.Digest, p.Nonce, ctx);
                    Check(nc.State == "refused" && nc.Problem == CommunityStorage.NeedConfirm && Writes(c).Count == 0 && p.Nonce != null, "without the person's confirmation that it is the storage the console uses: refused, nothing written, the confirmation not used up");
                    var keepBytes = File.ReadAllBytes(good.PrimaryPath); File.Copy(whole.PrimaryPath, good.PrimaryPath, true);
                    var chg = CommunityEngine.Execute(con, p, p.Digest, p.Nonce, ctx, true); File.WriteAllBytes(good.PrimaryPath, keepBytes);
                    Check(chg.State == "refused" && chg.Problem.Contains("changed after the review") && Writes(c).Count == 0 && p.Nonce != null, "the USB and SD backup replaced between the review and the confirmation: refused, nothing written (" + chg.Problem + ")");
                    var ok = CommunityEngine.Execute(con, p, p.Digest, p.Nonce, ctx, true);
                    var set = ok.RecoverySetRecord == null ? null : RecoverySet.Check(ok.RecoverySetRecord); var setRec = ok.RecoverySetRecord == null ? null : Record.Load(ok.RecoverySetRecord); var opRec = ok.RecordFile == null ? null : Record.Load(ok.RecordFile);
                    var led = CommunityLedger.Load(s, con.Identify().ShortTag).SingleOrDefault();
                    Check(ok.State == "scheduled" && set?.Ok == true && setRec.O("storage_backup")?.S("sha256") == good.Sha256 && setRec.O("console_backup")?.S("file") == Path.GetFullPath(p.ProtectivePath)
                          && opRec.S("recovery_set") == ok.RecoverySetRecord && opRec.S("storage_confirmed") == CommunityStorage.ConfirmText && led?.StorageBackupSha256 == good.Sha256 && led.RecoverySetRecord == ok.RecoverySetRecord
                          && led.BackupPath == p.ProtectivePath && Waiting(c).SequenceEqual(new[] { "fx_ext.hmod" }),
                          "confirmed: both backups checked again, named together in a recovery set record, the operation record and the ledger name both; the mod waits for the next start (" + ok.Problem + ")");
                    Gone(p.ProtectivePath);
                    // its removal, with a whole drive image this time
                    SimulatedConsole.NormalStart(c);
                    var con2 = OpenComm(c);
                    var (rm, rr2) = CommunityEngine.Prepare(con2, ctx, cat, Fn(() => { var x = Change(new Dictionary<string, string>(), null, new[] { "ext" }); x.StorageBackup = whole.PrimaryPath; return x; }));
                    var ro = rm == null ? null : CommunityEngine.Execute(con2, rm, rm.Digest, rm.Nonce, ctx, true);
                    var led2 = CommunityLedger.Load(s, con2.Identify().ShortTag).SingleOrDefault();
                    Check(rr2 == null && rm.Storage.Mode == "image" && rm.Storage.Cover.Single().how.StartsWith("the whole drive image") && ro.State == "scheduled" && RecoverySet.Check(ro.RecoverySetRecord).Ok && led2?.StorageBackupSha256 == whole.Sha256,
                          "removing it with a whole drive image of the storage: bound and kept with the console's backup the same way (" + Flat(rr2?.Problem ?? ro?.Problem) + ")");
                    Gone(rm?.ProtectivePath);
                }
                finally
                {
                    CommunityStorage.Seen = keepSeen;
                    var sf = StorageBackup.Folder(s); if (Directory.Exists(sf)) Directory.Delete(sf, true);
                    GoneConsole(c); GoneBackups(s);
                }
            });

            Part("CCS Nucleus and its chooser updated from an earlier version, keeping its settings and choices", () =>
            {
                Check(Methods.Sha256("ccs-nucleus") != OldNucleusPin && Methods.Sha256("ccs-nucleus-chooser") == OldChooserPin,
                      "this build's CCS Nucleus package is a new version (" + Methods.Sha256("ccs-nucleus")?.Substring(0, 12) + "...); its chooser package is still 0.11.11's (1c6abd0f...)");
                var mctx = Ctx(s, ImageKind.Method);
                var c = SmallConsole("modified", true); ClearLedgers(s);
                SetInstalled(c, "ccs-mod", "ccs-nucleus", "ccs-nucleus-chooser"); SetStart(c, "ccs-home always on last 10"); SetOwnImage(c);
                SimulatedConsole.SetAddonInstalled(c, "ccs-nucleus", OldNucleusPin); SimulatedConsole.SetAddonInstalled(c, "ccs-nucleus-chooser", OldChooserPin);
                var (st0, _, why0) = MethodEngine.Current(OpenMethod(c), mctx);
                var ev = Json.Parse(Json.Write(MethodEngine.StateEvent("method", "hakchi", null, st0)));
                var (n1, r1) = MethodEngine.Prepare(OpenMethod(c), mctx, AddonChoice.FromState(st0));
                Check(why0 == null && Methods.Earlier(st0).SequenceEqual(new[] { "ccs-nucleus" }) && st0.AddonVersion("ccs-nucleus-chooser") == "current" && (ev.A("earlier") ?? new List<object>()).Count == 1
                      && n1 == null && r1.Problem.Contains("CCS Nucleus is an earlier version. Choose Update to this version") && Writes(c).Count == 0,
                      "0.11.11's CCS Nucleus (0074f2ec...) reads as an earlier version, its chooser (1c6abd0f...) as this version's; the add-on page is told; without Update nothing is planned and the refusal says what to choose (" + r1?.Problem + ")");
                // Last Used with no countdown (set on the console's own startup chooser): the page's choice carries it, and an update alone keeps it exactly
                SetStart(c, "ccs-home always on last 0");
                var con = OpenMethod(c); var up = AddonChoice.FromState(MethodEngine.Current(con, mctx).st); up.Update = true;
                var (p, r) = MethodEngine.Prepare(con, mctx, up);
                var pj = p == null ? null : Json.Parse(Json.Write(p.ToJson()));
                Check(r == null && p.Updated.SequenceEqual(new[] { "ccs-nucleus" }) && p.Add.Count == 0 && p.Remove.Count == 0 && p.StartLine == null && p.Steps.SequenceEqual(new[] { "method:ccs-nucleus" })
                      && p.Text.StartsWith("Update CCS Nucleus to this version: hakchi installs it over the earlier one") && p.Text.Contains("your system images and community menus in CCS Nucleus's list, games and saves are kept")
                      && (pj.A("update") ?? new List<object>()).Count == 1,
                      "Update chosen: one review names it; only this version's CCS Nucleus package is placed over the earlier one, nothing removed, the start setting (CCS HOME, Last Used, no countdown) not written (" + Flat(r?.Problem ?? p.Text) + ")");
                var o = MethodEngine.Execute(con, p, p.Digest, p.Nonce, mctx); Gone(p.ProtectivePath);
                var waited = Waiting(c);
                var stNow = SimulatedConsole.NormalStart(c);
                var (st1, _, _) = MethodEngine.Current(OpenMethod(c), mctx);
                Check(o.State == "scheduled" && Writes(c).SequenceEqual(new[] { "method ccs-nucleus" }) && waited.SequenceEqual(new[] { "ccs-nucleus.hmod" }) && stNow.Contains("install ccs-nucleus") && st1.AddonVersion("ccs-nucleus") == "current"
                      && StartIn(c) == "ccs-home always on last 0" && OwnImageKept(c) && Installed(c).Contains("ccs-nucleus-chooser") && Installed(c).Contains("ccs-mod") && Methods.Earlier(st1).Count == 0,
                      "confirmed and the next start: hakchi installs this version over the earlier one; what opens first, Last Used with no countdown, the owner's system image, the chooser and CCS Mod stay (" + o.Problem + ")");
                // a community menu added on the updated console: no update any more, only its choice and its package
                var cc = OpenComm(c); var (m1, mr1) = CommunityEngine.Prepare(cc, ctx, cat, Change(Pick(files, "menu-x"), new[] { "menu-x" }));
                var mo = m1 == null ? null : CommunityEngine.Execute(cc, m1, m1.Digest, m1.Nonce, ctx); Gone(m1?.ProtectivePath);
                Check(mr1 == null && m1.Updated.Count == 0 && m1.Addons.Count == 0 && m1.Steps.SequenceEqual(new[] { "nmadd:" + CommunityMenus.ChoiceId("menu-x"), "cput:fx_menu" }) && mo.State == "scheduled",
                      "a community menu on the updated console: only its choice and its package (" + Flat(mr1?.Problem ?? mo?.Problem) + ")");
                GoneConsole(c); GoneBackups(s);
                // an earlier chooser too: both updated in one review, the chooser in hmod/ (hakchi installs it after CCS Nucleus)
                var c2 = SmallConsole("modified", true); ClearLedgers(s);
                SetInstalled(c2, "ccs-nucleus", "ccs-nucleus-chooser"); SetStart(c2, "native always off default 0");
                SimulatedConsole.SetAddonInstalled(c2, "ccs-nucleus", OldNucleusPin); SimulatedConsole.SetAddonInstalled(c2, "ccs-nucleus-chooser", new string('c', 64));
                var con2 = OpenMethod(c2); var (s2, _, _) = MethodEngine.Current(con2, mctx); var up2 = AddonChoice.FromState(s2); up2.Update = true;
                var (p2, r2) = MethodEngine.Prepare(con2, mctx, up2);
                var o2 = p2 == null ? null : MethodEngine.Execute(con2, p2, p2.Digest, p2.Nonce, mctx); Gone(p2?.ProtectivePath);
                Check(r2 == null && p2.Updated.SequenceEqual(new[] { "ccs-nucleus", "ccs-nucleus-chooser" }) && p2.StartLine == null && o2.State == "scheduled" && Waiting(c2).SequenceEqual(new[] { "ccs-nucleus.hmod", "hmod/ccs-nucleus-chooser.hmod" }),
                      "an earlier chooser too: both updated in one review (Wait for a Choice kept), the chooser waiting in hmod/ so hakchi installs it after CCS Nucleus (" + Flat(r2?.Problem ?? o2?.Problem) + ")");
                SimulatedConsole.NormalStart(c2); GoneConsole(c2); GoneBackups(s);
                // the update's write fails: stopped, nothing more written, the console keeps its earlier version; a new review finishes it once
                var c3 = SmallConsole("modified", true); ClearLedgers(s);
                SetInstalled(c3, "ccs-nucleus"); SetStart(c3, "native never off default 10"); SimulatedConsole.SetAddonInstalled(c3, "ccs-nucleus", OldNucleusPin);
                File.WriteAllText(Path.Combine(c3, "faults.json"), "{\"prep\":{\"step\":\"method\"}}");
                var con3 = OpenMethod(c3); var (s3, _, _) = MethodEngine.Current(con3, mctx); var up3 = AddonChoice.FromState(s3); up3.Update = true;
                var (p3, _) = MethodEngine.Prepare(con3, mctx, up3); var o3 = MethodEngine.Execute(con3, p3, p3.Digest, p3.Nonce, mctx); Gone(p3.ProtectivePath);
                File.Delete(Path.Combine(c3, "faults.json"));
                var (s3b, _, _) = MethodEngine.Current(OpenMethod(c3), mctx);
                Check(o3.State == "stopped" && o3.Done.Count == 0 && o3.Problem.Contains("Nothing more was written and nothing was retried") && Record.Load(o3.RecordFile).S("state") == "stopped"
                      && s3b.AddonVersion("ccs-nucleus") == "earlier" && Waiting(c3).Count == 0 && StartIn(c3) == "native never off default 10",
                      "the update's write fails: stopped there, nothing retried or written, the record says so; the console keeps its earlier CCS Nucleus and its settings (" + o3.Problem + ")");
                var con4 = OpenMethod(c3); var (s4, _, _) = MethodEngine.Current(con4, mctx); var up4 = AddonChoice.FromState(s4); up4.Update = true;
                var (p5, r5) = MethodEngine.Prepare(con4, mctx, up4); var o5 = p5 == null ? null : MethodEngine.Execute(con4, p5, p5.Digest, p5.Nonce, mctx); Gone(p5?.ProtectivePath);
                // something else waiting in hakchi's transfer folder: the update is refused before anything
                var w5 = Writes(c3).Count; File.WriteAllText(Path.Combine(Transfer(c3), "someone-else.hmod"), "x");
                var (p6, r6) = MethodEngine.Prepare(OpenMethod(c3), mctx, up4);
                Check(o5?.State == "scheduled" && Writes(c3).Count(x => x == "method ccs-nucleus") == 1 && p6 == null && r6.Problem.Contains("something else already waits in hakchi's transfer folder") && Writes(c3).Count == w5,
                      "reviewed again, the update is written once; with something else waiting for the next start it is refused before anything (" + Flat(r5?.Problem ?? r6?.Problem) + ")");
                GoneConsole(c3); GoneBackups(s);
            });

            Part("a community menu's approved update and its variant switch: its choice in the startup chooser follows", () =>
            {
                var cid = CommunityMenus.ChoiceId("menu-x");
                Dictionary<string, object> Opt(string cc) { var f = Path.Combine(cc, "nucleus-registry.json"); return File.Exists(f) ? Json.Parse(File.ReadAllText(f)).O("options")?.O(cid) : null; }
                var mdir = NewDir("community-menu-versions");
                var v1Sha = ShaOf(set.First(f => f.Id == "menu-x").Hmod);
                // version 2: its program moves to etc/fx_menu/run (inside its own places); an approved update of 1.0.0; a compat package too
                byte[] MenuHmod(string version, string extra) => Hmod(TD("./"), TD("./bin"), TF("./bin/fx_menu", "#!/bin/sh\necho fx_menu " + version + "\n"), TD("./etc"), TD("./etc/init.d"), TF("./etc/init.d/S81fx_menu", "#!/bin/sh\n"), TD("./etc/fx_menu"),
                    TF("./etc/fx_menu/defaults.cfg", "volume=5\n" + extra), TF("./etc/fx_menu/run", "#!/bin/sh\necho menu " + version + "\n"), TF("./install", "#!/bin/sh\n"), TF("./uninstall", "rm -f \"$rootfs/etc/fx_menu/settings.cfg\"\n"), TF("./readme.md", "fx_menu " + version + "\n"));
                var v2 = MenuHmod("2.0.0", ""); var v2c = MenuHmod("2.0.0", "compat=1\n");
                var f2 = Path.Combine(mdir, "fx_menu.hmod"); File.WriteAllBytes(f2, v2); var f2c = Path.Combine(mdir, "fx_menu_compat.hmod"); File.WriteAllBytes(f2c, v2c);
                JObj Menu2(bool compat)
                {
                    var r = RecordJson(new ModFx { Id = "menu-x", Name = "Fixture Menu", Section = "menus", Order = 10, Package = "fx_menu", Version = "2.0.0", Hmod = v2, Launch = "launcher", Program = "etc/fx_menu/run",
                        Return = "Choose Exit in its menu to return to the startup chooser.", From = new[] { ("1.0.0", v1Sha) } });
                    if (compat) ((List<object>)r["variants"]).Add(new JObj { { "variant", "compat" }, { "package", "fx_menu_compat" }, { "url", "https://mods.example.org/menu-x/fx_menu_compat.hmod" }, { "file_name", "fx_menu_compat.hmod" },
                        { "bytes", (long)v2c.Length }, { "sha256", ShaOf(v2c) }, { "unpacked_bytes", 100000L }, { "use_when", new List<object> { "retroarch-k" } }, { "reason", "Uses the build that works with RetroArch, Build K." } });
                    return r;
                }
                var others = set.Where(f => f.Id != "menu-x").Select(RecordJson).ToList();
                var catB = CommunityCatalog.Parse(CatalogBytes(6, others.Concat(new[] { Menu2(false) })));
                var catC = CommunityCatalog.Parse(CatalogBytes(7, others.Concat(new[] { Menu2(true) })));
                var c = SmallConsole("modified", true); ClearLedgers(s); SetInstalled(c, "ccs-nucleus", "ccs-nucleus-chooser"); SetStart(c, "native always off default 10");
                var con = OpenComm(c); var (p1, _) = CommunityEngine.Prepare(con, ctx, cat, Change(Pick(files, "menu-x"), new[] { "menu-x" })); CommunityEngine.Execute(con, p1, p1.Digest, p1.Nonce, ctx); Gone(p1.ProtectivePath);
                SimulatedConsole.NormalStart(c);
                var led = CommunityEngine.Read(OpenComm(c), ctx, cat).Ledger.Single();
                // the approved update (installed over): its choice changed to the new program in the same review
                con = OpenComm(c);
                var (u, ur) = CommunityEngine.Prepare(con, ctx, catB, Change(new Dictionary<string, string> { { "menu-x", f2 } }, update: new[] { "menu-x" }));
                var uo = u == null ? null : CommunityEngine.Execute(con, u, u.Digest, u.Nonce, ctx); Gone(u?.ProtectivePath);
                Check(CommunityResolver.UpdateAvailable(catB, led) && ur == null && u.Resolution.Items.Single().Op == "update" && u.Steps.SequenceEqual(new[] { "ckeep:fx_menu", "nmadd:" + cid, "cput:fx_menu" })
                      && u.MenusAdded.Single().Program == "/var/lib/hakchi/rootfs/etc/fx_menu/run" && u.Text.StartsWith("Update Fixture Menu to 2.0.0.") && uo.State == "scheduled"
                      && Opt(c)?.S("program") == "/var/lib/hakchi/rootfs/etc/fx_menu/run" && Opt(c)?.S("package") == "fx_menu",
                      "an approved update of a menu: one review updates its package and changes its choice in the startup chooser to its new program; read back (" + Flat(ur?.Problem ?? uo?.Problem) + ")");
                SimulatedConsole.NormalStart(c); var ld2 = CommunityEngine.Read(OpenComm(c), ctx, catB).Ledger.Single();
                // its variant switch: RetroArch, Build K added, the menu changes to its compat package; its choice follows
                con = OpenComm(c);
                var (w, wr) = CommunityEngine.Prepare(con, ctx, catC, Change(new Dictionary<string, string> { { "menu-x", f2c }, { "retroarch-k", files["retroarch-k"] } }, install: new[] { "retroarch-k" }));
                var wo = w == null ? null : CommunityEngine.Execute(con, w, w.Digest, w.Nonce, ctx); Gone(w?.ProtectivePath);
                var sw = w?.Resolution.Items.FirstOrDefault(i => i.Op == "switch");
                Check(ld2.Version == "2.0.0" && ld2.State == CommunityLedger.Installed && wr == null && sw?.Record.Id == "menu-x" && sw.Variant.Package == "fx_menu_compat" && w.MenusAdded.Single().Package == "fx_menu_compat"
                      && w.Steps.IndexOf("cunlist") >= 0 && w.Steps.IndexOf("cunlist") < w.Steps.IndexOf("nmadd:" + cid) && w.Steps.IndexOf("nmadd:" + cid) < w.Steps.IndexOf("cput:fx_menu_compat")
                      && w.Notes.Any(n => n.Contains("Fixture Menu changes to its compat version: Uses the build that works with RetroArch, Build K.")) && wo.State == "scheduled" && Opt(c)?.S("package") == "fx_menu_compat",
                      "its variant switch (RetroArch, Build K added): the review says why, the earlier package goes, the menu's choice changes to the compat package and that package is placed; read back (" + Flat(wr?.Problem ?? wo?.Problem) + ")");
                var stx = SimulatedConsole.NormalStart(c); var ld3 = CommunityEngine.Read(OpenComm(c), ctx, catC).Ledger.First(m => m.Id == "menu-x");
                Check(stx.Contains("uninstall fx_menu") && stx.Contains("install fx_menu_compat") && ld3.Package == "fx_menu_compat" && ld3.ChoiceId == cid && ld3.State == CommunityLedger.Installed,
                      "the next start swaps the packages; the ledger names the compat package and the same chooser choice (" + string.Join("; ", stx) + ")");
                GoneConsole(c); GoneBackups(s);
            });

            Part("restore the pre-install backup: found and checked, or refused", () =>
            {
                var c = SmallConsole("modified", true); ClearLedgers(s);
                var con = OpenComm(c);
                var (p, _) = CommunityEngine.Prepare(con, ctx, cat, Change(Pick(files, "wifi"), new[] { "wifi" }));
                CommunityEngine.Execute(con, p, p.Digest, p.Nonce, ctx);
                var tag = con.Identify().ShortTag; var m = CommunityLedger.Load(s, tag).Single();
                var (bk, warn, why) = CommunityRollback.Check(s, tag, m);
                Check(bk == p.ProtectivePath && why == null && warn.StartsWith("Restore the pre-install backup puts the whole console back to how it was on ") && warn.Contains("before Wi-Fi was installed") && warn.Contains("backed up first"),
                      "the backup made before the mod was installed is found and verified, with the plain date and scope warning: " + warn);
                var (_, _, w1) = CommunityRollback.Check(s, "00000000", m);
                var none = new CommunityInstalled { Id = "x", Name = "X" }; var (_, _, w2) = CommunityRollback.Check(s, tag, none);
                File.Move(p.ProtectivePath, p.ProtectivePath + ".aside"); var (_, _, w3) = CommunityRollback.Check(s, tag, m); File.Move(p.ProtectivePath + ".aside", p.ProtectivePath);
                var other = Backup(c, s); m.BackupPath = other.PrimaryPath; var (_, _, w4) = CommunityRollback.Check(s, tag, m); Gone(other.PrimaryPath, other.ArchivePath);
                m.BackupPath = p.ProtectivePath; using (var f = new FileStream(p.ProtectivePath, FileMode.Open, FileAccess.ReadWrite)) { f.Position = f.Length / 2; var bb = f.ReadByte(); f.Position = f.Length / 2; f.WriteByte((byte)(bb ^ 0xff)); }
                var (_, _, w5) = CommunityRollback.Check(s, tag, m);
                Check(w1.Contains("different console") && w2.Contains("no record") && w3.Contains("not where it was saved") && w4.Contains("not the one made before") && w5.Contains("no longer passes its check"),
                      "refused with the reason: another console's tag, no backup recorded, the file moved, another backup in its place, or a damaged file (" + w5 + ")");
                Gone(p.ProtectivePath); GoneConsole(c); GoneBackups(s);
            });

            Part("the worker's community request", () =>
            {
                var c = SmallConsole("modified", true); ClearLedgers(s);
                var req = new JObj { { "cmd", "community" }, { "console", "simulated" }, { "folder", c }, { "catalog", catFile } };
                Dictionary<string, object> plan = null;
                var evs = WorkerTalk(req, s, ev => ev.S("event") == "community-state" ? new JObj { { "cmd", "community-change" }, { "install", new List<object> { "wifi" } }, { "files", new JObj { { "wifi", files["wifi"] } } } }
                    : ev.S("event") == "community-plan" ? Confirm("community-confirm", plan = ev.O("plan")) : null);
                var stt = Ev(evs, "community-state"); var res = Last(evs);
                Check(stt != null && stt.S("refusal") == null && stt.S("family") == "nes-classic" && plan != null && Strings(plan, "steps").SequenceEqual(new[] { "cput:fx_wifi" }) && res?.S("operation") == "community" && res.S("state") == "scheduled"
                      && Waiting(c).SequenceEqual(new[] { "fx_wifi.hmod" }) && res.S("applies") == "at the console's next normal start",
                      "worker: the console's mods read first (nothing written), then the change planned after the backup and confirmed with the plan's own code (" + res?.S("state") + " " + res?.S("problem") + ")");
                Gone(res?.S("protective"));
                var quit = WorkerTalk(req, s, ev => ev.S("event") == "community-state" ? new JObj { { "cmd", "quit" } } : null);
                var bad = WorkerTalk(new JObj { { "cmd", "community" }, { "console", "simulated" }, { "folder", c }, { "catalog", Path.Combine(dir, "no-such.json") } }, s);
                Check(Last(quit)?.S("state") == "checked" && Writes(c).Count == 1 && Last(bad)?.S("state") == "refused" && Last(bad).S("problem").Contains("cannot be used"),
                      "worker: leaving after the reading changes nothing; an unreadable catalog file is refused (" + Last(bad)?.S("problem") + ")");
                GoneConsole(c); GoneBackups(s);
            });

            // ---- the real console stays behind its own closed boundary
            Check(CcsBackup.Gate.ConsoleSessions && CcsBackup.Gate.CommunityWrites && RecoverySessions.Refusal(ImageKind.Community) == null, "a real console: Community Mods' boundary is open in this version (asked of the boundary itself: no test opens a real session or lists USB devices)");
            var real2 = new WriteAuthority("x", HwidA, CommunityEngine.Steps, () => CcsBackup.Gate.CommunityWrites, CcsBackup.Gate.CommunityText);
            foreach (var step in CommunityEngine.Steps) { try { real2.Require(step, HwidA); Check(true, "a community write (" + step + ") on a real console: admitted by its open boundary for the plan's own step and console"); } catch (Exception ex) { Check(false, "a community write (" + step + ") was refused: " + ex.Message); } }
            try { real2.Require("no-such-step", HwidA); Check(false, "a step outside the plan was admitted"); } catch (InvalidOperationException) { Check(true, "a community write: a step outside the reviewed plan is refused"); }
            try { real2.Require(CommunityEngine.Steps[0], HwidB); Check(false, "another console was admitted"); } catch (InvalidOperationException) { Check(true, "a community write: another console is refused"); }
            real2.Revoke("an earlier step failed"); try { real2.Require(CommunityEngine.Steps[0], HwidA); Check(false, "a revoked authority was admitted"); } catch (InvalidOperationException) { Check(true, "a community write: nothing more after an earlier failure"); }
        }
    }
}
