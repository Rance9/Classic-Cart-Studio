"""The physical operations' lifecycle in the production dispatcher and run pump (console_home.Dispatcher -> console_runs), around the moments
K3C4-F1 found open: a change counts from its admission (the checked console marked changed, every confirmation void), the dispatcher stays
busy until the worker has stopped and the completion has run, a completion acts only for its own admission, and every failure (no worker,
no answer, a cancel, a worker that is slow to stop or will not stop) leaves the console to be checked again. Same injected sessions as
test_console_dispatch (an in-process worker over pipes with a simulated stand-in) plus scripted workers for the failure paths; nothing here
opens a device.
  python tools/companion/test_console_lifecycle.py"""
from __future__ import annotations
import io, json, os, shutil, subprocess, sys, threading, time, unittest
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE); sys.path.insert(0, os.path.join(HERE, "..", "rom-pipeline"))
import console_test_guard   # noqa: E402,F401  (first: the controlled console boundary, k3e)
import test_console_dispatch as TD   # noqa: E402  (sets the data folder to its own temporary one first)
import console_runs   # noqa: E402
from console_engine import ccs_install as CI   # noqa: E402
from test_ccs_install import make_package, tree, changes   # noqa: E402
from test_k3_boundaries import boot_state   # noqa: E402

class Scripted:
    """a Popen-like worker that answers (or not) and stops (or not) as told"""
    def __init__(self, answer=True, quits=True, killable=True):
        self.answer, self.quits, self.killable, self.killed = answer, quits, killable, False; self.stdin = io.StringIO(); self.stderr = io.StringIO()
    @property
    def stdout(self):
        def lines():
            if self.answer:
                rid = json.loads(self.stdin.getvalue().splitlines()[0])["id"]
                yield json.dumps({"v": 1, "id": rid, "event": "result", "result": {"ok": True, "result": "installed", "simulated": True}}) + "\n"
        return lines()
    def stopped(self): return self.quits or (self.killed and self.killable)
    def poll(self): return 0 if self.stopped() else None
    def wait(self, timeout=None):
        if not self.stopped(): raise subprocess.TimeoutExpired("scripted worker", timeout)
        return 0
    def kill(self): self.killed = True

class Recorder:
    """console_runs as the dispatcher sees it, keeping each run's completion so a test can deliver it again, late"""
    def __init__(self): self.completions = []
    def __getattr__(self, k): return getattr(console_runs, k)
    def start(self, cmd, args, spawn=None, on_done=None):
        def probe(ev, stopped=True):   # LOCK is free here: a non-reentrant lock held by this thread could not be taken
            got = console_runs.LOCK.acquire(timeout=2)
            if got: console_runs.LOCK.release()
            self.completions.append({"cmd": cmd, "fn": on_done, "ev": ev, "lock_free": got}); on_done(ev, stopped)
        return console_runs.start(cmd, args, spawn=spawn, on_done=probe)

class Lifecycle(TD.Base):
    def setUp(self):
        super().setUp(); self.rec = Recorder(); self.d.runs = self.rec; self.base_spawn = self.d.spawn
    def tearDown(self):
        console_runs.PROCS[:] = [p for p in console_runs.PROCS if not isinstance(p, Scripted)]; super().tearDown()
    def hold_next(self):
        """the next worker answers normally, then its stop (the pump's wait) is held until released"""
        ended, release = threading.Event(), threading.Event(); spawn = self.base_spawn
        def held():
            p = spawn(); wait = p.wait
            def slow(timeout=None):
                r = wait(timeout); ended.set(); release.wait(60); return r
            p.wait = slow; self.d.spawn = spawn; return p
        self.d.spawn = held; self.addCleanup(release.set); return ended, release
    def refused(self, op, body, code, why):
        c, r = self.d.post(op, body); self.assertEqual((c, r.get("code")), (code, why), (op, r)); return r

    def test_40_a_finished_change_holds_the_dispatcher_until_its_worker_has_stopped(self):
        h = self.plan(); self.run_op("inspect"); tok = self.confirm("install", plan_hash=h); other = self.confirm("update", plan_hash=h)
        ended, release = self.hold_next()
        code, r = self.d.post("install", {"plan_hash": h, "confirmation": tok}); self.assertEqual(code, 200, r)
        self.assertEqual(self.d.conf.live, {}); self.assertTrue(self.d.checked()["written"])   # counted at admission: every confirmation void
        self.assertTrue(ended.wait(60)); p = console_runs.poll(r["run"], 0)
        self.assertEqual(p["result"]["result"]["result"], "installed"); self.assertFalse(p["done"]); self.assertEqual(p["state"], "finishing")
        self.assertTrue(self.d.status_console()["busy"])
        for op, body in (("uninstall-review", {}), ("inspect", {}), ("verify", {"plan_hash": h}), ("confirm", {"operation": "update", "plan_hash": h}),
                         ("update", {"plan_hash": h, "confirmation": other})): self.refused(op, body, 409, "busy")
        release.set(); self.wait(r["run"]); self.assertFalse(self.d.status_console()["busy"])
        rv, _ = self.run_op("uninstall-review"); self.assertTrue(rv["ok"], rv)   # reads are fine after a change
        self.refused("confirm", {"operation": "uninstall", "review": rv["hash"]}, 409, "check-console")   # Codex's case: no new confirmation without a new check
        self.refused("update", {"plan_hash": h, "confirmation": other}, 403, "not-confirmed")   # made before the change: void
        self.assertEqual(boot_state(self.root)[:4], "ccs:")
        self.run_op("inspect"); rv, _ = self.run_op("uninstall-review"); tok = self.confirm("uninstall", review=rv["hash"])
        u, _ = self.run_op("uninstall", review=rv["hash"], confirmation=tok); self.assertTrue(u["ok"], u); self.assertEqual(boot_state(self.root), "nintendo")

    def test_41_a_check_replaces_the_old_one_when_it_starts_and_a_late_one_never_replaces_a_newer_one(self):
        h = self.plan(); _, first = self.run_op("inspect"); old = self.d.checked(); tok = self.confirm("install", plan_hash=h)
        ended, release = self.hold_next(); code, r = self.d.post("inspect", {}); self.assertEqual(code, 200, r)
        self.assertIsNone(self.d.checked()); self.assertEqual(self.d.conf.live, {})   # the old check is gone, with its confirmation
        self.assertTrue(ended.wait(60)); self.refused("inspect", {}, 409, "busy"); self.refused("confirm", {"operation": "install", "plan_hash": h}, 409, "busy")
        release.set(); self.wait(r["run"]); new = self.d.checked(); self.assertIsNotNone(new); self.assertNotEqual(new["id"], old["id"]); self.assertFalse(new["written"])
        self.refused("install", {"plan_hash": h, "confirmation": tok}, 403, "not-confirmed")
        late = self.rec.completions[0]; self.assertEqual(late["cmd"], "home-inspect"); late["fn"](first["result"], True)   # the first check's completion, delivered again
        self.assertEqual(self.d.checked()["id"], new["id"]); self.assertFalse(self.d.status_console()["busy"])
        tok = self.confirm("install", plan_hash=h); res, _ = self.run_op("install", plan_hash=h, confirmation=tok); self.assertEqual(res["result"], "installed", res)

    def test_42_a_late_change_completion_leaves_a_newer_check_and_operation_alone(self):
        h = self.plan(); self.run_op("inspect"); self.run_op("install", plan_hash=h, confirmation=self.confirm("install", plan_hash=h))
        late = [x for x in self.rec.completions if x["cmd"] == "home-execute"][0]
        self.run_op("inspect"); new = self.d.checked(); self.assertFalse(new["written"]); rv, _ = self.run_op("uninstall-review"); tok = self.confirm("uninstall", review=rv["hash"])
        ended, release = self.hold_next(); code, r = self.d.post("verify", {"plan_hash": h}); self.assertEqual(code, 200); self.assertTrue(ended.wait(60))
        late["fn"](late["ev"], True)   # the install's completion again, while a newer operation runs on a newer check
        self.assertTrue(self.d.status_console()["busy"]); self.assertFalse(self.d.checked()["written"]); self.assertEqual(len(self.d.conf.live), 1)
        release.set(); self.wait(r["run"])
        u, _ = self.run_op("uninstall", review=rv["hash"], confirmation=tok); self.assertTrue(u["ok"], u); self.assertEqual(boot_state(self.root), "nintendo")

    def test_43_no_worker_for_a_change_leaves_the_console_to_be_checked_again(self):
        h = self.plan(); self.run_op("inspect"); tok = self.confirm("install", plan_hash=h); before = tree(self.root)
        def broken(): raise OSError("no worker")
        self.d.spawn = broken; code, r = self.d.post("install", {"plan_hash": h, "confirmation": tok}); self.assertEqual(code, 200)
        p = self.wait(r["run"]); self.assertEqual(p["result"]["code"], "worker-spawn"); self.assertEqual(p["state"], "failed")
        self.assertFalse(self.d.status_console()["busy"]); self.assertTrue(self.d.checked()["written"]); self.assertEqual(changes(before, tree(self.root)), [])
        self.refused("confirm", {"operation": "install", "plan_hash": h}, 409, "check-console"); self.refused("install", {"plan_hash": h, "confirmation": tok}, 403, "not-confirmed")
        self.d.spawn = self.base_spawn; self.run_op("inspect"); self.confirm("install", plan_hash=h)

    def test_44_a_canceled_change_leaves_the_console_to_be_checked_again(self):
        big = make_package(os.path.join(self.t, "big"), tag="big", extra={f"roms/{i:02x}" + "cd" * 31 + ".nes": os.urandom(400_000) for i in range(60)})
        shutil.rmtree(self.pkg); shutil.copytree(big, self.pkg); h = self.plan(); self.run_op("inspect")
        code, r = self.d.post("install", {"plan_hash": h, "confirmation": self.confirm("install", plan_hash=h)}); self.assertEqual(code, 200)
        time.sleep(0.2); self.assertEqual(self.d.post("cancel", {"run": r["run"]})[0], 200)
        res = self.wait(r["run"])["result"]["result"]; self.assertEqual(res["result"], "cancelled", res); self.assertEqual(boot_state(self.root), "nintendo")
        self.assertTrue(self.d.checked()["written"]); self.refused("confirm", {"operation": "install", "plan_hash": h}, 409, "check-console")
        self.assertFalse(console_runs.cancel(r["run"])["ok"])   # nothing left to cancel
        self.run_op("inspect"); self.confirm("install", plan_hash=h)

    def test_45_no_answer_a_slow_stop_and_a_worker_that_will_not_stop(self):
        h = self.plan()
        for kind, proc, busy in (("no answer", Scripted(answer=False), False), ("slow to stop, ended", Scripted(quits=False), False), ("will not stop", Scripted(quits=False, killable=False), True)):
            with self.subTest(kind):
                self.d.spawn = self.base_spawn; self.run_op("inspect")
                tok = self.confirm("install", plan_hash=h); self.d.spawn = lambda proc=proc: proc
                code, r = self.d.post("install", {"plan_hash": h, "confirmation": tok}); self.assertEqual(code, 200); p = self.wait(r["run"])
                self.assertEqual(p["result"].get("code") if kind == "no answer" else p["result"]["result"]["result"], "worker-ended" if kind == "no answer" else "installed")
                self.assertEqual(proc.killed, kind != "no answer"); self.assertTrue(self.d.checked()["written"])
                self.assertEqual(self.d.status_console()["busy"], busy); self.refused("confirm", {"operation": "install", "plan_hash": h}, 409, "busy" if busy else "check-console")
        r = self.refused("inspect", {}, 409, "busy"); self.assertIn("did not stop", r["error"])   # held until the app restarts
        self.assertFalse(TD.console_home.Dispatcher().status_console()["busy"])

    def test_46_completions_never_run_under_the_runs_lock_and_polling_never_waits_on_them(self):
        h = self.plan(); self.run_op("inspect"); ended, release = self.hold_next()
        code, r = self.d.post("install", {"plan_hash": h, "confirmation": self.confirm("install", plan_hash=h)}); self.assertEqual(code, 200); self.assertTrue(ended.wait(60))
        stop = threading.Event(); seen = []
        def hammer():   # polls, status and refused requests from another thread while the completion is due (done is read first: it is set last)
            while not stop.is_set():
                dn = console_runs.poll(r["run"], 0)["done"]; seen.append((dn, self.d.status_console()["busy"], self.d.post("confirm", {"operation": "update", "plan_hash": h})))
        t = threading.Thread(target=hammer, daemon=True); t.start(); time.sleep(0.2); release.set(); self.wait(r["run"], timeout=20); time.sleep(0.1); stop.set(); t.join(10)
        self.assertFalse(t.is_alive()); self.assertTrue(all(x["lock_free"] for x in self.rec.completions), self.rec.completions)
        self.assertTrue([s for s in seen if not s[0]]); self.assertTrue([s for s in seen if s[0]])   # sampled on both sides of the completion
        self.assertFalse([s for s in seen if s[0] and s[1]], "done while still busy")
        self.assertEqual({(c, x.get("code")) for _, _, (c, x) in seen} - {(409, "busy"), (409, "check-console")}, set())

if __name__ == "__main__":
    try: unittest.main(verbosity=2)
    finally: shutil.rmtree(TD._T, ignore_errors=True)
