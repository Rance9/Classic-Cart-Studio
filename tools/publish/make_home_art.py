"""Console HOME theme assets, reconstructed from the four approved master mockups (visual fidelity remediation, 2026-10-12).

The masters (1672 x 941, kept unmodified under _temp/images/master mockup HOME themes) are the visual authority. For each
theme this script produces, deterministically:

  scene-<theme>.png      the master scaled to the 1280 x 720 stage (lossless PNG), with ONLY the dynamic content removed:
                         fictional covers, titles, counts, legends, clock digits and sign labels are inpainted from their
                         own surroundings (OpenCV Telea, or a ring fill for flat panels) so the authored scenery, panels,
                         frames, lighting and ornament stay pixel-exact everywhere else. No blur-and-darken rectangles.
  ref-<theme>.png        the registered reference: the unaltered master at stage size (for the comparison packets)
  sprite-<theme>-*.png   authored UI pieces cut from the master with transparent interiors where the renderer draws the
                         live content: card frames, focus frames, the active tab plate, sign plates, icons, markers.
  home-art.json          the layer manifest: master hashes, the region taxonomy (STATIC PRESERVE / DYNAMIC RECREATE /
                         LEGEND CORRECTION) with every region's master-pixel box, and the geometry manifest in stage pixels
                         that home-themes.js positions against. UNRESOLVED ASSET entries are listed explicitly.

Coordinates below are MASTER pixels (measured on the masters with a grid); they are converted to stage pixels here.
  python make_home_art.py   -> tools/publish/assets/home/
"""
from __future__ import annotations
import os, json, hashlib
import numpy as np
from PIL import Image
import cv2
HERE = os.path.dirname(os.path.abspath(__file__)); PROJECT = os.path.dirname(os.path.dirname(HERE))
SRC = os.path.join(PROJECT, "_temp", "images", "master mockup HOME themes"); OUT = os.path.join(HERE, "assets", "home")
MW, MH, SW, SH = 1672, 941, 1280, 720; SX, SY = SW / MW, SH / MH
MAP = {"featured-shelf": "ChatGPT Image Oct 11, 2026, 08_49_16 PM-1.png", "arcade-grid": "ChatGPT Image Oct 11, 2026, 08_49_17 PM-2.png",
       "box-art-showcase": "ChatGPT Image Oct 11, 2026, 08_49_19 PM-3.png", "adventure-map": "ChatGPT Image Oct 11, 2026, 08_49_20 PM-4.png"}

def R(x0, y0, x1, y1, kind="dynamic", method="telea", name="", sample=None): return {"box": [x0, y0, x1, y1], "kind": kind, "method": method, "name": name, "sample": sample}
# region taxonomy per theme (master px). kind: dynamic (inpaint), legend (inpaint; controller legend redrawn), static (listed for the diff masks)
# method: telea (textured surroundings) | ring (flat panel behind: fill with the median of a ring around the box, then soften the seam)
REGIONS = {
 "arcade-grid": [
  R(1535, 18, 1655, 50, name="clock"), R(412, 64, 1480, 118, method="ring", name="tab bar labels, icons and the active plate (plate kept as sprite)", sample=(1440, 72, 1476, 114)),
  R(40, 155, 300, 395, method="ring", name="stats panel contents"),
  R(322, 132, 716, 600, method="ring", name="selected card and frame (frame kept as sprite)", sample=(725, 364, 1600, 371)),
  *[R(725 + c * 180, 150 + r * 225, 725 + c * 180 + 166, 150 + r * 225 + 212, method="ring", name=f"small cover r{r}c{c}") for r in range(2) for c in range(5)],
  *[R(x, 600, x + 166, 794, method="ring", name=f"small cover r2c{i}") for i, x in enumerate([342, 540, 725, 905, 1085, 1265])],
  R(40, 856, 1640, 910, kind="legend", method="rows", name="legend bar contents", sample=(1010, 856, 1440, 910)),
  R(0, 0, MW, MH, kind="static", name="everything else: sky, islands, castle, waterfall, ground, brand, bar plates")],
 "box-art-showcase": [
  R(940, 25, 1420, 62, name="top navigation labels"), R(1540, 14, 1650, 66, name="clock"), 
  *[R(*b, method="ring", name=f"box envelope {i}") for i, b in enumerate([(44, 244, 210, 522), (208, 220, 362, 524), (352, 180, 578, 524), (570, 80, 962, 584), (958, 190, 1188, 534), (1188, 236, 1352, 524), (1344, 260, 1488, 524), (1480, 286, 1606, 520)])],
  R(40, 522, 1612, 608, name="floor reflections (rebuilt)"),
  R(112, 596, 830, 694, name="title (the compass star stays)"), R(60, 715, 760, 775, name="blurb"), R(60, 805, 800, 865, name="facts row"),
  R(900, 645, 1240, 870, method="ring", name="collection panel contents"),
  R(300, 876, 1380, 918, kind="legend", name="legend row (the master shows no controller legend here; the renderer adds the original-controller legend in the empty floor band)"),
  R(0, 0, MW, MH, kind="static", name="everything else: sky, moon, castle, floor, brand plate, characters, arrows, panel frame")],
 "featured-shelf": [
  R(1515, 14, 1650, 100, name="clock, date, status icons"),
  R(40, 150, 310, 420, method="ring", name="sidebar items (plate kept as sprite)"),
  R(330, 128, 1140, 575, method="ring", name="hero scene"),
  R(1175, 135, 1615, 495, method="ring", name="featured title, metadata, text"),
  R(274, 586, 456, 824, method="ring", name="shelf cover 0 and focus frame with its glow (frame kept as sprite)", sample=(606, 620, 616, 790)), *[R(x, 603, x + 150, 808, method="ring", name=f"shelf cover {i}") for i, x in enumerate([456, 616, 780, 938, 1098, 1260, 1420], 1)],
  R(440, 876, 1100, 918, kind="legend", method="ring", name="legend"),
  R(0, 0, MW, MH, kind="static", name="everything else: brand, lamp, characters, ground, mountains, moons, shelf trim, arrows")],
 "adventure-map": [
  R(1518, 24, 1620, 60, name="clock"), R(142, 288, 266, 386, method="synth", name="walking character and dog (become the walker sprite)"),
  R(478, 110, 690, 348, method="ring", name="landmark game card and frame (frame kept as sprite)"),
  R(310, 350, 440, 376, name="sign BUILT-IN label"), R(738, 372, 872, 397, name="sign FAVORITES label"), R(988, 364, 1118, 390, name="sign FOLDER 1 label"),
  R(1225, 374, 1356, 399, name="sign FOLDER 2 label"), R(1458, 394, 1588, 421, name="sign SETTINGS label"), R(42, 298, 108, 324, name="sign START label"),
  R(480, 488, 1190, 692, method="ring", name="details panel contents (frame kept as sprite)"),
  R(142, 717, 338, 855, method="ring", name="shelf cover 0 and focus frame (frame kept as sprite)", sample=(348, 850, 900, 858)), *[R(x, 725, x + 182, 847, method="ring", name=f"shelf cover {i}") for i, x in enumerate([348, 546, 744, 942, 1146, 1346], 1)],
  R(380, 878, 1300, 915, kind="legend", method="ring", name="legend"),
  R(0, 0, MW, MH, kind="static", name="everything else: the overworld, signs, statues, bridges, waterfalls, castle, creatures, brand, arrows")]}

# sprites: (name, box, transparent interior box or None)
SPRITES = {
 "arcade-grid": [("card-frame", (322, 132, 716, 600), (338, 148, 702, 594)), ("tab-active", (412, 64, 618, 120), None, [(430, 74, 606, 110)]), ("tab-bar", (408, 60, 1492, 124), (426, 70, 1478, 116)),
                 ("icon-games", (436, 76, 472, 108), None), ("icon-builtin", (655, 76, 700, 108), None), ("icon-star", (858, 76, 898, 108), None), ("icon-folder", (1080, 76, 1120, 108), None), ("icon-clock", (1303, 76, 1343, 108), None), ("icon-gear", (1472, 862, 1512, 902), None),
                 ("badge-a", (58, 862, 100, 902), None), ("badge-x", (236, 862, 278, 902), None), ("stat-icon-games", (46, 160, 92, 200), None), ("stat-icon-storage", (46, 232, 92, 272), None), ("stat-icon-coll", (46, 335, 92, 375), None)],
 "box-art-showcase": [("badge-brand", (835, 530, 950, 562), None), ("seal", (898, 478, 948, 528), None), ("logo-mark", (636, 516, 692, 566), None), ("arrow-l", (8, 410, 64, 466), None), ("arrow-r", (1608, 410, 1664, 466), None), ("panel-frame", (888, 628, 1248, 878), (900, 640, 1236, 866))],
 "featured-shelf": [("side-active", (24, 146, 310, 214), None, [(52, 154, 110, 208), (118, 156, 302, 204)]), ("icon-home", (58, 158, 104, 204), None), ("icon-star", (58, 228, 104, 274), None), ("icon-folder", (58, 296, 104, 342), None), ("icon-gear", (58, 364, 104, 410), None),
                    ("hero-frame", (318, 118, 1150, 582), (330, 128, 1140, 575)), ("shelf-focus", (286, 598, 448, 812), (296, 608, 438, 802)), ("badge-a", (488, 880, 524, 916), None), ("badge-x", (628, 880, 664, 916), None), ("arrow-l", (238, 688, 274, 724), None), ("arrow-r", (1575, 688, 1611, 724), None)],
 "adventure-map": [("card-frame", (478, 110, 690, 348), (495, 128, 673, 331)), ("card-pointer", (560, 340, 600, 364), None), ("marker", (552, 360, 606, 392), None), ("panel-frame", (462, 470, 1208, 708), (480, 488, 1190, 692)),
                   ("sign", (300, 342, 450, 384), None, [(310, 350, 440, 376)]), ("sign-start", (36, 292, 114, 332), None, [(42, 298, 108, 324)]), ("shelf-focus", (142, 717, 338, 855), (152, 727, 328, 845)), ("shelf-frame", (340, 717, 536, 855), (350, 727, 526, 845)),
                   ("badge-dpad", (388, 876, 428, 916), None), ("badge-a", (560, 876, 600, 916), None), ("badge-b", (716, 876, 756, 916), None), ("badge-menu", (1200, 876, 1240, 916), None), ("arrow-l", (82, 762, 118, 798), None), ("arrow-r", (1556, 762, 1592, 798), None),
                   ("hero", (142, 288, 266, 386), None)]}

def s(box): return [round(box[0] * SX), round(box[1] * SY), round(box[2] * SX), round(box[3] * SY)]
def pt(x, y): return [round(x * SX), round(y * SY)]
# geometry manifest (stage px, converted from master px): the renderers position against these boxes
GEOMETRY = {
 "arcade-grid": {"brand": s((40, 28, 415, 80)), "clock": s((1535, 18, 1655, 50)), "tabbar": s((408, 60, 1492, 124)), "tab_active_w": round(206 * SX),
                 "stats": {"panel": s((22, 128, 305, 400)), "rows": [s((112, 160, 300, 205)), s((112, 235, 300, 280)), s((112, 338, 300, 383))], "icon_x": round(46 * SX)},
                 "card": {"frame": s((322, 132, 716, 600)), "cover": s((340, 150, 697, 530)), "caption": s((340, 532, 700, 592))},
                 "cells": [s((725 + c * 180, 150 + r * 225, 725 + c * 180 + 166, 150 + r * 225 + 212)) for r in range(2) for c in range(5)] + [s((x, 600, x + 166, 794)) for x in [342, 540, 725, 905, 1085, 1265]],
                 "legend": s((40, 850, 1640, 915))},
 "box-art-showcase": {"brand": s((70, 30, 525, 88)), "nav": s((900, 26, 1470, 62)), "clock": s((1540, 14, 1650, 66)),
                      "envelopes": [{"d": -3, "box": s((50, 250, 205, 515))}, {"d": -2, "box": s((215, 225, 355, 520))}, {"d": -1, "box": s((360, 185, 570, 520))},
                                    {"d": 0, "box": s((575, 85, 955, 580))}, {"d": 1, "box": s((965, 195, 1180, 530))}, {"d": 2, "box": s((1195, 240, 1345, 520))}, {"d": 3, "box": s((1350, 265, 1480, 520))}, {"d": 4, "box": s((1485, 290, 1600, 515))}],
                      "spine_ratio": 45 / 380, "dots": s((700, 594, 940, 606)), "title": s((125, 598, 820, 692)), "star": s((62, 616, 110, 662)), "blurb": s((70, 715, 760, 775)), "rule": s((72, 791, 888, 793)), "facts": s((70, 808, 840, 862)),
                      "panel": {"frame": s((888, 628, 1248, 878)), "heading": s((985, 650, 1230, 676)), "rows": [s((985, 702, 1230, 730)), s((985, 740, 1230, 768)), s((985, 778, 1230, 806)), s((985, 816, 1230, 844))], "icon_x": round(922 * SX)},
                      "arrow_l": s((8, 410, 64, 466)), "arrow_r": s((1608, 410, 1664, 466))},
 "featured-shelf": {"brand": s((155, 20, 630, 65)), "clock": s((1515, 14, 1650, 66)), "side": {"plate": s((24, 146, 310, 214)), "pitch": round(68 * SY), "icon_x": round(58 * SX), "text_x": round(130 * SX), "first_y": round(146 * SY)},
                    "hero": {"frame": s((318, 118, 1150, 582)), "inner": s((330, 128, 1140, 575))},
                    "info": {"label": s((1180, 138, 1600, 158)), "title": s((1180, 162, 1610, 212)), "rows": [s((1180, 236, 1590, 266)), s((1180, 276, 1590, 306)), s((1180, 316, 1590, 346))], "text": s((1180, 372, 1612, 492)), "icon_x": round(1180 * SX), "value_right": round(1588 * SX)},
                    "shelf": {"cells": [s((x + 6, 611, x + 144, 801)) for x in [292, 456, 616, 780, 938, 1098, 1260, 1420]], "focus": s((286, 598, 448, 812)), "pitch": round(162 * SX)},
                    "arrow_l": s((238, 688, 274, 724)), "arrow_r": s((1575, 688, 1611, 724)), "legend": s((300, 876, 1380, 918))},
 "adventure-map": {"brand": s((95, 55, 435, 100)), "clock": s((1518, 24, 1620, 60)),
                   "landmarks": [{"id": "allgames", "sign": s((36, 292, 114, 332)), "center": pt(75, 334), "stand": pt(142, 288)}, {"id": "browse", "sign": s((300, 342, 450, 384)), "center": pt(375, 395)},
                                 {"id": "favorites", "sign": s((730, 364, 880, 406)), "center": pt(805, 420)}, {"id": "folder1", "sign": s((978, 356, 1128, 398)), "center": pt(1052, 410)},
                                 {"id": "folder2", "sign": s((1215, 366, 1365, 408)), "center": pt(1292, 420)}, {"id": "settings", "sign": s((1448, 386, 1598, 428)), "center": pt(1522, 432)}],
                   "card": {"frame": s((478, 110, 690, 348)), "cover": s((495, 128, 673, 331)), "pointer": s((560, 340, 600, 364)), "marker": s((552, 360, 606, 392))},
                   "path": [pt(80, 332), pt(200, 338), pt(330, 372), pt(470, 372), pt(578, 378), pt(700, 402), pt(805, 420), pt(930, 416), pt(1052, 410), pt(1180, 422), pt(1292, 420), pt(1420, 428), pt(1522, 432)],
                   "panel": {"frame": s((462, 470, 1208, 708)), "cover": s((495, 500, 737, 686)), "title": s((775, 500, 1180, 540)), "tags": s((778, 546, 1180, 574)), "text": s((775, 586, 1180, 664)), "rule": s((790, 678, 1160, 684))},
                   "shelf": {"cells": [s((x + 8, 733, x + 174, 839)) for x in [150, 348, 546, 744, 942, 1146, 1346]], "focus": s((142, 717, 338, 855)), "frame": s((340, 717, 536, 855)), "pitch": round(198 * SX)},
                   "arrow_l": s((82, 762, 118, 798)), "arrow_r": s((1556, 762, 1592, 798)), "legend": s((380, 876, 1300, 918))}}


def synth_fill(out: np.ndarray, box, avoid, block=8, ctx=10, search=170):
    """patch-based texture synthesis: each block of the region is copied from the best-matching block nearby whose
    surroundings match the already-known ring around it (onion order, masked SQDIFF template match). Keeps textures
    such as the overworld path and grass coherent where a diffusion inpaint would smear."""
    x0, y0, x1, y1 = box; H, W = out.shape[:2]
    known = np.ones((H, W), np.uint8); known[y0:y1, x0:x1] = 0
    src_ok = known.copy()
    for a in avoid: src_ok[max(0, a[1]):a[3], max(0, a[0]):a[2]] = 0
    todo = [(bx, by) for by in range(y0, y1, block) for bx in range(x0, x1, block)]; sums = {}
    def kc(b): return int(known[max(0, b[1] - ctx):b[1] + block + ctx, max(0, b[0] - ctx):b[0] + block + ctx].sum())
    while todo:
        todo.sort(key=lambda b: -kc(b)); bx, by = todo.pop(0); bw, bh = min(block, x1 - bx), min(block, y1 - by)
        tx0, ty0, tx1, ty1 = bx - ctx, by - ctx, bx + bw + ctx, by + bh + ctx
        templ = out[ty0:ty1, tx0:tx1]; mask = known[ty0:ty1, tx0:tx1].copy(); mask[ctx:ctx + bh, ctx:ctx + bw] = 0
        wx0, wy0, wx1, wy1 = max(0, tx0 - search), max(0, ty0 - search), min(W, tx1 + search), min(H, ty1 + search)
        res = cv2.matchTemplate(out[wy0:wy1, wx0:wx1], templ, cv2.TM_SQDIFF, mask=mask)
        if (bw, bh) not in sums: sums[(bw, bh)] = cv2.boxFilter(src_ok.astype(np.float32), -1, (bw, bh), anchor=(0, 0), normalize=False, borderType=cv2.BORDER_CONSTANT)
        okw = sums[(bw, bh)][wy0 + ctx:wy0 + ctx + res.shape[0], wx0 + ctx:wx0 + ctx + res.shape[1]]
        res = res.copy(); res[okw < bw * bh - 0.5] = np.inf
        j, i = np.unravel_index(int(np.argmin(res)), res.shape); sx, sy = wx0 + i + ctx, wy0 + j + ctx
        out[by:by + bh, bx:bx + bw] = out[sy:sy + bh, sx:sx + bw]; known[by:by + bh, bx:bx + bw] = 1
    return out

def inpaint(bgr: np.ndarray, regions: list) -> np.ndarray:
    out = bgr.copy()
    for rg in regions:
        if rg["kind"] == "static": continue
        x0, y0, x1, y1 = rg["box"]; x0, y0, x1, y1 = max(0, x0), max(0, y0), min(MW, x1), min(MH, y1)
        if rg["method"] == "synth":
            out = synth_fill(out, (x0, y0, x1, y1), [r["box"] for r in regions if r is not rg and r["kind"] != "static"]); continue
        if rg["method"] == "rows":   # per-row median of the sample box: keeps a plate's vertical gradient, no seam band
            sx0, sy0, sx1, sy1 = rg["sample"]
            for y in range(y0, y1):
                yy = min(max(y, sy0), sy1 - 1); out[y, x0:x1] = np.median(out[yy, sx0:sx1], axis=0).astype(np.uint8)
            continue
        if rg["method"] == "ring":
            ring = np.zeros((MH, MW), np.uint8)
            if rg.get("sample"): sx0, sy0, sx1, sy1 = rg["sample"]; cv2.rectangle(ring, (sx0, sy0), (sx1, sy1), 255, -1)
            else: cv2.rectangle(ring, (x0 - 6, y0 - 6), (x1 + 6, y1 + 6), 255, -1); cv2.rectangle(ring, (x0, y0), (x1, y1), 0, -1)
            vals = out[ring > 0]; med = np.median(vals, axis=0) if len(vals) else np.array([20, 20, 24])
            out[y0:y1, x0:x1] = med.astype(np.uint8)
            m = np.zeros((MH, MW), np.uint8); cv2.rectangle(m, (x0 - 3, y0 - 3), (x1 + 3, y1 + 3), 255, -1); cv2.rectangle(m, (x0 + 3, y0 + 3), (x1 - 3, y1 - 3), 0, -1)
            out = cv2.inpaint(out, m, 3, cv2.INPAINT_TELEA)
        else:
            m = np.zeros((MH, MW), np.uint8); cv2.rectangle(m, (x0, y0), (x1, y1), 255, -1)
            out = cv2.inpaint(out, m, 5, cv2.INPAINT_TELEA)
    return out

def sprite(master: Image.Image, box, hole, clean=None):
    src = master
    if clean:   # authored labels/icons inside the plate are inpainted so the live label can be drawn on the clean plate
        bgr = cv2.cvtColor(np.array(master), cv2.COLOR_RGB2BGR); m = np.zeros((MH, MW), np.uint8)
        for (cx0, cy0, cx1, cy1) in clean: cv2.rectangle(m, (cx0, cy0), (cx1, cy1), 255, -1)
        src = Image.fromarray(cv2.cvtColor(cv2.inpaint(bgr, m, 4, cv2.INPAINT_TELEA), cv2.COLOR_BGR2RGB))
    im = src.crop(box).convert("RGBA")
    if hole:
        a = Image.new("L", im.size, 255); a.paste(0, (hole[0] - box[0], hole[1] - box[1], hole[2] - box[0], hole[3] - box[1])); im.putalpha(a)
    return im.resize((max(1, round(im.width * SX)), max(1, round(im.height * SY))), Image.LANCZOS)

def hero_sprite(master: Image.Image, box):
    """the walking character near START, keyed out of the scene: sprite colors kept (outline, cape, skin, shirt), path and grass dropped"""
    im = master.crop(box).convert("RGBA"); px = np.array(im).astype(int); r, g, b = px[..., 0], px[..., 1], px[..., 2]
    dark = (r + g + b) < 150; red = (r > 150) & (g < 90) & (b < 90); skin = (r > 200) & (g > 150) & (b > 110) & (r - b > 40); blue = (b > 120) & (b > r + 30); white = (r > 200) & (g > 200) & (b > 200)
    keep = (dark | red | skin | blue | white).astype(np.uint8) * 255
    keep = cv2.morphologyEx(keep, cv2.MORPH_CLOSE, np.ones((3, 3), np.uint8))
    n, lab, stats, _ = cv2.connectedComponentsWithStats((keep > 0).astype(np.uint8), 8)
    if n > 1: keep = (lab == 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))).astype(np.uint8) * 255
    out = np.array(im); out[..., 3] = keep
    return Image.fromarray(out).resize((round(im.width * SX), round(im.height * SY)), Image.NEAREST)

def main():
    os.makedirs(OUT, exist_ok=True)
    man = {"schema": "ccs-home-art/2", "stage": [SW, SH], "master_size": [MW, MH], "scale": [SX, SY], "method": "lossless scaled scene with only the dynamic regions inpainted; authored frames, plates, icons and markers cut as sprites; geometry measured on the masters", "themes": {}}
    for theme, fn in MAP.items():
        p = os.path.join(SRC, fn); raw = open(p, "rb").read(); master = Image.open(p).convert("RGB"); assert master.size == (MW, MH), master.size
        bgr = cv2.cvtColor(np.array(master), cv2.COLOR_RGB2BGR); scene = inpaint(bgr, REGIONS[theme])
        scene_im = Image.fromarray(cv2.cvtColor(scene, cv2.COLOR_BGR2RGB)).resize((SW, SH), Image.LANCZOS); sp = os.path.join(OUT, f"scene-{theme}.png"); scene_im.save(sp, optimize=True)
        master.resize((SW, SH), Image.LANCZOS).save(os.path.join(OUT, f"ref-{theme}.png"), optimize=True)
        scene_im.resize((320, 180), Image.LANCZOS).save(os.path.join(OUT, f"thumb-{theme}.jpg"), "JPEG", quality=84, optimize=True)
        sprites = {}
        for spec in SPRITES[theme]:
            name, box, hole = spec[0], spec[1], spec[2]; clean = spec[3] if len(spec) > 3 else None
            im = hero_sprite(master, box) if name == "hero" else sprite(master, box, hole, clean); fnm = f"sprite-{theme}-{name}.png"; im.save(os.path.join(OUT, fnm), optimize=True)
            sprites[name] = {"file": fnm, "box_master": list(box), "box_stage": s(box), "hole_stage": s(hole) if hole else None}
        man["themes"][theme] = {"master_file": fn, "master_sha256": hashlib.sha256(raw).hexdigest(), "scene": os.path.basename(sp), "scene_bytes": os.path.getsize(sp), "reference": f"ref-{theme}.png",
                               "regions": [{"name": rg["name"], "sample_master": rg.get("sample"), "kind": {"dynamic": "DYNAMIC RECREATE", "legend": "LEGEND CORRECTION", "static": "STATIC PRESERVE"}[rg["kind"]], "box_master": rg["box"], "box_stage": s(rg["box"]), "method": rg["method"] if rg["kind"] != "static" else None} for rg in REGIONS[theme]],
                               "sprites": sprites, "geometry": GEOMETRY[theme]}
        print(theme, "scene", os.path.getsize(sp), "bytes,", len(sprites), "sprites")
    for old in ("bg-arcade-grid.jpg", "bg-box-art-showcase.jpg", "bg-featured-shelf.jpg", "bg-adventure-map.jpg"):
        q = os.path.join(OUT, old)
        if os.path.exists(q): os.remove(q)
    man["unresolved_assets"] = {"featured-shelf hero": "no illustrated wide scene exists for NES games; the hero shows the game's own gameplay screen (libretro Named_Snaps) inside the master's hero frame with softened (blurred) edge extension of the same screen; an owner-supplied hero image per game (reports/art/hero/<game_id>.png) takes precedence"}
    json.dump(man, open(os.path.join(OUT, "home-art.json"), "w"), indent=1); print("manifest", os.path.join(OUT, "home-art.json"))

if __name__ == "__main__": main()
