"""The public website's browse-only Community Mods page (/community/), made at build time from tools/community/ccs-community-catalog.json
(the same file CCS Console Workbench carries). Copyright (C) 2026 Rance9, GPL-3.0-or-later.

  write(out_dir, public)          build_site_ci.py --public prelaunch|launch: <out>/community/index.html
  render(catalog: dict, public)   the page's text (tests)

It lists the catalog's entries in their sections' fixed order (Menus, Utilities, Themes, Emulators) and each section's own order, with each
entry's version, author and credits, license, what it does, where it applies, and links to the author's project, support, license and
source pages and to its permitted pictures. It has no install control: installation happens in CCS Console Workbench on Windows. With no
entry listed (this version) it says so plainly. Every text is escaped; every address must be https; the catalog is refused (the build
stops) when it is not a ccs-community-catalog/1 catalog. Pictures stay on their authors' sites as links, so the page itself loads nothing
from another site. Not indexed, like the site's other secondary pages."""
from __future__ import annotations
import html, json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
CATALOG = os.path.join(REPO, "tools", "community", "ccs-community-catalog.json")
SECTIONS = ["menus", "utilities", "themes", "emulators"]
TITLES = {"menus": "Menus", "utilities": "Utilities", "themes": "Themes", "emulators": "Emulators"}
SCOPE = "Community add-ons are designed for Original Menu — Nintendo's own menu. Their features and settings may not work in CCS HOME or other menus. Each card explains where it applies."
EMPTY_TITLE = "No community mods are listed yet"
EMPTY_TEXT = "A mod appears here once its author agrees to have it listed and its exact version has been reviewed. None is listed yet, so there is nothing to show here."
WINDOWS = "Installation happens in CCS Console Workbench on Windows, which downloads each mod's exact approved file from its author's release and backs up your console first. This page only shows what is listed."

class CatalogError(Exception): pass

def _https(u) -> str:
    if not isinstance(u, str) or not re.match(r"^https://[A-Za-z0-9.-]+\.[A-Za-z]{2,}(/[^\s\"'<>\\]*)?$", u) or "@" in u.split("/")[2]: raise CatalogError(f"not an https address: {u!r}")
    return u

def _text(d: dict, k: str, need=True) -> str | None:
    v = d.get(k)
    if v is None and not need: return None
    if not isinstance(v, str) or not v.strip() or any(ord(c) < 32 for c in v): raise CatalogError(f"{d.get('id', 'an entry')}: {k} is missing or not plain text")
    return v

def check(cat: dict) -> list[dict]:
    """the listed entries, after the checks the page relies on (the engine checks every field; this refuses what the page cannot show)"""
    if cat.get("schema") != "ccs-community-catalog/1": raise CatalogError("not a ccs-community-catalog/1 catalog")
    if cat.get("sections") != SECTIONS: raise CatalogError("the sections must be menus, utilities, themes and emulators, in that order")
    out = []
    for e in cat.get("entries", []):
        if not isinstance(e, dict) or e.get("section") not in SECTIONS or not isinstance(e.get("order"), int): raise CatalogError("an entry has no section or place")
        for k in ("id", "name", "version", "summary", "applies"): _text(e, k)
        a, l = e.get("author") or {}, e.get("license") or {}
        _text(a, "name"); _https(a.get("project_url")); a.get("support_url") and _https(a["support_url"]); _text(l, "name"); _https(l.get("url")); _https(l.get("source_url"))
        for s in e.get("screenshots") or []: _https(s.get("url")); _text(s, "caption")
        out.append(e)
    return out

def render(cat: dict, public: str) -> str:
    entries = check(cat); esc = lambda s: html.escape(s or "", quote=True)
    back = "/preview/" if public == "prelaunch" else "/"
    body = []
    if not entries:
        body.append(f'<section class="card empty"><h2>{esc(EMPTY_TITLE)}</h2><p>{esc(EMPTY_TEXT)}</p></section>')
    else:
        body.append(f'<p class="scope">{esc(SCOPE)}</p>')
        for sec in SECTIONS:
            rows = sorted((e for e in entries if e["section"] == sec), key=lambda e: e["order"])
            if not rows: continue
            body.append(f'<h2>{esc(TITLES[sec])}</h2><div class="grid">')
            for e in rows:
                a, l = e["author"], e["license"]
                links = [(a["project_url"], "Project page"), (a.get("support_url"), "Support"), (l["url"], "License"), (l["source_url"], "Source")]
                links += [(s["url"], "Picture: " + s["caption"]) for s in e.get("screenshots") or []]
                credits = a.get("credits") or []; alpha = ' <span class="alpha">Alpha</span>' if e.get("alpha") else ""
                body.append('<article class="card">'
                            f'<h3>{esc(e["name"])}{alpha}</h3>'
                            f'<p class="meta">Version {esc(e["version"])} &middot; By {esc(a["name"])} &middot; {esc(l["name"])}</p>'
                            f'<p>{esc(e["summary"])}</p><p class="applies">{esc(e["applies"])}</p>'
                            + (f'<p class="meta">Also credited: {esc(", ".join(credits))}</p>' if credits else "")
                            + "".join(f'<p class="warn">Its author says: {esc(w)}</p>' for w in e.get("warnings") or [])
                            + '<p class="links">' + " &middot; ".join(f'<a href="{esc(u)}" target="_blank" rel="noopener">{esc(t)}</a>' for u, t in links if u) + "</p></article>")
            body.append("</div>")
    return f"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Classic Cart Studio | Community Mods</title>
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="robots" content="noindex">
<meta name="description" content="Community mods for the NES and SNES Classic, listed with their authors' permission. Installation happens in CCS Console Workbench on Windows.">
<link rel="icon" href="../favicon.ico">
<style>
html{{background:#131317;color:#e8e8ee;font:15px/1.5 system-ui,Segoe UI,Arial,sans-serif}}
body{{margin:0;padding:24px 16px 48px;box-sizing:border-box}}
main{{max-width:980px;margin:0 auto}}
header{{display:flex;align-items:center;gap:14px;margin-bottom:8px}}
h1{{font-size:24px;margin:0}} h2{{font-size:18px;margin:26px 0 10px}} h3{{font-size:16.5px;margin:0 0 6px}}
p{{margin:6px 0;color:#c8c8d4}} .meta{{color:#9a9aa8;font-size:13px}} .applies{{font-weight:600;color:#e8e8ee}}
.card{{background:#1c1c22;border:1px solid #2e2e38;border-radius:10px;padding:18px}}
.grid{{display:grid;grid-template-columns:repeat(auto-fill,minmax(280px,1fr));gap:14px}}
.scope,.note{{background:#1c1c22;border-left:3px solid #4a8cff;border-radius:6px;padding:10px 14px}}
.warn{{border-left:3px solid #e0a020;padding-left:10px}} .alpha{{font-size:12px;border:1px solid #e0a020;color:#e0a020;border-radius:10px;padding:1px 8px;margin-left:6px;vertical-align:middle}}
a{{color:#ff5a63;font-weight:600}} .links{{margin-top:10px}}
</style>
</head>
<body>
<main>
<header><img src="../icon-192.png" width="48" height="48" alt="" style="image-rendering:pixelated"><h1>Community Mods</h1></header>
<p>Mods other authors make for the NES Classic and SNES Classic, listed only with their authors' agreement, each at one exact version.</p>
<p class="note">{esc(WINDOWS)}</p>
{chr(10).join(body)}
<p style="margin-top:28px"><a href="{back}">Back to Classic Cart Studio</a> &middot; <a href="../app/">Classic Cart Studio for Windows</a></p>
</main>
</body>
</html>
"""

def write(out: str, public: str) -> str:
    cat = json.load(open(CATALOG, encoding="utf-8"))
    d = os.path.join(out, "community"); os.makedirs(d, exist_ok=True)
    p = os.path.join(d, "index.html"); open(p, "w", encoding="utf-8", newline="\n").write(render(cat, public))
    print(f"community mods page: {len(cat.get('entries', []))} listed entries")
    return p

if __name__ == "__main__":
    print(render(json.load(open(CATALOG, encoding="utf-8")), sys.argv[1] if len(sys.argv) > 1 else "prelaunch"))
