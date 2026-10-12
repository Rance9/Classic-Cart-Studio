"""Custom artwork for console HOME folders (Classic Cart Studio for Windows).

A folder's artwork is chosen with the native file picker, validated (PNG / JPEG / WebP / BMP / GIF, at most 8 MB, at
least 160 x 160) and kept in the Studio's data folder as the untouched source plus two derivatives the themes and the
future console use directly:
  personal/art/folders/<folder_id>-src.<ext>    the file as chosen
  personal/art/folders/<folder_id>-cover.jpg    2:3 portrait, center crop, 480 x 720   (Browse tiles, shelves, cards)
  personal/art/folders/<folder_id>-wide.jpg     16:9 landscape, center crop, 810 x 447 (hero and landmark use)
Replacement is transactional: the complete new set (source copy + both derivatives) is written to staging names and
re-verified first; then the previous files step aside as .bak copies and the staged set moves in. A failure while
preparing removes the staging files; a failure during the swap removes what was swapped in and restores the .bak copies;
an interruption (crash, power loss) is repaired by recover() on the next operation: a complete, verified final set means
the swap finished (backups dropped), anything else restores the backups. Removing artwork deletes only that folder's files.
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
BAK = ".bak"

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
    return [n for n in os.listdir(FOLDER_ART_DIR) if n.startswith(fid + "-") and n.split("-", 1)[1].split(".")[0] in ("src", "cover", "wide") and not n.endswith(STAGE) and not n.endswith(BAK)]   # final files only

def _verify_jpeg(path: str, size):
    from PIL import Image
    with Image.open(path) as im:
        im.verify()
    with Image.open(path) as im:
        if im.size != tuple(size): raise ValueError(f"derivative has the wrong size {im.size}")

def _swap(src: str, dst: str): os.replace(src, dst)   # one atomic file replace; patched by tests to inject a failure mid-swap

def recover(folder_id: str) -> dict:
    """Finish or undo an interrupted replacement. A swap in progress leaves <name>.bak copies of the previous files: when
    every final file is present and verifies, the swap had completed and the backups go; otherwise the backups are put
    back over whatever the swap managed to write. Staging files are always removed. Called before any operation."""
    fid = _fid(folder_id); out = {"recovered": None}
    if not os.path.isdir(FOLDER_ART_DIR): return out
    names = os.listdir(FOLDER_ART_DIR); baks = [n for n in names if n.startswith(fid + "-") and n.endswith(BAK)]
    for n in names:
        if n.startswith(fid + "-") and n.endswith(STAGE):
            try: os.remove(os.path.join(FOLDER_ART_DIR, n))
            except OSError: pass
    if not baks: return out
    cover = os.path.join(FOLDER_ART_DIR, f"{fid}-cover.jpg"); wide = os.path.join(FOLDER_ART_DIR, f"{fid}-wide.jpg")
    complete = os.path.isfile(cover) and os.path.isfile(wide) and any(n.startswith(fid + "-src.") and not n.endswith(BAK) and not n.endswith(STAGE) for n in names)
    if complete:
        try: _verify_jpeg(cover, COVER); _verify_jpeg(wide, WIDE)
        except Exception: complete = False
    if complete:
        for n in baks:
            try: os.remove(os.path.join(FOLDER_ART_DIR, n))
            except OSError: pass
        out["recovered"] = "completed"
    else:
        for n in names:                                   # drop whatever the interrupted swap wrote
            if n.startswith(fid + "-") and not n.endswith(BAK) and not n.endswith(STAGE):
                try: os.remove(os.path.join(FOLDER_ART_DIR, n))
                except OSError: pass
        for n in baks: os.replace(os.path.join(FOLDER_ART_DIR, n), os.path.join(FOLDER_ART_DIR, n[:-len(BAK)]))
        out["recovered"] = "restored previous artwork"
    return out

def set_art(folder_id: str, path: str) -> dict:
    fid = _fid(folder_id); recover(fid); im, ext = validate(path)
    os.makedirs(FOLDER_ART_DIR, exist_ok=True)
    src = os.path.join(FOLDER_ART_DIR, f"{fid}-src.{ext}"); cover = os.path.join(FOLDER_ART_DIR, f"{fid}-cover.jpg"); wide = os.path.join(FOLDER_ART_DIR, f"{fid}-wide.jpg")
    finals = [cover, wide, src]; staged = [f + STAGE for f in finals]
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
    # 3. group swap with rollback: previous files step aside as .bak, the staged set moves in; any failure puts the backups back
    previous = [os.path.join(FOLDER_ART_DIR, n) for n in _files(fid)]; backed = []; swapped = []
    try:
        for p in previous: _swap(p, p + BAK); backed.append(p)
        for st, fin in zip(staged, finals): _swap(st, fin); swapped.append(fin)
    except Exception as e:
        for fin in swapped:
            try: os.remove(fin)
            except OSError: pass
        for p in backed:
            try: os.replace(p + BAK, p)
            except OSError: pass
        for p in staged:
            try: os.remove(p)
            except OSError: pass
        raise ValueError(f"artwork could not be replaced; the previous artwork was restored ({e})")
    for p in backed:
        try: os.remove(p + BAK)
        except OSError: pass
    return describe(fid, os.path.basename(path), (im.width, im.height))

def describe(folder_id: str, source_name: str = "", size=None) -> dict:
    fid = _fid(folder_id); recover(fid); cover = os.path.join(FOLDER_ART_DIR, f"{fid}-cover.jpg")
    if not os.path.isfile(cover): return {"src": None, "wide": None, "provenance": "generated"}
    v = int(os.path.getmtime(cover))
    return {"src": f"personal/art/folders/{fid}-cover.jpg?v={v}", "wide": f"personal/art/folders/{fid}-wide.jpg?v={v}", "provenance": "custom", "source_name": source_name or "", "source_size": list(size) if size else None, "updated": time.strftime("%Y-%m-%dT%H:%M:%S", time.localtime(v))}

def clear(folder_id: str, keep_dir: bool = False) -> dict:
    fid = _fid(folder_id); recover(fid); n = 0; failed = []
    for name in _files(fid):
        try: os.remove(os.path.join(FOLDER_ART_DIR, name)); n += 1
        except OSError as e: failed.append(f"{name}: {e}")
    out = {"src": None, "wide": None, "provenance": "generated", "removed": n}
    if failed: out["failed"] = failed
    return out
