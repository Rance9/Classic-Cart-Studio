r"""Artwork consistency: every view of a game shows the same effective cover (PC-only, offline).

The rule (one contract for the Game Library, the Preview on TV sample, the TV Preview package and the console package):
  the user's replacement > authentic art (curated catalog: the row's art; Extended Library: the version's own scan, else its
  title's same-region box, art_family) > the generated NES-style cover. A factory game is shown through the catalog row that
  stands for it (its exact factory version, else the catalog's revision of the same game, else an identity-only match).

  data       every curated row and every library version: art paths are well-formed, exist, belong to the same title; hidden scans
             are not shown; no row carries both authentic art and a generated placeholder; a curated row never ignores the
             authentic art its library version has; the packager's rule (native_home.config.cover_source) selects the same
             cover as the Studio page's rule for every record
  factory    all 30 factory games: one catalog row per code, authentic art, the Preview on TV sample (the page's own code, run in
             node) shows that art; Excitebike (CLV-P-NAAHE) is checked by name
  package    a real TV Preview package of the 30 factory games plus representative records (family boxes, hidden scans, generated
             placeholders, Extended own art / family / none): each cover's kind and source, and the packaged image is derived from
             that source
  override   a user's own cover wins over authentic art; after a reset the authentic cover returns
  alias      a cover stored once under another name (the installed site's art/aliases.json) still resolves as authentic
  render     the package in the native core: Excitebike's own cover is drawn in all four themes and in Game Details (compared
             against the same package with a generated cover in its place)

  python test_artwork_consistency.py [--report out.json] [--frames DIR] [--no-render]      Exit 0 = PASS.
  The site comes from NES_STUDIO_SITE (the release build's site) or the development checkout; the native core from
  CCS_NATIVE_PREVIEW_CORE or the development build."""
from __future__ import annotations
import argparse, hashlib, json, os, re, shutil, subprocess, sys, tempfile, threading, queue, time
HERE = os.path.dirname(os.path.abspath(__file__)); TOOLS = os.path.dirname(HERE)
for p in (HERE, os.path.join(TOOLS, "rom-pipeline")):
    if p not in sys.path: sys.path.insert(0, p)
import library_lookup as LL   # noqa: E402
from common import site_path   # noqa: E402
from native_home import config as CF, derive as DV, package as P   # noqa: E402

EXCITEBIKE = "CLV-P-NAAHE"
ART_RE = re.compile(r"^art/([0-9a-f]{16})\.jpg$")
THEMES = ("arcade-grid", "box-art-showcase", "featured-shelf", "adventure-map")
CHECKS: list = []
def ok(k, v, n=""):
    CHECKS.append((k, bool(v), n)); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:400]})" if n and not v else ""), flush=True)

def _jl(p):
    with open(p, encoding="utf-8") as f: return json.load(f)

def page_rule(gid: str, cat: dict, lib: dict) -> str | None:
    """the Studio page's authentic cover for a version id (studio.js: curated rows show their art; extRow: art || art_family)"""
    if gid in cat: return cat[gid].get("art") or None
    v = lib["versions"].get(gid) or {}
    return v.get("art") or v.get("art_family") or None

# ---------------------------------------------------------------- data contract
def data_checks(rep: dict):
    cat = LL._catalog(); lib = LL._library(); V = lib["versions"]; D = CF._descriptions()
    bad_path, missing, cross, hidden_shown, both, ignored, disagree = [], [], [], [], [], [], []
    counts = {"curated_rows": len(cat), "library_versions": len(V), "authentic": 0, "placeholder": 0, "placeholder_hidden_scan": 0}
    def check_ref(owner_id, title_id, rel, field):
        m = ART_RE.match(rel or "")
        if not m: bad_path.append((owner_id, field, rel)); return
        if not os.path.isfile(site_path(*rel.split("/"))) and not CF._art_path(rel): missing.append((owner_id, field, rel)); return
        src = V.get(m.group(1))
        if src is None or (title_id and src["title_id"] != title_id): cross.append((owner_id, field, rel, src and src.get("name")))
    for gid, g in cat.items():
        tid = LL.title_id_for(gid); v = V.get(gid) or {}
        if g.get("art"): check_ref(gid, tid, g["art"], "catalog.art")
        if g.get("art") and g.get("art_generated"): both.append(gid)
        if g.get("art_generated") and not os.path.isfile(site_path(*g["art_generated"].split("/"))) and not CF._art_path(g["art_generated"]): missing.append((gid, "art_generated", g["art_generated"]))
        if not g.get("art") and v.get("art_class") != "hidden" and (v.get("art") or v.get("art_family")): ignored.append((gid, g.get("nointro_name"), v.get("art") or v.get("art_family")))
    for vid, v in V.items():
        for f in ("art", "art_family"):
            if v.get(f): check_ref(vid, v["title_id"], v[f], f)
        if v.get("art_class") == "hidden" and (v.get("art") or v.get("art_family")): hidden_shown.append(vid)
    for gid in list(cat) + [x for x in V if x not in cat]:
        rec = CF.resolve_game(gid, D, {})
        if rec is None: disagree.append((gid, "unresolved")); continue
        rec["id"] = gid; cs = CF.cover_source(rec); want = page_rule(gid, cat, lib)
        if (cs["kind"] == "authentic") != bool(want) or (want and cs["rel"] != want) or cs["problem"]: disagree.append((gid, want, cs["kind"], cs["rel"], cs["problem"]))
        if cs["kind"] == "authentic": counts["authentic"] += 1
        else:
            counts["placeholder"] += 1
            if (V.get(gid) or {}).get("art_class") == "hidden": counts["placeholder_hidden_scan"] += 1
    rep.update(counts=counts, findings={"malformed_paths": bad_path, "missing_files": missing, "cross_title": cross, "hidden_scan_shown": hidden_shown,
               "art_and_generated": both, "authentic_ignored": ignored, "page_vs_package": disagree[:200], "page_vs_package_count": len(disagree)})
    ok("art_paths_well_formed", not bad_path, bad_path[:5]); ok("art_files_exist", not missing, missing[:5])
    ok("art_belongs_to_same_title", not cross, cross[:5]); ok("hidden_scans_not_shown", not hidden_shown, hidden_shown[:5])
    ok("no_generated_flag_beside_authentic_art", not both, both[:5]); ok("curated_rows_use_available_authentic_art", not ignored, ignored[:5])
    ok("page_and_package_select_the_same_cover_for_every_record", not disagree, disagree[:5])
    ok("records_examined", counts["library_versions"] >= 4000 and counts["curated_rows"] >= 700, counts)

# ---------------------------------------------------------------- factory games
def factory_checks(rep: dict) -> list[str]:
    codes = [b["code"] for b in _jl(site_path("builtin.json"))["games"]]; rows = {c: LL.factory_row(c) for c in codes}
    ok("factory_30_codes", len(codes) == 30, len(codes))
    ok("factory_every_code_has_a_catalog_row", all(rows.values()), [c for c, r in rows.items() if not r])
    noart = [(c, r and r.get("display_title")) for c, r in rows.items() if not (r and r.get("art"))]
    ok("factory_every_game_has_authentic_art", not noart, noart)
    ex = rows.get(EXCITEBIKE) or {}
    ok("excitebike_row_and_art", ex.get("display_title") == "Excitebike" and ex.get("art") == f"art/{ex.get('game_id')}.jpg" and os.path.isfile(site_path(*ex["art"].split("/"))), {k: ex.get(k) for k in ("game_id", "builtin_status", "art")})
    rep["factory"] = [{"code": c, "title": r.get("builtin_title") or r.get("display_title"), "game_id": r["game_id"], "status": r.get("builtin_status"), "art": r.get("art")} for c, r in rows.items() if r]
    # the Preview on TV sample, computed by the page's own functions (studio.js factoryRowFor, tv.js tvSampleEntries) in node
    node = shutil.which("node"); studio = os.path.join(TOOLS, "rom-pipeline", "studio")
    if node and os.path.isdir(studio):
        sjs = open(os.path.join(studio, "studio.js"), encoding="utf-8").read(); tjs = open(os.path.join(studio, "tv.js"), encoding="utf-8").read()
        grab = lambda src, pat: (re.search(pat, src, re.M) or [""])[0]   # a missing piece makes the sample check fail, not crash
        code = "\n".join([grab(sjs, r"^const FACTORY_PRECEDENCE=.*$"), grab(sjs, r"^function factoryRowFor\(.*$"), grab(tjs, r"^function tvSortTitle\(.*$"),
                          grab(tjs, r"^function tvFactoryTitle\(.*$"), grab(tjs, r"^function tvSampleEntries\(.*$")])
        cat = _jl(site_path("catalog.json")); titles = {g["builtin_code"]: g.get("builtin_title") for g in cat["games"] if g.get("builtin_code")}
        prog = f"const DATA={{games:{json.dumps(cat['games'])}}}; const FACTORY_CODES={json.dumps(codes)}; const FACTORY_TITLES={json.dumps(titles)};\n{code}\nprocess.stdout.write(JSON.stringify(tvSampleEntries().map(e=>({{code:e.builtin_code,id:e.id,art:e.art}}))));"
        fd, js = tempfile.mkstemp(suffix=".js"); os.close(fd); open(js, "w", encoding="utf-8").write(prog)
        try: r = subprocess.run([node, js], capture_output=True, text=True, encoding="utf-8", timeout=120)
        finally: os.remove(js)
        try: sample = json.loads(r.stdout or "[]")
        except ValueError: sample = []
        wrong = [e for e in sample if not e["art"] or e["id"].startswith("factory:") or e["art"] != (rows.get(e["code"]) or {}).get("art")]
        ok("preview_sample_shows_the_catalog_cover_for_all_30", len(sample) == 30 and not wrong, wrong[:5] or r.stderr[-300:])
        exs = next((e for e in sample if e["code"] == EXCITEBIKE), {})
        ok("excitebike_preview_sample_cover", exs.get("art") == ex.get("art") and exs.get("id") == ex.get("game_id"), exs)
        rep["preview_sample"] = sample
    elif os.path.isdir(studio): ok("preview_sample_shows_the_catalog_cover_for_all_30 (node not available)", False, "node is required for this check")
    # the page's sources may not look a factory game up by its exact version alone (the defect that hid three covers)
    if os.path.isdir(studio):
        offenders = []
        for n in os.listdir(studio):
            if n.endswith(".js"):
                for i, line in enumerate(open(os.path.join(studio, n), encoding="utf-8"), 1):
                    if "builtin_code===" in line and "'BUILT-IN: EXACT MATCH'" in line and "FACTORY_PRECEDENCE" not in line: offenders.append(f"{n}:{i}")
        ok("no_exact_match_only_factory_lookups", not offenders, offenders)
    return [rows[c]["game_id"] for c in codes]

# ---------------------------------------------------------------- packaging
def collection(ids: list[str], theme="arcade-grid"):
    sels, hg = [], []; cat = LL._catalog(); V = LL._library()["versions"]
    for gid in ids:
        v = V.get(gid) or {}; row = cat.get(gid) or {"display_title": v.get("title"), "nointro_name": v.get("name"), "builtin_code": v.get("builtin_code")}; code = row.get("builtin_code") or ""
        sels.append({"game_id": gid, "display_title": row.get("display_title") or gid, "nointro_name": row.get("nointro_name") or "", "builtin_code": code,
                     "install_required": not bool(code), "source": "personal" if gid.startswith("p_") else "catalog"})
        hg.append({"id": gid, "title": row.get("builtin_title") or row.get("display_title") or gid, "version_label": row.get("nointro_name") or "", "kind": "factory" if code else "added"})
    man = {"schema": "nes-classic-collection/4", "name": "Artwork check", "selections": sels, "hidden_factory_games": []}
    home = {"schema": "ccs-home-collection/1", "theme": theme, "order": list(ids), "games": hg, "folders": [], "favorites": {"items": {}}, "settings": {"reduced_motion": False, "hints": True}, "sample": False}
    return man, home

def representatives(cat, lib) -> dict:
    V = lib["versions"]; out = {}
    out["family_rows"] = [g for g, r in cat.items() if r.get("art_source") == "libretro-equivalent" and r.get("art_family_from")][:4]
    out["hidden_rows"] = [g for g, r in cat.items() if (V.get(g) or {}).get("art_class") == "hidden"][:5]
    out["generated_rows"] = [g for g, r in cat.items() if r.get("art_generated") and not r.get("art")][:5]
    ext = [v for v in V.values() if v["id"] not in cat]
    out["extended_own"] = [v["id"] for v in ext if v.get("art")][:3]
    out["extended_family"] = [v["id"] for v in ext if not v.get("art") and v.get("art_family")][:3]
    out["extended_hidden"] = [v["id"] for v in ext if v.get("art_class") == "hidden"][:3]
    out["extended_none"] = [v["id"] for v in ext if v.get("art_class") == "mock"][:3]
    return out

def package_checks(rep: dict, factory_ids: list[str], root: str) -> dict:
    cat = LL._catalog(); lib = LL._library(); reps = representatives(cat, lib)
    ids = list(dict.fromkeys(factory_ids + sum(reps.values(), [])))
    man, home = collection(ids, "arcade-grid"); r = P.build(man, home, mode="preview", root=root)
    ok("preview_package_built", r.get("ok"), {k: r.get(k) for k in ("error", "problems")})
    if not r.get("ok"): return {}
    pkg = r["package_dir"]; games = {g["id"]: g for g in _jl(os.path.join(pkg, "games.json"))["games"]}
    wrong = []
    for gid in ids:
        g = games.get(gid) or {}; want = page_rule(gid, cat, lib)
        if want:
            exp_tmp = os.path.join(root, "expect.jpg"); DV.cover(CF._art_path(want), exp_tmp)
            same = g.get("cover") and DV.sha256_file(os.path.join(pkg, g["cover"])) == DV.sha256_file(exp_tmp)
            if g.get("cover_kind") != "authentic" or g.get("cover_source") != want or not same: wrong.append((gid, g.get("cover_kind"), g.get("cover_source"), want, bool(same)))
        elif g.get("cover_kind") != "mock": wrong.append((gid, g.get("cover_kind"), "expected the generated cover"))
    fac_bad = [x for x in wrong if x[0] in factory_ids]
    ok("package_factory_30_authentic_from_catalog_art", len([i for i in factory_ids if i in games]) == 30 and not fac_bad, fac_bad[:5])
    ok("package_representatives_follow_the_rule", not [x for x in wrong if x[0] not in factory_ids], [x for x in wrong if x[0] not in factory_ids][:5])
    ex = next((g for g in games.values() if g.get("builtin_code") == EXCITEBIKE), {})
    ok("excitebike_package_cover_authentic", ex.get("cover_kind") == "authentic" and ex.get("cover_source") == LL.factory_row(EXCITEBIKE)["art"], {k: ex.get(k) for k in ("id", "cover_kind", "cover_source")})
    rep["package"] = {"games": len(games), "kinds": {k: sum(1 for g in games.values() if g.get("cover_kind") == k) for k in ("user", "authentic", "mock")}, "representatives": reps}
    return {"pkg": pkg, "games": games, "ex": ex}

def override_checks(rep: dict, root: str):
    import overrides as OV
    ex_id = LL.factory_row(EXCITEBIKE)["game_id"]; old = OV.ROOT; OV.ROOT = os.path.join(root, "overrides")
    try:
        from PIL import Image
        img = os.path.join(root, "user.png"); Image.new("RGB", (600, 840), (12, 140, 60)).save(img)
        OV.import_art(ex_id, img, "user.png")
        man, home = collection([ex_id]); r = P.build(man, home, mode="preview", root=os.path.join(root, "ov"))
        g = _jl(os.path.join(r["package_dir"], "games.json"))["games"][0] if r.get("ok") else {}
        ok("user_cover_wins_over_authentic_art", g.get("cover_kind") == "user", g.get("cover_kind"))
        OV.reset(ex_id, "art"); r = P.build(man, home, mode="preview", root=os.path.join(root, "ov"))
        g = _jl(os.path.join(r["package_dir"], "games.json"))["games"][0] if r.get("ok") else {}
        ok("authentic_cover_returns_after_reset", g.get("cover_kind") == "authentic" and g.get("cover_source") == LL.factory_row(EXCITEBIKE)["art"], g.get("cover_kind"))
    finally: OV.ROOT = old

ALIAS_CHILD = r'''
import json, os, sys
sys.path[:0] = [os.environ["T_HERE"], os.path.join(os.environ["T_HERE"], "..", "rom-pipeline")]
from native_home import config as CF
CF.REPORTS = os.environ["T_EMPTY"]; CF.data_path = lambda *p: os.path.join(os.environ["T_EMPTY"], *p)
print(json.dumps({"path": CF._art_path(os.environ["T_REL"]), "cover": CF.cover_source({"art": os.environ["T_REL"]})["kind"]}))
'''
def alias_checks(root: str):
    """the installed site keeps one copy of byte-identical covers (art/aliases.json): a cover listed under the removed name resolves"""
    ex = LL.factory_row(EXCITEBIKE); rel = ex["art"]; src = CF._art_path(rel)
    site = os.path.join(root, "alias-site"); os.makedirs(os.path.join(site, "art")); keep = "art/0000000000000000.jpg"
    shutil.copyfile(src, os.path.join(site, *keep.split("/"))); json.dump({"aliases": {rel: keep}}, open(os.path.join(site, "art", "aliases.json"), "w"))
    empty = os.path.join(root, "empty"); os.makedirs(empty)
    env = dict(os.environ, NES_STUDIO_SITE=site, NES_STUDIO_DATA=empty, T_HERE=HERE, T_EMPTY=empty, T_REL=rel)
    r = subprocess.run([sys.executable, "-W", "ignore", "-c", ALIAS_CHILD], env=env, capture_output=True, text=True, timeout=120)
    try: res = json.loads(r.stdout.strip().splitlines()[-1])
    except Exception: res = {"error": (r.stdout + r.stderr)[-400:]}
    ok("aliased_cover_resolves_as_authentic", res.get("cover") == "authentic" and os.path.normcase(res.get("path") or "") == os.path.normcase(os.path.join(site, "art", "0000000000000000.jpg")), res)

# ---------------------------------------------------------------- the native renderer
class Core:
    def __init__(self, exe):
        self.p = subprocess.Popen([exe], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, bufsize=0); self.q = queue.Queue()
        threading.Thread(target=self._read, daemon=True).start()
    def _read(self):
        for raw in self.p.stdout:
            line = raw.decode("utf-8", "replace").strip()
            if line.startswith("{"):
                try: self.q.put(json.loads(line))
                except ValueError: pass
    def send(self, line): self.p.stdin.write((line + "\n").encode()); self.p.stdin.flush()
    def wait(self, evs, t=60):
        end = time.time() + t; evs = (evs,) if isinstance(evs, str) else evs
        while time.time() < end:
            try: e = self.q.get(timeout=max(0.05, end - time.time()))
            except queue.Empty: break
            if e.get("ev") in evs: return e
        raise TimeoutError(evs)
    def close(self):
        try: self.send("shutdown"); self.p.wait(10)
        except Exception: self.p.kill()

def _frames(exe, pkg, ex_id, out_dir, tag):
    """one frame per theme with Excitebike focused, and its Game Details; returns {name: png path}"""
    from PIL import Image
    c = Core(exe); shots = {}
    try:
        c.send(f"home {pkg}"); c.wait("home_loaded", 300); c.wait("home", 30)
        ids = [g["id"] for g in _jl(os.path.join(pkg, "games.json"))["games"]]
        def shot(name):
            for _ in range(40):   # covers are decoded on demand: draw until none is pending
                c.send("tick 16"); c.wait("tick"); ppm = os.path.join(out_dir, f"{tag}-{name}.ppm"); c.send(f"frame {ppm}"); f = c.wait(("frame_written", "frame_failed"))
                if f.get("covers_pending", 0) == 0: break
            png = ppm[:-4] + ".png"; Image.open(ppm).save(png); os.remove(ppm); shots[name] = png
        for t in THEMES:
            c.send(f"home-theme {t}"); c.wait("home"); c.send(f"home-select {ids.index(ex_id)}"); c.wait("home"); shot(t)
            c.send("input press select"); c.send("input release select"); c.wait("details_opened", 30); shot(t + "-details")
            c.send("input press b"); c.send("input release b"); c.wait("home")
    finally: c.close()
    return shots

def _regions(mask, step=4, min_px=24):
    """bounding boxes of the separate areas where two frames differ (a theme may draw the focused cover in several places)"""
    w, h = mask.size; px = mask.load(); gw, gh = w // step, h // step
    on = [[any(px[x * step + i, y * step + j] for i in range(step) for j in range(step)) for x in range(gw)] for y in range(gh)]
    seen = [[False] * gw for _ in range(gh)]; boxes = []
    for y in range(gh):
        for x in range(gw):
            if not on[y][x] or seen[y][x]: continue
            st = [(x, y)]; seen[y][x] = True; x0 = x1 = x; y0 = y1 = y
            while st:
                cx, cy = st.pop(); x0, x1, y0, y1 = min(x0, cx), max(x1, cx), min(y0, cy), max(y1, cy)
                for nx, ny in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
                    if 0 <= nx < gw and 0 <= ny < gh and on[ny][nx] and not seen[ny][nx]: seen[ny][nx] = True; st.append((nx, ny))
            b = (x0 * step, y0 * step, (x1 + 1) * step, (y1 + 1) * step)
            if b[2] - b[0] >= min_px and b[3] - b[1] >= min_px: boxes.append(b)
    return boxes

def _cover_match(png_a, png_b, cover_jpg, mock_jpg):
    """the largest area where the frames differ: how far frame a (authentic package) and frame b (control) are from each cover.
    -> {"a_real", "a_mock", "b_real", "b_mock", "box"} (mean absolute difference per pixel), or None when nothing differs"""
    from PIL import Image, ImageChops
    a = Image.open(png_a).convert("RGB"); b = Image.open(png_b).convert("RGB")
    boxes = _regions(ImageChops.difference(a, b).convert("L").point(lambda x: 255 if x > 40 else 0))
    if not boxes: return None
    box = max(boxes, key=lambda r: (r[2] - r[0]) * (r[3] - r[1])); out = {"box": box}
    for tag, cj in (("real", cover_jpg), ("mock", mock_jpg)):
        cv = Image.open(cj).convert("RGB"); cw, ch = cv.size; bw, bh = box[2] - box[0], box[3] - box[1]; s = min(bw / cw, bh / ch)
        fit = cv.resize((max(1, int(cw * s)), max(1, int(ch * s)))); ox = box[0] + (bw - fit.size[0]) // 2; oy = box[1] + (bh - fit.size[1]) // 2
        reg = (ox, oy, ox + fit.size[0], oy + fit.size[1])
        for fr, im in (("a", a), ("b", b)): out[f"{fr}_{tag}"] = round(sum(ImageChops.difference(im.crop(reg), fit).convert("L").getdata()) / (fit.size[0] * fit.size[1]), 2)
    return out

def render_checks(rep: dict, pk: dict, root: str, frames_dir: str | None):
    import native_preview as NP
    exe = NP.core_binary()
    if not exe: ok("render_native_core_available", False, "no native core (CCS_NATIVE_PREVIEW_CORE)"); return
    pkg = pk["pkg"]; ex = pk["ex"]; out = frames_dir or os.path.join(root, "frames"); os.makedirs(out, exist_ok=True)
    # control: the same package with Excitebike's cover replaced by its generated cover
    ctl = os.path.join(root, "control"); shutil.copytree(pkg, ctl)
    mock = os.path.join(ctl, "cover", "control-mock.jpg"); DV.mock("Excitebike", "Nintendo", "1985", ex["id"], mock)
    gj = _jl(os.path.join(ctl, "games.json"))
    for g in gj["games"]:
        if g["id"] == ex["id"]: g["cover"] = "cover/control-mock.jpg"
    json.dump(gj, open(os.path.join(ctl, "games.json"), "w", encoding="utf-8"), ensure_ascii=False)
    real = _frames(exe, pkg, ex["id"], out, "authentic"); fake = _frames(exe, ctl, ex["id"], out, "control")
    cover = os.path.join(pkg, ex["cover"]); res = {}
    for name in list(THEMES) + [t + "-details" for t in THEMES]: res[name] = _cover_match(real[name], fake[name], cover, mock)
    rep["render"] = res
    # the authentic frame is closer to the authentic cover than to the generated one, and the control frame the other way round
    shows = lambda r: r and r["a_real"] < 0.8 * r["a_mock"] and r["b_mock"] < 0.8 * r["b_real"]
    drawn = {n: r for n, r in res.items() if r}
    for t in THEMES:
        views = [n for n in (t, t + "-details") if n in drawn]
        good = views and all(shows(drawn[n]) for n in views)
        ok(f"excitebike_authentic_cover_drawn_{t}", good, {n: res[n] for n in (t, t + "-details")})
    ok("excitebike_details_cover_drawn_every_theme", all(t + "-details" in drawn for t in THEMES), [t for t in THEMES if t + "-details" not in drawn])

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--report", default=""); ap.add_argument("--frames", default=""); ap.add_argument("--no-render", action="store_true"); a = ap.parse_args()
    rep = {"site": site_path()}; root = tempfile.mkdtemp(prefix="ccs-artwork-")
    try:
        data_checks(rep); fids = factory_checks(rep)
        pk = package_checks(rep, fids, os.path.join(root, "pkg"))
        override_checks(rep, root); alias_checks(root)
        if pk and not a.no_render: render_checks(rep, pk, root, a.frames or None)
    finally:
        shutil.rmtree(root, ignore_errors=True)
    rep["checks"] = [{"check": k, "pass": v, "note": str(n)[:300] if not v else ""} for k, v, n in CHECKS]
    if a.report: json.dump(rep, open(a.report, "w", encoding="utf-8"), indent=1, ensure_ascii=False, default=str)
    bad = [k for k, v, _ in CHECKS if not v]; print("RESULT:", "PASS" if not bad else "FAIL " + json.dumps(bad), f"({len(CHECKS)} checks)"); return 0 if not bad else 1

if __name__ == "__main__": sys.exit(main())
