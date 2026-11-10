r"""Classic Cart Studio console package (D-113): everything the NES Classic needs to show the configured HOME and play its games, built from
the same configuration as the native HOME package (package.py, deployment mode): never a sample catalog, never a game found by its name.

  python -m native_home.console_package build --collection collection.json --home home.json --out DIR [--layout ram-test|install]

The package (one folder, every file listed with its size and SHA-256 in MANIFEST.json and SHA256SUMS):
  home/          the native HOME package of this configuration: games in the HOME's order, covers, texts, the four themes, fonts, the HOME
                 song, the HOME runtime (bin/ccs-home-armv7, whose read-only tools the console scripts use)
  bin/           ccs-player-armv7: the game player for the console (the same session engine and pinned FCEUmm as Windows and the browser)
  fonts/         the player's menu fonts
  roms/          each game's exact ROM file, named by its content SHA-256 (the bytes after any iNES header): a game whose file is missing or
                 is another version is in the package as unavailable (the HOME says so; nothing is substituted)
  startup/       the five startup images and startup.json: Random (the default) or one fixed image, independent of the HOME theme
  config/        player-settings.json (the picture: TV proportions), controller-map.txt (the console controllers' codes)
  session.json   the player's session for this package's place on the console (layout): where it reads and where it writes
Layouts: ram-test (K2: the package is inside the RAM image, everything written goes to /tmp/ccs, nothing persistent) and install (K3,
prepared only: the package in its own folder under /var/lib/ccs/pkg, which the start-up links in RAM as /tmp/ccs/pkg at each start (the paths in
session.json), saves and settings under /var/lib/ccs/data; the console's own games, saves and Nintendo's
HOME keep their places). Saves stay on the console: the package never carries saves or Game Settings from this computer.
Copyright (C) 2026 Rance9, GPL-3.0-or-later."""
from __future__ import annotations
import argparse, hashlib, json, os, re, shutil, sys, tempfile, time
HERE = os.path.dirname(os.path.abspath(__file__)); TOOLS = os.path.normpath(os.path.join(HERE, "..", ".."))
for _p in (os.path.dirname(HERE), os.path.join(TOOLS, "rom-pipeline")):
    if _p not in sys.path: sys.path.insert(0, _p)
from common import FROZEN, BUNDLE, site_path, WORK   # noqa: E402

SCHEMA = "ccs-console-package/1"; STARTUP_SCHEMA = "ccs-startup/1"
STARTUP_IDS = ("forgotten-keep", "galactic-frontier", "kingdoms-beyond", "neon-highway", "pixel-worlds")
MIN_VISIBLE_MS = 5000
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
    v = (home or {}).get("startup_image") if isinstance(home, dict) else None
    return v if isinstance(v, str) and v in STARTUP_IDS else "random"

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
                  pack_root: str | None = None, label: str | None = None, pack_dir: str | None = None) -> dict:
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
    pack = res["package_dir"]; L = LAYOUTS[layout]; os.makedirs(out); st = Stage(out)
    try:
        st.copy_tree(pack, "home", "home")
        st.put("bin/ccs-player-armv7", pb, "executable", 0o755)
        fd = play.fonts_dir()
        for f in sorted(os.listdir(fd)):
            if f.endswith(".ccsf") and "-2x" not in f or f.startswith("OFL-"): st.put("fonts/" + f, open(os.path.join(fd, f), "rb").read(), "font")   # the console draws at 1x
        sman, sfiles = startup_files(); choice = startup_choice(home)
        for n, b in sfiles.items(): st.put("startup/" + n, b, "startup-image")
        scfg = {"schema": STARTUP_SCHEMA, "choice": choice, "min_visible_ms": MIN_VISIBLE_MS,
                "images": [{"id": i["id"], "name": i["name"], "file": i["file"], "sha256": i["sha256"]} for i in sman["images"]]}
        st.put("startup/startup.json", (json.dumps(scfg, indent=1) + "\n").encode(), "configuration")
        st.put("config/player-settings.json", b'{"tv_aspect": true, "integer_scale": false}\n', "configuration")
        st.put("config/controller-map.txt", CONTROLLER_MAP.encode(), "configuration")
        pg = json.load(open(os.path.join(pack, "games.json"), encoding="utf-8")).get("games") or []
        ctx: dict = {}; games = []; unavailable = []; resolver = rom_resolver or default_rom_resolver
        for e in pg:
            vid = str(e.get("id") or ""); title = str(e.get("title") or vid)[:200]
            r = resolver(vid, title, ctx); g = {"id": vid, "title": title, "version_label": str(e.get("version_label") or "")[:200], "rom": "", "header_hex": "", "content_sha256": "", "unavailable": ""}
            if r.get("playable"):
                b = open(r["rom"], "rb").read(); cs = content_sha256(b)
                if cs != r.get("content_sha256"): r = {"playable": False, "code": "mismatched", "reason": "The ROM file changed since it was matched to this version, so it is not used (nothing is substituted)."}
                else:
                    rel = f"roms/{cs}.nes"
                    if rel not in st.files: st.put(rel, b, "rom")
                    g.update(rom=f"{L['pkg']}/{rel}", header_hex=r.get("header_hex") or "", content_sha256=cs)
            if not r.get("playable"): g["unavailable"] = str(r.get("reason") or "The ROM file of this game is not in this package."); unavailable.append({"id": vid, "title": title, "code": r.get("code")})
            games.append(g)
        if not games: raise ValueError("this HOME has no games")
        mus = (json.load(open(os.path.join(pack, "pack.json"), encoding="utf-8")).get("music") or {}).get("file")
        sess = {"schema": "ccs-play-session/1", "mode": "home", "saves_root": f"{L['data']}/saves", "fonts_dir": f"{L['pkg']}/fonts", "scratch_dir": L["scratch"],
                "result_path": f"{L['logs']}/result.json", "settings_path": f"{L['pkg']}/config/player-settings.json", "display_path": f"{L['data']}/state/player-display.json",
                "game_settings_dir": f"{L['data']}/game-settings", "studio_pid": 0, "games": games, "direct_id": "",
                "home": {"pack": f"{L['pkg']}/home", "state_dir": f"{L['data']}/state", "music": {"asset": f"{L['pkg']}/home/{mus}" if mus else "", "pref": f"{L['data']}/state/home-music.json"},
                         "leave": L["leave"]}}
        st.put("session.json", (json.dumps(sess, indent=1, ensure_ascii=False) + "\n").encode("utf-8"), "configuration")
        man = {"schema": SCHEMA, "label": label or (manifest.get("name") or "HOME"), "layout": layout, "console_paths": {k: L[k] for k in ("pkg", "data", "scratch", "logs")},
               "created_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "games": len(games), "playable": len(games) - len(unavailable), "unavailable": unavailable,
               "startup": {"choice": choice, "min_visible_ms": MIN_VISIBLE_MS}, "home_package": {"fingerprint": (res.get("manifest") or {}).get("configuration", {}).get("fingerprint"), "files": res.get("files")},
               "player": {"file": "bin/ccs-player-armv7", "sha256": _sha(pb), "size": len(pb), "core": "fceumm ac0d00a7"},
               "sizes": {k: sum(f["size"] for f in st.files.values() if f["kind"] == k) for k in sorted({f["kind"] for f in st.files.values()})},
               "total_bytes": sum(f["size"] for f in st.files.values()), "files": dict(sorted(st.files.items()))}
        mb = (json.dumps(man, indent=1, ensure_ascii=False) + "\n").encode("utf-8")
        with open(os.path.join(out, "MANIFEST.json"), "wb") as f: f.write(mb)
        with open(os.path.join(out, "SHA256SUMS"), "w", newline="\n") as f:
            for rel, info in sorted(st.files.items()): f.write(f"{info['sha256']}  {rel}\n")
            f.write(f"{_sha(mb)}  MANIFEST.json\n")
        return {"ok": True, "out": out, "manifest": man}
    except Exception as e:
        shutil.rmtree(out, ignore_errors=True); return {"ok": False, "error": str(e)}
    finally:
        if tmp and not pack_root: shutil.rmtree(tmp, ignore_errors=True)

def verify(out: str) -> dict:
    """every file listed is there with its size and SHA-256, and nothing else is"""
    man = json.load(open(os.path.join(out, "MANIFEST.json"), encoding="utf-8")); bad = []; seen = set()
    for rel, info in man["files"].items():
        p = os.path.join(out, *rel.split("/")); seen.add(rel)
        if not os.path.isfile(p): bad.append(f"missing {rel}"); continue
        b = open(p, "rb").read()
        if len(b) != info["size"] or _sha(b) != info["sha256"]: bad.append(f"changed {rel}")
    for r, _, fs in os.walk(out):
        for f in fs:
            rel = os.path.relpath(os.path.join(r, f), out).replace("\\", "/")
            if rel not in seen and rel not in ("MANIFEST.json", "SHA256SUMS"): bad.append(f"extra {rel}")
    return {"ok": not bad, "problems": bad, "files": len(man["files"])}

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter); ap.add_argument("cmd", choices=["build", "verify"])
    ap.add_argument("--collection"); ap.add_argument("--home"); ap.add_argument("--out", required=True); ap.add_argument("--layout", default="ram-test"); a = ap.parse_args()
    if a.cmd == "verify": r = verify(a.out); print(json.dumps(r, indent=1)); sys.exit(0 if r["ok"] else 1)
    if not (a.collection and a.home): sys.exit("build needs --collection and --home")
    r = build_console(json.load(open(a.collection, encoding="utf-8")), json.load(open(a.home, encoding="utf-8")), a.out, layout=a.layout)
    print(json.dumps({k: (v if k != "manifest" else {x: v[x] for x in ("games", "playable", "unavailable", "startup", "sizes", "total_bytes")}) for k, v in r.items()}, indent=1, ensure_ascii=False)); sys.exit(0 if r["ok"] else 1)

if __name__ == "__main__": main()
