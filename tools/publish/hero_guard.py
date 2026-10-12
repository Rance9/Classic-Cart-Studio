"""Release safeguard for hero key art: only owner-approved game-specific images leave the workspace.

Used by build_public_repo.py (site build and public repo tree) and testable on its own:
  approved_ids(registry)              -> set of game ids whose registry status is "approved"
  prune_hero_dir(folder, approved)    -> removes every <id>.png not approved, the rejected/ folder, stray files; rewrites index.json with approved records only
  prune_private_art(folder)           -> removes hero-requests/ and hero-batch/ (briefs, references, batch work)
  suppress_catalog(games, approved)   -> strips art_hero* fields from catalog rows whose image is not approved, so no page references a missing file
The universal fallback (tools/publish/assets/home/hero-fallback.png) is an approved shared asset and is not touched here;
the runtime shows it for every game without an approved hero (hmKeyArt in home-themes.js).
"""
from __future__ import annotations
import os, json, shutil

HERO_FIELDS = ("art_hero", "art_hero_source", "art_hero_sha256", "art_hero_status", "art_hero_provenance", "art_hero_title")

def load_registry(path: str) -> dict:
    try: return json.load(open(path, encoding="utf-8")).get("games", {})
    except Exception: return {}

def approved_ids(registry: dict) -> set:
    return {gid for gid, rec in registry.items() if isinstance(rec, dict) and rec.get("status") == "approved"}

def prune_hero_dir(folder: str, approved: set) -> dict:
    out = {"removed": [], "kept": []}
    if not os.path.isdir(folder): return out
    if os.path.isdir(os.path.join(folder, "rejected")): shutil.rmtree(os.path.join(folder, "rejected"), ignore_errors=True); out["removed"].append("rejected")
    reg = load_registry(os.path.join(folder, "index.json"))
    for name in sorted(os.listdir(folder)):
        p = os.path.join(folder, name)
        if os.path.isdir(p): shutil.rmtree(p, ignore_errors=True); out["removed"].append(name); continue
        if name == "index.json": continue
        gid, ext = os.path.splitext(name)
        if ext.lower() == ".png" and gid in approved: out["kept"].append(name)
        else:
            try: os.remove(p); out["removed"].append(name)
            except OSError: pass
    json.dump({"schema": "ccs-hero-art/1", "games": {k: v for k, v in reg.items() if k in approved}}, open(os.path.join(folder, "index.json"), "w", encoding="utf-8"), indent=1)
    return out

def prune_private_art(art_folder: str):
    """remove request packs and batch work; Windows sometimes refuses to delete freshly emptied directories, so files go first and leftover empty folders are tolerated (nothing tracked)"""
    for sub in ("hero-requests", "hero-batch"):
        root = os.path.join(art_folder, sub)
        if not os.path.isdir(root): continue
        for dp, dn, fn in os.walk(root, topdown=False):
            for f in fn:
                try: os.remove(os.path.join(dp, f))
                except OSError: pass
            for d in dn:
                try: os.rmdir(os.path.join(dp, d))
                except OSError: pass
        try: os.rmdir(root)
        except OSError: pass

def suppress_catalog(games: list, approved: set) -> int:
    n = 0
    for g in games:
        if not isinstance(g, dict): continue
        if g.get("art_hero") and g.get("game_id") not in approved:
            for k in HERO_FIELDS: g.pop(k, None)
            n += 1
        elif g.get("art_hero") and g.get("art_hero_status") != "approved":
            for k in HERO_FIELDS: g.pop(k, None)
            n += 1
    return n
