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
        import test_hakchi_publish as HPT   # a collection without CCS Mod: its game files only (games_only.py)
        cls.pg = HPT.make_package(os.path.join(cls.root, "g"), HPT.GAMES[:2]); cls.plan_g = CI.build_plan(cls.pg, {"mod": False})
    @classmethod
    def tearDownClass(cls): shutil.rmtree(cls.t, ignore_errors=True)
    def host(self, cls=StandIn, seed=(), links=(), **k):   # seed: (console path, file) placed in the stand-in first (another program's file); links: (console path, console target)
        p = subprocess.Popen([HOST, "--serve-busybox", "--package", self.root] + [x for s in seed for x in ("--seed", s[0], s[1])] + [x for l in links for x in ("--link", l[0], l[1])],
                             stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        c = cls(p, simulated=True, kind="busybox-stand-in", package_root=self.root, **k)
        try: c.call("capabilities")
        except ConnectionError:
            err = p.stderr.read().decode(errors="replace"); c.close()
            if p.returncode == 3: raise unittest.SkipTest(f"SKIPPED: WSL / qemu-arm / the console files are not available ({err.strip()}) (not a pass)")
            raise
        self.addCleanup(c.close); return c
    def state(self, c):   # what the console would start from, read back through the worker
        b = c.read(CI.POINTER, 64); ptr = b.decode().strip() if b else None
        return {"pointer": ptr, "module": c.method_files("ccs-mod"), "overlay": c.overlay_sha256("/bin/ccs-mod"), "earlier": c.pack_files(CI.LEGACY_NAME), "pkg": sorted(c.listdir(CI.PKG_DIR)), "install": sorted(c.listdir(CI.INST_DIR)),
                "stock_s81": c.sha256("/etc/init.d/S81clover-mcp"), "unknown": c.sha256(CI.PKG_DIR + "/someone-elses/keep.txt")}
    def seed(self, c):
        for d in (CI.ROOT_DIR, CI.PKG_DIR, CI.PKG_DIR + "/someone-elses"): c.mkdir(d)
        c.put(CI.PKG_DIR + "/someone-elses/keep.txt", b"not ours")
    def test_01_install_repeat_update_uninstall_and_export_through_the_worker(self):
        c = self.host(); self.seed(c); stock = c.sha256("/etc/init.d/S81clover-mcp"); self.assertIsNotNone(stock)
        self.assertEqual(c.call("connect", {"transport": "ssh"}, raw=True)["error"], "physical-disabled", "the test host never connects: its connect goes to its own refusing opener, whatever the gate")
        r = CI.Installer(c, self.plan_a, self.pa, self.j).run(); self.assertTrue(r["ok"], r); self.assertEqual(r["result"], "installed")
        mp = CI.addon_package("mod"); s = self.state(c); self.assertEqual(s["pointer"], r["copy"]); self.assertEqual(s["module"], {"package": mp["package_id"], "files": mp["files"]}, "CCS Mod installed by hakchi's own pack_install, every file as its package lists it")
        self.assertEqual(s["overlay"], mp["files"]["/bin/ccs-mod"]); self.assertEqual(s["earlier"], {}); self.assertIsNone(c.sha256(CI.OVERLAY + CI.LEGACY_FILE), "no start file of the kind hakchi deletes at every start")
        self.assertEqual(s["unknown"], sha(b"not ours")); self.assertEqual(s["stock_s81"], stock, "the stock start script itself is never written (hakchi's overlay is)")
        for f in self.plan_a["files"]: self.assertEqual(c.sha256(f"{CI.PKG_DIR}/{r['copy']}/{f['path']}"), f["sha256"], f["path"])
        self.assertEqual(CI.Installer(c, self.plan_a, self.pa, self.j).run()["result"], "no-change")
        # the update with the setting keep none, so the earlier copy goes (the default keeps one earlier version: test_03 and test_ccs_install)
        u = CI.Installer(c, CI.build_plan(self.pb, earlier=0), self.pb, self.j).run(); self.assertTrue(u["ok"], u); self.assertEqual(self.state(c)["pointer"], u["copy"])
        self.assertNotIn(r["copy"], c.listdir(CI.PKG_DIR), "the earlier copy's own files and folders removed after the switch")
        c.mkdir(CI.DATA_DIR + "/saves"); c.put(CI.DATA_DIR + "/saves/cart.sav", b"\x01" * 8192)
        ex = os.path.join(self.t, "export"); e = CI.export_data(c, ex); self.assertTrue(e["ok"], e); self.assertEqual(e["files"], 2)
        plan = CI.uninstall_plan(c); self.assertTrue(plan["ok"], plan); self.assertTrue(any("someone-elses" in k for k in plan["keep"]))
        x = CI.uninstall(c); self.assertTrue(x["ok"], x); s = self.state(c)
        self.assertIsNone(s["module"]); self.assertIsNone(s["overlay"], "hakchi's pack_uninstall ran CCS Mod's own removal: its program is gone"); self.assertIsNone(s["pointer"])
        self.assertEqual(s["pkg"], ["someone-elses"]); self.assertEqual(s["unknown"], sha(b"not ours")); self.assertEqual(c.sha256(CI.DATA_DIR + "/saves/cart.sav"), sha(b"\x01" * 8192), "saves kept")
    def test_03_an_earlier_copy_another_menu_entry_names_is_kept_through_the_worker(self):
        """the worker's menu-refs on the console's own BusyBox: an Original Menu entry of another program that launches a file of the earlier
        copy keeps it; without that entry the same update removes it"""
        a_copy = f"{self.plan_a['version']}.0a0a0a0a"; other = os.path.join(self.t, "other.desktop")
        with open(other, "wb") as f: f.write(f"[Desktop Entry]\nName=Other\nExec=/bin/sh /var/lib/ccs/pkg/{a_copy}/bin/ccs-player-armv7\n".encode())
        plan_b0 = CI.build_plan(self.pb, earlier=0)
        for named in (True, False):
            with self.subTest(named=named):
                c = self.host(seed=[("/var/lib/hakchi/games/nes-usa/CLV-L-OTHER/CLV-L-OTHER.desktop", other)] if named else ())
                j = os.path.join(self.t, f"journals-named-{named}"); os.makedirs(j, exist_ok=True)   # the fixed attempt's journal is new in each case
                m = c.menu_refs(["pkg/" + a_copy]); self.assertTrue(m["complete"], m)
                self.assertEqual([x["path"] for x in m["refs"]], ["/var/lib/hakchi/games/nes-usa/CLV-L-OTHER/CLV-L-OTHER.desktop"] if named else [], "the worker's own answer")
                a = CI.Installer(c, self.plan_a, self.pa, j, attempt="0a0a0a0a").run(); self.assertTrue(a["ok"], a); self.assertEqual(a["copy"], a_copy)
                r = CI.Installer(c, plan_b0, self.pb, j).run(); self.assertTrue(r["ok"], r); self.assertEqual(r["result"], "installed")
                ear = next(x for x in r["tidy"] if "earlier" in x)
                if named:
                    self.assertEqual(ear["earlier"]["remove"], []); self.assertEqual(ear["earlier"]["why"][a_copy], CI.NAMED_YES)
                    self.assertEqual(ear["named_by"], {a_copy: ["/var/lib/hakchi/games/nes-usa/CLV-L-OTHER/CLV-L-OTHER.desktop"]})
                    self.assertIn(a_copy, c.listdir(CI.PKG_DIR)); self.assertEqual(r["preflight"]["earlier"]["why"][a_copy], CI.NAMED_YES, "the review says so first")
                else:
                    self.assertEqual(ear["earlier"]["remove"], [a_copy]); self.assertNotIn(a_copy, c.listdir(CI.PKG_DIR))
                c.close()
    def test_06_a_folder_link_that_hides_entries_keeps_the_earlier_copy_through_the_worker(self):
        """the worker's menu-refs on the console's own BusyBox and the installer with the default setting (keep one earlier version): a
        link to a folder outside hakchi's folders, where an entry names the oldest copy, keeps it (the listing is not complete); with
        Hakchi2 CE's own kind of link (a game's media folder inside its .storage) the same oldest copy goes"""
        a_copy = f"{self.plan_a['version']}.0b0b0b0b"; hidden = os.path.join(self.t, "hidden.desktop"); media = os.path.join(self.t, "media.txt")
        with open(hidden, "wb") as f: f.write(f"[Desktop Entry]\nExec=/bin/sh /var/lib/ccs/pkg/{a_copy}/bin/ccs-player-armv7\n".encode())
        with open(media, "wb") as f: f.write(b"media")
        G = "/var/lib/hakchi/games/nes-usa"
        cases = {"hidden": ([("/var/lib/elsewhere/hidden.desktop", hidden)], [(G + "/000/CLV-L-LINK", "/var/lib/elsewhere")]),
                 "ordinary": ([(G + "/.storage/CLV-L-S/autoplay/media.txt", media)], [(G + "/000/CLV-L-S/autoplay", G + "/.storage/CLV-L-S/autoplay")])}
        pc = make_package(os.path.join(self.root, "c6"), "c6"); plan_c = CI.build_plan(pc)
        for name, (seed, links) in cases.items():
            with self.subTest(case=name):
                c = self.host(seed=seed, links=links); j = os.path.join(self.t, f"journals-link-{name}"); os.makedirs(j, exist_ok=True)
                m = c.menu_refs(["pkg/" + a_copy]); self.assertEqual(m["complete"], name == "ordinary", m)
                if name == "hidden": self.assertEqual(m["linked"], [G + "/000/CLV-L-LINK"]); self.assertEqual(m["refs"], [], "the entry behind the link is not read")
                a = CI.Installer(c, self.plan_a, self.pa, j, attempt="0b0b0b0b").run(); self.assertTrue(a["ok"], a)
                b = CI.Installer(c, self.plan_b, self.pb, j).run(); self.assertTrue(b["ok"], b)
                r = CI.Installer(c, plan_c, pc, j).run(); self.assertTrue(r["ok"], r); self.assertEqual(r["result"], "installed")
                ear = next(x for x in r["tidy"] if "earlier" in x)["earlier"]; self.assertEqual(ear["keep"], 1)
                if name == "hidden":
                    self.assertEqual(ear["remove"], []); self.assertEqual(ear["why"][a_copy], CI.NAMED_UNKNOWN); self.assertIn(a_copy, c.listdir(CI.PKG_DIR))
                    self.assertEqual(r["preflight"]["earlier"]["why"][a_copy], CI.NAMED_UNKNOWN, "the review says so first")
                else:
                    self.assertEqual(ear["remove"], [a_copy]); self.assertNotIn(a_copy, c.listdir(CI.PKG_DIR)); self.assertIn(b["copy"], c.listdir(CI.PKG_DIR), "the one earlier version kept")
                c.close()
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
    def test_05_without_ccs_mod_only_the_game_files_go_through_the_worker(self):
        c = self.host(); self.seed(c); stock = c.sha256("/etc/init.d/S81clover-mcp")
        r = CI.Installer(c, self.plan_g, self.pg, self.j).run()
        # the game files (streamed from the package) and their notices (sent as bytes) are written and read back through the worker; this
        # stand-in has no hakchi of its own to find the console menu's game folders with, so the games are not added to Original Menu here,
        # and the result says so plainly instead of claiming an installation
        self.assertEqual((r["result"], r["delivery"], r["ok"]), ("incomplete", "games", False), r); self.assertFalse(r["hakchi"]["ok"]); self.assertIn("Install the collection again", r["next"])
        for f in self.plan_g["files"]: self.assertEqual(c.sha256(f"{CI.GAMES_DIR}/{r['copy']}/{f['path']}"), f["sha256"], f["path"])
        s = self.state(c); self.assertEqual((s["pointer"], s["module"], s["overlay"]), (None, None, None), "nothing of CCS HOME: no pointer and no CCS Mod")
        self.assertEqual(s["pkg"], ["someone-elses"], "nothing under CCS HOME's folder"); self.assertEqual(c.stat(CI.DATA_DIR)["type"], "none", "no CCS HOME saves folder")
        self.assertEqual(s["stock_s81"], stock); self.assertEqual(s["unknown"], sha(b"not ours"))
        v = CI.verify_installed(c, self.plan_g); self.assertEqual((v["mismatches"], v["hakchi_as_planned"], v["verified"]), ([], False, False), v)
        x = CI.uninstall(c); self.assertTrue(x["ok"], x); self.assertEqual(c.stat(CI.GAMES_DIR)["type"], "none", "the games copy is removed with the rest")
        self.assertEqual(self.state(c)["pkg"], ["someone-elses"]); self.assertEqual(self.state(c)["unknown"], sha(b"not ours"))
    def test_07_a_large_hakchi_copy_is_streamed_from_the_package_through_the_worker(self):
        """the Hakchi entry's copy of a 786,448-byte MMC3 game (larger than one message): the installer's stage step puts it by its place in
        the package; the worker reads it there, checks its SHA-256 and writes it; a copy changed on this computer is refused and nothing is
        written; the same bytes cannot go within a message; the stage's own files are removed afterwards and the package stays as it was"""
        import test_hakchi_publish as HPT
        from console_engine import hakchi_publish as HP
        from console_engine.worker_console import WorkerError
        big = HPT.rom(4, battery=True, seed=31, prg=32, chr_=32); src = big[:8] + b"\x01" + big[9:]   # iNES byte 8 set: Hakchi rewrites the header
        pk = HPT.make_package(os.path.join(self.root, "big"), [{"id": "mmc3", "title": "Large Test Game", "rom": src, "players": "1"}])
        keep = {os.path.relpath(os.path.join(r, f), pk): sha(open(os.path.join(r, f), "rb").read()) for r, _, fs in os.walk(pk) for f in fs}
        sp = HP.spec(pk); lay = HP.layout(sp, "storage", "0" * 16 + "." + "0" * 8, CI.GAMES_DIR); staged = HP.stage_files(lay); sources = HP.stage_sources(lay)
        (rel, srel), = sources.items(); want = bytes.fromhex("4e45531a202042000000000000000000") + big[16:]
        self.assertEqual((staged[rel], len(staged[rel])), (want, 786_448))
        c = self.host()
        for d in ("/var/lib/ccs", HP.INTEGRATION): c.mkdir(d)
        inst = CI.Installer(c, CI.build_plan(pk, {"mod": False}), pk, self.j); stage = f"{HP.STAGE_ROOT}/{inst.attempt}"
        inst._stage_put(stage, staged, sources)
        for r, b in staged.items(): self.assertEqual(c.sha256(f"{stage}/{r}"), sha(b), r)
        self.assertEqual(c.stat(f"{stage}/{rel}")["size"], 786_448)
        inst._stage_remove(stage, staged); self.assertEqual(c.stat(HP.STAGE_ROOT)["type"], "none", "the stage's own files and folders are removed")
        with self.assertRaises(WorkerError) as e: c.put(f"{HP.INTEGRATION}/inline.nes", want)   # no place in the package: not sent within a message
        self.assertEqual(e.exception.code, "too-large"); self.assertEqual(c.stat(f"{HP.INTEGRATION}/inline.nes")["type"], "none")
        cp_ = os.path.join(pk, *srel.split("/")); good = open(cp_, "rb").read()
        try:
            with open(cp_, "r+b") as f: f.seek(4096); f.write(bytes([good[4096] ^ 0xFF]))   # changed after the check
            with self.assertRaises(WorkerError) as e: c.put(f"{HP.INTEGRATION}/changed.nes", want, source=cp_)
            self.assertEqual(e.exception.code, "source-mismatch"); self.assertEqual(c.stat(f"{HP.INTEGRATION}/changed.nes")["type"], "none", "nothing written")
        finally:
            with open(cp_, "wb") as f: f.write(good)
        self.assertEqual({os.path.relpath(os.path.join(r, f), pk): sha(open(os.path.join(r, f), "rb").read()) for r, _, fs in os.walk(pk) for f in fs}, keep, "the package is never changed")
    def test_04_the_shipped_path_is_closed(self):   # (in the activation rehearsal, with the gate open: blocked by the test boundary instead)
        refused = gate.GateClosed if not gate.allowed("install") else console_test_guard.RealConsoleBlocked
        with self.assertRaises(refused): open_worker_console("ssh")
        with self.assertRaises(refused): CI.open_console("clovershell")

if __name__ == "__main__":
    unittest.main(verbosity=2)
