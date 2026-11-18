r"""Library favorites (the Game Library's stars) on the built website, headless Edge, one fresh profile, real pointer and key events.
  details   the star in an open Game Details: a real mouse click adds the favorite, and the open modal's star, its pressed state and its
            label change at once; the modal stays open with focus on the star; Space removes it the same way; the stored list follows
  views     a game shown in the collection: its card star, the Favorites count and the Show Favorites label change with the Details star,
            without closing Details
  reload    the stored state is what Details shows after a reload
  tabs      a second tab of the site: its change reaches the first tab's open Details; a change made in the first (stale) tab to a
            different game keeps the second tab's favorite (the stored list is read again before each change)
  failure   a store that cannot be written: the star does not change, the stored list is unchanged, and the page says so; collections
            that cannot be read: the same, with its own message
  python test_favorites_web.py <built site folder> [--out results.json]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, json, os, sys, tempfile, time
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from studio_harness import EdgeSession, READY, CALM, serve

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); a = ap.parse_args()
    srv = serve(a.site); url = f"http://127.0.0.1:{srv.server_address[1]}/index.html"; res = []
    def ok(n, c, d=""):
        res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:300]})" if d and not c else ""), flush=True)
    def click(ed, sel):
        p = ed.js("(()=>{ const b=document.querySelector(%s); if(!b) return null; const r=b.getBoundingClientRect(); return {x:r.x+r.width/2, y:r.y+r.height/2}; })()" % json.dumps(sel))
        if not p: return False
        for t in ("mouseMoved", "mousePressed", "mouseReleased"):
            ed.call("Input.dispatchMouseEvent", {"type": t, "x": p["x"], "y": p["y"], "button": "left", "clickCount": 1} if t != "mouseMoved" else {"type": t, "x": p["x"], "y": p["y"]})
        time.sleep(0.4); return True
    def key(ed, k, code, vk):
        ed.call("Input.dispatchKeyEvent", {"type": "keyDown", "key": k, "code": code, "windowsVirtualKeyCode": vk, "text": k if k == " " else ""})
        ed.call("Input.dispatchKeyEvent", {"type": "keyUp", "key": k, "code": code, "windowsVirtualKeyCode": vk}); time.sleep(0.4)
    STAR = "#details .star"
    def dstate(ed): return ed.js("(()=>{ const b=document.querySelector('#details .star'); const d=document.getElementById('details'); return b?{on:b.classList.contains('on'), pressed:b.getAttribute('aria-pressed'), label:b.getAttribute('aria-label'), open:d.classList.contains('open'), focus:document.activeElement===b}:null; })()")
    def stored(ed): return ed.js("JSON.parse(localStorage.getItem(FKEY)||'[]')")
    def toastText(ed): return ed.js("(document.getElementById('toast')||{}).textContent||''")
    try:
        with EdgeSession(profile=tempfile.mkdtemp(prefix="ccs-fav-"), width=1440, height=900) as ed:
            ed.navigate(url); ed.wait_for(READY, timeout=120); ed.js(CALM)
            ed.wait_for("CS.starter&&CS.starter.state==='done'", timeout=90); time.sleep(1.0)
            # ---- details: a game in the open collection (its card is on screen), Game Details opened from its title ----
            gid = ed.js("(()=>{ const c=document.querySelector('#gridView .card[data-g]'); return c?c.dataset.g:null; })()")
            ok("setup: a game card is shown in the collection", bool(gid), gid)
            ed.js("openDetails(%s); 1" % json.dumps(gid)); time.sleep(0.6)
            ed.js("document.getElementById('details').scrollTop=0; 1")
            before = dstate(ed)
            ok("details: the star starts off (aria-pressed false, 'Add to Favorites')", before and not before["on"] and before["pressed"] == "false" and before["label"].startswith("Add to Favorites"), before)
            click(ed, STAR); after = dstate(ed)
            ok("details: a real mouse click selects the open modal's star at once (class, aria-pressed, 'Remove from Favorites')",
               after and after["on"] and after["pressed"] == "true" and after["label"].startswith("Remove from Favorites") and gid in stored(ed), (after, stored(ed)))
            ok("details: Game Details stays open with focus on the star", after and after["open"] and after["focus"], after)
            card = ed.js("(()=>{ const b=document.querySelector('#gridView .card[data-g=\"%s\"] .star'); return b?{on:b.classList.contains('on'), pressed:b.getAttribute('aria-pressed')}:null; })()" % gid)
            counts = ed.js("({hero:document.getElementById('heroFav').textContent, fav:(document.getElementById('favView')||{}).textContent||''})")
            ok("views: the game's card star matches, and the Favorites count says 1, without closing Details", card and card["on"] and card["pressed"] == "true" and counts["hero"] == "1" and "(1)" in counts["fav"], (card, counts))
            key(ed, " ", "Space", 32); kd = dstate(ed)
            ok("details: Space on the focused star removes it the same way (modal open, focus kept, stored list empty)", kd and not kd["on"] and kd["pressed"] == "false" and kd["open"] and kd["focus"] and gid not in stored(ed), (kd, stored(ed)))
            card = ed.js("(()=>{ const b=document.querySelector('#gridView .card[data-g=\"%s\"] .star'); return b?b.classList.contains('on'):null; })()" % gid)
            ok("views: the card star is off again and the count is 0", card is False and ed.js("document.getElementById('heroFav').textContent") == "0", card)
            # ---- reload ----
            click(ed, STAR); ed.navigate(url); ed.wait_for(READY, timeout=120); ed.js(CALM); time.sleep(1.0)
            ed.js("openDetails(%s); 1" % json.dumps(gid)); time.sleep(0.6); rd = dstate(ed)
            ok("reload: Details shows the stored favorite", rd and rd["on"] and rd["pressed"] == "true", rd)
            # ---- tabs ----
            other = ed.js("DATA.games.find(g=>g.game_id!==%s).game_id" % json.dumps(gid))
            t2 = ed.new_tab(); t2.navigate(url); t2.wait_for(READY, timeout=120); time.sleep(1.0)
            t2.js("favSet(%s, false); 1" % json.dumps(gid)); time.sleep(0.8)
            td = dstate(ed)
            ok("tabs: a change in another tab reaches this tab's open Details (star off, aria-pressed false)", td and not td["on"] and td["pressed"] == "false" and td["open"], td)
            t2.js("favSet(%s, true); 1" % json.dumps(gid)); time.sleep(0.8)
            ed.js("fav.delete(%s); 1" % json.dumps(gid))   # this tab now holds a stale list without the other tab's favorite
            ed.js("favSet(%s, true); 1" % json.dumps(other)); time.sleep(0.4)
            st = stored(ed)
            ok("tabs: a change made in a stale tab to another game keeps the other tab's favorite", gid in st and other in st, st)
            # ---- failure ----
            ed.js("(()=>{ window.__si=Storage.prototype.setItem; Storage.prototype.setItem=function(k,v){ if(k===FKEY){ const e=new Error('full'); e.name='QuotaExceededError'; throw e; } return window.__si.call(this,k,v); }; })(); 1")
            pre = dstate(ed); pst = stored(ed); click(ed, STAR); fd = dstate(ed)
            ok("failure: a store that cannot be written leaves the star and the stored list unchanged and says so",
               fd and fd["on"] == pre["on"] and fd["pressed"] == pre["pressed"] and stored(ed) == pst and "could not be saved" in toastText(ed), (pre, fd, toastText(ed)))
            ed.js("Storage.prototype.setItem=window.__si; CS_STATE='unreadable'; 1"); time.sleep(0.2)
            click(ed, STAR); ud = dstate(ed)
            ok("failure: with unreadable collections the star is not changed and the page says why", ud and ud["on"] == pre["on"] and "cannot be read" in toastText(ed), (ud, toastText(ed)))
            ed.js("CS_STATE='ok'; 1")
            ok("no page errors", not ed.exceptions, ed.exceptions[:2])
    finally:
        srv.shutdown()
    fails = [n for n, c in res if not c]
    print(f"\n{len(res) - len(fails)}/{len(res)} PASS" + (f"; FAIL: {', '.join(fails)}" if fails else ""))
    if a.out: json.dump({"results": [{"name": n, "pass": c} for n, c in res]}, open(a.out, "w", encoding="utf-8"), indent=1)
    return 0 if not fails else 1

if __name__ == "__main__": sys.exit(main())
