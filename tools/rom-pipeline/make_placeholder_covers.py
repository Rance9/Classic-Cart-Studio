"""Generated placeholder covers for games without box art (website, Windows app and all four HOME themes share them).

Deterministic, procedural, offline, in the look of an NES box front: black top band with the Studio wordmark and the
three pixel bars, a pixel-art night landscape drawn from the title's hash (sky bands, stars, two mountain ridges, a
horizon glow, ground stripes) in one of the box palettes, the real game title set large in the pixel display face on a
color plate, publisher / year line, the silver Studio seal, and ONE small GENERATED COVER mark in the bottom corner baked
into the image itself (the only disclosure; renderers must not add a second label). Nothing here touches the authentic
catalog art: the files go to reports/art/generated/<game_id>.png and catalog rows get `art_generated` (provenance
"generated") while `art` stays empty. When authentic art appears for a game later, build_html prefers `art`.

  python make_placeholder_covers.py            -> writes the covers and updates reports/catalog.json
  python make_placeholder_covers.py --force    -> regenerates every placeholder (otherwise only missing files)
"""
from __future__ import annotations
import os, sys, json, hashlib, random
from PIL import Image, ImageDraw, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from common import REPORTS
OUT = os.path.join(REPORTS, "art", "generated"); W, H = 300, 420
FONT_PX = os.path.join(REPORTS, "brand", "fonts", "PressStart2P-Regular.ttf"); FONT_UI = r"C:\Windows\Fonts\bahnschrift.ttf"
# (sky top, sky horizon, far ridge, near ridge, ground, plate, ink)
PALETTES = [
    ((10, 14, 48), (90, 40, 110), (40, 24, 80), (22, 14, 50), (12, 8, 30), (228, 0, 15), (255, 255, 255)),
    ((8, 30, 60), (30, 120, 170), (16, 60, 100), (10, 36, 66), (6, 20, 40), (30, 110, 220), (255, 255, 255)),
    ((6, 28, 24), (40, 140, 90), (14, 70, 50), (8, 44, 34), (4, 24, 20), (16, 140, 80), (255, 240, 120)),
    ((30, 10, 40), (190, 70, 60), (90, 30, 50), (50, 18, 36), (24, 10, 20), (230, 120, 20), (40, 20, 10)),
    ((12, 12, 40), (60, 60, 160), (28, 28, 90), (16, 16, 60), (8, 8, 32), (120, 50, 200), (255, 220, 250)),
    ((5, 25, 35), (20, 150, 160), (10, 70, 80), (6, 44, 52), (3, 24, 30), (20, 160, 170), (255, 255, 255)),
    ((40, 14, 10), (220, 120, 40), (120, 50, 20), (70, 28, 14), (30, 12, 8), (240, 90, 30), (255, 245, 200)),
]

def font(path, size):
    try: return ImageFont.truetype(path, size)
    except Exception: return ImageFont.load_default()

def landscape(seed: bytes, pal, w=75, h=62) -> Image.Image:
    """a tiny pixel-art night scene, later scaled 4x with nearest neighbor for crisp pixels"""
    rnd = random.Random(seed); top, hor, far, near, ground, _, _ = pal
    im = Image.new("RGB", (w, h)); px = im.load()
    for y in range(h):
        t = y / (h - 1); c = tuple(int(top[i] + (hor[i] - top[i]) * (t ** 1.6)) for i in range(3))
        for x in range(w): px[x, y] = c
    for _ in range(28):
        x, y = rnd.randrange(w), rnd.randrange(int(h * 0.55)); px[x, y] = (235, 235, 245) if rnd.random() < 0.6 else (160, 170, 210)
    mx, my = rnd.randrange(8, w - 8), rnd.randrange(6, 18); r = rnd.choice([3, 4, 5])                   # moon
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            if dx * dx + dy * dy <= r * r: px[mx + dx, my + dy] = (250, 240, 200)
    for layer, col, base, amp in ((far, far, int(h * 0.58), 10), (near, near, int(h * 0.74), 7)):      # ridges
        yy = base; heights = []
        for x in range(w):
            yy += rnd.choice([-1, 0, 0, 1]); yy = max(base - amp, min(base + amp, yy)); heights.append(yy)
        for x in range(w):
            for y in range(heights[x], h): px[x, y] = col
    for y in range(int(h * 0.86), h):
        for x in range(w): px[x, y] = ground if (y + x // 3) % 3 else tuple(min(255, c + 18) for c in ground)
    for _ in range(3):                                                                                   # a tower or two on the near ridge
        x = rnd.randrange(4, w - 6); base_y = int(h * 0.74) - 4; ht = rnd.randrange(5, 12)
        for y in range(base_y - ht, base_y + 6):
            for dx in range(3): px[x + dx, y] = tuple(max(0, c - 6) for c in near)
        px[x + 1, base_y - ht - 1] = (255, 214, 102)
    return im.resize((w * 4, h * 4), Image.NEAREST)

def wrap_title(d: ImageDraw.ImageDraw, title: str, maxw: int):
    for size in (18, 16, 14, 12, 10):
        f = font(FONT_PX, size); words = title.split(" "); lines, cur = [], ""
        for wd in words:
            t = (cur + " " + wd).strip()
            if d.textlength(t, font=f) <= maxw or not cur: cur = t
            else: lines.append(cur); cur = wd
        if cur: lines.append(cur)
        if len(lines) <= 3 and all(d.textlength(l, font=f) <= maxw for l in lines): return f, lines, size
    f = font(FONT_PX, 10); return f, [title[:40]], 10

def cover(title: str, publisher: str | None, year: str | None, seed: str) -> Image.Image:
    h = hashlib.sha256(seed.encode("utf-8")).digest(); pal = PALETTES[h[0] % len(PALETTES)]; plate, ink = pal[5], pal[6]
    im = Image.new("RGB", (W, H), (14, 14, 18)); d = ImageDraw.Draw(im)
    im.paste(landscape(h, pal), (0, 40))                                                                 # scene 40..288
    d.rectangle([0, 0, W, 40], fill=(8, 8, 10)); d.rectangle([0, 40, W, 42], fill=plate)                 # top band
    px8 = font(FONT_PX, 8); d.text((14, 16), "CLASSIC CART STUDIO", font=px8, fill=(190, 190, 200))
    for i, hh in enumerate((10, 16, 12)): d.rectangle([W - 48 + i * 12, 30 - hh, W - 40 + i * 12, 30], fill=plate)
    # title plate over the lower scene
    f, lines, size = wrap_title(d, title, W - 44); lh = size + 8; ph = 18 + len(lines) * lh
    y0 = 288 - ph - 10; d.rectangle([12, y0, W - 12, y0 + ph], fill=plate); d.rectangle([12, y0, W - 12, y0 + ph], outline=(0, 0, 0), width=2); d.rectangle([16, y0 + 4, W - 16, y0 + ph - 4], outline=tuple(min(255, c + 60) for c in plate), width=1)
    y = y0 + 11
    for ln in lines:
        d.text((24, y + 2), ln, font=f, fill=(0, 0, 0)); d.text((22, y), ln, font=f, fill=ink); y += lh
    # bottom band: publisher / year, NES mark, the seal, and the single corner disclosure
    d.rectangle([0, 288, W, H], fill=(10, 10, 13)); d.rectangle([0, 288, W, 291], fill=plate)
    sub = " · ".join(x for x in (publisher, year) if x); d.text((16, 304), sub or "Nintendo Entertainment System", font=font(FONT_UI, 15), fill=(205, 205, 212))
    d.rounded_rectangle([16, 334, 86, 356], radius=4, fill=(40, 40, 48), outline=(120, 120, 130)); d.text((23, 341), "NES", font=px8, fill=(230, 230, 236))
    cx, cy = W - 44, 345; d.ellipse([cx - 22, cy - 22, cx + 22, cy + 22], fill=(200, 200, 206), outline=(120, 120, 130), width=2); d.ellipse([cx - 16, cy - 16, cx + 16, cy + 16], outline=(110, 110, 118), width=1)
    d.text((cx - 11, cy - 4), "CCS", font=font(FONT_PX, 7), fill=(50, 50, 60))
    px7 = font(FONT_PX, 7); d.text((100, 342), "GENERATED COVER", font=px7, fill=(214, 182, 96))       # the one disclosure, in the image (beside the NES chip, clear of the box-theme overlays)
    d.rectangle([0, 0, W - 1, H - 1], outline=(70, 70, 80), width=2)
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
