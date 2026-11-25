"""Classic Cart Studio's HOME on a console, the backend side (K3; docs/console-install-architecture.md sections 13 to 15). The one dispatcher behind
/api/console/home/*: the page names an operation and, where it applies, a plan by its hash, a reviewed removal by its hash, an attempt by its
id, a simulated profile, a rehearsal and, for a new plan, the add-ons the console should have afterwards (mod: true or false; nucleus:
"none", "direct" or "chooser"; start: "native" or "ccs-home"; auto: "menu", "last" or "wait"; hakchi_games: true or false, the collection's
games also added to Original Menu as ordinary Hakchi games; what is not given is Studio's default: CCS Mod added, CCS Nucleus only when asked
for, Hakchi games when CCS Mod is not; without CCS Mod only the game files those Hakchi games need go to the console, never CCS HOME, and a
choice with neither CCS Mod nor Hakchi games is refused in plain words); nothing else reaches a worker. A check (the preflight against a simulated console, or check
against the checked console) reads that console's add-ons and CCS Nucleus's start and answers with what the reviewed plan adds or changes,
in plain words; a plan that would remove an add-on is refused there, with the console's add-ons and the nearest choice that keeps them.
inspect reports the console's add-ons and what opens at power-on in plain words. Rehearsals run against disposable stand-ins (the
simulation tier). The
physical operations run the same engine code (console_engine.ccs_install through console_engine.worker), each asking Gate G1 here first
(in code; both tiers open in this version), then again inside the worker, then in the transport worker:
  inspect   reads the console and makes it the checked console (its identity, for SESSION_TTL; a new check replaces it and voids every
            confirmation made for the console before)
  check     the reviewed plan against the checked console (reads only)
  confirm   the backend makes a single-use confirmation (console_confirm.py) for exactly one operation, one subject (a plan, a reviewed
            removal or an attempt) and the checked console; it lasts 5 minutes and only its hash is kept. The answer says what it covers
  install / update / uninstall / recover   need a verified backup of the checked console first (backup_admission: the same validation as
            Console Backup & Restore, run by the shipped console worker; a single-use admission for exactly this console, operation and
            subject, checked again by the run's own worker before its first write), then use up that confirmation, then run on the checked
            console (bound by its identity, which the engine reads again inside the operation); a failure stops all writes and is never
            undone by itself (recovery is its own confirmed operation, G3); afterwards the console must be checked again before the next
            confirmation
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
import console_runs, backup_admission
import console_confirm as cc
from console_engine import gate as cg

SESSION_TTL = 600   # seconds: a console checked longer ago is checked again before anything is confirmed or run on it
ORIGINAL_UNAVAILABLE_TITLE = "Returning to Nintendo's original software is unavailable"   # = CcsBackup Admission.OriginalUnavailableTitle/Text
ORIGINAL_UNAVAILABLE_TEXT = ("Classic Cart Studio can back up your current Hakchi setup and add the changes you choose. It cannot return this console to Nintendo's "
                             "original software without the original material from your own backup.")
WRITE = {"install": "home-execute", "update": "home-execute", "uninstall": "home-uninstall", "recover": "home-recover"}
READ = {"inspect": "home-inspect", "check": "home-check", "verify": "home-verify", "uninstall-review": "home-uninstall-review", "recover-review": "home-recover-review", "export": "home-export-console"}
TIER = {**{k: "inspect" for k in READ}, **{k: "install" for k in WRITE}}
CLOSED = {"inspect": "Reading a real console is turned off in this build.", "install": "Changing a real console is turned off in this build."}   # only when a tier is closed in code
BUSY = {"ok": False, "code": "busy", "error": "Another console operation is still running; wait for it to finish."}
STUCK = {"ok": False, "code": "busy", "error": "The last console operation did not stop. Close Classic Cart Studio and open it again before the next console operation."}
SCENARIOS = ("first-install", "update", "repeat", "repair", "interrupted")
CHOICE = {"mod": bool, "nucleus": str, "start": str, "auto": str, "hakchi_games": bool}   # a new plan's add-on choice (console_engine.ccs_install.addon_choice) and its Hakchi games

def _hex(v, n: int) -> str: s = re.sub(r"[^0-9a-f]", "", str(v or ""))[:n]; return s if len(s) == n else ""

class Dispatcher:
    def __init__(self, runs=None, allowed=None, confirmations=None, clock=None, spawn=None, admit=None):
        self.runs = runs or console_runs; self.allowed = allowed or cg.allowed; self.conf = confirmations or cc.BACKEND
        self.admit = admit or backup_admission.admit   # (hwid, operation, subject) -> {"ok", "ticket", ...}: the backup prerequisite
        self.admit_preview = backup_admission.preview   # the same answer without a ticket, for the confirmation (data-only notice)
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
                        "label": f"{dev.get('model_name') or 'Console'} ({dev.get('system')}, hakchi {dev.get('hakchi')}), console {hashlib.sha256(dev['hwid'].encode()).hexdigest()[:8]}",
                        "identity": {k: dev.get(k) for k in ("system", "kernel", "hakchi", "model", "model_name", "board", "region")},
                        "state": {k: r.get(k) for k in ("pointer", "module", "addons", "addons_text", "addons_problem", "start", "attempts", "copies", "games_copies", "in_use", "free_bytes")}}
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
        if op == "plan":   # the add-on choice: only its fields, each of its own kind (the worker checks the values and says why one cannot be made)
            choice = {k: body[k] for k in CHOICE if body.get(k) is not None}
            if any(not isinstance(v, CHOICE[k]) or (isinstance(v, str) and not re.fullmatch(r"[a-z-]{1,16}", v)) for k, v in choice.items()):
                return 400, {"ok": False, "code": "bad-choice", "error": "Choose the add-ons again, then make the plan again."}
            return self._res(self.runs.call("home-plan", choice, timeout=300, spawn=self.spawn))
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
        what = {"install": "install this collection as planned", "update": "update this console as planned", "uninstall": "remove what Classic Cart Studio added, exactly as reviewed", "recover": "recover this interrupted attempt"}[op]
        # what the backup prerequisite will be, before anything proceeds: a data-only admission (no original Nintendo material in this
        # console's backups) is said here, in the approved words; the change itself checks again
        try: pv = self.admit_preview(s["device"], backup_admission.operation_for(op, body), subject)
        except Exception: pv = {}
        backup = {"data_only": pv.get("data_only") is True, "ok": pv.get("ok") is True, "text": str(pv.get("text") or "")}
        if backup["data_only"]: backup.update(title=ORIGINAL_UNAVAILABLE_TITLE, notice=ORIGINAL_UNAVAILABLE_TEXT)
        return 200, {"ok": True, "confirmation": c["token"], "operation": op, "subject": subject, "console": label, "expires_in": c["expires_in"], "backup": backup,
                     "covers": f"Once, within {c['expires_in'] // 60} minutes: {what} on {label}. Another console, plan or change needs a new confirmation."}

    def _physical(self, op: str, body: dict) -> tuple[int, dict]:
        tier = TIER[op]
        if not self.allowed(tier): return 403, {"ok": False, "code": "physical-disabled", "physical": False, "error": CLOSED[tier]}
        ticket = None
        if op in WRITE:   # the backup prerequisite, outside the lock (it verifies whole backup files); the session is re-checked below
            with self.lock:
                if self.active: return 409, STUCK if self.active.get("stuck") else BUSY
                s0 = self._session()
                if not s0: return 409, {"ok": False, "code": "check-console", "error": "Check the console first (again after 10 minutes)."}
                sid, dev0, subject0 = s0["id"], s0["device"], self._subject(op, body)
            if not subject0: return 400, {"ok": False, "code": "bad-subject", "error": "Review the plan, the removal or the recovery again, then confirm."}
            adm = self.admit(dev0, backup_admission.operation_for(op, body), subject0)
            if not adm.get("ok"): return 409, {"ok": False, "code": adm.get("code") or "backup-required", "error": adm.get("text") or "Back up this console first."}
            ticket = adm["ticket"]
        with self.lock:   # admission: every check below reads the state as it is now
            if op in WRITE and (not self.console or self.console.get("id") != sid): return 409, {"ok": False, "code": "check-console", "error": "The console was checked again while its backup was being verified. Confirm again."}
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
                args["admission"] = {"ticket": ticket, "operation": backup_admission.operation_for(op, body), "subject": subject}
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
