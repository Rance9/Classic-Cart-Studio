r"""Collections only (D-146) on the built website, headless Edge, one fresh browser profile per scenario. Every check reads the stored
collections and the page itself.
  fresh       a new profile: the Included Games are prepared once (their files verified and kept in the browser's game store first) and
              their collection opens with Play on each game; no catalog, no dialog or tour; nothing else was created
  every       browsing the Game Catalog, then a reload: the collection view of the same collection, never the catalog
  once        a starter game removed, then the starter collection removed (the last one): reloads add nothing back; the empty Studio
              offers Add ROM Files, Restore a Backup and New Collection
  concurrent  two copies of the page start together on a new profile: exactly one Included Games collection
  unreadable  stored collections that cannot be read: said plainly; nothing is written over them (byte for byte), nothing is set up
  migrate     a store written by v1p2 with lineups (distinct HOME themes, one equal to its collection, a name already taken, one open):
              each distinct lineup becomes its own collection with its games, versions and HOME settings, the open lineup's collection is
              open, the store before is kept once; a reload, a second migration from another page and a half-done earlier migration add
              nothing twice
  restore     an older JSON backup with lineups: restored as collections; restoring it again adds nothing
  python test_collections_model_web.py <built site folder> [--out results.json] [--shots DIR]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, functools, http.server, json, os, sys, tempfile, threading, time
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from studio_harness import EdgeSession, READY, CALM, free_port

CSKEY = "studio-collections-v2"
STATE = """JSON.stringify((()=>{ return {state:CS_STATE, fresh:CS_FRESH, starter:CS.starter||null, active:CS.active, mode:CV_MODE,
 colls:csList().map(c=>({id:c.id, name:c.name, sel:(c.sel||[]).slice().sort(), home:c.home||null, decisions:c.decisions||{}, mark:!!(c.coll&&c.coll.included), from:c.fromLineup||null, loadouts:c.loadouts||null})),
 rows:+document.documentElement.dataset.rows, play:document.querySelectorAll('#gridView [data-cvplay],#listView [data-cvplay]').length,
 rdy:Object.fromEntries([...rdyS().st.entries()]), panel:($('cvPanel').hidden?'':$('cvPanel').innerText), dialog:($('collm').classList.contains('open')?$('collTitle').textContent:''),
 welcome:!!document.querySelector('#welcome.open'), stored:localStorage.getItem('studio-collections-v2')}; })())"""

def serve_with_pair(folder):
    """the built site, plus /__pair.html: two copies of the page in iframes that start at the same moment (one origin, one profile)"""
    pair = b"<!doctype html><meta charset=utf-8><title>pair</title><iframe id=a src='index.html?a' width=900 height=600></iframe><iframe id=b src='index.html?b' width=900 height=600></iframe>"
    class Q(http.server.SimpleHTTPRequestHandler):
        def log_message(self, *a): pass
        def do_GET(self):
            if self.path.startswith("/__pair.html"):
                self.send_response(200); self.send_header("Content-Type", "text/html"); self.send_header("Content-Length", str(len(pair))); self.end_headers(); self.wfile.write(pair); return
            return super().do_GET()
        def handle(self):
            try: super().handle()
            except Exception: pass
    class S(http.server.ThreadingHTTPServer): request_queue_size = 256   # the page's service worker asks for ~200 files at once: the default backlog of 5 drops some
    srv = S(("127.0.0.1", free_port()), functools.partial(Q, directory=folder)); srv.handle_error = lambda *a: None
    threading.Thread(target=srv.serve_forever, daemon=True).start(); return srv

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); ap.add_argument("--shots", default=""); a = ap.parse_args()
    idx = json.load(open(os.path.join(a.site, "included", "index.json"), encoding="utf-8")); inc = sorted("p_" + g["content_sha256"][:16] for g in idx["games"])
    srv = serve_with_pair(a.site); base = f"http://127.0.0.1:{srv.server_address[1]}"; url = base + "/index.html"
    res, log = [], {}
    def ok(n, c, d=""):
        res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:300]})" if d and not c else ""), flush=True)
    def st(ed): return json.loads(ed.js(STATE))
    def boot(ed, u=url, settle=True):
        ed.navigate(u)
        try: ed.wait_for(READY, timeout=120)
        except Exception:
            print('page did not start:', json.dumps(ed.exceptions[-3:])[:2000], ed.console_errors[-3:], ed.js("JSON.stringify({ready:document.documentElement.dataset.ready, rs:document.readyState, cs:typeof CS})"), flush=True); raise
        ed.js(CALM)
        if settle: ed.wait_for("!(CS.starter&&CS.starter.state!=='done'&&CS.starter.state!=='failed')||CS_STATE!=='ok'", timeout=90); time.sleep(1.2)
    def seed(ed, js):   # storage is prepared from a page of the same site that runs none of the app (an open app page could save over it)
        ed.navigate(base + "/robots.txt"); time.sleep(0.4); ed.js(js)
    def shot(ed, name):
        if a.shots: os.makedirs(a.shots, exist_ok=True); open(os.path.join(a.shots, name), "wb").write(ed.screenshot())
    try:
        # ---- fresh, every, once ----
        prof = tempfile.mkdtemp(prefix="ccs-model-fresh-")
        with EdgeSession(profile=prof, width=1440, height=900) as ed:
            boot(ed); s = st(ed); log["fresh"] = s
            ok("fresh: one collection, the Included Games, open", len(s["colls"]) == 1 and s["colls"][0]["name"] == "Included Games" and s["active"] == s["colls"][0]["id"] and s["colls"][0]["mark"], s["colls"])
            ok("fresh: it holds exactly the licensed Included Games", s["colls"] and s["colls"][0]["sel"] == inc, s["colls"])
            ok("fresh: each game's file is verified and kept before it shows as ready", s["rdy"] == {i: "ready" for i in inc}, s["rdy"])
            kept = ed.js("CCSROMS.keys(true).then(k=>JSON.stringify([...k]))"); kept = json.loads(kept)
            ok("fresh: the browser's game store holds both exact files", all(any(k.startswith(i[2:]) for k in kept) for i in inc), kept)
            ok("fresh: the collection view, with Play on each game", s["mode"] == "collection" and s["rows"] == len(inc) and s["play"] == len(inc), (s["mode"], s["rows"], s["play"]))
            ok("fresh: no dialog, tour or welcome in the way", not s["dialog"] and not s["welcome"], (s["dialog"], s["welcome"]))
            ok("fresh: the starter is remembered as done", (s["starter"] or {}).get("state") == "done", s["starter"])
            ok("fresh: Add Game Files and Restore a Backup are visible", ed.js("['addBtn','restoreBtn','tvBtn'].every(i=>{ const b=$(i); return b&&b.offsetParent!==null; })") is True)
            shot(ed, "fresh-1440.png")
            # every launch: the catalog is a lookup, never where the Studio starts
            ed.js("cvCatalog(true); 1"); time.sleep(0.5); cat = st(ed)
            ok("every: the Game Catalog lookup lists catalog games and offers Add Game File…", cat["mode"] == "catalog" and cat["rows"] > 100 and ed.js("document.querySelectorAll('[data-cvadd]').length>0") is True, (cat["mode"], cat["rows"]))
            boot(ed); s2 = st(ed)
            ok("every: after a reload, the same collection in the collection view", s2["mode"] == "collection" and s2["active"] == s["active"] and s2["rows"] == len(inc), (s2["mode"], s2["active"], s2["rows"]))
            # once: a removed starter game and a removed starter collection never come back
            ed.js("cvRemove(%s); 1" % json.dumps(inc[0])); time.sleep(0.6); boot(ed); s3 = st(ed)
            ok("once: a removed Included Game stays removed after a reload", len(s3["colls"]) == 1 and s3["colls"][0]["sel"] == inc[1:], s3["colls"])
            ed.js("csDelete(CS.active); 1"); time.sleep(0.6); boot(ed); s4 = st(ed); log["empty"] = s4
            ok("once: removing the last collection leaves none, and a reload adds nothing back", s4["colls"] == [] and s4["active"] is None and (s4["starter"] or {}).get("state") == "done", s4)
            ok("empty: the Studio offers Add Game Files, Restore a Backup and New Collection", all(t in s4["panel"] for t in ("Add Game Files", "Restore a Backup", "New Collection")), s4["panel"])
            ok("empty: no catalog is shown in its place", s4["mode"] == "collection" and s4["rows"] == 0, (s4["mode"], s4["rows"]))
            shot(ed, "empty-1440.png")
            ed.js("tvBtn.click(); 1"); time.sleep(0.8); dlg = ed.js("$('collm').classList.contains('open')?$('collBody').innerText:''") or ""
            ok("empty: Open HOME explains there is no collection open (HOME does not open)", "collection" in dlg and ed.js("TV.open") is False, dlg)
            ed.js("closeColl(); 1")
        # ---- concurrent first opens ----
        prof = tempfile.mkdtemp(prefix="ccs-model-pair-")
        with EdgeSession(profile=prof, width=1900, height=700) as ed:
            ed.navigate(base + "/__pair.html")
            ed.wait_for("(()=>{ try{ return ['a','b'].every(i=>{ const w=document.getElementById(i).contentWindow; return w.document.documentElement.dataset.ready==='1'&&w.eval(\"typeof CS!=='undefined'&&!!CS&&!!CS.starter&&(CS.starter.state==='done'||CS.starter.state==='failed')\"); }); }catch(e){ return false; } })()", timeout=120)
            time.sleep(2.0)
            pair = json.loads(ed.js("JSON.stringify(['a','b'].map(i=>{ const w=document.getElementById(i).contentWindow; return w.eval('({names:csList().map(c=>c.name), active:CS.active})'); }).concat([JSON.parse(localStorage.getItem('studio-collections-v2'))]))"))
            stored = pair[2]; names = sorted(r["name"] for r in stored["items"].values())
            ok("concurrent: two pages starting together make exactly one Included Games collection", names == ["Included Games"], names)
            ok("concurrent: both pages show that same collection", pair[0]["active"] == pair[1]["active"] == stored["active"], pair[:2])
        # ---- unreadable ----
        prof = tempfile.mkdtemp(prefix="ccs-model-bad-")
        with EdgeSession(profile=prof, width=1440, height=900) as ed:
            junk = '{"schema":"ccs-collections-store/1","items":{"x":'   # cut short: what a damaged write leaves
            seed(ed, "localStorage.setItem(%s, %s); localStorage.setItem('nes-classic-selection-v1', '[\"p_0123456789abcdef\"'); 1" % (json.dumps(CSKEY), json.dumps(junk)))
            boot(ed, settle=False); time.sleep(2.5); s = st(ed); log["unreadable"] = s
            ok("unreadable: the Studio says its collections could not be read", s["state"] == "unreadable" and "could not be read" in s["panel"], (s["state"], s["panel"]))
            ok("unreadable: nothing is set up as new (no Included Games)", s["colls"] == [] and not s["starter"], s)
            ed.js("try{ persist(); csWrite(); sel.add('p_1111222233334444'); persist(); }catch(e){} 1"); time.sleep(0.5)
            raw = ed.js("JSON.stringify([localStorage.getItem(%s), localStorage.getItem('nes-classic-selection-v1')])" % json.dumps(CSKEY))
            ok("unreadable: the stored values stay byte for byte (no write over them)", json.loads(raw) == [junk, '["p_0123456789abcdef"'], raw)
            ed.js("tvBtn.click(); 1"); time.sleep(0.6)
            ok("unreadable: HOME refuses with the storage explanation", ed.js("$('collm').classList.contains('open')&&/could not read your collections/i.test($('collBody').innerText)") is True and ed.js("TV.open") is False)
            shot(ed, "unreadable-1440.png"); ed.js("closeColl(); 1")
        # ---- migrate (a v1p2 store with lineups) ----
        prof = tempfile.mkdtemp(prefix="ccs-model-migrate-")
        with EdgeSession(profile=prof, width=1440, height=900) as ed:
            g = [x["game_id"] for x in json.load(open(os.path.join(a.site, "catalog.json"), encoding="utf-8"))["games"][:6]]
            now = "2026-11-12T10:00:00.000Z"
            homeA = {"v": 1, "theme": "arcade", "folders": [], "favorites": [], "history": [], "settings": {}}
            homeB = {"v": 1, "theme": "boxart", "folders": [], "favorites": [g[1]], "history": [], "settings": {}}
            homeP = {"v": 1, "theme": "minimal", "folders": [], "favorites": [], "history": [], "settings": {}}
            store = {"schema": "ccs-collections-store/1", "active": "cP", "items": {
                "cP": {"id": "cP", "name": "Main", "created": now, "modified": now, "sel": g[:5], "decisions": {}, "coll": {"pref": {}, "multi": {}, "avail": [], "placeholders": [], "missing": {}}, "home": homeP, "shares": [], "openLoadout": "lB",
                       "loadouts": [{"id": "lA", "target": "nes-classic", "name": "Party", "created": now, "modified": now, "sel": g[:2], "decisions": {}, "home": homeA},
                                    {"id": "lB", "target": "nes-classic", "name": "Kids", "created": now, "modified": now, "sel": g[1:4], "decisions": {}, "home": homeB},
                                    {"id": "lC", "target": "nes-classic", "name": "Same", "created": now, "modified": now, "sel": g[:5], "decisions": {}, "home": homeP}]},
                "cT": {"id": "cT", "name": "Main: Party", "created": now, "modified": now, "sel": [g[5]], "decisions": {}, "coll": {}, "home": None, "shares": [], "loadouts": []}}}
            seed(ed, "localStorage.clear(); localStorage.setItem(%s, %s); localStorage.setItem('nes-classic-selection-v1', %s); 1" % (json.dumps(CSKEY), json.dumps(json.dumps(store)), json.dumps(json.dumps(g[1:4]))))
            boot(ed, settle=False); time.sleep(2.0); s = st(ed); log["migrate"] = s
            by = {c["name"]: c for c in s["colls"]}
            ok("migrate: each distinct lineup is a collection; an equal one adds nothing", sorted(by) == ["Main", "Main: Kids", "Main: Party", "Main: Party (Copy 1)"], sorted(by))
            ok("migrate: the lineups' games kept", by.get("Main: Party (Copy 1)", {}).get("sel") == sorted(g[:2]) and by.get("Main: Kids", {}).get("sel") == sorted(g[1:4]), by)
            ok("migrate: each keeps its own HOME settings (theme, favorites)", (by.get("Main: Party (Copy 1)", {}).get("home") or {}).get("theme") == "arcade" and (by.get("Main: Kids", {}).get("home") or {}).get("favorites") == [g[1]], by)
            ok("migrate: the parent keeps its games and HOME settings, without lineups", by.get("Main", {}).get("sel") == sorted(g[:5]) and (by.get("Main", {}).get("home") or {}).get("theme") == "minimal" and by.get("Main", {}).get("loadouts") is None, by.get("Main"))
            ok("migrate: the person's own same-named collection is untouched", by.get("Main: Party", {}).get("sel") == [g[5]] and not by.get("Main: Party", {}).get("from"), by.get("Main: Party"))
            ok("migrate: the lineup that was open is the open collection", s["active"] == by.get("Main: Kids", {}).get("id"), (s["active"], by.get("Main: Kids", {}).get("id")))
            before = json.loads(ed.js("localStorage.getItem('studio-collections-v2.before-collections-only')") or "null")
            ok("migrate: the store before is kept, unchanged", bool(before) and before.get("store") == store, str(before)[:200])
            ok("migrate: no game file is copied (the browser's game store stays empty)", ed.js("CCSROMS.keys(true).then(k=>k.size)") == 0)
            boot(ed, settle=False); time.sleep(1.5); s2 = st(ed)
            ok("migrate: a reload adds nothing twice", sorted(c["name"] for c in s2["colls"]) == sorted(by) and s2["active"] == s["active"], [c["name"] for c in s2["colls"]])
            # half done: a lineup collection already made, its parent still carrying both lineups
            half = json.loads(json.dumps(store)); half["items"]["cK"] = {"id": "cK", "name": "Main: Kids", "created": now, "modified": now, "sel": g[1:4], "decisions": {}, "coll": {}, "home": homeB, "shares": [], "fromLineup": {"collection": "cP", "lineup": "lB"}}
            seed(ed, "localStorage.clear(); localStorage.setItem(%s, %s); 1" % (json.dumps(CSKEY), json.dumps(json.dumps(half))))
            ed.navigate(base + "/__pair.html")
            ed.wait_for("(()=>{ try{ return ['a','b'].every(i=>{ const w=document.getElementById(i).contentWindow; return w.document.documentElement.dataset.ready==='1'&&w.eval(\"typeof CS!=='undefined'&&!!CS\"); }); }catch(e){ return false; } })()", timeout=120); time.sleep(2.0)
            fin = json.loads(ed.js("localStorage.getItem('studio-collections-v2')")); names = sorted(r["name"] for r in fin["items"].values())
            ok("migrate: a half-done earlier migration, finished by two pages at once, makes each lineup once", names == ["Main", "Main: Kids", "Main: Party", "Main: Party (Copy 1)"] and not any(r.get("loadouts") for r in fin["items"].values()), names)
            ok("migrate: the open lineup's existing collection is the open one", fin["active"] == "cK", fin["active"])
        # ---- restore an older JSON backup with lineups, twice ----
        prof = tempfile.mkdtemp(prefix="ccs-model-restore-")
        with EdgeSession(profile=prof, width=1440, height=900) as ed:
            boot(ed); g = json.loads(ed.js("JSON.stringify(DATA.games.slice(0,4).map(x=>x.game_id))"))
            bk = {"schema": "ccs-studio-backup/2", "exported": "2026-11-12T09:00:00Z", "app": "Classic Cart Studio", "active": "cR", "favorites": [], "view": "grid", "extended": "", "welcomed": True, "snapshots": [],
                  "collections": [{"schema": "ccs-collection/1", "id": "cR", "name": "Road Trip", "created": "2026-11-01T00:00:00Z", "modified": "2026-11-02T00:00:00Z", "sel": g, "decisions": {}, "coll": {}, "home": None,
                                   "loadouts": [{"id": "l1", "target": "nes-classic", "name": "Short", "sel": g[:2], "decisions": {}, "home": {"v": 1, "theme": "arcade"}}]}]}
            ed.js("window.__bk=%s; window.__r1=csMergeStudioData(__bk, __bk.collections, null); 1" % json.dumps(bk)); time.sleep(0.5)
            r1 = json.loads(ed.js("JSON.stringify({ok:__r1.ok, added:__r1.added, names:csList().map(c=>c.name).sort()})"))
            ok("restore: an older backup's collection and its lineup arrive as collections", r1["ok"] and r1["added"] == 2 and "Road Trip" in r1["names"] and "Road Trip: Short" in r1["names"], r1)
            ed.js("window.__r2=csMergeStudioData(__bk, __bk.collections, null); 1"); time.sleep(0.4)
            r2 = json.loads(ed.js("JSON.stringify({ok:__r2.ok, added:__r2.added, skipped:__r2.skipped, n:csList().length})"))
            ok("restore: restoring it again adds nothing", r2["ok"] and r2["added"] == 0 and r2["skipped"] == 2 and r2["n"] == r1["added"] + 1, r2)
        # ---- the Windows app's durable copy at start: the newer collections store wins (its own saved time), whichever collection is open ----
        # (build4's Sandbox run: a collection opened just before the app closed was lost, because only the open collections' modified times were compared)
        prof = tempfile.mkdtemp(prefix="ccs-model-durable-")
        with EdgeSession(profile=prof, width=1440, height=900) as ed:
            boot(ed)
            RUN = r"""(async()=>{ const inc=CS.active; const lr=csCreate('Living Room'); csSwitch(lr.id, true);
              const s1=JSON.parse(localStorage.getItem('studio-collections-v2')).saved; csWrite(); const s1b=JSON.parse(localStorage.getItem('studio-collections-v2')).saved;
              const older=JSON.parse(JSON.stringify(desktopStateSnapshot()));   // the durable copy as the app last received it (Living Room open)
              await new Promise(r=>setTimeout(r,30)); csSwitch(inc, true);         // then Included Games was opened, and the app closed before its copy followed
              const local=localStorage.getItem('studio-collections-v2');
              const stub=st=>{ window.capi=async(u,o)=>(u==='/api/desktop/state'&&!(o&&o.method==='POST'))?{ok:true, state:st}:{ok:true}; };
              const was={d:CO.desktop, ok:CO.ok, capi:window.capi}; CO.desktop=true; CO.ok=true;
              stub(older); await desktopStateRestore(); const keptLocal=localStorage.getItem('studio-collections-v2')===local&&csActive().id===inc;
              const later=JSON.parse(JSON.stringify(older)); const s=JSON.parse(later.ls['studio-collections-v2']); s.saved=new Date(Date.now()+3600e3).toISOString(); later.ls['studio-collections-v2']=JSON.stringify(s);
              stub(later); await desktopStateRestore(); await new Promise(r=>setTimeout(r,400)); const tookRemote=JSON.parse(localStorage.getItem('studio-collections-v2')).active===lr.id;
              CO.desktop=was.d; CO.ok=was.ok; window.capi=was.capi;
              return JSON.stringify({keptLocal, tookRemote, identicalKeeps:!!s1&&s1===s1b, savedLocal:JSON.parse(local).saved, savedOlder:JSON.parse(older.ls['studio-collections-v2']).saved}); })()"""
            r = json.loads(ed.js(RUN))
            ok("app start: rewriting an unchanged store keeps its saved time", r["identicalKeeps"], r)
            ok("app start: a collection opened just before closing stays open (the page's newer store beats an older durable copy)", r["keptLocal"], r)
            ok("app start: a durable copy written later than the page's store is taken", r["tookRemote"], r)
            # (the Sandbox update run: starting the checked installer reads it again and outlasted the ordinary 12 s request limit)
            ok("update: starting the checked installer is a long request the page waits for", ed.js("HK_WAITS.test('/api/update/install')&&!HK_WAITS.test('/api/update/status')") is True)
    finally:
        srv.shutdown()
    fails = [n for n, c in res if not c]
    print(f"\n{len(res) - len(fails)}/{len(res)} PASS" + (f"; FAIL: {', '.join(fails)}" if fails else ""))
    if a.out: json.dump({"results": [{"name": n, "pass": c} for n, c in res], "log": log}, open(a.out, "w", encoding="utf-8"), indent=1)
    return 0 if not fails else 1

if __name__ == "__main__": sys.exit(main())
