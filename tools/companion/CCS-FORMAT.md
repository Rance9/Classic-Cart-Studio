# The Classic Cart Studio collection package (`.ccs`)

A `.ccs` file is the portable form of a Classic Cart Studio collection. It is used for **Export Collection**, **Import .ccs Collection**, **Back Up Studio Data** and for passing a collection on yourself. The website and Classic Cart Studio for Windows read and write the same format.

Readers: `tools/companion/ccs_package.py` (Windows app) and `tools/rom-pipeline/studio/ccs.js` (website and app page). Both apply the rules below.

## Container

- A standard ZIP archive. Entries are *stored* or *deflate*; no encryption and no ZIP64. Entry names are UTF-8.
- Media type: `application/vnd.classiccartstudio.collection+zip`. The extension alone never makes a file valid: the archive and its manifest are checked.

## Entries

| Path | Required | Content |
|---|---|---|
| `ccs-manifest.json` | yes | Identifies the package and lists every other file |
| `collection.json` | for `kind: "collection"` | The collection: the schema-4 collection file with the Studio record in `ccs_collection` (every game id in order, exact versions, decisions, personal entries, entries not available on the sender's computer, NES Classic Loadouts, HOME settings) |
| `studio-backup.json` | for `kind: "backup"` | A Studio data backup: every collection with its loadouts, library favorites, settings and recovery snapshots |
| `art/<id>.jpg` or `.png` | no | Thumbnails of the collection's own artwork (personal games, replaced covers); at most 1 MB each |
| `roms/<version id>.nes` or `.unh` | no | Game files. They are present only when the sender chose **Export with ROMs** and confirmed the acknowledgment; at most 16 MB each |
| `README.txt` | yes | What the file is and how to open it |

Nothing else may be in the archive: no folders, links or executables.

## Manifest

```json
{
  "format": "classic-cart-studio-package",
  "schema": "ccs-package/1",
  "kind": "collection",
  "created": "2026-10-20T18:00:00Z",
  "app": {"name": "Classic Cart Studio", "version": "0.7.0"},
  "summary": {"name": "…", "games": 120, "loadouts": 2, "personal": 3, "roms": 40},
  "files": [{"path": "collection.json", "sha256": "…", "size": 1234, "kind": "collection"},
            {"path": "roms/0123456789abcdef.nes", "sha256": "…", "size": 40976, "kind": "rom", "version_id": "0123456789abcdef", "title": "…"}],
  "roms": {"included": [{"version_id": "…", "title": "…", "path": "roms/…", "sha256": "…", "size": 0}],
           "missing":  [{"version_id": "…", "title": "…", "reason": "the game file is not on this computer"}]}
}
```

- `kind` is `"collection"` or `"backup"`. A reader refuses any other `format` or `schema` value, and refuses a schema from a newer release.
- `files` lists every entry except the manifest, with its SHA-256 and size. A package with an unlisted entry is refused. A listed file that is missing, has a different size or has a different checksum is reported, and that file is not used.
- `roms.missing` lists games whose files the sender did not have. Those games remain in the collection.

## Reading rules (untrusted input)

A package is refused when any of the following is true:

- an entry name is absolute, uses a drive letter, contains a backslash, `.` or `..` segment or a control character, or is outside the paths above;
- the archive has duplicate entries, links or encrypted entries;
- an entry declares more than its limit, or is compressed more than 400:1;
- the whole package is larger than 4 GB, or has more than 60,000 entries.

Decompression stops at the declared size of each entry.

Nothing in a package is ever run.

The website reads only the manifest, the collection and the artwork. It never reads, keeps or uploads the game files in a package.

## Game files

Game files are imported only in Classic Cart Studio for Windows, and only after the recipient confirms the import acknowledgment.

The **whole** package is verified first: every checksum must match before any file is used. Each game file then goes to the app's managed game files, where it is identified by its own content hash:

- a known version gets the library copy;
- anything else becomes a personal game.

A different revision is never substituted for the one the package names, and existing files are reused, never overwritten.

**Import Without ROMs** imports the same collection and loadouts without touching any game file.

## Public share links

A share link (`share.classiccartstudio.com/c/<id>`) holds the same collection and loadouts as metadata only. The sharing service accepts no game files. It also refuses images that carry an iNES or FDS game header.
