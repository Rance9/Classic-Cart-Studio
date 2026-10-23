r"""Classic Cart Studio collection packages (.ccs): the complete, portable form of a collection, on the website and in the Windows app.

Format (CCS-FORMAT.md beside this file is the specification): a ZIP archive (stored or deflate entries, no encryption; ZIP64 whenever
needed) containing
  ccs-manifest.json   {"format": "classic-cart-studio-package", "schema": "ccs-package/2", "kind": "collection", "created", "app",
                       "summary": {...}, "files": [{"path", "sha256", "size", "kind", ...}], "roms": {"included": [...]}}
  collection.json     the schema-4 collection with the Studio record in "ccs_collection" (every game id in "sel", exact versions,
                      personal entries, loadouts, HOME settings)
  roms/<id>.nes|.unh  exactly one ROM file per game in ccs_collection.sel (exact bytes; .nes with an iNES header, .unh without)
  art/<id>.jpg|.png   thumbnails of the collection's own artwork (personal games, replaced covers)
  README.txt          what the file is and how to open it
Every packaged file is listed in the manifest with its SHA-256 and size; nothing else may be in the archive.

Game identity: a ROM's content is the file without its 16-byte iNES header (and without a 512-byte trainer when the header declares
one); content_sha256 = SHA-256 of the content. A library version id is content_sha256[:16]; a personal id is "p_" + content_sha256[:16].
A package is complete and exact: every game has its ROM and every ROM is the exact version its id names. Placeholder ids (games whose
file is unknown) cannot be packaged. There is no package size, ROM size or game count limit; ROM entries are streamed (hashing while
copying), so multi-gigabyte packages never sit in memory. Only the two JSON files are parsed in memory (bounded at 1 GB each).

Reading treats the package as untrusted input: entry names are checked (no absolute paths, drive letters, backslashes, '.', '..',
control characters; only the known paths), duplicates, links, encryption and unknown compression methods are refused, a compression
ratio above 400:1 is refused (decompression bombs), decompression stops at each entry's declared size, every listed file must be present
and match its size and checksum, and every ROM must be the exact version of its game id. Nothing in a package is ever executed. ROM files
are only stored after the user accepts them, after the WHOLE package has been verified, into the Studio's managed stores (personal.py),
where each file is identified by its own content hash: exact versions only, never a substitute.
This module never uses the network."""
from __future__ import annotations
import hashlib, json, os, re, stat, time, zipfile, zlib

FORMAT = "classic-cart-studio-package"; SCHEMA = "ccs-package/2"; MANIFEST = "ccs-manifest.json"; KIND = "collection"
MEDIA_TYPE = "application/vnd.classiccartstudio.collection+zip"
MAX_JSON = 1024 ** 3          # ccs-manifest.json and collection.json are parsed in memory (shown to people as 1.07 GB: fmt_size)
MAX_RATIO = 400               # decompression-bomb guard (per entry)
CHUNK = 1 << 20
NES_MAGIC = b"NES\x1a"
LIB_ID = re.compile(r"^[0-9a-f]{16}$"); PERSONAL_ID = re.compile(r"^p_[0-9a-f]{16}$")
ROM_RE = re.compile(r"^roms/((?:p_)?[0-9a-f]{16})\.(nes|unh)$"); ART_RE = re.compile(r"^art/[A-Za-z0-9_.-]{1,96}\.(jpg|png)$")
HEX64 = re.compile(r"^[0-9a-f]{64}$")
FIXED = {MANIFEST: "manifest", "collection.json": "collection", "README.txt": "readme"}
README = """Classic Cart Studio collection package (.ccs)

This file holds a complete game collection made with Classic Cart Studio: every game in it with its ROM file (exact versions),
its NES Classic Loadouts, HOME settings and artwork.

Open it with Classic Cart Studio for Windows (double-click the file once the app is installed), or on the website:
https://classiccartstudio.com  ->  My Collections  ->  Import Collection

Classic Cart Studio does not provide or host game files. Whoever shares or receives game files is responsible for having the
rights to copy, distribute and use them.
"""

class PackageError(ValueError):
    """A plain, user-facing reason. `details` carries structured lists (missing, mismatched, unidentified) for the API."""
    def __init__(self, msg: str, **details):
        super().__init__(msg); self.details = details

# ---------------- identity ----------------
def is_identity_id(gid: str) -> bool:
    return isinstance(gid, str) and bool(LIB_ID.match(gid) or PERSONAL_ID.match(gid))

def id_matches(gid: str, content_sha256: str) -> bool:
    """True when the content hash IS the game id (library: content_sha256[:16]; personal: "p_" + content_sha256[:16])."""
    if not isinstance(content_sha256, str) or not HEX64.match(content_sha256): return False
    return gid == content_sha256[:16] or gid == "p_" + content_sha256[:16]

class IdentityHasher:
    """Streams a ROM file's bytes; computes the file SHA-256 and the content SHA-256 (the bytes after the iNES header and trainer),
    exactly as personal.identify_bytes does: a header is present when the file starts with NES<1A> and is at least 16 bytes long."""
    def __init__(self):
        self.file = hashlib.sha256(); self.content = hashlib.sha256(); self.size = 0; self.content_size = 0; self.crc = 0; self.head = b""
        self._pre = bytearray(); self._skip = None; self._pos = 0; self.magic = False
    def update(self, b: bytes):
        if not b: return
        self.file.update(b); self.size += len(b)
        if self._skip is None:
            self._pre += b
            if len(self._pre) < 16: return
            head = self.head = bytes(self._pre[:16]); self.magic = head[:4] == NES_MAGIC
            self._skip = (16 + (512 if head[6] & 0x04 else 0)) if self.magic else 0
            b = bytes(self._pre); self._pre = bytearray()
        self._feed(b)
    def _feed(self, b: bytes):   # b starts at file offset self._pos
        start = self._pos; self._pos += len(b)
        if self._pos > self._skip:
            part = b[max(0, self._skip - start):]; self.content.update(part); self.content_size += len(part); self.crc = zlib.crc32(part, self.crc)
    def finish(self) -> dict:
        if self._skip is None:   # shorter than 16 bytes: never a header (identify_bytes needs 16 bytes)
            self.head = bytes(self._pre[:16]); self.magic = self.head[:4] == NES_MAGIC; self._skip = 0; b = bytes(self._pre); self._pre = bytearray(); self._feed(b)
        return {"sha256": self.file.hexdigest(), "content_sha256": self.content.hexdigest(), "size": self.size, "content_size": self.content_size,
                "headered": self._skip > 0, "magic": self.magic, "content_crc32": self.crc & 0xFFFFFFFF, "head": self.head}

def fmt_size(n: int) -> str:
    """Sizes for people: decimal units from the exact byte count (1 KB = 1,000 B), the same rule as the page's fmt(): bytes below 1,000,
    then KB (1 decimal), MB/GB/TB (2 decimals); a value that would round to 1,000 of a unit is shown in the next one."""
    n = max(0, int(n or 0))
    if n < 1000: return f"{n:,} B"
    units = [("KB", 10**3, 1), ("MB", 10**6, 2), ("GB", 10**9, 2), ("TB", 10**12, 2)]; i = 0
    while i < len(units) - 1 and n >= units[i + 1][1]: i += 1
    if i < len(units) - 1 and round(n / units[i][1], units[i][2]) >= 1000: i += 1
    u, d, p = units[i]; return f"{n / d:,.{p}f}".rstrip("0").rstrip(".") + " " + u

def identify_file(path: str, cancel=lambda: False, progress=None) -> dict:
    h = IdentityHasher()
    with open(path, "rb") as f:
        while True:
            if cancel(): raise PackageError("Export canceled. No file was saved.")
            b = f.read(CHUNK)
            if not b: break
            h.update(b)
            if progress: progress(len(b))
    return h.finish()

def identify_bytes(data: bytes) -> dict:
    h = IdentityHasher(); h.update(data); return h.finish()

def safe_name(n: str) -> bool:
    if not isinstance(n, str) or not n or len(n) > 200 or n.startswith(("/", "\\")) or "\\" in n or ":" in n or any(ord(c) < 32 or ord(c) == 127 for c in n): return False
    if any(p in ("", ".", "..") for p in n.split("/")): return False
    return n in FIXED or bool(ROM_RE.match(n)) or bool(ART_RE.match(n))

def _names(ids: list, titles: dict, n: int = 6) -> str:
    shown = [str(titles.get(i) or i)[:80] for i in ids[:n]]
    return ", ".join(shown) + (f" and {len(ids) - n} more" if len(ids) > n else "")

def _game_ids(collection) -> list:
    cc = collection.get("ccs_collection") if isinstance(collection, dict) else None
    sel = cc.get("sel") if isinstance(cc, dict) else None
    if not isinstance(sel, list) or not all(isinstance(x, str) for x in sel): return None
    seen, out = set(), []
    for x in sel:
        if x not in seen: seen.add(x); out.append(x)
    return out

def _titles(collection: dict, roms: list) -> dict:
    t = {}
    for s in (collection.get("selections") or []) if isinstance(collection, dict) else []:
        if isinstance(s, dict) and s.get("version_id"): t.setdefault(s["version_id"], s.get("title") or s.get("name") or "")
    for r in roms or []:
        if isinstance(r, dict) and r.get("title"): t[r.get("version_id")] = r["title"]
    return {k: v for k, v in t.items() if v}

# ---------------- writing ----------------
def check_complete(collection: dict, roms: list, *, cancel=lambda: False, progress=None, identities: dict | None = None) -> list:
    """Everything write() needs, verified BEFORE anything is written: every game id has an identity, a ROM file on this computer, and that
    file is the exact version. Returns the ordered ROM plan [{version_id, title, path, sha256, size, content_sha256, ext}].
    identities: optional {path: identify_file result} already computed (e.g. by the API's rom check); write() re-hashes every ROM while
    streaming it into the package and refuses the package if a file changed, so a stale cached identity can never produce a wrong package."""
    ids = _game_ids(collection)
    if ids is None: raise PackageError("The collection has no game list, so it can't be packaged.")
    titles = _titles(collection, roms); by = {}
    for r in roms or []:
        if isinstance(r, dict) and isinstance(r.get("version_id"), str): by.setdefault(r["version_id"], r)
    unidentified = [i for i in ids if not is_identity_id(i)]
    missing = [i for i in ids if is_identity_id(i) and not (by.get(i) and by[i].get("path") and os.path.isfile(by[i]["path"]))]
    plan, mismatched = [], []
    if not unidentified and not missing:
        for i in ids:
            p = by[i]["path"]; ident = (identities or {}).get(p)
            if ident and progress: progress(ident["size"])
            ident = ident or identify_file(p, cancel, progress)
            if not id_matches(i, ident["content_sha256"]): mismatched.append(i); continue
            plan.append({"version_id": i, "title": str(by[i].get("title") or titles.get(i) or "")[:120], "path": p, "sha256": ident["sha256"], "size": ident["size"],
                         "content_sha256": ident["content_sha256"], "ext": ".nes" if ident.get("magic", ident["headered"]) else ".unh"})
    det = {"missing": missing, "mismatched": mismatched, "unidentified": unidentified}
    if unidentified: raise PackageError(f"These games don't have a known ROM file, so they can't be packaged: {_names(unidentified, titles)}. Remove them from the selection or locate their files.", **det)
    if missing: raise PackageError(f"The ROM files of these games aren't on this computer: {_names(missing, titles)}. Locate the files or remove the games, then try again.", **det)
    if mismatched: raise PackageError(f"These ROM files are a different version than the game in the collection: {_names(mismatched, titles)}. Locate the exact version or remove the games, then try again.", **det)
    return plan

def write(out_path: str, *, collection: dict, roms: list | None = None, art: dict | None = None, summary: dict | None = None, app_version: str = "",
          rom_compression: int = zipfile.ZIP_DEFLATED, cancel=lambda: False, progress=lambda done, total: None, identities: dict | None = None,
          stage=lambda name: None, rom_done=lambda n, total: None) -> dict:
    """Write a complete package. roms: [{"version_id", "path" (a local file), "title"}] covering every id in collection.ccs_collection.sel.
    Refuses (PackageError naming the games, details: missing/mismatched/unidentified) BEFORE writing anything when a game has no ROM, a
    ROM is a different version than its id, or an id has no content identity. ROM entries are streamed. Written to a temporary file
    next to out_path, then renamed: a cancelled or failed export leaves nothing behind. Returns the manifest.
    stage("check" | "write" | "roms") and rom_done(n, total) report what is happening for a progress display; cancel is checked every 1 MB."""
    if not isinstance(collection, dict): raise PackageError("There is nothing to export. Open a collection, then try again.")
    rom_total = sum(os.path.getsize(r["path"]) for r in roms or [] if isinstance(r, dict) and r.get("path") and os.path.isfile(r["path"]))
    body = json.dumps(collection, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    total = 2 * rom_total + len(body); done = [0]
    def tick(n): done[0] += n; progress(done[0], total)
    stage("check"); plan = check_complete(collection, roms, cancel=cancel, progress=tick, identities=identities)
    if len(body) > MAX_JSON: raise PackageError(f"The collection is too large to package (its description is over {fmt_size(MAX_JSON)}).")
    files, included = [], []; tmp = out_path + ".part"; now = time.localtime()[:6]; stage("write")
    try:
        with zipfile.ZipFile(tmp, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as z:
            z.writestr(_zi("collection.json", now, zipfile.ZIP_DEFLATED), body); files.append({"path": "collection.json", "sha256": _sha(body), "size": len(body), "kind": "collection"}); tick(len(body))
            for name, data in sorted((art or {}).items()):
                n = "art/" + str(name)
                if not ART_RE.match(n) or not isinstance(data, (bytes, bytearray)): continue
                z.writestr(_zi(n, now, zipfile.ZIP_STORED), bytes(data)); files.append({"path": n, "sha256": _sha(data), "size": len(data), "kind": "art"})
            stage("roms")
            for r in plan:
                if cancel(): raise PackageError("Export canceled. No file was saved.")
                n = f"roms/{r['version_id']}{r['ext']}"
                method, check = (zipfile.ZIP_STORED, False) if rom_compression == zipfile.ZIP_STORED else _rom_method(r["path"], r["size"], cancel)
                start = z.fp.tell(); got = _write_rom(z, n, now, method, r, cancel, tick)
                zi = z.filelist[-1]
                if check and zi.compress_size and zi.file_size / zi.compress_size > MAX_RATIO / 2:
                    # compressed further than readers accept (mostly padding that the samples missed): take the entry back, store it
                    # (progress already counted these bytes once; it never moves backward, so the second write is not counted again)
                    z.fp.seek(start); z.fp.truncate(); z.start_dir = start; z.filelist.pop(); z.NameToInfo.pop(n, None)
                    got = _write_rom(z, n, now, zipfile.ZIP_STORED, r, cancel, lambda k: None)
                if got["sha256"] != r["sha256"] or got["size"] != r["size"]: raise PackageError(f"The ROM file of {r['title'] or r['version_id']} changed while the package was being made. Nothing was saved. Try again.")
                rec = {"path": n, "sha256": r["sha256"], "size": r["size"], "kind": "rom", "version_id": r["version_id"], "title": r["title"], "content_sha256": r["content_sha256"]}
                files.append(rec); included.append({k: rec[k] for k in ("version_id", "title", "path", "sha256", "size", "content_sha256")}); rom_done(len(included), len(plan))
            readme = README.encode("utf-8")
            z.writestr(_zi("README.txt", now, zipfile.ZIP_DEFLATED), readme); files.append({"path": "README.txt", "sha256": _sha(readme), "size": len(readme), "kind": "readme"})
            summ = dict(summary or {}); summ.update(games=len(plan), roms=len(included), rom_bytes=sum(r["size"] for r in included))
            summ.setdefault("name", str((collection.get("ccs_collection") or {}).get("name") or collection.get("name") or "")[:200])
            summ.setdefault("personal", sum(1 for r in plan if r["version_id"].startswith("p_")))
            man = {"format": FORMAT, "schema": SCHEMA, "kind": KIND, "created": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                   "app": {"name": "Classic Cart Studio", "version": app_version}, "summary": summ, "files": files, "roms": {"included": included}}
            z.writestr(_zi(MANIFEST, now, zipfile.ZIP_DEFLATED), json.dumps(man, ensure_ascii=False, indent=1).encode("utf-8"))
        os.replace(tmp, out_path); return man
    except BaseException:
        try: os.remove(tmp)
        except OSError: pass
        raise

def _write_rom(z: zipfile.ZipFile, name: str, now, method: int, r: dict, cancel, tick) -> dict:
    """One ROM entry, streamed 1 MB at a time and hashed as it is written; returns its identity."""
    zi = _zi(name, now, method); zi.file_size = r["size"]; h = IdentityHasher()
    with open(r["path"], "rb") as f, z.open(zi, "w", force_zip64=r["size"] > 0x7FFFFFFF) as w:
        while True:
            if cancel(): raise PackageError("Export canceled. No file was saved.")
            b = f.read(CHUNK)
            if not b: break
            h.update(b); w.write(b); tick(len(b))
    return h.finish()

SAMPLE = 256 * 1024
def _rom_method(path: str, size: int, cancel=lambda: False) -> tuple[int, bool]:
    """How to store one ROM, from three 256 KB samples (start, middle, end) compressed once: (method, check the result afterwards).
    A ROM the samples cannot shrink by 3 % is stored: compressing it would cost most of the export time and save nothing. An ordinary ROM
    is compressed while it is written (one pass), and the written entry's ratio is checked. Samples that shrink more than 20:1 suggest a
    mostly padded image, so the whole file is measured first (_deflate_ok), as every ROM was before."""
    if not size: return zipfile.ZIP_STORED, False
    seen = got = 0
    with open(path, "rb") as f:
        for o in sorted({0, max(0, (size - SAMPLE) // 2), max(0, size - SAMPLE)}):
            f.seek(o); b = f.read(SAMPLE); seen += len(b); got += len(zlib.compress(b, 6))
    ratio = got / max(1, seen)
    if ratio >= 0.97: return zipfile.ZIP_STORED, False
    if ratio < 0.05: return (zipfile.ZIP_DEFLATED if _deflate_ok(path, cancel) else zipfile.ZIP_STORED), False
    return zipfile.ZIP_DEFLATED, True

def _deflate_ok(path: str, cancel=lambda: False) -> bool:
    """A ROM that deflates past half the readers' bomb ratio (e.g. a mostly-padded image) is stored instead, so every package this
    writer makes is one every reader accepts. One streaming pass; nothing is kept."""
    import zlib
    c = zlib.compressobj(6, zlib.DEFLATED, -15); size = out = 0
    with open(path, "rb") as f:
        while True:
            if cancel(): raise PackageError("Export canceled. No file was saved.")
            b = f.read(CHUNK)
            if not b: break
            size += len(b); out += len(c.compress(b))
    out += len(c.flush())
    return not size or size / max(1, out) <= MAX_RATIO / 2

def _zi(name: str, now, method: int) -> zipfile.ZipInfo:
    zi = zipfile.ZipInfo(name, date_time=now); zi.compress_type = method; zi.external_attr = 0o644 << 16
    if method == zipfile.ZIP_DEFLATED: zi.compress_level = 6
    return zi

def _sha(b: bytes) -> str: return hashlib.sha256(b).hexdigest()

# ---------------- reading ----------------
def _stream(z: zipfile.ZipFile, info: zipfile.ZipInfo, sink, cancel=lambda: False):
    """Decompress one entry into sink(chunk), refusing more data than it declares (a decompression bomb stops at the declared size).
    Returns the number of bytes, or raises PackageError / zipfile errors for damage."""
    n = 0
    with z.open(info) as f:
        while True:
            if cancel(): raise PackageError("Canceled. Nothing was imported.")
            b = f.read(min(CHUNK, info.file_size - n + 1))
            if not b: break
            n += len(b)
            if n > info.file_size: raise PackageError(f"The package contains an entry with more data than it declares ({info.filename[:60]}).")
            sink(b)
    if n != info.file_size: raise PackageError(f"The package contains an entry with less data than it declares ({info.filename[:60]}).")
    return n

def _read_json_entry(z, info, what: str):
    if info.file_size > MAX_JSON: raise PackageError(f"The package's {what} is larger than {fmt_size(MAX_JSON)}, so it can't be opened.")
    buf = bytearray(); _stream(z, info, buf.extend); return bytes(buf)

def _verify_rom(z, info, cancel=lambda: False) -> dict:
    h = IdentityHasher(); _stream(z, info, h.update, cancel); return h.finish()

_DAMAGE = (zipfile.BadZipFile, zlib.error, EOFError, OSError)

def inspect(path: str, *, verify_roms: bool = True, cancel=lambda: False, progress=None) -> dict:
    """Validate a package completely and describe it (never extracts anything). Raises PackageError with a plain reason for anything that
    makes the package unusable as a whole; per-file damage (missing listed file, size or checksum difference, a ROM that is not the version
    its id names) is returned in "problems" with ok False. verify_roms=False checks everything except reading the ROM bytes (the listing,
    completeness, entry presence and sizes are still checked); those ROMs have verified None."""
    try: size = os.path.getsize(path)
    except OSError: raise PackageError("The file could not be read. Check that it still exists, then try again.")
    try: z = zipfile.ZipFile(path)
    except (zipfile.BadZipFile, OSError, ValueError, EOFError) as e: raise PackageError("This file is not a Classic Cart Studio package, or it is damaged.") from e
    with z:
        infos = z.infolist(); names = [i.filename for i in infos]
        if len(set(names)) != len(names): raise PackageError("The package has duplicate entries.")
        for i in infos:
            if i.is_dir(): raise PackageError(f"The package contains an unexpected folder entry ({i.filename[:60]}).")
            mode = (i.external_attr >> 16) & 0xFFFF
            if mode and stat.S_ISLNK(mode): raise PackageError(f"The package contains a link, which is not allowed ({i.filename[:60]}).")
            if i.flag_bits & 0x1: raise PackageError("Encrypted packages are not supported.")
            if i.compress_type not in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED): raise PackageError("The package uses an unsupported compression method.")
            if i.file_size and (i.compress_size == 0 or i.file_size / i.compress_size > MAX_RATIO): raise PackageError(f"The package contains an entry with a suspicious compression ratio ({i.filename[:60]}).")
            if i.compress_type == zipfile.ZIP_STORED and i.compress_size != i.file_size: raise PackageError(f"The package contains an entry with less or more data than it declares ({i.filename[:60]}).")
        by = {i.filename: i for i in infos}
        if MANIFEST not in by: raise PackageError("This is not a Classic Cart Studio package.")
        try: man = json.loads(_read_json_entry(z, by[MANIFEST], "contents list").decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as e: raise PackageError("The package contents list is damaged.") from e
        except _DAMAGE as e: raise PackageError("The package contents list is damaged.") from e
        if not isinstance(man, dict) or man.get("format") != FORMAT: raise PackageError("This is not a Classic Cart Studio package.")
        sch = man.get("schema"); m = re.fullmatch(r"ccs-package/(\d{1,6})", sch) if isinstance(sch, str) else None
        if not m: raise PackageError("The package contents list is damaged.")
        if int(m.group(1)) == 1: raise PackageError("This .ccs file uses an older format that doesn't always include its ROM files, so it can't be opened. Ask for a new .ccs file.")
        if int(m.group(1)) > 2: raise PackageError("This package was made by a newer version of Classic Cart Studio. Update the app to open it.")
        if man.get("kind") == "backup": raise PackageError("This .ccs file is an old Studio backup, not a collection package, so it can't be opened.")
        if man.get("kind") != KIND: raise PackageError("This .ccs file is not a collection package, so it can't be opened.")
        for i in infos:
            if not safe_name(i.filename): raise PackageError(f"The package contains an unsafe or unexpected entry ({i.filename[:60]}).")
        listed = man.get("files"); rl = man.get("roms")
        if not isinstance(listed, list) or not all(isinstance(f, dict) for f in listed): raise PackageError("The package contents list is damaged.")
        if not isinstance(rl, dict) or not isinstance(rl.get("included"), list) or not all(isinstance(r, dict) for r in rl["included"]): raise PackageError("The package contents list is damaged.")
        if rl.get("missing"): raise PackageError("This package leaves out some ROM files, so it can't be opened. Ask for a new .ccs file.")
        lp = [f.get("path") for f in listed]
        if len(set(lp)) != len(lp): raise PackageError("The package contents list names a file twice.")
        for f in listed:
            p, sha, sz = f.get("path"), f.get("sha256"), f.get("size")
            if not isinstance(p, str) or not safe_name(p) or p == MANIFEST: raise PackageError("The package contents list names an unsafe file.")
            if not (isinstance(sha, str) and HEX64.match(sha)) or not isinstance(sz, int) or isinstance(sz, bool) or sz < 0: raise PackageError(f"The package contents list has an invalid checksum or size ({p[:60]}).")
        extra = set(names) - set(lp) - {MANIFEST}
        if extra: raise PackageError(f"The package contains files its contents list does not name ({sorted(extra)[0][:60]}).")
        for req in ("collection.json", "README.txt"):
            if req not in lp: raise PackageError("The package contents list is incomplete (it does not name " + req + ").")
        # ---- the ROM listing: one per game, exact identity ----
        lmap = {f["path"]: f for f in listed}; inc_ids = []; rom_recs = []
        for r in rl["included"]:
            vid, p = r.get("version_id"), r.get("path"); mm = ROM_RE.match(p) if isinstance(p, str) else None
            if not mm or mm.group(1) != vid: raise PackageError("The package contents list is damaged (a ROM file is listed under the wrong game).")
            f = lmap.get(p)
            if not f or f.get("sha256") != r.get("sha256") or f.get("size") != r.get("size") or (f.get("content_sha256") not in (None, r.get("content_sha256"))): raise PackageError("The package contents list is damaged (its ROM list and file list disagree).")
            if not id_matches(vid, r.get("content_sha256")): raise PackageError(f"The package lists a ROM file that is not the version its game names ({str(r.get('title') or vid)[:60]}), so it can't be opened.")
            inc_ids.append(vid); rom_recs.append(r)
        if len(set(inc_ids)) != len(inc_ids): raise PackageError("The package contents list names a game's ROM file twice.")
        listed_roms = {f["path"] for f in listed if f["path"].startswith("roms/")}
        if listed_roms != {r["path"] for r in rom_recs}: raise PackageError("The package contains ROM files that its game list does not account for.")
        # ---- integrity of every listed file ----
        problems, roms, main = [], [], None; total_rom = sum(r["size"] for r in rom_recs)
        for f in listed:
            p, sha, sz = f["path"], f["sha256"], f["size"]
            if p not in by: problems.append({"path": p, "problem": "listed but missing from the package"}); continue
            if by[p].file_size != sz: problems.append({"path": p, "problem": "size differs from the listed size"}); continue
            if p.startswith("roms/"): continue
            try:
                if p == "collection.json":
                    data = _read_json_entry(z, by[p], "collection")
                    if _sha(data) != sha: problems.append({"path": p, "problem": "checksum does not match (damaged or altered)"}); continue
                    try: main = json.loads(data.decode("utf-8"))
                    except (UnicodeDecodeError, json.JSONDecodeError): raise PackageError("The collection inside the package is not valid.")
                else:
                    h = hashlib.sha256(); _stream(z, by[p], h.update, cancel)
                    if h.hexdigest() != sha: problems.append({"path": p, "problem": "checksum does not match (damaged or altered)"})
            except _DAMAGE: problems.append({"path": p, "problem": "damaged (could not be read)"})
        if main is None:
            if problems: raise PackageError("The collection inside the package is damaged, so nothing can be imported.")
            raise PackageError("The package holds no collection.")
        ids = _game_ids(main)
        if ids is None: raise PackageError("The collection inside the package has no game list.")
        unidentified = [i for i in ids if not is_identity_id(i)]
        if unidentified: raise PackageError(f"The package includes games without a known ROM file ({', '.join(x[:40] for x in unidentified[:3])}), so it can't be opened.")
        if set(ids) != set(inc_ids): raise PackageError("This package doesn't include a ROM file for every game in it, so it can't be opened. Ask for a new .ccs file.")
        done = 0
        for r in rom_recs:
            rec = {k: r.get(k) for k in ("version_id", "title", "path", "sha256", "size", "content_sha256")}
            if r["path"] not in by or by[r["path"]].file_size != r["size"]: roms.append(dict(rec, verified=False)); continue
            if not verify_roms: roms.append(dict(rec, verified=None)); continue
            try: got = _verify_rom(z, by[r["path"]], cancel)
            except _DAMAGE: problems.append({"path": r["path"], "problem": "damaged (could not be read)"}); roms.append(dict(rec, verified=False)); continue
            done += r["size"]
            if progress: progress(done, total_rom)
            if got["sha256"] != r["sha256"]: problems.append({"path": r["path"], "problem": "checksum does not match (damaged or altered)"}); roms.append(dict(rec, verified=False)); continue
            if got["content_sha256"] != r["content_sha256"]: problems.append({"path": r["path"], "problem": "the ROM is not the version its game names"}); roms.append(dict(rec, verified=False)); continue
            roms.append(dict(rec, verified=True))
        order = {v: n for n, v in enumerate(ids)}; roms.sort(key=lambda r: order.get(r["version_id"], 0))
        return {"manifest": man, "kind": KIND, "data": main, "roms": roms, "problems": problems, "size": size, "ok": not problems,
                "rom_count": len(roms), "rom_bytes": sum(r["size"] for r in roms), "games": len(ids), "art": sorted(p for p in lp if p.startswith("art/"))}

MAX_ART = 1 << 20      # art entries are thumbnails; larger ones are left unused (the website reader's limit too)
PERSONAL_RE = re.compile(r"^p_[0-9a-f]{16}$")
_ROM_NAME = re.compile(r"^(p_)?[0-9a-f]{16}(\.(nes|unh|bin|fds|zip))?$", re.I)
def generic_text(v: str) -> bool:
    """an automatic value, not a description: blank, a game id, a package ROM file name, a ROM file name, 'Personal game'"""
    s = str(v or "").strip(); return not s or bool(_ROM_NAME.match(s)) or s.lower() in ("personal game", "unknown") or bool(re.search(r"\.(nes|unh|bin|fds|zip)$", s, re.I))

def package_metadata(data: dict, manifest: dict | None = None) -> dict:
    """The descriptive details a package carries for each personal game (untrusted text; personal.merge_metadata cleans and bounds it),
    field by field from the first location that has a real value: personal_library[].user, ccs_collection.coll.missing, selections,
    personal_library[] itself, then the ROM listing's title. Automatic values (ids, file names) never count. {id: {field: text}}"""
    d = data if isinstance(data, dict) else {}; cc = d.get("ccs_collection") if isinstance(d.get("ccs_collection"), dict) else {}
    coll = cc.get("coll") if isinstance(cc.get("coll"), dict) else {}; miss = coll.get("missing") if isinstance(coll.get("missing"), dict) else {}
    pl = {p["id"]: p for p in d.get("personal_library") or [] if isinstance(p, dict) and isinstance(p.get("id"), str)}
    sels = {}
    for s in d.get("selections") or []:
        if isinstance(s, dict): sels.setdefault(str(s.get("version_id") or s.get("game_id") or ""), s)
    rt = {}
    for r in ((manifest or {}).get("roms") or {}).get("included") or [] if isinstance((manifest or {}).get("roms"), dict) else []:
        if isinstance(r, dict) and isinstance(r.get("version_id"), str): rt[r["version_id"]] = r.get("title")
    ids = [i for i in (_game_ids(d) or list(sels)) if PERSONAL_RE.match(i)]
    out = {}
    for i in ids:
        p = pl.get(i) or {}; u = p.get("user") if isinstance(p.get("user"), dict) else {}; m = miss.get(i) if isinstance(miss.get(i), dict) else {}; s = sels.get(i) or {}
        cand = {"title": (u.get("title"), m.get("title"), s.get("display_title"), s.get("title"), p.get("title"), rt.get(i)),
                "version_label": (u.get("version_label"), m.get("version_label"), m.get("version"), s.get("version_label"), s.get("nointro_name"), p.get("version_label")),
                "description": (u.get("description"), m.get("description"), s.get("description")),
                "year": (u.get("year"), m.get("year"), s.get("year")), "publisher": (u.get("publisher"), m.get("publisher"), s.get("publisher"))}
        rec = {}
        for f, vs in cand.items():
            for v in vs:
                if isinstance(v, (str, int)) and not isinstance(v, bool) and not generic_text(str(v)) and str(v).strip() != i: rec[f] = str(v); break
        out[i] = rec
    return out

def art_entry(z: zipfile.ZipFile, gid: str) -> bytes | None:
    """a package's artwork for one game (art/<id>.jpg or .png), at most MAX_ART bytes; None when absent or too large"""
    for ext in (".jpg", ".png"):
        try: info = z.getinfo("art/" + gid + ext)
        except KeyError: continue
        if info.file_size > MAX_ART: return None
        buf = bytearray(); _stream(z, info, buf.extend); return bytes(buf)
    return None

def import_roms(path: str, accept: list[str] | None = None, *, ingest=None, cancel=lambda: False, progress=None, merge: bool = True) -> dict:
    """After the user accepted: verify the whole package again, then hand each accepted, verified ROM file to the Studio's managed stores
    (personal.py identifies it by its own content; a known version gets the library copy, anything else a personal entry). Files already
    there are reused (deduplicated by content hash), never overwritten. Returns {"imported": [{version_id, stored_as, ...}], "rejected": [...]}"""
    info = inspect(path, verify_roms=True, cancel=cancel)
    if not info["ok"]: raise PackageError("Nothing was imported: the package failed its integrity check (" + "; ".join(f"{p['path']}: {p['problem']}" for p in info["problems"][:3]) + ").")
    ps = None
    if ingest is None:   # the Studio's stores: each ROM is copied to a temporary file while it is checked (memory stays bounded)
        import personal as ps
    want = set(accept) if accept is not None else None; imported, rejected = [], []; done = 0
    total = sum(r["size"] for r in info["roms"] if want is None or r["version_id"] in want)
    src, note = "package:" + os.path.basename(path), "from a Classic Cart Studio package"
    meta = package_metadata(info["data"], info["manifest"]) if merge and ps is not None else {}; merged = []
    with zipfile.ZipFile(path) as z:
        for r in info["roms"]:
            vid = r["version_id"]; fname = os.path.basename(r["path"])
            if want is not None and vid not in want: continue
            bad = lambda reason, vid=vid, r=r: rejected.append({"version_id": vid, "title": r.get("title", ""), "reason": reason})
            if ps is not None:
                if cancel(): raise PackageError("Canceled. Nothing more was imported.")
                zi = z.getinfo(r["path"])
                try: tmp, ident = ps.spool(lambda zi=zi: z.open(zi), zi.file_size, fname, cancel)
                except ValueError as e:
                    if "free space" in str(e) or str(e) == "Canceled.": raise PackageError(str(e) if "free space" in str(e) else "Canceled. Nothing more was imported.")
                    bad(str(e)[:200]); continue
                if ident["file_sha256"] != r["sha256"] or not id_matches(vid, ident["content_sha256"]): ps._rm(tmp); bad("The file is damaged or altered (checksum does not match)."); continue
                try: res = ps.adopt(tmp, ident, fname, src, note, "package")
                except ValueError as e: bad(str(e)[:200]); continue
            else:
                buf = bytearray(); h = IdentityHasher()
                def sink(b, buf=buf, h=h): buf.extend(b); h.update(b)
                try: _stream(z, z.getinfo(r["path"]), sink, cancel)
                except _DAMAGE: bad("The file is damaged (it could not be read)."); continue
                got = h.finish()
                if got["sha256"] != r["sha256"] or not id_matches(vid, got["content_sha256"]): bad("The file is damaged or altered (checksum does not match)."); continue
                try: res = ingest(bytes(buf), fname, src, note, "package")
                except ValueError as e: bad(str(e)[:200]); continue
            got_id = (res.get("known") or {}).get("version_id") or (res.get("entry") or {}).get("id") or ""
            exact = got_id.removeprefix("p_") == vid.removeprefix("p_") and got_id != ""
            (imported if exact else rejected).append({"version_id": vid, "stored_as": got_id, "title": r.get("title", ""), "exact": exact,
                                                      "reason": "" if exact else "This file was stored under a different identity than the package says, so it was kept as its own game."})
            if exact and merge and ps is not None and PERSONAL_RE.match(got_id):   # the package's details and artwork for a personal game (D-1)
                try:
                    mr = ps.merge_metadata(got_id, meta.get(vid) or {}, art=art_entry(z, vid))
                    if mr["changed"]: merged.append({"version_id": vid, "fields": mr["changed"]})
                except Exception as e: merged.append({"version_id": vid, "fields": [], "problem": str(e)[:200]})   # the ROM is imported either way; the page keeps the details and tries again
            done += r["size"]
            if progress: progress(done, total)
    return {"imported": imported, "rejected": rejected, "merged": merged}
