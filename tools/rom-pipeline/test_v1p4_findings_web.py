r"""The two v1p3 review findings, on the built website (headless Edge, a fresh profile per store):
  F1 lineup alias  an earlier version's store whose open lineup is identical to another lineup (the later one open), identical to its
                   collection, or already converted (by another tab or an earlier run): the open choice becomes the collection that lineup
                   actually became; no duplicate collection is made to hide it; the store as it was is kept once, unchanged; a reload, a
                   second migration, a collection file and a Full Studio Backup's restore plan follow the same map (CS.lineupMap)
  F2 headerless    the licensed Included Game Thwaite without its 16-byte header (exactly its recorded content) is accepted by Add ROM
                   Files directly and inside a ZIP, kept with its recorded header and ready; Locate ROM File… accepts it for the game;
                   headerless bytes that are no known game, and a .nes file without a header, are refused with the reason; a headerless
                   file of another game is not taken for the located one
  python test_v1p4_findings_web.py <built site folder> [--out results.json]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, hashlib, json, os, sys, tempfile, time, zipfile, io, base64
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from studio_harness import EdgeSession, READY, CALM, serve

NOW = "2026-11-12T10:00:00.000Z"; HOME = {"v": 1, "theme": "arcade", "folders": [], "favorites": [], "history": [], "settings": {}}
def lineup(i, sel, home=HOME): return {"id": i, "name": i[1:], "target": "nes-classic", "created": NOW, "modified": NOW, "sel": sel, "decisions": {}, "home": home}
def parent(games, open_, lineups, extra=None):
    return dict({"id": "cParent", "name": "All Games", "created": NOW, "modified": NOW, "sel": games, "decisions": {}, "coll": {"pref": {}, "multi": {}, "avail": [], "placeholders": [], "missing": {}},
                 "home": dict(HOME, theme="minimal"), "openLoadout": open_, "loadouts": lineups}, **(extra or {}))

def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")   # check names carry arrows and dashes; any console prints them
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); a = ap.parse_args()
    srv = serve(a.site); base = f"http://127.0.0.1:{srv.server_address[1]}"; res = []
    def ok(n, c, d=""):
        res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:400]})" if d and not c else ""), flush=True)
    games = [g["game_id"] for g in json.load(open(os.path.join(a.site, "catalog.json"), encoding="utf-8"))["games"][:3]]
    def store(items, active="cParent"): return {"schema": "ccs-collections-store/1", "active": active, "items": items}
    def load(ed, st):
        ed.navigate(base + "/robots.txt"); ed.wait_for("document.readyState==='complete'", timeout=30)
        ed.js("localStorage.clear(); localStorage.setItem('studio-collections-v2'," + json.dumps(json.dumps(st)) + "); 1")
        ed.navigate(base + "/index.html"); ed.wait_for(READY, timeout=120); time.sleep(0.8)
        return json.loads(ed.js("JSON.stringify({active:CS.active, working:[...sel], list:csList().map(c=>({id:c.id, name:c.name, sel:c.sel, fromLineup:c.fromLineup||null})), map:CS.lineupMap||null, before:JSON.parse(localStorage.getItem(CSKEY+'.before-collections-only')||'null')})"))
    try:
        # ---------------- F1 ----------------
        with EdgeSession(profile=tempfile.mkdtemp(prefix="ccs-v1p4-f1a-"), width=1440, height=900) as ed:
            st = store({"cParent": parent(games, "lSecond", [lineup("lFirst", games[:1]), lineup("lSecond", games[:1])])}); s = load(ed, st)
            conv = [c for c in s["list"] if c["fromLineup"]]
            ok("F1 duplicate siblings, the later open: its equivalent collection opens with its one game", s["working"] == games[:1] and s["active"] != "cParent", s)
            ok("F1 duplicate siblings: one collection made for both (no copy to hide the alias)", len(conv) == 1 and len(s["list"]) == 2, s["list"])
            ok("F1 duplicate siblings: both lineup identities map to that collection", s["map"] == {"cParent|lFirst": conv[0]["id"], "cParent|lSecond": conv[0]["id"]} if conv else False, s["map"])
            ok("F1 the store as it was is kept once, unchanged", (s["before"] or {}).get("store") == st, (s["before"] or {}).get("store"))
            ed.js("location.reload(); 1"); time.sleep(1.0); ed.wait_for(READY, timeout=120); time.sleep(0.6)
            r = json.loads(ed.js("JSON.stringify({active:CS.active, n:csList().length, again:csMigrateStore(), n2:csList().length})"))
            ok("F1 reload: the same collection is open; migrating again adds nothing", r["active"] == s["active"] and r["n"] == 2 and r["again"] is False and r["n2"] == 2, r)
            # a collection file written by an earlier version (its open lineup the later duplicate): importing it opens what that lineup became
            file = {"schema": "ccs-collections/1", "collections": [dict(parent(games, "lSecond", [lineup("lFirst", games[:1]), lineup("lSecond", games[:1])]), id="cFile", name="From File")]}
            ed.js("window.__i=0; csImportObject(" + json.dumps(file) + ", 'old.json').then(()=>window.__i=1); 1"); ed.wait_for("window.__i===1", timeout=30); time.sleep(0.4)
            r = json.loads(ed.js("JSON.stringify({name:csActive().name, working:[...sel]})"))
            ok("F1 a collection file: the file's open lineup (a duplicate) opens as the collection it became", r["working"] == games[:1] and r["name"].startswith("From File: "), r)
            # a Full Studio Backup restore plan into a new Studio: the backup's open choice is what its open lineup became
            bk = {"schema": "ccs-studio-backup/2", "active": "cBk", "collections": [dict(parent(games, "lSecond", [lineup("lFirst", games[:1]), lineup("lSecond", games[:1])]), id="cBk", name="Backed Up")]}
            r = json.loads(ed.js("(()=>{ const f=csFresh; csFresh=()=>true; try{ const p=csMergePlan(" + json.dumps(bk) + "); return JSON.stringify({open:p.open, add:p.add.map(x=>({name:x.name, sel:x.rec.sel, wasActive:x.wasActive}))}); } finally { csFresh=f; } })()"))
            tgt = [x for x in r["add"] if x["wasActive"]]
            ok("F1 a backup's restore plan: the open choice is the duplicate's collection", len(tgt) == 1 and tgt[0]["sel"] == games[:1] and r["open"] == tgt[0]["name"], r)
        with EdgeSession(profile=tempfile.mkdtemp(prefix="ccs-v1p4-f1b-"), width=1440, height=900) as ed:
            st = store({"cParent": parent(games, "lAll", [lineup("lAll", games, dict(HOME, theme="minimal")), lineup("lOne", games[:2])])}); s = load(ed, st)
            ok("F1 a lineup equal to its collection, open: the collection itself stays open, with all its games", s["active"] == "cParent" and s["working"] == games, s)
            ok("F1 equal to its collection: nothing is made for it, and it maps to the collection", len(s["list"]) == 2 and (s["map"] or {}).get("cParent|lAll") == "cParent", s)
        with EdgeSession(profile=tempfile.mkdtemp(prefix="ccs-v1p4-f1c-"), width=1440, height=900) as ed:
            conv = {"id": "cConv", "name": "All Games: First", "created": NOW, "modified": NOW, "sel": games[:1], "decisions": {}, "coll": {"pref": {}, "multi": {}, "avail": [], "placeholders": [], "missing": {}},
                    "home": HOME, "fromLineup": {"collection": "cParent", "lineup": "lFirst"}, "shares": []}
            st = store({"cParent": parent(games, "lSecond", [lineup("lFirst", games[:1]), lineup("lSecond", games[:1])]), "cConv": conv}); s = load(ed, st)
            ok("F1 already converted equivalent: the open duplicate opens the collection converted earlier", s["active"] == "cConv" and s["working"] == games[:1], s)
            ok("F1 already converted: no second copy", len(s["list"]) == 2 and (s["map"] or {}).get("cParent|lSecond") == "cConv", s["list"])
        # ---------------- F2 ----------------
        with EdgeSession(profile=tempfile.mkdtemp(prefix="ccs-v1p4-f2-"), width=1440, height=900) as ed:
            ed.navigate(base + "/index.html"); ed.wait_for(READY, timeout=120); ed.js(CALM); ed.wait_for("CS.starter&&CS.starter.state==='done'", timeout=90); time.sleep(0.8)
            ed.js("(()=>{ const r=csCreate('Headerless'); csSwitch(r.id, true); return 1; })()"); time.sleep(0.4)
            inc = json.load(open(os.path.join(a.site, "included", "index.json"), encoding="utf-8"))["games"][0]
            full = open(os.path.join(a.site, "included", inc["file_name"]), "rb").read(); raw = full[16:]; pid = "p_" + inc["content_sha256"][:16]
            assert hashlib.sha256(raw).hexdigest() == inc["content_sha256"]
            unknown = hashlib.sha256(b"x").digest() * 768; other = bytearray(raw); other[100] ^= 0xFF
            zb = io.BytesIO(); zf = zipfile.ZipFile(zb, "w", zipfile.ZIP_DEFLATED); zf.writestr("roms/Thwaite.unh", raw); zf.close()
            def files(*items):
                parts = ",".join(f"new File([Uint8Array.from(atob('{base64.b64encode(b).decode()}'), c=>c.charCodeAt(0))], {json.dumps(n)})" for n, b in items)
                ed.js(f"window.__files=[{parts}]; cvPick=async()=>window.__files; 1")
            def dlg(): return ed.js("$('collm').classList.contains('open')?{title:$('collTitle').textContent, text:$('collBody').innerText}:null") or {}
            def add():
                ed.js("window.__a=0; cvAddFiles().then(()=>window.__a=1); 1"); ed.wait_for("window.__a===1", timeout=60); time.sleep(0.4); d = dlg(); ed.js("closeColl(); 1"); return d
            def kept(): return json.loads(ed.js("CCSROMS.get(%s).then(r=>JSON.stringify(r?{size:r.size, header_hex:r.header_hex}:null))" % json.dumps(inc["content_sha256"])))
            ed.js("cpTx(['roms'],'readwrite',t=>t.objectStore('roms').delete(%s)).then(()=>{ CCSROMS._have=null; window.__d=1; }); 1" % json.dumps(inc["content_sha256"])); ed.wait_for("window.__d===1", timeout=20)
            files(("Thwaite.unh", raw)); d = add()
            ok("F2 Add Game Files: the known headerless Thwaite is added", ed.js("sel.has(%s)" % json.dumps(pid)) is True and "Added 1 game" in d.get("text", ""), d)
            k = kept(); ok("F2 its kept copy carries its recorded header (playable)", k and k["size"] == len(raw) and k["header_hex"] == inc["header_hex"], k)
            ok("F2 it is ready", ed.js("rdyCheck().then(()=>rdyS().st.get(%s))" % json.dumps(pid)) == "ready")
            ok("F2 it gets its canonical details (title, author, version), not its file name", ed.js("(()=>{ const m=COLL.missing[%s]; return m&&m.title==='Thwaite'&&m.publisher===%s&&m.version===%s; })()" % (json.dumps(pid), json.dumps(inc["creator"]), json.dumps(inc["version"]))) is True)
            ed.js("sel.delete(%s); persist(); 1" % json.dumps(pid))
            files(("Thwaite.zip", zb.getvalue())); d = add()
            ok("F2 Add Game Files: the headerless Thwaite inside a ZIP is added", ed.js("sel.has(%s)" % json.dumps(pid)) is True and "Added 1 game" in d.get("text", ""), d)
            files(("mystery.unh", unknown)); d = add()
            ok("F2 headerless bytes that are no known game are refused, with the reason", "No game was added" in d.get("text", "") and "no NES header" in d.get("text", "") and "cannot be determined" in d.get("text", ""), d)
            files(("noheader.nes", raw)); d = add()
            ok("F2 a .nes file without its header is not taken as one", "not an NES game file" in d.get("text", ""), d)
            # Locate ROM File… for the game: the headerless file of exactly it is accepted; another's is not
            ed.js("cpTx(['roms'],'readwrite',t=>t.objectStore('roms').delete(%s)).then(()=>{ CCSROMS._have=null; window.__d=2; }); 1" % json.dumps(inc["content_sha256"])); ed.wait_for("window.__d===2", timeout=20)
            files(("other.unh", bytes(other))); ed.js("window.__l=0; rdyLocate(%s).then(()=>window.__l=1); 1" % json.dumps(pid)); ed.wait_for("window.__l===1", timeout=30); d = dlg(); ed.js("closeColl(); 1")
            ok("F2 Locate: a headerless file of other content is refused, nothing kept", d.get("title") == "Game File Not Added" and kept() is None, d)
            files(("Thwaite.unh", raw)); ed.js("window.__l=0; rdyLocate(%s).then(()=>window.__l=1); 1" % json.dumps(pid)); ed.wait_for("window.__l===1", timeout=30); time.sleep(0.3)
            k = kept(); ok("F2 Locate: the game's own headerless file is accepted and kept with its header", k and k["header_hex"] == inc["header_hex"] and ed.js("rdyCheck().then(()=>rdyS().st.get(%s))" % json.dumps(pid)) == "ready", k)
            ok("F2 no page errors", not ed.exceptions, ed.exceptions[-3:])
    finally:
        srv.shutdown(); srv.server_close()
    n = sum(1 for _, c in res if c); print(f"{n}/{len(res)} PASS" if n == len(res) else f"FAILED: {len(res) - n} of {len(res)}", flush=True)
    if a.out: json.dump([{"check": k, "pass": v} for k, v in res], open(a.out, "w", encoding="utf-8"), indent=1)
    return 0 if n == len(res) else 1

if __name__ == "__main__": sys.exit(main())
