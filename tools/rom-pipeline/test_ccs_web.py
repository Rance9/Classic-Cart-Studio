r"""Share or Export and .ccs packages on the website, across the website and Windows readers and writers (headless Edge; built site on
127.0.0.1; no other service: public share links are retired). Packages are complete (ccs-package/2, tools/companion/CCS-FORMAT.md); the
games are synthetic ROMs carrying a sentinel byte pattern (no real games). The package contract itself is test_ccs_v2_web.py.

  S  dialog            a mixed collection without ROM files here (factory, catalog, Extended Library, personal, an unknown entry) is not
                       packaged: Share or Export lists every game that needs its ROM file (unknown files marked), Export and Send stay
                       off; both ROM acknowledgment modals (export required before building; import gates only the ROM import)
  E  .ccs round trip   a mixed sentinel collection (Game Library versions, two versions of one title, headered and headerless personal
                       games, one with artwork, two loadouts with HOME settings) exported through Share or Export: the Windows reader
                       accepts it; imported in a clean browser: every game in order, every loadout and its HOME theme, titles and
                       artwork kept; the import is a new, independent collection
  H  Windows package   the same collection written by the Windows app's writer (ccs_package.write) opened on the website: every ROM
                       verified, Save to My Collections keeps every game and loadout, no ROM byte kept by the browser; Open in Windows
                       App falls back clearly when the app does not answer
  J  safety            traversal, duplicate entries, a changed collection byte, a changed ROM byte, a malformed manifest: refused,
                       collections unchanged
  K  large             1,200 games with three loadouts through a complete package without truncation
  L  network           no request with a body leaves this computer during export, import or the app handoff; the package is handed only
                       to 127.0.0.1; no request goes to share.classiccartstudio.com or carries ROM bytes off this computer; the only
                       remote reads are the download information
Screenshots of the Share or Export dialog (ROM files needed, all ready), both ROM acknowledgment modals and the package previews are
written to --shots.
  python test_ccs_web.py <built site folder> [--shots DIR]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, base64, json, os, shutil, sys, tempfile, time, zipfile
HERE = os.path.dirname(os.path.abspath(__file__)); TOOLS = os.path.dirname(HERE); sys.path[:0] = [HERE, os.path.join(TOOLS, "companion")]
import ccs_package as cp
from test_loadouts_web import EdgeSession, serve, free_port, READY, CALM, PERSONAL, CCS_EXPORT_UI, page_file, page_bytes, net_take, net_report
from test_ccs_v2_web import NET, ROMS

MARK = b"CCS-SENTINEL-ROM-BYTES"
def b64_marks():   # the sentinel as it would appear in base64 at any of the three alignments
    return [base64.b64encode(bytes(k) + MARK * 3).decode()[8:28] for k in range(3)]

# two loadouts with HOME settings: Everything (all games; theme and a folder of the personal games) and Two Players (the first three)
LOADOUTS = r"""((ids, mine)=>{ loPrepare({newOnly:true, mode:'all'}); $('loNm').value='Everything'; $('loGo').click(); localStorage.setItem(HOME_KEY, JSON.stringify({v:1, theme:'adventure-map', folders:[{id:'f:a', name:'Mine', parent:null, pos:0, order:mine}], favorites:{items:{}}, history:{}})); csSaveActiveNow(); loClose(true);
  loPrepare({newOnly:true, mode:'choose'}); $('loNm').value='Two Players'; $('loGo').click(); ids.slice(0,3).forEach(i=>sel.add(i)); persist(); csSaveActiveNow(); loClose(true); })"""

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--shots", default=""); a = ap.parse_args()
    shots = a.shots or os.path.join(tempfile.gettempdir(), "ccs-pkg-shots"); os.makedirs(shots, exist_ok=True)
    checks = []
    def ok(k, v, n=""): checks.append((k, bool(v))); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:400]})" if n and not v else ""), flush=True)
    def J(ed, expr):
        v = ed.js(expr); return json.loads(v) if isinstance(v, str) else v
    def shot(ed, name): open(os.path.join(shots, name + ".png"), "wb").write(ed.screenshot())
    web = serve(a.site); WEB = f"http://127.0.0.1:{web.server_address[1]}"; dead = free_port()
    tmp = tempfile.mkdtemp(prefix="ccs-web-"); p1 = os.path.join(tmp, "p1"); p2 = os.path.join(tmp, "p2"); os.makedirs(p1); os.makedirs(p2)
    URL = f"{WEB}/index.html?companion={dead}"   # a loopback port nothing listens on: the Windows app is "not running"
    def load(ed): ed.navigate(URL); ed.wait_for(READY, timeout=120); time.sleep(0.8); ed.js(CALM); ed.js(NET); ed.js(ROMS); ed.js(CCS_EXPORT_UI)
    nets = []
    try:
        # ================= sender =================
        with EdgeSession(width=1440, height=900, profile=p1, call_timeout=600) as ed:
            ed.navigate(WEB + "/robots.txt"); ed.js("localStorage.setItem('studio-welcomed-v1','1'); 1"); load(ed)
            pids = J(ed, PERSONAL)
            M = J(ed, r"""JSON.stringify((()=>{ csNew('Mixed Collection'); const ids=[];
              ['CLV-P-NAAHE','CLV-P-NAAAE'].forEach(cd=>ids.push(factoryRowFor(cd).game_id)); DATA.games.filter(g=>!g.builtin_code&&g.art).slice(0,3).forEach(g=>ids.push(g.game_id)); EXT_ROWS.filter(g=>g.art).slice(0,2).forEach(g=>ids.push(g.game_id));
              const t=Object.values(TITLES).find(t=>(t.versions||[]).filter(v=>byId[v]).length>=2&&!(t.versions||[]).some(v=>ids.includes(v))); const two=t.versions.filter(v=>byId[v]).slice(0,2); two.forEach(v=>ids.push(v)); COLL.multi[t.id]=true;
              __PIDS__.forEach(i=>ids.push(i)); const unk='x_unknownproto001'; COLL.missing=Object.assign(COLL.missing||{}, {[unk]:{title:'Unreleased Prototype', version:'Proto 2', source:'unknown', content_sha256:'c'.repeat(64)}}); ids.push(unk);
              ids.forEach(i=>sel.add(i)); persist(); render(); csSaveActiveNow(); return {ids, id:CS.active}; })())""".replace("__PIDS__", json.dumps(pids)))
            # ---- S: the Share or Export dialog for a collection without ROM files ----
            st = J(ed, f"(async()=>JSON.stringify(await ccsExportUi('{M['id']}', null)))()"); shot(ed, "1-share-or-export-roms-needed")
            ok("S_collection_without_roms_lists_every_game_and_is_not_packaged", st["exp"] and st["send"] and sorted(st["missing"]) == sorted(M["ids"]) and f"{len(M['ids'])} of {len(M['ids'])} games need their ROM file" in st["text"]
               and "Unreleased Prototype (file unknown)" in st["text"] and "My Homebrew Quest" in st["text"] and st["pick"] and not st.get("name"), st)
            ok("S_collection_without_roms_unchanged", J(ed, f"JSON.stringify(CS.items['{M['id']}'].sel)") == M["ids"])
            # both acknowledgment modals (the implemented UI)
            ed.js("closeColl(); window.__ack=ccsAck(CCS_EXPORT_ACK); 1"); time.sleep(0.3)
            st = J(ed, "JSON.stringify({dis:document.querySelector('#ccsAck [data-i=\"1\"]').disabled, inert:!!document.querySelector('main[inert],#toolbar[inert],.stickyhead[inert]'), title:$('ccsAckT').textContent})")
            shot(ed, "2-export-with-roms-modal")
            ed.js("$('ccsAckC').click()"); en = ed.js("!document.querySelector('#ccsAck [data-i=\"1\"]').disabled")
            ok("S_export_ack_required_before_export", st["dis"] and en and st["inert"] and st["title"] == "Export Collection with ROM Files", st)
            ed.js("document.querySelector('#ccsAck [data-i=\"0\"]').click()"); time.sleep(0.2)
            ed.js("window.__ack2=ccsAck(CCS_IMPORT_ACK); 1"); time.sleep(0.3); shot(ed, "3-import-with-roms-modal")
            st = J(ed, "JSON.stringify([...document.querySelectorAll('#ccsAck button')].map(b=>[b.textContent,b.disabled]))")
            ok("S_import_ack_only_gates_the_rom_import", st == [["Cancel", False], ["Import Without ROMs", False], ["Import Collection with ROMs", True]], st)
            ed.js("document.querySelector('#ccsAck [data-i=\"0\"]').click()")
            # ---- E: a mixed collection with every ROM file, exported through Share or Export ----
            E = J(ed, r"""(async()=>{ const rows=DATA.games.filter(g=>g.art&&!g.builtin_code).slice(30,32); const G=[];
              for(let i=0;i<2;i++) G.push(await ccsLib(rows[i], i));
              G.push(ccsNamed(await mkRom('v0', true), 'Twin Title', null, {version:'Rev 0'})); G.push(ccsNamed(await mkRom('v1', false), 'Twin Title', null, {version:'Rev 1'}));
              G.push(ccsNamed(await mkRom('own', true), 'My Homebrew Quest', ccsArt('#3a6'))); G.push(ccsNamed(await mkRom('hack', false), 'Translated Hack (T-En)', null));
              G.push(await mkRom('e0', true)); G.push(await mkRom('e1', false));
              csNew('Sentinel Mixed'); const ids=G.map(x=>x.id); ids.forEach(i=>sel.add(i)); persist(); render(); csSaveActiveNow(); (__LO__)(ids, G.slice(2,6).map(x=>x.id));
              window.__E=G; return JSON.stringify({ids, id:CS.active, own:G[4].id}); })()""".replace("__LO__", LOADOUTS))
            want = J(ed, f"JSON.stringify((()=>{{ const r=CS.items['{E['id']}']; return {{sel:r.sel, L:r.loadouts.map(l=>[l.name,l.sel,(l.home||{{}}).theme||null])}}; }})())")
            ed.js(f"(async()=>{{ ROMSRC.clear(); await ccsAddRomSources(__E.map(x=>x.file)); closeColl(); $('collBody').innerHTML=''; ccsShareDialog('{E['id']}'); return 1; }})()")
            ed.wait_for("!!$('ccsExp')&&!$('ccsExp').disabled", timeout=60); time.sleep(0.3); shot(ed, "1b-share-or-export-ready")
            st = J(ed, f"(async()=>JSON.stringify(await ccsExportUi('{E['id']}', null)))()")
            ok("E_complete_package_exported_through_share_or_export", st.get("ready") == 8 and st.get("ack") == "Export Collection with ROM Files" and st.get("name") == "Sentinel Mixed.ccs", st)
            complete = os.path.join(tmp, "Sentinel Mixed.ccs"); open(complete, "wb").write(page_bytes(ed, "window.__pkg"))
            info = cp.inspect(complete)
            ok("E_website_package_valid_for_the_windows_reader", info["ok"] and info["manifest"]["schema"] == "ccs-package/2" and info["rom_count"] == 8 and all(r["verified"] for r in info["roms"])
               and [r["version_id"] for r in info["roms"]] == want["sel"] and f"art/{E['own']}.jpg" in info["art"], {k: info.get(k) for k in ("ok", "rom_count", "art", "problems")})
            # ---- K: large ----
            big = J(ed, r"""(async()=>{ const N=1200, files=[], ids=[]; for(let i=0;i<N;i++){ const x=await mkRom('k'+i, i%2===0, 8192); ids.push(x.id); files.push(x.file); }
              csNew('Large Library'); ids.forEach(i=>sel.add(i)); persist(); render(); csSaveActiveNow();
              ['A','B','C'].forEach((n,k)=>{ loPrepare({newOnly:true, mode:'choose'}); $('loNm').value='Set '+n; $('loGo').click(); ids.slice(k*300, k*300+250).forEach(i=>sel.add(i)); persist(); csSaveActiveNow(); loClose(true); });
              ROMSRC.clear(); await ccsAddRomSources(files); const r=csActive(); const p=await ccsCollectionParts(r); const b=await ccsBuild(p); const info=await ccsInspect(b); const n=ccsSaveCollection(info);
              return JSON.stringify({size:b.size, ok:info.ok, roms:info.roms.length, same:JSON.stringify(n.sel)===JSON.stringify(r.sel), L:JSON.stringify(n.loadouts.map(l=>[l.name,l.sel]))===JSON.stringify(r.loadouts.map(l=>[l.name,l.sel])), nL:n.loadouts.length, n:n.sel.length}); })()""")
            ok("K_1200_games_and_three_loadouts_through_a_complete_package", big["ok"] and big["roms"] == 1200 and big["same"] and big["L"] and big["nL"] == 3 and big["n"] == 1200, big)
            nets.append(net_take(ed))
        # the Windows app's writer makes the same package from the same ROM files
        rom_dir = os.path.join(tmp, "roms"); os.makedirs(rom_dir); roms = []; art = {}
        with zipfile.ZipFile(complete) as z:
            for r in info["manifest"]["roms"]["included"]:
                pth = os.path.join(rom_dir, os.path.basename(r["path"])); open(pth, "wb").write(z.read(r["path"])); roms.append({"version_id": r["version_id"], "path": pth, "title": r["title"]})
            for n in info["art"]: art[n[4:]] = z.read(n)
        win_path = os.path.join(tmp, "Sentinel Mixed from Windows ünï.ccs")
        cp.write(win_path, collection=info["data"], roms=roms, art=art, summary={"name": "Sentinel Mixed"})
        ok("H_windows_writer_package_valid", cp.inspect(win_path)["ok"])
        # malicious and damaged packages
        evil = {}
        def mk(name, fn):
            pth = os.path.join(tmp, name + ".ccs")
            with zipfile.ZipFile(complete) as zi, zipfile.ZipFile(pth, "w") as zo: fn(zi, zo)
            evil[name] = pth
        def flip(b):
            b = bytearray(b); b[len(b) // 2] ^= 0x55; return bytes(b)
        first_rom = info["manifest"]["roms"]["included"][0]["path"]
        import warnings; warnings.simplefilter("ignore")
        mk("traversal", lambda zi, zo: [zo.writestr(i, zi.read(i)) for i in zi.infolist()] + [zo.writestr("../../evil.nes", b"x")])
        mk("duplicate", lambda zi, zo: [zo.writestr(i, zi.read(i)) for i in zi.infolist()] + [zo.writestr("collection.json", b"{}")])
        mk("changed", lambda zi, zo: [zo.writestr(i, (zi.read(i).replace(b"Sentinel Mixed", b"Sentinel Mixee") if i.filename == "collection.json" else zi.read(i))) for i in zi.infolist()])
        mk("rom_changed", lambda zi, zo: [zo.writestr(i, (flip(zi.read(i)) if i.filename == first_rom else zi.read(i))) for i in zi.infolist()])
        mk("malformed", lambda zi, zo: [zo.writestr(i, (b"{oops" if i.filename == cp.MANIFEST else zi.read(i))) for i in zi.infolist()])
        # ================= recipient (clean browser, no game files) =================
        with EdgeSession(width=1440, height=900, profile=p2) as ed:
            ed.navigate(WEB + "/robots.txt"); ed.js("localStorage.setItem('studio-welcomed-v1','1'); 1"); load(ed)
            ed.js("window.__launched=[]; ccsLaunch=u=>window.__launched.push(u); window.CCS_WAIT_TRIES=1; window.showSaveFilePicker=undefined; 1")
            n0 = ed.js("csList().length")
            # E: import the website's package
            page_file(ed, open(complete, "rb").read(), "Sentinel Mixed.ccs", "__F1"); ed.js("ccsOpenFile(__F1)"); ed.wait_for("!!$('ccsGo')", timeout=60)
            ok("E_import_verifies_every_rom", "8 verified" in ed.js("$('collBody').innerText"), ed.js("$('collBody').innerText")[:300])
            ed.js("$('ccsGo').click()"); ed.wait_for("!!$('ccsOpen')", timeout=30); ed.js("$('ccsOpen').click()"); time.sleep(0.4)
            got = J(ed, "JSON.stringify({sel:csActive().sel, L:csActive().loadouts.map(l=>[l.name,l.sel,(l.home||{}).theme||null]), name:csActive().name, own:Object.values(csActive().coll.missing||{}).filter(m=>/^data:image/.test(m.thumb_url||'')).length, titles:Object.values(csActive().coll.missing||{}).map(m=>m.title)})")
            ok("E_import_keeps_every_game_in_order", got["sel"] == want["sel"], (len(got["sel"]), len(want["sel"])))
            ok("E_import_keeps_every_loadout_and_its_HOME_theme", got["L"] == want["L"], got["L"])
            ok("E_import_keeps_personal_titles_and_artwork", got["own"] == 1 and "My Homebrew Quest" in got["titles"] and got["titles"].count("Twin Title") == 2, got)
            ok("E_import_is_a_new_independent_collection", ed.js("csList().length") == n0 + 1)
            # H: the Windows app's package on the website
            page_file(ed, open(win_path, "rb").read(), os.path.basename(win_path), "__F2"); ed.js("ccsOpenFile(__F2)"); ed.wait_for("!!$('ccsGo')", timeout=60); time.sleep(0.3)
            st = J(ed, "JSON.stringify({go:$('ccsGo').textContent, win:!!$('ccsWin'), body:$('collBody').innerText})")
            ok("H_windows_package_previewed_every_rom_verified", st["go"] == "Save to My Collections" and st["win"] and "8 verified" in st["body"] and "does not keep them" in st["body"], st)
            shot(ed, "4-windows-package-on-the-website")
            ed.js("$('ccsWin').click()"); ed.wait_for("$('collTitle').textContent==='Open in Windows App'&&$('collBody').textContent.includes('did not answer')", timeout=60)
            ok("H_open_in_windows_app_falls_back_clearly", ed.js("$('collBody').textContent.includes('did not answer')&&!!$('dl')") and ed.js("__launched.some(u=>u==='classiccartstudio://open')"))
            ed.js("closeColl(); ccsOpenFile(__F2)"); ed.wait_for("!!$('ccsGo')", timeout=60); ed.js("$('ccsGo').click()"); ed.wait_for("!!$('ccsOpen')", timeout=30); ed.js("$('ccsOpen').click()"); time.sleep(0.3)
            ok("H_saved_keeps_every_game_and_loadout", J(ed, "JSON.stringify(csActive().sel)") == want["sel"] and J(ed, "JSON.stringify(csActive().loadouts.map(l=>[l.name,l.sel,(l.home||{}).theme||null]))") == want["L"])
            store = ed.js("JSON.stringify([Object.fromEntries(Object.keys(localStorage).map(k=>[k,localStorage.getItem(k)])), Object.fromEntries(Object.keys(sessionStorage).map(k=>[k,sessionStorage.getItem(k)]))])")
            ok("H_no_rom_bytes_kept_by_the_website", MARK.decode()[:14] not in store and not any(m in store for m in b64_marks()), len(store))
            # J: unsafe and damaged packages
            nJ = ed.js("csList().length")
            for name, pth in evil.items():
                page_file(ed, open(pth, "rb").read(), name + ".ccs", "__FE"); ed.js("ccsOpenFile(__FE)"); time.sleep(0.8)
                try: ed.wait_for("$('collBody').textContent.includes('cannot be opened')||!!$('ccsGo')", timeout=30)
                except Exception: pass
                ok(f"J_refused_{name}", ed.js("$('collBody').textContent.includes('cannot be opened')") and not ed.js("!!$('ccsGo')"), ed.js("$('collBody').textContent.slice(0,200)"))
                ed.js("closeColl()")
            ok("J_collections_unchanged_after_refusals", ed.js("csList().length") == nJ)
            nets.append(net_take(ed))
        # ================= L: every request both browsers made =================
        rep = net_report(nets); allreq = [x for n in nets for x in n["net"]]
        handoff = [x for x in allreq if "/api/handoff/package" in x["url"]]
        ok("L_nothing_with_a_body_left_this_computer", not rep["body_remote"], rep["body_remote"][:3])
        ok("L_package_handed_only_to_this_computer", handoff and all(x["url"].startswith("http://127.0.0.1") for x in handoff), handoff)
        ok("L_no_share_service_and_no_rom_bytes_off_this_computer", not rep["share"] and not rep["rom_remote"], rep)
        ok("L_remote_requests_only_read_the_download_information", set(rep["remote"]) <= {"api.github.com", "github.com"}, rep["remote"])
    finally:
        web.shutdown(); shutil.rmtree(tmp, ignore_errors=True)
    bad = [k for k, v in checks if not v]
    print("RESULT:", "PASS" if not bad else "FAIL " + json.dumps(bad), f"({len(checks)} checks)"); print("screenshots:", shots); return 0 if not bad else 1

if __name__ == "__main__": sys.exit(main())
