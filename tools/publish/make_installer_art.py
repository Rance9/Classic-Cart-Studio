r"""The Windows setup's artwork: drawn here, in code, from Classic Cart Studio's own graphics (the cast in site-data/brand/ccs, the app icon,
the approved Classic Cart Studio wordmark) in the colors of the startup images (a synthwave sunset, a neon horizon). Written at every
display scale the setup chooses from, so it is sharp at 100 % to 250 %:
  wizard-panel-<pct>.png    the Welcome and Finish pages' tall picture (164 x 314 at 100 %): sky, striped sun, mountains, a grid floor,
                            the app icon, the wordmark, and the cast on the ground
  wizard-wordmark-<pct>.png the top right of every other page (100 x 26 at 100 %): the wordmark, transparent around it; the setup loads
                            the one for the display's scale
The wordmark is ALWAYS the approved one (the launch brand pack's logo, as the website's header draws it): Press Start 2P (site-data/brand/
fonts, OFL), CLASSIC CART #f5f6fc over STUDIO #ff344b at 1 : 1.25, centered, the lines 9/16 of the first line's size apart. Never another
font, color or proportion (owner, 2026-11-12: the earlier Pixelify Sans lettering here was the wrong wordmark).
  ../brand-wordmark.png     the wordmark at four times the website's size (768 x 184, transparent): the Windows app's loading screens
                            and the Help picture of the app show it (one source, never redrawn by hand)
  wizard-back-<pct>.png     every page's background (the setup window at 100 %): dark, with a faint grid floor and a neon horizon glow
                            behind the buttons, never behind text
  python tools/publish/make_installer_art.py       (needs Pillow; writes tools/publish/assets/installer/)
Part of Classic Cart Studio: GNU GPL v3 or later, like the rest of this repository."""
from __future__ import annotations
import functools, json, math, os
from PIL import Image, ImageDraw, ImageFilter, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.dirname(os.path.dirname(HERE))
CAST = os.path.join(REPO, "site-data", "brand", "ccs"); WM_FONT = os.path.join(REPO, "site-data", "brand", "fonts", "PressStart2P-Regular.ttf")
ICON = os.path.join(HERE, "assets", "app-256.png"); OUT = os.path.join(HERE, "assets", "installer")
SCALES = (100, 125, 150, 175, 200, 250)
PANEL, WORDMARK, BACK = (164, 314), (100, 26), (690, 500)
INK = (20, 16, 30)
WM_TOP, WM_BOTTOM, WM_RATIO, WM_GAP = ((245, 246, 252), "CLASSIC CART"), ((255, 52, 75), "STUDIO"), 1.25, 9 / 16   # the approved wordmark

def lerp(a, b, t): return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(len(a)))
def grad(img, box, stops):   # a vertical gradient through (position, color) stops
    x0, y0, x1, y1 = box; d = ImageDraw.Draw(img)
    for y in range(y0, y1):
        t = (y - y0) / max(1, y1 - y0 - 1)
        for (p0, c0), (p1, c1) in zip(stops, stops[1:]):
            if p0 <= t <= p1: d.line([(x0, y), (x1 - 1, y)], fill=lerp(c0, c1, (t - p0) / max(1e-9, p1 - p0)) + (255,)); break
def sprite(name, k):   # k display units per cast unit (the cast is drawn at two pixels per unit: ccs_cast_hd.py)
    im = Image.open(os.path.join(CAST, name + ".png")).convert("RGBA"); return im.resize((im.width * k // 2, im.height * k // 2), Image.NEAREST)
def draw_wordmark(img, cx, top, p1):   # the approved wordmark, CLASSIC CART at p1 px, centered on cx from top; drawn 8 times larger and
    SS = 8; lines = ((WM_TOP, p1), (WM_BOTTOM, p1 * WM_RATIO))   # reduced, so its proportions are exact at every size
    hi = Image.new("RGBA", (img.width * SS, round((p1 * (1 + WM_GAP + WM_RATIO) + 2) * SS)), (0, 0, 0, 0)); d = ImageDraw.Draw(hi); y = 0
    for (col, text), px in lines:
        f = ImageFont.truetype(WM_FONT, max(8, round(px * SS))); w = d.textlength(text, font=f)
        d.text((cx * SS - w / 2, y), text, font=f, fill=col + (255,)); y += px * SS + (p1 * WM_GAP * SS if px == p1 else 0)
    lo = hi.resize((img.width, hi.height // SS), Image.BOX); img.alpha_composite(lo, (0, round(top)))
    return p1 * (1 + WM_GAP + WM_RATIO)   # its height

def floor(img, box, horizon_col, line_col, s, alpha=255):   # a perspective grid floor (the startup images' road, in lines)
    x0, y0, x1, y1 = box; d = ImageDraw.Draw(img, "RGBA"); cx = (x0 + x1) / 2; h = y1 - y0
    grad(img, box, [(0, horizon_col), (1, INK)])
    for i in range(1, 14):   # horizontals, closer together toward the horizon
        t = (i / 13) ** 2.2; y = y0 + t * h; d.line([(x0, y), (x1, y)], fill=line_col + (int(alpha * (0.25 + 0.75 * t)),), width=max(1, s // 100))
    for i in range(-14, 15):   # verticals from a vanishing point at the horizon
        xb = cx + i * (x1 - x0) / 7; d.line([(cx + i * (x1 - x0) / 60, y0), (xb, y1)], fill=line_col + (int(alpha * 0.55),), width=max(1, s // 100))

def panel(pct):
    s = pct; W, H = round(PANEL[0] * s / 100), round(PANEL[1] * s / 100); img = Image.new("RGBA", (W, H), INK + (255,))
    hz = round(H * 0.64)   # the horizon (below the wordmark)
    grad(img, (0, 0, W, hz), [(0, (16, 12, 40)), (0.45, (70, 22, 92)), (0.8, (196, 44, 112)), (1, (247, 120, 70))])
    d = ImageDraw.Draw(img, "RGBA")
    for i in range(40):   # stars in the upper sky, fixed positions
        x = (i * 73 % 997) / 997 * W; y = (i * 151 % 613) / 613 * hz * 0.45; d.point((x, y), fill=(255, 255, 255, 140 + i * 3 % 100))
    r = round(W * 0.30); cx, cy = W // 2, hz - round(r * 0.12)   # the striped sun
    sun = Image.new("RGBA", (2 * r, 2 * r)); grad(sun, (0, 0, 2 * r, 2 * r), [(0, (255, 214, 92)), (1, (240, 60, 110))])
    m = Image.new("L", (2 * r, 2 * r)); ImageDraw.Draw(m).ellipse((0, 0, 2 * r - 1, 2 * r - 1), fill=255)
    md = ImageDraw.Draw(m)
    for k in range(6):   # the bands cut through its lower half
        y = round(r * (1.0 + 0.16 * k + 0.05)); md.rectangle((0, y, 2 * r, y + max(1, round(r * 0.035 * (k + 1)))), fill=0)
    glow = Image.new("RGBA", (W, H)); ImageDraw.Draw(glow).ellipse((cx - r * 1.5, cy - r * 1.5, cx + r * 1.5, cy + r * 1.5), fill=(255, 90, 120, 90))
    img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(r * 0.4))); img.paste(sun, (cx - r, cy - r), m)
    pts = [(0, hz)] + [(x, hz - (math.sin(x / W * 7.1) * 0.5 + 0.6) * H * 0.07 - (x * 37 % 11) * s / 100 * 0.6) for x in range(0, W + 1, max(2, W // 24))] + [(W, hz)]
    d.polygon(pts, fill=(34, 14, 52, 255))   # mountains on the horizon
    floor(img, (0, hz, W, H), (120, 30, 110), (255, 70, 170), s)
    k = max(1, round(1.6 * s / 100))   # the cast on the floor, nearest scaled
    cast = ["carty-0", "padd-1", "hero-0", "astro-1", "dino-0"]; sp = [sprite(n, k) for n in cast]
    gap = max(1, (W - sum(x.width for x in sp)) // (len(sp) + 1)); x = gap; base = H - round(H * 0.07)
    for im in sp: img.alpha_composite(im, (x, base - im.height)); x += im.width + gap
    for name, fx in (("coin", 0.18), ("gem", 0.5), ("heart", 0.82)):   # items floating above them
        it = sprite(name, k); img.alpha_composite(it, (round(W * fx - it.width / 2), base - round(H * 0.17)))
    ic = Image.open(ICON).convert("RGBA"); isz = round(W * 0.42); ic = ic.resize((isz, isz), Image.LANCZOS); img.alpha_composite(ic, ((W - isz) // 2, round(H * 0.05)))
    draw_wordmark(img, W / 2, round(H * 0.05) + isz + round(H * 0.035), W * 0.84 / 12)   # CLASSIC CART across 84 % of the width
    return img.convert("RGB")

def wordmark(pct):   # the approved wordmark, centered in its box, transparent around it
    W, H = round(WORDMARK[0] * pct / 100), round(WORDMARK[1] * pct / 100); img = Image.new("RGBA", (W, H), (0, 0, 0, 0)); p1 = 8 * pct / 100
    draw_wordmark(img, W / 2, (H - p1 * (1 + WM_GAP + WM_RATIO)) / 2, p1)
    return img

def back(pct):
    W, H = round(BACK[0] * pct / 100), round(BACK[1] * pct / 100); img = Image.new("RGBA", (W, H), (21, 18, 32, 255))
    hz = round(H * 0.80); g = Image.new("RGBA", (W, H)); ImageDraw.Draw(g).ellipse((-W * 0.1, hz - H * 0.18, W * 1.1, hz + H * 0.18), fill=(200, 40, 130, 70))
    img.alpha_composite(g.filter(ImageFilter.GaussianBlur(H * 0.06)))
    fl = Image.new("RGBA", (W, H - hz)); floor(fl, (0, 0, W, H - hz), (64, 20, 70), (255, 70, 170), pct, alpha=110); img.alpha_composite(fl, (0, hz))
    ImageDraw.Draw(img).line([(0, hz), (W, hz)], fill=(255, 110, 190, 255), width=max(1, pct // 100))
    return img.convert("RGB")

def brand_wordmark():   # the website's 16 / 20 px wordmark, four times larger (its pixels land on whole pixels)
    img = Image.new("RGBA", (768, 184), (0, 0, 0, 0)); draw_wordmark(img, 384, 2, 64); return img

if __name__ == "__main__":
    brand_wordmark().save(os.path.join(HERE, "assets", "brand-wordmark.png"), optimize=True)
    os.makedirs(OUT, exist_ok=True); made = {}
    for pct in SCALES:
        for name, fn in (("wizard-panel", panel), ("wizard-wordmark", wordmark), ("wizard-back", back)):
            p = os.path.join(OUT, f"{name}-{pct}.png"); fn(pct).save(p, optimize=True); made[os.path.basename(p)] = os.path.getsize(p)
    json.dump({"schema": "ccs-installer-art/1", "source": "drawn in code by tools/publish/make_installer_art.py from the CCS cast, the app icon and the approved wordmark (Press Start 2P, OFL)",
               "files": made}, open(os.path.join(OUT, "index.json"), "w"), indent=1)
    print(f"{len(made)} files in {OUT}")
