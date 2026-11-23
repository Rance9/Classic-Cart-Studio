// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The By-type categories and the By-game grouping of the files in a captured data storage image (mounted at /var/lib on the console, so
// "/clover/..." here is /var/lib/clover/... there). Only structures documented in pinned source or in the console's own software are used;
// a file name alone never decides a game, and a file no rule recognizes stays visible as "Everything else". Each rule and its evidence:
//
//  Folder owners (the engine's Inventory.OwnerOf uses the same paths):
//   O1 /clover/**    Nintendo's own data folder (the stock system's XDG_DATA_HOME is /var/lib/clover/profiles/0/ and its inittab creates
//                    /var/lib/clover/...; checked in the console's own system storage): System files unless a rule below says more.
//   O2 /hakchi/**    hakchi's folder (Hakchi2 CE 3.9.3: RemoteGameSyncPath /var/lib/hakchi/games, the mod overlay /var/lib/hakchi/rootfs):
//                    Mods and emulators unless a rule below says more.
//   O3 /ccs/**       Classic Cart Studio's folder (tools/companion/native_home/console_package.py, layout "install"): Mods and emulators
//                    unless a rule below says more. "/ccs-something" at the top is NOT Classic Cart Studio's (only the folder itself is).
//   O4 /lost+found   the file system's own folder (made by mke2fs): System files.
//   Everything else at first: Everything else.
//  Nintendo (stock NES Classic):
//   N1 /clover/profiles/0/CLV-*/  one game's save folder, named by the game's code (the console's own .desktop files say
//                    Path=/var/lib/clover/profiles/0//<Code>; Hakchi2 CE 3.9.3 SaveStateManager lists /var/saves/CLV-* and deletes
//                    /var/lib/clover/profiles/0/<code> for the same game). Inside it (SaveStateManager's own checks):
//                      cartridge.sram, save.sram            the battery save                         -> Saves
//                      1.state .. 4.state                   a suspend point (set 1-4)                -> Suspend points
//                      suspendpoint1/ .. suspendpoint4/     a suspend point set and all its files    -> Suspend points (kept together)
//                      anything else                        kept with the game, kind not recognized  -> Everything else
//   N2 /clover/profiles/0/home-menu/**  the HOME menu's own state (stock inittab creates it; clover-menu-reset deletes it to reset the
//                    menu): Settings.
//   N3 The 30 game codes of the North American / European NES Classic and their names (Name= in each game's .desktop: the console's own
//                    /usr/share/games/nes/kachikachi/<code>/<code>.desktop, identical to Hakchi2 CE 3.9.3 data/desktop_entries/nes-usa):
//                    a save folder named by one of them is that game's, even though its game files are in the system storage, which is
//                    kept whole in the backup and not browsed here. The same for the 21 game codes of the North American / European
//                    Super NES Classic (/usr/share/games/<code>/<code>.desktop; Hakchi2 CE 3.9.3 data/desktop_entries/snes-usa and snes-eur:
//                    the same codes and names). Its saves are kept and exported exactly as they are: no conversion of any kind.
//  hakchi (Hakchi2 CE 3.9.3):
//   H1 /hakchi/config                                     hakchi's settings (the engine reads cfg_usb_* from it): Settings.
//   H2 /hakchi/hmod/**, /hakchi/rootfs/**                  installed mods' uninstall lists and the overlay mods install into: Mods and emulators.
//   H3 a folder X under /hakchi/games/ holding the file X.desktop is one game's folder (SyncTask/NesApplication: .storage/<code>/ and the
//                    menu folders NNN/<code>/, each with <code>.desktop). Its .desktop (DesktopFile.GetConfig) gives Code=, Name=,
//                    Exec=, Icon= and Path=:
//                      X.desktop                            the game's entry                         -> Games
//                      a file Exec= names in this folder    the game file                            -> Games
//                      a file starting with the iNES or FDS header ("NES" 1A / "FDS" 1A)   a game file -> Games
//                      the file Icon= names, <code>.png, <code>_small.png, <code>_original.png, <code>_spine.png, <code>_logo.png
//                                                           the game's artwork                       -> Artwork
//                      <icon name>_mdmini.png, <code>_mdmini.png (Hakchi2 CE's picture for the Sega Mini menu; SegaPictures.cs)
//                                                           artwork, role sega-composite             -> Artwork
//                      anything else                       kept with the game, kind not recognized  -> Everything else
//                    Path= (/var/saves/<n> or /var/lib/clover/profiles/0/<n>) names the game's save folder (N1): that folder joins this game.
//                    /var/saves is taken to be /var/lib/clover/profiles/0 (SaveStateManager uses both for the same folder); saves kept on
//                    a USB drive are not in a backup of built-in storage at all.
//  Classic Cart Studio (tools/companion/native_home/console_package.py, tools/player/core/gp_save.c):
//   C1 /ccs/pkg/current                                   which HOME package copy the console uses: Settings.
//   C2 /ccs/pkg/<16 hex>.<8 hex>/   one HOME package copy:
//                      MANIFEST.json, SHA256SUMS            the package's own file list                -> Mods and emulators
//                      files MANIFEST.json lists, by kind: rom -> Games, startup-image -> Artwork, configuration -> Settings,
//                                                           executable, font and license -> Mods and emulators, home -> the HOME's own
//                                                           manifest.json (image types -> Artwork, the rest -> Mods and emulators)
//                      session.json                         the game list and save places              -> Settings; each listed game's file
//                                                           (rom /tmp/ccs/pkg/roms/... = this copy's roms/...) -> Games, linked to the
//                                                           game by its content_sha256
//                      startup/startup.json                 the startup screen setting                 -> Settings; the pictures it lists -> Artwork
//                      home/games.json                      each game's cover (cover=, by the game's id) -> Artwork, linked to the game
//   C3 /ccs/data/saves/<64 hex>/   one game's saves, named by its game file's content SHA-256 (gp_save.c):
//                      cart.sav                             the cartridge save (battery or flash)     -> Saves
//                      slot1.ccsstate .. slot4.ccsstate     a suspend point (save slot 1-4)           -> Suspend points
//                      continue.ccsstate                    an older version's automatic continuation -> Suspend points (set 0)
//                      last-used.json, slot-names.json, game.json   details of the saves                -> Settings (game.json's last_title
//                                                           names the game when no HOME list does)
//                      session.lock                         the player's lock while the game is open  -> System files
//                      anything else                        kept with the game, kind not recognized  -> Everything else
//   C4 /ccs/data/state/**, /ccs/data/game-settings/**      the HOME's and the player's settings: Settings.
// The backup file's own members are classified by BackupContents (storage images and reference copies: System files; the .ccs export:
// Games; the file list, README and manifest: Everything else).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsBackup.Browse
{
    public static class Classifier
    {
        /// <summary>N3: the NES Classic (North America / Europe) game codes and the names its own .desktop files give them</summary>
        public static readonly IReadOnlyDictionary<string, string> StockGames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "CLV-P-NAAAE", "Super Mario Bros." }, { "CLV-P-NAACE", "Super Mario Bros. 3" }, { "CLV-P-NAADE", "Super Mario Bros. 2" },
            { "CLV-P-NAAEE", "Donkey Kong" }, { "CLV-P-NAAFE", "Donkey Kong Jr." }, { "CLV-P-NAAHE", "Excitebike" }, { "CLV-P-NAANE", "The Legend of Zelda" },
            { "CLV-P-NAAPE", "Kirby's Adventure" }, { "CLV-P-NAAQE", "Metroid" }, { "CLV-P-NAARE", "Balloon Fight" }, { "CLV-P-NAASE", "Zelda II - The Adventure of Link" },
            { "CLV-P-NAATE", "Punch-Out!! Featuring Mr. Dream" }, { "CLV-P-NAAUE", "Ice Climber" }, { "CLV-P-NAAVE", "Kid Icarus" }, { "CLV-P-NAAWE", "Mario Bros." },
            { "CLV-P-NAAXE", "Dr. MARIO" }, { "CLV-P-NAAZE", "StarTropics" }, { "CLV-P-NABBE", "MEGA MAN\u2122 2" }, { "CLV-P-NABCE", "GHOSTS'N GOBLINS\u2122" },
            { "CLV-P-NABJE", "FINAL FANTASY\u00ae" }, { "CLV-P-NABKE", "BUBBLE BOBBLE" }, { "CLV-P-NABME", "PAC-MAN" }, { "CLV-P-NABNE", "Galaga" },
            { "CLV-P-NABQE", "Castlevania" }, { "CLV-P-NABRE", "GRADIUS" }, { "CLV-P-NABVE", "Super C" }, { "CLV-P-NABXE", "Castlevania II Simon's Quest" },
            { "CLV-P-NACBE", "NINJA GAIDEN" }, { "CLV-P-NACDE", "TECMO BOWL" }, { "CLV-P-NACHE", "DOUBLE DRAGON II: The Revenge" },
            // the Super NES Classic (North America / Europe)
            { "CLV-P-SAAAE", "Super Mario World" }, { "CLV-P-SAABE", "F-ZERO" }, { "CLV-P-SAAEE", "The Legend of Zelda: A Link to the Past" },
            { "CLV-P-SAAFE", "Super Mario Kart" }, { "CLV-P-SAAHE", "Super Metroid" }, { "CLV-P-SAAJE", "EarthBound" }, { "CLV-P-SAAKE", "Kirby's Dream Course" },
            { "CLV-P-SAALE", "Donkey Kong Country" }, { "CLV-P-SAAQE", "Kirby Super Star" }, { "CLV-P-SAAXE", "Super Punch-Out!!" }, { "CLV-P-SABCE", "Mega Man X" },
            { "CLV-P-SABDE", "Super Ghouls'n Ghosts" }, { "CLV-P-SABHE", "Street Fighter II Turbo: Hyper Fighting" }, { "CLV-P-SABQE", "Super Mario RPG: Legend of the Seven Stars" },
            { "CLV-P-SABRE", "Secret of Mana" }, { "CLV-P-SABTE", "Final Fantasy III" }, { "CLV-P-SACBE", "Super Castlevania IV" }, { "CLV-P-SACCE", "CONTRA III THE ALIEN WARS" },
            { "CLV-P-SADGE", "Star Fox" }, { "CLV-P-SADJE", "Yoshi's Island" }, { "CLV-P-SADKE", "Star Fox 2" },
        };
        /// <summary>the console a stock game code belongs to ("NES Classic", "SNES Classic"), or null for any other code</summary>
        public static string StockConsole(string code) => code == null || !StockGames.ContainsKey(code) ? null : code.StartsWith("CLV-P-S", StringComparison.Ordinal) ? "SNES Classic" : "NES Classic";

        static readonly Regex SaveFolderRe = new Regex(@"^/clover/profiles/0/(CLV-[^/]+)$", RegexOptions.CultureInvariant), StateRe = new Regex(@"^([1-4])\.state$", RegexOptions.CultureInvariant),
            SuspendRe = new Regex(@"^suspendpoint([1-4])$", RegexOptions.CultureInvariant), CopyRe = new Regex(@"^[0-9a-f]{16}\.[0-9a-f]{8}$", RegexOptions.CultureInvariant),
            Hex64 = new Regex(@"^[0-9a-f]{64}$", RegexOptions.CultureInvariant), SlotRe = new Regex(@"^slot([1-4])\.ccsstate$", RegexOptions.CultureInvariant),
            PkgRomRe = new Regex(@"^roms/([0-9a-f]{64})\.nes$", RegexOptions.CultureInvariant);
        const long SmallText = 64 * 1000, BigText = 8L * 1000 * 1000;

        sealed class Game { public string Key, Code, Sha; public readonly List<(string title, string source)> Titles = new List<(string, string)>(); public bool Hakchi; }

        /// <summary>Classifies every data entry in place and returns the games found. read(entry, max): the entry's bytes (null when it
        /// cannot be read or is larger than max); head(entry, n): its first n bytes (null when unreadable).</summary>
        public static List<GameGroup> Run(IList<BrowseEntry> data, Func<BrowseEntry, long, byte[]> read, Func<BrowseEntry, int, byte[]> head)
        {
            var byPath = new Dictionary<string, BrowseEntry>(StringComparer.Ordinal);
            foreach (var e in data) byPath[e.ImagePath] = e;
            var kids = new Dictionary<string, List<BrowseEntry>>(StringComparer.Ordinal);
            foreach (var e in data) { var p = Parent(e.ImagePath); if (!kids.TryGetValue(p, out var l)) kids[p] = l = new List<BrowseEntry>(); l.Add(e); }
            var games = new Dictionary<string, Game>(StringComparer.Ordinal);
            Game G(string key) { if (!games.TryGetValue(key, out var g)) games[key] = g = new Game { Key = key }; return g; }
            List<BrowseEntry> Kids(string dir) => kids.TryGetValue(dir, out var l) ? l : new List<BrowseEntry>();
            IEnumerable<BrowseEntry> Under(string dir) { foreach (var k in Kids(dir)) { yield return k; if (k.Kind == EntryKind.Folder) foreach (var d in Under(k.ImagePath)) yield return d; } }
            BrowseEntry At(string p) => p != null && byPath.TryGetValue(p, out var e) ? e : null;
            void Set(BrowseEntry e, Category c, string why, string key = null, string role = null, int set = -1, string rel = null)
            { if (e == null) return; e.Category = c; e.CategoryReason = why; e.GameKey = key; e.Role = role; e.SuspendSet = set; e.GameRelative = rel; }
            string Text(BrowseEntry e, long max) { if (e == null || e.Kind != EntryKind.File) return null; var b = read(e, max); return b == null ? null : Encoding.UTF8.GetString(b); }
            Dictionary<string, object> JsonOf(BrowseEntry e, long max) { try { var t = Text(e, max); return t == null ? null : Json.Parse(t); } catch (Exception) { return null; } }

            // owners first (O1-O4), then the specific rules override
            foreach (var e in data)
            {
                var p = e.ImagePath;
                if (In(p, "/clover")) Set(e, Category.SystemFiles, "In Nintendo's own data folder (its kind is not recognized more precisely).");
                else if (In(p, "/hakchi")) Set(e, Category.ModsAndEmulators, "In hakchi's folder (the modification software; its kind is not recognized more precisely).");
                else if (In(p, "/ccs")) Set(e, Category.ModsAndEmulators, "In Classic Cart Studio's folder (its kind is not recognized more precisely).");
                else if (In(p, "/lost+found")) Set(e, Category.SystemFiles, "The file system's own recovery folder.");
                else Set(e, Category.EverythingElse, "No rule recognizes it.");
            }
            // N2, H1, H2, C1, C4
            foreach (var e in data)
            {
                var p = e.ImagePath;
                if (In(p, "/clover/profiles/0/home-menu")) Set(e, Category.Settings, "The console's own HOME menu settings.");
                else if (p == "/hakchi/config" && e.Kind == EntryKind.File) Set(e, Category.Settings, "hakchi's settings.");
                else if (In(p, "/hakchi/hmod")) Set(e, Category.ModsAndEmulators, "hakchi's record of an installed mod.");
                else if (In(p, "/hakchi/rootfs")) Set(e, Category.ModsAndEmulators, "Installed by a mod (hakchi's overlay of the system).");
                else if (p == "/ccs/pkg/current" && e.Kind == EntryKind.File) Set(e, Category.Settings, "Which Classic Cart Studio HOME the console uses.");
                else if (In(p, "/ccs/data/state") || In(p, "/ccs/data/game-settings")) Set(e, Category.Settings, "Classic Cart Studio's HOME and player settings.");
            }

            // H3 hakchi game folders (outer folders first, so a folder inside another game's folder wins for its own files)
            var saveLinks = new Dictionary<string, string>(StringComparer.Ordinal);   // save folder name -> game key (from Path=)
            var gameDirs = data.Where(d => d.Kind == EntryKind.Folder && d.ImagePath.StartsWith("/hakchi/games/", StringComparison.Ordinal)
                                      && Kids(d.ImagePath).Any(k => k.Kind == EntryKind.File && k.Name == d.Name + ".desktop")).OrderBy(d => Depth(d.ImagePath)).ThenBy(d => d.ImagePath, StringComparer.Ordinal).ToList();
            foreach (var d in gameDirs)
            {
                var desk = Kids(d.ImagePath).First(k => k.Kind == EntryKind.File && k.Name == d.Name + ".desktop");
                var f = Desktop(Text(desk, SmallText));
                var code = f.TryGetValue("Code", out var c0) && c0.Length > 0 ? c0 : d.Name;
                var key = "code:" + code; var g = G(key); g.Code = code; g.Hakchi = true;
                if (f.TryGetValue("Name", out var nm) && nm.Trim().Length > 0) g.Titles.Add((nm.Trim(), "the game's hakchi entry (" + desk.ConsolePath + ")"));
                var console = "/var/lib" + d.ImagePath;
                string Local(string v)   // a value naming a file of this folder: /var/games/<folder>/<file>, or the folder's own console path
                {
                    if (string.IsNullOrEmpty(v)) return null;
                    foreach (var pre in new[] { "/var/games/" + d.Name + "/", console + "/" })
                        if (v.StartsWith(pre, StringComparison.Ordinal) && v.Length > pre.Length && v.IndexOf('/', pre.Length) < 0) return v.Substring(pre.Length);
                    return null;
                }
                var execFiles = new HashSet<string>(StringComparer.Ordinal);
                if (f.TryGetValue("Exec", out var ex)) foreach (var t in ex.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)) { var n = Local(t.Trim('"', '\'')); if (n != null) execFiles.Add(n); }
                var icon = f.TryGetValue("Icon", out var ic) ? Local(ic.Trim()) : null;
                var art = new HashSet<string>(StringComparer.Ordinal) { code + ".png", code + "_small.png", code + "_original.png", code + "_spine.png", code + "_logo.png" };
                var mdmini = SegaPictures.NamesFor(code, icon);
                foreach (var e in Under(d.ImagePath))
                {
                    var rel = e.ImagePath.Substring(d.ImagePath.Length + 1); bool direct = rel.IndexOf('/') < 0;
                    if (e == desk) { Set(e, Category.Games, "The game's hakchi entry (its name, how it starts, its picture).", key, "game-entry", -1, rel); continue; }
                    if (direct && e.Kind == EntryKind.File)
                    {
                        if (execFiles.Contains(e.Name)) { Set(e, Category.Games, "The game file its hakchi entry starts.", key, "game-file", -1, rel); continue; }
                        var h = head(e, 4);
                        if (h != null && h.Length == 4 && (h[0] == 'N' && h[1] == 'E' && h[2] == 'S' || h[0] == 'F' && h[1] == 'D' && h[2] == 'S') && h[3] == 0x1A) { Set(e, Category.Games, "An NES game file (it starts with the NES game file header).", key, "game-file", -1, rel); continue; }
                        if (icon == e.Name) { Set(e, Category.Artwork, "The game's picture (its hakchi entry shows it).", key, "box-art", -1, rel); continue; }
                        if (mdmini.Contains(e.Name)) { Set(e, Category.Artwork, "A picture for the Sega Mini menu that Hakchi2 CE makes from the game's picture. " + SegaPictures.Explanation, key, SegaPictures.Role, -1, rel); continue; }
                        if (art.Contains(e.Name)) { Set(e, Category.Artwork, "One of the game's pictures (hakchi's own picture names).", key, "artwork", -1, rel); continue; }
                    }
                    Set(e, Category.EverythingElse, "In this game's hakchi folder; its kind is not recognized.", key, "other", -1, rel);
                }
                Set(d, Category.Games, "The game's hakchi folder.", key, "folder");
                if (f.TryGetValue("Path", out var pth))
                {
                    var s = Regex.Replace(pth.Trim(), "/{2,}", "/");
                    foreach (var pre in new[] { "/var/saves/", "/var/lib/clover/profiles/0/" })
                        if (s.StartsWith(pre, StringComparison.Ordinal) && s.Length > pre.Length && s.IndexOf('/', pre.Length) < 0) { saveLinks[s.Substring(pre.Length)] = key; break; }
                }
            }

            // N1 save folders, N3 names
            foreach (var d in data.Where(x => x.Kind == EntryKind.Folder && SaveFolderRe.IsMatch(x.ImagePath)).ToList())
            {
                var name = d.Name; var key = saveLinks.TryGetValue(name, out var k) ? k : "code:" + name; var g = G(key); if (g.Code == null) g.Code = name;
                Set(d, Category.Saves, "This game's save folder.", key, "folder");
                foreach (var e in Under(d.ImagePath))
                {
                    var rel = e.ImagePath.Substring(d.ImagePath.Length + 1); var top = rel.Split('/')[0]; bool direct = top == rel;
                    Match m;
                    if (direct && e.Kind == EntryKind.File && (e.Name == "cartridge.sram" || e.Name == "save.sram")) Set(e, Category.Saves, "The game's battery save.", key, "battery-save", -1, rel);
                    else if (direct && e.Kind == EntryKind.File && (m = StateRe.Match(e.Name)).Success) Set(e, Category.SuspendPoints, "A suspend point (set " + m.Groups[1].Value + ").", key, "suspend-point", int.Parse(m.Groups[1].Value), e.Name);
                    else if ((m = SuspendRe.Match(top)).Success && Kids(d.ImagePath).Any(x => x.Name == top && x.Kind == EntryKind.Folder))
                    {
                        if (direct) Set(e, Category.SuspendPoints, "A suspend point's folder (set " + m.Groups[1].Value + ").", key, "folder", int.Parse(m.Groups[1].Value));
                        else Set(e, Category.SuspendPoints, "Part of suspend point set " + m.Groups[1].Value + " (its files stay together).", key, "suspend-point", int.Parse(m.Groups[1].Value), rel.Substring(top.Length + 1));
                    }
                    else Set(e, Category.EverythingElse, "In this game's save folder; its kind is not recognized.", key, "other", -1, rel);
                }
            }

            // C2 HOME package copies
            var idToKey = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var d in Kids("/ccs/pkg").Where(x => x.Kind == EntryKind.Folder && CopyRe.IsMatch(x.Name)))
            {
                var root = d.ImagePath;
                var man = At(root + "/MANIFEST.json"); var sums = At(root + "/SHA256SUMS");
                Set(man, Category.ModsAndEmulators, "The HOME package's own list of its files."); Set(sums, Category.ModsAndEmulators, "The HOME package's own list of its checksums.");
                var mj = JsonOf(man, BigText);
                if (mj?.S("schema") == "ccs-console-package/1" && mj.O("files") is Dictionary<string, object> files)
                {
                    Dictionary<string, object> homeFiles = null;
                    foreach (var kv in files)
                    {
                        var e = At(root + "/" + kv.Key); if (e == null || e.Kind != EntryKind.File) continue;
                        var kind = (kv.Value as Dictionary<string, object>).S("kind");
                        switch (kind)
                        {
                            case "rom":
                                var pm = PkgRomRe.Match(kv.Key);
                                if (pm.Success) { var key = "ccs:" + pm.Groups[1].Value; G(key).Sha = pm.Groups[1].Value; Set(e, Category.Games, "A game file the HOME package lists (named by the game's identity).", key, "game-file", -1, e.Name); }
                                else Set(e, Category.Games, "A game file the HOME package lists.");
                                break;
                            case "startup-image": Set(e, Category.Artwork, "A startup picture the HOME package lists."); break;
                            case "configuration": Set(e, Category.Settings, "A setting the HOME package lists."); break;
                            case "executable": Set(e, Category.ModsAndEmulators, "Classic Cart Studio's game player."); break;
                            case "font": Set(e, Category.ModsAndEmulators, "Lettering the game player uses."); break;
                            case "license": Set(e, Category.ModsAndEmulators, "The HOME package's notice of its parts, or a license text it carries."); break;
                            case "home":
                                if (homeFiles == null) homeFiles = JsonOf(At(root + "/home/manifest.json"), BigText)?.O("files") ?? new Dictionary<string, object>();
                                var t = homeFiles.TryGetValue(kv.Key.StartsWith("home/", StringComparison.Ordinal) ? kv.Key.Substring(5) : kv.Key, out var hv) ? (hv as Dictionary<string, object>).S("type") ?? "" : "";
                                if (t.EndsWith("-jpeg", StringComparison.Ordinal) || t.EndsWith("-png", StringComparison.Ordinal)) Set(e, Category.Artwork, "A picture of Classic Cart Studio's HOME (its own list says so).");
                                else Set(e, Category.ModsAndEmulators, "Part of Classic Cart Studio's HOME.");
                                break;
                        }
                    }
                }
                var sj = At(root + "/session.json"); var sess = JsonOf(sj, BigText);
                if (sess != null)
                {
                    Set(sj, Category.Settings, "The HOME's game list and where its saves go.");
                    foreach (var gm in (sess.A("games") ?? new List<object>()).OfType<Dictionary<string, object>>())
                    {
                        var rom = gm.S("rom") ?? ""; var cs = gm.S("content_sha256") ?? ""; var id = gm.S("id") ?? "";
                        if (!Hex64.IsMatch(cs)) continue;
                        var key = "ccs:" + cs; if (id.Length > 0) idToKey[id] = key;
                        var g = G(key); g.Sha = cs; var title = (gm.S("title") ?? "").Trim(); if (title.Length > 0) g.Titles.Add((title.Length > 200 ? title.Substring(0, 200) : title, "the HOME's game list (" + sj.ConsolePath + ")"));
                        if (!rom.StartsWith(Ext4.CcsExport.PkgOnConsole, StringComparison.Ordinal)) continue;
                        var rel = rom.Substring(Ext4.CcsExport.PkgOnConsole.Length);
                        if (!Regex.IsMatch(rel, "^roms/[^/]+$")) continue;
                        var e = At(root + "/" + rel);
                        if (e != null && e.Kind == EntryKind.File) Set(e, Category.Games, "A game file the HOME's game list names.", key, "game-file", -1, e.Name);
                    }
                }
                var st = At(root + "/startup/startup.json"); var sjs = JsonOf(st, SmallText * 16);
                if (sjs != null && sjs.S("schema") == "ccs-startup/1")
                {
                    Set(st, Category.Settings, "The HOME's startup screen setting.");
                    foreach (var im in (sjs.A("images") ?? new List<object>()).OfType<Dictionary<string, object>>())
                    {
                        var fn = im.S("file") ?? ""; if (fn.Length == 0 || fn.Contains("/") || fn == "." || fn == "..") continue;
                        var e = At(root + "/startup/" + fn); if (e != null && e.Kind == EntryKind.File) Set(e, Category.Artwork, "A startup picture the startup setting lists.");
                    }
                }
                var gj = JsonOf(At(root + "/home/games.json"), BigText);
                foreach (var gm in (gj?.A("games") ?? new List<object>()).OfType<Dictionary<string, object>>())
                {
                    var cov = gm.S("cover") ?? ""; var id = gm.S("id") ?? "";
                    if (cov.Length == 0 || cov.Split('/').Any(s => s.Length == 0 || s == "." || s == "..")) continue;
                    var e = At(root + "/home/" + cov); if (e == null || e.Kind != EntryKind.File) continue;
                    if (idToKey.TryGetValue(id, out var key)) Set(e, Category.Artwork, "The game's cover in the HOME.", key, "box-art", -1, e.Name);
                    else Set(e, Category.Artwork, "A game cover in the HOME.");
                }
            }

            // C3 Classic Cart Studio's saves
            foreach (var d in Kids("/ccs/data/saves").Where(x => x.Kind == EntryKind.Folder && Hex64.IsMatch(x.Name)))
            {
                var key = "ccs:" + d.Name; var g = G(key); g.Sha = d.Name;
                Set(d, Category.Saves, "This game's save folder (named by the game's identity).", key, "folder");
                foreach (var e in Under(d.ImagePath))
                {
                    var rel = e.ImagePath.Substring(d.ImagePath.Length + 1); bool file = rel.IndexOf('/') < 0 && e.Kind == EntryKind.File; Match m;
                    if (file && e.Name == "cart.sav") Set(e, Category.Saves, "The game's cartridge save (battery-backed memory, or flash memory on a cartridge that saves to flash).", key, "battery-save", -1, rel);
                    else if (file && (m = SlotRe.Match(e.Name)).Success) Set(e, Category.SuspendPoints, "A suspend point (save slot " + m.Groups[1].Value + ").", key, "suspend-point", int.Parse(m.Groups[1].Value), rel);
                    else if (file && e.Name == "continue.ccsstate") Set(e, Category.SuspendPoints, "An automatic continuation kept from an older version.", key, "suspend-point", 0, rel);
                    else if (file && (e.Name == "last-used.json" || e.Name == "slot-names.json" || e.Name == "game.json"))
                    {
                        Set(e, Category.Settings, "Details of the game's saves.", key, "save-details", -1, rel);
                        if (e.Name == "game.json") { var t = JsonOf(e, SmallText)?.S("last_title")?.Trim(); if (!string.IsNullOrEmpty(t)) g.Titles.Add((t.Length > 200 ? t.Substring(0, 200) : t, "the last name it was played under (" + e.ConsolePath + ")")); }
                    }
                    else if (file && e.Name == "session.lock") Set(e, Category.SystemFiles, "The game player's lock while the game is open.", key, "other", -1, rel);
                    else Set(e, Category.EverythingElse, "In this game's save folder; its kind is not recognized.", key, "other", -1, rel);
                }
            }

            // the groups: only games that have entries; a game without a known name keeps its code or identity, never a guess
            var groups = new List<GameGroup>();
            var byKey = data.Where(e => e.GameKey != null).GroupBy(e => e.GameKey).ToDictionary(x => x.Key, x => x.ToList());
            foreach (var g in games.Values)
            {
                if (!byKey.TryGetValue(g.Key, out var es)) continue;
                var gg = new GameGroup { Key = g.Key, Code = g.Code, ContentSha256 = g.Sha };
                var titles = g.Titles.Where(t => t.title.Length > 0).ToList();
                if (titles.Count == 0 && g.Code != null && StockGames.TryGetValue(g.Code, out var stock)) titles.Add((stock, "the " + StockConsole(g.Code) + "'s own game list"));
                if (titles.Count > 0)
                {
                    gg.Title = titles[0].title; gg.TitleSource = titles[0].source; gg.TitleKnown = true;
                    gg.OtherTitles.AddRange(titles.Select(t => t.title).Distinct(StringComparer.Ordinal).Where(t => t != gg.Title));
                }
                else { gg.Title = g.Code ?? ("Unknown game " + (g.Sha ?? "").Substring(0, Math.Min(16, (g.Sha ?? "").Length))); gg.TitleSource = "no name is recorded for it"; }
                gg.Kind = g.Key.StartsWith("ccs:", StringComparison.Ordinal) ? "Classic Cart Studio game" : g.Hakchi ? "Game added with hakchi" : g.Code != null && StockGames.ContainsKey(g.Code) ? StockConsole(g.Code) + " original game" : "Game known only by its save folder";
                gg.Entries.AddRange(es.Where(e => e.Kind != EntryKind.Folder).OrderBy(e => e.ImagePath, StringComparer.Ordinal));
                gg.BoxArt = gg.Entries.FirstOrDefault(e => e.Role == "box-art") ?? gg.Entries.FirstOrDefault(e => e.Category == Category.Artwork);
                if (gg.Entries.Count > 0) groups.Add(gg);
            }
            return groups.OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
        }

        static bool In(string p, string dir) => p == dir || p.StartsWith(dir + "/", StringComparison.Ordinal);
        static string Parent(string p) { int i = p.LastIndexOf('/'); return i <= 0 ? "" : p.Substring(0, i); }
        static int Depth(string p) => p.Count(c => c == '/');

        /// <summary>the first value of each key in a .desktop file (all sections; hakchi writes each key once)</summary>
        public static Dictionary<string, string> Desktop(string text)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (text == null) return d;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r'); int eq = line.IndexOf('=');
                if (eq <= 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("[", StringComparison.Ordinal)) continue;
                var k = line.Substring(0, eq).Trim(); if (!d.ContainsKey(k)) d[k] = line.Substring(eq + 1);
            }
            return d;
        }
    }
}
