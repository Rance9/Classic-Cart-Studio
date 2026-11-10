"""Console engine runs for the backend: one isolated worker process per run, the line protocol over pipes, an in-memory
event buffer the UI polls, and the plan folder under the backend's data root. Desktop-principal only (enforced in
nes_companion). The worker is this same program in worker mode (`--console-worker`, frozen build) or the Python module
(development), never a path supplied by a client. Gate G1 stays closed inside the worker; the backend adds nothing that
could open it. call() and start() take a spawn function only from Python (console_home.Dispatcher's tests: an in-process
worker over pipes with an already simulated console); the app never passes one.
Lock order: console_home.Dispatcher may hold its lock while it takes LOCK here (start, poll), never the reverse; LOCK is never
held while a run's on_done is called (it takes the dispatcher's lock).
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
    if FROZEN: return [sys.executable, "--console-worker", "--root", ROOT]   # (worker mode adds the staged console package itself)
    from native_home import package as nhp
    return [sys.executable, "-m", "console_engine.worker", "--root", ROOT, "--console-package", os.path.join(nhp.package_root(), "console", "current")]


def _prune():
    now = time.time()
    with LOCK:
        for rid in [r for r, v in RUNS.items() if v["done"] and now - v["started"] > RUN_TTL]: RUNS.pop(rid, None)
        while len(RUNS) > MAX_RUNS:   # finished runs only: one still running stays visible to whoever polls it
            old = [r for r, v in RUNS.items() if v["done"]]
            if not old: break
            RUNS.pop(min(old, key=lambda r: RUNS[r]["started"]), None)


def _spawn() -> subprocess.Popen:
    env = dict(os.environ); env["PYTHONPATH"] = os.pathsep.join([os.path.dirname(os.path.abspath(__file__)), os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "rom-pipeline"), env.get("PYTHONPATH", "")])
    return subprocess.Popen(worker_command(), stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8", env=env, cwd=os.path.dirname(os.path.abspath(__file__)))


def call(cmd: str, args: dict, timeout: float = 120.0, spawn=None) -> dict:
    """Synchronous one-shot command (capabilities, identify, plan, preflight, verify, execute, recover): spawn, ask, collect."""
    os.makedirs(ROOT, exist_ok=True)
    p = (spawn or _spawn)()
    PROCS.append(p); rid = uuid.uuid4().hex[:8]; events = []; result = None
    try:
        p.stdin.write(json.dumps({"v": 1, "id": rid, "cmd": cmd, "args": args}, ensure_ascii=False) + "\n"); p.stdin.flush(); p.stdin.write("quit\n"); p.stdin.flush()
        deadline = time.time() + timeout
        for line in p.stdout:
            if time.time() > deadline: break
            try: ev = json.loads(line)
            except ValueError: continue
            if ev.get("event") in ("result", "error") and ev.get("id") in (rid, None): result = ev; break
            if len(events) < MAX_EVENTS: events.append(ev)
    finally:
        try: p.stdin.close()
        except Exception: pass
        try: p.wait(timeout=10)
        except Exception: p.kill()
        PROCS[:] = [q for q in PROCS if q.poll() is None]
    if result is None: return {"event": "error", "code": "worker-no-reply", "text": "the console worker gave no answer", "stderr": (p.stderr.read() or "")[-500:] if p.stderr else ""}
    return result


def start(cmd: str, args: dict, spawn=None, on_done=None) -> str:
    """Streaming command (simulate, the physical operations): returns a run id; events accumulate for polling. Its answer is kept as it
    arrives, but the run is done only after its worker has stopped (or was ended) and on_done(result event, stopped) has returned, so
    nobody sees it finished while the dispatcher still counts it as running. on_done is called once, whatever happened (no worker, no
    answer, a cancel, a worker that would not stop: stopped is False then), and never from inside start()."""
    _prune(); os.makedirs(ROOT, exist_ok=True); rid = time.strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:6]
    run = {"id": rid, "cmd": cmd, "started": time.time(), "events": [], "done": False, "result": None, "proc": None, "state": "starting"}
    with LOCK: RUNS[rid] = run

    def answer(ev):   # this run's own answer; the run goes on until its worker has stopped
        run.update(result=ev, final="finished" if ev.get("event") == "result" else "failed", state="finishing")
    def pump():
        p, stopped = None, True
        try:
            try: p = (spawn or _spawn)()
            except Exception as e:
                with LOCK: answer({"event": "error", "code": "worker-spawn", "text": str(e)})
                return
            PROCS.append(p); run["proc"] = p
            p.stdin.write(json.dumps({"v": 1, "id": rid, "cmd": cmd, "args": args}, ensure_ascii=False) + "\n"); p.stdin.flush()
            with LOCK: run["state"] = "running"
            for line in p.stdout:
                try: ev = json.loads(line)
                except ValueError: continue
                with LOCK:   # only this run's own answer ends it (a cancel's acknowledgment has its own id and is kept as an event)
                    if ev.get("id") == rid + "-cancel": run["events"].append({**ev, "event": "cancel-ack"})
                    elif ev.get("event") in ("result", "error") and ev.get("id") in (rid, None): answer(ev)
                    elif len(run["events"]) < MAX_EVENTS: run["events"].append(ev)
                    if ev.get("event") == "stage" and run["result"] is None: run["state"] = "running: " + str(ev.get("stage"))
                if run["result"] is not None: break
        except Exception: pass   # the worker went away while being asked: no answer, below
        finally:
            with LOCK:
                if run["result"] is None: answer({"event": "error", "code": "worker-ended", "text": "the console worker ended without a result"})
            if p is not None:
                try: p.stdin.write("quit\n"); p.stdin.flush(); p.stdin.close()
                except Exception: pass
                try: p.wait(timeout=10)
                except Exception:
                    try: p.kill(); p.wait(timeout=5)
                    except Exception: stopped = p.poll() is not None
                PROCS[:] = [q for q in PROCS if q.poll() is None]
            try:
                if on_done: on_done(run["result"], stopped)   # LOCK is not held here (see the lock order above)
            except Exception: pass
            finally:
                with LOCK: run.update(done=True, state=run.get("final") or "failed", stopped=stopped)
    threading.Thread(target=pump, daemon=True, name="console-run-" + rid).start(); return rid


def done(rid: str) -> bool:
    with LOCK: run = RUNS.get(rid)
    return run is None or bool(run["done"])


def poll(rid: str, cursor: int = 0) -> dict | None:
    with LOCK:
        run = RUNS.get(rid)
        if not run: return None
        evs = run["events"][cursor:]
        return {"id": rid, "state": run["state"], "done": run["done"], "events": evs, "cursor": cursor + len(evs), "result": run["result"]}


def cancel(rid: str) -> dict:
    with LOCK: run = RUNS.get(rid); live = bool(run and not run["done"] and run["result"] is None and run.get("proc"))
    if not live: return {"ok": False, "error": "no running operation with that id"}
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
