r"""ROM files and HOME readiness on the built website, headless Edge, one fresh profile. Synthetic sentinel ROMs (iNES headers with
made-up content; one is listed as a Game Catalog version under its own content identity, as a real catalog version would be). The browser's
file chooser is the only thing replaced: the test hands the chosen files to the page (cvPick). Every check reads the page and its stores.
  add         Add ROM Files: a catalog version joins with the catalog's details, an unknown NES ROM joins as a personal game, a file that
              is not an NES ROM and a duplicate are reported and change nothing; each added file is kept in the browser's game store
  missing     a game's kept file is deleted: it stays in the collection, marked ROM missing (not hidden, same count); its Play explains
              and does not start; the mark opens the same explanation (Locate ROM File…, Remove from This Collection)
  home        with a ROM missing, every way into HOME refuses and names the game: Open HOME, a game's Show in HOME, HOME Settings' Open
              HOME, openTv() itself; HOME is never open afterwards
  locate      Locate ROM File… with a different file is refused and keeps nothing; with the right file the game is ready again and HOME opens
  remove      Remove from This Collection (on another missing game): the game leaves this collection only (its other collection and its
              saved game stay); HOME opens
  unreadable  the game store cannot be read: games say "Not checked", HOME refuses with the storage explanation (never "ROM missing"),
              nothing is changed
  play        Play on a ready game opens it by itself (no HOME): the player's own start menu (save slots, Start New Game) is shown
  python test_rom_readiness_web.py <built site folder> [--out results.json] [--shots DIR]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, base64, hashlib, json, os, sys, tempfile, time
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from studio_harness import EdgeSession, READY, CALM, serve

def rom(tag: str, prg_kb=16, chr_kb=8) -> bytes:
    hdr = bytes([0x4E, 0x45, 0x53, 0x1A, prg_kb // 16, chr_kb // 8, 0, 0]) + bytes(8)
    body = bytearray(); seed = hashlib.sha256(("CCS-SENTINEL-ROM-BYTES " + tag).encode()).digest()
    while len(body) < (prg_kb + chr_kb) * 1024: seed = hashlib.sha256(seed).digest(); body += seed
    return hdr + bytes(body[:(prg_kb + chr_kb) * 1024])

def cid(b: bytes) -> str: return hashlib.sha256(b[16:]).hexdigest()

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); ap.add_argument("--shots", default=""); a = ap.parse_args()
    srv = serve(a.site); url = f"http://127.0.0.1:{srv.server_address[1]}/index.html"; res, log = [], {}
    def ok(n, c, d=""):
        res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:300]})" if d and not c else ""), flush=True)
    def shot(ed, name):
        if a.shots: os.makedirs(a.shots, exist_ok=True); open(os.path.join(a.shots, name), "wb").write(ed.screenshot())
    known, unknown, other, other_ver = rom("known"), rom("unknown"), rom("other"), rom("known-rev1")
    kid, uid = cid(known)[:16], "p_" + cid(unknown)[:16]
    def files(ed, *items):   # the files a person picks, handed to the page's file chooser hook
        parts = ",".join(f"new File([Uint8Array.from(atob('{base64.b64encode(b).decode()}'), c=>c.charCodeAt(0))], {json.dumps(n)})" for n, b in items)
        ed.js(f"window.__files=[{parts}]; cvPick=async()=>window.__files; 1")
    def dlg(ed): return ed.js("$('collm').classList.contains('open')?{title:$('collTitle').textContent, text:$('collBody').innerText}:null")
    def close(ed): ed.js("closeColl(); 1"); time.sleep(0.2)
    def recheck(ed): ed.js("window.__rd=0; rdyCheck().then(()=>{ render(); cvPanel(); window.__rd=1; }); 1"); ed.wait_for("window.__rd===1", timeout=30); time.sleep(0.3)
    def state(ed, i): return ed.js("rdyS().st.get(%s)||null" % json.dumps(i))
    try:
        with EdgeSession(profile=tempfile.mkdtemp(prefix="ccs-ready-"), width=1440, height=900) as ed:
            ed.navigate(url); ed.wait_for(READY, timeout=120); ed.js(CALM)
            ed.wait_for("CS.starter&&CS.starter.state==='done'", timeout=90); time.sleep(1.0)
            inc = ed.js("[...sel]"); base_n = len(inc)
            # a Game Catalog version for the "known" file (its content identity, as the catalog lists real versions)
            ed.js("(()=>{ const id=%s, t='tt_test_'+id; byId[id]=Object.assign({}, DATA.games[0], {game_id:id, title_id:t, display_title:'Test Catalog Game', nointro_name:'Test Catalog Game (USA)', builtin_code:'', builtin_status:'NOT BUILT-IN', payload_size:24576, art:'', art_generated:''});"
                  " TITLES[t]={id:t, title:'Test Catalog Game', versions:[id], recommended:id, curated:true}; return 1; })()" % json.dumps(kid))
            # ---- add ----
            files(ed, ("Test Catalog Game (USA).nes", known), ("my-homebrew.nes", unknown), ("notes.txt", b"not a rom at all, just text" * 10), ("copy.nes", known))
            ed.js("window.__a=0; cvAddFiles().then(()=>window.__a=1); 1"); ed.wait_for("window.__a===1", timeout=60); time.sleep(0.5)
            d = dlg(ed) or {}; log["add"] = d; s = ed.js("[...sel]")
            ok("add: a Game Catalog version joins under its catalog identity", kid in s, s)
            ok("add: an unknown NES ROM joins as a personal game (p_ + its content identity)", uid in s and ed.js("!!(COLL.missing[%s]&&COLL.missing[%s].source==='personal')" % (json.dumps(uid), json.dumps(uid))) is True, s)
            ok("add: the summary counts what was added, and names the file that is not an NES ROM", "Added 2 games" in d.get("text", "") and "notes.txt" in d.get("text", "") and "not an NES game file" in d.get("text", ""), d)
            ok("add: a duplicate of a file in the same choice adds nothing more", len(s) == base_n + 2, s)
            have = json.loads(ed.js("CCSROMS.keys(true).then(k=>JSON.stringify([...k]))"))
            ok("add: each added file is kept in the browser's game store by its exact content", cid(known) in have and cid(unknown) in have, have)
            close(ed); recheck(ed)
            ok("add: both are ready", state(ed, kid) == "ready" and state(ed, uid) == "ready", (state(ed, kid), state(ed, uid)))
            files(ed, ("again.nes", known)); ed.js("window.__a=0; cvAddFiles().then(()=>window.__a=1); 1"); ed.wait_for("window.__a===1", timeout=60); time.sleep(0.4)
            d = dlg(ed) or {}; ok("add: adding a game already in the collection says so and adds nothing", "No game was added" in d.get("text", "") and "already in it" in d.get("text", "") and len(ed.js("[...sel]")) == base_n + 2, d); close(ed)
            # the unknown game also in a second collection, with a saved game (they must survive its removal from the first)
            ed.js("(()=>{ const r=CS.items[CS.active]; csSaveActiveNow(); const c=csCreate('Second', {sel:[%s], coll:{missing:{[%s]:JSON.parse(JSON.stringify(COLL.missing[%s]))}}}); window.__second=c.id; return 1; })()" % (json.dumps(uid), json.dumps(uid), json.dumps(uid)))
            ed.js("cpTx(['files'],'readwrite',t=>t.objectStore('files').put({path:'/data/saves/%s/slot1.ccsstate', bytes:new Uint8Array([1,2,3]).buffer})).then(()=>window.__sv=1); 1" % cid(unknown)); ed.wait_for("window.__sv===1", timeout=20)
            # ---- missing ----
            for sha in (cid(known), cid(unknown)):
                ed.js("cpTx(['roms'],'readwrite',t=>t.objectStore('roms').delete(%s)).then(()=>{ CCSROMS._have=null; window.__del=(window.__del||0)+1; }); 1" % json.dumps(sha))
            ed.wait_for("window.__del===2", timeout=20); recheck(ed)
            rows = ed.js("+document.documentElement.dataset.rows")
            ok("missing: both games stay in the collection and on screen (same count)", rows == base_n + 2 and ed.js("sel.has(%s)&&sel.has(%s)" % (json.dumps(kid), json.dumps(uid))) is True, rows)
            ok("missing: each is marked Game file missing", state(ed, kid) == "missing" and ed.js("!!document.querySelector('[data-rdy=%s].rdy-missing')" % json.dumps(kid)) is True)
            ok("missing: the panel says how many games need their game file", "2 games need their game file" in (ed.js("$('cvPanel').innerText") or ""), ed.js("$('cvPanel').innerText"))
            ok("missing: the mark explains itself on hover (its title) and is reachable by keyboard (a button)", ed.js("(()=>{ const b=document.querySelector('[data-rdy=%s]'); return !!b&&b.tagName==='BUTTON'&&/Game file missing/.test(b.title)&&b.tabIndex>=0; })()" % json.dumps(kid)) is True)
            shot(ed, "missing-1440.png")
            ed.js("document.querySelector('[data-cvplay=%s]').click(); 1" % json.dumps(kid)); time.sleep(1.2); d = dlg(ed) or {}
            ok("missing: Play explains and does not start the game", d.get("title") == "Game File Missing" and "Locate Game File" in d.get("text", "") and "Remove from This Collection" in d.get("text", "") and ed.js("CCSPLAY.running()") is False, d)
            close(ed); ed.js("document.querySelector('[data-rdy=%s]').click(); 1" % json.dumps(uid)); time.sleep(0.5); d = dlg(ed) or {}
            ok("missing: the mark opens the same explanation", d.get("title") == "Game File Missing" and "my-homebrew" in d.get("text", "").lower() or "Locate Game File" in d.get("text", ""), d); close(ed)
            # ---- home: every way in refuses ----
            def refused(label, js):
                ed.js("TV.open&&closeTv(); 1"); close(ed); ed.js(js); time.sleep(1.4); d = dlg(ed) or {}
                ok(f"home: {label} refuses and names the games", ed.js("TV.open") is False and d.get("title") == "HOME" and "Test Catalog Game" in d.get("text", "") and "Game file missing" in d.get("text", ""), d)
            refused("Open HOME", "$('tvBtn').click(); 1")
            refused("openTv() itself", "openTv(); 1")
            ed.js("openDetails(%s); 1" % json.dumps(inc[0])); time.sleep(0.4)
            refused("a game's Show in HOME", "document.querySelector('#details [data-tvfocus]').click(); 1"); ed.js("$('details').classList.remove('open'); 1")
            ed.js("openFolders(); 1"); time.sleep(0.6)
            refused("HOME Settings' Open HOME", "$('hfPreview').click(); 1")
            # the toolbar's Install on NES Classic as the Windows app has it (the website shows its invitation instead): the button itself,
            # not only openInstall() (build4's Sandbox run found the button bound to the function before the gate wrapped it)
            ed.js("TV.open&&closeTv(); window.__h=COMPANION.hosted; COMPANION.hosted=false; 1"); close(ed); ed.js("$('hkBtn').click(); 1"); time.sleep(1.4); d = dlg(ed) or {}
            ok("console: the Install on NES Classic button refuses and names the games", d.get("title") == "Prepare for NES Classic" and "Test Catalog Game" in d.get("text", "") and ed.js("$('hkpanel').classList.contains('open')") is False, d)
            ed.js("COMPANION.hosted=window.__h; $('hkpanel').classList.remove('open'); 1"); close(ed)
            shot(ed, "home-refused-1440.png"); close(ed)
            # ---- locate ----
            files(ed, ("wrong.nes", other_ver)); ed.js("window.__l=0; rdyLocate(%s).then(()=>window.__l=1); 1" % json.dumps(kid)); ed.wait_for("window.__l===1", timeout=30); d = dlg(ed) or {}
            have = json.loads(ed.js("CCSROMS.keys(true).then(k=>JSON.stringify([...k]))"))
            ok("locate: a different file is refused and nothing is kept", d.get("title") == "Game File Not Added" and cid(other_ver) not in have, d)
            close(ed); files(ed, ("Test Catalog Game (USA).nes", known)); ed.js("window.__l=0; rdyLocate(%s).then(()=>window.__l=1); 1" % json.dumps(kid)); ed.wait_for("window.__l===1", timeout=30); recheck(ed)
            ok("locate: the right file makes the game ready again", state(ed, kid) == "ready", state(ed, kid))
            # ---- remove ----
            ed.js("document.querySelector('[data-rdy=%s]').click(); 1" % json.dumps(uid)); time.sleep(0.4); ed.js("$('rdyRm').click(); 1"); time.sleep(0.6); recheck(ed)
            other_sel = ed.js("CS.items[window.__second].sel"); save = ed.js("cpTx(['files'],'readonly',t=>cpReq(t.objectStore('files').get('/data/saves/%s/slot1.ccsstate'))).then(r=>!!r)" % cid(unknown))
            ok("remove: the game leaves this collection only", ed.js("sel.has(%s)" % json.dumps(uid)) is False and other_sel == [uid], other_sel)
            ok("remove: its saved game is kept", save is True)
            ed.js("$('tvBtn').click(); 1"); time.sleep(2.0)
            ok("home: with every game ready, Open HOME opens", ed.js("TV.open") is True and not (dlg(ed) or {}).get("title"))
            shot(ed, "home-open-1440.png"); ed.js("closeTv(); 1"); time.sleep(0.4)
            # ---- unreadable game store ----
            ed.js("window.__keys=CCSROMS.keys; CCSROMS.keys=async()=>{ throw new Error('This browser could not read its storage for games (test).'); }; 1"); recheck(ed)
            ok("unreadable: games say Not checked, never Game file missing", state(ed, kid) == "unknown" and ed.js("!!document.querySelector('[data-rdy=%s].rdy-unknown')" % json.dumps(kid)) is True)
            ed.js("$('tvBtn').click(); 1"); time.sleep(1.2); d = dlg(ed) or {}
            ok("unreadable: HOME refuses with the storage explanation, and nothing was changed", ed.js("TV.open") is False and "could not check" in d.get("text", "") and "Nothing was changed" in d.get("text", "") and "Game file missing" not in d.get("text", ""), d)
            ok("unreadable: the collection is unchanged", len(ed.js("[...sel]")) == base_n + 1)
            close(ed); ed.js("CCSROMS.keys=window.__keys; 1"); recheck(ed)
            # ---- play: one game by itself ----
            ed.call("Emulation.setFocusEmulationEnabled", {"enabled": True})
            ed.js("document.querySelector('[data-cvplay=%s]').click(); 1" % json.dumps(inc[0]))
            started = False
            try: ed.wait_for("CCSPLAY.running()&&!!CCSPLAY.debug().lastMenu", timeout=40); started = True
            except Exception: pass
            dbg = ed.js("CCSPLAY.running()?CCSPLAY.debug():null") or {}
            ok("play: Play opens the game by itself (no HOME) at the player's own start menu", started and ed.js("$('playv').classList.contains('open')&&!TV.open") is True, json.dumps(dbg)[:200])
            shot(ed, "play-start-menu-1440.png"); log["play"] = {"lastMenu": dbg.get("lastMenu")}
            ed.js("CCSPLAY.requestClose(); 1")
            try: ed.wait_for("!CCSPLAY.running()&&!$('playv').classList.contains('open')", timeout=20); closed = True
            except Exception: closed = False
            ok("play: closing the game from its menu returns to the collection", closed)
            print("exceptions:", ed.exceptions[:3], "console:", ed.console_errors[:3])
            ok("no page errors", not ed.exceptions, ed.exceptions[:2])
    finally:
        srv.shutdown()
    fails = [n for n, c in res if not c]
    print(f"\n{len(res) - len(fails)}/{len(res)} PASS" + (f"; FAIL: {', '.join(fails)}" if fails else ""))
    if a.out: json.dump({"results": [{"name": n, "pass": c} for n, c in res], "log": log}, open(a.out, "w", encoding="utf-8"), indent=1)
    return 0 if not fails else 1

if __name__ == "__main__": sys.exit(main())
