r"""The platform registry on the built website, headless Edge, one fresh profile. Synthetic files only.
  vectors   tools/platform/target-vectors.json decided inside the real page (every part of it loaded) exactly as the Windows app's backend
            (platform_ids.py) decides them; the page's own ccsName (a backup's file name, ccs.js) is untouched by the registry's helpers
  .ccs      the page's package reader (ccsInspect): a package without ccs_target, and one with an explicit NES target, open; one whose
            manifest or collection names SNES, or whose game names SNES, is refused with the plain reason
  saves     the page's Game Saves reader (gxRead): the same for a Game Saves file and for a game in it
  python test_platform_ids_web.py <built site folder> [--out results.json]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, base64, hashlib, json, os, sys, tempfile, zipfile
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path[:0] = [HERE, os.path.join(HERE, "..", "companion")]
from studio_harness import EdgeSession, READY, serve
import platform_ids as P
import test_ccs_package as TCP
VECTORS = json.load(open(os.path.join(HERE, "..", "platform", "target-vectors.json"), encoding="utf-8"))["vectors"]
SNES = {"schema": "ccs-target/1", "game_system": "snes"}

def decide_py(v):
    if "request" in v: return P.check_request(v["request"])
    doc = v["doc"]; return P.resolve_target(doc, v["doc_kind"], *([doc.get(v["entries"])] if v.get("entries") else []))

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); a = ap.parse_args()
    srv = serve(a.site); url = f"http://127.0.0.1:{srv.server_address[1]}/index.html"; res = []; tmp = tempfile.mkdtemp(prefix="ccs-pfweb-")
    def ok(n, c, d=""):
        res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:300]})" if d and not c else ""), flush=True)
    def package(name, man_patch=lambda m: m, coll_patch=lambda c: c):
        rom, sha = TCP.make_rom("platform-web"); vid = sha[:16]
        return open(TCP.craft(os.path.join(tmp, name), coll_patch(TCP.collection([vid])), {vid: rom}, man_patch=man_patch), "rb").read()
    def saves(man_extra=None, game_extra=None):
        sha = hashlib.sha256(b"synthetic game").hexdigest(); cart = b"\x5a" * 8192
        game = dict({"content_sha256": sha, "title": "T", "files": [{"name": "cart.sav", "bytes": len(cart), "sha256": hashlib.sha256(cart).hexdigest(), "kind": "cart"}]}, **(game_extra or {}))
        man = dict({"schema": "ccs-game-saves/1", "app": "test", "made": "2026-11-15T00:00:00Z", "platform": "web-wasm32", "core": "fceumm ac0d00a7", "games": [game]}, **(man_extra or {}))
        p = os.path.join(tmp, "saves-%d.zip" % len(os.listdir(tmp)))
        with zipfile.ZipFile(p, "w", zipfile.ZIP_DEFLATED) as z: z.writestr("ccs-game-saves.json", json.dumps(man)); z.writestr(f"saves/{sha}/cart.sav", cart)
        return open(p, "rb").read()
    def read_in_page(ed, fn, name, b):
        ed.js(f"window.__f=new File([Uint8Array.from(atob('{base64.b64encode(b).decode()}'), c=>c.charCodeAt(0))], {json.dumps(name)}); 1")
        return json.loads(ed.js(f"{fn}(window.__f).then(r=>JSON.stringify({{ok:true}}), e=>JSON.stringify({{ok:false, text:String(e&&e.message||e)}}))"))
    try:
        with EdgeSession(profile=tempfile.mkdtemp(prefix="ccs-pfweb-profile-"), width=1280, height=800) as ed:
            ed.navigate(url); ed.wait_for(READY, timeout=300)
            # ---- the shared vectors, in the page
            js = ("(()=>{ const V=JSON.parse(" + json.dumps(json.dumps(VECTORS)) + "); return JSON.stringify(V.map(v=>{ let d; if(v.request) d=ccsTargetRequest(v.request); "
                  "else { const doc=JSON.parse(JSON.stringify(v.doc)); d=ccsTargetResolve(doc, v.doc_kind, ...(v.entries?[doc[v.entries]]:[])); } return [d.ok,d.code,d.legacy,d.target]; })); })()")
            got = json.loads(ed.js(js)); bad = []
            for v, g in zip(VECTORS, got):
                p = decide_py(v)
                if g != [p["ok"], p["code"], p["legacy"], p["target"]] or g[1] != v["expect"]: bad.append((v["name"], g[1]))
            ok(f"vectors: {len(VECTORS) - len(bad)}/{len(VECTORS)} decided in the page as in the backend", len(VECTORS) >= 30 and not bad, bad)
            ok("the page's own ccsName (two arguments, ccs.js) is the one in use; the registry's string check is its own", ed.js("ccsName.length===2&&typeof ccsPfName==='function'&&ccsPfName.length===1"))
            ok("no reserved entry is allowed in the page", ed.js("!ccsAllowed('game-system','snes')&&!ccsAllowed('capability','snes-gameplay')&&!ccsAllowed('boot-option','ccs-snes')&&ccsAllowed('game-system','nes')"))
            # ---- .ccs packages
            r = read_in_page(ed, "ccsInspect", "nes.ccs", package("nes.ccs"))
            ok(".ccs: a package without ccs_target opens (NES, as before)", r["ok"], r)
            r = read_in_page(ed, "ccsInspect", "nes-explicit.ccs", package("nes-explicit.ccs", man_patch=lambda m: dict(m, ccs_target={"schema": "ccs-target/1", "game_system": "nes", "core": "fceumm"})))
            ok(".ccs: an explicit NES target opens", r["ok"], r)
            for what, kw in (("the manifest names SNES", {"man_patch": lambda m: dict(m, ccs_target=SNES)}),
                             ("the collection names SNES", {"coll_patch": lambda c: dict(c, ccs_target=SNES)}),
                             ("a game in it names SNES", {"coll_patch": lambda c: dict(c, selections=[dict(s, game_system="snes") for s in c["selections"]])}),
                             ("the manifest names the reserved core", {"man_patch": lambda m: dict(m, ccs_target={"schema": "ccs-target/1", "core": "snes-unselected"})})):
                r = read_in_page(ed, "ccsInspect", "x.ccs", package("x-%d.ccs" % len(os.listdir(tmp)), **kw))
                ok(f".ccs: {what}: refused with the plain reason", not r["ok"] and r.get("text") == P.DOCUMENT_TEXT, r)
            # ---- Game Saves files
            r = read_in_page(ed, "gxRead", "saves.zip", saves())
            ok("saves: a Game Saves file without ccs_target reads (NES, as before)", r["ok"], r)
            r = read_in_page(ed, "gxRead", "saves.zip", saves(man_extra={"ccs_target": SNES}))
            ok("saves: a Game Saves file that names SNES: refused with the plain reason", not r["ok"] and r.get("text") == P.DOCUMENT_TEXT, r)
            r = read_in_page(ed, "gxRead", "saves.zip", saves(game_extra={"game_system": "snes"}))
            ok("saves: a game in it that names SNES: refused with the plain reason", not r["ok"] and r.get("text") == P.DOCUMENT_TEXT, r)
            ok("no page errors", not ed.exceptions, ed.exceptions[:2])
    finally:
        srv.shutdown()
    fails = [n for n, c in res if not c]
    print(f"\n{len(res) - len(fails)}/{len(res)} PASS" + (f"; FAIL: {', '.join(fails)}" if fails else ""))
    if a.out: json.dump({"results": [{"name": n, "pass": c} for n, c in res]}, open(a.out, "w", encoding="utf-8"), indent=1)
    return 0 if not fails else 1

if __name__ == "__main__": sys.exit(main())
