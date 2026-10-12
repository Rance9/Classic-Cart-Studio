"""Release-safeguard tests for hero key art (offline, temp folders only).
  python -m unittest tools/publish/test_hero_guard.py"""
import os, sys, json, tempfile, shutil, unittest
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import hero_guard as hg
from PIL import Image

def png(p): Image.new("RGB", (16, 9), (1, 2, 3)).save(p)

class HeroGuardTests(unittest.TestCase):
    def setUp(self):
        self.work = tempfile.mkdtemp(prefix="ccs-hero-guard-"); self.art = os.path.join(self.work, "art"); self.hero = os.path.join(self.art, "hero"); os.makedirs(self.hero)
        self.reg = {"games": {"ok1": {"status": "approved", "title": "A"}, "cand": {"status": "candidate"}, "rej": {"status": "rejected"}, "ok2": {"status": "approved"}}}
        json.dump(self.reg, open(os.path.join(self.hero, "index.json"), "w"))
        for gid in ("ok1", "cand", "ok2", "stray"): png(os.path.join(self.hero, gid + ".png"))
        os.makedirs(os.path.join(self.hero, "rejected")); png(os.path.join(self.hero, "rejected", "rej.png"))
        open(os.path.join(self.hero, "notes.txt"), "w").write("x")
        for sub in ("hero-requests", "hero-batch"): os.makedirs(os.path.join(self.art, sub, "g1")); open(os.path.join(self.art, sub, "g1", "brief.md"), "w").write("private")
    def tearDown(self): shutil.rmtree(self.work, ignore_errors=True)
    def test_only_approved_images_survive(self):
        ap = hg.approved_ids(self.reg["games"]); self.assertEqual(ap, {"ok1", "ok2"})
        r = hg.prune_hero_dir(self.hero, ap)
        self.assertEqual(sorted(os.listdir(self.hero)), ["index.json", "ok1.png", "ok2.png"])
        self.assertEqual(sorted(r["kept"]), ["ok1.png", "ok2.png"]); self.assertIn("cand.png", r["removed"]); self.assertIn("stray.png", r["removed"]); self.assertIn("rejected", r["removed"])
        pub = json.load(open(os.path.join(self.hero, "index.json")))["games"]; self.assertEqual(set(pub), {"ok1", "ok2"})
    def test_private_art_folders_are_removed(self):
        hg.prune_private_art(self.art); self.assertEqual(sorted(os.listdir(self.art)), ["hero"])
    def test_catalog_references_to_excluded_images_are_suppressed(self):
        games = [{"game_id": "ok1", "art_hero": "art/hero/ok1.png", "art_hero_status": "approved", "art_hero_sha256": "a"},
                 {"game_id": "cand", "art_hero": "art/hero/cand.png", "art_hero_status": "candidate", "art_hero_source": "x.png"},
                 {"game_id": "rej", "art_hero": "art/hero/rej.png", "art_hero_status": "rejected"},
                 {"game_id": "stale", "art_hero": "art/hero/stale.png", "art_hero_status": "approved"},   # claims approval but is not in the registry
                 {"game_id": "none", "art": "art/none.jpg"}]
        n = hg.suppress_catalog(games, {"ok1", "ok2"}); self.assertEqual(n, 3)
        self.assertEqual(games[0]["art_hero"], "art/hero/ok1.png")
        for g in games[1:4]: self.assertFalse(any(k.startswith("art_hero") for k in g), g)
        self.assertEqual(games[4], {"game_id": "none", "art": "art/none.jpg"})
    def test_no_png_without_registry_record(self):
        hg.prune_hero_dir(self.hero, set()); self.assertEqual(os.listdir(self.hero), ["index.json"])
    def test_real_workspace_state_has_no_approved_art_yet(self):
        reg = hg.load_registry(os.path.join(os.path.dirname(HERE), "..", "reports", "art", "hero", "index.json"))
        self.assertTrue(all(r.get("status") in ("candidate", "approved", "rejected") for r in reg.values()))
        # the public page must not reference any candidate hero: every art_hero in the published catalog must be approved
        site = os.environ.get("CCS_PUBLIC_SITE", r"C:\NES\work\build\site"); cat = os.path.join(site, "catalog.json")
        if os.path.exists(cat):
            games = json.load(open(cat, encoding="utf-8"))["games"]; ap = hg.approved_ids(reg)
            bad = [g["game_id"] for g in games if g.get("art_hero") and g["game_id"] not in ap]
            self.assertEqual(bad, [], "published catalog references unapproved hero art")
            hdir = os.path.join(site, "art", "hero")
            if os.path.isdir(hdir): self.assertEqual([n for n in os.listdir(hdir) if n.endswith(".png") and os.path.splitext(n)[0] not in ap], [])

if __name__ == "__main__": unittest.main()
