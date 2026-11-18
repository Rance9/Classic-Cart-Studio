# Console Composer and HOME Designer file formats

Three file roles, three formats. Reader, writer and checks: `CcsBackup/Compose` (namespace `CcsBackup.Compose`), one library for both
Windows hosts (the standalone CCS Console Workbench and Classic Cart Studio for Windows). Tests: `CcsBackup.Tests --only compose`.

| Role (what people see) | File | What it holds | What it never holds | Shared? |
|---|---|---|---|---|
| **Shared HOME Design** | `.ccshome` (ZIP, `ccs-home-design/1`) | The design's own still pictures, layout, appearance and settings, a folder and game-order template, credits | Games, saves, console system files, console identities or keys, private file locations, notes, private game names or fingerprints | Yes, after its share preview |
| **Shared Composition Plan** | `.ccsplan` (JSON, `ccs-composition/1`) | Choices and public references: starting point, install policy, environments, public catalog games, public mod builds, CCS parts by version, a HOME design by id and fingerprint, safe public build fingerprints | Any file or payload, console identity or anything derived from it, keys, saves or their fingerprints, console-specific software fingerprints, private names, notes, locations, screenshots, attachments, links | Yes, after its share preview |
| **Private Restore Package** | `.zip` named `PRIVATE NES Classic Restore Package …` (`ccs-private-restore/1`) | The owner's own files for one destination console, private source bindings, the exact target plan, every member's fingerprint and provenance | (It is private by nature) | **Never.** Every share path refuses it |

A **Console Backup** (`ccs-console-backup/1`, `FORMAT.md`) and a **collection package** (`.ccs`, `tools/companion/CCS-FORMAT.md`) keep
their own roles: a console backup is private; a `.ccs` carries game files and is not shared from Console Composer. A file's role is
decided by its content, never by its name or extension (`ShareGuard.Classify`): a private package renamed `.ccshome` is still refused.

## Rules shared by every format

**Untrusted input.** A received file is data. Nothing in it is run, fetched, opened or interpreted: no scripts, commands, links, local or
network paths, executable fields, fonts or plug-ins exist in any schema, and text is shown escaped (`Display.Html`, `Display.Line`).

**Allowlisted schemas.** Every object has a fixed list of members. Any other member is refused (`design.key`, `composition.key`), not
skipped; this is an allowlist, not a list of forbidden fields.

**Versions.** `schema` names the major version (`…/1`). `minor` (a whole number, default 0) and `requires` (capability names) say more:

| Situation | Result |
|---|---|
| Major 1, every `requires` entry known | Opened fully: everything below is checked; it can be edited, saved and applied |
| A newer minor that added optional data | That data lives in `optional` objects (below): kept, never interpreted |
| An unknown value in a known field (a newer theme, slot, region kind, font, starting point, policy, environment kind or input role) | Viewable; **apply is blocked** and the reason is listed; the value is never replaced or dropped |
| A newer major, or a `requires` entry this version does not know | **Inspect only**: the envelope and the inert-data rules are checked; the original file can be exported unchanged; apply, edit and re-save are refused; nothing is silently downgraded |

**Optional data** (`optional`, allowed on every object of a design or a plan): member names `a-z` then `a-z 0-9 _ -` (at most 48
characters); values null, true/false, whole or decimal numbers, plain text (at most 500 characters), arrays and objects (at most 64
items or members each, at most 6 levels deep); at most 64,000 bytes per `optional` object as written. A fingerprint (64 hex digits) is
allowed in a design's optional data only when it is one of the design's own pictures; never in a plan's. Opening, changing something the
program understands, saving and opening again keeps every optional member exactly (and, in a design, every picture it names).

**Plain text** (`PlainText.Check`): Unicode normal form C; no control, invisible formatting (including right-to-left overrides),
unassigned or private-use characters; and nothing that is a link (`://`, `www.`, `javascript:`, `data:`, `file:` …), a file location
(drive paths, `\`, leading `/` or `~`, `..\`, `%VAR%`), markup (`<tag`), a console identity (four groups of eight hex digits) or a
fingerprint (16 or more hex digits in a row). Each has its own refusal code (`*.url`, `*.path`, `*.markup`, `*.console-id`,
`*.fingerprint`, `*.control`, `*.encoding`, `*.length`).

**JSON** (`StrictJson`): RFC 8259 only, strict UTF-8 without a byte order mark, no member named twice in one object (also differing only
in case), bounded bytes, depth, values and text length. Member order and number scale are kept.

**ZIP** (`SafeZip`, the `.ccshome` and the private package), decided from the index and local headers before anything is decompressed or
decoded: archive bytes, member count, each member's declared size, the total, and for compressed members the ratio (at most 100:1) are
bounded; names must be UTF-8 when not ASCII, with no control or formatting characters, no absolute (`/x`, `\x`), drive (`C:`), network
(`//server`, `\\server`) or device (`CON`, `NUL`, `COM1`, `LPT1` …) names, no `:` (alternate data streams), `\`, `.`, `..`, empty
parts, trailing dots or spaces, no folder entries, no second name in an extra field; two names equal exactly, ignoring case, or after
Unicode compatibility normalization are refused; no encryption, data descriptors, comments, links or reparse points; stored or deflate
only; members tile the file from byte 0 to the index (nothing before, between or after); every local header agrees with the index; each
member read stops at its declared size and checks its CRC-32. Codes: `zip.ratio`, `zip.size`, `zip.total`, `zip.count`, `zip.traversal`,
`zip.absolute`, `zip.unc`, `zip.drive`, `zip.device`, `zip.stream`, `zip.backslash`, `zip.path`, `zip.name`, `zip.name-encoding`,
`zip.name-extra`, `zip.folder`, `zip.duplicate`, `zip.collision`, `zip.link`, `zip.encrypted`, `zip.descriptor`, `zip.method`,
`zip.trailing`, `zip.gap`, `zip.overlap`, `zip.mismatch`, `zip.unlisted`, `zip.damaged`, `zip.not-zip`.

## Shared HOME Design (`.ccshome`, `ccs-home-design/1`)

A standard ZIP: `README.txt` (deflate; plain text; rewritten on every save), `images/<sha256>.png` or `images/<sha256>.jpg` (stored;
named by the SHA-256 of their bytes), and `ccs-home-design.json` (deflate; last). Nothing else, in apply mode. Written with a fixed ZIP
time, so the same design gives the same bytes on either host. The design's identity for a plan is the SHA-256 of its manifest as written
(`HomeDesign.ContentSha256`); the pictures are inside it by fingerprint.

### Limits

| | |
|---|---|
| File | at most 64,000,000 bytes; at most 50 members; manifest at most 1,000,000 bytes; README at most 16,000 bytes |
| Pictures | at most 48; at most 4,000,000 bytes each (less where the slot says); stored, not compressed |
| Layout | at most 40 regions, of them at most 32 decorations; decoration layers 0-7 (the HOME's own controls are always drawn above layer 7); focus positions 1-16, unique |
| Folders | at most 200, nested at most 5 deep, no cycles, every parent present; at most 2,000 games each |
| Games | order at most 5,000 (no game twice); at most 20,000 game references in all |
| Text | design name and author 60, folder name 40, label 60, credit `by` 80, `title` 120, `license` 80, catalog title 120 characters |
| Readability | label fonts at least 13 px; a label box at least 24 px wide and font size + 4 px high; text color against panel color at least 4.5:1; the accent color against white at least 4.5:1 (the HOME draws white lettering on it; lighter: viewable, not applicable); menu lettering: `title_font` at most 16 px, `text_font` 13 to 22 px (else viewable, not applicable). The render check then measures every drawn text on the console HOME itself (`HOME-DESIGNER.md`) |
| Screen | every region inside 12 px of every edge: `x >= 12`, `y >= 12`, `x + w <= 1268`, `y + h <= 708` (the HOME edge rule) |

### Members of `ccs-home-design.json`

| Member | Required | Value |
|---|---|---|
| `format` | yes | `"classic-cart-studio-home-design"` |
| `schema` | yes | `"ccs-home-design/1"` |
| `minor` | | 0-999 |
| `requires` | | at most 16 capability names; known: `home-design/1` |
| `profile` | | `"nes-classic-home-1280x720/1"` (another profile: apply blocked) |
| `design` | yes | `id` (32 lowercase hex, random), `revision` (1-1,000,000), `name`, `base_theme` (`arcade-grid`, `box-art-showcase`, `featured-shelf`, `adventure-map`), `author` (optional public creator name) |
| `images` | yes | one entry per picture: `sha256`, `file` (`images/<sha256>.png` or `.jpg`), `bytes`, `format` (`png`/`jpeg`), `width`, `height`, `alpha`; every value must match the picture itself |
| `credits` | | `by` (required), `title`, `license`, `image` (one of the design's pictures, or null) |
| `slots` | | `{ "<slot>": { "image": "<sha256>" } }` for the slots below |
| `appearance` | | `text_color`, `accent_color`, `panel_color` (`#rrggbb`), `panel_opacity` (0-100), `title_font`, `text_font` (existing fonts only) |
| `layout` | | `regions`: `id` (`a-z`, `0-9`, `-`; unique), `kind`, `x`, `y`, `w`, `h` (whole pixels), and by kind: `ui` (`ui`, `focus`, `hit {x,y,w,h}` inside the region; no two overlap), `image` (`image`, `layer`, `opacity`), `label` (`text`, `font`, `color`, `align` left/center/right, `layer`, `opacity`), `panel` (`color`, `layer`, `opacity`) |
| `folders` | | `id` (`a-z`, `0-9`, `-`), `name`, `parent` (a folder id or null), `pos` (0-9,999), `art` (a picture or null), `games` (game references) |
| `order` | | game references: the All Games order template |
| `map_shortcuts` | | exactly two entries: a folder id or null |
| `settings` | | `reduced_motion`, `hints`, `music` (true/false) |
| `startup` | | `mode`: `random`, `builtin` (with `builtin`: `forgotten-keep`, `galactic-frontier`, `kingdoms-beyond`, `neon-highway`, `pixel-worlds`) or `image` (with `image`) |
| `optional` | | inert data (above) |

A **game reference** is `{"catalog": "<16 hex: a public catalog version id>", "title": "<its public title>"}` or `{"placeholder": N}`
(1-99,999). A private game id (`p_…`) is refused (`design.private-game`). When a design is shared (`HomeDesign.PrepareShare`), every
game the public catalog does not list becomes a numbered placeholder ("Your game 2"), without its name or fingerprint. When a design is
received, `MatchGames` lists the games this computer has, lacks, and the placeholders, for an explicit remap or skip: nothing is added,
downloaded or removed by it, and no collection entry without a game file is ever created from it.

### Picture slots (`DesignProfile.Slots`; the console profile `nes-classic-home-1280x720/1`)

| Slot | Size | Kinds | See-through | Bytes at most |
|---|---|---|---|---|
| `scene` (HOME background) | exactly 1280x720 | PNG, JPEG | no | 4,000,000 |
| `details-backdrop` (game details background) | exactly 1272x712 | PNG, JPEG | no | 4,000,000 |
| `plate` (frame or plate over the background) | at most 1256x696 | PNG | yes | 4,000,000 |
| `icon-games`, `icon-folder`, `icon-star`, `icon-clock`, `icon-gear` | at most 128x128 | PNG | yes | 200,000 |
| `startup` (from `startup.image`) | exactly 1280x720 | JPEG | no | 450,000 |
| folder picture (from `folders[].art`) | at most 544x148 | PNG, JPEG | no | 1,000,000 |
| decoration (from an `image` region) | at most 1256x696 | PNG, JPEG | yes | 4,000,000 |

**HOME regions** (`ui` regions, smallest size any of them can have): `games` 400x100, `details` 320x100, `title` 160x28, `status` 96x28,
`hints` 240x24. In the console renderer profile `nes-classic-home-1280x720/1` they move the HOME's own controls in their base theme, each
group at **its theme's size**, under the layout rules (`DesignProfile.LayoutProblems`; `HOME-DESIGNER.md` "Control regions": each theme's
groups, the ones it does not have and why). A region another theme's size, a group the base theme does not have, or a place that breaks a
rule is kept, viewable and **not applicable**, with the reason. Which slots each theme draws: `DesignProfile.ThemeDraws`, `SlotsOf`
(`HOME-DESIGNER.md`).

### Pictures

**In a design** (received or written), pictures are only the canonical console profile, checked from structure before any pixel is
decoded (`ImageRules.Inspect`): kind from the first bytes (an SVG, HTML, a program, an archive or GIF/WebP/BMP/TIFF/HEIF named `.png`
is refused and named for what it is), dimensions from the header against the slot and the stage (a header claiming 20,000 pixels is
refused without allocating anything), then:

- **PNG**: 8 bits per channel, RGB or RGBA, not interlaced; chunks `IHDR`, `IDAT` (consecutive), `IEND` only; every chunk checksum
  correct; nothing after `IEND`; the pixel data inflates to exactly `height x (1 + width x channels)` bytes, with a filter type 0-4 on
  every row and a matching Adler-32. No `acTL` (animation), no text, EXIF, color profile or other metadata chunks.
- **JPEG**: baseline (`SOF0`), 8-bit, 1 or 3 components, one scan; an optional JFIF `APP0`; no other `APPn` (EXIF, XMP, ICC), no
  comment; nothing after the end marker (a file with an archive glued after it is refused).

Received pictures are never converted: their fingerprints stay theirs. A non-canonical picture is refused (`design.image-profile`).

**Adding a person's own picture** (`ImageNormalizer.Normalize`, on the PC): accepted are still PNG (any standard color type and bit depth,
interlaced or not; extra chunks are dropped) and JPEG (baseline, extended or progressive; 8-bit; gray or color; metadata is dropped), at
most 25,000,000 bytes, 12,000 pixels a side and 50,000,000 pixels. Not accepted: animated PNG, CMYK or 12-bit, lossless, hierarchical or
arithmetic JPEG, GIF, WebP, BMP, TIFF, HEIF, SVG and anything else. The picture is decoded once on the PC, turned upright by its EXIF
orientation, fitted to the slot (exact-size slots: `cover` fills it with a centered crop, `contain` shows all of it on black or, where
see-through is allowed, on nothing; other slots are only made smaller, never larger), and written again, which drops all metadata: an
RGBA PNG when some pixel is see-through and the slot allows it, otherwise a baseline JPEG (quality 90, lowered in steps to 50 to fit the
slot's byte limit), or an RGB PNG for PNG-only slots. The PNG writer is the engine's own (the same pixels give the same bytes); the JPEG
encoder is Windows' own.

**Decoded memory** (`HomeDesign.Budget`, measured with the native renderer: `HOME-DESIGNER.md` "Budgets"). One full 1280x720 RGBA frame is
3,686,400 bytes (3.69 MB). `DecodeWorkBytes`: the distinct pictures the HOME uses (the startup picture apart), width x height x 4 each,
added up and compared with `DesignProfile.DecodeWorkBudget`, **8 frames, 29,491,200 bytes (29.49 MB)**: the decoding one start does
(each picture is decoded once and drawn into the one background frame); over it: viewable, apply blocked, and the console sets the design
aside. `PeakDecodedBytes`: the editor's upper-bound estimate of the most memory the design adds at once; the render check measures the
real peak on the native HOME and refuses a design over `DesignProfile.ActiveDecodedBudget`, **16,000,000 bytes (16 MB)** (the worst case
at the file limits measured 15,203,136 bytes). A file size limit is not a memory budget.

### Example (synthetic placeholders)

```json
{
  "format": "classic-cart-studio-home-design",
  "schema": "ccs-home-design/1",
  "minor": 0,
  "requires": [],
  "profile": "nes-classic-home-1280x720/1",
  "design": {"id": "0f1e2d3c4b5a69788796a5b4c3d2e1f0", "revision": 1, "name": "Neon Night", "base_theme": "arcade-grid"},
  "credits": [{"by": "Rance9", "title": "Neon Night background", "license": "CC BY 4.0", "image": "1111111111111111111111111111111111111111111111111111111111111111"}],
  "images": [
    {"sha256": "1111111111111111111111111111111111111111111111111111111111111111", "file": "images/1111111111111111111111111111111111111111111111111111111111111111.jpg",
     "bytes": 212345, "format": "jpeg", "width": 1280, "height": 720, "alpha": false}
  ],
  "slots": {"scene": {"image": "1111111111111111111111111111111111111111111111111111111111111111"}},
  "appearance": {"text_color": "#ffffff", "accent_color": "#e60012", "panel_color": "#101018", "panel_opacity": 80, "title_font": "sans700_20", "text_font": "sans400_16"},
  "layout": {"regions": [
    {"id": "games", "kind": "ui", "ui": "games", "x": 40, "y": 120, "w": 900, "h": 480, "focus": 1},
    {"id": "hello", "kind": "label", "x": 40, "y": 40, "w": 400, "h": 40, "text": "Game Night", "font": "sans700_20", "color": "#ffcc00", "align": "left", "layer": 3}
  ]},
  "folders": [{"id": "puzzles", "name": "Puzzle Night", "parent": null, "pos": 0, "games": [{"catalog": "0123456789abcdef", "title": "Example Quest"}, {"placeholder": 1}]}],
  "order": [{"catalog": "0123456789abcdef", "title": "Example Quest"}, {"placeholder": 1}],
  "map_shortcuts": ["puzzles", null],
  "settings": {"reduced_motion": false, "hints": true, "music": true},
  "startup": {"mode": "random"},
  "optional": {"glow": {"strength": 3}}
}
```

## Shared Composition Plan (`.ccsplan`, `ccs-composition/1`)

One UTF-8 JSON file, at most 1,000,000 bytes (depth 16, text at most 4,000 characters). A **plan only**: it cannot authorize a write and
is never a console restore point (`Composition.CanAuthorizeWrites` and `IsRestorePoint` are false; no member for a confirmation, an
admission or a restore point exists). Built for sharing only through `CompositionShare.Prepare` (below).

| Member | Required | Value |
|---|---|---|
| `format` | yes | `"classic-cart-studio-composition"` |
| `schema` | yes | `"ccs-composition/1"` |
| `kind` | yes | `"shared-plan"` (anything else is refused: a private plan or package never takes this path) |
| `minor`, `requires` | | as above; known capability: `composition/1` |
| `plan` | yes | `id` (32 lowercase hex, random), `revision` (1-1,000,000), `title` (plain text, at most 80) |
| `platform` | yes | `nes-classic` |
| `starting_point` | yes | `like-my-other-console`, `rebuild-from-backups`, `fresh-ccs` |
| `install_policy` | yes | `ccs-mod`, `ccs-nucleus` |
| `environments` | yes | 1-8: `id` (`a-z`, `0-9`, `-`; at most 16), `kind` (`nintendo-original`, `ccs-home`, `hakchi-ce`), `default` (at most one) |
| `games` | | at most 5,000: `ref` (a game reference as in a design), `environment`, `content_sha256` (only for a catalog game: the public fingerprint of that version), `save` (`bring-own`: the receiver's own save is used; `none`) |
| `mods` | | at most 500: a public build `name` (`a-z 0-9 . _ -`), `version`, `sha256`, `environment`; or `{"placeholder": N, "environment": …}` |
| `home_design` | | `id`, `revision`, `sha256` (the design's `ContentSha256`), `name`: names the `.ccshome`, never carries it |
| `inputs` | | at most 32: `role` `console-system` or `console-saves` (`supply: "recipient"`, and no fingerprint, name or version: the receiver's own material), `ccs-component` (`supply: "plan"`, `ref` `ccs-mod`/`ccs-nucleus`, `version`, optional public `sha256`), `hakchi-kernel` (`supply: "plan"`, `version`, optional public `sha256`); optional `environment` |
| `optional` | | inert data, with no fingerprints |

No member can hold a link, a path, a file or file contents. A fingerprint of console-specific software, a save fingerprint, or a plan
claiming to supply someone's console software or saves is refused (`composition.hash-place`, `composition.key`). Other codes:
`composition.format`, `composition.type`, `composition.bounds`, `composition.duplicate`, `composition.private-game`, the plain-text codes.

**Sharing** (`CompositionShare.Prepare(draft, registry, privateValues)`): the plan is built from the Composer's private draft by copying
only what the allowlist and the public registry (`IPublicRegistry`: catalog games with their public titles and fingerprints, public builds)
permit. A catalog game keeps its public title (never the person's own title for it) and, when the registry confirms it, its public
fingerprint; any other game becomes a placeholder. A mod or a build the registry does not confirm loses its fingerprint (a mod becomes a
placeholder). The preview lists what is included and every class of what was removed: notes, the console's identity, screenshots,
attachments, the private source list, the person's own games, save fingerprints, console-software fingerprints, backup references, file
locations, private mods, unconfirmed build fingerprints. A plan name that holds a private detail (the person's names, a console tag, a
path, a link) is refused until it is changed, never silently edited. Finally the written text is searched for every private value known
(the given ones and every private part of the draft); a hit refuses the share. `CompositionShare.Write` writes only a preview that passed,
and only after `ShareGuard` accepts the written file.

**Receiving** (`Composition.Checklist`): item by item, what the receiver supplies (their console's original software, their saves, games
for placeholders), what is ready, missing, mismatched (a different version than the catalog lists) or an unknown build, and the design
file to open. Nothing is fetched, downloaded or changed by it.

### Example (synthetic placeholders)

```json
{
  "format": "classic-cart-studio-composition",
  "schema": "ccs-composition/1",
  "minor": 0,
  "kind": "shared-plan",
  "requires": [],
  "plan": {"id": "a0b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5", "revision": 1, "title": "Living room setup"},
  "platform": "nes-classic",
  "starting_point": "like-my-other-console",
  "install_policy": "ccs-mod",
  "environments": [{"id": "e1", "kind": "nintendo-original"}, {"id": "e2", "kind": "ccs-home", "default": true}],
  "games": [
    {"ref": {"catalog": "0123456789abcdef", "title": "Example Quest"}, "environment": "e2", "content_sha256": "2222222222222222222222222222222222222222222222222222222222222222", "save": "bring-own"},
    {"ref": {"placeholder": 1}, "environment": "e2"}
  ],
  "mods": [{"name": "example-mod", "version": "1.0", "sha256": "3333333333333333333333333333333333333333333333333333333333333333", "environment": "e2"}],
  "home_design": {"id": "0f1e2d3c4b5a69788796a5b4c3d2e1f0", "revision": 1, "sha256": "4444444444444444444444444444444444444444444444444444444444444444", "name": "Neon Night"},
  "inputs": [
    {"role": "console-system", "supply": "recipient", "environment": "e1"},
    {"role": "console-saves", "supply": "recipient"},
    {"role": "ccs-component", "supply": "plan", "ref": "ccs-mod", "version": "1.0.0"}
  ]
}
```

## Private Restore Package (`ccs-private-restore/1`)

A standard ZIP built by `PrivatePackage.Write` for one destination console: `README-PRIVATE.txt` (deflate; starts with PRIVATE),
`payload/<dir>/<sha256><ext>` (stored, one per distinct file), `ccs-private-restore.json` (deflate; last). At most 4,000,000,000 bytes and
4,096 members; manifest at most 16,000,000 bytes. Every source is checked for its role and hashed, written once (a source that changes
meanwhile stops the build), and the finished file is opened and verified in full before it is moved into place. Nothing is overwritten;
it is never saved as `.ccshome` or `.ccsplan`. Source backups and files are only read.

Manifest: `format` `"classic-cart-studio-private-restore"`, `schema`, `private: true`, `label` (starts with `PRIVATE`), `package_id`,
`created_utc`, `tool`, `destination` (`hwid` and its `tag`, which must agree), `composition` (`id`, `revision`, `sha256` of the shared
plan it was built from, if any), `target_plan` (`install_policy`, `starting_point`, `environments` [`id`, `kind`], `components`
[`role`, `sha256`, `place`]) and `members` [`name`, `role`, `size`, `sha256`, `provenance`]. `provenance.kind` is `console-backup`
(with `backup_id`, `backup_member`, `console_tag`), `user-file`, `public-build` or `ccs-built`. Every component names a member of its
role; every member is used by a component; every payload member is listed (`private.unexplained`, `private.unlisted`, `private.missing`).

### Content roles (`PrivateRoles.Check`: decided by content, never by name or extension)

| Role | Member | At most | Content check | Places |
|---|---|---|---|---|
| `root-squashfs` | `payload/system/<sha>.hsqs` | 536,870,912 B | `hsqs` superblock, version 4.0, block size 4,096 to 1,048,576 bytes matching its log, known compression, files and ids present, `bytes_used` within the file, table positions in order | `environment:<id>/root` |
| `encrypted-partition` | `payload/partitions/<sha>.img` | 536,870,912 B | partition-sized (at least 1,048,576 bytes, a multiple of 4,096), neither SquashFS nor ext4, first 65,536 bytes ciphertext-like (byte frequencies near uniform). Plain dm-crypt has no header: its content cannot be checked without the console's key | `partition:system` |
| `filesystem-image` | `payload/filesystems/<sha>.img` | 536,870,912 B | ext2/3/4 superblock and a root folder (`Ext4Reader`) | `partition:data`, `partition:private`, `partition:udisk` |
| `kernel` | `payload/kernels/<sha>.img` | 33,554,432 B | `ANDROID!` boot image header, a kernel, a valid page size, the header's declared size within the file | `kernel:active`, `kernel:spare`, `environment:<id>/kernel` |
| `boot0` | `payload/boot/<sha>.boot0` | 32,768 B | `eGON.BT0` header, length and checksum (`BootImage.Check`) | `reference`, `same-console-repair` |
| `boot1` | `payload/boot/<sha>.boot1` | 15,728,640 B | `uboot` header, length, checksum, optional nandinfo block | `reference`, `same-console-repair` |
| `game-nes` | `payload/games/<sha>.nes` | 64,000,000 B | `NES<1A>` header (iNES or NES 2.0), program data present, the exact size the header declares (PlayChoice data allowed) | `environment:<id>/games` |
| `save-battery` | `payload/saves/<sha>.sav` | 131,072 B | a power of two from 1,024 to 131,072 bytes, and not a game, an archive, compressed data or a save state (`FCSX`) | `environment:<id>/saves` |
| `mod-overlay` | `payload/mods/<sha>.hmod` | 512,000,000 B | tar or tar.gz, first entry `ustar` with a correct header checksum | `environment:<id>/mods` |
| `fs-metadata` | `payload/metadata/<sha>.json` | 16,000,000 B | `ccs-fs-metadata/1`: entries with a safe relative `path`, `type` f/d/l, `mode`, `uid`, `gid`, `size`, optional `sha256`, `target` for links | `environment:<id>/metadata` |
| `ccs-collection` | `payload/collections/<sha>.ccs` | 2,000,000,000 B | a Classic Cart Studio collection package (`ccs-package/2`, kind collection or studio; the nested archive passes `SafeZip`). Only genuine collection or Studio data is nested | `environment:<id>/collection` |

Boot stages are evidence (`reference`) or the destination console's own repair material (`same-console-repair`, routed through Rescue),
which must come from a backup of that same console (`provenance.console_tag` = the destination's tag); anything else is refused
(`private.boot-transplant`, `private.place`). An ordinary composed restore never writes boot0, boot1, the storage table or the console's
identity. A private package is not a console backup and never replaces one.

## The share gate (`ShareGuard`)

Every share or send action calls `ShareGuard.RequireShareable(path)`. It classifies by content and accepts only a Shared Composition Plan
or a Shared HOME Design that passes its full validation again. Refused: a private restore package or its manifest alone, also renamed
(`share.private`); a console backup (`share.private`); a collection package (`share.collection`); anything else (`share.unknown`). A
design with any other member (a package tucked inside) is refused as `zip.unlisted`. Nothing here uploads or sends anything.

## Open decisions

- Settled with the console renderer (`HOME-DESIGNER.md`): the budgets (`ActiveDecodedBudget` 16 MB measured, `DecodeWorkBudget` 8
  frames), the slot list and sizes (exercised in the four themes; each theme's drawn slots published), `ui` regions moving each theme's
  control groups at the theme's size (`HOME-DESIGNER.md` "Control regions"). A new design starts from the console HOME's own menu look (text `#e9e7e1`, accent `#e4000f`, panel
  `#111115` at 100, `px11` titles, `sans400_20` text).
- File extensions: `.ccsplan` for the shared plan and `.zip` with a `PRIVATE …` name for the private package are proposals.
- WebP is not accepted on import (Windows has no built-in decoder on every supported version).
- Links are not allowed in credits (attribution by name and license text).
- The renderer bakes fonts per character set: the Python package builder adds a compiled design's label text and folder names to the bake
  set; a design applied to an installed HOME can only use characters its fonts have (the render check names any that are missing; a face
  with only capitals draws labels in capitals).
- The stock NES Classic save container is not a role here (raw battery saves only); conversion is a separate module.
- The encrypted-partition check assumes plain dm-crypt with no header (as the read-only audit shows the console opening it); confirm
  against a real capture before relying on it.
- A `.ccs` collection carries the same design file losslessly as `home.design` (`ccs-home-design-embed/1`: the `.ccshome` bytes as Base64
  with their SHA-256 and the Workbench's description; `tools/companion/design_embed.py`, `studio/startup-custom.js`; `HOME-DESIGNER.md`).
