"""Collection manifest schema 4 (`nes-classic-collection/4`) — what Classic Cart Studio saves and loads.

Compared with `nes-classic-selection/3` (which stays accepted everywhere with its documented semantics):
  * `selections` replace `games`: each carries the logical `title_id`, the exact `version_id` (= the payload-derived id that schema 3
    called `game_id`; both keys are present), the payload identity, the `source` (catalog | extended | personal) and the same
    factory fields (comparison_status, builtin_code, install_required, revision_decision) so every consumer keeps its rule;
  * `personal_library`: the user's own entries (added files or games found on this computer's console-management library) with
    user-authored metadata kept apart from detected metadata; `available_library`: entries imported but not selected;
  * `external_dependencies`: ROM files the collection needs that are not part of the Studio's inventory, with availability;
  * `baseline`: the console-management sets captured when the collection was loaded from this computer (conflict detection);
  * `name`, `status`, `provenance`. Exclusive-collection policy and the factory inventory fingerprint are unchanged.
`normalize()` gives every schema a `games` view so the importer, the bridge and the inspector work unchanged; v1/v2 keep their
non-exclusive meaning and are never silently reinterpreted (migration is explicit)."""
from __future__ import annotations
import os, sys, json, time, hashlib
sys.path.insert(0, os.path.dirname(__file__))
from import_selected import SCHEMAS, V3_POLICIES, migrate_to_v3, builtin_class, DEPLOY_CAPABILITIES
from library_lookup import title_id_for

SCHEMA4 = "nes-classic-collection/4"
ALL_SCHEMAS = SCHEMAS + (SCHEMA4,)
EXCLUSIVE_SCHEMAS = (SCHEMAS[2], SCHEMA4)
SOURCES = ("catalog", "extended", "personal")
STATUSES = ("exact-match", "different-revision", "identity-only", "not-built-in")
APP = "Classic Cart Studio"

def is_exclusive(man: dict) -> bool: return man.get("schema") in EXCLUSIVE_SCHEMAS

def normalize(man: dict) -> dict:
    """A copy with a `games` list for every schema (v4: derived from `selections`, each with `game_id` = `version_id`)."""
    if man.get("schema") != SCHEMA4: return man
    games = []
    for s in man.get("selections", []):
        g = dict(s); g["game_id"] = s.get("game_id") or s.get("version_id"); g.setdefault("display_title", s.get("title")); g.setdefault("nointro_name", s.get("version_label")); games.append(g)
    out = dict(man); out["games"] = games; out.setdefault("count", len(games)); return out

def selection_from_row(c: dict, decision: str | None, source: str, factory: dict, extra: dict | None = None) -> dict:
    """A v4 selection record from a catalog-shaped row (catalog / resolved extended / personal)."""
    cls, b = builtin_class(c, factory); kind = cls.split("-")[0]
    status = {"exact": "exact-match", "revision": "different-revision", "identity": "identity-only"}.get(kind, "not-built-in")
    dec = decision if kind in ("revision", "identity") else None
    if kind in ("revision", "identity") and dec not in ("factory", "alternate"): dec = "factory"
    install = kind == "new" or dec == "alternate"
    s = {"title_id": c.get("title_id") or title_id_for(c["game_id"]), "version_id": c["game_id"], "game_id": c["game_id"], "payload_sha256": c["payload_sha256"], "title": c["display_title"], "version_label": c.get("nointro_name") or "", "source": source,
         "comparison_status": status, "builtin_code": c.get("builtin_code") or None, "builtin_factory_version": c.get("builtin_factory_version") or None, "builtin_status": c.get("builtin_status") or "NOT BUILT-IN",
         "install_required": install, "revision_decision": dec, "recommended_emulator": c.get("recommended_emulator"), "emulator_preference": None, "display_title": c["display_title"], "nointro_name": c.get("nointro_name") or "",
         "input_requirements": {k[6:]: c.get(k) for k in c if k.startswith("input_")}}
    if extra: s.update(extra)
    return s

def visible_codes(man: dict) -> set:
    vis = set()
    for g in normalize(man).get("games", []):
        st = g.get("comparison_status"); dec = (g.get("revision_decision") or "").lower(); code = g.get("builtin_code")
        if code and (st == "exact-match" or (st in ("different-revision", "identity-only") and dec == "factory")): vis.add(code)
    return vis

def migrate_to_v4(man: dict, cat: dict, factory_ref: dict, factory: dict, resolve=None, name: str = "Imported collection") -> dict:
    """Explicit upgrade of any older manifest; selections, decisions, identities and emulator recommendations are preserved."""
    src = man
    if man.get("schema") == SCHEMAS[2] or man.get("schema") in (SCHEMAS[0], SCHEMAS[1]):
        v3 = man if man.get("schema") == SCHEMAS[2] else migrate_to_v3(man, cat, factory_ref, factory)
        sels = []
        for g in v3.get("games", []):
            c = cat.get(g.get("game_id")) or (resolve(g.get("game_id")) if resolve else None)
            if not c: continue
            sels.append(selection_from_row(c, g.get("revision_decision"), c.get("source", "catalog"), factory, {"consensus_rank": g.get("consensus_rank")}))
        out = {"schema": SCHEMA4, "app": APP, "name": name, "migrated_from": man.get("schema"), "created": time.strftime("%Y-%m-%dT%H:%M:%S"), "modified": time.strftime("%Y-%m-%dT%H:%M:%S"), "exported": time.strftime("%Y-%m-%dT%H:%M:%S"),
               "collection_mode": "exclusive", "factory_game_policy": "hide_unselected", "factory_inventory": dict(factory_ref), "count": len(sels), "selections": sels, "personal_library": [], "available_library": [], "external_dependencies": [],
               "baseline": None, "status": {"saved_in_browser": None, "saved_to_file": None, "prepared_on_pc": None, "installed_on_console": None, "verified_on_console": None},
               "deployment": {"required_capabilities": DEPLOY_CAPABILITIES, "console_enforcement": "pending", "note": "Unselected factory games are intended to be hidden during deployment. Console-side enforcement has not yet been configured."},
               "provenance": {"migrated_by": "manifest_v4.migrate_to_v4", "source_schema": man.get("schema")}}
        vis = visible_codes(out); out["hidden_factory_games"] = [{"code": cd, "title": factory[cd]["factory_title"]} for cd in factory_ref["codes"] if cd not in vis]
        return out
    raise ValueError(f"cannot migrate schema {man.get('schema')!r}")

def validate_v4(man: dict, factory_ref: dict, factory: dict, resolve) -> list[str]:
    """Fail-closed structural + identity validation of a v4 manifest (the planner adds payload/header checks)."""
    p = []
    if man.get("schema") != SCHEMA4: return [f"not a schema-4 manifest ({man.get('schema')!r})"]
    for k, allowed in V3_POLICIES.items():
        if man.get(k) not in allowed: p.append(f"{k}={man.get(k)!r} not in {sorted(allowed)}")
    fi = man.get("factory_inventory") or {}
    if fi.get("sha256") != factory_ref["sha256"]: p.append("factory_inventory.sha256 does not match reports/builtin.json (stale catalog page or edited manifest)")
    if fi.get("count") != factory_ref["count"] or sorted(fi.get("codes", [])) != factory_ref["codes"]: p.append("factory inventory codes/count differ from the verified inventory")
    sels = man.get("selections")
    if not isinstance(sels, list): p.append("selections missing"); return p
    personal = {e.get("id"): e for e in man.get("personal_library", []) if isinstance(e, dict)}
    seen_v, seen_t = set(), {}
    for s in sels:
        vid = s.get("version_id") or s.get("game_id"); lbl = s.get("title") or vid
        if not vid: p.append("selection without version_id"); continue
        if vid in seen_v: p.append(f"duplicate selection {vid}"); continue
        seen_v.add(vid)
        if s.get("source") not in SOURCES: p.append(f"{lbl}: source {s.get('source')!r} not in {SOURCES}")
        if s.get("comparison_status") not in STATUSES: p.append(f"{lbl}: comparison_status {s.get('comparison_status')!r} unknown")
        if s.get("comparison_status") in ("different-revision", "identity-only") and s.get("revision_decision") not in ("factory", "alternate"): p.append(f"{lbl}: revision decision unresolved")
        if s.get("source") == "personal" and vid not in personal: p.append(f"{lbl}: personal selection {vid} is not in personal_library")
        c = resolve(vid) if resolve else None
        if not c: p.append(f"{lbl}: {vid} is not in the catalog, the Extended Library or the personal library on this computer"); continue
        if s.get("payload_sha256") and s.get("payload_sha256") != c.get("payload_sha256"): p.append(f"{lbl}: payload hash differs from the known version (edited or stale manifest)")
        tid = s.get("title_id") or c.get("title_id") or title_id_for(vid)
        if tid:
            if tid in seen_t and not (s.get("multiple_versions") and seen_t[tid]): p.append(f"{lbl}: a second version of the same title is selected without the explicit multiple-versions choice")
            seen_t[tid] = bool(s.get("multiple_versions"))
    vis = visible_codes(man); hidden = [cd for cd in factory_ref["codes"] if cd not in vis]
    declared = sorted(h.get("code") for h in man.get("hidden_factory_games", []))
    if declared != hidden: p.append(f"hidden_factory_games ({len(declared)}) disagrees with the derived list ({len(hidden)})")
    if not sels: p.append("the collection is EMPTY: every factory game would be hidden and the TV library would be empty")
    return p

def fingerprint(man: dict) -> str:
    """Stable hash of the collection-defining content (selections + decisions + personal identities), for drafts and conflict checks."""
    n = normalize(man); core = sorted((g.get("game_id"), g.get("revision_decision") or "", g.get("payload_sha256") or "", bool(g.get("multiple_versions"))) for g in n.get("games", []))
    return hashlib.sha256(json.dumps(core, sort_keys=True).encode("utf-8")).hexdigest()
