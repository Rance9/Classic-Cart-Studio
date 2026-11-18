r"""The Included Games on the website (fresh browser profile, built site on 127.0.0.1, no other service), as a new person meets them (D-146):
  ready      the first start prepares them by itself and opens their collection: exactly the games of included/index.json as personal games
             (p_ + content SHA-256), each kept in this browser's game store by its exact content identity, each with a Play button; no
             menu, popup or tour comes first
  again      Add the Included Games… (collection menu) keeps one collection (no duplicate) and says it is already there
  play       each game's own Play button starts it by itself (not HOME) in the browser game player (the pinned FCEUmm build); the FIRST
             Enter at the player's start choice (Start New Game) starts the game and it draws frames; a screenshot of each running game.
             The page has focus before the game starts, as a window in front does for every key a person presses (CDP focus emulation):
             headless Edge otherwise gives the page focus together with the first key, and the player rightly ignores a key already held
             when focus arrives (settle on focus gained). v1p2 trace: evidence 5b, first-key-trace.json
  backup     a backup sees both games' files (fbKeptSources: the files a Back Up includes)
  restart    after a reload, the collection and both files are still here, and nothing was added again
Screenshots go to --shots.   python test_included_web.py <built site folder> [--shots DIR]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, json, os, sys, tempfile, time
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from studio_harness import EdgeSession, READY, CALM, serve, SETTLED

def key(ed, k, code, vk):   # a trusted key press, as a person's keyboard sends it
    for t in ("keyDown", "keyUp"): ed.call("Input.dispatchKeyEvent", {"type": t, "key": k, "code": code, "windowsVirtualKeyCode": vk, "nativeVirtualKeyCode": vk}); time.sleep(0.08)

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--shots", default=""); a = ap.parse_args()
    idx = json.load(open(os.path.join(a.site, "included", "index.json"), encoding="utf-8")); want = {"p_" + g["content_sha256"][:16]: g for g in idx["games"]}
    s = serve(a.site); srv, port = s if isinstance(s, tuple) else (s, s.server_address[1]); url = f"http://127.0.0.1:{port}/index.html"
    res = []; ok = lambda n, c, d="": (res.append((n, bool(c))), print(("PASS " if c else "FAIL ") + n + (f"  ({d})" if d and not c else ""), flush=True))
    shots = a.shots or tempfile.mkdtemp(prefix="ccs-included-shots-"); os.makedirs(shots, exist_ok=True)
    prof = tempfile.mkdtemp(prefix="ccs-included-profile-")
    def wait(ed, expr, t=30):
        end = time.time() + t
        while time.time() < end:
            v = ed.js(expr)
            if v: return v
            time.sleep(0.25)
        return None
    try:
        with EdgeSession(profile=prof, width=1440, height=900) as ed:
            ed.navigate(url); ed.wait_for(READY, timeout=120); time.sleep(0.5)
            ok("ready: no welcome, popup or tour on the first start", ed.js("!$('collm').classList.contains('open')&&!document.querySelector('#welcome.open')") is True)
            ed.wait_for(SETTLED, timeout=120); time.sleep(1.0); ed.js(CALM)
            st = ed.js("(()=>{ const r=CS.items[CS.active]; return {name:r&&r.name, sel:r?r.sel.slice().sort():[], miss:r?Object.keys((r.coll||{}).missing||{}).sort():[], n:csList().length, mode:CV_MODE, play:[...document.querySelectorAll('[data-cvplay]')].map(b=>b.dataset.cvplay).sort()}; })()")
            ok("ready: the Included Games collection is open, holding exactly the included games", st["name"] == "Included Games" and st["sel"] == sorted(want) and st["miss"] == sorted(want) and st["n"] == 1, json.dumps(st)[:240])
            ok("ready: the collection view shows a Play button on each game", st["mode"] == "collection" and st["play"] == sorted(want), st["play"])
            ed.js("(async()=>{ CCSROMS._have=null; window.__have=await Promise.all(%s.map(s=>CCSROMS.has(s))); })()" % json.dumps([g["content_sha256"] for g in want.values()]))
            ok("ready: both files kept by their content identity", wait(ed, "window.__have&&window.__have.every(Boolean)", 20), str(ed.js("window.__have")))
            open(os.path.join(shots, "included-ready.png"), "wb").write(ed.screenshot())
            ed.js("inclAdd()"); ok("again: Add the Included Games keeps one collection and says it is already there", wait(ed, "!!document.getElementById('inclOpen')&&/already in My Collections/.test(document.getElementById('collm').textContent)", 60)
                                                                       and ed.js("csList().filter(x=>x.name==='Included Games').length") == 1)
            ed.js("document.getElementById('inclStay').click()"); time.sleep(0.4)
            for gid, g in want.items():
                ed.navigate(url); ed.wait_for(READY, timeout=120); ed.wait_for(SETTLED, timeout=60); time.sleep(1.0); ed.js(CALM)   # each game in a fresh page: no earlier session can answer for it
                ed.call("Emulation.setFocusEmulationEnabled", {"enabled": True})   # the page is in front (see play above)
                ed.js("document.querySelector('[data-cvplay=%s]').click(); 1" % json.dumps(gid))
                wait(ed, "CCSPLAY.running()&&!!CCSPLAY.debug().lastMenu", 30); time.sleep(0.5)
                ok(f"play: {g['title']} opens by itself (not HOME) at the player's start choice", ed.js("$('playv').classList.contains('open')&&!TV.open&&CCSPLAY.running()") is True)
                open(os.path.join(shots, f"included-start-{g['key']}.png"), "wb").write(ed.screenshot())
                ok(f"play: the page has focus when {g['title']} starts (the player takes keys)", ed.js("document.hasFocus()&&CPL.s&&CPL.s.focused===true"))
                key(ed, "Enter", "Enter", 13)   # the player's start choice: Start New Game (highlighted; Enter = A): ONE press
                ok(f"play: the first Enter starts {g['title']} (no second press)", wait(ed, "CCSPLAY.running()&&CCSPLAY.debug().frames>0", 3))
                started = wait(ed, "CCSPLAY.running()&&CCSPLAY.debug().frames>120", 40)
                dbg = ed.js("CCSPLAY.running()?CCSPLAY.debug():null"); running = ed.js("CCSPLAY.running()&&CPL.s&&CPL.s.game?CPL.s.game.sha:''")
                ok(f"play: {g['title']} plays in the browser player (the running game is this file)", started and running == g["content_sha256"], f"running {running[:16]}, {json.dumps(dbg)[:120]}")
                time.sleep(1.5); open(os.path.join(shots, f"included-title-{g['key']}.png"), "wb").write(ed.screenshot())
                key(ed, "m", "KeyM", 77); time.sleep(2.5)   # START on the title screen
                open(os.path.join(shots, f"included-play-{g['key']}.png"), "wb").write(ed.screenshot())
            ed.js("(async()=>{ window.__fb=await fbKeptSources(); window.__src=[...ROMSRC.values()].map(r=>r.content_sha256); })()")
            n = wait(ed, "window.__src&&window.__src.length?window.__src:null", 20) or []
            ok("backup: a backup includes both games' files", all(g["content_sha256"] in n for g in want.values()), str(n)[:160])
        with EdgeSession(profile=prof, width=1440, height=900) as ed:
            ed.navigate(url); ed.wait_for(READY, timeout=120); ed.wait_for(SETTLED, timeout=60); time.sleep(1.0)
            ok("restart: the collection is kept, once", ed.js("csList().filter(x=>x.name==='Included Games').length") == 1 and ed.js("CS.items[CS.active].name") == "Included Games")
            ed.js("(async()=>{ CCSROMS._have=null; window.__have=await Promise.all(%s.map(s=>CCSROMS.has(s))); })()" % json.dumps([g["content_sha256"] for g in want.values()]))
            ok("restart: both files are kept", wait(ed, "window.__have&&window.__have.every(Boolean)", 20))
    finally:
        try: srv.shutdown()
        except Exception: pass
    passed = sum(1 for _, c in res if c); print(f"RESULT: {'PASS' if passed == len(res) else 'FAIL'} ({passed}/{len(res)} checks); screenshots in {shots}")
    return 0 if passed == len(res) else 1

if __name__ == "__main__": sys.exit(main())
