r"""A collection of N games stays exactly N games through export, import, transfer, publication, retrieval and saving a shared copy,
whatever the games' origin and whether the receiving side has their files (headless Edge, website mode, local share Worker).

Mixed collection: factory games, curated catalog games, Extended Library versions, two versions of one title, personal games (one with
the user's own artwork), an entry whose identity is not in the Game Library. Checks: file export/import in a clean browser; the
transfer bundle (website <-> app format); restart; publish; the public JSON, page and thumbnails; a recipient in a clean browser saves the
copy (restart, export again); unpublish; nothing published contains game data, file names or local paths.

  python test_collection_integrity.py <built site folder>      Exit 0 = PASS."""
from __future__ import annotations
import base64, json, os, re, shutil, subprocess, sys, tempfile, time, urllib.request, urllib.error
HERE = os.path.dirname(os.path.abspath(__file__)); TOOLS = os.path.dirname(HERE); sys.path.insert(0, HERE)
from test_collections_web import EdgeSession, serve, free_port

READY = "typeof CS!=='undefined'&&!!CS&&typeof byId!=='undefined'&&typeof csEntries==='function'&&document.readyState==='complete'"
# the sender's collection, built in the page: personal rows are injected exactly as the app would hold them (no backend on the website)
BUILD = r"""(async()=>{ try{ doneWelcome(); }catch(e){}
 const c=document.createElement('canvas'); c.width=60; c.height=84; const x=c.getContext('2d'); x.fillStyle='#3a6'; x.fillRect(0,0,60,84); x.fillStyle='#fff'; x.fillRect(10,10,40,20); const own=c.toDataURL('image/png');
 const P=(id,title,art)=>{ const e={id, content_sha256:id.slice(2).padEnd(64,'a'), file_sha256:id.slice(2).padEnd(64,'b'), content_size:40976, file_size:41000, format:'nes', file_name:'C:\\\\Users\\\\someone\\\\ROMs\\\\'+title+'.nes', origin:'file', user:{title, year:'2024', publisher:'Homebrew Team'}, detected:{title}, file_present:true}; PERSONAL[id]=e; const r=perRow(e); if(art) r.art=art; byId[id]=r; return id; };
 csNew('Mixed Integrity'); const ids=[];
 ['CLV-P-NAAHE','CLV-P-NAAAE','CLV-P-NACDE'].forEach(cd=>ids.push(factoryRowFor(cd).game_id));
 DATA.games.filter(g=>!g.builtin_code&&g.art).slice(0,3).forEach(g=>ids.push(g.game_id));
 EXT_ROWS.filter(g=>g.art).slice(0,2).forEach(g=>ids.push(g.game_id));
 const t=Object.values(TITLES).find(t=>(t.versions||[]).filter(v=>byId[v]).length>=2&&!(t.versions||[]).some(v=>ids.includes(v))); const two=t.versions.filter(v=>byId[v]).slice(0,2); two.forEach(v=>ids.push(v)); COLL.multi[t.id]=true;
 ids.push(P('p_1111222233334444','My Homebrew Quest', own)); ids.push(P('p_5555666677778888','Translated Hack (T-En)', null));
 const unk='x_unknownproto001'; COLL.missing=Object.assign(COLL.missing||{}, {[unk]:{title:'Unreleased Prototype (Unknown)', version:'Proto 2', year:'1990', publisher:'', source:'unknown', art:'', content_sha256:'c'.repeat(64)}}); ids.push(unk);
 ids.forEach(i=>sel.add(i)); persist(); render(); csSaveActiveNow();
 return JSON.stringify({ids, two, n:sel.size}); })()"""

def main():
    site = sys.argv[1]; checks = []
    def ok(k, v, n=""): checks.append((k, bool(v), n)); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:300]})" if n and not v else ""), flush=True)
    web = serve(site); WEB = f"http://127.0.0.1:{web.server_address[1]}"; sport = free_port(); SHARE = f"http://127.0.0.1:{sport}"
    dev = subprocess.Popen([shutil.which("node"), os.path.join(TOOLS, "share-worker", "test", "dev-server.mjs"), str(sport), WEB], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    profs = [tempfile.mkdtemp(prefix=f"ccs-int-{k}-") for k in ("a", "b", "c")]; URL = f"{WEB}/index.html?shareapi={SHARE}"
    ids_of = "JSON.stringify([...sel])"
    try:
        for _ in range(40):
            try: urllib.request.urlopen(SHARE + "/c/none", timeout=1)
            except urllib.error.HTTPError: break
            except Exception: time.sleep(0.25)
        # ---- sender ----
        with EdgeSession(width=1400, height=900, profile=profs[0]) as ed:
            ed.navigate(URL); ed.wait_for(READY, timeout=120)
            b = json.loads(ed.js(BUILD)); ids = b["ids"]; N = len(ids)
            ok("mixed_collection_built", N == 13 and b["n"] == 13, b)
            m = json.loads(ed.js("JSON.stringify(csFileFor(csActive()))"))
            ok("export_lists_every_game", m["count"] == N and [s["version_id"] for s in m["selections"]].count("x_unknownproto001") == 1 and sorted(s["version_id"] for s in m["selections"]) == sorted(ids), m["count"])
            ok("export_keeps_both_versions", all(v in [s["version_id"] for s in m["selections"]] for v in b["two"]))
            ok("export_personal_records_without_paths", len(m["personal_library"]) == 2 and all(":\\" not in json.dumps(p) and "/Users" not in json.dumps(p) for p in m["personal_library"]), m["personal_library"][:1])
            bundle = json.loads(ed.js("JSON.stringify(csBundle([CS.active]))"))
            ok("transfer_bundle_every_game", len(bundle["collections"][0]["sel"]) == N)
            # publish
            ed.js("openShare()"); ed.wait_for("!!$('shGo')&&!!SH.img", timeout=30)
            ed.js("$('shGo').click()"); ed.wait_for("!!(SH.pub&&SH.pub.url)||/Not published/.test(($('shOut')||{}).textContent||'')", timeout=60)
            pub = json.loads(ed.js("JSON.stringify(SH.pub||{err:$('shOut').textContent})")); ok("published", "id" in pub, pub)
            sid = pub.get("id")
        # ---- what the world can see ----
        pj = json.loads(urllib.request.urlopen(f"{SHARE}/api/c/{sid}").read())
        ok("public_json_every_game_in_order", [g["id"] for g in pj["games"]] == ids, [g["id"] for g in pj["games"]][:4])
        ok("public_json_personal_and_unknown_present", sum(g.get("source") == "personal" for g in pj["games"]) == 2 and any(g["id"] == "x_unknownproto001" and g.get("source") == "unknown" for g in pj["games"]))
        own = next(g for g in pj["games"] if g["id"] == "p_1111222233334444")
        th = urllib.request.urlopen(own["thumb_url"]).read() if own.get("thumb_url") else b""
        ok("custom_artwork_published_as_thumbnail", th[:3] == b"\xff\xd8\xff", own.get("thumb_url"))
        page = urllib.request.urlopen(f"{SHARE}/c/{sid}").read().decode()
        ok("share_page_lists_every_game", all(re.search(re.escape(g["title"].replace("&", "&amp;").replace("'", "&#39;")), page) or g["title"] in page for g in pj["games"]), [g["title"] for g in pj["games"] if g["title"] not in page][:3])
        blob = json.dumps(pj)
        ok("published_data_has_no_files_or_paths", not re.search(r"[A-Za-z]:\\\\|/Users/|\\\\Users\\\\|file_name|\.nes\b|personal/art|data:", blob) and len(blob) < 200000, re.findall(r"[A-Za-z]:\\\\[^\"]{0,30}|file_name|personal/art", blob)[:3])
        # ---- a recipient in a clean browser, with none of the personal files ----
        with EdgeSession(width=1400, height=900, profile=profs[1]) as ed:
            ed.navigate(f"{WEB}/index.html?shareapi={SHARE}&share={sid}"); ed.wait_for(READY, timeout=120); ed.wait_for("!!$('srSave')", timeout=30)
            ok("recipient_sees_every_game", ed.js("document.querySelectorAll('.shg').length") == N and ed.js("document.querySelectorAll('.shg.away').length") >= 3)
            ed.js("$('srSave').click()"); ed.wait_for("!!$('srOpen')", timeout=10); ed.js("$('srOpen').click()")
            got = json.loads(ed.js(ids_of)); ok("recipient_copy_every_game", sorted(got) == sorted(ids), len(got))
            ok("recipient_tray_lists_unavailable", ed.js("render(); document.querySelectorAll('#trayList li.trmiss').length") >= 3)
        with EdgeSession(width=1400, height=900, profile=profs[1]) as ed:
            ed.navigate(URL); ed.wait_for(READY, timeout=120)
            ok("recipient_copy_after_restart", sorted(json.loads(ed.js(ids_of))) == sorted(ids) and ed.js("COLL.name") == "Mixed Integrity")
            m2 = json.loads(ed.js("JSON.stringify(csFileFor(csActive()))"))
            ok("recipient_export_every_game", m2["count"] == N and sorted(s["version_id"] for s in m2["selections"]) == sorted(ids))
        # ---- the original file and the transfer bundle in another clean browser ----
        with EdgeSession(width=1400, height=900, profile=profs[2]) as ed:
            ed.navigate(URL); ed.wait_for(READY, timeout=120)
            ed.js(f"csImportObject({json.dumps(m)}, 'mixed.json')"); time.sleep(1.5); ed.js("try{ closeColl(); }catch(e){}")
            ok("file_import_every_game", sorted(json.loads(ed.js(ids_of))) == sorted(ids) and ed.js("Object.keys(COLL.missing||{}).length") >= 3, len(json.loads(ed.js(ids_of))))
            ed.js(f"csImportObject({json.dumps(bundle)}, 'bundle.json')"); time.sleep(1)
            ok("bundle_import_every_game", sorted(json.loads(ed.js(ids_of))) == sorted(ids))
        with EdgeSession(width=1400, height=900, profile=profs[2]) as ed:
            ed.navigate(URL); ed.wait_for(READY, timeout=120)
            ok("imports_after_restart", all(len(r) == N for r in json.loads(ed.js("JSON.stringify(csList().filter(r=>r.name.startsWith('Mixed')).map(r=>r.sel))"))) and ed.js("csList().filter(r=>r.name.startsWith('Mixed')).length") == 2)
        # ---- unpublish ----
        with EdgeSession(width=1400, height=900, profile=profs[0]) as ed:
            ed.navigate(URL); ed.wait_for(READY, timeout=120); ed.js("window.confirm=()=>true"); ed.js(f"shUnpublish('{sid}')"); time.sleep(1.5)
        try: urllib.request.urlopen(f"{SHARE}/api/c/{sid}"); gone = False
        except urllib.error.HTTPError as e: gone = e.code == 410
        ok("unpublished", gone)
    finally:
        dev.kill(); web.shutdown()
        for p in profs: shutil.rmtree(p, ignore_errors=True)
    bad = [k for k, v, _ in checks if not v]; print("RESULT:", "PASS" if not bad else "FAIL " + json.dumps(bad), f"({len(checks)} checks)"); return 0 if not bad else 1

if __name__ == "__main__": sys.exit(main())
