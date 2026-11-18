r"""A collection's own console startup picture on the built website (the Windows app shows the same page), headless Edge, one fresh
profile; pictures are chosen through the page's real file picker (the browser's file chooser, answered with local test files made here).
  choose    HOME Settings › Choose Image…: a photo stored sideways with an EXIF orientation is framed upright; Fit shows it whole on black,
            Fill fills the screen; Use This Picture makes it the startup screen ("Your picture"), 1280x720, with no metadata (the photo's
            artist and comment are not in it); the person's file is unchanged; only this browser's storage holds the bytes (the collection
            records in localStorage keep a short reference); no request leaves this computer
  replace   Replace Image… with a transparent PNG: transparency is black; the earlier picture stays until the new one is kept, and is
            removed from this browser's storage once nothing names it (another collection's picture never is)
  refuse    animated PNG, WebP and GIF, a damaged JPEG, a picture too small or too large, a file that is not a picture and a file over
            the size limit: each is refused with its reason, and nothing changes
  isolate   a second collection has its own picture (or none); switching back shows the first one's
  reload    after a reload the picture is still the startup screen
  carry     a .ccs file of the collection carries the picture's bytes; opened in a browser that does not have the picture, it is checked
            and kept again; a Full Studio Backup does the same; a damaged picture in a backup is refused with its reason (HOME Settings
            says so, the console package request carries no picture) and never turns into Random; the console package request carries
            the chosen picture's bytes
  python test_startup_custom_web.py <built site folder> [--out results.json]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, hashlib, io, json, os, sys, tempfile, time
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from studio_harness import EdgeSession, READY, CALM, serve

def make_files(d: str) -> dict:
    from PIL import Image
    f = {}
    def save(name, im, fmt, **kw):
        p = os.path.join(d, name); im.save(p, fmt, **kw); f[name] = p
    # a portrait photo (900x1600, red band on top) stored sideways (1600x900) with EXIF orientation 6 (display: turn 90 degrees clockwise)
    content = Image.new("RGB", (900, 1600), (20, 40, 200)); content.paste((220, 20, 20), (0, 0, 900, 200))
    stored = content.rotate(90, expand=True); ex = Image.Exif(); ex[0x0112] = 6; ex[0x013B] = "Owner Name"
    save("living-room-photo.jpg", stored, "JPEG", quality=90, exif=ex.tobytes(), comment=b"secret comment")
    t = Image.new("RGBA", (1600, 900), (255, 255, 255, 0)); t.paste((10, 200, 30, 255), (800, 0, 1600, 900)); save("transparent.png", t, "PNG")
    save("second.png", Image.new("RGB", (1280, 720), (240, 200, 0)), "PNG")
    a1, a2 = Image.new("RGB", (640, 360), (0, 0, 0)), Image.new("RGB", (640, 360), (255, 255, 255))
    save("animated.png", a1, "PNG", save_all=True, append_images=[a2], duration=100); save("animated.webp", a1, "WEBP", save_all=True, append_images=[a2], duration=100)
    save("animated.gif", a1, "GIF", save_all=True, append_images=[a2], duration=100)
    save("tiny.png", Image.new("RGB", (200, 100)), "PNG"); save("wide.png", Image.new("RGB", (13000, 20)), "PNG")
    p = os.path.join(d, "damaged.jpg"); open(p, "wb").write(b"\xff\xd8\xff\xe0" + os.urandom(4000)); f["damaged.jpg"] = p
    p = os.path.join(d, "notes.png"); open(p, "wb").write(b"These are notes, not a picture.\n" * 20); f["notes.png"] = p
    p = os.path.join(d, "huge.jpg"); open(p, "wb").write(b"\xff\xd8\xff\xe0" + bytes(26_000_000)); f["huge.jpg"] = p
    return f

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); a = ap.parse_args()
    srv = serve(a.site); url = f"http://127.0.0.1:{srv.server_address[1]}/index.html"; res, log = [], {}
    work = tempfile.mkdtemp(prefix="ccs-startup-files-"); F = make_files(work); before = {k: hashlib.sha256(open(p, "rb").read()).hexdigest() for k, p in F.items()}
    def ok(n, c, d=""):
        res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:300]})" if d and not c else ""), flush=True)
    try:
        with EdgeSession(profile=tempfile.mkdtemp(prefix="ccs-startup-"), width=1440, height=1000) as ed:
            chosen = []; orig = ed.ws.recv
            def recv(*x, **k):
                raw = orig(*x, **k)
                try:
                    r = json.loads(raw)
                    if r.get("method") == "Page.fileChooserOpened": chosen.append(r["params"])
                except Exception: pass
                return raw
            ed.ws.recv = recv
            ed.navigate(url); ed.wait_for(READY, timeout=120); ed.js(CALM)
            ed.wait_for("CS.starter&&['done','failed'].includes(CS.starter.state)", timeout=120); time.sleep(0.8)
            ed.call("Page.enable"); ed.call("DOM.enable"); ed.call("Page.setInterceptFileChooserDialog", {"enabled": True})
            t0 = ed.js("performance.now()")
            def click(sel):
                p = ed.js("(()=>{ const b=document.querySelector(%s); if(!b) return null; b.scrollIntoView({block:'center', behavior:'instant'}); const r=b.getBoundingClientRect(); return {x:r.x+r.width/2, y:r.y+r.height/2}; })()" % json.dumps(sel))
                if not p: return False
                for t in ("mouseMoved", "mousePressed", "mouseReleased"):
                    ed.call("Input.dispatchMouseEvent", {"type": t, "x": p["x"], "y": p["y"], "button": "left", "clickCount": 1} if t != "mouseMoved" else {"type": t, "x": p["x"], "y": p["y"]})
                time.sleep(0.3); return True
            def pick(name):   # Choose/Replace Image… with the real picker, answered with this file
                for attempt in range(3):   # the picker's input is not in the page: with the chooser intercepted, nothing keeps it alive, so it can be collected first
                    chosen.clear(); click("#suPick")
                    for _ in range(50):
                        if chosen: break
                        ed.js("1"); time.sleep(0.1)
                    if not chosen: return False
                    try: ed.call("DOM.setFileInputFiles", {"files": [F[name]], "backendNodeId": chosen[-1]["backendNodeId"]})
                    except Exception: continue
                    time.sleep(1.5); return True
                return False
            def model(): return ed.js("(()=>{ const m=TV.model; return {choice:m.startup_image, custom:m.startup_custom||null, draft:!!SU.draft, out:(document.getElementById('suOut')||{}).textContent||'', opts:[...document.querySelectorAll('#hfStartup option')].map(o=>o.textContent), sel:(document.getElementById('hfStartup')||{}).value||null}; })()")
            def px(expr, pts):   # pixels of a JPEG data URL at the given points
                return ed.js("(async()=>{ const im=new Image(); im.src=%s; await im.decode(); const c=document.createElement('canvas'); c.width=im.naturalWidth; c.height=im.naturalHeight; const x=c.getContext('2d'); x.drawImage(im,0,0); return {w:c.width, h:c.height, p:%s.map(([a,b])=>[...x.getImageData(a,b,1,1).data].slice(0,3))}; })()" % (expr, json.dumps(pts)))
            near = lambda c, want, tol=60: c and all(abs(c[i] - want[i]) <= tol for i in range(3))
            def idb(): return ed.js("(async()=>{ try{ return (await suTx('readonly', s=>s.getAll())).map(r=>({sha:r.sha256, n:r.bytes.byteLength})); }catch(e){ return 'error '+e.message; } })()")
            # ---- choose ----
            ed.js("openFolders(); 1"); time.sleep(0.6)
            ok("setup: HOME Settings offers Choose Image… and the startup screen is Random", ed.js("!!document.querySelector('#suPick')&&/Choose Image/.test(document.querySelector('#suPick').textContent)") and model()["choice"] == "random", model())
            ok("choose: the real file picker opens and takes the photo", pick("living-room-photo.jpg") and ed.js("!!SU.draft"), model())
            d = ed.js("({fit:SU.draft.fit, w:SU.draft.w, h:SU.draft.h, name:SU.draft.name})"); log["draft"] = d
            fitpx = px("SU.draft.url", [[640, 40], [640, 600], [100, 360], [1200, 360]])
            ok("choose: the photo is turned upright as its EXIF orientation says (900x1600) and Fit shows it whole on black (red band on top, black sides)",
               d["w"] == 900 and d["h"] == 1600 and d["fit"] == "fit" and fitpx["w"] == 1280 and fitpx["h"] == 720 and near(fitpx["p"][0], (220, 20, 20)) and near(fitpx["p"][1], (20, 40, 200)) and near(fitpx["p"][2], (0, 0, 0), 12) and near(fitpx["p"][3], (0, 0, 0), 12), (d, fitpx))
            click("input[name=suFit][value=fill]"); fillpx = px("SU.draft.url", [[100, 360], [1200, 360], [640, 20]])
            ok("choose: Fill fills the screen (no black sides; the edges cut evenly)", ed.js("SU.draft.fit") == "fill" and all(near(p, (20, 40, 200)) for p in fillpx["p"]), fillpx)
            click("input[name=suFit][value=fit]"); click("#suUse"); time.sleep(1.2); m = model(); log["chosen"] = m
            sha = (m["custom"] or {}).get("sha256", "")
            ok("choose: Use This Picture makes it the startup screen (Your picture, its name)", m["custom"] and m["choice"] == m["custom"]["id"] == "custom-" + sha[:16] and m["custom"]["name"] == "living-room-photo" and any("Your picture" in o for o in m["opts"]) and m["sel"] == m["choice"], m)
            saved = ed.js("SU.b64.get(%s)" % json.dumps(sha)) or ""
            raw = ed.js("(()=>{ const s=atob(%s); return {n:s.length, exif:s.includes('Exif'), artist:s.includes('Owner Name'), comment:s.includes('secret comment'), sha:null}; })()" % json.dumps(saved)) if saved else {}
            spx = px(json.dumps("data:image/jpeg;base64," + saved), [[640, 40], [100, 360]]) if saved else {}
            ok("choose: the saved picture is 1280x720 with the framing shown, and carries no metadata (no EXIF, artist or comment)",
               saved and spx["w"] == 1280 and spx["h"] == 720 and near(spx["p"][0], (220, 20, 20)) and near(spx["p"][1], (0, 0, 0), 12) and not raw["exif"] and not raw["artist"] and not raw["comment"] and raw["n"] <= 450_000, (raw, spx))
            st = idb(); ok("choose: the bytes are in this browser's own storage under their SHA-256", isinstance(st, list) and any(x["sha"] == sha for x in st), st)
            ls = ed.js("(()=>{ let big=0, named=false; for(let i=0;i<localStorage.length;i++){ const v=localStorage.getItem(localStorage.key(i))||''; if(v.includes(%s)) named=true; if(v.includes(%s)) big++; } return {named, big}; })()" % (json.dumps(sha), json.dumps(saved[:200])))
            ok("choose: the collection records keep only a short reference (the bytes are not in localStorage)", ls["named"] and ls["big"] == 0, ls)
            # ---- replace ----
            ok("replace: Replace Image… opens the picker; a transparent PNG is shown with black where it is transparent", pick("transparent.png") and ed.js("SU.draft&&SU.draft.fit") == "fill" and
               (lambda p: near(p["p"][0], (0, 0, 0), 12) and near(p["p"][1], (10, 200, 30)))(px("SU.draft.url", [[300, 360], [1000, 360]])), model())
            ok("replace: until it is kept the earlier picture is still the startup screen", model()["custom"]["sha256"] == sha)
            click("#suUse"); time.sleep(1.2); m2 = model(); sha2 = (m2["custom"] or {}).get("sha256", "")
            ok("replace: the new picture is the startup screen", sha2 and sha2 != sha and m2["choice"] == "custom-" + sha2[:16], m2)
            ed.js("(async()=>{ await suTx('readwrite', s=>{ const r=s.get(%s); r.onsuccess=()=>{ const v=r.result; if(v){ v.at=0; s.put(v); } }; }); await suTidy(); })()" % json.dumps(sha)); time.sleep(0.5)
            st = idb(); ok("replace: the earlier picture, named by nothing, is removed from this browser's storage (the new one stays)", isinstance(st, list) and not any(x["sha"] == sha for x in st) and any(x["sha"] == sha2 for x in st), st)
            # ---- refuse ----
            reasons = {"animated.png": "animated PNG", "animated.webp": "animated WebP", "animated.gif": "GIF", "damaged.jpg": "could not be read", "tiny.png": "only 200×100",
                       "wide.png": "13,000×20", "notes.png": "not a PNG, JPEG or WebP", "huge.jpg": "26"}
            for name, why in reasons.items():
                pick(name); m3 = model()
                ok(f"refuse: {name} is refused with its reason and nothing changes", "cannot be used" in m3["out"] and why in m3["out"] and not m3["draft"] and m3["custom"]["sha256"] == sha2, m3["out"])
            ok("refuse: the person's files are unchanged", all(hashlib.sha256(open(p, "rb").read()).hexdigest() == before[k] for k, p in F.items()))
            # ---- isolate ----
            first = ed.js("CS.active"); ed.js("closeColl(); csNew('Second Collection'); 1"); time.sleep(0.6); ed.js("openFolders(); 1"); time.sleep(0.6); m4 = model()
            ok("isolate: a second collection starts with Random and no picture of its own", m4["choice"] == "random" and not m4["custom"] and not any("Your picture" in o for o in m4["opts"]), m4)
            pick("second.png"); click("#suUse"); time.sleep(1.2); m5 = model(); sha3 = (m5["custom"] or {}).get("sha256", "")
            ok("isolate: the second collection has its own picture", sha3 and sha3 not in (sha2,) and m5["choice"] == "custom-" + sha3[:16], m5)
            ed.js("closeColl(); csSwitch(%s, true); 1" % json.dumps(first)); time.sleep(0.6); ed.js("openFolders(); 1"); time.sleep(0.6); m6 = model()
            ok("isolate: switching back shows the first collection's picture", (m6["custom"] or {}).get("sha256") == sha2 and m6["choice"] == "custom-" + sha2[:16], m6)
            # ---- reload ----
            ed.navigate(url); ed.wait_for(READY, timeout=120); ed.js(CALM); ed.js("SU.ready"); time.sleep(1.0)
            ed.call("Page.enable"); ed.call("DOM.enable"); ed.call("Page.setInterceptFileChooserDialog", {"enabled": True})
            ed.js("openFolders(); 1"); time.sleep(0.8); m7 = model()
            ok("reload: the picture is still the startup screen, shown from this browser's storage", (m7["custom"] or {}).get("sha256") == sha2 and ed.js("!!document.querySelector('#hfStartupPrev img[src^=\"data:image/jpeg\"]')"), m7)
            nh = ed.js("(()=>{ const h=nhHome(false); const c=h.startup_custom; return {choice:h.startup_image, sha:c&&c.sha256, data:!!(c&&c.data)}; })()")
            ok("carry: the console package request carries the chosen picture with its bytes", nh["choice"] == "custom-" + sha2[:16] and nh["sha"] == sha2 and nh["data"], nh)
            fsb = ed.js("(()=>{ const o=csBackupObject(); window.__fsb=o; const c=o.collections.map(x=>x.home&&x.home.startup_custom).filter(Boolean); return c.map(x=>({sha:x.sha256, data:!!x.data})); })()")
            ok("carry: a Full Studio Backup carries each collection's picture with its bytes", any(x["sha"] == sha2 and x["data"] for x in fsb) and any(x["sha"] == sha3 and x["data"] for x in fsb), fsb)
            # ---- carry: .ccs ----
            wipe = "(async()=>{ try{ (await SU.db).close(); }catch(e){} SU.db=null; SU.b64.clear(); SU.bad.clear(); await new Promise(r=>{ const q=indexedDB.deleteDatabase('ccs-startup-images'); q.onsuccess=q.onerror=q.onblocked=()=>r(); }); return true; })()"
            cc = ed.js("(async()=>{ if(typeof fbKeptSources==='function') await fbKeptSources(); const parts=await ccsCollectionParts(CS.items[CS.active]); const c=parts.file.ccs_collection.home.startup_custom; window.__ccs=await ccsBuild(parts); return {sha:c&&c.sha256, data:!!(c&&c.data), size:window.__ccs.size}; })()")
            ok("carry: the collection's .ccs file carries the picture's bytes", cc and cc.get("sha") == sha2 and cc.get("data"), cc)
            ed.js("closeColl(); 1"); ed.js(wipe); ok("carry: (this browser's picture storage emptied, as on another device)", ed.js("SU.b64.size") == 0 and idb() == [])
            imp = ed.js("(async()=>{ const info=await ccsInspect(window.__ccs); ccsSaveCollection(info); for(let i=0;i<50&&SU.pending.size;i++) await new Promise(r=>setTimeout(r,100)); return {ok:info.ok, have:SU.b64.has(%s), bad:[...SU.bad.values()]}; })()" % json.dumps(sha2))
            st = idb(); ok("carry: opened elsewhere, the .ccs file's picture is checked and kept again (the same SHA-256)", imp["have"] and not imp["bad"] and isinstance(st, list) and any(x["sha"] == sha2 for x in st), (imp, st))
            # ---- carry: Full Studio Backup ----
            ed.js(wipe)
            ed.js("csRestoreDialog(window.__fsb, 'Full Studio Backup.json'); 1"); time.sleep(0.5); click("#rsGo"); time.sleep(1.0)
            for _ in range(50):
                if not ed.js("SU.pending.size"): break
                time.sleep(0.1)
            st = idb(); ok("carry: restoring the Full Studio Backup checks and keeps both pictures again", isinstance(st, list) and {sha2, sha3} <= {x["sha"] for x in st}, st)
            ed.js("closeColl(); 1"); time.sleep(0.3)
            # ---- carry: a damaged picture ----
            ed.js(wipe)
            dmg = ed.js("(()=>{ const o=JSON.parse(JSON.stringify(window.__fsb)); o.collections.forEach(x=>{ const c=x.home&&x.home.startup_custom; if(c&&c.data){ const s=atob(c.data).split(''); s[2000]=String.fromCharCode(s[2000].charCodeAt(0)^255); c.data=btoa(s.join('')); } }); window.__bad=o; return true; })()")
            ed.js("csRestoreDialog(window.__bad, 'Damaged Backup.json'); 1"); time.sleep(0.5); click("#rsGo"); time.sleep(1.0)
            for _ in range(50):
                if not ed.js("SU.pending.size"): break
                time.sleep(0.1)
            ed.js("closeColl(); 1"); time.sleep(0.3)
            ed.js("openFolders(); 1"); time.sleep(0.6); m8 = model(); msg = ed.js("(document.querySelector('#hfStartupPrev .msgbox')||{}).textContent||''")
            nh2 = ed.js("(()=>{ const h=nhHome(false); const c=h.startup_custom; return {choice:h.startup_image, data:!!(c&&c.data)}; })()")
            ok("carry: a damaged picture in a backup is refused with its reason; HOME Settings says so; it does not turn into Random; no picture is sent for the console package",
               "not the picture the backup names" in ed.js("[...SU.bad.values()].join(' ')") and "cannot be used" in msg and m8["choice"] == "custom-" + sha2[:16] and nh2["choice"] == m8["choice"] and not nh2["data"], (msg, m8["choice"], nh2))
            ed.js("closeColl(); 1"); ed.js(wipe); ed.js("csRestoreDialog(window.__fsb, 'Full Studio Backup.json'); 1"); time.sleep(0.5); click("#rsGo"); time.sleep(1.5); ed.js("closeColl(); 1")
            ok("carry: restoring the good backup again makes the picture usable again", ed.js("SU.b64.has(%s)" % json.dumps(sha2)))
            far = ed.js("performance.getEntriesByType('resource').filter(e=>e.startTime>%f&&!e.name.startsWith(location.origin)&&!e.name.startsWith('data:')&&!e.name.startsWith('blob:')).map(e=>e.name).slice(0,5)" % t0)
            ok("no request left this computer while pictures were chosen, checked, saved and carried", far == [], far)
            ok("no page errors", not ed.exceptions, ed.exceptions[:2])
    finally:
        srv.shutdown()
    fails = [n for n, c in res if not c]
    print(f"\n{len(res) - len(fails)}/{len(res)} PASS" + (f"; FAIL: {', '.join(fails)}" if fails else ""))
    if a.out: json.dump({"results": [{"name": n, "pass": c} for n, c in res], "log": log}, open(a.out, "w", encoding="utf-8"), indent=1, default=str)
    return 0 if not fails else 1

if __name__ == "__main__": sys.exit(main())
