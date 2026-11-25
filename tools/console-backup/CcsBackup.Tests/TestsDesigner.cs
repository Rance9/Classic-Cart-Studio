// Classic Cart Studio console backup and restore engine tests: the HOME Designer's compiler, the render check on the native HOME, the
// apply with its journal and fallback, and Revert Design. GPL-3.0-or-later. Simulated consoles only (changes through
// debugfs in WSL). Needs the native HOME renderer (CCS_HOME_RENDERER: ccs-sim-core.exe) and a native HOME package (CCS_DESIGNER_PACK);
// without them the group is SKIPPED (a skip is never a pass). The Python cross-checks need --python and CCS_COMPANION_DIR.
using CcsBackup.Archive;
using CcsBackup.Compose;
using CcsBackup.Device;
using CcsBackup.Engine;
using CcsBackup.Ext4;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static CcsBackup.Tests.Program;
using CSha = CcsBackup.Compose.Sha;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        /// <summary>a picture of one color with a soft diagonal shade (BGRA), as a canonical PNG for the given slot</summary>
        static byte[] Shade(int w, int h, int r, int g, int b, bool alpha = false, int a = 255)
        {
            var px = new byte[w * h * 4];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { int i = (y * w + x) * 4, k = (x + y) * 24 / (w + h); px[i] = (byte)Math.Min(255, b + k); px[i + 1] = (byte)Math.Min(255, g + k); px[i + 2] = (byte)Math.Min(255, r + k); px[i + 3] = (byte)(alpha ? a : 255); }
            return ImageRules.EncodePng(w, h, px, alpha);
        }
        static List<ConsoleGame> PackGames(string pack) =>
            Json.Parse(File.ReadAllText(Path.Combine(pack, "games.json"), Encoding.UTF8)).A("games").OfType<Dictionary<string, object>>().Select(g => new ConsoleGame { Id = g.S("id"), Title = g.S("title") }).ToList();

        static HomeDesign DesignFor(List<ConsoleGame> games, string name, (int r, int g, int b) sky, bool bright = false)
        {
            var d = HomeDesign.New(name, "arcade-grid");
            d.SetSlot("scene", ImageNormalizer.Normalize(bright ? Shade(1280, 720, 235, 235, 225) : Shade(1280, 720, sky.r, sky.g, sky.b), DesignProfile.Slots["scene"]));
            d.SetSlot("details-backdrop", ImageNormalizer.Normalize(Shade(1272, 712, sky.r / 2, sky.g / 2, sky.b / 2), DesignProfile.Slots["details-backdrop"]));
            d.SetSlot("plate", Shade(1200, 60, 0, 0, 0, true, 90));
            d.SetSlot("icon-star", Shade(64, 64, 250, 200, 40, true, 255));
            d.SetStartup("image", null, ImageNormalizer.Normalize(Shade(1280, 720, 30, 20, 60), DesignProfile.Slots["startup"]));
            var art = d.AddImage(ImageNormalizer.Normalize(Shade(544, 148, 80, 30, 120), DesignProfile.Slots["folder-art"]));
            d.UpsertRegion("hello", r => { r["kind"] = "label"; r["x"] = 1000L; r["y"] = 14L; r["w"] = 260L; r["h"] = 28L; r["text"] = "Game Night"; r["font"] = "sans700_20"; r["color"] = "#ffcc00"; r["align"] = "right"; r["layer"] = 3L; });
            d.UpsertRegion("band", r => { r["kind"] = "panel"; r["x"] = 1000L; r["y"] = 12L; r["w"] = 268L; r["h"] = 32L; r["color"] = "#000000"; r["opacity"] = 40L; r["layer"] = 1L; });
            d.SetAppearance("accent_color", "#0050c8"); d.SetAppearance("panel_color", "#101828"); d.SetAppearance("panel_opacity", 95); d.SetAppearance("text_color", "#f0f0f0");
            d.SetSetting("reduced_motion", true);
            var refs = games.Take(3).Select(g => new GameRef { Catalog = g.Id, Title = g.Title }).Concat(new[] { new GameRef { Placeholder = 1 }, new GameRef { Catalog = "fedcba9876543210", Title = "Not Here" } }).ToList();
            d.UpsertFolder("puzzles", f => { f["name"] = "Puzzle Night"; f["parent"] = null; f["pos"] = 0L; f["art"] = art; f["games"] = refs.Select(x => (object)x.ToJson()).ToList(); });
            d.UpsertFolder("late", f => { f["name"] = "Late Night"; f["parent"] = "puzzles"; f["pos"] = 0L; f["games"] = new List<object> { new GameRef { Catalog = games[5].Id, Title = games[5].Title }.ToJson() }; });
            d.UpsertFolder("empty", f => { f["name"] = "To Sort Later"; f["parent"] = null; f["pos"] = 1L; });
            d.SetOrder(games.Take(10).Reverse().Select(g => new GameRef { Catalog = g.Id, Title = g.Title }));
            d.SetMapShortcuts("puzzles", null);
            return d;
        }

        /// <summary>the console's design folder as it is on the simulated console's data storage, copied out for a cold start</summary>
        static string DesignRootOf(string console, string into)
        {
            Directory.CreateDirectory(into);
            using (var f = new FileStream(SimulatedConsole.Dev(console, "nandc"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var fs = new Ext4Reader(f); var pre = "/" + DesignApplyEngine.Root + "/";
                foreach (var e in fs.List().Where(x => x.Type == 'f' && x.Path.StartsWith(pre, StringComparison.Ordinal)))
                {
                    var dst = Path.Combine(into, e.Path.Substring(pre.Length).Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.WriteAllBytes(dst, fs.ReadAll(fs.Lookup(e.Path).Value, 64L * 1000 * 1000));
                }
            }
            return into;
        }
        static Dictionary<string, object> Boot(string exe, string pack, string root)
        {
            var w = NewDir("boot-state"); var (code, o) = Run(exe, "--design-boot --pack \"" + pack + "\" --design-root \"" + root + "\" --work \"" + w + "\"");
            var line = o.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("{"));
            return line == null ? new Dictionary<string, object> { { "error", o } } : Json.Parse(line);
        }

        public static void Designer()
        {
            var exe = Environment.GetEnvironmentVariable("CCS_HOME_RENDERER"); var pack = Environment.GetEnvironmentVariable("CCS_DESIGNER_PACK");
            if (exe == null || !File.Exists(exe)) throw new SkipException("no native HOME renderer was given (CCS_HOME_RENDERER: ccs-sim-core.exe)");
            if (pack == null || !File.Exists(Path.Combine(pack, "pack.json")) || !File.Exists(Path.Combine(pack, "manifest.json"))) throw new SkipException("no native HOME package was given (CCS_DESIGNER_PACK)");
            var dir = NewDir("designer"); var games = PackGames(pack); var renderer = new ProcessHomeRenderer(exe);
            Check(HomeRenderNative.Available == false && HomeRenderNative.UnavailableReason != null && HomeRenderers.Find(exe) is ProcessHomeRenderer,
                "outside the standalone Workbench's native host the renderer is the HOME program on this PC (" + HomeRenderNative.UnavailableReason + ")");

            // ---- the one compiler: deterministic, every file by its SHA-256, games resolved to this console's own ids
            var d = DesignFor(games, "Neon Night", (14, 18, 40));
            var ph = new Dictionary<int, string> { [1] = games[4].Id };
            var c1 = DesignCompiler.Compile(d, games, ph); var c2 = DesignCompiler.Compile(HomeDesign.Open(SaveDesign(d, dir, "neon.ccshome")), games, ph);
            Check(c1.Id == c2.Id && c1.Manifest.Count == c2.Manifest.Count && c1.Manifest.Zip(c2.Manifest, (a, b) => a.Name == b.Name && a.Sha256 == b.Sha256 && a.Bytes == b.Bytes).All(x => x) && c1.Id == CSha.Of(c1.DesignJson),
                "compiling the same design twice (also after a save and an open) gives the same bytes: id " + c1.Id.Substring(0, 12) + ", " + c1.Manifest.Count + " files, " + CcsBackup.Compose.Units.Size(c1.FileBytes));
            var dj = Json.Parse(Encoding.UTF8.GetString(c1.DesignJson)); var fl = dj.A("folders").OfType<Dictionary<string, object>>().ToList();
            Check(fl.Count == 3 && fl[0].S("name") == "Puzzle Night" && fl[1].S("name") == "Late Night" && fl[1].L("parent") == 0 && fl[2].S("name") == "To Sort Later"
                  && fl[0].A("games").OfType<string>().SequenceEqual(games.Take(3).Select(g => g.Id).Concat(new[] { games[4].Id })) && c1.LeftOut.Count == 1 && c1.LeftOut[0].Contains("Not Here"),
                "folders compiled in display order with their nesting; games by the console's ids; the placeholder as mapped; a game this console lacks left out and named (" + string.Join("; ", c1.LeftOut) + ")");
            Check(dj.A("order").OfType<string>().SequenceEqual(games.Take(10).Reverse().Select(g => g.Id)) && dj.O("startup").S("mode") == "image" && dj.A("layers").OfType<Dictionary<string, object>>().Select(l => l.S("kind")).SequenceEqual(new[] { "panel", "label" }),
                "the game order; the startup picture; decorations in drawing order (by layer: the panel under the label)");
            Check(Code(() => DesignCompiler.Compile(HomeDesign.Open(DesignFile(WithUi(d.Document), FilesOf(d))), games)) == "design.not-applicable", "a design the console HOME cannot show (its controls at sizes the theme does not have) is refused by the compiler");
            var light = d.Document; ((JObj)light["appearance"])["accent_color"] = "#00a0ff"; var lo = HomeDesign.Open(DesignFile(light, FilesOf(d)));
            Check(!lo.CanApply && lo.Unsupported.Any(u => u.Contains("too light")), "a focus color too light for the white lettering the HOME draws on it is viewable, not applicable (" + lo.Unsupported.FirstOrDefault() + ")");
            var cd = Path.Combine(dir, "compiled"); c1.WriteTo(cd); var back = CompiledDesign.Read(cd);
            var tamper = Path.Combine(dir, "compiled-tampered"); CopyTree(cd, tamper); var img = Directory.GetFiles(Path.Combine(tamper, "img"))[0]; var ib = File.ReadAllBytes(img); ib[ib.Length / 2] ^= 0x41; File.WriteAllBytes(img, ib);
            Check(back.Id == c1.Id && Code(() => CompiledDesign.Read(tamper)) == "compiled.changed", "written and read back the same; a changed picture is refused (compiled.changed)");

            // ---- the Python side takes these bytes as they are: the package builder's ingest, the .ccs carriage (studio page too)
            var ccshome = Path.Combine(dir, "neon.ccshome"); var carried = Path.Combine(dir, "carried.ccshome");
            var py = Python != null && File.Exists(Python) ? Environment.GetEnvironmentVariable("CCS_COMPANION_DIR") : null;
            if (py == null) Console.WriteLine("SKIP python cross-checks (no --python or CCS_COMPANION_DIR)");
            else
            {
                Environment.SetEnvironmentVariable("CCS_TEST_COMPILED", cd); Environment.SetEnvironmentVariable("CCS_TEST_CCSHOME", ccshome); Environment.SetEnvironmentVariable("CCS_TEST_CARRIED_OUT", carried);
                // the Windows app's way: a carried design that moves the HOME's controls, compiled by the host program (--design-compile) for six
                // of the HOME's games, against the same compiled here; a design the console cannot show; the console game player for both methods
                var six = games.Take(6).ToList(); var mv = MovedDesign("arcade-grid");
                mv.SetOrder(six.Take(3).Reverse().Select(g => new GameRef { Catalog = g.Id, Title = g.Title }));
                mv.UpsertFolder("three", f => { f["name"] = "Three Games"; f["parent"] = null; f["pos"] = 0L; f["games"] = six.Skip(3).Select(g => (object)new GameRef { Catalog = g.Id, Title = g.Title }.ToJson()).ToList(); });
                var mvFile = SaveDesign(mv, dir, "moved.ccshome"); var mvc = DesignCompiler.Compile(mv, six); var mvDir = Path.Combine(dir, "moved-compiled"); mvc.WriteTo(mvDir);
                var gj = Path.Combine(dir, "six-games.json"); File.WriteAllBytes(gj, Json.Bytes(new JObj { { "games", six.Select(g => (object)new JObj { { "id", g.Id }, { "title", g.Title } }).ToList() } }));
                var refused = Path.Combine(dir, "refused.ccshome"); File.WriteAllBytes(refused, DesignFile(WithUi(d.Document), FilesOf(d)));
                var nd = d.Document; nd["requires"] = new List<object> { "home-design/9" }; var newer = Path.Combine(dir, "newer.ccshome"); File.WriteAllBytes(newer, DesignFile(nd, FilesOf(d)));
                Environment.SetEnvironmentVariable("CCS_TEST_CCSHOME_NEWER", newer);
                Environment.SetEnvironmentVariable("CCS_TEST_CCSHOME_MOVED", mvFile); Environment.SetEnvironmentVariable("CCS_TEST_GAMES", gj); Environment.SetEnvironmentVariable("CCS_TEST_COMPILED_MOVED", mvDir); Environment.SetEnvironmentVariable("CCS_TEST_CCSHOME_REFUSED", refused);
                var comp = Environment.GetEnvironmentVariable("CCS_DESIGN_COMPILER");
                if (comp == null || !File.Exists(comp)) Console.WriteLine("NOTE the app's compiler cross-check needs CCS_DESIGN_COMPILER (a host built from this source)");
                var (rc, o) = Run(Python, "\"" + Path.Combine(py, "test_home_design_carry.py") + "\"", null, 1800000);
                Check(rc == 0 && o.Contains("RESULT 8/8 passed, 0 skipped"), "Python: the .ccs carriage keeps the Workbench's design byte for byte through a change of the theme (and the startup picture with it), the studio page keeps it and its unknown members, the package builder's ingest takes the compiler's files as they are, and the app compiles a carried design through the host program's --design-compile to the Workbench's exact bytes, refuses one the console cannot show in words, and carries it into CCS Mod's and CCS Nucleus's console packages alike (" + o.Split('\n').LastOrDefault(l => l.StartsWith("RESULT"))?.Trim() + ")" + (rc == 0 ? "" : ": " + Display.Line(string.Join(" | ", o.Split('\n').Where(l => l.Contains("FAIL") || l.Contains("Error") || l.Contains("skipped '")).Take(6)), 600)));
                Check(File.Exists(carried) && File.ReadAllBytes(carried).SequenceEqual(File.ReadAllBytes(ccshome)) && HomeDesign.Open(carried).ContentSha256 == d.ContentSha256, "the design carried through two .ccs files opens here as the same design");
                var script = "import sys, os, json, hashlib\nsys.path.insert(0, os.environ['CCS_COMPANION_DIR']); sys.path.insert(0, os.path.join(os.environ['CCS_COMPANION_DIR'], '..', 'rom-pipeline'))\n" +
                             "import test_native_home_package as T\nfrom native_home import package as P\n" +
                             "ids = [g for g in json.load(open(os.path.join(sys.argv[1], 'games.json'), encoding='utf-8'))['games']][:6]\n" +
                             "man, home = T.studio_config([g['id'] for g in ids]); home['design_compiled_dir'] = sys.argv[2]\n" +
                             "r = P.build(man, home, mode='preview', root=sys.argv[3], cache_root=os.environ.get('CCS_TEST_PKCACHE'))\n" +
                             "d = os.path.join(r['package_dir'], 'design'); out = {}\n" +
                             "for root, _, fs in os.walk(d):\n    for f in fs: p = os.path.join(root, f); out[os.path.relpath(p, d).replace(os.sep, '/')] = hashlib.sha256(open(p, 'rb').read()).hexdigest()\n" +
                             "pk = json.load(open(os.path.join(r['package_dir'], 'pack.json'), encoding='utf-8'))\nprint(json.dumps({'ok': r['ok'], 'files': out, 'design': pk.get('design', {}).get('id')}))\n";
                var sp = Path.Combine(Work, "pkdesign-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".py"); File.WriteAllText(sp, script);
                var (prc, po) = Run(Python, "\"" + sp + "\" \"" + pack + "\" \"" + cd + "\" \"" + Path.Combine(dir, "pkroot") + "\"", null, 1800000);
                var pl = po.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("{\"ok\""));
                var pj = pl == null ? null : Json.Parse(pl); var pf = pj?.O("files");
                Check(prc == 0 && pj != null && pj.B("ok") && pj.S("design") == c1.Id && pf != null && pf.Count == c1.Manifest.Count && c1.Manifest.All(m => pf.S(m.Name) == m.Sha256),
                    "the Python HOME package builder carries the compiled design byte for byte (design/, " + (pf?.Count ?? 0) + " files, the same SHA-256 each)" + (pl == null ? ": " + Display.Line(po, 300) : ""));
            }

            // ---- the render check: every theme and state on the native HOME, with and without the design
            var rr = renderer.Check(pack, cd, new RenderCheckOptions { Frames = true });
            var stateNames = rr.States.Select(s => s.State).Distinct().ToList();
            Check(rr.Ok && rr.DesignId == c1.Id && rr.States.Count >= 50 && rr.States.Select(s => s.Theme).Distinct().Count() == 4 && new[] { "all-games", "long-title", "details", "details-long-title", "menu", "menu-focus", "theme-picker", "browse", "browse-focus", "folder", "folder-empty", "navigation-focus", "notice" }.All(stateNames.Contains),
                "the render check passes: " + rr.States.Count + " screens in the four themes (All Games, a long title, Details, the menu and its focus, the theme picker, Browse, a folder, an empty folder, the navigation focus, a notice)" + (rr.Ok ? "" : ": " + string.Join(" | ", rr.Problems.Take(4))));
            var arcade = rr.States.Where(s => s.Theme == "arcade-grid").ToList(); var others = rr.States.Where(s => s.Theme == "box-art-showcase").ToList();
            Check(arcade.All(s => s.DesignSha256 != s.DefaultSha256) && rr.FallbackSameAsDefaults && rr.StartupMode == "image" && rr.StartupPeakBytes > 0,
                "the design draws in its own theme (every Arcade Grid screen differs from the theme as it comes); with a picture missing it falls back exactly to the theme defaults; the startup picture decodes at 1280x720");
            var est = d.Budget();
            Check(rr.AddedPeakBytes <= DesignProfile.ActiveDecodedBudget && est.PeakDecodedBytes >= rr.AddedPeakBytes,
                "memory: the design adds " + CcsBackup.Compose.Units.Size(rr.AddedPeakBytes) + " of decoded pictures at its worst moment (measured on the native HOME; at most " + CcsBackup.Compose.Units.Size(DesignProfile.ActiveDecodedBudget) + "); the editor's estimate " + CcsBackup.Compose.Units.Size(est.PeakDecodedBytes) + " is not below it");
            var fr = rr.Frames.FirstOrDefault(f => f.WithDesign && f.Theme == "arcade-grid" && f.State == "all-games"); var png = fr?.Png();
            Check(rr.Frames.Count(f => f.WithDesign) >= rr.States.Count && fr != null && ImageRules.Inspect(png, 1280, 1280 * 720).Canonical && rr.Frames.Any(f => f.Theme == "startup"),
                "the editor's preview frames: " + rr.Frames.Count + " screens as drawn by the console HOME (" + CcsBackup.Compose.Units.Size(png?.Length ?? 0) + " as PNG for All Games), the startup picture too");
            var bright = DesignCompiler.Compile(DesignFor(games, "Snow Blind", (14, 18, 40), true), games, ph); var bd = Path.Combine(dir, "bright"); bright.WriteTo(bd);
            var br = renderer.Check(pack, bd, new RenderCheckOptions { Frames = false });
            Check(!br.Ok && br.Problems.Any(p => p.Contains("hard to read")), "a bright background under the HOME's white text fails the check, with the text named (" + br.Problems.FirstOrDefault(p => p.Contains("hard to read")) + ")");
            // ---- the HOME's own controls moved in bounded groups, in every theme, on the native HOME
            DesignerRegions(renderer, pack, dir, games);
            // ---- the theme's sprites over a design's own background: their own pixels only
            DesignerOwnSprites(renderer, pack, dir, games);

            // ---- applying on a (simulated) console: strict policy, journal, fallback at a cold start, Revert Design keeps newer saves
            SimulatedConsole.FileStore = new DebugfsFileStore();
            try
            {
                var s = SettingsIn(NewDir("designer-records"), false); var con = Console_("modified"); const string copy = "0123456789abcdef.01234567";
                SimulatedConsole.FileStore.Write(con, "ccs/pkg/current", Encoding.ASCII.GetBytes(copy + "\n"));
                SimulatedConsole.FileStore.Write(con, "ccs/pkg/" + copy + "/home/manifest.json", File.ReadAllBytes(Path.Combine(pack, "manifest.json")));
                const string save = "ccs/data/saves/0123456789abcdef/cart.sav"; SimulatedConsole.FileStore.Write(con, save, Encoding.ASCII.GetBytes("a save from before"));
                var rc0 = RecoverySessions.OpenSimulated(con, ImageKind.Files); var ctx = Ctx(s, ImageKind.Files);
                var (pa, wa) = DesignApplyEngine.PlanApply(rc0, ctx, c1, renderer, pack);
                Check(pa != null && pa.Refusal == null && pa.Protective?.Session == rc0.SessionId && pa.RenderCheckSha256 != null && pa.Changes.Count(x => x.Path.StartsWith(DesignApplyEngine.Folder(c1.Id))) == c1.Manifest.Count
                      && pa.Changes.Select(x => x.Path).SequenceEqual(c1.Manifest.Select(m => DesignApplyEngine.Folder(c1.Id) + "/" + m.Name).Concat(new[] { DesignApplyEngine.JournalPath, DesignApplyEngine.KnownGood, DesignApplyEngine.Current, DesignApplyEngine.JournalPath })),
                    "the apply plan: a backup of the console made in this session, the render check on the console's own HOME bound, then the design's files, the journal, known-good, current (the commit) and the journal again (" + wa + ")");
                if (pa == null) return;   // nothing below can run without the first apply
                var oa = DesignApplyEngine.Execute(rc0, pa, pa.Digest, pa.Nonce, ctx);
                Check(oa.Ok && rc0.FileStat("compose", DesignApplyEngine.Current).state == CSha.Of(Encoding.ASCII.GetBytes(c1.Id + "\n")) && Verifier.VerifyFile(pa.Protective.Path).Ok && File.Exists(oa.RecordFile),
                    "applied: current names the design; the record and the backup made just now are kept (" + oa.Problem + ")");
                DropBackups(s);
                var b1 = Boot(exe, pack, DesignRootOf(con, Path.Combine(dir, "root-1")));
                Check(b1.S("source") == "current" && b1.S("design") == c1.Id && b1.S("theme") == "arcade-grid", "a cold start shows the design (from current) in its own theme (" + b1.S("source") + ")");
                // a second design: the first becomes known-good
                var d2 = DesignFor(games, "Deep Sea", (8, 30, 40)); var c3 = DesignCompiler.Compile(d2, games, ph);
                var rc1 = RecoverySessions.OpenSimulated(con, ImageKind.Files); var (pb, wb) = DesignApplyEngine.PlanApply(rc1, ctx, c3, renderer, pack); var ob = pb == null ? null : DesignApplyEngine.Execute(rc1, pb, pb.Digest, pb.Nonce, ctx);
                DropBackups(s);
                Check(ob != null && ob.Ok && pb.KnownGoodAfter == c1.Id && pb.CurrentBefore == c1.Id, "a second design applied: the one shown before is kept as known-good (" + (wb ?? ob?.Problem) + ")");
                // a picture of the current design goes missing: the next start shows known-good, deterministically
                var gone = DesignApplyEngine.Folder(c3.Id) + "/" + c3.Manifest.First(m => m.Name.StartsWith("img/")).Name; SimulatedConsole.FileStore.Delete(con, gone);
                var b2 = Boot(exe, pack, DesignRootOf(con, Path.Combine(dir, "root-2"))); var b2b = Boot(exe, pack, DesignRootOf(con, Path.Combine(dir, "root-2b")));
                Check(b2.S("source") == "known-good" && b2.S("design") == c1.Id && (b2.S("why") ?? "").Contains("missing") && b2.S("frame_sha256") == b2b.S("frame_sha256") && b2.S("frame_sha256") == b1.S("frame_sha256"),
                    "with a picture of the current design missing, a cold start shows the known-good design, the same picture every time and the same as when it was current (" + b2.S("why") + ")");
                // Revert Design: appearance only; a save made after the apply stays exactly as it is
                SimulatedConsole.FileStore.Write(con, save, Encoding.ASCII.GetBytes("a newer save, made after the design was applied"));
                var rc2 = RecoverySessions.OpenSimulated(con, ImageKind.Files); var newer = rc2.FileStat("compose", save);
                var (pr, wr) = DesignApplyEngine.PlanRevert(rc2, ctx, "previous"); var or = pr == null ? null : DesignApplyEngine.Execute(rc2, pr, pr.Digest, pr.Nonce, ctx); DropBackups(s);
                Check(or != null && or.Ok && pr.Changes.All(x => x.Path == DesignApplyEngine.Current || x.Path == DesignApplyEngine.JournalPath) && rc2.FileStat("compose", save).state == newer.state
                      && rc2.FileStat("compose", DesignApplyEngine.Current).state == CSha.Of(Encoding.ASCII.GetBytes(c1.Id + "\n")),
                    "Revert Design to the previous design writes only current and the journal: the save made after the apply is unchanged (" + (wr ?? or?.Problem) + ")");
                var rc3 = RecoverySessions.OpenSimulated(con, ImageKind.Files); var (pd, wd) = DesignApplyEngine.PlanRevert(rc3, ctx, "defaults"); var od = pd == null ? null : DesignApplyEngine.Execute(rc3, pd, pd.Digest, pd.Nonce, ctx); DropBackups(s);
                var b3 = Boot(exe, pack, DesignRootOf(con, Path.Combine(dir, "root-3")));
                Check(od != null && od.Ok && b3.S("source") == "chosen-defaults" && b3.S("design") == "" && rc3.FileStat("compose", save).state == newer.state, "Revert Design to the themes as they come: a cold start shows no design; the save is still the newer one (" + (wd ?? od?.Problem) + ")");
                // a design that moves the HOME's controls: the same apply (render check on the console's own HOME, journal), shown at a cold start;
                // a HOME package from before control regions (no "design_schemas") would set it aside, so its apply is refused before anything
                var mvd = DesignCompiler.Compile(MovedDesign("box-art-showcase"), games);
                var rc5 = RecoverySessions.OpenSimulated(con, ImageKind.Files); var (pm, wm) = DesignApplyEngine.PlanApply(rc5, ctx, mvd, renderer, pack); var om = pm == null ? null : DesignApplyEngine.Execute(rc5, pm, pm.Digest, pm.Nonce, ctx); DropBackups(s);
                var b5 = Boot(exe, pack, DesignRootOf(con, Path.Combine(dir, "root-5")));
                Check(om != null && om.Ok && b5.S("source") == "current" && b5.S("design") == mvd.Id && b5.S("theme") == "box-art-showcase" && rc5.FileStat("compose", save).state == newer.state,
                    "a design that moves the HOME's controls is applied under the same strict rules, a cold start shows it in Box Art Showcase, and the newer save is unchanged (" + (wm ?? om?.Problem) + ")");
                var early = Path.Combine(dir, "earlier-pack"); CopyTree(pack, early); var epj = Json.Parse(File.ReadAllText(Path.Combine(early, "pack.json"))); epj.Remove("design_schemas"); File.WriteAllBytes(Path.Combine(early, "pack.json"), Json.Bytes(epj));
                var why5 = DesignApplyEngine.PackageRefuses(early, mvd);
                Check(why5 != null && why5.Contains("earlier version") && DesignApplyEngine.PackageRefuses(early, c1) == null && DesignApplyEngine.PackageRefuses(pack, mvd) == null,
                    "an earlier CCS HOME on the console is named before a moved layout is written (" + why5 + "); a design that moves nothing still applies to it");
                // refusals: a design that fails the check, a HOME package that is not the console's own; nothing is written
                var rc4 = RecoverySessions.OpenSimulated(con, ImageKind.Files); var before = rc4.FileStat("compose", DesignApplyEngine.Current).state;
                var (pf1, wf1) = DesignApplyEngine.PlanApply(rc4, ctx, bright, renderer, pack); DropBackups(s);
                var other = Path.Combine(dir, "other-pack"); CopyTree(pack, other); File.AppendAllText(Path.Combine(other, "manifest.json"), " ");
                var (pf2, wf2) = DesignApplyEngine.PlanApply(rc4, ctx, c1, renderer, other); DropBackups(s);
                Check(pf1 == null && wf1.Contains("did not pass") && pf2 == null && wf2.Contains("not the one installed") && rc4.FileStat("compose", DesignApplyEngine.Current).state == before,
                    "refused before anything is written: a design that fails the check (" + wf1.Substring(0, Math.Min(90, wf1.Length)) + "...) and a HOME package that is not the console's own (" + wf2 + ")");
                var stale = pa; var ostale = DesignApplyEngine.Execute(rc4, stale, stale.Digest, stale.Nonce, ctx);
                Check(ostale.State == "refused", "a confirmation is used once and belongs to its own session (" + ostale.Problem + ")");
                Check(CcsBackup.Gate.ConsoleSessions && CcsBackup.Gate.ComposerWrites && RecoverySessions.Refusal(ImageKind.Files) == null, "a real console: the HOME design's boundary (Console Composer's) is open in this version (asked of the boundary itself: no test opens a real session or lists USB devices)");
            }
            finally { SimulatedConsole.FileStore = null; }
        }

        static string SaveDesign(HomeDesign d, string dir, string name) { var p = Path.Combine(dir, name); d.Save(p); return p; }
        /// <summary>the backups made by plans that have run (each about 1.1 GB here): removed, so the test keeps at most one on disk</summary>
        static void DropBackups(Settings s) { foreach (var f in Directory.GetFiles(s.PrimaryFolder, "*", SearchOption.AllDirectories)) try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        static void CopyTree(string from, string to)
        {
            foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories)) { var t = Path.Combine(to, f.Substring(from.TrimEnd('\\').Length + 1)); Directory.CreateDirectory(Path.GetDirectoryName(t)); File.Copy(f, t); }
        }
    }
}
