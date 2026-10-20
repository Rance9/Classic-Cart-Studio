r"""One storage calculation: the NES Classic Loadout editor's meter (studio/storage-model.js) and the installation plan's budget
(console_engine/storage_model.py) return identical results for the same loadouts, and the model counts what it should:
  byte-based     a 1 MB game costs more than a 32 KB game by exactly the difference in file bytes
  built-in       a built-in game kept as it is adds no game file and no per-game files (only its HOME cover in the menu estimate)
  shared         RetroArch is counted once for any number of games that need it; the Hakchi base once; none when already present
  unknown        an unknown game size is never counted as zero: the fit cannot be confirmed
  oversized      a loadout larger than the capacity is "over" by the exact excess
  capacity       the reference capacity is derived from the reference console figures, not a constant
  python test_storage_model.py      Exit 0 = PASS."""
from __future__ import annotations
import json, os, shutil, subprocess, sys
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, os.path.join(HERE, "..", "companion"))
from console_engine import storage_model as SM

def main():
    checks = []
    def ok(k, v, n=""): checks.append((k, bool(v))); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:300]})" if n and not v else ""))
    G = lambda b, install=True, ra=False: {"bytes": b, "install": install, "retroarch": ra}
    cases = {
        "small":   {"games": [G(32 * 1024 + 16)] * 3, "home": {"games": 3}},
        "large":   {"games": [G(1024 * 1024 + 16)] * 3, "home": {"games": 3}},
        "builtin": {"games": [G(None, install=False)] * 30 + [G(262160)], "home": {"games": 31}},
        "ra":      {"games": [G(262160, ra=True) for _ in range(40)], "home": {"games": 40}},
        "ra_have": {"games": [G(262160, ra=True) for _ in range(40)], "home": {"games": 40}, "hakchi_present": True, "retroarch_present": True},
        "unknown": {"games": [G(262160), G(None)], "home": {"games": 2}},
        "measured_home": {"games": [G(262160)], "home": {"measured": 5_850_284, "games": 1}},
        "device":  {"games": [G(262160)] * 5, "home": {"games": 5}, "free": 20 * 1024 * 1024, "hakchi_present": True},
        "over":    {"games": [G(524304)] * 900, "home": {"games": 900}},
        "empty":   {"games": [], "home": {"games": 0}},
    }
    py = {k: SM.estimate(v) for k, v in cases.items()}
    js_src = open(os.path.join(HERE, "studio", "storage-model.js"), encoding="utf-8").read()
    prog = js_src + f"\nconst M={json.dumps(SM.MODEL)}; const C={json.dumps(cases)}; const o={{}}; for(const k in C) o[k]=ccsStorageEstimate(C[k], M); process.stdout.write(JSON.stringify(o));"
    js = json.loads(subprocess.run([shutil.which("node"), "-"], input=prog, capture_output=True, text=True, check=True).stdout)
    ok("editor_and_planner_identical_for_every_case", all(js[k] == json.loads(json.dumps(py[k])) for k in cases), [k for k in cases if js[k] != json.loads(json.dumps(py[k]))])
    ok("byte_based_not_count_based", py["large"]["rom"] - py["small"]["rom"] == 3 * (1024 * 1024 - 32 * 1024) and py["large"]["files"] == py["small"]["files"])
    ok("builtin_games_add_no_game_files", py["builtin"]["rom"] == 262160 and py["builtin"]["added"] == 1 and py["builtin"]["home"] == SM.MODEL["home_estimate"]["base"] + 31 * SM.MODEL["home_estimate"]["per_game"])
    ok("retroarch_counted_once_for_40_games", py["ra"]["components"]["retroarch"] == SM.MODEL["components"]["retroarch"] and py["ra"]["components"]["hakchi"] == SM.MODEL["components"]["hakchi"])
    ok("components_not_counted_when_present", py["ra_have"]["components"] == {"hakchi": 0, "retroarch": 0})
    ok("unknown_size_is_not_a_fit", py["unknown"]["status"] == "unconfirmed" and py["unknown"]["unknown"] == 1)
    ok("measured_home_used_when_prepared", py["measured_home"]["home"] == 5_850_284 and py["measured_home"]["home_measured"])
    ok("oversized_is_over_by_the_exact_excess", py["over"]["status"] == "over" and py["over"]["remaining"] == py["over"]["available"] - py["over"]["used"] < 0)
    r = SM.MODEL["reference"]; cap = SM.capacity()
    ok("capacity_derived_from_the_reference_console", cap == r["free_before_mod"] - r["root_reserved"] - r["saves_reserve"] and py["empty"]["available"] == cap and 300e6 < cap < 400e6, cap)
    ok("device_free_space_used_when_known", py["device"]["available"] == 20 * 1024 * 1024 and py["device"]["source"] == "device")
    bad = [k for k, v in checks if not v]; print("RESULT:", "PASS" if not bad else "FAIL " + json.dumps(bad), f"({len(checks)} checks)"); return 0 if not bad else 1

if __name__ == "__main__": sys.exit(main())
