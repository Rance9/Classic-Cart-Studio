"""The console boundary for an activation rehearsal, and the separate permissions (k3e, D-119 brief section 2), PC only, with no console:
  - the test boundary (console_test_guard) refuses a real session once a tier is open, before any worker starts, and records it;
  - without it, a source run still cannot start the shipped transport worker (it exists only inside the frozen app);
  - a console session opened to inspect refuses every change itself, before the worker is asked; one opened to install asks the gate;
  - the older game sync through hakchi (G2) is refused whatever the gate says, with no transport opened.
A tier is opened (or closed) here only inside one test, in this process, with the boundary in front; the gate constants are restored
afterwards, so the suite also runs unchanged in the activation rehearsal (the delta applied to a temporary copy).
  python tools/companion/test_console_guard.py"""
from __future__ import annotations
import io, json, os, subprocess, sys, tempfile, unittest
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path[:0] = [HERE, os.path.join(HERE, "..", "rom-pipeline")]
import console_test_guard as guard   # noqa: E402  (first: the controlled boundary)
from console_engine import gate, worker_console as WC, engine as EN, transport as TR   # noqa: E402
from console_engine.worker import Worker   # noqa: E402
def rd(p):
    with open(p, encoding="utf-8") as f: return f.read()

class Opened:
    """open the two physical tiers in this process for one test (the activation rehearsal's state), then close them again"""
    def __init__(self, inspect=True, write=True): self.want = (inspect, write)
    def __enter__(self): self.was = (gate.PHYSICAL_INSPECT_ENABLED, gate.PHYSICAL_WRITE_ENABLED); gate.PHYSICAL_INSPECT_ENABLED, gate.PHYSICAL_WRITE_ENABLED = self.want; return self
    def __exit__(self, *a): gate.PHYSICAL_INSPECT_ENABLED, gate.PHYSICAL_WRITE_ENABLED = self.was

class Sentinel:
    """stands in for a process or a transport: being reached at all is the failure"""
    def __init__(self): self.calls = []
    def __call__(self, *a, **k): self.calls.append((a, k)); raise AssertionError(f"reached: {a} {k}")

class T(unittest.TestCase):
    def setUp(self):
        try: os.remove(guard.LOG)
        except OSError: pass
        self.popen = Sentinel(); self._p = WC.subprocess.Popen; WC.subprocess.Popen = self.popen
    def tearDown(self):
        WC.subprocess.Popen = self._p
        try: os.remove(guard.LOG)
        except OSError: pass
    def requests(self):
        try: return rd(guard.LOG).split()
        except OSError: return []

    def test_01_closed_the_boundary_refuses_exactly_as_the_shipped_opener(self):
        with Opened(False, False):   # (this version's state; the activation rehearsal runs this suite with the gate open too)
            for tier in ("inspect", "install"):
                with self.assertRaises(gate.GateClosed): WC.open_worker_console("ssh", tier=tier)
                with self.assertRaises(gate.GateClosed): guard._guarded.original("clovershell", tier=tier)
        self.assertEqual(self.requests(), []); self.assertEqual(self.popen.calls, [])

    def test_02_open_a_real_session_is_blocked_before_any_worker_and_recorded(self):
        with Opened():
            for kind, tier in (("ssh", "inspect"), ("clovershell", "install")):
                with self.assertRaises(guard.RealConsoleBlocked): WC.open_worker_console(kind, tier=tier)
            from console_engine import ccs_install as CI
            with self.assertRaises(guard.RealConsoleBlocked): CI.open_console("ssh")
        self.assertEqual(self.requests(), ["ssh", "inspect", "clovershell", "install", "ssh", "install"]); self.assertEqual(self.popen.calls, [], "no process was started")

    def test_03_open_a_physical_worker_command_reaches_the_boundary_and_no_further(self):
        out = io.StringIO(); w = Worker(tempfile.mkdtemp(prefix="ccs-guard-"), out=out)
        with Opened():
            w.handle(json.dumps({"v": 1, "id": "i", "cmd": "home-inspect", "args": {}}))
        ev = json.loads(out.getvalue().splitlines()[-1]); self.assertEqual(ev["event"], "error"); self.assertIn("blocked by the test boundary", ev["text"])
        self.assertEqual(self.requests(), ["ssh", "inspect"], "the shipped opener tries ssh first; it stopped there"); self.assertEqual(self.popen.calls, [])

    def test_04_without_the_boundary_a_source_run_cannot_start_the_shipped_worker(self):
        self.assertIsNone(WC.worker_exe(), "the transport worker is named only inside the frozen app")
        with Opened():
            with self.assertRaises(FileNotFoundError): guard._guarded.original("ssh", tier="inspect")
        self.assertEqual(self.popen.calls, [], "no process was started"); self.assertEqual(self.requests(), [])
        src = rd(WC.__file__); self.assertEqual(src.count("subprocess.Popen("), 1, "one place starts the transport worker: open_worker_console")
        self.assertLess(src.index("gate.require(tier"), src.index("exe = worker_exe()"), "the gate before the worker is looked for")

    def test_05_an_inspect_session_never_writes_and_an_install_session_asks_the_gate(self):
        class Proc:   # a worker process that must not be asked anything
            def __init__(self): self.stdin = self; self.stdout = self; self.stderr = None; self.asked = []
            def write(self, b): self.asked.append(b); raise AssertionError("the worker was asked")
            def flush(self): pass
            def readline(self): raise AssertionError("the worker was read")
        calls = {"put": lambda c: c.put("/var/lib/ccs/x", b"x"), "mkdir": lambda c: c.mkdir("/var/lib/ccs/d"), "rmdir": lambda c: c.rmdir("/var/lib/ccs/d"),
                 "rename": lambda c: c.rename("/var/lib/ccs/a", "/var/lib/ccs/b"), "remove": lambda c: c.remove("/var/lib/ccs/x", "a" * 64),
                 "pointer": lambda c: c.set_pointer("/var/lib/ccs/pkg/current", "v1"), "module-install": lambda c: c.pack_install("ccs-home", {}),
                 "module-uninstall": lambda c: c.pack_uninstall("ccs-home")}
        for opened in ((False, False), (True, False), (True, True)):
            with Opened(*opened):
                for name, fn in calls.items():
                    p = Proc(); c = WC.WorkerConsole(p, simulated=False, kind="ssh", tier="inspect")
                    with self.assertRaises(gate.GateClosed, msg=f"{name} on an inspect session ({opened})") as e: fn(c)
                    self.assertIn("opened to inspect", str(e.exception)); self.assertEqual(p.asked, [], name)
        for opened in ((False, False), (True, False)):   # closed, and a staged activation that opened only the inspect tier
            with Opened(*opened):
                p = Proc(); c = WC.WorkerConsole(p, simulated=False, kind="ssh", tier="install")
                with self.assertRaises(gate.GateClosed): c.mkdir("/var/lib/ccs/d")   # the write tier is closed
                self.assertEqual(p.asked, [])
        self.assertEqual(sorted(calls), sorted(["put", "mkdir", "rmdir", "rename", "remove", "pointer", "module-install", "module-uninstall"]), "the worker's write commands (WorkerServer.WriteCommands)")

    def test_06_an_inspect_session_needs_a_worker_that_asks_its_write_tier_per_command(self):
        src = rd(WC.__file__); i = src.index("def open_worker_console")
        body = src[i:]; self.assertLess(body.index('"write-tier" not in'), body.index('c.call("connect"'), "checked before connect")
        cs = os.path.join(HERE, "..", "console-worker", "CcsWorker", "Protocol", "WorkerServer.cs")
        if os.path.isfile(cs): self.assertIn('"write-tier"', rd(cs), "the transport worker lists the feature")

    def test_07_the_older_game_sync_is_refused_whatever_the_gate(self):
        sent = Sentinel(); was = TR.open_transport; TR.open_transport = sent
        try:
            for opened in ((False, False), (True, True)):
                with Opened(*opened):
                    events = []; e = EN.Engine.__new__(EN.Engine); e.emit = lambda *a, **k: events.append((a, k))
                    r = EN.Engine.execute(e, {"hash": "a" * 64}, "ssh"); self.assertEqual(r["code"], "physical-disabled"); self.assertIn("not part of Classic Cart Studio", r["state"])
                    out = io.StringIO(); w = Worker(tempfile.mkdtemp(prefix="ccs-guard-"), out=out)
                    w.handle(json.dumps({"v": 1, "id": "x", "cmd": "execute", "args": {"plan_hash": "b" * 64, "transport": "ssh"}}))
                    ev = json.loads(out.getvalue().splitlines()[-1]); self.assertEqual(ev["event"], "error"); self.assertEqual(ev["code"], "physical-disabled")   # k3f: a refusal event (the G1 guard's contract)
        finally: TR.open_transport = was
        self.assertEqual(sent.calls, [], "no transport was opened"); self.assertNotIn("open_transport", rd(EN.__file__), "the engine opens no transport by kind")
        nc = rd(os.path.join(HERE, "nes_companion.py")); line = nc[nc.index('if op == "execute"'):].split("\n", 1)[0]
        self.assertIn("403", line); self.assertIn("physical-disabled", line); self.assertNotIn("cr.call", line, "the route starts no worker")

    def test_08_the_console_test_modules_put_the_boundary_first(self):
        for f in ("test_console_home.py", "test_console_dispatch.py", "test_console_lifecycle.py", "test_ccs_install.py", "test_k3_boundaries.py", "test_worker_console.py", "test_console_guard.py"):
            s = rd(os.path.join(HERE, f)); g = s.find("import console_test_guard")
            self.assertGreater(g, 0, f); first = min(x for x in (s.find("from console_engine"), s.find("import console_runs"), s.find("import test_")) if x > 0)
            self.assertLess(g, first, f"{f}: the boundary before any console module")

if __name__ == "__main__":
    unittest.main(verbosity=2)
