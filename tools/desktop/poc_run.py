r"""Proof of concept / integration harness for Classic Cart Studio for Windows (the WebView2 host).
Starts a static server for a site build (or uses --url for the live site), launches ClassicCartStudio.exe in development mode with
an isolated data folder and a DevTools port, drives the page inside WebView2 over the DevTools protocol (desktop session present,
backend connected without pairing, artwork rendered, screenshot), optionally runs extra JavaScript, then closes the window through
the normal close path and checks that the backend process and port are released.
  python poc_run.py [--exe <ClassicCartStudio.exe>] [--site C:\NES\work\build\site] [--url https://classiccartstudio.com/] [--data <dir>] [--out <dir>] [--keep]
Exit 0 = PASS."""
from __future__ import annotations
import os, sys, json, time, subprocess, tempfile, shutil, base64, argparse, threading, http.server, socketserver, urllib.request
HERE = os.path.dirname(os.path.abspath(__file__)); TOOLS = os.path.dirname(HERE); sys.path.insert(0, os.path.join(TOOLS, "companion"))
from websocket_min import WS

class Quiet(http.server.SimpleHTTPRequestHandler):
    def log_message(self, *a): pass
    def end_headers(self): self.send_header("Cache-Control", "no-store"); super().end_headers()

def serve(folder, port):
    h = lambda *a, **k: Quiet(*a, directory=folder, **k)
    srv = socketserver.ThreadingTCPServer(("127.0.0.1", port), h); srv.daemon_threads = True; srv.allow_reuse_address = True
    threading.Thread(target=srv.serve_forever, daemon=True).start(); return srv

class Dev:
    def __init__(self, port, timeout=60, prefer=""):
        t0 = time.time(); self.ws = None
        while time.time() - t0 < timeout:
            try:
                tabs = json.load(urllib.request.urlopen(f"http://127.0.0.1:{port}/json", timeout=1))
                pages = [t for t in tabs if t["type"] == "page" and not t["url"].startswith("devtools")]
                page = next((t for t in pages if prefer and prefer in t["url"]), None) or (None if prefer and any(t["url"].startswith("about:") for t in pages) and time.time() - t0 < 10 else (pages[0] if pages else None))
                if page: self.ws = WS(page["webSocketDebuggerUrl"]); break
            except Exception: pass
            time.sleep(0.4)
        if not self.ws: raise RuntimeError("no DevTools page")
        self.i = 0; self.call("Page.enable"); self.call("Runtime.enable")
    def call(self, m, params=None):
        self.i += 1; self.ws.send(json.dumps({"id": self.i, "method": m, "params": params or {}}))
        while True:
            x = json.loads(self.ws.recv())
            if x.get("id") == self.i: return x
    def js(self, expr, timeout=60):
        r = self.call("Runtime.evaluate", {"expression": expr, "awaitPromise": True, "returnByValue": True, "timeout": timeout * 1000}); res = r.get("result", {})
        if "exceptionDetails" in res: return "ERR " + str((res["exceptionDetails"].get("exception") or {}).get("description", ""))[:200]
        return res.get("result", {}).get("value")
    def shot(self, path):
        d = self.call("Page.captureScreenshot", {"format": "png"})["result"]["data"]; open(path, "wb").write(base64.b64decode(d)); return path

def port_listening(port):
    try: urllib.request.urlopen(f"http://127.0.0.1:{port}/api/ping", timeout=1); return True
    except Exception: return False

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--exe", default=os.path.join(HERE, "ClassicCartStudio", "bin", "Release", "net48", "ClassicCartStudio.exe"))
    ap.add_argument("--site", default=r"C:\NES\work\build\site"); ap.add_argument("--url", default=""); ap.add_argument("--site-port", type=int, default=4173)
    ap.add_argument("--data", default=""); ap.add_argument("--out", default=r"C:\NES\work\logs\desktop-poc"); ap.add_argument("--devtools", type=int, default=9350)
    ap.add_argument("--backend-port", type=int, default=8765); ap.add_argument("--keep", action="store_true"); ap.add_argument("--js", default="", help="extra JavaScript to evaluate (JSON result printed as EXTRA)")
    a = ap.parse_args(); os.makedirs(a.out, exist_ok=True)
    data = a.data or os.path.join(tempfile.gettempdir(), "ccs-poc-data")
    if not a.data: shutil.rmtree(data, ignore_errors=True)
    os.makedirs(data, exist_ok=True); json.dump({"port": a.backend_port, "hakchi_exe": os.path.join(data, "no-hakchi", "hakchi.exe"), "hakchi_args": ["/portable"], "exports_dir": os.path.join(data, "exports"), "staging_dir": os.path.join(data, "staging"), "no_launch": True, "allowed_origins": ([f"http://localhost:{a.site_port}", f"http://127.0.0.1:{a.site_port}"] if not a.url else [])}, open(os.path.join(data, "companion.json"), "w"))
    checks = []; ok = lambda k, v, n="": checks.append(f"{k}={'true' if v else 'false'}{(' ('+str(n)[:200]+')') if n and not v else ''}")
    srv = None; url = a.url
    if not url: srv = serve(a.site, a.site_port); url = f"http://localhost:{a.site_port}/index.html"
    env = dict(os.environ, NES_STUDIO_DATA=data, NES_PERSONAL_DIR=os.path.join(data, "personal"), NES_COMPANION_CONFIG=os.path.join(data, "companion.json"), CCS_DEV="1", CCS_BACKEND_SOURCE=os.path.join(TOOLS, "companion"), PYTHONIOENCODING="utf-8")
    if not a.url: env["CCS_SITE_URL"] = url
    ok("port_free_before", not port_listening(a.backend_port))
    t0 = time.time(); p = subprocess.Popen([a.exe, f"--devtools-port={a.devtools}"], env=env, cwd=os.path.dirname(a.exe))
    try:
        d = Dev(a.devtools, 60); ok("window_and_webview_up", True); t_dev = time.time() - t0
        for _ in range(200):
            if d.js("typeof COMPANION!=='undefined' && typeof render==='function' && document.querySelectorAll('#tb tr.pick').length>0") is True: break
            time.sleep(0.25)
        t_page = time.time() - t0
        ok("page_loaded_in_webview", d.js("document.querySelectorAll('#tb tr.pick').length>0") is True)
        ok("desktop_session_injected", d.js("!!(window.__CCS_DESKTOP&&window.__CCS_DESKTOP.token&&window.__CCS_DESKTOP.port)") is True)
        ok("page_recognizes_desktop_mode", d.js("COMPANION.desktop===true") is True)
        st = None
        for _ in range(240):
            st = d.js("COMPANION.state")
            if st in ("ok", "error", "unpaired", "off", "denied"): break
            time.sleep(0.25)
        t_ok = time.time() - t0
        ok("backend_connected_without_pairing", st == "ok", st)
        ok("pill_says_studio_for_windows", "Studio for Windows" in (d.js("$('connPill').textContent") or ""), d.js("$('connPill').textContent"))
        ok("artwork_rendered", (d.js("[...document.querySelectorAll('.parade img')].filter(i=>i.naturalWidth>0).length") or 0) >= 27, d.js("[...document.querySelectorAll('.parade img')].filter(i=>i.naturalWidth>0).length"))
        ok("covers_rendered", (d.js("[...document.querySelectorAll('#tb .thumb')].filter(i=>i.complete&&i.naturalWidth>0).length") or 0) >= 5)
        ok("version_handshake_is_desktop", d.js("(async()=>{ const r=await capi('/api/version'); return r.desktop===true && Array.isArray(r.capabilities); })()") is True)
        ok("status_authorized", d.js("(async()=>{ const r=await capi('/api/status'); return r.ok===true; })()") is True)
        d.js("try{ doneWelcome(); }catch(e){}"); time.sleep(0.5); d.shot(os.path.join(a.out, "poc-main.png"))
        d.js("openConnect()"); time.sleep(1.2); d.shot(os.path.join(a.out, "poc-connect.png")); conn = (d.js("$('collBody').textContent") or "")[:300]; d.js("closeColl()")
        ok("connect_dialog_has_no_pairing_ui", "Allow" not in conn and "Get the companion" not in conn and "Classic Cart Studio for Windows" in conn, conn[:160])
        extra = d.js(a.js) if a.js else None
        if a.js: print("EXTRA:", json.dumps(extra)[:1500])
        print(f"TIMINGS: window+webview {t_dev:.1f}s, page loaded {t_page:.1f}s, backend connected {t_ok:.1f}s")
        if a.keep: print("--keep: leaving the application open"); return 0
    except Exception as e:
        ok("harness", False, repr(e))
    finally:
        if not a.keep:
            subprocess.run(["powershell", "-NoProfile", "-Command", "Get-Process ClassicCartStudio -ErrorAction SilentlyContinue | ForEach-Object { $null = $_.CloseMainWindow() }"], capture_output=True)
            try: p.wait(timeout=15); ok("window_closes_cleanly", p.returncode == 0, p.returncode)
            except subprocess.TimeoutExpired: p.kill(); ok("window_closes_cleanly", False, "killed after 15 s")
            time.sleep(1.0); ok("backend_stopped_with_window", not port_listening(a.backend_port))
            if srv: srv.shutdown()
            log = os.path.join(data, "logs", "desktop.log"); print("desktop.log tail:", (open(log, encoding="utf-8-sig").read()[-600:] if os.path.exists(log) else "(none)").encode("ascii", "replace").decode())
    for c in checks: print(c)
    fails = [c for c in checks if "=false" in c]; print(("RESULT: PASS" if not fails else "RESULT: FAIL " + str(fails)) + f" ({len(checks)} checks)"); return 1 if fails else 0

if __name__ == "__main__": sys.exit(main())
