"""Featured Shelf hero art: request packs and import for per-game illustrated widescreen key art.

The approved Featured Shelf master shows an illustrated, cinematic widescreen scene for the featured game. No such art
exists for NES games, and composing covers and screenshots was rejected by the owner. Producing it needs an image
generation (or illustration) capability that this toolchain does not have, so the pipeline is split:

  request   writes a brief per game under reports/art/hero-requests/<game_id>/ with the real facts (title, year,
            publisher, genre, players), the composition brief matching the master (16:9, pixel-art-inspired painterly
            night scene, no lettering in the image: the runtime draws the title), and the authentic references
            (cover, title screen, gameplay screen) for whoever or whatever generates the art; plus an index.
  import    validates finished images (PNG/JPEG/WebP, at least 1280 x 704, aspect 16:9 within 4 % or center-cropped),
            normalizes them to 1620 x 894 PNG in reports/art/hero/<game_id>.png and records `art_hero` in the catalog;
            the runtime then uses the image instead of the composed fallback (hmHero in home-themes.js).
  status    counts games with hero art, with requests, and without either.

  python hero_art.py request <game_id>... | --all
  python hero_art.py import <game_id> <image> | --dir <folder with <game_id>.<ext> files>
  python hero_art.py status
"""
from __future__ import annotations
import os, sys, json, shutil
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import REPORTS
from PIL import Image
HERO_W, HERO_H = 1620, 894
REQ = os.path.join(REPORTS, "art", "hero-requests"); OUT = os.path.join(REPORTS, "art", "hero")

BRIEF = """# Hero art brief: {title}

Facts (use only these; nothing invented): title "{title}", year {year}, publisher {publisher}, genre {genre}, players {players}.

Deliver ONE image, 16:9, at least 1620 x 894 px (1920 x 1080 preferred), PNG or JPEG, saved as `{gid}.png` (or .jpg).

Composition (matches the approved Featured Shelf master): a cinematic widescreen key-art scene in painterly pixel-art
style, night or dusk palette with deep blues and warm accents, strong foreground subject at the left third, open sky or
environment at the right third (the title lettering is drawn there by the software, so keep the right third free of
important detail), atmospheric depth, soft rim lighting. The scene must depict this game's own world, characters and
objects as shown in the references; no text, logos, box edges, borders, watermarks or UI in the image.

References in this folder: cover.jpg (box front), title.png (title screen), snap.png (gameplay screen).
"""

def games():
    cat = json.load(open(os.path.join(REPORTS, "catalog.json"), encoding="utf-8")); return cat, {g["game_id"]: g for g in cat["games"]}

def request(ids):
    cat, by = games(); os.makedirs(REQ, exist_ok=True); rows = []
    for gid in ids:
        g = by.get(gid)
        if not g: print("unknown game", gid); continue
        d = os.path.join(REQ, gid); os.makedirs(d, exist_ok=True)
        for key, name in (("art", "cover.jpg"), ("art_title", "title.png"), ("art_snap", "snap.png")):
            src = os.path.join(REPORTS, g[key]) if g.get(key) else None
            if src and os.path.exists(src): shutil.copy2(src, os.path.join(d, name))
        open(os.path.join(d, "brief.md"), "w", encoding="utf-8").write(BRIEF.format(title=g.get("display_title") or gid, year=g.get("year") or "unknown", publisher=g.get("publisher") or "unknown", genre=g.get("genre") or "unknown", players=g.get("players") or "unknown", gid=gid))
        rows.append((gid, g.get("display_title") or gid))
    idx = ["# Hero art requests", "", "| game_id | title | deliver as |", "|---|---|---|"] + [f"| {gid} | {t} | reports/art/hero/{gid}.png (import with hero_art.py) |" for gid, t in rows]
    open(os.path.join(REQ, "index.md"), "w", encoding="utf-8").write("\n".join(idx) + "\n"); print("requests written:", len(rows), "->", REQ)

def normalize(path: str) -> Image.Image:
    im = Image.open(path); im.verify(); im = Image.open(path).convert("RGB")
    if im.width < 1280 or im.height < 704: raise ValueError(f"too small ({im.width} x {im.height}); at least 1280 x 704")
    want = HERO_W / HERO_H; have = im.width / im.height
    if abs(have - want) / want > 0.04:                                 # center crop to 16:9
        if have > want: w = round(im.height * want); x = (im.width - w) // 2; im = im.crop((x, 0, x + w, im.height))
        else: h = round(im.width / want); y = (im.height - h) // 2; im = im.crop((0, y, im.width, y + h))
    return im.resize((HERO_W, HERO_H), Image.LANCZOS)

def import_one(gid: str, path: str, cat=None, by=None):
    own = cat is None
    if own: cat, by = games()
    if gid not in by: raise ValueError(f"unknown game {gid}")
    im = normalize(path); os.makedirs(OUT, exist_ok=True); out = os.path.join(OUT, gid + ".png"); im.save(out + ".tmp", "PNG", optimize=True); os.replace(out + ".tmp", out)
    by[gid]["art_hero"] = f"art/hero/{gid}.png"; by[gid]["art_hero_source"] = os.path.basename(path)
    if own: json.dump(cat, open(os.path.join(REPORTS, "catalog.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    return out

def status():
    cat, by = games(); have = [g for g in cat["games"] if g.get("art_hero") and os.path.exists(os.path.join(REPORTS, g["art_hero"]))]
    req = [d for d in (os.listdir(REQ) if os.path.isdir(REQ) else []) if os.path.isdir(os.path.join(REQ, d))]
    print(f"hero art: {len(have)} games with imported art, {len(req)} request packs, {len(cat['games']) - len(have)} games on the composed fallback")

def main(argv):
    if not argv or argv[0] not in ("request", "import", "status"): print(__doc__); return 2
    if argv[0] == "status": status(); return 0
    if argv[0] == "request":
        cat, by = games(); ids = list(by) if "--all" in argv[1:] else argv[1:]; request(ids); return 0
    cat, by = games(); n = 0
    if "--dir" in argv:
        d = argv[argv.index("--dir") + 1]
        for name in sorted(os.listdir(d)):
            gid, ext = os.path.splitext(name)
            if ext.lower() in (".png", ".jpg", ".jpeg", ".webp") and gid in by:
                try: import_one(gid, os.path.join(d, name), cat, by); n += 1; print("imported", gid, name)
                except Exception as e: print("refused", name, e)
    else:
        import_one(argv[1], argv[2], cat, by); n = 1; print("imported", argv[1])
    json.dump(cat, open(os.path.join(REPORTS, "catalog.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1); print("hero art imported:", n); return 0

if __name__ == "__main__": sys.exit(main(sys.argv[1:]))
