r"""Hosted Included Games (D-161/D-163) on the built website, headless Edge:
  add         Included Games… lists the approved games with author, version and edition; Add to Collection gets the exact build from this
              site, checks it and adds it to the chosen collection only (the open one, or another); again adds nothing
  failures    a changed file, a failed download and unreadable storage each say so and add nothing (no ROM-less entry, nothing kept)
  play        Play gets and checks the build and opens the game at its own start menu; nothing joins a collection
  offline     a game played once plays again with the network off; a game never got says it cannot be downloaded (not "ready")
  catalog     the Game Catalog lists the Included Games with Play and Add to Collection (no file to find)
  links       ?play=<key> opens that game's page with its author, edition and Play, cleans the address, changes no collection and writes
              no save; Play there starts it; an unknown key or a web address is ignored (nothing is fetched from anywhere else)
  guard       a demo (test list) and its full game: none here (Get the Full Game, opened only after a fresh check), kept here outside every
              collection, in another collection, previously added with its ROM missing (Locate Full Game ROM…, then available), an older
              full build (its version and a separate developer's-store button), a same-name game of other content and the demo itself
              (never a full match), storage that cannot be read (Try Again; a deliberate "anyway"), a purchase activation rechecked;
              keyboard activation reaches no store; the demo's Game Details carry the same check
  python test_included_hosted_web.py <built site folder> [--out results.json] [--shots DIR]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, base64, hashlib, json, os, sys, tempfile, time
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from studio_harness import EdgeSession, READY, CALM, serve

def rom(tag: str) -> bytes:
    hdr = bytes([0x4E, 0x45, 0x53, 0x1A, 1, 1, 0, 0]) + bytes(8); body = bytearray(); seed = hashlib.sha256(("CCS-GUARD " + tag).encode()).digest()
    while len(body) < 24576: seed = hashlib.sha256(seed).digest(); body += seed
    return hdr + bytes(body[:24576])
def cid(b: bytes) -> str: return hashlib.sha256(b[16:]).hexdigest()

def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")   # check names carry arrows and dashes; any console prints them
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); ap.add_argument("--shots", default=""); a = ap.parse_args()
    srv = serve(a.site); base = f"http://127.0.0.1:{srv.server_address[1]}"; res = []
    def ok(n, c, d=""):
        res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:400]})" if d and not c else ""), flush=True)
    def shot(ed, name):
        if a.shots: os.makedirs(a.shots, exist_ok=True); open(os.path.join(a.shots, name), "wb").write(ed.screenshot())
    idx = json.load(open(os.path.join(a.site, "included", "index.json"), encoding="utf-8")); th, cr = idx["games"][0], idx["games"][1]
    tid, crid = "p_" + th["content_sha256"][:16], "p_" + cr["content_sha256"][:16]
    try:
        with EdgeSession(profile=tempfile.mkdtemp(prefix="ccs-hosted-"), width=1440, height=900) as ed:
            def dlg(): return ed.js("$('collm').classList.contains('open')?{title:$('collTitle').textContent, text:$('collBody').innerText}:null") or {}
            def close(): ed.js("closeColl(); 1"); time.sleep(0.2)
            def drop(sha): ed.js("cpTx(['roms'],'readwrite',t=>t.objectStore('roms').delete(%s)).then(()=>{ CCSROMS._have=null; window.__dr=(window.__dr||0)+1; }); 1" % json.dumps(sha)); time.sleep(0.4)
            def has(sha): return ed.js("CCSROMS.has(%s)" % json.dumps(sha))
            def run(js, flag="__r", t=60): ed.js(f"window.{flag}=0; Promise.resolve({js}).then(()=>window.{flag}=1, ()=>window.{flag}=1); 1"); ed.wait_for(f"window.{flag}===1", timeout=t); time.sleep(0.4)
            ed.navigate(base + "/index.html"); ed.wait_for(READY, timeout=120); ed.js(CALM); ed.wait_for("CS.starter&&CS.starter.state==='done'", timeout=90); time.sleep(0.8)
            inc_coll = ed.js("CS.active")
            ed.js("(()=>{ const r=csCreate('Living Room'); csSwitch(r.id, true); window.__lr=r.id; const k=csCreate('Kids'); window.__kids=k.id; csSwitch(r.id, true); return 1; })()"); time.sleep(0.5)
            ed.js("window.__fetches=[]; const f0=window.fetch; window.__f0=f0; window.fetch=function(u,o){ window.__fetches.push(String(u)); return f0.apply(this, arguments); }; 1")
            # ---- add ----
            ed.js("incBrowse(); 1"); time.sleep(0.5); d = dlg()
            ok("add: Included Games… lists each approved game with author, version and edition", d.get("title") == "Included Games" and "Thwaite" in d.get("text", "") and "Damian Yerrick" in d.get("text", "") and "Full game" in d.get("text", "") and th["version"] in d.get("text", ""), d)
            shot(ed, "included-browse-1440.png")
            drop(th["content_sha256"]); n0 = ed.js("csList().length")
            ed.js("document.querySelector('[data-incadd=\"thwaite\"]').click(); 1"); ed.wait_for("sel.has(%s)" % json.dumps(tid), timeout=30); time.sleep(0.4)
            ok("add: Add to Collection got the exact build from this site and kept it", has(th["content_sha256"]) is True and any(u.endswith("included/thwaite.nes") for u in ed.js("window.__fetches")), ed.js("window.__fetches"))
            ok("add: it joined the open collection only, with its canonical details", ed.js("(()=>{ const m=COLL.missing[%s]; return sel.has(%s)&&m&&m.title==='Thwaite'&&m.publisher===%s; })()" % (json.dumps(tid), json.dumps(tid), json.dumps(th["creator"]))) is True and ed.js("(CS.items[window.__kids].sel||[]).length") == 0)
            ok("add: no collection was made", ed.js("csList().length") == n0)
            ed.js("document.querySelector('[data-incadd=\"thwaite\"]').click(); 1"); time.sleep(0.8)
            ok("add: adding it again adds nothing", ed.js("[...sel].filter(x=>x===%s).length" % json.dumps(tid)) == 1)
            ed.js("document.querySelector('select[data-inc-to=\"concentration-room\"]').value=window.__kids; document.querySelector('[data-incadd=\"concentration-room\"]').click(); 1")
            ed.wait_for("(CS.items[window.__kids].sel||[]).includes(%s)" % json.dumps(crid), timeout=30); time.sleep(0.3)
            ok("add: to another collection, which alone changes", ed.js("sel.has(%s)" % json.dumps(crid)) is False and ed.js("CS.active") == ed.js("window.__lr"), ed.js("CS.items[window.__kids].sel"))
            # ---- failures ----
            ed.js("sel.delete(%s); persist(); 1" % json.dumps(tid)); drop(th["content_sha256"])
            ed.js("window.fetch=function(u,o){ if(String(u).includes('included/thwaite.nes')) return window.__f0(String(u).replace('thwaite.nes','croom.nes'), o); return window.__f0.apply(this, arguments); }; 1")
            close(); run("incAddTo('thwaite', window.__lr)"); d = dlg()
            ok("failures: a file that does not match its record is refused; nothing is added or kept", "does not match its record" in d.get("text", "") and "Nothing was added" in d.get("text", "") and ed.js("sel.has(%s)" % json.dumps(tid)) is False and has(th["content_sha256"]) is False, d)
            ed.js("window.fetch=function(u,o){ if(String(u).includes('included/')) return Promise.reject(new TypeError('Failed to fetch')); return window.__f0.apply(this, arguments); }; 1")
            close(); run("incAddTo('thwaite', window.__lr)"); d = dlg()
            ok("failures: a failed download says so; nothing is added", "could not be downloaded" in d.get("text", "") and ed.js("sel.has(%s)" % json.dumps(tid)) is False, d)
            ed.js("window.fetch=window.__f0; window.__has=CCSROMS.has; CCSROMS.has=async()=>{ throw cpReadError(new Error('x'), 'the games it keeps for playing here'); }; 1")
            close(); run("incAddTo('thwaite', window.__lr)"); d = dlg()
            ok("failures: unreadable storage says so (never 'not here'); nothing is added", "could not read" in d.get("text", "") and ed.js("sel.has(%s)" % json.dumps(tid)) is False, d)
            ed.js("CCSROMS.has=window.__has; 1"); close()
            # ---- play ----
            ed.call("Emulation.setFocusEmulationEnabled", {"enabled": True}); n_sel = ed.js("sel.size")
            ed.js("incBrowse(); 1"); time.sleep(0.4); ed.js("document.querySelector('[data-incplay=\"thwaite\"]').click(); 1")
            started = False
            try: ed.wait_for("CCSPLAY.running()&&!!CCSPLAY.debug().lastMenu", timeout=60); started = True
            except Exception: pass
            ok("play: Play gets the build and opens the game at its own start menu", started and has(th["content_sha256"]) is True)
            ok("play: nothing joins a collection", ed.js("sel.size") == n_sel and ed.js("sel.has(%s)" % json.dumps(tid)) is False)
            shot(ed, "included-play-1440.png"); ed.js("CCSPLAY.requestClose(); 1")
            try: ed.wait_for("!CCSPLAY.running()", timeout=20)
            except Exception: pass
            # ---- offline ----
            drop(cr["content_sha256"]); ed.call("Network.enable"); ed.call("Network.emulateNetworkConditions", {"offline": True, "latency": 0, "downloadThroughput": -1, "uploadThroughput": -1})
            close(); ed.js("incPlay('thwaite'); 1")   # resolves when the game ends
            try: ed.wait_for("CCSPLAY.running()&&!!CCSPLAY.debug().lastMenu", timeout=60); off = True
            except Exception: off = False
            ok("offline: a game played once plays again with the network off", off)
            ed.js("CCSPLAY.requestClose(); 1")
            try: ed.wait_for("!CCSPLAY.running()", timeout=20)
            except Exception: pass
            # a game this browser never got (the starter did get the two real ones, and the site's offline cache keeps them): a test record
            ed.js("INCL_DATA.games.push(Object.assign({}, INCL_DATA.games[1], {key:'never-got', title:'Never Got', file_name:'never-got.nes', content_sha256:'ab'.repeat(32)})); 1")
            close(); run("incPlay('never-got')"); d = dlg()
            ok("offline: a game never got here says it cannot be downloaded (not offline-ready)", "could not be downloaded" in d.get("text", "") and ed.js("CCSPLAY.running()") is False, d)
            ed.call("Network.emulateNetworkConditions", {"offline": False, "latency": 0, "downloadThroughput": -1, "uploadThroughput": -1}); close()
            # ---- catalog ----
            ed.js("cvCatalog(true); $('q').value='Thwaite'; $('q').dispatchEvent(new Event('input')); 1"); time.sleep(1.2)
            ok("catalog: the Game Catalog lists Thwaite with Play and Add to Collection", ed.js("!!document.querySelector('[data-incplay=\"thwaite\"]')&&!!document.querySelector('[data-incadd=\"thwaite\"]')") is True)
            shot(ed, "included-catalog-1440.png"); ed.js("$('q').value=''; $('q').dispatchEvent(new Event('input')); cvCatalog(false); 1"); time.sleep(0.5)
            # ---- links ----
            before = json.loads(ed.js("JSON.stringify({active:CS.active, n:csList().length, saves:0})"))
            ed.navigate(base + "/index.html?play=concentration-room"); ed.wait_for(READY, timeout=120); time.sleep(1.5); d = dlg()
            ok("links: ?play= opens that game's page with author, edition and Play", d.get("title") == "Concentration Room" and "Damian Yerrick" in d.get("text", "") and "Play" in d.get("text", ""), d)
            ok("links: the address is cleaned and no collection changes", "play=" not in ed.js("location.search") and ed.js("CS.active") == before["active"] and ed.js("csList().length") == before["n"])
            shot(ed, "included-link-1440.png")
            sv0 = ed.js("cpTx(['files'],'readonly',t=>cpReq(t.objectStore('files').count()))")
            ed.call("Emulation.setFocusEmulationEnabled", {"enabled": True}); ed.js("$('incDirGo').click(); 1")
            try: ed.wait_for("CCSPLAY.running()&&!!CCSPLAY.debug().lastMenu", timeout=60); st = True
            except Exception: st = False
            ok("links: Play there starts the game at its start menu", st)
            ed.js("CCSPLAY.requestClose(); 1")
            try: ed.wait_for("!CCSPLAY.running()", timeout=20)
            except Exception: pass
            ok("links: no save was written by following the link", ed.js("cpTx(['files'],'readonly',t=>cpReq(t.objectStore('files').getAllKeys())).then(k=>k.filter(p=>/\\.(ccsstate|sav)$/.test(p)).length)") == 0, sv0)
            ed.js("window.__fetches=[]; 1")
            for bad in ("https%3A%2F%2Fevil.example%2From.nes", "..%2F..%2Fsecret", "unknown-game"):
                ed.navigate(base + "/index.html?play=" + bad); ed.wait_for(READY, timeout=120); time.sleep(1.2); d = dlg()
                ok(f"links: an unknown value ({bad[:20]}) opens no game page", d.get("title") not in ("Concentration Room", "Thwaite") and "play=" not in ed.js("location.search"), d)
            ok("links: nothing was fetched from another site", not [u for u in ed.js("performance.getEntriesByType('resource').map(e=>e.name)") if "evil.example" in u])
            close()
            # ---- guard ---- (the links reloaded the page: find the collections again)
            ed.js("window.__kids=csList().find(c=>c.name==='Kids').id; window.__lr=csList().find(c=>c.name==='Living Room').id; csSwitch(window.__lr, true); 1"); time.sleep(0.4)
            demo, full, full2, other = rom("demo"), rom("full-1.0"), rom("full-1.1"), rom("unrelated")
            def b64(b): return base64.b64encode(b).decode()
            rec = lambda key, b, ed_, ver, extra=None: dict({"key": key, "title": "Space Test" + (" Demo" if ed_ == "demo" else ""), "version": ver, "creator": "Test Author", "family": "space-test", "edition": ed_,
                                                          "content_sha256": cid(b), "file_sha256": hashlib.sha256(b).hexdigest(), "size": len(b), "content_size": len(b) - 16, "file_name": key + ".nes",
                                                          "header_hex": b[:16].hex(), "description": "A test.", "text": "A test.", "links": {"author": "https://example.com/", "store": "https://store.example/space-test"}}, **(extra or {}))
            ed.js("INCL_DATA.games.push(%s); INCL_DATA.known_builds.push(%s, %s); INC_ROWS=null; 1" % (json.dumps(rec("space-demo", demo, "demo", "0.9", {"hosted": True})), json.dumps(rec("space-full", full, "full", "1.0")), json.dumps(rec("space-full-11", full2, "full", "1.1"))))
            ed.js("INCL_DATA.known_builds.pop(); 1")   # one full build first
            ed.js("window.__opened=[]; window.open=(u)=>{ window.__opened.push(u); return null; }; 1")
            def buy():
                ed.js("incBrowse(); 1"); time.sleep(0.3); ed.wait_for("!/Checking your library/.test((document.querySelector('[data-inc-buy=\"space-demo\"]')||{}).innerText||'')", timeout=20); time.sleep(0.2)
                return ed.js("(()=>{ const e=document.querySelector('[data-inc-buy=\"space-demo\"]'); return {text:e.innerText, store:!!e.querySelector('[data-inc-store]:not([data-inc-store-anyway])'), anyway:!!e.querySelector('[data-inc-store-anyway]'), go:!!e.querySelector('[data-inc-gofull]'), loc:!!e.querySelector('[data-inc-locate]'), links:e.querySelectorAll('a[href]').length}; })()")
            def put(b, title):   # kept, and finished, before the next step (a store write can take longer than a fixed wait)
                ed.js("window.__p=0; CCSROMS.put(Uint8Array.from(atob('%s'), c=>c.charCodeAt(0)), {sha:%s, title:%s, name:'x.nes'}).then(()=>window.__p=1); 1" % (b64(b), json.dumps(cid(b)), json.dumps(title))); ed.wait_for("window.__p===1", timeout=20); time.sleep(0.2)
            g = buy(); ok("guard none: the demo offers Get the Full Game (a button, no live link)", g["store"] and not g["go"] and g["links"] == 0, g)
            ed.js("document.querySelector('[data-inc-buy=\"space-demo\"] [data-inc-store]').click(); 1"); time.sleep(1.0)
            ok("guard none: the store opens only after a fresh check", ed.js("window.__opened") == ["https://store.example/space-test"], ed.js("window.__opened"))
            put(demo, "Space Test Demo"); g = buy(); ok("guard demo only: the demo itself is never a full match", g["store"] and not g["go"], g)
            ed.js("(()=>{ const r=csCreate('Same Name'); r.sel=['p_aaaabbbbccccdddd']; r.coll={missing:{p_aaaabbbbccccdddd:{title:'Space Test', source:'personal', content_sha256:'aaaabbbbccccdddd'.padEnd(64,'0')}}}; csWrite(); return 1; })()")
            put(other, "Space Test"); g = buy(); ok("guard unrelated: a same-name game of other content is not the full game", g["store"] and not g["go"], g)
            put(full, "Space Test"); g = buy()
            ok("guard available (kept, in no collection): Full game already in your library and Go to Full Game; no store control", "Full game already in your library" in g["text"] and g["go"] and not g["store"] and not g["anyway"], g)
            shot(ed, "guard-available-1440.png")
            ed.js("window.__opened=[]; const b=document.querySelector('[data-inc-buy=\"space-demo\"] [data-inc-gofull]'); b.focus(); b.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',bubbles:true})); b.click(); 1"); time.sleep(0.6); d = dlg()
            ok("guard available: Go to Full Game opens the full game's page, never a store", d.get("title") == "Space Test" and ed.js("window.__opened") == [] and "Full game" in d.get("subtitle", "Full game"), d)
            close(); ed.js("(()=>{ const r=CS.items[window.__kids]; r.sel=[...r.sel, %s]; csWrite(); return 1; })()" % json.dumps("p_" + cid(full)[:16])); g = buy()
            ok("guard available (in another collection): the same", "Full game already in your library" in g["text"] and not g["store"], g)
            drop(cid(full)); g = buy()
            ok("guard missing: Full game previously added — game file missing, with Locate Full Game File…; no store control", "previously added" in g["text"] and g["loc"] and not g["store"] and not g["anyway"], g)
            ed.js("window.__files=[new File([Uint8Array.from(atob('%s'), c=>c.charCodeAt(0))], 'space.nes')]; cvPick=async()=>window.__files; 1" % b64(full))
            ed.js("document.querySelector('[data-inc-buy=\"space-demo\"] [data-inc-locate]').click(); 1"); time.sleep(1.5)
            g = ed.js("document.querySelector('[data-inc-buy=\"space-demo\"]').innerText")
            ok("guard locate with the demo open: the full game is available at once", "Full game already in your library" in g, g)
            ed.js("INCL_DATA.known_builds.push(%s); 1" % json.dumps(rec("space-full-11", full2, "full", "1.1"))); g = buy()
            ok("guard older full build: its version is shown, with a separate developer's-store button", "version 1.0" in g["text"] and g["go"] and g["anyway"] and not g["store"], g)
            ed.js("window.__k=CCSROMS.keys; CCSROMS.keys=async()=>{ throw new Error('storage test'); }; 1"); g = buy()
            ok("guard unreadable: says it could not check, Try Again and a deliberate 'anyway' (no plain purchase)", "could not check" in g["text"] and g["anyway"] and not g["store"], g)
            ed.js("CCSROMS.keys=window.__k; CCSROMS._have=null; 1"); drop(cid(full)); ed.js("(()=>{ const r=CS.items[window.__kids]; r.sel=r.sel.filter(x=>x!==%s); csWrite(); return 1; })()" % json.dumps("p_" + cid(full)[:16]))
            g = buy(); ok("guard none again after removal", g["store"], g)
            # the purchase button as it was drawn before the full game arrived (the list not redrawn yet): its click checks again first
            ed.js("window.__ir=incRefresh; incRefresh=()=>{}; window.__stale=document.querySelector('[data-inc-buy=\"space-demo\"] [data-inc-store]:not([data-inc-store-anyway])'); 1")
            put(full, "Space Test"); ed.js("window.__opened=[]; window.__stale.click(); incRefresh=window.__ir; 1"); time.sleep(1.2)
            ok("guard recheck: a purchase click when the full game has just arrived opens no store", ed.js("window.__opened") == [], ed.js("window.__opened"))
            close(); ed.js("(()=>{ sel.add('p_'+%s); COLL.missing=Object.assign({}, COLL.missing, {['p_'+%s]:incKept(INCL_DATA.games.find(x=>x.key==='space-demo'))}); persist(); render(); openDetails('p_'+%s); return 1; })()" % ((json.dumps(cid(demo)[:16]),) * 3))
            time.sleep(1.5); g = ed.js("(()=>{ const e=document.querySelector('#details .incbuy'); return e?e.innerText:null; })()")
            ok("guard Game Details: the demo's Details carry the same check", g and "Full game already in your library" in g, g)
            ok("no page errors", not ed.exceptions, ed.exceptions[-3:])
    finally:
        srv.shutdown(); srv.server_close()
    n = sum(1 for _, c in res if c); print(f"{n}/{len(res)} PASS" if n == len(res) else f"FAILED: {len(res) - n} of {len(res)}", flush=True)
    if a.out: json.dump([{"check": k, "pass": v} for k, v in res], open(a.out, "w", encoding="utf-8"), indent=1)
    return 0 if n == len(res) else 1

if __name__ == "__main__": sys.exit(main())
