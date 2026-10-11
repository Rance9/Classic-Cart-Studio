"""The installation plan: one immutable, hashed document derived from the collection manifest (through the existing
bridge planner, which validates exact versions, factory decisions, payload hashes and identities) plus whichever device
state is truly known (architecture section 7). It drives preflight, the storage budget, the simulated or future physical
operation, the read-back comparison and the journal. Nothing in it is a device claim unless `device.source` says so.
"""
from __future__ import annotations
import hashlib, json, os, time, zlib

SCHEMA = "ccs-install-plan/1"
HEADER_LEN = 16

# Modules each emulator route needs on the console (docs/decisions.md D-06 rev. 2; the catalog's launch command names the core)
CORE_MODULES = {
    "clover-kachikachi-wr": [],
    "km_fceumm": ["_km_retroarch_1910_ozone", "_km_fceumm_xtreme"],
    "km_nestopia": ["_km_retroarch_1910_ozone", "_km_nestopia"],
}
MODULE_PINS = {   # SHA-256 placeholders are filled from the Stage 7 record when a module is actually obtained; unknown means "not cleared"
    "_km_retroarch_1910_ozone": None, "_km_fceumm_xtreme": None, "_km_nestopia": None,
}
ART_BYTES, ART_SMALL_BYTES, DESKTOP_BYTES, METADATA_BYTES = 40_000, 6_000, 700, 180


def _bridge():
    import hakchi_bridge as hb   # the companion's PC-side planner (validated manifest -> rows)
    return hb


def _code_for_custom(hb, item: dict) -> tuple[str, int, int]:
    """(hakchi code, size, headerless crc32) for a custom game from its payload, exactly as apply() derives them."""
    path = item.get("payload_path"); size = 0; crc = 0
    if path and os.path.exists(path):
        size = os.path.getsize(path)
        with open(path, "rb") as f: data = f.read()
        if item.get("headered_file") or (item.get("header_hex") and data[:4] == b"NES\x1a"): data = data[HEADER_LEN:]
        crc = zlib.crc32(data) & 0xffffffff
        return hb.code_for(crc), size, crc
    return "CLV-H-?????", size, crc


def build(manifest: dict, *, device: dict | None = None, foreign: dict | None = None, approve_alternates: bool = False, keep_foreign: bool = True) -> dict:
    """Build the plan. `device` is the identification result (from a simulated or, later, a real inspection) or None;
    `foreign` maps codes already on the device that the plan does not own (from the inspection) to their file sets."""
    hb = _bridge(); pv = hb.plan(manifest, approve_alternates=approve_alternates)
    entries, modules_needed, problems, warnings = [], {}, list(pv["problems"]), list(pv["warnings"])
    custom_by_id = {it["game"]["game_id"]: it for it in pv["custom"]}
    for row in pv["games"]:
        st = row.get("statuses") or []; kind = row.get("kind") or ""
        if kind == "factory" and "factory game hidden" in st:
            entries.append({"code": row["code"], "kind": "factory-hidden", "title": row["title"], "action": "hide", "size": 0, "core": "clover-kachikachi-wr", "requires": []})
        elif kind == "factory" or kind.startswith("factory") and "already installed" in st:
            entries.append({"code": row["code"], "kind": "factory-visible", "title": row["title"], "action": "keep", "size": 0, "core": "clover-kachikachi-wr", "requires": [], "version_id": row.get("game_id")})
        elif row.get("game_id") in custom_by_id:
            it = custom_by_id[row["game_id"]]; g = it["game"]; code, size, crc = _code_for_custom(hb, it)
            exec_cmd, core = hb.exec_for(g, code, g.get("nointro_name") or "rom.nes")
            req = CORE_MODULES.get(core); kind2 = "factory-alternate" if kind.startswith("factory") else ("personal" if g.get("source") == "personal" else "added")
            if req is None: problems.append(f"{row['title']}: emulator core {core!r} has no pinned module route"); req = ["?"]
            for m in req: modules_needed[m] = modules_needed.get(m, 0) + 1
            entries.append({"code": code, "kind": kind2, "title": row["title"], "action": "add", "size": size, "sha256": g.get("payload_sha256"), "version_id": g.get("game_id"), "crc32": crc, "core": core, "exec": exec_cmd, "requires": req,
                            "files": {f"{code}/{code}.desktop": DESKTOP_BYTES, f"{code}/{code}.png": ART_BYTES, f"{code}/{code}_small.png": ART_SMALL_BYTES, f"{code}/metadata.json": METADATA_BYTES, f"{code}/{(g.get('nointro_name') or 'rom.nes').replace('/', '_')}.nes" if not (g.get('nointro_name') or '').endswith('.nes') else f"{code}/{g.get('nointro_name')}": size + (HEADER_LEN if g.get("headered_file") is False else 0)}})
        else:
            entries.append({"code": row.get("code") or "", "kind": "unresolved", "title": row["title"], "action": "none", "size": 0, "core": "", "requires": [], "detail": row.get("detail", ""), "statuses": st})
    for code, files in (foreign or {}).items():
        entries.append({"code": code, "kind": "foreign-keep" if keep_foreign else "foreign-remove", "title": f"existing game {code} (not from this collection)", "action": "keep-unknown" if keep_foreign else "remove", "size": sum(files.values()), "core": "", "requires": [], "files": dict(files)})
    installed = set((device or {}).get("modules") or [])
    modules = [{"name": m, "needed_by": n, "pinned_sha256": MODULE_PINS.get(m), "action": "present" if m in installed else ("install" if device else "unknown"), "cleared": MODULE_PINS.get(m) is not None}
               for m, n in sorted(modules_needed.items())]
    add_bytes = sum(sum(e.get("files", {}).values()) for e in entries if e["action"] == "add")
    keep_foreign_bytes = sum(e["size"] for e in entries if e["kind"] == "foreign-keep")
    dev = device or {"source": "none", "measured": False}
    if dev.get("storage"):
        st = dev["storage"]; total, used, free = st["total"], st["used"], st["free"]
        src = "simulated" if dev.get("simulated") else "measured"
    else:
        from .profiles import STORAGE_REF
        total = STORAGE_REF["total"]; used = None; free = None; src = "reference"
    margin = 8 * 1024 * 1024 + int(0.05 * add_bytes)
    fits = None if free is None else (free - margin - sum(0 for _ in ()) >= add_bytes)
    budget = {"source": src, "total": total, "used": used, "free": free, "proposed_add_bytes": add_bytes, "foreign_kept_bytes": keep_foreign_bytes, "margin": margin, "fits": fits,
              "text": {"reference": "estimated from a reference NES Classic, not your console", "simulated": "simulated console figures, not a real measurement", "measured": "measured on the connected console"}[src]}
    firmware = {"state": dev.get("firmware_state", "unknown"), "needs_first_time_setup": dev.get("needs_first_time_setup")}
    backup = {"state": dev.get("backup_state", "unknown"), "artifacts": dev.get("backup_artifacts", [])}
    if any(m["action"] == "install" and not m["cleared"] for m in modules): warnings.append("a required emulator module is not cleared for distribution; the Studio will not download it, you obtain it yourself (compliant route)")
    plan = {"schema": SCHEMA, "created": time.strftime("%Y-%m-%dT%H:%M:%S"), "collection": {"name": manifest.get("name") or manifest.get("collection_name") or "", "schema": manifest.get("schema"), "games": pv["summary"]["collection"]},
            "device": dev, "entries": entries, "modules": modules, "budget": budget, "firmware": firmware, "backup": backup,
            "summary": {**pv["summary"], "adds": sum(1 for e in entries if e["action"] == "add"), "hides": sum(1 for e in entries if e["action"] == "hide"), "keeps": sum(1 for e in entries if e["action"] == "keep"),
                        "foreign": sum(1 for e in entries if e["kind"].startswith("foreign")), "unresolved": sum(1 for e in entries if e["kind"] == "unresolved")},
            "problems": problems, "warnings": warnings, "can_proceed": not problems and pv["can_apply"]}
    plan["hash"] = hashlib.sha256(json.dumps({k: v for k, v in plan.items() if k not in ("created", "hash")}, sort_keys=True, default=str).encode()).hexdigest()
    return plan


def local_file_set(plan: dict) -> dict[str, int]:
    """What the sync would place on the device: every added entry's files plus every kept foreign entry (so the
    upstream-style differential never deletes it)."""
    out: dict[str, int] = {}
    for e in plan["entries"]:
        if e["action"] in ("add", "keep-unknown"):
            for rel, size in (e.get("files") or {}).items(): out[rel] = size
    return out


def expected_after(plan: dict) -> dict[str, int]:
    """The read-back the verify step must find (same as the local set in this version: hidden factory games are a
    config change, not files)."""
    return local_file_set(plan)


def save(plan: dict, folder: str) -> str:
    os.makedirs(folder, exist_ok=True); p = os.path.join(folder, f"plan-{plan['hash'][:16]}.json")
    if not os.path.exists(p):
        with open(p, "w", encoding="utf-8") as f: json.dump(plan, f, indent=1, ensure_ascii=False)
    return p


def load(folder: str, plan_hash: str) -> dict | None:
    p = os.path.join(folder, f"plan-{plan_hash[:16]}.json")
    if not os.path.exists(p): return None
    with open(p, encoding="utf-8") as f: plan = json.load(f)
    return plan if plan.get("hash") == plan_hash else None
