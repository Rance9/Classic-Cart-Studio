# CCS Collection Interface kit (v1)

Classic Cart Studio installs a game collection on an NES Classic that runs Hakchi. It adds the collection's games to the console's menu as
ordinary Hakchi game entries (a game folder with `CODE.desktop`, `CODE.png` and `CODE_small.png`), grouped in Hakchi folders. A menu that
already reads Hakchi's game folders sees those games without doing anything else.

Hakchi's folders cannot say everything a collection holds: a game appears in one folder only, and the collection's own order, its nested
folders and its favorites are lost. The **CCS Collection Interface** carries that information in one small index file that any menu can
read. This kit is everything a menu author needs to use it:

| Part | What it is |
|---|---|
| `schema/ccs-collection-interface-v1.schema.json` | The JSON Schema of the v1 index |
| `examples/` | Indexes that conform (`minimal.json`, `folders.json`) and, in `examples/invalid/`, ones that do not |
| `reader/ccs-ci`, `reader/ccs-ci-flat.awk` | A reference reader in POSIX `sh` and `awk` (BusyBox, mawk or gawk), small enough to carry inside a menu package |
| `checker/ccs_ci_check.py` | A conformance checker (Python 3, standard library only) |
| `tests/test_kit.py` | The kit's own tests |
| `build_kit.py` | Makes the versioned kit archive and its manifest |
| `LICENSE` | MIT License |

The kit is independent: it contains none of Classic Cart Studio's own code, and nothing in it needs Classic Cart Studio to run.

## Where the index is

On the console: `/var/lib/ccs/integration/v1/index.json`

The file is replaced in one rename each time it changes, so a reader never sees half of one: read it whole, once. Its `generation` number
goes up with every rewrite; a menu that keeps its own copy of the lists can compare the number to know whether anything changed.

## Reading it

With the reference reader (keep `ccs-ci` and `ccs-ci-flat.awk` in the same folder):

```sh
ccs-ci check                      # prints the generation, or exits 1 when there is no usable v1 index
ccs-ci collections                # installed<TAB>Weekend Picks
ccs-ci games installed            # one line per game, in the collection's order: ID<TAB>TITLE<TAB>ENTRY
ccs-ci folders installed          # ID<TAB>PARENT<TAB>NAME<TAB>ENTRY, a parent before its folders
ccs-ci folder-games installed f-0011223344556677
ccs-ci favorites installed
ccs-ci game nes-0f1e2d3c4b5a69788796a5b4c3d2e1f0   # title, status, reason, entry, cover, thumbnail
ccs-ci -f ./index.json flat       # every value, one per line (useful when writing your own reader)
```

`ENTRY` is the path on the console of the game's standard Hakchi entry (its `.desktop` file), or `-` when the game has none. To start a game,
start that entry the way your menu starts any Hakchi game. **The index never contains a command**, and a reader must never run anything
from it.

Your own reader can use any JSON parser. It must follow the rules below.

## The v1 index

```json
{
 "schema": "ccs-collection-interface",
 "version": 1,
 "generation": 7,
 "bases": {"entries": "/var/lib/hakchi/games/nes-usa", "system": "/var/squashfs/usr/share/games/nes/kachikachi"},
 "capabilities": ["hakchi-entries", "folders", "favorites"],
 "collections": [
  {"id": "installed", "name": "Weekend Picks", "games": ["nes-0f1e…", "nes-1a2b…"],
   "folders": [{"id": "f-0011…", "name": "Action", "parent": null, "games": ["nes-0f1e…"], "entry": {"base": "entries", "path": "…"}}],
   "favorites": ["nes-1a2b…"],
   "entry": {"base": "entries", "path": "000/CLV-S-POIUY/CLV-S-POIUY.desktop", "code": "CLV-S-POIUY"}}
 ],
 "games": {
  "nes-0f1e…": {"title": "Example Quest", "status": "published",
                "entry": {"base": "entries", "path": "ccs-2222222222/CLV-H-ABCDE/CLV-H-ABCDE.desktop", "code": "CLV-H-ABCDE"},
                "art": {"cover": {"base": "entries", "path": "ccs-2222222222/CLV-H-ABCDE/CLV-H-ABCDE.png"}}}
 }
}
```

Required fields and their meaning (this meaning never changes within v1):

| Field | Meaning |
|---|---|
| `schema` | Always `"ccs-collection-interface"` |
| `version` | Always `1` (the major version of the interface) |
| `generation` | A whole number from 1, higher after every rewrite |
| `bases` | Named console folders. Every path in the index is relative to one of them |
| `collections` | The collections, in order. Each has a stable `id`, a `name` and `games` (game ids, in the collection's order) |
| `games` | Every game a collection names, by its stable id. Each has a `title` |

Optional fields: `producer`, `capabilities`, a collection's `folders` (in order, a parent before its folders; each with `id`, `name`,
`parent` (null at the top) and `games`), `favorites` and `entry`; a game's `system`, `kind`, `content_sha256`, `status` (`published` or
`not-published`), `reason` (why it has no entry), `entry` and `art` (`cover`, `thumbnail`); `extensions` for anything else.

A **reference** is `{"base": NAME, "path": RELATIVE PATH}` (and, for an entry, the entry's `code`). Its console path is the base's folder,
`/`, then the path.

Ids are stable: the same game keeps its id from one installation to the next, and a collection or folder keeps its id while it exists.

## Rules for readers

1. Check `schema` and `version` first. Use nothing from a file that is not v1.
2. Ignore every field you do not know, and every `capabilities` word and `extensions` entry you do not know. Their presence never changes the
   meaning of a field you know.
3. Use a reference only when its base is listed in `bases` and its path is relative and plain: no leading `/`, no `.` or `..` part, only
   letters, digits, `.`, `_` and `-` in each part. Ignore any other reference.
4. Never run, open or download anything because the index names it. Start games through their standard entries only.
5. A game without an entry (for example one the console's own emulator cannot run) still belongs to its collections: show it as
   unavailable, or leave it out, but do not guess another file.
6. The index carries no game files, saves, system files, keys or console identifiers; a reader must not expect them.

## Rules for producers

1. Write only what this README defines, or new optional fields. Never change what a v1 field means.
2. Replace the whole file in one rename after writing it completely, and raise `generation` each time.
3. Use only relative paths from listed bases; never a computer's own path, a command, an embedded file or encoded data.
4. A future major version (`v2`) is written beside v1 (`/var/lib/ccs/integration/v2/index.json`), never in place of it, for as long as
   readers of v1 are supported. A reader that knows only v1 keeps working with v1.

## Checking an index

```sh
python3 checker/ccs_ci_check.py /var/lib/ccs/integration/v1/index.json
```

It prints `conforms to CCS Collection Interface v1`, or one line per problem (exit status 1), or why the file cannot be read (exit status
2). It checks the JSON Schema and the rules the schema cannot express: every reference's base is listed, every game named is listed, a
folder's parent comes before it, names are not repeated, and nothing in the document looks like a command, a computer's own path or an
embedded file.

## Tests

```sh
python3 tests/test_kit.py
```

The reader's tests need a POSIX shell with `awk` (on Windows, through WSL); set `CCS_CI_AWK` to test with a particular `awk` (for example
`busybox awk`).

## Versions of this kit

The kit's own version (in `VERSION`) is separate from the interface version: kit 1.x.y always describes interface v1.

## License

MIT License, Copyright (c) 2026 Rance9. See `LICENSE`.
