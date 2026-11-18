r"""Right-click menus on the built website (the Windows app shows the same page), headless Edge, one fresh profile, real pointer and key
events through the browser's input layer.
  game      a game card: a short menu at the pointer (3-5 rows, no nested menus) with the card's own actions; Arrow keys move, Enter acts
            (Add to Favorites: the card's star changes), Escape closes and focus returns; a second right-click replaces the first menu;
            a click outside closes it without acting
  details   inside the open Game Details: its menu (no Game Details row); Escape closes the menu only, Details stays open
  keys      the ContextMenu key and Shift+F10 on a focused card open its menu
  safety    the game removed from the collection (another tab), or another collection opened, between opening the menu and choosing a
            row: nothing is done and the page says so
  collection, folder, space   Manage Collections' row (Duplicate makes a copy), the collection menu's open collection, a HOME folder
            in HOME Settings (Move Down reorders it), the empty part of the list (Add ROM Files…, Included Games…, Restore a Backup…,
            Open HOME)
  kept      the browser's own behavior stays for the search field, a link, selected text, a canvas (the games' screens) and a long press
            on a touch screen
  python test_context_menu_web.py <built site folder> [--out results.json]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, json, os, sys, tempfile, time
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from studio_harness import EdgeSession, READY, CALM, serve

MENU = "(()=>{ const m=document.querySelector('.ctxmenu'); if(!m) return null; const r=m.getBoundingClientRect(); return {rows:[...m.querySelectorAll('.mi')].map(b=>b.textContent.trim()), nested:m.querySelectorAll('.menu,[aria-haspopup],details').length, x:r.x, y:r.y, focus:document.activeElement&&document.activeElement.closest('.ctxmenu')?document.activeElement.textContent.trim():null, n:document.querySelectorAll('.ctxmenu').length}; })()"

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); a = ap.parse_args()
    srv = serve(a.site); url = f"http://127.0.0.1:{srv.server_address[1]}/index.html"; res, log = [], {}
    def ok(n, c, d=""):
        res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:300]})" if d and not c else ""), flush=True)
    def center(ed, sel):
        if not ed.js("(()=>{ const e=document.querySelector(%s); if(!e) return false; const r=e.getBoundingClientRect(); if(r.top<80||r.bottom>innerHeight-20) e.scrollIntoView({block:'center', behavior:'instant'}); return true; })()" % json.dumps(sel)): return None
        time.sleep(0.5)   # a scroll closes an open menu: let it settle first
        return ed.js("(()=>{ const e=document.querySelector(%s); const r=e.getBoundingClientRect(); return {x:Math.round(r.x+Math.min(r.width/2,40)), y:Math.round(r.y+Math.min(r.height/2,30))}; })()" % json.dumps(sel))
    def mouse(ed, x, y, button="right"):
        ed.call("Input.dispatchMouseEvent", {"type": "mouseMoved", "x": x, "y": y})
        ed.call("Input.dispatchMouseEvent", {"type": "mousePressed", "x": x, "y": y, "button": button, "clickCount": 1})
        ed.call("Input.dispatchMouseEvent", {"type": "mouseReleased", "x": x, "y": y, "button": button, "clickCount": 1}); time.sleep(0.3)
    def rclick(ed, sel):
        p = center(ed, sel)
        if not p: return None
        mouse(ed, p["x"], p["y"]); return p
    def key(ed, k, code, vk, shift=False):
        mods = 8 if shift else 0
        ed.call("Input.dispatchKeyEvent", {"type": "rawKeyDown", "key": k, "code": code, "windowsVirtualKeyCode": vk, "modifiers": mods})
        ed.call("Input.dispatchKeyEvent", {"type": "keyUp", "key": k, "code": code, "windowsVirtualKeyCode": vk, "modifiers": mods}); time.sleep(0.3)
    def enter(ed): ed.call("Input.dispatchKeyEvent", {"type": "keyDown", "key": "Enter", "code": "Enter", "windowsVirtualKeyCode": 13, "text": "\r"}); ed.call("Input.dispatchKeyEvent", {"type": "keyUp", "key": "Enter", "code": "Enter", "windowsVirtualKeyCode": 13}); time.sleep(0.5)
    def esc(ed): key(ed, "Escape", "Escape", 27)
    def down(ed): key(ed, "ArrowDown", "ArrowDown", 40)
    def choose(ed, label):   # the row by its name, with the keyboard (Arrow keys until it has focus, then Enter)
        for _ in range(8):
            m = ed.js(MENU)
            if not m: return False
            if m["focus"] == label: enter(ed); return True
            down(ed)
        return False
    def toastText(ed): return ed.js("(document.getElementById('toast')||{}).textContent||''")
    try:
        with EdgeSession(profile=tempfile.mkdtemp(prefix="ccs-ctx-"), width=1440, height=950) as ed:
            ed.navigate(url); ed.wait_for(READY, timeout=120); ed.js(CALM)
            ed.wait_for("CS.starter&&CS.starter.state==='done'", timeout=120); time.sleep(1.0)
            ed.js("window.__cm=[]; addEventListener('contextmenu', e=>window.__cm.push({prevented:e.defaultPrevented, tag:e.target.tagName}), false); 1")
            ids = ed.js("[...document.querySelectorAll('#gridView .card[data-g]')].map(c=>c.dataset.g)")
            ok("setup: the open collection shows its games as cards", ids and len(ids) >= 2, ids)
            gid, other = ids[0], ids[1]; card = f'#gridView .card[data-g="{gid}"]'
            # ---- a game card ----
            p = rclick(ed, card + " .m"); m = ed.js(MENU); log["card"] = m
            ok("game: right-click on a card opens a short menu at the pointer (3-5 rows, nothing nested) and focuses its first row",
               m and 3 <= len(m["rows"]) <= 5 and m["nested"] == 0 and abs(m["x"] - p["x"]) < 4 and abs(m["y"] - p["y"]) < 4 and m["focus"] == m["rows"][0], (m, p))
            ok("game: the rows are the card's own actions (Play, Game Details, Add to Favorites, Show in HOME, Remove from This Collection)",
               m and m["rows"] == ["Play", "Game Details", "Add to Favorites", "Show in HOME", "Remove from This Collection"], m and m["rows"])
            ok("game: the browser's own menu is not shown over it (the event is handled)", ed.js("window.__cm.length&&window.__cm[window.__cm.length-1].prevented") is True, ed.js("window.__cm"))
            down(ed); m2 = ed.js(MENU); ok("game: ArrowDown moves to the next row", m2 and m2["focus"] == m["rows"][1], m2)
            ed.js("document.querySelector(%s).focus(); 1" % json.dumps(card + " .topen")); rclick(ed, card + " .m")
            ok("game: one menu at a time (a second right-click replaces the first)", ed.js(MENU)["n"] == 1)
            esc(ed); ok("game: Escape closes the menu and focus returns to where it was (the card's title)", ed.js(MENU) is None and ed.js("document.activeElement===document.querySelector(%s)" % json.dumps(card + " .topen")), ed.js("document.activeElement.outerHTML.slice(0,80)"))
            rclick(ed, card + " .m"); chose = choose(ed, "Add to Favorites")
            star = ed.js("(()=>{ const b=document.querySelector(%s); return b?{on:b.classList.contains('on'), pressed:b.getAttribute('aria-pressed')}:null; })()" % json.dumps(card + " .star"))
            ok("game: Enter on Add to Favorites makes it a favorite (the card's star changes) and closes the menu", chose and star and star["on"] and star["pressed"] == "true" and ed.js(MENU) is None and ed.js("fav.has(%s)" % json.dumps(gid)), star)
            rclick(ed, card + " .m"); rows = (ed.js(MENU) or {}).get("rows")
            ok("game: the menu follows the game's state (now Remove from Favorites)", rows and "Remove from Favorites" in rows, rows)
            p2 = ed.js("(()=>{ const r=document.querySelector('#gridView').getBoundingClientRect(); return {x:Math.round(r.right-12), y:Math.round(r.top+6)}; })()"); mouse(ed, p2["x"], p2["y"], "left")   # plain space beside the cards
            ok("game: a click outside closes it without acting", ed.js(MENU) is None and ed.js("fav.has(%s)" % json.dumps(gid)))
            # ---- Game Details ----
            ed.js("openDetails(%s); 1" % json.dumps(gid)); time.sleep(0.6)
            rclick(ed, "#details .star"); md = ed.js(MENU); log["details"] = md
            ok("details: inside Game Details its menu has the game's actions without Game Details", md and "Game Details" not in md["rows"] and "Play" in md["rows"] and 3 <= len(md["rows"]) <= 5, md)
            esc(ed); ok("details: Escape closes the menu only; Game Details stays open", ed.js(MENU) is None and ed.js("document.getElementById('details').classList.contains('open')"))
            ed.js("document.getElementById('details').classList.remove('open'); 1"); time.sleep(0.3)
            # ---- keys ----
            ed.js("document.querySelector(%s).focus(); 1" % json.dumps(card + " .topen")); key(ed, "ContextMenu", "ContextMenu", 93); mk = ed.js(MENU)
            ok("keys: the ContextMenu key on a focused card opens its menu with focus in it", mk and mk["rows"][0] == "Play" and mk["focus"] == "Play", mk)
            esc(ed); key(ed, "F10", "F10", 121, shift=True); mf = ed.js(MENU)
            ok("keys: Shift+F10 opens it too", mf and mf["rows"][0] == "Play", mf); esc(ed)
            # ---- safety: re-checked before the row runs ----
            o_card = f'#gridView .card[data-g="{other}"]'
            rclick(ed, o_card + " .m"); ed.js("sel.delete(%s); 1" % json.dumps(other)); time.sleep(0.3)   # removed meanwhile (as another tab's change arrives), before the list is drawn again
            before = ed.js("[...sel]"); chose = choose(ed, "Remove from This Collection")
            ok("safety: a game removed meanwhile: its row does nothing and the page says so", chose and ed.js("[...sel]") == before and "Nothing was done" in toastText(ed), (toastText(ed), chose))
            ed.js("sel.add(%s); 1" % json.dumps(other)); time.sleep(0.5)
            rclick(ed, o_card + " .m"); first = ed.js("CS.active"); ed.js("const r=csCreate('Other'); csSwitch(r.id, true); 1"); time.sleep(0.5)
            still = ed.js(MENU) is not None; chose = choose(ed, "Add to Favorites")
            ok("safety: another collection opened meanwhile: nothing is done (the menu closed with the change, or its row refuses and says so)",
               not ed.js("fav.has(%s)" % json.dumps(other)) and ((not still and not chose) or "Nothing was done" in toastText(ed)), (still, chose, toastText(ed)))
            ed.js("csSwitch(%s, true); 1" % json.dumps(first)); time.sleep(0.6)
            # ---- collections ----
            ed.js("csManager(); 1"); time.sleep(0.5); n0 = ed.js("csList().length")
            rclick(ed, ".csrow:not(.on) .csn"); mc = ed.js(MENU); log["collection_row"] = mc
            ok("collection: a Manage Collections row: Open, Rename…, Duplicate, Back Up…, Remove…", mc and mc["rows"] == ["Open", "Rename…", "Duplicate", "Back Up…", "Remove…"], mc)
            choose(ed, "Duplicate"); time.sleep(0.4)
            ok("collection: Duplicate makes a copy (one more collection)", ed.js("csList().length") == n0 + 1, (n0, ed.js("csList().length")))
            ed.js("closeColl(); 1"); time.sleep(0.3)
            ed.js("document.getElementById('collBtn').click(); 1"); time.sleep(0.4)
            rclick(ed, "#csQuick [data-cs-cur]"); mq = ed.js(MENU)
            ok("collection: the open collection in the collection menu: Rename…, Duplicate, Back Up…, Remove… (no Open)", mq and mq["rows"] == ["Rename…", "Duplicate", "Back Up…", "Remove…"], mq)
            esc(ed); ed.js("document.querySelectorAll('.menu').forEach(m=>m.hidden=true); 1")
            # ---- a HOME folder ----
            ed.js("openFolders(); const m=TV.model; homeFolderCreate(m,'Alpha',null); homeFolderCreate(m,'Beta',null); hfSave(); 1"); time.sleep(0.5)
            order0 = ed.js("homeTopFolders(TV.model).map(f=>f.name)")
            rclick(ed, ".hf-f:first-child"); mh = ed.js(MENU)
            ok("folder: a HOME folder: Rename…, New Subfolder…, Move Up, Move Down, Delete…", mh and mh["rows"] == ["Rename…", "New Subfolder…", "Move Up", "Move Down", "Delete…"], mh)
            choose(ed, "Move Down"); time.sleep(0.3); order1 = ed.js("homeTopFolders(TV.model).map(f=>f.name)")
            ok("folder: Move Down moves that folder below the next one", order0[:2] == ["Alpha", "Beta"] and order1[:2] == ["Beta", "Alpha"], (order0, order1))
            ed.js("closeColl(); 1"); time.sleep(0.3)
            # ---- the empty part of the list ----
            ed.js("[...document.querySelectorAll('#gridView .card')].pop().scrollIntoView({block:'center', behavior:'instant'}); 1"); time.sleep(0.5)
            sp = ed.js("(()=>{ const g=document.getElementById('gridView'), r=g.getBoundingClientRect(), c=[...g.querySelectorAll('.card')].pop().getBoundingClientRect(); const x=Math.round(Math.min(r.right-30, c.right+60)), y=Math.round(c.top+c.height/2); return {x, y, at:document.elementFromPoint(x,y).outerHTML.slice(0,60)}; })()")
            mouse(ed, sp["x"], sp["y"]); ms = ed.js(MENU)
            ok("space: the empty part of the list: Add Game Files…, Included Games…, Restore a Backup…, Open HOME", ms and ms["rows"] == ["Add Game Files…", "Included Games…", "Restore a Backup…", "Open HOME"], (ms, sp))
            esc(ed)
            # ---- the browser's own behavior is kept ----
            ed.js("window.scrollTo(0,0); window.__cm=[]; 1"); time.sleep(0.3)
            rclick(ed, "#q"); r1 = ed.js("({menu:!!document.querySelector('.ctxmenu'), ev:window.__cm.slice(-1)[0]||null})")
            ok("kept: the search field keeps the browser's own menu (no page menu, not prevented)", not r1["menu"] and r1["ev"] and not r1["ev"]["prevented"], r1)
            ed.js("(()=>{ const a=document.createElement('a'); a.href='#x'; a.id='ctxLink'; a.textContent='a link'; document.querySelector(%s).appendChild(a); })(); 1" % json.dumps(card + " .body"))
            rclick(ed, "#ctxLink"); r2 = ed.js("({menu:!!document.querySelector('.ctxmenu'), ev:window.__cm.slice(-1)[0]||null})")
            ok("kept: a link keeps the browser's own menu", not r2["menu"] and r2["ev"] and not r2["ev"]["prevented"], r2)
            ed.js("(()=>{ const t=document.querySelector(%s); const r=document.createRange(); r.selectNodeContents(t); const s=getSelection(); s.removeAllRanges(); s.addRange(r); })(); 1" % json.dumps(card + " .m"))
            rclick(ed, card + " .m"); r3 = ed.js("({menu:!!document.querySelector('.ctxmenu'), ev:window.__cm.slice(-1)[0]||null})")
            ok("kept: selected text keeps the browser's own menu (Copy)", not r3["menu"] and r3["ev"] and not r3["ev"]["prevented"], r3)
            ed.js("getSelection().removeAllRanges(); (()=>{ const c=document.createElement('canvas'); c.id='ctxCanvas'; c.width=80; c.height=40; document.querySelector(%s).appendChild(c); })(); 1" % json.dumps(card + " .body"))
            rclick(ed, "#ctxCanvas"); r4 = ed.js("({menu:!!document.querySelector('.ctxmenu'), ev:window.__cm.slice(-1)[0]||null})")
            ok("kept: a canvas (the games' screens use their own right-click) gets no page menu", not r4["menu"] and r4["ev"] and not r4["ev"]["prevented"], r4)
            p = center(ed, card + " .m")
            ed.call("Input.dispatchTouchEvent", {"type": "touchStart", "touchPoints": [{"x": p["x"], "y": p["y"]}]}); time.sleep(0.1)
            ed.call("Input.dispatchTouchEvent", {"type": "touchEnd", "touchPoints": []}); time.sleep(0.1)
            ed.js("(()=>{ const t=document.querySelector(%s); t.dispatchEvent(new PointerEvent('contextmenu',{bubbles:true,cancelable:true,pointerType:'touch',clientX:%d,clientY:%d})); })(); 1" % (json.dumps(card + " .m"), p["x"], p["y"]))
            time.sleep(0.3); r5 = ed.js("({menu:!!document.querySelector('.ctxmenu'), ev:window.__cm.slice(-1)[0]||null})")
            ok("kept: a long press on a touch screen opens no page menu (the card's own buttons do the same)", not r5["menu"] and r5["ev"] and not r5["ev"]["prevented"], r5)
            ok("no page errors", not ed.exceptions, ed.exceptions[:2])
    finally:
        srv.shutdown()
    fails = [n for n, c in res if not c]
    print(f"\n{len(res) - len(fails)}/{len(res)} PASS" + (f"; FAIL: {', '.join(fails)}" if fails else ""))
    if a.out: json.dump({"results": [{"name": n, "pass": c} for n, c in res], "log": log}, open(a.out, "w", encoding="utf-8"), indent=1, default=str)
    return 0 if not fails else 1

if __name__ == "__main__": sys.exit(main())
