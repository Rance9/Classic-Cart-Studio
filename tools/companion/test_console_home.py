"""Classic Cart Studio's HOME on the console (K3) through the worker and the backend, PC only: the worker's HOME commands on fresh stand-ins
(every rehearsal scenario, removal and the saves copy, real progress, a cancel at a safe point, obsolete plans, the PC-side records, every
physical step refused), the backend's /api/console/home/ routes (desktop principal only; the page names no path, shell text, transport or
fault; physical operations and confirmations refused by the gate in code), and the backend-owned confirmation (bound to one operation,
plan and console, single use, expiring; made only when the gate allows the tier, so never today). No device, socket to a console or shell.
  python tools/companion/test_console_home.py"""
from __future__ import annotations
import hashlib, io, json, os, secrets, shutil, subprocess, sys, tempfile, time, unittest, urllib.error, urllib.request
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path[:0] = [HERE, os.path.join(HERE, "..", "rom-pipeline")]
import console_test_guard   # noqa: E402,F401  (first: the controlled console boundary, k3e)
from console_engine import gate, ccs_install as CI   # noqa: E402
from console_engine.worker import Worker   # noqa: E402
import console_confirm as CC   # noqa: E402
from test_ccs_install import make_package, rd, wr   # noqa: E402

def ask(w: Worker, cmd: str, args: dict | None = None) -> tuple[dict, list]:
    out = io.StringIO(); w.out = out; w.handle(json.dumps({"v": 1, "id": "t", "cmd": cmd, "args": args or {}}))
    lines = [json.loads(l) for l in out.getvalue().splitlines() if l.strip()]; return lines[-1], lines[:-1]

class WorkerHome(unittest.TestCase):
    def setUp(self):
        self.t = tempfile.mkdtemp(prefix="ccs-home-w-"); self.pkg = make_package(os.path.join(self.t, "staged"))
        self.w = Worker(os.path.join(self.t, "root"), console_package=self.pkg)
        r, _ = ask(self.w, "home-plan"); self.assertEqual(r["event"], "result", r); self.plan = r["result"]
    def tearDown(self): shutil.rmtree(self.t, ignore_errors=True)
    def sim(self, **a): return ask(self.w, "home-simulate", {"plan_hash": self.plan["hash"], "profile": "hakchi-ready", **a})
    def test_01_the_plan_is_the_staged_package_and_takes_no_argument(self):
        r, _ = ask(self.w, "home-plan", {"folder": "C:/elsewhere"}); self.assertEqual(r["event"], "error")
        self.assertEqual(self.plan["schema"], CI.PLAN_SCHEMA); self.assertNotIn("files", self.plan); self.assertGreater(self.plan["file_count"], 3)
        rec, _ = ask(self.w, "home-records"); self.assertTrue(rec["result"]["last_plan_current"]); self.assertEqual(rec["result"]["last_plan"]["plan"]["hash"], self.plan["hash"])
    def test_02_preflight_on_each_simulated_console(self):
        exp = {"hakchi-ready": (True, ""), "stock": (False, "hakchi must be installed first"), "nearly-full": (False, "not enough room"), "other-module": (False, "another hakchi module")}
        for prof, (ok, words) in exp.items():
            r, _ = ask(self.w, "home-preflight", {"plan_hash": self.plan["hash"], "profile": prof}); res = r["result"]
            self.assertEqual(res["ok"], ok, (prof, res)); self.assertTrue(res["simulated"])
            if words: self.assertIn(words, res["why"])
        r, _ = ask(self.w, "home-preflight", {"plan_hash": self.plan["hash"], "profile": "../real"}); self.assertEqual(r["code"], "not-found")
    def test_03_every_rehearsal_scenario(self):
        r, ev = self.sim(scenario="first-install"); i = r["result"]["install"]; self.assertEqual(i["result"], "installed", i); self.assertTrue(r["result"]["stock_unchanged"])
        st = [e for e in ev if e.get("event") == "progress" and e.get("stage") == "stage"]; self.assertGreater(len(st), 3); self.assertEqual(st[-1]["done"], st[-1]["total"])
        self.assertTrue(all(e.get("simulated") for e in ev)); self.assertIn("readback", [e.get("stage") for e in ev])
        r, _ = self.sim(scenario="repeat"); self.assertEqual(r["result"]["install"]["result"], "no-change")
        r, _ = self.sim(scenario="update"); i = r["result"]["install"]; self.assertEqual(i["result"], "installed"); self.assertTrue(i["tidy"] and i["tidy"][0]["removed"], i["tidy"])
        r, _ = self.sim(scenario="repair"); i = r["result"]["install"]; self.assertEqual(i["result"], "installed"); self.assertTrue(any(k.endswith("/session.json") for k in i["kept_changed_or_unknown"]))
        r, _ = self.sim(scenario="interrupted"); x = r["result"]; self.assertFalse(x["install"]["ok"]); self.assertTrue(x["install"]["rolled_back"]["ok"]); self.assertEqual(x["after_recovery"], "installed")
        r, _ = self.sim(scenario="first-install", then=["uninstall", "export"]); x = r["result"]
        self.assertTrue(x["uninstall"]["ok"]); self.assertTrue(x["stock_unchanged_after_uninstall"]); self.assertTrue(x["export"]["verified"]); self.assertEqual(x["export"]["files"], 2)
        self.assertTrue(any("start-up module" in s for s in x["uninstall_plan"]["remove"]))
        self.assertEqual(os.listdir(os.path.join(self.t, "root", "stand-ins")), [], "every stand-in is removed afterwards")
    def test_04_bad_scenarios_and_faults_are_refused(self):
        for a in ({"scenario": "real-console"}, {"then": ["format"]}, {"fault": {"disconnect_at": 3}}, {"fault": {"before": True}}, {"fault": "x"}):
            r, _ = self.sim(**a); self.assertEqual(r["event"], "error", a)
    def test_05_a_plan_for_a_package_that_is_no_longer_staged_is_obsolete(self):
        shutil.rmtree(self.pkg); make_package(self.pkg, "changed")
        for cmd in ("home-preflight", "home-simulate"):
            r, _ = ask(self.w, cmd, {"plan_hash": self.plan["hash"], "profile": "hakchi-ready"}); self.assertEqual(r.get("code"), "plan-obsolete", (cmd, r))
        # k3c: a physical step is refused by the gate before its arguments are read (the obsolete plan of a physical install: test_console_dispatch)
        r, _ = ask(self.w, "home-execute", {"plan_hash": self.plan["hash"], "profile": "hakchi-ready"}); self.assertEqual(r.get("code"), "physical-disabled", r)
        rec, _ = ask(self.w, "home-records"); self.assertFalse(rec["result"]["last_plan_current"])
    def test_06_records_keep_each_rehearsal(self):
        self.sim(scenario="first-install"); self.sim(scenario="interrupted")
        rec, _ = ask(self.w, "home-records"); ops = rec["result"]["operations"]; self.assertEqual(len(ops), 2)
        for o in ops:
            self.assertEqual(o["schema"], "ccs-console-operation/1"); self.assertTrue(o["simulated"]); self.assertEqual(o["plan"], self.plan["hash"]); self.assertTrue(o["journal"].endswith(".jsonl"))
            self.assertNotIn("token", json.dumps(o).lower()); self.assertTrue(os.path.isfile(os.path.join(self.t, "root", "journals", o["journal"])))
        self.assertEqual({o["outcome"] for o in ops}, {"installed", "stopped"})
    def test_07_every_physical_step_is_refused_whatever_the_client_adds(self):
        extra = {"transport": "ssh", "host": "169.254.13.37", "force": True, "gate": "open", "token": "x", "physical": True}
        for cmd in ("home-inspect", "home-execute", "home-uninstall", "home-recover", "home-export-console"):
            r, _ = ask(self.w, cmd, {"plan_hash": self.plan["hash"], **extra}); self.assertEqual(r.get("code"), "physical-disabled", (cmd, r))
        self.assertFalse(gate.PHYSICAL_WRITE_ENABLED or gate.PHYSICAL_INSPECT_ENABLED)
    def test_08_a_cancel_reaches_a_running_rehearsal_and_stops_it_before_the_switch(self):
        big = make_package(os.path.join(self.t, "big"), extra={f"home/covers/c{i:04d}.png": os.urandom(4000) for i in range(500)})
        root = os.path.join(self.t, "root2"); env = dict(os.environ, PYTHONPATH=HERE + os.pathsep + os.environ.get("PYTHONPATH", ""))
        p = subprocess.Popen([sys.executable, "-B", "-m", "console_engine.worker", "--root", root, "--console-package", big], cwd=HERE, env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, encoding="utf-8")
        try:
            def send(o): p.stdin.write(json.dumps({"v": 1, **o}) + "\n"); p.stdin.flush()
            def line(): return json.loads(p.stdout.readline())
            send({"id": "p", "cmd": "home-plan", "args": {}}); h = line()["result"]["hash"]
            send({"id": "s", "cmd": "home-simulate", "args": {"plan_hash": h, "profile": "hakchi-ready"}})
            while True:
                e = line()
                if e.get("stage") == "stage" and e.get("done", 0) >= 5: break
            send({"id": "c", "cmd": "cancel-if-safe", "args": {}}); res = None; acc = None
            while res is None:
                e = line()
                if e.get("id") == "c": acc = e["result"]
                elif e.get("id") == "s" and e.get("event") in ("result", "error"): res = e
            self.assertTrue(acc and acc["accepted"], acc); i = res["result"]["install"]
            self.assertEqual(i["result"], "cancelled", i); self.assertTrue(i["rolled_back"]["ok"]); self.assertTrue(res["result"]["stock_unchanged"])
            send({"id": "c2", "cmd": "cancel-if-safe", "args": {}}); self.assertFalse(line()["result"]["accepted"], "nothing is running")
        finally:
            try: p.stdin.write("quit\n"); p.stdin.close()
            except OSError: pass
            p.wait(30); p.stdout.close()

class Confirmation(unittest.TestCase):
    def test_10_the_backend_makes_none_while_the_gate_is_closed(self):
        for op in CC.OPS:
            with self.assertRaises(gate.GateClosed): CC.BACKEND.mint(op, "a" * 64, "sim-0001")
            with self.assertRaises(gate.GateClosed): CC.BACKEND.consume("anything", op, "a" * 64, "sim-0001")
        self.assertIs(CC.BACKEND.allowed, gate.allowed, "the backend's confirmations ask the gate in code")
    def test_11_a_confirmation_covers_exactly_one_operation_plan_and_console_once(self):
        now = [1000.0]; c = CC.Confirmations(allowed=lambda tier: True, clock=lambda: now[0]); P, Q, D = "a" * 64, "b" * 64, "hw-0001"
        t = c.mint("install", P, D)["token"]; c.consume(t, "install", P, D)
        with self.assertRaises(CC.ConfirmationRefused): c.consume(t, "install", P, D)   # used up
        for op, plan, dev in (("install", Q, D), ("install", P, "hw-0002"), ("uninstall", P, D)):
            t = c.mint("install", P, D)["token"]
            with self.assertRaises(CC.ConfirmationRefused, msg=(op, plan, dev)): c.consume(t, op, plan, dev)
        t = c.mint("install", P, D)["token"]; now[0] += CC.TTL + 1
        with self.assertRaises(CC.ConfirmationRefused): c.consume(t, "install", P, D)
        t = c.mint("install", P, D)["token"]; self.assertEqual(c.invalidate(plan_hash=P), 1)
        with self.assertRaises(CC.ConfirmationRefused): c.consume(t, "install", P, D)
        for bad in (("install", "short", D), ("install", P, "a b"), ("format", P, D)):
            with self.assertRaises(CC.ConfirmationRefused): c.mint(*bad)
        self.assertNotIn(t, json.dumps(list(c.live)), "only a hash of a confirmation is kept")

PORT = 8773; B = f"http://127.0.0.1:{PORT}"; SITE = "https://classiccartstudio.com"
def req(method, path, body=None, headers=None):
    data = json.dumps(body).encode() if body is not None else None
    r = urllib.request.Request(B + path, data=data, method=method, headers={"Content-Type": "application/json", "X-NES-Protocol": "1", **(headers or {})})
    try:
        with urllib.request.urlopen(r, timeout=300) as resp: return resp.status, json.loads(resp.read() or b"{}")
    except urllib.error.HTTPError as e:
        try: return e.code, json.loads(e.read() or b"{}")
        except Exception: return e.code, {}
    except Exception as e: return 0, {"error": str(e)}

class Routes(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.t = tempfile.mkdtemp(prefix="ccs-home-api-"); cls.nh = os.path.join(cls.t, "native-home"); cls.pkg = make_package(os.path.join(cls.nh, "console", "current"))
        cfgp = os.path.join(cls.t, "companion.json"); wr(cfgp, json.dumps({"hakchi_exe": os.path.join(cls.t, "no-hakchi", "hakchi.exe"), "port": PORT, "no_launch": True, "exports_dir": os.path.join(cls.t, "exports"), "staging_dir": os.path.join(cls.t, "staging")}))
        cls.token = secrets.token_hex(32); env = dict(os.environ, NES_COMPANION_CONFIG=cfgp, NES_PERSONAL_DIR=os.path.join(cls.t, "personal"), NES_STUDIO_DATA=cls.t, CCS_NATIVE_HOME_ROOT=cls.nh,
                                                       CCS_HOST_TOKEN=cls.token, CCS_HOST_PID=str(os.getpid()), PYTHONIOENCODING="utf-8", CCS_PHYSICAL_WRITE="1", CCS_GATE="open")
        cls.log = open(os.path.join(cls.t, "companion.log"), "w", encoding="utf-8")
        cls.proc = subprocess.Popen([sys.executable, "-u", "-B", os.path.join(HERE, "companion_app.py"), "--headless", "--port", str(PORT)], env=env, stdout=cls.log, stderr=subprocess.STDOUT)
        for _ in range(200):
            try:
                with urllib.request.urlopen(B + "/api/ping", timeout=1): break
            except Exception: time.sleep(0.25)
        cls.desk = {"Origin": SITE, "Authorization": "Bearer " + cls.token}
    @classmethod
    def tearDownClass(cls):
        cls.proc.terminate()
        try: cls.proc.wait(20)
        except subprocess.TimeoutExpired: cls.proc.kill(); cls.proc.wait(10)
        cls.log.close(); shutil.rmtree(cls.t, ignore_errors=True)
    def test_20_only_the_desktop_principal_reaches_the_home_routes(self):
        for hdr in ({"Origin": SITE}, {}, {"Origin": "https://evil.example", "Authorization": "Bearer " + self.token}):
            self.assertIn(req("GET", "/api/console/home/status", headers=hdr)[0], (401, 403), hdr)
            for op in ("plan", "rehearse", "confirm", "install"): self.assertIn(req("POST", f"/api/console/home/{op}", {}, headers=hdr)[0], (401, 403), (op, hdr))
    def test_21_status_plan_preflight_rehearsal_and_records(self):
        s, j = req("GET", "/api/console/home/status", headers=self.desk); self.assertEqual(s, 200, j); self.assertEqual(j["staged"]["layout"], "install")
        ops = {o["id"]: o for o in j["operations"]}; self.assertTrue(ops["rehearse"]["available"]); self.assertTrue(all(not ops[k]["available"] for k in ("inspect", "install", "recover", "export", "ram-test", "first-setup")))
        self.assertFalse(j["gate"]["install"]); self.assertEqual({p["name"] for p in j["profiles"]}, set(CI.HOME_PROFILES))
        s, j = req("POST", "/api/console/home/plan", {"folder": "C:/elsewhere"}, headers=self.desk); self.assertEqual(s, 200, j); h = j["result"]["hash"]
        s, j = req("POST", "/api/console/home/preflight", {"plan_hash": h, "profile": "hakchi-ready"}, headers=self.desk); self.assertTrue(j["result"]["ok"], j)
        self.assertEqual(req("POST", "/api/console/home/preflight", {"plan_hash": h, "profile": ""}, headers=self.desk)[0], 400)
        self.assertEqual(req("POST", "/api/console/home/rehearse", {"plan_hash": h, "profile": "hakchi-ready", "scenario": "real"}, headers=self.desk)[0], 400)
        s, j = req("POST", "/api/console/home/rehearse", {"plan_hash": h, "profile": "hakchi-ready", "scenario": "update", "then": ["uninstall", "rm -rf /"], "fault": {"before": 3}, "transport": "ssh"}, headers=self.desk)
        self.assertEqual(s, 200, j); run = j["run"]; cur = 0; evs = []; res = None
        for _ in range(600):
            s, q = req("GET", f"/api/console/run?id={run}&cursor={cur}", headers=self.desk); evs += q.get("events") or []; cur = q.get("cursor", cur)
            if q.get("done"): res = q["result"]; break
            time.sleep(0.2)
        r = res["result"]; self.assertEqual(r["install"]["result"], "installed", r); self.assertTrue(r["uninstall"]["ok"]); self.assertEqual(r["scenario"], "update")
        self.assertTrue(any(e.get("stage") == "readback" for e in evs)); self.assertNotIn("export", r, "only the listed follow-ups run")
        s, j = req("GET", "/api/console/home/status", headers=self.desk); self.assertEqual(j["records"]["operations"][0]["outcome"], "installed"); self.assertTrue(j["records"]["last_plan_current"])
    def test_22_physical_operations_and_confirmations_are_refused_end_to_end(self):
        s, j = req("POST", "/api/console/home/plan", {}, headers=self.desk); h = j["result"]["hash"]
        for op in ("confirm", "install", "update", "uninstall", "recover", "inspect", "export"):
            for extra in ({}, {"force": True, "token": "x", "transport": "clovershell", "host": "169.254.13.37", "physical": True, "gate": "open", "device": "hw-0001", "operation": "install"}):
                s, j = req("POST", f"/api/console/home/{op}", {"plan_hash": h, **extra}, headers=self.desk)
                self.assertEqual(s, 403, (op, extra, j)); self.assertEqual(j["code"], "physical-disabled"); self.assertIs(j["physical"], False)
        self.assertEqual(req("POST", "/api/console/home/format", {}, headers=self.desk)[0], 404)
    def test_23_a_changed_package_makes_the_old_plan_obsolete(self):
        s, j = req("POST", "/api/console/home/plan", {}, headers=self.desk); h = j["result"]["hash"]
        shutil.rmtree(self.pkg); make_package(self.pkg, "changed")
        s, j = req("POST", "/api/console/home/preflight", {"plan_hash": h, "profile": "hakchi-ready"}, headers=self.desk); self.assertEqual(s, 409, j); self.assertEqual(j["code"], "plan-obsolete")
        s, j = req("POST", "/api/console/home/rehearse", {"plan_hash": h, "profile": "hakchi-ready", "scenario": "first-install"}, headers=self.desk); run = j["run"]
        for _ in range(200):
            s, q = req("GET", f"/api/console/run?id={run}", headers=self.desk)
            if q.get("done"): break
            time.sleep(0.2)
        self.assertEqual(q["result"]["code"], "plan-obsolete")
        s, j = req("GET", "/api/console/home/status", headers=self.desk); self.assertFalse(j["records"]["last_plan_current"])

if __name__ == "__main__":
    unittest.main(verbosity=2)
