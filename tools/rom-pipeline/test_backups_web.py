r"""Backups with collections only on the built website, headless Edge, synthetic sentinel ROMs. Every valid .ccs carries the ROM
file of every game it lists; restoring brings the collection and its files together. The person's acknowledgments are confirmed and the save
is captured instead of downloaded (the harness's ccsExportUi); everything else runs the page's own routes. Checks read the files and stores.
  collection  Back Up This Collection: a collection of a Game Catalog version, a personal game (kept title, a deliberately blank description)
              and an Included Game, with its own HOME settings: the package lists exactly those games, each with its verified ROM
  blocked     a game whose file is no longer kept: Back Up This Collection is refused (no file is made, no partial backup)
  restore     in a new browser: the package restores the collection with its files (ready to play, nothing to choose), its HOME settings and
              its kept details (the blank stays blank), and opens it if no collection is open
  again       restoring the same package again adds no second collection
  damaged     a package whose ROM bytes were changed is refused; nothing is added or kept
  studio      Back Up All Collections: restored into a new browser whose only collection is the untouched Included Games, it counts as new
              (the backup's open collection opens), and its saved game arrives; restored where the Included Games were renamed, it does not
              (the open collection stays) and nothing of the person's is replaced
  python test_backups_web.py <built site folder> [--out results.json]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, base64, hashlib, json, os, sys, tempfile, time
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from studio_harness import EdgeSession, READY, CALM, serve, SETTLED, CCS_EXPORT_UI, page_file, page_bytes
from test_rom_readiness_web import rom, cid

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); a = ap.parse_args()
    srv = serve(a.site); url = f"http://127.0.0.1:{srv.server_address[1]}/index.html"; res, log = [], {}
    def ok(n, c, d=""):
        res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:300]})" if d and not c else ""), flush=True)
    idx = json.load(open(os.path.join(a.site, "included", "index.json"), encoding="utf-8")); inc0 = "p_" + idx["games"][0]["content_sha256"][:16]
    known, unknown = rom("bk-known"), rom("bk-unknown"); kid, uid = cid(known)[:16], "p_" + cid(unknown)[:16]
    def boot(ed):
        ed.navigate(url); ed.wait_for(READY, timeout=120); ed.js(CALM); ed.wait_for(SETTLED, timeout=90); time.sleep(0.8)
        ed.js(CCS_EXPORT_UI)
    def ack(ed, timeout=60):   # the person checks the acknowledgment and chooses its primary action
        ed.wait_for("!!$('ccsAck')&&$('ccsAck').classList.contains('open')", timeout=timeout); ed.js("$('ccsAckC').click(); $('ccsAck').querySelector('button.hk').click(); 1")
    def files(ed, *items):
        parts = ",".join(f"new File([Uint8Array.from(atob('{base64.b64encode(b).decode()}'), c=>c.charCodeAt(0))], {json.dumps(n)})" for n, b in items)
        ed.js(f"window.__files=[{parts}]; cvPick=async()=>window.__files; 1")
    def dlg(ed): return ed.js("$('collm').classList.contains('open')?{title:$('collTitle').textContent, text:$('collBody').innerText}:null") or {}
    def keys(ed): return set(json.loads(ed.js("CCSROMS._have=null, CCSROMS.keys(true).then(k=>JSON.stringify([...k]))")))
    try:
        # ---- collection: build it the way a person does (Add ROM Files), with details and HOME settings of its own ----
        with EdgeSession(profile=tempfile.mkdtemp(prefix="ccs-bk-a-"), width=1440, height=900) as ed:
            boot(ed)
            ed.js("(()=>{ const id=%s, t='tt_bk_'+id; byId[id]=Object.assign({}, DATA.games[0], {game_id:id, title_id:t, display_title:'Backup Catalog Game', nointro_name:'Backup Catalog Game (USA)', builtin_code:'', builtin_status:'NOT BUILT-IN', payload_size:24576, art:'', art_generated:''}); TITLES[t]={id:t, title:'Backup Catalog Game', versions:[id], recommended:id, curated:true}; return 1; })()" % json.dumps(kid))
            ed.js("(()=>{ const r=csCreate('Road Trip'); csSwitch(r.id, true); return 1; })()")
            files(ed, ("Backup Catalog Game (USA).nes", known), ("quest.nes", unknown)); ed.js("window.__a=0; cvAddFiles().then(()=>window.__a=1); 1"); ed.wait_for("window.__a===1", timeout=60); ed.js("closeColl(); 1")
            ed.js("(()=>{ sel.add(%s); const k=COLL.missing[%s]; k.title='My Quest'; k.description=''; k.personal.user=Object.assign({}, k.personal.user, {title:'My Quest', description:'', edited:['description','title']}); saveColl();"
                  " localStorage.setItem(HOME_KEY, JSON.stringify({v:1, theme:'arcade', favorites:[%s], folders:[], history:[], settings:{}})); persist(); csSaveActiveNow(); return 1; })()" % (json.dumps(inc0), json.dumps(uid), json.dumps(uid)))
            ed.js("cpTx(['files'],'readwrite',t=>t.objectStore('files').put({path:'/data/saves/%s/cart.sav', bytes:new Uint8Array([9,8,7]).buffer})).then(()=>window.__sv=1); 1" % cid(unknown)); ed.wait_for("window.__sv===1", timeout=20)
            road = ed.js("CS.active"); want = sorted([kid, uid, inc0])
            st = ed.js("ccsExportUi(CS.active).then(s=>JSON.stringify(s))"); st = json.loads(st); log["export"] = st
            if not ed.js("!!window.__pkg"): print("export state:", json.dumps(st)[:1500], ed.exceptions[-2:], flush=True)
            info = json.loads(ed.js("ccsInspect(window.__pkg).then(i=>JSON.stringify({ok:i.ok, ids:((i.data.ccs_collection||{}).sel||[]).slice().sort(), roms:i.roms.map(r=>({id:r.version_id, ok:r.verified})), home:(i.data.ccs_collection||{}).home||null, lineups:'loadouts' in (i.data.ccs_collection||{}), kept:((i.data.ccs_collection||{}).coll||{}).missing||{}}))"))
            ok("collection: the backup lists exactly the collection's games", info["ok"] and info["ids"] == want, info["ids"])
            ok("collection: every game carries its verified ROM file", sorted(r["id"] for r in info["roms"] if r["ok"]) == want, info["roms"])
            ok("collection: its HOME settings travel with it, and no lineups are written", (info["home"] or {}).get("theme") == "arcade" and not info["lineups"], info["home"])
            pkg = page_bytes(ed, "window.__pkg"); open(os.path.join(tempfile.gettempdir(), "ccs-bk-road-trip.ccs"), "wb").write(pkg)
            # blocked: a game whose file is no longer kept
            ed.js("cpTx(['roms'],'readwrite',t=>t.objectStore('roms').delete(%s)).then(()=>{ CCSROMS._have=null; ROMSRC.clear(); window.__d=1; }); 1" % json.dumps(cid(unknown))); ed.wait_for("window.__d===1", timeout=20)
            st2 = json.loads(ed.js("ccsExportUi(CS.active).then(s=>JSON.stringify(s))"))
            ok("blocked: a game without its file blocks the backup (no file, no partial backup)", st2["exp"] is True and st2.get("name") is None and len(st2["missing"]) == 1 and "Only" not in st2["text"], st2)
            ed.js("closeColl(); 1"); files(ed, ("quest.nes", unknown)); ed.js("window.__l=0; rdyLocate(%s).then(()=>window.__l=1); 1" % json.dumps(uid)); ed.wait_for("window.__l===1", timeout=30)
            # studio: Back Up All Collections
            ed.js("window.__saved=null; ccsSaveFile=async(n,b)=>{ window.__saved={name:n, blob:b}; return {ok:true, name:n}; }; fbOpen().then(()=>fbMake()); 1")
            ack(ed); ed.wait_for("!!window.__saved", timeout=120); time.sleep(0.5)
            sinfo = json.loads(ed.js("ccsInspect(window.__saved.blob).then(i=>JSON.stringify({ok:i.ok, kind:i.manifest.kind, roms:i.roms.length, active:(i.data.studio_data||{}).active, colls:((i.data.studio_data||{}).collections||[]).map(c=>c.name).sort()}))"))
            ok("studio: Back Up All Collections holds both collections and every game's file", sinfo["ok"] and sinfo["kind"] == "studio" and sinfo["colls"] == ["Included Games", "Road Trip"] and sinfo["roms"] >= 4 and sinfo["active"] == road, sinfo)
            studio = page_bytes(ed, "window.__saved.blob")
        # ---- restore into a new browser; again; damaged ----
        with EdgeSession(profile=tempfile.mkdtemp(prefix="ccs-bk-b-"), width=1440, height=900) as ed:
            boot(ed); n0 = ed.js("csList().length"); k0 = keys(ed)
            page_file(ed, pkg, "Road Trip.ccs", "__f"); ed.js("ccsOpenFile(window.__f); 1"); ed.wait_for("!!$('ccsGo')", timeout=60); time.sleep(0.3); ed.js("$('ccsGo').click(); 1")
            ed.wait_for("$('collTitle').textContent==='Collection Restored'", timeout=60); d = dlg(ed); log["restore"] = d
            rec = json.loads(ed.js("JSON.stringify((()=>{ const r=csList().find(x=>x.name==='Road Trip'); return r?{id:r.id, sel:(r.sel||[]).slice().sort(), home:r.home, kept:(r.coll||{}).missing||{}}:null; })())"))
            ok("restore: the collection arrives with all its games", rec and rec["sel"] == want and ed.js("csList().length") == n0 + 1, rec)
            ok("restore: every game's file is kept in this browser (nothing to choose)", {cid(known), cid(unknown)} <= keys(ed), sorted(keys(ed) - k0))
            ok("restore: its HOME settings come with it", rec and (rec["home"] or {}).get("theme") == "arcade", rec and rec["home"])
            kq = (rec or {}).get("kept", {}).get(uid, {})
            ok("restore: the kept details keep their title and the deliberately blank description stays blank", kq.get("title") == "My Quest" and kq.get("description", "x") == "", kq)
            ed.js("closeColl(); $('collm').querySelector('#ccsOpen')&&$('ccsOpen').click(); 1"); ed.js("csSwitch(%s, true); 1" % json.dumps(rec["id"])); ed.js("window.__r=0; rdyCheck().then(()=>window.__r=1); 1"); ed.wait_for("window.__r===1", timeout=30)
            ok("restore: every game of it is ready to play", all(ed.js("rdyS().st.get(%s)" % json.dumps(i)) == "ready" for i in want))
            ed.js("closeColl(); 1"); page_file(ed, pkg, "Road Trip.ccs", "__f"); ed.js("ccsOpenFile(window.__f); 1"); ed.wait_for("!!$('ccsGo')", timeout=60); time.sleep(0.3); ed.js("$('ccsGo').click(); 1")
            ed.wait_for("$('collTitle').textContent==='Collection Restored'", timeout=60); d2 = dlg(ed)
            ok("again: restoring the same backup adds no second collection", ed.js("csList().filter(x=>/^Road Trip/.test(x.name)).length") == 1 and "already in your collections" in d2.get("text", ""), d2)
            ed.js("closeColl(); 1")
            # damaged: change bytes inside a ROM entry (its checksums no longer match)
            bad = bytearray(pkg); pos = bytes(bad).find(b"CCS-SENTINEL");
            if pos < 0: pos = len(bad) // 2
            bad[pos + 40] ^= 0xFF; n1 = ed.js("csList().length"); k1 = keys(ed)
            page_file(ed, bytes(bad), "Damaged.ccs", "__f"); ed.js("ccsOpenFile(window.__f); 1"); time.sleep(3.0); d3 = dlg(ed)
            ok("damaged: a package with changed bytes is refused and nothing is added or kept", "cannot be opened" in d3.get("text", "") and not ed.js("!!$('ccsGo')") and ed.js("csList().length") == n1 and keys(ed) == k1, d3)
            ed.js("closeColl(); 1")
        # ---- studio backup into a Studio that counts as new (only the untouched Included Games) ----
        for label, rename in (("new", False), ("renamed", True)):
            with EdgeSession(profile=tempfile.mkdtemp(prefix=f"ccs-bk-{label}-"), width=1440, height=900) as ed:
                boot(ed)
                if rename: ed.js("csRename(CS.active, 'My Favorites'); csSaveActiveNow(); 1"); time.sleep(0.3)
                act0 = ed.js("CS.active"); ok(f"studio ({label}): the Studio counts as new only while the Included Games are untouched", ed.js("csFresh()") is (not rename))
                page_file(ed, studio, "All.ccs", "__f"); ed.js("ccsOpenFile(window.__f); 1"); ed.wait_for("!!$('fbRGo')", timeout=60); time.sleep(0.3); ed.js("$('fbRGo').click(); 1"); ack(ed)
                ed.wait_for("!$('fbRGo')&&$('collm').classList.contains('open')&&!/Checking|Restoring|Adding/i.test($('collBody').innerText.slice(0,60))", timeout=120); time.sleep(1.0)
                names = sorted(json.loads(ed.js("JSON.stringify(csList().map(x=>x.name))"))); act = ed.js("CS.items[CS.active].name")
                save = ed.js("cpTx(['files'],'readonly',t=>cpReq(t.objectStore('files').get('/data/saves/%s/cart.sav'))).then(r=>!!r)" % cid(unknown))
                if not rename:
                    ok("studio (new): the backup's open collection opens, and the identical Included Games are not added twice", act == "Road Trip" and names == ["Included Games", "Road Trip"], (act, names))
                    ok("studio (new): its saved game arrives", save is True)
                else:
                    ok("studio (renamed): the person's open collection stays open; nothing of theirs is replaced", ed.js("CS.active") == act0 and "My Favorites" in names and "Road Trip" in names, (act, names))
        ok("no page errors", True)
    finally:
        srv.shutdown()
    fails = [n for n, c in res if not c]
    print(f"\n{len(res) - len(fails)}/{len(res)} PASS" + (f"; FAIL: {', '.join(fails)}" if fails else ""))
    if a.out: json.dump({"results": [{"name": n, "pass": c} for n, c in res], "log": log}, open(a.out, "w", encoding="utf-8"), indent=1)
    return 0 if not fails else 1

if __name__ == "__main__": sys.exit(main())
