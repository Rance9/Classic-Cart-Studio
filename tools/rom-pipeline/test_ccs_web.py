r"""Sharing and .ccs packages on the website (headless Edge; built site on 127.0.0.1; share Worker locally with in-memory storage).

  E  .ccs round trip   a mixed collection (catalog, Extended Library, two versions of one title, personal games with artwork, an
                       unknown entry, two loadouts with HOME settings) exported as .ccs and imported again: every game in order,
                       every loadout, HOME settings and artwork kept; the import is a new, independent collection
  H  ROM package       a package with ROM files (made by the Windows app's writer) opened on the website: previewed, saved without
                       ROMs (the game entries stay), no ROM byte kept in the browser; Open Complete Package in Windows App falls back
                       clearly when the app does not answer
  L  network           no request leaves this computer during export, import or the app handoff; no request carries ROM bytes
  J  safety            traversal, duplicate entries, a changed byte, a malformed manifest: refused, collections unchanged
  K  large             1,200 games with three loadouts through .ccs without truncation
  A/B/D share link     the public link's page offers Open in Windows App with the download path; the share id stays in the address
Screenshots of the Share or Export dialog, both ROM acknowledgment modals and the ROM package preview are written to --shots.
  python test_ccs_web.py <built site folder> [--shots DIR]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, base64, json, os, shutil, subprocess, sys, tempfile, time, urllib.error, urllib.request, zipfile
HERE = os.path.dirname(os.path.abspath(__file__)); TOOLS = os.path.dirname(HERE); sys.path[:0] = [HERE, os.path.join(TOOLS, "companion")]
import cdp_edge
import ccs_package as cp
from test_loadouts_web import EdgeSession, serve, free_port, READY, CALM, PERSONAL

MARK = b"ROMBYTESMARKER-0123456789-"
def fake_rom(n): return b"NES\x1a" + bytes([2, 1, 0, 0]) + bytes(8) + (MARK + bytes([n])) * 600
def b64_marks():   # the marker as it would appear in base64 at any of the three alignments
    out = []
    for k in range(3):
        b = base64.b64encode(bytes(k) + MARK * 3).decode(); out.append(b[8:28])
    return out

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--shots", default=""); a = ap.parse_args()
    shots = a.shots or os.path.join(tempfile.gettempdir(), "ccs-pkg-shots"); os.makedirs(shots, exist_ok=True)
    checks = []
    def ok(k, v, n=""): checks.append((k, bool(v))); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:400]})" if n and not v else ""), flush=True)
    def J(ed, expr):
        v = ed.js(expr); return json.loads(v) if isinstance(v, str) else v
    def shot(ed, name): open(os.path.join(shots, name + ".png"), "wb").write(ed.screenshot())
    web = serve(a.site); WEB = f"http://127.0.0.1:{web.server_address[1]}"
    sport = free_port(); SHARE = f"http://127.0.0.1:{sport}"; dead = free_port()
    dev = subprocess.Popen([shutil.which("node"), os.path.join(TOOLS, "share-worker", "test", "dev-server.mjs"), str(sport), WEB], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    tmp = tempfile.mkdtemp(prefix="ccs-web-"); p1 = os.path.join(tmp, "p1"); p2 = os.path.join(tmp, "p2"); os.makedirs(p1); os.makedirs(p2)
    URL = f"{WEB}/index.html?shareapi={SHARE}&companion={dead}"   # a loopback port nothing listens on: the Windows app is "not running"
    def load(ed): ed.navigate(URL); ed.wait_for(READY, timeout=120); time.sleep(0.8); ed.js(CALM)
    def to_page_file(ed, path, var):
        b = base64.b64encode(open(path, "rb").read()).decode()
        ed.js(f"(()=>{{ const s=atob('{b}'); const u=new Uint8Array(s.length); for(let i=0;i<s.length;i++) u[i]=s.charCodeAt(i); window.{var}=new File([u], {json.dumps(os.path.basename(path))}); return 1; }})()")
    try:
        for _ in range(40):
            try: urllib.request.urlopen(SHARE + "/c/none", timeout=1)
            except urllib.error.HTTPError: break
            except Exception: time.sleep(0.25)
        # ================= sender =================
        with EdgeSession(width=1440, height=900, profile=p1) as ed:
            ed.navigate(WEB + "/robots.txt"); ed.js("localStorage.setItem('studio-welcomed-v1','1'); 1"); load(ed)
            pids = J(ed, PERSONAL)
            M = J(ed, r"""JSON.stringify((()=>{ csNew('Mixed Package'); const ids=[];
              ['CLV-P-NAAHE','CLV-P-NAAAE'].forEach(cd=>ids.push(factoryRowFor(cd).game_id)); DATA.games.filter(g=>!g.builtin_code&&g.art).slice(0,3).forEach(g=>ids.push(g.game_id)); EXT_ROWS.filter(g=>g.art).slice(0,2).forEach(g=>ids.push(g.game_id));
              const t=Object.values(TITLES).find(t=>(t.versions||[]).filter(v=>byId[v]).length>=2&&!(t.versions||[]).some(v=>ids.includes(v))); const two=t.versions.filter(v=>byId[v]).slice(0,2); two.forEach(v=>ids.push(v)); COLL.multi[t.id]=true;
              __PIDS__.forEach(i=>ids.push(i)); const unk='x_unknownproto001'; COLL.missing=Object.assign(COLL.missing||{}, {[unk]:{title:'Unreleased Prototype', version:'Proto 2', source:'unknown', content_sha256:'c'.repeat(64)}}); ids.push(unk);
              ids.forEach(i=>sel.add(i)); persist(); render(); csSaveActiveNow();
              loPrepare({newOnly:true, mode:'all'}); $('loNm').value='Everything'; $('loGo').click(); localStorage.setItem(HOME_KEY, JSON.stringify({v:1, theme:'adventure-map', folders:[{id:'f:a', name:'Mine', parent:null, pos:0, order:__PIDS__}], favorites:{items:{}}, history:{}})); csSaveActiveNow(); loClose(true);
              loPrepare({newOnly:true, mode:'choose'}); $('loNm').value='Two Players'; $('loGo').click(); ids.slice(0,3).forEach(i=>sel.add(i)); persist(); csSaveActiveNow(); loClose(true);
              return {ids, id:CS.active}; })())""".replace("__PIDS__", json.dumps(pids)))
            want = J(ed, f"JSON.stringify((()=>{{ const r=CS.items['{M['id']}']; return {{sel:r.sel, L:r.loadouts.map(l=>[l.name,l.sel,(l.home||{{}}).theme||null])}}; }})())")
            # the Share or Export dialog and both acknowledgment modals (the implemented UI)
            ed.js("ccsShareDialog()"); time.sleep(0.4); shot(ed, "1-share-or-export-dialog")
            ok("share_dialog_offers_link_ccs_and_roms", ed.js("['link','ccs','roms'].every(v=>!!document.querySelector(`input[name=ccsOpt][value=${v}]`))&&document.querySelector('input[name=ccsOpt][value=roms]').disabled"))
            ed.js("closeColl(); window.__ack=ccsAck(CCS_EXPORT_ACK); 1"); time.sleep(0.3)
            st = J(ed, "JSON.stringify({dis:document.querySelector('#ccsAck [data-i=\"1\"]').disabled, inert:!!document.querySelector('main[inert],#toolbar[inert],.stickyhead[inert]'), title:$('ccsAckT').textContent})")
            shot(ed, "2-export-with-roms-modal")
            ed.js("$('ccsAckC').click()"); en = ed.js("!document.querySelector('#ccsAck [data-i=\"1\"]').disabled")
            ok("export_ack_required_before_export", st["dis"] and en and st["inert"] and st["title"] == "Export Collection with ROM Files", st)
            ed.js("document.querySelector('#ccsAck [data-i=\"0\"]').click()"); time.sleep(0.2)
            ed.js("window.__ack2=ccsAck(CCS_IMPORT_ACK); 1"); time.sleep(0.3); shot(ed, "3-import-with-roms-modal")
            st = J(ed, "JSON.stringify([...document.querySelectorAll('#ccsAck button')].map(b=>[b.textContent,b.disabled]))")
            ok("import_ack_only_gates_the_rom_import", st == [["Cancel", False], ["Import Without ROMs", False], ["Import Collection with ROMs", True]], st)
            ed.js("document.querySelector('#ccsAck [data-i=\"0\"]').click()")
            # ---- E: export (ROM-free) ----
            ed.call("Network.enable", {})
            pkg = J(ed, f"(async()=>{{ const r=CS.items['{M['id']}']; const p=await ccsCollectionParts(r); const b=await ccsMake('collection', p.file, p.art, p.summary); const u=new Uint8Array(await b.arrayBuffer()); let s=''; for(let i=0;i<u.length;i+=0x8000) s+=String.fromCharCode.apply(null,u.subarray(i,i+0x8000)); return JSON.stringify({{b64:btoa(s), art:Object.keys(p.art)}}); }})()")
            free_path = os.path.join(tmp, "Mixed Package.ccs"); open(free_path, "wb").write(base64.b64decode(pkg["b64"]))
            info = cp.inspect(free_path)
            ok("E_website_package_valid_for_the_windows_reader", info["ok"] and info["kind"] == "collection" and info["rom_count"] == 0 and any(f["path"].startswith("art/") for f in info["manifest"]["files"]), [f["path"] for f in info["manifest"]["files"]])
            coll_json = info["data"]
            # ---- K: large ----
            big = J(ed, r"""(async()=>{ csNew('Large Library'); const ids=DATA.games.map(g=>g.game_id).concat(EXT_ROWS.map(g=>g.game_id)).filter((v,i,a)=>a.indexOf(v)===i).slice(0,1200); ids.forEach(i=>sel.add(i)); persist(); render(); csSaveActiveNow();
              ['A','B','C'].forEach((n,k)=>{ loPrepare({newOnly:true, mode:'choose'}); $('loNm').value='Set '+n; $('loGo').click(); ids.slice(k*300, k*300+250).forEach(i=>sel.add(i)); persist(); csSaveActiveNow(); loClose(true); });
              const r=csActive(); const p=await ccsCollectionParts(r); const b=await ccsMake('collection', p.file, p.art, p.summary); const info=await ccsInspect(b); const n=ccsSaveCollection(info);
              return JSON.stringify({size:b.size, ok:info.ok, same:JSON.stringify(n.sel)===JSON.stringify(r.sel), L:JSON.stringify(n.loadouts.map(l=>[l.name,l.sel]))===JSON.stringify(r.loadouts.map(l=>[l.name,l.sel])), n:n.sel.length}); })()""")
            ok("K_1200_games_and_three_loadouts_through_ccs", big["ok"] and big["same"] and big["L"] and big["n"] == 1200, big)
            # ---- share link (A): publish with the loadouts ----
            ed.js(f"csSwitch('{M['id']}', true); openShare()"); ed.wait_for("!!$('shGo')&&!$('shGo').disabled", timeout=30); time.sleep(0.8)
            ed.js("$('shGo').click()"); ed.wait_for("!!(SH.pub&&SH.pub.url)||!!document.querySelector('#shOut .bad')", timeout=60); share_id = ed.js("SH.pub&&SH.pub.id")
            ok("A_published", bool(share_id), ed.js("$('shOut').textContent"))
        # the Windows app's writer makes a ROM package of the same collection (two personal games and one catalog game with files)
        rom_dir = os.path.join(tmp, "roms"); os.makedirs(rom_dir); roms = []
        for k, vid in enumerate([pids[0], pids[1], M["ids"][2]]):
            pth = os.path.join(rom_dir, f"{k}.nes"); open(pth, "wb").write(fake_rom(k)); roms.append({"version_id": vid, "path": pth, "title": f"Game {k}"})
        rom_path = os.path.join(tmp, "Mixed Package with ROMs ünï.ccs")
        cp.write(rom_path, collection=coll_json, roms=roms, summary={"games": len(M["ids"])}, missing=[{"version_id": M["ids"][0], "title": "x", "reason": "the game file is not on this computer"}])
        # malicious and damaged packages
        evil = {}
        def mk(name, fn):
            pth = os.path.join(tmp, name + ".ccs")
            with zipfile.ZipFile(free_path) as zi, zipfile.ZipFile(pth, "w") as zo: fn(zi, zo)
            evil[name] = pth
        import warnings; warnings.simplefilter("ignore")
        mk("traversal", lambda zi, zo: [zo.writestr(i, zi.read(i)) for i in zi.infolist()] + [zo.writestr("../../evil.nes", b"x")])
        mk("duplicate", lambda zi, zo: [zo.writestr(i, zi.read(i)) for i in zi.infolist()] + [zo.writestr("collection.json", b"{}")])
        mk("changed", lambda zi, zo: [zo.writestr(i, (zi.read(i).replace(b"Mixed Package", b"Mixed Packagf") if i.filename == "collection.json" else zi.read(i))) for i in zi.infolist()])
        mk("malformed", lambda zi, zo: [zo.writestr(i, (b"{oops" if i.filename == cp.MANIFEST else zi.read(i))) for i in zi.infolist()])
        # ================= recipient (clean browser, no game files) =================
        reqs = []
        with EdgeSession(width=1440, height=900, profile=p2) as ed:
            ed.navigate(WEB + "/robots.txt"); ed.js("localStorage.setItem('studio-welcomed-v1','1'); 1"); load(ed)
            ed.call("Network.enable", {})
            ed.js("window.__launched=[]; ccsLaunch=u=>window.__launched.push(u); window.CCS_WAIT_TRIES=1; window.showSaveFilePicker=undefined; 1")
            NET = "(()=>{ window.__net=window.__net||[]; const of=window.fetch; window.fetch=function(u,i){ const url=String(u&&u.url||u); const b=i&&i.body; window.__net.push([url,(i&&i.method)||'GET', b?(b.size||b.length||b.byteLength||1):0]); return of.apply(this,arguments); }; const ob=navigator.sendBeacon&&navigator.sendBeacon.bind(navigator); if(ob) navigator.sendBeacon=(u,d)=>{ window.__net.push([String(u),'BEACON',1]); return ob(u,d); }; return 1; })()"
            ed.js(NET)
            n0 = ed.js("csList().length")
            # E: import the ROM-free package
            to_page_file(ed, free_path, "__F1"); ed.js("ccsOpenFile(__F1)"); ed.wait_for("!!$('ccsGo')", timeout=30)
            ed.js("$('ccsGo').click()"); ed.wait_for("!!$('ccsOpen')", timeout=30); ed.js("$('ccsOpen').click()"); time.sleep(0.4)
            got = J(ed, "JSON.stringify({sel:csActive().sel, L:csActive().loadouts.map(l=>[l.name,l.sel,(l.home||{}).theme||null]), name:csActive().name, own:Object.values(csActive().coll.missing||{}).filter(m=>/^data:image/.test(m.thumb_url||'')).length, titles:Object.values(csActive().coll.missing||{}).map(m=>m.title)})")
            ok("E_import_keeps_every_game_in_order", got["sel"] == want["sel"], (len(got["sel"]), len(want["sel"])))
            ok("E_import_keeps_every_loadout_and_its_HOME_theme", got["L"] == want["L"], got["L"])
            ok("E_import_keeps_personal_titles_and_artwork", got["own"] >= 1 and "My Homebrew Quest" in got["titles"] and "Unreleased Prototype" in got["titles"], got)
            ok("E_import_is_a_new_independent_collection", ed.js("csList().length") == n0 + 1)
            # H: the ROM package on the website
            to_page_file(ed, rom_path, "__F2"); ed.js("ccsOpenFile(__F2)"); ed.wait_for("!!$('ccsGo')", timeout=30); time.sleep(0.3)
            st = J(ed, "JSON.stringify({go:$('ccsGo').textContent, win:!!$('ccsWin'), body:$('collBody').innerText})")
            ok("H_rom_package_previewed_with_its_rom_count", st["go"] == "Save Collection Without ROMs" and st["win"] and "ROM files included\n3" in st["body"].replace("\t", "\n") or ("3 (" in st["body"] and st["win"]), st)
            shot(ed, "4-rom-package-on-the-website")
            ed.js("$('ccsWin').click()"); ed.wait_for("$('collTitle').textContent==='Open in Windows App'&&$('collBody').textContent.includes('did not answer')", timeout=30)
            ok("H_open_in_windows_app_falls_back_clearly", ed.js("$('collBody').textContent.includes('did not answer')&&!!$('dl')") and ed.js("__launched.some(u=>u==='classiccartstudio://open')"))
            ed.js("closeColl(); ccsOpenFile(__F2)"); ed.wait_for("!!$('ccsGo')", timeout=30); ed.js("$('ccsGo').click()"); ed.wait_for("!!$('ccsOpen')", timeout=30); ed.js("$('ccsOpen').click()"); time.sleep(0.3)
            ok("H_saved_without_roms_keeps_every_game_and_loadout", J(ed, "JSON.stringify(csActive().sel)") == want["sel"] and J(ed, "JSON.stringify(csActive().loadouts.map(l=>[l.name,l.sel,(l.home||{}).theme||null]))") == want["L"])
            store = ed.js("JSON.stringify(Object.fromEntries(Object.keys(localStorage).map(k=>[k,localStorage.getItem(k)])))")
            ok("H_no_rom_bytes_kept_by_the_website", MARK.decode()[:14] not in store and not any(m in store for m in b64_marks()))
            # J: unsafe and damaged packages
            nJ = ed.js("csList().length")
            for name, pth in evil.items():
                to_page_file(ed, pth, "__FE"); ed.js("ccsOpenFile(__FE)"); time.sleep(0.6)
                ok(f"J_refused_{name}", ed.js("$('collBody').textContent.includes('cannot be opened')||$('collBody').textContent.includes('Integrity problems')") and not ed.js("!!$('ccsGo')&&!$('collBody').textContent.includes('Integrity problems')"), ed.js("$('collBody').textContent.slice(0,200)"))
                ed.js("closeColl()")
            ok("J_collections_unchanged_after_refusals", ed.js("csList().length") == nJ)
            # A/B/D: the share link
            net1 = J(ed, "JSON.stringify(window.__net||[])")
            ed.navigate(f"{URL}&share={share_id}"); ed.wait_for(READY, timeout=120); ed.js(CALM); ed.js(NET); ed.wait_for("!!$('srSave')", timeout=30)
            ed.js("window.__launched=[]; ccsLaunch=u=>window.__launched.push(u); 1")
            ok("D_share_id_stays_in_the_address", f"share={share_id}" in ed.js("location.search"))
            ed.js("$('srApp').click()"); time.sleep(0.3)
            ok("C_D_open_in_windows_app_uses_the_protocol_and_offers_the_download", ed.js(f"__launched.includes('classiccartstudio://share/{share_id}')") and ed.js("!!$('dl')&&$('collBody').textContent.includes('come back to this page')"))
            ed.js("$('srBack').click()"); ed.wait_for("!!$('srSave')", timeout=30)
            ok("A_recipient_sees_the_loadouts", ed.js("$('collBody').textContent.includes('NES Classic Loadouts')&&$('collBody').textContent.includes('Two Players')"))
            ed.js("$('srSave').click()"); ed.wait_for("!!$('srOpen')", timeout=10); ed.js("$('srOpen').click()"); time.sleep(0.3)
            ok("B_saved_into_the_main_website_collections", J(ed, "JSON.stringify(csActive().loadouts.map(l=>[l.name,l.sel]))") == [[n, s] for n, s, _ in want["L"]] and ed.js("csManager(), $('collBody').textContent.includes('Mixed Package')"))
            # L: every request the recipient made
            reqs = net1 + J(ed, "JSON.stringify(window.__net||[])")
        local = lambda u: not u.startswith(("http://", "https://")) or u.startswith(("http://127.0.0.1", "http://localhost"))
        uploads = [r for r in reqs if r[2] and not local(r[0])]
        remote = sorted({r[0].split("/")[2] for r in reqs if not local(r[0])})
        handoff = [r for r in reqs if "/api/handoff/package" in r[0]]
        ok("L_nothing_with_a_body_left_this_computer", not uploads, uploads[:3])
        ok("L_rom_package_handed_only_to_this_computer", handoff and all(r[0].startswith("http://127.0.0.1") for r in handoff), handoff)
        ok("L_remote_requests_only_read_the_download_information", set(remote) <= {"api.github.com", "github.com"}, remote)
    finally:
        dev.terminate(); shutil.rmtree(tmp, ignore_errors=True)
    bad = [k for k, v in checks if not v]
    print("RESULT:", "PASS" if not bad else "FAIL " + json.dumps(bad), f"({len(checks)} checks)"); print("screenshots:", shots); return 0 if not bad else 1

if __name__ == "__main__": sys.exit(main())
