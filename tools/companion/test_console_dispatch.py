"""The production dispatcher (console_home.Dispatcher -> console_runs -> console_engine.worker -> ccs_install) for the physical console
operations, proven through private injected sessions: the worker runs in-process over pipes (the same line protocol console_runs speaks to
the frozen worker), its console opener replaced by one that returns an already simulated stand-in, and its gate and the confirmations given
by the test. Nothing here opens a device, and the app's own instance (console_home.DISPATCH) is shown refusing everything with the gate as
it is in code. The flow: check the console, check the plan, confirm, install, verify, copy the saves, review and remove; also a lost
connection that stops (no rollback by itself) and its separately confirmed recovery, another console answering, an old check, a confirmation
used twice or for something else, one operation at a time, a cancel before the switch and a plan whose package changed.
  python tools/companion/test_console_dispatch.py"""
from __future__ import annotations
import io, json, os, shutil, subprocess, sys, tempfile, threading, time, unittest
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE); sys.path.insert(0, os.path.join(HERE, "..", "rom-pipeline"))
import console_test_guard   # noqa: E402,F401  (first: the controlled console boundary, k3e)
_T = tempfile.mkdtemp(prefix="ccs-dispatch-"); os.environ["NES_STUDIO_DATA"] = _T   # console_runs' folder: never the owner's data
import console_runs, console_home, console_confirm as CC   # noqa: E402
from console_engine import gate, ccs_install as CI   # noqa: E402
from console_engine.worker import Worker, read_home_records   # noqa: E402
from test_ccs_install import make_package, wr, rd, tree, changes   # noqa: E402
from test_k3_boundaries import boot_state   # noqa: E402

class InProc:
    """a Popen-like worker: console_engine.worker.Worker.serve in a thread, its input and output over OS pipes"""
    def __init__(self, **kw):
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

class Clock:
    def __init__(self): self.t = 1_000_000.0
    def __call__(self): return self.t

class Base(unittest.TestCase):
    def setUp(self):
        self.t = tempfile.mkdtemp(prefix="ccs-disp-", dir=_T); self.pkg = make_package(os.path.join(self.t, "staged"))
        self.root = os.path.join(self.t, "console"); CI.make_stand_in(self.root, "hakchi-ready"); self.wroot = os.path.join(self.t, "worker")
        self.faults = {}; self.opened = []; self.clock = Clock(); self.spawned = 0
        def factory(tier, pkg):
            self.opened.append(tier); return CI.FakeConsole(self.root, dict(self.faults))
        def spawn():
            self.spawned += 1; return InProc(root=self.wroot, console_package=self.pkg, console_factory=factory, allowed=lambda t: True)
        self.d = console_home.Dispatcher(allowed=lambda t: True, confirmations=CC.Confirmations(allowed=lambda t: True, clock=self.clock), clock=self.clock, spawn=spawn)
    def tearDown(self): shutil.rmtree(self.t, ignore_errors=True)
    def wait(self, rid, timeout=120):
        end = time.time() + timeout
        while time.time() < end:
            p = console_runs.poll(rid, 0)
            if p and p["done"]: return p
            time.sleep(0.05)
        self.fail(f"run {rid} did not finish")
    def run_op(self, op, **body):
        code, r = self.d.post(op, body); self.assertEqual(code, 200, (op, r)); p = self.wait(r["run"])
        self.assertEqual(p["result"]["event"], "result", (op, p["result"])); return p["result"]["result"], p
    def plan(self):
        code, r = self.d.post("plan", {}); self.assertEqual(code, 200, r); return r["result"]["hash"]
    def confirm(self, op, **subject):
        code, r = self.d.post("confirm", {"operation": op, **subject}); self.assertEqual(code, 200, r); self.assertIn("Once, within 5 minutes", r["covers"]); return r["confirmation"]

class Flow(Base):
    def test_01_check_confirm_install_verify_export_review_and_remove(self):
        h = self.plan(); stock = CI.stock_digest(self.root)
        code, r = self.d.post("check", {"plan_hash": h}); self.assertEqual((code, r.get("code")), (409, "check-console"))   # nothing before the console is checked
        ins, _ = self.run_op("inspect"); self.assertTrue(ins["ok"]); self.assertEqual(ins["module"], "absent"); s = self.d.public_console(); self.assertIsNotNone(s); self.assertEqual(s["identity"]["system"], "nes-usa")
        chk, _ = self.run_op("check", plan_hash=h); self.assertTrue(chk["ok"], chk); self.assertFalse(chk["already"])
        code, r = self.d.post("install", {"plan_hash": h}); self.assertEqual((code, r.get("code")), (403, "not-confirmed"))   # a write needs its confirmation
        tok = self.confirm("install", plan_hash=h)
        res, p = self.run_op("install", plan_hash=h, confirmation=tok); self.assertTrue(res["ok"], res); self.assertEqual(res["result"], "installed"); self.assertTrue(res["simulated"])
        self.assertTrue(any(e.get("stage") == "stage" for e in p["events"])); self.assertEqual(boot_state(self.root), f"ccs:{res['copy']}")
        code, r = self.d.post("install", {"plan_hash": h, "confirmation": tok}); self.assertEqual(code, 403)   # used up
        code, r = self.d.post("confirm", {"operation": "update", "plan_hash": h}); self.assertEqual((code, r.get("code")), (409, "check-console"))   # written since the check
        v, _ = self.run_op("verify", plan_hash=h); self.assertTrue(v["verified"], v)
        wr(os.path.join(self.root, "var/lib/ccs/data/saves/ab/slot1.ccsstate"), os.urandom(1_200_000))
        x, _ = self.run_op("export"); self.assertEqual(x["status"], "complete", x); self.assertTrue(os.path.isfile(os.path.join(self.wroot, "exports", x["folder"], "data/saves/ab/slot1.ccsstate")))
        self.run_op("inspect"); rv, _ = self.run_op("uninstall-review"); self.assertTrue(rv["ok"], rv)
        tok = self.confirm("uninstall", review=rv["hash"]); u, _ = self.run_op("uninstall", review=rv["hash"], confirmation=tok); self.assertTrue(u["ok"], u)
        self.assertEqual(boot_state(self.root), "nintendo"); self.assertEqual(CI.stock_digest(self.root), stock)
        self.assertTrue(os.path.isfile(os.path.join(self.root, "var/lib/ccs/data/saves/ab/slot1.ccsstate")))   # saves kept
        recs = read_home_records(os.path.join(self.wroot, "records"), None)
        self.assertTrue({"inspect", "check", "install", "verify", "export", "uninstall-review", "uninstall"} <= {o.get("kind") for o in recs["operations"]}, [o.get("kind") for o in recs["operations"]])
        self.assertTrue(all(o.get("simulated") is True for o in recs["operations"]))

    def test_02_a_lost_connection_stops_and_recovery_is_its_own_confirmed_operation(self):
        h = self.plan(); before = tree(self.root); self.run_op("inspect"); self.faults = {"after": 14}
        res, _ = self.run_op("install", plan_hash=h, confirmation=self.confirm("install", plan_hash=h)); self.assertEqual(res["result"], "stopped", res); self.assertIsNone(res["rolled_back"])
        self.assertNotEqual(changes(before, tree(self.root)), []); self.faults = {}
        self.run_op("inspect"); rr, _ = self.run_op("recover-review"); self.assertEqual(len(rr["attempts"]), 1); a = rr["attempts"][0]; self.assertTrue(a["recoverable"], a)
        code, r = self.d.post("recover", {"attempt": a["attempt"]}); self.assertEqual(code, 403)
        rec, _ = self.run_op("recover", attempt=a["attempt"], confirmation=self.confirm("recover", attempt=a["attempt"])); self.assertEqual(rec["result"], "rolled-back", rec)
        self.assertEqual(changes(before, tree(self.root)), [])

class Binding(Base):
    def test_10_another_console_answering_changes_nothing(self):
        h = self.plan(); self.run_op("inspect"); tok = self.confirm("install", plan_hash=h)
        p = os.path.join(self.root, "console.json"); info = json.loads(rd(p)); info["hwid"] = "sim-ANOTHER"; wr(p, json.dumps(info)); before = tree(self.root)
        res, _ = self.run_op("install", plan_hash=h, confirmation=tok); self.assertEqual(res["result"], "refused", res); self.assertIn("not the console that was checked", res["why"])
        self.assertEqual(changes(before, tree(self.root)), [])

    def test_11_an_old_check_a_new_check_and_wrong_confirmations(self):
        h = self.plan(); self.run_op("inspect"); tok = self.confirm("install", plan_hash=h)
        code, r = self.d.post("install", {"plan_hash": "0" * 64, "confirmation": tok}); self.assertEqual((code, r["code"]), (403, "mismatch"))   # another plan
        tok = self.confirm("install", plan_hash=h); code, r = self.d.post("update", {"plan_hash": h, "confirmation": tok}); self.assertEqual((code, r["code"]), (403, "mismatch"))   # another operation
        tok = self.confirm("install", plan_hash=h); self.run_op("inspect")   # a new check voids it
        code, r = self.d.post("install", {"plan_hash": h, "confirmation": tok}); self.assertEqual((code, r["code"]), (403, "not-confirmed"))
        tok = self.confirm("install", plan_hash=h); self.clock.t += 301
        code, r = self.d.post("install", {"plan_hash": h, "confirmation": tok}); self.assertEqual((code, r["code"]), (403, "expired"))
        self.clock.t += 600; code, r = self.d.post("confirm", {"operation": "install", "plan_hash": h}); self.assertEqual((code, r["code"]), (409, "check-console"))
        self.assertEqual(boot_state(self.root), "nintendo")

    def test_12_another_system_is_never_a_checked_console(self):
        CI.make_stand_in(os.path.join(self.t, "snes"), "snes-eur"); self.root = os.path.join(self.t, "snes")
        ins, _ = self.run_op("inspect"); self.assertFalse(ins["ok"]); self.assertIsNone(self.d.public_console())
        code, r = self.d.post("confirm", {"operation": "install", "plan_hash": self.plan()}); self.assertEqual(code, 409)

class Lifetime(Base):
    def test_20_one_operation_at_a_time_and_a_cancel_before_the_switch(self):
        big = make_package(os.path.join(self.t, "big"), tag="big", extra={f"roms/{i:02x}" + "cd" * 31 + ".nes": os.urandom(400_000) for i in range(60)})
        shutil.rmtree(self.pkg); shutil.copytree(big, self.pkg); h = self.plan(); self.run_op("inspect")
        code, r = self.d.post("install", {"plan_hash": h, "confirmation": self.confirm("install", plan_hash=h)}); self.assertEqual(code, 200); rid = r["run"]
        code2, r2 = self.d.post("verify", {"plan_hash": h}); self.assertEqual((code2, r2.get("code")), (409, "busy"))
        time.sleep(0.2); self.assertEqual(self.d.post("cancel", {"run": rid})[0], 200)
        res = self.wait(rid)["result"]["result"]; self.assertEqual(res["result"], "cancelled", res); self.assertEqual(res["switched"], "no"); self.assertEqual(boot_state(self.root), "nintendo")

    def test_21_a_plan_whose_package_changed_is_obsolete(self):
        h = self.plan(); self.run_op("inspect"); tok = self.confirm("install", plan_hash=h)
        man = os.path.join(self.pkg, "MANIFEST.json"); m = json.loads(rd(man)); m["label"] = "changed"; wr(man, json.dumps(m))
        code, r = self.d.post("install", {"plan_hash": h, "confirmation": tok}); self.assertEqual(code, 200); p = self.wait(r["run"])
        self.assertEqual(p["result"]["code"], "plan-obsolete"); self.assertEqual(boot_state(self.root), "nintendo")

class Closed(unittest.TestCase):
    def test_30_the_apps_dispatcher_refuses_every_physical_operation_before_any_worker(self):
        self.assertFalse(gate.PHYSICAL_INSPECT_ENABLED); self.assertFalse(gate.PHYSICAL_WRITE_ENABLED)
        spawned = []; d = console_home.Dispatcher(spawn=lambda: spawned.append(1))
        for op in ("inspect", "check", "verify", "export", "uninstall-review", "recover-review", "install", "update", "uninstall", "recover", "confirm"):
            code, r = d.post(op, {"operation": "install", "plan_hash": "a" * 64, "confirmation": "x", "review": "b" * 64, "attempt": "c" * 8, "device": "sim-0001"})
            self.assertEqual((code, r.get("code")), (403, "physical-disabled"), (op, r))
        self.assertEqual(spawned, []); self.assertIs(console_home.DISPATCH.allowed, gate.allowed); self.assertIs(console_home.DISPATCH.conf, CC.BACKEND)
        w = Worker(os.path.join(_T, "closed-worker"), out=io.StringIO())   # the worker's own refusal, whatever the arguments
        for cmd in ("home-inspect", "home-check", "home-execute", "home-verify", "home-uninstall-review", "home-uninstall", "home-recover-review", "home-recover", "home-export-console"):
            w.out = io.StringIO(); w.handle(json.dumps({"v": 1, "id": "x", "cmd": cmd, "args": {"plan_hash": "a" * 64, "device": "sim-0001", "review": "b" * 64, "attempt": "c" * 8}}))
            self.assertEqual(json.loads(w.out.getvalue())["code"], "physical-disabled", cmd)

if __name__ == "__main__":
    try: unittest.main(verbosity=2)
    finally: shutil.rmtree(_T, ignore_errors=True)
