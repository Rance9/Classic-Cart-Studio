r"""Export Game Saves… / Import Game Saves… between the website and the Windows app, on the built website (headless Edge) and the
Windows app's own module (tools/companion/saves_transfer.py, in a throwaway data folder):
  browser → app   the browser's export (named slots, cartridge save, last used) is read by the Windows app's reader and imported there;
                  the files arrive byte for byte; no ROM, art or settings is in the file
  app → browser   the Windows app's export is imported in the browser: new saves to their slots with their names; a different save in
                  an occupied slot is kept unless chosen; a save put into an empty slot keeps its name; a replaced cartridge save is set
                  aside, not lost; a game whose ROM file is not here keeps its saves, joins no collection, and the dialog says how to
                  get it (an Included Game: from the Included Games)
  refusals        a changed member, a save of another game: refused before anything is written; an open game and another tab's game
                  (the player's lock) refuse the import; nothing is written
  python test_game_saves_web.py <built site folder> [--out results.json] [--shots DIR]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, base64, hashlib, io, json, os, shutil, sys, tempfile, threading, time, types, zipfile, zlib
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE); CO = os.path.normpath(os.path.join(HERE, "..", "companion")); sys.path.insert(0, CO)
PLAY = types.ModuleType("play"); PLAY.RUN = types.SimpleNamespace(lock=threading.RLock()); PLAY._running = lambda: False; sys.modules["play"] = PLAY
import saves_transfer as ST
from studio_harness import EdgeSession, READY, CALM, serve

def state(sha, payload, platform, created):
    h = json.dumps({"schema": "ccs-save-state/1", "kind": "manual", "rom_content_sha256": sha, "title": "T", "core": "fceumm", "core_version": "ac0d00a7", "platform": platform,
                    "size": len(payload), "crc32": f"{zlib.crc32(payload) & 0xFFFFFFFF:08x}", "frames": 1, "created": created}).encode()
    return b"CCSSTATE" + len(h).to_bytes(4, "little") + h + payload
def names(sha, n): return json.dumps({"schema": "ccs-slot-names/1", "rom_content_sha256": sha, "names": n}).encode()

def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")   # check names carry arrows and dashes; any console prints them
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); ap.add_argument("--shots", default=""); a = ap.parse_args()
    srv = serve(a.site); base = f"http://127.0.0.1:{srv.server_address[1]}"; res = []; tmp = tempfile.mkdtemp(prefix="ccs-gsaves-")
    def ok(n, c, d=""):
        res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:400]})" if d and not c else ""), flush=True)
    th = json.load(open(os.path.join(a.site, "included", "index.json"), encoding="utf-8"))["games"][0]; A = th["content_sha256"]; B = hashlib.sha256(b"a game not here").hexdigest()
    class Data:
        def __init__(self, n): self.root = os.path.join(tmp, n); os.makedirs(self.root)
        def __call__(self, *p): return os.path.join(self.root, *p)
        def put(self, sha, name, b): os.makedirs(self("saves", sha), exist_ok=True); open(self("saves", sha, name), "wb").write(b)
        def get(self, sha, name):
            p = self("saves", sha, name); return open(p, "rb").read() if os.path.isfile(p) else None
    try:
        with EdgeSession(profile=tempfile.mkdtemp(prefix="ccs-gsaves-p-"), width=1440, height=900) as ed:
            ed.navigate(base + "/index.html"); ed.wait_for(READY, timeout=120); ed.js(CALM); ed.wait_for("CS.starter&&CS.starter.state==='done'", timeout=90); time.sleep(0.8)
            def wput(path, b): ed.js("cpTx(['files'],'readwrite',t=>t.objectStore('files').put({path:%s, bytes:Uint8Array.from(atob('%s'),c=>c.charCodeAt(0)).buffer, at:'2026-11-12T08:00:00Z'})).then(()=>window.__w=1); 1" % (json.dumps(path), base64.b64encode(b).decode())); ed.wait_for("window.__w===1", timeout=20); ed.js("window.__w=0; 1")
            def wget(path):
                v = ed.js("cpTx(['files'],'readonly',t=>cpReq(t.objectStore('files').get(%s))).then(r=>r?ccsB64(new Blob([r.bytes])):null)" % json.dumps(path)); return base64.b64decode(v) if v else None
            ed.js("""(()=>{ window.ccsB64=async b=>{ const u8=new Uint8Array(await b.arrayBuffer()); let s=''; for(let i=0;i<u8.length;i+=0x8000) s+=String.fromCharCode.apply(null,u8.subarray(i,i+0x8000)); return btoa(s); }; return 1; })()""")
            def dlg(): return ed.js("$('collm').classList.contains('open')?{title:$('collTitle').textContent, text:$('collBody').innerText}:null") or {}
            # ---------------- browser → app ----------------
            s1 = state(A, b"\x11" * 300, "web-wasm32", "2026-11-10T09:00:00Z"); s2 = state(A, b"\x22" * 300, "web-wasm32", "2026-11-11T09:00:00Z"); cart = b"\xA5" * 8192
            for n, b in (("slot1.ccsstate", s1), ("slot2.ccsstate", s2), ("cart.sav", cart), ("slot-names.json", names(A, {"1": "Castle", "2": "Boss"})),
                         ("last-used.json", json.dumps({"schema": "ccs-last-used/1", "rom_content_sha256": A, "slot": 2}).encode()), ("game.json", json.dumps({"schema": "ccs-save-game/1", "rom_content_sha256": A, "last_title": "Thwaite"}).encode())):
                wput(f"/data/saves/{A}/{n}", b)
            ed.js("window.__saved=null; ccsSaveFile=async(n,b)=>{ window.__saved={name:n, blob:b}; return {ok:true, name:n}; }; window.__x=0; gxExport().then(()=>window.__x=1); 1"); ed.wait_for("window.__x===1", timeout=30)
            d = dlg(); name = ed.js("window.__saved&&window.__saved.name"); data = base64.b64decode(ed.js("ccsB64(window.__saved.blob)"))
            ok("browser export: one file of the saved games, with the plain summary", name and name.startswith("Classic Cart Studio Game Saves ") and name.endswith(".zip") and "No game files are in it" in d.get("text", ""), (name, d))
            fp = os.path.join(tmp, "from-browser.zip"); open(fp, "wb").write(data)
            with zipfile.ZipFile(fp) as z: members = z.namelist(); man = json.loads(z.read(ST.MANIFEST))
            ok("browser export: only the manifest and saves/<game>/<save file>; no ROM, art or settings", members[0] == ST.MANIFEST and all(ST.MEMBER.match(m) for m in members[1:]) and len(members) == 7, members)
            g = man["games"][0]; s1d = next(f for f in g["files"] if f["name"] == "slot1.ccsstate")
            ok("browser export: the manifest names each slot and when it was saved (from the save itself)", s1d["slot_name"] == "Castle" and s1d["saved_at"] == "2026-11-10T09:00:00Z" and man["platform"] == "web-wasm32", s1d)
            app = Data("app"); p = ST.plan(app, fp)
            ok("app reads the browser's file: every save usable, nothing rejected", not p["rejected"] and {f["name"]: f["status"] for f in p["games"][0]["files"]} == {"cart.sav": "add", "slot1.ccsstate": "add", "slot2.ccsstate": "add"}, p)
            r = ST.apply(app, fp, p["token"], {}); ok("app imports it", r.get("ok") is True, r)
            ok("browser → app: each file arrives byte for byte (slots, names, cartridge, last used)", app.get(A, "slot1.ccsstate") == s1 and app.get(A, "slot2.ccsstate") == s2 and app.get(A, "cart.sav") == cart and json.loads(app.get(A, "slot-names.json"))["names"] == {"1": "Castle", "2": "Boss"} and app.get(A, "last-used.json") is not None)
            # ---------------- app → browser ----------------
            w1 = state(A, b"\x33" * 300, "windows-x86_64", "2026-11-12T20:00:00Z"); w3 = state(A, b"\x44" * 300, "windows-x86_64", "2026-11-12T21:00:00Z"); wcart = b"\x5A" * 8192; wb = state(B, b"\x55" * 300, "windows-x86_64", "2026-11-12T22:00:00Z")
            src = Data("app2"); src.put(A, "slot1.ccsstate", w1); src.put(A, "slot3.ccsstate", w3); src.put(A, "cart.sav", wcart); src.put(A, "slot-names.json", names(A, {"1": "Laptop", "3": "Train"}))
            src.put(B, "slot1.ccsstate", wb); src.put(B, "game.json", json.dumps({"schema": "ccs-save-game/1", "rom_content_sha256": B, "last_title": "Not Here"}).encode())
            fw = os.path.join(tmp, "from-app.zip"); r = ST.export(src, fw, None, "test"); ok("app export: written and read back", r.get("ok") is True and r["games"] == 2, r)
            fwb = open(fw, "rb").read()
            def pick(b, n): ed.js("window.__files=[new File([Uint8Array.from(atob('%s'), c=>c.charCodeAt(0))], %s)]; cvPick=async()=>window.__files; 1" % (base64.b64encode(b).decode(), json.dumps(n)))
            pick(fwb, "Classic Cart Studio Game Saves.zip"); ed.js("window.__i=0; gxImport().then(()=>window.__i=1); 1"); ed.wait_for("window.__i===1", timeout=30); time.sleep(0.3); d = dlg()
            tx = d.get("text", "").lower(); ok("app → browser: the choices list both games (by name) and their saves", d.get("title") == "Import Game Saves" and "slot 1" in tx and "laptop" in tx and "not here" in tx and "thwaite" in tx, d)
            ok("app → browser: the game whose ROM is not here keeps its saves, joins nothing, and says how to get it", "is not in this browser" in d.get("text", "") and "not added to any collection" in d.get("text", ""), d)
            sel = lambda f, sha=A: f"document.querySelector('select[data-gx-sha=\"{sha}\"][data-gx-file=\"{f}\"]')"
            ok("app → browser: a different save in an occupied slot defaults to keeping the one here", ed.js(sel("slot1.ccsstate") + ".value") == "keep" and ed.js(sel("cart.sav") + ".value") == "keep")
            ok("app → browser: a save for an empty slot defaults to its own slot", ed.js(sel("slot3.ccsstate") + ".value") == "slot3")
            if a.shots: os.makedirs(a.shots, exist_ok=True); open(os.path.join(a.shots, "saves-choices-1440.png"), "wb").write(ed.screenshot())
            ed.js(sel("slot1.ccsstate") + ".value='slot4'; " + sel("cart.sav") + ".value='replace'; 1")
            ed.js("window.__g=0; const b=$('gxGo'); b.click(); 1"); time.sleep(2.0); d = dlg()
            ok("app → browser: imported", d.get("title") == "Game Saves Imported", d)
            ok("app → browser: the occupied slot is untouched; the chosen empty slot holds the imported save", wget(f"/data/saves/{A}/slot1.ccsstate") == s1 and wget(f"/data/saves/{A}/slot4.ccsstate") == w1 and wget(f"/data/saves/{A}/slot3.ccsstate") == w3)
            nm = json.loads(wget(f"/data/saves/{A}/slot-names.json")); ok("app → browser: the names travel with the saves to their slots", nm["names"] == {"1": "Castle", "2": "Boss", "3": "Train", "4": "Laptop"}, nm)
            aside = ed.js("cpTx(['files'],'readonly',t=>cpReq(t.objectStore('files').getAllKeys())).then(k=>k.filter(p=>/cart-set-aside-/.test(p)))")
            ok("app → browser: the replaced cartridge save is set aside, not lost", wget(f"/data/saves/{A}/cart.sav") == wcart and len(aside) == 1 and wget(aside[0]) == cart, aside)
            ok("app → browser: the absent game's save is kept; no collection holds it", wget(f"/data/saves/{B}/slot1.ccsstate") == wb and ed.js("csList().some(c=>(c.sel||[]).includes('p_'+%s))" % json.dumps(B[:16])) is False)
            # ---------------- refusals ----------------
            def zip_with(mut):
                zb = io.BytesIO()
                with zipfile.ZipFile(io.BytesIO(fwb)) as z, zipfile.ZipFile(zb, "w") as o:
                    for n in z.namelist(): o.writestr(n, mut(n, z.read(n)))
                return zb.getvalue()
            before = ed.js("cpTx(['files'],'readonly',t=>cpReq(t.objectStore('files').count()))")
            pick(zip_with(lambda n, b: b"\x00" * len(b) if n.endswith("cart.sav") else b), "changed.zip"); ed.js("window.__i=0; gxImport().then(()=>window.__i=1); 1"); ed.wait_for("window.__i===1", timeout=30); d = dlg()
            ok("refused: a changed member (before anything is written)", "does not match its check value" in d.get("text", "") and ed.js("cpTx(['files'],'readonly',t=>cpReq(t.objectStore('files').count()))") == before, d)
            other = state(B, b"\x66" * 300, "windows-x86_64", "2026-11-12T22:00:00Z")
            mixed = zip_with(lambda n, b: other if n == f"saves/{A}/slot3.ccsstate" else b)
            mz = io.BytesIO()
            with zipfile.ZipFile(io.BytesIO(mixed)) as z, zipfile.ZipFile(mz, "w") as o:
                m = json.loads(z.read(ST.MANIFEST))
                for gg in m["games"]:
                    for f in gg["files"]:
                        if gg["content_sha256"] == A and f["name"] == "slot3.ccsstate": f["bytes"] = len(other); f["sha256"] = hashlib.sha256(other).hexdigest()
                for n in z.namelist(): o.writestr(n, json.dumps(m) if n == ST.MANIFEST else z.read(n))
            pick(mz.getvalue(), "mixed.zip"); ed.js("window.__i=0; gxImport().then(()=>window.__i=1); 1"); ed.wait_for("window.__i===1", timeout=30); d = dlg()
            ok("refused: a save of another game is listed as not usable", "belongs to a different game file" in d.get("text", ""), d); ed.js("closeColl(); 1")
            # a game open in another tab of this browser (the player's lock held elsewhere), then a game open here
            ed.js("navigator.locks.request(CP_LOCK, ()=>new Promise(r=>{ window.__rel=r; })); 1"); time.sleep(0.3)
            pick(fwb, "again.zip"); ed.js("window.__i=0; gxImport().then(()=>window.__i=1); 1"); ed.wait_for("window.__i===1", timeout=30); time.sleep(0.2)
            ed.js(sel("slot3.ccsstate") + ".value='replace'; $('gxGo').click(); 1"); time.sleep(1.5); d = dlg()
            ok("refused: a game open in another tab (the player's lock); nothing written", d.get("title") == "Saves Not Imported" and "another tab" in d.get("text", "") and ed.js("cpTx(['files'],'readonly',t=>cpReq(t.objectStore('files').count()))") == before, d)
            ed.js("window.__rel(); closeColl(); 1"); time.sleep(0.3)
            ed.js("window.__run=CCSPLAY.running; CCSPLAY.running=()=>true; 1"); pick(fwb, "again.zip"); ed.js("window.__i=0; gxImport().then(()=>window.__i=1); 1"); ed.wait_for("window.__i===1", timeout=30); time.sleep(0.2)
            ed.js("$('gxGo').click(); 1"); time.sleep(1.2); d = dlg(); ed.js("CCSPLAY.running=window.__run; closeColl(); 1")
            ok("refused: a game open here; nothing written", d.get("title") == "Saves Not Imported" and "A game is open" in d.get("text", ""), d)
            # saves of an Included Game whose build is not here: the result offers to get exactly that build (nothing joins a collection)
            ed.js("cpTx(['roms'],'readwrite',t=>t.objectStore('roms').delete(%s)).then(()=>{ CCSROMS._have=null; window.__dr=1; }); 1" % json.dumps(A)); ed.wait_for("window.__dr===1", timeout=20)
            pick(fwb, "again.zip"); ed.js("window.__i=0; gxImport().then(()=>window.__i=1); 1"); ed.wait_for("window.__i===1", timeout=30); time.sleep(0.3)
            ed.js("$('gxGo').click(); 1"); ed.wait_for("!!document.querySelector('[data-gx-get]')", timeout=20); d = dlg()
            ok("an Included Game's saves without its build: the result offers Get Thwaite", "Get Thwaite" in d.get("text", ""), d)
            ed.js("document.querySelector('[data-gx-get]').click(); 1"); ed.wait_for("CCSROMS.has(%s)" % json.dumps(A), timeout=30)
            ok("Get Thwaite gets and keeps the exact approved build; no collection changes", ed.js("CCSROMS.has(%s)" % json.dumps(A)) is True)
            ok("no page errors", not ed.exceptions, ed.exceptions[-3:])
    finally:
        srv.shutdown(); srv.server_close(); shutil.rmtree(tmp, ignore_errors=True)
    n = sum(1 for _, c in res if c); print(f"{n}/{len(res)} PASS" if n == len(res) else f"FAILED: {len(res) - n} of {len(res)}", flush=True)
    if a.out: json.dump([{"check": k, "pass": v} for k, v in res], open(a.out, "w", encoding="utf-8"), indent=1)
    return 0 if n == len(res) else 1

if __name__ == "__main__": sys.exit(main())
