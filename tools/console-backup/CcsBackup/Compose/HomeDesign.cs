// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The Shared HOME Design (.ccshome, schema ccs-home-design/1; COMPOSER-FORMATS.md): a standard ZIP holding ccs-home-design.json, the
// design's own still pictures (images/<sha256>.png|.jpg, content-addressed) and a README. A design is pictures plus layout and settings
// for CCS HOME and a portable folder/order template: never games, saves, console system files, console identities or keys, private
// locations, private notes or private game fingerprints. Games are named only by public catalog references; a game of the sender's own
// is a neutral numbered placeholder ("Your game 3") that the receiver maps or skips explicitly.
//
// One lossless model: HomeDesign keeps the whole validated document (JObj, in file order) and its pictures; an edit changes only the
// fields it understands, on a copy that must validate before it replaces the original, so data a newer version added ("optional"
// objects, and pictures they name) survives open -> edit -> save -> open unchanged. A file is opened in one of two modes:
//   apply    schema major 1 and every "requires" capability known: every member name, field, value, picture, layout rule and budget below
//            is checked; an unknown value in a known field (a newer theme, slot, region kind or font) leaves it viewable but blocks apply
//   inspect  a newer schema major or an unknown required capability: only the envelope and the inert-data rules are checked; the design
//            can be looked at and its original file exported unchanged, never applied, edited or silently downgraded
// Receiving validates before decoding: SafeZip (limits, names, links, collisions, bombs), then the manifest, then each picture's member
// name = listed fingerprint = SHA-256 of its bytes, its size, its real kind and header dimensions, its canonical profile, its slot rules.
// Pictures are never converted on receipt (ImageNormalizer is for a person's own pictures on this PC).
// Console profile nes-classic-home-1280x720/1: a 1280x720 stage, nothing within 12 px of an edge (the HOME edge rule), existing fonts
// only, PNG/JPEG only, the startup picture a still 1280x720 JPEG of at most 450,000 bytes, the decoding-work budget (DecodeWorkBudget) and
// the measured decoded-memory budget (ActiveDecodedBudget, enforced on the native renderer's own measurement by the render check; a
// package size limit is not a memory budget). What the console renderer draws and what it does not: HOME-DESIGNER.md.
using CcsBackup.Archive;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsBackup.Compose
{
    public static class DesignProfile
    {
        public const string Format = "classic-cart-studio-home-design", Schema = "ccs-home-design/1", Profile = "nes-classic-home-1280x720/1";
        public const string ManifestName = "ccs-home-design.json", ReadmeName = "README.txt", Extension = ".ccshome", TypeName = "Shared HOME Design";
        public const string Label = "Shared HOME Design: pictures and appearance settings; no games or saves.";
        public const int StageW = 1280, StageH = 720, Margin = 12;
        public const long FrameBytes = ImageRules.FrameBytes;
        /// <summary>MEASURED, ENFORCED (HOME-DESIGNER.md "Budgets"): the most decoded-picture memory (decoded pictures plus the decoder's and
        /// resampler's working memory) a design may make the console HOME hold beyond the same HOME without it, at its worst moment (the start,
        /// a Details screen, a Browse page). Measured with the native renderer's own accounting (design.h) by the render check, which refuses a
        /// design over it; DesignBudget.PeakDecodedBytes is the editor's upper-bound estimate of the same.</summary>
        public const long ActiveDecodedBudget = 16000000;
        /// <summary>ENFORCED (here and by the console renderer, design.h DESIGN_DECODED_BUDGET): the decoding work one start may do for a design,
        /// as the sum over its distinct pictures (the startup picture apart) of width x height x 4 bytes: eight full frames, 29,491,200 bytes
        /// (29.49 MB). A start decodes each once and draws it into the one background frame, so this bounds time, not resident memory.</summary>
        public const long DecodeWorkBudget = 8 * FrameBytes;
        /// <summary>ENFORCED by the apply plan: the data storage keeps at least this much free after a design is staged (decimal 8 MB, the
        /// same room the HOME installation keeps for saves and settings)</summary>
        public const long StorageReserve = 8000000;
        /// <summary>the shared menus' lettering the console HOME lays out: menu titles at most 16 px, menu text 13 to 22 px</summary>
        public const int MaxMenuTitlePx = 16, MinMenuTextPx = 13, MaxMenuTextPx = 22;
        /// <summary>true: the HOME's own controls move in bounded groups (ui regions, HOME-DESIGNER.md "Control regions"): in the design's
        /// base theme each group the theme has keeps its size and moves whole, inside the safe area, never over another group or a control
        /// that never moves, the game list below the navigation and the hints below the game list and the Browse panel</summary>
        public const bool UiRegionsPlaceable = true;
        static readonly Dictionary<string, string[]> ThemeSlots = new Dictionary<string, string[]>
        {
            ["arcade-grid"] = new[] { "scene", "details-backdrop", "plate", "icon-games", "icon-folder", "icon-star", "icon-clock", "icon-gear" },
            ["box-art-showcase"] = new[] { "scene", "details-backdrop", "plate" },
            ["featured-shelf"] = new[] { "scene", "details-backdrop", "plate", "icon-games", "icon-folder", "icon-star", "icon-gear" },
            ["adventure-map"] = new[] { "scene", "details-backdrop", "plate" },
        };
        /// <summary>does this theme's console renderer draw this picture place (the four themes' own sprites: HOME-DESIGNER.md)</summary>
        public static bool ThemeDraws(string theme, string slot) => ThemeSlots.TryGetValue(theme ?? "", out var s) && s.Contains(slot);
        public static IReadOnlyList<string> SlotsOf(string theme) => ThemeSlots.TryGetValue(theme ?? "", out var s) ? s : new string[0];
        public static string ThemeName(string theme) => theme == "arcade-grid" ? "Arcade Grid" : theme == "box-art-showcase" ? "Box Art Showcase" : theme == "featured-shelf" ? "Featured Shelf" : theme == "adventure-map" ? "Adventure Map" : theme;
        public const int MaxImages = 48, MaxRegions = 40, MaxDecorations = 32, MaxDecorLayer = 7, MaxFolders = 200, MaxFolderDepth = 5,
                         MaxOrder = 5000, MaxFolderGames = 2000, MaxRefs = 20000, MaxCredits = 64, MaxFocus = 16, MinLabelPx = 13;
        public const long MaxManifestBytes = 1000000, MaxReadmeBytes = 16000, MaxImageBytes = 4000000, MaxArchiveBytes = 64000000;

        public static readonly string[] Themes = { "arcade-grid", "box-art-showcase", "featured-shelf", "adventure-map" };
        public static readonly string[] StartupBuiltins = { "forgotten-keep", "galactic-frontier", "kingdoms-beyond", "neon-highway", "pixel-worlds" };
        public static readonly string[] KnownCapabilities = { "home-design/1" };
        /// <summary>the HOME's existing baked font faces (tools/companion/native_home/derive.py FONTS, the symbol faces excepted); a design
        /// chooses among them and never carries a font</summary>
        public static readonly string[] Fonts =
        {
            "silk9", "silk10", "silk11", "silk12", "silk13", "silk14", "silk17", "silk25", "silk30", "silkt16", "silkt19", "silkt23",
            "silkh20", "silkh22", "silkh24", "silkh26", "silkh28", "silkh30", "silkh32", "silkh34", "silkh36", "silkh38", "silkh40", "silkh42", "silkh44", "silkh46",
            "sans400_9", "sans400_10", "sans400_11", "sans400_12", "sans400_14", "sans400_15", "sans400_16", "sans400_16b", "sans400_17", "sans400_19", "sans400_20",
            "sans600_14", "sans600_15", "sans700_13", "sans700_14", "sans700_15", "sans700_16", "sans700_18", "sans700_20", "sans800_10", "sans800_15", "sans800_40",
            "cin700_8", "cin700_11", "cin700_16", "cin700_20", "cin700_29", "cin700_32", "cin700_36", "cin700_40", "cin700_43", "cin700_46",
            "vt17", "vt18", "vt19", "vt21", "vt23", "vt26", "px7", "px8", "px9", "px10", "px11", "px13", "px15", "px17", "px19", "px21", "px22",
            "mono14", "mono16", "mono19", "mono20", "mono700_13", "mono700_16",
        };
        public static int FontPx(string font) { var m = Regex.Match(font ?? "", @"(\d+)\D*$"); return m.Success ? int.Parse(m.Groups[1].Value) : 0; }

        static ImageSlotRule R(string id, int w, int h, bool exact, bool png, bool jpeg, bool alpha, long bytes, string text) =>
            new ImageSlotRule { Id = id, MaxW = w, MaxH = h, Exact = exact, Png = png, Jpeg = jpeg, Alpha = alpha, MaxBytes = bytes, Text = text };
        /// <summary>every place a picture can go (slots by id; "startup", "folder-art" and "decoration" are named by their own fields)</summary>
        public static readonly Dictionary<string, ImageSlotRule> Slots = new[]
        {
            R("scene", 1280, 720, true, true, true, false, MaxImageBytes, "the HOME background"),
            R("details-backdrop", 1272, 712, true, true, true, false, MaxImageBytes, "the game details background"),
            R("plate", 1256, 696, false, true, false, true, MaxImageBytes, "the frame or plate over the background"),
            R("icon-games", 128, 128, false, true, false, true, 200000, "the All Games icon"),
            R("icon-folder", 128, 128, false, true, false, true, 200000, "the folder icon"),
            R("icon-star", 128, 128, false, true, false, true, 200000, "the favorites icon"),
            R("icon-clock", 128, 128, false, true, false, true, 200000, "the recently played icon"),
            R("icon-gear", 128, 128, false, true, false, true, 200000, "the settings icon"),
            R("startup", 1280, 720, true, false, true, false, 450000, "the startup picture"),
            R("folder-art", 544, 148, false, true, true, false, 1000000, "a folder picture"),
            R("decoration", 1256, 696, false, true, true, true, MaxImageBytes, "a decoration"),
        }.ToDictionary(r => r.Id);
        public static readonly string[] SlotIds = { "scene", "details-backdrop", "plate", "icon-games", "icon-folder", "icon-star", "icon-clock", "icon-gear" };
        /// <summary>the HOME's own interactive regions the format can describe, with the smallest size any of them can have (each theme's
        /// own sizes are larger: RegionBox)</summary>
        public static readonly Dictionary<string, (int w, int h)> UiRegions = new Dictionary<string, (int, int)>
        { ["games"] = (400, 100), ["details"] = (320, 100), ["title"] = (160, 28), ["status"] = (96, 28), ["hints"] = (240, 24) };
        public static readonly string[] UiOrder = { "games", "details", "title", "status", "hints" };
        /// <summary>why a design that moves a control group needs its own background (the console HOME checks the same: design_layout)</summary>
        public const string MovedNeedsScene = "each theme's own background has the panels of its controls painted where the theme puts them";
        public static string UiName(string ui) => ui == "games" ? "game list" : ui == "details" ? "game details" : ui == "title" ? "title" : ui == "status" ? "collection status" : ui == "hints" ? "button hints" : ui;

        // ---- the control regions (HOME-DESIGNER.md "Control regions"): the same numbers and rules as the console HOME's home.c
        //      (region_default, region_fixed, design_layout), which computes them from its package's geometry; the render check's "layout"
        //      publishes the console's numbers and the tests compare them with these. Boxes are x0, y0, x1, y1 on the 1280x720 stage.
        static readonly Dictionary<string, Dictionary<string, int[]>> RegionBoxes = new Dictionary<string, Dictionary<string, int[]>>
        {
            ["arcade-grid"] = new Dictionary<string, int[]> { ["games"] = new[] { 243, 97, 1237, 612 }, ["status"] = new[] { 17, 98, 233, 306 }, ["hints"] = new[] { 31, 650, 1256, 700 } },
            ["box-art-showcase"] = new Dictionary<string, int[]> { ["games"] = new[] { 14, 65, 1266, 458 }, ["details"] = new[] { 54, 547, 680, 660 }, ["title"] = new[] { 96, 458, 628, 529 }, ["status"] = new[] { 680, 481, 955, 672 }, ["hints"] = new[] { 38, 668, 659, 700 } },
            ["featured-shelf"] = new Dictionary<string, int[]> { ["games"] = new[] { 182, 455, 1233, 624 }, ["details"] = new[] { 903, 181, 1234, 376 }, ["title"] = new[] { 903, 106, 1233, 162 }, ["hints"] = new[] { 230, 670, 1056, 702 } },
            ["adventure-map"] = new Dictionary<string, int[]> { ["games"] = new[] { 63, 545, 1219, 658 }, ["details"] = new[] { 354, 360, 925, 542 }, ["hints"] = new[] { 180, 670, 1100, 702 } },
        };
        /// <summary>each theme's controls that never move; the first is its navigation (the game list's Up)</summary>
        static readonly Dictionary<string, (string name, int[] box)[]> FixedBoxes = new Dictionary<string, (string, int[])[]>
        {
            ["arcade-grid"] = new[] { ("tab bar", new[] { 312, 46, 1142, 95 }) },
            ["box-art-showcase"] = new[] { ("top navigation", new[] { 681, 14, 1133, 51 }) },
            ["featured-shelf"] = new[] { ("sidebar", new[] { 18, 112, 237, 424 }), ("featured picture", new[] { 243, 90, 880, 445 }) },
            ["adventure-map"] = new[] { ("map", new[] { 9, 84, 1257, 343 }) },
        };
        /// <summary>the Browse panel, drawn over each theme's games area (the hints stay below it)</summary>
        static readonly Dictionary<string, int[]> BrowsePanels = new Dictionary<string, int[]>
        { ["arcade-grid"] = new[] { 246, 101, 1232, 622 }, ["box-art-showcase"] = new[] { 60, 90, 1220, 660 }, ["featured-shelf"] = new[] { 250, 96, 1254, 656 }, ["adventure-map"] = new[] { 120, 110, 1160, 530 } };
        /// <summary>why a theme has no movable group of that kind (the exact dependency in its renderer)</summary>
        static readonly Dictionary<string, string> NoRegion = new Dictionary<string, string>
        {
            ["arcade-grid/details"] = "its selected game's card is part of the cover grid (the grid's first two rows sit beside the card and its last row under it), so the card moves with the game list",
            ["arcade-grid/title"] = "its selected game's title is the caption inside the card, which moves with the game list",
            ["featured-shelf/status"] = "it shows no collection status (the section's name is part of its title area)",
            ["adventure-map/title"] = "its selected game's title is drawn inside the information panel's frame, which moves as the game details",
            ["adventure-map/status"] = "it shows no collection status",
        };
        /// <summary>the theme's own place of a control group (x0, y0, x1, y1), or null when the theme has none (RegionUnavailable says why)</summary>
        public static int[] RegionBox(string theme, string ui) => RegionBoxes.TryGetValue(theme ?? "", out var t) && t.TryGetValue(ui ?? "", out var b) ? (int[])b.Clone() : null;
        public static string RegionUnavailable(string theme, string ui) => RegionBox(theme, ui) != null ? null : ThemeName(theme) + " has no " + UiName(ui) + " that can move" + (NoRegion.TryGetValue(theme + "/" + ui, out var w) ? ": " + w : "");
        public static IReadOnlyList<(string name, int[] box)> FixedControls(string theme) => FixedBoxes.TryGetValue(theme ?? "", out var f) ? f : new (string, int[])[0];
        public static int[] BrowsePanel(string theme) => BrowsePanels.TryGetValue(theme ?? "", out var b) ? (int[])b.Clone() : null;
        static bool Hit(int[] a, int[] b) => a[0] < b[2] && b[0] < a[2] && a[1] < b[3] && b[1] < a[3];

        /// <summary>what keeps a set of placed control groups from being shown in this theme (empty: nothing). placed: ui -> (x, y, w, h)
        /// as the design gives them; every other group of the theme stays at its own place. The console HOME checks the same again against
        /// its package's geometry before it uses a design.</summary>
        public static List<string> LayoutProblems(string theme, IDictionary<string, (long x, long y, long w, long h)> placed)
        {
            var p = new List<string>(); if (!RegionBoxes.ContainsKey(theme ?? "")) return p;
            var eff = new Dictionary<string, int[]>(); var tn = ThemeName(theme);
            foreach (var u in UiOrder) { var b = RegionBox(theme, u); if (b != null) eff[u] = b; }
            foreach (var kv in placed)
            {
                var b = RegionBox(theme, kv.Key); if (b == null) { p.Add(RegionUnavailable(theme, kv.Key)); continue; }
                int w = b[2] - b[0], h = b[3] - b[1]; var (x, y, rw, rh) = kv.Value;
                if (rw != w || rh != h) { p.Add("the " + UiName(kv.Key) + " keeps " + tn + "'s size (" + w + " x " + h + "), not " + rw + " x " + rh); continue; }
                if (x < Margin || y < Margin || x + w > StageW - Margin || y + h > StageH - Margin) { p.Add("the " + UiName(kv.Key) + " reaches the screen's edge"); continue; }
                eff[kv.Key] = new[] { (int)x, (int)y, (int)x + w, (int)y + h };
            }
            if (p.Count > 0) return p;
            var us = UiOrder.Where(eff.ContainsKey).ToList(); var fx = FixedControls(theme);
            for (int i = 0; i < us.Count; i++)
            {
                for (int k = i + 1; k < us.Count; k++) if (Hit(eff[us[i]], eff[us[k]])) p.Add("the " + UiName(us[i]) + " would cover the " + UiName(us[k]));
                foreach (var f in fx) if (Hit(eff[us[i]], f.box)) p.Add("the " + UiName(us[i]) + " would cover " + tn + "'s " + f.name + ", which does not move");
            }
            if (eff.TryGetValue("games", out var g) && fx.Count > 0 && g[1] < fx[0].box[3]) p.Add("the game list must stay below the " + fx[0].name + " (the controller's Up goes there)");
            if (eff.TryGetValue("hints", out var hb) && g != null && hb[1] < g[3]) p.Add("the button hints must stay below the game list (the controller's Down goes there)");
            if (hb != null && hb[1] < BrowsePanels[theme][3]) p.Add("the button hints must stay below the Browse panel, which is drawn over the game list");
            return p;
        }
    }

    /// <summary>a game named by a design or a plan: a public catalog version id (and its public title), or a numbered placeholder</summary>
    public sealed class GameRef
    {
        public string Catalog, Title; public int Placeholder;
        public bool IsPlaceholder => Catalog == null;
        public string DisplayName => IsPlaceholder ? "Your game " + Placeholder : Title ?? "Game " + Catalog;
        public static readonly Regex CatalogId = new Regex("^[0-9a-f]{16}$", RegexOptions.CultureInvariant);
        public JObj ToJson() => IsPlaceholder ? new JObj { { "placeholder", (long)Placeholder } } : Title == null ? new JObj { { "catalog", Catalog } } : new JObj { { "catalog", Catalog }, { "title", Title } };
        public static GameRef Of(JObj o) => o == null ? null : o.Has("placeholder") ? new GameRef { Placeholder = (int)Doc.Int(o["placeholder"]) } : new GameRef { Catalog = o["catalog"] as string, Title = o["title"] as string };
    }

    /// <summary>What a design asks of the console renderer (HOME-DESIGNER.md "Budgets"). DecodeWorkBytes: the decoding a start does (each
    /// distinct picture once, width x height x 4). PeakDecodedBytes: an upper bound of the most decoded-picture memory the design adds at
    /// any moment, from the renderer's own steps (the background frame, one picture decoded and fitted at a time, the Details background,
    /// the folder pictures of one Browse page, the icons). ResidentDecodedBytes: what stays held between those steps. Startup: the startup
    /// picture, decoded by the game player before the HOME starts. The render check measures the real numbers on the native renderer.</summary>
    public sealed class DesignBudget
    {
        public sealed class Item { public string Sha256, Use; public int Width, Height; public long Bytes, FileBytes, StepBytes; public bool Png; }
        public List<Item> Items = new List<Item>();
        public long DecodeWorkBytes, PeakDecodedBytes, ResidentDecodedBytes, StartupDecodedBytes, Limit = DesignProfile.ActiveDecodedBudget, WorkLimit = DesignProfile.DecodeWorkBudget;
        /// <summary>within what the file itself can show: the decoding work and the startup picture (exact). The memory peak is enforced on the
        /// renderer's measurement by the render check (an estimate over the limit is a warning here, not a refusal)</summary>
        public bool Ok => DecodeWorkBytes <= WorkLimit && StartupDecodedBytes <= DesignProfile.FrameBytes;
        public bool PeakEstimateOver => PeakDecodedBytes > Limit;
        public string Text => "Memory: " + (DecodeWorkBytes > 0 ? "your pictures need " + Units.Size(DecodeWorkBytes) + " of decoding when the console starts (the limit is " + Units.Size(WorkLimit) + "; one full-screen picture is "
            + Units.Size(DesignProfile.FrameBytes) + ")" : "no pictures of yours need decoding when the console starts") + ", and the HOME needs about " + Units.Size(PeakDecodedBytes) + " of memory at once (the limit is " + Units.Size(Limit) + ")"
            + (StartupDecodedBytes > 0 ? "; the startup picture needs " + Units.Size(StartupDecodedBytes) + " by itself" : "") + ".";
        /// <summary>the renderer's decode of one picture, as an upper bound: its pixels (out channels) plus the decoder's working memory
        /// (JPEG: the sample planes of three full-size components; PNG: the compressed data twice, the inflated rows and one converted copy)</summary>
        public static long DecodeBytes(int w, int h, bool png, long fileBytes, int outChannels)
        {
            long W16 = (w + 15) / 16 * 16, H16 = (h + 15) / 16 * 16, px = (long)w * h * outChannels;
            return png ? px + 2 * fileBytes + (long)h * (1 + (long)w * 4) + (long)w * h * 4 + 65536 : px + 3 * W16 * H16 + 65536;
        }
        /// <summary>fitting a decoded RGBA picture into a box (img_contain): the fitted copy, the premultiplied source and the resampler's rows</summary>
        public static long FitBytes(int w, int h, int bw, int bh) => (long)bw * bh * 4 + (long)w * h * 4 + (long)bw * h * 8 + (long)(bw + bh) * 264;
    }

    public sealed class DesignSharePreview
    {
        public bool CanShare; public HomeDesign Shared; public List<string> Included = new List<string>(), Removed = new List<string>(), Refusals = new List<string>();
    }

    public sealed class DesignMatch { public List<GameRef> Available = new List<GameRef>(), Missing = new List<GameRef>(), Placeholders = new List<GameRef>(); }

    public sealed class HomeDesign
    {
        JObj doc; Dictionary<string, byte[]> images;
        Dictionary<string, ImageInfo> facts = new Dictionary<string, ImageInfo>();   // per fingerprint: a picture's bytes never change under its name
        public JObj Document => (JObj)StrictJson.Clone(doc);
        public IReadOnlyDictionary<string, byte[]> Images => images;
        public bool CanApply { get; private set; }
        public bool InspectOnly { get; private set; }
        public List<string> Unsupported { get; private set; } = new List<string>();
        public byte[] OriginalFile { get; private set; }
        public IReadOnlyList<(string name, long size)> Members { get; private set; } = new List<(string, long)>();

        HomeDesign() { }
        public string Schema => doc["schema"] as string;
        public string Id => (doc["design"] as JObj)?["id"] as string;
        public long Revision => Doc.Int((doc["design"] as JObj)?["revision"]);
        public string Name => (doc["design"] as JObj)?["name"] as string;
        public string BaseTheme => (doc["design"] as JObj)?["base_theme"] as string;
        public byte[] ManifestBytes() => Json.Bytes(doc);
        /// <summary>the design's identity for a plan: SHA-256 of its manifest as this program writes it (the pictures are in it by fingerprint)</summary>
        public string ContentSha256 => Sha.Of(ManifestBytes());
        public string ImageFor(string slot) => ((doc["slots"] as JObj)?[slot] as JObj)?["image"] as string;
        public List<GameRef> Order => (Doc.Arr(doc["order"]) ?? new List<object>()).OfType<JObj>().Select(GameRef.Of).ToList();
        public List<JObj> Folders => (Doc.Arr(doc["folders"]) ?? new List<object>()).OfType<JObj>().Select(f => (JObj)StrictJson.Clone(f)).ToList();
        public List<JObj> Regions => (Doc.Arr((doc["layout"] as JObj)?["regions"]) ?? new List<object>()).OfType<JObj>().Select(f => (JObj)StrictJson.Clone(f)).ToList();

        // ------------------------------------------------------------------ making one
        public static HomeDesign New(string name, string baseTheme)
        {
            var d = new JObj
            {
                { "format", DesignProfile.Format }, { "schema", DesignProfile.Schema }, { "minor", 0L }, { "requires", new List<object>() }, { "profile", DesignProfile.Profile },
                { "design", new JObj { { "id", NewId.Make() }, { "revision", 1L }, { "name", name }, { "base_theme", baseTheme } } },
                { "credits", new List<object>() }, { "images", new List<object>() }, { "slots", new JObj() },
                { "appearance", new JObj { { "text_color", "#e9e7e1" }, { "accent_color", "#e4000f" }, { "panel_color", "#111115" }, { "panel_opacity", 100L }, { "title_font", "px11" }, { "text_font", "sans400_20" } } },   // the console HOME's own menus
                { "layout", new JObj { { "regions", new List<object>() } } }, { "folders", new List<object>() }, { "order", new List<object>() },
                { "map_shortcuts", new List<object> { null, null } }, { "settings", new JObj { { "reduced_motion", false }, { "hints", true }, { "music", true } } },
                { "startup", new JObj { { "mode", "random" } } },
            };
            var h = new HomeDesign { doc = d, images = new Dictionary<string, byte[]>() };
            h.Validate(); return h;
        }

        // ------------------------------------------------------------------ editing (each edit validates a copy, then replaces)
        void Edit(Action<JObj, Dictionary<string, byte[]>> change)
        {
            if (InspectOnly) throw new ComposeException("design.inspect-only", "This HOME design was made by a newer version of Classic Cart Studio. It can be viewed and its original file exported, but not changed here.");
            var d = (JObj)StrictJson.Clone(doc); var im = new Dictionary<string, byte[]>(images);
            change(d, im);
            var h = new HomeDesign { doc = d, images = im, facts = facts }; h.Validate();
            doc = h.doc; images = h.images; CanApply = h.CanApply; Unsupported = h.Unsupported; OriginalFile = null;
        }
        static JObj Sub(JObj d, string key) { if (!(d[key] is JObj o)) { o = new JObj(); d[key] = o; } return o; }
        static List<object> List(JObj d, string key) { if (!(d[key] is List<object> a)) { a = new List<object>(); d[key] = a; } return a; }

        public void SetName(string name) => Edit((d, _) => Sub(d, "design")["name"] = name);
        public void SetAuthor(string author) => Edit((d, _) => { var x = Sub(d, "design"); if (string.IsNullOrEmpty(author)) d["design"] = Doc.Without(x, "author"); else x["author"] = author; });
        public void SetBaseTheme(string theme) => Edit((d, _) => Sub(d, "design")["base_theme"] = theme);
        public void NextRevision() => Edit((d, _) => Sub(d, "design")["revision"] = Doc.Int(Sub(d, "design")["revision"]) + 1);
        public void SetAppearance(string key, object value) => Edit((d, _) => Sub(d, "appearance")[key] = value is int i ? (long)i : value);
        public void SetSetting(string key, bool value) => Edit((d, _) => Sub(d, "settings")[key] = value);
        public void SetMapShortcuts(string a, string b) => Edit((d, _) => d["map_shortcuts"] = new List<object> { a, b });

        /// <summary>adds a canonical picture (ImageNormalizer's output) to the design's pictures; returns its fingerprint</summary>
        public string AddImage(byte[] canonical)
        {
            string sha = Sha.Of(canonical);
            Edit((d, im) => AddImageTo(d, im, canonical));
            return sha;
        }
        static string AddImageTo(JObj d, Dictionary<string, byte[]> im, byte[] b)
        {
            var info = ImageRules.Inspect(b, DesignProfile.StageW, (long)DesignProfile.StageW * DesignProfile.StageH);
            if (!info.Canonical) throw new ComposeException("design.image", "The picture is not in the console profile yet (" + info.NotCanonical + "); add it through the picture converter.");
            string sha = Sha.Of(b);
            if (!im.ContainsKey(sha))
            {
                im[sha] = b;
                List(d, "images").Add(new JObj { { "sha256", sha }, { "file", FileFor(sha, info.Format) }, { "bytes", (long)b.Length }, { "format", info.Format }, { "width", (long)info.Width }, { "height", (long)info.Height }, { "alpha", info.Alpha } });
            }
            return sha;
        }
        static string FileFor(string sha, string format) => "images/" + sha + (format == "png" ? ".png" : ".jpg");

        /// <summary>the picture in one of DesignProfile.SlotIds (null clears it); a picture nothing else uses any more leaves the design</summary>
        public void SetSlot(string slot, byte[] canonical) => Edit((d, im) =>
        {
            if (!DesignProfile.SlotIds.Contains(slot)) throw new ComposeException("design.slot", "There is no picture place called \"" + Display.Line(slot, 40) + "\" in this version.");
            var slots = Sub(d, "slots"); string old = (slots[slot] as JObj)?["image"] as string;
            if (canonical == null) d["slots"] = Doc.Without(slots, slot);
            else { var sha = AddImageTo(d, im, canonical); if (slots[slot] is JObj s) s["image"] = sha; else slots[slot] = new JObj { { "image", sha } }; }
            DropIfUnused(d, im, old);
        });

        public void SetStartup(string mode, string builtin = null, byte[] canonicalJpeg = null) => Edit((d, im) =>
        {
            var st = Sub(d, "startup"); string old = st["image"] as string;
            var n = new JObj { { "mode", mode } };
            if (mode == "builtin") n["builtin"] = builtin;
            if (mode == "image") n["image"] = AddImageTo(d, im, canonicalJpeg);
            foreach (var kv in st) if (kv.Key == "optional") n["optional"] = kv.Value;   // what a newer version added stays
            d["startup"] = n; DropIfUnused(d, im, old);
        });

        /// <summary>changes (or adds) the layout region with this id; patch sees the region's own object, unknown parts included</summary>
        public void UpsertRegion(string id, Action<JObj> patch) => Edit((d, _) =>
        {
            var regs = List(Sub(d, "layout"), "regions"); var r = regs.OfType<JObj>().FirstOrDefault(x => x["id"] as string == id);
            if (r == null) { r = new JObj { { "id", id } }; regs.Add(r); }
            patch(r);
        });
        /// <summary>the control groups this design moves (ui -> its region), in the HOME's order</summary>
        public Dictionary<string, JObj> ControlRegions => Regions.Where(r => r["kind"] as string == "ui" && r["ui"] is string).GroupBy(r => (string)r["ui"]).ToDictionary(g => g.Key, g => g.First());

        /// <summary>moves one of the base theme's control groups so its top-left corner is at x, y (its size stays the theme's). Refused, with
        /// the reason, when the base theme has no such group or the place breaks a layout rule (DesignProfile.LayoutProblems); nothing changes then</summary>
        public void PlaceRegion(string ui, long x, long y)
        {
            var theme = BaseTheme; var box = DesignProfile.RegionBox(theme, ui);
            if (box == null) throw new ComposeException("design.layout-place", DesignProfile.RegionUnavailable(theme, ui) + ".");
            int w = box[2] - box[0], h = box[3] - box[1], M = DesignProfile.Margin;
            if (x < M || y < M || x + w > DesignProfile.StageW - M || y + h > DesignProfile.StageH - M)
                throw new ComposeException("design.layout-place", "The " + DesignProfile.UiName(ui) + " (" + w + " x " + h + ") fits on the screen with x from " + M + " to " + (DesignProfile.StageW - M - w) + " and y from " + M + " to " + (DesignProfile.StageH - M - h) + ".");
            if ((x != box[0] || y != box[1]) && ImageFor("scene") == null)
                throw new ComposeException("design.layout-place", "Moving the " + DesignProfile.UiName(ui) + " needs the design's own background picture (Pictures: " + DesignProfile.Slots["scene"].Text + "): " + DesignProfile.MovedNeedsScene + ".");
            var placed = ControlRegions.ToDictionary(kv => kv.Key, kv => (Doc.Int(kv.Value["x"]), Doc.Int(kv.Value["y"]), Doc.Int(kv.Value["w"]), Doc.Int(kv.Value["h"])));
            placed[ui] = (x, y, w, h);
            var problems = DesignProfile.LayoutProblems(theme, placed);
            if (problems.Count > 0) throw new ComposeException("design.layout-place", "That place cannot be used: " + string.Join("; ", problems) + ".");
            var existing = ControlRegions.TryGetValue(ui, out var r0) ? r0["id"] as string : null;
            UpsertRegion(existing ?? NewRegionId(ui), r =>
            {
                if (r["hit"] is JObj hit && r["x"] is long ox && r["y"] is long oy) { hit["x"] = Doc.Int(hit["x"]) + x - ox; hit["y"] = Doc.Int(hit["y"]) + y - oy; }   // a kept pointer area moves with its region
                r["kind"] = "ui"; r["ui"] = ui; r["x"] = x; r["y"] = y; r["w"] = (long)w; r["h"] = (long)h;
            });
        }
        string NewRegionId(string ui) { var ids = new HashSet<string>(Regions.Select(r => r["id"] as string)); var id = ui; for (int k = 2; ids.Contains(id); k++) id = ui + "-" + k; return id; }

        public void RemoveRegion(string id) => Edit((d, im) =>
        {
            var regs = List(Sub(d, "layout"), "regions"); var r = regs.OfType<JObj>().FirstOrDefault(x => x["id"] as string == id);
            if (r != null) { regs.Remove(r); DropIfUnused(d, im, r["image"] as string); }
        });
        public void UpsertFolder(string id, Action<JObj> patch) => Edit((d, _) =>
        {
            var fs = List(d, "folders"); var f = fs.OfType<JObj>().FirstOrDefault(x => x["id"] as string == id);
            if (f == null) { f = new JObj { { "id", id } }; fs.Add(f); }
            patch(f);
        });
        /// <summary>removes a folder that has no folders inside it (its games stay in the game order); its picture leaves the design if
        /// nothing else uses it</summary>
        public void RemoveFolder(string id) => Edit((d, im) =>
        {
            var fs = List(d, "folders"); var f = fs.OfType<JObj>().FirstOrDefault(x => x["id"] as string == id); if (f == null) return;
            if (fs.OfType<JObj>().Any(x => x["parent"] as string == id)) throw new ComposeException("design.folder-parent", "Move or remove the folders inside \"" + Display.Line(f["name"] as string, 40) + "\" first.");
            fs.Remove(f);
            if (d["map_shortcuts"] is List<object> ms) for (int i = 0; i < ms.Count; i++) if (ms[i] as string == id) ms[i] = null;
            DropIfUnused(d, im, f["art"] as string);
        });
        public void SetOrder(IEnumerable<GameRef> refs) => Edit((d, _) => d["order"] = refs.Select(r => (object)r.ToJson()).ToList());
        public void AddCredit(string by, string title, string license, string imageSha = null) => Edit((d, _) =>
        {
            var c = new JObj { { "by", by } }; if (title != null) c["title"] = title; if (license != null) c["license"] = license; if (imageSha != null) c["image"] = imageSha;
            List(d, "credits").Add(c);
        });

        static void DropIfUnused(JObj d, Dictionary<string, byte[]> im, string sha)
        {
            if (sha == null || !im.ContainsKey(sha)) return;
            var images = List(d, "images");
            var others = Json.Write(Doc.Without(d, "images"));
            if (others.Contains("\"" + sha + "\"")) return;   // still named somewhere (a slot, a region, a folder, a credit or optional data)
            im.Remove(sha); images.RemoveAll(x => (x as JObj)?["sha256"] as string == sha);
        }

        // ------------------------------------------------------------------ receiving
        static ZipLimits Limits => new ZipLimits
        {
            What = "HOME design", MaxMembers = DesignProfile.MaxImages + 2, MaxArchiveBytes = DesignProfile.MaxArchiveBytes, MaxTotalBytes = DesignProfile.MaxArchiveBytes, MaxRatio = 100,
            MaxMemberBytes = n => n == DesignProfile.ManifestName ? DesignProfile.MaxManifestBytes : n == DesignProfile.ReadmeName ? DesignProfile.MaxReadmeBytes : DesignProfile.MaxImageBytes,
            MustBeStored = n => n.StartsWith("images/"),
        };
        static readonly Regex ImageMember = new Regex("^images/([0-9a-f]{64})\\.(png|jpg)$", RegexOptions.CultureInvariant);

        public static HomeDesign Open(string path)
        {
            var len = new FileInfo(path).Length;
            if (len > DesignProfile.MaxArchiveBytes) throw new ComposeException("zip.too-large", "This HOME design was refused: it is " + Units.Size(len) + "; at most " + Units.Size(DesignProfile.MaxArchiveBytes) + " is accepted.");
            return Open(File.ReadAllBytes(path));
        }

        /// <summary>validates a received .ccshome (see the file header) and returns the design; refusals are ComposeExceptions</summary>
        public static HomeDesign Open(byte[] file)
        {
            using (var z = SafeZip.Open(new MemoryStream(file, false), Limits))
            {
                var me = z.Find(DesignProfile.ManifestName) ?? throw new ComposeException("design.manifest", "This file is not a HOME design: it has no " + DesignProfile.ManifestName + ".");
                var doc = StrictJson.ParseObject(z.ReadAll(me, DesignProfile.MaxManifestBytes), new JsonLimits { MaxBytes = (int)DesignProfile.MaxManifestBytes, MaxDepth = 24, MaxValues = 200000, MaxString = 4000 }, "The design's manifest");
                var h = new HomeDesign { doc = doc, images = new Dictionary<string, byte[]>(), OriginalFile = file, Members = z.Entries.Select(e => (e.Name, e.Size)).ToList() };
                h.Envelope();
                if (h.InspectOnly) { h.InspectScan(); return h; }
                var listed = (Doc.Arr(doc["images"]) ?? new List<object>()).OfType<JObj>().Select(x => x["sha256"] as string).ToList();
                foreach (var e in z.Entries)
                {
                    if (e.Name == DesignProfile.ManifestName) continue;
                    if (e.Name == DesignProfile.ReadmeName)   // never shown or kept (a design writes its own): plain UTF-8 text, nothing else
                    {
                        string t;
                        try { t = new UTF8Encoding(false, true).GetString(z.ReadAll(e, DesignProfile.MaxReadmeBytes)).Replace("\r\n", "\n"); }
                        catch (ArgumentException) { throw new ComposeException("design.readme", "This HOME design was refused: its README is not UTF-8 text."); }
                        var (c, w) = PlainText.Check(t, (int)DesignProfile.MaxReadmeBytes, true);
                        if (c == "text.control" || c == "text.encoding" || c == "text.length") throw new ComposeException("design.readme", "This HOME design was refused: its README " + w + ".");
                        continue;
                    }
                    var m = ImageMember.Match(e.Name);
                    if (!m.Success || !listed.Contains(m.Groups[1].Value)) throw new ComposeException("zip.unlisted", "This HOME design was refused: member \"" + Display.Line(e.Name, 80) + "\" is not one of its listed pictures.");
                }
                foreach (var li in (Doc.Arr(doc["images"]) ?? new List<object>()).OfType<JObj>())
                {
                    string sha = li["sha256"] as string, file2 = li["file"] as string;
                    if (!Sha.Is(sha) || file2 == null) continue;   // Validate names the problem
                    var e = z.Find(file2) ?? throw new ComposeException("design.missing-image", "This HOME design was refused: its picture " + file2 + " is listed but not in the file.");
                    if (e.Size != Doc.Int(li["bytes"])) throw new ComposeException("design.image-size", "This HOME design was refused: picture " + file2 + " is not the size its listing says.");
                    var bytes = z.ReadAll(e, DesignProfile.MaxImageBytes);
                    if (Sha.Of(bytes) != sha) throw new ComposeException("design.image-hash", "This HOME design was refused: picture " + file2 + " is not the picture its fingerprint names (changed or damaged).");
                    h.images[sha] = bytes;
                }
                h.Validate();
                return h;
            }
        }

        /// <summary>apply is blocked (with the reasons) when the design is inspect-only, uses something this version does not know, or is
        /// over the console profile's picture budget</summary>
        public void RequireApplicable()
        {
            if (!CanApply) throw new ComposeException("design.not-applicable", "This HOME design cannot be applied: " + string.Join("; ", Unsupported) + ".");
        }

        /// <summary>the original file exactly as it was received (for a design this version can only inspect)</summary>
        public void ExportOriginal(string path)
        {
            if (OriginalFile == null) throw new ComposeException("design.no-original", "This design was changed here; save it as a new file instead.");
            using (var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) f.Write(OriginalFile, 0, OriginalFile.Length);
        }

        // ------------------------------------------------------------------ writing
        public const string Readme =
            "Classic Cart Studio - Shared HOME Design (.ccshome)\r\n\r\n" +
            "This file holds a design for CCS HOME: its pictures, layout and appearance settings, and a folder and game-order template.\r\n" +
            "It holds no games, no saves, no console system files and nothing that identifies a console. Games are named by their public\r\n" +
            "catalog entries only; when you apply the design, games you do not have are shown so you can choose a replacement or skip them.\r\n\r\n" +
            "Open it with HOME Designer in CCS Console Workbench (on its own, or inside Classic Cart Studio for Windows).\r\n" +
            "Sharing pictures requires the right to share them; the credits inside name who made them.\r\n";

        /// <summary>writes the design as a .ccshome (pictures stored, sorted by fingerprint; the manifest last), checks the written file by
        /// opening it again, then puts it in place; an existing file is never replaced unless overwrite is true</summary>
        public void Save(string path, bool overwrite = false)
        {
            if (InspectOnly) throw new ComposeException("design.inspect-only", "This HOME design was made by a newer version of Classic Cart Studio; export the original file instead.");
            Validate();
            if (File.Exists(path) && !overwrite) throw new ComposeException("design.exists", "A file with that name already exists; choose another name (Save As).");
            var tmp = path + ".partial";
            try
            {
                using (var f = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite))
                {
                    var w = new ZipWriter(f, new DateTime(1980, 1, 1));
                    var rb = Encoding.UTF8.GetBytes(Readme); w.Add(DesignProfile.ReadmeName, true, s => s.Write(rb, 0, rb.Length));
                    foreach (var li in (Doc.Arr(doc["images"]) ?? new List<object>()).OfType<JObj>().OrderBy(x => x["sha256"] as string, StringComparer.Ordinal))
                    { var b = images[li["sha256"] as string]; w.Add(li["file"] as string, false, s => s.Write(b, 0, b.Length)); }
                    var mb = ManifestBytes(); w.Add(DesignProfile.ManifestName, true, s => s.Write(mb, 0, mb.Length));
                    w.Finish(); f.Flush(true);
                }
                var back = Open(File.ReadAllBytes(tmp));
                if (back.ContentSha256 != ContentSha256) throw new IOException("the written design does not read back the same");
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        // ------------------------------------------------------------------ sharing and receiving games
        /// <summary>the design as it may be shared: catalog references the public catalog knows keep their public title; every other game
        /// becomes a numbered placeholder; no text may hold one of the person's private values (private: user and computer names, console
        /// tags, folder locations). Nothing is written; the preview says what is included and what was removed.</summary>
        public DesignSharePreview PrepareShare(Func<string, string> publicCatalogTitle, IEnumerable<string> privateValues)
        {
            var p = new DesignSharePreview();
            if (InspectOnly) { p.Refusals.Add("This design was made by a newer version; it can only be passed on as its original file."); return p; }
            var num = new Dictionary<string, int>(); int next = Math.Max(0, AllRefs(doc).Where(r => r.IsPlaceholder).Select(r => r.Placeholder).DefaultIfEmpty(0).Max()); int removedGames = 0;
            JObj Map(JObj r)
            {
                var g = GameRef.Of(r); if (g.IsPlaceholder) return r;
                var t = publicCatalogTitle(g.Catalog);
                if (t != null) { var n = (JObj)StrictJson.Clone(r); n["title"] = t; return n; }
                if (!num.TryGetValue(g.Catalog, out var k)) { num[g.Catalog] = k = ++next; removedGames++; }
                return new GameRef { Placeholder = k }.ToJson();
            }
            var d = (JObj)StrictJson.Clone(doc);
            d["order"] = (Doc.Arr(d["order"]) ?? new List<object>()).Select(x => (object)Map((JObj)x)).ToList();
            foreach (var f in (Doc.Arr(d["folders"]) ?? new List<object>()).OfType<JObj>()) if (f["games"] is List<object> gs) f["games"] = gs.Select(x => (object)Map((JObj)x)).ToList();
            var shared = new HomeDesign { doc = d, images = new Dictionary<string, byte[]>(images), facts = facts };
            try { shared.Validate(); } catch (ComposeException ex) { p.Refusals.Add(ex.Message); return p; }
            var text = Json.Write(d).ToLowerInvariant();
            foreach (var v in (privateValues ?? Enumerable.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v) && v.Length >= 4))
                if (text.Contains(Json.Write(v).Trim().Trim('"').ToLowerInvariant())) p.Refusals.Add("Some text in the design contains one of your private details (\"" + Display.Line(v, 40) + "\"); change it before sharing.");
            var refs = AllRefs(d);
            p.Included.Add("Design \"" + Display.Line(Name, 60) + "\" for the " + DesignProfile.ThemeName(BaseTheme) + " theme");
            p.Included.Add(Units.Count(images.Count, "picture") + ", " + Units.Size(images.Values.Sum(b => (long)b.Length)) + " (picture metadata was removed when each picture was added)");
            p.Included.Add(Units.Count(Doc.Arr(d["folders"])?.Count ?? 0, "folder") + " and a game order of " + Units.Count(Doc.Arr(d["order"])?.Count ?? 0, "game"));
            p.Included.Add(Units.Count(refs.Count(r => !r.IsPlaceholder), "game reference") + " to the public catalog (titles only; no game files)");
            var cr = (Doc.Arr(d["credits"]) ?? new List<object>()).OfType<JObj>().Select(c => Display.Line(c["by"] as string, 60)).ToList();
            if (cr.Count > 0) p.Included.Add("Credits: " + string.Join(", ", cr));
            if (removedGames > 0) p.Removed.Add(removedGames.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " of your own games that are not in the public catalog: shown to the receiver as \"Your game N\" placeholders (no names or fingerprints)");
            p.Included.Add("Never part of a design: games, saves, console system files, a console's identity, file locations or notes");
            p.Shared = shared; p.CanShare = p.Refusals.Count == 0; return p;
        }

        static List<GameRef> AllRefs(JObj d)
        {
            var l = (Doc.Arr(d["order"]) ?? new List<object>()).OfType<JObj>().Select(GameRef.Of).ToList();
            foreach (var f in (Doc.Arr(d["folders"]) ?? new List<object>()).OfType<JObj>()) l.AddRange((Doc.Arr(f["games"]) ?? new List<object>()).OfType<JObj>().Select(GameRef.Of));
            return l;
        }

        /// <summary>which named games this computer has (by public catalog id), which it lacks and which are placeholders: what the receiver
        /// maps or skips explicitly. Nothing is added, downloaded or removed by asking.</summary>
        public DesignMatch MatchGames(Func<string, bool> haveCatalogId)
        {
            var m = new DesignMatch(); var seen = new HashSet<string>();
            foreach (var r in AllRefs(doc))
            {
                if (!seen.Add(r.IsPlaceholder ? "p" + r.Placeholder : r.Catalog)) continue;
                (r.IsPlaceholder ? m.Placeholders : haveCatalogId(r.Catalog) ? m.Available : m.Missing).Add(r);
            }
            return m;
        }

        public DesignBudget Budget()
        {
            var b = new DesignBudget();
            var listed = (Doc.Arr(doc["images"]) ?? new List<object>()).OfType<JObj>().ToDictionary(x => x["sha256"] as string, x => x);
            var seen = new HashSet<string>(); const long Frame = 1280L * 720 * 3;
            long start = 0, details = 0, folders = 0, icons = 0, compose = 0; int folderArts = 0;
            foreach (var (sha, use, bw, bh) in Steps(doc))
            {
                if (!listed.TryGetValue(sha, out var li)) continue;
                int w = (int)Doc.Int(li["width"]), h = (int)Doc.Int(li["height"]); bool png = li["format"] as string == "png"; long fb = Doc.Int(li["bytes"]);
                if (use == "startup") { b.StartupDecodedBytes = (long)w * h * 4; continue; }
                bool rgb = use == "scene" || use == "details-backdrop" || use == "folder-art";
                long step = DesignBudget.DecodeBytes(w, h, png, fb, rgb ? 3 : 4) + (bw > 0 && (bw != w || bh != h) ? DesignBudget.FitBytes(w, h, bw, bh) : 0);
                if (seen.Add(sha)) { b.Items.Add(new DesignBudget.Item { Sha256 = sha, Use = use, Width = w, Height = h, Bytes = (long)w * h * 4, FileBytes = fb, Png = png, StepBytes = step }); b.DecodeWorkBytes += (long)w * h * 4; }
                if (use == "scene") start = Math.Max(start, step);
                else if (use == "details-backdrop") details = Math.Max(details, step);
                else if (use == "folder-art") { folders = Math.Max(folders, step + DesignBudget.FitBytes(w, h, 272, 72)); folderArts++; }
                else if (use.StartsWith("icon-")) { icons += 128L * 128 * 4; compose = Math.Max(compose, step); }
                else compose = Math.Max(compose, step);   // the frame and each decoration, one at a time, over the background frame
            }
            long tiles = Math.Min(folderArts, 16) * 272L * 72 * 3;   // a Browse page keeps the tiles it shows (at most 16 on screen)
            b.ResidentDecodedBytes = Frame + icons + tiles;
            b.PeakDecodedBytes = Math.Max(Math.Max(start, Frame + compose), Math.Max(details, folders)) + icons + tiles;
            return b;
        }

        /// <summary>every picture the renderer decodes, with the box it is fitted into (0: drawn at its own size or filled)</summary>
        static IEnumerable<(string sha, string use, int bw, int bh)> Steps(JObj d)
        {
            if (d["slots"] is JObj slots) foreach (var kv in slots) if (kv.Value is JObj s && s["image"] is string i) yield return (i, kv.Key, kv.Key.StartsWith("icon-") ? 36 : 0, kv.Key.StartsWith("icon-") ? 35 : 0);
            if (d["startup"] is JObj st && st["image"] is string si) yield return (si, "startup", 0, 0);
            foreach (var f in (Doc.Arr(d["folders"]) ?? new List<object>()).OfType<JObj>()) if (f["art"] is string fa) yield return (fa, "folder-art", 0, 0);
            foreach (var r in (Doc.Arr((d["layout"] as JObj)?["regions"]) ?? new List<object>()).OfType<JObj>()) if (r["image"] is string ri) yield return (ri, "decoration", (int)Doc.Int(r["w"]), (int)Doc.Int(r["h"]));
        }

        /// <summary>every picture use the known fields name: (fingerprint, slot rule id)</summary>
        static IEnumerable<(string sha, string use)> Uses(JObj d)
        {
            if (d["slots"] is JObj slots) foreach (var kv in slots) if (kv.Value is JObj s && s["image"] is string i) yield return (i, kv.Key);
            if (d["startup"] is JObj st && st["image"] is string si) yield return (si, "startup");
            foreach (var f in (Doc.Arr(d["folders"]) ?? new List<object>()).OfType<JObj>()) if (f["art"] is string fa) yield return (fa, "folder-art");
            foreach (var r in (Doc.Arr((d["layout"] as JObj)?["regions"]) ?? new List<object>()).OfType<JObj>()) if (r["image"] is string ri) yield return (ri, "decoration");
        }

        // ------------------------------------------------------------------ validation
        static ComposeException Refuse(string code, string where, string why) => new ComposeException(code, "This HOME design was refused: " + where + " " + why + ".");

        void Envelope()
        {
            if (doc["format"] as string != DesignProfile.Format) throw Refuse("design.format", "the file", "is not a HOME design (its format is not " + DesignProfile.Format + ")");
            var schema = doc["schema"] as string; var m = Regex.Match(schema ?? "", "^ccs-home-design/([0-9]{1,3})$");
            if (!m.Success) throw Refuse("design.format", "the file", "does not name a HOME design schema");
            Unsupported = new List<string>(); InspectOnly = false;
            if (m.Groups[1].Value != "1") { Unsupported.Add("it was made with a newer HOME design format (" + Display.Line(schema, 40) + ")"); InspectOnly = true; }
            if (doc.Has("requires"))
            {
                var req = Doc.Arr(doc["requires"]) ?? throw Refuse("design.type", "\"requires\"", "is not a list");
                if (req.Count > 16) throw Refuse("design.bounds", "\"requires\"", "lists more than 16 capabilities");
                foreach (var r in req)
                {
                    if (!(r is string s) || !Regex.IsMatch(s, "^[a-z][a-z0-9-]{0,39}(/[0-9]{1,3})?$")) throw Refuse("design.type", "\"requires\"", "has an entry that is not a capability name");
                    if (!DesignProfile.KnownCapabilities.Contains(s)) { Unsupported.Add("it needs a capability this version does not have (" + s + ")"); InspectOnly = true; }
                }
            }
            var design = doc["design"] as JObj ?? throw Refuse("design.type", "\"design\"", "is missing");
            if (!NewId.Is(design["id"] as string)) throw Refuse("design.type", "the design id", "is not 32 lowercase hex digits");
            Text(design["name"] as string, "the design name", 60, required: true);
            CanApply = !InspectOnly;
        }

        /// <summary>inspect mode: every value of the whole document follows the inert-data rules (plain text, no links, locations, markup or
        /// console identities; whole fingerprints and ids allowed as values); nothing else is interpreted</summary>
        void InspectScan()
        {
            bool Fp(string s) => Sha.Is(s) || NewId.Is(s) || GameRef.CatalogId.IsMatch(s) || Regex.IsMatch(s, "^images/[0-9a-f]{64}\\.(png|jpg)$");
            void Walk(object v, string where, int depth)
            {
                if (depth > 24) throw Refuse("design.bounds", where, "is nested too deeply");
                switch (v)
                {
                    case string s: if (Fp(s)) return; var (c, w) = PlainText.Check(s, 4000, true); if (c != null) throw Refuse("design." + c.Substring(5), where, w); return;
                    case JObj o: foreach (var kv in o) { if (!Regex.IsMatch(kv.Key, "^[a-z][a-z0-9_-]{0,47}$")) throw Refuse("design.key", where, "has a member name that is not allowed (\"" + Display.Line(kv.Key, 40) + "\")"); Walk(kv.Value, where + "." + kv.Key, depth + 1); } return;
                    case List<object> a: for (int i = 0; i < a.Count; i++) Walk(a[i], where + "[" + i + "]", depth + 1); return;
                }
            }
            Walk(doc, "the design", 0);
        }

        static void Text(string s, string where, int max, bool required = false, bool multiline = false)
        {
            if (s == null) { if (required) throw Refuse("design.type", where, "is missing"); return; }
            if (required && s.Trim().Length == 0) throw Refuse("design.type", where, "is empty");
            var (c, w) = PlainText.Check(s, max, multiline);
            if (c != null) throw Refuse("design." + c.Substring(5), where, w);
        }

        sealed class V
        {
            public HomeDesign H; public HashSet<string> Listed = new HashSet<string>(); public Dictionary<string, JObj> ListedInfo = new Dictionary<string, JObj>();
            public void Unsupported(string why) { H.Unsupported.Add(why); H.CanApply = false; }
            public void Keys(JObj o, string where, params string[] allowed)
            {
                foreach (var kv in o)
                {
                    if (kv.Key == "optional") { Inert.Check(kv.Value is JObj ? kv.Value : throw Refuse("design.type", where + ".optional", "is not an object"), where + ".optional", "design", s => Listed.Contains(s)); continue; }
                    if (!allowed.Contains(kv.Key)) throw Refuse("design.key", where, "has \"" + Display.Line(kv.Key, 40) + "\", which is not part of a HOME design");
                }
            }
            public long Int(JObj o, string k, string where, long min, long max, bool required = false)
            {
                if (!o.Has(k)) { if (required) throw Refuse("design.type", where + "." + k, "is missing"); return long.MinValue; }
                if (!(o[k] is long v)) throw Refuse("design.type", where + "." + k, "is not a whole number");
                if (v < min || v > max) throw Refuse("design.bounds", where + "." + k, "is outside " + min + " to " + max);
                return v;
            }
            public bool? Bool(JObj o, string k, string where) { if (!o.Has(k)) return null; if (!(o[k] is bool b)) throw Refuse("design.type", where + "." + k, "is not true or false"); return b; }
            public string Enum(JObj o, string k, string where, string[] values, bool required = false)
            {
                if (!o.Has(k)) { if (required) throw Refuse("design.type", where + "." + k, "is missing"); return null; }
                if (!(o[k] is string s) || !Regex.IsMatch(s, "^[a-z0-9][a-z0-9_-]{0,47}$")) throw Refuse("design.type", where + "." + k, "is not a name");
                if (!values.Contains(s)) { Unsupported(where + "." + k + " is \"" + s + "\", which this version does not know"); return null; }
                return s;
            }
            public string Image(JObj o, string k, string where, string rule, bool required = false)
            {
                if (!o.Has(k) || o[k] == null) { if (required) throw Refuse("design.type", where + "." + k, "is missing"); return null; }
                if (!(o[k] is string s) || !Sha.Is(s)) throw Refuse("design.type", where + "." + k, "is not a picture fingerprint");
                if (!Listed.Contains(s)) throw Refuse("design.missing-image", where + "." + k, "names a picture the design does not carry");
                var li = ListedInfo[s]; var r = DesignProfile.Slots[rule];
                var info = new ImageInfo { Format = li["format"] as string, Width = (int)Doc.Int(li["width"]), Height = (int)Doc.Int(li["height"]), Alpha = li["alpha"] is bool a && a };
                var (c, w) = r.Check(info, Doc.Int(li["bytes"]));
                if (c != null) throw Refuse(c, where + "." + k + " (" + r.Text + ")", w);
                return s;
            }
            public string Color(JObj o, string k, string where) { if (!o.Has(k)) return null; if (!(o[k] is string s) || !Regex.IsMatch(s, "^#[0-9a-f]{6}$")) throw Refuse("design.type", where + "." + k, "is not a color (#rrggbb)"); return s; }
        }

        static double Luminance(string hex)
        {
            double C(int v) { double c = v / 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
            int r = Convert.ToInt32(hex.Substring(1, 2), 16), g = Convert.ToInt32(hex.Substring(3, 2), 16), b = Convert.ToInt32(hex.Substring(5, 2), 16);
            return 0.2126 * C(r) + 0.7152 * C(g) + 0.0722 * C(b);
        }
        public static double Contrast(string a, string b) { double x = Luminance(a), y = Luminance(b); return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05); }

        /// <summary>the full apply-mode check of the document and pictures (Open, every edit, Save); sets CanApply and Unsupported</summary>
        void Validate()
        {
            Envelope();
            if (InspectOnly) { InspectScan(); return; }
            var v = new V { H = this };
            // pictures first: everything else (optional data included) may name them
            var imgs = Doc.Arr(doc["images"]) ?? throw Refuse("design.type", "\"images\"", "is missing or not a list");
            if (imgs.Count > DesignProfile.MaxImages) throw Refuse("design.bounds", "\"images\"", "lists more than " + DesignProfile.MaxImages + " pictures");
            for (int i = 0; i < imgs.Count; i++)
            {
                var w = "images[" + i + "]"; var li = imgs[i] as JObj ?? throw Refuse("design.type", w, "is not an object");
                v.Keys(li, w, "sha256", "file", "bytes", "format", "width", "height", "alpha");
                var sha = li["sha256"] as string; if (!Sha.Is(sha)) throw Refuse("design.type", w + ".sha256", "is not a SHA-256 fingerprint");
                if (!v.Listed.Add(sha)) throw Refuse("design.duplicate", w, "lists a picture twice");
                var fmt = li["format"] as string; if (fmt != "png" && fmt != "jpeg") throw Refuse("design.type", w + ".format", "is not png or jpeg");
                if (li["file"] as string != FileFor(sha, fmt)) throw Refuse("design.type", w + ".file", "is not images/<its fingerprint>." + (fmt == "png" ? "png" : "jpg"));
                v.Int(li, "bytes", w, 1, DesignProfile.MaxImageBytes, true); v.Int(li, "width", w, 1, DesignProfile.StageW, true); v.Int(li, "height", w, 1, DesignProfile.StageH, true);
                if (v.Bool(li, "alpha", w) == null) throw Refuse("design.type", w + ".alpha", "is missing");
                if (!images.TryGetValue(sha, out var bytes)) throw Refuse("design.missing-image", w, "is listed but not carried");
                ImageInfo info;
                try { if (!facts.TryGetValue(sha, out info)) facts[sha] = info = ImageRules.Inspect(bytes, DesignProfile.StageW, (long)DesignProfile.StageW * DesignProfile.StageH); }
                catch (ComposeException ex) { throw new ComposeException(ex.Code, "This HOME design was refused: " + li["file"] + ": " + ex.Message); }
                if (!info.Canonical) throw Refuse("design.image-profile", li["file"] as string, "is not in the console profile (" + info.NotCanonical + ")");
                if (info.Format != fmt || info.Width != Doc.Int(li["width"]) || info.Height != Doc.Int(li["height"]) || info.Alpha != (bool)li["alpha"] || bytes.Length != Doc.Int(li["bytes"]))
                    throw Refuse("design.image-listing", li["file"] as string, "is not what its listing says (kind, size or transparency)");
                v.ListedInfo[sha] = li;
            }
            foreach (var extra in images.Keys.Where(k => !v.Listed.Contains(k)).ToList()) images.Remove(extra);
            v.Keys(doc, "the design", "format", "schema", "minor", "requires", "profile", "design", "credits", "images", "slots", "appearance", "layout", "folders", "order", "map_shortcuts", "settings", "startup");
            v.Int(doc, "minor", "the design", 0, 999);
            if (doc.Has("profile") && doc["profile"] as string != DesignProfile.Profile) v.Unsupported("it was made for another screen profile (" + Display.Line(doc["profile"] as string, 40) + ")");
            // the design itself
            var design = (JObj)doc["design"]; v.Keys(design, "design", "id", "revision", "name", "base_theme", "author");
            v.Int(design, "revision", "design", 1, 1000000, true); v.Enum(design, "base_theme", "design", DesignProfile.Themes, true);
            Text(design["author"] as string, "design.author", 60); if (design.Has("author") && !(design["author"] is string)) throw Refuse("design.type", "design.author", "is not text");
            // credits
            var credits = Doc.Arr(doc["credits"]) ?? (doc.Has("credits") ? throw Refuse("design.type", "\"credits\"", "is not a list") : new List<object>());
            if (credits.Count > DesignProfile.MaxCredits) throw Refuse("design.bounds", "\"credits\"", "has more than " + DesignProfile.MaxCredits + " entries");
            for (int i = 0; i < credits.Count; i++)
            {
                var w = "credits[" + i + "]"; var c = credits[i] as JObj ?? throw Refuse("design.type", w, "is not an object");
                v.Keys(c, w, "by", "title", "license", "image");
                Text(c["by"] as string, w + ".by", 80, required: true); Text(c["title"] as string, w + ".title", 120); Text(c["license"] as string, w + ".license", 80);
                foreach (var k in new[] { "title", "license" }) if (c.Has(k) && !(c[k] is string)) throw Refuse("design.type", w + "." + k, "is not text");
                if (c.Has("image") && c["image"] != null && !(c["image"] is string s && v.Listed.Contains(s))) throw Refuse("design.missing-image", w + ".image", "names a picture the design does not carry");
            }
            // slots
            if (doc.Has("slots"))
            {
                var slots = doc["slots"] as JObj ?? throw Refuse("design.type", "\"slots\"", "is not an object");
                foreach (var kv in slots)
                {
                    if (kv.Key == "optional") { v.Keys(new JObj { { "optional", kv.Value } }, "slots"); continue; }
                    var w = "slots." + Display.Line(kv.Key, 40); var s = kv.Value as JObj ?? throw Refuse("design.type", w, "is not an object");
                    if (!DesignProfile.SlotIds.Contains(kv.Key))
                    {
                        if (!Regex.IsMatch(kv.Key, "^[a-z][a-z0-9-]{0,39}$")) throw Refuse("design.key", "slots", "has a slot name that is not allowed");
                        v.Unsupported("it uses a picture place this version does not know (" + kv.Key + ")"); v.Keys(s, w, "image");
                        if (!(s["image"] is string si && v.Listed.Contains(si))) throw Refuse("design.missing-image", w + ".image", "names a picture the design does not carry");
                        continue;
                    }
                    v.Keys(s, w, "image"); v.Image(s, "image", w, kv.Key, true);
                }
            }
            // appearance
            if (doc.Has("appearance"))
            {
                var a = doc["appearance"] as JObj ?? throw Refuse("design.type", "\"appearance\"", "is not an object");
                v.Keys(a, "appearance", "text_color", "accent_color", "panel_color", "panel_opacity", "title_font", "text_font");
                var tc = v.Color(a, "text_color", "appearance"); var ac = v.Color(a, "accent_color", "appearance"); var pc = v.Color(a, "panel_color", "appearance");
                if (ac != null && Contrast(ac, "#ffffff") < 4.5) v.Unsupported("appearance.accent_color is too light for the white lettering the console HOME draws on it (focused menu rows and buttons, the A and B badges: contrast " + Contrast(ac, "#ffffff").ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ":1; at least 4.5:1)");
                v.Int(a, "panel_opacity", "appearance", 0, 100); var tf = v.Enum(a, "title_font", "appearance", DesignProfile.Fonts); var xf = v.Enum(a, "text_font", "appearance", DesignProfile.Fonts);
                if (tf != null && DesignProfile.FontPx(tf) > DesignProfile.MaxMenuTitlePx) v.Unsupported("appearance.title_font is " + DesignProfile.FontPx(tf) + " px; the console HOME's menu titles are at most " + DesignProfile.MaxMenuTitlePx + " px");
                if (xf != null && (DesignProfile.FontPx(xf) < DesignProfile.MinMenuTextPx || DesignProfile.FontPx(xf) > DesignProfile.MaxMenuTextPx)) v.Unsupported("appearance.text_font is " + DesignProfile.FontPx(xf) + " px; the console HOME's menu text is " + DesignProfile.MinMenuTextPx + " to " + DesignProfile.MaxMenuTextPx + " px");
                if (tc != null && pc != null && Contrast(tc, pc) < 4.5) throw Refuse("design.contrast", "appearance.text_color", "is too close to the panel color to read (contrast " + Contrast(tc, pc).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ":1; at least 4.5:1)");
            }
            // layout
            if (doc.Has("layout"))
            {
                var lay = doc["layout"] as JObj ?? throw Refuse("design.type", "\"layout\"", "is not an object");
                v.Keys(lay, "layout", "regions");
                var regs = Doc.Arr(lay["regions"]) ?? (lay.Has("regions") ? throw Refuse("design.type", "layout.regions", "is not a list") : new List<object>());
                if (regs.Count > DesignProfile.MaxRegions) throw Refuse("design.bounds", "layout.regions", "has more than " + DesignProfile.MaxRegions + " regions");
                var ids = new HashSet<string>(); var uiSeen = new HashSet<string>(); var focus = new HashSet<long>(); var uiRects = new List<(string id, long x, long y, long w, long h)>(); int deco = 0;
                var placed = new Dictionary<string, (long x, long y, long w, long h)>();
                for (int i = 0; i < regs.Count; i++)
                {
                    var r = regs[i] as JObj ?? throw Refuse("design.type", "layout.regions[" + i + "]", "is not an object");
                    var id = r["id"] as string; var w = "layout region " + (id != null && Regex.IsMatch(id, "^[a-z][a-z0-9-]{0,31}$") ? "\"" + id + "\"" : "#" + (i + 1));
                    if (id == null || !Regex.IsMatch(id, "^[a-z][a-z0-9-]{0,31}$")) throw Refuse("design.type", w, "has no valid id");
                    if (!ids.Add(id)) throw Refuse("design.duplicate", w, "uses an id another region uses");
                    var kind = r["kind"] as string;
                    if (kind != "ui" && kind != "image" && kind != "label" && kind != "panel")
                    {
                        if (kind == null || !Regex.IsMatch(kind, "^[a-z][a-z0-9-]{0,31}$")) throw Refuse("design.type", w + ".kind", "is missing or not a name");
                        v.Unsupported(w + " is of a kind this version does not know (" + kind + ")"); Inert.Check(Doc.Without(Doc.Without(r, "id"), "kind"), w, "design", s => v.Listed.Contains(s)); continue;
                    }
                    long x = v.Int(r, "x", w, 0, 1280, true), y = v.Int(r, "y", w, 0, 720, true), rw = v.Int(r, "w", w, 1, 1280, true), rh = v.Int(r, "h", w, 1, 720, true);
                    int M = DesignProfile.Margin;
                    if (x < M || y < M || x + rw > DesignProfile.StageW - M || y + rh > DesignProfile.StageH - M)
                        throw Refuse("design.layout-bounds", w, "reaches within " + M + " px of the screen edge or beyond it (" + x + "," + y + " " + rw + "x" + rh + "; the area is " + M + "," + M + " to " + (DesignProfile.StageW - M) + "," + (DesignProfile.StageH - M) + ")");
                    if (kind == "ui")
                    {
                        v.Keys(r, w, "id", "kind", "ui", "x", "y", "w", "h", "focus", "hit");
                        var ui = v.Enum(r, "ui", w, DesignProfile.UiRegions.Keys.ToArray(), true);
                        if (ui == null) continue;
                        if (!uiSeen.Add(ui)) throw Refuse("design.duplicate", w, "places the HOME's " + ui + " region a second time");
                        placed[ui] = (x, y, rw, rh);
                        var (mw, mh) = DesignProfile.UiRegions[ui];
                        if (rw < mw || rh < mh) throw Refuse("design.layout-size", w, "is smaller than the " + ui + " region can be read at (" + mw + "x" + mh + ")");
                        var f = v.Int(r, "focus", w, 1, DesignProfile.MaxFocus);
                        if (f != long.MinValue && !focus.Add(f)) throw Refuse("design.layout-focus", w, "uses a focus position another region uses");
                        if (r.Has("hit"))
                        {
                            var hit = r["hit"] as JObj ?? throw Refuse("design.type", w + ".hit", "is not an object");
                            v.Keys(hit, w + ".hit", "x", "y", "w", "h");
                            long hx = v.Int(hit, "x", w + ".hit", 0, 1280, true), hy = v.Int(hit, "y", w + ".hit", 0, 720, true), hw = v.Int(hit, "w", w + ".hit", 1, 1280, true), hh = v.Int(hit, "h", w + ".hit", 1, 720, true);
                            if (hx < x || hy < y || hx + hw > x + rw || hy + hh > y + rh) throw Refuse("design.layout-hit", w, "has a pointer area outside the region");
                        }
                        foreach (var o in uiRects) if (x < o.x + o.w && o.x < x + rw && y < o.y + o.h && o.y < y + rh) throw Refuse("design.layout-overlap", w, "overlaps the HOME's region \"" + o.id + "\" (controls must not cover each other)");
                        uiRects.Add((id, x, y, rw, rh));
                        continue;
                    }
                    if (++deco > DesignProfile.MaxDecorations) throw Refuse("design.bounds", "layout.regions", "has more than " + DesignProfile.MaxDecorations + " decorations");
                    v.Int(r, "layer", w, 0, DesignProfile.MaxDecorLayer); v.Int(r, "opacity", w, 0, 100);
                    if (kind == "image") { v.Keys(r, w, "id", "kind", "x", "y", "w", "h", "layer", "opacity", "image"); v.Image(r, "image", w, "decoration", true); }
                    else if (kind == "panel") { v.Keys(r, w, "id", "kind", "x", "y", "w", "h", "layer", "opacity", "color"); if (v.Color(r, "color", w) == null) throw Refuse("design.type", w + ".color", "is missing"); }
                    else
                    {
                        v.Keys(r, w, "id", "kind", "x", "y", "w", "h", "layer", "opacity", "text", "font", "color", "align");
                        Text(r["text"] as string, w + ".text", 60, required: true); if (!(r["text"] is string)) throw Refuse("design.type", w + ".text", "is not text");
                        var font = v.Enum(r, "font", w, DesignProfile.Fonts, true); v.Color(r, "color", w); v.Enum(r, "align", w, new[] { "left", "center", "right" });
                        if (font != null)
                        {
                            int px = DesignProfile.FontPx(font);
                            if (px < DesignProfile.MinLabelPx) throw Refuse("design.layout-size", w, "uses a font smaller than " + DesignProfile.MinLabelPx + " px (too small to read on a TV)");
                            if (rh < px + 4 || rw < 24) throw Refuse("design.layout-size", w, "is too small for its text (at least 24 px wide and " + (px + 4) + " px high)");
                        }
                    }
                }
                // the moved control groups in the base theme: viewable whatever they are, applicable only where that theme can show them
                var bt = (doc["design"] as JObj)?["base_theme"] as string;
                if (placed.Count > 0 && DesignProfile.Themes.Contains(bt))
                {
                    foreach (var why in DesignProfile.LayoutProblems(bt, placed)) v.Unsupported("layout: " + why);
                    if (ImageFor("scene") == null && placed.Any(kv => { var b = DesignProfile.RegionBox(bt, kv.Key); return b != null && (kv.Value.x != b[0] || kv.Value.y != b[1]); }))
                        v.Unsupported("layout: moving the HOME's controls needs the design's own background picture: " + DesignProfile.MovedNeedsScene);
                }
            }
            // folders: unique ids, existing parents, no cycles, at most five levels
            var folders = Doc.Arr(doc["folders"]) ?? (doc.Has("folders") ? throw Refuse("design.type", "\"folders\"", "is not a list") : new List<object>());
            if (folders.Count > DesignProfile.MaxFolders) throw Refuse("design.bounds", "\"folders\"", "has more than " + DesignProfile.MaxFolders + " folders");
            var parent = new Dictionary<string, string>(); long refs = 0;
            for (int i = 0; i < folders.Count; i++)
            {
                var f = folders[i] as JObj ?? throw Refuse("design.type", "folders[" + i + "]", "is not an object");
                var id = f["id"] as string; var w = "folder " + (id != null && Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{0,39}$") ? "\"" + id + "\"" : "#" + (i + 1));
                if (id == null || !Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{0,39}$")) throw Refuse("design.type", w, "has no valid id");
                if (parent.ContainsKey(id)) throw Refuse("design.duplicate", w, "uses an id another folder uses");
                v.Keys(f, w, "id", "name", "parent", "pos", "art", "games");
                Text(f["name"] as string, w + ".name", 40, required: true); if (!(f["name"] is string)) throw Refuse("design.type", w + ".name", "is not text");
                if (f.Has("parent") && f["parent"] != null && !(f["parent"] is string)) throw Refuse("design.type", w + ".parent", "is not a folder id");
                parent[id] = f["parent"] as string;
                v.Int(f, "pos", w, 0, 9999); v.Image(f, "art", w, "folder-art");
                var gs = Doc.Arr(f["games"]) ?? (f.Has("games") ? throw Refuse("design.type", w + ".games", "is not a list") : new List<object>());
                if (gs.Count > DesignProfile.MaxFolderGames) throw Refuse("design.bounds", w + ".games", "has more than " + DesignProfile.MaxFolderGames + " games");
                refs += gs.Count; Refs(v, gs, w + ".games");
            }
            foreach (var id in parent.Keys)
            {
                int depth = 0; var cur = id; var path = new HashSet<string>();
                while (cur != null)
                {
                    if (!path.Add(cur)) throw Refuse("design.folder-cycle", "folder \"" + id + "\"", "is inside itself (a folder cycle)");
                    if (!parent.ContainsKey(cur)) throw Refuse("design.folder-parent", "folder \"" + id + "\"", "is inside a folder the design does not have");
                    if (++depth > DesignProfile.MaxFolderDepth) throw Refuse("design.folder-depth", "folder \"" + id + "\"", "is nested more than " + DesignProfile.MaxFolderDepth + " levels deep");
                    cur = parent[cur];
                }
            }
            // order, shortcuts, settings, startup
            var order = Doc.Arr(doc["order"]) ?? (doc.Has("order") ? throw Refuse("design.type", "\"order\"", "is not a list") : new List<object>());
            if (order.Count > DesignProfile.MaxOrder) throw Refuse("design.bounds", "\"order\"", "has more than " + DesignProfile.MaxOrder + " games");
            refs += order.Count; Refs(v, order, "order", unique: true);
            if (refs > DesignProfile.MaxRefs) throw Refuse("design.bounds", "the design", "names more than " + DesignProfile.MaxRefs + " games in all");
            if (doc.Has("map_shortcuts"))
            {
                var ms = Doc.Arr(doc["map_shortcuts"]);
                if (ms == null || ms.Count != 2) throw Refuse("design.type", "\"map_shortcuts\"", "is not a list of two entries");
                foreach (var s in ms) if (s != null && !(s is string sid && parent.ContainsKey(sid))) throw Refuse("design.folder-parent", "\"map_shortcuts\"", "names a folder the design does not have");
            }
            if (doc.Has("settings"))
            {
                var s = doc["settings"] as JObj ?? throw Refuse("design.type", "\"settings\"", "is not an object");
                v.Keys(s, "settings", "reduced_motion", "hints", "music"); v.Bool(s, "reduced_motion", "settings"); v.Bool(s, "hints", "settings"); v.Bool(s, "music", "settings");
            }
            if (doc.Has("startup"))
            {
                var s = doc["startup"] as JObj ?? throw Refuse("design.type", "\"startup\"", "is not an object");
                var mode = v.Enum(s, "mode", "startup", new[] { "random", "builtin", "image" }, true);
                v.Keys(s, "startup", "mode", "builtin", "image");
                if (mode == "builtin") v.Enum(s, "builtin", "startup", DesignProfile.StartupBuiltins, true);
                if (mode == "image") v.Image(s, "image", "startup", "startup", true);
                if (mode != "builtin" && s.Has("builtin") || mode != "image" && s.Has("image") && mode != null) throw Refuse("design.type", "\"startup\"", "names a picture its mode does not use");
            }
            var budget = Budget();
            if (!budget.Ok) v.Unsupported(budget.Text);
        }

        static void Refs(V v, List<object> refs, string where, bool unique = false)
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < refs.Count; i++)
            {
                var w = where + "[" + i + "]"; var r = refs[i] as JObj ?? throw Refuse("design.type", w, "is not a game reference");
                if (r.Has("placeholder"))
                {
                    v.Keys(r, w, "placeholder"); var n = v.Int(r, "placeholder", w, 1, 99999, true);
                    if (unique && !seen.Add("p" + n)) throw Refuse("design.duplicate", w, "lists the same game twice");
                    continue;
                }
                v.Keys(r, w, "catalog", "title");
                if (!(r["catalog"] is string c) || !GameRef.CatalogId.IsMatch(c))
                    throw Refuse(r["catalog"] is string pc && pc.StartsWith("p_") ? "design.private-game" : "design.type", w + ".catalog", "is not a public catalog reference (a game of the sender's own is never named in a design)");
                Text(r["title"] as string, w + ".title", 120); if (r.Has("title") && !(r["title"] is string)) throw Refuse("design.type", w + ".title", "is not text");
                if (unique && !seen.Add(c)) throw Refuse("design.duplicate", w, "lists the same game twice");
            }
        }
    }
}
