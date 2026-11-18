r"""The Included Games' covers on the built website (headless Edge): each Included Game shows its author's own screen from the game's
published archive (tools/publish/make_included_covers.py: Thwaite's title screen, Concentration Room's in-game screen, 2x on a black
card), never the generated cover, unless the person set an image of their own:
  files      each cover in the site is the recorded file, made from the recorded source file that is published beside the game
  rows       the open collection's cards, the Game Catalog's Included rows and Details (caption: title / game screen, from the author)
  HOME       the browser HOME takes it as real artwork (not generated), and each Included Game's backdrop is its genre's shared scene
  own image  an image the person set still wins; a .ccs backup does not carry the cover as the person's own artwork
  python test_included_covers_web.py <built site folder> [--out results.json] [--shots DIR]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, hashlib, json, os, sys, tempfile, time
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from studio_harness import EdgeSession, READY, CALM, serve

def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); ap.add_argument("--shots", default=""); a = ap.parse_args()
    site = os.path.abspath(a.site); res = []
    def ok(n, c, d=""): res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:400]})" if d and not c else ""), flush=True)
    games = json.load(open(os.path.join(site, "included", "index.json"), encoding="utf-8"))["games"]
    sha = lambda p: hashlib.sha256(open(os.path.join(site, "included", *p.split("/")), "rb").read()).hexdigest()
    ok("files: every Included Game has its recorded cover, made from its recorded source beside it", all(g.get("cover") and sha(g["cover"]["file"]) == g["cover"]["sha256"] and sha(g["cover"]["source"]) == g["cover"]["source_sha256"] and g["cover"]["source"].startswith("source/") for g in games), [g.get("cover") for g in games])
    kinds = {g["key"]: g["cover"]["kind"] for g in games}; ids = {g["key"]: "p_" + g["content_sha256"][:16] for g in games}; src = {g["key"]: "included/" + g["cover"]["file"] for g in games}
    srv = serve(site); base = f"http://127.0.0.1:{srv.server_address[1]}"
    try:
        with EdgeSession(profile=tempfile.mkdtemp(prefix="ccs-covers-p-"), width=1440, height=900) as ed:
            ed.navigate(base + "/index.html"); ed.wait_for(READY, timeout=120); ed.js(CALM); ed.wait_for("CS.starter&&CS.starter.state==='done'", timeout=90); time.sleep(0.8)
            for k, pid in ids.items():
                r = ed.js("(()=>{ const g=gameOf(%s); return g?{art:g.art, src:g.art_source, kind:g.art_kind, img:(document.querySelector('img[data-id=%s]')||{}).getAttribute?document.querySelector('img[data-id=%s]').getAttribute('src'):null, gen:!!document.querySelector('img.gen[data-id=%s]')}:null; })()" % (json.dumps(pid), json.dumps(pid), json.dumps(pid), json.dumps(pid)))
                ok(f"rows: {k} shows its author's {kinds[k]} on the card (not the generated cover)", r and r["art"] == src[k] and r["src"] == "included" and r["kind"] == kinds[k] and r["img"] == src[k] and not r["gen"], r)
            if a.shots: os.makedirs(a.shots, exist_ok=True); open(os.path.join(a.shots, "covers-collection-1440.png"), "wb").write(ed.screenshot())
            for k, pid in ids.items():
                ed.js("openDetails(%s); 1" % json.dumps(pid)); time.sleep(0.6)
                d = ed.js("({cap:($('dArtCap')||{}).textContent||'', img:(document.querySelector('#dCover img')||{}).getAttribute?document.querySelector('#dCover img').getAttribute('src'):null})")
                want = ("Title screen" if kinds[k] == "title screen" else "Game screen") + ", from the game's author"
                ok(f"Details: {k} shows the cover with the caption \"{want}\"", d["img"] == src[k] and d["cap"] == want, d)
                if a.shots and k == "thwaite": open(os.path.join(a.shots, "covers-details-1440.png"), "wb").write(ed.screenshot())
                ed.js("typeof closeDetails==='function'?closeDetails():document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape'})); 1"); time.sleep(0.3)
            r = ed.js("(()=>{ const rows=incRows(); return rows.map(x=>({id:x.game_id, art:x.art, src:x.art_source})); })()")
            ok("Game Catalog: its Included rows carry the same covers", r and all(x["art"] == src[k] and x["src"] == "included" for k in ids for x in r if x["id"] == ids[k]) and len(r) >= 0, r)
            h = ed.js("(()=>{ const m=homeModelFromStudio(); return m.games.filter(g=>/^p_/.test(g.id)).map(g=>({id:g.id, art:g.art, scene:g.scene})); })()")
            ed.js("openTv({theme:'featured-shelf', focus:%s}); 1" % json.dumps(ids["thwaite"])); time.sleep(2.0); txt = ed.js("document.body.innerText")
            ok("HOME: the side panel says one of the Included Games, and the game's own title screen, from its author", "one of the Included Games" in txt and "the game's own title screen, from its author" in txt and "Game Library box art" not in txt and "the image kept with this collection" not in txt, txt[txt.find("Selected game"):][:400])
            if a.shots: open(os.path.join(a.shots, "covers-home-1440.png"), "wb").write(ed.screenshot())
            ed.js("typeof closeTv==='function'&&closeTv(); 1"); time.sleep(0.5)
            ok("HOME: the browser HOME takes the cover as real artwork (not generated)", h and all(x["art"]["provenance"] == "authentic" and x["art"]["src"] == src[k] for k in ids for x in h if x["id"] == ids[k]) and len(h) == 2, h)
            sc = json.load(open(os.path.join(site, "art", "scenes", "index.json"), encoding="utf-8"))
            want = {ids[g["key"]]: sc["genres"].get(g.get("genre") or "", sc["fallback"]) for g in games}
            ok("HOME: each Included Game's backdrop is its genre's shared scene (" + ", ".join(f"{g['key']}: {g.get('genre')} -> {want[ids[g['key']]]}" for g in games) + ")",
               h and all(x["scene"] == want[x["id"]] for x in h) and any(v != sc["fallback"] for v in want.values()), {x["id"]: x["scene"] for x in (h or [])})
            sg = ed.js("shGames().filter(g=>/^p_/.test(g.id)).map(g=>({id:g.id, own:g.own_art, art:g.art}))")
            ok(".ccs backup: the cover is not carried as the person's own artwork (it returns by the game's identity)", sg and all(not x["own"] for x in sg), sg)
            png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="
            r = ed.js("(()=>{ const id=%s; COLL.missing=Object.assign({}, COLL.missing); COLL.missing[id]=Object.assign({}, COLL.missing[id], {thumb_url:%s}); const g=keptRow(id)||gameOf(id); return {art:g.art, src:g.art_source}; })()" % (json.dumps(ids["thwaite"]), json.dumps(png)))
            ok("own image: an image the person set wins over the cover", r and r["art"].startswith("data:image/png") and r["src"] == "kept", r)
            ok("no page errors", not ed.exceptions and not ed.console_errors, (ed.exceptions, ed.console_errors))
    finally:
        srv.shutdown(); srv.server_close()
    n = sum(1 for _, c in res if c); print(f"RESULT {n} / {len(res)}")
    if a.out: json.dump([{"check": k, "pass": c} for k, c in res], open(a.out, "w", encoding="utf-8"), indent=1)
    return 0 if res and n == len(res) else 1

if __name__ == "__main__": sys.exit(main())
