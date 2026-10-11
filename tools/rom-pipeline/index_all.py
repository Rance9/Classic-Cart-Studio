"""Stage D — the complete logical index: every payload in the source inventory becomes a VERSION of a logical TITLE.

Inputs (all local, read-only): <WORK>\\inventory.sqlite (archives, payloads, ident), reports\\catalog.json (the 773 curated
representatives, whose group rule this stage reproduces exactly so curated titles map 1:1), reports\\builtin.json, the Mesen DB
(expansion-device codes), input overrides, libretro genre/year dats, reports\\art\\index.json.
Outputs: reports\\library.json (titles + versions + payload facts, compact; no file paths, no ROM bytes), reports\\inventory-quality.md/.json.

Identity rules (docs/studio-ux-spec.md §4): exact identity = SHA-256 of the source bytes; content identity = SHA-256 of the headerless
PRG+CHR (equal for No-Intro headerless files; what a headered .nes reduces to); a content match is "probable equivalence", never
"byte-identical". Title grouping = normalised title + publisher (when known) + licensed class — the curated catalog's rule — so no
title is merged on text alone. Unknown stays unknown: confidence is recorded per version (verified / cartridge-db / filename)."""
from __future__ import annotations
import os, sys, json, sqlite3, hashlib, time, re
from collections import defaultdict, Counter
sys.path.insert(0, os.path.dirname(__file__))
from common import *
from catalog import norm_title, rev_key, compat, nonretail_reasons, bracket_reasons, load_dat, GENRE_MAP
from input_requirements import classify as classify_input, load_overrides as load_input_overrides

LOGFILE = os.path.join(LOGS, "index_all.log")
KIND_RULES = [("prototype", ("Proto", "Possible Proto")), ("beta", ("Beta",)), ("sample", ("Sample", "Demo", "Kiosk", "Test Program", "Program", "Competition Cart")),
              ("pirate", ("Pirate",)), ("unlicensed", ("Unl", "Unlicensed")), ("translation", ("T-En", "T-Fr", "T-De", "T-Es", "T-It", "T-Ru", "T-Pt", "T-Zh")), ("hack", ("Hack",)), ("alternate", ("Alt",))]

def version_kind(flags: list[str], name: str) -> str:
    fl = set(flags)
    for kind, tags in KIND_RULES:
        if any(f == t or f.startswith(t + " ") for f in fl for t in tags): return kind
    if any(f.startswith(p) for f in fl for p in RERELEASE_PREFIX): return "rerelease"
    if any(f.startswith("Beta") or f.startswith("Proto") for f in fl): return "prototype"
    if re.search(r"\[(b|h|p|t|f|o|a)\d*\]", name): return "alternate"   # GoodTools-style brackets, if any slipped in
    return "retail"

def licensed_class(flags: list[str], cart_class: str) -> str:
    return "unlicensed" if any(f.strip().lower() in ("unl", "unlicensed", "pirate") for f in flags) or cart_class == "Unlicensed" else "licensed"

def publisher_key(row: dict, flags: list[str]) -> str:
    pub = (row.get("cart_publisher") or "").strip().lower()
    if not pub:
        tag = next((f for f in flags if f in ("Tengen", "Namco", "Jaleco", "Acclaim", "Color Dreams", "ShareData", "Hudson", "Kemco", "Sunsoft", "HAL Laboratory")), "")
        pub = tag.lower()
    return pub

def main():
    ensure_dirs(); t0 = time.time()
    con = sqlite3.connect(DB_PATH); con.row_factory = sqlite3.Row
    arch = {r["relpath"]: dict(r) for r in con.execute("SELECT * FROM archives")}
    pays = [dict(r) for r in con.execute("SELECT * FROM payloads ORDER BY relpath, entry")]
    ident = {r["sha256"]: dict(r) for r in con.execute("SELECT * FROM ident")}
    cat = json.load(open(os.path.join(REPORTS, "catalog.json"), encoding="utf-8")); curated = {g["payload_sha256"]: g for g in cat["games"]}
    builtin = json.load(open(os.path.join(REPORTS, "builtin.json"), encoding="utf-8"))["games"]; by_builtin_sha = {b["nointro_gamedata_sha256"]: b for b in builtin if b.get("nointro_gamedata_sha256")}
    genre = load_dat(os.path.join(WORK, "db", "libretro_genre.dat")); years = load_dat(os.path.join(WORK, "db", "libretro_releaseyear.dat"))
    art = json.load(open(os.path.join(REPORTS, "art", "index.json"), encoding="utf-8")) if os.path.exists(os.path.join(REPORTS, "art", "index.json")) else {}
    mesen_ctrl = {}
    try:
        for line in open(os.path.join(WORK, "db", "MesenNesDB.txt"), encoding="utf-8", errors="replace"):
            if line.startswith("#") or not line.strip(): continue
            p = line.rstrip("\n").split(",")
            if len(p) > 13 and p[13].strip().isdigit(): mesen_ctrl[p[0].upper()] = int(p[13])
    except OSError: pass
    overrides = load_input_overrides()
    # ---------------- payloads (exact identity) ----------------
    payloads = {}; invalid = []
    for p in pays:
        i = ident.get(p["sha256"], {}); a = arch.get(p["relpath"], {})
        fmt = p["format"]; valid = fmt in ("headerless", "iNES", "NES2.0") and a.get("status") == "ok"
        rec = payloads.setdefault(p["sha256"], {"sha256": p["sha256"], "sha1": p["sha1"], "crc32": p["crc32"], "size": p["size"], "format": fmt, "valid": valid,
                                                "content_sha256": p["sha256"] if fmt == "headerless" else None, "archives": [], "header": None, "converted_sha256": i.get("converted_sha256") or None})
        rec["archives"].append({"archive": os.path.basename(p["relpath"]), "member": p["entry"], "zip_crc": p["zip_crc"], "archive_dir": os.path.dirname(p["relpath"]).replace("\\", "/")})
        if i.get("header_hex"):
            rec["header"] = {"hex": i["header_hex"], "source": i.get("header_source"), "status": i.get("header_status"), "validation": i.get("header_validation"), "format": "NES 2.0" if i.get("nes2") else "iNES",
                             "mapper": i.get("mapper"), "submapper": i.get("submapper"), "prg": i.get("prg"), "chr": i.get("chr"), "mirroring": "four-screen" if i.get("four_screen") else ("vertical" if i.get("vertical") else "horizontal"),
                             "battery": bool(i.get("battery")), "board": i.get("cart_board") or i.get("mesen_board") or ""}
        if not valid: invalid.append({"sha256": p["sha256"][:16], "archive": os.path.basename(p["relpath"]), "member": p["entry"], "reason": f"format {fmt}" if a.get("status") == "ok" else f"archive {a.get('status')}: {a.get('error')}"})
    # ---------------- versions (release identity) ----------------
    versions = {}
    for p in pays:
        sha = p["sha256"]; i = ident.get(sha, {}); pl = payloads[sha]
        if not pl["valid"]: continue
        name = i.get("nointro_name") or os.path.basename(p["relpath"])[:-4]
        flags = [x for x in (i.get("flags") or "").split("|") if x]; regions = [x for x in (i.get("regions") or "").split(",") if x]
        kind = version_kind(flags, name); lic = licensed_class(flags, i.get("cart_class") or "")
        conf = "verified" if i.get("nointro_match") else ("cartridge-db" if (i.get("cart_match") or i.get("mesen_match")) else "filename")
        row = {"title": i.get("title") or name, "cart_system": i.get("cart_system") or "", "mesen_system": i.get("mesen_system") or "", "mapper": i.get("mapper"), "four_screen": i.get("four_screen"), "licensed": lic, "mesen_mapper": i.get("mesen_mapper"), "cart_board": i.get("cart_board") or ""}
        cp = compat(row) if i.get("header_hex") else {"recommended_emulator": "unknown", "core": "", "launch_command": "", "compat_confidence": "none", "compat_notes": "header/mapper not established", "compat_warning": 0, "compat_evidence": "documentation and community reports"}
        inp = classify_input({"display_title": i.get("title") or name, "players": "", "nointro_name": name}, mesen_ctrl.get((p["crc32"] or "").upper()), overrides)
        c = curated.get(sha); b = by_builtin_sha.get(sha)
        gi = genre.get(name, {}); yi = years.get(name, {})
        v = {"id": sha[:16], "sha256": sha, "name": name, "title": i.get("title") or name, "regions": regions, "rev": i.get("rev") or "", "flags": flags, "kind": kind, "licensed": lic,
             "identity": conf, "nointro_match": bool(i.get("nointro_match")), "cart_match": bool(i.get("cart_match")), "cart_game": i.get("cart_game") or "", "publisher": i.get("cart_publisher") or "", "date": i.get("cart_date") or "", "cart_catalog": i.get("cart_catalog") or "",
             "year": (i.get("cart_date") or "")[:4] or (yi.get("releaseyear", "") if yi else ""), "genre": ", ".join(sorted({GENRE_MAP.get(x.strip(), x.strip()) for x in gi.get("genre", "").split("/") if x.strip()})) if gi.get("genre") else "",
             "size": p["size"], "emulator": cp["recommended_emulator"], "core": cp["core"], "compat_confidence": cp["compat_confidence"], "compat_notes": cp["compat_notes"], "launch_command": cp["launch_command"],
             "input_level": inp.get("input_level"), "input_accessory": inp.get("input_special_accessory"), "input_support": inp.get("input_nes_classic_support"), "input_requirement": inp.get("input_accessory_requirement"), "input_modes": inp.get("input_modes"),
             "curated": bool(c), "builtin_code": (c or {}).get("builtin_code") or (b["code"] if b else ""), "builtin_status": (c or {}).get("builtin_status") or ("BUILT-IN: EXACT MATCH" if b else ""),
             "art": (c or {}).get("art") or (f"art/{sha[:16]}.jpg" if art.get(sha[:16], {}).get("source") in ("libretro-thumbnails", "oem-backup") else ""),
             "header_ok": bool(i.get("header_hex")), "notes": i.get("notes") or ""}
        versions[v["id"]] = v
    # ---------------- titles (logical games) — the curated catalog's rule, applied to everything ----------------
    groups = defaultdict(list)
    for v in versions.values():
        i = ident.get(v["sha256"], {}); flags = v["flags"]
        groups[(norm_title(v["title"]), publisher_key(i, flags), v["licensed"])].append(v)
    for k in list(groups):
        if k[1] == "" and k in groups:
            others = [o for o in groups if o != k and o[0] == k[0] and o[2] == k[2] and o[1] != ""]
            if len(others) == 1: groups[others[0]].extend(groups.pop(k))
    titles = []
    for key, vs in groups.items():
        gid = f"{key[0]}|{key[1]}|{key[2]}"; tid = "t_" + hashlib.sha1(gid.encode("utf-8")).hexdigest()[:12]
        cur = [v for v in vs if v["curated"]]
        vs.sort(key=lambda x: (1 if x["curated"] else 0, 1 if x["nointro_match"] else 0, 1 if x["kind"] == "retail" else 0, 1 if ("USA" in x["regions"] or "World" in x["regions"]) else 0, rev_key(x["rev"]), -len(x["flags"])), reverse=True)
        rec = cur[0] if cur else vs[0]
        disp = curated[rec["sha256"]]["display_title"] if cur else rec["title"]
        names = sorted({v["title"] for v in vs}); regions = sorted({r for v in vs for r in v["regions"]})
        conf = "verified" if any(v["identity"] == "verified" for v in vs) else ("cartridge-db" if any(v["identity"] == "cartridge-db" for v in vs) else "filename")
        t = {"id": tid, "group": gid, "title": disp, "names": names, "publisher": key[1], "licensed": key[2], "curated": bool(cur), "versions": [v["id"] for v in vs], "recommended": rec["id"],
             "identity": conf, "regions": regions, "kinds": sorted({v["kind"] for v in vs}), "builtin_code": next((v["builtin_code"] for v in vs if v["builtin_code"]), ""),
             "year": next((v["year"] for v in vs if v["year"]), ""), "genre": next((v["genre"] for v in vs if v["genre"]), ""), "art": next((v["art"] for v in vs if v["art"]), "")}
        for v in vs: v["title_id"] = tid; v["recommended"] = v["id"] == rec["id"]
        titles.append(t)
    titles.sort(key=lambda t: t["title"].lower())
    # ---------------- quality metrics ----------------
    by_content = defaultdict(list)
    for pl in payloads.values():
        if pl["content_sha256"]: by_content[pl["content_sha256"]].append(pl["sha256"])
    aliases = {k: v for k, v in by_content.items() if len(v) > 1}
    multi_archive = [pl for pl in payloads.values() if len(pl["archives"]) > 1]
    q = {"generated": time.strftime("%Y-%m-%dT%H:%M:%S"), "archives": len(arch), "archives_ok": sum(1 for a in arch.values() if a["status"] == "ok"), "payload_records": len(pays), "valid_payloads": sum(1 for pl in payloads.values() if pl["valid"]),
         "unique_payloads_by_file_hash": len([pl for pl in payloads.values() if pl["valid"]]), "unique_contents": len(by_content), "byte_identical_under_different_names": len(multi_archive), "content_identical_across_file_hashes": len(aliases),
         "invalid_or_unsupported": len(invalid), "titles": len(titles), "curated_titles": sum(1 for t in titles if t["curated"]), "extended_titles": sum(1 for t in titles if not t["curated"]),
         "titles_with_multiple_versions": sum(1 for t in titles if len(t["versions"]) > 1), "versions": len(versions), "versions_in_curated_titles": sum(len(t["versions"]) for t in titles if t["curated"]),
         "identity": dict(Counter(v["identity"] for v in versions.values())), "kinds": dict(Counter(v["kind"] for v in versions.values())), "emulator": dict(Counter(v["emulator"] for v in versions.values())),
         "versions_without_header_evidence": sum(1 for v in versions.values() if not v["header_ok"]), "regions_top": Counter(r for v in versions.values() for r in v["regions"]).most_common(8),
         "curated_rows_mapped": sum(1 for sha in curated if sha[:16] in versions and versions[sha[:16]]["curated"]), "curated_rows": len(curated)}
    # ---------------- write ----------------
    lib = {"generated": q["generated"], "source": f"{SOURCE} (No-Intro headerless set) via inventory.sqlite", "counts": {k: q[k] for k in ("archives", "valid_payloads", "unique_contents", "titles", "curated_titles", "extended_titles", "versions")},
           "titles": titles, "versions": sorted(versions.values(), key=lambda v: v["name"].lower()),
           "payloads": {sha: {k: v for k, v in pl.items() if k != "valid"} for sha, pl in payloads.items() if pl["valid"]}}
    json.dump(lib, open(os.path.join(REPORTS, "library.json"), "w", encoding="utf-8"), separators=(",", ":"), ensure_ascii=False)
    json.dump({"quality": q, "invalid": invalid, "aliases": aliases}, open(os.path.join(REPORTS, "inventory-quality.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    md = ["# Complete ROM inventory — quality report", "", f"Generated {q['generated']} by `tools/rom-pipeline/index_all.py` from `{DB_PATH}` (read-only; no archive was extracted again, no ROM modified). Source: the owner's No-Intro headerless set under `{SOURCE}`.", "",
          "| Measure | Count |", "|---|---|", f"| ZIP archives scanned | {q['archives']} ({q['archives_ok']} readable) |", f"| Payload records | {q['payload_records']} |", f"| Valid NES ROM payloads | {q['valid_payloads']} |",
          f"| Unique byte-identical payloads (file SHA-256) | {q['unique_payloads_by_file_hash']} |", f"| Unique contents (headerless PRG+CHR SHA-256) | {q['unique_contents']} |", f"| Byte-identical payloads found under more than one archive/name | {q['byte_identical_under_different_names']} |",
          f"| Logical titles | {q['titles']} |", f"| Curated titles (the default Game Library) | {q['curated_titles']} |", f"| Extended titles (Extended Library only) | {q['extended_titles']} |", f"| Titles with more than one version | {q['titles_with_multiple_versions']} |",
          f"| Versions (release identities) | {q['versions']} (of which {q['versions_in_curated_titles']} belong to curated titles) |", f"| Exact known-release matches (No-Intro verified) | {q['identity'].get('verified', 0)} |", f"| Cartridge-database identity only | {q['identity'].get('cartridge-db', 0)} |",
          f"| Uncertain identity (filename only) | {q['identity'].get('filename', 0)} |", f"| Versions without header evidence (cannot be prepared) | {q['versions_without_header_evidence']} |", f"| Unsupported or invalid payloads | {q['invalid_or_unsupported']} |", "",
          "Version kinds: " + ", ".join(f"{k} {n}" for k, n in sorted(q["kinds"].items(), key=lambda x: -x[1])) + ".", "", "Emulator routing (rule-based, documentation/community evidence only): " + ", ".join(f"{k} {n}" for k, n in sorted(q["emulator"].items(), key=lambda x: -x[1])) + ".", "",
          f"Curated catalog rows mapped onto titles 1:1: {q['curated_rows_mapped']} of {q['curated_rows']}.", "",
          "## Invalid or unsupported payloads", ""] + [f"- `{x['archive']}` / `{x['member']}` — {x['reason']}" for x in invalid] + ["", "## Notes", "",
          "- Exact identity is the SHA-256 of the source bytes. Content identity (headerless PRG+CHR) is recorded separately; a content match is probable equivalence, never proof that two files are byte-identical.",
          "- Titles are grouped by normalised title + publisher (when known) + licensed class — the curated catalog's rule — so curated titles map 1:1 and nothing merges on text alone. Regional retitles remain separate titles unless their No-Intro base title is the same.",
          "- 'filename' identity means the No-Intro file name is the only evidence; such versions are shown with that confidence and are never promoted to verified releases."]
    open(os.path.join(REPORTS, "inventory-quality.md"), "w", encoding="utf-8").write("\n".join(md) + "\n")
    log(f"index_all: {q['titles']} titles ({q['curated_titles']} curated), {q['versions']} versions, {q['valid_payloads']} payloads, {len(invalid)} invalid; {time.time()-t0:.1f}s", LOGFILE)
    print(json.dumps({k: q[k] for k in ("archives", "valid_payloads", "unique_contents", "titles", "curated_titles", "extended_titles", "titles_with_multiple_versions", "versions", "invalid_or_unsupported", "curated_rows_mapped")}))
    print("library.json bytes:", os.path.getsize(os.path.join(REPORTS, "library.json")))

if __name__ == "__main__":
    main()
