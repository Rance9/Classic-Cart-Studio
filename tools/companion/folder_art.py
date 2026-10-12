"""Custom artwork for console HOME folders (Classic Cart Studio for Windows).

A folder's artwork is chosen with the native file picker, validated (PNG / JPEG / WebP / BMP / GIF, at most 8 MB, at
least 160 x 160) and kept in the Studio's data folder as the untouched source plus two derivatives the themes and the
future console use directly:
  personal/art/folders/<folder_id>-src.<ext>    the file as chosen
  personal/art/folders/<folder_id>-cover.jpg    2:3 portrait, center crop, 480 x 720   (Browse tiles, shelves, cards)
  personal/art/folders/<folder_id>-wide.jpg     16:9 landscape, center crop, 810 x 447 (hero and landmark use)
Nothing is written unless the chosen file validates; replacing existing artwork is the caller's explicit decision (the
Windows UI asks first); removing artwork deletes only that folder's files. Without custom artwork the themes draw the
collage of the folder's own game covers (the default fallback). Files are served by the companion under
personal/art/… for the Studio's own origin only.
"""
from __future__ import annotations
import os, re, time
from personal import ART_DIR

FOLDER_ART_DIR = os.path.join(ART_DIR, "folders")
ALLOWED = {"png", "jpg", "jpeg", "webp", "bmp", "gif"}
MAX_BYTES = 8 * 1024 * 1024
MIN_SIDE = 160
COVER = (480, 720); WIDE = (810, 447)

def _fid(folder_id: str) -> str:
    fid = re.sub(r"[^A-Za-z0-9_:-]", "", str(folder_id or ""))[:64].replace(":", "_")
    if not fid: raise ValueError("folder id missing")
    return fid

def _crop_to(im, size):
    from PIL import Image
    w, h = im.size; tw, th = size; s = max(tw / w, th / h); z = im.resize((max(1, round(w * s)), max(1, round(h * s))), Image.LANCZOS)
    x0 = (z.width - tw) // 2; y0 = (z.height - th) // 2; return z.crop((x0, y0, x0 + tw, y0 + th))

def validate(path: str) -> tuple:
    """returns (PIL image, extension) or raises ValueError with a message the UI shows as-is"""
    from PIL import Image
    if not path or not os.path.isfile(path): raise ValueError("no file was chosen")
    ext = os.path.splitext(path)[1].lower().lstrip(".")
    if ext not in ALLOWED: raise ValueError("choose a PNG, JPEG, WebP, BMP or GIF image")
    if os.path.getsize(path) > MAX_BYTES: raise ValueError("the image is larger than 8 MB")
    try:
        im = Image.open(path); im.verify(); im = Image.open(path)
        if getattr(im, "is_animated", False): im.seek(0)
        im = im.convert("RGB")
    except Exception: raise ValueError("the file is not a readable image")
    if im.width < MIN_SIDE or im.height < MIN_SIDE: raise ValueError(f"the image is smaller than {MIN_SIDE} x {MIN_SIDE} pixels")
    return im, ("jpg" if ext == "jpeg" else ext)

def set_art(folder_id: str, path: str) -> dict:
    fid = _fid(folder_id); im, ext = validate(path)
    os.makedirs(FOLDER_ART_DIR, exist_ok=True)
    clear(fid, keep_dir=True)                       # the previous files go only after the new one validated
    src = os.path.join(FOLDER_ART_DIR, f"{fid}-src.{ext}")
    with open(path, "rb") as f, open(src + ".tmp", "wb") as g: g.write(f.read())
    os.replace(src + ".tmp", src)
    _crop_to(im, COVER).save(os.path.join(FOLDER_ART_DIR, f"{fid}-cover.jpg"), "JPEG", quality=90, optimize=True)
    _crop_to(im, WIDE).save(os.path.join(FOLDER_ART_DIR, f"{fid}-wide.jpg"), "JPEG", quality=90, optimize=True)
    return describe(fid, os.path.basename(path), (im.width, im.height))

def describe(folder_id: str, source_name: str = "", size=None) -> dict:
    fid = _fid(folder_id); cover = os.path.join(FOLDER_ART_DIR, f"{fid}-cover.jpg")
    if not os.path.isfile(cover): return {"src": None, "wide": None, "provenance": "generated"}
    v = int(os.path.getmtime(cover))
    return {"src": f"personal/art/folders/{fid}-cover.jpg?v={v}", "wide": f"personal/art/folders/{fid}-wide.jpg?v={v}", "provenance": "custom", "source_name": source_name or "", "source_size": list(size) if size else None, "updated": time.strftime("%Y-%m-%dT%H:%M:%S", time.localtime(v))}

def clear(folder_id: str, keep_dir: bool = False) -> dict:
    fid = _fid(folder_id); n = 0; failed = []
    if os.path.isdir(FOLDER_ART_DIR):
        for name in os.listdir(FOLDER_ART_DIR):
            if name.startswith(fid + "-") and (name.split("-", 1)[1].split(".")[0] in ("src", "cover", "wide")):
                try: os.remove(os.path.join(FOLDER_ART_DIR, name)); n += 1
                except OSError as e: failed.append(f"{name}: {e}")
    out = {"src": None, "wide": None, "provenance": "generated", "removed": n}
    if failed: out["failed"] = failed
    return out
