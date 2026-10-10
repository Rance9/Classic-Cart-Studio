"""Stage E — validate an OWNER-APPROVED selection manifest and prepare the additional ROMs (run only after explicit approval).

  python import_selected.py --manifest <selection.json> [--dest <folder>] [--dry-run] [--approve-alternates]
                            [--migrate-to-v3 <out.json>] [--confirm-empty]

Manifest schemas: nes-classic-selection/1 (legacy list), /2 (collection with install flags), /3 (EXCLUSIVE collection:
the selected games define the complete intended TV library; unselected factory games are to be HIDDEN, never deleted).
Older manifests keep their original (non-exclusive) semantics; `--migrate-to-v3` writes an explicit v3 copy.

Two separate concerns are reported and never conflated:
  ROM PREPARATION  — building headered .nes files for selected games that need installation (this script does it).
  COLLECTION DEPLOYMENT — configuring the console so the normal launcher shows exactly the selected collection
                     (NOT done here; status is reported as PENDING / NOT AUTHORIZED; Gate G1 applies).

Built-in status is re-derived from reports/builtin.json via the catalog record; browser flags are never trusted:
  EXACT MATCH -> SKIP-BUILTIN; DIFFERENT REVISION -> SKIP-FACTORY-DEFAULT unless decision 'alternate' + --approve-alternates;
  IDENTITY ONLY -> SKIP-REVIEW unless decision 'alternate' + --approve-alternates; NOT BUILT-IN -> prepared.
Fails closed on: unknown schema, missing/unknown game ids, duplicate ids, factory inventory mismatch (v3), unknown
factory codes (v3), policy values outside the documented set, an exclusive collection with zero games (without
--confirm-empty), and any payload/header/hash check failure.
"""
from __future__ import annotations
import os, sys, json, hashlib, argparse, time, re, sqlite3
sys.path.insert(0, os.path.dirname(__file__))
from common import *

SCHEMAS = ("nes-classic-selection/1", "nes-classic-selection/2", "nes-classic-selection/3")
V3_POLICIES = {"collection_mode": {"exclusive"}, "factory_game_policy": {"hide_unselected"}}
DEPLOY_CAPABILITIES = ["hide_unselected_factory_games", "install_additional_games", "preserve_factory_roms", "reversible"]

def safe_name(nointro: str) -> str:
    return re.sub(r'[<>:"/\\|?*]', "_", nointro).strip() + ".nes"

def builtin_class(c: dict, factory: dict):
    code = c.get("builtin_code") or ""; b = factory.get(code); st = c.get("builtin_status") or "NOT BUILT-IN"
    if st == "BUILT-IN: EXACT MATCH":
        return ("exact" if b and b.get("nointro_gamedata_sha256") == c.get("payload_sha256") else "exact-unverified"), b
    if st == "BUILT-IN: DIFFERENT REVISION": return ("revision" if b else "revision-unverified"), b
    if st == "BUILT-IN: IDENTITY ONLY": return ("identity" if b else "identity-unverified"), b
    return "new", None

def factory_reference(builtin_path: str):
    fb = open(builtin_path, "rb").read(); fj = json.loads(fb.decode("utf-8"))
    return {"file": "reports/builtin.json", "sha256": hashlib.sha256(fb).hexdigest(), "count": len(fj["games"]), "codes": sorted(x["code"] for x in fj["games"])}, {b["code"]: b for b in fj["games"]}

def migrate_to_v3(man: dict, cat: dict, factory_ref: dict, factory: dict) -> dict:
    """Explicit upgrade of a v1/v2 manifest to the exclusive v3 form (selections, decisions and identities preserved)."""
    games = []
    for g in man.get("games", []):
        c = cat.get(g.get("game_id"))
        if not c: continue
        cls, b = builtin_class(c, factory)
        dec = g.get("revision_decision") if cls in ("revision", "identity", "revision-unverified", "identity-unverified") else None
        games.append({"game_id": c["game_id"], "display_title": c["display_title"], "nointro_name": c["nointro_name"], "payload_sha256": c["payload_sha256"], "recommended_emulator": c.get("recommended_emulator"),
                      "builtin_status": c.get("builtin_status"), "builtin_code": c.get("builtin_code") or None, "builtin_factory_version": c.get("builtin_factory_version") or None,
                      "comparison_status": {"exact": "exact-match", "revision": "different-revision", "identity": "identity-only"}.get(cls.split("-")[0], "not-built-in"),
                      "install_required": cls == "new" or (dec == "alternate"), "revision_decision": dec or ("factory" if cls != "new" and not cls.startswith("exact") else None),
                      "input_requirements": {k[6:]: c.get(k) for k in c if k.startswith("input_")}})
    sel_codes = {g["builtin_code"] for g in games if g.get("builtin_code") and (g["comparison_status"] == "exact-match" or (g["comparison_status"] in ("different-revision", "identity-only") and g.get("revision_decision") != "alternate"))}
    hidden = [{"code": code, "title": factory[code]["factory_title"]} for code in factory_ref["codes"] if code not in sel_codes]
    return {"schema": "nes-classic-selection/3", "migrated_from": man.get("schema"), "exported": time.strftime("%Y-%m-%dT%H:%M:%S"), "collection_mode": "exclusive", "factory_game_policy": "hide_unselected",
            "factory_inventory": factory_ref, "count": len(games), "games": games, "hidden_factory_games": hidden,
            "deployment": {"required_capabilities": DEPLOY_CAPABILITIES, "console_enforcement": "pending", "note": "Unselected factory games are intended to be hidden during deployment. Console-side enforcement has not yet been configured."}}

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--manifest", required=True); ap.add_argument("--dest", default=INCOMING); ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--approve-alternates", action="store_true"); ap.add_argument("--confirm-empty", action="store_true", help="accept an exclusive manifest with zero games (would leave an empty TV library)")
    ap.add_argument("--migrate-to-v3", metavar="OUT", help="write an explicit schema-3 copy of a v1/v2 manifest and exit (no preparation)")
    ap.add_argument("--migrate-to-v4", metavar="OUT", help="write an explicit schema-4 (Studio collection) copy of a v1/v2/v3 manifest and exit (no preparation)")
    ap.add_argument("--catalog", default=os.path.join(REPORTS, "catalog.json")); ap.add_argument("--builtin", default=os.path.join(REPORTS, "builtin.json"))
    ap.add_argument("--db", default=DB_PATH); ap.add_argument("--no-report-copy", action="store_true")
    a = ap.parse_args()
    catj = json.load(open(a.catalog, encoding="utf-8")); cat = {g["game_id"]: g for g in catj["games"]}
    factory_ref, factory = factory_reference(a.builtin) if os.path.exists(a.builtin) else (None, {})
    man = json.load(open(a.manifest, encoding="utf-8"))
    from manifest_v4 import ALL_SCHEMAS, SCHEMA4, normalize, migrate_to_v4, validate_v4
    from library_lookup import resolve
    if man.get("schema") not in ALL_SCHEMAS: raise SystemExit(f"unknown manifest schema {man.get('schema')!r}; accepted: {ALL_SCHEMAS}")
    schema = man["schema"]; legacy = schema == SCHEMAS[0]; v3 = schema in (SCHEMAS[2], SCHEMA4)   # v4 keeps the exclusive semantics of v3
    if a.migrate_to_v3:
        if v3: raise SystemExit("manifest is already schema 3 or newer")
        out = migrate_to_v3(man, cat, factory_ref, factory); json.dump(out, open(a.migrate_to_v3, "w", encoding="utf-8"), indent=1)
        print(f"migrated {schema} -> nes-classic-selection/3: {out['count']} games, {len(out['hidden_factory_games'])} factory games to hide -> {a.migrate_to_v3}"); return
    if a.migrate_to_v4:
        if schema == SCHEMA4: raise SystemExit("manifest is already schema 4")
        out = migrate_to_v4(man, cat, factory_ref, factory, resolve); json.dump(out, open(a.migrate_to_v4, "w", encoding="utf-8"), indent=1)
        print(f"migrated {schema} -> {SCHEMA4}: {out['count']} selections, {len(out['hidden_factory_games'])} factory games to hide -> {a.migrate_to_v4}"); return
    v4_problems = validate_v4(man, factory_ref, factory, lambda vid: cat.get(vid) or resolve(vid)) if (schema == SCHEMA4 and factory_ref) else []
    man = normalize(man)
    problems = list(v4_problems)   # fail-closed validation list
    ids = [g.get("game_id") for g in man.get("games", [])]
    dup = sorted({i for i in ids if ids.count(i) > 1})
    if dup: problems.append(f"duplicate game ids: {dup}")
    if v3:
        for k, allowed in V3_POLICIES.items():
            if man.get(k) not in allowed: problems.append(f"{k}={man.get(k)!r} not in {sorted(allowed)}")
        fi = man.get("factory_inventory") or {}
        if not factory_ref: problems.append("reports/builtin.json missing; cannot validate factory inventory")
        else:
            if fi.get("sha256") != factory_ref["sha256"]: problems.append("factory_inventory.sha256 does not match reports/builtin.json (stale or edited manifest)")
            if fi.get("count") != factory_ref["count"] or sorted(fi.get("codes", [])) != factory_ref["codes"]: problems.append("factory_inventory codes/count differ from the verified inventory")
        hidden_codes = [h.get("code") for h in man.get("hidden_factory_games", [])]
        unknown_codes = [c for c in hidden_codes if c not in factory]
        if unknown_codes: problems.append(f"hidden_factory_games contains unknown codes: {unknown_codes}")
        if not man.get("games") and not a.confirm_empty: problems.append("exclusive collection with ZERO games: every factory game would be hidden and the TV library would be empty; re-run with --confirm-empty only if that is intended")
    con = sqlite3.connect(a.db)
    stored = {r[0]: r[1] for r in con.execute("SELECT sha256, stored FROM payloads")}
    ident = {r[0]: r[1] for r in con.execute("SELECT sha256, header_hex FROM ident")}
    results = []; counts = {"collection": 0, "already_installed": 0, "factory_default": 0, "review": 0, "pending_approval": 0, "prepared": 0, "present": 0, "failed": 0, "input_warnings": 0}
    visible_codes = set()
    for g in man.get("games", []):
        gid = g.get("game_id"); r = {"game_id": gid, "status": "", "detail": ""}
        c = cat.get(gid) or resolve(gid or "")
        if not c: r.update(status="FAIL", detail="not in the catalog, the Extended Library or the personal library"); results.append(r); counts["failed"] += 1; continue
        counts["collection"] += 1; r["source"] = c.get("source", "catalog")
        r["title"] = c["display_title"]; r["nointro_name"] = c["nointro_name"]; r["builtin_status"] = c.get("builtin_status"); r["builtin_code"] = c.get("builtin_code") or ""
        cls, b = builtin_class(c, factory); r["builtin_class"] = cls
        # controller metadata is re-derived from the catalog (trusted), browser claims ignored
        inp = {k[6:]: c.get(k) for k in c if k.startswith("input_")}; r["input_requirements"] = inp
        if inp.get("nes_classic_support") in ("unverified", "unsupported", "unknown") and inp.get("standard_controller_playable") != "true":
            counts["input_warnings"] += 1; r["input_warning"] = f"{inp.get('special_accessory') or 'input requirement'} — NES Classic support {inp.get('nes_classic_support')}"
            if g.get("input_requirements", {}).get("nes_classic_support") not in (None, inp.get("nes_classic_support")): r["input_warning"] += " (manifest claim ignored)"
        decision = (g.get("revision_decision") or "").lower() if not legacy else ""
        if cls.startswith("exact") or ((cls.startswith("revision") or cls.startswith("identity")) and decision != "alternate"): visible_codes.add(r["builtin_code"])   # same rule as the catalog page and the hakchi bridge
        if cls.startswith("exact"):
            r.update(status="SKIP-BUILTIN", detail=f"factory game {r['builtin_code']} already contains this ROM" + ("" if cls == "exact" else " (factory inventory could not confirm; nothing written)") + ("; manifest install flag ignored" if g.get("install_required") else ""))
            counts["already_installed"] += 1; results.append(r); continue
        if cls.startswith("revision") or cls.startswith("identity"):
            kind = "different revision" if cls.startswith("revision") else "identity-only match (different release/publisher)"
            fv = c.get("builtin_factory_version") or (b or {}).get("nointro_name") or "factory version"
            if decision == "alternate" and a.approve_alternates and not cls.endswith("unverified"): r["alternate_of_factory"] = fv
            elif decision == "alternate":
                r.update(status="PENDING-APPROVAL", detail=f"{kind}: manifest asks for the catalog version instead of factory '{fv}'; re-run with --approve-alternates after review"); counts["pending_approval"] += 1; results.append(r); continue
            else:
                r.update(status="SKIP-FACTORY-DEFAULT" if cls.startswith("revision") else "SKIP-REVIEW", detail=f"{kind}: keeping factory '{fv}' (no duplicate install)")
                counts["factory_default" if cls.startswith("revision") else "review"] += 1; results.append(r); continue
        if problems: r.update(status="BLOCKED", detail="manifest failed validation; nothing prepared"); results.append(r); continue
        sha = c["payload_sha256"]; path = c.get("stored") or stored.get(sha)
        if c.get("file_missing") or not path or not os.path.exists(path): r.update(status="FAIL", detail="game file missing — locate the file in the Studio" if c.get("source") == "personal" else "payload not in work store (re-run inventory.py)"); results.append(r); counts["failed"] += 1; continue
        raw = open(path, "rb").read()
        if c.get("headered_file"):   # personal headered .nes: used as-is, its own header kept; content identity = bytes after the header
            data = raw[16:]; hdr_hex = raw[:16].hex(); out = raw
        else:
            data = raw; hdr_hex = c.get("header_hex") or ident.get(sha) or ""
            if not hdr_hex: r.update(status="FAIL", detail="no header evidence for this payload"); results.append(r); counts["failed"] += 1; continue
            out = bytes.fromhex(hdr_hex) + data
        if hashlib.sha256(data).hexdigest() != sha: r.update(status="FAIL", detail="stored payload hash mismatch"); results.append(r); counts["failed"] += 1; continue
        if out[16:] != data: r.update(status="FAIL", detail="payload preservation check failed"); results.append(r); counts["failed"] += 1; continue
        csha = hashlib.sha256(out).hexdigest()
        if c.get("converted_sha256") and csha != c["converted_sha256"]: r.update(status="FAIL", detail="converted SHA-256 differs from catalog"); results.append(r); counts["failed"] += 1; continue
        os.makedirs(a.dest, exist_ok=True)
        name = safe_name(c["nointro_name"]); dest = os.path.join(a.dest, name); k = 2
        while os.path.exists(dest):
            if hashlib.sha256(open(dest, "rb").read()).hexdigest() == csha: break
            dest = os.path.join(a.dest, f"{name[:-4]} [{k}].nes"); k += 1
        r.update(payload_sha256=sha, converted_sha256=csha, header_hex=hdr_hex, size=len(out), dest=dest, recommended_emulator=c.get("recommended_emulator"), launch_command=c.get("launch_command"))
        if os.path.exists(dest): r.update(status="SKIP", detail="identical file already present"); counts["present"] += 1
        elif a.dry_run: r.update(status="DRY-RUN", detail="would write" + (" (approved alternate of factory version)" if r.get("alternate_of_factory") else "")); counts["prepared"] += 1
        else:
            tmp = dest + ".part"
            with open(tmp, "wb") as f: f.write(out)
            if hashlib.sha256(open(tmp, "rb").read()).hexdigest() != csha: os.remove(tmp); r.update(status="FAIL", detail="write verification failed"); results.append(r); counts["failed"] += 1; continue
            os.replace(tmp, dest); r.update(status="OK", detail="written and verified" + (" (approved alternate of factory version)" if r.get("alternate_of_factory") else "")); counts["prepared"] += 1
        results.append(r)
    # ---- deployment status (never enforced here) ----
    if v3 and factory_ref:
        hidden_expected = [cd for cd in factory_ref["codes"] if cd not in visible_codes]
        declared = sorted(h.get("code") for h in man.get("hidden_factory_games", []))
        if declared != sorted(hidden_expected): problems.append(f"hidden_factory_games disagrees with the selection: manifest lists {len(declared)}, derived {len(hidden_expected)} (derived list is authoritative)")
    deployment = {"manifest_validated": not problems, "validation_problems": problems, "rom_preparation": "dry-run" if a.dry_run else ("done" if counts["prepared"] else "nothing to prepare"),
                  "factory_visibility_policy": ("hide_unselected" if v3 else "not applicable (non-exclusive manifest; factory games untouched)"),
                  "factory_games_visible": sorted(visible_codes) if v3 else None, "factory_games_to_hide": (sorted(cd for cd in factory_ref["codes"] if cd not in visible_codes) if (v3 and factory_ref) else None),
                  "exclusive_collection_enforcement": "pending — no console-side mechanism configured; see docs/exclusive-collection-research.md" if v3 else "not requested",
                  "console_deployment": "NOT AUTHORIZED (Gate G1 not approved)", "input_hardware_warnings": counts["input_warnings"]}
    stamp = time.strftime("%Y%m%d-%H%M%S")
    summary = {"manifest": a.manifest, "schema": schema, "dest": a.dest, "dry_run": a.dry_run, "approve_alternates": a.approve_alternates, "requested": len(man.get("games", [])), "collection_size": counts["collection"],
               "already_installed_skipped": counts["already_installed"], "factory_default_skipped": counts["factory_default"], "review_skipped": counts["review"], "pending_approval": counts["pending_approval"],
               "installation_count": counts["prepared"] + counts["present"], "prepared": counts["prepared"], "already_present": counts["present"], "failed": counts["failed"] + (len([r for r in results if r["status"] == "BLOCKED"])),
               "blocked": len([r for r in results if r["status"] == "BLOCKED"]), "deployment": deployment, "results": results, "at": stamp}
    os.makedirs(a.dest, exist_ok=True)
    for folder in ([a.dest] if a.no_report_copy else list({a.dest, REPORTS})):
        json.dump(summary, open(os.path.join(folder, f"import-manifest-{stamp}.json"), "w", encoding="utf-8"), indent=1)
    md = [f"# Import summary {stamp}", "", f"Destination: `{a.dest}`  Dry-run: {a.dry_run}  Approve alternates: {a.approve_alternates}  Schema: {schema}", "", "## Deployment status", ""]
    md += [f"- Manifest validated: {'yes' if not problems else 'NO — ' + '; '.join(problems)}", f"- ROM preparation: {deployment['rom_preparation']}", f"- Factory visibility policy: {deployment['factory_visibility_policy']}",
           f"- Factory games visible / to hide: {len(deployment['factory_games_visible'] or [])} / {len(deployment['factory_games_to_hide'] or [])}" if v3 else "- Factory games: untouched (non-exclusive manifest)",
           f"- Exclusive collection enforcement: {deployment['exclusive_collection_enforcement']}", f"- Console deployment: {deployment['console_deployment']}", f"- Input-hardware warnings: {counts['input_warnings']}", "", "## Counts", ""]
    md += [f"- collection size (selected games): {counts['collection']}", f"- already installed on the console (skipped): {counts['already_installed']}", f"- factory version kept (different revision, skipped): {counts['factory_default']}",
           f"- identity-only matches needing review (skipped): {counts['review']}", f"- alternates pending approval (skipped): {counts['pending_approval']}", f"- installation count (prepared + already present): {counts['prepared'] + counts['present']}", f"- failed/blocked: {summary['failed']}", "",
           "| Status | Title | Built-in | Input | File | Emulator | Detail |", "|---|---|---|---|---|---|---|"]
    md += [f"| {r['status']} | {r.get('title','')} | {(r.get('builtin_status') or '').replace('BUILT-IN: ','')} | {r.get('input_warning','')} | {os.path.basename(r.get('dest','') or '')} | {r.get('recommended_emulator','')} | {r['detail']} |" for r in results]
    open(os.path.join(a.dest if a.no_report_copy else REPORTS, f"import-manifest-{stamp}.md"), "w", encoding="utf-8").write("\n".join(md))
    print(f"schema={schema} validated={'yes' if not problems else 'NO'} collection={counts['collection']} already_installed={counts['already_installed']} factory_default={counts['factory_default']} review={counts['review']} pending_approval={counts['pending_approval']} install_count={counts['prepared'] + counts['present']} prepared={counts['prepared']} present={counts['present']} failed={summary['failed']} input_warnings={counts['input_warnings']} enforcement={'pending' if v3 else 'n/a'} dry_run={a.dry_run} dest={a.dest}")
    for p in problems: print("PROBLEM:", p)
    sys.exit(1 if (summary["failed"] or problems) else 0)

if __name__ == "__main__":
    main()
