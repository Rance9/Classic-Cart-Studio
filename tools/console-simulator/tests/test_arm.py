"""Real ARM execution suite. Fails (does not skip) when the ARM build is not a 32-bit ARM ELF or when QEMU does not actually
run it; skips only when the WSL toolchain was never set up (reported as a skip with the reason, never as a pass)."""
from __future__ import annotations
import json, os, subprocess, sys, time, unittest
HERE = os.path.dirname(os.path.abspath(__file__)); SIM = os.path.dirname(HERE); sys.path.insert(0, os.path.join(SIM, "host"))
from runner import Core, ARTIFACTS, BUILD, DISTRO, wsl_path
ART = os.path.join(ARTIFACTS, "tests", "arm"); os.makedirs(ART, exist_ok=True)
INFO = os.path.join(BUILD, "arm-musl", "build-info.json"); INFO_GLIBC = os.path.join(BUILD, "arm-glibc", "build-info.json")

def wsl(cmd: str, timeout: float = 120) -> subprocess.CompletedProcess:
    return subprocess.run(["wsl.exe", "-d", DISTRO, "--", "bash", "-lc", cmd], capture_output=True, timeout=timeout)

def toolchain_ready() -> bool:
    try: return wsl('test -x "$HOME/.cache/ccs-sim/toolchain/deb/usr/bin/qemu-arm" && echo yes').stdout.strip().endswith(b"yes")
    except Exception: return False

class ArmBinary(unittest.TestCase):
    def test_build_is_32bit_arm_elf_cortex_a7_hardfloat(self):
        self.assertTrue(os.path.exists(INFO), "arm-musl build missing: run `sim.ps1 build-arm`")
        info = json.load(open(INFO)); h = info["readelf"]
        self.assertEqual(h.get("Class"), "ELF32"); self.assertEqual(h.get("Machine"), "ARM"); self.assertIn("little endian", h.get("Data", ""))
        self.assertIn("hard-float", h.get("Flags", "")); self.assertIn("ARM", info["file"]); self.assertNotIn("x86", info["file"])
        self.assertTrue(any('Tag_CPU_name: "cortex-a7"' in a for a in info["arm_attributes"])); self.assertTrue(any("Tag_CPU_arch: v7" in a for a in info["arm_attributes"]))
        self.assertEqual(info["interpreter"], "none (static)"); self.assertEqual(info["needed"], [])
        json.dump({"target": info["target"], "file": info["file"], "readelf": h, "arm_attributes": info["arm_attributes"], "sha256": info["sha256"]}, open(os.path.join(ART, "elf-inspection.json"), "w"), indent=1)
    def test_glibc_variant_records_interpreter_and_needed_libs(self):
        self.assertTrue(os.path.exists(INFO_GLIBC)); info = json.load(open(INFO_GLIBC))
        self.assertEqual(info["interpreter"], "/lib/ld-linux-armhf.so.3"); self.assertIn("libc.so.6", info["needed"]); self.assertEqual(info["readelf"].get("Machine"), "ARM")
    def test_windows_build_is_not_mislabeled(self):
        info = json.load(open(os.path.join(BUILD, "windows-x86_64", "build-info.json"))); self.assertIn("x86-64", info["file"]); self.assertIn("PE32+", info["file"])

@unittest.skipUnless(toolchain_ready(), "WSL toolchain not set up (run `sim.ps1 setup`): ARM execution not verified")
class ArmExecution(unittest.TestCase):
    def test_qemu_runs_protocol_session_with_expected_results(self):
        c = Core("arm", timeout=30); r = c.ready(60); self.assertIsNotNone(r, c.stderr[-300:]); self.assertEqual((r["arch"], r["abi"], r["float_abi"], r["pointer_bits"]), ("arm", "linux-musl", "hard", 32))
        ld = c.load(os.path.join(SIM, "fixtures", "mixed.json")); self.assertEqual((ld["ev"], ld["entries"], ld["menu"]), ("fixture_loaded", 37, 29))
        c.press("down"); c.tick(500); c.release("down"); ppm = os.path.join(ART, "arm-frame.ppm"); fr = c.frame(ppm); self.assertEqual((fr["ev"], fr["w"], fr["h"]), ("frame_written", 1280, 720))
        rec = c.launch_cycle(stub_exit=0); self.assertEqual(rec["result"]["ev"], "resumed"); self.assertEqual(rec["stub"]["events"][0]["arch"], "arm")
        st = c.stats(); code = c.shutdown(); self.assertEqual(code, 0); self.assertEqual((st["arch"], st["inputs"], st["launches"]), ("arm", 3, 1))
        from PIL import Image
        self.assertEqual(Image.open(ppm).size, (1280, 720))
    def test_same_script_same_events_on_arm_and_host(self):
        """the shared core yields identical event sequences on the ARM path and on the Windows path (shared logic proof)"""
        def run(path):
            c = Core(path, timeout=30); c.ready(60); c.load(os.path.join(SIM, "fixtures", "mixed.json")); c.drain(0.3)
            for i in range(12): c.press("down" if i % 4 else "up"); c.tick(210); c.release("down" if i % 4 else "up")
            c.send("focus lost"); c.press("a"); c.send("focus gained"); c.launch_cycle(stub_exit=0, stub_delay_ms=1)
            evs = [{k: v for k, v in e.items() if k not in ("path", "rss_kb", "build", "arch", "abi", "float_abi", "pointer_bits", "rss_note", "fb_bytes", "note")} for e in c.drain(0.5) if e["ev"] in ("input", "cursor", "focus", "launch_requested", "resumed", "state")]
            c.shutdown(); return evs
        a, w = run("arm"), run("windows"); self.assertEqual(a, w); self.assertGreater(len(a), 30)
        json.dump({"events_compared": len(a), "identical": a == w}, open(os.path.join(ART, "arm-vs-host.json"), "w"))
    def test_missing_arm_runtime_fails_understandably(self):
        tc = "$HOME/.cache/ccs-sim/toolchain"; exe = f"{wsl_path(BUILD)}/arm-glibc/ccs-sim-core"
        r = wsl(f'"{tc}/deb/usr/bin/qemu-arm" "{exe}" --version'); self.assertNotEqual(r.returncode, 0); self.assertIn(b"ld-linux-armhf.so.3", r.stderr + r.stdout)
        r2 = wsl(f'"{tc}/deb/usr/bin/qemu-arm" -L "{tc}/deb/usr/arm-linux-gnueabihf" "{exe}" --version'); self.assertEqual(r2.returncode, 0); self.assertIn(b"linux-glibc", r2.stdout)
    def test_nonzero_exit_and_timeout_under_qemu(self):
        c = Core("arm", ["--fault=startup-fail"]); self.assertIsNone(c.ready(10)); c.proc.wait(20); self.assertEqual(c.proc.returncode, 3)
        d = Core("arm", ["--fault=startup-hang"]); self.assertIsNone(d.ready(2)); self.assertIsNone(d.exit_code()); d.kill()
    def test_arm_stress_and_timing(self):
        c = Core("arm", timeout=60); c.ready(60); t0 = time.time(); r = c.load(os.path.join(SIM, "fixtures", "stress-5000.json")); dt = time.time() - t0; st = c.stats(); c.shutdown()
        self.assertEqual((r["ev"], r["entries"]), ("fixture_loaded", 5000))
        json.dump({"path": "arm (qemu user mode on x86-64)", "entries": 5000, "load_seconds": round(dt, 3), "rss_kb_of_qemu_process": st["rss_kb"], "model_bytes": st["model_bytes"], "caveat": "x86-hosted QEMU timing is not a device benchmark"}, open(os.path.join(ART, "arm-stress.json"), "w"))

if __name__ == "__main__": unittest.main(verbosity=2)
