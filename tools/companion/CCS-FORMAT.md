# The Classic Cart Studio package (`.ccs`)

A `.ccs` file is a complete local backup made by Classic Cart Studio: every game it lists, **with its ROM file**. There is one
package format with two kinds, used on the website and in Classic Cart Studio for Windows:

- `kind: "collection"`: one collection (**Back Up Collection**);
- `kind: "studio"`: a Full Studio Backup (**Back Up Everything**): every collection, the Game Settings and, from the Windows app, the
  saved games, settings and custom artwork (see *Full Studio Backup* below).

Both are opened with **Import Collection** (or by dropping or opening the file). A package is always complete: it contains a verified
ROM file for every game it lists.

Collections themselves may exist without ROM files (organizing, favorites, loadouts). The ROM requirement applies only to packages.
The lists-and-settings file (**Save Lists and Settings**) is not a package: it is a separate JSON file without game files.

Readers and writers: `tools/companion/ccs_package.py` (Windows app) and `tools/rom-pipeline/studio/ccs.js` (website and app page).
Both apply the rules below and produce packages the other accepts.

## Where packages are made and read

Only on the user's own device. Packages and ROM bytes are never uploaded to, relayed through, cached or processed by any service
operated by Classic Cart Studio, and Classic Cart Studio offers no way to send them to other people: it saves the file where the
person chooses (or, where a browser cannot ask, to the browser's downloads). Before a package is made, the person confirms: "I confirm
that I have the necessary rights to create this backup and understand that doing so does not grant permission to redistribute
copyrighted game files." Creating a backup does not grant permission to distribute its contents.

## Container

- A standard ZIP archive. Entries are *stored* or *deflate*; no encryption. Entry names are UTF-8 (flag bit 11).
- ZIP64 is supported and used when needed (archives or entries of 4 GB or more, more than 65,535 entries, offsets past 4 GB).
  Writers may also use ZIP64 records for smaller packages; readers accept both.
- Media type: `application/vnd.classiccartstudio.collection+zip`. The extension alone never makes a file valid.

## Entries

| Path | Required | Content |
|---|---|---|
| `ccs-manifest.json` | yes | Identifies the package and lists every other file |
| `collection.json` | yes | The collection: the schema-4 collection file with the Studio record in `ccs_collection` (every game id in order, exact versions, decisions, personal entries, favorites and folders, NES Classic Loadouts and their HOME settings) |
| `roms/<game id>.nes` or `.unh` | one per game | The game's ROM file, exact bytes (`.nes` with an iNES header, `.unh` without) |
| `art/<id>.jpg` or `.png` | no | Thumbnails of the collection's own artwork (personal games, replaced covers) |
| `README.txt` | yes | What the file is and how to open it |

Nothing else may be in the archive: no folders, links or executables.

## Manifest

```json
{
  "format": "classic-cart-studio-package",
  "schema": "ccs-package/2",
  "kind": "collection",
  "created": "2026-10-21T18:00:00Z",
  "app": {"name": "Classic Cart Studio", "version": "0.7.0"},
  "summary": {"name": "…", "games": 120, "loadouts": 2, "personal": 3, "roms": 120, "rom_bytes": 31457280},
  "files": [{"path": "collection.json", "sha256": "…", "size": 1234, "kind": "collection"},
            {"path": "roms/0123456789abcdef.unh", "sha256": "…", "size": 40960, "kind": "rom",
             "version_id": "0123456789abcdef", "title": "…", "content_sha256": "…"}],
  "roms": {"included": [{"version_id": "…", "title": "…", "path": "roms/…", "sha256": "…", "size": 0, "content_sha256": "…"}]}
}
```

- `kind` is `"collection"` or `"studio"`. A reader refuses any other `format`, `schema` or `kind` (`ccs-package/1` packages, which
  could omit ROM files, are not accepted). Readers before the Full Studio Backup refuse a `"studio"` package without reading it as a
  collection (the 0.7.7 Windows app: "This .ccs file is not a collection package, so it can't be opened."; the 0.7.7 website: "The
  package contains an unsafe or unexpected entry (studio.json).").
- `files` lists every entry except the manifest, with its SHA-256 and size. An unlisted entry, a listed entry that is missing, or
  one whose size or checksum differs makes the package invalid: nothing from it is imported.
- `roms.included` has exactly one entry per game in `ccs_collection.sel`, and every game has one. There is no "missing" list.

## Game identity (exact versions)

A ROM's **content** is the file without its 16-byte iNES header (and without a 512-byte trainer when the header declares one);
a file without a header is all content. `content_sha256` is the SHA-256 of the content.

- A Game Library version's id is the first 16 hex digits of its content SHA-256 (the library stores every version headerless).
- A personal game's id is `p_` followed by the first 16 hex digits of its content SHA-256.

A package is valid only when, for every game id, the ROM's content hash matches the id this way. A different revision or region
has a different content hash, so it can never stand in for another version. Ids without a content identity (placeholders for games
whose file is unknown) cannot be packaged; the user locates the file or removes the game from the collection.

## Size

There is no package size, ROM size or game count limit. Packages are not limited by the NES Classic's storage (that applies only to
installing on the console). Genuine limits are the device's free space and memory.

The Windows app makes a package as a background job with progress (checking ROM files, packaging *n* of *N* games, bytes written)
and a Cancel that stops at the next 1 MB step. The package is written under a temporary name, checked, and only then given its
final name; a canceled or failed export leaves no file behind.

How the Windows app stores each ROM (readers accept either method): three 256 KB samples (start, middle, end) are compressed first. A
ROM they cannot shrink by 3 % is *stored* (compressing it would take most of the export time and save nothing). Other ROMs are
compressed once, while they are written; if the finished entry is compressed beyond 200:1 (a mostly padded image the samples missed),
it is taken back and stored, so every entry stays well inside the readers' 400:1 limit. Samples that shrink more than 20:1 make the
writer measure the whole file before choosing. The website writes ROM entries stored, or copies them unchanged from another package.

## Reading rules (untrusted input)

A package is refused when any of the following is true:

- an entry name is absolute, uses a drive letter, contains a backslash, `.` or `..` segment or a control character, or is outside
  the paths above;
- the archive has duplicate entries, links or encrypted entries, or an unsupported compression method;
- an entry is compressed more than 400:1 (decompression bombs), or decompresses to more or less than it declares;
- `ccs-manifest.json` or `collection.json` is larger than 1 GB (they are parsed in memory);
- any rule in *Manifest* or *Game identity* fails.

Decompression stops at the declared size of each entry. Nothing in a package is ever run.

## Importing

- **Windows app:** the whole package is verified first. After the recipient confirms the import acknowledgment, each ROM goes to
  the app's managed game files, identified by its own content (a known version gets the library copy; anything else a personal
  game); existing files are reused, never overwritten. Each ROM is copied in 1 MB pieces while it is checked, so memory use does
  not grow with the package. Loose ROM files and ZIP archives added with Add Games
  from Files follow the same path and have no size or count limit either (unsafe or damaged archives are refused).
- **Website:** the package is verified completely in the browser (every checksum and identity). The collection is added to My
  Collections; the website does not keep ROM files, so to back the collection up again the person chooses the `.ccs` file (or the
  ROM files) when asked.

## Personal games: details and artwork

A personal game's descriptive details are its title, version label, description, year and publisher, plus its artwork
(`art/<id>.jpg` or `.png`). Writers put them in `personal_library[].user`, the selections and, for games without their file on the
writer's device, `ccs_collection.coll.missing`. Readers take each field from the first of these that has a real value:
`personal_library[].user`, `ccs_collection.coll.missing`, the selection, `personal_library[]` itself, then the ROM listing's title.
Automatic values never count as details: a game id, a ROM file name, "Personal game".

All of it is untrusted input. Control and formatting characters are removed and each field is bounded (title 120, version label
160, description 4,000, year 8, publisher 80 characters). Artwork must be a JPEG or PNG of at most 1 MB and 40 million pixels. It is
re-encoded as a JPEG thumbnail; an unusable image is ignored and never stops an import.

How the details are merged into what this device already has for the same game, field by field:

1. A field the person edited on this device is never changed.
2. Otherwise a real local value is kept: the first real value wins, so importing another package or collection with different
   details for the same game changes nothing.
3. An empty or automatic local value (for example the title taken from the file name the game was added with) is replaced by the
   package's real value.
4. Artwork is added only when the game has none.

The game's identity (content hashes, id) and the technical detection read from its file are never affected.

In the Windows app, the details of a collection package go to the personal game itself when its ROM file is imported (a Full Studio
Backup's go inside its restore instead: see below). The artwork is kept in the app's own folder (`personal/art`). When the exact ROM
file is added later, the
collection's details are merged the same way. The collection keeps its copy of the details until the app has confirmed saving
them; if saving fails, the app tries again the next time it refreshes its games or starts. Nothing depends on the imported `.ccs` file or on the app's staged copy of it (staged
copies older than 24 hours are removed the next time a package is opened, staged or exported; there is no scheduled cleanup, and the
uninstaller leaves the data folder, including any staged copies, in place).

## Full Studio Backup (`kind: "studio"`)

Everything needed to rebuild a Studio, in the same container with the same reading rules and defenses. Entries:

| Path | Required | Content |
|---|---|---|
| `ccs-manifest.json` | yes | As above, with `kind: "studio"` and a summary of collections, games, ROM files, saved games and settings |
| `studio.json` | yes | `{"schema": "ccs-studio-archive/1", "games": [every listed game id], "titles": {id: title}, "studio_data": {the Studio's lists: "collections" (ccs-collection/1 records with their loadouts and HOME settings), "active", "favorites", "view", "extended", "welcomed", "snapshots"}, "personal": {<p_ id>: personal-game record}, "app": {"game_settings": {"defaults", "games": {<content sha256>: record}}, "player_settings", "controllers", "progress": [{"sha", "title", "files", "orphaned"}], "custom"} (Windows app), "web": {"game_settings": {"defaults", "games"}} (website), "platform": "windows" or "web", "created"}` |
| `roms/<game id>.nes` or `.unh` | one per distinct game listed by any collection | As in a collection package (verified by content identity on write and on read) |
| `art/<id>.jpg` or `.png` | no | The artwork of listed personal games (in the Windows app from its own games; on the website a collection's kept cover) |
| `progress/<content sha256>/<file>` | no | Saved games (Windows app): `cart.sav`, `slot1`–`slot4.ccsstate`, `last-used.json`, `game.json`, set-aside cartridge saves `cart-set-aside-*.sav`, and an earlier test version's automatic save `continue.ccsstate` (kept for recovery only; the player never loads it by itself) |
| `custom/...` | no | Custom artwork and text (Windows app) |
| `README.txt` | yes | What the file is and how to restore it |

- Every collection's games must have their ROM files, as for a collection package. Saved games of a game in no collection are carried
  too (`"orphaned": true`); their game file is not included, and the preview says so.
- `personal` holds, for every listed personal game, the same portable record as `personal_library[]` (identity, sizes, file name without
  a folder, `user` details, `detected` header values); the key is the identity. `user.edited` lists the fields the person chose on the
  device that made the backup (`title`, `version_label`, `description`, `year`, `publisher`, `art`); an edited field that is blank is a
  deliberate clear. It is read strictly: a list of distinct known names whose edited text values are text; anything else and the edits
  are not used (the details still are, as automatic ones), and the preview and the result say so. A restore treats the details like any
  package's: cleaned and bounded, never put into the Game Library as such; on the website they become the collections' kept details
  (the edits travel with them, through reloads, Save Lists and Settings and later backups).
- Only Studio backups (a Full Studio Backup, Save Lists and Settings) carry the person's edits. A collection package or collection file
  keeps its details but never another Studio's edit list: its details merge with the rules above.
- In the Windows app a Full Studio Backup's ROM step only adds: a ROM file this computer does not have, and a new record (automatic
  details) for a new personal game. Nothing already here changes (a game's record, details, artwork and file; a managed copy that is
  missing is put back at its own path). The backup's details and artwork are applied inside the restore's own transaction (below): the
  merge rules above, and each edited field taken as the person's choice where the value here is empty or automatic, marked as an edit
  here; this computer's own edit or real value is kept unless the person chooses Replace. A game not on this computer keeps its details
  with the collections, and once its ROM file is added the collection's kept edits are merged the same way (never over an edit or a real
  value here).
- Restoring is validated first and previewed before anything changes. Collections are compared by their content (games, decisions, HOME
  settings, loadouts, their own details and the kept details of games the reader cannot resolve; ids, times, saved marks, a generated
  "(Copy n)" ending and the HOME's cursor position do not count): an equivalent one is skipped, a changed one is added as a copy, and
  nothing present is replaced. A Studio with nothing of the person's in it yet takes the backup's library view and opens its open
  collection; an existing one keeps its own unless the person chooses to replace them. Favorites of games the reader does not know are
  reported, not added; existing snapshots are never dropped.
- Saved progress is checked with the game player's own rules before anything is planned: a save state is restored only when its header
  names this exact game (the folder's content SHA-256), the same core and core version and the reader's platform, and its size and
  CRC-32 match; a cartridge save is the game's own bytes (no header); a `last-used.json` is restored only when the slot it names will
  hold exactly the state it names. Anything else (another game's state, a damaged one, one made by another player version or platform,
  a file the backup lists but does not hold) is not written: it stays in the backup file, and the preview and the result name it.
- Game Settings are read from both branches (`app.game_settings`, `web.game_settings`), each record normalized as the player reads it (a
  missing field means Use Default; Off is a choice); the backup's own platform wins where both name a game; records that cannot be used
  (no exact game, another game's record, no choices) are reported. Differing Game Settings and saved games are kept unless the person
  chooses to replace them; controller setups are restored only when chosen.
- The Windows app writes each file beside its target under the restore's own name and reads it back, keeps a journal of every change,
  copies each current file being replaced first into a write-once `replaced-<date-time>-<restore id>` folder beside it (never overwritten
  or deleted), and only then moves the files into place, each checked. A failure puts back everything the restore changed and checks
  each; if putting a file back fails, the result names the file and where its original is kept, and Try Again finishes it from the
  journal. The list of the computer's own games (`personal/index.json`) and their artwork are files of the same transaction. Saved games
  are written, by a restore and by Try Again, only when the game player says no game window is open (when that cannot be checked, they
  are not written), and the player is held meanwhile so no game window opens; Try Again for saved games waits, touching nothing, until
  the window is closed. A new restore does not start while an earlier one is unfinished (Try Again first). The page's own data
  (collections, favorites, view, earlier versions and, on the website, Game Settings) is written as one checked write and put back if a
  later part fails. ROM files added by a restore stay: they only add. A restore deletes nothing.
- The website restores collections, favorites and Game Settings for the browser; saved games stay in the file for the Windows app.
