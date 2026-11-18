r"""The public splash's sound control, driven in headless Edge against the BUILT prelaunch site (build_site_ci.py --public prelaunch), served
on 127.0.0.1 by this test's own server (it logs every request and can make an audio file fail, arrive slowly or arrive damaged). The page
is instrumented before it loads (AudioContext, decoding, buffer sources, fetch, storage, errors); presses are real DevTools input (trusted,
with user activation), Edge's autoplay policy is the default one (a gesture is required) and its output is muted.
  page        the built index.html without the control is the coming-soon page byte for byte (public_entry.splash_html(); and a given
              --baseline file, e.g. the page built before the control existed); its files as recorded; not in the worker's files
  before      no audio request, AudioContext, decode, audio element, preload, worker or storage before a press (hover, Tab focus, waiting)
  a11y        a native button named "Turn sound on" / "Turn sound off", its icon hidden from the accessibility tree, a visible focus ring
              from the keyboard only
  play        a click starts the HOME theme: one AAC request, the context running at 48 kHz, ONE looping AudioBufferSourceNode (loop over the
              whole buffer) whose buffer is exactly the song's 2,976,768 frames (62.016 s), aligned with the PCM at its start and its end
              (no priming left in), at gain 0.3; rendered offline, the loop's seam repeats the song's first samples exactly (no gap); still
              the same node after the song's end in real time (no restart; skipped with --quick)
  toggle      a second click stops it; a third starts again from the decoded song (no new request); Enter and Space toggle once each, a
              held key's repeats do nothing
  background  hidden (emulated visibilitychange), window blur, the context leaving "running" (an interruption), leaving the page
              (pagehide, real; back/forward cache restore): off, and nothing turns it on again on return
  races       fast double and triple toggles, off while loading, hidden while loading, a late decode, a late fallback: never stale audio
  failure     AAC 404 / dropped connection: off, no MP3 request, no error; a later press works; AAC not playable (canPlayType ""): only the
              MP3; AAC not decodable: the MP3 exactly once; both failing: off
  priming     decoders that keep what the files' own metadata says to drop (the AAC's edit-list priming; the MP3's LAME delay and padding,
              its Info frame), emulated (Edge drops them itself): the page still loops exactly the song, aligned
  storage     nothing written to cookies, local/session storage, IndexedDB, Cache Storage, no service worker registered
  layout      desktop, phone portrait and short phone landscape (safe areas emulated): 44 x 44, its inset, clear of the wordmark, tagline,
              status and footer (at any scroll), inside the screen, no sideways scroll, every text of the page where it was without the control
  python test_splash_sound.py <built site> [--baseline <index.html built before the control>] [--out results.json] [--shots <folder>] [--quick]
Needs the private cdp_edge module on PYTHONPATH (the other website tests' harness). Exit 0 = PASS."""
from __future__ import annotations
import argparse, base64, functools, hashlib, http.server, json, os, re, socket, sys, threading, time, wave
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE); sys.path.insert(0, os.path.join(HERE, "..", "publish"))
import public_entry as PE
from cdp_edge import EdgeSession, HarnessError

FLAGS = ["--disable-renderer-backgrounding", "--disable-background-timer-throttling", "--disable-backgrounding-occluded-windows",
         "--disable-features=CalculateNativeWinOcclusion", "--autoplay-policy=user-gesture-required", "--mute-audio"]
SONG_FRAMES, SONG_RATE = 2976768, 48000

# ---- the page's instrumentation (before any of its scripts) ----
INSTR = r"""(()=>{ const T=window.__T={ctx:[],starts:[],stops:0,ended:0,decodes:[],decoded:[],fetches:[],storage:[],rej:[],errs:[],cons:[],gains:[],
  decodeDelay:0,failMp4:false,noAac:false,node:null};
 const AC=window.AudioContext; window.AudioContext=class extends AC{constructor(o){super(o);T.ctx.push(this);}};
 const cg=BaseAudioContext.prototype.createGain; BaseAudioContext.prototype.createGain=function(){const g=cg.apply(this,arguments);T.gains.push(g);return g;};
 const st=AudioBufferSourceNode.prototype.start; AudioBufferSourceNode.prototype.start=function(){ const b=this.buffer;
  if(this.context instanceof OfflineAudioContext) return st.apply(this,arguments);   /* this test's own offline rendering of the seam */
  const r={loop:this.loop,loopStart:this.loopStart,loopEnd:this.loopEnd,len:b&&b.length,rate:b&&b.sampleRate,dur:b&&b.duration,ch:b&&b.numberOfChannels,state:this.context.state,ctxRate:this.context.sampleRate,at:this.context.currentTime,args:[...arguments],ended:false};
  T.starts.push(r); T.node=this; this.addEventListener('ended',()=>{T.ended++;r.ended=true;}); return st.apply(this,arguments); };
 const sp=AudioBufferSourceNode.prototype.stop; AudioBufferSourceNode.prototype.stop=function(){T.stops++;return sp.apply(this,arguments);};
 const dec=BaseAudioContext.prototype.decodeAudioData; BaseAudioContext.prototype.decodeAudioData=function(a,ok,no){
  const h=new Uint8Array(a.slice(0,12)), mp4=String.fromCharCode(h[4],h[5],h[6],h[7])==='ftyp'; T.decodes.push({bytes:a.byteLength,mp4,t:performance.now()});
  const p=new Promise((y,n)=>setTimeout(()=>{ if(T.failMp4&&mp4){ n(new DOMException('Unable to decode audio data','EncodingError')); return; }
    dec.call(this,a).then(b=>{T.decoded.push({len:b.length,rate:b.sampleRate,mp4});
     const k=T.keep&&T.keep[mp4?'mp4':'mp3'];   /* a decoder that keeps the file's priming / padding (silence where they were), as some browsers' may */
     if(!k){ y(b); return; } const o=new AudioBuffer({length:b.length+k[0]+k[1],numberOfChannels:b.numberOfChannels,sampleRate:b.sampleRate});
     for(let c=0;c<b.numberOfChannels;c++) o.getChannelData(c).set(b.getChannelData(c),k[0]); T.kept=(T.kept||[]).concat([o.length]); y(o); },n); },T.decodeDelay));
  p.then(ok||(()=>{}),no||(()=>{})); return p; };
 const cpt=HTMLMediaElement.prototype.canPlayType; HTMLMediaElement.prototype.canPlayType=function(t){ T.canPlay=(T.canPlay||0)+1; return T.noAac&&/mp4/.test(t)?'':cpt.call(this,t); };
 const f=window.fetch; window.fetch=function(u){ T.fetches.push(String(u&&u.url||u)); return f.apply(this,arguments); };
 for(const m of ['setItem','removeItem','clear']){ const o=Storage.prototype[m]; Storage.prototype[m]=function(){T.storage.push('Storage.'+m);return o.apply(this,arguments);}; }
 const cd=Object.getOwnPropertyDescriptor(Document.prototype,'cookie'); Object.defineProperty(Document.prototype,'cookie',{configurable:true,get(){return cd.get.call(this);},set(v){T.storage.push('cookie');cd.set.call(this,v);}});
 const io=IDBFactory.prototype.open; IDBFactory.prototype.open=function(){T.storage.push('indexedDB.open');return io.apply(this,arguments);};
 if(window.CacheStorage){ const co=CacheStorage.prototype.open; CacheStorage.prototype.open=function(){T.storage.push('caches.open');return co.apply(this,arguments);}; }
 if(navigator.serviceWorker){ const r=ServiceWorkerContainer.prototype.register; ServiceWorkerContainer.prototype.register=function(){T.storage.push('serviceWorker.register');return r.apply(this,arguments);}; }
 window.addEventListener('unhandledrejection',e=>T.rej.push(String(e.reason&&e.reason.message||e.reason)));
 window.addEventListener('error',e=>T.errs.push(String(e.message)));
 for(const k of ['error','warn']){ const o=console[k]; console[k]=function(){T.cons.push(k+': '+[...arguments].join(' '));return o.apply(this,arguments);}; }
 T.ev=[]; document.addEventListener('visibilitychange',()=>T.ev.push('visibility:'+document.visibilityState));
 for(const n of ['pagehide','pageshow','blur','focus']) window.addEventListener(n,e=>T.ev.push(n+(e.persisted===undefined?'':':'+e.persisted)));
 T.pick=(a,n)=>{ const c=T.node.buffer.getChannelData(0), s=a<0?c.length+a:a, u=new Uint8Array(Float32Array.from(c.subarray(s,s+n)).buffer); let x='';
  for(let i=0;i<u.length;i+=8192) x+=String.fromCharCode.apply(null,u.subarray(i,i+8192)); return btoa(x); };
})()"""
READY = "document.readyState==='complete'&&!!window.__T&&!!document.getElementById('snd')&&!document.getElementById('snd').hidden"
STATE = r"""JSON.stringify((()=>{ const b=document.getElementById('snd'); return {label:b.getAttribute('aria-label'), cls:b.className, starts:__T.starts.length, stops:__T.stops,
 ended:__T.ended, ctx:__T.ctx.map(c=>c.state), decodes:__T.decodes.length, decoded:__T.decoded.length, fetches:__T.fetches.slice(), storage:__T.storage.slice(),
 rej:__T.rej.slice(), errs:__T.errs.slice(), cons:__T.cons.slice(), ev:__T.ev.slice()}; })())"""

class Site:
    """the built site on 127.0.0.1; every request logged; faults by path suffix: 404, drop (no response), garbage, slow:<seconds>.
    /__bare.html is the built splash without its sound control (the layout reference); /__away.html a plain page to leave to"""
    def __init__(self, folder: str, bare: bytes):
        self.log, self.faults, self.lock = [], {}, threading.Lock(); site = self
        class H(http.server.SimpleHTTPRequestHandler):
            def log_message(self, *a): pass
            def end_headers(self):
                if re.search(r"\.(m4a|mp3)$", self.path.split("?")[0]): self.send_header("Cache-Control", "no-store")
                super().end_headers()
            def _send(self, code, body, ctype):
                self.send_response(code); self.send_header("Content-Type", ctype); self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)
            def do_GET(self):
                p = self.path.split("?")[0]
                with site.lock: site.log.append(p)
                if p == "/__bare.html": return self._send(200, bare, "text/html; charset=utf-8")
                if p == "/__away.html": return self._send(200, b"<!doctype html><meta charset=utf-8><title>away</title><p>away</p>", "text/html; charset=utf-8")
                f = next((v for k, v in list(site.faults.items()) if p.endswith(k)), None)
                if f == "404": self.send_error(404); return
                if f == "drop": self.close_connection = True; return
                if f == "garbage": return self._send(200, bytes(range(256)) * 600, "audio/mpeg")
                if f and f.startswith("slow:"):
                    body = open(self.translate_path(self.path), "rb").read(); secs = float(f[5:])
                    self.send_response(200); self.send_header("Content-Type", "application/octet-stream"); self.send_header("Content-Length", str(len(body))); self.end_headers()
                    n = 20; step = -(-len(body) // n)
                    for i in range(n): self.wfile.write(body[i * step:(i + 1) * step]); self.wfile.flush(); time.sleep(secs / n)
                    return
                return super().do_GET()
            def handle(self):
                try: super().handle()
                except Exception: pass
        class S(http.server.ThreadingHTTPServer): daemon_threads = True; request_queue_size = 64
        s = socket.socket(); s.bind(("127.0.0.1", 0)); port = s.getsockname()[1]; s.close()
        self.srv = S(("127.0.0.1", port), functools.partial(H, directory=folder)); self.srv.handle_error = lambda *a: None
        threading.Thread(target=self.srv.serve_forever, daemon=True).start(); self.url = f"http://127.0.0.1:{port}/"
    def count(self, suffix: str) -> int:
        with self.lock: return sum(1 for p in self.log if p.endswith(suffix))
    def audio(self) -> list:
        with self.lock: return [p for p in self.log if re.search(r"\.(m4a|mp3|wav|ogg|aac|flac)$", p)]

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--baseline", default=""); ap.add_argument("--out", default="")
    ap.add_argument("--shots", default=""); ap.add_argument("--quick", action="store_true"); a = ap.parse_args()
    site = os.path.abspath(a.site); res, info = [], {}
    def ok(n, c, d=""):
        res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:400]})" if d and not c else ""), flush=True)
    # ---- the generated page ----
    idx = open(os.path.join(site, "index.html"), encoding="utf-8").read(); snd = PE.sound_config(site); css, ctl = PE.sound_parts(snd)
    bare = idx.replace(css, "", 1).replace(ctl, "", 1)
    ok("page: the control once (its CSS at the end of the style, its button and script first in the body)", idx.count(css) == 1 and idx.count(ctl) == 1 and idx.index(css) + len(css) == idx.index("</style>") and idx.index(ctl) == idx.index("<body>") + 6)
    ok("page: without the control, the generated coming-soon page byte for byte", bare == PE.splash_html())
    if a.baseline:
        base = open(a.baseline, encoding="utf-8").read(); ok("page: without the control, byte for byte the page built before the control existed (--baseline)", bare == base, f"{len(bare)} vs {len(base)}")
        info["baseline"] = {"file": a.baseline, "sha256": hashlib.sha256(base.encode()).hexdigest()}
    info["control_bytes"] = {"css": len(css.encode()), "button_and_script": len(ctl.encode())}
    mp3p, aacp = os.path.join(site, *snd["mp3"].split("/")), os.path.join(site, *snd["aac"].split("/"))
    sha = lambda p: hashlib.sha256(open(p, "rb").read()).hexdigest()
    info["files"] = {"primary": {"path": snd["aac"], "bytes": os.path.getsize(aacp), "sha256": sha(aacp)}, "fallback": {"path": snd["mp3"], "bytes": os.path.getsize(mp3p), "sha256": sha(mp3p)}}
    ok("page: the primary is the HOME theme's AAC unchanged, the fallback the recorded MP3", info["files"]["primary"] == {"path": "brand/home/music/ccs-home-theme-7d07857f25e3.m4a", "bytes": 1529985, "sha256": "7d07857f25e3ae4ffd3d38a4a200c43e25b97891fb6195671c486c9aabdd2c68"}
       and info["files"]["fallback"]["sha256"] == snd["mp3_sha256"] and info["files"]["fallback"]["bytes"] == snd["mp3_bytes"], info["files"])
    sw = open(os.path.join(site, "sw.js"), encoding="utf-8").read(); spec = json.loads(re.match(r"self\.CCS_SW = (\{.*?\});\n", sw).group(1))
    ok("page: the splash's MP3 is not one of the service worker's files (the HOME's AAC stays a HOME file, as before)", not any(p.startswith("brand/splash/") or p.endswith(".mp3") for p in spec["shell"] + spec["home"]) and snd["aac"] in spec["home"])
    pcm = wave.open(os.path.join(HERE, "..", "publish", "assets", "home-music", "ccs-home-theme.wav"), "rb"); ref = pcm.readframes(pcm.getnframes()); pcm.close()
    import numpy as np
    ref0 = np.frombuffer(ref, dtype="<i2").reshape(-1, 2)[:, 0].astype(np.float64) / 32768

    srv = Site(site, bare.encode("utf-8")); url = srv.url
    def click(ed):
        r = ed.js("(()=>{const r=document.getElementById('snd').getBoundingClientRect();return [r.left+r.width/2,r.top+r.height/2];})()")
        for t in ("mousePressed", "mouseReleased"): ed.call("Input.dispatchMouseEvent", {"type": t, "x": r[0], "y": r[1], "button": "left", "buttons": 1 if t == "mousePressed" else 0, "clickCount": 1})
    KEYS = {"Tab": ("Tab", "Tab", 9, ""), "Enter": ("Enter", "Enter", 13, "\r"), "Space": (" ", "Space", 32, " ")}
    def key(ed, k, repeat=0):
        kk, code, vk, text = KEYS[k]; d = {"key": kk, "code": code, "windowsVirtualKeyCode": vk, "nativeVirtualKeyCode": vk}
        ed.call("Input.dispatchKeyEvent", {"type": "keyDown" if text else "rawKeyDown", **d, **({"text": text, "unmodifiedText": text} if text else {})})
        for _ in range(repeat): ed.call("Input.dispatchKeyEvent", {"type": "keyDown" if text else "rawKeyDown", "autoRepeat": True, **d, **({"text": text, "unmodifiedText": text} if text else {})}); time.sleep(0.03)
        ed.call("Input.dispatchKeyEvent", {"type": "keyUp", **d})
    def st(ed): return json.loads(ed.js(STATE))
    def fresh(ed, faults=None, setup=""):
        srv.faults.clear(); srv.faults.update(faults or {}); ed.navigate(url); ed.wait_for(READY, 60)
        with srv.lock: srv.log.clear()
        ed.exceptions.clear(); ed.console_errors.clear()
        if setup: ed.js(setup)
    def clean(s, ed): return not (s["rej"] or s["errs"] or s["cons"] or s["storage"] or ed.exceptions or ed.console_errors)
    def why(s, ed): return {k: s[k] for k in ("label", "starts", "stops", "ctx", "fetches", "rej", "errs", "cons", "storage")} | {"exceptions": ed.exceptions[:3], "console": ed.console_errors[:3]}
    def settle(ed, t=1.0): time.sleep(t); return st(ed)
    def lag(x, y):
        return max(range(-300, 301), key=lambda k: float(np.dot(x[400:-400], np.roll(y, k)[400:-400])))
    def pick(ed, a0, n): return np.frombuffer(base64.b64decode(ed.js(f"__T.pick({a0},{n})")), dtype="<f4").astype(np.float64)
    def song_checks(ed, tag, want_m4a, want_mp3):
        ed.wait_for("__T.starts.length>0", 60, what=f"{tag}: the song to start")
        s0 = json.loads(ed.js("JSON.stringify({s:__T.starts[0],decoded:__T.decoded,gain:__T.gains.map(g=>g.gain.value),ctxRate:__T.ctx[0].sampleRate})")); s = settle(ed, 1.2)
        p = s0["s"]; info[f"{tag}_start"] = s0
        ok(f"{tag}: one start, the context running", s["starts"] == 1 and s["ctx"] == ["running"] and s["label"] == "Turn sound off" and s["cls"] == "on", why(s, ed))
        ok(f"{tag}: requests (AAC {want_m4a}, MP3 {want_mp3})", srv.count(".m4a") == want_m4a and srv.count(".mp3") == want_mp3, srv.audio())
        ok(f"{tag}: one looping buffer source over the whole buffer (loop, loopStart 0, loopEnd 0, start(0))", p["loop"] is True and p["loopStart"] == 0 and p["loopEnd"] == 0 and p["args"] == [0], p)
        ok(f"{tag}: the buffer is exactly the song: {SONG_FRAMES:,} frames at {SONG_RATE:,} Hz (62.016 s), stereo", p["len"] == SONG_FRAMES and p["rate"] == SONG_RATE and abs(p["dur"] - 62.016) < 1e-9 and p["ch"] == 2 and s0["ctxRate"] == SONG_RATE, p)
        st0, en0 = lag(ref0[:16384], pick(ed, 0, 16384)), lag(ref0[-16384:], pick(ed, -16384, 16384))
        info[f"{tag}_alignment"] = {"start_lag": st0, "end_lag": en0, "decoded_by_edge": s0["decoded"]}
        ok(f"{tag}: aligned with the PCM at its first and last samples (no priming or padding left in; lags {st0}, {en0})", st0 == 0 and en0 == 0)
        ok(f"{tag}: gain 0.3", len(s0["gain"]) == 1 and abs(s0["gain"][0] - 0.3) < 1e-6, s0["gain"])
        t1 = ed.js("__T.ctx[0].currentTime"); time.sleep(0.6); t2 = ed.js("__T.ctx[0].currentTime"); ok(f"{tag}: the context's clock runs", t2 > t1 + 0.3, (t1, t2))
        ok(f"{tag}: no error, rejection or storage", clean(s, ed), why(s, ed))
    def is_off(s, starts=None): return s["label"] == "Turn sound on" and s["cls"] == "" and (starts is None or s["starts"] == starts)

    try:
        with EdgeSession(width=1366, height=768, extra_args=FLAGS, start_timeout=120.0) as ed:
            ed.call("Page.addScriptToEvaluateOnNewDocument", {"source": INSTR}); ed.call("Emulation.setFocusEmulationEnabled", {"enabled": True})
            # ---- before any press ----
            fresh(ed); r = ed.js("(()=>{const r=document.getElementById('snd').getBoundingClientRect();return [r.left+22,r.top+22];})()")
            ed.call("Input.dispatchMouseEvent", {"type": "mouseMoved", "x": r[0], "y": r[1]}); time.sleep(0.5); key(ed, "Tab"); time.sleep(2.5)
            s = st(ed); extra = json.loads(ed.js("JSON.stringify({audio:document.querySelectorAll('audio,video').length,pre:document.querySelectorAll('link[rel=preload],link[rel=prefetch],link[rel=modulepreload]').length,res:performance.getEntriesByType('resource').map(e=>e.name),canPlay:__T.canPlay||0})"))
            reg = ed.js("navigator.serviceWorker.getRegistration().then(r=>!!r)")
            ok("before: no audio request, no AudioContext, no decode, no canPlayType, no audio element or preload", not srv.audio() and not s["ctx"] and not s["decodes"] and not s["fetches"] and extra["canPlay"] == 0 and extra["audio"] == 0 and extra["pre"] == 0
               and not [u for u in extra["res"] if re.search(r"\.(m4a|mp3)", u)], {"server": srv.audio(), **s, **extra})
            ok("before: off, no worker registered, nothing stored", is_off(s, 0) and reg is False and clean(s, ed), why(s, ed))
            # ---- accessibility ----
            fv = json.loads(ed.js("JSON.stringify((()=>{const b=document.getElementById('snd'),c=getComputedStyle(b);return {focused:document.activeElement===b,fv:b.matches(':focus-visible'),style:c.outlineStyle,width:c.outlineWidth,color:c.outlineColor,type:b.type,tag:b.tagName}})())"))
            ok("a11y: a native button (type=button), Tab reaches it with a visible 2 px cyan focus ring", fv == {"focused": True, "fv": True, "style": "solid", "width": "2px", "color": "rgb(127, 248, 255)", "type": "button", "tag": "BUTTON"}, fv)
            def ax():
                nodes = ed.call("Accessibility.getFullAXTree")["result"]["nodes"]; shown = [n for n in nodes if not n.get("ignored")]
                btn = [n for n in shown if (n.get("role") or {}).get("value") == "button"]
                pics = [n for n in shown if (n.get("role") or {}).get("value") in ("image", "graphics-document", "graphics-symbol", "img", "SvgRoot")]
                return [(b.get("name") or {}).get("value") for b in btn], len(pics)
            ed.call("Accessibility.enable"); names, pics = ax()
            ok("a11y: the accessibility tree has one button, named Turn sound on, and no picture (the icon is hidden)", names == ["Turn sound on"] and pics == 0, (names, pics))
            # ---- play, stop, play again ----
            click(ed); song_checks(ed, "play AAC", 1, 0)
            names, _ = ax(); ok("a11y: while on, the button is named Turn sound off", names == ["Turn sound off"], names)
            seam = json.loads(ed.js(r"""(async()=>{ const b=__T.node.buffer, N=b.length, X=4800, o=new OfflineAudioContext(b.numberOfChannels,N+X,b.sampleRate), s=o.createBufferSource();
              s.buffer=b; s.loop=true; s.connect(o.destination); s.start(0); const r=await o.startRendering(); let bad=0, quiet=0;
              for(let c=0;c<b.numberOfChannels;c++){ const R=r.getChannelData(c), B=b.getChannelData(c); for(let k=0;k<X;k++){ if(R[N+k]!==B[k]) bad++; if(R[N-X+k]!==B[N-X+k]) bad++; } }
              let e=0; const R0=r.getChannelData(0); for(let k=N-480;k<N+480;k++) e+=R0[k]*R0[k]; return JSON.stringify({bad, N, rms_around_seam:Math.sqrt(e/960)}); })()"""))
            info["seam"] = seam; ok("play AAC: rendered offline, the loop's seam is the song's last then first samples exactly (no gap, no silence)", seam["bad"] == 0 and seam["N"] == SONG_FRAMES and seam["rms_around_seam"] > 0.005, seam)
            click(ed); s = settle(ed, 0.8)
            ok("stop: a second click turns it off (the source stopped, the context suspended)", is_off(s, 1) and s["stops"] == 1 and s["ctx"] == ["suspended"], why(s, ed))
            click(ed); ed.wait_for("__T.starts.length===2", 20); s = settle(ed, 0.6)
            ok("again: a third click plays the decoded song again: no new request, the same context", s["starts"] == 2 and s["ctx"] == ["running"] and srv.count(".m4a") == 1 and s["decodes"] == 1 and s["label"] == "Turn sound off", why(s, ed))
            if not a.quick:   # past the song's end in real time: the same node, never ended or restarted
                ed.wait_for("__T.ctx[0].currentTime>__T.starts[1].at+63.5", 100, what="the song's end"); s = st(ed); e2 = ed.js("[__T.starts[1].ended,__T.ctx[0].currentTime-__T.starts[1].at]")
                ok("loop: after the song's end in real time, the same node plays on (it never ended; no restart)", s["starts"] == 2 and e2[0] is False and e2[1] > 63.5 and s["ctx"] == ["running"], why(s, ed) | {"node": e2})
            # ---- leaving and returning ----
            ed.js("(()=>{Object.defineProperty(document,'hidden',{configurable:true,get:()=>true});Object.defineProperty(document,'visibilityState',{configurable:true,get:()=>'hidden'});document.dispatchEvent(new Event('visibilitychange'));return 1})()")
            s = settle(ed, 0.6); ok("hidden: the page hidden turns it off", is_off(s) and s["ctx"] == ["suspended"], why(s, ed))
            ed.js("(()=>{delete document.hidden;delete document.visibilityState;document.dispatchEvent(new Event('visibilitychange'));window.dispatchEvent(new Event('focus'));return 1})()")
            s = settle(ed, 2.0); ok("hidden: shown again, it stays off (no resume)", is_off(s) and s["starts"] == 2 and s["ctx"] == ["suspended"], why(s, ed))
            click(ed); ed.wait_for("__T.starts.length===3", 20)
            ed.call("Emulation.setFocusEmulationEnabled", {"enabled": False}); time.sleep(0.5); real_blur = "blur" in st(ed)["ev"]
            if not real_blur: ed.js("window.dispatchEvent(new Event('blur'))")
            info["blur"] = "real (focus emulation off)" if real_blur else "emulated (window blur event)"
            s = settle(ed, 0.6); ok(f"blur: the window in the background turns it off ({info['blur']})", is_off(s) and s["ctx"] == ["suspended"], why(s, ed))
            ed.call("Emulation.setFocusEmulationEnabled", {"enabled": True}); ed.js("window.dispatchEvent(new Event('focus'))")
            s = settle(ed, 2.0); ok("blur: focused again, it stays off", is_off(s) and s["starts"] == 3, why(s, ed))
            click(ed); ed.wait_for("__T.starts.length===4", 20); ed.js("__T.ctx[0].suspend().then(()=>1)")
            s = settle(ed, 0.6); ok("interruption: the context leaving running (suspended by the system) turns it off", is_off(s) and s["stops"] >= 4, why(s, ed))
            s = settle(ed, 2.0); ok("interruption: nothing resumes it", is_off(s) and s["starts"] == 4 and s["ctx"] == ["suspended"], why(s, ed))
            click(ed); ed.wait_for("__T.starts.length===5", 20); ok("leaving: playing before leaving the page", st(ed)["ctx"] == ["running"])
            ed.navigate(url + "__away.html"); time.sleep(1.5); h = ed.call("Page.getNavigationHistory")["result"]
            ed.call("Page.navigateToHistoryEntry", {"entryId": h["entries"][h["currentIndex"] - 1]["id"]}); ed.wait_for(READY, 30); s = settle(ed, 2.0)
            restored = "pageshow:true" in s["ev"]; info["back_forward_cache"] = "restored from the back/forward cache" if restored else "loaded again (not from the back/forward cache)"
            ok(f"leaving: back again it is off and silent ({info['back_forward_cache']})", is_off(s) and (not restored or ("pagehide:true" in s["ev"] and s["stops"] >= 5 and s["ctx"] == ["suspended"] and s["starts"] == 5)), why(s, ed) | {"ev": s["ev"]})
            # ---- keyboard ----
            fresh(ed); key(ed, "Tab"); key(ed, "Enter"); ed.wait_for("__T.starts.length===1", 30); s = settle(ed, 0.8)
            ok("keyboard: Enter turns it on once", s["starts"] == 1 and s["label"] == "Turn sound off" and srv.count(".m4a") == 1, why(s, ed))
            key(ed, "Space"); s = settle(ed, 0.8); ok("keyboard: Space turns it off once", is_off(s, 1) and s["stops"] == 1, why(s, ed))
            key(ed, "Enter", repeat=6); s = settle(ed, 0.8); ok("keyboard: Enter held (6 repeats) toggles once", s["starts"] == 2 and s["label"] == "Turn sound off", why(s, ed))
            key(ed, "Space", repeat=6); s = settle(ed, 0.8); ok("keyboard: Space held (6 repeats) toggles once", is_off(s, 2), why(s, ed))
            ok("keyboard: no error, rejection or storage", clean(s, ed), why(s, ed))
            # ---- races ----
            fresh(ed, {".m4a": "slow:2.5"}); click(ed); click(ed); s = settle(ed, 4.5)
            ok("race: on-off before the song arrives: never starts", is_off(s, 0) and srv.count(".m4a") == 1 and s["decoded"] == 1 and clean(s, ed), why(s, ed))
            fv2 = ed.js("(()=>{const b=document.getElementById('snd');return [document.activeElement===b,b.matches(':focus-visible'),getComputedStyle(b).outlineStyle]})()")
            ok("a11y: a pointer press (no keyboard use) shows no focus ring", fv2 == [True, False, "none"], fv2)
            click(ed); ed.wait_for("__T.starts.length===1", 10); s = settle(ed, 0.5)
            ok("race: the next press plays the song already decoded (no new request)", s["starts"] == 1 and srv.count(".m4a") == 1, why(s, ed))
            fresh(ed, {".m4a": "slow:2.5"}); click(ed); click(ed); click(ed); s = settle(ed, 4.5)
            ok("race: on-off-on while loading: exactly one start, on", s["starts"] == 1 and s["label"] == "Turn sound off" and s["ctx"] == ["running"] and srv.count(".m4a") == 1 and clean(s, ed), why(s, ed))
            fresh(ed, {".m4a": "slow:3"}); click(ed); time.sleep(1.0); click(ed); s = settle(ed, 4.0)
            ok("race: off while loading: nothing starts when the song arrives", is_off(s, 0) and s["decoded"] == 1 and clean(s, ed), why(s, ed))
            fresh(ed, {".m4a": "slow:2.5"}); click(ed); time.sleep(0.8)
            ed.js("(()=>{Object.defineProperty(document,'hidden',{configurable:true,get:()=>true});Object.defineProperty(document,'visibilityState',{configurable:true,get:()=>'hidden'});document.dispatchEvent(new Event('visibilitychange'));return 1})()")
            s = settle(ed, 3.5); ed.js("(()=>{delete document.hidden;delete document.visibilityState;document.dispatchEvent(new Event('visibilitychange'));return 1})()"); s2 = settle(ed, 1.0)
            ok("race: hidden while loading: nothing starts, then or on return", is_off(s, 0) and is_off(s2, 0) and s2["decoded"] == 1 and clean(s2, ed), why(s2, ed))
            fresh(ed, setup="__T.decodeDelay=2000"); click(ed); ed.wait_for("__T.decodes.length===1", 20); click(ed); s = settle(ed, 3.5)
            ok("race: a late decode (off while decoding): nothing starts", is_off(s, 0) and s["decoded"] == 1 and clean(s, ed), why(s, ed))
            fresh(ed, {".mp3": "slow:3"}, setup="__T.failMp4=true"); click(ed); ed.wait_for("__T.fetches.length===2", 20); time.sleep(0.5); click(ed); s = settle(ed, 4.5)
            ok("race: a late fallback (off while the MP3 loads): nothing starts", is_off(s, 0) and srv.count(".m4a") == 1 and srv.count(".mp3") == 1 and s["decoded"] == 1 and clean(s, ed), why(s, ed))
            # ---- failures and the codec fallback ----
            # requests the PAGE makes (fetch calls); a dropped connection is also resent by the browser's own network stack, below the page
            n_ = lambda s, ext: sum(1 for u in s["fetches"] if u.endswith(ext)); info["dropped_connection_server_requests"] = None
            for mode in ("404", "drop"):
                fresh(ed, {".m4a": mode}); click(ed); s = settle(ed, 2.0)
                ok(f"failure: the AAC request fails ({mode}): off, no MP3 request (not a codec failure), no error or rejection", is_off(s, 0) and n_(s, ".m4a") == 1 and n_(s, ".mp3") == 0 and srv.count(".mp3") == 0 and clean(s, ed), why(s, ed))
                time.sleep(1.0); s = st(ed); ok(f"failure ({mode}): no retry by the page", len(s["fetches"]) == 1 and is_off(s, 0), s["fetches"])
                if mode == "drop": info["dropped_connection_server_requests"] = srv.count(".m4a")   # the browser's own resends of the one fetch
            srv.faults.clear(); click(ed); ed.wait_for("__T.starts.length===1", 30); s = settle(ed, 0.5)
            ok("failure: a later press tries again and plays", s["starts"] == 1 and s["label"] == "Turn sound off" and n_(s, ".m4a") == 2, why(s, ed))
            fresh(ed, setup="__T.noAac=true"); click(ed); song_checks(ed, "no AAC (canPlayType empty)", 0, 1)
            fresh(ed, setup="__T.failMp4=true"); click(ed); song_checks(ed, "AAC not decodable", 1, 1)
            s = st(ed); ok("AAC not decodable: the MP3 exactly once (two decodes in all)", s["decodes"] == 2 and len(s["fetches"]) == 2, why(s, ed))
            # decoders that keep what the files' own metadata says to drop (Edge drops both itself): the page cuts exactly the song out of them
            sm = json.load(open(PE.SPLASH_MUSIC, encoding="utf-8")); al, at_ = sm["aac"]["lead_frames"], sm["aac"]["tail_frames"]; ml, mt = sm["mp3"]["lead_frames"], sm["mp3"]["tail_frames"]
            for tag, keep, setup, want in ((f"AAC decoder keeping its {al:,} priming frames (emulated)", [al, at_], "__T.keep={mp4:%s}" % json.dumps([al, at_]), (1, 0)),
                                           (f"MP3 decoder ignoring the LAME tag ({ml:,} before, {mt} after; emulated)", [ml, mt], "__T.noAac=true;__T.keep={mp3:%s}" % json.dumps([ml, mt]), (0, 1)),
                                           (f"MP3 decoder also playing the Info frame ({ml + 1152:,} before, {mt} after; emulated)", [ml + 1152, mt], "__T.noAac=true;__T.keep={mp3:%s}" % json.dumps([ml + 1152, mt]), (0, 1)),
                                           (f"MP3 decoder honoring only the start ({mt} after; emulated)", [0, mt], "__T.noAac=true;__T.keep={mp3:%s}" % json.dumps([0, mt]), (0, 1))):
                fresh(ed, setup=setup); click(ed); song_checks(ed, tag, *want); info[tag + "_kept_length"] = ed.js("__T.kept")
            fresh(ed, {".mp3": "garbage"}, setup="__T.failMp4=true"); click(ed); s = settle(ed, 2.5)
            ok("failure: AAC and MP3 both undecodable: off, one request each, no error", is_off(s, 0) and srv.count(".m4a") == 1 and srv.count(".mp3") == 1 and clean(s, ed), why(s, ed))
            # ---- storage, worker ----
            store = json.loads(ed.js("Promise.all([indexedDB.databases?indexedDB.databases():[],caches.keys(),navigator.serviceWorker.getRegistrations()]).then(([d,c,r])=>JSON.stringify({ls:localStorage.length,ss:sessionStorage.length,cookie:document.cookie,idb:d.length,caches:c.length,sw:r.length}))"))
            ok("storage: nothing kept anywhere (local/session storage, cookies, IndexedDB, Cache Storage, service worker)", store == {"ls": 0, "ss": 0, "cookie": "", "idb": 0, "caches": 0, "sw": 0}, store)
            # ---- layout ----
            SIZES = [("desktop 1366x768", 1366, 768, 1, False, None, 22), ("desktop 1920x1080", 1920, 1080, 1, False, None, 22), ("desktop 1024x768", 1024, 768, 1, False, None, 22),
                     ("short desktop 1280x400", 1280, 400, 1, False, None, 16),
                     ("phone portrait 390x844 (notch)", 390, 844, 3, True, {"top": 47, "bottom": 34, "left": 0, "right": 0}, 16), ("phone portrait 375x667", 375, 667, 2, True, None, 16),
                     ("phone portrait 320x568", 320, 568, 2, True, None, 16),
                     ("phone landscape 844x390 (notch)", 844, 390, 3, True, {"top": 0, "bottom": 21, "left": 47, "right": 47}, 16), ("phone landscape 667x375", 667, 375, 2, True, None, 16),
                     ("phone landscape 568x320", 568, 320, 2, True, None, 16), ("phone landscape 640x360", 640, 360, 3, True, None, 16), ("phone landscape 932x430 (notch)", 932, 430, 3, True, {"top": 0, "bottom": 21, "left": 59, "right": 59}, 16)]
            MEASURE = r"""document.fonts.ready.then(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r)))).then(()=>{ const R=e=>{const r=e.getBoundingClientRect();return [r.left,r.top,r.right,r.bottom]};
             const txt=e=>{const g=document.createRange();g.selectNodeContents(e);return [...g.getClientRects()].filter(r=>r.width&&r.height).map(r=>[r.left,r.top,r.right,r.bottom])};
             const b=document.getElementById('snd'), q=s=>document.querySelector(s), el=b&&!b.hidden?document.elementFromPoint((b.getBoundingClientRect().left+b.getBoundingClientRect().right)/2,(b.getBoundingClientRect().top+b.getBoundingClientRect().bottom)/2):null;
             return JSON.stringify({vw:document.documentElement.clientWidth,vh:innerHeight,sw:document.documentElement.scrollWidth,sh:document.documentElement.scrollHeight,
              btn:b&&!b.hidden?R(b):null, hit:el?(el===b||b.contains(el)):null,
              text:{w1:txt(q('.w1')),w2:txt(q('.w2')),tag:txt(q('.tag')),soon:txt(q('.soon')),footer:txt(q('footer'))},
              box:{w1:R(q('.w1')),w2:R(q('.w2')),tag:R(q('.tag')),soon:R(q('.soon')),footer:R(q('footer')),main:R(q('main'))}}); })"""
            info["layout"] = {}; GAP = 8
            for name, w, h, dpr, mobile, ins, inset in SIZES:
                ed.call("Emulation.setDeviceMetricsOverride", {"width": w, "height": h, "deviceScaleFactor": dpr, "mobile": mobile})
                ed.call("Emulation.setTouchEmulationEnabled", {"enabled": mobile})
                ed.call("Emulation.setSafeAreaInsetsOverride", {"insets": ins or {"top": 0, "bottom": 0, "left": 0, "right": 0}})
                got = {}
                for which, path in (("bare", "__bare.html"), ("page", "")):
                    ed.navigate(url + path); ed.wait_for("document.readyState==='complete'", 30); got[which] = json.loads(ed.js(MEASURE))
                g, b0 = got["page"], got["bare"]; btn = g["btn"]
                same = all(len(g["text"][k]) == len(b0["text"][k]) and all(abs(x - y) < 0.01 for r1, r2 in zip(g["text"][k], b0["text"][k]) for x, y in zip(r1, r2)) for k in g["text"]) and all(abs(x - y) < 0.01 for k in g["box"] for x, y in zip(g["box"][k], b0["box"][k]))
                top, right = btn[1], g["vw"] - btn[2]; want_top = inset + ((ins or {}).get("top", 0)); want_right = inset + ((ins or {}).get("right", 0))
                # each text's distance from the button: beside it, or above / below it at every scroll position (the text moves up by up to
                # maxs, the fixed button stays): the smallest over every line of every text
                maxs = max(0, g["sh"] - g["vh"]); worst = min(max(btn[0] - r_, l - btn[2], t - maxs - btn[3], btn[1] - bt) for rects in g["text"].values() for (l, t, r_, bt) in rects)
                info["layout"][name] = {"button": [round(x, 1) for x in btn], "top": round(top, 1), "right": round(right, 1), "clearance_px": round(worst, 1), "scroll": maxs, "safe_area": ins}
                ok(f"layout {name}: 44 x 44 at top {top:g} px, right {right:g} px (want {want_top}, {want_right}), {worst:.0f} px clear of every text",
                   abs(btn[2] - btn[0] - 44) < 0.01 and abs(btn[3] - btn[1] - 44) < 0.01 and abs(top - want_top) < 0.01 and abs(right - want_right) < 0.01 and worst >= GAP
                   and btn[0] >= 0 and btn[3] <= g["vh"] and g["sw"] <= g["vw"] and g["hit"] is True, info["layout"][name])
                ok(f"layout {name}: every text of the page exactly where it is without the control", same, {"page": g["box"], "bare": b0["box"]})
                if a.shots:
                    os.makedirs(a.shots, exist_ok=True); open(os.path.join(a.shots, re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-") + ".png"), "wb").write(ed.screenshot())
            if a.shots:   # on, at the first size again (the sounding speaker)
                ed.call("Emulation.setDeviceMetricsOverride", {"width": 1366, "height": 768, "deviceScaleFactor": 1, "mobile": False}); ed.call("Emulation.setTouchEmulationEnabled", {"enabled": False})
                ed.call("Emulation.setSafeAreaInsetsOverride", {"insets": {"top": 0, "bottom": 0, "left": 0, "right": 0}})
                fresh(ed); click(ed); ed.wait_for("__T.starts.length===1", 30); ed.call("Input.dispatchMouseEvent", {"type": "mouseMoved", "x": 600, "y": 600}); time.sleep(0.3)
                open(os.path.join(a.shots, "desktop-1366x768-on.png"), "wb").write(ed.screenshot([1366 - 120, 0, 120, 90]))
                click(ed); time.sleep(0.3); key(ed, "Tab"); key(ed, "Tab"); time.sleep(0.3); open(os.path.join(a.shots, "desktop-1366x768-off-focus.png"), "wb").write(ed.screenshot([1366 - 120, 0, 120, 90]))
    except HarnessError as e:
        print("HARNESS ERROR:", e); ok("harness", False, str(e))
    finally:
        srv.srv.shutdown()
    bad = [n for n, c in res if not c]
    if a.out: json.dump({"results": [{"check": n, "pass": c} for n, c in res], "info": info}, open(a.out, "w", encoding="utf-8"), indent=1)
    print(json.dumps(info, indent=1)[:6000])
    print(f"RESULT: {'PASS' if not bad else 'FAIL'} ({len(res) - len(bad)}/{len(res)})" + (f" failed: {bad}" if bad else "")); return 1 if bad else 0

if __name__ == "__main__":
    sys.exit(main())
