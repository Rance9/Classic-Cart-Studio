r"""Real TV Preview (N3): the Windows application's Preview on TV drawn by the console's own native HOME renderer.

The page sends the current collection and HOME model; this module builds a TV Preview package with the same just-in-time
packager the console installation uses (native_home.package, mode "preview": same resolution, same effective customized
content, same derivation cache, so only changed content is re-derived), starts the native core (the Windows build of
tools/console-simulator/core, the same C code the console runs), and relays the controller: presses, releases and the theme
buttons go to the core; frames come back as JPEG. Nothing is installed or sent anywhere; the preview's theme changes stay in a
throwaway state folder, so they never touch the console's saved choice.

  POST /api/preview/start  {manifest, home}  -> {ok, theme, games, seconds, fingerprint, staged: current|outdated|none}
  GET  /api/preview/frame                    -> image/jpeg (X-Preview-Seq, X-Preview-Focus headers)
  POST /api/preview/input  {button, action}  -> {ok, state}
  POST /api/preview/theme  {theme}           -> {ok, state}
  GET  /api/preview/state                    -> {ok, running, state, theme, fingerprint, staged}
  POST /api/preview/stop
"""
from __future__ import annotations
import io, json, os, queue, shutil, subprocess, sys, tempfile, threading, time
HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path: sys.path.insert(0, HERE)
from common import FROZEN, APP_ROOT, WORK, data_path   # noqa: E402

BUTTONS = {"up", "down", "left", "right", "a", "b", "start", "select"}
THEMES = ("arcade-grid", "box-art-showcase", "featured-shelf", "adventure-map")

def core_binary() -> str | None:
    """the native core for Windows: bundled with the application, or the development build"""
    for p in (os.environ.get("CCS_NATIVE_PREVIEW_CORE"), os.path.join(getattr(sys, "_MEIPASS", APP_ROOT), "native", "ccs-sim-core.exe") if FROZEN else None,
              os.path.join(os.environ.get("CCS_SIM_BUILD_WIN") or os.path.join(WORK, "console-simulator", "build"), "windows-x86_64", "ccs-sim-core.exe")):
        if p and os.path.isfile(p): return p
    return None

class Session:
    def __init__(self):
        self.lock = threading.RLock(); self.proc = None; self.q: queue.Queue = queue.Queue(); self.state: dict = {}; self.seq = 0; self.frame: bytes | None = None
        self.t_last = None; self.pkg = None; self.state_dir = None; self.fingerprint = None; self.theme = None; self.tmp = tempfile.mkdtemp(prefix="ccs-preview-")

    # ---- the core process (line protocol: commands on stdin, JSON events on stdout) ----
    def _reader(self, proc):
        for raw in proc.stdout:
            line = raw.decode("utf-8", "replace").strip()
            if not line.startswith("{"): continue
            try: ev = json.loads(line)
            except json.JSONDecodeError: continue
            if ev.get("ev") == "home": self.state = ev
            self.q.put(ev)
        self.q.put({"ev": "__eof__"})
    def _send(self, line: str):
        self.proc.stdin.write((line + "\n").encode("utf-8")); self.proc.stdin.flush()
    def _wait(self, names, timeout=60.0) -> dict | None:
        end = time.time() + timeout; names = (names,) if isinstance(names, str) else names
        while time.time() < end:
            try: ev = self.q.get(timeout=max(0.01, end - time.time()))
            except queue.Empty: return None
            if ev.get("ev") in names: return ev
            if ev.get("ev") == "__eof__": return None
        return None
    def _stop_proc(self):
        if self.proc and self.proc.poll() is None:
            try: self._send("shutdown"); self.proc.wait(5)
            except Exception: self.proc.kill()
        self.proc = None

    def start(self, manifest: dict, home: dict) -> dict:
        from native_home import package as P
        exe = core_binary()
        if not exe: return {"ok": False, "error": "the native HOME renderer for Windows is not available in this installation"}
        t0 = time.time(); res = P.build(manifest, home, mode="preview")
        if not res.get("ok"): return {"ok": False, "status": res.get("status"), "error": res.get("error") or "; ".join(res.get("problems") or [])}
        with self.lock:
            self._stop_proc(); focus = self.state.get("id")
            self.pkg = res["package_dir"]; self.fingerprint = (res.get("manifest") or {}).get("configuration", {}).get("content_fingerprint")
            self.state_dir = os.path.join(self.tmp, "state"); shutil.rmtree(self.state_dir, ignore_errors=True)   # the preview never keeps theme choices
            self.q = queue.Queue(); self.state = {}
            flags = 0x08000000 if os.name == "nt" else 0   # CREATE_NO_WINDOW
            self.proc = subprocess.Popen([exe, f"--state-dir={self.state_dir}"], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, bufsize=0, creationflags=flags)
            threading.Thread(target=self._reader, args=(self.proc,), daemon=True).start()
            if not self._wait("ready", 30): self._stop_proc(); return {"ok": False, "error": "the native renderer did not start"}
            self._send(f"home {self.pkg}"); ev = self._wait(("home_loaded", "home_rejected"), 120)
            if not ev or ev.get("ev") != "home_loaded": self._stop_proc(); return {"ok": False, "error": (ev or {}).get("error") or "the preview package could not be loaded"}
            self.theme = ev.get("theme"); self._wait("home", 10)
            if focus:   # keep the focused game across a rebuild (by identity)
                ids = [g["id"] for g in json.load(open(os.path.join(self.pkg, "games.json"), encoding="utf-8"))["games"]]
                if focus in ids: self._send(f"home-select {ids.index(focus)}"); self._wait("home", 10)
            self.t_last = time.time(); self.frame = None
        return {"ok": True, "theme": self.theme, "games": res.get("games"), "seconds": round(time.time() - t0, 2), "fingerprint": self.fingerprint, "staged": staged_status(self.fingerprint), "cache": res.get("cache")}

    def render(self) -> bytes | None:
        """advance the core's clock by the real time since the last frame (held buttons repeat, titles scroll), then draw"""
        from PIL import Image
        with self.lock:
            if not self.proc or self.proc.poll() is not None: return None
            now = time.time(); dt = int(min(1000, max(0, (now - (self.t_last or now)) * 1000))); self.t_last = now
            if dt: self._send(f"tick {dt}"); self._wait("tick", 5)
            ppm = os.path.join(self.tmp, "frame.ppm"); self._send(f"frame {ppm}"); ev = self._wait(("frame_written", "frame_failed"), 10)
            if not ev or ev.get("ev") != "frame_written": return self.frame
            with Image.open(ppm) as im: buf = io.BytesIO(); im.save(buf, "JPEG", quality=90); self.frame = buf.getvalue()
            self.seq += 1; return self.frame
    def input(self, button: str, action: str) -> dict:
        if button not in BUTTONS or action not in ("press", "release"): return {"ok": False, "error": "unknown button or action"}
        with self.lock:
            if not self.proc: return {"ok": False, "error": "the preview is not running"}
            now = time.time(); dt = int(min(1000, max(0, (now - (self.t_last or now)) * 1000))); self.t_last = now
            if dt: self._send(f"tick {dt}"); self._wait("tick", 5)
            self._send(f"input {action} {button}"); self._send("home-state"); self._wait("home", 5)
            return {"ok": True, "state": self.state}
    def set_theme(self, theme: str) -> dict:
        if theme not in THEMES: return {"ok": False, "error": "unknown theme"}
        with self.lock:
            if not self.proc: return {"ok": False, "error": "the preview is not running"}
            self._send(f"home-theme {theme}"); self._wait("home", 10); self.theme = theme
            return {"ok": True, "state": self.state, "theme": theme}
    def info(self) -> dict:
        with self.lock:
            running = bool(self.proc and self.proc.poll() is None)
            return {"ok": True, "running": running, "state": self.state, "theme": self.state.get("theme") or self.theme, "fingerprint": self.fingerprint, "staged": staged_status(self.fingerprint), "package": self.pkg}   # package: the preview package folder (desktop-only route; tests read what the preview shows)
    def stop(self):
        with self.lock: self._stop_proc()

def staged_status(fp: str | None) -> str:
    """the prepared installation package against what the preview shows: current, outdated or none"""
    try:
        from native_home import package as P
        m = P.inspect()
    except Exception: m = None
    if not m: return "none"
    return "current" if fp and (m.get("configuration") or {}).get("content_fingerprint") == fp else "outdated"

SESSION: Session | None = None
def session() -> Session:
    global SESSION
    if SESSION is None: SESSION = Session()
    return SESSION

# ---- HTTP routes (desktop principal only; dispatched from nes_companion.py) ----
def _desktop(h) -> bool:
    b = h._bearer(); return bool(b and b.get("desktop"))
def handle_get(h, path: str, qs: str):
    if not _desktop(h): return h._json(403, {"ok": False, "error": "desktop application only"})
    s = session()
    if path == "/api/preview/state": return h._json(200, s.info())
    if path == "/api/preview/frame":
        data = s.render()
        if not data: return h._json(409, {"ok": False, "error": "the preview is not running"})
        h.send_response(200); h.send_header("Content-Type", "image/jpeg"); h.send_header("Content-Length", str(len(data))); h.send_header("Cache-Control", "no-store")
        h.send_header("X-Preview-Seq", str(s.seq)); h.send_header("X-Preview-Focus", str((s.state or {}).get("id") or "")); h.send_header("X-Preview-Theme", str((s.state or {}).get("theme") or ""))
        h.send_header("Access-Control-Expose-Headers", "X-Preview-Seq, X-Preview-Focus, X-Preview-Theme"); h._cors(); h.end_headers(); h.wfile.write(data); return
    return h._json(404, {"ok": False, "error": "unknown preview endpoint"})
def handle_post(h, path: str, body: dict):
    if not _desktop(h): return h._json(403, {"ok": False, "error": "desktop application only"})
    s = session(); body = body if isinstance(body, dict) else {}
    if path == "/api/preview/start":
        man, home = body.get("manifest"), body.get("home")
        if not isinstance(man, dict) or not isinstance(home, dict): return h._json(400, {"ok": False, "error": "the collection and the HOME configuration are required"})
        try: r = s.start(man, home)
        except Exception as e: r = {"ok": False, "error": f"the preview could not start: {e}"}
        return h._json(200 if r.get("ok") else 409, r)
    if path == "/api/preview/input": r = s.input(str(body.get("button") or ""), str(body.get("action") or "")); return h._json(200 if r.get("ok") else 400, r)
    if path == "/api/preview/theme": r = s.set_theme(str(body.get("theme") or "")); return h._json(200 if r.get("ok") else 400, r)
    if path == "/api/preview/stop": s.stop(); return h._json(200, {"ok": True})
    return h._json(404, {"ok": False, "error": "unknown preview endpoint"})
