r"""The five shared HOME scenes: static backgrounds composed from human-made, openly licensed environment art by Luis Zuno (Ansimuz),
with each scene's image, sources, license and changes recorded in site-data/art/scenes/index.json.

  python tools/publish/make_scenes.py <sources folder> [--check]

<sources folder> holds the artist's own layer files exactly as published (each file's SHA-256 is checked against the record below before it is
used).
Each scene is made in three plain steps and nothing else:
  1. the environment's background layers are stacked in the artist's order at their native pixel size (a layer drawn to repeat sideways is
     repeated sideways; nothing is drawn, filled, extended, retouched or generated, and no tile sheet, character, sprite, title text or
     preview picture is used);
  2. the stack is cropped to a 16:9 box at native size (480 x 270 or 384 x 216 pixels); the layer positions follow the artist's own
     preview of the environment where the pack has one;
  3. that box is enlarged by a whole number (4 or 5) with nearest-neighbor sampling to 1920 x 1080, so every original pixel stays a sharp
     square, and saved as a PNG with no metadata. Static: no animation, parallax or frames on any surface.
--check rebuilds every scene in memory and compares it with the files already there.
Part of Classic Cart Studio: GNU GPL v3 or later (this program). The artwork is the artist's: C01 Magic Cliffs under CC BY 3.0 (credited, with
the changes above), the others under CC0 (credited by choice); see THIRD_PARTY_NOTICES.md."""
from __future__ import annotations
import argparse, hashlib, io, json, os, sys
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.dirname(os.path.dirname(HERE))
SCENES = os.path.join(REPO, "site-data", "art", "scenes"); INDEX = os.path.join(SCENES, "index.json")
OUT_SIZE = (1920, 1080)

MC = "magic-original/Magic-Cliffs-Environment/PNG/"
FI = "forest-illusion/Forest of Illusion Files/Layers/"
NT = "gothic-castles/ gothicvania patreon collection/night-town-background-files/layers/night-town-background-"
DC = "gothic-castles/ gothicvania patreon collection/Old-dark-Castle-tileset-Files/PNG/"
SM = "sideview-fantasy/Sideview Fantasy Patreon Collection/Environments/sunny-rocky-mountains/PNG/sunny-mountains-"
# every source file a scene may use, with its published SHA-256 (from the selection record)
SOURCES = {
    MC + "sky.png": "546a7b1d41872c72db182ea6c029b7833d4f5e586d91ac6d097a4350daec4306",
    MC + "clouds.png": "378bff555e7e3944dd9e74f40c77c6b52102c06ff5959405d571e80e34f3a313",
    MC + "sea.png": "bb06b53e63f2039311175a8945d5883e746e616af15e48d7eb9c52dac5b9f860",
    MC + "far-grounds.png": "5fccdc75271a3fc37964c35b1eb3c0ad86b91a6a73943dbe7f0c8cf9db6a088c",
    FI + "back.png": "2bad4228ba3a64e220b6ac1090106df35b9bc3d3d41eeed7cf2e3771f1e439c3",
    FI + "middle.png": "2a8978b4281afb279bb2036d24919f0a5cf426f1573d946fbdb7175616e9bc11",
    NT + "sky.png": "2e61ea60be5ca3fee5e23a9b7501a89e89259d8f274e5908bedd6e4c988d6097",
    NT + "clouds.png": "e95503a89ba77ec2f6481504d749065b1dba90c1a497bba50bd65e592160d20b",
    NT + "mountains.png": "95a116e92d7f90649dcb100a33e6af08d15df6c59347806281f396d6d4832f98",
    NT + "far-buildings.png": "dd550f031548c5ba14eb1d020e503607365da16fd54b5b6763f120067045a576",
    NT + "forest.png": "8856996283340f232a71a08ec66d4d251651568d6fdc3a61e974af1acda2e6ab",
    NT + "town.png": "125cf6f1f45916edbc1ae267e8310e26c5bbeed95a12e2c7afca4fc59758ab15",
    DC + "old-dark-castle-interior-background.png": "feec0704cdcd2c8dbe7e6d39d4841a13ed49569a848ac08d413ac26d2197ab1d",
    SM + "sky.png": "5ab8a199f3bc4ef11ff54c5a7b2f0b6c8a5add901a8d9f4ecf80250d18ed4e86",
    SM + "far-back.png": "ae4187241d0baf8ac7c65b7c98fa9c900fbd8332c453947acd4d7b4cb600b096",
    SM + "hills.png": "257e29d3cab4a43e3526b40d28d12691fc6165272e409f22d8b37eb66da2cbdd",
}
# each scene: the stack (layer, x, y, repeat sideways), the native canvas, the 16:9 crop box and the whole-number scale
RECIPES = {
    "magic-cliffs": {"canvas": (480, 304), "layers": [(MC + "sky.png", 0, 0, True), (MC + "clouds.png", -32, 24, True),
                                                        (MC + "sea.png", 0, 208, True), (MC + "far-grounds.png", -68, 194, False)],
                     "crop": (0, 34, 480, 304), "scale": 4},
    "forest-of-illusion": {"canvas": (480, 272), "layers": [(FI + "back.png", 0, 0, True), (FI + "middle.png", 0, 0, True)],
                           "crop": (48, 19, 432, 235), "scale": 5},
    "town-at-night": {"canvas": (512, 224), "layers": [(NT + "sky.png", 0, 0, True), (NT + "clouds.png", 0, 0, True), (NT + "mountains.png", 0, 0, True),
                                                        (NT + "far-buildings.png", 256, 118, True), (NT + "forest.png", 0, 128, True),
                                                        (NT + "town.png", 0, 125, False)],
                      "crop": (64, 8, 448, 224), "scale": 5},
    "old-dark-castle": {"canvas": (960, 304), "layers": [(DC + "old-dark-castle-interior-background.png", 0, 0, False)],
                        "crop": (258, 17, 738, 287), "scale": 4},   # the lit window left of the HOME title, plain wall behind it
    "sunny-rocky-mountains": {"canvas": (384, 224), "layers": [(SM + "sky.png", 0, 0, True), (SM + "far-back.png", 0, 0, True), (SM + "hills.png", 0, 0, True)],
                              "crop": (0, 8, 384, 224), "scale": 5},
}

def sha(b: bytes) -> str: return hashlib.sha256(b).hexdigest()

def load(src: str, rel: str) -> Image.Image:
    p = os.path.join(src, *rel.split("/")); b = open(p, "rb").read(); want = SOURCES[rel]
    if not sha(b).startswith(want): raise SystemExit(f"{rel}: not the artist's published file (SHA-256 {sha(b)[:16]}, expected {want[:16]})")
    im = Image.open(io.BytesIO(b)); im.load(); return im.convert("RGBA")

def compose(src: str, sid: str) -> tuple[bytes, list]:
    r = RECIPES[sid]; cw, ch = r["canvas"]; canvas = Image.new("RGBA", (cw, ch), (0, 0, 0, 0)); used = []
    for rel, x, y, rep in r["layers"]:
        im = load(src, rel); used.append(rel)
        xs = range(x - ((x // im.width) + 1) * im.width if rep else x, cw, im.width) if rep else [x]
        for xx in xs:
            if xx + im.width <= 0 or xx >= cw: continue
            layer = Image.new("RGBA", (cw, ch), (0, 0, 0, 0)); layer.paste(im, (xx, y)); canvas = Image.alpha_composite(canvas, layer)
    box = canvas.crop(r["crop"]); s = r["scale"]
    if box.size[0] * s != OUT_SIZE[0] or box.size[1] * s != OUT_SIZE[1]: raise SystemExit(f"{sid}: crop {box.size} x{s} is not {OUT_SIZE}")
    if box.getextrema()[3][0] < 255: raise SystemExit(f"{sid}: the crop has a transparent pixel (the layers must cover the whole box)")
    out = box.convert("RGB").resize(OUT_SIZE, Image.NEAREST)   # whole-number nearest-neighbor: each original pixel becomes an s x s square
    buf = io.BytesIO(); out.save(buf, "PNG", optimize=True); return buf.getvalue(), used

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("sources"); ap.add_argument("--check", action="store_true"); a = ap.parse_args()
    idx = json.load(open(INDEX, encoding="utf-8")); bad = []
    for sid, sc in idx["scenes"].items():
        b, used = compose(a.sources, sid); dst = os.path.join(REPO, "site-data", *sc["file"].split("/"))
        rec = {"sha256": sha(b), "bytes": len(b), "size": list(OUT_SIZE), "native": [RECIPES[sid]["crop"][2] - RECIPES[sid]["crop"][0], RECIPES[sid]["crop"][3] - RECIPES[sid]["crop"][1]],
               "scale": RECIPES[sid]["scale"], "layers": [{"file": u.split("/")[-1], "sha256": SOURCES[u]} for u in used]}
        if a.check:
            if not os.path.isfile(dst) or sha(open(dst, "rb").read()) != rec["sha256"] or any(sc.get(k) != v for k, v in rec.items()): bad.append(sid)
            continue
        with open(dst, "wb") as f: f.write(b)
        sc.update(rec); print(f"{sid}: {len(b):,} bytes {rec['sha256'][:16]}")
    if a.check:
        print("SCENES " + ("OK" if not bad else "DIFFER: " + ", ".join(bad))); return 1 if bad else 0
    json.dump(idx, open(INDEX, "w", encoding="utf-8", newline="\n"), indent=1); return 0

if __name__ == "__main__": sys.exit(main())
