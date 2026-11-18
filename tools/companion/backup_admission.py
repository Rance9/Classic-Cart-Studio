"""The backup prerequisite for Classic Cart Studio's console maintenance writes (D-154): before the backend starts an installation, update,
repair, removal or recovery on a console, the console must have a verified backup. This module asks the shipped console worker
(ccs-console-worker.exe --admit-backup), which runs the SAME validation as Console Backup & Restore (tools/console-backup/CcsBackup,
Engine/Admission.cs): the console's backups are verified from scratch, the oldest complete recovery baseline (with the original Nintendo
kernel) is required, and an operation that can remove what was made since needs a backup newer than the console's last change. The answer
is a single-use admission for exactly this console, operation and subject, which the run's own worker checks again before its first write.
Nothing here can grant an admission by itself: no setting, environment variable, request field or fallback. Copyright (C) 2026 Rance9,
GPL-3.0-or-later."""
from __future__ import annotations
import json, re, subprocess

OPERATIONS = ("prepare", "install", "update", "repair", "uninstall", "uninstall-data", "recover")

def operation_for(op: str, body: dict | None = None) -> str:
    """the admission's operation for a dispatcher operation (a removal that also deletes the console's Classic Cart Studio data is its own kind)"""
    if op == "uninstall" and (body or {}).get("keep_data") is False: return "uninstall-data"
    return op

def admit(hwid: str, operation: str, subject: str, exe: str | None = None, settings: str | None = None, timeout: int = 1800) -> dict:
    """{"ok": True, "ticket": …, "baseline": …, "current": …} or {"ok": False, "code": …, "text": …}; never raises for a refusal"""
    if operation not in OPERATIONS: return {"ok": False, "code": "bad-operation", "text": "Unknown maintenance operation."}
    if not re.fullmatch(r"[0-9a-fA-F ]{8,64}", str(hwid or "")): return {"ok": False, "code": "no-console", "text": "Check the console first."}
    if exe is None:
        from console_engine.worker_console import worker_exe
        exe = worker_exe()
    if not exe: return {"ok": False, "code": "backup-required", "text": "The backup check is not available in this installation, so no change can be made to a console."}
    args = [exe, "--admit-backup", "--hwid", str(hwid), "--operation", operation, "--subject", str(subject or "")] + (["--settings", settings] if settings else [])
    try: p = subprocess.run(args, capture_output=True, timeout=timeout, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    except (OSError, subprocess.TimeoutExpired) as e: return {"ok": False, "code": "backup-required", "text": f"The backup check could not run ({type(e).__name__}); nothing was changed."}
    try: r = json.loads(p.stdout.decode("utf-8"))
    except (ValueError, UnicodeDecodeError): return {"ok": False, "code": "backup-required", "text": "The backup check gave no answer; nothing was changed."}
    if p.returncode != 0 or r.get("ok") is not True or not re.fullmatch(r"[0-9a-f]{32}", str(r.get("ticket") or "")):
        return {"ok": False, "code": str(r.get("code") or "backup-required"), "text": str(r.get("text") or "Back up this console first.")}
    return {"ok": True, "ticket": r["ticket"], "baseline": r.get("baseline"), "current": r.get("current"), "baseline_utc": r.get("baseline_utc"), "current_utc": r.get("current_utc"), "text": r.get("text")}
