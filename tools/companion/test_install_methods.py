r"""Classic Cart Studio's own collection on a console that uses CCS Mod or CCS Nucleus, and CCS Mod's start at power-on, PC only (stand-ins and
an in-process worker; no device, socket to a console or shell). Nothing here is a real console: every console below is a disposable folder.
  staging   the app stages the collection's console package in both methods' layouts: the same files but session.json, whose leave item
            names where leaving CCS HOME goes; MANIFEST.json as the builder writes it
  engine    the method the console uses is read from it (CCS Mod, CCS Nucleus, neither; both, a changed or unknown package and a program
            without its package refused); a collection is installed into CCS Nucleus (first install, update, repeat, saves kept), never
            into the other method or a method that is not there (a stale method: refused, nothing changed); CCS Mod's start at power-on:
            off by default (the start unchanged), on by CCS Mod's own command, off again the same way, a start change alone copies nothing,
            a start script that is not CCS Mod's own is kept; every change of a CCS Nucleus install and of a CCS Mod install with its start
            on fails in turn (before and after) and is undone exactly or finished; a crash after any change is recovered from its journal;
            a cancel before the switch is undone
  worker    one plan per staged layout; the check reads the console's method and answers with that method's plan (the same collection);
            rehearsals of every scenario on a CCS Nucleus console; a stale method refused; the start choice bound in the plan; a cancel
  dispatch  the physical flow through the app's dispatcher with injected sessions: the check picks the CCS Nucleus plan, the confirmation
            and the installation are for exactly that plan; the console changed its method after the check: refused, nothing changed; a lost
            connection stops and its recovery is its own confirmed operation; the worker ends during the installation (the app closed or the
            PC stopped) and a new app session recovers it from this computer's records
  page      the Install on NES Classic page code (install.js, run by Node with a small stand-in for the page around it) against the real
            backend and worker: the review names the method this simulated console uses, offers CCS HOME at power-on only with CCS Mod,
            makes a new plan when that choice changes, and rehearses each method
  python tools/companion/test_install_methods.py      Exit 0 = PASS."""
from __future__ import annotations
import hashlib, io, json, os, secrets, shutil, subprocess, sys, tempfile, threading, time, unittest, urllib.error, urllib.request
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path[:0] = [HERE, os.path.join(HERE, "..", "rom-pipeline")]
import console_test_guard   # noqa: E402,F401  (first: the controlled console boundary, k3e)
_T = tempfile.mkdtemp(prefix="ccs-methods-"); os.environ.setdefault("NES_STUDIO_DATA", _T)
from console_engine import ccs_install as CI, gate   # noqa: E402
from console_engine.worker import Worker   # noqa: E402
from native_home import console_package as CPK   # noqa: E402
from test_ccs_install import rd, wr, tree, changes, journal_ops   # noqa: E402

def sha(b): return hashlib.sha256(b).hexdigest()

def staged(d: str, tag: str = "a", extra: dict | None = None) -> str:
    """a console package as the app stages it: d/mod built (synthetic files, the builder's MANIFEST.json form), d/nucleus derived from it"""
    m = os.path.join(d, "mod"); L = CPK.LAYOUTS["mod"]
    sess = {"schema": "ccs-play-session/1", "mode": "home", "saves_root": "/var/lib/ccs/data/saves", "games": [{"id": "g1", "title": f"Game {tag}", "rom": "/tmp/ccs/pkg/roms/" + "ab" * 32 + ".nes"}],
            "home": {"pack": "/tmp/ccs/pkg/home", "state_dir": "/var/lib/ccs/data/state", "design_root": "/var/lib/ccs/data/home-design", "leave": L["leave"]}}
    files = {"bin/ccs-player-armv7": (b"\x7fELF\x01" + tag.encode() * 4000, "executable", "0o755"), "home/pack.json": (b'{"schema":"ccs-native-home-pack/2"}', "home", "0o644"),
             "home/design/design.json": (b'{"schema":"ccs-home-design-compiled/2","tag":"' + tag.encode() + b'"}', "home", "0o644"),
             "roms/" + "ab" * 32 + ".nes": (b"NES\x1a" + bytes(24576), "rom", "0o644"), "startup/startup.json": (b'{"choice":"custom-0123456789abcdef"}', "configuration", "0o644"),
             "startup/startup-custom-0123456789abcdef.jpg": (b"\xff\xd8\xff" + tag.encode() * 300, "startup-image", "0o644"), "config/controller-map.txt": (b"key 304 a\n", "configuration", "0o644"),
             "session.json": ((json.dumps(sess, indent=1, ensure_ascii=False) + "\n").encode(), "configuration", "0o644")} | (extra or {})
    for rel, (b, _, _) in files.items(): wr(os.path.join(m, *rel.split("/")), b)
    fm = {rel: {"size": len(b), "sha256": sha(b), "kind": k, "mode": mo} for rel, (b, k, mo) in sorted(files.items())}
    man = {"schema": "ccs-console-package/1", "label": f"test {tag}", "layout": "mod", "console_paths": {k: L[k] for k in ("pkg", "data", "scratch", "logs")}, "created_utc": "2026-11-16T00:00:00Z",
           "games": 1, "playable": 1, "unavailable": [], "startup": {"choice": "custom-0123456789abcdef"}, "player": {"file": "bin/ccs-player-armv7", "sha256": fm["bin/ccs-player-armv7"]["sha256"]},
           "sizes": {k: sum(f["size"] for f in fm.values() if f["kind"] == k) for k in sorted({f["kind"] for f in fm.values()})}, "total_bytes": sum(f["size"] for f in fm.values()), "files": fm}
    wr(os.path.join(m, "MANIFEST.json"), (json.dumps(man, indent=1, ensure_ascii=False) + "\n").encode())
    r = CPK.derive_layout(m, os.path.join(d, "nucleus"), "nucleus"); assert r["ok"], r
    return d

def sealed_config(root: str, state: str):
    body = f"schema=ccs-mod-config/1\nautostart={state}\n"; wr(os.path.join(root, "var/lib/ccs/mod/config"), (body + f"end={sha(body.encode())}\n").encode())

class Base(unittest.TestCase):
    def setUp(self):
        self.t = tempfile.mkdtemp(prefix="ccs-meth-", dir=_T); self.st = staged(os.path.join(self.t, "staged-a")); self.n = 0
        self.jdir = os.path.join(self.t, "journals"); os.makedirs(self.jdir)
    def tearDown(self): shutil.rmtree(self.t, ignore_errors=True)
    def jnew(self): self.n += 1; return os.path.join(self.jdir, f"j{self.n}.jsonl")
    def console(self, profile: str, name: str | None = None) -> str:
        root = os.path.join(self.t, name or profile); CI.make_stand_in(root, profile); return root
    def plan(self, method: str, start: str | None = None, st: str | None = None):
        return CI.build_plan(os.path.join(st or self.st, method), mod_start=start)
    def run_inst(self, root, plan, method, st=None, **faults):
        return CI.Installer(CI.FakeConsole(root, faults), plan, os.path.join(st or self.st, method), self.jnew()).run()
    def pointer(self, root): b = CI.FakeConsole(root).read(CI.POINTER, 64); return b.decode().strip() if b else None

class Staging(Base):
    def test_01_both_layouts_are_one_package_but_the_leave_item(self):
        a, b = os.path.join(self.st, "mod"), os.path.join(self.st, "nucleus")
        ma, mb = json.loads(rd(os.path.join(a, "MANIFEST.json"))), json.loads(rd(os.path.join(b, "MANIFEST.json")))
        self.assertEqual((ma["layout"], mb["layout"]), ("mod", "nucleus")); self.assertTrue(CPK.verify(a)["ok"] and CPK.verify(b)["ok"])
        diff = [r for r in ma["files"] if rd(os.path.join(a, *r.split("/"))) != rd(os.path.join(b, *r.split("/")))]
        self.assertEqual(diff, ["session.json"], "every file but session.json is the same")
        sa, sb = json.loads(rd(os.path.join(a, "session.json"))), json.loads(rd(os.path.join(b, "session.json")))
        self.assertEqual(sb["home"]["leave"], CPK.LAYOUTS["nucleus"]["leave"]); sb["home"]["leave"] = sa["home"]["leave"]; self.assertEqual(sa, sb, "only the leave item differs")
        keep = {k: v for k, v in mb.items() if k not in ("layout", "files", "sizes", "total_bytes")}; self.assertEqual(keep, {k: v for k, v in ma.items() if k not in ("layout", "files", "sizes", "total_bytes")})
        self.assertEqual(rd(os.path.join(b, "MANIFEST.json")), (json.dumps(mb, indent=1, ensure_ascii=False) + "\n").encode(), "MANIFEST.json written as the builder writes it")
        self.assertEqual(CI.staged_packages(self.st), {"mod": a, "nucleus": b}); self.assertEqual(CI.staged_packages(a), {"mod": a}, "a single package folder is its own layout's")
        self.assertTrue(CI.same_collection(self.plan("mod"), self.plan("nucleus")))
        self.assertFalse(CI.same_collection(self.plan("mod"), CI.build_plan(os.path.join(staged(os.path.join(self.t, "b"), "b"), "nucleus"))))
        self.assertFalse(CPK.derive_layout(a, os.path.join(self.t, "ram"), "ram-test")["ok"], "never into a layout with other console places")
        self.assertFalse(CPK.derive_layout(a, b, "nucleus")["ok"], "always into a new folder")

class Method(Base):
    def test_10_the_check_reads_the_method_the_console_uses(self):
        self.assertEqual(CI.console_method(CI.FakeConsole(self.console("hakchi-ready"))), "none")
        self.assertEqual(CI.console_method(CI.FakeConsole(self.console("mod-module"))), "mod")
        self.assertEqual(CI.console_method(CI.FakeConsole(self.console("other-module"))), "nucleus")
        with self.assertRaises(CI.Refused) as e: CI.console_method(CI.FakeConsole(self.console("both-methods")))
        self.assertIn("both CCS Mod and CCS Nucleus", e.exception.why)
        root = self.console("other-module", "changed"); wr(os.path.join(root, "var/lib/hakchi/rootfs/bin/ccs-nucleus"), b"#!/bin/sh\n# changed\n")
        with self.assertRaises(CI.Refused) as e: CI.console_method(CI.FakeConsole(root))
        self.assertIn("were changed", e.exception.why)
        root = self.console("other-module", "foreign"); f = os.path.join(root, "var/lib/hakchi/rootfs/etc/ccs/nucleus/VERSION"); wr(f, rd(f).replace(CI.method_package("nucleus")["package_id"].encode(), b"someone-elses"))
        with self.assertRaises(CI.Refused) as e: CI.console_method(CI.FakeConsole(root))
        self.assertIn("does not recognize", e.exception.why)
        root = self.console("hakchi-ready", "prog"); wr(os.path.join(root, "var/lib/hakchi/rootfs/bin/ccs-nucleus"), b"#!/bin/sh\n")
        with self.assertRaises(CI.Refused) as e: CI.console_method(CI.FakeConsole(root))
        self.assertIn("hakchi does not list its package", e.exception.why)

    def test_11_a_collection_into_ccs_nucleus_first_install_update_repeat_saves_kept(self):
        root = self.console("other-module"); stock = CI.stock_digest(root); nuc = CI.FakeConsole(root).method_files("ccs-nucleus")
        r = self.run_inst(root, self.plan("nucleus"), "nucleus"); self.assertTrue(r["ok"], r); self.assertEqual(r["preflight"]["module"], "current"); self.assertEqual(r["preflight"]["console_method"], "nucleus")
        c = CI.FakeConsole(root); self.assertEqual(c.method_files("ccs-nucleus"), nuc, "CCS Nucleus is as it was"); self.assertIsNone(c.method_files("ccs-mod"), "CCS Mod is not added")
        self.assertEqual(self.pointer(root), r["copy"]); self.assertEqual(json.loads(rd(os.path.join(root, "var/lib/ccs/pkg", r["copy"], "session.json")))["home"]["leave"]["label"], "Choose Another System")
        self.assertIsNone(r.get("mod_start")); self.assertFalse(os.path.exists(os.path.join(root, "var/lib/hakchi/rootfs/etc/init.d/S81ccs-mod")))
        wr(os.path.join(root, "var/lib/ccs/data/saves/ab/cart.sav"), b"battery"); ident = rd(os.path.join(root, "var/lib/ccs/data/IDENTITY.json"))
        st_b = staged(os.path.join(self.t, "staged-b"), "b"); u = self.run_inst(root, CI.build_plan(os.path.join(st_b, "nucleus")), "nucleus", st=st_b)
        self.assertTrue(u["ok"], u); self.assertEqual(self.pointer(root), u["copy"]); self.assertTrue(u["tidy"] and u["tidy"][0]["removed"], u["tidy"])
        self.assertEqual(rd(os.path.join(root, "var/lib/ccs/data/saves/ab/cart.sav")), b"battery"); self.assertEqual(rd(os.path.join(root, "var/lib/ccs/data/IDENTITY.json")), ident, "the saves and their identity are kept")
        c = CI.FakeConsole(root); r2 = CI.Installer(c, CI.build_plan(os.path.join(st_b, "nucleus")), os.path.join(st_b, "nucleus"), self.jnew()).run()
        self.assertEqual(r2["result"], "no-change"); self.assertEqual(c.writes, []); self.assertEqual(CI.stock_digest(root), stock)
        self.assertTrue(CI.verify_installed(CI.FakeConsole(root), CI.build_plan(os.path.join(st_b, "nucleus")))["verified"])

    def test_12_a_stale_method_is_refused_and_nothing_changes(self):
        for profile, method, words in (("hakchi-ready", "nucleus", "CCS Nucleus is not installed on this console"), ("mod-module", "nucleus", "this console uses CCS Mod"),
                                       ("other-module", "mod", "this console uses CCS Nucleus"), ("both-methods", "mod", "both CCS Mod and CCS Nucleus"), ("both-methods", "nucleus", "both CCS Mod and CCS Nucleus")):
            with self.subTest(profile=profile, method=method):
                root = self.console(profile, f"stale-{profile}-{method}"); before = tree(root); r = self.run_inst(root, self.plan(method), method)
                self.assertEqual(r["result"], "refused", r); self.assertIn(words, r["why"]); self.assertEqual(changes(before, tree(root)), [], "nothing changed")
                self.assertIn("never changes a console's method" if "uses" in words else "", r["why"])

    def test_13_ccs_mod_start_at_power_on(self):
        root = self.console("hakchi-ready"); s81 = os.path.join(root, "var/lib/hakchi/rootfs/etc/init.d/S81ccs-mod")
        r = self.run_inst(root, self.plan("mod"), "mod"); self.assertTrue(r["ok"], r); self.assertEqual(r["mod_start"], "off")
        self.assertFalse(os.path.exists(s81), "off (the default): the console's start is unchanged"); self.assertEqual(CI.mod_start_state(CI.FakeConsole(root))["state"], "off")
        p_on = self.plan("mod", "on"); self.assertNotEqual(p_on["hash"], self.plan("mod")["hash"], "the choice is part of the plan")
        c = CI.FakeConsole(root); r = CI.Installer(c, p_on, os.path.join(self.st, "mod"), self.jnew()).run(); self.assertTrue(r["ok"], r); self.assertEqual(r["mod_start"], "on")
        self.assertTrue(r["preflight"]["same_copy"]); self.assertFalse([w for w in c.writes if "/pkg/" in w and "current" not in w], "a start change alone copies nothing")
        self.assertIn("ccs-mod:autostart on", c.writes); self.assertEqual(rd(s81), CI.FakeConsole(root).overlay_read(CI.MOD_START_TEMPLATE), "CCS Mod's own start script, from its template")
        self.assertEqual(CI.mod_start_state(CI.FakeConsole(root)), {"state": "on", "script": "ours", "setting": "on"}); self.assertTrue(CI.verify_installed(CI.FakeConsole(root), p_on)["verified"])
        self.assertEqual(CI.Installer(CI.FakeConsole(root), p_on, os.path.join(self.st, "mod"), self.jnew()).run()["result"], "no-change")
        r = self.run_inst(root, self.plan("mod", "off"), "mod"); self.assertTrue(r["ok"], r); self.assertFalse(os.path.exists(s81), "turned off the same way")
        self.assertEqual(CI.mod_start_state(CI.FakeConsole(root))["setting"], "off")
        r = self.run_inst(root, p_on, "mod"); self.assertTrue(r["ok"]); u = CI.uninstall(CI.FakeConsole(root)); self.assertTrue(u["ok"], u)
        self.assertFalse(os.path.exists(s81), "CCS Mod's removal takes its start script with it"); self.assertEqual(CI.mod_start_state(CI.FakeConsole(root))["state"], "off")
        with self.assertRaises(CI.PlanError): self.plan("nucleus", "on")   # CCS Nucleus is not offered it
        self.assertIsNone(self.plan("nucleus")["mod_start"]); self.assertNotIn("start", self.plan("nucleus")["steps"])

    def test_14_a_kept_setting_and_a_start_script_that_is_not_ccs_mods(self):
        root = self.console("hakchi-ready", "kept"); sealed_config(root, "on")   # an earlier CCS Mod's setting, kept when it was removed
        r = self.run_inst(root, self.plan("mod"), "mod"); self.assertTrue(r["ok"], r); self.assertEqual(CI.mod_start_state(CI.FakeConsole(root))["state"], "off", "the reviewed choice (off), not the kept setting")
        root = self.console("mod-module", "foreign"); wr(os.path.join(root, "var/lib/hakchi/rootfs/etc/init.d/S81ccs-mod"), b"#!/bin/sh\n# someone else's\n"); before = tree(root)
        r = self.run_inst(root, self.plan("mod", "on"), "mod"); self.assertEqual(r["result"], "refused", r); self.assertIn("S81ccs-mod", r["why"]); self.assertEqual(changes(before, tree(root)), [])

    @staticmethod
    def rest(before: dict, root: str) -> list:
        """what changed, but CCS Mod's own setting when it reads off: the start's undo is CCS Mod's own command, which keeps its setting (off),
        and CCS Mod's removal keeps its settings as its data; the console's start is as before either way"""
        ch = changes(before, tree(root)); st = CI.mod_start_state(CI.FakeConsole(root))
        own = {"var/lib/ccs/", "var/lib/ccs/mod/", "var/lib/ccs/mod/config"}
        return [k for k in ch if not (k in own and st["state"] == "off" and st["setting"] in ("off", "none") and st["script"] == "absent")]

    def sweep(self, label, profile, plan, method, st=None, setup=None, least=5):
        base = self.console(profile, f"{label}-base")
        if setup: setup(base)
        cnt = os.path.join(self.t, f"{label}-count"); shutil.copytree(base, cnt); c = CI.FakeConsole(cnt)
        r = CI.Installer(c, plan, os.path.join(st or self.st, method), self.jnew()).run(); self.assertTrue(r["ok"], r); total = c.nchanges; seen = {"rolled": 0, "installed": 0}
        for n in range(1, total + 1):
            for kind in ("before", "after"):
                with self.subTest(case=label, change=n, kind=kind):
                    root = os.path.join(self.t, f"{label}-{kind}-{n}"); shutil.copytree(base, root); before = tree(root); j = self.jnew()
                    r = CI.Installer(CI.FakeConsole(root, {kind: n}), plan, os.path.join(st or self.st, method), j).run()
                    if "installed" in journal_ops(j):
                        seen["installed"] += 1; self.assertTrue(r["ok"], r)
                        if CI.read_records(CI.FakeConsole(root))["attempts"]: self.assertEqual(CI.Installer(CI.FakeConsole(root), plan, os.path.join(st or self.st, method), j).resume()["result"], "installed")
                    else: seen["rolled"] += 1; self.assertTrue(r["rolled_back"]["ok"], r); self.assertEqual(self.rest(before, root), [], f"{label}: change {n} {kind}")
                    shutil.rmtree(root, ignore_errors=True)
        self.assertGreaterEqual(seen["rolled"], least); self.assertGreater(seen["installed"], 0)

    def test_15_every_change_fails_in_turn(self):
        self.sweep("nucleus-first", "other-module", self.plan("nucleus"), "nucleus")
        st_b = staged(os.path.join(self.t, "staged-b"), "b")
        self.sweep("nucleus-update", "other-module", CI.build_plan(os.path.join(st_b, "nucleus")), "nucleus", st=st_b, setup=lambda root: self.assertTrue(self.run_inst(root, self.plan("nucleus"), "nucleus")["ok"]))
        self.sweep("mod-start-on", "hakchi-ready", self.plan("mod", "on"), "mod")
        self.sweep("mod-start-only", "hakchi-ready", self.plan("mod", "on"), "mod", setup=lambda root: self.assertTrue(self.run_inst(root, self.plan("mod"), "mod")["ok"]), least=2)

    def test_16_a_crash_after_any_change_is_recovered_and_a_cancel_is_undone(self):
        for label, profile, plan, method in (("nucleus", "other-module", self.plan("nucleus"), "nucleus"), ("mod-on", "hakchi-ready", self.plan("mod", "on"), "mod")):
            base = self.console(profile, f"crash-{label}"); cnt = os.path.join(self.t, f"crash-{label}-count"); shutil.copytree(base, cnt); c = CI.FakeConsole(cnt)
            self.assertTrue(CI.Installer(c, plan, os.path.join(self.st, method), self.jnew()).run()["ok"]); total = c.nchanges
            for n in range(1, total + 1):
                with self.subTest(case=label, change=n):
                    root = os.path.join(self.t, f"crash-{label}-{n}"); shutil.copytree(base, root); before = tree(root); j = self.jnew()
                    with self.assertRaises(SystemExit): CI.Installer(CI.FakeConsole(root, {"crash_after": n}), plan, os.path.join(self.st, method), j).run()
                    r = CI.Installer(CI.FakeConsole(root), plan, os.path.join(self.st, method), j).resume()
                    if "installed" in journal_ops(j): self.assertEqual(r["result"], "installed", r)
                    else: self.assertIn(r["result"], ("rolled-back", "nothing to resume"), r); self.assertEqual(self.rest(before, root), [], f"crash after change {n}")
                    self.assertEqual(CI.read_records(CI.FakeConsole(root))["attempts"], {}); shutil.rmtree(root, ignore_errors=True)
            root = os.path.join(self.t, f"cancel-{label}"); shutil.copytree(base, root); before = tree(root); calls = {"n": 0}
            def cancel(): calls["n"] += 1; return calls["n"] > 3
            r = CI.Installer(CI.FakeConsole(root), plan, os.path.join(self.st, method), self.jnew(), cancel=cancel).run()
            self.assertEqual(r["result"], "cancelled"); self.assertTrue(r["rolled_back"]["ok"]); self.assertEqual(changes(before, tree(root)), [])

def ask(w: Worker, cmd: str, args: dict | None = None) -> tuple[dict, list]:
    out = io.StringIO(); w.out = out; w.handle(json.dumps({"v": 1, "id": "t", "cmd": cmd, "args": args or {}}))
    lines = [json.loads(l) for l in out.getvalue().splitlines() if l.strip()]; return lines[-1], lines[:-1]

class WorkerMethods(Base):
    def setUp(self):
        super().setUp(); self.w = Worker(os.path.join(self.t, "root"), console_package=self.st)
        r, _ = ask(self.w, "home-plan"); self.assertEqual(r["event"], "result", r); self.p = r["result"]
    def pre(self, profile, h=None): r, _ = ask(self.w, "home-preflight", {"plan_hash": h or self.p["hash"], "profile": profile}); return r["result"]
    def sim(self, h, profile, **a):
        r, ev = ask(self.w, "home-simulate", {"plan_hash": h, "profile": profile, **a}); self.assertEqual(r["event"], "result", r); return r["result"], ev
    def test_20_one_plan_per_layout_and_the_start_choice(self):
        self.assertEqual(self.p["method"], "mod"); self.assertEqual(self.p["mod_start"], "off"); self.assertEqual(set(self.p["plans"]), {"mod", "nucleus"}); self.assertEqual(self.p["hash"], self.p["plans"]["mod"])
        r, _ = ask(self.w, "home-plan", {"mod_start": "on"}); self.assertEqual(r["result"]["mod_start"], "on"); self.assertNotEqual(r["result"]["hash"], self.p["hash"]); self.assertEqual(r["result"]["plans"]["nucleus"], self.p["plans"]["nucleus"])
        for bad in ({"folder": "C:/elsewhere"}, {"mod_start": "maybe"}, {"method": "nucleus"}): r, _ = ask(self.w, "home-plan", bad); self.assertEqual(r["event"], "error", bad)
        rec, _ = ask(self.w, "home-records"); self.assertTrue(rec["result"]["last_plan_current"]); self.assertEqual(set(rec["result"]["staged_manifests"]), {"mod", "nucleus"})

    def test_21_the_check_answers_with_the_plan_for_the_consoles_method(self):
        for profile, method, selected in (("hakchi-ready", "mod", False), ("mod-module", "mod", False), ("other-module", "nucleus", True)):
            pf = self.pre(profile); self.assertTrue(pf["ok"], (profile, pf)); self.assertEqual((pf["method"], pf["selected"], pf["plan"]["method"]), (method, selected, method))
            self.assertEqual(pf["plan_hash"], self.p["plans"][method]); self.assertEqual(pf["plan"]["hash"], pf["plan_hash"]); self.assertNotIn("files", pf["plan"])
        pf = self.pre("hakchi-ready", self.p["plans"]["nucleus"]); self.assertEqual(pf["plan_hash"], self.p["plans"]["mod"], "a console with neither: CCS Mod (the app's install policy)")
        pf = self.pre("both-methods"); self.assertFalse(pf["ok"]); self.assertIn("both CCS Mod and CCS Nucleus", pf["why"])
        pf = self.pre("stock"); self.assertFalse(pf["ok"]); self.assertIn("hakchi must be installed first", pf["why"])
        r, _ = ask(self.w, "home-plan", {"mod_start": "on"}); pf = self.pre("other-module", r["result"]["hash"]); self.assertEqual((pf["plan"]["method"], pf["plan"]["mod_start"]), ("nucleus", None), "CCS Nucleus is not offered the start choice")

    def test_22_every_rehearsal_on_a_ccs_nucleus_console(self):
        h = self.pre("other-module")["plan_hash"]
        x, ev = self.sim(h, "other-module", scenario="first-install"); self.assertEqual(x["install"]["result"], "installed", x); self.assertEqual(x["method"], "nucleus"); self.assertTrue(x["stock_unchanged"])
        self.assertTrue(any(e.get("stage") == "readback" for e in ev)); self.assertFalse(any(e.get("stage") == "module" for e in ev), "CCS Nucleus is already there: no package step")
        self.assertEqual(self.sim(h, "other-module", scenario="repeat")[0]["install"]["result"], "no-change")
        x, _ = self.sim(h, "other-module", scenario="update"); self.assertEqual(x["install"]["result"], "installed"); self.assertTrue(x["install"]["tidy"][0]["removed"])
        x, _ = self.sim(h, "other-module", scenario="repair"); self.assertEqual(x["install"]["result"], "installed")
        x, _ = self.sim(h, "other-module", scenario="interrupted"); self.assertTrue(x["install"]["rolled_back"]["ok"]); self.assertEqual(x["after_recovery"], "installed")
        x, _ = self.sim(h, "other-module", scenario="first-install", then=["uninstall", "export"]); self.assertTrue(x["uninstall"]["ok"]); self.assertTrue(x["export"]["verified"]); self.assertTrue(x["stock_unchanged_after_uninstall"])

    def test_23_a_stale_method_in_a_rehearsal_and_the_start_choice(self):
        x, _ = self.sim(self.p["plans"]["nucleus"], "hakchi-ready"); self.assertEqual(x["install"]["result"], "refused"); self.assertIn("CCS Nucleus is not installed", x["install"]["why"]); self.assertTrue(x["stock_unchanged"])
        x, _ = self.sim(self.p["plans"]["mod"], "other-module"); self.assertEqual(x["install"]["result"], "refused"); self.assertIn("this console uses CCS Nucleus", x["install"]["why"])
        r, _ = ask(self.w, "home-plan", {"mod_start": "on"}); x, ev = self.sim(r["result"]["hash"], "mod-module")
        self.assertEqual((x["install"]["result"], x["install"]["mod_start"], x["mod_start"]), ("installed", "on", "on")); self.assertTrue(any(e.get("stage") == "start" for e in ev))

    def test_24_a_restaged_collection_makes_both_plans_obsolete(self):
        h = self.pre("other-module")["plan_hash"]; shutil.rmtree(self.st); staged(self.st, "changed")
        for plan in (h, self.p["hash"]):
            r, _ = ask(self.w, "home-preflight", {"plan_hash": plan, "profile": "other-module"}); self.assertEqual(r.get("code"), "plan-obsolete", r)

    def test_25_a_cancel_reaches_a_ccs_nucleus_rehearsal_in_its_own_process(self):
        big = staged(os.path.join(self.t, "big"), "big", extra={f"home/covers/c{i:04d}.png": (os.urandom(4000), "home", "0o644") for i in range(500)})
        root = os.path.join(self.t, "root2"); env = dict(os.environ, PYTHONPATH=HERE + os.pathsep + os.path.join(HERE, "..", "rom-pipeline") + os.pathsep + os.environ.get("PYTHONPATH", ""))
        p = subprocess.Popen([sys.executable, "-B", "-m", "console_engine.worker", "--root", root, "--console-package", big], cwd=HERE, env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, encoding="utf-8")
        try:
            def send(o): p.stdin.write(json.dumps({"v": 1, **o}) + "\n"); p.stdin.flush()
            def line(): return json.loads(p.stdout.readline())
            send({"id": "p", "cmd": "home-plan", "args": {}}); line(); send({"id": "f", "cmd": "home-preflight", "args": {"plan_hash": CI.build_plan(os.path.join(big, "mod"))["hash"], "profile": "other-module"}}); h = line()["result"]["plan_hash"]
            send({"id": "s", "cmd": "home-simulate", "args": {"plan_hash": h, "profile": "other-module"}})
            while True:
                e = line()
                if e.get("stage") == "stage" and e.get("done", 0) >= 5: break
            send({"id": "c", "cmd": "cancel-if-safe", "args": {}}); res = None; acc = None
            while res is None:
                e = line()
                if e.get("id") == "c": acc = e["result"]
                elif e.get("id") == "s" and e.get("event") in ("result", "error"): res = e
            self.assertTrue(acc and acc["accepted"], acc); i = res["result"]["install"]
            self.assertEqual(i["result"], "cancelled", i); self.assertTrue(i["rolled_back"]["ok"]); self.assertEqual(res["result"]["method"], "nucleus")
        finally:
            try: p.stdin.write("quit\n"); p.stdin.close()
            except OSError: pass
            p.wait(30); p.stdout.close()

class Dispatch(Base):
    """the physical operations through the app's dispatcher, with the test's own gate, confirmations and an in-process worker whose console is a
    stand-in (test_console_dispatch.py's harness); nothing opens a device"""
    def setUp(self):
        super().setUp(); import console_runs, console_home, console_confirm as CC
        from test_console_dispatch import InProc, Clock
        self.runs = console_runs; self.root = self.console("other-module", "console"); self.wroot = os.path.join(self.t, "worker"); self.faults = {}; self.clock = Clock()
        def factory(tier, pkg): return CI.FakeConsole(self.root, dict(self.faults))
        def spawn(): return InProc(root=self.wroot, console_package=self.st, console_factory=factory, allowed=lambda t: True)
        self.d = console_home.Dispatcher(allowed=lambda t: True, confirmations=CC.Confirmations(allowed=lambda t: True, clock=self.clock), clock=self.clock, spawn=spawn, admit=lambda h, o, s: {"ok": True, "ticket": "0" * 32})
    def wait(self, rid, timeout=120):
        end = time.time() + timeout
        while time.time() < end:
            p = self.runs.poll(rid, 0)
            if p and p["done"]: return p
            time.sleep(0.05)
        self.fail(f"run {rid} did not finish")
    def run_op(self, op, **body):
        code, r = self.d.post(op, body); self.assertEqual(code, 200, (op, r)); p = self.wait(r["run"]); self.assertEqual(p["result"]["event"], "result", (op, p["result"])); return p["result"]["result"]
    def confirm(self, op, **subject):
        code, r = self.d.post("confirm", {"operation": op, **subject}); self.assertEqual(code, 200, r); return r["confirmation"]

    def test_30_the_check_picks_ccs_nucleus_and_the_confirmation_names_that_plan(self):
        code, r = self.d.post("plan", {"mod_start": "on", "folder": "C:/x"}); self.assertEqual(code, 200, r); h = r["result"]["hash"]; self.assertEqual(r["result"]["mod_start"], "on")
        ins = self.run_op("inspect"); self.assertEqual((ins["method"], ins["mod_start"]), ("nucleus", None)); self.assertEqual(self.d.public_console()["state"]["method"], "nucleus")
        chk = self.run_op("check", plan_hash=h); self.assertTrue(chk["ok"], chk); self.assertTrue(chk["selected"]); hn = chk["plan_hash"]; self.assertEqual(chk["plan"]["method"], "nucleus")
        res = self.run_op("install", plan_hash=hn, confirmation=self.confirm("install", plan_hash=hn)); self.assertEqual(res["result"], "installed", res)
        self.assertEqual(CI.console_method(CI.FakeConsole(self.root)), "nucleus"); v = self.run_op("verify", plan_hash=hn); self.assertTrue(v["verified"], v)

    def test_31_the_console_changed_its_method_after_the_check(self):
        code, r = self.d.post("plan", {}); h = r["result"]["hash"]; self.run_op("inspect"); hn = self.run_op("check", plan_hash=h)["plan_hash"]; tok = self.confirm("install", plan_hash=hn)
        c = CI.FakeConsole(self.root); c.pack_uninstall("ccs-nucleus"); mp = CI.method_package("mod"); c.pack_install(mp["name"], mp["hmod"]); before = tree(self.root)
        res = self.run_op("install", plan_hash=hn, confirmation=tok); self.assertEqual(res["result"], "refused", res); self.assertIn("this console uses CCS Mod", res["why"]); self.assertEqual(changes(before, tree(self.root)), [])

    def test_33_the_worker_ends_during_the_installation_and_a_new_app_session_recovers_it(self):
        import console_home, console_confirm as CC
        from test_console_dispatch import InProc
        code, r = self.d.post("plan", {}); h = r["result"]["hash"]; before = tree(self.root); self.run_op("inspect"); hn = self.run_op("check", plan_hash=h)["plan_hash"]
        self.faults = {"crash_after": 9}   # the worker process ends right after a change (as when the app is closed or the PC stops)
        code, r = self.d.post("install", {"plan_hash": hn, "confirmation": self.confirm("install", plan_hash=hn)}); self.assertEqual(code, 200, r); p = self.wait(r["run"])
        self.assertEqual((p["result"]["event"], p["result"].get("code")), ("error", "worker-ended"), p["result"]); self.assertTrue(CI.read_records(CI.FakeConsole(self.root))["attempts"], "the console keeps the attempt's record")
        self.faults = {}
        def factory(tier, pkg): return CI.FakeConsole(self.root, {})
        self.d = console_home.Dispatcher(allowed=lambda t: True, confirmations=CC.Confirmations(allowed=lambda t: True, clock=self.clock), clock=self.clock,
                                         spawn=lambda: InProc(root=self.wroot, console_package=self.st, console_factory=factory, allowed=lambda t: True), admit=lambda h, o, s: {"ok": True, "ticket": "0" * 32})
        self.run_op("inspect"); rr = self.run_op("recover-review"); a = rr["attempts"][0]; self.assertTrue(a["recoverable"], a)   # a new app session: the same records on this PC
        rec = self.run_op("recover", attempt=a["attempt"], confirmation=self.confirm("recover", attempt=a["attempt"])); self.assertEqual(rec["result"], "rolled-back", rec)
        self.assertEqual(changes(before, tree(self.root)), []); self.assertEqual(CI.console_method(CI.FakeConsole(self.root)), "nucleus")

    def test_32_a_lost_connection_stops_and_its_recovery_is_its_own_confirmed_operation(self):
        code, r = self.d.post("plan", {}); h = r["result"]["hash"]; before = tree(self.root); self.run_op("inspect"); hn = self.run_op("check", plan_hash=h)["plan_hash"]
        self.faults = {"after": 9}; res = self.run_op("install", plan_hash=hn, confirmation=self.confirm("install", plan_hash=hn)); self.assertEqual(res["result"], "stopped", res); self.assertIsNone(res["rolled_back"])
        self.faults = {}; self.run_op("inspect"); rr = self.run_op("recover-review"); a = rr["attempts"][0]; self.assertTrue(a["recoverable"], a)
        rec = self.run_op("recover", attempt=a["attempt"], confirmation=self.confirm("recover", attempt=a["attempt"])); self.assertEqual(rec["result"], "rolled-back", rec)
        self.assertEqual(changes(before, tree(self.root)), [])

PAGE = r"""
const vm=require('vm'), fs=require('fs');
const [JS, B, TOKEN]=process.argv.slice(1);
const els={}; const mk=id=>({id, innerHTML:'', textContent:'', disabled:false, checked:false, value:'', onclick:null, onchange:null, classList:{add(){}, remove(){}, contains(){return false;}}});
const $=id=>{ const h=Object.values(els).map(e=>e.innerHTML).join(''); if(!els[id]&&!['hkBody','hkTitle','hkSub','hkpanel','nhPkg'].includes(id)&&!h.includes(`id="${id}"`)) return null; return els[id]||(els[id]=mk(id)); };
const H={'Content-Type':'application/json','X-NES-Protocol':'1',Origin:'https://classiccartstudio.com',Authorization:'Bearer '+TOKEN};
async function hkPost(p,b){ const r=await fetch(B+p,{method:'POST',headers:H,body:JSON.stringify(b||{})}); const j=await r.json(); return Object.assign({status:r.status}, j); }
async function capi(p){ const r=await fetch(B+p,{headers:H}); return r.json(); }
const ctx=vm.createContext({console, setTimeout, clearTimeout, setInterval, clearInterval, fetch, $, hkPost, capi, document:{addEventListener(){}, querySelectorAll(){return [];}},
  esc:s=>String(s==null?'':s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;'), fmt:n=>n+' B', COMPANION:{ok:true, desktop:true, capabilities:['console-sim']},
  INST:{step:'review'}, STEPS:[], stepperHtml:()=>'', renderInstall:async()=>{}, procSet(){}, PROC:{}, HOME_STARTUP:[], HOME_THEME_META:[], homeModelFromStudio:()=>({theme:'arcade-grid'}), dispatchEvent(){}, CustomEvent:function(){} });
ctx.window=ctx; vm.runInContext(fs.readFileSync(JS,'utf8'), ctx);
const R=(code)=>vm.runInContext(code, ctx);
const wait=async(f,ms)=>{ for(let i=0;i<(ms||60000)/100;i++){ if(await f()) return true; await new Promise(r=>setTimeout(r,100)); } return false; };
(async()=>{ const out={};
  const review=async(profile)=>{ R(`CONSOLE.profile=${JSON.stringify(profile)}; CONSOLE.plan=null; CONSOLE.preflight=null; INST.step='review';`); await R('renderConsoleStep()');
    await wait(()=>R(`!!(CONSOLE.preflight&&CONSOLE.preflight._p)`)); await R('new Promise(r=>setTimeout(r,200))'); return {html:els.cPlan?els.cPlan.innerHTML:'', plan:R('CONSOLE.plan')}; };
  let v=await review('hakchi-ready'); out.mod={method:v.plan.method, start:v.plan.mod_start, names:v.html.includes('Installs into CCS Mod for Hakchi'), box:v.html.includes('id="cModStart"'), ready:v.html.includes('ready')};
  els.cModStart.checked=true; await els.cModStart.onchange(); await wait(()=>R(`!!(CONSOLE.plan&&CONSOLE.plan.mod_start==='on'&&CONSOLE.preflight&&CONSOLE.preflight._p)`)); await R('new Promise(r=>setTimeout(r,200))');
  out.mod_on={start:R('CONSOLE.plan.mod_start'), words:els.cPlan.innerHTML.includes('When the console is turned on, CCS HOME opens first'), turns:els.cPlan.innerHTML.includes('This turns on CCS HOME at power-on'), checked:els.cPlan.innerHTML.includes('id="cModStart" checked')};
  v=await review('other-module'); out.nucleus={method:v.plan.method, start:v.plan.mod_start, names:v.html.includes('Installs into CCS Nucleus'), box:v.html.includes('id="cModStart"'), chooser:v.html.includes('stays as it is: its start chooser')};
  v=await review('both-methods'); out.both={blocked:v.html.includes('blocked'), why:v.html.includes('both CCS Mod and CCS Nucleus')};
  for(const profile of ['other-module','hakchi-ready']){ await review(profile); R("INST.step='install'"); await R('renderConsoleStep()'); R("CONSOLE.scenario='first-install'");
    await els.hkSim.onclick(); await wait(()=>R("INST.step==='verify'"), 120000); await R('renderConsoleStep()');
    out['rehearse_'+profile]={installed:R("!!(CONSOLE.result&&CONSOLE.result.result&&CONSOLE.result.result.install.result==='installed')"), method:R("CONSOLE.result&&CONSOLE.result.result&&CONSOLE.result.result.method"), shown:els.hkBody.innerHTML.includes('Installed and checked on the simulated console')}; }
  console.log(JSON.stringify(out)); })().catch(e=>{ console.log(JSON.stringify({error:String(e&&e.stack||e)})); process.exit(1); });
"""

PORT = 8774; B = f"http://127.0.0.1:{PORT}"
@unittest.skipUnless(shutil.which("node"), "node is not installed")
class Page(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.t = tempfile.mkdtemp(prefix="ccs-meth-page-", dir=_T); cls.nh = os.path.join(cls.t, "native-home"); staged(os.path.join(cls.nh, "console", "current"))
        cfgp = os.path.join(cls.t, "companion.json"); wr(cfgp, json.dumps({"hakchi_exe": os.path.join(cls.t, "no-hakchi", "hakchi.exe"), "port": PORT, "no_launch": True, "exports_dir": os.path.join(cls.t, "exports"), "staging_dir": os.path.join(cls.t, "staging")}))
        cls.token = secrets.token_hex(32); env = dict(os.environ, NES_COMPANION_CONFIG=cfgp, NES_PERSONAL_DIR=os.path.join(cls.t, "personal"), NES_STUDIO_DATA=cls.t, CCS_NATIVE_HOME_ROOT=cls.nh,
                                                       CCS_HOST_TOKEN=cls.token, CCS_HOST_PID=str(os.getpid()), PYTHONIOENCODING="utf-8")
        cls.log = open(os.path.join(cls.t, "companion.log"), "w", encoding="utf-8")
        cls.proc = subprocess.Popen([sys.executable, "-u", "-B", os.path.join(HERE, "companion_app.py"), "--headless", "--port", str(PORT)], env=env, stdout=cls.log, stderr=subprocess.STDOUT)
        for _ in range(200):
            try:
                with urllib.request.urlopen(B + "/api/ping", timeout=1): break
            except Exception: time.sleep(0.25)
    @classmethod
    def tearDownClass(cls):
        cls.proc.terminate()
        try: cls.proc.wait(20)
        except subprocess.TimeoutExpired: cls.proc.kill(); cls.proc.wait(10)
        cls.log.close()
    def test_40_the_page_reviews_and_rehearses_each_method(self):
        js = os.path.join(HERE, "..", "rom-pipeline", "studio", "install.js")
        r = subprocess.run([shutil.which("node"), "-e", PAGE, js, B, self.token], capture_output=True, text=True, timeout=600, encoding="utf-8")
        self.assertEqual(r.returncode, 0, (r.stdout[-1500:], r.stderr[-1500:])); out = json.loads(r.stdout.strip().splitlines()[-1])
        self.assertEqual(out["mod"], {"method": "mod", "start": "off", "names": True, "box": True, "ready": True}, out)
        self.assertEqual(out["mod_on"], {"start": "on", "words": True, "turns": True, "checked": True}, out)
        self.assertEqual(out["nucleus"], {"method": "nucleus", "start": None, "names": True, "box": False, "chooser": True}, out)
        self.assertEqual(out["both"], {"blocked": True, "why": True}, out)
        for prof, m in (("other-module", "nucleus"), ("hakchi-ready", "mod")): self.assertEqual(out["rehearse_" + prof], {"installed": True, "method": m, "shown": True}, out)

REAL = ("NES_STUDIO_SITE", "CCS_CONSOLE_PLAYER", "CCS_NATIVE_HOME_RUNTIME", "CCS_PLAYER_FONTS")
PORT2 = 8775; B2 = f"http://127.0.0.1:{PORT2}"
@unittest.skipUnless(all(os.environ.get(k) and os.path.exists(os.environ[k]) for k in REAL), "the built site, console player, HOME runtime and player fonts are not given (" + ", ".join(REAL) + ")")
class RealPackage(unittest.TestCase):
    """the app's own route: /api/home/native-package builds the native HOME package and stages the console package in both layouts (the real
    builder, an Included Game from the built site, the collection's own startup picture), then the review and a rehearsal of each method"""
    @classmethod
    def setUpClass(cls):
        cls.t = tempfile.mkdtemp(prefix="ccs-meth-real-", dir=_T); cls.nh = os.path.join(cls.t, "native-home")
        if "personal" not in sys.modules: os.environ["NES_PERSONAL_DIR"] = os.path.join(cls.t, "personal")
        import personal as ps, library_lookup as LL
        pdir = ps.PERSONAL_DIR   # the backend reads the same personal library
        site = os.environ["NES_STUDIO_SITE"]; idx = json.load(open(os.path.join(site, "included", "index.json"), encoding="utf-8")); g = idx["games"][0]
        r = ps.ingest(os.path.join(site, "included", g["file_name"]), origin="included"); cls.pid = pid = r["entry"]["id"]; ps.merge_metadata(pid, {"title": g["title"], "version_label": g["version"]})
        title = LL.personal_index()["entries"][pid]["user"]["title"]
        import test_startup_custom_package as SC, base64
        jp = SC.jpeg(); h = sha(jp); cls.pic = h
        cls.man = {"schema": "nes-classic-collection/4", "name": "Method test", "hidden_factory_games": [], "selections": [{"game_id": pid, "version_id": pid, "display_title": title, "source": "personal", "install_required": True}]}
        cls.home = {"schema": "ccs-home-collection/1", "theme": "arcade-grid", "order": [pid], "games": [{"id": pid, "title": title, "version_label": "", "kind": "personal"}], "folders": [], "favorites": {"items": {}},
                    "settings": {"reduced_motion": False, "hints": True}, "sample": False, "map_shortcuts": [None, None], "apply_theme": False, "startup_image": "custom-" + h[:16],
                    "startup_custom": {"id": "custom-" + h[:16], "sha256": h, "name": "Living room", "fit": "fill", "bytes": len(jp), "width": 1280, "height": 720, "data": base64.b64encode(jp).decode()}}
        cfgp = os.path.join(cls.t, "companion.json"); wr(cfgp, json.dumps({"hakchi_exe": os.path.join(cls.t, "no-hakchi", "hakchi.exe"), "port": PORT2, "no_launch": True, "exports_dir": os.path.join(cls.t, "exports"), "staging_dir": os.path.join(cls.t, "staging")}))
        cls.token = secrets.token_hex(32); env = dict(os.environ, NES_COMPANION_CONFIG=cfgp, NES_PERSONAL_DIR=pdir, NES_STUDIO_DATA=cls.t, CCS_NATIVE_HOME_ROOT=cls.nh, CCS_HOST_TOKEN=cls.token, CCS_HOST_PID=str(os.getpid()), PYTHONIOENCODING="utf-8")
        cls.log = open(os.path.join(cls.t, "companion.log"), "w", encoding="utf-8")
        cls.proc = subprocess.Popen([sys.executable, "-u", "-B", os.path.join(HERE, "companion_app.py"), "--headless", "--port", str(PORT2)], env=env, stdout=cls.log, stderr=subprocess.STDOUT)
        for _ in range(200):
            try:
                with urllib.request.urlopen(B2 + "/api/ping", timeout=1): break
            except Exception: time.sleep(0.25)
    @classmethod
    def tearDownClass(cls):
        cls.proc.terminate()
        try: cls.proc.wait(20)
        except subprocess.TimeoutExpired: cls.proc.kill(); cls.proc.wait(10)
        cls.log.close()
    def req(self, method, path, body=None):
        r = urllib.request.Request(B2 + path, data=json.dumps(body).encode() if body is not None else None, method=method, headers={"Content-Type": "application/json", "X-NES-Protocol": "1", "Origin": "https://classiccartstudio.com", "Authorization": "Bearer " + self.token})
        try:
            with urllib.request.urlopen(r, timeout=600) as resp: return resp.status, json.loads(resp.read() or b"{}")
        except urllib.error.HTTPError as e: return e.code, json.loads(e.read() or b"{}")
    def build(self, man, home):
        s, j = self.req("POST", "/api/home/native-package", {"manifest": man, "home": home}); self.assertEqual(s, 200, j)
        for _ in range(3000):
            s, q = self.req("GET", f"/api/home/native-package?job={j['job']}")
            if q.get("state") == "done": return q["result"]
            time.sleep(0.2)
        self.fail("the package was not prepared")
    def run_wait(self, rid):
        for _ in range(3000):
            s, q = self.req("GET", f"/api/console/run?id={rid}")
            if q.get("done"): return q["result"]
            time.sleep(0.2)
        self.fail("the rehearsal did not finish")
    def test_50_the_real_package_in_both_layouts_reviewed_and_rehearsed(self):
        res = self.build(self.man, self.home); c = res.get("console") or {}; self.assertTrue(res.get("ok") and c.get("ok"), (res.get("error"), c)); self.assertEqual(c["layouts"], ["mod", "nucleus"])
        cur = os.path.join(self.nh, "console", "current"); a, b = os.path.join(cur, "mod"), os.path.join(cur, "nucleus")
        self.assertTrue(CPK.verify(a)["ok"] and CPK.verify(b)["ok"]); ma = json.loads(rd(os.path.join(a, "MANIFEST.json")))
        self.assertEqual([r for r in ma["files"] if rd(os.path.join(a, *r.split("/"))) != rd(os.path.join(b, *r.split("/")))], ["session.json"])
        st = json.loads(rd(os.path.join(b, "startup", "startup.json"))); self.assertEqual(st["choice"], "custom-" + self.pic[:16]); self.assertEqual(ma["startup"]["custom"]["source_sha256"], self.pic, "the collection's own picture in both")
        s, j = self.req("GET", "/api/console/home/status"); self.assertEqual(j["staged"]["layouts"].keys(), {"mod", "nucleus"}); self.assertEqual(j["staged"]["layout"], "mod")
        s, j = self.req("POST", "/api/console/home/plan", {}); self.assertEqual(s, 200, j); h = j["result"]["hash"]
        s, j = self.req("POST", "/api/console/home/preflight", {"plan_hash": h, "profile": "other-module"}); pf = j["result"]; self.assertTrue(pf["ok"], pf); self.assertEqual(pf["plan"]["method"], "nucleus")
        s, j = self.req("POST", "/api/console/home/rehearse", {"plan_hash": pf["plan_hash"], "profile": "other-module", "scenario": "update", "then": ["uninstall"]}); x = self.run_wait(j["run"])["result"]
        self.assertEqual((x["install"]["result"], x["method"]), ("installed", "nucleus"), x); self.assertTrue(x["uninstall"]["ok"])
        s, j = self.req("POST", "/api/console/home/plan", {"mod_start": "on"}); s, j = self.req("POST", "/api/console/home/rehearse", {"plan_hash": j["result"]["hash"], "profile": "hakchi-ready", "scenario": "first-install"})
        x = self.run_wait(j["run"])["result"]; self.assertEqual((x["install"]["result"], x["install"]["mod_start"]), ("installed", "on"), x)
        # a game whose file is not here: the whole collection is refused and the staged package stays the one before
        before = rd(os.path.join(a, "MANIFEST.json")); man2 = json.loads(json.dumps(self.man)); home2 = json.loads(json.dumps(self.home))
        man2["selections"].append({"game_id": "p_00000000deadbeef", "version_id": "p_00000000deadbeef", "display_title": "Missing", "source": "personal", "install_required": True})
        home2["order"].append("p_00000000deadbeef"); home2["games"].append({"id": "p_00000000deadbeef", "title": "Missing", "version_label": "", "kind": "personal"})
        res2 = self.build(man2, home2); c2 = res2.get("console") or {}
        self.assertFalse(c2.get("ok"), c2) if res2.get("ok") else self.assertFalse(res2.get("ok"))
        self.assertEqual(rd(os.path.join(a, "MANIFEST.json")), before, "the package staged before stays as it was"); self.assertTrue(CPK.verify(b)["ok"])

if __name__ == "__main__":
    try:
        r = unittest.main(exit=False, verbosity=2).result
        print(f"RESULT {r.testsRun - len(r.failures) - len(r.errors) - len(r.skipped)}/{r.testsRun} passed, {len(r.skipped)} skipped"); code = 1 if r.failures or r.errors else 0
    finally: shutil.rmtree(_T, ignore_errors=True)
    sys.exit(code)
