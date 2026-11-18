r"""The five shared HOME scenes: the one rule for every game's background, read from the one shared definition
(site-data/art/scenes/index.json, served as art/scenes/index.json) that the website's HOME also uses (build_html.py embeds it as
HOME_SCENES; home-model.js homeScene() is this function's twin). A game's scene follows its genre; a game without a listed genre (no genre,
imported, personal) gets the fallback scene. Nothing a collection or package stores chooses a scene, so a retired background can never return."""
from __future__ import annotations
import functools, json, os

SCHEMA = "ccs-scenes/1"
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.normpath(os.path.join(HERE, "..", "..", ".."))

@functools.lru_cache(maxsize=1)
def definition() -> dict:
    cands = []
    try:
        from common import site_path   # the installed app's site, when there is one
        cands.append(site_path("art", "scenes", "index.json"))
    except Exception: pass
    cands.append(os.path.join(REPO, "site-data", "art", "scenes", "index.json"))
    for p in cands:
        if os.path.isfile(p):
            d = json.load(open(p, encoding="utf-8"))
            if d.get("schema") != SCHEMA or d.get("fallback") not in d.get("scenes", {}) or any(v not in d["scenes"] for v in d.get("genres", {}).values()):
                raise ValueError(f"{p}: not a valid {SCHEMA} definition")
            return d
    raise FileNotFoundError("the shared scenes definition (art/scenes/index.json) was not found")

def scene_for(genre: str | None) -> dict:
    """{"id", "file" (site-relative), "title" (the lettering's box)} for a game of this genre"""
    d = definition(); sid = d["genres"].get(genre or "") or d["fallback"]; s = d["scenes"][sid]
    return {"id": sid, "file": s["file"], "title": dict(s.get("title") or {"x": 0.5, "y": 0.09})}
