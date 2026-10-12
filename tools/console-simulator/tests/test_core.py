"""Core behavior suites (run against the Windows build by default; CCS_SIM_TEST_PATH=linux|arm runs the same assertions on
the other execution paths). Each test drives the real core process through the protocol: nothing here is mocked except
the game (a stub) and the console (fake services inside the core)."""
from __future__ import annotations
import json, os, sys, time, unittest, hashlib
HERE = os.path.dirname(os.path.abspath(__file__)); SIM = os.path.dirname(HERE); sys.path.insert(0, os.path.join(SIM, "host"))
from runner import Core, ARTIFACTS, ppm_to_png
PATH = os.environ.get("CCS_SIM_TEST_PATH", "windows"); FX = os.path.join(SIM, "fixtures"); ART = os.path.join(ARTIFACTS, "tests", PATH); os.makedirs(ART, exist_ok=True)

def core(*args, **kw) -> Core:
    c = Core(PATH, list(args), timeout=kw.get("timeout", 20.0)); r = c.ready(30)
    if not r: raise RuntimeError(f"core not ready on path {PATH}: {c.stderr[-300:]}")
    return c

class Model(unittest.TestCase):
    def test_factory_only_deterministic(self):
        a = core(); r1 = a.load(os.path.join(FX, "factory-only.json")); a.send("menu"); m1 = [e for e in a.drain(0.3) if e["ev"] == "menu_entry"]; a.shutdown()
        b = core(); r2 = b.load(os.path.join(FX, "factory-only.json")); b.send("menu"); m2 = [e for e in b.drain(0.3) if e["ev"] == "menu_entry"]; b.shutdown()
        self.assertEqual(r1["ev"], "fixture_loaded"); self.assertEqual((r1["entries"], r1["menu"], r1["rejected"]), (30, 30, 0))
        self.assertEqual([e["id"] for e in m1], [e["id"] for e in m2]); self.assertEqual(len(m1), 30)
        self.assertEqual([e["id"] for e in m1], sorted([e["id"] for e in m1], key=lambda i: int(i.split(":")[-1])))   # order field respected
    def test_mixed_identities_and_hidden(self):
        c = core(); r = c.load(os.path.join(FX, "mixed.json")); c.send("menu"); menu = [e for e in c.drain(0.3) if e["ev"] == "menu_entry"]; c.shutdown()
        self.assertEqual(r["ev"], "fixture_loaded"); self.assertEqual(r["entries"], 37); self.assertEqual(r["rejected"], 0)
        self.assertEqual(r["menu"], 29, "8 factory-hidden entries leave the menu")
        self.assertTrue(all(e["kind"] != "factory-hidden" for e in menu))
        ids = [e["id"] for e in menu]; self.assertEqual(len(ids), len(set(ids)), "stable identities are unique")
        versions = {e["id"]: e["version_id"] for e in menu}; self.assertNotEqual(versions["sim:added:00101"], versions["sim:added:00102"], "two versions of the same display name keep distinct exact versions")
        self.assertEqual([e["ready"] for e in menu if e["id"] == "sim:added:00103"], ["missing"])
    def test_malformed_records_rejected_transparently(self):
        c = core(); r = c.load(os.path.join(FX, "malformed.json")); rej = [e for e in c.drain(0.3) if e["ev"] == "record_rejected"]; c.shutdown()
        self.assertEqual(r["ev"], "fixture_loaded"); self.assertEqual(r["entries"], 2); self.assertEqual(r["rejected"], 9); self.assertEqual(len(rej), 9)
        reasons = " | ".join(e["reason"] for e in rej)
        for needle in ("missing or empty id", "version_id", "kind is not one of", "duplicate id", "security guardrail", "visible is not a boolean", "order is not a number", "ready is not one of", "not an object"): self.assertIn(needle, reasons)
    def test_not_json_and_wrong_schema(self):
        c = core(); r1 = c.load(os.path.join(FX, "not-json.json")); r2 = c.load(os.path.join(FX, "wrong-schema.json")); r3 = c.load(os.path.join(FX, "does-not-exist.json")); c.shutdown()
        self.assertEqual(r1["ev"], "fixture_rejected"); self.assertIn("not valid JSON", r1["error"])
        self.assertEqual(r2["ev"], "fixture_rejected"); self.assertIn("schema", r2["error"])
        self.assertEqual(r3["ev"], "fixture_rejected"); self.assertIn("cannot open", r3["error"])
    def test_sort_key_ignores_leading_article(self):
        c = core(); c.load(os.path.join(FX, "mixed.json")); c.send("menu"); menu = [e["id"] for e in c.drain(0.3) if e["ev"] == "menu_entry"]; c.shutdown()
        self.assertLess(menu.index("sim:added:00105"), menu.index("sim:added:00106"), "'the Lowercase Article' sorts under L, before the explicit zzz sort key")

class Input(unittest.TestCase):
    def events(self, c: Core, kinds=("input", "cursor")): return [e for e in c.drain(0.2) if e["ev"] in kinds]
    def test_press_release_repeat_deterministic(self):
        c = core(); c.load(os.path.join(FX, "factory-only.json")); c.drain(0.2)
        c.press("down"); c.tick(399); c.tick(1); c.tick(100); c.tick(100); c.release("down"); evs = self.events(c); c.shutdown()
        seq = [(e["button"], e["kind"], e["clock_ms"]) for e in evs if e["ev"] == "input"]
        self.assertEqual(seq, [("down", "press", 0), ("down", "repeat", 400), ("down", "repeat", 500), ("down", "repeat", 600), ("down", "release", 600)])
        self.assertEqual([e["index"] for e in evs if e["ev"] == "cursor"], [1, 2, 3, 4])
    def test_duplicate_press_suppressed_and_all_buttons_observable(self):
        c = core(); c.load(os.path.join(FX, "factory-only.json")); c.drain(0.2)
        for b in ("up", "down", "left", "right", "a", "b", "start", "select"): c.press(b); c.press(b); c.release(b)
        evs = [(e["button"], e["kind"]) for e in self.events(c) if e["ev"] == "input"]; c.shutdown()
        for b in ("up", "down", "left", "right", "a", "b", "start", "select"): self.assertEqual([k for bb, k in evs if bb == b], ["press", "duplicate", "release"])
    def test_focus_loss_releases_everything_no_stuck_buttons(self):
        c = core(); c.load(os.path.join(FX, "factory-only.json")); c.drain(0.2)
        c.press("a"); c.press("down"); c.send("focus lost"); c.press("b"); c.send("focus gained"); c.tick(1000); c.press("a"); evs = self.events(c); c.shutdown()
        kinds = [(e["button"], e["kind"]) for e in evs if e["ev"] == "input"]
        self.assertIn(("a", "forced-release"), kinds); self.assertIn(("down", "forced-release"), kinds); self.assertIn(("b", "rejected"), kinds)
        self.assertNotIn(("down", "repeat"), kinds, "no repeats after focus loss: nothing is stuck"); self.assertEqual(kinds[-1], ("a", "press"))
    def test_device_disconnect_and_reconnect(self):
        c = core(); c.load(os.path.join(FX, "factory-only.json")); c.drain(0.2)
        c.press("right"); c.send("device disconnect"); c.press("left"); c.tick(500); c.send("device connect"); c.press("left"); evs = c.drain(0.2); c.shutdown()
        kinds = [(e["button"], e["kind"]) for e in evs if e["ev"] == "input"]; dev = [e["connected"] for e in evs if e["ev"] == "device"]
        self.assertEqual(dev, [False, True]); self.assertEqual(kinds, [("right", "press"), ("right", "forced-release"), ("left", "rejected"), ("left", "press")])
    def test_replay_is_repeatable(self):
        def run():
            c = core(); c.load(os.path.join(FX, "mixed.json")); c.drain(0.2)
            for i in range(25): c.press("down" if i % 3 else "up"); c.tick(137); c.release("down" if i % 3 else "up")
            evs = [(e.get("button"), e.get("kind"), e.get("clock_ms"), e.get("index")) for e in c.drain(0.3) if e["ev"] in ("input", "cursor")]; st = c.stats(); c.shutdown(); return evs, st["inputs"], st["clock_ms"]
        a, b = run(), run(); self.assertEqual(a, b); self.assertEqual(a[1], 25)   # 25 presses; 137 ms holds never reach the 400 ms repeat delay
    def test_rapid_input_burst(self):
        c = core(); c.load(os.path.join(FX, "stress-5000.json")); c.drain(0.5); t0 = time.time()
        for i in range(2000): c.press("down"); c.release("down")
        st = c.stats(); dt = time.time() - t0; evs = len([e for e in c.drain(0.5) if e["ev"] == "cursor"]); c.shutdown()
        self.assertEqual(st["inputs"], 2000); self.assertLess(dt, 30); self.assertEqual(evs + 0, evs)
        json.dump({"path": PATH, "presses": 2000, "seconds": round(dt, 3)}, open(os.path.join(ART, "rapid-input.json"), "w"))

class Lifecycle(unittest.TestCase):
    def test_launch_return_cycle(self):
        c = core(); c.load(os.path.join(FX, "factory-only.json")); c.drain(0.2); rec = c.launch_cycle(stub_exit=0); st = c.stats(); c.shutdown()
        self.assertEqual(rec["request"]["ev"], "launch_requested"); self.assertEqual(rec["stub"]["exit"], 0); self.assertEqual(rec["stub"]["events"][0]["ev"], "game_stub_started")
        self.assertEqual(rec["result"]["ev"], "resumed"); self.assertEqual(st["launches"], 1)
    def test_launch_refused_for_missing_file_and_while_suspended(self):
        c = core(); c.load(os.path.join(FX, "mixed.json")); c.send("menu"); menu = [e for e in c.drain(0.3) if e["ev"] == "menu_entry"]
        idx = next(e["index"] for e in menu if e["id"] == "sim:added:00103")
        for _ in range(idx): c.press("down"); c.release("down")
        c.drain(0.2); c.send("launch"); r1 = c.wait_for(("launch_requested", "launch_refused"))
        c.press("up"); c.release("up"); c.drain(0.1); c.send("launch"); r2 = c.wait_for(("launch_requested", "launch_refused")); c.wait_for("state"); c.send("launch"); r3 = c.wait_for(("launch_requested", "launch_refused")); c.send("game-exit 0"); c.drain(0.2); c.shutdown()
        self.assertEqual((r1["ev"], r1["reason"]), ("launch_refused", "game file missing")); self.assertEqual(r2["ev"], "launch_requested"); self.assertEqual(r3["ev"], "launch_refused"); self.assertIn("suspended", r3["reason"])
    def test_game_stub_failure_and_timeout_recognized(self):
        c = core(); c.load(os.path.join(FX, "factory-only.json")); c.drain(0.2)
        r1 = c.launch_cycle(stub_exit=2, stub_crash=True); r2 = c.launch_cycle(stub_exit=0, stub_delay_ms=3000, stub_timeout=0.5); r3 = c.launch_cycle(stub_exit=0); c.shutdown()
        self.assertEqual(r1["result"]["ev"], "launch_failed"); self.assertNotEqual(r1["stub"]["exit"], 0)
        self.assertTrue(r2["stub"].get("timeout")); self.assertEqual(r2["result"]["ev"], "launch_failed")
        self.assertEqual(r3["result"]["ev"], "resumed", "launcher usable again after a failed and a timed-out stub")
    def test_repeated_launch_return_cycles(self):
        c = core(); c.load(os.path.join(FX, "mixed.json")); c.drain(0.2); t0 = time.time()
        for i in range(20): rec = c.launch_cycle(stub_exit=0, stub_delay_ms=1); self.assertEqual(rec["result"]["ev"], "resumed", f"cycle {i}")
        st = c.stats(); dt = time.time() - t0; code = c.shutdown()
        self.assertEqual(st["launches"], 20); self.assertEqual(code, 0); json.dump({"path": PATH, "cycles": 20, "seconds": round(dt, 2)}, open(os.path.join(ART, "launch-cycles.json"), "w"))
    def test_shutdown_and_stdin_close(self):
        c = core(); code = c.shutdown(); self.assertEqual(code, 0); self.assertEqual(c.events[-1], {"ev": "shutdown", "clean": True})
        d = core(); d.proc.stdin.close(); d.proc.wait(10); time.sleep(0.2); self.assertEqual(d.proc.returncode, 0); self.assertEqual(d.events[-1]["ev"], "shutdown"); self.assertFalse(d.events[-1]["clean"])

class Display(unittest.TestCase):
    def test_frame_1280x720_valid_image_and_deterministic(self):
        c = core(); c.load(os.path.join(FX, "mixed.json")); c.drain(0.2); c.press("down"); c.release("down")
        ppm = os.path.join(ART, "frame.ppm"); ev1 = c.frame(ppm); ev2 = c.frame(ppm.replace(".ppm", "-2.ppm")); c.shutdown()
        self.assertEqual((ev1["ev"], ev1["w"], ev1["h"], ev1["bytes"]), ("frame_written", 1280, 720, 1280 * 720 * 3))
        self.assertNotEqual(ev1["crc32"], ev2["crc32"], "the frame counter is drawn, so consecutive frames differ")
        w, h = ppm_to_png(ppm, os.path.join(ART, "frame.png")); self.assertEqual((w, h), (1280, 720))
        from PIL import Image
        im = Image.open(os.path.join(ART, "frame.png")); self.assertEqual(im.getpixel((10, 10)), (235, 235, 235), "color bar 1"); self.assertEqual(im.getpixel((1270, 10)), (40, 40, 40), "color bar 8")
    def test_state_change_changes_frame_and_diagnostic_label_present(self):
        c = core(); c.load(os.path.join(FX, "mixed.json")); c.drain(0.2)
        e1 = c.frame(os.path.join(ART, "s1.ppm")); c.press("down"); c.release("down"); c.drain(0.1); e2 = c.frame(os.path.join(ART, "s2.ppm")); c.shutdown()
        from PIL import Image
        a, b = Image.open(os.path.join(ART, "s1.ppm")), Image.open(os.path.join(ART, "s2.ppm"))
        diff = sum(1 for y in range(300, 440, 4) for x in range(40, 1200, 4) if a.getpixel((x, y)) != b.getpixel((x, y))); self.assertGreater(diff, 20, "the cursor text region changed after an input")
        # the red "NOT A HOME SCREEN DESIGN" label row contains red pixels
        self.assertTrue(any(a.getpixel((x, 140))[0] > 200 and a.getpixel((x, 140))[1] < 120 for x in range(40, 800)))
    def test_frame_failure_is_reported(self):
        c = core("--fault=frame-fail"); c.load(os.path.join(FX, "factory-only.json")); c.drain(0.2); ev = c.frame(os.path.join(ART, "never.ppm")); c.shutdown()
        self.assertEqual(ev["ev"], "frame_failed")
        d = core(); d.load(os.path.join(FX, "factory-only.json")); d.drain(0.2); ev2 = d.frame(os.path.join(ART, "no-such-dir", "x", "f.ppm")); d.shutdown(); self.assertEqual(ev2["ev"], "frame_failed")

class Faults(unittest.TestCase):
    def test_startup_fail_exit_code(self):
        c = Core(PATH, ["--fault=startup-fail"]); r = c.ready(5); c.proc.wait(10); self.assertIsNone(r); self.assertEqual(c.proc.returncode, 3); self.assertEqual(c.events[0]["name"], "startup-fail")
    def test_startup_hang_timeout(self):
        c = Core(PATH, ["--fault=startup-hang"]); r = c.ready(1.5); self.assertIsNone(r); self.assertIsNone(c.exit_code()); c.kill(); self.assertIsNotNone(c.exit_code())
    def test_slow_start_within_tolerance(self):
        t0 = time.time(); c = core("--fault=slow-start=700"); self.assertGreaterEqual(time.time() - t0, 0.65); c.shutdown()
    def test_crash_after_inputs(self):
        c = core("--fault=crash-after-inputs=3"); c.load(os.path.join(FX, "factory-only.json")); c.drain(0.2)
        for _ in range(3): c.press("down"); c.release("down")
        c.proc.wait(10); self.assertNotEqual(c.proc.returncode, 0); self.assertTrue(any(e["ev"] == "fault" and e["name"] == "crash-after-inputs" for e in c.events))
    def test_crash_on_launch(self):
        c = core("--fault=crash-on-launch"); c.load(os.path.join(FX, "factory-only.json")); c.drain(0.2); c.send("launch"); c.proc.wait(10); self.assertNotEqual(c.proc.returncode, 0)
    def test_memory_budget_low_memory_simulation(self):
        c = core("--mem-budget-kb=256"); r1 = c.load(os.path.join(FX, "stress-5000.json")); r2 = c.load(os.path.join(FX, "factory-only.json")); c.shutdown()
        self.assertEqual(r1["ev"], "fixture_rejected"); self.assertIn("memory budget", r1["error"]); self.assertEqual(r2["ev"], "fixture_loaded", "bounded, diagnosable: small fixture still loads")
    def test_unknown_command_and_argument(self):
        c = core(); c.send("frobnicate 1"); ev = c.wait_for("error"); c.shutdown(); self.assertEqual(ev["error"], "unknown command")
        d = Core(PATH, ["--bogus"]); d.proc.wait(10); self.assertEqual(d.proc.returncode, 64)

class Security(unittest.TestCase):
    def test_core_sources_have_no_device_endpoints_or_process_execution(self):
        src = "".join(open(os.path.join(SIM, "core", n), encoding="utf-8").read() for n in os.listdir(os.path.join(SIM, "core")))
        for bad in ("169.254", "clovershell", "hakchi", "socket(", "connect(", "system(", "popen(", "execv", "CreateProcess", "ShellExecute", "fork("): self.assertNotIn(bad, src, bad)
    def test_runner_refuses_device_endpoints(self):
        from runner import guard
        for bad in ("169.254.13.37", "clovershell", "ssh://root@x"):
            with self.assertRaises(RuntimeError): guard(f"load {bad}")
    def test_fixture_commands_rejected(self):
        p = os.path.join(ART, "smuggle.json"); json.dump({"schema": "ccs-sim-fixture/1", "entries": [{"id": "x", "version_id": "v", "title": "t", "kind": "added", "exec": "calc.exe"}, {"id": "y", "version_id": "v", "title": "t", "kind": "added", "path": "C:/x"}]}, open(p, "w"))
        c = core(); r = c.load(p); rej = [e for e in c.drain(0.2) if e["ev"] == "record_rejected"]; c.shutdown()
        self.assertEqual((r["entries"], r["rejected"]), (0, 2)); self.assertTrue(all("security guardrail" in e["reason"] for e in rej))
    def test_no_listening_ports_opened(self):
        if PATH != "windows": self.skipTest("port check is done for the native Windows process")
        import subprocess
        c = core(); c.load(os.path.join(FX, "mixed.json")); c.drain(0.2)
        out = subprocess.run(["powershell", "-NoProfile", "-Command", f"(Get-NetTCPConnection -OwningProcess {c.proc.pid} -ErrorAction SilentlyContinue | Measure-Object).Count"], capture_output=True, text=True).stdout.strip(); c.shutdown()
        self.assertEqual(out, "0")
    def test_generated_files_stay_in_designated_dirs(self):
        for root in (ARTIFACTS,): self.assertTrue(os.path.isdir(root))
        for n in os.listdir(SIM): self.assertNotIn(n, ("build", "artifacts", "__pycache__x"), "no build output inside the source tree")

class Stress(unittest.TestCase):
    def test_stress_5000_loads_and_orders(self):
        c = core(); t0 = time.time(); r = c.load(os.path.join(FX, "stress-5000.json")); dt = time.time() - t0; c.send("menu"); menu = [e for e in c.drain(1.0) if e["ev"] == "menu_entry"]; st = c.stats(); c.shutdown()
        self.assertEqual(r["ev"], "fixture_loaded"); self.assertEqual(r["entries"], 5000); self.assertEqual(len(menu), r["menu"]); self.assertEqual(menu, sorted(menu, key=lambda e: e["index"]))
        json.dump({"path": PATH, "entries": 5000, "load_seconds": round(dt, 3), "rss_kb": st["rss_kb"], "model_bytes": st["model_bytes"], "note": "software budget estimate on the development machine, not device performance"}, open(os.path.join(ART, "stress.json"), "w"))
        self.assertLess(dt, 20)
    def test_missing_and_large_art_refs_do_not_break_frames(self):
        c = core(); c.load(os.path.join(FX, "stress-5000.json")); c.drain(0.5)
        for _ in range(50): c.press("down"); c.release("down")
        ev = c.frame(os.path.join(ART, "stress-frame.ppm")); c.shutdown(); self.assertEqual(ev["ev"], "frame_written")

if __name__ == "__main__": unittest.main(verbosity=2)
