"""NES Classic catalog companion — a loopback-only local server (no network access, no background service: it runs
while the window is open and exits with Ctrl+C or when the Start-Catalog window is closed).

It serves reports/ (the catalog page, art and brand assets) from http://127.0.0.1:<port>/ so the page is same-origin
with the API, and exposes three JSON endpoints that only the served page can call:
  GET  /api/status            environment (hakchi path/hash check, running, console attached) — no secrets
  POST /api/preview           body {manifest} -> validates, saves reports/exports/<ts>-selection.json, returns the plan
  POST /api/apply             body {manifest, manifest_file} -> transactional PC-side staging into hakchi, verify, launch
  POST /api/install-hakchi    body {dry_run?} -> set up the pinned Hakchi2 CE 3.9.3 portable build from the verified package (only when absent)
  POST /api/install-driver    body {dry_run?} -> run hakchi's bundled console driver installer elevated (UAC; only when the driver is absent)
  GET  /api/library/local     read-only view of the console-management library on this computer (factory + custom games, checked state, content matches)
  GET  /api/personal          the personal game entries (Add Your Own Game / adopted local games)
  POST /api/personal/pick     native file picker -> identify (exact content match or new personal entry with a managed copy)
  POST /api/personal/relink   body {id} -> picker; accepted only when the content hash matches the entry
  POST /api/personal/update   body {id, user:{title, version_label, description, year, publisher}}
  POST /api/personal/art      body {id} -> picker (image) -> stored thumbnail
  POST /api/personal/remove   body {id, delete_copy?}
  POST /api/library/adopt     create personal entries for local custom games the inventory does not know (read-only towards Hakchi)
  POST /api/personal/ingest-path  body {path}  (rehearsal/test configurations only: no dialog)
  GET  /api/version           public: app id, version, protocol number, whether this caller is the desktop app (CORS for allow-listed origins only)
  POST /api/import/pick       body {mode: files|folder} -> native picker on this computer -> background import job {job}
  GET  /api/import/status?job=..  progress, per-file results, errors; POST /api/import/cancel {job}
  GET  /api/library/store     managed copies of known versions (files the person imported) — what can be prepared without the dev store
  POST /api/library/locate    body {version_id} -> picker; accepted only when the file's content hash is that version
Security: binds 127.0.0.1 only and refuses any Host header other than 127.0.0.1/localhost (DNS rebinding). Two callers exist:
  * the page served by the companion itself (same origin): POSTs carry X-NES-Token = the per-process token embedded in the page;
  * Classic Cart Studio for Windows (its window shows the bundled site under an allow-listed origin): CORS with an exact-origin
    allow list, OPTIONS preflight (incl. Private Network Access), and Authorization: Bearer <the per-run host token>.
  The public website never authenticates: it may only hand over a collection or a .ccs package (handoff endpoints below).
Tokens never appear in URLs or in the public page source; a request whose Origin is not allow-listed gets no CORS headers (the
browser blocks the response) and a 403. No generic command or path parameters are accepted (the hakchi executable comes from
companion.json and must hash-match the recorded 3.9.3 build); manifests are written only under the exports folder. It never
touches the console.
"""
from __future__ import annotations
import os, sys, json, secrets, time, re, mimetypes, http.server, socketserver, threading, webbrowser, argparse, hashlib, urllib.parse
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import hakchi_bridge as hb
import bundle_integrity   # noqa: E402  the installation's integrity (nothing is ever downloaded to repair it)
import hakchi_inspect as hi
import personal as ps
import console_runs as cr   # console engine runs (simulation tier; Gate G1 closed in code inside the worker)
from version import VERSION, PROTOCOL, APP_ID, SITE_ORIGINS, SITE_URL
from common import SITE_ROOT, DATA_ROOT, FROZEN, data_path, art_alias

CFG = hb.load_cfg(); TOKEN = secrets.token_urlsafe(24); ROOT = SITE_ROOT
mimetypes.add_type("application/json", ".json")
LOCK = threading.Lock()
ORIGIN_RE = re.compile(r"^https://[a-z0-9.-]+$|^http://(127\.0\.0\.1|localhost)(:\d+)?$")
def allowed_origins(port: int) -> set:
    own = {f"http://127.0.0.1:{port}", f"http://localhost:{port}"}
    extra = {o.rstrip("/") for o in (CFG.get("allowed_origins") or []) if isinstance(o, str) and ORIGIN_RE.match(o.rstrip("/"))}
    return set(SITE_ORIGINS) | own | extra
ALLOWED: set = set()


# ---- desktop host channel: Classic Cart Studio for Windows starts this backend headless and hands it a per-run token through the
#      environment. Requests carrying that token (Authorization: Bearer) from the website's origin are the "desktop" principal:
#      an exact-origin, authenticated caller (the only one besides this server's own page). The token never appears in URLs, logs or pages of other origins.
HOST_TOKEN = os.environ.pop("CCS_HOST_TOKEN", "") or ""
HOST_PID = int(os.environ.pop("CCS_HOST_PID", "0") or 0)
CAPABILITIES = ["library", "personal", "import", "locate", "prepare", "handoff", "ccs-package", "ccs-package-2", "package-save", "desktop-state", "shutdown", "console-sim", "native-home-package", "native-preview", "content-overrides"]
SERVER_REF: dict = {"srv": None}
def _desktop_principal(auth: str, origin: str) -> dict | None:
    if not HOST_TOKEN or not auth.startswith("Bearer "): return None
    tok = auth[7:].strip()
    if len(tok) != len(HOST_TOKEN) or not secrets.compare_digest(tok, HOST_TOKEN): return None
    if origin not in ALLOWED: return None
    return {"origin": origin, "expires": 2 ** 31, "desktop": True, "token": "desktop"}

# ---- website -> desktop collection handoff: a bounded inbox of offered collections (the website may only ADD; only the desktop
#      principal can list, take or discard). Non-destructive, idempotent by offer id, expires, capped. ----
HANDOFF: dict = {}
HANDOFF_MAX, HANDOFF_TTL, HANDOFF_MAX_BYTES, HANDOFF_RATE = 8, 20 * 60, 32_000_000, 30   # a complete library with loadouts fits
_handoff_times: list = []
OFFER_ID = re.compile(r"^[A-Za-z0-9-]{8,64}$")
def handoff_path(): return data_path("handoff-inbox.json")
def handoff_load():
    try:
        j = json.load(open(handoff_path(), encoding="utf-8"))
        if isinstance(j, dict): HANDOFF.update({k: v for k, v in j.items() if isinstance(v, dict)})
    except Exception: pass
def handoff_save():
    try:
        os.makedirs(DATA_ROOT, exist_ok=True); tmp = handoff_path() + ".tmp"
        json.dump(HANDOFF, open(tmp, "w", encoding="utf-8")); os.replace(tmp, handoff_path())
    except Exception: pass
def handoff_prune():
    now = time.time()
    for k in [k for k, v in HANDOFF.items() if now - v.get("received", 0) > HANDOFF_TTL]: HANDOFF.pop(k, None)
    while len(HANDOFF) > HANDOFF_MAX: HANDOFF.pop(min(HANDOFF, key=lambda k: HANDOFF[k].get("received", 0)), None)
def handoff_validate(body: dict) -> tuple[dict | None, str]:
    oid = str(body.get("id") or "")
    if not OFFER_ID.match(oid): return None, "The handoff could not be read. Reload the page and try again."
    man = body.get("collection")
    if not isinstance(man, dict): return None, "The collection could not be read. Reload the page and try again."
    if not isinstance(man.get("schema"), str) or not man["schema"].startswith("nes-classic-collection/"): return None, "This collection comes from an unknown or newer version of Classic Cart Studio. Update the Windows app, then try again."
    games = man.get("selections") if isinstance(man.get("selections"), list) else man.get("games")   # schema 4 uses "selections"; older files "games"
    if not isinstance(games, list) or len(games) > 2000: return None, "The collection has more than 2000 games, or could not be read. Use Export Collection (.ccs) instead."
    for g in games:
        gid = (g.get("version_id") or g.get("game_id")) if isinstance(g, dict) else None
        if not isinstance(gid, str) or not gid or len(gid) > 128: return None, "A game in the collection has no version. Reload the page and try again."
    name = str(man.get("name") or body.get("name") or "Collection")[:120]
    return {"id": oid, "name": name, "count": len(games), "schema": man["schema"], "collection": man, "fingerprint": hashlib.sha256(json.dumps(man, sort_keys=True, separators=(",", ":")).encode()).hexdigest()}, ""

def desktop_state_path(): return data_path("desktop-state.json")
def home_state_path(): return data_path("home-state.json")
# ---- native HOME packages (N2): one build at a time, in the background, from the configuration the page sends ----
NATIVE_JOBS: dict[str, dict] = {}

def native_package_start(manifest, home) -> dict:
    """starts building the console's native HOME package from the user's collection and HOME configuration (staged on this PC
    only; nothing is sent to a console). One build at a time; a second request while one runs returns that job."""
    with LOCK:
        run = next((j for j in NATIVE_JOBS.values() if j["state"] == "running"), None)
        if run: return {"ok": False, "busy": True, "job": run["id"], "error": "A native HOME package is already being prepared. Wait for it to finish."}
        jid = secrets.token_hex(6); job = {"id": jid, "state": "running", "started": time.time(), "progress": {"stage": "configuration", "done": 0, "total": 0, "text": ""}, "result": None}
        NATIVE_JOBS.clear(); NATIVE_JOBS[jid] = job
    def progress(stage, done, total, text=""):
        with LOCK: job["progress"] = {"stage": stage, "done": done, "total": total, "text": str(text)[:120]}
    def run():
        try:
            from native_home import package as nhp
            res = nhp.build(manifest, home, progress=progress)
        except Exception as e: res = {"ok": False, "status": "failed", "ready": False, "error": f"The native HOME package could not be prepared ({e}). Try again; your collection is unchanged."}
        with LOCK: job["result"] = res; job["state"] = "done"; job["finished"] = time.time()
    threading.Thread(target=run, daemon=True, name="native-home-package").start()
    return {"ok": True, "job": jid}

def native_package_status(jid: str) -> dict | None:
    with LOCK:
        j = NATIVE_JOBS.get(jid)
        return None if not j else {"id": j["id"], "state": j["state"], "progress": dict(j["progress"]), "result": j["result"]}

def home_state_load() -> dict:
    try: return json.load(open(home_state_path(), encoding="utf-8"))
    except Exception: return {}
def home_state_save(state: dict) -> dict:
    state = dict(state); state["saved_at"] = time.strftime("%Y-%m-%dT%H:%M:%S"); tmp = home_state_path() + ".tmp"
    json.dump(state, open(tmp, "w", encoding="utf-8"), ensure_ascii=False); os.replace(tmp, home_state_path()); return state
def desktop_state_load() -> dict:
    try:
        j = json.load(open(desktop_state_path(), encoding="utf-8")); return j if isinstance(j, dict) else {}
    except Exception: return {}
def desktop_state_save(state: dict) -> dict:
    os.makedirs(DATA_ROOT, exist_ok=True); p = desktop_state_path(); tmp = p + ".tmp"
    state = dict(state); state["saved_at"] = time.strftime("%Y-%m-%dT%H:%M:%S")
    json.dump(state, open(tmp, "w", encoding="utf-8"));
    if os.path.exists(p):
        try: os.replace(p, p + ".bak")
        except Exception: pass
    os.replace(tmp, p); return state

def host_watchdog():
    """When the desktop host that started us goes away, stop too (no orphaned backend holding the port)."""
    if not HOST_PID: return
    import ctypes
    k32 = ctypes.windll.kernel32; SYNCHRONIZE = 0x00100000
    h = k32.OpenProcess(SYNCHRONIZE, False, HOST_PID)
    if not h: return
    def run():
        k32.WaitForSingleObject(h, 0xFFFFFFFF)   # INFINITE: returns when the host process ends
        srv = SERVER_REF.get("srv")
        if srv: threading.Thread(target=srv.shutdown, daemon=True).start()
    threading.Thread(target=run, daemon=True, name="host-watchdog").start()


def save_manifest(man: dict) -> str:
    os.makedirs(CFG["exports_dir"], exist_ok=True)
    stamp = time.strftime("%Y%m%d-%H%M%S"); p = os.path.join(CFG["exports_dir"], f"{stamp}-selection.json")
    k = 2
    while os.path.exists(p): p = os.path.join(CFG["exports_dir"], f"{stamp}-selection-{k}.json"); k += 1
    open(p, "w", encoding="utf-8").write(json.dumps(man, indent=1)); return p

class H(http.server.BaseHTTPRequestHandler):
    server_version = "nes-companion/1.0"
    protocol_version = "HTTP/1.1"   # keep-alive: Chromium reuses the connection it opened for a CORS preflight for the actual request; an
                                    # HTTP/1.0 server closing after the preflight left those requests waiting until the page's timeout
    def log_message(self, fmt, *a):
        if "/api/" in (a[0] if a else "") and sys.stderr is not None:   # a windowed (frozen) build may have no stderr
            try: sys.stderr.write("%s - %s\n" % (self.address_string(), fmt % a))
            except Exception: pass
    def _cors(self):
        """CORS headers only for an exact allow-listed Origin; anything else gets none (the browser then withholds the response)."""
        o = self.headers.get("Origin", "")
        if o and o in ALLOWED:
            self.send_header("Access-Control-Allow-Origin", o); self.send_header("Vary", "Origin"); self.send_header("Access-Control-Expose-Headers", "X-NES-Protocol, X-NES-Version")
        self.send_header("X-NES-Protocol", str(PROTOCOL)); self.send_header("X-NES-Version", VERSION)
    def _json(self, code: int, obj: dict):
        try:   # an unread request body must not leak into the next request on a kept-alive connection
            if int(self.headers.get("Content-Length", "0") or 0) > 0 and not getattr(self, "_body_read", False): self.close_connection = True
        except ValueError: self.close_connection = True
        b = json.dumps(obj).encode("utf-8"); self.send_response(code); self.send_header("Content-Type", "application/json; charset=utf-8"); self.send_header("Content-Length", str(len(b)))
        self.send_header("Cache-Control", "no-store"); self.send_header("X-Content-Type-Options", "nosniff"); self._cors(); self.end_headers(); self.wfile.write(b)
    def _host_ok(self) -> bool:
        """Defense in depth against DNS rebinding: the server answers only when addressed as 127.0.0.1 or localhost (any other Host header is refused outright)."""
        return bool(re.match(r"^(127\.0\.0\.1|localhost)(:\d+)?$", self.headers.get("Host", "")))
    def _origin(self) -> str:
        o = self.headers.get("Origin") or ""
        if not o and self.headers.get("Referer"): o = "/".join(self.headers["Referer"].split("/", 3)[:3])
        return o.rstrip("/")
    def _same_origin(self) -> bool:
        host = self.headers.get("Host", ""); o = self._origin()
        return self._host_ok() and bool(re.match(r"^http://(127\.0\.0\.1|localhost)(:\d+)?$", o)) and o.split("//", 1)[1] == host
    def _cross_site(self) -> bool:
        """True when the browser says the request did not come from this server's own origin (Origin header or Sec-Fetch-Site)."""
        o = self._origin(); sfs = self.headers.get("Sec-Fetch-Site", "")
        if o: return not self._same_origin()
        return sfs not in ("", "same-origin", "none")
    def _bearer(self) -> dict | None:
        """The desktop host's per-run token, from an allow-listed Origin (the only bearer this server accepts)."""
        a = self.headers.get("Authorization", ""); o = self._origin()
        if not a.startswith("Bearer ") or not o or o not in ALLOWED: return None
        return _desktop_principal(a, o)
    def _protocol_ok(self) -> bool:
        try: return int(self.headers.get("X-NES-Protocol") or PROTOCOL) <= PROTOCOL
        except ValueError: return False
    def _authed(self) -> bool:
        """Same-origin page with the per-process token, or the desktop app with its host token."""
        if not self._host_ok(): return False
        if self._same_origin() and secrets.compare_digest(self.headers.get("X-NES-Token", ""), TOKEN): return True
        return self._bearer() is not None
    def do_OPTIONS(self):
        """CORS preflight (incl. Chrome/Edge Private Network Access) for allow-listed origins only."""
        o = self.headers.get("Origin", "")
        if not self._host_ok() or o not in ALLOWED: self.send_response(403); self.send_header("Content-Length", "0"); self.end_headers(); return
        self.send_response(204); self._cors(); self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS"); self.send_header("Access-Control-Allow-Headers", "Content-Type, Authorization, X-NES-Protocol")
        self.send_header("Access-Control-Max-Age", "600")
        if self.headers.get("Access-Control-Request-Private-Network", "").lower() == "true": self.send_header("Access-Control-Allow-Private-Network", "true")
        if self.headers.get("Access-Control-Request-Local-Network", "").lower() == "true": self.send_header("Access-Control-Allow-Local-Network", "true")
        self.send_header("Content-Length", "0"); self.end_headers()
    def _html(self, code: int, html: str):
        b = html.encode("utf-8"); self.send_response(code); self.send_header("Content-Type", "text/html; charset=utf-8"); self.send_header("Content-Length", str(len(b))); self.send_header("Cache-Control", "no-store")
        self.send_header("X-Frame-Options", "DENY"); self.send_header("Content-Security-Policy", "frame-ancestors 'none'; default-src 'self' 'unsafe-inline'"); self.send_header("Referrer-Policy", "no-referrer"); self.end_headers(); self.wfile.write(b)
    def _body(self) -> dict:
        n = int(self.headers.get("Content-Length", "0") or 0)
        if n > 20_000_000: raise ValueError("body too large")
        self._body_read = True
        return json.loads(self.rfile.read(n).decode("utf-8") or "{}")
    def do_GET(self):
        path, _, qs = self.path.partition("?")
        if not self._host_ok(): return self._json(403, {"ok": False, "error": "forbidden host"})
        if path == "/api/ping": return self._json(200, {"ok": True})
        if path == "/api/version":   # public discovery endpoint: no local paths, no secrets
            b = self._bearer(); return self._json(200, {"ok": True, "app": APP_ID, "version": VERSION, "protocol": PROTOCOL, "paired": b is not None, "desktop": bool(b and b.get("desktop")), "expires": int(b["expires"]) if b else None, "pair_required": self._cross_site(), "frozen": FROZEN, "capabilities": CAPABILITIES, "handoff_pending": len(HANDOFF) if (b and b.get("desktop")) else None})
        if path.startswith("/api/"):
            if not self._protocol_ok(): return self._json(426, {"ok": False, "error": "Classic Cart Studio for Windows on this computer is older than this website expects. Install the latest version from ClassicCartStudio.com/app.", "protocol": PROTOCOL, "version": VERSION})
            if self._cross_site() and not self._bearer(): return self._json(401, {"ok": False, "error": "Only Classic Cart Studio for Windows can use this. Open the Windows app on this computer.", "pair_required": True})
        if path.startswith(("/api/overrides", "/overrides/")): import overrides as ov; return ov.handle_get(self, path, qs)   # N3 content customization (desktop principal; images like personal art)
        if path.startswith("/api/preview/"): import native_preview as npv; return npv.handle_get(self, path, qs)   # N3: TV Preview drawn by the native HOME renderer (desktop principal)
        if path == "/api/status":
            q = dict(p.split("=", 1) for p in qs.split("&") if "=" in p); wait = min(float(q.get("wait", "0") or 0), 30.0)
            return self._json(200, {"ok": True, **status_payload(wait), "bundle": bundle_integrity.check(), "version": VERSION, "protocol": PROTOCOL, "dev_store": os.path.exists(hb.DB_PATH)})
        if path == "/api/bundle":   # the installation's integrity: every installed file present with its size; ?full=1 also checks every SHA-256
            q = dict(p.split("=", 1) for p in qs.split("&") if "=" in p)
            return self._json(200, {"ok": True, "bundle": bundle_integrity.check(full=q.get("full") == "1")})
        if path == "/api/import/status":
            q = dict(p.split("=", 1) for p in qs.split("&") if "=" in p); j = ps.job_status(q.get("job", ""))
            return self._json(200 if j else 404, {"ok": bool(j), "job": j, "error": None if j else "That import is no longer running. Start it again."})
        if path == "/api/library/store":
            st = ps.load_store(); return self._json(200, {"ok": True, "store": {k: {"version_id": k, "present": os.path.exists(v["path"]), "format": v.get("format"), "file_size": v.get("file_size"), "source_name": v.get("source_name"), "added": v.get("added")} for k, v in st.items()}})
        if path == "/api/library/local":
            try: return self._json(200, {"ok": True, "library": ps.read_local_library(os.path.dirname(CFG["hakchi_exe"]))})
            except Exception as e: return self._json(500, {"ok": False, "error": f"Could not read the games set up in Hakchi on this computer ({e})."})
        if path == "/api/personal": return self._json(200, {"ok": True, "entries": [ps.public(e) for e in ps.load_index()["entries"].values()]})
        if path == "/api/handoff/pending":
            b = self._bearer()
            if not (b and b.get("desktop")): return self._json(403, {"ok": False, "error": "Available only in Classic Cart Studio for Windows."})
            with LOCK: handoff_prune(); items = sorted(HANDOFF.values(), key=lambda v: v.get("received", 0), reverse=True)
            return self._json(200, {"ok": True, "pending": [{k: v[k] for k in ("id", "name", "count", "schema", "received", "fingerprint", "taken_at") if k in v} for v in items]})
        if path in ("/api/package/preview", "/api/package/file"):   # .ccs packages staged on this computer: desktop application only
            b = self._bearer()
            if not (b and b.get("desktop")): return self._json(403, {"ok": False, "error": "Available only in Classic Cart Studio for Windows."})
            import package_api as pk; return pk.handle_get(self, path, qs, data_path)
        if path == "/api/desktop/state":
            b = self._bearer()
            if not (b and b.get("desktop")): return self._json(403, {"ok": False, "error": "Available only in Classic Cart Studio for Windows."})
            return self._json(200, {"ok": True, "state": desktop_state_load()})
        if path == "/api/home/state":   # console HOME model extras (folders, favorites, history, sorts, theme, shortcuts): desktop application only
            b = self._bearer()
            if not (b and b.get("desktop")): return self._json(403, {"ok": False, "error": "Available only in Classic Cart Studio for Windows."})
            return self._json(200, {"ok": True, "home": home_state_load()})
        if path == "/api/home/native-package":   # a build's progress and result, or (no job) the current staged package
            b = self._bearer()
            if not (b and b.get("desktop")): return self._json(403, {"ok": False, "error": "Available only in Classic Cart Studio for Windows."})
            q = urllib.parse.parse_qs(self.path.split("?", 1)[1] if "?" in self.path else ""); jid = re.sub(r"[^0-9a-f]", "", (q.get("job") or [""])[0])[:12]
            if jid:
                st = native_package_status(jid); return self._json(200 if st else 404, {"ok": bool(st), **(st or {"error": "That preparation is no longer running. Start it again."})})
            from native_home import package as nhp
            m = nhp.inspect()
            return self._json(200, {"ok": True, "package": m and {k: m.get(k) for k in ("mode", "installable", "not_installable_reason", "configuration", "sizes", "warnings", "blockers")}, "root": nhp.package_root()})
        if path == "/api/home/console":   # the MOCK console store (simulation; Gate G1 not approved, no device)
            b = self._bearer()
            if not (b and b.get("desktop")): return self._json(403, {"ok": False, "error": "Available only in Classic Cart Studio for Windows."})
            import home_sync as hs; return self._json(200, {"ok": True, "simulation": True, "console": hs.MockConsoleStore(data_path("mock-console.json")).load()})
        if path.startswith("/api/console/"):   # console engine (simulation tier): desktop application only; the website never sees these
            b = self._bearer()
            if not (b and b.get("desktop")): return self._json(403, {"ok": False, "error": "Available only in Classic Cart Studio for Windows."})
            if path == "/api/console/capabilities":
                r = cr.call("capabilities", {}, timeout=60); return self._json(200 if r.get("event") == "result" else 500, {"ok": r.get("event") == "result", **({"capabilities": r.get("result")} if r.get("event") == "result" else {"error": r.get("text")})})
            if path == "/api/console/run":
                q = urllib.parse.parse_qs(self.path.split("?", 1)[1] if "?" in self.path else ""); rid = (q.get("id") or [""])[0]
                try: cur = int((q.get("cursor") or ["0"])[0])
                except ValueError: cur = 0
                r = cr.poll(re.sub(r"[^A-Za-z0-9-]", "", rid)[:40], max(0, cur))
                return self._json(200 if r else 404, {"ok": bool(r), **(r or {"error": "That rehearsal is no longer running. Start it again."})})
            return self._json(404, {"ok": False, "error": "unknown endpoint"})
        if path.startswith("/api/"): return self._json(404, {"ok": False, "error": "unknown endpoint"})
        if path == "/": path = "/nes-catalog.html"
        rel = os.path.normpath(path.lstrip("/")).replace("\\", "/")
        if rel.startswith("..") or rel.startswith("/") or rel.startswith("rollback") or rel.startswith("exports") or rel.startswith("hakchi-staging") or rel.startswith("personal/files") or rel.startswith("personal/library") or rel.endswith(".json") and rel.startswith("personal/"): return self._json(403, {"ok": False, "error": "forbidden"})
        fp = os.path.join(DATA_ROOT if rel.startswith("personal/art/") else ROOT, rel)
        if not os.path.isfile(fp) and rel.startswith("art/"): fp = os.path.join(ROOT, art_alias(rel))   # the installed site keeps one copy of byte-identical covers
        if not os.path.isfile(fp): self.send_response(404); self.send_header("Content-Length", "0"); self.end_headers(); return   # keep-alive: an empty, complete response
        if rel == "nes-catalog.html":
            html = open(fp, encoding="utf-8").read().replace('<meta name="nes-companion-token" content="">', f'<meta name="nes-companion-token" content="{TOKEN}">', 1).encode("utf-8")
            self.send_response(200); self.send_header("Content-Type", "text/html; charset=utf-8"); self.send_header("Content-Length", str(len(html))); self.send_header("Cache-Control", "no-store"); self.end_headers(); self.wfile.write(html); return
        ctype = mimetypes.guess_type(fp)[0] or "application/octet-stream"
        with open(fp, "rb") as f: data = f.read()
        self.send_response(200); self.send_header("Content-Type", ctype); self.send_header("Content-Length", str(len(data))); self.send_header("Cache-Control", "max-age=3600"); self.end_headers(); self.wfile.write(data)
    def _handoff_offer(self):
            o = self._origin()
            if o not in ALLOWED or o in (f"http://127.0.0.1:{PORT}", f"http://localhost:{PORT}"): return self._json(403, {"ok": False, "error": "This website is not allowed to connect to Classic Cart Studio on this computer."})   # the website's origin (or a listed dev origin), never this backend's own page
            n = int(self.headers.get("Content-Length", "0") or 0)
            if n > HANDOFF_MAX_BYTES: return self._json(413, {"ok": False, "error": "The collection is too large to hand over directly. Use Export Collection (.ccs) instead."})
            now = time.time(); _handoff_times[:] = [t for t in _handoff_times if now - t < 60]
            if len(_handoff_times) >= HANDOFF_RATE: return self._json(429, {"ok": False, "error": "Too many requests in the last minute. Wait a minute and try again."})
            _handoff_times.append(now)
            self._body_read = True
            try: body = json.loads(self.rfile.read(n).decode("utf-8") or "{}")
            except Exception: return self._json(400, {"ok": False, "error": "The collection could not be read. Reload the page and try again."})
            item, err = handoff_validate(body if isinstance(body, dict) else {})
            if not item: return self._json(400, {"ok": False, "error": err})
            with LOCK:
                handoff_prune(); prev = HANDOFF.get(item["id"])
                if prev and prev.get("fingerprint") == item["fingerprint"]: return self._json(200, {"ok": True, "id": item["id"], "duplicate": True, "pending": len(HANDOFF)})
                dup = next((v for v in HANDOFF.values() if v.get("fingerprint") == item["fingerprint"] and not v.get("taken_at")), None)
                if dup: return self._json(200, {"ok": True, "id": dup["id"], "duplicate": True, "pending": len(HANDOFF)})
                item["received"] = now; item["origin"] = o; HANDOFF[item["id"]] = item; handoff_save()
            try: sys.stderr.write(f"handoff offer {item['id']} {item['count']} games from {o}\n")
            except Exception: pass
            return self._json(200, {"ok": True, "id": item["id"], "duplicate": False, "pending": len(HANDOFF)})
    def do_POST(self):
        path = self.path.split("?", 1)[0]
        if not path.startswith("/api/"): return self._json(404, {"ok": False, "error": "unknown endpoint"})
        if not self._host_ok(): return self._json(403, {"ok": False, "error": "forbidden host"})
        if not self._protocol_ok(): return self._json(426, {"ok": False, "error": "Classic Cart Studio for Windows on this computer is older than this website expects. Install the latest version from ClassicCartStudio.com/app.", "protocol": PROTOCOL, "version": VERSION})
        if path == "/api/handoff/offer": return self._handoff_offer()
        if path == "/api/handoff/package":   # a .ccs the person chose on the website, handed to this app on this computer (never readable back by a website)
            o = self._origin()
            if o not in ALLOWED or o in (f"http://127.0.0.1:{PORT}", f"http://localhost:{PORT}"): return self._json(403, {"ok": False, "error": "This website is not allowed to connect to Classic Cart Studio on this computer."})
            now = time.time(); _handoff_times[:] = [t for t in _handoff_times if now - t < 60]
            if len(_handoff_times) >= HANDOFF_RATE: return self._json(429, {"ok": False, "error": "Too many requests in the last minute. Wait a minute and try again."})
            _handoff_times.append(now)
            import package_api as pk; return pk.handle_website_package(self, data_path)
        if not self._authed():
            return self._json(401 if self._cross_site() and self._origin() in ALLOWED else 403, {"ok": False, "error": "Only Classic Cart Studio for Windows can use this. Open the Windows app on this computer." if self._cross_site() else "This request is not authorized. Reload the page and try again.", "pair_required": self._cross_site()})
        if path.startswith("/api/package/"):   # .ccs packages: desktop application only; before _body(): uploads stream to disk
            b = self._bearer()
            if not (b and b.get("desktop")): return self._json(403, {"ok": False, "error": "Available only in Classic Cart Studio for Windows."})
            import package_api as pk; return pk.handle_post(self, path, data_path, VERSION, CFG)
        if path.startswith("/api/overrides/"): import overrides as ov; return ov.handle_post(self, path, CFG, ps.pick_file)   # N3: before _body(): image uploads stream to disk
        try: body = self._body()
        except Exception as e: return self._json(400, {"ok": False, "error": f"The request could not be read ({e})."})
        if path.startswith("/api/preview/"): import native_preview as npv; return npv.handle_post(self, path, body)   # N3: TV Preview (desktop principal)
        if path in ("/api/handoff/take", "/api/handoff/discard", "/api/desktop/state", "/api/shutdown") or path.startswith("/api/console/") or path.startswith("/api/home/"):
            b = self._bearer()
            if not (b and b.get("desktop")): return self._json(403, {"ok": False, "error": "Available only in Classic Cart Studio for Windows."})
            if path == "/api/shutdown":
                self._json(200, {"ok": True, "stopping": True}); cr.shutdown(); srv = SERVER_REF.get("srv")
                if srv: threading.Thread(target=srv.shutdown, daemon=True).start()
                return
            if path.startswith("/api/console/"):
                # Console engine, simulation tier. Every call goes to an isolated worker process over the line protocol; the
                # worker addresses plans by hash and can only open the fake transport. `execute` is forwarded on purpose so the
                # refusal (Gate G1, in code) is proven end to end; nothing in this backend can open the gate.
                op = path[len("/api/console/"):]; prof = re.sub(r"[^a-z0-9-]", "", str(body.get("profile") or ""))[:40]
                if op == "identify": r = cr.call("identify", {"profile": prof or "no-device"}); return self._json(200 if r.get("event") == "result" else 400, {"ok": r.get("event") == "result", "device": r.get("result"), "error": r.get("text")})
                if op == "plan":
                    man = body.get("manifest")
                    if not isinstance(man, dict): return self._json(400, {"ok": False, "error": "No collection was sent. Reload the page and try again."})
                    r = cr.call("plan", {"manifest": man, "profile": prof or None, "keep_foreign": bool(body.get("keep_foreign", True)), "approve_alternates": bool(CFG.get("approve_alternates", False))}, timeout=180)
                    if r.get("event") != "result": return self._json(400, {"ok": False, "error": r.get("text")})
                    pf = cr.call("preflight", {"plan_hash": r["result"]["plan"]["hash"]})
                    return self._json(200, {"ok": True, "plan": r["result"]["plan"], "preflight": pf.get("result") if pf.get("event") == "result" else {"ok": False, "blocks": [{"code": "preflight-failed", "text": pf.get("text")}], "warnings": []}})
                h = re.sub(r"[^0-9a-f]", "", str(body.get("plan_hash") or ""))[:64]
                if op == "simulate":
                    if len(h) != 64 or not prof: return self._json(400, {"ok": False, "error": "Choose a simulated console and review the plan again."})
                    rid = cr.start("simulate", {"plan_hash": h, "profile": prof}); return self._json(200, {"ok": True, "run": rid, "simulated": True})
                if op == "verify":
                    r = cr.call("verify", {"plan_hash": h, "profile": prof or "no-device"}); return self._json(200 if r.get("event") == "result" else 400, {"ok": r.get("event") == "result", "verify": r.get("result"), "error": r.get("text")})
                if op == "execute":
                    r = cr.call("execute", {"plan_hash": h, "transport": str(body.get("transport") or "ssh")})
                    res = r.get("result") if r.get("event") == "result" else {"code": r.get("code"), "state": r.get("text")}
                    return self._json(403, {"ok": False, "error": (res or {}).get("state") or "Installing on a real console is not available in this version.", "code": (res or {}).get("code") or "physical-disabled", "physical": False})
                if op == "cancel": return self._json(200, {"ok": True, **cr.cancel(re.sub(r"[^A-Za-z0-9-]", "", str(body.get("run") or ""))[:40])})
                if op == "recover": r = cr.call("recover", {"stage": re.sub(r"[^a-z-]", "", str(body.get("stage") or ""))[:20]}); return self._json(200, {"ok": True, **(r.get("result") or {})})
                return self._json(404, {"ok": False, "error": "unknown console operation"})
            if path == "/api/home/native-package":   # prepare the native HOME package for the console (staged on this PC; never sent anywhere)
                man = body.get("manifest") if isinstance(body, dict) else None; home = body.get("home") if isinstance(body, dict) else None
                if not isinstance(man, dict) or not isinstance(home, dict): return self._json(400, {"ok": False, "error": "The collection or its HOME settings were not sent. Reload the page and try again."})
                if len(json.dumps(body)) > 16_000_000: return self._json(413, {"ok": False, "error": "The collection and its HOME settings are too large to prepare (16 MB at most)."})
                r = native_package_start(man, home); return self._json(200 if r["ok"] else 409, r)
            if path == "/api/home/folder-art":   # custom folder artwork: native picker (or a path in rehearsal configurations), validation, derivatives; or removal
                import folder_art as fa
                fid = str(body.get("folder") or ""); op = str(body.get("op") or "pick")
                try:
                    if op == "clear": return self._json(200, {"ok": True, "art": fa.clear(fid)})
                    if op == "describe": return self._json(200, {"ok": True, "art": fa.describe(fid)})
                    if op in ("pick", "set"):
                        if body.get("path"):
                            if not CFG.get("rehearsal_tree"): return self._json(403, {"ok": False, "error": "setting artwork from a path is available only in rehearsal configurations"})
                            chosen = str(body.get("path"))
                        else:
                            chosen = ps.pick_file("Choose artwork for this folder (PNG, JPEG, WebP, BMP or GIF)", [("Images", "*.png *.jpg *.jpeg *.webp *.bmp *.gif"), ("All files", "*.*")])
                            if not chosen: return self._json(200, {"ok": False, "canceled": True})
                        return self._json(200, {"ok": True, "art": fa.set_art(fid, chosen)})
                    return self._json(400, {"ok": False, "error": "unknown artwork operation"})
                except ValueError as e: return self._json(400, {"ok": False, "error": str(e)})
                except Exception as e: return self._json(500, {"ok": False, "error": f"The image could not be used ({e})."})
            if path == "/api/home/state":   # desktop application only (checked above with the other desktop routes)
                st = body.get("home") if isinstance(body, dict) else None
                if not isinstance(st, dict): return self._json(400, {"ok": False, "error": "The HOME settings could not be read. Reload the page and try again."})
                if len(json.dumps(st)) > 8_000_000: return self._json(413, {"ok": False, "error": "The HOME settings are too large to save (8 MB at most)."})
                try: saved = home_state_save(st)
                except Exception as e: return self._json(500, {"ok": False, "error": f"Could not save the HOME settings on this computer ({e})."})
                return self._json(200, {"ok": True, "saved_at": saved.get("saved_at")})
            if path == "/api/home/sync":   # one sync round against the MOCK console store; never a device (Gate G1 not approved)
                import home_sync as hs
                man = body.get("manifest") if isinstance(body, dict) else None; st = body.get("state") if isinstance(body, dict) else None
                if not isinstance(man, dict) or not isinstance(st, dict): return self._json(400, {"ok": False, "error": "The collection or its HOME settings were not sent. Reload the page and try again."})
                if len(json.dumps(body)) > 16_000_000: return self._json(413, {"ok": False, "error": "The collection and its HOME settings are too large to sync (16 MB at most)."})
                try: res = hs.sync(hs.MockConsoleStore(data_path("mock-console.json")), man, st)
                except Exception as e: return self._json(500, {"ok": False, "error": f"Could not sync with the simulated console ({e})."})
                if res.get("ok"):
                    try: cur = home_state_load(); cur["favorites"] = res["state"]["favorites"]; cur["history"] = res["state"]["history"]; home_state_save(cur)
                    except Exception: pass
                return self._json(200 if res.get("ok") else 422, res)
            if path == "/api/desktop/state":   # body was parsed above (bounded by _body)
                st = body.get("state") if isinstance(body, dict) else None
                if isinstance(st, dict) and len(json.dumps(st)) > 4_000_000: return self._json(413, {"ok": False, "error": "The app settings are too large to save (4 MB at most)."})
                if not isinstance(st, dict): return self._json(400, {"ok": False, "error": "The app settings could not be read. Reload the page and try again."})
                try: saved = desktop_state_save(st)
                except Exception as e: return self._json(500, {"ok": False, "error": f"Could not save the app settings on this computer ({e})."})
                return self._json(200, {"ok": True, "saved_at": saved.get("saved_at")})
            oid = str(body.get("id") or "")
            with LOCK: item = HANDOFF.get(oid)
            if not item: return self._json(404, {"ok": False, "error": "That collection handoff was not found; it may have expired. Hand the collection over again from the website."})
            if path == "/api/handoff/discard":
                with LOCK: HANDOFF.pop(oid, None); handoff_save()
                return self._json(200, {"ok": True})
            with LOCK: item["taken_at"] = item.get("taken_at") or time.strftime("%Y-%m-%dT%H:%M:%S"); handoff_save()
            return self._json(200, {"ok": True, "offer": {k: item[k] for k in ("id", "name", "count", "schema", "received", "fingerprint", "taken_at", "collection")}})
        if path.startswith("/api/import/") or path == "/api/library/locate":
            try:
                if path == "/api/import/pick":
                    mode = str(body.get("mode") or "files")
                    if mode == "folder": f = ps.pick_folder("Add games from a folder (ROM files and ZIP archives inside it)"); items = [f] if f else []
                    else: items = ps.pick_files("Add games from files (ROM files or ZIP archives)", [("NES ROMs and archives", "*.nes *.unh *.unif *.bin *.rom *.fds *.zip"), ("All files", "*.*")])
                    if not items: return self._json(200, {"ok": False, "canceled": True})
                    return self._json(200, {"ok": True, "job": ps.start_import(items), "items": len(items)})
                if path == "/api/import/start":
                    if not CFG.get("rehearsal_tree"): return self._json(403, {"ok": False, "error": "path import without a dialog is available only in rehearsal configurations"})
                    items = [str(x) for x in (body.get("paths") or []) if isinstance(x, str)]
                    return self._json(200, {"ok": True, "job": ps.start_import(items), "items": len(items)})
                if path == "/api/import/cancel": return self._json(200, {"ok": ps.cancel_job(str(body.get("job") or ""))})
                if path == "/api/library/locate":
                    vid = re.sub(r"[^0-9a-f]", "", str(body.get("version_id") or ""))[:16]
                    if not ps.version_known(vid): return self._json(409, {"ok": False, "error": "This game version is not in the Game Library."})
                    f = str(body.get("path") or "") if CFG.get("rehearsal_tree") and body.get("path") else (ps.pick_files("Locate the game file for this version", [("NES ROMs and archives", "*.nes *.unh *.unif *.bin *.rom *.fds *.zip"), ("All files", "*.*")]) or [""])[0]
                    if not f: return self._json(200, {"ok": False, "canceled": True})
                    return self._json(200, {"ok": True, **ps.locate_version(vid, f)})
            except ValueError as e: return self._json(409, {"ok": False, "error": str(e)})
            except Exception as e: return self._json(500, {"ok": False, "error": f"Could not complete this action ({e}). Try again."})
            return self._json(404, {"ok": False, "error": "unknown endpoint"})
        if path in ("/api/install-hakchi", "/api/install-driver"):
            # explicit owner click in the catalog; same live guards as staging (never while hakchi runs or the console is attached)
            if not CFG.get("rehearsal_tree"):
                if hb.hakchi_running(): return self._json(409, {"ok": False, "error": "Hakchi2 CE is open. Close it, then try again."})
                if hb.console_connected(): return self._json(409, {"ok": False, "error": "The console is connected over USB. Disconnect it, then try again."})
            try:
                fn = hb.install_hakchi if path == "/api/install-hakchi" else hb.install_driver
                res = fn(CFG, dry_run=bool(body.get("dry_run"))); diag_refresh(force=True); res["env"] = status_payload(wait=20)
                return self._json(200 if res.get("ok") else 409, {"ok": bool(res.get("ok")), "error": None if res.get("ok") else res.get("message"), "result": res})
            except Exception as e: return self._json(500, {"ok": False, "error": f"Could not complete this action ({e}). Try again."})
        if path.startswith("/api/personal/") or path == "/api/library/adopt":
            try:
                root = os.path.dirname(CFG["hakchi_exe"]); eid = str(body.get("id") or "")
                if path == "/api/personal/pick":
                    f = ps.pick_file("Add Your Own Game — choose a ROM file or ZIP", [("NES ROMs and archives", "*.nes *.unh *.bin *.fds *.zip"), ("All files", "*.*")])
                    if not f: return self._json(200, {"ok": False, "canceled": True})
                    return self._json(200, {"ok": True, **ps.ingest(f)})
                if path == "/api/personal/ingest-path":
                    if not CFG.get("rehearsal_tree"): return self._json(403, {"ok": False, "error": "path ingestion is available only in rehearsal configurations"})
                    return self._json(200, {"ok": True, **ps.ingest(str(body.get("path") or ""), origin=str(body.get("origin") or "file"), local_code=str(body.get("local_code") or ""), local_name=str(body.get("local_name") or ""))})
                if path == "/api/personal/relink":
                    f = str(body.get("path") or "") if CFG.get("rehearsal_tree") and body.get("path") else ps.pick_file("Locate the game file", [("NES ROMs and archives", "*.nes *.unh *.bin *.fds *.zip"), ("All files", "*.*")])
                    if not f: return self._json(200, {"ok": False, "canceled": True})
                    return self._json(200, {"ok": True, **ps.relink(eid, f)})
                if path == "/api/personal/update": return self._json(200, {"ok": True, **ps.update_user(eid, body.get("user") or {})})
                if path == "/api/personal/art":
                    f = str(body.get("path") or "") if CFG.get("rehearsal_tree") and body.get("path") else ps.pick_file("Choose artwork", [("Images", "*.png *.jpg *.jpeg *.webp"), ("All files", "*.*")])
                    if not f: return self._json(200, {"ok": False, "canceled": True})
                    return self._json(200, {"ok": True, **ps.set_art(eid, f)})
                if path == "/api/personal/remove": return self._json(200, {"ok": True, **ps.remove(eid, bool(body.get("delete_copy")))})
                if path == "/api/library/adopt": lib = ps.read_local_library(root); return self._json(200, {"ok": True, "adopted": ps.adopt_unmatched(root, lib), "library": lib})
                if path == "/api/personal/rehearsal-mutate-config":   # test hook (rehearsal configurations only): simulate an external change to the DISPOSABLE tree's selection
                    if not CFG.get("rehearsal_tree"): return self._json(403, {"ok": False, "error": "rehearsal configurations only"})
                    cp = os.path.join(root, "config", "config.json"); c = json.load(open(cp, encoding="utf-8")); n = int(body.get("drop") or 1)
                    c["gamesCollectionSettings"]["NES"]["SelectedGames"] = c["gamesCollectionSettings"]["NES"]["SelectedGames"][:-n]; json.dump(c, open(cp, "w", encoding="utf-8"), indent=2)
                    return self._json(200, {"ok": True, "dropped": n})
                return self._json(404, {"ok": False, "error": "unknown endpoint"})
            except ValueError as e: return self._json(409, {"ok": False, "error": str(e)})
            except Exception as e: return self._json(500, {"ok": False, "error": f"Could not complete this action ({e}). Try again."})
        man = body.get("manifest")
        if not isinstance(man, dict): return self._json(400, {"ok": False, "error": "No collection was sent. Reload the page and try again."})
        # divergence check: the collection's import baseline (the console-management sets when it was loaded) vs the sets now
        conflict = None
        base = man.get("baseline") if isinstance(man.get("baseline"), dict) else None
        if base:
            try:
                cur = hi.effective_collection(os.path.dirname(CFG["hakchi_exe"]))
                d_o = sorted(set(cur["originals_enabled"]) ^ set(base.get("originals_enabled") or [])); d_c = sorted(set(cur["customs_enabled"]) ^ set(base.get("customs_enabled") or []))
                if d_o or d_c: conflict = {"changed_factory": d_o, "changed_custom": d_c, "names": {c: cur["names"].get(c, c) for c in d_o + d_c}, "baseline_captured": base.get("captured")}
            except Exception as e: conflict = {"error": f"Could not compare with the games set up in Hakchi on this computer ({e})."}
        if path == "/api/preview":
            try:
                mf = save_manifest(man); pv = hb.plan(man, approve_alternates=bool(CFG.get("approve_alternates", False)))
                pv_out = {k: pv[k] for k in ("can_apply", "problems", "warnings", "summary", "games")}
                # reuse count for the preview
                try:
                    tree = hb.HakchiTree(os.path.dirname(CFG["hakchi_exe"])); ex = tree.custom_entries()
                    pv_out["summary"]["reused"] = sum(1 for it in pv["custom"] if any(e["marker"] and e["marker"].get("game_id") == it["game"]["game_id"] for e in ex.values()))
                    pv_out["summary"]["to_prepare"] = pv_out["summary"]["to_prepare_total"] - pv_out["summary"]["reused"]
                except Exception: pass
                env = status_payload(wait=20)
                return self._json(200, {"ok": True, "manifest_file": os.path.basename(mf), "preview": pv_out, "env": env, "conflict": conflict})
            except Exception as e: return self._json(500, {"ok": False, "error": f"Could not check the collection ({e}). Try again."})
        if path == "/api/apply":
            mf = body.get("manifest_file") or ""
            if not re.match(r"^[0-9]{8}-[0-9]{6}-selection(-\d+)?\.json$", mf): return self._json(400, {"ok": False, "error": "Check the collection again, then apply it."})
            full = os.path.join(CFG["exports_dir"], mf); rel = os.path.relpath(full, hb.PROJECT); mf = full if rel.startswith("..") or FROZEN else rel
            saved = json.load(open(full, encoding="utf-8")) if os.path.exists(full) else None
            if not saved or (saved.get("games") if saved.get("games") is not None else saved.get("selections")) != (man.get("games") if man.get("games") is not None else man.get("selections")): return self._json(409, {"ok": False, "error": "The collection changed after it was checked. Check it again, then apply it."})
            if conflict and not body.get("override_conflict"): return self._json(409, {"ok": False, "error": "The games set up in Hakchi on this computer changed since this collection was loaded. Review the differences first.", "conflict": conflict})
            res = hb.apply(man, mf, CFG, approve_alternates=bool(CFG.get("approve_alternates", False)), launch=bool(body.get("launch", True)))   # the Studio prepares without launching; "Verify" launches and reads the window
            if res.get("ok") and res.get("launched"):
                try: res["ui_verification"] = ui_verify(res)
                except Exception as e: res["ui_verification"] = f"unverified ({e})"
            return self._json(200 if res.get("ok") else 409, {"ok": bool(res.get("ok")), "error": res.get("error"), "result": res})
        return self._json(404, {"ok": False, "error": "unknown endpoint"})

def ui_verify(res: dict) -> str:
    """Read Hakchi's game list through UI Automation (read-only) and compare the checked set with the applied selection."""
    import subprocess
    script = os.path.join(HERE, "Read-HakchiSelection.ps1")
    out = subprocess.run([hb.pwsh(), "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-ProcessId", str(res.get("hakchi_pid", 0)), "-WaitSeconds", "180"], capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=300).stdout.strip()
    j = json.loads(out.splitlines()[-1]) if out else {}
    if not j.get("ok"): return f"unverified ({j.get('error','no window')})"
    ver = res["verification"]; want = set(ver["factory_enabled"]) | set(ver["custom_checked"]); n_want = len(want)
    listed = j.get("listed", []); got_names = j.get("checked", [])
    if j.get("rows"):
        # stable identifiers: window rows (name + group) -> codes through Hakchi's own desktop entries (hakchi_inspect), then set comparison
        import hakchi_inspect as hi
        m = hi.ui_rows_to_codes(j["rows"], res.get("tree_root") or os.path.dirname(CFG["hakchi_exe"])); got = set(m["checked_codes"])
        if m["ambiguous"] or m["unmapped"]: return f"unverified: {len(m['ambiguous'])} ambiguous and {len(m['unmapped'])} unmapped window rows ({len(got_names)} checked of {len(listed)} listed)"
        if got == want: return f"matches by code: {len(got)} checked of {len(listed)} listed ({len(ver['factory_enabled'])} factory + {len(ver['custom_checked'])} additional)"
        return f"MISMATCH by code: missing {sorted(want - got)} unexpected {sorted(got - want)} ({len(got)} checked of {len(listed)} listed)"
    if len(got_names) == n_want: return f"matches by count only: {len(got_names)} checked of {len(listed)} listed (expected {n_want})"
    return f"MISMATCH: expected {n_want} checked, window shows {len(got_names)} of {len(listed)} listed"

PORT = int(CFG.get("port", 8765))

# ---- diagnostics cache: the slow checks (process list, USB devices, driver store) run in ONE background thread, never on the request path.
#      /api/status answers at once with cheap facts + the last snapshot (with its age); a refresh is started when the snapshot is stale. ----
DIAG = {"snapshot": None, "at": 0.0, "running": False, "started": 0.0, "error": None, "timings": {}}
DIAG_TTL = 20.0
def diag_refresh(force: bool = False):
    now = time.time()
    with LOCK:
        if DIAG["running"] and now - DIAG["started"] < 120: return
        if not force and DIAG["snapshot"] and now - DIAG["at"] < DIAG_TTL: return
        DIAG["running"] = True; DIAG["started"] = now
    def run():
        try:
            full = hb.env_status(CFG); snap = {"hakchi_running": full["hakchi_running"], "console_connected": full["console_connected"], "driver": full["driver"]}
            with LOCK: DIAG["snapshot"] = snap; DIAG["at"] = time.time(); DIAG["timings"] = full.get("timings", {}); DIAG["error"] = None
        except Exception as e:
            with LOCK: DIAG["error"] = str(e)[:200]; DIAG["at"] = time.time()
        finally:
            with LOCK: DIAG["running"] = False
    threading.Thread(target=run, daemon=True).start()
def status_payload(wait: float = 0.0) -> dict:
    """Cheap status now; the diagnostics snapshot when one exists (or after waiting up to `wait` seconds for a fresh one)."""
    st = hb.env_status(CFG, quick=True); diag_refresh()
    if wait > 0:
        t0 = time.time()
        while time.time() - t0 < wait:
            with LOCK: fresh = DIAG["snapshot"] is not None and not DIAG["running"] and time.time() - DIAG["at"] < DIAG_TTL + 1
            if fresh: break
            time.sleep(0.2)
    with LOCK: snap = dict(DIAG["snapshot"] or {}); age = round(time.time() - DIAG["at"], 1) if DIAG["snapshot"] else None; running = DIAG["running"]; err = DIAG["error"]; tm = dict(DIAG["timings"])
    st.update({"hakchi_running": snap.get("hakchi_running"), "console_connected": snap.get("console_connected"), "driver": snap.get("driver"), "diag": {"state": "checking" if (running or not snap) else "ok", "age_s": age, "running": running, "error": err, "timings": tm}, "rehearsal": bool(CFG.get("rehearsal_tree"))})
    return st

class QuietServer(socketserver.ThreadingTCPServer):
    daemon_threads = True; block_on_close = False   # kept-alive connections must not hold the process open at shutdown
    request_queue_size = 64   # a page load opens many loopback connections at once (preflights, status, store, state, inbox); the default backlog of 5 made Chromium retry SYNs with a 30 s back-off
    """A client that drops its connection (tab closed, browser timeout) is not an error worth a traceback in the log."""
    def handle_error(self, request, client_address):
        import sys as _s
        e = _s.exc_info()[1]
        if isinstance(e, (ConnectionError, TimeoutError)): return
        super().handle_error(request, client_address)

def make_server(port: int):
    """Bind the loopback server (raises OSError when the port is taken) and freeze the origin allow list for this port."""
    global PORT, ALLOWED
    PORT = port; ALLOWED = allowed_origins(port)
    socketserver.TCPServer.allow_reuse_address = True
    srv = QuietServer(("127.0.0.1", port), H); SERVER_REF["srv"] = srv; handoff_load(); host_watchdog(); return srv

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--port", type=int, default=PORT); ap.add_argument("--no-browser", action="store_true"); a = ap.parse_args()
    with make_server(a.port) as srv:
        url = f"http://127.0.0.1:{a.port}/nes-catalog.html"
        print(f"Classic Cart Studio backend {VERSION} (protocol {PROTOCOL}) listening on {url}  (loopback only; Ctrl+C to stop); trusted websites: {', '.join(sorted(ALLOWED))}")
        st = hb.env_status(CFG); print(f"hakchi: {st['hakchi_path']} -> {'OK' if st['hakchi_found'] else st['hakchi_check']}; install={st['install']['state']}; console driver={st['driver']['state']} ({st['driver'].get('inf')}); running={st['hakchi_running']}; console connected={st['console_connected']}")
        if not a.no_browser: threading.Timer(0.8, lambda: webbrowser.open(url)).start()
        try: srv.serve_forever()
        except KeyboardInterrupt: pass

if __name__ == "__main__":
    main()
