r"""Included Games collection identity (Codex v1p F3), on the built website in one fresh browser profile, one sequence:
  normal      the first Add makes one marked collection holding exactly the included games; the result names it and counts what it holds;
              the open collection (its games, decisions, HOME settings) and a saved game are untouched; each game's details in it are its
              record's (title, version, year, author, the text the Windows app keeps: test_included_backend.py)
  repeat      a second Add makes nothing; the result names the existing collection and counts its games
  changed     the person renames it and removes a game: Add leaves that collection as it is and adds a complete one under a free name
  renamed     a complete one the person renamed is recognized by its mark (Add makes nothing and names it)
  same name   with the marked ones gone, a person's own (unmarked) collection called "Included Games", with its own HOME settings: Add leaves it as it
              is and adds the games as "Included Games (Copy 1)"; Open It Now opens that one, never the person's
  overlap     with the marked ones gone again, two Adds started together make exactly one collection
  reload      after a page reload the marks, names and games are kept, and Add makes nothing
Every check reads the resulting stored collections and the dialog the page shows; nothing is taken from the fixture. On a build without
the mark (v1p), the edit steps find the collection by its name instead, so that build's failures are all listed rather than stopping the run.
  python test_included_collections.py <built site folder> [--out results.json]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, json, os, sys, tempfile, time
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from studio_harness import EdgeSession, READY, CALM, serve, SETTLED, NEW_COLL

STATE = """(()=>{ csSaveActiveNow(); return {active:CS.active, open:{sel:[...sel].sort(), decisions:decisions, home:(CS.items[CS.active].home||null)},
 colls:csList().map(c=>({id:c.id, name:c.name, sel:(c.sel||[]).slice().sort(), mark:(c.coll&&c.coll.included)||null, theme:(c.home||{}).theme||null}))}; })()"""

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); a = ap.parse_args()
    idx = json.load(open(os.path.join(a.site, "included", "index.json"), encoding="utf-8"))
    ids = sorted("p_" + g["content_sha256"][:16] for g in idx["games"]); marks = [g["content_sha256"] for g in idx["games"]]
    s = serve(a.site); srv, port = s if isinstance(s, tuple) else (s, s.server_address[1]); url = f"http://127.0.0.1:{port}/index.html"
    res, log = [], []
    def ok(n, c, d=""):
        res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({d})" if d and not c else ""))
    prof = tempfile.mkdtemp(prefix="ccs-included-coll-")
    def add(ed, n=1):
        ed.js("window.__addDone=false; Promise.all(Array.from({length:%d}, ()=>inclAdd())).then(()=>window.__addDone=true); 1" % n)
        ed.wait_for("window.__addDone", timeout=90)
        text = ed.js("(()=>{ const m=document.getElementById('collm'); return m&&!m.hidden&&document.getElementById('inclOpen')?m.innerText:''; })()") or ""
        st = ed.js(STATE); log.append({"adds": n, "dialog": text, "state": st}); return text, st
    def stay(ed): ed.js("(()=>{ const b=document.getElementById('inclStay'); if(b) b.click(); return 1; })()"); time.sleep(0.3)
    def marked(st): return [c for c in st["colls"] if c["mark"]]
    def drop_marked(ed): ed.js("(()=>{ for(const c of csList()) if(c.coll&&c.coll.included&&c.id!==CS.active) delete CS.items[c.id]; csWrite(); csMenuRefresh(); return 1; })()")
    try:
        with EdgeSession(profile=prof, width=1440, height=900) as ed:
            ed.navigate(url); ed.wait_for(READY, timeout=120); time.sleep(1.0); ed.js(CALM)
            # a new profile gets the Included Games by itself (D-146: collections.js/included.js); here the person removed that collection and
            # opened a collection of their own, so each Add below starts from the earlier situations
            ed.wait_for(SETTLED, timeout=90); ed.js(NEW_COLL); drop_marked(ed)
            # the person's open collection: a game, a decision-free selection, and its own HOME settings; one saved game for an included game
            ed.js("(()=>{ const g=DATA.games.find(x=>x.ines_ok!==false)||DATA.games[0]; sel.add(g.game_id); persist(); const r=CS.items[CS.active];"
                  " localStorage.setItem(HOME_KEY, JSON.stringify({v:1, theme:'arcade', folders:[], favorites:[], history:[], settings:{}})); csSaveActiveNow(); return 1; })()")
            ed.js("cpTx(['files'],'readwrite',t=>t.objectStore('files').put({path:'/data/saves/%s/test.sav', data:new Uint8Array([7,7,7])})).then(()=>window.__saved=1); 1" % marks[0])
            ed.wait_for("window.__saved===1", timeout=20)
            base = ed.js(STATE)

            text, st = add(ed)   # normal
            m = marked(st)
            ok("normal: one marked collection, exactly the included games", len(m) == 1 and m[0]["sel"] == ids and m[0]["mark"]["games"] == marks, json.dumps(m)[:200])
            ok("normal: the dialog names it and counts what it holds", m and f'"{m[0]["name"]}" ({len(m[0]["sel"])} games) is now in My Collections' in text, text[:200])
            ok("normal: the open collection, its games and HOME settings are unchanged", st["active"] == base["active"] and st["open"] == base["open"], json.dumps(st["open"])[:200])
            kept = ed.js("(()=>{ const c=csList().find(x=>x.coll&&x.coll.included); const mi=(c&&c.coll.missing)||{}; const o={};"
                         " Object.keys(mi).forEach(k=>{ const x=mi[k]; o[k]={title:x.title, version_label:x.version, description:x.description, year:String(x.year), publisher:x.publisher}; }); return o; })()") or {}
            byid = {"p_" + g["content_sha256"][:16]: g for g in idx["games"]}
            ok("normal: each game's details in the collection are its record's (the text the Windows app keeps too)",
               sorted(kept) == ids and all(kept[i] == {"title": byid[i]["title"], "version_label": byid[i]["version"], "description": byid[i]["text"], "year": byid[i]["year"], "publisher": byid[i]["creator"]} for i in ids), json.dumps(kept)[:240])
            stay(ed)

            text, st2 = add(ed)   # repeat
            ok("repeat: nothing new", len(st2["colls"]) == len(st["colls"]) and marked(st2) == m)
            ok("repeat: the dialog names the existing one with its real count", bool(m) and f'"{m[0]["name"]}" is already in My Collections with all {len(ids)} Included Games' in text, text[:200])
            stay(ed)

            ed.js("(()=>{ const c=csList().find(x=>x.coll&&x.coll.included)||csList().find(x=>x.name==='Included Games'); c.name='My Starter Games'; c.sel=c.sel.slice(0,1); c.modified=csNow(); csWrite(); return 1; })()")
            text, st3 = add(ed)   # changed (renamed and a game removed)
            edited = [c for c in st3["colls"] if c["name"] == "My Starter Games"]; whole = [c for c in marked(st3) if c["sel"] == ids]
            ok("changed: the person's edited collection stays as it is", len(edited) == 1 and len(edited[0]["sel"]) == 1)
            ok("changed: a complete marked collection was added", len(whole) == 1 and len(st3["colls"]) == len(st2["colls"]) + 1)
            ok("changed: the dialog says so, naming both and the real count", whole and f'You changed "My Starter Games", so it stays as it is. A complete copy, "{whole[0]["name"]}" ({len(ids)} games)' in text, text[:240])
            stay(ed)

            ed.js("(()=>{ const c=csList().find(x=>x.coll&&x.coll.included&&x.sel.length>1)||csList().find(x=>x.name==='Included Games'&&x.sel.length>1); if(!c) return 0; c.name='Starter Two'; csWrite(); return 1; })()")
            text, st4 = add(ed)   # renamed, complete
            ok("renamed: recognized by its mark, nothing new", len(st4["colls"]) == len(st3["colls"]) and '"Starter Two" is already in My Collections' in text, text[:200])
            stay(ed)

            drop_marked(ed)
            ed.js("(()=>{ const c=csCreate('Included Games', {sel:[], home:{v:1, theme:'boxart'}}); window.__own=c.id; return 1; })()")
            text, st5 = add(ed)   # same name, the person's own
            own = [c for c in st5["colls"] if c["id"] == ed.js("window.__own")]; new = [c for c in marked(st5)]
            ok("same name: the person's collection stays as it is (no games added, its HOME settings kept, its name kept)", len(own) == 1 and own[0]["sel"] == [] and own[0]["theme"] == "boxart" and own[0]["name"] == "Included Games" and not own[0]["mark"])
            ok("same name: the games were added under a free name", len(new) == 1 and new[0]["name"] == "Included Games (Copy 1)" and new[0]["sel"] == ids, json.dumps(new)[:200])
            ok("same name: the dialog names both and the real count", 'You already have a collection called "Included Games", so it stays as it is. The Included Games are now in My Collections as "Included Games (Copy 1)" (2 games)' in text, text[:240])
            ed.js("document.getElementById('inclOpen').click()"); time.sleep(1.0)
            ok("same name: Open It Now opens the added collection", ed.js("CS.items[CS.active].name") == "Included Games (Copy 1)" and sorted(ed.js("[...sel]")) == ids)
            ed.js("csSwitch(%s, true)" % json.dumps(base["active"])); time.sleep(0.8)

            drop_marked(ed)
            text, st6 = add(ed, 2)   # overlapping
            ok("overlap: two Adds started together make exactly one collection", len(marked(st6)) == 1 and len(st6["colls"]) == len(st5["colls"]), json.dumps([c["name"] for c in st6["colls"]]))
            ok("overlap: the open collection is still the person's, unchanged", st6["active"] == base["active"] and st6["open"] == base["open"])
            stay(ed)
            ed.js("cpTx(['files'],'readonly',t=>cpReq(t.objectStore('files').get('/data/saves/%s/test.sav'))).then(r=>window.__save=r?[...r.data]:null); 1" % marks[0])
            ed.wait_for("window.__save!==undefined", timeout=20); ok("the saved game is untouched", ed.js("window.__save") == [7, 7, 7], str(ed.js("window.__save")))
            before_reload = ed.js(STATE)
        with EdgeSession(profile=prof, width=1440, height=900) as ed:
            ed.navigate(url); ed.wait_for(READY, timeout=120); time.sleep(1.0); ed.js(CALM); ed.wait_for(SETTLED, timeout=60)
            st7 = ed.js(STATE)
            ok("reload: every collection, name, mark and game kept", st7["colls"] == before_reload["colls"], json.dumps(st7["colls"])[:240])
            text, st8 = add(ed)
            ok("reload: Add makes nothing and names the marked one", st8["colls"] == st7["colls"] and bool(marked(st7)) and f'"{marked(st7)[0]["name"]}" is already in My Collections' in text, text[:200])
            ok("reload: the open collection is still the person's, unchanged", st8["active"] == base["active"] and st8["open"] == base["open"])
    finally:
        try: srv.shutdown()
        except Exception: pass
    passed = sum(1 for _, c in res if c)
    if a.out: open(a.out, "w", encoding="utf-8").write(json.dumps({"checks": res, "log": log}, indent=1))
    print(f"RESULT: {'PASS' if passed == len(res) else 'FAIL'} ({passed}/{len(res)} checks)")
    return 0 if passed == len(res) else 1

if __name__ == "__main__": sys.exit(main())
