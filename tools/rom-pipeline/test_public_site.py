"""Checks on the built PUBLIC site (no private workspace needed):  python test_public_site.py <site_dir>
- required files (index.html, nes-catalog.html, data JSON, .nojekyll, robots.txt, release.json, original sprites + index + provenance)
- every src/href/url() reference is relative (works under the /NES-Classic-Collection-Studio/ sub-path and on localhost), exists, and
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
    need = ["index.html", "nes-catalog.html", "catalog.json", "builtin.json", "library.json", ".nojekyll", "robots.txt", "release.json", "brand/public/index.json", "brand/public/provenance.json", "brand/public/byte.png", "brand/fonts/PressStart2P-Regular.ttf"]
    missing = [n for n in need if not os.path.exists(os.path.join(site, n))]; ok("required_files", not missing, str(missing))
    page = open(os.path.join(site, "nes-catalog.html"), encoding="utf-8").read(); idx = open(os.path.join(site, "index.html"), encoding="utf-8").read()
    ok("index_is_the_page", page == idx)
    ok("title_and_disclaimer", "<title>NES Classic Collection Studio</title>" in page and "independent project" in page and "not produced, licensed, sponsored, approved or endorsed by Nintendo" in page)
    ok("no_private_assets", "brand/oem/" not in page and "brand/web/" not in page and 'src="art/' not in page and "nes-wordmark" not in page)
    refs = set(re.findall(r'(?:src|href)="([^"]+)"', page)) | set(re.findall(r'url\("([^"]+)"\)', page))
    refs = {r for r in refs if not r.startswith(("data:", "#", "blob:", "mailto:")) and "${" not in r}
    ext = sorted(r for r in refs if re.match(r"^[a-z]+:", r)); allowed_ext = ("https://github.com/", "https://rance9.github.io/", "https://api.github.com/", "http://127.0.0.1:", "https://www.nintendo.com/")
    bad_ext = [r for r in ext if not r.startswith(allowed_ext)]; ok("external_links_are_only_github_and_loopback", not bad_ext, str(bad_ext)[:200])
    rel = [r for r in refs if not re.match(r"^[a-z]+:", r)]; abs_ = [r for r in rel if r.startswith("/")]; ok("no_root_absolute_references (sub-path safe)", not abs_, str(abs_)[:200])
    miss = [r for r in rel if not os.path.exists(os.path.join(site, r.split("?")[0]))]; ok("relative_references_exist", not miss, str(miss)[:200])
    ok("no_external_script_or_style", not re.search(r'<script[^>]+src="https?://', page) and not re.search(r'<link[^>]+href="https?://', page))
    try:
        import scan_prohibited as S
        hits = [m.group(0)[:40] for m in S.PRIVATE_PATH.finditer(page)]; ok("no_private_paths_in_page", not hits, str(hits)[:120])
        ok("no_secret_patterns_in_page", not S.SECRET.search(page))
        for n in ("catalog.json", "library.json", "builtin.json"):
            t = open(os.path.join(site, n), encoding="utf-8").read(); ok(f"no_private_paths_in_{n}", not S.PRIVATE_PATH.search(t) and '"art"' not in t)
    except ImportError: ok("scanner_available", False)
    cat = json.load(open(os.path.join(site, "catalog.json"), encoding="utf-8")); ok("catalog_has_games_without_art", len(cat["games"]) > 700 and all(not g.get("art") for g in cat["games"]))
    prov = json.load(open(os.path.join(site, "brand", "public", "provenance.json"), encoding="utf-8")); ok("sprites_are_original", all("Original pixel art" in p["origin"] for p in prov) and len(prov) >= 20)
    sprites = set(re.findall(r'src="(brand/public/[^"]+\.png)"', page)); ok("page_uses_public_sprites", len(sprites) >= 20 and all(os.path.exists(os.path.join(site, s)) for s in sprites), str(len(sprites)))
    node = shutil.which("node")
    if node:
        sc = re.findall(r"<script>(.*?)</script>", page, re.S); p = os.path.join(site, "_script_check.js"); open(p, "w", encoding="utf-8").write(sc[0] if sc else "")
        r = subprocess.run([node, "--check", p], capture_output=True, text=True); os.remove(p); ok("inline_script_parses", r.returncode == 0, r.stderr[:200])
    else: ok("inline_script_parses (node not available, skipped)", True)
    ok("page_size_reasonable", 1_000_000 < len(page) < 8_000_000, str(len(page)))
    bad = [c for c in checks if "=false" in c]; print("\n".join(checks)); print(f"RESULT: {'PASS' if not bad else 'FAIL '+str(bad)} ({len(checks)} checks)"); sys.exit(1 if bad else 0)

if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "..", "build", "site"))
