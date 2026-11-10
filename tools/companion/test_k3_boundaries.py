"""K3 boundaries after Codex's cs3 review (CS3-F1, CS3-F2, durability, export), PC only, against disposable stand-ins: ownership is executed exactly
as reviewed (an unknown pointer, a file replaced during a rollback, a transfer cut short), the console now answering is the one bound to the
operation (another console, another system, an older hakchi, an unreadable identity, a console swapped mid-way), every intent is on this
computer's disk and the console's writes are made persistent in order (a power loss at every change, a journal cut short, a lost answer),
and the saves export reads any supported size in bounded pieces and says complete, partial, incomplete or empty. No device is opened.
  python tools/companion/test_k3_boundaries.py"""
from __future__ import annotations
import json, os, shutil, sys, tempfile, unittest
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import console_test_guard   # noqa: E402,F401  (first: the controlled console boundary, k3e)
from console_engine import ccs_install as CI   # noqa: E402
from test_ccs_install import make_package, tree, changes, rd, wr, sha   # noqa: E402

def boot_state(root: str) -> str:
    """what the console would start, as its start-up script decides it: "nintendo", or "ccs:<copy>" for a complete copy; "INCOMPLETE:<copy>"
    when the script would start a copy whose files do not all match its record (the state no interruption may ever leave)"""
    c = CI.FakeConsole(root); mod = c.pack_files(CI.HOOK_NAME)
    if mod != {CI.HOOK_FILE: CI.module_sha()} or c.overlay_sha256(CI.HOOK_FILE) != CI.module_sha(): return "nintendo" if not mod and c.overlay_sha256(CI.HOOK_FILE) is None else "MODULE-PARTIAL"
    b = c.read(CI.POINTER, 64); v = (b or b"").decode(errors="replace").strip()
    if not CI.COPY_RE.match(v) or c.stat(f"{CI.PKG_DIR}/{v}")["type"] != "dir" or c.stat(f"{CI.PKG_DIR}/{v}/bin/ccs-player-armv7")["type"] != "file" or c.stat(f"{CI.PKG_DIR}/{v}/session.json")["type"] != "file": return "nintendo"
    rec = json.loads(c.read(f"{CI.INST_DIR}/v-{v}.json") or b"{}")
    ok = bool(rec.get("files")) and all(c.sha256(f"{CI.PKG_DIR}/{v}/{rel}") == h for rel, h in rec["files"].items())
    return f"ccs:{v}" if ok else f"INCOMPLETE:{v}"

class ReplaceAfterPut(CI.FakeConsole):
    """the put of one file takes effect, something replaces its bytes, then the answer is lost (Codex's changed-during-rollback case)"""
    suffix = "/session.json"; fired = False
    def put(self, path, data, mode="0o644", source=None):
        r = super().put(path, data, mode, source)
        if path.endswith(self.suffix) and not self.fired:
            self.fired = True
            with open(self.p(path), "wb") as f: f.write(b"UNKNOWN BYTES FROM SOMETHING ELSE")
            raise ConnectionError("the console stopped answering after the write")
        return r

class CutPut(CI.FakeConsole):
    """a transfer cut short: the first half of one file arrives, then the connection is lost"""
    suffix = "/bin/ccs-player-armv7"; fired = False
    def put(self, path, data, mode="0o644", source=None):
        if path.endswith(self.suffix) and not self.fired:
            self.fired = True; self._may_write(path); self.nchanges += 1
            with open(self.p(path), "wb") as f: f.write(data[: len(data) // 2])
            self.writes.append(path); raise ConnectionError("the connection was lost during the transfer")
        return super().put(path, data, mode, source)

class Base(unittest.TestCase):
    def setUp(self):
        self.t = tempfile.mkdtemp(prefix="ccs-k3b-"); self.pkg = make_package(os.path.join(self.t, "pkg-a")); self.plan = CI.build_plan(self.pkg)
        self.root = os.path.join(self.t, "console"); CI.make_stand_in(self.root, "hakchi-ready"); self.jdir = os.path.join(self.t, "journals"); os.makedirs(self.jdir); self.n = 0
    def tearDown(self): shutil.rmtree(self.t, ignore_errors=True)
    def jnew(self): self.n += 1; return os.path.join(self.jdir, f"j{self.n}.jsonl")
    def con(self, cls=CI.FakeConsole, root=None, durable=False, **faults): return cls(root or self.root, faults, durable)
    def setident(self, **k):
        p = os.path.join(self.root, "console.json"); info = json.loads(rd(p)); info.update(k); wr(p, json.dumps(info))

# ---------------------------------------------------------------- CS3-F1: ownership as reviewed, through removal and recovery
class Ownership(Base):
    def test_01_an_unknown_pointer_is_kept_by_the_review_and_by_the_removal(self):
        wr(os.path.join(self.root, "var/lib/ccs/pkg/current"), b"OWNER POINTER CONTENT")
        p = CI.uninstall_plan(self.con()); self.assertTrue(p["ok"], p)
        self.assertFalse(p["pointer"]["owned"]); self.assertIsNone(p["pointer"]["value"]); self.assertNotIn(CI.POINTER, [a.get("path") for a in p["actions"]])
        self.assertTrue(any("not this program's pointer" in k for k in p["keep"]))
        r = CI.uninstall(self.con(), expect=p["hash"]); self.assertTrue(r["ok"], r); self.assertNotIn(CI.POINTER, r["removed"])
        self.assertEqual(rd(os.path.join(self.root, "var/lib/ccs/pkg/current")), b"OWNER POINTER CONTENT"); self.assertTrue(r["verified"]["pointer"])

    def test_02_an_installed_home_whose_pointer_was_replaced_keeps_that_pointer(self):
        a = CI.Installer(self.con(), self.plan, self.pkg, self.jnew()).run(); self.assertTrue(a["ok"], a)
        wr(os.path.join(self.root, "var/lib/ccs/pkg/current"), b"0123456789abcdef.0badc0de\n")   # well formed, but no record of it
        self.assertEqual(CI.Installer(self.con(), self.plan, self.pkg, self.jnew()).run()["result"], "refused")
        p = CI.uninstall_plan(self.con()); self.assertFalse(p["pointer"]["owned"])
        r = CI.uninstall(self.con(), expect=p["hash"]); self.assertTrue(r["ok"], r)
        self.assertEqual(rd(os.path.join(self.root, "var/lib/ccs/pkg/current")), b"0123456789abcdef.0badc0de\n"); self.assertEqual(boot_state(self.root), "nintendo")

    def test_03_a_file_replaced_during_the_attempt_is_kept_and_recovery_is_incomplete(self):
        before = tree(self.root); c = self.con(ReplaceAfterPut); r = CI.Installer(c, self.plan, self.pkg, self.jnew()).run()
        self.assertFalse(r["ok"]); rb = r["rolled_back"]; self.assertFalse(rb["ok"], rb)
        self.assertTrue(any("session.json" in x and "kept" in x for x in rb["problems"]), rb)
        q = os.path.join(self.root, "var/lib/ccs/pkg", f"{self.plan['version']}.{r['attempt']}", "session.json")
        self.assertEqual(rd(q), b"UNKNOWN BYTES FROM SOMETHING ELSE")                      # never removed because of its current hash
        self.assertTrue(os.path.isfile(os.path.join(self.root, "var/lib/ccs/install", f"attempt-{r['attempt']}.json")))   # what is known stays recorded
        self.assertTrue(os.path.isfile(os.path.join(self.root, "var/lib/ccs/install", f"v-{self.plan['version']}.{r['attempt']}.json")))
        self.assertEqual(boot_state(self.root), "nintendo"); self.assertEqual(CI.stock_digest(self.root), {k: sha(v) for k, v in CI.STOCK_SAMPLE.items()})
        self.assertIn("earlier attempt", CI.Installer(self.con(), self.plan, self.pkg, self.jnew()).run()["why"])   # nothing further until recovered
        self.assertTrue(set(changes(before, tree(self.root))) <= {k for k in tree(self.root) if k.startswith("var/lib/ccs/")})

    def test_04_a_transfer_cut_short_is_this_attempts_and_is_undone(self):
        before = tree(self.root); c = self.con(CutPut); r = CI.Installer(c, self.plan, self.pkg, self.jnew()).run()
        self.assertFalse(r["ok"]); self.assertTrue(r["rolled_back"]["ok"], r); self.assertEqual(changes(before, tree(self.root)), [])

    def test_05_a_cut_transfer_whose_source_changed_cannot_be_proven_and_is_kept(self):
        c = self.con(CutPut); inst = CI.Installer(c, self.plan, self.pkg, self.jnew(), policy="stop"); r = inst.run()
        self.assertEqual(r["result"], "stopped"); self.assertEqual(r["switched"], "no")
        wr(os.path.join(self.pkg, "bin", "ccs-player-armv7"), b"rebuilt since")              # the computer's copy is no longer the plan's
        rr = CI.Installer(self.con(), self.plan, self.pkg, inst.journal).resume(); self.assertEqual(rr["result"], "rollback-incomplete", rr)
        self.assertTrue(any("ccs-player-armv7" in x for x in rr["rolled_back"]["problems"]))
        self.assertTrue(os.path.isfile(os.path.join(self.root, "var/lib/ccs/install", f"attempt-{r['attempt']}.json")))

    def test_06_a_pointer_changed_by_something_else_during_recovery_is_kept(self):
        n = 0
        while True:   # stop right after the switch
            n += 1; shutil.rmtree(self.root); CI.make_stand_in(self.root, "hakchi-ready")
            inst = CI.Installer(self.con(after=n), self.plan, self.pkg, self.jnew(), policy="stop"); r = inst.run()
            if r.get("switched") == "yes": break
            self.assertLess(n, 60)
        wr(os.path.join(self.root, "var/lib/ccs/pkg/current"), b"something else\n")
        rr = CI.Installer(self.con(), self.plan, self.pkg, inst.journal).resume(); self.assertEqual(rr["result"], "rollback-incomplete", rr)
        self.assertEqual(rd(os.path.join(self.root, "var/lib/ccs/pkg/current")), b"something else\n")

    def test_07_a_module_changed_by_something_else_during_recovery_is_left(self):
        n = 0
        while True:   # stop right after the module
            n += 1; shutil.rmtree(self.root); CI.make_stand_in(self.root, "hakchi-ready")
            inst = CI.Installer(self.con(after=n), self.plan, self.pkg, self.jnew(), policy="stop"); r = inst.run()
            if r.get("at") == "transfer" and any(e.get("op") == "module" for e in CI.Installer.entries(inst.journal)): break
            self.assertLess(n, 60)
        wr(os.path.join(self.root, "var/lib/hakchi/rootfs/etc/init.d/S81clover-mcp"), b"#!/bin/sh\n# changed by something else\n")
        rr = CI.Installer(self.con(), self.plan, self.pkg, inst.journal).resume(); self.assertEqual(rr["result"], "rollback-incomplete", rr)
        self.assertEqual(rd(os.path.join(self.root, "var/lib/hakchi/rootfs/etc/init.d/S81clover-mcp")), b"#!/bin/sh\n# changed by something else\n")

    def test_08_the_removal_runs_only_as_reviewed(self):
        a = CI.Installer(self.con(), self.plan, self.pkg, self.jnew()).run(); self.assertTrue(a["ok"])
        p = CI.uninstall_plan(self.con()); before = tree(self.root)
        wr(os.path.join(self.root, "var/lib/ccs/pkg", a["copy"], "session.json"), b"changed after the review")
        r = CI.uninstall(self.con(), expect=p["hash"]); self.assertEqual(r["result"], "refused"); self.assertIn("review it again", r["why"])
        self.assertEqual(changes(before, tree(self.root)), [f"var/lib/ccs/pkg/{a['copy']}/session.json"])
        p2 = CI.uninstall_plan(self.con()); self.assertTrue(any("session.json (changed)" in k for k in p2["keep"]))
        r = CI.uninstall(self.con(), expect=p2["hash"]); self.assertTrue(r["ok"], r)
        self.assertEqual(rd(os.path.join(self.root, "var/lib/ccs/pkg", a["copy"], "session.json")), b"changed after the review")
        self.assertTrue(os.path.isfile(os.path.join(self.root, "var/lib/ccs/install", f"v-{a['copy']}.json"))); self.assertEqual(boot_state(self.root), "nintendo")

    def test_09_a_file_changed_between_the_review_and_its_removal_is_kept(self):
        a = CI.Installer(self.con(), self.plan, self.pkg, self.jnew()).run(); wr(os.path.join(self.root, "var/lib/ccs/data/saves/x/cart.sav"), b"battery")
        p = CI.uninstall_plan(self.con(), keep_data=False)
        class Racy(CI.FakeConsole):
            def remove(s, path, sha256):
                if path.endswith("cart.sav"): wr(s.p(path), b"saved again just now")
                return super().remove(path, sha256)
        r = CI.uninstall(Racy(self.root), keep_data=False, expect=p["hash"]); self.assertFalse(r["ok"]); self.assertEqual(r["result"], "incomplete")
        self.assertTrue(any("cart.sav" in k for k in r["kept_changed"])); self.assertEqual(rd(os.path.join(self.root, "var/lib/ccs/data/saves/x/cart.sav")), b"saved again just now")
        self.assertTrue(r["verified"]["module_removed"]); self.assertEqual(r["data"], "partly kept")

    def test_10_a_damaged_transfer_is_this_attempts_and_is_undone(self):
        # (added after k3c1: the cs3 guard's corrupt-transfer case found that the undo kept this attempt's own damaged file)
        damaged = 0
        for n in range(1, 30):   # every change; the ones that write a file of the copy are damaged in transfer
            shutil.rmtree(self.root); CI.make_stand_in(self.root, "hakchi-ready"); before = tree(self.root)
            r = CI.Installer(self.con(corrupt=n), self.plan, self.pkg, self.jnew()).run()
            if r.get("at") != "readback": continue
            damaged += 1
            with self.subTest(n): self.assertTrue(r["rolled_back"]["ok"], r); self.assertEqual(changes(before, tree(self.root)), [])
        self.assertGreaterEqual(damaged, 5)
        shutil.rmtree(self.root); CI.make_stand_in(self.root, "hakchi-ready")
        r = CI.Installer(self.con(ReplaceAfterPut), self.plan, self.pkg, self.jnew()).run(); self.assertFalse(r["rolled_back"]["ok"], r)   # still kept when replaced after the write

# ---------------------------------------------------------------- CS3-F2: the console answering now
class Identity(Base):
    def test_20_another_system_an_older_hakchi_a_custom_firmware_or_no_identity_change_nothing(self):
        for prof, words in (("snes-eur", "North American NES Classic"), ("old-hakchi", "pinned hakchi"), ("custom-firmware", "custom firmware"), ("no-identity", "identity could not be read"), ("stock", "hakchi must be installed first")):
            with self.subTest(prof):
                root = os.path.join(self.t, prof); CI.make_stand_in(root, prof); before = tree(root)
                r = CI.Installer(CI.FakeConsole(root), self.plan, self.pkg, self.jnew()).run(); self.assertEqual(r["result"], "refused", r); self.assertIn(words, r["why"])
                self.assertFalse(CI.uninstall_plan(CI.FakeConsole(root))["ok"]); self.assertFalse(CI.inspect_console(CI.FakeConsole(root))["ok"])
                self.assertEqual(CI.export_data(CI.FakeConsole(root), os.path.join(self.t, prof + "-x"))["status"], "refused")
                self.assertEqual(changes(before, tree(root)), [])

    def test_21_an_operation_bound_to_another_console_changes_nothing(self):
        before = tree(self.root); r = CI.Installer(self.con(), self.plan, self.pkg, self.jnew(), device="sim-OTHER").run()
        self.assertEqual(r["result"], "refused"); self.assertIn("not the console that was checked", r["why"]); self.assertEqual(changes(before, tree(self.root)), [])
        self.assertFalse(CI.uninstall_plan(self.con(), device="sim-OTHER")["ok"]); self.assertEqual(CI.export_data(self.con(), os.path.join(self.t, "x"), device="sim-OTHER")["status"], "refused")

    def test_22_recovery_on_a_different_console_changes_nothing(self):
        j = self.jnew()
        with self.assertRaises(SystemExit): CI.Installer(self.con(crash_after=10), self.plan, self.pkg, j).run()
        self.setident(hwid="sim-DIFFERENT-CONSOLE"); before = tree(self.root)
        r = CI.Installer(self.con(), self.plan, self.pkg, j).resume(); self.assertEqual(r["result"], "refused", r); self.assertEqual(changes(before, tree(self.root)), [])
        self.setident(hwid="sim-0001"); r = CI.Installer(self.con(), self.plan, self.pkg, j, device="sim-0009").resume(); self.assertEqual(r["result"], "refused")
        r = CI.Installer(self.con(), self.plan, self.pkg, j).resume(); self.assertEqual(r["result"], "rolled-back", r)

    def test_23_a_console_swapped_during_the_operation_stops_it_before_the_next_commit(self):
        for n in (3, 12):
            with self.subTest(n):
                shutil.rmtree(self.root); CI.make_stand_in(self.root, "hakchi-ready")
                c = self.con(swap_after=n); r = CI.Installer(c, self.plan, self.pkg, self.jnew(), policy="stop").run()
                self.assertEqual(r["result"], "stopped", r); self.assertIn("another console answers", r["why"]); self.assertEqual(r["switched"], "no")
                self.assertEqual(boot_state(self.root), "nintendo")

# ---------------------------------------------------------------- durability: intent on this computer's disk, the console's writes in order
class Durability(Base):
    def test_30_every_journal_line_is_written_through_before_its_change(self):
        calls = []; real = CI.os.fsync
        class Watch(CI.FakeConsole):
            def _change(s, what, act):
                calls.append(("change", what)); return super()._change(what, act)
        def fs(fd): calls.append(("fsync", fd)); return real(fd)
        CI.os.fsync = fs
        try: r = CI.Installer(Watch(self.root), self.plan, self.pkg, self.jnew()).run()
        finally: CI.os.fsync = real
        self.assertTrue(r["ok"], r)
        for k, (kind, _) in enumerate(calls):
            if kind == "change": self.assertEqual(calls[k - 1][0], "fsync", calls[max(0, k - 3):k + 1])   # the intent line reached the disk first

    def test_31_the_consoles_writes_are_made_persistent_in_order(self):
        j = self.jnew(); r = CI.Installer(self.con(), self.plan, self.pkg, j).run(); self.assertTrue(r["ok"], r)
        es = [e for e in CI.Installer.entries(j) if e.get("state") in ("intent", None)]; ops = [(e.get("op"), e.get("why") or e.get("path") or "") for e in es]
        idx = lambda pred: next(k for k, o in enumerate(ops) if pred(o))
        rec = idx(lambda o: o[0] == "record-attempt"); s1 = idx(lambda o: o[0] == "sync"); copyrec = idx(lambda o: o[0] == "record-copy")
        self.assertTrue(rec < s1 < copyrec, ops)                                     # the attempt record (with the earlier state) first
        last_put = max(k for k, o in enumerate(ops) if o[0] == "put"); sw = idx(lambda o: o[0] == "switch")
        self.assertTrue(any(o[0] == "sync" and last_put < k < sw for k, o in enumerate(ops)))   # the copy persistent before the switch
        rb = idx(lambda o: o[0] == "readback"); self.assertTrue(last_put < rb < sw)
        mrec = idx(lambda o: o[0] == "record-module"); mod = idx(lambda o: o[0] == "module")
        self.assertTrue(any(o[0] == "sync" and sw < k < mrec for k, o in enumerate(ops)) and any(o[0] == "sync" and mrec < k < mod for k, o in enumerate(ops)))
        inst = idx(lambda o: o[0] == "installed"); self.assertTrue(any(o[0] == "sync" and mod < k < inst for k, o in enumerate(ops)))

    def test_32_a_power_loss_at_any_change_never_leaves_an_incomplete_copy_to_start(self):
        earlier = make_package(os.path.join(self.t, "pkg-e"), tag="e"); ep = CI.build_plan(earlier); seen = set(); n = 0
        while True:
            n += 1; shutil.rmtree(self.root); CI.make_stand_in(self.root, "hakchi-ready")
            self.assertTrue(CI.Installer(self.con(), ep, earlier, self.jnew()).run()["ok"]); first = boot_state(self.root)
            c = self.con(durable=True, crash_after=n); j = self.jnew()
            try: r = CI.Installer(c, self.plan, self.pkg, j).run(); finished = True
            except SystemExit: finished = False
            if finished: break
            c.power_cut(); st = boot_state(self.root); seen.add(st.split(":")[0])
            self.assertNotIn("INCOMPLETE", st, (n, st)); self.assertNotEqual(st, "MODULE-PARTIAL", (n, st)); self.assertIn(st, (first, f"ccs:{self.plan['version']}.{json.loads(rd(j).splitlines()[0])['attempt']}"), (n, st))
            rr = CI.Installer(self.con(), self.plan, self.pkg, j).resume(); self.assertIn(rr["result"], ("rolled-back", "installed", "nothing to resume"), (n, rr))
            self.assertNotIn("INCOMPLETE", boot_state(self.root)); self.assertTrue(n < 80)
        self.assertTrue(r["ok"]); self.assertGreater(n, 15)

    def test_33_a_journal_cut_short_is_read_up_to_its_last_whole_line(self):
        j = self.jnew()
        with self.assertRaises(SystemExit): CI.Installer(self.con(crash_after=8), self.plan, self.pkg, j).run()
        with open(j, "ab") as f: f.write(b'{"t": 1, "attempt": "abcd', )                    # the computer stopped while writing a line
        r = CI.Installer(self.con(), self.plan, self.pkg, j).resume(); self.assertEqual(r["result"], "rolled-back", r)
        j2 = self.jnew()
        with self.assertRaises(SystemExit): CI.Installer(self.con(crash_after=8), self.plan, self.pkg, j2).run()
        lines = rd(j2).split(b"\n"); lines[3] = lines[3][:10]; wr(j2, b"\n".join(lines)); before = tree(self.root)
        r = CI.Installer(self.con(), self.plan, self.pkg, j2).resume(); self.assertEqual(r["result"], "unknown", r); self.assertIn("damaged", r["why"]); self.assertEqual(changes(before, tree(self.root)), [])

    def test_34_a_lost_answer_stops_a_real_style_operation_and_its_recovery_is_separate(self):
        for n in (5, 30):
            with self.subTest(n):
                shutil.rmtree(self.root); CI.make_stand_in(self.root, "hakchi-ready"); before = tree(self.root)
                inst = CI.Installer(self.con(after=n), self.plan, self.pkg, self.jnew(), policy="stop"); r = inst.run()
                if r.get("ok"): continue
                self.assertEqual(r["result"], "stopped"); self.assertIsNone(r["rolled_back"]); self.assertIn("own approved operation", r["next"])
                self.assertNotIn("INCOMPLETE", boot_state(self.root))
                rr = CI.Installer(self.con(), self.plan, self.pkg, inst.journal, device="sim-0001").resume(); self.assertEqual(rr["result"], "rolled-back", rr)
                self.assertEqual(changes(before, tree(self.root)), [])

# ---------------------------------------------------------------- the saves export
class Export(Base):
    def setUp(self):
        super().setUp(); self.assertTrue(CI.Installer(self.con(), self.plan, self.pkg, self.jnew()).run()["ok"])
        self.files = {"saves/ab/below.ccsstate": os.urandom(999_999), "saves/ab/above.ccsstate": os.urandom(1_000_001), "saves/ab/largest.ccsstate": os.urandom(8 * 1048576 + 65536),
                      "saves/ab/cart.sav": os.urandom(8192), "saves/ab/exact.ccsstate": os.urandom(262144 * 2), "game-settings/ab.json": b"{}"}
        for rel, b in self.files.items(): wr(os.path.join(self.root, "var/lib/ccs/data", *rel.split("/")), b)

    def test_40_every_supported_size_is_copied_in_bounded_pieces_and_verified(self):
        out = os.path.join(self.t, "x"); c = self.con(); r = CI.export_data(c, out); self.assertTrue(r["ok"], r); self.assertEqual(r["status"], "complete")
        self.assertEqual(r["files"], len(self.files) + 1)                              # + IDENTITY.json
        for rel, b in self.files.items(): self.assertEqual(rd(os.path.join(out, "data", *rel.split("/"))), b)
        self.assertGreaterEqual(c.nreads, 33 + 4)                                       # the largest alone took 33 pieces of 262,144 bytes
        man = json.loads(rd(os.path.join(out, "EXPORT.json"))); self.assertEqual(man["status"], "complete"); self.assertNotIn("hwid", json.dumps(man["console"]))

    def test_41_an_interrupted_export_says_so_and_keeps_only_whole_verified_files(self):
        out = os.path.join(self.t, "x"); r = CI.export_data(self.con(read_fail_at=12), out); self.assertFalse(r["ok"]); self.assertEqual(r["status"], "incomplete")
        self.assertTrue(r["interrupted"]); self.assertTrue(r["verified"]["ok"], r["verified"])
        self.assertFalse([n for d, _, ns in os.walk(out) for n in ns if n.endswith(".part")])

    def test_42_a_file_changed_while_it_was_read_is_named_not_copied(self):
        out = os.path.join(self.t, "x"); r = CI.export_data(self.con(grow_on_read=2), out); self.assertEqual(r["status"], "partial", r)
        bad = [s for s in r["skipped"] if "changed while it was read" in s["why"]]; self.assertEqual(len(bad), 1, r["skipped"])
        self.assertFalse(os.path.exists(os.path.join(out, "data", *bad[0]["path"].split("/"))))

    def test_43_too_large_and_empty(self):
        r = CI.export_data(self.con(), os.path.join(self.t, "x"), max_file=1_000_000); self.assertEqual(r["status"], "partial"); self.assertEqual(len(r["skipped"]), 2)
        self.assertTrue(all("largest file this export copies" in s["why"] for s in r["skipped"]))
        root2 = os.path.join(self.t, "c2"); CI.make_stand_in(root2, "hakchi-ready"); r = CI.export_data(CI.FakeConsole(root2), os.path.join(self.t, "y"))
        self.assertTrue(r["ok"]); self.assertEqual(r["status"], "empty"); self.assertEqual(r["files"], 0)

# ---------------------------------------------------------------- inspection and verification
class Inspect(Base):
    def test_50_inspect_and_verify_read_what_the_console_starts(self):
        i = CI.inspect_console(self.con()); self.assertTrue(i["ok"]); self.assertEqual(i["module"], "absent"); self.assertIsNone(i["in_use"])
        a = CI.Installer(self.con(), self.plan, self.pkg, self.jnew()).run(); i = CI.inspect_console(self.con()); self.assertEqual(i["module"], "current"); self.assertEqual(i["in_use"]["copy"], a["copy"])
        self.assertTrue(CI.verify_installed(self.con(), self.plan)["verified"])
        wr(os.path.join(self.root, "var/lib/ccs/pkg", a["copy"], "session.json"), b"x"); v = CI.verify_installed(self.con(), self.plan); self.assertFalse(v["verified"]); self.assertEqual(v["mismatches"], ["session.json"])
        self.assertFalse(CI.verify_installed(self.con(), self.plan, device="sim-OTHER")["ok"])

if __name__ == "__main__": unittest.main(verbosity=2)
