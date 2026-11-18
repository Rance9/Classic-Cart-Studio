// Classic Cart Studio console backup and restore engine tests: the theme's sprites over a HOME design's own background.
// GPL-3.0-or-later. No console; the native HOME renderer and package of group
// "designer" only.
using CcsBackup.Compose;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        /// <summary>a PNG as BGRA bytes</summary>
        static (int w, int h, byte[] px) Bgra(string file)
        {
            using (var b = new Bitmap(file))
            {
                var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try { var px = new byte[b.Width * b.Height * 4]; for (int y = 0; y < b.Height; y++) Marshal.Copy(d.Scan0 + y * d.Stride, px, y * b.Width * 4, b.Width * 4); return (b.Width, b.Height, px); }
                finally { b.UnlockBits(d); }
            }
        }

        /// <summary>where each theme draws a sprite at its own size and full strength over the background, in drawing order: (screen, sprite,
        /// the layout record that places it, or null for the sprite's own box in the package)</summary>
        static readonly Dictionary<string, (string state, string sprite, string at)[]> PlacedSprites = new Dictionary<string, (string, string, string)[]>
        {
            ["arcade-grid"] = new[] { ("all-games", "tab-bar", (string)null), ("all-games", "tab-active", null), ("all-games", "card-frame", null), ("all-games", "card-trim", null) },
            ["box-art-showcase"] = new[] { ("all-games", "arrow-r", (string)null), ("last-game", "arrow-l", null) },
            ["featured-shelf"] = new[] { ("all-games", "side-active", (string)null), ("all-games", "hero-frame", null), ("all-games", "arrow-r", null), ("last-game", "arrow-l", null) },
            ["adventure-map"] = new[] { ("all-games", "card-frame", (string)null), ("all-games", "card-pointer", null), ("all-games", "panel-frame", null), ("all-games", "walker", "walker"), ("all-games", "arrow-r", null), ("last-game", "arrow-l", null) },
        };

        /// <summary>part of group "designer": in each theme, a design with its own background. Every pixel a sprite carries from the theme's old
        /// picture (opaque in the approved sprite, see-through in its own-pixel version, the package's file_own) must not reach the screen: on
        /// the native HOME's screens the design's background shows there instead. The sprite's own pixels are drawn unchanged; the theme's
        /// own screens (no design) are the same with or without the own-pixel versions in the package.</summary>
        static void DesignerOwnSprites(IHomeRenderer renderer, string pack, string dir, List<ConsoleGame> games)
        {
            var pj = Json.Parse(File.ReadAllText(Path.Combine(pack, "pack.json"))); var themes = pj.O("themes");
            int withOwn = themes.Keys.Sum(t => themes.O(t).O("sprites").Values.OfType<Dictionary<string, object>>().Count(s => s.S("file_own") != null));
            Check(withOwn == 33, "the package carries, for the " + withOwn + " sprites the four themes draw over the background, their own pixels (file_own)");
            var comp = Python != null && File.Exists(Python) ? Environment.GetEnvironmentVariable("CCS_COMPANION_DIR") : null;
            if (comp == null) Console.WriteLine("SKIP the own-pixel rules' Python check (no --python or CCS_COMPANION_DIR)");
            else
            {
                var (rc, o) = Run(Python, "\"" + Path.Combine(comp, "test_own_sprites.py") + "\"", null, 600000);
                Check(rc == 0 && o.Contains("RESULT 4/4 passed, 0 skipped"), "Python: every sprite drawn over the background has its rule; its own-pixel version never has more coverage than the approved sprite, keeps a whole pixel's color, makes the ground along its edge see-through and is the same every time (" + o.Split('\n').LastOrDefault(l => l.StartsWith("RESULT"))?.Trim() + ")");
            }
            var bg = (r: 6, g: 22, b: 16);   // the design's background: a dark green the HOME's lettering stays readable on (Shade: plus up to 24 toward the lower right)
            (int r, int g, int b) BgAt(int x, int y) { int k = (x + y) * 24 / (1280 + 720); return (bg.r + k, bg.g + k, bg.b + k); }
            foreach (var theme in DesignProfile.Themes)
            {
                var d = HomeDesign.New("Own Pixels " + DesignProfile.ThemeName(theme), theme);
                d.SetSlot("scene", ImageNormalizer.Normalize(Shade(1280, 720, bg.r, bg.g, bg.b), DesignProfile.Slots["scene"]));
                var c = DesignCompiler.Compile(d, games); var cd = Path.Combine(dir, "own-" + theme); c.WriteTo(cd);
                var r = renderer.Check(pack, cd, new RenderCheckOptions { Frames = true });
                var sp = themes.O(theme).O("sprites"); var lines = new List<string>(); bool ok = r.Ok && r.Frames.Count > 0;
                if (!r.Ok) lines.Add("the render check: " + string.Join(" | ", r.Problems.Take(2)));
                // each placed sprite: where it is and its two versions; a pixel a sprite drawn after it covers is not judged as its own
                var placed = new List<(string state, string name, int x0, int y0, (int w, int h, byte[] px) ap, (int w, int h, byte[] px) op)>();
                foreach (var (state, name, at) in PlacedSprites[theme])
                {
                    var s = sp.O(name == "walker" ? "hero" : name);
                    if (s == null || s.S("file_own") == null) { ok = false; lines.Add(name + ": no own pixels in the package"); continue; }
                    int x0, y0;
                    if (at == null) { var b = s.A("box"); x0 = Convert.ToInt32(b[0]); y0 = Convert.ToInt32(b[1]); }
                    else { var ctl = r.States.FirstOrDefault(z => z.Theme == theme && z.State == state)?.Controls.FirstOrDefault(z => z.name == at) ?? default; if (ctl.name == null) { ok = false; lines.Add(name + ": no layout record"); continue; } x0 = ctl.x0; y0 = ctl.y0; }
                    placed.Add((state, name, x0, y0, Bgra(Path.Combine(pack, s.S("file").Replace('/', Path.DirectorySeparatorChar))), Bgra(Path.Combine(pack, s.S("file_own").Replace('/', Path.DirectorySeparatorChar)))));
                }
                bool Covered(int k, int fx, int fy) => placed.Skip(k + 1).Any(q => q.state == placed[k].state && fx >= q.x0 && fy >= q.y0 && fx < q.x0 + q.op.w && fy < q.y0 + q.op.h && q.op.px[((fy - q.y0) * q.op.w + fx - q.x0) * 4 + 3] > 0);
                for (int k = 0; k < placed.Count; k++)
                {
                    var (state, name, x0, y0, ap, op) = placed[k]; var fr = r.Frames.FirstOrDefault(f => f.WithDesign && f.Theme == theme && f.State == state);
                    if (fr == null) { ok = false; lines.Add(name + ": its screen was not drawn"); continue; }
                    int removed = 0, shown = 0, kept = 0, keptDrawn = 0;
                    for (int y = 0; y < ap.h; y++) for (int x = 0; x < ap.w; x++)
                    {
                        int fx = x0 + x, fy = y0 + y; if (fx < 0 || fy < 0 || fx >= fr.Width || fy >= fr.Height || Covered(k, fx, fy)) continue;
                        int i = (y * ap.w + x) * 4, j = (fy * fr.Width + fx) * 3;
                        // the approved pixel on the screen exactly (an old-picture pixel); an own pixel as approved within 3 (the faint outer edge of a
                        // focus glow drawn later can touch it). Not within 3 for the old picture: the theme's own dark panel fill can come that close.
                        int dr = Math.Abs(fr.Rgb[j] - ap.px[i + 2]), dg = Math.Abs(fr.Rgb[j + 1] - ap.px[i + 1]), db = Math.Abs(fr.Rgb[j + 2] - ap.px[i]);
                        bool same = dr == 0 && dg == 0 && db == 0, near = dr <= 3 && dg <= 3 && db <= 3;
                        var under = BgAt(fx, fy);   // an old-picture pixel unlike the design's background there: drawn, it would show
                        if (ap.px[i + 3] >= 250 && op.px[i + 3] == 0 && Math.Max(Math.Abs(ap.px[i + 2] - under.r), Math.Max(Math.Abs(ap.px[i + 1] - under.g), Math.Abs(ap.px[i] - under.b))) >= 8) { removed++; if (same) shown++; }
                        if (ap.px[i + 3] == 255 && op.px[i + 3] == 255 && op.px[i] == ap.px[i] && op.px[i + 1] == ap.px[i + 1] && op.px[i + 2] == ap.px[i + 2]) { kept++; if (near) keptDrawn++; }
                    }
                    bool good = removed > 0 && shown == 0 && (kept == 0 || keptDrawn * 10 >= kept * 8);
                    ok &= good; lines.Add(name + " " + shown + "/" + removed + " old-picture pixels shown, " + keptDrawn + "/" + kept + " own pixels as approved" + (good ? "" : " (WRONG)"));
                }
                Check(ok, DesignProfile.ThemeName(theme) + " over a design's own background: no pixel the sprites carry from the theme's old picture reaches the screen; their own pixels are drawn as approved (" + string.Join("; ", lines) + "; added memory " + CcsBackup.Compose.Units.Size(r.AddedPeakBytes) + ")");
            }
            // the theme's own screens never use them: the same package without the own-pixel versions draws every default screen the same
            var plain = Path.Combine(dir, "pack-without-own"); CopyTree(pack, plain); var pp = Json.Parse(File.ReadAllText(Path.Combine(plain, "pack.json")));
            foreach (var t in pp.O("themes").Values.OfType<Dictionary<string, object>>()) foreach (var s in t.O("sprites").Values.OfType<Dictionary<string, object>>()) s.Remove("file_own");
            File.WriteAllBytes(Path.Combine(plain, "pack.json"), Json.Bytes(pp));
            var md = HomeDesign.New("Own Pixels Check", "adventure-map"); md.SetSlot("scene", ImageNormalizer.Normalize(Shade(1280, 720, bg.r, bg.g, bg.b), DesignProfile.Slots["scene"]));
            var mc = DesignCompiler.Compile(md, games); var mcd = Path.Combine(dir, "own-check"); mc.WriteTo(mcd);
            var a1 = renderer.Check(pack, mcd, new RenderCheckOptions { Frames = false }); var a2 = renderer.Check(plain, mcd, new RenderCheckOptions { Frames = false });
            var sameDefaults = a1.States.Count > 0 && a1.States.Count == a2.States.Count && a1.States.Zip(a2.States, (x, y) => x.Theme == y.Theme && x.State == y.State && x.DefaultSha256 == y.DefaultSha256).All(v => v);
            var mapDiffers = a1.States.Zip(a2.States, (x, y) => x.Theme == "adventure-map" && x.State == "all-games" && x.DesignSha256 != y.DesignSha256).Any(v => v);
            var othersSame = a1.States.Zip(a2.States, (x, y) => x.Theme == "adventure-map" || x.DesignSha256 == y.DesignSha256).All(v => v);
            Check(sameDefaults && mapDiffers && othersSame, "the theme's own screens are the same with or without the own-pixel versions (" + a1.States.Count + " screens); only the design's own theme draws them (Adventure Map's All Games differs, the other themes' design screens do not)");
        }
    }
}
