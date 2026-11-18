r"""The Classic Cart Studio cast at twice the detail (owner, 2026-11-12: "higher resolution pixel art"): each character and item drawn here
from simple shapes on a 32 x 40 grid (items their own sizes), then outlined and lit the same way for all of them, so every picture is crisp
pixel art at two pixels per old pixel. Same characters, colors and walking frames as before; the website shows them at the same size
(index.json keeps the size in display units and says "density": 2). Used by make_ccs_cast.py. GNU GPL v3 or later."""
from __future__ import annotations
import math
from PIL import Image

INK = (27, 27, 38); LINE = (44, 40, 58)
# material: (highlight, base, shade); single letters are what the drawings below paint with
TONES = {
    "w": ((255, 255, 255), (244, 241, 234), (204, 199, 214)), "g": ((222, 219, 230), (185, 182, 195), (134, 131, 148)),
    "G": ((134, 131, 148), (108, 106, 120), (78, 76, 92)), "r": ((255, 104, 104), (228, 32, 43), (164, 18, 28)),
    "R": ((196, 40, 50), (154, 16, 24), (104, 10, 18)), "b": ((108, 158, 255), (47, 111, 224), (29, 63, 138)),
    "c": ((180, 240, 255), (88, 208, 240), (42, 143, 176)), "y": ((255, 240, 154), (246, 199, 68), (192, 138, 28)),
    "n": ((184, 120, 63), (138, 85, 48), (74, 44, 20)), "N": ((110, 70, 38), (74, 44, 20), (52, 30, 14)),
    "s": ((255, 210, 166), (242, 176, 122), (201, 128, 77)), "p": ((181, 133, 245), (138, 74, 216), (90, 42, 150)),
    "P": ((120, 66, 190), (90, 42, 150), (62, 26, 106)), "e": ((142, 232, 154), (70, 194, 90), (35, 122, 53)),
    "E": ((60, 150, 76), (35, 122, 53), (22, 84, 36)), "u": ((255, 248, 214), (243, 230, 176), (214, 190, 128)),
    "v": ((74, 95, 138), (42, 53, 80), (28, 34, 54)), "m": ((255, 140, 204), (224, 79, 160), (160, 38, 112)),
}
FLAT = {"W": (255, 255, 255), "K": INK, "i": (200, 248, 255), "V": (74, 95, 138), "j": (255, 150, 200), "f": (255, 246, 190), "Y": (192, 138, 28),
        "h": (204, 199, 214), "z": (78, 76, 92), "S": (201, 128, 77)}

class Canvas:
    def __init__(self, w=32, h=40): self.w, self.h = w, h; self.p = [["."] * w for _ in range(h)]
    def px(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h: self.p[y][x] = c
    def rect(self, x0, y0, x1, y1, c):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1): self.px(x, y, c)
    def rrect(self, x0, y0, x1, y1, r, c):   # a rectangle with its corners cut round (radius r)
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                dx = max(x0 + r - x, 0, x - (x1 - r)); dy = max(y0 + r - y, 0, y - (y1 - r))
                if dx * dx + dy * dy <= r * r + 0.5: self.px(x, y, c)
    def ell(self, cx, cy, rx, ry, c):
        for y in range(self.h):
            for x in range(self.w):
                if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1.0: self.px(x, y, c)
    def poly(self, pts, c):   # filled polygon, pixel centers
        for y in range(self.h):
            for x in range(self.w):
                X, Y = x + 0.5, y + 0.5; ins = False; j = len(pts) - 1
                for i in range(len(pts)):
                    (xi, yi), (xj, yj) = pts[i], pts[j]
                    if (yi > Y) != (yj > Y) and X < (xj - xi) * (Y - yi) / (yj - yi) + xi: ins = not ins
                    j = i
                if ins: self.px(x, y, c)
    def image(self, outline=True, shade=True):
        P = self.p; W, H = self.w, self.h; g = lambda x, y: P[y][x] if 0 <= x < W and 0 <= y < H else "."
        out = [row[:] for row in P]
        if outline:   # a one-pixel outline around everything, on the transparent side
            for y in range(H):
                for x in range(W):
                    if P[y][x] == "." and any(g(x + dx, y + dy) not in ".k" for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))): out[y][x] = "k"
        im = Image.new("RGBA", (W, H), (0, 0, 0, 0)); o = lambda x, y: out[y][x] if 0 <= x < W and 0 <= y < H else "."
        for y in range(H):
            for x in range(W):
                c = out[y][x]
                if c == ".": continue
                if c == "k": col = INK
                elif c in TONES and shade:   # lit from the upper left: a light rim where the material begins, a shade where it ends
                    hi, base, lo = TONES[c]; top = o(x, y - 1) != c or o(x - 1, y) != c; bot = o(x, y + 1) != c or o(x + 1, y) != c
                    col = base if top and bot else hi if top else lo if bot else base
                elif c in TONES: col = TONES[c][1]
                else: col = FLAT[c]
                im.putpixel((x, y), col + (255,))
        return im

def legs(c: Canvas, frame: int, col: str, boot: str):
    if frame == 0:   # stride: the back leg straight, the front leg reaching forward
        c.rect(9, 30, 11, 35, col); c.rect(9, 36, 13, 37, boot)
        c.rect(19, 30, 21, 32, col); c.rect(20, 33, 22, 35, col); c.rect(21, 36, 25, 37, boot)
    else:            # passing: both legs under the body
        c.rect(11, 30, 13, 35, col); c.rect(11, 36, 15, 37, boot)
        c.rect(18, 30, 20, 35, col); c.rect(18, 36, 22, 37, boot)

def carty(c, f):
    c.rrect(5, 2, 26, 28, 3, "g"); c.rect(6, 7, 25, 7, "z")
    for x in range(7, 25, 2): c.rect(x, 3, x, 6, "G")
    c.rrect(8, 9, 23, 25, 2, "r")
    c.rect(10, 12, 13, 16, "W"); c.rect(18, 12, 21, 16, "W"); c.rect(12, 14, 13, 16, "K"); c.rect(20, 14, 21, 16, "K")
    c.px(10, 19, "K"); c.px(21, 19, "K"); c.rect(11, 20, 20, 21, "K"); c.rect(13, 21, 18, 21, "j")
    c.rect(9, 26, 22, 27, "G"); legs(c, f, "G", "z")
def padd(c, f):
    c.rrect(1, 14, 30, 28, 4, "g"); c.rect(4, 16, 27, 16, "h")
    c.rect(7, 17, 9, 25, "K"); c.rect(4, 20, 12, 22, "K"); c.px(8, 21, "z")
    c.rect(13, 23, 15, 24, "G"); c.rect(17, 23, 19, 24, "G"); c.rect(13, 18, 19, 20, "G")
    c.ell(22, 23, 2.1, 2.1, "r"); c.ell(27, 20, 2.1, 2.1, "r"); c.px(21, 22, "W"); c.px(26, 19, "W")
    legs(c, f, "G", "z")
def hero(c, f):
    c.poly([(7, 14), (13, 13), (13, 29), (4, 29)], "R")                       # the cape, behind
    c.ell(16, 8, 6.5, 6.5, "n"); c.poly([(10, 4), (12, 0), (14, 3), (17, 0), (19, 3), (22, 2), (22, 6)], "n")   # hair
    c.rrect(15, 6, 23, 14, 2, "s"); c.rect(15, 5, 21, 6, "n"); c.rect(14, 6, 15, 11, "n")
    c.rect(20, 8, 21, 10, "K"); c.px(20, 8, "W"); c.rect(23, 10, 24, 11, "s"); c.px(17, 10, "S"); c.rect(19, 12, 21, 12, "S")
    c.rrect(12, 15, 22, 28, 2, "b"); c.rect(12, 24, 22, 25, "n"); c.rect(16, 24, 17, 25, "y"); c.px(17, 18, "y"); c.px(17, 21, "y")
    c.rect(26, 9, 27, 21, "w"); c.rect(23, 22, 30, 23, "y"); c.rect(26, 24, 27, 26, "n")   # the sword
    c.rrect(21, 16, 24, 23, 1, "b"); c.rect(23, 21, 26, 24, "s")
    legs(c, f, "n", "N")
def astro(c, f):
    c.rect(4, 15, 9, 26, "g"); c.rect(5, 16, 8, 17, "G")                    # the backpack
    c.ell(15.5, 8.5, 8.5, 7.5, "w"); c.rrect(14, 4, 24, 12, 3, "v"); c.rect(16, 5, 20, 6, "V"); c.rect(21, 6, 22, 7, "c"); c.px(22, 6, "i")
    c.rrect(9, 15, 23, 28, 3, "w"); c.rect(15, 19, 17, 21, "r"); c.rect(9, 26, 23, 27, "g")
    c.rrect(20, 17, 24, 25, 1, "w"); c.rect(21, 25, 24, 27, "g")
    legs(c, f, "w", "g")
def boltbot(c, f):
    c.rect(15, 2, 16, 4, "G"); c.ell(15.5, 1.5, 1.8, 1.8, "y")
    c.rrect(8, 5, 23, 14, 2, "g"); c.rect(10, 8, 13, 10, "c"); c.rect(18, 8, 21, 10, "c"); c.px(10, 8, "i"); c.px(18, 8, "i")
    for x in range(12, 20, 2): c.px(x, 12, "z")
    c.rect(13, 15, 18, 16, "G")
    c.rrect(6, 17, 25, 28, 2, "g"); c.rrect(10, 19, 21, 24, 1, "G")
    c.rect(11, 21, 12, 22, "r"); c.rect(15, 21, 16, 22, "y"); c.rect(19, 21, 20, 22, "r")
    c.rrect(2, 18, 5, 26, 1, "G"); c.rrect(26, 18, 29, 26, 1, "G")
    legs(c, f, "G", "z")
def wizzy(c, f):
    c.poly([(9, 11), (16, 2), (19, 0), (21, 3), (24, 11)], "p"); c.rect(6, 10, 27, 11, "P")
    c.rect(16, 5, 16, 9, "y"); c.rect(14, 7, 18, 7, "y"); c.px(16, 7, "f")
    c.rrect(11, 12, 22, 17, 1, "s"); c.rect(13, 13, 14, 14, "K"); c.rect(19, 13, 20, 14, "K"); c.rect(16, 15, 17, 16, "S")
    c.poly([(6, 29), (11, 17), (22, 17), (27, 29)], "p"); c.rect(6, 27, 27, 28, "P")
    c.poly([(10, 16), (23, 16), (19, 26), (16.5, 28), (14, 26)], "w")
    c.rrect(22, 19, 26, 24, 1, "p"); c.rect(24, 23, 26, 25, "s")
    legs(c, f, "P", "P")
def dino(c, f):
    c.poly([(9, 18), (0, 12), (2, 11), (11, 16)], "e")                       # the tail
    c.ell(14, 21, 9, 7.5, "e"); c.rect(14, 10, 20, 17, "e"); c.rrect(15, 2, 29, 11, 3, "e")
    c.rect(19, 9, 29, 11, "u"); c.rect(20, 11, 27, 11, "E")
    c.rect(22, 4, 24, 6, "W"); c.rect(23, 5, 24, 6, "K"); c.px(28, 5, "E")
    c.ell(17, 22.5, 4.5, 5, "u"); c.rect(22, 17, 24, 19, "e")
    for x, y in ((9, 14), (6, 16), (12, 12)): c.px(x, y, "E")
    legs(c, f, "E", "E")
def ghosty(c, f):
    dy = -2 if f else 0
    c.rrect(7, 3 + dy, 24, 34 + dy, 8, "w"); c.rect(7, 18 + dy, 24, 34 + dy, "w")
    for x0 in (7, 12, 17, 22): c.rect(x0, 35 + dy, x0 + 2, 37 + dy, "w")
    c.ell(12.5, 15 + dy, 1.8, 2.8, "K"); c.ell(19.5, 15 + dy, 1.8, 2.8, "K"); c.px(12, 13 + dy, "W"); c.px(19, 13 + dy, "W")
    c.ell(16, 22 + dy, 2.2, 1.6, "m"); c.rect(9, 19 + dy, 10, 19 + dy, "j"); c.rect(21, 19 + dy, 22, 19 + dy, "j")
def slimo(c, f):
    squash = 1 if f else 0
    c.ell(16, 31 + squash, 13 + squash, 8 - squash, "e"); c.rect(3, 36, 28, 38, "e") if not f else c.rect(2, 36, 29, 38, "e")
    c.ell(10, 27 + squash, 2.5, 1.5, "u")
    c.ell(11.5, 31 + squash, 2.6, 3, "W"); c.ell(20.5, 31 + squash, 2.6, 3, "W")
    c.rect(12, 31 + squash, 13, 33 + squash, "K"); c.rect(21, 31 + squash, 22, 33 + squash, "K"); c.rect(15, 35, 17, 35, "E")
def batty(c, f):
    up = f == 1
    wl = [(1, 9), (9, 14), (13, 17), (11, 22), (8, 19), (5, 22), (3, 18)] if not up else [(1, 20), (9, 15), (13, 17), (11, 22), (8, 22), (5, 25), (2, 23)]
    c.poly(wl, "p"); c.poly([(31 - x, y) for x, y in wl], "p")
    c.ell(15.5, 18, 5.5, 5.5, "p"); c.poly([(11, 15), (12, 10), (15, 14)], "p"); c.poly([(20, 15), (19, 10), (16, 14)], "p")
    c.rect(12, 16, 14, 18, "W"); c.rect(17, 16, 19, 18, "W"); c.rect(13, 17, 14, 18, "K"); c.rect(18, 17, 19, 18, "K")
    c.px(14, 21, "W"); c.px(17, 21, "W")
def racer(c, f):
    c.poly([(9, 28), (12, 21), (22, 21), (26, 28)], "m"); c.poly([(12, 27), (14, 22), (20, 22), (23, 27)], "c"); c.rect(15, 23, 16, 24, "i")
    c.rrect(1, 27, 30, 32, 2, "m"); c.rect(3, 30, 28, 30, "j"); c.rect(28, 28, 29, 29, "y"); c.rect(2, 28, 3, 29, "r")
    c.rect(5, 33, 11, 34, "G"); c.rect(20, 33, 26, 34, "G")
    glow = "c" if f else "i"; c.rect(6, 36, 10, 36, glow); c.rect(21, 36, 25, 36, glow)
def knightly(c, f):
    c.rrect(9, 1, 22, 13, 5, "g"); c.rect(12, 5, 22, 8, "K"); c.rect(14, 6, 21, 7, "c"); c.px(15, 6, "i"); c.rect(10, 10, 21, 10, "h")
    c.rrect(9, 15, 22, 28, 2, "g"); c.rect(15, 17, 16, 25, "r"); c.rect(12, 19, 19, 20, "r"); c.rect(9, 26, 22, 27, "G")
    c.ell(8, 17.5, 3.5, 3, "b"); c.ell(23, 17.5, 3.5, 3, "b"); c.rrect(5, 19, 8, 26, 1, "b"); c.rrect(23, 19, 26, 26, 1, "b")
    legs(c, f, "G", "z")

CHARS = [   # id, name, role, drawing; every character has two frames (walking, or floating, flapping, squashing, hovering)
    ("carty", "Carty", "the Classic Cart Studio mascot, a walking game cartridge", carty),
    ("padd", "Padd", "a walking controller, Carty's best friend", padd),
    ("hero", "Hero", "a caped adventurer (inspired by the Kingdoms Beyond design)", hero),
    ("astro", "Astro", "a space explorer (inspired by the Galactic Frontier design)", astro),
    ("boltbot", "Bolt Bot", "a friendly robot", boltbot),
    ("wizzy", "Wizzy", "a wizard who keeps the save slots", wizzy),
    ("dino", "Dino", "a small dinosaur who loves racing games", dino),
    ("ghosty", "Ghosty", "a shy ghost from the haunted levels", ghosty),
    ("slimo", "Slimo", "a bouncy slime", slimo),
    ("batty", "Batty", "a bat from the cave levels", batty),
    ("racer", "Racer", "a neon racer (inspired by the Neon Highway design)", racer),
    ("knightly", "Knightly", "a knight guarding the collections", knightly),
]

def star(c):
    pts = [(12 + 11 * math.cos(math.radians(-90 + 36 * i)) * (1 if i % 2 == 0 else 0.45), 10.8 + 10 * math.sin(math.radians(-90 + 36 * i)) * (1 if i % 2 == 0 else 0.45)) for i in range(10)]
    c.poly(pts, "y"); c.px(9, 7, "f"); c.px(10, 6, "f")   # a plain star: no face
def gem(c):
    c.poly([(2, 5), (5, 1), (15, 1), (18, 5), (10, 14.5)], "c"); c.rect(4, 6, 16, 6, "i"); c.rect(7, 3, 9, 4, "W")
def heart(c):
    c.ell(6, 5.5, 4.6, 4.4, "r"); c.ell(14, 5.5, 4.6, 4.4, "r"); c.poly([(1.6, 6.5), (18.4, 6.5), (10, 14.8)], "r"); c.rect(4, 4, 5, 5, "W")
def coin(c):
    c.ell(8, 8, 7, 7, "y"); c.ell(8, 8, 4.5, 4.6, "Y"); c.ell(8, 8, 3.2, 3.4, "y"); c.rect(7, 5, 8, 10, "f")
def chest(c):
    c.rrect(1, 1, 30, 9, 3, "n"); c.rect(1, 10, 30, 18, "n"); c.rect(1, 9, 30, 10, "N"); c.rect(14, 7, 17, 12, "y"); c.rect(15, 10, 16, 11, "K")
    for x in (5, 26): c.rect(x, 2, x, 18, "N")
ITEMS = {"chest": ("Treasure chest", (32, 20), chest), "star": ("Star", (24, 20), star), "gem": ("Gem", (20, 16), gem), "heart": ("Heart", (20, 16), heart),
         "coin": ("Token", (16, 16), coin)}   # twice the earlier sizes

def character(draw, frame: int) -> Image.Image:
    c = Canvas(32, 40); draw(c, frame); return c.image()
def item(size, draw) -> Image.Image:
    c = Canvas(*size); draw(c); return c.image()
