"""Classic Cart Studio for Windows: the folders and files behind the app's own Open, Save and folder dialog. Copyright (C) 2026 Rance9.
GPL-3.0-or-later.

The page shows the dialog (tools/rom-pipeline/studio/files.js); the person chooses there, and the page sends the chosen path(s) to the
operation that asked (adding games, locating a game file, artwork, saving a package or game saves). This module only answers what the
dialog shows and does what the person asked inside it:
  places()                 Desktop, Documents, Downloads and Pictures, then This PC and its drives (by letter and kind only)
  list_folder(path)        a folder's folders and files (name, folder or file, size, date changed, kind), hidden and system items left out
  make_folder(parent, n)   New Folder (a valid name, nothing of that name there)
  stat(path)               whether a path is there, a folder or a file, its size and date
  grant(paths, mode)       the files the person chose to open (read) or the file they chose to save to (write), for a few minutes
  read_path(token)         the chosen file to read (the page reads a file it needs itself: a collection file, a picture)
  write_to(handler, token) the page's bytes into the chosen file (a new file next to it first, then put in its place)
Only the desktop principal (Classic Cart Studio for Windows' own window) reaches these (ccs_backend.py checks it first). Long paths are
read with Windows' \\\\?\\ form. Nothing here is a general file service: no deletion, renaming, moving or listing outside a folder asked for."""
from __future__ import annotations
import ctypes, os, re, secrets, stat as st, string, sys, threading, time
from datetime import datetime

# a test (a rehearsal configuration only) may give the places instead of this PC's own: [{"name", "path", "drive"?}]
PLACES_OVERRIDE: list | None = None
GRANT_SECONDS = 600
_GRANTS: dict = {}; _LOCK = threading.Lock()
RESERVED = {"CON", "PRN", "AUX", "NUL", *(f"COM{i}" for i in range(1, 10)), *(f"LPT{i}" for i in range(1, 10))}

def _long(p: str) -> str:
    if not p or len(p) < 240 or p.startswith("\\\\?\\"): return p
    return "\\\\?\\UNC\\" + p[2:] if p.startswith("\\\\") else "\\\\?\\" + p

def norm(p) -> str:
    """a full Windows path in its usual form (no \\\\?\\, no trailing separator except a drive's root); ValueError when it is not one"""
    p = str(p or "").strip().strip('"')
    if p.startswith("\\\\?\\UNC\\"): p = "\\\\" + p[8:]
    elif p.startswith("\\\\?\\"): p = p[4:]
    if len(p) == 2 and p[1] == ":": p += "\\"
    if not (re.match(r"^[A-Za-z]:[\\/]", p) or (p.startswith("\\\\") and not p.startswith("\\\\.\\"))): raise ValueError("That is not a full folder path.")
    if any(c in p[2:] for c in '<>"|?*') or any(ord(c) < 32 for c in p): raise ValueError("That path has characters Windows does not allow.")
    full = os.path.normpath(p)
    if len(full) > 3 and full.endswith("\\"): full = full.rstrip("\\")
    return full

def name_problem(n: str, what: str = "file") -> str | None:
    """why a name cannot be a file's or a folder's name on Windows (None: it can)"""
    n = str(n or "")
    if not n.strip(): return f"Type a name for the {what}."
    if any(c in n for c in '\\/:*?"<>|') or any(ord(c) < 32 for c in n): return f'A {what} name cannot contain any of these: \\ / : * ? " < > |'
    if n.endswith(".") or n.endswith(" "): return f"A {what} name cannot end with a period or a space."
    if n in (".", ".."): return f"Type a name for the {what}."
    if n.split(".")[0].rstrip().upper() in RESERVED: return f"“{n.split('.')[0].rstrip()}” is a name Windows keeps for itself. Type another name."
    if len(n) > 240: return f"That name is too long ({len(n)} characters). Use a shorter one."
    return None

def _known(guid: str) -> str:
    """a Windows known folder (Desktop, Documents, Downloads, Pictures) for this user, or ''"""
    try:
        class GUID(ctypes.Structure): _fields_ = [("a", ctypes.c_uint32), ("b", ctypes.c_uint16), ("c", ctypes.c_uint16), ("d", ctypes.c_ubyte * 8)]
        h = guid.replace("-", ""); g = GUID(int(h[0:8], 16), int(h[8:12], 16), int(h[12:16], 16), (ctypes.c_ubyte * 8)(*bytes.fromhex(h[16:32])))
        out = ctypes.c_wchar_p()
        if ctypes.windll.shell32.SHGetKnownFolderPath(ctypes.byref(g), 0, None, ctypes.byref(out)) != 0: return ""
        try: return out.value or ""
        finally: ctypes.windll.ole32.CoTaskMemFree(out)
    except Exception: return ""

KNOWN = [("Desktop", "B4BFCC3A-DB2C-424C-B029-7FE99A87C641"), ("Documents", "FDD39AD0-238F-46AF-ADB4-6C85480369C7"),
         ("Downloads", "374DE290-123F-4565-9164-39C4925E467B"), ("Pictures", "33E28130-4E1E-4676-835A-98395C3BC3BB")]

def _drives() -> list:
    """this PC's drives by letter and kind (Windows' drive list and each drive's type; nothing on a drive is read here)"""
    out = []
    try:
        mask = ctypes.windll.kernel32.GetLogicalDrives()
        for i, letter in enumerate(string.ascii_uppercase):
            if not mask & (1 << i): continue
            root = letter + ":\\"; t = ctypes.windll.kernel32.GetDriveTypeW(root)
            if t <= 1: continue
            kind = {2: "USB or SD drive", 4: "Network drive", 5: "Disc drive"}.get(t, "Local disk")
            out.append({"name": f"{kind} ({letter}:)", "path": root, "drive": True})
    except Exception: pass
    return out

def places() -> list:
    if PLACES_OVERRIDE is not None: return [dict(p) for p in PLACES_OVERRIDE]
    out = []
    for name, guid in KNOWN:
        p = _known(guid)
        if p and os.path.isdir(p) and not any(x["path"].lower() == p.lower() for x in out): out.append({"name": name, "path": p})
    out.append({"name": "This PC", "path": "", "pc": True})
    return out + _drives()

def _kind(name: str) -> str:
    ext = os.path.splitext(name)[1].lower().lstrip(".")
    return {"": "File", "zip": "ZIP file", "json": "Settings or data file", "png": "PNG picture", "jpg": "JPEG picture", "jpeg": "JPEG picture",
            "gif": "GIF picture", "bmp": "Bitmap picture", "webp": "WebP picture", "nes": "NES game file", "unh": "NES game file", "unif": "NES game file",
            "fds": "NES game file", "bin": "Image file", "rom": "Game file", "ccs": "Classic Cart Studio backup", "ccshome": "Shared HOME Design",
            "txt": "Text file", "exe": "Program", "sav": "Game save file"}.get(ext, ext.upper() + " file")

def _crumbs(path: str) -> list:
    steps = []; p = path
    while True:
        parent = os.path.dirname(p)
        steps.insert(0, {"name": p if parent == p else os.path.basename(p), "path": p})
        if parent == p or not parent: break
        p = parent
    root = steps[0]["path"]; d = next((x for x in places() if x.get("drive") and x["path"].rstrip("\\").lower() == root.rstrip("\\").lower()), None)
    if d: steps[0]["name"] = d["name"]
    return [{"name": "This PC", "path": ""}] + steps

def list_folder(path) -> dict:
    if not path: return {"ok": True, "path": "", "crumbs": [{"name": "This PC", "path": ""}], "entries": [{"name": d["name"], "path": d["path"], "folder": True, "drive": True, "kind": "Drive"} for d in places() if d.get("drive")]}
    try: full = norm(path)
    except ValueError as e: return {"ok": False, "error": str(e)}
    entries = []
    try:
        with os.scandir(_long(full)) as it:
            for e in it:
                try:
                    s = e.stat(follow_symlinks=False); attrs = getattr(s, "st_file_attributes", 0)
                    if attrs & (st.FILE_ATTRIBUTE_HIDDEN | st.FILE_ATTRIBUTE_SYSTEM): continue   # as File Explorer shows them by default
                    folder = e.is_dir()
                    entries.append({"name": e.name, "path": os.path.join(full, e.name), "folder": folder, "size": None if folder else s.st_size,
                                    "modified": datetime.fromtimestamp(s.st_mtime).strftime("%Y-%m-%dT%H:%M:%S"), "kind": "Folder" if folder else _kind(e.name)})
                except OSError: continue
    except FileNotFoundError: return {"ok": False, "error": f"There is no folder at that place: {full}. Check the spelling, or choose a place at the side."}
    except NotADirectoryError: return {"ok": False, "error": f"That is a file, not a folder: {full}.", "file": True}
    except PermissionError: return {"ok": False, "error": f"Windows did not allow this folder to be opened (you may not have permission to see it): {full}"}
    except OSError as e: return {"ok": False, "error": f"This folder could not be opened ({e.strerror or e}): {full}"}
    return {"ok": True, "path": full, "parent": os.path.dirname(full) if os.path.dirname(full) != full else "", "crumbs": _crumbs(full), "entries": entries}

def stat(path) -> dict:
    try: full = norm(path)
    except ValueError as e: return {"ok": False, "error": str(e)}
    try: s = os.stat(_long(full))
    except OSError: return {"ok": True, "path": full, "exists": False}
    folder = st.S_ISDIR(s.st_mode)
    return {"ok": True, "path": full, "exists": True, "folder": folder, "file": not folder, "size": None if folder else s.st_size, "modified": datetime.fromtimestamp(s.st_mtime).strftime("%Y-%m-%dT%H:%M:%S")}

def make_folder(parent, name) -> dict:
    try: full = norm(parent)
    except ValueError as e: return {"ok": False, "error": str(e)}
    why = name_problem(name, "folder")
    if why: return {"ok": False, "error": why}
    target = os.path.join(full, str(name).strip())
    if os.path.exists(_long(target)): return {"ok": False, "error": f"Something named “{name}” is already here. Nothing was changed."}
    try: os.mkdir(_long(target))
    except PermissionError: return {"ok": False, "error": "The folder could not be made here: Windows did not allow it (you may not have permission to change this folder)."}
    except OSError as e: return {"ok": False, "error": f"The folder could not be made here ({e.strerror or e})."}
    return {"ok": True, "path": target}

# ------------------------------------------------------------------ the person's choice
def grant(paths, mode: str) -> dict:
    """the files the person chose in the dialog: to read (each an existing file) or the one to save to (its folder exists)"""
    out = []; now = time.time()
    for p in (paths or [])[:500]:
        try: full = norm(p)
        except ValueError as e: return {"ok": False, "error": str(e)}
        if mode == "read":
            if not os.path.isfile(_long(full)): return {"ok": False, "error": f"That file is not there any more: {full}"}
        elif mode == "write":
            if not os.path.isdir(_long(os.path.dirname(full))): return {"ok": False, "error": f"That folder is not there any more: {os.path.dirname(full)}"}
            if os.path.isdir(_long(full)): return {"ok": False, "error": f"A folder named “{os.path.basename(full)}” is already there. Choose another name."}
            why = name_problem(os.path.basename(full));
            if why: return {"ok": False, "error": why}
        else: return {"ok": False, "error": "unknown choice"}
        t = secrets.token_urlsafe(18)
        with _LOCK:
            for k in [k for k, v in _GRANTS.items() if v["expires"] < now]: _GRANTS.pop(k, None)
            _GRANTS[t] = {"path": full, "mode": mode, "expires": now + GRANT_SECONDS}
        out.append({"token": t, "path": full, "name": os.path.basename(full), "size": os.path.getsize(_long(full)) if mode == "read" else None})
    return {"ok": True, "files": out}

def _take(token: str, mode: str) -> str | None:
    with _LOCK:
        g = _GRANTS.get(str(token or ""))
        if not g or g["mode"] != mode or g["expires"] < time.time(): return None
        if mode == "write": _GRANTS.pop(token, None)   # one write per choice
        return g["path"]

def read_path(token: str) -> str | None:
    p = _take(token, "read")
    return _long(p) if p and os.path.isfile(_long(p)) else None

def write_to(handler, token: str, read_body) -> dict:
    """the page's bytes into the file the person chose: written beside it first, then put in its place (a failed write leaves the old file)"""
    p = _take(token, "write")
    if not p: return {"ok": False, "error": "That save is no longer ready. Choose where to save it again."}
    tmp = _long(p) + ".ccs-saving-" + secrets.token_hex(4)
    try:
        with open(tmp, "wb") as f: read_body(f)
        os.replace(tmp, _long(p))
    except PermissionError:
        _drop(tmp); return {"ok": False, "error": "Windows did not allow the file to be saved there (you may not have permission, or the file is open in another program)."}
    except OSError as e:
        _drop(tmp); return {"ok": False, "error": f"The file could not be saved there ({e.strerror or e})."}
    except Exception as e:
        _drop(tmp); return {"ok": False, "error": f"The file was not saved ({e})."}
    return {"ok": True, "path": p, "name": os.path.basename(p), "size": os.path.getsize(_long(p))}

def _drop(p):
    try: os.remove(p)
    except OSError: pass

def chosen(body: dict, key: str = "path") -> str:
    """a path the page sent for an operation (one the person chose in the dialog), checked to be a full path; '' when none"""
    v = body.get(key) if isinstance(body, dict) else None
    if not isinstance(v, str) or not v.strip(): return ""
    try: return norm(v)
    except ValueError: return ""

def chosen_list(body: dict, key: str = "paths") -> list:
    v = body.get(key) if isinstance(body, dict) else None
    out = []
    for x in (v if isinstance(v, list) else [])[:5000]:
        try: out.append(norm(x))
        except (ValueError, TypeError): continue
    return out
