"""The backend's .ccs package routes (ccs_package.py does the format work; CCS-FORMAT.md is the specification). Everything stays on this
computer: no route here uses the network.

Desktop application only (the window's desktop principal):
  POST /api/package/rom-check     {games: [{version_id, title}]} -> which games have their exact ROM file on this computer
  POST /api/package/export        build a COMPLETE package of the page's collection in the staging folder (every game with its exact ROM)
  GET  /api/package/file?id=      the built package's bytes
  POST /api/package/save          {id, name} -> native Save As on this computer, the staged package copied there
  POST /api/package/reveal        {id} -> File Explorer with the file the save route produced selected
  POST /api/package/stage         a package the user picked in the window (raw bytes) -> staged, verified, described
  POST /api/package/open          a package opened from Windows (file association; a local .ccs path) -> staged, verified, described
  GET  /api/package/preview?id=   the description of a staged package (also for one handed over by the website)
  POST /api/package/import-roms   after the recipient accepted: verified ROM files into the managed stores
The website (allow-listed origin only, like the collection offer) may only ADD a package to the staging folder:
  POST /api/handoff/package       raw bytes of a package the person chose on ClassicCartStudio.com; never readable back by any website
There is no package size limit; a package is refused (HTTP 507) only when the drive lacks the free space for it. Staged packages expire
after a day."""
from __future__ import annotations
import base64, json, os, re, secrets, shutil, sqlite3, subprocess, threading, time
import ccs_package as cp

STAGE_TTL = 24 * 3600; SPACE_MARGIN = 64 * 1024 ** 2
ID_RE = re.compile(r"^[A-Za-z0-9_-]{16,40}$"); GAME_RE = re.compile(r"^[A-Za-z0-9_.:-]{1,96}$")
SAVED: dict = {}          # staged id -> the path the save route wrote (the only paths reveal opens)
_IDENT: dict = {}         # path -> (size, mtime_ns, identity): content hashes of local ROM files, recomputed when a file changes
_DESC: dict = {}          # staged path -> (size, mtime_ns, description)
_LOCK = threading.Lock()

class NoSpace(Exception):
    pass

def stage_dir(data_path) -> str:
    d = data_path("packages"); os.makedirs(d, exist_ok=True); return d

def _prune(d: str):
    now = time.time()
    for n in os.listdir(d):
        p = os.path.join(d, n)
        try:
            if now - os.path.getmtime(p) > STAGE_TTL: os.remove(p); _DESC.pop(p, None)
        except OSError: pass

def _new_path(d: str) -> tuple[str, str]:
    i = secrets.token_urlsafe(18); return i, os.path.join(d, i + ".ccs")

def _path_for(d: str, i: str) -> str | None:
    if not ID_RE.match(i or ""): return None
    p = os.path.join(d, i + ".ccs"); return p if os.path.isfile(p) else None

def _gb(n: int) -> str:
    return f"{n / 1024 ** 3:.1f} GB" if n >= 1024 ** 3 else f"{max(1, round(n / 1024 ** 2))} MB"

def _check_space(folder: str, need: int, what: str):
    """Raise NoSpace with a plain message when the drive holding `folder` has less than need + a margin free."""
    try: free = shutil.disk_usage(folder).free
    except OSError: return
    if need + SPACE_MARGIN > free:
        raise NoSpace(f"There isn't enough free space on this computer to {what} (about {_gb(need + SPACE_MARGIN)} is needed, {_gb(free)} is free). Free up some space, then try again.")

def describe(i: str, path: str, origin: str = "") -> dict:
    """the import preview: collection, counts, ROM files (verified), problems (never the bytes of a ROM file)"""
    try: st = os.stat(path); key = (st.st_size, st.st_mtime_ns)
    except OSError: return {"ok": False, "id": i, "error": "That package is no longer open. Open the .ccs file again."}
    hit = _DESC.get(path)
    if hit and hit[:2] == key: return dict(hit[2], origin=origin)
    try: info = cp.inspect(path, verify_roms=True)
    except cp.PackageError as e: return {"ok": False, "id": i, "error": str(e), "size": st.st_size}
    m = info["manifest"]
    out = {"ok": True, "id": i, "kind": info["kind"], "data": info["data"], "summary": m.get("summary") or {}, "size": info["size"], "origin": origin,
           "valid": info["ok"], "games": info["games"], "roms": [{k: r.get(k) for k in ("version_id", "title", "size", "sha256", "content_sha256", "verified")} for r in info["roms"]],
           "rom_bytes": info["rom_bytes"], "art_count": len(info["art"]), "problems": info["problems"], "created": m.get("created"), "app": m.get("app")}
    _DESC[path] = (key[0], key[1], out); return out

def _read_raw_to(handler, path: str) -> int:
    n = int(handler.headers.get("Content-Length", "0") or 0)
    if n <= 0: raise ValueError("The package is empty.")
    _check_space(os.path.dirname(path), n, "open this package")
    handler._body_read = True; left = n
    try:
        with open(path, "wb") as f:
            while left:
                chunk = handler.rfile.read(min(1 << 20, left))
                if not chunk: raise ValueError("The package did not arrive completely. Try again.")
                f.write(chunk); left -= len(chunk)
    except BaseException:
        try: os.remove(path)
        except OSError: pass
        raise
    return n

def _json_body(handler) -> dict:
    """The JSON request body (a collection with its artwork can be large; bounded like the package's own JSON files)."""
    n = int(handler.headers.get("Content-Length", "0") or 0)
    if n > cp.MAX_JSON: raise ValueError("The request is larger than 1 GB.")
    handler._body_read = True; buf = bytearray(); left = n
    while left:
        b = handler.rfile.read(min(1 << 20, left))
        if not b: raise ValueError("The request did not arrive completely. Try again.")
        buf.extend(b); left -= len(b)
    v = json.loads(bytes(buf).decode("utf-8") or "{}"); return v if isinstance(v, dict) else {}

def handle_website_package(handler, data_path) -> None:
    """POST /api/handoff/package from the allow-listed website: stage only; the desktop window decides what happens with it"""
    d = stage_dir(data_path); _prune(d); i, p = _new_path(d)
    try: _read_raw_to(handler, p)
    except NoSpace as e: return handler._json(507, {"ok": False, "error": str(e)})
    except Exception as e: return handler._json(400, {"ok": False, "error": str(e)})
    try: cp.inspect(p, verify_roms=False)   # the listing, completeness, names and sizes; the window verifies every ROM before showing it
    except cp.PackageError as e:
        os.remove(p); return handler._json(400, {"ok": False, "error": str(e)})
    return handler._json(200, {"ok": True, "id": i})

# ---------------- local ROM files: exact identity only ----------------
def _rom_candidates(vid: str, ctx: dict | None = None) -> list[str]:
    """local files that may hold this exact version: a personal game's managed copy and original file, the managed library copy, or the
    development payload store. Every candidate is checked by content identity before use; nothing else is ever substituted."""
    import personal as ps
    ctx = ctx if ctx is not None else {}; out = []
    if "index" not in ctx: ctx["index"] = ps.load_index().get("entries") or {}; ctx["store"] = ps.load_store()
    if vid.startswith("p_"):
        e = ctx["index"].get(vid) or {}
        out += [e.get("managed_copy"), e.get("source_path")]
    else:
        rec = ctx["store"].get(vid) or {}; out.append(rec.get("path"))
        try:
            import hakchi_bridge as hb
            c = hb.resolve_version(vid) if hasattr(hb, "resolve_version") else None
            out.append((c or {}).get("stored")); sha = (c or {}).get("payload_sha256") or ""
            from common import DB_PATH
            if sha and os.path.exists(DB_PATH):
                con = sqlite3.connect(DB_PATH)
                try: r = con.execute("SELECT stored FROM payloads WHERE sha256=?", (sha,)).fetchone()
                finally: con.close()
                if r: out.append(r[0])
        except Exception: pass
    seen, res = set(), []
    for p in out:
        if p and isinstance(p, str) and p not in seen and os.path.isfile(p) and not os.path.islink(p): seen.add(p); res.append(p)
    return res

def _identity(p: str) -> dict:
    st = os.stat(p); key = (st.st_size, st.st_mtime_ns)
    with _LOCK: hit = _IDENT.get(p)
    if hit and hit[:2] == key: return hit[2]
    ident = cp.identify_file(p)
    with _LOCK: _IDENT[p] = (key[0], key[1], ident)
    return ident

def classify(vid: str, title: str, ctx: dict | None = None) -> tuple[str, dict, str | None, dict | None]:
    """('available' | 'missing' | 'mismatched', record, path, identity) for one game id"""
    if not cp.is_identity_id(vid): return "missing", {"version_id": vid, "title": title, "reason": "no_identity"}, None, None
    cands = _rom_candidates(vid, ctx); wrong = None
    for p in cands:
        try: ident = _identity(p)
        except OSError: continue
        if cp.id_matches(vid, ident["content_sha256"]): return "available", {"version_id": vid, "title": title, "size": ident["size"], "content_sha256": ident["content_sha256"]}, p, ident
        wrong = wrong or ident
    if wrong: return "mismatched", {"version_id": vid, "title": title, "reason": "different_version", "found_content_sha256": wrong["content_sha256"]}, None, None
    return "missing", {"version_id": vid, "title": title, "reason": "not_found"}, None, None

def _games_titles(body: dict) -> dict:
    t = {}
    for g in (body.get("roms") or body.get("games") or []):
        if isinstance(g, dict) and isinstance(g.get("version_id"), str): t[g["version_id"]] = str(g.get("title") or "")[:120]
    return t

def _safe_file_name(name: str) -> str:
    n = re.sub(r"\s+", " ", re.sub(r'[<>:"/\\|?*\x00-\x1f]+', " ", str(name or ""))).strip().rstrip(". ")[:120] or "Collection"
    if re.match(r"^(con|prn|aux|nul|com\d|lpt\d)$", n, re.I): n = "Collection " + n
    return n if n.lower().endswith(".ccs") else n + ".ccs"

# ---------------- Save As and Explorer (this computer only) ----------------
def pick_save(title: str, initial_name: str, initial_dir: str = "") -> str:
    """Native Windows Save As dialog on the user's desktop (the same tkinter mechanism as the backend's file pickers)."""
    import tkinter as tk
    from tkinter import filedialog
    root = tk.Tk(); root.withdraw(); root.attributes("-topmost", True)
    try: return filedialog.asksaveasfilename(title=title, initialfile=initial_name, initialdir=initial_dir or None, defaultextension=".ccs",
                                             filetypes=[("Classic Cart Studio collection", "*.ccs")], confirmoverwrite=True) or ""
    finally: root.destroy()

def open_explorer(path: str):
    """File Explorer with the file selected (explorer.exe /select,"<path>"); Windows paths cannot contain a double quote."""
    if '"' in path: raise ValueError("unsafe path")
    subprocess.Popen(f'explorer.exe /select,"{os.path.normpath(path)}"')

def _default_dir() -> str:
    last = next((os.path.dirname(p) for p in reversed(list(SAVED.values())) if os.path.isdir(os.path.dirname(p))), "")
    docs = os.path.join(os.path.expanduser("~"), "Documents")
    return last or (docs if os.path.isdir(docs) else "")

def _copy_to(src: str, dest: str):
    _check_space(os.path.dirname(dest) or ".", os.path.getsize(src), "save this package there")
    tmp = dest + ".part"
    try:
        with open(src, "rb") as f, open(tmp, "wb") as w: shutil.copyfileobj(f, w, 1 << 20)
        os.replace(tmp, dest)
    except BaseException:
        try: os.remove(tmp)
        except OSError: pass
        raise

# ---------------- routes ----------------
def handle_post(handler, path: str, data_path, version: str, cfg: dict | None = None) -> None:
    cfg = cfg or {}; d = stage_dir(data_path)
    if path == "/api/package/stage":
        _prune(d); i, p = _new_path(d)
        try: _read_raw_to(handler, p)
        except NoSpace as e: return handler._json(507, {"ok": False, "error": str(e)})
        except Exception as e: return handler._json(400, {"ok": False, "error": str(e)})
        return handler._json(200, describe(i, p))
    try: body = _json_body(handler)
    except Exception as e: return handler._json(400, {"ok": False, "error": f"The request could not be read ({e})."})
    if path == "/api/package/open":
        src = str(body.get("path") or "")
        if not src.lower().endswith(".ccs") or not os.path.isfile(src): return handler._json(400, {"ok": False, "error": "That file is not a .ccs file on this computer."})
        _prune(d); i, p = _new_path(d)
        try: _check_space(d, os.path.getsize(src), "open this package"); shutil.copyfile(src, p)
        except NoSpace as e: return handler._json(507, {"ok": False, "error": str(e)})
        except OSError as e:
            try: os.remove(p)
            except OSError: pass
            return handler._json(400, {"ok": False, "error": f"The file could not be read ({e.strerror or e})."})
        r = describe(i, p); r["file_name"] = os.path.basename(src); return handler._json(200, r)
    if path == "/api/package/import-roms":
        p = _path_for(d, str(body.get("id") or ""))
        if not p: return handler._json(404, {"ok": False, "error": "That package is no longer open. Open the .ccs file again."})
        acc = body.get("accept"); acc = [str(x) for x in acc] if isinstance(acc, list) else None
        try: r = cp.import_roms(p, acc)
        except cp.PackageError as e: return handler._json(400, {"ok": False, "error": str(e)})
        except OSError as e: return handler._json(507, {"ok": False, "error": f"The ROM files could not be saved on this computer ({e.strerror or e}). Nothing was imported."})
        return handler._json(200, dict(r, ok=True))
    if path == "/api/package/rom-check":   # which of the games have their EXACT ROM file on this computer (never a substitute)
        out = {"available": [], "missing": [], "mismatched": []}; ctx = {}
        for g in body.get("games") or []:
            vid = str((g or {}).get("version_id") or "") if isinstance(g, dict) else ""; title = str((g or {}).get("title") or vid)[:120] if isinstance(g, dict) else ""
            if not GAME_RE.match(vid): out["missing"].append({"version_id": vid[:96], "title": title, "reason": "no_identity"}); continue
            state, rec, _, _ = classify(vid, title, ctx); out[state].append(rec)
        return handler._json(200, dict(out, ok=True))
    if path == "/api/package/export":   # complete packages only; ROM files are always included (include_roms is accepted and ignored)
        coll = body.get("collection")
        if not isinstance(coll, dict):
            if isinstance(body.get("backup"), dict): return handler._json(400, {"ok": False, "error": "Studio backups are saved as a backup file, not as a .ccs package."})
            return handler._json(400, {"ok": False, "error": "There is nothing to export. Open a collection, then try again."})
        ids = cp._game_ids(coll)
        if ids is None: return handler._json(400, {"ok": False, "error": "The collection has no game list, so it can't be packaged."})
        titles = {**cp._titles(coll, []), **_games_titles(body)}; roms, missing, mismatched, idents, ctx = [], [], [], {}, {}
        for vid in ids:
            state, rec, src, ident = classify(vid, titles.get(vid) or vid, ctx)
            if state == "available": roms.append({"version_id": vid, "path": src, "title": titles.get(vid) or ""}); idents[src] = ident
            else: (missing if state == "missing" else mismatched).append(rec)
        if missing or mismatched:
            parts = []
            if missing: parts.append(f"{len(missing)} game{'s' if len(missing) != 1 else ''} without a ROM file on this computer")
            if mismatched: parts.append(f"{len(mismatched)} game{'s' if len(mismatched) != 1 else ''} whose ROM file is a different version")
            return handler._json(409, {"ok": False, "missing": missing, "mismatched": mismatched,
                                       "error": "The package can't be made: " + " and ".join(parts) + ". Locate the exact files or remove those games, then try again. Nothing was saved."})
        art = {}
        for k, v in (body.get("art") or {}).items() if isinstance(body.get("art"), dict) else []:
            try: art[str(k)] = base64.b64decode(str(v).split(",", 1)[-1], validate=True)
            except Exception: pass
        _prune(d); i, p = _new_path(d)
        try:
            _check_space(d, sum(x["size"] for x in idents.values()) + sum(len(a) for a in art.values()) + len(json.dumps(coll)), "make this package")
            man = cp.write(p, collection=coll, roms=roms, art=art, summary=body.get("summary") if isinstance(body.get("summary"), dict) else {}, app_version=version, identities=idents)
        except NoSpace as e: return handler._json(507, {"ok": False, "error": str(e)})
        except cp.PackageError as e:
            det = getattr(e, "details", {}) or {}
            if det.get("missing") or det.get("mismatched") or det.get("unidentified"):
                return handler._json(409, {"ok": False, "error": str(e), "missing": [{"version_id": x, "title": titles.get(x) or x, "reason": "not_found"} for x in det.get("missing", [])] + [{"version_id": x, "title": titles.get(x) or x, "reason": "no_identity"} for x in det.get("unidentified", [])],
                                           "mismatched": [{"version_id": x, "title": titles.get(x) or x, "reason": "different_version"} for x in det.get("mismatched", [])]})
            return handler._json(400, {"ok": False, "error": str(e)})
        except OSError as e: return handler._json(507, {"ok": False, "error": f"The package could not be saved on this computer ({e.strerror or e}). Check the free space, then try again."})
        name = _safe_file_name((coll.get("ccs_collection") or {}).get("name") or coll.get("name") or "Collection")
        return handler._json(200, {"ok": True, "id": i, "size": os.path.getsize(p), "games": man["summary"]["games"], "rom_bytes": man["summary"]["rom_bytes"], "name": name, "included": man["roms"]["included"]})
    if path == "/api/package/save":
        i = str(body.get("id") or ""); p = _path_for(d, i)
        if not p: return handler._json(404, {"ok": False, "error": "That package is no longer ready. Make it again, then save it."})
        name = _safe_file_name(body.get("name") or "Collection")
        if cfg.get("rehearsal_tree") and body.get("path"): dest = str(body["path"])   # rehearsal/test configurations only: no dialog
        else: dest = pick_save("Save the collection package", name, _default_dir())
        if not dest: return handler._json(200, {"ok": False, "canceled": True})
        dest = os.path.abspath(dest)
        if not dest.lower().endswith(".ccs"): dest += ".ccs"
        try: _copy_to(p, dest)
        except NoSpace as e: return handler._json(507, {"ok": False, "error": str(e)})
        except OSError as e: return handler._json(500, {"ok": False, "error": f"The package could not be saved there ({e.strerror or e}). Choose another folder, then try again."})
        SAVED[i] = dest; return handler._json(200, {"ok": True, "name": os.path.basename(dest), "path": dest})
    if path == "/api/package/reveal":
        i = str(body.get("id") or ""); dest = SAVED.get(i) if ID_RE.match(i) else None
        if not dest or not os.path.isfile(dest): return handler._json(404, {"ok": False, "error": "The saved file is no longer there. It may have been moved or deleted."})
        try: open_explorer(dest)
        except (OSError, ValueError) as e: return handler._json(500, {"ok": False, "error": f"File Explorer could not be opened ({e})."})
        return handler._json(200, {"ok": True, "path": dest})
    return handler._json(404, {"ok": False, "error": "unknown package endpoint"})

def handle_get(handler, path: str, qs: str, data_path) -> None:
    d = stage_dir(data_path); q = dict(x.split("=", 1) for x in qs.split("&") if "=" in x)
    p = _path_for(d, q.get("id", ""))
    if not p: return handler._json(404, {"ok": False, "error": "That package is no longer open. Open the .ccs file again."})
    if path == "/api/package/preview": return handler._json(200, describe(q["id"], p))
    if path == "/api/package/file":
        n = os.path.getsize(p); handler.send_response(200); handler.send_header("Content-Type", cp.MEDIA_TYPE); handler.send_header("Content-Length", str(n)); handler.send_header("Cache-Control", "no-store"); handler.end_headers()
        with open(p, "rb") as f: shutil.copyfileobj(f, handler.wfile, 1 << 20)
        return
    return handler._json(404, {"ok": False, "error": "unknown package endpoint"})
