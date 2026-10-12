"""Classic Cart Studio backend — the Windows entry point of the Python backend.

A small status window plus the loopback server from nes_companion.py (same code as the development companion). It:
  * creates the per-user data folder (%LOCALAPPDATA%\\ClassicCartStudio) and a default companion.json on first run,
  * starts the server on 127.0.0.1:8765 (refuses to start when another program holds the port; if the other program IS this
    companion, the new instance just hands over and exits — single instance),
  * shows status, the connected websites, and buttons to open the Studio on this computer or the public website,
  * handles the nesstudio:// link (registered per user by the installer) by opening the website or the pairing page,
  * writes a short log to <data>\\logs\\companion.log (no tokens, no paired-token values).
Closing the window stops the companion; nothing keeps running in the background. It never touches the console.
"""
from __future__ import annotations
import os, sys, json, threading, time, webbrowser, urllib.request, urllib.parse, logging, logging.handlers
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE); sys.path.insert(0, os.path.join(HERE, "..", "rom-pipeline"))
from common import DATA_ROOT, SITE_ROOT, FROZEN
import nes_companion as nc
from version import VERSION, PROTOCOL, APP_ID, PUBLIC_ORIGIN, DEFAULT_PORT, SITE_URL

APP_NAME = "Classic Cart Studio backend"
SITE_URL = SITE_URL
LOG = logging.getLogger("companion")

def setup_logging():
    d = os.path.join(DATA_ROOT, "logs"); os.makedirs(d, exist_ok=True)
    h = logging.handlers.RotatingFileHandler(os.path.join(d, "companion.log"), maxBytes=512_000, backupCount=2, encoding="utf-8")
    h.setFormatter(logging.Formatter("%(asctime)s %(levelname)s %(message)s")); LOG.addHandler(h); LOG.setLevel(logging.INFO)
    if FROZEN:   # a windowed executable has no console: server tracebacks and request lines go to a second small log instead of nowhere
        try: sys.stderr = sys.stdout = open(os.path.join(d, "server.log"), "a", encoding="utf-8", buffering=1)
        except Exception: pass

def other_instance(port: int) -> dict | None:
    """If something answers on the port, say whether it is this companion (app id) or a stranger."""
    try:
        with urllib.request.urlopen(f"http://127.0.0.1:{port}/api/version", timeout=2) as r: j = json.load(r)
        return {"ours": j.get("app") == APP_ID, "version": j.get("version")}
    except urllib.error.HTTPError as e: return {"ours": False, "version": None, "http": e.code}
    except Exception: return None

def handle_protocol_arg(argv: list[str]) -> str | None:
    """nesstudio://open  -> website; nesstudio://pair?origin=... -> pairing page on this companion (origin is re-validated by the server)."""
    for a in argv:
        if a.lower().startswith("nesstudio:"):
            u = urllib.parse.urlparse(a); q = dict(urllib.parse.parse_qsl(u.query))
            if u.netloc == "pair" and q.get("origin"): return f"http://127.0.0.1:{DEFAULT_PORT}/pair?origin=" + urllib.parse.quote(q["origin"], safe="")
            return SITE_URL
    return None

class App:
    def __init__(self, port: int):
        import tkinter as tk
        from tkinter import ttk
        self.tk = tk; self.port = port; self.root = tk.Tk(); self.root.title(APP_NAME); self.root.minsize(520, 300); self.root.configure(bg="#15161a")
        try: self.root.iconbitmap(os.path.join(SITE_ROOT, "brand", "public", "app.ico") if FROZEN else os.path.join(HERE, "..", "publish", "assets", "app.ico"))
        except Exception: pass
        st = ttk.Style(); st.theme_use("clam"); st.configure("TFrame", background="#15161a"); st.configure("TLabel", background="#15161a", foreground="#f2f2f2", font=("Segoe UI", 10))
        st.configure("H.TLabel", font=("Segoe UI Semibold", 14)); st.configure("M.TLabel", foreground="#9a9ba3", font=("Segoe UI", 9)); st.configure("TButton", font=("Segoe UI", 10), padding=6)
        f = ttk.Frame(self.root, padding=18); f.pack(fill="both", expand=True)
        ttk.Label(f, text=APP_NAME, style="H.TLabel").pack(anchor="w"); ttk.Label(f, text=f"version {VERSION} · protocol {PROTOCOL}", style="M.TLabel").pack(anchor="w", pady=(0, 10))
        self.status = ttk.Label(f, text="Starting…"); self.status.pack(anchor="w"); self.sites = ttk.Label(f, text="", style="M.TLabel", wraplength=480, justify="left"); self.sites.pack(anchor="w", pady=(2, 12))
        b = ttk.Frame(f); b.pack(anchor="w", fill="x")
        ttk.Button(b, text="Open the Studio website", command=lambda: webbrowser.open(SITE_URL)).grid(row=0, column=0, padx=(0, 8), pady=4, sticky="w")
        ttk.Button(b, text="Open the Studio on this computer", command=lambda: webbrowser.open(f"http://127.0.0.1:{self.port}/nes-catalog.html")).grid(row=0, column=1, padx=(0, 8), pady=4, sticky="w")
        ttk.Button(b, text="Disconnect all websites", command=self.revoke).grid(row=1, column=0, padx=(0, 8), pady=4, sticky="w")
        ttk.Button(b, text="Open the data folder", command=lambda: os.startfile(DATA_ROOT)).grid(row=1, column=1, padx=(0, 8), pady=4, sticky="w")
        ttk.Label(f, text="The companion only listens on this computer (127.0.0.1). A website can use it only after you click Allow in the window the companion opens. Closing this window stops the companion. Nothing here reaches the console.", style="M.TLabel", wraplength=480, justify="left").pack(anchor="w", pady=(14, 0))
        self.root.protocol("WM_DELETE_WINDOW", self.quit); self.srv = None; self.thread = None
    def start_server(self):
        try: self.srv = nc.make_server(self.port)
        except OSError as e:
            self.status.configure(text=f"Could not start: port {self.port} is in use by another program ({e.strerror or e}). Close it and start the companion again.", foreground="#ff8e8e"); LOG.error("port busy: %s", e); return
        self.thread = threading.Thread(target=self.srv.serve_forever, daemon=True); self.thread.start()
        LOG.info("listening on 127.0.0.1:%s version %s protocol %s data=%s site=%s", self.port, VERSION, PROTOCOL, DATA_ROOT, SITE_ROOT); self.refresh()
    def refresh(self):
        if self.srv:
            now = time.time()
            with nc.LOCK: sites = sorted({v["origin"] for v in nc.PAIRED.values() if v["expires"] > now})
            self.status.configure(text=f"Running on 127.0.0.1:{self.port}", foreground="#39d37a"); self.sites.configure(text=("Connected websites: " + ", ".join(sites)) if sites else "No website is connected yet. Open the Studio website and click Connect to Your Computer.")
        self.root.after(2000, self.refresh)
    def revoke(self): nc.revoke_all(); LOG.info("all website connections revoked by the user"); self.refresh()
    def quit(self):
        try:
            if self.srv: self.srv.shutdown(); self.srv.server_close()
        except Exception: pass
        LOG.info("stopped"); self.root.destroy()
    def run(self): self.root.after(50, self.start_server); self.root.mainloop()

def run_headless(port: int) -> int:
    """Classic Cart Studio for Windows starts the backend this way: no window, the host supervises the process (CCS_HOST_PID) and
    stops it through /api/shutdown; the host's per-run token (CCS_HOST_TOKEN) is the desktop principal."""
    if not FROZEN:   # under the host there is no console either: request lines and tracebacks go to logs\server.log
        try: sys.stderr = sys.stdout = open(os.path.join(DATA_ROOT, "logs", "server.log"), "a", encoding="utf-8", buffering=1)
        except Exception: pass
    try: srv = nc.make_server(port)
    except OSError as e: LOG.error("headless: port %s busy: %s", port, e); return 2
    LOG.info("headless backend listening on 127.0.0.1:%s version %s protocol %s data=%s site=%s host_pid=%s", port, VERSION, PROTOCOL, DATA_ROOT, SITE_ROOT, nc.HOST_PID or "-")
    try: srv.serve_forever()
    except KeyboardInterrupt: pass
    finally:
        try: srv.server_close()
        except Exception: pass
        LOG.info("headless backend stopped")
    return 0

def main():
    os.makedirs(DATA_ROOT, exist_ok=True); setup_logging()
    port = int(nc.CFG.get("port", DEFAULT_PORT)); link = handle_protocol_arg(sys.argv[1:])
    if "--console-worker" in sys.argv[1:]:   # the console engine worker (simulation tier): line protocol on stdin/stdout, no window, no network
        import io
        # The protocol runs on the pipes the parent provides, whatever the process's own std streams are: a windowed frozen
        # build has none, and setup_logging() above points sys.stdout/sys.stderr at server.log (which keeps receiving errors).
        try:
            sys.stdin = io.TextIOWrapper(os.fdopen(0, "rb", closefd=False), encoding="utf-8")
            sys.stdout = io.TextIOWrapper(os.fdopen(1, "wb", closefd=False), encoding="utf-8", line_buffering=True)
        except OSError as e:
            LOG.error("console worker: no stdio pipes (%s)", e); return
        from console_engine.worker import main as worker_main
        root = sys.argv[sys.argv.index("--root") + 1] if "--root" in sys.argv[1:] else os.path.join(DATA_ROOT, "console")
        worker_main(["--root", root]); return
    if "--headless" in sys.argv[1:]:
        if "--port" in sys.argv[1:]:
            try: port = int(sys.argv[sys.argv.index("--port") + 1])
            except Exception: pass
        sys.exit(run_headless(port))
    other = other_instance(port)
    if other and other["ours"]:   # already running: hand over the link (or just show the website) and exit
        LOG.info("another instance is running (version %s); handing over", other["version"]); webbrowser.open(link or SITE_URL); return
    if link: threading.Timer(1.5, lambda: webbrowser.open(link)).start()
    App(port).run()

if __name__ == "__main__":
    main()
