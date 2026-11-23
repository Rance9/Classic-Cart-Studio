"""The CCS Collection Interface kit on the first-party downloads page (/app/), for the release build (build_site_ci.py). Copyright (C) 2026
Rance9, GPL-3.0-or-later (this file; the kit itself is MIT, tools/collection-interface-kit/LICENSE).

The kit is built by its own build script (tools/collection-interface-kit/build_kit.py, run as a separate program: nothing of the kit is
imported here and nothing of this program goes into the kit) into the site's downloads/ folder, and the downloads page gets one entry for
it, with its version, size and SHA-256 from the kit's manifest. It is not published yet: the release build does this only when
CCS_PUBLISH_COLLECTION_KIT=1 is set; without it the site is built exactly as before."""
from __future__ import annotations
import html, json, os, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
KIT = os.path.join(REPO, "tools", "collection-interface-kit")
SWITCH = "CCS_PUBLISH_COLLECTION_KIT"
ANCHOR = '</div>\n<p class="fine">'   # the end of the downloads page's cards (tools/publish/site-static/app/index.html)

def build(out_dir: str) -> dict:
    """the kit's archive and manifest in out_dir, made by the kit's own build script; its manifest"""
    r = subprocess.run([sys.executable, os.path.join(KIT, "build_kit.py"), "--out", out_dir], capture_output=True, text=True)
    if r.returncode != 0: raise SystemExit("the collection interface kit did not build: " + (r.stderr or r.stdout)[-2000:])
    v = open(os.path.join(KIT, "VERSION"), encoding="utf-8").read().strip()
    return json.load(open(os.path.join(out_dir, f"ccs-collection-interface-kit-{v}.json"), encoding="utf-8"))

def kb(n: int) -> str: return f"{n / 1000:.0f} KB" if n < 1_000_000 else f"{n / 1_000_000:.1f} MB"   # decimal units

def entry_html(man: dict) -> str:
    """the downloads page's entry for the kit (links relative to /app/)"""
    f = html.escape(man["file"]); m = html.escape(f"{man['file'][:-4]}.json")
    return ('<article><h2>CCS Collection Interface kit</h2><p>For people who make console menus: read the collections Classic Cart Studio installs. '
            'The guide, the JSON Schema, examples, a reference reader and a conformance checker, under the MIT License.</p>'
            f'<p class="fine">Version {html.escape(man["version"])} · interface v{int(man["interface"])} · ZIP, {kb(int(man["size"]))}<br>SHA-256 {html.escape(man["sha256"])}</p>\n'
            f'<a class="button" href="../downloads/{f}">Download the kit</a>\n<a class="secondary fine" href="../downloads/{m}">Kit manifest</a></article>\n')

def apply(site: str, env: dict | None = None) -> dict | None:
    """with the switch on: the kit in site/downloads/ and its entry on site/app/index.html (before the page's closing notes); otherwise
    nothing. Its manifest, or None"""
    if (env if env is not None else os.environ).get(SWITCH) != "1": return None
    page = os.path.join(site, "app", "index.html"); t = open(page, encoding="utf-8").read()
    if t.count(ANCHOR) != 1: raise SystemExit("the downloads page does not have the one place for the kit's entry")
    man = build(os.path.join(site, "downloads"))
    open(page, "w", encoding="utf-8", newline="\n").write(t.replace(ANCHOR, entry_html(man) + ANCHOR))
    return man
