"""Classic Cart Studio's HOME on a console, the backend side (K3; docs/console-install-architecture.md sections 13 to 15). The one dispatcher behind
/api/console/home/*: the page names an operation and, where it applies, a plan by its hash, a reviewed removal by its hash, an attempt by its
id, a simulated profile and a rehearsal; nothing else reaches a worker. Rehearsals run against disposable stand-ins (the simulation tier). The
physical operations run the same engine code (console_engine.ccs_install through console_engine.worker), each asking Gate G1 here first
(closed in code: refused before any worker starts), then again inside the worker, then in the transport worker:
  inspect   reads the console and makes it the checked console (its identity, for SESSION_TTL; a new check replaces it and voids every
            confirmation made for the console before)
  check     the reviewed plan against the checked console (reads only)
  confirm   the backend makes a single-use confirmation (console_confirm.py) for exactly one operation, one subject (a plan, a reviewed
            removal or an attempt) and the checked console; it lasts 5 minutes and only its hash is kept. The answer says what it covers
  install / update / uninstall / recover   use up that confirmation, then run on the checked console (bound by its identity, which the
            engine reads again inside the operation); a failure stops all writes and is never undone by itself (recovery is its own
            confirmed operation, G3); afterwards the console must be checked again before the next confirmation
  verify, uninstall-review, recover-review, export   reads only (export copies the console's saves into a new folder of the app's data)
One physical operation at a time, decided under one lock from the state as it is then: an operation is admitted (its confirmation used up),
and at that moment a change counts as made (the checked console is marked changed and every confirmation is void) and a new check replaces
the old one (which is dropped, with its confirmations). The dispatcher stays busy until the run has ended, its worker has stopped and the
completion below has run; a completion acts only for the admission it belongs to, so a late one never touches a newer check. Nothing is ever
cleaned by a completion: no worker, no answer, a cancel or a worker that would not stop all leave the console to be checked again (a worker
that would not stop keeps the dispatcher busy until the app restarts). A test builds its own Dispatcher with an injected worker spawner (an
in-process worker over pipes, with an already simulated console) and its own gate and confirmations; the app's instance takes the defaults,
and nothing a request carries can change them. Copyright (C) 2026 Rance9, GPL-3.0-or-later."""
from __future__ import annotations
import hashlib, os, re, secrets, threading, time
import console_runs
import console_confirm as cc
from console_engine import gate as cg

SESSION_TTL = 600   # seconds: a console checked longer ago is checked again before anything is confirmed or run on it
WRITE = {"install": "home-execute", "update": "home-execute", "uninstall": "home-uninstall", "recover": "home-recover"}
READ = {"inspect": "home-inspect", "check": "home-check", "verify": "home-verify", "uninstall-review": "home-uninstall-review", "recover-review": "home-recover-review", "export": "home-export-console"}
TIER = {**{k: "inspect" for k in READ}, **{k: "install" for k in WRITE}}
CLOSED = {"inspect": "Reading a real console is not available in this version.", "install": "Installing on your NES Classic is not available in this version."}
BUSY = {"ok": False, "code": "busy", "error": "Another console operation is still running; wait for it to finish."}
STUCK = {"ok": False, "code": "busy", "error": "The last console operation did not stop. Close Classic Cart Studio and open it again before the next console operation."}
SCENARIOS = ("first-install", "update", "repeat", "repair", "interrupted")

def _hex(v, n: int) -> str: s = re.sub(r"[^0-9a-f]", "", str(v or ""))[:n]; return s if len(s) == n else ""

class Dispatcher:
    def __init__(self, runs=None, allowed=None, confirmations=None, clock=None, spawn=None):
        self.runs = runs or console_runs; self.allowed = allowed or cg.allowed; self.conf = confirmations or cc.BACKEND
        self.clock = clock or time.time; self.spawn = spawn; self.lock = threading.Lock(); self.console: dict | None = None
        self.active: dict | None = None; self.last: str | None = None; self.epoch = 0   # the running operation; the last run; admissions so far

    # ---- the checked console (in memory only: its identity, when it was read, whether it has been written since, the admission it came from)
    def _session(self) -> dict | None:   # (the lock is held) an old check is dropped, with its confirmations
        s = self.console
        if s and self.clock() - s["at"] > SESSION_TTL: self.conf.invalidate(device=s["device"]); self.console = s = None
        return s
    def checked(self) -> dict | None:
        with self.lock: s = self._session(); return dict(s) if s else None
    def public_console(self) -> dict | None:
        s = self.checked()
        if not s: return None
        return {"session": s["id"], "label": s["label"], "checked_seconds_ago": int(self.clock() - s["at"]), "written_since": s["written"], "identity": s["identity"], "state": s.get("state")}
    def _inspected(self, epoch: int, ev: dict | None):   # (the lock is held) only the newest admission's check becomes the checked console
        r = (ev or {}).get("result") if (ev or {}).get("event") == "result" else None
        dev = (r or {}).get("device") if (r or {}).get("ok") else None
        if epoch != self.epoch or not dev or not cc.HWID_RE.match(str(dev.get("hwid") or "")): return
        self.console = {"id": secrets.token_hex(6), "epoch": epoch, "device": dev["hwid"], "at": self.clock(), "written": False, "simulated": bool(r.get("simulated")),
                        "label": f"NES Classic ({dev.get('system')}, hakchi {dev.get('hakchi')}), console {hashlib.sha256(dev['hwid'].encode()).hexdigest()[:8]}",
                        "identity": {k: dev.get(k) for k in ("system", "kernel", "hakchi")},
                        "state": {k: r.get(k) for k in ("pointer", "module", "attempts", "copies", "in_use", "free_bytes")}}
    def _finished(self, epoch: int, op: str, ev: dict | None, stopped: bool = True):
        """a run's completion (console_runs calls it after the worker has stopped, without its own lock held). A change was already counted
        when it was admitted, so a write's completion has nothing to add; only an inspection's answer is used, and only if nothing was
        admitted after it."""
        with self.lock:
            try:
                if op == "inspect": self._inspected(epoch, ev)
            finally:
                if self.active and self.active["epoch"] == epoch: self.active = None if stopped else {**self.active, "stuck": True}

    # ---- requests
    def post(self, op: str, body: dict) -> tuple[int, dict]:
        hsh = _hex(body.get("plan_hash"), 64); prof = re.sub(r"[^a-z0-9-]", "", str(body.get("profile") or ""))[:40]
        if op == "plan": return self._res(self.runs.call("home-plan", {}, timeout=300, spawn=self.spawn))
        if op == "preflight":
            if not hsh or not prof: return 400, {"ok": False, "error": "Prepare and review the plan again, then choose a simulated console."}
            return self._res(self.runs.call("home-preflight", {"plan_hash": hsh, "profile": prof}, timeout=120, spawn=self.spawn))
        if op == "rehearse":
            sc = str(body.get("scenario") or "first-install"); then = [x for x in (body.get("then") or []) if x in ("uninstall", "export")][:2]
            if not hsh or not prof or sc not in SCENARIOS: return 400, {"ok": False, "error": "Choose a simulated console and a rehearsal, then try again."}
            return 200, {"ok": True, "run": self.runs.start("home-simulate", {"plan_hash": hsh, "profile": prof, "scenario": sc, "then": then}, spawn=self.spawn), "simulated": True}
        if op == "cancel": return 200, {"ok": True, **self.runs.cancel(re.sub(r"[^A-Za-z0-9-]", "", str(body.get("run") or ""))[:40])}
        if op == "confirm": return self._confirm(body)
        if op in TIER: return self._physical(op, body)
        return 404, {"ok": False, "error": "unknown console operation"}

    def _res(self, r: dict, code: int = 400) -> tuple[int, dict]:
        ok = r.get("event") == "result"
        return (200 if ok else 409 if r.get("code") in ("plan-obsolete", "refused") else code), {"ok": ok, "result": r.get("result"), "code": r.get("code"), "error": r.get("text")}

    def _subject(self, op: str, body: dict) -> str:
        if op in ("install", "update"): return _hex(body.get("plan_hash"), 64)
        if op == "uninstall": return _hex(body.get("review"), 64)
        if op == "recover": return _hex(body.get("attempt"), 8)
        return ""

    def _confirm(self, body: dict) -> tuple[int, dict]:
        if not self.allowed("install"): return 403, {"ok": False, "code": "physical-disabled", "physical": False, "error": CLOSED["install"]}   # first, whatever was sent
        op = str(body.get("operation") or "")
        if op not in WRITE: return 400, {"ok": False, "error": "Choose the operation to confirm."}
        with self.lock:   # the checked console as it is now: not while an operation runs, not after a change
            if self.active: return 409, STUCK if self.active.get("stuck") else BUSY
            s = self._session()
            if not s or s["written"]: return 409, {"ok": False, "code": "check-console", "error": "Check the console first (again, after a change or after 10 minutes)."}
            subject = self._subject(op, body)
            if not subject: return 400, {"ok": False, "code": "bad-subject", "error": "Review the plan, the removal or the recovery again, then confirm."}
            try: c = self.conf.mint(op, subject, s["device"])
            except cg.GateClosed: return 403, {"ok": False, "code": "physical-disabled", "physical": False, "error": CLOSED["install"]}
            except cc.ConfirmationRefused as e: return 400, {"ok": False, "code": e.code, "error": str(e)}
            label = s["label"]
        what = {"install": "install this plan's HOME", "update": "update to this plan's HOME", "uninstall": "remove Classic Cart Studio's HOME exactly as reviewed", "recover": "recover this interrupted attempt"}[op]
        return 200, {"ok": True, "confirmation": c["token"], "operation": op, "subject": subject, "console": label, "expires_in": c["expires_in"],
                     "covers": f"Once, within {c['expires_in'] // 60} minutes: {what} on {label}. Another console, plan or change needs a new confirmation."}

    def _physical(self, op: str, body: dict) -> tuple[int, dict]:
        tier = TIER[op]
        if not self.allowed(tier): return 403, {"ok": False, "code": "physical-disabled", "physical": False, "error": CLOSED[tier]}
        with self.lock:   # admission: every check below reads the state as it is now
            if self.active: return 409, STUCK if self.active.get("stuck") else BUSY
            s = self._session() if op != "inspect" else None
            if op != "inspect" and not s: return 409, {"ok": False, "code": "check-console", "error": "Check the console first (again after 10 minutes)."}
            args = {"device": s["device"]} if s else {}
            if op in ("check", "verify", "install", "update"):
                if not _hex(body.get("plan_hash"), 64): return 400, {"ok": False, "error": "Prepare and review the plan again."}
                args["plan_hash"] = _hex(body.get("plan_hash"), 64)
            if op in ("uninstall-review", "uninstall"): args["keep_data"] = body.get("keep_data") is not False
            if op in WRITE:
                subject = self._subject(op, body)
                try: self.conf.consume(str(body.get("confirmation") or ""), op, subject, s["device"])
                except cg.GateClosed: return 403, {"ok": False, "code": "physical-disabled", "physical": False, "error": CLOSED[tier]}
                except cc.ConfirmationRefused as e: return 403, {"ok": False, "code": e.code, "error": str(e)}
                if s["written"]: return 409, {"ok": False, "code": "check-console", "error": "Check the console again before the next change."}
                if op in ("install", "update"): args["operation"] = op
                if op == "uninstall": args["review"] = subject
                if op == "recover": args["attempt"] = subject
            self.epoch += 1; epoch = self.epoch
            if op == "inspect": self.console = None; self.conf.invalidate()   # the new check replaces the old one now
            elif op in WRITE: s["written"] = True; self.conf.invalidate()      # the change counts from now, whatever happens next
            try: rid = self.runs.start(WRITE.get(op) or READ[op], args, spawn=self.spawn, on_done=lambda ev, stopped=True: self._finished(epoch, op, ev, stopped))
            except Exception as e: return 500, {"ok": False, "code": "worker-start", "error": f"The console operation could not start: {e}"}
            self.active = {"run": rid, "epoch": epoch, "op": op}; self.last = rid
            return 200, {"ok": True, "run": rid, "operation": op, "physical": True}

    def status_console(self) -> dict:
        with self.lock: a, last = self.active, self.last
        return {"checked": self.public_console(), "busy": bool(a), "run": (a or {}).get("run") or last}

DISPATCH = Dispatcher()   # the app's own: the gate and the confirmations in code
