"""Collection adapter suite: a synthetic nes-classic-collection/4 manifest (the Studio's own export schema) projects to a
ccs-sim-fixture/1 the core accepts, with identities, exact versions, hidden factory titles, readiness levels and ordering
preserved; malformed selections are passed through for the core to reject (never silently dropped)."""
from __future__ import annotations
import json, os, sys, unittest
HERE = os.path.dirname(os.path.abspath(__file__)); SIM = os.path.dirname(HERE); sys.path.insert(0, os.path.join(SIM, "host")); sys.path.insert(0, os.path.join(SIM, "adapter"))
from ccs_projection import project
from runner import Core, ARTIFACTS
PATH = os.environ.get("CCS_SIM_TEST_PATH", "windows"); ART = os.path.join(ARTIFACTS, "tests", PATH); os.makedirs(ART, exist_ok=True)

def manifest(prepared=False):
    sel = lambda **k: {"title_id": k.get("title_id", "t:" + k["game_id"]), "version_id": k["game_id"], "game_id": k["game_id"], "payload_sha256": k.get("sha"), "title": k["title"], "display_title": k["title"], "nointro_name": k.get("ver", ""), "source": k.get("source", "catalog"),
                       "builtin_code": k.get("code"), "builtin_status": k.get("status", ""), "install_required": k.get("install", True), "revision_decision": k.get("dec", "")}
    return {"schema": "nes-classic-collection/4", "name": "Owner test collection", "selections": [
        sel(game_id="g-smb", title="Super Game One", code="CLV-P-NAAAE", status="BUILT-IN: EXACT MATCH", install=False),
        sel(game_id="g-alt", title="Game Two", code="CLV-P-NAABE", status="BUILT-IN: DIFFERENT REVISION", install=True, dec="alternate", sha="a" * 64, ver="Game Two (USA) (Rev 1)"),
        sel(game_id="g-add", title="The Added Game", sha="b" * 64, ver="Added Game (USA)"),
        sel(game_id="g-per", title="My Personal Game", source="personal", sha="c" * 64),
        sel(game_id="g-dup", title="Dup", sha="d" * 64), sel(game_id="g-dup", title="Dup again", sha="e" * 64),
        {"title": "no id at all", "display_title": "no id at all"}],
        "hidden_factory_games": [{"code": "CLV-P-NAACE", "title": "Hidden Factory Three"}, {"code": "CLV-P-NAADE", "title": "Hidden Factory Four"}],
        "factory_inventory": {"sha256": "f" * 64, "count": 30}, "external_dependencies": [{"content_sha256": "b" * 64, "status": "available"}, {"content_sha256": "c" * 64, "status": "missing"}],
        "status": {"prepared_on_pc": "2026-10-12T00:00:00Z" if prepared else None}}

class Adapter(unittest.TestCase):
    def test_projection_fields_and_levels(self):
        fx = project(manifest(prepared=False)); self.assertEqual(fx["schema"], "ccs-sim-fixture/1"); self.assertEqual(fx["projection"]["readiness_level"], "unknown")
        by = {e["id"]: e for e in fx["entries"] if e.get("id")}
        self.assertEqual(by["g-smb"]["kind"], "factory-visible"); self.assertEqual(by["g-smb"]["ready"], "n/a")
        self.assertEqual(by["g-alt"]["kind"], "alternate"); self.assertEqual(by["g-alt"]["version_id"], "a" * 64)
        self.assertEqual(by["g-add"]["kind"], "added"); self.assertEqual(by["g-add"]["ready"], "unknown", "website export: readiness unknown, never guessed")
        self.assertEqual(by["g-per"]["kind"], "personal"); self.assertEqual(by["factory:CLV-P-NAACE"]["kind"], "factory-hidden"); self.assertFalse(by["factory:CLV-P-NAACE"]["visible"])
        self.assertEqual(by["g-add"]["sort_key"], "added game", "leading The ignored, as Preview on TV does")
    def test_desktop_readiness_known(self):
        by = {e["id"]: e for e in project(manifest(prepared=True))["entries"] if e.get("id")}
        self.assertEqual(by["g-add"]["ready"], "ready"); self.assertEqual(by["g-per"]["ready"], "missing"); self.assertEqual(by["g-alt"]["ready"], "unknown", "no dependency record: unknown, not invented")
    def test_round_trip_through_core_rejects_only_the_malformed(self):
        fx = project(manifest(prepared=True)); p = os.path.join(ART, "projected.json"); json.dump(fx, open(p, "w", encoding="utf-8"), ensure_ascii=False)
        c = Core(PATH, timeout=30); c.ready(60); r = c.load(p); rej = [e for e in c.drain(0.3) if e["ev"] == "record_rejected"]; c.send("menu"); menu = [e for e in c.drain(0.3) if e["ev"] == "menu_entry"]; c.shutdown()
        self.assertEqual(r["ev"], "fixture_loaded"); self.assertEqual(r["rejected"], 2); self.assertEqual(sorted(e["reason"][:12] for e in rej), ["duplicate id", "missing or e"])
        self.assertEqual([e["id"] for e in menu], ["g-add", "g-dup", "g-alt", "g-per", "g-smb"], "title order: Added, Dup, Game Two, My Personal, Super")
        self.assertEqual(r["menu"], 5); self.assertEqual(r["entries"], 7, "5 visible + 2 hidden factory titles")
    def test_deterministic_output(self):
        a, b = project(manifest()), project(manifest()); self.assertEqual(json.dumps(a, sort_keys=True), json.dumps(b, sort_keys=True)); self.assertEqual(a["projection"]["entries_sha256"], b["projection"]["entries_sha256"])
    def test_wrong_schema_refused(self):
        with self.assertRaises(ValueError): project({"schema": "nes-classic-selection/3", "selections": []})

if __name__ == "__main__": unittest.main(verbosity=2)
