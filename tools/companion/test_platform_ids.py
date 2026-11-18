r"""The platform registry and its boundaries in the Windows app's backend and on the website (tools/console-backup/SNES-GROUNDWORK.md).
  registry      platform_ids.py carries tools/platform/ccs-platform-registry.json byte for byte; what it hands out is a copy
  vectors       tools/platform/target-vectors.json decided as expected here, and (with Node.js) by the website's platform-ids.js as
                build_html.py builds it: the same decisions in both (the C# engine runs the same vectors: CcsBackup.Tests --only platform)
  NES unchanged files written now carry no ccs_target (.ccs packages, Game Saves files); files without one, and files with an explicit
                NES target, read exactly as before
  boundaries    a .ccs package, a collection (HOME configuration, the page's handoff), a console package plan, a Game Saves file and a
                worker request that name the reserved SNES system, core, model, provider or capability are refused with the plain reason
  inputs        the NES controller definition is the eight actions the players already use, in their order
  build         only the supported core is vendored and pinned as the registry says; no other core, nothing for the reserved one
  no promise    no page, window or Help text promises SNES; the recovery programs have no input that names a system or provider
  python tools/companion/test_platform_ids.py      Exit 0 = PASS (a missing Node.js skips only the website half, and says so)."""
from __future__ import annotations
import hashlib, io, json, os, re, shutil, subprocess, sys, tempfile, unittest, zipfile
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path[:0] = [HERE, os.path.join(HERE, "..", "rom-pipeline")]
import platform_ids as P
import ccs_package as cp
import test_ccs_package as TCP
PLATFORM = os.path.join(REPO, "tools", "platform"); STUDIO = os.path.join(REPO, "tools", "rom-pipeline", "studio")
VECTORS = json.load(open(os.path.join(PLATFORM, "target-vectors.json"), encoding="utf-8"))["vectors"]
SNES_TARGET = {"schema": "ccs-target/1", "game_system": "snes"}

def decide_py(v: dict) -> dict:
    if "request" in v: return P.check_request(v["request"])
    doc = v["doc"]; lists = [doc.get(v["entries"])] if v.get("entries") else []
    return P.resolve_target(doc, v["doc_kind"], *lists)

def expected(v: dict, d: dict) -> bool:
    ok = d["code"] == v["expect"] and d["ok"] == (v["expect"] == "ok")
    if ok and "legacy" in v: ok = d["legacy"] == v["legacy"]
    if ok and "target" in v: ok = d["target"] == v["target"]
    if ok and not d["ok"]: ok = bool(d["text"]) and "snes" not in d["text"].lower()
    return ok

def platform_js() -> str:
    """platform-ids.js exactly as build_html.py puts it into the page (its own _platform_registry, run from its source)"""
    import ast
    src = open(os.path.join(REPO, "tools", "rom-pipeline", "build_html.py"), encoding="utf-8").read()
    fn = next(n for n in ast.parse(src).body if isinstance(n, ast.FunctionDef) and n.name == "_platform_registry")
    ns = {"os": os, "json": json, "__file__": os.path.join(REPO, "tools", "rom-pipeline", "build_html.py")}
    exec(compile(ast.Module(body=[fn], type_ignores=[]), "build_html._platform_registry", "exec"), ns)
    assert "part(\"platform-ids.js\").replace(\"__PLATFORM__\", _platform_registry())" in src, "build_html.py no longer builds platform-ids.js into the page"
    return open(os.path.join(STUDIO, "platform-ids.js"), encoding="utf-8").read().replace("__PLATFORM__", ns["_platform_registry"]())

class T(unittest.TestCase):
    def setUp(self): self.tmp = tempfile.mkdtemp(prefix="ccs-platform-")
    def tearDown(self): shutil.rmtree(self.tmp, ignore_errors=True)

    # ---- the registry
    def test_registry_is_the_file(self):
        raw = open(os.path.join(PLATFORM, "ccs-platform-registry.json"), "rb").read()
        self.assertEqual(P.REGISTRY_TEXT.encode("utf-8"), raw)
        self.assertEqual(P.registry_sha256(), hashlib.sha256(raw).hexdigest())

    def test_registry_cannot_be_changed_through_what_it_hands_out(self):
        e = P.entry("game-system", "snes"); e["status"] = "supported"
        lst = P.entries(); [x.update(status="supported") for x in lst]
        self.assertFalse(P.allowed("game-system", "snes")); self.assertFalse(P.allowed("capability", "snes-gameplay"))
        self.assertEqual(P.supported_ids("game-system"), ["nes"]); self.assertEqual(P.supported_ids("core"), ["fceumm"]); self.assertEqual(P.supported_ids("physical-model"), ["nes-classic-edition"])
        os.environ["CCS_ENABLE_SNES"] = "1"
        try: self.assertFalse(P.allowed("game-system", "snes"), "no environment variable opens a reserved entry")
        finally: os.environ.pop("CCS_ENABLE_SNES", None)
        self.assertTrue(all(not P.allowed(x["kind"], x["id"]) for x in P.entries() if x["status"] == "reserved"))

    def test_vectors_python(self):
        bad = [(v["name"], decide_py(v)["code"]) for v in VECTORS if not expected(v, decide_py(v))]
        self.assertGreaterEqual(len(VECTORS), 30); self.assertEqual(bad, [])

    def test_vectors_website(self):
        node = shutil.which("node")
        if not node: self.skipTest("Node.js is not installed: the website half of the shared vectors was NOT run")
        js = platform_js() + "\n;const V=JSON.parse(" + json.dumps(json.dumps(VECTORS)) + ");\nconst out=V.map(v=>{ let d; if(v.request) d=ccsTargetRequest(v.request); else { const doc=JSON.parse(JSON.stringify(v.doc)); d=ccsTargetResolve(doc, v.doc_kind, ...(v.entries?[doc[v.entries]]:[])); } return {name:v.name, ok:d.ok, code:d.code, legacy:d.legacy, target:d.target, text:d.text}; });\n" \
             "out.push({name:'__inputs', actions:ccsInputActions('nes-pad'), snes:ccsInputActions('snes-pad'), allowedSnes:ccsAllowed('game-system','snes'), board:ccsModelForBoard('dp-nes'), board2:ccsModelForBoard('dp-shvc')});\nprocess.stdout.write(JSON.stringify(out));\n"
        # the vectors are JSON.parse'd, as the page reads every file: a "__proto__" key stays an own key there
        f = os.path.join(self.tmp, "vectors.js"); open(f, "w", encoding="utf-8").write(js)
        r = subprocess.run([node, f], capture_output=True, text=True, encoding="utf-8", timeout=60)
        self.assertEqual(r.returncode, 0, r.stderr[-800:])
        got = json.loads(r.stdout); extra = got.pop()
        bad = [g["name"] + " -> " + str(g["code"]) for v, g in zip(VECTORS, got) if not expected(v, g)]
        self.assertEqual(bad, [])
        for v, g in zip(VECTORS, got):   # the very same decision as here, field by field
            p = decide_py(v); self.assertEqual((g["ok"], g["code"], g["legacy"], g["target"]), (p["ok"], p["code"], p["legacy"], p["target"]), v["name"])
        self.assertEqual(extra["actions"], ["up", "down", "left", "right", "a", "b", "select", "start"]); self.assertEqual(len(extra["snes"]), 12)
        self.assertFalse(extra["allowedSnes"]); self.assertEqual(extra["board"], "nes-classic-edition"); self.assertIsNone(extra["board2"])

    def test_page_readers_ask_the_registry(self):
        """each page reader of a file that can carry ccs_target calls the shared rule before using the file (the rule itself: test_vectors_website)"""
        want = {"ccs.js": ["ccsTargetResolve(man,'ccs-package')", "ccsTargetResolve(data,'collection',data.selections,data.games,data.personal_library)"],
                "studio.js": ["ccsTargetResolve(m,'collection',m.selections,m.games,m.personal_library)"],
                "collections.js": ["ccsTargetResolve(obj,'collection',obj.selections,obj.games,obj.personal_library)"],
                "saves-transfer.js": ["ccsTargetResolve(man,'game-saves',man.games)"]}
        for f, calls in want.items():
            t = open(os.path.join(STUDIO, f), encoding="utf-8").read()
            for c in calls: self.assertIn(c, t, f)
        js = platform_js(); self.assertNotIn("__PLATFORM__", js); self.assertNotIn("localStorage", js); self.assertNotIn("location", js)
        # the page is one script: a name declared here and again in another part would silently replace one of them
        mine = re.findall(r"^function\s+([A-Za-z_$][\w$]*)", open(os.path.join(STUDIO, "platform-ids.js"), encoding="utf-8").read(), re.M)
        self.assertGreaterEqual(len(mine), 10)
        clash = []
        for f in sorted(os.listdir(STUDIO)):
            if not f.endswith(".js") or f == "platform-ids.js": continue
            t = open(os.path.join(STUDIO, f), encoding="utf-8").read()
            clash += [f"{n} in {f}" for n in mine if re.search(r"\bfunction\s+%s\b|\b(?:const|let|var|class)\s+%s\b|,\s*%s\s*=[^=]" % (n, n, n), t)]
        self.assertEqual(clash, [], "a platform-ids.js name is declared again in another part of the page")

    # ---- inputs: the NES definition is what the players already use
    def test_nes_input_definition_is_the_existing_actions(self):
        want = P.input_actions("nes-pad"); self.assertEqual(want, ["up", "down", "left", "right", "a", "b", "select", "start"])
        cp_js = open(os.path.join(STUDIO, "ccs-play.js"), encoding="utf-8").read(); play_js = open(os.path.join(STUDIO, "play.js"), encoding="utf-8").read()
        m = re.search(r"const CP_ACT=\[([^\]]*)\]", cp_js); self.assertEqual([x.strip(" '\"") for x in m.group(1).split(",")], want)
        m = re.search(r"const PLAY_ACTIONS=\[(.*?)\];", play_js); self.assertEqual(re.findall(r"\['([a-z]+)',", m.group(1)), want)
        snes = P.input_actions("snes-pad"); self.assertTrue(set(want) <= set(snes) and {"x", "y", "l", "r"} <= set(snes)); self.assertFalse(P.allowed("input", "snes-pad"))

    # ---- NES files: unchanged, and read as before
    def _package(self, man_patch=lambda m: m, coll_patch=lambda c: c) -> str:
        rom, sha = TCP.make_rom("platform-a"); vid = sha[:16]
        return TCP.craft(os.path.join(self.tmp, "p-%d.ccs" % len(os.listdir(self.tmp))), coll_patch(TCP.collection([vid])), {vid: rom}, man_patch=man_patch)

    def test_ccs_package_nes_unchanged_and_snes_refused(self):
        rom, sha = TCP.make_rom("platform-w"); vid = sha[:16]; rp = os.path.join(self.tmp, "w.nes"); open(rp, "wb").write(rom)
        out = os.path.join(self.tmp, "written.ccs")
        cp.write(out, collection=TCP.collection([vid]), roms=[{"version_id": vid, "path": rp, "title": "A"}])
        with zipfile.ZipFile(out) as z: man = json.loads(z.read(cp.MANIFEST)); coll = json.loads(z.read("collection.json"))
        self.assertNotIn("ccs_target", man); self.assertNotIn("ccs_target", coll)
        self.assertTrue(cp.inspect(out)["ok"], "a package written now reads as before")
        self.assertTrue(cp.inspect(self._package())["ok"], "a package without ccs_target is NES")
        self.assertTrue(cp.inspect(self._package(lambda m: dict(m, ccs_target={"schema": "ccs-target/1", "game_system": "nes", "core": "fceumm"})))["ok"], "an explicit NES target reads")
        for what, kw in (("the manifest names SNES", {"man_patch": lambda m: dict(m, ccs_target=SNES_TARGET)}),
                         ("the manifest names the reserved core", {"man_patch": lambda m: dict(m, ccs_target={"schema": "ccs-target/1", "core": "snes-unselected"})}),
                         ("the collection names SNES", {"coll_patch": lambda c: dict(c, ccs_target=SNES_TARGET)}),
                         ("a game in the collection is SNES", {"coll_patch": lambda c: dict(c, selections=[dict(s, game_system="snes") for s in c["selections"]])}),
                         ("a newer target schema", {"man_patch": lambda m: dict(m, ccs_target={"schema": "ccs-target/2"})})):
            with self.assertRaises(cp.PackageError, msg=what) as cm: cp.inspect(self._package(**kw))
            self.assertEqual(str(cm.exception), P.DOCUMENT_TEXT, what)

    def test_collection_configuration_refused(self):
        from native_home import config as CF
        man = TCP.collection(["0123456789abcdef"])
        for bad in (dict(man, ccs_target=SNES_TARGET), dict(man, selections=[dict(man["selections"][0], game_system="snes")])):
            with self.assertRaises(CF.ConfigError) as cm: CF.resolve(bad, {"theme": "x"})
            self.assertEqual(str(cm.exception), P.DOCUMENT_TEXT)
        with self.assertRaises(CF.ConfigError) as cm: CF.resolve(man, {"theme": "not-a-theme"})
        self.assertNotEqual(str(cm.exception), P.DOCUMENT_TEXT, "an NES collection goes on to the next check, as before")

    def test_page_handoff_refused(self):
        env = dict(os.environ, NES_STUDIO_DATA=os.path.join(self.tmp, "data"), NES_COMPANION_CONFIG=os.path.join(self.tmp, "none.json"), NES_STUDIO_SITE=os.path.join(self.tmp, "site"))
        code = ("import json,sys; sys.path.insert(0, %r); import nes_companion as N\n"
                "c={'schema':'nes-classic-collection/4','selections':[{'version_id':'0123456789abcdef'}]}\n"
                "out=[N.handoff_validate({'id':'abcdefgh1','collection':c})[1], N.handoff_validate({'id':'abcdefgh2','collection':dict(c, ccs_target={'schema':'ccs-target/1','game_system':'snes'})})[1],"
                " N.handoff_validate({'id':'abcdefgh3','collection':dict(c, selections=[{'version_id':'0123456789abcdef','game_system':'snes'}])})[1]]\n"
                "print(json.dumps(out))") % HERE
        r = subprocess.run([sys.executable, "-I", "-c", code], capture_output=True, text=True, env=env, timeout=120)
        self.assertEqual(r.returncode, 0, r.stderr[-800:])
        ok, snes, game = json.loads(r.stdout.strip().splitlines()[-1])
        self.assertEqual(ok, ""); self.assertEqual(snes, P.DOCUMENT_TEXT); self.assertEqual(game, P.DOCUMENT_TEXT)

    def test_console_package_plan_refused(self):
        from console_engine import ccs_install as CI
        from test_ccs_install import make_package
        ok = make_package(os.path.join(self.tmp, "nes"))
        self.assertTrue(CI.build_plan(ok), "a console package without ccs_target is planned as before")
        for tgt in (SNES_TARGET, {"schema": "ccs-target/1", "physical_model": "snes-classic-edition"}, {"schema": "ccs-target/1", "method": "ccs-nucleus-unknown"}):
            d = make_package(os.path.join(self.tmp, "x-%d" % len(os.listdir(self.tmp))))
            m = json.load(open(os.path.join(d, "MANIFEST.json"))); m["ccs_target"] = tgt; json.dump(m, open(os.path.join(d, "MANIFEST.json"), "w"))
            with self.assertRaises(CI.PlanError) as cm: CI.build_plan(d)
            self.assertEqual(str(cm.exception), P.DOCUMENT_TEXT)

    def test_console_identity_refuses_snes_hardware(self):
        from console_engine import ccs_install as CI
        for system in ("snes-usa", "snes-eur"):
            with self.assertRaises(CI.Refused): CI.check_device({"hwid": "sim-0005", "system": system, "kernel": CI.SUPPORTED["kernel"], "hakchi": CI.SUPPORTED["hakchi"], "can_interact": True, "custom_firmware": False})

    def test_worker_request_refused(self):
        from console_engine.worker import Worker
        w = Worker(os.path.join(self.tmp, "worker"), out=io.StringIO())
        def ask(args):
            w.out = io.StringIO(); w.handle(json.dumps({"v": 1, "id": "x", "cmd": "capabilities", "args": args})); return json.loads(w.out.getvalue().strip().splitlines()[-1])
        self.assertEqual(ask({})["event"], "result", "a request that names nothing: as before")
        self.assertEqual(ask({"game_system": "nes", "capability": "nes-gameplay"})["event"], "result")
        for args in ({"capability": "snes-gameplay"}, {"game_system": "snes"}, {"provider": {"kind": "core", "id": "snes-unselected"}}, {"boot_option": "ccs-snes"},
                     {"ccs_target": SNES_TARGET}, {"physical_model": "snes-classic-edition"}, {"input": "snes-pad"}, {"capability": "snes-classic-restore"}):
            r = ask(args); self.assertEqual((r["event"], r["code"]), ("error", "unsupported-target"), args); self.assertEqual(r["text"], P.REQUEST_TEXT)
        w.out = io.StringIO(); w.handle(json.dumps({"v": 1, "id": "p", "cmd": "plan", "args": {"manifest": dict(TCP.collection(["0123456789abcdef"]), ccs_target=SNES_TARGET)}}))
        r = json.loads(w.out.getvalue().strip().splitlines()[-1]); self.assertEqual((r["event"], r["code"]), ("error", "unsupported-target"), "a plan request whose collection names SNES")

    def test_game_saves_refused_and_unchanged(self):
        import saves_transfer as st
        def z(man, members=()):
            p = os.path.join(self.tmp, "gs-%d.zip" % len(os.listdir(self.tmp)))
            with zipfile.ZipFile(p, "w") as f:
                f.writestr(st.MANIFEST, json.dumps(man))
                for n, b in members: f.writestr(n, b)
            return p
        base = {"schema": st.SCHEMA, "platform": "web-wasm32", "games": []}
        self.assertEqual(st.read(z(base))["manifest"]["games"], [], "a Game Saves file without ccs_target reads as before")
        self.assertTrue(st.read(z(dict(base, ccs_target={"schema": "ccs-target/1", "game_system": "nes"}))))
        sha = hashlib.sha256(b"synthetic game").hexdigest(); cart = b"\x5a" * 8192
        game = {"content_sha256": sha, "title": "T", "files": [{"name": "cart.sav", "bytes": len(cart), "sha256": hashlib.sha256(cart).hexdigest(), "kind": "cart"}]}
        self.assertTrue(st.read(z(dict(base, games=[game]), [(f"saves/{sha}/cart.sav", cart)])))
        for bad in (dict(base, ccs_target=SNES_TARGET), dict(base, games=[dict(game, game_system="snes")])):
            with self.assertRaises(st.SavesError) as cm: st.read(z(bad, [(f"saves/{sha}/cart.sav", cart)] if bad.get("games") else []))
            self.assertEqual(str(cm.exception), P.DOCUMENT_TEXT)
        self.assertNotIn("ccs_target", open(os.path.join(HERE, "saves_transfer.py"), encoding="utf-8").read().split("def export", 1)[1].split("def _member", 1)[0], "export writes no ccs_target (its files stay as they were)")

    # ---- build and licensing: only the supported core
    def test_only_the_supported_core_is_vendored(self):
        core = P.entry("core", "fceumm"); third = os.path.join(REPO, "tools", "player", "third_party")
        self.assertIn(core["pin"], open(os.path.join(REPO, core["source"], "VENDORED.md"), encoding="utf-8").read())
        for n in core["notices"]: self.assertTrue(os.path.isfile(os.path.join(REPO, n)), n)
        self.assertIn("FCEUmm", open(os.path.join(REPO, "THIRD_PARTY_NOTICES.md"), encoding="utf-8").read())
        self.assertEqual(sorted(os.listdir(third)), ["fceumm", "sdl_gamecontrollerdb", "zig-runtime"], "a new third-party folder needs its registry entry (and review) first")
        snes = P.entry("core", "snes-unselected"); self.assertEqual((snes["bundled"], snes["license"], snes["pin"], snes["source"]), (False, None, None, None))
        hits = [os.path.join(r, f) for r, ds, fs in os.walk(os.path.join(REPO, "tools")) for f in fs + ds if re.search(r"snes9x|bsnes|higan|canoe|\.sfrom$", f, re.I)]
        self.assertEqual(hits, [], "no SNES core, firmware or game file is in the source tree")

    # ---- no SNES promise in what people see; the recovery programs take no system
    def test_no_snes_promise_in_ui(self):
        allowed = {"Do not use it with other models, the Super NES Classic or clones.",   # existing refusals, not promises
                   "The Super NES Classic Edition and the Famicom Mini are different consoles: Workbench does not work with their hardware."}   # About This Console (D-203)
        hits = []
        roots = [STUDIO, os.path.join(REPO, "tools", "console-backup", "CcsBackupUi"), os.path.join(REPO, "tools", "desktop")]
        for root in roots:
            for r, _, fs in os.walk(root):
                for f in fs:
                    if not f.endswith((".js", ".html", ".css", ".cs", ".xaml", ".resx")) or f == "platform-ids.js": continue
                    for line in open(os.path.join(r, f), encoding="utf-8", errors="replace"):
                        line = line.replace("SNES-GROUNDWORK.md", "")   # a reference to the specification in a code comment, not page text
                        for m in re.finditer(r"[^.<>]*\b(snes|super nes|super nintendo|super famicom)\b[^.<>]*\.?", line, re.I):
                            s = re.sub(r'^\("[^"]*",\s*"', "", m.group(0).strip())   # a C# (heading, "text") pair: its text
                            if s not in allowed: hits.append(f"{f}: {m.group(0).strip()[:120]}")
        import ast   # the backend's own messages: every string constant in the modules this change touched (the registry's data aside)
        for f in ("ccs_package.py", "saves_transfer.py", "nes_companion.py", "native_home/config.py", "console_engine/ccs_install.py", "console_engine/worker.py"):
            for n in ast.walk(ast.parse(open(os.path.join(HERE, *f.split("/")), encoding="utf-8").read())):
                if isinstance(n, ast.Constant) and isinstance(n.value, str) and re.search(r"\b(snes|super nes|super nintendo)\b", n.value.replace("SNES-GROUNDWORK.md", ""), re.I) and " " in n.value:
                    if "snes-eur" in n.value or "snes-usa" in n.value: continue   # hakchi's own system codes in the refusal checks and test stand-ins
                    hits.append(f"{f}: {n.value[:120]}")
        self.assertEqual(hits, [])
        self.assertNotIn("snes", P.DOCUMENT_TEXT.lower()); self.assertNotIn("snes", P.REQUEST_TEXT.lower())

    def test_recovery_programs_take_no_system_or_provider(self):
        ram = os.path.join(REPO, "tools", "console-backup", "ram")
        for f in ("ccsbr", "ccs-init", "ccsprep", "ccs-idle"):
            t = open(os.path.join(ram, f), encoding="utf-8").read().lower()
            self.assertFalse(re.search(r"\b(snes|game_system|provider|capabilit)", t), f + " names a system, provider or capability")
        br = open(os.path.join(ram, "ccsbr"), encoding="utf-8").read()
        self.assertIn('TARGETS="boot0 boot1 boot2 boot2-68 nand nanda nandb nandc nandd nande phy parttable"', br, "ccsbr's fixed targets")
        facts = open(os.path.join(REPO, "tools", "console-backup", "CcsBackup", "Device", "ConsoleFacts.cs"), encoding="utf-8").read()
        self.assertIn("Compose.Capabilities.ModelForBoard(id.Board) == null", facts, "every console session's identity check asks the registry for the model")

if __name__ == "__main__":
    unittest.main(verbosity=2)
