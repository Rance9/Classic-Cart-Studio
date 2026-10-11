"""Fake NES Classic device profiles for the simulator (directive Phase 4 list). A profile describes what a console would
answer to the commands Hakchi2 CE 3.9.3 issues after a shell connects (hakchi.cs Shell_OnConnected, MemoryStats.Refresh,
SyncTask.CalculateRemoteDiff): the USB presence and mode, the FEL board id, the on-device versions, the system code,
the firmware mode, the storage figures, installed modules, existing games and saves, plus failure injection points.

Numbers come from the pinned sources: minimum boot 1.0.2, kernel 3.4.113, script 1.0.4.118 (hakchi.cs 285-298); the
pinned hmod ships boot 1.0.3, kernel 3.4.113.29-madmonkey, script v1.0.4-126 (var/version); FEL board id 0x00166700
(FelLib.Fel.Open); USB ids 1F3A:EFE8 for FEL and the clovershell gadget, 04E8:6863 for the RNDIS gadget (docs/research.md
section 3). Storage figures are illustrative reference values for a stock NES Classic data partition, labeled as such.
"""
from __future__ import annotations
import copy

USB_FEL = {"vid": 0x1F3A, "pid": 0xEFE8, "mode": "fel"}
USB_CLOVERSHELL = {"vid": 0x1F3A, "pid": 0xEFE8, "mode": "clovershell"}
USB_RNDIS = {"vid": 0x04E8, "pid": 0x6863, "mode": "rndis", "ip": "169.254.13.37"}

PINNED_VERSIONS = {"bootVersion": "1.0.3", "kernelVersion": "3.4.113.29-madmonkey", "hakchiVersion": "v1.0.4-126"}
HWID_A = "01c2380012345678abcdef0000000001"
HWID_B = "01c2380087654321fedcba0000000002"

# Reference storage (bytes). A stock NES Classic's /dev/nandc data partition is a few hundred MiB; the Studio's own
# reference estimate lives in the website and is labeled "reference" there too. Measured values only ever come from a
# real inspection, which Gate G1 does not allow in this milestone.
STORAGE_REF = {"total": 310 * 1024 * 1024, "reserved_pinned_modules": 8 * 1024 * 1024}

BASE = {
    "name": "", "label": "", "usb": None, "fel_board": 0x00166700, "driver": "ok",           # driver: ok | missing | wrong-binding
    "shell": None,                                      # None (no shell), "clovershell", "ssh"
    "versions": None, "system_code": "nes-usa", "hwid": HWID_A, "firmware": "_nand_",      # firmware: _nand_ (stock root) | path (custom)
    "md_partitioning": False, "minimal_memboot": False,
    "storage": {"total": STORAGE_REF["total"], "used_other": 6 * 1024 * 1024, "saves": 2 * 1024 * 1024},
    "games": {},                                        # code -> {"files": {relpath: size}, "kind": "foreign"|"studio"}
    "modules": [],                                      # installed hmod names (pack_list)
    "backup2": True,                                    # haveBackup2: the stock kernel backup is present on the console
    "config": {},                                       # cfg_* values in /etc/preinit.d/p0000_config
    "fail": None,                                       # {"at": stage, "kind": "disconnect"|"error"|"timeout"} injected during execute
    "notes": [],
}

def _p(**kw) -> dict:
    d = copy.deepcopy(BASE); d.update(kw); return d

def _games(n: int, prefix: str = "CLV-H-F", size: int = 300 * 1024, kind: str = "foreign") -> dict:
    out = {}
    for i in range(n):
        code = f"{prefix}{i:04d}"[:11]
        out[code] = {"kind": kind, "files": {f"{code}/{code}.desktop": 700, f"{code}/{code}.png": 40_000, f"{code}/{code}_small.png": 6_000, f"{code}/{code}.nes": size}}
    return out

PROFILES: dict[str, dict] = {
    "no-device": _p(name="no-device", label="No console connected", usb=None, notes=["nothing on USB"]),
    "stock-normal": _p(name="stock-normal", label="Stock (unmodified) NES Classic, powered on", usb=None,
                       notes=["an unmodified console exposes no USB device in normal mode: nothing to talk to until FEL"]),
    "stock-fel": _p(name="stock-fel", label="Stock NES Classic in FEL mode (RESET held while connecting)", usb=USB_FEL, shell=None, versions=None,
                    notes=["FEL answers VERIFY_DEVICE with board 0x00166700; no custom kernel; first-time setup would be required"]),
    "stock-fel-driver-missing": _p(name="stock-fel-driver-missing", label="FEL device present, WinUSB driver missing", usb=USB_FEL, driver="missing"),
    "stock-fel-driver-wrong": _p(name="stock-fel-driver-wrong", label="FEL device present, a different driver bound", usb=USB_FEL, driver="wrong-binding"),
    "modified-compatible": _p(name="modified-compatible", label="Hakchi CE environment already installed and compatible", usb=USB_RNDIS, shell="ssh",
                              versions=dict(PINNED_VERSIONS), firmware="_nand_", modules=["clovercon", "font_remount", "gamepath-cleanup"], games={}, backup2=True),
    "modified-compatible-with-games": _p(name="modified-compatible-with-games", label="Compatible environment with foreign games and saves to preserve", usb=USB_RNDIS, shell="ssh",
                                         versions=dict(PINNED_VERSIONS), modules=["clovercon", "font_remount", "_km_retroarch_1910_ozone"], games=_games(14), backup2=True,
                                         storage={"total": STORAGE_REF["total"], "used_other": 6 * 1024 * 1024, "saves": 30 * 1024 * 1024}),
    "modified-clovershell-only": _p(name="modified-clovershell-only", label="Compatible environment reachable only over clovershell (sync disabled upstream)", usb=USB_CLOVERSHELL, shell="clovershell",
                                    versions=dict(PINNED_VERSIONS), modules=["clovercon"], backup2=True),
    "modified-outdated": _p(name="modified-outdated", label="Older custom kernel (needs reflash per upstream minimums)", usb=USB_RNDIS, shell="ssh",
                            versions={"bootVersion": "1.0.1", "kernelVersion": "3.4.112-00", "hakchiVersion": "v1.0.3-100"}, backup2=True),
    "modified-unknown-kernel": _p(name="modified-unknown-kernel", label="Custom environment without a version file (severely outdated or unknown)", usb=USB_RNDIS, shell="ssh", versions=None, backup2=False),
    "wrong-model-snes": _p(name="wrong-model-snes", label="A Super NES Classic with Hakchi CE", usb=USB_RNDIS, shell="ssh", versions=dict(PINNED_VERSIONS), system_code="snes-usa", hwid=HWID_B),
    "wrong-model-famicom": _p(name="wrong-model-famicom", label="A Famicom Mini with Hakchi CE", usb=USB_RNDIS, shell="ssh", versions=dict(PINNED_VERSIONS), system_code="nes-jpn", hwid=HWID_B),
    "ambiguous-vidpid": _p(name="ambiguous-vidpid", label="Something with 1F3A:EFE8 that is not an NES Classic (wrong FEL board id)", usb=USB_FEL, fel_board=0x00162500, shell=None),
    "md-partitioning": _p(name="md-partitioning", label="Mega Drive style partitioning (unsupported for NES flows)", usb=USB_RNDIS, shell="ssh", versions=dict(PINNED_VERSIONS), md_partitioning=True, system_code="md-usa", hwid=HWID_B),
    "storage-marginal": _p(name="storage-marginal", label="Compatible environment with little free space", usb=USB_RNDIS, shell="ssh", versions=dict(PINNED_VERSIONS),
                           games=_games(60, size=3_460_300), storage={"total": STORAGE_REF["total"], "used_other": 60 * 1024 * 1024, "saves": 40 * 1024 * 1024}, backup2=True),   # about 12 MB free
    "storage-insufficient": _p(name="storage-insufficient", label="Compatible environment with no room left", usb=USB_RNDIS, shell="ssh", versions=dict(PINNED_VERSIONS),
                               games=_games(90, size=2 * 1024 * 1024), storage={"total": STORAGE_REF["total"], "used_other": 90 * 1024 * 1024, "saves": 40 * 1024 * 1024}, backup2=True),
    "backup-missing": _p(name="backup-missing", label="Compatible environment whose stock kernel backup is absent", usb=USB_RNDIS, shell="ssh", versions=dict(PINNED_VERSIONS), backup2=False),
    "disconnect-before-transfer": _p(name="disconnect-before-transfer", label="USB disconnect after preflight, before the transfer", usb=USB_RNDIS, shell="ssh", versions=dict(PINNED_VERSIONS), backup2=True,
                                     fail={"at": "transfer", "kind": "disconnect", "before": True}),
    "disconnect-during-transfer": _p(name="disconnect-during-transfer", label="USB disconnect in the middle of the transfer", usb=USB_RNDIS, shell="ssh", versions=dict(PINNED_VERSIONS), backup2=True,
                                     fail={"at": "transfer", "kind": "disconnect", "fraction": 0.5}),
    "fail-after-modules": _p(name="fail-after-modules", label="Error after the module stage, before the game sync", usb=USB_RNDIS, shell="ssh", versions=dict(PINNED_VERSIONS), backup2=True,
                             fail={"at": "modules", "kind": "error", "after": True}),
    "fail-before-verify": _p(name="fail-before-verify", label="Transfer completes, verification read-back fails", usb=USB_RNDIS, shell="ssh", versions=dict(PINNED_VERSIONS), backup2=True,
                             fail={"at": "verify", "kind": "timeout"}),
    "interrupted-first-time-setup": _p(name="interrupted-first-time-setup", label="First-time setup interrupted (recovery unknown)", usb=USB_FEL, shell=None, versions=None,
                                       fail={"at": "memboot", "kind": "disconnect"}),
}

def get(name: str) -> dict:
    if name not in PROFILES: raise KeyError(f"unknown device profile {name!r}")
    return copy.deepcopy(PROFILES[name])

def names() -> list[str]: return list(PROFILES)

def public(name: str) -> dict:
    """What the UI may show about a profile (no internals)."""
    p = get(name); return {"name": p["name"], "label": p["label"], "simulated": True}
