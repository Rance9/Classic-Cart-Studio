"""Classic Cart Studio's HOME on the console (K3) through the worker and the backend, PC only: the worker's HOME commands on fresh stand-ins
(the add-on choice in the plan, the check on each simulated console, every rehearsal scenario, removal and the saves copy, real progress, a
cancel at a safe point, obsolete plans, the PC-side records, every physical step reaching only the console opener, which the test boundary
blocks here), the backend's /api/console/home/ routes (desktop principal only; the page names no path, shell text, transport or fault, and
only the add-on choice reaches a new plan; physical operations and confirmations open in code and refused without a checked console; a
console check from a source checkout finds no console worker), and the backend-owned confirmation (bound to one operation, plan and console,
single use, expiring; made only when the gate allows the tier). The backend started here answers its USB and driver probes with nothing
(no Windows device or driver list is read). No device, socket to a console or shell.
  python tools/companion/test_console_home.py"""
from __future__ import annotations
import hashlib, io, json, os, secrets, shutil, subprocess, sys, tempfile, time, unittest, urllib.error, urllib.request
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path[:0] = [HERE, os.path.join(HERE, "..", "rom-pipeline")]
import console_test_guard   # noqa: E402,F401  (first: the controlled console boundary, k3e)
from console_engine import gate, ccs_install as CI   # noqa: E402
from console_engine.worker import Worker   # noqa: E402
import console_confirm as CC   # noqa: E402
from test_ccs_install import make_package, rd, wr, chooser_env   # noqa: E402  (also sets the chooser's stand-in inputs and keeps this PC's own Workbench settings out)

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
        self.assertEqual((self.plan["addons"], self.plan["start_line"]), (CI.STUDIO_CHOICE, None), "Studio's default: CCS Mod, no CCS Nucleus")
        self.assertEqual(self.plan["start_text"], "When the console is turned on, Original Menu opens, as it does now. CCS HOME opens from its tile there.")
        r, _ = ask(self.w, "home-plan", {"mod_start": "on"}); self.assertEqual(r["event"], "error", "the retired start at power-on of CCS Mod is not a choice")
        rec, _ = ask(self.w, "home-records"); self.assertTrue(rec["result"]["last_plan_current"]); self.assertEqual(rec["result"]["last_plan"]["plan"]["hash"], self.plan["hash"])
    def test_02_preflight_on_each_simulated_console(self):
        # Studio's default plan (CCS Mod) against each simulated console: where it would take an add-on away, the check refuses and names the
        # choice that keeps the console's add-ons (test_install_methods.py installs those choices)
        snes = CI.model_refusal("snes-classic-edition")   # a Super NES Classic: as the platform registry's capability for its add-ons says
        exp = {"hakchi-ready": (True, ""), "stock": (False, "hakchi must be installed first"), "nearly-full": (False, "not enough room"), "with-mod": (True, ""),
               "with-nucleus": (False, CI.REMOVES_TEXT), "with-mod-nucleus": (False, CI.REMOVES_TEXT), "with-chooser": (False, CI.REMOVES_TEXT), "with-all": (False, CI.REMOVES_TEXT),
               "snes-ready": (snes is None, snes or "")}
        self.assertEqual(set(exp), set(CI.HOME_PROFILES))
        for prof, (ok, words) in exp.items():
            r, _ = ask(self.w, "home-preflight", {"plan_hash": self.plan["hash"], "profile": prof}); res = r["result"]
            self.assertEqual(res["ok"], ok, (prof, res)); self.assertTrue(res["simulated"])
            if words: self.assertIn(words, res["why"])
            if words == CI.REMOVES_TEXT:
                self.assertTrue(res["keep_choice"]["mod"] and res["addons_now"]["nucleus"] != "none" and res["addons_now_text"].startswith("This console has CCS"), (prof, res))
            if ok: self.assertTrue(res["addons_now_text"].startswith("This console has")); self.assertIn(res["changes_text"], ("Adds CCS Mod.", "The add-ons and what opens at power-on stay as they are."))
        r, _ = ask(self.w, "home-preflight", {"plan_hash": self.plan["hash"], "profile": "../real"}); self.assertEqual(r["code"], "not-found")
    def test_03_every_rehearsal_scenario(self):
        r, ev = self.sim(scenario="first-install"); i = r["result"]["install"]; self.assertEqual(i["result"], "installed", i); self.assertTrue(r["result"]["stock_unchanged"])
        st = [e for e in ev if e.get("event") == "progress" and e.get("stage") == "stage"]; self.assertGreater(len(st), 3); self.assertEqual(st[-1]["done"], st[-1]["total"])
        self.assertTrue(all(e.get("simulated") for e in ev)); self.assertIn("readback", [e.get("stage") for e in ev])
        r, _ = self.sim(scenario="repeat"); self.assertEqual(r["result"]["install"]["result"], "no-change")
        r, _ = self.sim(scenario="update"); i = r["result"]["install"]; self.assertEqual(i["result"], "installed")
        ear = next(t["earlier"] for t in i["tidy"] if "earlier" in t)
        self.assertEqual((ear["keep"], ear["remove"], len(ear["kept"])), (1, [], 2), ("one earlier version by default: the copy in use before the update stays", i["tidy"]))
        r, _ = self.sim(scenario="repair"); i = r["result"]["install"]; self.assertEqual(i["result"], "installed"); self.assertTrue(any(k.endswith("/session.json") for k in i["kept_changed_or_unknown"]))
        r, _ = self.sim(scenario="interrupted"); x = r["result"]; self.assertFalse(x["install"]["ok"]); self.assertTrue(x["install"]["rolled_back"]["ok"]); self.assertEqual(x["after_recovery"], "installed")
        r, _ = self.sim(scenario="first-install", then=["uninstall", "export"]); x = r["result"]
        self.assertTrue(x["uninstall"]["ok"]); self.assertTrue(x["stock_unchanged_after_uninstall"]); self.assertTrue(x["export"]["verified"]); self.assertEqual(x["export"]["files"], 2)
        self.assertTrue(any(s.startswith("CCS Mod (ccs-mod, through hakchi's pack_uninstall") for s in x["uninstall_plan"]["remove"]), x["uninstall_plan"]["remove"])
        self.assertEqual(os.listdir(os.path.join(self.t, "root", "stand-ins")), [], "every stand-in is removed afterwards")
    def test_04_bad_scenarios_and_faults_are_refused(self):
        for a in ({"scenario": "real-console"}, {"then": ["format"]}, {"fault": {"disconnect_at": 3}}, {"fault": {"before": True}}, {"fault": "x"}):
            r, _ = self.sim(**a); self.assertEqual(r["event"], "error", a)
    def test_05_a_plan_for_a_package_that_is_no_longer_staged_is_obsolete(self):
        shutil.rmtree(self.pkg); make_package(self.pkg, "changed")
        for cmd in ("home-preflight", "home-simulate"):
            r, _ = ask(self.w, cmd, {"plan_hash": self.plan["hash"], "profile": "hakchi-ready"}); self.assertEqual(r.get("code"), "plan-obsolete", (cmd, r))
        # a physical step whose plan is no longer the staged package's is refused before any console session is looked for
        r, _ = ask(self.w, "home-execute", {"plan_hash": self.plan["hash"], "device": "sim-0001"}); self.assertEqual(r.get("code"), "plan-obsolete", r)
        self.assertFalse(os.path.exists(console_test_guard.LOG), "no console session was asked for")
        rec, _ = ask(self.w, "home-records"); self.assertFalse(rec["result"]["last_plan_current"])
    def test_06_records_keep_each_rehearsal(self):
        self.sim(scenario="first-install"); self.sim(scenario="interrupted")
        rec, _ = ask(self.w, "home-records"); ops = rec["result"]["operations"]; self.assertEqual(len(ops), 2)
        for o in ops:
            self.assertEqual(o["schema"], "ccs-console-operation/1"); self.assertTrue(o["simulated"]); self.assertEqual(o["plan"], self.plan["hash"]); self.assertTrue(o["journal"].endswith(".jsonl"))
            self.assertNotIn("token", json.dumps(o).lower()); self.assertTrue(os.path.isfile(os.path.join(self.t, "root", "journals", o["journal"])))
        self.assertEqual({o["outcome"] for o in ops}, {"installed", "stopped"})
    def test_07_every_physical_step_reaches_only_the_console_opener_whatever_the_client_adds(self):
        # both tiers are open in this version: a physical step opens its console session through the shipped opener (the transport worker),
        # which the test boundary blocks before any worker starts; nothing the client adds chooses a transport, a host or the gate
        self.assertTrue(gate.PHYSICAL_WRITE_ENABLED and gate.PHYSICAL_INSPECT_ENABLED)
        try: os.remove(console_test_guard.LOG)
        except OSError: pass
        extra = {"transport": "clovershell", "host": "169.254.13.37", "force": True, "gate": "closed", "token": "x", "physical": True}
        for cmd, tier in (("home-inspect", "inspect"), ("home-check", "inspect"), ("home-verify", "inspect"), ("home-export-console", "inspect"), ("home-uninstall-review", "inspect"),
                          ("home-recover-review", "inspect"), ("home-execute", "install"), ("home-uninstall", "install")):
            r, _ = ask(self.w, cmd, {"plan_hash": self.plan["hash"], "device": "sim-0001", "review": "b" * 64, **extra})
            self.assertEqual((r["event"], r.get("code")), ("error", "worker-error"), (cmd, r)); self.assertIn("blocked by the test boundary", r["text"], cmd)
            self.assertEqual(rd(console_test_guard.LOG, "r").split()[-2:], ["ssh", tier], f"{cmd}: the shipped opener tries ssh first, for its own tier")
        r, _ = ask(self.w, "home-recover", {"device": "sim-0001", "attempt": "c" * 8, **extra}); self.assertEqual(r.get("code"), "not-found", "no journal for that attempt: refused before a session")
        r, _ = ask(self.w, "home-execute", {"plan_hash": self.plan["hash"], **extra}); self.assertEqual(r.get("code"), "not-found", "no checked console named: refused before a session")
        os.remove(console_test_guard.LOG)
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
    def test_10_the_backend_asks_the_gate_in_code_and_makes_none_while_a_tier_is_closed(self):
        self.assertIs(CC.BACKEND.allowed, gate.allowed, "the backend's confirmations ask the gate in code (open in this version)")
        c = CC.Confirmations(allowed=lambda tier: False)   # a build whose tiers are closed
        for op in CC.OPS:
            with self.assertRaises(gate.GateClosed): c.mint(op, "a" * 64, "sim-0001")
            with self.assertRaises(gate.GateClosed): c.consume("anything", op, "a" * 64, "sim-0001")
        t = CC.BACKEND.mint("install", "a" * 64, "sim-0001")["token"]; CC.BACKEND.consume(t, "install", "a" * 64, "sim-0001")   # this version: made and used once
        with self.assertRaises(CC.ConfirmationRefused): CC.BACKEND.consume(t, "install", "a" * 64, "sim-0001")
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

PORT = int(os.environ.get("CCS_TEST_PORT") or 8773); B = f"http://127.0.0.1:{PORT}"; SITE = "https://classiccartstudio.com"
def req(method, path, body=None, headers=None):
    data = json.dumps(body).encode() if body is not None else None
    r = urllib.request.Request(B + path, data=data, method=method, headers={"Content-Type": "application/json", "X-CCS-Protocol": "1", **(headers or {})})
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
        wb = os.path.join(cls.t, "workbench-settings.json"); wr(wb, json.dumps({"schema": "ccs-console-backup-settings/1", "earlier_home_versions": 2}).encode())   # Workbench's setting: two
        cls.token = secrets.token_hex(32); env = dict(os.environ, CCS_WORKBENCH_SETTINGS=wb, CCS_COMPANION_CONFIG=cfgp, CCS_PERSONAL_DIR=os.path.join(cls.t, "personal"), CCS_STUDIO_DATA=cls.t, CCS_NATIVE_HOME_ROOT=cls.nh,
                                                       CCS_HOST_TOKEN=cls.token, CCS_HOST_PID=str(os.getpid()), PYTHONIOENCODING="utf-8", CCS_PHYSICAL_WRITE="1", CCS_GATE="open", **chooser_env())
        cls.log = open(os.path.join(cls.t, "companion.log"), "w", encoding="utf-8")
        cls.proc = subprocess.Popen([sys.executable, "-u", "-B", os.path.join(HERE, "console_test_backend.py"), os.path.join(HERE, "companion_app.py"), "--headless", "--port", str(PORT)], env=env, stdout=cls.log, stderr=subprocess.STDOUT)
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
    def test_25_the_earlier_versions_setting_reaches_the_meter_and_the_plan(self):
        s, j = req("GET", "/api/status", headers=self.desk); self.assertEqual((s, j.get("earlier_home_versions")), (200, 2), "the app's status carries Workbench's setting (the loadout meter uses it)")
        s, j = req("POST", "/api/console/home/plan", {}, headers=self.desk); self.assertEqual((s, j["result"]["earlier_home_versions"]), (200, 2), "and so does the installation plan")
    def test_21_status_plan_preflight_rehearsal_and_records(self):
        s, j = req("GET", "/api/console/home/status", headers=self.desk); self.assertEqual(s, 200, j); self.assertEqual(j["staged"]["layout"], "mod")
        ops = {o["id"]: o for o in j["operations"]}; self.assertTrue(ops["rehearse"]["available"]); self.assertTrue(all(ops[k]["available"] for k in ("inspect", "install", "recover", "export", "first-setup")))
        self.assertNotIn("ram-test", ops); self.assertFalse(any("not available" in o["text"] for o in j["operations"]), "no operation says it is not available")
        self.assertTrue(j["gate"]["install"] and j["gate"]["inspect"]); self.assertEqual({p["name"] for p in j["profiles"]}, set(CI.HOME_PROFILES))
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
    def test_24_the_addon_choice_through_the_routes(self):
        s, j = req("POST", "/api/console/home/plan", {"nucleus": "chooser", "start": "ccs-home", "auto": "wait", "folder": "C:/elsewhere", "mod_start": "on"}, headers=self.desk); self.assertEqual(s, 200, j)
        p = j["result"]; self.assertEqual((p["addons"], p["start_line"]), ({"mod": True, "nucleus": "chooser", "start": "ccs-home", "auto": "wait", "countdown": 10, "at_start": True}, "ccs-home always on default 0"))
        self.assertEqual(p["start_text"], "When the console is turned on, the startup chooser appears first. Nothing starts until you choose."); self.assertEqual(list(p["packages"]), ["mod", "nucleus", "nucleus-chooser"])
        for bad in ({"mod": "true"}, {"nucleus": ["chooser"]}, {"auto": "wait now"}, {"countdown": "10"}, {"countdown": True}, {"at_start": 1}):
            s, j = req("POST", "/api/console/home/plan", bad, headers=self.desk); self.assertEqual((s, j.get("code")), (400, "bad-choice"), bad)
        s, j = req("POST", "/api/console/home/plan", {"mod": False, "nucleus": "direct", "start": "ccs-home"}, headers=self.desk); self.assertEqual((s, j.get("code"), j.get("error")), (409, "refused", CI.HOME_NEEDS_MOD))
        # without CCS Mod the collection reaches the console only as Hakchi games: neither is refused, and so is a collection none of whose
        # games can be one (this staged package's only game file is not a complete game)
        s, j = req("POST", "/api/console/home/plan", {"mod": False, "hakchi_games": False}, headers=self.desk); self.assertEqual((s, j.get("code"), j.get("error")), (409, "refused", CI.NOTHING_TEXT))
        s, j = req("POST", "/api/console/home/plan", {"mod": False}, headers=self.desk); self.assertEqual((s, j.get("code"), j.get("error")), (409, "refused", CI.NO_HAKCHI_GAMES_TEXT))
        s, j = req("POST", "/api/console/home/preflight", {"plan_hash": p["hash"], "profile": "with-nucleus"}, headers=self.desk); pf = j["result"]; self.assertTrue(pf["ok"], pf)
        self.assertEqual((pf["add"], pf["start"]["now"], pf["start"]["want"]), (["ccs-mod", "ccs-nucleus-chooser"], "native never off default 10", "ccs-home always on default 0"))
        self.assertEqual(pf["changes_text"], "Adds CCS Mod and CCS Nucleus's startup chooser. Updates CCS Nucleus's start setting: When the console is turned on, the startup chooser appears first. Nothing starts until you choose.")
        s, j = req("POST", "/api/console/home/plan", {}, headers=self.desk); s, j = req("POST", "/api/console/home/preflight", {"plan_hash": j["result"]["hash"], "profile": "with-all"}, headers=self.desk)
        pf = j["result"]; self.assertFalse(pf["ok"]); self.assertEqual((pf["why"], pf["keep_choice"]), (CI.REMOVES_TEXT, {"mod": True, "nucleus": "chooser", "start": "ccs-home", "auto": "last", "countdown": 10, "at_start": True}))
    def test_22_physical_operations_need_a_checked_console_and_a_source_checkout_has_no_console_worker(self):
        s, j = req("POST", "/api/console/home/plan", {}, headers=self.desk); h = j["result"]["hash"]
        for op in ("confirm", "install", "update", "uninstall", "recover", "check", "verify", "export"):
            for extra in ({}, {"force": True, "token": "x", "transport": "clovershell", "host": "169.254.13.37", "physical": True, "gate": "open", "device": "hw-0001", "operation": "install", "review": "b" * 64, "attempt": "c" * 8}):
                s, j = req("POST", f"/api/console/home/{op}", {"plan_hash": h, "operation": "install", **extra}, headers=self.desk)
                self.assertEqual((s, j.get("code")), (409, "check-console"), (op, extra, j))   # the gate is open; nothing is confirmed or run before a check
        # the check itself starts its run; this backend is a source checkout, whose console worker (the shipped transport worker) is not
        # there, so no console is ever looked for and nothing becomes the checked console
        s, j = req("POST", "/api/console/home/inspect", {"transport": "ssh", "host": "169.254.13.37"}, headers=self.desk); self.assertEqual(s, 200, j); self.assertTrue(j["physical"])
        q = None
        for _ in range(300):
            s, q = req("GET", f"/api/console/run?id={j['run']}", headers=self.desk)
            if q.get("done"): break
            time.sleep(0.2)
        self.assertEqual(q["result"]["event"], "error", q); self.assertIn("no console answered", q["result"]["text"])
        s, st = req("GET", "/api/console/home/status", headers=self.desk); self.assertIsNone(st["console"]["checked"]); self.assertFalse(st["transport_worker"]["present"])
        self.assertEqual(req("POST", "/api/console/home/format", {}, headers=self.desk)[0], 404)
        self.assertEqual(req("POST", "/api/console/execute", {"plan_hash": h}, headers=self.desk)[0], 404, "the older game sync has no route")
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
