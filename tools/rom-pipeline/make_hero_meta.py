"""Per-game facts for the Featured Shelf hero system (no new images; the hero is composed at runtime from authentic art).

For every catalog game with cover art, records in reports/catalog.json:
  art_size  [w, h] of the cover image
  art_bands [top, bottom] fractions of the cover that are flat dark packaging strips (the NES band, the seal strip), so the
            hero shows the illustration without its packaging edges
  art_tone  dominant illustration color (hex), the fallback glow when a game has no gameplay screen
and writes the shared night-sky plate tools/publish/assets/home/hero-sky.png (deterministic). The hero composition itself
(sky + the game's own world from its gameplay screen + the cover illustration feathered in + chunky two-tone lettering)
lives in studio/home-themes.js (hmHero) and home.css. An owner-supplied widescreen image reports/art/hero/<game_id>.png
(`art_hero`) overrides the composition for that game.

  python make_hero_meta.py
"""
from __future__ import annotations
import os, sys, json, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import REPORTS
from PIL import Image, ImageDraw
import numpy as np
ASSETS = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "publish", "assets", "home")

def bands(cov: Image.Image):
    a = np.array(cov.convert("RGB")).astype(int); h = a.shape[0]; dark = (a.max(axis=2) < 64).mean(axis=1); sat = (a.max(axis=2) - a.min(axis=2)).mean(axis=1)
    strip = (dark > 0.55) & (sat < 60)
    def scan(idx, lim):
        n = 0; miss = 0; last = 0
        for i in idx:
            if n >= lim: break
            if strip[i]: n += 1; last = n; miss = 0
            else:
                miss += 1; n += 1
                if miss > max(2, int(h * 0.012)): break
        return last if last >= int(h * 0.03) else 0        # a strip must be at least 3 % tall to count
    t = scan(range(h), int(h * 0.24)); b = scan(range(h - 1, -1, -1), int(h * 0.24))
    return round(t / h, 4), round(b / h, 4)

def tone(cov: Image.Image, t: float, b: float) -> str:
    ill = cov.crop((0, int(cov.height * t), cov.width, int(cov.height * (1 - b)))).convert("RGB").resize((48, 48))
    a = np.array(ill).reshape(-1, 3).astype(int); sat = a.max(axis=1) - a.min(axis=1); lum = a.mean(axis=1)
    keep = a[(sat > 40) & (lum > 40) & (lum < 220)]
    c = (np.median(keep, axis=0) if len(keep) else np.median(a, axis=0)).astype(int)
    return "#%02x%02x%02x" % tuple(int(x) for x in c)

def sky(path: str, w=810, h=447):
    rnd = random.Random(20261012); px = np.zeros((h, w, 3), np.uint8)
    for y in range(h):
        t = y / (h - 1); px[y] = (int(6 + 10 * t), int(8 + 14 * t), int(24 + 30 * t))
    im = Image.fromarray(px); d = ImageDraw.Draw(im)
    for _ in range(260):
        x, y = rnd.randrange(w), rnd.randrange(int(h * 0.8)); d.point((x, y), rnd.choice([(150, 160, 200), (210, 215, 235), (255, 255, 255), (120, 130, 180)]))
    for _ in range(14):
        x, y = rnd.randrange(20, w - 20), rnd.randrange(10, int(h * 0.55)); c = (240, 240, 255); r = rnd.choice([3, 4, 6])
        d.line((x - r, y, x + r, y), c); d.line((x, y - r, x, y + r), c); d.point((x, y), (255, 255, 255))
    im.save(path, optimize=True)

def main():
    cat = json.load(open(os.path.join(REPORTS, "catalog.json"), encoding="utf-8")); n = 0
    for g in cat["games"]:
        g.pop("art_card", None); g.pop("art_card_source", None)
        if not g.get("art"): continue
        p = os.path.join(REPORTS, g["art"])
        if not os.path.exists(p): continue
        cov = Image.open(p); t, b = bands(cov); g["art_size"] = [cov.width, cov.height]; g["art_bands"] = [t, b]; g["art_tone"] = tone(cov, t, b); n += 1
    os.makedirs(ASSETS, exist_ok=True); sky(os.path.join(ASSETS, "hero-sky.png"))
    json.dump(cat, open(os.path.join(REPORTS, "catalog.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print("hero meta for", n, "covers")

if __name__ == "__main__": main()
