"""Search visibility in Preview on TV, every theme, at 1280 x 720: results, suggestions and the keyboard must all be
visible (not merely present in the DOM). For each theme: one result, many results (focused row stays inside the results
zone while moving down through the list), no results (message visible); each state is screenshotted.
  python test_tv_search.py [--url http://127.0.0.1:8899/nes-catalog.html] [--out logs/home-review/search]"""
from __future__ import annotations
import os, sys, json, time, subprocess, tempfile, shutil, base64, urllib.request, argparse
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from shot_mobile import WS, EDGE
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
THEMES = ["arcade-grid", "box-art-showcase", "featured-shelf", "adventure-map"]
ONE = "adventures in the magic kingdom"; MANY = "a"; NONE = "zzqqxxjj"
VIS = """(()=>{const sh=$('tvHome').shadowRoot; const hm=sh.querySelector('.hm').getBoundingClientRect(); const k=1280/hm.width; const R=el=>{const r=el.getBoundingClientRect(); return [(r.left-hm.left)*k,(r.top-hm.top)*k,(r.right-hm.left)*k,(r.bottom-hm.top)*k];};
 const inside=(a,b,tol)=>a[0]>=b[0]-tol&&a[1]>=b[1]-tol&&a[2]<=b[2]+tol&&a[3]<=b[3]+tol; const onStage=a=>inside(a,[0,0,1280,720],1);
 const zr=sh.querySelector('.search .zone.results'), zs=sh.querySelector('.search .zone.suggest'), zk=sh.querySelector('.search .zone.keys'); if(!zr||!zs||!zk) return {err:'zones missing'};
 const zrB=R(zr), zsB=R(zs), zkB=R(zk); const rows=[...sh.querySelectorAll('.search .res .r')]; const vis=rows.map(r=>R(r)).filter(b=>inside(b,zrB,1)&&b[3]-b[1]>40);
 const foc=sh.querySelector('.search .res .r.focus'); const focB=foc?R(foc):null; const keys=[...sh.querySelectorAll('.search .kb .k')].map(R); const sug=[...sh.querySelectorAll('.search .sug span')].map(R);
 const nores=sh.querySelector('.search .nores'); const q=sh.querySelector('.search .q');
 return {zones:[zrB,zsB,zkB].map(onStage), zonesSeparate:zrB[3]<=zsB[1]+1&&zsB[3]<=zkB[1]+1, rows:rows.length, visible:vis.length, focusVisible:focB?inside(focB,zrB,1):null, focusIndex:foc?+foc.dataset.i:null,
  keys:keys.length, keysVisible:keys.filter(b=>inside(b,zkB,1)&&b[3]-b[1]>18).length, sug:sug.length, sugVisible:sug.filter(b=>inside(b,zsB,1)).length, nores:nores?inside(R(nores),zrB,1)&&nores.textContent.trim().length>0:false, query:q?q.textContent.trim():'', resH:zrB[3]-zrB[1]}; })()"""

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--url", default="http://127.0.0.1:8899/nes-catalog.html"); ap.add_argument("--out", default=os.path.join(ROOT, "logs", "home-review", "search")); a = ap.parse_args(); os.makedirs(a.out, exist_ok=True)
    checks = []; ok = lambda k, v, n="": checks.append(f"{k}={'true' if v else 'false'}{(' ('+str(n)[:300]+')') if n and not v else ''}")
    prof = tempfile.mkdtemp(prefix="nes-search-"); port = 9449; errors = []
    p = subprocess.Popen([EDGE, "--headless=new", "--disable-gpu", f"--user-data-dir={prof}", f"--remote-debugging-port={port}", "--hide-scrollbars", "about:blank"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        for _ in range(80):
            try: tabs = json.load(urllib.request.urlopen(f"http://127.0.0.1:{port}/json", timeout=1)); break
            except Exception: time.sleep(0.25)
        ws = WS(next(t["webSocketDebuggerUrl"] for t in tabs if t["type"] == "page")); mid = [0]
        def call(m, params=None):
            mid[0] += 1; ws.send(json.dumps({"id": mid[0], "method": m, "params": params or {}}))
            while True:
                r = json.loads(ws.recv())
                if r.get("method") == "Runtime.exceptionThrown": errors.append(r["params"]["exceptionDetails"].get("exception", {}).get("description", "")[:300])
                if r.get("id") == mid[0]: return r
        def js(e): return call("Runtime.evaluate", {"expression": e, "returnByValue": True, "awaitPromise": True}).get("result", {}).get("result", {}).get("value")
        def shot(name):
            r = js("(()=>{const b=$('tvHome').shadowRoot.querySelector('.hm').getBoundingClientRect();return [b.x,b.y,b.width,b.height]})()")
            d = call("Page.captureScreenshot", {"format": "png", "clip": {"x": r[0], "y": r[1], "width": 1280, "height": 720, "scale": 1}})["result"]["data"]; open(os.path.join(a.out, name), "wb").write(base64.b64decode(d))
        call("Page.enable"); call("Runtime.enable"); call("Emulation.setDeviceMetricsOverride", {"width": 1600, "height": 1000, "deviceScaleFactor": 1, "mobile": False}); call("Page.navigate", {"url": a.url})
        for _ in range(160):
            if js("document.documentElement.dataset.ready==='1'") is True: break
            time.sleep(0.25)
        js("try{doneWelcome()}catch(e){} localStorage.removeItem('studio-home-v1'); sel.clear(); DATA.games.filter(g=>g.art).slice(0,120).forEach(g=>sel.add(g.game_id)); persist(); render()"); time.sleep(0.5)
        js("openTv()"); time.sleep(0.9); js("(()=>{const st=$('tvStage'); st.style.position='fixed'; st.style.left='0px'; st.style.top='0px'; st.style.width='1280px'; st.style.height='720px'; st.style.zIndex='99999'; homeFit($('tvHome'));})()"); time.sleep(0.3)
        for theme in THEMES:
            js(f"TV.S.theme='{theme}'; TV.model.theme='{theme}'; TV.S.section={{type:'search'}}; TV.S.zone='keyboard'; TV.S.overlay=null; TV.S.query=''; TV.S.keyRow=0; TV.S.keyCol=0; TV.S.resultIndex=0; tvDraw()"); time.sleep(0.6)
            v = js(VIS); ok(f"{theme}_empty_query_three_zones_visible", v and all(v["zones"]) and v["zonesSeparate"] and v["keysVisible"] == 39 and v["keys"] == 39, v)
            js(f"TV.S.query={json.dumps(ONE)}; TV.S.resultIndex=0; tvDraw()"); time.sleep(0.5); v = js(VIS); shot(f"search-{theme}-one.png")
            ok(f"{theme}_one_result_visible", v and v["rows"] == 1 and v["visible"] == 1 and v["sug"] >= 1 and v["sugVisible"] == v["sug"] and all(v["zones"]), v)
            js(f"TV.S.query={json.dumps(MANY)}; TV.S.resultIndex=0; tvDraw()"); time.sleep(0.5); v = js(VIS); shot(f"search-{theme}-many.png")
            ok(f"{theme}_many_results_visible", v and v["rows"] >= 12 and v["visible"] >= 8 and v["keysVisible"] == 39 and v["resH"] >= 150, v)
            # move into the results and walk down: the focused row must stay inside the results zone every step
            js("TV.S.zone='results'; TV.S.resultIndex=0; tvDraw()"); time.sleep(0.2); steps = []; good = True
            for i in range(8):
                js("tvAct('down')"); time.sleep(0.2); v = js(VIS); steps.append((v["focusIndex"], v["focusVisible"]))
                if not v["focusVisible"]: good = False
            shot(f"search-{theme}-many-scrolled.png"); ok(f"{theme}_focused_result_stays_visible_while_scrolling", good and steps[-1][0] >= 8, steps)
            js(f"TV.S.zone='keyboard'; TV.S.query={json.dumps(NONE)}; TV.S.resultIndex=0; tvDraw()"); time.sleep(0.5); v = js(VIS); shot(f"search-{theme}-none.png")
            ok(f"{theme}_no_results_message_visible", v and v["rows"] == 0 and v["nores"] and v["keysVisible"] == 39, v)
        ok("no_js_errors", not errors, errors[:3])
    finally:
        try: call("Browser.close")
        except Exception: pass
        try: p.wait(timeout=10)
        except Exception: p.kill()
        shutil.rmtree(prof, ignore_errors=True)
    bad = [c for c in checks if "=false" in c]; print("\n".join(checks)); print(f"RESULT: {'PASS' if not bad else 'FAIL ' + str(bad)} ({len(checks)} checks)"); return 1 if bad else 0

if __name__ == "__main__": sys.exit(main())
