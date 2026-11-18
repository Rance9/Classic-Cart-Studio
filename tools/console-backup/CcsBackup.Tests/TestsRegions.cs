// Classic Cart Studio console backup and restore engine tests: the HOME Designer's control regions:
// each theme's groups, their fixed sizes, the layout rules and the editor's refusals. GPL-3.0-or-later. No console, no files
// outside the test's work folder.
using CcsBackup.Compose;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        /// <summary>part of group "compose": d is an applicable Arcade Grid design with its own background (never changed here)</summary>
        static void ComposeRegions(HomeDesign d0)
        {
            HomeDesign Copy() => HomeDesign.Open(DesignFile(d0.Document, FilesOf(d0)));
            // what each theme can move: the published groups and the exact reason for each one it cannot
            var have = DesignProfile.Themes.ToDictionary(t => t, t => DesignProfile.UiOrder.Where(u => DesignProfile.RegionBox(t, u) != null).ToList());
            Check(have["arcade-grid"].SequenceEqual(new[] { "games", "status", "hints" }) && have["box-art-showcase"].Count == 5 && have["featured-shelf"].SequenceEqual(new[] { "games", "details", "title", "hints" })
                  && have["adventure-map"].SequenceEqual(new[] { "games", "details", "hints" }),
                "the movable control groups: Arcade Grid games/status/hints, Box Art Showcase all five, Featured Shelf games/details/title/hints, Adventure Map games/details/hints");
            var missing = DesignProfile.Themes.SelectMany(t => DesignProfile.UiOrder.Where(u => DesignProfile.RegionBox(t, u) == null).Select(u => DesignProfile.RegionUnavailable(t, u))).ToList();
            Check(missing.Count == 5 && missing.All(m => m.Contains(": ")) && missing.Any(m => m.Contains("part of the cover grid")) && missing.Any(m => m.Contains("inside the information panel's frame")),
                "each group a theme cannot move names its exact dependency (" + string.Join(" | ", missing) + ")");
            var defaults = DesignProfile.Themes.ToDictionary(t => t, t => (IDictionary<string, (long, long, long, long)>)new Dictionary<string, (long, long, long, long)>());
            Check(DesignProfile.Themes.All(t => DesignProfile.LayoutProblems(t, defaults[t]).Count == 0)
                  && DesignProfile.Themes.All(t => have[t].All(u => { var b = DesignProfile.RegionBox(t, u); return b[0] >= 12 && b[1] >= 12 && b[2] <= 1268 && b[3] <= 708; })),
                "every theme's own layout keeps every rule (inside the safe area; no group over another or over a control that never moves; Up and Down between them)");

            // placing groups through the one model: inside the bounds it is applied, compiled as a moved layout; outside it is refused with the reason
            var d = Copy();
            d.PlaceRegion("games", 243, 127); d.PlaceRegion("status", 17, 400);
            var c = DesignCompiler.Compile(d, new List<ConsoleGame>()); var j = Json.Parse(System.Text.Encoding.UTF8.GetString(c.DesignJson));
            var rg = j.A("regions").OfType<Dictionary<string, object>>().ToList();
            Check(d.CanApply && d.ControlRegions.Count == 2 && c.SchemaName == CompiledDesign.Schema2 && j.S("schema") == CompiledDesign.Schema2 && rg.Count == 2 && rg[0].S("ui") == "games" && rg[0].L("y") == 127 && rg[0].L("w") == 994 && rg[0].L("h") == 515 && rg[1].S("ui") == "status" && rg[1].L("y") == 400,
                "the game list 30 px lower and the collection status lower down: applicable; compiled as " + c.SchemaName + " with each group's place and the theme's size");
            var plain = DesignCompiler.Compile(Copy(), new List<ConsoleGame>());
            Check(plain.SchemaName == CompiledDesign.Schema && !System.Text.Encoding.UTF8.GetString(plain.DesignJson).Contains("\"regions\""), "a design that moves nothing still compiles to " + CompiledDesign.Schema + " without regions (the same bytes as before)");
            string Refused(Action a) { try { a(); return null; } catch (ComposeException ex) { return ex.Code + ": " + ex.Message; } }
            var before = d.ContentSha256;
            var r1 = Refused(() => d.PlaceRegion("details", 300, 300)); var r2 = Refused(() => d.PlaceRegion("games", 243, 40)); var r3 = Refused(() => d.PlaceRegion("hints", 31, 600));
            var r4 = Refused(() => d.PlaceRegion("hints", 12, 12)); var r5 = Refused(() => d.PlaceRegion("status", 1100, 600));
            Check(r1 != null && r1.Contains("part of the cover grid") && r2 != null && r2.Contains("tab bar") && r3 != null && r3.Contains("would cover") && r4 != null && r4.Contains("below the game list")
                  && r5 != null && r5.Contains("x from 12 to 1052") && d.ContentSha256 == before,
                "refused, nothing changed: Arcade Grid's details (" + r1 + "); the game list over the tab bar (" + r2 + "); the hints over the game list (" + r3 + "); the hints at the top (" + r4 + "); the status beyond the screen (" + r5 + ")");
            // Box Art Showcase: every group; the hints slid right beside the collection status
            var b = Copy(); b.SetBaseTheme("box-art-showcase");
            b.PlaceRegion("hints", 300, 676); b.PlaceRegion("title", 140, 470);
            var r6 = Refused(() => b.PlaceRegion("hints", 600, 668));
            Check(b.CanApply && b.ControlRegions.Count == 2 && r6 != null && r6.Contains("collection status"),
                "Box Art Showcase: the hints moved right and the title moved: applicable; the hints over the collection status are refused (" + r6 + ")");
            // the same moved groups in another theme: kept and viewable, not applicable, with the reasons
            var t = Copy(); t.PlaceRegion("games", 243, 127); t.SetBaseTheme("featured-shelf");
            Check(!t.CanApply && t.Unsupported.Any(u => u.Contains("keeps Featured Shelf's size")), "a moved game list taken to another theme is kept, not applicable there (" + string.Join("; ", t.Unsupported) + ")");
            // moving needs the design's own background (the theme's own has its controls' panels painted in their places)
            var n = HomeDesign.New("No Background", "arcade-grid");
            var r7 = Refused(() => n.PlaceRegion("games", 243, 127)); n.PlaceRegion("games", 243, 97);
            Check(r7 != null && r7.Contains("own background picture") && n.CanApply, "moving a group without the design's own background is refused (" + r7 + "); the theme's own place needs none");
            // a moved group's kept pointer area moves with it; a file that moves a group the theme lacks opens, not applicable
            var doc = d.Document; var regs = (List<object>)((JObj)doc["layout"])["regions"];
            regs.Add(new JObj { { "id", "details" }, { "kind", "ui" }, { "ui", "details" }, { "x", 300L }, { "y", 620L }, { "w", 320L }, { "h", 100L } });
            Check(Code(() => HomeDesign.Open(DesignFile(doc, FilesOf(d)))) == "design.layout-bounds", "a region beyond the safe area is refused on receipt");
            ((JObj)regs.Last())["y"] = 12L; var od = HomeDesign.Open(DesignFile(doc, FilesOf(d)));
            Check(!od.CanApply && od.Unsupported.Any(u => u.Contains("Arcade Grid has no game details")), "a received design that moves a group its theme does not have opens, viewable, not applicable (" + string.Join("; ", od.Unsupported) + ")");
        }

        /// <summary>every group each theme can move, moved inside its bounds (the places the designer tests use)</summary>
        internal static readonly Dictionary<string, (string ui, long x, long y)[]> Moves = new Dictionary<string, (string, long, long)[]>
        {
            ["arcade-grid"] = new[] { ("games", 243L, 127L), ("status", 17L, 400L), ("hints", 31L, 655L) },
            ["box-art-showcase"] = new[] { ("games", 14L, 51L), ("title", 140L, 445L), ("details", 54L, 551L), ("status", 700L, 481L), ("hints", 300L, 676L) },
            ["featured-shelf"] = new[] { ("games", 182L, 475L), ("title", 890L, 100L), ("details", 900L, 200L), ("hints", 240L, 672L) },
            ["adventure-map"] = new[] { ("games", 63L, 551L), ("details", 554L, 360L), ("hints", 150L, 672L) },
        };
        static readonly string[] ThemeScreens = { "all-games", "long-title", "last-game", "navigation-focus", "folder", "folder-empty" };
        static readonly string[] SharedPanels = { "menu", "menu-focus", "menu-item", "notice", "theme-picker", "theme-focus", "theme-card" };
        static HomeDesign MovedDesign(string theme)
        {
            var d = HomeDesign.New("Moved " + DesignProfile.ThemeName(theme), theme);
            d.SetSlot("scene", ImageNormalizer.Normalize(Shade(1280, 720, 12, 14, 30), DesignProfile.Slots["scene"]));
            foreach (var (ui, x, y) in Moves[theme]) d.PlaceRegion(ui, x, y);
            return d;
        }

        /// <summary>part of group "designer": the console HOME's own layout numbers against DesignProfile's, then, in each theme, a design that
        /// moves every group it can: compiled, checked on the native HOME on every screen, its controls (the layout the mouse targets and
        /// the focus rings come from) exactly where the theme draws them moved by the group's move, everything else where it was</summary>
        static void DesignerRegions(IHomeRenderer renderer, string pack, string dir, List<ConsoleGame> games)
        {
            int[] Box(object o) => ((o as object[])?.ToList() ?? (List<object>)o).Select(v => Convert.ToInt32(v)).ToArray();
            foreach (var theme in DesignProfile.Themes)
            {
                var d = MovedDesign(theme); var c = DesignCompiler.Compile(d, games); var cd = Path.Combine(dir, "moved-" + theme); c.WriteTo(cd);
                var r = renderer.Check(pack, cd, new RenderCheckOptions { Frames = theme == "box-art-showcase" });
                var rep = Json.Parse(r.Report); var lay = rep.O("layout");
                if (theme == "arcade-grid")
                {
                    var diffs = new List<string>();
                    foreach (var t in DesignProfile.Themes)
                    {
                        var lt = lay.O(t); var regs = lt.O("regions");
                        foreach (var u in DesignProfile.UiOrder) { var mine = DesignProfile.RegionBox(t, u); var theirs = regs.ContainsKey(u) ? Box(regs[u]) : null; if (!(mine == null && theirs == null || mine != null && theirs != null && mine.SequenceEqual(theirs))) diffs.Add(t + "/" + u); }
                        var fx = (lt.A("fixed") ?? new List<object>()).Select(x => ((x as object[])?.ToList() ?? (List<object>)x)).ToList(); var mf = DesignProfile.FixedControls(t);
                        if (fx.Count != mf.Count || fx.Where((x, i) => (string)x[0] != mf[i].name || !x.Skip(1).Select(v => Convert.ToInt32(v)).SequenceEqual(mf[i].box)).Any()) diffs.Add(t + "/fixed");
                        if (!Box(lt["browse"]).SequenceEqual(DesignProfile.BrowsePanel(t))) diffs.Add(t + "/browse");
                    }
                    Check(diffs.Count == 0, "the console HOME computes from its package the same control groups, fixed controls and Browse panels as the editor's DesignProfile, in all four themes" + (diffs.Count > 0 ? ": differ " + string.Join(", ", diffs) : ""));
                }
                var moves = (rep.A("design_regions") ?? new List<object>()).OfType<Dictionary<string, object>>().ToDictionary(x => x.S("ui"), x => Box(x["move"]));
                var expect = Moves[theme].ToDictionary(m => m.ui, m => { var b = DesignProfile.RegionBox(theme, m.ui); return new[] { (int)m.x - b[0], (int)m.y - b[1] }; });
                Check(r.Ok && c.SchemaName == CompiledDesign.Schema2 && moves.Count == expect.Count && expect.All(kv => moves.TryGetValue(kv.Key, out var mv) && mv.SequenceEqual(kv.Value)),
                    DesignProfile.ThemeName(theme) + ": a design moving " + string.Join(", ", Moves[theme].Select(m => m.ui)) + " passes the render check on all " + r.States.Count + " screens (readable, within memory) and the HOME reports each group's move" + (r.Ok ? "" : ": " + string.Join(" | ", r.Problems.Take(3))));
                // the HOME's own controls in the base theme: moved with their group, the rest unchanged (same controls, same order)
                var bad = new List<string>(); int compared = 0, moved = 0;
                foreach (var s in (rep.A("states") ?? new List<object>()).OfType<Dictionary<string, object>>().Where(s => s.S("theme") == theme))
                {
                    var dc = (s.A("default_controls") ?? new List<object>()).Select(x => ((x as object[])?.ToList() ?? (List<object>)x)).ToList();
                    var xc = (s.A("controls") ?? new List<object>()).Select(x => ((x as object[])?.ToList() ?? (List<object>)x)).ToList();
                    if (dc.Count != xc.Count) { bad.Add(s.S("state") + ": " + dc.Count + " controls vs " + xc.Count); continue; }
                    for (int i = 0; i < dc.Count; i++)
                    {
                        var db = dc[i].Skip(1).Select(v => Convert.ToInt32(v)).ToArray(); var xb = xc[i].Skip(1).Select(v => Convert.ToInt32(v)).ToArray(); var name = (string)dc[i][0];
                        if (SharedPanels.Contains(name)) { if ((string)xc[i][0] != name) bad.Add(s.S("state") + " " + name + " -> " + xc[i][0]); continue; }   // centered, sized by the design's lettering, above the hints
                        int cx = (db[0] + db[2]) / 2, cy = (db[1] + db[3]) / 2; var g = expect.Keys.FirstOrDefault(u => { var b = DesignProfile.RegionBox(theme, u); return cx >= b[0] && cx < b[2] && cy >= b[1] && cy < b[3]; });
                        var mv = g == null ? new[] { 0, 0 } : expect[g]; bool same = xb.SequenceEqual(db), shifted = xb[0] == db[0] + mv[0] && xb[1] == db[1] + mv[1] && xb[2] == db[2] + mv[0] && xb[3] == db[3] + mv[1];
                                // the theme's own screen: a control in a moved group moves with it, exactly; on the other screens (under a panel, Browse, Game
                        // Details) a control is either moved with its group or unchanged
                        bool strict = ThemeScreens.Contains(s.S("state"));
                        compared++; if (!same && shifted) moved++;
                        if ((string)xc[i][0] != name || !(strict ? shifted : same || shifted)) bad.Add(s.S("state") + " " + name + " [" + string.Join(",", db) + "] -> [" + string.Join(",", xb) + "] expected move " + mv[0] + "," + mv[1]);
                    }
                }
                Check(bad.Count == 0 && moved > 0, DesignProfile.ThemeName(theme) + ": " + compared + " controls on its screens, " + moved + " moved exactly with their group (the focus rings and mouse targets follow them), the others where the theme draws them" + (bad.Count > 0 ? ": " + string.Join(" | ", bad.Take(6)) : ""));
                if (theme == "box-art-showcase")
                    Check(r.Frames.Count(f => f.WithDesign && f.Theme == theme) == r.States.Count(s => s.Theme == theme) && r.States.Where(s => s.Theme == theme && ThemeScreens.Contains(s.State)).All(s => s.DesignSha256 != s.DefaultSha256),
                        "the moved layout's frames: " + r.Frames.Count(f => f.WithDesign && f.Theme == theme) + " Box Art Showcase screens drawn; each of the theme's own screens differs from the theme as it comes");
            }
            // the console HOME refuses a layout its package cannot show: a group its theme lacks, written straight into a compiled design
            var ad = MovedDesign("arcade-grid"); var ac = DesignCompiler.Compile(ad, games); var bdir = Path.Combine(dir, "moved-bad"); ac.WriteTo(bdir);
            var dj = File.ReadAllText(Path.Combine(bdir, "design.json")).Replace("\"ui\": \"status\"", "\"ui\": \"title\""); File.WriteAllText(Path.Combine(bdir, "design.json"), dj);
            var br = renderer.Check(pack, bdir, new RenderCheckOptions { Frames = false });
            Check(!br.Ok && br.Problems.Any(p => p.Contains("set the design aside") && p.Contains("Arcade Grid has no title")), "a compiled layout the package's theme cannot show is set aside by the console HOME, whole (" + br.Problems.FirstOrDefault() + ")");
        }
    }
}
