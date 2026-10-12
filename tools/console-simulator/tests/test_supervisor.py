"""Mock console lifecycle under an EXTERNAL supervisor: start, startup failure, hang/timeout, crash mid-operation, crash
loop -> fallback stub (outside the launcher process), power loss -> clean restart, malformed data recovery, game stub
failure/cancel. Journals are kept as artifacts."""
from __future__ import annotations
import json, os, sys, unittest
HERE = os.path.dirname(os.path.abspath(__file__)); SIM = os.path.dirname(HERE); sys.path.insert(0, os.path.join(SIM, "host"))
from supervisor import Supervisor, STOCK_STUB_NAME
from runner import ARTIFACTS
PATH = os.environ.get("CCS_SIM_TEST_PATH", "windows"); FX = os.path.join(SIM, "fixtures", "mixed.json"); ART = os.path.join(ARTIFACTS, "tests", PATH, "supervisor"); os.makedirs(ART, exist_ok=True)

def run(scenario: str, **kw) -> tuple[dict, list[dict]]:
    s = Supervisor(PATH, FX, os.path.join(ART, f"{scenario}.jsonl"), startup_timeout=kw.pop("startup_timeout", 3.0), **kw); return s.run(scenario), s.journal

class SupervisorTests(unittest.TestCase):
    def ev(self, j, name): return [e for e in j if e["event"] == name]
    def test_normal_start_and_clean_exit(self):
        s, j = run("normal"); self.assertEqual((s["outcome"], s["attempts"], s["fallback_used"]), ("clean", 1, False)); self.assertEqual(self.ev(j, "launcher_clean_exit")[0]["exit_code"], 0); self.assertEqual(self.ev(j, "game_stub_cycle")[0]["result"], "resumed")
    def test_startup_failure_then_crash_loop_fallback(self):
        s, j = run("startup-fail"); self.assertEqual((s["outcome"], s["attempts"], s["failures"], s["fallback_used"]), ("fallback", 3, 3, True))
        self.assertEqual([e["exit_code"] for e in self.ev(j, "launcher_startup_exit")], [3, 3, 3]); self.assertEqual(self.ev(j, "fallback_started")[0]["launcher"], STOCK_STUB_NAME); self.assertTrue(self.ev(j, "crash_loop_detected"))
    def test_startup_timeout_then_clean_restart(self):
        s, j = run("startup-hang"); self.assertEqual((s["outcome"], s["attempts"]), ("clean", 2)); self.assertTrue(self.ev(j, "launcher_startup_timeout")); self.assertEqual(self.ev(j, "launcher_startup_timeout")[0]["decision"], "killed")
    def test_crash_mid_operation_then_clean_restart(self):
        s, j = run("crash-mid"); self.assertEqual((s["outcome"], s["attempts"], s["failures"]), ("clean", 2, 1)); self.assertNotEqual(self.ev(j, "launcher_exited_mid_operation")[0]["exit_code"], 0)
    def test_crash_loop_detection_triggers_fallback_outside_launcher(self):
        s, j = run("crash-loop"); self.assertEqual((s["outcome"], s["failures"], s["fallback_used"]), ("fallback", 3, True))
        loop = self.ev(j, "crash_loop_detected")[0]; self.assertEqual(loop["decision"], "fallback"); self.assertGreater(self.ev(j, "fallback_ready")[0]["t_ms"], loop["t_ms"])
    def test_power_loss_then_recovery(self):
        s, j = run("power-loss"); self.assertEqual(s["outcome"], "recovered"); self.assertTrue(self.ev(j, "power_loss_simulated")); self.assertTrue(self.ev(j, "restart_after_power_loss")); self.assertEqual(self.ev(j, "launcher_clean_exit")[-1]["exit_code"], 0)
    def test_malformed_collection_recovery(self):
        s, j = run("malformed"); self.assertEqual(s["outcome"], "clean"); self.assertTrue(self.ev(j, "fixture_rejected")); self.assertTrue(self.ev(j, "recovery_malformed_data")); self.assertEqual(self.ev(j, "fixture_loaded")[-1]["detail"]["entries"], 30)
    def test_game_stub_failure_handled_and_launcher_continues(self):
        s, j = run("game-fail"); self.assertEqual(s["outcome"], "clean"); h = self.ev(j, "game_stub_failed_handled")[0]; self.assertEqual(h["result"], "launch_failed"); self.assertNotEqual(h["stub"], 0); self.assertEqual(self.ev(j, "game_stub_cycle")[0]["result"], "resumed")
    def test_journal_has_timing_and_structured_codes(self):
        s, j = run("crash-mid"); self.assertTrue(all("t_ms" in e for e in j)); self.assertTrue(os.path.exists(s["journal"])); lines = [json.loads(l) for l in open(s["journal"], encoding="utf-8")]; self.assertEqual(len(lines), len(j))

if __name__ == "__main__": unittest.main(verbosity=2)
