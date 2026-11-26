r"""Classic Cart Studio's own collection on a console with the CCS add-ons (CCS Mod, CCS Nucleus and its startup chooser), PC only (stand-ins and
an in-process worker; no device, socket to a console or shell). Nothing here is a real console: every console below is a disposable folder.
  staging   the app stages the collection's console package in CCS HOME's one layout (mod): its leave item, Choose Another Menu, is decided
            on the console; MANIFEST.json as the builder writes it; no other layout is made or planned
  choice    the add-on choice: Studio's default (CCS Mod, no CCS Nucleus), every combination's start line and plain words, a choice that
            cannot be made refused in plain words (CCS HOME at power-on without CCS Mod)
  engine    the console's add-ons read from it (each profile; a program without its package, an unknown package, changed files, a chooser
            without CCS Nucleus and unreadable CCS Nucleus settings refused, and the chooser refused by the console without CCS Nucleus); all
            four CCS Mod / CCS Nucleus combinations, CCS Nucleus direct or with its chooser, start Original Menu or CCS HOME, the chooser's
            auto menu, last or wait: the missing packages added in order and CCS Nucleus's start set by its own command and read back; a
            choice that would remove an add-on refused with the console's add-ons and the nearest choice that keeps them; an add-on or a start
            change alone copies nothing again; every change of five installations fails in turn (before and after) and is undone exactly
            (CCS Nucleus's start put back as it was) or finished; a crash after any change is recovered from its journal; a cancel before the
            switch is undone; a start that began with one of the console owner's own system images cannot be put back by CCS Nucleus's start command
            and the rollback says so; the uninstall removes only the add-ons this program's records say it installed, CCS Nucleus's chooser
            before CCS Nucleus, never CCS Nucleus while a chooser that stays needs it, and an interrupted uninstall finishes when run again
  worker    the plan takes the choice; the check answers with the console's add-ons and what the plan adds or changes, or the refusal with
            the choice that keeps them; rehearsals of every scenario on a console with CCS Nucleus; a cancel in the worker's own process
  dispatch  the physical flow through the app's dispatcher with injected sessions: only the choice passes through, inspect reports the
            add-ons in plain words, the confirmation and the installation are for exactly the reviewed plan; another tool added an add-on
            after the check: refused, nothing changed; a lost connection stops and its recovery is its own confirmed operation; the worker
            ends during the installation and a new app session recovers it from this computer's records
  page      the Install on Console page (install.js): the two add-on pages, the review's summary and a rehearsal; the review's Hakchi games
            choice (its default from the plan, a choice with neither CCS Mod nor Hakchi games refused with the choice still there), and a
            collection without CCS Mod reviewed and rehearsed as its game files only, a game the console's own emulator cannot run named
  python tools/companion/test_install_methods.py      Exit 0 = PASS."""
from __future__ import annotations
import hashlib, io, json, os, secrets, shutil, subprocess, sys, tempfile, time, unittest, urllib.error, urllib.request
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path[:0] = [HERE, os.path.join(HERE, "..", "rom-pipeline")]
import console_test_guard   # noqa: E402,F401  (first: the controlled console boundary, k3e)
_T = tempfile.mkdtemp(prefix="ccs-addons-"); os.environ.setdefault("CCS_STUDIO_DATA", _T)
from console_engine import ccs_install as CI   # noqa: E402
from console_engine.worker import Worker   # noqa: E402
from native_home import console_package as CPK   # noqa: E402
from test_ccs_install import rd, wr, tree, changes, journal_ops, chooser_env, registry_line   # noqa: E402  (also sets the chooser's stand-in inputs and keeps this PC's own Workbench settings out)

def sha(b): return hashlib.sha256(b).hexdigest()
def ch(**k) -> dict: return CI.addon_choice(k)

def staged(d: str, tag: str = "a", extra: dict | None = None, more: list | None = None) -> str:
    """a console package as the app stages it: d/mod built (synthetic files, the builder's MANIFEST.json form). Its game is a complete
    cartridge image of zeros (not a game) that Nintendo's built-in emulator would run; more: further (id, title, cartridge image) games"""
    m = os.path.join(d, "mod"); L = CPK.LAYOUTS["mod"]
    more_files = {f"roms/{sha(b[16:])}.nes": (b, "rom", "0o644") for _, _, b in (more or [])}
    sess = {"schema": "ccs-play-session/1", "mode": "home", "saves_root": "/var/lib/ccs/data/saves", "games": [{"id": "g1", "title": f"Game {tag}", "rom": "/tmp/ccs/pkg/roms/" + "ab" * 32 + ".nes", "content_sha256": "ab" * 32}]
            + [{"id": gid, "title": t, "rom": f"/tmp/ccs/pkg/roms/{sha(b[16:])}.nes", "content_sha256": sha(b[16:])} for gid, t, b in (more or [])],
            "home": {"pack": "/tmp/ccs/pkg/home", "state_dir": "/var/lib/ccs/data/state", "design_root": "/var/lib/ccs/data/home-design", "leave": L["leave"]}}
    files = {"bin/ccs-player-armv7": (b"\x7fELF\x01" + tag.encode() * 4000, "executable", "0o755"), "home/pack.json": (b'{"schema":"ccs-native-home-pack/2"}', "home", "0o644"),
             "home/design/design.json": (b'{"schema":"ccs-home-design-compiled/2","tag":"' + tag.encode() + b'"}', "home", "0o644"),
             "roms/" + "ab" * 32 + ".nes": (b"NES\x1a\x01\x01" + bytes(10) + bytes(24576), "rom", "0o644"), "startup/startup.json": (b'{"choice":"custom-0123456789abcdef"}', "configuration", "0o644"),
             "startup/startup-custom-0123456789abcdef.jpg": (b"\xff\xd8\xff" + tag.encode() * 300, "startup-image", "0o644"), "config/controller-map.txt": (b"key 304 a\n", "configuration", "0o644"),
             "session.json": ((json.dumps(sess, indent=1, ensure_ascii=False) + "\n").encode(), "configuration", "0o644")} | more_files | (extra or {})
    for rel, (b, _, _) in files.items(): wr(os.path.join(m, *rel.split("/")), b)
    fm = {rel: {"size": len(b), "sha256": sha(b), "kind": k, "mode": mo} for rel, (b, k, mo) in sorted(files.items())}
    man = {"schema": "ccs-console-package/1", "label": f"test {tag}", "layout": "mod", "console_paths": {k: L[k] for k in ("pkg", "data", "scratch", "logs")}, "created_utc": "2026-11-16T00:00:00Z",
           "games": 1 + len(more or []), "playable": 1 + len(more or []), "unavailable": [], "startup": {"choice": "custom-0123456789abcdef"}, "player": {"file": "bin/ccs-player-armv7", "sha256": fm["bin/ccs-player-armv7"]["sha256"]},
           "sizes": {k: sum(f["size"] for f in fm.values() if f["kind"] == k) for k in sorted({f["kind"] for f in fm.values()})}, "total_bytes": sum(f["size"] for f in fm.values()), "files": fm}
    wr(os.path.join(m, "MANIFEST.json"), (json.dumps(man, indent=1, ensure_ascii=False) + "\n").encode())
    return d

def listed(root: str) -> list[str]:   # the add-on packages hakchi lists on the stand-in, in install order
    c = CI.FakeConsole(root); return [n for n in CI.ADDONS.values() if c.method_files(n) is not None]
def names(addons) -> list[str]: return [CI.ADDONS[a] for a in addons]

def add_owner_image(root: str, default: bool = True):
    """one of the console owner's own system images as a CCS Nucleus option (as its import command records it), the start's default when asked"""
    c = CI.FakeConsole(root)
    def add(lines): return [(k, v + " snes" if k == "options" else v) for k, v in lines] + [("opt.snes.kind", "owner-root"), ("opt.snes.label", "SNES"), ("opt.snes.image", "/var/lib/ccs/nucleus/roots/snes.hsqs"),
                                                                                         ("opt.snes.sha256", "ab" * 32), ("opt.snes.bytes", "4096"), ("opt.snes.fallback", "native")]
    assert c._reg_edit(add)
    if default: assert c._reg_set("default", "snes")

class Base(unittest.TestCase):
    def setUp(self):
        self.t = tempfile.mkdtemp(prefix="ccs-add-", dir=_T); self.st = staged(os.path.join(self.t, "staged-a")); self.n = 0
        self.jdir = os.path.join(self.t, "journals"); os.makedirs(self.jdir)
    def tearDown(self): shutil.rmtree(self.t, ignore_errors=True)
    def jnew(self): self.n += 1; return os.path.join(self.jdir, f"j{self.n}.jsonl")
    def console(self, profile: str, name: str | None = None) -> str:
        root = os.path.join(self.t, name or profile); CI.make_stand_in(root, profile); return root
    def pkg(self, st: str | None = None) -> str: return CI.staged_package(st or self.st)
    def plan(self, choice: dict | None = None, st: str | None = None): return CI.build_plan(self.pkg(st), choice)
    def run_inst(self, root, plan, st=None, **faults):
        return CI.Installer(CI.FakeConsole(root, faults), plan, self.pkg(st), self.jnew()).run()
    def pointer(self, root): b = CI.FakeConsole(root).read(CI.POINTER, 64); return b.decode().strip() if b else None
    @staticmethod
    def rest(before: dict, root: str) -> list:
        """what changed, but CCS Nucleus's own settings where its own commands leave them: its registry is rewritten (with a new generation)
        when its start is put back, so it is compared by the start it holds instead (the callers do); and when CCS Nucleus was not there
        before, the settings its own removal keeps (without CCS Nucleus they change nothing at power-on)"""
        nuc_before = "var/lib/hakchi/hmod/uninstall-ccs-nucleus" in before
        return [k for k in changes(before, tree(root)) if not (k.startswith("var/lib/ccs/nucleus/") and (not nuc_before or k == "var/lib/ccs/nucleus/registry"))]
    def changes_until(self, plan, profile, what, setup=None) -> int:
        """the place, among an installation's console changes, of the first change named what (counted on a fresh copy of the profile)"""
        root = self.console(profile, "n" + secrets.token_hex(3))
        if setup: setup(root)
        c = CI.FakeConsole(root); marks = []; orig = c._change
        def change(w, act): marks.append(w); return orig(w, act)
        c._change = change; self.assertTrue(CI.Installer(c, plan, self.pkg(), self.jnew()).run()["ok"])
        return marks.index(what) + 1

class Staging(Base):
    def test_01_one_layout_whose_leave_item_the_console_decides(self):
        a = os.path.join(self.st, "mod"); ma = json.loads(rd(os.path.join(a, "MANIFEST.json")))
        self.assertEqual(ma["layout"], "mod"); self.assertTrue(CPK.verify(a)["ok"]); self.assertEqual(sorted(os.listdir(self.st)), ["mod"])
        self.assertEqual(rd(os.path.join(a, "MANIFEST.json")), (json.dumps(ma, indent=1, ensure_ascii=False) + "\n").encode(), "MANIFEST.json written as the builder writes it")
        self.assertEqual(CI.staged_package(self.st), a); self.assertEqual(CI.staged_package(a), a, "a single package folder is the package"); self.assertIsNone(CI.staged_package(os.path.join(self.t, "none")))
        self.assertNotIn("nucleus", CPK.LAYOUTS, "CCS Mod is the only provider of CCS HOME: one layout")
        lv = CPK.LAYOUTS["mod"]["leave"]; self.assertEqual(lv["label"], "Choose Another Menu"); self.assertIn("startup chooser", lv["text"]); self.assertIn("Original Menu (Nintendo's own menu)", lv["text"])
        self.assertEqual(json.loads(rd(os.path.join(a, "session.json")))["home"]["leave"], lv)
        self.assertIn("unknown layout", CPK.build_console({}, {}, os.path.join(self.t, "x"), layout="nucleus")["error"])
        self.assertFalse(hasattr(CPK, "derive_layout"), "nothing derives another layout")

class Choice(Base):
    def test_05_the_default_every_combination_and_the_refusals(self):
        self.assertEqual(CI.addon_choice(), {"mod": True, "nucleus": "none", "start": "native", "auto": "menu", "countdown": 10, "at_start": True}, "Studio's default")
        self.assertEqual(CI.addon_choice({"nucleus": "none", "start": "ccs-home", "auto": "wait"}), CI.STUDIO_CHOICE, "start and auto count only with CCS Nucleus")
        self.assertEqual(CI.addon_choice({"nucleus": "direct", "auto": "last"})["auto"], "last", "with CCS Nucleus straight away, the menu used last opens (the console's own setting is kept, never rewritten)")
        self.assertEqual(CI.start_line(ch(nucleus="direct", auto="last")), "native never on last 10")
        seen = {}
        for mod in (True, False):
            for nuc in CI.NUCLEUS_PROFILES:
                for start in CI.STARTS:
                    for auto in CI.AUTOS:
                        if nuc != "none" and start == "ccs-home" and not mod:
                            with self.assertRaises(CI.Refused) as e: CI.addon_choice({"mod": mod, "nucleus": nuc, "start": start, "auto": auto})
                            self.assertEqual(e.exception.why, CI.HOME_NEEDS_MOD); continue
                        c = CI.addon_choice({"mod": mod, "nucleus": nuc, "start": start, "auto": auto}); seen[json.dumps(c, sort_keys=True)] = (CI.start_line(c), CI.start_text(c))
        self.assertEqual(len(seen), 20, "every distinct choice: without CCS Nucleus 2, straight away 9 and with the chooser 9 (CCS HOME needs CCS Mod)")
        self.assertEqual(len({l for l, _ in seen.values()}), 19, "each choice with CCS Nucleus has its own start line (both without it have none)")
        self.assertEqual(CI.start_text(ch()), "When the console is turned on, Original Menu opens, as it does now. CCS HOME opens from its tile there.")
        self.assertIsNone(CI.start_line(ch(mod=False)))
        self.assertEqual(CI.start_line(ch(nucleus="direct")), "native never on default 10"); self.assertEqual(CI.start_line(ch(mod=False, nucleus="direct")), "native never off default 10")
        self.assertEqual(CI.start_line(ch(nucleus="direct", start="ccs-home")), "ccs-home never on default 10")
        self.assertEqual(CI.start_text(ch(nucleus="direct", start="ccs-home")), "When the console is turned on, CCS HOME opens straight away. The startup chooser is off.")
        self.assertEqual(CI.start_line(ch(nucleus="chooser")), "native always on default 10"); self.assertEqual(CI.start_line(ch(nucleus="chooser", auto="last", start="ccs-home")), "ccs-home always on last 10")
        self.assertEqual(CI.start_line(ch(mod=False, nucleus="chooser", auto="wait")), "native always off default 0")
        self.assertEqual(CI.start_text(ch(nucleus="chooser", auto="wait")), "When the console is turned on, the startup chooser appears first. Nothing starts until you choose.")
        self.assertEqual(CI.start_text(ch(nucleus="chooser", auto="last", start="ccs-home")), "When the console is turned on, the startup chooser appears first. If you do nothing, the menu you used last starts in 10 seconds (CCS HOME the first time).")
        self.assertEqual(CI.start_text(ch(nucleus="chooser")), "When the console is turned on, the startup chooser appears first. If you do nothing, Original Menu starts in 10 seconds.")
        for bad, words in (({"mod": "yes"}, "CCS Mod"), ({"nucleus": "maybe"}, "CCS Nucleus"), ({"nucleus": "direct", "start": "Snes!"}, "what opens"), ({"nucleus": "chooser", "auto": "soon"}, "starts by itself"),
                           ({"nucleus": "chooser", "countdown": 100}, "counts down"), ({"nucleus": "chooser", "countdown": 0}, "counts down"), ({"nucleus": "chooser", "at_start": "no"}, "shows at start")):
            with self.assertRaises(CI.Refused, msg=bad) as e: CI.build_plan(self.pkg(), bad)
            self.assertIn(words, e.exception.why)
        with self.assertRaises(CI.Refused) as e: CI.build_plan(self.pkg(), {"mod": False, "nucleus": "direct", "start": "ccs-home"})
        self.assertEqual(e.exception.why, CI.HOME_NEEDS_MOD)
        p = self.plan(ch(nucleus="chooser", start="ccs-home", auto="last"))
        self.assertEqual(list(p["packages"]), ["mod", "nucleus", "nucleus-chooser"]); self.assertEqual(p["start_line"], "ccs-home always on last 10"); self.assertIn("start", p["steps"])
        self.assertEqual(p["addons_text"], "Afterwards this console has CCS Mod and CCS Nucleus with its startup chooser."); self.assertEqual(p["notes"], [])
        self.assertNotEqual(p["hash"], self.plan(ch(nucleus="chooser", start="ccs-home"))["hash"], "the choice is part of the plan")
        g = self.plan(ch(mod=False, nucleus="direct")); self.assertEqual(g["delivery"], "games"); self.assertTrue(g["notes"][0].startswith("Without CCS Mod, CCS HOME is not installed"))
        self.assertTrue(g["hakchi"]["publish"], "without CCS Mod the Hakchi games are the default"); self.assertNotIn("switch", g["steps"]); self.assertIn("CCS HOME is not installed.", g["hakchi_text"])
        self.assertEqual(self.plan(ch())["delivery"], "home")
        for choice in (ch(mod=False), ch(mod=False, nucleus="direct"), ch(mod=False, nucleus="chooser", auto="wait")):
            with self.assertRaises(CI.Refused) as e: CI.build_plan(self.pkg(), choice, False)
            self.assertEqual((e.exception.step, e.exception.why), ("addons", CI.NOTHING_TEXT), "without CCS Mod and without Hakchi games nothing of the collection would go to the console")

class Addons(Base):
    def test_10_the_consoles_addons_are_read_from_it(self):
        exp = {"hakchi-ready": ({"mod": False, "nucleus": "none", "start": "native", "auto": "menu", "countdown": 10, "at_start": True}, None, "This console has no CCS add-ons. When the console is turned on, Original Menu opens."),
               "with-mod": ({"mod": True, "nucleus": "none", "start": "native", "auto": "menu", "countdown": 10, "at_start": True}, None, "This console has CCS Mod. When the console is turned on, Original Menu opens. CCS HOME opens from its tile there."),
               "with-nucleus": ({"mod": False, "nucleus": "direct", "start": "native", "auto": "menu", "countdown": 10, "at_start": True}, "native never off default 10", "This console has CCS Nucleus. When the console is turned on, Original Menu opens straight away. The startup chooser is off."),
               "with-mod-nucleus": ({"mod": True, "nucleus": "direct", "start": "ccs-home", "auto": "menu", "countdown": 10, "at_start": True}, "ccs-home never on default 10", "This console has CCS Mod and CCS Nucleus. When the console is turned on, CCS HOME opens straight away. The startup chooser is off."),
               "with-chooser": ({"mod": False, "nucleus": "chooser", "start": "native", "auto": "menu", "countdown": 10, "at_start": True}, "native always off default 10", "This console has CCS Nucleus with its startup chooser. When the console is turned on, the startup chooser appears first. If you do nothing, Original Menu starts in 10 seconds."),
               "with-all": ({"mod": True, "nucleus": "chooser", "start": "ccs-home", "auto": "last", "countdown": 10, "at_start": True}, "ccs-home always on last 10", "This console has CCS Mod and CCS Nucleus with its startup chooser. When the console is turned on, the startup chooser appears first. If you do nothing, the menu you used last starts in 10 seconds (CCS HOME the first time).")}
        for prof, (choice, line, text) in exp.items():
            with self.subTest(profile=prof):
                a = CI.console_addons(CI.FakeConsole(self.console(prof))); self.assertEqual((a["choice"], a["start"]["line"], a["text"]), (choice, line, text))
                self.assertTrue(all(x["state"] == "current" for x in a["packages"].values()), a["packages"])
        root = self.console("with-chooser", "wait"); CI.FakeConsole(root).nucleus_start("native", "always", "off", "default", "0")
        self.assertEqual(CI.console_addons(CI.FakeConsole(root))["choice"]["auto"], "wait")
        root = self.console("with-chooser", "owner"); add_owner_image(root)
        a = CI.console_addons(CI.FakeConsole(root)); self.assertEqual((a["start"]["line"], a["choice"]["start"]), ("snes always off default 10", "snes"), "the console's own default, kept as it is")
        self.assertIn("If you do nothing, SNES starts in 10 seconds.", a["text"])

    def test_10b_the_consoles_own_start_setting_is_kept_through_another_change_and_an_update(self):
        """settings made on the console's own startup chooser (Last Used with no countdown, another countdown, not shown at start, its own system
        image first) are read as they are; a choice made from them (as the startup page starts from them) writes every part not changed back
        exactly, also when CCS Nucleus is updated in the same installation"""
        root = self.console("with-chooser", "keep"); CI.FakeConsole(root).nucleus_start("native", "always", "off", "last", "0")
        a = CI.console_addons(CI.FakeConsole(root)); have = a["choice"]
        self.assertEqual((have["auto"], have["countdown"], have["at_start"]), ("last", 0, True)); self.assertEqual(CI.start_line(have), "native always off last 0")
        self.assertIn("Nothing starts until you choose; the menu you used last is selected first (Original Menu the first time).", a["text"])
        want = dict(have, mod=True)   # another startup change: CCS Mod added, CCS HOME offered
        p = self.plan(want); self.assertEqual(p["start_line"], "native always on last 0", "only CCS HOME's offer changes")
        r = self.run_inst(root, p); self.assertTrue(r["ok"], r)
        self.assertEqual(CI.nucleus_start_state(CI.FakeConsole(root))["line"], "native always on last 0", "Last Used with no countdown kept after the change")
        # the console's CCS Nucleus from an earlier version: this installation's update keeps it too
        f = os.path.join(root, "var/lib/hakchi/rootfs/etc/ccs/nucleus/VERSION"); nuc = CI.addon_package("nucleus"); wr(f, rd(f).replace(nuc["package_id"].encode(), b"0" * 64))
        b = CI.console_addons(CI.FakeConsole(root)); self.assertEqual(b["packages"]["nucleus"]["state"], "older"); self.assertEqual(CI.start_line(b["choice"]), "native always on last 0")
        r = self.run_inst(root, self.plan(b["choice"])); self.assertTrue(r["ok"], r)
        c = CI.console_addons(CI.FakeConsole(root)); self.assertEqual(c["packages"]["nucleus"]["state"], "current"); self.assertEqual(c["start"]["line"], "native always on last 0", "kept through the update")
        # a 20-second countdown and the chooser not shown at start, then its own system image first: each read and written back as it was
        CI.FakeConsole(root).nucleus_start("native", "never", "on", "default", "20")
        d = CI.console_addons(CI.FakeConsole(root))["choice"]; self.assertEqual((d["at_start"], d["countdown"], CI.start_line(d)), (False, 20, "native never on default 20"))
        self.assertIn("The startup chooser does not show at start", CI.start_text(d))
        add_owner_image(root, default=False); CI.FakeConsole(root).nucleus_start("snes", "always", "on", "default", "10")
        e = CI.console_addons(CI.FakeConsole(root)); self.assertEqual((e["choice"]["start"], CI.start_line(e["choice"])), ("snes", "snes always on default 10"))
        self.assertIn("If you do nothing, SNES starts in 10 seconds.", e["text"])
        with self.assertRaises(OSError) as x: CI.FakeConsole(root).nucleus_start("snes", "never", "on")
        self.assertIn("refused default", str(x.exception), "its own system image opens first only with the chooser at start")
        with self.assertRaises(OSError) as x: CI.FakeConsole(root).nucleus_start("other", "always", "on")
        self.assertIn("refused default", str(x.exception), "only an option the registry lists")
        r = self.run_inst(root, self.plan(dict(e["choice"], start="other"))); self.assertEqual(r["result"], "refused"); self.assertIn("Choose what opens when the console is turned on", r["why"])
        r = self.run_inst(root, self.plan(dict(e["choice"], nucleus="chooser", at_start=False))); self.assertEqual(r["result"], "refused"); self.assertIn("opens first only from the startup chooser", r["why"])

    def test_11_what_cannot_be_read_is_refused_and_nothing_changes(self):
        nuc, cho = CI.addon_package("nucleus"), CI.addon_package("nucleus-chooser")
        def case(name, profile, setup, words):
            root = self.console(profile, name); setup(root); before = tree(root)
            with self.assertRaises(CI.Refused, msg=name) as e: CI.console_addons(CI.FakeConsole(root))
            self.assertIn(words, e.exception.why, name)
            r = self.run_inst(root, self.plan(ch(nucleus="chooser"))); self.assertEqual(r["result"], "refused", name); self.assertIn(words, r["why"]); self.assertEqual(changes(before, tree(root)), [], name)
        def foreign(root): f = os.path.join(root, "var/lib/hakchi/rootfs/etc/ccs/nucleus/VERSION"); wr(f, rd(f).replace(nuc["package_id"].encode(), b"someone-elses"))
        def lone(root):   # hakchi lists the chooser while CCS Nucleus is gone (only reachable by changing the console by hand)
            os.remove(os.path.join(root, "var/lib/hakchi/hmod/uninstall-ccs-nucleus"))
            for f in nuc["files"]: os.remove(os.path.join(root, "var/lib/hakchi/rootfs", f.lstrip("/")))
        case("program", "hakchi-ready", lambda root: wr(os.path.join(root, "var/lib/hakchi/rootfs/bin/ccs-nucleus"), b"#!/bin/sh\n"), "hakchi does not list its package")
        case("foreign", "with-nucleus", foreign, "does not recognize")
        case("changed", "with-chooser", lambda root: wr(os.path.join(root, "var/lib/hakchi/rootfs/etc/ccs/nucleus/chooser/fonts/silk17.ccsf"), b"changed"), "were changed")
        case("registry", "with-nucleus", lambda root: wr(os.path.join(root, "var/lib/ccs/nucleus/registry"), b"not a registry\n"), "CCS Nucleus's settings on this console cannot be read")
        case("lone-chooser", "with-chooser", lone, "without CCS Nucleus")
        # the console itself refuses the chooser without CCS Nucleus: nothing is copied and hakchi keeps no list for it
        root = self.console("hakchi-ready", "refuse"); before = tree(root)
        with self.assertRaises(OSError) as e: CI.FakeConsole(root).pack_install(cho["name"], cho["hmod"])
        self.assertIn("its chooser needs it", str(e.exception)); self.assertEqual(changes(before, tree(root)), [])
        with self.assertRaises(OSError) as e: CI.FakeConsole(root).nucleus_start("native", "never", "off")
        self.assertIn("not-installed", str(e.exception)); self.assertEqual(changes(before, tree(root)), [], "CCS Nucleus's start command needs CCS Nucleus")

    def test_12_every_combination_from_a_console_without_addons(self):
        for choice, added, line in ((ch(), ["ccs-mod"], None), (ch(mod=False), [], None), (ch(nucleus="direct"), ["ccs-mod", "ccs-nucleus"], "native never on default 10"),
                                    (ch(mod=False, nucleus="direct"), ["ccs-nucleus"], "native never off default 10"), (ch(nucleus="direct", start="ccs-home"), ["ccs-mod", "ccs-nucleus"], "ccs-home never on default 10"),
                                    (ch(nucleus="chooser"), ["ccs-mod", "ccs-nucleus", "ccs-nucleus-chooser"], "native always on default 10"),
                                    (ch(nucleus="chooser", start="ccs-home", auto="last"), ["ccs-mod", "ccs-nucleus", "ccs-nucleus-chooser"], "ccs-home always on last 10"),
                                    (ch(mod=False, nucleus="chooser", auto="wait"), ["ccs-nucleus", "ccs-nucleus-chooser"], "native always off default 0")):
            with self.subTest(choice=choice):
                root = self.console("hakchi-ready", "c" + secrets.token_hex(3)); stock = CI.stock_digest(root); p = self.plan(choice)
                c = CI.FakeConsole(root); r = CI.Installer(c, p, self.pkg(), self.jnew()).run(); self.assertTrue(r["ok"], r)
                self.assertEqual((r["added"], r["start"]), (added, line)); self.assertEqual(listed(root), added)
                self.assertEqual([w.split(" ", 1)[1] for w in c.writes if w.startswith("hakchi:pack_install")], added, "in install order: CCS Mod, CCS Nucleus, its chooser")
                self.assertEqual(registry_line(root), line); self.assertEqual(CI.console_addons(CI.FakeConsole(root))["choice"], choice)
                self.assertTrue(CI.verify_installed(CI.FakeConsole(root), p)["verified"]); self.assertEqual(CI.stock_digest(root), stock)
                if choice["mod"]:
                    self.assertEqual(self.pointer(root), r["copy"])
                    self.assertEqual(json.loads(rd(os.path.join(root, "var/lib/ccs/pkg", r["copy"], "session.json")))["home"]["leave"]["label"], "Choose Another Menu")
                else:   # without CCS Mod: no CCS HOME at all, only the game file its Hakchi game launches (games_only.py)
                    self.assertEqual((r["delivery"], r["hakchi"]["published"]), ("games", 1)); self.assertIsNone(self.pointer(root)); self.assertFalse([w for w in c.writes if "/var/lib/ccs/pkg" in w or "/var/lib/ccs/data" in w])
                    self.assertEqual(sorted(os.listdir(os.path.join(root, "var/lib/ccs/games", r["copy"]))), ["licenses", "roms"]); self.assertFalse(os.path.exists(os.path.join(root, "var/lib/ccs/data")))
                self.assertFalse(os.path.exists(os.path.join(root, "var/lib/hakchi/rootfs/etc/init.d/S81ccs-mod")), "CCS Mod has no start of its own")
                c2 = CI.FakeConsole(root); r2 = CI.Installer(c2, p, self.pkg(), self.jnew()).run(); self.assertEqual(r2["result"], "no-change"); self.assertEqual(c2.writes, [])
                recs = CI.read_records(CI.FakeConsole(root))
                self.assertEqual(CI.studio_installed(recs), {(CI.addon_package(a)["name"], CI.addon_package(a)["package_id"]) for a in CI.choice_addons(choice)}, "a record of each add-on it installed")

    def test_13_a_choice_that_would_remove_an_addon_is_refused_with_the_choice_that_keeps_it(self):
        for prof, choice, keep in (("with-mod", ch(mod=False), ch()), ("with-mod", ch(mod=False, nucleus="direct"), ch(nucleus="direct")),
                                   ("with-nucleus", ch(), ch(nucleus="direct")), ("with-chooser", ch(nucleus="direct"), ch(nucleus="chooser")),
                                   ("with-all", ch(), ch(nucleus="chooser", start="ccs-home", auto="last")), ("with-all", ch(mod=False, nucleus="chooser"), ch(nucleus="chooser")),
                                   ("with-mod-nucleus", ch(), ch(nucleus="direct", start="ccs-home"))):
            with self.subTest(profile=prof, choice=choice):
                root = self.console(prof, "r" + secrets.token_hex(3)); before = tree(root); now = CI.console_addons(CI.FakeConsole(root))["choice"]
                r = self.run_inst(root, self.plan(choice))
                self.assertEqual((r["result"], r["at"], r["why"]), ("refused", "addons", CI.REMOVES_TEXT)); self.assertEqual(changes(before, tree(root)), [], "nothing changed")
                with self.assertRaises(CI.Refused) as e: CI.Installer(CI.FakeConsole(root), self.plan(choice), self.pkg(), self.jnew()).preflight()
                d = e.exception.detail; self.assertEqual((d["keep_choice"], d["addons_now"]), (keep, now)); self.assertEqual(d["would_remove"], names(CI.removes(now, choice)))
                self.assertTrue(d["would_remove"]); self.assertTrue(d["addons_now_text"].startswith("This console has "))
                r = self.run_inst(root, self.plan(keep)); self.assertTrue(r["ok"], r)   # the choice that keeps them installs
                self.assertEqual(r["added"], names(a for a in CI.choice_addons(keep) if a not in CI.choice_addons(now)))

    def test_14_an_addon_or_a_start_change_alone_copies_nothing_again(self):
        root = self.console("hakchi-ready"); r = self.run_inst(root, self.plan()); self.assertTrue(r["ok"], r); copy = r["copy"]
        for choice, added, line in ((ch(nucleus="direct"), ["ccs-nucleus"], "native never on default 10"), (ch(nucleus="direct", start="ccs-home"), [], "ccs-home never on default 10"),
                                    (ch(nucleus="chooser", start="ccs-home", auto="wait"), ["ccs-nucleus-chooser"], "ccs-home always on default 0")):
            c = CI.FakeConsole(root); r = CI.Installer(c, self.plan(choice), self.pkg(), self.jnew()).run(); self.assertTrue(r["ok"], r)
            self.assertTrue(r["preflight"]["same_copy"]); self.assertEqual(r["copy"], copy); self.assertFalse([w for w in c.writes if "/pkg/" in w], "nothing copied again")
            self.assertEqual((r["added"], r["start"], registry_line(root)), (added, line, line))
            if added: self.assertIn("Adds " + CI.ADDON_LABELS[CI.choice_addons(choice)[-1]] + ".", r["preflight"]["changes_text"])
            if r["preflight"]["start"]["change"]: self.assertIn("CCS Nucleus's start setting: " + CI.start_text(choice), r["preflight"]["changes_text"])
        self.assertEqual(self.run_inst(root, self.plan(ch(nucleus="chooser", start="ccs-home", auto="wait")))["result"], "no-change")

    def test_15_ccs_nucleus_start_set_on_a_console_that_has_it(self):
        root = self.console("with-mod-nucleus"); self.assertEqual(registry_line(root), "ccs-home never on default 10")
        r = self.run_inst(root, self.plan(ch(nucleus="direct"))); self.assertTrue(r["ok"], r)
        self.assertEqual(r["preflight"]["start"], {"now": "ccs-home never on default 10", "want": "native never on default 10", "change": True, "text": "When the console is turned on, Original Menu opens straight away. The startup chooser is off."})
        self.assertEqual(registry_line(root), "native never on default 10"); self.assertEqual(r["added"], [])
        root = self.console("with-nucleus", "home-on"); r = self.run_inst(root, self.plan(ch(nucleus="direct"))); self.assertTrue(r["ok"], r)
        self.assertEqual((r["added"], registry_line(root)), (["ccs-mod"], "native never on default 10"), "CCS Mod arrives: CCS Nucleus offers CCS HOME")
        reg = CI.parse_registry(rd(os.path.join(root, "var/lib/ccs/nucleus/registry"))); self.assertEqual(reg["options"], ["native", "ccs-home"]); self.assertEqual(reg["kinds"]["ccs-home"], "ccs-home")
        root = self.console("with-chooser", "keep-owner"); add_owner_image(root, default=False)
        r = self.run_inst(root, self.plan(ch(nucleus="chooser", start="ccs-home", auto="last"))); self.assertTrue(r["ok"], r)
        reg = CI.parse_registry(rd(os.path.join(root, "var/lib/ccs/nucleus/registry"))); self.assertIn("snes", reg["options"], "the console owner's own system images stay listed")

    def sweep(self, label, profile, plan, st=None, setup=None, least=5):
        base = self.console(profile, f"{label}-base")
        if setup: setup(base)
        cnt = os.path.join(self.t, f"{label}-count"); shutil.copytree(base, cnt); c = CI.FakeConsole(cnt)
        r = CI.Installer(c, plan, self.pkg(st), self.jnew()).run(); self.assertTrue(r["ok"], r); total = c.nchanges; seen = {"rolled": 0, "installed": 0}
        for n in range(1, total + 1):
            for kind in ("before", "after"):
                with self.subTest(case=label, change=n, kind=kind):
                    root = os.path.join(self.t, f"{label}-{kind}-{n}"); shutil.copytree(base, root); before = tree(root); line = registry_line(root); j = self.jnew()
                    r = CI.Installer(CI.FakeConsole(root, {kind: n}), plan, self.pkg(st), j).run()
                    if "installed" in journal_ops(j):
                        seen["installed"] += 1
                        if "cleanup-stopped" in journal_ops(j):   # a change after the verification was not confirmed: nothing more was written, the attempt stays open
                            self.assertFalse(r["ok"], r); self.assertEqual(r["result"], "cleanup-stopped"); self.assertTrue(r["verified"]); self.assertFalse(r["record_closed"])
                            self.assertEqual(journal_ops(j)[-2:], ["cleanup-stopped", "end"])
                        else: self.assertTrue(r["ok"], r)
                        if CI.read_records(CI.FakeConsole(root))["attempts"]: self.assertEqual(CI.Installer(CI.FakeConsole(root), plan, self.pkg(st), j).resume()["result"], "installed")
                        self.assertTrue(CI.verify_installed(CI.FakeConsole(root), plan)["verified"])
                    else:
                        seen["rolled"] += 1; self.assertTrue(r["rolled_back"]["ok"], r); self.assertEqual(self.rest(before, root), [], f"{label}: change {n} {kind}")
                        if "var/lib/hakchi/hmod/uninstall-ccs-nucleus" in before: self.assertEqual(registry_line(root), line, f"{label}: change {n} {kind}: CCS Nucleus's start as it was")
                    shutil.rmtree(root, ignore_errors=True)
        self.assertGreaterEqual(seen["rolled"], least); self.assertGreater(seen["installed"], 0)

    def test_16_every_change_fails_in_turn(self):
        self.sweep("all-first", "hakchi-ready", self.plan(ch(nucleus="chooser", start="ccs-home", auto="last")))
        self.sweep("add-nucleus", "with-mod", self.plan(ch(nucleus="direct", start="ccs-home")))
        self.sweep("add-chooser", "with-mod-nucleus", self.plan(ch(nucleus="chooser", auto="wait")))
        self.sweep("start-only", "with-mod-nucleus", self.plan(ch(nucleus="direct")), setup=lambda root: self.assertTrue(self.run_inst(root, self.plan(ch(nucleus="direct", start="ccs-home")))["ok"]), least=2)
        st_b = staged(os.path.join(self.t, "staged-b"), "b")
        self.sweep("update-and-start", "with-all", CI.build_plan(CI.staged_package(st_b), ch(nucleus="chooser")), st=st_b,
                   setup=lambda root: self.assertTrue(self.run_inst(root, self.plan(ch(nucleus="chooser", start="ccs-home", auto="last")))["ok"]))

    def test_17_a_failure_after_the_start_puts_the_start_back_and_says_what_stays(self):
        p = self.plan(ch(nucleus="direct")); root = self.console("with-mod-nucleus", "fail"); before = registry_line(root)
        r = self.run_inst(root, p, after=self.changes_until(p, "with-mod-nucleus", "ccs-nucleus start"))
        self.assertTrue(r["rolled_back"]["ok"], r); self.assertEqual(registry_line(root), before, "CCS Nucleus's start put back by its own command"); self.assertIn("nucleus-start", r["rolled_back"]["undone"])
        p = self.plan(ch(nucleus="direct", start="ccs-home")); root = self.console("hakchi-ready", "kept")
        r = self.run_inst(root, p, after=self.changes_until(p, "hakchi-ready", "ccs-nucleus start")); self.assertTrue(r["rolled_back"]["ok"], r); self.assertEqual(listed(root), [])
        self.assertTrue(any("CCS Nucleus's own settings" in k for k in r["rolled_back"].get("kept", [])), r["rolled_back"])
        self.assertTrue(os.path.isfile(os.path.join(root, "var/lib/ccs/nucleus/registry")), "its own removal keeps its settings, as on a console")
        # a start that began with one of the console owner's own system images: CCS Nucleus's start command cannot set it back, and the result says so
        p = self.plan(ch(nucleus="chooser", start="ccs-home", auto="last")); root = self.console("with-all", "owner"); add_owner_image(root); self.assertTrue(registry_line(root).startswith("snes always"))
        r = self.run_inst(root, p, after=self.changes_until(p, "with-all", "ccs-nucleus start", setup=add_owner_image))
        self.assertFalse(r["rolled_back"]["ok"], r); self.assertIn("one of your own system images", " ".join(r["rolled_back"]["problems"])); self.assertIn("no further writes", r["rolled_back"]["next"])

    def test_18_a_crash_after_any_change_is_recovered_and_a_cancel_is_undone(self):
        for label, profile, plan in (("all", "hakchi-ready", self.plan(ch(nucleus="chooser", start="ccs-home"))), ("nucleus", "with-mod", self.plan(ch(nucleus="direct", start="ccs-home"))),
                                     ("start", "with-mod-nucleus", self.plan(ch(nucleus="chooser", auto="last")))):
            base = self.console(profile, f"crash-{label}"); cnt = os.path.join(self.t, f"crash-{label}-count"); shutil.copytree(base, cnt); c = CI.FakeConsole(cnt)
            self.assertTrue(CI.Installer(c, plan, self.pkg(), self.jnew()).run()["ok"]); total = c.nchanges
            for n in range(1, total + 1):
                with self.subTest(case=label, change=n):
                    root = os.path.join(self.t, f"crash-{label}-{n}"); shutil.copytree(base, root); before = tree(root); line = registry_line(root); j = self.jnew()
                    with self.assertRaises(SystemExit): CI.Installer(CI.FakeConsole(root, {"crash_after": n}), plan, self.pkg(), j).run()
                    r = CI.Installer(CI.FakeConsole(root), plan, self.pkg(), j).resume()
                    if "installed" in journal_ops(j): self.assertEqual(r["result"], "installed", r)
                    else:
                        self.assertIn(r["result"], ("rolled-back", "nothing to resume"), r); self.assertEqual(self.rest(before, root), [], f"crash after change {n}")
                        if "var/lib/hakchi/hmod/uninstall-ccs-nucleus" in before: self.assertEqual(registry_line(root), line, f"crash after change {n}: CCS Nucleus's start as it was")
                    self.assertEqual(CI.read_records(CI.FakeConsole(root))["attempts"], {}); shutil.rmtree(root, ignore_errors=True)
            root = os.path.join(self.t, f"cancel-{label}"); shutil.copytree(base, root); before = tree(root); calls = {"n": 0}
            def cancel(): calls["n"] += 1; return calls["n"] > 3
            r = CI.Installer(CI.FakeConsole(root), plan, self.pkg(), self.jnew(), cancel=cancel).run()
            self.assertEqual(r["result"], "cancelled"); self.assertTrue(r["rolled_back"]["ok"]); self.assertEqual(changes(before, tree(root)), [])

class Uninstall(Base):
    def test_19_only_the_addons_this_program_installed_chooser_before_nucleus(self):
        root = self.console("hakchi-ready"); stock = CI.stock_digest(root); self.assertTrue(self.run_inst(root, self.plan(ch(nucleus="chooser", start="ccs-home")))["ok"])
        p = CI.uninstall_plan(CI.FakeConsole(root)); self.assertTrue(p["ok"], p)
        self.assertEqual([a["name"] for a in p["actions"] if a["op"] == "addon-uninstall"], ["ccs-nucleus-chooser", "ccs-nucleus", "ccs-mod"])
        self.assertTrue(any(x.startswith("CCS Mod (ccs-mod, through hakchi's pack_uninstall") for x in p["remove"]), p["remove"])
        self.assertTrue(any("var/lib/ccs/nucleus (the add-ons' own records and settings: kept)" in k for k in p["keep"]), p["keep"])
        c = CI.FakeConsole(root); u = CI.uninstall(c, expect=p["hash"]); self.assertTrue(u["ok"], u); self.assertEqual(listed(root), [])
        self.assertEqual([w.split(" ", 1)[1] for w in c.writes if w.startswith("hakchi:pack_uninstall")], ["ccs-nucleus-chooser", "ccs-nucleus", "ccs-mod"])
        self.assertEqual(CI.read_records(CI.FakeConsole(root))["modules"], {}, "their records go with them"); self.assertEqual(CI.stock_digest(root), stock); self.assertIn("its own menu", u["after_restart"])
        self.assertIsNone(self.pointer(root)); self.assertEqual(u["addons_removed"], ["ccs-nucleus-chooser", "ccs-nucleus", "ccs-mod"])

    def test_20_addons_another_tool_added_stay(self):
        root = self.console("with-chooser"); self.assertTrue(self.run_inst(root, self.plan(ch(nucleus="chooser", start="ccs-home")))["ok"])
        self.assertEqual(listed(root), ["ccs-mod", "ccs-nucleus", "ccs-nucleus-chooser"]); self.assertEqual(registry_line(root), "ccs-home always on default 10")
        p = CI.uninstall_plan(CI.FakeConsole(root)); self.assertEqual([a["name"] for a in p["actions"] if a["op"] == "addon-uninstall"], ["ccs-mod"])
        self.assertTrue(any("CCS Nucleus (ccs-nucleus): Classic Cart Studio did not add it" in k for k in p["keep"]), p["keep"]); self.assertTrue(any("its CCS HOME choice cannot start" in k for k in p["keep"]), p["keep"])
        u = CI.uninstall(CI.FakeConsole(root), expect=p["hash"]); self.assertTrue(u["ok"], u); self.assertEqual(listed(root), ["ccs-nucleus", "ccs-nucleus-chooser"])
        self.assertIn("CCS Nucleus stays as it is", u["after_restart"]); self.assertEqual(u["addons_kept"], ["ccs-nucleus", "ccs-nucleus-chooser"])
        # CCS Nucleus this program added stays while a chooser another tool added needs it
        root = self.console("hakchi-ready", "nucleus-mine"); self.assertTrue(self.run_inst(root, self.plan(ch(nucleus="direct")))["ok"])
        cho = CI.addon_package("nucleus-chooser"); CI.FakeConsole(root).pack_install(cho["name"], cho["hmod"])
        p = CI.uninstall_plan(CI.FakeConsole(root)); self.assertEqual([a["name"] for a in p["actions"] if a["op"] == "addon-uninstall"], ["ccs-mod"])
        self.assertTrue(any("needs it" in k for k in p["keep"]), p["keep"]); u = CI.uninstall(CI.FakeConsole(root), expect=p["hash"]); self.assertTrue(u["ok"], u)
        self.assertEqual(listed(root), ["ccs-nucleus", "ccs-nucleus-chooser"]); self.assertEqual(CI.console_addons(CI.FakeConsole(root))["packages"]["nucleus-chooser"]["state"], "current")

    def test_21_an_interrupted_uninstall_is_finished_by_running_it_again(self):
        base = self.console("hakchi-ready", "base"); self.assertTrue(self.run_inst(base, self.plan(ch(nucleus="chooser")))["ok"])
        cnt = os.path.join(self.t, "count"); shutil.copytree(base, cnt); c = CI.FakeConsole(cnt); self.assertTrue(CI.uninstall(c)["ok"]); total = c.nchanges
        for n in range(1, total + 1):
            for kind in ("before", "after"):
                with self.subTest(change=n, kind=kind):
                    root = os.path.join(self.t, f"u-{kind}-{n}"); shutil.copytree(base, root)
                    try: CI.uninstall(CI.FakeConsole(root, {kind: n}))
                    except (ConnectionError, OSError): pass
                    r = CI.uninstall(CI.FakeConsole(root)); self.assertTrue(r["ok"], r); self.assertEqual(listed(root), [])
                    shutil.rmtree(root, ignore_errors=True)

def ask(w: Worker, cmd: str, args: dict | None = None) -> tuple[dict, list]:
    out = io.StringIO(); w.out = out; w.handle(json.dumps({"v": 1, "id": "t", "cmd": cmd, "args": args or {}}))
    lines = [json.loads(l) for l in out.getvalue().splitlines() if l.strip()]; return lines[-1], lines[:-1]

class SuperNes(Base):
    """the Super NES Classic Edition (its own software's board and region): what Classic Cart Studio may change follows the shared platform
    registry's capability for its add-ons and the shared storage specification's room for its saves (both as this version ships them:
    refused before any change, with the reason); with both supplied in this test only, CCS HOME installs, is verified and is removed again,
    its 21 built-in games and their saves never touched, and the collection's games are never added as Hakchi games (its own software has no
    NES emulator). A Super Famicom Classic Mini, or a console whose software and hakchi disagree, is never one."""
    def supplied(self):
        import platform_ids as P
        from console_engine import storage_model as SM
        was, pol = P.allowed, SM.SPEC["policies"]["saves_allowance_bytes"]; had = pol.get("snes-classic-edition")
        P.allowed = lambda k, i: True if (k, i) == ("capability", "snes-classic-addons") else was(k, i)
        pol["snes-classic-edition"] = pol["nes-classic-edition"]   # this test only: the NES Classic's allowance
        def undo(): P.allowed = was; pol["snes-classic-edition"] = had
        self.addCleanup(undo)
    def test_25_as_this_version_ships_the_change_is_refused_before_anything(self):
        import platform_ids as P
        from console_engine import storage_model as SM
        cap, room = P.allowed("capability", "snes-classic-addons"), SM.SPEC["policies"]["saves_allowance_bytes"].get("snes-classic-edition") is not None
        for prof in ("snes-usa", "snes-eur"):
            root = self.console(prof); before = tree(root); r = self.run_inst(root, self.plan())
            if cap and room: self.assertEqual(r["result"], "installed", r); continue
            self.assertEqual((r["ok"], r["result"]), (False, "refused"), r)
            self.assertIn("Super NES Classic Edition is not available in this version" if not cap else "room needed on the console cannot be confirmed", r["why"])
            self.assertEqual(changes(before, tree(root)), [], prof)
            ins = CI.inspect_console(CI.FakeConsole(root)); self.assertTrue(ins["ok"], ins); self.assertEqual((ins["device"]["model"], ins["device"]["system"]), ("snes-classic-edition", prof))
    def test_26_with_its_capability_and_room_supplied_ccs_home_installs_and_is_removed(self):
        self.supplied()
        for prof in ("snes-usa", "snes-eur"):
            root = self.console(prof); before = tree(root); stock = CI.stock_digest(root)
            r = self.run_inst(root, self.plan({"mod": True, "nucleus": "direct", "start": "ccs-home"})); self.assertEqual(r["result"], "installed", r)
            self.assertEqual(listed(root), ["ccs-mod", "ccs-nucleus"]); self.assertTrue(CI.verify_installed(CI.FakeConsole(root), self.plan({"mod": True, "nucleus": "direct", "start": "ccs-home"}))["verified"])
            self.assertEqual(CI.stock_digest(root), stock, "the built-in games, their saves and suspend points as they were")
            self.assertFalse(any(k.startswith(("usr/", "var/lib/clover/", "var/squashfs/")) for k in changes(before, tree(root))), "nothing of Nintendo's own")
            hk = CI.build_plan(self.pkg(), {"mod": True, "nucleus": "direct", "start": "ccs-home"}, True); x = self.run_inst(root, hk); self.assertEqual(x["result"], "refused", x); self.assertIn("has no NES emulator", x["why"])
            rv = CI.uninstall_plan(CI.FakeConsole(root)); self.assertTrue(rv["ok"], rv); u = CI.uninstall(CI.FakeConsole(root), expect=rv["hash"]); self.assertTrue(u["ok"], u)
            self.assertEqual([k for k in self.rest(before, root) if not k.startswith("var/lib/ccs/data")], [], prof)
            self.assertEqual(CI.stock_digest(root), stock)
    def test_27_other_models_and_disagreeing_identities_are_never_one(self):
        self.supplied()
        for prof, words in (("sfc-jpn", "is not one Classic Cart Studio supports"), ("model-mismatch", "but hakchi reports snes-usa"), ("no-model", "model could not be read"), ("data-elsewhere", "data storage is not where")):
            root = self.console(prof); before = tree(root); r = self.run_inst(root, self.plan())
            self.assertEqual(r["result"], "refused", (prof, r)); self.assertIn(words, r["why"]); self.assertEqual(changes(before, tree(root)), [], prof)

class EuropeanNes(Base):
    """an NES Classic whose software names REGION EUR: hakchi's system nes-eur (its softwareCheck), its game storage games/nes-eur (its
    findGameStorage). The same model as a North American one: the same checks, the same add-ons, its 30 built-in games and their saves never
    touched; the collection's games go into its own game storage as Hakchi games. Software and hakchi that disagree on the region is never one."""
    def test_28_ccs_home_and_the_hakchi_games_install_and_are_removed(self):
        root = self.console("nes-eur"); before = tree(root); stock = CI.stock_digest(root); store = CI.storage_digest(root)
        ins = CI.inspect_console(CI.FakeConsole(root)); self.assertTrue(ins["ok"], ins); self.assertEqual((ins["device"]["model"], ins["device"]["system"]), ("nes-classic-edition", "nes-eur"))
        p = self.plan({"mod": True, "nucleus": "direct", "start": "ccs-home"}); r = self.run_inst(root, p); self.assertEqual(r["result"], "installed", r)
        self.assertEqual(listed(root), ["ccs-mod", "ccs-nucleus"]); self.assertTrue(CI.verify_installed(CI.FakeConsole(root), p)["verified"])
        self.assertEqual(CI.stock_digest(root), stock, "the built-in games and their saves as they were")
        hk = CI.build_plan(self.pkg(), {"mod": True, "nucleus": "direct", "start": "ccs-home"}, True); x = self.run_inst(root, hk); self.assertEqual(x["result"], "installed", x)
        made = [k for k in changes(before, tree(root)) if "/hakchi/games/" in "/" + k]
        self.assertTrue(made and all(k.startswith("var/lib/hakchi/games/nes-eur/") for k in made), made)
        self.assertEqual(CI.storage_digest(root), store, "another tool's games in its storage unchanged")
        self.assertFalse(os.path.exists(os.path.join(root, "var/lib/hakchi/games/nes-usa")), "nothing in another region's storage")
        rv = CI.uninstall_plan(CI.FakeConsole(root)); self.assertTrue(rv["ok"], rv); u = CI.uninstall(CI.FakeConsole(root), expect=rv["hash"]); self.assertTrue(u["ok"], u)
        # nothing of Classic Cart Studio's own is left but the console's saves folder (kept by default): not even the console programs' empty
        # journal folder
        self.assertEqual([k for k in self.rest(before, root) if not k.startswith("var/lib/ccs/data")], [])
        self.assertFalse(os.path.exists(os.path.join(root, "var/lib/ccs/owned")))
        self.assertEqual(CI.stock_digest(root), stock); self.assertEqual(CI.storage_digest(root), store)
    def test_29_software_and_hakchi_disagreeing_on_the_region_is_refused(self):
        root = self.console("region-mismatch"); before = tree(root); r = self.run_inst(root, self.plan())
        self.assertEqual(r["result"], "refused", r); self.assertIn("but hakchi reports nes-usa", r["why"]); self.assertEqual(changes(before, tree(root)), [])

class RemovalLeavesNothing(Base):
    """a removal leaves nothing of Classic Cart Studio's own behind: the console programs' journal folder (/var/lib/ccs/owned) goes once their
    own removal has emptied it, and stays while it holds the record of an add-on that stays (another tool's)"""
    def remove_all(self, root, keep_data=True):
        rv = CI.uninstall_plan(CI.FakeConsole(root), keep_data); self.assertTrue(rv["ok"], rv)
        u = CI.uninstall(CI.FakeConsole(root), keep_data, expect=rv["hash"]); self.assertTrue(u["ok"], u); return rv
    def test_31_everything_this_program_added_goes_and_the_empty_journal_folder_with_it(self):
        for keep in (True, False):
            with self.subTest(keep_data=keep):
                root = self.console("hakchi-ready", f"all-{keep}"); before = tree(root)
                p = self.plan({"mod": True, "nucleus": "chooser", "start": "ccs-home", "auto": "menu"}); self.assertEqual(self.run_inst(root, p)["result"], "installed")
                hk = CI.build_plan(self.pkg(), {"mod": True, "nucleus": "chooser", "start": "ccs-home", "auto": "menu"}, True); self.assertEqual(self.run_inst(root, hk)["result"], "installed")
                self.assertTrue(os.listdir(os.path.join(root, "var/lib/ccs/owned")), "the console programs keep their journals there while installed")
                rv = self.remove_all(root, keep)
                self.assertIn({"op": "rmdir-if-empty", "path": CI.OWNED_DIR}, rv["actions"]); self.assertTrue(any(k.startswith(CI.OWNED_DIR + " (") for k in rv["keep"]), rv["keep"])
                self.assertFalse(os.path.exists(os.path.join(root, "var/lib/ccs/owned")))
                left = [k for k in self.rest(before, root) if not (keep and k.startswith("var/lib/ccs/data"))]
                self.assertEqual(left, [], "nothing of this program's own is left")
    def test_32_the_journal_folder_stays_while_it_holds_another_programs_journal(self):
        root = self.console("hakchi-ready", "other-journal"); own = os.path.join(root, "var/lib/ccs/owned"); os.makedirs(own)
        other = os.path.join(own, "ccs-mod.own")
        with open(other, "w", encoding="utf-8", newline="\n") as f: f.write("schema=ccs-ownership/1\nanother tool's journal\n")
        opts = {"mod": True, "nucleus": "direct", "start": "ccs-home"}
        self.assertEqual(self.run_inst(root, self.plan(opts))["result"], "installed")
        self.assertEqual(self.run_inst(root, CI.build_plan(self.pkg(), opts, True))["result"], "installed")
        self.assertIn("published.own", os.listdir(own))
        self.remove_all(root)
        self.assertEqual(os.listdir(own), ["ccs-mod.own"], "this program's console program's journal goes; another program's stays, with its folder")
        with open(other, encoding="utf-8") as f: self.assertEqual(f.read(), "schema=ccs-ownership/1\nanother tool's journal\n")

class WorkerAddons(Base):
    def setUp(self):
        super().setUp(); self.w = Worker(os.path.join(self.t, "root"), console_package=self.st)
        r, _ = ask(self.w, "home-plan"); self.assertEqual(r["event"], "result", r); self.p = r["result"]
    def pre(self, profile, h=None): r, _ = ask(self.w, "home-preflight", {"plan_hash": h or self.p["hash"], "profile": profile}); return r["result"]
    def planh(self, **choice): r, _ = ask(self.w, "home-plan", choice); self.assertEqual(r["event"], "result", r); return r["result"]
    def sim(self, h, profile, **a):
        r, ev = ask(self.w, "home-simulate", {"plan_hash": h, "profile": profile, **a}); self.assertEqual(r["event"], "result", r); return r["result"], ev
    def test_30_the_plan_takes_the_choice(self):
        self.assertEqual((self.p["addons"], self.p["start_line"], list(self.p["packages"])), (CI.STUDIO_CHOICE, None, ["mod"])); self.assertNotIn("files", self.p["packages"]["mod"])
        x = self.planh(nucleus="chooser", start="ccs-home", auto="last"); self.assertEqual((x["start_line"], list(x["packages"])), ("ccs-home always on last 10", ["mod", "nucleus", "nucleus-chooser"]))
        self.assertEqual(x["start_text"], CI.start_text(ch(nucleus="chooser", start="ccs-home", auto="last"))); self.assertNotEqual(x["hash"], self.p["hash"])
        for bad in ({"folder": "C:/elsewhere"}, {"mod_start": "on"}, {"method": "nucleus"}):
            r, _ = ask(self.w, "home-plan", bad); self.assertEqual(r["event"], "error", bad)
        r, _ = ask(self.w, "home-plan", {"nucleus": "maybe"}); self.assertEqual((r["code"], r["at"]), ("refused", "addons")); self.assertIn("CCS Nucleus", r["text"])
        r, _ = ask(self.w, "home-plan", {"mod": False, "nucleus": "direct", "start": "ccs-home"}); self.assertEqual((r["code"], r["text"]), ("refused", CI.HOME_NEEDS_MOD))
        rec, _ = ask(self.w, "home-records"); self.assertTrue(rec["result"]["last_plan_current"]); self.assertEqual(rec["result"]["last_plan"]["plan"]["hash"], x["hash"])

    def test_31_the_check_answers_with_the_consoles_addons_and_what_the_plan_changes(self):
        pf = self.pre("hakchi-ready"); self.assertTrue(pf["ok"], pf); self.assertEqual((pf["add"], pf["start"], pf["plan_hash"]), (["ccs-mod"], None, self.p["hash"]))
        self.assertEqual(pf["addons_now_text"], "This console has no CCS add-ons. When the console is turned on, Original Menu opens."); self.assertEqual(pf["changes_text"], "Adds CCS Mod.")
        pf = self.pre("with-mod"); self.assertTrue(pf["ok"]); self.assertEqual(pf["add"], []); self.assertEqual(pf["changes_text"], "The add-ons and what opens at power-on stay as they are.")
        pf = self.pre("with-nucleus"); self.assertFalse(pf["ok"]); self.assertEqual((pf["at"], pf["why"]), ("addons", CI.REMOVES_TEXT))
        self.assertEqual(pf["addons_now"], {"mod": False, "nucleus": "direct", "start": "native", "auto": "menu", "countdown": 10, "at_start": True}); self.assertEqual(pf["keep_choice"], ch(nucleus="direct")); self.assertEqual(pf["would_remove"], ["ccs-nucleus"])
        k = self.planh(**pf["keep_choice"]); pf = self.pre("with-nucleus", k["hash"]); self.assertTrue(pf["ok"], pf)
        self.assertEqual((pf["add"], pf["start"]["now"], pf["start"]["want"], pf["start"]["change"]), (["ccs-mod"], "native never off default 10", "native never on default 10", True))
        self.assertEqual(pf["changes_text"], "Adds CCS Mod. Updates CCS Nucleus's start setting: When the console is turned on, Original Menu opens straight away. The startup chooser is off.")
        pf = self.pre("with-all"); self.assertEqual(pf["keep_choice"], ch(nucleus="chooser", start="ccs-home", auto="last"))
        pf = self.pre("with-all", self.planh(**pf["keep_choice"])["hash"]); self.assertTrue(pf["ok"], pf); self.assertEqual((pf["add"], pf["start"]["change"]), ([], False))
        pf = self.pre("stock"); self.assertFalse(pf["ok"]); self.assertIn("hakchi must be installed first", pf["why"])
        pf = self.pre("nearly-full"); self.assertFalse(pf["ok"]); self.assertIn("not enough room", pf["why"])

    def test_32_every_rehearsal_on_a_console_with_ccs_nucleus(self):
        h = self.planh(nucleus="direct")["hash"]
        x, ev = self.sim(h, "with-nucleus", scenario="first-install"); self.assertEqual(x["install"]["result"], "installed", x); self.assertTrue(x["stock_unchanged"])
        self.assertEqual((x["install"]["added"], x["install"]["start"], x["start"]), (["ccs-mod"], "native never on default 10", "native never on default 10"))
        self.assertTrue(any(e.get("stage") == "readback" for e in ev)); self.assertEqual([e.get("name") for e in ev if e.get("stage") == "module"], ["ccs-mod"]); self.assertTrue(any(e.get("stage") == "start" for e in ev))
        self.assertEqual(self.sim(h, "with-nucleus", scenario="repeat")[0]["install"]["result"], "no-change")
        x, _ = self.sim(h, "with-nucleus", scenario="update"); self.assertEqual(x["install"]["result"], "installed")
        ear = next(t["earlier"] for t in x["install"]["tidy"] if "earlier" in t); self.assertEqual((ear["remove"], len(ear["kept"])), ([], 2), "one earlier version by default: the copy in use before the update stays")
        x, _ = self.sim(h, "with-nucleus", scenario="repair"); self.assertEqual(x["install"]["result"], "installed")
        x, _ = self.sim(h, "with-nucleus", scenario="interrupted"); self.assertTrue(x["install"]["rolled_back"]["ok"]); self.assertEqual(x["after_recovery"], "installed")
        x, _ = self.sim(h, "with-nucleus", scenario="first-install", then=["uninstall", "export"]); self.assertTrue(x["uninstall"]["ok"]); self.assertTrue(x["export"]["verified"]); self.assertTrue(x["stock_unchanged_after_uninstall"])
        self.assertEqual((x["uninstall"]["addons_removed"], x["uninstall"]["addons_kept"]), (["ccs-mod"], ["ccs-nucleus"]), "the CCS Nucleus another tool added stays")
        x, _ = self.sim(self.planh(nucleus="chooser", start="ccs-home", auto="wait")["hash"], "hakchi-ready")
        self.assertEqual((x["install"]["result"], x["install"]["added"]), ("installed", ["ccs-mod", "ccs-nucleus", "ccs-nucleus-chooser"]))

    def test_33_a_removal_in_a_rehearsal_and_a_restaged_collection(self):
        x, _ = self.sim(self.p["hash"], "with-chooser"); self.assertEqual((x["install"]["result"], x["install"]["why"]), ("refused", CI.REMOVES_TEXT)); self.assertTrue(x["stock_unchanged"])
        h = self.planh(nucleus="direct")["hash"]; shutil.rmtree(self.st); staged(self.st, "changed")
        for plan in (h, self.p["hash"]):
            r, _ = ask(self.w, "home-preflight", {"plan_hash": plan, "profile": "with-nucleus"}); self.assertEqual(r.get("code"), "plan-obsolete", r)

    def test_34_a_cancel_reaches_a_rehearsal_in_its_own_process(self):
        big = staged(os.path.join(self.t, "big"), "big", extra={f"home/covers/c{i:04d}.png": (os.urandom(4000), "home", "0o644") for i in range(500)})
        root = os.path.join(self.t, "root2"); env = dict(os.environ, PYTHONPATH=HERE + os.pathsep + os.path.join(HERE, "..", "rom-pipeline") + os.pathsep + os.environ.get("PYTHONPATH", ""), **chooser_env())
        p = subprocess.Popen([sys.executable, "-B", "-m", "console_engine.worker", "--root", root, "--console-package", big], cwd=HERE, env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, encoding="utf-8")
        try:
            def send(o): p.stdin.write(json.dumps({"v": 1, **o}) + "\n"); p.stdin.flush()
            def line(): return json.loads(p.stdout.readline())
            send({"id": "p", "cmd": "home-plan", "args": {"nucleus": "chooser"}}); h = line()["result"]["hash"]
            send({"id": "s", "cmd": "home-simulate", "args": {"plan_hash": h, "profile": "with-chooser"}})
            while True:
                e = line()
                if e.get("stage") == "stage" and e.get("done", 0) >= 5: break
            send({"id": "c", "cmd": "cancel-if-safe", "args": {}}); res = None; acc = None
            while res is None:
                e = line()
                if e.get("id") == "c": acc = e["result"]
                elif e.get("id") == "s" and e.get("event") in ("result", "error"): res = e
            self.assertTrue(acc and acc["accepted"], acc); i = res["result"]["install"]
            self.assertEqual(i["result"], "cancelled", i); self.assertTrue(i["rolled_back"]["ok"]); self.assertEqual(res["result"]["addons"], ch(nucleus="chooser"))
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
        self.runs = console_runs; self.root = self.console("with-nucleus", "console"); self.wroot = os.path.join(self.t, "worker"); self.faults = {}; self.clock = Clock()
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
    def dplan(self, **choice):
        code, r = self.d.post("plan", choice); self.assertEqual(code, 200, r); return r["result"]

    def test_40_the_choice_passes_through_and_the_confirmation_names_the_reviewed_plan(self):
        d = self.dplan(folder="C:/x", mod_start="on"); self.assertEqual(d["addons"], CI.STUDIO_CHOICE, "only the add-on choice reaches the worker")
        for bad in ({"mod": "yes"}, {"nucleus": 3}, {"start": "ccs home; rm"}):
            code, r = self.d.post("plan", bad); self.assertEqual((code, r["code"]), (400, "bad-choice"), bad)
        code, r = self.d.post("plan", {"nucleus": "maybe"}); self.assertEqual((code, r["code"]), (409, "refused")); self.assertIn("CCS Nucleus", r["error"])
        ins = self.run_op("inspect"); self.assertEqual(ins["addons"], {"mod": False, "nucleus": "direct", "start": "native", "auto": "menu", "countdown": 10, "at_start": True}); self.assertEqual(ins["start"], "native never off default 10")
        st = self.d.public_console()["state"]; self.assertEqual(st["addons"]["nucleus"], "direct"); self.assertIn("This console has CCS Nucleus.", st["addons_text"])
        chk = self.run_op("check", plan_hash=d["hash"]); self.assertFalse(chk["ok"]); self.assertEqual(chk["why"], CI.REMOVES_TEXT); k = chk["keep_choice"]
        p = self.dplan(**k); chk = self.run_op("check", plan_hash=p["hash"]); self.assertTrue(chk["ok"], chk); self.assertEqual((chk["plan_hash"], chk["add"]), (p["hash"], ["ccs-mod"]))
        res = self.run_op("install", plan_hash=p["hash"], confirmation=self.confirm("install", plan_hash=p["hash"])); self.assertEqual(res["result"], "installed", res)
        self.assertEqual((res["added"], res["start"]), (["ccs-mod"], "native never on default 10")); v = self.run_op("verify", plan_hash=p["hash"]); self.assertTrue(v["verified"], v)
        self.assertEqual(listed(self.root), ["ccs-mod", "ccs-nucleus"])

    def test_41_another_tool_added_an_addon_after_the_check(self):
        p = self.dplan(nucleus="direct"); self.run_op("inspect"); self.assertTrue(self.run_op("check", plan_hash=p["hash"])["ok"]); tok = self.confirm("install", plan_hash=p["hash"])
        cho = CI.addon_package("nucleus-chooser"); CI.FakeConsole(self.root).pack_install(cho["name"], cho["hmod"]); before = tree(self.root)
        res = self.run_op("install", plan_hash=p["hash"], confirmation=tok); self.assertEqual((res["result"], res["why"]), ("refused", CI.REMOVES_TEXT)); self.assertEqual(changes(before, tree(self.root)), [])

    def test_42_a_lost_connection_stops_and_its_recovery_is_its_own_confirmed_operation(self):
        p = self.dplan(nucleus="direct"); before = tree(self.root); line = registry_line(self.root); self.run_op("inspect"); self.run_op("check", plan_hash=p["hash"])
        self.faults = {"after": 9}; res = self.run_op("install", plan_hash=p["hash"], confirmation=self.confirm("install", plan_hash=p["hash"])); self.assertEqual(res["result"], "stopped", res); self.assertIsNone(res["rolled_back"])
        self.faults = {}; self.run_op("inspect"); rr = self.run_op("recover-review"); a = rr["attempts"][0]; self.assertTrue(a["recoverable"], a)
        rec = self.run_op("recover", attempt=a["attempt"], confirmation=self.confirm("recover", attempt=a["attempt"])); self.assertEqual(rec["result"], "rolled-back", rec)
        self.assertEqual(self.rest(before, self.root), []); self.assertEqual(registry_line(self.root), line)

    def test_43_the_worker_ends_during_the_installation_and_a_new_app_session_recovers_it(self):
        import console_home, console_confirm as CC
        from test_console_dispatch import InProc
        p = self.dplan(nucleus="direct"); before = tree(self.root); self.run_op("inspect"); self.run_op("check", plan_hash=p["hash"])
        self.faults = {"crash_after": 9}   # the worker process ends right after a change (as when the app is closed or the PC stops)
        code, r = self.d.post("install", {"plan_hash": p["hash"], "confirmation": self.confirm("install", plan_hash=p["hash"])}); self.assertEqual(code, 200, r); q = self.wait(r["run"])
        self.assertEqual((q["result"]["event"], q["result"].get("code")), ("error", "worker-ended"), q["result"]); self.assertTrue(CI.read_records(CI.FakeConsole(self.root))["attempts"], "the console keeps the attempt's record")
        self.faults = {}
        def factory(tier, pkg): return CI.FakeConsole(self.root, {})
        self.d = console_home.Dispatcher(allowed=lambda t: True, confirmations=CC.Confirmations(allowed=lambda t: True, clock=self.clock), clock=self.clock,
                                         spawn=lambda: InProc(root=self.wroot, console_package=self.st, console_factory=factory, allowed=lambda t: True), admit=lambda h, o, s: {"ok": True, "ticket": "0" * 32})
        self.run_op("inspect"); rr = self.run_op("recover-review"); a = rr["attempts"][0]; self.assertTrue(a["recoverable"], a)   # a new app session: the same records on this PC
        rec = self.run_op("recover", attempt=a["attempt"], confirmation=self.confirm("recover", attempt=a["attempt"])); self.assertEqual(rec["result"], "rolled-back", rec)
        self.assertEqual(self.rest(before, self.root), []); self.assertEqual(listed(self.root), ["ccs-nucleus"])

PAGE = r"""
const vm=require('vm'), fs=require('fs');
const [JS, B, TOKEN]=process.argv.slice(1);
const els={}; const mk=id=>({id, innerHTML:'', textContent:'', disabled:false, checked:false, value:'', onclick:null, onchange:null, classList:{add(){}, remove(){}, contains(){return false;}}});
const $=id=>{ const h=Object.values(els).map(e=>e.innerHTML).join(''); if(!els[id]&&!['hkBody','hkTitle','hkSub','hkpanel','nhPkg'].includes(id)&&!h.includes(`id="${id}"`)) return null; return els[id]||(els[id]=mk(id)); };
const H={'Content-Type':'application/json','X-CCS-Protocol':'1',Origin:'https://classiccartstudio.com',Authorization:'Bearer '+TOKEN};
async function hkPost(p,b){ const r=await fetch(B+p,{method:'POST',headers:H,body:JSON.stringify(b||{})}); const j=await r.json(); return Object.assign({status:r.status}, j); }
async function capi(p){ const r=await fetch(B+p,{headers:H}); return r.json(); }
let tvOpened=0;
const ctx=vm.createContext({console, setTimeout, clearTimeout, setInterval, clearInterval, fetch, $, hkPost, capi, document:{addEventListener(){}, querySelectorAll(){return [];}},
  esc:s=>String(s==null?'':s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;'), fmt:n=>n+' B', COMPANION:{ok:true, desktop:true, capabilities:['console-sim']},
  INST:{step:'review'}, STEPS:[], stepperHtml:()=>'', renderInstall:async()=>{}, procSet(){}, PROC:{}, HOME_STARTUP:[], HOME_THEME_META:[], homeModelFromStudio:()=>({theme:'arcade-grid'}), dispatchEvent(){}, CustomEvent:function(){},
  openTv:()=>{ tvOpened++; } });
ctx.window=ctx; vm.runInContext(fs.readFileSync(JS,'utf8'), ctx);
const R=(code)=>vm.runInContext(code, ctx);
const wait=async(f,ms)=>{ for(let i=0;i<(ms||60000)/100;i++){ if(await f()) return true; await new Promise(r=>setTimeout(r,100)); } return false; };
const html=()=>els.cPlan?els.cPlan.innerHTML:'';
const has=t=>html().includes(t);
(async()=>{ const out={};
  const open=async(profile)=>{ R(`addonReset(); CONSOLE.profile=${JSON.stringify(profile)}; INST.step='review';`); await R('renderConsoleStep()'); await wait(()=>has('Choose your add-ons')); };
  // a console with hakchi only: CCS Mod suggested from Classic Cart Studio, CCS Nucleus off
  await open('hakchi-ready');
  out.page1={title:has('Choose your add-ons'), mod:has('id="cMod" checked'), locked:has('id="cMod" checked disabled'), author:has('href="https://Rance9.com" target="_blank" rel="noopener">Rance9</a>'), next:has('Next: Startup')};
  await els.cSeeHome.onclick(); out.see_home=tvOpened;
  await els.cNextStart.onclick(); await wait(()=>has('What should open when you turn on the console?'));
  out.page2={nucleus:has('id="cNuc" checked'), keep:has('Keep the console') && has('current startup.'), review:has('Review Changes'), back:has('Back: Add-ons')};
  els.cNuc.checked=true; await els.cNuc.onchange(); await wait(()=>has('Open a menu straight away'));
  out.on={addons:R('JSON.stringify(CONSOLE.addons)'), immediate:has('This menu opens immediately. The startup chooser is off.'), home_listed:has('<option value="ccs-home" selected>CCS HOME</option>')};
  R("CONSOLE.addons.nucleus='chooser'"); els.cFirst.value='ccs-home'; await els.cFirst.onchange(); await wait(()=>has('If you do nothing, CCS HOME starts in 10 seconds.'));
  els.cAuto.value='wait'; await els.cAuto.onchange(); await wait(()=>has('Nothing starts until you choose.'));
  out.chooser={auto:R('CONSOLE.addons.auto'), countdown:has('Nothing starts until you choose.')};
  // CCS Mod cleared: CCS HOME is never what opens first without it, and the page says so
  R("CONSOLE.addons.nucleus='direct'; CONSOLE.addons.auto='menu'"); await els.cBackAddons.onclick(); await wait(()=>has('Choose your add-ons'));
  els.cMod.checked=false; await els.cMod.onchange(); await els.cNextStart.onclick(); await wait(()=>has('What should open when you turn on the console?'));
  out.no_mod={start:R('CONSOLE.addons.start'), said:has('CCS HOME is not chosen, so Original Menu opens first instead.'), home_listed:has('value="ccs-home"')};
  await els.cBackAddons.onclick(); await wait(()=>has('Choose your add-ons')); els.cMod.checked=true; await els.cMod.onchange(); await els.cNextStart.onclick(); await wait(()=>has('What should open when you turn on the console?'));
  out.mod_again={start:R('CONSOLE.addons.start')};
  await els.cReview.onclick(); await wait(()=>R("!!(CONSOLE.plan&&CONSOLE.preflight&&CONSOLE.preflight._p)")&&has('Your add-ons')); await R('new Promise(r=>setTimeout(r,200))');
  out.review={home:has('<dt>CCS HOME</dt><dd>Add with CCS Mod</dd>'), menu:has('<dt>Original Menu</dt><dd>Keep, with the CCS HOME tile</dd>'), startup:has('<dt>Startup</dt><dd>CCS Nucleus — open CCS HOME immediately</dd>'),
    chooser:has('<dt>Startup chooser</dt><dd>Off</dd>'), ready:has('ready'), plan:R('JSON.stringify(CONSOLE.plan.addons)'), change:has('id="cChangeAddons"'), hakchi_off:has('id="cHakchi" >'), delivery:R('CONSOLE.plan.delivery')};
  R("INST.step='install'"); await R('renderConsoleStep()'); R("CONSOLE.scenario='first-install'");
  await els.hkSim.onclick(); await wait(()=>R("INST.step==='verify'"), 120000);
  const x=R("CONSOLE.result&&CONSOLE.result.result"); out.rehearse={installed:!!(x&&x.install&&x.install.result==='installed'), added:x&&x.install?x.install.added:null};
  R("INST.step='ready'"); await R('renderConsoleStep()'); out.ready={tile:els.hkBody.innerHTML.includes('CCS HOME is a tile in Original Menu')};
  // a console with CCS Nucleus: the pages start from it and keep it (nothing is removed from here)
  await open('with-nucleus'); await els.cNextStart.onclick(); await wait(()=>has('What should open when you turn on the console?'));
  out.with_nucleus={locked:has('id="cNuc" checked disabled'), stays:has('CCS Nucleus is installed on this console and stays'), addons:R('JSON.stringify(CONSOLE.addons)')};
  await els.cReview.onclick(); await wait(()=>R("!!(CONSOLE.plan&&CONSOLE.preflight&&CONSOLE.preflight._p)")&&has('Your add-ons')); await R('new Promise(r=>setTimeout(r,200))');
  out.with_nucleus.ready=R("!!(CONSOLE.preflight&&CONSOLE.preflight.ok)"); out.with_nucleus.kept=has('<dt>CCS HOME</dt><dd>Add with CCS Mod</dd>');
  // without CCS Mod: the collection's games as Hakchi games and nothing of CCS HOME; the choice is on the review, with what goes where
  await open('hakchi-ready'); els.cMod.checked=false; await els.cMod.onchange(); await els.cNextStart.onclick(); await wait(()=>has('What should open when you turn on the console?'));
  await els.cReview.onclick(); await wait(()=>R("!!(CONSOLE.plan&&CONSOLE.preflight&&CONSOLE.preflight._p)")&&has('Your add-ons')); await R('new Promise(r=>setTimeout(r,200))');
  out.games={delivery:R('CONSOLE.plan.delivery'), box:has('id="cHakchi" checked'), text:has('CCS HOME is not installed.'), where:has('go into a folder of Classic Cart Studio'), added:has('This collection’s game files for its Hakchi games'),
    home:has('<dt>CCS HOME</dt><dd>Not selected</dd>'), left:has('Not added to Original Menu (1)'), why:has('Mapper Thirty Demo</span>: Nintendo'), sum:has('<span>Hakchi games</span>'), ready:R("!!(CONSOLE.preflight&&CONSOLE.preflight.ok)"), sent:R('JSON.stringify(planBody())')};
  els.cHakchi.checked=false; await els.cHakchi.onchange(); await wait(()=>has('No plan.'));
  out.neither={said:has('Nothing from this collection would go to the console'), box:has('id="cHakchi" >'), change:has('id="cChangeAddons"'), sent:R('JSON.stringify(planBody())')};
  els.cHakchi.checked=true; await els.cHakchi.onchange(); await wait(()=>R("!!(CONSOLE.plan&&CONSOLE.preflight&&CONSOLE.preflight._p)")&&has('Your add-ons'));
  R("INST.step='install'"); await R('renderConsoleStep()'); R("CONSOLE.scenario='first-install'");
  await els.hkSim.onclick(); await wait(()=>R("INST.step==='verify'"), 120000);
  const y=R("CONSOLE.result&&CONSOLE.result.result"); out.games_rehearse={result:y&&y.install?y.install.result:null, delivery:y&&y.install?y.install.delivery:null, published:y&&y.install&&y.install.hakchi?y.install.hakchi.published:null, added:y&&y.install?y.install.added:null};
  R("INST.step='verify'"); await R('renderConsoleStep()'); out.games_verify={files:els.hkBody.innerHTML.includes('Game files on the console'), menu:els.hkBody.innerHTML.includes('<dt>Original Menu</dt>')};
  R("INST.step='ready'"); await R('renderConsoleStep()'); out.games_ready={said:els.hkBody.innerHTML.includes('as Hakchi games beside its own games and saves. CCS HOME is not installed.')};
  // the console's own start setting (set on its startup chooser) shown as it is and sent back unchanged with another change
  R("CONSOLE.addonsNow={mod:false,nucleus:'chooser',start:'native',auto:'last',countdown:0,at_start:true}; CONSOLE.addons=Object.assign({},CONSOLE.addonsNow,{mod:true}); CONSOLE.startNowName=null; CONSOLE.startNowKind=null; CONSOLE.keptReplaced=false; CONSOLE.hakchi=null;");
  let sp=R('startupPageHtml()');
  out.kept={last:sp.includes('<option value="last" selected>Last menu used</option>'), text:sp.includes('Nothing starts until you choose. The menu you used last is selected first; until you have used one, that is Original Menu.'),
    said:sp.includes('The countdown is off, as set on the console’s startup chooser (Recovery and Settings). It stays.'), sent:R('JSON.stringify(planBody())')};
  R("CONSOLE.addonsNow={mod:true,nucleus:'chooser',start:'snes',auto:'menu',countdown:20,at_start:false}; CONSOLE.addons=Object.assign({},CONSOLE.addonsNow); CONSOLE.startNowName='SNES'; CONSOLE.startNowKind='owner-root';");
  sp=R('startupPageHtml()');
  out.own={listed:sp.includes('<option value="snes" selected>SNES (as set now)</option>'), box:sp.includes('id="cAtStart"')&&!sp.includes('id="cAtStart" checked'),
    said:sp.includes('The countdown of 20 seconds is the one set on the console’s startup chooser (Recovery and Settings). It stays.'), text:sp.includes('SNES opens straight away. The startup chooser does not show at start')};
  R("CONSOLE.addons.nucleus='direct'"); sp=R('startupPageHtml()');
  out.own.direct={start:R('CONSOLE.addons.start'), said:sp.includes('SNES opens first only from the startup chooser, so Original Menu opens first instead.')};
  R("CONSOLE.addons.nucleus='chooser'"); R('startupPageHtml()'); out.own.back=R('CONSOLE.addons.start');
  console.log(JSON.stringify(out)); })().catch(e=>{ console.log(JSON.stringify({error:String(e&&e.stack||e)})); process.exit(1); });
"""

PORT = int(os.environ.get("CCS_TEST_PORT") or 8774); B = f"http://127.0.0.1:{PORT}"
@unittest.skipUnless(shutil.which("node"), "node is not installed")
class Page(unittest.TestCase):
    """the Install on Console page (tools/rom-pipeline/studio/install.js) through the app's own backend: the two add-on pages, what they
    start from, the review's summary and a rehearsal of the chosen add-ons"""
    @classmethod
    def setUpClass(cls):
        cls.t = tempfile.mkdtemp(prefix="ccs-add-page-", dir=_T); cls.nh = os.path.join(cls.t, "native-home")
        m30 = bytes([0x4E, 0x45, 0x53, 0x1A, 1, 1, 0xE0, 0x10]) + bytes(8) + bytes(24576)   # an empty cartridge image of mapper 30 (not a game)
        staged(os.path.join(cls.nh, "console", "current"), more=[("g2", "Mapper Thirty Demo", m30)])
        cfgp = os.path.join(cls.t, "companion.json"); wr(cfgp, json.dumps({"hakchi_exe": os.path.join(cls.t, "no-hakchi", "hakchi.exe"), "port": PORT, "no_launch": True, "exports_dir": os.path.join(cls.t, "exports"), "staging_dir": os.path.join(cls.t, "staging")}))
        cls.token = secrets.token_hex(32); env = dict(os.environ, CCS_COMPANION_CONFIG=cfgp, CCS_PERSONAL_DIR=os.path.join(cls.t, "personal"), CCS_STUDIO_DATA=cls.t, CCS_NATIVE_HOME_ROOT=cls.nh,
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
    def test_50_the_page_reviews_the_addon_choice_and_rehearses_it(self):
        js = os.path.join(HERE, "..", "rom-pipeline", "studio", "install.js")
        r = subprocess.run([shutil.which("node"), "-e", PAGE, js, B, self.token], capture_output=True, text=True, timeout=900, encoding="utf-8")
        self.assertEqual(r.returncode, 0, (r.stdout[-1500:], r.stderr[-1500:])); out = json.loads(r.stdout.strip().splitlines()[-1])
        self.assertEqual(out["page1"], {"title": True, "mod": True, "locked": False, "author": True, "next": True}, out)
        self.assertEqual(out["see_home"], 1, out)
        self.assertEqual(out["page2"], {"nucleus": False, "keep": True, "review": True, "back": True}, out)
        self.assertEqual(json.loads(out["on"]["addons"]), {"mod": True, "nucleus": "direct", "start": "ccs-home", "auto": "menu", "countdown": 10, "at_start": True}, out)
        self.assertTrue(out["on"]["immediate"] and out["on"]["home_listed"], out)
        self.assertEqual(out["chooser"], {"auto": "wait", "countdown": True}, out)
        self.assertEqual(out["no_mod"], {"start": "native", "said": True, "home_listed": False}, out)
        self.assertEqual(out["mod_again"], {"start": "ccs-home"}, out)
        rv = out["review"]; self.assertEqual(json.loads(rv.pop("plan")), {"mod": True, "nucleus": "direct", "start": "ccs-home", "auto": "menu", "countdown": 10, "at_start": True}, out)
        self.assertEqual(rv, {"home": True, "menu": True, "startup": True, "chooser": True, "ready": True, "change": True, "hakchi_off": True, "delivery": "home"}, out)
        self.assertEqual(out["rehearse"], {"installed": True, "added": ["ccs-mod", "ccs-nucleus"]}, out)
        self.assertEqual(out["ready"], {"tile": True}, out)
        wn = out["with_nucleus"]; a = json.loads(wn.pop("addons")); self.assertTrue(a["mod"] and a["nucleus"] != "none", out)
        self.assertEqual(wn, {"locked": True, "stays": True, "ready": True, "kept": True}, out)
        g = out["games"]; self.assertEqual(json.loads(g.pop("sent")), {"mod": False, "nucleus": "none", "start": "native", "auto": "menu", "countdown": 10, "at_start": True}, "the plan's own default: Hakchi games without CCS Mod")
        self.assertEqual(g, {"delivery": "games", "box": True, "text": True, "where": True, "added": True, "home": True, "left": True, "why": True, "sum": True, "ready": True}, out)
        n = out["neither"]; self.assertEqual(json.loads(n.pop("sent"))["hakchi_games"], False); self.assertEqual(n, {"said": True, "box": True, "change": True}, out)
        self.assertIn("Nothing from this collection would go to the console", CI.NOTHING_TEXT)
        self.assertEqual(out["games_rehearse"], {"result": "installed", "delivery": "games", "published": 1, "added": []}, out)
        self.assertEqual(out["games_verify"], {"files": True, "menu": True}, out); self.assertEqual(out["games_ready"], {"said": True}, out)
        k = out["kept"]; sent = json.loads(k.pop("sent")); self.assertEqual({x: sent[x] for x in ("mod", "auto", "countdown", "at_start")}, {"mod": True, "auto": "last", "countdown": 0, "at_start": True}, out)
        self.assertEqual(k, {"last": True, "text": True, "said": True}, "Last Used with no countdown is shown as it is and sent back unchanged with CCS Mod added")
        self.assertEqual(out["own"], {"listed": True, "box": True, "said": True, "text": True, "direct": {"start": "native", "said": True}, "back": "snes"},
                         "its own system image first, a 20-second countdown and the chooser not shown at start, as set on the console; that image opens first only from the chooser")

PAGE_PHYS = r"""
const vm=require('vm'), fs=require('fs');
const [JS, B, TOKEN, CONSOLE_JSON, ADMIT, FAULTS]=process.argv.slice(1);
const els={}; const mk=id=>({id, innerHTML:'', textContent:'', disabled:false, checked:false, value:'', onclick:null, onchange:null, classList:{add(){}, remove(){}, contains(){return false;}}});
const $=id=>{ const h=Object.values(els).map(e=>e.innerHTML).join(''); if(!els[id]&&!['hkBody','hkTitle','hkSub','hkpanel','nhPkg'].includes(id)&&!h.includes(`id="${id}"`)) return null; return els[id]||(els[id]=mk(id)); };
const H={'Content-Type':'application/json','X-CCS-Protocol':'1',Origin:'https://classiccartstudio.com',Authorization:'Bearer '+TOKEN};
async function hkPost(p,b){ const r=await fetch(B+p,{method:'POST',headers:H,body:JSON.stringify(b||{})}); const j=await r.json(); return Object.assign({status:r.status}, j); }
async function capi(p){ const r=await fetch(B+p,{headers:H}); return Object.assign({status:r.status}, await r.json()); }
const ctx=vm.createContext({console, setTimeout, clearTimeout, setInterval, clearInterval, fetch, $, hkPost, capi, document:{addEventListener(){}, querySelectorAll(){return [];}},
  esc:s=>String(s==null?'':s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;'), fmt:n=>n+' B', COMPANION:{ok:true, desktop:true, capabilities:['console-sim']},
  INST:{step:'inspect'}, STEPS:[], stepperHtml:()=>'', renderInstall:async()=>{}, procSet(){}, PROC:{}, HOME_STARTUP:[], HOME_THEME_META:[], homeModelFromStudio:()=>({theme:'arcade-grid'}), dispatchEvent(){}, CustomEvent:function(){}, openTv(){} });
ctx.window=ctx; vm.runInContext(fs.readFileSync(JS,'utf8'), ctx);
const R=(code)=>vm.runInContext(code, ctx);
const phys=()=>els.cPhys?els.cPhys.innerHTML:'';
const btn=id=>{ const m=phys().match(new RegExp('<button[^>]*id="'+id+'"[^>]*>')); return m?{on:!/ disabled/.test(m[0])}:null; };
const setConsole=o=>{ const c=JSON.parse(fs.readFileSync(CONSOLE_JSON,'utf8')); fs.writeFileSync(CONSOLE_JSON, JSON.stringify(Object.assign(c,o))); };
const until=async(f,ms)=>{ for(let i=0;i<(ms||120000)/50;i++){ if(f()) return true; await new Promise(r=>setTimeout(r,50)); } throw new Error('timed out'); };
// a click as a person makes it: some buttons start their work without returning it, so the page is read again only once it settled
const click=async id=>{ if(!els[id]||!els[id].onclick) throw new Error('no '+id+' on the page: '+R("JSON.stringify({result:PHYS.result,check:PHYS.check,confirm:PHYS.confirm})").slice(0,600));
  const before=R('PHYS.result'), conf=R('PHYS.confirm'); await els[id].onclick();
  if(id==='hkPhys'||id==='pRemove') await until(()=>R('PHYS.confirm')!==conf||R('PHYS.result')!==before); else await until(()=>!R('PHYS.busy')&&R('PHYS.result')!==before); };
(async()=>{ const out={};
  // Console State: On your console, every step available (no "Turned off" text), nothing checked yet
  R("INST.step='inspect'"); await R('renderConsoleStep()');
  out.state={panel:phys().includes('<b>On your console</b>'), off:phys().includes('Turned off'), inspect:btn('pInspect'), install:btn('hkPhys'), ops:els.hkBody.innerHTML.includes('Turned off')};
  await click('pInspect'); const c=R('physConsole()');
  out.checked={label:c&&c.label, model:c&&c.identity.model, result:R("PHYS.result&&PHYS.result.result&&PHYS.result.result.ok")};
  // the plan, checked against the checked console, confirmed and installed; then verified; Verify and Complete show the change on the console
  await R('ensurePlan(true)'); R("INST.step='install'"); await R('renderConsoleStep()');
  out.before_check={install:btn('hkPhys')};
  await click('pCheck'); out.check={ok:R('PHYS.check&&PHYS.check.ok'), install:btn('hkPhys')};
  await click('hkPhys'); out.confirm={covers:R("PHYS.confirm&&PHYS.confirm.covers")||'', yes:phys().includes('Confirm and Install')};
  await click('pConfYes'); const r=R("PHYS.result&&PHYS.result.result"); out.install={result:r&&r.result, simulated:r&&r.simulated, next:els.hkNext&&!els.hkNext.disabled};
  await R('consoleStatus()'); await click('pInspect'); await click('pVerify'); const v=R("PHYS.result&&PHYS.result.result"); out.verify={verified:v&&v.verified};
  R("INST.step='verify'"); await R('renderConsoleStep()'); out.verify_step={sub:els.hkSub.textContent, panel:phys().includes('On your console')};
  R("INST.step='ready'"); await R('renderConsoleStep()'); out.ready={said:els.hkBody.innerHTML.includes('Installed and checked'), sim:els.hkBody.innerHTML.includes('SIMULATION')};
  // a refused backup admission: the confirmation is made, the change is refused before any worker, and the console is not changed
  fs.writeFileSync(ADMIT, 'refuse'); await click('pInspect'); R("INST.step='install'"); await R('renderConsoleStep()'); await click('pCheck');
  await click('hkPhys'); await click('pConfYes'); out.refused={code:R("PHYS.result&&PHYS.result.code"), text:R("PHYS.result&&PHYS.result.text")}; fs.writeFileSync(ADMIT, 'ok');
  // another console answers after the check: the install is refused by the engine before any change
  await click('pInspect'); await click('pCheck'); await click('hkPhys'); setConsole({hwid:'sim-SWAPPED'}); await click('pConfYes');
  const sw=R("PHYS.result&&PHYS.result.result"); out.swapped={result:sw&&sw.result, why:sw&&sw.why}; setConsole({hwid:'sim-0001'});
  // no confirmation: the backend refuses the change itself
  const nc=await R("hkPost('/api/console/home/install',{plan_hash:CONSOLE.plan.hash})"); out.unconfirmed={status:nc.status, code:nc.code};
  // removal, exactly as reviewed and confirmed
  await click('pInspect'); await click('pReview'); out.review={ok:R('!!(PHYS.review&&PHYS.review.ok)'), remove:R("PHYS.review&&PHYS.review.remove.length")};
  await click('pRemove'); out.remove_confirm=R("PHYS.confirm&&PHYS.confirm.operation"); await click('pConfYes'); const u=R("PHYS.result&&PHYS.result.result"); out.uninstall={result:u&&u.result};
  // an installation whose connection is lost after a change stops there (nothing more is written); its recovery is its own confirmed step
  fs.writeFileSync(FAULTS, JSON.stringify({after:6})); await click('pInspect'); await click('pCheck'); await click('hkPhys'); await click('pConfYes');
  const st=R("PHYS.result&&PHYS.result.result"); out.stopped={result:st&&st.result, attempt:st&&st.attempt}; fs.unlinkSync(FAULTS);
  await click('pInspect'); await click('pRecRev'); const a=R("PHYS.recovery&&PHYS.recovery.attempts&&PHYS.recovery.attempts[0]"); out.recovery={attempt:a&&a.attempt, recoverable:a&&a.recoverable};
  await R("physConfirm('recover',{attempt:PHYS.recovery.attempts[0].attempt})"); await until(()=>R('!!PHYS.confirm')); await click('pConfYes'); const rc=R("PHYS.result&&PHYS.result.result"); out.recovered={result:rc&&rc.result};
  // another Hakchi setup on the console: refused with the notice and the official Hakchi2 CE 3.9.3 release page (opened outside the window)
  setConsole({hakchi:'v1.0.3-111'}); await click('pInspect'); out.hakchi={checked:!!R('physConsole()'), title:phys().includes('Hakchi2 CE 3.9.3 is required'),
    link:phys().includes('href="https://github.com/TeamShinkansen/Hakchi2-CE/releases/tag/v3.9.3" target="_blank" rel="noopener"'), found:phys().includes('hakchi v1.0.3-111')}; setConsole({hakchi:'v1.0.4-126'});
  // a Super NES Classic (its own software's board and region): checked as one; the page counts its 21 built-in games; the plan is checked against it
  setConsole({system:'snes-usa', board:'dp-shvc', region:'USA', hwid:'sim-0014'}); await click('pInspect'); const s=R('physConsole()');
  out.snes={label:s&&s.label, model:s&&s.identity.model, games:R('stockGames()')}; await click('pCheck'); out.snes.check={ok:R('PHYS.check&&PHYS.check.ok'), why:R("PHYS.check&&PHYS.check.why")||''};
  // a Super Famicom Classic Mini (JPN): never a checked console
  setConsole({system:'shvc-jpn', board:'dp-shvc', region:'JPN', hwid:'sim-0016'}); await click('pInspect'); out.sfc={checked:!!R('physConsole()'), why:R("PHYS.result&&PHYS.result.result&&PHYS.result.result.why")||''};
  console.log(JSON.stringify(out)); })().catch(e=>{ console.log(JSON.stringify({error:String(e&&e.stack||e)})); process.exit(1); });
"""

@unittest.skipUnless(shutil.which("node"), "node is not installed")
class PagePhysical(unittest.TestCase):
    """the physical steps on the Install on Console page (install.js) through the app's own backend, routes, dispatcher, confirmations,
    worker protocol and installation engine (console_test_backend.py: the worker runs in the backend's process and its console sessions open
    a disposable stand-in; the backup prerequisite is answered by the test): check the console, check the plan, confirm, install, verify;
    review and remove; an installation stopped by a lost connection and its separately confirmed recovery;
    a refused backup admission, another console answering after the check and a change without a confirmation all refuse before any change;
    a Super NES Classic is checked as one and the plan is checked against its model; a Super Famicom Classic Mini is never a checked console"""
    @classmethod
    def setUpClass(cls):
        cls.t = tempfile.mkdtemp(prefix="ccs-add-phys-", dir=_T); cls.nh = os.path.join(cls.t, "native-home"); staged(os.path.join(cls.nh, "console", "current"))
        cls.standin = os.path.join(cls.t, "console"); CI.make_stand_in(cls.standin, "hakchi-ready"); cls.stock = CI.stock_digest(cls.standin)
        cls.admit = os.path.join(cls.t, "admit.txt"); wr(cls.admit, b"ok"); cls.faults = os.path.join(cls.t, "faults.json")
        cfgp = os.path.join(cls.t, "companion.json"); wr(cfgp, json.dumps({"hakchi_exe": os.path.join(cls.t, "no-hakchi", "hakchi.exe"), "port": PORT + 2, "no_launch": True, "exports_dir": os.path.join(cls.t, "exports"), "staging_dir": os.path.join(cls.t, "staging")}))
        cls.token = secrets.token_hex(32); env = dict(os.environ, CCS_COMPANION_CONFIG=cfgp, CCS_PERSONAL_DIR=os.path.join(cls.t, "personal"), CCS_STUDIO_DATA=cls.t, CCS_NATIVE_HOME_ROOT=cls.nh,
                                                       CCS_HOST_TOKEN=cls.token, CCS_HOST_PID=str(os.getpid()), PYTHONIOENCODING="utf-8", CCS_TEST_STANDIN=cls.standin, CCS_TEST_ADMIT_FILE=cls.admit, CCS_TEST_FAULTS_FILE=cls.faults, **chooser_env())
        cls.log = open(os.path.join(cls.t, "companion.log"), "w", encoding="utf-8"); cls.B = f"http://127.0.0.1:{PORT + 2}"
        cls.proc = subprocess.Popen([sys.executable, "-u", "-B", os.path.join(HERE, "console_test_backend.py"), os.path.join(HERE, "companion_app.py"), "--headless", "--port", str(PORT + 2)], env=env, stdout=cls.log, stderr=subprocess.STDOUT)
        for _ in range(200):
            try:
                with urllib.request.urlopen(cls.B + "/api/ping", timeout=1): break
            except Exception: time.sleep(0.25)
    @classmethod
    def tearDownClass(cls):
        cls.proc.terminate()
        try: cls.proc.wait(20)
        except subprocess.TimeoutExpired: cls.proc.kill(); cls.proc.wait(10)
        cls.log.close()
    def test_51_the_page_runs_the_physical_steps_with_every_safeguard(self):
        js = os.path.join(HERE, "..", "rom-pipeline", "studio", "install.js")
        r = subprocess.run([shutil.which("node"), "-e", PAGE_PHYS, js, self.B, self.token, os.path.join(self.standin, "console.json"), self.admit, self.faults], capture_output=True, text=True, timeout=900, encoding="utf-8")
        self.assertEqual(r.returncode, 0, (r.stdout[-1500:], r.stderr[-1500:])); out = json.loads(r.stdout.strip().splitlines()[-1])
        self.assertEqual(out["state"], {"panel": True, "off": False, "inspect": {"on": True}, "install": {"on": False}, "ops": False}, out)
        self.assertTrue(out["checked"]["label"].startswith("NES Classic Edition (nes-usa, hakchi v1.0.4-126)"), out); self.assertEqual((out["checked"]["model"], out["checked"]["result"]), ("nes-classic-edition", True), out)
        self.assertEqual(out["before_check"], {"install": {"on": False}}, "no install before the plan is checked against the console")
        self.assertEqual(out["check"], {"ok": True, "install": {"on": True}}, out)
        self.assertIn("Once, within 5 minutes: install this collection as planned on NES Classic Edition", out["confirm"]["covers"]); self.assertTrue(out["confirm"]["yes"])
        self.assertEqual(out["install"], {"result": "installed", "simulated": True, "next": True}, out)
        self.assertEqual(out["verify"], {"verified": True}, out); self.assertEqual(out["verify_step"], {"sub": "Verify", "panel": True}, out)
        self.assertEqual(out["ready"], {"said": True, "sim": False}, out)
        self.assertEqual(out["refused"]["code"], "backup-required", out); self.assertIn("Back up this console first", out["refused"]["text"])
        self.assertEqual(out["swapped"]["result"], "refused", out); self.assertIn("not the console that was checked", out["swapped"]["why"])
        self.assertEqual(out["unconfirmed"], {"status": 403, "code": "not-confirmed"}, out)
        self.assertEqual((out["review"]["ok"], out["remove_confirm"], out["uninstall"]), (True, "uninstall", {"result": "uninstalled"}), out); self.assertGreater(out["review"]["remove"], 0)
        self.assertEqual(out["stopped"]["result"], "stopped", out); self.assertEqual(out["recovery"], {"attempt": out["stopped"]["attempt"], "recoverable": True}, out)
        self.assertEqual(out["recovered"], {"result": "rolled-back"}, out)
        self.assertEqual(out["hakchi"], {"checked": False, "title": True, "link": True, "found": True}, out)
        self.assertTrue(out["snes"]["label"].startswith("Super NES Classic Edition (snes-usa"), out); self.assertEqual((out["snes"]["model"], out["snes"]["games"]), ("snes-classic-edition", 21), out)
        import platform_ids
        if not platform_ids.allowed("capability", "snes-classic-addons"):
            self.assertFalse(out["snes"]["check"]["ok"]); self.assertIn("Super NES Classic Edition is not available in this version", out["snes"]["check"]["why"])
        self.assertFalse(out["sfc"]["checked"], out); self.assertIn("is not one Classic Cart Studio supports", out["sfc"]["why"])
        self.assertEqual(CI.stock_digest(self.standin), self.stock, "the stand-in's built-in games, saves and other modules are as they were")

REAL = ("CCS_STUDIO_SITE", "CCS_CONSOLE_PLAYER", "CCS_NATIVE_HOME_RUNTIME", "CCS_PLAYER_FONTS")
PORT2 = PORT + 1; B2 = f"http://127.0.0.1:{PORT2}"
@unittest.skipUnless(all(os.environ.get(k) and os.path.exists(os.environ[k]) for k in REAL), "the built site, console player, HOME runtime and player fonts are not given (" + ", ".join(REAL) + ")")
class RealPackage(unittest.TestCase):
    """the app's own route: /api/home/native-package builds the native HOME package and stages the console package in CCS HOME's layout (the real
    builder, an Included Game from the built site, the collection's own startup picture), then the review and rehearsals of add-on choices
    (the startup chooser's package from the app's own HOME runtime and player fonts)"""
    @classmethod
    def setUpClass(cls):
        cls.t = tempfile.mkdtemp(prefix="ccs-add-real-", dir=_T); cls.nh = os.path.join(cls.t, "native-home")
        if "personal" not in sys.modules: os.environ["CCS_PERSONAL_DIR"] = os.path.join(cls.t, "personal")
        import personal as ps, library_lookup as LL
        pdir = ps.PERSONAL_DIR   # the backend reads the same personal library
        site = os.environ["CCS_STUDIO_SITE"]; idx = json.load(open(os.path.join(site, "included", "index.json"), encoding="utf-8")); g = idx["games"][0]
        r = ps.ingest(os.path.join(site, "included", g["file_name"]), origin="included"); cls.pid = pid = r["entry"]["id"]; ps.merge_metadata(pid, {"title": g["title"], "version_label": g["version"]})
        title = LL.personal_index()["entries"][pid]["user"]["title"]
        import test_startup_custom_package as SC, base64
        jp = SC.jpeg(); h = sha(jp); cls.pic = h
        cls.man = {"schema": "nes-classic-collection/4", "name": "Add-on test", "hidden_factory_games": [], "selections": [{"game_id": pid, "version_id": pid, "display_title": title, "source": "personal", "install_required": True}]}
        cls.home = {"schema": "ccs-home-collection/1", "theme": "arcade-grid", "order": [pid], "games": [{"id": pid, "title": title, "version_label": "", "kind": "personal"}], "folders": [], "favorites": {"items": {}},
                    "settings": {"reduced_motion": False, "hints": True}, "sample": False, "map_shortcuts": [None, None], "apply_theme": False, "startup_image": "custom-" + h[:16],
                    "startup_custom": {"id": "custom-" + h[:16], "sha256": h, "name": "Living room", "fit": "fill", "bytes": len(jp), "width": 1280, "height": 720, "data": base64.b64encode(jp).decode()}}
        cfgp = os.path.join(cls.t, "companion.json"); wr(cfgp, json.dumps({"hakchi_exe": os.path.join(cls.t, "no-hakchi", "hakchi.exe"), "port": PORT2, "no_launch": True, "exports_dir": os.path.join(cls.t, "exports"), "staging_dir": os.path.join(cls.t, "staging")}))
        cls.token = secrets.token_hex(32); env = dict(os.environ, CCS_COMPANION_CONFIG=cfgp, CCS_PERSONAL_DIR=pdir, CCS_STUDIO_DATA=cls.t, CCS_NATIVE_HOME_ROOT=cls.nh, CCS_HOST_TOKEN=cls.token, CCS_HOST_PID=str(os.getpid()), PYTHONIOENCODING="utf-8")
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
        r = urllib.request.Request(B2 + path, data=json.dumps(body).encode() if body is not None else None, method=method, headers={"Content-Type": "application/json", "X-CCS-Protocol": "1", "Origin": "https://classiccartstudio.com", "Authorization": "Bearer " + self.token})
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
    def test_60_the_real_package_reviewed_and_rehearsed_with_addon_choices(self):
        res = self.build(self.man, self.home); c = res.get("console") or {}; self.assertTrue(res.get("ok") and c.get("ok"), (res.get("error"), c)); self.assertEqual(c["layout"], "mod")
        cur = os.path.join(self.nh, "console", "current"); a = os.path.join(cur, "mod"); self.assertEqual(sorted(os.listdir(cur)), ["mod"])
        self.assertTrue(CPK.verify(a)["ok"]); ma = json.loads(rd(os.path.join(a, "MANIFEST.json")))
        self.assertEqual(json.loads(rd(os.path.join(a, "session.json")))["home"]["leave"]["label"], "Choose Another Menu")
        st = json.loads(rd(os.path.join(a, "startup", "startup.json"))); self.assertEqual(st["choice"], "custom-" + self.pic[:16]); self.assertEqual(ma["startup"]["custom"]["source_sha256"], self.pic, "the collection's own picture")
        s, j = self.req("GET", "/api/console/home/status"); self.assertEqual(j["staged"]["layout"], "mod")
        s, j = self.req("POST", "/api/console/home/plan", {}); self.assertEqual(s, 200, j); h = j["result"]["hash"]
        s, j = self.req("POST", "/api/console/home/preflight", {"plan_hash": h, "profile": "with-nucleus"}); pf = j["result"]; self.assertFalse(pf["ok"]); self.assertEqual(pf["why"], CI.REMOVES_TEXT)
        s, j = self.req("POST", "/api/console/home/plan", pf["keep_choice"]); hk = j["result"]["hash"]
        s, j = self.req("POST", "/api/console/home/rehearse", {"plan_hash": hk, "profile": "with-nucleus", "scenario": "update", "then": ["uninstall"]}); x = self.run_wait(j["run"])["result"]
        # an update rehearsal first installs an earlier HOME with the same add-on choice (CCS HOME's one layout comes with CCS Mod), so the update
        # itself adds nothing; CCS Mod is this program's and goes with the removal, the CCS Nucleus another tool added stays
        self.assertEqual((x["setup"][0]["installed_first"], x["install"]["result"], x["install"]["added"]), ("installed", "installed", []), x); self.assertTrue(x["uninstall"]["ok"])
        self.assertEqual((x["uninstall"]["addons_removed"], x["uninstall"]["addons_kept"]), (["ccs-mod"], ["ccs-nucleus"]), x)
        s, j = self.req("POST", "/api/console/home/plan", {"nucleus": "chooser", "start": "ccs-home"}); self.assertEqual(s, 200, j)
        s, j = self.req("POST", "/api/console/home/rehearse", {"plan_hash": j["result"]["hash"], "profile": "hakchi-ready", "scenario": "first-install"})
        x = self.run_wait(j["run"])["result"]; self.assertEqual((x["install"]["result"], x["install"]["start"]), ("installed", "ccs-home always on default 10"), x)
        # a first install on a console with hakchi and none of the add-ons adds every add-on of its choice: CCS Mod (CCS HOME's layout), CCS
        # Nucleus and its startup chooser (a first install that added nothing would fail here)
        self.assertEqual(x["install"]["added"], ["ccs-mod", "ccs-nucleus", "ccs-nucleus-chooser"], x)
        # a game whose file is not here: the whole collection is refused and the staged package stays the one before
        before = rd(os.path.join(a, "MANIFEST.json")); man2 = json.loads(json.dumps(self.man)); home2 = json.loads(json.dumps(self.home))
        man2["selections"].append({"game_id": "p_00000000deadbeef", "version_id": "p_00000000deadbeef", "display_title": "Missing", "source": "personal", "install_required": True})
        home2["order"].append("p_00000000deadbeef"); home2["games"].append({"id": "p_00000000deadbeef", "title": "Missing", "version_label": "", "kind": "personal"})
        res2 = self.build(man2, home2); c2 = res2.get("console") or {}
        self.assertFalse(c2.get("ok"), c2) if res2.get("ok") else self.assertFalse(res2.get("ok"))
        self.assertEqual(rd(os.path.join(a, "MANIFEST.json")), before, "the package staged before stays as it was")

if __name__ == "__main__":
    try:
        r = unittest.main(exit=False, verbosity=2).result
        print(f"RESULT {r.testsRun - len(r.failures) - len(r.errors) - len(r.skipped)}/{r.testsRun} passed, {len(r.skipped)} skipped"); code = 1 if r.failures or r.errors else 0
    finally: shutil.rmtree(_T, ignore_errors=True)
    sys.exit(code)
