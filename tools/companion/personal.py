"""Personal games and the local console-management library — companion-side, PC-only, read-only towards Hakchi.

Personal entries (Add Your Own Game): the user's file is treated as untrusted input — ZIP members checked for traversal, links,
encryption, duplicate names and compression ratio before reading, each copied in 1 MB pieces while it is identified (no size or count
limit; memory stays bounded), refused when it holds more or less data than declared or the drive lacks the space, only ROM members
considered, nothing is ever executed. The original file is never modified;
a managed copy is kept under reports/personal/files so the collection keeps working if the original moves. Identity: SHA-256 of the file,
SHA-256 of the headerless content (what the Studio's inventory keys on); an exact content match means "this is a known version" and the
known entry is used instead of creating a personal one. Detected metadata (iNES header fields, compatibility rule) is stored apart from
the user's own metadata (title, version label, description, year, publisher, artwork).

Local library (Load Existing Collection → Games already on this computer): reads config\\config.json, games_originals\\ and games\\ of the
configured Hakchi tree, hashes every custom game's ROM, matches by content hash, reports checked state, missing files and ownership.
Nothing is written to the tree."""
from __future__ import annotations
import os, sys, json, hashlib, zlib, zipfile, time, shutil, re
HERE = os.path.dirname(os.path.abspath(__file__)); PIPE = os.path.normpath(os.path.join(HERE, "..", "rom-pipeline")); sys.path.insert(0, PIPE)
from common import PROJECT, REPORTS, DB_PATH, detect_format, SITE_ROOT, DATA_ROOT, site_path, data_path
from catalog import compat
import hakchi_inspect as hi

PERSONAL_DIR = os.environ.get("NES_PERSONAL_DIR") or data_path("personal"); FILES_DIR = os.path.join(PERSONAL_DIR, "files"); ART_DIR = os.path.join(PERSONAL_DIR, "art"); INDEX = os.path.join(PERSONAL_DIR, "index.json")
LIB_FILES_DIR = os.path.join(PERSONAL_DIR, "library"); LIB_STORE = os.path.join(PERSONAL_DIR, "library-store.json")   # managed copies of files whose bytes are known versions
ROM_EXT = (".nes", ".unh", ".unif", ".bin", ".rom", ".fds"); INCOMING = os.path.join(PERSONAL_DIR, "incoming")   # files being copied in
MAX_RATIO = 400                  # per ZIP member: refuses decompression bombs (the rule .ccs packages use); not a size limit
CHUNK = 1 << 20; SPACE_MARGIN = 64 * 1024 * 1024
NES_MAGIC = b"NES\x1a"

def load_index() -> dict:
    return json.load(open(INDEX, encoding="utf-8")) if os.path.exists(INDEX) else {"version": 1, "entries": {}}

def save_index(idx: dict):
    os.makedirs(PERSONAL_DIR, exist_ok=True); tmp = INDEX + ".tmp"; json.dump(idx, open(tmp, "w", encoding="utf-8"), indent=1, ensure_ascii=False); os.replace(tmp, INDEX)

def sha256(b: bytes) -> str: return hashlib.sha256(b).hexdigest()

def parse_header(b: bytes) -> dict:
    """iNES / NES 2.0 header fields, only when the magic is present. Never guessed."""
    if len(b) < 16 or b[:4] != NES_MAGIC: return {}
    f6, f7, f8, f9 = b[6], b[7], b[8], b[9]; nes2 = (f7 & 0x0C) == 0x08
    mapper = (f6 >> 4) | (f7 & 0xF0); sub = 0; prg = b[4] * 16384; chr_ = b[5] * 8192
    if nes2:
        mapper |= (f8 & 0x0F) << 8; sub = f8 >> 4
        if (f9 & 0x0F) != 0x0F: prg = ((f9 & 0x0F) << 8 | b[4]) * 16384
        if (f9 >> 4) != 0x0F: chr_ = ((f9 >> 4) << 8 | b[5]) * 8192
    return {"format": "NES 2.0" if nes2 else "iNES", "mapper": mapper, "submapper": sub, "prg": prg, "chr": chr_, "mirroring": "four-screen" if f6 & 0x08 else ("vertical" if f6 & 0x01 else "horizontal"), "battery": bool(f6 & 0x02), "trainer": bool(f6 & 0x04), "header_hex": b[:16].hex()}

def _rm(p: str):
    try: os.remove(p)
    except OSError: pass

def _zip_roms(z: zipfile.ZipFile, path: str) -> list:
    """The ROM members of a ZIP, checked before anything is read: safe names, no links, no encryption, no duplicate names, a plausible
    compression ratio. An unsafe archive raises ValueError and nothing from it is used. There is no size or member-count limit."""
    out, seen = [], set()
    for m in z.infolist():
        name = m.filename.replace("\\", "/")
        if m.is_dir() or os.path.splitext(name)[1].lower() not in ROM_EXT: continue
        if name.startswith("/") or ".." in name.split("/") or (len(name) > 1 and name[1] == ":"): raise ValueError(f"The ZIP file contains an unsafe path ({name[:80]}), so it was not used.")
        if (m.external_attr >> 16) & 0o170000 == 0o120000: raise ValueError(f"The ZIP file contains a link ({name[:80]}), so it was not used.")
        if m.flag_bits & 0x1: raise ValueError(f"{os.path.basename(path)} is password-protected, so it was not opened.")
        if name.lower() in seen: raise ValueError(f"The ZIP file lists {name[:80]} more than once, so it was not used.")
        if m.file_size and (m.compress_size == 0 or m.file_size / m.compress_size > MAX_RATIO): raise ValueError(f"{name[:80]} has an unsafe compression ratio, so the ZIP file was not opened.")
        seen.add(name.lower()); out.append(m)
    return out

def _candidates(path: str, single: bool = False):
    """Yield (open function, declared size, display file name, note) for every ROM in a file: the file itself, or each ROM member of a ZIP.
    single: the file must hold exactly one ROM (Add Your Own Game, Locate). Untrusted input rules apply; nothing is read here."""
    if not os.path.isfile(path): raise ValueError("The file was not found. Choose it again.")
    if os.path.islink(path): raise ValueError("Links to files are not accepted. Choose the ROM file itself.")
    ext = os.path.splitext(path)[1].lower()
    if ext == ".zip":
        try: z = zipfile.ZipFile(path)
        except (zipfile.BadZipFile, OSError, ValueError): raise ValueError(f"{os.path.basename(path)} is not a readable ZIP file (it may be damaged).")
        with z:
            roms = _zip_roms(z, path)
            if not roms: raise ValueError("The ZIP file has no ROM file (.nes, .unh, .bin or .fds) in it.")
            if single and len(roms) > 1: raise ValueError("The ZIP file holds more than one ROM file. Use Add Games from Files to add them all, or extract the one you want.")
            for m in roms: yield (lambda m=m: z.open(m)), m.file_size, os.path.basename(m.filename.replace("\\", "/")), f"from archive {os.path.basename(path)}"
        return
    if ext in (".7z", ".rar"): raise ValueError("7z and RAR files are not supported. Extract the ROM file first.")
    if ext not in ROM_EXT: raise ValueError(f"This is an unsupported file type ({ext or 'no extension'}). Choose an NES ROM file or a ZIP file.")
    yield (lambda: open(path, "rb")), os.path.getsize(path), os.path.basename(path), ""

def spool(open_fn, declared: int, fname: str, cancel=lambda: False) -> tuple[str, dict]:
    """Copy one ROM into a temporary file under the personal folder while identifying it, 1 MB at a time (memory stays bounded whatever
    its size). Refuses more or less data than declared (a decompression bomb stops at its declared size) and a drive without the space.
    Returns (temporary path, identity as identify_bytes gives it); the caller adopts or removes the file."""
    from ccs_package import IdentityHasher
    os.makedirs(INCOMING, exist_ok=True)
    try: free = shutil.disk_usage(INCOMING).free
    except OSError: free = None
    if free is not None and declared + SPACE_MARGIN > free: raise ValueError(f"There isn't enough free space on this computer to add {fname}. Free up some space, then try again.")
    tmp = os.path.join(INCOMING, f"{os.getpid()}-{threading.get_ident()}-{time.time_ns()}.part"); h = IdentityHasher(); n = 0
    try:
        with open_fn() as src, open(tmp, "wb") as out:
            while True:
                if cancel(): raise ValueError("Canceled.")
                b = src.read(min(CHUNK, declared - n + 1))
                if not b: break
                n += len(b)
                if n > declared: raise ValueError(f"{fname} holds more data than its archive declares, so it was not used.")
                h.update(b); out.write(b)
        if n != declared: raise ValueError(f"{fname} ended early (the file is damaged or changed while it was read).")
        got = h.finish()
        if got["size"] - (16 if got["magic"] else 0) < MIN_CONTENT: raise ValueError("The file is too small to be an NES ROM.")
    except (zipfile.BadZipFile, zlib.error, EOFError, NotImplementedError):
        _rm(tmp); raise ValueError(f"{fname} is damaged or uses an unsupported compression, so it was not used.")
    except BaseException:
        _rm(tmp); raise
    hdr = parse_header(got["head"]) if got["magic"] else {}
    return tmp, {"format": "NES2.0" if hdr.get("format") == "NES 2.0" else ("iNES" if hdr else "headerless"), "file_sha256": got["sha256"], "file_size": got["size"],
                 "content_sha256": got["content_sha256"], "content_size": got["content_size"], "content_crc32": f"{got['content_crc32']:08X}", "header": hdr}

def _file_sha(p: str) -> str:
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(CHUNK), b""): h.update(b)
    return h.hexdigest()

def _place(tmp: str, managed: str, sha: str, replace: bool = False):
    """Move a spooled file to its managed path after checking it again; an identical existing copy is kept (deduplicated)."""
    if not replace and os.path.exists(managed) and _file_sha(managed) == sha: _rm(tmp); return
    if _file_sha(tmp) != sha: _rm(tmp); raise ValueError("The copy saved on this computer did not match the original, so it was not kept. Try again.")
    os.replace(tmp, managed)

def _spool_one(path: str) -> tuple[str, dict, str, str]:
    g = _candidates(path, single=True)
    try: open_fn, declared, fname, note = next(g); tmp, ident = spool(open_fn, declared, fname)
    finally: g.close()
    return tmp, ident, fname, note

MIN_CONTENT = 8192   # no NES program is smaller than one 8 KiB bank; anything smaller is not a game file
def version_known(version_id: str) -> bool: return version_id in _lib()["by_id"]
def identify_bytes(data: bytes) -> dict:
    if len(data) - (16 if data[:4] == NES_MAGIC else 0) < MIN_CONTENT: raise ValueError("The file is too small to be an NES ROM.")
    fmt = detect_format(data)
    if fmt == "unknown":
        fmt = "iNES" if data[:4] == NES_MAGIC else "headerless"
    hdr = parse_header(data) if data[:4] == NES_MAGIC else {}
    content = data[16:] if hdr else data
    if hdr and hdr.get("trainer"): content = data[16 + 512:]
    return {"format": "NES2.0" if hdr.get("format") == "NES 2.0" else ("iNES" if hdr else "headerless"), "file_sha256": sha256(data), "file_size": len(data), "content_sha256": sha256(content), "content_size": len(content),
            "content_crc32": f"{zlib.crc32(content) & 0xFFFFFFFF:08X}", "header": hdr}

def match_known(content_sha256: str) -> dict | None:
    """Exact content match in the Studio's inventory → the known version (never a title-string guess)."""
    lib = _lib()
    v = lib["by_sha"].get(content_sha256)
    return {"version_id": v["id"], "name": v["name"], "title": lib["titles"][v["title_id"]]["title"], "title_id": v["title_id"], "curated": v["curated"]} if v else None

_LIB = None
def _lib():
    global _LIB
    if _LIB is None:
        p = site_path("library.json")
        lib = json.load(open(p, encoding="utf-8")) if os.path.exists(p) else {"titles": [], "versions": []}
        pays = lib.get("payloads") or {}
        for v in lib["versions"]: v["content_sha256"] = (pays.get(v["sha256"]) or {}).get("content_sha256") or v["sha256"]   # headered payloads: match by the bytes after the header too
        _LIB = {"titles": {t["id"]: t for t in lib["titles"]}, "by_sha": {}, "by_id": {v["id"]: v for v in lib["versions"]}, "by_norm": {}}
        for v in lib["versions"]: _LIB["by_sha"].setdefault(v["content_sha256"], v); _LIB["by_sha"].setdefault(v["sha256"], v)
        for t in lib["titles"]: _LIB["by_norm"].setdefault(re.sub(r"[^a-z0-9]+", " ", t["title"].lower()).strip(), []).append(t["id"])
    return _LIB

def probable_relatives(name: str) -> list:
    """Titles whose normalized name equals the file's name stem — reported separately, never used as an exact match."""
    stem = re.sub(r"\s*\(.*?\)|\s*\[.*?\]", "", os.path.splitext(name)[0]); key = re.sub(r"[^a-z0-9]+", " ", stem.lower()).strip()
    lib = _lib(); return [{"title_id": tid, "title": lib["titles"][tid]["title"], "curated": lib["titles"][tid]["curated"]} for tid in lib["by_norm"].get(key, [])][:5]

def ingest(path: str, origin: str = "file", local_code: str = "", local_name: str = "") -> dict:
    """Identify a user file; return either {'known': ...} (exact content match) or the new/updated personal entry (managed copy made)."""
    tmp, ident, fname, note = _spool_one(path)
    return adopt(tmp, ident, fname, path, note, origin, local_code, local_name)

def adopt(tmp: str, ident: dict, fname: str, src: str, note: str, origin: str = "file", local_code: str = "", local_name: str = "") -> dict:
    """Keep a spooled ROM: an exact content match becomes the known version's library copy, anything else a personal entry. The temporary
    file is always consumed (moved into place or removed)."""
    try:
        known = match_known(ident["content_sha256"])
        if known: return {"known": known, "identity": ident, "file_name": fname, "note": note, "library_copy": keep_known_copy(known["version_id"], tmp, ident, fname, src, note), "content_sha256": ident["content_sha256"]}
        return _personal_entry(tmp, ident, fname, src, note, origin, local_code, local_name)
    finally: _rm(tmp)

def _personal_entry(tmp: str, ident: dict, fname: str, src: str, note: str, origin: str, local_code: str, local_name: str) -> dict:
    idx = load_index(); eid = "p_" + ident["content_sha256"][:16]; os.makedirs(FILES_DIR, exist_ok=True)
    ext = ".nes" if ident["format"] != "headerless" else ".unh"; managed = os.path.join(FILES_DIR, eid + ext)
    _place(tmp, managed, ident["file_sha256"])
    hdr = ident["header"]; det = {"title": re.sub(r"\s*\(.*?\)|\s*\[.*?\]", "", os.path.splitext(fname)[0]).strip() or fname, **{k: hdr.get(k) for k in ("mapper", "submapper", "prg", "chr", "mirroring", "battery", "header_hex")}}
    if hdr:
        cp = compat({"title": det["title"], "mapper": hdr["mapper"], "four_screen": hdr["mirroring"] == "four-screen", "licensed": "unknown", "cart_system": "", "mesen_system": "", "mesen_mapper": None, "cart_board": ""})
        det.update(emulator=cp["recommended_emulator"], core=cp["core"], launch_command=cp["launch_command"], compat_notes=cp["compat_notes"], compat_confidence=cp["compat_confidence"])
    else: det.update(emulator="unknown", core="", launch_command="", compat_notes="no header: the ROM's mapper is unknown, so it cannot be prepared for the console", compat_confidence="none")
    old = idx["entries"].get(eid, {})
    e = {"id": eid, "origin": old.get("origin") or origin, "added": old.get("added") or time.strftime("%Y-%m-%dT%H:%M:%S"), "file_name": fname, "source_path": src, "note": note, "format": ident["format"], "file_sha256": ident["file_sha256"], "file_size": ident["file_size"],
         "content_sha256": ident["content_sha256"], "content_size": ident["content_size"], "content_crc32": ident["content_crc32"], "managed_copy": managed, "detected": det, "user": old.get("user") or {"title": local_name or det["title"], "version_label": "", "description": "", "year": "", "publisher": "", "art": ""},
         "local_code": local_code or old.get("local_code", ""), "relatives": probable_relatives(fname), "identity": "personal"}
    idx["entries"][eid] = e; save_index(idx); return {"entry": public(e), "content_sha256": ident["content_sha256"]}

def load_store() -> dict:
    return json.load(open(LIB_STORE, encoding="utf-8")) if os.path.exists(LIB_STORE) else {}

def save_store(st: dict):
    os.makedirs(PERSONAL_DIR, exist_ok=True); tmp = LIB_STORE + ".tmp"; json.dump(st, open(tmp, "w", encoding="utf-8"), indent=1, ensure_ascii=False); os.replace(tmp, LIB_STORE)

def keep_known_copy(version_id: str, tmp: str, ident: dict, fname: str, src: str, note: str) -> dict:
    """A known version imported by the user: keep ONE managed copy per version (deduplicated by content hash) so the collection can be prepared
    without the development payload store. The original file is never modified. Returns the store record."""
    st = load_store(); rec = st.get(version_id)
    if rec and os.path.exists(rec["path"]) and rec.get("content_sha256") == ident["content_sha256"]: rec["seen"] = rec.get("seen", 1) + 1; st[version_id] = rec; save_store(st); return dict(rec, reused=True)
    os.makedirs(LIB_FILES_DIR, exist_ok=True); ext = ".nes" if ident["format"] != "headerless" else ".unh"; managed = os.path.join(LIB_FILES_DIR, version_id + ext)
    _place(tmp, managed, ident["file_sha256"], replace=True)
    rec = {"version_id": version_id, "path": managed, "format": ident["format"], "file_sha256": ident["file_sha256"], "content_sha256": ident["content_sha256"], "file_size": ident["file_size"], "source_name": fname, "source_archive": os.path.basename(src) if note else "", "added": time.strftime("%Y-%m-%dT%H:%M:%S"), "seen": 1}
    st[version_id] = rec; save_store(st); return dict(rec, reused=False)

def public(e: dict) -> dict:
    """What the page sees (no absolute paths beyond the managed copy's existence)."""
    out = {k: v for k, v in e.items() if k not in ("managed_copy", "source_path")}
    out["file_present"] = bool(e.get("managed_copy") and os.path.exists(e["managed_copy"])); out["source_folder"] = os.path.basename(os.path.dirname(e.get("source_path") or "")); return out

def relink(entry_id: str, path: str) -> dict:
    idx = load_index(); e = idx["entries"].get(entry_id)
    if not e: raise ValueError("That game is no longer among your own games.")
    tmp, ident, fname, note = _spool_one(path)
    try:
        if ident["content_sha256"] != e["content_sha256"]: raise ValueError("This file is a different ROM than the one this game was added with, so nothing was changed.")
        os.makedirs(FILES_DIR, exist_ok=True); managed = e.get("managed_copy") or os.path.join(FILES_DIR, entry_id + (".nes" if ident["format"] != "headerless" else ".unh"))
        _place(tmp, managed, ident["file_sha256"], replace=True)
    finally: _rm(tmp)
    e["managed_copy"] = managed; e["source_path"] = path; e["file_name"] = fname; save_index(idx); return {"entry": public(e)}

def update_user(entry_id: str, user: dict) -> dict:
    idx = load_index(); e = idx["entries"].get(entry_id)
    if not e: raise ValueError("That game is no longer among your own games.")
    allowed = ("title", "version_label", "description", "year", "publisher")
    for k in allowed:
        if k in user: e["user"][k] = str(user[k])[:200]
    save_index(idx); return {"entry": public(e)}

def set_art(entry_id: str, path: str) -> dict:
    from PIL import Image
    idx = load_index(); e = idx["entries"].get(entry_id)
    if not e: raise ValueError("That game is no longer among your own games.")
    if (Image.MAX_IMAGE_PIXELS or 0) < 200_000_000: Image.MAX_IMAGE_PIXELS = 200_000_000   # no file-size limit (N3.1); only the decompression-bomb ceiling
    os.makedirs(ART_DIR, exist_ok=True); im = Image.open(path); im.verify(); im = Image.open(path)
    if im.width * im.height > 200_000_000: raise ValueError("The image is too large (more than 200 million pixels).")
    im = im.convert("RGB"); im.thumbnail((480, 640)); out = os.path.join(ART_DIR, entry_id + ".jpg"); im.save(out, "JPEG", quality=88)
    e["user"]["art"] = "personal/art/" + entry_id + ".jpg"; save_index(idx); return {"entry": public(e)}

def remove(entry_id: str, delete_copy: bool = False) -> dict:
    idx = load_index(); e = idx["entries"].pop(entry_id, None)
    if not e: raise ValueError("That game is no longer among your own games.")
    if delete_copy and e.get("managed_copy") and os.path.exists(e["managed_copy"]): os.remove(e["managed_copy"])
    save_index(idx); return {"removed": entry_id, "copy_deleted": bool(delete_copy)}

# ------------------------------------------------------------------ local console-management library (read-only)
def read_local_library(root: str) -> dict:
    """Everything Hakchi would list in the NES collection: factory games (checked or not), custom games with content identity, match and file state."""
    eff = hi.effective_collection(root); cust = hi.hakchi_customs(root); orig = hi.hakchi_originals(root)
    lib = _lib(); entries = []
    for code, v in cust.items():
        d = os.path.join(root, "games", code); roms = [f for f in os.listdir(d) if f.lower().endswith((".nes", ".unh", ".fds"))]
        rec = {"code": code, "name": v["name"], "checked": code in eff["customs_enabled"], "managed": v["managed"], "file_present": bool(roms), "exec": v["exec"], "emulator": "RetroArch" if "/bin/km_" in v["exec"] or "retroarch" in v["exec"].lower() else "kachikachi"}
        if roms:
            data = open(os.path.join(d, roms[0]), "rb").read()
            try: ident = identify_bytes(data)
            except ValueError as e: rec.update(file_name=roms[0], format="unknown", file_sha256=sha256(data), content_sha256=sha256(data), content_size=len(data), match=None, unreadable=str(e)); entries.append(rec); continue
            rec.update(file_name=roms[0], format=ident["format"], file_sha256=ident["file_sha256"], content_sha256=ident["content_sha256"], content_size=ident["content_size"], header=ident["header"])
            k = lib["by_sha"].get(ident["content_sha256"]); rec["match"] = {"version_id": k["id"], "name": k["name"], "title": lib["titles"][k["title_id"]]["title"], "title_id": k["title_id"], "curated": k["curated"]} if k else None
            mk = os.path.join(d, "nes-classic-catalog.json")
            if os.path.exists(mk):
                try: rec["marker"] = json.load(open(mk, encoding="utf-8"))
                except Exception: rec["marker"] = None
        else: rec["match"] = None
        entries.append(rec)
    return {"root": root, "config_exists": eff["config_exists"], "config_sha256": eff["config_sha256"], "factory": [{"code": c, "name": orig[c]["name"], "checked": c in eff["originals_enabled"]} for c in sorted(orig)],
            "factory_checked": eff["originals_enabled"], "customs": entries, "customs_checked": eff["customs_enabled"], "summary": {"factory_total": len(orig), "factory_checked": len(eff["originals_enabled"]), "custom_total": len(entries), "custom_checked": len(eff["customs_enabled"]),
            "matched": sum(1 for e in entries if e.get("match")), "unmatched": sum(1 for e in entries if not e.get("match") and e["file_present"]), "missing_files": sum(1 for e in entries if not e["file_present"]), "managed_by_studio": sum(1 for e in entries if e["managed"])},
            "baseline": {"captured": time.strftime("%Y-%m-%dT%H:%M:%S"), "originals_enabled": eff["originals_enabled"], "customs_enabled": eff["customs_enabled"], "config_sha256": eff["config_sha256"]}}

def adopt_unmatched(root: str, library: dict) -> list:
    """Create personal entries (managed copies) for unmatched custom games whose ROM file exists; returns the public entries."""
    out = []
    for e in library["customs"]:
        if e.get("match") or not e["file_present"]: continue
        d = os.path.join(root, "games", e["code"]); r = ingest(os.path.join(d, e["file_name"]), origin="local-library", local_code=e["code"], local_name=e["name"])
        if "entry" in r: out.append(r["entry"])
    return out

def pick_file(title: str, patterns) -> str:
    """Native Windows file dialog on the user's desktop (the browser cannot hand the companion a path)."""
    import tkinter as tk
    from tkinter import filedialog
    root = tk.Tk(); root.withdraw(); root.attributes("-topmost", True)
    try: return filedialog.askopenfilename(title=title, filetypes=patterns) or ""
    finally: root.destroy()

# ------------------------------------------------------------------ bulk import (Add Games from Files): asynchronous, cancellable, deduplicated
import threading
JOBS: dict = {}
def _ingest_bytes(data: bytes, fname: str, src: str, note: str, origin: str = "file") -> dict:
    """ROM bytes already in memory (kept for callers that have them): the same path as a file, through a temporary copy."""
    import io
    tmp, ident = spool(lambda: io.BytesIO(data), len(data), fname)
    return adopt(tmp, ident, fname, src, note, origin)

def collect_paths(items: list[str]) -> list[str]:
    out = []
    for it in items:
        if os.path.isdir(it):
            for d, dirs, files in os.walk(it):
                dirs[:] = [x for x in dirs if not x.startswith(".")]
                for f in files:
                    if os.path.splitext(f)[1].lower() in ROM_EXT + (".zip",): out.append(os.path.join(d, f))
        elif os.path.isfile(it): out.append(it)
    return sorted(set(out))

def start_import(items: list[str], origin: str = "file") -> str:
    paths = collect_paths(items); jid = "imp_" + hashlib.sha256((str(time.time()) + "|".join(paths[:5])).encode()).hexdigest()[:12]
    job = {"id": jid, "started": time.strftime("%Y-%m-%dT%H:%M:%S"), "total": len(paths), "done": 0, "canceled": False, "finished": False, "current": "", "known": 0, "known_new_copies": 0, "personal": 0, "personal_new": 0, "duplicates": 0, "errors": [], "items": [], "seen_contents": set()}
    JOBS[jid] = job
    def run():
        for p in paths:
            if job["canceled"]: break
            job["current"] = os.path.basename(p)
            try:
                for open_fn, declared, fname, note in _candidates(p):   # every ROM of a ZIP, one at a time (an unsafe ZIP is refused as a whole)
                    if job["canceled"]: break
                    try:
                        tmp, ident = spool(open_fn, declared, fname, cancel=lambda: job["canceled"])
                        if ident["content_sha256"] in job["seen_contents"]: _rm(tmp); job["duplicates"] += 1; job["items"].append({"file": os.path.basename(p), "member": fname, "result": "duplicate (same bytes already imported in this run)"}); continue
                        job["seen_contents"].add(ident["content_sha256"])
                        r = adopt(tmp, ident, fname, p, note, origin)
                    except Exception as e:   # one damaged or unsuitable member does not stop the rest of its ZIP
                        if job["canceled"]: break
                        job["errors"].append({"file": os.path.basename(p), "member": fname, "error": str(e)[:160]}); job["items"].append({"file": os.path.basename(p), "member": fname, "result": "error: " + str(e)[:120]}); continue
                    if "known" in r:
                        job["known"] += 1; job["known_new_copies"] += 0 if r["library_copy"].get("reused") else 1
                        job["items"].append({"file": os.path.basename(p), "member": fname, "result": "known: " + r["known"]["name"], "version_id": r["known"]["version_id"], "title": r["known"]["title"], "curated": r["known"]["curated"], "reused": r["library_copy"].get("reused")})
                    else:
                        job["personal"] += 1; job["personal_new"] += 0 if r["entry"].get("origin") and r["entry"].get("added") and r["entry"]["added"] < job["started"] else 1
                        job["items"].append({"file": os.path.basename(p), "member": fname, "result": "personal: " + (r["entry"]["user"].get("title") or fname), "personal_id": r["entry"]["id"]})
            except Exception as e:
                job["errors"].append({"file": os.path.basename(p), "error": str(e)[:160]}); job["items"].append({"file": os.path.basename(p), "member": "", "result": "error: " + str(e)[:120]})
            job["done"] += 1
        job["finished"] = True; job["current"] = ""
    threading.Thread(target=run, daemon=True).start(); return jid

def job_status(jid: str) -> dict | None:
    j = JOBS.get(jid)
    if not j: return None
    out = {k: v for k, v in j.items() if k not in ("seen_contents", "items")}; out["items"] = j["items"][-400:]; out["unique_contents"] = len(j["seen_contents"]); return out

def cancel_job(jid: str) -> bool:
    j = JOBS.get(jid)
    if not j: return False
    j["canceled"] = True; return True

def pick_files(title: str, patterns) -> list[str]:
    import tkinter as tk
    from tkinter import filedialog
    root = tk.Tk(); root.withdraw(); root.attributes("-topmost", True)
    try: return list(filedialog.askopenfilenames(title=title, filetypes=patterns) or [])
    finally: root.destroy()

def pick_folder(title: str) -> str:
    import tkinter as tk
    from tkinter import filedialog
    root = tk.Tk(); root.withdraw(); root.attributes("-topmost", True)
    try: return filedialog.askdirectory(title=title) or ""
    finally: root.destroy()

def locate_version(version_id: str, path: str) -> dict:
    """Locate Game File for a known version that has no file on this computer: accept the chosen file only when its content hash IS that
    version (exact identity; never a substitute), then keep a managed copy."""
    lib = _lib(); v = lib["by_id"].get(version_id)
    if not v: raise ValueError("This game version is not in the Game Library.")
    for open_fn, declared, fname, note in _candidates(path):
        try: tmp, ident = spool(open_fn, declared, fname)
        except ValueError as e:
            if "free space" in str(e): raise
            continue   # a member that is not a ROM; the others are still checked
        try:
            if ident["content_sha256"] == v.get("content_sha256", v["sha256"]) or ident["file_sha256"] == v["sha256"]:
                return {"version_id": version_id, "name": v.get("name"), "library_copy": keep_known_copy(version_id, tmp, ident, fname, path, note)}
        finally: _rm(tmp)
    raise ValueError(f"The chosen file is not this version ({v.get('name', version_id)}), so it was not used.")
