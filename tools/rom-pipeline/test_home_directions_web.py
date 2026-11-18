r"""Controller directions on the browser HOME (Preview on TV on the website; the Windows app shows the same page), all four themes, the
sample of the 30 built-in games (a new, empty collection) with five folders. Every direction is pressed twice over, from the keyboard
(real key events) and from a standard-layout controller supplied through navigator.getGamepads (its D-pad buttons, and its left stick),
and judged against what the HOME DRAWS: the highlighted element's place before and after the press.
  raw       each raw input reaches the HOME as exactly one direction: Arrow keys, D-pad buttons 12-15, the stick past half way, and a
            diagonal stick as its stronger axis only (one move, not two)
  rule      after a direction the highlight is on the element drawn in that direction from where it was (measured in one frame, so a
            scrolling row or panel cannot mislead), mostly along that axis; in a grid, the element nearest its column in the row
            drawn above/below; into another area (the tabs above, the hints below, the description under the buttons) that area lies
            in the pressed direction; or nothing moved (an edge). The Arcade Grid's page turn keeps the column
  screens   the games (grid, carousel, shelf, map), the tabs/sidebar/signs, Game Details (buttons in a row, then the description),
            the START menu (a list), the theme picker (one row: Up and Down do nothing), the Clear History question (two answers side
            by side), Browse (folders, then the categories on their own rows)
  same      the keyboard and the controller make the same moves on every screen
  python test_home_directions_web.py <built site folder> [--out results.json]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, json, os, sys, tempfile, time
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from studio_harness import EdgeSession, READY, CALM, serve

PAD = ("(w=>{ if(!w.__pad){ w.__pad={index:0, id:'Xbox 360 Controller (XInput STANDARD GAMEPAD Vendor: 045e Product: 028e)', mapping:'standard', "
       "connected:true, timestamp:1, buttons:Array.from({length:17},()=>({pressed:false,touched:false,value:0})), axes:[0,0,0,0]}; } "
       "Object.defineProperty(w.navigator,'getGamepads',{configurable:true,value:()=>[window.__pad]}); return true; })(window)")
# the highlighted element as drawn: the last .focus/.ffocus in the HOME (an overlay is drawn after the section), never a whole zone
SNAP = r"""(()=>{ const R=homeRoot(document.getElementById('tvHome')); const all=[...R.querySelectorAll('.focus,.ffocus')].filter(e=>!e.classList.contains('zone')&&e.getBoundingClientRect().width>0);
 const key=e=>{ const c=[...e.classList].filter(x=>!['focus','ffocus','active','on'].includes(x)).sort().join('.'); return e.tagName+'.'+c+'|'+(e.dataset.id||(e.textContent||'').trim().replace(/\s+/g,' ').slice(0,40)); };
 const grp=e=>e.tagName+'.'+[...e.classList].filter(x=>!['focus','ffocus','active','on','cat','game'].includes(x)).sort().join('.');   // one grid: Browse's folder, game and category tiles
 const box=e=>{ const r=e.getBoundingClientRect(); return [r.x,r.y,r.x+r.width,r.y+r.height]; };
 const f=all[all.length-1]||null; const items={}; if(f){ const g=grp(f); R.querySelectorAll('*').forEach(e=>{ if(grp(e)===g&&e.getBoundingClientRect().width>0) items[key(e)]=box(e); }); }
 const S=TV.S; return {focus:f?{key:key(f), grp:grp(f), box:box(f)}:null, items, st:{zone:S.zone, index:S.index, overlay:S.overlay?S.overlay.type:null, oindex:S.overlay&&S.overlay.index!=null?S.overlay.index:null,
   details:S.detailsFocus, scroll:S.descScroll, browse:S.browseIndex, nav:S.navIndex, landmark:S.landmark, footer:S.footer==null?null:S.footer, section:S.section.type, theme:S.theme}}; })()"""
VEC = {"right": (1, 0), "left": (-1, 0), "down": (0, 1), "up": (0, -1)}
KEYS = {"up": ("ArrowUp", 38), "down": ("ArrowDown", 40), "left": ("ArrowLeft", 37), "right": ("ArrowRight", 39)}
DPAD = {"up": 12, "down": 13, "left": 14, "right": 15}
STICK = {"up": (1, -1.0), "down": (1, 1.0), "left": (0, -1.0), "right": (0, 1.0)}
THEMES = ("arcade-grid", "box-art-showcase", "featured-shelf", "adventure-map")

def judge(theme, d, b, a):
    s0, s1 = b["st"], a["st"]; f0, f1 = b["focus"], a["focus"]
    moved = json.dumps(s0, sort_keys=True) != json.dumps(s1, sort_keys=True)
    if not moved: return True, "edge (nothing moved)"
    if not (f0 and f1): return False, "no highlight drawn"
    cen = lambda bx: ((bx[0] + bx[2]) / 2, (bx[1] + bx[3]) / 2); vx, vy = VEC[d]
    # one frame: where the new highlight's element was drawn before the press, or where the old one is drawn after it
    pair = None
    if f0["grp"] == f1["grp"]:
        if f1["key"] in b["items"] and f1["key"] != f0["key"]: pair = (b["items"][f0["key"]] if f0["key"] in b["items"] else f0["box"], b["items"][f1["key"]], b["items"])
        elif f0["key"] in a["items"] and f1["key"] != f0["key"]: pair = (a["items"][f0["key"]], f1["box"], a["items"])
    if pair:
        (x0, y0), (x1, y1) = cen(pair[0]), cen(pair[1]); dx, dy = x1 - x0, y1 - y0; along = dx * vx + dy * vy
        if d in ("left", "right"): return along > 0 and abs(dy) < along, f"drawn at ({dx:+.0f},{dy:+.0f})"
        ahead = [cen(bx)[1] for bx in pair[2].values() if (cen(bx)[1] - y0) * vy > 4]; nexty = min(ahead, key=lambda y: abs(y - y0)) if ahead else y1
        rowx = [cen(bx)[0] for bx in pair[2].values() if abs(cen(bx)[1] - y1) < 8]; near = min(abs(x - x0) for x in rowx) if rowx else 0
        return along > 0 and abs(y1 - nexty) < 8 and abs(x1 - x0) <= near + 1, f"drawn at ({dx:+.0f},{dy:+.0f}), nearest in its row {near:.0f}" + ("" if abs(y1 - nexty) < 8 else ", skipped the row drawn next")
    (x0, y0), (x1, y1) = cen(f0["box"]), cen(f1["box"]); dx, dy = x1 - x0, y1 - y0; along = dx * vx + dy * vy; across = abs(dx * vy) + abs(dy * vx)
    if abs(dx) < 2 and abs(dy) < 2:
        if s1["scroll"] != s0["scroll"]: return (s1["scroll"] - s0["scroll"]) * (1 if d == "down" else -1) > 0, "description scrolled"
        if f0["key"] == f1["key"]: return True, "same element (state only)"
        return False, "the selection changed but nothing drawn moved"
    if theme == "arcade-grid" and s0["zone"] == s1["zone"] == "games" and s0["index"] // 16 != s1["index"] // 16 and d in ("up", "down"):
        return abs(dx) < 4 and (s1["index"] - s0["index"]) * (1 if d == "down" else -1) > 0, "page turn keeps the column"
    other = s1["zone"] != s0["zone"] or s1["footer"] != s0["footer"] or f0["grp"] != f1["grp"]
    return along > 0 and (other or across <= along * 1.2 + 4), f"moved ({dx:+.0f},{dy:+.0f})" + (" into another area" if other else "")

NO_PAUSE = ["--disable-renderer-backgrounding", "--disable-background-timer-throttling", "--disable-backgrounding-occluded-windows", "--disable-features=CalculateNativeWinOcclusion"]   # the HOME reads controllers on animation frames: never paused here

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); a = ap.parse_args()
    srv = serve(a.site); url = f"http://127.0.0.1:{srv.server_address[1]}/index.html"; res, rows = [], []
    def ok(n, c, d=""):
        res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:400]})" if d and not c else ""), flush=True)
    try:
        with EdgeSession(profile=tempfile.mkdtemp(prefix="ccs-dirs-"), width=1440, height=1000, extra_args=NO_PAUSE) as ed:
            ed.navigate(url); ed.wait_for(READY, timeout=120); ed.js(CALM)
            ed.wait_for("CS.starter&&['done','failed'].includes(CS.starter.state)", timeout=120)
            ed.call("Emulation.setFocusEmulationEnabled", {"enabled": True})   # headless Edge often reports no window focus; the HOME reads controllers only with focus (as a real window has)
            ed.js("csNew('Directions'); 1"); time.sleep(0.5); ed.js(PAD)
            ed.js("openTv(); 1"); ed.wait_for("!!(TV.open&&TV.model)", timeout=15); time.sleep(0.6)
            ed.js("(()=>{ window.__acts=[]; const p=tvPress; tvPress=function(a){ window.__acts.push(a); return p.apply(this, arguments); }; })(); 1")
            ok("setup: the HOME shows the 30 built-in games (a new, empty collection) and a standard controller is connected",
               ed.js("TV.model.games.length") == 30 and ed.js("navigator.getGamepads()[0].mapping") == "standard", ed.js("TV.model.games.length"))
            ed.js("(()=>{ for(let k=0;k<5;k++){ const f=homeFolderCreate(TV.model,'Folder '+(k+1)); for(let j=0;j<3;j++) homeFolderAddGame(TV.model,f.id,TV.model.order[k*3+j]); } tvDraw(); })(); 1")
            def stage_focus(): ed.js("tpStage().focus({preventScroll:true}); 1")
            def raw_press(how, d):
                if how == "key":
                    k, vk = KEYS[d]; stage_focus()
                    ed.call("Input.dispatchKeyEvent", {"type": "rawKeyDown", "key": k, "code": k, "windowsVirtualKeyCode": vk})
                    ed.call("Input.dispatchKeyEvent", {"type": "keyUp", "key": k, "code": k, "windowsVirtualKeyCode": vk})
                else:   # the controller: held until the HOME takes the press, released once; then until the HOME has let go
                    n0 = ed.js("window.__acts.length")
                    if how == "dpad": i = DPAD[d]; ed.js("(()=>{ const p=window.__pad; p.buttons[%d]={pressed:true,touched:true,value:1}; p.timestamp++; })(); 1" % i)
                    else: ax, v = STICK[d]; ed.js("(()=>{ const p=window.__pad; p.axes[%d]=%s; p.timestamp++; })(); 1" % (ax, v))
                    for _ in range(60):
                        if ed.js("window.__acts.length") > n0: break
                        time.sleep(0.03)
                    if how == "dpad": ed.js("(()=>{ const p=window.__pad; p.buttons[%d]={pressed:false,touched:false,value:0}; p.timestamp++; })(); 1" % i)
                    else: ed.js("(()=>{ const p=window.__pad; p.axes=[0,0,0,0]; p.timestamp++; })(); 1")
                    for _ in range(60):
                        if ed.js("!TV.held"): break
                        time.sleep(0.03)
                time.sleep(0.25)
            # ---- raw input: one direction per press, for every raw source; a diagonal stick is one move along its stronger axis ----
            ed.js("TV.S.index=5; TV.S.zone='games'; tvDraw(); 1"); stage_focus(); time.sleep(0.3)
            raw = []
            for how in ("key", "dpad", "stick"):
                for d in ("right", "left", "down", "up"):
                    ed.js("window.__acts=[]; 1"); raw_press(how, d); got = ed.js("window.__acts.slice()")
                    raw.append({"raw": how, "direction": d, "logical": got})
            ok("raw: Arrow keys, D-pad buttons 12-15 and the stick each reach the HOME as exactly that one direction",
               all(r["logical"] == [r["direction"]] for r in raw), [r for r in raw if r["logical"] != [r["direction"]]])
            ed.js("window.__acts=[]; window.__pad.axes=[0.8,0.6,0,0]; window.__pad.timestamp++; 1"); time.sleep(0.15)
            ed.js("window.__pad.axes=[0,0,0,0]; window.__pad.timestamp++; 1"); time.sleep(0.25); diag = ed.js("window.__acts.slice()")
            ok("raw: a diagonal stick (right 0.8, down 0.6) makes one move along its stronger axis (right), not two", diag == ["right"], diag)
            rows.append({"raw_table": raw, "diagonal": diag})
            # ---- the screens, every theme, keyboard and controller ----
            def run(theme, how, screen, seq, setup=None):
                if setup: ed.js(setup + "; tvDraw(); 1"); time.sleep(0.3)
                out = []
                for d in seq:
                    b = ed.js(SNAP); ed.js("window.__acts=[]; 1"); raw_press(how, d); a_ = ed.js(SNAP); acts = ed.js("window.__acts.slice()")
                    v, why = judge(theme, d, b, a_)
                    out.append({"theme": theme, "screen": screen, "raw": how, "direction": d, "logical": acts, "from": [b["focus"] and b["focus"]["key"], b["focus"] and [round(x) for x in b["focus"]["box"]], b["st"]],
                                "to": [a_["focus"] and a_["focus"]["key"], a_["focus"] and [round(x) for x in a_["focus"]["box"]], a_["st"]], "pass": v and acts == [d], "why": why})
                    if not (v and acts == [d]): print(f"   {theme} {screen} {how} {d}: {out[-1]['from'][:2]} -> {out[-1]['to'][:2]} {why} acts={acts}", flush=True)
                rows.extend(out); return out
            for t in THEMES:
                ed.js("TV.S=homeInitialState(%s); TV.S.theme=%s; tvDraw(); 1" % (json.dumps(t), json.dumps(t))); time.sleep(0.5)
                per = {}
                for how in ("key", "dpad"):
                    res_how = []
                    res_how += run(t, how, "games", ["right", "right", "down", "left", "left", "down", "right", "up", "up", "up", "up", "left", "right", "right", "down", "down", "down", "down", "down", "up"],
                                   "TV.S=homeInitialState(%s); TV.S.index=5" % json.dumps(t))
                    res_how += run(t, how, "details", ["right", "right", "left", "down", "up", "up", "down", "right", "left", "left"],
                                   "TV.S=homeInitialState(%s); TV.S.index=2; TV.S.overlay={type:'details',id:TV.model.order[2],from:{zone:'games'}}; TV.S.detailsFocus='play'; TV.S.descScroll=0" % json.dumps(t))
                    res_how += run(t, how, "start menu", ["down", "down", "up", "down"], "TV.S=homeInitialState(%s); TV.S.overlay={type:'start',index:0}" % json.dumps(t))
                    res_how += run(t, how, "theme picker", ["up", "down", "left", "right", "right", "left"],
                                   "TV.S=homeInitialState(%s); TV.S.overlay={type:'theme',index:1,below:null}" % json.dumps(t))
                    res_how += run(t, how, "clear history", ["up", "down", "right", "up", "left", "down"],
                                   "TV.S=homeInitialState(%s); TV.S.section={type:'history'}; TV.S.zone='games'; TV.S.overlay={type:'confirm',what:'clear-history',index:0,text:'Clear Play History?'}" % json.dumps(t))
                    res_how += run(t, how, "browse", ["right", "right", "down", "down", "right", "up", "up", "left", "down", "down", "down", "up", "up", "up", "up"],
                                   "TV.S=homeInitialState(%s); TV.S.section={type:'browse'}; TV.S.zone='browse'; TV.S.browseIndex=0" % json.dumps(t))
                    per[how] = res_how
                for screen in ("games", "details", "start menu", "theme picker", "clear history", "browse"):
                    bad = [(r["raw"], r["direction"], r["why"]) for how in per for r in per[how] if r["screen"] == screen and not r["pass"]]
                    ok(f"{t}: {screen}: the highlight moves to what is drawn in the pressed direction (keyboard and controller)", not bad, bad[:4])
                same = [(x["screen"], x["direction"]) for x, y in zip(per["key"], per["dpad"]) if json.dumps(x["to"][2], sort_keys=True) != json.dumps(y["to"][2], sort_keys=True)]
                ok(f"{t}: the keyboard and the controller make the same moves on every screen", not same, same[:4])
                up_down = [r for how in per for r in per[how] if r["screen"] == "theme picker" and r["direction"] in ("up", "down")]
                ok(f"{t}: the theme picker (one row) and the Clear History answers ignore Up and Down",
                   all(r["from"][2]["oindex"] == r["to"][2]["oindex"] for r in up_down) and
                   all(r["from"][2]["oindex"] == r["to"][2]["oindex"] for how in per for r in per[how] if r["screen"] == "clear history" and r["direction"] in ("up", "down")), [r["to"][2]["oindex"] for r in up_down])
            ok("no page errors", not ed.exceptions, ed.exceptions[:2])
    finally:
        srv.shutdown()
    fails = [n for n, c in res if not c]
    print(f"\n{len(res) - len(fails)}/{len(res)} PASS" + (f"; FAIL: {', '.join(fails)}" if fails else ""))
    if a.out: json.dump({"results": [{"name": n, "pass": c} for n, c in res], "steps": rows}, open(a.out, "w", encoding="utf-8"), indent=1, default=str)
    return 0 if not fails else 1

if __name__ == "__main__": sys.exit(main())
