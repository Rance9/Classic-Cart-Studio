"""Bake the player's menu fonts (CCSF2 atlases, the native HOME's own format and baker): the HOME's Silkscreen for headings and Barlow
for text, at the sizes the player's 1280 x 720 menus use and at twice those sizes (<name>-2x.ccsf: the game window's full-resolution canvas),
covering printable ASCII, Latin-1 and the punctuation the menus and game titles use (a character outside the set is drawn as '?').
  python bake_fonts.py <theme root: the site's brand/home folder> <out dir>"""
import os, sys
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, os.path.join(HERE, "..", "companion", "native_home")); sys.path.insert(0, os.path.join(HERE, "..", "companion"))
import derive as DV   # noqa: E402
FONTS = [("silk17", "silk", 400, 17), ("sans700_30", "sans", 700, 30), ("sans600_22", "sans", 600, 22), ("sans400_17", "sans", 400, 17)]
CHARS = [chr(c) for c in range(0x20, 0x7F)] + [chr(c) for c in range(0xA0, 0x100)] + list("‘’“”–—…·•×←→↑↓™®©")
def main(theme_root: str, out: str) -> None:
    os.makedirs(out, exist_ok=True)
    import shutil   # the atlases are made from these fonts: their licenses travel with them
    shutil.copy2(os.path.join(DV.FONTS_DIR, "OFL-Barlow.txt"), out); shutil.copy2(os.path.join(theme_root, "fonts", "OFL-Silkscreen.txt"), out)
    for name, fam, w, px in FONTS:   # the computer's game window: no pixel typeface (the headings' Silkscreen becomes its smooth stand-in)
        hd = DV.HD_FACES.get(fam); gf = (hd[0], hd[1], max(1, round(px * hd[2])), hd[3]) if hd else None; gf2 = (hd[0], hd[1], max(1, round(px * 2 * hd[2])), hd[3]) if hd else None
        info = DV.bake_font(name, fam, w, px, CHARS, theme_root, os.path.join(out, name + ".ccsf"), glyphs_from=gf)
        print(name, os.path.getsize(os.path.join(out, name + ".ccsf")), "bytes", ("missing: " + ascii("".join(info.get("missing") or []))) if isinstance(info, dict) and info.get("missing") else "")
        DV.bake_font(name, fam, w, px * 2, CHARS, theme_root, os.path.join(out, name + "-2x.ccsf"), glyphs_from=gf2); print(name + "-2x", os.path.getsize(os.path.join(out, name + "-2x.ccsf")), "bytes")
if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
