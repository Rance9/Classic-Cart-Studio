"""HOME sync contract against the mock console store: per-item favorites, history dedupe, clear generations with stale
peers, folders and games Windows-owned, removed games, idempotence, validation. Also cross-checks the Python rules against
the JavaScript rules in studio/home-model.js by running the same cases through node (the two implementations must agree)."""
from __future__ import annotations
import os, sys, json, tempfile, unittest, subprocess, shutil
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import home_sync as hs
STUDIO = os.path.join(os.path.dirname(HERE), "rom-pipeline", "studio")

def manifest(n=5, folders=True):
    games = [{"id": f"g{i}", "version_id": f"v{i}", "title": f"Game {i}"} for i in range(n)]
    m = {"schema": hs.SCHEMA_MANIFEST, "games": games, "order": [g["id"] for g in games], "folders": [], "map_shortcuts": [None, None], "settings": {}, "theme": "arcade-grid"}
    if folders: m["folders"] = [{"id": "f1", "name": "One", "parent": None, "order": ["g1", "g0"]}, {"id": "f2", "name": "Two", "parent": "f1", "order": ["g2"]}]; m["map_shortcuts"] = ["f1", None]
    return m

class Rules(unittest.TestCase):
    def test_favorites_per_item(self):
        a = {"gen": 2, "items": {"g0": {"on": True, "rev": 1, "at": "2026-10-11T10:00:00Z"}, "g1": {"on": True, "rev": 1, "at": "2026-10-11T10:00:00Z"}}}
        b = {"gen": 1, "items": {"g0": {"on": False, "rev": 2, "at": "2026-10-11T09:00:00Z"}, "g2": {"on": True, "rev": 1, "at": "2026-10-11T08:00:00Z"}}}
        r = hs.reconcile_favorites(a, b); self.assertEqual((r["items"]["g0"]["on"], r["items"]["g1"]["on"], r["items"]["g2"]["on"], r["gen"]), (False, True, True, 2))
        self.assertEqual(hs.reconcile_favorites(b, a), r, "symmetric")
    def test_favorites_equal_rev_later_wins(self):
        a = {"items": {"g0": {"on": True, "rev": 1, "at": "2026-10-11T10:00:00Z"}}}; b = {"items": {"g0": {"on": False, "rev": 1, "at": "2026-10-11T11:00:00Z"}}}
        self.assertFalse(hs.reconcile_favorites(a, b)["items"]["g0"]["on"])
    def test_history_union_dedupe(self):
        a = {"events": [{"id": "e1", "game": "g0", "at": "2026-10-11T10:00:00Z"}, {"id": "e2", "game": "g1", "at": "2026-10-11T11:00:00Z"}], "cleared_gen": 0}
        b = {"events": [{"id": "e2", "game": "g1", "at": "2026-10-11T11:00:00Z"}, {"id": "e3", "game": "g0", "at": "2026-10-11T12:00:00Z"}], "cleared_gen": 0}
        r = hs.reconcile_history(a, b); self.assertEqual([e["id"] for e in r["events"]], ["e1", "e2", "e3"])
    def test_clear_generation_beats_stale_peer(self):
        a = {"events": [{"id": "e9", "game": "g0", "at": "2026-10-11T14:00:00Z"}], "cleared_gen": 1, "cleared_at": "2026-10-11T13:00:00Z"}
        stale = {"events": [{"id": "e1", "game": "g0", "at": "2026-10-11T10:00:00Z"}, {"id": "e5", "game": "g2", "at": "2026-10-11T12:45:00Z"}], "cleared_gen": 0, "cleared_at": None}
        r = hs.reconcile_history(a, stale); self.assertEqual([e["id"] for e in r["events"]], ["e9"]); self.assertEqual(r["cleared_gen"], 1)
        r2 = hs.reconcile_history(stale, a); self.assertEqual(r2, r)
    def test_two_clears_offline_simultaneously(self):
        a = {"events": [{"id": "ea", "game": "g0", "at": "2026-10-11T15:00:00Z"}], "cleared_gen": 1, "cleared_at": "2026-10-11T13:00:00Z"}
        b = {"events": [{"id": "eb", "game": "g1", "at": "2026-10-11T14:30:00Z"}], "cleared_gen": 1, "cleared_at": "2026-10-11T14:00:00Z"}
        r = hs.reconcile_history(a, b); self.assertEqual(r["cleared_at"], "2026-10-11T14:00:00Z"); self.assertEqual([e["id"] for e in r["events"]], ["eb", "ea"])
    def test_validate_manifest(self):
        m = manifest(); self.assertEqual(hs.validate_manifest(m), [])
        bad = manifest(); bad["folders"][1]["order"] = ["nope"]; bad["map_shortcuts"] = ["zz", None]; bad["order"].append("g0")
        p = hs.validate_manifest(bad); self.assertTrue(any("not installed" in x for x in p) and any("missing folder" in x for x in p) and any("exactly once" in x for x in p))
        deep = manifest(folders=False); prev = None
        for i in range(6): deep["folders"].append({"id": f"d{i}", "name": f"D{i}", "parent": prev, "order": []}); prev = f"d{i}"
        self.assertTrue(any("deeper than 5" in x for x in hs.validate_manifest(deep)))
        self.assertEqual(hs.validate_manifest({"schema": "x"}), [f"schema is not {hs.SCHEMA_MANIFEST}"])

class MockConsole(unittest.TestCase):
    def setUp(self): self.dir = tempfile.mkdtemp(prefix="ccs-home-sync-"); self.store = hs.MockConsoleStore(os.path.join(self.dir, "mock-console.json"))
    def tearDown(self): shutil.rmtree(self.dir, ignore_errors=True)
    def test_first_sync_pushes_folders_and_is_idempotent(self):
        m = manifest(); st = {"favorites": {"gen": 1, "items": {"g3": {"on": True, "rev": 1, "at": "2026-10-11T10:00:00Z"}}}, "history": {"events": [], "cleared_gen": 0, "cleared_at": None}}
        r1 = hs.sync(self.store, m, st); self.assertTrue(r1["ok"] and r1["simulation"]); doc = self.store.load(); self.assertEqual(len(doc["manifest"]["folders"]), 2); self.assertTrue(doc["state"]["favorites"]["items"]["g3"]["on"])
        r2 = hs.sync(self.store, m, r1["state"]); self.assertFalse(r2["changed"]); self.assertEqual(r2["state"], r1["state"])
    def test_console_edits_merge_both_directions(self):
        m = manifest(); r = hs.sync(self.store, m, {"favorites": {"gen": 0, "items": {}}, "history": {"events": [], "cleared_gen": 0, "cleared_at": None}})
        self.store.console_favorite("g2", True, "2026-10-11T11:00:00Z"); self.store.console_launch("g1", "2026-10-11T11:05:00Z", "ev:c1"); self.store.console_launch("g1", "2026-10-11T11:30:00Z", "ev:c2")
        win = {"favorites": {"gen": 1, "items": {"g4": {"on": True, "rev": 1, "at": "2026-10-11T10:30:00Z"}}}, "history": {"events": [{"id": "ev:c1", "game": "g1", "at": "2026-10-11T11:05:00Z"}], "cleared_gen": 0, "cleared_at": None}}
        r = hs.sync(self.store, m, win); fav = r["state"]["favorites"]["items"]; self.assertTrue(fav["g2"]["on"] and fav["g4"]["on"]); self.assertEqual([e["id"] for e in r["state"]["history"]["events"]], ["ev:c1", "ev:c2"], "offline launch kept once")
    def test_clear_on_windows_propagates_and_new_console_launch_survives(self):
        m = manifest(); hs.sync(self.store, m, {}); self.store.console_launch("g0", "2026-10-11T09:00:00Z", "ev:old")
        win = {"favorites": {"gen": 0, "items": {}}, "history": {"events": [], "cleared_gen": 1, "cleared_at": "2026-10-11T10:00:00Z"}}
        self.store.console_launch("g2", "2026-10-11T10:30:00Z", "ev:new")
        r = hs.sync(self.store, m, win); self.assertEqual([e["id"] for e in r["state"]["history"]["events"]], ["ev:new"]); self.assertEqual(self.store.load()["state"]["history"]["cleared_gen"], 1)
        again = hs.sync(self.store, m, {"history": {"events": [{"id": "ev:old", "game": "g0", "at": "2026-10-11T09:00:00Z"}], "cleared_gen": 0, "cleared_at": None}}); self.assertEqual([e["id"] for e in again["state"]["history"]["events"]], ["ev:new"], "a stale Windows snapshot cannot resurrect")
    def test_clear_on_console_propagates(self):
        m = manifest(); hs.sync(self.store, m, {"history": {"events": [{"id": "e1", "game": "g0", "at": "2026-10-11T09:00:00Z"}], "cleared_gen": 0, "cleared_at": None}})
        self.store.console_clear_history("2026-10-11T12:00:00Z"); r = hs.sync(self.store, m, {"history": {"events": [{"id": "e1", "game": "g0", "at": "2026-10-11T09:00:00Z"}], "cleared_gen": 0, "cleared_at": None}})
        self.assertEqual(r["state"]["history"]["events"], []); self.assertEqual(r["state"]["history"]["cleared_gen"], 1)
    def test_removed_game_not_favorite_history_stays(self):
        m = manifest(); hs.sync(self.store, m, {"favorites": {"gen": 1, "items": {"g4": {"on": True, "rev": 1, "at": "2026-10-11T10:00:00Z"}}}, "history": {"events": [{"id": "e4", "game": "g4", "at": "2026-10-11T10:00:00Z"}], "cleared_gen": 0, "cleared_at": None}})
        m2 = manifest(4); r = hs.sync(self.store, m2, self.store.load()["state"]); self.assertNotIn("g4", r["state"]["favorites"]["items"]); self.assertEqual(r["state"]["history"]["events"][0]["game"], "g4"); self.assertNotIn("g4", self.store.load()["manifest"]["order"])
    def test_folders_are_windows_owned(self):
        m = manifest(); hs.sync(self.store, m, {}); doc = self.store.load(); doc["manifest"]["folders"][0]["name"] = "Edited on console"; self.store.save(doc)
        hs.sync(self.store, m, self.store.load()["state"]); self.assertEqual(self.store.load()["manifest"]["folders"][0]["name"], "One")
    def test_invalid_manifest_refused_without_writing(self):
        bad = manifest(); bad["order"] = []; r = hs.sync(self.store, bad, {}); self.assertFalse(r["ok"]); self.assertFalse(os.path.exists(self.store.path))

class CrossCheckWithJavaScript(unittest.TestCase):
    """the JavaScript model (home-model.js) and this module must reconcile identically"""
    def test_same_results_as_home_model_js(self):
        node = shutil.which("node")
        if not node: self.skipTest("node not installed")
        a = {"favorites": {"gen": 2, "items": {"g0": {"on": True, "rev": 1, "at": "2026-10-11T10:00:00Z"}, "g1": {"on": True, "rev": 3, "at": "2026-10-11T10:00:00Z"}}}, "history": {"events": [{"id": "e1", "game": "g0", "at": "2026-10-11T10:00:00Z"}, {"id": "e9", "game": "g1", "at": "2026-10-11T14:00:00Z"}], "cleared_gen": 1, "cleared_at": "2026-10-11T13:00:00Z"}}
        b = {"favorites": {"gen": 1, "items": {"g0": {"on": False, "rev": 2, "at": "2026-10-11T09:00:00Z"}, "g1": {"on": False, "rev": 3, "at": "2026-10-11T12:00:00Z"}}}, "history": {"events": [{"id": "e1", "game": "g0", "at": "2026-10-11T10:00:00Z"}, {"id": "e5", "game": "g2", "at": "2026-10-11T12:45:00Z"}, {"id": "e7", "game": "g2", "at": "2026-10-11T15:00:00Z"}], "cleared_gen": 0, "cleared_at": None}}
        py = hs.reconcile_state(a, b); py.pop("schema")
        script = "const vm=require('vm'),fs=require('fs');const c={module:{exports:{}},console};vm.createContext(c);vm.runInContext(fs.readFileSync(%s,'utf8'),c);const H=c.module.exports;const r=H.homeReconcile(%s,%s);process.stdout.write(JSON.stringify(r));" % (json.dumps(os.path.join(STUDIO, "home-model.js")), json.dumps(a), json.dumps(b))
        out = subprocess.run([node, "-e", script], capture_output=True, text=True, timeout=30); self.assertEqual(out.returncode, 0, out.stderr[-300:]); js = json.loads(out.stdout)
        norm = lambda d: json.dumps({"favorites": {"gen": d["favorites"]["gen"], "items": d["favorites"]["items"]}, "history": {"events": [(e["id"], e["game"], e["at"], e.get("gen")) for e in d["history"]["events"]], "cleared_gen": d["history"]["cleared_gen"], "cleared_at": d["history"]["cleared_at"]}}, sort_keys=True)
        self.assertEqual(norm(py), norm(js))

if __name__ == "__main__": unittest.main(verbosity=2)
