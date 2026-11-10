"""Build the public static site inside the PUBLIC repository (CI and local checkouts): original sprites + the page generated from the
public data in site-data/. No private workspace is needed.   python build_site_ci.py <out_dir>"""
from __future__ import annotations
import os, sys, json, shutil, subprocess, stat
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.normpath(os.path.join(HERE, "..", ".."))

def _writable_retry(func, path, exc):
    """rmtree helper: a read-only entry (Windows keeps the read-only flag on copied folders) is made writable and removed"""
    os.chmod(path, stat.S_IWRITE); func(path)

def main(out: str):
    out = os.path.abspath(out)
    # the previous output may hold read-only folders copied from site-data; without the helper the second local build fails
    if os.path.isdir(out): shutil.rmtree(out, **({"onexc": _writable_retry} if sys.version_info >= (3, 12) else {"onerror": _writable_retry}))
    os.makedirs(out)
    sd = os.path.join(REPO, "site-data")
    shutil.copytree(os.path.join(sd, "brand", "ccs"), os.path.join(out, "brand", "ccs"), dirs_exist_ok=True)   # the CCS cast (tools/publish/make_ccs_cast.py)
    shutil.copytree(os.path.join(sd, "art"), os.path.join(out, "art"), dirs_exist_ok=True)
    os.makedirs(os.path.join(out, "brand", "public"), exist_ok=True); shutil.copy2(os.path.join(REPO, "tools", "publish", "assets", "app.ico"), os.path.join(out, "brand", "public", "app.ico"))   # companion icon at the path the installer expects
    for n in ("catalog.json", "builtin.json", "library.json", "rankings.json", "descriptions.json", "descriptions-library.json"):   # descriptions-library.json: the extended games' overview and official texts (customize.js, native HOME)
        if os.path.exists(os.path.join(sd, n)): shutil.copy2(os.path.join(sd, n), out)
    shutil.copytree(os.path.join(sd, "brand", "fonts"), os.path.join(out, "brand", "fonts"), dirs_exist_ok=True)
    # the console HOME (Preview on TV) runtime: theme scenes, thumbnails, sprites, HOME fonts, hero sky and the universal fallback,
    # copied from the public assets exactly as build_public_repo.py does (game hero images come with site-data/art/hero)
    shutil.copytree(os.path.join(REPO, "tools", "publish", "assets", "home"), os.path.join(out, "brand", "home"), dirs_exist_ok=True)
    # the console startup screen's preview (D-113): the five thumbnails and their manifest only (the console images ship in the app)
    sd2 = os.path.join(REPO, "tools", "publish", "assets", "startup"); os.makedirs(os.path.join(out, "brand", "startup"), exist_ok=True)
    for n in sorted(os.listdir(sd2)):
        if n.endswith("-thumb.jpg") or n == "startup-images.json": shutil.copy2(os.path.join(sd2, n), os.path.join(out, "brand", "startup", n))
    subprocess.run([sys.executable, os.path.join(REPO, "tools", "rom-pipeline", "build_html.py"), "--public", "--out", out], check=True, env=dict(os.environ, NES_STUDIO_SITE=out, NES_PUBLIC_BUILD="1"))
    shutil.copy2(os.path.join(out, "nes-catalog.html"), os.path.join(out, "index.html"))
    shutil.copytree(os.path.join(REPO, "tools", "publish", "site-static"), out, ignore=shutil.ignore_patterns("README.md", "store.sample.json"), dirs_exist_ok=True)   # static pages such as /app (the Windows app short link)
    for n in ("favicon.ico", "icon-32.png", "apple-touch-icon.png", "icon-192.png", "icon-512.png", "icon-maskable-512.png"): shutil.copy2(os.path.join(os.path.join(REPO, "tools", "publish", "assets"), "app-32.png" if n == "icon-32.png" else n), os.path.join(out, n))   # browser tab / home-screen icons
    shutil.copy2(os.path.join(os.path.join(REPO, "tools", "publish", "assets"), "as-seen-on-tv.png"), os.path.join(out, "brand", "public", "as-seen-on-tv.png"))
    if os.path.isdir(os.path.join(REPO, "tools", "publish", "assets", "help")): shutil.copytree(os.path.join(REPO, "tools", "publish", "assets", "help"), os.path.join(out, "brand", "help"), dirs_exist_ok=True)   # Help & User Guide figures   # the Preview on TV badge (Wikimedia Commons PNG, public domain, see THIRD_PARTY_NOTICES)
    # the browser game player (W1): the engine compiled to WebAssembly by tools/player/scripts/build_player.sh web (the pinned zig) into
    # build/web-player, its sound worklet and its menu fonts (baked from the site's own HOME fonts, as the Windows player's); engine.json
    # names the engine's SHA-256 so a page never mixes an engine with another build's runtime
    wp = os.environ.get("CCS_WEB_PLAYER") or os.path.join(REPO, "build", "web-player", "ccs-player.wasm")
    if os.path.isfile(wp):
        import hashlib
        pd = os.path.join(out, "play"); os.makedirs(pd, exist_ok=True); shutil.copy2(wp, os.path.join(pd, "ccs-player.wasm"))
        shutil.copy2(os.path.join(REPO, "tools", "rom-pipeline", "studio", "ccs-play-worklet.js"), pd)
        shutil.copy2(os.path.join(REPO, "tools", "player", "third_party", "sdl_gamecontrollerdb", "pad-db.json"), pd)   # known controllers (F17), the Windows player's table
        subprocess.run([sys.executable, os.path.join(REPO, "tools", "player", "bake_fonts.py"), os.path.join(out, "brand", "home"), os.path.join(pd, "fonts")], check=True, stdout=subprocess.DEVNULL)
        import re
        gh = open(os.path.join(REPO, "tools", "player", "core", "gp.h"), encoding="utf-8").read(); core = " ".join(re.search(rf'#define {k} "([^"]+)"', gh).group(1) for k in ("GP_CORE_ID", "GP_CORE_VERSION"))
        zig = json.load(open(os.path.join(REPO, "tools", "console-simulator", "toolchain.lock.json"), encoding="utf-8"))["zig"]["version"]   # the pinned toolchain that built it
        h = hashlib.sha256(open(wp, "rb").read()).hexdigest(); json.dump({"schema": "ccs-web-player/1", "engine_sha256": h, "core": core, "toolchain": f"zig {zig} wasm32-wasi"}, open(os.path.join(pd, "engine.json"), "w"))
        print("browser player:", os.path.getsize(wp), "bytes, sha256", h[:16])
    elif os.environ.get("CCS_REQUIRE_WEB_PLAYER") == "1": raise SystemExit("the browser player engine is missing (build_player.sh <repo>/build/web-player web)")
    pwa(out)
    open(os.path.join(out, ".nojekyll"), "w").close(); open(os.path.join(out, "robots.txt"), "w").write("User-agent: *\nAllow: /\n")
    if not os.path.exists(os.path.join(out, "release.json")): json.dump({"version": None, "note": "no companion release published yet"}, open(os.path.join(out, "release.json"), "w"))
    print("site built at", out)

def pwa(out: str):
    """W2: the web app manifest and the service worker. The worker's version is the SHA-256 of every file it keeps (path and bytes), so any
    change to the site makes a new version that is fetched completely beside the old one; shell = what the page needs to open and play,
    home = the HOME's art and music (kept when the app is installed or made ready for offline use). Covers are kept as they are used."""
    import hashlib
    man = {"name": "Classic Cart Studio", "short_name": "Cart Studio", "id": "./", "start_url": "./", "scope": "./", "display": "standalone", "orientation": "any",
           "background_color": "#101014", "theme_color": "#101014", "description": "Browse NES games, build your collections, back them up, and plan NES Classic Loadouts.",
           "icons": [{"src": "icon-192.png", "sizes": "192x192", "type": "image/png"}, {"src": "icon-512.png", "sizes": "512x512", "type": "image/png"},
                     {"src": "icon-maskable-512.png", "sizes": "512x512", "type": "image/png", "purpose": "maskable"}]}
    json.dump(man, open(os.path.join(out, "manifest.webmanifest"), "w", encoding="utf-8"), indent=1)
    rel = lambda p: os.path.relpath(p, out).replace(os.sep, "/")
    def files(*parts):
        b = os.path.join(out, *parts)
        if os.path.isfile(b): return [rel(b)]
        return sorted(rel(os.path.join(r, f)) for r, _, fs in os.walk(b) for f in fs) if os.path.isdir(b) else []
    shell = [p for p in ["index.html", "manifest.webmanifest", "favicon.ico", "icon-32.png", "icon-192.png", "icon-512.png", "icon-maskable-512.png", "apple-touch-icon.png",
                         "catalog.json", "builtin.json", "library.json", "rankings.json", "descriptions.json", "descriptions-library.json", "release.json"] if os.path.isfile(os.path.join(out, p))]
    shell += files("play") + files("brand", "fonts") + files("brand", "ccs") + files("brand", "public") + files("store")
    home = files("brand", "home") + files("brand", "startup")
    h = hashlib.sha256()
    for p in shell + home: h.update(p.encode() + b"\0" + hashlib.sha256(open(os.path.join(out, p), "rb").read()).digest())
    sw = open(os.path.join(REPO, "tools", "rom-pipeline", "studio", "sw.js"), encoding="utf-8").read()
    spec = {"version": h.hexdigest()[:20], "shell": shell, "home": home}
    open(os.path.join(out, "sw.js"), "w", encoding="utf-8", newline="\n").write("self.CCS_SW = " + json.dumps(spec, separators=(",", ":")) + ";\n" + sw)
    print("service worker:", spec["version"], len(shell), "shell files,", len(home), "HOME files")

if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(REPO, "build", "site"))
