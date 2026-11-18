r"""Hover cover previews (D-146) on the built website, headless Edge, real input through DevTools (mouse moves, wheel, keys, touch):
  sweep      the pointer crossing several covers quickly shows nothing
  dwell      held still on one cover (small jitter allowed) for about half a second shows its enlargement once, anchored beside that
             cover inside the window; more jitter does not move it (it never follows the pointer)
  hoverable  the pointer can move onto the enlargement: it stays; leaving both closes it
  scroll     scrolling closes it; covers moving under a still pointer show nothing until the pointer moves and settles again
  escape     Escape closes it and it does not come back while the pointer stays on that cover; another cover shows again after a dwell
  click      a click on a cover closes it (Game Details opens with nothing over it)
  touch      a finger tap never shows it
  dialog     with a dialog open nothing shows
  pref       Hover Cover Previews (View menu) off: nothing shows, also after a reload; on again: it shows
  list       the list view's covers behave the same
  python test_hover_web.py <built site folder> [--out results.json] [--shots DIR]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, json, os, sys, tempfile, time
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from studio_harness import EdgeSession, READY, CALM, serve

SHOWN = "(()=>{ const h=$('hover'); return h.style.display==='block'?(h.dataset.id||'?'):''; })()"

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); ap.add_argument("--shots", default=""); a = ap.parse_args()
    srv = serve(a.site); url = f"http://127.0.0.1:{srv.server_address[1]}/index.html"; res, log = [], {}
    def ok(n, c, d=""):
        res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:300]})" if d and not c else ""), flush=True)
    prof = tempfile.mkdtemp(prefix="ccs-hover-")
    try:
        with EdgeSession(profile=prof, width=1440, height=900) as ed:
            def move(x, y): ed.call("Input.dispatchMouseEvent", {"type": "mouseMoved", "x": float(x), "y": float(y), "pointerType": "mouse"})
            def covers(sel="#gridView .card .cover img[data-id]"):
                return ed.js("JSON.stringify([...document.querySelectorAll(%s)].map(i=>{ const r=i.getBoundingClientRect(); return {id:i.dataset.id, l:r.left, t:r.top, w:r.width, h:r.height}; }).filter(c=>c.t>230&&c.t+c.h<880))" % json.dumps(sel))
            def boot():
                ed.navigate(url); ed.wait_for(READY, timeout=120); ed.js(CALM); ed.wait_for("CS.starter&&CS.starter.state==='done'", timeout=90); time.sleep(1.0)
                ed.js("cvCatalog(true); 1"); time.sleep(1.0)   # the catalog lookup: many covers in a row
            def center(c, dx=0, dy=0): return c["l"] + c["w"] / 2 + dx, c["t"] + c["h"] / 2 + dy
            def dwell(c, ms=700):
                x, y = center(c); move(x, y); t0 = time.time()
                while time.time() - t0 < ms / 1000: time.sleep(0.1); move(x + ((int((time.time() - t0) * 10)) % 3) - 1, y)   # a hand's small jitter
            ed.call("Emulation.setFocusEmulationEnabled", {"enabled": True})
            boot(); cs = json.loads(covers()); log["covers"] = cs[:6]
            ok("setup: a row of covers to hover", len(cs) >= 6, len(cs))
            move(5, 600); time.sleep(0.3)
            # sweep
            seen = set()
            for c in cs[:6]:
                for k in range(3):
                    move(*center(c, -c["w"] / 3 + k * c["w"] / 3)); time.sleep(0.06); s = ed.js(SHOWN)
                    if s: seen.add(s)
            move(5, 600); time.sleep(0.7); s = ed.js(SHOWN)
            ok("sweep: crossing covers shows nothing", not seen and not s, (seen, s))
            # dwell
            c = cs[1]; x, y = center(c); move(x, y); t0 = time.time(); shown_at = None
            while time.time() - t0 < 1.5:
                time.sleep(0.05); move(x + (1 if int((time.time() - t0) * 20) % 2 else -1), y)
                if ed.js(SHOWN) == c["id"]: shown_at = time.time() - t0; break
            ok("dwell: about half a second on one cover shows its enlargement", shown_at is not None and 0.4 <= shown_at <= 1.0, shown_at)
            r1 = ed.js("JSON.stringify((()=>{ const r=$('hover').getBoundingClientRect(); return {l:r.left,t:r.top,r:r.right,b:r.bottom}; })())"); r1 = json.loads(r1)
            ok("dwell: anchored beside the cover, inside the window", (r1["l"] >= c["l"] + c["w"] - 1 or r1["r"] <= c["l"] + 1) and r1["l"] >= 0 and r1["t"] >= 0 and r1["r"] <= 1440 and r1["b"] <= 900, (r1, c))
            for k in range(6): move(x + k * 2, y + k); time.sleep(0.05)
            r2 = json.loads(ed.js("JSON.stringify((()=>{ const r=$('hover').getBoundingClientRect(); return {l:r.left,t:r.top,r:r.right,b:r.bottom}; })())"))
            ok("dwell: it does not follow the pointer", abs(r2["l"] - r1["l"]) < 1 and abs(r2["t"] - r1["t"]) < 1, (r1, r2))
            if a.shots: os.makedirs(a.shots, exist_ok=True); open(os.path.join(a.shots, "hover-dwell-1440.png"), "wb").write(ed.screenshot())
            # hoverable
            hx, hy = (r1["l"] + r1["r"]) / 2, (r1["t"] + r1["b"]) / 2
            for k in range(1, 6): move(x + (hx - x) * k / 5, y + (hy - y) * k / 5); time.sleep(0.03)
            time.sleep(0.5); ok("hoverable: the pointer can rest on the enlargement", ed.js(SHOWN) == c["id"], ed.js(SHOWN))
            move(5, 600); time.sleep(0.5); ok("hoverable: leaving both closes it", not ed.js(SHOWN))
            # scroll under a still pointer
            dwell(cs[2]); ok("scroll: (shown before scrolling)", ed.js(SHOWN) == cs[2]["id"])
            x, y = center(cs[2]); ed.call("Input.dispatchMouseEvent", {"type": "mouseWheel", "x": x, "y": y, "deltaX": 0, "deltaY": 260, "pointerType": "mouse"}); time.sleep(0.15)
            ok("scroll: scrolling closes it", not ed.js(SHOWN))
            t0 = time.time(); again = ""
            while time.time() - t0 < 1.4: time.sleep(0.1); move(x, y); again = again or ed.js(SHOWN)   # the same point: covers moved under it
            ok("scroll: covers moving under a still pointer show nothing", not again, again)
            now = json.loads(covers()); tgt = min(now, key=lambda c: abs(center(c)[0] - x) + abs(center(c)[1] - y))   # the cover nearest the pointer now
            tx, ty = center(tgt); move((x + tx) / 2, (y + ty) / 2); time.sleep(0.05); move(tx, ty); time.sleep(0.25); move(tx, ty); time.sleep(0.8)
            ok("scroll: a real move onto a cover and a settled pointer show it again", ed.js(SHOWN) == tgt["id"], (ed.js(SHOWN), tgt["id"]))
            move(5, 600); time.sleep(0.5); ed.js("window.scrollTo(0,0); 1"); time.sleep(0.5); move(5, 650); time.sleep(0.3); cs = json.loads(covers())
            # escape
            dwell(cs[3]); ok("escape: (shown)", ed.js(SHOWN) == cs[3]["id"])
            ed.call("Input.dispatchKeyEvent", {"type": "keyDown", "key": "Escape", "code": "Escape", "windowsVirtualKeyCode": 27}); ed.call("Input.dispatchKeyEvent", {"type": "keyUp", "key": "Escape", "code": "Escape", "windowsVirtualKeyCode": 27})
            time.sleep(0.2); ok("escape: Escape closes it", not ed.js(SHOWN))
            dwell(cs[3], 1100); ok("escape: it stays closed while the pointer stays on that cover", not ed.js(SHOWN), ed.js(SHOWN))
            dwell(cs[4]); ok("escape: another cover shows after a dwell", ed.js(SHOWN) == cs[4]["id"], ed.js(SHOWN))
            # click
            x, y = center(cs[4]); ed.call("Input.dispatchMouseEvent", {"type": "mousePressed", "x": x, "y": y, "button": "left", "clickCount": 1, "pointerType": "mouse"}); ed.call("Input.dispatchMouseEvent", {"type": "mouseReleased", "x": x, "y": y, "button": "left", "clickCount": 1, "pointerType": "mouse"})
            time.sleep(0.9); ok("click: a click opens Game Details with no enlargement over it", ed.js("$('details').classList.contains('open')") is True and not ed.js(SHOWN))
            ed.js("$('details').classList.remove('open'); 1"); move(5, 650); time.sleep(0.4)
            # dialog
            ed.js("openColl('Test','', '<p>dialog</p>'); 1"); time.sleep(0.3); dwell(cs[5], 900)
            ok("dialog: with a dialog open nothing shows", not ed.js(SHOWN)); ed.js("closeColl(); 1"); move(5, 650); time.sleep(0.4)
            # touch
            x, y = center(cs[5]); ed.call("Emulation.setTouchEmulationEnabled", {"enabled": True, "maxTouchPoints": 2})
            ed.call("Input.dispatchTouchEvent", {"type": "touchStart", "touchPoints": [{"x": x, "y": y}]}); time.sleep(0.7)
            s_touch = ed.js(SHOWN); ed.call("Input.dispatchTouchEvent", {"type": "touchEnd", "touchPoints": []}); time.sleep(0.6)
            ok("touch: a finger never shows it", not s_touch and not ed.js(SHOWN), s_touch)
            ed.call("Emulation.setTouchEmulationEnabled", {"enabled": False}); ed.js("$('details').classList.remove('open'); 1"); move(5, 650); time.sleep(0.3)
            # preference
            ed.js("$('selectBtn').click(); 1"); time.sleep(0.3); ed.js("$('hvPref').click(); 1"); time.sleep(0.3)
            ok("pref: View › Hover Cover Previews turns it off (and says so)", ed.js("HV.on") is False and ed.js("$('hvPref').getAttribute('aria-checked')") == "false")
            dwell(cs[1], 900); ok("pref: off, nothing shows", not ed.js(SHOWN))
            boot(); cs = json.loads(covers()); dwell(cs[1], 900); ok("pref: still off after a reload", ed.js("HV.on") is False and not ed.js(SHOWN))
            ed.js("hvSetPref(true); 1"); move(5, 650); time.sleep(0.3); dwell(cs[2]); ok("pref: on again, it shows", ed.js(SHOWN) == cs[2]["id"], ed.js(SHOWN))
            move(5, 650); time.sleep(0.4)
            # list view
            ed.js("$('vList').click(); 1"); time.sleep(0.8); lc = json.loads(covers("#listView img.thumb[data-id]"))
            if lc: dwell(lc[1]); ok("list: a list cover held still shows its enlargement", ed.js(SHOWN) == lc[1]["id"], ed.js(SHOWN))
            else: ok("list: covers in the list view", False, "none found")
            ok("no page errors", not ed.exceptions, ed.exceptions[:2])
    finally:
        srv.shutdown()
    fails = [n for n, c in res if not c]
    print(f"\n{len(res) - len(fails)}/{len(res)} PASS" + (f"; FAIL: {', '.join(fails)}" if fails else ""))
    if a.out: json.dump({"results": [{"name": n, "pass": c} for n, c in res], "log": log}, open(a.out, "w", encoding="utf-8"), indent=1)
    return 0 if not fails else 1

if __name__ == "__main__": sys.exit(main())
