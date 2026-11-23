r"""One Classic Cart Studio window per data folder (tools/desktop/ClassicCartStudio/Program.cs): the built launcher started for real, with its
backend beside it, in new data folders (NES_STUDIO_DATA set for the started programs only) on loopback ports chosen here; no console, no
network, no Hakchi2 CE. A Classic Cart Studio already open on this computer with its own data folder is never touched: only the programs this
test starts are closed.
  same folder      a second start for the same data folder hands over to the open window (it logs the handover, the second start ends at
                   once) and no second window or backend appears
  other folder     a start for another data folder never reaches the first window (no handover there, a lock and pipe of its own); when
                   the first window's backend holds the port it says so ("Another copy of Classic Cart Studio") instead of using it, its
                   window never shows a page from that port, and the first data folder is unchanged; on a port of its own it runs with
                   its own backend in its own data folder
  default folder   the default data folder's personal, saves and play folders are the same before and after (read, never written)
  environment      NES_STUDIO_DATA is not set in this process or in the user's saved environment, before or after
  python test_studio_instance.py --launcher <ClassicCartStudio.exe>        Exit 0 = PASS."""
from __future__ import annotations
import argparse, ctypes, ctypes.wintypes as wt, hashlib, json, os, shutil, socket, subprocess, sys, tempfile, time, urllib.request, winreg
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
    return subprocess.Popen([A.launcher, *args], env=dict(os.environ, NES_STUDIO_DATA=d))
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
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as k: return winreg.QueryValueEx(k, "NES_STUDIO_DATA")[0]
    except OSError: return None

DEFAULT = os.path.join(os.environ["LOCALAPPDATA"], "ClassicCartStudio")
default_before = {n: folder_state(os.path.join(DEFAULT, n), ()) for n in ("personal", "saves", "play") if os.path.isdir(os.path.join(DEFAULT, n))}
ok("environment_not_set_before", "NES_STUDIO_DATA" not in os.environ and user_env() is None, (os.environ.get("NES_STUDIO_DATA"), user_env()))
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
    c = subprocess.Popen([A.launcher, f"--devtools-port={dt}"], env=dict(os.environ, NES_STUDIO_DATA=R2, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=f"--remote-debugging-port={dt}")); started.append(c)
    said = wait(lambda: "port busy: Another copy of Classic Cart Studio" in log(R2), 90)
    ok("other_folder_same_port_says_so", bool(said), log(R2)[-500:])
    def pages():
        try: return [t.get("url", "") for t in json.load(urllib.request.urlopen(f"http://127.0.0.1:{dt}/json", timeout=2)) if t.get("type") == "page"]
        except Exception: return None
    seen = wait(lambda: pages() and "port held by another program" in log(R2), 30); time.sleep(3); urls = pages()
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
finally:
    for p in started: close(p)
default_after = {n: folder_state(os.path.join(DEFAULT, n), ()) for n in ("personal", "saves", "play") if os.path.isdir(os.path.join(DEFAULT, n))}
ok("default_folder_unchanged", default_after == default_before, sorted(default_after))
ok("environment_not_set_after", "NES_STUDIO_DATA" not in os.environ and user_env() is None)
shutil.rmtree(T, ignore_errors=True)
bad = [k for k, v in checks if not v]
print(f"\n{len(checks) - len(bad)}/{len(checks)} checks passed" + (": FAILED " + ", ".join(bad) if bad else ""))
sys.exit(1 if bad else 0)
