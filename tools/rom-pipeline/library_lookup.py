"""Resolve any version (curated, Extended Library) or personal entry into the row shape the planner and importer use.

Curated versions come from reports/catalog.json unchanged. Extended versions are resolved from inventory.sqlite (payload store path,
evidence-based header) plus reports/library.json (compatibility, input, title) — the same facts the index was built from. Personal
entries come from reports/personal/index.json (written by the companion): a managed copy of the user's file; if that file is a headered
.nes its own header is used as-is, otherwise it cannot be prepared (no evidence-based header) and the row says so."""
from __future__ import annotations
import os, sys, json, sqlite3, hashlib, functools
sys.path.insert(0, os.path.dirname(__file__))
from common import REPORTS, DB_PATH, SITE_ROOT, DATA_ROOT, site_path, data_path
from catalog import KACHI_CMD, RA_CMD

PERSONAL_INDEX = os.path.join(os.environ.get("NES_PERSONAL_DIR") or data_path("personal"), "index.json")   # tests isolate the personal store through NES_PERSONAL_DIR
LIBRARY_STORE = os.path.join(os.environ.get("NES_PERSONAL_DIR") or data_path("personal"), "library-store.json")   # managed copies of imported files whose bytes are known versions

@functools.lru_cache(maxsize=1)
def _library():
    p = site_path("library.json")
    if not os.path.exists(p): return {"titles": {}, "versions": {}, "payloads": {}}
    lib = json.load(open(p, encoding="utf-8"))
    return {"titles": {t["id"]: t for t in lib["titles"]}, "versions": {v["id"]: v for v in lib["versions"]}, "payloads": lib["payloads"]}

@functools.lru_cache(maxsize=1)
def _catalog():
    cat = json.load(open(site_path("catalog.json"), encoding="utf-8"))["games"]
    return {g["game_id"]: g for g in cat}

def title_id_for(version_id: str) -> str:
    """Logical title of a version (catalog rows carry no title_id of their own; the index does)."""
    v = _library()["versions"].get(version_id); return v["title_id"] if v else ""

def personal_index() -> dict:
    return json.load(open(PERSONAL_INDEX, encoding="utf-8")) if os.path.exists(PERSONAL_INDEX) else {"entries": {}}

def resolve(version_id: str) -> dict | None:
    """Catalog-shaped row for a version id (16 hex) or a personal entry id ('p_' + 16 hex of the content hash)."""
    cat = _catalog()
    if version_id in cat:
        row = dict(cat[version_id], source="catalog")
        if not os.path.exists(DB_PATH):   # packaged companion: the curated row's bytes must come from the user's own imported copy
            ls = library_store().get(version_id); row["stored"] = ls["path"] if ls and os.path.exists(ls["path"]) else ""; row["headered_file"] = bool(ls and ls.get("format") in ("iNES", "NES2.0")); row["file_missing"] = not row["stored"]
            if not row["stored"]:
                lib = _library(); pl = lib["payloads"].get(row["payload_sha256"]) or {}; row["header_hex"] = (pl.get("header") or {}).get("hex") or ""
        return row
    if version_id.startswith("p_"):
        e = personal_index()["entries"].get(version_id)
        return _personal_row(e) if e else None
    lib = _library(); v = lib["versions"].get(version_id)
    if not v: return None
    i, p = {}, None
    if os.path.exists(DB_PATH):   # development checkout: pipeline payload store + identification rows
        con = sqlite3.connect(DB_PATH); con.row_factory = sqlite3.Row
        p = con.execute("SELECT * FROM payloads WHERE sha256=?", (v["sha256"],)).fetchone(); i = con.execute("SELECT * FROM ident WHERE sha256=?", (v["sha256"],)).fetchone(); con.close(); i = dict(i) if i else {}
    pl = lib["payloads"].get(v["sha256"]) or {}; hdr = (pl.get("header") or {})
    if not i: i = {"converted_sha256": pl.get("converted_sha256"), "mapper": hdr.get("mapper"), "prg": hdr.get("prg"), "chr": hdr.get("chr"), "battery": hdr.get("battery"), "header_hex": hdr.get("hex")}
    t = lib["titles"].get(v["title_id"], {})
    headered_file = False; stored = p["stored"] if p else None
    if not stored:   # packaged companion / no payload store: a managed copy the user imported (Add Games from Files)
        ls = library_store().get(version_id)
        if ls and os.path.exists(ls["path"]): stored = ls["path"]; headered_file = ls.get("format") in ("iNES", "NES2.0")
    if not stored and not i.get("header_hex"): return None
    return {"game_id": v["id"], "display_title": t.get("title") or v["title"], "nointro_name": v["name"], "payload_sha256": v["sha256"], "payload_size": v["size"], "converted_sha256": i.get("converted_sha256") or "",
            "recommended_emulator": v["emulator"], "core": v.get("core") or "", "launch_command": v.get("launch_command") or (RA_CMD if v["emulator"] == "RetroArch" else KACHI_CMD), "builtin_status": v.get("builtin_status") or "NOT BUILT-IN", "builtin_code": v.get("builtin_code") or "",
            "builtin_factory_version": "", "players": "", "simultaneous": "", "year": v.get("year") or "", "publisher": v.get("publisher") or "", "art": v.get("art") or "", "mapper": i.get("mapper"), "prg": i.get("prg"), "chr": i.get("chr"), "battery": i.get("battery"),
            "input_level": v.get("input_level") or "unknown", "input_special_accessory": v.get("input_accessory") or "", "input_nes_classic_support": v.get("input_support") or "unknown", "input_accessory_requirement": v.get("input_requirement") or "unknown",
            "input_standard_controller_playable": "true" if v.get("input_level") == "none" else "unknown", "input_modes": v.get("input_modes") or ["standard-controller"], "input_primary": "standard-controller",
            "header_hex": i.get("header_hex") or "", "stored": stored or "", "headered_file": headered_file, "file_missing": not stored, "source": "catalog" if v.get("curated") else "extended", "title_id": v["title_id"], "identity": v["identity"], "kind": v["kind"]}

def library_store() -> dict:
    return json.load(open(LIBRARY_STORE, encoding="utf-8")) if os.path.exists(LIBRARY_STORE) else {}

def _personal_row(e: dict) -> dict:
    headered = e.get("format") in ("iNES", "NES2.0")
    return {"game_id": e["id"], "display_title": e.get("user", {}).get("title") or e.get("detected", {}).get("title") or e.get("file_name", "Personal game"), "nointro_name": e.get("user", {}).get("version_label") or e.get("file_name", ""),
            "payload_sha256": e["content_sha256"], "payload_size": e["content_size"], "converted_sha256": e["file_sha256"] if headered else "", "recommended_emulator": e.get("detected", {}).get("emulator") or "unknown", "core": e.get("detected", {}).get("core") or "",
            "launch_command": e.get("detected", {}).get("launch_command") or "", "builtin_status": "NOT BUILT-IN", "builtin_code": "", "builtin_factory_version": "", "players": "", "simultaneous": "", "year": e.get("user", {}).get("year") or "", "publisher": e.get("user", {}).get("publisher") or "",
            "art": e.get("user", {}).get("art") or "", "mapper": e.get("detected", {}).get("mapper"), "prg": e.get("detected", {}).get("prg"), "chr": e.get("detected", {}).get("chr"), "battery": e.get("detected", {}).get("battery"),
            "input_level": "unknown", "input_special_accessory": "", "input_nes_classic_support": "unknown", "input_accessory_requirement": "unknown", "input_standard_controller_playable": "unknown", "input_modes": ["standard-controller"], "input_primary": "standard-controller",
            "header_hex": e.get("detected", {}).get("header_hex") or "", "stored": e.get("managed_copy") or "", "headered_file": headered, "source": "personal", "title_id": e.get("title_id") or "", "identity": e.get("identity") or "personal", "kind": "personal",
            "file_missing": not (e.get("managed_copy") and os.path.exists(e["managed_copy"]))}
