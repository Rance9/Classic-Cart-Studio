"""The notices every console package carries (licenses/ at the package's top), made only from records Classic Cart Studio already keeps:
nothing here states a license, an author or a source that a record does not. One path for every console package: the ones Classic Cart
Studio for Windows builds for a collection and the release-built CCS HOME that CCS Console Workbench carries
(tools/console-nucleus/build_home_package.py) are both made by console_package.build_console, which calls write() before its MANIFEST.json;
a file no part below covers, a record that is missing, or a license text that is not byte for byte its record stops the build.
Copyright (C) 2026 Rance9, GPL-3.0-or-later.

  licenses/NOTICE.txt   each part of the package: its files, what it is, who made it, its terms and its exact source (only parts present)
  licenses/<texts>      the license texts and notices those parts require, byte for byte from their records

The records (RECORDS: paths in the source repository; the frozen Windows app carries the same files under its bundle's notices/ folder,
companion.spec, stage()): THIRD_PARTY_NOTICES.md (rows and sections quoted as they are), tools/player/third_party/fceumm (Copying, Authors,
LICENSE.emu2413.txt, LICENSE.pic16c5x.txt; the notice at the head of pic16c5x.c and of each compiled libretro-common file),
tools/player/third_party/sdl_gamecontrollerdb/LICENSE, tools/player/third_party/zig-runtime (musl's COPYRIGHT; the Apache 2.0 with LLVM
Exceptions text, compiler-rt's license), the license section of stb_image.h, tools/console-worker/licenses/GPL-3.0.txt; and from the site:
art/scenes/index.json (each scene's credit) and included/index.json (each Included Game's author, license, cover credit and source; the
game's own license file is carried byte for byte).

  python notices.py --check PACKAGE_DIR          a built package's notices (exit 1 with the problems)
  python notices.py --audit                       the records themselves (source checkout): every record file, row and section present,
                                                  the compiled libretro-common files as tools/player/scripts/build_player.sh names them
  python notices.py --matrix PACKAGE_DIR --out F  the package's part-to-notice/source matrix (Markdown F, JSON beside it)"""
from __future__ import annotations
import hashlib, json, os, re, shutil, sys
HERE = os.path.dirname(os.path.abspath(__file__)); TOOLS = os.path.normpath(os.path.join(HERE, "..", "..")); PROJECT = os.path.dirname(TOOLS)
for _p in (os.path.dirname(HERE), os.path.join(TOOLS, "rom-pipeline")):
    if _p not in sys.path: sys.path.insert(0, _p)
from common import FROZEN, BUNDLE, site_path   # noqa: E402

SCHEMA = "ccs-package-notices/1"
CCS_REPO = "the Classic Cart Studio source repository at the commit each release names"
CCS_SOURCE = (CCS_REPO + ", published with each release under GPL-3.0-or-later, "
              "and available on request from the publisher for three years after distribution")   # as the CCS Console Workbench NOTICE says it
LRC = "tools/player/third_party/fceumm/src/drivers/libretro/libretro-common/"
LIBRETRO_COMMON = ["streams/memory_stream.c", "compat/compat_posix_string.c", "compat/compat_snprintf.c", "compat/compat_strcasestr.c", "compat/compat_strl.c",
                   "compat/fopen_utf8.c", "encodings/encoding_utf.c", "file/file_path.c", "file/file_path_io.c", "streams/file_stream.c",
                   "streams/file_stream_transforms.c", "string/stdstring.c", "time/rtime.c", "vfs/vfs_implementation.c"]   # as build_player.sh compiles them (audit())
RECORDS = {
    "notices": "THIRD_PARTY_NOTICES.md",
    "gpl3": "tools/console-worker/licenses/GPL-3.0.txt",
    "fceumm_copying": "tools/player/third_party/fceumm/Copying",
    "fceumm_authors": "tools/player/third_party/fceumm/Authors",
    "emu2413": "tools/player/third_party/fceumm/LICENSE.emu2413.txt",
    "pic16c5x_text": "tools/player/third_party/fceumm/LICENSE.pic16c5x.txt",
    "pic16c5x_src": "tools/player/third_party/fceumm/src/boards/pic16c5x.c",
    "sdl_db": "tools/player/third_party/sdl_gamecontrollerdb/LICENSE",
    "musl": "tools/player/third_party/zig-runtime/musl-COPYRIGHT",
    "apache_llvm": "tools/player/third_party/zig-runtime/wasi-libc-LICENSE-APACHE-LLVM",
    "stb": "tools/console-simulator/core/third_party/stb_image.h",
} | {"lrc:" + f: LRC + f for f in LIBRETRO_COMMON}

def record_path(key: str) -> str:
    rel = RECORDS[key]
    return os.path.join(BUNDLE, "notices", *rel.split("/")) if FROZEN else os.path.join(PROJECT, *rel.split("/"))

def read(key: str) -> bytes:
    with open(record_path(key), "rb") as f: return f.read()

def stage(dst: str) -> str:
    """the record files at their repository paths under dst (companion.spec bundles dst as notices/)"""
    for rel in RECORDS.values():
        p = os.path.join(dst, *rel.split("/")); os.makedirs(os.path.dirname(p), exist_ok=True); shutil.copyfile(os.path.join(PROJECT, *rel.split("/")), p)
    return dst

# ------------------------------------------------------------------ the license texts, each made from its record
def _lf(b: bytes) -> str: return b.decode("utf-8", "replace").replace("\r\n", "\n")

def _first_comment(b: bytes) -> str:
    s = _lf(b); i = s.index("/*"); return s[i:s.index("*/", i) + 2]

def _stb(r) -> bytes:
    s = _lf(r["stb"]); i = s.rindex("This software is available under 2 licenses"); i = s.rindex("\n", 0, s.rindex("\n", 0, i)) + 1; j = s.rindex("*/")
    return ("stb_image v2.30 (https://github.com/nothings/stb), by Sean Barrett: the license section at the end of stb_image.h, as it is.\n\n" + s[i:j].rstrip() + "\n").encode()

def _pic(r) -> bytes:
    head = "\n".join(_lf(r["pic16c5x_src"]).split("\n")[:8])
    return ("pic16c5x (src/boards/pic16c5x.c in FCEUmm, from MAME). The head of the file, as it is:\n\n" + head + "\n\n"
            "The license it names, as MAME publishes it (docs/legal/BSD-3-Clause at MAME commit ff7d968ebd027ace692a2e91e48632f1c6156238):\n\n").encode() + r["pic16c5x_text"]

def _lrc(r) -> bytes:
    out = ["libretro-common: the parts FCEUmm compiles into the game player. Each of these files carries its own notice, reproduced here as it is."]
    for f in LIBRETRO_COMMON: out += ["", "--- " + f + " ---", _first_comment(r["lrc:" + f])]
    return ("\n".join(out) + "\n").encode()

# file name in licenses/ -> (the record keys it is made from, maker)
TEXTS = {
    "GPL-3.0.txt": (("gpl3",), lambda r: r["gpl3"]),
    "FCEUmm-Copying.txt": (("fceumm_copying",), lambda r: r["fceumm_copying"]),
    "FCEUmm-Authors.txt": (("fceumm_authors",), lambda r: r["fceumm_authors"]),
    "emu2413-LICENSE.txt": (("emu2413",), lambda r: r["emu2413"]),
    "pic16c5x-LICENSE.txt": (("pic16c5x_src", "pic16c5x_text"), _pic),
    "libretro-common-NOTICES.txt": (tuple("lrc:" + f for f in LIBRETRO_COMMON), _lrc),
    "SDL_GameControllerDB-LICENSE.txt": (("sdl_db",), lambda r: r["sdl_db"]),
    "stb_image-LICENSE.txt": (("stb",), _stb),
    "musl-COPYRIGHT.txt": (("musl",), lambda r: r["musl"]),
    "Apache-2.0-with-LLVM-Exceptions.txt": (("apache_llvm",), lambda r: r["apache_llvm"]),
}
PLAYER_TEXTS = ["GPL-3.0.txt", "FCEUmm-Copying.txt", "FCEUmm-Authors.txt", "emu2413-LICENSE.txt", "pic16c5x-LICENSE.txt", "libretro-common-NOTICES.txt",
                "SDL_GameControllerDB-LICENSE.txt", "stb_image-LICENSE.txt", "musl-COPYRIGHT.txt", "Apache-2.0-with-LLVM-Exceptions.txt"]
HOME_TEXTS = ["GPL-3.0.txt", "stb_image-LICENSE.txt", "musl-COPYRIGHT.txt", "Apache-2.0-with-LLVM-Exceptions.txt"]

def texts(names) -> dict[str, bytes]:
    keys = {k for n in names for k in TEXTS[n][0]}; recs = {k: read(k) for k in keys}
    return {n: TEXTS[n][1](recs) for n in names}

# ------------------------------------------------------------------ THIRD_PARTY_NOTICES.md, quoted as it is
def _tpn() -> str: return _lf(read("notices"))

def row(first_cell: str) -> str:
    """a table row of THIRD_PARTY_NOTICES.md, by its first cell: each column's heading with the row's cell"""
    lines = _tpn().split("\n")
    for i, l in enumerate(lines):
        if l.startswith("| " + first_cell + " |"):
            j = i
            while j > 0 and not lines[j - 1].startswith("|---"): j -= 1
            head = [c.strip() for c in lines[j - 2].strip().strip("|").split("|")]; cells = [c.strip() for c in l.strip().strip("|").split("|")]
            return "\n".join(f"    {h}: {c}" for h, c in zip(head, cells))
    raise KeyError("THIRD_PARTY_NOTICES.md has no row " + first_cell)

def section(title: str) -> str:
    m = re.search(r"^## " + re.escape(title) + r"\n(.*?)(?=^## |\Z)", _tpn(), re.S | re.M)
    if not m: raise KeyError("THIRD_PARTY_NOTICES.md has no section " + title)
    return m.group(1).strip()

ROWS = ["Barlow, Silkscreen, Cinzel, VT323, Pixelify Sans, Space Mono", "Press Start 2P", "HOME theme art", "Shared HOME scene backgrounds",
        "Game box art, screenshots and title screens"]
SECTIONS = ["Included Games", "Console startup images", "HOME Music"]

# ------------------------------------------------------------------ the site's records
def _json(*p):
    with open(site_path(*p), encoding="utf-8") as f: return json.load(f)

def included() -> dict:
    """the Included Games by the SHA-256 of their game data (a package's roms/<that>.nes)"""
    try: d = _json("included", "index.json")
    except (OSError, ValueError): return {}
    return {g["content_sha256"]: g for g in d.get("games") or [] if g.get("content_sha256")}

def scene_credits() -> dict:
    return {k: v.get("credit") or {} for k, v in (_json("art", "scenes", "index.json").get("scenes") or {}).items()}

def game_license_name(g: dict) -> str: return f"{g['key']}-{os.path.basename(g['license_file'])}"

# ------------------------------------------------------------------ which part each file is
CCS_FILES = {"session.json", "startup/startup.json", "home/config.json", "home/pack.json", "home/games.json", "home/manifest.json"}
COVER_PARTS = {"included": "covers-included", "authentic": "covers-box-art", "user": "own-pictures", "mock": "ccs-drawn"}

def classify(files, games: list) -> dict[str, list[str]]:
    """part id -> its files; the part "" holds the files no part covers. games: the HOME's games.json records"""
    covers: dict[str, set] = {}
    for g in games:
        if g.get("cover"): covers.setdefault("home/" + g["cover"], set()).add("included" if str(g.get("cover_source") or "").startswith("included/") else str(g.get("cover_kind") or ""))
    out: dict[str, list] = {}
    for rel in sorted(files):
        if rel.startswith("licenses/"): p = "notices"
        elif rel == "bin/ccs-player-armv7": p = "player"
        elif rel == "home/bin/ccs-home-armv7": p = "home-runtime"
        elif rel.startswith(("fonts/", "home/font/")) or re.fullmatch(r"home/licenses/OFL-[A-Za-z0-9]+\.txt", rel): p = "fonts"
        elif rel == "home/licenses/THIRD-PARTY.txt": p = "home-notice"
        elif re.fullmatch(r"home/theme/icon-[a-z]+-\d+(-2x)?\.png", rel): p = "ccs-drawn"
        elif rel.startswith("home/theme/"): p = "theme-art"
        elif rel.startswith(("home/hero/", "home/wide/")): p = "scenes"
        elif rel.startswith("home/cover/"): k = covers.get(rel) or set(); p = COVER_PARTS.get(next(iter(k)), "") if len(k) == 1 else ""
        elif rel.startswith(("home/folder/", "home/design/", "startup/startup-custom-")): p = "own-pictures"
        elif rel.startswith("home/music/"): p = "music"
        elif rel.startswith("home/text/"): p = "texts"
        elif re.fullmatch(r"startup/startup-[a-z-]+\.jpg", rel): p = "startup"
        elif rel.startswith("roms/"): p = "games"
        elif rel in CCS_FILES or rel.startswith("config/"): p = "ccs-files"
        else: p = ""
        out.setdefault(p, []).append(rel)
    return out

# ------------------------------------------------------------------ the notice
def build(files, games: list, label: str) -> tuple[dict[str, bytes], dict]:
    """the licenses/ files for a package of these files (paths; licenses/ itself is left out) whose HOME lists these games: (name -> bytes, the matrix)"""
    parts = classify([f for f in files if not f.startswith("licenses/")], games)
    if parts.get(""): raise ValueError("no notice covers these files of the package: " + ", ".join(parts[""][:5]))
    inc = included(); roms = parts.get("games") or []
    inc_games = [inc[os.path.splitext(r[5:])[0]] for r in roms if os.path.splitext(r[5:])[0] in inc]; own = [r for r in roms if os.path.splitext(r[5:])[0] not in inc]
    gtexts = {}
    for g in inc_games:
        with open(site_path(*g["license_file"].split("/")), "rb") as f: gtexts[game_license_name(g)] = f.read()
    scenes = sorted({g.get("scene") for g in games if g.get("scene")}) if parts.get("scenes") else []; credits = scene_credits() if scenes else {}
    bad = [s for s in scenes if not (credits.get(s) or {}).get("license")]
    if bad: raise ValueError("a HOME scene has no credit record: " + ", ".join(bad))
    names = sorted(set((PLAYER_TEXTS if "player" in parts else []) + (HOME_TEXTS if "home-runtime" in parts else []) + (["GPL-3.0.txt"] if {"ccs-files", "ccs-drawn"} & set(parts) else [])))
    out = texts(names) | gtexts
    mx = {"schema": SCHEMA, "label": label, "parts": [], "texts": {}, "record_gaps": []}
    L: list[str] = []
    def item(pid, title, what, terms, source, needs):
        fs = parts.get(pid) or []
        if not fs: return
        mx["parts"].append({"id": pid, "title": title, "files": fs, "what": what, "terms": terms, "source": source, "texts": needs})
        L.append(title); L.append("  Files: " + (", ".join(fs) if len(fs) <= 6 else f"{len(fs)} files (MANIFEST.json lists each one), among them " + ", ".join(fs[:4])))
        for k, v in (("What it is", what), ("Terms", terms), ("Source", source)):
            if v: L.append(f"  {k}: {v}")
        if needs: L.append("  Texts in this folder: " + ", ".join(needs))
        L.append("")
    L += [f"Notices for this console package ({label}), made by Classic Cart Studio", "=" * 76,
          "Each part of this package, with its files, what it is, who made it, the terms it comes under and where its exact source is. The",
          "license texts and notices these parts require are in this folder. Classic Cart Studio's own parts are free software under the GNU",
          "General Public License, version 3 or (at your option) any later version (GPL-3.0.txt). That license does not cover the third-party",
          "material below, which stays under its owners' rights and licenses.", ""]
    item("player", "The game player (bin/ccs-player-armv7)",
         "Classic Cart Studio's game player for the console (tools/player, with the HOME core files of tools/console-simulator/core), compiled by zig 0.17.0 "
         "for arm-linux-musleabihf as one static program. It contains: FCEUmm (libretro-fceumm) at commit ac0d00a7fc01166f84c7bac01d1084ebfd2b865e, unchanged "
         "(tools/player/third_party/fceumm/VENDORED.md lists what is compiled); in it, libretro-common (the parts FCEUmm compiles), pic16c5x (Tony La Porta, from MAME) "
         "and emu2413 0.60 as modified for FCE Ultra and FCEUmm; SDL_GameControllerDB at commit 07af1d50187e94c861c9f9f5d8a55b148f11306b, as data (the layouts of known "
         "controllers, tools/player/core/gp_pad_db.h); stb_image v2.30; and, as zig 0.17.0 carries and links them, musl libc and compiler-rt.",
         "Classic Cart Studio's code: GPL-3.0-or-later. FCEUmm: GNU GPL version 2 or (at your option) any later version; the combined player is distributed under "
         "GPL version 3 or later. libretro-common: an MIT-style permission notice in each file. pic16c5x: BSD-3-Clause. emu2413: MIT License, Copyright (c) "
         "2001-2019 Mitsutaka Okazaki. SDL_GameControllerDB: zlib License. stb_image: public domain (Unlicense) or MIT, at the user's choice. musl libc: MIT. "
         "compiler-rt: Apache-2.0 with LLVM Exceptions.",
         CCS_SOURCE + " (tools/player, tools/console-simulator/core, and the vendored sources in tools/player/third_party). Upstream: "
         "https://github.com/libretro/libretro-fceumm (at the commit above), https://github.com/mdqinc/SDL_GameControllerDB (at the commit above), "
         "https://github.com/nothings/stb; zig 0.17.0: https://ziglang.org/download/0.17.0/ (tools/console-simulator/toolchain.lock.json gives the archive's SHA-256).",
         PLAYER_TEXTS)
    item("home-runtime", "The HOME program (home/bin/ccs-home-armv7)",
         "Classic Cart Studio's native HOME (tools/console-simulator/core), compiled by zig 0.17.0 for arm-linux-musleabihf as one static program, with stb_image v2.30 "
         "and, as zig 0.17.0 carries and links them, musl libc and compiler-rt.",
         "Classic Cart Studio's code: GPL-3.0-or-later. stb_image: public domain (Unlicense) or MIT, at the user's choice. musl libc: MIT. compiler-rt: Apache-2.0 with LLVM Exceptions.",
         CCS_SOURCE + " (tools/console-simulator/core).", HOME_TEXTS)
    item("fonts", "Fonts (fonts/, home/font/)",
         "Font atlases (.ccsf) drawn from these fonts by Classic Cart Studio's tools, with the fonts' license texts. From THIRD_PARTY_NOTICES.md:\n" + row(ROWS[0]) + "\n" + row(ROWS[1]),
         "SIL Open Font License 1.1: the texts are the OFL-*.txt files in fonts/ and home/licenses/.", "", [])
    item("home-notice", "The HOME's own notice (home/licenses/THIRD-PARTY.txt)", "The HOME package's notice of stb_image and its fonts, carried with it.", "", "", [])
    item("theme-art", "HOME theme art (home/theme/)",
         "The four themes' scenes, overlays, sprites and thumbnails, with the copies of the sprites Classic Cart Studio's tools cut out and enlarge from them "
         "(tools/companion/native_home/own_sprites.py, hd_sprites.py). From THIRD_PARTY_NOTICES.md:\n" + row(ROWS[2]), "As recorded above.", "", [])
    if scenes:
        used = "\n".join(f"    {s}: \"{credits[s].get('title')}\" by {credits[s].get('artist')}, {credits[s].get('license')}"
                         + (f" ({credits[s]['license_url']})" if credits[s].get("license_url") else "") + f", {credits[s].get('source')}. Changes: {credits[s].get('changes')}" for s in scenes)
        item("scenes", "Game backdrops (home/hero/, home/wide/)",
             "Each game's backdrop is the shared HOME scene of its genre (art/scenes/index.json), cut and scaled by Classic Cart Studio for the HOME's details and wide "
             "pictures. The scenes this package's games use:\n" + used + "\n  From THIRD_PARTY_NOTICES.md:\n" + row(ROWS[3]), "As recorded above.", "", [])
    if parts.get("covers-included"):
        cr = "; ".join(f"{g['title']}: {g['cover']['credit']}" for g in inc_games if (g.get("cover") or {}).get("credit"))
        item("covers-included", "The Included Games' covers (home/cover/)", cr, "As the game's own (see Games, below).", "As the game's own (see Games, below).", [])
    item("covers-box-art", "Game box art (home/cover/)", "From THIRD_PARTY_NOTICES.md:\n" + row(ROWS[4]), "As recorded above.", "", [])
    item("ccs-drawn", "Pictures Classic Cart Studio draws (home/theme/icon-*.png; an NES-style cover for a game without box art)",
         "Drawn by Classic Cart Studio's own code (tools/companion/native_home/derive.py).", "GPL-3.0-or-later, with the rest of Classic Cart Studio.", CCS_SOURCE + ".", ["GPL-3.0.txt"])
    item("own-pictures", "The collection's own pictures (a startup picture, folder pictures, covers or a HOME design chosen for this collection)",
         "Pictures the person who made this collection supplied or chose; they belong to whoever made them.", "", "", [])
    item("music", "HOME Music (home/music/)", "From THIRD_PARTY_NOTICES.md: " + section("HOME Music"), "As recorded above.", "", [])
    item("startup", "Console startup images (startup/)", "From THIRD_PARTY_NOTICES.md: " + section("Console startup images"), "As recorded above.", "", [])
    if parts.get("texts"):
        inc_text = {"home/" + g["text"] for g in games if g.get("text") and g.get("id") in {"p_" + x["content_sha256"][:16] for x in inc_games}}
        rest = [f for f in parts["texts"] if f not in inc_text]
        what = "The descriptions Game Details shows." + (" For the Included Games: Classic Cart Studio's own words (included/index.json)." if inc_text else "") + \
               (" For other games: the descriptions Classic Cart Studio's catalog records (an official description is its publisher's text; no license is claimed for it), or the person's own words." if rest else "")
        item("texts", "Game descriptions (home/text/)", what, "", "", [])
        if rest: mx["record_gaps"].append("home/text/ (packages of catalog games only): THIRD_PARTY_NOTICES.md has no row for the catalog's game descriptions")
    if roms:
        lines = []
        for g in inc_games:
            a = g["original_archive"]
            lines.append(f"{g['title']} {g['version']} ({g['year']}) by {g['creator']} ({g['author_page']}): roms/{g['content_sha256']}.nes is the author's own build, unchanged "
                         f"(file SHA-256 {g['file_sha256']}). License: {g['license']}; its text, as the author's source has it: {game_license_name(g)}. Corresponding source, "
                         f"as the author published it (archive {a['name']}, SHA-256 {a['sha256']}; {a.get('files_kept')} files kept): site-data/{g['source']} in {CCS_REPO}; "
                         f"and in Classic Cart Studio for Windows, beside the game ({g['source']}).")
        if own: lines.append(f"{len(own)} other game file(s): supplied by the person who made this collection. Classic Cart Studio supplies no commercial NES game.")
        item("games", "Games (roms/)", "\n  ".join(lines) + (("\n  From THIRD_PARTY_NOTICES.md: " + section("Included Games")) if inc_games else ""),
             "As recorded above." if inc_games else "", "", [game_license_name(g) for g in inc_games])
    item("ccs-files", "Classic Cart Studio's own files for this package",
         "The package's session, settings and startup choice and the HOME's configuration and lists, written by Classic Cart Studio for this collection.",
         "GPL-3.0-or-later, with the rest of Classic Cart Studio.", CCS_SOURCE + ".", ["GPL-3.0.txt"])
    L += ["The license texts and notices in this folder", "-" * 44] + [f"  {n} ({len(out[n]):,} bytes, SHA-256 {hashlib.sha256(out[n]).hexdigest()})" for n in sorted(out)]
    out["NOTICE.txt"] = ("\n".join(L) + "\n").encode("utf-8")
    mx["texts"] = {n: {"bytes": len(b), "sha256": hashlib.sha256(b).hexdigest()} for n, b in sorted(out.items())}
    return out, mx

def write(st, games: list, label: str) -> dict:
    """adds licenses/ to a package being staged (console_package.Stage) and checks what was written; returns the matrix"""
    out, mx = build(list(st.files), games, label)
    for n, b in sorted(out.items()): st.put("licenses/" + n, b, "license")
    def rf(rel):
        with open(os.path.join(st.d, *rel.split("/")), "rb") as f: return f.read()
    probs = check(st.files, rf, label, games)
    if probs: raise ValueError("the package's notices are not complete: " + "; ".join(probs[:5]))
    return mx

def check(files: dict, read_file, label: str, games: list | None = None) -> list[str]:
    """problems with a package's notices (none: an empty list). files: its MANIFEST files map; read_file(rel): a file's bytes; label: its
    MANIFEST label; games: the HOME's games.json records (read from the package when not given)"""
    if "licenses/NOTICE.txt" not in files: return ["the package has no licenses/NOTICE.txt"]
    if games is None:
        try: games = json.loads(read_file("home/games.json")).get("games") or []
        except Exception: return ["the package's home/games.json cannot be read"]
    try: want, _ = build(list(files), games, label)
    except (ValueError, KeyError, OSError) as e: return [str(e)]
    have = {f[len("licenses/"):] for f in files if f.startswith("licenses/")}
    p = [f"licenses/{n} is missing" for n in sorted(set(want) - have)] + [f"licenses/{n} is not a notice of this package" for n in sorted(have - set(want))]
    p += [f"licenses/{n} is not {'the notice of this package' if n == 'NOTICE.txt' else 'the text of its record'}" for n in sorted(set(want) & have) if read_file("licenses/" + n) != want[n]]
    return p

def check_dir(d: str) -> list[str]:
    with open(os.path.join(d, "MANIFEST.json"), encoding="utf-8") as f: m = json.load(f)
    def rf(rel):
        with open(os.path.join(d, *rel.split("/")), "rb") as f: return f.read()
    return check(m["files"], rf, m.get("label") or "HOME")

def audit() -> list[str]:
    """the records themselves, in a source checkout"""
    p = [f"record missing: {rel}" for rel in RECORDS.values() if not os.path.isfile(os.path.join(PROJECT, *rel.split("/")))]
    if p: return p
    for r in ROWS:
        try: row(r)
        except KeyError as e: p.append(str(e))
    for s in SECTIONS:
        try: section(s)
        except KeyError as e: p.append(str(e))
    with open(os.path.join(TOOLS, "player", "scripts", "build_player.sh"), encoding="utf-8") as f: sh = f.read()
    built = sorted(set(re.findall(r"\$LC/([A-Za-z0-9_/]+\.c)", sh)))
    if built != sorted(LIBRETRO_COMMON): p.append(f"the libretro-common files build_player.sh compiles differ from this list: {sorted(set(built) ^ set(LIBRETRO_COMMON))}")
    try: texts(list(TEXTS))
    except (ValueError, OSError) as e: p.append(f"a license text cannot be made from its record: {e}")
    srcd = os.path.join(PROJECT, "site-data")
    with open(os.path.join(srcd, "included", "index.json"), encoding="utf-8") as f: idx = json.load(f)
    for g in idx.get("games") or []:
        if not os.path.isfile(os.path.join(srcd, *g["license_file"].split("/"))): p.append(f"{g['title']}: its license file {g['license_file']} is not in site-data")
        if not os.path.isdir(os.path.join(srcd, *g["source"].split("/"))): p.append(f"{g['title']}: its source {g['source']} is not in site-data")
    return p

def matrix_md(mx: dict) -> str:
    cell = lambda s: (s or "").replace("|", "/").replace("\n", " ")
    L = [f"# Notice matrix: the console package \"{mx['label']}\"", "", "| Part | Files | Terms | Source | Texts in licenses/ |", "|---|---|---|---|---|"]
    for c in mx["parts"]:
        fs = ", ".join(f"`{x}`" for x in c["files"][:3]) + (f" and {len(c['files']) - 3} more" if len(c["files"]) > 3 else "")
        L.append(f"| {cell(c['title'])} | {fs} | {cell(c['terms'] or c['what'])} | {cell(c['source']) or 'as recorded'} | {', '.join(c['texts']) or '-'} |")
    L += ["", "| File in licenses/ | Bytes | SHA-256 |", "|---|---:|---|"] + [f"| `{n}` | {v['bytes']:,} | `{v['sha256']}` |" for n, v in mx["texts"].items()]
    if mx["record_gaps"]: L += ["", "Record gaps:"] + [f"- {g}" for g in mx["record_gaps"]]
    return "\n".join(L) + "\n"

if __name__ == "__main__":
    import argparse
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--check"); ap.add_argument("--audit", action="store_true"); ap.add_argument("--matrix"); ap.add_argument("--out")
    a = ap.parse_args(); probs = []
    if a.audit: probs += audit()
    if a.check: probs += check_dir(a.check)
    if a.matrix:
        d = a.matrix
        with open(os.path.join(d, "MANIFEST.json"), encoding="utf-8") as f: m = json.load(f)
        with open(os.path.join(d, "home", "games.json"), encoding="utf-8") as f: games = json.load(f).get("games") or []
        _, mx = build(list(m["files"]), games, m.get("label") or "HOME")
        with open(a.out, "w", encoding="utf-8", newline="\n") as f: f.write(matrix_md(mx))
        with open(os.path.splitext(a.out)[0] + ".json", "w", encoding="utf-8", newline="\n") as f: json.dump(mx, f, indent=1, ensure_ascii=False)
    print("NOTICES OK" if not probs else "NOTICES NOT COMPLETE:\n  " + "\n  ".join(probs)); sys.exit(1 if probs else 0)
