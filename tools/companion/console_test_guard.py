"""Test support only (never imported by the app, so never bundled): the controlled console boundary for the console tests. Each console
test module imports it first. It stands in front of worker_console.open_worker_console, the only way to a real console (it starts the
shipped transport worker and sends "connect"):
  - while a tier is closed it asks the gate exactly as before, so a closed tier is refused the same way;
  - once a tier is open (the activation rehearsal: the reviewed delta applied to a temporary copy) it refuses every real session before any
    worker starts, and records the request next to this file (.console-guard-requests.txt), so the rehearsal can list each one.
By construction, too, a source run cannot start the shipped worker: worker_console.worker_exe() names it only inside the frozen app. A
worker process started by a test (console_runs) does not import this module; there worker_exe() is what stops it.
Copyright (C) 2026 Rance9, GPL-3.0-or-later."""
from __future__ import annotations
import os
from console_engine import gate, worker_console

LOG = os.path.join(os.path.dirname(os.path.abspath(__file__)), ".console-guard-requests.txt")

class RealConsoleBlocked(RuntimeError):
    """a test asked for a real console session while its tier was open: refused before any worker started"""

def _guarded(kind: str, package_dir: str | None = None, tier: str = "install"):
    if tier not in ("inspect", "install"): raise ValueError("tier: inspect or install")
    gate.require(tier, f"open a {kind} console")   # a closed tier: refused exactly as the shipped opener refuses it
    with open(LOG, "a", encoding="utf-8") as f: f.write(f"{kind} {tier}\n")
    raise RealConsoleBlocked(f"a test asked for a real {kind} console ({tier}): blocked by the test boundary before any worker started")

def install():
    if getattr(worker_console.open_worker_console, "guard", False): return
    _guarded.guard = True; _guarded.original = worker_console.open_worker_console
    worker_console.open_worker_console = _guarded

install()
