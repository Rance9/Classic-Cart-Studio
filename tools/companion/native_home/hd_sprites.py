r"""The computer's high-resolution HOME overlays ("play" packages): each theme overlay the native renderer draws (frames, tab bars, panels,
badges, icons) redrawn as clean anti-aliased artwork at twice its stage size, in the approved sprite's own shapes and colors. The scene
(the background picture) is not redrawn: it stays the approved retro art. The console's packages keep the approved sprites only.

render(theme, name, src) -> an RGBA image at twice src's size, or None when the overlay has no redrawing (it is then drawn from the
approved sprite, doubled). Drawn at 4 x that size and reduced (Lanczos), so edges are smooth.
"""
from __future__ import annotations
import math
from PIL import Image, ImageDraw, ImageFilter, ImageFont

SS = 4   # supersampling over the 2x target
VERSION = "hd-1"

def _font(px: int, bold: bool = True):
    import os
    here = os.path.dirname(os.path.abspath(__file__))
    return ImageFont.truetype(os.path.join(here, "fonts", "Barlow-Bold.ttf" if bold else "Barlow-SemiBold.ttf"), px)

def _rgba(hexs: str, a: int = 255) -> tuple[int, int, int, int]:
    h = hexs.lstrip("#"); return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)

class Canvas:
    """a 4x supersampled RGBA drawing of a w x h (target pixels) image; coordinates are given in target pixels"""
    def __init__(self, w: int, h: int):
        self.w, self.h = w, h; self.im = Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))
    def layer(self) -> tuple[Image.Image, ImageDraw.ImageDraw]:
        im = Image.new("RGBA", self.im.size, (0, 0, 0, 0)); return im, ImageDraw.Draw(im)
    def add(self, im: Image.Image, blur: float = 0.0):
        if blur > 0: im = im.filter(ImageFilter.GaussianBlur(blur * SS))
        self.im.alpha_composite(im)
    def s(self, *v): return [x * SS for x in v]
    def done(self) -> Image.Image: return self.im.resize((self.w, self.h), Image.LANCZOS)

def _rrect(d: ImageDraw.ImageDraw, c: Canvas, box, r, fill=None, outline=None, width=0):
    d.rounded_rectangle(c.s(*box), radius=r * SS, fill=fill, outline=outline, width=int(round(width * SS)) if width else 0)

def _vgrad(c: Canvas, box, r, top, bottom):
    """a vertically graded rounded rectangle"""
    x0, y0, x1, y1 = box; im, d = c.layer(); m = Image.new("L", c.im.size, 0); ImageDraw.Draw(m).rounded_rectangle(c.s(x0, y0, x1, y1), radius=r * SS, fill=255)
    g = Image.new("RGBA", (1, 256)); g.putdata([tuple(int(top[k] + (bottom[k] - top[k]) * i / 255) for k in range(4)) for i in range(256)])
    g = g.resize((int((x1 - x0) * SS), int((y1 - y0) * SS))); im.paste(g, (int(x0 * SS), int(y0 * SS))); out = Image.new("RGBA", c.im.size, (0, 0, 0, 0)); out.paste(im, (0, 0), m); c.add(out)

def _glow_stroke(c: Canvas, box, r, color, width, blur):
    im, d = c.layer(); _rrect(d, c, box, r, outline=color, width=width); c.add(im, blur)

def _letter(c: Canvas, ch: str, cx: float, cy: float, px: float, color):
    im, d = c.layer(); f = _font(int(px * SS)); d.text((cx * SS, cy * SS), ch, font=f, fill=color, anchor="mm"); c.add(im)

def _star(d, c, cx, cy, ro, ri, fill, outline=None, width=0, rot=-90):
    pts = []
    for i in range(10):
        a = math.radians(rot + i * 36); r = ro if i % 2 == 0 else ri; pts.append(((cx + r * math.cos(a)) * SS, (cy + r * math.sin(a)) * SS))
    d.polygon(pts, fill=fill, outline=outline, width=int(width * SS) if width else 1)

def _gear(d, c, cx, cy, ro, ri, teeth, hole, fill, bg):
    pts = []
    for i in range(teeth * 4):
        a = 2 * math.pi * i / (teeth * 4); r = ro if (i % 4) in (1, 2) else ri; pts.append(((cx + r * math.cos(a)) * SS, (cy + r * math.sin(a)) * SS))
    d.polygon(pts, fill=fill); d.ellipse(c.s(cx - ri * 0.82, cy - ri * 0.82, cx + ri * 0.82, cy + ri * 0.82), fill=fill)
    d.ellipse(c.s(cx - hole, cy - hole, cx + hole, cy + hole), fill=bg)

def _gamepad(d, c, x0, y0, x1, y1, body, dark):
    w, h = x1 - x0, y1 - y0
    d.rounded_rectangle(c.s(x0 + w * .06, y0 + h * .18, x1 - w * .06, y1 - h * .08), radius=h * .32 * SS, fill=body)
    d.ellipse(c.s(x0, y0 + h * .34, x0 + w * .34, y1), fill=body); d.ellipse(c.s(x1 - w * .34, y0 + h * .34, x1, y1), fill=body)
    cx, cy, a, t = x0 + w * .26, y0 + h * .55, h * .18, h * .07   # the D-pad
    d.rectangle(c.s(cx - a, cy - t, cx + a, cy + t), fill=dark); d.rectangle(c.s(cx - t, cy - a, cx + t, cy + a), fill=dark)
    for dx, dy in ((.70, .45), (.80, .58), (.70, .70), (.60, .58)):
        r = h * .075; px, py = x0 + w * dx, y0 + h * dy; d.ellipse(c.s(px - r, py - r, px + r, py + r), fill=dark)

def _console(d, c, x0, y0, x1, y1, body, dark, heart=None):   # an NES-style console: body, cartridge slot, two ports
    w, h = x1 - x0, y1 - y0
    d.rounded_rectangle(c.s(x0, y0 + h * .12, x1, y1), radius=h * .16 * SS, fill=body)
    d.rounded_rectangle(c.s(x0 + w * .22, y0 + h * .26, x1 - w * .22, y0 + h * .52), radius=h * .06 * SS, fill=dark)
    d.rounded_rectangle(c.s(x0 + w * .30, y0 + h * .33, x1 - w * .30, y0 + h * .45), radius=h * .03 * SS, fill=body)
    for k in range(3): r = h * .05; px, py = x0 + w * (.16 + k * .1), y0 + h * .80; d.ellipse(c.s(px - r, py - r, px + r, py + r), fill=dark)
    if heart:
        hx, hy, r = x1 - w * .2, y0 + h * .78, h * .11
        d.ellipse(c.s(hx - 2 * r, hy - r, hx, hy + r * .2), fill=heart); d.ellipse(c.s(hx, hy - r, hx + 2 * r, hy + r * .2), fill=heart)
        d.polygon([c.s(hx - 2 * r, hy - r * .2)[0:2], c.s(hx + 2 * r, hy - r * .2)[0:2], c.s(hx, hy + 1.8 * r)[0:2]], fill=heart)

# ---------------- Arcade Grid ----------------
def arcade(name: str, w: int, h: int) -> Image.Image | None:
    c = Canvas(w, h); im, d = c.layer()
    RED, RED2, RED3, CREAM = _rgba("#db1121"), _rgba("#b30c1a"), _rgba("#ff5a5a"), _rgba("#fceace")
    BLUE, BLUE2, NAVY = _rgba("#5f90f6"), _rgba("#2c4bb8"), _rgba("#020717")
    if name == "tab-bar":   # the bar's fine blue outline (its dark ground is in the scene)
        _rrect(d, c, (1.5, 1.5, w - 1.5, h - 1.5), h * .22, outline=_rgba("#2b4596"), width=2.4); _rrect(d, c, (4.5, 4.5, w - 4.5, h - 4.5), h * .17, outline=_rgba("#101c52", 200), width=1.4); c.add(im)
    elif name == "tab-active":   # the lit red tab
        _vgrad(c, (2, 2, w - 2, h - 2), h * .22, _rgba("#ee1d2b"), _rgba("#c00b19"))
        _rrect(d, c, (2, 2, w - 2, h - 2), h * .22, outline=RED3, width=2.2); _rrect(d, c, (5, 5, w - 5, h * .48), h * .16, fill=(255, 255, 255, 26)); c.add(im)
    elif name == "card-frame":   # the neon card frame: glow, the red line, the pale corner brackets
        inset = 8; box = (inset, inset, w - inset, h - inset)
        _glow_stroke(c, box, 14, _rgba("#ff1a2a", 235), 18, 9); _glow_stroke(c, box, 14, _rgba("#ff2a35", 255), 9, 2.5)
        _glow_stroke(c, box, 14, _rgba("#ff3c40", 255), 6, 0.6); _rrect(d, c, box, 14, outline=_rgba("#ffb0a8"), width=1.8)
        L, t = 46, 7; x0, y0, x1, y1 = box; pale = _rgba("#ffe7df")
        for sx, sy, dx, dy in ((x0, y0, 1, 1), (x1, y0, -1, 1), (x0, y1, 1, -1), (x1, y1, -1, -1)):   # an L at each corner
            hx = sorted((sx, sx + dx * L)); vy = sorted((sy, sy + dy * L))
            d.rounded_rectangle(c.s(hx[0] - t / 2, sy - t / 2, hx[1] + t / 2, sy + t / 2), radius=t / 2 * SS, fill=pale)
            d.rounded_rectangle(c.s(sx - t / 2, vy[0] - t / 2, sx + t / 2, vy[1] + t / 2), radius=t / 2 * SS, fill=pale)
        c.add(im)
    elif name == "icon-games":   # four cream squares (All Games)
        g = min(w, h) * .80; x0, y0 = (w - g) / 2, (h - g) / 2; s = g * .44
        for i in range(2):
            for j in range(2): _rrect(d, c, (x0 + i * (g - s), y0 + j * (g - s), x0 + i * (g - s) + s, y0 + j * (g - s) + s), s * .2, fill=CREAM)
        c.add(im)
    elif name == "icon-builtin": _console(d, c, w * .1, h * .1, w * .9, h * .9, BLUE, NAVY); c.add(im)
    elif name == "icon-star": _star(d, c, w / 2, h * .54, min(w, h) * .46, min(w, h) * .2, _rgba("#f63f39")); c.add(im)
    elif name == "icon-folder":
        x0, y0, x1, y1 = w * .1, h * .16, w * .9, h * .88
        _rrect(d, c, (x0, y0, x0 + (x1 - x0) * .45, y0 + (y1 - y0) * .35), (y1 - y0) * .1, fill=_rgba("#5f90ee"))
        _rrect(d, c, (x0, y0 + (y1 - y0) * .18, x1, y1), (y1 - y0) * .12, fill=_rgba("#6b9ffc")); c.add(im)
    elif name == "icon-clock":
        r = min(w, h) * .42; cx, cy = w / 2, h / 2; d.ellipse(c.s(cx - r, cy - r, cx + r, cy + r), fill=_rgba("#6d99f6"))
        lw = r * .16; d.line([c.s(cx, cy)[0:2], c.s(cx, cy - r * .62)[0:2]], fill=NAVY, width=int(lw * SS)); d.line([c.s(cx, cy)[0:2], c.s(cx + r * .45, cy + r * .1)[0:2]], fill=NAVY, width=int(lw * SS))
        d.ellipse(c.s(cx - lw * .8, cy - lw * .8, cx + lw * .8, cy + lw * .8), fill=NAVY); c.add(im)
    elif name == "icon-gear": r = min(w, h) * .46; _gear(d, c, w / 2, h / 2, r, r * .76, 8, r * .3, _rgba("#9598da"), (0, 0, 0, 0)); c.add(im)
    elif name in ("badge-a", "badge-x"):
        fill, ring, ch = (_rgba("#f63331"), _rgba("#c0161b"), "A") if name == "badge-a" else (_rgba("#458cfb"), _rgba("#2a5fc4"), "X")
        r = min(w, h) * .46; cx, cy = w / 2, h / 2; d.ellipse(c.s(cx - r, cy - r, cx + r, cy + r), fill=fill, outline=ring, width=int(r * .12 * SS)); c.add(im)
        _letter(c, ch, cx, cy + r * .04, r * 1.25, _rgba("#140608"))
    elif name == "stat-icon-games": _console(d, c, w * .08, h * .1, w * .92, h * .9, _rgba("#4372f9"), _rgba("#050a2b")); c.add(im)
    elif name == "stat-icon-storage": _console(d, c, w * .08, h * .1, w * .92, h * .9, _rgba("#608ef6"), _rgba("#080e28"), heart=_rgba("#b05cd0")); c.add(im)
    elif name == "stat-icon-coll": _gamepad(d, c, w * .06, h * .12, w * .94, h * .9, _rgba("#6c9ef9"), _rgba("#000820")); c.add(im)
    else: return None
    return c.done()

RENDERERS = {"arcade-grid": arcade}
REDRAWN = {"arcade-grid": {"tab-bar", "tab-active", "card-frame", "icon-games", "icon-builtin", "icon-star", "icon-folder", "icon-clock", "icon-gear",
                           "badge-a", "badge-x", "stat-icon-games", "stat-icon-storage", "stat-icon-coll"}}
def has(theme: str, name: str) -> bool: return name in REDRAWN.get(theme, ())

def render(theme: str, name: str, src: Image.Image) -> Image.Image | None:
    fn = RENDERERS.get(theme); return fn(name, src.width * 2, src.height * 2) if fn else None

# ---------------- the scene at 2x: the background picture kept (its pixels doubled), its painted panels redrawn clean ----------------
def _patch(base: Image.Image, box, r, fill=None, outline=None, width=0.0, pad=6):
    """one anti-aliased rounded rectangle composited onto base (target pixels), drawn 4x inside its own bounding box"""
    x0, y0, x1, y1 = box; bx0, by0 = int(x0 - pad), int(y0 - pad); bw, bh = int(x1 - x0 + 2 * pad) + 1, int(y1 - y0 + 2 * pad) + 1
    im = Image.new("RGBA", (bw * SS, bh * SS), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.rounded_rectangle(((x0 - bx0) * SS, (y0 - by0) * SS, (x1 - bx0) * SS, (y1 - by0) * SS), radius=r * SS, fill=fill, outline=outline, width=int(round(width * SS)) if width else 0)
    base.alpha_composite(im.resize((bw, bh), Image.LANCZOS), (bx0, by0))

def _wordmark(base, box, text, ground, ink, bold=True, shadow=(0, 0, 0, 170)):
    """the brand wordmark redrawn: its area filled with the sky behind it (feathered), the text set clean, fitted to the box"""
    x0, y0, x1, y1 = box; m = Image.new("L", base.size, 0); ImageDraw.Draw(m).rectangle((x0, y0, x1, y1), fill=255); m = m.filter(ImageFilter.GaussianBlur(4))
    fill = Image.new("RGBA", base.size, ground); base.paste(Image.composite(fill, base, m), (0, 0))
    W, H = x1 - x0, y1 - y0; px = int(H * 1.2)
    while px > 8 and _font(px, bold).getlength(text) > W * 0.98: px -= 1
    f = _font(px * SS, bold); tw = f.getlength(text); im = Image.new("RGBA", (int(W * SS) + 8 * SS, int(H * SS) + 8 * SS), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    bx, by = 4 * SS + (W * SS - tw) / 2, 4 * SS + H * SS * 0.84
    d.text((bx + 3 * SS, by + 3 * SS), text, font=f, fill=shadow, anchor="ls"); d.text((bx, by), text, font=f, fill=ink, anchor="ls")
    base.alpha_composite(im.resize((int(W) + 8, int(H) + 8), Image.LANCZOS), (int(x0) - 4, int(y0) - 4))

def scene_hd(theme: str, scene: Image.Image, geo: dict) -> Image.Image | None:
    if theme not in SCENES: return None
    base = scene.convert("RGB").resize((scene.width * 2, scene.height * 2), Image.NEAREST).convert("RGBA")
    SCENES[theme](base, geo, lambda b: [v * 2 for v in b]); return base.convert("RGB")

def _arcade_scene(base, geo, S2):
    panel, edge, ground, plate, plate_edge = _rgba("#000517"), _rgba("#1d2e58"), _rgba("#00030f"), _rgba("#02071a"), _rgba("#0e173a")
    for r in ((243, 95, 1237, 452), (251, 446, 1105, 616)): _patch(base, S2(r), 18, fill=ground)   # the dark ground under the card and every cell
    for c in geo["cells"]: x0, y0, x1, y1 = c; _patch(base, S2((x0 - 3, y0 - 3, x1 + 3, y1 + 3)), 10, fill=plate, outline=plate_edge, width=2)
    x0, y0, x1, y1 = geo["card"]["frame"]; _patch(base, S2((x0 + 4, y0 + 4, x1 - 4, y1 - 4)), 22, fill=plate)
    _patch(base, S2((20, 103, 235, 303)), 24, fill=panel, outline=edge, width=4)            # the stats panel
    _patch(base, S2((313, 48, 1139, 93)), 18, fill=panel, outline=_rgba("#1a2b52"), width=3)  # the tab band (the bar's outline is drawn on it)
    _patch(base, S2((31, 650, 1256, 700)), 20, fill=panel, outline=_rgba("#1c2950"), width=3)  # the legend bar
    _wordmark(base, S2((24, 20, 316, 72)), "Classic Cart Studio", _rgba("#000416"), _rgba("#f8eddd"))   # the brand, clean (the red star stays)

SCENES = {"arcade-grid": _arcade_scene}

# ---------------- shared shapes for the other themes ----------------
def _chevron(c, d, cx, cy, size, left, color, width):
    s = size; pts = [(cx + s * .35, cy - s), (cx - s * .35, cy), (cx + s * .35, cy + s)] if left else [(cx - s * .35, cy - s), (cx + s * .35, cy), (cx - s * .35, cy + s)]
    d.line([(x * SS, y * SS) for x, y in pts], fill=color, width=int(width * SS), joint="curve")
    for x, y in (pts[0], pts[2]): r = width / 2; d.ellipse(c.s(x - r, y - r, x + r, y + r), fill=color)
def _neon_frame(c, box, r, core, glow, inner=None, width=4.0):
    _glow_stroke(c, box, r, glow, width * 3.2, width * 1.6); _glow_stroke(c, box, r, core, width * 1.4, width * .35)
    im, d = c.layer(); _rrect(d, c, box, r, outline=core, width=width)
    if inner: _rrect(d, c, box, r, outline=inner, width=max(1.0, width * .35))
    c.add(im)
def _circle_badge(c, w, h, fill, ring, letter, ink, ring_w=.12, bg=None):
    im, d = c.layer(); r = min(w, h) * .46; cx, cy = w / 2, h / 2
    if bg: d.ellipse(c.s(cx - r, cy - r, cx + r, cy + r), fill=bg)
    d.ellipse(c.s(cx - r, cy - r, cx + r, cy + r), fill=fill, outline=ring, width=int(r * ring_w * SS) if ring else 0); c.add(im)
    if letter: _letter(c, letter, cx, cy + r * .04, r * 1.2, ink)
def _house(d, c, w, h, color):
    x0, y0, x1, y1 = w * .14, h * .16, w * .86, h * .86; mx = (x0 + x1) / 2; ey = y0 + (y1 - y0) * .46
    d.polygon([c.s(mx, y0)[0:2], c.s(x1, ey)[0:2], c.s(x0, ey)[0:2]], fill=color)
    d.rectangle(c.s(x0 + (x1 - x0) * .14, ey - 1, x1 - (x1 - x0) * .14, y1), fill=color)
    d.rectangle(c.s(mx - (x1 - x0) * .1, y1 - (y1 - y0) * .32, mx + (x1 - x0) * .1, y1), fill=(0, 0, 0, 0))
def _plank(c, w, h, top, bottom, edge, light):
    _vgrad(c, (2, 2, w - 2, h - 2), h * .16, top, bottom); im, d = c.layer()
    _rrect(d, c, (2, 2, w - 2, h - 2), h * .16, outline=edge, width=2.6); _rrect(d, c, (5, 5, w - 5, h * .42), h * .1, fill=light)
    for x in (w * .06, w * .94): r = h * .05; d.ellipse(c.s(x - r, h / 2 - r, x + r, h / 2 + r), fill=edge)
    c.add(im)

# ---------------- Box Art Showcase ----------------
def boxart(name: str, w: int, h: int) -> Image.Image | None:
    c = Canvas(w, h); im, d = c.layer()
    if name in ("arrow-l", "arrow-r"):
        r = min(w, h) * .46; cx, cy = w / 2, h / 2; d.ellipse(c.s(cx - r, cy - r, cx + r, cy + r), fill=_rgba("#121316", 235), outline=_rgba("#3d3e43"), width=int(2.4 * SS)); c.add(im)
        im2, d2 = c.layer(); _chevron(c, d2, cx + (r * .06 if name == "arrow-l" else -r * .06), cy, r * .34, name == "arrow-l", _rgba("#ececee"), r * .14); c.add(im2)
    elif name == "badge-brand":
        _rrect(d, c, (2, 2, w - 2, h - 2), (h - 4) / 2, fill=_rgba("#060606"), outline=_rgba("#c8262a"), width=3); c.add(im)
        _fit_text(c, "Classic Cart Studio", (h * .5, 0, w - h * .5, h), _rgba("#d93034"))
    elif name == "logo-mark":
        _rrect(d, c, (1, 1, w - 1, h - 1), 6, fill=_rgba("#0a0e11"))
        d.polygon([c.s(w * .16, h * .86)[0:2], c.s(w * .5, h * .12)[0:2], c.s(w * .84, h * .86)[0:2]], fill=_rgba("#e3e2e6"))
        d.polygon([c.s(w * .34, h * .86)[0:2], c.s(w * .5, h * .5)[0:2], c.s(w * .66, h * .86)[0:2]], fill=_rgba("#0a0e11")); c.add(im)
    elif name == "panel-frame":
        _rrect(d, c, (2, 2, w - 2, h - 2), 16, fill=_rgba("#0b0d11", 120), outline=_rgba("#34373d"), width=2.6)
        _rrect(d, c, (6, 6, w - 6, h - 6), 12, outline=_rgba("#16181c"), width=1.4); c.add(im)
    elif name == "seal":
        r = min(w, h) * .47; cx, cy = w / 2, h / 2
        d.ellipse(c.s(cx - r, cy - r, cx + r, cy + r), fill=_rgba("#c49a62"), outline=_rgba("#6e5136"), width=int(r * .08 * SS))
        for k in range(16):   # the scalloped rim
            a = 2 * math.pi * k / 16; px_, py_ = cx + r * .86 * math.cos(a), cy + r * .86 * math.sin(a); q = r * .1; d.ellipse(c.s(px_ - q, py_ - q, px_ + q, py_ + q), fill=_rgba("#e0bb85"))
        ri = r * .62; d.ellipse(c.s(cx - ri, cy - ri, cx + ri, cy + ri), fill=_rgba("#f1e7d6"), outline=_rgba("#8f6b45"), width=int(r * .06 * SS))
        for k, yy in enumerate((-.22, 0, .22)): ww = ri * (.62 if k != 1 else .82); d.rounded_rectangle(c.s(cx - ww, cy + yy * r - r * .045, cx + ww, cy + yy * r + r * .045), radius=r * .04 * SS, fill=_rgba("#8a6a48"))
        c.add(im)
    else: return None
    return c.done()

def _fit_text(c, text, box, color, bold=True, shadow=None, spacing=0.0):
    x0, y0, x1, y1 = box; W, H = x1 - x0, y1 - y0; px = int(H * .78)
    while px > 6 and _font(px, bold).getlength(text) + spacing * len(text) > W: px -= 1
    im, d = c.layer(); f = _font(px * SS, bold); tw = f.getlength(text) + spacing * SS * (len(text) - 1); x = (x0 + (W - tw / SS) / 2) * SS; y = (y0 + H / 2) * SS
    for k, ch in enumerate(text):
        if shadow: d.text((x + SS * 1.5, y + SS * 1.5), ch, font=f, fill=shadow, anchor="lm")
        d.text((x, y), ch, font=f, fill=color, anchor="lm"); x += f.getlength(ch) + spacing * SS
    c.add(im)

# ---------------- Featured Shelf ----------------
def shelf(name: str, w: int, h: int) -> Image.Image | None:
    c = Canvas(w, h); im, d = c.layer(); CREAM, RED = _rgba("#eccdb2"), _rgba("#eb2c2f")
    if name in ("arrow-l", "arrow-r"): _chevron(c, d, w / 2, h / 2, min(w, h) * .34, name == "arrow-l", _rgba("#a39b95"), min(w, h) * .085); c.add(im)
    elif name == "badge-a": _circle_badge(c, w, h, _rgba("#ec272b"), _rgba("#a81b1e"), "A", _rgba("#140507"))
    elif name == "badge-x": _circle_badge(c, w, h, _rgba("#8a8689"), _rgba("#5d595c"), "X", _rgba("#110d0f"))
    elif name == "hero-frame":
        _rrect(d, c, (2, 2, w - 2, h - 2), 6, outline=_rgba("#2c2d33"), width=3); _rrect(d, c, (6, 6, w - 6, h - 6), 4, outline=_rgba("#5d5e68"), width=1.6); c.add(im)
    elif name == "icon-folder":
        x0, y0, x1, y1 = w * .14, h * .2, w * .86, h * .82
        _rrect(d, c, (x0, y0, x0 + (x1 - x0) * .44, y0 + (y1 - y0) * .32), (y1 - y0) * .1, fill=_rgba("#d9b99c")); _rrect(d, c, (x0, y0 + (y1 - y0) * .17, x1, y1), (y1 - y0) * .1, fill=CREAM); c.add(im)
    elif name == "icon-gear": r = min(w, h) * .4; _gear(d, c, w / 2, h / 2, r, r * .74, 8, r * .3, CREAM, (0, 0, 0, 0)); c.add(im)
    elif name == "icon-home": _house(d, c, w, h, RED); c.add(im)
    elif name == "icon-star": _star(d, c, w / 2, h * .53, min(w, h) * .4, min(w, h) * .17, _rgba("#e2c5ab")); c.add(im)
    elif name == "shelf-focus": _neon_frame(c, (7, 7, w - 7, h - 7), 8, _rgba("#ff2c2c"), _rgba("#ff1a20", 220), _rgba("#ffc2bb"), 3.2)
    elif name == "side-active":
        _vgrad(c, (2, 2, w - 2, h - 2), 8, _rgba("#4a1e1f"), _rgba("#2f1416")); _rrect(d, c, (2, 2, w - 2, h - 2), 8, outline=_rgba("#c02a2c"), width=2.4)
        d.rounded_rectangle(c.s(2, 6, 9, h - 6), radius=3 * SS, fill=_rgba("#ff3b3e")); c.add(im)
    else: return None
    return c.done()

# ---------------- Adventure Map ----------------
def advmap(name: str, w: int, h: int) -> Image.Image | None:
    c = Canvas(w, h); im, d = c.layer(); RED, DARK = _rgba("#ed222b"), _rgba("#05080c")
    if name in ("arrow-l", "arrow-r"): _chevron(c, d, w / 2, h / 2, min(w, h) * .34, name == "arrow-l", RED, min(w, h) * .27); c.add(im)
    elif name in ("badge-a", "badge-b"): _circle_badge(c, w, h, RED, _rgba("#a5151c"), "A" if name == "badge-a" else "B", _rgba("#14050a"), .1)
    elif name == "badge-dpad":
        a, t = min(w, h) * .4, min(w, h) * .14; cx, cy = w / 2, h / 2
        d.rounded_rectangle(c.s(cx - a, cy - t, cx + a, cy + t), radius=t * .5 * SS, fill=RED); d.rounded_rectangle(c.s(cx - t, cy - a, cx + t, cy + a), radius=t * .5 * SS, fill=RED); c.add(im)
    elif name == "badge-menu":
        r = min(w, h) * .45; cx, cy = w / 2, h / 2; d.ellipse(c.s(cx - r, cy - r, cx + r, cy + r), fill=RED)
        for yy in (-.32, 0, .32): d.rounded_rectangle(c.s(cx - r * .5, cy + yy * r - r * .08, cx + r * .5, cy + yy * r + r * .08), radius=r * .08 * SS, fill=DARK)
        c.add(im)
    elif name in ("card-frame", "shelf-focus"): _neon_frame(c, (8, 8, w - 8, h - 8), 10, _rgba("#ff2a30"), _rgba("#ff1820", 225), _rgba("#ffb4ac"), 3.6)
    elif name == "shelf-frame": _rrect(d, c, (3, 3, w - 3, h - 3), 8, fill=_rgba("#05080d", 150), outline=_rgba("#2a2c34"), width=2.4); c.add(im)
    elif name == "card-pointer": d.polygon([c.s(w * .08, h * .1)[0:2], c.s(w * .92, h * .1)[0:2], c.s(w * .5, h * .92)[0:2]], fill=_rgba("#fed799"), outline=_rgba("#d1572f"), width=int(2.4 * SS)); c.add(im)
    elif name == "marker":
        d.ellipse(c.s(w * .04, h * .1, w * .96, h * .9), fill=_rgba("#c95731")); d.ellipse(c.s(w * .14, h * .2, w * .86, h * .74), fill=_rgba("#e12125"), outline=_rgba("#fdd7be"), width=int(2 * SS)); c.add(im)
    elif name == "panel-frame":
        k = 14; pts = [(k, 3), (w - k, 3), (w - 3, k), (w - 3, h - k), (w - k, h - 3), (k, h - 3), (3, h - k), (3, k)]
        d.polygon([(x * SS, y * SS) for x, y in pts], fill=_rgba("#0d0710", 150)); d.line([(x * SS, y * SS) for x, y in pts + pts[:1]], fill=_rgba("#d4252f"), width=int(3 * SS), joint="curve"); c.add(im)
    elif name == "sign": _plank(c, w, h, _rgba("#8a5a30"), _rgba("#5a3318"), _rgba("#2a160b"), (255, 230, 190, 40))
    elif name == "sign-start": _plank(c, w, h, _rgba("#8a6a30"), _rgba("#5c4317"), _rgba("#2a1d0b"), (255, 235, 180, 40))
    else: return None
    return c.done()

# ---------------- their scenes' painted UI ----------------
def _line(base, p0, p1, color, width):
    x0, y0 = p0; x1, y1 = p1; pad = width + 4; bx0, by0 = int(min(x0, x1) - pad), int(min(y0, y1) - pad); bw, bh = int(abs(x1 - x0) + 2 * pad) + 1, int(abs(y1 - y0) + 2 * pad) + 1
    im = Image.new("RGBA", (bw * SS, bh * SS), (0, 0, 0, 0)); ImageDraw.Draw(im).line(((x0 - bx0) * SS, (y0 - by0) * SS, (x1 - bx0) * SS, (y1 - by0) * SS), fill=color, width=int(width * SS))
    base.alpha_composite(im.resize((bw, bh), Image.LANCZOS), (bx0, by0))
def _cover(base, box, color, feather=4):
    x0, y0, x1, y1 = box; m = Image.new("L", base.size, 0); ImageDraw.Draw(m).rectangle(box, fill=255); m = m.filter(ImageFilter.GaussianBlur(feather))
    base.paste(Image.composite(Image.new("RGBA", base.size, color), base, m), (0, 0))
def _text_at(base, text, x, y, px, color, anchor="ls", bold=False, spacing=0.0):
    f = _font(int(px * SS), bold); tw = f.getlength(text) + spacing * SS * max(0, len(text) - 1); th = int(px * 1.6)
    im = Image.new("RGBA", (int(tw / SS) + 8, th + 8), (0, 0, 0, 0)); im2 = Image.new("RGBA", (im.width * SS, im.height * SS), (0, 0, 0, 0)); d = ImageDraw.Draw(im2)
    cx = 4 * SS
    for ch in text: d.text((cx, (4 + px * 1.1) * SS), ch, font=f, fill=color, anchor="ls"); cx += f.getlength(ch) + spacing * SS
    im = im2.resize(im.size, Image.LANCZOS); ox = x - (im.width - 8) if anchor.startswith("r") else x
    base.alpha_composite(im, (int(ox) - 4, int(y - px * 1.1) - 4))

def _boxart_scene(base, geo, S2):
    bg = _rgba("#0c1016")
    _cover(base, S2((44, 15, 412, 75)), bg, 6)   # the brand: a red pill outline, the name in red
    c = Canvas(int(358 * 2), int(52 * 2)); im, d = c.layer(); _rrect(d, c, (3, 3, c.w - 3, c.h - 3), (c.h - 6) / 2, outline=_rgba("#e3222a"), width=7); c.add(im)
    _fit_text(c, "Classic Cart Studio", (c.h * .42, 4, c.w - c.h * .42, c.h - 4), _rgba("#e3222a")); base.alpha_composite(c.done(), (int(49 * 2), int(19 * 2)))
    _cover(base, S2((1138, 98, 1262, 184)), _rgba("#1d222a"), 8)   # the tagline, right-aligned, letter-spaced
    for k, word in enumerate(("PLAY", "COLLECT", "EXPLORE", "BELONG")): _text_at(base, word, 1251 * 2, (113 + k * 18.2) * 2, 21, _rgba("#c9cbd0"), anchor="rs", spacing=6)
    _line(base, (1234 * 2, 192.5 * 2), (1251 * 2, 192.5 * 2), _rgba("#e0222a"), 3)
    _cover(base, S2((42, 464, 90, 514)), _rgba("#0e0f12"), 4)   # the red compass star
    c = Canvas(96, 96); im, d = c.layer(); pts = []
    for i in range(8): a = math.radians(-90 + i * 45); r = 44 if i % 2 == 0 else 11; pts.append(((48 + r * math.cos(a)) * SS, (48 + r * math.sin(a)) * SS))
    d.polygon(pts, fill=_rgba("#e01b23")); c.add(im, 0); im2, d2 = c.layer(); pts2 = [(x * .5 + 24 * SS, y * .5 + 24 * SS) for x, y in pts]; d2.polygon(pts2, fill=_rgba("#ff6a66")); c.add(im2)
    base.alpha_composite(c.done(), (int(65.5 * 2 - 48), int(488.5 * 2 - 48)))
    _cover(base, S2((50, 530, 84, 540)), _rgba("#0a0b0d"), 2); _line(base, (55 * 2, 535.5 * 2), (77 * 2, 535.5 * 2), _rgba("#8e8e8d"), 2.2)   # the short rule
    _cover(base, S2((50, 601, 610, 610)), _rgba("#050507"), 2); _line(base, (55 * 2, 605.5 * 2), (604 * 2, 605.5 * 2), _rgba("#46464a"), 3)   # the long rule

def _shelf_scene(base, geo, S2):
    _cover(base, S2((116, 8, 492, 50)), _rgba("#111113"), 5)   # the brand name (the castle and the speed lines stay)
    c = Canvas(int(366 * 2), int(40 * 2)); _fit_text(c, "Classic Cart Studio", (0, 0, c.w, c.h), _rgba("#f2e1cb"), shadow=(0, 0, 0, 150)); base.alpha_composite(c.done(), (int(118 * 2), int(10 * 2)))
    _cover(base, S2((612, 48, 990, 68)), _rgba("#121214"), 5)   # the tagline
    x = 647 * 2
    for k, word in enumerate(("PLAY", "COLLECT", "EXPLORE", "REWIND")):
        if k: _patch(base, (x + 14, 112, x + 24, 122), 1.5, fill=_rgba("#c8262a")); x += 38
        f = _font(int(20 * SS), False); w = (f.getlength(word) + 5 * SS * (len(word) - 1)) / SS; _text_at(base, word, x, 122, 20, _rgba("#c9c4bd"), spacing=5); x += w + 2
    for r in ((218, 452, 1210, 622),): _patch(base, S2(r), 10, fill=_rgba("#0a0a0c"))   # the shelf's dark ground, then a plate per cell
    for cbox in geo["shelf"]["cells"]: x0, y0, x1, y1 = cbox; _patch(base, S2((x0 - 3, y0 - 3, x1 + 3, y1 + 3)), 8, fill=_rgba("#111114"), outline=_rgba("#1d1d22"), width=2)

def _map_scene(base, geo, S2):
    _cover(base, S2((70, 40, 338, 80)), _rgba("#0d0a14"), 5)   # the brand name in red (the crown stays)
    c = Canvas(int(262 * 2), int(37 * 2)); _fit_text(c, "Classic Cart Studio", (0, 0, c.w, c.h), _rgba("#e8232b"), shadow=(0, 0, 0, 170)); base.alpha_composite(c.done(), (int(72 * 2), int(41 * 2)))
    for lm in geo["landmarks"]:   # the red pads on the path, clean
        cx, cy = lm["center"]; pad = Canvas(84, 50); im, d = pad.layer(); d.ellipse(pad.s(3, 5, 81, 45), fill=_rgba("#c95731")); d.ellipse(pad.s(11, 9, 73, 37), fill=_rgba("#e12125"), outline=_rgba("#fdd7be"), width=int(2 * SS)); pad.add(im)
        base.alpha_composite(pad.done(), (int(cx * 2 - 42), int(cy * 2 - 22)))
    _patch(base, S2((100, 552, 1180, 652)), 8, fill=_rgba("#070a0f"))   # the shelf's dark ground (each cell has its frame)
    _cover(base, S2((84, 658, 1196, 667)), _rgba("#05060a"), 2); _line(base, (88 * 2, 662.5 * 2), (1192 * 2, 662.5 * 2), _rgba("#2a2c33"), 2.5)   # the progress line's track

RENDERERS.update({"box-art-showcase": boxart, "featured-shelf": shelf, "adventure-map": advmap})
REDRAWN.update({"box-art-showcase": {"arrow-l", "arrow-r", "badge-brand", "logo-mark", "panel-frame", "seal"},
                "featured-shelf": {"arrow-l", "arrow-r", "badge-a", "badge-x", "hero-frame", "icon-folder", "icon-gear", "icon-home", "icon-star", "shelf-focus", "side-active"},
                "adventure-map": {"arrow-l", "arrow-r", "badge-a", "badge-b", "badge-dpad", "badge-menu", "card-frame", "card-pointer", "marker", "panel-frame", "shelf-focus", "shelf-frame", "sign", "sign-start"}})
SCENES.update({"box-art-showcase": _boxart_scene, "featured-shelf": _shelf_scene, "adventure-map": _map_scene})
