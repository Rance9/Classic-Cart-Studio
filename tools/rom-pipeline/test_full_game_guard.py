r"""A demo's full-game check across the Windows app's actual inventory (Codex v1p4 F2): the app's own backend from source on loopback with
throwaway data folders (never the person's), then the built page's guard (headless Edge) fed exactly the answers that backend gave.
  backend   /api/package/inventory answers by exact content across both game stores: a Game Library copy under the normal catalog id
            (content SHA-256[:16]; the licensed Thwaite file used only as an identity fixture) and an own game under "p_" are both
            available; a kept record whose file is gone is missing (either store); no record is none; deleting the record turns missing
            into none; a file that cannot be read is unreadable; an unreadable game list is an error (never none); a same-name game of
            other content is not the build; Locate of a game a collection names only by its content keeps a file only of exactly that
            content
  page      (desktop) available under the catalog id offers Go to Full Game, and Go / Play / Add use that id; a missing record that no
            collection selects keeps Buy unavailable, offers Locate, and the store stays closed on a fresh check; Locate asks the store
            that holds the record (relink with the content for "p_", the Game Library's locate for a catalog id); unreadable and an error
            say so with the deliberate "anyway" only; (browser) a collection selecting the catalog id counts as missing too
  There is no approved real demo/full pair: the demo and full builds here are test records, not a claim about any purchase.
  python test_full_game_guard.py <built site folder> [--out results.json] [--shots DIR]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, hashlib, json, os, random, shutil, sys, tempfile, time, urllib.error, urllib.request
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE); CO = os.path.normpath(os.path.join(HERE, "..", "companion")); sys.path.insert(0, CO)
from test_included_backend import Backend
from studio_harness import EdgeSession, READY, CALM, serve

def rom(seed: int) -> tuple[bytes, str]:
    """a synthetic iNES file (NROM, 16 KB PRG + 8 KB CHR) and its content identity (the bytes after the header)"""
    r = random.Random(seed); body = bytes(r.getrandbits(8) for _ in range(16384 + 8192))
    return b"NES\x1a\x01\x01\x00\x00" + bytes(8) + body, hashlib.sha256(body).hexdigest()

def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); ap.add_argument("--shots", default=""); a = ap.parse_args()
    site = os.path.abspath(a.site); res = []; work = tempfile.mkdtemp(prefix="ccs-fullguard-")
    def ok(n, c, d=""): res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:400]})" if d and not c else ""), flush=True)
    th = json.load(open(os.path.join(site, "included", "index.json"), encoding="utf-8"))["games"][0]; TH = th["content_sha256"]
    cfg = os.path.join(work, "config.json"); os.makedirs(os.path.join(work, "tree")); json.dump({"rehearsal_tree": os.path.join(work, "tree")}, open(cfg, "w"))
    data = os.path.join(work, "data"); pers = os.path.join(data, "personal"); files = os.path.join(work, "files"); os.makedirs(pers); os.makedirs(files)
    def keep(name, b): p = os.path.join(files, name); open(p, "wb").write(b); return p
    own, OWN = rom(1); gone_p, GONE_P = rom(2); gone_c, GONE_C = rom(3); _, NONE = rom(4); locked, LOCKED = rom(5); other, OTHER = rom(6); loc, LOC = rom(7); cat, CAT = rom(8)
    th_copy = keep("thwaite-copy.nes", open(os.path.join(site, "included", th["file_name"]), "rb").read())
    idx = {"version": 1, "entries": {
        "p_" + OWN[:16]: {"id": "p_" + OWN[:16], "content_sha256": OWN, "managed_copy": keep("own.nes", own), "source_path": "", "user": {"title": "Own Full"}, "detected": {}},
        "p_" + GONE_P[:16]: {"id": "p_" + GONE_P[:16], "content_sha256": GONE_P, "managed_copy": os.path.join(files, "gone-personal.nes"), "source_path": "", "user": {"title": "Gone Full"}, "detected": {}},
        "p_" + LOCKED[:16]: {"id": "p_" + LOCKED[:16], "content_sha256": LOCKED, "managed_copy": keep("locked.nes", locked), "source_path": "", "user": {"title": "Locked Full"}, "detected": {}},
        "p_" + OTHER[:16]: {"id": "p_" + OTHER[:16], "content_sha256": OTHER, "managed_copy": keep("other.nes", other), "source_path": "", "user": {"title": "Space Test"}, "detected": {}}}}
    store = {TH[:16]: {"version_id": TH[:16], "path": th_copy, "content_sha256": TH, "format": "iNES"},
             CAT[:16]: {"version_id": CAT[:16], "path": keep("catalog-copy.nes", cat), "content_sha256": CAT, "format": "iNES"},
             GONE_C[:16]: {"version_id": GONE_C[:16], "path": os.path.join(files, "gone-catalog.nes"), "content_sha256": GONE_C, "format": "iNES"}}
    def save(): json.dump(idx, open(os.path.join(pers, "index.json"), "w")); json.dump(store, open(os.path.join(pers, "library-store.json"), "w"))
    save(); B = None; answers = {}
    try:
        B = Backend(site, data, cfg)
        def inv(cs):
            r = B.call("/api/package/inventory", {"contents": cs}); return r, {x["content_sha256"]: x for x in r.get("items") or []}
        r, by = inv([TH, OWN, GONE_P, GONE_C, NONE, OTHER, CAT])
        ok("backend: one answer per build", r.get("ok") and len(by) == 7, r)
        ok("backend: a Game Library copy under the normal catalog id is available, under that id (the v1p4 guard asked only for p_)", by[TH]["state"] == "available" and by[TH]["id"] == TH[:16], by[TH])
        ok("backend: an own game under p_ is available, under p_", by[OWN]["state"] == "available" and by[OWN]["id"] == "p_" + OWN[:16], by[OWN])
        ok("backend: a kept own-game record whose file is gone is missing (in no collection)", by[GONE_P]["state"] == "missing" and by[GONE_P]["id"] == "p_" + GONE_P[:16], by[GONE_P])
        ok("backend: a kept Game Library record whose file is gone is missing, under the catalog id", by[GONE_C]["state"] == "missing" and by[GONE_C]["id"] == GONE_C[:16], by[GONE_C])
        ok("backend: no record anywhere is none", by[NONE]["state"] == "none" and by[NONE]["id"] is None, by[NONE])
        ok("backend: a same-name game of other content does not answer for the build", by[OTHER]["state"] == "available" and inv([NONE])[1][NONE]["state"] == "none")
        ok("backend: a second Game Library copy (synthetic build) likewise, under its catalog id", by[CAT]["state"] == "available" and by[CAT]["id"] == CAT[:16], by[CAT])
        answers.update({k: by[k] for k in (TH, GONE_P, GONE_C, NONE, CAT)})
        import msvcrt
        fh = open(idx["entries"]["p_" + LOCKED[:16]]["managed_copy"], "r+b"); msvcrt.locking(fh.fileno(), msvcrt.LK_NBLCK, len(locked))
        try: r, by = inv([LOCKED])
        finally: fh.seek(0); msvcrt.locking(fh.fileno(), msvcrt.LK_UNLCK, len(locked)); fh.close()
        ok("backend: a file that is there but cannot be read is unreadable (never none or missing)", by.get(LOCKED, {}).get("state") == "unreadable", by); answers["unreadable"] = by.get(LOCKED)
        del idx["entries"]["p_" + GONE_P[:16]]; save(); r, by = inv([GONE_P])
        ok("backend: deleting the record (not just its file) turns missing into none", by[GONE_P]["state"] == "none", by); idx["entries"]["p_" + GONE_P[:16]] = {"id": "p_" + GONE_P[:16], "content_sha256": GONE_P, "managed_copy": os.path.join(files, "gone-personal.nes"), "source_path": "", "user": {"title": "Gone Full"}, "detected": {}}; save()
        open(os.path.join(pers, "index.json"), "w").write("{ not json"); r, _ = inv([TH])
        ok("backend: a game list that cannot be read is an error, never none", r.get("ok") is False and "could not be read" in r.get("error", ""), r); answers["error"] = r; save()
        # Locate of a game a collection names only by its content (no record here yet)
        wrong = keep("wrong.nes", other)
        try: r = B.call("/api/personal/relink", {"id": "p_" + LOC[:16], "content_sha256": LOC, "path": wrong}); err = r.get("error", "") if not r.get("ok") else ""
        except urllib.error.HTTPError as e: err = json.loads(e.read() or b"{}").get("error", "")
        ok("backend Locate (no record yet): a file of other content is refused, nothing kept", "different game file" in err and ("p_" + LOC[:16]) not in B.entries(), err)
        r = B.call("/api/personal/relink", {"id": "p_" + LOC[:16], "content_sha256": LOC, "path": keep("located.nes", loc)})
        ok("backend Locate (no record yet): exactly that content is kept, and the build is then available", r.get("ok") and inv([LOC])[1][LOC]["state"] == "available", r)
    finally:
        if B: B.stop()
    # ---------------- the page, fed exactly those answers ----------------
    srv = serve(site); base = f"http://127.0.0.1:{srv.server_address[1]}"
    demo = {"key": "guard-demo", "title": "Guard Test Demo", "family": "guard-test", "edition": "demo", "version": "0.1", "content_sha256": hashlib.sha256(b"guard demo").hexdigest(), "links": {"store": "https://store.example/guard-test"}, "creator": "Test", "description": "test record"}
    def full(key, c): return {"key": key, "title": "Guard Test", "family": "guard-test", "edition": "full", "version": "1.0", "content_sha256": c, "links": {"store": "https://store.example/guard-test"}, "hosted": False, "creator": "Test"}
    try:
        with EdgeSession(profile=tempfile.mkdtemp(prefix="ccs-fullguard-p-"), width=1440, height=900) as ed:
            ed.navigate(base + "/index.html"); ed.wait_for(READY, timeout=120); ed.js(CALM); ed.wait_for("CS.starter&&CS.starter.state==='done'", timeout=90); time.sleep(0.6)
            ed.js("INCL_DATA.games.push(%s); window.__opened=[]; window.open=u=>{ window.__opened.push(u); return null; }; window.__calls=[]; 1" % json.dumps(demo))
            def desktop(content, answer):   # the app's guard with this one full build and the backend's own answer for it
                ed.js("""(()=>{ INCL_DATA.known_builds.length=0; INCL_DATA.known_builds.push(%s); CO.desktop=true; window.__calls=[]; window.__opened=[];
                  window.__ans=%s; hkPost=async(path,body)=>{ window.__calls.push({path,body}); if(path==='/api/package/inventory') return window.__ans; return {ok:false, canceled:true}; };
                  playDirect=id=>{ window.__played=id; }; return 1; })()""" % (json.dumps(full("guard-full", content)), json.dumps(answer)))
            def buy():
                return ed.js("""(async()=>{ closeColl(); const box=document.createElement('div'); box.className='incbuy'; box.dataset.incBuy='guard-demo';
                  openColl('Guard','',''); $('collBody').append(box); await incBuyFill(box); const st=await incFullState(incByKey('guard-demo'));
                  const b=s=>box.querySelector(s); return {state:st.state, id:st.id||null, text:box.innerText, store:!!b('[data-inc-store]:not([data-inc-store-anyway])'), anyway:!!b('[data-inc-store-anyway]'),
                    go:b('[data-inc-gofull]')?b('[data-inc-gofull]').dataset.incId:null, loc:b('[data-inc-locate]')?b('[data-inc-locate]').dataset.incId:null}; })()""")
            def fresh_store(): ed.js("window.__opened=[]; window.__d=0; incOpenStore('guard-demo', false).then(()=>window.__d=1); 1"); ed.wait_for("window.__d===1", timeout=20); return ed.js("window.__opened")
            ok_ = lambda n: {"ok": True, "items": [answers[n]]}
            desktop(TH, ok_(TH)); g = buy()
            ok("page: the full build held as a Game Library copy under the catalog id (Codex F2 case 2): already in your library, no store", g["state"] == "available" and g["go"] == TH[:16] and not g["store"] and fresh_store() == [], g)
            desktop(CAT, ok_(CAT)); g = buy()
            ok("page: a full game held under the catalog id is already in your library, Go to Full Game under that id, no store", g["state"] == "available" and "Full game already in your library" in g["text"] and g["go"] == CAT[:16] and not g["store"] and fresh_store() == [], g)
            if a.shots: os.makedirs(a.shots, exist_ok=True); open(os.path.join(a.shots, "guard-catalog-available-1440.png"), "wb").write(ed.screenshot())
            ed.js("document.querySelector('#collBody [data-inc-gofull]').click(); 1"); time.sleep(0.4)
            ed.js("document.querySelector('#collBody [data-inc-playown]').click(); 1"); time.sleep(0.3)
            ok("page: Go to Full Game, then Play, plays the catalog id the app holds", ed.js("window.__played") == CAT[:16], ed.js("window.__played"))
            ed.js("incGoFull(%s, %s); document.querySelector('#collBody [data-inc-addown]').click(); 1" % (json.dumps(CAT), json.dumps(CAT[:16]))); time.sleep(0.4)
            ok("page: Add to the collection adds the catalog id (no p_ stand-in)", ed.js("sel.has(%s)&&!sel.has(%s)" % (json.dumps(CAT[:16]), json.dumps("p_" + CAT[:16]))))
            ed.js("sel.delete(%s); persist(); 1" % json.dumps(CAT[:16]))
            desktop(GONE_P, ok_(GONE_P)); g = buy()
            ok("page: a kept own-game record with its file gone, in no collection: missing, Locate, no Buy (Codex F2 case 1)", g["state"] == "missing" and g["loc"] == "p_" + GONE_P[:16] and not g["store"] and not g["anyway"] and "previously added" in g["text"], g)
            ok("page: the store stays closed on a fresh check", fresh_store() == [])
            if a.shots: open(os.path.join(a.shots, "guard-missing-uncollected-1440.png"), "wb").write(ed.screenshot())
            ed.js("document.querySelector('#collBody [data-inc-locate]').click(); 1"); time.sleep(0.4); c = [x for x in ed.js("window.__calls") if x["path"] != "/api/package/inventory"]
            ok("page: Locate asks the own-games store with the exact content", c and c[0]["path"] == "/api/personal/relink" and c[0]["body"] == {"id": "p_" + GONE_P[:16], "content_sha256": GONE_P}, c)
            desktop(GONE_C, ok_(GONE_C)); g = buy(); ed.js("document.querySelector('#collBody [data-inc-locate]').click(); 1"); time.sleep(0.4); c = [x for x in ed.js("window.__calls") if x["path"] != "/api/package/inventory"]
            ok("page: a kept Game Library record with its file gone: missing, Locate through the Game Library's locate under the catalog id", g["state"] == "missing" and g["loc"] == GONE_C[:16] and not g["store"] and c and c[0] == {"path": "/api/library/locate", "body": {"version_id": GONE_C[:16]}}, (g, c))
            desktop(NONE, ok_(NONE)); g = buy()
            ok("page: no record: Get the Full Game, opened only after a fresh check", g["state"] == "none" and g["store"] and fresh_store() == ["https://store.example/guard-test"], g)
            desktop(GONE_P, {"ok": True, "items": [dict(answers[NONE], content_sha256=GONE_P)]}); ed.js("csCreate('Another collection').sel=[%s]; csWrite(); 1" % json.dumps("p_" + GONE_P[:16])); g = buy()
            ok("page: no record in the app but another collection selects it: missing with Locate", g["state"] == "missing" and g["loc"] == "p_" + GONE_P[:16] and not g["store"], g)
            desktop(LOCKED, {"ok": True, "items": [answers["unreadable"]]}); g = buy()
            ok("page: unreadable: could not check, Try Again and the deliberate 'anyway' only", g["state"] == "error" and "could not check" in g["text"] and g["anyway"] and not g["store"] and fresh_store() == [], g)
            desktop(TH, answers["error"]); g = buy()
            ok("page: an unreadable game list: could not check (never Buy)", g["state"] == "error" and "could not be read" in g["text"] and not g["store"], g)
            # the browser: either id of the build in a collection counts
            ed.js("CO.desktop=false; 1"); ed.js("INCL_DATA.known_builds.length=0; INCL_DATA.known_builds.push(%s); csCreate('Catalog id').sel=[%s]; csWrite(); 1" % (json.dumps(full("guard-full", NONE)), json.dumps(NONE[:16]))); g = buy()
            ok("page (browser): a collection selecting the catalog id of the build counts: missing with Locate under that id", g["state"] == "missing" and g["loc"] == NONE[:16] and not g["store"], g)
            ok("no page errors", not ed.exceptions and not ed.console_errors, (ed.exceptions, ed.console_errors))
    finally:
        srv.shutdown(); srv.server_close(); shutil.rmtree(work, ignore_errors=True)
    n = sum(1 for _, c in res if c); print(f"RESULT {n} / {len(res)}")
    if a.out: json.dump([{"check": k, "pass": c} for k, c in res], open(a.out, "w", encoding="utf-8"), indent=1)
    return 0 if res and n == len(res) else 1

if __name__ == "__main__": sys.exit(main())
