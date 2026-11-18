r"""A HOME Designer design carried by a collection, losslessly (tools/console-backup/HOME-DESIGNER.md; CCS-FORMAT.md "HOME design").

  ccs package      a collection whose HOME settings hold a Workbench design (.ccshome bytes as Base64 with its reference) and members this
                   version does not know: written, read, ONE understood setting changed (the theme), written again and read again: the
                   design (every member, its bytes) and the unknown members are exactly as they were
  studio page      the HOME model (home-model.js homeBuild), the HOME settings save (tv.js homeExtrasSave) and the collection's arrival and
                   departure (startup-custom.js suAdoptHome / suInlineHome) keep the design reference and its unknown members exactly,
                   change only what they understand, and carry the bytes out unchanged (node)
  compiled design  the package builder's ingest of a compiled design (native_home/design_ingest.py) takes the C# compiler's bytes as they
                   are and refuses a changed or extra file
  carried design   the Windows app compiles a collection's carried design through the host program's --design-compile (design_compile.py)
                   to exactly the bytes the Workbench compiles; a design the console cannot show stops the build with its reason in words
                   (never left out); the package and the CCS Mod and CCS Nucleus console packages carry the same compiled design and startup
                   picture, and their sessions name the same design folder on the console; a .ccs keeps the design and the startup picture
Environment (set by the engine tests, group "designer"): CCS_TEST_CCSHOME, a .ccshome the C# HomeDesign wrote; CCS_TEST_COMPILED, a folder
the C# DesignCompiler wrote; CCS_DESIGN_COMPILER, CCS_TEST_CCSHOME_MOVED, CCS_TEST_GAMES, CCS_TEST_COMPILED_MOVED, CCS_TEST_CCSHOME_REFUSED,
NES_STUDIO_SITE, CCS_TEST_PLAYER (class CarriedDesign). Without them, synthetic bytes stand in where they can, and the rest is skipped.
  python test_home_design_carry.py      Exit 0 = PASS."""
from __future__ import annotations
import base64, hashlib, json, os, shutil, subprocess, sys, tempfile, unittest
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path[:0] = [HERE, os.path.join(HERE, "..", "rom-pipeline")]
import ccs_package as cp, design_embed as DE
import test_ccs_package as T
STUDIO = os.path.join(HERE, "..", "rom-pipeline", "studio")

def design_bytes() -> bytes:
    p = os.environ.get("CCS_TEST_CCSHOME")
    return open(p, "rb").read() if p and os.path.isfile(p) else b"PK\x03\x04" + hashlib.shake_256(b"synthetic design").digest(70000)

class CcsPackage(unittest.TestCase):
    def setUp(self): self.tmp = tempfile.mkdtemp(prefix="ccs-design-carry-")
    def tearDown(self): shutil.rmtree(self.tmp, ignore_errors=True)
    def test_round_trip_keeps_the_design_and_unknown_members(self):
        b = design_bytes(); rom, _ = T.make_rom("carry-1"); ident = cp.identify_bytes(rom); vid = ident["content_sha256"][:16]
        rp = os.path.join(self.tmp, "g.nes")
        with open(rp, "wb") as f: f.write(rom)
        d = DE.embed(b, id="0f1e2d3c4b5a69788796a5b4c3d2e1f0", revision=3, name="Neon Night", theme="arcade-grid", content_sha256="ab" * 32, optional={"glow": {"strength": 3}})
        coll = T.collection([vid]); coll["ccs_collection"]["home"] = {"v": 1, "theme": "arcade-grid", "startup_image": "random", "design": d, "x_future_setting": {"tilt": [1, 2, 3]}}
        p1 = os.path.join(self.tmp, "one.ccs"); cp.write(p1, collection=coll, roms=[{"version_id": vid, "path": rp, "title": "Game"}])
        r1 = cp.inspect(p1); self.assertTrue(r1["ok"], r1["problems"]); h1 = r1["data"]["ccs_collection"]["home"]
        self.assertEqual(h1["design"], d); got, why = DE.check(h1["design"]); self.assertIsNone(why); self.assertEqual(got, b)
        h1["theme"] = "box-art-showcase"   # one setting this program understands, changed
        p2 = os.path.join(self.tmp, "two.ccs"); cp.write(p2, collection=r1["data"], roms=[{"version_id": vid, "path": rp, "title": "Game"}])
        h2 = cp.inspect(p2)["data"]["ccs_collection"]["home"]
        self.assertEqual(h2["theme"], "box-art-showcase"); self.assertEqual(h2["design"], d); self.assertEqual(h2["x_future_setting"], {"tilt": [1, 2, 3]})
        got2, why2 = DE.check(h2["design"]); self.assertIsNone(why2); self.assertEqual(hashlib.sha256(got2).hexdigest(), hashlib.sha256(b).hexdigest())
        if os.environ.get("CCS_TEST_CARRIED_OUT"):
            with open(os.environ["CCS_TEST_CARRIED_OUT"], "wb") as f: f.write(got2)   # the engine test opens these bytes with HomeDesign
    def test_damaged_or_foreign_designs_are_named_not_used(self):
        d = DE.embed(design_bytes(), name="x"); bad = dict(d, data=base64.b64encode(b"PK\x03\x04something else").decode())
        self.assertIn("fingerprint", DE.check(bad)[1]); self.assertIn("not with the collection", DE.check(DE.reference(d))[1]); self.assertIsNone(DE.reference({"format": "other"}))
        self.assertEqual({k: v for k, v in d.items() if k != "data"}, DE.reference(d))

NODE = r"""
const vm=require('vm'), fs=require('fs'), path=require('path'), S=process.argv[1], B64=fs.readFileSync(process.argv[2],'utf8'), SHA=process.argv[3];
const store={}; const localStorage={getItem:k=>k in store?store[k]:null, setItem:(k,v)=>{store[k]=String(v);}, removeItem:k=>{delete store[k];}, key:i=>Object.keys(store)[i], get length(){return Object.keys(store).length;}};
const ctx=vm.createContext({console, localStorage, TV:{model:null, S:{sel:0}}, btoa:s=>Buffer.from(s,'binary').toString('base64'), atob:s=>Buffer.from(s,'base64').toString('binary')});
vm.runInContext(fs.readFileSync(path.join(S,'home-model.js'),'utf8'), ctx);
vm.runInContext(fs.readFileSync(path.join(S,'startup-custom.js'),'utf8'), ctx);
const tv=fs.readFileSync(path.join(S,'tv.js'),'utf8'); const fn=tv.match(/function homeExtrasSave\(\)\{[\s\S]*?\n(?=\/\/ another tab saved)/)[0];
vm.runInContext('const HOME_KEY="ccs-home-test"; function homeReconcileHistory(a,b){return b;} function homeReconcileFavorites(a,b){return b;}\n'+fn, ctx);
const ref={format:'ccs-home-design-embed/1', sha256:SHA, id:'0f1e2d3c4b5a69788796a5b4c3d2e1f0', revision:3, name:'Neon Night', theme:'arcade-grid', optional:{glow:{strength:3}, later:[1,'two',null]}};
const prev={v:1, theme:'box-art-showcase', startup_image:'random', design:ref, x_future_setting:{tilt:[1,2,3]}, folders:[], favorites:{items:{}}};
const out={};
const m=vm.runInContext('homeBuild', ctx)({schema:'nes-classic-collection/4', selections:[]}, {}, prev);
out.model_keeps=JSON.stringify(m.design)===JSON.stringify(ref);
store['ccs-home-test']=JSON.stringify(prev); ctx.TV.model=m; m.theme='arcade-grid'; vm.runInContext('homeExtrasSave()', ctx);
const saved=JSON.parse(store['ccs-home-test']); out.save_theme=saved.theme; out.save_keeps=JSON.stringify(saved.design)===JSON.stringify(ref)&&JSON.stringify(saved.x_future_setting)==='{"tilt":[1,2,3]}';
vm.runInContext('SU.quiet=1', ctx); const adopted=vm.runInContext('suAdoptHome', ctx)(Object.assign({}, saved, {design:Object.assign({}, ref, {data:B64})}), 'Test');
out.adopt_drops_bytes_only=!('data' in adopted.design)&&JSON.stringify(adopted.design)===JSON.stringify(ref);
vm.runInContext('SU.b64.set('+JSON.stringify(SHA)+', '+JSON.stringify(B64)+')', ctx);
const inl=vm.runInContext('suInlineHome', ctx)(adopted, false);
out.inline_bytes=inl.design.data===B64; out.inline_keeps=JSON.stringify(Object.assign({}, inl.design, {data:undefined}))===JSON.stringify(Object.assign({}, ref, {data:undefined}));
out.inline_other=JSON.stringify(inl.x_future_setting)==='{"tilt":[1,2,3]}';
const bad=vm.runInContext('suAdoptHome', ctx)({design:{format:'ccs-home-design-embed/1', sha256:'nothex', name:'x', data:'AAAA'}}, 'Test');
out.bad_named=typeof bad.design.problem==='string'&&!('data' in bad.design)&&bad.design.name==='x';
console.log(JSON.stringify(out));
"""

@unittest.skipUnless(shutil.which("node"), "node is not installed")
class StudioPage(unittest.TestCase):
    def test_the_page_keeps_the_design_and_unknown_members(self):
        b = design_bytes(); sha = hashlib.sha256(b).hexdigest(); fd, bp = tempfile.mkstemp(suffix=".b64"); os.write(fd, base64.b64encode(b)); os.close(fd)
        try: r = subprocess.run([shutil.which("node"), "-e", NODE, STUDIO, bp, sha], capture_output=True, text=True, timeout=120)
        finally: os.remove(bp)
        self.assertEqual(r.returncode, 0, r.stderr[-800:]); out = json.loads(r.stdout.strip().splitlines()[-1])
        self.assertEqual(out, {"model_keeps": True, "save_theme": "arcade-grid", "save_keeps": True, "adopt_drops_bytes_only": True, "inline_bytes": True, "inline_keeps": True, "inline_other": True, "bad_named": True})

class CompiledIngest(unittest.TestCase):
    def test_the_package_builder_takes_the_compilers_bytes(self):
        from native_home import design_ingest as DI
        src = os.environ.get("CCS_TEST_COMPILED")
        if not (src and os.path.isfile(os.path.join(src, "design.json"))): self.skipTest("no compiled design given (CCS_TEST_COMPILED; the engine tests, group designer, give one)")
        d = DI.read(src); self.assertEqual(d["id"], hashlib.sha256(open(os.path.join(src, "design.json"), "rb").read()).hexdigest())
        for m in d["manifest"]: self.assertEqual(hashlib.sha256(d["files"][m["name"]]).hexdigest(), m["sha256"])
        tmp = tempfile.mkdtemp(prefix="ccs-design-ingest-");
        try:
            cp2 = os.path.join(tmp, "c"); shutil.copytree(src, cp2); img = next(n for n in d["files"] if n.startswith("img/")); p = os.path.join(cp2, *img.split("/"))
            b = bytearray(open(p, "rb").read()); b[len(b) // 2] ^= 0x5A
            with open(p, "wb") as f: f.write(bytes(b))
            with self.assertRaises(DI.DesignError): DI.read(cp2)
            cp3 = os.path.join(tmp, "e"); shutil.copytree(src, cp3)
            with open(os.path.join(cp3, "img", "notes.txt"), "w") as f: f.write("x")
            with self.assertRaises(DI.DesignError): DI.read(cp3)
        finally: shutil.rmtree(tmp, ignore_errors=True)

def _env(*names):
    v = [os.environ.get(n) for n in names]
    return v if all(v) and all(os.path.exists(x) for x in v) else None

def _tree(d: str) -> dict:
    out = {}
    for r, _, fs in os.walk(d):
        for f in fs: p = os.path.join(r, f); out[os.path.relpath(p, d).replace(os.sep, "/")] = hashlib.sha256(open(p, "rb").read()).hexdigest()
    return out

def _jpeg(color=(30, 90, 160)) -> bytes:
    import io
    from PIL import Image
    im = Image.new("RGB", (1280, 720), color); b = io.BytesIO(); im.save(b, "JPEG", quality=88); return b.getvalue()

def _startup(b: bytes) -> dict:
    sha = hashlib.sha256(b).hexdigest()
    return {"id": "custom-" + sha[:16], "sha256": sha, "name": "My picture", "fit": "fit", "bytes": len(b), "width": 1280, "height": 720, "data": base64.b64encode(b).decode("ascii"), "mime": "image/jpeg"}

class CarriedDesign(unittest.TestCase):
    """the Windows app applies a collection's carried design through the one C# compiler (native_home/design_compile.py): the same bytes as
    the Workbench, a refusal in words when it cannot be shown, never a design silently left out. Environment from the engine tests (group
    designer): CCS_DESIGN_COMPILER (a host built from this source that answers --design-compile), CCS_TEST_CCSHOME_MOVED (a design that
    moves the HOME's controls), CCS_TEST_GAMES (the games it was compiled for), CCS_TEST_COMPILED_MOVED (the Workbench's compiled folder for
    them), CCS_TEST_CCSHOME_REFUSED (a design the console cannot show), NES_STUDIO_SITE (the built site, for a package) and CCS_TEST_PLAYER
    (the console game player, for the CCS Mod and CCS Nucleus console packages)"""
    def setUp(self): self.tmp = tempfile.mkdtemp(prefix="ccs-design-compile-")
    def tearDown(self): shutil.rmtree(self.tmp, ignore_errors=True)

    def test_the_app_compiles_the_carried_design_to_the_workbenchs_bytes(self):
        e = _env("CCS_DESIGN_COMPILER", "CCS_TEST_CCSHOME_MOVED", "CCS_TEST_GAMES", "CCS_TEST_COMPILED_MOVED")
        if not e: self.skipTest("no compiler or moved design given (the engine tests, group designer, give them)")
        from native_home import design_compile as DC
        games = json.load(open(e[2], encoding="utf-8"))["games"]; out = os.path.join(self.tmp, "c")
        r = DC.compile_carried(DE.embed(open(e[1], "rb").read(), name="Moved"), games, out)
        self.assertEqual(_tree(out), _tree(e[3])); self.assertEqual(r["schema_name"], "ccs-home-design-compiled/2")
        self.assertEqual(r["id"], hashlib.sha256(open(os.path.join(e[3], "design.json"), "rb").read()).hexdigest())

    def test_a_design_the_console_cannot_show_is_refused_in_words(self):
        from native_home import design_compile as DC
        with self.assertRaises(DC.CompileRefused) as c0: DC.compile_carried(DE.reference(DE.embed(design_bytes(), name="Lost")), [], os.path.join(self.tmp, "x"))
        self.assertIn("not with the collection", str(c0.exception))
        e = _env("CCS_DESIGN_COMPILER", "CCS_TEST_CCSHOME_REFUSED")
        if not e: self.skipTest("no compiler or refused design given")
        with self.assertRaises(DC.CompileRefused) as c1: DC.compile_carried(DE.embed(open(e[1], "rb").read(), name="Too Big"), [{"id": "0123456789abcdef", "title": "x"}], os.path.join(self.tmp, "y"))
        m = str(c1.exception); self.assertIn('The HOME design "Too Big" cannot be used on the console', m); self.assertIn("HOME Designer", m); self.assertIn("keeps", m)
        self.assertFalse(os.path.exists(os.path.join(self.tmp, "y", "design.json")))
        newer = os.environ.get("CCS_TEST_CCSHOME_NEWER")   # a required capability this version does not have: inspect only, never applied or downgraded
        if newer and os.path.isfile(newer):
            with self.assertRaises(DC.CompileRefused) as c2: DC.compile_carried(DE.embed(open(newer, "rb").read(), name="Future"), [{"id": "0123456789abcdef", "title": "x"}], os.path.join(self.tmp, "z"))
            m2 = str(c2.exception); self.assertIn("newer version", m2); self.assertIn("Update Classic Cart Studio", m2); self.assertIn("home-design/9", m2)

    def test_the_package_builder_carries_the_compiled_design_into_both_methods(self):
        e = _env("CCS_DESIGN_COMPILER", "CCS_TEST_CCSHOME_MOVED", "CCS_TEST_GAMES", "CCS_TEST_COMPILED_MOVED", "NES_STUDIO_SITE", "CCS_TEST_CCSHOME_REFUSED")
        if not e: self.skipTest("no compiler, design or built site given")
        import test_native_home_package as TN
        from native_home import package as P, console_package as CP
        ids = [g["id"] for g in json.load(open(e[2], encoding="utf-8"))["games"]]; man, home = TN.studio_config(ids)
        jp = _jpeg(); home["design"] = DE.embed(open(e[1], "rb").read(), name="Moved"); home["startup_custom"] = _startup(jp); home["startup_image"] = home["startup_custom"]["id"]
        r = P.build(man, home, mode="deployment", root=os.path.join(self.tmp, "pk"), cache_root=os.environ.get("CCS_TEST_PKCACHE"))
        self.assertTrue(r.get("ok"), r.get("error")); pd = os.path.join(r["package_dir"], "design")
        self.assertEqual(_tree(pd), _tree(e[3])); pk = json.load(open(os.path.join(r["package_dir"], "pack.json"), encoding="utf-8"))
        self.assertEqual(pk["design"]["id"], r["design"]["id"]); self.assertIn("ccs-home-design-compiled/2", pk["design_schemas"])
        # a design that cannot be shown stops the build with its reason; it is never left out
        h2 = dict(home); h2["design"] = DE.embed(open(e[5], "rb").read(), name="Too Big")
        r2 = P.build(man, h2, mode="deployment", root=os.path.join(self.tmp, "pk2"), cache_root=os.environ.get("CCS_TEST_PKCACHE"))
        self.assertFalse(r2.get("ok")); self.assertEqual(r2.get("status"), "design"); self.assertIn("HOME Designer", r2.get("error") or "")
        player = os.environ.get("CCS_TEST_PLAYER")
        if not (player and os.path.isfile(player)): self.skipTest("no console game player given (CCS_TEST_PLAYER): the package part passed")
        no_rom = lambda vid, title, ctx: {"playable": False, "code": "test", "reason": "no game file in this test"}
        trees, sess = {}, {}
        for layout in ("mod", "nucleus"):   # CCS Mod and CCS Nucleus: the same HOME package, design and startup picture; the same design folder on the console
            out = os.path.join(self.tmp, layout); c = CP.build_console(man, home, out, layout=layout, rom_resolver=no_rom, player=player, pack_dir=r["package_dir"], created_utc="2026-11-16T00:00:00Z")
            self.assertTrue(c.get("ok"), c.get("error")); self.assertTrue(CP.verify(out)["ok"])
            trees[layout] = {k: v for k, v in _tree(out).items() if k.startswith("home/design/") or k.startswith("startup/")}
            s = json.load(open(os.path.join(out, "session.json"), encoding="utf-8")); sess[layout] = s["home"]["design_root"]
            st = json.load(open(os.path.join(out, "startup", "startup.json"), encoding="utf-8")); self.assertEqual(st["choice"], home["startup_image"]); self.assertEqual(st["images"][0]["id"], home["startup_image"])
            mf = json.load(open(os.path.join(out, "MANIFEST.json"), encoding="utf-8")); self.assertEqual(mf["startup"]["custom"]["source_sha256"], hashlib.sha256(jp).hexdigest())
        self.assertEqual(trees["mod"], trees["nucleus"]); self.assertEqual(sess["mod"], sess["nucleus"]); self.assertEqual(sess["mod"], "/var/lib/ccs/data/home-design")
        # the app stages CCS Nucleus's layout from CCS Mod's package (derive_layout): byte for byte the package the builder makes for it
        dv = CP.derive_layout(os.path.join(self.tmp, "mod"), os.path.join(self.tmp, "derived"), "nucleus"); self.assertTrue(dv.get("ok"), dv.get("error"))
        self.assertEqual(_tree(os.path.join(self.tmp, "derived")), _tree(os.path.join(self.tmp, "nucleus")))
        self.assertEqual({k[len("home/design/"):]: v for k, v in trees["mod"].items() if k.startswith("home/design/")}, _tree(e[3]))

    def test_a_ccs_file_keeps_the_design_and_the_startup_picture(self):
        b = design_bytes(); jp = _jpeg((90, 30, 60)); rom, _ = T.make_rom("carry-2"); vid = cp.identify_bytes(rom)["content_sha256"][:16]
        rp = os.path.join(self.tmp, "g.nes")
        with open(rp, "wb") as f: f.write(rom)
        d = DE.embed(b, id="0f1e2d3c4b5a69788796a5b4c3d2e1f0", revision=2, name="Neon Night", theme="arcade-grid"); su = _startup(jp)
        coll = T.collection([vid]); coll["ccs_collection"]["home"] = {"v": 1, "theme": "arcade-grid", "startup_image": su["id"], "startup_custom": su, "design": d}
        p1 = os.path.join(self.tmp, "a.ccs"); cp.write(p1, collection=coll, roms=[{"version_id": vid, "path": rp, "title": "Game"}])
        data = cp.inspect(p1)["data"]; data["ccs_collection"]["home"]["theme"] = "featured-shelf"   # one understood setting changed
        p2 = os.path.join(self.tmp, "b.ccs"); cp.write(p2, collection=data, roms=[{"version_id": vid, "path": rp, "title": "Game"}])
        h2 = cp.inspect(p2)["data"]["ccs_collection"]["home"]
        self.assertEqual(h2["design"], d); self.assertEqual(h2["startup_custom"], su); self.assertEqual(h2["theme"], "featured-shelf")
        self.assertEqual(base64.b64decode(h2["startup_custom"]["data"]), jp); self.assertEqual(DE.check(h2["design"])[0], b)

if __name__ == "__main__":
    r = unittest.main(exit=False, verbosity=2).result
    print(f"RESULT {r.testsRun - len(r.failures) - len(r.errors) - len(r.skipped)}/{r.testsRun} passed, {len(r.skipped)} skipped"); sys.exit(1 if r.failures or r.errors else 0)
