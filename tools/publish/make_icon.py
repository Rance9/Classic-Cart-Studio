"""Classic Cart Studio and CCS Console Workbench application icons, drawn by this code from no input file.

  Classic Cart Studio: an upright gray cartridge, slightly taller than wide, with notches at both bottom corners, a column of ridges at the
  left, a red label over about two thirds of the width with a near-white stripe near its top (and two short pale lines at detailed sizes),
  and a shallow recessed strip below the label with a small downward triangle at its left.
  CCS Console Workbench: one open-end wrench at 45 degrees, jaw at the upper right, handle at the lower left, the lower part of the handle
  in a red grip.
  Neither contains lettering, a controller or a wordmark.

Each size is drawn for itself, not scaled from another: 16, 20 and 24 px in flat colors on whole pixels (the cartridge with four or five
thick ridges and no small lines, a one-pixel darker edge so the gray holds on a light bar); 32 to 64 px with soft shading and medium detail;
256 px and the web sizes in full detail. Drawing is done on a grid 8 times finer and reduced by area, so the same Pillow version
(tools/publish/requirements-build.txt) writes the same bytes; nothing depends on the time, randomness or a path.

  python tools/publish/make_icon.py          writes into tools/publish/assets: app.ico (Classic Cart Studio, Windows), workbench.ico
      (CCS Console Workbench, Windows), app-16.png, app-32.png, app-256.png, favicon.ico, icon-192.png, icon-512.png,
      icon-maskable-512.png, apple-touch-icon.png (180), apple-touch-icon-167.png, apple-touch-icon-152.png, app-icon-sheet.png and
      app-icon-sheet-light.png (both marks at their sizes), workbench-20.png and workbench-40.png (the wrench the Studio page shows on
      its Console Workbench button and menu entry, at 20 px and for 2x displays); and the icon pictures in
      tools/desktop/ClassicCartStudio/Loading.html and Recovery.html
  python tools/publish/make_icon.py --check  exit 1 when any of those differs from what this code draws"""
from __future__ import annotations
import base64, hashlib, io, math, os, re, struct, sys
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__)); ASSETS = os.path.join(HERE, "assets")
DESKTOP = os.path.normpath(os.path.join(HERE, "..", "desktop", "ClassicCartStudio"))
K = 8                                        # drawing grid: 8 x the output size, reduced by area
ICO_SIZES = (16, 20, 24, 32, 40, 48, 64, 256)
WEB_BG = (16, 16, 20, 255)                   # #101014: the website's and installed web app's own background (manifest background_color)

GRAY0, GRAY1 = (0xb0, 0xb3, 0xba), (0x8a, 0x8d, 0x95)   # body / metal, top to bottom
RED0, RED1 = (0xe6, 0x48, 0x3c), (0xc1, 0x2d, 0x23)     # label / grip, top to bottom
INK = (0x3f, 0x42, 0x4a)                                 # ridges, recess and triangle (at partial strength)
WHITE = (255, 255, 255)

# ------------------------------------------------------------------ drawing helpers (big canvas, then reduce)
def blank(s: int) -> Image.Image: return Image.new("RGBA", (s, s), (0, 0, 0, 0))
def mask(s: int) -> Image.Image: return Image.new("L", (s, s), 0)

def vgrad(s: int, y0: float, y1: float, c0, c1) -> Image.Image:
    """an s x s picture whose rows go from c0 at y0 to c1 at y1 (clamped)"""
    col = Image.new("RGBA", (1, s)); span = max(1e-6, y1 - y0)
    col.putdata([tuple(int(round(a + (b - a) * min(1.0, max(0.0, (y + 0.5 - y0) / span)))) for a, b in zip(c0, c1)) + (255,) for y in range(s)])
    return col.resize((s, s), Image.NEAREST)

def fill(canvas: Image.Image, m: Image.Image, paint, opacity: float = 1.0) -> None:
    """paint (a color or a picture) through the mask m at the given strength, over what is already there"""
    s = canvas.size[0]
    src = Image.new("RGBA", (s, s), tuple(paint) + (255,)) if isinstance(paint, tuple) else paint.copy()
    a = m if opacity >= 1.0 else m.point(lambda v: int(v * opacity + 0.5))
    src.putalpha(a); canvas.alpha_composite(src)

def rrect(m: Image.Image, box, r: float, val: int = 255) -> None:
    x0, y0, x1, y1 = (int(round(v)) for v in box); ImageDraw.Draw(m).rounded_rectangle([x0, y0, x1 - 1, y1 - 1], radius=max(0, int(round(r))), fill=val)

def rect(m: Image.Image, box, val: int = 255) -> None:
    x0, y0, x1, y1 = (int(round(v)) for v in box); ImageDraw.Draw(m).rectangle([x0, y0, x1 - 1, y1 - 1], fill=val)

def poly(m: Image.Image, pts, val: int = 255) -> None: ImageDraw.Draw(m).polygon([(round(x, 3), round(y, 3)) for x, y in pts], fill=val)

def reduce(big: Image.Image, k: int = K) -> Image.Image:
    return big.convert("RGBa").reduce(k).convert("RGBA")   # area average of premultiplied color: no dark fringes at the edges

# ------------------------------------------------------------------ the cartridge (Classic Cart Studio)
def cartridge_detailed(n: int, ridges: int, pale_lines: bool) -> Image.Image:
    """32 px and up: shaded body, ridges, label, stripe, recess and triangle (64-unit drawing, as the approved sketch)"""
    s = n * K; u = s / 64.0; U = lambda *v: [x * u for x in v]
    im = blank(s)
    body = mask(s); rrect(body, U(9, 6, 55, 54), 2 * u); rect(body, U(9, 50, 55, 54)); rect(body, U(11, 53.9, 53, 58))
    fill(im, body, vgrad(s, 6 * u, 58 * u, GRAY0, GRAY1))
    top = mask(s); rrect(top, U(9, 6, 55, 9.5), 2 * u); rect(top, U(9, 7.5, 55, 9.5), 0); top = Image.composite(top, mask(s), body)
    fill(im, top, WHITE, 0.28)
    y0, y1 = 12.0, 41.28                       # the ridges fill the label's height, as in the sketches (9 thin, or fewer and thicker)
    h = 1.72 if ridges >= 9 else (y1 - y0) / (ridges * 2 - 1) * 1.1
    step = (y1 - y0 - h) / (ridges - 1)
    rid = mask(s)
    for i in range(ridges): rrect(rid, U(12.5, y0 + i * step, 23, y0 + i * step + h), 0.6 * u)
    fill(im, rid, INK, 0.5)
    lab = mask(s); rrect(lab, U(26, 10, 52.5, 44), 1.5 * u); fill(im, lab, vgrad(s, 10 * u, 44 * u, RED0, RED1))
    stripe = mask(s); rect(stripe, U(26, 16, 52.5, 23)); fill(im, stripe, WHITE, 0.88)
    if pale_lines:
        pl = mask(s); rrect(pl, U(29, 36, 43, 38), u); rrect(pl, U(29, 39.5, 38, 41.5), u); fill(im, pl, WHITE, 0.75)
    rec = mask(s); rrect(rec, U(12.5, 47, 52.5, 54.5), u); fill(im, rec, INK, 0.28)
    tri = mask(s); poly(tri, [(14.5 * u, 48.5 * u), (22.5 * u, 48.5 * u), (18.5 * u, 53.3 * u)]); fill(im, tri, INK, 0.7)
    return reduce(im)

def cartridge_small(n: int) -> Image.Image:
    """16, 20 and 24 px: flat colors on whole pixels, the simplified cartridge (four or five thick ridges, red block, pale stripe, no small
    lines), a one-pixel darker edge so the gray holds on a light bar"""
    u = n / 64.0; P = lambda v: int(math.floor(v * u + 0.5))
    EDGE, BODY, HI, RIDGE, REC, ARROW = (0x6c, 0x6f, 0x77), (0xa2, 0xa5, 0xac), (0xc2, 0xc4, 0xca), (0x6a, 0x6d, 0x75), (0x86, 0x89, 0x91), (0x55, 0x58, 0x60)
    LAB, LAB_LO, STRIPE = (0xd6, 0x3d, 0x32), (0xbd, 0x2c, 0x22), (0xf7, 0xe9, 0xe7)
    im = blank(n); px = im.load()
    def put(x, y, c): px[x, y] = tuple(c) + (255,)
    x0, x1, y0, y1 = P(9), P(55) - 1, P(6), P(58) - 1; notch = P(54)
    body = {(x, y) for y in range(y0, y1 + 1) for x in (range(x0 + 1, x1) if y >= notch else range(x0, x1 + 1))} - {(x0, y0), (x1, y0)}   # notches, rounded top corners
    for x, y in body:
        edge = any((x + dx, y + dy) not in body for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
        put(x, y, EDGE if edge else (HI if y == y0 + 1 else BODY))
    lx0, lx1, ly0, ly1 = P(26), x1 - 1, P(10), P(44) - 1                # the label reaches the right edge's inner column
    for y in range(ly0, ly1 + 1):
        for x in range(lx0, lx1 + 1): put(x, y, LAB_LO if y == ly1 else LAB)
    for y in range(P(16), max(P(16) + 1, P(23))):
        for x in range(lx0, lx1 + 1): put(x, y, STRIPE)
    rx0, rx1 = x0 + 1, lx0 - 2                                            # the ridges: whole rows of one pixel, evenly over the label's height
    count = 4 if n < 20 else 5; pitch = (ly1 - ly0) // (count - 1)
    for i in range(count):
        for x in range(rx0, rx1 + 1): put(x, ly0 + i * pitch, RIDGE)
    ry0, ry1 = P(47), y1 - 1
    for y in range(ry0, ry1 + 1):
        for x in range(x0 + 1, x1): put(x, y, REC)
    ax = x0 + 1                                                           # the small down arrow at the recess's left: 3 over 1 pixels
    for x in range(ax, ax + 3): put(x, ry0, ARROW)
    put(ax + 1, ry0 + 1, ARROW)
    return im

def cartridge(n: int) -> Image.Image:
    if n <= 24: return cartridge_small(n)
    if n <= 40: return cartridge_detailed(n, 5, False)
    if n <= 64: return cartridge_detailed(n, 7, n >= 48)
    return cartridge_detailed(n, 9, True)

# ------------------------------------------------------------------ the wrench (CCS Console Workbench)
def _rot(pts, ang_deg, cx, cy):
    a = math.radians(ang_deg); c, s_ = math.cos(a), math.sin(a)
    return [(cx + (x - cx) * c - (y - cy) * s_, cy + (x - cx) * s_ + (y - cy) * c) for x, y in pts]

def _arc(cx, cy, r, a0, a1, steps=48):
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / steps)), cy + r * math.sin(math.radians(a0 + (a1 - a0) * i / steps))) for i in range(steps + 1)]

# the wrench as the approved sketch draws it (64 units): head radius, jaw width and depth, handle width at the head and at the end
FULL = dict(head_r=12.5, jaw=10.0, floor=18.5, dip=3.5, w0=8.0, w1=10.0, zoom=1.0)
# 16, 20 and 24 px (drawn for themselves): a little larger in the frame, a thicker head, a narrower and shallower jaw and a wider handle,
# so the jaw stays open and the head and handle stay at least two pixels wide
SMALL = dict(head_r=14.0, jaw=7.0, floor=14.5, dip=2.0, w0=10.0, w1=12.0, zoom=1.18)

def wrench_shapes(f=FULL):
    """the wrench's parts in its own upright 64-unit frame (head up)"""
    h0, h1 = f["w0"] / 2, f["w1"] / 2; r_end = h1
    handle = [(32 - h0, 25.5), (32 + h0, 25.5), (32 + h1, 52.0)] + _arc(32.0, 52.0, r_end, 0, 180)[1:-1] + [(32 - h1, 52.0)]
    g0, g1 = h0 + (h1 - h0) * (38.73 - 25.5) / (52 - 25.5) + 0.72, h1 + 0.8
    grip = [(32 - g0, 38.73), (32 + g0, 38.73), (32 + g1, 52.0)] + _arc(32.0, 52.0, r_end + 0.8, 0, 180)[1:-1] + [(32 - g1, 52.0)]
    # the head: a circle around (32, 17) with an open jaw from its top down to a curved floor, turned 15 degrees
    circle = _arc(32.0, 17.0, f["head_r"], 0, 360, 96); j = f["jaw"] / 2; fl = f["floor"]; dp = f["dip"]
    q = lambda t: ((1 - t) ** 2 * (32 + j) + t * t * (32 - j) + 2 * (1 - t) * t * 32, (1 - t) ** 2 * fl + 2 * (1 - t) * t * (fl + dp) + t * t * fl)   # the jaw's floor
    slot = [(32 - j, -8.0), (32 + j, -8.0)] + [q(i / 16) for i in range(17)]
    head = (_rot(circle, 15, 32, 17), _rot(slot, 15, 32, 17))
    z = f["zoom"]; Z = lambda pts: [(32 + (x - 32) * z, 32 + (y - 32) * z) for x, y in pts]
    return Z(handle), Z(grip), (Z(head[0]), Z(head[1]))

def wrench_detailed(n: int, f=FULL) -> Image.Image:
    s = n * K; u = s / 64.0; turn = 45
    handle, grip, (circle, slot) = wrench_shapes(f)
    T = lambda pts: [(x * u, y * u) for x, y in _rot(pts, turn, 32, 32)]
    # shading along the wrench's own axis: lighter at the head, darker toward the end of the handle
    def axial(y0, y1, c0, c1):
        g = vgrad(s, y0 * u, y1 * u, c0, c1)
        return g.rotate(-turn, resample=Image.BICUBIC, center=(32 * u, 32 * u), fillcolor=tuple(c1) + (255,))
    im = blank(s)
    m = mask(s); poly(m, T(handle)); fill(im, m, axial(25.5, 57.0, GRAY0, GRAY1))
    m = mask(s); poly(m, T(grip)); fill(im, m, axial(38.73, 57.8, RED0, RED1))
    m = mask(s); poly(m, T(circle)); poly(m, T(slot), 0); fill(im, m, axial(4.5, 29.5, GRAY0, GRAY1))
    return reduce(im)

def wrench_small(n: int) -> Image.Image:
    """16, 20 and 24 px: the drawing on whole pixels (a pixel is in when more than half of it is), flat gray and red, the gray's outer
    pixels on the lower and right side one shade darker so it holds on a light bar"""
    big = wrench_detailed(n, SMALL)
    EDGE, METAL, HI, RED, RED_LO = (0x6c, 0x6f, 0x77), (0xa2, 0xa5, 0xac), (0xc2, 0xc4, 0xca), (0xd6, 0x3d, 0x32), (0xb4, 0x2a, 0x21)
    im = blank(n); src = big.load(); px = im.load()
    inside = lambda x, y: 0 <= x < n and 0 <= y < n and src[x, y][3] >= 128
    for y in range(n):
        for x in range(n):
            if not inside(x, y): continue
            r, g, b, _ = src[x, y]; red = r > g + 40
            lower_right = not inside(x + 1, y) or not inside(x, y + 1)
            upper_left = not inside(x - 1, y) or not inside(x, y - 1)
            if red: px[x, y] = (RED_LO if lower_right else RED) + (255,)
            else: px[x, y] = (EDGE if lower_right else (HI if upper_left else METAL)) + (255,)
    return im

def wrench(n: int) -> Image.Image:
    return wrench_small(n) if n <= 24 else wrench_detailed(n)

# ------------------------------------------------------------------ files
def png_bytes(im: Image.Image) -> bytes:
    buf = io.BytesIO(); im.save(buf, format="PNG", optimize=True); return buf.getvalue()

def ico_bytes(frames) -> bytes:
    """an .ico holding one PNG frame per size (as Windows reads them since Vista; the previous icon file was built the same way)"""
    data = [png_bytes(f) for f in frames]; out = struct.pack("<HHH", 0, 1, len(frames)); off = 6 + 16 * len(frames)
    for f, d in zip(frames, data):
        w = f.width if f.width < 256 else 0; out += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(d), off); off += len(d)
    return out + b"".join(data)

def on_background(mark_at, n: int, fit: float) -> Image.Image:
    """an opaque square in the website's background color with the mark drawn for the size that fits, centered"""
    im = Image.new("RGBA", (n, n), WEB_BG); m = mark_at(int(round(n * fit)))
    im.alpha_composite(m, ((n - m.width) // 2, (n - m.height) // 2)); return im

def maskable(n: int = 512) -> Image.Image:
    """the whole cartridge inside the centered safe circle (radius 40 % of the width) that every launcher's mask keeps"""
    half_diag_units = math.hypot(23.0, 26.0)        # the cartridge spans 46 x 52 of its 64 units, centered on (32, 32): half its diagonal
    size = int(math.floor(64 * (0.40 * n) / half_diag_units * 0.98))   # its 64-unit square in pixels, a hair inside the circle
    big = cartridge(size); im = Image.new("RGBA", (n, n), WEB_BG)
    im.alpha_composite(big, ((n - size) // 2, (n - size) // 2)); return im

def sheet(bg, fg) -> Image.Image:
    sizes = (16, 20, 24, 32, 40, 48, 64); W = 24 + sum(s + 18 for s in sizes) + 136; im = Image.new("RGBA", (W, 2 * 150), bg + (255,))
    for row, mark in enumerate((cartridge, wrench)):
        x = 16; y = row * 150 + 16
        for s_ in sizes: im.alpha_composite(mark(s_), (x, y + (128 - s_) // 2)); x += s_ + 18
        big = mark(256).resize((128, 128), Image.LANCZOS); im.alpha_composite(big, (x, y))
    return im

def outputs() -> dict:
    studio = {n: cartridge(n) for n in ICO_SIZES}; bench = {n: wrench(n) for n in ICO_SIZES}
    files = {
        "app.ico": ico_bytes([studio[n] for n in ICO_SIZES]),
        "workbench.ico": ico_bytes([bench[n] for n in ICO_SIZES]),
        "app-16.png": png_bytes(studio[16]), "app-32.png": png_bytes(studio[32]), "app-256.png": png_bytes(studio[256]),
        "workbench-20.png": png_bytes(bench[20]), "workbench-40.png": png_bytes(bench[40]),
        "favicon.ico": ico_bytes([studio[16], studio[32], studio[48]]),
        "icon-192.png": png_bytes(cartridge(192)), "icon-512.png": png_bytes(cartridge(512)),
        "icon-maskable-512.png": png_bytes(maskable(512)),
        "apple-touch-icon.png": png_bytes(on_background(cartridge, 180, 0.86)),
        "apple-touch-icon-167.png": png_bytes(on_background(cartridge, 167, 0.86)),
        "apple-touch-icon-152.png": png_bytes(on_background(cartridge, 152, 0.86)),
        "app-icon-sheet.png": png_bytes(sheet((0x20, 0x20, 0x26), None)),
        "app-icon-sheet-light.png": png_bytes(sheet((0xf3, 0xf3, 0xf3), None)),
    }
    return {os.path.join(ASSETS, k): v for k, v in files.items()} | pages(studio)

def pages(studio) -> dict:
    """the two pages Classic Cart Studio for Windows shows before its interface (embedded resources): Loading.html's icon (256 px) and
    Recovery.html's mark beside the name (56 px, shown at 28)"""
    out = {}
    p = os.path.join(DESKTOP, "Loading.html"); t = open(p, encoding="utf-8", newline="").read()
    b64 = base64.b64encode(png_bytes(studio[256])).decode()
    t2, k = re.subn(r'(<img class="i" alt="" src="data:image/png;base64,)[A-Za-z0-9+/=]+(")', lambda m: m.group(1) + b64 + m.group(2), t)
    if k != 1: raise SystemExit("Loading.html: the icon picture was not found exactly once")
    out[p] = t2.encode("utf-8")
    p = os.path.join(DESKTOP, "Recovery.html"); t = open(p, encoding="utf-8", newline="").read()
    b64 = base64.b64encode(png_bytes(cartridge(56))).decode()
    pat = r'(<div class="brand">)(?:<svg viewBox="0 0 28 28" aria-hidden="true">.*?</svg>|<img class="mark" alt="" width="28" height="28" src="data:image/png;base64,[A-Za-z0-9+/=]+">)'
    t2, k = re.subn(pat, lambda m: m.group(1) + f'<img class="mark" alt="" width="28" height="28" src="data:image/png;base64,{b64}">', t, flags=re.S)
    if k != 1: raise SystemExit("Recovery.html: the mark beside the name was not found exactly once")
    out[p] = t2.encode("utf-8")
    return out

def main() -> int:
    check = "--check" in sys.argv[1:]; bad = []
    for path, data in outputs().items():
        old = open(path, "rb").read() if os.path.exists(path) else None
        if check:
            if old != data: bad.append(os.path.relpath(path, os.path.dirname(HERE)))
        elif old != data:
            with open(path, "wb") as f: f.write(data)
        print(f"{hashlib.sha256(data).hexdigest()}  {len(data):>8}  {os.path.relpath(path, os.path.normpath(os.path.join(HERE, '..', '..')))}")
    if bad: print("differs from what make_icon.py draws: " + ", ".join(bad)); return 1
    return 0

if __name__ == "__main__": sys.exit(main())
