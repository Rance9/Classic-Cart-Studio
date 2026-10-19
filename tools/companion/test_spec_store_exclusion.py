r"""The Windows application never bundles the website's Store (companion.spec).

  python tools/companion/test_spec_store_exclusion.py            Exit 0 = PASS.
  python tools/companion/test_spec_store_exclusion.py --build    also runs a real PyInstaller build of the spec (a few minutes)

companion.spec is executed as PyInstaller would execute it, with the build steps (Analysis, PYZ, EXE, COLLECT) replaced by recorders
and NES_SITE_DIR pointing at a small fixture site that has a top-level store/ folder (with an Amazon-style link) and a nested
brand/store folder. Checks: the site entry in DATAS is a staging copy without store/ but with everything else (the nested folder
included), bundle-manifest.json lists no site/store file, and the source site is untouched. With --build, PyInstaller builds the spec
into a temporary folder and the frozen _internal/site has no store folder and no Amazon host name."""
from __future__ import annotations
import json, os, re, shutil, subprocess, sys, tempfile
HERE = os.path.dirname(os.path.abspath(__file__)); SPEC = os.path.join(HERE, "companion.spec")
AMAZON_HOST = re.compile(r"(?<![a-z0-9])(?:amazon|amzn)\.[a-z]{2,}", re.I)
checks: list[tuple[str, bool, object]] = []
def ok(name, cond, note=""): checks.append((name, bool(cond), note))

def fixture_site(root: str) -> str:
    site = os.path.join(root, "site")
    files = {"index.html": "<!doctype html><title>fixture</title>", "catalog.json": "{}", "brand/store/keep.json": '{"nested": true}',
             "store/index.html": "<a href='https://www.amazon.com/dp/FIXTURE?tag=fixture-20'>fixture</a>", "store/store.json": '{"schema": "ccs-store/1"}',
             "store/img/x.txt": "fixture"}
    for rel, text in files.items():
        p = os.path.join(site, *rel.split("/")); os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "w", encoding="utf-8") as f: f.write(text)
    return site

def tree(d: str) -> list[str]:
    return sorted(os.path.relpath(os.path.join(a, n), d).replace("\\", "/") for a, _, ns in os.walk(d) for n in ns)

def isolated_env(tmp: str, site: str) -> dict:
    env = {k: v for k, v in os.environ.items() if k not in ("CCS_RELEASE_BUILD", "CCS_REQUIRE_NATIVE", "CCS_HAKCHI_PACKAGE")}
    env.update(NES_SITE_DIR=site, CCS_NATIVE_HOME_RUNTIME=os.path.join(tmp, "absent"), CCS_NATIVE_PREVIEW_CORE=os.path.join(tmp, "absent"))
    return env

def exec_spec(tmp: str, site: str):
    rec = {}
    class Analysis:
        def __init__(self, scripts, **kw): rec["datas"] = kw.get("datas"); self.pure = []; self.scripts = []; self.binaries = []; self.datas = []
    stub = lambda *a, **k: None
    saved = dict(os.environ); os.environ.clear(); os.environ.update(isolated_env(tmp, site)); path = list(sys.path)
    try:
        g = {"__file__": SPEC, "__name__": "__main__", "SPEC": SPEC, "Analysis": Analysis, "PYZ": stub, "EXE": stub, "COLLECT": stub}
        exec(compile(open(SPEC, encoding="utf-8").read(), SPEC, "exec"), g)
    finally:
        os.environ.clear(); os.environ.update(saved); sys.path[:] = path
    return rec, g

def main(build: bool) -> int:
    tmp = tempfile.mkdtemp(prefix="ccs-spec-store-")
    try:
        site = fixture_site(tmp); before = tree(site)
        rec, g = exec_spec(tmp, site)
        datas = rec.get("datas") or []
        sites = [s for s, d in datas if d == "site"]
        ok("one_site_entry", len(sites) == 1, sites)
        staged = sites[0] if sites else ""
        ok("site_entry_is_staging_copy", staged and os.path.normcase(os.path.abspath(staged)) != os.path.normcase(os.path.abspath(site)), staged)
        st = tree(staged) if os.path.isdir(staged) else []
        ok("staging_has_no_store", st and not any(f.lower().startswith("store/") for f in st), st)
        ok("staging_keeps_everything_else", st == [f for f in before if not f.startswith("store/")], st)
        man_src = next((s for s, d in datas if os.path.basename(s) == "bundle-manifest.json"), None)
        man = json.load(open(man_src, encoding="utf-8")) if man_src else {}
        ok("manifest_lists_no_store", man.get("files") and not any(f.lower().startswith("site/store/") for f in man["files"]), sorted(man.get("files") or [])[:12])
        ok("manifest_keeps_site_files", all(f"site/{f}" in (man.get("files") or {}) for f in ("index.html", "catalog.json", "brand/store/keep.json")))
        ok("source_site_untouched", tree(site) == before)
        ok("other_datas_unchanged", any(d == os.path.join("native_home", "fonts") for _, d in datas) and any(os.path.basename(s) == "Read-HakchiSelection.ps1" for s, _ in datas))
        if build:
            dist, work = os.path.join(tmp, "dist"), os.path.join(tmp, "work")
            r = subprocess.run([sys.executable, "-m", "PyInstaller", SPEC, "--distpath", dist, "--workpath", work, "--noconfirm", "--log-level", "WARN"],
                               env=isolated_env(tmp, site), capture_output=True, text=True, timeout=1800)
            internal = os.path.join(dist, "ClassicCartStudioBackend", "_internal"); fsite = os.path.join(internal, "site")
            ok("pyinstaller_build_succeeds", r.returncode == 0 and os.path.isfile(os.path.join(fsite, "index.html")), (r.stdout + r.stderr)[-600:])
            ok("frozen_site_has_no_store", os.path.isdir(fsite) and not os.path.exists(os.path.join(fsite, "store")) and os.path.isfile(os.path.join(fsite, "brand", "store", "keep.json")),
               tree(fsite) if os.path.isdir(fsite) else fsite)
            hits = [f for f in (tree(fsite) if os.path.isdir(fsite) else []) if AMAZON_HOST.search(open(os.path.join(fsite, f), encoding="utf-8", errors="replace").read())]
            ok("frozen_site_has_no_amazon_links", os.path.isdir(fsite) and not hits, hits)
            fm = os.path.join(internal, "bundle-manifest.json")
            fman = json.load(open(fm, encoding="utf-8")) if os.path.isfile(fm) else {}
            ok("frozen_manifest_lists_no_store", fman.get("files") and not any(f.lower().startswith("site/store/") for f in fman["files"]))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    for k, v, n in checks: print(f"{k}={'true' if v else 'false'}" + (f" ({str(n)[:400]})" if not v and n != "" else ""))
    bad = [k for k, v, _ in checks if not v]
    print("RESULT:", "PASS" if not bad else "FAIL " + json.dumps(bad), f"({len(checks)} checks)")
    return 0 if not bad else 1

if __name__ == "__main__": sys.exit(main("--build" in sys.argv[1:]))
