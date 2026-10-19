r"""Website checks for My Collections, the new navigation and Share Collection (headless Edge, no Windows app, no backend).

The built site is served on 127.0.0.1 (website mode: no desktop backend) and the share Worker runs locally with in-memory storage
(tools/share-worker/test/dev-server.mjs). A persistent browser profile is used so "browser restarts" are real restarts.

  collections   migration of an existing single collection; three named collections with independent selections and exact
                versions; switch; rename; duplicate; delete and restore; export / import round trip; persistence across a restart
  favorites     Select → Show Favorites shows only favorites without changing the selection, and leaving it is one click
  sharing       publish from the dialog, the share page's Open Graph / X card metadata in the first HTML response, the card image,
                Save to My Collections in a clean profile (independent copy; the sender's collection unchanged), share actions'
                generated URLs and text, unpublish (the link stops working), invalid payload rejected
  layout        screenshots of the toolbar and its menus at 1440, 1024 and 390 px wide (saved for review), no horizontal scroll

  python test_collections_web.py <built site folder> [--shots DIR]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, json, os, re, shutil, socket, subprocess, sys, tempfile, threading, time, urllib.request, urllib.error, http.server, functools
HERE = os.path.dirname(os.path.abspath(__file__)); TOOLS = os.path.dirname(HERE); sys.path.insert(0, HERE)
import cdp_edge
from cdp_edge import EdgeSession as _Edge
class EdgeSession(_Edge):
    """the harness session with a persistent profile (a real browser restart keeps local storage); the profile is not deleted on close"""
    def __init__(self, profile=None, **kw):
        kw.pop("extra_args", None); super().__init__(**kw); self._p = profile
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

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--shots", default=""); a = ap.parse_args()
    shots = a.shots or os.path.join(tempfile.gettempdir(), "ccs-web-shots"); os.makedirs(shots, exist_ok=True)
    checks = []
    def ok(k, v, n=""): checks.append((k, bool(v), n)); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:300]})" if n and not v else ""), flush=True)
    web = serve(a.site); WEB = f"http://127.0.0.1:{web.server_address[1]}"
    sport = free_port(); SHARE = f"http://127.0.0.1:{sport}"
    node = shutil.which("node"); dev = subprocess.Popen([node, os.path.join(TOOLS, "share-worker", "test", "dev-server.mjs"), str(sport), WEB], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    prof = tempfile.mkdtemp(prefix="ccs-web-profile-"); prof2 = tempfile.mkdtemp(prefix="ccs-web-clean-"); prof3 = tempfile.mkdtemp(prefix="ccs-web-off-")
    URL = f"{WEB}/index.html?shareapi={SHARE}"
    READY = "typeof CS!=='undefined'&&!!CS&&typeof byId!=='undefined'&&typeof csList==='function'&&document.readyState==='complete'"
    try:
        for _ in range(40):
            try: urllib.request.urlopen(SHARE + "/c/none", timeout=1)
            except urllib.error.HTTPError: break
            except Exception: time.sleep(0.25)
        # ---------------- session 1: an existing single-collection user (0.6.1 keys) ----------------
        with EdgeSession(width=1440, height=900, profile=prof) as ed:
            ed.navigate(WEB + "/robots.txt")
            ids = None
            ed.js("""localStorage.setItem('nes-classic-selection-v1', JSON.stringify(['e9f4c9d1b7c66c6a','fcb6a0ef3a20c19b'])); localStorage.setItem('nes-classic-decisions-v1', JSON.stringify({'e9f4c9d1b7c66c6a':'factory'}));
                     localStorage.setItem('studio-collection-v1', JSON.stringify({name:'My Old Collection', created:'2026-10-01T10:00:00.000Z', modified:'2026-10-18T10:00:00.000Z', pref:{}, multi:{}, avail:[], placeholders:[]}));
                     localStorage.setItem('studio-home-v1', JSON.stringify({v:1, folders:[{id:'f:a', name:'Racing', parent:null, order:['e9f4c9d1b7c66c6a']}], theme:'adventure-map', favorites:{items:{}}, history:{}})); localStorage.setItem('studio-welcomed-v1','1'); 'ok'""")
            ed.navigate(URL); ed.wait_for(READY, timeout=120)
            st = ed.js("JSON.stringify({n:csList().length, name:csActive().name, sel:csActive().sel.slice().sort(), dec:csActive().decisions, home:(csActive().home||{}).theme, folders:((csActive().home||{}).folders||[]).length, src:CS.migrated.from, keep:!!localStorage.getItem('nes-classic-selection-v1'), web:COMPANION.state})")
            st = json.loads(st)
            ok("migration_keeps_the_existing_collection", st["n"] == 1 and st["name"] == "My Old Collection" and st["sel"] == ["e9f4c9d1b7c66c6a", "fcb6a0ef3a20c19b"] and st["dec"] == {"e9f4c9d1b7c66c6a": "factory"} and st["home"] == "adventure-map" and st["folders"] == 1 and "0.6.1" in st["src"] and st["keep"], st)
            ok("website_needs_no_backend", st["web"] == "web", st["web"])
            ok("share_shown_when_the_service_is_configured", ed.js("!$('mShare').hidden&&SHARE_ON===true"))
            ed.navigate(URL); ed.wait_for(READY, timeout=120)
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
            ok("rename_and_duplicate", ed.js(f"CS.items['{bid}'].name") == "Bravo Renamed" and sorted(json.loads(ed.js(f"JSON.stringify(CS.items['{dup}'].sel)"))) == sorted(pick[:3]) and ed.js(f"CS.items['{dup}'].name") == "Alpha copy")
            cid = ed.js("csList().find(r=>r.name==='Charlie').id"); ed.js(f"csDelete('{cid}')")
            snap = ed.js("JSON.parse(localStorage.getItem('studio-snapshots-v1')||'[]').some(s=>s.name==='Charlie')")
            ok("delete_with_recovery_snapshot", not ed.js(f"!!CS.items['{cid}']") and snap)
            ed.js("restoreSnapshot(JSON.parse(localStorage.getItem('studio-snapshots-v1')).findIndex(s=>s.name==='Charlie'))")
            ok("restore_previous_version", sorted(json.loads(ed.js("JSON.stringify([...sel])"))) == [pick[5]], ed.js("JSON.stringify([...sel])"))
            ed.js(f"csSwitch('{aid}')")
            exp = json.loads(ed.js(f"JSON.stringify(csFileFor(CS.items['{aid}']))"))
            ok("export_file_is_schema4_plus_record", exp.get("schema") == "nes-classic-collection/4" and exp.get("ccs_collection", {}).get("schema") == "ccs-collection/1" and exp.get("count") == 3, {k: exp.get(k) for k in ("schema", "count", "error")} if isinstance(exp, dict) else exp)
            ed.js(f"csImportObject({json.dumps(exp)}, 'x.json')")
            imp = ed.js("JSON.stringify((r=>r?({name:r.name,sel:r.sel.slice().sort(),act:r.id===CS.active}):{names:csList().map(x=>x.name)})(csList().find(r=>r.name==='Alpha (2)')))"); imp = json.loads(imp) if isinstance(imp, str) else imp
            ok("import_round_trip_new_collection", imp.get("name") == "Alpha (2)" and imp.get("sel") == sorted(pick[:3]) and imp.get("act"), imp)
            ed.js(f"csSwitch('{bid}')")
            # favorites from the Select menu: a view, never a selection change
            favs = pick[:2]; ed.js(f"{json.dumps(favs)}.forEach(i=>fav.add(i)); persist(); render();")
            before = sorted(json.loads(ed.js("JSON.stringify([...sel])")))
            ed.js("$('selectBtn').click()"); ok("select_menu_has_favorites", ed.js("!$('selectMenu').hidden&&$('favView').textContent.includes('Show Favorites')"), ed.js("$('favView').textContent"))
            ed.js("$('favView').click()"); time.sleep(0.4)
            vis = json.loads(ed.js("JSON.stringify(visible().map(g=>g.game_id))"))
            ok("favorites_view_shows_only_favorites", sorted(vis) == sorted(favs) and ed.js("$('fSel').value") == "fav", vis)
            ok("favorites_view_keeps_selection", sorted(json.loads(ed.js("JSON.stringify([...sel])"))) == before)
            ed.js("$('selectBtn').click(); $('favView').click()"); time.sleep(0.3)
            ok("favorites_view_one_click_to_leave", ed.js("$('fSel').value") == "" and ed.js("visible().length") > 100)
            active_name = ed.js("csActive().name")
            # layout: the toolbar and menus at three widths
            ed.js("try{ doneWelcome(); }catch(e){} document.querySelectorAll('.modal.open').forEach(m=>m.classList.remove('open')); 1")
            for w, h in ((1440, 900), (1024, 768), (390, 844)):
                ed.call("Emulation.setDeviceMetricsOverride", {"width": w, "height": h, "deviceScaleFactor": 1, "mobile": w < 500}); time.sleep(0.8); ed.js("window.scrollTo(0,0)")
                ok(f"no_horizontal_scroll_{w}", ed.js("document.documentElement.scrollWidth<=innerWidth+1"), ed.js("document.documentElement.scrollWidth"))
                for name, btn in (("bar", None), ("collections", "collBtn"), ("menu", "appBtn"), ("select", "selectBtn")):
                    ed.js("document.querySelectorAll('.menu').forEach(x=>x.hidden=true)")
                    if btn: ed.js(f"$('{btn}').click()"); time.sleep(0.4)
                    if btn:
                        r = json.loads(ed.js(f"JSON.stringify((()=>{{ const m=$('{btn}').closest('.menuwrap').querySelector('.menu'); const b=m.getBoundingClientRect(); return [b.left,b.right,innerWidth]; }})())"))
                        ok(f"menu_{name}_inside_viewport_{w}", r[0] >= -1 and r[1] <= r[2] + 1, r)
                    open(os.path.join(shots, f"{w}-{name}.png"), "wb").write(ed.screenshot())
                ed.js("document.querySelectorAll('.menu').forEach(x=>x.hidden=true)")
            ed.call("Emulation.setDeviceMetricsOverride", {"width": 1440, "height": 900, "deviceScaleFactor": 1, "mobile": False})
            # sharing: publish through the dialog
            ed.js(f"csSwitch('{aid}')"); ed.js("openShare()"); ed.wait_for("!!$('shImg')&&$('shImg').naturalWidth>0", timeout=30)
            open(os.path.join(shots, "share-dialog.png"), "wb").write(ed.screenshot())
            ed.js("$('shDesc').value='Three of my favorites.'; $('shGo').click()"); ed.wait_for("!!(SH.pub&&SH.pub.url)", timeout=30)
            pub = json.loads(ed.js("JSON.stringify(SH.pub)")); share_id = pub["id"]
            ok("publish_from_dialog", re.match(r"^[A-Za-z0-9]{10}$", share_id) and pub["url"] == f"{SHARE}/c/{share_id}", pub)
            open(os.path.join(shots, "share-published.png"), "wb").write(ed.screenshot())
            tok = ed.js(f"csActive().shares.find(s=>s.id==='{share_id}').token"); ok("manage_token_kept_locally_not_in_link", bool(tok) and tok not in pub["url"])
            page = urllib.request.urlopen(f"{SHARE}/c/{share_id}").read().decode()
            ok("share_page_metadata_in_first_html", '<meta property="og:title" content="Alpha"' in page and f'{SHARE}/c/{share_id}/card.png' in page and 'twitter:card" content="summary_large_image"' in page and "Three of my favorites." in page, page[:400])
            img = urllib.request.urlopen(f"{SHARE}/c/{share_id}/card.png").read(); ok("share_card_png_1200x630", img[:8] == b"\x89PNG\r\n\x1a\n" and int.from_bytes(img[16:20], "big") == 1200 and int.from_bytes(img[20:24], "big") == 630)
            snapj = json.loads(urllib.request.urlopen(f"{SHARE}/api/c/{share_id}").read()); keys = {k for g in snapj["games"] for k in g}
            ok("published_fields_allowlisted", keys <= {"id", "title", "version", "year", "publisher", "art", "source", "content_sha256", "file_sha256", "size", "thumb", "thumb_url"} and "token" not in json.dumps(snapj) and len(snapj["games"]) == 3, keys)
            # share actions: generated URLs and text (the windows they open are not followed)
            ed.js("window.__opened=[]; window.open=(u)=>{ window.__opened.push(String(u)); return null; }; navigator.clipboard.writeText=async t=>{ window.__clip=t; };")
            for k in ("facebook", "x", "linkedin", "copy"): ed.js(f"shDest('{k}')"); time.sleep(0.3)
            opened = json.loads(ed.js("JSON.stringify(window.__opened)")); E = urllib.parse.quote(pub["url"], safe="")
            ok("share_facebook_url", any(u.startswith("https://www.facebook.com/sharer/sharer.php?u=" + E) for u in opened), opened)
            ok("share_x_url_with_text", any(u.startswith("https://x.com/intent/post?text=") and ("url=" + E) in u and "Check%20out%20my%20Alpha" in u for u in opened), opened)
            ok("share_linkedin_url", any(u == "https://www.linkedin.com/sharing/share-offsite/?url=" + E for u in opened), opened)
            ok("copy_link", ed.js("window.__clip") == pub["url"])
            ed.js("window.__loc=[]; Object.defineProperty(window,'__mail',{value:1}); const _sd=shDest; window.__hrefs=[]; const desc=Object.getOwnPropertyDescriptor(Location.prototype,'href');")
            mail = ed.js("(()=>{ const p=SH.pub, u=p.url; const msg=$('shMsg').value; const E=encodeURIComponent; const subj=p.name+' · an NES Classic collection'; return 'mailto:?subject='+E(subj)+'&body='+E(msg.replace(u,'').trim()+'\\n\\n'+u); })()")
            ok("email_mailto_prefilled_recipients_empty", mail.startswith("mailto:?subject=Alpha") and E in mail and "&to=" not in mail, mail[:120])
            ed.js("window.showSaveFilePicker=undefined; shDest('instagram'); 1"); ed.wait_for("!!document.getElementById('savem')&&document.getElementById('savem').classList.contains('open')", timeout=10); ed.js("$('savemS').click()"); time.sleep(0.8)
            ok("instagram_desktop_fallback_honest", "instagram.com" in ed.js("JSON.stringify(window.__opened)") and "caption" in ed.js("$('shHint').textContent"), ed.js("$('shHint').textContent"))
            closeall = ed.js("document.getElementById('savem')&&document.getElementById('savem').classList.remove('open'); 1")
            sender_before = sorted(json.loads(ed.js(f"JSON.stringify(CS.items['{aid}'].sel)")))
        # ---------------- without a test service: Share Collection follows share-config.json as shipped ----------------
        with EdgeSession(width=1280, height=900, profile=prof3) as ed:
            ed.navigate(f"{WEB}/index.html"); ed.wait_for(READY, timeout=120); time.sleep(1.5)
            cfg = {}
            try: cfg = json.load(open(os.path.join(a.site, "share-config.json"), encoding="utf-8"))
            except Exception: pass
            if cfg.get("enabled") is True: ok("share_follows_share_config_on", ed.js("!$('mShare').hidden&&SHARE_ON===true&&SHARE_API===" + json.dumps(cfg.get("api"))))
            else: ok("share_hidden_when_service_off", ed.js("$('mShare').hidden&&SHARE_ON===false&&document.body.classList.contains('noshare')"))
        # ---------------- session 2: the same profile after a browser restart ----------------
        with EdgeSession(width=1440, height=900, profile=prof) as ed:
            ed.navigate(URL); ed.wait_for(READY, timeout=120)
            names = sorted(json.loads(ed.js("JSON.stringify(csList().map(r=>r.name))")))
            ok("collections_persist_across_restart", names == sorted(["My Old Collection", "Alpha", "Bravo Renamed", "Alpha copy", "Alpha (2)", "Charlie"]), names)
            ok("active_collection_persists", ed.js("csActive().name") == "Alpha" and sorted(json.loads(ed.js("JSON.stringify([...sel])"))) == sender_before)
            ok("favorites_persist", sorted(json.loads(ed.js("JSON.stringify([...fav])"))) == sorted(pick[:2]))
        # ---------------- session 3: a recipient in a clean browser ----------------
        with EdgeSession(width=1280, height=900, profile=prof2) as ed:
            ed.navigate(f"{WEB}/index.html?shareapi={SHARE}&share={share_id}"); ed.wait_for(READY, timeout=120)
            ed.wait_for("!!$('srSave')", timeout=30); open(os.path.join(shots, "share-received.png"), "wb").write(ed.screenshot())
            ok("recipient_sees_collection_without_change", ed.js("$('collTitle').textContent") == "Alpha" and ed.js("sel.size") == 0)
            ed.js("$('srSave').click()"); ed.wait_for("!!$('srOpen')", timeout=10); ed.js("$('srOpen').click()")
            ok("recipient_saved_independent_copy", ed.js("csActive().name") == "Alpha" and sorted(json.loads(ed.js("JSON.stringify([...sel])"))) == sender_before and ed.js("csList().length") == 2)
            ok("share_param_removed_from_address", "share=" not in ed.js("location.search"))
        # ---------------- unpublish and invalid input ----------------
        with EdgeSession(width=1280, height=900, profile=prof) as ed:
            ed.navigate(URL); ed.wait_for(READY, timeout=120)
            ed.js("window.confirm=()=>true"); ed.js(f"shUnpublish('{share_id}')"); time.sleep(1.5)
            try: urllib.request.urlopen(f"{SHARE}/c/{share_id}"); gone = False
            except urllib.error.HTTPError as e: gone = e.code == 410
            ok("unpublish_stops_the_link", gone and ed.js(f"!!csActive().shares.find(s=>s.id==='{share_id}').unpublished"))
            ok("sender_collection_unaffected", sorted(json.loads(ed.js("JSON.stringify([...sel])"))) == sender_before)
        req = urllib.request.Request(SHARE + "/api/publish", data=json.dumps({"schema": "ccs-share/2", "name": "x", "games": [{"id": "../etc", "title": "x"}], "image": ""}).encode(), headers={"Content-Type": "application/json", "Origin": WEB}, method="POST")
        try: urllib.request.urlopen(req); rej = False
        except urllib.error.HTTPError as e: rej = e.code == 400
        ok("invalid_publish_rejected", rej)
    finally:
        dev.kill(); web.shutdown()
        for p in (prof, prof2, prof3): shutil.rmtree(p, ignore_errors=True)
    bad = [k for k, v, _ in checks if not v]; print("RESULT:", "PASS" if not bad else "FAIL " + json.dumps(bad), f"({len(checks)} checks; screenshots in {shots})"); return 0 if not bad else 1

if __name__ == "__main__":
    import urllib.parse
    sys.exit(main())
