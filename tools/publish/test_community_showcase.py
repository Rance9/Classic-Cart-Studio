"""Checks of the website's Community Mods page (community_showcase.py). Copyright (C) 2026 Rance9, GPL-3.0-or-later.   python test_community_showcase.py
The catalog this repository carries, and small fixture catalogs made here (never published): the honest empty state, the sections' fixed
order and each section's own order, credits/license/links/warnings shown, every text escaped, nothing loaded from another site, no install
control, and a catalog the page cannot show refused (the build stops). Exit 0 = PASS."""
from __future__ import annotations
import copy, html, json, os, re, sys
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import community_showcase as CS   # noqa: E402

passed = failed = 0
def check(ok: bool, what: str):
    global passed, failed
    print(("ok   " if ok else "FAIL ") + what); passed += ok; failed += (not ok)

def entry(i: str, name: str, section: str, order: int, **kw) -> dict:
    e = {"schema": "ccs-community-record/1", "id": i, "name": name, "section": section, "order": order, "version": "1.0.0", "alpha": False, "summary": "A fixture made by this test.", "applies": "For: Original Menu.",
         "author": {"name": "Fixture Author", "credits": ["Fixture Upstream"], "project_url": f"https://mods.example.org/{i}", "support_url": f"https://mods.example.org/{i}/support"},
         "license": {"name": "GPL-3.0-or-later", "url": f"https://mods.example.org/{i}/LICENSE", "source_url": f"https://mods.example.org/{i}/source"}, "screenshots": []}
    e.update(kw); return e

def catalog(*entries) -> dict: return {"schema": "ccs-community-catalog/1", "generation": 1, "issued": "2026-11-20", "sections": ["menus", "utilities", "themes", "emulators"], "entries": list(entries)}

def main() -> int:
    shipped = json.load(open(CS.CATALOG, encoding="utf-8"))
    pre, launch = CS.render(shipped, "prelaunch"), CS.render(shipped, "launch")
    check(shipped["entries"] == [] and CS.EMPTY_TITLE in pre and "<article" not in pre and CS.SCOPE not in pre, "the catalog this repository carries lists nothing, and the page says so plainly, with no fabricated entry")
    check('<meta name="robots" content="noindex">' in pre and 'href="/preview/"' in pre and 'href="/preview/"' not in launch and 'href="/"' in launch, "not indexed; its way back follows the site's entry (the app at /preview/ before the launch, at / after)")
    for page in (pre, launch):
        check("<form" not in page and "<button" not in page and "<script" not in page.lower() and not re.search(r">\s*Install\b", page) and html.escape(CS.WINDOWS) in page, "browse only: no form, button, script or install control; installation is said to happen in CCS Console Workbench on Windows")
    cat = catalog(entry("wifi", "Wi-Fi", "utilities", 30), entry("remap", "Gamepad Remapping Tool", "utilities", 20), entry("opt", "Options Menu", "utilities", 10),
                  entry("menu", "Fixture Menu", "menus", 5, alpha=True, warnings=["May stop after a game closes."]), entry("ra", "RetroArch <b>K</b> & \"friends\"", "emulators", 1,
                  screenshots=[{"url": "https://mods.example.org/ra/shot.png", "caption": "Its menu"}]))
    p = CS.render(cat, "prelaunch")
    heads = re.findall(r"<h2>([^<]+)</h2>", p); names = re.findall(r"<h3>([^<]+)", p)
    check(heads == ["Menus", "Utilities", "Emulators"] and names[1:4] == ["Options Menu", "Gamepad Remapping Tool", "Wi-Fi"] and CS.SCOPE.replace("'", "&#x27;") in p,
          f"sections in their fixed order (an empty one left out), each in its own order, never alphabetically, under the scope statement ({heads}; {names})")
    check("RetroArch &lt;b&gt;K&lt;/b&gt; &amp; &quot;friends&quot;" in p and "<b>K</b>" not in p, "every text is escaped")
    check('<span class="alpha">Alpha</span>' in p and "Its author says: May stop after a game closes." in p and "Also credited: Fixture Upstream" in p and "By Fixture Author" in p and "GPL-3.0-or-later" in p,
          "Alpha, the author's own warnings, the author, credits and license are shown")
    check('href="https://mods.example.org/ra/shot.png"' in p and not re.search(r'\ssrc="(https?:)?//', p) and p.count('target="_blank" rel="noopener"') == 4 * 5 + 1, "links open in a new tab without opener; a permitted picture stays on its author's site as a link (the page loads nothing from another site)")
    bad = [("an http address", lambda c: c["entries"][0]["author"].__setitem__("project_url", "http://mods.example.org/x")),
           ("an address with a user name", lambda c: c["entries"][0]["license"].__setitem__("url", "https://u:p@mods.example.org/x")),
           ("the sections in another order", lambda c: c.__setitem__("sections", ["utilities", "menus", "themes", "emulators"])),
           ("another schema", lambda c: c.__setitem__("schema", "something/1")),
           ("a control character", lambda c: c["entries"][0].__setitem__("summary", "line" + chr(1))),
           ("an entry without a place", lambda c: c["entries"][0].__setitem__("order", "first"))]
    for what, change in bad:
        c = copy.deepcopy(cat); change(c)
        try: CS.render(c, "launch"); check(False, f"a catalog with {what} is refused")
        except CS.CatalogError as ex: check(True, f"a catalog with {what} is refused, so the build stops ({ex})")
    print(f"RESULT {passed}/{passed + failed}")
    return 0 if failed == 0 else 1

if __name__ == "__main__": sys.exit(main())
