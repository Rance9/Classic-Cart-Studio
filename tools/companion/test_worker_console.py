"""The corrected installer through the console transport worker, end to end on a PC: ccs_install.Installer -> worker_console.WorkerConsole ->
the worker's line protocol -> the worker's own typed console commands -> the console's stock BusyBox 1.24.1 (and hakchi's own pack_install /
pack_uninstall with hakchi's BusyBox) under qemu-arm, in a disposable folder that stands in for the console. The worker is its test host
(CcsWorker.Tests.exe --serve-busybox, test project only: the shipped worker has no such mode and refuses every real connection). The stand-in's
identity (hakchi's kernel, a hardware id) is given here, as console.json gives it to the PC stand-in: identify itself is the worker's own test.
Skipped (never passed) when the test host, WSL or qemu-arm is not there.
  python tools/companion/test_worker_console.py [test host exe]"""
from __future__ import annotations
import hashlib, json, os, shutil, subprocess, sys, tempfile, unittest
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path[:0] = [HERE]
import console_test_guard   # noqa: E402,F401  (first: the controlled console boundary, k3e)
from console_engine import ccs_install as CI, gate   # noqa: E402
from console_engine.worker_console import WorkerConsole, open_worker_console   # noqa: E402
from test_ccs_install import make_package   # noqa: E402
HOST = next((a for a in sys.argv[1:] if a.endswith(".exe")), r"C:\NES\work\cs\console-worker\bin\CcsWorker.Tests\Release\net48\CcsWorker.Tests.exe")
if HOST in sys.argv: sys.argv.remove(HOST)
def sha(b): return hashlib.sha256(b).hexdigest()

class StandIn(WorkerConsole):
    """the worker test host's disposable folder, identified as a console that runs hakchi"""
    def identity(self, fresh=False): return {"kernel": CI.HAKCHI_KERNEL, "hakchi": "v1.0.4-126", "system": "nes-usa", "hwid": "busybox-standin", "can_interact": True, "custom_firmware": False, "simulated": True}   # k3c: the fields the engine now requires
class Faulty(StandIn):
    def __init__(self, *a, fault=None, **k): super().__init__(*a, **k); self.fault = fault or {}; self.nchanges = 0
    def _f(self, what, fn, *a):
        self.nchanges += 1
        if self.fault.get("before") == self.nchanges: raise ConnectionError(f"lost before {what}")
        r = fn(*a)
        if self.fault.get("after") == self.nchanges: raise ConnectionError(f"lost after {what}")
        return r
    def put(self, *a, **k): return self._f("put", lambda: WorkerConsole.put(self, *a, **k))
    def mkdir(self, p): return self._f("mkdir", WorkerConsole.mkdir, self, p)
    def rmdir(self, p): return self._f("rmdir", WorkerConsole.rmdir, self, p)
    def remove(self, p, h): return self._f("remove", WorkerConsole.remove, self, p, h)
    def set_pointer(self, p, v, expect=None): return self._f("pointer", WorkerConsole.set_pointer, self, p, v, expect)   # k3c: the checked pointer
    def pack_install(self, n, f): return self._f("pack_install", WorkerConsole.pack_install, self, n, f)
    def pack_uninstall(self, n): return self._f("pack_uninstall", WorkerConsole.pack_uninstall, self, n)

class T(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not os.path.isfile(HOST): raise unittest.SkipTest(f"SKIPPED: the worker's test host is not built ({HOST}) (not a pass)")
        cls.t = tempfile.mkdtemp(prefix="ccs-wc-"); cls.root = os.path.join(cls.t, "packages")
        cls.pa = make_package(os.path.join(cls.root, "a"), "a"); cls.pb = make_package(os.path.join(cls.root, "b"), "b")
        cls.plan_a, cls.plan_b = CI.build_plan(cls.pa), CI.build_plan(cls.pb); cls.j = os.path.join(cls.t, "journals"); os.makedirs(cls.j)
    @classmethod
    def tearDownClass(cls): shutil.rmtree(cls.t, ignore_errors=True)
    def host(self, cls=StandIn, **k):
        p = subprocess.Popen([HOST, "--serve-busybox", "--package", self.root], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        c = cls(p, simulated=True, kind="busybox-stand-in", package_root=self.root, **k)
        try: c.call("capabilities")
        except ConnectionError:
            err = p.stderr.read().decode(errors="replace"); c.close()
            if p.returncode == 3: raise unittest.SkipTest(f"SKIPPED: WSL / qemu-arm / the console files are not available ({err.strip()}) (not a pass)")
            raise
        self.addCleanup(c.close); return c
    def state(self, c):   # what the console would start from, read back through the worker
        b = c.read(CI.POINTER, 64); ptr = b.decode().strip() if b else None
        return {"pointer": ptr, "module": c.pack_files(CI.HOOK_NAME), "overlay": c.overlay_sha256(CI.HOOK_FILE), "pkg": sorted(c.listdir(CI.PKG_DIR)), "install": sorted(c.listdir(CI.INST_DIR)),
                "stock_s81": c.sha256("/etc/init.d/S81clover-mcp"), "unknown": c.sha256(CI.PKG_DIR + "/someone-elses/keep.txt")}
    def seed(self, c):
        for d in (CI.ROOT_DIR, CI.PKG_DIR, CI.PKG_DIR + "/someone-elses"): c.mkdir(d)
        c.put(CI.PKG_DIR + "/someone-elses/keep.txt", b"not ours")
    def test_01_install_repeat_update_uninstall_and_export_through_the_worker(self):
        c = self.host(); self.seed(c); stock = c.sha256("/etc/init.d/S81clover-mcp"); self.assertIsNotNone(stock)
        self.assertEqual(c.call("connect", {"transport": "ssh"}, raw=True)["error"], "physical-disabled", "the test host never connects: its connect goes to its own refusing opener, whatever the gate")
        r = CI.Installer(c, self.plan_a, self.pa, self.j).run(); self.assertTrue(r["ok"], r); self.assertEqual(r["result"], "installed")
        s = self.state(c); self.assertEqual(s["pointer"], r["copy"]); self.assertEqual(s["module"], {CI.HOOK_FILE: CI.module_sha()}); self.assertEqual(s["overlay"], CI.module_sha())
        self.assertEqual(s["unknown"], sha(b"not ours")); self.assertEqual(s["stock_s81"], stock, "the stock start script itself is never written (hakchi's overlay is)")
        for f in self.plan_a["files"]: self.assertEqual(c.sha256(f"{CI.PKG_DIR}/{r['copy']}/{f['path']}"), f["sha256"], f["path"])
        self.assertEqual(CI.Installer(c, self.plan_a, self.pa, self.j).run()["result"], "no-change")
        u = CI.Installer(c, self.plan_b, self.pb, self.j).run(); self.assertTrue(u["ok"], u); self.assertEqual(self.state(c)["pointer"], u["copy"])
        self.assertNotIn(r["copy"], c.listdir(CI.PKG_DIR), "the earlier copy's own files and folders removed after the switch")
        c.mkdir(CI.DATA_DIR + "/saves"); c.put(CI.DATA_DIR + "/saves/cart.sav", b"\x01" * 8192)
        ex = os.path.join(self.t, "export"); e = CI.export_data(c, ex); self.assertTrue(e["ok"], e); self.assertEqual(e["files"], 2)
        plan = CI.uninstall_plan(c); self.assertTrue(plan["ok"], plan); self.assertTrue(any("someone-elses" in k for k in plan["keep"]))
        x = CI.uninstall(c); self.assertTrue(x["ok"], x); s = self.state(c)
        self.assertEqual(s["module"], {}); self.assertIsNone(s["overlay"], "hakchi's pack_uninstall removed the start-up file"); self.assertIsNone(s["pointer"])
        self.assertEqual(s["pkg"], ["someone-elses"]); self.assertEqual(s["unknown"], sha(b"not ours")); self.assertEqual(c.sha256(CI.DATA_DIR + "/saves/cart.sav"), sha(b"\x01" * 8192), "saves kept")
    def test_02_lost_connections_at_the_switch_and_the_module_are_undone_through_the_worker(self):
        # the console-changing calls of a first install here: mkdir install (1), attempt record (2), copy record (3), copy folder (4), its folders and
        # files, data folder, identity, pointer, module record, module. Faults at the copy, just after the switch, around the module and just before it
        for fault in ({"after": 5}, {"after": 16}, {"after": 19}, {"before": 21}, {"after": 21}):   # a folder, the last file, the switch, before and after hakchi's pack_install
            with self.subTest(fault=fault):
                c = self.host(Faulty, fault=fault); self.seed(c); before = self.state(c)
                r = CI.Installer(c, self.plan_a, self.pa, self.j).run(); self.assertFalse(r["ok"], r)
                self.assertTrue(r["rolled_back"]["ok"], r); self.assertEqual(self.state(c), before, f"{fault}: exactly as before, read back through the worker")
                c.fault = {}; self.assertEqual(CI.Installer(c, self.plan_a, self.pa, self.j).run()["result"], "installed", "and it installs afterwards"); c.close()
    def test_03_refusals_from_the_console_itself(self):
        c = self.host(); self.seed(c)
        c.call("mkdir", {"path": CI.PKG_DIR + "/current"})   # a folder where the pointer belongs: refused, nothing changed
        before = self.state(c); r = CI.Installer(c, self.plan_a, self.pa, self.j).run(); self.assertEqual(r["result"], "refused"); self.assertEqual(self.state(c), before)
        with self.assertRaises(PermissionError): c.put("/etc/init.d/S81clover-mcp.x", b"x")
        with self.assertRaises(PermissionError): c.mkdir("/var/saves/x")
    def test_04_the_shipped_path_is_closed(self):   # (in the activation rehearsal, with the gate open: blocked by the test boundary instead)
        refused = gate.GateClosed if not gate.allowed("install") else console_test_guard.RealConsoleBlocked
        with self.assertRaises(refused): open_worker_console("ssh")
        with self.assertRaises(refused): CI.open_console("clovershell")

if __name__ == "__main__":
    unittest.main(verbosity=2)
