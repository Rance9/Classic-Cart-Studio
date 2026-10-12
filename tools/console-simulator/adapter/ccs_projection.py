"""Developer-only adapter: Classic Cart Studio collection manifest (schema nes-classic-collection/4, the document the
Studio exports and the install planner consumes) -> simulator fixture (schema ccs-sim-fixture/1).

This is a projection of the EXISTING model, not a second collection model: every fixture field is derived from manifest
fields that already exist (selections[].game_id / title_id / payload_sha256 / builtin_code / builtin_status /
install_required / revision_decision / display_title / nointro_name / source, hidden_factory_games[], factory_inventory,
external_dependencies[]). Known / estimated / unknown stay separate: file readiness is "unknown" unless the manifest came
from the desktop app with external_dependencies resolved; nothing is invented about the console.

  python ccs_projection.py <manifest.json> <out.json> [--factory-titles <builtin.json>]

The same projection rules are the ones Preview on TV uses (tv.js tvEntryFor / tvProject): kind = builtin, factory-kept,
alternate or added; unselected factory games hidden; order by sort title with a leading "The" ignored. The simulator does
not render a menu; the future launcher will consume this fixture (or a successor version) through its own adapter.
"""
from __future__ import annotations
import json, sys, hashlib, os, re

SCHEMA_IN = "nes-classic-collection/4"; SCHEMA_OUT = "ccs-sim-fixture/1"

def sort_title(t: str) -> str:
    t = (t or "").strip(); low = t.lower()
    return (low[4:] if low.startswith("the ") else low)

def kind_for(sel: dict) -> str:
    st = (sel.get("builtin_status") or ""); dec = sel.get("revision_decision") or ""; inst = sel.get("install_required")
    if st.startswith("BUILT-IN: EXACT") or (sel.get("builtin_code") and not inst): return "factory-visible"
    if sel.get("builtin_code") and dec == "alternate": return "alternate"
    if sel.get("builtin_code") and inst: return "alternate" if dec == "alternate" else "factory-visible"
    return "personal" if sel.get("source") == "personal" else "added"

def ready_for(sel: dict, deps: dict[str, str], from_desktop: bool) -> str:
    if not sel.get("install_required"): return "n/a"
    if not from_desktop: return "unknown"
    h = sel.get("payload_sha256")
    return {"available": "ready", "missing": "missing"}.get(deps.get(h or "", ""), "unknown")

def project(manifest: dict, factory_titles: dict[str, str] | None = None) -> dict:
    if manifest.get("schema") != SCHEMA_IN: raise ValueError(f"manifest schema {manifest.get('schema')!r} is not {SCHEMA_IN}")
    from_desktop = bool((manifest.get("status") or {}).get("prepared_on_pc"))
    deps = {d.get("content_sha256") or "": d.get("status") or "" for d in manifest.get("external_dependencies") or []}
    entries = []; seen = set()
    for i, sel in enumerate(manifest.get("selections") or []):
        gid = sel.get("game_id") or sel.get("version_id")
        if not gid: entries.append({"id": "", "title": sel.get("display_title") or "", "kind": "added", "version_id": "", "note": "manifest selection without game_id: left for the core to reject"}); continue
        if gid in seen: entries.append({"id": gid, "version_id": sel.get("payload_sha256") or gid, "title": sel.get("display_title") or "", "kind": "added", "note": "duplicate selection in manifest: left for the core to reject"}); continue
        seen.add(gid); kind = kind_for(sel)
        title = (factory_titles or {}).get(sel.get("builtin_code") or "", None) if kind == "factory-visible" else None
        title = title or sel.get("display_title") or sel.get("title") or gid
        entries.append({"id": gid, "title_id": sel.get("title_id") or "", "version_id": sel.get("payload_sha256") or sel.get("builtin_factory_version") or gid, "title": title,
                        "version_label": sel.get("nointro_name") or sel.get("version_label") or ("factory version" if kind == "factory-visible" else ""), "kind": kind, "ready": ready_for(sel, deps, from_desktop),
                        "sort_key": sort_title(title), "order": 0, "group": "", "art_ref": "", "source_hash": sel.get("payload_sha256") or ""})
    for h in manifest.get("hidden_factory_games") or []:
        code = h.get("code") or ""; entries.append({"id": f"factory:{code}", "version_id": "factory", "title": h.get("title") or code, "kind": "factory-hidden", "ready": "n/a", "sort_key": sort_title(h.get("title") or code), "order": 0, "visible": False})
    valid = [e for e in entries if "note" not in e]; passthrough = [e for e in entries if "note" in e]   # malformed selections go last, so the core rejects them, never a valid record
    valid.sort(key=lambda e: (e.get("sort_key", ""), e.get("id", ""))); entries = valid + passthrough
    for i, e in enumerate(entries): e["order"] = i
    inv = manifest.get("factory_inventory") or {}
    body = json.dumps(entries, sort_keys=True, ensure_ascii=False).encode("utf-8")
    return {"schema": SCHEMA_OUT, "name": manifest.get("name") or "(unnamed collection)", "source": SCHEMA_IN, "source_hash": hashlib.sha256(json.dumps(manifest, sort_keys=True).encode("utf-8")).hexdigest(),
            "projection": {"rules": "tv.js tvEntryFor/tvProject equivalents: unselected factory games hidden, title order with a leading The ignored", "readiness_level": "known" if from_desktop else "unknown", "factory_inventory_sha256": inv.get("sha256"), "entries_sha256": hashlib.sha256(body).hexdigest()},
            "entries": entries}

def main():
    if len(sys.argv) < 3: print(__doc__); sys.exit(2)
    ft = None
    if "--factory-titles" in sys.argv:
        p = sys.argv[sys.argv.index("--factory-titles") + 1]; d = json.load(open(p, encoding="utf-8"))
        games = d.get("games") if isinstance(d, dict) else d; ft = {g.get("code"): g.get("title") for g in games or [] if g.get("code")}
    m = json.load(open(sys.argv[1], encoding="utf-8")); out = project(m, ft)
    with open(sys.argv[2], "w", encoding="utf-8", newline="\n") as f: json.dump(out, f, ensure_ascii=False, indent=0, sort_keys=True); f.write("\n")
    print(f"wrote {sys.argv[2]}: {len(out['entries'])} entries (readiness {out['projection']['readiness_level']})")

if __name__ == "__main__": main()
