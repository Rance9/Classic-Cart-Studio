r"""Import Game Saves… in a browser keeps every save it replaces (Codex v1p4 F1), on the built website (headless Edge) with the Windows app's
own module (tools/companion/saves_transfer.py, throwaway folders) making and reading the files:
  replace        an occupied Slot 1 replaced by explicit choice: the original's exact bytes, its record time and the slot names as they were
                 are kept (replaced-<id>/), in the same transaction as the replacement; an unnamed slot likewise; a repeated replacement
                 keeps each original apart; a replaced cartridge save is kept and still set aside as before
  all or nothing a save changed after the choices were read, a recovery copy that cannot be added (an existing record at its place: never
                 written over) and a failed write each leave the saves and the copies exactly as they were, with a plain reason
  recovery       after a reload, Replaced Game Saves… lists each import's originals (slot, name, save time); Export These Saves… gives an
                 ordinary Game Saves file the Windows app reads; importing it puts the original back into an empty slot with its name
  untouched      default Keep; the backups' own list of saved games (fbWebSaves) does not take the copies for saves
  python test_replaced_saves_web.py <built site folder> [--out results.json] [--shots DIR]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, base64, hashlib, json, os, sys, tempfile, threading, time, types, zipfile, zlib
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE); CO = os.path.normpath(os.path.join(HERE, "..", "companion")); sys.path.insert(0, CO)
PLAY = types.ModuleType("play"); PLAY.RUN = types.SimpleNamespace(lock=threading.RLock()); PLAY._running = lambda: False; sys.modules["play"] = PLAY
import saves_transfer as ST
from studio_harness import EdgeSession, READY, CALM, serve

def state(sha, payload, created, platform="windows-x86_64"):
    h = json.dumps({"schema": "ccs-save-state/1", "kind": "manual", "rom_content_sha256": sha, "title": "Thwaite", "core": "fceumm", "core_version": "ac0d00a7", "platform": platform,
                    "size": len(payload), "crc32": f"{zlib.crc32(payload) & 0xFFFFFFFF:08x}", "frames": 1, "created": created}).encode()
    return b"CCSSTATE" + len(h).to_bytes(4, "little") + h + payload
def names(sha, n): return json.dumps({"schema": "ccs-slot-names/1", "rom_content_sha256": sha, "names": n}).encode()
def sha(b): return hashlib.sha256(b).hexdigest()

def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); ap.add_argument("--shots", default=""); a = ap.parse_args()
    srv = serve(a.site); base = f"http://127.0.0.1:{srv.server_address[1]}"; res = []; tmp = tempfile.mkdtemp(prefix="ccs-replaced-")
    def ok(n, c, d=""):
        res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:400]})" if d and not c else ""), flush=True)
    A = json.load(open(os.path.join(a.site, "included", "index.json"), encoding="utf-8"))["games"][0]["content_sha256"]
    class Data:
        def __init__(self, n): self.root = os.path.join(tmp, n); os.makedirs(self.root)
        def __call__(self, *p): return os.path.join(self.root, *p)
        def put(self, s, name, b): os.makedirs(self("saves", s), exist_ok=True); open(self("saves", s, name), "wb").write(b)
    def make(n, files):   # a Game Saves file made by the Windows app's own exporter
        d = Data(n)
        for name, b in files.items(): d.put(A, name, b)
        f = os.path.join(tmp, n + ".zip"); r = ST.export(d, f, None, "test"); assert r["ok"], r; return open(f, "rb").read()
    orig = state(A, b"ORIGINAL PROGRESS " * 40, "2026-11-13T20:00:00Z", "web-wasm32"); orig_names = names(A, {"1": "My original checkpoint"})
    inc1 = state(A, b"IMPORTED PROGRESS " * 40, "2026-11-10T12:00:00Z"); inc2 = state(A, b"SECOND IMPORT     " * 40, "2026-11-11T12:00:00Z")
    o2 = state(A, b"UNNAMED ORIGINAL  " * 40, "2026-11-09T08:00:00Z", "web-wasm32"); i2 = state(A, b"UNNAMED INCOMING  " * 40, "2026-11-12T08:00:00Z")
    ocart = b"\xC3" * 8192; icart = b"\x3C" * 8192
    f1 = make("incoming1", {"slot1.ccsstate": inc1, "slot-names.json": names(A, {"1": "Incoming checkpoint"})})
    f2 = make("incoming2", {"slot1.ccsstate": inc2, "slot2.ccsstate": i2, "cart.sav": icart})
    f3 = make("incoming3", {"slot1.ccsstate": state(A, b"THIRD IMPORT      " * 40, "2026-11-12T12:00:00Z")})
    try:
        with EdgeSession(profile=tempfile.mkdtemp(prefix="ccs-replaced-p-"), width=1440, height=900) as ed:
            def boot():
                ed.navigate(base + "/index.html"); ed.wait_for(READY, timeout=120); ed.js(CALM); ed.wait_for("CS.starter&&CS.starter.state==='done'", timeout=90); time.sleep(0.6)
                ed.js("""(()=>{ window.ccsB64=async b=>{ const u8=new Uint8Array(await b.arrayBuffer()); let s=''; for(let i=0;i<u8.length;i+=0x8000) s+=String.fromCharCode.apply(null,u8.subarray(i,i+0x8000)); return btoa(s); }; return 1; })()""")
            boot()
            def wput(path, b, at="2026-11-13T20:00:00Z"):
                ed.js("window.__w=0; cpTx(['files'],'readwrite',t=>t.objectStore('files').put({path:%s, bytes:Uint8Array.from(atob('%s'),c=>c.charCodeAt(0)).buffer, at:%s})).then(()=>window.__w=1); 1" % (json.dumps(path), base64.b64encode(b).decode(), json.dumps(at))); ed.wait_for("window.__w===1", timeout=20)
            def wdel(path):
                ed.js("window.__w=0; cpTx(['files'],'readwrite',t=>t.objectStore('files').delete(%s)).then(()=>window.__w=1); 1" % json.dumps(path)); ed.wait_for("window.__w===1", timeout=20)
            def rows():
                return ed.js("cpTx(['files'],'readonly',t=>cpReq(t.objectStore('files').getAll(IDBKeyRange.bound('/data/saves/','/data/saves/\\uffff')))).then(async rs=>{ const o=[]; for(const r of rs) o.push({path:r.path, at:r.at||'', replaced_at:r.replaced_at||'', b64:await ccsB64(new Blob([r.bytes]))}); return o; })")
            def files(): return {r["path"]: dict(r, bytes=base64.b64decode(r["b64"])) for r in rows()}
            def here(name): return (files().get(f"/data/saves/{A}/{name}") or {}).get("bytes")
            def kept(): return {p: r for p, r in files().items() if "/replaced-" in p}
            def dlg(): return ed.js("$('collm').classList.contains('open')?{title:$('collTitle').textContent, text:$('collBody').innerText, html:$('collBody').innerHTML}:null") or {}
            def pick(b, n): ed.js("window.__files=[new File([Uint8Array.from(atob('%s'), c=>c.charCodeAt(0))], %s)]; cvPick=async()=>window.__files; 1" % (base64.b64encode(b).decode(), json.dumps(n)))
            sel = lambda f: f"document.querySelector('select[data-gx-sha=\"{A}\"][data-gx-file=\"{f}\"]')"
            def run_import(b, choices, wait_title=("Game Saves Imported", "Saves Not Imported")):
                pick(b, "saves.zip"); ed.js("window.__i=0; gxImport().then(()=>window.__i=1); 1"); ed.wait_for("window.__i===1&&!!$('gxGo')", timeout=30)
                pre = dlg()
                for f, v in choices.items(): ed.js(sel(f) + ".value=%s; 1" % json.dumps(v))
                ed.js("$('gxGo').click(); 1"); ed.wait_for(" || ".join(f"$('collTitle').textContent==={json.dumps(t)}" for t in wait_title), timeout=30); time.sleep(0.2); return pre, dlg()
            # ---------------- replace an occupied, named slot ----------------
            wput(f"/data/saves/{A}/slot1.ccsstate", orig); wput(f"/data/saves/{A}/slot-names.json", orig_names, "2026-11-13T20:00:01Z")
            pre, d = run_import(f1, {"slot1.ccsstate": "replace"})
            ok("the choice says the save here is kept", "(it is kept, not deleted)" in pre.get("html", ""), pre.get("text", "")[-300:])
            k = kept(); ks = {p.rsplit("/", 1)[-1]: r for p, r in k.items()}; tx1 = {p.split("/")[4] for p in k}
            ok("replace: Slot 1 holds the imported save and its name", here("slot1.ccsstate") == inc1 and json.loads(here("slot-names.json"))["names"] == {"1": "Incoming checkpoint"})
            ok("replace: the original save is kept byte for byte, with its record's own time", ks.get("slot1.ccsstate", {}).get("bytes") == orig and ks["slot1.ccsstate"]["at"] == "2026-11-13T20:00:00Z" and ks["slot1.ccsstate"]["replaced_at"], {p: (r["at"], len(r["bytes"])) for p, r in k.items()})
            ok("replace: the slot names as they were are kept beside it (the original's name)", ks.get("slot-names.json", {}).get("bytes") == orig_names and ks["slot-names.json"]["at"] == "2026-11-13T20:00:01Z")
            ok("replace: one import, one recovery place (replaced-<date>-<time>-<random>)", len(tx1) == 1 and all(len(t) == len("replaced-20261114-050746-c5785943") for t in tx1), tx1)
            ok("Codex probe check: the original state and names exist in browser storage", any(r["bytes"] == orig for r in files().values()) and any(r["bytes"] == orig_names for r in files().values()))
            ok("the result says the replaced save is kept and offers Export Replaced Saves…", "kept in this browser" in d.get("text", "") and "Export Replaced Saves" in d.get("text", "") and "Replaced Game Saves" in d.get("text", ""), d)
            if a.shots: os.makedirs(a.shots, exist_ok=True); open(os.path.join(a.shots, "replaced-result-1440.png"), "wb").write(ed.screenshot())
            # ---------------- an unnamed slot, a repeated replacement, the cartridge save ----------------
            wput(f"/data/saves/{A}/slot2.ccsstate", o2, "2026-11-09T08:00:01Z"); wput(f"/data/saves/{A}/cart.sav", ocart, "2026-11-09T08:00:02Z")
            pre, d = run_import(f2, {"slot1.ccsstate": "replace", "slot2.ccsstate": "replace", "cart.sav": "replace"})
            k2 = {p: r for p, r in kept().items() if p.split("/")[4] not in tx1}; tx2 = {p.split("/")[4] for p in k2}; ks2 = {p.rsplit("/", 1)[-1]: r for p, r in k2.items()}
            ok("repeated replacement: a second recovery place, the first untouched", len(tx2) == 1 and {p.rsplit("/", 1)[-1]: r["bytes"] for p, r in kept().items() if p.split("/")[4] in tx1} == {"slot1.ccsstate": orig, "slot-names.json": orig_names}, sorted(kept()))
            ok("repeated replacement: the save it replaced this time (the earlier import) is kept too", ks2.get("slot1.ccsstate", {}).get("bytes") == inc1 and here("slot1.ccsstate") == inc2)
            ok("unnamed slot: the original is kept with its time; the names kept say it had none", ks2.get("slot2.ccsstate", {}).get("bytes") == o2 and ks2["slot2.ccsstate"]["at"] == "2026-11-09T08:00:01Z" and "2" not in json.loads(ks2["slot-names.json"]["bytes"])["names"] and here("slot2.ccsstate") == i2)
            aside = [r for p, r in files().items() if p.startswith(f"/data/saves/{A}/cart-set-aside-")]
            ok("cartridge save: replaced, the original kept with the others and still set aside as before", here("cart.sav") == icart and ks2.get("cart.sav", {}).get("bytes") == ocart and len(aside) == 1 and aside[0]["bytes"] == ocart)
            ok("the result counts the saves kept", "The 3 saves this import replaced are kept" in d.get("text", ""), d.get("text", ""))
            # ---------------- all or nothing ----------------
            before = files()
            ed.js("window.__gw=gxWrite; gxWrite=async todo=>{ await cpTx(['files'],'readwrite',t=>t.objectStore('files').put({path:'/data/saves/%s/slot1.ccsstate', bytes:new Uint8Array([1,2,3]).buffer, at:'x'})); return window.__gw(todo); }; 1" % A)
            pre, d = run_import(f3, {"slot1.ccsstate": "replace"}); ed.js("gxWrite=window.__gw; 1")
            after = files(); changed = {p for p in set(before) | set(after) if (before.get(p) or {}).get("bytes") != (after.get(p) or {}).get("bytes")}
            ok("a save changed after the choices were read: nothing imported, nothing kept, the plain reason", d.get("title") == "Saves Not Imported" and "changed while the import was being prepared" in d.get("text", "") and changed == {f"/data/saves/{A}/slot1.ccsstate"} and here("slot1.ccsstate") == bytes([1, 2, 3]), (d, changed))
            wput(f"/data/saves/{A}/slot1.ccsstate", inc2, "2026-11-14T01:00:00Z")
            fixed = "20261114-120000-0badc0de"; wput(f"/data/saves/{A}/replaced-{fixed}/slot1.ccsstate", b"an earlier copy", "2026-11-14T00:00:00Z")
            before = files(); ed.js("window.__tx=gxTxId; gxTxId=()=>%s; 1" % json.dumps(fixed))
            pre, d = run_import(f3, {"slot1.ccsstate": "replace"}); ed.js("gxTxId=window.__tx; 1"); after = files()
            ok("a recovery copy that cannot be added (its place is taken): nothing changed, the copy there is not written over", d.get("title") == "Saves Not Imported" and "could not be kept" in d.get("text", "") and after.keys() == before.keys() and all(after[p]["bytes"] == before[p]["bytes"] for p in after) and after[f"/data/saves/{A}/replaced-{fixed}/slot1.ccsstate"]["bytes"] == b"an earlier copy", d)
            wdel(f"/data/saves/{A}/replaced-{fixed}/slot1.ccsstate")
            before = files()
            ed.js("window.__put=IDBObjectStore.prototype.put; IDBObjectStore.prototype.put=function(v){ if(v&&/slot1\\.ccsstate$/.test(v.path)&&!/replaced-/.test(v.path)) { this.transaction.abort(); return {}; } return window.__put.apply(this, arguments); }; 1")
            pre, d = run_import(f3, {"slot1.ccsstate": "replace"}); ed.js("IDBObjectStore.prototype.put=window.__put; 1"); after = files()
            ok("a write that fails: nothing changed, no copy left behind", d.get("title") == "Saves Not Imported" and "Nothing was changed" in d.get("text", "") and after.keys() == before.keys() and all(after[p]["bytes"] == before[p]["bytes"] for p in after), (d, sorted(set(after) ^ set(before))))
            ok("default Keep: an import with no choice changes nothing", (lambda r: r[1].get("title") == "Game Saves Imported" and here("slot1.ccsstate") == inc2)(run_import(f3, {})))
            fb = ed.js("fbWebSaves().then(g=>g.flatMap(x=>x.files.map(f=>f.name)))")
            ok("the backups' list of saved games does not take the kept copies for saves", fb and not [n for n in fb if "replaced" in n or "/" in n], fb)
            # ---------------- after a reload: list, export, put back ----------------
            boot()
            ed.js("window.__k=0; gxKeptOpen().then(()=>window.__k=1); 1"); ed.wait_for("window.__k===1", timeout=30); d = dlg()
            ok("after a reload, Replaced Game Saves… lists each import's originals with slot, name and save time",
               d.get("title") == "Replaced Game Saves" and d["text"].count("Export These Saves") == 2 and 'Slot 1 "My original checkpoint"' in d["text"] and "saved Nov 13, 2026" in d["text"] and "Cartridge save" in d["text"], d.get("text", "")[:800])
            if a.shots: open(os.path.join(a.shots, "replaced-list-1440.png"), "wb").write(ed.screenshot())
            ed.js("window.__saved=null; ccsSaveFile=async(n,b)=>{ window.__saved={name:n, blob:b}; return {ok:true, name:n}; }; 1")
            btn = ed.js("[...document.querySelectorAll('#collBody [data-gx-kept]')].map(b=>b.dataset.gxKept)"); first = sorted(btn)[0]
            ed.js("document.querySelector('#collBody [data-gx-kept=%s]').click(); 1" % json.dumps(first)); ed.wait_for("window.__saved&&$('collTitle').textContent==='Replaced Saves Exported'", timeout=30)
            zb = base64.b64decode(ed.js("ccsB64(window.__saved.blob)")); zp = os.path.join(tmp, "replaced.zip"); open(zp, "wb").write(zb); name = ed.js("window.__saved.name")
            rd = ST.read(zp); man = rd["manifest"]; g = man["games"][0]; fs = {f["name"]: f for f in g["files"]}
            ok("Export These Saves…: an ordinary Game Saves file the Windows app reads, named for the replacement", name.startswith("Classic Cart Studio Replaced Saves ") and name.endswith(".zip") and man["schema"] == "ccs-game-saves/1" and "replaced in this browser" in man["note"], (name, man.get("note")))
            ok("Export These Saves…: the original save, its name, save time and record time, no ROM",
               rd["members"][f"saves/{A}/slot1.ccsstate"] == orig and fs["slot1.ccsstate"]["slot_name"] == "My original checkpoint" and fs["slot1.ccsstate"]["saved_at"] == "2026-11-13T20:00:00Z" and fs["slot1.ccsstate"]["modified"] == "2026-11-13T20:00:00Z"
               and rd["members"][f"saves/{A}/slot-names.json"] == orig_names and not [m for m in rd["members"] if not ST.MEMBER.match(m)], fs)
            app = Data("app"); p = ST.plan(app, zp); ok("the Windows app plans it like any Game Saves file", not p["rejected"] and p["games"][0]["files"][0]["slot_name"] == "My original checkpoint", p)
            pre, d = run_import(zb, {"slot1.ccsstate": "slot3"})
            ok("Import Game Saves… puts the original back into an empty slot with its name", d.get("title") == "Game Saves Imported" and here("slot3.ccsstate") == orig and json.loads(here("slot-names.json"))["names"].get("3") == "My original checkpoint" and here("slot1.ccsstate") == inc2, d)
            ok("no page errors", not ed.exceptions and not ed.console_errors, (ed.exceptions, ed.console_errors))
    finally:
        srv.shutdown(); srv.server_close()
    n = sum(1 for _, c in res if c); print(f"RESULT {n} / {len(res)}")
    if a.out: json.dump([{"check": k, "pass": c} for k, c in res], open(a.out, "w", encoding="utf-8"), indent=1)
    return 0 if res and n == len(res) else 1

if __name__ == "__main__": sys.exit(main())
