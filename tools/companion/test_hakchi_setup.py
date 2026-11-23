r"""Set Up Hakchi2 CE (POST /api/install-hakchi, GET /api/install-hakchi/status): the real pinned package extracted into a disposable
destination by the backend in this process (its HTTP server on a free loopback port); never the person's own Hakchi2 CE folder, no driver,
no console. The setup runs in the background, one at a time, and its outcome is read back, never guessed.
  delayed     a slow setup (each stage held back): the request answers at once that it started; a second request while it runs is refused
              (nothing started twice); the stages and the extraction count are reported; the outcome is set up, verified, 914 files, no staging folder
  failure     a check that fails after extraction: the outcome says Hakchi2 CE was not set up and why; the partly extracted files are removed,
              so "changed" is false; the destination is still absent
  lost reply  the answer to the request never reaches the caller (it stops listening at once): the setup still finishes, the status reads
              it as set up, and asking again changes nothing (already set up)
  windows     the backend's own command-line helpers run without a console window of their own (CREATE_NO_WINDOW)
  page        (with a built site and headless Edge: --site, --edge-module) the Install on NES Classic panel in the page shows the progress and
              then the actual outcome for each case above; after a lost reply it reads the outcome instead of starting again, and says
              "Nothing was changed" only when the backend established it
  python test_hakchi_setup.py --package-dir <folder with the pinned package> [--site <built site>] [--edge-module <folder of cdp_edge.py>]
  Exit 0 = PASS."""
from __future__ import annotations
import argparse, hashlib, json, os, shutil, socket, sys, tempfile, threading, time, unittest, urllib.error, urllib.request
HERE = os.path.dirname(os.path.abspath(__file__))
ap = argparse.ArgumentParser(add_help=False); ap.add_argument("--package-dir"); ap.add_argument("--site"); ap.add_argument("--edge-module")
A, rest = ap.parse_known_args(); sys.argv[1:] = rest
TMP = tempfile.mkdtemp(prefix="ccs-hksetup-"); DEST = os.path.join(TMP, "dest")
CFGP = os.path.join(TMP, "companion.json")
json.dump({"hakchi_exe": os.path.join(DEST, "one", "hakchi2-ce-3.9.3", "hakchi.exe"), "staging_dir": os.path.join(TMP, "staging"), "exports_dir": os.path.join(TMP, "exports"),
           "rehearsal_tree": True, "packages_dir": A.package_dir or ""}, open(CFGP, "w"))
os.environ.update(CCS_COMPANION_CONFIG=CFGP, CCS_STUDIO_DATA=os.path.join(TMP, "data"), CCS_PERSONAL_DIR=os.path.join(TMP, "personal"))
if A.site: os.environ["CCS_STUDIO_SITE"] = A.site
sys.path[:0] = [HERE, os.path.join(HERE, "..", "rom-pipeline")]
import hakchi_bridge as hb, nes_companion as nc   # noqa: E402
from console_engine import storage_model as _SM, worker_console as _WC   # noqa: E402,F401  (this source's own, before anything else is on the path)

def free_port():
    with socket.socket() as s: s.bind(("127.0.0.1", 0)); return s.getsockname()[1]
REAL_INSTALL = hb.install_hakchi   # the setup itself; a case may hold it back or make its check fail
PORT = free_port(); SRV = nc.make_server(PORT); threading.Thread(target=SRV.serve_forever, daemon=True).start()
BASE = f"http://127.0.0.1:{PORT}"
def call(method, path, body=None, timeout=60):
    req = urllib.request.Request(BASE + path, method=method, data=json.dumps(body or {}).encode() if method == "POST" else None,
                                 headers={"Content-Type": "application/json", "X-NES-Token": nc.TOKEN, "X-NES-Protocol": str(nc.PROTOCOL), "Host": f"127.0.0.1:{PORT}", "Origin": BASE})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r: return r.status, json.loads(r.read())
    except urllib.error.HTTPError as e: return e.code, json.loads(e.read())
def wait_done(limit=300):
    seen = []; t0 = time.time()
    while time.time() - t0 < limit:
        _, s = call("GET", "/api/install-hakchi/status"); j = s.get("job") or {}
        seen.append((j.get("state"), j.get("stage"), j.get("done"), j.get("total")))
        if j.get("state") != "running": return s, seen
        time.sleep(0.25)
    raise AssertionError("the setup did not finish")
def use_dest(name):
    """a new disposable destination for one case (the configuration's Hakchi2 CE path)"""
    nc.CFG["hakchi_exe"] = os.path.join(DEST, name, "hakchi2-ce-3.9.3", "hakchi.exe"); return os.path.dirname(nc.CFG["hakchi_exe"])

class Setup(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not (A.package_dir and os.path.isfile(os.path.join(A.package_dir, hb.HAKCHI_PACKAGE["name"])) and hb.package_verified(os.path.join(A.package_dir, hb.HAKCHI_PACKAGE["name"]))):
            raise unittest.SkipTest("SKIPPED: the pinned Hakchi2 CE package is not given (--package-dir) (not a pass)")
    def tearDown(self): hb.install_hakchi = REAL_INSTALL

    def test_01_delayed_success_one_at_a_time(self):
        root = use_dest("delayed"); real = REAL_INSTALL
        def slow(cfg, dry_run=False, progress=None):
            def p(stage, done=0, total=0):
                progress(stage, done, total)
                if stage != "extracting" or done == total: time.sleep(1.0)   # each stage held back: a setup that takes a while
            return real(cfg, dry_run, p)
        hb.install_hakchi = slow
        t0 = time.time(); code, r = call("POST", "/api/install-hakchi"); took = time.time() - t0
        self.assertEqual((code, r.get("started")), (202, True), r); self.assertLess(took, 5, "the request answers once the setup has started")
        code2, r2 = call("POST", "/api/install-hakchi"); self.assertEqual((code2, r2.get("running")), (409, True), r2)   # never two at once
        s, seen = wait_done()
        j = s["job"]; res = j["result"]
        self.assertEqual((j["state"], res["ok"], j["changed"], s["install"]["state"]), ("done", True, True, "ok"), j)
        stages = [x[1] for x in seen]; self.assertTrue({"checking", "extracting", "verifying", "placing"} <= set(stages) or "extracting" in stages, stages)
        self.assertTrue(any(x[1] == "extracting" and x[3] == 914 and 0 < x[2] <= 914 for x in seen), "the extraction count is reported")
        self.assertEqual(hb.sha256_file(os.path.join(root, "hakchi.exe")), hb.HAKCHI_SHA256); self.assertFalse(os.path.exists(root + ".installing"))
        self.assertEqual(sum(len(fs) for _, _, fs in os.walk(root)), 914)

    def test_02_failure_before_completion(self):
        root = use_dest("fails"); real = REAL_INSTALL
        def failing(cfg, dry_run=False, progress=None):
            old = hb.HAKCHI_SHA256; hb.HAKCHI_SHA256 = "0" * 64   # the check after extraction finds a different hakchi.exe
            try: return real(cfg, dry_run, progress)
            finally: hb.HAKCHI_SHA256 = old
        hb.install_hakchi = failing
        code, r = call("POST", "/api/install-hakchi"); self.assertEqual(code, 202, r)
        s, _ = wait_done(); j = s["job"]; res = j["result"]
        self.assertEqual((j["state"], res["ok"], j["changed"], res.get("removed")), ("failed", False, False, True), j)
        self.assertIn("was not set up", res["message"]); self.assertIn("removed", res["message"])
        self.assertFalse(os.path.exists(root)); self.assertFalse(os.path.exists(root + ".installing"))
        self.assertNotEqual(s["install"]["state"], "ok")

    def test_03_lost_reply_after_success(self):
        root = use_dest("lost")
        try: call("POST", "/api/install-hakchi", timeout=0.0001)   # the caller stops listening at once: its answer is lost
        except (TimeoutError, socket.timeout, urllib.error.URLError, OSError): pass
        s, _ = wait_done(); self.assertEqual((s["job"]["state"], s["install"]["state"]), ("done", "ok"), s)
        before = hb.sha256_file(os.path.join(root, "hakchi.exe")); mt = os.path.getmtime(os.path.join(root, "hakchi.exe"))
        code, r = call("POST", "/api/install-hakchi"); self.assertEqual(code, 202)
        s2, _ = wait_done(); res = s2["job"]["result"]
        self.assertEqual((res["ok"], res.get("already"), res["changed"]), (True, True, False), "asking again finds it set up and changes nothing")
        self.assertEqual((hb.sha256_file(os.path.join(root, "hakchi.exe")), os.path.getmtime(os.path.join(root, "hakchi.exe"))), (before, mt))

    def test_04_helpers_have_no_window(self):
        import inspect, console_runs, console_engine.worker_console as wc
        src = inspect.getsource(hb)
        runs = [src[i:src.index(")", src.index("capture_output", i))] for i in range(len(src)) if src.startswith("subprocess.run(", i)]
        self.assertEqual(len(runs), 5, "the process, device, version and driver queries and the driver installer's waiting PowerShell")
        for r in runs: self.assertTrue("creationflags=NO_WINDOW" in src[src.index(r):src.index(r) + len(r) + 160], r[:80])
        self.assertEqual(hb.NO_WINDOW, 0x08000000)
        self.assertIn("CREATE_NO_WINDOW", inspect.getsource(wc.open_worker_console)); self.assertIn("CREATE_NO_WINDOW", inspect.getsource(console_runs._spawn))
        self.assertIn("creationflags=hb.NO_WINDOW", inspect.getsource(nc.ui_verify))

class Page(unittest.TestCase):
    """the Install on NES Classic panel in the page the backend serves (headless Edge)"""
    @classmethod
    def setUpClass(cls):
        if not (A.site and A.edge_module and os.path.isfile(os.path.join(A.edge_module, "cdp_edge.py"))): raise unittest.SkipTest("SKIPPED: no built site or browser driver given (not a pass)")
        if not (A.package_dir and hb.package_verified(os.path.join(A.package_dir, hb.HAKCHI_PACKAGE["name"]))): raise unittest.SkipTest("SKIPPED: the pinned package is not given (not a pass)")
        sys.path.append(A.edge_module)   # the browser driver only: after this source's own folders
        from studio_harness import EdgeSession, READY
        cls.ed = EdgeSession(); cls.ed.start(); cls.ed.navigate(BASE + "/nes-catalog.html"); cls.ed.wait_for(READY, timeout=120)
    @classmethod
    def tearDownClass(cls):
        try: cls.ed.close()
        except Exception: pass
    def tearDown(self): hb.install_hakchi = REAL_INSTALL
    def panel(self, lose=False):
        """the panel as the page opens it when Hakchi2 CE is not set up; Set Up clicked (lose: the page's request answers as if lost, the
        request itself still reaching the backend); returns the texts seen while it ran and at the end, and how many setups the page asked for"""
        self.ed.js("window.__posts=0; window.__real=window.__real||hkPost; hkPost=async(p,b)=>{ if(p==='/api/install-hakchi'){ window.__posts++; if(%s){ window.__real(p,b); return {ok:false,timeout:true,error:'Classic Cart Studio did not answer in time. Try again.'}; } } return window.__real(p,b); }; 1" % ("true" if lose else "false"))
        self.ed.js("(async()=>{ const s=await capi('/api/status'); hkInstallPanel(s.install, s); window.__open=1; })(); 1"); self.ed.wait_for("window.__open===1&&!!document.getElementById('hkInstall')", timeout=60)
        self.ed.js("window.__open=0; $('hkInstall').click(); 1"); seen = set(); t0 = time.time()
        while time.time() - t0 < 300:
            b = self.ed.js("($('hkInstall')||{}).textContent||''"); seen.add(b)
            if self.ed.js("!!document.getElementById('hkDone')"): break
            time.sleep(0.2)
        return seen, self.ed.js("$('hkBody').innerText"), self.ed.js("window.__posts")
    def test_05_page_delayed_success(self):
        use_dest("page-ok"); real = REAL_INSTALL
        hb.install_hakchi = lambda cfg, dry_run=False, progress=None: real(cfg, dry_run, lambda s, d=0, t=0: (progress(s, d, t), time.sleep(0.6 if s != "extracting" else 0)))
        seen, text, posts = self.panel()
        self.assertIn("Hakchi2 CE 3.9.3 is set up.", text); self.assertNotIn("Nothing was changed", text); self.assertEqual(posts, 1)
        self.assertTrue(any(x.startswith("Copying Hakchi2 CE's files") for x in seen), seen)
    def test_06_page_failure(self):
        use_dest("page-fail"); real = REAL_INSTALL
        def failing(cfg, dry_run=False, progress=None):
            old = hb.HAKCHI_SHA256; hb.HAKCHI_SHA256 = "0" * 64
            try: return real(cfg, dry_run, progress)
            finally: hb.HAKCHI_SHA256 = old
        hb.install_hakchi = failing
        _, text, posts = self.panel()
        self.assertIn("Nothing was changed.", text); self.assertIn("partly extracted files were removed", text); self.assertEqual(posts, 1)
    def test_07_page_lost_reply_after_success(self):
        use_dest("page-lost")
        seen, text, posts = self.panel(lose=True)
        self.assertIn("Hakchi2 CE 3.9.3 is set up.", text, "the outcome read from Classic Cart Studio, not assumed"); self.assertNotIn("Nothing was changed", text)
        self.assertEqual(posts, 1, "no second setup started by the page")

if __name__ == "__main__":
    try: r = unittest.main(verbosity=2, exit=False).result
    finally:
        SRV.shutdown(); shutil.rmtree(TMP, ignore_errors=True)
    sys.exit(0 if r.wasSuccessful() else 1)
