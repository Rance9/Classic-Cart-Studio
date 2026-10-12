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
  python hero_art.py approve|reject <game_id> [reason]    review verdicts (only approved art is published)
  python hero_art.py remove <game_id>                     drop the runtime image (candidates stay on disk; back to the fallback)
  python hero_art.py select <game_id> <tag>               make an earlier candidate the runtime image again
  python hero_art.py approve|reject <game_id> [tag=<tag>] [reason]
  python hero_art.py fallback <image>                     install the universal genre-neutral fallback scene (hero-fallback.png)
"""
from __future__ import annotations
import os, sys, json, shutil, re
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
    want = HERO_W / HERO_H; have = im.width / im.height   # the derivative's own aspect (1620:894 = 1.8121), not 16:9: crop straight to it, then resize; never stretch
    if have > want + 1e-6: w = round(im.height * want); x = (im.width - w) // 2; im = im.crop((x, 0, x + w, im.height))
    elif have < want - 1e-6: h = round(im.width / want); y = (im.height - h) // 2; im = im.crop((0, y, im.width, y + h))
    return im.resize((HERO_W, HERO_H), Image.LANCZOS)

TITLE_SLOTS = [   # lettering block candidates (fractions of the hero box): left x, top y, preference penalty; alternates are right-aligned
    ("top-right", 0.50, 0.09, 0.00), ("top-right-narrow", 0.58, 0.09, 0.05), ("top-right-far", 0.64, 0.09, 0.08), ("mid-right", 0.50, 0.40, 0.15), ("low-right", 0.50, 0.62, 0.20)]
DEFAULT_TITLE = {"x": 0.50, "y": 0.09, "slot": "top-right"}

def title_box(im: Image.Image) -> dict:
    """pick the quietest lettering slot on the normalized master so the title stays off the subject. Busyness = share of strong
    edges (contrast steps above 40 levels) inside the text footprint (the slot's top 30 %, from its left edge to the right margin),
    normalized by the whole image; the preference penalty keeps the master's top-right placement unless it is clearly busier."""
    import numpy as np
    a = np.array(im.convert("L")).astype(float); H, W = a.shape
    e = np.maximum(np.abs(np.diff(a, axis=1, prepend=a[:, :1])), np.abs(np.diff(a, axis=0, prepend=a[:1, :]))); strong = (e > 40).astype(float); whole = max(strong.mean(), 1e-6)
    best = None
    for slot, x, y, pen in TITLE_SLOTS:
        x0, y0, x1, y1 = int(W * x), int(H * y), int(W * 0.965), int(H * min(1.0, y + 0.30)); score = strong[y0:y1, x0:x1].mean() / whole + pen
        if best is None or score < best[0]: best = (score, slot, x, y)
    return {"x": best[2], "y": best[3], "slot": best[1], "busyness": round(float(best[0]), 3), "align": "left" if best[1] == "top-right" else "right"}

def _reg():
    reg_p = os.path.join(OUT, "index.json"); return reg_p, (json.load(open(reg_p, encoding="utf-8")) if os.path.exists(reg_p) else {"schema": "ccs-hero-art/2", "games": {}})

def _apply_current(by, reg, gid, tag):
    """make <tag> the runtime candidate for the game: copy its master to <gid>.png and mirror its fields into the catalog row and the record's top level"""
    rec = reg["games"][gid]; c = rec["candidates"][tag]; cur = os.path.join(OUT, gid + ".png")
    shutil.copy2(os.path.join(OUT, c["file"]), cur + ".tmp"); os.replace(cur + ".tmp", cur)
    rec.update({"current": tag, "file": f"art/hero/{gid}.png", "sha256": c["sha256"], "source": c["source"], "status": c["status"], "provenance": c["provenance"], "imported": c["imported"], "title_box": c.get("title_box") or DEFAULT_TITLE})
    by[gid]["art_hero"] = f"art/hero/{gid}.png"; by[gid]["art_hero_source"] = c["source"]; by[gid]["art_hero_sha256"] = c["sha256"]; by[gid]["art_hero_status"] = c["status"]; by[gid]["art_hero_provenance"] = c["provenance"]; by[gid]["art_hero_title"] = rec["title_box"]

def import_one(gid: str, path: str, cat=None, by=None, tag: str = None, pinned_title: bool = False):
    """import one candidate under its own tag: originals/<gid>-<tag>.<ext> (untouched), candidates/<gid>-<tag>.png (master), registry entry;
    an existing tag is never overwritten (a numeric suffix is added); the new candidate becomes the runtime current for development preview"""
    own = cat is None
    if own: cat, by = games()
    if gid not in by: raise ValueError(f"unknown game {gid}")
    import hashlib, time
    reg_p, reg = _reg(); rec = reg["games"].setdefault(gid, {"title": by[gid].get("display_title"), "candidates": {}}); rec.setdefault("candidates", {})
    tag = re.sub(r"[^A-Za-z0-9_.-]", "", tag or os.environ.get("HERO_TAG") or time.strftime("import-%Y%m%d-%H%M%S")) or "import"
    base = tag; n = 2
    while tag in rec["candidates"]: tag = f"{base}-{n}"; n += 1
    im = normalize(path); os.makedirs(os.path.join(OUT, "candidates"), exist_ok=True); os.makedirs(os.path.join(OUT, "originals"), exist_ok=True)
    master = os.path.join(OUT, "candidates", f"{gid}-{tag}.png"); im.save(master + ".tmp", "PNG", optimize=True); os.replace(master + ".tmp", master)
    ext = os.path.splitext(path)[1].lower() or ".png"; orig = os.path.join(OUT, "originals", f"{gid}-{tag}{ext}"); shutil.copy2(path, orig)
    sha = hashlib.sha256(open(master, "rb").read()).hexdigest()
    rec["candidates"][tag] = {"file": f"candidates/{gid}-{tag}.png", "original": f"originals/{gid}-{tag}{ext}", "original_size": list(Image.open(path).size), "sha256": sha, "source": os.path.basename(path), "status": "candidate",
                              "provenance": os.environ.get("HERO_PROVENANCE", "generated:chatgpt"), "prompt_version": os.environ.get("HERO_PROMPT_VERSION"), "imported": time.strftime("%Y-%m-%dT%H:%M:%S"), "title_box": DEFAULT_TITLE if pinned_title else title_box(im)}
    _apply_current(by, reg, gid, tag); json.dump(reg, open(reg_p, "w", encoding="utf-8"), indent=1)
    if own: json.dump(cat, open(os.path.join(REPORTS, "catalog.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    return master

def select(gid: str, tag: str):
    cat, by = games(); reg_p, reg = _reg()
    if gid not in reg["games"] or tag not in reg["games"][gid].get("candidates", {}): raise ValueError(f"no candidate {tag} for {gid}")
    _apply_current(by, reg, gid, tag); json.dump(reg, open(reg_p, "w", encoding="utf-8"), indent=1); json.dump(cat, open(os.path.join(REPORTS, "catalog.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1); print(gid, "current ->", tag)

def status():
    cat, by = games(); reg_p, reg = _reg()
    have = [g for g, r in reg["games"].items() if r.get("current") and os.path.exists(os.path.join(OUT, g + ".png"))]
    nc = sum(len(r.get("candidates", {})) for r in reg["games"].values()); req = [d for d in (os.listdir(REQ) if os.path.isdir(REQ) else []) if os.path.isdir(os.path.join(REQ, d))]
    print(f"hero art: {len(have)} games with a current candidate ({nc} candidates kept), {len(req)} request packs, {len(cat['games']) - len(have)} games on the universal fallback")
    for g, r in reg["games"].items(): print(f"  {g} {r.get('title')}: current={r.get('current')} " + ", ".join(f"{t}[{c['status']}]" for t, c in r.get("candidates", {}).items()))

def verdict(gid: str, status: str, reason: str = "", tag: str = None):
    """approve or reject one candidate (the current one by default); only an approved current candidate is ever published"""
    cat, by = games(); reg_p, reg = _reg(); rec = reg["games"].get(gid)
    if not rec or not rec.get("candidates"): raise ValueError("no imported hero art for " + gid)
    tag = tag or rec.get("current"); c = rec["candidates"].get(tag)
    if not c: raise ValueError(f"no candidate {tag} for {gid}")
    import time
    c["status"] = status; c["reviewed"] = time.strftime("%Y-%m-%dT%H:%M:%S"); c["reason"] = reason
    if tag == rec.get("current"):
        rec["status"] = status; by[gid]["art_hero_status"] = status
        if status == "rejected":   # a rejected current candidate leaves the runtime path; its files stay recoverable under candidates/ and originals/
            p = os.path.join(OUT, gid + ".png")
            if os.path.exists(p): os.remove(p)
            rec["current"] = None
            for k in ("art_hero", "art_hero_source", "art_hero_sha256", "art_hero_status", "art_hero_provenance", "art_hero_title"): by[gid].pop(k, None)
    json.dump(reg, open(reg_p, "w", encoding="utf-8"), indent=1); json.dump(cat, open(os.path.join(REPORTS, "catalog.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1); print(gid, tag, status)

def remove(gid: str):
    """drop the runtime image only; candidates and originals stay on disk"""
    cat, by = games(); reg_p, reg = _reg()
    for k in ("art_hero", "art_hero_source", "art_hero_sha256", "art_hero_status", "art_hero_provenance", "art_hero_title"): by.get(gid, {}).pop(k, None)
    if gid in reg["games"]: reg["games"][gid]["current"] = None
    p = os.path.join(OUT, gid + ".png")
    if os.path.exists(p): os.remove(p)
    json.dump(reg, open(reg_p, "w", encoding="utf-8"), indent=1); json.dump(cat, open(os.path.join(REPORTS, "catalog.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1); print("removed runtime image for", gid)

def main(argv):
    if not argv or argv[0] not in ("request", "import", "status", "approve", "reject", "remove", "fallback", "select"): print(__doc__); return 2
    if argv[0] == "select": select(argv[1], argv[2]); return 0
    if argv[0] == "fallback": fallback(argv[1]); return 0
    if argv[0] == "status": status(); return 0
    if argv[0] in ("approve", "reject"):
        tag = argv[2] if len(argv) > 2 and argv[2].startswith("tag=") else None; reason = " ".join(x for x in argv[2:] if not x.startswith("tag="))
        verdict(argv[1], "approved" if argv[0] == "approve" else "rejected", reason, tag[4:] if tag else None); return 0
    if argv[0] == "remove": remove(argv[1]); return 0
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
