r"""Effective native HOME configuration: the user's collection (schema nes-classic-collection/4, the manifest the Install
stepper already sends to the console planner) plus the HOME model the Studio keeps (theme, canonical order, titles as HOME shows
them, folders, favorites, settings), resolved against the whole library: curated catalog versions, Extended Library versions
(4,465 versions of 3,056 titles) and personal imports. Nothing here depends on the 773-game default catalog being the source.

Rules (the Studio's own, not new ones):
  identity      a HOME entry is the exact version id the collection selected; an id the library cannot resolve is an error
  titles        as the HOME model shows them (factory-kept games carry their factory title there)
  metadata      catalog row; Extended versions: version, then the curated sibling, then the title (studio.js extRow);
                personal: the user's own fields
  cover         catalog: approved art, else the marked generated placeholder; Extended: the version's own scan, else a same-region scan of the same title
                (art_family, N3.1 art audit; studio.js extRow); never another region's box; personal: the user's uploaded art; otherwise none (the runtime's title placeholder)
  hero          only the version's own approved hero (catalog art_hero); otherwise the shared universal fallback backdrop
  texts         the approved description dataset by exact id (Official description + Game overview); other versions of a curated
                title share its approved overview (same game identity); other library identities use the researched library
                overview (reports/descriptions-library.json, by title identity); personal: the user's own description; otherwise none
  overrides     N3: the user's Windows customizations (tools/companion/overrides.py, keyed by the exact version id) win over the
                canonical content field by field: art = user > authentic > NES-style mock cover; overview = user > canonical;
                Official description = user (labeled "edited", or "custom" when no canonical text exists) > canonical > empty
  factory games the collection decides: hidden factory games are not in HOME; kept ones appear with their factory title
  theme         N3: the HOME theme the Windows app chose becomes the package's default theme (all four themes are installed and
                switchable on the console); "apply" asks the console to switch to it once, over its saved choice
"""
from __future__ import annotations
import hashlib, json, os, sys
HERE = os.path.dirname(os.path.abspath(__file__)); TOOLS = os.path.normpath(os.path.join(HERE, "..", ".."))
for p in (os.path.join(TOOLS, "rom-pipeline"), os.path.join(TOOLS, "companion")):
    if p not in sys.path: sys.path.insert(0, p)
import library_lookup as LL   # noqa: E402
from common import site_path, data_path, art_alias, REPORTS   # noqa: E402

CONFIG_SCHEMA = "ccs-native-home-config/1"
HOME_THEMES = ("arcade-grid", "box-art-showcase", "featured-shelf", "adventure-map")
COLLECTION_SCHEMAS = ("nes-classic-collection/4",)

class ConfigError(Exception):
    """the configuration cannot become a package (empty, sample, inconsistent, unknown game): never packaged anyway"""

def _jload(p: str):
    with open(p, encoding="utf-8") as f: return json.load(f)

def _descriptions() -> dict:
    for p in (site_path("descriptions.json"), site_path("site-data", "descriptions.json"), os.path.join(REPORTS, "descriptions.json")):
        if os.path.isfile(p):
            d = _jload(p)
            if d.get("schema") == "ccs-descriptions/1": return d["games"]
    return {}

def _library_overviews() -> dict:
    for p in (site_path("descriptions-library.json"), site_path("site-data", "descriptions-library.json"), os.path.join(REPORTS, "descriptions-library.json")):
        if os.path.isfile(p):
            with open(p, encoding="utf-8") as f: d = json.load(f)
            if d.get("schema") == "ccs-descriptions-library/1": return d.get("titles") or {}
    return {}

def _overrides_module():
    """the Windows override store (tools/companion/overrides.py); absent in a build without customization support"""
    try:
        import overrides as OV   # noqa: F401
        return OV
    except Exception:
        return None

def effective(gid: str, rec: dict) -> dict:
    """applies the user's overrides for this exact version id, field by field (canonical content is never changed)"""
    OV = _overrides_module(); o = {}
    if OV is not None:
        try: o = OV.game(gid) or {}
        except Exception: o = {}
    art = o.get("art") or {}
    if art:
        try: master = OV.art_master_path(gid)
        except Exception: master = None
        if master and os.path.isfile(master): rec["art_user"] = {"path": master, "crop": art.get("crop"), "sha256": art.get("sha256")}
    t = rec.get("texts"); ov_off = (o.get("official") or {}).get("text"); ov_ovr = (o.get("overview") or {}).get("text")
    if rec.get("source") == "personal":
        if ov_ovr: rec["texts"] = {"description": ov_ovr}
        return rec
    if ov_off is not None or ov_ovr is not None:
        canon_off = (t or {}).get("official"); t = dict(t or {"official": None, "overview": None})
        if ov_off is not None: t["official"] = ov_off.strip() or None; t["official_label"] = ("edited" if canon_off else "custom") if t["official"] else None
        if ov_ovr is not None: t["overview"] = ov_ovr.strip() or None
        rec["texts"] = t if (t.get("official") or t.get("overview")) else None
    return rec

def _art_path(rel: str | None) -> str | None:
    """catalog/library art paths are relative to the site (development: reports\\); personal art to the data folder"""
    if not rel or rel.startswith(("http:", "https:", "data:")) or os.path.isabs(rel) or ":" in rel or ".." in rel.replace("\\", "/").split("/"): return None
    for root in (site_path(), REPORTS, data_path()):
        p = os.path.join(root, rel.replace("/", os.sep))
        if os.path.isfile(p): return p
    alias = art_alias(rel.split("?")[0])   # the installed site keeps one copy of byte-identical covers (art/aliases.json)
    if alias != rel.split("?")[0] and os.path.isfile(site_path(*alias.split("/"))): return site_path(*alias.split("/"))
    return None

def _extended(version_id: str) -> dict | None:
    lib = LL._library(); v = lib["versions"].get(version_id)
    if not v: return None
    t = lib["titles"].get(v["title_id"], {}); cat = LL._catalog()
    cur = next((cat[x] for x in (t.get("versions") or []) if x in cat), None)   # the curated sibling of the same title (studio.js CUR_BY_TITLE)
    pick = lambda *xs: next((str(x) for x in xs if x not in (None, "")), None)
    return {"title": t.get("title") or v.get("name"), "version_label": v.get("name"), "year": pick(v.get("year"), cur and cur.get("year"), t.get("year")),
            "players": pick(cur and cur.get("players")), "genre": pick(v.get("genre"), cur and cur.get("genre"), t.get("genre")),
            "publisher": pick(v.get("publisher"), cur and cur.get("publisher"), (t.get("publisher") or "").title() or None), "developer": None,
            "art": v.get("art") or v.get("art_family") or None, "hero": None, "source": "extended", "title_id": v["title_id"]}

def resolve_game(gid: str, descriptions: dict, library_overviews: dict | None = None) -> dict | None:
    """the native record of one version id from wherever the library keeps it (canonical content); None when the id is unknown"""
    cat = LL._catalog(); library_overviews = library_overviews if library_overviews is not None else {}
    if gid in cat:
        g = cat[gid]; s = lambda k: str(g[k]) if g.get(k) not in (None, "") else None
        texts = descriptions.get(gid)
        return {"source": "catalog", "title": g.get("display_title"), "version_label": g.get("nointro_name"), "year": s("year"), "players": s("players"), "genre": s("genre"),
                "publisher": s("publisher"), "developer": s("developer"), "art": g.get("art") or g.get("art_generated") or None, "cover_generated": not g.get("art") and bool(g.get("art_generated")),
                "hero": g.get("art_hero") or None, "hero_title": g.get("art_hero_title") if g.get("art_hero") else None, "texts": {"official": texts.get("official") or None, "overview": texts.get("overview") or None} if texts is not None and (texts.get("official") or texts.get("overview")) else None,
                "builtin_code": g.get("builtin_code") or None, "title_id": LL.title_id_for(gid)}
    if gid.startswith("p_"):
        e = LL.personal_index()["entries"].get(gid)
        if not e: return None
        u = e.get("user") or {}; det = e.get("detected") or {}
        return {"source": "personal", "title": u.get("title") or det.get("title") or e.get("file_name"), "version_label": u.get("version_label") or None, "year": u.get("year") or None,
                "players": None, "genre": None, "publisher": u.get("publisher") or None, "developer": None, "art": u.get("art") or None, "hero": None,
                "texts": {"description": u.get("description")} if u.get("description") else None, "title_id": e.get("title_id") or None}
    x = _extended(gid)
    if x:   # another version of a curated title is the same game: it shares the approved overview (never the release-specific
        # Official text); any other identity uses its researched library overview, if one exists
        lib = LL._library(); t = lib["titles"].get(x["title_id"], {}); sib = next((v for v in (t.get("versions") or []) if v in cat and (descriptions.get(v) or {}).get("overview")), None)
        le = library_overviews.get(x["title_id"]) or {}
        ov = (descriptions.get(sib) or {}).get("overview") if sib else le.get("overview")
        off = le.get("official")   # a verified Official description of this identity's own release (Nintendo of Europe page), if one exists
        x["texts"] = {"official": off, "overview": ov} if (ov or off) else None; x["overview_source"] = "curated-title" if sib else ("library" if ov else None)
    return x

def fingerprint(obj) -> str: return hashlib.sha256(json.dumps(obj, sort_keys=True, ensure_ascii=False, separators=(",", ":")).encode("utf-8")).hexdigest()

def resolve(manifest: dict, home: dict) -> dict:
    """the effective configuration (deployment): exactly the games the configured HOME can show, in its order"""
    if not isinstance(manifest, dict) or manifest.get("schema") not in COLLECTION_SCHEMAS: raise ConfigError("the collection is missing or not a nes-classic-collection/4 document")
    if not isinstance(home, dict): raise ConfigError("the HOME configuration is missing")
    if home.get("sample"): raise ConfigError("the tray is empty: the HOME shows a sample of the built-in games, which is not a configuration to install")
    theme = home.get("theme")
    if theme not in HOME_THEMES: raise ConfigError(f"unknown HOME theme {theme!r}")
    apply_theme = bool(home.get("apply_theme"))   # the user asked to apply this default on the console at the next installation
    sels = [s for s in manifest.get("selections") or [] if isinstance(s, dict)]
    ids = [s.get("game_id") or s.get("version_id") for s in sels]
    if not sels or not all(ids): raise ConfigError("the collection selects no games (an empty selection is never packaged)")
    if len(set(ids)) != len(ids): raise ConfigError("the collection lists a game twice")
    order = [str(x) for x in home.get("order") or []]
    if sorted(order) != sorted(ids): raise ConfigError("the HOME order does not list exactly the collection's games (refresh the Preview on TV and try again)")
    hg = {g.get("id"): g for g in home.get("games") or [] if isinstance(g, dict)}
    by_sel = {(s.get("game_id") or s.get("version_id")): s for s in sels}; D = _descriptions(); LO = _library_overviews(); games = []; unknown = []
    for gid in order:
        rec = resolve_game(gid, D, LO)
        if rec is None: unknown.append(gid); continue
        s = by_sel[gid]; h = hg.get(gid) or {}
        rec["id"] = gid; rec["title"] = h.get("title") or s.get("display_title") or rec["title"]; rec["version_label"] = h.get("version_label") if h.get("version_label") is not None else rec.get("version_label")
        rec["home_kind"] = h.get("kind") or ("factory" if s.get("builtin_code") and not s.get("install_required") else ("personal" if s.get("source") == "personal" else "added"))
        rec["builtin_code"] = s.get("builtin_code") or rec.get("builtin_code"); rec["install_required"] = bool(s.get("install_required"))
        games.append(effective(gid, rec))
    if unknown: raise ConfigError(f"{len(unknown)} selected game(s) are not in this library: {', '.join(unknown[:5])}")
    installed = set(order)
    folders = [{"id": f.get("id"), "name": f.get("name"), "parent": f.get("parent"), "pos": f.get("pos") or 0, "order": [x for x in f.get("order") or [] if x in installed], "art": bool((f.get("art") or {}).get("src"))}
               for f in home.get("folders") or [] if isinstance(f, dict) and f.get("id")]
    favorites = sorted(k for k, v in ((home.get("favorites") or {}).get("items") or {}).items() if isinstance(v, dict) and v.get("on") and k in installed)
    hidden = sorted(h.get("code") for h in manifest.get("hidden_factory_games") or [] if isinstance(h, dict) and h.get("code"))
    by_folder = {f["id"]: f["name"] for f in folders}   # Adventure Map's two folder shortcuts (labels of their signs)
    shortcuts = [by_folder.get(x) if isinstance(x, str) else None for x in (home.get("map_shortcuts") or [None, None])[:2]]
    while len(shortcuts) < 2: shortcuts.append(None)
    shortcut_ids = [x if isinstance(x, str) and x in by_folder else None for x in (home.get("map_shortcuts") or [None, None])[:2]]
    while len(shortcut_ids) < 2: shortcut_ids.append(None)
    cfg = {"schema": CONFIG_SCHEMA, "mode": "deployment", "theme": theme, "theme_apply": apply_theme, "themes_enabled": list(HOME_THEMES), "collection": {"name": manifest.get("name"), "fingerprint": fingerprint(manifest.get("selections"))},
           "games": games, "folders": folders, "favorites": favorites, "hidden_factory_codes": hidden, "map_shortcuts": shortcuts, "map_shortcut_ids": shortcut_ids, "settings": {k: v for k, v in (home.get("settings") or {}).items() if k in ("reduced_motion", "hints")}}
    cfg["fingerprint"] = fingerprint({k: cfg[k] for k in ("theme", "theme_apply", "themes_enabled", "games", "folders", "favorites", "hidden_factory_codes", "map_shortcuts", "settings")})
    cfg["content_fingerprint"] = fingerprint({k: cfg[k] for k in ("theme", "games", "folders", "favorites", "hidden_factory_codes", "map_shortcuts", "settings")})   # what the HOME shows (the one-time apply request excluded)
    return cfg

def development_full_catalog(theme: str = "arcade-grid") -> dict:
    """development only (explicit): every curated catalog game, for exercising the renderer at scale; never an installation"""
    D = _descriptions(); cat = LL._catalog(); games = []
    for gid in cat:
        rec = resolve_game(gid, D, {}); rec["id"] = gid; g = cat[gid]
        # tv.js tvEntryFor with the default decisions (the development exporter's entry_for): factory-kept games show the factory
        # title and version; games already on the console are "factory"
        st = g.get("builtin_status") or "NOT BUILT-IN"; kept = st in ("BUILT-IN: DIFFERENT REVISION", "BUILT-IN: IDENTITY ONLY"); install = st not in ("BUILT-IN: EXACT MATCH",) and not kept
        if kept: rec["title"] = g.get("builtin_title") or g.get("display_title")
        version = (g.get("builtin_factory_version") or "factory version") if kept else (g.get("nointro_name") or g.get("display_title"))
        rec["version_label"] = ("" if version == "factory version" else version) or ("factory version" if g.get("builtin_code") and not install else "")
        rec["home_kind"] = "factory" if g.get("builtin_code") and not install else "added"; games.append(rec)
    key = lambda r: ((r["title"] or "").lower()[4:] if (r["title"] or "").lower().startswith("the ") else (r["title"] or "").lower(), r["id"])
    games.sort(key=key)
    cfg = {"schema": CONFIG_SCHEMA, "mode": "development", "theme": theme, "theme_apply": False, "themes_enabled": list(HOME_THEMES), "collection": {"name": "development: full default catalog", "fingerprint": None},
           "games": games, "folders": [], "favorites": [], "hidden_factory_codes": [], "settings": {}}
    cfg["fingerprint"] = fingerprint({k: cfg[k] for k in ("theme", "games")}); return cfg
