r"""Website checks for My Collections, the new navigation and Back Up Collection (headless Edge, no Windows app, no backend).

The built site is served on 127.0.0.1 (website mode: no desktop backend) and nothing else runs: public share links are retired, a collection
is backed up as a complete .ccs file made on this device (sharing is removed; section 7 of the coordinated phase). A persistent browser profile is used so "browser restarts" are real restarts.

  collections   migration of an existing single collection; three named collections with independent selections and exact
                versions; switch; rename; duplicate; delete and restore; export / import round trip; persistence across a restart
  favorites     View -> Show Favorites shows only the open collection's favorites without changing it, and leaving it is one click
  backup        Back Up Collection from My Collections: a collection whose games have no ROM file here stays usable and is
                not packaged (the dialog lists the games); a collection of synthetic sentinel ROMs is exported as a complete .ccs file,
                unchanged by the export, and saved into My Collections in a clean profile (an independent new collection, exact versions);
                an old ?share= address opens the Studio normally (a new Studio: its Included Games); a Studio data backup is a JSON file
                that restores in a clean profile;
                no request leaves this computer, none goes to share.classiccartstudio.com, none carries ROM bytes
  layout        screenshots of the toolbar and its menus at 1440, 1280, 1100, 1024 and 390 px wide (saved for review), no horizontal scroll
                (the page no wider than the screen, also at 320, 360, 414 and 430 px phone widths); every menu inside the window; from 1100 px
                wide the toolbar's buttons are all on one row; on phones the collection menu keeps a 44 px touch target and, from 360 px, its
                name (shortened)
(The package contract itself is test_ccs_v2_web.py.)

  python test_collections_web.py <built site folder> [--shots DIR]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, json, os, re, shutil, socket, sys, tempfile, threading, time, http.server, functools
HERE = os.path.dirname(os.path.abspath(__file__)); TOOLS = os.path.dirname(HERE); sys.path.insert(0, HERE)
import cdp_edge
from cdp_edge import EdgeSession as _Edge
class EdgeSession(_Edge):
    """the harness session with a persistent profile (a real browser restart keeps local storage); the profile is not deleted on close"""
    def __init__(self, profile=None, **kw):
        kw.pop("extra_args", None); super().__init__(**{"start_timeout": 120.0, **kw}); self._p = profile
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
    srv = http.server.ThreadingHTTPServer(("127.0.0.1", free_port()), functools.partial(Q, directory=folder)); threading.Thread(target=srv.serve_forever, daemon=True).start(); return srv

COLL_JS = "(()=>{ const b=$('collBtn').getBoundingClientRect(), n=$('collName'); return [Math.round(b.width), Math.round(n.getBoundingClientRect().width), Math.round(n.scrollWidth), Math.round($('restoreBtn').getBoundingClientRect().width)]; })()"
def coll_ok(w, c):   # the phone's first row: the collection menu a 44 px touch target, its name showing (shortened) from 360 px; Restore a Backup its icon's 44 px
    return c[0] >= 44 and (w < 360 or c[1] >= min(20, c[2])) and c[3] == 44
def main():
    from studio_harness import CCS_EXPORT_UI, page_file, page_bytes, net_take, net_report   # here: those modules import this one's neighbors
    from test_ccs_v2_web import NET, ROMS
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--shots", default=""); a = ap.parse_args()
    shots = a.shots or os.path.join(tempfile.gettempdir(), "ccs-web-shots"); os.makedirs(shots, exist_ok=True)
    checks = []
    def ok(k, v, n=""): checks.append((k, bool(v), n)); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:300]})" if n and not v else ""), flush=True)
    web = serve(a.site); WEB = f"http://127.0.0.1:{web.server_address[1]}"
    prof = tempfile.mkdtemp(prefix="ccs-web-profile-"); prof2 = tempfile.mkdtemp(prefix="ccs-web-clean-"); prof3 = tempfile.mkdtemp(prefix="ccs-web-off-")
    URL = f"{WEB}/index.html?companion={free_port()}"   # a loopback port nothing listens on: website mode, whatever runs on this computer
    READY = "typeof CS!=='undefined'&&!!CS&&typeof byId!=='undefined'&&typeof csList==='function'&&typeof ccsBackupDialog==='function'&&document.readyState==='complete'"
    # a new Studio prepares its Included Games once, in the background, and opens them (included.js inclStarter): a check that counts collections
    # or reads the open one waits until that is done, or it races it
    SETTLED = "CS_STATE!=='ok'||!(CS.starter&&CS.starter.state!=='done'&&CS.starter.state!=='failed')"
    import json as _j
    INCL = [g["title"] for g in _j.load(open(os.path.join(a.site, "included", "index.json"), encoding="utf-8"))["games"] if g.get("hosted") is not False]
    nets = []
    def tools(ed): ed.js(NET); ed.js(ROMS); ed.js(CCS_EXPORT_UI)
    try:
        # ---------------- session 1: an existing single-collection user (0.6.1 keys) ----------------
        with EdgeSession(width=1440, height=900, profile=prof) as ed:
            ed.navigate(WEB + "/robots.txt")
            ed.js("""localStorage.setItem('nes-classic-selection-v1', JSON.stringify(['e9f4c9d1b7c66c6a','fcb6a0ef3a20c19b'])); localStorage.setItem('nes-classic-decisions-v1', JSON.stringify({'e9f4c9d1b7c66c6a':'factory'}));
                     localStorage.setItem('studio-collection-v1', JSON.stringify({name:'My Old Collection', created:'2026-10-01T10:00:00.000Z', modified:'2026-10-18T10:00:00.000Z', pref:{}, multi:{}, avail:[], placeholders:[]}));
                     localStorage.setItem('studio-home-v1', JSON.stringify({v:1, folders:[{id:'f:a', name:'Racing', parent:null, order:['e9f4c9d1b7c66c6a']}], theme:'adventure-map', favorites:{items:{}}, history:{}})); localStorage.setItem('studio-welcomed-v1','1'); 'ok'""")
            ed.navigate(URL); ed.wait_for(READY, timeout=120); tools(ed)
            st = ed.js("JSON.stringify({n:csList().length, name:csActive().name, sel:csActive().sel.slice().sort(), dec:csActive().decisions, home:(csActive().home||{}).theme, folders:((csActive().home||{}).folders||[]).length, src:CS.migrated.from, keep:!!localStorage.getItem('nes-classic-selection-v1'), web:COMPANION.state})")
            st = json.loads(st)
            ok("migration_keeps_the_existing_collection", st["n"] == 1 and st["name"] == "My Old Collection" and st["sel"] == ["e9f4c9d1b7c66c6a", "fcb6a0ef3a20c19b"] and st["dec"] == {"e9f4c9d1b7c66c6a": "factory"} and st["home"] == "adventure-map" and st["folders"] == 1 and "0.6.1" in st["src"] and st["keep"], st)
            ok("website_needs_no_backend", st["web"] == "web", st["web"])
            ed.js("try{ doneWelcome(); }catch(e){} $('collBtn').click(); 1"); time.sleep(0.3)
            st = json.loads(ed.js("JSON.stringify({vis:!$('mShare').hidden, label:$('mShare').innerText, gone:['SHARE_ON','SHARE_API','openShare','shReceive','shPublish','shUnpublish','shareConfig'].filter(n=>typeof window[n]!=='undefined')})"))
            ed.js("$('mShare').click()")   # the dialog opens once its own check of the game files has run (asynchronous): waited for, not timed
            try: ed.wait_for("$('collm').classList.contains('open')&&$('collTitle').textContent==='Back Up This Collection'", timeout=30)
            except Exception: pass
            st["dialog"] = ed.js("$('collm').classList.contains('open')&&$('collTitle').textContent"); ed.js("closeColl(); 1")
            ok("back_up_collection_in_my_collections", st["vis"] and st["label"].strip() == "Back Up This Collection…" and st["dialog"] == "Back Up This Collection" and not st["gone"], st)
            nets.append(net_take(ed))
            ed.navigate(URL); ed.wait_for(READY, timeout=120); tools(ed)
            ok("migration_is_idempotent", ed.js("csList().length") == 1)
            pick = json.loads(ed.js("JSON.stringify(DATA.games.filter(g=>!g.builtin_code).slice(0,6).map(g=>g.game_id))"))
            alt = json.loads(ed.js("JSON.stringify((()=>{ const t=Object.values(TITLES).find(t=>t.versions&&t.versions.length>2&&t.versions.every(v=>byId[v])); return t?[t.versions[0],t.versions[2]]:[]; })())"))
            ed.js(f"""(()=>{{ csNew('Alpha'); {json.dumps(pick[:3])}.forEach(i=>sel.add(i)); persist(); render(); csSaveActiveNow();
                      csNew('Bravo'); {json.dumps(pick[3:5] + alt[1:2])}.forEach(i=>sel.add(i)); persist(); render(); csSaveActiveNow();
                      csNew('Charlie'); sel.add({json.dumps(pick[5])}); persist(); render(); csSaveActiveNow(); return 1; }})()""")
            lst = json.loads(ed.js("JSON.stringify(csList().map(r=>[r.name,(r.sel||[]).slice().sort()]))")); d = dict((n, s) for n, s in lst)
            ok("three_named_collections_independent", d.get("Alpha") == sorted(pick[:3]) and d.get("Bravo") == sorted(pick[3:5] + alt[1:2]) and d.get("Charlie") == [pick[5]] and d.get("My Old Collection") == ["e9f4c9d1b7c66c6a", "fcb6a0ef3a20c19b"], lst)
            ok("exact_version_kept", (alt[1] in d.get("Bravo", [])) and alt[0] not in d.get("Bravo", []), alt)
            aid = ed.js("csList().find(r=>r.name==='Alpha').id"); ed.js(f"csSwitch('{aid}')")
            ok("switch_loads_that_selection", sorted(json.loads(ed.js("JSON.stringify([...sel])"))) == sorted(pick[:3]) and ed.js("COLL.name") == "Alpha" and ed.js("$('collName').textContent") == "Alpha")
            bid = ed.js("csList().find(r=>r.name==='Bravo').id"); ed.js(f"csRename('{bid}','Bravo Renamed')"); dup = ed.js(f"csDuplicate('{aid}').id")
            ok("rename_and_duplicate", ed.js(f"CS.items['{bid}'].name") == "Bravo Renamed" and sorted(json.loads(ed.js(f"JSON.stringify(CS.items['{dup}'].sel)"))) == sorted(pick[:3]) and ed.js(f"CS.items['{dup}'].name") == "Alpha (Copy 1)")   # the released duplicate name (0.7.x)
            cid = ed.js("csList().find(r=>r.name==='Charlie').id"); ed.js(f"csDelete('{cid}')")
            snap = ed.js("JSON.parse(localStorage.getItem('studio-snapshots-v1')||'[]').some(s=>s.name==='Charlie')")
            ok("delete_with_recovery_snapshot", not ed.js(f"!!CS.items['{cid}']") and snap)
            ed.js("restoreSnapshot(JSON.parse(localStorage.getItem('studio-snapshots-v1')).findIndex(s=>s.name==='Charlie'))")
            ok("restore_previous_version", sorted(json.loads(ed.js("JSON.stringify([...sel])"))) == [pick[5]], ed.js("JSON.stringify([...sel])"))
            ed.js(f"csSwitch('{aid}')")
            exp = json.loads(ed.js(f"JSON.stringify(csFileFor(CS.items['{aid}']))"))
            ok("export_file_is_schema4_plus_record", exp.get("schema") == "nes-classic-collection/4" and exp.get("ccs_collection", {}).get("schema") == "ccs-collection/1" and exp.get("count") == 3, {k: exp.get(k) for k in ("schema", "count", "error")} if isinstance(exp, dict) else exp)
            ed.js(f"csImportObject({json.dumps(exp)}, 'x.json')")
            imp = ed.js("JSON.stringify((r=>r?({name:r.name,sel:r.sel.slice().sort(),act:r.id===CS.active}):{names:csList().map(x=>x.name)})(csList().find(r=>r.name==='Alpha (Copy 2)')))"); imp = json.loads(imp) if isinstance(imp, str) else imp
            ok("import_round_trip_new_collection", imp.get("name") == "Alpha (Copy 2)" and imp.get("sel") == sorted(pick[:3]) and imp.get("act"), imp)
            ed.js(f"csSwitch('{bid}')")
            # favorites from the Select menu: a view, never a selection change
            favs = pick[3:5]; ed.js(f"{json.dumps(favs)}.forEach(i=>fav.add(i)); persist(); render();")   # two of the open collection's (Bravo's) three games
            before = sorted(json.loads(ed.js("JSON.stringify([...sel])")))
            ed.js("$('selectBtn').click()"); ok("select_menu_has_favorites", ed.js("!$('selectMenu').hidden&&$('favView').textContent.includes('Show Favorites')"), ed.js("$('favView').textContent"))
            ed.js("$('favView').click()"); time.sleep(0.4)
            vis = json.loads(ed.js("JSON.stringify(visible().map(g=>g.game_id))"))
            ok("favorites_view_shows_only_favorites", sorted(vis) == sorted(favs) and ed.js("$('fSel').value") == "fav", vis)
            ok("favorites_view_keeps_selection", sorted(json.loads(ed.js("JSON.stringify([...sel])"))) == before)
            ed.js("$('selectBtn').click(); $('favView').click()"); time.sleep(0.3)
            ok("favorites_view_one_click_to_leave", ed.js("$('fSel').value") == "" and ed.js("visible().length") == len(before), ed.js("visible().length"))
            # layout: the toolbar and menus at three widths
            ed.js("try{ doneWelcome(); }catch(e){} document.querySelectorAll('.modal.open').forEach(m=>m.classList.remove('open')); 1")
            for w, h in ((1440, 900), (1280, 800), (1100, 800), (1024, 768), (390, 844)):
                ed.call("Emulation.setDeviceMetricsOverride", {"width": w, "height": h, "deviceScaleFactor": 1, "mobile": w < 500}); time.sleep(0.8); ed.js("window.scrollTo(0,0)")
                # the screen's own width (a phone's browser widens innerWidth to a page that is too wide, so that never shows it)
                ok(f"no_horizontal_scroll_{w}", ed.js("document.documentElement.scrollWidth<=document.documentElement.clientWidth+1"), ed.js("[document.documentElement.scrollWidth, document.documentElement.clientWidth]"))
                if w < 500: c = ed.js(COLL_JS); ok(f"phone_collection_menu_{w}", coll_ok(w, c), c)
                if w >= 1100:   # every button of the toolbar's first line on one row: Open HOME, the collection, Add Game Files, Restore a Backup, Install on Console, Studio;
                                # with this collection's name and with another (the button's words change its width: a line measured a fraction too narrow wrapped)
                    was = ed.js("$('collName').textContent")
                    for nm in (was, "Family Favorites"):
                        ed.js(f"$('collName').textContent={json.dumps(nm)}; window.foldKick&&foldKick(); 1"); time.sleep(0.5)
                        tops = json.loads(ed.js("JSON.stringify([...$('toolbar').querySelectorAll('.row.bar>*, .row.bar>.actions>*')].filter(e=>e.offsetParent&&e.tagName!=='SPAN'&&!e.classList.contains('actions')&&getComputedStyle(e).display!=='none').map(e=>[e.id||e.className, Math.round(e.getBoundingClientRect().top)]))"))
                        ok(f"toolbar_one_row_{w}{'' if nm == was else '_family_favorites'}", len(tops) >= 6 and max(t for _, t in tops) - min(t for _, t in tops) <= 4, tops)
                    ed.js(f"$('collName').textContent={json.dumps(was)}; window.foldKick&&foldKick(); 1"); time.sleep(0.4)
                for name, btn in (("bar", None), ("collections", "collBtn"), ("menu", "appBtn"), ("select", "selectBtn")):
                    ed.js("document.querySelectorAll('.menu').forEach(x=>x.hidden=true)")
                    if btn: ed.js(f"$('{btn}').click()"); time.sleep(0.4)
                    if btn:
                        r = json.loads(ed.js(f"JSON.stringify((()=>{{ const m=$('{btn}').closest('.menuwrap').querySelector('.menu'); const b=m.getBoundingClientRect(); return [b.left,b.right,innerWidth]; }})())"))
                        ok(f"menu_{name}_inside_viewport_{w}", r[0] >= -1 and r[1] <= r[2] + 1, r)
                    open(os.path.join(shots, f"{w}-{name}.png"), "wb").write(ed.screenshot())
                ed.js("document.querySelectorAll('.menu').forEach(x=>x.hidden=true)")
            for w in (320, 360, 414, 430):   # more phone widths: the page no wider than the screen
                ed.call("Emulation.setDeviceMetricsOverride", {"width": w, "height": 844, "deviceScaleFactor": 2, "mobile": True}); time.sleep(0.8); ed.js("window.scrollTo(0,0)")
                ok(f"no_horizontal_scroll_{w}", ed.js("document.documentElement.scrollWidth<=document.documentElement.clientWidth+1"), ed.js("[document.documentElement.scrollWidth, document.documentElement.clientWidth]"))
                c = ed.js(COLL_JS); ok(f"phone_collection_menu_{w}", coll_ok(w, c), c)
            ed.call("Emulation.setDeviceMetricsOverride", {"width": 1440, "height": 900, "deviceScaleFactor": 1, "mobile": False})
            # backing up a collection whose games have no ROM file here: it stays a collection, no package is made
            ed.js(f"csSwitch('{aid}', true)")
            st = json.loads(ed.js(f"(async()=>JSON.stringify(await ccsExportUi('{aid}', null)))()"))
            open(os.path.join(shots, "backup-dialog-roms-needed.png"), "wb").write(ed.screenshot())
            titles = json.loads(ed.js(f"JSON.stringify({json.dumps(pick[:3])}.map(i=>byId[i].display_title))"))
            ok("backup_without_roms_lists_the_games_and_makes_nothing", st["exp"] and st["send"] and sorted(st["missing"]) == sorted(pick[:3]) and "3 of 3 games need their game file" in st["text"] and all(t in st["text"] for t in titles) and st["pick"] and not st.get("name"), st)
            ed.js("closeColl(); 1")
            ok("collection_without_roms_unchanged", sorted(json.loads(ed.js(f"JSON.stringify(CS.items['{aid}'].sel)"))) == sorted(pick[:3]) and ed.js("csActive().name") == "Alpha")
            # a collection of synthetic games with their ROM files: backed up as a complete .ccs file through Back Up Collection
            D = json.loads(ed.js(r"""(async()=>{ const G=[]; for(let i=0;i<4;i++) G.push(await mkRom('d'+i, i%2===0)); csNew('Delta'); G.forEach(x=>sel.add(x.id)); persist(); render(); csSaveActiveNow(); window.__D=G;
              return JSON.stringify({id:CS.active, sel:csActive().sel.slice(), rec:JSON.stringify(csRecordOut(csActive()))}); })()"""))
            st = json.loads(ed.js(f"(async()=>JSON.stringify(await ccsExportUi('{D['id']}', __D.map(x=>x.file))))()"))
            ok("complete_backup_made_through_back_up_collection", st.get("ready") == 4 and st.get("ack") == "Back Up This Collection" and st.get("name") == "Delta.ccs", st)
            pkg = page_bytes(ed, "window.__pkg")
            ok("backup_leaves_the_collection_unchanged", ed.js(f"JSON.stringify(csRecordOut(CS.items['{D['id']}']))") == D["rec"])
            # Studio data backup: a JSON file (not a package)
            bk = json.loads(ed.js("(async()=>{ window.__saved=null; ccsSaveFile=async(n,b)=>{ window.__saved={name:n, text:await b.text()}; return {ok:true,name:n}; }; await csBackup(); return JSON.stringify(window.__saved); })()"))
            ok("studio_data_backup_is_a_json_file", bk["name"].endswith(".json") and json.loads(bk["text"]).get("schema") == "ccs-studio-backup/2", bk["name"])
            ed.js(f"csSwitch('{aid}', true)"); sender_before = sorted(json.loads(ed.js(f"JSON.stringify(CS.items['{aid}'].sel)")))
            nets.append(net_take(ed))
        # ---------------- an old public share address: the Studio opens normally, nothing is fetched from the retired service ----------------
        with EdgeSession(width=1280, height=900, profile=prof3) as ed:
            ed.navigate(f"{URL}&share=AbCdEf1234"); ed.wait_for(READY, timeout=120); ed.wait_for(SETTLED, timeout=180); ed.js(NET); time.sleep(1.5)
            st = json.loads(ed.js("JSON.stringify({open:$('collm').classList.contains('open')?$('collTitle').textContent:'', sr:!!document.getElementById('srSave'), n:csList().length, name:csActive()&&csActive().name, games:visible().map(g=>g.display_title).sort(), cards:$('gridView').children.length})"))
            ok("old_share_address_opens_the_studio_normally", not st["sr"] and st["open"] == "" and st["n"] == 1 and st["name"] == "Included Games" and st["games"] == sorted(INCL) and st["cards"] > 0, st)
            nets.append(net_take(ed))
        # ---------------- session 2: the same profile after a browser restart ----------------
        with EdgeSession(width=1440, height=900, profile=prof) as ed:
            ed.navigate(URL); ed.wait_for(READY, timeout=120); tools(ed)
            names = sorted(json.loads(ed.js("JSON.stringify(csList().map(r=>r.name))")))
            ok("collections_persist_across_restart", names == sorted(["My Old Collection", "Alpha", "Bravo Renamed", "Alpha (Copy 1)", "Alpha (Copy 2)", "Charlie", "Delta"]), names)
            ok("active_collection_persists", ed.js("csActive().name") == "Alpha" and sorted(json.loads(ed.js("JSON.stringify([...sel])"))) == sender_before)
            ok("favorites_persist", sorted(json.loads(ed.js("JSON.stringify([...fav])"))) == sorted(favs))
            ok("exported_collection_persists_with_exact_versions", json.loads(ed.js("JSON.stringify(csList().find(r=>r.name==='Delta').sel)")) == D["sel"])
            nets.append(net_take(ed))
        # ---------------- session 3: a recipient in a clean browser ----------------
        with EdgeSession(width=1280, height=900, profile=prof2) as ed:
            ed.navigate(WEB + "/robots.txt"); ed.js("localStorage.setItem('studio-welcomed-v1','1'); 1")
            ed.navigate(URL); ed.wait_for(READY, timeout=120); ed.wait_for(SETTLED, timeout=180); tools(ed); n0 = ed.js("csList().length"); s0 = sorted(json.loads(ed.js("JSON.stringify([...sel])")))
            page_file(ed, pkg, "Delta.ccs", "__P"); ed.js("ccsOpenFile(__P)"); ed.wait_for("!!$('ccsGo')", timeout=60)
            open(os.path.join(shots, "ccs-received.png"), "wb").write(ed.screenshot())
            st = json.loads(ed.js("JSON.stringify({go:$('ccsGo').textContent, body:$('collBody').innerText, sel:sel.size})"))
            st["sel"] = sorted(json.loads(ed.js("JSON.stringify([...sel])")))
            ok("recipient_sees_the_package_without_change", st["go"] == "Add to My Collections" and "4 checked" in st["body"] and st["sel"] == s0 and ed.js("csList().length") == n0, st)
            ed.js("$('ccsGo').click()"); ed.wait_for("!!$('ccsOpen')", timeout=30); ed.js("$('ccsOpen').click()"); ed.wait_for("csActive()&&csActive().name==='Delta'", timeout=30)
            st = json.loads(ed.js("JSON.stringify({name:csActive().name, sel:csActive().sel, n:csList().length})"))
            ok("recipient_saved_independent_copy", st["name"] == "Delta" and st["sel"] == D["sel"] and st["n"] == n0 + 1, (st, n0))
            # the sender's Studio data backup restores here (it adds; nothing here is replaced)
            page_file(ed, bk["text"].encode("utf-8"), bk["name"], "__B"); ed.js("ccsOpenFile(__B)"); ed.wait_for("!!$('rsGo')", timeout=30); ed.js("$('rsGo').click()"); time.sleep(1.0); ed.js("try{ closeColl(); }catch(e){} 1")
            names2 = json.loads(ed.js("JSON.stringify(csList().map(r=>r.name))"))
            ok("studio_data_backup_restores_in_a_clean_browser", all(any(n == x or n.startswith(x) for n in names2) for x in ("Alpha", "Bravo Renamed", "Alpha (Copy 1)", "Charlie", "My Old Collection")) and sorted(json.loads(ed.js("JSON.stringify([...fav])"))) == sorted(favs), names2)
            nets.append(net_take(ed))
        rep = net_report(nets)
        ok("no_request_left_this_computer", not rep["remote"] and not rep["share"], rep)
        ok("no_request_carried_rom_bytes", not rep["rom"], rep["rom"][:3])
    finally:
        web.shutdown()
        for p in (prof, prof2, prof3): shutil.rmtree(p, ignore_errors=True)
    bad = [k for k, v, _ in checks if not v]; print("RESULT:", "PASS" if not bad else "FAIL " + json.dumps(bad), f"({len(checks)} checks; screenshots in {shots})"); return 0 if not bad else 1

if __name__ == "__main__":
    sys.exit(main())
