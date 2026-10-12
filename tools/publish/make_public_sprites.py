"""Original pixel art for the PUBLIC build of Classic Cart Studio.

Everything here is drawn for this project from the pixel maps below (no Nintendo characters, no console-firmware crops, no
box art, no wordmark). The private development build keeps using the firmware crops that never leave this computer.
Output: <out>/brand/public/*.png + index.json (width/height per file, like the OEM index) + provenance.json.

Usage: python make_public_sprites.py <out_dir>      (Pillow required)
"""
from __future__ import annotations
import os, sys, json
from PIL import Image, ImageDraw

PAL = {".": None, "k": (16, 16, 20), "w": (244, 244, 240), "r": (228, 0, 15), "R": (150, 0, 10), "o": (240, 136, 40), "y": (242, 182, 50), "Y": (255, 232, 120),
       "g": (31, 171, 74), "G": (18, 110, 48), "b": (92, 148, 252), "B": (40, 80, 200), "c": (120, 220, 240), "p": (180, 96, 220), "P": (110, 40, 150), "s": (201, 201, 206), "S": (120, 120, 128),
       "t": (232, 184, 130), "T": (160, 100, 60), "n": (90, 60, 40), "m": (255, 140, 170), "d": (60, 60, 70), "e": (235, 235, 230), "l": (150, 230, 120)}

# ---- original characters (names and designs are this project's) -----------------------------------------------------------------
CHARS = {
"byte": ("Byte — a round helper robot", """
....ssss....
..ssssssss..
.sskkssskss.
.skccssckss.
.sskkssskss.
.ssssssssss.
..sskkkkss..
...ssssss...
..rrrrrrrr..
.rrbbrrbbrr.
.rrrrrrrrrr.
..rr....rr..
..dd....dd..
.ddd....ddd.
"""),
"pip": ("Pip — a friendly ghost", """
....cccc....
..cccccccc..
.cccccccccc.
.cckkccckkc.
.cckwccckwc.
.cccccccccc.
cccccccccccc
cccccccccccc
cccccccccccc
cccccccccccc
cc.cccccc.cc
c...cccc...c
"""),
"kat": ("Kat — an alley cat", """
.o........o.
.oo......oo.
.ooooooooo..
.okoooookoo.
.oooommoooo.
.ooooooooo..
..oooooooo..
..oooooooo..
.oooooooooo.
.oooooooooo.
..oo.oo.oo..
..oo.oo.oo.o
"""),
"sir-pixel": ("Sir Pixel — a pocket knight", """
....ssss....
...ssssss...
...sSkkSs...
...ssssss...
..rrssssrr..
.rrrrrrrrrr.
.rrryrryrrr.
srrrrrrrrrrs
s.rrrrrrrr.s
s..rr..rr..s
...dd..dd...
..ddd..ddd..
"""),
"nova": ("Nova — an astronaut", """
....wwww....
..wwwwwwww..
.wwccccccww.
.wccbbbbccw.
.wwccccccww.
..wwwwwwww..
.rwwwwwwwwr.
rrwwyyyywwrr
rrwwwwwwwwrr
..ww....ww..
..ww....ww..
.sss....sss.
"""),
"shade": ("Shade — a shadow ninja", """
....dddd....
..dddddddd..
.dddddddddd.
.ddwwddwwdd.
.ddkwddkwdd.
.dddddddddd.
..ddrrrrdd..
..dddddddd..
.ddddddddddr
.dddddddddd.
..dd....dd..
.ddd....ddd.
"""),
"dot": ("Dot — a bouncing slime", """
....gggg....
..gglllggg..
.ggllllgggg.
.gglkggkggg.
ggggggggggGG
ggggggggggGG
ggggggggggGG
.gGGGGGGGGG.
..GGGGGGGG..
"""),
"rex": ("Rex — a tiny dinosaur", """
......gggg..
.....ggkggg.
.....gggggg.
.....ggggg..
.gg..gggg...
.ggggggggg..
..ggggggggg.
..gggggggg..
...ggggggg..
...gg...gg..
...gg...gg..
..ggg...ggg.
"""),
"petal": ("Petal — a walking flower", """
....mmmm....
..mmyyyymm..
.mmyyyyyymm.
.mmyykkyymm.
..mmyyyymm..
....mmmm....
.....gg.....
...g.gg.g...
....gggg....
.....gg.....
....gggg....
...gg..gg...
"""),
"gizmo": ("Gizmo — a TV-headed robot", """
..k......k..
...k....k...
.ssssssssss.
.sccccccccs.
.sckkcckkcs.
.sccccccccs.
.ssssssssss.
..ssrrrrss..
.sssssssssss
.ss.ssss.ss.
....ss.ss...
...sss.sss..
"""),
"hopper": ("Hopper — a frog", """
..gg....gg..
.gkgg..ggkg.
.gggggggggg.
.gggggggggg.
gggGGGGGGggg
ggggggggggg.
.gggggggggg.
gg.gggggg.gg
g..gg..gg..g
"""),
"pixie": ("Pixie — a night owl", """
....nnnn....
..nnnnnnnn..
.nnyknnyknn.
.nnyynnyynn.
.nnnnoonnnn.
.nnttttttnn.
.nnttttttnn.
.nntttttt.n.
..nnnnnnnn..
...oo..oo...
"""),
"finn": ("Finn — a fish", """
.........b..
..bbbbb.bb..
.bbbbbbbbb..
bbkbbbbbbbb.
bbbbbbbbbbb.
.bbbbbbbbb..
..bbbbb.bb..
.........b..
"""),
"bolt": ("Bolt — a lightning spark", """
.....yyyy...
....yyyy....
...yyyy.....
..yyyyyyyy..
.....yyyy...
....yyyy....
...yyyy.....
..yyy.......
..yy........
"""),
}
# ---- items and hardware ------------------------------------------------------------------------------------------------------------
ITEMS = {
"star": ("Star", """
.......yy.......
.......yy.......
......yyyy......
......yyyy......
yyyyyyyyyyyyyyyy
.yyyyyyyyyyyyyy.
..yyyyyyyyyyyy..
...yyyyyyyyyy...
...yyyyyyyyyy...
..yyyyy..yyyyy..
..yyy......yyy..
..y..........y..
"""),
"heart": ("Heart", """
..rrr....rrr..
.rrrrr..rrrrr.
rrrrrrrrrrrrrr
rrrrrrrrrrrrrr
rrrrrrrrrrrrrr
.rrrrrrrrrrrr.
..rrrrrrrrrr..
...rrrrrrrr...
....rrrrrr....
.....rrrr.....
......rr......
"""),
"gem": ("Gem", """
...cccccccc...
..cccccccccc..
.ccbbbbbbbbcc.
ccbbbbbbbbbbcc
.ccbbbbbbbbcc.
..ccbbbbbbcc..
...ccbbbbcc...
....ccbbcc....
.....cccc.....
......cc......
"""),
"question-crate": ("? crate", """
yyyyyyyyyyyyyyyy
yYYYYYYYYYYYYYYy
yYYYYYooooYYYYYy
yYYYYooYYooYYYYy
yYYYYYYYYooYYYYy
yYYYYYYYooYYYYYy
yYYYYYYooYYYYYYy
yYYYYYYooYYYYYYy
yYYYYYYYYYYYYYYy
yYYYYYYooYYYYYYy
yYYYYYYooYYYYYYy
yYYYYYYYYYYYYYYy
yYYYYYYYYYYYYYYy
yyyyyyyyyyyyyyyy
"""),
"cartridge": ("Game cartridge", """
..ssssssssssss..
..sSSSSSSSSSSs..
..sSeeeeeeeeSs..
..sSerrrrrreSs..
..sSerrrrrreSs..
..sSeeeeeeeeSs..
..sSSSSSSSSSSs..
..sSSSSSSSSSSs..
..sSSSSSSSSSSs..
..sSSSSSSSSSSs..
..ssssssssssss..
...ssssssssss...
...sddddddddds..
"""),
"controller": ("Controller", """
ssssssssssssssssssss
sddddddddddddddddddds
sdd.k.ddddddddd.r.dds
sdkkkkkddsssddd.r.dds
sdd.k.dddsssdddr.rdds
sddddddddddddddddddds
ssssssssssssssssssss
"""),
"console": ("Console", """
.sssssssssssssssssssssssssssss.
sSSSSSSSSSSSSSSSSSSSSSSSSSSSSSs
sSssssssssssssssssssssssssssSSs
sSsddddddddddddddddddddddddsSSs
sSssssssssssssssssssssssssssSSs
sSSSSSSSSSSSSSSSSSSSSSSSSSSSSSs
sSSrSSSSkSSSSSSSSSSSSSSSSSSSSSs
sSSSSSSSSSSSSSSSSSSSSSSSSSSSSSs
.sssssssssssssssssssssssssssss.
"""),
"cloud": ("Cloud", """
.....eeee.......
...eeeeeeee.....
..eeeeeeeeeee...
.eeeeeeeeeeeeee.
eeeeeeeeeeeeeeee
eeeeeeeeeeeeeeee
.eeeeeeeeeeeeee.
"""),
}
ICONS = {   # the UI glyphs the page uses (same file names as the private build, original drawings)
"icon-a": ("A button", """
..rrrrrrrr..
.rrrrrrrrrr.
rrrrrwwrrrrr
rrrrwwwwrrrr
rrrwwrrwwrrr
rrrwwwwwwrrr
rrrwwrrwwrrr
rrrwwrrwwrrr
.rrrrrrrrrr.
..rrrrrrrr..
"""),
"icon-b": ("B button", """
..rrrrrrrr..
.rrrrrrrrrr.
rrrwwwwwrrrr
rrrwwrrwwrrr
rrrwwwwwrrrr
rrrwwrrwwrrr
rrrwwrrwwrrr
rrrwwwwwrrrr
.rrrrrrrrrr.
..rrrrrrrr..
"""),
"icon-cartridge": ("Cartridge icon", """
.ssssssssss.
.sSSSSSSSSs.
.sSeeeeeeSs.
.sSerrrreSs.
.sSeeeeeeSs.
.sSSSSSSSSs.
.sSSSSSSSSs.
.ssssssssss.
..ssssssss..
..dddddddd..
"""),
"icon-disk": ("Disk icon", """
dddddddddddd
dssssssssssd
dsddddddddsd
dsddddddddsd
dssssssssssd
dddddddddddd
ddddeeeeeedd
ddddekeeeedd
ddddeeeeeedd
dddddddddddd
"""),
"icon-dpad": ("D-pad icon", """
....dddd....
....dkkd....
....dkkd....
ddddkkkkdddd
dkkkkkkkkkkd
dkkkkkkkkkkd
ddddkkkkdddd
....dkkd....
....dkkd....
....dddd....
"""),
"icon-start": ("Start icon", """
.ssssssssss.
sddddddddddd
sdddddddddds
.ssssssssss.
"""),
"icon-select": ("Select icon", """
.ssssssssss.
sddddddddddd
sdddddddddds
.ssssssssss.
"""),
"icon-warning": ("Warning icon", """
.....yy.....
....yyyy....
....ykky....
...yykkyy...
...yykkyy...
..yyykkyyy..
..yyykkyyy..
.yyyyyyyyyy.
.yyyykkyyyy.
yyyyyyyyyyyy
"""),
"icon-1p": ("1 player icon", """
....ss......
...sss......
....ss......
....ss......
....ss......
..ssssss....
"""),
"icon-2p-sim": ("2 players icon", """
.ssss...ssss
s...s..s...s
....s......s
..ss.....ss.
.s......s...
sssss...ssss
"""),
"icon-1p-2p": ("1 or 2 players icon", """
..s....ssss.
.ss...s...s.
..s.......s.
..s.....ss..
..s....s....
.sss...ssss.
"""),
"icon-resume": ("Resume icon", """
ss..........
ssss........
ssssss......
ssssssss....
ssssss......
ssss........
ss..........
"""),
"game-card": ("Game card", """
ssssssssssssssss
sddddddddddddddds
sdeeeeeeeeeeeedds
sdeeeeeeeeeeeedds
sdeeeeeeeeeeeedds
sdeeeeeeeeeeeedds
sddddddddddddddds
sdssssssssssssdds
sddddddddddddddds
ssssssssssssssss
"""),
"cmn-circle": ("Circle", """
...ssss...
.ssssssss.
ssssssssss
ssssssssss
ssssssssss
ssssssssss
.ssssssss.
...ssss...
"""),
"tile-ground": ("Ground tile", """
TTTTTTTTTTTTTTTTTTTTTT
TnnnnnnnnnTnnnnnnnnnnT
TnTTTTTTTnTnTTTTTTTTnT
TnTnnnnnTnTnTnnnnnnTnT
TnTnTTTnTnTnTnTTTTnTnT
TnTnnnnnTnTnTnnnnnnTnT
TnTTTTTTTnTnTTTTTTTTnT
TnnnnnnnnnTnnnnnnnnnnT
TTTTTTTTTTTTTTTTTTTTTT
TnnnnnnnnnnnnnTnnnnnnT
TnTTTTTTTTTTTnTnTTTTnT
TnTnnnnnnnnnTnTnTnnTnT
TnTnTTTTTTTnTnTnTnnTnT
TnTnnnnnnnnnTnTnTnnTnT
TnTTTTTTTTTTTnTnTTTTnT
TnnnnnnnnnnnnnTnnnnnnT
TTTTTTTTTTTTTTTTTTTTTT
TnnnnnnTnnnnnnnnnnnnnT
TnTTTTnTnTTTTTTTTTTTnT
TnTnnTnTnTnnnnnnnnnTnT
TnTTTTnTnTTTTTTTTTTTnT
TTTTTTTTTTTTTTTTTTTTTT
"""),
}

def draw(map_: str) -> Image.Image:
    rows = [r for r in map_.strip("\n").splitlines() if r]; w = max(len(r) for r in rows); h = len(rows)
    im = Image.new("RGBA", (w, h), (0, 0, 0, 0)); px = im.load()
    for y, r in enumerate(rows):
        for x, ch in enumerate(r.ljust(w, ".")):
            c = PAL.get(ch)
            if c: px[x, y] = c + (255,)
    return im

def console_face(w=320, h=120) -> Image.Image:
    """A generic gray top-loading console drawn from rectangles (no logos)."""
    im = Image.new("RGBA", (w, h), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.rectangle([4, 20, w - 5, h - 8], fill=(201, 201, 206)); d.rectangle([4, 20, w - 5, 44], fill=(222, 222, 226)); d.rectangle([4, h - 30, w - 5, h - 8], fill=(120, 120, 128))
    d.rectangle([30, 48, w - 110, 64], fill=(60, 60, 70)); d.rectangle([w - 90, 50, w - 60, 62], fill=(228, 0, 15)); d.rectangle([w - 50, 50, w - 20, 62], fill=(60, 60, 70))
    d.rectangle([20, h - 26, 60, h - 12], fill=(40, 40, 48)); d.rectangle([70, h - 26, 110, h - 12], fill=(40, 40, 48))
    return im

def app_icon(out_dir: str):
    """Multi-size .ico from the Byte sprite on a dark rounded tile (for the installer, shortcuts and the window)."""
    base = draw(CHARS["byte"][1]); sizes = [16, 24, 32, 48, 64, 128, 256]; frames = []
    for s in sizes:
        tile = Image.new("RGBA", (s, s), (0, 0, 0, 0)); d = ImageDraw.Draw(tile); r = max(2, s // 6); d.rounded_rectangle([0, 0, s - 1, s - 1], radius=r, fill=(21, 22, 26, 255), outline=(228, 0, 15, 255), width=max(1, s // 32))
        sc = max(1, int(s * 0.78 // max(base.size))); spr = base.resize((base.width * sc, base.height * sc), Image.NEAREST); tile.alpha_composite(spr, ((s - spr.width) // 2, (s - spr.height) // 2)); frames.append(tile)
    frames[-1].save(os.path.join(out_dir, "app.ico"), sizes=[(s, s) for s in sizes], append_images=frames[:-1]); frames[-1].save(os.path.join(out_dir, "app-256.png"))

def main(out: str):
    bd = os.path.join(out, "brand", "public"); os.makedirs(bd, exist_ok=True); index = []; prov = []
    for group, label in ((CHARS, "character"), (ITEMS, "item"), (ICONS, "icon")):
        for stem, (name, m) in group.items():
            im = draw(m); im.save(os.path.join(bd, stem + ".png")); index.append({"file": stem + ".png", "label": name, "w": im.width, "h": im.height, "kind": label})
            prov.append({"slug": stem, "name": name, "kind": label, "origin": "Original pixel art drawn for Classic Cart Studio (this repository, tools/publish/make_public_sprites.py)", "license": "MIT (project license)"})
    cf = console_face(); cf.save(os.path.join(bd, "nes-console-face.png")); index.append({"file": "nes-console-face.png", "label": "Generic console drawing", "w": cf.width, "h": cf.height, "kind": "hardware"})
    cf.crop((0, 60, cf.width, cf.height)).save(os.path.join(bd, "nes-console-face-lower.png"))
    app_icon(bd)
    json.dump({"files": index, "note": "All files are original drawings for this project; none is extracted from console firmware or any Nintendo product."}, open(os.path.join(bd, "index.json"), "w", encoding="utf-8"), indent=1)
    json.dump(prov, open(os.path.join(bd, "provenance.json"), "w", encoding="utf-8"), indent=1)
    print("wrote", len(index), "sprites to", bd)

if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "build", "site"))
