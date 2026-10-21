# The Classic Cart Studio package (`.ccs`)

A `.ccs` file is the complete, portable form of a Classic Cart Studio collection: every game in it, **with its ROM file**. There is
one package format. It is used for **Export .ccs**, **Share Games**, **Send to Phone** and **Import .ccs Collection**, on the website
and in Classic Cart Studio for Windows. A package is always complete: it contains a verified ROM file for every game it lists.

Collections themselves may exist without ROM files (organizing, favorites, loadouts). The ROM requirement applies only to packages.
Studio data backups (every collection's settings) are not packages: they are a separate JSON file.

Readers and writers: `tools/companion/ccs_package.py` (Windows app) and `tools/rom-pipeline/studio/ccs.js` (website and app page).
Both apply the rules below and produce packages the other accepts.

## Where packages are made and read

Only on the user's own device. Packages and ROM bytes are never uploaded to, relayed through, cached or processed by any service
operated by Classic Cart Studio. A user may send a finished file with a service they choose (Messages, email, AirDrop, OneDrive,
Nearby Share and so on); Classic Cart Studio only prepares the file and hands it to the device's own sharing or saving.

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

- `kind` is always `"collection"`. A reader refuses any other `format`, `schema` or `kind` (`ccs-package/1` packages, which could
  omit ROM files, are not accepted).
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
whose file is unknown) cannot be packaged; the user removes them from the share selection or locates the file.

## Size

There is no package size, ROM size or game count limit. Packages are not limited by the NES Classic's storage (that applies only to
installing on the console). Genuine limits are the device's free space and memory and a destination app's own attachment limits;
when a destination refuses a file, the complete package is kept and can be saved or sent another way.

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
  game); existing files are reused, never overwritten. **Import Without ROMs** adds the collection only.
- **Website:** the package is verified completely in the browser (every checksum and identity). The collection is added to My
  Collections; the website does not keep ROM files, so to export or share again the person chooses the `.ccs` file (or the ROM
  files) when asked.
