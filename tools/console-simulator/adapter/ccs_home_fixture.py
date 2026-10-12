"""Developer-only adapter: the console HOME model (schema ccs-home-collection/1, what Preview on TV and the Windows app keep)
-> Console Simulator fixture (ccs-sim-fixture/1), so the shared C core can exercise the same All Games order, hidden factory
titles and readiness that the HOME runtime shows. The simulator renders no HOME design; it proves data handling and lifecycle
on the host and under QEMU (see tools/console-simulator/ARCHITECTURE.md).

  python ccs_home_fixture.py <home-model.json> <out.json>
The input is the JSON the HOME runtime persists (window.TV.model) or a `homeFixture()` document from node.
"""
from __future__ import annotations
import json, sys, hashlib

SCHEMA_IN = "ccs-home-collection/1"; SCHEMA_OUT = "ccs-sim-fixture/1"
KIND = {"factory": "factory-visible", "added": "added", "personal": "personal", "alternate": "alternate"}

def project(model: dict) -> dict:
    if model.get("schema") != SCHEMA_IN: raise ValueError(f"model schema {model.get('schema')!r} is not {SCHEMA_IN}")
    by = {g["id"]: g for g in model.get("games", [])}; entries = []
    for i, gid in enumerate(model.get("order", [])):
        g = by.get(gid)
        if not g: entries.append({"id": gid, "title": "", "version_id": "", "kind": "added", "note": "order lists an unknown game: left for the core to reject"}); continue
        entries.append({"id": gid, "version_id": g.get("version_id") or "", "title": g.get("title") or "", "sort_key": g.get("sort_title") or "", "kind": KIND.get(g.get("kind"), "added"), "order": i, "visible": True,
                        "ready": "n/a" if g.get("kind") == "factory" else ("ready" if g.get("playable") else "missing"), "group": "", "art_ref": (g.get("art") or {}).get("src") or "", "source_hash": (g.get("art") or {}).get("provenance") or ""})
    folders = [{"id": f.get("id"), "name": f.get("name"), "parent": f.get("parent"), "order": list(f.get("order", []))} for f in model.get("folders", [])]
    body = json.dumps(entries, sort_keys=True).encode("utf-8")
    return {"schema": SCHEMA_OUT, "name": f"HOME: {(model.get('source') or {}).get('name') or 'collection'}", "source": SCHEMA_IN, "source_hash": hashlib.sha256(json.dumps(model, sort_keys=True).encode("utf-8")).hexdigest(),
            "projection": {"rules": "All Games in canonical order; favorites, history and folders carried as metadata (the core's menu is the flat list)", "entries_sha256": hashlib.sha256(body).hexdigest(), "folders": folders, "favorites": [gid for gid, it in ((model.get("favorites") or {}).get("items") or {}).items() if it.get("on")], "theme": model.get("theme")},
            "entries": entries}

def main():
    if len(sys.argv) < 3: print(__doc__); sys.exit(2)
    out = project(json.load(open(sys.argv[1], encoding="utf-8")))
    with open(sys.argv[2], "w", encoding="utf-8", newline="\n") as f: json.dump(out, f, ensure_ascii=False, indent=0, sort_keys=True); f.write("\n")
    print(f"wrote {sys.argv[2]}: {len(out['entries'])} entries, {len(out['projection']['folders'])} folders")

if __name__ == "__main__": main()
