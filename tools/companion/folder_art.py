"""Custom artwork for console HOME folders (Classic Cart Studio for Windows).

A folder's artwork is chosen with the native file picker, validated (PNG / JPEG / WebP / BMP / GIF, at most 8 MB, at
least 160 x 160) and kept in the Studio's data folder as the untouched source plus two derivatives the themes and the
future console use directly:
  personal/art/folders/<folder_id>-src.<ext>    the file as chosen
  personal/art/folders/<folder_id>-cover.jpg    2:3 portrait, center crop, 480 x 720   (Browse tiles, shelves, cards)
  personal/art/folders/<folder_id>-wide.jpg     16:9 landscape, center crop, 810 x 447 (hero and landmark use)
Replacement is transactional: the complete new set (source copy + both derivatives) is written to staging names and
re-verified first; only then are the previous files swapped out, one atomic replace per file. A failure at any point
removes the staging files and leaves the previous artwork untouched. Removing artwork deletes only that folder's files.
Without custom artwork the themes draw the collage of the folder's own game covers (the default fallback). Files are
served by the companion under personal/art/… for the Studio's own origin only.
"""
from __future__ import annotations
import os, re, time
from personal import ART_DIR

FOLDER_ART_DIR = os.path.join(ART_DIR, "folders")
ALLOWED = {"png", "jpg", "jpeg", "webp", "bmp", "gif"}
MAX_BYTES = 8 * 1024 * 1024
MIN_SIDE = 160
COVER = (480, 720); WIDE = (810, 447)
STAGE = ".new"

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

def _files(fid: str) -> list:
    if not os.path.isdir(FOLDER_ART_DIR): return []
    return [n for n in os.listdir(FOLDER_ART_DIR) if n.startswith(fid + "-") and n.split("-", 1)[1].split(".")[0] in ("src", "cover", "wide")]

def _verify_jpeg(path: str, size):
    from PIL import Image
    with Image.open(path) as im:
        im.verify()
    with Image.open(path) as im:
        if im.size != tuple(size): raise ValueError(f"derivative has the wrong size {im.size}")

def set_art(folder_id: str, path: str) -> dict:
    fid = _fid(folder_id); im, ext = validate(path)
    os.makedirs(FOLDER_ART_DIR, exist_ok=True)
    src = os.path.join(FOLDER_ART_DIR, f"{fid}-src.{ext}"); cover = os.path.join(FOLDER_ART_DIR, f"{fid}-cover.jpg"); wide = os.path.join(FOLDER_ART_DIR, f"{fid}-wide.jpg")
    staged = [src + STAGE, cover + STAGE, wide + STAGE]
    try:
        # 1. prepare the complete replacement set under staging names
        with open(path, "rb") as f, open(src + STAGE, "wb") as g: g.write(f.read())
        _crop_to(im, COVER).save(cover + STAGE, "JPEG", quality=90, optimize=True)
        _crop_to(im, WIDE).save(wide + STAGE, "JPEG", quality=90, optimize=True)
        # 2. verify every staged file re-opens with the expected size before anything existing is touched
        _verify_jpeg(cover + STAGE, COVER); _verify_jpeg(wide + STAGE, WIDE)
        if os.path.getsize(src + STAGE) != os.path.getsize(path): raise ValueError("the source copy is incomplete")
    except Exception as e:
        for p in staged:
            try: os.remove(p)
            except OSError: pass
        raise ValueError(f"artwork could not be prepared; the previous artwork is unchanged ({e})")
    # 3. swap: atomic replace per file; a source with another extension from the previous artwork goes last
    old_src = [n for n in _files(fid) if n.startswith(fid + "-src.") and not n.endswith(STAGE) and n != os.path.basename(src)]
    os.replace(cover + STAGE, cover); os.replace(wide + STAGE, wide); os.replace(src + STAGE, src)
    for n in old_src:
        try: os.remove(os.path.join(FOLDER_ART_DIR, n))
        except OSError: pass
    return describe(fid, os.path.basename(path), (im.width, im.height))

def describe(folder_id: str, source_name: str = "", size=None) -> dict:
    fid = _fid(folder_id); cover = os.path.join(FOLDER_ART_DIR, f"{fid}-cover.jpg")
    if not os.path.isfile(cover): return {"src": None, "wide": None, "provenance": "generated"}
    v = int(os.path.getmtime(cover))
    return {"src": f"personal/art/folders/{fid}-cover.jpg?v={v}", "wide": f"personal/art/folders/{fid}-wide.jpg?v={v}", "provenance": "custom", "source_name": source_name or "", "source_size": list(size) if size else None, "updated": time.strftime("%Y-%m-%dT%H:%M:%S", time.localtime(v))}

def clear(folder_id: str, keep_dir: bool = False) -> dict:
    fid = _fid(folder_id); n = 0; failed = []
    for name in _files(fid) + [p for p in (os.listdir(FOLDER_ART_DIR) if os.path.isdir(FOLDER_ART_DIR) else []) if p.startswith(fid + "-") and p.endswith(STAGE)]:
        try: os.remove(os.path.join(FOLDER_ART_DIR, name)); n += 1
        except OSError as e: failed.append(f"{name}: {e}")
    out = {"src": None, "wide": None, "provenance": "generated", "removed": n}
    if failed: out["failed"] = failed
    return out
