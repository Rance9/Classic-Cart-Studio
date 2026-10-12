"""External process supervisor for the simulated launcher (the recovery testbed, NOT a console boot hook).

Runs OUTSIDE the launcher process: starts the core, waits for its ready event within a startup timeout, watches it, and on
failure decides: restart (clean restart after a crash), or fall back to the STOCK LAUNCHER STUB (a plainly labeled mock,
never Nintendo's Clover binary) once a crash loop is detected (N failures within a window). Every decision goes to a JSON
lines journal with timing and structured exit codes; fault injection is passed through to the core (--fault=...).

Nothing here touches a device, and nothing here is meant to be installed on one: the real boot-time supervision of a
future launcher on the console is a separate, unapproved, hardware phase (see ARCHITECTURE.md section "What stays mock").

  python supervisor.py --path windows --fixture ../fixtures/mixed.json --scenario crash-loop
  scenarios: normal | startup-fail | startup-hang | crash-mid | crash-loop | power-loss | malformed | game-fail
"""
from __future__ import annotations
import argparse, json, os, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from runner import Core, ARTIFACTS, SIM

STOCK_STUB_NAME = "stock-launcher-stub (MOCK, not Clover)"

class Supervisor:
    def __init__(self, path: str, fixture: str, journal_path: str, startup_timeout: float = 5.0, max_failures: int = 3, window_s: float = 30.0, restart_delay: float = 0.05, core_args: list[str] | None = None):
        self.path, self.fixture, self.journal_path = path, fixture, journal_path
        self.startup_timeout, self.max_failures, self.window_s, self.restart_delay = startup_timeout, max_failures, window_s, restart_delay
        self.core_args = list(core_args or []); self.failures: list[float] = []; self.journal: list[dict] = []; self.t0 = time.time(); self.fallback_used = False
        os.makedirs(os.path.dirname(journal_path), exist_ok=True); open(journal_path, "w").close()

    def log(self, **rec):
        rec = {"t_ms": int((time.time() - self.t0) * 1000), **rec}; self.journal.append(rec)
        with open(self.journal_path, "a", encoding="utf-8") as f: f.write(json.dumps(rec, default=str) + "\n")
        return rec

    def start_once(self, args: list[str]) -> tuple[Core | None, str]:
        """start the launcher; returns (core, outcome) with outcome in ready | startup-timeout | startup-exit:<code>"""
        t = time.time(); c = Core(self.path, args, timeout=self.startup_timeout)
        ev = c.ready(self.startup_timeout)
        if ev: self.log(event="launcher_ready", ms=int((time.time() - t) * 1000), build=ev.get("build"), arch=ev.get("arch")); return c, "ready"
        code = c.exit_code()
        if code is None:
            try: c.proc.wait(timeout=0.5); code = c.exit_code()   # a failed start may still be exiting when the ready wait gives up
            except Exception: code = None
        if code is None: c.kill(); self.log(event="launcher_startup_timeout", timeout_s=self.startup_timeout, decision="killed"); return None, "startup-timeout"
        self.log(event="launcher_startup_exit", exit_code=code, stderr=c.stderr[-300:]); return None, f"startup-exit:{code}"

    def failure(self, why: str) -> str:
        """record a failure; decide restart or fallback. Returns 'restart' or 'fallback'."""
        now = time.time(); self.failures = [f for f in self.failures if now - f <= self.window_s] + [now]
        if len(self.failures) >= self.max_failures:
            self.log(event="crash_loop_detected", failures=len(self.failures), window_s=self.window_s, decision="fallback", why=why); return "fallback"
        self.log(event="launcher_failure", failures=len(self.failures), decision="restart", why=why, delay_s=self.restart_delay); time.sleep(self.restart_delay); return "restart"

    def fallback(self):
        """the mock stock launcher: a labeled stub that proves the supervisor can hand over control outside the broken process"""
        self.fallback_used = True; self.log(event="fallback_started", launcher=STOCK_STUB_NAME, note="MOCK fallback; a real device fallback is a future hardware phase")
        self.log(event="fallback_ready", launcher=STOCK_STUB_NAME)

    def run_session(self, c: Core, scenario: str) -> str:
        """drive one launcher session; returns 'clean' | 'crashed' | 'power-loss'"""
        ld = c.load(self.fixture)
        if not ld or ld["ev"] != "fixture_loaded":
            self.log(event="fixture_rejected", detail=ld); self.log(event="recovery_malformed_data", decision="restart with the last known-good fixture", fixture=os.path.join(SIM, "fixtures", "factory-only.json"))
            ld = c.load(os.path.join(SIM, "fixtures", "factory-only.json")); self.log(event="fixture_loaded", detail={k: ld.get(k) for k in ("name", "entries", "menu", "rejected")} if ld else None)
        else: self.log(event="fixture_loaded", detail={k: ld.get(k) for k in ("name", "entries", "menu", "rejected")})
        for i in range(3):
            c.press("down"); c.tick(50); c.release("down")
            if c.exit_code() is not None: self.log(event="launcher_exited_mid_operation", exit_code=c.exit_code(), stderr=c.stderr[-200:]); return "crashed"
        if scenario == "game-fail":
            rec = c.launch_cycle(stub_exit=2, stub_crash=True, stub_timeout=5); self.log(event="game_stub_failed_handled", result=(rec.get("result") or {}).get("ev"), stub=rec.get("stub", {}).get("exit"))
            rec = c.launch_cycle(stub_exit=0); self.log(event="game_stub_cycle", result=(rec.get("result") or {}).get("ev"))
        else:
            rec = c.launch_cycle(stub_exit=0); self.log(event="game_stub_cycle", result=(rec.get("result") or {}).get("ev"), stub_ms=rec.get("stub", {}).get("ms"))
        if c.exit_code() is not None: self.log(event="launcher_exited_mid_operation", exit_code=c.exit_code()); return "crashed"
        if scenario == "power-loss":
            self.log(event="power_loss_simulated", decision="kill launcher without shutdown (as a power cut would)"); c.kill(); return "power-loss"
        code = c.shutdown(); self.log(event="launcher_clean_exit", exit_code=code); return "clean"

    def run(self, scenario: str) -> dict:
        args = list(self.core_args)
        fixture = self.fixture
        if scenario == "startup-fail": args.append("--fault=startup-fail")
        if scenario == "startup-hang": args.append("--fault=startup-hang")
        if scenario in ("crash-mid", "crash-loop"): args.append("--fault=crash-after-inputs=2")
        if scenario == "malformed": self.fixture = os.path.join(SIM, "fixtures", "not-json.json")
        self.log(event="supervisor_start", scenario=scenario, path=self.path, args=args, max_failures=self.max_failures, window_s=self.window_s)
        attempts = 0; outcome = None
        while True:
            attempts += 1; self.log(event="launcher_start_attempt", attempt=attempts)
            c, why = self.start_once(args)
            if c is None:
                if scenario == "startup-hang" and attempts == 1: args = [a for a in args if a != "--fault=startup-hang"]; self.log(event="fault_cleared_for_restart", note="hang fault removed to show a clean restart after the timeout")
                if self.failure(why) == "fallback": self.fallback(); outcome = "fallback"; break
                continue
            res = self.run_session(c, scenario)
            if res == "clean": outcome = "clean"; break
            if res == "power-loss":
                self.log(event="restart_after_power_loss", decision="clean restart; fixture reloaded from disk"); args = [a for a in args if not a.startswith("--fault")]
                c2, why2 = self.start_once(args)
                if c2: self.run_session(c2, "normal"); outcome = "recovered"
                else: outcome = "fallback" if self.failure(why2) == "fallback" else "unrecovered"
                break
            if scenario == "crash-mid": args = [a for a in args if not a.startswith("--fault")]; self.log(event="fault_cleared_for_restart", note="crash fault removed to show a clean restart")
            if self.failure("crashed mid-operation") == "fallback": self.fallback(); outcome = "fallback"; break
        self.fixture = fixture
        summary = {"schema": "ccs-sim-supervisor-run/1", "scenario": scenario, "path": self.path, "outcome": outcome, "attempts": attempts, "failures": len(self.failures), "fallback_used": self.fallback_used, "journal": self.journal_path, "elapsed_ms": int((time.time() - self.t0) * 1000)}
        self.log(event="supervisor_end", **{k: v for k, v in summary.items() if k != "journal"}); return summary

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--path", default="windows"); ap.add_argument("--fixture", default=os.path.join(SIM, "fixtures", "mixed.json")); ap.add_argument("--scenario", default="normal")
    ap.add_argument("--out", default=os.path.join(ARTIFACTS, "supervisor")); ap.add_argument("--max-failures", type=int, default=3); ap.add_argument("--startup-timeout", type=float, default=5.0)
    a = ap.parse_args(); j = os.path.join(a.out, f"{a.scenario}-{a.path}.jsonl")
    s = Supervisor(a.path, a.fixture, j, startup_timeout=a.startup_timeout, max_failures=a.max_failures); print(json.dumps(s.run(a.scenario)))

if __name__ == "__main__": main()
