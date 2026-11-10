r"""Classic Cart Studio for Windows: optional usage counts (D-123). The app's own page cannot reach the internet (its policy allows only
this backend), so the page hands each count here; this module checks the choice kept on this computer (usage-reporting.json in the
app's data folder, %LOCALAPPDATA%\ClassicCartStudio: written by the setup wizard's optional choice before the first launch, or here by
Studio menu > Usage Reporting; no valid file means Off), checks the count against the same fixed lists as
the collector, fills in what only it should say (Windows app, its own version, Edge/Windows), and sends it to the one collector address
below. Never anything else: no game, collection, file, save, controller or identifier, no IP or other detail of this computer. A send
waits at most 4 s in the background and is dropped on any failure (no queue, no retry, at most two at a time); nothing the app does
waits for it. Turning it Off is in force at once, even when the choice cannot be kept on disk (held Off for this run): a count admitted
before a decision is checked again at the moment it would be sent, and dropped. The choice is never part of a backup or export, and a restore never sets it. Copyright (C) 2026 Rance9, GPL-3.0-or-later."""
from __future__ import annotations
import json, os, re, threading, urllib.request

COLLECTOR = "https://metrics.classiccartstudio.com/v1/events"   # the only remote address this module ever contacts
CHOICE_FILE = "usage-reporting.json"; CHOICE_SCHEMA = "ccs-usage-choice/1"
EVENTS = {"studio_opened", "home_opened", "play_started", "backup_completed", "restore_completed", "operation_failed", "download_clicked"}
OPERATIONS = {"home", "play", "backup", "restore", "storage"}
REASONS = {"not_ready", "missing_rom", "storage_unavailable", "read_failed", "write_failed", "invalid_data", "unsupported", "other"}
ELAPSED = {"under_1s", "1_to_5s", "5_to_15s", "15_to_60s", "over_60s"}
FIELDS = {"schema", "event", "app_version", "surface", "browser", "os", "operation", "reason", "elapsed"}
VERSION_RE = re.compile(r"^\d{1,3}\.\d{1,3}\.\d{1,3}(?:-[a-z0-9]{1,16})?$")
_lock = threading.Lock(); _inflight = 0
_epoch = 0          # every decision (On or Off, kept or not) starts a new epoch: a count admitted under an earlier one is never sent
_held_off = False   # Off asked for in this run of the app but not kept on disk: in force until a choice is kept

def load_choice(data_path) -> dict:
    """the effective choice: on or off, whether a person chose it, and where (installer, app). Other keys are ignored; anything that is
    not exactly this schema with "on" or "off" counts as no decision, which is Off"""
    try:
        with open(data_path(CHOICE_FILE), encoding="utf-8") as f: j = json.load(f)
        if isinstance(j, dict) and j.get("schema") == CHOICE_SCHEMA and j.get("choice") in ("on", "off"):
            return {"choice": j["choice"], "chosen": True, "source": j.get("source") if j.get("source") in ("installer", "app") else None}
    except (OSError, ValueError, UnicodeDecodeError): pass
    return {"choice": "off", "chosen": False, "source": None}   # Off until the person chooses

def effective_choice(data_path) -> dict:
    """the choice in force now: Off held in this run (asked for, not kept) wins over the file"""
    with _lock:
        if _held_off: return {"choice": "off", "chosen": True, "source": "app", "held": True}
    return {**load_choice(data_path), "held": False}

def _permitted(data_path) -> bool:
    return not _held_off and load_choice(data_path)["choice"] == "on"

def save_choice(data_path, choice: str) -> dict:
    """keeps the choice (OSError when it cannot). Off is in force before the write is tried, and stays in force if the write fails;
    a failed On changes nothing. Under the lock, so a count's last check before sending sees one decision or the next, never half of one"""
    global _epoch, _held_off
    if choice not in ("on", "off"): raise ValueError("choice")
    with _lock:
        _epoch += 1
        if choice == "off": _held_off = True
        p = data_path(CHOICE_FILE); os.makedirs(os.path.dirname(p), exist_ok=True); tmp = p + ".tmp"
        try:
            with open(tmp, "w", encoding="utf-8") as f: json.dump({"schema": CHOICE_SCHEMA, "choice": choice, "source": "app"}, f)
            os.replace(tmp, p)
        except OSError:
            try: os.remove(tmp)   # no half-written copy left behind
            except OSError: pass
            raise
        _held_off = False   # kept: the file is the choice again
    return {"choice": choice, "chosen": True, "source": "app", "held": False}

def event_from(body, version: str) -> dict | None:
    """the count as the collector accepts it, or None. The page's surface, browser, system and version are replaced by this app's own"""
    if not isinstance(body, dict) or set(body) - FIELDS or body.get("schema") != 1 or body.get("event") not in EVENTS or not VERSION_RE.match(str(version or "")): return None
    d = {"schema": 1, "event": body["event"], "app_version": version, "surface": "windows", "browser": "edge", "os": "windows"}
    if body["event"] == "operation_failed":
        if body.get("operation") not in OPERATIONS or body.get("reason") not in REASONS: return None
        d["operation"] = body["operation"]; d["reason"] = body["reason"]
    elif "operation" in body or "reason" in body: return None
    if body.get("elapsed") is not None:
        if body["elapsed"] not in ELAPSED: return None
        d["elapsed"] = body["elapsed"]
    return d

def _send(d: dict):   # tests replace this with their own (from Python only)
    rq = urllib.request.Request(COLLECTOR, data=json.dumps(d).encode(), method="POST", headers={"Content-Type": "application/json", "User-Agent": "ClassicCartStudio"})
    with urllib.request.urlopen(rq, timeout=4) as r: r.read(64)

def relay(d: dict, data_path) -> bool:
    global _inflight
    with _lock:
        if _inflight >= 2: return False   # never a backlog
        _inflight += 1; epoch = _epoch
    def run():
        global _inflight
        try:
            with _lock: go = epoch == _epoch and _permitted(data_path)   # the send boundary: no decision since it was admitted, and still On
            if go: _send(d)
        except Exception: pass   # offline, refused or slow: dropped
        finally:
            with _lock: _inflight -= 1
    threading.Thread(target=run, daemon=True, name="usage-count").start(); return True

def handle(handler, method: str, path: str, body, data_path, version: str):
    """/api/metrics/choice (GET, POST {choice}) and /api/metrics/event (POST): Classic Cart Studio for Windows only (desktop principal)"""
    b = handler._bearer()
    if not (b and b.get("desktop")): return handler._json(403, {"ok": False, "error": "Available only in Classic Cart Studio for Windows."})
    if path == "/api/metrics/choice":
        if method == "GET": return handler._json(200, {"ok": True, **effective_choice(data_path)})
        try: return handler._json(200, {"ok": True, **save_choice(data_path, str((body or {}).get("choice") or ""))})
        except ValueError: return handler._json(400, {"ok": False, "error": "not a choice"})
        except OSError: return handler._json(500, {"ok": False, "error": "The choice could not be saved.", **effective_choice(data_path)})   # Off is still in force
    if path == "/api/metrics/event" and method == "POST":
        if not _permitted(data_path): return handler._json(200, {"ok": True, "sent": False})   # Off (kept or held) or never chosen: nothing leaves
        d = event_from(body, version)
        if d is None: return handler._json(400, {"ok": False, "error": "not a usage count"})
        return handler._json(200, {"ok": True, "sent": relay(d, data_path)})
    return handler._json(404, {"ok": False, "error": "unknown"})
