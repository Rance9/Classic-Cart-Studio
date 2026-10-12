"""Test runner: runs the suites, prints exact pass / fail / skip counts per suite and writes a JSON report to the artifacts
directory. Exit code 1 if anything failed.   python run_tests.py [core|arm|env|supervisor|faults|all] [--path windows|linux|arm]"""
from __future__ import annotations
import json, os, sys, time, unittest, io
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE); sys.path.insert(0, os.path.join(os.path.dirname(HERE), "host"))

def main():
    which = [a for a in sys.argv[1:] if not a.startswith("--")] or ["all"]
    if "--path" in sys.argv: os.environ["CCS_SIM_TEST_PATH"] = sys.argv[sys.argv.index("--path") + 1]
    from runner import ARTIFACTS
    suites = {"core": "test_core", "adapter": "test_adapter", "supervisor": "test_supervisor", "arm": "test_arm", "env": "test_env"}
    if "faults" in which: which = ["core", "supervisor"]; os.environ["CCS_SIM_ONLY"] = "Faults,SupervisorTests"
    names = list(suites) if "all" in which else [w for w in which if w in suites]
    report = {"schema": "ccs-sim-test-report/1", "path": os.environ.get("CCS_SIM_TEST_PATH", "windows"), "suites": {}, "started": time.strftime("%Y-%m-%dT%H:%M:%S")}; failed = 0
    for n in names:
        mod = __import__(suites[n]); loader = unittest.TestLoader(); suite = loader.loadTestsFromModule(mod)
        only = os.environ.get("CCS_SIM_ONLY")
        if only:
            keep = unittest.TestSuite()
            for s in suite:
                for t in s:
                    if t.__class__.__name__ in only.split(","): keep.addTest(t)
            suite = keep
        out = io.StringIO(); t0 = time.time(); r = unittest.TextTestRunner(stream=out, verbosity=2).run(suite); dt = time.time() - t0
        rec = {"tests": r.testsRun, "passed": r.testsRun - len(r.failures) - len(r.errors) - len(r.skipped), "failed": len(r.failures), "errors": len(r.errors), "skipped": [(str(t).split(" ")[0], why) for t, why in r.skipped], "seconds": round(dt, 1),
               "failures": [(str(t).split(" ")[0], tb.strip().splitlines()[-1][:300]) for t, tb in r.failures + r.errors]}
        report["suites"][n] = rec; failed += rec["failed"] + rec["errors"]
        print(f"== {n}: {rec['passed']} passed, {rec['failed']} failed, {rec['errors']} errors, {len(rec['skipped'])} skipped ({rec['seconds']} s)")
        for t, why in rec["skipped"]: print(f"   skipped {t}: {why}")
        for t, why in rec["failures"]: print(f"   FAILED {t}: {why}")
        open(os.path.join(ARTIFACTS, f"test-{n}-{report['path']}.log"), "w", encoding="utf-8").write(out.getvalue())
    tot = {k: sum(s[k] for s in report["suites"].values()) for k in ("tests", "passed", "failed", "errors")}; tot["skipped"] = sum(len(s["skipped"]) for s in report["suites"].values()); report["total"] = tot
    os.makedirs(ARTIFACTS, exist_ok=True); json.dump(report, open(os.path.join(ARTIFACTS, f"test-report-{report['path']}.json"), "w"), indent=1)
    print(f"TOTAL: {tot['passed']} passed, {tot['failed']} failed, {tot['errors']} errors, {tot['skipped']} skipped of {tot['tests']} (path {report['path']})"); sys.exit(1 if failed else 0)

if __name__ == "__main__": main()
