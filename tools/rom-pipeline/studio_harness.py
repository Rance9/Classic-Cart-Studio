r"""Shared helpers for the website tests (headless Edge through the private cdp_edge module; the built site served on 127.0.0.1):
EdgeSession with a persistent profile, serve(), READY/CALM, the synthetic personal games (PERSONAL), the complete-.ccs backup driver
(CCS_EXPORT_UI) and the request recorder readers. Moved here from test_loadouts_web.py when lineups were retired."""
from __future__ import annotations
import argparse, base64, functools, http.server, json, os, shutil, socket, sys, tempfile, threading, time
HERE = os.path.dirname(os.path.abspath(__file__)); TOOLS = os.path.dirname(HERE); sys.path.insert(0, HERE)
import cdp_edge
from cdp_edge import EdgeSession as _Edge

class EdgeSession(_Edge):
    """a session with a persistent profile, so a browser restart keeps local storage"""
    def __init__(self, profile=None, **kw):
        super().__init__(**{"start_timeout": 120.0, **kw}); self._p = profile
    def start(self):
        orig = cdp_edge.tempfile.mkdtemp; cdp_edge.tempfile.mkdtemp = (lambda *a, **k: self._p) if self._p else orig
        try: super().start()
        finally: cdp_edge.tempfile.mkdtemp = orig
    def close(self):
        orig = cdp_edge.shutil.rmtree; cdp_edge.shutil.rmtree = (lambda *a, **k: None) if self._p else orig
        try: super().close()
        finally: cdp_edge.shutil.rmtree = orig

def free_port():
    with socket.socket() as s: s.bind(("127.0.0.1", 0)); return s.getsockname()[1]

def serve(folder):
    class Q(http.server.SimpleHTTPRequestHandler):
        def log_message(self, *a): pass
        def handle(self):
            try: super().handle()
            except Exception: pass
    class S(http.server.ThreadingHTTPServer): request_queue_size = 256   # the page's service worker asks for ~200 files at once: the default backlog of 5 drops some
    srv = S(("127.0.0.1", free_port()), functools.partial(Q, directory=folder)); srv.handle_error = lambda *a: None
    threading.Thread(target=srv.serve_forever, daemon=True).start(); return srv

READY = "document.documentElement.dataset.ready==='1'&&typeof CS!=='undefined'&&!!CS&&typeof cvPanel==='function'&&document.readyState==='complete'"
# a new profile prepares the Included Games once and opens them: SETTLED waits for that; NEW_COLL then opens an empty collection of
# its own, the baseline earlier versions started with (their single "My Collection")
SETTLED = "CS_STATE!=='ok'||!(CS.starter&&CS.starter.state!=='done'&&CS.starter.state!=='failed')"
NEW_COLL = "(()=>{ const r=csCreate('My Collection'); csSwitch(r.id, true); return 1; })()"
CALM = "(()=>{ try{ if($('wEvery')) $('wEvery').checked=false; doneWelcome(); }catch(e){} document.querySelectorAll('#welcome').forEach(m=>m.classList.remove('open')); return 1; })()"
PERSONAL = r"""(()=>{ const c=document.createElement('canvas'); c.width=60; c.height=84; const x=c.getContext('2d'); x.fillStyle='#3a6'; x.fillRect(0,0,60,84); const own=c.toDataURL('image/png');
 const P=(id,title,art)=>{ const e={id, content_sha256:id.slice(2).padEnd(64,'a'), file_sha256:id.slice(2).padEnd(64,'b'), content_size:262160, file_size:262176, format:'nes', file_name:title+'.nes', origin:'file', user:{title, year:'2024', publisher:'Homebrew Team'}, detected:{title}, file_present:true}; PERSONAL[id]=e; const r=perRow(e); if(art) r.art=art; byId[id]=r; return id; };
 return JSON.stringify([P('p_1111222233334444','My Homebrew Quest', own), P('p_5555666677778888','Translated Hack (T-En)', null)]); })()"""
# ---- complete .ccs packages in the page (shared by test_ccs_web, test_collections_web, test_collection_integrity; the synthetic sentinel
#      ROMs themselves are mkRom in test_ccs_v2_web.ROMS, the request recorder is test_ccs_v2_web.NET) ----
# ccsExportUi(cid, files): Back Up Collection as a person does it: the dialog checks the ROM files, Create Backup, the rights
# acknowledgment (CCS_EXPORT_ACK) is confirmed, the save is captured instead of downloaded (window.__pkg holds the package as a File).
# Returns the dialog state; when Export is blocked nothing is built. ccsLib(row, i): a Game Library version made from a synthetic ROM (the
# library id is the content SHA-256[:16], so a real cover row is reused under the synthetic identity, as both sides' Game Library would hold
# it). ccsNamed(x, title, art, extra): retitle an mkRom personal game (and give it artwork). ccsB64(blob): bytes for Python.
CCS_EXPORT_UI = r"""(()=>{ const wait=async(f,ms)=>{ const n=(ms||30000)/100; for(let i=0;i<n;i++){ const v=f(); if(v) return v; await new Promise(r=>setTimeout(r,100)); } return null; };
 window.ccsExportUi=async(cid, files)=>{ window.__pkg=null; window.__saved=null; ccsSaveFile=async(n,b)=>{ window.__saved={name:n, blob:b}; return {ok:true, name:n}; };
  if(files){ ROMSRC.clear(); await ccsAddRomSources(files); }
  try{ closeColl(); }catch(e){} $('collBody').innerHTML=''; ccsBackupDialog(cid); const exp=await wait(()=>$('ccsExp'));
  const st={title:$('collTitle').textContent, missing:CCS.missing.slice(), ready:CCS.ready.length, ids:CCS.ids.length, exp:!exp||exp.disabled, send:!$('ccsSend')||$('ccsSend').disabled, pick:!!$('ccsPick'), text:$('collBody').innerText};
  if(!exp||exp.disabled) return st;
  exp.click(); const ack=await wait(()=>$('ccsAck')&&$('ccsAck').classList.contains('open')&&$('ccsAck')); if(!ack) return st;
  st.ack=ack.querySelector('h2').textContent; $('ccsAckC').click(); ack.querySelector('button.hk').click();   /* the acknowledgment's own primary action (Save Backup File) */
  const s=await wait(()=>window.__saved, 600000); if(!s) return st; window.__pkg=new File([s.blob], s.name, {type:s.blob.type}); st.name=s.name; st.size=s.blob.size; return st; };
 window.ccsLib=async(row, i)=>{ const x=await mkRom('lib'+i, true); delete PERSONAL[x.id]; delete byId[x.id]; const id=x.id.slice(2);
  const r=Object.assign({}, row, {game_id:id, title_id:'tt_ccs_'+id, builtin_code:'', payload_size:16384}); byId[id]=r; return {id, row:r, file:x.file, title:r.display_title}; };
 window.ccsNamed=(x, title, art, extra)=>{ const e=PERSONAL[x.id]; e.user.title=title; e.detected.title=title; Object.assign(e, extra||{}); if(extra&&extra.version) e.user.version_label=extra.version; const r=perRow(e); if(art) r.art=art; byId[x.id]=r; x.title=title; return x; };
 window.ccsArt=(color)=>{ const c=document.createElement('canvas'); c.width=60; c.height=84; const g=c.getContext('2d'); g.fillStyle=color||'#3a6'; g.fillRect(0,0,60,84); g.fillStyle='#fff'; g.fillRect(10,10,40,20); return c.toDataURL('image/png'); };
 window.ccsB64=async b=>{ const u8=new Uint8Array(await b.arrayBuffer()); let s=''; for(let i=0;i<u8.length;i+=0x8000) s+=String.fromCharCode.apply(null,u8.subarray(i,i+0x8000)); return btoa(s); };
 return 1; })()"""
SHARE_HOST = "share.classiccartstudio.com"
NET_READ = "JSON.stringify({net:(window.__net||[]).map(x=>({kind:x.kind, url:x.url, bytes:x.bytes, sentinel:x.sentinel})), res:performance.getEntriesByType('resource').map(e=>e.name)})"

def page_file(ed, data: bytes, name: str, var: str):
    """put bytes into the page as window[var] = File (the way a person picks a file)"""
    b = base64.b64encode(data).decode()
    ed.js(f"(()=>{{ const s=atob('{b}'); const u=new Uint8Array(s.length); for(let i=0;i<s.length;i++) u[i]=s.charCodeAt(i); window.{var}=new File([u], {json.dumps(name)}); return 1; }})()")

def page_bytes(ed, expr: str) -> bytes:
    return base64.b64decode(ed.js(f"ccsB64({expr})"))

def net_take(ed) -> dict:
    """every request the page made (the NET recorder and the browser's resource timing); call before the session closes"""
    time.sleep(0.6); return json.loads(ed.js(NET_READ))

def net_report(nets: list) -> dict:
    urls = [x["url"] for n in nets for x in n["net"]] + [u for n in nets for u in n["res"]]
    local = lambda u: not u.startswith(("http://", "https://")) or u.startswith(("http://127.0.0.1", "http://localhost"))
    return {"hosts": sorted({u.split("/")[2] for u in urls if u.startswith("http")}), "remote": sorted({u.split("/")[2] for u in urls if not local(u)}),
            "share": [u for u in urls if SHARE_HOST in u or "/share-config.json" in u], "rom": [x for n in nets for x in n["net"] if x["sentinel"]],
            "rom_remote": [x for n in nets for x in n["net"] if x["sentinel"] and not local(x["url"])], "body_remote": [x for n in nets for x in n["net"] if x["bytes"] and not local(x["url"])]}
