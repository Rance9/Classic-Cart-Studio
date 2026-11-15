r"""The public website's entry pages (GitHub Pages only; the Windows app's own copy of the site never uses this). build_site_ci.py calls
apply(out, mode) for `--public prelaunch` or `--public launch`, after the page and the static pages are in <out> and before the service
worker's file list is made, so the entry pages and the worker's inventory always belong together.

  prelaunch  /            a coming-soon page: the wordmark, YOUR CLASSICS. YOUR WAY. and COMING FEBRUARY 2027 (it may wrap on a narrow screen), drawn with the site's own pixel font
                          and CSS only (no picture, no link, no button); the only page offered to search engines. A tiny script sends an
                          installed web app (launched at its start address, /) and the app's own working links (?companion=, ?play=) on
                          to the app at /preview/, with their query and fragment; an ordinary browser stays on the page. It never registers
                          a service worker; it only asks an existing registration to look for an update.
             /preview/    the whole site exactly as before (its files stay at the root: <base href="../">), not indexed, its own canonical
  launch     /            the whole site, indexed, canonical /
             /preview/    the same site (a transition copy, not indexed, canonical /) until the host's permanent redirect to / is on
Both: /nes-catalog.html is not published (the generated page is the Windows app's local document; the public copy was only a duplicate),
the link-preview card (the wordmark alone, 1200 x 630) and each page's title, description and Open Graph / Twitter data.
Part of Classic Cart Studio: GNU GPL v3 or later, like the rest of this repository."""
from __future__ import annotations
import os, random, re
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.dirname(os.path.dirname(HERE))
SITE = "https://classiccartstudio.com"
CARD = "brand/social/classic-cart-studio-1200x630.png"; CARD_W, CARD_H = 1200, 630
NAME = "Classic Cart Studio"
DESC_SPLASH = "Your classics. Your way. Coming soon."
DESC_APP = "A free, open-source app for organizing and playing your NES collection in a browser, on a phone or on Windows."
FOOTER = "An independent, open-source project. Not affiliated with or endorsed by any console maker or game publisher."
FONT = os.path.join(REPO, "site-data", "brand", "fonts", "PressStart2P-Regular.ttf")
WHITE, RED, CYAN = "#f5f6fc", "#ff344b", "#7ff8ff"   # the site's wordmark colors (studio.css .wm1/.wm2) and the neon
# COMING SOON's light: steady most of the time; dips (start, end) in seconds of a 9 s loop, at most three in any one second, the loop's
# seam included (flashes_ok); never fully dark
CYCLE = 9.0
DIMS = [(2.80, 2.86), (2.94, 3.01), (5.40, 5.45), (5.52, 5.58), (5.66, 5.71), (7.60, 8.10)]

def flashes_ok(dims=DIMS, cycle=CYCLE, limit=3) -> bool:
    """at most `limit` dips start in any one-second window, over two loops (the seam between them included)"""
    starts = sorted(s + k * cycle for k in (0, 1, 2) for s, _ in dims)
    return all(sum(1 for t in starts if a <= t < a + 1.0) <= limit for a in [x - 1.0 + 1e-9 for x in starts] + starts)

def _keyframes() -> str:
    lit = f"opacity:1;text-shadow:0 0 .12em #bffcff,0 0 .35em {CYAN},0 0 .8em #27c8ff,0 0 1.6em rgba(20,150,255,.55)"
    dim = "opacity:.5;text-shadow:0 0 .1em rgba(127,248,255,.35)"
    pct = lambda t: f"{100 * t / CYCLE:.3f}%"; steps = ["0%{" + lit + "}"]
    for s, e in DIMS:
        edge = 0.02 if e - s < 0.2 else 0.08   # quick dips switch fast; the long one fades
        steps += [pct(s) + "{" + lit + "}", pct(s + edge) + "{" + dim + "}", pct(e - edge) + "{" + dim + "}", pct(e) + "{" + lit + "}"]
    return "@keyframes soon{" + "".join(steps) + "100%{" + lit + "}}"

def _stars(seed=1984, n=46) -> str:
    rnd = random.Random(seed); cols = ("rgba(190,210,255,.55)", "rgba(255,255,255,.45)", "rgba(150,190,255,.4)", "rgba(255,220,240,.35)")
    return ",".join(f"{rnd.uniform(1, 99):.1f}vw {rnd.uniform(1, 97):.1f}vh 0 {rnd.choice((0, 0, 0, 1))}px {rnd.choice(cols)}" for _ in range(n))

def splash_html() -> str:
    assert flashes_ok(), "COMING SOON may flash more than three times in a second"
    meta = _meta(NAME, DESC_SPLASH, SITE + "/", "index,follow")
    return f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
<script>/* an installed web app (started at this address) and the app's own working links go on to the app; an ordinary browser stays here */
(function(){{try{{var q=location.search||"",h=location.hash||"",m=window.matchMedia,app=navigator.standalone===true||!!(m&&(m("(display-mode: standalone)").matches||m("(display-mode: minimal-ui)").matches||m("(display-mode: window-controls-overlay)").matches));
if((app||/[?&](companion|play)=/.test(q))&&!/[?&]ccs-fresh=/.test(q)){{var k="ccs-preview-hop",t=+(sessionStorage.getItem(k)||0),n=Date.now(),
nv=performance.getEntriesByType&&performance.getEntriesByType("navigation")[0],back=(nv?nv.redirectCount>0:true)&&n-t<10000;
/* back: this page was reached again through a redirect right after sending the app on (a permanent /preview/ -> / redirect meeting a
   stale copy of this page): ask once for a fresh copy instead of going round; a page reached that way never sends the app on again */
if(!back){{sessionStorage.setItem(k,String(n));location.replace("preview/"+q+h);return;}}
location.replace("./?ccs-fresh="+n+(q?"&"+q.slice(1):"")+h);return;}}
if(navigator.serviceWorker&&navigator.serviceWorker.getRegistration)navigator.serviceWorker.getRegistration().then(function(r){{if(r)r.update();}}).catch(function(){{}});}}catch(e){{}}}})();
</script>
<title>{NAME}</title>{meta}
<meta name="color-scheme" content="dark"><meta name="theme-color" content="#05060d">
<link rel="icon" href="favicon.ico" sizes="16x16 32x32 48x48"><link rel="icon" type="image/png" sizes="32x32" href="icon-32.png">
<style>
@font-face{{font-family:"Press Start 2P";src:url("brand/fonts/PressStart2P-Regular.ttf") format("truetype");font-display:block}}
:root{{--u:max(12px,min(calc((100vw - 48px) / 12.5),8.4vh,56px))}}
*{{box-sizing:border-box}}
html{{height:100%;background:#03040a}}
body{{margin:0;min-height:100%;min-height:100svh;min-height:100dvh;display:flex;flex-direction:column;color:#fff;overflow-x:hidden;
 background:radial-gradient(120% 90% at 50% 42%,#101a3e 0%,#0a1028 34%,#05070f 72%,#020308 100%)}}
body::before{{content:"";position:fixed;left:0;top:0;width:2px;height:2px;box-shadow:{_stars()};pointer-events:none}}
body::after{{content:"";position:fixed;inset:0;pointer-events:none;background:repeating-linear-gradient(to bottom,rgba(255,255,255,.035) 0,rgba(255,255,255,.035) 1px,transparent 1px,transparent 3px),radial-gradient(ellipse at 50% 45%,transparent 52%,rgba(0,0,0,.55) 100%)}}
main{{flex:1 0 auto;display:flex;align-items:center;justify-content:center;padding:max(20px,env(safe-area-inset-top)) max(16px,env(safe-area-inset-right)) 12px max(16px,env(safe-area-inset-left))}}
.c{{display:flex;flex-direction:column;align-items:center;text-align:center;font-family:"Press Start 2P",monospace;text-transform:uppercase;line-height:1;position:relative;z-index:1}}
h1{{margin:0;font-weight:400;display:flex;flex-direction:column;align-items:center;gap:calc(var(--u) * .5625)}}
.w1{{font-size:var(--u);color:{WHITE}}}.w2{{font-size:calc(var(--u) * 1.25);color:{RED}}}
.tag{{margin:calc(var(--u) * 1.05) 0 0;font-size:max(8px,min(calc(var(--u) * .4),17px));color:#fff;letter-spacing:.04em}}
.soon{{margin:calc(var(--u) * 1.5) 0 0;font-size:max(11px,calc(var(--u) * .7));line-height:1.35;text-wrap:balance;color:{CYAN};animation:soon {CYCLE:g}s linear infinite}}
{_keyframes()}
@media (prefers-reduced-motion:reduce){{.soon{{animation:none}}}}
footer{{position:relative;z-index:1;flex:none;padding:10px max(16px,env(safe-area-inset-right)) max(14px,env(safe-area-inset-bottom)) max(16px,env(safe-area-inset-left));text-align:center;
 font:400 13px/1.45 system-ui,-apple-system,"Segoe UI",Roboto,"Helvetica Neue",Arial,sans-serif;color:#b8bdcc}}
@media (max-height:420px){{footer{{font-size:12px;padding-top:6px}}main{{padding-top:max(10px,env(safe-area-inset-top))}}}}
</style></head>
<body><main><div class="c"><h1><span class="w1">Classic Cart</span><span class="w2">Studio</span></h1><p class="tag">Your Classics. Your Way.</p><p class="soon">Coming February 2027</p></div></main>
<footer>{FOOTER}</footer></body></html>
"""

def _meta(title: str, desc: str, url: str, robots: str) -> str:
    img = f"{SITE}/{CARD}"; alt = f"{NAME} wordmark"
    return (f'<meta name="description" content="{desc}"><meta name="robots" content="{robots}"><link rel="canonical" href="{url}">'
            f'<meta property="og:type" content="website"><meta property="og:site_name" content="{NAME}"><meta property="og:title" content="{title}">'
            f'<meta property="og:description" content="{desc}"><meta property="og:url" content="{url}"><meta property="og:image" content="{img}">'
            f'<meta property="og:image:type" content="image/png"><meta property="og:image:width" content="{CARD_W}"><meta property="og:image:height" content="{CARD_H}">'
            f'<meta property="og:image:alt" content="{alt}"><meta name="twitter:card" content="summary_large_image"><meta name="twitter:title" content="{title}">'
            f'<meta name="twitter:description" content="{desc}"><meta name="twitter:image" content="{img}"><meta name="twitter:image:alt" content="{alt}">')

# the generated page's public head (build_html.py, PUBLIC): replaced by each entry's own metadata
HEAD_RE = re.compile(r'<title>Classic Cart Studio</title><meta name="description" content="[^"]*"><meta name="robots" content="index,follow"><meta property="og:title" content="[^"]*"><meta property="og:description" content="[^"]*">')
# a link to a place in this page (#id, #) stays in this page under the base: the browser would otherwise resolve it against the base (/)
FRAGMENTS = ('<script>/* public /preview/: the page\'s files are the site\'s own at its root (the base above); links to a place in this page stay here */'
             'window.addEventListener("click",function(e){var a=e.target&&e.target.closest&&e.target.closest(\'a[href^="#"]\');'
             'if(!a||e.button>0||e.metaKey||e.ctrlKey||e.shiftKey||e.altKey)return;e.preventDefault();var id=decodeURIComponent(a.getAttribute("href").slice(1));if(!id)return;'
             'var t=document.getElementById(id);if(!t)return;history.replaceState(history.state,"",location.href.split("#")[0]+"#"+encodeURIComponent(id));'
             't.scrollIntoView();if(!t.hasAttribute("tabindex"))t.setAttribute("tabindex","-1");t.focus({preventScroll:true});},true);</script>')

def app_html(page: str, *, url: str, robots: str, preview: bool, home: str | None) -> str:
    """the generated page as one public entry: its metadata; under /preview/ the base (files at the root) and the in-page link guard;
    home: where its Classic Cart Studio wordmark (the Home link) points, relative to the base"""
    assert len(HEAD_RE.findall(page)) == 1, "the generated page's public head is not the expected one"
    out = HEAD_RE.sub(lambda m: f"<title>{NAME}</title>" + _meta(NAME, DESC_APP, url, robots), page)
    if preview:
        head = '<meta charset="utf-8">'; assert 0 <= out.find(head) < 200, "the document's own head is not where expected"   # (the pop-out window's template in the script has its own)
        out = out.replace(head, head + '<base href="../">' + FRAGMENTS, 1)
    if home is not None:
        a = 'id="brandHome" href="./"'; assert out.count(a) == 1; out = out.replace(a, f'id="brandHome" href="{home}"', 1)
    return out

def social_card(path: str):
    """the link-preview card: the wordmark alone (CLASSIC CART over STUDIO, the site's colors) on the dark background, 1200 x 630; drawn
    from the font's own 8 px grid and enlarged by whole pixels, so every letter is the font's exact pixels"""
    from PIL import Image, ImageDraw, ImageFont
    f = ImageFont.truetype(FONT, 8)
    def line(text, color, k):
        m = Image.new("L", (8 * len(text) + 8, 16), 0); d = ImageDraw.Draw(m); d.fontmode = "1"; d.text((0, 0), text, font=f, fill=255)
        m = m.crop(m.getbbox()); m = m.resize((m.width * k, m.height * k), Image.NEAREST)
        tile = Image.new("RGBA", m.size, color); tile.putalpha(m); return tile
    hexc = lambda h: tuple(int(h[i:i + 2], 16) for i in (1, 3, 5)) + (255,)
    a, b = line("CLASSIC CART", hexc(WHITE), 8), line("STUDIO", hexc(RED), 10)
    img = Image.new("RGBA", (CARD_W, CARD_H), (5, 7, 16, 255)); gap = 36; h = a.height + gap + b.height; y = (CARD_H - h) // 2
    img.alpha_composite(a, ((CARD_W - a.width) // 2, y)); img.alpha_composite(b, ((CARD_W - b.width) // 2, y + a.height + gap))
    os.makedirs(os.path.dirname(path), exist_ok=True); img.convert("RGB").save(path, "PNG", optimize=True)

def apply(out: str, mode: str) -> dict:
    """write the public entry pages for `mode` into <out>; returns what the service worker needs: nav {address: cached file}, moved
    {address: address it redirects to, never cached} and drop (files it does not keep)"""
    if mode not in ("prelaunch", "launch"): raise SystemExit(f"unknown public mode {mode!r} (prelaunch or launch)")
    gen = os.path.join(out, "nes-catalog.html"); page = open(gen, encoding="utf-8").read(); os.remove(gen)   # the Windows app's local document, not a public address
    os.makedirs(os.path.join(out, "preview"), exist_ok=True)
    if mode == "prelaunch":
        open(os.path.join(out, "index.html"), "w", encoding="utf-8", newline="\n").write(splash_html())
        open(os.path.join(out, "preview", "index.html"), "w", encoding="utf-8", newline="\n").write(app_html(page, url=SITE + "/preview/", robots="noindex,follow", preview=True, home="preview/"))
        st = os.path.join(out, "store", "index.html")   # the store's Back links go to the app (during the preview, at /preview/)
        if os.path.isfile(st):
            s = open(st, encoding="utf-8").read(); n = s.count('href="/"'); open(st, "w", encoding="utf-8", newline="\n").write(s.replace('href="/"', 'href="/preview/"'))
            print("store page:", n, "Back links to /preview/")
    else:
        open(os.path.join(out, "index.html"), "w", encoding="utf-8", newline="\n").write(app_html(page, url=SITE + "/", robots="index,follow", preview=False, home=None))
        open(os.path.join(out, "preview", "index.html"), "w", encoding="utf-8", newline="\n").write(app_html(page, url=SITE + "/", robots="noindex,follow", preview=True, home=None))
    social_card(os.path.join(out, *CARD.split("/")))
    print(f"public entry pages: {mode} (index.html, preview/index.html, {CARD}); nes-catalog.html not published")
    if mode == "prelaunch":
        return {"nav": {"": "index.html", "index.html": "index.html", "preview/": "preview/index.html", "preview/index.html": "preview/index.html"}, "moved": {}, "drop": []}
    # launch: the app is the root; preview/ (the host's permanent redirect to /, and until then the transition copy for browsers without
    # this worker) is answered by the worker with the same redirect, never from a cache, so a cached preview can never show a page
    # without its base and no root -> preview -> root circle can form
    return {"nav": {"": "index.html", "index.html": "index.html"}, "moved": {"preview/": "", "preview/index.html": "", "preview": ""}, "drop": ["preview/index.html"]}
