r"""Replacing a game Classic Cart Studio prepared for Hakchi2 CE, and undoing a replacement.

The synthetic site and isolated data folders of test_managed_copies, synthetic game files and disposable Hakchi trees; nothing touches
Hakchi2 CE itself, a console or the network. The game files are added the way Add ROM Files adds them (personal.ingest). Cases:
  header replaced   a rebuilt file at the same path with the same content and another header (same game, same Hakchi code) replaces the
                    entry the Studio prepared earlier; going back gives the earlier bytes exactly; preparing unchanged bytes again changes
                    nothing; the chosen file is never changed
  times             a replaced file gets a time at least five seconds after the earlier one's, a file whose bytes did not change keeps its
                    time (Hakchi2 CE's sync compares size and time, three seconds' leeway)
  refusals          no or an unreadable Studio record in the folder, an edited record, another game under the same code, no preparation
                    record, a changed or missing game file, a file the Studio did not write, a folder another program made: refused, and
                    the folder, Hakchi2 CE's settings and the preparation record are byte for byte and time for time as they were
  undo              a stop at each step of a replacement puts everything back exactly, and the next preparation works; an undo that
                    cannot finish says so, and running it again finishes it
  new content       a rebuilt file whose game content changed is another game (another code): a new entry; the earlier one is left as it
                    is and no longer selected
  python test_hakchi_replace.py      Exit 0 = PASS."""
from __future__ import annotations
import glob, hashlib, json, os, shutil, sys, tempfile, unittest
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import test_managed_copies as T   # noqa: E402  its synthetic site and isolated data folders
hb, ps, M4 = T.hb, T.ps, T.M4

def sha(b: bytes) -> str: return hashlib.sha256(b).hexdigest()
def build(flags6: int, seed: str) -> bytes:
    """an iNES file: 32 KB PRG, 8 KB CHR, mapper 4 with battery (flags6 0x42; 0x43 adds vertical mirroring)"""
    return bytes([0x4E, 0x45, 0x53, 0x1A, 2, 1, flags6, 0]) + bytes(8) + T.content(seed, 2 * 16384 + 8192)
A = build(0x42, "rebuilt game"); B = build(0x43, "rebuilt game"); C = build(0x42, "rebuilt game, new content")
BUILDS = os.path.join(T.TMP, "builds"); os.makedirs(BUILDS); FILE = os.path.join(BUILDS, "build output.nes")

def snapshot(*paths) -> dict:
    """every file under the given folders/files: size, SHA-256 and modification time"""
    out = {}
    for p in paths:
        files = [p] if os.path.isfile(p) else [os.path.join(r, f) for r, _, fs in os.walk(p) for f in fs]
        for q in files:
            st = os.stat(q); out[q] = (st.st_size, sha(open(q, "rb").read()), st.st_mtime_ns)
    return out

class Replace(unittest.TestCase):
    def new_tree(self):
        self.tree = tempfile.mkdtemp(dir=T.TMP, prefix="hakchi-"); fref, _ = hb.factory_reference(os.path.join(T.SITE, "builtin.json"))
        os.makedirs(os.path.join(self.tree, "config"))
        with open(os.path.join(self.tree, "config", "config.json"), "w") as f: json.dump({"gamesCollectionSettings": {"NES": {"SelectedGames": [], "OriginalGames": list(fref["codes"])}}}, f)
        self.cfg = {"hakchi_exe": os.path.join(self.tree, "hakchi.exe"), "staging_dir": self.tree + "-staging", "rehearsal_tree": True, "hakchi_args": []}
        self.ledger = os.path.join(self.cfg["staging_dir"], hb.LEDGER_NAME)

    def add(self, b: bytes) -> str:
        """write the build to the same path and add it the way Add ROM Files does; the file is not changed"""
        with open(FILE, "wb") as f: f.write(b)
        r = ps.ingest(FILE); self.assertEqual(open(FILE, "rb").read(), b, "the chosen file is never changed")
        pid = r["entry"]["id"]; self.assertEqual(open(ps.load_index()["entries"][pid]["managed_copy"], "rb").read(), b, "the kept copy is the current file")
        return pid

    def manifest(self, pid: str) -> dict:
        fref, factory = hb.factory_reference(os.path.join(T.SITE, "builtin.json"))
        return {"schema": M4.SCHEMA4, "name": "Replace", "collection_mode": "exclusive", "factory_game_policy": "hide_unselected", "factory_inventory": dict(fref),
               "selections": [M4.selection_from_row(hb.resolve_version(pid), None, "personal", factory)], "personal_library": [{"id": pid}],
               "hidden_factory_games": [{"code": cd, "title": factory[cd]["factory_title"]} for cd in fref["codes"]]}

    def prepare(self, pid: str) -> dict:
        return hb.apply(self.manifest(pid), "replace.json", self.cfg, launch=False, tree_root=self.tree)

    def counted_ready(self, pid: str) -> int:
        """what the preview counts as already in Hakchi2 CE"""
        return hb.already_prepared(hb.plan(self.manifest(pid)), hb.HakchiTree(self.tree))

    def folder(self) -> str:
        ds = [d for d in glob.glob(os.path.join(self.tree, "games", "*")) if os.path.isdir(d)]
        self.assertEqual(len(ds), 1, ds); return ds[0]

    def state(self) -> dict:
        return snapshot(self.tree, self.ledger) if os.path.exists(self.ledger) else snapshot(self.tree)

    def prepared_a_then_added_b(self):
        self.new_tree(); pid = self.add(A); r = self.prepare(pid); self.assertTrue(r["ok"], r["message"]); self.assertEqual(r["prepared"], 1)
        self.assertEqual(self.add(B), pid, "same content, same game"); return pid

    def test_01_header_replaced_and_back(self):
        self.new_tree(); pid = self.add(A); r = self.prepare(pid); self.assertTrue(r["ok"], r["message"])
        d = self.folder(); code = os.path.basename(d); rom = os.path.join(d, code + ".nes"); self.assertEqual(open(rom, "rb").read(), A)
        first = {f: os.stat(os.path.join(d, f)).st_mtime_ns for f in os.listdir(d)}
        self.assertEqual(self.counted_ready(pid), 1); self.assertEqual(self.add(B), pid); self.assertEqual(self.counted_ready(pid), 0, "a changed file is not counted as ready")
        r = self.prepare(pid); self.assertTrue(r["ok"], r["message"]); self.assertEqual((r["prepared"], r["replaced"], r["reused"]), (1, 1, 0)); self.assertTrue(r["verification"]["match"])
        self.assertEqual(self.folder(), d, "the same Hakchi code"); self.assertEqual(sha(open(rom, "rb").read()), sha(B))
        self.assertEqual(open(os.path.join(r["backup_dir"], "backup", "games", code, code + ".nes"), "rb").read(), A, "the earlier entry is kept with the run")
        second = {f: os.stat(os.path.join(d, f)).st_mtime_ns for f in os.listdir(d)}
        self.assertGreaterEqual(second[code + ".nes"] - first[code + ".nes"], hb.SYNC_STEP_NS, "a changed file is at least five seconds later")
        for f in (code + ".desktop", "metadata.json"): self.assertEqual(second[f], first[f], f"{f}: same bytes, same time")
        self.assertEqual(json.load(open(self.ledger, encoding="utf-8"))["managed"][code]["converted_sha256"], sha(B))
        self.assertEqual(self.add(A), pid)
        r = self.prepare(pid); self.assertTrue(r["ok"], r["message"]); self.assertEqual(r["replaced"], 1); self.assertEqual(sha(open(rom, "rb").read()), sha(A), "back to the earlier bytes exactly")
        self.assertGreaterEqual(os.stat(rom).st_mtime_ns - second[code + ".nes"], hb.SYNC_STEP_NS)
        before = self.state(); r = self.prepare(pid); self.assertTrue(r["ok"], r["message"]); self.assertEqual((r["prepared"], r["replaced"], r["reused"]), (0, 0, 1))
        after = self.state(); self.assertEqual({k: v for k, v in after.items() if k != self.ledger}, {k: v for k, v in before.items() if k != self.ledger}, "unchanged bytes: nothing in Hakchi2 CE changes")

    def refused(self, mutate, why: str):
        pid = self.prepared_a_then_added_b(); d = self.folder(); mutate(d)
        before = self.state(); r = self.prepare(pid)
        self.assertFalse(r["ok"]); self.assertIn("cannot replace", r["message"]); self.assertIn(why, r["message"]); self.assertIn("Nothing was changed", r["recovery"])
        self.assertEqual(self.state(), before, "the folder, the settings and the record are as they were")

    def test_02_refusals(self):
        def rec(edit):
            def m(d):
                p = os.path.join(d, hb.MARKER); j = json.load(open(p, encoding="utf-8")); edit(j)
                with open(p, "w", encoding="utf-8") as f: json.dump(j, f)
            return m
        def ledger_without(d):
            j = json.load(open(self.ledger, encoding="utf-8")); del j["managed"][os.path.basename(d)]
            with open(self.ledger, "w", encoding="utf-8") as f: json.dump(j, f)
        def flip(d):
            p = os.path.join(d, os.path.basename(d) + ".nes"); b = bytearray(open(p, "rb").read()); b[100] ^= 1
            with open(p, "wb") as f: f.write(b)
        def garbage(d):
            with open(os.path.join(d, hb.MARKER), "w") as f: f.write("{not json")
        def extra(d):
            with open(os.path.join(d, "notes.txt"), "w") as f: f.write("mine")
        def foreign(d):
            code = os.path.basename(d); shutil.rmtree(d); os.makedirs(d)
            with open(os.path.join(d, code + ".nes"), "wb") as f: f.write(A[:-1] + b"\x00")
            with open(os.path.join(d, code + ".desktop"), "w") as f: f.write("[Desktop Entry]\n")
        cases = [("no record", lambda d: os.remove(os.path.join(d, hb.MARKER)), "did not prepare it"),
                 ("unreadable record", garbage, "cannot be read"),
                 ("edited record", rec(lambda j: j.update(game_id="p_0000000000000000")), "another game"),
                 ("same code, another game", rec(lambda j: j.update(game_id="p_1111111111111111", payload_sha256="1" * 64)), "another game"),
                 ("no preparation record", ledger_without, "does not list it"),
                 ("no preparation record file", lambda d: os.remove(self.ledger), "does not list it"),
                 ("changed game file", flip, "not the one this Studio put there"),
                 ("missing game file", lambda d: os.remove(os.path.join(d, os.path.basename(d) + ".nes")), "game file is missing"),
                 ("a file the Studio did not write", extra, "did not put there"),
                 ("a folder another program made", foreign, "did not prepare it")]
        for name, mutate, why in cases:
            with self.subTest(name): self.refused(mutate, why)

    def test_03_stop_at_each_step_puts_everything_back(self):
        for step in ("before-remove-old", "after-remove-old", "after-place-new", "after-config", "after-ledger"):
            with self.subTest(step):
                pid = self.prepared_a_then_added_b(); before = self.state()
                hb.FAULT_AT = step
                try: r = self.prepare(pid)
                finally: hb.FAULT_AT = None
                self.assertFalse(r["ok"]); self.assertIn(step, r["message"]); self.assertTrue(r["rolled_back"], r.get("message"))
                self.assertIn("Nothing was changed" if step == "before-remove-old" else "put back and checked", r["recovery"])
                self.assertEqual(self.state(), before, f"{step}: as it was, bytes and times")
                r = self.prepare(pid); self.assertTrue(r["ok"], r["message"]); self.assertEqual(open(glob.glob(os.path.join(self.folder(), "*.nes"))[0], "rb").read(), B, "the next preparation works")

    def test_04_undo_that_cannot_finish_says_so(self):
        pid = self.prepared_a_then_added_b(); before = self.state(); code = os.path.basename(self.folder()); real = hb._checkpoint; seen = {}
        def stop(name):
            if name == "after-remove-old":   # the kept copy of the earlier entry is damaged before the undo needs it
                runs = sorted(d for d in glob.glob(os.path.join(self.cfg["staging_dir"], "*")) if os.path.isdir(d)); kept = os.path.join(runs[-1], "backup", "games", code, code + ".nes")
                seen["kept"] = kept; seen["bytes"] = open(kept, "rb").read()
                with open(kept, "wb") as f: f.write(b"damaged")
                raise RuntimeError("stopped on purpose")
        hb._checkpoint = stop
        try: r = self.prepare(pid)
        finally: hb._checkpoint = real
        self.assertFalse(r["ok"]); self.assertFalse(r["rolled_back"]); self.assertIn("UNDO NOT FINISHED", r["message"]); self.assertIn("Do not sync", r["recovery"])
        self.assertNotEqual(self.state(), before, "the result does not claim what did not happen")
        with open(seen["kept"], "wb") as f: f.write(seen["bytes"])
        rb = hb.rollback(r["backup_dir"], hb.HakchiTree(self.tree), self.cfg); self.assertEqual(rb["restored_folders"], [code])
        self.assertEqual(self.state(), before, "running the undo again finishes it")

    def test_05_new_content_is_another_game(self):
        self.new_tree(); pid = self.add(A); r = self.prepare(pid); self.assertTrue(r["ok"], r["message"]); old = self.folder(); kept = snapshot(old)
        pid2 = self.add(C); self.assertNotEqual(pid2, pid, "changed content is another game")
        r = self.prepare(pid2); self.assertTrue(r["ok"], r["message"]); self.assertEqual((r["prepared"], r["replaced"]), (1, 0))
        self.assertEqual(snapshot(old), kept, "the earlier entry is left as it is")
        sel = json.load(open(os.path.join(self.tree, "config", "config.json"), encoding="utf-8"))["gamesCollectionSettings"]["NES"]["SelectedGames"]
        self.assertEqual(len(sel), 1); self.assertNotEqual(sel[0], os.path.basename(old), "only the new entry is selected")

if __name__ == "__main__":
    try: res = unittest.main(verbosity=2, exit=False).result
    finally: shutil.rmtree(T.TMP, ignore_errors=True)
    sys.exit(0 if res.wasSuccessful() else 1)
