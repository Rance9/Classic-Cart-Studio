"""Console engine runs for the backend: one isolated worker process per run, the line protocol over pipes, an in-memory
event buffer the UI polls, and the plan folder under the backend's data root. Desktop-principal only (enforced in
nes_companion). The worker is this same program in worker mode (`--console-worker`, frozen build) or the Python module
(development), never a path supplied by a client. Gate G1 stays closed inside the worker; the backend adds nothing that
could open it.
"""
from __future__ import annotations
import json, os, subprocess, sys, threading, time, uuid

from common import DATA_ROOT, FROZEN, data_path

ROOT = data_path("console")
RUNS: dict[str, dict] = {}
LOCK = threading.Lock()
MAX_RUNS, MAX_EVENTS, RUN_TTL = 20, 4000, 6 * 3600
PROCS: list[subprocess.Popen] = []


def worker_command() -> list[str]:
    if FROZEN: return [sys.executable, "--console-worker", "--root", ROOT]
    here = os.path.dirname(os.path.abspath(__file__))
    return [sys.executable, "-m", "console_engine.worker", "--root", ROOT]


def _prune():
    now = time.time()
    with LOCK:
        for rid in [r for r, v in RUNS.items() if v["done"] and now - v["started"] > RUN_TTL]: RUNS.pop(rid, None)
        while len(RUNS) > MAX_RUNS:
            oldest = min(RUNS, key=lambda r: RUNS[r]["started"]); RUNS.pop(oldest, None)


def call(cmd: str, args: dict, timeout: float = 120.0) -> dict:
    """Synchronous one-shot command (capabilities, identify, plan, preflight, verify, execute, recover): spawn, ask, collect."""
    os.makedirs(ROOT, exist_ok=True)
    env = dict(os.environ); env["PYTHONPATH"] = os.pathsep.join([os.path.dirname(os.path.abspath(__file__)), os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "rom-pipeline"), env.get("PYTHONPATH", "")])
    p = subprocess.Popen(worker_command(), stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8", env=env, cwd=os.path.dirname(os.path.abspath(__file__)))
    PROCS.append(p); rid = uuid.uuid4().hex[:8]; events = []; result = None
    try:
        p.stdin.write(json.dumps({"v": 1, "id": rid, "cmd": cmd, "args": args}, ensure_ascii=False) + "\n"); p.stdin.flush(); p.stdin.write("quit\n"); p.stdin.flush()
        deadline = time.time() + timeout
        for line in p.stdout:
            if time.time() > deadline: break
            try: ev = json.loads(line)
            except ValueError: continue
            if ev.get("event") in ("result", "error"): result = ev; break
            if len(events) < MAX_EVENTS: events.append(ev)
    finally:
        try: p.stdin.close()
        except Exception: pass
        try: p.wait(timeout=10)
        except Exception: p.kill()
        PROCS[:] = [q for q in PROCS if q.poll() is None]
    if result is None: return {"event": "error", "code": "worker-no-reply", "text": "the console worker gave no answer", "stderr": (p.stderr.read() or "")[-500:] if p.stderr else ""}
    return result


def start(cmd: str, args: dict) -> str:
    """Streaming command (simulate): returns a run id; events accumulate for polling."""
    _prune(); os.makedirs(ROOT, exist_ok=True); rid = time.strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:6]
    run = {"id": rid, "cmd": cmd, "started": time.time(), "events": [], "done": False, "result": None, "proc": None, "state": "starting"}
    with LOCK: RUNS[rid] = run

    def pump():
        env = dict(os.environ); env["PYTHONPATH"] = os.pathsep.join([os.path.dirname(os.path.abspath(__file__)), os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "rom-pipeline"), env.get("PYTHONPATH", "")])
        try:
            p = subprocess.Popen(worker_command(), stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8", env=env, cwd=os.path.dirname(os.path.abspath(__file__)))
        except Exception as e:
            with LOCK: run.update(done=True, state="failed", result={"event": "error", "code": "worker-spawn", "text": str(e)}); return
        PROCS.append(p); run["proc"] = p
        try:
            p.stdin.write(json.dumps({"v": 1, "id": rid, "cmd": cmd, "args": args}, ensure_ascii=False) + "\n"); p.stdin.flush()
            with LOCK: run["state"] = "running"
            for line in p.stdout:
                try: ev = json.loads(line)
                except ValueError: continue
                with LOCK:
                    if ev.get("event") in ("result", "error"): run["result"] = ev; run["done"] = True; run["state"] = "finished" if ev["event"] == "result" else "failed"
                    elif len(run["events"]) < MAX_EVENTS: run["events"].append(ev)
                    if ev.get("event") == "stage": run["state"] = "running: " + str(ev.get("stage"))
                if run["done"]: break
            if not run["done"]:
                with LOCK: run.update(done=True, state="failed", result={"event": "error", "code": "worker-ended", "text": "the console worker ended without a result"})
        finally:
            try: p.stdin.write("quit\n"); p.stdin.flush(); p.stdin.close()
            except Exception: pass
            try: p.wait(timeout=10)
            except Exception: p.kill()
            PROCS[:] = [q for q in PROCS if q.poll() is None]
    threading.Thread(target=pump, daemon=True, name="console-run-" + rid).start(); return rid


def poll(rid: str, cursor: int = 0) -> dict | None:
    with LOCK:
        run = RUNS.get(rid)
        if not run: return None
        evs = run["events"][cursor:]
        return {"id": rid, "state": run["state"], "done": run["done"], "events": evs, "cursor": cursor + len(evs), "result": run["result"]}


def cancel(rid: str) -> dict:
    with LOCK: run = RUNS.get(rid)
    if not run or run["done"] or not run.get("proc"): return {"ok": False, "error": "no running operation with that id"}
    try:
        run["proc"].stdin.write(json.dumps({"v": 1, "id": rid + "-cancel", "cmd": "cancel-if-safe", "args": {}}) + "\n"); run["proc"].stdin.flush(); return {"ok": True, "requested": True}
    except Exception as e: return {"ok": False, "error": str(e)}


def shutdown():
    """Called when the backend stops: no worker outlives the app."""
    for p in list(PROCS):
        try:
            if p.poll() is None: p.kill()
        except Exception: pass
    PROCS.clear()
