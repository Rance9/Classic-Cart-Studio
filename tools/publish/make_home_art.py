"""Console HOME theme scenery from the four approved master mockups (visual authorities, kept unmodified in
_temp/images/master mockup HOME themes). Each master (1672 x 941) is scaled to the 1280 x 720 stage and saved as the
theme's scenery layer; the renderers cover the functional regions (grid, shelf, panels, legends) with live content, so the
fictional covers, counts and X/Y legends baked into the art never show. Also writes a manifest with the source hashes and
the theme mapping (by visual content, recorded in Phase A of the HOME directive).
  python make_home_art.py  -> tools/publish/assets/home/bg-<theme>.jpg + home-art.json"""
from __future__ import annotations
import os, json, hashlib
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__)); PROJECT = os.path.dirname(os.path.dirname(HERE))
SRC = os.path.join(PROJECT, "_temp", "images", "master mockup HOME themes"); OUT = os.path.join(HERE, "assets", "home")
MAP = {   # discovered by inspecting the files (Phase A); the order of the files does not follow the theme order
    "featured-shelf": "ChatGPT Image Oct 11, 2026, 08_49_16 PM-1.png",
    "arcade-grid": "ChatGPT Image Oct 11, 2026, 08_49_17 PM-2.png",
    "box-art-showcase": "ChatGPT Image Oct 11, 2026, 08_49_19 PM-3.png",
    "adventure-map": "ChatGPT Image Oct 11, 2026, 08_49_20 PM-4.png",
}

# Regions (stage coordinates) where the master carries fictional UI content: covers, counts, legends, clock, labels. They are
# neutralized in the scenery layer (blurred, darkened, feathered) so the live renderers draw the real content there and
# nothing fictional can show through a translucent panel. Composition, scenery and accent colors stay.
NEUTRAL = {
    "arcade-grid": [(0, 0, 1280, 100), (14, 100, 246, 330), (250, 100, 1240, 622), (0, 640, 1280, 720)],
    "box-art-showcase": [(0, 0, 1280, 66), (0, 66, 900, 470), (900, 222, 1280, 470), (1180, 110, 1280, 250), (0, 470, 1060, 720)],   # the moon and castle (900-1180 x 66-222) stay
    "featured-shelf": [(0, 0, 1280, 84), (14, 96, 244, 330), (250, 96, 880, 448), (896, 96, 1250, 380), (216, 456, 1250, 650), (280, 660, 1000, 720)],
    "adventure-map": [(60, 18, 350, 84), (1140, 14, 1270, 62), (372, 90, 522, 268), (20, 224, 100, 254), (234, 260, 334, 294), (574, 278, 666, 310), (758, 272, 854, 304), (942, 280, 1040, 312), (1108, 290, 1216, 322), (356, 362, 924, 532), (100, 534, 1180, 662), (0, 666, 1280, 720)],
}

def neutralize(bg: Image.Image, rects):
    from PIL import ImageFilter, ImageEnhance
    soft = ImageEnhance.Brightness(bg.filter(ImageFilter.GaussianBlur(26))).enhance(0.42)
    mask = Image.new("L", bg.size, 0)
    from PIL import ImageDraw
    d = ImageDraw.Draw(mask)
    for (x0, y0, x1, y1) in rects: d.rectangle([x0, y0, x1, y1], fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(6))
    return Image.composite(soft, bg, mask)

def main():
    os.makedirs(OUT, exist_ok=True); man = {"schema": "ccs-home-art/1", "stage": [1280, 720], "themes": {}}
    for theme, fn in MAP.items():
        p = os.path.join(SRC, fn)
        if not os.path.exists(p): man["themes"][theme] = {"missing": fn}; print("MISSING", theme, fn); continue
        raw = open(p, "rb").read(); im = Image.open(p).convert("RGB"); w, h = im.size
        bg = im.resize((1280, 720), Image.LANCZOS); bg = neutralize(bg, NEUTRAL.get(theme, [])); out = os.path.join(OUT, f"bg-{theme}.jpg"); bg.save(out, "JPEG", quality=86, optimize=True, progressive=True)
        thumb = bg.resize((320, 180), Image.LANCZOS); tp = os.path.join(OUT, f"thumb-{theme}.jpg"); thumb.save(tp, "JPEG", quality=82, optimize=True)   # thumbnails from the neutralized scenery: no fictional titles or counts
        man["themes"][theme] = {"neutralized_regions": NEUTRAL.get(theme, []), "master_file": fn, "master_sha256": hashlib.sha256(raw).hexdigest(), "master_size": [w, h], "scenery": os.path.basename(out), "scenery_bytes": os.path.getsize(out), "thumb": os.path.basename(tp)}
        print(theme, "<-", fn, f"{w}x{h}", "->", os.path.basename(out), os.path.getsize(out), "bytes")
    json.dump(man, open(os.path.join(OUT, "home-art.json"), "w"), indent=1); print("manifest", os.path.join(OUT, "home-art.json"))

if __name__ == "__main__": main()
