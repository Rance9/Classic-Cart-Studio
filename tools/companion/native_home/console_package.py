r"""Classic Cart Studio console package: everything the NES Classic needs to show the configured HOME and play its games, built from
the same configuration as the native HOME package (package.py, deployment mode): never a sample catalog, never a game found by its name.

  python -m native_home.console_package build --collection collection.json --home home.json --out DIR [--layout ram-test|install]

The package (one folder, every file listed with its size and SHA-256 in MANIFEST.json and SHA256SUMS):
  home/          the native HOME package of this configuration: games in the HOME's order, covers, texts, the four themes, fonts, the HOME
                 song, the HOME runtime (bin/ccs-home-armv7, whose read-only tools the console scripts use)
  bin/           ccs-player-armv7: the game player for the console (the same session engine and pinned FCEUmm as Windows and the browser)
  fonts/         the player's menu fonts
  roms/          each game's exact ROM file, named by its content SHA-256 (the bytes after any iNES header): a game whose file is missing or
                 is another version is in the package as unavailable (the HOME says so; nothing is substituted)
  startup/       the five startup images and startup.json: Random (the default) or one fixed image, independent of the HOME theme; when the
                 collection's own picture is the choice, it too (startup-custom-<16 hex>.jpg, listed first)
  config/        player-settings.json (the picture: TV proportions), controller-map.txt (the console controllers' codes)
  session.json   the player's session for this package's place on the console (layout): where it reads and where it writes
  licenses/      NOTICE.txt (each part of the package, its terms and its exact source) and the license texts those parts require, made
                 from Classic Cart Studio's records by notices.py; a file no part covers stops the build
  native-covers/ this computer's only, with PC.json (its list, named in MANIFEST.json by pc_sha256; neither is sent to a console): each
                 game's cover as CCS HOME carried it before console covers became smaller, from which Original Menu's covers are made
                 (PC.json native_covers: game id -> file), so those stay exactly as they were
Layouts: ram-test (K2: the package is inside the RAM image, everything written goes to /tmp/ccs, nothing persistent) and install (K3,
prepared only: the package in its own folder under /var/lib/ccs/pkg, which the start-up links in RAM as /tmp/ccs/pkg at each start (the paths in
session.json), saves and settings under /var/lib/ccs/data; the console's own games, saves and Nintendo's
HOME keep their places). mod: the install layout's places for CCS HOME, which CCS Mod provides (tools/console-nucleus): the one CCS HOME
layout, whatever else the console has. Its leave item, Choose Another Menu, is decided on the console when it is chosen: CCS Nucleus's
startup chooser when that is installed, otherwise Original Menu (Nintendo's own menu). Saves stay on the console: the package never carries
saves or Game Settings from this computer.
Copyright (C) 2026 Rance9, GPL-3.0-or-later."""
from __future__ import annotations
import argparse, hashlib, io, json, os, re, shutil, sys, tempfile, time
HERE = os.path.dirname(os.path.abspath(__file__)); TOOLS = os.path.normpath(os.path.join(HERE, "..", ".."))
for _p in (os.path.dirname(HERE), os.path.join(TOOLS, "rom-pipeline")):
    if _p not in sys.path: sys.path.insert(0, _p)
from common import FROZEN, BUNDLE, site_path, WORK   # noqa: E402
import startup_picture   # noqa: E402

SCHEMA = "ccs-console-package/1"; STARTUP_SCHEMA = "ccs-startup/1"
STARTUP_IDS = ("forgotten-keep", "galactic-frontier", "kingdoms-beyond", "neon-highway", "pixel-worlds")
MIN_VISIBLE_MS = 5000
PC_FILE = "PC.json"; PC_DIR = "native-covers"; PC_SCHEMA = "ccs-console-package-pc/1"   # this computer's only (what Original Menu's covers are made from); never sent
CUSTOM_RE = re.compile(r"^custom-[0-9a-f]{16}$")   # the collection's own picture (its checks: tools/companion/startup_picture.py)
CONTROLLER_MAP = """# The NES Classic's controllers (clovercon), as Linux input codes. Written by Classic Cart Studio; a RAM test confirms them on the console.
device Nintendo Clovercon - controller1
device Nintendo Clovercon - controller2
key 304 a
key 305 b
key 314 select
key 315 start
key 704 left
key 705 right
key 706 up
key 707 down
"""
LAYOUTS = {
    "ram-test": {"pkg": "/hakchi/ccs/pkg", "data": "/tmp/ccs", "scratch": "/tmp/ccs/scratch", "logs": "/tmp/ccs/logs",
                 "leave": {"label": "End the Test", "sub": "A report to photograph, then the console restarts into Nintendo's HOME", "title": "End the test?",
                           "text": "A report of this test appears first: photograph it, then press A. After that, any button restarts the console into Nintendo's original HOME. What was saved during this test is not kept: it was in memory only."}},
    "install": {"pkg": "/tmp/ccs/pkg", "data": "/var/lib/ccs/data", "scratch": "/tmp/ccs/scratch", "logs": "/tmp/ccs/logs",
                "leave": {"label": "Switch to Nintendo HOME", "sub": "Its games and saves are kept; this HOME returns at the next start", "title": "Switch to Nintendo's HOME?",
                          "text": "Nintendo's original HOME opens with its own games and saves. Classic Cart Studio's HOME comes back the next time the console is turned on."}},
    # CCS HOME on a console with CCS Mod (tools/console-nucleus): the same places as "install". Leaving it is decided on the console when it is
    # chosen (ccs-mod launch): CCS Nucleus's startup chooser when that is installed, otherwise Original Menu
    "mod": {"pkg": "/tmp/ccs/pkg", "data": "/var/lib/ccs/data", "scratch": "/tmp/ccs/scratch", "logs": "/tmp/ccs/logs",
            "leave": {"label": "Choose Another Menu", "sub": "", "title": "Choose another menu?",
                      "text": "If CCS Nucleus's startup chooser is installed, it opens so you can choose another menu. Otherwise Original Menu (Nintendo's own menu) opens with its own games and saves. Your games and saves here are kept."}},
}

def _sha(b: bytes) -> str: return hashlib.sha256(b).hexdigest()
def content_sha256(b: bytes) -> str:
    """the game identity: SHA-256 of the bytes after an iNES header and trainer (what gp_rom_load verifies)"""
    skip = (16 + (512 if len(b) > 6 and b[6] & 4 else 0)) if b[:4] == b"NES\x1a" else 0
    return _sha(b[skip:])

def player_binary() -> str | None:
    env = os.environ.get("CCS_CONSOLE_PLAYER")
    if env and os.path.isfile(env): return env
    if FROZEN:
        import bundle_integrity as bi
        try: return next(p for p in bi.require("native-runtime") if p.endswith("ccs-player-armv7"))
        except (bi.ComponentError, StopIteration): return None
    p = os.path.join(os.environ.get("CCS_SIM_BUILD_WIN") or os.path.join(WORK, "console-simulator", "build"), "arm-musl-device", "ccs-player-armv7")
    return p if os.path.isfile(p) else None

def startup_root() -> str:
    for p in (os.environ.get("CCS_STARTUP_ASSETS"), os.path.join(BUNDLE, "startup") if FROZEN else os.path.join(TOOLS, "publish", "assets", "startup")):
        if p and os.path.isfile(os.path.join(p, "startup-images.json")): return p
    raise FileNotFoundError("the startup images (startup-images.json) were not found")

def startup_choice(home: dict | None) -> str:
    """Random, one of the five, or the collection's own picture (custom-<16 hex>; startup_custom checks it); anything else is Random"""
    v = (home or {}).get("startup_image") if isinstance(home, dict) else None
    return v if isinstance(v, str) and (v in STARTUP_IDS or CUSTOM_RE.match(v)) else "random"

def startup_custom(home: dict | None) -> tuple[dict | None, bytes | None, str | None]:
    """the collection's own startup picture when it is the choice: (its startup.json entry, the bytes to write, None), (None, None, None)
    when another startup screen is chosen, or (None, None, reason) when it cannot be used"""
    choice = startup_choice(home)
    if not CUSTOM_RE.match(choice): return None, None, None
    c = (home or {}).get("startup_custom")
    if not isinstance(c, dict) or c.get("id") != choice: return None, None, "the collection does not describe the picture"
    raw, why = startup_picture.check(c)   # the same rules as every backup the app writes (tools/companion/startup_picture.py)
    if why: return None, None, why + (" (choose it again in HOME Settings)" if "not with the collection" in why else "")
    sha = c["sha256"]
    try:
        from PIL import Image
        px = Image.open(io.BytesIO(raw)).convert("RGB"); out = io.BytesIO()
        Image.frombytes("RGB", px.size, px.tobytes()).save(out, "JPEG", quality=92, optimize=True)   # a new image from the pixels only: no EXIF, ICC profile or comment carries over
    except Exception as e: return None, None, f"the picture could not be read ({type(e).__name__})"
    b = out.getvalue(); name = re.sub(r"[\x00-\x1f]", " ", str(c.get("name") or "Your picture")).strip()[:80] or "Your picture"
    return {"id": choice, "name": name, "file": f"startup-{choice}.jpg", "sha256": _sha(b), "source_sha256": sha}, b, None

def startup_files() -> tuple[dict, dict[str, bytes]]:
    """the five derivatives, checked against their manifest (size and SHA-256); one that does not match stops the build"""
    root = startup_root(); man = json.load(open(os.path.join(root, "startup-images.json"), encoding="utf-8"))
    if man.get("schema") != "ccs-startup-images/1" or [i.get("id") for i in man.get("images") or []] != list(STARTUP_IDS): raise ValueError("the startup images manifest is not the five approved images")
    files = {}
    for i in man["images"]:
        b = open(os.path.join(root, i["file"]), "rb").read()
        if len(b) != i["bytes"] or _sha(b) != i["sha256"]: raise ValueError(f"{i['file']} is not the image its manifest lists")
        files[i["file"]] = b
    return man, files

class Stage:
    def __init__(self, d: str): self.d = d; self.files: dict[str, dict] = {}
    def put(self, rel: str, data: bytes, kind: str, mode: int = 0o644):
        p = os.path.join(self.d, *rel.split("/")); os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "wb") as f: f.write(data)
        self.files[rel] = {"size": len(data), "sha256": _sha(data), "kind": kind, "mode": oct(mode)}
    def copy_tree(self, src: str, prefix: str, kind: str):
        for r, ds, fs in os.walk(src):
            ds.sort()
            for f in sorted(fs):
                if f.startswith(".ccs-"): continue   # the packager's own marker stays on the PC
                full = os.path.join(r, f); rel = prefix + "/" + os.path.relpath(full, src).replace("\\", "/")
                self.put(rel, open(full, "rb").read(), kind, 0o755 if rel.endswith("/bin/ccs-home-armv7") else 0o644)

def default_rom_resolver(vid: str, title: str, ctx: dict) -> dict:
    import play
    return play.rom_for(vid, title, ctx)

def build_console(manifest: dict, home: dict, out: str, *, layout: str = "ram-test", rom_resolver=None, player: str | None = None,
                  pack_root: str | None = None, label: str | None = None, pack_dir: str | None = None, created_utc: str | None = None) -> dict:
    """build the package into out (a new folder; refused when it exists). rom_resolver(id, title, ctx) -> {playable, rom, header_hex,
    content_sha256, reason, code} (default: the Studio's own, play.rom_for). pack_dir: a native HOME package of this same configuration
    built a moment ago (the Studio's Prepare HOME Menu), used as it is instead of building it again"""
    if layout not in LAYOUTS: return {"ok": False, "error": f"unknown layout {layout!r}"}
    if os.path.exists(out): return {"ok": False, "error": f"{out} exists; a package is always built into a new folder"}
    player = player or player_binary()
    if not player: return {"ok": False, "error": "the console player (ccs-player-armv7) is not available in this installation"}
    pb = open(player, "rb").read()
    if pb[:4] != b"\x7fELF" or pb[4] != 1 or int.from_bytes(pb[18:20], "little") != 40: return {"ok": False, "error": "the console player is not a 32-bit ARM executable"}
    from native_home import package as P
    import play
    tmp = pack_root or (None if pack_dir else tempfile.mkdtemp(prefix="ccs-console-pack-"))
    if pack_dir:
        pm = json.load(open(os.path.join(pack_dir, "manifest.json"), encoding="utf-8")); res = {"ok": True, "package_dir": pack_dir, "manifest": pm, "files": len(pm.get("files") or {})}
    else: res = P.build(manifest, home, mode="deployment", root=tmp)
    if not res.get("ok"): return {"ok": False, "error": res.get("error") or "; ".join(res.get("problems") or []) or "the HOME package could not be built"}
    mb = [b for b in (res.get("manifest") or {}).get("blockers") or [] if b.get("code") == "music-mp3-missing"]
    if mb: return {"ok": False, "error": mb[0]["text"] + ": the console package is not made without it"}   # never a package that silently lacks its song
    pack = res["package_dir"]; L = LAYOUTS[layout]; os.makedirs(out); st = Stage(out)
    try:
        st.copy_tree(pack, "home", "home")
        st.put("bin/ccs-player-armv7", pb, "executable", 0o755)
        fd = play.fonts_dir()
        for f in sorted(os.listdir(fd)):
            if f.endswith(".ccsf") and "-2x" not in f or f.startswith("OFL-"): st.put("fonts/" + f, open(os.path.join(fd, f), "rb").read(), "font")   # the console draws at 1x
        sman, sfiles = startup_files(); choice = startup_choice(home)
        custom, cbytes, cwhy = startup_custom(home)
        if cwhy: raise ValueError(f"The startup picture chosen for this collection cannot be used: {cwhy}. Choose it again in HOME Settings, or choose another startup screen.")
        for n, b in sfiles.items(): st.put("startup/" + n, b, "startup-image")
        if custom: st.put("startup/" + custom["file"], cbytes, "startup-image")
        scfg = {"schema": STARTUP_SCHEMA, "choice": choice, "min_visible_ms": MIN_VISIBLE_MS,
                "images": ([{k: custom[k] for k in ("id", "name", "file", "sha256")}] if custom else []) + [{"id": i["id"], "name": i["name"], "file": i["file"], "sha256": i["sha256"]} for i in sman["images"]]}
        st.put("startup/startup.json", (json.dumps(scfg, indent=1) + "\n").encode(), "configuration")
        st.put("config/player-settings.json", b'{"tv_aspect": true, "integer_scale": false}\n', "configuration")
        st.put("config/controller-map.txt", CONTROLLER_MAP.encode(), "configuration")
        pg = json.load(open(os.path.join(pack, "games.json"), encoding="utf-8")).get("games") or []
        ctx: dict = {}; games = []; unavailable = []; resolver = rom_resolver or default_rom_resolver
        pc, native, copies = {}, {}, {}   # this computer's files (PC.json): what Original Menu's covers are made from, and Hakchi-ready copies too large for one message
        from console_engine import hakchi_publish as HPUB
        for e in pg:
            vid = str(e.get("id") or ""); title = str(e.get("title") or vid)[:200]
            r = resolver(vid, title, ctx); g = {"id": vid, "title": title, "version_label": str(e.get("version_label") or "")[:200], "rom": "", "header_hex": "", "content_sha256": "", "unavailable": ""}
            if r.get("playable"):
                b = open(r["rom"], "rb").read(); cs = content_sha256(b)
                if cs != r.get("content_sha256"): r = {"playable": False, "code": "mismatched", "reason": "The game file changed since it was matched to this version, so it is not used (nothing is substituted)."}
                else:
                    rel = f"roms/{cs}.nes"
                    if rel not in st.files: st.put(rel, b, "rom")
                    g.update(rom=f"{L['pkg']}/{rel}", header_hex=r.get("header_hex") or "", content_sha256=cs)
                    # the file in the form Hakchi stores it (hakchi_publish.nes_rom), when it differs and is too large to send within one message:
                    # kept here, named by its SHA-256, so the Hakchi entry's copy is sent from it (never changed: the package's own game file stays as it is)
                    try: hr = HPUB.nes_rom(b, g["header_hex"])
                    except ValueError: hr = {"ok": False}
                    if hr.get("ok") and not hr["same"] and len(hr["ready"]) > HPUB.INLINE_MAX:
                        hs = _sha(hr["ready"]); crel = f"{HPUB.COPY_DIR}/{hs[:16]}.nes"
                        if crel not in pc:
                            os.makedirs(os.path.join(out, HPUB.COPY_DIR), exist_ok=True)
                            with open(os.path.join(out, *crel.split("/")), "wb") as f: f.write(hr["ready"])
                            pc[crel] = {"size": len(hr["ready"]), "sha256": hs}
                        copies[hs] = crel
            if not r.get("playable"): g["unavailable"] = str(r.get("reason") or "The game file for this game is not in this package."); unavailable.append({"id": vid, "title": title, "code": r.get("code")})
            games.append(g)
        if not games: raise ValueError("this HOME has no games")
        mpk = json.load(open(os.path.join(pack, "pack.json"), encoding="utf-8")).get("music") or {}; mus = mpk.get("file")
        from native_home import notices as NTX
        if mpk.get("format") == "mp3" and NTX.MP3_MARK not in pb: raise ValueError("this HOME's music is MP3, which only the game player built with it plays")
        sess = {"schema": "ccs-play-session/1", "mode": "home", "saves_root": f"{L['data']}/saves", "fonts_dir": f"{L['pkg']}/fonts", "scratch_dir": L["scratch"],
                "result_path": f"{L['logs']}/result.json", "settings_path": f"{L['pkg']}/config/player-settings.json", "display_path": f"{L['data']}/state/player-display.json",
                "game_settings_dir": f"{L['data']}/game-settings", "studio_pid": 0, "games": games, "direct_id": "",
                "home": {"pack": f"{L['pkg']}/home", "state_dir": f"{L['data']}/state", "design_root": f"{L['data']}/home-design", "music": {"asset": f"{L['pkg']}/home/{mus}" if mus else "", "pref": f"{L['data']}/state/home-music.json"},
                         "leave": L["leave"]}}
        st.put("session.json", (json.dumps(sess, indent=1, ensure_ascii=False) + "\n").encode("utf-8"), "configuration")
        # Original Menu's covers are made from these (hakchi_publish.spec): each game's cover as CCS HOME carried it before console covers
        # became smaller, from the same source, so those covers stay exactly as they were. This computer's only: PC.json lists them (the
        # manifest names PC.json by its SHA-256) and neither is ever sent to a console
        srcs = P.native_cover_sources(manifest, home, os.path.join(os.path.dirname(pack), "cache"))
        for e in pg:
            b = srcs.get(str(e.get("id") or ""))
            if not b: continue
            rel = f"{PC_DIR}/{_sha(b)[:16]}.jpg"; native[str(e["id"])] = rel
            if rel not in pc:
                os.makedirs(os.path.join(out, PC_DIR), exist_ok=True)
                with open(os.path.join(out, *rel.split("/")), "wb") as f: f.write(b)
                pc[rel] = {"size": len(b), "sha256": _sha(b)}
        pcb = (json.dumps({"schema": PC_SCHEMA, "native_covers": dict(sorted(native.items())), **({"hakchi_copies": dict(sorted(copies.items()))} if copies else {}), "files": dict(sorted(pc.items()))}, indent=1) + "\n").encode("utf-8")
        with open(os.path.join(out, PC_FILE), "wb") as f: f.write(pcb)
        from native_home import notices as NT
        NT.write(st, pg, label or (manifest.get("name") or "HOME"))   # the package's notices (licenses/), checked before the package is listed
        man = {"schema": SCHEMA, "label": label or (manifest.get("name") or "HOME"), "layout": layout, "console_paths": {k: L[k] for k in ("pkg", "data", "scratch", "logs")},
               "created_utc": created_utc or time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "games": len(games), "playable": len(games) - len(unavailable), "unavailable": unavailable,
               "startup": {"choice": choice, "min_visible_ms": MIN_VISIBLE_MS, **({"custom": {"name": custom["name"], "sha256": custom["sha256"], "source_sha256": custom["source_sha256"]}} if custom else {})}, "home_package": {"fingerprint": (res.get("manifest") or {}).get("configuration", {}).get("fingerprint"), "files": res.get("files")},
               "player": {"file": "bin/ccs-player-armv7", "sha256": _sha(pb), "size": len(pb), "core": "fceumm ac0d00a7", "core_build": "ac0d00a7+patches.6dcff395"},
               "sizes": {k: sum(f["size"] for f in st.files.values() if f["kind"] == k) for k in sorted({f["kind"] for f in st.files.values()})},
               "total_bytes": sum(f["size"] for f in st.files.values()), "files": dict(sorted(st.files.items())), "pc_sha256": _sha(pcb)}
        mb = (json.dumps(man, indent=1, ensure_ascii=False) + "\n").encode("utf-8")
        with open(os.path.join(out, "MANIFEST.json"), "wb") as f: f.write(mb)
        with open(os.path.join(out, "SHA256SUMS"), "w", newline="\n") as f:
            for rel, info in sorted({**st.files, **pc}.items()): f.write(f"{info['sha256']}  {rel}\n")
            f.write(f"{_sha(pcb)}  {PC_FILE}\n")
            f.write(f"{_sha(mb)}  MANIFEST.json\n")
        return {"ok": True, "out": out, "manifest": man}
    except Exception as e:
        shutil.rmtree(out, ignore_errors=True); return {"ok": False, "error": str(e)}
    finally:
        if tmp and not pack_root: shutil.rmtree(tmp, ignore_errors=True)

def verify(out: str, notices: bool = False) -> dict:
    """every file listed is there with its size and SHA-256, and nothing else is; notices: also every part's notice and license
    texts (notices.py; the release build and the app's staging ask for it)"""
    man = json.load(open(os.path.join(out, "MANIFEST.json"), encoding="utf-8")); bad = []; seen = set(); pcf = {}
    if man.get("pc_sha256"):   # this computer's files: PC.json is the one the manifest names, and every file it lists is there
        try: pcb = open(os.path.join(out, PC_FILE), "rb").read()
        except OSError: pcb = b""
        if _sha(pcb) != man["pc_sha256"]: bad.append(f"changed {PC_FILE}")
        else:
            doc = json.loads(pcb); pcf = doc.get("files") or {}; seen.add(PC_FILE)
            bad += [f"native cover source not listed {r}" for r in (doc.get("native_covers") or {}).values() if r not in pcf]
    for rel, info in list(man["files"].items()) + list(pcf.items()):
        p = os.path.join(out, *rel.split("/")); seen.add(rel)
        if not os.path.isfile(p): bad.append(f"missing {rel}"); continue
        b = open(p, "rb").read()
        if len(b) != info["size"] or _sha(b) != info["sha256"]: bad.append(f"changed {rel}")
    for r, _, fs in os.walk(out):
        for f in fs:
            rel = os.path.relpath(os.path.join(r, f), out).replace("\\", "/")
            if rel not in seen and rel not in ("MANIFEST.json", "SHA256SUMS"): bad.append(f"extra {rel}")
    if notices and not bad:
        from native_home import notices as NT
        bad += NT.check_dir(out)
    return {"ok": not bad, "problems": bad, "files": len(man["files"])}

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter); ap.add_argument("cmd", choices=["build", "verify"])
    ap.add_argument("--collection"); ap.add_argument("--home"); ap.add_argument("--out", required=True); ap.add_argument("--layout", default="ram-test"); a = ap.parse_args()
    if a.cmd == "verify": r = verify(a.out); print(json.dumps(r, indent=1)); sys.exit(0 if r["ok"] else 1)
    if not (a.collection and a.home): sys.exit("build needs --collection and --home")
    r = build_console(json.load(open(a.collection, encoding="utf-8")), json.load(open(a.home, encoding="utf-8")), a.out, layout=a.layout)
    print(json.dumps({k: (v if k != "manifest" else {x: v[x] for x in ("games", "playable", "unavailable", "startup", "sizes", "total_bytes")}) for k, v in r.items()}, indent=1, ensure_ascii=False)); sys.exit(0 if r["ok"] else 1)

if __name__ == "__main__": main()
