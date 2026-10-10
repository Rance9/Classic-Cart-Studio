"""Controller / accessory requirements for catalog games (PC-side metadata; separate from emulator compatibility).

Evidence sources, in priority order:
 1. Curated overrides (tools/rom-pipeline/data/input-overrides.json) — researched per title from published accessory lists
    (Wikipedia NES Zapper / Power Pad / Four Score lists, NESdev input-device pages, manuals); each entry carries its URLs.
 2. The Mesen 2 / NewRisingSun NES 2.0 database "default expansion device" code recorded for the ROM (C:/NES/work/db/
    MesenNesDB.txt column 14, keyed by PRG+CHR CRC32). Codes follow the NES 2.0 header specification
    (https://www.nesdev.org/wiki/NES_2.0#Default_Expansion_Device).
 3. Otherwise: unknown (never assumed playable).

The classification never changes emulator routing or ROM identity. "nes_classic_support" describes the 2018 NES Classic
Edition with its two standard controllers only; anything needing other hardware is "unsupported" unless a verified
alternative exists for this console (none verified as of 2026-10-09 — see docs/controller-compatibility.md).
"""
from __future__ import annotations
import os, json, re, unicodedata

NESDEV_URL = "https://www.nesdev.org/wiki/NES_2.0#Default_Expansion_Device"
# NES 2.0 default expansion device codes (decimal) -> (device, family)
EXPANSION = {
    0: ("unspecified", "unknown"), 1: ("standard controllers", "standard"), 2: ("NES Four Score / Satellite", "four-score"), 3: ("Famicom Four Players Adapter", "four-score"),
    4: ("Vs. System", "arcade"), 5: ("Vs. System (reversed inputs)", "arcade"), 6: ("Vs. Pinball", "arcade"), 7: ("Vs. Zapper", "arcade"),
    8: ("NES Zapper", "light-gun"), 9: ("two Zappers", "light-gun"), 10: ("Bandai Hyper Shot light gun", "light-gun"),
    11: ("Power Pad (side A)", "power-pad"), 12: ("Power Pad (side B)", "power-pad"), 13: ("Family Trainer (side A)", "power-pad"), 14: ("Family Trainer (side B)", "power-pad"),
    15: ("Arkanoid Vaus controller (NES)", "vaus"), 16: ("Arkanoid Vaus controller (Famicom)", "vaus"), 17: ("two Vaus + data recorder", "vaus"),
    18: ("Konami Hyper Shot", "other"), 19: ("Coconuts Pachinko controller", "other"), 20: ("Exciting Boxing punching bag", "other"), 21: ("Jissen Mahjong controller", "other"),
    22: ("Party Tap", "other"), 23: ("Oeka Kids tablet", "other"), 24: ("Sunsoft Barcode Battler", "other"), 25: ("Miracle Piano Teaching System", "piano"),
    26: ("Pokkun Moguraa", "other"), 27: ("Top Rider", "other"), 28: ("Double-Fisted (two controllers, one player)", "standard"), 29: ("Famicom 3D System", "other"),
    30: ("Doremikko keyboard", "other"), 31: ("R.O.B. Gyro Set", "rob"), 32: ("Famicom Data Recorder", "other"), 33: ("ASCII Turbo File", "other"), 34: ("IGS Storage Battle Box", "other"),
    35: ("Family BASIC keyboard", "other"), 36: ("Dongda PEC-586 keyboard", "other"), 37: ("Bit Corp. Bit-79 keyboard", "other"), 38: ("Subor keyboard", "other"),
    39: ("Subor keyboard + mouse", "other"), 40: ("Subor keyboard + mouse (24-bit)", "other"), 41: ("SNES mouse", "other"), 42: ("multicart (unspecified)", "unknown"),
    43: ("two SNES controllers", "other"), 44: ("RacerMate bicycle", "other"), 45: ("U-Force", "other"), 46: ("R.O.B. Stack-Up", "rob"),
    47: ("City Patrolman light gun", "light-gun"), 48: ("Sharp C1 cassette", "other"), 49: ("standard controller with swapped L/R", "standard"),
    50: ("Excalibor Sudoku pad", "other"), 51: ("ABL Pinball", "other"), 52: ("Golden Nugget Casino extra buttons", "other"),
}
FAMILY_LABEL = {"light-gun": "light gun", "power-pad": "Power Pad", "four-score": "NES Four Score (3–4 players)", "vaus": "Arkanoid Vaus controller", "rob": "R.O.B.", "piano": "Miracle Piano keyboard", "other": "special accessory", "arcade": "arcade cabinet inputs"}
# NES Classic support per family when the accessory is REQUIRED (no verified alternatives on this console as of 2026-10-09)
REQUIRED_SUPPORT = {"light-gun": "unverified", "power-pad": "unsupported", "rob": "unsupported", "piano": "unsupported", "vaus": "unsupported", "other": "unsupported", "arcade": "unsupported", "four-score": "partial"}

OVERRIDES_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "data", "input-overrides.json")

def norm(t: str) -> str:
    t = unicodedata.normalize("NFKD", t or "").encode("ascii", "ignore").decode().lower()
    t = re.sub(r"\(.*?\)", " ", t); t = re.sub(r"[^a-z0-9]+", " ", t)
    w = t.split()
    if w and w[0] == "the": w = w[1:]
    if w and w[-1] == "the": w = w[:-1]
    return " ".join(w)

def load_overrides() -> dict:
    if not os.path.exists(OVERRIDES_PATH): return {}
    out = {}
    for e in json.load(open(OVERRIDES_PATH, encoding="utf-8")):
        out[norm(e["title"])] = e
        for a in e.get("aliases", []): out[norm(a)] = e
    return out

def classify(row: dict, mesen_code, overrides: dict) -> dict:
    """row: catalog row (display_title, players, nointro_name). mesen_code: int or None. Returns the input_* fields."""
    players = (row.get("players") or "").strip()
    base = {"input_primary": "standard-controller", "input_modes": ["standard-controller"], "input_min_controllers": 1, "input_special_accessory": "", "input_accessory_requirement": "none",
            "input_standard_controller_playable": "true", "input_nes_classic_support": "supported", "input_evidence": [], "input_level": "none", "input_notes": ""}
    ov = overrides.get(norm(row.get("display_title") or row.get("title") or ""))
    if ov:
        req = ov.get("requirement", "required"); fam = ov.get("family", "other"); acc = ov.get("accessory", FAMILY_LABEL.get(fam, "special accessory"))
        scp = ov.get("standard_controller_playable", "unknown"); scp = {True: "true", False: "false"}.get(scp, str(scp)).lower()
        support = ov.get("nes_classic_support") or ("supported" if scp == "true" else "partial" if scp == "partial" else REQUIRED_SUPPORT.get(fam, "unsupported") if scp == "false" else "unknown")
        modes = ["standard-controller"] if scp in ("true", "partial") else []
        modes.append(fam)
        base.update(input_primary=(fam if req == "required" else "standard-controller"), input_modes=modes, input_special_accessory=acc, input_accessory_requirement=req, input_standard_controller_playable=scp,
                    input_nes_classic_support=support, input_evidence=[{"source": "curated accessory list", "url": u} for u in ov.get("sources", [])], input_notes=ov.get("notes", ""),
                    input_min_controllers=int(ov.get("min_controllers", 1)))
        base["input_level"] = "required" if (req == "required" and scp == "false") else "partial" if (req == "mode-specific" or scp == "partial") else "optional" if req == "optional" else "info"
        return base
    code = mesen_code if isinstance(mesen_code, int) else None
    if code is None:
        base.update(input_nes_classic_support="unknown", input_standard_controller_playable="unknown", input_level="unknown", input_notes="no input-device record for this ROM in the NES 2.0 database and no curated entry")
        return base
    dev, fam = EXPANSION.get(code, (f"expansion device code {code}", "other"))
    ev = [{"source": "Mesen/NES 2.0 database default expansion device", "code": code, "device": dev, "url": NESDEV_URL}]
    if fam == "standard":
        base.update(input_evidence=ev); return base
    if fam == "unknown":
        base.update(input_evidence=ev, input_nes_classic_support="unknown", input_standard_controller_playable="unknown", input_level="unknown", input_notes=f"NES 2.0 database records '{dev}'"); return base
    if code in (32, 33, 34):   # Famicom data recorder / Turbo File / Battle Box: save-data peripherals, never needed to play
        base.update(input_evidence=ev, input_special_accessory=dev, input_accessory_requirement="optional", input_level="optional", input_notes=f"NES 2.0 database lists '{dev}' as an optional save/data peripheral; the game plays with a standard controller"); return base
    if fam == "four-score":
        base.update(input_primary="standard-controller", input_modes=["standard-controller", "four-score"], input_special_accessory="NES Four Score / Satellite", input_accessory_requirement="mode-specific",
                    input_standard_controller_playable="partial", input_nes_classic_support="partial", input_evidence=ev, input_level="partial", input_notes="3–4 player modes need a Four Score adapter (no NES Classic equivalent); 1–2 player modes use standard controllers")
        return base
    # accessory recorded as the default device: treat as required unless curated otherwise
    base.update(input_primary=fam, input_modes=[fam], input_special_accessory=dev, input_accessory_requirement="required", input_standard_controller_playable="false",
                input_nes_classic_support=REQUIRED_SUPPORT.get(fam, "unsupported"), input_evidence=ev, input_level="required", input_notes=f"NES 2.0 database default input device: {dev}")
    return base

INPUT_COLUMNS = ["input_primary", "input_modes", "input_min_controllers", "input_special_accessory", "input_accessory_requirement", "input_standard_controller_playable", "input_nes_classic_support", "input_evidence", "input_level", "input_notes"]
