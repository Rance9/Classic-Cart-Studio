"""Environment, packaging and isolation suite: dependency detection reports, no system-wide change by run/test, generated
files inside designated directories only, clean removes only designated files, production data untouched."""
from __future__ import annotations
import json, os, subprocess, sys, unittest, hashlib, time
HERE = os.path.dirname(os.path.abspath(__file__)); SIM = os.path.dirname(HERE); sys.path.insert(0, os.path.join(SIM, "host"))
from runner import ARTIFACTS, BUILD, DISTRO
PS = os.path.join(SIM, "sim.ps1"); LA = os.environ.get("LOCALAPPDATA", "")
PROD = [os.path.join(LA, "ClassicCartStudio"), os.path.join(LA, "Programs", "ClassicCartStudio"), os.path.join(os.path.dirname(os.path.dirname(SIM)), "..", "hakchi"), os.path.join(os.path.dirname(os.path.dirname(SIM)), "..", "roms")]

def snapshot(root: str) -> dict:
    out = {}
    if not os.path.isdir(root): return out
    for r, _, fs in os.walk(root):
        for f in fs:
            p = os.path.join(r, f)
            try: out[p] = (os.path.getsize(p), int(os.path.getmtime(p)))
            except OSError: pass
    return out

def ps(*args, timeout=600) -> subprocess.CompletedProcess:
    return subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", PS, *args], capture_output=True, text=True, timeout=timeout)

class Environment(unittest.TestCase):
    def test_doctor_reports_versions_or_clear_missing_dependency(self):
        r = ps("doctor", "--json"); self.assertEqual(r.returncode, 0, r.stderr[-400:]); d = json.loads(r.stdout[r.stdout.index("{"):])
        for k in ("windows", "wsl"): self.assertIn(k, d)
        self.assertIn("python", d["windows"]); self.assertIn("tkinter", d["windows"])
        if d["wsl"].get("available"): self.assertTrue(d["wsl"]["report"].get("gcc") or d["wsl"]["report"]["problems"])
        json.dump(d, open(os.path.join(ARTIFACTS, "doctor.json"), "w"), indent=1)
    def test_toolchain_lock_matches_installed_versions(self):
        lock = json.load(open(os.path.join(SIM, "toolchain.lock.json")))
        r = subprocess.run(["wsl.exe", "-d", DISTRO, "--", "bash", "-lc", 'cat "$HOME/.cache/ccs-sim/toolchain/manifest.json"'], capture_output=True, text=True, timeout=60)
        if r.returncode != 0 or "{" not in r.stdout: self.skipTest("toolchain manifest not present: setup not run")
        m = json.loads(r.stdout[r.stdout.index("{"):]); self.assertEqual(m["zig"], lock["zig"]["version"]); self.assertIn("10.2.1", m["qemu_arm"])
    def test_wsl_doctor_valid_json(self):
        r = subprocess.run(["wsl.exe", "-d", DISTRO, "--", "bash", "-lc", f'bash "{SIM.replace(chr(92), "/").replace("C:", "/mnt/c")}/scripts/wsl_doctor.sh"'], capture_output=True, text=True, timeout=120)
        if r.returncode != 0 and "{" not in r.stdout: self.skipTest("WSL not reachable")
        d = json.loads(r.stdout[r.stdout.index("{"):]); self.assertEqual(d["schema"], "ccs-sim-doctor/1"); self.assertIn("qemu_arm", d)

class Isolation(unittest.TestCase):
    def test_runs_do_not_touch_production_data_or_system(self):
        before = {p: snapshot(p) for p in PROD}; path_before = os.environ.get("PATH"); t0 = time.time()
        r = subprocess.run([sys.executable, os.path.join(SIM, "host", "viewer.py"), "--path", "windows", "--script", os.path.join(SIM, "fixtures", "scripts", "demo.txt"), "--out", os.path.join(ARTIFACTS, "isolation-check")], capture_output=True, text=True, timeout=120)
        self.assertEqual(r.returncode, 0, r.stderr[-400:])
        for p in PROD: self.assertEqual(snapshot(p), before[p], f"production location changed: {p}")
        self.assertEqual(os.environ.get("PATH"), path_before)
        for root, _, fs in os.walk(os.path.join(ARTIFACTS, "isolation-check")):
            for f in fs: self.assertGreaterEqual(os.path.getmtime(os.path.join(root, f)), t0 - 5)
    def test_generated_files_only_under_build_and_artifacts(self):
        src_dirs = {n for n in os.listdir(SIM)}; self.assertFalse({"build", "artifacts"} & src_dirs, "nothing generated inside tools/console-simulator")
        for root, dirs, fs in os.walk(SIM):
            for f in fs: self.assertFalse(f.endswith((".ppm", ".exe", ".o", ".elf")) and "fixtures" not in root, f"generated binary inside the source tree: {f}")
    def test_clean_removes_only_designated_dirs(self):
        """clean is exercised on a throw-away work root (CCS_SIM_WORK) so the evidence under the real artifacts folder survives a test run"""
        import tempfile
        work = tempfile.mkdtemp(prefix="ccs-sim-clean-"); art = os.path.join(work, "artifacts"); bld = os.path.join(work, "build", "windows-x86_64"); os.makedirs(art); os.makedirs(bld)
        marker = os.path.join(art, "clean-probe", "probe.txt"); os.makedirs(os.path.dirname(marker)); open(marker, "w").write("x"); open(os.path.join(bld, "keep.txt"), "w").write("b")
        outside = os.path.join(work, "not-simulator-file.txt"); open(outside, "w").write("keep")
        env = dict(os.environ, CCS_SIM_WORK=work)
        try:
            r = subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", PS, "clean", "--artifacts"], capture_output=True, text=True, timeout=120, env=env)
            self.assertEqual(r.returncode, 0, r.stderr[-300:]); self.assertFalse(os.path.exists(marker)); self.assertTrue(os.path.exists(outside)); self.assertTrue(os.path.exists(os.path.join(bld, "keep.txt")), "clean --artifacts keeps the builds")
            self.assertTrue(os.path.isdir(ARTIFACTS), "the real artifacts folder is untouched")
        finally:
            import shutil; shutil.rmtree(work, ignore_errors=True)
    def test_no_secrets_in_artifacts(self):
        import re
        for root, _, fs in os.walk(ARTIFACTS):
            for f in fs:
                if f.endswith((".json", ".jsonl", ".txt", ".log")):
                    t = open(os.path.join(root, f), encoding="utf-8", errors="replace").read()
                    self.assertIsNone(re.search(r"(ghp_[A-Za-z0-9]{20,}|CCS_HOST_TOKEN=\S+|Authorization: Bearer)", t), f)

if __name__ == "__main__": unittest.main(verbosity=2)
