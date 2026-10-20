"""Content customization (N3, Classic Cart Studio for Windows): the person's own box art, Official description and Game
overview for a game version, kept beside (never over) the canonical content.

Two layers. Canonical content (authentic box art or the mock cover, the approved texts in reports/descriptions.json, the
library overviews) is never written by this module. User overrides live only in the Studio's data folder:

  <data>/overrides/overrides.json          {"schema":"ccs-overrides/1","games":{"<version_id>":{"art":{...},"official":{...},"overview":{...}}}}
  <data>/overrides/art/<vid>/<sha16>.<ext> the imported image, ORIGINAL BYTES (never re-encoded); content-addressed
  <data>/overrides/cache/                  page-display / working derivatives (rebuildable; keyed by master hash + crop)

Keys are EXACT version ids (catalog game_id / library version id), never titles. An absent field means "no override" for that
field; the three fields are independent (resetting one never touches the others). Writes are atomic (temporary file +
os.replace) and keep the previous file as overrides.json.bak. A master that a reset or replacement no longer references is
deleted only after the new state has been saved.

Contract used by the console packager and the native preview (keep it stable):
  load() -> dict                     the whole store (schema + games)
  game(vid) -> dict                  one version's overrides ({} when none)
  art_master_path(vid) -> str|None   absolute path of the imported original
  art_crop(vid) -> dict|None         normalized crop rectangle {x,y,w,h} (0..1) on the master as displayed (EXIF orientation applied); None = whole image, fitted
  set_text(vid, field, text)         field: official | overview
  reset(vid, field)                  field: art | official | overview
  import_art(vid, src, original_name) -> art record   src: a file path or bytes
  set_crop(vid, crop_or_None)
  display_path(vid) -> str|None      page-display derivative (crop applied, longest side about 900 px, JPEG), cached by master hash + crop

Images: the format is read from the bytes (PNG, JPEG, WebP, GIF, BMP), the whole image is decoded once to prove it is real
content. There is no file-size or ordinary dimension limit; only a protective ceiling against decompression bombs
(MAX_PIXELS) and unreadable data. Uploads are streamed to disk by the HTTP route (handle_post), never held in memory.
"""
from __future__ import annotations
import os, sys, re, io, json, time, shutil, hashlib, threading, tempfile, zipfile, urllib.parse

HERE = os.path.dirname(os.path.abspath(__file__))
try:
    from common import data_path
except ImportError:   # stand-alone use (tests, the console packager): the shared helpers live in tools/rom-pipeline
    sys.path.insert(0, os.path.join(HERE, "..", "rom-pipeline")); from common import data_path

SCHEMA = "ccs-overrides/1"
FIELDS = ("art", "official", "overview")
TEXT_FIELDS = ("official", "overview")
ROOT = data_path("overrides")           # module global on purpose: tests point it at a scratch folder
MAX_PIXELS = 200_000_000                 # protective ceiling only (decompression bombs); normal photos and scans are far below it
MAX_TEXT = 100_000                       # characters; a guard against pasted binary junk, far above any description
DISPLAY_SIDE = 900; WORK_SIDE = 1600
FORMATS = {"PNG": ("png", "png"), "JPEG": ("jpeg", "jpg"), "MPO": ("jpeg", "jpg"), "WEBP": ("webp", "webp"), "GIF": ("gif", "gif"), "BMP": ("bmp", "bmp")}
CTYPES = {"png": "image/png", "jpeg": "image/jpeg", "webp": "image/webp", "gif": "image/gif", "bmp": "image/bmp"}
VID_RE = re.compile(r"^[A-Za-z0-9_-]{1,64}$")
MASTER_RE = re.compile(r"^art/([A-Za-z0-9_-]{1,64})/([0-9a-f]{16})\.(png|jpg|webp|gif|bmp)$")
LOCK = threading.RLock()
BACKGROUND = (14, 14, 18)                # transparent pixels are shown over the cover background, never over white

def _now() -> str: return time.strftime("%Y-%m-%dT%H:%M:%S")
def _vid(vid) -> str:
    v = str(vid or "")
    if not VID_RE.match(v): raise ValueError("unknown game version")
    return v
def _store() -> str: return os.path.join(ROOT, "overrides.json")
def _abs(rel: str) -> str: return os.path.join(ROOT, *rel.split("/"))

# ------------------------------------------------------------------------------------------------ state file (atomic + .bak)
def _read(p: str):
    with open(p, encoding="utf-8") as f: j = json.load(f)
    if not isinstance(j, dict) or j.get("schema") != SCHEMA or not isinstance(j.get("games"), dict): raise ValueError("not an overrides store")
    return j

def load() -> dict:
    """The whole store. A missing file is an empty store; a damaged file falls back to its .bak copy (the damaged file is left
    in place for inspection and replaced only by the next successful save)."""
    with LOCK:
        p = _store()
        if not os.path.exists(p): return {"schema": SCHEMA, "games": {}}
        try: return _read(p)
        except Exception:
            try: return _read(p + ".bak")
            except Exception: return {"schema": SCHEMA, "games": {}}

def _save(state: dict):
    os.makedirs(ROOT, exist_ok=True); p = _store()
    state = {"schema": SCHEMA, "saved": _now(), "games": {k: v for k, v in sorted(state.get("games", {}).items()) if v}}
    fd, tmp = tempfile.mkstemp(prefix="overrides-", suffix=".tmp", dir=ROOT)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            json.dump(state, f, ensure_ascii=False, indent=1); f.flush(); os.fsync(f.fileno())
        _read(tmp)                                           # the new file must parse before it replaces anything
        if os.path.exists(p):
            try: _read(p); shutil.copy2(p, p + ".bak")       # keep the last GOOD state as the backup
            except Exception: pass
        os.replace(tmp, p)
    except Exception:
        try: os.remove(tmp)
        except OSError: pass
        raise

def game(vid) -> dict:
    with LOCK: return json.loads(json.dumps(load()["games"].get(_vid(vid)) or {}))

# ------------------------------------------------------------------------------------------------ texts
def _clean_text(text) -> str:
    if not isinstance(text, str): raise ValueError("the text must be a string")
    t = text.replace("\r\n", "\n").replace("\r", "\n").replace("\x00", "")
    t = "\n".join(line.rstrip() for line in t.split("\n")).strip()
    t = re.sub(r"\n{3,}", "\n\n", t)
    if not t: raise ValueError("the text is empty; use Reset to Original to go back to the original text")
    if len(t) > MAX_TEXT: raise ValueError(f"the text is longer than {MAX_TEXT:,} characters")
    return t

def set_text(vid, field: str, text) -> dict:
    vid = _vid(vid)
    if field not in TEXT_FIELDS: raise ValueError("field must be official or overview")
    t = _clean_text(text)
    with LOCK:
        st = load(); g = st["games"].setdefault(vid, {}); g[field] = {"text": t, "saved": _now()}; _save(st)
        return dict(g)

def reset(vid, field: str) -> dict:
    """Removes ONE field's override; the other fields stay exactly as they are."""
    vid = _vid(vid)
    if field not in FIELDS: raise ValueError("field must be art, official or overview")
    with LOCK:
        st = load(); g = st["games"].get(vid) or {}
        had = g.pop(field, None)
        if g: st["games"][vid] = g
        else: st["games"].pop(vid, None)
        if had is not None: _save(st)
        if field == "art": _gc(vid, st)                    # after the save: the old master goes only once nothing references it
        return dict(g)

# ------------------------------------------------------------------------------------------------ images
def _pil():
    from PIL import Image
    if (Image.MAX_IMAGE_PIXELS or 0) < MAX_PIXELS: Image.MAX_IMAGE_PIXELS = MAX_PIXELS   # our own check below is the real limit
    return Image

def inspect_image(path: str) -> dict:
    """Validate an image file by its CONTENT (format from the bytes, full decode). Returns format/ext/size as displayed
    (EXIF orientation applied). Raises ValueError with a message the page shows as-is."""
    Image = _pil()
    if not path or not os.path.isfile(path): raise ValueError("no file was chosen")
    if os.path.getsize(path) == 0: raise ValueError("the file is empty")
    try:
        with Image.open(path) as im:
            fmt = im.format; w, h = im.size
            if fmt not in FORMATS: raise ValueError("unsupported")
            if w * h > MAX_PIXELS: raise OverflowError
            if w < 1 or h < 1: raise ValueError("empty image")
            orient = 1
            try: orient = int((im.getexif() or {}).get(0x0112, 1) or 1)
            except Exception: orient = 1
        with Image.open(path) as im:
            im.verify()                                      # structure / checksums (PNG CRCs, truncated data)
        with Image.open(path) as im:
            if getattr(im, "is_animated", False): im.seek(0)
            im.load()                                        # real content: the whole first frame decodes
    except OverflowError:
        raise ValueError(f"the image is too large to process safely (more than {MAX_PIXELS // 1_000_000} megapixels)")
    except ValueError as e:
        if str(e) == "unsupported": raise ValueError("choose a PNG, JPEG, WebP, GIF or BMP image")
        raise ValueError("the file is not a readable image")
    except Exception as e:
        if type(e).__name__ == "DecompressionBombError": raise ValueError(f"the image is too large to process safely (more than {MAX_PIXELS // 1_000_000} megapixels)")
        raise ValueError("the file is not a readable image")
    if orient in (5, 6, 7, 8): w, h = h, w
    fmt_name, ext = FORMATS[fmt]
    return {"format": fmt_name, "ext": ext, "width": w, "height": h, "orientation": orient}

def _hash_copy(src, dst_dir: str) -> tuple[str, str]:
    """Copy a path / bytes / binary stream into a new temporary file under dst_dir, hashing on the way. Returns (tmp path, sha256)."""
    os.makedirs(dst_dir, exist_ok=True); h = hashlib.sha256()
    fd, tmp = tempfile.mkstemp(prefix="import-", suffix=".part", dir=dst_dir)
    try:
        with os.fdopen(fd, "wb") as out:
            if isinstance(src, (bytes, bytearray, memoryview)):
                b = bytes(src); h.update(b); out.write(b)
            else:
                f = open(src, "rb") if isinstance(src, (str, os.PathLike)) else src
                try:
                    while True:
                        chunk = f.read(1 << 20)
                        if not chunk: break
                        h.update(chunk); out.write(chunk)
                finally:
                    if f is not src: f.close()
            out.flush(); os.fsync(out.fileno())
    except Exception:
        try: os.remove(tmp)
        except OSError: pass
        raise
    return tmp, h.hexdigest()

def _safe_name(name) -> str:
    n = os.path.basename(str(name or "").replace("\\", "/"))
    n = re.sub(r"[\x00-\x1f]", "", n).strip()[:200]
    return n or "image"

def _place_master(vid: str, tmp: str, sha: str, info: dict, original_name: str) -> dict:
    rel = f"art/{vid}/{sha[:16]}.{info['ext']}"; dest = _abs(rel); os.makedirs(os.path.dirname(dest), exist_ok=True)
    if os.path.exists(dest):
        try: os.remove(tmp)
        except OSError: pass
    else: os.replace(tmp, dest)
    rec = {"master": rel, "sha256": sha, "width": info["width"], "height": info["height"], "format": info["format"], "crop": None, "imported": _now(), "original_name": _safe_name(original_name)}
    meta = dest + ".meta.json"; t = meta + ".tmp"   # sidecar: lets a staged import be committed later with its original facts
    with open(t, "w", encoding="utf-8") as f: json.dump(rec, f, ensure_ascii=False)
    os.replace(t, meta)
    return rec

def stage_art(vid, src, original_name: str = "") -> dict:
    """Validate and keep an image for this version WITHOUT changing the override (the editor's preview; Save commits it)."""
    vid = _vid(vid); tmpdir = os.path.join(ROOT, "tmp")
    tmp, sha = _hash_copy(src, tmpdir)
    try: info = inspect_image(tmp)
    except Exception:
        try: os.remove(tmp)
        except OSError: pass
        raise
    with LOCK: rec = _place_master(vid, tmp, sha, info, original_name)
    return dict(rec, staged=True)

def commit_art(vid, master: str, crop=None) -> dict:
    """Make a staged (or already present) master this version's art override, with an optional crop; old masters are removed
    after the new state is saved."""
    vid = _vid(vid); m = MASTER_RE.match(str(master or ""))
    if not m or m.group(1) != vid: raise ValueError("unknown image for this game")
    p = _abs(master)
    if not os.path.isfile(p): raise ValueError("that image is no longer available; import it again")
    sha = hashlib.sha256()
    with open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""): sha.update(chunk)
    sha = sha.hexdigest()
    if not sha.startswith(m.group(2)): raise ValueError("the stored image is damaged; import it again")
    try: rec = json.load(open(p + ".meta.json", encoding="utf-8"))
    except Exception:
        info = inspect_image(p); rec = {"master": master, "sha256": sha, "width": info["width"], "height": info["height"], "format": info["format"], "crop": None, "imported": _now(), "original_name": os.path.basename(p)}
    rec = {k: rec.get(k) for k in ("master", "sha256", "width", "height", "format", "crop", "imported", "original_name")}
    rec["master"] = master; rec["sha256"] = sha; rec["crop"] = _crop(crop)
    with LOCK:
        st = load(); g = st["games"].setdefault(vid, {}); g["art"] = rec; _save(st); _gc(vid, st)
    return dict(rec)

def import_art(vid, src, original_name: str = "") -> dict:
    """Import an image (file path or bytes) as this version's art override: the original bytes are kept, fitted whole (no crop)."""
    rec = stage_art(vid, src, original_name)
    return commit_art(vid, rec["master"], None)

def _crop(crop):
    if crop is None: return None
    if not isinstance(crop, dict): raise ValueError("crop must be an object or null")
    try: x, y, w, h = (float(crop[k]) for k in ("x", "y", "w", "h"))
    except Exception: raise ValueError("crop needs x, y, w and h between 0 and 1")
    if any(v != v for v in (x, y, w, h)): raise ValueError("crop values must be numbers")
    x = min(max(x, 0.0), 1.0); y = min(max(y, 0.0), 1.0)
    w = min(max(w, 0.0), 1.0 - x); h = min(max(h, 0.0), 1.0 - y)
    if w < 0.005 or h < 0.005: raise ValueError("the crop area is too small")
    if w > 0.9999 and h > 0.9999: return None                  # the whole image is the same as no crop
    return {"x": round(x, 6), "y": round(y, 6), "w": round(w, 6), "h": round(h, 6)}

def set_crop(vid, crop) -> dict:
    vid = _vid(vid); c = _crop(crop)
    with LOCK:
        st = load(); g = st["games"].get(vid) or {}
        if not g.get("art"): raise ValueError("this game has no custom image to crop")
        g["art"]["crop"] = c; _save(st)
        return dict(g["art"])

def art_master_path(vid) -> str | None:
    a = game(vid).get("art")
    if not a or not MASTER_RE.match(str(a.get("master") or "")): return None
    p = _abs(a["master"]); return p if os.path.isfile(p) else None

def art_crop(vid) -> dict | None:
    a = game(vid).get("art"); return (a or {}).get("crop") or None

def _crop_key(crop) -> str:
    return "full" if not crop else hashlib.sha256(json.dumps(crop, sort_keys=True).encode()).hexdigest()[:10]

def _render(master: str, crop, side: int, out: str):
    """Oriented, color-converted, optionally cropped, downscaled JPEG derivative (never upscaled)."""
    Image = _pil(); from PIL import ImageOps
    with Image.open(master) as im0:
        if getattr(im0, "is_animated", False): im0.seek(0)
        if im0.format == "JPEG": im0.draft("RGB", (side * 2, side * 2) if not crop else (side * 4, side * 4))   # fast large-JPEG decode
        im = ImageOps.exif_transpose(im0)
        if im.mode == "P": im = im.convert("RGBA")
        if im.mode in ("RGBA", "LA", "PA") or (im.mode == "P" and "transparency" in im.info):
            im = im.convert("RGBA"); bg = Image.new("RGB", im.size, BACKGROUND); bg.paste(im, mask=im.split()[-1]); im = bg
        elif im.mode == "CMYK": im = im.convert("RGB")
        elif im.mode != "RGB": im = im.convert("RGB")
        if crop:
            W, H = im.size; box = (round(crop["x"] * W), round(crop["y"] * H), round((crop["x"] + crop["w"]) * W), round((crop["y"] + crop["h"]) * H))
            box = (max(0, box[0]), max(0, box[1]), min(W, max(box[0] + 1, box[2])), min(H, max(box[1] + 1, box[3]))); im = im.crop(box)
        im.thumbnail((side, side), Image.LANCZOS, reducing_gap=3.0)
        os.makedirs(os.path.dirname(out), exist_ok=True); tmp = out + ".tmp"
        im.save(tmp, "JPEG", quality=88, optimize=True, progressive=True)
    os.replace(tmp, out)

def _derivative(vid: str, master_rel: str, crop, side: int, tag: str) -> str:
    sha16 = MASTER_RE.match(master_rel).group(2); key = f"{vid}-{sha16}-{_crop_key(crop)}-{tag}{side}"
    out = os.path.join(ROOT, "cache", key + ".jpg")
    if os.path.isfile(out): return out
    with LOCK:
        if os.path.isfile(out): return out
        _render(_abs(master_rel), crop, side, out)
        for n in os.listdir(os.path.dirname(out)):            # one display + one working derivative per version is enough
            if n.startswith(vid + "-") and n.endswith(f"-{tag}{side}.jpg") and n != key + ".jpg":
                try: os.remove(os.path.join(ROOT, "cache", n))
                except OSError: pass
    return out

def display_path(vid) -> str | None:
    """The page-display derivative of the CURRENT art override (crop applied, longest side about 900 px), cached by master hash + crop."""
    vid = _vid(vid); a = game(vid).get("art")
    if not a or not art_master_path(vid): return None
    return _derivative(vid, a["master"], a.get("crop"), DISPLAY_SIDE, "d")

def work_path(vid, sha16: str = "") -> str | None:
    """Uncropped working derivative (longest side 1600 px) of the current or a staged master: the editor crops on this, never on the
    multi-megabyte original."""
    vid = _vid(vid); a = game(vid).get("art") or {}; cur = a.get("master") or ""
    rel = cur if cur and (not sha16 or cur.split("/")[-1].startswith(sha16)) else None
    if rel is None and sha16:                                  # a staged image of this version (imported, not saved yet)
        d = _abs(f"art/{vid}")
        hit = [n for n in (os.listdir(d) if os.path.isdir(d) else []) if MASTER_RE.match(f"art/{vid}/{n}") and n.startswith(sha16)]
        rel = f"art/{vid}/{hit[0]}" if hit else None
    if not rel or not os.path.isfile(_abs(rel)): return None
    return _derivative(vid, rel, None, WORK_SIDE, "w")

def _gc(vid: str, st: dict):
    """Delete masters (and their sidecars) of this version that the SAVED state does not reference, plus stale staging files."""
    keep = ((st["games"].get(vid) or {}).get("art") or {}).get("master")
    d = _abs(f"art/{vid}")
    if os.path.isdir(d):
        for n in os.listdir(d):
            rel = f"art/{vid}/{n[:-len('.meta.json')] if n.endswith('.meta.json') else n}"
            if rel != keep:
                try: os.remove(os.path.join(d, n))
                except OSError: pass
        if not os.listdir(d):
            try: os.rmdir(d)
            except OSError: pass
    c = os.path.join(ROOT, "cache"); sha16 = keep.split("/")[-1][:16] if keep else None
    if os.path.isdir(c):
        for n in os.listdir(c):
            if n.startswith(vid + "-") and (not sha16 or not n.startswith(f"{vid}-{sha16}-")):
                try: os.remove(os.path.join(c, n))
                except OSError: pass
    t = os.path.join(ROOT, "tmp")
    if os.path.isdir(t):
        for n in os.listdir(t):
            p = os.path.join(t, n)
            try:
                if time.time() - os.path.getmtime(p) > 3600: os.remove(p)
            except OSError: pass

def public(st: dict | None = None) -> dict:
    """The store as the page sees it: everything except absolute paths (there are none in the store) plus a cache key per image."""
    st = st or load(); out = {}
    for vid, g in st["games"].items():
        e = {k: dict(v) for k, v in g.items() if k in FIELDS and isinstance(v, dict)}
        if "art" in e:
            e["art"]["present"] = bool(MASTER_RE.match(str(e["art"].get("master") or "")) and os.path.isfile(_abs(e["art"]["master"])))
            e["art"]["key"] = (e["art"].get("sha256") or "")[:16] + "-" + _crop_key(e["art"].get("crop"))
        out[vid] = e
    return {"schema": SCHEMA, "games": out}

def export_zip(dest: str) -> dict:
    """Backup: overrides.json plus every referenced master (original bytes). Paths inside the archive equal the store's."""
    st = load(); n = 0
    with zipfile.ZipFile(dest, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("overrides.json", json.dumps(st, ensure_ascii=False, indent=1))
        for vid, g in st["games"].items():
            a = g.get("art") or {}
            if MASTER_RE.match(str(a.get("master") or "")) and os.path.isfile(_abs(a["master"])):
                z.write(_abs(a["master"]), a["master"], compress_type=zipfile.ZIP_STORED); n += 1
        z.writestr("README.txt", "Classic Cart Studio customized content (schema ccs-overrides/1).\r\nThe original box art, descriptions and overviews are not included: only your own changes and the images you imported.\r\n")
    return {"games": len(st["games"]), "images": n}

# ------------------------------------------------------------------------------------------------ HTTP routes (nes_companion dispatch)
def _desktop(h) -> bool:
    b = h._bearer(); return bool(b and b.get("desktop"))

def _send_file(h, path: str, ctype: str, cache: str = "no-store", name: str | None = None):
    size = os.path.getsize(path)
    h.send_response(200); h.send_header("Content-Type", ctype); h.send_header("Content-Length", str(size)); h.send_header("Cache-Control", cache)
    h.send_header("X-Content-Type-Options", "nosniff")
    if name: h.send_header("Content-Disposition", f'attachment; filename="{name}"')
    h._cors(); h.end_headers()
    with open(path, "rb") as f:
        while True:
            b = f.read(1 << 20)
            if not b: break
            h.wfile.write(b)

def handle_get(h, path: str, qs: str):
    q = urllib.parse.parse_qs(qs or "")
    if path.startswith("/api/"):
        if not _desktop(h): return h._json(403, {"ok": False, "error": "this works only in the Windows app"})
        if path == "/api/overrides": return h._json(200, {"ok": True, **public()})
        if path == "/api/overrides/export":
            tmpdir = os.path.join(ROOT, "tmp"); os.makedirs(tmpdir, exist_ok=True)
            fd, tmp = tempfile.mkstemp(prefix="export-", suffix=".zip", dir=tmpdir); os.close(fd)
            try:
                export_zip(tmp); _send_file(h, tmp, "application/zip", "no-store", f"classic-cart-studio-custom-content-{time.strftime('%Y%m%d')}.zip")
            finally:
                try: os.remove(tmp)
                except OSError: pass
            return
        return h._json(404, {"ok": False, "error": "unknown endpoint"})
    # images: served like personal art (loopback host only; an <img> cannot carry the desktop token)
    m = re.match(r"^/overrides/(display|work|master)/([A-Za-z0-9_-]{1,64})(?:\.jpg)?$", path)
    if not m: h.send_response(404); h.send_header("Content-Length", "0"); h.end_headers(); return
    kind, vid = m.group(1), m.group(2)
    try:
        if kind == "display": p = display_path(vid); ct = "image/jpeg"
        elif kind == "work": p = work_path(vid, re.sub(r"[^0-9a-f]", "", (q.get("m") or [""])[0])[:16]); ct = "image/jpeg"
        else:
            p = art_master_path(vid); ct = CTYPES.get((game(vid).get("art") or {}).get("format"), "application/octet-stream")
    except Exception: p = None
    if not p or not os.path.isfile(p): h.send_response(404); h.send_header("Content-Length", "0"); h.end_headers(); return
    return _send_file(h, p, ct, "max-age=31536000, immutable" if (q.get("v") or q.get("m")) else "no-cache")

def _stream_body(h) -> str:
    """Stream the request body to a temporary file (no size limit, never all in memory)."""
    try: n = int(h.headers.get("Content-Length", "") or -1)
    except ValueError: n = -1
    if n <= 0: raise ValueError("the image arrived empty")
    tmpdir = os.path.join(ROOT, "tmp"); os.makedirs(tmpdir, exist_ok=True)
    fd, tmp = tempfile.mkstemp(prefix="upload-", suffix=".part", dir=tmpdir); left = n
    h._body_read = True
    try:
        with os.fdopen(fd, "wb") as f:
            while left > 0:
                b = h.rfile.read(min(left, 1 << 20))
                if not b: raise ValueError("the upload was interrupted")
                f.write(b); left -= len(b)
    except Exception:
        try: os.remove(tmp)
        except OSError: pass
        raise
    return tmp

def handle_post(h, path: str, cfg: dict, pick_file=None):
    if not _desktop(h): return h._json(403, {"ok": False, "error": "this works only in the Windows app"})
    q = urllib.parse.parse_qs(h.path.split("?", 1)[1] if "?" in h.path else "")
    ctype = (h.headers.get("Content-Type") or "").split(";")[0].strip().lower()
    try:
        if path == "/api/overrides/art" and ctype and ctype != "application/json":
            # the page's <input type=file>: raw image bytes, ?id=<vid>&name=<file name>[&stage=1]
            vid = _vid((q.get("id") or [""])[0]); name = (q.get("name") or ["image"])[0]
            tmp = _stream_body(h)
            try:
                rec = stage_art(vid, tmp, name)
            finally:
                try: os.remove(tmp)
                except OSError: pass
            if (q.get("stage") or ["0"])[0] in ("1", "true"): return h._json(200, {"ok": True, "art": rec, "staged": True})
            return h._json(200, {"ok": True, "art": commit_art(vid, rec["master"], None), "game": game(vid)})
        body = h._body()
        if not isinstance(body, dict): return h._json(400, {"ok": False, "error": "body must be an object"})
        vid = _vid(body.get("id"))
        if path == "/api/overrides/text":
            g = set_text(vid, str(body.get("field") or ""), body.get("text")); return h._json(200, {"ok": True, "game": g})
        if path == "/api/overrides/reset":
            g = reset(vid, str(body.get("field") or "")); return h._json(200, {"ok": True, "game": g})
        if path == "/api/overrides/crop":
            a = set_crop(vid, body.get("crop")); return h._json(200, {"ok": True, "art": a, "game": game(vid)})
        if path == "/api/overrides/art":
            op = str(body.get("op") or "pick"); stage = bool(body.get("stage"))
            if op == "commit": return h._json(200, {"ok": True, "art": commit_art(vid, str(body.get("master") or ""), body.get("crop")), "game": game(vid)})
            if op in ("pick", "path"):
                if body.get("path"):
                    if not cfg.get("rehearsal_tree"): return h._json(403, {"ok": False, "error": "importing from a path is available only in rehearsal configurations"})
                    chosen = str(body.get("path"))
                else:
                    if not pick_file: return h._json(400, {"ok": False, "error": "no file picker on this computer"})
                    chosen = pick_file("Choose box art for this game (PNG, JPEG, WebP, GIF or BMP)", [("Images", "*.png *.jpg *.jpeg *.webp *.gif *.bmp"), ("All files", "*.*")])
                    if not chosen: return h._json(200, {"ok": False, "canceled": True})
                rec = stage_art(vid, chosen, os.path.basename(chosen))
                if stage: return h._json(200, {"ok": True, "art": rec, "staged": True})
                return h._json(200, {"ok": True, "art": commit_art(vid, rec["master"], None), "game": game(vid)})
            return h._json(400, {"ok": False, "error": "unknown artwork operation"})
        return h._json(404, {"ok": False, "error": "unknown endpoint"})
    except ValueError as e: return h._json(400, {"ok": False, "error": str(e)})
    except Exception as e: return h._json(500, {"ok": False, "error": f"customization failed: {e}"})
