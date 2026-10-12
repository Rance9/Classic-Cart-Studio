"""Hero art proof of concept: import finished widescreen images and render them in the actual Featured Shelf runtime.

  python hero_demo.py <folder-with-<game_id>.png|jpg> [--url http://127.0.0.1:8899/nes-catalog.html] [--out logs/home-review/hero-poc]

Steps: hero_art.import for every file in the folder, rebuild the page, open Preview on TV (every catalog game selected),
focus each imported game in the Featured Shelf, capture 1280 x 720, and write per game: the full screenshot, the hero
region at 2x beside the master's hero region (03-hero-vs-master-<id>.png), plus a sheet of all heroes. Nothing here
rolls hero art out to other games; games without imported art keep the fallback (NOT READY FOR VISUAL SIGNOFF).
"""
from __future__ import annotations
import os, sys, json, time, subprocess, tempfile, shutil, base64, urllib.request, argparse
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from shot_mobile import WS, EDGE
from PIL import Image, ImageDraw, ImageFont
import hero_art
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ASSETS = os.path.join(ROOT, "tools", "publish", "assets", "home")

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("folder"); ap.add_argument("--url", default="http://127.0.0.1:8899/nes-catalog.html"); ap.add_argument("--out", default=os.path.join(ROOT, "logs", "home-review", "hero-poc")); ap.add_argument("--no-build", action="store_true"); a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True); cat, by = hero_art.games(); ids = []
    for name in sorted(os.listdir(a.folder)):
        gid, ext = os.path.splitext(name)
        if ext.lower() in (".png", ".jpg", ".jpeg", ".webp") and gid in by:
            hero_art.import_one(gid, os.path.join(a.folder, name), cat, by); ids.append(gid); print("imported", gid, by[gid].get("display_title"))
    if not ids: print("no <game_id>.<ext> files found for catalog games in", a.folder); return 2
    json.dump(cat, open(os.path.join(hero_art.REPORTS, "catalog.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    if not a.no_build: subprocess.run([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)), "build_html.py")], check=True, stdout=subprocess.DEVNULL)
    art = json.load(open(os.path.join(ASSETS, "home-art.json"), encoding="utf-8")); hb = art["themes"]["featured-shelf"]["geometry"]["hero"]["frame"]
    master = Image.open(os.path.join(ASSETS, art["themes"]["featured-shelf"]["reference"])).convert("RGB").crop(tuple(hb))
    prof = tempfile.mkdtemp(prefix="nes-heropoc-"); port = 9453; errors = []
    p = subprocess.Popen([EDGE, "--headless=new", "--disable-gpu", f"--user-data-dir={prof}", f"--remote-debugging-port={port}", "--hide-scrollbars", "about:blank"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    shots = []
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
        def wait_images(secs=12):
            t0 = time.time()
            while time.time() - t0 < secs:
                if js("(()=>{const sh=$('tvHome').shadowRoot; return [...sh.querySelectorAll('img')].filter(i=>!i.complete||!i.naturalWidth).length})()") == 0: break
                time.sleep(0.2)
            time.sleep(0.4)
        call("Page.enable"); call("Runtime.enable"); call("Emulation.setDeviceMetricsOverride", {"width": 1600, "height": 1000, "deviceScaleFactor": 1, "mobile": False}); call("Page.navigate", {"url": a.url})
        for _ in range(160):
            if js("document.documentElement.dataset.ready==='1'") is True: break
            time.sleep(0.25)
        js("try{doneWelcome()}catch(e){} localStorage.removeItem('studio-home-v1'); sel.clear(); DATA.games.forEach(g=>sel.add(g.game_id)); persist(); render()"); time.sleep(0.6)
        js("openTv()"); time.sleep(1.0); js("(()=>{const st=$('tvStage'); st.style.position='fixed'; st.style.left='0px'; st.style.top='0px'; st.style.width='1280px'; st.style.height='720px'; st.style.zIndex='99999'; homeFit($('tvHome'));})()"); time.sleep(0.3)
        js("TV.S.theme='featured-shelf'; TV.model.theme='featured-shelf'; TV.S.section={type:'allgames'}; TV.S.zone='games'; TV.S.overlay=null; tvDraw()"); time.sleep(0.8)
        for gid in ids:
            k = js(f"(()=>{{const ids=homeListing(TV.model,TV.S,tvSearch()).ids; const k=ids.findIndex(id=>id.endsWith('{gid}')); if(k>=0){{TV.S.index=k; tvDraw();}} return k;}})()"); time.sleep(0.6); wait_images()
            r = js("(()=>{const b=$('tvHome').shadowRoot.querySelector('.hm').getBoundingClientRect();return [b.x,b.y,b.width,b.height]})()")
            d = call("Page.captureScreenshot", {"format": "png", "clip": {"x": r[0], "y": r[1], "width": 1280, "height": 720, "scale": 1}})["result"]["data"]
            full = os.path.join(a.out, f"01-featured-shelf-{gid}.png"); open(full, "wb").write(base64.b64decode(d)); shots.append((gid, full))
            uses = js("(()=>{const sh=$('tvHome').shadowRoot; return !!sh.querySelector('.hero img.wide')})()"); print(gid, "index", k, "hero override in use:", uses)
    finally:
        try: call("Browser.close")
        except Exception: pass
        try: p.wait(timeout=10)
        except Exception: p.kill()
        shutil.rmtree(prof, ignore_errors=True)
    f = ImageFont.truetype("C:/Windows/Fonts/consola.ttf", 18) if os.path.exists("C:/Windows/Fonts/consola.ttf") else ImageFont.load_default()
    sheet = Image.new("RGB", (1280 + 16, len(shots) * (720 + 34)), (12, 12, 14)); ds = ImageDraw.Draw(sheet)
    for i, (gid, full) in enumerate(shots):
        im = Image.open(full).convert("RGB"); hero = im.crop(tuple(hb)); w, h = hero.size
        pair = Image.new("RGB", (2 * w * 2 + 24, h * 2 + 40), (12, 12, 14)); d = ImageDraw.Draw(pair)
        pair.paste(master.resize((w * 2, h * 2), Image.LANCZOS), (0, 36)); pair.paste(hero.resize((w * 2, h * 2), Image.LANCZOS), (w * 2 + 24, 36))
        d.text((6, 8), "MASTER hero region (2x)", fill=(255, 255, 255), font=f); d.text((w * 2 + 30, 8), f"RUNTIME hero region (2x): {by[gid].get('display_title')}", fill=(255, 255, 255), font=f)
        pair.save(os.path.join(a.out, f"03-hero-vs-master-{gid}.png"))
        sheet.paste(im, (8, i * (720 + 34) + 30)); ds.text((10, i * (720 + 34) + 6), f"Featured Shelf · {by[gid].get('display_title')} · imported hero art", fill=(255, 255, 255), font=f)
    sheet.save(os.path.join(a.out, "02-all-heroes.png")); print("errors:", errors[:3] if errors else "none"); print("wrote", a.out)
    return 0

if __name__ == "__main__": sys.exit(main())
