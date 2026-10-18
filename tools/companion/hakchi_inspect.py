"""Independent reader of a Hakchi2 CE 3.9.3 portable tree: what collection would Hakchi present, computed from Hakchi's OWN data
(config\\config.json, games_originals\\CLV-P-NA??E\\*.desktop, games\\<code>\\<code>.desktop) without using the bridge's planner, ledger
or writer. Also computes the EXPECTED collection from a schema-3 manifest on its own (comparison status + revision decision + the
headerless-CRC32 code rule applied to the stored payload bytes) and compares the two by stable identifiers (Hakchi game codes).

Hakchi facts this relies on (verified from the 3.9.3 source and the real tree on 2026-10-09):
 - original games are listed from games_originals\\<code>\\<code>.desktop; a code is CHECKED when it is in
   gamesCollectionSettings.NES.OriginalGames (MainForm LoadGames / OriginalGames);
 - custom games are listed from games\\<code>\\ that contain <code>.desktop (a folder without it is deleted at start-up);
   a code is CHECKED when it is in gamesCollectionSettings.NES.SelectedGames;
 - a custom game's code is 'CLV-H-' + five letters from the headerless ROM CRC32 (NesApplication.GenerateCode)."""
from __future__ import annotations
import os, re, json, zlib, sqlite3, hashlib, sys
HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(PROJECT, "tools", "rom-pipeline")); sys.path.insert(0, os.path.join(HERE, "..", "rom-pipeline"))
try:
    from common import SITE_ROOT as REPORTS, DB_PATH, data_path   # noqa: E402
except Exception:   # pragma: no cover
    REPORTS = os.path.join(PROJECT, "reports"); DB_PATH = os.path.join(os.environ.get("NES_WORK") or os.path.join(PROJECT, "work"), "inventory.sqlite")
COLLECTION = "NES"
NES_ORIGINAL = re.compile(r"^CLV-P-NA..E$")   # NES Classic (USA/Europe) factory codes

def desktop_fields(path: str) -> dict:
    out = {}
    for line in open(path, encoding="utf-8", errors="replace"):
        line = line.strip()
        if "=" in line and not line.startswith("["):
            k, v = line.split("=", 1); out.setdefault(k.strip(), v.strip())
    return out

def hakchi_originals(root: str) -> dict:
    """code -> {name, exec} for the 30 NES (USA/Europe) originals, from Hakchi's own desktop entries."""
    d = os.path.join(root, "games_originals"); out = {}
    if not os.path.isdir(d): return out
    for code in sorted(os.listdir(d)):
        if NES_ORIGINAL.match(code) and os.path.isfile(os.path.join(d, code, code + ".desktop")):
            f = desktop_fields(os.path.join(d, code, code + ".desktop")); out[code] = {"name": f.get("Name", ""), "exec": f.get("Exec", ""), "code_field": f.get("Code", "")}
    return out

def hakchi_customs(root: str) -> dict:
    """code -> {name, exec, rom_sha256, managed} for every games\\<code>\\ folder Hakchi would list (desktop present)."""
    d = os.path.join(root, "games"); out = {}
    if not os.path.isdir(d): return out
    for code in sorted(os.listdir(d)):
        p = os.path.join(d, code); desk = os.path.join(p, code + ".desktop")
        if not os.path.isdir(p) or not os.path.isfile(desk): continue
        f = desktop_fields(desk); roms = [x for x in os.listdir(p) if x.lower().endswith(".nes")]
        out[code] = {"name": f.get("Name", ""), "exec": f.get("Exec", ""), "code_field": f.get("Code", ""), "rom_sha256": hashlib.sha256(open(os.path.join(p, roms[0]), "rb").read()).hexdigest() if roms else None,
                     "managed": os.path.isfile(os.path.join(p, "nes-classic-catalog.json"))}
    return out

def effective_collection(root: str) -> dict:
    """What Hakchi would show as checked/listed for the NES collection, read from its own files."""
    cfgp = os.path.join(root, "config", "config.json")
    cfg = json.load(open(cfgp, encoding="utf-8")) if os.path.exists(cfgp) else {}
    coll = (cfg.get("gamesCollectionSettings") or {}).get(COLLECTION) or {}
    orig = hakchi_originals(root); cust = hakchi_customs(root)
    orig_sel = set(coll.get("OriginalGames") or []); cust_sel = set(coll.get("SelectedGames") or [])
    names = {c: v["name"] for c, v in orig.items()}; names.update({c: v["name"] for c, v in cust.items()})
    res = {"config_exists": os.path.exists(cfgp), "config_sha256": hashlib.sha256(open(cfgp, "rb").read()).hexdigest() if os.path.exists(cfgp) else None,
           "originals_listed": sorted(orig), "originals_enabled": sorted(orig_sel & set(orig)), "originals_hidden": sorted(set(orig) - orig_sel),
           "customs_listed": sorted(cust), "customs_enabled": sorted(cust_sel & set(cust)), "customs_managed": sorted(c for c, v in cust.items() if v["managed"]),
           "stale_original_codes": sorted(orig_sel - set(orig)), "stale_selected_codes": sorted(cust_sel - set(cust)), "names": names,
           "update_flags": {"EnableHakchiUpdate": cfg.get("EnableHakchiUpdate"), "EnableCeUpdate": cfg.get("EnableCeUpdate")}}
    res["enabled_codes"] = sorted(res["originals_enabled"] + res["customs_enabled"])
    dup_names = {}
    for c, n in names.items(): dup_names.setdefault(n.strip().lower(), []).append(c)
    res["duplicate_names"] = {n: cs for n, cs in dup_names.items() if len(cs) > 1}
    return res

def code_from_headerless(data: bytes) -> str:
    crc = zlib.crc32(data) & 0xFFFFFFFF
    return "CLV-H-" + "".join(chr(65 + ((crc >> sh) % 26)) for sh in (0, 5, 10, 15, 20))

def expected_from_manifest(man: dict, root: str, db_path: str = DB_PATH, builtin_path: str | None = None) -> dict:
    """Expected Hakchi collection derived from the manifest alone (plus the factory inventory file and the stored payload bytes)."""
    builtin_path = builtin_path or os.path.join(REPORTS, "builtin.json")
    fb = open(builtin_path, "rb").read(); inv = json.loads(fb.decode("utf-8")); codes30 = sorted(b["code"] for b in inv["games"])
    problems = []
    if len(codes30) != 30 or len(set(codes30)) != 30: problems.append(f"factory inventory has {len(codes30)} codes")
    fi = man.get("factory_inventory") or {}
    if fi.get("sha256") != hashlib.sha256(fb).hexdigest(): problems.append("manifest factory inventory fingerprint differs from reports/builtin.json")
    hk = hakchi_originals(root)
    if sorted(hk) != codes30: problems.append(f"Hakchi's original-game records differ from the inventory: only_in_hakchi={sorted(set(hk)-set(codes30))} only_in_inventory={sorted(set(codes30)-set(hk))}")
    visible, additional, seen = set(), {}, {}
    stored = {}
    if os.path.exists(db_path):
        con = sqlite3.connect(db_path); stored = {r[0]: r[1] for r in con.execute("SELECT sha256, stored FROM payloads")}; con.close()
    try:
        from library_lookup import library_store
        for vid, e in library_store().items():   # managed copies of known versions (imported by the user)
            if os.path.exists(e["path"]): stored.setdefault(e["content_sha256"], (e["path"], e.get("format") in ("iNES", "NES2.0")))
    except Exception: pass
    try:
        sys.path.insert(0, os.path.join(PROJECT, "tools", "rom-pipeline")); from library_lookup import personal_index
        for e in personal_index().get("entries", {}).values():   # personal entries: the managed copy; content = bytes after the header when the file is headered
            if e.get("managed_copy") and os.path.exists(e["managed_copy"]): stored.setdefault(e["content_sha256"], (e["managed_copy"], e.get("format") in ("iNES", "NES2.0")))
    except Exception: pass
    games = man.get("games") if man.get("games") is not None else [dict(s, game_id=s.get("game_id") or s.get("version_id")) for s in man.get("selections", [])]
    def content_bytes(p):
        if isinstance(p, tuple): b = open(p[0], "rb").read(); return b[16:] if p[1] else b
        return open(p, "rb").read()
    def present(p): return bool(p) and os.path.exists(p[0] if isinstance(p, tuple) else p)
    for g in games:
        gid = g.get("game_id")
        if gid in seen: problems.append(f"duplicate game id {gid}")
        seen[gid] = 1
        st = g.get("comparison_status"); dec = g.get("revision_decision"); code = g.get("builtin_code")
        if st == "exact-match":
            if not code: problems.append(f"{g.get('display_title')}: exact match without a factory code")
            else: visible.add(code)
            if g.get("install_required"): problems.append(f"{g.get('display_title')}: exact factory match marked install_required")
        elif st in ("different-revision", "identity-only"):
            if dec == "factory":
                visible.add(code)
                if g.get("install_required"): problems.append(f"{g.get('display_title')}: factory retained but install_required")
            elif dec == "alternate":
                if not g.get("install_required"): problems.append(f"{g.get('display_title')}: alternate chosen but not install_required")
                p = stored.get(g.get("payload_sha256"))
                if not present(p): problems.append(f"{g.get('display_title')}: payload not stored"); continue
                additional[code_from_headerless(content_bytes(p))] = gid
            else: problems.append(f"{g.get('display_title')}: unresolved revision decision {dec!r}")
        elif st == "not-built-in":
            if not g.get("install_required"): problems.append(f"{g.get('display_title')}: additional game not marked install_required")
            p = stored.get(g.get("payload_sha256"))
            if not present(p): problems.append(f"{g.get('display_title')}: payload not stored"); continue
            c = code_from_headerless(content_bytes(p))
            if c in additional: problems.append(f"code collision {c}: {additional[c]} and {gid}")
            additional[c] = gid
        else: problems.append(f"{g.get('display_title')}: unknown comparison_status {st!r}")
    unknown = sorted(visible - set(codes30))
    if unknown: problems.append(f"visible factory codes not in the inventory: {unknown}")
    hidden = sorted(set(codes30) - visible)
    declared = sorted(h.get("code") for h in man.get("hidden_factory_games", []))
    if declared != hidden: problems.append(f"manifest hidden list ({len(declared)}) differs from the derived hidden set ({len(hidden)})")
    return {"visible_codes": sorted(visible), "hidden_codes": hidden, "additional_codes": sorted(additional), "additional": additional, "expected_enabled": sorted(visible) + sorted(additional),
            "count": len(games), "problems": problems, "inventory_sha256": hashlib.sha256(fb).hexdigest(), "hakchi_originals": {c: v["name"] for c, v in hk.items()}}

def compare(expected: dict, effective: dict) -> dict:
    want = set(expected["expected_enabled"]); got = set(effective["enabled_codes"])
    return {"match": want == got and not expected["problems"], "missing": sorted(want - got), "unexpected": sorted(got - want), "problems": expected["problems"],
            "counts": {"expected_total": len(want), "expected_factory": len(expected["visible_codes"]), "expected_hidden": len(expected["hidden_codes"]), "expected_additional": len(expected["additional_codes"]),
                       "effective_total": len(got), "effective_factory": len(effective["originals_enabled"]), "effective_hidden": len(effective["originals_hidden"]), "effective_additional": len(effective["customs_enabled"]),
                       "listed_customs": len(effective["customs_listed"]), "duplicate_names": len(effective["duplicate_names"])}}

def ui_rows_to_codes(rows: list, root: str) -> dict:
    """Map the rows read from Hakchi's window ({name, group, checked}) to codes through Hakchi's own desktop entries: originals by name within
    games_originals, customs by name within games\\. Names that map to more than one code in a group are reported as ambiguous, never guessed."""
    orig = hakchi_originals(root); cust = hakchi_customs(root)
    by_o, by_c = {}, {}
    for c, v in orig.items(): by_o.setdefault(v["name"].strip().lower(), []).append(c)
    for c, v in cust.items(): by_c.setdefault(v["name"].strip().lower(), []).append(c)
    listed, checked, ambiguous, unmapped = [], [], [], []
    for r in rows:
        n = (r.get("name") or "").strip().lower(); grp = (r.get("group") or "").lower()
        table = by_o if grp.startswith("original") else by_c
        cs = table.get(n) or []
        if len(cs) == 1: listed.append(cs[0]); (checked.append(cs[0]) if r.get("checked") else None)
        elif len(cs) > 1: ambiguous.append({"name": r.get("name"), "group": r.get("group"), "codes": cs, "checked": bool(r.get("checked"))})
        else: unmapped.append({"name": r.get("name"), "group": r.get("group"), "checked": bool(r.get("checked"))})
    return {"listed_codes": sorted(listed), "checked_codes": sorted(checked), "ambiguous": ambiguous, "unmapped": unmapped}

if __name__ == "__main__":
    import sys
    root = sys.argv[1] if len(sys.argv) > 1 else os.path.join(PROJECT, "hakchi", "hakchi2-ce-3.9.3")
    print(json.dumps({k: v for k, v in effective_collection(root).items() if k != "names"}, indent=1))
