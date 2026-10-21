r"""A collection of N games stays exactly N games through export, import, transfer, a complete .ccs package and sharing it again, whatever
the games' origin and whether the receiving side has their files (headless Edge, website mode; no other service: public share links are
retired, packages are made and read on this device).

Mixed collection (no ROM files anywhere): factory games, curated catalog games, Extended Library versions, two versions of one title,
personal games (one with the user's own artwork, local file paths), an entry whose identity is not in the Game Library. It stays a valid
collection without ROMs: the collection file and the transfer bundle (website <-> app format) list every game, a loadout of all of them
keeps every game, and Share or Export is blocked with the list of games that need their ROM file (nothing is built, nothing is saved).

Sentinel collection (synthetic ROMs carrying a sentinel byte pattern, no real games): Game Library versions (content SHA-256[:16] ids),
two versions of one title, a ROM with a 512-byte trainer, headered and headerless personal games, one with artwork, stray local paths.
Exported through Share or Export -> the Windows reader (ccs_package.inspect) accepts every game; imported in a clean browser with none of
the files: every game in order, unavailable games listed, titles and artwork kept; restart; the recipient exports N again and, choosing the
.ccs file as the ROM source, packages all N again. The file and bundle import in another clean browser. No request leaves this computer,
none goes to share.classiccartstudio.com, none carries ROM bytes.

  python test_collection_integrity.py <built site folder>      Exit 0 = PASS."""
from __future__ import annotations
import json, os, re, shutil, sys, tempfile, time
HERE = os.path.dirname(os.path.abspath(__file__)); TOOLS = os.path.dirname(HERE); sys.path[:0] = [HERE, os.path.join(TOOLS, "companion")]
from test_collections_web import EdgeSession, serve, free_port
from test_loadouts_web import CCS_EXPORT_UI, page_file, page_bytes, net_take, net_report
from test_ccs_v2_web import NET, ROMS
import ccs_package as cp

READY = "typeof CS!=='undefined'&&!!CS&&typeof byId!=='undefined'&&typeof csEntries==='function'&&typeof ccsShareDialog==='function'&&document.readyState==='complete'"
# the mixed collection, built in the page: personal rows are injected exactly as the app would hold them (no backend on the website)
BUILD = r"""(async()=>{ try{ doneWelcome(); }catch(e){}
 const c=document.createElement('canvas'); c.width=60; c.height=84; const x=c.getContext('2d'); x.fillStyle='#3a6'; x.fillRect(0,0,60,84); x.fillStyle='#fff'; x.fillRect(10,10,40,20); const own=c.toDataURL('image/png');
 const P=(id,title,art)=>{ const e={id, content_sha256:id.slice(2).padEnd(64,'a'), file_sha256:id.slice(2).padEnd(64,'b'), content_size:40976, file_size:41000, format:'nes', file_name:'D:\\\\Games\\\\someone\\\\ROMs\\\\'+title+'.nes', origin:'file', user:{title, year:'2024', publisher:'Homebrew Team'}, detected:{title}, file_present:true}; PERSONAL[id]=e; const r=perRow(e); if(art) r.art=art; byId[id]=r; return id; };
 csNew('Mixed Integrity'); const ids=[];
 ['CLV-P-NAAHE','CLV-P-NAAAE','CLV-P-NACDE'].forEach(cd=>ids.push(factoryRowFor(cd).game_id));
 DATA.games.filter(g=>!g.builtin_code&&g.art).slice(0,3).forEach(g=>ids.push(g.game_id));
 EXT_ROWS.filter(g=>g.art).slice(0,2).forEach(g=>ids.push(g.game_id));
 const t=Object.values(TITLES).find(t=>(t.versions||[]).filter(v=>byId[v]).length>=2&&!(t.versions||[]).some(v=>ids.includes(v))); const two=t.versions.filter(v=>byId[v]).slice(0,2); two.forEach(v=>ids.push(v)); COLL.multi[t.id]=true;
 ids.push(P('p_1111222233334444','My Homebrew Quest', own)); ids.push(P('p_5555666677778888','Translated Hack (T-En)', null));
 const unk='x_unknownproto001'; COLL.missing=Object.assign(COLL.missing||{}, {[unk]:{title:'Unreleased Prototype (Unknown)', version:'Proto 2', year:'1990', publisher:'', source:'unknown', art:'', content_sha256:'c'.repeat(64)}}); ids.push(unk);
 ids.forEach(i=>sel.add(i)); persist(); render(); csSaveActiveNow();
 return JSON.stringify({ids, two, n:sel.size, cid:CS.active}); })()"""
# a ROM with a 512-byte trainer: its content (and so its id) excludes the header and the trainer
TRAINER = r"""window.mkTrainer=async(i)=>{ const n=16384, c=new Uint8Array(n), s=new TextEncoder().encode('CCS-SENTINEL-ROM-BYTES'); for(let k=0;k<n;k++) c[k]=s[k%s.length]; const tag=new TextEncoder().encode('#trainer'+i+'#'+Math.random()); c.set(tag, n-tag.length);
 const u8=new Uint8Array(16+512+n); u8.set([0x4E,0x45,0x53,0x1A,1,1,4,0]); u8.fill(0x7E,16,16+512); u8.set(c,16+512); const sha=await ccsSha(c); const id='p_'+sha.slice(0,16); const title='Trainer Game '+i;
 const e={id, content_sha256:sha, file_sha256:await ccsSha(u8), content_size:n, file_size:u8.length, format:'nes', file_name:title+'.nes', origin:'file', user:{title, year:'2024', publisher:'Test'}, detected:{title}, file_present:true};
 PERSONAL[id]=e; byId[id]=perRow(e); return {id, title, file:new File([u8], title+'.nes')}; }; 1"""
# the sentinel collection: 13 games, every one with a synthetic ROM file
SENTINEL = r"""(async()=>{ const rows=DATA.games.filter(g=>g.art&&!g.builtin_code).slice(20,23); const G=[];
 for(let i=0;i<3;i++) G.push(await ccsLib(rows[i], i));
 const p=t=>({file_name:t+'.nes', source_path:'D:\\Games\\someone\\ROMs\\'+t+'.nes', path:'D:\\Games\\someone\\ROMs\\'+t+'.nes'});   // the app hands the page a file name; a stray local path must never reach a package
 G.push(ccsNamed(await mkRom('v0', true), 'Twin Title', null, {version:'Rev 0'})); G.push(ccsNamed(await mkRom('v1', false), 'Twin Title', null, {version:'Rev 1'}));
 G.push(await mkTrainer(0));
 G.push(ccsNamed(await mkRom('own', true), 'My Homebrew Quest', ccsArt('#3a6'), p('My Homebrew Quest')));
 G.push(ccsNamed(await mkRom('hack', true), 'Translated Hack (T-En)', null, p('Translated Hack (T-En)')));
 for(let k=0;k<5;k++) G.push(await mkRom('h'+k, k%2===0));
 csNew('Sentinel Integrity'); G.forEach(x=>sel.add(x.id)); persist(); render(); csSaveActiveNow(); window.__G=G;
 return JSON.stringify({ids:csActive().sel.slice(), n:sel.size, cid:CS.active, lib:G.slice(0,3).map(x=>x.id), twins:G.slice(3,5).map(x=>x.id), trainer:G[5].id, own:G[6].id}); })()"""

def main():
    site = sys.argv[1]; checks = []
    def ok(k, v, n=""): checks.append((k, bool(v), n)); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:300]})" if n and not v else ""), flush=True)
    J = lambda ed, e: json.loads(ed.js(e))
    web = serve(site); WEB = f"http://127.0.0.1:{web.server_address[1]}"
    profs = [tempfile.mkdtemp(prefix=f"ccs-int-{k}-") for k in ("a", "b", "c")]; URL = f"{WEB}/index.html?companion={free_port()}"   # nothing listens there: website mode
    tmp = tempfile.mkdtemp(prefix="ccs-int-pkg-"); ids_of = "JSON.stringify([...sel])"; nets = []
    def load(ed): ed.navigate(URL); ed.wait_for(READY, timeout=120); ed.js(NET); ed.js(ROMS); ed.js(TRAINER); ed.js(CCS_EXPORT_UI)
    try:
        # ---- sender ----
        with EdgeSession(width=1400, height=900, profile=profs[0]) as ed:
            load(ed)
            b = J(ed, BUILD); ids = b["ids"]; N = len(ids)
            ok("mixed_collection_built", N == 13 and b["n"] == 13, b)
            m = J(ed, "JSON.stringify(csFileFor(csActive()))")
            ok("export_lists_every_game", m["count"] == N and [s["version_id"] for s in m["selections"]].count("x_unknownproto001") == 1 and sorted(s["version_id"] for s in m["selections"]) == sorted(ids), m["count"])
            ok("export_keeps_both_versions", all(v in [s["version_id"] for s in m["selections"]] for v in b["two"]))
            ok("export_personal_records_without_paths", len(m["personal_library"]) == 2 and all(":\\" not in json.dumps(p) and "/Users" not in json.dumps(p) for p in m["personal_library"]), m["personal_library"][:1])
            bundle = J(ed, "JSON.stringify(csBundle([CS.active]))")
            ok("transfer_bundle_every_game", len(bundle["collections"][0]["sel"]) == N)
            # organizing needs no ROM file: a loadout of every game, a duplicate
            st = J(ed, f"""JSON.stringify((()=>{{ loPrepare({{newOnly:true, mode:'all'}}); $('loNm').value='Everything'; $('loGo').click(); const lo=loCur()&&loCur().sel.length; loClose(true); csSaveActiveNow();
              const d=csDuplicate('{b['cid']}'); const dn=d.sel.length; csSwitch('{b['cid']}', true); return {{lo, dup:dn, L:csActive().loadouts.map(l=>l.sel.length)}}; }})())""")
            ok("mixed_collection_organizes_without_roms", st["lo"] == N and st["dup"] == N and st["L"] == [N], st)
            # Share or Export: blocked, with the list of games that need their ROM file
            st = J(ed, f"(async()=>JSON.stringify(await ccsExportUi('{b['cid']}', null)))()")
            ok("mixed_share_blocked_with_the_missing_list", st["exp"] and st["send"] and len(st["missing"]) == N and sorted(st["missing"]) == sorted(ids) and f"{N} of {N} games need their ROM file" in st["text"]
               and "My Homebrew Quest" in st["text"] and "and 1 more" in st["text"] and st["pick"] and not st.get("name"), st["text"])
            st = J(ed, "(async()=>{ let e=''; try{ await ccsBuild(CCS.parts); }catch(x){ e=x.message; } return JSON.stringify({e, saved:!!window.__saved}); })()")
            ok("mixed_package_cannot_be_built_incomplete", "missing ROM" in st["e"] and not st["saved"], st)
            ed.js("closeColl(); 1")
            ok("mixed_collection_unchanged_after_blocked_export", J(ed, "JSON.stringify(csActive().sel)") == ids)
            # the sentinel collection: every game with its ROM
            s = J(ed, SENTINEL); sids = s["ids"]
            ok("sentinel_collection_built", s["n"] == 13 and len(sids) == 13, s)
            st = J(ed, f"(async()=>JSON.stringify(await ccsExportUi('{s['cid']}', __G.map(x=>x.file))))()")
            ok("package_exported_through_share_or_export", st.get("ready") == 13 and st.get("ack") == "Export Collection with ROM Files" and (st.get("name") or "").endswith(".ccs"), st)
            pkg = page_bytes(ed, "window.__pkg"); path = os.path.join(tmp, "Sentinel Integrity.ccs"); open(path, "wb").write(pkg)
            info = cp.inspect(path); inc = [r["version_id"] for r in info["manifest"]["roms"]["included"]]
            ok("windows_reader_accepts_every_game", info["ok"] and info["games"] == 13 and info["rom_count"] == 13 and all(r["verified"] for r in info["roms"]) and inc == sids
               and (info["data"]["ccs_collection"] or {}).get("sel") == sids, {k: info[k] for k in ("ok", "games", "rom_count", "problems")})
            ok("package_keeps_library_versions_both_twins_and_the_trainer_rom", all(i in inc for i in s["lib"] + s["twins"] + [s["trainer"]]) and len(set(s["twins"])) == 2 and all(not i.startswith("p_") for i in s["lib"]), inc)
            ok("package_carries_the_artwork_thumbnail", f"art/{s['own']}.jpg" in info["art"], info["art"])
            import zipfile
            with zipfile.ZipFile(path) as z: text = z.read("collection.json").decode("utf-8") + z.read(cp.MANIFEST).decode("utf-8")
            ok("package_has_no_local_paths", not re.search(r"[A-Za-z]:\\\\|\\\\Users\\\\|/Users/|someone", text), re.findall(r".{20}(?:someone|\\\\Users).{20}", text)[:3])
            nets.append(net_take(ed))
        # ---- a recipient in a clean browser, with none of the files ----
        with EdgeSession(width=1400, height=900, profile=profs[1]) as ed:
            load(ed); n0 = ed.js("csList().length")
            page_file(ed, pkg, "Sentinel Integrity.ccs", "__P"); ed.js("ccsOpenFile(__P)"); ed.wait_for("!!$('ccsGo')", timeout=60)
            ok("recipient_preview_verifies_every_rom", "13 verified" in ed.js("$('collBody').innerText"), ed.js("$('collBody').innerText")[:300])
            ed.js("$('ccsGo').click()"); ed.wait_for("!!$('ccsOpen')", timeout=30); ed.js("$('ccsOpen').click()"); time.sleep(0.4)
            ok("recipient_copy_every_game_in_order", J(ed, "JSON.stringify(csActive().sel)") == sids and ed.js("csList().length") == n0 + 1)
            ok("recipient_tray_lists_unavailable", ed.js("render(); document.querySelectorAll('#trayList li.trmiss').length") == 13)
            st = J(ed, "JSON.stringify(Object.values(csActive().coll.missing||{}).map(m=>({t:m.title, v:m.version, art:/^data:image/.test(m.thumb_url||'')})))")
            ok("recipient_keeps_titles_versions_and_artwork", sum(x["t"] == "Twin Title" for x in st) == 2 and {x["v"] for x in st if x["t"] == "Twin Title"} >= {"Rev 0", "Rev 1"} and any(x["t"] == "My Homebrew Quest" and x["art"] for x in st), st)
            nets.append(net_take(ed))
        with EdgeSession(width=1400, height=900, profile=profs[1]) as ed:
            load(ed)
            ok("recipient_copy_after_restart", J(ed, "JSON.stringify(csActive().sel)") == sids and ed.js("COLL.name") == "Sentinel Integrity")
            m2 = J(ed, "JSON.stringify(csFileFor(csActive()))")
            ok("recipient_export_every_game", m2["count"] == 13 and sorted(x["version_id"] for x in m2["selections"]) == sorted(sids))
            st = J(ed, "(async()=>JSON.stringify(await ccsExportUi(CS.active, null)))()")
            ok("recipient_share_asks_for_the_rom_files_again", st["exp"] and len(st["missing"]) == 13 and st["pick"], {k: st.get(k) for k in ("exp", "ready", "missing")})
            page_file(ed, pkg, "Sentinel Integrity.ccs", "__P")
            st = J(ed, "(async()=>JSON.stringify(await ccsExportUi(CS.active, [__P])))()")
            again = page_bytes(ed, "window.__pkg") if st.get("name") else b""; p2 = os.path.join(tmp, "again.ccs"); open(p2, "wb").write(again)
            i2 = cp.inspect(p2) if again else {"ok": False}
            ok("recipient_reshares_all_13_with_the_package_as_rom_source", st.get("ready") == 13 and i2["ok"] and i2["rom_count"] == 13 and [r["version_id"] for r in i2["roms"]] == sids, st)
            nets.append(net_take(ed))
        # ---- the original file and the transfer bundle in another clean browser ----
        with EdgeSession(width=1400, height=900, profile=profs[2]) as ed:
            load(ed)
            ed.js(f"csImportObject({json.dumps(m)}, 'mixed.json')"); time.sleep(1.5); ed.js("try{ closeColl(); }catch(e){}")
            ok("file_import_every_game", sorted(json.loads(ed.js(ids_of))) == sorted(ids) and ed.js("Object.keys(COLL.missing||{}).length") >= 3, len(json.loads(ed.js(ids_of))))
            ed.js(f"csImportObject({json.dumps(bundle)}, 'bundle.json')"); time.sleep(1)
            ok("bundle_import_every_game", sorted(json.loads(ed.js(ids_of))) == sorted(ids))
            nets.append(net_take(ed))
        with EdgeSession(width=1400, height=900, profile=profs[2]) as ed:
            load(ed)
            ok("imports_after_restart", all(len(r) == N for r in json.loads(ed.js("JSON.stringify(csList().filter(r=>r.name.startsWith('Mixed')).map(r=>r.sel))"))) and ed.js("csList().filter(r=>r.name.startsWith('Mixed')).length") == 2)
            nets.append(net_take(ed))
        rep = net_report(nets)
        ok("no_request_left_this_computer", not rep["remote"] and not rep["share"], rep)
        ok("no_request_carried_rom_bytes", not rep["rom"], rep["rom"][:3])
    finally:
        web.shutdown()
        for p in profs + [tmp]: shutil.rmtree(p, ignore_errors=True)
    bad = [k for k, v, _ in checks if not v]; print("RESULT:", "PASS" if not bad else "FAIL " + json.dumps(bad), f"({len(checks)} checks)"); return 0 if not bad else 1

if __name__ == "__main__": sys.exit(main())
