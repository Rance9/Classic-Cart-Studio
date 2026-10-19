r"""Release layout (N3.1): the frozen Windows application folder carries the console's native HOME runtime (32-bit ARM Linux,
static) and the Windows build of the same renderer for Preview on TV, and a native HOME package can be prepared from the
release layout alone: the bundled site data, the bundled runtime, no development checkout, compiler or build output.

  python test_release_layout.py <dist folder: ...\ClassicCartStudioBackend>      Exit 0 = PASS.

Checks: everything the installed application needs is inside the release folder (nothing is downloaded at run time): the Hakchi2
CE 3.9.3 package with its console tools and USB driver (recorded size and SHA-256, required components inside), both native binaries
in _internal\native (ELF 32-bit ARM, statically linked; a Windows PE executable), the bundled site, fonts, helper scripts and
notices, and the installation's file list (bundle-manifest.json: every listed file present and matching its SHA-256);
then, in a child process whose only inputs are the release folder (NES_STUDIO_SITE and CCS_NATIVE_HOME_RUNTIME point into it, the
development build folder is pointed at an empty place), a 12-game deployment package with all four themes is built: ready, no
blockers, the packaged runtime byte-identical to the release's; finally the release's Windows renderer loads that package."""
from __future__ import annotations
import hashlib, json, os, subprocess, sys, tempfile, shutil, zipfile
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path[:0] = [HERE, os.path.join(HERE, "..", "rom-pipeline")]
import bundle_integrity as BI   # noqa: E402

def elf(p):
    b = open(p, "rb").read(4096)
    if b[:4] != b"\x7fELF": return None
    cls = b[4]; mach = int.from_bytes(b[18:20], "little"); phoff = int.from_bytes(b[28:32], "little"); phnum = int.from_bytes(b[44:46], "little"); phsz = int.from_bytes(b[42:44], "little")
    interp = any(int.from_bytes(b[phoff + i * phsz: phoff + i * phsz + 4], "little") == 3 for i in range(phnum)) if cls == 1 else None
    return {"class": cls, "machine": mach, "interp": interp}

CHILD = r'''
import json, os, sys, tempfile
sys.path[:0] = [os.environ["CCS_T_HERE"], os.path.join(os.environ["CCS_T_HERE"], "..", "rom-pipeline")]
from native_home import package as P
import test_native_home_package as T
ids = T.factory_ids()[:8] + T.added_ids(4)
man, home = T.studio_config(ids, theme="featured-shelf", folders=[{"id": "f:a", "name": "Favorites Shelf", "parent": None, "order": ids[:3]}])
root = os.path.join(os.environ["CCS_T_TMP"], "pkgroot"); r = P.build(man, home, root=root)
m = json.load(open(os.path.join(root, "current", "manifest.json"), encoding="utf-8")) if r.get("ok") else {}
print(json.dumps({"ok": r.get("ok"), "error": r.get("error"), "problems": r.get("problems"), "blockers": m.get("blockers"), "status": r.get("status"), "runtime": P.runtime_binary(),
                  "themes": sorted((json.load(open(os.path.join(root, "current", "pack.json"), encoding="utf-8")).get("themes") or {}).keys()) if r.get("ok") else [], "package": os.path.join(root, "current")}))
'''

def main(dist: str) -> int:
    checks = []; ok = lambda k, v, n="": checks.append((k, bool(v), n))
    internal = os.path.join(dist, "_internal") if os.path.isdir(os.path.join(dist, "_internal")) else dist
    rt = os.path.join(internal, "native", "ccs-home-armv7"); core = os.path.join(internal, "native", "ccs-sim-core.exe"); site = os.path.join(internal, "site")
    e = elf(rt) if os.path.isfile(rt) else None
    ok("arm_runtime_bundled", e is not None, rt); ok("arm_runtime_is_32bit_arm_static", e and e["class"] == 1 and e["machine"] == 40 and e["interp"] is False, e)
    ok("preview_core_bundled", os.path.isfile(core) and open(core, "rb").read(2) == b"MZ", core)
    ok("site_bundled", os.path.isfile(os.path.join(site, "index.html")) and os.path.isfile(os.path.join(site, "catalog.json")), site)
    from hakchi_bridge import HAKCHI_PACKAGE, HAKCHI_REQUIRED
    hk = os.path.join(internal, "packages", HAKCHI_PACKAGE["name"])
    ok("hakchi_package_bundled_and_verified", os.path.isfile(hk) and os.path.getsize(hk) == HAKCHI_PACKAGE["size"] and BI.sha256(hk) == HAKCHI_PACKAGE["sha256"], hk)
    if os.path.isfile(hk):
        names = set(zipfile.ZipFile(hk).namelist()); ok("hakchi_required_components_in_package", all(r in names for r in HAKCHI_REQUIRED), [r for r in HAKCHI_REQUIRED if r not in names])
    man = BI.manifest(internal)
    ok("installation_file_list_present", man is not None and not man.get("required_missing") and all(r in man["files"] for r in BI.REQUIRED_FILES),
       (man or {}).get("required_missing") if man else "bundle-manifest.json missing")
    full = BI.check(full=True, bundle=internal)
    ok("installation_file_list_verified", full["ok"] and full["checked"] >= 1000, full["message"])
    ok("fonts_and_scripts_bundled", os.path.isdir(os.path.join(internal, "native_home", "fonts")) and all(os.path.isfile(os.path.join(internal, n)) for n in ("Read-HakchiSelection.ps1", "Get-ProcessWindows.ps1", "Close-Hakchi.ps1", "THIRD_PARTY_NOTICES.md")))
    if all(c[1] for c in checks):
        tmp = tempfile.mkdtemp(prefix="ccs-release-layout-")
        env = dict(os.environ, NES_STUDIO_SITE=site, CCS_NATIVE_HOME_RUNTIME=rt, CCS_SIM_BUILD_WIN=os.path.join(tmp, "no-dev-build"), NES_STUDIO_DATA=os.path.join(tmp, "data"),
                   NES_PERSONAL_DIR=os.path.join(tmp, "data", "personal"), CCS_T_HERE=HERE, CCS_T_TMP=tmp, PYTHONIOENCODING="utf-8")
        r = subprocess.run([sys.executable, "-W", "ignore", "-c", CHILD], env=env, capture_output=True, text=True, timeout=1800)
        try: res = json.loads(r.stdout.strip().splitlines()[-1])
        except Exception: res = {"ok": False, "error": (r.stdout + r.stderr)[-800:]}
        ok("package_prepared_from_release_layout", res.get("ok") and not res.get("blockers"), {k: res.get(k) for k in ("error", "problems", "blockers", "status")})
        ok("runtime_comes_from_the_release", res.get("runtime") and os.path.normcase(os.path.abspath(res["runtime"])) == os.path.normcase(os.path.abspath(rt)), res.get("runtime"))
        ok("all_four_themes", res.get("themes") == sorted(["arcade-grid", "box-art-showcase", "featured-shelf", "adventure-map"]), res.get("themes"))
        if res.get("ok"):
            pr = os.path.join(res["package"], "bin", "ccs-home-armv7")
            ok("packaged_runtime_identical", os.path.isfile(pr) and hashlib.sha256(open(pr, "rb").read()).digest() == hashlib.sha256(open(rt, "rb").read()).digest(), pr)
            p = subprocess.Popen([core, f"--state-dir={os.path.join(tmp, 'state')}"], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
            out, _ = p.communicate(f"home {res['package']}\nshutdown\n", timeout=120)
            evs = [json.loads(l) for l in out.splitlines() if l.startswith("{")]
            ok("release_renderer_loads_the_package", any(x.get("ev") == "home_loaded" for x in evs), [x.get("ev") for x in evs][:6])
        shutil.rmtree(tmp, ignore_errors=True)
    for k, v, n in checks: print(f"{k}={'true' if v else 'false'}" + (f" ({str(n)[:400]})" if not v and n else ""))
    bad = [k for k, v, _ in checks if not v]; print("RESULT:", "PASS" if not bad else "FAIL " + json.dumps(bad), f"({len(checks)} checks)"); return 0 if not bad else 1

if __name__ == "__main__": sys.exit(main(sys.argv[1]))
