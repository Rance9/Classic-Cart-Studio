"""Mobile/narrow captures of reports/nes-catalog.html (or a URL) in headless Edge with mobile emulation: initial view, the open
My Collection menu, the Filter & Sort dialog, grid view and a game-details dialog, at phone and small-tablet widths. Reports layout
facts (horizontal overflow, toolbar height, first row height, card rows). Usage: python shot_mobile.py [--url <url>] [--out <dir>]"""
from __future__ import annotations
import os, sys, json, time, subprocess, tempfile, shutil, base64, urllib.request, argparse
HERE = os.path.dirname(os.path.abspath(__file__)); REPORTS = os.path.normpath(os.path.join(HERE, "..", "..", "reports"))
sys.path.insert(0, os.path.join(HERE, "..", "companion")); from websocket_min import WS
EDGE = next(x for x in [r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", r"C:\Program Files\Microsoft\Edge\Application\msedge.exe"] if os.path.exists(x))
WIDTHS = [(360, 780), (390, 844), (430, 932), (540, 960), (640, 960), (768, 1024)]

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--url", default="file:///" + os.path.join(REPORTS, "nes-catalog.html").replace("\\", "/")); ap.add_argument("--out", default=r"C:\NES\work\logs\mobile"); ap.add_argument("--widths", default=""); a = ap.parse_args()
    widths = [tuple(int(x) for x in w.split("x")) for w in a.widths.split(",")] if a.widths else WIDTHS
    os.makedirs(a.out, exist_ok=True); report = {}
    for w, h in widths:
        prof = tempfile.mkdtemp(prefix="nes-mob-"); port = 9339
        p = subprocess.Popen([EDGE, "--headless=new", "--disable-gpu", "--allow-file-access-from-files", f"--user-data-dir={prof}", f"--remote-debugging-port={port}", "about:blank"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        try:
            for _ in range(80):
                try: tabs = json.load(urllib.request.urlopen(f"http://127.0.0.1:{port}/json", timeout=1)); break
                except Exception: time.sleep(0.25)
            for attempt in range(6):
                try: ws = WS(next(t["webSocketDebuggerUrl"] for t in tabs if t["type"] == "page")); break
                except OSError: time.sleep(1); tabs = json.load(urllib.request.urlopen(f"http://127.0.0.1:{port}/json", timeout=2))
            i = [0]
            def call(m, params=None):
                i[0] += 1; ws.send(json.dumps({"id": i[0], "method": m, "params": params or {}}))
                while True:
                    x = json.loads(ws.recv())
                    if x.get("id") == i[0]: return x
            def js(expr):
                r = call("Runtime.evaluate", {"expression": expr, "awaitPromise": True, "returnByValue": True}); res = r.get("result", {})
                if "exceptionDetails" in res: return "ERR " + str((res["exceptionDetails"].get("exception") or {}).get("description", ""))[:160]
                return res.get("result", {}).get("value")
            def shot(name):
                d = call("Page.captureScreenshot", {"format": "png"})["result"]["data"]; fn = os.path.join(a.out, f"{name}-{w}x{h}.png"); open(fn, "wb").write(base64.b64decode(d)); return fn
            call("Page.enable"); call("Emulation.setDeviceMetricsOverride", {"width": w, "height": h, "deviceScaleFactor": 1 if w >= 1000 else 2, "mobile": w < 1000}); call("Emulation.setTouchEmulationEnabled", {"enabled": w < 1000}); call("Page.navigate", {"url": a.url})
            for _ in range(120):
                if js("typeof render==='function' && document.querySelectorAll('#tb tr.pick').length>0") is True: break
                time.sleep(0.5)
            time.sleep(1.0); js("try{ doneWelcome(); }catch(e){}")
            facts = js("""(()=>{ const r=document.querySelector('#tb tr.pick'); const tds=[...r.querySelectorAll('td')].filter(t=>getComputedStyle(t).display!=='none').length; return {hscroll:document.documentElement.scrollWidth>document.documentElement.clientWidth, docW:document.documentElement.scrollWidth, clientW:document.documentElement.clientWidth, toolbar:$('toolbar').offsetHeight, hero:$('hero').offsetHeight, rowH:r.offsetHeight, visibleCells:tds, tableW:$('t').scrollWidth, wrapW:$('tablewrap').clientWidth, thead:getComputedStyle(document.querySelector('#t thead')).display, trayBg:getComputedStyle($('trayBtn')).backgroundImage.slice(0,60), hkBg:getComputedStyle($('hkBtn')).backgroundImage.slice(0,60), ioBg:getComputedStyle($('ioBtn')).backgroundImage===getComputedStyle($('collBtn')).backgroundImage, tagColor:getComputedStyle(document.querySelector('#tb .tag')||document.body).color}; })()""")
            report[f"{w}x{h}"] = {"facts": facts}
            shot("initial")
            js("scrollTo(0, 900)"); time.sleep(0.4); shot("scrolled"); js("scrollTo(0,0)")
            js("$('collBtn').click()"); time.sleep(0.4); shot("menu"); js("document.querySelectorAll('.menu').forEach(m=>m.hidden=true)")
            js("$('filtersBtn').click()"); time.sleep(0.5); shot("filter-sort"); report[f"{w}x{h}"]["modal_hscroll"] = js("(()=>{ const m=$('filters'); return m.scrollWidth>m.clientWidth; })()"); js("$('filtersCancel').click()")
            js("$('vGrid').click()"); time.sleep(0.5); shot("grid"); js("$('vList').click()")
            js("(document.querySelector('#tb tr.pick .btn.sm')||document.querySelector('#tb tr.pick [data-details]')||{click(){}}).click()"); time.sleep(0.6); shot("details"); report[f"{w}x{h}"]["details_hscroll"] = js("(()=>{ const m=$('details'); return m?m.scrollWidth>m.clientWidth:null; })()")
            js("document.querySelectorAll('.modal.open').forEach(m=>m.classList.remove('open'))")
            for ch in ("start", "install", "versions", "legal"):
                js("openGuide('%s')" % ch); time.sleep(0.9); shot("guide-" + ch); report[f"{w}x{h}"]["guide_" + ch] = js("(()=>{ const a=$('guideArt'); const sec=a.querySelector('section.on'); return {hscroll:a.scrollWidth>a.clientWidth+1, steps:sec.querySelectorAll('.step').length, stepH:[...sec.querySelectorAll('.step')].map(x=>x.offsetHeight), band:!!sec.querySelector('.deco .gnum'), title:$('guideTitle').textContent.trim().slice(0,40)}; })()"); js("closeGuide()")
        finally:
            try: call("Browser.close")
            except Exception: pass
            try: p.wait(timeout=10)
            except Exception: p.kill()
            shutil.rmtree(prof, ignore_errors=True)
    json.dump(report, open(os.path.join(a.out, "report.json"), "w", encoding="utf-8"), indent=1)
    for k, v in report.items(): print(k, json.dumps(v)[:700])
    print("shots in", a.out)

if __name__ == "__main__": main()
