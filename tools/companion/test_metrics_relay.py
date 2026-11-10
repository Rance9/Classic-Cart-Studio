"""Optional usage counts in Classic Cart Studio for Windows (D-123): metrics_relay.py. The choice is Off until chosen, kept on this computer
only, and in no backup or restore; only the desktop page may use it; a count leaves only when the choice is On, only with the fixed
fields (the page cannot change the surface, version or families), only to the one collector address, never more than two at a time and
never retried; a failed or slow send changes nothing. The sender is replaced from Python (no network here).   python test_metrics_relay.py"""
import ast, json, os, shutil, sys, tempfile, threading, time, unittest
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import metrics_relay as mr
def rd(p):
    with open(p, encoding="utf-8") as f: return f.read()
def wr(p, t):
    with open(p, "w", encoding="utf-8") as f: f.write(t)

class H:   # the backend's request handler, as far as the relay uses it
    def __init__(self, desktop=True): self.desktop = desktop; self.out = None
    def _bearer(self): return {"desktop": True} if self.desktop else None
    def _json(self, code, obj): self.out = (code, obj); return obj

class T(unittest.TestCase):
    def setUp(self):
        self.d = tempfile.mkdtemp(prefix="ccs-usage-"); self.dp = lambda *a: os.path.join(self.d, *a); self.sent = []; self.gate = None
        def fake(d):
            if self.gate: self.gate.wait(5)
            self.sent.append(d)
        self._orig = mr._send; mr._send = fake; mr._inflight = 0; mr._held_off = False   # (k3f7) no Off held over from another test
    def tearDown(self): mr._send = self._orig; shutil.rmtree(self.d, ignore_errors=True)
    def post(self, path, body, desktop=True): h = H(desktop); mr.handle(h, "POST", path, body, self.dp, "0.8.0"); return h.out
    def get(self, path, desktop=True): h = H(desktop); mr.handle(h, "GET", path, None, self.dp, "0.8.0"); return h.out
    def wait_sent(self, n, t=3.0):
        end = time.time() + t
        while time.time() < end and len(self.sent) < n: time.sleep(0.02)
        time.sleep(0.1); return self.sent

    def test_01_off_until_chosen_and_nothing_leaves(self):
        self.assertEqual(self.get("/api/metrics/choice"), (200, {"ok": True, "choice": "off", "chosen": False, "source": None, "held": False}))
        self.assertEqual(self.post("/api/metrics/event", {"schema": 1, "event": "studio_opened"}), (200, {"ok": True, "sent": False}))
        self.assertEqual(self.wait_sent(1, 0.5), [])
    def test_02_only_the_desktop_page(self):
        for path in ("/api/metrics/choice", "/api/metrics/event"):
            self.assertEqual(self.post(path, {"choice": "on"}, desktop=False)[0], 403); self.assertEqual(self.get(path, desktop=False)[0], 403)
        self.assertFalse(os.path.exists(self.dp(mr.CHOICE_FILE)))
    def test_03_on_sends_exactly_the_fixed_fields(self):
        self.assertEqual(self.post("/api/metrics/choice", {"choice": "on"})[1]["choice"], "on")
        r = self.post("/api/metrics/event", {"schema": 1, "event": "play_started", "app_version": "9.9.9", "surface": "web", "browser": "firefox", "os": "linux"})
        self.assertEqual(r, (200, {"ok": True, "sent": True}))
        self.assertEqual(self.wait_sent(1), [{"schema": 1, "event": "play_started", "app_version": "0.8.0", "surface": "windows", "browser": "edge", "os": "windows"}])
    def test_04_anything_off_the_lists_is_refused(self):
        self.post("/api/metrics/choice", {"choice": "on"})
        for body in ({"schema": 1, "event": "play_started", "rom": "x"}, {"schema": 1, "event": "a game title"}, {"schema": 2, "event": "play_started"},
                     {"schema": 1, "event": "operation_failed", "operation": "restore", "reason": "C:/private"}, {"schema": 1, "event": "play_started", "reason": "other"},
                     {"schema": 1, "event": "home_opened", "elapsed": "forever"}, ["play_started"], "play_started"):
            self.assertEqual(self.post("/api/metrics/event", body)[0], 400, body)
        self.assertEqual(self.post("/api/metrics/event", {"schema": 1, "event": "operation_failed", "operation": "restore", "reason": "read_failed", "elapsed": "1_to_5s"})[0], 200)
        self.assertEqual(self.wait_sent(1), [{"schema": 1, "event": "operation_failed", "app_version": "0.8.0", "surface": "windows", "browser": "edge", "os": "windows", "operation": "restore", "reason": "read_failed", "elapsed": "1_to_5s"}])
    def test_05_never_more_than_two_at_a_time_no_backlog(self):
        self.post("/api/metrics/choice", {"choice": "on"}); self.gate = threading.Event()
        res = [self.post("/api/metrics/event", {"schema": 1, "event": "home_opened"})[1]["sent"] for _ in range(5)]
        self.assertEqual(res, [True, True, False, False, False]); self.gate.set()
        self.assertEqual(len(self.wait_sent(2)), 2); time.sleep(0.3); self.assertEqual(len(self.sent), 2, "the dropped ones are not replayed")
    def test_06_a_failed_send_changes_nothing(self):
        self.post("/api/metrics/choice", {"choice": "on"})
        def boom(d): raise OSError("offline")
        mr._send = boom
        self.assertEqual(self.post("/api/metrics/event", {"schema": 1, "event": "studio_opened"}), (200, {"ok": True, "sent": True}))
        time.sleep(0.3); self.assertEqual(mr._inflight, 0)
    def test_07_off_again_stops_sends(self):
        self.post("/api/metrics/choice", {"choice": "on"}); self.post("/api/metrics/choice", {"choice": "off"})
        self.assertEqual(self.get("/api/metrics/choice")[1], {"ok": True, "choice": "off", "chosen": True, "source": "app", "held": False})
        self.assertEqual(self.post("/api/metrics/event", {"schema": 1, "event": "studio_opened"})[1]["sent"], False); self.assertEqual(self.wait_sent(1, 0.4), [])
    def test_08_a_bad_choice_is_refused_and_a_damaged_file_means_off(self):
        self.assertEqual(self.post("/api/metrics/choice", {"choice": "yes"})[0], 400)
        wr(self.dp(mr.CHOICE_FILE), "{not json"); self.assertEqual(mr.load_choice(self.dp), {"choice": "off", "chosen": False, "source": None})
        wr(self.dp(mr.CHOICE_FILE), json.dumps({"schema": "other", "choice": "on"})); self.assertEqual(mr.load_choice(self.dp)["choice"], "off")
    def test_09_the_one_collector_address(self):
        self.assertEqual(mr.COLLECTOR, "https://metrics.classiccartstudio.com/v1/events")
        src = rd(os.path.join(HERE, "metrics_relay.py"))
        self.assertEqual(src.count("https://"), 1, "no other remote address in the relay")
    def test_10_the_choice_is_in_no_backup_and_no_restore(self):
        import studio_backup as sb
        os.makedirs(self.dp("saves"), exist_ok=True); mr.save_choice(self.dp, "on")
        fields, items = sb.app_parts(self.dp, [])
        self.assertNotIn("usage", json.dumps(fields, default=str)); self.assertFalse(any("usage" in str(getattr(i, "path", i)) for i in items))
        tree = ast.parse(rd(os.path.join(HERE, "studio_backup.py")))
        names = {a.value for n in ast.walk(tree) if isinstance(n, ast.Call) and getattr(n.func, "id", "") == "data_path" for a in n.args if isinstance(a, ast.Constant)}
        self.assertNotIn(mr.CHOICE_FILE, names, "a restore never writes the choice")
        dt = rd(os.path.join(HERE, "nes_companion.py"))
        self.assertNotIn(mr.CHOICE_FILE, dt, "the page's synced state (desktop-state.json) never carries it")

    def test_11_the_setup_wizards_choice_is_the_choice(self):
        """the setup wizard writes {"schema":"ccs-usage-choice/1","choice":...,"source":"installer"} before the first launch: other keys are
        ignored; it is shown as chosen when installing; the app's own later choice replaces it"""
        for v in ("on", "off"):
            wr(self.dp(mr.CHOICE_FILE), json.dumps({"schema": "ccs-usage-choice/1", "choice": v, "source": "installer", "version": "0.8.0", "extra": {"x": 1}}))
            self.assertEqual(self.get("/api/metrics/choice")[1], {"ok": True, "choice": v, "chosen": True, "source": "installer", "held": False})
            self.assertEqual(self.post("/api/metrics/event", {"schema": 1, "event": "studio_opened"})[1]["sent"], v == "on")
            if v == "on": self.wait_sent(1)   # (k3f7) sent before the next file: a count whose send begins after Off is dropped (test_13)
        self.wait_sent(1); self.assertEqual(len(self.sent), 1, "only the On file sent")
        self.post("/api/metrics/choice", {"choice": "off"}); self.assertEqual(json.loads(rd(self.dp(mr.CHOICE_FILE))), {"schema": "ccs-usage-choice/1", "choice": "off", "source": "app"})
    def test_12_no_valid_decision_never_enables_reporting(self):
        bad = ['', '{}', '[]', 'null', '"on"', json.dumps({"choice": "on"}), json.dumps({"schema": "ccs-usage-choice/1", "choice": "On"}), json.dumps({"schema": "ccs-usage-choice/1", "choice": "yes"}),
               json.dumps({"schema": "ccs-usage-choice/2", "choice": "on"}), json.dumps({"schema": "ccs-usage-choice/1", "choice": True}), json.dumps({"schema": "ccs-usage-choice/1", "choice": ["on"]})]
        for t in bad:
            wr(self.dp(mr.CHOICE_FILE), t)
            self.assertEqual(mr.load_choice(self.dp), {"choice": "off", "chosen": False, "source": None}, t)
            self.assertEqual(self.post("/api/metrics/event", {"schema": 1, "event": "studio_opened"})[1]["sent"], False, t)
        with open(self.dp(mr.CHOICE_FILE), "wb") as f: f.write(b"\xff\xfe\x00garbage")
        self.assertEqual(mr.load_choice(self.dp)["choice"], "off"); os.remove(self.dp(mr.CHOICE_FILE))
        self.assertEqual(mr.load_choice(self.dp)["choice"], "off", "no file: Off"); self.assertEqual(self.wait_sent(1, 0.4), [])

    # (k3f7, Codex K3F-F1/F2) Off reaches counts already admitted; a choice that cannot be kept is honest and Off stays in force
    def deferred(self):   # sending threads held before they run, as Codex's native_pending_probe holds them
        held = []
        class Later:
            def __init__(self, target, **k): self.target = target
            def start(self): held.append(self.target)
        orig = mr.threading.Thread; mr.threading.Thread = Later; self.addCleanup(setattr, mr.threading, "Thread", orig); return held
    def refuse_writes(self):
        orig = mr.os.replace
        def no(*a): raise PermissionError("synthetic: the data folder refuses the write")
        mr.os.replace = no; self.addCleanup(setattr, mr.os, "replace", orig)
    def test_13_off_reaches_a_count_already_admitted(self):
        self.post("/api/metrics/choice", {"choice": "on"}); held = self.deferred()
        self.assertTrue(self.post("/api/metrics/event", {"schema": 1, "event": "play_started"})[1]["sent"])   # admitted under On
        self.post("/api/metrics/choice", {"choice": "off"})
        for run in held: run()
        self.assertEqual(self.sent, [], "admitted before Off, never sent after it"); self.assertEqual(mr._inflight, 0)
    def test_14_any_decision_after_admission_voids_the_count(self):   # On again in between: still a new decision; the count was admitted under the old one
        self.post("/api/metrics/choice", {"choice": "on"}); held = self.deferred()
        self.post("/api/metrics/event", {"schema": 1, "event": "studio_opened"}); self.post("/api/metrics/choice", {"choice": "off"}); self.post("/api/metrics/choice", {"choice": "on"})
        for run in held: run()
        self.assertEqual(self.sent, [])
        held2 = self.deferred(); self.post("/api/metrics/event", {"schema": 1, "event": "studio_opened"})
        for run in held2: run()
        self.assertEqual(len(self.sent), 1, "a count admitted under the current On is sent")
    def test_15_a_refused_off_is_held_and_says_so(self):
        self.post("/api/metrics/choice", {"choice": "on"}); held = self.deferred()
        self.post("/api/metrics/event", {"schema": 1, "event": "play_started"})   # admitted while On
        self.refuse_writes(); code, r = self.post("/api/metrics/choice", {"choice": "off"})
        self.assertEqual((code, r["ok"], r["choice"], r["held"]), (500, False, "off", True), r)   # not kept, but in force
        self.assertEqual(mr.load_choice(self.dp)["choice"], "on", "the file still says On")
        for run in held: run()
        self.assertEqual(self.post("/api/metrics/event", {"schema": 1, "event": "studio_opened"})[1]["sent"], False)
        self.assertEqual(self.get("/api/metrics/choice")[1], {"ok": True, "choice": "off", "chosen": True, "source": "app", "held": True})
        self.assertEqual(self.wait_sent(1, 0.4), [], "nothing after Off, kept or not")
    def test_16_a_refused_on_changes_nothing_and_a_kept_choice_ends_the_hold(self):
        self.refuse_writes(); code, r = self.post("/api/metrics/choice", {"choice": "on"})
        self.assertEqual((code, r["ok"], r["choice"]), (500, False, "off"), r)
        self.assertEqual(self.post("/api/metrics/event", {"schema": 1, "event": "studio_opened"})[1]["sent"], False)
        code, r = self.post("/api/metrics/choice", {"choice": "off"}); self.assertEqual((code, r["held"]), (500, True))   # Off asked, not kept: held
        self.doCleanups()   # writes work again
        self.assertEqual(self.post("/api/metrics/choice", {"choice": "on"})[1], {"ok": True, "choice": "on", "chosen": True, "source": "app", "held": False})
        self.assertTrue(self.post("/api/metrics/event", {"schema": 1, "event": "studio_opened"})[1]["sent"]); self.assertEqual(len(self.wait_sent(1)), 1)

if __name__ == "__main__":
    prog = unittest.main(exit=False, verbosity=2); r = prog.result
    print(f"RESULT {r.testsRun - len(r.failures) - len(r.errors)} / {r.testsRun}"); sys.exit(0 if r.wasSuccessful() else 1)
