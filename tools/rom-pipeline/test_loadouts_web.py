r"""NES Classic Loadouts on the website (headless Edge; built site on 127.0.0.1; the share Worker locally with in-memory storage).

Focused scenarios (the storage arithmetic itself is test_storage_model.py; the installation plan is tools/desktop/check_loadouts_app.py):
  E  creation       a new collection has no loadout; loadouts are created only when asked (all games, or chosen games), named in the
                    creation dialog with a suggested, editable name
  D  scope          the NES Classic storage meter exists only in the loadout editor: not in the library, the collection view, the tray,
                    the collection manager, the Prepare dialog or the share dialog; Back to Collection removes it
  A  live bytes     adding and removing games moves the meter by the games' own bytes (a large game more than a small one); switching
                    loadouts shows the other loadout's figure at once
  C  oversized      a loadout larger than the estimated space is kept whole with an exceed warning, and survives a restart
  B  independence   loadouts of a 1,200-game collection keep their own games and HOME theme across switching and a restart
  F  artwork        a recipient of a shared collection sees its catalog covers and the thumbnails of personal artwork (images load)
  N  navigation     Preview on TV sits directly before My Collections in the primary row (collection and loadout views, desktop and phone)
Screenshots of the collection view and the loadout editor at 1440 and 390 px are written to --shots.
  python test_loadouts_web.py <built site folder> [--shots DIR]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, functools, http.server, json, os, shutil, socket, subprocess, sys, tempfile, threading, time, urllib.error, urllib.request
HERE = os.path.dirname(os.path.abspath(__file__)); TOOLS = os.path.dirname(HERE); sys.path.insert(0, HERE)
import cdp_edge
from cdp_edge import EdgeSession as _Edge

class EdgeSession(_Edge):
    """a session with a persistent profile, so a browser restart keeps local storage"""
    def __init__(self, profile=None, **kw):
        super().__init__(**kw); self._p = profile
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

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--shots", default=""); a = ap.parse_args()
    shots = a.shots or os.path.join(tempfile.gettempdir(), "ccs-loadout-shots"); os.makedirs(shots, exist_ok=True)
    checks = []
    def ok(k, v, n=""): checks.append((k, bool(v))); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:400]})" if n and not v else ""), flush=True)
    def shot(ed, name): open(os.path.join(shots, name + ".png"), "wb").write(ed.screenshot())
    def J(ed, expr):
        v = ed.js(expr); return json.loads(v) if isinstance(v, str) else v
    web = serve(a.site); WEB = f"http://127.0.0.1:{web.server_address[1]}"
    sport = free_port(); SHARE = f"http://127.0.0.1:{sport}"
    dev = subprocess.Popen([shutil.which("node"), os.path.join(TOOLS, "share-worker", "test", "dev-server.mjs"), str(sport), WEB], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    p1 = tempfile.mkdtemp(prefix="ccs-lo-a-"); p2 = tempfile.mkdtemp(prefix="ccs-lo-b-")
    URL = f"{WEB}/index.html?shareapi={SHARE}"
    def load(ed): ed.navigate(URL); ed.wait_for(READY, timeout=120); time.sleep(0.8); ed.js(CALM)
    try:
        for _ in range(40):
            try: urllib.request.urlopen(SHARE + "/c/none", timeout=1)
            except urllib.error.HTTPError: break
            except Exception: time.sleep(0.25)
        with EdgeSession(width=1440, height=900, profile=p1) as ed:
            ed.navigate(WEB + "/robots.txt"); ed.js("localStorage.setItem('studio-welcomed-v1','1'); 1"); load(ed)
            # ================= E: creation and naming =================
            ed.js("(()=>{ csNew('First Collection'); const ids=DATA.games.filter(g=>!g.builtin_code&&g.payload_size>0).slice(10,35).map(g=>g.game_id); ids.forEach(i=>sel.add(i)); persist(); render(); csSaveActiveNow(); return 1; })()")
            ok("E_new_collection_has_no_loadout", ed.js("csActive().loadouts.length===0&&!LO&&csList().every(r=>(r.loadouts||[]).length===0)"))
            ok("D_no_meter_in_library_or_collection_view", ed.js(METER_VISIBLE) is False)
            ed.js("$('trayBtn').click()"); time.sleep(0.3); ok("D_no_meter_in_the_collection_tray", ed.js(METER_VISIBLE) is False); ed.js("$('trayClose').click()")
            ed.js("csManager()"); time.sleep(0.3); ok("D_no_meter_in_the_collection_manager", ed.js(METER_VISIBLE) is False); ed.js("closeColl()")
            shot(ed, "1-collection-view-1440")
            ed.js("$('loBtn').click()"); time.sleep(0.2); ed.js("document.querySelector('#loMenu [data-lo-new=all]').click()"); ed.wait_for("!!$('loGo')", timeout=10)
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
            # ================= F: shared artwork, recipient without any files =================
            ed.js("loClose(true)"); pids = J(ed, PERSONAL)
            ed.js(f"(()=>{{ csNew('Share Art'); DATA.games.filter(g=>g.art).slice(0,8).forEach(g=>sel.add(g.game_id)); {json.dumps(pids)}.forEach(i=>sel.add(i)); persist(); render(); csSaveActiveNow(); loPrepare({{newOnly:true, mode:'all'}}); $('loNm').value='Party'; $('loGo').click(); loClose(true); return 1; }})()")
            ed.js("openShare()"); ed.wait_for("!!$('shGo')&&!$('shGo').disabled", timeout=30); time.sleep(1.0)
            ok("D_no_meter_in_the_share_dialog", ed.js(METER_VISIBLE) is False)
            ed.js("$('shGo').click()"); ed.wait_for("!!(SH.pub&&SH.pub.url)||!!document.querySelector('#shOut .bad')", timeout=60); ok("F_published", ed.js("!!(SH.pub&&SH.pub.url)"), ed.js("$('shOut').textContent")); share_id = ed.js("SH.pub&&SH.pub.id")
        with EdgeSession(width=1440, height=900, profile=p2) as ed:
            ed.navigate(WEB + "/robots.txt"); ed.js("localStorage.setItem('studio-welcomed-v1','1'); 1")
            ed.navigate(f"{URL}&share={share_id}"); ed.wait_for(READY, timeout=120); ed.js(CALM); ed.wait_for("!!$('srSave')", timeout=30)
            try: ed.wait_for("[...document.querySelectorAll('.shg img')].every(i=>i.complete)", timeout=30)
            except Exception: pass
            st = J(ed, "JSON.stringify([...document.querySelectorAll('.shg img')].map(i=>({src:i.getAttribute('src'), w:i.naturalWidth})))")
            loaded = [x for x in st if x["w"] > 0]
            ok("F_recipient_sees_catalog_covers_personal_thumbnails_and_fallback_art", len(loaded) == 9 and any("/t/0.jpg" in x["src"] for x in loaded) and not any("8765" in x["src"] for x in st) and ed.js("document.querySelectorAll('.shg .shph').length") == 1, st)
            shot(ed, "4-shared-collection-received")
            ed.js("$('srSave').click()"); ed.wait_for("!!$('srOpen')", timeout=10); ed.js("$('srOpen').click()"); time.sleep(0.4)
            ok("F_saved_copy_keeps_every_game_and_the_loadout", ed.js("csActive().sel.length===10&&csActive().loadouts.length===1&&csActive().loadouts[0].sel.length===10"))
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
        dev.terminate()
        for p in (p1, p2): cdp_edge.reap_profile(p) if hasattr(cdp_edge, "reap_profile") else shutil.rmtree(p, ignore_errors=True)
    bad = [k for k, v in checks if not v]
    print("RESULT:", "PASS" if not bad else "FAIL " + json.dumps(bad), f"({len(checks)} checks)"); print("screenshots:", shots); return 0 if not bad else 1

if __name__ == "__main__": sys.exit(main())
