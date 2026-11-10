r"""The website shows each device only what works there (headless Edge; the device is set with the browser's user agent, platform report and
touch points, the way a real device reports itself; the window width alone never changes it). Synthetic ROMs; no network beyond this PC.
  windows        a Windows desktop browser, wide and narrowed to 390 px: Back Up Collection; Open in Windows App offered (menu and dialog)
  phones         iPhone and Android: Back Up Collection; no Open in Windows App anywhere; what needs the Windows app (Install on NES
                 Classic, Add Games from Files) explains that it needs a Windows PC instead of inviting to open an app that cannot exist there
  mac, ipad      a Mac browser has the backup without the Windows app; an iPad (Mac user agent with touch) is treated as a tablet
  no sharing     sharing is removed (coordinated phase, section 7): on every device, even where the browser could share files
                 (navigator.canShare), the backup is saved, navigator.share is never called and no share action exists; a browser
                 download is reported as one; no sharing service is named (AirDrop, Phone Link ...)
  modal safety   on the website: while a package is being made, a click on the backdrop, Escape and the header Close change nothing
  screenshots    the mobile backup dialog, platform-appropriate instructions (--out)
  python test_platform_web.py <built site folder> [--out DIR]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, json, os, sys, tempfile, time
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from test_loadouts_web import EdgeSession, serve, READY, CALM
from test_ccs_v2_web import ROMS
DEVICES = {
    "windows-wide": dict(w=1440, h=900, ua="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36 Edg/140.0.0.0", platform="Windows", mobile=False, touch=0),
    "windows-narrow": dict(w=390, h=844, ua="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36 Edg/140.0.0.0", platform="Windows", mobile=False, touch=0),
    "iphone": dict(w=390, h=844, ua="Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1", platform="iOS", mobile=True, touch=5),
    "android": dict(w=412, h=915, ua="Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Mobile Safari/537.36", platform="Android", mobile=True, touch=5),
    "mac": dict(w=1440, h=900, ua="Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15", platform="macOS", mobile=False, touch=0),
    "ipad": dict(w=1024, h=1366, ua="Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15", platform="iOS", mobile=False, touch=5),
}
STATE = r"""(async()=>{ const x=await mkRom(1,true); const y=await mkRom(2,true); sel.clear(); sel.add(x.id); sel.add(y.id); persist(); render(); csSaveActiveNow();
 ccsBackupDialog(); await new Promise(r=>setTimeout(r,800)); const miss={text:$('collBody').innerText, winApp:!!$('ccsWinApp'), pick:!!$('ccsPick')};
 await ccsAddRomSources([x.file, y.file]); await ccsCheck(); await new Promise(r=>setTimeout(r,500));
 const ready={send:!!$('ccsSend')||!!$('ccsShare'), exp:$('ccsExp').textContent, text:$('collBody').innerText};
 return JSON.stringify({phone:ccsPhone(), win:ccsWindowsDesktop(), avail:desktopAvailable(), menuDesktop:!!$('mDesktop')&&!$('mDesktop').hidden, miss, ready}); })()"""

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=os.path.join(tempfile.gettempdir(), "ccs-platform-web")); a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True); checks = []
    def ok(k, v, n=""): checks.append((k, bool(v))); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:400]})" if n and not v else ""), flush=True)
    web = serve(a.site); URL = f"http://127.0.0.1:{web.server_address[1]}/index.html"; R = {}
    for name, dv in DEVICES.items():
        with EdgeSession(width=dv["w"], height=dv["h"], scale=2 if dv["mobile"] else 1, profile=tempfile.mkdtemp(prefix=f"ccs-pf-{name}-")) as ed:
            ed.call("Emulation.setUserAgentOverride", {"userAgent": dv["ua"], "platform": dv["platform"], "userAgentMetadata": {"platform": dv["platform"], "platformVersion": "", "architecture": "", "model": "", "mobile": dv["mobile"], "brands": []}})
            if dv["touch"]: ed.call("Emulation.setTouchEmulationEnabled", {"enabled": True, "maxTouchPoints": dv["touch"]})
            else: ed.call("Page.addScriptToEvaluateOnNewDocument", {"source": "Object.defineProperty(Navigator.prototype,'maxTouchPoints',{get:()=>0});"})   # this PC has a touch screen; a Mac or a desktop PC without one reports 0
            ed.navigate(URL); ed.wait_for(READY, timeout=120); time.sleep(1); ed.js(CALM); ed.js(ROMS)
            st = json.loads(ed.js(STATE)); R[name] = st
            if name in ("iphone", "android"):
                open(os.path.join(a.out, f"{name}-backup-ready.png"), "wb").write(ed.screenshot())
                # the backup on a phone: saved through the browser (here a download), never handed to a share sheet
                fb = json.loads(ed.js(r"""(async()=>{ window.__shares=0; Object.defineProperty(navigator,'canShare',{value:()=>true, configurable:true}); Object.defineProperty(navigator,'share',{value:async()=>{ window.__shares++; }, configurable:true});
                  window.__saved=null; ccsSaveFile=async(n,b)=>{ window.__saved={name:n, size:b.size}; return {ok:true, name:n, download:true}; }; $('toast').textContent='';
                  $('ccsExp').click(); const m=await new Promise(r=>{ let i=0; const t=setInterval(()=>{ const a=$('ccsAck'); if(a&&a.classList.contains('open')||++i>50){ clearInterval(t); r(a); } },100); });
                  $('ccsAckC').click(); [...m.querySelectorAll('button')].find(b=>b.textContent==='Create Backup').click(); for(let i=0;i<40&&!window.__saved;i++) await new Promise(r=>setTimeout(r,150)); await new Promise(r=>setTimeout(r,300));
                  return JSON.stringify({saved:!!window.__saved, shares:window.__shares, shareBtn:!!$('ccsShare'), toast:$('toast').textContent}); })()"""))
                R[name]["fallback"] = fb
                if name == "iphone": open(os.path.join(a.out, "iphone-backup-saved.png"), "wb").write(ed.screenshot())
                inv = json.loads(ed.js(r"""(async()=>{ closeColl(); openInstall(); await new Promise(r=>setTimeout(r,500)); const a={title:$('collTitle').textContent, text:$('collBody').innerText, open:$('collm').classList.contains('open')}; closeColl();
                  importGames('files'); await new Promise(r=>setTimeout(r,500)); const b={title:$('collTitle').textContent, text:$('collBody').innerText}; closeColl(); return JSON.stringify({install:a, add:b}); })()"""))
                R[name]["needs_pc"] = inv
                if name == "iphone":
                    ed.js("openInstall(); true"); time.sleep(0.5); open(os.path.join(a.out, "iphone-install-needs-a-windows-pc.png"), "wb").write(ed.screenshot()); ed.js("closeColl(); true")
            if name == "windows-wide":
                # a browser that could share files: the backup is still only saved
                sh = json.loads(ed.js(r"""(async()=>{ window.__shares=0; Object.defineProperty(navigator,'canShare',{value:()=>true, configurable:true}); Object.defineProperty(navigator,'share',{value:async()=>{ window.__shares++; }, configurable:true});
                  window.__saved=null; ccsSaveFile=async(n,b)=>{ window.__saved={name:n, size:b.size}; return {ok:true, name:n, download:true}; }; $('toast').textContent='';
                  $('ccsExp').click(); const m=await new Promise(r=>{ let i=0; const t=setInterval(()=>{ const a=$('ccsAck'); if(a&&a.classList.contains('open')||++i>50){ clearInterval(t); r(a); } },100); });
                  $('ccsAckC').click(); [...m.querySelectorAll('button')].find(b=>b.textContent==='Create Backup').click(); for(let i=0;i<40&&!window.__saved;i++) await new Promise(r=>setTimeout(r,150)); await new Promise(r=>setTimeout(r,300));
                  return JSON.stringify({saved:!!window.__saved, shares:window.__shares, shareBtn:!!$('ccsShare'), toast:$('toast').textContent}); })()"""))
                R[name]["sharing"] = sh; open(os.path.join(a.out, "windows-browser-backup-saved.png"), "wb").write(ed.screenshot())
                # modal safety on the website: a package being made (big enough to take a moment)
                ms = json.loads(ed.js(r"""(async()=>{ closeColl(); const xs=[]; for(let i=0;i<6;i++) xs.push(await mkRom(100+i,true,4000000)); sel.clear(); xs.forEach(x=>sel.add(x.id)); persist(); render(); csSaveActiveNow();
                  await ccsAddRomSources(xs.map(x=>x.file)); ccsBackupDialog(); await new Promise(r=>setTimeout(r,800)); window.ccsSaveFile=async()=>({ok:false});
                  let built=false; const ob=ccsBuild; ccsBuild=async function(){ await new Promise(r=>setTimeout(r,1500)); const b=await ob.apply(this,arguments); built=true; return b; };
                  $('ccsExp').click(); const m=await new Promise(r=>{ let i=0; const t=setInterval(()=>{ const a=$('ccsAck'); if(a&&a.classList.contains('open')||++i>50){ clearInterval(t); r(a); } },100); });
                  $('ccsAckC').click(); [...m.querySelectorAll('button')].find(b=>b.textContent==='Create Backup').click(); await new Promise(r=>setTimeout(r,200));
                  const s0={open:$('collm').classList.contains('open'), proc:PROC.collm, close:$('collClose').disabled};
                  $('collm').dispatchEvent(new MouseEvent('click',{bubbles:true})); document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true})); $('collClose').click();
                  const s1={open:$('collm').classList.contains('open'), proc:PROC.collm, text:$('collBody').innerText.slice(0,80)}; ccsBuild=ob; return JSON.stringify({s0, s1}); })()"""))
                R[name]["modal"] = ms
    for k, v in R.items(): print("  ", k, json.dumps({x: v[x] for x in ("phone", "win", "avail", "menuDesktop")}), "|", v["ready"]["exp"])
    W, N, I, A, M, P = (R[k] for k in ("windows-wide", "windows-narrow", "iphone", "android", "mac", "ipad"))
    B = "Create Backup…"
    ok("windows_browser_offers_the_backup_and_the_windows_app", W["ready"]["exp"] == B and not W["ready"]["send"] and W["avail"] and W["menuDesktop"] and W["miss"]["winApp"])
    ok("narrow_windows_browser_is_still_a_windows_desktop", N["ready"]["exp"] == B and not N["ready"]["send"] and N["avail"] and N["miss"]["winApp"] and not N["phone"])
    ok("phones_offer_the_backup_and_no_windows_app", all(x["ready"]["exp"] == B and not x["ready"]["send"] and not x["avail"] and not x["menuDesktop"] and not x["miss"]["winApp"] and x["miss"]["pick"] for x in (I, A)), (I["miss"], A["miss"]))
    ok("phones_explain_windows_only_features", all(x["needs_pc"]["install"]["title"] == "Needs a Windows PC" and x["needs_pc"]["add"]["title"] == "Needs a Windows PC" and "Open in Windows App" not in x["needs_pc"]["install"]["text"] for x in (I, A)), (I["needs_pc"], A["needs_pc"]))
    ok("mac_has_the_backup_without_the_windows_app", M["ready"]["exp"] == B and not M["ready"]["send"] and not M["avail"] and not M["menuDesktop"] and not M["miss"]["winApp"])
    ok("ipad_is_a_tablet_not_a_desktop", P["phone"] and P["ready"]["exp"] == B and not P["avail"])
    ok("no_sharing_on_any_device_even_when_the_browser_could", all(x["saved"] and x["shares"] == 0 and not x["shareBtn"] and "Your browser is saving" in x["toast"] for x in (W["sharing"], I["fallback"], A["fallback"])), (W["sharing"], I["fallback"], A["fallback"]))
    texts = " ".join([W["sharing"]["toast"], I["fallback"]["toast"], A["fallback"]["toast"]] + [x["ready"]["text"] for x in R.values()])
    ok("no_sharing_service_names_in_instructions", not any(s in texts for s in ("AirDrop", "Phone Link", "Nearby Share", "OneDrive")), [s for s in ("AirDrop", "Phone Link", "Nearby Share", "OneDrive") if s in texts])
    ms = W["modal"]
    ok("website_modal_cannot_be_dismissed_while_building", ms["s0"]["open"] and ms["s0"]["proc"] == "running" and ms["s0"]["close"] and ms["s1"]["open"] and ms["s1"]["proc"] == "running", ms)
    bad = [k for k, v in checks if not v]; print("RESULT:", "PASS" if not bad else "FAIL " + str(bad), f"({len(checks)} checks; screenshots in {a.out})"); return 0 if not bad else 1

if __name__ == "__main__": sys.exit(main())
