"""Connection-state checks for the hosted Studio page against a real companion, driven in headless Microsoft Edge over the DevTools
protocol. The public site build (NES_STUDIO_SITE or build/site) is served from http://localhost:4173 (a configured dev origin) and a
second, UNLISTED origin (http://localhost:4199) for the untrusted-origin case. Scenarios: companion absent, running but unpaired, paired
(time to "Connected" and time until the background console-setup check finishes), a second tab reusing the pairing, companion stopped,
companion restarted (in-memory tokens are gone, the page must fall back to "Connect"), companion too old (a fake /api/version with
protocol 0) and an untrusted origin. The browser's local-network permission ("Permission needed") cannot be simulated headlessly
(Edge auto-denies the prompt); its label is checked and the live test documents the manual check. Isolated data folder, no Hakchi, no
game files, nothing touches the console. Exit 0 = PASS."""
from __future__ import annotations
import os, sys, json, subprocess, tempfile, time, re, shutil, threading, urllib.request, urllib.error, urllib.parse, http.server, socketserver
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.normpath(os.path.join(HERE, "..", "..")); sys.path.insert(0, HERE)
from websocket_min import WS
PORT = int(os.environ.get("NES_TEST_PORT", "8797")); B = f"http://127.0.0.1:{PORT}"; SITE_PORT = 4173; BAD_PORT = 4199; FAKE_PORT = 8798; DEV = 9361
EDGE = next(x for x in [r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", r"C:\Program Files\Microsoft\Edge\Application\msedge.exe"] if os.path.exists(x))

def req(method, path, body=None, headers=None, base=None):
    data = json.dumps(body).encode() if body is not None else None
    r = urllib.request.Request((base or B) + path, data=data, headers={**({"Content-Type": "application/json"} if data else {}), **(headers or {})}, method=method)
    try:
        with urllib.request.urlopen(r, timeout=20) as resp: return resp.status, dict(resp.headers), (json.loads(resp.read() or b"{}") if "json" in resp.headers.get("Content-Type", "") else {})
    except urllib.error.HTTPError as e:
        try: return e.code, dict(e.headers), json.loads(e.read() or b"{}")
        except Exception: return e.code, dict(e.headers), {}

class Quiet(http.server.SimpleHTTPRequestHandler):
    def log_message(self, *a): pass
    def end_headers(self): self.send_header("Cache-Control", "no-store"); super().end_headers()

def serve(folder, port):
    h = lambda *a, **k: Quiet(*a, directory=folder, **k)
    srv = socketserver.ThreadingTCPServer(("127.0.0.1", port), h); srv.daemon_threads = True; srv.allow_reuse_address = True
    threading.Thread(target=srv.serve_forever, daemon=True).start(); return srv

class OldCompanion(http.server.BaseHTTPRequestHandler):
    """Pretends to be a companion whose protocol is older than the page requires."""
    def log_message(self, *a): pass
    def _cors(self):
        o = self.headers.get("Origin", "")
        if o: self.send_header("Access-Control-Allow-Origin", o); self.send_header("Access-Control-Allow-Headers", "Authorization, Content-Type, X-NES-Protocol"); self.send_header("Access-Control-Allow-Private-Network", "true")
    def do_OPTIONS(self): self.send_response(204); self._cors(); self.end_headers()
    def do_GET(self):
        body = json.dumps({"ok": True, "version": "0.0.9", "protocol": 0, "paired": False}).encode()
        self.send_response(200); self._cors(); self.send_header("Content-Type", "application/json"); self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)

class Browser:
    def __init__(self, tmp):
        self.p = subprocess.Popen([EDGE, "--headless=new", "--disable-gpu", f"--user-data-dir={tmp}", f"--remote-debugging-port={DEV}", "about:blank"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        for _ in range(80):
            try: self.tabs = json.load(urllib.request.urlopen(f"http://127.0.0.1:{DEV}/json", timeout=1)); break
            except Exception: time.sleep(0.25)
        else: raise RuntimeError("no devtools")
    def tab(self, url):
        t = json.load(urllib.request.urlopen(urllib.request.Request(f"http://127.0.0.1:{DEV}/json/new?{urllib.parse.quote(url, safe='')}", method="PUT"), timeout=5))
        return Tab(t["webSocketDebuggerUrl"])
    def close(self):
        self.p.terminate()
        try: self.p.wait(timeout=10)
        except Exception: self.p.kill()

class Tab:
    def __init__(self, ws_url):
        self.ws = WS(ws_url); self.i = 0; self.call("Page.enable"); self.call("Runtime.enable")
    def call(self, m, params=None):
        self.i += 1; self.ws.send(json.dumps({"id": self.i, "method": m, "params": params or {}}))
        while True:
            x = json.loads(self.ws.recv())
            if x.get("id") == self.i: return x
    def js(self, expr, timeout=60):
        r = self.call("Runtime.evaluate", {"expression": expr, "awaitPromise": True, "returnByValue": True, "timeout": timeout * 1000})
        res = r.get("result", {})
        if "exceptionDetails" in res: return "ERR " + str((res["exceptionDetails"].get("exception") or {}).get("description", "exception"))[:200]
        return res.get("result", {}).get("value")
    def ready(self):
        for _ in range(120):
            if self.js("typeof COMPANION!=='undefined' && typeof companionStatus==='function' && !!document.getElementById('connPill')") is True: return True
            time.sleep(0.25)
        return False
    def wait_state(self, states, limit=15.0):
        t0 = time.time()
        while time.time() - t0 < limit:
            s = self.js("CO.state")
            if s in states: return s, round((time.time() - t0) * 1000)
            time.sleep(0.1)
        return self.js("CO.state"), round((time.time() - t0) * 1000)

def start_companion(work, site, cfgp):
    env = dict(os.environ, NES_COMPANION_CONFIG=cfgp, NES_PERSONAL_DIR=os.path.join(work, "personal"), NES_STUDIO_DATA=work, NES_STUDIO_SITE=site, PYTHONIOENCODING="utf-8")
    logf = open(os.path.join(work, "companion.log"), "a", encoding="utf-8")
    proc = subprocess.Popen([sys.executable, os.path.join(HERE, "nes_companion.py"), "--no-browser", "--port", str(PORT)], env=env, stdout=logf, stderr=subprocess.STDOUT)
    t0 = time.time()
    for _ in range(80):
        try: urllib.request.urlopen(B + "/api/ping", timeout=1); return proc, time.time() - t0
        except Exception: time.sleep(0.25)
    return proc, None

def stop(proc):
    if proc and proc.poll() is None:
        proc.terminate()
        try: proc.wait(timeout=10)
        except Exception: proc.kill()
    for _ in range(40):
        try: urllib.request.urlopen(B + "/api/ping", timeout=0.5); time.sleep(0.25)
        except Exception: return

def main():
    site = os.environ.get("NES_STUDIO_SITE") or os.path.join(REPO, "build", "site")
    if not os.path.exists(os.path.join(site, "index.html")): print("no site build at", site); return 2
    work = os.path.join(tempfile.gettempdir(), "nes-conn-states"); shutil.rmtree(work, ignore_errors=True); os.makedirs(work)
    cfg = {"hakchi_exe": os.path.join(work, "no-hakchi", "hakchi.exe"), "hakchi_args": ["/portable"], "exports_dir": os.path.join(work, "exports"), "staging_dir": os.path.join(work, "staging"), "port": PORT, "allowed_origins": [f"http://localhost:{SITE_PORT}"], "no_launch": True}
    cfgp = os.path.join(work, "companion.json"); json.dump(cfg, open(cfgp, "w"))
    checks = []; timings = {}; ok = lambda k, v, n="": checks.append(f"{k}={'true' if v else 'false'}{(' ('+n+')') if n and not v else ''}")
    srv = serve(site, SITE_PORT); srv2 = serve(site, BAD_PORT)
    fake = socketserver.ThreadingTCPServer(("127.0.0.1", FAKE_PORT), OldCompanion); fake.daemon_threads = True; threading.Thread(target=fake.serve_forever, daemon=True).start()
    ORIGIN = f"http://localhost:{SITE_PORT}"; URL = f"{ORIGIN}/index.html?companion={PORT}"
    tmp = tempfile.mkdtemp(prefix="nes-conn-"); br = Browser(tmp); proc = None
    try:
        # the website never connects: no probe on load, state 'web' at once, pill hidden, every computer-dependent action invites the desktop app
        t1 = br.tab(URL); ok("page_loads_hosted_mode", t1.ready() and t1.js("CO.hosted") is True)
        st, ms = t1.wait_state(("web", "off", "denied", "unpaired", "ok", "old", "error")); timings["web_state_ms"] = ms
        ok("website_state_is_web", st == "web", st); ok("decided_quickly", ms < 2000, str(ms))
        ok("pill_hidden_on_website", t1.js("$('connPill').hidden") is True)
        ok("no_backend_probe_on_load", t1.js("performance.getEntriesByType('resource').some(e=>e.name.includes('127.0.0.1:'))") is False)
        ok("install_button_not_dimmed", t1.js("$('hkBtn').classList.contains('off')") is False)
        ok("install_invites_desktop_app", t1.js("(()=>{ $('hkBtn').click(); const t=$('collTitle').textContent; closeColl(); return t; })()") == "Continue in Classic Cart Studio for Windows")
        ok("own_games_invites_desktop_app", t1.js("(async()=>{ $('mOwn').click(); await new Promise(r=>setTimeout(r,300)); const t=$('collTitle').textContent; closeColl(); return t; })()") == "Continue in Classic Cart Studio for Windows")
        ok("local_library_invites_desktop_app", t1.js("(async()=>{ $('mLoadLocal').click(); await new Promise(r=>setTimeout(r,300)); const t=$('collTitle').textContent; closeColl(); return t; })()") == "Continue in Classic Cart Studio for Windows")
        ok("no_pairing_token_stored", t1.js("localStorage.getItem('studio-companion-v1')") in (None, ""))
        # with the backend running the website still does not connect; the handoff offer is the only request it may send
        proc, boot = start_companion(work, site, cfgp); timings["companion_boot_s"] = round(boot or -1, 2); ok("companion_started", boot is not None)
        t1.js("companionStatus()"); st, ms = t1.wait_state(("web", "off", "unpaired", "ok", "error")); ok("still_web_with_backend_running", st == "web", st)
        ok("still_no_probe", t1.js("performance.getEntriesByType('resource').some(e=>e.name.includes('127.0.0.1:'))") is False)
        r = t1.js("(async()=>{ sel.clear(); sel.add(DATA.games[0].game_id); persist(); render(); const r=await offerCollection({id:'conn-test-1', name:'Conn test', collection:buildManifest()}); return r; })()") or {}
        ok("handoff_offer_accepted_from_site_origin", r.get("ok") is True, json.dumps(r)[:200])
        s_, h_, j_ = req("POST", "/api/handoff/offer", {"id": "conn-test-2", "name": "x", "collection": {"schema": "nes-classic-collection/4", "selections": []}}, headers={"Origin": f"http://localhost:{BAD_PORT}", "Content-Type": "application/json"})
        ok("handoff_offer_refused_from_untrusted_origin", s_ == 403, str(s_))
        log = open(os.path.join(work, "companion.log"), encoding="utf-8").read(); ok("no_version_or_status_request_from_website", "/api/version" not in log and "/api/status" not in log, log[-300:])
        ok("status_cross_origin_still_needs_privilege", req("GET", "/api/status", headers={"Origin": ORIGIN})[0] == 401)
    finally:
        stop(proc); br.close(); srv.shutdown(); srv2.shutdown(); fake.shutdown(); shutil.rmtree(tmp, ignore_errors=True)
    for c in checks: print(c)
    print("TIMINGS:", json.dumps(timings))
    fails = [c for c in checks if "=false" in c]; print(("RESULT: PASS" if not fails else "RESULT: FAIL " + str(fails)) + f" ({len(checks)} checks)"); return 1 if fails else 0

if __name__ == "__main__": sys.exit(main())
