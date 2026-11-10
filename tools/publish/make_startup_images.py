r"""The five console startup images (D-113): traceable 1280 x 720 derivatives of the owner's approved masters, and their manifest.

  python tools/publish/make_startup_images.py <masters folder> [--out tools/publish/assets/startup] [--check]

The masters (1672 x 941 PNG, kept privately by the owner and never changed or published) are read only and checked by their SHA-256.
Each derivative is the whole picture at the console's output resolution: the master's exact 16:9 box (a quarter of a source pixel off
the top and the bottom: 941 rows hold 940.5 rows of 16:9) resampled with Lanczos to 1280 x 720, no stretching and nothing else cropped,
saved as a baseline JPEG at quality 92 without chroma subsampling (the small tagline stays clean on a TV; the console decodes it with
the HOME's own decoder). A 480 x 270 thumbnail of each serves the Studio's preview of the setting. --check rebuilds in memory and
compares with the files already there (nothing is written). Copyright (C) 2026 Rance9, GPL-3.0-or-later (this program; the images are
not covered by it: THIRD_PARTY_NOTICES.md, Startup images)."""
from __future__ import annotations
import argparse, hashlib, io, json, os, sys
from PIL import Image

SCHEMA = "ccs-startup-images/1"
IMAGES = [   # id, name, master file (the owner's names, never renamed), master SHA-256
    ("forgotten-keep", "Forgotten Keep", "Classic-Cart-Studio-Background-Forgotten-Keep-Theme.png", "6012364fe9da8f79aca973f6c704e53c1b39e0f09a26df2b4738bdfe801fbbdc"),
    ("galactic-frontier", "Galactic Frontier", "Classic-Cart-Studio-Background-Galactic-Frontier-Theme.png", "d2db7707abef329a02db36ffbef9e2a0f1c19e3538dacee4b147fd23eb54ce67"),
    ("kingdoms-beyond", "Kingdoms Beyond", "Classic-Cart-Studio-Background-Kingdoms-Beyond-Theme.png", "a3cb535fbd3814a2713eb03ffeba326fa44d9ab1873da2e4c88d44bd134ab3de"),
    ("neon-highway", "Neon Highway", "Classic-Cart-Studio-Background-Neon-Highway-Theme.png", "1bf71790981718288a0d851a3eef91458d1970870c890f6fc8d49227ee39d5e8"),
    ("pixel-worlds", "Pixel Worlds", "Classic-Cart-Studio-Background-Pixel-Worlds-Theme.png", "10b53330d8be9096eb42c444b7d8966ed539d386ee4b342d0c0196ddc684c55f"),
]
MASTER_SIZE = (1672, 941); OUT_SIZE = (1280, 720); THUMB_SIZE = (480, 270); QUALITY = 92; THUMB_QUALITY = 85
HERE = os.path.dirname(os.path.abspath(__file__)); DEFAULT_OUT = os.path.join(HERE, "assets", "startup")

def box_16_9(w: int, h: int) -> tuple[float, float, float, float]:
    """the largest centered 16:9 box of a w x h picture (fractional: a quarter pixel is resampled, not rounded away)"""
    if w * 9 >= h * 16: bw = h * 16 / 9; return ((w - bw) / 2, 0.0, (w + bw) / 2, float(h))
    bh = w * 9 / 16; return (0.0, (h - bh) / 2, float(w), (h + bh) / 2)

def jpeg(im: Image.Image, q: int) -> bytes:
    b = io.BytesIO(); im.save(b, "JPEG", quality=q, subsampling=0, optimize=True, progressive=False); return b.getvalue()

def build(masters: str) -> tuple[dict, dict[str, bytes]]:
    files: dict[str, bytes] = {}; out = []
    for iid, name, fname, want in IMAGES:
        p = os.path.join(masters, fname); data = open(p, "rb").read(); got = hashlib.sha256(data).hexdigest()
        if got != want: raise SystemExit(f"{fname}: SHA-256 {got} is not the approved master's {want}; nothing was made")
        im = Image.open(io.BytesIO(data)); im.load()
        if im.size != MASTER_SIZE: raise SystemExit(f"{fname}: {im.size[0]}x{im.size[1]}, not the approved {MASTER_SIZE[0]}x{MASTER_SIZE[1]}")
        rgb = im.convert("RGB"); box = box_16_9(*rgb.size)
        big = rgb.resize(OUT_SIZE, Image.Resampling.LANCZOS, box=box); small = rgb.resize(THUMB_SIZE, Image.Resampling.LANCZOS, box=box)
        d, t = jpeg(big, QUALITY), jpeg(small, THUMB_QUALITY); dn, tn = f"startup-{iid}.jpg", f"startup-{iid}-thumb.jpg"; files[dn] = d; files[tn] = t
        out.append({"id": iid, "name": name, "master": {"file": fname, "sha256": want, "bytes": len(data), "size": list(MASTER_SIZE)},
                    "file": dn, "sha256": hashlib.sha256(d).hexdigest(), "bytes": len(d), "size": list(OUT_SIZE),
                    "thumb": {"file": tn, "sha256": hashlib.sha256(t).hexdigest(), "bytes": len(t), "size": list(THUMB_SIZE)},
                    "derivation": {"source_box": [round(x, 4) for x in box], "resample": "lanczos", "format": "jpeg baseline", "quality": QUALITY, "subsampling": "4:4:4"}})
    man = {"schema": SCHEMA, "default": "random", "min_visible_ms": 5000, "images": out,
           "note": "Derivatives of the owner's approved masters (not published). Creative assets, not covered by the software license (THIRD_PARTY_NOTICES.md)."}
    files["startup-images.json"] = (json.dumps(man, indent=1) + "\n").encode("utf-8")
    return man, files

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("masters"); ap.add_argument("--out", default=DEFAULT_OUT); ap.add_argument("--check", action="store_true"); a = ap.parse_args()
    man, files = build(a.masters)
    if a.check:
        bad = [n for n, b in files.items() if not os.path.isfile(os.path.join(a.out, n)) or open(os.path.join(a.out, n), "rb").read() != b]
        print(json.dumps({"same": not bad, "different": bad, "pillow": Image.__version__})); sys.exit(1 if bad else 0)
    os.makedirs(a.out, exist_ok=True)
    for n, b in files.items():
        p = os.path.join(a.out, n); tmp = p + ".new"
        with open(tmp, "wb") as f: f.write(b)
        os.replace(tmp, p)
    print(json.dumps({"out": a.out, "images": [(i["id"], i["bytes"], i["sha256"][:16]) for i in man["images"]], "pillow": Image.__version__}, indent=1))

if __name__ == "__main__": main()
