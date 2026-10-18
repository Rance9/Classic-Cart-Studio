r"""Classic Cart Studio native HOME packaging (milestones N2, N3): just in time, from the user's effective configuration.

  python -m native_home.package build --collection collection.json --home home.json [--root DIR]     deployment package
  python -m native_home.package dev-full-catalog [--root DIR]                                          development only
  python -m native_home.package inspect [--root DIR]                                                   the current package

Deployment builds exactly what the configured HOME can reach in the native runtime, nothing more. N3: the user can switch
between all four themes on the console, so every theme is reachable and installed; per-game content is still only the
selected games' (the union of what the four themes draw for them):
  games         the collection's games, in the HOME order (folders and favorites are recorded, but the native runtime cannot show
                Browse or Favorites yet, so folder artwork is unreachable and not packaged)
  per game      its effective cover (user replacement > authentic art > NES-style mock cover; fitted whole; one image serves all
                four themes and Details), its own approved hero as the Details backdrop and as the Featured Shelf key art, its
                effective texts (user overrides > canonical) and the short previews the themes show
  shared        each theme's scene, the sprites its renderer draws and its picker thumbnail, the icons at the sizes drawn, the
                universal fallback backdrop and key art (only when some game uses them), fonts baked per role for the
                characters the package shows, the native runtime executable, and the licenses that must travel with them
  theme         the Windows-chosen default theme; with "apply", a one-time request that the console switch to it
An empty, sample or inconsistent configuration is refused (no package); deployment never falls back to the full catalog. The
development full-catalog package and the TV Preview package are separate, explicitly named modes whose manifests say they are
not installable.

Staging: a new directory per build, validated (every file listed with its size and SHA-256, every reference resolved, nothing
extra), then swapped into place atomically; the previous package is removed only after the swap and only if it carries this
packager's ownership marker. Derivations come from a content-addressed cache, so rebuilding after a small change re-derives only
what is new, and nothing in the cache reaches a package unless the configuration asks for it.
"""
from __future__ import annotations
import argparse, json, os, shutil, struct, sys, time, uuid
HERE = os.path.dirname(os.path.abspath(__file__)); TOOLS = os.path.normpath(os.path.join(HERE, "..", ".."))
if os.path.dirname(HERE) not in sys.path: sys.path.insert(0, os.path.dirname(HERE))
from native_home import derive as DV, config as CF   # noqa: E402
from common import FROZEN, APP_ROOT, site_path, data_path, WORK   # noqa: E402

PACKAGE_SCHEMA = "ccs-native-home-package/2"; PACK_SCHEMA = "ccs-native-home-pack/2"; GAMES_SCHEMA = "ccs-native-home-games/2"
OWNED = ".ccs-native-home-owned"
HOME_THEMES = ("arcade-grid", "box-art-showcase", "featured-shelf", "adventure-map")
CATEGORIES = ("executable", "shared", "theme-arcade-grid", "theme-box-art-showcase", "theme-featured-shelf", "theme-adventure-map", "game-covers",
              "game-details-artwork", "game-theme-artwork", "folder-artwork", "font", "catalog", "descriptions", "configuration", "other")
# what the native runtime (tools/console-simulator/core) can display: reachability is decided against this, not against what the
# Windows application or the browser HOME can show. N3: all four themes and switching between them on the console.
RUNTIME_FEATURES = {"all-games": True, "game-details": True, "themes": list(HOME_THEMES), "theme-switching": True, "theme-preference-persistence": True,
                    "browse-folders": True, "categories": True, "favorites": False, "recently-played": False, "search": False, "settings": False, "launch": False}
THEMES = {   # theme -> what its native renderer draws (sprites by name; the scene; the card trim for Arcade Grid)
    "arcade-grid": {"scene": "scene-arcade-grid.png", "ref": "ref-arcade-grid.png", "thumb": "thumb-arcade-grid.jpg", "card_trim": True,
                    "sprites": ["card-frame", "tab-bar", "tab-active", "badge-a", "icon-games", "icon-folder", "icon-star", "icon-clock", "icon-gear", "stat-icon-games"]},
    "box-art-showcase": {"scene": "scene-box-art-showcase.png", "thumb": "thumb-box-art-showcase.jpg",
                         "sprites": ["badge-brand", "seal", "logo-mark", "arrow-l", "arrow-r"]},
    "featured-shelf": {"scene": "scene-featured-shelf.png", "thumb": "thumb-featured-shelf.jpg",
                       "sprites": ["side-active", "icon-home", "icon-star", "icon-folder", "icon-gear", "hero-frame", "shelf-focus", "badge-a", "arrow-l", "arrow-r"]},
    "adventure-map": {"scene": "scene-adventure-map.png", "thumb": "thumb-adventure-map.jpg",
                      "sprites": ["card-frame", "card-pointer", "marker", "panel-frame", "sign", "sign-start", "shelf-focus", "shelf-frame", "badge-dpad", "badge-a", "badge-b", "arrow-l", "arrow-r", "hero"]},
}
THEME_NAMES = {"arcade-grid": "Arcade Grid", "box-art-showcase": "Box Art Showcase", "featured-shelf": "Featured Shelf", "adventure-map": "Adventure Map"}

def overview_preview(text: str | None, mx: int) -> str:
    """home-model.js homeOverviewPreview: the first paragraph, cut at a word boundary with an ellipsis when longer than mx"""
    import re
    first = re.split(r"\n\s*\n", text or "")[0].strip()
    if len(first) <= mx: return first
    cut = first[:mx]; sp = cut.rfind(" ")
    return re.sub(r"[\s,;:.]+$", "", cut[:sp] if sp > mx * 0.6 else cut) + "…"

def theme_root() -> str:
    for p in (os.environ.get("CCS_HOME_ASSETS"), os.path.join(TOOLS, "publish", "assets", "home"), site_path("brand", "home")):
        if p and os.path.isfile(os.path.join(p, "home-art.json")): return p
    raise FileNotFoundError("the HOME theme assets (home-art.json) were not found")
def package_root() -> str:
    return os.environ.get("CCS_NATIVE_HOME_ROOT") or (data_path("native-home") if FROZEN else os.path.join(WORK, "native-home"))
def runtime_binary() -> str | None:
    """the native HOME runtime for the console (32-bit ARM Linux, static): bundled with the application, or the development build"""
    for p in (os.environ.get("CCS_NATIVE_HOME_RUNTIME"), os.path.join(getattr(sys, "_MEIPASS", APP_ROOT), "native", "ccs-home-armv7") if FROZEN else None,
              os.path.join(os.environ.get("CCS_SIM_BUILD_WIN") or os.path.join(WORK, "console-simulator", "build"), "arm-musl-device", "ccs-home-armv7")):
        if p and os.path.isfile(p): return p
    return None

def _elf_info(p: str) -> dict:
    with open(p, "rb") as f: b = f.read(64)
    if b[:4] != b"\x7fELF": return {"format": "not ELF"}
    return {"format": "ELF", "class": {1: "32-bit", 2: "64-bit"}.get(b[4]), "machine": {40: "ARM", 62: "x86-64", 183: "AArch64"}.get(int.from_bytes(b[18:20], "little"), "other")}

def _atlas_chars(p: str) -> set[str]:
    """the characters a baked atlas holds (CCSF2 header: magic, px, ascent, descent, gap, glyph count, kerning count; glyph records)"""
    with open(p, "rb") as f: d = f.read()
    n = struct.unpack_from("<I", d, 6 + 16)[0]; rec = struct.calcsize("<IihhHHI")
    return {chr(struct.unpack_from("<I", d, 6 + 24 + i * rec)[0]) for i in range(n)}

def _touch(p: str):
    with open(p, "w"): pass

def _jload(p: str):
    with open(p, encoding="utf-8") as f: return json.load(f)

class Builder:
    def __init__(self, cfg: dict, root: str, progress=None):
        self.cfg = cfg; self.root = root; self.progress = progress or (lambda stage, done, total, text="": None)
        self.cache = DV.Cache(os.path.join(root, "cache")); self.files: dict[str, dict] = {}; self.blockers: list[dict] = []; self.warnings: list[str] = []
        self.excluded: list[dict] = []; self.stage = None

    def put(self, rel: str, src: str, category: str, kind: str, **meta):
        """place one file into the staging tree (relative path, deduplicated by path); records category and type for accounting"""
        if rel in self.files: return rel
        dst = os.path.join(self.stage, rel.replace("/", os.sep)); os.makedirs(os.path.dirname(dst), exist_ok=True); shutil.copyfile(src, dst)
        self.files[rel] = {"category": category, "type": kind, **meta}; return rel
    def put_bytes(self, rel: str, data: bytes, category: str, kind: str, **meta):
        dst = os.path.join(self.stage, rel.replace("/", os.sep)); os.makedirs(os.path.dirname(dst), exist_ok=True)
        with open(dst, "wb") as f: f.write(data)
        self.files[rel] = {"category": category, "type": kind, **meta}; return rel

    def build(self) -> dict:
        os.makedirs(self.root, exist_ok=True); _touch(os.path.join(self.root, OWNED))
        self.stage = os.path.join(self.root, f"staging-{time.strftime('%Y%m%d-%H%M%S')}-{uuid.uuid4().hex[:6]}"); os.makedirs(self.stage); _touch(os.path.join(self.stage, OWNED))
        try:
            res = self._assemble(); res.update(self._validate())
            if not res["valid"]: return {"ok": False, "status": "invalid", "ready": False, "error": "the package did not validate: " + "; ".join(res["problems"][:5]), **res}
            final = self._swap(); res["package_dir"] = final; return res
        finally:
            if self.stage and os.path.isdir(self.stage): self._remove_owned(self.stage)

    def cached(self, kind: str, src_hash: str, params: str, ext: str, make) -> str: return self.cache.get(kind, src_hash, params, ext, make)

    def game_cover(self, g: dict) -> tuple[str | None, str]:
        """effective cover: the user's replacement > authentic art > the NES-style mock cover (always one image per game)"""
        u = g.get("art_user")
        if u:
            crop = json.dumps(u.get("crop"), sort_keys=True); h = u.get("sha256") or DV.sha256_file(u["path"])
            p = self.cached("user-cover", h, f"{crop}|{DV.COVER_BOX}", ".jpg", lambda out: DV.user_cover(u["path"], u.get("crop"), out)); kind = "user"
        else:
            src = CF._art_path(g.get("art"))
            if g.get("art") and not src: self.warnings.append(f"{g['title']}: artwork listed but not found ({g['art']}); the mock cover is shown")
            if src and not g.get("cover_generated"):
                p = self.cached("cover", DV.sha256_file(src), f"contain{DV.COVER_BOX}", ".jpg", lambda out: DV.cover(src, out)); kind = "authentic"
            else:
                key = DV.sha256_bytes(json.dumps([g["id"], g.get("title"), g.get("publisher"), g.get("year")], ensure_ascii=False).encode())
                p = self.cached("mock", key, f"mock-2|{DV.COVER_BOX}", ".jpg", lambda out: DV.mock(g.get("title") or g["id"], g.get("publisher"), g.get("year"), g["id"], out, theme_root())); kind = "mock"
        d = DV.sha256_file(p); return self.put(f"cover/{d[:16]}.jpg", p, "game-covers", "cover-jpeg", art=kind), kind

    def _assemble(self) -> dict:
        cfg = self.cfg; troot = theme_root(); art = _jload(os.path.join(troot, "home-art.json")); games = cfg["games"]
        steps = len(games) + 8; done = 0; tick = lambda text="": self.progress("assemble", done, steps, text)
        # every theme: scene, the sprites its renderer draws (byte-identical approved files), theme-picker thumbnail
        themes = {}
        for tid, th in THEMES.items():
            T = art["themes"][tid]; cat = f"theme-{tid}"; sprites = {}
            scene = self.put(f"theme/{tid}/scene.png", os.path.join(troot, th["scene"]), cat, "scene-png")
            for name in th["sprites"]:
                sp = T["sprites"][name]; sprites[name] = {"file": self.put(f"theme/{tid}/sprite-{name}.png", os.path.join(troot, sp["file"]), cat, "sprite-png"), "box": sp["box_stage"]}
            for name in T["sprites"]:
                if name not in th["sprites"]: self.excluded.append({"asset": f"{THEME_NAMES[tid]} sprite {name}", "reason": "not drawn by the native renderer"})
            if th.get("card_trim"):
                ref = os.path.join(troot, th["ref"]); p = self.cached("trim", DV.sha256_file(ref), json.dumps(T["geometry"]["card"], sort_keys=True), ".png", lambda out, ref=ref, T=T: DV.card_trim(ref, T["geometry"], out))
                sprites["card-trim"] = {"file": self.put(f"theme/{tid}/sprite-card-trim.png", p, cat, "sprite-png"), "box": T["geometry"]["card"]["frame"]}
            tsrc = os.path.join(troot, th["thumb"]); tp = self.cached("thumb", DV.sha256_file(tsrc), "q88", ".jpg", lambda out, tsrc=tsrc: DV.thumb(tsrc, out))
            themes[tid] = {"name": THEME_NAMES[tid], "scene": scene, "thumb": self.put(f"theme/{tid}/thumb.jpg", tp, "shared", "thumb-jpeg"), "geometry": T["geometry"], "sprites": sprites}
        icons = {}
        for k, sizes in DV.ICON_SIZES.items():
            for sz in sizes:
                p = self.cached("icon", DV.sha256_bytes(k.encode()), str(sz), ".png", lambda out, k=k, sz=sz: DV.meta_icon(k, out, sz)); icons[f"{k}-{sz}"] = self.put(f"theme/icon-{k}-{sz}.png", p, "shared", "icon-png")
        done += 4; tick("themes")
        # games: effective cover (user > authentic > mock), the Details backdrop (own hero or the shared fallback), the Featured
        # Shelf key art (own hero or the shared fallback), effective texts and their previews
        recs = []; fb = {}; chars = set(DV.UI_TEXT); titles = set(); n_kind = {"user": 0, "authentic": 0, "mock": 0}
        def fallback(kind):
            if kind not in fb:
                src = os.path.join(troot, art["hero_fallback"]); h = DV.sha256_file(src)
                if kind == "details": p = self.cached("hero", h, f"fallback{DV.DETAILS_ART}", ".jpg", lambda out: DV.hero(src, "fallback", out)); fb[kind] = self.put(f"hero/{DV.sha256_file(p)[:16]}.jpg", p, "shared", "backdrop-jpeg")
                else: p = self.cached("hero-wide", h, f"{DV.HERO_WIDE}", ".jpg", lambda out: DV.hero_wide(src, out)); fb[kind] = self.put(f"wide/{DV.sha256_file(p)[:16]}.jpg", p, "shared", "keyart-jpeg")
            return fb[kind]
        for g in games:
            rec = {k: g.get(k) for k in ("id", "title", "version_label", "year", "players", "genre", "publisher", "developer")}
            rec["home_kind"] = g.get("home_kind"); rec["source"] = g.get("source")
            rec["cover"], kind = self.game_cover(g); rec["cover_kind"] = kind; n_kind[kind] += 1
            hs = CF._art_path(g.get("hero"))
            if hs:
                h = DV.sha256_file(hs)
                p = self.cached("hero", h, f"hero{DV.DETAILS_ART}", ".jpg", lambda out, hs=hs: DV.hero(hs, "hero", out)); rec["hero"] = self.put(f"hero/{DV.sha256_file(p)[:16]}.jpg", p, "game-details-artwork", "backdrop-jpeg")
                p = self.cached("hero-wide", h, f"{DV.HERO_WIDE}", ".jpg", lambda out, hs=hs: DV.hero_wide(hs, out)); rec["hero_wide"] = self.put(f"wide/{DV.sha256_file(p)[:16]}.jpg", p, "game-theme-artwork", "keyart-jpeg")
                rec["hero_kind"] = "hero"; rec["hero_title"] = {k: v for k, v in (g.get("hero_title") or {}).items() if k in ("x", "y", "w", "align")} or None
            else: rec["hero"] = fallback("details"); rec["hero_wide"] = fallback("wide"); rec["hero_kind"] = "fallback"
            t = g.get("texts")
            if t:
                body = {k: v for k, v in t.items() if v is not None}
                if body.get("official_label"): body["official_label"] = {"edited": "OFFICIAL DESCRIPTION (EDITED)", "custom": "CUSTOM DESCRIPTION"}[body["official_label"]]
                raw = json.dumps(body, ensure_ascii=False).encode("utf-8"); d = DV.sha256_bytes(raw)
                rec["text"] = self.put_bytes(f"text/{d[:16]}.json", raw, "descriptions", "text-json")
                for v in body.values(): chars |= set(v or "")
            else: rec["text"] = None
            desc = (t or {}).get("description"); ov = (t or {}).get("overview")
            # the browser HOME's short texts (home-themes.js): Box Art blurb, Adventure Map panel text, Featured Shelf text
            rec["blurb"] = desc[:180] if desc else overview_preview(ov, 240) or None
            rec["mblurb"] = desc[:220] if desc else overview_preview(ov, 160) or None
            rec["ftext"] = desc or None
            for k in ("title", "version_label", "year", "players", "genre", "publisher", "developer", "blurb", "mblurb", "ftext"): chars |= set(rec.get(k) or "")
            titles |= set(rec.get("title") or "") | set(rec.get("genre") or "") | set(rec.get("publisher") or "") | set(rec.get("year") or "")
            recs.append(rec); done += 1; tick(g.get("title") or "")
        # Browse (N3.1): the collection's folders in the Windows order and nesting, the games in each (indices into games.json), a
        # folder's custom artwork as its tile image, the automatic categories (home-model.js homeCategories) and Adventure Map's
        # two folder shortcuts. Only folders of this collection are packaged; a folder without custom art shows its covers' collage.
        browse = browse_index(cfg, recs)
        listed = {f.get("id") for f in cfg.get("folders") or [] if f.get("art")}
        for f, src in zip(browse["folders"], browse.pop("_art_src")):
            chars |= set(f["name"]); titles |= set(f["name"])
            if f["id"] in listed and not src: self.warnings.append(f"folder {f['name']}: custom artwork is listed but its file is not in the data folder; the collage of its covers is shown")
            if src:
                p = self.cached("folder-art", DV.sha256_file(src), f"tile{DV.FOLDER_TILE}", ".jpg", lambda out, src=src: DV.folder_tile(src, out))
                f["art"] = self.put(f"folder/{DV.sha256_file(p)[:16]}.jpg", p, "folder-artwork", "folder-jpeg")
        for c in browse["categories"]: chars |= set(c["label"])
        # fonts: per role, only the characters that role can draw (plus both cases, for the uppercase labels)
        def both(cs): return {c.upper() for c in cs} | {c.lower() for c in cs} | set(cs)
        ui = set(DV.UI_TEXT) | set("0123456789/.,:;!?'\"()-&+#%*") | {"…", "·"}
        sets = {"all": both(chars | ui), "titles": both(titles | ui), "upper": {c.upper() for c in titles | ui}}
        fonts = {}; missing = set()
        for spec in DV.FONTS:
            name, fam, w, px = spec[:4]; cs = spec[4] if len(spec) > 4 else "all"
            want = sorted(c for c in (sets[cs] if fam != "sym" else set(DV.SYMBOLS)) if c.isprintable() or c == " ")
            if fam == "sym": want = [c for c in want if c in DV.SYMBOLS]
            src_id = DV.sha256_bytes(("".join(want) + fam + str(w) + str(px)).encode())
            p = self.cached("font", src_id, f"{name}|{DV.COVERAGE_EXP}", ".ccsf", lambda out, name=name, fam=fam, w=w, px=px, want=want: DV.bake_font(name, fam, w, px, want, troot, out))
            fonts[name] = {"file": self.put(f"font/{name}.ccsf", p, "font", "font-atlas"), "px": px, "family": DV.FAMILY_LABEL[fam]}
            missing |= {c for c in want if c.strip()} - _atlas_chars(p)
        if missing: self.warnings.append(f"{len(missing)} character(s) have no glyph in some face and show as '?': {''.join(sorted(missing))[:40]}")
        for lic, src in DV.font_licenses(troot).items(): self.put(f"licenses/{lic}", src, "font", "license")
        self.put_bytes("licenses/THIRD-PARTY.txt", ("Native HOME runtime third-party notices\n\nstb_image v2.30 (https://github.com/nothings/stb), Sean Barrett: public domain (Unlicense) or MIT, at the user's choice.\n"
                                                    "Fonts baked into font/*.ccsf, all under the SIL Open Font License 1.1 (license texts in this folder): Barlow (OFL-Barlow.txt), "
                                                    "Silkscreen (OFL-Silkscreen.txt), Cinzel (OFL-Cinzel.txt), VT323 (OFL-VT323.txt), Space Mono (OFL-SpaceMono.txt), Press Start 2P (OFL-PressStart2P.txt).\n").encode(), "other", "license")
        done += 2; tick("fonts")
        # runtime executable
        rb = runtime_binary(); runtime = None
        if rb:
            info = _elf_info(rb); runtime = {"file": self.put("bin/ccs-home-armv7", rb, "executable", "elf-executable", **info), **info, "protocol": "ccs-sim-core/1"}
            if not (info.get("machine") == "ARM" and info.get("class") == "32-bit"): self.blockers.append({"code": "runtime-arch", "text": f"the native runtime is {info.get('class')} {info.get('machine')}, not 32-bit ARM"})
        elif cfg["mode"] == "deployment": self.blockers.append({"code": "runtime-missing", "text": "the native HOME runtime for the console (32-bit ARM) is not available in this installation"})
        done += 1; tick("runtime")
        # the runtime index (pack.json, games.json) and the configuration snapshot
        pack = {"schema": PACK_SCHEMA, "stage": [1280, 720], "theme_default": cfg["theme"], "theme_apply": cfg.get("theme_apply_epoch"), "themes": themes, "icons": icons,
                "fonts": fonts, "games": "games.json", "count": len(recs), "counts": {"games": len(recs), "folders": len(cfg.get("folders") or []), "favorites": len(cfg.get("favorites") or [])}, "map_shortcuts": cfg.get("map_shortcuts") or [None, None], "map_folders": browse["map_folders"], "browse": {"folders": browse["folders"], "categories": browse["categories"]}}
        gdoc = {"schema": GAMES_SCHEMA, "collection": cfg["collection"].get("name"), "count": len(recs), "games": recs}
        self.put_bytes("games.json", json.dumps(gdoc, ensure_ascii=False, indent=0).encode("utf-8"), "catalog", "games-json")
        self.put_bytes("pack.json", json.dumps(pack, ensure_ascii=False, indent=1).encode("utf-8"), "catalog", "pack-json")
        snap = {k: cfg.get(k) for k in ("schema", "mode", "theme", "theme_apply", "theme_apply_epoch", "themes_enabled", "collection", "folders", "favorites", "hidden_factory_codes", "settings", "fingerprint")}
        snap["games"] = [g["id"] for g in recs]
        self.put_bytes("config.json", json.dumps(snap, ensure_ascii=False, indent=1).encode("utf-8"), "configuration", "config-json")
        done += 1; tick("manifest")
        return {"games": len(recs), "runtime": runtime, "covers": n_kind}

    def _manifest(self) -> dict:
        files = []; totals = {}
        for rel in sorted(self.files):
            p = os.path.join(self.stage, rel.replace("/", os.sep)); size = os.path.getsize(p); m = self.files[rel]
            files.append({"path": rel, "size": size, "sha256": DV.sha256_file(p), **m}); totals[m["category"]] = totals.get(m["category"], 0) + size
        cfg = self.cfg; ready = cfg["mode"] == "deployment" and not self.blockers
        why = {"development": "development full-catalog package: never an installation", "preview": "TV Preview package: rendered on this computer only, never an installation"}
        return {"schema": PACKAGE_SCHEMA, "mode": cfg["mode"], "installable": ready,
                "not_installable_reason": None if ready else (why.get(cfg["mode"]) or "; ".join(b["text"] for b in self.blockers)),
                "configuration": {"fingerprint": cfg["fingerprint"], "content_fingerprint": cfg.get("content_fingerprint"), "collection": cfg["collection"], "theme_default": cfg["theme"], "theme_apply": cfg.get("theme_apply_epoch"), "themes_installed": list(HOME_THEMES)},
                "games": [g["id"] for g in cfg["games"]], "folders": [{"id": f["id"], "name": f["name"], "games": len(f["order"])} for f in cfg.get("folders") or []], "favorites": cfg.get("favorites") or [],
                "runtime": {"features": RUNTIME_FEATURES, "renderer": "native HOME (tools/console-simulator/core), framebuffer 1280x720", "target": "32-bit ARM Linux (static, Cortex-A7)", "device_adapter": "ccs-home-armv7 --device: Linux framebuffer and evdev controller (console test mode)"},
                "files": files, "sizes": {"total": sum(f["size"] for f in files), "by_category": {c: totals.get(c, 0) for c in CATEGORIES}, "rom_data": "not included (ROMs are prepared and measured by the console plan, never by this package)"}, "excluded": self.excluded, "warnings": self.warnings, "blockers": self.blockers}

    def _validate(self) -> dict:
        problems = []; man = self._manifest()
        listed = {f["path"] for f in man["files"]}
        on_disk = set()
        for r, _, fs in os.walk(self.stage):
            for f in fs:
                rel = os.path.relpath(os.path.join(r, f), self.stage).replace(os.sep, "/")
                if rel != OWNED: on_disk.add(rel)
        if on_disk != listed: problems.append(f"files and manifest differ: extra {sorted(on_disk - listed)[:3]}, missing {sorted(listed - on_disk)[:3]}")
        pack = _jload(os.path.join(self.stage, "pack.json")); gd = _jload(os.path.join(self.stage, "games.json"))
        refs = [pack["games"]] + list(pack["icons"].values()) + [f["file"] for f in pack["fonts"].values()]
        for t in pack["themes"].values(): refs += [t["scene"], t["thumb"]] + [s["file"] for s in t["sprites"].values()]
        for g in gd["games"]: refs += [x for x in (g.get("cover"), g.get("hero"), g.get("hero_wide"), g.get("text")) if x]
        refs += [f["art"] for f in (pack.get("browse") or {}).get("folders") or [] if f.get("art")]   # folder tile artwork (Browse)
        if set(pack["themes"]) != set(HOME_THEMES): problems.append("not all four themes are in the package")
        for r in refs:
            if r not in listed: problems.append(f"unresolved reference {r}")
        used = set(refs) | {"pack.json", "config.json"} | {f for f in listed if f.startswith(("licenses/", "bin/"))}
        unused = sorted(listed - used)
        if unused: problems.append(f"unreferenced files {unused[:3]}")
        if not gd["games"]: problems.append("no games")
        man_bytes = json.dumps(man, ensure_ascii=False, indent=1).encode("utf-8")
        with open(os.path.join(self.stage, "manifest.json"), "wb") as f: f.write(man_bytes)
        man["sizes"]["manifest"] = len(man_bytes); man["sizes"]["total_with_manifest"] = man["sizes"]["total"] + len(man_bytes)
        status = "ready" if man["installable"] and not problems else (self.cfg["mode"] if self.cfg["mode"] != "deployment" else "not-ready")
        return {"ok": not problems, "valid": not problems, "problems": problems, "status": status, "ready": status == "ready", "manifest": {k: man[k] for k in ("schema", "mode", "installable", "not_installable_reason", "configuration", "sizes", "warnings", "blockers", "excluded")},
                "files": len(man["files"]), "cache": {"hits": self.cache.hits, "misses": self.cache.misses}}

    def _swap(self) -> str:
        """the validated staging tree replaces the current package in one rename; the previous package goes after the swap"""
        name = {"deployment": "current", "development": "development", "preview": "preview"}[self.cfg["mode"]]; final = os.path.join(self.root, name); old = None
        if os.path.isdir(final): old = final + f".old-{uuid.uuid4().hex[:6]}"; os.replace(final, old)
        os.replace(self.stage, final); self.stage = None
        if old: self._remove_owned(old)
        return final

    def _remove_owned(self, d: str):
        """removes a directory only when it is this packager's own output (ownership marker) inside the package root"""
        d = os.path.abspath(d)
        if os.path.dirname(d) != os.path.abspath(self.root) or not os.path.isfile(os.path.join(d, OWNED)): raise RuntimeError(f"refusing to remove {d}: not a native HOME package directory")
        shutil.rmtree(d)

def next_apply_epoch(root: str) -> int:
    """each explicit "apply the Windows default theme" request gets a new number; the console applies a number once and then
    keeps the user's own later choice (the runtime stores the last number it applied with its saved theme)"""
    os.makedirs(root, exist_ok=True); p = os.path.join(root, "apply-epoch.json")
    n = (_jload(p).get("last", 0) if os.path.isfile(p) else 0) + 1
    with open(p + ".tmp", "w", encoding="utf-8") as f: json.dump({"last": n}, f)
    os.replace(p + ".tmp", p); return n

def _folder_art_wide(folder_id: str) -> str | None:
    """the folder's custom artwork, landscape derivative (tools/companion/folder_art.py); None when the folder uses the collage"""
    try:
        import folder_art as FA
        p = os.path.join(FA.FOLDER_ART_DIR, f"{FA._fid(folder_id)}-wide.jpg")
        return p if os.path.isfile(p) else None
    except Exception: return None

def categories(recs: list) -> list:
    """home-model.js homeCategories: genre, year, publisher and player-count groups of the installed games (the HOME order),
    genres first, then years (newest first), publishers and player counts, alphabetical inside a kind"""
    import re as _re
    groups, idx = [], {}
    def add(kind, key, label):
        if (kind, key) not in idx: idx[(kind, key)] = {"id": f"cat:{kind}:{key}", "kind": kind, "label": label, "games": []}; groups.append(idx[(kind, key)])
        return idx[(kind, key)]
    for i, r in enumerate(recs):
        for ge in [x.strip() for x in str(r.get("genre") or "").split(",") if x.strip()]: add("genre", ge, ge)["games"].append(i)
        if r.get("year"): add("year", str(r["year"]), str(r["year"]))["games"].append(i)
        if r.get("publisher"): add("publisher", r["publisher"], r["publisher"])["games"].append(i)
        if r.get("players"):
            n = str(r["players"]); lab = "1 player" if _re.fullmatch(r"1", n) else ("2 players" if "2" in n else n + " players"); add("players", lab, lab)["games"].append(i)
    order = {"genre": 0, "year": 1, "publisher": 2, "players": 3}
    years = sorted([g for g in groups if g["kind"] == "year"], key=lambda g: g["label"], reverse=True)
    rest = sorted([g for g in groups if g["kind"] != "year"], key=lambda g: (order[g["kind"]], g["label"].lower()))
    return [g for g in rest if order[g["kind"]] < 1] + years + [g for g in rest if order[g["kind"]] > 1]

def browse_index(cfg: dict, recs: list) -> dict:
    gi = {r["id"]: i for i, r in enumerate(recs)}; flist = [f for f in cfg.get("folders") or [] if f.get("id")]; ids = {f["id"] for f in flist}
    def kids(pid): return sorted([f for f in flist if (f.get("parent") if f.get("parent") in ids else None) == pid], key=lambda f: f.get("pos") or 0)
    ordered = []
    def walk(pid, depth=0):
        for f in kids(pid):
            if f in ordered or depth > 8: continue
            ordered.append(f); walk(f["id"], depth + 1)
    walk(None)
    fidx = {f["id"]: i for i, f in enumerate(ordered)}
    folders = [{"id": f["id"], "name": f.get("name") or "Folder", "parent": fidx.get(f.get("parent"), -1), "games": [gi[x] for x in f.get("order") or [] if x in gi], "art": None} for f in ordered]
    art = [(_folder_art_wide(f["id"]) if f.get("art") else None) for f in ordered]
    return {"folders": folders, "_art_src": art, "categories": categories(recs), "map_folders": [fidx.get(x, -1) if x else -1 for x in (cfg.get("map_shortcut_ids") or [None, None])[:2]]}

def build(manifest: dict | None, home: dict | None, *, mode: str = "deployment", root: str | None = None, progress=None, apply_epoch: int | None = None) -> dict:
    """the one entry point (backend route and CLI): resolve the configuration, then build, validate and stage"""
    root = root or package_root()
    if mode in ("deployment", "preview"):
        if manifest is None or home is None: return {"ok": False, "status": "invalid-configuration", "ready": False, "error": "deployment needs the collection and the HOME configuration; nothing is packaged without them"}
        try: cfg = CF.resolve(manifest, home)
        except CF.ConfigError as e: return {"ok": False, "status": "invalid-configuration", "ready": False, "error": str(e)}
        cfg["mode"] = mode
        if cfg.get("theme_apply") and mode == "deployment": cfg["theme_apply_epoch"] = apply_epoch or next_apply_epoch(root)
    elif mode == "development": cfg = CF.development_full_catalog()
    else: return {"ok": False, "status": "invalid-configuration", "ready": False, "error": f"unknown mode {mode!r}"}
    t0 = time.time(); res = Builder(cfg, root, progress).build(); res["seconds"] = round(time.time() - t0, 2); res["mode"] = mode; return res

def inspect(root: str | None = None, name: str = "current") -> dict | None:
    p = os.path.join(root or package_root(), name, "manifest.json")
    return _jload(p) if os.path.isfile(p) else None

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter); ap.add_argument("cmd", choices=["build", "dev-full-catalog", "inspect"])
    ap.add_argument("--collection"); ap.add_argument("--home"); ap.add_argument("--root"); a = ap.parse_args()
    if a.cmd == "inspect": m = inspect(a.root); print(json.dumps(m and {k: m[k] for k in ("mode", "installable", "configuration", "sizes")}, indent=1)); return
    if a.cmd == "dev-full-catalog": res = build(None, None, mode="development", root=a.root)
    else:
        if not (a.collection and a.home): sys.exit("build needs --collection and --home (deployment never packages without a configuration)")
        res = build(_jload(a.collection), _jload(a.home), root=a.root)
    print(json.dumps({k: res.get(k) for k in ("ok", "status", "ready", "error", "package_dir", "games", "files", "seconds", "cache")}, indent=1))
    if res.get("manifest"): print(json.dumps(res["manifest"]["sizes"], indent=1))
    sys.exit(0 if res.get("ok") else 1)

if __name__ == "__main__": main()
