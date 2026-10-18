"""Schema-4 collection manifest checks that need no game files and no private workspace: normalisation, validation, fingerprints,
migration from the older selection schemas, and rejection of malformed manifests. Uses a synthetic catalog/factory set. Exit 0 = PASS."""
from __future__ import annotations
import os, sys, json, copy
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from manifest_v4 import SCHEMA4, normalize, validate_v4, migrate_to_v4, fingerprint, selection_from_row, visible_codes

def synthetic():
    cat = {}
    for i, (name, code, status) in enumerate([("Alpha Quest (USA)", "CLV-P-TEST1", "BUILT-IN: EXACT MATCH"), ("Beta Blaster (USA)", "", "NOT BUILT-IN"), ("Gamma Racer (USA)", "CLV-P-TEST3", "BUILT-IN: DIFFERENT REVISION"), ("Delta Puzzle (USA)", "", "NOT BUILT-IN")]):
        gid = f"{i:016x}"; cat[gid] = {"game_id": gid, "display_title": name.split(" (")[0], "nointro_name": name, "builtin_code": code, "builtin_status": status, "payload_sha256": gid * 4, "recommended_emulator": "kachikachi", "core": "", "launch_command": "", "regions": "USA", "rev": "", "kind": "retail", "identity": "verified", "publisher": "Test Co", "year": "1990", "licensed": "licensed"}
    factory = {"CLV-P-TEST1": {"factory_title": "Alpha Quest", "code": "CLV-P-TEST1", "nointro_gamedata_sha256": "00000000000000000" * 4}, "CLV-P-TEST3": {"factory_title": "Gamma Racer", "code": "CLV-P-TEST3", "nointro_gamedata_sha256": "other"}, "CLV-P-TEST9": {"factory_title": "Omega Only", "code": "CLV-P-TEST9", "nointro_gamedata_sha256": "x"}}
    factory["CLV-P-TEST1"]["nointro_gamedata_sha256"] = ("%016x" % 0) * 4   # exact match with the catalog row's payload hash
    fref = {"file": "builtin.json", "sha256": "f" * 64, "codes": sorted(factory), "count": len(factory)}
    return cat, factory, fref

def main():
    checks = []; ok = lambda k, v, n="": checks.append(f"{k}={'true' if v else 'false'}{(' ('+n+')') if n and not v else ''}")
    cat, factory, fref = synthetic(); res = lambda v: cat.get(v)
    rows = list(cat.values())
    sels = [selection_from_row(rows[0], None, "catalog", factory), selection_from_row(rows[1], None, "catalog", factory), selection_from_row(rows[2], "factory", "catalog", factory)]
    man = {"schema": SCHEMA4, "name": "Pure test", "collection_mode": "exclusive", "factory_game_policy": "hide_unselected", "factory_inventory": {"sha256": fref["sha256"], "count": fref["count"], "codes": fref["codes"]}, "selections": sels, "hidden_factory_games": [{"code": "CLV-P-TEST9", "title": "Omega Only"}]}
    man = normalize(man)
    errs = validate_v4(man, fref, factory, res); ok("valid_manifest_passes", not errs, str(errs)[:200])
    ok("fingerprint_stable", fingerprint(man) == fingerprint(copy.deepcopy(man)) and len(fingerprint(man)) >= 16)
    m2 = copy.deepcopy(man); m2["selections"] = list(reversed(m2["selections"])); ok("fingerprint_order_independent_or_normalised", fingerprint(normalize(m2)) == fingerprint(man))
    vc = visible_codes(man); ok("visible_codes_from_selections", "CLV-P-TEST1" in vc and "CLV-P-TEST3" in vc and "CLV-P-TEST9" not in vc, str(sorted(vc)))
    bad = copy.deepcopy(man); bad["selections"][0]["version_id"] = "ffffffffffffffff"; ok("unknown_version_rejected", any("not in the catalog" in e.lower() or "unknown" in e.lower() for e in validate_v4(bad, fref, factory, res)), str(validate_v4(bad, fref, factory, res))[:200])
    bad = copy.deepcopy(man); bad["schema"] = "nes-classic-collection/99"; ok("wrong_schema_rejected", bool(validate_v4(bad, fref, factory, res)))
    bad = copy.deepcopy(man); bad["selections"].append(copy.deepcopy(bad["selections"][0])); ok("duplicate_selection_rejected", bool(validate_v4(bad, fref, factory, res)))
    bad = copy.deepcopy(man); bad["hidden_factory_games"] = [{"code": "CLV-P-NOPE", "title": "x"}]; ok("unknown_factory_code_rejected", bool(validate_v4(bad, fref, factory, res)))
    bad = copy.deepcopy(man); bad["hidden_factory_games"] = [{"code": "CLV-P-TEST1", "title": "Alpha Quest"}]; ok("hidden_and_selected_factory_conflict_rejected", bool(validate_v4(bad, fref, factory, res)))
    for junk in ({}, {"schema": SCHEMA4}, {"schema": SCHEMA4, "selections": "no"}, {"schema": SCHEMA4, "selections": [{"version_id": 5}]}, {"schema": SCHEMA4, "selections": [None]}):
        try: e = validate_v4(normalize(copy.deepcopy(junk)), fref, factory, res); fine = bool(e)
        except Exception: fine = True   # raising is acceptable for junk; silently accepting is not
        ok(f"junk_rejected_{json.dumps(junk)[:30]}", fine)
    m1 = {"schema": "nes-classic-selection/1", "games": [{"game_id": rows[1]["game_id"]}, {"game_id": rows[0]["game_id"]}]}
    m4 = migrate_to_v4(m1, cat, fref, factory, res, name="Legacy"); e = validate_v4(m4, fref, factory, res)
    ok("legacy_v1_migrates_to_valid_v4", m4["schema"] == SCHEMA4 and not e and m4["count"] == 2 and m4.get("migrated_from") == "nes-classic-selection/1", str(e)[:200])
    ok("migration_hides_unselected_factory_games", {h["code"] for h in m4["hidden_factory_games"]} == {"CLV-P-TEST3", "CLV-P-TEST9"}, str(m4["hidden_factory_games"]))
    s = json.dumps(man); back = normalize(json.loads(s)); ok("json_round_trip", fingerprint(back) == fingerprint(man))
    badc = [c for c in checks if "=false" in c]; print("\n".join(checks)); print(f"RESULT: {'PASS' if not badc else 'FAIL '+str(badc)} ({len(checks)} checks)"); sys.exit(1 if badc else 0)

if __name__ == "__main__":
    main()
