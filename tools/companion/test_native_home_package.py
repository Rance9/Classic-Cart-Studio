r"""Native HOME packaging (N2): configuration-driven, just-in-time packages, checked on real library data.

  A  small selection                       D  changed selection: removed games' assets disappear
  B  typical 30-game HOME with folders     E  theme default: every theme is a valid default; all four themes are always installed; explicit apply numbers
  C  games outside the default catalog     F  development full catalog: explicit, never installable
plus invalid configurations, determinism, and the staged package running in the native core (Windows build and the package's
own ARM executable under qemu-arm, bit-identical frames).

  python test_native_home_package.py            (CCS_N2_EVIDENCE=<dir> also writes the sample manifest, sizes and frames)
"""
from __future__ import annotations
import hashlib, json, os, shutil, sys, tempfile, unittest
HERE = os.path.dirname(os.path.abspath(__file__)); TOOLS = os.path.dirname(HERE)
for p in (HERE, os.path.join(TOOLS, "rom-pipeline"), os.path.join(TOOLS, "console-simulator", "host")):
    if p not in sys.path: sys.path.insert(0, p)
import library_lookup as LL   # noqa: E402
from native_home import package as P, config as CF, derive as DV   # noqa: E402

EVIDENCE = os.environ.get("CCS_N2_EVIDENCE")
CAT = LL._catalog(); LIB = LL._library()
ZELDA = "ec0d4ebf6d2fcecd"

def sel_for(gid: str) -> dict:
    """one Studio selection (studio.js buildManifest) and its HOME game (tv.js homeModelFromStudio -> homeBuild)"""
    if gid in CAT:
        g = CAT[gid]; st = g.get("builtin_status") or "NOT BUILT-IN"; factory = st == "BUILT-IN: EXACT MATCH"
        s = {"game_id": gid, "version_id": gid, "title_id": LL.title_id_for(gid), "display_title": g["display_title"], "nointro_name": g.get("nointro_name"), "source": "catalog",
             "builtin_code": g.get("builtin_code"), "install_required": not factory}
        h = {"id": gid, "title": g["display_title"], "version_label": "factory version" if factory else g.get("nointro_name"), "kind": "factory" if factory else "added"}
    elif gid.startswith("p_"):
        e = LL.personal_index()["entries"][gid]; s = {"game_id": gid, "version_id": gid, "display_title": e["user"]["title"], "source": "personal", "install_required": True}
        h = {"id": gid, "title": e["user"]["title"], "version_label": e["user"].get("version_label") or "", "kind": "personal"}
    else:
        v = LIB["versions"][gid]; t = LIB["titles"][v["title_id"]]
        s = {"game_id": gid, "version_id": gid, "title_id": v["title_id"], "display_title": t.get("title") or v["name"], "nointro_name": v["name"], "source": "extended", "install_required": True}
        h = {"id": gid, "title": t.get("title") or v["name"], "version_label": v["name"], "kind": "added"}
    return s, h

def studio_config(ids: list[str], theme: str = "arcade-grid", folders=None, favorites=(), name="Test collection") -> tuple[dict, dict]:
    pairs = [sel_for(i) for i in ids]
    man = {"schema": "nes-classic-collection/4", "name": name, "selections": [s for s, _ in pairs], "hidden_factory_games": []}
    home = {"schema": "ccs-home-collection/1", "theme": theme, "order": list(ids), "games": [h for _, h in pairs], "folders": folders or [],
            "favorites": {"items": {i: {"on": True} for i in favorites}}, "settings": {"reduced_motion": False, "hints": True}, "sample": False}
    return man, home

def factory_ids() -> list[str]: return sorted(k for k, g in CAT.items() if g.get("builtin_status") == "BUILT-IN: EXACT MATCH")
def added_ids(n: int) -> list[str]:
    return [k for k, g in sorted(CAT.items(), key=lambda kv: kv[1]["display_title"]) if not g.get("builtin_code") and g.get("art_hero")][:n // 2] + \
           [k for k, g in sorted(CAT.items(), key=lambda kv: kv[1]["display_title"]) if not g.get("builtin_code") and not g.get("art_hero")][:n - n // 2]

def pick_extended() -> dict:
    """versions outside the curated catalog: own art, title art only, no art at all (unlicensed), a regional and a revision"""
    out = {}
    for vid, v in sorted(LIB["versions"].items()):
        if vid in CAT: continue
        t = LIB["titles"].get(v["title_id"], {}); nm = v.get("name") or ""
        if "own_art" not in out and v.get("art"): out["own_art"] = vid
        elif "title_art" not in out and not v.get("art") and t.get("art"): out["title_art"] = vid
        elif "unlicensed" not in out and not v.get("art") and not t.get("art") and ("(Unl)" in nm or "Pirate" in nm): out["unlicensed"] = vid
        elif "regional" not in out and not v.get("art") and "(Japan)" in nm and not t.get("art"): out["regional"] = vid
        elif "revision" not in out and "(Rev " in nm and not any(x in nm for x in ("Pirate", "Hack")): out["revision"] = vid
        if len(out) == 5: break
    return out

def tree(d: str) -> dict:
    return {os.path.relpath(os.path.join(r, f), d).replace(os.sep, "/"): DV.sha256_file(os.path.join(r, f)) for r, _, fs in os.walk(d) for f in fs}

def _jsave(obj, p: str):
    with open(p, "w", encoding="utf-8") as f: json.dump(obj, f, indent=1)

def _jload(p: str):
    with open(p, encoding="utf-8") as f: return json.load(f)

class Base(unittest.TestCase):
    @classmethod
    def setUpClass(cls): cls.root = tempfile.mkdtemp(prefix="ccs-n2-")
    @classmethod
    def tearDownClass(cls): shutil.rmtree(cls.root, ignore_errors=True)
    def build(self, man, home, **kw):
        r = P.build(man, home, root=self.root, progress=lambda *a: None, **kw); return r
    def manifest(self, name="current"): return _jload(os.path.join(self.root, name, "manifest.json"))
    def games(self, name="current"): return _jload(os.path.join(self.root, name, "games.json"))["games"]
    def assertPackageIntegrity(self, name="current"):
        d = os.path.join(self.root, name); m = self.manifest(name); t = tree(d)
        listed = {f["path"]: f for f in m["files"]}
        self.assertEqual(set(t) - {"manifest.json", P.OWNED}, set(listed), "files on disk = files in the manifest")
        for p, f in listed.items(): self.assertEqual(t[p], f["sha256"], p); self.assertEqual(os.path.getsize(os.path.join(d, p)), f["size"], p)
        self.assertEqual(sum(f["size"] for f in m["files"]), m["sizes"]["total"]); self.assertEqual(sum(m["sizes"]["by_category"].values()), m["sizes"]["total"])
        self.assertEqual(list(m["sizes"]["by_category"]), list(P.CATEGORIES))
        self.assertFalse([x for x in os.listdir(self.root) if x.startswith("staging-") or ".old-" in x], "no staging or previous package left behind")
        for tid, th in P.THEMES.items():   # N3: all four themes, each with exactly the sprites its renderer draws
            got = sorted(p for p in t if p.replace(os.sep, "/").startswith(f"theme/{tid}/sprite-"))
            want = sorted([f"theme/{tid}/sprite-{s}.png" for s in th["sprites"]] + ([f"theme/{tid}/sprite-card-trim.png"] if th.get("card_trim") else []))
            self.assertEqual([x.replace(os.sep, "/") for x in got], want, f"{tid}: only the sprites its native renderer draws")
            self.assertIn(f"theme/{tid}/scene.png", [x.replace(os.sep, "/") for x in t])
        self.assertEqual(m["configuration"]["themes_installed"], list(P.HOME_THEMES))
        self.assertFalse(any(p.startswith("cache/") for p in t)); return m

class A_Small(Base):
    def test_small_selection(self):
        ids = [ZELDA] + added_ids(2); man, home = studio_config(ids); r = self.build(man, home)
        self.assertTrue(r["ok"], r.get("error")); self.assertEqual(r["status"], "ready"); self.assertTrue(r["ready"])
        m = self.assertPackageIntegrity(); g = self.games()
        self.assertEqual([x["id"] for x in g], ids); self.assertEqual(m["games"], ids); self.assertTrue(m["installable"])
        self.assertEqual(len([f for f in m["files"] if f["type"] == "cover-jpeg"]), 3, "one cover per game serves all four themes")
        for x in g: self.assertTrue(x["cover"] and x["text"]); self.assertIn(x["hero_kind"], ("hero", "fallback"))
        for f in m["files"]:
            if f["type"] == "cover-jpeg":
                from PIL import Image
                im = Image.open(os.path.join(self.root, "current", f["path"])); self.assertEqual(im.format, "JPEG"); self.assertLessEqual(im.size[0], DV.COVER_BOX[0]); self.assertLessEqual(im.size[1], DV.COVER_BOX[1])
                self.assertFalse(im.info.get("progressive"), "baseline JPEG: the runtime decoder's path")
        self.assertEqual(m["runtime"]["target"], "32-bit ARM Linux (static, Cortex-A7)"); b = next(f for f in m["files"] if f["path"] == "bin/ccs-home-armv7"); self.assertEqual((b["class"], b["machine"]), ("32-bit", "ARM"))
        for lic in ("licenses/OFL-Barlow.txt", "licenses/OFL-Silkscreen.txt", "licenses/THIRD-PARTY.txt"): self.assertIn(lic, [f["path"] for f in m["files"]])
    def test_deterministic(self):
        ids = [ZELDA] + added_ids(2); man, home = studio_config(ids)
        self.build(man, home); a = tree(os.path.join(self.root, "current")); r = self.build(man, home); b = tree(os.path.join(self.root, "current"))
        self.assertEqual(a, b, "same configuration, byte-identical package (manifest included)"); self.assertEqual(r["cache"]["misses"], 0, "a rebuild derives nothing new")
    def test_invalid_configurations_are_refused(self):
        before = sorted(os.listdir(self.root)) if os.path.isdir(self.root) else []
        man, home = studio_config([]); r = self.build(man, home); self.assertEqual(r["status"], "invalid-configuration"); self.assertIn("no games", r["error"])
        man, home = studio_config([ZELDA]); home["sample"] = True; self.assertEqual(self.build(man, home)["status"], "invalid-configuration")
        man, home = studio_config([ZELDA, ZELDA]); self.assertIn("twice", self.build(man, home)["error"])
        man, home = studio_config([ZELDA]); man["selections"].append({"game_id": "ffffffffffffffff", "version_id": "ffffffffffffffff"}); home["order"].append("ffffffffffffffff")
        self.assertIn("not in this library", self.build(man, home)["error"])
        man, home = studio_config([ZELDA] + added_ids(1)); home["order"] = home["order"][:1]; self.assertIn("HOME order", self.build(man, home)["error"])
        self.assertEqual(P.build(None, None, root=self.root)["status"], "invalid-configuration", "deployment never falls back to the full catalog")
        self.assertEqual(sorted(os.listdir(self.root)) if os.path.isdir(self.root) else [], before, "nothing staged for a refused configuration")

class B_Typical(Base):
    def test_typical_installation_with_folders(self):
        ids = factory_ids()[:20] + added_ids(10)
        folders = [{"id": "f:rpg", "name": "Adventures", "parent": None, "order": ids[20:24], "art": {"src": "data:image/png;base64,AAAA"}}, {"id": "f:two", "name": "Two players", "parent": None, "order": ids[:6]}]
        man, home = studio_config(ids, folders=folders, favorites=ids[:3], name="Typical HOME"); r = self.build(man, home)
        self.assertTrue(r["ready"], r.get("error")); m = self.assertPackageIntegrity()
        self.assertEqual(len(m["games"]), 30); self.assertEqual([f["name"] for f in m["folders"]], ["Adventures", "Two players"]); self.assertEqual(m["favorites"], sorted(ids[:3]))
        # N3.1: Browse is native, so folders are packaged; this folder's artwork reference has no file in the data folder, so it is
        # reported and the folder shows the collage of its covers (test_browse.py covers real folder artwork)
        self.assertEqual(m["sizes"]["by_category"]["folder-artwork"], 0); self.assertTrue(any("folder Adventures: custom artwork is listed" in w for w in m["warnings"]), m["warnings"])
        self.assertTrue(m["runtime"]["features"]["browse-folders"]); self.assertFalse([e for e in m["excluded"] if "folder artwork" in e["asset"]])
        heroes = {g["hero"] for g in self.games() if g["hero_kind"] == "fallback"}; self.assertLessEqual(len(heroes), 1, "one shared fallback backdrop")
        for g in self.games():
            src = CAT[g["id"]]; self.assertEqual(g["hero_kind"], "hero" if src.get("art_hero") else "fallback")
        if EVIDENCE:
            os.makedirs(EVIDENCE, exist_ok=True); shutil.copyfile(os.path.join(self.root, "current", "manifest.json"), os.path.join(EVIDENCE, "sample-manifest-typical.json"))
            _jsave({"configuration": "B: 20 factory games + 10 added, 2 folders, 3 favorites", "files": len(m["files"]), "sizes": m["sizes"], "excluded": m["excluded"]}, os.path.join(EVIDENCE, "sizes-typical.json"))
        type(self).package = os.path.join(self.root, "current")

    def test_package_runs_in_the_native_core(self):
        """the staged package loads in the Windows core and in the package's own ARM executable under qemu-arm; frames match"""
        if not getattr(type(self), "package", None): self.test_typical_installation_with_folders()
        from runner import Core, ppm_to_png
        pk = type(self).package; crcs = {}; out = EVIDENCE or tempfile.mkdtemp(prefix="ccs-n2-frames-")
        for path, kw in (("windows", {}), ("arm", {"exe": os.path.join(pk, "bin", "ccs-home-armv7")})):
            try: c = Core(path, **kw)
            except FileNotFoundError as e: self.skipTest(f"{path}: {e}")
            try:
                self.assertTrue(c.ready(120)); ev = c.home(pk, 300); self.assertEqual(ev and ev["ev"], "home_loaded", ev); self.assertEqual(ev.get("games"), 30); c.wait_for("home")
                seq = []
                for name, keys in (("grid", []), ("moved", ["right", "down"]), ("details", ["select"])):
                    for k in keys: c.press(k); c.release(k)
                    c.send("tick 16"); c.wait_for("tick"); ppm = os.path.join(out, f"n2-{path}-{name}.ppm"); f = c.frame(ppm); self.assertEqual(f["ev"], "frame_written", f); seq.append(f["crc32"])
                    if path == "arm" or not EVIDENCE: os.remove(ppm)
                    else: ppm_to_png(ppm, ppm[:-4] + ".png"); os.remove(ppm)
                crcs[path] = seq
            finally: c.shutdown()
        self.assertEqual(crcs["windows"], crcs["arm"], "bit-identical frames: Windows core and the packaged ARM executable")

class C_NonDefault(Base):
    def setUp(self):
        self.tmp = tempfile.mkdtemp(prefix="ccs-n2-personal-"); os.makedirs(os.path.join(self.tmp, "personal", "art"))
        from PIL import Image
        Image.new("RGB", (300, 420), (40, 90, 160)).save(os.path.join(self.tmp, "personal", "art", "p_n2test.jpg"))
        self.entry = {"id": "p_n2test", "file_name": "homebrew.nes", "title_id": None, "detected": {}, "user": {"title": "My Homebrew Quest", "version_label": "v1.2", "description": "A personal game.\n\nWith two paragraphs.", "year": "2024", "publisher": "Me", "art": "personal/art/p_n2test.jpg"}}
        self._pi, self._dp = LL.personal_index, CF.data_path
        LL.personal_index = lambda *a, **k: {"entries": {"p_n2test": self.entry}}; CF.data_path = lambda *a: os.path.join(self.tmp, *a)
    def tearDown(self):
        LL.personal_index, CF.data_path = self._pi, self._dp; shutil.rmtree(self.tmp, ignore_errors=True)
    def test_games_outside_the_default_catalog(self):
        ext = pick_extended(); self.assertEqual(set(ext), {"own_art", "title_art", "unlicensed", "regional", "revision"}, ext)
        ids = [ZELDA] + list(ext.values()) + ["p_n2test"]; man, home = studio_config(ids); r = self.build(man, home)
        self.assertTrue(r["ready"], r.get("error")); m = self.assertPackageIntegrity(); g = {x["id"]: x for x in self.games()}
        for k, vid in ext.items():
            v = LIB["versions"][vid]; t = LIB["titles"][v["title_id"]]; x = g[vid]
            self.assertEqual(x["version_label"], v["name"], "the exact selected version, never another revision"); self.assertEqual(x["source"], "extended")
            self.assertEqual(x["hero_kind"], "fallback")
            if x["text"]:   # only the same game's approved overview (another version of a curated title) or its own library overview; never an Official text
                body = _jload(os.path.join(self.root, "current", x["text"])); self.assertNotIn("official", body)
                cur = [c for c in (LIB["titles"][v["title_id"]].get("versions") or []) if c in CAT]
                lib_ov = (CF._library_overviews().get(v["title_id"]) or {}).get("overview")
                self.assertTrue(any(body["overview"] == (CF._descriptions().get(c) or {}).get("overview") for c in cur) or body["overview"] == lib_ov, f"{k}: the overview belongs to this game")
            want = v.get("art") or t.get("art")
            if want:
                exp = DV.Cache(os.path.join(self.root, "cache")).path("cover", DV.sha256_bytes(f"{DV.DERIVE_VERSION}|cover|contain{DV.COVER_BOX}|{DV.sha256_file(CF._art_path(want))}".encode())[:40], ".jpg")
                self.assertEqual(x["cover"], f"cover/{DV.sha256_file(exp)[:16]}.jpg", f"{k}: its own version art, else its title's art")
            else: self.assertEqual(x["cover_kind"], "mock", f"{k}: no authentic art -> the NES-style mock cover with its own title, nothing borrowed")
        p = g["p_n2test"]; self.assertEqual((p["title"], p["version_label"], p["source"]), ("My Homebrew Quest", "v1.2", "personal")); self.assertTrue(p["cover"])
        self.assertEqual(_jload(os.path.join(self.root, "current", p["text"])), {"description": "A personal game.\n\nWith two paragraphs."})
        self.assertTrue(g[ZELDA]["text"])
        # the native core shows them: Details of an Extended game (no description note) and the personal one (single block)
        from runner import Core
        c = Core("windows")
        try:
            c.ready(60); ev = c.home(os.path.join(self.root, "current"), 300); self.assertEqual(ev["ev"], "home_loaded", ev); c.wait_for("home")
            ix = list(g).index
            def details_of(gid):
                c.send(f"home-select {ix(gid)}"); c.press("select"); c.release("select"); e = c.wait_for("details_opened")
                if EVIDENCE:
                    from runner import ppm_to_png
                    c.send("tick 16"); c.wait_for("tick"); ppm = os.path.join(EVIDENCE, f"n2-details-{gid}.ppm"); c.frame(ppm); ppm_to_png(ppm, ppm[:-4] + ".png"); os.remove(ppm)
                c.press("b"); c.release("b"); return e
            self.assertIn(details_of(ext["unlicensed"])["desc"], ("none", "two")); self.assertEqual(details_of("p_n2test")["desc"], "one"); self.assertEqual(details_of(ZELDA)["desc"], "two")
        finally: c.shutdown()

class D_Changed(Base):
    def test_removed_games_assets_disappear(self):
        ids = factory_ids()[:10] + added_ids(10); man, home = studio_config(ids); self.assertTrue(self.build(man, home)["ready"])
        first = {g["id"]: g for g in self.games()}; removed = added_ids(10)[:3]
        keep = [i for i in ids if i not in removed]; added = [k for k, g in sorted(CAT.items()) if k not in ids and not g.get("builtin_code")][:1]
        man, home = studio_config(keep + added); r = self.build(man, home); self.assertTrue(r["ready"], r.get("error")); self.assertPackageIntegrity()
        second = {g["id"]: g for g in self.games()}; files = set(tree(os.path.join(self.root, "current")))
        self.assertEqual(set(second), set(keep + added))
        still = {second[i][k] for i in second for k in ("cover", "hero", "text") if second[i].get(k)}
        gone = []
        for gid in removed:
            for k in ("cover", "text", "hero"):
                f = first[gid].get(k)
                if f and f not in still: self.assertNotIn(f, files, f"{gid} {k} left behind"); gone.append(f)
        self.assertGreaterEqual(len(gone), 6, "the removed games' covers and texts (and own heroes) are gone")
        for gid in added: self.assertIn(second[gid]["cover"], files)
        if EVIDENCE: _jsave({"removed_games": removed, "added_games": added, "files_removed": sorted(gone), "files_after": len(files)}, os.path.join(EVIDENCE, "changed-selection.json"))

class E_ThemeDefault(Base):
    def test_every_theme_is_a_valid_default_and_all_four_are_installed(self):
        """N3: the Windows-chosen theme is only the default; every package carries all four themes (switchable on the console)"""
        trees = {}
        for theme in P.HOME_THEMES:
            man, home = studio_config([ZELDA] + added_ids(2), theme=theme); r = self.build(man, home)
            self.assertTrue(r["ready"], r.get("error")); self.assertPackageIntegrity()
            pk = _jload(os.path.join(self.root, "current", "pack.json")); self.assertEqual(pk["theme_default"], theme); self.assertIsNone(pk["theme_apply"])
            self.assertEqual(sorted(pk["themes"]), sorted(P.HOME_THEMES)); trees[theme] = tree(os.path.join(self.root, "current"))
        a, b = trees["arcade-grid"], trees["adventure-map"]
        self.assertEqual({k for k in a if a[k] != b.get(k)}, {"pack.json", "config.json", "manifest.json"}, "the default theme changes only the configuration, never the content")
        man, home = studio_config([ZELDA], theme="arcade-grid"); home["apply_theme"] = True; r = self.build(man, home)
        n1 = _jload(os.path.join(self.root, "current", "pack.json"))["theme_apply"]; r = self.build(man, home); n2 = _jload(os.path.join(self.root, "current", "pack.json"))["theme_apply"]
        self.assertTrue(isinstance(n1, int) and n2 == n1 + 1, "each explicit apply request gets a new number (the console applies each number once)")
        man, home = studio_config([ZELDA], theme="neon-city"); self.assertEqual(self.build(man, home)["status"], "invalid-configuration")

class F_Development(Base):
    def test_development_full_catalog_is_explicit_and_not_installable(self):
        r = P.build(None, None, mode="development", root=self.root, progress=lambda *a: None)
        self.assertTrue(r["ok"], r.get("error")); self.assertEqual(r["status"], "development"); self.assertFalse(r["ready"])
        self.assertFalse(os.path.isdir(os.path.join(self.root, "current")), "the development package never becomes the current package")
        m = self.assertPackageIntegrity("development"); self.assertEqual(m["mode"], "development"); self.assertFalse(m["installable"]); self.assertIn("never an installation", m["not_installable_reason"])
        self.assertEqual(len(m["games"]), 773)
        import importlib.util
        spec = importlib.util.spec_from_file_location("nhp", os.path.join(TOOLS, "console-simulator", "adapter", "native_home_pack.py")); nhp = importlib.util.module_from_spec(spec); spec.loader.exec_module(nhp)
        exp = sorted((nhp.entry_for(g) for g in CAT.values()), key=lambda e: (e["sort"], e["id"])); g = self.games("development")
        self.assertEqual([x["id"] for x in g], [e["id"] for e in exp]); self.assertEqual([x["title"] for x in g], [e["title"] for e in exp]); self.assertEqual([x["version_label"] for x in g], [e["version_label"] for e in exp])
        if EVIDENCE: _jsave({"configuration": "F: development full catalog (773)", "files": len(m["files"]), "sizes": m["sizes"]}, os.path.join(EVIDENCE, "sizes-development-full.json"))

if __name__ == "__main__": unittest.main(verbosity=2)
