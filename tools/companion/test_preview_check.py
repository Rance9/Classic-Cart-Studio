r"""The collection check (POST /api/preview, Install on NES Classic's Check Collection step) against the app's own backend, PC only: a new
data folder, a new port, the built site; this computer's USB presence and driver checks answered by the test (console_test_backend.py, here
made slow: CCS_TEST_SLOW_PROBE), never read.
  prompt     with this computer's own checks slow (25 s, as right after setting up Hakchi2 CE on a slow PC), the check answers at once,
             saying those checks are still running ("checking"), and /api/status has them when they finish
  together   two checks sent at the same moment both answer, one after the other, each with its own checked-collection file
  failure    a check that fails inside the backend says why to the page and writes its reason and traceback to the backend's log
  idle       a kept-alive connection left idle past the backend's idle time is closed quietly: no traceback in the log
  own game   a game added from a file named like a word (an own game of 786,448 bytes: mapper 4, 512 KB + 256 KB, battery) is checked
             like any other, by its recorded title
  python test_preview_check.py --site <built site>      Exit 0 = PASS. (Port: CCS_TEST_PORT, else 8779.)"""
from __future__ import annotations
import argparse, hashlib, json, os, secrets, shutil, socket, subprocess, sys, tempfile, threading, time, unittest, urllib.error, urllib.request
HERE = os.path.dirname(os.path.abspath(__file__))
ap = argparse.ArgumentParser(add_help=False); ap.add_argument("--site"); A, rest = ap.parse_known_args(); sys.argv[1:] = rest
PORT = int(os.environ.get("CCS_TEST_PORT") or 8779); B = f"http://127.0.0.1:{PORT}"; SITE = "https://classiccartstudio.com"

def own_game() -> bytes:
    """an iNES file made here (not a game): mapper 4, 32 x 16 KB of program and 32 x 8 KB of picture data, battery: 786,448 bytes"""
    body = bytearray(); h = hashlib.sha256(b"CCS-PREVIEW-CHECK own game").digest()
    while len(body) < 786432: h = hashlib.sha256(h).digest(); body += h
    return bytes([0x4E, 0x45, 0x53, 0x1A, 32, 32, 0x42, 0x00]) + bytes(8) + bytes(body[:786432])

@unittest.skipUnless(A.site and os.path.isfile(os.path.join(A.site, "catalog.json")), "the built site is not given (--site)")
class T(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.t = tempfile.mkdtemp(prefix="ccs-preview-"); d = os.path.join(cls.t, "data"); os.makedirs(d)
        cfgp = os.path.join(cls.t, "companion.json")
        with open(cfgp, "w", encoding="utf-8") as f: json.dump({"hakchi_exe": os.path.join(cls.t, "no-hakchi", "hakchi.exe"), "port": PORT, "no_launch": True, "exports_dir": os.path.join(cls.t, "exports"), "staging_dir": os.path.join(cls.t, "staging")}, f)
        cls.token = secrets.token_hex(32); cls.env = dict(os.environ, CCS_COMPANION_CONFIG=cfgp, CCS_PERSONAL_DIR=os.path.join(cls.t, "personal"), CCS_STUDIO_DATA=d, CCS_STUDIO_SITE=A.site,
                                                           CCS_HOST_TOKEN=cls.token, CCS_HOST_PID=str(os.getpid()), PYTHONIOENCODING="utf-8", CCS_TEST_SLOW_PROBE="25", CCS_HELPER_IDLE_TIMEOUT="2")
        # the own game, added as Add Game Files adds one (its title is its file's name), in this test's own personal folder
        src = os.path.join(cls.t, "files", "active.nes"); os.makedirs(os.path.dirname(src)); open(src, "wb").write(own_game())
        r = subprocess.run([sys.executable, "-c", "import sys, json; sys.path[:0] = [sys.argv[1], sys.argv[1] + '/../rom-pipeline']; import personal as ps; print(json.dumps(ps.ingest(sys.argv[2], origin='file')['entry']))", HERE, src],
                           env=cls.env, capture_output=True, text=True, encoding="utf-8", timeout=120)
        assert r.returncode == 0, r.stderr[-800:]; cls.entry = json.loads(r.stdout.strip().splitlines()[-1])
        cls.log = os.path.join(cls.t, "backend.log"); cls.logf = open(cls.log, "w", encoding="utf-8")
        cls.proc = subprocess.Popen([sys.executable, "-u", "-B", os.path.join(HERE, "console_test_backend.py"), os.path.join(HERE, "companion_app.py"), "--headless", "--port", str(PORT)], env=cls.env, stdout=cls.logf, stderr=subprocess.STDOUT)
        for _ in range(200):
            try:
                with urllib.request.urlopen(B + "/api/ping", timeout=1): break
            except Exception: time.sleep(0.25)
        cls.desk = {"Origin": SITE, "Authorization": "Bearer " + cls.token, "Content-Type": "application/json", "X-CCS-Protocol": "1"}
    @classmethod
    def tearDownClass(cls):
        cls.proc.terminate()
        try: cls.proc.wait(20)
        except subprocess.TimeoutExpired: cls.proc.kill(); cls.proc.wait(10)
        cls.logf.close(); shutil.rmtree(cls.t, ignore_errors=True)
    def req(self, method, path, body=None, timeout=120):
        r = urllib.request.Request(B + path, data=json.dumps(body).encode() if body is not None else None, method=method, headers=self.desk)
        try:
            with urllib.request.urlopen(r, timeout=timeout) as resp: return resp.status, json.loads(resp.read() or b"{}")
        except urllib.error.HTTPError as e: return e.code, json.loads(e.read() or b"{}")
    def manifest(self):
        pid = self.entry["id"]
        return {"schema": "nes-classic-collection/4", "name": "Preview check", "hidden_factory_games": [], "selections": [{"game_id": pid, "version_id": pid, "display_title": self.entry["user"]["title"], "source": "personal", "install_required": True}]}
    def logtext(self):   # the backend's own log (headless: request lines and failures go to <data>/logs/server.log, as in the app), and its output
        self.logf.flush(); sl = os.path.join(self.t, "data", "logs", "server.log")
        return (open(sl, encoding="utf-8", errors="replace").read() if os.path.isfile(sl) else "") + open(self.log, encoding="utf-8", errors="replace").read()

    def test_1_the_check_answers_at_once_while_this_computers_own_checks_run(self):
        self.assertEqual(self.entry["user"]["title"], "active", "Add Game Files names a game after its file until the person names it")
        t0 = time.time(); s, j = self.req("POST", "/api/preview", {"manifest": self.manifest()}); took = time.time() - t0
        self.assertEqual(s, 200, j); self.assertLess(took, 10, f"answered in {took:.1f} s (it used to wait up to 20 s for this computer's checks)")
        self.assertEqual(j["env"]["diag"]["state"], "checking", j["env"]); self.assertIsNone(j["env"]["hakchi_running"], "not known yet: never said to be no")
        row = next(g for g in j["preview"]["games"] if g.get("game_id") == self.entry["id"]); self.assertEqual(row["title"], "active"); self.assertEqual(row["kind"], "personal game")
        for _ in range(80):   # the page follows /api/status until this computer's checks have answered
            s, st = self.req("GET", "/api/status")
            if st.get("diag", {}).get("state") == "ok": break
            time.sleep(1)
        self.assertEqual(st["diag"]["state"], "ok", st.get("diag")); self.assertIs(st["console_connected"], False)
    def test_2_two_checks_at_once_both_answer_each_with_its_own_file(self):
        out = []; m = self.manifest()
        def one(): out.append(self.req("POST", "/api/preview", {"manifest": m}))
        th = [threading.Thread(target=one) for _ in range(2)]
        for x in th: x.start()
        for x in th: x.join(180)
        self.assertEqual([s for s, _ in out], [200, 200], out); names = {j["manifest_file"] for _, j in out}; self.assertEqual(len(names), 2, names)
        for n in names: self.assertTrue(os.path.isfile(os.path.join(self.t, "exports", n)))
    def test_3_a_failed_check_says_why_and_the_log_has_it(self):
        s, j = self.req("POST", "/api/preview", {"manifest": {"schema": "nes-classic-collection/4", "selections": "not a list"}})
        self.assertEqual(s, 500, j); self.assertIn("Try again", j["error"]); self.assertNotIn("Error", j["error"]); self.assertNotIn("check the collection", j["error"])   # plain words; the technical name is in the log
        time.sleep(0.3); log = self.logtext()
        self.assertIn("the collection check (/api/preview) failed: Traceback", log); self.assertIn("Error", log.split("the collection check (/api/preview) failed:")[-1][:4000])
    def test_4_an_idle_kept_alive_connection_is_closed_quietly(self):
        before = self.logtext().count("Traceback")
        k = socket.create_connection(("127.0.0.1", PORT), timeout=10)
        try:
            k.sendall(b"GET /api/ping HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n"); time.sleep(0.5); k.recv(65536)   # one answered request, then idle
            time.sleep(4); k.settimeout(5); rest = k.recv(65536)
        finally: k.close()
        self.assertEqual(rest, b"", "the backend closed the idle connection")
        time.sleep(0.5); log = self.logtext()
        self.assertEqual(log.count("Traceback") - before, 0, log[-2000:]); self.assertNotIn("is not iterable", log); self.assertIn("POST /api/preview", log, "the log is the one the backend writes")

if __name__ == "__main__":
    r = unittest.main(exit=False, verbosity=2).result
    print(f"RESULT {r.testsRun - len(r.failures) - len(r.errors) - len(r.skipped)}/{r.testsRun}" + (f" ({len(r.skipped)} skipped)" if r.skipped else ""))
    sys.exit(0 if r.wasSuccessful() and not r.skipped else 1 if not r.wasSuccessful() else 2)
