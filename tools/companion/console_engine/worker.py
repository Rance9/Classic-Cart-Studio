"""Worker protocol (architecture section 3): newline-delimited JSON on stdin/stdout, version 1.

Request:  {"v": 1, "id": "<client id>", "cmd": "<name>", "args": {...}}
Replies:  {"v": 1, "id": ..., "event": "progress"|"stage"|"result"|"error", ...}  (zero or more progress/stage events, then exactly one result or error)

Commands: capabilities, identify(profile), plan(manifest, profile?, keep_foreign?), preflight(plan_hash), simulate(plan_hash, profile),
execute(plan_hash) [always refused: Gate G1], cancel-if-safe, recover(stage), verify(plan_hash, profile).
The worker never accepts file paths, shell strings or transport parameters from its client: a plan is addressed by its
hash and read from the worker's own plan folder; the only transport it can open is the fake one. Inputs are bounded.

This Python worker is the protocol reference and the simulation tier. The derived GPL worker (Phase 2b) speaks the same
protocol for the physical tiers once Gate G1 opens them; the backend refuses any worker whose `capabilities` reply does
not match the pinned protocol version.
"""
from __future__ import annotations
import json, os, sys
from . import ENGINE_VERSION, PROTOCOL_VERSION, CAPABILITY, gate, plan as planmod, profiles
from .engine import Engine, new_run_id
from .journal import Journal
from .transport import open_transport

MAX_LINE = 1_000_000
COMMANDS = ("capabilities", "identify", "plan", "preflight", "simulate", "execute", "cancel-if-safe", "recover", "verify")


class Worker:
    def __init__(self, root: str, out=None):
        self.root = root; self.plans = os.path.join(root, "plans"); self.journals = os.path.join(root, "journals")
        os.makedirs(self.plans, exist_ok=True); os.makedirs(self.journals, exist_ok=True)
        self.out = out or sys.stdout; self.engine: Engine | None = None; self.last_device: dict | None = None

    def send(self, obj: dict):
        self.out.write(json.dumps({"v": PROTOCOL_VERSION, **obj}, ensure_ascii=False) + "\n"); self.out.flush()

    def _engine(self, req_id, plan_hash: str = "") -> Engine:
        run = new_run_id(); j = Journal(os.path.join(self.journals, run + ".jsonl"), run, plan_hash)
        self.engine = Engine(j, emit=lambda ev: self.send({"id": req_id, **ev})); return self.engine

    def handle(self, line: str):
        if len(line) > MAX_LINE: self.send({"id": None, "event": "error", "code": "too-large", "text": "request exceeds 1 MB"}); return
        try: req = json.loads(line)
        except ValueError: self.send({"id": None, "event": "error", "code": "bad-json", "text": "request is not JSON"}); return
        if not isinstance(req, dict) or req.get("v") != PROTOCOL_VERSION: self.send({"id": (req or {}).get("id") if isinstance(req, dict) else None, "event": "error", "code": "bad-version", "text": f"protocol version {PROTOCOL_VERSION} required"}); return
        rid = req.get("id"); cmd = req.get("cmd"); args = req.get("args") or {}
        if cmd not in COMMANDS or not isinstance(args, dict): self.send({"id": rid, "event": "error", "code": "bad-command", "text": "unknown command or arguments"}); return
        try: result = getattr(self, "cmd_" + cmd.replace("-", "_"))(rid, args); self.send({"id": rid, "event": "result", "result": result})
        except KeyError as e: self.send({"id": rid, "event": "error", "code": "not-found", "text": str(e)})
        except gate.GateClosed as e: self.send({"id": rid, "event": "error", "code": "physical-disabled", "text": str(e)})
        except Exception as e: self.send({"id": rid, "event": "error", "code": "worker-error", "text": f"{type(e).__name__}: {e}"})

    # ---- commands ----
    def cmd_capabilities(self, rid, a):
        return {"engine": ENGINE_VERSION, "protocol": PROTOCOL_VERSION, "capability": CAPABILITY, "tiers": gate.state(), "transports": ["fake"], "profiles": [profiles.public(n) for n in profiles.names()], "commands": list(COMMANDS)}

    def _profile(self, a) -> str:
        name = str(a.get("profile") or "no-device")
        if name not in profiles.names(): raise KeyError(f"unknown profile {name!r}")
        return name

    def cmd_identify(self, rid, a):
        t = open_transport("fake", self._profile(a)); e = self._engine(rid); d = e.identify(t); self.last_device = d
        return {k: v for k, v in d.items() if k != "foreign"} | {"foreign_count": len(d.get("foreign") or {})}

    def cmd_plan(self, rid, a):
        man = a.get("manifest")
        if not isinstance(man, dict): raise ValueError("manifest (object) required")
        dev = None; foreign = None
        if a.get("profile"):
            t = open_transport("fake", self._profile(a)); e = self._engine(rid); dev = e.identify(t); foreign = dev.pop("foreign", None)
        p = planmod.build(man, device=dev, foreign=foreign, approve_alternates=bool(a.get("approve_alternates")), keep_foreign=bool(a.get("keep_foreign", True)))
        path = planmod.save(p, self.plans); return {"plan": p, "file": os.path.basename(path)}

    def _plan(self, a) -> dict:
        h = str(a.get("plan_hash") or "")
        if not (len(h) == 64 and all(c in "0123456789abcdef" for c in h)): raise KeyError("plan_hash must be the plan's SHA-256")
        p = planmod.load(self.plans, h)
        if not p: raise KeyError("plan not found (plans are addressed by hash and must be built by this worker)")
        return p

    def cmd_preflight(self, rid, a):
        p = self._plan(a); e = self._engine(rid, p["hash"]); return e.preflight(p)

    def cmd_simulate(self, rid, a):
        p = self._plan(a); t = open_transport("fake", self._profile(a)); e = self._engine(rid, p["hash"])
        e.identify(t); return e.simulate(p, t)

    def cmd_execute(self, rid, a):
        p = self._plan(a); e = self._engine(rid, p["hash"]); return e.execute(p, str(a.get("transport") or "ssh"))

    def cmd_cancel_if_safe(self, rid, a):
        if not self.engine: return {"accepted": False, "text": "no operation running"}
        return self.engine.cancel_if_safe()

    def cmd_recover(self, rid, a):
        return {"stage": str(a.get("stage") or ""), "advice": Engine.recovery_advice(str(a.get("stage") or "")), "physical": gate.state()}

    def cmd_verify(self, rid, a):
        p = self._plan(a); t = open_transport("fake", self._profile(a)); e = self._engine(rid, p["hash"]); d = e.identify(t)
        if not t.online: return {"match": None, "state": d.get("state"), "text": "no shell to read from"}
        rb = t.read_back(); exp = planmod.expected_after(p)
        missing = sorted(r for r in exp if rb.get(r) != exp[r]); extra = sorted(r for r in rb if r not in exp)
        return {"match": not missing and not extra, "missing": missing[:20], "extra": extra[:20], "simulated": True, "text": "read-back of the simulated console compared with the plan (a verified physical read-back is a future gated state)"}

    def serve(self, inp=None):
        inp = inp or sys.stdin
        for line in inp:
            line = line.strip()
            if not line: continue
            if line == "quit": break
            self.handle(line)


def main(argv=None):
    import argparse
    ap = argparse.ArgumentParser(); ap.add_argument("--root", required=True); a = ap.parse_args(argv)
    Worker(a.root).serve()


if __name__ == "__main__": main()
