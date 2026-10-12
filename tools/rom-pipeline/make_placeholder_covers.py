"""Generated placeholder covers for games without box art (website, Windows app and all four HOME themes share them).

Deterministic, procedural, offline: a retro-game-box-inspired front (dark card, colored title band derived from the title's
hash, a pixel-style corner mark, the real game title set large, publisher / year line when known) with a small legible
GENERATED COVER tag baked into a corner of the image itself. Nothing here touches the authentic catalog art: the files go
to reports/art/generated/<game_id>.png and catalog rows get `art_generated` (provenance "generated") while `art` stays empty.
When authentic art appears for a game later, build_html prefers `art`, and the generated file is simply left unused.

  python make_placeholder_covers.py            -> writes the covers and updates reports/catalog.json
  python make_placeholder_covers.py --force    -> regenerates every placeholder (otherwise only missing files)
"""
from __future__ import annotations
import os, sys, json, hashlib, textwrap
from PIL import Image, ImageDraw, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from common import REPORTS
OUT = os.path.join(REPORTS, "art", "generated"); W, H = 300, 420
FONT_PX = os.path.join(REPORTS, "brand", "fonts", "PressStart2P-Regular.ttf"); FONT_UI = r"C:\Windows\Fonts\bahnschrift.ttf"
PALETTES = [((228, 0, 15), (255, 214, 102)), ((30, 110, 220), (255, 255, 255)), ((16, 140, 80), (255, 240, 120)), ((120, 50, 200), (255, 200, 240)), ((230, 120, 20), (40, 20, 10)), ((20, 160, 170), (255, 255, 255)), ((200, 30, 120), (255, 230, 120)), ((90, 90, 100), (255, 255, 255))]

def font(path, size):
    try: return ImageFont.truetype(path, size)
    except Exception: return ImageFont.load_default()

def cover(title: str, publisher: str | None, year: str | None, seed: str) -> Image.Image:
    h = hashlib.sha256(seed.encode("utf-8")).digest(); band, ink = PALETTES[h[0] % len(PALETTES)]
    im = Image.new("RGB", (W, H), (22, 22, 28)); d = ImageDraw.Draw(im)
    for y in range(H): d.line([(0, y), (W, y)], fill=(22 + y // 30, 22 + y // 30, 28 + y // 24))              # soft vertical gradient
    d.rectangle([0, 0, W - 1, H - 1], outline=(60, 60, 70), width=3); d.rectangle([10, 10, W - 11, H - 11], outline=(90, 90, 100), width=1)
    px = font(FONT_PX, 8); d.text((16, 24), "CLASSIC CART STUDIO", font=px, fill=(170, 170, 180))
    for i in range(6): d.rectangle([W - 60 + i * 8, 20 + (h[1 + i] % 3) * 6, W - 55 + i * 8, 36], fill=band)       # pixel mark
    size = 34; f = font(FONT_UI, size); lines = textwrap.wrap(title, 14) or [title]
    while len(lines) > 3 and size > 18: size -= 2; f = font(FONT_UI, size); lines = textwrap.wrap(title, int(14 * 34 / size))
    lines = lines[:3]; band_h = 24 + len(lines) * (size + 4)
    d.rectangle([0, 56, W, 56 + band_h], fill=band)                                                                    # title band sized to the title
    d.rectangle([0, 56 + band_h, W, 62 + band_h], fill=(0, 0, 0)); d.rectangle([0, 50, W, 56], fill=(0, 0, 0))
    y = 68
    for ln in lines:
        d.text((17, y + 1), ln, font=f, fill=(0, 0, 0)); d.text((16, y), ln, font=f, fill=ink); y += size + 4
    sub = " · ".join(x for x in (publisher, year) if x); d.text((16, 82 + band_h), sub or "NES", font=font(FONT_UI, 16), fill=(200, 200, 210))
    # abstract art block: hashed blocks in the palette (never a real scene, never licensed art)
    r = h[2:]; bx0, by0 = 16, 112 + band_h
    for i in range(6 * 7):
        x, yy = bx0 + (i % 6) * 44, by0 + (i // 6) * 24; v = r[i % len(r)]
        if yy + 20 > 372: break
        if v % 3: d.rectangle([x, yy, x + 40, yy + 20], fill=tuple(int(c * (0.35 + (v % 5) * 0.12)) for c in band))
    d.text((16, 384), "GENERATED COVER", font=px, fill=(255, 214, 102)); d.rectangle([12, 379, 160, 396], outline=(255, 214, 102), width=1)   # the disclosure, in the image itself
    return im

def ensure(games: list, force: bool = False) -> tuple[int, int]:
    """write a placeholder for every game without art (idempotent) and set art_generated on those rows; rows with art lose any stale field"""
    os.makedirs(OUT, exist_ok=True); made = kept = 0
    for g in games:
        if g.get("art"): g.pop("art_generated", None); g.pop("art_generated_provenance", None); continue
        rel = f"art/generated/{g['game_id']}.png"; p = os.path.join(REPORTS, rel.replace("/", os.sep))
        if force or not os.path.exists(p): cover(g.get("display_title") or g["game_id"], g.get("publisher"), g.get("year"), g["game_id"]).save(p, optimize=True); made += 1
        else: kept += 1
        g["art_generated"] = rel; g["art_generated_provenance"] = "generated"
    return made, kept

def main(force=False):
    cat_path = os.path.join(REPORTS, "catalog.json"); d = json.load(open(cat_path, encoding="utf-8"))
    made, kept = ensure(d["games"], force)
    json.dump(d, open(cat_path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(f"generated covers: {made} written, {kept} kept, folder {OUT}")

if __name__ == "__main__": main("--force" in sys.argv)
