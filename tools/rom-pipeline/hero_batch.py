"""Bulk hero-art production for the consensus Top 100 (development-only tooling; never shipped to the site or the app).

Produces game-specific widescreen key art with Nano Banana Pro (`gemini-3-pro-image`) through Google's Gemini Batch API,
from the authentic references the request packs already carry (cover, title screen, gameplay screen) and the art-direction
template calibrated on the three approved-style proof-of-concept illustrations. Nothing is submitted without `submit`,
and `submit` refuses without `--confirm` (paid). Approval stays manual (`hero_art.py approve|reject`).

Secrets: the API key comes only from the GEMINI_API_KEY environment variable (or the file named by GEMINI_API_KEY_FILE);
it is never written to the manifest, logs, requests or output.

  python hero_batch.py manifest [--limit N] [--ids id,id] [--include-poc]   build/refresh reports/art/hero-batch/manifest.json
  python hero_batch.py prepare [--batch NAME] [--ids ...] [--limit N]        write per-game prompts + reference packages + the JSONL request file
  python hero_batch.py validate [--batch NAME]                              check the request file offline (schema, sizes, references)
  python hero_batch.py estimate [--batch NAME]                              request count, reference bytes, rough cost note (no network)
  python hero_batch.py submit --batch NAME --confirm                        upload the JSONL, create the batch job (PAID; owner authorization required)
  python hero_batch.py status [--batch NAME] [--watch]                      poll the job state
  python hero_batch.py fetch --batch NAME                                   download results, decode images, run technical validation, import as candidates, write contact sheets
  python hero_batch.py retry --batch NAME                                   re-prepare only the failed / rejected games into a new batch
  python hero_batch.py sheet [--batch NAME]                                 regenerate the contact sheets
  python hero_batch.py smoke --id <game_id>                                 prepare a one-image batch (paid API smoke test); submit separately with --confirm

Manifest: reports/art/hero-batch/manifest.json
  games: {game_id: {title, rank, status: pending|prepared|submitted|generated|failed|candidate|approved|rejected, batch, attempts, last_error, sha256, reviewed}}
Batches: reports/art/hero-batch/<batch>/ (requests.jsonl, prompts/<id>.md, refs/<id>/, results/, sheet-*.png, job.json)
Re-runs skip games whose status is approved (or candidate) unless --force is given.
"""
from __future__ import annotations
import os, sys, json, time, base64, hashlib, argparse, io, re
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import REPORTS
from PIL import Image, ImageDraw, ImageFont
import hero_art

MODEL = "gemini-3-pro-image"
PROMPT_VERSION = "v3-finish"   # v1: text-only art direction (smoke1, rejected for flatness); v2: three approved illustrations as labeled style references
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
BATCH_DIR = os.path.join(REPORTS, "art", "hero-batch"); MANIFEST = os.path.join(BATCH_DIR, "manifest.json")
POC = {"8e844d77da01f86a": "10-Yard Fight", "9f54aafa367247b9": "1943 - The Battle of Midway", "ec0d4ebf6d2fcecd": "The Legend of Zelda"}
REF_MAX = 1024          # longest side of a reference image sent to the model (bytes matter in batch files)
OUT_MIN = (1280, 704)   # technical floor for a usable result

# ---- art direction, revised after the first paid test (owner review 2026-10-13): the three approved illustrations are STYLE
# references for density, lighting and finish; each game's cover, title screen and gameplay screen are the IDENTITY references.
STYLE_REF_IDS = ["8e844d77da01f86a", "9f54aafa367247b9", "ec0d4ebf6d2fcecd"]   # 10-Yard Fight, 1943, The Legend of Zelda (owner-approved)
STYLE = """RENDERING FINISH (this is the part the STYLE references define; match it closely):
- A premium, dense, painterly pixel-art illustration, not an enlarged retro gameplay screen. Every surface carries rich tonal
  variation and material texture: grass with blades and highlights, hills with shading bands and ground texture, stone with
  cracks and weathering, clouds with volume and lit edges, mushrooms and pipes with sheen, dents and shadow, buildings with
  bricks, windows and wear. Fine illustration density comparable to the STYLE references.
- Sophisticated light interaction: a clear key light with directional shadows cast by every object, rim light on the main
  subject, bounce light in the shadows, atmospheric depth (haze, light rays, depth-faded distance), glowing highlights on
  anything that emits or reflects light.
- Environmental complexity: a detailed foreground, a fully developed middle ground and a layered distance with landmarks, so
  the scene reads as a world, not a stage.
- Keep the game's own mood and colors: a bright, colorful world stays bright and colorful; do not force realism, grimness,
  darkness or a moody cinematic palette onto a cheerful game. Sophistication comes from texture, light and depth, not from
  darkening.
- The box art defines WHO is in the scene (characters, costumes, enemies, props, their colors) and must NOT dictate the
  rendering style: do not reproduce its flat cartoon shading or thick outlines. Render those characters with the painterly
  pixel-art finish of the STYLE references.
- Composition: 16:9, full bleed, edge to edge; the main subject large and dynamic in the center-left at mid-height; the upper
  right third stays visually quiet and lower in contrast (soft sky or atmosphere) because the title lettering is drawn there:
  keep it calm, do not fill it with detail; the bottom edge stays scenery with no ground strip, checkerboard, platform band or
  decorative base.
- Nothing else in the image: no text, letters, numbers, logos, borders, frames, vignettes, letterbox bars, fake UI, HUD, score,
  box edges, cartridges, consoles or screens of any kind."""

VARIATION = {   # per-genre nudges so scenes vary; the game's own references still decide the setting
    "Sports": "a packed arena or field under event lighting, crowd and banners in the distance, action frozen mid-play",
    "Racing": "speed and motion blur in the scenery, a track or road receding into the distance, dust or spray",
    "Shooter": "an intense moment with tracer light, explosions or energy effects, enemies converging from the distance",
    "Platformer": "a vivid, wide landscape of the game's world with its landmarks, the hero mid-leap, enemies and props in the scene",
    "Action": "a dramatic confrontation in the game's signature location, weather and light heightening the tension",
    "Adventure": "a wide vista of the game's world with a destination in the distance, the hero at the threshold of the journey",
    "RPG": "a sweeping fantasy landscape with castles, ruins or towns, the party or hero gazing toward a distant goal",
    "Puzzle": "a stylized abstract or architectural space built from the game's shapes and colors, lit theatrically",
    "Strategy": "a commanding overview of the game's battlefield or domain with units, terrain and weather",
    "Simulation": "the game's environment rendered with atmospheric realism and a human-scale subject",
    "Educational": "a warm, inviting scene built from the game's own characters and motifs",
    "Board/Card": "a dramatic, moody table or arena built from the game's pieces and iconography",
}

def prompt_for(g, rank):
    facts = f'"{g.get("display_title")}" ({g.get("year") or "year unknown"}, {g.get("publisher") or "publisher unknown"}, genre {g.get("genre") or "unknown"}, {g.get("players") or "?"} player(s))'
    genre = (g.get("genre") or "").split(",")[0].strip(); nudge = VARIATION.get(genre, "a dramatic scene in the game's own signature setting")
    return f"""Create ONE widescreen (16:9) cinematic key-art illustration for the NES game {facts}.

REFERENCE IMAGES, in order, and what each is for:
- Image 1, 2 and 3 are STYLE references only (key art for three other games). Match their rendering: pixel density, painterly
  texture, layered depth, lighting sophistication and overall polish. They say nothing about this game: do NOT reuse their
  subjects, characters, settings, compositions or palettes. Only images 4 to 6 define who and what appears in the scene.
- Image 4 is THIS game's box cover: the authoritative look of its characters, costumes, vehicles and colors (identity only; its
  flat cartoon rendering style is NOT to be copied).
- Image 5 is THIS game's title screen: its logo style is NOT to be drawn; use it only to recognize the game's mood and colors.
- Image 6 is THIS game's gameplay screen: the authoritative setting, objects, enemies and environment to build the scene from.

Depict this game's own world and its recognizable subjects, unmistakably this game, interpreted as a richly developed scene:
{nudge}. Build the environment from what the gameplay and cover show, expanded into a full widescreen landscape with layered,
textured scenery.

{STYLE}

Output: a single full-bleed image, no text anywhere."""

def load_manifest():
    if os.path.exists(MANIFEST): return json.load(open(MANIFEST, encoding="utf-8"))
    return {"schema": "ccs-hero-batch/1", "model": MODEL, "created": time.strftime("%Y-%m-%dT%H:%M:%S"), "games": {}, "batches": {}}

def save_manifest(m):
    os.makedirs(BATCH_DIR, exist_ok=True); m["updated"] = time.strftime("%Y-%m-%dT%H:%M:%S"); tmp = MANIFEST + ".tmp"
    json.dump(m, open(tmp, "w", encoding="utf-8"), indent=1, ensure_ascii=False); os.replace(tmp, MANIFEST)

def top100(by):
    """the exact consensus Top 100: rankings.json games with consensus <= 100 (the website's top100 filter logic)"""
    rk = json.load(open(os.path.join(REPORTS, "rankings.json"), encoding="utf-8"))["games"]
    rows = [(v["consensus"], gid) for gid, v in rk.items() if v.get("consensus") and v["consensus"] <= 100 and gid in by]
    return [gid for _, gid in sorted(rows)], {gid: v["consensus"] for gid, v in rk.items()}

def cmd_manifest(a):
    cat, by = hero_art.games(); m = load_manifest(); ids, ranks = top100(by)
    if a.include_poc:
        for gid in POC: ids.append(gid) if gid not in ids else None
    if a.ids: ids = [x for x in a.ids.split(",") if x]
    if a.limit: ids = ids[:a.limit]
    reg_p = os.path.join(hero_art.OUT, "index.json"); reg = json.load(open(reg_p, encoding="utf-8"))["games"] if os.path.exists(reg_p) else {}
    for gid in ids:
        g = by[gid]; rec = m["games"].get(gid) or {"attempts": 0, "batch": None, "last_error": None}
        rec.update({"title": g.get("display_title") or gid, "rank": ranks.get(gid), "has_cover": bool(g.get("art")), "has_title": bool(g.get("art_title")), "has_snap": bool(g.get("art_snap"))})
        if gid in reg and reg[gid].get("status") in ("approved", "candidate"): rec["status"] = reg[gid]["status"]; rec["sha256"] = reg[gid].get("sha256"); rec["provenance"] = reg[gid].get("provenance")
        else: rec.setdefault("status", "pending")
        m["games"][gid] = rec
    save_manifest(m); st = {}
    for r in m["games"].values(): st[r["status"]] = st.get(r["status"], 0) + 1
    print(f"manifest: {len(m['games'])} games", st, "->", MANIFEST)

def shrink(path):
    im = Image.open(path).convert("RGB"); im.thumbnail((REF_MAX, REF_MAX), Image.LANCZOS); b = io.BytesIO(); im.save(b, "PNG", optimize=True); return b.getvalue()

def style_refs():
    """the three approved illustrations (imported masters), shrunk; falls back to the owner's originals in _temp if a master is missing"""
    out = []
    for gid in STYLE_REF_IDS:
        p = os.path.join(hero_art.OUT, gid + ".png")
        if not os.path.exists(p): p = os.path.join(ROOT, "_temp", "images", "featured-shelf-hero-candidates", gid + ".png")
        if os.path.exists(p): out.append((f"style-{gid}", shrink(p)))
    return out

def cmd_prepare(a):
    cat, by = hero_art.games(); m = load_manifest(); name = a.batch or time.strftime("b%Y%m%d-%H%M")
    ids = [x for x in a.ids.split(",") if x] if a.ids else [gid for gid, r in m["games"].items() if r["status"] in ("pending", "failed") or (a.force and r["status"] != "approved")]
    if a.limit: ids = ids[:a.limit]
    if not ids: print("nothing to prepare (all games approved/candidate; use --force or --ids)"); return 1
    bdir = os.path.join(BATCH_DIR, name); os.makedirs(os.path.join(bdir, "prompts"), exist_ok=True); os.makedirs(os.path.join(bdir, "refs"), exist_ok=True)
    srefs = style_refs()
    if len(srefs) != 3: raise SystemExit(f"expected 3 style references, found {len(srefs)}; nothing prepared")
    lines = []; total = 0
    for gid in ids:
        g = by[gid]; refs = list(srefs); roles = [("style", n) for n, _ in srefs]
        for key, nm in (("art", "cover"), ("art_title", "title"), ("art_snap", "snap")):
            if g.get(key) and os.path.exists(os.path.join(REPORTS, g[key])): refs.append((nm, shrink(os.path.join(REPORTS, g[key])))); roles.append(("identity", nm))
        if len(refs) != 6: raise SystemExit(f"{gid} {g.get('display_title')}: expected cover, title screen and gameplay references, found {[r for r in roles if r[0] == 'identity']}; nothing prepared")
        rd = os.path.join(bdir, "refs", gid); os.makedirs(rd, exist_ok=True)
        for i, (nm, data) in enumerate(refs, 1): open(os.path.join(rd, f"{i:02d}-{nm}.png"), "wb").write(data)
        prompt = prompt_for(g, m["games"].get(gid, {}).get("rank")); open(os.path.join(bdir, "prompts", gid + ".md"), "w", encoding="utf-8").write(prompt)
        open(os.path.join(rd, "references.md"), "w", encoding="utf-8").write("\n".join(f"Image {i}: {role} reference ({nm})" for i, (role, nm) in enumerate(roles, 1)) + "\n")
        parts = [{"text": prompt}] + [{"inline_data": {"mime_type": "image/png", "data": base64.b64encode(d).decode("ascii")}} for _, d in refs]
        req = {"key": gid, "request": {"contents": [{"role": "user", "parts": parts}], "generation_config": {"response_modalities": ["IMAGE"], "image_config": {"aspect_ratio": "16:9", "image_size": "2K"}}}}
        line = json.dumps(req, ensure_ascii=False); lines.append(line); total += len(line)
        rec = m["games"].setdefault(gid, {"title": g.get("display_title"), "attempts": 0}); rec.update({"status": "prepared", "batch": name, "refs": [f"{role}:{nm}" for role, nm in roles], "prompt_version": PROMPT_VERSION})
    open(os.path.join(bdir, "requests.jsonl"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    m["batches"][name] = {"created": time.strftime("%Y-%m-%dT%H:%M:%S"), "games": ids, "state": "prepared", "job": None, "bytes": total, "prompt_version": PROMPT_VERSION}
    save_manifest(m); print(f"batch {name}: {len(ids)} requests, {total/1e6:.1f} MB, prompt {PROMPT_VERSION} ->", bdir)

def cmd_validate(a):
    m = load_manifest(); name = a.batch or latest(m); bdir = os.path.join(BATCH_DIR, name); p = os.path.join(bdir, "requests.jsonl"); ok = True; n = 0
    for line in open(p, encoding="utf-8"):
        if not line.strip(): continue
        n += 1; r = json.loads(line); key = r["key"]; req = r["request"]; parts = req["contents"][0]["parts"]
        txt = [x for x in parts if "text" in x]; imgs = [x for x in parts if "inline_data" in x]
        probs = []
        if len(txt) != 1 or len(txt[0]["text"]) < 400: probs.append("prompt missing/short")
        if len(imgs) != 6: probs.append(f"expected 6 references (3 style + cover + title + gameplay), found {len(imgs)}")
        if "STYLE references only" not in txt[0]["text"] or "Image 4 is THIS game" not in txt[0]["text"]: probs.append("prompt does not label the references")
        if "text" in txt[0]["text"].lower() and "no text" not in txt[0]["text"].lower(): probs.append("prompt lacks the no-text rule")
        for im in imgs:
            try: Image.open(io.BytesIO(base64.b64decode(im["inline_data"]["data"]))).verify()
            except Exception: probs.append("reference not decodable")
        gc = req.get("generation_config", {}); ic = gc.get("image_config", {})
        if gc.get("response_modalities") != ["IMAGE"] or ic.get("aspect_ratio") != "16:9": probs.append("generation config wrong")
        if key not in m["games"]: probs.append("key not in manifest")
        if probs: ok = False; print("FAIL", key, probs)
    print(f"validate {name}: {n} requests, {'OK' if ok else 'PROBLEMS'}"); return 0 if ok else 1

def cmd_estimate(a):
    m = load_manifest(); name = a.batch or latest(m); b = m["batches"][name]
    print(f"batch {name}: {len(b['games'])} image requests, request file {b['bytes']/1e6:.1f} MB, model {MODEL}, 2K 16:9 output.")
    print("Cost: see the current Gemini API pricing for gemini-3-pro-image (per output image, batch tier at half the standard rate); this tool does not fetch prices. No network call made.")

def latest(m):
    if not m["batches"]: raise SystemExit("no batches prepared")
    return sorted(m["batches"], key=lambda k: m["batches"][k]["created"])[-1]

def client():
    key = os.environ.get("GEMINI_API_KEY") or (open(os.environ["GEMINI_API_KEY_FILE"]).read().strip() if os.environ.get("GEMINI_API_KEY_FILE") else "")
    if not key: raise SystemExit("GEMINI_API_KEY is not set (or GEMINI_API_KEY_FILE); nothing was submitted")
    from google import genai
    return genai.Client(api_key=key)

def cmd_submit(a):
    if not a.confirm: raise SystemExit("submit is a PAID operation: re-run with --confirm after the owner's explicit authorization; nothing was submitted")
    m = load_manifest(); name = a.batch or latest(m); b = m["batches"][name]
    if b.get("job"): raise SystemExit(f"batch {name} was already submitted as {b['job']}; use status/fetch or prepare a new batch")
    if cmd_validate(argparse.Namespace(batch=name)) != 0: raise SystemExit("validation failed; nothing was submitted")
    c = client(); p = os.path.join(BATCH_DIR, name, "requests.jsonl")
    f = c.files.upload(file=p, config={"display_name": f"ccs-hero-{name}", "mime_type": "jsonl"})
    job = c.batches.create(model=MODEL, src=f.name, config={"display_name": f"ccs-hero-{name}"})
    b.update({"job": job.name, "state": str(job.state), "submitted": time.strftime("%Y-%m-%dT%H:%M:%S"), "input_file": f.name})
    for gid in b["games"]: m["games"][gid]["status"] = "submitted"; m["games"][gid]["attempts"] = m["games"][gid].get("attempts", 0) + 1
    save_manifest(m); json.dump({"job": job.name, "state": str(job.state)}, open(os.path.join(BATCH_DIR, name, "job.json"), "w")); print("submitted", name, job.name, job.state)

def cmd_status(a):
    m = load_manifest(); name = a.batch or latest(m); b = m["batches"][name]
    if not b.get("job"): print(name, "not submitted"); return
    c = client()
    while True:
        job = c.batches.get(name=b["job"]); b["state"] = str(job.state); save_manifest(m); print(name, job.name, job.state)
        if not a.watch or "RUNNING" not in str(job.state) and "PENDING" not in str(job.state): break
        time.sleep(60)

def extract_image(r: dict):
    """one JSONL response line -> (key, image bytes or None, error or None); tolerant of camelCase and snake_case"""
    key = r.get("key") or r.get("custom_id"); resp = r.get("response") or {}; err = r.get("error") or resp.get("error")
    for cand in resp.get("candidates", []):
        for part in cand.get("content", {}).get("parts", []):
            d = part.get("inlineData") or part.get("inline_data")
            if d and d.get("data"): return key, base64.b64decode(d["data"]), None
    return key, None, err or "no image in response"

def sharpness(im: Image.Image):
    """(detail, strong_edge_fraction) at a normalized 1620 px width: detail = mean difference between the image and a
    Gaussian-blurred copy of itself; strong = share of horizontal neighbor pairs differing by more than 24 levels.
    A crisp image, even flat-shaded pixel art with large plain areas, keeps far more fine detail than its blur; a soft
    upscale keeps almost none. Scale- and style-independent, unlike raw edge energy."""
    import numpy as np
    from PIL import ImageFilter
    n = im.convert("L").resize((1620, max(1, round(im.height * 1620 / im.width))), Image.LANCZOS); an = np.array(n).astype(float)
    ab = np.array(n.filter(ImageFilter.GaussianBlur(2.0))).astype(float)
    return float(np.abs(an - ab).mean()), float((np.abs(np.diff(an, axis=1)) > 24).mean())

def technical_check(im: Image.Image):
    """size, aspect, letterbox/border, flatness, sharpness; returns a list of problems (empty = pass)"""
    import numpy as np
    probs = []
    if im.width < OUT_MIN[0] or im.height < OUT_MIN[1]: probs.append(f"too small {im.size}")
    if abs(im.width / im.height - 16 / 9) > 0.05: probs.append(f"aspect {im.width / im.height:.3f}")
    a = np.array(im.convert("L")).astype(int); h, w = a.shape
    for name, strip in (("top", a[: h // 40]), ("bottom", a[-h // 40:]), ("left", a[:, : w // 40]), ("right", a[:, -w // 40:])):
        if strip.std() < 3: probs.append(f"flat {name} border")
    if a.std() < 18: probs.append("image is nearly flat")
    detail, strong = sharpness(im)
    if detail < 1.6 or strong < 0.012: probs.append(f"soft image (detail {detail:.2f}, strong edges {strong * 100:.2f}%)")
    return probs

def cmd_fetch(a):
    m = load_manifest(); name = a.batch or latest(m); b = m["batches"][name]; rdir = os.path.join(BATCH_DIR, name, "results"); os.makedirs(rdir, exist_ok=True)
    if getattr(a, "local", False):   # re-process the stored responses offline (for example after a validation rule changed); no network, no new request
        data = open(os.path.join(rdir, "responses.jsonl"), encoding="utf-8").read()
    else:
        c = client(); job = c.batches.get(name=b["job"]); b["state"] = str(job.state)
        if "SUCCEEDED" not in str(job.state): save_manifest(m); print("job state", job.state, "- nothing fetched"); return 1
        dest = job.dest; data = None
        if getattr(dest, "file_name", None): data = c.files.download(file=dest.file_name).decode("utf-8")
        elif getattr(dest, "inlined_responses", None): data = "\n".join(json.dumps(r.model_dump() if hasattr(r, "model_dump") else r) for r in dest.inlined_responses)
        if not data: raise SystemExit("no results in the job")
        open(os.path.join(rdir, "responses.jsonl"), "w", encoding="utf-8").write(data)
    cat, by = hero_art.games(); good = bad = 0
    for line in data.splitlines():
        if not line.strip(): continue
        key, img, err = extract_image(json.loads(line))
        rec = m["games"].get(key, {})
        if not img:
            rec["status"] = "failed"; rec["last_error"] = str(err or "no image in response")[:300]; bad += 1; continue
        raw = os.path.join(rdir, key + ".png"); open(raw, "wb").write(img); im = Image.open(raw); probs = technical_check(im)
        rec["generated_size"] = list(im.size)
        if probs: rec["status"] = "failed"; rec["last_error"] = "technical: " + "; ".join(probs); bad += 1; continue
        os.environ["HERO_PROVENANCE"] = f"generated:{MODEL}:{name}"; os.environ["HERO_PROMPT_VERSION"] = str(b.get("prompt_version") or PROMPT_VERSION); hero_art.import_one(key, raw, cat, by, tag=name); rec["status"] = "candidate"; rec["sha256"] = by[key].get("art_hero_sha256"); rec["last_error"] = None; good += 1
    json.dump(cat, open(os.path.join(REPORTS, "catalog.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    b["state"] = "fetched"; b["good"] = good; b["bad"] = bad; save_manifest(m); print(f"fetched {name}: {good} candidates, {bad} failed"); cmd_sheet(argparse.Namespace(batch=name))

def cmd_retry(a):
    m = load_manifest(); name = a.batch or latest(m); ids = [gid for gid in m["batches"][name]["games"] if m["games"][gid]["status"] in ("failed", "rejected")]
    if not ids: print("nothing to retry"); return
    cmd_prepare(argparse.Namespace(batch=f"{name}-retry{int(time.time()) % 10000}", ids=",".join(ids), limit=None, force=True))

def cmd_smoke(a):
    """prepare a ONE-image batch (the paid API smoke test): same request shape, same submit/status/fetch path as production"""
    cat, by = hero_art.games(); m = load_manifest(); gid = a.id
    if gid not in by: raise SystemExit("unknown game " + gid)
    m["games"].setdefault(gid, {"title": by[gid].get("display_title"), "attempts": 0, "status": "pending"}); save_manifest(m)
    name = a.batch or "smoke1"; cmd_prepare(argparse.Namespace(batch=name, ids=gid, limit=None, force=True)); cmd_validate(argparse.Namespace(batch=name))
    print(f"smoke batch {name} prepared for {by[gid].get('display_title')}; submit with: hero_batch.py submit --batch {name} --confirm   (PAID, one image; owner authorization required)")

def cmd_sheet(a):
    m = load_manifest(); name = a.batch or latest(m); b = m["batches"][name]; cat, by = hero_art.games(); art = json.load(open(os.path.join(ROOT, "tools", "publish", "assets", "home", "home-art.json"), encoding="utf-8"))
    hb = art["themes"]["featured-shelf"]["geometry"]["hero"]["frame"]; master = Image.open(os.path.join(ROOT, "tools", "publish", "assets", "home", art["themes"]["featured-shelf"]["reference"])).convert("RGB").crop(tuple(hb))
    f = ImageFont.truetype("C:/Windows/Fonts/consola.ttf", 16) if os.path.exists("C:/Windows/Fonts/consola.ttf") else ImageFont.load_default()
    tw, th = 480, 270; rows = []
    for gid in b["games"]:
        p = os.path.join(hero_art.OUT, gid + ".png"); rec = m["games"][gid]
        if os.path.exists(p) and rec["status"] in ("candidate", "approved"): rows.append((gid, Image.open(p).convert("RGB").resize((tw, th), Image.LANCZOS), rec))
        else: rows.append((gid, None, rec))
    per = 8
    for k in range(0, len(rows), per):
        chunk = rows[k:k + per]; sheet = Image.new("RGB", (3 * (tw + 8), len(chunk) * (th + 30) + 30), (12, 12, 14)); d = ImageDraw.Draw(sheet)
        d.text((6, 6), f"hero batch {name} · sheet {k // per + 1} · master hero | candidate | cover   (status in label; approve/reject with hero_art.py)", fill=(255, 255, 255), font=f)
        for i, (gid, im, rec) in enumerate(chunk):
            y = 30 + i * (th + 30); sheet.paste(master.resize((tw, th), Image.LANCZOS), (0, y + 24))
            if im: sheet.paste(im, (tw + 8, y + 24))
            else: d.rectangle((tw + 8, y + 24, 2 * tw + 8, y + 24 + th), outline=(120, 40, 40)); d.text((tw + 20, y + 34), "no image: " + str(rec.get("last_error") or rec["status"])[:70], fill=(255, 150, 150), font=f)
            g = by.get(gid, {})
            if g.get("art") and os.path.exists(os.path.join(REPORTS, g["art"])):
                cv = Image.open(os.path.join(REPORTS, g["art"])).convert("RGB"); cv.thumbnail((tw, th)); sheet.paste(cv, (2 * (tw + 8), y + 24))
            d.text((tw + 8, y + 4), f"{rec.get('rank') or '-'}  {rec.get('title')}  [{rec['status']}]  {gid}", fill=(255, 255, 255), font=f)
        sheet.save(os.path.join(BATCH_DIR, name, f"sheet-{k // per + 1}.png"))
    print("sheets written for", name)

def main(argv):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter); sub = ap.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("manifest"); s.add_argument("--limit", type=int); s.add_argument("--ids"); s.add_argument("--include-poc", action="store_true"); s.set_defaults(fn=cmd_manifest)
    s = sub.add_parser("prepare"); s.add_argument("--batch"); s.add_argument("--ids"); s.add_argument("--limit", type=int); s.add_argument("--force", action="store_true"); s.set_defaults(fn=cmd_prepare)
    s = sub.add_parser("validate"); s.add_argument("--batch"); s.set_defaults(fn=cmd_validate)
    s = sub.add_parser("estimate"); s.add_argument("--batch"); s.set_defaults(fn=cmd_estimate)
    s = sub.add_parser("submit"); s.add_argument("--batch"); s.add_argument("--confirm", action="store_true"); s.set_defaults(fn=cmd_submit)
    s = sub.add_parser("status"); s.add_argument("--batch"); s.add_argument("--watch", action="store_true"); s.set_defaults(fn=cmd_status)
    s = sub.add_parser("fetch"); s.add_argument("--batch"); s.add_argument("--local", action="store_true", help="re-process stored responses offline"); s.set_defaults(fn=cmd_fetch)
    s = sub.add_parser("retry"); s.add_argument("--batch"); s.set_defaults(fn=cmd_retry)
    s = sub.add_parser("sheet"); s.add_argument("--batch"); s.set_defaults(fn=cmd_sheet)
    s = sub.add_parser("smoke"); s.add_argument("--id", required=True); s.add_argument("--batch"); s.set_defaults(fn=cmd_smoke)
    a = ap.parse_args(argv); return a.fn(a) or 0

if __name__ == "__main__": sys.exit(main(sys.argv[1:]))
