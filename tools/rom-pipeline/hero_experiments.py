"""Controlled hero-art experiment campaign (development-only; never shipped). Super Mario Bros. 3 only, by owner authorization.

Many independent attempts for ONE game, each with its own experiment id, prompt, exact reference list, method (generate or
image-to-image edit), model configuration, batch job, usage, estimated cost, untouched result, normalized candidate and
provenance. Nothing overwrites an earlier candidate (hero_art tags). Spending safeguards live here, not at Google:
  CAMPAIGN_MAX_REQUESTS and CAMPAIGN_MAX_USD are hard ceilings on submitted requests and on the cumulative CONSERVATIVE
  estimate (standard-tier prices, so the batch tier can only come in under). `submit` refuses anything that would cross them.

  python hero_experiments.py plan --wave 1            write the wave's experiments into the campaign (offline)
  python hero_experiments.py build --wave 1           build requests.jsonl + per-experiment prompt/refs for the wave (offline)
  python hero_experiments.py validate --wave 1        schema, references, key->experiment mapping, ceilings (offline)
  python hero_experiments.py estimate [--wave N]      per-request and cumulative conservative cost (offline)
  python hero_experiments.py submit --wave 1 --confirm   PAID: one Batch API job for the wave (owner-authorized campaign only)
  python hero_experiments.py status --wave 1 [--watch]
  python hero_experiments.py fetch --wave 1 [--local] download, map every result to its experiment, validate, import as tagged candidates, write review sheets
  python hero_experiments.py review                   rebuild the campaign review package (contact sheets, crops, configs, costs)
  python hero_experiments.py ledger                   print every submitted request with its estimated cost and status

Layout: reports/art/hero-experiments/campaign.json, waves/<n>/requests.jsonl, waves/<n>/<exp>/ (prompt.md, references.md,
refs/, result.png, result-crops.png), review/ (sheets).
"""
from __future__ import annotations
import os, sys, json, time, base64, io, argparse, hashlib
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import REPORTS
from PIL import Image, ImageDraw, ImageFont
import hero_art, hero_batch as hb

GAME = "959fdd32c71735d6"; GAME_TITLE = "Super Mario Bros. 3"
MODEL = hb.MODEL
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DIR = os.path.join(REPORTS, "art", "hero-experiments"); CAMPAIGN = os.path.join(DIR, "campaign.json")
CAMPAIGN_MAX_REQUESTS = 36; CAMPAIGN_MAX_USD = 7.00
# conservative = STANDARD tier (the Batch tier halves both): input $2.00 per 1M tokens, image output $120 per 1M tokens, a 2K image = 1120 tokens
PRICE_IN_PER_TOKEN = 2.00 / 1e6; PRICE_OUT_IMAGE = 1120 * 120.00 / 1e6
TOKENS_PER_REF_IMAGE = 774          # measured: one 1024 px reference image cost 774 prompt tokens in smoke1 (three refs = 2322 in smoke4 at larger sizes); the estimate uses 1000 per image
EST_TOKENS_PER_IMAGE = 1000; EST_TOKENS_TEXT = 900; EST_TOKENS_OUT = 1400   # wave 1 measured: ~1290-1380 output tokens per request (image + a little text), 643-1805 input

ORIG = {g: os.path.join(hero_art.OUT, "originals", f"{g}-owner-poc.png") for g in hb.STYLE_REF_IDS}
NAMES = {"8e844d77da01f86a": "10-Yard Fight", "9f54aafa367247b9": "1943", "ec0d4ebf6d2fcecd": "The Legend of Zelda"}
SMOKE3 = os.path.join(hero_art.OUT, "originals", f"{GAME}-smoke3.png")

# ---------------------------------------------------------------- prompt language (short, visual; no pixel arithmetic, no dithering instructions)
IDENTITY = ("The last three reference images are this game's box cover (who the characters are and their colors; not a drawing style to copy), "
            "its title screen (mood and colors only; do not draw the logo) and a gameplay screen (the authentic setting, objects and enemies; not a composition to enlarge).")
SCENE = ("Scene: Super Mario Bros. 3. Raccoon Mario mid-leap, large, in the center-left of a wide landscape of the game's world: hills, pipes, mushrooms, "
         "blocks, a castle or airship in the distance, Goombas and Koopas below, a bright cheerful sky. Full bleed 16:9, no text, logo, border or UI; the "
         "upper right stays quiet and open for a title; scenery runs to the bottom edge with no ground strip or checkerboard band.")
LANG = {
    "neutral": "Render it in exactly the visual finish of the style reference illustration(s): the same fine, dense, richly detailed texture, the same lighting and depth, the same overall look. Match how they look.",
    "pixel-painted": "Render it as a rich cinematic illustration with fine pixel-painted textures, in the same finish as the style reference illustration(s).",
    "hires-pixel": "Render it as an intricately rendered high-resolution pixel-art illustration, in the same finish as the style reference illustration(s).",
    "painterly-raster": "Render it as a painterly raster illustration matching the actual visual finish of the style reference illustration(s): their texture, light, color depth and detail.",
    "detail-dense": "Also raise the fine detail density everywhere to the level of the style reference illustration(s): individual grass blades and small flowers, leaf and bark texture, weathering and cracks on pipes and stone, clouds with volume and lit edges, small distant elements (birds, ships, towers, windows), subtle highlights and cast shadows, so the image rewards close viewing. Keep every character, position, shape and color exactly as it is.",
}
EDIT = ("Keep this image's scene, characters, composition, framing and colors exactly as they are. Change only the artistic rendering: re-render the whole "
        "image in the visual finish of the style reference illustration(s) that follow it: the same fine, dense, richly detailed texture, lighting, shading "
        "and depth. No text, logo, border or UI. Output one full-bleed 16:9 image.")

def style_part(gid, crop=False):
    if crop:
        x, y = hb.STYLE_CROPS[gid]; im = Image.open(ORIG[gid]).convert("RGB").crop((x, y, x + hb.CROP, y + hb.CROP)); b = io.BytesIO(); im.save(b, "PNG", optimize=True); return (f"style-crop-{NAMES[gid]}", b.getvalue())
    return (f"style-{NAMES[gid]}", hb.native_png(ORIG[gid]))

def identity_parts(by):
    g = by[GAME]; out = []
    for key, nm, longest in (("art", "identity-cover", 640), ("art_title", "identity-title", 512), ("art_snap", "identity-gameplay", 512)):
        out.append((nm, hb.shrink(os.path.join(REPORTS, g[key]), longest)))
    return out

def exp_def(eid, variant, method, styles, crops, lang, samples_note="", source=None):
    """one experiment definition (what, not bytes)"""
    return {"id": eid, "game": GAME, "variant": variant, "method": method, "styles": styles, "crops": crops, "lang": lang, "source": source, "note": samples_note}

def wave_plan(n: int, campaign: dict) -> list:
    Z, F, T = "ec0d4ebf6d2fcecd", "9f54aafa367247b9", "8e844d77da01f86a"
    if n == 1:
        return [
            exp_def("w1-A1a", "A: one style ref (Zelda) + short visual prompt", "generate", [Z], False, "neutral", "sample 1 of 2"),
            exp_def("w1-A1b", "A: one style ref (Zelda) + short visual prompt", "generate", [Z], False, "neutral", "sample 2 of 2"),
            exp_def("w1-A2",  "A: one style ref (1943) + short visual prompt", "generate", [F], False, "neutral"),
            exp_def("w1-A3",  "A: one style ref (10-Yard Fight) + short visual prompt", "generate", [T], False, "neutral"),
            exp_def("w1-B1a", "B: three style refs, no crops, neutral language", "generate", [T, F, Z], False, "neutral", "sample 1 of 2"),
            exp_def("w1-B1b", "B: three style refs, no crops, neutral language", "generate", [T, F, Z], False, "neutral", "sample 2 of 2"),
            exp_def("w1-C1",  "C: three style refs, language 'pixel-painted textures'", "generate", [T, F, Z], False, "pixel-painted"),
            exp_def("w1-C2",  "C: three style refs, language 'high-resolution pixel-art illustration'", "generate", [T, F, Z], False, "hires-pixel"),
            exp_def("w1-C3",  "C: three style refs, language 'painterly raster, actual finish'", "generate", [T, F, Z], False, "painterly-raster"),
            exp_def("w1-D1a", "D: image-to-image from smoke3 + three style refs", "edit", [T, F, Z], False, "neutral", "sample 1 of 2", source=SMOKE3),
            exp_def("w1-D1b", "D: image-to-image from smoke3 + three style refs", "edit", [T, F, Z], False, "neutral", "sample 2 of 2", source=SMOKE3),
            exp_def("w1-D2",  "D: image-to-image from smoke3 + one style ref (Zelda)", "edit", [Z], False, "neutral", source=SMOKE3),
        ]
    raise SystemExit(f"wave {n} is planned after reviewing wave {n - 1}: add it with `plan --wave {n} --from <json>`")

def load():
    if os.path.exists(CAMPAIGN): return json.load(open(CAMPAIGN, encoding="utf-8"))
    return {"schema": "ccs-hero-experiments/1", "game": GAME, "title": GAME_TITLE, "model": MODEL, "max_requests": CAMPAIGN_MAX_REQUESTS, "max_usd": CAMPAIGN_MAX_USD, "experiments": {}, "waves": {}, "submitted_requests": 0, "estimated_usd": 0.0}

def save(c):
    os.makedirs(DIR, exist_ok=True); c["updated"] = time.strftime("%Y-%m-%dT%H:%M:%S"); tmp = CAMPAIGN + ".tmp"; json.dump(c, open(tmp, "w", encoding="utf-8"), indent=1); os.replace(tmp, CAMPAIGN)

def estimate_request(n_images: int, text_chars: int) -> float:
    tokens_in = n_images * EST_TOKENS_PER_IMAGE + max(EST_TOKENS_TEXT, text_chars // 4)
    return round(tokens_in * PRICE_IN_PER_TOKEN + EST_TOKENS_OUT * 120.00 / 1e6, 4)

def cmd_plan(a):
    c = load(); exps = json.load(open(a.from_json, encoding="utf-8")) if getattr(a, "from_json", None) else wave_plan(a.wave, c)
    for e in exps:
        if e["id"] in c["experiments"]: raise SystemExit(f"experiment {e['id']} already exists; ids are never reused")
        e.update({"wave": a.wave, "status": "planned", "prompt_version": "campaign-v1"}); c["experiments"][e["id"]] = e
    c["waves"][str(a.wave)] = {"ids": [e["id"] for e in exps], "state": "planned", "job": None}; save(c); print(f"wave {a.wave}: {len(exps)} experiments planned ->", CAMPAIGN)

def build_prompt(e):
    ns = len(e["styles"])
    if e["method"] == "edit":
        head = f"The first image is the picture to re-render. The next {ns} image(s) are STYLE reference illustration(s) (other games): they define the visual finish only, not the content."
        if e["crops"]: head += f" The {ns} image(s) after those are close-up crops of the same reference illustrations at full size, showing the fine texture and grain of their finish up close; the re-rendered image must show that grain when viewed up close."
        tail = "" if e["lang"] == "neutral" else "\n\n" + LANG[e["lang"]]
        return f"{head}\n\n{EDIT}{tail}"
    head = f"The first {ns} image(s) are STYLE reference illustration(s) for other games: they define the visual finish only (texture, light, depth, overall look); do not reuse their subjects or compositions."
    if e["crops"]: head += f" The {ns} image(s) after those are close-up crops of the same reference illustrations at full size, showing the fine texture and grain of their finish up close; the new image must show that grain when viewed up close."
    head += " " + IDENTITY
    return f"{head}\n\n{SCENE}\n\n{LANG[e['lang']]}"

def build_parts(e, by):
    refs = []
    if e["method"] == "edit":
        src = e["source"]; label = "source-" + (os.path.basename(os.path.dirname(src)) if os.path.basename(src) == "result.png" else os.path.splitext(os.path.basename(src))[0].split("-")[-1])
        refs.append((label, hb.native_png(src)))
    for gid in e["styles"]: refs.append(style_part(gid))
    if e["crops"]:
        for gid in e["styles"]: refs.append(style_part(gid, crop=True))
    if e["method"] == "generate": refs += identity_parts(by)
    return refs

def cmd_build(a):
    c = load(); w = c["waves"].get(str(a.wave))
    if not w: raise SystemExit("plan the wave first")
    cat, by = hero_art.games(); wdir = os.path.join(DIR, "waves", str(a.wave)); os.makedirs(wdir, exist_ok=True); lines = []; total = 0.0
    for eid in w["ids"]:
        e = c["experiments"][eid]; edir = os.path.join(wdir, eid); os.makedirs(os.path.join(edir, "refs"), exist_ok=True)
        prompt = build_prompt(e); refs = build_parts(e, by)
        for i, (nm, data) in enumerate(refs, 1): open(os.path.join(edir, "refs", f"{i:02d}-{nm}.png"), "wb").write(data)
        open(os.path.join(edir, "prompt.md"), "w", encoding="utf-8").write(prompt)
        open(os.path.join(edir, "references.md"), "w", encoding="utf-8").write("\n".join(f"Image {i}: {nm}" for i, (nm, _) in enumerate(refs, 1)) + "\n")
        parts = [{"text": prompt}] + [{"inline_data": {"mime_type": "image/png", "data": base64.b64encode(d).decode("ascii")}} for _, d in refs]
        req = {"key": eid, "request": {"contents": [{"role": "user", "parts": parts}], "generation_config": {"response_modalities": ["IMAGE"], "image_config": {"aspect_ratio": "16:9", "image_size": "2K"}}}}
        lines.append(json.dumps(req, ensure_ascii=False)); est = estimate_request(len(refs), len(prompt)); total += est
        e.update({"status": "built", "refs": [nm for nm, _ in refs], "prompt_sha": hashlib.sha256(prompt.encode()).hexdigest()[:12], "config": req["request"]["generation_config"], "est_usd": est})
    open(os.path.join(wdir, "requests.jsonl"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    w.update({"state": "built", "est_usd": round(total, 2), "requests": len(lines)}); save(c); print(f"wave {a.wave}: {len(lines)} requests built, conservative estimate ${total:.2f} ->", wdir)

def cmd_validate(a):
    c = load(); w = c["waves"][str(a.wave)]; p = os.path.join(DIR, "waves", str(a.wave), "requests.jsonl"); probs = []; keys = []
    for line in open(p, encoding="utf-8"):
        if not line.strip(): continue
        r = json.loads(line); keys.append(r["key"]); e = c["experiments"].get(r["key"])
        if not e: probs.append(f"{r['key']}: not in campaign"); continue
        parts = r["request"]["contents"][0]["parts"]; imgs = [x for x in parts if "inline_data" in x]; txt = [x for x in parts if "text" in x]
        if len(txt) != 1 or len(txt[0]["text"]) < 200: probs.append(f"{r['key']}: prompt missing")
        if len(imgs) != len(e["refs"]): probs.append(f"{r['key']}: {len(imgs)} images vs {len(e['refs'])} recorded refs")
        if r["request"]["generation_config"]["image_config"] != {"aspect_ratio": "16:9", "image_size": "2K"}: probs.append(f"{r['key']}: config")
        for x in imgs:
            try: Image.open(io.BytesIO(base64.b64decode(x["inline_data"]["data"]))).verify()
            except Exception: probs.append(f"{r['key']}: undecodable reference")
        if "pixel" in txt[0]["text"].lower() and ("1 to 3" in txt[0]["text"] or "dither" in txt[0]["text"].lower()): probs.append(f"{r['key']}: prompt prescribes pixel sizes or dithering")
    if len(keys) != len(set(keys)): probs.append("duplicate keys")
    if set(keys) != set(w["ids"]): probs.append("keys differ from the wave plan")
    if c["submitted_requests"] + len(keys) > CAMPAIGN_MAX_REQUESTS: probs.append(f"would exceed {CAMPAIGN_MAX_REQUESTS} requests")
    if c["estimated_usd"] + w.get("est_usd", 0) > CAMPAIGN_MAX_USD: probs.append(f"would exceed ${CAMPAIGN_MAX_USD:.2f}")
    print(f"validate wave {a.wave}: {len(keys)} requests, {'OK' if not probs else 'PROBLEMS'}"); [print("  ", x) for x in probs]; return 0 if not probs else 1

def cmd_estimate(a):
    c = load()
    for n, w in c["waves"].items(): print(f"wave {n}: {w.get('requests', len(w['ids']))} requests, est ${w.get('est_usd', 0):.2f}, state {w['state']}")
    print(f"campaign: {c['submitted_requests']} of {CAMPAIGN_MAX_REQUESTS} requests submitted, ${c['estimated_usd']:.2f} of ${CAMPAIGN_MAX_USD:.2f} estimated (standard-tier prices; batch tier bills half)")

def cmd_submit(a):
    if not a.confirm: raise SystemExit("submit is PAID: re-run with --confirm (owner-authorized campaign only); nothing was submitted")
    c = load(); w = c["waves"][str(a.wave)]
    if w.get("job"): raise SystemExit(f"wave {a.wave} already submitted as {w['job']}")
    if cmd_validate(argparse.Namespace(wave=a.wave)) != 0: raise SystemExit("validation failed; nothing was submitted")
    n = w["requests"]; est = w["est_usd"]
    if c["submitted_requests"] + n > CAMPAIGN_MAX_REQUESTS or c["estimated_usd"] + est > CAMPAIGN_MAX_USD: raise SystemExit("ceiling reached; nothing was submitted")
    cl = hb.client(); p = os.path.join(DIR, "waves", str(a.wave), "requests.jsonl")
    f = cl.files.upload(file=p, config={"display_name": f"ccs-hero-exp-w{a.wave}", "mime_type": "jsonl"}); job = cl.batches.create(model=MODEL, src=f.name, config={"display_name": f"ccs-hero-exp-w{a.wave}"})
    w.update({"job": job.name, "state": str(job.state), "submitted": time.strftime("%Y-%m-%dT%H:%M:%S"), "input_file": f.name}); c["submitted_requests"] += n; c["estimated_usd"] = round(c["estimated_usd"] + est, 2)
    for eid in w["ids"]: c["experiments"][eid]["status"] = "submitted"; c["experiments"][eid]["job"] = job.name
    save(c); print(f"submitted wave {a.wave}: {n} requests, {job.name}, {job.state}; campaign {c['submitted_requests']}/{CAMPAIGN_MAX_REQUESTS} requests, ${c['estimated_usd']:.2f}/${CAMPAIGN_MAX_USD:.2f}")

def cmd_status(a):
    c = load(); w = c["waves"][str(a.wave)]; cl = hb.client()
    while True:
        job = cl.batches.get(name=w["job"]); w["state"] = str(job.state); save(c); print(a.wave, job.name, job.state)
        if not a.watch or ("RUNNING" not in str(job.state) and "PENDING" not in str(job.state)): break
        time.sleep(60)

def cmd_fetch(a):
    c = load(); w = c["waves"][str(a.wave)]; wdir = os.path.join(DIR, "waves", str(a.wave)); rp = os.path.join(wdir, "responses.jsonl")
    if getattr(a, "local", False): data = open(rp, encoding="utf-8").read()
    else:
        cl = hb.client(); job = cl.batches.get(name=w["job"]); w["state"] = str(job.state)
        if "SUCCEEDED" not in str(job.state): save(c); print("job state", job.state); return 1
        dest = job.dest; data = cl.files.download(file=dest.file_name).decode("utf-8") if getattr(dest, "file_name", None) else "\n".join(json.dumps(r.model_dump() if hasattr(r, "model_dump") else r) for r in (dest.inlined_responses or []))
        open(rp, "w", encoding="utf-8").write(data)
    cat, by = hero_art.games(); good = bad = 0
    for line in data.splitlines():
        if not line.strip(): continue
        r = json.loads(line); key, img, err = hb.extract_image(r); e = c["experiments"].get(key)
        if not e: print("response for unknown key", key); continue
        resp = r.get("response") or {}; e["usage"] = resp.get("usageMetadata"); e["model_version"] = resp.get("modelVersion"); e["finish"] = [x.get("finishReason") for x in resp.get("candidates", [])]
        if not img: e["status"] = "failed"; e["error"] = str(err)[:300]; bad += 1; continue
        edir = os.path.join(wdir, key); os.makedirs(edir, exist_ok=True); raw = os.path.join(edir, "result.png"); open(raw, "wb").write(img); im = Image.open(raw)
        e["result_size"] = list(im.size); e["technical"] = hb.technical_check(im); e["pixel_signature"] = hb.pixel_signature(im); e["pixel_verdict"] = hb.pixel_verdict(e["pixel_signature"])[0]
        hb.crop_sheet(raw, os.path.join(edir, "result-crops.png"), key)
        os.environ["HERO_PROVENANCE"] = f"generated:{MODEL}:experiment:{key}"; os.environ["HERO_PROMPT_VERSION"] = "campaign-v1:" + e["variant"]
        hero_art.import_one(GAME, raw, cat, by, tag=key); e["status"] = "candidate"; e["candidate_tag"] = key; good += 1
    json.dump(cat, open(os.path.join(REPORTS, "catalog.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    # actual usage priced at STANDARD rates replaces the estimate for this wave when it is higher (the ceiling never relaxes)
    actual = sum(((e.get("usage") or {}).get("promptTokenCount", 0) * PRICE_IN_PER_TOKEN + (e.get("usage") or {}).get("candidatesTokenCount", 0) * 120.00 / 1e6) for e in c["experiments"].values() if e.get("wave") == a.wave)
    w["actual_usd_standard"] = round(actual, 3); w["actual_usd_batch"] = round(actual / 2, 3)
    if actual > w.get("est_usd", 0): c["estimated_usd"] = round(c["estimated_usd"] - w.get("est_usd", 0) + actual, 2); w["est_usd"] = round(actual, 2)
    w["state"] = "fetched"; w["good"] = good; w["bad"] = bad; save(c); print(f"wave {a.wave}: {good} candidates, {bad} failed; actual ${actual:.2f} at standard prices (${actual/2:.2f} batch); campaign ${c['estimated_usd']:.2f}/${CAMPAIGN_MAX_USD:.2f}"); cmd_review(argparse.Namespace())

def cmd_review(a):
    c = load(); rdir = os.path.join(DIR, "review"); os.makedirs(rdir, exist_ok=True); f = ImageFont.truetype("C:/Windows/Fonts/consola.ttf", 18); fs = ImageFont.truetype("C:/Windows/Fonts/consola.ttf", 14)
    done = [e for e in c["experiments"].values() if e.get("status") == "candidate"]
    if not done: print("no results yet"); return
    tw, th = 640, 360; cols = 3; rows = (len(done) + 3 + cols - 1) // cols
    sheet = Image.new("RGB", (cols * (tw + 10), rows * (th + 52)), (12, 12, 14)); d = ImageDraw.Draw(sheet)
    tiles = [(f"ORIGINAL {NAMES[g]} (locked standard)", ORIG[g], "") for g in hb.STYLE_REF_IDS] + [(f"{e['id']}  {e['variant']}", os.path.join(DIR, "waves", str(e["wave"]), e["id"], "result.png"), f"{e['method']}, styles {len(e['styles'])}{'+crops' if e['crops'] else ''}, lang {e['lang']}; sig hard {e['pixel_signature']['hard']}% colors {e['pixel_signature']['colors_per_tile']} block {e['pixel_signature']['block_pitch']}") for e in done]
    for i, (label, p, sub) in enumerate(tiles):
        im = Image.open(p).convert("RGB"); w_, h_ = im.size; want = tw / th
        if w_ / h_ > want: nw = round(h_ * want); im = im.crop(((w_ - nw) // 2, 0, (w_ - nw) // 2 + nw, h_))
        elif w_ / h_ < want: nh = round(w_ / want); im = im.crop((0, (h_ - nh) // 2, w_, (h_ - nh) // 2 + nh))
        im = im.resize((tw, th), Image.LANCZOS); x = (i % cols) * (tw + 10); y = (i // cols) * (th + 52); sheet.paste(im, (x, y + 24)); d.text((x + 4, y + 4), label[:70], fill=(255, 255, 255), font=f); d.text((x + 4, y + th + 28), sub[:90], fill=(200, 200, 200), font=fs)
    sheet.save(os.path.join(rdir, "01-contact-sheet-all-results.png"))
    # crops: subject crop of every result at 100 % (3x) beside the Zelda and 1943 crops
    S, Z = 320, 3; tiles = [("ORIGINAL Zelda", Image.open(ORIG["ec0d4ebf6d2fcecd"]), hb.STYLE_CROPS["ec0d4ebf6d2fcecd"]), ("ORIGINAL 1943", Image.open(ORIG["9f54aafa367247b9"]), hb.STYLE_CROPS["9f54aafa367247b9"])]
    for e in done:
        im = Image.open(os.path.join(DIR, "waves", str(e["wave"]), e["id"], "result.png")); W, H = im.size; tiles.append((e["id"], im, (int(W * 0.30), int(H * 0.18))))
    cols = 4; rows = (len(tiles) + cols - 1) // cols; cs = Image.new("RGB", (cols * (S * Z + 10), rows * (S * Z + 30)), (12, 12, 14)); d = ImageDraw.Draw(cs)
    for i, (lab, im, (x, y)) in enumerate(tiles):
        cr = im.convert("RGB").crop((x, y, x + S, y + S)).resize((S * Z, S * Z), Image.NEAREST); cx = (i % cols) * (S * Z + 10); cy = (i // cols) * (S * Z + 30); cs.paste(cr, (cx, cy + 26)); d.text((cx + 4, cy + 4), f"{lab}  {S}px at 100% (3x)", fill=(255, 255, 255), font=f)
    cs.save(os.path.join(rdir, "02-crops-100pct-all-results.png"))
    # configuration and cost table
    L = ["# Experiment campaign: Super Mario Bros. 3 hero art (Gemini, Nano Banana Pro)", "", f"Ceilings: {CAMPAIGN_MAX_REQUESTS} requests, ${CAMPAIGN_MAX_USD:.2f} conservative estimate. Submitted: {c['submitted_requests']} requests, ${c['estimated_usd']:.2f} estimated (standard-tier prices).", "",
         "| id | wave | variant | method | style refs | crops | language | refs | job | result | est $ | usage (in/out tokens) | pixel sig (hard %, soft %, colors, block) | status |", "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|"]
    for e in c["experiments"].values():
        u = e.get("usage") or {}; sg = e.get("pixel_signature") or {}
        L.append(f"| {e['id']} | {e['wave']} | {e['variant']} | {e['method']} | {', '.join(NAMES[g] for g in e['styles'])} | {'yes' if e['crops'] else 'no'} | {e['lang']} | {len(e.get('refs', []))} | {(e.get('job') or '')[-12:]} | {e.get('result_size')} | {e.get('est_usd', 0):.3f} | {u.get('promptTokenCount', '')}/{u.get('candidatesTokenCount', '')} | {sg.get('hard', '')}, {sg.get('soft', '')}, {sg.get('colors_per_tile', '')}, {sg.get('block_pitch', '')} | {e['status']} |")
    L += ["", "Pixel signature is a supplementary diagnostic only; the standard is the visual appearance of the three originals.", "Per-experiment prompt and references: waves/<n>/<id>/prompt.md and references.md; untouched result: result.png; crops: result-crops.png."]
    open(os.path.join(rdir, "03-configurations-and-costs.md"), "w", encoding="utf-8").write("\n".join(L) + "\n"); print("review package ->", rdir)

def cmd_ledger(a):
    c = load(); print(f"submitted {c['submitted_requests']}/{CAMPAIGN_MAX_REQUESTS}, estimated ${c['estimated_usd']:.2f}/${CAMPAIGN_MAX_USD:.2f}")
    for e in c["experiments"].values(): print(f"  {e['id']:8s} wave {e['wave']} {e['status']:9s} est ${e.get('est_usd', 0):.3f} {e.get('variant', '')}")

def main(argv):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter); sub = ap.add_subparsers(dest="cmd", required=True)
    for name, fn, extra in (("plan", cmd_plan, ["--wave", "--from-json"]), ("build", cmd_build, ["--wave"]), ("validate", cmd_validate, ["--wave"]), ("estimate", cmd_estimate, []), ("submit", cmd_submit, ["--wave", "--confirm"]), ("status", cmd_status, ["--wave", "--watch"]), ("fetch", cmd_fetch, ["--wave", "--local"]), ("review", cmd_review, []), ("ledger", cmd_ledger, [])):
        s = sub.add_parser(name)
        for x in extra:
            if x == "--wave": s.add_argument("--wave", type=int, required=True)
            elif x == "--from-json": s.add_argument("--from-json", dest="from_json")
            else: s.add_argument(x, action="store_true")
        s.set_defaults(fn=fn)
    a = ap.parse_args(argv); return a.fn(a) or 0

if __name__ == "__main__": sys.exit(main(sys.argv[1:]))
