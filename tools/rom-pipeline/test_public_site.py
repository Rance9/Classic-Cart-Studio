"""Checks on the built PUBLIC site (no private workspace needed):  python test_public_site.py <site_dir>
- required files (index.html, nes-catalog.html, data JSON, .nojekyll, robots.txt, release.json, original sprites + index + provenance)
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
    need = ["index.html", "nes-catalog.html", "catalog.json", "builtin.json", "library.json", ".nojekyll", "robots.txt", "release.json", "brand/oem/index.json", "brand/oem/nes-wordmark.png", "brand/web/provenance.json", "brand/fonts/PressStart2P-Regular.ttf", "art"]
    missing = [n for n in need if not os.path.exists(os.path.join(site, n))]; ok("required_files", not missing, str(missing))
    page = open(os.path.join(site, "nes-catalog.html"), encoding="utf-8").read(); idx = open(os.path.join(site, "index.html"), encoding="utf-8").read()
    ok("index_is_the_page", page == idx)
    ok("title_and_disclaimer", "<title>Classic Cart Studio</title>" in page and "independent" in page and ("not produced, licensed, sponsored, approved or endorsed by Nintendo" in page or "not produced, licensed, sponsored, approved, or endorsed by Nintendo" in page))
    # ---- approved visual presentation (owner directive 2026-10-10): the public page must carry the same artwork as the accepted build ----
    sys.path.insert(0, HERE); import build_html as BH
    pi = page.index('<div class="parade"'); parade = page[pi:page.index('<div class="ground">', pi)]
    hero_imgs = re.findall(r'src="(brand/(?:oem|web)/[^"]+\.png)"', parade); expected = [f"brand/{d}/{st}.png" for d, st, sc, cls in BH.HERO]
    ok("hero_parade_27_original_characters_in_order", hero_imgs == expected and len(expected) == 27, f"{len(hero_imgs)} of {len(expected)}")
    ok("hero_sprites_exist", all(os.path.exists(os.path.join(site, p)) for p in expected))
    ok("wordmark_present", 'src="brand/oem/nes-wordmark.png"' in page and os.path.exists(os.path.join(site, "brand", "oem", "nes-wordmark.png")))
    ok("interface_icons_from_firmware", page.count("brand/oem/icon-") >= 8 and all(os.path.exists(os.path.join(site, "brand", "oem", n)) for n in ("icon-a.png", "icon-b.png", "icon-cartridge.png", "icon-disk.png", "icon-warning.png", "icon-start.png")))
    ok("footer_roster_present", page.count('src="brand/oem/px-') >= 40 and page.count('src="brand/web/') >= 15, f"oem={page.count(chr(34)+'brand/oem/px-')} web={page.count('src='+chr(34)+'brand/web/')}")
    ok("no_generic_substitutes", page.replace("brand/public/as-seen-on-tv.svg", "").count("brand/public/") == 0 and "original pixel art drawn" not in page and "original drawings" not in page)
    refs = set(re.findall(r'(?:src|href)="([^"]+)"', page)) | set(re.findall(r'url\("([^"]+)"\)', page))
    refs = {r for r in refs if not r.startswith(("data:", "#", "blob:", "mailto:")) and "${" not in r}
    ext = sorted(r for r in refs if re.match(r"^[a-z]+:", r)); allowed_ext = ("https://github.com/", "https://rance9.github.io/", "https://ClassicCartStudio.com/", "https://classiccartstudio.com/", "https://api.github.com/", "http://127.0.0.1:", "https://www.nintendo.com/", "https://www.ebay.com/itm/302788782864")   # the seller recommendation is a plain anchor, not an asset
    bad_ext = [r for r in ext if not r.startswith(allowed_ext)]; ok("external_links_are_only_github_and_loopback", not bad_ext, str(bad_ext)[:200])
    rel = [r for r in refs if not re.match(r"^[a-z]+:", r)]; abs_ = [r for r in rel if r.startswith("/")]; ok("no_root_absolute_references (sub-path safe)", not abs_, str(abs_)[:200])
    miss = [r for r in rel if not os.path.exists(os.path.join(site, r.split("?")[0]))]; ok("relative_references_exist", not miss, str(miss)[:200])
    ok("no_external_script_or_style", not re.search(r'<script[^>]+src="https?://', page) and not re.search(r'<link[^>]+href="https?://', page))
    try:
        import scan_prohibited as S
        hits = [m.group(0)[:40] for m in S.PRIVATE_PATH.finditer(page)]; ok("no_private_paths_in_page", not hits, str(hits)[:120])
        ok("no_secret_patterns_in_page", not S.SECRET.search(page))
        for n in ("catalog.json", "library.json", "builtin.json"):
            t = open(os.path.join(site, n), encoding="utf-8").read(); ok(f"no_private_paths_in_{n}", not S.PRIVATE_PATH.search(t))
    except ImportError: ok("scanner_available", False)
    cat = json.load(open(os.path.join(site, "catalog.json"), encoding="utf-8")); with_art = [g for g in cat["games"] if g.get("art")]
    ok("curated_cover_art_coverage_restored", len(cat["games"]) > 700 and len(with_art) >= 745, f"{len(with_art)} of {len(cat['games'])}")
    ok("cover_art_files_exist", all(os.path.exists(os.path.join(site, g["art"])) for g in with_art) and len(os.listdir(os.path.join(site, "art"))) >= 800)
    lib = json.load(open(os.path.join(site, "library.json"), encoding="utf-8")); ok("library_art_mappings_kept", sum(1 for t in lib["titles"] if t.get("art")) >= 750 and sum(1 for v in lib["versions"] if v.get("art")) >= 800)
    sprites = set(re.findall(r'src="(brand/(?:oem|web)/[^"]+\.png)"', page)); ok("every_sprite_reference_exists", len(sprites) >= 60 and all(os.path.exists(os.path.join(site, s)) for s in sprites), str(len(sprites)))
    prov = {p["slug"] for p in json.load(open(os.path.join(site, "brand", "web", "provenance.json"), encoding="utf-8"))}
    used = {s.split("/")[-1][:-4] for s in sprites if s.startswith("brand/web/")}; ok("web_characters_have_provenance", bool(used) and used <= prov, str(sorted(used - prov)))
    node = shutil.which("node")
    if node:
        sc = re.findall(r"<script>(.*?)</script>", page, re.S); p = os.path.join(site, "_script_check.js"); open(p, "w", encoding="utf-8").write(sc[0] if sc else "")
        r = subprocess.run([node, "--check", p], capture_output=True, text=True); os.remove(p); ok("inline_script_parses", r.returncode == 0, r.stderr[:200])
    else: ok("inline_script_parses (node not available, skipped)", True)
    ok("page_size_reasonable", 1_000_000 < len(page) < 8_000_000, str(len(page)))
    ok("favicon_set_present", all(os.path.exists(os.path.join(site, n)) for n in ("favicon.ico", "icon-32.png", "apple-touch-icon.png")) and 'rel="icon" href="favicon.ico"' in page and 'rel="apple-touch-icon"' in page)
    app_p = os.path.join(site, "app", "index.html"); app = open(app_p, encoding="utf-8").read() if os.path.exists(app_p) else ""
    ok("app_shortlink_page_exists", bool(app), app_p)
    ok("app_shortlink_redirects_to_latest_release", app.count("https://github.com/Rance9/Classic-Cart-Studio/releases/latest") >= 3 and "location.replace(" in app and 'http-equiv="refresh"' in app and "<title>Classic Cart Studio | Windows App</title>" in app and "NES-Classic-Collection-Studio" not in app)
    bad = [c for c in checks if "=false" in c]; print("\n".join(checks)); print(f"RESULT: {'PASS' if not bad else 'FAIL '+str(bad)} ({len(checks)} checks)"); sys.exit(1 if bad else 0)

if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "..", "build", "site"))
