"""Console HOME sync contract (Windows side), against a MOCK console store. Gate G1 is NOT approved: there is no transport to
a physical console here and none is opened; the "console" is a JSON document in the Studio's data folder that stands in for
the device's state so the reconciliation rules can be exercised end to end.

Schemas
  ccs-home-sync-manifest/1   Windows -> console: installed collection (ids, versions, canonical order), folders with their
                             own orders, map shortcuts, settings, plus the two-way parts below
  ccs-home-sync-state/1      two-way: favorites {gen, items{id:{on,rev,at}}} and history {events[{id,game,at,gen}],
                             cleared_gen, cleared_at}

Rules (the same as studio/home-model.js homeReconcile*, kept identical on purpose; test_home_sync.py cross-checks them)
  favorites  per item: higher rev wins; equal rev -> later `at`; never a whole-list overwrite
  history    union by event id; the newest clear (highest cleared_gen, latest cleared_at) is a high-water mark: events at or
             before it are dropped on both sides, so a stale peer cannot resurrect cleared entries; later launches survive
  folders    Windows-owned: the console copy is replaced by the manifest's; console edits to folders are not accepted
  games      Windows-owned: a game missing from the manifest is no longer launchable; its history events stay as facts
"""
from __future__ import annotations
import json, os, time, hashlib

SCHEMA_MANIFEST = "ccs-home-sync-manifest/1"; SCHEMA_STATE = "ccs-home-sync-state/1"

def _now(): return time.strftime("%Y-%m-%dT%H:%M:%S.000Z", time.gmtime())

def reconcile_favorites(a: dict | None, b: dict | None) -> dict:
    a = a or {"gen": 0, "items": {}}; b = b or {"gen": 0, "items": {}}
    out = {"gen": max(a.get("gen", 0), b.get("gen", 0)), "items": {}}
    for gid in set(a.get("items", {})) | set(b.get("items", {})):
        x, y = a.get("items", {}).get(gid), b.get("items", {}).get(gid)
        if not x: out["items"][gid] = y
        elif not y: out["items"][gid] = x
        elif x.get("rev", 0) != y.get("rev", 0): out["items"][gid] = x if x.get("rev", 0) > y.get("rev", 0) else y
        else: out["items"][gid] = x if (x.get("at") or "") >= (y.get("at") or "") else y
    return out

def reconcile_history(a: dict | None, b: dict | None) -> dict:
    a = a or {"events": [], "cleared_gen": 0, "cleared_at": None}; b = b or {"events": [], "cleared_gen": 0, "cleared_at": None}
    gen = max(a.get("cleared_gen", 0), b.get("cleared_gen", 0)); ats = [x for x in (a.get("cleared_at"), b.get("cleared_at")) if x]; at = max(ats) if ats else None
    seen = {}
    for e in list(a.get("events", [])) + list(b.get("events", [])):
        if at and e.get("at", "") <= at: continue
        if e.get("id") and e["id"] not in seen: seen[e["id"]] = e
    events = sorted(seen.values(), key=lambda e: (e.get("at", ""), e.get("id", "")))
    return {"events": [dict(e, gen=gen) for e in events], "cleared_gen": gen, "cleared_at": at}

def reconcile_state(local: dict, remote: dict) -> dict:
    return {"schema": SCHEMA_STATE, "favorites": reconcile_favorites(local.get("favorites"), remote.get("favorites")), "history": reconcile_history(local.get("history"), remote.get("history"))}

def validate_manifest(m: dict) -> list[str]:
    p = []
    if not isinstance(m, dict) or m.get("schema") != SCHEMA_MANIFEST: return [f"schema is not {SCHEMA_MANIFEST}"]
    ids = set()
    for g in m.get("games", []):
        if not g.get("id"): p.append("game without id")
        elif g["id"] in ids: p.append(f"duplicate game id {g['id']}")
        else: ids.add(g["id"])
        if not g.get("version_id"): p.append(f"game {g.get('id')} without version_id")
    if sorted(m.get("order", [])) != sorted(ids) or len(m.get("order", [])) != len(ids): p.append("order must list every game exactly once")
    fids = {f.get("id") for f in m.get("folders", [])}
    for f in m.get("folders", []):
        if not f.get("id") or not f.get("name"): p.append("folder without id or name")
        if f.get("parent") and f["parent"] not in fids: p.append(f"folder {f.get('name')} has a missing parent")
        for gid in f.get("order", []):
            if gid not in ids: p.append(f"folder {f.get('name')} lists a game that is not installed: {gid}")
        d, cur, guard = 0, f, 0
        while cur and guard < 64:
            d += 1; cur = next((x for x in m.get("folders", []) if x.get("id") == cur.get("parent")), None) if cur.get("parent") else None; guard += 1
        if d > 5: p.append(f"folder {f.get('name')} nested deeper than 5")
    for s in m.get("map_shortcuts", [None, None])[:2]:
        if s and s not in fids: p.append(f"map shortcut points to a missing folder {s}")
    return p

class MockConsoleStore:
    """stands in for the device: one JSON file, atomic replace on write, generation counter, never a real device"""
    def __init__(self, path: str): self.path = path
    def load(self) -> dict:
        if not os.path.exists(self.path): return {"schema": "ccs-mock-console/1", "simulation": True, "generation": 0, "manifest": None, "state": {"schema": SCHEMA_STATE, "favorites": {"gen": 0, "items": {}}, "history": {"events": [], "cleared_gen": 0, "cleared_at": None}}}
        return json.load(open(self.path, encoding="utf-8"))
    def save(self, doc: dict):
        doc["simulation"] = True; doc["generation"] = doc.get("generation", 0) + 1; doc["saved"] = _now(); tmp = self.path + ".tmp"
        os.makedirs(os.path.dirname(self.path), exist_ok=True); json.dump(doc, open(tmp, "w", encoding="utf-8"), ensure_ascii=False, indent=1); os.replace(tmp, self.path); return doc
    def console_launch(self, game_id: str, at: str | None = None, event_id: str | None = None) -> dict:
        """what the device would do when a game is launched (used by tests and the rehearsal): append an event"""
        doc = self.load(); h = doc["state"]["history"]; ev = {"id": event_id or ("ev:" + hashlib.sha1(f"{game_id}{at or _now()}{len(h['events'])}".encode()).hexdigest()[:12]), "game": game_id, "at": at or _now(), "gen": h.get("cleared_gen", 0)}
        h["events"].append(ev); self.save(doc); return ev
    def console_favorite(self, game_id: str, on: bool, at: str | None = None) -> dict:
        doc = self.load(); it = doc["state"]["favorites"]["items"].get(game_id, {"on": False, "rev": 0});
        if bool(it.get("on")) == bool(on): return it
        it = {"on": bool(on), "rev": it.get("rev", 0) + 1, "at": at or _now()}; doc["state"]["favorites"]["items"][game_id] = it; doc["state"]["favorites"]["gen"] = doc["state"]["favorites"].get("gen", 0) + 1; self.save(doc); return it
    def console_clear_history(self, at: str | None = None) -> int:
        doc = self.load(); h = doc["state"]["history"]; h["cleared_gen"] = h.get("cleared_gen", 0) + 1; h["cleared_at"] = at or _now(); h["events"] = [e for e in h["events"] if e.get("at", "") > h["cleared_at"]]; self.save(doc); return h["cleared_gen"]

def sync(store: MockConsoleStore, manifest: dict, local_state: dict) -> dict:
    """one sync round against the mock console: validate, reconcile the two-way parts, push the Windows-owned parts, return
    the merged state (the Windows side adopts it too). Deterministic and idempotent: a second call changes nothing."""
    problems = validate_manifest(manifest)
    if problems: return {"ok": False, "problems": problems, "simulation": True}
    doc = store.load(); before = json.dumps(doc.get("state"), sort_keys=True)
    merged = reconcile_state(local_state or {}, doc.get("state") or {})
    installed = set(manifest["order"])
    merged["favorites"]["items"] = {k: v for k, v in merged["favorites"]["items"].items() if k in installed}   # favorites of removed games are dropped; history stays as facts
    doc["manifest"] = {k: manifest[k] for k in ("schema", "games", "order", "folders", "map_shortcuts", "settings", "theme") if k in manifest}; doc["state"] = merged
    changed = json.dumps(merged, sort_keys=True) != before; store.save(doc)
    return {"ok": True, "simulation": True, "changed": changed, "state": merged, "console_generation": doc["generation"], "installed": len(installed), "folders": len(manifest.get("folders", [])), "note": "mock console store; no device was contacted (Gate G1 not approved)"}
