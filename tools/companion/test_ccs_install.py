"""K3 install engine (console_engine/ccs_install.py), PC only: every install, update, repair, failure, interruption, recovery, uninstall and data
export runs against a disposable folder standing in for a console that runs hakchi. No device, socket or shell is opened; a real console is
refused by Gate G1 in code. The fault cases fail every console change in turn, before it happens and after it happened (a call can change
the console and still fail), and require the stand-in to be byte for byte as it was, or the result to say exactly what is not.
  python tools/companion/test_ccs_install.py"""
from __future__ import annotations
import base64, hashlib, json, os, shutil, subprocess, sys, tempfile, unittest
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import console_test_guard   # noqa: E402,F401  (first: the controlled console boundary, k3e)
from console_engine import ccs_install as CI, gate   # noqa: E402

def sha(b): return hashlib.sha256(b).hexdigest()
def rd(p, mode="rb"):
    with open(p, mode) as f: return f.read()
def wr(p, b):
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "wb" if isinstance(b, bytes) else "w") as f: f.write(b)
def make_package(d: str, tag: str = "a", layout: str = "install", extra: dict | None = None, modes: dict | None = None) -> str:
    files = {"bin/ccs-player-armv7": b"\x7fELF\x01" + tag.encode() * 4000, "session.json": json.dumps({"schema": "ccs-play-session/1", "tag": tag}).encode(),
             "home/pack.json": b'{"schema":"ccs-native-home-pack/2"}', "roms/" + "ab" * 32 + ".nes": b"NES\x1a" + bytes(24576), "startup/startup.json": b'{"choice":"random"}',
             "config/controller-map.txt": b"key 304 a\n"} | (extra or {})
    for rel, b in files.items(): wr(os.path.join(d, *rel.split("/")), b)
    man = {"schema": "ccs-console-package/1", "layout": layout, "label": f"test {tag}", "games": 1, "playable": 1, "startup": {"choice": "random"}, "player": {"sha256": sha(files["bin/ccs-player-armv7"])},
           "console_paths": {"pkg": "/tmp/ccs/pkg"} if layout == "install" else {"pkg": "/hakchi/ccs/pkg"},
           "files": {rel: {"size": len(b), "sha256": sha(b), "kind": "x", "mode": (modes or {}).get(rel, "0o755" if rel.startswith("bin/") else "0o644")} for rel, b in files.items()}}
    wr(os.path.join(d, "MANIFEST.json"), json.dumps(man)); return d
def tree(root: str) -> dict:   # the whole stand-in, byte for byte (its link table included)
    out = {}
    for r, ds, fs in os.walk(root):
        for x in ds: q = os.path.join(r, x); out[os.path.relpath(q, root).replace("\\", "/") + "/"] = "dir"
        for f in fs: q = os.path.join(r, f); out[os.path.relpath(q, root).replace("\\", "/")] = sha(rd(q))
    return out
def changes(a: dict, b: dict) -> list: return sorted(k for k in set(a) | set(b) if a.get(k) != b.get(k))
def journal_ops(j): return [e.get("op") for e in CI.Installer.entries(j)]

class Base(unittest.TestCase):
    def setUp(self):
        self.t = tempfile.mkdtemp(prefix="ccs-k3-"); self.pkg = make_package(os.path.join(self.t, "pkg-a")); self.plan = CI.build_plan(self.pkg)
        self.root = os.path.join(self.t, "console"); CI.make_stand_in(self.root, "hakchi-ready"); self.jdir = os.path.join(self.t, "journals"); os.makedirs(self.jdir); self.n = 0
    def tearDown(self): shutil.rmtree(self.t, ignore_errors=True)
    def jnew(self): self.n += 1; return os.path.join(self.jdir, f"j{self.n}.jsonl")
    def con(self, root=None, **faults): return CI.FakeConsole(root or self.root, faults)
    def inst(self, plan=None, pkg=None, root=None, journal=None, **faults):
        return CI.Installer(self.con(root, **faults), plan or self.plan, pkg or self.pkg, journal or self.jnew())
    def pointer(self, root=None): c = self.con(root); b = c.read(CI.POINTER, 64); return b.decode().strip() if b else None
    def fork(self, name):   # a copy of the stand-in as it is now
        d = os.path.join(self.t, name); shutil.copytree(self.root, d); return d

class Plan(Base):
    def test_01_plan_is_immutable_and_named_by_its_hash(self):
        self.assertEqual(self.plan, CI.build_plan(self.pkg))
        f = CI.save_plan(self.plan, os.path.join(self.t, "plans")); self.assertTrue(f.endswith(self.plan["hash"] + ".json")); self.assertEqual(CI.load_plan(f), self.plan)
        j = json.loads(rd(f)); j["bytes"] += 1; wr(f, json.dumps(j))
        with self.assertRaises(CI.PlanError): CI.load_plan(f)
        self.assertIn("pack_install", " ".join(self.plan["never_written"]), "the plan says that hakchi's module install writes its overlay and uninstall list")
    def test_02_ram_test_changed_and_misplaced_packages_are_refused(self):
        with self.assertRaises(CI.PlanError): CI.build_plan(make_package(os.path.join(self.t, "ram"), layout="ram-test"))
        b = make_package(os.path.join(self.t, "bad")); wr(os.path.join(b, "session.json"), rd(os.path.join(b, "session.json")) + b"x")
        with self.assertRaises(CI.PlanError): CI.build_plan(b)
        m = make_package(os.path.join(self.t, "old-place")); man = json.loads(rd(os.path.join(m, "MANIFEST.json"))); man["console_paths"] = {"pkg": "/var/lib/ccs/pkg/current"}; wr(os.path.join(m, "MANIFEST.json"), json.dumps(man))
        with self.assertRaises(CI.PlanError): CI.build_plan(m)
    def test_03_package_paths_and_modes_are_validated(self):
        for i, (rel, mode) in enumerate((("../escape", None), ("roms/../../x", None), ("a//b", None), (".hidden", None), ("roms/..x", None), ("ok/file", "0o4755"), ("notbin/run", "0o755"), ("a\\b", None))):
            d = os.path.join(self.t, f"v{i}"); make_package(d)
            man = json.loads(rd(os.path.join(d, "MANIFEST.json"))); man["files"][rel] = {"size": 1, "sha256": sha(b"x"), "mode": mode or "0o644"}; wr(os.path.join(d, "MANIFEST.json"), json.dumps(man))
            if CI.REL_RE.match(rel) and ".." not in rel: wr(os.path.join(d, *rel.split("/")), b"x")
            with self.assertRaises((CI.PlanError, OSError), msg=rel): CI.build_plan(d)
        d = os.path.join(self.t, "dup"); make_package(d, extra={"Session.JSON": b"{}"})
        with self.assertRaises(CI.PlanError): CI.build_plan(d)

class Install(Base):
    def test_10_preflight_refusals_change_nothing(self):
        def case(name, setup, words):
            root = self.fork(name); setup(root); before = tree(root); r = self.inst(root=root).run()
            self.assertFalse(r["ok"], name); self.assertEqual(r["result"], "refused", (name, r)); self.assertIn(words, r["why"], name); self.assertEqual(changes(before, tree(root)), [], name)
        def cfg(**k): return lambda root: wr(os.path.join(root, "console.json"), json.dumps(json.loads(rd(os.path.join(root, "console.json"))) | k))
        case("stock", cfg(kernel="3.4.113"), "hakchi must be installed first")
        case("full", cfg(free_bytes=1000), "not enough room")
        case("foreign-current", lambda root: CI.FakeConsole(root).link(CI.POINTER, "/var/lib/ccs/pkg/someone-elses"), "not this program's pointer")
        case("unknown-pointer", lambda root: wr(os.path.join(root, "var/lib/ccs/pkg/current"), "0123456789abcdef.01234567\n"), "records do not know")
        case("root-link", lambda root: (shutil.rmtree(os.path.join(root, "var/lib/ccs")), CI.FakeConsole(root).link("/var/lib/ccs", "/var/saves")), "a link")
        case("foreign-s81", lambda root: wr(os.path.join(root, "var/lib/hakchi/rootfs/etc/init.d/S81clover-mcp"), b"#!/bin/sh\nother\n"), "another hakchi module")
        case("foreign-ccs-home", lambda root: (wr(os.path.join(root, "var/lib/hakchi/rootfs/etc/init.d/S81clover-mcp"), b"#!/bin/sh\nnot ours\n"),
                                               wr(os.path.join(root, "var/lib/hakchi/hmod/uninstall-ccs-home"), 'rm -f "$rootfs/./etc/init.d/S81clover-mcp"\n')), "did not make")
        case("damaged-record", lambda root: wr(os.path.join(root, "var/lib/ccs/install/v-0123456789abcdef.01234567.json"), b"{not json"), "cannot all be read")
        case("pending-attempt", lambda root: wr(os.path.join(root, "var/lib/ccs/install/attempt-0badc0de.json"), json.dumps({"schema": "ccs-install-attempt/1", "attempt": "0badc0de"})), "did not finish")
        case("other-saves", lambda root: wr(os.path.join(root, "var/lib/ccs/data/IDENTITY.json"), json.dumps({"schema": "ccs-console-data/1", "state_identity": {"platform": "windows-x86_64", "core": "fceumm", "core_version": "ac0d00a7"}})), "another player build")
    def test_11_install_places_verifies_and_keeps_everything_else(self):
        before = tree(self.root); r = self.inst().run(); self.assertTrue(r["ok"], r); self.assertEqual(r["result"], "installed")
        copy = r["copy"]; self.assertRegex(copy, CI.COPY_RE); self.assertEqual(self.pointer(), copy); c = self.con()
        for f in self.plan["files"]: self.assertEqual(c.sha256(f"{CI.PKG_DIR}/{copy}/{f['path']}"), f["sha256"])
        self.assertEqual(rd(os.path.join(self.root, "var/lib/hakchi/rootfs/etc/init.d/S81clover-mcp")).decode(), CI.S81_OVERRIDE)
        self.assertIn("exec /usr/bin/clover-mcp", CI.S81_OVERRIDE)
        ch = changes(before, tree(self.root))
        self.assertTrue(all(k.startswith(("var/lib/ccs/pkg/" + copy, "var/lib/ccs/pkg/current", "var/lib/ccs/install", "var/lib/ccs/data", "var/lib/hakchi/rootfs/etc", "var/lib/hakchi/hmod/uninstall-ccs-home")) for k in ch), ch)
        self.assertEqual(CI.stock_digest(self.root), {rel: sha(b) for rel, b in CI.STOCK_SAMPLE.items()}, "stock games, saves, the stock script, hakchi's other module and the unknown folder unchanged")
        recs = CI.read_records(c); self.assertEqual(list(recs["versions"]), [copy]); self.assertEqual(recs["attempts"], {}, "the attempt record is closed"); self.assertEqual(len(recs["modules"]), 1)
        self.assertTrue(r["record_closed"]); self.assertIn("/var/lib/ccs/pkg/someone-elses", r["kept_changed_or_unknown"])
    def test_12_the_same_plan_again_changes_nothing(self):
        self.assertTrue(self.inst().run()["ok"]); before = tree(self.root); c = self.con()
        r = CI.Installer(c, self.plan, self.pkg, self.jnew()).run(); self.assertEqual(r["result"], "no-change"); self.assertEqual(c.writes, []); self.assertEqual(changes(before, tree(self.root)), [])
    def test_13_update_keeps_data_and_tidies_only_what_it_owns(self):
        a = self.inst().run(); self.assertTrue(a["ok"])
        wr(os.path.join(self.root, "var/lib/ccs/data/saves/x/slot1.ccsstate"), b"my slot"); wr(os.path.join(self.root, "var/lib/ccs/pkg", a["copy"], "owner-notes.txt"), b"KEEP")
        pkg_b = make_package(os.path.join(self.t, "pkg-b"), "b"); plan_b = CI.build_plan(pkg_b); r = self.inst(plan_b, pkg_b).run(); self.assertTrue(r["ok"], r)
        self.assertEqual(self.pointer(), r["copy"]); self.assertEqual(rd(os.path.join(self.root, "var/lib/ccs/data/saves/x/slot1.ccsstate")), b"my slot")
        old = os.path.join(self.root, "var/lib/ccs/pkg", a["copy"]); self.assertEqual(sorted(os.listdir(old)), ["owner-notes.txt"], "the earlier copy's own files removed, the owner's file kept")
        self.assertIn(f"/var/lib/ccs/pkg/{a['copy']}/owner-notes.txt", r["kept_changed_or_unknown"])
    def test_14_repair_never_touches_the_copy_in_use_until_the_switch(self):
        a = self.inst().run(); live = os.path.join(self.root, "var/lib/ccs/pkg", a["copy"]); s = os.path.join(live, "session.json"); wr(s, rd(s) + b" ")
        snap = tree(self.root); n = len(self.plan["files"]) + 4
        r = self.inst(before=n).run(); self.assertFalse(r["ok"]); self.assertTrue(r["rolled_back"]["ok"], r); self.assertEqual(changes(snap, tree(self.root)), [], "a failed repair leaves everything as it was")
        r = self.inst().run(); self.assertTrue(r["ok"], r); self.assertNotEqual(r["copy"], a["copy"]); self.assertEqual(self.pointer(), r["copy"])
        self.assertEqual(sorted(os.listdir(live)), ["session.json"], "the changed file of the earlier copy is kept, its unchanged files removed")
        self.assertIn(f"/var/lib/ccs/pkg/{a['copy']}/session.json", r["kept_changed_or_unknown"])

class Faults(Base):
    """every console change of a first install, an update and a repair fails in turn, before and after it happens"""
    def count(self, setup):
        root = self.fork("count"); setup(root); c = self.con(root); r = CI.Installer(c, *self.args(root), self.jnew()).run(); self.assertTrue(r["ok"], r)
        es = CI.Installer.entries(self.j_last()); return c.nchanges
    def j_last(self): return os.path.join(self.jdir, f"j{self.n}.jsonl")
    def args(self, root): return self.plan_x, self.pkg_x
    def sweep(self, label, setup):
        total = self.count(setup); seen = {"rolled": 0, "installed": 0}
        for n in range(1, total + 1):
            for kind in ("before", "after"):
                with self.subTest(case=label, change=n, kind=kind):
                    root = self.fork(f"{label}-{kind}-{n}"); setup(root); before = tree(root); j = self.jnew()
                    r = CI.Installer(self.con(root, **{kind: n}), *self.args(root), j).run()
                    if "installed" in journal_ops(j):
                        seen["installed"] += 1; self.assertTrue(r["ok"], r); self.assertEqual(r["result"], "installed")
                        if CI.read_records(self.con(root))["attempts"]:   # the record could not be closed: the next run refuses until that attempt is finished
                            self.assertEqual(self.inst(*self.args(root), root=root).run()["result"], "refused")
                            fin = CI.Installer(self.con(root), *self.args(root), j).resume(); self.assertEqual(fin["result"], "installed", fin)
                            self.assertEqual(CI.read_records(self.con(root))["attempts"], {})
                    else:
                        seen["rolled"] += 1; self.assertFalse(r["ok"]); self.assertTrue(r["rolled_back"]["ok"], r)
                        self.assertEqual(changes(before, tree(root)), [], f"{label}: change {n} {kind}: the stand-in is exactly as before")
                    shutil.rmtree(root, ignore_errors=True)
        self.assertGreater(seen["rolled"], 10); self.assertGreater(seen["installed"], 0)
    def test_20_first_install(self):
        self.plan_x, self.pkg_x = self.plan, self.pkg; self.sweep("first", lambda root: None)
    def test_21_update(self):
        self.assertTrue(self.inst().run()["ok"]); pkg_b = make_package(os.path.join(self.t, "pkg-b"), "b"); self.plan_x, self.pkg_x = CI.build_plan(pkg_b), pkg_b; self.sweep("update", lambda root: None)
    def test_22_repair_and_an_older_module(self):
        self.assertTrue(self.inst().run()["ok"]); copy = self.pointer()
        s = os.path.join(self.root, "var/lib/ccs/pkg", copy, "session.json"); wr(s, rd(s) + b" ")
        old = b"#!/bin/sh\n# an earlier Classic Cart Studio start-up\nexec /usr/bin/clover-mcp\n"   # an earlier module of this program (recorded)
        wr(os.path.join(self.root, "var/lib/hakchi/rootfs/etc/init.d/S81clover-mcp"), old)
        wr(os.path.join(self.root, f"var/lib/ccs/install/module-{sha(old)[:16]}.json"), json.dumps({"schema": "ccs-owned-module/1", "name": "ccs-home", "path": CI.HOOK_FILE, "sha256": sha(old)}))
        self.plan_x, self.pkg_x = self.plan, self.pkg; self.sweep("repair", lambda root: None)
        r = self.inst().run(); self.assertTrue(r["ok"]); self.assertEqual(self.con().overlay_sha256(CI.HOOK_FILE), CI.module_sha())
    def test_23_a_crash_after_any_change_is_recovered_from_that_attempts_journal(self):
        self.assertTrue(self.inst().run()["ok"]); pkg_b = make_package(os.path.join(self.t, "pkg-b"), "b"); plan_b = CI.build_plan(pkg_b)
        self.plan_x, self.pkg_x = plan_b, pkg_b; total = self.count(lambda root: None)
        for n in range(1, total + 1):
            with self.subTest(change=n):
                root = self.fork(f"crash-{n}"); before = tree(root); j = self.jnew()
                with self.assertRaises(SystemExit): CI.Installer(self.con(root, crash_after=n), plan_b, pkg_b, j).run()
                if CI.read_records(self.con(root))["attempts"]: self.assertEqual(self.inst(plan_b, pkg_b, root=root).run()["result"], "refused", "a new attempt waits for the interrupted one")
                r = CI.Installer(self.con(root), plan_b, pkg_b, j).resume()
                if "installed" in journal_ops(j): self.assertEqual(r["result"], "installed", r)
                else: self.assertEqual(r["result"], "rolled-back", r); self.assertEqual(changes(before, tree(root)), [], f"crash after change {n}")
                self.assertEqual(CI.read_records(self.con(root))["attempts"], {})
                self.assertEqual(CI.Installer(self.con(root), plan_b, pkg_b, j).resume()["result"], "nothing to resume", "a finished recovery is never repeated")
                shutil.rmtree(root, ignore_errors=True)

class Recovery(Base):
    def test_30_a_journal_is_never_reused_and_a_finished_attempt_never_undone(self):
        j = self.jnew(); self.assertTrue(CI.Installer(self.con(), self.plan, self.pkg, j).run()["ok"]); snap = tree(self.root)
        r = CI.Installer(self.con(), self.plan, self.pkg, j).run(); self.assertEqual(r["result"], "refused"); self.assertIn("never reused", r["why"])
        r = CI.Installer(self.con(), self.plan, self.pkg, j).resume(); self.assertEqual(r["result"], "nothing to resume"); self.assertEqual(changes(snap, tree(self.root)), [])
    def test_31_recovery_is_bound_to_its_attempt_plan_and_console(self):
        j = self.jnew()
        with self.assertRaises(SystemExit): CI.Installer(self.con(crash_after=6), self.plan, self.pkg, j).run()
        rec = [n for n in os.listdir(os.path.join(self.root, "var/lib/ccs/install")) if n.startswith("attempt-")][0]; p = os.path.join(self.root, "var/lib/ccs/install", rec)
        good = rd(p); bad = json.loads(good); bad["plan"] = "0" * 64; wr(p, json.dumps(bad)); snap = tree(self.root)
        r = CI.Installer(self.con(), self.plan, self.pkg, j).resume(); self.assertEqual(r["result"], "refused"); self.assertEqual(changes(snap, tree(self.root)), [])
        wr(p, good); other = make_package(os.path.join(self.t, "pkg-other"), "o")
        self.assertEqual(CI.Installer(self.con(), CI.build_plan(other), other, j).resume()["result"], "refused", "another plan's recovery does not use this journal")
        self.assertEqual(CI.Installer(self.con(), self.plan, self.pkg, j).resume()["result"], "rolled-back")
    def test_32_a_real_console_stops_and_never_rolls_back_by_itself(self):
        before = tree(self.root); j = self.jnew(); i = CI.Installer(self.con(after=7), self.plan, self.pkg, j); i.auto_rollback = False   # the physical policy
        r = i.run(); self.assertEqual(r["result"], "stopped"); self.assertIsNone(r["rolled_back"]); self.assertIn("G3", r["next"])
        self.assertNotEqual(changes(before, tree(self.root)), [], "left as it was when it stopped"); self.assertTrue(CI.read_records(self.con())["attempts"])
        r = CI.Installer(self.con(), self.plan, self.pkg, j).resume(); self.assertEqual(r["result"], "rolled-back"); self.assertEqual(changes(before, tree(self.root)), [])
    def test_33_a_failed_rollback_is_reported_not_claimed(self):
        class Flaky(CI.FakeConsole):
            def rmdir(self, path): raise ConnectionError("the console stopped answering")
            def remove(self, path, h):
                if "/install/" in path: raise ConnectionError("the console stopped answering")
                return super().remove(path, h)
        r = CI.Installer(Flaky(self.root, {"before": 12}), self.plan, self.pkg, self.jnew()).run()
        self.assertFalse(r["ok"]); self.assertFalse(r["rolled_back"]["ok"]); self.assertIn("no further writes", r["rolled_back"]["next"]); self.assertTrue(r["rolled_back"]["problems"])
    def test_34_cancel_before_the_switch_rolls_back(self):
        before = tree(self.root); calls = {"n": 0}
        def cancel(): calls["n"] += 1; return calls["n"] > 3
        r = CI.Installer(self.con(), self.plan, self.pkg, self.jnew(), cancel=cancel).run(); self.assertEqual(r["result"], "cancelled"); self.assertTrue(r["rolled_back"]["ok"]); self.assertEqual(changes(before, tree(self.root)), [])

class Uninstall(Base):
    def test_40_uninstall_shows_what_goes_and_what_stays_then_restores_stock_start(self):
        pristine = tree(self.root); a = self.inst().run(); self.assertTrue(a["ok"])
        wr(os.path.join(self.root, "var/lib/ccs/data/saves/x/cart.sav"), b"battery"); wr(os.path.join(self.root, "var/lib/ccs/pkg", a["copy"], "owner-notes.txt"), b"KEEP")
        wr(os.path.join(self.root, "var/lib/ccs/pkg/0123456789abcdef.staging/owner.txt"), b"KEEP")
        p = CI.uninstall_plan(self.con()); self.assertTrue(p["ok"]); self.assertTrue(any("start-up module" in x for x in p["remove"]))
        self.assertTrue(any("owner-notes.txt" in x for x in p["keep"]) and any("0123456789abcdef.staging" in x for x in p["keep"]) and any("saves" in x for x in p["keep"]), p)
        r = CI.uninstall(self.con()); self.assertTrue(r["ok"], r); self.assertEqual(r["data"], "kept")
        self.assertIsNone(self.con().overlay_sha256(CI.HOOK_FILE)); self.assertFalse(os.path.exists(os.path.join(self.root, "var/lib/hakchi/hmod/uninstall-ccs-home")))
        self.assertEqual(rd(os.path.join(self.root, "var/lib/ccs/data/saves/x/cart.sav")), b"battery"); self.assertTrue(os.path.isfile(os.path.join(self.root, "var/lib/ccs/pkg", a["copy"], "owner-notes.txt")))
        self.assertTrue(os.path.isfile(os.path.join(self.root, "var/lib/ccs/pkg/0123456789abcdef.staging/owner.txt"))); self.assertEqual(CI.stock_digest(self.root), {k: sha(v) for k, v in CI.STOCK_SAMPLE.items()})
        self.assertIn("Nintendo's HOME", r["after_restart"])
        r = CI.uninstall(self.con(), keep_data=False); self.assertTrue(r["ok"]); self.assertFalse(os.path.exists(os.path.join(self.root, "var/lib/ccs/data")))
    def test_41_uninstall_refuses_a_module_it_did_not_make(self):
        self.assertTrue(self.inst().run()["ok"]); wr(os.path.join(self.root, "var/lib/hakchi/rootfs/etc/init.d/S81clover-mcp"), b"#!/bin/sh\nchanged by someone\n"); snap = tree(self.root)
        r = CI.uninstall(self.con()); self.assertEqual(r["result"], "refused"); self.assertEqual(changes(snap, tree(self.root)), [])
    def test_42_an_interrupted_uninstall_is_finished_by_running_it_again(self):
        self.assertTrue(self.inst().run()["ok"]); base = self.fork("installed"); c = self.con(self.fork("count-u")); self.assertTrue(CI.uninstall(c)["ok"]); total = c.nchanges
        for n in range(1, total + 1):
            for kind in ("before", "after"):
                with self.subTest(change=n, kind=kind):
                    root = os.path.join(self.t, f"u-{kind}-{n}"); shutil.copytree(base, root)
                    try: CI.uninstall(self.con(root, **{kind: n}))
                    except (ConnectionError, OSError): pass
                    r = CI.uninstall(self.con(root)); self.assertTrue(r["ok"], r)
                    self.assertIsNone(self.con(root).overlay_sha256(CI.HOOK_FILE)); self.assertEqual(CI.stock_digest(root), {k: sha(v) for k, v in CI.STOCK_SAMPLE.items()})
                    shutil.rmtree(root, ignore_errors=True)
    def test_43_data_export_copies_verifies_and_never_follows_a_link(self):
        self.assertTrue(self.inst().run()["ok"]); c = self.con()
        wr(os.path.join(self.root, "var/lib/ccs/data/saves/ab/slot1.ccsstate"), b"CCSSTATE" + bytes(300)); wr(os.path.join(self.root, "var/lib/ccs/data/game-settings/ab.json"), b"{}")
        c.link("/var/lib/ccs/data/saves/escape", "/var/saves")
        # k3c: a link left out makes the export partial, not ok (it was ok with the skip listed before)
        out = os.path.join(self.t, "export"); r = CI.export_data(c, out); self.assertFalse(r["ok"], r); self.assertEqual(r["status"], "partial"); self.assertEqual(r["files"], 3)
        self.assertTrue(any(s["path"] == "saves/escape" for s in r["skipped"])); self.assertTrue(CI.verify_export(out)["ok"])
        man = json.loads(rd(os.path.join(out, "EXPORT.json"))); self.assertEqual(man["schema"], "ccs-console-data-export/2"); self.assertEqual(man["identity"]["state_identity"], CI.STATE_IDENTITY)
        wr(os.path.join(out, "data/saves/ab/slot1.ccsstate"), b"tampered"); self.assertFalse(CI.verify_export(out)["ok"])

class Gate(Base):
    def test_50_a_real_console_is_refused_by_the_gate_in_code(self):
        self.assertFalse(gate.PHYSICAL_WRITE_ENABLED); self.assertFalse(gate.PHYSICAL_INSPECT_ENABLED)
        for kind in ("ssh", "clovershell", "fel", "usb", "rndis"):
            with self.assertRaises(gate.GateClosed): CI.open_console(kind, "169.254.13.37")
        class Real(CI.FakeConsole): simulated = False
        r = Real(self.root)
        with self.assertRaises(gate.GateClosed): CI.Installer(r, self.plan, self.pkg, self.jnew())
        with self.assertRaises(gate.GateClosed): CI.uninstall(r)
        with self.assertRaises(gate.GateClosed): CI.export_data(r, os.path.join(self.t, "x"))
        with self.assertRaises(gate.GateClosed): CI.Installer.resume(type("I", (), {"c": r})())

class Launcher(Base):
    """the start-up script under the console's own BusyBox 1.24.1 (qemu-arm in WSL), paths moved into a disposable folder"""
    BB = "/mnt/c/NES/work/h0/rootfs"
    def test_60_start_up_runs_the_copy_named_by_the_pointer_and_always_ends_in_clover_mcp(self):
        qemu = "$HOME/.cache/ccs-sim/toolchain/deb/usr/bin/qemu-arm"   # inside WSL (script files: wsl.exe re-expands command lines)
        work = "/mnt/c/NES/work/cs/launcher-test"; lw = r"C:\NES\work\cs\launcher-test"; shutil.rmtree(lw, ignore_errors=True); os.makedirs(lw)
        wr(os.path.join(lw, "probe.sh"), f"test -x {qemu} && test -f {self.BB}/bin/busybox && echo yes\n".encode())
        try: probe = subprocess.run(["wsl.exe", "-d", "Ubuntu", "--", "bash", f"{work}/probe.sh"], capture_output=True, timeout=60)
        except (OSError, subprocess.TimeoutExpired): probe = None
        if not probe or b"yes" not in probe.stdout.replace(b"\0", b""): self.skipTest("SKIPPED: WSL, qemu-arm or the console's BusyBox is not available (not a pass)")
        wr(os.path.join(lw, "S81"), CI.S81_OVERRIDE.encode())
        script = f"""#!/bin/bash
set -u; Q={qemu}; B="$Q -L {self.BB} {self.BB}/bin/busybox"; S={work}/S81
run() {{  # case-name setup
  R=$(mktemp -d /tmp/k3-launch-XXXX); mkdir -p $R/var/lib/ccs/pkg $R/usr/bin $R/tmp
  printf '#!/bin/sh\\necho CLOVER >> %s\\n' "{work}/out-$1" > $R/usr/bin/clover-mcp; chmod +x $R/usr/bin/clover-mcp
  C=0123456789abcdef.89abcdef; mkdir -p $R/var/lib/ccs/pkg/$C/bin; echo '{{}}' > $R/var/lib/ccs/pkg/$C/session.json
  printf '#!/bin/sh\\necho "PLAYER $*" >> %s\\nexit 3\\n' "{work}/out-$1" > $R/var/lib/ccs/pkg/$C/bin/ccs-player-armv7; chmod +x $R/var/lib/ccs/pkg/$C/bin/ccs-player-armv7
  eval "$2"
  sed -e "s#/var/lib/ccs#$R/var/lib/ccs#g" -e "s#/tmp/ccs#$R/tmp/ccs#g" -e "s#/usr/bin/clover-mcp#$R/usr/bin/clover-mcp#g" $S > $R/S81
  : > {work}/out-$1; $B sh $R/S81 start > {work}/log-$1 2>&1; echo "rc=$?" >> {work}/log-$1; rm -rf $R; }}
run good 'echo $C > $R/var/lib/ccs/pkg/current'
run none ':'
run link 'ln -s $R/var/lib/ccs/pkg/$C $R/var/lib/ccs/pkg/current'
run garbage 'echo "../../etc" > $R/var/lib/ccs/pkg/current'
run missing 'echo fedcba9876543210.89abcdef > $R/var/lib/ccs/pkg/current'
run linkedcopy 'mv $R/var/lib/ccs/pkg/$C $R/var/lib/ccs/pkg/real; ln -s $R/var/lib/ccs/pkg/real $R/var/lib/ccs/pkg/$C; echo $C > $R/var/lib/ccs/pkg/current'
"""
        wr(os.path.join(lw, "run.sh"), script.encode()); subprocess.run(["wsl.exe", "-d", "Ubuntu", "--", "bash", f"{work}/run.sh"], capture_output=True, env=dict(os.environ, MSYS_NO_PATHCONV="1"), timeout=300)
        out = {k: rd(os.path.join(lw, f"out-{k}"), "r") for k in ("good", "none", "link", "garbage", "missing", "linkedcopy")}
        self.assertIn("PLAYER --session ", out["good"]); self.assertIn("/tmp/ccs/pkg/session.json --trace", out["good"]); self.assertTrue(out["good"].rstrip().endswith("CLOVER"), out["good"])
        for k in ("none", "link", "garbage", "missing", "linkedcopy"): self.assertEqual(out[k].strip(), "CLOVER", (k, out[k], rd(os.path.join(lw, f"log-{k}"), "r")))
        shutil.rmtree(lw, ignore_errors=True)

if __name__ == "__main__":
    unittest.main(verbosity=2)
