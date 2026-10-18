"""Explicit, documented genre corrections applied after the source genre is read (catalog.py) and to an existing catalog.json.

The catalog takes each game's genre from the libretro NES genre database (metadat/genre). Where that database is
demonstrably wrong, a correction is listed here, keyed by the stable game_id (first 16 hex digits of the payload SHA-256)
and guarded by the source genre it replaces. If the source ever changes, the override stops applying and is reported,
rather than silently overwriting new data. Values use the catalog's existing genre vocabulary only.

  python genre_overrides.py check      report what would change in reports/catalog.json (no write)
  python genre_overrides.py apply      apply the overrides to reports/catalog.json in place
"""
from __future__ import annotations
import os, sys, json

GENRE_OVERRIDES = {
    "6928e8c589d6d9cc": {"title": "Jackal (USA)", "from": "Racing", "to": "Shooter", "approved": "owner, 2026-10-13",
                         "evidence": "libretro lists every Jackal release as Racing; Wikipedia 'Jackal (video game)': 'a run and gun video game developed and published by Konami' (infobox genre: Run and gun). The catalog files run-and-gun games such as Contra and Super C under Shooter."},
    "ae8684d9075b6665": {"title": "Pirates! (USA)", "from": "Sports", "to": "Strategy", "approved": "owner, 2026-10-13",
                         "evidence": "libretro lists every Pirates! release as Sports; Wikipedia 'Sid Meier's Pirates!': 'an action-adventure strategy video game' (infobox: Action-adventure, strategy); NES port published by Ultra Games, 1991."},
}
SOURCE_LABEL = "owner-approved override (tools/rom-pipeline/genre_overrides.py); libretro-database said {from_}"

def apply_row(row: dict) -> str | None:
    """correct one catalog row in place; returns 'applied', 'already', 'source-changed' or None (no override for this game)"""
    gid = row.get("game_id") or (row.get("payload_sha256") or "")[:16]; o = GENRE_OVERRIDES.get(gid)
    if not o: return None
    if row.get("genre") == o["to"] and row.get("genre_source", "").startswith("owner-approved override"): return "already"
    if row.get("genre") != o["from"]: return "source-changed"
    row["genre"] = o["to"]; row["genre_source"] = SOURCE_LABEL.format(from_=o["from"]); return "applied"

def apply_catalog(games: list) -> dict:
    out = {}
    for g in games:
        r = apply_row(g)
        if r: out[g["game_id"]] = r
    missing = [gid for gid in GENRE_OVERRIDES if gid not in out]
    if missing: out.update({gid: "game-not-in-catalog" for gid in missing})
    return out

def main(argv):
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from common import REPORTS
    p = os.path.join(REPORTS, "catalog.json"); cat = json.load(open(p, encoding="utf-8"))
    res = apply_catalog(cat["games"]); print(json.dumps(res))
    if argv[:1] == ["apply"] and any(v == "applied" for v in res.values()):
        tmp = p + ".tmp"; json.dump(cat, open(tmp, "w", encoding="utf-8"), ensure_ascii=False, indent=1); os.replace(tmp, p); print("written", p)
    return 0 if all(v in ("applied", "already") for v in res.values()) else 1

if __name__ == "__main__": sys.exit(main(sys.argv[1:]))
