r"""NES Classic Loadouts on the website (headless Edge; built site on 127.0.0.1; no other service: public share links are retired).

Focused scenarios (the storage arithmetic itself is test_storage_model.py; the installation plan is tools/desktop/check_loadouts_app.py):
  E  creation       a new collection has no loadout; loadouts are created only when asked (all games, or chosen games), named in the
                    creation dialog with a suggested, editable name
  D  scope          the NES Classic storage meter exists only in the loadout editor: not in the library, the collection view, the tray,
                    the collection manager, the Prepare dialog or the Share or Export dialog; Back to Collection removes it
  A  live bytes     adding and removing games moves the meter by the games' own bytes (a large game more than a small one); switching
                    loadouts shows the other loadout's figure at once
  C  oversized      a loadout larger than the estimated space is kept whole with an exceed warning, and survives a restart
  B  independence   loadouts of a 1,200-game collection keep their own games and HOME theme across switching and a restart
  F  artwork        a complete .ccs package (synthetic sentinel ROMs: eight Game Library versions with catalog covers, a personal game
                    with the user's own artwork, one without art) exported through Share or Export and imported in a clean browser
                    with none of the files: catalog covers and the personal artwork thumbnail load, the game without art falls back;
                    every game and the loadout kept; no request leaves this computer, none carries ROM bytes
  N  navigation     Preview on TV sits directly before My Collections in the primary row (collection and loadout views, desktop and phone)
Screenshots of the collection view and the loadout editor at 1440 and 390 px are written to --shots.
  python test_loadouts_web.py <built site folder> [--shots DIR]      Exit 0 = PASS."""
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
    srv = http.server.ThreadingHTTPServer(("127.0.0.1", free_port()), functools.partial(Q, directory=folder)); srv.handle_error = lambda *a: None
    threading.Thread(target=srv.serve_forever, daemon=True).start(); return srv

READY = "document.documentElement.dataset.ready==='1'&&typeof CS!=='undefined'&&!!CS&&typeof loUi==='function'&&document.readyState==='complete'"
CALM = "(()=>{ try{ if($('wEvery')) $('wEvery').checked=false; doneWelcome(); }catch(e){} document.querySelectorAll('#welcome').forEach(m=>m.classList.remove('open')); return 1; })()"
# where storage could appear: a visible meter, or visible text about the console's storage
METER_VISIBLE = "(()=>{ const vis=el=>!!el&&el.offsetParent!==null&&getComputedStyle(el).visibility!=='hidden'; const m=[...document.querySelectorAll('.lometer,#storage,.meter,[role=meter]')].filter(vis); const t=[...document.querySelectorAll('#collBody,#traySub,#stats,.toolbar,#collMenu,#loMenu')].filter(vis).some(el=>/estimated (space|to fit)|MB available|NES Classic Storage|Will it fit/i.test(el.innerText)); return m.length>0||t; })()"
PERSONAL = r"""(()=>{ const c=document.createElement('canvas'); c.width=60; c.height=84; const x=c.getContext('2d'); x.fillStyle='#3a6'; x.fillRect(0,0,60,84); const own=c.toDataURL('image/png');
 const P=(id,title,art)=>{ const e={id, content_sha256:id.slice(2).padEnd(64,'a'), file_sha256:id.slice(2).padEnd(64,'b'), content_size:262160, file_size:262176, format:'nes', file_name:title+'.nes', origin:'file', user:{title, year:'2024', publisher:'Homebrew Team'}, detected:{title}, file_present:true}; PERSONAL[id]=e; const r=perRow(e); if(art) r.art=art; byId[id]=r; return id; };
 return JSON.stringify([P('p_1111222233334444','My Homebrew Quest', own), P('p_5555666677778888','Translated Hack (T-En)', null)]); })()"""
# ---- complete .ccs packages in the page (shared by test_ccs_web, test_collections_web, test_collection_integrity; the synthetic sentinel
#      ROMs themselves are mkRom in test_ccs_v2_web.ROMS, the request recorder is test_ccs_v2_web.NET) ----
# ccsExportUi(cid, files): Export .ccs as a person does it: the Share or Export dialog checks the ROM files, Export .ccs, the ROM
# acknowledgment (CCS_EXPORT_ACK) is confirmed, the save is captured instead of downloaded (window.__pkg holds the package as a File).
# Returns the dialog state; when Export is blocked nothing is built. ccsLib(row, i): a Game Library version made from a synthetic ROM (the
# library id is the content SHA-256[:16], so a real cover row is reused under the synthetic identity, as both sides' Game Library would hold
# it). ccsNamed(x, title, art, extra): retitle an mkRom personal game (and give it artwork). ccsB64(blob): bytes for Python.
CCS_EXPORT_UI = r"""(()=>{ const wait=async(f,ms)=>{ const n=(ms||30000)/100; for(let i=0;i<n;i++){ const v=f(); if(v) return v; await new Promise(r=>setTimeout(r,100)); } return null; };
 window.ccsExportUi=async(cid, files)=>{ window.__pkg=null; window.__saved=null; ccsSaveFile=async(n,b)=>{ window.__saved={name:n, blob:b}; return {ok:true, name:n}; };
  if(files){ ROMSRC.clear(); await ccsAddRomSources(files); }
  try{ closeColl(); }catch(e){} $('collBody').innerHTML=''; ccsShareDialog(cid); const exp=await wait(()=>$('ccsExp'));
  const st={title:$('collTitle').textContent, missing:CCS.missing.slice(), ready:CCS.ready.length, ids:CCS.ids.length, exp:!exp||exp.disabled, send:!$('ccsSend')||$('ccsSend').disabled, pick:!!$('ccsPick'), text:$('collBody').innerText};
  if(!exp||exp.disabled) return st;
  exp.click(); const ack=await wait(()=>$('ccsAck')&&$('ccsAck').classList.contains('open')&&$('ccsAck')); if(!ack) return st;
  st.ack=ack.querySelector('h2').textContent; $('ccsAckC').click(); [...ack.querySelectorAll('button')].find(b=>b.textContent==='Export with ROMs').click();
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

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--shots", default=""); a = ap.parse_args()
    shots = a.shots or os.path.join(tempfile.gettempdir(), "ccs-loadout-shots"); os.makedirs(shots, exist_ok=True)
    checks = []
    def ok(k, v, n=""): checks.append((k, bool(v))); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:400]})" if n and not v else ""), flush=True)
    def shot(ed, name): open(os.path.join(shots, name + ".png"), "wb").write(ed.screenshot())
    def J(ed, expr):
        v = ed.js(expr); return json.loads(v) if isinstance(v, str) else v
    from test_ccs_v2_web import NET, ROMS   # here, not at the top: test_ccs_v2_web imports this module
    web = serve(a.site); WEB = f"http://127.0.0.1:{web.server_address[1]}"
    p1 = tempfile.mkdtemp(prefix="ccs-lo-a-"); p2 = tempfile.mkdtemp(prefix="ccs-lo-b-")
    URL = f"{WEB}/index.html?companion={free_port()}"   # a loopback port nothing listens on: website mode, whatever runs on this computer
    def load(ed): ed.navigate(URL); ed.wait_for(READY, timeout=120); time.sleep(0.8); ed.js(CALM)
    nets = []
    try:
        with EdgeSession(width=1440, height=900, profile=p1) as ed:
            ed.navigate(WEB + "/robots.txt"); ed.js("localStorage.setItem('studio-welcomed-v1','1'); 1"); load(ed)
            # ================= E: creation and naming =================
            ed.js("(()=>{ csNew('First Collection'); const ids=DATA.games.filter(g=>!g.builtin_code&&g.payload_size>0).slice(10,35).map(g=>g.game_id); ids.forEach(i=>sel.add(i)); persist(); render(); csSaveActiveNow(); return 1; })()")
            ok("E_new_collection_has_no_loadout", ed.js("csActive().loadouts.length===0&&!LO&&csList().every(r=>(r.loadouts||[]).length===0)"))
            ok("D_no_meter_in_library_or_collection_view", ed.js(METER_VISIBLE) is False)
            ed.js("$('trayBtn').click()"); time.sleep(0.3); ok("D_no_meter_in_the_collection_tray", ed.js(METER_VISIBLE) is False); ed.js("$('trayClose').click()")
            ed.js("csManager()"); time.sleep(0.3); ok("D_no_meter_in_the_collection_manager", ed.js(METER_VISIBLE) is False); ed.js("closeColl()")
            shot(ed, "1-collection-view-1440")
            ed.js("$('loBtn').click()"); time.sleep(0.2); ed.js("document.querySelector('#loMenu [data-lo-new]').click()"); ed.wait_for("!!$('loGo')", timeout=10)
            st = J(ed, "JSON.stringify({name:$('loNm').value, focused:document.activeElement===$('loNm'), meter:" + METER_VISIBLE + "})")
            ok("E_name_asked_with_an_editable_suggestion", st["name"] == "My NES Classic Setup" and st["focused"], st)
            ok("D_no_meter_in_the_prepare_dialog", st["meter"] is False)
            ed.js("$('loNm').value='Family Favorites'; $('loGo').click()"); time.sleep(0.4)
            st = J(ed, "JSON.stringify({name:loCur()&&loCur().name, n:sel.size})")
            ok("E_all_games_loadout_named_by_the_user", st == {"name": "Family Favorites", "n": 25}, st)
            ok("D_meter_shown_in_the_loadout_editor", ed.js("!!$('loMeter').offsetParent&&$('loUse').textContent.includes('used')&&$('loUse').textContent.includes('available')&&$('loStat').textContent.length>3"))
            shot(ed, "2-loadout-editor-1440")
            # ================= A: live, byte-based =================
            small, big = J(ed, "JSON.stringify((()=>{ const c=DATA.games.filter(g=>!g.builtin_code&&g.recommended_emulator==='kachikachi'&&g.payload_size>0&&!csCollSel().has(g.game_id)); c.sort((a,b)=>a.payload_size-b.payload_size); return [c[0].game_id, c[c.length-1].game_id]; })())")
            ed.js(f"loClose(true); sel.add('{small}'); sel.add('{big}'); persist(); csSaveActiveNow(); loOpen(csActive().loadouts[0].id, true)")
            def used(): return J(ed, "JSON.stringify((()=>{ const e=loStorage(); return {used:e.used, rom:e.rom, files:e.files, home:e.home, comp:e.components, margin:e.margin, txt:$('loUse').textContent}; })())")
            u0 = used()
            ed.js(f"toggleSel('{small}', true)"); u1 = used()
            ed.js(f"toggleSel('{big}', true)"); u2 = used()
            sz = J(ed, f"JSON.stringify([byId['{small}'].payload_size+16, byId['{big}'].payload_size+16])")
            ok("A_meter_rises_by_each_games_own_bytes", u1["rom"] - u0["rom"] == sz[0] and u2["rom"] - u1["rom"] == sz[1] and u2["used"] - u1["used"] > u1["used"] - u0["used"] and u0["txt"] != u2["txt"], (sz, u0["used"], u1["used"], u2["used"]))
            ed.js(f"toggleSel('{big}', false)"); u3 = used()
            ok("A_meter_falls_when_a_game_is_removed", u3["used"] == u1["used"], (u3["used"], u1["used"]))
            ed.js(f"toggleSel('{small}', false)")
            ok("A_editor_figure_is_the_shared_model", u0["used"] == u0["rom"] + u0["files"] + u0["home"] + sum(u0["comp"].values()) + u0["margin"])
            # ================= C: oversized =================
            ed.js("loClose(true)"); ok("D_back_to_collection_removes_the_meter", ed.js(METER_VISIBLE) is False)
            ed.js(r"""(()=>{ csNew('Complete NES Library'); const ids=DATA.games.map(g=>g.game_id).concat(EXT_ROWS.map(g=>g.game_id)).filter((v,i,a)=>a.indexOf(v)===i).slice(0,1200); ids.forEach(i=>sel.add(i)); persist(); render(); csSaveActiveNow(); return 1; })()""")
            ed.js("loPrepare({newOnly:true, mode:'all'})"); ed.wait_for("!!$('loGo')", timeout=10); ed.js("$('loNm').value='My Ultimate NES Setup'; $('loGo').click()"); time.sleep(0.5)
            st = J(ed, "JSON.stringify({n:sel.size, st:loStorage().status, stat:$('loStat').textContent, cls:$('loMeter').className})")
            ok("C_oversized_loadout_kept_whole_with_an_exceed_warning", st["n"] == 1200 and st["st"] == "over" and "exceed available space by" in st["stat"] and "bad" in st["cls"], st)
            shot(ed, "3-oversized-loadout-1440")
            # ================= B: independent loadouts =================
            B = J(ed, r"""JSON.stringify((()=>{ loClose(true); const ids=csActive().sel.slice();
              const mk=(name, part, theme)=>{ loPrepare({newOnly:true, mode:'choose'}); $('loNm').value=name; $('loGo').click(); part.forEach(i=>sel.add(i)); persist(); localStorage.setItem(HOME_KEY, JSON.stringify({v:1, theme, folders:[], favorites:{items:{}}, history:{}})); csSaveActiveNow(); const id=LO.lid; loClose(true); return id; };
              return {L:[mk('Weekend Multiplayer', ids.slice(20,62), 'arcade-grid'), mk('RPG Collection', ids.slice(300,328), 'adventure-map')], b:ids.slice(20,62), c:ids.slice(300,328)}; })())""")
            want = {B["L"][0]: (B["b"], "arcade-grid"), B["L"][1]: (B["c"], "adventure-map")}
            seen = []
            for lid in (B["L"][1], B["L"][0], B["L"][1]):
                ed.js(f"loOpen('{lid}', true)"); seen.append((lid, J(ed, "JSON.stringify({sel:[...sel], theme:homeModelFromStudio().theme, used:loStorage().used})")))
            ok("B_switching_shows_each_loadouts_games_HOME_and_storage", all(s["sel"] == want[l][0] and s["theme"] == want[l][1] for l, s in seen) and seen[0][1]["used"] != seen[1][1]["used"], [(len(s["sel"]), s["theme"]) for l, s in seen])
            ed.js("csSaveActiveNow()")
        with EdgeSession(width=1440, height=900, profile=p1) as ed:
            load(ed)
            st = J(ed, "JSON.stringify({lo:LO&&loCur().name, L:csActive().loadouts.map(l=>[l.name,l.sel.length]), coll:csCollSel().size})")
            ok("B_C_after_restart_every_loadout_exact", st["coll"] == 1200 and st["L"] == [["My Ultimate NES Setup", 1200], ["Weekend Multiplayer", 42], ["RPG Collection", 28]] and st["lo"] == "RPG Collection", st)
            # ================= F: artwork through a complete .ccs package; the recipient has none of the files =================
            ed.js("loClose(true)"); ed.js(NET); ed.js(ROMS); ed.js(CCS_EXPORT_UI)
            F = J(ed, r"""(async()=>{ const rows=DATA.games.filter(g=>g.art&&!g.builtin_code).slice(0,8); const lib=[]; for(let i=0;i<8;i++) lib.push(await ccsLib(rows[i], i));
              const own=ccsNamed(await mkRom('own', true), 'My Homebrew Quest', ccsArt('#3a6')), plain=ccsNamed(await mkRom('plain', false), 'Translated Hack (T-En)', null);
              csNew('Share Art'); lib.forEach(x=>sel.add(x.id)); sel.add(own.id); sel.add(plain.id); persist(); render(); csSaveActiveNow();
              loPrepare({newOnly:true, mode:'all'}); $('loNm').value='Party'; $('loGo').click(); loClose(true); csSaveActiveNow();
              window.__F={files:lib.map(x=>x.file).concat([own.file, plain.file])}; return JSON.stringify({rows:lib.map(x=>x.row), ids:csActive().sel.slice(), own:own.id, plain:plain.id}); })()""")
            ed.js("closeColl(); ccsShareDialog()"); ed.wait_for("!!$('ccsExp')", timeout=30); time.sleep(0.5)
            ok("D_no_meter_in_the_share_or_export_dialog", ed.js(METER_VISIBLE) is False)
            st = J(ed, "(async()=>JSON.stringify(await ccsExportUi(CS.active, __F.files)))()")
            ok("F_complete_package_exported_through_share_or_export", st.get("ready") == 10 and st.get("ack") == "Export Collection with ROM Files" and (st.get("name") or "").endswith(".ccs"), st)
            pkg = page_bytes(ed, "window.__pkg"); nets.append(net_take(ed))
        with EdgeSession(width=1440, height=900, profile=p2) as ed:
            ed.navigate(WEB + "/robots.txt"); ed.js("localStorage.setItem('studio-welcomed-v1','1'); 1"); load(ed); ed.js(NET)
            ed.js(f"{json.dumps(F['rows'])}.forEach(r=>{{ byId[r.game_id]=r; }}); 1")   # the recipient's Game Library knows the eight versions; the personal games are not here
            page_file(ed, pkg, "Share Art.ccs", "__P"); ed.js("ccsOpenFile(__P)"); ed.wait_for("!!$('ccsGo')", timeout=60)
            ok("F_recipient_package_verified", "10 verified" in ed.js("$('collBody').innerText"), ed.js("$('collBody').innerText"))
            ed.js("$('ccsGo').click()"); ed.wait_for("!!$('ccsOpen')", timeout=30); ed.js("$('ccsOpen').click()"); time.sleep(0.5)
            ed.js("render(); $('trayBtn').click(); document.querySelectorAll('#trayList img').forEach(i=>{ i.loading='eager'; }); 1")
            try: ed.wait_for("[...document.querySelectorAll('#trayList img')].every(i=>i.complete)", timeout=30)
            except Exception: pass
            st = J(ed, "JSON.stringify({img:[...document.querySelectorAll('#trayList img')].map(i=>({src:i.getAttribute('src').slice(0,60), w:i.naturalWidth})), ph:document.querySelectorAll('#trayList .ph').length})")
            loaded = [x for x in st["img"] if x["w"] > 0]
            covers = [x for x in loaded if x["src"].startswith("art/") and x["src"].endswith(".jpg")]; thumbs = [x for x in loaded if x["src"].startswith("data:image/")]
            ok("F_recipient_sees_catalog_covers_personal_thumbnails_and_fallback_art", len(loaded) == 9 and len(covers) == 8 and len(thumbs) == 1 and st["ph"] == 1 and not any("8765" in x["src"] or "/personal" in x["src"] for x in st["img"]), st)
            shot(ed, "4-ccs-package-received")
            ed.js("$('trayClose').click()")
            ok("F_saved_copy_keeps_every_game_and_the_loadout", J(ed, "JSON.stringify(csActive().sel)") == F["ids"] and ed.js("csActive().loadouts.length===1&&csActive().loadouts[0].name==='Party'&&csActive().loadouts[0].sel.length===10"))
            nets.append(net_take(ed))
        rep = net_report(nets)
        ok("F_no_request_left_this_computer", not rep["remote"] and not rep["share"], rep)
        ok("F_no_request_carried_rom_bytes", not rep["rom"], rep["rom"][:3])
        # ================= N: Preview on TV placement; screenshots at phone width =================
        for w, h, sc in ((1440, 900, 1), (390, 844, 3)):
            with EdgeSession(width=w, height=h, scale=sc, profile=p1) as ed:
                load(ed); ed.js("loClose(true); csSwitch(csList().find(r=>r.name==='First Collection').id, true); scrollTo(0,0)"); time.sleep(0.3)
                pos = "JSON.stringify((()=>{ const t=$('tvBtn').getBoundingClientRect(), c=$('collBtn').getBoundingClientRect(); return {tv:!$('tvBtn').hidden, row:$('tvBtn').parentElement.classList.contains('bar'), before:!!($('tvBtn').compareDocumentPosition($('collWrap'))&4), left:t.right<=c.left+1, mid:Math.abs((t.top+t.bottom)/2-(c.top+c.bottom)/2)}; })())"
                st = J(ed, pos); ok(f"N_preview_on_tv_before_my_collections_{w}", st["tv"] and st["row"] and st["before"] and st["left"] and st["mid"] < 12, st)
                ok(f"N_no_horizontal_scroll_{w}", ed.js("document.documentElement.scrollWidth<=innerWidth+1"))
                if w == 390: shot(ed, "5-collection-view-390")
                ed.js("loOpen(csActive().loadouts[0].id, true); scrollTo(0,0)"); time.sleep(0.4)
                st = J(ed, pos); ok(f"N_preview_on_tv_placement_kept_in_the_loadout_editor_{w}", st["tv"] and st["left"], st)
                if w == 390: shot(ed, "6-loadout-editor-390")
    finally:
        web.shutdown()
        for p in (p1, p2): cdp_edge.reap_profile(p) if hasattr(cdp_edge, "reap_profile") else shutil.rmtree(p, ignore_errors=True)
    bad = [k for k, v in checks if not v]
    print("RESULT:", "PASS" if not bad else "FAIL " + json.dumps(bad), f"({len(checks)} checks)"); print("screenshots:", shots); return 0 if not bad else 1

if __name__ == "__main__": sys.exit(main())
