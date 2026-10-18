"""Operation sequencing for the console engine, following the pinned Hakchi2 CE 3.9.3 order and preconditions
(hakchi.cs Shell_OnConnected, SyncTask.UploadGames, ModTasks, MembootTasks) but emitting structured events instead of
WinForms dialogs. In this build every operation runs against `FakeTransport`; `execute` (physical) is refused by Gate G1
at two boundaries. States are the truthful set from the directive (section D2)."""
from __future__ import annotations
import re, time, uuid
from . import gate, plan as planmod
from .transport import FakeTransport, Disconnected, TransportError, open_transport
from .journal import Journal

MIN_BOOT, MIN_KERNEL, MIN_SCRIPT = (1, 0, 2), (3, 4, 113), (1, 0, 4, 118)     # hakchi.cs 285-298
FEL_BOARD_NES = 0x00166700                                                     # FelLib.Fel.Open
NES_SYSTEM_CODES = ("nes-usa",)                                                # this product: North American NES Classic only
SAFE_CANCEL_STAGES = ("identify", "preflight", "stage", "modules-planned", "diff", "done")   # never inside transfer/link/config/memboot


def _ver(s: str) -> tuple:
    m = re.search(r"(?:\d+[.-]){1,3}\d+", s or "")
    return tuple(int(x) for x in m.group(0).replace("-", ".").split(".")) if m else (0,)


class Cancelled(RuntimeError): pass


class Engine:
    def __init__(self, journal: Journal, emit=None):
        self.j = journal; self._emit = emit or (lambda ev: None); self.stage = "idle"; self.cancel_requested = False; self.transport = None

    # ---- events ----
    def emit(self, event: str, stage: str, **data):
        rec = self.j.append(stage, event, data or None, level=data.get("level", "info") if data else "info", code=data.get("code", "") if data else "")
        self._emit({"event": event, "stage": stage, **data, "ts": rec["ts"]}); return rec

    def progress(self, stage: str, done: int, total: int, detail: str = ""): self.emit("progress", stage, done=done, total=total, detail=detail)

    def _checkpoint(self, stage: str):
        self.stage = stage
        if self.cancel_requested and stage in SAFE_CANCEL_STAGES: raise Cancelled(f"cancelled at the safe checkpoint before {stage}")

    # ---- identify (what Shell_OnConnected learns) ----
    def identify(self, transport: FakeTransport) -> dict:
        """Returns the device record for the plan. Simulated transports are marked `simulated: true` and the state names
        say so; this never claims a console is connected."""
        self.transport = transport; self._checkpoint("identify"); t = transport
        usb = t.usb_present(); d = {"simulated": t.simulated, "source": "simulation" if t.simulated else "inspection", "measured": False, "usb": usb, "driver": t.driver()}
        if usb is None:
            d.update(state="Console not connected", state_code="no-device", detail="nothing answered on USB (a stock console shows nothing in normal mode)"); self.emit("identified", "identify", **d); return d
        if t.driver() != "ok":
            d.update(state="Console detected but not identified", state_code="driver-" + t.driver(), detail={"missing": "the FEL/clovershell device needs the WinUSB driver (a separate, elevated step)", "wrong-binding": "a different driver is bound to the device"}[t.driver()])
            self.emit("identified", "identify", **d); return d
        if usb.get("mode") == "fel":
            board = t.fel_board(); d["fel_board"] = f"0x{board:08x}"
            if board != FEL_BOARD_NES: d.update(state="Console detected but not identified", state_code="fel-wrong-board", detail="FEL answered with a board id that is not an NES/SNES Classic SoC")
            else: d.update(state="Console recognized, mode known", state_code="fel", mode="FEL (recovery)", firmware_state="stock-or-unknown", needs_first_time_setup=True, detail="FEL mode: no shell; first-time setup would memboot the custom kernel (physical, gated)")
            self.emit("identified", "identify", **d); return d
        try: t.open()
        except Disconnected as e:
            d.update(state="Console detected but not identified", state_code="no-shell", detail=str(e)); self.emit("identified", "identify", **d); return d
        d["shell"] = t.p.get("shell"); d["md_partitioning"] = t.execute("hakchi ismdPartitioning")[0] == 0; d["minimal_memboot"] = t.execute("source /hakchi/config; [ \"$cf_memboot\" = \"y\" ]")[0] == 0
        d["hwid"] = t.execute_simple("hakchi hwid")
        if t.execute("[ -f /var/version ] && echo \"yes\"")[1].strip() == "yes":
            b, k, s = (t.execute_simple("source /var/version && echo \"$bootVersion $kernelVersion $hakchiVersion\"").split(" ") + ["", "", ""])[:3]
            d["versions"] = {"boot": b, "kernel": k, "script": s}
            reflash = _ver(k) < MIN_KERNEL or _ver(b) < MIN_BOOT; rootfs = _ver(s) < MIN_SCRIPT
            d["can_interact"] = not reflash and not rootfs
            d["firmware_state"] = "compatible-custom" if d["can_interact"] else "outdated-custom"
        else:
            d["versions"] = None; d["can_interact"] = False; d["firmware_state"] = "unknown"
        d["can_sync"] = bool(d["can_interact"]) and d["shell"] != "clovershell"      # CanSync = !(caller is ClovershellConnection)
        if d["can_interact"]:
            d["system_code"] = t.execute_simple("hakchi eval 'echo \"$sftype-$sfregion\"'"); d["firmware"] = t.execute_simple("hakchi currentFirmware")
            d["sync_path"] = t.execute_simple("hakchi findGameSyncStorage"); d["config"] = dict(t.cfg)
            parts = [x.strip() for x in t.execute_simple("df \"$(hakchi findGameSyncStorage)\" | tail -n 1 | awk '{ print $2 \" | \" $3 \" | \" $4 }'").split("|")]
            total, used, free = (int(parts[0]) * 1024, int(parts[1]) * 1024, int(parts[2]) * 1024) if len(parts) == 3 else (0, 0, 0)
            saves = int(t.execute_simple("du -s \"$(readlink /var/saves)\" | awk '{ print $1 }'") or 0) * 1024
            d["storage"] = {"total": total, "used": used, "free": free, "saves": saves, "games": t.games_bytes()}
            d["modules"] = [m for m in t.execute_simple("hakchi pack_list").split("\n") if m]
            d["backup_state"] = "verified" if t.execute("hakchi haveBackup2")[0] == 0 else "missing"
            d["backup_artifacts"] = ["stock kernel backup (boot2, on the console)"] if d["backup_state"] == "verified" else []
            d["foreign"] = {}
            for rel, size in t.fs.items():
                code = rel.split("/")[0]; d["foreign"].setdefault(code, {})[rel] = size
            if d["md_partitioning"]: d.update(state="Unknown or incompatible environment", state_code="md-partitioning", detail="Mega Drive style partitioning: not an NES flow")
            elif d["system_code"] not in NES_SYSTEM_CODES: d.update(state="Unknown or incompatible environment", state_code="wrong-model", detail=f"this console reports {d['system_code']}; Classic Cart Studio supports the North American NES Classic (nes-usa)")
            elif not d["can_sync"]: d.update(state="Console recognized, mode known", state_code="clovershell-only", detail="connected over legacy clovershell: upstream disables game sync on this link; the network (RNDIS) link is required")
            else: d.update(state="Compatible modified environment already present", state_code="compatible", needs_first_time_setup=False)
        else:
            d.update(state="Unknown or incompatible environment" if d["firmware_state"] == "unknown" else "First-time setup required", state_code="outdated" if d["versions"] else "no-version",
                     detail="the installed custom environment is older than the pinned minimums (kernel 3.4.113, boot 1.0.2, script 1.0.4.118): upstream would reflash" if d["versions"] else "no /var/version on the console: severely outdated or unknown software",
                     needs_first_time_setup=True)
        self.emit("identified", "identify", **{k: v for k, v in d.items() if k != "foreign"}); return d

    # ---- preflight (pure, from the plan) ----
    def preflight(self, p: dict) -> dict:
        self._checkpoint("preflight"); blocks, warns = [], []
        for e in p["entries"]:
            if e["kind"] == "unresolved": blocks.append({"code": "unresolved-entry", "text": f"{e['title']}: {e.get('detail') or 'not resolvable'}"})
            if e["action"] == "add" and not e.get("size"): blocks.append({"code": "missing-file", "text": f"{e['title']}: game file missing on this computer"})
            if e["action"] == "add" and "?" in e.get("requires", []): blocks.append({"code": "unsupported-core", "text": f"{e['title']}: no module route for core {e.get('core')!r}"})
        for m in p["modules"]:
            if m["action"] == "install" and not m["cleared"]: blocks.append({"code": "module-not-cleared", "text": f"{m['name']}: required by {m['needed_by']} game(s), not cleared for distribution"})
        d = p["device"]; sc = d.get("state_code")
        if d.get("source") == "none": blocks.append({"code": "no-device-state", "text": "no console state (not connected, or inspection not approved)"})
        elif sc in ("wrong-model", "md-partitioning", "fel-wrong-board"): blocks.append({"code": sc, "text": d.get("detail", sc)})
        elif sc in ("driver-missing", "driver-wrong-binding"): blocks.append({"code": sc, "text": d.get("detail", sc)})
        elif sc in ("no-device",): blocks.append({"code": "no-device", "text": "console not connected"})
        elif sc in ("fel", "outdated", "no-version"): blocks.append({"code": "first-time-setup-required", "text": "first-time setup (memboot, kernel flash) is a separate physical operation; disabled pending Gate G1"})
        elif sc == "clovershell-only": blocks.append({"code": "link-cannot-sync", "text": d.get("detail", sc)})
        if p["backup"]["state"] in ("missing", "invalid"): blocks.append({"code": "backup-" + p["backup"]["state"], "text": "the stock kernel backup on the console is " + p["backup"]["state"] + "; a write would not be allowed without a verified recovery path"})
        if p["budget"]["fits"] is False: blocks.append({"code": "storage-insufficient", "text": f"not enough free space: {p['budget']['free']} free, {p['budget']['proposed_add_bytes']} needed plus a {p['budget']['margin']} margin"})
        if p["budget"]["source"] == "reference": warns.append({"code": "storage-estimated", "text": "storage is a reference estimate, not a measurement of your console"})
        if p["budget"]["fits"] is True and p["budget"]["free"] - p["budget"]["proposed_add_bytes"] < 2 * p["budget"]["margin"]: warns.append({"code": "storage-marginal", "text": "free space after the install would be small"})
        if p["summary"].get("foreign"): warns.append({"code": "foreign-kept", "text": f"{p['summary']['foreign']} existing game(s) on the console are not in this collection and are kept"})
        ok = not blocks and p["can_proceed"]
        r = {"ok": ok, "blocks": blocks, "warnings": warns, "state": "Ready for simulated rehearsal" if ok and d.get("simulated") else ("Collection incomplete" if any(b["code"] in ("unresolved-entry", "missing-file") for b in blocks) else ("Ready" if ok else "Blocked")),
             "physical": gate.state()}
        self.emit("preflight", "preflight", ok=ok, blocks=len(blocks), warnings=len(warns)); return r

    # ---- the operation (simulate tier) ----
    def simulate(self, p: dict, transport: FakeTransport, expected_before: dict | None = None) -> dict:
        """Run the upstream sequence against the fake device: modules, differential sync (delete absent, transfer new),
        link, config, read-back, verify. Returns a result whose `state` is never a physical success."""
        gate.require("simulate"); t = transport; res = {"simulated": True, "plan_hash": p["hash"], "stages": [], "state": None}
        try:
            self._checkpoint("preflight"); pf = self.preflight(p)      # pure: decided before anything touches the (fake) device
            if not pf["ok"]: res.update(state="Blocked", preflight=pf); return res
            if not t.online: t.open()
            local = planmod.local_file_set(p); before = dict(t.fs)
            # idempotence: the same plan against the same verified state is a no-op
            if before == local and all(m["action"] != "install" for m in p["modules"]):
                res.update(state="Simulated install succeeded (no change needed)", noop=True, verify={"match": True, "added": 0, "removed": 0}); self.emit("noop", "done"); return res
            self._checkpoint("modules-planned"); self.stage = "modules"
            need = [m["name"] for m in p["modules"] if m["action"] == "install"]
            if need: self.emit("stage", "modules", modules=need); t.install_modules(need); res["stages"].append("modules")
            self._checkpoint("diff"); self.stage = "diff"
            t.execute("hakchi eval 'umount \"$gamepath\"'"); listing = t.execute("mkdir -p \"/var/lib/hakchi/games\"; cd \"/var/lib/hakchi/games\"; find . -type f -exec sh -c \"stat {} -c '%n %s %y'\" \\;")[1]
            remote = {}
            for line in listing.split("\n"):   # "%n %s %y": names may contain spaces, so parse from the right like upstream's ApplicationFileInfo
                mm = re.match(r"^\./(.+) (\d+) (\d{4}-\d\d-\d\d .*)$", line.strip())
                if mm: remote[mm.group(1)] = int(mm.group(2))
            to_delete = [r for r in remote if r not in local]; to_send = {r: s for r, s in local.items() if remote.get(r) != s}
            self.emit("stage", "diff", remote=len(remote), to_delete=len(to_delete), to_send=len(to_send), bytes=sum(to_send.values())); res["stages"].append("diff")
            self.stage = "transfer"; t.delete_files(to_delete)
            if to_send:
                self.emit("stage", "transfer", files=len(to_send), bytes=sum(to_send.values()))
                t.transfer(to_send, on_progress=lambda d, tot, rel: self.progress("transfer", d, tot, rel))
            res["stages"].append("transfer")
            self.stage = "link"; t.execute("hakchi eval 'true'"); res["stages"].append("link")
            self.stage = "config"; hidden = [e["code"] for e in p["entries"] if e["action"] == "hide"]; t.cfg["hidden_factory_games"] = ",".join(hidden); res["stages"].append("config")
            t.execute("hakchi overmount_games; uistart")
            self._checkpoint("done"); self.stage = "verify"
            rb = t.read_back(); exp = planmod.expected_after(p)
            missing = sorted(r for r in exp if rb.get(r) != exp[r]); extra = sorted(r for r in rb if r not in exp)
            res["verify"] = {"match": not missing and not extra, "missing": missing[:20], "extra": extra[:20], "read_back_files": len(rb), "expected_files": len(exp), "modules_present": all(m["name"] in t.modules for m in p["modules"]), "hidden_config": hidden}
            res["state"] = "Simulated install succeeded" if res["verify"]["match"] and res["verify"]["modules_present"] else "Simulated install unverified"
            self.emit("verified", "verify", match=res["verify"]["match"], state=res["state"]); return res
        except Cancelled as e:
            res.update(state="Cancelled at a safe checkpoint", error={"code": "cancelled", "text": str(e), "stage": self.stage}); self.emit("cancelled", self.stage, text=str(e), level="warn", code="cancelled"); return res
        except Disconnected as e:
            res.update(state="Potentially recoverable failure / unknown device condition", error={"code": "disconnected", "text": str(e), "stage": self.stage, "recovery": self.recovery_advice(self.stage)})
            self.emit("failure", self.stage, text=str(e), level="error", code="disconnected"); return res
        except TransportError as e:
            res.update(state="Potentially recoverable failure / unknown device condition", error={"code": "device-error", "text": str(e), "stage": self.stage, "recovery": self.recovery_advice(self.stage)})
            self.emit("failure", self.stage, text=str(e), level="error", code="device-error"); return res

    def execute(self, p: dict, transport_kind: str = "ssh") -> dict:
        """Physical operation: refused by Gate G1 in code; the transport boundary refuses again."""
        try: gate.require("install", "execute installation plan")
        except gate.GateClosed as e:
            self.emit("refused", "execute", text=str(e), level="warn", code="physical-disabled"); return {"state": gate.GATE_STATE_TEXT, "code": "physical-disabled", "simulated": False, "plan_hash": p["hash"]}
        open_transport(transport_kind)   # unreachable while the gate is closed; and still refused at the boundary
        return {"state": "unreachable"}

    def cancel_if_safe(self) -> dict:
        self.cancel_requested = True; safe = self.stage in SAFE_CANCEL_STAGES or self.stage == "idle"
        self.emit("cancel-request", self.stage, safe=safe, level="warn")
        return {"accepted": True, "safe_now": safe, "stage": self.stage, "text": "will stop at the next safe checkpoint" if not safe else "stopped"}

    @staticmethod
    def recovery_advice(stage: str) -> str:
        return {"transfer": "reconnect the console and run the same plan again: unchanged files are kept, missing ones are sent again; nothing was flashed",
                "modules": "reconnect and run again; module installation is repeatable",
                "memboot": "the console may be in an unknown state: power-cycle it; if the HOME menu does not appear, hold RESET while connecting USB (FEL) and ask for help before any write",
                "verify": "the files were sent but could not be read back: reconnect and verify again before trusting the result"}.get(stage, "reconnect the console and run the plan again")


def new_run_id() -> str: return time.strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:6]
