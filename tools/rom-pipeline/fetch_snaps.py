"""Fetch authentic gameplay screenshots (libretro-thumbnails Named_Snaps, 256 x 240 PNG) and title screens (Named_Titles)
for catalog games by exact No-Intro name, into reports/art/snaps/<game_id>.png and reports/art/titles/<game_id>.png.
These are the truthful wide-scene sources the Featured Shelf hero uses (no illustration exists for NES games, so the
hero shows the game's own screen). Idempotent; records reports/art/snaps/index.json with source attribution.
  python fetch_snaps.py [--titles]"""
from __future__ import annotations
import os, sys, json, re, io, time, urllib.request, urllib.parse, concurrent.futures as cf
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import REPORTS
from PIL import Image
RAW = "https://raw.githubusercontent.com/libretro-thumbnails/Nintendo_-_Nintendo_Entertainment_System/master/"
UA = "ClassicCartStudio/1.0 (contact: github.com/Rance9)"
def libretro_name(n: str) -> str: return re.sub(r'[&*/:`<>?\\|"]', "_", n)

def fetch_one(kind: str, gid: str, nointro: str) -> tuple[str, str]:
    d = os.path.join(REPORTS, "art", kind); os.makedirs(d, exist_ok=True); out = os.path.join(d, gid + ".png")
    if os.path.exists(out): return gid, "present"
    url = RAW + ("Named_Snaps/" if kind == "snaps" else "Named_Titles/") + urllib.parse.quote(libretro_name(nointro) + ".png")
    try:
        req = urllib.request.Request(url, headers={"User-Agent": UA})
        with urllib.request.urlopen(req, timeout=30) as r: data = r.read()
        im = Image.open(io.BytesIO(data)).convert("RGB")
        if im.size != (256, 240): im = im.resize((256, 240), Image.NEAREST)
        im.save(out, optimize=True); return gid, "fetched"
    except urllib.error.HTTPError as e: return gid, f"http {e.code}"
    except Exception as e: return gid, f"error {e}"

def main():
    kinds = ["snaps"] + (["titles"] if "--titles" in sys.argv else [])
    cat = json.load(open(os.path.join(REPORTS, "catalog.json"), encoding="utf-8")); games = [(g["game_id"], g.get("nointro_name") or "") for g in cat["games"] if g.get("nointro_name")]
    for kind in kinds:
        res = {}
        with cf.ThreadPoolExecutor(8) as ex:
            for gid, st in ex.map(lambda t: fetch_one(kind, *t), games): res[gid] = st
        counts = {}
        for st in res.values(): counts[st.split(" ")[0]] = counts.get(st.split(" ")[0], 0) + 1
        json.dump({"source": RAW + ("Named_Snaps" if kind == "snaps" else "Named_Titles"), "license": "libretro-thumbnails community project; screenshots of the games themselves, shown to identify them", "fetched": time.strftime("%Y-%m-%dT%H:%M:%S"), "status": res}, open(os.path.join(REPORTS, "art", kind, "index.json"), "w", encoding="utf-8"), indent=1)
        print(kind, counts)
    # mark availability in the catalog (art_snap / art_title paths) without touching `art`
    for g in cat["games"]:
        for kind, key in (("snaps", "art_snap"), ("titles", "art_title")):
            p = os.path.join(REPORTS, "art", kind, g["game_id"] + ".png")
            if os.path.exists(p): g[key] = f"art/{kind}/{g['game_id']}.png"
    json.dump(cat, open(os.path.join(REPORTS, "catalog.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1); print("catalog annotated")

if __name__ == "__main__": main()
