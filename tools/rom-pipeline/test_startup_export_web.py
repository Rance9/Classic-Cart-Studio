r"""A collection's own startup picture never leaves in a backup without its bytes (Codex cbr1 F2), on the built website (the Windows app
shows the same page), headless Edge, one fresh profile, no file is really saved (the page's save function is replaced by a recorder).
  heal      the page's in-memory copy is lost and marked unreadable while this browser's picture storage still has it: a collection's
            .ccs is made from the stored picture, read again and checked at that moment (Codex's injected failure)
  missing   the stored picture is gone: Back Up This Collection, Back Up All Collections and the collections file each stop with the
            reason, save nothing and offer Try Again; the collection still names its picture (never Random)
  damaged   the stored copy is not the picture it is named by: the backup stops with "damaged"
  back      the picture stored again: the backup carries it
  import    a backup naming a picture without its bytes, arriving where the picture is not kept: the collection keeps the choice, HOME
            Settings and a message say the picture is not in the backup; arriving where it is kept: usable
  backend   the Windows app's package writer (ccs_package.check_complete) refuses a collection or Full Studio Backup that names a picture
            without carrying it, and accepts one that carries it
  python test_startup_export_web.py <built site folder> [--out results.json]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, base64, hashlib, io, json, os, sys, tempfile
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE); sys.path.insert(0, os.path.join(HERE, "..", "companion"))
from studio_harness import EdgeSession, READY, CALM, serve

SETUP = r"""(async()=>{ await SU.ready; closeColl(); const r=CS.items[CS.active]; const c=document.createElement('canvas'); c.width=1280; c.height=720; const x=c.getContext('2d'); x.fillStyle='#375'; x.fillRect(0,0,1280,720);
 const b64=c.toDataURL('image/jpeg',.8).split(',')[1], u=suBytes(b64), sha=await suSha(u); await suPut(sha,u,'Test picture'); SU.b64.set(sha,b64);
 openFolders(); const m=TV.model; m.startup_custom={id:'custom-'+sha.slice(0,16), sha256:sha, name:'Test picture', fit:'fill', bytes:u.length, width:1280, height:720}; m.startup_image=m.startup_custom.id; hfSave(); csSaveActiveNow(); closeColl();
 window.__sha=sha; window.__b64=b64; window.__saves=[]; window.__toasts=[]; ccsSaveFile=async(n)=>{ window.__saves.push(n); return {ok:true, name:n}; }; ccsAck=async()=>'export'; confirmExportWarnings=()=>true;
 const t0=toast; toast=(t)=>{ window.__toasts.push(String(t)); try{ t0(t); }catch(e){} };
 return {sha, collection:r.name, home:!!CS.items[CS.active].home&&CS.items[CS.active].home.startup_image===m.startup_image}; })()"""
PARTS = r"""(async()=>{ const r=CS.items[CS.active]; try{ if(typeof fbKeptSources==='function') await fbKeptSources(); const p=await ccsCollectionParts(r); const c=p.file.ccs_collection.home.startup_custom; const blob=await ccsBuild(p); const i=await ccsInspect(blob);
 const ic=i.data&&i.data.ccs_collection&&i.data.ccs_collection.home&&i.data.ccs_collection.home.startup_custom; return {ok:true, data:!!(c&&c.data), inspected:i.ok, inspectedData:!!(ic&&ic.data), same:ic&&ic.data===window.__b64}; }
 catch(e){ return {ok:false, error:String(e&&e.message||e)}; } })()"""
STATE = "(()=>{ const r=CS.items[CS.active]; const h=r.home||{}; return {choice:h.startup_image, named:!!h.startup_custom, saves:window.__saves.length, state:suState(h.startup_custom)}; })()"

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); a = ap.parse_args()
    srv = serve(a.site); url = f"http://127.0.0.1:{srv.server_address[1]}/index.html"; res = []
    def ok(n, c, d=""):
        res.append({"name": n, "pass": bool(c)}); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:300]})" if d and not c else ""), flush=True)
    try:
        with EdgeSession(profile=tempfile.mkdtemp(prefix="ccs-suexp-"), width=1280, height=900) as ed:
            ed.navigate(url); ed.wait_for(READY, timeout=120); ed.js(CALM)
            ed.wait_for("CS.starter&&['done','failed'].includes(CS.starter.state)", timeout=120)
            s = ed.js(SETUP); ok("setup: the open collection's own startup picture is chosen and kept in this browser's storage", s.get("home") and s.get("sha"), s)
            # heal: Codex's injected state (the in-memory copy gone and marked unreadable, the stored picture intact)
            ed.js("SU.b64.delete(window.__sha); SU.bad.set(window.__sha,'the kept copy could not be read'); 1")
            p = ed.js(PARTS); ok("heal: with the in-memory copy lost and marked unreadable, the .ccs carries the picture read again from storage", p.get("ok") and p.get("data") and p.get("inspected") and p.get("inspectedData") and p.get("same"), p)
            # missing: the stored picture is gone
            ed.js("(async()=>{ await suTx('readwrite', s=>s.delete(window.__sha)); SU.b64.delete(window.__sha); return 1; })()")
            p = ed.js(PARTS); ok("missing: making the .ccs stops with the reason (the picture is not in this browser any more)", not p.get("ok") and "could not be read" in p.get("error", "") and "not in this browser any more" in p.get("error", "") and "HOME Settings" in p.get("error", ""), p)
            ed.js("ccsBackupDialog(CS.active); 1"); ed.wait_for("$('collBody')&&/was not made|Checking/.test($('collBody').textContent)", timeout=30)
            ed.wait_for("/was not made/.test($('collBody').textContent)", timeout=30)
            d = ed.js("({text:$('collBody').textContent, again:!!$('ccsAgain'), saves:window.__saves.length})")
            ok("missing: Back Up This Collection says the backup was not made, why, and offers Try Again; nothing is saved", "no file was saved" in d["text"] and "Startup screen" in d["text"] and d["again"] and d["saves"] == 0, d)
            ed.js("closeColl(); 1")
            ed.js("(async()=>{ await fbMake(); return 1; })()")
            d = ed.js("({text:($('collBody')||{}).textContent||'', again:!!$('fbAgain'), saves:window.__saves.length})")
            ok("missing: Back Up All Collections stops the same way; nothing is saved", "The backup was not created" in d["text"] and "could not be read" in d["text"] and d["again"] and d["saves"] == 0, d)
            ed.js("closeColl(); 1")
            ed.js("(async()=>{ window.__toasts.length=0; await csBackup(); return 1; })()")   # (csExport is the .ccs dialog above: ccs.js replaces the old file export)
            t = ed.js("({toasts:window.__toasts.slice(), saves:window.__saves.length})")
            ok("missing: the collections file (studio.js's csBackup) is not saved either, with the reason", t["saves"] == 0 and any("The file was not saved" in x and "could not be read" in x for x in t["toasts"]), t)
            st = ed.js(STATE)
            ok("missing: the collection still names its own picture as the startup screen (not Random), and HOME Settings says it cannot be used", st["named"] and st["choice"].startswith("custom-") and st["state"]["st"] == "bad", st)
            # damaged: another picture's bytes under its name
            ed.js("(async()=>{ await suTx('readwrite', s=>s.put({sha256:window.__sha, bytes:new Uint8Array([0xFF,0xD8,0xFF,0xE0,1,2,3,4]).buffer, name:'x', at:Date.now()})); return 1; })()")
            p = ed.js(PARTS); ok("damaged: a stored copy that is not the picture it is named by stops the backup (damaged)", not p.get("ok") and "damaged" in p.get("error", ""), p)
            # back: the picture stored again
            ed.js("(async()=>{ await suPut(window.__sha, suBytes(window.__b64), 'Test picture'); return 1; })()")
            p = ed.js(PARTS); ok("back: with the picture stored again, the .ccs carries it", p.get("ok") and p.get("data") and p.get("inspectedData") and p.get("same"), p)
            # import: a backup naming the picture without its bytes
            imp = r"""(async(have)=>{ if(!have){ await suTx('readwrite', s=>s.delete(window.__sha)); SU.b64.delete(window.__sha); SU.bad.delete(window.__sha); } window.__toasts.length=0;
              const h=CS.items[CS.active].home; const ref=Object.assign({}, h.startup_custom); delete ref.data; delete ref.mime;
              const {rec}=csRecordFromFile({name:'Arrived '+(have?'here':'elsewhere'), sel:[], home:{startup_image:ref.id, startup_custom:ref}}, 'x');
              for(let i=0;i<50&&SU.pending.size;i++) await new Promise(r=>setTimeout(r,100));
              return {choice:rec.home.startup_image, state:suState(rec.home.startup_custom), toasts:window.__toasts.slice()}; })(%s)"""
            r1 = ed.js(imp % "true")
            ok("import: a backup naming the picture without its bytes, where this browser keeps the picture: usable", r1["state"]["st"] == "ok" and r1["choice"].startswith("custom-"), r1)
            r2 = ed.js(imp % "false")
            ok("import: the same backup where the picture is not kept: the choice stays, HOME Settings and a message say the backup does not contain it",
               r2["state"]["st"] == "bad" and "does not contain it" in r2["state"].get("why", "") and r2["choice"].startswith("custom-") and any('"Arrived elsewhere"' in x and "cannot be used" in x for x in r2["toasts"]), r2)
            ok("no page errors", not ed.exceptions, ed.exceptions[:2])
        # backend: the Windows app's package writer
        comp = os.path.abspath(os.path.join(HERE, "..", "companion")); sys.path.insert(0, comp); sys.modules.pop("ccs_package", None); sys.modules.pop("startup_picture", None)   # the tested tree's writer (the harness puts another folder first)
        import ccs_package as cp
        ok("backend: the package writer under test is this tree's", os.path.dirname(os.path.abspath(cp.__file__)) == comp, cp.__file__)
        from PIL import Image
        bio = io.BytesIO(); Image.new("RGB", (1280, 720), (30, 60, 90)).save(bio, "JPEG", quality=80); raw = bio.getvalue(); sha = hashlib.sha256(raw).hexdigest()
        ref = {"id": "custom-" + sha[:16], "sha256": sha, "name": "Test picture", "fit": "fill", "bytes": len(raw), "width": 1280, "height": 720}
        def coll(c): return {"schema": "nes-classic-collection/4", "ccs_collection": {"name": "Living room", "sel": [], "home": {"startup_image": ref["id"], "startup_custom": c}}}
        def studio(c): return {"schema": cp.STUDIO_SCHEMA, "games": [], "studio_data": {"collections": [{"name": "Den", "sel": [], "home": {"startup_custom": c}}]}}
        def refused(o):
            try: cp.check_complete(o, []); return None
            except cp.PackageError as e: return str(e)
        good = dict(ref, data=base64.b64encode(raw).decode())
        ok("backend: a collection that carries its picture is accepted", refused(coll(good)) is None)
        w = refused(coll(dict(ref))); ok("backend: a collection .ccs naming a picture without it is refused before anything is written", w and "Living room" in w and "not with the collection" in w and "no file was saved" in w, w)
        w = refused(studio(dict(ref))); ok("backend: a Full Studio Backup naming a picture without it is refused", w and "Den" in w, w)
        w = refused(coll(dict(ref, data=base64.b64encode(b"\xff\xd8\xff\xe0other").decode()))); ok("backend: another picture's bytes under its name are refused", w and "content differs" in w, w)
    finally:
        srv.shutdown(); srv.server_close()
    n = sum(1 for r in res if r["pass"]); print(f"\n{n}/{len(res)} {'PASS' if n == len(res) else 'FAIL'}")
    if a.out: json.dump({"schema": "ccs-test-results/1", "results": res}, open(a.out, "w", encoding="utf-8"), indent=1)
    return 0 if n == len(res) else 1

if __name__ == "__main__":
    sys.exit(main())
