r"""NES Classic Loadouts on the website (headless Edge; built site on 127.0.0.1; the share Worker locally with in-memory storage).

Representative scenarios, not a regression suite:
  F  migration      a 0.7.0 user's collections, HOME settings, favorites and snapshots are unchanged; each collection with games or
                    HOME settings gains one loadout holding all of them and a copy of the HOME settings; idempotent; recoverable copy kept
  A  first time     a 25-game collection becomes a 25-game loadout in three clicks (no per-game selection); Install works on it;
                    console actions from the collection ask for a loadout first
  B  large          a 1,200-game collection with three independent loadouts (own games, decisions, HOME theme and folders) survives
                    switching and a browser restart exactly; editing one never changes another or the collection; removing a
                    collection game that loadouts use asks first (keep, or remove from the loadouts too)
  C  oversized      a loadout larger than the estimated space is saved whole, the warning says so, nothing disappears
  D  shared         a large collection with personal games and loadouts, published and saved by someone without any game files:
                    every game and every loadout arrives intact (also by file export / import)
  E  preparation    with a loadout open, the installation manifest holds exactly that loadout; collection-level files and the
                    desktop handoff hold the whole collection
  G  header         CLASSIC CART and STUDIO on one baseline at desktop, laptop and phone widths (pixels measured)
Screenshots of the finished views are written to --shots.

  python test_loadouts_web.py <built site folder> [--shots DIR]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, functools, http.server, io, json, os, shutil, socket, subprocess, sys, tempfile, threading, time, urllib.error, urllib.request
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
# a 0.7.0 store: two collections (one with games, decisions and HOME settings, one empty), favorites and a recovery snapshot
PICK = "JSON.stringify({ids:DATA.games.filter(g=>!g.builtin_code).slice(0,5).map(g=>g.game_id), rev:(DATA.games.find(g=>g.builtin_status==='BUILT-IN: DIFFERENT REVISION')||{}).game_id||null})"
SEED = r"""(()=>{ const P=__PICK__; const ids=P.ids.slice(); const rev=P.rev?{game_id:P.rev}:null; if(rev) ids.push(rev.game_id);
 const home={v:1, folders:[{id:'f:a', name:'Racing', parent:null, pos:0, order:ids.slice(0,2)}], theme:'adventure-map', favorites:{items:{[ids[0]]:1}}, history:{}};
 const dec=rev?{[rev.game_id]:'alternate'}:{}; const coll={created:'2026-10-01T10:00:00.000Z', modified:'2026-10-18T10:00:00.000Z', pref:{}, multi:{}, avail:[], placeholders:[], missing:{}};
 const store={schema:'ccs-collections-store/1', active:'c0000000000000a', migrated:{at:'2026-10-18T10:00:00.000Z', from:'new'}, items:{
  c0000000000000a:{id:'c0000000000000a', name:'Old Favorites', created:coll.created, modified:coll.modified, sel:ids, decisions:dec, coll, home, shares:[]},
  c0000000000000b:{id:'c0000000000000b', name:'Empty One', created:coll.created, modified:'2026-10-17T10:00:00.000Z', sel:[], decisions:{}, coll:{}, home:null, shares:[]}}};
 localStorage.clear(); localStorage.setItem('studio-collections-v2', JSON.stringify(store));
 localStorage.setItem('nes-classic-selection-v1', JSON.stringify(ids)); localStorage.setItem('nes-classic-decisions-v1', JSON.stringify(dec)); localStorage.setItem('studio-collection-v1', JSON.stringify(Object.assign({name:'Old Favorites'}, coll))); localStorage.setItem('studio-home-v1', JSON.stringify(home));
 localStorage.setItem('nes-classic-favorites-v1', JSON.stringify(ids.slice(1,3))); localStorage.setItem('studio-snapshots-v1', JSON.stringify([{at:'2026-10-18T09:00:00.000Z', reason:'before something', name:'Old Favorites', sel:ids.slice(0,3), decisions:{}, pref:{}, multi:{}, avail:[], fav:[], baseline:null, fp:null}]));
 localStorage.setItem('studio-welcomed-v1','1'); return JSON.stringify({store, fav:ids.slice(1,3)}); })()"""
# personal games held exactly as the app holds them (no backend on the website), plus their kept metadata for a recipient
PERSONAL = r"""(()=>{ const P=(id,title)=>{ const e={id, content_sha256:id.slice(2).padEnd(64,'a'), file_sha256:id.slice(2).padEnd(64,'b'), content_size:262160, file_size:262176, format:'nes', file_name:'C:/Users/someone/ROMs/'+title+'.nes', origin:'file', user:{title, year:'2024', publisher:'Homebrew Team'}, detected:{title}, file_present:true}; PERSONAL[id]=e; const r=perRow(e); byId[id]=r; return id; };
 return JSON.stringify([P('p_1111222233334444','My Homebrew Quest'), P('p_5555666677778888','Translated Hack (T-En)')]); })()"""

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--shots", default=""); a = ap.parse_args()
    shots = a.shots or os.path.join(tempfile.gettempdir(), "ccs-loadout-shots"); os.makedirs(shots, exist_ok=True)
    checks = []
    def ok(k, v, n=""): checks.append((k, bool(v))); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:400]})" if n and not v else ""), flush=True)
    def shot(ed, name, clip=None): open(os.path.join(shots, name + ".png"), "wb").write(ed.screenshot(clip=clip))
    def J(ed, expr): return json.loads(ed.js(expr))
    web = serve(a.site); WEB = f"http://127.0.0.1:{web.server_address[1]}"
    sport = free_port(); SHARE = f"http://127.0.0.1:{sport}"
    dev = subprocess.Popen([shutil.which("node"), os.path.join(TOOLS, "share-worker", "test", "dev-server.mjs"), str(sport), WEB], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    p1 = tempfile.mkdtemp(prefix="ccs-lo-a-"); p2 = tempfile.mkdtemp(prefix="ccs-lo-b-")
    URL = f"{WEB}/index.html?shareapi={SHARE}"
    try:
        for _ in range(40):
            try: urllib.request.urlopen(SHARE + "/c/none", timeout=1)
            except urllib.error.HTTPError: break
            except Exception: time.sleep(0.25)
        # ================= F: migration of a 0.7.0 user =================
        with EdgeSession(width=1440, height=900, profile=p1) as ed:
            ed.navigate(WEB + "/robots.txt"); ed.js("localStorage.setItem('studio-welcomed-v1','1'); 1"); ed.navigate(URL); ed.wait_for(READY, timeout=120); time.sleep(0.8); ed.js(CALM); pick = ed.js(PICK)
            ed.navigate(WEB + "/robots.txt"); seed = json.loads(ed.js(SEED.replace("__PICK__", pick))); store0 = seed["store"]
            ed.navigate(URL); ed.wait_for(READY, timeout=120); time.sleep(0.8); ed.js(CALM)
            st = J(ed, "JSON.stringify({items:CS.items, mig:CS.loadoutsMigrated, before:JSON.parse(localStorage.getItem('studio-collections-v2.before-loadouts')||'null'), fav:[...fav], snaps:JSON.parse(localStorage.getItem('studio-snapshots-v1')||'[]'), lo:!!LO, sel:[...sel]})")
            A0, B0 = store0["items"]["c0000000000000a"], store0["items"]["c0000000000000b"]; A1, B1 = st["items"]["c0000000000000a"], st["items"]["c0000000000000b"]
            ok("F_collections_unchanged", A1["sel"] == A0["sel"] and A1["decisions"] == A0["decisions"] and A1["home"] == A0["home"] and A1["name"] == "Old Favorites" and B1["sel"] == [] and B1["name"] == "Empty One", A1)
            L = A1.get("loadouts") or []
            ok("F_one_loadout_with_every_game_and_the_HOME_settings", len(L) == 1 and L[0]["sel"] == A0["sel"] and L[0]["decisions"] == A0["decisions"] and L[0]["home"] == A0["home"] and L[0]["target"] == "nes-classic", L)
            ok("F_empty_collection_gets_no_loadout", B1.get("loadouts") == [])
            ok("F_favorites_snapshots_kept_and_recoverable_copy", st["fav"] == seed["fav"] and len(st["snaps"]) == 1 and st["before"] and st["before"]["store"]["items"]["c0000000000000a"]["sel"] == A0["sel"])
            ok("F_opens_in_the_collection", st["lo"] is False and sorted(st["sel"]) == sorted(A0["sel"]))
            ed.navigate(URL); ed.wait_for(READY, timeout=120); time.sleep(0.8); ed.js(CALM)
            ok("F_idempotent", ed.js("(CS.items.c0000000000000a.loadouts||[]).length") == 1)
            # ================= A: first-time user, 25 games =================
            ids25 = J(ed, "JSON.stringify((()=>{ csNew('First Collection'); const ids=DATA.games.filter(g=>!g.builtin_code).slice(10,35).map(g=>g.game_id); ids.forEach(i=>sel.add(i)); persist(); render(); csSaveActiveNow(); return ids; })())")
            ok("A_collection_mode_shows_no_console_actions", ed.js("$('tvBtn').hidden&&$('homeBtn').hidden&&$('hkBtn').hidden&&!$('loWrap').hidden&&$('loBand').hidden&&!LO"))
            ed.js("$('loBtn').click()"); time.sleep(0.3); shot(ed, "A1-collection-loadout-menu")
            ed.js("document.querySelector('#loMenu [data-lo-new=all]').click()"); ed.wait_for("$('collm').classList.contains('open')&&!!$('loGo')", timeout=10); shot(ed, "A2-create-loadout")
            ed.js("$('loGo').click()"); time.sleep(0.5)
            st = J(ed, "JSON.stringify({lo:!!LO, sel:[...sel], coll:[...csCollSel()], band:!$('loBand').hidden, name:loCur()&&loCur().name, fig:$('loFig').textContent, tv:!$('tvBtn').hidden&&!$('homeBtn').hidden&&!$('hkBtn').hidden})")
            ok("A_three_clicks_to_a_25_game_loadout", st["lo"] and sorted(st["sel"]) == sorted(ids25) and st["band"] and st["tv"] and "25 games" in st["fig"] and "Estimated to fit" in st["fig"], st)
            shot(ed, "A3-loadout-open")
            ed.js("$('hkBtn').click()"); time.sleep(0.5)
            st = J(ed, "JSON.stringify({t:$('collTitle').textContent, b:$('collBody').textContent})")
            ok("A_install_continues_from_the_loadout", st["t"].startswith("Continue in Classic Cart Studio") and "25 games" in st["b"], st)
            ed.js("closeColl(); $('loBack').click()"); time.sleep(0.3)
            ed.js("$('trayBtn').click()"); time.sleep(0.3); shot(ed, "A4-collection-tray")
            ed.js("$('trayHk').click()"); time.sleep(0.4)
            st = J(ed, "JSON.stringify({lo:!!LO, t:$('collTitle').textContent, open:document.querySelectorAll('[data-lo-open]').length})")
            ok("A_console_action_from_the_collection_asks_for_a_loadout", not st["lo"] and st["t"] == "Prepare for NES Classic" and st["open"] == 1, st)
            shot(ed, "A5-prepare-for-nes-classic"); ed.js("closeColl()")
            # ================= B: 1,200-game collection, three loadouts =================
            B = J(ed, r"""JSON.stringify((()=>{ csNew('Complete NES Library'); const ids=DATA.games.map(g=>g.game_id).concat(EXT_ROWS.map(g=>g.game_id)).filter((v,i,a)=>a.indexOf(v)===i).slice(0,1200); ids.forEach(i=>sel.add(i)); persist(); render(); csSaveActiveNow();
              const rev=ids.find(i=>byId[i]&&byId[i].builtin_status==='BUILT-IN: DIFFERENT REVISION');
              const mk=(name, part, theme, folder, dec)=>{ const l=loCreate(name,'choose'); part.forEach(i=>sel.add(i)); if(dec) Object.assign(decisions, dec); persist(); localStorage.setItem(HOME_KEY, JSON.stringify({v:1, theme, folders:[{id:'f:x', name:folder, parent:null, pos:0, order:part.slice(0,3)}], favorites:{items:{[part[0]]:1}}, history:{}})); homeExtrasSave&&homeExtrasSave(homeExtrasLoad()); csSaveActiveNow(); loClose(true); return l.id; };
              const a=ids.slice(0,35).concat(rev&&!ids.slice(0,35).includes(rev)?[rev]:[]), b=ids.slice(20,62), c=ids.slice(300,328);
              return {ids, rev, L:[mk('Family Favorites', a, 'featured-shelf', 'Kids', rev?{[rev]:'alternate'}:null), mk('Weekend Multiplayer', b, 'arcade-grid', 'Party', rev?{[rev]:'factory'}:null), mk('RPG Collection', c, 'adventure-map', 'Quests', null)], a, b, c}; })())""")
            want = {B["L"][0]: B["a"], B["L"][1]: B["b"], B["L"][2]: B["c"]}; themes = {B["L"][0]: "featured-shelf", B["L"][1]: "arcade-grid", B["L"][2]: "adventure-map"}
            def loadouts_exact(ed):
                got = J(ed, "JSON.stringify((()=>{ csSaveActiveNow(); const r=csActive(); return {coll:r.sel, L:r.loadouts.map(l=>({id:l.id, sel:l.sel, theme:(l.home||{}).theme, folder:((l.home||{}).folders||[])[0], dec:l.decisions}))}; })())")
                good = len(got["coll"]) == 1200 and set(got["coll"]) == set(B["ids"])
                for l in got["L"]:
                    good = good and l["sel"] == want[l["id"]] and l["theme"] == themes[l["id"]] and l["folder"]["order"] == want[l["id"]][:3]
                if B["rev"]: good = good and next(l for l in got["L"] if l["id"] == B["L"][0])["dec"].get(B["rev"]) == "alternate" and next(l for l in got["L"] if l["id"] == B["L"][1])["dec"].get(B["rev"]) == "factory"
                return good, got
            ok("B_three_independent_loadouts", loadouts_exact(ed)[0], loadouts_exact(ed)[1]["L"][:1])
            seen = []
            for lid in (B["L"][1], B["L"][0], B["L"][2], B["L"][1]):
                ed.js(f"loOpen('{lid}', true)"); seen.append(J(ed, "JSON.stringify({sel:[...sel], theme:homeModelFromStudio().theme, coll:csCollSel().size, rows:+document.documentElement.dataset.rows})"))
            ok("B_switching_shows_each_loadouts_own_games_and_HOME", all(s["sel"] == want[l] and s["theme"] == themes[l] and s["coll"] == 1200 for s, l in zip(seen, (B["L"][1], B["L"][0], B["L"][2], B["L"][1]))), [(len(s["sel"]), s["theme"]) for s in seen])
            ok("B_library_lists_only_the_collection_while_editing", all(s["rows"] <= 1200 for s in seen), [s["rows"] for s in seen])
            shot(ed, "B1-loadout-weekend-multiplayer")
            # editing one loadout never changes another or the collection
            x = B["b"][0]; ed.js(f"sel.delete('{x}'); persist(); render(); csSaveActiveNow()")
            g = J(ed, "JSON.stringify({L:Object.fromEntries(csActive().loadouts.map(l=>[l.id,l.sel])), coll:csActive().sel})")
            ok("B_removing_from_one_loadout_keeps_it_in_the_collection_and_the_others", x not in g["L"][B["L"][1]] and x in g["coll"] and (x in g["L"][B["L"][0]]) == (x in B["a"]), x)
            ed.js(f"sel.add('{x}'); persist(); csSaveActiveNow(); loClose(true)")
            # removing a collection game that two loadouts use: asked first
            y = B["a"][25]; ed.js(f"toggleSel('{y}', false)"); time.sleep(0.4)
            st = J(ed, "JSON.stringify({t:$('collTitle').textContent, open:$('collm').classList.contains('open'), rows:document.querySelectorAll('#collBody .lorow').length})")
            ok("B_removal_used_by_loadouts_asks_first", st["open"] and "NES Classic Loadouts" in st["t"] and st["rows"] == 2, st)
            shot(ed, "B2-remove-used-game")
            ed.js("$('loKeep').click()"); time.sleep(0.3)
            ok("B_keep_restores_the_game", ed.js(f"(()=>{{ csSaveActiveNow(); const L=Object.fromEntries(csActive().loadouts.map(l=>[l.id,l.sel])); return sel.has('{y}')&&L['{B['L'][0]}'].includes('{y}')&&L['{B['L'][1]}'].includes('{y}')&&!L['{B['L'][2]}'].includes('{y}'); }})()"))
            ed.js(f"toggleSel('{y}', false)"); time.sleep(0.4); ed.js("$('loDrop').click()"); time.sleep(0.3)
            g = J(ed, "JSON.stringify({L:Object.fromEntries(csActive().loadouts.map(l=>[l.id,l.sel])), has:sel.has(" + json.dumps(y) + ")})")
            ok("B_remove_too_updates_only_the_loadouts_that_used_it", not g["has"] and y not in g["L"][B["L"][0]] and y not in g["L"][B["L"][1]] and g["L"][B["L"][2]] == B["c"], g["has"])
            ed.js(f"sel.add('{y}'); persist(); csSaveActiveNow(); loOpen('{B['L'][0]}', true); sel.add('{y}'); persist(); csSaveActiveNow(); loClose(true); loOpen('{B['L'][1]}', true); sel.add('{y}'); persist(); csSaveActiveNow(); loClose(true)")
            ed.js(f"loOpen('{B['L'][1]}', true); const o=[...sel]; sel.clear(); {json.dumps(B['b'])}.forEach(i=>sel.add(i)); persist(); csSaveActiveNow(); loClose(true); loOpen('{B['L'][0]}', true); sel.clear(); {json.dumps(B['a'])}.forEach(i=>sel.add(i)); persist(); csSaveActiveNow()")
            # ================= E: the installation manifest is the open loadout =================
            ed.js(f"loOpen('{B['L'][1]}', true)")
            m = J(ed, "JSON.stringify((()=>{ const m=buildManifest(); return {n:m.count, ids:m.selections.map(s=>s.version_id), lo:m.loadout, name:m.name}; })())")
            ok("E_manifest_is_exactly_the_open_loadout", m["n"] == 42 and sorted(m["ids"]) == sorted(B["b"]) and m["lo"]["name"] == "Weekend Multiplayer" and m["lo"]["target"] == "nes-classic", m)
            f = J(ed, "JSON.stringify((()=>{ const f=csFileFor(csActive()); return {n:f.count, lo:(f.ccs_collection.loadouts||[]).map(l=>[l.name,l.sel.length]), tag:!!f.loadout}; })())")
            ok("E_collection_file_is_the_whole_collection_with_its_loadouts", f["n"] == 1200 and not f["tag"] and f["lo"] == [["Family Favorites", len(B["a"])], ["Weekend Multiplayer", 42], ["RPG Collection", 28]], f)
            hand = J(ed, "JSON.stringify((()=>{ let n=null; const o=window.openColl; desktopInvite('x'); n=$('collBody').textContent; closeColl(); return {n}; })())")
            ok("E_desktop_handoff_offers_the_whole_collection", "1,200 games" in hand["n"] or "1200 games" in hand["n"], hand["n"][:200])
            ed.js("loClose(true)")
            # ================= C: oversized loadout =================
            c = J(ed, "JSON.stringify((()=>{ const l=loCreate('My Ultimate NES Setup','all'); const e=loEstimate([...sel], decisions, {open:true}); return {n:sel.size, st:e.status, fig:$('loFig').textContent}; })())")
            ok("C_oversized_loadout_saved_whole_with_a_clear_warning", c["n"] == 1200 and c["st"] == "over" and "Too large" in c["fig"], c)
            shot(ed, "C1-oversized-loadout")
            ed.js("$('loWhy').click()"); time.sleep(0.3); shot(ed, "C2-will-it-fit")
            ok("C_will_it_fit_explains_in_plain_words", ed.js("$('collTitle').textContent==='Will it fit?'&&$('collBody').textContent.includes('saved as it is')&&$('collBody').textContent.includes('not from your console')"))
            ed.js("closeColl(); csSaveActiveNow()")
        # ---- restart: everything exactly as it was, the open loadout reopened ----
        with EdgeSession(width=1440, height=900, profile=p1) as ed:
            ed.navigate(URL); ed.wait_for(READY, timeout=120); time.sleep(0.8); ed.js(CALM)
            st = J(ed, "JSON.stringify({lo:LO&&loCur().name, n:sel.size, coll:csCollSel().size, band:!$('loBand').hidden})")
            ok("C_after_restart_the_oversized_loadout_is_whole_and_open", st["lo"] == "My Ultimate NES Setup" and st["n"] == 1200 and st["coll"] == 1200 and st["band"], st)
            ed.js("loClose(true)")
            g = loadouts_exact_after = J(ed, "JSON.stringify(csActive().loadouts.filter(l=>l.name!=='My Ultimate NES Setup').map(l=>({id:l.id, sel:l.sel, theme:(l.home||{}).theme})))")
            ok("B_after_restart_every_loadout_exact", all(l["sel"] == want[l["id"]] and l["theme"] == themes[l["id"]] for l in g) and len(g) == 3, [(len(l["sel"]), l["theme"]) for l in g])
            ok("B_switching_collections_opens_the_collection_not_a_loadout", ed.js("(()=>{ const o=csList().find(r=>r.name==='Old Favorites'); csSwitch(o.id, true); const a=!LO; csSwitch(csList().find(r=>r.name==='Complete NES Library').id, true); return a&&!LO&&sel.size===1200; })()"))
            # ================= D: share a large collection with personal games and loadouts =================
            pids = json.loads(ed.js(PERSONAL))
            ed.js(f"{json.dumps(pids)}.forEach(i=>sel.add(i)); persist(); render(); csSaveActiveNow(); const l=csActive().loadouts.find(l=>l.name==='RPG Collection'); loOpen(l.id, true); {json.dumps(pids)}.forEach(i=>sel.add(i)); persist(); csSaveActiveNow(); loClose(true)")
            sender = J(ed, "JSON.stringify({sel:csActive().sel, L:csActive().loadouts.map(l=>[l.name,l.sel])})")
            ed.js("openShare()"); ed.wait_for("!!$('shGo')&&!$('shGo').disabled", timeout=30); time.sleep(1.0)
            ok("D_share_dialog_mentions_every_game_and_the_loadouts", ed.js("$('shMeta').textContent.includes('All 1202 games')&&$('shMeta').textContent.includes('4 NES Classic Loadouts')"), ed.js("$('shMeta').textContent"))
            ed.js("$('shGo').click()"); ed.wait_for("!!(SH.pub&&SH.pub.url)||!!document.querySelector('#shOut .bad')", timeout=60)
            share_id = ed.js("SH.pub&&SH.pub.id"); ok("D_published", bool(share_id), ed.js("$('shOut').textContent"))
            file_obj = ed.js("JSON.stringify(csFileFor(csActive()))"); bundle_obj = ed.js("JSON.stringify(csBundle([CS.active]))")
        pub = json.loads(urllib.request.urlopen(f"{SHARE}/api/c/{share_id}").read())
        ok("D_public_copy_has_every_game_and_loadout", len(pub["games"]) == 1202 and [(l["name"], l["games"]) for l in pub.get("loadouts", [])] == [(n, s) for n, s in sender["L"]], (len(pub["games"]), [(l["name"], len(l["games"])) for l in pub.get("loadouts", [])]))
        blob = json.dumps(pub)
        ok("D_no_files_or_local_paths_published", "C:/Users" not in blob and "file_name" not in blob and ".nes" not in blob)
        # ---- a recipient with no game files, in a clean browser ----
        with EdgeSession(width=1440, height=900, profile=p2) as ed:
            ed.navigate(WEB + "/robots.txt"); ed.js("localStorage.setItem('studio-welcomed-v1','1'); 1")
            ed.navigate(f"{URL}&share={share_id}"); ed.wait_for(READY, timeout=120); time.sleep(0.8); ed.js(CALM); ed.wait_for("!!$('srSave')", timeout=30)
            ok("D_recipient_sees_the_loadouts", ed.js("$('collBody').textContent.includes('NES Classic Loadouts')&&$('collBody').textContent.includes('RPG Collection')"))
            shot(ed, "D1-shared-collection-received")
            ed.js("$('srSave').click()"); ed.wait_for("!!$('srOpen')", timeout=10); ed.js("$('srOpen').click()"); time.sleep(0.5)
            got = J(ed, "JSON.stringify({sel:csActive().sel, L:csActive().loadouts.map(l=>[l.name,l.sel]), lo:!!LO})")
            ok("D_saved_copy_has_every_game_and_loadout", sorted(got["sel"]) == sorted(sender["sel"]) and got["L"] == sender["L"] and not got["lo"], (len(got["sel"]), [(n, len(s)) for n, s in got["L"]]))
            ed.js(f"csImportObject({file_obj}, 'file.json'); csImportObject({bundle_obj}, 'bundle.json')"); time.sleep(1.0); ed.js("try{ closeColl(); }catch(e){}")
            got2 = J(ed, "JSON.stringify(csList().filter(r=>r.name.startsWith('Complete NES Library')).map(r=>({n:r.sel.length, L:(r.loadouts||[]).map(l=>[l.name,l.sel])})))")
            ok("D_file_and_bundle_import_keep_every_game_and_loadout", len(got2) == 3 and all(x["n"] == 1202 and x["L"] == sender["L"] for x in got2), [(x["n"], len(x["L"])) for x in got2])
            ok("D_import_never_opens_a_loadout", ed.js("!LO"))
            rid = ed.js("csList().find(r=>r.name.startsWith('Complete NES Library')).id")
        with EdgeSession(width=1440, height=900, profile=p2) as ed:
            ed.navigate(URL); ed.wait_for(READY, timeout=120); time.sleep(0.8); ed.js(CALM)
            got3 = J(ed, f"JSON.stringify((()=>{{ const r=CS.items['{rid}']; return {{n:r.sel.length, L:r.loadouts.map(l=>[l.name,l.sel])}}; }})())")
            ok("D_after_restart_the_recipient_copy_is_intact", got3["n"] == 1202 and got3["L"] == sender["L"])
            ed.js(f"csSwitch('{rid}', true); const l=csActive().loadouts.find(l=>l.name==='RPG Collection'); loOpen(l.id, true); $('trayBtn').click()"); time.sleep(0.5); shot(ed, "D2-recipient-loadout-tray")
            ok("D_recipient_loadout_lists_games_without_files", ed.js("document.querySelectorAll('#trayList li.trmiss').length")== 2)
        # ================= G: header, and the loadout bar on a phone =================
        from PIL import Image
        for w, h, sc in ((1440, 900, 1), (1920, 1080, 1.25), (1366, 768, 1), (1024, 768, 1), (390, 844, 3)):
            with EdgeSession(width=w, height=h, scale=sc, profile=p1 if w == 390 else None) as ed:
                if w != 390: ed.navigate(WEB + "/robots.txt"); ed.js("localStorage.setItem('studio-welcomed-v1','1'); 1")
                ed.navigate(URL); ed.wait_for(READY, timeout=120); time.sleep(0.8); ed.js(CALM); ed.js("document.querySelectorAll('.modal.open').forEach(m=>m.classList.remove('open')); scrollTo(0,0); 1")
                hb = J(ed, "JSON.stringify((()=>{ const r=document.querySelector('.hero h1').getBoundingClientRect(); const s=document.querySelector('.hero h1 span').getBoundingClientRect(); return [r.left,r.top,r.height,s.right]; })())")
                png = ed.screenshot(clip=(max(0, hb[0] - 10), max(0, hb[1] - 6), hb[3] - hb[0] + 30, hb[2] + 12)); open(os.path.join(shots, f"G-header-{w}.png"), "wb").write(png)
                im = Image.open(io.BytesIO(png)).convert("RGB"); W, H = im.size; px = im.load()
                def box(pred):
                    pts = [(x, y) for y in range(H) for x in range(W) if pred(px[x, y])]
                    return (min(p[0] for p in pts), min(p[1] for p in pts), max(p[0] for p in pts), max(p[1] for p in pts)) if pts else None
                wb = box(lambda p: p[0] > 235 and p[1] > 235 and p[2] > 235); rb = box(lambda p: p[0] > 220 and p[1] < 110 and p[2] < 120)
                gap = (rb[0] - wb[2]) / sc; em = float(ed.js("parseFloat(getComputedStyle(document.querySelector('.hero h1')).fontSize)"))
                ok(f"G_header_one_baseline_and_word_gap_{w}", abs(rb[1] - wb[1]) <= 1 and abs(rb[3] - wb[3]) <= 1 and 0.15 * em <= gap <= 0.45 * em and ed.js("document.querySelector('.hero h1 span').getBoundingClientRect().top-document.querySelector('.hero h1').getBoundingClientRect().top<2"), (wb, rb, round(gap, 1), em))
                if w == 390:
                    ed.js("loOpen(csActive().loadouts[0].id, true); scrollTo(0,0); 1"); time.sleep(0.3)
                    ok("G_phone_loadout_bar_no_horizontal_scroll", ed.js("!!LO&&document.documentElement.scrollWidth<=innerWidth+1"), ed.js("[document.documentElement.scrollWidth, innerWidth, !!LO]"))
                    shot(ed, "G-phone-loadout")
                if w == 1440: shot(ed, "G-page-1440")
    finally:
        dev.terminate()
        for p in (p1, p2): cdp_edge.reap_profile(p) if hasattr(cdp_edge, "reap_profile") else shutil.rmtree(p, ignore_errors=True)
    bad = [k for k, v in checks if not v]
    print("RESULT:", "PASS" if not bad else "FAIL " + json.dumps(bad), f"({len(checks)} checks)"); print("screenshots:", shots); return 0 if not bad else 1

if __name__ == "__main__": sys.exit(main())
