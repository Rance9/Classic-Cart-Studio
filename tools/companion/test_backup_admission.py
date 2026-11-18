"""The backup prerequisite for console maintenance writes (D-154), at the backend: console_home.Dispatcher asks for an admission before it
starts any write run (install, update, removal, recovery) and passes it to the run; the run's engine worker sends it to a real console's
worker ("admit") before the first write; backup_admission.admit asks the shipped console worker (--admit-backup), which runs the shared
validation (tools/console-backup/CcsBackup Engine/Admission.cs, tested with real backups in CcsBackup.Tests). Nothing here opens a device.
  python tools/companion/test_backup_admission.py        (CCS_WORKER_EXE: a built ccs-console-worker.exe, for the production admitter)"""
from __future__ import annotations
import json, os, shutil, sys, tempfile, unittest
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE); sys.path.insert(0, os.path.join(HERE, "..", "rom-pipeline"))
import console_test_guard   # noqa: E402,F401
import test_console_dispatch as TD   # noqa: E402
import backup_admission as BA, console_home   # noqa: E402
from console_engine.worker import Worker   # noqa: E402

class Dispatch(TD.Base):
    def refuse_then(self, answer):
        self.calls = []
        def admit(hwid, op, subject): self.calls.append((hwid, op, subject)); return answer(len(self.calls))
        self.d.admit = admit

    def checked_and_confirmed(self):
        h = self.plan(); self.run_op("inspect"); return h, self.confirm("install", plan_hash=h)

    def test_install_refused_without_backup_nothing_starts(self):
        plan, conf = self.checked_and_confirmed(); spawned = self.spawned
        self.refuse_then(lambda n: {"ok": False, "code": "backup-required", "text": "Back up this console first."})
        st, b = self.d.post("install", {"plan_hash": plan, "confirmation": conf})
        self.assertEqual((st, b["code"]), (409, "backup-required"), b); self.assertEqual(self.spawned, spawned, "no worker started")
        self.assertEqual(self.calls[0][1:], ("install", plan)); self.assertTrue(self.calls[0][0])
        self.assertFalse(self.d.checked()["written"], "nothing counts as changed")
        # once a backup exists the same (unused) confirmation works
        self.refuse_then(lambda n: {"ok": True, "ticket": "1" * 32})
        st, b = self.d.post("install", {"plan_hash": plan, "confirmation": conf}); self.assertEqual(st, 200, b)
        self.wait(b["run"])

    def test_admission_reaches_the_run(self):
        plan, conf = self.checked_and_confirmed(); seen = {}
        orig = TD.console_runs.start
        def start(cmd, args, **kw): seen.update(cmd=cmd, args=args); return orig(cmd, args, **kw)
        TD.console_runs.start = start
        try:
            self.refuse_then(lambda n: {"ok": True, "ticket": "2" * 32})
            st, b = self.d.post("install", {"plan_hash": plan, "confirmation": conf}); self.assertEqual(st, 200, b); self.wait(b["run"])
        finally: TD.console_runs.start = orig
        self.assertEqual(seen["args"]["admission"], {"ticket": "2" * 32, "operation": "install", "subject": plan})

    def test_removal_that_deletes_data_is_its_own_operation(self):
        self.run_op("inspect")
        self.refuse_then(lambda n: {"ok": False, "code": "current-backup-required", "text": "Back up the console again now."})
        st, b = self.d.post("uninstall", {"review": "a" * 64, "keep_data": False, "confirmation": "x"})
        self.assertEqual((st, b["code"]), (409, "current-backup-required"), b); self.assertEqual(self.calls[0][1], "uninstall-data")
        st, b = self.d.post("uninstall", {"review": "a" * 64, "confirmation": "x"}); self.assertEqual(self.calls[1][1], "uninstall")

    def test_a_new_check_during_the_backup_check_voids_the_attempt(self):
        plan, conf = self.checked_and_confirmed()
        def admit(hwid, op, subject):
            self.d.console = dict(self.d.console, id="another-check")   # the console was checked again meanwhile
            return {"ok": True, "ticket": "3" * 32}
        self.d.admit = admit
        st, b = self.d.post("install", {"plan_hash": plan, "confirmation": conf}); self.assertEqual((st, b["code"]), (409, "check-console"), b)

    def test_the_apps_own_dispatcher_uses_the_shared_validation(self):
        self.assertIs(console_home.DISPATCH.admit, BA.admit)

class WorkerSide(unittest.TestCase):
    """the engine worker sends "admit" before the body of a write session on a real console; a refusal ends it before any change"""
    class Real:
        simulated = False; kind = "worker"
        def __init__(self, refuse): self.calls = []; self.refuse = refuse
        def call(self, cmd, args=None, raw=False):
            self.calls.append((cmd, args))
            if cmd == "admit" and self.refuse: raise OSError("backup-required: Back up this console first.")
            return {"admitted": True}
        def close(self): pass
    def run_session(self, refuse, admission):
        t = tempfile.mkdtemp(prefix="ccs-admw-"); con = self.Real(refuse); ran = []
        try:
            w = Worker(root=t, console_factory=lambda tier, pkg: con, allowed=lambda tier: True)
            try: w._run_session("install", "install", "install", lambda c: ran.append(1) or {"ok": True}, admission=admission)
            except OSError as e: return con, ran, str(e)
            return con, ran, None
        finally: shutil.rmtree(t, ignore_errors=True)
    def test_admit_first_then_the_body(self):
        con, ran, err = self.run_session(False, {"ticket": "4" * 32, "operation": "install", "subject": "p"})
        self.assertIsNone(err); self.assertEqual(con.calls[0], ("admit", {"ticket": "4" * 32, "operation": "install", "subject": "p"})); self.assertEqual(ran, [1])
    def test_refusal_stops_before_the_body(self):
        con, ran, err = self.run_session(True, {"ticket": "4" * 32, "operation": "install", "subject": "p"})
        self.assertIn("backup-required", err); self.assertEqual(ran, [])
    def test_no_admission_is_an_empty_ticket_the_worker_refuses(self):
        con, ran, err = self.run_session(True, None)
        self.assertEqual(con.calls[0][1]["ticket"], ""); self.assertEqual(ran, [])

class ProductionAdmitter(unittest.TestCase):
    """backup_admission.admit with the built console worker (its --admit-backup mode and the shared validation)"""
    exe = os.environ.get("CCS_WORKER_EXE")
    def setUp(self):
        if not self.exe or not os.path.isfile(self.exe): self.skipTest("CCS_WORKER_EXE is not a built ccs-console-worker.exe")
        self.t = tempfile.mkdtemp(prefix="ccs-admp-"); self.settings = os.path.join(self.t, "settings.json")
        json.dump({"schema": "ccs-console-backup-settings/1", "primary_folder": os.path.join(self.t, "backups"), "archive_folder": ""}, open(self.settings, "w"))
    def tearDown(self): shutil.rmtree(self.t, ignore_errors=True)
    def test_no_backup_refused(self):
        r = BA.admit("0461aaaa881800001e6700006c118000", "install", "p", exe=self.exe, settings=self.settings)
        self.assertEqual((r["ok"], r["code"]), (False, "backup-required"), r); self.assertIn("Back up this console first", r["text"])
    def test_bad_identity_and_operation_refused_before_running(self):
        self.assertEqual(BA.admit("not a console", "install", "p", exe=self.exe)["code"], "no-console")
        self.assertEqual(BA.admit("0461aaaa881800001e6700006c118000", "format", "p", exe=self.exe)["code"], "bad-operation")
    def test_missing_worker_refused(self):
        self.assertEqual(BA.admit("0461aaaa881800001e6700006c118000", "install", "p", exe=os.path.join(self.t, "absent.exe"))["code"], "backup-required")

if __name__ == "__main__":
    r = unittest.main(exit=False, verbosity=2).result
    print(f"RESULT {r.testsRun - len(r.failures) - len(r.errors) - len(r.skipped)}/{r.testsRun}" + (f" ({len(r.skipped)} skipped)" if r.skipped else ""))
    sys.exit(0 if r.wasSuccessful() and not r.skipped else 1 if not r.wasSuccessful() else 2)
