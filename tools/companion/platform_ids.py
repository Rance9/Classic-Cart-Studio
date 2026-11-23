r"""The platform registry and its one decision rule, for the Windows app's backend.

Independent identifiers say what a file or a request is for: game system, core, console model, console software, installation method and
storage layout, plus controllers, boot options, save formats, drivers and capabilities. An entry is "supported" (this version may use it;
every gate and approval still applies) or "reserved" (named, and denied everywhere).
  resolve_target(doc, doc_kind, *entry_lists)  a document's optional ccs_target (ccs-target/1). Absent: the document kind's NES defaults
                     (files written before the field existed are NES, unchanged). Present: its schema, fields and every identifier must
                     be known and supported. Items of the document's entry lists may each carry game_system: it must be supported too.
  check_request(req) a worker request: any identifier it names (directly, in ccs_target, or as a provider {kind, id}) must be supported;
                     a request that names none is the NES default, as every request was before.
  model_for(board, region)  the supported console model a board and region answer with (a model that lists regions needs one of them),
                     else None; model_for_board(board) is the same without a region.
The registry text below is tools/platform/ccs-platform-registry.json byte for byte (test_platform_ids.py checks it); the C# engine embeds
the same file and the website's platform-ids.js is built from it, and all three decide tools/platform/target-vectors.json the same way.
Nothing read from a file, a request, a URL, a setting or the environment can add an entry or change a status: the registry is read once,
and every value handed out is a copy. Part of Classic Cart Studio: GNU GPL v3 or later."""
from __future__ import annotations
import copy, hashlib, json

REGISTRY_TEXT = r'''{
  "schema": "ccs-platform-registry/1",
  "about": "The identifiers Classic Cart Studio files and programs use to say which game system, core, console model, console software, installation method, storage layout, controller, boot option, save format, driver and capability something is for. Each axis is independent: a console model is not its software, its software is not a game system, and a game system is not a core. status 'supported' means this version may use the entry (every other check, gate and approval still applies); status 'reserved' means the name exists so files and code can refer to it, and it is denied everywhere: in the website and the Windows app, in the shared engine, in the workers and before any console program. Nothing read from a file, a URL, a request or a setting can change an entry's status; only a reviewed release can. GPL-3.0-or-later.",
  "target": {
    "key": "ccs_target",
    "schema": "ccs-target/1",
    "fields": {
      "game_system": "game-system",
      "core": "core",
      "physical_model": "physical-model",
      "firmware": "firmware",
      "method": "method",
      "layout": "layout"
    },
    "entry_key": "game_system"
  },
  "document_defaults": {
    "collection": {"game_system": "nes", "core": "fceumm"},
    "ccs-package": {"game_system": "nes", "core": "fceumm"},
    "game-saves": {"game_system": "nes", "core": "fceumm"},
    "console-package": {"game_system": "nes", "core": "fceumm", "physical_model": "nes-classic-edition", "method": "ccs-home-autostart"},
    "console-backup": {"physical_model": "nes-classic-edition", "firmware": "nintendo-nes-classic", "layout": "nes-classic-nand-512m"},
    "stock-save": {"game_system": "nes", "physical_model": "nes-classic-edition", "firmware": "nintendo-nes-classic"},
    "worker-request": {"game_system": "nes", "physical_model": "nes-classic-edition"}
  },
  "entries": [
    {"kind": "game-system", "id": "nes", "status": "supported", "input": "nes-pad", "cores": ["fceumm"]},
    {"kind": "game-system", "id": "snes", "status": "reserved", "input": "snes-pad", "cores": [], "reason": "No core is selected, licensed, built or qualified for this system."},

    {"kind": "core", "id": "fceumm", "status": "supported", "game_system": "nes", "bundled": true, "license": "GPL-2.0-or-later",
     "upstream": "https://github.com/libretro/libretro-fceumm", "pin": "ac0d00a7fc01166f84c7bac01d1084ebfd2b865e", "build": "ac0d00a7+patches.6dcff395",
     "source": "tools/player/third_party/fceumm", "notices": ["tools/player/third_party/fceumm/VENDORED.md", "THIRD_PARTY_NOTICES.md"]},
    {"kind": "core", "id": "snes-unselected", "status": "reserved", "game_system": "snes", "bundled": false, "license": null, "upstream": null, "pin": null,
     "source": null, "notices": [], "reason": "A placeholder for a core that has not been chosen. Nothing is bundled for it."},

    {"kind": "physical-model", "id": "nes-classic-edition", "status": "supported", "boards": ["dp-nes"], "layouts": ["nes-classic-nand-512m"]},
    {"kind": "physical-model", "id": "snes-classic-edition", "status": "supported", "boards": ["dp-shvc"], "regions": ["USA", "EUR"], "layouts": ["snes-classic-nand-512m"],
     "note": "The Super NES Classic Edition of North America and Europe (board dp-shvc with REGION USA or EUR; the Super Famicom Classic Mini, REGION JPN, is not this model). The console hardware only: its games still need a core this version does not have. Each operation it allows is listed as its own capability."},

    {"kind": "firmware", "id": "nintendo-nes-classic", "status": "supported", "physical_model": "nes-classic-edition",
     "note": "The console's own software, as Nintendo shipped it or with hakchi added."},
    {"kind": "firmware", "id": "nintendo-snes-classic", "status": "supported", "physical_model": "snes-classic-edition",
     "note": "The console's own software on Super NES Classic hardware, as Nintendo shipped it or with hakchi added. A copy of it is a console software environment, never a Classic Cart Studio core, on any hardware."},

    {"kind": "method", "id": "ccs-home-autostart", "status": "supported", "physical_model": "nes-classic-edition",
     "note": "Classic Cart Studio HOME started by the console's startup under the pinned hakchi (the current installation)."},

    {"kind": "layout", "id": "nes-classic-nand-512m", "status": "supported", "physical_model": "nes-classic-edition", "nandinfo": "2048 64 4096",
     "labels": ["boot", "rootfs", "data", "private", "UDISK", "rootfs_data"]},
    {"kind": "layout", "id": "snes-classic-nand-512m", "status": "supported", "physical_model": "snes-classic-edition", "nandinfo": "2048 64 4096",
     "labels": ["boot", "rootfs", "data", "private", "UDISK", "rootfs_data"],
     "note": "536,870,912 bytes of NAND (the console's own nandinfo reads 2048 64 4096); five parts, its data storage an ext4 file system named data. The table each console reports is checked against its running system before anything is read."},
    {"kind": "layout", "id": "snes-classic-unproven", "status": "reserved", "physical_model": "snes-classic-edition",
     "reason": "A name kept for files that referred to it; the supported layout is snes-classic-nand-512m."},

    {"kind": "input", "id": "nes-pad", "status": "supported", "game_system": "nes", "players": 2,
     "actions": ["up", "down", "left", "right", "a", "b", "select", "start"]},
    {"kind": "input", "id": "snes-pad", "status": "reserved", "game_system": "snes", "players": 2,
     "actions": ["up", "down", "left", "right", "a", "b", "x", "y", "l", "r", "select", "start"]},

    {"kind": "boot-option", "id": "ccs-home", "status": "supported", "game_system": "nes", "visible": true},
    {"kind": "boot-option", "id": "native-menu", "status": "supported", "visible": true},
    {"kind": "boot-option", "id": "ccs-snes", "status": "reserved", "game_system": "snes", "visible": false,
     "reason": "Never shown or installed: there is no supported provider and no verified payload."},

    {"kind": "save-adapter", "id": "ccs-fceumm-battery", "status": "supported", "game_system": "nes", "core": "fceumm",
     "note": "cart.sav: the core's battery memory, exact bytes, no header."},
    {"kind": "save-adapter", "id": "nes-classic-stock-battery", "status": "supported", "game_system": "nes", "firmware": "nintendo-nes-classic",
     "note": "Inspection, and conversion only for the exact games and sizes this version recognizes."},
    {"kind": "save-adapter", "id": "snes-classic-stock-battery", "status": "reserved", "game_system": "snes", "firmware": "nintendo-snes-classic",
     "reason": "Its files are kept and exported as they are; nothing converts them."},

    {"kind": "driver", "id": "clovershell-usb", "status": "supported", "physical_models": ["nes-classic-edition", "snes-classic-edition"], "gate": "ConsoleSessions"},

    {"kind": "capability", "id": "nes-gameplay", "status": "supported", "game_system": "nes", "core": "fceumm"},
    {"kind": "capability", "id": "snes-gameplay", "status": "reserved", "game_system": "snes"},
    {"kind": "capability", "id": "nes-classic-backup", "status": "supported", "physical_model": "nes-classic-edition", "gate": "ConsoleSessions"},
    {"kind": "capability", "id": "nes-classic-restore", "status": "supported", "physical_model": "nes-classic-edition", "gate": "RestoreWrites"},
    {"kind": "capability", "id": "nes-classic-preparation", "status": "supported", "physical_model": "nes-classic-edition", "gate": "PreparationWrites"},
    {"kind": "capability", "id": "nes-classic-return", "status": "supported", "physical_model": "nes-classic-edition", "gate": "ReturnWrites"},
    {"kind": "capability", "id": "snes-classic-identify", "status": "supported", "physical_model": "snes-classic-edition", "gate": "ConsoleSessions",
     "note": "Reading the console's identity, startup software and storage table, and the check-up (Console Browser), read only."},
    {"kind": "capability", "id": "snes-classic-backup", "status": "supported", "physical_model": "snes-classic-edition", "gate": "ConsoleSessions"},
    {"kind": "capability", "id": "snes-classic-browse", "status": "supported", "physical_model": "snes-classic-edition",
     "note": "Opening, checking, exporting from and repairing a backup file of this console on the PC; no console is needed."},
    {"kind": "capability", "id": "snes-classic-restore", "status": "supported", "physical_model": "snes-classic-edition", "gate": "RestoreWrites"},
    {"kind": "capability", "id": "snes-classic-preparation", "status": "reserved", "physical_model": "snes-classic-edition",
     "reason": "Preparing this console with hakchi needs hakchi's kernel built with this model's own system key and an audit of its startup and storage-table changes on this model."},
    {"kind": "capability", "id": "snes-classic-return", "status": "reserved", "physical_model": "snes-classic-edition",
     "reason": "Returning this console to its original software needs its preparation (above) and this model's own replacement-bootloader evidence."},
    {"kind": "capability", "id": "snes-classic-rescue", "status": "reserved", "physical_model": "snes-classic-edition",
     "reason": "Repairing this console's startup stages needs its own boot0 and boot1 references read from a genuine console of this model."},
    {"kind": "capability", "id": "snes-classic-files", "status": "reserved", "physical_model": "snes-classic-edition",
     "reason": "Changing files on this console (Expert Mode) needs this model's file scopes, which this version does not define yet."},
    {"kind": "capability", "id": "snes-classic-developer", "status": "reserved", "physical_model": "snes-classic-edition",
     "reason": "Test boots and startup or system changes need this model's system key and root settings established on its own startup software."},
    {"kind": "capability", "id": "snes-classic-composition", "status": "reserved", "physical_model": "snes-classic-edition",
     "reason": "Moving data between consoles needs two consoles of this model and its own data plan."},
    {"kind": "capability", "id": "snes-classic-addons", "status": "reserved", "physical_model": "snes-classic-edition",
     "reason": "CCS Mod and CCS Nucleus on this console need its preparation (above) and its own menu and game paths."},
    {"kind": "capability", "id": "snes-classic-firmware", "status": "reserved", "physical_model": "snes-classic-edition",
     "reason": "Adding system images to this console's startup chooser needs CCS Nucleus on it (above)."},
    {"kind": "capability", "id": "snes-classic-community", "status": "reserved", "physical_model": "snes-classic-edition",
     "reason": "Community mods on this console need CCS Nucleus on it and each mod's own approval for this model."},
    {"kind": "capability", "id": "nes-stock-save-conversion", "status": "supported", "game_system": "nes"},
    {"kind": "capability", "id": "snes-stock-save-conversion", "status": "reserved", "game_system": "snes"}
  ]
}
'''
REGISTRY_SCHEMA = "ccs-platform-registry/1"; TARGET_SCHEMA = "ccs-target/1"; TARGET_KEY = "ccs_target"; ENTRY_KEY = "game_system"
DOCUMENT_TEXT = "This file is for a game system or console that this version of Classic Cart Studio does not support, so it was not used. Nothing was changed."
REQUEST_TEXT = "This request names a game system, console or feature that this version does not support. Nothing was done."
FIELDS = (("game_system", "game-system"), ("core", "core"), ("physical_model", "physical-model"), ("firmware", "firmware"), ("method", "method"), ("layout", "layout"))
REQUEST_KEYS = (("capability", "capability"), ("boot_option", "boot-option"), ("input", "input"), ("save_adapter", "save-adapter"), ("driver", "driver"))

_REG = json.loads(REGISTRY_TEXT)
if _REG.get("schema") != REGISTRY_SCHEMA: raise RuntimeError("the platform registry is not " + REGISTRY_SCHEMA)
_ENTRIES = {(e["kind"], e["id"]): e for e in _REG["entries"]}
_DEFAULTS = {k: {f: v for f, v in d.items() if isinstance(v, str)} for k, d in _REG["document_defaults"].items()}
_FIELD_NAMES = {f for f, _ in FIELDS}

def registry_sha256() -> str: return hashlib.sha256(REGISTRY_TEXT.encode("utf-8")).hexdigest()
def entries() -> list: return copy.deepcopy(list(_REG["entries"]))
def entry(kind: str, id_) -> dict | None:
    e = _ENTRIES.get((kind, id_)) if isinstance(kind, str) and isinstance(id_, str) else None
    return copy.deepcopy(e) if e else None
def allowed(kind: str, id_) -> bool:
    e = _ENTRIES.get((kind, id_)) if isinstance(kind, str) and isinstance(id_, str) else None
    return bool(e) and e.get("status") == "supported"
def supported_ids(kind: str) -> list: return [e["id"] for e in _REG["entries"] if e["kind"] == kind and e.get("status") == "supported"]
def input_actions(id_: str) -> list: return list((_ENTRIES.get(("input", id_)) or {}).get("actions") or [])
def model_for_board(board) -> str | None:
    """the supported model a board answers with; never a model told apart by its region (model_for)"""
    return model_for(board, None)
def model_for(board, region) -> str | None:
    """the supported model a board and region answer with: its boards include the board and, when it lists regions, they include the region"""
    if not isinstance(board, str) or not board: return None
    return next((e["id"] for e in _REG["entries"] if e["kind"] == "physical-model" and e.get("status") == "supported" and board in (e.get("boards") or [])
                 and (not e.get("regions") or (isinstance(region, str) and region in e["regions"]))), None)
def defaults(doc_kind: str) -> dict:
    if doc_kind not in _DEFAULTS: raise ValueError(f"no defaults for document kind {doc_kind!r}")
    return dict(_DEFAULTS[doc_kind])

def _deny(d: dict, code: str, field=None, value=None, text: str = DOCUMENT_TEXT) -> dict:
    d.update(ok=False, code=code, field=field, value=value, text=text); return d

def _nonempty(v) -> bool: return isinstance(v, str) and len(v) > 0

def resolve_target(doc, doc_kind: str, *entry_lists) -> dict:
    """{ok, code, text, field, value, legacy, target} for one document. code: ok | damaged | target-schema | unknown-field | unknown-id |
    reserved | inconsistent. Nothing is assumed to be NES except a document with no ccs_target at all."""
    if doc_kind not in _DEFAULTS: raise ValueError(f"no defaults for document kind {doc_kind!r}")
    d = {"ok": False, "code": None, "text": None, "field": None, "value": None, "legacy": False, "target": dict(_DEFAULTS[doc_kind])}
    if not isinstance(doc, dict): return _deny(d, "damaged")
    if TARGET_KEY not in doc: d["legacy"] = True
    else:
        t = doc[TARGET_KEY]
        if not isinstance(t, dict): return _deny(d, "damaged", TARGET_KEY)
        if t.get("schema") != TARGET_SCHEMA or not isinstance(t.get("schema"), str): return _deny(d, "target-schema", "schema", t.get("schema") if isinstance(t.get("schema"), str) else None)
        for k in t:
            if k != "schema" and k not in _FIELD_NAMES: return _deny(d, "unknown-field", k)
        for f, _ in FIELDS:
            if f not in t: continue
            if not _nonempty(t[f]): return _deny(d, "damaged", f)
            d["target"][f] = t[f]
    for f, kind in FIELDS:
        if f not in d["target"]: continue
        e = _ENTRIES.get((kind, d["target"][f]))
        if e is None: return _deny(d, "unknown-id", f, d["target"][f])
        if e.get("status") != "supported": return _deny(d, "reserved", f, d["target"][f])
    tg = d["target"]
    if "core" in tg and "game_system" in tg and _ENTRIES[("core", tg["core"])].get("game_system") != tg["game_system"]: return _deny(d, "inconsistent", "core", tg["core"])
    if "physical_model" in tg:
        for f in ("firmware", "method", "layout"):
            if f in tg and _ENTRIES[(f, tg[f])].get("physical_model") != tg["physical_model"]: return _deny(d, "inconsistent", f, tg[f])
    for lst in entry_lists:
        if not isinstance(lst, list): continue
        for it in lst:
            if not isinstance(it, dict) or ENTRY_KEY not in it: continue
            v = it[ENTRY_KEY]
            if not _nonempty(v): return _deny(d, "damaged", ENTRY_KEY)
            e = _ENTRIES.get(("game-system", v))
            if e is None: return _deny(d, "unknown-id", ENTRY_KEY, v)
            if e.get("status") != "supported": return _deny(d, "reserved", ENTRY_KEY, v)
            if "game_system" in tg and tg["game_system"] != v: return _deny(d, "inconsistent", ENTRY_KEY, v)
    d.update(ok=True, code="ok"); return d

def check_request(req) -> dict:
    """the decision for one worker request's arguments (see the module text)"""
    if not isinstance(req, dict): return _deny({"legacy": False, "target": {}}, "damaged", text=REQUEST_TEXT)
    r = resolve_target({TARGET_KEY: req[TARGET_KEY]} if TARGET_KEY in req else {}, "worker-request")
    if not r["ok"]: r["text"] = REQUEST_TEXT; return r
    for f, kind in FIELDS + REQUEST_KEYS:
        if f not in req: continue
        v = req[f]
        if not _nonempty(v): return _deny(r, "damaged", f, text=REQUEST_TEXT)
        e = _ENTRIES.get((kind, v))
        if e is None: return _deny(r, "unknown-id", f, v, REQUEST_TEXT)
        if e.get("status") != "supported": return _deny(r, "reserved", f, v, REQUEST_TEXT)
    if "provider" in req:
        p = req["provider"]
        if not isinstance(p, dict) or not _nonempty(p.get("kind")) or not _nonempty(p.get("id")): return _deny(r, "damaged", "provider", text=REQUEST_TEXT)
        e = _ENTRIES.get((p["kind"], p["id"]))
        if e is None: return _deny(r, "unknown-id", "provider", f"{p['kind']}/{p['id']}", REQUEST_TEXT)
        if e.get("status") != "supported": return _deny(r, "reserved", "provider", f"{p['kind']}/{p['id']}", REQUEST_TEXT)
    r.update(ok=True, code="ok", text=None); return r
