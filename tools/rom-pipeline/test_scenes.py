"""D-131/D-163: the five shared HOME scenes (static human pixel art, Luis Zuno) replace every per-game background. Checks, from the files and two small runs of the real code:
  rule           every catalog genre has a scene; a missing, unknown, imported or personal genre gets the fallback; the definition is valid
  agreement      the website's homeScene() (home-model.js, run in Node) and the packages' scene_for() (native_home/scenes.py) choose the same
                 scene and image for every catalog game and the edge cases
  old profiles   a HOME model stored by an earlier version (games carrying hero / hero_title of a retired background) shows the scene of the
                 game's genre: the retired picture is never drawn (home-themes.js hmKeyArt, run in Node)
  retired        no per-game background anywhere in the published inputs: no art/hero folder, no art_hero field in the catalog, no
                 universal fallback picture, no theme reference picture, no path to them in the page code, the builds or the packages
  publishable    each scene image is the one make_scenes.py composed from the artist's layer files (1920 x 1080 PNG, SHA-256 recorded, no
                 metadata, each layer's checksum recorded, a whole-number nearest-neighbor enlargement: every s x s block is one color); its
                 credit names the artist, the license (CC BY 3.0 or CC0) and the changes; a missing or different image fails
  python tools/rom-pipeline/test_scenes.py"""
from __future__ import annotations
import json, os, re, shutil, subprocess, sys, unittest
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(REPO, "tools", "companion")); sys.path.insert(0, HERE)
from native_home import scenes as SC
SD = os.path.join(REPO, "site-data"); STUDIO = os.path.join(HERE, "studio")
def rd(p): return open(p, encoding="utf-8").read()
CAT = json.load(open(os.path.join(SD, "catalog.json"), encoding="utf-8"))["games"]
DEF = json.load(open(os.path.join(SD, "art", "scenes", "index.json"), encoding="utf-8"))
EDGE = [None, "", "Unknown Genre", "platformer", "Shooter (light gun)"]   # no genre, empty, unlisted, wrong case, the one with brackets

def node(prog: str):
    return json.loads(subprocess.run([shutil.which("node"), "-"], input=prog, capture_output=True, text=True, check=True).stdout)

def js_scenes(genres):   # the website's own homeScene(), with the HOME_SCENES the page embeds (build_html.py _home_scenes)
    import build_html as BH
    src = rd(os.path.join(STUDIO, "home-model.js")); fn = src[src.index("function homeScene(genre)"):]; fn = fn[:fn.index("\n", fn.index("return s?"))]
    return node(f"const HOME_SCENES={BH._home_scenes()};\n{fn}\nprocess.stdout.write(JSON.stringify({json.dumps(genres)}.map(g=>homeScene(g))));")

class T(unittest.TestCase):
    def test_1_rule(self):
        self.assertEqual(DEF["schema"], "ccs-scenes/1"); self.assertEqual(len(DEF["scenes"]), 5)
        self.assertEqual(set(DEF["scenes"]), {"magic-cliffs", "forest-of-illusion", "town-at-night", "old-dark-castle", "sunny-rocky-mountains"})
        genres = {g.get("genre") for g in CAT if g.get("genre")}
        self.assertFalse(genres - set(DEF["genres"]), "every catalog genre has its own scene")
        for g in EDGE[:4]: self.assertEqual(SC.scene_for(g)["id"], DEF["fallback"], g)
        used = {SC.scene_for(g.get("genre"))["id"] for g in CAT}; self.assertEqual(used, set(DEF["scenes"]), "all five scenes are in use")
    def test_2_browser_and_packages_agree(self):
        genres = sorted({g.get("genre") or "" for g in CAT}) + EDGE
        js = js_scenes(genres); py = [SC.scene_for(g) for g in genres]
        self.assertEqual([(j["id"], j["src"], j["title"]) for j in js], [(p["id"], p["file"], p["title"]) for p in py])
    def test_3_an_old_profile_never_shows_a_retired_background(self):
        src = rd(os.path.join(STUDIO, "home-model.js")); hs = src[src.index("function homeScene(genre)"):]; hs = hs[:hs.index("\n", hs.index("return s?"))]
        th = rd(os.path.join(STUDIO, "home-themes.js")); ka = th[th.index("function hmKeyArt(g)"):]; ka = ka[:ka.index("\n")]
        import build_html as BH
        old = [{"id": "8e844d77da01f86a", "genre": "Sports", "hero": "art/hero/8e844d77da01f86a.jpg", "hero_title": {"x": 0.5, "y": 0.09}},
               {"id": "p_mine", "genre": None, "hero": "art/hero/0009d222946f66ff.jpg"}, {"id": "x", "genre": "RPG", "hero": "brand/home/hero-fallback.png"}]
        out = node(f"const HOME_SCENES={BH._home_scenes()};\n{hs}\n{ka}\nprocess.stdout.write(JSON.stringify({json.dumps(old)}.map(g=>hmKeyArt(g))));")
        self.assertEqual([o["src"] for o in out], ["art/scenes/sunny-rocky-mountains.png", "art/scenes/sunny-rocky-mountains.png", "art/scenes/magic-cliffs.png"])
        self.assertNotIn("art_hero", src); self.assertNotIn("hero_fallback", th)
    def test_4_retired(self):
        self.assertFalse(os.path.exists(os.path.join(SD, "art", "hero")), "the 102 per-game backgrounds are gone")
        self.assertFalse([g for g in CAT if any(k.startswith("art_hero") for k in g)], "no catalog game names a per-game background")
        home = os.path.join(REPO, "tools", "publish", "assets", "home")
        self.assertFalse([f for f in os.listdir(home) if f.startswith("ref-") or f == "hero-fallback.png"], "no reference picture or universal fallback")
        art = json.load(open(os.path.join(home, "home-art.json"), encoding="utf-8")); self.assertNotIn("hero_fallback", art)
        self.assertFalse([t for t, v in art["themes"].items() if "reference" in v])
        for rel in ("tools/rom-pipeline/studio/home-model.js", "tools/rom-pipeline/studio/home-themes.js", "tools/rom-pipeline/build_html.py", "tools/companion/native_home/package.py",
                    "tools/companion/native_home/config.py", "site-data/catalog.json"):
            t = rd(os.path.join(REPO, rel)); self.assertIsNone(re.search(r"art/hero/|hero-fallback\.png|art_hero", t), rel)
        b = rd(os.path.join(REPO, "tools", "publish", "build_site_ci.py"))
        self.assertIn('raise SystemExit("retired per-game backgrounds (art/hero) must not be published")', b); self.assertIn('ignore_patterns("ref-*.png", "hero-fallback.png")', b)
        self.assertIn("const RETIRED = /\\/art\\/hero\\//;", rd(os.path.join(STUDIO, "sw.js")))   # an installed app's kept copies are removed at its next update
    def test_5_publishable_images(self):   # the images tools/publish/make_scenes.py composed from the artist's own layer files
        from PIL import Image
        import hashlib
        sys.path.insert(0, os.path.join(REPO, "tools", "publish")); import make_scenes as MS
        for sid, s in DEF["scenes"].items():
            p = os.path.join(SD, *s["file"].split("/")); self.assertTrue(os.path.isfile(p), f"{sid}: no image (run make_scenes.py on the artist's layer files)")
            with open(p, "rb") as f: self.assertEqual(hashlib.sha256(f.read()).hexdigest(), s.get("sha256"), f"{sid}: not the image make_scenes.py recorded")
            self.assertEqual([l["sha256"] for l in s.get("layers") or []], [MS.SOURCES[r] for r, *_ in MS.RECIPES[sid]["layers"]], f"{sid}: layers recorded")
            c = s.get("credit") or {}; self.assertEqual(c.get("artist"), "Luis Zuno (Ansimuz)"); self.assertIn(c.get("license"), ("CC BY 3.0", "CC0")); self.assertTrue(c.get("source", "").startswith("https://opengameart.org/") and c.get("changes"))
            self.assertEqual(c.get("license") == "CC BY 3.0", sid == "magic-cliffs", f"{sid}: license")
            with Image.open(p) as im:
                self.assertEqual((im.format, im.size), ("PNG", (1920, 1080))); self.assertFalse(im.info.get("exif") or im.info.get("icc_profile") or im.getexif() or getattr(im, "text", None), f"{sid}: metadata")
                self.assertFalse(getattr(im, "is_animated", False), f"{sid}: static")
                k = s["scale"]; px = im.convert("RGB"); small = px.resize((1920 // k, 1080 // k), Image.NEAREST)
                self.assertEqual(small.resize((1920, 1080), Image.NEAREST).tobytes(), px.tobytes(), f"{sid}: a whole-number nearest-neighbor enlargement (no smoothing or generated detail)")
        t = rd(os.path.join(REPO, "THIRD_PARTY_NOTICES.md")); self.assertIn("Luis Zuno (Ansimuz)", t); self.assertIn("CC BY 3.0", t)

if __name__ == "__main__":
    r = unittest.main(exit=False, verbosity=2).result; print(f"RESULT {r.testsRun - len(r.failures) - len(r.errors)} / {r.testsRun}"); sys.exit(0 if r.wasSuccessful() else 1)
