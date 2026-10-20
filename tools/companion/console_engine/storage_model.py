"""The NES Classic storage model: one calculation for the NES Classic Loadout editor's meter (studio/storage-model.js, a line-by-line
port that test_storage_model.py proves identical) and for the installation plan's storage budget (plan.py). Bytes everywhere;
readable units only for display.

Capacity (before a console is inspected) comes from the reference stock 2018 NES Classic Edition (CLV-101): the free space of its data
partition read from an unmodified console's ext4 superblock, minus the blocks the file system reserves and a reserve for save data.
Everything an installation adds is charged to the loadout: each added game's file (exact bytes, 16-byte header included) and its
menu files, the native HOME menu (measured when the package has been prepared on this computer, otherwise estimated from a measured
package), and the shared components it needs, each counted once: the Hakchi base when the console does not have it yet, RetroArch with
its cores when at least one added game needs it. A safety margin covers file-system overhead (8 MiB plus 5 % of the content).
Unknown game sizes are never counted as zero: the result says the fit cannot be confirmed."""
from __future__ import annotations

MIB = 1024 * 1024
MODEL = {
    "schema": "ccs-storage-model/1",
    # reference console: ext4 superblock of the data partition of an unmodified CLV-101 (catalog.py reads the same dump)
    "reference": {"free_before_mod": round(377.1 * MIB), "root_reserved": round(19.7 * MIB), "saves_reserve": 10 * MIB,
                  "source": "reference stock NES Classic Edition (CLV-101): data partition free space before any change, minus file-system reserved blocks and a save-data reserve"},
    # files each added game puts on the console besides its game file (plan.py writes the same set)
    "per_game": {"desktop": 700, "cover": 40_000, "cover_small": 6_000, "metadata": 180},
    "header": 16,
    # the native HOME menu when no package has been prepared: a measured package (1.3 MB program, themes and fonts; 154 KB of covers,
    # backdrops and texts per game), rounded up
    "home_estimate": {"base": round(1.5 * MIB), "per_game": 160 * 1024},
    # shared components, each counted once (conservative allowances until the modules are obtained and measured)
    "components": {"hakchi": 25 * MIB, "retroarch": 60 * MIB},
    "margin": {"fixed": 8 * MIB, "fraction": 0.05},
}

def capacity(model: dict = MODEL) -> int:
    r = model["reference"]; return r["free_before_mod"] - r["root_reserved"] - r["saves_reserve"]

def estimate(inp: dict, model: dict = MODEL) -> dict:
    """inp: games [{bytes: int|None (the game file on the console), install: bool (False = a built-in game kept as it is), retroarch: bool}],
    home {measured: int|None, games: int (the games shown on the console)}, hakchi_present: bool, retroarch_present: bool, free: int|None (measured
    or simulated free space; None = the reference capacity)."""
    pg = model["per_game"]; per = pg["desktop"] + pg["cover"] + pg["cover_small"] + pg["metadata"]
    rom = 0; files = 0; added = 0; unknown = 0; ra = False
    for g in inp.get("games") or []:
        if not g.get("install"): continue
        added += 1; files += per
        b = g.get("bytes")
        if isinstance(b, int) and b > 0: rom += b
        else: unknown += 1
        if g.get("retroarch"): ra = True
    h = inp.get("home") or {}; he = model["home_estimate"]
    home_measured = isinstance(h.get("measured"), int) and h["measured"] > 0
    home = h["measured"] if home_measured else he["base"] + he["per_game"] * int(h.get("games") or 0)
    comp = {"hakchi": 0 if inp.get("hakchi_present") else model["components"]["hakchi"], "retroarch": model["components"]["retroarch"] if ra and not inp.get("retroarch_present") else 0}
    content = rom + files + home + comp["hakchi"] + comp["retroarch"]
    margin = model["margin"]["fixed"] + int(model["margin"]["fraction"] * content)
    used = content + margin
    free = inp.get("free"); available = free if isinstance(free, int) else capacity(model)
    remaining = available - used
    status = "over" if remaining < 0 else "unconfirmed" if unknown else "near" if remaining < margin else "fits"
    return {"rom": rom, "files": files, "added": added, "unknown": unknown, "home": home, "home_measured": home_measured, "components": comp,
            "content": content, "margin": margin, "used": used, "available": available, "remaining": remaining, "status": status,
            "source": "reference" if not isinstance(free, int) else "device"}
