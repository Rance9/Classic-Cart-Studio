"""Gate G1 capability model for the console engine (docs/console-install-architecture.md section 6).

Three tiers. `simulate` is always available and only ever touches fake transports and disposable copies. `inspect`
(read-only console interrogation) and `install` (anything that changes a console) are decided by the two constants below,
which are **code**: not a config key, not an environment variable, not an API argument, not a query parameter. Changing
them is a reviewed code change released as its own milestone; this version opens both.
The two physical tiers stay separate all the way down: a console session is opened for one of them (worker_console), a
session opened to inspect refuses every change itself, and the transport worker asks the write tier again for each command
that changes the console. Every change also needs its own checks: the console's identity and model, a verified backup of it,
the reviewed plan and a confirmation for exactly that operation (console_home, ccs_install). Only Classic Cart Studio's own
HOME operations (ccs_install) reach a console; the older game sync through hakchi is not part of Classic Cart Studio.
"""
from __future__ import annotations

PHYSICAL_INSPECT_ENABLED = True    # open in this version: reading a real console
PHYSICAL_WRITE_ENABLED = True      # open in this version: changing a real console, after its own checks, plan and confirmation

TIERS = ("simulate", "inspect", "install")
GATE_STATE_TEXT = "Changing a real console is turned off in this build."   # shown only while a tier is closed in code


class GateClosed(PermissionError):
    """Raised when an operation needs a tier that Gate G1 has not opened."""
    def __init__(self, tier: str, what: str = ""):
        self.tier = tier
        super().__init__(f"{GATE_STATE_TEXT} ({what or tier}: tier '{tier}' is closed in code, Gate G1)")


def allowed(tier: str) -> bool:
    if tier == "simulate": return True
    if tier == "inspect": return PHYSICAL_INSPECT_ENABLED is True
    if tier == "install": return PHYSICAL_WRITE_ENABLED is True
    return False


def require(tier: str, what: str = "") -> None:
    if not allowed(tier): raise GateClosed(tier, what)


def state() -> dict:
    """Machine-readable gate state for /api/version and the UI; never trusts anything but the constants."""
    return {"simulate": True, "inspect": allowed("inspect"), "install": allowed("install"), "text": GATE_STATE_TEXT if not allowed("install") else "physical writes enabled"}
