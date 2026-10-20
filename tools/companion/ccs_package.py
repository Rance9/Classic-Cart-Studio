r"""Classic Cart Studio collection packages (.ccs): the native, portable collection file of the website and the Windows application.

Format (documented in CCS-FORMAT.md beside this file): a ZIP archive (stored or deflate entries, no encryption, no ZIP64) containing
  ccs-manifest.json   {"format": "classic-cart-studio-package", "schema": "ccs-package/1", "kind": "collection" | "backup", "created",
                       "app", "summary": {...}, "files": [{"path", "sha256", "size", "kind", ...}], "roms": {"included": [...], "missing": [...]}}
  collection.json     the collection (schema-4 manifest with the Studio record: games, exact versions, order, personal entries, loadouts,
                      HOME settings) — or, for kind "backup", studio-backup.json (every collection, favorites, settings, snapshots)
  art/<id>.jpg        thumbnails of the collection's own artwork (personal games, replaced covers)
  roms/<version>.nes  OPTIONAL game files, only those the sender chose to include (exact bytes; .nes or .unh)
  README.txt          what the file is and how to open it
Every packaged file is listed in the manifest with its SHA-256 and size; nothing else may be in the archive.

Reading treats the package as untrusted input: entry names are checked (no absolute paths, drive letters, backslashes, '..', control
characters; only the known folders), duplicates and symlinks are rejected, sizes are bounded before and during decompression (a
decompression bomb stops at the declared size), every listed file must be present and match its checksum, and nothing in a package is
ever executed. ROM files are only extracted after the user accepts them, after the WHOLE package has been verified, into the Studio's
managed stores (personal.py), where each file is identified by its own content hash: exact versions only, never a substitute.
This module never uses the network."""
from __future__ import annotations
import hashlib, io, json, os, re, stat, tempfile, time, zipfile

FORMAT = "classic-cart-studio-package"; SCHEMA = "ccs-package/1"; MANIFEST = "ccs-manifest.json"
MAX_MANIFEST = 8 * 1024 * 1024; MAX_COLLECTION = 96 * 1024 * 1024; MAX_ART = 1024 * 1024; MAX_ROM = 16 * 1024 * 1024; MAX_ENTRIES = 60_000
MAX_TOTAL = 4 * 1024 ** 3; MAX_RATIO = 400
ROM_RE = re.compile(r"^roms/[A-Za-z0-9_.-]{1,96}\.(nes|unh)$"); ART_RE = re.compile(r"^art/[A-Za-z0-9_.-]{1,96}\.(jpg|png)$")
FIXED = {MANIFEST: "manifest", "collection.json": "collection", "studio-backup.json": "backup", "README.txt": "readme"}
README = """Classic Cart Studio collection package (.ccs)

This file holds a game collection made with Classic Cart Studio: its games and exact versions, its NES Classic Loadouts,
HOME settings and artwork{roms}.

Open it with Classic Cart Studio for Windows (double-click the file once the app is installed), or on the website:
https://classiccartstudio.com  ->  My Collections  ->  Import .ccs Collection

Classic Cart Studio does not provide or host game files. Whoever includes or receives game files is responsible for having the
rights to copy, distribute and use them.
"""

class PackageError(ValueError):
    pass

def _sha(b: bytes) -> str: return hashlib.sha256(b).hexdigest()

def safe_name(n: str) -> bool:
    if not n or len(n) > 200 or n.startswith(("/", "\\")) or "\\" in n or ":" in n or any(ord(c) < 32 for c in n): return False
    parts = n.split("/")
    if any(p in ("", ".", "..") for p in parts): return False
    return n in FIXED or bool(ROM_RE.match(n)) or bool(ART_RE.match(n))

# ---------------- writing ----------------
def write(out_path: str, *, collection: dict | None = None, backup: dict | None = None, art: dict | None = None, roms: list | None = None,
          missing: list | None = None, summary: dict | None = None, app_version: str = "", cancel=lambda: False, progress=lambda done, total: None) -> dict:
    """Write a package. art: {name: jpeg/png bytes}; roms: [{"version_id", "path" (a local file), "title", "content_sha256"?}] (only the
    files the user chose); missing: [{"version_id", "title", "reason"}]. Written to a temporary file next to out_path, then renamed: a
    cancelled or failed export leaves nothing behind. Returns the manifest."""
    if (collection is None) == (backup is None): raise PackageError("a package holds either one collection or a Studio backup")
    files, included = [], []
    body = json.dumps(collection if collection is not None else backup, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    main = "collection.json" if collection is not None else "studio-backup.json"
    tmp = out_path + ".part"; total = len(body) + sum(os.path.getsize(r["path"]) for r in roms or [] if os.path.exists(r["path"])); done = 0
    try:
        with zipfile.ZipFile(tmp, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as z:
            z.writestr(main, body); files.append({"path": main, "sha256": _sha(body), "size": len(body), "kind": FIXED[main]}); done += len(body)
            for name, data in sorted((art or {}).items()):
                n = "art/" + name
                if not ART_RE.match(n) or len(data) > MAX_ART: continue
                z.writestr(n, data); files.append({"path": n, "sha256": _sha(data), "size": len(data), "kind": "art"})
            seen = set()
            for r in roms or []:
                if cancel(): raise PackageError("cancelled")
                data = open(r["path"], "rb").read()
                if len(data) > MAX_ROM: raise PackageError(f"{r.get('title') or r['version_id']}: the game file is larger than a package allows")
                ext = ".unh" if not data.startswith(b"NES\x1a") else ".nes"; n = f"roms/{r['version_id']}{ext}"
                if n in seen or not ROM_RE.match(n): continue
                seen.add(n); z.writestr(n, data, compress_type=zipfile.ZIP_DEFLATED)
                rec = {"path": n, "sha256": _sha(data), "size": len(data), "kind": "rom", "version_id": r["version_id"], "title": str(r.get("title") or "")[:120]}
                files.append(rec); included.append({k: rec[k] for k in ("version_id", "title", "path", "sha256", "size")}); done += len(data); progress(done, total)
            readme = README.format(roms=", and game files chosen by the sender" if included else " (no game files)").encode("utf-8")
            z.writestr("README.txt", readme); files.append({"path": "README.txt", "sha256": _sha(readme), "size": len(readme), "kind": "readme"})
            man = {"format": FORMAT, "schema": SCHEMA, "kind": "collection" if collection is not None else "backup", "created": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                   "app": {"name": "Classic Cart Studio", "version": app_version}, "summary": summary or {}, "files": files,
                   "roms": {"included": included, "missing": [{k: str(m.get(k) or "")[:160] for k in ("version_id", "title", "reason")} for m in (missing or [])]}}
            z.writestr(MANIFEST, json.dumps(man, ensure_ascii=False, indent=1).encode("utf-8"))
        os.replace(tmp, out_path); return man
    except BaseException:
        try: os.remove(tmp)
        except OSError: pass
        raise

# ---------------- reading ----------------
def _bounded_read(z: zipfile.ZipFile, info: zipfile.ZipInfo, limit: int) -> bytes:
    """read an entry, refusing more bytes than it declares (and than the limit): a decompression bomb stops early"""
    if info.file_size > limit: raise PackageError(f"{info.filename}: larger than allowed")
    out = io.BytesIO()
    with z.open(info) as f:
        while True:
            chunk = f.read(65536)
            if not chunk: break
            out.write(chunk)
            if out.tell() > min(limit, info.file_size): raise PackageError(f"{info.filename}: more data than declared")
    return out.getvalue()

def inspect(path: str, *, verify_roms: bool = True) -> dict:
    """Validate a package completely and describe it (never extracts anything). Raises PackageError with a plain reason."""
    try: size = os.path.getsize(path)
    except OSError: raise PackageError("the file cannot be read")
    if size > MAX_TOTAL: raise PackageError("the file is larger than a collection package can be")
    try: z = zipfile.ZipFile(path)
    except (zipfile.BadZipFile, OSError) as e: raise PackageError("this is not a Classic Cart Studio package (not a valid archive)") from e
    with z:
        infos = z.infolist()
        if len(infos) > MAX_ENTRIES: raise PackageError("the package has too many entries")
        names = [i.filename for i in infos]
        if len(set(names)) != len(names): raise PackageError("the package has duplicate entries")
        total = 0
        for i in infos:
            if i.is_dir(): raise PackageError(f"unexpected folder entry {i.filename!r}")
            if not safe_name(i.filename): raise PackageError(f"unsafe or unexpected entry name {i.filename[:80]!r}")
            mode = (i.external_attr >> 16) & 0xFFFF
            if mode and stat.S_ISLNK(mode): raise PackageError(f"{i.filename}: links are not allowed in a package")
            if i.flag_bits & 0x1: raise PackageError("encrypted packages are not supported")
            if i.compress_type not in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED): raise PackageError("the package uses an unsupported compression method")
            if i.compress_size and i.file_size / max(1, i.compress_size) > MAX_RATIO: raise PackageError(f"{i.filename}: suspicious compression ratio")
            total += i.file_size
        if total > MAX_TOTAL: raise PackageError("the package would unpack to more than a collection package can hold")
        by = {i.filename: i for i in infos}
        if MANIFEST not in by: raise PackageError("this is not a Classic Cart Studio package (no ccs-manifest.json)")
        try: man = json.loads(_bounded_read(z, by[MANIFEST], MAX_MANIFEST).decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as e: raise PackageError("the package manifest is not valid") from e
        if not isinstance(man, dict) or man.get("format") != FORMAT: raise PackageError("this is not a Classic Cart Studio package")
        if man.get("schema") != SCHEMA: raise PackageError(f"this package was made by a newer Classic Cart Studio ({str(man.get('schema'))[:40]}); update the app to open it")
        listed = man.get("files")
        if not isinstance(listed, list) or not all(isinstance(f, dict) for f in listed): raise PackageError("the package manifest is not valid (files)")
        lp = [f.get("path") for f in listed]
        if len(set(lp)) != len(lp): raise PackageError("the package manifest lists a file twice")
        extra = set(names) - set(lp) - {MANIFEST}
        if extra: raise PackageError(f"the package contains files its manifest does not list ({sorted(extra)[0][:60]})")
        problems, roms, main = [], [], None
        for f in listed:
            p = f.get("path"); sha = f.get("sha256"); sz = f.get("size")
            if not isinstance(p, str) or not safe_name(p) or p == MANIFEST: raise PackageError("the package manifest lists an unsafe file name")
            if not (isinstance(sha, str) and re.fullmatch(r"[0-9a-f]{64}", sha)) or not isinstance(sz, int) or sz < 0: raise PackageError(f"{p}: invalid checksum or size in the manifest")
            if p not in by: problems.append({"path": p, "problem": "listed in the manifest but missing from the package"}); continue
            if by[p].file_size != sz: problems.append({"path": p, "problem": "size differs from the manifest"}); continue
            kind = "rom" if p.startswith("roms/") else "art" if p.startswith("art/") else FIXED[p]
            if kind == "rom" and not verify_roms: roms.append(dict(f, verified=None)); continue
            data = _bounded_read(z, by[p], MAX_ROM if kind == "rom" else MAX_ART if kind == "art" else MAX_COLLECTION)
            if _sha(data) != sha: problems.append({"path": p, "problem": "checksum does not match (damaged or altered)"}); continue
            if kind == "rom": roms.append(dict(f, verified=True))
            if kind in ("collection", "backup"):
                try: main = (kind, json.loads(data.decode("utf-8")))
                except (UnicodeDecodeError, json.JSONDecodeError): raise PackageError("the collection inside the package is not valid")
        if not main: raise PackageError("the package holds no collection")
        if man.get("kind") != ("collection" if main[0] == "collection" else "backup"): raise PackageError("the package manifest does not match its contents")
        return {"manifest": man, "kind": man["kind"], "data": main[1], "roms": roms, "problems": problems, "size": size,
                "ok": not problems, "rom_count": len(roms), "rom_bytes": sum(r["size"] for r in roms)}

def import_roms(path: str, accept: list[str] | None = None, *, ingest=None) -> dict:
    """After the user accepted: verify the whole package again, then hand each accepted, verified game file to the Studio's managed stores
    (personal.py identifies it by its own content; a known version gets the library copy, anything else a personal entry). Files already
    there are reused (deduplicated by content hash), never overwritten. Returns {"imported": [{version_id, stored_as, ...}], "rejected": [...]}"""
    info = inspect(path, verify_roms=True)
    if not info["ok"]: raise PackageError("the package failed its integrity check: " + "; ".join(f"{p['path']}: {p['problem']}" for p in info["problems"][:3]))
    if ingest is None:
        import personal as ps
        ingest = ps._ingest_bytes
    want = set(accept) if accept is not None else None; imported, rejected = [], []
    with zipfile.ZipFile(path) as z:
        for r in info["roms"]:
            vid = r.get("version_id") or ""
            if want is not None and vid not in want: continue
            data = _bounded_read(z, z.getinfo(r["path"]), MAX_ROM)
            if _sha(data) != r["sha256"]: rejected.append({"version_id": vid, "reason": "checksum does not match"}); continue
            res = ingest(data, os.path.basename(r["path"]), "package:" + os.path.basename(path), "from a Classic Cart Studio package", "package")
            got = (res.get("known") or {}).get("version_id") or (res.get("entry") or {}).get("id")
            exact = got == vid
            (imported if exact else rejected).append({"version_id": vid, "stored_as": got, "title": r.get("title", ""), "exact": exact,
                                                       "reason": "" if exact else "the file is a different version than the package says; it was kept under its own identity"})
    return {"imported": imported, "rejected": rejected}
