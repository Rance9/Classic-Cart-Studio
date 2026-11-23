r"""One Classic Cart Studio window per data folder (tools/desktop/ClassicCartStudio/Program.cs): the built launcher started for real, with its
backend beside it, in new data folders (CCS_STUDIO_DATA set for the started programs only) on loopback ports chosen here; no console, no
network, no Hakchi2 CE. A Classic Cart Studio already open on this computer with its own data folder is never touched: only the programs this
test starts are closed.
  same folder      a second start for the same data folder hands over to the open window (it logs the handover, the second start ends at
                   once) and no second window or backend appears
  other folder     a start for another data folder never reaches the first window (no handover there, a lock and pipe of its own); when
                   the first window's backend holds the port it says so ("Another copy of Classic Cart Studio") instead of using it, its
                   window never shows a page from that port, and the first data folder is unchanged; on a port of its own it runs with
                   its own backend in its own data folder
  not its backend  a server on the window's port that merely answers like Classic Cart Studio: the window stays on its recovery screen;
                   neither the menu's Reload nor the recovery screen's Try Again loads a page from that server; once the port is free,
                   Reload starts the window's own backend and its page loads as usual
  default folder   the default data folder's personal, saves and play folders are the same before and after (read, never written)
  environment      CCS_STUDIO_DATA is not set in this process or in the user's saved environment, before or after
  python test_studio_instance.py --launcher <ClassicCartStudio.exe>        Exit 0 = PASS."""
from __future__ import annotations
import argparse, ctypes, ctypes.wintypes as wt, hashlib, json, os, shutil, socket, subprocess, sys, tempfile, threading, time, urllib.request, winreg
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
ap = argparse.ArgumentParser(); ap.add_argument("--launcher", required=True); A = ap.parse_args()
checks = []
def ok(k, v, n=""): checks.append((k, bool(v))); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:300]})" if n and not v else ""), flush=True)
U = ctypes.windll.user32; K = ctypes.windll.kernel32
T = tempfile.mkdtemp(prefix="ccs-instance-")

def free_port() -> int:
    s = socket.socket(); s.bind(("127.0.0.1", 0)); p = s.getsockname()[1]; s.close(); return p
def root(name: str, port: int) -> str:
    d = os.path.join(T, name); os.makedirs(d)
    with open(os.path.join(d, "companion.json"), "w", encoding="utf-8") as f: json.dump({"port": port}, f)
    return d
def root_id(d: str) -> str:   # Program.DataRootId: the full path, trailing separators set aside, upper case, SHA-256, first 8 bytes
    return hashlib.sha256(os.path.abspath(d).rstrip("\\/").upper().encode("utf-8")).digest()[:8].hex()
def mutex_exists(name: str) -> bool:
    h = K.OpenMutexW(0x00100000, False, name)   # SYNCHRONIZE
    if h: K.CloseHandle(h); return True
    return False
def cdp_eval(ws_url: str, expr: str):
    """one DevTools Runtime.evaluate over a plain WebSocket (no package): the expression's value"""
    import base64, struct
    from urllib.parse import urlparse
    u = urlparse(ws_url); s = socket.create_connection((u.hostname, u.port), timeout=10)
    key = base64.b64encode(os.urandom(16)).decode()
    s.sendall((f"GET {u.path} HTTP/1.1\r\nHost: {u.hostname}:{u.port}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n\r\n").encode())
    hdr = b""
    while b"\r\n\r\n" not in hdr: hdr += s.recv(1)
    msg = json.dumps({"id": 1, "method": "Runtime.evaluate", "params": {"expression": expr, "returnByValue": True}}).encode()
    mask = os.urandom(4); n = len(msg)
    head = bytes([0x81]) + (bytes([0x80 | n]) if n < 126 else bytes([0x80 | 126]) + struct.pack(">H", n))
    s.sendall(head + mask + bytes(b ^ mask[i % 4] for i, b in enumerate(msg)))
    def recv_n(k):
        buf = b""
        while len(buf) < k: buf += s.recv(k - len(buf))
        return buf
    while True:
        b0, b1 = recv_n(2); ln = b1 & 0x7F
        if ln == 126: ln = struct.unpack(">H", recv_n(2))[0]
        elif ln == 127: ln = struct.unpack(">Q", recv_n(8))[0]
        data = json.loads(recv_n(ln))
        if data.get("id") == 1: s.close(); return data.get("result", {}).get("result", {}).get("value")
def log(d: str) -> str:
    p = os.path.join(d, "logs", "desktop.log"); return open(p, encoding="utf-8", errors="replace").read() if os.path.exists(p) else ""
def children(pid: int) -> list:
    out = subprocess.run(["powershell", "-NoProfile", "-Command", f"(Get-CimInstance Win32_Process -Filter 'ParentProcessId={pid}').ProcessId"], capture_output=True, text=True).stdout
    return [int(x) for x in out.split() if x.strip().isdigit()]
def windows(pids: set) -> list:
    found = []
    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(h, _):
        q = wt.DWORD(); U.GetWindowThreadProcessId(h, ctypes.byref(q))
        if q.value in pids and U.IsWindowVisible(h):
            b = ctypes.create_unicode_buffer(256); U.GetWindowTextW(h, b, 256); found.append((h, q.value, b.value))
        return True
    U.EnumWindows(cb, 0); return found
def wait(pred, secs: float):
    end = time.time() + secs
    while time.time() < end:
        v = pred()
        if v: return v
        time.sleep(0.3)
    return pred()
def ping(port: int) -> bool:
    try: return urllib.request.urlopen(f"http://127.0.0.1:{port}/api/ping", timeout=2).status == 200
    except Exception: return False
def start(d: str, *args) -> subprocess.Popen:
    return subprocess.Popen([A.launcher, *args], env=dict(os.environ, CCS_STUDIO_DATA=d))
def close(p: subprocess.Popen):
    """close the windows of a program this test started (its dialogs first), then wait; only if it does not end, end it and its backend"""
    tree = {p.pid, *children(p.pid)}
    for _ in range(20):
        if p.poll() is not None: break
        for h, _pid, title in sorted(windows(tree), key=lambda w: "Classic Cart Studio" in w[2]): U.PostMessageW(h, 0x0010, 0, 0)   # WM_CLOSE
        time.sleep(1.5)
    if p.poll() is None: subprocess.run(["taskkill", "/PID", str(p.pid), "/T", "/F"], capture_output=True)
    p.wait(30)
    for c in tree - {p.pid}:
        subprocess.run(["taskkill", "/PID", str(c), "/F"], capture_output=True)
def folder_state(d: str, skip=("logs", "webview2")) -> dict:
    out = {}
    for r, ds, fs in os.walk(d):
        ds[:] = [x for x in ds if os.path.relpath(os.path.join(r, x), d).split(os.sep)[0] not in skip]
        for f in fs:
            q = os.path.join(r, f)
            if os.path.relpath(q, d).split(os.sep)[0] in skip: continue
            st = os.stat(q); out[os.path.relpath(q, d)] = (st.st_size, st.st_mtime_ns)
    return out
def user_env() -> str | None:
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as k: return winreg.QueryValueEx(k, "CCS_STUDIO_DATA")[0]
    except OSError: return None

DEFAULT = os.path.join(os.environ["LOCALAPPDATA"], "ClassicCartStudio")
default_before = {n: folder_state(os.path.join(DEFAULT, n), ()) for n in ("personal", "saves", "play") if os.path.isdir(os.path.join(DEFAULT, n))}
ok("environment_not_set_before", "CCS_STUDIO_DATA" not in os.environ and user_env() is None, (os.environ.get("CCS_STUDIO_DATA"), user_env()))
P1, P2 = free_port(), free_port()
R1, R2, R3 = root("one", P1), root("two", P1), root("three", P2)   # two shares one's port, three has its own
started = []
try:
    # ---- same folder: the second start hands over
    a = start(R1); started.append(a)
    up = wait(lambda: ping(P1) and windows({a.pid}), 90)
    ok("first_window_and_backend", bool(up), log(R1)[-400:])
    ok("first_window_lock_is_its_folders", mutex_exists(r"Local\ClassicCartStudio.Main." + root_id(R1)) and not mutex_exists(r"Local\ClassicCartStudio.Main." + root_id(R2)))
    ok("setup_wait_lock_held", mutex_exists(r"Local\ClassicCartStudio.Main"))
    b = start(R1); rc = None
    try: rc = b.wait(30)
    except subprocess.TimeoutExpired: started.append(b)
    ok("same_folder_second_start_ends_at_once", rc == 0, rc)
    ok("same_folder_handed_over", wait(lambda: "activated by another instance" in log(R1) and "forwarded to the running instance" in log(R1), 15), log(R1)[-400:])
    ok("same_folder_no_second_window", len([w for w in windows({a.pid, b.pid}) if "Classic Cart Studio" in w[2]]) == 1, windows({a.pid, b.pid}))
    # ---- another folder, the first window's port: refused plainly, nothing handed over, the first folder unchanged
    def settled(d, quiet=8, most=120):   # the first window's own first-launch work (Included Games, its settings) finishes before the comparison
        end, last, since = time.time() + most, None, time.time()
        while time.time() < end:
            s = folder_state(d)
            if s != last: last, since = s, time.time()
            elif time.time() - since >= quiet: return s
            time.sleep(1)
        return last
    r1_before = settled(R1); handovers = log(R1).count("activated by another instance")
    dt = free_port()
    c = subprocess.Popen([A.launcher, f"--devtools-port={dt}"], env=dict(os.environ, CCS_STUDIO_DATA=R2, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=f"--remote-debugging-port={dt}")); started.append(c)
    said = wait(lambda: "port busy: Another copy of Classic Cart Studio" in log(R2), 90)
    ok("other_folder_same_port_says_so", bool(said), log(R2)[-500:])
    def pages():
        try: return [t.get("url", "") for t in json.load(urllib.request.urlopen(f"http://127.0.0.1:{dt}/json", timeout=2)) if t.get("type") == "page"]
        except Exception: return None
    seen = wait(lambda: pages() and "its page is not loaded" in log(R2), 30); time.sleep(3); urls = pages()
    ok("other_folder_never_shows_the_other_backends_page", bool(seen) and urls is not None and not any(u.startswith((f"http://127.0.0.1:{P1}", f"http://localhost:{P1}")) for u in urls), (urls, log(R2)[-300:]))
    ok("other_folder_runs_its_own_window", c.poll() is None and bool(windows({c.pid})), (c.poll(), windows({c.pid})))
    ok("other_folder_has_its_own_lock", mutex_exists(r"Local\ClassicCartStudio.Main." + root_id(R2)))
    ok("other_folder_nothing_handed_to_the_first", log(R1).count("activated by another instance") == handovers and "forwarded to the running instance" not in log(R2))
    ok("other_folder_uses_its_own_folder", os.path.isdir(os.path.join(R2, "webview2")) and "start " in log(R2))
    close(c); started.remove(c)
    data_of = lambda s: {k: v for k, v in s.items() if not os.path.basename(k).startswith("desktop")}   # its games, collections, saves and settings (its own window's place and page state may change as focus moves)
    ok("first_folder_unchanged", data_of(folder_state(R1)) == data_of(r1_before), sorted(set(data_of(folder_state(R1)).items()) ^ set(data_of(r1_before).items()))[:6])
    ok("first_window_still_running", a.poll() is None and ping(P1))
    # ---- another folder on its own port: its own backend in its own folder
    d = start(R3); started.append(d)
    ok("own_port_own_backend", bool(wait(lambda: ping(P2) and windows({d.pid}), 90)), log(R3)[-400:])
    srv = os.path.join(R3, "logs", "companion.log")   # the backend's own log: "headless backend listening ... data=<folder>"
    ok("own_port_backend_data_folder", wait(lambda: os.path.exists(srv) and ("data=" + os.path.abspath(R3)) in open(srv, encoding="utf-8", errors="replace").read(), 20))
    close(d); started.remove(d)
    # ---- a server that merely answers like Classic Cart Studio on the window's port: Reload and Try Again never load its page
    asked = []
    class Pretend(BaseHTTPRequestHandler):
        def do_GET(self):
            asked.append(self.path); api = self.path.startswith("/api/")
            b = json.dumps({"app": "classic-cart-studio-backend", "version": "not-this-window"}).encode() if api else b"<!doctype html><title>not this window's</title>"
            self.send_response(200); self.send_header("Content-Type", "application/json" if api else "text/html"); self.send_header("Content-Length", str(len(b))); self.end_headers(); self.wfile.write(b)
        def log_message(self, *a): pass
    srv = ThreadingHTTPServer(("127.0.0.1", 0), Pretend); threading.Thread(target=srv.serve_forever, daemon=True).start(); P4 = srv.server_port
    R4 = root("four", P4); dt4 = free_port()
    e = subprocess.Popen([A.launcher, f"--devtools-port={dt4}"], env=dict(os.environ, CCS_STUDIO_DATA=R4, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=f"--remote-debugging-port={dt4}")); started.append(e)
    ok("pretend_server_not_trusted_at_start", bool(wait(lambda: "its page is not loaded" in log(R4), 90)), log(R4)[-400:])
    pages_of = lambda: [x for x in asked if not x.startswith("/api/")]
    def own_reload():   # the window's own App tools > Reload (UI Automation in this window's process only)
        ps = os.path.join(T, "reload.ps1")
        with open(ps, "w", encoding="utf-8") as f:
            f.write("Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes\n$A = [System.Windows.Automation.AutomationElement]\n"
                    "function ByName($n) { $c = New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, %d)), (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $n))); $A::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c) }\n"
                    "$m = ByName 'App tools'; if (-not $m) { throw 'no App tools' }; $m.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 500\n"
                    "$r = ByName 'Reload'; if (-not $r) { throw 'no Reload' }; $r.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); 'reloaded'\n" % e.pid)
        r = subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ps], capture_output=True, text=True, timeout=60); return r.returncode == 0, (r.stdout + r.stderr)[-300:]
    done, out = own_reload(); time.sleep(4)
    ok("reload_never_loads_the_pretend_page", done and pages_of() == [], (done, out, asked[-6:]))
    try:
        tabs = json.load(urllib.request.urlopen(f"http://127.0.0.1:{dt4}/json", timeout=5))
        ws_url = next(x["webSocketDebuggerUrl"] for x in tabs if x.get("type") == "page")
        tried = cdp_eval(ws_url, "(()=>{const b=[...document.querySelectorAll('button')].find(x=>/Try Again/.test(x.textContent)); if(b){b.click(); return true} return false})()")
    except Exception as ex: tried = repr(ex)
    time.sleep(4)
    ok("try_again_never_loads_the_pretend_page", tried is True and pages_of() == [], (tried, asked[-6:]))
    srv.shutdown(); srv.server_close()
    done, out = own_reload()
    def tabs4():
        try: return [x.get("url", "") for x in json.load(urllib.request.urlopen(f"http://127.0.0.1:{dt4}/json", timeout=2)) if x.get("type") == "page"]
        except Exception: return []
    ok("reload_after_the_port_is_free_loads_its_own_page", done and bool(wait(lambda: any(u.startswith(f"http://127.0.0.1:{P4}") for u in tabs4()), 90)), (tabs4(), log(R4)[-400:]))
    srv4 = os.path.join(R4, "logs", "companion.log")
    ok("reload_own_backend_data_folder", wait(lambda: os.path.exists(srv4) and ("data=" + os.path.abspath(R4)) in open(srv4, encoding="utf-8", errors="replace").read(), 20))
    close(e); started.remove(e)
finally:
    for p in started: close(p)
default_after = {n: folder_state(os.path.join(DEFAULT, n), ()) for n in ("personal", "saves", "play") if os.path.isdir(os.path.join(DEFAULT, n))}
ok("default_folder_unchanged", default_after == default_before, sorted(default_after))
ok("environment_not_set_after", "CCS_STUDIO_DATA" not in os.environ and user_env() is None)
shutil.rmtree(T, ignore_errors=True)
bad = [k for k, v in checks if not v]
print(f"\n{len(checks) - len(bad)}/{len(checks)} checks passed" + (": FAILED " + ", ".join(bad) if bad else ""))
sys.exit(1 if bad else 0)
