"""Stage D visual review harness for the Console HOME themes (Preview on TV).

Drives the real catalog page in headless Edge, captures the actual renderer at exactly 1280 x 720 for every theme
and every required state, and writes one review packet per theme under <out>/<theme>/:
  01-master.png            the official master registered to 1280 x 720 (tools/publish/assets/home/ref-<theme>.png)
  02-runtime.png           the actual runtime screenshot of the comparable state (All Games, fixture data)
  03-side-by-side.png      master | runtime at equal pixel size, labeled
  04-overlay-50.png        50 % opacity overlay (component shifts show as doubled edges)
  05-diff-heatmap.png      per-pixel difference heat map (whole frame, informational)
  06-static-mask.png       the STATIC PRESERVE mask (white = compared strictly)
  07-static-diff.png       strict pixel diff inside the static mask, with the largest differing blobs boxed
  08-layer-diagnostic.png  asset layers: inpainted scene | sprites | runtime
  10..18-*.png             state captures: fixture default, actual catalog data, another game, Browse (custom folders +
                           categories), Search (keyboard, suggestions, results), Game Details, theme-specific state,
                           stress (long title, large library)
  geometry.json / verdicts.json / REVIEW.md
and <out>/REVIEW.md with the four-theme summary in the owner's review format.

The comparisons are made against the runtime, never against composites. Static regions are compared pixel-strictly
(mask = everything outside the manifest's DYNAMIC / LEGEND regions; a second mask also excludes the sprite copies the
renderer deliberately moves, such as the shelf focus frame or the walker). Dynamic regions get geometry and
presence checks. A verdict is PASS, FAIL or BLOCKED; nothing is "close enough". The owner's approval of the
screenshots is the acceptance gate; this harness only produces the evidence.

  python home_visual_review.py [--url http://127.0.0.1:8899/nes-catalog.html] [--out logs/home-review]
"""
from __future__ import annotations
import os, sys, json, time, subprocess, tempfile, shutil, base64, urllib.request, hashlib, argparse, glob
import numpy as np, cv2
from PIL import Image, ImageDraw, ImageFont
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from shot_mobile import WS, EDGE

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ASSETS = os.path.join(ROOT, "tools", "publish", "assets", "home")
MASTER_DIR = os.path.join(ROOT, "_temp", "images", "master mockup HOME themes")
THEMES = ["arcade-grid", "box-art-showcase", "featured-shelf", "adventure-map"]
TITLES = {"arcade-grid": "ARCADE GRID", "box-art-showcase": "BOX ART SHOWCASE", "featured-shelf": "FEATURED SHELF", "adventure-map": "ADVENTURE MAP"}
STATIC_FAIL_PCT = 1.0      # % of static-mask pixels differing by more than DIFF_T (any channel) that fails STATIC PRESERVE
DIFF_T = 32
GEO_TOL = 5                # px tolerance for measured element edges

def sha256(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""): h.update(b)
    return h.hexdigest()

def font(size):
    try: return ImageFont.truetype("C:/Windows/Fonts/consola.ttf", size)
    except Exception:
        try: return ImageFont.load_default(size=size)
        except Exception: return ImageFont.load_default()

# ---------------------------------------------------------------- browser driver
class Browser:
    def __init__(self, url):
        self.prof = tempfile.mkdtemp(prefix="nes-review-"); self.port = 9441; self.errors = []; self.mid = 0
        self.p = subprocess.Popen([EDGE, "--headless=new", "--disable-gpu", f"--user-data-dir={self.prof}", f"--remote-debugging-port={self.port}", "--hide-scrollbars", "about:blank"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        for _ in range(80):
            try: tabs = json.load(urllib.request.urlopen(f"http://127.0.0.1:{self.port}/json", timeout=1)); break
            except Exception: time.sleep(0.25)
        self.ws = WS(next(t["webSocketDebuggerUrl"] for t in tabs if t["type"] == "page"))
        self.call("Page.enable"); self.call("Runtime.enable")
        self.call("Emulation.setDeviceMetricsOverride", {"width": 1600, "height": 1000, "deviceScaleFactor": 1, "mobile": False})
        self.call("Page.navigate", {"url": url})
        for _ in range(160):
            if self.js("document.documentElement.dataset.ready==='1'") is True: break
            time.sleep(0.25)
    def call(self, m, params=None):
        self.mid += 1; self.ws.send(json.dumps({"id": self.mid, "method": m, "params": params or {}}))
        while True:
            r = json.loads(self.ws.recv())
            if r.get("method") == "Runtime.exceptionThrown": self.errors.append(r["params"]["exceptionDetails"].get("exception", {}).get("description", "")[:300])
            if r.get("id") == self.mid: return r
    def js(self, e):
        r = self.call("Runtime.evaluate", {"expression": e, "returnByValue": True, "awaitPromise": True}).get("result", {})
        if "exceptionDetails" in r: self.errors.append("eval: " + str(r["exceptionDetails"].get("exception", {}).get("description", ""))[:300])
        return r.get("result", {}).get("value")
    def wait_images(self, secs=8.0):
        t0 = time.time()
        while time.time() - t0 < secs:
            st = self.js("(()=>{const sh=$('tvHome').shadowRoot; const hm=sh.querySelector('.hm'); const im=[...sh.querySelectorAll('img')]; const bg=getComputedStyle(hm).backgroundImage; const u=bg.match(/url\(\"?([^\")]+)/); const probe=u?new Image():null; return {pending:im.filter(i=>!i.complete||!i.naturalWidth).length, n:im.length}})()")
            if st and st["pending"] == 0: break
            time.sleep(0.2)
        time.sleep(0.25)
    def shot(self, path):
        self.wait_images()
        r = self.js("(()=>{const b=$('tvHome').shadowRoot.querySelector('.hm').getBoundingClientRect();return [b.x,b.y,b.width,b.height]})()")
        d = self.call("Page.captureScreenshot", {"format": "png", "clip": {"x": r[0], "y": r[1], "width": 1280, "height": 720, "scale": 1}})["result"]["data"]
        open(path, "wb").write(base64.b64decode(d)); return path
    def act(self, seq, wait=0.35):
        for a in seq: self.js(f"tvAct('{a}')")
        time.sleep(wait)
    def close(self):
        try: self.call("Browser.close")
        except Exception: pass
        try: self.p.wait(timeout=10)
        except Exception: self.p.kill()
        shutil.rmtree(self.prof, ignore_errors=True)

FIXTURE = "sel.clear(); DATA.games.filter(g=>g.art).slice(0,34).forEach(g=>sel.add(g.game_id)); DATA.games.filter(g=>!g.art).slice(0,2).forEach(g=>sel.add(g.game_id)); persist(); render()"
ALL = "sel.clear(); DATA.games.forEach(g=>sel.add(g.game_id)); persist(); render()"
FOLDERS = """(()=>{const m=TV.model; const ids=m.order.slice(); if(m.folders.length) return m.folders.length;
 const f1=homeFolderCreate(m,'Role-Playing Picks',null); ids.slice(0,6).forEach(id=>homeFolderAddGame(m,f1.id,id));
 const f2=homeFolderCreate(m,'Saturday Morning Cartoon Adventures and Licensed Tie-Ins',null); ids.slice(6,14).forEach(id=>homeFolderAddGame(m,f2.id,id));
 const f3=homeFolderCreate(m,'Two Player',f1.id); ids.slice(2,5).forEach(id=>homeFolderAddGame(m,f3.id,id));
 m.map_shortcuts=[f1.id,f2.id]; try{homeExtrasSave()}catch(e){} return m.folders.length; })()"""
STAGE = "(()=>{const st=$('tvStage'); st.style.position='fixed'; st.style.left='0px'; st.style.top='0px'; st.style.width='1280px'; st.style.height='720px'; st.style.zIndex='99999'; homeFit($('tvHome'));})()"
MEASURE = """(()=>{const sh=$('tvHome').shadowRoot; const h=sh.querySelector('.hm').getBoundingClientRect(); const R=el=>{const r=el.getBoundingClientRect(); return [Math.round(r.left-h.left),Math.round(r.top-h.top),Math.round(r.right-h.left),Math.round(r.bottom-h.top)];};
 const q=(s,i)=>{const el=sh.querySelectorAll(s)[i||0]; return el?R(el):null;}; const out={};
 for(const [k,s,i] of SPEC) out[k]=q(s,i);
 out.__sprites=[...sh.querySelectorAll('img.sp, #hmWalker, .signglow')].map(R);
 out.__legend=[...sh.querySelectorAll('.legend')].map(e=>e.textContent.replace(/\\s+/g,' ').trim());
 out.__fonts=[...new Set([...sh.querySelectorAll('.hm *')].map(e=>getComputedStyle(e).fontFamily))];
 return out; })()"""

def open_tv(b, selection_js, theme):
    b.js("try{doneWelcome()}catch(e){} localStorage.removeItem('studio-home-v1'); " + selection_js); time.sleep(0.5)
    b.js("openTv()"); time.sleep(0.8); b.js(STAGE); time.sleep(0.3)
    b.js(FOLDERS)
    set_theme(b, theme)

def set_theme(b, theme):
    b.js(f"(()=>{{const i=new Image(); i.src='brand/home/scene-{theme}.png'; window.__warm=i;}})()")
    b.js(f"TV.S.theme='{theme}'; TV.model.theme='{theme}'; TV.S.section={{type:'allgames'}}; TV.S.zone='games'; TV.S.overlay=null; TV.S.index=0; TV.S.landmark=0; tvDraw()"); time.sleep(0.9); b.wait_images(12.0)

def state(b):
    return b.js("(()=>{const S=TV.S; return {theme:S.theme, section:S.section, zone:S.zone, index:S.index, landmark:S.landmark, overlay:S.overlay&&S.overlay.type, n:homeListing(TV.model,S,tvSearch()).ids.length, folders:TV.model.folders.length}})()")

# ---------------------------------------------------------------- measurement specs (label, selector, index, expected box or mode)
def specs(theme, G, art):
    s = []
    if theme == "arcade-grid":
        s += [("clock", ".clock", 0, G["clock"]), ("tab active plate (native sprite box)", "img.sp[src*='tab-active']", 0, art["themes"][theme]["sprites"]["tab-active"]["box_stage"]),
              ("card frame", "img.sp[src*='card-frame']", 0, G["card"]["frame"]), ("card caption", ".cap", 0, G["card"]["caption"]),
              ("grid cell 0", ".cell", 0, G["cells"][0]), ("grid cell 15", ".cell", 15, G["cells"][15]), ("legend", ".legend", 0, G["legend"])]
        rows = G["stats"].get("rows") if isinstance(G.get("stats"), dict) else None
        if rows: s.append(("stat row 0 (top-left)", ".stat", 0, ("tl", rows[0][:2])))
    elif theme == "box-art-showcase":
        c = next(e for e in G["envelopes"] if e["d"] == 0)
        s += [("clock", ".clock", 0, G["clock"]), ("top navigation", ".nav", 0, G["nav"]), ("focus box envelope (center x, height)", ".box3.c", 0, ("cxh", c["box"])),
              ("title", ".title", 0, G["title"]), ("blurb", ".blurb", 0, G["blurb"]), ("facts row", ".facts", 0, G["facts"]), ("collection panel heading", ".pnl.h", 0, G["panel"]["heading"]), ("dots", ".dots", 0, G["dots"])]
    elif theme == "featured-shelf":
        s += [("clock", ".clock", 0, G["clock"]), ("hero inner", ".hero", 0, G["hero"]["inner"]), ("info label", ".flabel", 0, G["info"]["label"]), ("info title", ".ftitle", 0, G["info"]["title"]),
              ("info row 0", ".frow", 0, G["info"]["rows"][0]), ("info text", ".ftext", 0, G["info"]["text"]), ("shelf cell 0", ".scell", 0, G["shelf"]["cells"][0]), ("legend", ".legend", 0, G["legend"])]
        if isinstance(G.get("side"), dict) and G["side"].get("plate"): s.append(("sidebar active plate", "img.sp[src*='side-active']", 0, G["side"]["plate"]))
    else:
        for i, l in enumerate(G["landmarks"]): s.append((f"sign {l['id']} (vertical + center x)", ".sign", i, ("vcx", l["sign"])))
        s += [("clock", ".clock", 0, G["clock"]), ("card frame", "img.sp[src*='card-frame']", 0, G["card"]["frame"]), ("panel title", ".mtitle", 0, G["panel"]["title"]), ("panel tags", ".mtags", 0, G["panel"]["tags"]),
              ("panel text", ".mtext", 0, G["panel"]["text"]), ("shelf cell 0", ".mcell", 0, G["shelf"]["cells"][0]), ("walker stand (top-left)", "#hmWalker", 0, ("tl", G["landmarks"][0].get("stand") or [0, 0])), ("legend", ".legend", 0, G["legend"])]
    return s

def judge(expected, actual):
    if actual is None: return "FAIL", "element not rendered"
    if isinstance(expected, tuple):
        mode, box = expected
        if mode == "cxh":
            cx_e, cx_a = (box[0] + box[2]) / 2, (actual[0] + actual[2]) / 2; he, ha = box[3] - box[1], actual[3] - actual[1]
            d = max(abs(cx_e - cx_a), abs(he - ha)); return ("PASS" if d <= 8 else "FAIL"), f"center x {cx_a:.0f} vs {cx_e:.0f}, height {ha} vs {he}"
        if mode == "vcx":
            cx_e, cx_a = (box[0] + box[2]) / 2, (actual[0] + actual[2]) / 2; d = max(abs(cx_e - cx_a), abs(box[1] - actual[1]), abs(box[3] - actual[3]))
            return ("PASS" if d <= GEO_TOL + 1 else "FAIL"), f"center x {cx_a:.0f} vs {cx_e:.0f}, top/bottom {actual[1]}/{actual[3]} vs {box[1]}/{box[3]}"
        if mode == "tl":
            d = max(abs(box[0] - actual[0]), abs(box[1] - actual[1])); return ("PASS" if d <= GEO_TOL else "FAIL"), f"top-left {actual[:2]} vs {box}"
    d = max(abs(a - e) for a, e in zip(actual, expected))
    return ("PASS" if d <= GEO_TOL else "FAIL"), f"max edge delta {d}px ({actual} vs {expected})"

# ---------------------------------------------------------------- image analysis
def load(p): return np.array(Image.open(p).convert("RGB"))

def labeled_pair(a, b, la, lb):
    out = Image.new("RGB", (2560 + 8, 720 + 30), (12, 12, 14)); out.paste(Image.fromarray(a), (0, 30)); out.paste(Image.fromarray(b), (1288, 30))
    d = ImageDraw.Draw(out); f = font(18); d.text((8, 6), la, fill=(255, 255, 255), font=f); d.text((1296, 6), lb, fill=(255, 255, 255), font=f); return out

def heat(diff):  # diff: HxW uint8
    return cv2.cvtColor(cv2.applyColorMap(diff, cv2.COLORMAP_JET), cv2.COLOR_BGR2RGB)

def blobs(mask_bool, k=6):
    n, lab, stats, _ = cv2.connectedComponentsWithStats(mask_bool.astype(np.uint8), 8)
    items = sorted([(int(stats[i, cv2.CC_STAT_AREA]), [int(stats[i, cv2.CC_STAT_LEFT]), int(stats[i, cv2.CC_STAT_TOP]), int(stats[i, cv2.CC_STAT_LEFT] + stats[i, cv2.CC_STAT_WIDTH]), int(stats[i, cv2.CC_STAT_TOP] + stats[i, cv2.CC_STAT_HEIGHT])]) for i in range(1, n)], key=lambda t: -t[0])
    return items[:k]

def analyze(theme, out, art, meas):
    T = art["themes"][theme]; ref = load(os.path.join(ASSETS, T["reference"])); run = load(os.path.join(out, "02-runtime.png")); scene = load(os.path.join(ASSETS, T["scene"]))
    Image.fromarray(ref).save(os.path.join(out, "01-master.png"))
    labeled_pair(ref, run, f"MASTER (registered 1280x720)  {TITLES[theme]}", "RUNTIME (actual renderer, Preview on TV)").save(os.path.join(out, "03-side-by-side.png"))
    Image.blend(Image.fromarray(ref), Image.fromarray(run), 0.5).save(os.path.join(out, "04-overlay-50.png"))
    diff = np.abs(ref.astype(int) - run.astype(int)).max(axis=2).astype(np.uint8); Image.fromarray(heat(diff)).save(os.path.join(out, "05-diff-heatmap.png"))
    # static masks
    H, W = diff.shape; mask = np.ones((H, W), bool)
    for rg in T["regions"]:
        if rg["kind"] in ("DYNAMIC RECREATE", "LEGEND CORRECTION"):
            x0, y0, x1, y1 = rg["box_stage"]; mask[max(0, y0 - 2):y1 + 2, max(0, x0 - 2):x1 + 2] = False
    mask_b = mask.copy()
    for (x0, y0, x1, y1) in meas.get("__sprites", []): mask_b[max(0, y0 - 1):y1 + 1, max(0, x0 - 1):x1 + 1] = False
    Image.fromarray((mask * 255).astype(np.uint8)).save(os.path.join(out, "06-static-mask.png"))
    bad_a = (diff > DIFF_T) & mask; bad_b = (diff > DIFF_T) & mask_b
    pct_a = 100 * bad_a.sum() / max(1, mask.sum()); pct_b = 100 * bad_b.sum() / max(1, mask_b.sum()); mean_b = float(diff[mask_b].mean()) if mask_b.any() else 0.0
    sd = heat(np.where(mask_b, diff, 0).astype(np.uint8)); sd[~mask_b] = (sd[~mask_b] * 0.25).astype(np.uint8)
    im = Image.fromarray(sd); d = ImageDraw.Draw(im); bl = blobs(bad_b)
    for area, bx in bl: d.rectangle(bx, outline=(255, 255, 255), width=2)
    d.text((8, 8), f"strict static diff: {pct_b:.2f}% of static pixels differ > {DIFF_T} (mean |d| {mean_b:.2f})  boxes = largest differing blobs", fill=(255, 255, 255), font=font(16)); im.save(os.path.join(out, "07-static-diff.png"))
    # layer diagnostic: scene | sprites | runtime at 50 %
    sheet = Image.new("RGB", (640, 360), (40, 40, 48)); x = y = 4; rowh = 0
    for name, sp in T["sprites"].items():
        s = Image.open(os.path.join(ASSETS, sp["file"])).convert("RGBA"); s.thumbnail((150, 110))
        if x + s.width > 636: x = 4; y += rowh + 4; rowh = 0
        if y + s.height > 356: break
        sheet.paste(s, (x, y), s); x += s.width + 6; rowh = max(rowh, s.height)
    diag = Image.new("RGB", (640 * 3 + 16, 360 + 30), (12, 12, 14)); d = ImageDraw.Draw(diag); f = font(16)
    for i, (lab, im2) in enumerate([("inpainted static scene (lossless)", Image.fromarray(scene).resize((640, 360), Image.LANCZOS)), ("cut sprites (plates, frames, badges, walker)", sheet), ("runtime", Image.fromarray(run).resize((640, 360), Image.LANCZOS))]):
        diag.paste(im2, (i * 648, 30)); d.text((i * 648 + 6, 6), lab, fill=(255, 255, 255), font=f)
    diag.save(os.path.join(out, "08-layer-diagnostic.png"))
    # per-region verdicts
    verdicts = []
    for rg in T["regions"]:
        x0, y0, x1, y1 = rg["box_stage"]; x0, y0, x1, y1 = max(0, x0), max(0, y0), min(W, x1), min(H, y1)
        if rg["kind"] == "STATIC PRESERVE":
            v = "PASS" if pct_b <= STATIC_FAIL_PCT else "FAIL"
            verdicts.append({"region": rg["name"], "kind": rg["kind"], "verdict": v, "evidence": f"{pct_b:.2f}% of static pixels differ > {DIFF_T} after excluding moved sprites ({pct_a:.2f}% before); mean |d| {mean_b:.2f}; largest blobs {[b for _, b in bl[:3]]}"})
        elif rg["kind"] == "LEGEND CORRECTION":
            txt = " ".join(meas.get("__legend", [])); ok = all(k in txt.upper() for k in ("LAUNCH", "DETAILS", "MENU", "BACK")) and not any(k in txt.upper().split() for k in ("X", "Y"))
            verdicts.append({"region": rg["name"], "kind": rg["kind"], "verdict": "PASS" if ok else "FAIL", "evidence": f"legend text: {txt!r} (original controller: A/B/SELECT/START only)"})
        else:
            rr = run[y0:y1, x0:x1].astype(int); sc = scene[y0:y1, x0:x1].astype(int); present = float(np.abs(rr - sc).mean()) if rr.size else 0.0
            v = "PASS" if present > 4.0 else "FAIL"
            verdicts.append({"region": rg["name"], "kind": rg["kind"], "verdict": v, "evidence": f"live content drawn over the rebuilt scene (mean |runtime - scene| {present:.1f})"})
    return {"static_pct_strict": round(pct_a, 3), "static_pct_excluding_moved_sprites": round(pct_b, 3), "static_mean_abs_diff": round(mean_b, 3), "blobs": bl, "verdicts": verdicts}

# ---------------------------------------------------------------- captures
def capture_theme(b, theme, out, art):
    G = art["themes"][theme]["geometry"]; caps = []
    def cap(name, note):
        p = b.shot(os.path.join(out, name + ".png")); st = state(b); caps.append({"file": name + ".png", "note": note, "state": st}); print(f"  {name}: {st}")
    set_theme(b, theme); cap("10-default-fixture", "Default HOME / All Games, deterministic fixture (first 36 catalog games with artwork, master order)")
    if theme == "box-art-showcase":   # the master depicts a mid-list selection (boxes on both sides); compare the comparable state
        b.js("TV.S.index=3; tvDraw()"); time.sleep(0.8); cap("10b-comparable-state", "Comparable state to the master: a mid-list selection with boxes receding to both sides"); shutil.copy(os.path.join(out, "10b-comparable-state.png"), os.path.join(out, "02-runtime.png")); b.js("TV.S.index=0; tvDraw()"); time.sleep(0.5)
    else: shutil.copy(os.path.join(out, "10-default-fixture.png"), os.path.join(out, "02-runtime.png"))
    spec = specs(theme, G, art); meas = b.js(MEASURE.replace("SPEC", json.dumps([[k, s, i] for k, s, i, _ in spec])))
    geometry = [{"element": k, "selector": s, "expected": e, "actual": meas.get(k), "verdict": judge(e, meas.get(k))[0], "detail": judge(e, meas.get(k))[1]} for k, s, i, e in spec]
    b.act(["right", "right"] + (["down"] if theme == "arcade-grid" else []), 0.9); cap("12-other-game", "Another highlighted game (D-pad moved): cover / hero / box / card replacement is live")
    b.js("TV.S.section={type:'browse'}; TV.S.zone='browse'; TV.S.overlay=null; tvDraw()"); time.sleep(0.6); cap("13-browse", "Browse: custom folders (two top-level, one nested) first, automatic categories below")
    b.js("TV.S.section={type:'search'}; TV.S.zone='keyboard'; TV.S.overlay=null; TV.S.query='ma'; TV.S.keyRow=0; TV.S.keyCol=0; tvDraw()"); time.sleep(0.6); cap("14a-search-keyboard", "Search: six-column keyboard, live results for 'ma'")
    b.act(["up"], 0.4); cap("14b-search-suggest", "Search: suggestion zone focused"); b.act(["a"], 0.5); cap("14c-search-results", "Search: results zone")
    b.js("TV.S.section={type:'allgames'}; TV.S.zone='games'; TV.S.overlay=null; TV.S.index=2; tvDraw()"); time.sleep(0.5); b.act(["select"], 0.6); cap("15-details", "Game Details (SELECT): real metadata, Play Game focused, original-controller legend"); b.act(["b"], 0.4)
    if theme == "arcade-grid":
        b.js("TV.S.index=21; tvDraw()"); time.sleep(0.6); cap("16-theme-state", "Scrolled grid: the dominant card follows the selection into the second page")
    elif theme == "box-art-showcase":
        b.js("TV.S.index=homeListing(TV.model,TV.S,tvSearch()).ids.length-1; tvDraw()"); time.sleep(0.6); cap("16-theme-state", "3-D carousel at the end position (right arrow dimmed, boxes receding to the left)")
    elif theme == "featured-shelf":
        b.js("TV.S.index=9; TV.S.zone='nav'; TV.S.navIndex=3; tvDraw()"); time.sleep(0.6); cap("16-theme-state", "Changed hero (another game) with the sidebar focused")
    else:
        b.js("TV.S.zone='map'; TV.S.landmark=0; tvDraw()"); time.sleep(0.3); b.act(["right"], 1.2); cap("16-theme-state", "Map zone: character traveled to the Browse landmark")
        b.act(["select"], 0.6); cap("16b-map-destination", "SELECT destination overlay (retro RPG menu) while a landmark is focused"); b.act(["b"], 0.4)
        b.js("TV.S.zone='map'; TV.S.landmark=0; tvDraw()"); time.sleep(0.3); b.act(["right", "right", "right"], 5.0); cap("16c-map-folder-landmark", "Character at a custom-folder landmark (live folder name on the authored sign)")
    b.js("TV.S.section={type:'allgames'}; TV.S.zone='games'; TV.S.overlay=null; TV.S.index=(()=>{const ids=homeListing(TV.model,TV.S,tvSearch()).ids; let b=0; ids.forEach((id,i)=>{ if(hmGame(TV.model,id).title.length>hmGame(TV.model,ids[b]).title.length) b=i; }); return b;})(); tvDraw()"); time.sleep(0.9)
    cap("17-stress-long-title", "Stress: the longest title in the listing focused (shrink-to-fit, two-line or scrolling treatment)")
    b.js("TV.S.section={type:'allgames'}; TV.S.zone='games'; TV.S.overlay=null; TV.S.index=(()=>{const ids=homeListing(TV.model,TV.S,tvSearch()).ids; const k=ids.findIndex(id=>{const g=hmGame(TV.model,id); return g.art&&g.art.provenance==='generated';}); return k<0?0:k;})(); tvDraw()"); time.sleep(0.9)
    cap("19-missing-artwork", "Missing artwork: a game whose cover is the generated placeholder (GENERATED COVER mark) focused")
    return caps, geometry, meas

def small_collections(b, outdir, results):
    """1-game and 3-game collections per theme (small library states)"""
    for n in (1, 3):
        b.js("try{tvClose()}catch(e){} " + f"sel.clear(); DATA.games.filter(g=>g.art).slice(0,{n}).forEach(g=>sel.add(g.game_id)); persist(); render()"); time.sleep(0.5); b.js("openTv()"); time.sleep(1.0); b.js(STAGE); time.sleep(0.3)
        for theme in THEMES:
            set_theme(b, theme); out = os.path.join(outdir, theme); name = f"20-small-collection-{n}.png"; b.shot(os.path.join(out, name)); st = state(b)
            results[theme]["captures"].append({"file": name, "note": f"Small collection: {n} game{'s' if n > 1 else ''} selected", "state": st}); print(f"  {theme} small {n}: {st}")

def run(url, outdir):
    art = json.load(open(os.path.join(ASSETS, "home-art.json"), encoding="utf-8")); os.makedirs(outdir, exist_ok=True)
    hashes = {}
    for theme in THEMES:
        T = art["themes"][theme]; p = os.path.join(MASTER_DIR, T["master_file"]); h = sha256(p); im = Image.open(p)
        hashes[theme] = {"file": T["master_file"], "sha256": h, "size": im.size, "unchanged": h == T["master_sha256"] and im.size == tuple(art["master_size"])}
        print(theme, "master", "UNCHANGED" if hashes[theme]["unchanged"] else "MISMATCH", h[:16], im.size)
    results = {}
    b = Browser(url)
    try:
        b.js("try{doneWelcome()}catch(e){} localStorage.removeItem('studio-home-v1'); " + FIXTURE); time.sleep(0.5); b.js("openTv()"); time.sleep(0.8); b.js(STAGE); time.sleep(0.3); b.js(FOLDERS)
        for theme in THEMES:
            out = os.path.join(outdir, theme); os.makedirs(out, exist_ok=True); print(theme)
            caps, geometry, meas = capture_theme(b, theme, out, art)
            an = analyze(theme, out, art, meas)
            results[theme] = {"captures": caps, "geometry": geometry, "analysis": an, "fonts": meas.get("__fonts", []), "legend": meas.get("__legend", [])}
        small_collections(b, outdir, results)
        # actual catalog data (every catalog game) and the large-library stress state, per theme
        b.js("try{tvClose()}catch(e){} " + ALL); time.sleep(0.5); b.js("openTv()"); time.sleep(1.0); b.js(STAGE); time.sleep(0.3); b.js(FOLDERS)
        for theme in THEMES:
            set_theme(b, theme); out = os.path.join(outdir, theme); p = b.shot(os.path.join(out, "11-actual-catalog-data.png")); st = state(b)
            results[theme]["captures"].insert(1, {"file": "11-actual-catalog-data.png", "note": "Same screen with the actual catalog data (every catalog game selected): live counts and metadata", "state": st})
            results[theme]["captures"].append({"file": "18-stress-large-library.png", "note": "Stress: large library (same capture as 11; counts in the hundreds)", "state": st}); shutil.copy(p, os.path.join(out, "18-stress-large-library.png")); print(f"  {theme} actual data: {st}")
    finally:
        errs = list(b.errors); b.close()
    # reports
    for theme in THEMES:
        r = results[theme]; json.dump(r["geometry"], open(os.path.join(outdir, theme, "geometry.json"), "w"), indent=1); json.dump(r["analysis"], open(os.path.join(outdir, theme, "verdicts.json"), "w"), indent=1)
        write_theme_review(theme, outdir, r, art, hashes[theme])
    write_summary(outdir, results, hashes, errs)
    print("errors:", errs[:8] if errs else "none"); return results

def write_theme_review(theme, outdir, r, art, h):
    T = art["themes"][theme]; out = os.path.join(outdir, theme); L = [f"# {TITLES[theme]} — visual review packet", "", "Automated numbers below are diagnostics, not fidelity evidence: the static-region diff covers untouched scenery only; the dynamic areas (cards, boxes, hero, signs) are judged by eye from the screenshots.", "", f"Master: `{os.path.join(MASTER_DIR, T['master_file'])}` (SHA-256 `{h['sha256']}`, {h['size'][0]}x{h['size'][1]}, {'unchanged' if h['unchanged'] else 'MISMATCH'})", f"Registered 1280x720 master: `01-master.png` (from `{T['reference']}`)", "Runtime: `02-runtime.png` (= `10-default-fixture.png`), side-by-side `03-side-by-side.png`, overlay `04-overlay-50.png`, heat map `05-diff-heatmap.png`, static mask `06-static-mask.png`, strict static diff `07-static-diff.png`, layers `08-layer-diagnostic.png`", ""]
    a = r["analysis"]; L += ["## Static regions (strict pixel diff)", "", f"- {a['static_pct_excluding_moved_sprites']:.2f}% of static pixels differ by more than {DIFF_T} (any channel) after excluding sprite copies the renderer moves; {a['static_pct_strict']:.2f}% before; mean |d| {a['static_mean_abs_diff']:.2f}.", f"- Largest differing blobs (stage px boxes): {[b for _, b in a['blobs'][:4]]}", ""]
    L += ["## Region-by-region verdicts", "", "| Region | Kind | Verdict | Evidence |", "|---|---|---|---|"] + [f"| {v['region']} | {v['kind']} | **{v['verdict']}** | {v['evidence']} |" for v in a["verdicts"]] + [""]
    L += ["## Measured key-element geometry (stage px, expected from the master-derived manifest)", "", "| Element | Verdict | Detail |", "|---|---|---|"] + [f"| {g['element']} | **{g['verdict']}** | {g['detail']} |" for g in r["geometry"]] + [""]
    L += ["## Captures", ""] + [f"- `{c['file']}` — {c['note']} (state: {json.dumps(c['state'])})" for c in r["captures"]] + ["", f"Fonts in use: {', '.join(r['fonts'])}", f"Legend text: {r['legend']}", "", "Status: **AWAITING OWNER APPROVAL** — the automated verdicts are evidence, not acceptance.", ""]
    open(os.path.join(out, "REVIEW.md"), "w", encoding="utf-8").write("\n".join(L))

def write_summary(outdir, results, hashes, errs):
    repo = os.environ.get("CCS_PUBLIC_REPO", "C:/NES/work/publish/repo3")   # the public build repo carries the commits; the OneDrive workspace is the source
    try: branch = subprocess.run(["git", "rev-parse", "--abbrev-ref", "HEAD"], capture_output=True, text=True, cwd=repo).stdout.strip(); commit = subprocess.run(["git", "rev-parse", "--short", "HEAD"], capture_output=True, text=True, cwd=repo).stdout.strip()
    except Exception: branch = commit = "?"
    L = ["CLASSIC CART STUDIO — FOUR-THEME VISUAL ACCEPTANCE REVIEW (automated evidence)", "", f"Branch / commit (public build repo {repo}): {branch} / {commit}", "Master PNG hashes confirmed: " + "; ".join(f"{t}: {h['sha256'][:12]}… {'unchanged' if h['unchanged'] else 'MISMATCH'}" for t, h in hashes.items()), f"1280x720 actual runtime screenshot artifact directory: {outdir}", f"Page JS errors during capture: {errs if errs else 'none'}", "No hardware interactions (G1): confirmed (browser-only harness)", ""]
    for i, t in enumerate(THEMES, 1):
        r = results[t]; a = r["analysis"]; fails = [v for v in a["verdicts"] if v["verdict"] != "PASS"] + [g for g in r["geometry"] if g["verdict"] != "PASS"]
        L += [f"THEME {i} — {TITLES[t]}", f"  Master image: {os.path.join(MASTER_DIR, hashes[t]['file'])}", f"  Runtime screenshot: {os.path.join(outdir, t, '02-runtime.png')}", f"  Overlay / difference map: {os.path.join(outdir, t, '04-overlay-50.png')} ; {os.path.join(outdir, t, '05-diff-heatmap.png')} ; {os.path.join(outdir, t, '07-static-diff.png')}",
              f"  Automated checks (evidence only, not fidelity): region verdicts {sum(v['verdict']=='PASS' for v in a['verdicts'])}/{len(a['verdicts'])} PASS; geometry {sum(g['verdict']=='PASS' for g in r['geometry'])}/{len(r['geometry'])} PASS (static-region pixel diff kept in verdicts.json as a diagnostic of untouched scenery only; it says nothing about the dynamic areas)",
              "  Automated FAIL items: " + ("NONE" if not fails else "; ".join((f.get('region') or f.get('element')) + " — " + (f.get('evidence') or f.get('detail')) for f in fails)), "  Status: AWAITING OWNER APPROVAL", ""]
    L += ["User visual approval: NOT YET GRANTED", "Publication / completion status: NOT RELEASED AS ACCEPTED", ""]
    open(os.path.join(outdir, "REVIEW.md"), "w", encoding="utf-8").write("\n".join(L)); print("\n".join(L))

if __name__ == "__main__":
    ap = argparse.ArgumentParser(); ap.add_argument("--url", default="http://127.0.0.1:8899/nes-catalog.html"); ap.add_argument("--out", default=os.path.join(ROOT, "logs", "home-review")); a = ap.parse_args()
    run(a.url, a.out)
