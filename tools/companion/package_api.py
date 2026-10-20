"""The backend's .ccs package routes (ccs_package.py does the format work). Everything stays on this computer.

Desktop application only (the window's desktop principal):
  POST /api/package/export        build a package from the page's collection (optionally with the chosen game files) in the staging folder
  GET  /api/package/file?id=      the built package's bytes, for the page's Save As
  POST /api/package/stage         a package the user picked in the window (raw bytes) -> staged, verified, described
  POST /api/package/open          a package opened from Windows (file association; a local .ccs path) -> staged, verified, described
  GET  /api/package/preview?id=   the description of a staged package (also for one handed over by the website)
  POST /api/package/import-roms   after the recipient accepted: verified game files into the managed stores
The website (allow-listed origin only, like the collection offer) may only ADD a package to the staging folder:
  POST /api/handoff/package       raw bytes of a package the person chose on ClassicCartStudio.com; never readable back by any website
Staged packages expire after a day."""
from __future__ import annotations
import base64, json, os, re, secrets, shutil, sqlite3, time
import ccs_package as cp

STAGE_TTL = 24 * 3600; MAX_UPLOAD = 2 * 1024 ** 3
ID_RE = re.compile(r"^[A-Za-z0-9_-]{16,40}$")

def stage_dir(data_path) -> str:
    d = data_path("packages"); os.makedirs(d, exist_ok=True); return d

def _prune(d: str):
    now = time.time()
    for n in os.listdir(d):
        p = os.path.join(d, n)
        try:
            if now - os.path.getmtime(p) > STAGE_TTL: os.remove(p)
        except OSError: pass

def _new_path(d: str) -> tuple[str, str]:
    i = secrets.token_urlsafe(18); return i, os.path.join(d, i + ".ccs")

def _path_for(d: str, i: str) -> str | None:
    if not ID_RE.match(i or ""): return None
    p = os.path.join(d, i + ".ccs"); return p if os.path.isfile(p) else None

def describe(i: str, path: str, origin: str = "") -> dict:
    """the import preview: collection, counts, game files included, problems (never the bytes of a game file)"""
    try: info = cp.inspect(path, verify_roms=True)
    except cp.PackageError as e: return {"ok": False, "id": i, "error": str(e), "size": os.path.getsize(path) if os.path.exists(path) else 0}
    m = info["manifest"]
    return {"ok": True, "id": i, "kind": info["kind"], "data": info["data"], "summary": m.get("summary") or {}, "size": info["size"], "origin": origin,
            "roms": [{k: r.get(k) for k in ("version_id", "title", "size", "sha256", "verified")} for r in info["roms"]], "rom_bytes": info["rom_bytes"],
            "missing_roms": (m.get("roms") or {}).get("missing") or [], "problems": info["problems"], "created": m.get("created"), "app": m.get("app")}

def _read_raw_to(handler, path: str, limit: int) -> int:
    n = int(handler.headers.get("Content-Length", "0") or 0)
    if n <= 0: raise ValueError("empty body")
    if n > limit: raise OverflowError("too large")
    handler._body_read = True; left = n
    with open(path, "wb") as f:
        while left:
            chunk = handler.rfile.read(min(1 << 20, left))
            if not chunk: raise ValueError("incomplete upload")
            f.write(chunk); left -= len(chunk)
    return n

def handle_website_package(handler, data_path) -> None:
    """POST /api/handoff/package from the allow-listed website: stage only; the desktop window decides what happens with it"""
    d = stage_dir(data_path); _prune(d); i, p = _new_path(d)
    try: _read_raw_to(handler, p, MAX_UPLOAD)
    except OverflowError: return handler._json(413, {"ok": False, "error": "package too large"})
    except Exception as e:
        try: os.remove(p)
        except OSError: pass
        return handler._json(400, {"ok": False, "error": str(e)})
    try: cp.inspect(p, verify_roms=False)
    except cp.PackageError as e:
        os.remove(p); return handler._json(400, {"ok": False, "error": str(e)})
    return handler._json(200, {"ok": True, "id": i})

def _rom_source(vid: str) -> str | None:
    """the local game file of an exact version: a personal game's managed copy, the managed library copy, or the development payload store"""
    import personal as ps
    if vid.startswith("p_"):
        e = ps.load_index()["entries"].get(vid)
        p = e and (e.get("managed_copy") if e.get("managed_copy") and os.path.exists(e["managed_copy"]) else e.get("source_path"))
        return p if p and os.path.exists(p) else None
    rec = ps.load_store().get(vid)
    if rec and os.path.exists(rec.get("path") or ""): return rec["path"]
    try:
        import hakchi_bridge as hb
        c = hb.resolve_version(vid) if hasattr(hb, "resolve_version") else None
        sha = (c or {}).get("payload_sha256") or ""
        st = (c or {}).get("stored")
        if st and os.path.exists(st): return st
        from common import DB_PATH
        if sha and os.path.exists(DB_PATH):
            con = sqlite3.connect(DB_PATH); r = con.execute("SELECT stored FROM payloads WHERE sha256=?", (sha,)).fetchone(); con.close()
            if r and r[0] and os.path.exists(r[0]): return r[0]
    except Exception: pass
    return None

def handle_post(handler, path: str, data_path, version: str) -> None:
    d = stage_dir(data_path)
    if path == "/api/package/stage":
        _prune(d); i, p = _new_path(d)
        try: _read_raw_to(handler, p, MAX_UPLOAD)
        except OverflowError: return handler._json(413, {"ok": False, "error": "the package is larger than 2 GB"})
        except Exception as e: return handler._json(400, {"ok": False, "error": str(e)})
        return handler._json(200, describe(i, p))
    body = handler._body()
    if path == "/api/package/open":
        src = str(body.get("path") or "")
        if not src.lower().endswith(".ccs") or not os.path.isfile(src): return handler._json(400, {"ok": False, "error": "that is not a .ccs file on this computer"})
        if os.path.getsize(src) > MAX_UPLOAD: return handler._json(413, {"ok": False, "error": "the package is larger than 2 GB"})
        _prune(d); i, p = _new_path(d); shutil.copyfile(src, p)
        r = describe(i, p); r["file_name"] = os.path.basename(src); return handler._json(200, r)
    if path == "/api/package/import-roms":
        p = _path_for(d, str(body.get("id") or ""))
        if not p: return handler._json(404, {"ok": False, "error": "that package is no longer staged; open the .ccs file again"})
        acc = body.get("accept"); acc = [str(x) for x in acc] if isinstance(acc, list) else None
        try: r = cp.import_roms(p, acc)
        except cp.PackageError as e: return handler._json(400, {"ok": False, "error": str(e)})
        except OSError as e: return handler._json(507, {"ok": False, "error": f"the game files could not be stored ({e.strerror or e}); nothing was imported"})
        return handler._json(200, dict(r, ok=True))
    if path == "/api/package/rom-check":   # which of the collection's game files are on this computer (before the person confirms what to package)
        avail, missing = [], []
        for g in body.get("games") or []:
            vid = str((g or {}).get("version_id") or ""); title = str((g or {}).get("title") or vid)[:120]
            src = _rom_source(vid) if re.fullmatch(r"[A-Za-z0-9_.:-]{1,96}", vid) else None
            (avail if src else missing).append({"version_id": vid, "title": title, "size": os.path.getsize(src) if src else None})
        return handler._json(200, {"ok": True, "available": avail, "missing": missing})
    if path == "/api/package/export":
        coll = body.get("collection"); backup = body.get("backup")
        if not isinstance(coll, dict) and not isinstance(backup, dict): return handler._json(400, {"ok": False, "error": "nothing to export"})
        art = {}
        for k, v in (body.get("art") or {}).items():
            try: art[str(k)] = base64.b64decode(str(v).split(",", 1)[-1], validate=True)
            except Exception: pass
        roms, missing = [], []
        if body.get("include_roms") is True:
            for g in body.get("roms") or []:
                vid = str((g or {}).get("version_id") or ""); title = str((g or {}).get("title") or vid)
                src = _rom_source(vid) if re.fullmatch(r"[A-Za-z0-9_.:-]{1,96}", vid) else None
                if src: roms.append({"version_id": vid, "path": src, "title": title})
                else: missing.append({"version_id": vid, "title": title, "reason": "the game file is not on this computer"})
        _prune(d); i, p = _new_path(d)
        try: man = cp.write(p, collection=coll if isinstance(coll, dict) else None, backup=backup if isinstance(backup, dict) and not isinstance(coll, dict) else None,
                            art=art, roms=roms, missing=missing, summary=body.get("summary") or {}, app_version=version)
        except cp.PackageError as e: return handler._json(400, {"ok": False, "error": str(e)})
        except OSError as e: return handler._json(507, {"ok": False, "error": f"the package could not be written ({e.strerror or e})"})
        return handler._json(200, {"ok": True, "id": i, "size": os.path.getsize(p), "included": man["roms"]["included"], "missing": man["roms"]["missing"]})
    return handler._json(404, {"ok": False, "error": "unknown package endpoint"})

def handle_get(handler, path: str, qs: str, data_path) -> None:
    d = stage_dir(data_path); q = dict(x.split("=", 1) for x in qs.split("&") if "=" in x)
    p = _path_for(d, q.get("id", ""))
    if not p: return handler._json(404, {"ok": False, "error": "that package is no longer staged"})
    if path == "/api/package/preview": return handler._json(200, describe(q["id"], p))
    if path == "/api/package/file":
        n = os.path.getsize(p); handler.send_response(200); handler.send_header("Content-Type", "application/vnd.classiccartstudio.collection+zip"); handler.send_header("Content-Length", str(n)); handler.send_header("Cache-Control", "no-store"); handler.end_headers()
        with open(p, "rb") as f: shutil.copyfileobj(f, handler.wfile, 1 << 20)
        return
    return handler._json(404, {"ok": False, "error": "unknown package endpoint"})
