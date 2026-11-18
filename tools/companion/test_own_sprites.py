r"""The theme sprites' own pixels for a HOME design's background (native_home/own_sprites.py),
on the approved theme files of this repository (tools/publish/assets/home):
  rules        every sprite the native HOME draws over the background has a rule; the badges, seal and mark it draws on the boxes and
               the sprites it does not draw have none
  only own     the own-pixel version has the approved sprite's size, never more coverage than it anywhere, and a pixel it keeps whole
               keeps its approved color exactly; it makes the old picture see-through (each sprite loses coverage)
  the ground   no pixel of a sprite's ground color that touches its edge keeps any coverage (the dark band, the forest, the grass)
  the same     two derivations give the same bytes
  python test_own_sprites.py      Exit 0 = PASS."""
from __future__ import annotations
import hashlib, io, json, os, sys, unittest
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from PIL import Image
from native_home import own_sprites as OS
from native_home.package import THEMES

HOME = os.path.join(HERE, "..", "publish", "assets", "home")
ART = json.load(open(os.path.join(HOME, "home-art.json"), encoding="utf-8"))
OVER_BACKGROUND = {"arcade-grid": {"card-frame", "card-trim", "tab-bar", "tab-active", "icon-games", "icon-folder", "icon-star", "icon-clock", "icon-gear", "stat-icon-games"},
                   "box-art-showcase": {"arrow-l", "arrow-r"},
                   "featured-shelf": {"side-active", "icon-home", "icon-star", "icon-folder", "icon-gear", "hero-frame", "shelf-focus", "arrow-l", "arrow-r"},
                   "adventure-map": {"card-frame", "card-pointer", "marker", "panel-frame", "sign", "sign-start", "shelf-focus", "shelf-frame", "badge-dpad", "arrow-l", "arrow-r", "hero"}}

def sprites():
    for tid, th in THEMES.items():
        for name in th["sprites"] + (["card-trim"] if th.get("card_trim") else []):
            yield tid, name, os.path.join(HOME, th["card_trim"] if name == "card-trim" else ART["themes"][tid]["sprites"][name]["file"])

class OwnSprites(unittest.TestCase):
    def test_rules(self):
        have = {t: {n for n in OVER_BACKGROUND[t] if OS.has(t, n)} for t in THEMES}
        self.assertEqual(have, OVER_BACKGROUND, "a rule for every sprite drawn over the background")
        extra = [(t, n) for t, n, _ in sprites() if OS.has(t, n) and n not in OVER_BACKGROUND[t]]
        self.assertEqual(extra, [], "no rule for a sprite drawn on the boxes or not drawn at all")
    def test_only_own_pixels(self):
        for tid, name, p in sprites():
            if not OS.has(tid, name): continue
            with self.subTest(sprite=f"{tid}/{name}"):
                a = Image.open(p).convert("RGBA"); o = OS.render(tid, name, a)
                self.assertEqual(o.size, a.size); self.assertEqual(o.mode, "RGBA")
                lost = 0
                for pa, po in zip(OS._px(a), OS._px(o)):
                    self.assertLessEqual(po[3], pa[3], "never more coverage than the approved sprite")
                    if po[3] == pa[3] and pa[3] > 0: self.assertEqual(po[:3], pa[:3], "a pixel kept whole keeps its color")
                    lost += pa[3] - po[3]
                self.assertGreater(lost, 0, "the old picture is see-through")
    def test_ground_touching_the_edge(self):
        grounds = {("arcade-grid", n): OS.NAVY for n in ("tab-active", "icon-folder", "icon-star", "icon-clock", "icon-gear", "stat-icon-games")}
        grounds.update({("featured-shelf", n): OS.SHELF for n in ("side-active", "icon-star", "icon-folder", "icon-gear")})
        grounds.update({("adventure-map", n): OS.MAPD for n in ("badge-dpad", "arrow-l", "arrow-r")})
        for (tid, name), g in grounds.items():
            with self.subTest(sprite=f"{tid}/{name}"):
                p = os.path.join(HOME, ART["themes"][tid]["sprites"][name]["file"]); a = Image.open(p).convert("RGBA"); o = OS.render(tid, name, a); w, h = a.size
                pa, po = OS._px(a), OS._px(o)
                left = [(i % w, i // w) for i in range(w * h) if (i % w in (0, w - 1) or i // w in (0, h - 1)) and pa[i][3] and min(OS._cheb(pa[i][:3], c) for c in g) <= 14 and po[i][3] > 0]
                self.assertEqual(left, [], "the ground along the edge is see-through")
    def test_deterministic(self):
        for tid, name, p in sprites():
            if not OS.has(tid, name): continue
            a = Image.open(p).convert("RGBA"); x = io.BytesIO(); y = io.BytesIO()
            OS.render(tid, name, a).save(x, "PNG"); OS.render(tid, name, a).save(y, "PNG")
            self.assertEqual(hashlib.sha256(x.getvalue()).hexdigest(), hashlib.sha256(y.getvalue()).hexdigest(), f"{tid}/{name}")

if __name__ == "__main__":
    r = unittest.main(verbosity=2, exit=False).result
    n = r.testsRun; bad = len(r.failures) + len(r.errors); print(f"RESULT {n - bad}/{n} passed, {len(r.skipped)} skipped"); sys.exit(1 if bad or r.skipped else 0)
