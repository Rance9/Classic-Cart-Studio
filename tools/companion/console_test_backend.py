"""Test support only (never imported by the app, so never bundled): the backend (companion_app.py) as a test starts it, with two changes made
before it starts, each in this process only:
  - its environment probes answer nothing: no Windows device list (the console's USB presence) and no driver store is read by a test
    (CCS_TEST_SLOW_PROBE=<seconds>: the USB presence check takes that long, as on a slow computer);
  - with CCS_TEST_STANDIN=<folder> (a disposable stand-in console made by console_engine.ccs_install.make_stand_in), the app's console
    dispatcher is replaced by one whose worker runs in this process over pipes, as the app's own would over its worker's pipes, and opens
    that stand-in for each console session; the backup prerequisite is answered by the test (the file CCS_TEST_ADMIT_FILE names, read at
    each answer: "refuse" refuses, anything else admits); the file CCS_TEST_FAULTS_FILE names, when it exists, holds the stand-in's faults
    for the next session (ccs_install.FakeConsole: a lost connection before or after a given change). The page, its routes, the dispatcher,
    the confirmations, the worker protocol and the installation engine are the app's own.
The console boundary (console_test_guard) is in front of every real opener first.
  python console_test_backend.py <companion_app.py arguments...>
Copyright (C) 2026 Rance9, GPL-3.0-or-later."""
from __future__ import annotations
import io, json, os, runpy, subprocess, sys, threading
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path[:0] = [HERE, os.path.join(HERE, "..", "rom-pipeline")]
import console_test_guard   # noqa: E402,F401  (first: the controlled console boundary)
import hakchi_bridge as hb   # noqa: E402

_SLOW = float(os.environ.get("CCS_TEST_SLOW_PROBE") or 0)   # a slow computer (seconds): the USB presence check takes this long, as PowerShell can
def _usb():
    if _SLOW: import time; time.sleep(_SLOW)
    return []
hb.console_usb = _usb
hb.driver_status = lambda cfg=None, pnputil_text=None: {"state": "unknown", "inf": None}


class InProc:
    """a Popen-like worker: console_engine.worker.Worker.serve in a thread, its input and output over OS pipes (text, as console_runs speaks)"""
    def __init__(self, **kw):
        from console_engine.worker import Worker
        r1, w1 = os.pipe(); r2, w2 = os.pipe()
        self.stdin = io.TextIOWrapper(os.fdopen(w1, "wb"), encoding="utf-8", line_buffering=True); self.stdout = io.TextIOWrapper(os.fdopen(r2, "rb"), encoding="utf-8")
        self.stderr = io.StringIO(); inp = io.TextIOWrapper(os.fdopen(r1, "rb"), encoding="utf-8"); out = io.TextIOWrapper(os.fdopen(w2, "wb"), encoding="utf-8", line_buffering=True)
        w = Worker(out=out, **kw)
        def run():
            try: w.serve(inp)
            finally:
                try: out.close()
                except OSError: pass
        self.t = threading.Thread(target=run, daemon=True); self.t.start()
    def poll(self): return None if self.t.is_alive() else 0
    def wait(self, timeout=None):
        self.t.join(timeout)
        if self.t.is_alive(): raise subprocess.TimeoutExpired("in-process worker", timeout)
        return 0
    def kill(self): pass


def install_stand_in(standin: str, admit_file: str | None = None, faults_file: str | None = None):
    import console_home, console_runs
    from console_engine import ccs_install as CI
    from native_home import package as nhp
    pkg = os.path.join(nhp.package_root(), "console", "current"); opened = []
    def factory(tier, package):
        faults = {}
        try:
            with open(faults_file, encoding="utf-8") as f: faults = json.load(f)
        except (OSError, TypeError, ValueError): pass
        opened.append(tier); return CI.FakeConsole(standin, faults)
    def spawn(): return InProc(root=console_runs.ROOT, console_package=pkg, console_factory=factory, allowed=lambda t: True)
    def refused():
        try:
            with open(admit_file, encoding="utf-8") as f: return f.read().strip() == "refuse"
        except (OSError, TypeError): return False
    def admitted(hwid, op, subject):
        if refused(): return {"ok": False, "code": "backup-required", "text": "Back up this console first."}
        return {"ok": True, "ticket": "0" * 32}
    console_home.DISPATCH = console_home.Dispatcher(spawn=spawn, admit=admitted)
    console_home.DISPATCH.admit_preview = lambda hwid, op, subject: {"ok": not refused(), "data_only": False, "text": ""}


if __name__ == "__main__":
    if os.environ.get("CCS_TEST_STANDIN"): install_stand_in(os.environ["CCS_TEST_STANDIN"], os.environ.get("CCS_TEST_ADMIT_FILE"), os.environ.get("CCS_TEST_FAULTS_FILE"))
    sys.argv = sys.argv[1:]
    runpy.run_path(sys.argv[0], run_name="__main__")
