"""The release-built minimal CCS HOME package that the CCS Console Workbench (the standalone and the one inside Classic Cart Studio) places
on a console when it installs CCS Mod (the only provider of CCS HOME), so CCS HOME has something to show without a full Studio or a Hakchi
PC program.
Copyright (C) 2026 Rance9, GPL-3.0-or-later.

  python build_home_package.py --site SITE --player ccs-player-armv7 --runtime ccs-home-armv7 --out DIR

SITE: the built website (build_site_ci.py output; its included/ folder holds the Included Games: each author's exact build, under the license
its record quotes, with that game's source beside it). The package is built with the app's own builder (tools/companion/native_home/
console_package.py, the same code Classic Cart Studio uses for a collection), offline, from the Included Games collection, in CCS HOME's one
layout (mod: its leave item, Choose Another Menu, is decided on the console). Output (a new or empty folder; the console programs' folder
gets it as ccs-home/, which the .NET build embeds next to the five images):
  <layout>.MANIFEST.json   the layout's own MANIFEST.json, byte for byte as the builder wrote it (its SHA-256 names the package version)
  <sha256>.blob            every file of the package once, named by its SHA-256
  HOME.json                schema ccs-home-release/1: per layout its MANIFEST.json SHA-256, the files and the total bytes; the games and
                           their licenses (each game file is the author's own exact build; its source is in the website's included/source/)
  sample.json              schema ccs-home-preview-sample/1: HOME Designer's sample games, a preview-only HOME (see build_sample); its files
                           are blobs here too (most it shares with the packages)
Each layout carries licenses/ (NOTICE.txt and the license texts its parts require, tools/companion/native_home/notices.py); the build stops
when a record is missing or a file of the package has no notice.
The engine (CcsBackup Engine/HomePackage.cs) reads a layout back by its MANIFEST.json and checks every blob's size and SHA-256 against it.
The sample is never part of a layout: no MANIFEST.json names its files, so it is never sent to a console."""
from __future__ import annotations
import argparse, hashlib, json, os, shutil, sys, tempfile
HERE = os.path.dirname(os.path.abspath(__file__)); TOOLS = os.path.dirname(HERE)
SCHEMA = "ccs-home-release/1"; LAYOUTS = ("mod",)   # CCS HOME's one layout (native_home/console_package.py LAYOUTS)

def sha(b: bytes) -> str: return hashlib.sha256(b).hexdigest()

def source_time() -> str:
    """the package's recorded time: SOURCE_DATE_EPOCH when set, else this source's commit time, so the same source always gives the same
    MANIFEST.json and so the same package id (the build's own clock would make every build a new version of identical files)"""
    import subprocess, time
    t = os.environ.get("SOURCE_DATE_EPOCH", "").strip()
    if not t.isdigit():
        r = subprocess.run(["git", "log", "-1", "--format=%ct"], cwd=HERE, capture_output=True, text=True, stdin=subprocess.DEVNULL)
        t = r.stdout.strip()
        if r.returncode != 0 or not t.isdigit(): raise SystemExit("the source's commit time could not be read: set SOURCE_DATE_EPOCH")
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(int(t)))

def build(site: str, player: str, runtime: str, out: str) -> dict:
    if os.path.exists(out) and os.listdir(out): raise SystemExit(f"{out} exists and is not empty: the HOME package is always built into a new folder")
    created = source_time()
    site = os.path.abspath(site); work = tempfile.mkdtemp(prefix="ccs-home-release-")
    os.environ["NES_STUDIO_SITE"] = site; os.environ["NES_STUDIO_DATA"] = os.path.join(work, "data"); os.makedirs(os.environ["NES_STUDIO_DATA"])
    os.environ["CCS_NATIVE_HOME_RUNTIME"] = os.path.abspath(runtime); os.environ["CCS_NATIVE_HOME_ROOT"] = os.path.join(work, "native-home")
    if not os.environ.get("CCS_PLAYER_FONTS"):   # the player's menu fonts, baked from the site's HOME fonts as the app's own build does (companion.spec)
        import subprocess
        fd = os.path.join(work, "player-fonts"); subprocess.run([sys.executable, os.path.join(TOOLS, "player", "bake_fonts.py"), os.path.join(site, "brand", "home"), fd], check=True, stdin=subprocess.DEVNULL)
        os.environ["CCS_PLAYER_FONTS"] = fd
    for p in (os.path.join(TOOLS, "companion"), os.path.join(TOOLS, "rom-pipeline")):
        if p not in sys.path: sys.path.insert(0, p)
    try:
        import personal as ps, library_lookup as LL
        from native_home import console_package as cpk, notices as NT
        bad = NT.audit()   # the records the package's notices are made from (licenses/; each layout is then checked: cpk.verify notices=True)
        if bad: raise SystemExit("the notice records are not complete: " + "; ".join(bad[:5]))
        idx = json.load(open(os.path.join(site, "included", "index.json"), encoding="utf-8"))
        sel, hg, games = [], [], []
        for g in idx["games"]:
            r = ps.ingest(os.path.join(site, "included", g["file_name"]), origin="included"); pid = r["entry"]["id"]
            ps.merge_metadata(pid, {"title": g["title"], "version_label": g["version"], "description": g["text"], "year": g["year"], "publisher": g["creator"]})
            e = LL.personal_index()["entries"][pid]
            sel.append({"game_id": pid, "version_id": pid, "display_title": e["user"]["title"], "source": "personal", "install_required": True})
            hg.append({"id": pid, "title": e["user"]["title"], "version_label": e["user"].get("version_label") or "", "kind": "personal"})
            games.append({"title": g["title"], "version": g["version"], "creator": g["creator"], "license": g["license"], "file_sha256": g["file_sha256"], "source": g.get("source")})
        man = {"schema": "nes-classic-collection/4", "name": "Included Games", "hidden_factory_games": [], "selections": sel}
        home = {"schema": "ccs-home-collection/1", "theme": "featured-shelf", "order": [x["id"] for x in hg], "games": hg, "folders": [], "favorites": {"items": {}},
                "settings": {"reduced_motion": False, "hints": True}, "sample": False}
        os.makedirs(out, exist_ok=True); rel = {"schema": SCHEMA, "collection": "Included Games", "games": games, "layouts": {}}; blobs = {}
        for layout in LAYOUTS:
            d = os.path.join(work, layout)
            r = cpk.build_console(man, home, d, layout=layout, player=os.path.abspath(player), label="Included Games", created_utc=created)
            if not r.get("ok"): raise SystemExit(f"the {layout} package could not be built: {r.get('error')}")
            m = r["manifest"]
            if m["unavailable"] or m["playable"] != len(games): raise SystemExit(f"the {layout} package does not have every Included Game playable: {m['unavailable']}")
            v = cpk.verify(d, notices=True)
            if not v["ok"]: raise SystemExit(f"the {layout} package does not verify: {v['problems'][:3]}")
            mb = open(os.path.join(d, "MANIFEST.json"), "rb").read()
            for f, info in m["files"].items():
                b = open(os.path.join(d, *f.split("/")), "rb").read()
                if sha(b) != info["sha256"] or len(b) != info["size"]: raise SystemExit(f"{layout}/{f} changed while it was copied")
                blobs[info["sha256"]] = b
            open(os.path.join(out, f"{layout}.MANIFEST.json"), "wb").write(mb)
            rel["layouts"][layout] = {"manifest_sha256": sha(mb), "manifest_bytes": len(mb), "files": len(m["files"]), "total_bytes": m["total_bytes"], "games": m["games"], "playable": m["playable"]}
        sm, sb = build_sample(os.path.join(work, "sample"))
        blobs.update(sb); open(os.path.join(out, "sample.json"), "w", encoding="utf-8", newline="\n").write(json.dumps(sm, indent=1, sort_keys=True) + "\n")
        rel["sample"] = {"games": len(sm["games"]), "files": len(sm["files"]), "total_bytes": sum(f["size"] for f in sm["files"].values())}
        for h, b in sorted(blobs.items()): open(os.path.join(out, f"{h}.blob"), "wb").write(b)
        rel["blobs"] = len(blobs); rel["blob_bytes"] = sum(len(b) for b in blobs.values())
        open(os.path.join(out, "HOME.json"), "w", encoding="utf-8", newline="\n").write(json.dumps(rel, indent=1) + "\n")
        return rel
    finally:
        shutil.rmtree(work, ignore_errors=True)

# HOME Designer's sample games: the console's own 30 built-in games, most popular first, with two folders (Mario, Adventures), four
# favorites and Adventure Map's two folder signs, exactly as the theme examples (CcsBackupUi/theme-examples/build_pack.py) were drawn.
SAMPLE_CODES = ["CLV-P-NAACE", "CLV-P-NAANE", "CLV-P-NAAAE", "CLV-P-NAAQE", "CLV-P-NABBE", "CLV-P-NABQE", "CLV-P-NAATE", "CLV-P-NAAPE",
                "CLV-P-NAAEE", "CLV-P-NAADE", "CLV-P-NAASE", "CLV-P-NABJE", "CLV-P-NABME", "CLV-P-NACBE", "CLV-P-NAAVE", "CLV-P-NAAXE",
                "CLV-P-NABNE", "CLV-P-NAAHE", "CLV-P-NACDE", "CLV-P-NABKE", "CLV-P-NABCE", "CLV-P-NACHE", "CLV-P-NABVE", "CLV-P-NABXE",
                "CLV-P-NABRE", "CLV-P-NAAZE", "CLV-P-NAAUE", "CLV-P-NAAWE", "CLV-P-NAARE", "CLV-P-NAAFE"]

def build_sample(work: str) -> tuple[dict, dict]:
    """HOME Designer's preview-only sample HOME (the HOME package builder's own "preview" mode: rendered on a computer, never an
    installation): the games' names, details, box art and game screens the site already carries, never a game file (a HOME package holds
    no ROM data). Workbench draws HOME Designer's preview with it by default and labels it "Sample games"; it is never playable, never a
    collection or a console payload. -> (sample.json, {sha256: bytes})"""
    import library_lookup as LL
    from native_home import package as P
    cat = LL._catalog(); rows = [LL.factory_row(c) for c in SAMPLE_CODES]
    if not all(rows) or len({r["game_id"] for r in rows}) != len(SAMPLE_CODES): raise SystemExit("the sample games are not the console's 30 built-in games")
    ids = [r["game_id"] for r in rows]; sel, hg = [], []
    for gid in ids:   # as the Studio page selects a built-in game (test_native_home_package.sel_for)
        g = cat[gid]; factory = (g.get("builtin_status") or "") == "BUILT-IN: EXACT MATCH"
        sel.append({"game_id": gid, "version_id": gid, "title_id": LL.title_id_for(gid), "display_title": g["display_title"], "nointro_name": g.get("nointro_name"),
                    "source": "catalog", "builtin_code": g.get("builtin_code"), "install_required": not factory})
        hg.append({"id": gid, "title": g["display_title"], "version_label": "factory version" if factory else g.get("nointro_name"), "kind": "factory" if factory else "added"})
    by = dict(zip(SAMPLE_CODES, ids))
    folders = [{"id": "f:mario", "name": "Mario", "parent": None, "order": [by[c] for c in ("CLV-P-NAACE", "CLV-P-NAAAE", "CLV-P-NAADE", "CLV-P-NAAXE", "CLV-P-NAAWE")]},
               {"id": "f:adventure", "name": "Adventures", "parent": None, "order": [by[c] for c in ("CLV-P-NAANE", "CLV-P-NAASE", "CLV-P-NAAQE", "CLV-P-NAAVE", "CLV-P-NABJE", "CLV-P-NAAZE")]}]
    man = {"schema": "nes-classic-collection/4", "name": "Sample games", "selections": sel, "hidden_factory_games": []}
    home = {"schema": "ccs-home-collection/1", "theme": "featured-shelf", "order": ids, "games": hg, "folders": folders, "favorites": {"items": {i: {"on": True} for i in ids[:4]}},
            "settings": {"reduced_motion": False, "hints": True}, "sample": False, "map_shortcuts": ["f:mario", "f:adventure"]}
    r = P.build(man, home, mode="preview", root=work, progress=lambda *a: None)
    if not r.get("ok") or not r.get("package_dir"): raise SystemExit(f"the sample HOME could not be built: {r.get('error')}")
    d = r["package_dir"]; files, data = {}, {}
    for base, _, names in os.walk(d):
        for n in names:
            if n == P.OWNED: continue
            p = os.path.join(base, n); rel = os.path.relpath(p, d).replace(os.sep, "/"); b = open(p, "rb").read(); h = sha(b)
            files[rel] = {"sha256": h, "size": len(b)}; data[h] = b
    if "pack.json" not in files or "games.json" not in files: raise SystemExit("the sample HOME has no pack.json or games.json")
    return {"schema": "ccs-home-preview-sample/1", "label": "Sample games",
            "note": "HOME Designer's preview only: the NES Classic's 30 built-in games' names, details, box art and game screens, never a game file; never playable, never a collection, never sent to a console",
            "games": [g["title"] for g in hg], "files": files}, data

def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--site", required=True); ap.add_argument("--player", required=True); ap.add_argument("--runtime", required=True); ap.add_argument("--out", required=True)
    a = ap.parse_args(); r = build(a.site, a.player, a.runtime, a.out)
    for k, v in r["layouts"].items(): print(f"{k}: {v['files']} files, {v['total_bytes']} bytes, MANIFEST.json {v['manifest_sha256']}")
    print(f"{r['blobs']} distinct files, {r['blob_bytes']} bytes")
    return 0

if __name__ == "__main__": sys.exit(main())
