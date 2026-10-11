"""Live verification of the DEPLOYED website against a companion on this computer (no host mapping, no local TLS: the real origin).
  python test_live_site.py [--url https://rance9.github.io/Classic-Cart-Studio/] [--port 8765]
Checks, in headless Edge with fresh profiles: the page loads from the real origin and sub-path with its assets; the connection pill
shows "Connect to Your Computer" (companion running, not paired); the pairing page served by the companion accepts the real origin;
claiming the one-time code connects the real page (library read through CORS + bearer); reload keeps the pairing; the download
section shows the release version and SHA-256 when a release exists; plain HTTP checks of index.html, robots.txt, release.json
and a sprite. A companion must be running on --port with the default allow list. Exit 0 = PASS."""
from __future__ import annotations
import os, sys, json, subprocess, tempfile, time, re, html, urllib.request, urllib.error, urllib.parse, shutil, argparse
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
GRANTS: list = []; LNA_BYPASS = os.environ.get("NES_LIVE_LNA_BYPASS", "1") != "0"
EDGE = next(x for x in [r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", r"C:\Program Files\Microsoft\Edge\Application\msedge.exe"] if os.path.exists(x))
JS = r"""(async()=>{ const out={}; const wait=ms=>new Promise(r=>setTimeout(r,ms)); const step=STEP; try{ await wait(600); await companionStatus(); out.step=step; out.origin=location.origin; out.path=location.pathname; out.hosted=COMPANION.hosted; out.state=COMPANION.state; out.pill=$('connPill').textContent.trim(); out.version=COMPANION.version; out.games=DATA.games.length; out.sprites=[...document.querySelectorAll('img.px')].filter(i=>i.complete&&i.naturalWidth>0).length; out.heroText=($('heroGames')||{}).textContent;
 if(step==='fresh'){ await openConnect(); await wait(1500); out.modal=$('collTitle').textContent; out.hasConnect=!!$('connGo'); out.dl=(($('dl')||{}).textContent||'').replace(/\s+/g,' ').slice(0,300); out.hasHash=!!$('dlHash'); out.hash=($('dlHash')||{}).textContent; closeColl(); }
 if(step==='claim'){ await openConnect(); await wait(500); $('connCode').value=CODE; $('connClaim').click(); for(let i=0;i<40&&COMPANION.state!=='ok';i++) await wait(250); out.state=COMPANION.state; out.pill=$('connPill').textContent.trim(); closeColl(); const L=await capi('/api/library/local'); out.libOk=!!L.ok; const st=await capi('/api/status'); out.statusOk=!!st.ok; }
 if(step==='reload'){ const L=await capi('/api/library/local'); out.libOk=!!L.ok; out.hasToken=!!COMPANION.bearer; }
 }catch(e){ out.err=e.message+' '+(e.stack||'').slice(0,120); } document.title='T '+JSON.stringify(out); })();"""

def cdp_run(url, profile, script, timeout=120):
    """Drive headless Edge over the DevTools protocol: open the real page, evaluate the script, poll the title for the result."""
    import socket, http.client, threading
    port = 9333
    # Headless Edge cannot show the Local Network Access prompt and reports LocalNetworkAccessPermissionDenied even after a DevTools grant
    # (verified 2026-10-10 with Network.loadingFailed). A real user answers the prompt once with Allow. The harness therefore disables the
    # check (LNA_BYPASS) to verify everything else over the real origin; the prompt itself is confirmed interactively by a person.
    flags = ["--disable-features=LocalNetworkAccessChecks,PrivateNetworkAccessChecks,PrivateNetworkAccessSendPreflights"] if LNA_BYPASS else []
    p = subprocess.Popen([EDGE, "--headless=new", "--disable-gpu", "--disable-popup-blocking", *flags, f"--user-data-dir={profile}", f"--remote-debugging-port={port}", "--window-size=1600,1000", "about:blank"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        for _ in range(80):
            try: tabs = json.load(urllib.request.urlopen(f"http://127.0.0.1:{port}/json", timeout=1)); break
            except Exception: time.sleep(0.25)
        else: return {"err": "no devtools"}
        ws_url = next(t["webSocketDebuggerUrl"] for t in tabs if t["type"] == "page")
        from websocket_min import WS   # tiny client below
        ws = WS(ws_url); i = [0]
        def call(method, params=None):
            i[0] += 1; ws.send(json.dumps({"id": i[0], "method": method, "params": params or {}}))
            while True:
                m = json.loads(ws.recv())
                if m.get("id") == i[0]: return m
        call("Page.enable")
        # a public https page reaching 127.0.0.1 needs Edge's Local Network Access permission; a real user answers the prompt (Allow), headless
        # auto-denies it, so the test grants it for this origin exactly as the user would (the page's "blocked" state is covered by test_hosted)
        origin = "/".join(url.split("/", 3)[:3]); g = call("Browser.grantPermissions", {"origin": origin, "permissions": ["localNetworkAccess"]}); GRANTS.append(g.get("error") or "granted")
        call("Page.navigate", {"url": url})
        t0 = time.time()
        while time.time() - t0 < 60:
            r = call("Runtime.evaluate", {"expression": "typeof companionStatus==='function' && document.readyState==='complete'", "returnByValue": True})
            if r.get("result", {}).get("result", {}).get("value"): break
            time.sleep(0.5)
        call("Runtime.evaluate", {"expression": script, "awaitPromise": False})
        t0 = time.time()
        while time.time() - t0 < timeout:
            r = call("Runtime.evaluate", {"expression": "document.title", "returnByValue": True}); t = r.get("result", {}).get("result", {}).get("value", "")
            if t.startswith("T {"): return json.loads(t[2:])
            time.sleep(0.5)
        return {"err": "timeout"}
    finally:
        try: call("Browser.close")   # a graceful close flushes localStorage (the pairing token); a plain kill right after a fast connect can lose it
        except Exception: pass
        try: p.wait(timeout=10)
        except Exception: p.terminate(); p.kill()

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--url", default="https://classiccartstudio.com/"); ap.add_argument("--port", type=int, default=8765); a = ap.parse_args()
    B = f"http://127.0.0.1:{a.port}"; checks = []; ok = lambda k, v, n="": checks.append(f"{k}={'true' if v else 'false'}{(' ('+n+')') if n and not v else ''}")
    origin = "/".join(a.url.split("/", 3)[:3]); work = os.path.join(tempfile.gettempdir(), "nes-live"); shutil.rmtree(work, ignore_errors=True); os.makedirs(work)
    def get(u):
        try:
            with urllib.request.urlopen(u, timeout=30) as r: return r.status, r.read(), dict(r.headers)
        except urllib.error.HTTPError as e: return e.code, b"", {}
    s, b, h = get(a.url); ok("live_index_loads", s == 200 and b"NES Classic Collection Studio" in b and b"brand/oem/" in b and b"Filter &amp; Sort" in b and b"Help &amp; User Guide" in b, str(s))
    ok("live_robots_and_nojekyll_assets", get(a.url + "robots.txt")[0] == 200 and get(a.url + "brand/oem/icon-a.png")[0] == 200 and get(a.url + "brand/fonts/PressStart2P-Regular.ttf")[0] == 200)
    s, b, h = get(a.url + "release.json"); rel = json.loads(b or b"{}") if s == 200 else {}; ok("live_release_json", s == 200, str(s))
    try: v = json.load(urllib.request.urlopen(B + "/api/version", timeout=3)); ok("companion_running", v.get("app") == "nes-classic-collection-studio-companion", str(v)[:100])
    except Exception as e: ok("companion_running", False, str(e)[:80]); print("\n".join(checks)); print("RESULT: FAIL (companion not running)"); sys.exit(1)
    prof = os.path.join(work, "p1")
    o = cdp_run(a.url, prof, JS.replace("STEP", "'fresh'").replace("CODE", "''")); print("fresh:", json.dumps(o)[:800])
    ok("real_origin_and_subpath", o.get("origin") == origin and (o.get("path") or "").startswith("/Classic-Cart-Studio/" if "github.io" in origin else "/"), str(o)[:200])
    ok("page_runs_with_sprites", (o.get("games") or 0) > 700 and (o.get("sprites") or 0) >= 10, f"games={o.get('games')} sprites={o.get('sprites')}")
    ok("fresh_unpaired_state", o.get("state") == "unpaired" and o.get("hasConnect") is True, f"{o.get('state')} {o.get('err')}")
    if rel.get("version"): ok("download_section_shows_release", (rel["version"] in (o.get("dl") or "")) and o.get("hasHash") and (o.get("hash") or "").lower() == (rel.get("sha256") or "").lower(), (o.get("dl") or "")[:200])
    else: ok("download_section_honest_without_release", "could not be loaded" in (o.get("dl") or "") or "releases page" in (o.get("dl") or "") or "no Windows installer" in (o.get("dl") or ""), (o.get("dl") or "")[:200])
    page = urllib.request.urlopen(B + "/pair").read().decode("utf-8"); tok = re.search(r'nes-companion-token" content="([^"]+)"', page).group(1)
    r = urllib.request.Request(B + "/api/pair/approve", data=json.dumps({"origin": origin}).encode(), headers={"Content-Type": "application/json", "Origin": B, "X-NES-Token": tok}, method="POST"); t = json.load(urllib.request.urlopen(r))
    o = cdp_run(a.url, prof, JS.replace("STEP", "'claim'").replace("CODE", "'" + t["code"] + "'")); print("claim:", json.dumps(o)[:600])
    ok("live_claim_connects", o.get("state") == "ok" and o.get("libOk") is True and o.get("statusOk") is True, f"{o.get('state')} {o.get('err')}")
    o = cdp_run(a.url, prof, JS.replace("STEP", "'reload'").replace("CODE", "''")); ok("live_reload_keeps_pairing", o.get("state") == "ok" and o.get("hasToken") is True and o.get("libOk") is True, f"{o.get('state')} {o.get('err')}")
    print("local-network-access permission grants:", GRANTS[:3], "| LNA checks bypassed in headless:", LNA_BYPASS, "(the real prompt is confirmed interactively)")
    bad = [c for c in checks if "=false" in c]; print("\n".join(checks)); print(f"RESULT: {'PASS' if not bad else 'FAIL '+str(bad)} ({len(checks)} checks)"); sys.exit(1 if bad else 0)

if __name__ == "__main__":
    main()
