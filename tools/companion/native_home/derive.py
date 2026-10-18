r"""Native HOME asset derivation (shared by the deployment packager and the development full-catalog exporter).

Everything the native renderer (tools/console-simulator/core) draws is derived here from the approved sources, never edited:
covers fitted whole for the largest box they are drawn in, Details backdrops pre-composed to the CSS geometry, the Arcade Grid
card trim taken from the registered master, the metadata icons, and baked font atlases (8-bit coverage) at the exact sizes
the design uses. Derivations are deterministic and cached by (source content hash, parameters, derivation version).

Fonts are console-redistributable (SIL Open Font License 1.1, licenses travel with every package): Barlow for all text the
browser HOME sets in Bahnschrift; the theme faces the approved designs use: Silkscreen (Arcade Grid labels, Featured Shelf
lettering), Cinzel (Box Art Showcase titles), VT323 (Featured Shelf text), Press Start 2P (Adventure Map labels) and Space Mono
(Adventure Map text). A handful of UI symbols (play triangle, stars, plus, magnifier, middle dot) are drawn as geometric glyphs,
so no Windows font is ever read. N3: all four themes are reachable on the console (theme switching), so the font list is the
union of the four designs; each atlas holds only the characters its role can draw (titles, uppercase lettering or all text).
"""
from __future__ import annotations
import hashlib, io, os, shutil, struct
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
FONTS_DIR = os.path.join(HERE, "fonts")
DERIVE_VERSION = "n3-2"
COVER_BOX = (340, 466)          # the Details cover box: the largest box a cover is drawn in by any theme (the Box Art Showcase focus box
                                # face is 281 x 369, Arcade card 274 x 291, Featured cells 106 x 145, Map panel 185 x 142): one cover serves all
HERO_WIDE = (620, 342)          # Featured Shelf hero box (home-art.json featured-shelf geometry hero.inner): the key art, object-fit: cover
DETAILS_ART = (1272, 712)       # the Details art area inside the Arcade Grid 4 px frame
FONT_MAGIC = b"CCSF2\0"
COVERAGE_EXP = float(os.environ.get("CCS_HOME_COVERAGE_EXP", "0.72"))
SANS_FILES = {400: "Barlow-Regular.ttf", 600: "Barlow-SemiBold.ttf", 700: "Barlow-Bold.ttf", 800: "Barlow-Bold.ttf"}   # 800 renders as 700 in the approved design
FAMILY_LABEL = {"sans": "Barlow", "silk": "Silkscreen", "sym": "geometric symbols", "cin": "Cinzel", "vt": "VT323", "px": "Press Start 2P", "mono": "Space Mono"}
# The approved layout's line metrics (the browser HOME's Windows font): baselines stay exactly where they were approved. Barlow has
# the same cap height (0.70 em) and x-height (0.51 em), so the glyphs sit identically on those baselines.
SANS_LAYOUT_METRICS = (0.7939453125, 0.2060546875, 0.2001953125)   # ascent, descent, line gap (em)
# and its word space (0.269 em; Barlow's own is 0.200 em): words keep the approved spacing, line widths and wrap points
SANS_SPACE_EM = 0.269

# (name, family, weight, px[, charset]): every face, weight and size the four HOME themes and their Details use (home.css).
# charset: "all" (UI text, titles, metadata, descriptions; the default), "titles" (UI text + titles), "upper" (uppercase UI + titles)
FONTS = [("silk9", "silk", 400, 9), ("silk11", "silk", 400, 11), ("silk14", "silk", 400, 14), ("silk17", "silk", 400, 17),
         ("sans400_10", "sans", 400, 10), ("sans700_14", "sans", 700, 14), ("sans600_14", "sans", 600, 14), ("sans800_15", "sans", 800, 15),
         ("sans800_10", "sans", 800, 10), ("sans800_40", "sans", 800, 40), ("sans700_15", "sans", 700, 15), ("sans400_15", "sans", 400, 15),
         ("sans700_16", "sans", 700, 16), ("sans400_16", "sans", 400, 16), ("sans400_17", "sans", 400, 17), ("sans400_19", "sans", 400, 19),
         ("sans400_20", "sans", 400, 20), ("sans700_13", "sans", 700, 13), ("sym16", "sym", 400, 16), ("sans400_14", "sans", 400, 14),
         # shared overlays: START menu (focused item bold), theme picker
         ("sans700_20", "sans", 700, 20), ("sans400_11", "sans", 400, 11), ("px11", "px", 400, 11, "titles"), ("sym13", "sym", 400, 13),
         # Box Art Showcase: Cinzel titles and spines, top navigation, facts, collection panel
         ("cin700_46", "cin", 700, 46, "titles"), ("cin700_20", "cin", 700, 20, "titles"), ("cin700_11", "cin", 700, 11, "titles"), ("cin700_8", "cin", 700, 8, "titles"),
         ("sans400_9", "sans", 400, 9), ("sans700_18", "sans", 700, 18), ("sans400_16b", "sans", 400, 16),
         # Featured Shelf: VT323 text, Silkscreen labels and the hero lettering (hmHeroFit: 46 down to 20 px in 2 px steps)
         ("vt26", "vt", 400, 26), ("vt23", "vt", 400, 23), ("vt21", "vt", 400, 21), ("vt19", "vt", 400, 19), ("vt18", "vt", 400, 18), ("vt17", "vt", 400, 17),
         ("silk10", "silk", 400, 10), ("silk12", "silk", 400, 12), ("silk13", "silk", 400, 13), ("silk25", "silk", 400, 25, "upper"), ("silk30", "silk", 400, 30, "titles")]
FONTS += [(f"silkh{px}", "silk", 400, px, "upper") for px in range(46, 18, -2)]
# title fitting (home-themes.js homeFitText: 6 % steps down to 62 % of the size, then an ellipsis natively)
FONTS += [("cin700_43", "cin", 700, 43, "titles"), ("cin700_40", "cin", 700, 40, "titles"), ("cin700_36", "cin", 700, 36, "titles"), ("cin700_32", "cin", 700, 32, "titles"), ("cin700_29", "cin", 700, 29, "titles"),
          ("silkt23", "silk", 400, 23, "upper"), ("silkt19", "silk", 400, 19, "upper"), ("silkt16", "silk", 400, 16, "upper"),
          ("px19", "px", 400, 19, "upper"), ("px17", "px", 400, 17, "upper"), ("px15", "px", 400, 15, "upper"), ("sym24", "sym", 400, 24)]
FONTS += [   # Adventure Map: Press Start 2P labels, Space Mono text; the rpg START menu
         ("px9", "px", 400, 9, "titles"), ("px8", "px", 400, 8, "titles"), ("px7", "px", 400, 7, "titles"), ("px10", "px", 400, 10, "titles"), ("px13", "px", 400, 13, "titles"),
         ("px21", "px", 400, 21, "upper"), ("px22", "px", 400, 22, "titles"),
         ("mono14", "mono", 400, 14), ("mono16", "mono", 400, 16), ("mono700_16", "mono", 700, 16), ("mono19", "mono", 400, 19), ("mono20", "mono", 400, 20), ("mono700_13", "mono", 700, 13)]
FONTS += [("sans600_15", "sans", 600, 15), ("sans400_12", "sans", 400, 12), ("cin700_16", "cin", 700, 16, "titles")]   # Browse (N3.1): tile names and counts, the Box Art Showcase panel heading
FONT_NAMES = [f[0] for f in FONTS]
SYMBOLS = "▶☆★✚⌕・◷"   # ▶ ☆ ★ ✚ ⌕ ・ ◷ drawn geometrically
UI_TEXT = ("AUTOMATIC CATEGORIES Browse › folder folders game games genre year publisher category No folders yet. Create folders in Classic Cart Studio for Windows; automatic categories appear once installed games carry metadata. This folder is empty. Add games using Classic Cart Studio for Windows. No installed games match this category. "
           "ALL GAMES BROWSE SEARCH FAVORITES RECENT SETTINGS GAMES FOLDERS Launch Details Menu Back Play / Favorite Move Scroll Open "
           "Return to Games Change Theme Sort Games All Games Recently Played MENU CHANGE THEME A applies at once current Arcade Grid Box Art Showcase "
           "Featured Shelf Adventure Map Your Collection Games Folders Favorites Recent of OF YEAR PLAYERS GENRE PUBLISHER version Navigate Travel Enter "
           "Destinations Choose Close Custom description CUSTOM DESCRIPTION Official description (edited) OFFICIAL DESCRIPTION (EDITED) WHERE TO? No folder set "
           "players player Theme: "
           "Back to games SELECT START A B Official description Game overview OFFICIAL DESCRIPTION GAME OVERVIEW Version Year Players Genre "
           "Publisher Developer Play Game Add Favorite Remove Favorite No verified official description available. "
           "No game overview available. No further details are available for this game. No description is available for this game. "
           "· … 0123456789/ LAUNCHING IS NOT AVAILABLE IN THIS PROTOTYPE NOT YET AVAILABLE Press any button to return. "
           "would start now on the console. This native HOME prototype does not launch games yet; nothing was run. "
           "is not part of this native HOME prototype yet. The START menu Favorites Browse Search Recently Played Settings Details for this game "
           + SYMBOLS)

def sha256_bytes(b: bytes) -> str: return hashlib.sha256(b).hexdigest()
def sha256_file(p: str) -> str:
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""): h.update(b)
    return h.hexdigest()

# ---------- cache: derived files keyed by source content + parameters ----------
class Cache:
    """content-addressed derivation cache; entries are only ever read back by key, so a stale or unrelated cache file can never
    reach a package unless the current configuration asks for exactly that derivation"""
    def __init__(self, root: str): self.root = root; os.makedirs(root, exist_ok=True); self.hits = self.misses = 0
    def path(self, kind: str, key: str, ext: str) -> str: return os.path.join(self.root, kind, key[:2], key + ext)
    def get(self, kind: str, src_hash: str, params: str, ext: str, make) -> str:
        key = sha256_bytes(f"{DERIVE_VERSION}|{kind}|{params}|{src_hash}".encode())[:40]; p = self.path(kind, key, ext)
        if os.path.isfile(p): self.hits += 1; return p
        self.misses += 1; os.makedirs(os.path.dirname(p), exist_ok=True); tmp = p[:-len(ext)] + ".tmp" + ext; make(tmp); os.replace(tmp, p); return p

# ---------- images ----------
def save_jpeg(im: Image.Image, out: str, q: int = 85):   # baseline 4:2:0: compact, decoded by the native runtime's stb_image (scalar paths)
    im.convert("RGB").save(out, "JPEG", quality=q, optimize=False, progressive=False, subsampling=2)

def cover(src: str, out: str):
    """the whole cover, fitted (contain) into the Details box and never enlarged; the grid cell and the card draw it smaller"""
    im = Image.open(src).convert("RGB"); w, h = im.size; s = min(COVER_BOX[0] / w, COVER_BOX[1] / h)
    if s < 1: im = im.resize((max(1, round(w * s)), max(1, round(h * s))), Image.LANCZOS)
    save_jpeg(im, out)

def hero(src: str, kind: str, out: str):
    """home.css .details .dbg img (object-fit: cover): the fallback fills the art area; a game's own hero is enlarged and shifted
    right (left 10 %, width 128 %, top -9 %, height 118 %) so its subject clears the cover; nearest-neighbor like the theme"""
    W, H = DETAILS_ART; im = Image.open(src).convert("RGB"); sw, sh = im.size
    bx, by, bw, bh = (0.10 * W, -0.09 * H, 1.28 * W, 1.18 * H) if kind == "hero" else (0.0, 0.0, float(W), float(H))
    s = max(bw / sw, bh / sh); rw, rh = sw * s, sh * s; x = bx + (bw - rw) / 2; y = by + (bh - rh) / 2
    canvas = Image.new("RGB", (W, H), (7, 7, 10)); canvas.paste(im.resize((max(1, round(rw)), max(1, round(rh))), Image.NEAREST), (int(round(x)), int(round(y))))
    save_jpeg(canvas, out)

def card_trim(ref_path: str, geometry: dict, out: str):
    """pixels of the registered master the frame sprite's cover hole removed: the lower bracket arms, the right neon edge and the
    caption plate border (zones from the measured boxes; over the cover only the frame's own neon pixels)"""
    ref = Image.open(ref_path).convert("RGB")
    if ref.size != (1280, 720): ref = ref.resize((1280, 720), Image.LANCZOS)
    fr, cv, cp = geometry["card"]["frame"], geometry["card"]["cover"], geometry["card"]["caption"]
    zones = [(fr[0], cv[3] - 6, cv[0] + 8, cp[1] + 2), (cv[2] - 8, cv[3] - 6, fr[2], cp[1] + 2),
             (cp[0] - 4, cp[1] + 2, cp[0] + 4, cp[3] + 3), (cp[2] - 2, cp[1] + 2, cp[2] + 6, cp[3] + 3), (cp[0] - 4, cp[3] - 3, cp[2] + 6, cp[3] + 3),
             (cv[2] - 2, fr[1], fr[2], cv[3])]
    trim = Image.new("RGBA", (fr[2] - fr[0], fr[3] - fr[1]), (0, 0, 0, 0))
    def neon(px): r, g, b = px; return r >= 110 and r > b + 25 or (r + g + b) >= 600
    for zi, (x0, y0, x1, y1) in enumerate(zones):
        x0, y0, x1, y1 = max(x0, fr[0]), max(y0, fr[1]), min(x1, fr[2]), min(y1, fr[3])
        for y in range(y0, y1):
            for x in range(x0, x1):
                px = ref.getpixel((x, y))
                if (zi in (2, 3, 4) or x < cv[0] or x >= cv[2] + 2) or neon(px): trim.putpixel((x - fr[0], y - fr[1]), px + (255,))
    trim.save(out, optimize=True)

ICON_SIZES = {"year": (18, 24, 26), "players": (18, 24, 26), "genre": (18, 24, 26), "publisher": (18, 26), "developer": (18,), "games": (22,), "folders": (22,), "favorites": (22,), "recent": (22,)}

def meta_icon(kind: str, out: str, size: int = 18):
    """home-themes.js hmIcon paths (24-unit viewBox, stroke 1.8, #e8e6e0)"""
    S = 8; k = size * S / 24.0; w = max(1, int(round(1.8 * k)))
    im = Image.new("L", (size * S, size * S), 0); d = ImageDraw.Draw(im)
    P = lambda *xy: [(x * k, y * k) for x, y in zip(xy[::2], xy[1::2])]
    line = lambda *xy: d.line(P(*xy), fill=255, width=w, joint="curve")
    if kind == "year": d.rounded_rectangle([3 * k, 5 * k, 21 * k, 21 * k], radius=2 * k, outline=255, width=w); line(3, 10, 21, 10); line(8, 3, 8, 7); line(16, 3, 16, 7)
    elif kind == "players":
        d.ellipse([5.5 * k, 4.5 * k, 12.5 * k, 11.5 * k], outline=255, width=w); d.ellipse([14 * k, 6 * k, 20 * k, 12 * k], outline=255, width=w)
        d.arc([2 * k, 14 * k, 16 * k, 26 * k], 180, 360, fill=255, width=w); d.arc([14 * k, 15 * k, 24 * k, 25 * k], 180, 360, fill=255, width=w)
    elif kind == "genre":
        d.rounded_rectangle([2 * k, 7 * k, 22 * k, 18 * k], radius=5 * k, outline=255, width=w); line(7, 11, 7, 15); line(5, 13, 9, 13)
        for cx, cy in ((16, 12), (18, 14)): d.ellipse([cx * k - w / 2, cy * k - w / 2, cx * k + w / 2, cy * k + w / 2], fill=255)
    elif kind == "publisher": d.rounded_rectangle([4 * k, 3 * k, 20 * k, 21 * k], radius=1 * k, outline=255, width=w); line(8, 7, 16, 7); line(8, 11, 16, 11); line(8, 15, 13, 15)
    elif kind == "developer": line(8, 8, 4, 12, 8, 16); line(16, 8, 20, 12, 16, 16)
    elif kind == "games":   # hmIcon games = the genre gamepad
        d.rounded_rectangle([2 * k, 7 * k, 22 * k, 18 * k], radius=5 * k, outline=255, width=w); line(7, 11, 7, 15); line(5, 13, 9, 13)
        for cx, cy in ((16, 12), (18, 14)): d.ellipse([cx * k - w / 2, cy * k - w / 2, cx * k + w / 2, cy * k + w / 2], fill=255)
    elif kind == "folders": line(3, 6, 9, 6, 11, 8, 21, 8, 21, 19, 3, 19, 3, 6)
    elif kind == "favorites": line(12, 3, 14.8, 8.7, 21, 9.6, 16.5, 14, 17.6, 20.2, 12, 17.3, 6.4, 20.2, 7.5, 14, 3, 9.6, 9.2, 8.7, 12, 3)
    elif kind == "recent": d.ellipse([3.5 * k, 3.5 * k, 20.5 * k, 20.5 * k], outline=255, width=w); line(12, 7, 12, 12, 15.5, 14)
    rgba = Image.new("RGBA", (size, size), (0xe8, 0xe6, 0xe0, 0)); rgba.putalpha(im.resize((size, size), Image.LANCZOS)); rgba.save(out, optimize=True)

# ---------- fonts ----------
def press_start_path(theme_root: str) -> str:
    """Press Start 2P (OFL 1.1) ships with the site brand fonts (brand/fonts), next to the HOME assets (brand/home)"""
    site = os.environ.get("NES_STUDIO_SITE") or ""   # the site the backend serves (a release keeps the font in its bundled site)
    for p in (os.path.join(theme_root, "..", "fonts", "PressStart2P-Regular.ttf"), os.path.join(theme_root, "..", "..", "..", "..", "reports", "brand", "fonts", "PressStart2P-Regular.ttf"),
              os.path.join(HERE, "..", "..", "..", "reports", "brand", "fonts", "PressStart2P-Regular.ttf"),
              os.path.join(site, "brand", "fonts", "PressStart2P-Regular.ttf") if site else "",
              os.path.join(HERE, "..", "..", "..", "site-data", "brand", "fonts", "PressStart2P-Regular.ttf")):   # the public repository's copy
        if os.path.isfile(p): return os.path.normpath(p)
    raise FileNotFoundError("Press Start 2P (brand/fonts/PressStart2P-Regular.ttf) was not found")

def family_path(family: str, weight: int, theme_root: str) -> str | None:
    if family == "silk": return os.path.join(theme_root, "fonts", "Silkscreen-Regular.ttf")
    if family == "sans": return os.path.join(FONTS_DIR, SANS_FILES[weight])
    if family == "cin": return os.path.join(theme_root, "fonts", "Cinzel.ttf")
    if family == "vt": return os.path.join(theme_root, "fonts", "VT323-Regular.ttf")
    if family == "mono": return os.path.join(theme_root, "fonts", "SpaceMono-Bold.ttf" if weight >= 700 else "SpaceMono-Regular.ttf")
    if family == "px": return press_start_path(theme_root)
    return None   # "sym": geometric glyphs only

def open_face(family: str, weight: int, px: int, theme_root: str):
    path = family_path(family, weight, theme_root)
    if not path: return None
    f = ImageFont.truetype(path, px)
    if family == "cin":
        try: f.set_variation_by_axes([weight])
        except Exception: pass
    return f

def _typo_metrics(path: str, px: int) -> tuple[int, int, int]:
    with open(path, "rb") as f: d = f.read()
    t = {}
    for i in range(struct.unpack(">H", d[4:6])[0]):
        tag, _cs, off, _ln = struct.unpack(">4sIII", d[12 + 16 * i:28 + 16 * i]); t[tag.decode("latin-1")] = off
    upm = struct.unpack(">H", d[t["head"] + 18:t["head"] + 20])[0]; o = t["OS/2"]
    ta, td, tg = struct.unpack(">hhh", d[o + 68:o + 74])
    if not struct.unpack(">H", d[o + 62:o + 64])[0] & 128: ta, td, tg = struct.unpack(">hhh", d[t["hhea"] + 4:t["hhea"] + 10])
    r = lambda v: int(v * px / upm + 0.5); return r(ta), r(-td), r(max(0, tg))

def layout_metrics(family: str, weight: int, px: int, theme_root: str) -> tuple[int, int, int]:
    if family == "sans": a, d, g = SANS_LAYOUT_METRICS; return int(a * px + 0.5), int(d * px + 0.5), int(g * px + 0.5)
    if family == "sym": return int(0.95 * px + 0.5), int(0.25 * px + 0.5), 0
    return _typo_metrics(family_path(family, weight, theme_root), px)

def _symbol(ch: str, px: int) -> tuple[Image.Image, int, int, float]:
    """a geometric glyph: (coverage image, x0, y0 relative to the baseline, advance)"""
    S = 8; em = px * S; im = Image.new("L", (em, em), 0); d = ImageDraw.Draw(im); cap = 0.70 * em; base = 0.85 * em; top = base - cap
    def star(filled):
        import math
        cx, cy, R, r = em * 0.5, top + cap * 0.52, cap * 0.6, cap * 0.24
        pts = [(cx + (R if i % 2 == 0 else r) * math.sin(math.pi * i / 5), cy - (R if i % 2 == 0 else r) * math.cos(math.pi * i / 5)) for i in range(10)]
        d.polygon(pts, fill=255 if filled else None, outline=255, width=max(1, int(0.08 * em)) if not filled else 1)
    if ch == "▶": d.polygon([(0.12 * em, top), (0.12 * em, base), (0.82 * em, (top + base) / 2)], fill=255)
    elif ch == "★": star(True)
    elif ch == "☆": star(False)
    elif ch == "✚": t = 0.20 * em; c = em / 2; m = top + cap / 2; d.rectangle([c - t / 2, m - cap * 0.5, c + t / 2, m + cap * 0.5], fill=255); d.rectangle([c - cap * 0.5, m - t / 2, c + cap * 0.5, m + t / 2], fill=255)
    elif ch == "⌕": w = max(1, int(0.10 * em)); r = cap * 0.32; cx, cy = em * 0.42, top + cap * 0.42; d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=255, width=w); d.line([(cx + r * 0.7, cy + r * 0.7), (cx + r * 1.6, cy + r * 1.6)], fill=255, width=w)
    elif ch == "・": r = 0.09 * em; d.ellipse([em / 2 - r, top + cap / 2 - r, em / 2 + r, top + cap / 2 + r], fill=255)
    elif ch == "◷":   # a clock face with the hand at the upper left quarter (Recent)
        w = max(1, int(0.09 * em)); r = cap * 0.5; cx, cy = em * 0.5, top + cap * 0.5; d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=255, width=w)
        d.line([(cx, cy), (cx, cy - r * 0.75)], fill=255, width=w); d.line([(cx, cy), (cx - r * 0.6, cy)], fill=255, width=w)
    small = im.resize((px, px), Image.LANCZOS); bb = small.getbbox() or (0, 0, 1, 1)
    return small.crop(bb), bb[0], bb[1] - int(0.85 * px), float(px) * (0.95 if ch != "・" else 0.5)

def _missing(font, ch: str) -> bool:
    try: a = font.getmask(ch); b = font.getmask(""); return a.size == b.size and bytes(a) == bytes(b)
    except Exception: return True

def bake_font(name: str, family: str, weight: int, px: int, chars: list[str], theme_root: str, out: str) -> dict:
    """one atlas: 8-bit coverage per glyph (with the coverage curve that matches the approved rendering), bearings, advances;
    characters the font lacks become geometric symbols (UI symbols) or are reported as missing (the runtime draws '?')"""
    main = open_face(family, weight, px, theme_root)
    asc, desc, gap = layout_metrics(family, weight, px, theme_root); glyphs = []; blob = bytearray(); synth = []; missing = []
    curve = bytes(int(255 * (v / 255) ** COVERAGE_EXP + 0.5) for v in range(256))
    for ch in sorted(set(chars)):
        if ch == " " and main is not None:   # the space has no ink, so the ink comparison below cannot tell it is present
            adv = SANS_SPACE_EM * px if family == "sans" else main.getlength(" "); glyphs.append((32, int(round(adv * 64)), 0, 0, 0, 0, len(blob))); continue
        if main is not None and not _missing(main, ch):
            adv = SANS_SPACE_EM * px if family == "sans" and ch == " " else main.getlength(ch); x0, y0, x1, y1 = main.getbbox(ch, anchor="ls"); w, h = max(0, x1 - x0), max(0, y1 - y0); off = len(blob)
            if w and h:
                im = Image.new("L", (w, h), 0); ImageDraw.Draw(im).text((-x0, -y0), ch, font=main, fill=255, anchor="ls"); blob += im.tobytes().translate(curve)
            glyphs.append((ord(ch), int(round(adv * 64)), x0, y0, w, h, off))
        elif ch in SYMBOLS:
            im, x0, y0, adv = _symbol(ch, px); off = len(blob); blob += im.tobytes(); glyphs.append((ord(ch), int(round(adv * 64)), x0, y0, im.width, im.height, off)); synth.append(ch)
        elif ch.strip(): missing.append(ch)
    if not any(g[0] == ord("?") for g in glyphs) and main is not None:   # the runtime's replacement glyph must exist
        adv = main.getlength("?"); x0, y0, x1, y1 = main.getbbox("?", anchor="ls"); im = Image.new("L", (x1 - x0, y1 - y0), 0); ImageDraw.Draw(im).text((-x0, -y0), "?", font=main, fill=255, anchor="ls")
        glyphs.append((ord("?"), int(round(adv * 64)), x0, y0, im.width, im.height, len(blob))); blob += im.tobytes().translate(curve)
    glyphs.sort(key=lambda g: g[0])
    buf = io.BytesIO(); buf.write(FONT_MAGIC); buf.write(struct.pack("<iiiiII", px, asc, desc, gap, len(glyphs), 0))
    for g in glyphs: buf.write(struct.pack("<IihhHHI", *g))
    buf.write(struct.pack("<I", len(blob))); buf.write(bytes(blob))
    with open(out, "wb") as f: f.write(buf.getvalue())
    return {"px": px, "family": FAMILY_LABEL[family], "weight": weight, "ascent": asc, "descent": desc, "line_gap": gap,
            "glyphs": len(glyphs), "synthesized": "".join(synth), "missing": "".join(missing)}

def font_licenses(theme_root: str) -> dict[str, str]:
    """license texts that travel with the baked fonts (OFL 1.1 requires the license with every redistribution)"""
    px = press_start_path(theme_root)
    return {"OFL-Barlow.txt": os.path.join(FONTS_DIR, "OFL-Barlow.txt"), "OFL-Silkscreen.txt": os.path.join(theme_root, "fonts", "OFL-Silkscreen.txt"),
            "OFL-Cinzel.txt": os.path.join(theme_root, "fonts", "OFL-Cinzel.txt"), "OFL-VT323.txt": os.path.join(theme_root, "fonts", "OFL-VT323.txt"),
            "OFL-SpaceMono.txt": os.path.join(theme_root, "fonts", "OFL-SpaceMono.txt"), "OFL-PressStart2P.txt": os.path.join(os.path.dirname(px), "OFL-PressStart2P.txt")}

def hero_wide(src: str, out: str):
    """Featured Shelf hero (home.css .hero img.wide: object-fit: cover over the 620 x 342 hero box), smooth scaling like the browser"""
    W, H = HERO_WIDE; im = Image.open(src).convert("RGB"); sw, sh = im.size; s = max(W / sw, H / sh)
    rw, rh = max(W, round(sw * s)), max(H, round(sh * s)); im = im.resize((rw, rh), Image.LANCZOS); x, y = (rw - W) // 2, (rh - H) // 2
    save_jpeg(im.crop((x, y, x + W, y + H)), out, q=88)

FOLDER_TILE = (272, 74)        # a Browse tile's artwork band (home.css .tile .tcol, 72 px high, up to 266 px wide in the widest theme panel), object-fit: cover
def folder_tile(src: str, out: str):
    """a folder's custom artwork as its Browse tile image: center crop to the tile band (the runtime fits it to each theme's tile)"""
    W, H = FOLDER_TILE; im = Image.open(src).convert("RGB"); sw, sh = im.size; s = max(W / sw, H / sh)
    rw, rh = max(W, round(sw * s)), max(H, round(sh * s)); im = im.resize((rw, rh), Image.LANCZOS); x, y = (rw - W) // 2, (rh - H) // 2
    save_jpeg(im.crop((x, y, x + W, y + H)), out, q=86)

def thumb(src: str, out: str):
    """theme picker thumbnails: the approved 320 x 180 thumbs, re-encoded as baseline JPEG for the runtime decoder"""
    save_jpeg(Image.open(src).convert("RGB"), out, q=88)

def user_cover(src: str, crop: dict | None, out: str):
    """a user's replacement artwork: EXIF orientation applied, the nondestructive crop (normalized rectangle) applied, then fitted
    whole into the cover box like every cover (never enlarged, never cut)"""
    from PIL import ImageOps
    im = ImageOps.exif_transpose(Image.open(src)); im = im.convert("RGBA") if im.mode in ("P", "LA", "RGBA") else im.convert("RGB")
    if im.mode == "RGBA":   # transparency becomes the box's dark ground: the console's covers are opaque JPEG
        bg = Image.new("RGB", im.size, (16, 16, 22)); bg.paste(im, mask=im.split()[3]); im = bg
    if crop:
        w, h = im.size; x0 = max(0, min(w - 1, round(crop["x"] * w))); y0 = max(0, min(h - 1, round(crop["y"] * h)))
        x1 = max(x0 + 1, min(w, round((crop["x"] + crop["w"]) * w))); y1 = max(y0 + 1, min(h, round((crop["y"] + crop["h"]) * h))); im = im.crop((x0, y0, x1, y1))
    w, h = im.size; s = min(COVER_BOX[0] / w, COVER_BOX[1] / h)
    if s < 1: im = im.resize((max(1, round(w * s)), max(1, round(h * s))), Image.LANCZOS)
    save_jpeg(im, out)

def mock(title: str, publisher: str | None, year: str | None, game_id: str, out: str, theme_root: str | None = None):
    """the NES-style mock cover (tools/rom-pipeline/mock_cover.py, the approved generated-placeholder design) for one game"""
    import sys
    rp = os.path.normpath(os.path.join(HERE, "..", "..", "rom-pipeline"))
    if rp not in sys.path: sys.path.insert(0, rp)
    import mock_cover
    if theme_root and not (mock_cover.FONT_PX and os.path.isfile(mock_cover.FONT_PX)): mock_cover.FONT_PX = press_start_path(theme_root)   # installed app: the site's brand fonts
    if not (mock_cover.FONT_UI and os.path.isfile(mock_cover.FONT_UI)): mock_cover.FONT_UI = os.path.join(FONTS_DIR, "Barlow-Regular.ttf")
    im = mock_cover.cover(title, publisher, year, game_id); w, h = im.size; s = min(COVER_BOX[0] / w, COVER_BOX[1] / h)
    if s < 1: im = im.resize((round(w * s), round(h * s)), Image.LANCZOS)
    save_jpeg(im, out, q=90)

def copy(src: str, out: str): shutil.copyfile(src, out)
