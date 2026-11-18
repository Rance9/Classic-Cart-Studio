r"""The public website's entry pages (GitHub Pages only; the Windows app's own copy of the site never uses this). build_site_ci.py calls
apply(out, mode) for `--public prelaunch` or `--public launch`, after the page and the static pages are in <out> and before the service
worker's file list is made, so the entry pages and the worker's inventory always belong together.

  prelaunch  /            a coming-soon page: the wordmark, YOUR CLASSICS. YOUR WAY. and COMING FEBRUARY 2027 (it may wrap on a narrow screen), drawn with the site's own pixel font
                          and CSS only (no picture, no link; its one button is the sound control, below); the only page offered to
                          search engines. A tiny script sends an installed web app (launched at its start address, /) and the app's own working links (?companion=, ?play=) on
                          to the app at /preview/, with their query and fragment; an ordinary browser stays on the page. It never registers
                          a service worker; it only asks an existing registration to look for an update.
             /preview/    the whole site exactly as before (its files stay at the root: <base href="../">), not indexed, its own canonical
  launch     /            the whole site, indexed, canonical /
             /preview/    the same site (a transition copy, not indexed, canonical /) until the host's permanent redirect to / is on
The splash's sound control (sound_parts): a speaker at the top right, off on every visit. Only a visitor's own click, tap or key press turns
on the CCS HOME theme (assets/home-music/music.json): the whole song, looped without a gap by Web Audio at a moderate level, from the HOME's
own browser AAC (brand/home/music/, unchanged) or, where AAC cannot be decoded, its MP3 (brand/splash/, make_splash_music.py). Nothing is
fetched, decoded or remembered before that, nothing is stored, and the page in the background (another tab or window, leaving the page, an
audio interruption) turns it off for good: only another press turns it on. The MP3 is published only with the splash and is never in the
service worker's files.
Both: /nes-catalog.html is not published (the generated page is the Windows app's local document; the public copy was only a duplicate),
the link-preview card (the wordmark alone, 1200 x 630) and each page's title, description and Open Graph / Twitter data.
Part of Classic Cart Studio: GNU GPL v3 or later, like the rest of this repository."""
from __future__ import annotations
import hashlib, json, os, random, re, shutil
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.dirname(os.path.dirname(HERE))
SITE = "https://classiccartstudio.com"
CARD = "brand/social/classic-cart-studio-1200x630.png"; CARD_W, CARD_H = 1200, 630
NAME = "Classic Cart Studio"
DESC_SPLASH = "Your classics. Your way. Coming soon."
DESC_APP = "A free, open-source app for organizing and playing your NES collection in a browser, on a phone or on Windows."
BACK_ROOT, BACK_PREVIEW = '<a href="https://ClassicCartStudio.com/">Back to the website</a>', '<a href="/preview/">Back to the website</a>'   # /app/, /source/
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

# ---- the splash's sound control ----
HOME_MUSIC = os.path.join(HERE, "assets", "home-music", "music.json")
SPLASH_MUSIC_DIR = os.path.join(HERE, "assets", "splash-music"); SPLASH_MUSIC = os.path.join(SPLASH_MUSIC_DIR, "splash-music.json")
SOUND_DIR = "brand/splash"   # the MP3 fallback's published folder: never one of the service worker's (build_site_ci.py pwa refuses it)
SOUND_GAIN = 0.3
SOUND_ICON = ('<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M3.5 9.5h3.8L12 5.8v12.4l-4.7-3.7H3.5z" fill="currentColor"/>'
              '<path class="x" d="M15.5 9.5l5 5m0-5l-5 5" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>'
              '<path class="w" d="M15.2 9a4.2 4.2 0 0 1 0 6M17.8 6.4a7.9 7.9 0 0 1 0 11.2" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>')
# top right: 22 px from the corner on a large screen; 16 px plus the screen's safe area on a phone (either way up); above the page's scanlines.
# Muted speaker (gray) when off, sounding speaker (the neon's cyan) when on; a cyan focus ring for the keyboard; nothing moves
SOUND_CSS = ("#snd{position:fixed;z-index:2;top:22px;right:22px;top:max(22px,calc(16px + env(safe-area-inset-top,0px)));right:max(22px,calc(16px + env(safe-area-inset-right,0px)));"
             "box-sizing:border-box;width:44px;height:44px;margin:0;padding:0;display:flex;align-items:center;justify-content:center;-webkit-appearance:none;appearance:none;"
             "border:1px solid rgba(184,189,204,.3);border-radius:50%;background:rgba(5,7,15,.55);color:#b8bdcc;cursor:pointer;touch-action:manipulation;-webkit-tap-highlight-color:transparent}\n"
             "#snd[hidden]{display:none}#snd svg{display:block;width:24px;height:24px;pointer-events:none}#snd .w,#snd.on .x{display:none}#snd.on .w{display:inline}\n"
             f"#snd.on{{color:{CYAN};border-color:rgba(127,248,255,.5)}}@media (hover:hover){{#snd:hover{{color:#e6e9f2;border-color:rgba(127,248,255,.45)}}#snd.on:hover{{color:{CYAN}}}}}\n"
             f"#snd:focus{{outline:2px solid {CYAN};outline-offset:3px}}#snd:focus:not(:focus-visible){{outline:none}}\n"
             "@media (max-width:600px),(max-height:500px){#snd{top:16px;right:16px;top:calc(16px + env(safe-area-inset-top,0px));right:calc(16px + env(safe-area-inset-right,0px))}}\n")
# The control. Off on every load and every return (pagehide, pageshow from the back/forward cache); nothing is stored anywhere. A deliberate
# activation of the button (click, tap, Enter or Space: the native button's one click; a held key's repeats are ignored) creates or resumes the
# AudioContext inside that gesture (iPhone), then fetches and decodes the song: AAC, or the MP3 when the browser says it cannot play AAC; an
# AAC that does not decode gets the MP3 once, while still turned on. The decoded song is cut to exactly its frames when a decoder kept the
# file's priming or padding (the record's trims: [start, extra] frames at 48 kHz, matched by length), then loops natively (loop over the
# whole buffer: sample-timed, no gap). It starts only if that same activation is still the current one and the page is in front. Hidden, left,
# in the background or interrupted (the context leaves "running"): it stops and shows off; nothing ever turns it on again by itself. A failed
# fetch, decode or start leaves it off without a word; another press tries again. Only a decoded buffer is kept, in memory, for this visit.
SOUND_JS = r"""(function(){var b=document.getElementById("snd"),W=window,D=document,AC=W.AudioContext||W.webkitAudioContext;
if(!b||!AC||!W.fetch||!W.Promise)return;
var F=__F__,A=__A__,M=__M__,S={on:0,g:0,c:null,v:null,s:null,b:null,p:null};
function nop(){}
function show(x){S.on=x;b.className=x?"on":"";b.setAttribute("aria-label",x?"Turn sound off":"Turn sound on");}
function off(){S.g++;show(0);var s=S.s,c=S.c;S.s=null;
 if(s){s.onended=null;try{s.stop();}catch(e){}try{s.disconnect();}catch(e){}}
 if(c&&c.state!=="closed")try{var p=c.suspend();if(p&&p.catch)p.catch(nop);}catch(e){}}
function dec(c,a){return new Promise(function(y,n){var p;try{p=c.decodeAudioData(a,y,n);}catch(e){n(e);return;}if(p&&p.then)p.then(y,n);});}
function fit(c,x,t){var k=x.sampleRate/48e3,N=Math.round(F*k),i,j,q,o,s;
 for(i=0;i<t.length;i++){q=t[i];if(Math.abs(x.length-N-Math.round(q[1]*k))<=4){if(!q[1])return x;s=Math.round(q[0]*k);o=c.createBuffer(x.numberOfChannels,N,x.sampleRate);
  for(j=0;j<x.numberOfChannels;j++)o.getChannelData(j).set(x.getChannelData(j).subarray(s,s+N));return o;}}
 return x;}
function one(c,f){return fetch(f[0]).then(function(r){if(!r.ok)throw 1;return r.arrayBuffer();}).then(function(a){return dec(c,a).then(function(x){return fit(c,x,f[1]);},function(){throw 2;});});}
function load(c){if(S.b)return Promise.resolve(S.b);
 if(!S.p){var m="";try{m=D.createElement("audio").canPlayType('audio/mp4; codecs="mp4a.40.2"');}catch(e){}
  S.p=(m?one(c,A).catch(function(e){if(e!==2||!S.on)throw e;return one(c,M);}):one(c,M)).then(function(x){S.p=null;return S.b=x;},function(e){S.p=null;throw e;});}
 return S.p;}
function on(){var g=++S.g,c=S.c,r;show(1);
 try{if(!c||c.state==="closed"){try{c=new AC({latencyHint:"playback",sampleRate:48e3});}catch(e){c=new AC();}
   S.c=c;S.v=c.createGain();S.v.gain.value=__G__;S.v.connect(c.destination);c.onstatechange=function(){if(S.s&&S.c===c&&c.state!=="running")off();};}
  r=c.resume();}catch(e){off();return;}
 Promise.all([Promise.race([r,new Promise(function(y,n){setTimeout(n,5e3);})]),load(c)]).then(function(v){
  if(g!==S.g)return;if(D.hidden||c.state!=="running")throw 0;
  var s=c.createBufferSource();s.buffer=v[1];s.loop=true;s.connect(S.v);s.onended=function(){if(S.s===s)off();};s.start(0);S.s=s;
 }).catch(function(){if(g===S.g)off();});}
b.addEventListener("click",function(e){if(e.isTrusted===false)return;if(S.on)off();else on();});
b.addEventListener("keydown",function(e){if(e.repeat&&(e.key==="Enter"||e.key===" "||e.key==="Spacebar"))e.preventDefault();});
D.addEventListener("visibilitychange",function(){if(D.hidden)off();});
W.addEventListener("pagehide",off);W.addEventListener("blur",off);W.addEventListener("pageshow",function(e){if(e.persisted)off();});
b.hidden=false;})();"""

def _sha(path: str) -> str: return hashlib.sha256(open(path, "rb").read()).hexdigest()

def sound_config(out: str) -> dict:
    """the control's two files, each checked against its record: the HOME theme's own browser AAC as the site build published it (music.json,
    brand/home/music/, copied unchanged from assets/home) and the MP3 fallback (make_splash_music.py); their trims and the song's frames"""
    hm = json.load(open(HOME_MUSIC, encoding="utf-8")); sm = json.load(open(SPLASH_MUSIC, encoding="utf-8")); br, mp3 = hm["browser"], sm["mp3"]
    aac = os.path.join(out, *br["file"].split("/"))
    if not os.path.isfile(aac) or _sha(aac) != br["sha256"]: raise SystemExit(f"the HOME theme's browser file is not the recorded one: {br['file']}")
    if (sm["aac"]["published"] != br["file"] or sm["aac"]["sha256"] != br["sha256"] or sm["aac"]["bytes"] != os.path.getsize(aac)
            or sm["song"]["pcm_sha256"] != hm["native"]["sha256"] or sm["song"]["frames"] != hm["native"]["frames"]):
        raise SystemExit("assets/splash-music/splash-music.json is not about this HOME theme (run tools/publish/make_splash_music.py)")
    src = os.path.join(SPLASH_MUSIC_DIR, mp3["file"])
    if not os.path.isfile(src) or _sha(src) != mp3["sha256"] or os.path.getsize(src) != mp3["bytes"] or mp3["published"] != f"{SOUND_DIR}/{mp3['file']}":
        raise SystemExit("the splash's MP3 is not the recorded one (run tools/publish/make_splash_music.py)")
    return {"frames": hm["native"]["frames"], "aac": br["file"], "aac_trims": sm["aac"]["trims"], "mp3": mp3["published"], "mp3_trims": mp3["trims"], "mp3_src": src,
            "aac_bytes": os.path.getsize(aac), "aac_sha256": br["sha256"], "mp3_bytes": mp3["bytes"], "mp3_sha256": mp3["sha256"]}

def sound_parts(cfg: dict) -> tuple[str, str]:
    """(the control's CSS, appended to the splash's style; its button and script, the body's first content)"""
    js = (SOUND_JS.replace("__F__", str(int(cfg["frames"]))).replace("__G__", repr(SOUND_GAIN))
          .replace("__A__", json.dumps([cfg["aac"], cfg["aac_trims"]], separators=(",", ":")))
          .replace("__M__", json.dumps([cfg["mp3"], cfg["mp3_trims"]], separators=(",", ":"))))
    assert "__" not in js and "</" not in js and "<!--" not in js
    return SOUND_CSS, f'<button type="button" id="snd" aria-label="Turn sound on" hidden>{SOUND_ICON}</button><script>{js}</script>'

def splash_html(sound: dict | None = None) -> str:
    """the coming-soon page; with `sound` (sound_config) its sound control, which adds only its CSS and its button and script: without it,
    the page is the one published before the control existed, byte for byte"""
    assert flashes_ok(), "COMING SOON may flash more than three times in a second"
    css, ctl = sound_parts(sound) if sound else ("", "")
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
{css}</style></head>
<body>{ctl}<main><div class="c"><h1><span class="w1">Classic Cart</span><span class="w2">Studio</span></h1><p class="tag">Your Classics. Your Way.</p><p class="soon">Coming February 2027</p></div></main>
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
        snd = sound_config(out); os.makedirs(os.path.join(out, *SOUND_DIR.split("/")), exist_ok=True)
        shutil.copyfile(snd["mp3_src"], os.path.join(out, *snd["mp3"].split("/")))
        open(os.path.join(out, "index.html"), "w", encoding="utf-8", newline="\n").write(splash_html(snd))
        print(f"splash sound: {snd['aac']} ({snd['aac_bytes']:,} bytes), fallback {snd['mp3']} ({snd['mp3_bytes']:,} bytes)")
        open(os.path.join(out, "preview", "index.html"), "w", encoding="utf-8", newline="\n").write(app_html(page, url=SITE + "/preview/", robots="noindex,follow", preview=True, home="preview/"))
        st = os.path.join(out, "store", "index.html")   # the store's Back links go to the app (during the preview, at /preview/)
        if os.path.isfile(st):
            s = open(st, encoding="utf-8").read(); n = s.count('href="/"'); open(st, "w", encoding="utf-8", newline="\n").write(s.replace('href="/"', 'href="/preview/"'))
            print("store page:", n, "Back links to /preview/")
        for n in ("app", "source"):   # the helper pages' "Back to the website" goes to the app too; their release and source links stay
            p = os.path.join(out, n, "index.html")
            if os.path.isfile(p):
                s = open(p, encoding="utf-8").read(); k = s.count(BACK_ROOT); open(p, "w", encoding="utf-8", newline="\n").write(s.replace(BACK_ROOT, BACK_PREVIEW))
                print(f"{n} page:", k, "Back link to /preview/")
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
