r"""CCS HOME's picture and font encodings: smaller files, the same decoded pixels.
  jpeg      every JPEG route (a cover from a JPEG or PNG source, a user's picture with transparency and a crop, a generated cover, a folder
            tile, a theme thumbnail, a backdrop) decodes to exactly the pixels of the standard-table encoding at the same quality and is
            never larger; the same input gives the same bytes
  png       an RGB picture of at most 256 colors becomes an indexed PNG of exactly its colors when that is smaller; a picture of more colors, with transparency,
            an indexed one with a transparent color, a grayscale one: every RGBA pixel kept, never larger; a 16-bit PNG and a file that is
            not a PNG stay as they are
  fonts     two identical font atlases are stored once and pack.json names that one file for both; different atlases stay apart
  console   a console's covers: the Details fit, then 256/340 of each dimension (never enlarged; tiny, wide and tall pictures), a baseline
  covers    4:2:0 JPEG of quality 75 made from the source, the same bytes each time; a person's picture with its orientation, crop and
            transparency; the generated cover; every console package (deployment, development, preview) gets them, the computer's
            full-resolution HOME keeps its own, and switched off the builder makes the covers made before them
  python test_home_encodings.py      Exit 0 = PASS."""
from __future__ import annotations
import io, os, random, shutil, sys, tempfile
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from PIL import Image
from native_home import derive as DV


def main() -> int:
    checks = []
    def ok(k, v, n=""): checks.append((k, bool(v))); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:300]})" if n and not v else ""))
    t = tempfile.mkdtemp(prefix="ccs-enc-"); rnd = random.Random(7)
    try:
        def photo(w, h, mode="RGB"):
            im = Image.new(mode, (w, h))
            px = im.load()
            for y in range(h):
                for x in range(w):
                    v = (x * 7 + y * 3 + rnd.randrange(40)) % 256
                    px[x, y] = (v, (v * 3) % 256, (x + y) % 256) + ((rnd.randrange(256),) if mode == "RGBA" else ())
            return im
        def plain(im, q):
            b = io.BytesIO(); im.convert("RGB").save(b, "JPEG", quality=q, optimize=False, progressive=False, subsampling=2); return b.getvalue()
        def path(name): return os.path.join(t, name)
        # ---- JPEG routes
        src_jpg = path("src.jpg"); photo(700, 900).save(src_jpg, "JPEG", quality=95)
        src_png = path("src.png"); photo(400, 300).save(src_png)
        user = path("user.png"); photo(500, 500, "RGBA").save(user)
        routes = {"cover-from-jpeg": lambda o: DV.cover(src_jpg, o), "cover-from-png": lambda o: DV.cover(src_png, o),
                  "user-cover-transparent-cropped": lambda o: DV.user_cover(user, {"x": 0.1, "y": 0.2, "w": 0.6, "h": 0.5}, o),
                  "folder-tile": lambda o: DV.folder_tile(src_png, o), "theme-thumb": lambda o: DV.thumb(src_png, o), "backdrop": lambda o: DV.hero(src_png, "hero", o)}
        bad = []; smaller = 0
        for name, make in routes.items():
            a, b = path(name + "-1.jpg"), path(name + "-2.jpg"); make(a); make(b)
            data = open(a, "rb").read(); im = Image.open(io.BytesIO(data)); im.load()
            q = 86 if name == "folder-tile" else 88 if name == "theme-thumb" else 85
            # the same route with the standard tables: the pixels it encodes are the route's own, so the two files must decode alike
            orig = DV.save_jpeg
            DV.save_jpeg = lambda im2, out, q=85: im2.convert("RGB").save(out, "JPEG", quality=q, optimize=False, progressive=False, subsampling=2)
            try: make(path(name + "-std.jpg"))
            finally: DV.save_jpeg = orig
            std = open(path(name + "-std.jpg"), "rb").read()
            same = DV.decoded(data) == DV.decoded(std); smaller += len(data) < len(std)
            if not (same and len(data) <= len(std) and data == open(b, "rb").read() and data[:2] == b"\xff\xd8" and im.format == "JPEG"): bad.append(name)
        ok("jpeg_every_route_same_pixels_never_larger_reproducible", not bad and smaller == len(routes), (bad, smaller))
        mock = path("mock.jpg"); DV.mock("A Test Title", "Publisher", "1990", "0123456789abcdef", mock)
        ok("jpeg_generated_cover_baseline", Image.open(mock).format == "JPEG" and "progression" not in Image.open(mock).info)
        # ---- PNG
        def png(name, im, **kw): p = path(name); im.save(p, **kw); return p
        few = Image.new("RGB", (256, 240)); fp = few.load(); colors = [(rnd.randrange(256), rnd.randrange(256), rnd.randrange(256)) for _ in range(16)]   # a game screen: few colors, busy
        for y in range(240):
            for x in range(256): fp[x, y] = colors[rnd.randrange(16)]
        cases = {"rgb-few-colors": png("few.png", few), "rgb-many-colors": png("many.png", photo(120, 90)), "rgba": png("rgba.png", photo(60, 40, "RGBA")),
                 "gray": png("gray.png", few.convert("L")), "gray-alpha": png("la.png", few.convert("LA"))}
        pal = few.convert("P", palette=Image.Palette.ADAPTIVE, colors=64); cases["indexed-transparent"] = png("pt.png", pal, transparency=0)
        res = {}
        for name, p in cases.items():
            o = path(name + "-out.png"); DV.png_exact(p, o); a, b = open(p, "rb").read(), open(o, "rb").read()
            res[name] = (DV.decoded(a) == DV.decoded(b), len(b) <= len(a), Image.open(o).mode)
        ok("png_every_rgba_pixel_kept_never_larger", all(v[0] and v[1] for v in res.values()), res)
        ok("png_few_colors_become_an_exact_palette", res["rgb-few-colors"][2] == "P" and os.path.getsize(path("rgb-few-colors-out.png")) < os.path.getsize(cases["rgb-few-colors"]), res["rgb-few-colors"])
        sixteen = path("i16.png"); Image.new("I;16", (8, 8), 40000).save(sixteen); o = path("i16-out.png"); DV.png_exact(sixteen, o)
        ok("png_16_bit_stays_as_it_is", open(sixteen, "rb").read() == open(o, "rb").read())
        notpng = path("not.png"); open(notpng, "wb").write(open(src_jpg, "rb").read()); o = path("not-out.png"); DV.png_exact(notpng, o)
        ok("png_a_file_that_is_not_a_png_stays_as_it_is", open(notpng, "rb").read() == open(o, "rb").read())
        o2 = path("few-again.png"); DV.png_exact(cases["rgb-few-colors"], o2)
        ok("png_reproducible", open(o2, "rb").read() == open(path("rgb-few-colors-out.png"), "rb").read())
        # ---- font aliases in the package builder
        from native_home import package as P
        b = P.Builder.__new__(P.Builder); b.files = {}; b.font_files = {}; b.stage = path("stage"); os.makedirs(b.stage)
        f1, f2, f3 = path("a.ccsf"), path("b.ccsf"), path("c.ccsf"); open(f1, "wb").write(b"CCSF2\0" + b"x" * 100); shutil.copyfile(f1, f2); open(f3, "wb").write(b"CCSF2\0" + b"y" * 100)
        r1, r2, r3 = b.put_font("font/one.ccsf", f1), b.put_font("font/two.ccsf", f2), b.put_font("font/three.ccsf", f3)
        ok("fonts_identical_atlases_stored_once", r1 == r2 == "font/one.ccsf" and r3 == "font/three.ccsf" and sorted(b.files) == ["font/one.ccsf", "font/three.ccsf"]
           and not os.path.exists(os.path.join(b.stage, "font", "two.ccsf")))
        console_covers(ok, path, photo)
    finally: shutil.rmtree(t, ignore_errors=True)
    bad = [k for k, v in checks if not v]; print("RESULT:", "PASS" if not bad else "FAIL " + str(bad), f"({len(checks)} checks)"); return 0 if not bad else 1


def jpeg_frame(data: bytes) -> tuple[int, list]:
    """(SOF marker, [(component id, horizontal, vertical sampling)]) of a JPEG"""
    i = 2
    while i < len(data):
        m, n = data[i + 1], int.from_bytes(data[i + 2:i + 4], "big")
        if m in (0xC0, 0xC1, 0xC2):
            seg = data[i + 4:i + 2 + n]; comps = [(seg[6 + 3 * k], seg[7 + 3 * k] >> 4, seg[7 + 3 * k] & 15) for k in range(seg[5])]
            return m, comps
        i += 2 + n
    return 0, []


def console_covers(ok, path, photo):
    """a console's covers: the Details fit, then both dimensions times 256/340 (never enlarged), a baseline 4:2:0 JPEG of quality 75 with
    its own Huffman tables, made from the source; the computer's full-resolution HOME keeps its own"""
    from native_home import package as P
    def expect(w, h):   # the Details fit (never enlarged), then 256/340 of each fitted dimension
        s = min(340 / w, 466 / h); fw, fh = (max(1, round(w * s)), max(1, round(h * s))) if s < 1 else (w, h)
        return max(1, round(fw * 256 / 340)), max(1, round(fh * 256 / 340))
    sizes = {"portrait": (700, 900), "landscape": (900, 400), "the-box": (340, 466), "tiny": (100, 80), "one-pixel": (1, 1), "tall-strip": (20, 2000)}
    got = {}
    for name, (w, h) in sizes.items():
        src = path(f"cc-{name}.png"); photo(w, h).save(src); o = path(f"cc-{name}.jpg"); DV.cover(src, o, reduced=True)
        got[name] = (Image.open(o).size, expect(w, h))
    ok("console_cover_geometry_fit_then_256_of_340_never_enlarged", all(a == b and a[0] <= sizes[n][0] and a[1] <= sizes[n][1] for n, (a, b) in got.items()), got)
    ok("console_cover_portrait_at_most_256_by_351", Image.open(path("cc-the-box.jpg")).size == (256, 351))
    data = open(path("cc-portrait.jpg"), "rb").read(); im = Image.open(io.BytesIO(data)); im.load()
    sof, comps = jpeg_frame(data)
    ref = io.BytesIO(); im.convert("RGB").save(ref, "JPEG", quality=75, subsampling=2); refq = Image.open(io.BytesIO(ref.getvalue())).quantization
    ok("console_cover_baseline_420_quality_75", sof == 0xC0 and comps == [(1, 2, 2), (2, 1, 1), (3, 1, 1)] and im.quantization == refq and "progression" not in im.info,
       (hex(sof), comps))
    # made from the source: the fitted source reduced and encoded once (never the larger cover decoded and encoded again)
    src = Image.open(path("cc-portrait.png")).convert("RGB"); s = min(340 / 700, 466 / 900); fit = src.resize((round(700 * s), round(900 * s)), Image.LANCZOS)
    once = path("cc-once.jpg"); DV.save_jpeg(fit.resize(expect(700, 900), Image.LANCZOS), once, q=75)
    big = path("cc-big.jpg"); DV.cover(path("cc-portrait.png"), big); again = path("cc-again.jpg"); DV.save_jpeg(Image.open(big).resize(expect(700, 900), Image.LANCZOS), again, q=75)
    ok("console_cover_from_the_source_never_an_earlier_jpeg", data == open(once, "rb").read() and data != open(again, "rb").read())
    o2 = path("cc-portrait-2.jpg"); DV.cover(path("cc-portrait.png"), o2, reduced=True)
    ok("console_cover_reproducible", data == open(o2, "rb").read())
    # a person's own picture: its orientation, its crop, transparency on the dark ground, then the same console size
    rot = path("cc-rotated.jpg"); ex = Image.Exif(); ex[0x0112] = 6; photo(900, 600).save(rot, "JPEG", quality=95, exif=ex)   # stored landscape, shown portrait
    o = path("cc-user-rot.jpg"); DV.user_cover(rot, None, o, reduced=True)
    ok("console_cover_user_picture_orientation", Image.open(o).size == expect(600, 900), Image.open(o).size)
    tr = path("cc-transparent.png"); Image.new("RGBA", (400, 600), (200, 30, 30, 0)).save(tr)
    o = path("cc-user-tr.jpg"); DV.user_cover(tr, {"x": 0.25, "y": 0.0, "w": 0.5, "h": 1.0}, o, reduced=True); u = Image.open(o).convert("RGB")
    corner = u.getpixel((u.width // 2, u.height // 2))
    ok("console_cover_user_picture_crop_and_transparency", u.size == expect(200, 600) and all(abs(a - b) <= 6 for a, b in zip(corner, (16, 16, 22))), (u.size, corner))
    # the generated cover: drawn by the generator, then the same console size and quality
    import mock_cover
    if not (mock_cover.FONT_PX and os.path.isfile(mock_cover.FONT_PX)): mock_cover.FONT_PX = os.path.join(DV.FONTS_DIR, "Barlow-Bold.ttf")
    gen = mock_cover.cover("A Test Title", "Publisher", "1990", "0123456789abcdef")
    m = path("cc-mock.jpg"); DV.mock("A Test Title", "Publisher", "1990", "0123456789abcdef", m, reduced=True); mi = Image.open(m)
    ok("console_cover_generated_cover", mi.size == expect(*gen.size) and jpeg_frame(open(m, "rb").read())[0] == 0xC0, (mi.size, gen.size))
    # the package builder: a console's package (deployment, development, the computer's preview of the console) gets the console cover; the
    # computer's full-resolution HOME ("play") keeps its own; turning REDUCED_CONSOLE_COVERS off gives the covers made before them
    own = path("cc-own.png"); photo(700, 900).save(own)
    def built(mode):
        b = P.Builder.__new__(P.Builder); b.cfg = {"mode": mode}; b.cache = DV.Cache(path("cc-cache")); b.warnings = []; b.files = {}; b.font_files = {}
        b.stage = path("cc-stage-" + mode + str(DV.REDUCED_CONSOLE_COVERS)); os.makedirs(b.stage, exist_ok=True)
        rel, kind, _ = b.game_cover({"id": "0123456789abcdef", "title": "Own", "art_user": {"path": own, "crop": None}})
        return kind, Image.open(os.path.join(b.stage, rel)).size, open(os.path.join(b.stage, rel), "rb").read()
    modes = {m: built(m) for m in ("deployment", "development", "preview", "play")}
    full = path("cc-full.jpg"); DV.user_cover(own, None, full, DV.COVER_BOX_HI)
    ok("console_cover_every_console_package", all(modes[m][0] == "user" and modes[m][1] == expect(700, 900) for m in ("deployment", "development", "preview")), {m: v[1] for m, v in modes.items()})
    ok("console_cover_the_computers_play_home_keeps_full_resolution", modes["play"][2] == open(full, "rb").read() and modes["play"][1] == Image.open(full).size, modes["play"][1])
    DV.REDUCED_CONSOLE_COVERS = False
    try: before = built("deployment")
    finally: DV.REDUCED_CONSOLE_COVERS = True
    plain = path("cc-plain.jpg"); DV.user_cover(own, None, plain)
    ok("console_cover_off_gives_the_covers_made_before", before[2] == open(plain, "rb").read() and before[1] == Image.open(plain).size, before[1])


if __name__ == "__main__": sys.exit(main())
