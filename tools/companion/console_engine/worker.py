"""Worker protocol (architecture section 3): newline-delimited JSON on stdin/stdout, version 1.

Request:  {"v": 1, "id": "<client id>", "cmd": "<name>", "args": {...}}
Replies:  {"v": 1, "id": ..., "event": "progress"|"stage"|"result"|"error", ...}  (zero or more progress/stage events, then exactly one result or error)

Commands: capabilities, identify(profile), plan(manifest, profile?, keep_foreign?), preflight(plan_hash), simulate(plan_hash, profile),
cancel-if-safe, recover(stage), verify(plan_hash, profile): the older plan's simulation against fake profiles (the older game sync through
hakchi itself is not part of Classic Cart Studio, so this worker has no command for it).
Classic Cart Studio's own HOME under hakchi (ccs_install.py, K3): home-plan(mod?, nucleus?, start?, auto?, hakchi_games?) from the console package the app
staged (the folder is given to the worker when it starts, never by its client) and the add-ons the console should have afterwards: mod
(true or false: CCS Mod, the CCS HOME tile), nucleus ("none", "direct" or "chooser": CCS Nucleus, started directly or with its startup
chooser), start ("native" or "ccs-home": what CCS Nucleus opens at power-on) and auto ("menu", "last" or "wait": what the startup chooser
starts by itself); hakchi_games (true or false: the collection's games also added to Original Menu as ordinary Hakchi games, hakchi_publish.py);
what is not given is Studio's default (CCS Mod added, CCS Nucleus only when asked for, Hakchi games when CCS Mod is not; without CCS Mod only
the game files those Hakchi games need are installed, never CCS HOME, and a choice with neither is refused). The plan is saved by hash and its
summary answers (a choice that cannot be made is refused with its reason). home-preflight(plan_hash, profile) and home-check(plan_hash,
device) read the console's add-ons and CCS Nucleus's start there and answer with what the plan would add or change; a plan that would remove
an add-on the console has is refused with the console's add-ons and the nearest choice that keeps them. home-simulate(plan_hash, profile,
scenario?, fault?, then?) runs against a fresh disposable stand-in that is removed afterwards (scenarios first-install, update, repeat,
repair, interrupted; then "uninstall" or "export" rehearses removal or the data export on the same stand-in), home-records() (the last plan
and the PC-side operation records). A plan whose package is no longer the staged one is refused as obsolete.
The physical operations on a console, each in its own console session opened here and closed at its end, bound to the console the client
names by its identity as it was checked (device) and read again inside the operation: home-inspect() and home-check(plan_hash, device)
(reads only), home-execute(plan_hash, device, operation install|update) (a failure stops all writes: no rollback by itself),
home-verify(plan_hash, device), home-uninstall-review(device, keep_data) then home-uninstall(device, review, keep_data) (exactly the
reviewed removal), home-recover-review(device) then home-recover(device, attempt) (G3: one interrupted attempt, from its own journal) and
home-export-console(device) (a new folder under the worker's own exports). Every one asks Gate G1 first (inspect or install tier, open in
this version), then opens its session through the shipped transport worker, which asks its own gate again at "connect" and connects only
through hakchi's clovershell or SSH; each write session is admitted by the console's verified backup ("admit") before its first change. A
test injects an already simulated session in place of the opener (the constructor's console_factory/allowed, from Python only); nothing a
client sends can choose a transport, a device or the gate.
While a command runs, the worker still reads its input: "cancel-if-safe" stops a rehearsal at its next safe point (before the switch).
The worker never accepts file paths, shell strings or transport parameters from its client: a plan is addressed by its
hash and read from the worker's own plan folder; the older plan's commands open only the fake transport. Inputs are bounded.

This Python worker is the protocol reference, the simulation tier and the engine of the physical operations; the console transport worker
(tools/console-worker, a separate process per session) carries each physical session's typed console commands.
"""
from __future__ import annotations
import json, os, queue, shutil, sys, threading, time
from . import ENGINE_VERSION, PROTOCOL_VERSION, CAPABILITY, gate, plan as planmod, profiles, ccs_install
from .engine import Engine, new_run_id
from .journal import Journal
from .transport import open_transport

MAX_LINE = 1_000_000
class PlanObsolete(RuntimeError): pass
COMMANDS = ("capabilities", "identify", "plan", "preflight", "simulate", "cancel-if-safe", "recover", "verify",
            "home-plan", "home-preflight", "home-simulate", "home-records", "home-inspect", "home-check", "home-execute", "home-verify",
            "home-uninstall-review", "home-uninstall", "home-recover-review", "home-recover", "home-export-console")
PHYSICAL_TIER = {"home-inspect": "inspect", "home-check": "inspect", "home-verify": "inspect", "home-uninstall-review": "inspect", "home-recover-review": "inspect", "home-export-console": "inspect",
                 "home-execute": "install", "home-uninstall": "install", "home-recover": "install"}   # asked before anything else, whatever the arguments
TRANSPORT_ORDER = ("ssh", "clovershell")   # hakchi's own links, the order the shipped opener tries them (confirmed by the reviewed activation, G1)

def _open_real(tier: str, package_dir: str | None):
    """the shipped opener: the gate in code first, here and again inside the transport worker (whose device code is compiled out)"""
    from .worker_console import open_worker_console
    gate.require(tier, "open a console session"); last = None
    for kind in TRANSPORT_ORDER:
        try: return open_worker_console(kind, package_dir, tier=tier)
        except gate.GateClosed: raise
        except (ConnectionError, OSError) as e: last = e
    raise ConnectionError(f"no console answered ({last})")

def _stamp() -> str:   # record names sort in the order they were written (to the millisecond)
    t = time.time(); return time.strftime("%Y%m%dT%H%M%S", time.gmtime(t)) + f"{int(t * 1000) % 1000:03d}Z"

def read_home_records(records: str, staged: str | None) -> dict:
    """the last plan and the latest operation records (PC side), read directly (the backend's status reads them without starting a worker).
    staged: the staged package's MANIFEST.json SHA-256 (None: nothing is staged)"""
    last = None
    try:
        with open(os.path.join(records, "last-plan.json"), encoding="utf-8") as f: last = json.load(f)
    except (OSError, ValueError): pass
    ops = []
    try: names = sorted((n for n in os.listdir(records) if n.startswith("op-") and n.endswith(".json")), reverse=True)[:20]
    except OSError: names = []
    for n in names:
        try:
            with open(os.path.join(records, n), encoding="utf-8") as f: ops.append(json.load(f))
        except (OSError, ValueError): ops.append({"file": n, "outcome": "unknown", "why": "the record cannot be read"})
    plan = (last or {}).get("plan") or {}
    return {"last_plan": last, "last_plan_current": bool(plan) and bool(staged) and plan.get("manifest_sha256") == staged, "operations": ops, "staged_manifest_sha256": staged}
HOME_FAULTS = ("before", "after", "corrupt", "no_space_after")
HOME_SCENARIOS = ("first-install", "update", "repeat", "repair", "interrupted")
HOME_THEN = ("uninstall", "export")
MAX_RECORDS = 200


class Worker:
    def __init__(self, root: str, out=None, console_package: str | None = None, console_factory=None, allowed=None):
        self.root = root; self.plans = os.path.join(root, "plans"); self.journals = os.path.join(root, "journals")
        os.makedirs(self.plans, exist_ok=True); os.makedirs(self.journals, exist_ok=True)
        self.out = out or sys.stdout; self.engine: Engine | None = None; self.last_device: dict | None = None
        self.console_package = console_package; self.home_plans = os.path.join(root, "plans", "home"); self.records = os.path.join(root, "records")
        self.cancel_flag = threading.Event(); self.send_lock = threading.Lock(); self.busy = False
        self.open_console = console_factory or _open_real; self.allowed = allowed or gate.allowed   # tests only: an injected, already simulated session

    def send(self, obj: dict):   # one line at a time: the input reader answers a cancel while a command is running
        with self.send_lock: self.out.write(json.dumps({"v": PROTOCOL_VERSION, **obj}, ensure_ascii=False) + "\n"); self.out.flush()

    def _final(self, obj: dict):   # a command's last answer: it no longer runs once its client can read that answer (a cancel sent next is refused)
        self.busy = False; self.send(obj)

    def _engine(self, req_id, plan_hash: str = "") -> Engine:
        run = new_run_id(); j = Journal(os.path.join(self.journals, run + ".jsonl"), run, plan_hash)
        self.engine = Engine(j, emit=lambda ev: self.send({"id": req_id, **ev})); return self.engine

    def handle(self, line: str):
        if len(line) > MAX_LINE: self._final({"id": None, "event": "error", "code": "too-large", "text": "request exceeds 1 MB"}); return
        try: req = json.loads(line)
        except ValueError: self._final({"id": None, "event": "error", "code": "bad-json", "text": "request is not JSON"}); return
        if not isinstance(req, dict) or req.get("v") != PROTOCOL_VERSION: self._final({"id": (req or {}).get("id") if isinstance(req, dict) else None, "event": "error", "code": "bad-version", "text": f"protocol version {PROTOCOL_VERSION} required"}); return
        rid = req.get("id"); cmd = req.get("cmd"); args = req.get("args") or {}
        if cmd not in COMMANDS or not isinstance(args, dict): self._final({"id": rid, "event": "error", "code": "bad-command", "text": "unknown command or arguments"}); return
        try:
            import platform_ids   # a missing registry refuses every request (never treated as "nothing named")
            tgt = platform_ids.check_request(args)
            if tgt["ok"] and isinstance(args.get("manifest"), dict):   # plan's collection: the same rule as every collection file
                tgt = platform_ids.resolve_target(args["manifest"], "collection", args["manifest"].get("selections"))
                if not tgt["ok"]: tgt["text"] = platform_ids.REQUEST_TEXT
        except ImportError: tgt = {"ok": False, "code": "no-registry", "text": "The platform registry is missing. Nothing was done."}
        if not tgt["ok"]: self._final({"id": rid, "event": "error", "code": "unsupported-target", "reason": tgt["code"], "text": tgt["text"]}); return
        try:
            if cmd in PHYSICAL_TIER and not self.allowed(PHYSICAL_TIER[cmd]): raise gate.GateClosed(PHYSICAL_TIER[cmd], cmd)
            result = getattr(self, "cmd_" + cmd.replace("-", "_"))(rid, args); self._final({"id": rid, "event": "result", "result": result})
        except PlanObsolete as e: self._final({"id": rid, "event": "error", "code": "plan-obsolete", "text": str(e)})
        except ccs_install.Refused as e: self._final({"id": rid, "event": "error", "code": "refused", "text": e.why, "at": e.step})
        except KeyError as e: self._final({"id": rid, "event": "error", "code": "not-found", "text": str(e)})
        except gate.GateClosed as e: self._final({"id": rid, "event": "error", "code": "physical-disabled", "text": str(e)})
        except Exception as e: self._final({"id": rid, "event": "error", "code": "worker-error", "text": f"{type(e).__name__}: {e}"})

    # ---- commands ----
    def cmd_capabilities(self, rid, a):
        return {"engine": ENGINE_VERSION, "protocol": PROTOCOL_VERSION, "capability": CAPABILITY, "tiers": gate.state(), "transports": ["fake"], "profiles": [profiles.public(n) for n in profiles.names()], "commands": list(COMMANDS),
                "home": {"profiles": list(ccs_install.HOME_PROFILES), "package_staged": bool(ccs_install.staged_package(self.console_package)), "physical": {"inspect": gate.allowed("inspect"), "install": gate.allowed("install")},
                         "models": {m: x["name"] for m, x in ccs_install.MODELS.items()}}}

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

    # ---- Classic Cart Studio's HOME under hakchi (K3) ----
    def _staged(self) -> str | None:
        """the staged package's MANIFEST.json SHA-256 (None: nothing is staged)"""
        d = ccs_install.staged_package(self.console_package)
        return ccs_install._sha(ccs_install._read(os.path.join(d, "MANIFEST.json"))) if d else None
    @staticmethod
    def _summary(p: dict) -> dict:
        return {k: v for k, v in p.items() if k not in ("files", "package_files")} | {"file_count": len(p["files"]),
                "packages": {a: {k: v for k, v in x.items() if k != "files"} for a, x in (p.get("packages") or {}).items()}}

    def _write_record(self, name: str, obj: dict):
        os.makedirs(self.records, exist_ok=True); p = os.path.join(self.records, name)
        with open(p + ".tmp", "w", encoding="utf-8") as f: json.dump(obj, f, indent=1)
        os.replace(p + ".tmp", p)
        names = sorted(n for n in os.listdir(self.records) if n.startswith("op-") and n.endswith(".json"))
        for n in names[:-MAX_RECORDS]:
            try: os.remove(os.path.join(self.records, n))
            except OSError: pass

    def cmd_home_plan(self, rid, a):
        if set(a) - set(ccs_install.STUDIO_CHOICE) - {"hakchi_games"}: raise ValueError("home-plan takes only the add-on choice (mod, nucleus, start, auto, countdown, at_start) and hakchi_games: the package is the one the app staged")
        choice = ccs_install.addon_choice({k: v for k, v in a.items() if k != "hakchi_games"})   # Refused (its reason in plain words) when the choice cannot be made
        d = ccs_install.staged_package(self.console_package)
        if not d: raise KeyError("no console package has been staged by the app")
        p = ccs_install.build_plan(d, choice, a.get("hakchi_games")); ccs_install.save_plan(p, self.home_plans)
        summary = self._summary(p)
        self._write_record("last-plan.json", {"schema": "ccs-console-last-plan/1", "saved": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "plan": summary})
        return summary

    def _home_plan(self, a) -> dict:
        h = str(a.get("plan_hash") or "")
        if not (len(h) == 64 and all(c in "0123456789abcdef" for c in h)): raise KeyError("plan_hash must be the plan's SHA-256")
        f = os.path.join(self.home_plans, f"plan-{h}.json")
        if not os.path.isfile(f): raise KeyError("plan not found (plans are addressed by hash and must be built by this worker)")
        p = ccs_install.load_plan(f)
        if p.get("manifest_sha256") != self._staged(): raise PlanObsolete("this plan is for a console package that is no longer the prepared one (the collection, loadout or HOME setup changed): prepare and review it again")
        return p

    def _stand_in(self, a):
        name = str(a.get("profile") or "hakchi-ready")
        if name not in ccs_install.HOME_PROFILES: raise KeyError(f"unknown profile {name!r}")
        d = os.path.join(self.root, "stand-ins", new_run_id()); return d, ccs_install.make_stand_in(d, name)

    def _package_of(self, p: dict) -> str: return ccs_install.staged_package(self.console_package)

    def _check(self, c, p: dict, dev: str | None, journal: str) -> dict:
        """the installer's preflight of the plan against the console (reads only): what it would add or change, or why it is refused (a
        choice that would remove an add-on: with the console's add-ons and the nearest choice that keeps them)"""
        try:
            pf = ccs_install.Installer(c, p, self._package_of(p), journal, device=dev, policy="stop" if dev else None).preflight()
            return {"ok": True, **pf, "plan_hash": p["hash"], "plan": self._summary(p)}
        except ccs_install.Refused as e: return {"ok": False, "at": e.step, "why": e.why, "plan_hash": p["hash"], **e.detail}

    def cmd_home_preflight(self, rid, a):
        p = self._home_plan(a); d, c = self._stand_in(a)
        try: return {"simulated": True, "profile": a.get("profile") or "hakchi-ready", **self._check(c, p, None, os.path.join(self.journals, f"home-preflight-{new_run_id()}.jsonl"))}
        finally: shutil.rmtree(d, ignore_errors=True)

    def _earlier_package(self, d: str, layout: str) -> str:
        """a small earlier HOME package in CCS HOME's layout (the update scenario starts from an installed earlier version), with one stand-in
        game (an empty cartridge image made here, not a game) so that an earlier collection without CCS Mod has a Hakchi game too"""
        import hashlib
        cart = b"NES\x1a\x01\x01" + bytes(10) + bytes(16384 + 8192); cs = hashlib.sha256(cart[16:]).hexdigest()
        sess = {"schema": "ccs-play-session/1", "earlier": True, "games": [{"id": "earlier", "title": "An earlier game", "rom": f"/tmp/ccs/pkg/roms/{cs}.nes", "header_hex": "", "content_sha256": cs, "unavailable": ""}]}
        files = {"bin/ccs-player-armv7": b"\x7fELF earlier player", "session.json": json.dumps(sess).encode(), "config/controller-map.txt": b"key 304 a\n", f"roms/{cs}.nes": cart}
        for rel, b in files.items():
            q = os.path.join(d, *rel.split("/")); os.makedirs(os.path.dirname(q), exist_ok=True)
            with open(q, "wb") as f: f.write(b)
        man = {"schema": "ccs-console-package/1", "layout": layout, "label": "an earlier HOME", "games": 0, "playable": 0, "console_paths": {"pkg": "/tmp/ccs/pkg"},
               "files": {rel: {"size": len(b), "sha256": hashlib.sha256(b).hexdigest(), "mode": "0o755" if rel.startswith("bin/") else "0o644"} for rel, b in files.items()}}
        with open(os.path.join(d, "MANIFEST.json"), "w", encoding="utf-8") as f: json.dump(man, f)
        return d

    def cmd_home_simulate(self, rid, a):
        p = self._home_plan(a); scenario = str(a.get("scenario") or "first-install"); then = a.get("then") or []
        if scenario not in HOME_SCENARIOS: raise ValueError(f"scenario: one of {', '.join(HOME_SCENARIOS)}")
        if not isinstance(then, list) or any(x not in HOME_THEN for x in then): raise ValueError(f"then: a list of {', '.join(HOME_THEN)}")
        fault = a.get("fault") or a.get("faults") or {}
        if not isinstance(fault, dict) or any(k not in HOME_FAULTS or not isinstance(v, int) or isinstance(v, bool) or not 0 <= v <= 10**10 for k, v in fault.items()): raise ValueError(f"fault: only {', '.join(HOME_FAULTS)} with whole numbers")
        if scenario == "interrupted" and not fault: fault = {"after": 6 if p.get("delivery") == "games" else 12}   # a lost connection while the copy is written (a games copy is small)
        d, c = self._stand_in(a); started = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
        emit = lambda ev: self.send({"id": rid, **ev, "simulated": True})
        try:
            before = ccs_install.stock_digest(d); setup = []
            if scenario in ("update", "repair", "repeat"):
                src = self._earlier_package(os.path.join(d, "..", os.path.basename(d) + "-earlier"), p["layout"]) if scenario == "update" else self._package_of(p)
                ep = p if scenario != "update" else ccs_install.build_plan(src, p["addons"], (p.get("hakchi") or {}).get("publish", False))
                r0 = ccs_install.Installer(c, ep, src, self.journals).run(); setup.append({"installed_first": r0.get("result"), "copy": r0.get("copy")})
                rep = (f"{ccs_install.GAMES_DIR}/{r0.get('copy')}/" + ((p.get("files") or [{}])[0].get("path") or "") if p.get("delivery") == "games" else f"{ccs_install.PKG_DIR}/{r0.get('copy')}/session.json")
                if scenario == "repair" and r0.get("ok") and c.stat(rep)["type"] == "file":
                    q = c.p(rep)
                    with open(q, "ab") as f: f.write(b" ")
                    setup.append({"changed_on_console": f"{rep.rsplit('/', 1)[1]} of the copy in use"})
            c = ccs_install.FakeConsole(d, fault)
            inst = ccs_install.Installer(c, p, self._package_of(p), self.journals, emit=emit, cancel=self.cancel_flag.is_set)
            r = inst.run(); journal = os.path.basename(inst.journal)
            out = {"simulated": True, "scenario": scenario, "profile": a.get("profile") or "hakchi-ready", "addons": p["addons"], "start": p["start_line"], "start_text": p["start_text"], "setup": setup, "install": {k: v for k, v in r.items() if k != "preflight"},
                   "preflight": {k: v for k, v in (r.get("preflight") or {}).items() if k != "identity"}, "writes": len(c.writes), "stock_unchanged": ccs_install.stock_digest(d) == before, "journal": journal}
            if scenario == "interrupted" and not r.get("ok"):
                emit({"event": "progress", "stage": "recover", "note": "the interrupted attempt is recovered from its own journal"})
                out["recovered"] = {"result": (r.get("rolled_back") or {}).get("ok") and "rolled-back" or r.get("result"), "verified": (r.get("rolled_back") or {}).get("verified")}
                out["after_recovery"] = ccs_install.Installer(ccs_install.FakeConsole(d), p, self._package_of(p), self.journals, emit=emit).run().get("result")
            if "uninstall" in then and (r.get("ok") or out.get("after_recovery") == "installed"):
                emit({"event": "progress", "stage": "uninstall"}); con = ccs_install.FakeConsole(d)
                rv = ccs_install.uninstall_plan(con); out["uninstall_plan"] = {k: v for k, v in rv.items() if k != "actions"} | {"actions": len(rv.get("actions") or [])}
                out["uninstall"] = ccs_install.uninstall(con, expect=rv.get("hash")); out["stock_unchanged_after_uninstall"] = ccs_install.stock_digest(d) == before
            if "export" in then and (r.get("ok") or out.get("after_recovery") == "installed"):
                emit({"event": "progress", "stage": "export"}); con = ccs_install.FakeConsole(d); sample = con.p(f"{ccs_install.DATA_DIR}/saves/" + "ab" * 32 + "/cart.sav")
                os.makedirs(os.path.dirname(sample), exist_ok=True)
                with open(sample, "wb") as f: f.write(b"\x00" * 8192)   # a cartridge save as the console would make it (synthetic)
                ex = os.path.join(d, "..", os.path.basename(d) + "-export"); er = ccs_install.export_data(con, ex)
                out["export"] = {k: v for k, v in er.items() if k != "verified"} | {"verified": er["verified"]["ok"]}; shutil.rmtree(ex, ignore_errors=True)
            self._write_record(f"op-{_stamp()}-rehearsal-{r.get('attempt')}.json", {
                "schema": "ccs-console-operation/1", "kind": "rehearsal", "simulated": True, "scenario": scenario, "started": started, "ended": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                "plan": p["hash"], "package_manifest_sha256": p["manifest_sha256"], "player_sha256": (p.get("player") or {}).get("sha256"), "engine": ENGINE_VERSION, "protocol": PROTOCOL_VERSION,
                "device": {"profile": a.get("profile") or "hakchi-ready", "hwid": ccs_install.HOME_PROFILES[a.get("profile") or "hakchi-ready"]["hwid"], "simulated": True},
                "attempt": r.get("attempt"), "stages": [e.get("op") for e in ccs_install.Installer.entries(inst.journal) if e.get("state") in (None, "done") and e.get("op") not in ("begin",)][:60],
                "readback": next(({"files": e.get("files"), "mismatches": len(e.get("mismatches") or [])} for e in ccs_install.Installer.entries(inst.journal) if e.get("op") == "readback"), None),
                "outcome": r.get("result"), "rolled_back": (r.get("rolled_back") or {}).get("ok"), "journal": journal, "then": then})
            return out
        finally:
            shutil.rmtree(d, ignore_errors=True); shutil.rmtree(d + "-earlier", ignore_errors=True)

    def cmd_home_records(self, rid, a):
        os.makedirs(self.records, exist_ok=True); return read_home_records(self.records, self._staged())

    # ---- the physical operations (each asks Gate G1 before a session is looked for; see the module docstring)
    def _device(self, a) -> str:
        d = str(a.get("device") or "")
        if not ccs_install.HWID_RE.match(d): raise KeyError("device: the console's identity as it was checked")
        return d

    def _session(self, tier: str, what: str):
        if not self.allowed(tier): raise gate.GateClosed(tier, what)
        return self.open_console(tier, self.console_package)

    def _op(self, kind: str, c, started: str, **k):
        self._write_record(f"op-{_stamp()}-{kind}-{new_run_id()[-6:]}.json", {
            "schema": "ccs-console-operation/1", "kind": kind, "simulated": getattr(c, "simulated", False) is True, "started": started, "ended": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "engine": ENGINE_VERSION, "protocol": PROTOCOL_VERSION, **k})

    def _run_session(self, tier: str, what: str, kind: str, body, admission: dict | None = None, **rec):
        """one console session for one operation: opened (gate first), the backup admission checked by the console worker before any
        write (a real console only: "admit", whose refusal ends the operation before its first change), the body run, a PC-side record
        kept, the session closed"""
        started = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()); c = self._session(tier, what)
        try:
            if tier == "install" and not getattr(c, "simulated", False):
                adm = admission or {}
                c.call("admit", {"ticket": str(adm.get("ticket") or ""), "operation": str(adm.get("operation") or ""), "subject": str(adm.get("subject") or "")})
            out = body(c)
            self._op(kind, c, started, outcome=out.get("result") or out.get("status") or ("ok" if out.get("ok") else "refused"), **rec,
                     **{k: out.get(k) for k in ("attempt", "why", "verified", "files", "folder") if out.get(k) is not None and k not in rec})
            return out | {"simulated": getattr(c, "simulated", False) is True}
        except BaseException as e:
            if not isinstance(e, (KeyboardInterrupt, SystemExit)): self._op(kind, c, started, outcome="failed", why=f"{type(e).__name__}: {e}", **rec)
            raise
        finally:
            try: c.close()
            except Exception: pass

    def cmd_home_inspect(self, rid, a):
        return self._run_session("inspect", "read a console's state", "inspect", lambda c: ccs_install.inspect_console(c))

    def cmd_home_check(self, rid, a):
        p = self._home_plan(a); dev = self._device(a)
        return self._run_session("inspect", "check a console against the plan", "check", lambda c: self._check(c, p, dev, os.path.join(self.journals, "check-not-written.jsonl")), plan=p["hash"], device=dev)

    def cmd_home_execute(self, rid, a):
        p = self._home_plan(a); dev = self._device(a); op = str(a.get("operation") or "install")
        if op not in ("install", "update"): raise ValueError("operation: install or update")
        emit = lambda ev: self.send({"id": rid, **ev})
        def body(c):
            inst = ccs_install.Installer(c, p, self._package_of(p), self.journals, emit=emit, cancel=self.cancel_flag.is_set, device=dev, policy="stop")
            r = inst.run(); return {k: v for k, v in r.items() if k != "preflight"} | {"operation": op, "journal": os.path.basename(inst.journal)}
        return self._run_session("install", "install Classic Cart Studio's HOME on a console", op, body, admission=a.get("admission"), plan=p["hash"], package_manifest_sha256=p["manifest_sha256"], device=dev)

    def cmd_home_verify(self, rid, a):
        p = self._home_plan(a); dev = self._device(a)
        return self._run_session("inspect", "verify a console", "verify", lambda c: ccs_install.verify_installed(c, p, dev), plan=p["hash"], device=dev)

    def cmd_home_uninstall_review(self, rid, a):
        dev = self._device(a); keep = a.get("keep_data") is not False
        def body(c):
            r = ccs_install.uninstall_plan(c, keep, dev)
            return ({k: v for k, v in r.items() if k != "actions"} | {"actions": len(r.get("actions") or [])}) if r.get("ok") else r
        return self._run_session("inspect", "review a removal", "uninstall-review", body, device=dev, keep_data=keep)

    def cmd_home_uninstall(self, rid, a):
        dev = self._device(a); keep = a.get("keep_data") is not False; review = str(a.get("review") or "")
        if not (len(review) == 64 and all(ch in "0123456789abcdef" for ch in review)): raise KeyError("review: the reviewed removal's hash")
        j = os.path.join(self.journals, f"uninstall-{new_run_id()}.jsonl")
        return self._run_session("install", "remove Classic Cart Studio's HOME from a console", "uninstall",
                                 lambda c: ccs_install.uninstall(c, keep, journal=j, expect=review, device=dev) | {"journal": os.path.basename(j)}, admission=a.get("admission"), device=dev, review=review, keep_data=keep)

    def _package_of_any(self, p: dict) -> str:
        """a recovery's package folder: its plan's staged layout when it is still staged (its files are only read to prove a transfer cut
        short); else the staged root, where its files are not found and nothing is assumed"""
        return ccs_install.staged_package(self.console_package) or self.console_package or ""

    def _attempt_journal(self, attempt: str):
        j = os.path.join(self.journals, f"home-{attempt}.jsonl")
        if not os.path.isfile(j): return j, None
        try: es = ccs_install.Installer.entries(j)
        except ccs_install.JournalCorrupt: return j, None
        h = (es[0] if es else {}).get("plan") or ""; f = os.path.join(self.home_plans, f"plan-{h}.json")
        try: return j, (ccs_install.load_plan(f) if len(h) == 64 and os.path.isfile(f) else None)
        except ccs_install.PlanError: return j, None

    def cmd_home_recover_review(self, rid, a):
        dev = self._device(a)
        def body(c):
            ins = ccs_install.inspect_console(c, dev)
            if not ins.get("ok"): return ins
            out = []
            for att in ins["attempts"]:
                j, plan = self._attempt_journal(att); es = []
                try: es = ccs_install.Installer.entries(j) if os.path.isfile(j) else []
                except ccs_install.JournalCorrupt: pass
                sw = [e for e in es if e.get("op") == "switch"]
                out.append({"attempt": att, "journal": os.path.isfile(j), "plan": bool(plan), "changes": sum(1 for e in es if e.get("state") == "intent"),
                            "switched": "no" if not sw else ("yes" if any(e.get("state") == "done" for e in sw) else "unknown"),
                            "recoverable": bool(plan) and bool(es), "why": None if plan and es else "this computer has no journal or plan for that attempt: its recovery needs its own diagnosis"})
            return {"ok": True, "device": ins["device"], "attempts": out}
        return self._run_session("inspect", "review a recovery", "recover-review", body, device=dev)

    def cmd_home_recover(self, rid, a):
        dev = self._device(a); att = str(a.get("attempt") or "")
        if not (len(att) == 8 and all(ch in "0123456789abcdef" for ch in att)): raise KeyError("attempt: the interrupted attempt's id")
        j, plan = self._attempt_journal(att)
        if plan is None: raise KeyError("this computer has no journal or plan for that attempt; its recovery needs its own diagnosis")
        return self._run_session("install", "recover an attempt on a console (G3: its own approval for that incident)", "recover",
                                 lambda c: ccs_install.Installer(c, plan, self._package_of_any(plan), j, device=dev, policy="stop").resume(), admission=a.get("admission"), device=dev, plan=plan["hash"], attempt=att)

    def cmd_home_export_console(self, rid, a):
        dev = self._device(a); name = time.strftime("%Y%m%d-%H%M%S") + "-" + new_run_id()[-6:]; out = os.path.join(self.root, "exports", name)
        os.makedirs(os.path.dirname(out), exist_ok=True)
        return self._run_session("inspect", "copy a console's saves", "export", lambda c: ccs_install.export_data(c, out, device=dev) | {"folder": name}, device=dev)

    def serve(self, inp=None):
        """requests in order; a reader thread keeps reading so that "cancel-if-safe" reaches a running rehearsal"""
        inp = inp or sys.stdin; q: queue.Queue = queue.Queue()
        def reader():
            for line in inp:
                s = line.strip()
                if not s: continue
                if '"cancel-if-safe"' in s and len(s) < 300:
                    try: req = json.loads(s)
                    except ValueError: req = None
                    if isinstance(req, dict) and req.get("cmd") == "cancel-if-safe":
                        if not self.busy: r = {"accepted": False, "text": "no operation running"}
                        else: self.cancel_flag.set(); r = self.engine.cancel_if_safe() if self.engine else {"accepted": True, "text": "stops at the next safe point (before the switch)"}
                        self.send({"id": req.get("id"), "event": "result", "result": r}); continue
                q.put(s)
                if s == "quit": break
            q.put(None)
        threading.Thread(target=reader, daemon=True, name="worker-input").start()
        while True:
            line = q.get()
            if line is None or line == "quit": break
            self.cancel_flag.clear(); self.busy = True
            try: self.handle(line)
            finally: self.busy = False


def main(argv=None):
    import argparse
    ap = argparse.ArgumentParser(); ap.add_argument("--root", required=True); ap.add_argument("--console-package"); a = ap.parse_args(argv)
    Worker(a.root, console_package=a.console_package).serve()


if __name__ == "__main__": main()
