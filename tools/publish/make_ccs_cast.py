r"""The Classic Cart Studio cast: original pixel art drawn here, in code, for the website and the Windows app (the header parade, the footer
roster, the welcome screen, empty states, the guide's chapter bands and the interface icons). Every picture is written from the grids below
with one shared palette (no other source); run it after changing a grid:
  python tools/publish/make_ccs_cast.py        (needs Pillow; writes site-data/brand/ccs/*.png, index.json and preview.png)
Characters are 16 x 20 pixels with two walking frames (-0, -1), items and icons their own sizes, the walkway tile 16 x 16. Shown at whole-
number scales with crisp pixels. Hero and Astro are new side views inspired by the Classic Cart Studio merchandise designs (Kingdoms Beyond
and Galactic Frontier), whose figures are seen only from behind there; the rest are new CCS characters. No game character is copied.
Part of Classic Cart Studio: GNU GPL v3 or later, like the rest of this repository."""
from __future__ import annotations
import json, os, sys
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.dirname(os.path.dirname(HERE)); OUT = os.path.join(REPO, "site-data", "brand", "ccs")
PAL = {".": None, "k": "1b1b26", "w": "f4f1ea", "g": "b9b6c3", "G": "6c6a78", "r": "e4202b", "R": "9a1018", "b": "2f6fe0", "B": "1d3f8a", "c": "58d0f0",
       "y": "f6c744", "Y": "c08a1c", "o": "f08a24", "n": "8a5530", "N": "4a2c14", "s": "f2b07a", "S": "c9804d", "p": "8a4ad8", "P": "5a2a96", "e": "46c25a",
       "E": "237a35", "v": "2a3550", "m": "e04fa0", "M": "a02670", "t": "3a3a4a"}
LEGS = {   # shared walking legs (rows 15-19): two frames; the letter is the leg color
    "a": [["....kXk..kXk....", "....kXk..kXk....", "...kXXk...kXk...", "...kkkk...kXXk..", "..........kkkk.."],
          [".....kXkkXk.....", ".....kXkkXk.....", ".....kXkkXk.....", "....kXXkXXk.....", "....kkkkkkk....."]],
}
def legs(color, frame): return [r.replace("X", color) for r in LEGS["a"][frame]]
# ---- characters: (id, name, role, body rows 0-14, leg color) ----
CHARS = [
    ("carty", "Carty", "the Classic Cart Studio mascot, a walking game cartridge", [
        "..kkkkkkkkkkkk..", ".kgGgGgGgGgGggk.", ".kGgGgGgGgGgGgk.", ".kggggggggggggk.", ".kgkkkkkkkkkkgk.", ".kgkrrrrrrrrkgk.", ".kgkrwwrrwwrkgk.",
        ".kgkrwkrrwkrkgk.", ".kgkrrrrrrrrkgk.", ".kgkrrkkkkrrkgk.", ".kgkrrrrrrrrkgk.", ".kgkkkkkkkkkkgk.", ".kggggggggggggk.", ".kgGGGGGGGGGGgk.", "..kkkkkkkkkkkk.."], "G"),
    ("padd", "Padd", "a walking controller, Carty's best friend", [
        "................", "................", "................", "................", "................", "................", "................",
        ".kkkkkkkkkkkkkk.", "kggggggggggggggk", "kggkgwkgwkgggggk", "kgkkkggggggrrgrk", "kggkggGGGgggrrgk", "kggggggggggggggk", "kggggggggggggggk", ".kkkkkkkkkkkkkk."], "G"),
    ("hero", "Hero", "a caped adventurer (inspired by the Kingdoms Beyond design)", [
        "......kk.k......", ".....knnknk.....", "....knnnnnnk....", "...knnnnnnnnk...", "...knnssssssk...", "...knssssksssk..", "...knssssssssk..",
        "....kssssSssk...", "..kRRkkkkkkk....", ".kRRRkbbbbbk.k..", ".kRRRkbybbbkkyk.", ".kRRRkbbbbbk.gk.", ".kRRRkbbbbbk.gk.", ".kRRRkbbbbbk.gk.", "..kRRkkkkkkk.kk."], "n"),
    ("astro", "Astro", "a space explorer (inspired by the Galactic Frontier design)", [
        "....kkkkkk......", "...kwwwwwwk.....", "..kwwwwwwwwk....", "..kwwkvvvvvk....", "..kwkvvcvvvvk...", "..kwkvvvvvvvk...", "..kwwkvvvvvk....",
        "...kwwwwwwk.....", "..kgkkwwwwk.....", ".kggkwwrwwwk....", ".kggkwwwwwwkk...", ".kggkwwwwwwwk...", ".kggkwwwwwwk....", "..kkkwwwwwwk....", "....kkkkkkkk...."], "w"),
    ("boltbot", "Bolt Bot", "a friendly robot", [
        "......kyk.......", ".......k........", "...kkkkkkkkk....", "...kgggggggk....", "...kgcgggcgk....", "...kgggggggk....", "...kgkkkkkgk....",
        "...kkkkkkkkk....", "....kGGGGGk.....", "..kkgggggggkk...", ".kGkgrgygrgkGk..", ".kGkgggggggkGk..", ".kkkgggggggkkk..", "...kgggggggk....", "...kkkkkkkkk...."], "G"),
    ("wizzy", "Wizzy", "a wizard who keeps the save slots", [
        "........kk......", ".......kPk......", "......kppk......", ".....kppypk.....", "....kpppppk.....", "...kkkkkkkkk....", "....kssssk......",
        "....ksskssk.....", "...kwwwwwwk.....", "...kwwwwwwk.....", "..kpkwwwwkpk....", "..kppkwwkppk....", "..kpppppppppk...", ".kpppppppppppk..", ".kPPPPPPPPPPPk.."], "P"),
    ("dino", "Dino", "a small dinosaur who loves racing games", [
        "........kkkk....", ".......keeeek...", "......keekeeek..", "......keeeeeeek.", "......keeewwwk..", ".......keeeek...", "..k....keeek....",
        ".kek..keeeek....", ".keekkeeeeeek...", "..keeeeewweek...", "...keeeewwwek...", "...keeeewwek....", "....keeeeek.....", ".....kkkkk......", "................"], "E"),
    ("ghosty", "Ghosty", "a shy ghost from the haunted levels", [
        "................", ".....kkkkkk.....", "....kwwwwwwk....", "...kwwwwwwwwk...", "...kwkwwwkwwk...", "...kwkwwwkwwk...", "...kwwwwwwwwk...",
        "...kwwwmmwwwk...", "...kwwwwwwwwk...", "...kwwwwwwwwk...", "...kwwwwwwwwk...", "...kwwwwwwwwk...", "...kwwwwwwwwk...", "...kwkwwkwwkwk..", "...kk.kk.kk.kk.."], None),
    ("slimo", "Slimo", "a bouncy slime", [
        "................", "................", "................", "................", "................", "................", "................",
        "................", "......kkkk......", "....kkeeeekk....", "...keeeeeeeek...", "..keewkeewkeek..", "..keeeeeeeeeek..", "..keeekkkkeeek..", "...kkkkkkkkkk..."], None),
    ("batty", "Batty", "a bat from the cave levels", [
        "................", "................", "................", "................", "kk............kk", "kpk...kkkk...kpk", "kppk.kppppk.kppk",
        "kpppkpwpwppkpppk", ".kpppppppppppk..", "..kkppkkkkppkk..", "....kk....kk....", "................", "................", "................", "................"], None),
    ("racer", "Racer", "a neon racer (inspired by the Neon Highway design)", [
        "................", "................", "................", "................", "................", "................", "................",
        "......kkkkk.....", ".....kmmcckk....", "..kkkmmmccmkk...", ".kmmmmmmmmmmmmk.", ".kmmyMMMMMMMymk.", ".kkkkkkkkkkkkkk.", "..kGGk....kGGk..", "..kkkk....kkkk.."], None),
    ("knightly", "Knightly", "a knight guarding the collections", [
        "......kkkk......", ".....kggggk.....", "....kggkkggk....", "....kgkccckgk...", "....kggkkggk....", ".....kggggk.....", "....kkkkkkkk....",
        "...kgggggggk....", "..kbkgggggkbk...", ".kbbkggrggkbbk..", ".kbbkgrrrgkbbk..", ".kbbkggrggkbbk..", "..kbkgggggkbk...", "...kgggggggk....", "...kkkkkkkkk...."], "G"),
]
FLOATERS = {"ghosty", "batty"}   # they float or flap: their second frame moves the whole picture up one pixel
ITEMS = {   # id: (name, rows)
    "chest": ("Treasure chest", ["..kkkkkkkkkkkk..", ".knnnnnnnnnnnnk.", "knNnnnnnnnnnnNnk", "knnnnnnnnnnnnnnk", "kkkkkkkyykkkkkkk", "knnnnnnyynnnnnnk", "knNnnnnkknnnnNnk",
                                 "knnnnnnnnnnnnnnk", "knnnnnnnnnnnnnnk", "kkkkkkkkkkkkkkkk"]),
    "star": ("Star", [".....kk.....", ".....kyk....", "....kyyk....", "kkkkkyyykkkk", "kyyyyyyyyyyk", ".kyyyyyyyyk.", "..kyyyyyyk..", "..kyykkyyk..", ".kyk....kyk.", ".kk......kk."]),
    "gem": ("Gem", ["..kkkkkk..", ".kcwccccck", "kccwccccck", "kkkkkkkkkk", ".kcccccck.", "..kcccck..", "...kcck...", "....kk...."]),
    "heart": ("Heart", [".kkk.kkk..", "krrrkrrrk.", "krwrrrrrrk", "krrrrrrrrk", ".krrrrrrk.", "..krrrrk..", "...krrk...", "....kk...."]),
    "coin": ("Token", ["..kkkk..", ".kyyyyk.", "kyykkyyk", "kyykyyyk", "kyykyyyk", "kyykkyyk", ".kyyyyk.", "..kkkk.."]),
}
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
    for cid, name, role, body, leg in CHARS:
        check(cid, body, 16)
        for f in (0, 1):
            rows = body + (legs(leg, f) if leg else ["." * 16] * 5)
            if cid in FLOATERS and f == 1: rows = rows[1:] + ["." * 16]
            check(cid, rows, 16); fn = f"{cid}-{f}.png"; img(rows).save(os.path.join(OUT, fn)); files.append(fn)
        index["characters"].append({"id": cid, "name": name, "role": role, "frames": [f"{cid}-0.png", f"{cid}-1.png"], "size": [16, 20]})
    for iid, (name, rows) in ITEMS.items():
        check(iid, rows); fn = f"{iid}.png"; img(rows).save(os.path.join(OUT, fn)); files.append(fn); index["items"].append({"id": iid, "name": name, "file": fn, "size": [max(map(len, rows)), len(rows)]})
    for iid, (name, rows) in ICONS.items():
        check(iid, rows); rows = square(rows); fn = f"{iid}.png"; img(rows).save(os.path.join(OUT, fn)); files.append(fn); index["icons"].append({"id": iid, "meaning": name, "file": fn, "size": [max(map(len, rows)), len(rows)]})
    check("ground", GROUND, 16); img(GROUND).save(os.path.join(OUT, "ground.png")); files.append("ground.png"); index["tiles"].append({"id": "ground", "file": "ground.png", "size": [16, 16]})
    json.dump(index, open(os.path.join(OUT, "index.json"), "w", encoding="utf-8", newline="\n"), indent=1)
    # a preview sheet (not shipped): every picture at 4x on the walkway
    pics = [Image.open(os.path.join(OUT, f)) for f in files]; S = 4; W = sum(p.width * S + 8 for p in pics[:24]) + 8
    sheet = Image.new("RGBA", (W, 2 * (20 * S + 24) + 16 * S), (24, 24, 34, 255)); x = 8; y = 8
    for i, p in enumerate(pics):
        if i == 24: x = 8; y += 20 * S + 24
        sheet.alpha_composite(p.resize((p.width * S, p.height * S), Image.NEAREST), (x, y)); x += p.width * S + 8
    g = img(GROUND).resize((16 * S, 16 * S), Image.NEAREST)
    for gx in range(0, W, 16 * S): sheet.alpha_composite(g, (gx, sheet.height - 16 * S))
    os.makedirs(os.path.join(REPO, "build"), exist_ok=True); sheet.save(os.path.join(REPO, "build", "ccs-cast-preview.png"))
    print(f"{len(CHARS)} characters ({2 * len(CHARS)} frames), {len(ITEMS)} items, {len(ICONS)} icons, 1 walkway tile -> {OUT}")
    return 0
if __name__ == "__main__": sys.exit(main())
