"""The color quantizer Hakchi2 CE 3.9.3 uses for the Original Menu's covers (NesMenuElementBase.Quantize: nQuant.WuQuantizer, nQuant
1.0.3), in Python, so Classic Cart Studio can make the same kind of 8-bit palette PNG. Copyright (C) 2026 Rance9, GPL-3.0-or-later.

Adapted from nQuant 1.0.3 (WuQuantizerBase and WuQuantizer, Copyright Matt Wrock, Apache License 2.0; the license text is in
THIRD_PARTY_NOTICES.md), itself adapted from Xiaolin Wu's color quantizer. Changes: written in Python; reads RGBA bytes instead of a
GDI+ bitmap and returns the palette and pixel indexes instead of a bitmap; the lookup table nQuant fills but never reads is not built.
Everything that decides the result is kept as nQuant computes it, so the same picture gives the same palette and the same indexes:
the 33 x 33 x 33 x 33 histogram of alpha, red, green and blue (each value >> 3, plus 1), the alpha fader (a partly transparent pixel's
alpha plus its remainder by 70), the moments summed in nQuant's own order with its single-precision (32-bit) arithmetic, the cube
splitting, the cut each cube takes, the nearest-color search (first nearest), each color's average (integer division) and the
transparent color nQuant adds after the others. Pixels at or below the alpha threshold (10) take that transparent color.

  quantize(rgba, width, height) -> (palette, indexes)
    rgba     width * height * 4 bytes, R G B A for each pixel, row by row
    palette  the colors [(r, g, b, a)]: nQuant's colors, then its transparent color (0, 0, 0, 0) last (at most 256 in all)
    indexes  width * height bytes, each pixel's color"""
from __future__ import annotations
import struct
from array import array
from operator import add

SIDE = 33; TOP = 32; PLANE = SIDE * SIDE * SIDE   # one alpha plane: red, green, blue
ALPHA_THRESHOLD = 10; ALPHA_FADER = 70; MAX_COLORS = 256
_F = struct.Struct("<f")
def f32(x: float) -> float:
    """x as a single-precision float (round to nearest even): nQuant's float arithmetic. A sum, product or quotient of two such floats
    worked out in double precision and rounded once gives exactly the single-precision result"""
    return _F.unpack(_F.pack(x))[0]


def _histogram(rgba: bytes, n: int):
    """BuildHistogram: the five integer moments and the float moment as alpha planes ({alpha index: flat red*1089+green*33+blue list}),
    each pixel's (alpha, red, green, blue) after the fader, as nQuant stores it"""
    W, A, R, G, B = ({} for _ in range(5)); M = {}; pixels = []
    def plane(d, a, zero):
        p = d.get(a)
        if p is None: p = d[a] = [zero] * PLANE
        return p
    for o in range(0, n * 4, 4):
        r, g, b, a = rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]
        if a > ALPHA_THRESHOLD:
            if a < 255:
                a = a + a % ALPHA_FADER
                if a > 255: a = 255
            ai = (a >> 3) + 1; c = ((r >> 3) + 1) * 1089 + ((g >> 3) + 1) * 33 + (b >> 3) + 1
            plane(W, ai, 0)[c] += 1; plane(A, ai, 0)[c] += a; plane(R, ai, 0)[c] += r; plane(G, ai, 0)[c] += g; plane(B, ai, 0)[c] += b
            m = plane(M, ai, 0.0); m[c] = f32(m[c] + (a * a + r * r + g * g + b * b))
        pixels.append((a, r, g, b))
    return W, A, R, G, B, M, pixels


def _sum3(p: list, rnd) -> list:
    """one alpha plane summed along blue, then green, then red (CalculateMoments' inner three loops, in their order)"""
    for c in range(1, SIDE):   # blue (the running sum along each row)
        p[c::SIDE] = rnd(map(add, p[c - 1::SIDE], p[c::SIDE]))
    for base in range(0, PLANE, 1089):   # green (CalculateMoments' array7..array12, one row of 33 at a time)
        for c in range(1, SIDE):
            o = base + c * SIDE; p[o:o + SIDE] = rnd(map(add, p[o - SIDE:o], p[o:o + SIDE]))
    for c in range(1, SIDE):   # red (CalculateMoments' array..array6)
        o = c * 1089; p[o:o + 1089] = rnd(map(add, p[o - 1089:o], p[o:o + 1089]))
    return p


def _moments(planes: dict, zero, rnd) -> list:
    """CalculateMoments for one moment: the 33 alpha planes, each the running sum of the planes up to it"""
    out = [None] * SIDE; prev = [zero] * PLANE; out[0] = prev
    for a in range(1, SIDE):
        p = planes.get(a)
        if p is not None:
            p = _sum3(p, rnd); prev = rnd(map(add, prev, p))
        out[a] = prev
    return out


def _vol(m: list, a0, a1, r0, r1, g0, g1, b0, b1):
    """Volume (integer moments): the cube's sum, in nQuant's order"""
    A1, A0 = m[a1], m[a0]; r1_, r0_ = r1 * 1089, r0 * 1089; g1_, g0_ = g1 * 33, g0 * 33
    return (A1[r1_ + g1_ + b1] - A1[r1_ + g0_ + b1] - A1[r0_ + g1_ + b1] + A1[r0_ + g0_ + b1] - A0[r1_ + g1_ + b1] + A0[r1_ + g0_ + b1]
            + A0[r0_ + g1_ + b1] - A0[r0_ + g0_ + b1]
            - (A1[r1_ + g1_ + b0] - A0[r1_ + g1_ + b0] - A1[r1_ + g0_ + b0] + A0[r1_ + g0_ + b0] - A1[r0_ + g1_ + b0] + A0[r0_ + g1_ + b0]
               + A1[r0_ + g0_ + b0] - A0[r0_ + g0_ + b0]))


def _vol_f(m: list, a0, a1, r0, r1, g0, g1, b0, b1) -> float:
    """VolumeFloat: the same terms in the same order, each step a single-precision operation"""
    A1, A0 = m[a1], m[a0]; r1_, r0_ = r1 * 1089, r0 * 1089; g1_, g0_ = g1 * 33, g0 * 33
    x = f32(A1[r1_ + g1_ + b1] - A1[r1_ + g0_ + b1]); x = f32(x - A1[r0_ + g1_ + b1]); x = f32(x + A1[r0_ + g0_ + b1]); x = f32(x - A0[r1_ + g1_ + b1])
    x = f32(x + A0[r1_ + g0_ + b1]); x = f32(x + A0[r0_ + g1_ + b1]); x = f32(x - A0[r0_ + g0_ + b1])
    y = f32(A1[r1_ + g1_ + b0] - A0[r1_ + g1_ + b0]); y = f32(y - A1[r1_ + g0_ + b0]); y = f32(y + A0[r1_ + g0_ + b0]); y = f32(y - A1[r0_ + g1_ + b0])
    y = f32(y + A0[r0_ + g1_ + b0]); y = f32(y + A1[r0_ + g0_ + b0]); y = f32(y - A0[r0_ + g0_ + b0])
    return f32(x - y)


def _top(m: list, c: list, d: int, p: int):
    """Top: the moment of the cube's part up to position p along direction d (3 alpha, 2 red, 1 green, 0 blue)"""
    a0, a1, r0, r1, g0, g1, b0, b1 = c
    if d == 3:
        P = m[p]; return (P[r1 * 1089 + g1 * 33 + b1] - P[r1 * 1089 + g0 * 33 + b1] - P[r0 * 1089 + g1 * 33 + b1] + P[r0 * 1089 + g0 * 33 + b1]
                          - (P[r1 * 1089 + g1 * 33 + b0] - P[r1 * 1089 + g0 * 33 + b0] - P[r0 * 1089 + g1 * 33 + b0] + P[r0 * 1089 + g0 * 33 + b0]))
    A1, A0 = m[a1], m[a0]
    if d == 2:
        q = p * 1089; return (A1[q + g1 * 33 + b1] - A1[q + g0 * 33 + b1] - A0[q + g1 * 33 + b1] + A0[q + g0 * 33 + b1]
                              - (A1[q + g1 * 33 + b0] - A1[q + g0 * 33 + b0] - A0[q + g1 * 33 + b0] + A0[q + g0 * 33 + b0]))
    if d == 1:
        q = p * 33; return (A1[r1 * 1089 + q + b1] - A1[r0 * 1089 + q + b1] - A0[r1 * 1089 + q + b1] + A0[r0 * 1089 + q + b1]
                            - (A1[r1 * 1089 + q + b0] - A1[r0 * 1089 + q + b0] - A0[r1 * 1089 + q + b0] + A0[r0 * 1089 + q + b0]))
    return (A1[r1 * 1089 + g1 * 33 + p] - A1[r1 * 1089 + g0 * 33 + p] - A1[r0 * 1089 + g1 * 33 + p] + A1[r0 * 1089 + g0 * 33 + p]
            - (A0[r1 * 1089 + g1 * 33 + p] - A0[r1 * 1089 + g0 * 33 + p] - A0[r0 * 1089 + g1 * 33 + p] + A0[r0 * 1089 + g0 * 33 + p]))


def _bottom(m: list, c: list, d: int):
    """Bottom: the moment of the cube's part below its own minimum along direction d"""
    a0, a1, r0, r1, g0, g1, b0, b1 = c
    A1, A0 = m[a1], m[a0]
    if d == 3:
        return (-A0[r1 * 1089 + g1 * 33 + b1] + A0[r1 * 1089 + g0 * 33 + b1] + A0[r0 * 1089 + g1 * 33 + b1] - A0[r0 * 1089 + g0 * 33 + b1]
                - (-A0[r1 * 1089 + g1 * 33 + b0] + A0[r1 * 1089 + g0 * 33 + b0] + A0[r0 * 1089 + g1 * 33 + b0] - A0[r0 * 1089 + g0 * 33 + b0]))
    if d == 2:
        return (-A1[r0 * 1089 + g1 * 33 + b1] + A1[r0 * 1089 + g0 * 33 + b1] + A0[r0 * 1089 + g1 * 33 + b1] - A0[r0 * 1089 + g0 * 33 + b1]
                - (-A1[r0 * 1089 + g1 * 33 + b0] + A1[r0 * 1089 + g0 * 33 + b0] + A0[r0 * 1089 + g1 * 33 + b0] - A0[r0 * 1089 + g0 * 33 + b0]))
    if d == 1:
        return (-A1[r1 * 1089 + g0 * 33 + b1] + A1[r0 * 1089 + g0 * 33 + b1] + A0[r1 * 1089 + g0 * 33 + b1] - A0[r0 * 1089 + g0 * 33 + b1]
                - (-A1[r1 * 1089 + g0 * 33 + b0] + A1[r0 * 1089 + g0 * 33 + b0] + A0[r1 * 1089 + g0 * 33 + b0] - A0[r0 * 1089 + g0 * 33 + b0]))
    return (-A1[r1 * 1089 + g1 * 33 + b0] + A1[r1 * 1089 + g0 * 33 + b0] + A1[r0 * 1089 + g1 * 33 + b0] - A1[r0 * 1089 + g0 * 33 + b0]
            - (-A0[r1 * 1089 + g1 * 33 + b0] + A0[r1 * 1089 + g0 * 33 + b0] + A0[r0 * 1089 + g1 * 33 + b0] - A0[r0 * 1089 + g0 * 33 + b0]))


def _maximize(mo, c, d, first, last, whole):
    """Maximize: the best cut of cube c along direction d -> (position or None, value as a single-precision float)"""
    WA, WR, WG, WB, WW = whole
    ba, br, bg, bb, bw = (_bottom(m, c, d) for m in mo)
    best = 0.0; pos = None
    for p in range(first, last):
        ha = ba + _top(mo[0], c, d, p); hr = br + _top(mo[1], c, d, p); hg = bg + _top(mo[2], c, d, p); hb = bb + _top(mo[3], c, d, p)
        hw = bw + _top(mo[4], c, d, p)
        if hw != 0:
            t = (ha * ha + hr * hr + hg * hg + hb * hb) // hw
            ha, hr, hg, hb, hw = WA - ha, WR - hr, WG - hg, WB - hb, WW - hw
            if hw != 0:
                t += (ha * ha + hr * hr + hg * hg + hb * hb) // hw
                ft = f32(t)
                if ft > best: best = ft; pos = p
    return pos, best


def _size(c) -> int: return (c[1] - c[0]) * (c[3] - c[2]) * (c[5] - c[4]) * (c[7] - c[6])


def _cut(mo, first: list, second: list) -> bool:
    """Cut: split cube first at its best cut; second becomes the other part"""
    whole = tuple(_vol(m, *first) for m in mo)
    ca = _maximize(mo, first, 3, first[0] + 1, first[1], whole); cr = _maximize(mo, first, 2, first[2] + 1, first[3], whole)
    cg = _maximize(mo, first, 1, first[4] + 1, first[5], whole); cb = _maximize(mo, first, 0, first[6] + 1, first[7], whole)
    A, R, G, B = ca[1], cr[1], cg[1], cb[1]
    if not (A >= R) or not (A >= G) or not (A >= B):
        d = 2 if (R >= A and R >= G and R >= B) else 1 if (G >= A and G >= R and G >= B) else 0
    else:
        d = 3
        if ca[0] is None: return False
    second[1], second[3], second[5], second[7] = first[1], first[3], first[5], first[7]
    k = {3: 0, 2: 2, 1: 4, 0: 6}[d]; p = (ca, cr, cg, cb)[3 - d][0]
    second[k] = p; first[k + 1] = p
    for j in (0, 2, 4, 6):
        if j != k: second[j] = first[j]
    return True


def _variance(mo, m_f, c) -> float:
    """CalculateVariance, in single precision"""
    a, r, g, b, w = (f32(_vol(m, *c)) for m in mo)
    v = _vol_f(m_f, *c)
    s = f32(f32(f32(f32(a * a) + f32(r * r)) + f32(g * g)) + f32(b * b))
    return f32(v - f32(s / w)) if w != 0 else 0.0   # nQuant: NaN (0/0) is taken as 0


def quantize(rgba: bytes, width: int, height: int) -> tuple[list, bytes]:
    n = width * height
    if len(rgba) != n * 4 or n == 0: raise ValueError("the picture's RGBA bytes do not match its size")
    W, A, R, G, B, Mf, pixels = _histogram(rgba, n)
    ints = lambda it: list(it); floats = lambda it: array("f", it).tolist()
    mo = [_moments(A, 0, ints), _moments(R, 0, ints), _moments(G, 0, ints), _moments(B, 0, ints), _moments(W, 0, ints)]
    m_f = _moments(Mf, 0.0, floats)
    # SplitData
    count = MAX_COLORS - 1; nxt = 0; var = [0.0] * MAX_COLORS
    cubes = [[0, 0, 0, 0, 0, 0, 0, 0] for _ in range(MAX_COLORS)]; cubes[0] = [0, TOP, 0, TOP, 0, TOP, 0, TOP]
    i = 1
    while i < count:
        if _cut(mo, cubes[nxt], cubes[i]):
            var[nxt] = _variance(mo, m_f, cubes[nxt]) if _size(cubes[nxt]) > 1 else 0.0
            var[i] = _variance(mo, m_f, cubes[i]) if _size(cubes[i]) > 1 else 0.0
        else:
            var[nxt] = 0.0; i -= 1
        nxt = 0; top = var[0]
        for j in range(1, i + 1):
            if not (var[j] <= top): top = var[j]; nxt = j
        if not (top > 0.0): count = i + 1; break
        i += 1
    cubes = cubes[:count]
    # BuildLookups (the colors): each cube with pixels gives its average, in cube order
    looks = []
    for c in cubes:
        w = _vol(mo[4], *c)
        if w > 0: looks.append((_vol(mo[0], *c) // w, _vol(mo[1], *c) // w, _vol(mo[2], *c) // w, _vol(mo[3], *c) // w))
    # GetQuantizedPalette: each opaque-enough pixel's nearest color (the first nearest), then each color's average of its pixels
    la = [x[0] for x in looks]; lr = [x[1] for x in looks]; lg = [x[2] for x in looks]; lb = [x[3] for x in looks]
    sq = [[(v - t) * (v - t) for t in range(256)] for v in range(256)]   # sq[v][t] = (v - t)^2
    cols = {}; idx = bytearray(n); sums = [[0, 0, 0, 0, 0] for _ in range(count + 1)]; transparent = count
    for k, (a, r, g, b) in enumerate(pixels):
        if a <= ALPHA_THRESHOLD: idx[k] = transparent; continue
        key = (a, r, g, b); v = cols.get(key)
        if v is None:
            sa, sr, sg, sb = sq[a], sq[r], sq[g], sq[b]
            d = list(map(add, map(add, map(sa.__getitem__, la), map(sr.__getitem__, lr)), map(add, map(sg.__getitem__, lg), map(sb.__getitem__, lb))))
            v = cols[key] = d.index(min(d)) if d else 0
        s = sums[v]; s[0] += a; s[1] += r; s[2] += g; s[3] += b; s[4] += 1; idx[k] = v
    palette = []
    for l in range(count):
        s = sums[l]
        palette.append((s[1] // s[4], s[2] // s[4], s[3] // s[4], s[0] // s[4]) if s[4] > 0 else (0, 0, 0, 0))
    palette.append((0, 0, 0, 0))
    return palette, bytes(idx)
