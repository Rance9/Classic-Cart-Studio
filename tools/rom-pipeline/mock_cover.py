"""The NES-style mock box cover (the approved generated-placeholder design), as one reusable template + the game's title.

Used wherever a game has no authentic box art and no user artwork: the Studio page draws the same design on a canvas
(studio/mock-cover.js, a line-for-line port), the console package derives it only for the selected games, and the native
HOME and TV Preview show those derivatives. Deterministic and portable: the scene is drawn from a mulberry32 stream seeded
with the first four bytes of SHA-256(game id), with integer pixel math, so Python and JavaScript draw the same landscape.

Layout (300 x 420, NES box-front proportions): black top band with the Studio wordmark and three pixel bars; a pixel-art night
landscape (sky gradient, stars, moon, two ridges, towers, ground stripes) in one of seven box palettes; the real title set
large in Press Start 2P on a color plate (wrapped to three lines, shrinking 18 -> 10 px); a publisher / year line (or
"Nintendo Entertainment System"); an NES chip, the silver Studio seal and ONE small GENERATED COVER mark baked into the
image, so it can never be mistaken for an authentic release and renderers never add a second label.
"""
from __future__ import annotations
import hashlib, math, os
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
W, H = 300, 420
VERSION = "mock-2"
PALETTES = [   # (sky top, sky horizon, far ridge, near ridge, ground, plate, ink)
    ((10, 14, 48), (90, 40, 110), (40, 24, 80), (22, 14, 50), (12, 8, 30), (228, 0, 15), (255, 255, 255)),
    ((8, 30, 60), (30, 120, 170), (16, 60, 100), (10, 36, 66), (6, 20, 40), (30, 110, 220), (255, 255, 255)),
    ((6, 28, 24), (40, 140, 90), (14, 70, 50), (8, 44, 34), (4, 24, 20), (16, 140, 80), (255, 240, 120)),
    ((30, 10, 40), (190, 70, 60), (90, 30, 50), (50, 18, 36), (24, 10, 20), (230, 120, 20), (40, 20, 10)),
    ((12, 12, 40), (60, 60, 160), (28, 28, 90), (16, 16, 60), (8, 8, 32), (120, 50, 200), (255, 220, 250)),
    ((5, 25, 35), (20, 150, 160), (10, 70, 80), (6, 44, 52), (3, 24, 30), (20, 160, 170), (255, 255, 255)),
    ((40, 14, 10), (220, 120, 40), (120, 50, 20), (70, 28, 14), (30, 12, 8), (240, 90, 30), (255, 245, 200)),
]

def _first(*paths):
    for p in paths:
        if p and os.path.isfile(p): return p
    return None
FONT_PX = _first(os.environ.get("CCS_MOCK_FONT_PX"), os.path.join(ROOT, "reports", "brand", "fonts", "PressStart2P-Regular.ttf"), os.path.join(HERE, "..", "publish", "assets", "fonts", "PressStart2P-Regular.ttf"))
FONT_UI = _first(os.environ.get("CCS_MOCK_FONT_UI"), os.path.join(ROOT, "tools", "companion", "native_home", "fonts", "Barlow-Regular.ttf"), os.path.join(HERE, "..", "companion", "native_home", "fonts", "Barlow-Regular.ttf"))

class Mulberry32:
    """the same stream as studio/mock-cover.js (all arithmetic in uint32)"""
    def __init__(self, seed: int): self.a = seed & 0xFFFFFFFF
    def next(self) -> float:
        self.a = (self.a + 0x6D2B79F5) & 0xFFFFFFFF; t = self.a
        t = ((t ^ (t >> 15)) * (t | 1)) & 0xFFFFFFFF
        t ^= (t + (((t ^ (t >> 7)) * (t | 61)) & 0xFFFFFFFF)) & 0xFFFFFFFF
        return ((t ^ (t >> 14)) & 0xFFFFFFFF) / 4294967296.0
    def below(self, n: int) -> int: return int(self.next() * n)
    def between(self, a: int, b: int) -> int: return a + self.below(b - a)
    def pick(self, seq): return seq[self.below(len(seq))]

def seed_of(game_id: str) -> tuple[int, int]:
    h = hashlib.sha256(game_id.encode("utf-8")).digest(); return h[0] % len(PALETTES), int.from_bytes(h[:4], "little")

def _font(path, size):
    try: return ImageFont.truetype(path, size)
    except Exception: return ImageFont.load_default()

def landscape(rnd: Mulberry32, pal, w=75, h=62) -> Image.Image:
    top, hor, far, near, ground, _, _ = pal
    im = Image.new("RGB", (w, h)); px = im.load()
    for y in range(h):
        t = y / (h - 1); k = t ** 1.6; c = tuple(int(top[i] + (hor[i] - top[i]) * k) for i in range(3))
        for x in range(w): px[x, y] = c
    for _ in range(28):
        x = rnd.below(w); y = rnd.below(int(h * 0.55)); px[x, y] = (235, 235, 245) if rnd.next() < 0.6 else (160, 170, 210)
    mx = rnd.between(8, w - 8); my = rnd.between(6, 18); r = rnd.pick([3, 4, 5])
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            if dx * dx + dy * dy <= r * r: px[mx + dx, my + dy] = (250, 240, 200)
    for col, base, amp in ((far, int(h * 0.58), 10), (near, int(h * 0.74), 7)):
        yy = base; heights = []
        for x in range(w):
            yy += rnd.pick([-1, 0, 0, 1]); yy = max(base - amp, min(base + amp, yy)); heights.append(yy)
        for x in range(w):
            for y in range(heights[x], h): px[x, y] = col
    for y in range(int(h * 0.86), h):
        for x in range(w): px[x, y] = ground if (y + x // 3) % 3 else tuple(min(255, c + 18) for c in ground)
    for _ in range(3):
        x = rnd.between(4, w - 6); base_y = int(h * 0.74) - 4; ht = rnd.between(5, 12)
        for y in range(base_y - ht, base_y + 6):
            for dx in range(3): px[x + dx, y] = tuple(max(0, c - 6) for c in near)
        px[x + 1, base_y - ht - 1] = (255, 214, 102)
    return im.resize((w * 4, h * 4), Image.NEAREST)

def wrap_title(d: ImageDraw.ImageDraw, title: str, maxw: int):
    for size in (18, 16, 14, 12, 10):
        f = _font(FONT_PX, size); words = title.split(" "); lines, cur = [], ""
        for wd in words:
            t = (cur + " " + wd).strip()
            if d.textlength(t, font=f) <= maxw or not cur: cur = t
            else: lines.append(cur); cur = wd
        if cur: lines.append(cur)
        if len(lines) <= 3 and all(d.textlength(l, font=f) <= maxw for l in lines): return f, lines, size
    f = _font(FONT_PX, 10); words = title.split(" "); lines, cur = [], ""   # very long titles: three lines at 10 px, the last one cut with an ellipsis
    for wd in words:
        t = (cur + " " + wd).strip()
        if d.textlength(t, font=f) <= maxw or not cur: cur = t
        else: lines.append(cur); cur = wd
    if cur: lines.append(cur)
    if len(lines) > 3:
        last = " ".join(lines[2:])
        while last and d.textlength(last + "...", font=f) > maxw: last = last[:-1]
        lines = lines[:2] + [last.rstrip() + "..."]
    return f, lines, 10

def cover(title: str, publisher: str | None, year: str | None, game_id: str) -> Image.Image:
    pi, s = seed_of(game_id); pal = PALETTES[pi]; plate, ink = pal[5], pal[6]; rnd = Mulberry32(s)
    im = Image.new("RGB", (W, H), (14, 14, 18)); d = ImageDraw.Draw(im)
    im.paste(landscape(rnd, pal), (0, 40))
    d.rectangle([0, 0, W, 40], fill=(8, 8, 10)); d.rectangle([0, 40, W, 42], fill=plate)
    px8 = _font(FONT_PX, 8); d.text((14, 16), "CLASSIC CART STUDIO", font=px8, fill=(190, 190, 200))
    for i, hh in enumerate((10, 16, 12)): d.rectangle([W - 48 + i * 12, 30 - hh, W - 40 + i * 12, 30], fill=plate)
    f, lines, size = wrap_title(d, title or game_id, W - 44); lh = size + 8; ph = 18 + len(lines) * lh
    y0 = 288 - ph - 10; d.rectangle([12, y0, W - 12, y0 + ph], fill=plate); d.rectangle([12, y0, W - 12, y0 + ph], outline=(0, 0, 0), width=2)
    d.rectangle([16, y0 + 4, W - 16, y0 + ph - 4], outline=tuple(min(255, c + 60) for c in plate), width=1)
    y = y0 + 11
    for ln in lines:
        d.text((24, y + 2), ln, font=f, fill=(0, 0, 0)); d.text((22, y), ln, font=f, fill=ink); y += lh
    d.rectangle([0, 288, W, H], fill=(10, 10, 13)); d.rectangle([0, 288, W, 291], fill=plate)
    sub = " · ".join(x for x in (publisher, year) if x); fu = _font(FONT_UI, 15); sub = sub or "Nintendo Entertainment System"
    while d.textlength(sub, font=fu) > W - 32 and len(sub) > 4: sub = sub[:-2].rstrip() + "…"
    d.text((16, 304), sub, font=fu, fill=(205, 205, 212))
    d.rounded_rectangle([16, 334, 86, 356], radius=4, fill=(40, 40, 48), outline=(120, 120, 130)); d.text((23, 341), "NES", font=px8, fill=(230, 230, 236))
    cx, cy = W - 44, 345; d.ellipse([cx - 22, cy - 22, cx + 22, cy + 22], fill=(200, 200, 206), outline=(120, 120, 130), width=2); d.ellipse([cx - 16, cy - 16, cx + 16, cy + 16], outline=(110, 110, 118), width=1)
    d.text((cx - 11, cy - 4), "CCS", font=_font(FONT_PX, 7), fill=(50, 50, 60))
    d.text((100, 342), "GENERATED COVER", font=_font(FONT_PX, 7), fill=(214, 182, 96))
    d.rectangle([0, 0, W - 1, H - 1], outline=(70, 70, 80), width=2)
    return im

if __name__ == "__main__":
    import sys
    gid = sys.argv[1] if len(sys.argv) > 1 else "0009d222946f66ff"; title = sys.argv[2] if len(sys.argv) > 2 else "Goonies II - Fratelli Saigo no Chousen"
    out = sys.argv[3] if len(sys.argv) > 3 else os.path.join(os.environ.get("TEMP", "."), f"mock-{gid}.png"); cover(title, "Konami", "1987", gid).save(out); print(out)
