# HOME Designer: the console side, the compiler and the render check

The Shared HOME Design file (`.ccshome`, `ccs-home-design/1`) and its validator are described in `COMPOSER-FORMATS.md`. This file
covers what happens after a design is made: the **one compiler** that turns it into what the console's native HOME reads, the **render
check** that draws every screen with the native HOME before anything is written, the **control regions** (the HOME's own controls a
design moves), the **on-console layout** with its journal, fallback and Revert Design, the **budgets** (measured), what the console
renderer draws and **what it does not**, **Classic Cart Studio for Windows' path** (a collection's carried design compiled by the same
compiler), the **preview drawn by the console HOME** in the wizard, and the **host ABI proposal** for the standalone Workbench.

Code: `CcsBackup/Compose/DesignCompiler.cs`, `CcsBackup/Compose/DesignRenderCheck.cs`, `CcsBackup/Engine/DesignApplyEngine.cs`
(engine, both Windows hosts); `tools/console-simulator/core/design.h`, `design.c`, `design_check.h`, `design_check.c` and the design
parts of `home.c` (the native HOME on the console, the simulator and the render check); `tools/companion/native_home/design_ingest.py`
(the Python package builder takes the compiled bytes as they are); `tools/companion/native_home/design_compile.py` (the app's package
builder runs the one compiler, `CcsBackup/Compose/DesignCompileCli.cs`, through the host program); `tools/companion/design_embed.py` and
`studio/startup-custom.js` (a collection carries a design losslessly); `CcsBackupUi/WorkbenchDesigner*.cs` (the wizard, its layout
step and its preview). Tests: engine group `designer` and `compose`; `tools/companion/test_home_design_carry.py`.

## The renderer profile `nes-classic-home-1280x720/1`

A design **skins its own base theme** (`design.base_theme`): its pictures, decorations and icons are drawn in that theme only, because
they are placed against that theme's layout. Its colors, menu lettering, folders, game order, startup choice and HOME Music default
apply in every theme. The other three themes stay as they come and remain one press away (START > Change Theme). When a design is
applied, the console switches to its base theme once (recorded in `home-prefs.json` as `"design": <id>`); after that the person's own
theme choice wins, as with the Windows "apply theme" request.

### Picture places the console draws

| Slot | Size | What the console does with it | Themes |
|---|---|---|---|
| `scene` | exactly 1280x720 | replaces the theme's background picture; the theme's own frames, tabs, signs and panels (its sprites) are drawn over it, with only their own pixels (below, "The theme's pictures over a design's background") | base theme |
| `plate` | at most 1256x696, PNG with see-through | drawn centered over the background, under every decoration | base theme |
| decorations (`image` regions) | at most 1256x696 | fitted whole (centered, not stretched) into their region, in layer order 0 to 7, at their opacity | base theme |
| `panel` / `label` regions | | a filled rectangle at its opacity; one line of text in one of the HOME's own fonts, aligned in its box, clipped to it (a face with only capitals draws the text in capitals) | base theme |
| `details-backdrop` | exactly 1272x712 | the Game Details background for every game, inside the theme's 4 px frame (in place of each game's own scene) | base theme |
| `icon-games`, `icon-folder`, `icon-star`, `icon-clock`, `icon-gear` | at most 128x128 PNG | fitted whole (see-through around it) into the size the theme draws that icon at | Arcade Grid: all five (`icon-games` also its GAMES stat); Featured Shelf: `icon-games` (as its home icon), `icon-folder`, `icon-star`, `icon-gear`; Box Art Showcase and Adventure Map draw none |
| folder pictures (`folders[].art`) | at most 544x148 | the folder's Browse tile picture (object-fit cover), in every theme | all |
| `startup` (startup.mode `image`) | 1280x720 JPEG, at most 450,000 bytes | shown first by the game player, before the HOME; `builtin` and `random` choose among the five | all |

`DesignProfile.ThemeDraws(theme, slot)` and `SlotsOf(theme)` publish this table; the compiler lists a picture the base theme does not
draw under `CompiledDesign.NotShown` (kept in the design, not shown).

### Appearance

| Member | Where the console uses it |
|---|---|
| `accent_color` | the HOME's focus and highlight color in every theme (focus rings, the focused menu row, the Browse focus, Details buttons in focus, badges) |
| `panel_color`, `panel_opacity` | the shared panels in every theme: the START menu, Settings, the theme picker, notices, the Browse panel |
| `text_color` | the text in those panels (titles and items) and a label's color when the label names none |
| `title_font`, `text_font` | the shared panels' title and item lettering (at most 16 px for titles, 13 to 22 px for text: `DesignProfile.MaxMenuTitlePx`, `MinMenuTextPx`, `MaxMenuTextPx`); the menus size themselves to the lettering, and the render check fails a menu that no longer fits its panel inside the screen's safe area |

### Folders, game order, settings, startup

- `folders`: the design's folders, nesting and membership **replace** the collection's own folders in Browse (and the two Adventure Map
  folder signs, from `map_shortcuts`). A design with no folders leaves the collection's folders as they are. Games are named by the
  console's own game ids after compiling (catalog references, and placeholders the receiver mapped); a game the console does not have is
  left out (`CompiledDesign.LeftOut`), never invented.
- `order`: the games the design names come first, in its order, then every other game in the collection's own order. An empty order
  keeps the collection's order. Automatic categories follow the new order.
- `settings.reduced_motion`: no scrolling titles (a long title ends with an ellipsis instead). `settings.music`: HOME Music starts On or
  Off (the person's own choice in Settings still wins). `settings.hints`: kept, **not used** (the console always shows its controller
  hints: the controller can focus them).
- `startup`: see the slot table. A design startup picture that cannot be read falls back to the package's own startup choice, then to the
  plain card, as before.

### Control regions (the HOME's own controls a design moves)

In its base theme a design may move the HOME's own controls in **groups** (`ui` regions: `games`, `details`, `title`, `status`, `hints`).
A group **moves whole and keeps the theme's size**: every box the theme's renderer reads for it, its sprites, its focus ring and shadow,
its mouse targets and its layout records move together (`home.c` `region_move`, `MOVED_SPRITES`; each theme moves its geometry in
`init`; Box Art Showcase moves its perspective with its boxes, so they look the same where they go). The controller's focus and the
pointer therefore follow what is drawn. Each theme's groups, as the console HOME computes them from its package's geometry
(`region_default`) and `DesignProfile.RegionBox` publishes them (boxes x0,y0 to x1,y1 on the 1280x720 stage):

| Theme | `games` | `details` | `title` | `status` | `hints` | Never moves |
|---|---|---|---|---|---|---|
| Arcade Grid | the card and its 16 cover cells, with the focus ring: 243,97 to 1237,612 | **none**: the selected game's card is part of the cover grid (its first two rows beside the card, the last under it), so it moves with the game list | **none**: the title is the card's caption | the stats: 17,98 to 233,306 | 31,650 to 1256,700 | the tab bar 312,46 to 1142,95 |
| Box Art Showcase | the boxes, their arrows and dots: 14,65 to 1266,458 | the overview, rule and facts: 54,547 to 680,660 | 96,458 to 628,529 | the collection panel: 680,481 to 955,672 | 38,668 to 659,700 | the top navigation with its mouse targets: 681,14 to 1133,51 |
| Featured Shelf | the shelf with its focus frame and arrows: 182,455 to 1233,624 | the facts and text: 903,181 to 1234,376 | the section label and title: 903,106 to 1233,162 | **none**: it shows no collection status | 230,670 to 1056,702 | the sidebar 18,112 to 237,424; the featured picture 243,90 to 880,445 |
| Adventure Map | the cover shelf with its frames and arrows: 63,545 to 1219,658 | the information panel: 354,360 to 925,542 | **none**: the title is inside the panel's frame | **none**: it shows no collection status | 180,670 to 1100,702 | the map (signs, markers, the card): 9,84 to 1257,343 |

The rules (`DesignProfile.LayoutProblems`, `HomeDesign.PlaceRegion`; the console HOME checks the same again against its package before it
uses a design: `design_layout`):

- a group the base theme has, at **the theme's size** (another size, or a group the theme does not have: kept, viewable, not applicable,
  with the reason above);
- inside the screen's **12 px safe area**;
- **no group over another**, nor over a control that never moves;
- the **game list below the theme's navigation** (the controller's Up goes there), the **button hints below the game list** (Down goes
  there) **and below the Browse panel** (drawn over the games area; the shared menus keep above the hints);
- **moving** a group needs the design's **own background** (`scene`): each theme's background has the panels of its controls painted
  where the theme puts them.

The wizard's Layout & Folders step moves each group (Left, Right, Up, Down, or an exact x, y) within these bounds and refuses a place that
breaks a rule, with the reason; nothing changes then. A region's own `focus` and `hit` members are kept and not used (the HOME's own
controller order and pointer areas apply, and move with their group). Not every renderer constant is exposed: sizes stay each theme's, and
the Game Details screen, the shared menus and Browse keep their own layout.

### Honest limits (what is not editable in this profile)

- The HOME's controls move only in the groups above, at each theme's size; Arcade Grid's details and title, Adventure Map's title and the
  status in Featured Shelf and Adventure Map have no group of their own (each dependency is named above). The navigation, the map, the
  featured picture, the Game Details screen, the shared menus and Browse do not move.
- The theme's own sprites (frames, tab bar, signs, shelf frames, the walker), its text colors and fonts (except the shared panels'),
  every size, the cover art, the Details layout, the scrolling speed, the animations and the sound are fixed by each theme.
- The other three themes are not skinned by a design's pictures (only by its colors, menus, folders and order).
- No animated pictures, no custom executable themes, no scripts, no fonts other than the HOME's own (`DesignProfile.Fonts`), no WebP.
- A label draws one line of at most 60 characters; characters the console's lettering does not have are refused by the render check.
- Text drawn by the theme directly over a picture keeps its color: a picture that makes it hard to read is refused (see the render check);
  a `plate` or `panel` regions behind that text are the design's way to keep it readable.

## The compiled design (`ccs-home-design-compiled/1`, `/2` with control regions)

`DesignCompiler.Compile(HomeDesign, IReadOnlyList<ConsoleGame>, IReadOnlyDictionary<int, string> placeholders)` returns a
`CompiledDesign`: one folder with `design.json` and `img/<sha256>.png|.jpg` (the design's own pictures, byte for byte, only those the
console draws). `design.json` holds everything the renderer needs, already resolved (the console never interprets the `.ccshome`):

```json
{
  "schema": "ccs-home-design-compiled/1", "profile": "nes-classic-home-1280x720/1",
  "source": {"id": "<32 hex>", "revision": 3, "content_sha256": "<the .ccshome manifest's SHA-256>", "name": "Neon Night"},
  "theme": "arcade-grid",
  "files": [{"name": "img/<sha256>.jpg", "sha256": "<sha256>", "bytes": 212345, "kind": "jpeg", "w": 1280, "h": 720}],
  "slots": {"scene": "img/<sha256>.jpg", "plate": "img/<sha256>.png", "icon-star": "img/<sha256>.png"},
  "appearance": {"accent": "#00a0ff", "panel": "#101828", "panel_opacity": 95, "text": "#f0f0f0", "title_font": "px11", "text_font": "sans400_20"},
  "layers": [{"kind": "panel", "x": 1000, "y": 12, "w": 268, "h": 32, "opacity": 40, "color": "#000000"},
             {"kind": "label", "x": 1000, "y": 14, "w": 260, "h": 28, "opacity": 100, "text": "Game Night", "font": "sans700_20", "color": "#ffcc00", "align": "right"}],
  "folders": [{"id": "puzzles", "name": "Puzzle Night", "parent": -1, "art": "img/<sha256>.jpg", "games": ["8e844d77da01f86a"]}],
  "order": ["8e844d77da01f86a"], "map_shortcuts": [0, -1],
  "settings": {"reduced_motion": true}, "startup": {"mode": "image", "image": "img/<sha256>.jpg"},
  "budget": {"decoded_bytes": 9123840, "peak_bytes": 14523904, "file_bytes": 612345}
}
```

- **Control regions**: a design that moves a group is written as `ccs-home-design-compiled/2` with `"regions": [{"ui", "x", "y", "w",
  "h"}]` (after `layers`, in the order games, details, title, status, hints). A design that moves nothing stays `/1`, byte for byte as
  before. A HOME that reads only `/1` sets a `/2` design aside whole (never half-applied). A HOME package of this version lists
  `"design_schemas": ["ccs-home-design-compiled/1", "ccs-home-design-compiled/2"]` in its `pack.json`; an apply is refused before
  anything is written when the console's HOME package does not list the design's schema (`DesignApplyEngine.PackageRefuses`).
- **Deterministic**: the same design and the same game list give the same bytes (one JSON writer, `Json.Write`; no time, no randomness,
  no paths). Folders are written each parent before its folders (by `pos`, then as listed); decorations by layer, then as listed.
- **Identity and hashes**: `CompiledDesign.Id` is the SHA-256 of `design.json`; `Manifest` lists every file with its SHA-256 and size
  (`design.json` first, then the pictures by name). `ManifestJson()` is what an apply plan records ("compiled package hashes").
- **Refusals**: anything `HomeDesign.RequireApplicable()` refuses (`design.not-applicable`), and a `design.json` over 2,000,000 bytes.
- **From the command line**: the Workbench program answers `--design-compile --design F.ccshome --games GAMES.json --out DIR --result
  RESULT.json [--placeholders MAP.json]` with this same code (`DesignCompileCli`; result `ccs-home-design-compile-result/1`: `ok`, `code`,
  `error` in words, `id`, `schema_name`, `theme`, `name`, `manifest`, `left_out`, `not_shown`, `unsupported`; exit 0 compiled, 1 refused,
  2 the request is wrong). Nothing else in the host starts: no window, no settings, no console.
- `CompiledDesign.WriteTo(dir)` writes it (new or empty folder) and reads it back; `CompiledDesign.Read(dir)` checks every file
  (`compiled.missing`, `compiled.changed`, `compiled.extra`, `compiled.file`, `compiled.schema`).
- **Python**: `native_home/design_ingest.py` (`read`, `place`, `python -m native_home.design_ingest verify DIR`) takes the folder as the
  compiler wrote it and puts the same bytes into a HOME package's `design/` folder (`pack.json` `"design": {"id", "dir", "theme", "files"}`;
  the package builder bakes the labels' and folder names' characters into the fonts). It never makes or changes a compiled design. The
  engine test group `designer` builds a package with it and compares every file's SHA-256 with the compiler's.

## The native HOME with a design (`design.h`, `design.c`, `home.c`)

`home_load3(pack, state_dir, design_root, design_dir, ...)`. **Read order at every start**: the console's design folder
(`design_root`): `current` -> `known-good` -> (when the console has no design of its own) the package's own `design/` -> the theme
defaults. `current` = `none` means the person chose the theme defaults (Revert Design): nothing else is tried.

A design is used **only when all of it checks out** (`design_load`): `design.json` is the one its folder names (its SHA-256), the
schema and profile, every value within the profile (slot sizes and kinds, decorations inside the 12 px safe area, known fonts, folder and
order bounds, startup picture limits), the decoding-work budget, **its control regions against the package's own layout**
(`design_layout`: the rules above), and **every picture there with its size, SHA-256, kind (from its first bytes) and header size**. Anything else and the **whole design is set aside** for the next step: a design is shown completely or not at
all, never half-applied, never a blank or crashed HOME (`design_resolved` event: source and why). A picture that changes after that check
(read again when drawn, its SHA-256 checked again) is left out where it is drawn: the theme's own background, the collage for a folder
tile, the game's own Details background (`design_picture_skipped` event).

The game player (`ccs-player-armv7`, `host_dev.c`) reads the session's `home.design_root` (`console_package.py` writes
`<data>/home-design`), shows the design's startup choice first (`design_peek_startup`: `design.json` checked, the picture's SHA-256
checked as it is read), and loads the HOME with the same folder (`gp_session.c`).

Frames stay bit-identical on Windows, Linux and ARM (integer arithmetic only; the design's pictures are composed once into the base
theme's one background frame at the stage size, then drawn like the theme's own).

### The theme's pictures over a design's background

Each theme's sprites (frames, plates, the tab bar, signs, the walker, icons, arrows) were cut as rectangles from the approved theme
designs, so around and between their own shapes they carry pixels of the picture they were cut from: dark ground, forest, grass, the edge
of a sample cover. Over the theme's own background those pixels sit where the same picture is; over a design's own background they would
show as patches of the old picture. So the HOME package carries, for each of the 33 sprites the four themes draw over the background,
the same sprite with only its own pixels (`pack.json` sprite member `file_own`, `theme/<theme>/sprite-<name>-own.png`, made at package
build by `native_home/own_sprites.py`: the old picture see-through, the sprite's own pixels unchanged, a soft edge with the coverage it
had over the old ground). The badge, seal and mark Box Art Showcase draws on its boxes are never over the background and have none.

The native HOME draws them only in the design's base theme and only while the design changes that background (its own `scene`, its
`plate` or a decoration was drawn: `home.c` `theme_activate`, `own_sprites`; `design_sprites` event); everywhere else, and in every
theme's own screens, the approved sprites are drawn exactly as before (every default screen is bit-identical to a package without them).
A package without them, or one whose own-pixel file cannot be read, gives the approved sprite (`design_picture_skipped` with `"what":
"own sprite"`). Same size as the approved sprite: no added memory. On the computer at twice the size the redrawn overlays
(`file_2x`, already clean) are drawn as before. The wizard's preview is drawn by this renderer, so it shows exactly this. Checks: `test_own_sprites.py` (every sprite drawn over the background has a rule; never more coverage
than the approved sprite, a pixel kept whole keeps its color, the ground along the edge see-through, deterministic), the engine group
`designer` (on the native HOME, in each theme, no pixel a sprite carries from the old picture reaches the screen over a design's own
background, the sprite's own pixels are drawn as approved, the default screens are the same with or without them).

## The render check (before any write)

`ccs_home_render_check` (C, `design_check.c`) loads two HOMEs from the same package, one with the theme defaults and one with the compiled
design, and in **each of the four themes** sets up and draws, through the HOME's own controls: **All Games**, the **longest title**, the
**last game**, **Game Details** and a **long title's Details**, the **START menu** and **its focus** moved, the **theme picker**,
**Browse** and a **focused tile**, a **folder**, an **empty folder**, the **navigation focus** and a **notice** (14 states in each theme,
56 screens with the design and 56 without; the same games are selected in both, by id, even when the design reorders them), plus the
**startup picture** and a **missing-picture fallback** copy of the design. Each screen is drawn twice: without text (the picture under
the text) and normally, with probes on every drawn text. It fails when:

- the HOME **sets the design aside** (it does not check out, or its control regions do not fit the package's layout);
- the HOME's text is **harder to read** with the design: the 10th percentile of the contrast ratios between the text's color and the
  picture under its own glyph pixels must reach the theme's own (at most 4.5:1) for the same text (a text in a moved group is
  compared with the same text where the theme draws it), or, for text that differs (another game in focus, another folder name), the
  theme's own weakest text on that screen (`hard_to_read` lists each text with its box). The screen dimmed under a menu, a notice or the
  theme picker is read in its own states, not through the dimmer: on those screens only the panel's own text is judged;
- a design **label** is under 4.5:1 against what is under it (3:1 at 24 px and larger);
- a **character** has no glyph in the lettering asked to draw it (it would show as `?`), where the theme itself has it;
- a shared **panel** (menu, notice, theme picker) leaves the screen's 12 px safe area, or its text leaves the panel;
- the design **with one picture missing** does not come up exactly as the theme defaults (frame SHA-256 compared);
- the **startup picture** does not decode at 1280x720;
- the design adds more than `ActiveDecodedBudget` of decoded-picture memory (measured, below).

Report (`ccs-home-render-check/1`): `ok`, `result`, `design`, `design_theme`, `pack_json_sha256`, `layout` (each theme's groups, fixed
controls and Browse panel as this package lays them out), `design_regions` (`ui`, `box`, `move`), `states[]` (`theme`, `state`, `ok`,
`default_sha256`, `design_sha256`, `texts`, `min_contrast`, `peak_bytes`, `problems`, `hard_to_read[]` with `text`, `box`,
`contrast`, `need`, and `controls[]`: `[name, x0, y0, x1, y1]` for every control the HOME draws on that screen, as the renderer records
its layout, so the editor can show where the HOME's own controls sit over a design; in the base theme of a design that moves groups,
also `default_controls`, the same screen without the design), `fallback`, `startup`, `memory` (below),
`problems`. Frames on request (RGB, `<with|without>-<theme>-<state>`).

Managed API (`DesignRenderCheck.cs`):

```csharp
IHomeRenderer r = HomeRenderers.Find(exe);              // the native host's own (standalone Workbench) or ccs-sim-core.exe (CCS for Windows: native/ccs-sim-core.exe)
RenderCheckResult res = r.Check(packDir, compiledDesignDir, new RenderCheckOptions { Frames = true, DefaultFrames = false, Cancel = token });
res.Ok; res.Problems; res.States (RenderState: Theme, State, Ok, DesignSha256, DefaultSha256, Texts, MinContrast, PeakBytes, Problems,
                                   HardToRead (text, box, contrast, need), Controls (name, box));
res.Frames (RenderFrame: Theme, State, WithDesign, Width, Height, Rgb; Png() -> an RGB PNG for the editor's preview);
res.AddedPeakBytes, res.DesignLoadPeakBytes, res.DefaultsLoadPeakBytes, res.StartupPeakBytes; res.FallbackSameAsDefaults; res.ReportSha256
```

`ccs-sim-core --render-check --pack DIR --work DIR [--design DIR] [--frames DIR] [--default-frames]` prints the same report (exit 0
passed, 1 failed, 2 could not run). Measured here for one design and the defaults in all four themes: 17.9 seconds (Linux x86-64 build,
its own file system), about 42 seconds (Windows x64 build on the build PC while other suites ran), 1 minute 58 seconds (32-bit ARM build
under qemu-arm). The same check gave the same SHA-256 for all 112 screens, the fallback screen and the startup picture on all three:
designs stay bit-identical across platforms. The memory figures are the same on the two 64-bit builds; the 32-bit ARM build counts 112
bytes less of the decoder's working memory in the Details screens (and in a design's load peak), from its pointer size. The same holds
for the four moved-layout designs of the tests (one per theme, every group moved): Windows x64 and Linux x86-64 give the same report,
32-bit ARM the same 96 frames. For a live editor preview, call it with `Frames = true` and show frames as they arrive (ABI: `on_frame`).

## Budgets (measured with the native renderer's own accounting)

The renderer counts decoded pictures (every `img_*` allocation) and the decoder's and resampler's working memory (stb_image's allocator
hooks and `scale_rect`'s scratch) with live and peak values (`design.h`: `ccs_mem_*`). The render check reports them; the C# estimate
(`HomeDesign.Budget()`: `DesignBudget.PeakDecodedBytes`, `ResidentDecodedBytes`, `DecodeWorkBytes`) is an upper bound of the same steps
(tested: estimate >= measurement).

| Budget | Value | Enforced by | Measured |
|---|---|---|---|
| `DesignProfile.ActiveDecodedBudget` (= `DESIGN_PEAK_BUDGET`): decoded-picture memory a design may add over the same HOME without it, at its worst moment | **16,000,000 bytes (16 MB)** | the render check (refuses the design) | the worst case at the file limits (a 1280x720 noise JPEG background, a 1256x696 noise RGBA PNG plate, two 1256x696 noise RGBA PNG decorations of 3.5 MB each fitted into 1255x695 and 1254x694, a 1272x712 Details background, three 544x148 PNG folder pictures, five 128x128 icons): **15,203,136 bytes (15.2 MB)** added (start: 21,464,288 bytes against 6,261,152 for the theme alone). A background-only design: **0** added (a JPEG background decodes lighter than the theme's own PNG) |
| `DesignProfile.DecodeWorkBudget` (= `DESIGN_DECODED_BUDGET`): the decoding a start does, sum of width x height x 4 over the distinct pictures (startup apart) | **29,491,200 bytes (8 frames, 29.49 MB)** | the validator (apply blocked) and the console (`design_load` sets the design aside) | not a memory figure: each picture is decoded once, drawn into the one background frame and freed; its time on the console is not measured (no console access); it bounds start-up work |
| Resident after the start (cache) | | measured, reported | the composed background replaces the theme's own (net zero); the icons (each at most the sprite's size); folder tiles are kept only for the tiles on the current Browse page (at most 16 x 272 x 72 x 3 bytes); the HOME's own cover caches are unchanged (16,777,216 + 8,388,608 bytes) |
| Staging (one picture at a time) | | measured | at most one decoded picture, its decoder working memory and its fitted copy at once, over the one background frame (the worst case above) |
| Startup picture | 1280x720 JPEG, at most 450,000 bytes | validator, console | decode peak 4,169,663 bytes (4.17 MB), shown before the HOME starts |
| Storage | the design's own bytes (at most 64,000,000) plus `DesignProfile.StorageReserve` | the apply plan (refused when the data storage's free space after the new files would be under 8,000,000 bytes) | at most three designs on the console at once during an apply (current, known-good, new); older ones are removed after the commit |
| Dimensions | every picture within its slot (at most 1280 x 720), checked from its header before any decode | validator and `design_load` | products in 64-bit; no allocation for a larger header |

One full RGBA frame is 3,686,400 bytes (3.69 MB); the console HOME's own frames are RGB (2,764,800 bytes, 2.76 MB). A file size limit is
not a memory budget.

## On the console: layout, journal, fallback, Revert Design

Under the data storage (the console's `/var/lib`), written only through `ram/ccsfiles` (scope `compose`: `put` stages the bytes in RAM,
checks them, writes beside the target, syncs, renames, syncs and reads back; every `put` and `del` names the state it must find). No new
`ccsfiles` verb was needed (the existing `stat`, `put` and `del` carry every step; the RAM images are unchanged); the free space is read
from the backup made in the same session (the data storage's own superblock):

```
ccs/data/home-design/
  d/<design id>/design.json, d/<design id>/img/<sha256>.png|.jpg   one compiled design, never changed after it is written
  current        "<design id>\n" or "none\n"   the commit: replaced in one rename
  known-good     "<design id>\n" or "none\n"   what the HOME shows when current does not check out
  journal.json   ccs-home-design-journal/1: operation, step ("commit" then "committed"), design, before/after pointers, the plan's nonce
```

**Apply** (`DesignApplyEngine.PlanApply` / `Execute`, Console Composer's strict policy, boundary `Gate.ComposerWrites`, false in this
version): the compiled design checked again file by file; a **complete backup of the console as it is now, in this session**, verified
and bound; the console's design state, its installed HOME package (`ccs/pkg/current` -> `pkg/<copy>/home/manifest.json`) and free space
read from that backup; the **render check run on the console's own HOME package** (copied out of the backup, or a copy on this PC
checked file by file against the console's `manifest.json`) and passed, its report's SHA-256 bound in the plan; the changes bound to
what is on the console now (a stat each): the design's files, `journal.json` (`commit`), `known-good` (the design shown now, when it is
complete; else unchanged), `current` (the commit), `journal.json` (`committed`), then the files of designs that are neither current nor
known-good any more. One-time confirmation (digest and nonce, 10 minutes); execute verifies the backup again under a hold, saves the
record first, writes each change once and stops at the first failure: before the commit the HOME looks as it did; after it the new
design is in place with its fallback.

**Revert Design** (`PlanRevert(con, ctx, "previous" | "defaults")`): appearance only: `current` := `known-good` (the design shown
before) or `none` (the themes as they come), with the journal. Saves, Game Settings, the HOME's other choices and every other file are
not touched (tested: a save written after the apply is unchanged).

**Fallback at every start** (no PC needed): current -> known-good -> package design -> theme defaults, deterministically (tested: with
a picture of the current design deleted, a cold start shows the known-good design, the same frame every time).

**Worker request** (`WorkerHost`, `--backup-worker`):

```
{"cmd":"home-design","action":"apply","design":"<compiled design folder>","pack":"<HOME package folder, optional>","renderer":"<ccs-sim-core.exe, optional>","console":"simulated"|"real","folder":"<simulated console>"}
{"cmd":"home-design","action":"revert","to":"previous"|"defaults","console":…,"folder":…}
  -> {"event":"home-design-plan","plan":{kind, console_tag, design, design_name, revert_to, compiled (the compiled package hashes), render_check_sha256,
       home_package_manifest_sha256, before, after, evidence, changes, new_bytes, free_bytes, notes, left_out, refusal, digest, nonce, expires_utc}}
{"cmd":"home-design-confirm","digest":…,"nonce":…}
  -> {"event":"result","operation":"home-design","state":"changed"|"stopped"|"refused"|"canceled"|"nothing-to-do","problem":…,"done":[…],"record":…,"protective":…}
```

The identical compiled design works with **CCS Mod** and **CCS Nucleus**: it lives on the data storage beside the HOME's other choices,
and both run the same game player and HOME with the same session (`home.design_root`): `console_package.py`'s `mod` and `nucleus` layouts
write the same `design_root` (`/var/lib/ccs/data/home-design`), and their console packages carry the same `home/design/` bytes (tested).
Revert Design and the known-good fallback are the same for both; saves are never part of a design.

## A collection carries a design (lossless)

`home.design` in a collection's HOME settings (`.ccs` collection.json, Full Studio Backup, Save Lists and Settings):
`{"format": "ccs-home-design-embed/1", "sha256": <the .ccshome file's>, ...what the Workbench wrote (id, revision, name, theme,
content_sha256, bytes) and anything a newer version adds, "data": <the .ccshome bytes, Base64>}`. The Studio page keeps the bytes apart in
its own storage (the startup pictures' store) and the reference in the collection, carries every member it does not know exactly
(`tv.js` `homeExtrasSave` keeps unknown HOME members; `home-model.js` keeps `design`), and puts the bytes back when a file is written
(`suInlineHome`, `suEnsure` checks them first). Python (`design_embed.py`) only checks and carries them. Neither ever edits a design:
the simpler controls change the collection's own settings beside it (tested: change the theme, save, reopen: the design and the unknown
members are byte for byte the same, and the carried bytes open as the same design). Building a console package from such a collection
needs the design compiled by `DesignCompiler` first: see the next section.

## Classic Cart Studio for Windows: a carried design compiled by the same compiler

The page sends the collection's design with its bytes (`install.js` `nhHome`: `home.design`, through `suInlineHome`; the prepared
package's fingerprint names the design's SHA-256, so a changed design means Prepare Again). The package builder (`native_home/package.py`)
compiles it **for the package's own games** before anything is placed: `design_compile.py` runs the `--design-compile` of the Workbench
program installed with Classic Cart Studio (`workbench\CCS-Console-Workbench.exe`: its path comes in `CCS_DESIGN_COMPILER`, set by
`Backend.cs`; otherwise that place beside the frozen backend), then `design_ingest.py` places the compiler's folder byte for
byte. No Python code compiles or changes a design. The build's result names the design (`design`: `id`, `schema_name`, `theme`, `name`,
`left_out`, `not_shown`). **Never left out silently**: a carried design without its bytes, a damaged one, one the console cannot show
(`design.not-applicable`, `design.inspect-only`) or a missing compiler stops the build with the reason in words and what to do
(`status: "design"`). The same design and games give the same bytes as the Workbench (tested: the app's compile, the package's
`design/` and both methods' console packages against the Workbench's compile). Placeholders ("Your game N") the person has not mapped are
left out and named; the app has no remapping screen (the Workbench's apply does).

## The preview drawn by the console HOME (the wizard)

HOME Designer is a wizard (`WorkbenchDesigner.cs`: Theme, Pictures & Startup, Text & Colors, Layout & Folders, Review & Save) with the
actual HOME beside every step (`WorkbenchDesignerPreview.cs`); there is no layout sketch. Each accepted change compiles the design with
`DesignCompiler` for a real CCS HOME package's games and draws the HOME as the console starts it: `HomeBoot.Place` writes the compiled design
into a design store of the preview's own (`d/<id>/` and `current`, as on the console) and `HomeBoot.Draw` calls `ccs_home_design_boot`
(the standalone Workbench's linked renderer, ABI slot `home_design_boot`; Classic Cart Studio's `ccs-sim-core.exe --design-boot`), whose
first screen is the preview. Only a frame from the design the HOME actually used (`source` "current") is shown; a design the HOME set aside
is said so, never shown as the theme's look. A moment later the full render check (`Check`, with `RenderCheckOptions.KeepFrame` keeping
the design theme's screens and the startup picture) draws the other screens in the background and its findings are shown in short
sentences; a newer change stops it (cancel) and it starts again. The native HOME keeps process-wide drawing state, so `HomeRenderNative`
runs one call at a time. HOME packages it draws with: by default the CCS HOME the program carries (`HomePackage.Embedded("mod")`, its
Included Games: no console or backup needed); under "Preview with", Classic Cart Studio's prepared HOME menu (the person's collection,
`WorkspaceOptions.PreparedHomePackage`) and a console's own installed CCS HOME read from one of its backups. Work files go to a folder of
the window's own under the PC's temporary folder (data only, never a program), removed when the window closes. A picture from before the
latest change is dimmed and says so; a drawing that fails says so with Retry. Nothing on a console changes.

## Host ABI proposal (standalone Workbench, branch `wb-host`)

The standalone `CCS-Console-Workbench.exe` links native code statically and passes one ABI table (`native-host/src/ccs_host_abi.h`).
Proposal, to be joined there (nothing in `wb-host` was edited):

1. **Link** `tools/console-simulator/core/{json_min,fb,gfx,font,home,persist,theme_arcade,theme_boxart,theme_shelf,theme_map,gfx3d,input,
   model,design,design_check}.c` into the host (C11, `-ffp-contract=off`, `-DCCS_BUILD_TARGET=...`; no `main.c`, `device.c`, `devio.c`).
   They use only the C runtime and, on Windows, `MultiByteToWideChar`/`_wfopen`/`CreateDirectoryW`/`MoveFileExW` (UTF-8 paths, every file
   name works). Nothing is extracted; the HOME package and the design are read from where they are.
2. **ABI version 2**: append after `par2_repair`, and bump `CCS_HOST_ABI_VERSION` to 2:

```c
#include "../../console-simulator/core/design_check.h"
typedef int32_t (*ccs_home_render_check_fn)(const ccs_home_check_request *req, char *report, uint32_t report_cap, uint32_t *report_len);
typedef int32_t (*ccs_home_design_boot_fn)(const char *pack_dir, const char *design_root, const char *state_dir, const char *frame_ppm,
                                           char *report, uint32_t report_cap, uint32_t *report_len);
typedef struct ccs_host_abi {
    /* ... version 1 members unchanged ... */
    ccs_par2_repair_fn par2_repair;
    uint32_t home_check_abi;                 /* CCS_HOME_CHECK_ABI (1) */
    uint32_t home_reserved;
    ccs_home_render_check_fn home_render_check;   /* = ccs_home_render_check */
    ccs_home_design_boot_fn home_design_boot;     /* = ccs_home_design_boot */
} ccs_host_abi;
```

   Offsets (x64 and Arm64, pointer size p = 8): version-1 size 24 + 6p = 72; `home_check_abi` at 72, `home_reserved` at 76,
   `home_render_check` at 80, `home_design_boot` at 88; version-2 size 96. `ccs_home_check_request` is 64 bytes (`size`, `flags`, six
   pointers, `cancel`): `size` is checked by the C side.
3. **Par2Native** must accept `version >= 1` and `size >= 72` (today it requires `version == 1` and `size == 72` exactly, which would
   refuse a version-2 table).
4. **Managed shim**: `CcsBackup.Compose.HomeRenderNative` (in this branch, compiled and dormant): reads `CcsNativeHost.Abi`, requires
   magic, version >= 2, size >= 96 and `home_check_abi == 1`, and calls `home_render_check` through a delegate (UTF-8 strings in
   `AllocHGlobal`, the frame callback kept alive for the call, the cancel cell in unmanaged memory set by the token). Elsewhere it reports
   NotHosted and `HomeRenderers.Find` uses `ccs-sim-core.exe`.
5. Threading: one call at a time per process (the renderer's probes are process-wide); call from a background thread; frames arrive on
   that thread through `on_frame` (return nonzero to stop).

## Tests

- Engine group `designer` (`CcsBackup.Tests --only designer`, with `CCS_HOME_RENDERER` = a `ccs-sim-core.exe` built from this branch and
  `CCS_DESIGNER_PACK` = a native HOME package; skipped without them): compiler determinism and resolution, compiled-folder tampering,
  the Python carriage and package ingest (byte identity), the render check (all states, fallback, startup, memory against the estimate,
  preview frames), a bright design refused for readability, apply on a simulated console (plan shape, commit, record), cold starts (current,
  known-good after a missing picture, chosen defaults), Revert Design keeping a newer save, refusals (failed check, another HOME package,
  a used confirmation), the closed boundary for a real console.
- Engine group `designer`, control regions: the console HOME's own layout numbers against `DesignProfile`'s in all four themes; in each
  theme a design moving every group it can passes the render check, each group's move is reported, and every control in a moved group
  is exactly where the theme draws it moved by the group (the others unchanged); a compiled layout a theme cannot show is set aside
  whole; a moved design applied on a simulated console and shown at a cold start; an earlier HOME package named before a moved layout is
  written; the app's path in Python (below).
- Engine group `designer`, the theme's pictures over a design's background: the package carries the 33 own-pixel sprites; in each theme
  a design with its own background passes the render check and no pixel a sprite carries from the theme's old picture reaches the
  screen (counted on the native HOME's frames, sprite by sprite), the sprite's own pixels are drawn as approved; the default screens are
  the same with and without the own-pixel versions, and only the design's own theme draws them.
- `tools/companion/test_own_sprites.py`: the own-pixel rules on the repository's approved theme files (above).
- Engine group `compose`: the format checks, menu lettering limits, the measured budgets, and the control regions: each theme's groups
  and the named dependency of each missing one, every theme's own layout keeping every rule, placing groups (compiled as `/2`; a design
  moving nothing stays `/1`), the refusals (a missing group, over the tab bar, over the game list, the hints at the top, beyond the
  screen, without the design's own background) and a moved layout taken to another theme (viewable, not applicable).
- `tools/companion/test_home_design_carry.py`: the `.ccs` round trip (with a startup picture), the studio page's carriage (node), the
  package builder's ingest, and the app's path: `--design-compile` to the Workbench's bytes, the refusal in words, the package and the
  CCS Mod and CCS Nucleus console packages carrying the same compiled design and startup picture.

## Open

- The decoding-work time on a real console is not measured (no console access in this version); the memory figures are the renderer's own
  accounting on this PC, the same code and the same accounting as on the console.
- Control groups keep each theme's size; resizing would need each theme's layout to reflow (the number of cover cells, text fitting,
  hint fitting). Arcade Grid's details and title, Adventure Map's title and the status in Featured Shelf and Adventure Map are not
  separate groups (their dependencies are named in "Control regions").
- The `plate` slot has no position of its own (always centered); a positioned frame is a decoration.
