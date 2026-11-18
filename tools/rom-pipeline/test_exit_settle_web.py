r"""A game's Exit chosen with a CONTROLLER ends the game and stays ended (the built website, headless Edge, an Included Game, isolated
storage). The browser has no real controller here: a standard-layout pad (an Xbox-compatible one) is supplied to the page through
navigator.getGamepads, in the window that shows the HOME (the page, or the pop-out). Mouse and keyboard Exit are covered elsewhere.
  embedded / popout   A on the HOME starts the highlighted game; the controller's menu button opens the Game Menu; Down twice reaches
                      Exit Without Saving; A is pressed and HELD while the game ends, then released. The game stays ended: no new game
                      start, the session is gone, the HOME is shown and has focus in the window that showed the game.
  held at start       a game started while A is still held (from the HOME): its first screen's choice is not taken until A is released.
  python test_exit_settle_web.py <built site folder> [--out results.json]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, json, os, sys, tempfile, time, urllib.request
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from studio_harness import EdgeSession, READY, CALM, serve
from cdp_edge import EdgeSession as BaseEdge
from websocket_min import WS

PAD = ("(w=>{ if(!w.__pad){ w.__pad={index:0, id:'Xbox 360 Controller (XInput STANDARD GAMEPAD Vendor: 045e Product: 028e)', mapping:'standard', "
       "connected:true, timestamp:1, buttons:Array.from({length:17},()=>({pressed:false,touched:false,value:0})), axes:[0,0,0,0]}; } "
       "Object.defineProperty(w.navigator,'getGamepads',{configurable:true,value:()=>[window.__pad]}); return true; })(%s)")

NO_PAUSE = ["--disable-renderer-backgrounding", "--disable-background-timer-throttling", "--disable-backgrounding-occluded-windows", "--disable-features=CalculateNativeWinOcclusion"]   # the HOME reads controllers on animation frames: never paused here

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); a = ap.parse_args()
    srv = serve(a.site); url = f"http://127.0.0.1:{srv.server_address[1]}/index.html"; res, log = [], {}
    def ok(n, c, d=""):
        res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:300]})" if d and not c else ""), flush=True)
    def btn(ed, i, on):
        ed.js("(()=>{ const p=window.__pad; p.buttons[%d]={pressed:%s,touched:%s,value:%d}; p.timestamp++; return 1; })()" % (i, "true" if on else "false", "true" if on else "false", 1 if on else 0))
    def tap(ed, i, hold=0.15): btn(ed, i, True); time.sleep(hold); btn(ed, i, False); time.sleep(0.3)
    def state(ed): return ed.js("({webGame:TP.webGame, session:!!CPL.s, view:CCSPLAY.view(), open:TV.open, launches:TV.S.launchCount||0, starts:CP_TRACE.filter(x=>x.ev==='game_start').length, chooses:CP_TRACE.filter(x=>x.ev==='choose').map(x=>x.act), menu:CPL.s&&CPL.s.lastMenu?CPL.s.lastMenu.title:null, popped:!!(TP.pop&&!TP.pop.closed)})")
    def attach_popup(ed):
        tabs = json.load(urllib.request.urlopen(f"http://127.0.0.1:{ed.port}/json", timeout=10))
        t = next(x for x in tabs if x.get("type") == "page" and x.get("title", "").startswith("HOME"))
        pop = BaseEdge(width=1280, height=760); pop.ws = WS(t["webSocketDebuggerUrl"]); pop.ws.s.settimeout(60)
        pop.call("Page.enable"); pop.call("Runtime.enable"); pop.call("Page.bringToFront"); pop.call("Emulation.setFocusEmulationEnabled", {"enabled": True}); return pop
    try:
        with EdgeSession(profile=tempfile.mkdtemp(prefix="ccs-exit-"), width=1440, height=1000, extra_args=NO_PAUSE) as ed:
            ed.navigate(url); ed.wait_for(READY, timeout=120); ed.js(CALM)
            ed.wait_for("CS.starter&&['done','failed'].includes(CS.starter.state)", timeout=120)
            ed.call("Emulation.setFocusEmulationEnabled", {"enabled": True})   # headless Edge often reports no window focus; the HOME reads controllers only with focus (as a real window has)
            gid = ed.js("[...sel][0]"); ed.js(PAD % "window")
            ok("setup: an Included Game in the collection and a standard controller in the page", bool(gid) and ed.js("navigator.getGamepads()[0].mapping") == "standard", gid)
            for mode in ("embedded", "popout"):
                target = ed
                ed.js("openTv({focus:%s}); 1" % json.dumps(gid)); ed.wait_for("TV.open", timeout=15); time.sleep(0.6)
                if mode == "popout":
                    ed.call("Runtime.evaluate", {"expression": "tpPopOut();true", "userGesture": True, "returnByValue": True})
                    ed.wait_for("!!TP.pop&&!TP.pop.closed", timeout=15); ed.js(PAD % "TP.pop"); target = attach_popup(ed); time.sleep(0.8)
                else: ed.call("Page.bringToFront")
                ed.js("CP_TRACE.splice(0); 1"); before = state(ed)
                tap(ed, 0)   # A on the HOME: starts the highlighted game (the pad object lives in the page; the pop-out reads the same one)
                ed.wait_for("!!CPL.s&&CCSPLAY.view()>=0", timeout=90); time.sleep(0.8)
                if ed.js("CCSPLAY.view()") != 1: tap(ed, 0); time.sleep(0.5)   # a first screen: take its choice (A)
                ed.wait_for("CCSPLAY.view()===1", timeout=20)
                tap(ed, 16)   # the controller's menu button: the Game Menu
                ed.wait_for("CPL.s&&CPL.s.lastMenu&&CPL.s.lastMenu.title==='Game Menu'", timeout=15)
                tap(ed, 13); tap(ed, 13)   # Down twice: Save to Slot, then Exit Without Saving (Load, disabled, is skipped)
                log[mode + "_menu"] = ed.js("CPL.s&&CPL.s.lastMenu")
                started = state(ed)["starts"]
                btn(ed, 0, True)   # A, held while the game ends
                try: ed.wait_for("!CPL.s", timeout=15)
                except Exception: pass
                time.sleep(0.6); btn(ed, 0, False); time.sleep(1.5)
                after = state(ed); log[mode] = {"before": before, "after": after, "trace": ed.js("CP_TRACE.slice(-30)")}
                ok(f"{mode}: A on Exit Without Saving ends the game (choose 7) and, still held when the HOME returns, does not start it again",
                   (not after["session"]) and after["webGame"] is False and after["view"] == -1 and 7 in after["chooses"] and after["starts"] == started and after["open"], after)
                focus = ed.js("(()=>{ const w=TP.pop&&!TP.pop.closed?TP.pop:window, st=TP.pop&&!TP.pop.closed?TP.popStage:tpStage(); return w.document.activeElement===st; })()")
                ok(f"{mode}: the HOME has focus in the window that showed the game", focus is True, focus)
                tap(ed, 2)   # nothing else happens later on the next press of another button (B is Back on the HOME)
                ok(f"{mode}: no game is running afterwards", not ed.js("!!CPL.s"), state(ed))
                if mode == "popout":
                    try: target.ws.s.close()
                    except Exception: pass
                    ed.js("tpPopBack(); 1"); ed.call("Page.bringToFront"); time.sleep(0.6)
            # ---- held at start: a game started while A is held takes no choice until A is released ----
            ed.js("openTv({focus:%s}); CP_TRACE.splice(0); 1" % json.dumps(gid)); time.sleep(0.6)
            ed.js("tpWebLaunch(%s); 1" % json.dumps(gid)); btn(ed, 0, True)
            ed.wait_for("!!CPL.s&&CCSPLAY.view()>=0", timeout=90); time.sleep(1.0)
            held = ed.js("({chooses:CP_TRACE.filter(x=>x.ev==='choose').length, view:CCSPLAY.view(), routed:CPL.s?Array.from(new Uint8Array(CPL.s.mem.buffer, CPL.s.x.web_routed(), 8)):null})")
            btn(ed, 0, False); time.sleep(0.4)
            ok("held at start: with A held from before the game opened, nothing is chosen and A does not reach the game until it is released",
               held["chooses"] == 0 and (held["routed"] is None or held["routed"][0] == 0), held)
            log["held_at_start"] = held
            if ed.js("!!CPL.s"):
                ed.js("CCSPLAY.view()===1&&(CPL.s.menuKey=1); 1"); time.sleep(0.5)
            ok("no page errors", not ed.exceptions, ed.exceptions[:2])
    finally:
        srv.shutdown()
    fails = [n for n, c in res if not c]
    print(f"\n{len(res) - len(fails)}/{len(res)} PASS" + (f"; FAIL: {', '.join(fails)}" if fails else ""))
    if a.out: json.dump({"results": [{"name": n, "pass": c} for n, c in res], "log": log}, open(a.out, "w", encoding="utf-8"), indent=1, default=str)
    return 0 if not fails else 1

if __name__ == "__main__": sys.exit(main())
