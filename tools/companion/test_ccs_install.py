"""K3 install engine (console_engine/ccs_install.py), PC only: every install, update, repair, failure, interruption, recovery, uninstall and data
export runs against a disposable folder standing in for a console that runs hakchi. No device, socket or shell is opened; a real console is
reached only through the console transport worker, which the test boundary blocks here. The fault cases fail every console change in turn, before it happens and after it happened (a call can change
the console and still fail), and require the stand-in to be byte for byte as it was, or the result to say exactly what is not.
  python tools/companion/test_ccs_install.py"""
from __future__ import annotations
import atexit, base64, hashlib, json, os, shutil, subprocess, sys, tempfile, unittest
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import console_test_guard   # noqa: E402,F401  (first: the controlled console boundary, k3e)
from console_engine import ccs_install as CI, gate   # noqa: E402
# the number of earlier CCS HOME versions to keep is read from CCS Console Workbench's settings (ccs_install.earlier_setting): in tests, from a
# file that is never written (the default) unless a test sets its own; never this PC's own settings
os.environ.setdefault("CCS_WORKBENCH_SETTINGS", os.path.join(tempfile.gettempdir(), f"ccs-tests-no-workbench-settings-{os.getpid()}.json"))

def sha(b): return hashlib.sha256(b).hexdigest()
def rd(p, mode="rb"):
    with open(p, mode) as f: return f.read()
def wr(p, b):
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "wb" if isinstance(b, bytes) else "w") as f: f.write(b)
def chooser_standin(d: str) -> dict:
    """the startup chooser package's own inputs for stand-in consoles: a stand-in program and one stand-in menu font with its license file (the
    real program and fonts are what the app ships; tools/console-nucleus/test_nucleus.py runs the chooser itself under qemu-arm)"""
    wr(os.path.join(d, "ccs-home-armv7"), b"\x7fELF\x01 stand-in chooser program for the tests")
    wr(os.path.join(d, "fonts", "silk17.ccsf"), b"CCSF stand-in font for the tests"); wr(os.path.join(d, "fonts", "OFL-Stand-in.txt"), b"stand-in license file for the tests\n")
    return {"program": os.path.join(d, "ccs-home-armv7"), "fonts": os.path.join(d, "fonts")}
CHOOSER_IN = tempfile.mkdtemp(prefix="ccs-chooser-in-"); atexit.register(shutil.rmtree, CHOOSER_IN, True)
CI.CHOOSER_INPUTS = chooser_standin(CHOOSER_IN)   # every stand-in console below builds the chooser package from these
def chooser_env() -> dict:   # the same inputs for a worker or backend process started by a test
    return {"CCS_CHOOSER_PROGRAM": CI.CHOOSER_INPUTS["program"], "CCS_CHOOSER_FONTS": CI.CHOOSER_INPUTS["fonts"]}
def registry_line(root: str) -> str | None: return CI.nucleus_start_state(CI.FakeConsole(root)).get("line")

def make_package(d: str, tag: str = "a", layout: str = "mod", extra: dict | None = None, modes: dict | None = None) -> str:
    files = {"bin/ccs-player-armv7": b"\x7fELF\x01" + tag.encode() * 4000, "session.json": json.dumps({"schema": "ccs-play-session/1", "tag": tag}).encode(),
             "home/pack.json": b'{"schema":"ccs-native-home-pack/2"}', "roms/" + "ab" * 32 + ".nes": b"NES\x1a" + bytes(24576), "startup/startup.json": b'{"choice":"random"}',
             "config/controller-map.txt": b"key 304 a\n"} | (extra or {})
    for rel, b in files.items(): wr(os.path.join(d, *rel.split("/")), b)
    man = {"schema": "ccs-console-package/1", "layout": layout, "label": f"test {tag}", "games": 1, "playable": 1, "startup": {"choice": "random"}, "player": {"sha256": sha(files["bin/ccs-player-armv7"])},
           "console_paths": {"pkg": "/tmp/ccs/pkg"} if layout in ("install", "mod", "nucleus") else {"pkg": "/hakchi/ccs/pkg"},
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
        self.t = tempfile.mkdtemp(prefix="ccs-k3-"); os.environ["CCS_WORKBENCH_SETTINGS"] = os.path.join(self.t, "workbench-settings.json")   # never this PC's own settings
        self.pkg = make_package(os.path.join(self.t, "pkg-a")); self.plan = CI.build_plan(self.pkg)
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
        self.assertIn("never removed", " ".join(self.plan["never_written"]), "the plan says that an add-on the console has is never removed")
    def test_02_ram_test_changed_and_misplaced_packages_are_refused(self):
        with self.assertRaises(CI.PlanError): CI.build_plan(make_package(os.path.join(self.t, "ram"), layout="ram-test"))
        with self.assertRaises(CI.PlanError) as e: CI.build_plan(make_package(os.path.join(self.t, "s81"), layout="install"))
        self.assertIn("removes at every start", str(e.exception), "a package for the earlier start-up module is refused: hakchi deletes that start file at every start")
        self.assertEqual(self.plan["layout"], "mod"); self.assertEqual(self.plan["addons"], CI.STUDIO_CHOICE, "Studio's default: CCS Mod, no CCS Nucleus")
        self.assertEqual({a: x["name"] for a, x in self.plan["packages"].items()}, {"mod": "ccs-mod"}); self.assertIsNone(self.plan["start_line"]); self.assertNotIn("start", self.plan["steps"])
        with self.assertRaises(CI.PlanError) as e: CI.build_plan(make_package(os.path.join(self.t, "nu"), layout="nucleus"))
        self.assertIn("prepare the HOME menu again", str(e.exception), "there is one CCS HOME layout: a package made for another is refused")
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
        case("foreign-ccs-home", lambda root: (wr(os.path.join(root, "var/lib/hakchi/rootfs/etc/init.d/S81clover-mcp"), b"#!/bin/sh\nnot ours\n"),
                                               wr(os.path.join(root, "var/lib/hakchi/hmod/uninstall-ccs-home"), 'rm -f "$rootfs/./etc/init.d/S81clover-mcp"\n')), "did not make")
        nuc = CI.addon_package("nucleus"); mod = CI.addon_package("mod"); cho = CI.addon_package("nucleus-chooser")
        case("other-addon", lambda root: CI.FakeConsole(root).pack_install(nuc["name"], nuc["hmod"]), CI.REMOVES_TEXT)   # Studio's default choice would take CCS Nucleus away
        def changed(root): CI.FakeConsole(root).pack_install(mod["name"], mod["hmod"]); wr(os.path.join(root, "var/lib/hakchi/rootfs/bin/ccs-mod"), b"#!/bin/sh\n# changed by someone\n")
        case("changed-method", changed, "were changed")
        def unknown(root): CI.FakeConsole(root).pack_install(mod["name"], mod["hmod"]); f = os.path.join(root, "var/lib/hakchi/rootfs/etc/ccs/mod/VERSION"); wr(f, rd(f).replace(mod["package_id"].encode(), b"not-a-package-record"))
        case("unknown-method-package", unknown, "does not recognize")
        case("program-without-package", lambda root: wr(os.path.join(root, "var/lib/hakchi/rootfs/bin/ccs-mod"), b"#!/bin/sh\n"), "hakchi does not list its package")
        case("chooser-program-without-package", lambda root: wr(os.path.join(root, "var/lib/hakchi/rootfs/etc/ccs/nucleus/chooser/bin/ccs-home-armv7"), b"\x7fELF"), "hakchi does not list its package")
        def chooser_alone(root): c = CI.FakeConsole(root); c.pack_install(nuc["name"], nuc["hmod"]); c.pack_install(cho["name"], cho["hmod"]); c.pack_uninstall(nuc["name"])
        case("chooser-without-nucleus", chooser_alone, "does not recognize")   # CCS Nucleus's removal took its chooser's files and hakchi still lists the chooser
        def bad_registry(root): CI.FakeConsole(root).pack_install(nuc["name"], nuc["hmod"]); wr(os.path.join(root, "var/lib/ccs/nucleus/registry"), b"schema=ccs-boot-registry/1\ndefault=native\nend=0\n")
        case("nucleus-settings-unreadable", bad_registry, "CCS Nucleus's settings on this console cannot be read")
        case("damaged-record", lambda root: wr(os.path.join(root, "var/lib/ccs/install/v-0123456789abcdef.01234567.json"), b"{not json"), "cannot all be read")
        case("pending-attempt", lambda root: wr(os.path.join(root, "var/lib/ccs/install/attempt-0badc0de.json"), json.dumps({"schema": "ccs-install-attempt/1", "attempt": "0badc0de"})), "did not finish")
        case("other-saves", lambda root: wr(os.path.join(root, "var/lib/ccs/data/IDENTITY.json"), json.dumps({"schema": "ccs-console-data/1", "state_identity": {"platform": "windows-x86_64", "core": "fceumm", "core_version": "ac0d00a7"}})), "another player build")
    def test_11_install_places_verifies_and_keeps_everything_else(self):
        before = tree(self.root); r = self.inst().run(); self.assertTrue(r["ok"], r); self.assertEqual(r["result"], "installed")
        copy = r["copy"]; self.assertRegex(copy, CI.COPY_RE); self.assertEqual(self.pointer(), copy); c = self.con()
        for f in self.plan["files"]: self.assertEqual(c.sha256(f"{CI.PKG_DIR}/{copy}/{f['path']}"), f["sha256"])
        mp = CI.addon_package("mod"); mf = c.method_files("ccs-mod")
        self.assertEqual(mf["package"], mp["package_id"]); self.assertEqual({p: s for p, s in mf["files"].items()}, mp["files"], "CCS Mod's package installed through hakchi's pack_install: every file it lists, unchanged")
        self.assertFalse(os.path.exists(os.path.join(self.root, "var/lib/hakchi/rootfs/etc/init.d/S81clover-mcp")), "no start file that hakchi deletes at every start is written")
        self.assertTrue(rd(os.path.join(self.root, "var/lib/hakchi/hmod/uninstall-ccs-mod")).startswith(b"# CCS Mod for Hakchi: removal"), "hakchi keeps the package's own removal ahead of its generated list")
        ch = changes(before, tree(self.root))
        self.assertTrue(all(k.startswith(("var/lib/ccs/pkg/" + copy, "var/lib/ccs/pkg/current", "var/lib/ccs/install", "var/lib/ccs/data", "var/lib/hakchi/rootfs/bin/ccs-mod", "var/lib/hakchi/rootfs/etc/ccs", "var/lib/hakchi/hmod/uninstall-ccs-mod"))
                            or k in ("var/lib/hakchi/rootfs/bin/", "var/lib/hakchi/rootfs/etc/") for k in ch), ch)
        self.assertEqual(CI.stock_digest(self.root), {rel: sha(b) for rel, b in CI.STOCK_SAMPLE.items()}, "stock games, saves, the stock script, hakchi's other module and the unknown folder unchanged")
        recs = CI.read_records(c); self.assertEqual(list(recs["versions"]), [copy]); self.assertEqual(recs["attempts"], {}, "the attempt record is closed"); self.assertEqual(len(recs["modules"]), 1)
        self.assertTrue(r["record_closed"]); self.assertIn("/var/lib/ccs/pkg/someone-elses", r["kept_changed_or_unknown"])
    def test_12_the_same_plan_again_changes_nothing(self):
        self.assertTrue(self.inst().run()["ok"]); before = tree(self.root); c = self.con()
        r = CI.Installer(c, self.plan, self.pkg, self.jnew()).run(); self.assertEqual(r["result"], "no-change"); self.assertEqual(c.writes, []); self.assertEqual(changes(before, tree(self.root)), [])
    def test_13_update_keeps_data_and_the_earlier_versions_the_setting_keeps(self):
        a = self.inst().run(); self.assertTrue(a["ok"])
        wr(os.path.join(self.root, "var/lib/ccs/data/saves/x/slot1.ccsstate"), b"my slot")
        pkg_b = make_package(os.path.join(self.t, "pkg-b"), "b"); plan_b = CI.build_plan(pkg_b); r = self.inst(plan_b, pkg_b).run(); self.assertTrue(r["ok"], r)
        self.assertEqual(plan_b["earlier_home_versions"], 1, "one earlier version by default")
        self.assertEqual(self.pointer(), r["copy"]); self.assertEqual(rd(os.path.join(self.root, "var/lib/ccs/data/saves/x/slot1.ccsstate")), b"my slot")
        old = os.path.join(self.root, "var/lib/ccs/pkg", a["copy"]); self.assertIn("MANIFEST.json", os.listdir(old), "the default keeps the copy before, whole")
        self.assertEqual(r["preflight"]["earlier"]["remove"], []); self.assertEqual(json.loads(rd(os.path.join(self.root, f"var/lib/ccs/install/v-{r['copy']}.json")))["previous"], a["copy"])
        vr = json.loads(rd(os.path.join(self.root, f"var/lib/ccs/install/v-{r['copy']}.json")))
        self.assertRegex(vr["installed_utc"], r"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$", "the copy record says when it was installed (this PC's time)")
        rv = {x["copy"]: x for x in r["preflight"]["earlier"]["copies"]}
        self.assertTrue(rv[a["copy"]]["in_use_now"] and rv[r["copy"]]["in_use"] and rv[r["copy"]]["new"] and rv[a["copy"]]["installed_utc"] is not None and rv[a["copy"]]["allocated_bytes"] is None,
                        ("the review: the copy in use now with its date, the new copy, and the space on the storage not measured here (unknown)", rv))
        # keep none: the clean copy before goes once the new one is in use and read back; a copy with a file the owner added stays whole
        wr(os.path.join(old, "owner-notes.txt"), b"KEEP"); mid = os.path.join(self.root, "var/lib/ccs/pkg", r["copy"])
        pkg_c = make_package(os.path.join(self.t, "pkg-c"), "c"); plan_c = CI.build_plan(pkg_c, earlier=0); rc = self.inst(plan_c, pkg_c).run(); self.assertTrue(rc["ok"], rc)
        self.assertEqual(sorted(rc["preflight"]["earlier"]["remove"]), sorted([r["copy"], a["copy"]]), "the review names both earlier copies (each is checked again before it goes)")
        self.assertFalse(os.path.exists(mid), "keep none: the clean earlier copy removed")
        self.assertIn("owner-notes.txt", os.listdir(old)); self.assertIn("MANIFEST.json", os.listdir(old), "a changed earlier copy is kept whole, never in part")
        ear = next(t["earlier"] for t in rc["tidy"] if "earlier" in t); self.assertEqual(ear["why"][a["copy"]], "changed since it was installed")
        self.assertTrue(any(t.get("copy") == r["copy"] and t.get("removed") and "Console Restore" in t.get("restore", "") for t in rc["tidy"]))
        self.assertEqual(rd(os.path.join(self.root, "var/lib/ccs/data/saves/x/slot1.ccsstate")), b"my slot", "saves untouched by the clean-up")
    def test_13b_the_setting_comes_from_the_workbench_settings(self):
        f = os.environ["CCS_WORKBENCH_SETTINGS"]
        wr(f, json.dumps({"schema": "ccs-console-backup-settings/1", "earlier_home_versions": 3}).encode()); self.assertEqual(CI.build_plan(self.pkg)["earlier_home_versions"], 3)
        wr(f, json.dumps({"schema": "ccs-console-backup-settings/1", "earlier_home_versions": 0}).encode()); self.assertEqual(CI.build_plan(self.pkg)["earlier_home_versions"], 0, "none is a valid choice")
        for bad in (-1, 10, "2", True, None):
            wr(f, json.dumps({"earlier_home_versions": bad}).encode()); self.assertEqual(CI.build_plan(self.pkg)["earlier_home_versions"], 1, f"{bad!r} is not a valid number: the default")
        wr(f, b"not json"); self.assertEqual(CI.build_plan(self.pkg)["earlier_home_versions"], 1)
        self.assertEqual(CI.build_plan(self.pkg, earlier=2)["earlier_home_versions"], 2, "a request's own choice comes first")
    def test_14_repair_never_touches_the_copy_in_use_until_the_switch(self):
        a = self.inst().run(); live = os.path.join(self.root, "var/lib/ccs/pkg", a["copy"]); s = os.path.join(live, "session.json"); wr(s, rd(s) + b" ")
        snap = tree(self.root); n = len(self.plan["files"]) + 4
        r = self.inst(before=n).run(); self.assertFalse(r["ok"]); self.assertTrue(r["rolled_back"]["ok"], r); self.assertEqual(changes(snap, tree(self.root)), [], "a failed repair leaves everything as it was")
        r = self.inst().run(); self.assertTrue(r["ok"], r); self.assertNotEqual(r["copy"], a["copy"]); self.assertEqual(self.pointer(), r["copy"])
        self.assertIn("MANIFEST.json", os.listdir(live), "the earlier copy, changed after it was installed, is kept whole")

    def test_15_another_version_of_ccs_mod_is_updated_by_its_own_install(self):
        mp = CI.addon_package("mod"); CI.FakeConsole(self.root).pack_install(mp["name"], mp["hmod"])   # e.g. placed by an earlier Workbench
        f = os.path.join(self.root, "var/lib/hakchi/rootfs/etc/ccs/mod/VERSION"); wr(f, rd(f).replace(mp["package_id"].encode(), b"a" * 64))
        r = self.inst().run(); self.assertTrue(r["ok"], r); self.assertEqual((r["preflight"]["update"], r["preflight"]["add"]), (["ccs-mod"], []))
        self.assertEqual(r["updated"], ["ccs-mod"])
        self.assertEqual(self.con().method_files("ccs-mod")["package"], mp["package_id"], "CCS Mod's own install updated it")

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
                        seen["installed"] += 1
                        if "cleanup-stopped" in journal_ops(j):   # a change after the verification was not confirmed: nothing more was written, the attempt stays open
                            self.assertFalse(r["ok"], r); self.assertEqual(r["result"], "cleanup-stopped"); self.assertTrue(r["verified"]); self.assertFalse(r["record_closed"])
                            self.assertEqual(journal_ops(j)[-2:], ["cleanup-stopped", "end"]); self.assertIn("No further writes", r["next"])
                        else: self.assertTrue(r["ok"], r); self.assertEqual(r["result"], "installed"); self.assertTrue(r["record_closed"])
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
    def test_22_repair_over_the_earlier_start_up_module(self):
        """a console where an earlier version installed its start-up module ccs-home (recorded; its start file already deleted by hakchi at a
        start): the repair installs CCS Mod and only then retires the earlier module through hakchi's pack_uninstall of its own list"""
        self.assertTrue(self.inst().run()["ok"]); copy = self.pointer()
        s = os.path.join(self.root, "var/lib/ccs/pkg", copy, "session.json"); wr(s, rd(s) + b" ")
        CI.FakeConsole(self.root).pack_uninstall("ccs-mod"); os.remove(os.path.join(self.root, f"var/lib/ccs/install/module-{CI.addon_package('mod')['sha256'][:16]}.json"))
        old = CI.S81_OVERRIDE.encode()
        wr(os.path.join(self.root, "var/lib/hakchi/hmod/uninstall-ccs-home"), '\n# auto-generated\nrm -f "$rootfs/./etc/init.d/S81clover-mcp"\nrmdir --ignore-fail-on-non-empty "$rootfs/./etc/init.d"\n')
        wr(os.path.join(self.root, f"var/lib/ccs/install/module-{sha(old)[:16]}.json"), json.dumps({"schema": "ccs-owned-module/1", "name": "ccs-home", "path": CI.LEGACY_FILE, "sha256": sha(old)}))
        self.plan_x, self.pkg_x = self.plan, self.pkg; self.sweep("repair", lambda root: None)
        r = self.inst().run(); self.assertTrue(r["ok"], r); c = self.con()
        self.assertEqual(c.pack_files("ccs-home"), {}, "the earlier module retired"); self.assertEqual(c.method_files("ccs-mod")["package"], CI.addon_package("mod")["package_id"])
        self.assertNotIn(f"module-{sha(old)[:16]}.json", os.listdir(os.path.join(self.root, "var/lib/ccs/install")), "and its record tidied once nothing of it is installed")
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
    def test_23b_keep_none_never_loses_the_copy_in_use_before_the_switch(self):
        """earlier versions to keep: none. A crash after any change before the switch is read back rolls back to exactly the state before,
        the copy in use whole (the only one to go back to); after it, the new copy is in use and the attempt finishes"""
        a = self.inst().run(); self.assertTrue(a["ok"]); pkg_b = make_package(os.path.join(self.t, "pkg-b"), "b"); plan_b = CI.build_plan(pkg_b, earlier=0)
        self.plan_x, self.pkg_x = plan_b, pkg_b; total = self.count(lambda root: None)
        for n in range(1, total + 1):
            with self.subTest(change=n):
                root = self.fork(f"crash0-{n}"); before = tree(root); j = self.jnew()
                with self.assertRaises(SystemExit): CI.Installer(self.con(root, crash_after=n), plan_b, pkg_b, j).run()
                r = CI.Installer(self.con(root), plan_b, pkg_b, j).resume()
                if "installed" in journal_ops(j): self.assertEqual(r["result"], "installed", r); self.assertNotEqual(rd(os.path.join(root, "var/lib/ccs/pkg/current")).strip().decode(), a["copy"])
                else:
                    self.assertEqual(r["result"], "rolled-back", r); self.assertEqual(changes(before, tree(root)), [], f"crash after change {n}")
                    self.assertIn("MANIFEST.json", os.listdir(os.path.join(root, "var/lib/ccs/pkg", a["copy"])), "the copy in use is whole: nothing went before the switch")
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
        p = CI.uninstall_plan(self.con()); self.assertTrue(p["ok"]); self.assertTrue(any("CCS Mod (ccs-mod" in x for x in p["remove"]), p["remove"])
        self.assertTrue(any("owner-notes.txt" in x for x in p["keep"]) and any("0123456789abcdef.staging" in x for x in p["keep"]) and any("saves" in x for x in p["keep"]), p)
        r = CI.uninstall(self.con()); self.assertTrue(r["ok"], r); self.assertEqual(r["data"], "kept")
        self.assertIsNone(self.con().method_files("ccs-mod")); self.assertIsNone(self.con().overlay_sha256("/bin/ccs-mod")); self.assertFalse(os.path.exists(os.path.join(self.root, "var/lib/hakchi/hmod/uninstall-ccs-mod")))
        self.assertEqual(rd(os.path.join(self.root, "var/lib/ccs/data/saves/x/cart.sav")), b"battery"); self.assertTrue(os.path.isfile(os.path.join(self.root, "var/lib/ccs/pkg", a["copy"], "owner-notes.txt")))
        self.assertTrue(os.path.isfile(os.path.join(self.root, "var/lib/ccs/pkg/0123456789abcdef.staging/owner.txt"))); self.assertEqual(CI.stock_digest(self.root), {k: sha(v) for k, v in CI.STOCK_SAMPLE.items()})
        self.assertIn("its own menu", r["after_restart"])
        r = CI.uninstall(self.con(), keep_data=False); self.assertTrue(r["ok"]); self.assertFalse(os.path.exists(os.path.join(self.root, "var/lib/ccs/data")))
    def test_41_uninstall_keeps_a_package_it_does_not_recognize_and_changed_method_files(self):
        self.assertTrue(self.inst().run()["ok"]); f = os.path.join(self.root, "var/lib/hakchi/rootfs/etc/ccs/mod/VERSION")
        good = rd(f); wr(f, good.replace(CI.addon_package("mod")["package_id"].encode(), b"not-a-package-record"))
        p = CI.uninstall_plan(self.con()); self.assertTrue(p["ok"]); self.assertFalse(any(a["op"] == "addon-uninstall" for a in p["actions"])); self.assertTrue(any("does not recognize" in k for k in p["keep"]), p["keep"])
        wr(f, good); wr(os.path.join(self.root, "var/lib/hakchi/rootfs/etc/ccs/mod/entry.png"), b"changed by someone")
        r = CI.uninstall(self.con()); self.assertTrue(r["verified"]["module_removed"], r)
        self.assertEqual(rd(os.path.join(self.root, "var/lib/hakchi/rootfs/etc/ccs/mod/entry.png")), b"changed by someone", "the package's own removal keeps a file someone changed")
    def test_42_an_interrupted_uninstall_is_finished_by_running_it_again(self):
        self.assertTrue(self.inst().run()["ok"]); base = self.fork("installed"); c = self.con(self.fork("count-u")); self.assertTrue(CI.uninstall(c)["ok"]); total = c.nchanges
        for n in range(1, total + 1):
            for kind in ("before", "after"):
                with self.subTest(change=n, kind=kind):
                    root = os.path.join(self.t, f"u-{kind}-{n}"); shutil.copytree(base, root)
                    try: CI.uninstall(self.con(root, **{kind: n}))
                    except (ConnectionError, OSError): pass
                    r = CI.uninstall(self.con(root)); self.assertTrue(r["ok"], r)
                    self.assertIsNone(self.con(root).method_files("ccs-mod")); self.assertEqual(CI.stock_digest(root), {k: sha(v) for k, v in CI.STOCK_SAMPLE.items()})
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
    def test_50_a_real_console_goes_only_through_the_transport_worker_and_a_closed_tier_refuses_it(self):
        self.assertTrue(gate.PHYSICAL_WRITE_ENABLED and gate.PHYSICAL_INSPECT_ENABLED, "both tiers are open in this version")
        for kind in ("ssh", "clovershell"):   # the shipped opener (the transport worker): blocked here by the test boundary before any worker starts
            with self.assertRaises(console_test_guard.RealConsoleBlocked): CI.open_console(kind, "169.254.13.37")
        for kind in ("fel", "usb", "rndis", "", None):
            with self.assertRaises(ValueError): CI.open_console(kind, "169.254.13.37")   # no other kind of console connection exists
        try: os.remove(console_test_guard.LOG)
        except OSError: pass
        class Real(CI.FakeConsole): simulated = False
        r = Real(self.root); was = (gate.PHYSICAL_INSPECT_ENABLED, gate.PHYSICAL_WRITE_ENABLED)
        try:
            gate.PHYSICAL_INSPECT_ENABLED = gate.PHYSICAL_WRITE_ENABLED = False   # a build whose tiers are closed in code
            with self.assertRaises(gate.GateClosed): CI.Installer(r, self.plan, self.pkg, self.jnew())
            with self.assertRaises(gate.GateClosed): CI.uninstall(r)
            with self.assertRaises(gate.GateClosed): CI.export_data(r, os.path.join(self.t, "x"))
            with self.assertRaises(gate.GateClosed): CI.Installer.resume(type("I", (), {"c": r})())
            gate.PHYSICAL_INSPECT_ENABLED = True   # reads open, writes closed: a real console is read, never changed
            with self.assertRaises(gate.GateClosed): CI.Installer(r, self.plan, self.pkg, self.jnew()).run()
            with self.assertRaises(gate.GateClosed): CI.uninstall(r)
        finally: gate.PHYSICAL_INSPECT_ENABLED, gate.PHYSICAL_WRITE_ENABLED = was

class Method(Base):
    """the start-up is the add-ons' own (CCS Mod's tile, CCS Nucleus's start), never the start file hakchi deletes (what the add-ons do on a
    console, with hakchi's own pack_install, preinit and rcS: tools/console-nucleus/test_nucleus.py under qemu-arm)"""
    def test_60_the_addon_packages_are_the_workbench_preparation_images_pinned_builds(self):
        sys.path.insert(0, os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "console-backup", "ram")))
        import build_images as BI
        self.assertEqual(set(BI.METHOD_SHAS), set(CI.ADDONS.values()), "the Workbench pins exactly the three add-on packages")
        for a in ("mod", "nucleus"): self.assertEqual(CI.addon_package(a)["sha256"], BI.METHOD_SHAS[CI.ADDONS[a]], f"{CI.ADDONS[a]}: the app installs the same bytes the Workbench places")
        worker = rd(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "console-worker", "CcsWorker", "Ops", "ConsolePath.cs")).decode()
        for n, s in BI.METHOD_SHAS.items(): self.assertIn(f'{{ "{n}", "{s}" }}', worker, f"the transport worker pins {n} to the same build")
        import re as _re
        self.assertEqual(len(_re.findall(r'\{ "ccs-[a-z-]+", "[0-9a-f]{64}" \}', worker.split("MethodPackages", 1)[1].split("};", 1)[0])), 3, "the transport worker pins three packages, no more")
    @unittest.skipUnless(os.environ.get("CCS_CHOOSER_PROGRAM") and os.environ.get("CCS_CHOOSER_FONTS"),
                         "the startup chooser's release inputs (CCS_CHOOSER_PROGRAM: the console build of ccs-home-armv7; CCS_CHOOSER_FONTS: the player's menu fonts) are not given, so its pinned bytes cannot be rebuilt here")
    def test_62_the_chooser_package_from_its_release_inputs_is_the_pinned_build(self):
        sys.path.insert(0, os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "console-backup", "ram")))
        import build_images as BI
        keep = CI.CHOOSER_INPUTS
        try: CI.CHOOSER_INPUTS = None; self.assertEqual(CI.addon_package("nucleus-chooser")["sha256"], BI.METHOD_SHAS["ccs-nucleus-chooser"])
        finally: CI.CHOOSER_INPUTS = keep
    def test_63_without_its_inputs_the_chooser_is_refused_in_plain_words(self):
        keep = CI.CHOOSER_INPUTS; env = {k: os.environ.pop(k) for k in ("CCS_CHOOSER_PROGRAM", "CCS_CHOOSER_FONTS", "CCS_NATIVE_HOME_RUNTIME", "CCS_PLAYER_FONTS") if k in os.environ}
        try:
            CI.CHOOSER_INPUTS = {"program": os.path.join(self.t, "no-program"), "fonts": os.path.join(self.t, "no-fonts")}
            with self.assertRaises(CI.Refused) as e: CI.build_plan(self.pkg, {"nucleus": "chooser"})
            self.assertEqual(e.exception.why, CI.CHOOSER_MISSING)
            self.assertEqual(CI.build_plan(self.pkg, {"nucleus": "direct"})["addons"]["nucleus"], "direct", "CCS Nucleus without its chooser needs no such inputs")
        finally: CI.CHOOSER_INPUTS = keep; os.environ.update(env)
    def test_61_nothing_installs_the_start_file_hakchi_removes(self):
        self.assertNotIn(CI.LEGACY_FILE, json.dumps({k: v for k, v in self.plan.items() if k != "never_written"}), "the plan names no S81clover-mcp")
        self.assertTrue(self.inst().run()["ok"])
        self.assertFalse(os.path.exists(os.path.join(self.root, "var/lib/hakchi/rootfs" + CI.LEGACY_FILE)) and not os.path.exists(os.path.join(self.root, "var/lib/hakchi/hmod/uninstall-ccs-home")))
        hp = CI.make_stand_in(os.path.join(self.t, "nu"), "with-nucleus"); before = tree(hp.root); r = self.inst(root=hp.root).run()
        self.assertEqual(r["result"], "refused"); self.assertEqual(r["why"], CI.REMOVES_TEXT); self.assertEqual(changes(before, tree(hp.root)), [], "a choice that would take CCS Nucleus away: refused, nothing changed")
        self.assertFalse(os.path.exists(os.path.join(self.root, "var/lib/hakchi/rootfs/etc/init.d/S81ccs-mod")), "CCS Mod has no start script of its own")
        self.assertFalse(os.path.exists(os.path.join(self.root, "var/lib/ccs/policy")), "and there is no policy file")

class EarlierCopiesAndMenus(Base):
    """the earlier copies an installation removes after the switch: never one an entry of the console's menus names or may name (read
    when the review is made, after this collection's entries are written and again just before each copy goes); a removal the console does
    not confirm stops every write that would follow, the attempt's record stays open and the result says what is known"""
    OTHER = "/var/lib/hakchi/games/nes-usa/CLV-L-OTHER/CLV-L-OTHER.desktop"
    OWN = "var/lib/ccs/owned/published.own"
    def entry(self, root, path, copy, kind="pkg"):   # an Original Menu entry of another program that launches a file of one of this program's copies
        wr(os.path.join(root, *path.strip("/").split("/")), f"[Desktop Entry]\nName=Other\nExec=/bin/sh /var/lib/ccs/{kind}/{copy}/bin/ccs-player-armv7 --other\n".encode())
    def install(self, tag, root=None, earlier=None, console=None, **faults):
        pkg = make_package(os.path.join(self.t, f"pkg-{tag}"), tag); plan = CI.build_plan(pkg, earlier=earlier)
        return CI.Installer(console or CI.FakeConsole(root or self.root, faults), plan, pkg, self.jnew()).run()
    def games_copy(self, root, copy="dddddddddddddddd.eeeeeeee"):   # an earlier games copy of this program's, recorded, its file unchanged
        b = b"NES\x1a" + bytes(16); wr(os.path.join(root, "var/lib/ccs/games", copy, "roms/piece.nes"), b)
        wr(os.path.join(root, "var/lib/ccs/install", f"games-{copy}.json"), json.dumps({"schema": CI.GO.RECORD, "copy": copy, "version": copy.split(".")[0], "files": {"roms/piece.nes": sha(b)}}))
        return copy
    def earlier(self, r): return next(x for x in r["tidy"] if "earlier" in x)

    def test_70_an_earlier_copy_another_menu_entry_names_is_kept_with_the_default_setting(self):
        a = self.install("a"); b = self.install("b"); self.assertTrue(a["ok"] and b["ok"])
        control = self.fork("control"); self.entry(self.root, self.OTHER, a["copy"])
        r = self.install("c")
        self.assertTrue(r["ok"], r); self.assertEqual(r["result"], "installed"); self.assertTrue(r["record_closed"])
        rev = r["preflight"]["earlier"]; self.assertEqual(rev["keep"], 1); self.assertNotIn(a["copy"], rev["remove"], "the review does not offer a copy an entry names")
        self.assertEqual(rev["why"][a["copy"]], CI.NAMED_YES); self.assertEqual({x["copy"]: x["named"] for x in rev["copies"]}[a["copy"]], "yes")
        t = self.earlier(r); self.assertEqual(t["earlier"]["remove"], []); self.assertEqual(t["earlier"]["why"][b["copy"]], "an earlier version kept")
        self.assertEqual(t["named_by"], {a["copy"]: [self.OTHER]})
        c = self.con()
        for f in self.plan["files"]: self.assertIsNotNone(c.sha256(f"{CI.PKG_DIR}/{a['copy']}/{f['path']}"), f["path"])
        self.assertTrue(os.path.isfile(os.path.join(self.root, *self.OTHER.strip("/").split("/"))), "the other program's entry stays")
        self.assertEqual(sorted(CI.read_records(c)["versions"]), sorted([a["copy"], b["copy"], r["copy"]]))
        rc = self.install("c2", root=control); self.assertTrue(rc["ok"], rc)   # the same installation where nothing names the oldest copy
        self.assertFalse(os.path.exists(os.path.join(control, "var/lib/ccs/pkg", a["copy"])), "without the entry the oldest copy goes, as the setting says")

    def test_71_entries_that_cannot_all_be_read_keep_every_earlier_copy(self):
        a = self.install("a"); self.assertTrue(a["ok"])
        def case(name, setup, **faults):
            root = self.fork(name); setup(root); before = tree(root); r = self.install("b", root=root, earlier=0, **faults)
            self.assertTrue(r["ok"], (name, r)); self.assertEqual(r["result"], "installed", name)
            t = self.earlier(r); self.assertEqual(t["earlier"]["remove"], [], name); self.assertEqual(t["earlier"]["why"][a["copy"]], CI.NAMED_UNKNOWN, name)
            self.assertEqual(r["preflight"]["earlier"]["remove"], [], name); self.assertEqual(r["preflight"]["earlier"]["why"][a["copy"]], CI.NAMED_UNKNOWN, name)
            gone = [k for k in changes(before, tree(root)) if k.startswith(("var/lib/ccs/pkg/" + a["copy"], "var/lib/ccs/install/v-" + a["copy"]))]
            self.assertEqual(gone, [], f"{name}: nothing of the earlier copy changed")
            return t
        unread = "/var/lib/hakchi/games/nes-usa/CLV-L-UNREAD/CLV-L-UNREAD.desktop"
        case("unreadable-entry", lambda root: wr(os.path.join(root, *unread.strip("/").split("/")), b"[Desktop Entry]\nExec=/bin/other\n"), menu_unreadable=unread)
        case("a-session-that-cannot-list-entries", lambda root: None, menu_refs_fail=True)
        t = case("the-usb-drive-folder-is-a-link", lambda root: CI.FakeConsole(root).link("/media/hakchi", "/var/lib/elsewhere"))
        self.assertIn("could be read", t["named_why"])
        # the console program's own record of entries on a USB drive or SD card that is not connected, and a record that cannot be read
        c = self.con(); name = "pkg/" + a["copy"]
        wr(os.path.join(self.root, *self.OWN.split("/")), ("schema=ccs-ownership/1\nmethod=published\npackage=none\nfile=" + "c" * 64 + " 0644 /media/hakchi/games/nes-usa/000/CLV-L-CCS/CLV-L-CCS.desktop\n").encode())
        n = CI.menu_names(c, [name]); self.assertEqual(n["named"], {name: "unknown"}); self.assertIn("USB drive or SD card", n["why"])
        os.makedirs(os.path.join(self.root, "media/hakchi")); self.assertEqual(CI.menu_names(c, [name])["named"], {name: "no"}, "the drive is there and read: nothing names it")
        os.remove(os.path.join(self.root, *self.OWN.split("/"))); os.makedirs(os.path.join(self.root, *self.OWN.split("/")))
        n = CI.menu_names(c, [name]); self.assertEqual(n["named"], {name: "unknown"}); self.assertIn("cannot be read", n["why"])

    def test_72_an_entry_on_a_connected_usb_drive_is_read_too(self):
        a = self.install("a"); self.assertTrue(a["ok"])
        named = self.fork("named"); self.entry(named, "/media/hakchi/games/nes-usa/000/CLV-L-USB/CLV-L-USB.desktop", a["copy"])
        r = self.install("b", root=named, earlier=0); self.assertTrue(r["ok"], r)
        self.assertEqual(self.earlier(r)["earlier"]["why"][a["copy"]], CI.NAMED_YES); self.assertTrue(os.path.isdir(os.path.join(named, "var/lib/ccs/pkg", a["copy"])))
        other = self.fork("other"); wr(os.path.join(other, "media/hakchi/games/nes-usa/000/CLV-L-USB/CLV-L-USB.desktop"), b"[Desktop Entry]\nExec=/bin/other\n")
        r = self.install("b", root=other, earlier=0); self.assertTrue(r["ok"], r)
        self.assertFalse(os.path.exists(os.path.join(other, "var/lib/ccs/pkg", a["copy"])), "the drive is there and read: nothing names the copy, so it goes")

    def test_73_a_copy_is_checked_again_just_before_it_goes(self):
        a = self.install("a"); self.assertTrue(a["ok"]); test = self
        class Moving(CI.FakeConsole):   # the console changes between the clean-up's decision and the removal
            def __init__(self, *x, change=None, **k): super().__init__(*x, **k); self.calls = 0; self.change = change
            def menu_refs(self, names):
                self.calls += 1
                if self.calls == 3: self.change(self.root)   # 1: the review, 2: the clean-up's decision, 3: just before the copy goes
                return super().menu_refs(names)
        for name, change, words in (("named-meanwhile", lambda root: test.entry(root, test.OTHER, a["copy"]), CI.NAMED_YES),
                                    ("changed-meanwhile", lambda root: wr(os.path.join(root, "var/lib/ccs/pkg", a["copy"], "session.json"), b"changed"), "it changed since it was checked")):
            root = self.fork(name); r = self.install("b", root=root, earlier=0, console=Moving(root, change=change))
            self.assertTrue(r["ok"], (name, r)); self.assertEqual(r["result"], "installed", name); self.assertTrue(r["record_closed"], f"{name}: a refusal is not a failure")
            self.assertEqual(self.earlier(r)["earlier"]["remove"], [a["copy"]], f"{name}: the decision was to remove it")
            t = next(x for x in r["tidy"] if x.get("copy") == a["copy"]); self.assertTrue(t["refused"], name); self.assertFalse(t["removed"], name); self.assertEqual(t["why"], words, name)
            self.assertTrue(os.path.isfile(os.path.join(root, "var/lib/ccs/pkg", a["copy"], "bin/ccs-player-armv7")), f"{name}: nothing of it removed")

    def stop_case(self, acted: bool):
        a = self.install("a"); b = self.install("b"); self.assertTrue(a["ok"] and b["ok"]); games = self.games_copy(self.root)
        target = f"{CI.PKG_DIR}/{b['copy']}/bin/ccs-player-armv7"
        class Failing(CI.FakeConsole):   # the removal of the first file of the clean-up is not confirmed: lost before it acts, or after
            def __init__(self, *x, **k): super().__init__(*x, **k); self.failed = False; self.after = []
            def _change(self, what, act):
                if self.failed: self.after.append(what)
                return super()._change(what, act)
            def remove(self, path, h):
                if path == target and not self.failed:
                    if acted: super().remove(path, h)   # the failing call's own change, before the failure is reported
                    self.failed = True
                    raise ConnectionError("the console stopped answering")
                return super().remove(path, h)
        c = Failing(self.root); r = self.install("c", earlier=0, console=c); j = os.path.join(self.jdir, f"j{self.n}.jsonl")
        self.assertTrue(c.failed); self.assertEqual(c.after, [], "no write after the failed removal")
        self.assertFalse(r["ok"], r); self.assertEqual(r["result"], "cleanup-stopped"); self.assertTrue(r["verified"]); self.assertFalse(r["record_closed"])
        self.assertEqual(self.pointer(), r["copy"], "the new HOME, verified, stays in use")
        self.assertEqual(r["cleanup"]["uncertain"], [target]); self.assertIn("stopped", r["why"]); self.assertIn("No further writes", r["next"])
        self.assertTrue(CI.read_records(self.con())["attempts"], "the attempt's record stays open on the console")
        self.assertTrue(os.path.isdir(os.path.join(self.root, "var/lib/ccs/pkg", a["copy"])), "the next copy in the clean-up is not touched")
        self.assertTrue(os.path.isfile(os.path.join(self.root, "var/lib/ccs/games", games, "roms/piece.nes")), "the games copy queued after it is not touched")
        self.assertTrue(os.path.isfile(os.path.join(self.root, "var/lib/ccs/install", f"games-{games}.json")))
        t = next(x for x in r["tidy"] if x.get("copy") == b["copy"]); self.assertTrue(t["stopped"]); self.assertEqual(t["uncertain"], [target])
        ops = journal_ops(j); self.assertIn("cleanup-stopped", ops); self.assertNotIn("finish", ops); self.assertEqual(ops[-1], "end")
        self.assertEqual(os.path.exists(os.path.join(self.root, *target.strip("/").split("/"))), not acted)
        return r, j

    def test_74_a_removal_that_fails_before_it_acts_stops_every_later_write(self):
        r, j = self.stop_case(acted=False)
        before = sorted(f["path"] for f in CI.build_plan(os.path.join(self.t, "pkg-b"))["files"] if f["path"] < "bin/ccs-player-armv7")
        self.assertEqual(r["cleanup"]["partial"]["removed_files"], len(before), "the files that sort before it were removed, and only those")

    def test_75_a_removal_that_fails_after_it_may_have_acted_is_reported_as_not_known(self):
        r, j = self.stop_case(acted=True); self.assertIn("Whether this changed is not known", r["why"])

    def test_76_the_attempt_left_open_is_finished_by_its_own_recovery(self):
        r, j = self.stop_case(acted=False)
        again = self.install("d"); self.assertEqual(again["result"], "refused"); self.assertIn("did not finish", again["why"])
        pkg = os.path.join(self.t, "pkg-c"); rr = CI.Installer(self.con(), CI.build_plan(pkg, earlier=0), pkg, j).resume()
        self.assertTrue(rr["ok"], rr); self.assertEqual(rr["result"], "installed"); self.assertEqual(CI.read_records(self.con())["attempts"], {})

    def test_77_an_earlier_games_copy_another_entry_names_is_kept(self):
        a = self.install("a"); self.assertTrue(a["ok"]); g1 = self.games_copy(self.root); g2 = self.games_copy(self.root, "eeeeeeeeeeeeeeee.ffffffff")
        self.entry(self.root, self.OTHER, g1, kind="games")
        r = self.install("b"); self.assertTrue(r["ok"], r)
        t1 = next(x for x in r["tidy"] if x.get("copy") == g1); self.assertTrue(t1["refused"]); self.assertEqual(t1["why"], CI.NAMED_YES)
        self.assertTrue(os.path.isfile(os.path.join(self.root, "var/lib/ccs/games", g1, "roms/piece.nes")))
        self.assertFalse(os.path.exists(os.path.join(self.root, "var/lib/ccs/games", g2)), "a games copy nothing names goes")

    def test_79_a_read_that_fails_during_the_clean_up_stops_it_too(self):
        a = self.install("a"); self.assertTrue(a["ok"])
        class Lost(CI.FakeConsole):   # the connection is lost while the clean-up lists the menu entries (after the installation was verified)
            def __init__(self, *x, **k): super().__init__(*x, **k); self.calls = 0; self.lost = False; self.after = []
            def menu_refs(self, names):
                self.calls += 1
                if self.calls == 2: self.lost = True; raise ConnectionError("the console stopped answering")
                return super().menu_refs(names)
            def _change(self, what, act):
                if self.lost: self.after.append(what)
                return super()._change(what, act)
        c = Lost(self.root); r = self.install("b", earlier=0, console=c)
        self.assertTrue(c.lost); self.assertEqual(c.after, [], "no write after the lost read")
        self.assertFalse(r["ok"]); self.assertEqual(r["result"], "cleanup-stopped"); self.assertTrue(r["verified"]); self.assertFalse(r["record_closed"])
        self.assertIn("reading the console during the clean-up", r["why"]); self.assertEqual(r["cleanup"]["uncertain"], [], "a read changes nothing")
        self.assertTrue(os.path.isdir(os.path.join(self.root, "var/lib/ccs/pkg", a["copy"])), "the earlier copy is untouched")
        self.assertEqual(self.pointer(), r["copy"])

    def test_80_a_folder_link_that_may_hide_entries_keeps_the_earlier_copy_with_the_default_setting(self):
        a = self.install("a"); b = self.install("b"); self.assertTrue(a["ok"] and b["ok"])
        G = "/var/lib/hakchi/games/nes-usa"
        def case(name, setup, kept):
            root = self.fork(name); c = CI.FakeConsole(root); setup(root, c); r = self.install("c", root=root)
            self.assertTrue(r["ok"], (name, r)); self.assertEqual(r["result"], "installed", name); t = self.earlier(r)
            self.assertEqual(t["earlier"]["keep"], 1, name)
            if kept:
                self.assertEqual(t["earlier"]["remove"], [], name); self.assertEqual(t["earlier"]["why"][a["copy"]], CI.NAMED_UNKNOWN, name)
                self.assertIn("folder link", t["named_why"], name); self.assertTrue(os.path.isdir(os.path.join(root, "var/lib/ccs/pkg", a["copy"])), name)
            else:
                self.assertEqual(t["earlier"]["remove"], [a["copy"]], name); self.assertFalse(os.path.exists(os.path.join(root, "var/lib/ccs/pkg", a["copy"])), name)
            return r
        def hidden(root, c):   # an entry naming the oldest copy, in a folder outside hakchi's folders that a link in the menu leads to
            self.entry(root, "/var/lib/elsewhere/CLV-L-HIDDEN.desktop", a["copy"]); c.link(G + "/000/CLV-L-HIDDEN", "/var/lib/elsewhere")
        case("hidden-behind-a-folder-link", hidden, kept=True)
        def dangling(root, c): os.makedirs(os.path.join(root, *(G + "/000/CLV-L-X").strip("/").split("/"))); c.link(G + "/000/CLV-L-X/autoplay", G + "/.storage/CLV-L-X/autoplay")
        case("a-link-that-leads-nowhere", dangling, kept=True)
        def ordinary(root, c):   # Hakchi2 CE's own links: a game's media folder in its .storage, an original game's in the console's system image
            for d in (G + "/.storage/CLV-L-S/autoplay", "/var/squashfs/usr/share/games/nes/kachikachi/CLV-P-NAAAE/autoplay", G + "/000/CLV-L-S", G + "/000/CLV-P-NAAAE"):
                os.makedirs(os.path.join(root, *d.strip("/").split("/")))
            c.link(G + "/000/CLV-L-S/autoplay", G + "/.storage/CLV-L-S/autoplay"); c.link(G + "/000/CLV-P-NAAAE/autoplay", "/var/squashfs/usr/share/games/nes/kachikachi/CLV-P-NAAAE/autoplay")
        case("hakchi-s-own-links", ordinary, kept=False)
        def outside(root, c): c.link("/var/lib/hakchi/rootfs/usr/game/save", "/var/lib")   # hakchi's own: not where menus list games
        case("a-link-outside-the-menus-folders", outside, kept=False)
        def overlay(root, c): os.makedirs(os.path.join(root, "var/lib/elsewhere"), exist_ok=True); c.link("/var/lib/hakchi/rootfs/usr/share/games/nes-linked", "/var/lib/elsewhere")
        case("a-link-in-the-overlay-games-folder", overlay, kept=True)

    def test_78_the_review_leaves_out_only_this_collections_own_unchanged_entries(self):
        a = self.install("a"); self.assertTrue(a["ok"]); c = self.con(); name = "pkg/" + a["copy"]
        own_path = "/var/lib/hakchi/games/nes-usa/000/CLV-L-CCS/CLV-L-CCS.desktop"; self.entry(self.root, own_path, a["copy"])
        own_sha = sha(rd(os.path.join(self.root, *own_path.strip("/").split("/"))))
        wr(os.path.join(self.root, *self.OWN.split("/")), f"schema=ccs-ownership/1\nmethod=published\npackage=none\nfile={own_sha} 0644 {own_path}\n".encode())
        self.assertEqual(CI.menu_names(c, [name], rewritten=True)["named"], {name: "no"}, "its own entry, as it wrote it: the installation rewrites it")
        self.assertEqual(CI.menu_names(c, [name])["named"], {name: "yes"}, "after the installation every entry counts")
        self.entry(self.root, self.OTHER, a["copy"])
        self.assertEqual(CI.menu_names(c, [name], rewritten=True)["named"], {name: "yes"}, "another program's entry counts in the review")
        os.remove(os.path.join(self.root, *self.OTHER.strip("/").split("/")))
        q = os.path.join(self.root, *own_path.strip("/").split("/")); wr(q, rd(q) + b"# changed\n")
        self.assertEqual(CI.menu_names(c, [name], rewritten=True)["named"], {name: "yes"}, "its own entry changed since: it counts")

if __name__ == "__main__":
    unittest.main(verbosity=2)
