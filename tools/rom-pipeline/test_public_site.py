"""Checks on the built PUBLIC site (no private workspace needed):  python test_public_site.py <site_dir>
- the layout: the Windows app's copy (build_site_ci.py without --public: nes-catalog.html, the app's local document, and the same
  page as index.html), or the public website's entry pages (public_entry.py): prelaunch (index.html the coming-soon page, preview/ the
  app, not indexed) or launch (index.html the app, preview/ its transition copy); nes-catalog.html is not published there. Each entry
  page's title, description, robots, canonical, link-preview card and data; the splash: only its text, no link/picture, its
  flicker within three dips in any second, steady with reduced motion; its one button the sound control (the page without it byte for
  byte the coming-soon page; its files as recorded; nothing of it stored, preloaded or kept by the worker: test_splash_sound.py drives it);
  the service worker's page addresses and file checks
- required files (index.html, data JSON, .nojekyll, robots.txt, release.json, original sprites + index + provenance)
- every src/href/url() reference is relative (works under the /Classic-Cart-Studio/ sub-path and on localhost), exists, and
  nothing is fetched from a third-party host except the explicit GitHub links/API named in the page
- no firmware-crop or online-character sprite paths, no box-art references, no NES wordmark image, disclaimer present, title correct
- no local paths, no user names, no tokens (scan_prohibited rules) in the page or the data
- the inline script parses (node --check when node is available)
Exit 0 = PASS."""
from __future__ import annotations
import os, sys, re, json, subprocess, shutil
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, os.path.join(HERE, "..", "publish"))

def main(site: str):
    checks = []; ok = lambda k, v, n="": checks.append(f"{k}={'true' if v else 'false'}{(' ('+n+')') if n and not v else ''}")
    win = os.path.exists(os.path.join(site, "nes-catalog.html")); idx = open(os.path.join(site, "index.html"), encoding="utf-8").read()
    mode = "windows" if win else ("prelaunch" if 'class="soon"' in idx else "launch"); checks.append(f"layout={mode}")
    need = ["index.html", *(["nes-catalog.html"] if win else ["preview/index.html"]), "catalog.json", "builtin.json", "library.json", "descriptions.json", ".nojekyll", "robots.txt", "release.json", "brand/ccs/index.json", "brand/ccs/ground.png", "brand/fonts/PressStart2P-Regular.ttf", "art"]
    missing = [n for n in need if not os.path.exists(os.path.join(site, n))]; ok("required_files", not missing, str(missing))
    if mode == "windows":
        page = open(os.path.join(site, "nes-catalog.html"), encoding="utf-8").read(); ok("index_is_the_page", page == idx)
        ok("no_splash_sound_in_the_windows_copy", not os.path.exists(os.path.join(site, "brand", "splash")) and 'id="snd"' not in idx)
    else:
        page = open(os.path.join(site, "preview", "index.html"), encoding="utf-8").read() if mode == "prelaunch" else idx
        public_layout_checks(site, mode, idx, page, ok)
    ok("title_and_disclaimer", "<title>Classic Cart Studio</title>" in page and "independent" in page and ("not produced, licensed, sponsored, approved or endorsed by Nintendo" in page or "not produced, licensed, sponsored, approved, or endorsed by Nintendo" in page))
    # ---- the Classic Cart Studio cast (owner direction 2026-11-03, D-66: original CCS art replaces the console software's and community sprites) ----
    sys.path.insert(0, HERE); import build_html as BH
    pi = page.index('<div class="parade"'); parade = page[pi:page.index('<div class="ground">', pi)]
    hero_imgs = re.findall(r'src="(brand/(?:oem|web|ccs)/[^"]+\.png)"', parade); expected = [f"brand/{d}/{st}.png" for d, st, sc, cls in BH.HERO]
    ok("hero_parade_is_the_ccs_cast_in_order", hero_imgs == expected and len(expected) >= 12 and all(p.startswith("brand/ccs/") for p in expected), f"{len(hero_imgs)} of {len(expected)}")
    walk = re.findall(r'data-walk="(brand/ccs/[^"]+\.png)"', parade); ok("parade_characters_have_their_second_frame", len(walk) >= 10 and all(os.path.exists(os.path.join(site, p)) for p in walk), len(walk))
    ok("hero_sprites_exist", all(os.path.exists(os.path.join(site, p)) for p in expected))
    ok("no_console_software_or_community_sprites", "brand/oem/" not in page and "brand/web/" not in page and not os.path.exists(os.path.join(site, "brand", "oem")) and not os.path.exists(os.path.join(site, "brand", "web")))
    ok("interface_icons_are_ccs", page.count("brand/ccs/icon-") >= 8 and all(os.path.exists(os.path.join(site, "brand", "ccs", n)) for n in ("icon-a.png", "icon-b.png", "icon-cartridge.png", "icon-disk.png", "icon-warning.png", "icon-start.png")))
    ok("footer_roster_is_the_ccs_cast", page.count('src="brand/ccs/') >= 40, page.count('src="brand/ccs/'))
    ok("no_unexpected_public_folder_art", page.replace("brand/public/as-seen-on-tv.png", "").count("brand/public/") == 0 and page.count("brand/help/") >= 20)
    refs = set(re.findall(r'(?:src|href)="([^"]+)"', page)) | set(re.findall(r'url\("([^"]+)"\)', page))
    refs = {r for r in refs if not r.startswith(("data:", "#", "blob:", "mailto:")) and "${" not in r}
    ext = sorted(r for r in refs if re.match(r"^[a-z]+:", r)); allowed_ext = ("https://github.com/", "https://rance9.github.io/", "https://ClassicCartStudio.com/", "https://classiccartstudio.com/", "https://api.github.com/", "http://127.0.0.1:", "https://www.nintendo.com/", "https://www.ebay.com/itm/302788782864", "https://commons.wikimedia.org/wiki/File:", "https://classic-cart-studio.printful.me/",
        "https://ansimuz.com/", "https://opengameart.org/content/magic-cliffs-environment", "https://opengameart.org/content/sunnyland-forest-of-illusion",
        "https://opengameart.org/content/gothicvania-patreons-collection", "https://opengameart.org/content/sideview-fantasy-patreon-collection", "https://pineight.com/")   # credit anchors: the scene artwork's artist and sources (CC BY 3.0 asks for the source), the Included Games' author   # the seller recommendation, the photo credits and the merch store (M2-1: the owner-verified Printful address, exactly) are plain anchors, not assets
    bad_ext = [r for r in ext if not r.startswith(allowed_ext)]; ok("external_links_are_only_github_and_loopback", not bad_ext, str(bad_ext)[:200])
    rel = [r for r in refs if not re.match(r"^[a-z]+:", r)]; abs_ = [r for r in rel if r.startswith("/")]; ok("no_root_absolute_references (sub-path safe)", not abs_, str(abs_)[:200])
    miss = [r for r in rel if not os.path.exists(os.path.join(site, r.split("?")[0]))]; ok("relative_references_exist", not miss, str(miss)[:200])
    ok("no_external_script_or_style", not re.search(r'<script[^>]+src="https?://', page) and not re.search(r'<link(?![^>]*rel="canonical")[^>]+href="https?://', page))   # a canonical address is not a file
    try:
        import scan_prohibited as S
        hits = [m.group(0)[:40] for m in S.PRIVATE_PATH.finditer(page)]; ok("no_private_paths_in_page", not hits, str(hits)[:120])
        ok("no_secret_patterns_in_page", not S.SECRET.search(page))
        for n in ("catalog.json", "library.json", "builtin.json"):
            t = open(os.path.join(site, n), encoding="utf-8").read(); ok(f"no_private_paths_in_{n}", not S.PRIVATE_PATH.search(t))
            lp = [m.group(0)[:40] for m in S.LOCAL_PATH.finditer(t)][:3]; ok(f"no_local_file_paths_in_{n} (file: URIs, C:/NES, ...)", not lp, str(lp))
        lp = [m.group(0)[:40] for m in S.LOCAL_PATH.finditer(page)][:3]; ok("no_local_file_paths_in_page", not lp, str(lp))
    except ImportError: ok("scanner_available", False)
    cat = json.load(open(os.path.join(site, "catalog.json"), encoding="utf-8")); with_art = [g for g in cat["games"] if g.get("art")]
    # N3.1 artwork audit: 5 catalog scans that showed another product or region were hidden on purpose (the generated cover shows instead)
    ok("curated_cover_art_coverage_restored", len(cat["games"]) > 700 and len(with_art) >= 740, f"{len(with_art)} of {len(cat['games'])}")
    # one file per group of byte-identical covers (build_site_ci.py fold_covers): a folded name resolves through art/aliases.json to a kept file
    try: al = json.load(open(os.path.join(site, "art", "aliases.json"), encoding="utf-8"))
    except (OSError, ValueError): al = {}
    amap = al.get("aliases") or {}; here = lambda rel: os.path.exists(os.path.join(site, *amap.get(rel, rel).split("/")))
    ok("cover_art_files_exist", all(here(g["art"]) for g in with_art) and len(os.listdir(os.path.join(site, "art"))) >= 800)
    import hashlib as _h
    covers = [n for n in os.listdir(os.path.join(site, "art")) if re.fullmatch(r"[0-9a-f]{16}\.jpg", n)]; digests = [_h.sha256(open(os.path.join(site, "art", n), "rb").read()).digest() for n in covers]
    ok("covers_kept_once", al.get("schema") == "ccs-art-aliases/1" and len(set(digests)) == len(digests) and all(not os.path.exists(os.path.join(site, *k.split("/"))) and os.path.exists(os.path.join(site, *v.split("/"))) for k, v in amap.items()),
       f"{len(covers)} covers, {len(set(digests))} distinct, {len(amap)} folded")
    lib = json.load(open(os.path.join(site, "library.json"), encoding="utf-8")); titles = list(lib["titles"].values()) if isinstance(lib["titles"], dict) else lib["titles"]
    ok("library_cover_files_resolve", all(here(v[k]) for v in lib["versions"] for k in ("art", "art_family") if v.get(k)) and all(here(t["art"]) for t in titles if t.get("art")))
    pcov = set(re.findall(r'"(art/[0-9a-f]{16}\.jpg)"(?!:)', page)); ok("page_names_only_shipped_covers", len(pcov) >= 740 and all(os.path.exists(os.path.join(site, *c.split("/"))) for c in pcov), f"{len(pcov)} cover names in the page, missing {sorted(c for c in pcov if not os.path.exists(os.path.join(site, *c.split('/'))))[:3]}")
    ok("library_art_mappings_kept", sum(1 for t in titles if t.get("art")) >= 740 and sum(1 for v in lib["versions"] if v.get("art")) >= 800, (sum(1 for t in titles if t.get("art")), sum(1 for v in lib["versions"] if v.get("art"))))
    sprites = set(re.findall(r'src="(brand/ccs/[^"]+\.png)"', page)); ok("every_sprite_reference_exists", len(sprites) >= 20 and all(os.path.exists(os.path.join(site, s)) for s in sprites), str(len(sprites)))
    ci = json.load(open(os.path.join(site, "brand", "ccs", "index.json"), encoding="utf-8")); known = {f for c in ci["characters"] for f in c["frames"]} | {x["file"] for k in ("items", "icons", "tiles") for x in ci[k]}
    used = {s.split("/")[-1] for s in sprites}; ok("ccs_art_has_provenance", used <= known and ci.get("source", "").startswith("drawn in code"), str(sorted(used - known)))
    node = shutil.which("node")
    if node:   # every inline script of the page (the public preview adds a small one before the page's own)
        sc = re.findall(r"<script>(.*?)</script>", page, re.S); p = os.path.join(site, "_script_check.js"); errs = []
        for i, code in enumerate(sc or [""]):
            open(p, "w", encoding="utf-8").write(code); r = subprocess.run([node, "--check", p], capture_output=True, text=True)
            if r.returncode != 0: errs.append(f"script {i}: {r.stderr[:160]}")
        os.remove(p); ok("inline_script_parses", not errs and len(sc) >= 1 and max(len(x) for x in sc) > 100_000, str(errs)[:200])
    else: ok("inline_script_parses (node not available, skipped)", True)
    ok("page_size_reasonable", 1_000_000 < len(page) < 8_000_000, str(len(page)))
    ok("favicon_set_present", all(os.path.exists(os.path.join(site, n)) for n in ("favicon.ico", "icon-32.png", "apple-touch-icon.png")) and 'rel="icon" href="favicon.ico"' in page and 'rel="apple-touch-icon"' in page)
    app_p = os.path.join(site, "app", "index.html"); app = open(app_p, encoding="utf-8").read() if os.path.exists(app_p) else ""
    ok("app_shortlink_page_exists", bool(app), app_p)
    src = open(os.path.join(site, "source", "index.html"), encoding="utf-8").read() if os.path.exists(os.path.join(site, "source", "index.html")) else ""
    ok("source_shortlink_redirects_to_repo", src.count("https://github.com/Rance9/Classic-Cart-Studio") >= 3 and 'location.replace("https://github.com/Rance9/Classic-Cart-Studio")' in src and 'http-equiv="refresh" content="0; url=https://github.com/Rance9/Classic-Cart-Studio"' in src and "<title>Classic Cart Studio | Source Code</title>" in src and "releases/latest" not in src, len(src))
    ok("footer_links_branded_source", 'Source code, releases and how to verify a download: <a href="https://ClassicCartStudio.com/source" rel="noopener">ClassicCartStudio.com/source</a>' in page and "download: <a href=\"https://github.com/Rance9/Classic-Cart-Studio\"" not in page)
    ok("footer_cast_pauses_on_hover", "footer .castwrap:hover .cast,footer .castwrap:focus-within .cast{animation-play-state:paused}" in page)
    ok("app_download_page_is_first_party", 'http-equiv="refresh"' not in app and "location.replace(" not in app and "<title>Classic Cart Studio | Windows Downloads</title>" in app and 'href="https://classiccartstudio.com/app/"' in app and 'name="robots" content="noindex,follow"' in app)
    ok("app_download_page_separates_preview_products", "/releases/download/v0.11.6/ClassicCartStudio-Setup-0.11.6.exe" in app and "/releases/download/workbench-v0.11.6/CCS-Console-Workbench-0.11.6.exe" in app and "Development previews" in app and "unsigned" in app and "Console connections, console writes and USB driver actions are disabled" in app and "/releases/latest" not in app and 'href="https://github.com/Rance9/Classic-Cart-Studio"' not in app)
    bad = [c for c in checks if "=false" in c]; print("\n".join(checks)); print(f"RESULT: {'PASS' if not bad else 'FAIL '+str(bad)} ({len(checks)} checks)"); sys.exit(1 if bad else 0)

SITE_URL = "https://classiccartstudio.com"
def _meta(doc, attr, key):
    m = re.search(r'<meta ' + attr + r'="' + re.escape(key) + r'" content="([^"]*)"', doc); return m.group(1) if m else None
def _canon(doc):
    m = re.search(r'<link rel="canonical" href="([^"]*)"', doc); return m.group(1) if m else None
def public_layout_checks(site, mode, idx, page, ok):
    """the public website's entry pages (tools/publish/public_entry.py), their metadata, the card and the worker's page addresses"""
    import struct
    sys.path.insert(0, os.path.join(HERE, "..", "publish")); import public_entry as PE
    card = os.path.join(site, *PE.CARD.split("/")); head = open(card, "rb").read(24) if os.path.exists(card) else b""
    ok("link_preview_card_is_1200x630_png", head[:8] == b"\x89PNG\r\n\x1a\n" and struct.unpack(">II", head[16:24]) == (1200, 630), PE.CARD)
    def social(doc, title, desc, url, robots, tag):
        img = f"{SITE_URL}/{PE.CARD}"
        good = (f"<title>{title}</title>" in doc and _meta(doc, "name", "description") == desc and _meta(doc, "name", "robots") == robots and _canon(doc) == url
                and _meta(doc, "property", "og:title") == title and _meta(doc, "property", "og:description") == desc and _meta(doc, "property", "og:url") == url
                and _meta(doc, "property", "og:image") == img and _meta(doc, "property", "og:image:width") == "1200" and _meta(doc, "property", "og:image:height") == "630"
                and bool(_meta(doc, "property", "og:image:alt")) and _meta(doc, "name", "twitter:card") == "summary_large_image" and _meta(doc, "name", "twitter:image") == img
                and _meta(doc, "name", "twitter:title") == title and _meta(doc, "name", "twitter:description") == desc and doc.count('<meta name="robots"') == 1)
        ok(f"{tag}_metadata (title, description, robots {robots}, canonical, Open Graph, Twitter card)", good, f"{_meta(doc, 'name', 'robots')} {_canon(doc)}")
    sw = open(os.path.join(site, "sw.js"), encoding="utf-8").read(); spec = json.loads(re.match(r"self\.CCS_SW = (\{.*?\});\n", sw).group(1))
    every = spec["shell"] + spec["home"]; ok("worker_checks_every_file_it_keeps", set(spec.get("sha") or {}) == set(every) and all(len(v) == 16 for v in spec["sha"].values()), len(spec.get("sha") or {}))
    ok("nes_catalog_not_published (the Windows app's local document only)", not os.path.exists(os.path.join(site, "nes-catalog.html")))
    if mode == "prelaunch":
        social(idx, PE.NAME, PE.DESC_SPLASH, SITE_URL + "/", "index,follow", "splash")
        # the sound control (public_entry.sound_parts) is the one addition to the page: without it, the page is the coming-soon page, byte for byte
        snd = PE.sound_config(site); css, ctl = PE.sound_parts(snd); bare = idx.replace(css, "", 1).replace(ctl, "", 1)
        ok("splash_sound_control_once (its CSS, its button and its script)", idx.count(css) == 1 and idx.count(ctl) == 1 and idx.count("<button") == 1 and idx.count('id="snd"') == 1 and idx.index(ctl) == idx.index("<body>") + 6)
        ok("splash_is_the_page_plus_only_its_sound_control (byte for byte)", bare == PE.splash_html())
        body = bare[bare.index("<body>"):]
        ok("splash_shows_only_its_text", re.sub(r"<[^>]+>", " ", body).split() == "Classic Cart Studio Your Classics. Your Way. Coming February 2027 An independent, open-source project. Not affiliated with or endorsed by any console maker or game publisher.".split()
           and f"<footer>{PE.FOOTER}</footer>" in body and not re.search(r"<(a|button|img|svg|form|input|video|audio|iframe|picture)\b", bare), re.sub(r"<[^>]+>", " ", body)[:200])
        ok("splash_has_no_picture_or_external_file (the site's own font only)", re.findall(r'url\("([^"]+)"\)', idx) == ["brand/fonts/PressStart2P-Regular.ttf"] and not re.search(r'src="', idx) and "manifest" not in idx and "sw.js" not in idx)
        js = ctl[ctl.index("<script>") + 8:ctl.rindex("</script>")]
        ok("splash_sound_button (native type=button, labeled Turn sound on, decorative icon hidden from screen readers, no text)",
           ctl.startswith('<button type="button" id="snd" aria-label="Turn sound on" hidden><svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">') and re.sub(r"<[^>]+>", "", ctl[:ctl.index("<script>")]) == ""
           and '"Turn sound off"' in js and '"Turn sound on"' in js)
        ok("splash_sound_nothing_before_a_press (no audio element, preload, prefetch, embedded sound, storage, cookie or worker)",
           not re.search(r"<(audio|video|link[^>]+(preload|prefetch))\b", idx) and "data:audio" not in idx and not re.search(r"localStorage|sessionStorage|indexedDB|cookie|serviceWorker|caches|preload|autoplay|setInterval", js)
           and js.count("fetch(") == 1 and js.count("new AC(") == 2 and "decodeAudioData" in js)
        ok("splash_sound_loops_the_decoded_song (Web Audio loop, no restart on ended, gain 0.3)", "s.loop=true" in js and "loopStart" not in js and "loopEnd" not in js and ".onended=function(){if(S.s===s)off();}" in js and "gain.value=0.3" in js and "currentTime" not in js)
        mp3 = os.path.join(site, *snd["mp3"].split("/")); aac = os.path.join(site, *snd["aac"].split("/"))
        import hashlib
        ok("splash_sound_files (the HOME theme's AAC as published; the MP3 fallback as recorded)", os.path.isfile(mp3) and os.path.getsize(mp3) == snd["mp3_bytes"] and hashlib.sha256(open(mp3, "rb").read()).hexdigest() == snd["mp3_sha256"]
           and os.path.getsize(aac) == snd["aac_bytes"] and hashlib.sha256(open(aac, "rb").read()).hexdigest() == snd["aac_sha256"] and snd["mp3"].startswith(PE.SOUND_DIR + "/")
           and sorted(os.listdir(os.path.join(site, *PE.SOUND_DIR.split("/")))) == [os.path.basename(mp3)], (snd["mp3"], snd["mp3_bytes"]))
        ok("splash_sound_not_one_of_the_workers_files (its MP3 neither kept nor answered by the worker)", not any(p.startswith(PE.SOUND_DIR + "/") or p.endswith(".mp3") for p in every) and not re.match(r"^(art|brand/help|included)/", snd["mp3"]))
        node = shutil.which("node")
        if node:
            p = os.path.join(site, "_sound_check.js"); open(p, "w", encoding="utf-8").write(js); r = subprocess.run([node, "--check", p], capture_output=True, text=True); os.remove(p)
            ok("splash_sound_script_parses", r.returncode == 0, r.stderr[:200])
        frames = re.search(r"@keyframes soon\{(.*?)\}\}", idx, re.S).group(1) + "}"; steps = re.findall(r"([0-9.]+)%\{([^}]*)\}", frames)
        dur = float(re.search(r"animation:soon ([0-9.]+)s", idx).group(1)); starts = []; dim = False
        for pc, css in steps:
            o = float(re.search(r"opacity:([0-9.]+)", css).group(1))
            if o < 1 and not dim: starts.append(float(pc) / 100 * dur)
            dim = o < 1
        both = sorted(t + k * dur for k in (0, 1) for t in starts); worst = max(sum(1 for x in both if a <= x < a + 1.0) for a in both)
        ok("splash_flicker_at_most_three_dips_in_any_second (the loop's seam included), never dark", 2 <= len(starts) and worst <= 3 and min(float(x) for x in re.findall(r"opacity:([0-9.]+)", frames)) >= 0.4, (len(starts), worst))
        ok("splash_steady_with_reduced_motion", "@media (prefers-reduced-motion:reduce){.soon{animation:none}}" in idx)
        ok("splash_sends_only_installed_apps_and_app_links_on (no worker registration for visitors)", 'location.replace("preview/"' in idx and "(display-mode: standalone)" in idx and "fullscreen" not in idx and "companion|play" in idx and ".register(" not in idx)
        social(page, PE.NAME, PE.DESC_APP, SITE_URL + "/preview/", "noindex,follow", "preview")
        ok("preview_files_at_the_root (base first in the head) and its in-page links kept in it", page.find('<base href="../">') == page.find('<meta charset="utf-8">') + len('<meta charset="utf-8">') and 'id="brandHome" href="preview/"' in page and PE.FRAGMENTS in page)
        ok("worker_pages_prelaunch", spec.get("nav") == {"": "index.html", "index.html": "index.html", "preview/": "preview/index.html", "preview/index.html": "preview/index.html"} and not spec.get("moved") and {"index.html", "preview/index.html"} <= set(spec["shell"]))
        st = open(os.path.join(site, "store", "index.html"), encoding="utf-8").read(); ok("store_back_links_to_the_app", 'href="/"' not in st and st.count('href="/preview/"') >= 3)
        for n in ("app", "source"):   # "Back to the website" goes to the app; the page's own release or source link is unchanged
            h = open(os.path.join(site, n, "index.html"), encoding="utf-8").read()
            ok(f"{n}_back_link_to_the_app", PE.BACK_ROOT not in h and h.count(PE.BACK_PREVIEW) == 1 and "github.com/Rance9/Classic-Cart-Studio" in h)
    else:
        social(idx, PE.NAME, PE.DESC_APP, SITE_URL + "/", "index,follow", "launch_root")
        prev = open(os.path.join(site, "preview", "index.html"), encoding="utf-8").read(); social(prev, PE.NAME, PE.DESC_APP, SITE_URL + "/", "noindex,follow", "launch_preview_copy")
        ok("launch_root_is_the_app_at_the_root (no base, no splash)", "<base " not in idx and 'class="soon"' not in idx and 'id="brandHome" href="./"' in idx)
        ok("worker_pages_launch (preview redirected by the worker, never cached)", spec.get("nav") == {"": "index.html", "index.html": "index.html"} and spec.get("moved") == {"preview/": "", "preview/index.html": "", "preview": ""} and "preview/index.html" not in spec["shell"])
        ok("no_splash_sound_at_the_launch (no splash, no MP3)", not os.path.exists(os.path.join(site, *PE.SOUND_DIR.split("/"))) and 'id="snd"' not in idx)
    rb = open(os.path.join(site, "robots.txt"), encoding="utf-8").read(); ok("robots_allows_crawling (noindex is read, not blocked)", "Disallow" not in rb)
    sm = os.path.join(site, "sitemap.xml"); ok("no_sitemap_or_only_indexable_pages", not os.path.exists(sm) or "/preview" not in open(sm, encoding="utf-8").read())
    for n in ("app", "source", "store"):
        p = os.path.join(site, n, "index.html")
        if os.path.exists(p): ok(f"{n}_page_keeps_noindex", bool(re.search(r'<meta name="robots" content="noindex(?:,[^"]*)?">', open(p, encoding="utf-8").read())))

if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "..", "build", "site"))
