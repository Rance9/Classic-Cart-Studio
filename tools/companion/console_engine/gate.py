"""Gate G1 capability model for the console engine (docs/console-install-architecture.md section 6).

Three tiers. `simulate` is always available and only ever touches fake transports and disposable copies. `inspect`
(read-only console interrogation) and `install` (anything that changes a console) are decided by the two constants below,
which are **code**: not a config key, not an environment variable, not an API argument, not a query parameter. Changing
them is a reviewed code change released as its own owner-approved milestone. `transport.open_transport` checks the same
constants again at the last boundary, so a caller that somehow reaches it with a real transport kind is still refused.
"""
from __future__ import annotations

PHYSICAL_INSPECT_ENABLED = False   # Gate G1 NOT approved (2026-10-11): no read-only console interrogation
PHYSICAL_WRITE_ENABLED = False     # Gate G1 NOT approved (2026-10-11): no console writes of any kind

TIERS = ("simulate", "inspect", "install")
GATE_STATE_TEXT = "Installing on a real console is not available in this version."   # Gate G1 not approved; shown in the interface


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
