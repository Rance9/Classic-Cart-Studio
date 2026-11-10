r"""Every Windows application build bundles the console transport worker exactly as build-worker.ps1 recorded it (companion.spec).

  python tools/companion/test_spec_worker_required.py     Exit 0 = PASS.

companion.spec is executed as PyInstaller would (test_spec_store_exclusion's recorder) with a stand-in worker folder: no folder, a folder
without BUILD-INFO.json, another schema, other upstream commits, a missing listed file, a changed byte, an unlisted file and a listed path
that leaves the folder each stop the build (SystemExit); the complete folder is bundled in console-worker/ with its BUILD-INFO.json and
its files appear in the installation's file list."""
from __future__ import annotations
import json, os, shutil, sys, tempfile
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from test_spec_store_exclusion import fixture_site, fixture_worker, exec_spec
checks: list[tuple[str, bool, object]] = []
def ok(name, cond, note=""): checks.append((name, bool(cond), note))

def refused(tmp, site, worker) -> str:
    try: exec_spec(tmp, site, worker)
    except SystemExit as e: return str(e)
    return ""

def main() -> int:
    tmp = tempfile.mkdtemp(prefix="ccs-spec-worker-")
    try:
        site = fixture_site(tmp)
        ok("no_folder_refused", "required" in refused(tmp, site, ""))
        ok("missing_folder_refused", "required" in refused(tmp, site, os.path.join(tmp, "nowhere")))
        cases = {
            "no_build_info": lambda d: os.remove(os.path.join(d, "BUILD-INFO.json")),
            "other_schema": lambda d: fixture_worker(os.path.dirname(d), schema="other/1"),
            "other_hakchi_commit": lambda d: fixture_worker(os.path.dirname(d), hakchi={"commit": "0" * 40, "submodules": {"Libraries/SSH.NET": "32181aeb4ce69e8f07e5e1d585fd592a44017bc1"}}),
            "missing_listed_file": lambda d: os.remove(os.path.join(d, "Renci.SshNet.dll")),
            "changed_byte": lambda d: open(os.path.join(d, "CcsWorker.dll"), "ab").write(b"x"),
            "unlisted_file": lambda d: open(os.path.join(d, "extra.dll"), "wb").write(b"x"),
            "no_notice_listed": lambda d: (lambda i: (i["files"].pop("NOTICE"), json.dump(i, open(os.path.join(d, "BUILD-INFO.json"), "w"))))(json.load(open(os.path.join(d, "BUILD-INFO.json")))),
            "path_outside": lambda d: (lambda i: (i["files"].__setitem__("../x", {"size": 1, "sha256": "0" * 64}), json.dump(i, open(os.path.join(d, "BUILD-INFO.json"), "w"))))(json.load(open(os.path.join(d, "BUILD-INFO.json")))),
        }
        for name, spoil in cases.items():
            root = os.path.join(tmp, name); os.makedirs(root); d = fixture_worker(root); spoil(d)
            why = refused(tmp, site, d); ok(f"{name}_refused", why != "", why or "the build went on")
        good = fixture_worker(os.path.join(tmp, "good"))
        rec, _ = exec_spec(tmp, site, good)
        datas = rec.get("datas") or []
        w = sorted((os.path.relpath(s, good).replace("\\", "/"), d.replace("\\", "/")) for s, d in datas if os.path.normcase(os.path.abspath(s)).startswith(os.path.normcase(os.path.abspath(good))))
        ok("complete_folder_bundled", w == sorted([("BUILD-INFO.json", "console-worker"), ("CcsWorker.dll", "console-worker"), ("LibUsbDotNet.LibUsbDotNet.dll", "console-worker"), ("NOTICE", "console-worker"),
                                                  ("Renci.SshNet.dll", "console-worker"), ("SOURCE.md", "console-worker"), ("ccs-console-worker.exe", "console-worker"),
                                                  ("ccs-console-worker.exe.config", "console-worker"), ("licenses/GPL-3.0.txt", "console-worker/licenses")]), w)
        man_src = next((s for s, d in datas if os.path.basename(s) == "bundle-manifest.json"), None)
        man = json.load(open(man_src, encoding="utf-8")) if man_src else {}
        ok("file_list_covers_worker", all(f in (man.get("files") or {}) for f in ("console-worker/ccs-console-worker.exe", "console-worker/BUILD-INFO.json", "console-worker/licenses/GPL-3.0.txt")),
           sorted(f for f in (man.get("files") or {}) if f.startswith("console-worker/")))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    for k, v, n in checks: print(f"{k}={'true' if v else 'false'}" + (f" ({str(n)[:300]})" if not v and n != "" else ""))
    bad = [k for k, v, _ in checks if not v]
    print("RESULT:", "PASS" if not bad else "FAIL " + json.dumps(bad), f"({len(checks)} checks)")
    return 0 if not bad else 1

if __name__ == "__main__": sys.exit(main())
