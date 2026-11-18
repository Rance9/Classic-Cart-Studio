r"""Each theme sprite's own pixels, for a HOME design's background.

The four themes' sprites (frames, plates, tabs, signs, icons, the walker) were cut as rectangles from the approved theme designs, so
around and between their own shapes they carry pixels of the picture they were cut from: dark ground, forest, grass, the edge of a
sample cover. Over the theme's own background those pixels sit where the same picture is. Over a design's own background they would
show as patches of the old picture, so the package also carries, for every sprite the native HOME draws over the background, the
same sprite with only its own pixels: those unchanged (at a soft edge, the sprite's own color with the coverage it had over the old
ground), every pixel of the old picture see-through. The native HOME draws these only while a design changes the active theme's
background; the theme's own screens keep the approved sprites exactly.

Each sprite's rule (OWN) tells its own pixels from the picture's, in the sprite's own pixel coordinates:
  key     the ground colors given: starting at the sprite's edge (and, with seeds "edge+hole", at the edge of its cover hole), every
          pixel reached through ground-like pixels is the ground (see-through); a pixel between lo and hi of the nearest ground
          color keeps its own part (coverage (d - lo) / (hi - lo), its color unmixed from that ground). What the sprite's own shape
          encloses is never reached, so dark details inside it stay; holes: enclosed ground areas of at least that many pixels are
          see-through too (a gear's hole).
  line    a frame drawn in one or two line colors over a ground: each pixel is read as the line color over the ground color (its
          coverage, its color unmixed); a pixel that is neither (a sample cover's edge, sky) is not the frame's.
  red     a neon frame: only the red, pink and near-white of its line and glow (the glow's coverage from how much red it adds).
  shape   the outline of the sprite's own shape (rounded rectangles, ellipses, polygons) at 4 x 4 samples per pixel: nothing
          outside it is the sprite's own.
Rules combine: the smaller coverage of each pixel wins. Pure functions of the approved files (integer and IEEE double arithmetic, no
randomness): the same sprite always gives the same bytes.
"""
from __future__ import annotations
from collections import deque
from PIL import Image, ImageChops, ImageDraw

VERSION = "own-1"

def _px(im: Image.Image) -> list[tuple[int, int, int, int]]:
    b = im.convert("RGBA").tobytes(); return [tuple(b[i:i + 4]) for i in range(0, len(b), 4)]
def _img(size, px) -> Image.Image:
    r = Image.new("RGBA", size); r.frombytes(bytes(v for p in px for v in p)); return r
def _cheb(a, b) -> int: return max(abs(a[0] - b[0]), abs(a[1] - b[1]), abs(a[2] - b[2]))
def _c8(v: float) -> int: return 0 if v < 0 else 255 if v > 255 else int(v + 0.5)
def _part(p, g, o: float):
    """the part o of pixel p that is not the ground color g: its color unmixed from g, its coverage o (a pixel whose coverage does not
    change is kept exactly)"""
    a = _c8(p[3] * o)
    return p if a == p[3] else tuple(_c8(g[k] + (p[k] - g[k]) / o) for k in range(3)) + (a,)

def key(im: Image.Image, ground: list, lo: int = 14, hi: int = 70, holes: int = 0, seeds: str = "edge") -> Image.Image:
    w, h = im.size; px = _px(im)
    near = [min(ground, key=lambda g: _cheb(p[:3], g)) for p in px]
    own = [0.0 if v <= lo else 1.0 if v >= hi else (v - lo) / (hi - lo) for v in (_cheb(p[:3], g) for p, g in zip(px, near))]
    def nb(i):
        x, y = i % w, i // w
        return [j for j in ((i - 1) if x > 0 else -1, (i + 1) if x < w - 1 else -1, (i - w) if y > 0 else -1, (i + w) if y < h - 1 else -1) if j >= 0]
    seen = bytearray(w * h); q = deque()
    for i in range(w * h):
        if px[i][3] == 0 or own[i] >= 1: continue
        x, y = i % w, i // w
        if x == 0 or y == 0 or x == w - 1 or y == h - 1 or (seeds == "edge+hole" and any(px[j][3] == 0 for j in nb(i))): seen[i] = 1; q.append(i)
    while q:
        i = q.popleft()
        for j in nb(i):
            if not seen[j] and own[j] < 1 and px[j][3] > 0: seen[j] = 1; q.append(j)
    if holes:
        lab = bytearray(w * h)
        for s in range(w * h):
            if seen[s] or lab[s] or own[s] > 0 or px[s][3] == 0: continue
            comp = [s]; lab[s] = 1; k = 0
            while k < len(comp):
                for j in nb(comp[k]):
                    if not lab[j] and not seen[j] and own[j] < 1 and px[j][3] > 0: lab[j] = 1; comp.append(j)
                k += 1
            if sum(1 for i in comp if own[i] == 0) >= holes:
                for i in comp: seen[i] = 1
    out = []
    for i, p in enumerate(px):
        o = own[i]
        if not seen[i]: out.append(p)
        elif o <= 0: out.append((0, 0, 0, 0))
        else: out.append(_part(p, near[i], o))
    return _img(im.size, out)

def line(im: Image.Image, colors: list, ground: list, residual: int = 24) -> Image.Image:
    out = []
    for p in _px(im):
        if p[3] == 0: out.append(p); continue
        best = None
        for G in ground:
            for F in colors:
                dv = [F[k] - G[k] for k in range(3)]; n = dv[0] ** 2 + dv[1] ** 2 + dv[2] ** 2
                t = max(0.0, min(1.0, sum((p[k] - G[k]) * dv[k] for k in range(3)) / n)) if n else 0.0
                r = max(abs(p[k] - (G[k] + t * dv[k])) for k in range(3))
                if best is None or r < best[0]: best = (r, t, G)
        r, t, G = best
        if r > residual or t <= 0: out.append((0, 0, 0, 0))
        else: out.append(_part(p, G, t))
    return _img(im.size, out)

def red(im: Image.Image, lo: int = 18, hi: int = 90, white: int = 150) -> Image.Image:
    out = []
    for p in _px(im):
        r_, g_, b_, a = p
        if a == 0: out.append((0, 0, 0, 0)); continue
        rd = r_ - max(g_, b_) - 1.5 * max(0, g_ - b_ - 24); o = 0.0 if rd <= lo else 1.0 if rd >= hi else (rd - lo) / (hi - lo)   # orange and yellow (flames, a path) are not the neon
        mn = min(r_, g_, b_)
        if r_ + 6 >= b_ and r_ + 10 >= g_ and mn > white: o = max(o, min(1.0, (mn - white) / 50))   # a warm near-white (the neon core), not sky
        if o <= 0: out.append((0, 0, 0, 0))
        else: out.append(_part(p, (0, 0, 0), o))   # the glow: what it adds over a black ground
    return _img(im.size, out)

SS = 4
def shape(im: Image.Image, parts: list) -> Image.Image:
    """parts: ("rrect", x0, y0, x1, y1, radius), ("ellipse", x0, y0, x1, y1), ("poly", [(x, y), ...]) in the sprite's pixels (edges
    between pixels); ("cut", part) takes a part away"""
    w, h = im.size; m = Image.new("L", (w * SS, h * SS), 0); d = ImageDraw.Draw(m)
    for p in parts:
        fill = 255
        if p[0] == "cut": p = p[1]; fill = 0
        if p[0] == "rrect": d.rounded_rectangle([round(p[1] * SS), round(p[2] * SS), round(p[3] * SS) - 1, round(p[4] * SS) - 1], radius=round(p[5] * SS), fill=fill)
        elif p[0] == "ellipse": d.ellipse([round(p[1] * SS), round(p[2] * SS), round(p[3] * SS) - 1, round(p[4] * SS) - 1], fill=fill)
        elif p[0] == "poly": d.polygon([(x * SS, y * SS) for x, y in p[1]], fill=fill)
    r = im.convert("RGBA"); r.putalpha(ImageChops.darker(r.getchannel("A"), m.resize((w, h), Image.BOX))); return r

def combine(*ims: Image.Image) -> Image.Image:
    """per pixel, the rule that leaves the smaller coverage"""
    px = [_px(i) for i in ims]; return _img(ims[0].size, [min(ps, key=lambda p: p[3]) for ps in zip(*px)])

# ---------- the rules ----------
NAVY = [(1, 4, 20), (2, 8, 28), (0, 2, 12)]           # Arcade Grid's dark band and plates
SHELF = [(17, 17, 19), (18, 17, 20), (10, 10, 13)]    # Featured Shelf's dark ground
MAPD = [(3, 6, 10), (5, 5, 12), (2, 4, 8)]            # Adventure Map's dark shelf ground
OWN = {
    # Arcade Grid: the card's neon frame and its trim (the dark plate under the card and the caption are the picture's); the tab
    # bar's outline (the band inside it is the picture's) and the lit tab; the tab and collection icons
    ("arcade-grid", "card-frame"): lambda im: red(im),
    ("arcade-grid", "card-trim"): lambda im: red(im),
    ("arcade-grid", "tab-bar"): lambda im: combine(key(im, NAVY, seeds="edge+hole"), shape(im, [("poly", [(0, 2.5), (820, 2.5), (827.5, 10), (827.5, 39), (820, 48), (0, 48)])])),
    ("arcade-grid", "tab-active"): lambda im: key(im, NAVY),
    ("arcade-grid", "icon-games"): lambda im: key(im, [(213, 20, 35), (200, 16, 30)]),   # drawn on the lit tab: its red is the ground
    ("arcade-grid", "icon-folder"): lambda im: key(im, NAVY),
    ("arcade-grid", "icon-star"): lambda im: key(im, NAVY),
    ("arcade-grid", "icon-clock"): lambda im: key(im, NAVY),
    ("arcade-grid", "icon-gear"): lambda im: key(im, NAVY, holes=3),
    ("arcade-grid", "stat-icon-games"): lambda im: key(im, NAVY),
    # Box Art Showcase: the carousel arrows, a dark disc in a gray ring (the badge, seal and mark are drawn on the boxes)
    ("box-art-showcase", "arrow-l"): lambda im: shape(im, [("ellipse", 1.8, 0.8, 40.9, 39.9)]),
    ("box-art-showcase", "arrow-r"): lambda im: shape(im, [("ellipse", 0.6, 0.8, 40.7, 39.9)]),
    # Featured Shelf: the sidebar's lit plate and icons, the featured picture's frame lines, the focus frame, the arrows
    ("featured-shelf", "side-active"): lambda im: key(im, SHELF),
    ("featured-shelf", "icon-home"): lambda im: key(im, [(66, 26, 26), (60, 24, 24)]),   # drawn on the lit plate: its dark red is the ground
    ("featured-shelf", "icon-star"): lambda im: key(im, SHELF),
    ("featured-shelf", "icon-folder"): lambda im: key(im, SHELF),
    ("featured-shelf", "icon-gear"): lambda im: key(im, SHELF, holes=3),
    ("featured-shelf", "hero-frame"): lambda im: line(im, [(100, 99, 104)], SHELF + [(27, 29, 31)]),
    ("featured-shelf", "shelf-focus"): lambda im: red(im),
    ("featured-shelf", "arrow-l"): lambda im: key(im, [(9, 7, 8), (4, 2, 3), (16, 14, 15)]),
    ("featured-shelf", "arrow-r"): lambda im: key(im, [(7, 4, 5), (2, 1, 2), (14, 12, 13)]),
    # Adventure Map: the neon frames, the panel's red line, the shelf frames' side lines, the signs, the marker and pointer, the
    # walker and his dog (the leaves above them and the path under them are the picture's), the arrows, the D-pad hint
    ("adventure-map", "card-frame"): lambda im: red(im),
    ("adventure-map", "card-pointer"): lambda im: shape(im, [("poly", [(1.5, 0), (29.5, 0), (15.5, 15.5)])]),
    ("adventure-map", "marker"): lambda im: shape(im, [("ellipse", 6, 3.5, 35, 22)]),
    ("adventure-map", "panel-frame"): lambda im: red(im),
    ("adventure-map", "sign"): lambda im: shape(im, [("rrect", 0, 2, 106, 28, 2)]),
    ("adventure-map", "sign-start"): lambda im: shape(im, [("rrect", 3, 2, 58, 17, 2), ("rrect", 4, 15, 16, 27, 1), ("rrect", 40, 15, 47, 31, 1)]),
    ("adventure-map", "shelf-focus"): lambda im: red(im),
    ("adventure-map", "shelf-frame"): lambda im: combine(line(im, [(62, 46, 42)], MAPD + [(20, 5, 6), (9, 8, 14)], residual=10), shape(im, [("rrect", 2, 6, 8, 100, 0), ("rrect", 141, 6, 148, 100, 0)])),   # its side lines
    ("adventure-map", "badge-dpad"): lambda im: key(im, MAPD),
    ("adventure-map", "arrow-l"): lambda im: key(im, MAPD),
    ("adventure-map", "arrow-r"): lambda im: key(im, MAPD),
    ("adventure-map", "hero"): lambda im: combine(key(im, [(248, 182, 119), (232, 160, 100), (252, 200, 140)], 20, 60),
                                                  shape(im, [("poly", [(0, 15), (61, 15), (61, 21), (65, 21), (65, 39), (95, 39), (95, 75), (0, 75)])])),
}

def has(theme: str, name: str) -> bool: return (theme, name) in OWN
def render(theme: str, name: str, src: Image.Image) -> Image.Image | None:
    f = OWN.get((theme, name)); return f(src.convert("RGBA")) if f else None
