"""Backend-owned confirmations for physical console operations (docs/console-install-architecture.md sections 5 and 14). Prepared, not enabled:
the backend's instance asks Gate G1 (console_engine.gate, code constants, both closed) before it makes anything, so every request is refused
today. When a physical tier is opened by its own reviewed release, the same object is what binds a person's confirmation to exactly one
operation:
  - made by the backend only, after the gate allows that operation's tier, for one operation (install, update, uninstall, recover, inspect,
    export), one subject (the plan's SHA-256 for install and update, the reviewed removal's hash for uninstall, the attempt's id for
    recover; none for inspect/export) and one console (its hardware id as the checked session read it);
  - shown in the app's own window (the host), never written to disk, never in a URL or a log (only its SHA-256 is kept, in memory);
  - single use, and it expires (5 minutes);
  - invalidated when the plan or the console changes (a new package, loadout, HOME setup or another console): an earlier confirmation never
    covers different bytes or a different console.
Copyright (C) 2026 Rance9, GPL-3.0-or-later."""
from __future__ import annotations
import hashlib, re, secrets, threading, time
from console_engine import gate

OPS = {"install": "install", "update": "install", "uninstall": "install", "recover": "install", "inspect": "inspect", "export": "inspect"}
TTL = 300
HWID_RE = re.compile(r"^[0-9a-zA-Z-]{4,64}$"); PLAN_RE = re.compile(r"^[0-9a-f]{64}$"); ATTEMPT_RE = re.compile(r"^[0-9a-f]{8}$")

class ConfirmationRefused(PermissionError):
    def __init__(self, code: str, text: str): self.code = code; super().__init__(text)

def _h(token: str) -> str: return hashlib.sha256(token.encode()).hexdigest()

class Confirmations:
    def __init__(self, allowed=gate.allowed, clock=time.time, ttl: int = TTL):
        self.allowed, self.clock, self.ttl = allowed, clock, ttl; self.lock = threading.Lock(); self.live: dict[str, dict] = {}
    def _check(self, op: str, plan_hash: str | None, device: str):
        if op not in OPS: raise ConfirmationRefused("bad-operation", "unknown operation")
        if not self.allowed(OPS[op]): raise gate.GateClosed(OPS[op], f"confirm {op}")   # first: nothing is made while the tier is closed
        if OPS[op] == "install" and op != "recover" and not (isinstance(plan_hash, str) and PLAN_RE.match(plan_hash)): raise ConfirmationRefused("bad-plan", "the operation needs the plan (or the reviewed removal) it confirms")
        if op == "recover" and not (isinstance(plan_hash, str) and ATTEMPT_RE.match(plan_hash)): raise ConfirmationRefused("bad-attempt", "a recovery confirms exactly one interrupted attempt")
        if not (isinstance(device, str) and HWID_RE.match(device)): raise ConfirmationRefused("bad-device", "the console's identity as this session read it is needed")
    def mint(self, op: str, plan_hash: str | None, device: str) -> dict:
        self._check(op, plan_hash, device); token = secrets.token_urlsafe(24); now = self.clock()
        with self.lock:
            self.live = {k: v for k, v in self.live.items() if v["expires"] > now}
            self.live[_h(token)] = {"op": op, "plan": plan_hash, "device": device, "expires": now + self.ttl}
        return {"token": token, "op": op, "plan": plan_hash, "device": device, "expires_in": self.ttl}
    def consume(self, token: str, op: str, plan_hash: str | None, device: str) -> None:
        """the operation may start: the confirmation matches it exactly and was not used; it is used up now (raises otherwise)"""
        self._check(op, plan_hash, device)
        with self.lock: rec = self.live.pop(_h(str(token or "")), None)
        if not rec: raise ConfirmationRefused("not-confirmed", "this operation was not confirmed (or its confirmation was used already)")
        if rec["expires"] <= self.clock(): raise ConfirmationRefused("expired", "the confirmation expired: confirm again")
        if (rec["op"], rec["plan"], rec["device"]) != (op, plan_hash, device): raise ConfirmationRefused("mismatch", "the confirmation was for another operation, plan or console: confirm again")
    def invalidate(self, plan_hash: str | None = None, device: str | None = None) -> int:
        with self.lock:
            drop = [k for k, v in self.live.items() if (plan_hash and v["plan"] == plan_hash) or (device and v["device"] == device) or (plan_hash is None and device is None)]
            for k in drop: self.live.pop(k, None)
        return len(drop)

BACKEND = Confirmations()   # the backend's own: asks the gate in code
