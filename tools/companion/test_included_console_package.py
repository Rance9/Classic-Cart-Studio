r"""An Included Game in an NES Classic package, built offline on this computer with no console attached (nothing is sent anywhere):
the Windows app keeps the approved build from its own installation (/api/personal/included's path: personal.ingest, origin "included",
with the record's details), then the console package of a collection holding it is built and verified (native_home console_package,
install layout). Checks: the game is playable in the package, its ROM file is the approved build's exact bytes, its title, version and
author are the record's, its HOME backdrop comes from one of the five shared scenes, its cover is the author's own screen, the package verifies, and no network connection is
attempted (any non-loopback connection fails the run).
  python test_included_console_package.py <built site folder> [--player ccs-player-armv7] [--out results.json]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, hashlib, json, os, shutil, socket, sys, tempfile
HERE = os.path.dirname(os.path.abspath(__file__))

def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")   # check names carry arrows and dashes; any console prints them
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--player", default=""); ap.add_argument("--out", default=""); a = ap.parse_args()
    site = os.path.abspath(a.site); work = tempfile.mkdtemp(prefix="ccs-incl-console-"); res = []; net = []
    os.environ["NES_STUDIO_SITE"] = site; os.environ["NES_STUDIO_DATA"] = os.path.join(work, "data"); os.makedirs(os.environ["NES_STUDIO_DATA"])
    real = socket.socket.connect
    def guard(self, addr):
        host = addr[0] if isinstance(addr, tuple) else str(addr)
        if host not in ("127.0.0.1", "localhost", "::1"): net.append(host); raise OSError("network blocked by the test")
        return real(self, addr)
    socket.socket.connect = guard
    for p in (HERE, os.path.join(HERE, "..", "rom-pipeline")): sys.path.insert(0, os.path.normpath(p))
    def ok(n, c, d=""): res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:300]})" if d and not c else ""), flush=True)
    try:
        import personal as ps, library_lookup as LL
        from native_home import console_package as cpk, scenes as SC
        idx = json.load(open(os.path.join(site, "included", "index.json"), encoding="utf-8")); g = idx["games"][0]; fp = os.path.join(site, "included", g["file_name"])
        r = ps.ingest(fp, origin="included"); pid = r["entry"]["id"]
        ps.merge_metadata(pid, {"title": g["title"], "version_label": g["version"], "description": g["text"], "year": g["year"], "publisher": g["creator"]})
        ok("the app keeps the approved build from its own installation", pid == "p_" + g["content_sha256"][:16])
        e = LL.personal_index()["entries"][pid]
        man = {"schema": "nes-classic-collection/4", "name": "Included Games", "hidden_factory_games": [],
               "selections": [{"game_id": pid, "version_id": pid, "display_title": e["user"]["title"], "source": "personal", "install_required": True}]}
        home = {"schema": "ccs-home-collection/1", "theme": "featured-shelf", "order": [pid], "games": [{"id": pid, "title": e["user"]["title"], "version_label": e["user"].get("version_label") or "", "kind": "personal"}],
                "folders": [], "favorites": {"items": {}}, "settings": {"reduced_motion": False, "hints": True}, "sample": False}
        out = os.path.join(work, "console")
        b = cpk.build_console(man, home, out, layout="install", player=a.player or None, label="Included Games")
        ok("the console package builds offline", b.get("ok"), b.get("error"))
        if b.get("ok"):
            m = b["manifest"]; gm = next((x for x in json.load(open(os.path.join(out, "session.json"), encoding="utf-8"))["games"] if x["id"] == pid), {})
            ok("the game is playable in the package", m["games"] == 1 and m["playable"] == 1 and not m["unavailable"], (m["games"], m["playable"], m["unavailable"]))
            rom = os.path.join(out, "roms", g["content_sha256"] + ".nes")
            ok("its ROM file is the approved build's exact bytes", os.path.isfile(rom) and hashlib.sha256(open(rom, "rb").read()).hexdigest() == g["file_sha256"], rom)
            ok("its title and version are the record's", gm.get("title") == g["title"] and gm.get("version_label") == g["version"], gm)
            hg = next(x for x in json.load(open(os.path.join(out, "home", "games.json"), encoding="utf-8"))["games"] if x["id"] == pid)
            want = SC.definition()["genres"].get(g.get("genre") or "") or SC.definition()["fallback"]
            ok(f"its HOME backdrop is its genre's shared scene ({g.get('genre')} -> {want})", hg.get("scene") == want and want != SC.definition()["fallback"] and hg.get("hero") and os.path.isfile(os.path.join(out, "home", hg["hero"])), {k: hg.get(k) for k in ("scene", "hero")})
            from native_home import config as CF
            cs = CF.cover_source(CF.resolve_game(pid, {}))
            ok("its cover is the author's own screen from the game (the Included Games cover), not a generated one", cs["kind"] == "authentic" and cs["rel"] == "included/" + g["cover"]["file"] and hg.get("cover") and os.path.isfile(os.path.join(out, "home", hg["cover"])), (cs, {k: v for k, v in hg.items() if "cover" in k}))
            ok("the package verifies", cpk.verify(out)["ok"])
            # Include HOME music: selected by default, the song is in the package and its player's session; left out, the package carries none
            def music_of(d):
                files = json.load(open(os.path.join(d, "MANIFEST.json"), encoding="utf-8"))["files"]
                pack = json.load(open(os.path.join(d, "home", "pack.json"), encoding="utf-8")); sess = json.load(open(os.path.join(d, "session.json"), encoding="utf-8"))
                cfgj = json.load(open(os.path.join(d, "home", "config.json"), encoding="utf-8"))
                return [f for f in files if f.startswith("home/music/")], pack.get("music"), sess["home"]["music"]["asset"], cfgj.get("include_music"), sum(f["size"] for f in files.values())
            dm, dp, da, dc, dt = music_of(out)
            ok("Include HOME music (the default): the song is in the package and the player's session", dm == ["home/music/ccs-home-theme.wav"] and dp and dp.get("file") and da.endswith("/home/music/ccs-home-theme.wav") and dc is None, (dm, dp, da, dc))
            out2 = os.path.join(work, "console-no-music"); b2 = cpk.build_console(man, dict(home, include_music=False), out2, layout="install", player=a.player or None, label="Included Games")
            if b2.get("ok"):
                nm, np_, na, nc, nt = music_of(out2)
                ok("Include HOME music left out: no song in the package, its HOME or the player's session (the payload, not only muted)",
                   nm == [] and np_ is None and na == "" and nc is False and not os.path.exists(os.path.join(out2, "home", "music")) and cpk.verify(out2)["ok"], (nm, np_, na, nc))
                fa = json.load(open(os.path.join(out, "MANIFEST.json"), encoding="utf-8"))["files"]; fb = json.load(open(os.path.join(out2, "MANIFEST.json"), encoding="utf-8"))["files"]
                diff = sorted(k for k in set(fa) | set(fb) if (fa.get(k) or {}).get("sha256") != (fb.get(k) or {}).get("sha256"))
                ok("leaving it out changes only the song (gone), its notice, and the records that name it; the totals agree",
                   diff == ["home/config.json", "home/manifest.json", "home/music/ccs-home-theme.wav", "home/pack.json", "licenses/NOTICE.txt", "session.json"] and "home/music/ccs-home-theme.wav" not in fb
                   and dt - nt == sum((fa.get(k) or {}).get("size", 0) - (fb.get(k) or {}).get("size", 0) for k in diff) and dt - nt > 11907116, (diff, dt, nt))
            else: ok("Include HOME music left out: the package builds", False, b2.get("error"))
            # HOME Music as MP3 is held in this version: a build that asks for it without its exact file is refused (never a package without its song)
            from native_home import package as PK
            PK.HOME_MUSIC_FORMAT = "mp3"; os.environ.pop("CCS_HOME_MUSIC_MP3", None)
            try: b3 = cpk.build_console(man, home, os.path.join(work, "console-mp3"), layout="install", player=a.player or None, label="Included Games")
            finally: PK.HOME_MUSIC_FORMAT = "wav"
            ok("HOME Music as MP3 without its file: refused, not built without music", not b3.get("ok") and "MP3 HOME Music" in (b3.get("error") or "") and not os.path.exists(os.path.join(work, "console-mp3", "MANIFEST.json")), b3)
            import home_music as HMU
            fake = os.path.join(work, "fake.mp3"); open(fake, "wb").write(b"\0" * HMU.MP3_BYTES); os.environ["CCS_HOME_MUSIC_MP3"] = fake
            try: ok("only the exact held MP3 is accepted (its SHA-256)", HMU.mp3_asset() is None and PK.HOME_MUSIC_FORMAT == "wav")
            finally: os.environ.pop("CCS_HOME_MUSIC_MP3", None)
        ok("no network connection was attempted", not net, net)
    finally:
        socket.socket.connect = real; shutil.rmtree(work, ignore_errors=True)
    n = sum(1 for _, c in res if c); print(f"RESULT: {'PASS' if n == len(res) else 'FAIL'} ({n}/{len(res)} checks)", flush=True)
    if a.out: json.dump([{"check": k, "pass": v} for k, v in res], open(a.out, "w", encoding="utf-8"), indent=1)
    return 0 if n == len(res) else 1

if __name__ == "__main__": sys.exit(main())
