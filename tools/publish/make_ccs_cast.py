r"""The Classic Cart Studio cast: original pixel art drawn here, in code, for the website and the Windows app (the header parade, the footer
roster, the welcome screen, empty states, the guide's chapter bands and the interface icons). The characters and items are drawn at twice the
detail by ccs_cast_hd.py (higher-resolution pixel art); the interface icons and the walkway tile are written from the
grids below with one shared palette. Run it after changing a drawing or a grid:
  python tools/publish/make_ccs_cast.py        (needs Pillow; writes site-data/brand/ccs/*.png, index.json and preview.png)
Characters are 32 x 40 pixels shown as 16 x 20 display units ("density": 2 in index.json) with two frames (-0, -1); items twice their display
size; icons and the walkway tile one pixel per unit. Shown at whole-number scales with crisp pixels. Hero and Astro are new side views inspired by the Classic Cart Studio merchandise designs (Kingdoms Beyond
and Galactic Frontier), whose figures are seen only from behind there; the rest are new CCS characters. No game character is copied.
Part of Classic Cart Studio: GNU GPL v3 or later, like the rest of this repository."""
from __future__ import annotations
import json, os, sys
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ccs_cast_hd as HD
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.dirname(os.path.dirname(HERE)); OUT = os.path.join(REPO, "site-data", "brand", "ccs")
PAL = {".": None, "k": "1b1b26", "w": "f4f1ea", "g": "b9b6c3", "G": "6c6a78", "r": "e4202b", "R": "9a1018", "b": "2f6fe0", "B": "1d3f8a", "c": "58d0f0",
       "y": "f6c744", "Y": "c08a1c", "o": "f08a24", "n": "8a5530", "N": "4a2c14", "s": "f2b07a", "S": "c9804d", "p": "8a4ad8", "P": "5a2a96", "e": "46c25a",
       "E": "237a35", "v": "2a3550", "m": "e04fa0", "M": "a02670", "t": "3a3a4a"}
GROUND = ["eeeEeeeeeEeeeeeE", "EeeEEeeeEEeeeEEe", "EEnEEEnEEEEnEEEn", "nnnnnnnnnnnnnnnn", "nnnnnnNnnnnnnnnn", "nnnNnnnnnnnnNnnn", "nnnnnnnnnnnnnnnn", "nnnnnnnnnNnnnnnn",
          "nnNnnnnnnnnnnnnn", "nnnnnnnNNnnnnnnn", "nnnnnnnnnnnnnnNn", "nnnnnnnnnnnnnnnn", "nnnnNnnnnnnnnnnn", "nnnnnnnnnnnNnnnn", "nnnnnnnnnnnnnnnn", "NNNNNNNNNNNNNNNN"]
def square(rows):   # an icon on a square canvas (transparent rows above and below, columns at the sides), so square boxes scale it evenly
    w = max(map(len, rows)); n = max(w, len(rows)); rows = [r.ljust(w, ".") for r in rows]; left = (n - w) // 2
    rows = ["." * left + r + "." * (n - w - left) for r in rows]; top = (n - len(rows)) // 2
    return ["." * n] * top + rows + ["." * n] * (n - len(rows) - top)
ICONS = {   # interface icons (replacing the console software's own): id: (meaning, rows)
    "icon-cartridge": ("game cartridge", ["kkkkkkkkkkkk", "kgGgGgGgGggk", "kggggggggggk", "kgkkkkkkkkgk", "kgkrrrrrrkgk", "kgkrwwwwrkgk", "kgkrrrrrrkgk", "kgkkkkkkkkgk", "kggggggggggk", "kgGGGGGGGGgk", "kkkkkkkkkkkk"]),
    "icon-a": ("A button", ["...kkkkkk...", ".kkrrrrrrkk.", ".krrrwwrrrk.", "krrrwrrwrrrk", "krrrwrrwrrrk", "krrrwwwwrrrk", "krrrwrrwrrrk", "krrrwrrwrrrk", ".krrrrrrrrk.", ".kkrrrrrrkk.", "...kkkkkk..."]),
    "icon-b": ("B button", ["...kkkkkk...", ".kkrrrrrrkk.", ".krrwwwrrrk.", "krrrwrrwrrrk", "krrrwwwrrrrk", "krrrwrrwrrrk", "krrrwrrwrrrk", "krrrwwwrrrrk", ".krrrrrrrrk.", ".kkrrrrrrkk.", "...kkkkkk..."]),
    "icon-dpad": ("direction pad", ["....kkkk....", "....kggk....", "....kggk....", "kkkkkggkkkkk", "kggggggggggk", "kggggGGggggk", "kkkkkggkkkkk", "....kggk....", "....kggk....", "....kkkk...."]),
    "icon-start": ("START", ["............", "............", ".kkkkkkkkkk.", "kggggggggggk", "kggggggggggk", ".kkkkkkkkkk.", "............"]),
    "icon-disk": ("save", ["kkkkkkkkkkk.", "kbbkwwwwkbbk", "kbbkwwwwkbbk", "kbbkkkkkkbbk", "kbbbbbbbbbbk", "kbkkkkkkkkbk", "kbkwwwwwwkbk", "kbkwkkkkwkbk", "kbkwwwwwwkbk", "kkkkkkkkkkkk"]),
    "icon-warning": ("warning", [".....kk.....", "....kyyk....", "....kyyk....", "...kyyyyk...", "...kykkyk...", "..kyykkyyk..", "..kyykkyyk..", ".kyyyyyyyyk.", ".kyyykkyyyk.", "kyyyyyyyyyyk", "kkkkkkkkkkkk"]),
    "icon-1p-2p": ("players 1 and 2", ["..kkk....kkk..", ".kbbbk..krrrk.", ".kbsbk..krsrk.", "..kkk....kkk..", ".kbbbk..krrrk.", "kbbbbbkkrrrrrk", "kbbbbbkkrrrrrk", "kkkkkkkkkkkkkk"]),
}
def img(rows: list[str]) -> Image.Image:
    w = max(len(r) for r in rows); im = Image.new("RGBA", (w, len(rows)), (0, 0, 0, 0))
    for y, r in enumerate(rows):
        for x, ch in enumerate(r.ljust(w, ".")):
            col = PAL[ch]
            if col: im.putpixel((x, y), tuple(int(col[i:i + 2], 16) for i in (0, 2, 4)) + (255,))
    return im
def check(name: str, rows: list[str], w: int | None = None):
    bad = [i for i, r in enumerate(rows) if w and len(r) != w or any(c not in PAL for c in r)]
    if bad: raise SystemExit(f"{name}: rows {bad} are not {w} wide or use colors outside the palette")
def main() -> int:
    os.makedirs(OUT, exist_ok=True); index = {"schema": "ccs-brand-cast/1", "source": "drawn in code by tools/publish/make_ccs_cast.py (original Classic Cart Studio pixel art)",
                                              "license": "GPL-3.0-or-later (part of Classic Cart Studio)", "characters": [], "items": [], "icons": [], "tiles": []}
    files = []
    for cid, name, role, draw in HD.CHARS:
        for f in (0, 1): fn = f"{cid}-{f}.png"; HD.character(draw, f).save(os.path.join(OUT, fn)); files.append(fn)
        index["characters"].append({"id": cid, "name": name, "role": role, "frames": [f"{cid}-0.png", f"{cid}-1.png"], "size": [16, 20], "density": 2})
    for iid, (name, size, draw) in HD.ITEMS.items():
        fn = f"{iid}.png"; HD.item(size, draw).save(os.path.join(OUT, fn)); files.append(fn); index["items"].append({"id": iid, "name": name, "file": fn, "size": [size[0] // 2, size[1] // 2], "density": 2})
    for iid, (name, rows) in ICONS.items():
        check(iid, rows); rows = square(rows); fn = f"{iid}.png"; img(rows).save(os.path.join(OUT, fn)); files.append(fn); index["icons"].append({"id": iid, "meaning": name, "file": fn, "size": [max(map(len, rows)), len(rows)]})
    check("ground", GROUND, 16); img(GROUND).save(os.path.join(OUT, "ground.png")); files.append("ground.png"); index["tiles"].append({"id": "ground", "file": "ground.png", "size": [16, 16]})
    json.dump(index, open(os.path.join(OUT, "index.json"), "w", encoding="utf-8", newline="\n"), indent=1)
    # a preview sheet (not shipped): every picture at 4x on the walkway
    pics = [Image.open(os.path.join(OUT, f)) for f in files]; S = 4; W = sum(p.width * S + 8 for p in pics[:24]) + 8
    sheet = Image.new("RGBA", (W, 2 * (40 * S + 24) + 16 * S), (24, 24, 34, 255)); x = 8; y = 8
    for i, p in enumerate(pics):
        if i == 24: x = 8; y += 40 * S + 24
        sheet.alpha_composite(p.resize((p.width * S, p.height * S), Image.NEAREST), (x, y)); x += p.width * S + 8
    g = img(GROUND).resize((16 * S, 16 * S), Image.NEAREST)
    for gx in range(0, W, 16 * S): sheet.alpha_composite(g, (gx, sheet.height - 16 * S))
    os.makedirs(os.path.join(REPO, "build"), exist_ok=True); sheet.save(os.path.join(REPO, "build", "ccs-cast-preview.png"))
    print(f"{len(HD.CHARS)} characters ({2 * len(HD.CHARS)} frames), {len(HD.ITEMS)} items, {len(ICONS)} icons, 1 walkway tile -> {OUT}")
    return 0
if __name__ == "__main__": sys.exit(main())
