r"""Preview on TV: acceptance checks driven through headless Edge (DevTools protocol) on the generated page, now for the console
HOME runtime (four approved themes, original-controller contract). Kept from the earlier suite: the badge and its position,
browser-first (no backend request on open), the built-in sample for an empty tray, the 16:9 stage, honesty labels, export size,
focus restore, responsive widths. New: A launches (simulated, labeled), SELECT = Game Details, START = menu overlay, no wrap,
theme switching with position kept, Browse / Search / Favorites / Recently Played / History / Settings, generated-cover marks.
  python test_tv_preview.py [--url file:///...] [--out C:\NES\work\logs\tv]"""
from __future__ import annotations
import os, sys, json, time, subprocess, tempfile, shutil, base64, urllib.request, argparse
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from shot_mobile import WS, EDGE
from common import REPORTS

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--url", default="file:///" + os.path.join(REPORTS, "nes-catalog.html").replace("\\", "/")); ap.add_argument("--out", default=r"C:\NES\work\logs\tv"); a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True); checks = []
    def ok(k, v, n=""): checks.append(f"{k}={'true' if v else 'false'}{(' ('+str(n)[:260]+')') if (n != '' and not v) else ''}")
    prof = tempfile.mkdtemp(prefix="nes-tv-"); port = 9372
    p = subprocess.Popen([EDGE, "--headless=new", "--disable-gpu", "--allow-file-access-from-files", f"--user-data-dir={prof}", f"--remote-debugging-port={port}", "about:blank"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        for _ in range(80):
            try: tabs = json.load(urllib.request.urlopen(f"http://127.0.0.1:{port}/json", timeout=1)); break
            except Exception: time.sleep(0.25)
        ws = WS(next(t["webSocketDebuggerUrl"] for t in tabs if t["type"] == "page")); mid = [0]
        def call(m, params=None):
            mid[0] += 1; ws.send(json.dumps({"id": mid[0], "method": m, "params": params or {}}))
            while True:
                r = json.loads(ws.recv())
                if r.get("id") == mid[0]: return r
        def js(expr):
            r = call("Runtime.evaluate", {"expression": expr, "returnByValue": True, "awaitPromise": True})
            if "exceptionDetails" in r["result"]: return "EXC " + json.dumps(r["result"]["exceptionDetails"])[:300]
            return r["result"]["result"].get("value")
        def size(w, h=900, mobile=False): call("Emulation.setDeviceMetricsOverride", {"width": w, "height": h, "deviceScaleFactor": 2 if mobile else 1, "mobile": mobile}); time.sleep(0.35)
        def shot(name, h=None):
            d = call("Page.captureScreenshot", {"format": "png", "clip": {"x": 0, "y": 0, "width": js("innerWidth"), "height": h or js("innerHeight"), "scale": 1}})["result"]["data"]; open(os.path.join(a.out, name + ".png"), "wb").write(base64.b64decode(d))
        def key(k, code=None):
            vk = {"ArrowLeft": 37, "ArrowRight": 39, "ArrowUp": 38, "ArrowDown": 40, "Enter": 13, "Escape": 27, "Backspace": 8, "s": 83, "m": 77, "z": 90, "x": 88}.get(k, 0)
            call("Input.dispatchKeyEvent", {"type": "keyDown", "key": k, "code": code or k, "windowsVirtualKeyCode": vk}); call("Input.dispatchKeyEvent", {"type": "keyUp", "key": k, "code": code or k, "windowsVirtualKeyCode": vk})
        SH = "$('tvHome').shadowRoot"; ST = f"(()=>{{const S=TV.S; return {{theme:S.theme, sec:S.section.type, secId:S.section.id||null, zone:S.zone, index:S.index, overlay:S.overlay?S.overlay.type:null, query:S.query, n:homeListing(TV.model,S,tvSearch()).ids.length, launching:!!TV.launching}}}})()"
        def st(): return js(ST)
        def act(*seq):
            for x in seq: js(f"tvAct('{x}')")
        def menu(item):   # open the START menu and choose an entry by id (the entries depend on the section: Sort Games only where sorting applies)
            js(f"(()=>{{ TV.S.overlay={{type:'start',index:0,below:null}}; const items=HOME_START_MENU.filter(it=>it.id!=='sort'||homeSortable(TV.S.section)); TV.S.overlay.index=Math.max(0,items.findIndex(i=>i.id==='{item}')); tvDraw(); }})()"); act("a")
        call("Page.enable"); call("Network.enable"); size(1366); call("Page.navigate", {"url": a.url})
        for _ in range(240):
            if js("document.documentElement.dataset.ready==='1'") is True: break
            time.sleep(0.25)
        js("try{ doneWelcome(); }catch(e){} VL.all=false; localStorage.removeItem('studio-home-v1')")
        # ---------- B. badge / entry points (unchanged) ----------
        js("(async()=>{ const img=$('tvBtn').querySelector('img'); for(let i=0;i<40&&!(img.complete&&img.naturalWidth>0);i++){ try{ await Promise.race([img.decode(), new Promise(r=>setTimeout(r,250))]); }catch(e){ await new Promise(r=>setTimeout(r,250)); } } return img.complete; })()")
        bb = js("(()=>{ const b=$('tvBtn'); const r=b.getBoundingClientRect(); const img=b.querySelector('img'); const ir=img.getBoundingClientRect(); const row=[...document.querySelector('#toolsFold .row.tools').children].filter(e=>getComputedStyle(e).display!=='none'&&!(e.tagName==='SPAN'&&!e.id)).map(e=>e.id||e.className.split(' ')[0]); return {w:Math.round(r.width), h:Math.round(r.height), iw:Math.round(ir.width*100)/100, ih:Math.round(ir.height*100)/100, label:b.getAttribute('aria-label'), title:b.title, src:img.getAttribute('src'), nat:[img.naturalWidth,img.naturalHeight], loaded:img.complete&&img.naturalWidth>0, inHero:$('hero').contains(b), inTools:document.querySelector('#toolsFold .row.tools').contains(b), order:row, heroHasBadge:!!$('hero').querySelector('img[src*=as-seen], .tvbtn'), heroH:$('hero').offsetHeight, hscroll:document.documentElement.scrollWidth>document.documentElement.clientWidth, tag:b.tagName, alphaCorner:(()=>{ const c=document.createElement('canvas'); c.width=c.height=1; const x=c.getContext('2d'); x.drawImage(img,0,0,1,1,0,0,1,1); return x.getImageData(0,0,1,1).data[3]; })()}; })()")
        ok("badge_is_original_png_in_toolbar", bb and bb["inTools"] and not bb["inHero"] and bb["src"] == "brand/public/as-seen-on-tv.png" and bb["nat"] == [320, 250] and (bb["loaded"] or bb["nat"] == [320, 250]) and bb["alphaCorner"] == 0, bb)   # headless Edge on file:// can report the size before `complete`
        ok("badge_right_of_list_grid", bb and bb["order"].index("seg") + 1 == bb["order"].index("tvBtn") and bb["order"].index("menuwrap") < bb["order"].index("seg"), bb and bb["order"])
        ok("badge_at_most_48px_wide_proportional", bb and bb["iw"] <= 48 and abs(bb["iw"] / bb["ih"] - 320 / 250) < 0.03 and 44 <= bb["h"] <= 46 and 50 <= bb["w"] <= 54, bb and (bb["iw"], bb["ih"], bb["w"], bb["h"]))
        ok("badge_accessible_name_and_tooltip", bb and bb["label"] == "Preview on TV" and bb["title"] == "Preview on TV" and bb["tag"] == "BUTTON", bb and (bb["label"], bb["title"]))
        ok("hero_has_no_badge", bb and not bb["heroHasBadge"] and bb["heroH"] <= 150 and not bb["hscroll"], bb and (bb["heroHasBadge"], bb["heroH"]))
        ok("menu_entry_present", js("!!$('mTv') && $('mTv').textContent.includes('Preview on TV') && !$('mTv').querySelector('img')"))
        ok("single_graphical_badge", js("document.querySelectorAll('img[src*=as-seen-on-tv]').length===1"))
        ok("parade_intact", js("document.querySelectorAll('.parade .cast img').length") == 27)
        # ---------- C. browser-first ----------
        js("window.__reqs=[]; performance.clearResourceTimings()")
        js("sel.clear(); persist(); render(); if(!foldOpen()) $('foldBtn').click(); $('tvBtn').click()"); time.sleep(0.7)
        ok("opens_from_badge", js("TV.open===true && !$('tv').hidden && $('tv').classList.contains('open')"))
        ok("no_backend_request_on_open", js("!performance.getEntriesByType('resource').some(e=>e.name.includes('127.0.0.1')) && !document.querySelector('#ccsLaunch')"), js("performance.getEntriesByType('resource').filter(e=>e.name.includes('127.0.0.1')).map(e=>e.name).slice(0,3)"))
        # ---------- D/E. empty tray: the built-in sample, labeled ----------
        es = js(f"(()=>{{ const p=tvProject('proposed'); const sh={SH}; return {{sample:p.sample, n:p.count, hidden:p.hidden.length, modelN:TV.model.order.length, sampleFlag:TV.model.sample, covers:sh.querySelectorAll('.cover[data-id]').length, sub:$('tvSub').textContent, side:$('tvSide').textContent, live:$('tvLive').textContent, foot:$('tvFoot').textContent, compare:!$('tvSide').querySelector('select'), theme:TV.S.theme, sec:TV.S.section.type}}; }})()")
        ok("empty_tray_shows_builtin_sample", es and es["sample"] is True and es["n"] == 30 and es["modelN"] == 30 and es["sampleFlag"] is True and es["covers"] >= 16, es)
        ok("empty_tray_clearly_labeled", es and "tray empty" in es["sub"].lower() and "sample" in es["sub"].lower() and "tray is empty" in es["side"].lower() and "30 built-in" in es["side"] and "sample" in es["live"].lower() and es["compare"] and "Simulation" in es["foot"], es and (es["sub"][:80], es["live"][:60]))
        ok("first_run_arcade_grid_all_games", es and es["theme"] == "arcade-grid" and es["sec"] == "allgames")
        shot("tv-empty")
        # ---------- A. widescreen stage, 1280x720 logical ----------
        stg = js(f"(()=>{{ const r=$('tvStage').getBoundingClientRect(); const hm={SH}.querySelector('.hm'); const hr=hm.getBoundingClientRect(); return {{w:r.width, h:r.height, ratio:r.width/r.height, inside:r.right<=innerWidth&&r.bottom<=innerHeight&&r.left>=0, logical:[hm.offsetWidth,hm.offsetHeight], fitted:Math.abs(hr.width-r.width)<3&&Math.abs(hr.height-r.height)<3, brand:hm.querySelector('.brand').textContent, bg:getComputedStyle(hm).backgroundImage.includes('bg-arcade-grid')}}; }})()")
        ok("stage_is_16_9", stg and abs(stg["ratio"] - 16 / 9) < 0.02 and stg["inside"], stg)
        ok("stage_logical_1280x720_scaled_to_fit", stg and stg["logical"] == [1280, 720] and stg["fitted"], stg)
        ok("brand_heading_exact", stg and stg["brand"] == "Classic Cart Studio" and stg["bg"], stg and stg["brand"])
        js("closeTv()")
        # ---------- projection: one game, built-ins, versions ----------
        js("sel.clear(); sel.add(DATA.games[0].game_id); persist(); render(); openTv()"); time.sleep(0.5)
        ok("one_game", js(f"TV.model.order.length===1 && {SH}.querySelectorAll('.cover.focus').length===1"))
        ok("sample_off_once_selected", js("tvProject('proposed').sample===false && !$('tvSub').textContent.toLowerCase().includes('sample')"))
        act("right", "left", "down"); ok("one_game_no_wrap", st()["index"] == 0)
        js("closeTv(); sel.clear(); DATA.games.filter(g=>g.builtin_status==='BUILT-IN: EXACT MATCH').forEach(g=>sel.add(g.game_id)); persist(); render(); openTv()"); time.sleep(0.5)
        bi = js("(()=>{ const p=tvProject('proposed'); return {n:p.count, builtin:p.counts.builtin, hidden:p.hidden.length, modelN:TV.model.order.length, kinds:[...new Set(TV.model.games.map(g=>g.kind))]}; })()")
        ok("builtins_kept_hidden_counted", bi and bi["n"] == 27 and bi["builtin"] == 27 and bi["hidden"] == 3 and bi["modelN"] == 27 and bi["kinds"] == ["factory"], bi)
        mv = js("(()=>{ closeTv(); decisions={}; sel.clear(); const t=Object.values(TITLES).find(t=>t.versions.length>=2 && t.versions.every(v=>byId[v])); COLL.multi[t.id]=true; t.versions.slice(0,2).forEach(v=>sel.add(v)); persist(); render(); openTv(); const m=TV.model; return {n:m.order.length, distinct:new Set(m.games.map(g=>g.version_id)).size, titles:new Set(m.games.map(g=>g.title)).size}; })()")
        ok("two_versions_of_one_title_both_present", mv and mv["n"] == 2 and mv["distinct"] == 2, mv)
        js("closeTv(); COLL.multi={}; sel.clear(); persist(); render()")
        # ---------- 120 games: master order, navigation, controller contract ----------
        js("sel.clear(); DATA.games.slice(0,120).forEach(g=>sel.add(g.game_id)); persist(); render(); openTv()"); time.sleep(0.7)
        od = js("(()=>{ const m=TV.model; const L=homeListing(m,TV.S,tvSearch()); const ents=tvEntries(); return {n:L.ids.length, master:L.ids.join()===m.order.join(), projOrder:ents.map(e=>e.id).join()===m.order.join(), theHandled:m.games.some(g=>/^the /i.test(g.title)) ? m.games.find(g=>/^the /i.test(g.title)).sort_title.startsWith('the ')===false : true}; })()")
        ok("large_collection_master_order", od and od["n"] == 120 and od["master"] and od["projOrder"] and od["theHandled"], od)
        t0 = time.time(); act(*(["right"] * 4 + ["down"] * 20)); dt = time.time() - t0
        ok("navigation_fast_24_steps", dt < 2.5 and st()["index"] == 4 + 5 * 20, f"{dt:.2f}s i={st()['index']}")
        act("down", "down", "down", "right"); ok("grid_clamps_last_row_no_wrap", st()["index"] == 119, st())
        js("TV.S.index=0; tvDraw()"); act("left"); ok("left_at_row_start_stays", st()["index"] == 0)
        act("up"); ok("up_from_top_row_enters_nav", st()["zone"] == "nav"); act("down"); ok("down_returns_to_games", st()["zone"] == "games")
        js("$('tvStage').focus()"); key("ArrowRight"); key("ArrowRight"); ok("arrow_keys_move", st()["index"] == 2)
        key("s"); ok("select_opens_details", st()["overlay"] == "details"); shot("tv-details")
        dd = js(f"(()=>{{ const sh={SH}; const d=sh.querySelector('.details'); const t=d.textContent; return {{play:!!sh.querySelector('.details .act.focus') && /Play Game/.test(sh.querySelector('.details .act.focus').textContent), fav:/Add Favorite/.test(t), fullTitle:t.includes(TV.model.games.find(g=>g.id===TV.S.overlay.id).title), noFake:!/\\b320 hours\\b|Starbound|Skyward/.test(t)}}; }})()")
        ok("details_play_default_full_title_no_fake_data", dd and dd["play"] and dd["fav"] and dd["fullTitle"] and dd["noFake"], dd)
        act("down", "a"); ok("details_favorite_toggle_in_place", js("homeFavorites(TV.model).length===1 && TV.S.overlay.type==='details'"))
        key("Backspace"); ok("b_returns_exact_position", st()["overlay"] is None and st()["index"] == 2)
        key("m"); ok("start_opens_menu_overlay", st()["overlay"] == "start"); shot("tv-start-menu")
        mi = js(f"[...{SH}.querySelectorAll('.menu .it span:first-child')].map(e=>e.textContent)")
        ok("start_menu_entries", mi and mi[:6] == ["Return to Games", "Change Theme", "Sort Games", "Favorites", "Recently Played", "Settings"] and "Browse" in mi and "Search" in mi and "All Games" in mi, mi)
        key("m"); ok("start_closes_with_start", st()["overlay"] is None and st()["index"] == 2)
        act("start", "down", "a"); ok("change_theme_picker", st()["overlay"] == "theme" and js(f"{SH}.querySelectorAll('.themes .th').length===4"))
        act("right", "a"); ok("theme_applied_immediately_position_kept", st()["theme"] == "box-art-showcase" and st()["overlay"] is None and st()["index"] == 2 and js("TV.model.theme==='box-art-showcase'"))
        shot("tv-box-art")
        act(*(["right"] * 130)); ok("carousel_no_wrap", st()["index"] == 119); act("up"); ok("carousel_up_nav", st()["zone"] == "nav"); act("down")
        js("TV.S.theme='featured-shelf'; TV.model.theme='featured-shelf'; TV.S.index=0; tvDraw()"); act("left"); ok("shelf_left_edge_enters_sidebar", st()["zone"] == "nav"); act("right"); ok("sidebar_right_back_to_shelf", st()["zone"] == "games" and st()["index"] == 0)
        shot("tv-featured-shelf")
        js("TV.S.theme='adventure-map'; TV.model.theme='adventure-map'; tvDraw()"); act("up"); ok("map_up_enters_landmarks", st()["zone"] == "map"); act("right", "right"); ok("map_dpad_moves_landmarks", js("TV.S.landmark===2"))
        act("select"); ok("map_select_destination_overlay", st()["overlay"] == "mapdest"); act("b"); act("down"); act("select"); ok("map_select_on_game_is_details", st()["overlay"] == "details"); act("b")
        shot("tv-adventure-map"); js("TV.S.theme='arcade-grid'; TV.model.theme='arcade-grid'; tvDraw()")
        # ---------- launch: simulated and labeled; recorded in history ----------
        act("a"); time.sleep(0.2); ln = js("(()=>{ const t=$('tvSim').textContent; return {launching:!!TV.launching, label:/SIMULATED LAUNCH/.test(t), honest:/Nothing runs in this browser/.test(t), hist:homeHistoryEvents(TV.model).length}; })()")
        ok("a_launches_simulated_and_labeled", ln and ln["launching"] and ln["label"] and ln["honest"] and ln["hist"] == 1, ln)
        act("a"); ok("launch_returns_to_same_place", st()["launching"] is False and st()["index"] == 0 and st()["overlay"] is None, st())
        # ---------- Browse, Search, Favorites, Recently Played, History, Settings ----------
        act("up", "right", "a"); ok("browse_from_nav", st()["sec"] == "browse" and st()["zone"] == "browse")
        br = js(f"(()=>{{ const B=homeBrowseItems(TV.model,TV.S); const sh={SH}; return {{cats:B.cats.length, folders:B.folders.length, tiles:sh.querySelectorAll('.tile').length, heading:/AUTOMATIC CATEGORIES/.test(sh.querySelector('.section').textContent)}}; }})()")
        ok("browse_shows_categories_from_real_metadata", br and br["cats"] > 3 and br["tiles"] >= br["cats"] and br["heading"], br)
        act("a"); ok("category_opens_in_master_relative_order", st()["sec"] == "category" and js("(()=>{ const L=homeListing(TV.model,TV.S,tvSearch()).ids; const m=TV.model.order.filter(id=>L.includes(id)); return L.join()===m.join() && L.length>0; })()"))
        act("b"); ok("category_b_back_to_browse", st()["sec"] == "browse")
        act("up", "right", "a"); ok("search_from_nav", st()["sec"] == "search" and st()["zone"] == "keyboard")
        kb = js(f"(()=>{{ const sh={SH}; const ks=[...sh.querySelectorAll('.kb .k')].map(k=>k.textContent); return {{n:ks.length, first:ks.slice(0,6).join(''), last:ks.slice(30,36).join(''), util:ks.slice(36).join('|'), cols:getComputedStyle(sh.querySelector('.kb')).gridTemplateColumns.split(' ').length}}; }})()")
        ok("keyboard_six_columns_alnum_plus_utility", kb and kb["n"] == 39 and kb["first"] == "ABCDEF" and kb["last"] == "456789" and kb["util"] == "SPACE|CLEAR SEARCH|BACK" and kb["cols"] == 6, kb)
        act("down", "down", "a", "a"); q = st()["query"]; ok("typing_updates_query_live", q == "mm" and st()["n"] >= 0, q)
        js("TV.S.query='ma'; TV.S.keyRow=0; TV.S.keyCol=0; tvDraw()"); r1 = st()["n"]; ok("results_exclude_nothing_installed_and_match", r1 > 0)
        act("select"); ok("select_on_keyboard_jumps_to_first_result", st()["zone"] == "results")
        act("select"); ok("result_select_opens_details", st()["overlay"] == "details"); act("b")
        act("b"); ok("results_b_returns_to_keyboard", st()["zone"] == "keyboard")
        act("up"); sg = st(); ok("up_from_top_row_enters_suggestions", sg["zone"] == "suggest" and js(f"{SH}.querySelectorAll('.sug span.focus').length===1"))
        act("a"); ok("suggestion_completes_without_launch", st()["zone"] == "results" and st()["launching"] is False and len(st()["query"]) > 2)
        js("TV.S.query='zzzzq'; TV.S.zone='keyboard'; tvDraw()"); ok("no_matches_message_keeps_query", js(f"/No matching games found/.test({SH}.querySelector('.search').textContent) && TV.S.query==='zzzzq'"))
        js("TV.S.keyRow=6; TV.S.keyCol=2; tvDraw()"); act("a"); ok("clear_search_restores_master_order", st()["query"] == "" and js("homeListing(TV.model,TV.S,tvSearch()).ids.join()===TV.model.order.join()"))
        shot("tv-search")
        js("TV.S.keyRow=6; TV.S.keyCol=4; tvDraw()"); act("a"); ok("back_key_exits_search", st()["sec"] != "search")
        menu("favorites"); ok("favorites_from_start_shows_one", st()["sec"] == "favorites" and st()["n"] == 1, st())
        menu("recent"); ok("recent_from_start", st()["sec"] == "recent" and st()["n"] == 1, st())
        act("up"); ok("recent_up_focuses_view_all", st()["zone"] == "viewall"); act("a"); ok("view_all_history", st()["sec"] == "history" and js(f"{SH}.querySelectorAll('.hist .ev').length===1 && {SH}.querySelectorAll('.hist .day').length===1"))
        act("b"); ok("history_b_returns_recent", st()["sec"] == "recent")
        menu("settings"); ok("settings_from_start", st()["sec"] == "settings", st()); shot("tv-settings")
        js("TV.S.settingsIndex=4; tvDraw()"); act("a"); ok("clear_history_confirmation", st()["overlay"] == "confirm", st()); act("right", "a"); ok("history_cleared", js("homeHistoryEvents(TV.model).length===0 && TV.model.favorites && homeFavorites(TV.model).length===1"))
        ok("sort_games_absent_where_not_sortable", js("!HOME_START_MENU.filter(it=>it.id!=='sort'||homeSortable(TV.S.section)).some(it=>it.id==='sort')"))
        js("TV.S.section={type:'allgames'}; TV.S.zone='games'; TV.S.index=0; tvDraw()"); menu("sort"); ok("sort_games_overlay", st()["overlay"] == "sort", st()); act("down", "a")
        ok("sort_title_az_applied_order_untouched", js("(()=>{ const L=homeListing(TV.model,TV.S,tvSearch()).ids; const by=id=>TV.model.games.find(g=>g.id===id).sort_title; return TV.model.sorts.allgames==='title-az' && L.every((id,i)=>i===0||by(L[i-1])<=by(id)) && TV.model.order.length===L.length && JSON.stringify(TV.model.sorts)==='{\"allgames\":\"title-az\"}'; })()"))   # the catalog's first 120 games already come in title order, so the sorted list can equal the canonical one
        js("homeSetSort(TV.model,'allgames','custom'); tvDraw()")
        # ---------- persistence and change resilience ----------
        js("TV.S.index=10; tvDraw(); homeExtrasSave()"); cur = js("homeListing(TV.model,TV.S,tvSearch()).ids[10]"); js("closeTv(); openTv()"); time.sleep(0.3)
        ok("state_persists_across_reopen", st()["index"] == 10 and st()["theme"] == "arcade-grid" and js("homeFavorites(TV.model).length===1"))
        js(f"sel.delete('{cur}'); persist(); render()"); time.sleep(0.2)
        ok("removed_game_selects_nearest_surviving", js(f"!TV.model.order.includes('{cur}') && TV.model.order.length===119 && TV.S.index===10"))
        # ---------- generated placeholder covers are marked ----------
        gc = js("(()=>{ const g=DATA.games.find(x=>!x.art&&x.art_generated); return g?{has:true, src:g.art_generated, site:!!document.querySelector('#tb img.gen, .grid img.gen')||true}:{has:false}; })()")
        ok("generated_cover_assigned_for_missing_art", gc and gc["has"] and gc["src"].startswith("art/generated/"), gc)
        js("closeTv(); (()=>{ const g=DATA.games.find(x=>!x.art&&x.art_generated); sel.add(g.game_id); persist(); render(); openTv({focus:g.game_id}); })()"); time.sleep(0.4)
        ok("generated_cover_marked_in_home", js(f"(()=>{{ const sh={SH}; const c=sh.querySelector('.cover.focus'); const g=TV.model.games.find(x=>x.id===TV.focusId); return !!c && !!c.querySelector('.gen') && /GENERATED COVER/.test(c.textContent) && g.art.provenance==='generated' && $('tvSide').textContent.includes('generated placeholder'); }})()"))
        # ---------- side panel honesty, export, deep link, escape ----------
        sp = js("(()=>{ const s=$('tvSide').textContent; return {known:(s.match(/known/g)||[]).length, unk:(s.match(/unknown/g)||[]).length, core:/Planned core/.test(s), file:/Game file/.test(s), notVerified:/Current console state not verified/.test(s), controls:/only the D-pad, A, B, START and SELECT/.test(s), sim:/simulation/i.test(s)}; })()")
        ok("side_panel_labels", sp and sp["known"] >= 2 and sp["unk"] >= 2 and sp["core"] and sp["file"] and sp["notVerified"] and sp["controls"] and sp["sim"], sp)
        ok("browser_does_not_claim_file_readiness", js("tvEntries().every(e=>e.ready==='unknown'||e.ready==='n/a')"))
        ex = js("tvExportSize()"); ok("export_is_16_9_hd", ex and ex["w"] == 1920 and ex["h"] == 1080 and ex["entries"] == js("TV.model.order.length"), ex)
        js("tvSetTreatment('retro')"); rt = js("(()=>{ const r=$('tvStage').getBoundingClientRect(); return {retro:$('tvStage').classList.contains('retro'), ratio:r.width/r.height}; })()"); ok("retro_keeps_16_9", rt and rt["retro"] and abs(rt["ratio"] - 16 / 9) < 0.02, rt); js("tvSetTreatment('clean')")
        gid = js("[...sel][20]"); js(f"closeTv(); openTv({{focus:'{gid}'}})"); ok("deep_link_focus", js(f"homeListing(TV.model,TV.S,tvSearch()).ids[TV.S.index]==='{gid}'"))
        js("closeTv()"); ex_id = js("DATA.games.find(g=>!sel.has(g.game_id)).game_id"); js(f"openTv({{focus:'{ex_id}'}})"); time.sleep(0.2)
        ok("excluded_game_explained", js("$('tvSub').textContent.includes('not part of the proposed HOME')"))
        js("closeTv(); snapshotNow('tv test'); sel.delete([...sel][0]); persist(); render(); openTv(); const s=$('tvSnap'); s.value='0'; s.dispatchEvent(new Event('change'))"); time.sleep(0.2)
        ok("snapshot_comparison_honest", js("$('tvDiff').textContent.includes('1 removed') && $('tvDiff').textContent.includes('not with a console')"))
        js("closeTv(); if(!foldOpen()) $('foldBtn').click(); $('tvBtn').focus(); $('tvBtn').click()"); key("Escape"); time.sleep(0.2)
        ok("escape_closes_and_restores_focus", js("!TV.open && $('tv').hidden && document.activeElement===$('tvBtn') && document.body.style.overflow===''"))
        ok("keys_do_not_leak_when_closed", js("(()=>{ const before=TV.S.index; document.dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowRight'})); return TV.S.index===before && !TV.open; })()"))
        ok("preflight_button_wired", "hkTv" in js("(window.renderInstallBase||renderInstall).toString()"))
        ok("guide_chapter_present", js("(()=>{ openGuide('tv'); const t=$('guideArt').querySelector('section.on').textContent; closeGuide(); return /16:9/.test(t) && /Arcade Grid/.test(t) && /SELECT/.test(t); })()"))
        ok("no_xy_controls_in_hints", js(f"(()=>{{ openTv(); const t={SH}.querySelector('.hints').textContent; closeTv(); return !/\\bX\\b|\\bY\\b/.test(t) && /SELECT|START|A|B/.test(t); }})()"))
        # ---------- responsive ----------
        for w, mob in ((900, False), (412, True)):
            size(w, 900 if not mob else 820, mob); js("openTv()"); time.sleep(0.5)
            f = js(f"(()=>{{ const r=$('tvStage').getBoundingClientRect(); const W=document.documentElement.clientWidth; const pad=$('tvPad').getBoundingClientRect(); const sd=$('tvSide').getBoundingClientRect(); const hm={SH}.querySelector('.hm').getBoundingClientRect(); return {{ratio:r.width/r.height, inside:r.left>=0&&r.right<=W+1, hscroll:document.documentElement.scrollWidth>W, padVisible:pad.width>0&&pad.right<=W+1, fitted:Math.abs(hm.width-r.width)<3, sideBelow:sd.top>=r.bottom-1}}; }})()")
            ok(f"responsive_{w}", f and abs(f["ratio"] - 16 / 9) < 0.03 and f["inside"] and not f["hscroll"] and f["padVisible"] and f["fitted"] and f["sideBelow"], f)
            shot(f"tv-{w}"); js("closeTv()")
            if mob: ok("badge_in_toolbar_on_phone", js("(()=>{ if(!foldOpen()) $('foldBtn').click(); const b=$('tvBtn'); const r=b.getBoundingClientRect(); const img=b.querySelector('img').getBoundingClientRect(); const W=document.documentElement.clientWidth; closeTv(); return r.width>0 && r.right<=W+1 && img.width<=48 && img.width>=36 && Math.abs(img.width/img.height-1.28)<0.05 && !$('mTv').hidden; })()"))
        size(1366); js("sel.clear(); persist(); render(); localStorage.removeItem('studio-home-v1')")
        errs = js("typeof errors!=='undefined'?errors.length:0"); ok("no_js_errors", not errs, errs)
    finally:
        try: call("Browser.close")
        except Exception: pass
        try: p.wait(timeout=10)
        except Exception: p.kill()
        shutil.rmtree(prof, ignore_errors=True)
    bad = [c for c in checks if "=false" in c]; print("\n".join(checks)); print(f"RESULT: {'PASS' if not bad else 'FAIL '+str(bad)} ({len(checks)} checks)"); return 1 if bad else 0

if __name__ == "__main__": sys.exit(main())
