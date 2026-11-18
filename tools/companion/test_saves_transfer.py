"""Game Saves export/import in the Windows app (saves_transfer.py), with real files in throwaway folders:
  export         only saves the player would load leave; no ROM, art or settings; the manifest lists every file (size, SHA-256, slot name,
                 save time) and the file reads back
  plan           new / identical / different saves are told apart; nothing is written; a different-size cartridge save is refused
  import         only what the person chose is written: an occupied slot is replaced only by an explicit choice (its original kept in
                 replaced-<tx>), a save can go to an empty slot with its name, last-used follows only a save in its own slot
  refusals       not a ZIP, no manifest, a member not listed / listed but missing / duplicate / unsafe name / changed bytes, a save of another
                 game, another platform, a game window open, an unfinished earlier restore, a file changed since it was looked at
  read errors    (Codex v1p4 F3) no saves folder or an empty one means no saves; a saves folder, a game's folder or a save file that cannot
                 be read is a read error (never "no saved games"): nothing written, a file already at the chosen place unchanged, and the
                 same export succeeds once the folder can be read again
  python tools/companion/test_saves_transfer.py"""
from __future__ import annotations
import hashlib, json, os, shutil, sys, tempfile, unittest, zipfile, zlib
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import threading, types
# the game player's lock and "is a game window open" as the app's play.py provides them (studio_backup.player_state imports play)
PLAY = types.ModuleType("play"); PLAY.RUN = types.SimpleNamespace(lock=threading.RLock()); PLAY.OPEN = False; PLAY._running = lambda: PLAY.OPEN; sys.modules["play"] = PLAY
import saves_transfer as st, studio_backup as sb

A = hashlib.sha256(b"game a").hexdigest(); B = hashlib.sha256(b"game b").hexdigest()

def state(sha, payload=b"\x01" * 64, platform="web-wasm32", created="2026-11-13T10:00:00Z"):
    h = json.dumps({"schema": "ccs-save-state/1", "kind": "manual", "rom_content_sha256": sha, "title": "T", "core": "fceumm", "core_version": "ac0d00a7",
                    "platform": platform, "size": len(payload), "crc32": f"{zlib.crc32(payload) & 0xFFFFFFFF:08x}", "frames": 1, "created": created}).encode()
    return b"CCSSTATE" + len(h).to_bytes(4, "little") + h + payload

def names(sha, n): return json.dumps({"schema": "ccs-slot-names/1", "rom_content_sha256": sha, "names": n}).encode()

class Data:
    def __init__(self): self.root = tempfile.mkdtemp(prefix="ccs-saves-")
    def __call__(self, *p): return os.path.join(self.root, *p)
    def put(self, sha, name, b):
        os.makedirs(self("saves", sha), exist_ok=True); open(self("saves", sha, name), "wb").write(b)
    def get(self, sha, name):
        p = self("saves", sha, name); return open(p, "rb").read() if os.path.isfile(p) else None

class T(unittest.TestCase):
    def setUp(self):
        self.src = Data(); self.dst = Data(); self.tmp = tempfile.mkdtemp(prefix="ccs-savesf-")
        s = self.src
        s.put(A, "cart.sav", b"\xAA" * 8192); s.put(A, "slot1.ccsstate", state(A, b"\x11" * 64, created="2026-11-10T09:00:00Z")); s.put(A, "slot2.ccsstate", state(A, b"\x22" * 64))
        s.put(A, "slot-names.json", names(A, {"1": "Castle", "2": "Boss"})); s.put(A, "game.json", json.dumps({"schema": "ccs-save-game/1", "rom_content_sha256": A, "last_title": "Game A"}).encode())
        s.put(A, "last-used.json", json.dumps({"schema": "ccs-last-used/1", "rom_content_sha256": A, "slot": 2}).encode())
        s.put(A, "slot3.ccsstate", state(A, platform="linux-armv7"))   # the console's platform: not part of a transfer
        s.put(B, "slot1.ccsstate", state(B, b"\x33" * 64))
        os.makedirs(s("saves", "notasha"), exist_ok=True); open(s("saves", "notasha", "cart.sav"), "wb").write(b"x")
        self.file = os.path.join(self.tmp, "saves.zip")
    def tearDown(self):
        for d in (self.src.root, self.dst.root, self.tmp): shutil.rmtree(d, ignore_errors=True)

    def export(self):
        r = st.export(self.src, self.file, None, "0.12.0"); self.assertTrue(r["ok"], r); return r

    def test_1_export(self):
        r = self.export(); self.assertEqual((r["games"], r["files"]), (2, 7)); self.assertEqual([x["name"] for x in r["skipped"]], ["slot3.ccsstate"])
        with zipfile.ZipFile(self.file) as z:
            n = sorted(z.namelist()); self.assertEqual(n[0], st.MANIFEST); self.assertTrue(all(st.MEMBER.match(x) for x in n[1:]))
            self.assertFalse([x for x in n if x.endswith((".nes", ".unh", ".png", ".jpg", ".json")) and "/" not in x and x != st.MANIFEST])
            m = json.loads(z.read(st.MANIFEST)); self.assertEqual(m["schema"], "ccs-game-saves/1"); self.assertIn("No game files are included", m["note"])
        ga = next(g for g in m["games"] if g["content_sha256"] == A); s1 = next(f for f in ga["files"] if f["name"] == "slot1.ccsstate")
        self.assertEqual((ga["title"], s1["slot_name"], s1["saved_at"], s1["kind"]), ("Game A", "Castle", "2026-11-10T09:00:00Z", "slot")); self.assertIn("modified", s1)
        self.assertEqual(st.read(self.file)["manifest"], m)

    def test_2_plan_and_import(self):
        self.export(); d = self.dst
        d.put(A, "slot1.ccsstate", state(A, b"\x99" * 64)); d.put(A, "cart.sav", b"\xBB" * 8192); d.put(A, "slot-names.json", names(A, {"1": "Mine"}))
        p = st.plan(d, self.file, lambda sha: "here" if sha == A else "missing")
        ga = next(g for g in p["games"] if g["sha"] == A); stt = {f["name"]: f["status"] for f in ga["files"]}
        self.assertEqual(stt, {"cart.sav": "conflict", "slot1.ccsstate": "conflict", "slot2.ccsstate": "add"}); self.assertEqual(ga["free_slots"], [2, 3, 4])
        self.assertEqual(next(f for f in ga["files"] if f["name"] == "slot1.ccsstate")["current"]["slot_name"], "Mine")
        self.assertEqual(next(g for g in p["games"] if g["sha"] == B)["rom"], "missing"); self.assertIsNone(d.get(A, "slot2.ccsstate"), "planning writes nothing")
        # defaults: differing saves are kept; new ones are added
        r = st.apply(d, self.file, p["token"], {}); self.assertTrue(r["ok"], r)
        self.assertEqual(d.get(A, "slot1.ccsstate"), state(A, b"\x99" * 64)); self.assertEqual(d.get(A, "cart.sav"), b"\xBB" * 8192)
        self.assertEqual(d.get(A, "slot2.ccsstate"), state(A, b"\x22" * 64)); self.assertEqual(json.loads(d.get(A, "slot-names.json"))["names"], {"1": "Mine", "2": "Boss"})
        self.assertIsNotNone(d.get(B, "slot1.ccsstate"), "a game whose ROM is not here keeps its saves (nothing joins a collection)")
        self.assertIsNotNone(d.get(A, "last-used.json"), "last-used names slot 2, and slot 2 took that save")

    def test_3_explicit_choices(self):
        self.export(); d = self.dst
        d.put(A, "slot1.ccsstate", state(A, b"\x99" * 64)); d.put(A, "cart.sav", b"\xBB" * 8192)
        p = st.plan(d, self.file)
        r = st.apply(d, self.file, p["token"], {A: {"slot1.ccsstate": "slot3", "cart.sav": "replace", "slot2.ccsstate": "slot2"}}); self.assertTrue(r["ok"], r)
        self.assertEqual(d.get(A, "slot1.ccsstate"), state(A, b"\x99" * 64), "the occupied slot is untouched")
        self.assertEqual(d.get(A, "slot3.ccsstate"), state(A, b"\x11" * 64, created="2026-11-10T09:00:00Z")); self.assertEqual(json.loads(d.get(A, "slot-names.json"))["names"], {"2": "Boss", "3": "Castle"})
        self.assertEqual(d.get(A, "cart.sav"), b"\xAA" * 8192); kept = d("saves", A, r["replaced_folder"], "cart.sav")
        self.assertEqual(open(kept, "rb").read(), b"\xBB" * 8192, "the replaced cartridge save is kept first")
        self.assertIsNotNone(d.get(A, "last-used.json"), "the last used save went into its own slot (2)")
        # replace by explicit choice
        p = st.plan(d, self.file); r = st.apply(d, self.file, p["token"], {A: {"slot1.ccsstate": "replace"}}); self.assertTrue(r["ok"], r)
        self.assertEqual(d.get(A, "slot1.ccsstate"), state(A, b"\x11" * 64, created="2026-11-10T09:00:00Z")); self.assertTrue(os.path.isfile(d("saves", A, r["replaced_folder"], "slot1.ccsstate")))
        # a choice that is no longer possible stops everything
        p = st.plan(d, self.file); r = st.apply(d, self.file, p["token"], {A: {"slot2.ccsstate": "slot1"}}); self.assertFalse(r["ok"]); self.assertTrue(r["nothing_changed"])

    def test_4_refusals_before_any_write(self):
        self.export(); d = self.dst
        def bad(mut, why):
            f = os.path.join(self.tmp, "bad.zip")
            with zipfile.ZipFile(self.file) as z, zipfile.ZipFile(f, "w") as o: mut(z, o)
            with self.assertRaises(st.SavesError, msg=why) as c: st.plan(d, f)
            self.assertFalse(os.path.exists(d("saves")), why); return str(c.exception)
        man = json.loads(zipfile.ZipFile(self.file).read(st.MANIFEST))
        def copy(z, o, skip=(), extra=()):
            for n in z.namelist():
                if n not in skip: o.writestr(n, z.read(n))
            for n, b in extra: o.writestr(n, b)
        bad(lambda z, o: copy(z, o, extra=[(f"saves/{A}/../x.sav", b"1")]), "unsafe name")
        bad(lambda z, o: copy(z, o, extra=[(f"saves/{A}/slot4.ccsstate", state(A))]), "a member not in the manifest")
        bad(lambda z, o: copy(z, o, skip=[f"saves/{A}/slot2.ccsstate"]), "a listed member missing")
        bad(lambda z, o: copy(z, o, skip=[st.MANIFEST]), "no manifest")
        bad(lambda z, o: copy(z, o, extra=[("readme.nes", b"NES\x1a")]), "a ROM file")
        m2 = json.loads(json.dumps(man)); m2["schema"] = "ccs-game-saves/9"
        bad(lambda z, o: (copy(z, o, skip=[st.MANIFEST]), o.writestr(st.MANIFEST, json.dumps(m2))), "unknown schema")
        def changed(z, o):
            for n in z.namelist(): o.writestr(n, z.read(n) if n != f"saves/{A}/cart.sav" else b"\x00" * 8192)
        self.assertIn("check value", bad(changed, "changed bytes"))
        # duplicate member names
        f = os.path.join(self.tmp, "dup.zip")
        import warnings
        with warnings.catch_warnings():
            warnings.simplefilter("ignore")
            with zipfile.ZipFile(self.file) as z, zipfile.ZipFile(f, "w") as o:
                copy(z, o); o.writestr(f"saves/{A}/cart.sav", b"\xAA" * 8192)
        with self.assertRaises(st.SavesError): st.plan(d, f)
        with self.assertRaises(st.SavesError): st.plan(d, os.path.join(HERE, "saves_transfer.py"))   # not a ZIP

    def test_5_saves_of_another_game_or_platform_are_not_used(self):
        f = os.path.join(self.tmp, "x.zip"); wrong = state(B); arm = state(A, platform="linux-armv7")
        man = {"schema": st.SCHEMA, "games": [{"content_sha256": A, "title": "A", "files": [{"name": "slot1.ccsstate", "bytes": len(wrong), "sha256": hashlib.sha256(wrong).hexdigest()},
                                                                                             {"name": "slot2.ccsstate", "bytes": len(arm), "sha256": hashlib.sha256(arm).hexdigest()}]}]}
        with zipfile.ZipFile(f, "w") as z: z.writestr(st.MANIFEST, json.dumps(man)); z.writestr(f"saves/{A}/slot1.ccsstate", wrong); z.writestr(f"saves/{A}/slot2.ccsstate", arm)
        p = st.plan(self.dst, f); self.assertEqual(p["games"], []); self.assertEqual({r["status"] for r in p["rejected"]}, {"invalid", "incompatible"})
        r = st.apply(self.dst, f, p["token"], {A: {"slot1.ccsstate": "slot1", "slot2.ccsstate": "slot2"}}); self.assertTrue(r["ok"]); self.assertFalse(os.path.exists(self.dst("saves", A, "slot1.ccsstate")))

    def test_6_game_open_pending_restore_and_changed_file(self):
        self.export(); d = self.dst; p = st.plan(d, self.file)
        PLAY.OPEN = True
        try: r = st.apply(d, self.file, p["token"], {}); self.assertFalse(r["ok"]); self.assertIn("A game window is open", r["error"]); self.assertFalse(os.path.exists(d("saves")))
        finally: PLAY.OPEN = False
        open(d("restore-20261113-101010-abcdef01.journal.json"), "w").write("{}")
        r = st.apply(d, self.file, p["token"], {}); self.assertFalse(r["ok"]); self.assertIn("earlier restore", r["error"]); os.remove(d("restore-20261113-101010-abcdef01.journal.json"))
        r = st.apply(d, self.file, "0" * 64, {}); self.assertFalse(r["ok"]); self.assertIn("changed since", r["error"]); self.assertFalse(os.path.exists(d("saves")))
        self.assertTrue(st.apply(d, self.file, p["token"], {})["ok"])

    def test_7_a_cartridge_save_of_another_layout(self):
        self.export(); d = self.dst; d.put(A, "cart.sav", b"\xBB" * 2048)
        p = st.plan(d, self.file); self.assertIn("different cartridge layout", " ".join(r["why"] for r in p["rejected"]))
        r = st.apply(d, self.file, p["token"], {A: {"cart.sav": "replace"}}); self.assertFalse(r["ok"]); self.assertEqual(d.get(A, "cart.sav"), b"\xBB" * 2048)

    def test_8_export_read_errors(self):
        from unittest import mock
        out = os.path.join(self.tmp, "existing.zip"); open(out, "wb").write(b"an earlier file")
        def unchanged(): return open(out, "rb").read() == b"an earlier file" and not os.path.exists(out + ".part")
        empty = Data()
        try:
            r = st.export(empty, out); self.assertFalse(r["ok"]); self.assertIn("no saved games", r["error"]); self.assertNotIn("read_error", r); self.assertTrue(unchanged())
            os.makedirs(empty("saves")); r = st.export(empty, out); self.assertIn("no saved games", r["error"]); self.assertTrue(unchanged(), "an empty saves folder: no saves")
        finally: shutil.rmtree(empty.root, ignore_errors=True)
        real_listdir, real_read = os.listdir, sb._read
        def deny(target):
            def f(p):
                if os.path.normcase(os.path.abspath(p)) == os.path.normcase(os.path.abspath(target)): raise PermissionError(13, "Access is denied", p)
                return real_listdir(p)
            return f
        for target, what in ((self.src("saves"), "the saved games folder"), (self.src("saves", A), "a game's saved games")):
            with mock.patch.object(st.os, "listdir", deny(target)): r = st.export(self.src, out)
            self.assertFalse(r["ok"], what); self.assertTrue(r.get("read_error"), r); self.assertNotIn("no saved games", r["error"])
            self.assertIn("could not read " + what, r["error"]); self.assertIn("Access is denied", r["error"]); self.assertTrue(unchanged(), what)
        def bad_read(p):
            if p.endswith("slot2.ccsstate"): raise OSError(5, "Input/output error", p)
            return real_read(p)
        with mock.patch.object(sb, "_read", bad_read): r = st.export(self.src, out)
        self.assertTrue(r.get("read_error"), r); self.assertIn("a saved game (slot2.ccsstate)", r["error"]); self.assertTrue(unchanged())
        r = st.export(self.src, out); self.assertTrue(r["ok"], r); self.assertEqual((r["games"], r["files"]), (2, 7), "Try Again: the same export, complete")
        self.assertEqual([x["name"] for x in r["skipped"]], ["slot3.ccsstate"], "a save the player would not load is still listed apart")

if __name__ == "__main__":
    r = unittest.main(exit=False, verbosity=2).result; print(f"RESULT {r.testsRun - len(r.failures) - len(r.errors)} / {r.testsRun}"); sys.exit(0 if r.wasSuccessful() else 1)
