"""A collection's games without CCS HOME: when CCS Mod is not chosen, Classic Cart Studio puts on the console only what the collection's
Hakchi game entries in Original Menu need (hakchi_publish.py), never CCS HOME's program, game player, themes, pictures, music, fonts or
session. Copyright (C) 2026 Rance9, GPL-3.0-or-later.

Where (written by ccs_install.Installer, recorded like every copy it makes):
  /var/lib/ccs/games/<version>.<attempt>/   one copy per installation attempt, never changed afterwards:
      roms/<content SHA-256>.nes            the game files the Hakchi entries launch where they are (the same bytes as in the collection's
                                            console package; an Included Game's own file always travels, with its license)
      licenses/NOTICE.txt                   each game file's terms and source, in the words every console package uses (native_home/notices.py)
      licenses/<game>-<license file>        the license texts those games require, byte for byte from their records
  /var/lib/ccs/install/games-<copy>.json    this program's record of the copy (its files and their SHA-256)
Nothing here is a HOME: no pointer names it (pkg/current is never changed for it), it is not under pkg/, it has no player or session and no
menu entry opens it. A game whose Hakchi entry carries Hakchi's own copy of its file (a file without its header, or one whose header Hakchi
rewrites) needs nothing here; a built-in game is a reference to the console's own game. When CCS Mod is added later, CCS HOME's own copy
arrives under pkg/, the Hakchi entries are written again naming the game files in it (one copy of each game on the console), and the games
copy is removed once no entry names it."""
from __future__ import annotations
import hashlib, json

GAMES_DIR = "/var/lib/ccs/games"
RECORD = "ccs-owned-games/1"
SCHEMA = "ccs-games-copy/1"

def _sha(b: bytes) -> str: return hashlib.sha256(b).hexdigest()

def size_text(n: int) -> str:
    """a byte count in plain decimal units (1 KB = 1,000 bytes)"""
    if n < 1000: return f"{n:,} byte{'' if n == 1 else 's'}"
    for unit, d in (("KB", 1e3), ("MB", 1e6), ("GB", 1e9)):
        if n < d * 1000 or unit == "GB": return f"{n / d:.1f} {unit}"
    return f"{n:,} bytes"

def needed_roms(sp: dict) -> list[str]:
    """the package's game files the Hakchi entries need on the console (paths in the package, roms/<SHA-256>.nes): each game added as a
    Hakchi game whose entry launches its file where it is, and each Included Game added as a Hakchi game (its own unchanged file travels
    with its license even where its entry carries Hakchi's copy)"""
    from native_home import notices as NT
    inc = NT.included(); out = []
    for g in sp["games"]:
        if g.get("status") != "publish" or g.get("kind") != "file" or not g.get("rom"): continue
        if g.get("copy") is None or g.get("content_sha256") in inc: out.append(g["rom"])
    return sorted(dict.fromkeys(out))

def notice(roms: list[str], label: str) -> dict[str, bytes]:
    """licenses/ for these game files: the license texts their records require and NOTICE.txt, whose part for the games is the one every
    console package carries (native_home/notices.py build), under a heading for a folder of game files"""
    from native_home import notices as NT
    out, mx = NT.build(roms, [], label)
    texts = {n: b for n, b in out.items() if n != "NOTICE.txt"}
    L = [f"Notices for the game files of this collection ({label}), made by Classic Cart Studio", "=" * 76,
         "The game files of this collection that its Hakchi games in Original Menu use, with who made each one, the terms it comes under and",
         "where its exact source is. The license texts these games require are in this folder. No CCS HOME is in this folder.", ""]
    for c in mx["parts"]:
        fs = c["files"]
        L.append(c["title"]); L.append("  Files: " + (", ".join(fs) if len(fs) <= 6 else f"{len(fs)} files, among them " + ", ".join(fs[:4])))
        for k, v in (("What it is", c["what"]), ("Terms", c["terms"]), ("Source", c["source"])):
            if v: L.append(f"  {k}: {v}")
        if c["texts"]: L.append("  Texts in this folder: " + ", ".join(c["texts"]))
        L.append("")
    if texts: L += ["The license texts in this folder", "-" * 32] + [f"  {n} ({len(b):,} bytes, SHA-256 {_sha(b)})" for n, b in sorted(texts.items())]
    return {**texts, "NOTICE.txt": ("\n".join(L) + "\n").encode("utf-8")}

def files_for(package_dir: str, package_files: dict, sp: dict, label: str) -> tuple[list[dict], dict[str, bytes]]:
    """the games copy for a collection's console package: ([{"path", "size", "sha256", "mode", "from": "package" (the same path in the package)
    or "made"}], {path: bytes} of the files made here). package_files: the package's checked {path: SHA-256}. Deterministic: the same
    package and records always give the same copy"""
    import os
    files, made = [], {}
    for rel in needed_roms(sp):
        if rel not in package_files: raise ValueError(f"{rel} is not in the console package")
        with open(os.path.join(package_dir, *rel.split("/")), "rb") as f: b = f.read()
        if _sha(b) != package_files[rel]: raise ValueError(f"{rel} differs from the package's manifest")
        files.append({"path": rel, "size": len(b), "sha256": package_files[rel], "mode": "0o644", "from": "package"})
    rels = [f["path"] for f in files]
    if rels:
        for n, b in sorted(notice(rels, label).items()):
            made["licenses/" + n] = b; files.append({"path": "licenses/" + n, "size": len(b), "sha256": _sha(b), "mode": "0o644", "from": "made"})
    return sorted(files, key=lambda f: f["path"]), made

def version(files: list[dict]) -> str:
    """the copy's version: the SHA-256 of its file list (paths, SHA-256 and modes), as a copy name begins with its first 16 hex"""
    return _sha(json.dumps([[f["path"], f["sha256"], f["mode"]] for f in files], separators=(",", ":")).encode())
