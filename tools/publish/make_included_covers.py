"""The Included Games' covers: each game's own screen, as its author published it, on a plain black card (no lettering, logo or drawing).

The screens come from the games' own published archives, which Classic Cart Studio already hosts unchanged beside each game (their
licenses, GPL-3.0-or-later, cover the program and its graphics): Thwaite's title screen (docs/manual_title.png) and Concentration Room's
in-game screen (docs/croom_screenshot01.png). Each is enlarged 2x with nearest-neighbor pixels and centered on a black 600 x 840 card,
the shape of a cover, so every view shows the whole screen without cropping. The result and its source are recorded in
site-data/included/index.json (cover: file, sha256, kind, source, source_sha256); nothing is generated or retouched.
  python tools/publish/make_included_covers.py            write the covers and their records
  python tools/publish/make_included_covers.py --check    fail unless every recorded cover is exactly what its source makes
Part of Classic Cart Studio: GNU GPL v3 or later."""
from __future__ import annotations
import hashlib, io, json, os, sys
from PIL import Image
REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
INC = os.path.join(REPO, "site-data", "included"); W, H, SCALE = 600, 840, 2
SOURCES = {   # key: (source path inside the game's hosted folder, its SHA-256, what it shows)
    "thwaite": ("source/thwaite/docs/manual_title.png", "title screen"),
    "concentration-room": ("source/concentration-room/docs/croom_screenshot01.png", "game screen"),
}
def sha(b: bytes) -> str: return hashlib.sha256(b).hexdigest()

def compose(src: bytes) -> bytes:
    im = Image.open(io.BytesIO(src)).convert("RGB")
    if im.width * SCALE > W or im.height * SCALE > H: raise SystemExit(f"a screen of {im.width} x {im.height} does not fit the card at {SCALE}x")
    big = im.resize((im.width * SCALE, im.height * SCALE), Image.NEAREST)
    card = Image.new("RGB", (W, H), (0, 0, 0)); card.paste(big, ((W - big.width) // 2, (H - big.height) // 2))
    out = io.BytesIO(); card.save(out, "PNG", optimize=True); return out.getvalue()

def main() -> int:
    check = "--check" in sys.argv[1:]; p = os.path.join(INC, "index.json"); idx = json.load(open(p, encoding="utf-8")); bad = []
    for g in idx["games"]:
        if g["key"] not in SOURCES: continue
        rel, kind = SOURCES[g["key"]]; src = open(os.path.join(INC, *rel.split("/")), "rb").read(); png = compose(src)
        rec = {"file": f"art/{g['key']}-cover.png", "sha256": sha(png), "kind": kind, "source": rel, "source_sha256": sha(src),
               "credit": f"{g['creator']}'s own {kind} of {g['title']}, from the game's published archive ({g['license']})"}
        out = os.path.join(INC, *rec["file"].split("/"))
        if check:
            if g.get("cover") != rec or not os.path.isfile(out) or sha(open(out, "rb").read()) != rec["sha256"]: bad.append(g["key"])
            continue
        os.makedirs(os.path.dirname(out), exist_ok=True); open(out, "wb").write(png); g["cover"] = rec
        print(f"{g['key']}: {rec['file']} {len(png):,} bytes {rec['sha256'][:16]} from {rel}")
    if check:
        print("RESULT " + ("OK: the Included Games' covers match their sources" if not bad else f"FAIL: {', '.join(bad)}")); return 1 if bad else 0
    with open(p, "w", encoding="utf-8", newline="\n") as f: f.write(json.dumps(idx, indent=1, ensure_ascii=False))   # the file's own form (no final newline)
    return 0

if __name__ == "__main__": sys.exit(main())
