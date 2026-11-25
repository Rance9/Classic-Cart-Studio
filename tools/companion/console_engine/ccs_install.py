"""Installing Classic Cart Studio's HOME on a console that runs hakchi (K3). Tested here against disposable stand-ins; a real console is
reached only through the console transport worker (worker_console), in a session opened for one tier (inspect or install, Gate G1 in code:
both open in this version), and the installer never rolls a real console back by itself (docs/console-install-architecture.md sections 13
and 14). The consoles: the North American NES Classic Edition and the North American and European Super NES Classic Edition, each told
apart by its own software's board and region (check_device); a change on any model but the NES Classic also needs the shared platform
registry's capability for that model's add-ons (model_refusal).

Where (all under /var/lib/ccs; nothing else on the console is written except through hakchi's own module commands):
  pkg/<version>.<attempt>/   one immutable copy of a console package per installation attempt (version = the first 16 hex of the SHA-256 of
                             its MANIFEST.json, attempt = 8 hex): a repair or an update never changes the copy in use
  pkg/current                a small file naming the copy in use ("<version>.<attempt>"), replaced in one rename. A file, not a link: the
                             console's BusyBox 1.24.1 has no "mv -T", and "mv" onto a link to a folder moves into that folder
  install/                   this program's records on the console, each written before what it records: v-<copy>.json (the files of a
                             copy and their SHA-256), module-<sha16>.json (an add-on package this program installed, by its SHA-256; or an
                             earlier version's start-up module), attempt-<attempt>.json (an attempt in progress, with what it found first)
  owned/, nucleus/           the add-on packages' own records and settings (their scripts keep them; this program only reads them, and
                             changes CCS Nucleus's start only through CCS Nucleus's own command)
  data/                      the console's own saves, Game Settings and HOME choices (kept across updates; kept by uninstall unless removal
                             is asked for), with IDENTITY.json naming the player build that made them
  integration/               the collection as ordinary Hakchi games (hakchi_publish.py), when the plan asks for it: the pinned console program
                             that writes them (program-<sha16>/, recorded by install/collections-<sha16>.json), the publication staged for it
                             (stage/<attempt>/, removed once applied) and the CCS Collection Interface v1 index (v1/index.json, the program's)
  games/<version>.<attempt>/ without CCS Mod: only the game files the Hakchi games launch and their license notices (games_only.py), recorded
                             by install/games-<copy>.json; never a HOME (no pointer, player, session, themes or pictures). A plan without CCS Mod
                             installs nothing of CCS HOME and changes nothing under pkg/; one with CCS Mod writes the Hakchi entries again
                             naming CCS HOME's copy, after which the games copies are removed (one copy of each game on the console)
Hakchi games (hakchi_publish.py): with the plan's hakchi_games (Studio's default: on when CCS Mod is not chosen; without CCS Mod they are how
the collection reaches the console, so a plan with neither is refused) the collection's games are also
added to Original Menu as ordinary Hakchi game entries in Hakchi folders, after the installation is verified, by the console program
ccs-collections (tools/console-nucleus/console/collections, pinned here and by the transport worker): it writes only into the place hakchi's
own findGameStorage names, only entries it records in its own journal (/var/lib/ccs/owned/published.own), never over another entry, and
replaces the index in one rename. Their game files are the installed copy's own, CCS HOME's or the games copy (no second copy unless Hakchi
itself would store a different file). A later plan without Hakchi games removes the ones this program added; an uninstall removes them first.
Add-ons (tools/console-nucleus): three independent hakchi packages. CCS Mod ("ccs-mod") is the only provider of CCS HOME: a CCS HOME tile in
Original Menu (Nintendo's own menu), with no power-on action. CCS Nucleus ("ccs-nucleus") decides what opens at power-on (Original Menu or
CCS HOME, started directly); its startup chooser ("ccs-nucleus-chooser", which needs CCS Nucleus) adds a screen at start. CCS Nucleus keeps
its start in its own sealed registry (/var/lib/ccs/nucleus/registry), which this program reads and changes only with CCS Nucleus's own
command ("ccs-nucleus start DEFAULT CHOOSER HOME SELECTION TIMEOUT", the worker's nucleus-start). A plan names the add-ons the console
should have afterwards ({mod, nucleus, start, auto}); the installer adds the ones missing, never removes one (that is the CCS Console
Workbench's Console Restore), and sets CCS Nucleus's start when it differs. Each package is the same bytes the Workbench's preparation image
carries (tools/console-nucleus/build_packages.py; pinned there and by the transport worker), installed by hakchi's own pack_install: its
install script checks that every file it would copy is free or its own, hakchi copies them into its overlay (/var/lib/hakchi/rootfs) and
keeps its uninstall list (/var/lib/hakchi/hmod/uninstall-<name>), and the script records its files in its own journal (/var/lib/ccs/owned)
and adds its menu entry. hakchi's pack_uninstall runs the package's own removal (only its unchanged files). CCS HOME starts the copy
pkg/current names. Earlier versions of this program installed a module "ccs-home" whose one file, an overlay /etc/init.d/S81clover-mcp,
hakchi's own script/removed deletes at every start (p7070_firmware -> mod_repair_etc): that module is retired (hakchi's pack_uninstall of its
list) once the new installation is verified, and only when it is this program's by its records.

Ownership (P3-F2): something is this program's only when a record in install/ names it AND its bytes still match that record. A link, a
familiar name or a pointer is never proof. Unknown, changed or ambiguous things are kept and reported, or the operation is refused before
any change; a copy is removed file by file (only its recorded, unchanged files), its folders only when empty.
Transactions (P3-F3): every attempt has its own id, journal (created exclusively, never reused) and on-console attempt record. Before each
change the journal records the intent and what was there before; undoing restores those states whether or not the change completed (a
call can change the console and still fail), then reads them back: "rolled back" means verified, otherwise the result says what is
unknown and that nothing more is written. Recovery of an interrupted attempt is bound to that attempt, its plan and its console; a finished
attempt is never undone. A stand-in rolls back automatically; a real console never does (recovery is its own approved operation, G3).
Copyright (C) 2026 Rance9, GPL-3.0-or-later."""
from __future__ import annotations
import base64, hashlib, json, os, re, secrets, shutil, time
from . import gate, hakchi_publish as HP, games_only as GO, storage_model as SM

PLAN_SCHEMA = "ccs-home-install-plan/5"
ROOT_DIR = "/var/lib/ccs"; PKG_DIR = ROOT_DIR + "/pkg"; DATA_DIR = ROOT_DIR + "/data"; INST_DIR = ROOT_DIR + "/install"; POINTER = PKG_DIR + "/current"
BIN_DIR = ROOT_DIR + "/bin"   # p3a1's launcher place: never written now (a file there is someone else's and is kept)
# the add-ons, by their tools/console-nucleus/build_packages.py name, in the order they are installed (CCS Nucleus before its chooser)
ADDONS = {"mod": "ccs-mod", "nucleus": "ccs-nucleus", "nucleus-chooser": "ccs-nucleus-chooser"}
ADDON_LABELS = {"mod": "CCS Mod", "nucleus": "CCS Nucleus", "nucleus-chooser": "CCS Nucleus's startup chooser"}
# each add-on's program in hakchi's overlay: there without its package listed by hakchi, it is not this program's (refused, nothing changed)
ADDON_PROGRAMS = {"mod": "/bin/ccs-mod", "nucleus": "/bin/ccs-nucleus", "nucleus-chooser": "/etc/ccs/nucleus/chooser/bin/ccs-home-armv7"}
ADDON_RECORDS = ("owned", "nucleus")   # what the add-on packages' own scripts keep in /var/lib/ccs (read, never written here)
KNOWN_ROOT = ("pkg", "data", "install", "integration", "games") + ADDON_RECORDS   # the folders of /var/lib/ccs this program knows (anything else: kept, reported)
GAMES_DIR = GO.GAMES_DIR   # without CCS Mod: the game files the Hakchi entries need, one copy per attempt (games_only.py); never a HOME
COLLECTIONS_RECORD = "ccs-owned-collections/1"   # install/collections-<sha16>.json: the Hakchi-games console program's files this program put there
# the add-on choice (the CCS Console Workbench's AddonChoice, CcsBackup/Engine/MethodEngine.cs): CCS Mod or not; CCS Nucleus none, direct
# (what opens at power-on starts straight away) or chooser (the startup chooser first); start: what CCS Nucleus opens (native: Original
# Menu; ccs-home: CCS HOME, which needs CCS Mod); auto, with the chooser: what it starts by itself after its countdown (menu: start; last: the
# menu used last; wait: no countdown)
NUCLEUS_PROFILES = ("none", "direct", "chooser"); STARTS = ("native", "ccs-home"); AUTOS = ("menu", "last", "wait"); COUNTDOWN = 10
NUCLEUS_RANK = {"none": 0, "direct": 1, "chooser": 2}
# Studio's own default: CCS Mod (CCS HOME's tile) is added; CCS Nucleus only when it is asked for
STUDIO_CHOICE = {"mod": True, "nucleus": "none", "start": "native", "auto": "menu"}
MADE_MARK = "# stand-in: folders this install made: "   # (FakeConsole only: its note in hakchi's list for a CCS package)
HOME_LAYOUT = "mod"   # the one CCS HOME package layout (native_home/console_package.py LAYOUTS): CCS Mod provides CCS HOME
PC_DIR = "native-covers"   # a console package's files for this computer only (native_home/console_package.py PC_DIR): never sent
PC_DIRS = (PC_DIR, "hakchi-copies")   # and its Hakchi-ready copies too large for one message (hakchi_publish.COPY_DIR): sent only as a Hakchi entry's game file
REGISTRY = ROOT_DIR + "/nucleus/registry"   # CCS Nucleus's sealed registry (ccs-boot-registry/1): read here, changed only by its own command
REGISTRY_SCHEMA = "ccs-boot-registry/1"
REMOVES_TEXT = "This console already has add-ons this choice would remove. Changing or removing add-ons is in CCS Console Workbench's Console Restore."
HOME_NEEDS_MOD = "CCS HOME opens at power-on only with CCS Mod, which provides CCS HOME. Add CCS Mod, or choose Original Menu."
NOTHING_TEXT = ("Nothing from this collection would go to the console: CCS Mod is not chosen and its games are not added to Original Menu. "
                "Choose CCS Mod to play the collection in CCS HOME, or add its games to Original Menu as Hakchi games.")
NO_HAKCHI_GAMES_TEXT = ("None of this collection's games can be added to Original Menu as Hakchi games, and CCS Mod is not chosen, so nothing from this "
                        "collection would go to the console. Choose CCS Mod to play it in CCS HOME.")
CHOOSER_MISSING = ("CCS Nucleus's startup chooser cannot be added from this installation: the program it shows itself with or its menu fonts "
                   "are not here. Install Classic Cart Studio again, or choose CCS Nucleus without its startup chooser. Nothing was changed.")
# the chooser package's own inputs (build_packages.CHOOSER_INPUTS): {"program": the console build of the native HOME program, "fonts": the
# player's menu fonts}. None: CCS_CHOOSER_PROGRAM and CCS_CHOOSER_FONTS when both are set, else the app's own (native_home.package.runtime_binary,
# play.fonts_dir). A test sets stand-ins here
CHOOSER_INPUTS: dict | None = None
# the start-up module of earlier versions: one overlay file that hakchi itself deletes at every start (script/removed); retired, never installed
LEGACY_NAME = "ccs-home"; LEGACY_FILE = "/etc/init.d/S81clover-mcp"
OVERLAY = "/var/lib/hakchi/rootfs"; HMOD_DIR = "/var/lib/hakchi/hmod"
HAKCHI_KERNEL = "3.4.113.29-madmonkey"
# the consoles this program works with: a North American NES Classic Edition, or a North American or European Super NES Classic Edition,
# running the pinned hakchi (Hakchi2 CE 3.9.3's bundled hakchi.hmod v1.0.4-126 with its kernel) on its own firmware. The model is the one
# the console's own software names by its board and REGION (/etc/clover/boardtype and /etc/clover/REGION, read by the worker's identify;
# the shared platform registry decides which model a board and region are), never a choice made on the PC; hakchi's own system code
# ($sftype-$sfregion) must say the same, and the data storage must be where that model keeps it. Anything else, or anything unreadable, is
# refused before any change
SUPPORTED = {"kernel": HAKCHI_KERNEL, "hakchi": "v1.0.4-126"}
# per model: its names, the registry's capability prefix (on any model but the NES Classic, a change needs the registry's "<prefix>-addons"
# capability: CCS Mod, CCS Nucleus and CCS HOME on that model), hakchi's system codes this program installs on, its built-in games, and
# whether Nintendo's software on it has the NES emulator that ordinary Hakchi games of NES titles launch (clover-kachikachi: the NES Classic
# only; the Super NES Classic's own software has none, so a collection's NES games reach it only through CCS HOME's own game player)
MODELS = {
    "nes-classic-edition": {"name": "NES Classic Edition", "short": "NES Classic", "capability": "nes-classic", "systems": ("nes-usa",), "stock_games": 30, "nes_emulator": True},
    "snes-classic-edition": {"name": "Super NES Classic Edition", "short": "SNES Classic", "capability": "snes-classic", "systems": ("snes-usa", "snes-eur"), "stock_games": 21, "nes_emulator": False},
}
DATA_MOUNT = "/dev/nandc ext4"   # where both models keep their data storage (/var/lib): the console's NAND part nandc, an ext4 file system
SUPPORTED_TEXT = "the North American NES Classic Edition and the North American and European Super NES Classic Edition"
HWID_RE = re.compile(r"^[0-9A-Za-z-]{4,64}$")
PROTECTED =("/usr", "/var/saves", "/etc", "/var/lib/hakchi", "/var/lib/clover", "/bin", "/sbin", "/lib", "/boot", "/dev", "/proc", "/sys", "/newroot")
WRITE_ROOTS = (ROOT_DIR + "/", "/tmp/ccs/")
PROFILE = "nes-classic-edition"   # the model whose room for saves and safety margin (the shared storage specification) a plan is measured with before a console is checked; a checked console's own model is used after
MAX_FILE = 64 * 1000 * 1000; MAX_TOTAL = 400 * 1000 * 1000; MAX_FILES = 4000
REL_RE = re.compile(r"^[A-Za-z0-9_-][A-Za-z0-9._-]{0,119}(/[A-Za-z0-9_-][A-Za-z0-9._-]{0,119}){0,7}$")
COPY_RE = re.compile(r"^[0-9a-f]{16}\.[0-9a-f]{8}$")
MODES = {"0o644": "0644", "0o755": "0755"}
STATE_IDENTITY = {"platform": "linux-armv7", "core": "fceumm", "core_version": "ac0d00a7"}   # gp.h for the console build: what its saves name

# the earlier start-up module's text (versions before the CCS Mod / CCS Nucleus policy): kept only so that this program recognizes it as its own
# when it retires it; it is never installed again
S81_OVERRIDE = """#!/bin/sh
# hakchi module ccs-home (Classic Cart Studio): the console starts Classic Cart Studio's HOME, then Nintendo's menu when it ends.
# hakchi's uistart runs "sh /etc/init.d/S81clover-mcp start" in the foreground; this start always ends by exec'ing clover-mcp, so the caller
# waits exactly as it does for Nintendo's menu, and Nintendo's menu runs after "Switch to Nintendo HOME", after any failure and when no
# package is installed. Uninstalling the module (hakchi pack_uninstall ccs-home) brings back the stock script. Copyright (C) 2026 Rance9.
# GPL-3.0-or-later.
P=/var/lib/ccs/pkg
start() {
  V=""
  if [ -f "$P/current" ] && [ ! -L "$P/current" ]; then read -r V < "$P/current"; fi
  case "$V" in ????????????????.????????) ;; *) V="" ;; esac
  case "$V" in *[!0-9a-f.]*) V="" ;; esac
  if [ -n "$V" ] && [ -d "$P/$V" ] && [ ! -L "$P/$V" ] && [ -x "$P/$V/bin/ccs-player-armv7" ] && [ -f "$P/$V/session.json" ]; then
    mkdir -p /tmp/ccs/scratch /tmp/ccs/logs /var/lib/ccs/data/saves /var/lib/ccs/data/state /var/lib/ccs/data/game-settings
    rm -rf /tmp/ccs/pkg && ln -s "$P/$V" /tmp/ccs/pkg && \\
      /tmp/ccs/pkg/bin/ccs-player-armv7 --session /tmp/ccs/pkg/session.json --trace /tmp/ccs/logs/trace.jsonl --report /tmp/ccs/logs/report.json \\
        --map /tmp/ccs/pkg/config/controller-map.txt --startup /tmp/ccs/pkg/startup/startup.json > /tmp/ccs/logs/player.txt 2>&1
  fi
  exec /usr/bin/clover-mcp
}
stop() { killall ccs-player-armv7 2>/dev/null; killall clover-mcp 2>/dev/null; true; }
case "$1" in
start) start ;;
stop) stop ;;
restart) stop; start ;;
*) echo "clover-mcp: Please use start, stop, or restart."; exit 1 ;;
esac
"""

class PlanError(ValueError): pass
# A console whose Hakchi setup is not Hakchi2 CE 3.9.3 (its console components: hakchi v1.0.4-126, kernel 3.4.113.29-madmonkey) is refused
# with the same words as CCS Console Workbench (CcsBackup/Engine/HakchiRequirement.cs) and the official release page; the page shows them
# (install.js hakchiNotice). Classic Cart Studio never installs, updates or downgrades Hakchi itself.
HAKCHI_MARKER = "[hakchi-3.9.3-required]"
HAKCHI_URL = "https://github.com/TeamShinkansen/Hakchi2-CE/releases/tag/v3.9.3"
def hakchi_required(i: dict) -> str:
    h = i.get("hakchi"); k = i.get("kernel")
    found = ("no Hakchi files on its data storage" if not h or h == "none" else f"hakchi {h}") + (f", kernel {k}" if k else "")
    return (f"{HAKCHI_MARKER}\nHakchi2 CE 3.9.3 is required\nThis console's Hakchi setup does not match the version Classic Cart Studio needs. "
            f"Nothing has been changed.\nInstall Hakchi2 CE 3.9.3 on your PC, use it to update your console, then return to Classic Cart Studio.\nDetected: {found}")

class Refused(RuntimeError):
    """a precondition does not hold: nothing was changed. detail: what the refusal can tell its caller beyond the reason (the console's
    add-ons now, and the nearest choice that keeps them, when a choice would remove one)"""
    def __init__(self, step, why, **detail):
        self.step, self.why, self.detail = step, why, detail; super().__init__(f"{step}: {why}")
class ChooserUnavailable(Refused):
    """the chooser package cannot be built here (its program or menu fonts are not part of this installation)"""
    def __init__(self, why: str = ""): super().__init__("addons", CHOOSER_MISSING); self.cause = why
class InstallStopped(RuntimeError):
    def __init__(self, step, why):
        self.step, self.why = step, why; super().__init__(f"{step}: {why}")
class Cancelled(RuntimeError): pass
class CleanupStopped(RuntimeError):
    """after the installation was verified: a clean-up step the console did not confirm, or a check before one that failed. Every write
    that would follow is skipped and the attempt's record stays open. uncertain: what may or may not have changed; partial: a copy part
    removed ({"copy", "removed_files"})"""
    def __init__(self, at: str, why: str, uncertain: list | None = None, partial: dict | None = None):
        super().__init__(f"{at}: {why}"); self.at, self.why, self.uncertain, self.partial = at, why, list(uncertain or []), partial
class JournalCorrupt(ValueError): pass
class PointerChanged(OSError): pass

def _sha(b: bytes) -> str: return hashlib.sha256(b).hexdigest()
def _read(path: str) -> bytes:
    with open(path, "rb") as f: return f.read()
def _canon(o) -> bytes: return json.dumps(o, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
def _now() -> str: return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
def module_sha() -> str: return _sha(S81_OVERRIDE.encode())   # the earlier start-up module's (recognized when retiring it)

def _nucleus_dir() -> str:
    """tools/console-nucleus (the installed app: its bundled copy beside the backend)"""
    import sys
    for d in (os.environ.get("CCS_CONSOLE_NUCLEUS"), os.path.join(getattr(sys, "_MEIPASS", ""), "console-nucleus") if getattr(sys, "frozen", False) else None,
              os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "console-nucleus"))):
        if d and os.path.isfile(os.path.join(d, "build_packages.py")): return d
    raise FileNotFoundError("the CCS Mod / CCS Nucleus package sources (tools/console-nucleus) are not part of this installation")

_ADDON_CACHE: dict = {}
def _build_packages():
    import importlib.util
    d = _nucleus_dir(); spec = importlib.util.spec_from_file_location("ccs_method_packages", os.path.join(d, "build_packages.py")); BP = importlib.util.module_from_spec(spec); spec.loader.exec_module(BP)
    return BP

def chooser_inputs() -> dict:
    """the chooser package's inputs (CHOOSER_INPUTS above): {"program", "fonts"}, either of them None when it is not here"""
    if CHOOSER_INPUTS: return dict(CHOOSER_INPUTS)
    if os.environ.get("CCS_CHOOSER_PROGRAM") and os.environ.get("CCS_CHOOSER_FONTS"): return {"program": os.environ["CCS_CHOOSER_PROGRAM"], "fonts": os.environ["CCS_CHOOSER_FONTS"]}
    prog = fonts = None
    try:
        from native_home import package as nhp; prog = nhp.runtime_binary()
    except Exception: pass
    try:
        import play; fonts = play.fonts_dir()
    except Exception: pass
    return {"program": prog, "fonts": fonts}

def addon_package(addon: str) -> dict:
    """the add-on's hakchi package, byte for byte the one the CCS Console Workbench's preparation image carries (tools/console-nucleus/
    build_packages.py builds it reproducibly; build_images.METHOD_SHAS and the transport worker pin it): {"addon", "name", "hmod" (bytes),
    "sha256", "package_id" (its VERSION's package), "files" ({overlay path: SHA-256}: what hakchi copies into its overlay), "overlay_bytes"}.
    The chooser's package carries its own program and menu fonts (chooser_inputs); without them it raises ChooserUnavailable"""
    if addon not in ADDONS: raise PlanError(f"unknown add-on {addon!r}")
    key = addon
    if addon == "nucleus-chooser":
        ci = chooser_inputs()
        if not (ci.get("program") and os.path.isfile(ci["program"]) and ci.get("fonts") and os.path.isdir(ci["fonts"])): raise ChooserUnavailable("its program or menu fonts are not here")
        st = os.stat(ci["program"]); key = json.dumps([addon, os.path.abspath(ci["program"]), st.st_size, st.st_mtime_ns, os.path.abspath(ci["fonts"]), sorted(os.listdir(ci["fonts"]))])
    if key not in _ADDON_CACHE:
        BP = _build_packages()
        if addon == "nucleus-chooser":
            BP.CHOOSER_INPUTS = ci
            try: mem = BP.members(addon)
            except BP.MissingInput as e: raise ChooserUnavailable(str(e)) from None
        else: mem = BP.members(addon)
        hb = BP.hmod_bytes(mem); files = {}
        for line in mem[f"etc/ccs/{addon}/FILES"][0].decode().splitlines():
            s, _mode, rel = line.split(" ", 2); files["/" + rel] = s
        files[f"/etc/ccs/{addon}/FILES"] = _sha(mem[f"etc/ccs/{addon}/FILES"][0])
        pid = dict(l.split("=", 1) for l in mem[f"etc/ccs/{addon}/VERSION"][0].decode().splitlines())["package"]
        _ADDON_CACHE[key] = {"addon": addon, "name": ADDONS[addon], "hmod": hb, "sha256": _sha(hb), "package_id": pid, "files": files,
                             "overlay_bytes": sum(len(b) for n, (b, _m) in mem.items() if n not in BP.NOT_COPIED),
                             "overlay_sizes": sorted(len(b) for n, (b, _m) in mem.items() if n not in BP.NOT_COPIED)}
    return _ADDON_CACHE[key]

def staged_package(root: str | None) -> str | None:
    """the console package the app staged: root/mod (ccs_backend.console_package_stage), or root itself when it holds a MANIFEST.json (a
    test's package folder); None when there is none"""
    if not root or not os.path.isdir(root): return None
    if os.path.isfile(os.path.join(root, "MANIFEST.json")): return root
    d = os.path.join(root, HOME_LAYOUT)
    return d if os.path.isfile(os.path.join(d, "MANIFEST.json")) else None

# ---------------------------------------------------------------- the add-on choice
def addon_choice(d: dict | None = None) -> dict:
    """a requested add-on choice, complete and checked: {"mod": bool, "nucleus": "none"|"direct"|"chooser", "start": "native"|"ccs-home",
    "auto": "menu"|"last"|"wait"}. What is not given is Studio's default (STUDIO_CHOICE). start counts only with CCS Nucleus and auto only with
    its chooser; otherwise they are their defaults, so one choice always gives one plan. A choice that cannot be made raises Refused with its
    reason in plain words"""
    ch = dict(STUDIO_CHOICE)
    for k in ch:
        if isinstance(d, dict) and d.get(k) is not None: ch[k] = d[k]
    if not isinstance(ch["mod"], bool): raise Refused("addons", "Choose whether CCS Mod is added.")
    if ch["nucleus"] not in NUCLEUS_PROFILES: raise Refused("addons", "Choose whether CCS Nucleus is added, and how it starts.")
    if ch["start"] not in STARTS: raise Refused("addons", "Choose what opens when the console is turned on.")
    if ch["auto"] not in AUTOS: raise Refused("addons", "Choose what the startup chooser starts by itself.")
    if ch["nucleus"] != "none" and ch["start"] == "ccs-home" and not ch["mod"]: raise Refused("addons", HOME_NEEDS_MOD)
    if ch["nucleus"] == "none": ch["start"], ch["auto"] = "native", "menu"
    elif ch["nucleus"] == "direct": ch["auto"] = "menu"
    return ch

def choice_addons(ch: dict) -> list[str]:
    """the add-ons a choice includes, in the order they are installed"""
    return (["mod"] if ch["mod"] else []) + (["nucleus"] if ch["nucleus"] != "none" else []) + (["nucleus-chooser"] if ch["nucleus"] == "chooser" else [])

def start_line(ch: dict) -> str | None:
    """CCS Nucleus's start for a choice, as its own command takes it: "DEFAULT CHOOSER HOME SELECTION TIMEOUT"; None without CCS Nucleus"""
    if ch["nucleus"] == "none": return None
    chooser = ch["nucleus"] == "chooser"
    return " ".join([ch["start"], "always" if chooser else "never", "on" if ch["mod"] else "off", "last" if chooser and ch["auto"] == "last" else "default",
                     "0" if chooser and ch["auto"] == "wait" else str(COUNTDOWN)])

def _menu_name(default: str) -> str: return {"native": "Original Menu", "ccs-home": "CCS HOME"}.get(default, "one of your own system images")
def start_words(default: str, chooser_on: bool, selection: str, timeout: int) -> str:
    """what opens at power-on, in plain words"""
    name = _menu_name(default)
    if not chooser_on: return f"When the console is turned on, {name} opens straight away. The startup chooser is off."
    secs = f"{timeout} second{'' if timeout == 1 else 's'}"
    return "When the console is turned on, the startup chooser appears first. " + ("Nothing starts until you choose." if timeout == 0
            else f"If you do nothing, the menu you used last starts in {secs} ({name} the first time)." if selection == "last" else f"If you do nothing, {name} starts in {secs}.")

def start_text(ch: dict) -> str:
    """what opens at power-on with a choice, in plain words (the CCS Console Workbench's AddonChoice.StartText)"""
    if ch["nucleus"] == "none": return "When the console is turned on, Original Menu opens, as it does now." + (" CCS HOME opens from its tile there." if ch["mod"] else "")
    p = start_line(ch).split(" ")
    return start_words(p[0], p[1] == "always", p[3], int(p[4]))

def addons_words(ch: dict) -> str:
    """which add-ons a choice names, in plain words ("CCS Mod and CCS Nucleus with its startup chooser"; "no CCS add-ons")"""
    parts = (["CCS Mod"] if ch["mod"] else []) + {"none": [], "direct": ["CCS Nucleus"], "chooser": ["CCS Nucleus with its startup chooser"]}[ch["nucleus"]]
    return " and ".join(parts) if parts else "no CCS add-ons"

def removes(now: dict, want: dict) -> list[str]:
    """the add-ons the console has (now) that a choice (want) would take away"""
    out = ["mod"] if now["mod"] and not want["mod"] else []
    if NUCLEUS_RANK[now["nucleus"]] > NUCLEUS_RANK[want["nucleus"]]: out += [a for a in ("nucleus-chooser", "nucleus") if a in choice_addons(now) and a not in choice_addons(want)]
    return out

def keep_choice(want: dict, now: dict) -> dict:
    """the nearest choice to want that removes nothing the console has: its add-ons kept; CCS Nucleus kept without being asked for keeps
    the start the console has"""
    k = dict(want, mod=bool(want["mod"] or now["mod"]))
    if NUCLEUS_RANK[now["nucleus"]] > NUCLEUS_RANK[want["nucleus"]]:
        k["nucleus"] = now["nucleus"]
        if want["nucleus"] == "none": k["start"], k["auto"] = now["start"], now["auto"]
    try: return addon_choice(k)
    except Refused: return addon_choice(dict(k, start="native"))

# ---------------------------------------------------------------- CCS Nucleus's registry (read only; its own command changes it)
def _sealed(b: bytes | None, schema: str) -> list[tuple[str, str]] | None:
    """a sealed record of CCS's console library (lib.sh ccs_sealed): its first line schema=..., its last end=<SHA-256 of every line before>;
    its lines between, as (key, value) in order"""
    if b is None: return None
    t = b.decode("utf-8", "replace"); lines = t.split("\n")
    if not t.endswith("\n") or len(lines) < 3 or lines[0] != f"schema={schema}" or not lines[-2].startswith("end="): return None
    body = "\n".join(lines[:-2]) + "\n"
    if _sha(body.encode()) != lines[-2][4:]: return None
    return [tuple(l.split("=", 1)) for l in lines[1:-2] if "=" in l]

ID_RE = re.compile(r"^[a-z0-9][a-z0-9-]{0,31}$")
def parse_registry(b: bytes | None) -> dict | None:
    """CCS Nucleus's registry (ccs-boot-registry/1) when it is sealed and consistent as CCS Nucleus's own check (reg_ok) requires: its
    values (each key's first line counts, as ccs_get reads it), its options in order and each option's fields; None otherwise"""
    kv = _sealed(b, REGISTRY_SCHEMA)
    if kv is None: return None
    g: dict = {}
    for k, v in kv: g.setdefault(k, v)
    def num(v): return isinstance(v, str) and v.isdigit() and len(v) <= 12
    opts_ids = (g.get("options") or "").split()
    if not num(g.get("generation")) or g.get("selection") not in ("default", "last") or g.get("chooser") not in ("always", "never"): return None
    if not num(g.get("timeout")) or int(g["timeout"]) > 99 or "native" not in opts_ids or len(set(opts_ids)) != len(opts_ids): return None
    opts = {}
    for i in opts_ids:
        kind = g.get(f"opt.{i}.kind")
        if not ID_RE.match(i) or not g.get(f"opt.{i}.label"): return None
        if (kind == "native") != (i == "native") or kind not in ("native", "ccs-home", "owner-root"): return None
        opts[i] = kind
    if g.get("default") not in opts or (g["chooser"] == "never" and opts[g["default"]] == "owner-root"): return None
    return {"lines": kv, "generation": int(g["generation"]), "default": g["default"], "selection": g["selection"], "chooser": g["chooser"],
            "timeout": int(g["timeout"]), "options": opts_ids, "kinds": opts}

def nucleus_start_state(c) -> dict:
    """CCS Nucleus's start as its registry holds it (reads only): {"state": "none" (no registry), "ok" or "invalid" (there, but not a
    registry CCS Nucleus would use), "line": "DEFAULT CHOOSER HOME SELECTION TIMEOUT" (ok only), and its parts}. HOME is "on" when CCS HOME
    is one of its options"""
    t = c.stat(REGISTRY)["type"]
    if t == "none": return {"state": "none", "line": None}
    reg = parse_registry(c.read(REGISTRY, 65536)) if t == "file" else None
    if reg is None: return {"state": "invalid", "line": None}
    home = "on" if "ccs-home" in reg["options"] else "off"
    return {"state": "ok", "line": f"{reg['default']} {reg['chooser']} {home} {reg['selection']} {reg['timeout']}", "default": reg["default"], "chooser": reg["chooser"],
            "home": home, "selection": reg["selection"], "timeout": reg["timeout"], "default_kind": reg["kinds"][reg["default"]]}

# ---------------------------------------------------------------- the console's add-ons (read only)
def console_addons(c) -> dict:
    """the add-ons the console has now: which of the three packages hakchi lists, each "current" (this installation's package, every file
    unchanged), "older" (another version of the same CCS package, by its own VERSION record: its own install updates it, refusing any file
    that is not its own unchanged file) or "present" (named by its own record; this installation cannot build that package to compare it),
    and CCS Nucleus's start (its registry). {"packages": {addon: {"name", "package", "state"}}, "choice": the add-ons as a choice (the
    Workbench's AddonChoice.FromState), "start": nucleus_start_state, "text": in plain words}. A program hakchi does not list, a package this
    program does not recognize, changed files, a startup chooser without CCS Nucleus, CCS Nucleus's settings that cannot be read, or a console
    that cannot be read raise Refused: then nothing is changed rather than an add-on guessed"""
    pk = {}
    try:
        for a, n in ADDONS.items():
            mf = c.method_files(n)
            if mf is None:
                if c.overlay_sha256(ADDON_PROGRAMS[a]) is not None: raise Refused("preflight", f"{ADDON_LABELS[a]}'s program is on the console but hakchi does not list its package ({n}); it is not this program's to change. Nothing was changed")
                continue
            pid = str(mf.get("package") or "")
            if not re.match(r"^[0-9a-f]{64}$", pid): raise Refused("preflight", f"a {n} package this program does not recognize (no package record of its own) is on the console; it is kept, and nothing was changed")
            try: ap = addon_package(a)
            except ChooserUnavailable: pk[a] = {"name": n, "package": pid, "state": "present"}; continue
            if pid == ap["package_id"]:
                bad = [p for p, s in ap["files"].items() if (mf.get("files") or {}).get(p) != s]
                if bad: raise Refused("preflight", f"files of {n} on the console were changed ({', '.join(bad[:3])}); they are kept, and nothing was changed")
                pk[a] = {"name": n, "package": pid, "state": "current"}
            else: pk[a] = {"name": n, "package": pid, "state": "older"}
        if "nucleus-chooser" in pk and "nucleus" not in pk: raise Refused("preflight", "CCS Nucleus's startup chooser is on the console without CCS Nucleus, which it needs; it is kept, and nothing was changed. CCS Console Workbench's Console Restore can repair it")
        start = nucleus_start_state(c) if "nucleus" in pk else {"state": "none", "line": None}
    except (ConnectionError, OSError, ValueError) as e: raise Refused("preflight", f"the add-ons on this console could not be read ({e}); nothing was changed") from None
    if "nucleus" in pk and start["state"] == "invalid": raise Refused("preflight", "CCS Nucleus's settings on this console cannot be read (its registry is not one it would use); they are kept as they are, and nothing was changed. CCS Console Workbench's Console Restore can repair them")
    nuc = "none" if "nucleus" not in pk else ("chooser" if "nucleus-chooser" in pk else "direct")
    p = (start.get("line") or "").split(" ")
    ch = {"mod": "mod" in pk, "nucleus": nuc, "start": "ccs-home" if p[0] == "ccs-home" else "native",
          "auto": "menu" if len(p) != 5 else ("last" if p[3] == "last" else "wait" if p[4] == "0" else "menu")}
    if nuc == "none": ch["start"], ch["auto"] = "native", "menu"
    elif nuc == "direct": ch["auto"] = "menu"
    text = f"This console has {addons_words(ch)}. " + (start_words(start["default"], start["chooser"] == "always" and nuc == "chooser", start["selection"], start["timeout"]) if start.get("line")
            else "When the console is turned on, Original Menu opens." + (" CCS HOME opens from its tile there." if ch["mod"] else ""))
    return {"packages": pk, "choice": ch, "start": start, "text": text}

def pointer_bytes(copy: str) -> bytes: return (copy + "\n").encode()

def hakchi_system(model: str, region: str) -> str | None:
    """hakchi's own system code ($sftype-$sfregion) for a model and the REGION its software names (hakchi: EUR is eur, anything else on an NES
    Classic is usa, as the North American console's EUR_USA; the Super NES Classic's USA and EUR are its two supported regions)"""
    if model == "nes-classic-edition": return "nes-eur" if region == "EUR" else "nes-usa"
    if model == "snes-classic-edition" and region in ("USA", "EUR"): return "snes-" + region.lower()
    return None

def model_refusal(model: str, operation: str = "addons") -> str | None:
    """None when this program may change a console of this model, else why not: on the NES Classic always; on any other model only when the
    shared platform registry supports its "<prefix>-<operation>" capability (the same rule as CCS Console Workbench's ConsoleModel.Refusal)"""
    m = MODELS.get(model)
    if not m: return "This console's model is not one Classic Cart Studio supports. Nothing was changed."
    if model == "nes-classic-edition": return None
    import platform_ids
    cap = f"{m['capability']}-{operation}"
    if platform_ids.allowed("capability", cap): return None
    why = (platform_ids.entry("capability", cap) or {}).get("reason") or ""
    return f"Installing Classic Cart Studio's add-ons on the {m['name']} is not available in this version" + (f": {why.rstrip('.')}." if why else ".") + " Nothing was changed."

def no_nes_emulator_text(model: str) -> str:
    return (f"The {MODELS[model]['name']}'s own software has no NES emulator, so this collection's games cannot be added to its menu as Hakchi games. "
            "Choose CCS Mod to play them in CCS HOME, and leave Also add this collection's games to Original Menu unchecked. Nothing was changed.")

def check_device(ident: dict | None, step: str = "device") -> dict:
    """the console this session reads now, against the supported consoles: its hardware id; hakchi's kernel; its model from the board and
    REGION its own software names, with hakchi's system code saying the same and its data storage where that model keeps it; hakchi's
    version, its own CanInteract rule and the console's own firmware in use. A missing, unreadable or different value refuses (fail closed,
    nothing changed). What a model allows to change is decided by the operation (model_refusal)"""
    i = ident or {}
    hwid = str(i.get("hwid") or "")
    if not HWID_RE.match(hwid): raise Refused(step, "the console's identity could not be read; nothing was changed")
    if i.get("kernel") != SUPPORTED["kernel"]:
        if "madmonkey" in str(i.get("kernel") or ""): raise Refused(step, hakchi_required(i))   # another Hakchi build's kernel
        raise Refused(step, f"the console runs kernel {i.get('kernel')!r}: hakchi must be installed first (Prepare Console in CCS Console Workbench); nothing was changed")
    if i.get("hakchi") != SUPPORTED["hakchi"]: raise Refused(step, hakchi_required(i))
    import platform_ids
    board, region = str(i.get("board") or ""), str(i.get("region") or "")
    if not board or not region: raise Refused(step, "the console's model could not be read (its software does not name its board and region); nothing was changed")
    model = platform_ids.model_for(board, region)
    if model not in MODELS: raise Refused(step, f"this console (board {board}, region {region}) is not one Classic Cart Studio supports: it works with {SUPPORTED_TEXT}. Nothing was changed")
    m = MODELS[model]; system = str(i.get("system") or "")
    if system != hakchi_system(model, region): raise Refused(step, f"this console's software names a {m['name']} (board {board}, region {region}), but hakchi reports {system or 'nothing'}; nothing was changed")
    if system not in m["systems"]: raise Refused(step, f"this console reports {system}; Classic Cart Studio works with {SUPPORTED_TEXT}. Nothing was changed")
    if i.get("data_mount") != DATA_MOUNT: raise Refused(step, f"this console's data storage is not where the {m['name']} keeps it (found {i.get('data_mount') or 'nothing readable'}); nothing was changed")
    if i.get("can_interact") is not True: raise Refused(step, "hakchi reports that this console cannot be used as it is (its boot, kernel or scripts are too old); nothing was changed")
    if i.get("custom_firmware") is not False: raise Refused(step, "this console runs a custom firmware (or it could not be read); only its own firmware is supported. Nothing was changed")
    return {"hwid": hwid, "system": system, "kernel": i["kernel"], "hakchi": i["hakchi"], "model": model, "model_name": m["name"], "board": board, "region": region, "simulated": i.get("simulated") is True}

def same_console(c, hwid: str, step: str = "device"):
    """the console answering now is still the bound one (read again, not from a cached answer): otherwise stop before the next change"""
    i = c.identity(fresh=True)
    if str(i.get("hwid") or "") != hwid: raise InstallStopped(step, "another console answers now (or its identity cannot be read): stopped before the next change")

def check_rel(rel: str) -> str:
    """a package path: relative, plain names (letters, digits, . _ -), no empty, "." or ".." part, at most 8 deep"""
    if not isinstance(rel, str) or not REL_RE.match(rel) or any(p in (".", "..") or p.startswith("..") for p in rel.split("/")): raise PlanError(f"package path not allowed: {rel!r}")
    return rel

def hakchi_plan(package_dir: str, files: list[dict], publish: bool, base: str = PKG_DIR) -> dict:
    """the plan's Hakchi games: {"publish": False}, or the collection's Hakchi projection (hakchi_publish.summary: no picture or file bytes),
    its review in plain words, the console program's pins and the room it needs (its entries staged and then written, the index twice).
    base: the folder of copies whose game files the entries launch (CCS HOME's pkg, or the games copies without CCS Mod)"""
    if not publish: return {"publish": False}
    try:
        sp = HP.spec(package_dir, {f["path"]: f["sha256"] for f in files})
        lay = HP.layout(sp, "storage", "0" * 16 + "." + "0" * 8, base)
    except HP.PublishError as e: raise PlanError(str(e)) from None
    prog = HP.program(); ent = sum(len(b) for e in lay["entries"] for b in e["files"].values())
    idx = len(HP.index_bytes(HP.index_doc(sp, lay, {"store": "/var/lib/hakchi/games/nes-usa", "menu": "/var/lib/hakchi/games/nes-usa/000", "system": HP.SQUASHFS + HP.GAMEPATH}, {}, 1)))
    copies = sum(1 for e in lay["entries"] if any(n.endswith(".nes") for n in e["files"]))   # game files the entries carry in the form Hakchi stores
    return {"publish": True, "projection": HP.summary(sp), "review": HP.review(sp), "program": prog["pins"], "program_dir": prog["dir"], "entry_bytes": ent, "entry_copies": copies, "_spec": sp,
            "bytes": 2 * ent + 2 * idx + 4096 * (len(lay["entries"]) + 4) + sum(len(b) for b in prog["files"].values())}

def _games_words(n: int) -> str: return f"{n} game{'' if n == 1 else 's'}"
def hakchi_where_text(hk: dict, games_only: dict | None) -> str:
    """where the Hakchi games' files go and how large they are, in plain words (the plan's hakchi_text after the review's own line)"""
    nc = hk.get("entry_copies") or 0
    ent = (f"their entries (launch files and pictures" + (f", and {nc} game file{'' if nc == 1 else 's'} in the form Hakchi stores" if nc else "")
           + f": {GO.size_text(hk['entry_bytes'])}) go into Hakchi's game folder")
    if games_only is None: return f"They use the game files already in CCS HOME's copy; {ent}."
    n = len([f for f in games_only["files"] if f["path"].startswith("roms/")])
    if not n: return f"Their game files travel inside their entries; {ent}. CCS HOME is not installed."
    return (f"Their game files ({n} file{'' if n == 1 else 's'}, {GO.size_text(games_only['rom_bytes'])}) go into a folder of Classic Cart Studio's own, with the license notices "
            f"that come with them ({GO.size_text(games_only['bytes'] - games_only['rom_bytes'])}); {ent}. CCS HOME is not installed.")

earlier_setting = SM.earlier_setting   # Settings > CCS HOME > Earlier HOME versions, as CCS Console Workbench saves it (storage_model.py)

def build_plan(package_dir: str, choice: dict | None = None, hakchi_games: bool | None = None, earlier: int | None = None) -> dict:
    """the immutable plan for one console package (CCS HOME's layout, "mod") and one add-on choice: the files the console gets with their
    SHA-256 and modes (validated), the add-ons the console should have afterwards (addon_choice: what is not given is Studio's default) with
    each one's pinned package, CCS Nucleus's start for that choice, the room it needs and the identity its saves will carry; named by the
    SHA-256 of its own content. hakchi_games: also add the collection's games to Original Menu as ordinary Hakchi games (hakchi_plan; None:
    Studio's default, on when CCS Mod is not chosen). delivery: "home" with CCS Mod (the whole package becomes CCS HOME's copy under pkg/),
    "games" without it (games_only.py: only the game files the Hakchi entries need, and their notices, under games/; nothing of CCS HOME).
    Without CCS Mod and without Hakchi games nothing of the collection would reach the console: refused, as is a collection none of whose
    games can be a Hakchi game. A choice that cannot be made raises Refused (its reason in plain words); a package that cannot be used,
    PlanError"""
    ch = addon_choice(choice)
    if hakchi_games is not None and not isinstance(hakchi_games, bool): raise Refused("addons", "Choose whether this collection's games are added to Original Menu as Hakchi games.")
    publish = (not ch["mod"]) if hakchi_games is None else hakchi_games
    if not ch["mod"] and not publish: raise Refused("addons", NOTHING_TEXT)
    man_b = _read(os.path.join(package_dir, "MANIFEST.json")); man = json.loads(man_b)
    if man.get("schema") != "ccs-console-package/1": raise PlanError("not a Classic Cart Studio console package")
    import platform_ids   # a package for a game system, core or method this version does not support is never planned
    tgt = platform_ids.resolve_target(man, "console-package", man.get("games") if isinstance(man.get("games"), list) else None)
    if not tgt["ok"]: raise PlanError(tgt["text"])
    if man.get("layout") == "install": raise PlanError("this package was made for the earlier start-up module, which hakchi removes at every start: prepare the HOME menu again")
    if man.get("layout") != HOME_LAYOUT: raise PlanError(f"a {man.get('layout')!r} package is not for installing (CCS HOME's package is made for CCS Mod's layout): prepare the HOME menu again")
    if (man.get("console_paths") or {}).get("pkg") not in (None, "/tmp/ccs/pkg"): raise PlanError("the package was made for another place on the console (prepare it again)")
    entries = man.get("files")
    if not isinstance(entries, dict) or not entries or len(entries) > MAX_FILES: raise PlanError("the package's file list is missing or too long")
    files, seen = [], set()
    for rel, info in sorted(entries.items()):
        check_rel(rel)
        if rel in ("MANIFEST.json",) or rel.lower() in seen: raise PlanError(f"package path listed twice: {rel!r}")
        seen.add(rel.lower()); mode = (info or {}).get("mode", "0o644")
        if mode not in MODES: raise PlanError(f"{rel}: mode {mode!r} is not allowed (only 0644 and 0755)")
        if mode == "0o755" and "bin" not in rel.split("/")[:-1]: raise PlanError(f"{rel}: only programs in a bin folder are executable")
        b = _read(os.path.join(package_dir, *rel.split("/")))
        if len(b) > MAX_FILE: raise PlanError(f"{rel} is larger than any file of a console package")
        if len(b) != info.get("size") or _sha(b) != info.get("sha256"): raise PlanError(f"{rel} differs from the package's manifest")
        files.append({"path": rel, "size": len(b), "sha256": info["sha256"], "mode": mode})
    files.append({"path": "MANIFEST.json", "size": len(man_b), "sha256": _sha(man_b), "mode": "0o644"})
    total = sum(f["size"] for f in files)
    if total > MAX_TOTAL: raise PlanError("the package is larger than the console can hold")
    if not any(f["path"] == "bin/ccs-player-armv7" for f in files) or not any(f["path"] == "session.json" for f in files): raise PlanError("the package has no console player or session")
    # this computer's only (console_package.py PC.json: what Original Menu's covers are made from): PC.json is the one the manifest names,
    # its files are checked like the package's, the Hakchi projection reads them, and none is ever sent to the console
    pcf = []
    if man.get("pc_sha256"):
        try: pcb = _read(os.path.join(package_dir, HP.PC_FILE))
        except OSError: pcb = b""
        if _sha(pcb) != man["pc_sha256"]: raise PlanError(f"{HP.PC_FILE} differs from the package's manifest")
        try: pcl = json.loads(pcb).get("files")
        except ValueError: pcl = None
        if not isinstance(pcl, dict) or len(pcl) > MAX_FILES: raise PlanError(f"the package's {HP.PC_FILE} is not valid")
        pcf.append({"path": HP.PC_FILE, "size": len(pcb), "sha256": man["pc_sha256"]})
        for rel, info in sorted(pcl.items()):
            check_rel(rel)
            if not rel.startswith(tuple(d + "/" for d in PC_DIRS)) or rel in entries: raise PlanError(f"{rel} is not one of this computer's files of a console package")
            b = _read(os.path.join(package_dir, *rel.split("/")))
            if len(b) != (info or {}).get("size") or _sha(b) != info.get("sha256"): raise PlanError(f"{rel} differs from the package's {HP.PC_FILE}")
            pcf.append({"path": rel, "size": len(b), "sha256": info["sha256"]})
    names = choice_addons(ch); home = ch["mod"]
    pk = {a: addon_package(a) for a in names}   # the chooser's package needs its own inputs (ChooserUnavailable: refused in plain words)
    line = start_line(ch); hk = hakchi_plan(package_dir, files + pcf, publish, PKG_DIR if home else GO.GAMES_DIR); sp = hk.pop("_spec", None)
    go = None
    if not home:   # without CCS Mod: only what the Hakchi entries need, never CCS HOME (games_only.py)
        if not hk["review"]["games"]: raise Refused("addons", NO_HAKCHI_GAMES_TEXT)
        try: gfiles, _made = GO.files_for(package_dir, {f["path"]: f["sha256"] for f in files}, sp, str(man.get("label") or "Classic Cart Studio")[:80])
        except (ValueError, KeyError, OSError) as e: raise PlanError(f"the game files for the Hakchi games cannot be prepared: {e}") from None
        go = {"files": gfiles, "bytes": sum(f["size"] for f in gfiles), "rom_bytes": sum(f["size"] for f in gfiles if f["path"].startswith("roms/"))}
    version = _sha(man_b)[:16] if home else GO.version(go["files"])[:16]
    pfiles = {f["path"]: f["sha256"] for f in files + pcf}; out = files if home else go["files"]; total = sum(f["size"] for f in out)
    games_note = ("Without CCS Mod, CCS HOME is not installed: no menu, game player, themes or pictures of CCS HOME go to the console, only the game "
                  "files these Hakchi games use, with the license notices that come with them.")
    plan = {"schema": PLAN_SCHEMA, "delivery": "home" if home else "games", "version": version, "label": man.get("label"), "games": man.get("games"), "playable": man.get("playable") if home else hk["review"]["games"],
            "startup": man.get("startup") if home else None, "player": man.get("player") if home else None, "manifest_sha256": _sha(man_b), "state_identity": dict(STATE_IDENTITY), "layout": HOME_LAYOUT,
            "paths": ({"root": ROOT_DIR, "package": f"{PKG_DIR}/{version}.<attempt>", "pointer": POINTER, "data": DATA_DIR, "records": INST_DIR, "nucleus_settings": REGISTRY} if home else
                      {"root": ROOT_DIR, "games": f"{GO.GAMES_DIR}/{version}.<attempt>", "records": INST_DIR, "nucleus_settings": REGISTRY}),
            "files": out, "package_files": pfiles, "bytes": total,
            "addons": ch, "addons_text": f"Afterwards this console has {addons_words(ch)}.", "start_line": line, "start_text": start_text(ch),
            "notes": ([] if home else [games_note]) + hk.get("review", {}).get("notes", []),
            "hakchi": hk, "hakchi_text": (hk["review"]["text"] + " " + hakchi_where_text(hk, go)) if hk["publish"] else "This collection's games are not added to Original Menu as Hakchi games.",
            "games_only": None if home else {"files": len(go["files"]), "bytes": go["bytes"], "game_files": len([f for f in go["files"] if f["path"].startswith("roms/")]), "rom_bytes": go["rom_bytes"]},
            "packages": {a: {"name": p["name"], "label": ADDON_LABELS[a], "sha256": p["sha256"], "package_id": p["package_id"], "files": p["files"], "overlay_bytes": p["overlay_bytes"], "overlay_sizes": p["overlay_sizes"]} for a, p in pk.items()},
            "where": (f"this collection's console package as a new copy {PKG_DIR}/{version}.<attempt>, which {POINTER} names after it is read back (CCS HOME starts it); " if home else
                      f"only the game files the Hakchi entries launch and their license notices, as a new copy {GO.GAMES_DIR}/{version}.<attempt> (nothing of CCS HOME: no player, "
                      f"menu, themes, pictures, music, fonts or session; {POINTER} is not written; the copy is removed once no Hakchi entry names it); ")
                     + f"hakchi's pack_install of each add-on's package that is missing: its install script copies its files into {OVERLAY}, records them in {ROOT_DIR}/owned and adds its menu entry; hakchi keeps its uninstall list {HMOD_DIR}/uninstall-<name>"
                     + (f"; CCS Nucleus's start set by its own command (ccs-nucleus start {line}) when it differs" if line else "")
                     + (f"; this collection's games added to Original Menu as Hakchi game entries by this program's console program ({hk['program_dir']}/ccs-collections), "
                        f"which writes only the entries it records in its own journal ({HP.JOURNAL}) into hakchi's game storage and the index {HP.INDEX_PATH}" if hk["publish"] else ""),
            "requires": {"kernel": HAKCHI_KERNEL, "note": "hakchi's kernel must already be installed (its first installation is a separate step: the CCS Console Workbench's Prepare Console)"},
            "never_written": ["/usr (the stock games and system)", "/var/saves (the stock saves)",
                              "/etc and /var/lib/hakchi other than through hakchi's own pack_install of the add-ons this choice includes and the console lacks"
                              + (", the Hakchi game entries named below" if hk["publish"] else "") + " "
                              f"(their own files, menu entries and records), and the retirement of this program's earlier start-up module {LEGACY_NAME} (hakchi's pack_uninstall of its own list)",
                              "/bin, /sbin, /lib, /boot, /dev, /proc, /sys, /newroot",
                              "an add-on the console already has: never removed (Changing or removing add-ons is in CCS Console Workbench's Console Restore); "
                              "an older version of one this choice includes is updated by its own package's install",
                              f"CCS Nucleus's settings ({REGISTRY}) other than through its own start command",
                              "hakchi's game storage and the console menu's game folders other than the Hakchi game entries and folders this program's console program "
                              "added and records (another game, folder or entry, Hakchi's own included, is kept as it is; an entry with the same code is never replaced)",
                              f"anything under {ROOT_DIR} that is not this program's by its records (kept as it is and reported)"]
                             + ([] if home else [f"CCS HOME's copies, their pointer {POINTER} and the saves folder {DATA_DIR}: nothing of CCS HOME is installed, and a CCS HOME already on the console is kept as it is"]),
            "steps": ["preflight", "record", "stage", "readback"] + (["switch"] if home else []) + ["addons"] + (["start"] if line else []) + ["verify"] + (["hakchi"] if hk["publish"] else []) + ["retire", "tidy", "finish"]}
    if home: plan["earlier_home_versions"] = SM.earlier_keep(earlier if earlier is not None else earlier_setting())
    room = space_account(plan, list(pk), bool(hk["publish"]), False, None)   # the room it needs on a console that has none of it yet
    plan["needs_free_bytes"] = room["peak_bytes"]
    plan["space"] = {k: room[k] for k in ("rows", "logical_bytes", "allocated_bytes", "block_bytes", "block_source", "saves_allowance_bytes", "margin_bytes", "steady_bytes", "peak_bytes")}
    plan["hash"] = _sha(_canon(plan)); return plan

def space_items(plan: dict, add, hakchi: bool, same: bool) -> list:
    """what this plan writes to the console's data storage, in order, for the shared storage accounting: the package's or games copy's files
    (nothing when this exact copy is already there), each add-on package's files in hakchi's overlay, the Hakchi games' entries (their
    room as hakchi_plan works it out: staged and then written). The copy it replaces stays on the console beside it."""
    items = [] if same else [{"category": "home" if plan["delivery"] == "home" else "game-files", "label": "CCS HOME's package" if plan["delivery"] == "home" else "the games copy",
                              "files": [{"bytes": f["size"]} for f in plan["files"]]}]
    for a in add:
        p = plan["packages"][a]; items.append({"category": "addon", "label": p["label"], "files": [{"bytes": b} for b in p.get("overlay_sizes") or [p["overlay_bytes"]]]})
    if hakchi and plan["hakchi"].get("publish"):
        b = plan["hakchi"]["bytes"]; items.append({"category": "hakchi-games", "label": "the Hakchi games", "estimate": {"logical_bytes": b, "allocated_bytes": b},
                                                    "what": "their entries staged and then written, the index twice and a block for each entry"})
    return items

def space_account(plan: dict, add, hakchi: bool, same: bool, storage: dict | None, profile: str = PROFILE) -> dict:
    """the shared storage accounting (storage_model.account) for this plan on a console of this model (profile): its room for saves and
    safety margin from the shared storage specification; storage None uses the model's dated reference"""
    return SM.account({"profile": profile, "storage": storage, "items": space_items(plan, add, hakchi, same)})

def space_refusal(room: dict) -> str:
    mb = lambda n: f"{n / 1e6:.1f} MB"
    if room["status"] == "over": return f"not enough room on the console: {mb(room['available_bytes'])} available, {mb(room['steady_bytes'])} needed (with room kept for saves and a safety margin)"
    if room["status"] == "over-at-peak": return f"not enough room on the console while the new copy is beside the old one: {mb(room['available_bytes'])} available, {mb(room['peak_bytes'])} needed"
    return "the room needed on the console cannot be confirmed: " + "; ".join(room["unknown"]) + " is not known"

def save_plan(plan: dict, folder: str) -> str:
    """written once, named by its hash; an existing plan file of that name is never rewritten"""
    os.makedirs(folder, exist_ok=True); p = os.path.join(folder, f"plan-{plan['hash']}.json")
    if not os.path.exists(p):
        with open(p + ".tmp", "wb") as f: f.write(_canon(plan))
        os.replace(p + ".tmp", p)
    return p
def load_plan(path: str) -> dict:
    plan = json.loads(_read(path)); h = plan.pop("hash", None)
    if h != _sha(_canon(plan)) or not os.path.basename(path).endswith(f"{h}.json"): raise PlanError("the plan file changed after it was written")
    if plan.get("schema") != PLAN_SCHEMA: raise PlanError("the plan was made by another version of Classic Cart Studio (prepare it again)")
    plan["hash"] = h; return plan

def _writable(path: str) -> bool:   # the roots themselves and what is under them (the worker's ConsolePath.Write)
    return isinstance(path, str) and path.startswith("/") and any(path == r.rstrip("/") or path.startswith(r) for r in WRITE_ROOTS) and "/../" not in path + "/" and "/./" not in path + "/" and "//" not in path

# ---------------------------------------------------------------- the disposable stand-in
class FakeConsole:
    """a disposable folder standing in for a console that runs hakchi. The same operations as the console worker (console_engine/
    worker_console.py, ccs-console-worker): identity and free space from console.json; console links emulated in .fake-links.json (resolved in
    the middle of a path as the kernel does, never at the last name, which the operations report as a link); writes only under /var/lib/ccs
    and /tmp/ccs, a new file never over an existing name, a removal only of a regular file with the expected SHA-256, the pointer replaced
    in one rename; hakchi's pack_install / pack_uninstall mirrored (its overlay and uninstall list) with what the add-ons' own install and
    removal scripts do to each other and to CCS Nucleus's registry, and CCS Nucleus's own start command (the worker's nucleus-start).
    Faults (tests; each counts the console-changing calls): {"before": n} the n-th change fails before it happens (a lost connection),
    {"after": n} it happens and then the call fails, {"crash_after": n} it happens and the process dies (BaseException), {"corrupt": n} the
    n-th file written gets one byte flipped, {"no_space_after": bytes}, {"swap_after": n} after the n-th change another console answers
    (its identity changes), {"read_fail_at": k} the k-th chunk read loses the connection, {"grow_on_read": k} the file being read grows by
    one byte after the k-th chunk (changed during a read).
    durable=True keeps what the last sync made persistent: power_cut() puts the stand-in back to that state (a power loss that kept nothing
    written since; a crash without a power loss keeps everything, as the plain stand-in does)"""
    simulated = True; kind = "fake"
    def __init__(self, root: str, faults: dict | None = None, durable: bool = False):
        self.root = os.path.abspath(root); self.faults = dict(faults or {}); self.writes: list[str] = []; self.written_bytes = 0; self.nchanges = 0
        self.info = json.loads(_read(os.path.join(self.root, "console.json"))); self.lpath = os.path.join(self.root, ".fake-links.json")
        self.syncs: list[int] = []; self.nreads = 0; self.durable = durable; self._snap = self._snapshot() if durable else None
    # links
    def _links(self) -> dict: return json.loads(_read(self.lpath)) if os.path.isfile(self.lpath) else {}
    def _save_links(self, m: dict):
        with open(self.lpath + ".tmp", "w", encoding="utf-8") as f: json.dump(m, f)
        os.replace(self.lpath + ".tmp", self.lpath)
    def link(self, path: str, target: str):
        """set-up helper for tests (the installer never makes a link): a console symbolic link at path"""
        m = self._links(); m[path] = target; self._save_links(m)
    def _resolve_dir(self, path: str) -> str:   # every part but the last, through links (as the kernel does)
        parts = [p for p in path.split("/") if p]; m = self._links(); cur = ""
        for i, part in enumerate(parts[:-1]):
            cur = cur + "/" + part
            for _ in range(8):
                if cur in m: t = m[cur]; cur = t if t.startswith("/") else (cur.rsplit("/", 1)[0] + "/" + t)
                else: break
        return cur + "/" + parts[-1] if parts else "/"
    def p(self, path: str) -> str:
        """the local file for a console path (through links in the middle; also used by tests to place things)"""
        if not isinstance(path, str) or not path.startswith("/") or "/../" in path + "/" or "/./" in path + "/": raise ValueError(f"not a plain console path: {path}")
        q = os.path.normpath(os.path.join(self.root, self._resolve_dir(path).lstrip("/")))
        if not (q + os.sep).startswith(self.root + os.sep) and q != self.root: raise ValueError(f"outside the console: {path}")
        return q
    def _is_link(self, path: str) -> str | None: return self._links().get(self._resolve_dir(path))
    # reading
    def identity(self, fresh: bool = False) -> dict:
        if fresh: self.info = json.loads(_read(os.path.join(self.root, "console.json")))
        return {k: self.info.get(k) for k in IDENTITY_KEYS} | {"simulated": True}
    def free_bytes(self, path: str) -> int: return int(self.info.get("free_bytes", 0)) - self.written_bytes
    def stat(self, path: str) -> dict:
        t = self._is_link(path)
        if t is not None: return {"type": "link", "target": t}
        q = self.p(path)
        if os.path.isfile(q): return {"type": "file", "size": os.path.getsize(q)}
        if os.path.isdir(q): return {"type": "dir"}
        return {"type": "other" if os.path.lexists(q) else "none"}
    def exists(self, path: str) -> bool: return self.stat(path)["type"] != "none"
    def close(self): pass
    def readlink(self, path: str) -> str | None: return self._is_link(path)
    def listdir(self, path: str) -> list[str]:
        if self.stat(path)["type"] != "dir": return []
        real = self._resolve_dir(path); names = set(os.listdir(self.p(path)))
        names |= {k.rsplit("/", 1)[1] for k in self._links() if k.rsplit("/", 1)[0] == real}
        return sorted(n for n in names if not (path == "/" and n in ("console.json", ".fake-links.json", ".fake-links.json.tmp")))
    def sha256(self, path: str) -> str | None:
        if self.stat(path)["type"] != "file": return None
        return _sha(_read(self.p(path)))
    def menu_refs(self, names: list[str]) -> dict:
        """the worker's menu-refs: the .desktop files under MENU_ROOTS that name one of names ("pkg/COPY" or "games/COPY") in a line
        ("ccs/NAME"). The stand-in's links (its link table) are never walked: an entry that is a link is read through it. Any other link
        matters where the console's menus list games or on the way there (games/ under a root, hakchi's rootfs/usr/share/games): a link to
        a folder whose real place is inside a root read here, or inside the console's system image (/var/squashfs), is accepted; any other
        folder link there, or a link that leads nowhere, is reported in "linked"; a link to a file holds no entries. A root that is a link
        is not read. Each of these makes the answer not complete. Faults: {"menu_unreadable": console path} that entry cannot be read; {"menu_refs_fail":
        True} the session cannot list them"""
        if self.faults.get("menu_refs_fail"): raise OSError("bad-command: unknown command")
        if not names or any(not re.match(r"^(pkg|games)/[0-9a-f]{16}\.[0-9a-f]{8}$", str(n)) for n in names): raise ValueError("names: pkg/COPY or games/COPY")
        roots, refs, unreadable, linked = {}, [], [], []
        def check(cp, b):
            lines = b.decode("utf-8", "replace").splitlines()
            for n in names:
                if any(f"ccs/{n}" in l for l in lines): refs.append({"name": n, "sha256": _sha(b), "path": cp})
        for root in MENU_ROOTS:
            if self._is_link(root) is not None: roots[root] = "link"; continue
            q = self.p(root)
            if not os.path.lexists(q): roots[root] = "absent"; continue
            if not os.path.isdir(q): roots[root] = "other"; continue
            roots[root] = "listed"
            for d, dirs, files in os.walk(q):
                dirs.sort()
                for fn in sorted(files):
                    if not fn.endswith(".desktop"): continue
                    cp = "/" + os.path.relpath(os.path.join(d, fn), self.root).replace(os.sep, "/")
                    if self.faults.get("menu_unreadable") == cp: unreadable.append(cp); continue
                    check(cp, _read(os.path.join(d, fn)))
        listed = [r for r, v in roots.items() if v == "listed"]
        for lp, target in sorted(self._links().items()):   # the stand-in's links inside a root that is read
            if not any(lp.startswith(r + "/") for r in listed): continue
            tp = target if target.startswith("/") else lp.rsplit("/", 1)[0] + "/" + target
            tp = "/" + "/".join(x for x in os.path.normpath(tp).replace(os.sep, "/").split("/") if x)
            try: q = self.p(tp)
            except ValueError: q = None
            r0 = next(r for r in listed if lp.startswith(r + "/"))
            menu = any(lp == r0 + x or lp.startswith(r0 + x + "/") for x in ("/games", "/rootfs/usr/share/games")) or lp in (r0 + "/rootfs", r0 + "/rootfs/usr", r0 + "/rootfs/usr/share")
            if lp.endswith(".desktop"):
                if q and os.path.isfile(q): check(lp, _read(q))
                else: unreadable.append(lp)
            elif not menu: continue   # not where the menus list games (hakchi's programs, libraries, settings)
            elif q and os.path.isdir(q):
                if not any(tp == r or tp.startswith(r + "/") for r in listed + ["/var/squashfs"]): linked.append(lp)
            elif not (q and os.path.exists(q)): linked.append(lp)
        return {"roots": roots, "refs": refs, "unreadable": unreadable, "linked": linked,
                "complete": not unreadable and not linked and all(v in ("listed", "absent") for v in roots.values())}
    def read(self, path: str, max_bytes: int = 1_000_000) -> bytes | None:
        if self.stat(path)["type"] != "file": return None
        b = _read(self.p(path)); return b if len(b) <= max_bytes else None
    def read_chunk(self, path: str, offset: int, length: int) -> dict | None:
        """the worker's chunked read: {"data", "file_size"} of one bounded piece (offset and length in 4,096-byte blocks); None when not a file"""
        if offset < 0 or offset % 4096 or length < 4096 or length > 262144 or length % 4096: raise ValueError("offset and length: whole 4,096-byte blocks, at most 262,144 bytes")
        if self.stat(path)["type"] != "file": return None
        self.nreads += 1
        if self.faults.get("read_fail_at") == self.nreads: raise ConnectionError(f"the console stopped answering while {path} was read")
        q = self.p(path)
        with open(q, "rb") as f: size = os.fstat(f.fileno()).st_size; f.seek(offset); d = f.read(length)
        if self.faults.get("grow_on_read") == self.nreads:
            with open(q, "ab") as f: f.write(b"+")
        return {"data": d, "file_size": size}
    def sync(self, drop_caches: bool = False):
        """the worker's sync: what was written becomes what a power loss keeps (durable stand-ins)"""
        self.syncs.append(self.nchanges)
        if self.durable: self._snap = self._snapshot()
    def _snapshot(self) -> dict:
        out = {}
        for d, dirs, files in os.walk(self.root):
            rd = os.path.relpath(d, self.root)
            for n in dirs: out[os.path.normpath(os.path.join(rd, n))] = None
            for n in files:
                r = os.path.normpath(os.path.join(rd, n))
                if r not in ("console.json",): out[r] = _read(os.path.join(d, n))
        return out
    def power_cut(self):
        """a power loss: the stand-in goes back to what the last sync kept (identity and test files outside it unchanged)"""
        if not self.durable: raise RuntimeError("power_cut needs a durable stand-in")
        for d, dirs, files in os.walk(self.root, topdown=False):
            for n in files:
                q = os.path.join(d, n)
                if os.path.relpath(q, self.root) != "console.json": os.remove(q)
            for n in dirs: os.rmdir(os.path.join(d, n))
        for r, b in sorted(self._snap.items(), key=lambda kv: kv[0].count(os.sep)):
            q = os.path.join(self.root, r)
            if b is None: os.makedirs(q, exist_ok=True)
            else:
                os.makedirs(os.path.dirname(q), exist_ok=True)
                with open(q, "wb") as f: f.write(b)
    # changing (each is one console-changing call; the faults count them)
    def _change(self, what: str, act):
        self.nchanges += 1; n = self.nchanges
        if self.faults.get("before") == n: raise ConnectionError(f"the console stopped answering before {what} (change {n})")
        r = act()
        if self.faults.get("swap_after") == n:
            info = dict(self.info, hwid="sim-SWAPPED")
            with open(os.path.join(self.root, "console.json"), "w", encoding="utf-8") as f: json.dump(info, f)
        if self.faults.get("after") == n: raise ConnectionError(f"the console stopped answering after {what} (change {n})")
        if self.faults.get("crash_after") == n: raise SystemExit(f"simulated crash after {what} (change {n})")
        return r
    def _may_write(self, path: str):
        if not _writable(path): raise PermissionError(f"refused: {path} is outside the places this program writes")
        root = next(r.rstrip("/") for r in WRITE_ROOTS if path == r.rstrip("/") or path.startswith(r)); parent = path.rsplit("/", 1)[0] or "/"
        chain, d = ([parent] if path == root else []), parent
        while path != root:   # every folder from the root down to the parent must be a real folder (the worker's Chain; a link would lead elsewhere)
            chain.insert(0, d)
            if d == root: break
            d = d.rsplit("/", 1)[0] or "/"
        for q in chain:
            if self.stat(q)["type"] != "dir": raise OSError(f"parent-not-folder: a folder on the way to {path} is missing or is a link")
    def _parent_real(self, path: str):
        parent = path.rsplit("/", 1)[0] or "/"
        if self.stat(parent)["type"] != "dir": raise OSError(f"{parent} is not a real folder")
    def put(self, path: str, data: bytes, mode: str = "0o644", source: str | None = None) -> str:   # source: the file on this computer (the worker streams it)
        self._may_write(path)
        if self.stat(path)["type"] != "none": raise FileExistsError(f"{path} already exists (a new file never replaces anything)")
        self._parent_real(path)
        nf = self.faults.get("no_space_after")
        if nf is not None and self.written_bytes + len(data) > nf: raise OSError(28, "No space left on device", path)
        def act():
            d = data
            if self.faults.get("corrupt") == self.nchanges and d: d = bytes([d[0] ^ 0xFF]) + d[1:]
            q = self.p(path)
            with open(q, "wb") as f: f.write(d)
            try: os.chmod(q, int(MODES.get(mode, "0644"), 8))
            except OSError: pass
            self.writes.append(path); self.written_bytes += len(d); return _sha(d)
        return self._change(f"writing {path}", act)
    def mkdir(self, path: str):
        self._may_write(path)
        if self.stat(path)["type"] != "none": raise FileExistsError(f"{path} already exists")
        self._parent_real(path); return self._change(f"creating {path}", lambda: (os.mkdir(self.p(path)), self.writes.append(path + "/")))
    def rmdir(self, path: str):
        self._may_write(path)
        if self.stat(path)["type"] != "dir": raise OSError(f"{path} is not a real folder")
        if self.listdir(path): raise OSError(f"{path} is not empty")
        return self._change(f"removing the folder {path}", lambda: (os.rmdir(self.p(path)), self.writes.append("-" + path + "/")))
    def rename(self, a: str, b: str):
        self._may_write(a); self._may_write(b)
        if self.stat(a)["type"] not in ("file", "dir"): raise OSError(f"{a} is not a real file or folder")
        if self.stat(b)["type"] != "none": raise FileExistsError(f"{b} already exists")
        return self._change(f"renaming {a}", lambda: (os.rename(self.p(a), self.p(b)), self.writes.append(b)))
    def remove(self, path: str, sha256: str) -> bool:
        self._may_write(path)
        if self.stat(path)["type"] != "file" or self.sha256(path) != sha256: return False
        return self._change(f"removing {path}", lambda: (os.remove(self.p(path)), self.writes.append("-" + path), True)[-1])
    def set_pointer(self, path: str, value: str, expect: str | None = None):
        """replaced in one rename; expect ("absent" or the SHA-256 of the file there now) is checked in the same step (the worker's pointer
        command checks it in the same shell command): anything else there is left as it is (PointerChanged)"""
        self._may_write(path)
        if not COPY_RE.match(value): raise ValueError("not a copy name")
        if self.stat(path)["type"] not in ("none", "file"): raise OSError(f"{path} is not a plain file; it is left as it is")
        if expect is not None and (self.stat(path)["type"] != "none" if expect == "absent" else self.sha256(path) != expect): raise PointerChanged(f"{path} is not what was checked; it is left as it is")
        def act():
            q = self.p(path)
            with open(q + ".new", "wb") as f: f.write((value + "\n").encode())
            os.replace(q + ".new", q); self.writes.append(path)
        return self._change(f"switching {path}", act)
    # hakchi's own module handling (b0030_package: pack_install copies the module's files into its overlay and writes an uninstall list)
    def _ov(self, path: str) -> str: return os.path.join(self.root, "var/lib/hakchi/rootfs", path.lstrip("/"))
    def _ulist(self, name: str) -> str: return os.path.join(self.root, "var/lib/hakchi/hmod", f"uninstall-{name}")
    def pack_install(self, name: str, hmod):
        """hakchi's pack_install of a package (b0030_package): its files (all but install, uninstall and readme files) copied into $rootfs,
        its own uninstall script kept with hakchi's generated list after it ("rm -f" for every file, "rmdir --ignore-fail-on-non-empty" for
        every folder, deepest first). hmod: the .hmod's bytes, or (tests of the earlier start-up module only) {path: (bytes, mode)}. What the
        add-ons' own install scripts do here as on a console: CCS Nucleus's chooser refuses to install without CCS Nucleus (nothing is copied
        and hakchi keeps no list: the worker reports module-failed), and CCS Nucleus writes its first registry when there is no valid one (the
        console's own menu, started directly; CCS HOME offered when CCS Mod is there). Their checks, journals and menu entries are shown by
        tools/console-nucleus/test_nucleus.py under qemu-arm with hakchi's own code"""
        if name == ADDONS["nucleus-chooser"] and self.method_files(ADDONS["nucleus"]) is None:
            raise OSError(f"module-failed: {name} was not installed: result install refused CCS Nucleus is not installed (its chooser needs it)")
        if isinstance(hmod, dict): files, script = dict(hmod), b""
        else:
            import gzip, io, tarfile
            files, script = {}, b""
            with tarfile.open(fileobj=io.BytesIO(gzip.decompress(hmod))) as t:
                for m in t.getmembers():
                    n = m.name[2:] if m.name.startswith("./") else m.name
                    if not m.isfile(): continue
                    if n == "uninstall": script = t.extractfile(m).read(); continue
                    if n == "install" or n.lower().startswith("readme"): continue
                    files["/" + n] = (t.extractfile(m).read(), oct(m.mode & 0o777))
        def act():
            lst, dirs = [], set()
            for path in files:
                parts = path.strip("/").split("/")[:-1]; dirs |= {"/" + "/".join(parts[:i]) for i in range(1, len(parts) + 1)}
            made = sorted((d for d in dirs if not os.path.isdir(self._ov(d))), key=lambda d: (-d.count("/"), d))   # the folders this install makes
            for path, (b, mode) in files.items():
                q = self._ov(path); os.makedirs(os.path.dirname(q), exist_ok=True)
                with open(q, "wb") as f: f.write(b)
                lst.append(f'rm -f "$rootfs/.{path}"')
            lst += [f'rmdir --ignore-fail-on-non-empty "$rootfs/.{d}"' for d in sorted(dirs, key=lambda d: -d.count("/"))]
            u = self._ulist(name); os.makedirs(os.path.dirname(u), exist_ok=True)
            with open(u, "w", newline="\n") as f: f.write(script.decode() + "\n" + MADE_MARK + " ".join(made) + "\n# auto-generated\n" + "\n".join(lst) + "\n")
            if name == ADDONS["nucleus"]:   # its postinstall: its folders, and the first registry when there is no valid one
                for d in ("nucleus", "nucleus/state", "nucleus/staging"): os.makedirs(self.p(f"{ROOT_DIR}/{d}"), exist_ok=True)
                if self._registry() is None: self._registry_write(self._reg_first("native", "default", "never", "on" if self.method_files(ADDONS["mod"]) is not None else "off"))
            self.writes.append(f"hakchi:pack_install {name}")
        return self._change(f"hakchi pack_install {name}", act)
    def method_files(self, name: str) -> dict | None:
        """a CCS add-on package as the console holds it (the worker's module-files): None when hakchi does not list it; else its VERSION's
        package id and every file its own FILES list names, with the SHA-256 each has now (None: missing)"""
        if not os.path.isfile(self._ulist(name)): return None
        m = name[4:]; ver = self.overlay_read(f"/etc/ccs/{m}/VERSION"); fl = self.overlay_read(f"/etc/ccs/{m}/FILES")
        pkg = dict(l.split("=", 1) for l in ver.decode().splitlines() if "=" in l).get("package") if ver else None
        files = {"/" + l.split(" ", 2)[2]: self.overlay_sha256("/" + l.split(" ", 2)[2]) for l in (fl or b"").decode().splitlines() if l.count(" ") >= 2}
        if fl is not None: files[f"/etc/ccs/{m}/FILES"] = _sha(fl)
        return {"package": pkg, "files": files}
    def _made_dirs(self, name: str) -> list[str]:
        """the overlay folders a CCS package's install made (the stand-in notes them in hakchi's list for it, which goes with the package)"""
        u = self._ulist(name)
        for l in (_read(u).decode().splitlines() if os.path.isfile(u) else []):
            if l.startswith(MADE_MARK): return [d for d in l[len(MADE_MARK):].split(" ") if d]
        return []
    def pack_files(self, name: str) -> dict:
        u = self._ulist(name)
        if not os.path.isfile(u): return {}
        out = {}
        for l in _read(u).decode().splitlines():
            m = re.match(r'^rm -f "\$rootfs/\.?(/[^"]+)"$', l.strip())
            if m: q = self._ov(m.group(1)); out[m.group(1)] = _sha(_read(q)) if os.path.isfile(q) else None
        return out
    def overlay_sha256(self, path: str) -> str | None:
        q = self._ov(path); return _sha(_read(q)) if os.path.isfile(q) else None
    def overlay_read(self, path: str) -> bytes | None:
        q = self._ov(path); return _read(q) if os.path.isfile(q) else None
    # CCS Nucleus's registry, as its own commands on the console keep it (ccs-nucleus: reg_first, reg_edit, start, chooser-remove)
    def _registry(self) -> dict | None:
        q = self.p(REGISTRY); return parse_registry(_read(q)) if os.path.isfile(q) else None
    def _registry_write(self, lines: list[tuple[str, str]]):
        body = f"schema={REGISTRY_SCHEMA}\n" + "".join(f"{k}={v}\n" for k, v in lines); q = self.p(REGISTRY); os.makedirs(os.path.dirname(q), exist_ok=True)
        with open(q + ".new", "w", newline="\n") as f: f.write(body + f"end={_sha(body.encode())}\n")
        os.replace(q + ".new", q)
    def console_type(self) -> str:
        """ccs-nucleus's ccs_console_type on this stand-in (CCS HOME's option records it): the console's own software type and region by
        hakchi's softwareCheck rules (nes-usa, nes-eur, snes-usa, snes-eur), read from /var/squashfs/etc/clover, else /etc/clover; else the
        type the stand-in's console.json records; "unknown" when none of them is there"""
        for d in ("/var/squashfs/etc/clover", "/etc/clover"):
            q = self.p(d)
            if not os.path.isdir(q): continue
            def cat(n): f = os.path.join(q, n); return _read(f).decode("utf-8", "replace").rstrip("\n") if os.path.isfile(f) else ""
            kind = "snes" if cat("boardtype") == "dp-shvc" else "nes"
            issue = os.path.join(os.path.dirname(q), "issue")
            if os.path.isfile(issue) and b"dp-hvcj" in _read(issue): kind = "hvcj"
            return kind + "-" + {"JPN": "jpn", "EUR": "eur"}.get(cat("REGION"), "usa")
        return str(self.info.get("system") or "unknown")   # a stand-in without those files: the type its console.json records
    def home_option(self) -> list[tuple[str, str]]:
        return [("opt.ccs-home.kind", "ccs-home"), ("opt.ccs-home.label", "CCS HOME"), ("opt.ccs-home.package", "current"), ("opt.ccs-home.kernel", HAKCHI_KERNEL),
                ("opt.ccs-home.drivers", ""), ("opt.ccs-home.requires", "/var/lib/ccs/pkg/current"), ("opt.ccs-home.overlay", "hakchi"), ("opt.ccs-home.saves", "/var/lib/ccs/data"),
                ("opt.ccs-home.compat", self.console_type()), ("opt.ccs-home.fallback", "native"), ("opt.ccs-home.auto", "yes")]
    def _reg_first(self, default: str, selection: str, chooser: str, home: str) -> list[tuple[str, str]]:
        return ([("generation", "1"), ("default", default), ("selection", selection), ("chooser", chooser), ("timeout", "10"), ("options", "native ccs-home" if home == "on" else "native"),
                 ("opt.native.kind", "native"), ("opt.native.label", "Original Menu"), ("opt.native.overlay", "hakchi"), ("opt.native.saves", "/var/lib/clover/profiles/0"), ("opt.native.fallback", "-")]
                + (self.home_option() if home == "on" else []))
    def _reg_edit(self, edit) -> bool:
        """one edit of the registry in force (generation raised), put in force only when the result is a valid registry"""
        reg = self._registry()
        if reg is None: return False
        lines = [(k, str(reg["generation"] + 1) if k == "generation" else v) for k, v in edit(list(reg["lines"]))]
        body = f"schema={REGISTRY_SCHEMA}\n" + "".join(f"{k}={v}\n" for k, v in lines)
        if parse_registry((body + f"end={_sha(body.encode())}\n").encode()) is None: return False
        self._registry_write(lines); return True
    def _reg_set(self, key: str, value: str) -> bool:
        def ed(lines):
            out, done = [], False
            for k, v in lines:
                if k == key and not done: out.append((k, value)); done = True
                else: out.append((k, v))
            return out
        return self._reg_edit(ed)
    def nucleus_start(self, default: str, chooser: str, home: str, selection: str = "default", timeout: str = "10") -> str:
        """CCS Nucleus's own command "ccs-nucleus start DEFAULT CHOOSER HOME SELECTION TIMEOUT" (the worker's nucleus-start runs it on a
        console): its registry changed in place (CCS HOME's option added or removed, then the default, the selection, the countdown and the
        chooser at start), or its first registry when there is none; the console owner's own system images stay listed. Its one result line; like the
        worker, anything but its success raises"""
        timeout = str(timeout)
        if default not in STARTS or chooser not in ("always", "never") or home not in ("on", "off") or selection not in ("default", "last") or not re.match(r"^([0-9]|[1-9][0-9])$", timeout):
            raise OSError("bad-args: default: native or ccs-home; chooser: always or never; home: on or off; selection: default or last; timeout: 0 to 99")
        if self.method_files(ADDONS["nucleus"]) is None: raise OSError("not-installed: CCS Nucleus is not installed (hakchi does not list it)")
        if not os.path.isfile(self._ov(ADDON_PROGRAMS["nucleus"])): raise OSError("not-a-file: CCS Nucleus's program is not a plain file")
        def refuse(why: str): raise OSError(f"module-failed: CCS Nucleus's own command answered: result start {why}")
        if default == "ccs-home" and home == "off": refuse("refused CCS HOME is not offered")
        def act():
            if self._registry() is None:
                if os.path.lexists(self.p(REGISTRY)): refuse("refused the registry on the console is not valid (it is kept as it is)")
                self._registry_write(self._reg_first(default, selection, chooser, home))
                if timeout != "10": self._reg_set("timeout", timeout)
                self.writes.append("ccs-nucleus:start"); return f"result start ok first default={default} chooser={chooser} home={home} selection={selection} timeout={timeout}"
            reg = self._registry()
            if home == "on" and "ccs-home" not in reg["options"]:
                def add(lines): return [(k, " ".join(["native", "ccs-home"] + [o for o in v.split() if o != "native"]) if k == "options" else v) for k, v in lines] + self.home_option()
                if not self._reg_edit(add): refuse("failed adding CCS HOME")
            if home == "off" and "ccs-home" in self._registry()["options"]:
                if self._registry()["default"] == "ccs-home" and not self._reg_set("default", "native"): refuse("failed default")
                def drop(lines): return [(k, " ".join(o for o in v.split() if o != "ccs-home") if k == "options" else v) for k, v in lines if not k.startswith("opt.ccs-home.")]
                if not self._reg_edit(drop): refuse("failed removing CCS HOME")
            for k, v in (("default", default), ("selection", selection), ("timeout", timeout), ("chooser", chooser)):
                if str(self._registry()[k]) != v and not self._reg_set(k, v): refuse(f"failed {k}")
            self.writes.append("ccs-nucleus:start"); return f"result start ok default={default} chooser={chooser} home={home} selection={selection} timeout={timeout}"
        return self._change("ccs-nucleus start", act)
    def collections(self, command: str, stage: str | None = None) -> str:
        """the console program for Hakchi games, as the transport worker runs it (its "collections" command): only the pinned program and
        library in the folder named by the program's pin (else not-installed); a stage only under integration/stage/; its output (its
        lines and its result line, success or not). Its rules are hakchi_publish.emulate's (the program itself runs in
        tools/console-nucleus/test_nucleus.py); where, check and status read only, apply and remove are one console change each"""
        if command not in ("where", "check", "apply", "remove", "status"): raise OSError("bad-args: command: where, check, apply, remove or status")
        if command in ("check", "apply") and not (isinstance(stage, str) and re.match(r"^/var/lib/ccs/integration/stage/[0-9a-f]{8}$", stage)): raise OSError("bad-args: stage: a folder under /var/lib/ccs/integration/stage")
        d = f"{HP.INTEGRATION}/program-{HP.PROGRAM_PINS['ccs-collections'][:16]}"
        for n, s in HP.PROGRAM_PINS.items():
            if self.stat(f"{d}/{n}")["type"] != "file" or self.sha256(f"{d}/{n}") != s: raise OSError(f"not-installed: the console program for Hakchi games is not in {d} as pinned")
        if command in ("where", "check", "status"): return HP.emulate(self, command, stage)
        return self._change(f"ccs-collections {command}", lambda: (self.writes.append(f"ccs-collections:{command}"), HP.emulate(self, command, stage))[1])
    def pack_uninstall(self, name: str):
        """hakchi's pack_uninstall: the uninstall list sourced. A CCS add-on package's own script returns before hakchi's generated list and
        removes only its unchanged files (here: the files its FILES list names with those bytes). As on a console: CCS Nucleus's removal
        first takes its chooser's files when the chooser is still there (hakchi keeps listing the chooser) and keeps its own settings (the
        registry and its folders); the chooser's removal, run by CCS Nucleus's program, leaves CCS Nucleus's settings without the chooser at
        start (a default that was one of the console owner's system images becomes the console's own menu). The earlier module's list removes its file"""
        mf = self.method_files(name) if name in ADDONS.values() else None
        if mf is not None:
            def unchanged(n: str) -> dict:
                m = n[4:]; fl = self.overlay_read(f"/etc/ccs/{m}/FILES") or b""
                return {"/" + l.split(" ", 2)[2]: l.split(" ", 1)[0] for l in fl.decode().splitlines() if l.count(" ") >= 2} | {f"/etc/ccs/{m}/FILES": _sha(fl)}
            want = unchanged(name)
            chooser_files = unchanged(ADDONS["nucleus-chooser"]) if name == ADDONS["nucleus"] and self.method_files(ADDONS["nucleus-chooser"]) is not None else {}
            runs = name != ADDONS["nucleus-chooser"] or os.path.isfile(self._ov(ADDON_PROGRAMS["nucleus"]))   # the chooser's removal is CCS Nucleus's own command
            def act_ccs():
                if name in (ADDONS["nucleus"], ADDONS["nucleus-chooser"]) and runs and (chooser_files or name == ADDONS["nucleus-chooser"]) and self._registry() is not None:
                    reg = self._registry()
                    if reg["kinds"][reg["default"]] == "owner-root": self._reg_set("default", "native")
                    self._reg_set("chooser", "never")
                for path, s in (list(chooser_files.items()) + list(want.items()) if runs else []):
                    q = self._ov(path)
                    if os.path.isfile(q) and _sha(_read(q)) == s: os.remove(q)
                # the folders the package's own install made, when left empty (on a console CCS's own removal keeps even those; a folder that
                # was there before, such as hakchi's own etc/init.d, always stays)
                for d in sorted(set(self._made_dirs(name) + (self._made_dirs(ADDONS["nucleus-chooser"]) if chooser_files else [])), key=lambda d: (-d.count("/"), d)):
                    q = self._ov(d)
                    if runs and os.path.isdir(q) and not os.listdir(q): os.rmdir(q)
                os.remove(self._ulist(name)); self.writes.append(f"hakchi:pack_uninstall {name}")
            return self._change(f"hakchi pack_uninstall {name}", act_ccs)
        def act():
            for path in list(self.pack_files(name)):
                q = self._ov(path)
                if os.path.isfile(q): os.remove(q)
            u = self._ulist(name)
            for l in (_read(u).decode().splitlines() if os.path.isfile(u) else []):
                m = re.match(r'^rmdir --ignore-fail-on-non-empty "\$rootfs/\.?(/[^"]+)"$', l.strip())
                if m and os.path.isdir(self._ov(m.group(1))) and not os.listdir(self._ov(m.group(1))): os.rmdir(self._ov(m.group(1)))
            if os.path.isfile(u): os.remove(u)
            self.writes.append(f"hakchi:pack_uninstall {name}")
        return self._change(f"hakchi pack_uninstall {name}", act)

def open_console(kind: str, target=None):
    """a disposable stand-in ("fake"), or a real console (ssh, clovershell) through the console transport worker
    (worker_console.open_worker_console), asked for the install tier; the worker connects only through those two transports"""
    if kind == "fake": return FakeConsole(target)
    if kind not in ("ssh", "clovershell"): raise ValueError(f"unknown console transport {kind!r}: a console is reached through clovershell or SSH")
    gate.require("install", f"open a {kind} console")
    from .worker_console import open_worker_console
    return open_worker_console(kind)

# ---------------------------------------------------------------- records on the console
def _record_bytes(obj: dict) -> bytes: return (json.dumps(obj, sort_keys=True, indent=1) + "\n").encode()
def read_records(c) -> dict:
    """this program's records on the console: {"versions": {copy: record}, "modules": {sha16: record}, "attempts": {id: record},
    "problems": [...]} (a record that cannot be read is a problem, never ownership)"""
    out = {"versions": {}, "games": {}, "modules": {}, "attempts": {}, "collections": {}, "problems": [], "files": {}}
    st = c.stat(INST_DIR)["type"]
    if st == "none": return out
    if st != "dir": out["problems"].append(f"{INST_DIR} is not a real folder"); return out
    for name in c.listdir(INST_DIR):
        path = f"{INST_DIR}/{name}"; b = c.read(path, 1_000_000)
        try: rec = json.loads(b) if b is not None else None
        except ValueError: rec = None
        if not isinstance(rec, dict): out["problems"].append(f"{path} cannot be read as a record"); continue
        out["files"][path] = _sha(b)
        m = re.match(r"^v-([0-9a-f]{16}\.[0-9a-f]{8})\.json$", name)
        if m and rec.get("schema") == "ccs-owned-copy/1" and rec.get("copy") == m.group(1): out["versions"][m.group(1)] = rec | {"_record": path}; continue
        m = re.match(r"^games-([0-9a-f]{16}\.[0-9a-f]{8})\.json$", name)
        if m and rec.get("schema") == GO.RECORD and rec.get("copy") == m.group(1) and isinstance(rec.get("files"), dict): out["games"][m.group(1)] = rec | {"_record": path}; continue
        m = re.match(r"^module-([0-9a-f]{16})\.json$", name)
        if m and rec.get("schema") in ("ccs-owned-module/1", "ccs-owned-module/2") and str(rec.get("sha256", "")).startswith(m.group(1)): out["modules"][m.group(1)] = rec | {"_record": path}; continue
        m = re.match(r"^attempt-([0-9a-f]{8})\.json$", name)
        if m and rec.get("schema") == "ccs-install-attempt/1" and rec.get("attempt") == m.group(1): out["attempts"][m.group(1)] = rec | {"_record": path}; continue
        m = re.match(r"^collections-([0-9a-f]{16})\.json$", name)
        if m and rec.get("schema") == COLLECTIONS_RECORD and rec.get("dir") == f"{HP.INTEGRATION}/program-{m.group(1)}" and isinstance(rec.get("files"), dict):
            out["collections"][m.group(1)] = rec | {"_record": path}; continue
        out["problems"].append(f"{path} is not one of this program's records")
    return out

def owned_module_shas(recs: dict) -> set: return {r["sha256"] for r in recs["modules"].values()} | {module_sha()}

def projection_id(projection: dict, copy: str) -> str:
    """what one publication of Hakchi games is: the plan's projection for one installed copy (the index carries it: an index naming the
    same is the same publication)"""
    return _sha(_canon(projection) + b"\0" + copy.encode())[:32]

def hakchi_state(c, recs: dict) -> dict:
    """the console's Hakchi games as this program can read them (reads only): {"program": this installation's console program is there
    (its record and its files, unchanged), "journal": the program's own journal is there, "index_sha256", "index": the v1 index when it is
    one this program would write, "generation"}"""
    pins = HP.PROGRAM_PINS; k16 = pins["ccs-collections"][:16]; rec = recs.get("collections", {}).get(k16)
    d = f"{HP.INTEGRATION}/program-{k16}"
    prog = bool(rec) and all(c.sha256(f"{d}/{n}") == s for n, s in pins.items()) and (rec.get("files") or {}) == {f"{d}/{n}": s for n, s in pins.items()}
    t = c.stat(HP.INDEX_PATH)["type"]; ib = c.read(HP.INDEX_PATH, 64_000_000) if t == "file" else None
    idx = HP.parse_index(ib)
    return {"program": prog, "journal": c.stat(HP.JOURNAL)["type"] != "none", "index_sha256": _sha(ib) if ib is not None else None, "index_type": t,
            "index": idx, "generation": (idx or {}).get("generation", 0)}

def legacy_state(c, recs: dict) -> str:
    """the earlier start-up module: "absent", "mine" (hakchi lists ccs-home with exactly its one file, which is gone (hakchi's own start removed
    it) or still this program's recorded bytes), or "foreign" (kept, and nothing is changed)"""
    mod = c.pack_files(LEGACY_NAME)
    if not mod: return "absent"
    return "mine" if set(mod) == {LEGACY_FILE} and (mod[LEGACY_FILE] is None or mod[LEGACY_FILE] in owned_module_shas(recs)) else "foreign"

def installed_addons(c) -> list[dict]:
    """each CCS add-on package hakchi lists on the console, in install order: {"name", "addon", "package", "state"}: "current" (this
    installation's package, every file unchanged), "changed" (its files were changed since), "older" (another version of the add-on's CCS
    package, by its own VERSION record: its own removal takes only its own unchanged files), "present" (named by its own record; this
    installation cannot build that package to compare it), or "not this program's" (no package record: kept)"""
    out = []
    for a, n in ADDONS.items():
        mf = c.method_files(n)
        if mf is None: continue
        pid = mf.get("package")
        if not re.match(r"^[0-9a-f]{64}$", str(pid or "")): st = "not this program's"
        else:
            try: ap = addon_package(a)
            except ChooserUnavailable: ap = None
            if ap is None: st = "present"
            elif pid == ap["package_id"]: st = "current" if all((mf.get("files") or {}).get(p) == s for p, s in ap["files"].items()) else "changed"
            else: st = "older"
        out.append({"name": n, "addon": a, "package": pid, "state": st})
    return out

def studio_installed(recs: dict) -> set:
    """the add-on packages this program's records say it installed, as (name, package id)"""
    return {(r.get("name"), r.get("package_id")) for r in recs["modules"].values() if r.get("schema") == "ccs-owned-module/2" and r.get("name") in ADDONS.values()}

def record_gone(c, rec: dict) -> bool:
    """a module record no longer describes anything on the console: the earlier start-up module's, once hakchi lists none of it; an add-on
    package's, once hakchi does not list that exact package (it was removed, or another version replaced it)"""
    if rec.get("schema") == "ccs-owned-module/1" or rec.get("name") == LEGACY_NAME: return not c.pack_files(LEGACY_NAME)
    mf = c.method_files(str(rec.get("name") or "")) if rec.get("name") in ADDONS.values() else None
    return mf is None or mf.get("package") != rec.get("package_id")

def copy_state(c, copy: str, rec: dict, where: str = PKG_DIR) -> dict:
    """a copy's files against its record: ours (recorded and unchanged), changed (recorded, bytes differ), missing, unknown (not recorded).
    where: CCS HOME's copies (pkg) or the games copies"""
    base = f"{where}/{copy}"; files = rec.get("files") or {}; ours, changed, missing = [], [], []
    hashes = {rel: c.sha256(f"{base}/{rel}") for rel in files}
    for rel, h in files.items():
        if hashes[rel] == h: ours.append(rel)
        elif hashes[rel] is None and c.stat(f"{base}/{rel}")["type"] == "none": missing.append(rel)
        else: changed.append(rel)
    unknown = []
    def walk(rel):
        for n in c.listdir(f"{base}/{rel}" if rel else base):
            r = f"{rel}/{n}" if rel else n; t = c.stat(f"{base}/{r}")["type"]
            if t == "dir": walk(r)
            elif r not in files: unknown.append(r + ("" if t == "file" else f" ({t})"))
    if c.stat(base)["type"] == "dir": walk("")
    return {"ours": ours, "changed": changed, "missing": missing, "unknown": unknown}

class RemovalFailed(OSError):
    """a removal that failed part way: the console did not confirm the change at path (lost before it acted, or after it may have acted:
    from here it cannot be told which), after removed_files of the copy's files were removed"""
    def __init__(self, copy: str, path: str, removed_files: int, cause: BaseException):
        self.copy, self.path, self.removed_files, self.cause = copy, path, removed_files, cause
        super().__init__(f"{path}: {cause}")

def remove_copy(c, copy: str, rec: dict, journal=None, where: str = PKG_DIR) -> dict:
    """a copy removed file by file: only its recorded, unchanged files; then its folders that are empty, deepest first; its record only
    when the folder is gone. Everything else is kept and reported. where: CCS HOME's copies (pkg) or the games copies. A change the
    console does not confirm raises RemovalFailed (an OSError) with the path and what was removed before it"""
    base = f"{where}/{copy}"; files = rec.get("files") or {}; removed = 0; path = base
    try:
        for rel, h in sorted(files.items()):
            path = f"{base}/{rel}"
            if c.stat(path)["type"] != "file": continue   # not there (its folder may be missing too), or not a file: kept as it is
            if journal: journal(op="remove-file", path=path, sha256=h)
            if c.remove(path, h): removed += 1
        dirs = sorted({"/".join(rel.split("/")[:i]) for rel in files for i in range(1, rel.count("/") + 1)}, key=lambda d: -d.count("/"))
        for d in dirs + [""]:
            path = f"{base}/{d}" if d else base
            if c.stat(path)["type"] == "dir" and not c.listdir(path):
                if journal: journal(op="rmdir", path=path)
                c.rmdir(path)
        path = base; gone = c.stat(base)["type"] == "none"; st = None if gone else copy_state(c, copy, rec, where); kept = [] if gone else [f"{base}/{x}" for x in st["changed"] + st["unknown"]]
        rp = rec.get("_record")
        if gone and rp:
            path = rp; b = c.read(rp, 1_000_000)
            if b is not None:
                if journal: journal(op="remove-record", path=rp, sha256=_sha(b))
                c.remove(rp, _sha(b))
    except RemovalFailed: raise
    except (ConnectionError, OSError) as e: raise RemovalFailed(copy, path, removed, e) from e
    return {"copy": copy, "removed_files": removed, "removed": gone, "kept": kept}

# ---------------------------------------------------------------- what the console's menus name
MENU_ROOTS = ("/var/lib/hakchi", "/media/hakchi")   # hakchi's folders on the data storage and on a USB drive or SD card (the worker's menu-refs)
NAMED_YES = "something on the console still names it"   # storage_model.retention's own words for a referenced copy
NAMED_UNKNOWN = ("an entry of the console's menu may name it: not every entry could be read now (for example one on a USB drive or SD card "
                 "that is not connected)")

def own_entries(c) -> dict | None:
    """the files the console program recorded as its own (lib.sh's ownership journal: path -> SHA-256 when written); {} when there is
    none; None when it cannot be read"""
    t = c.stat(HP.JOURNAL)["type"]
    if t == "none": return {}
    b = c.read(HP.JOURNAL, 4_000_000) if t == "file" else None
    if b is None: return None
    out = {}
    for line in b.decode("utf-8", "replace").splitlines():
        if line.startswith("file="):
            parts = line[5:].split(" ", 2)
            if len(parts) == 3 and re.match(r"^[0-9a-f]{64}$", parts[0]): out[parts[2]] = parts[0]
    return out

def menu_names(c, names: list[str], rewritten: bool = False) -> dict:
    """whether an entry of the console's menus names each of this program's copies, by the console's own entries now (reads only).
    names: "pkg/COPY" (a CCS HOME copy) or "games/COPY" (a games copy). Returns {"named": {name: "yes" | "no" | "unknown"}, "entries":
    {name: [paths]}, "why": what could not be read (or None)}. rewritten: for a review before this installation runs, when it rewrites or
    removes the console program's own entries; those, still exactly as it wrote them, are left out (afterwards they name the new copy or
    are gone). Everything else counts: an entry of another program, a changed one, one on a USB drive or SD card. "unknown" when the
    entries could not all be read: a folder or entry that cannot be read, a root that is a link, an answer that cannot be told apart, the
    console program's record that cannot be read, or entries it recorded on a USB drive or SD card that cannot be read now"""
    names = sorted(set(names)); named = {n: "no" for n in names}; entries = {n: [] for n in names}
    if not names: return {"named": named, "entries": entries, "why": None}
    try: r = c.menu_refs(names)
    except ConnectionError: raise
    except (OSError, AttributeError) as e:   # a session that cannot list the menu entries (an older worker): nothing is known
        return {"named": {n: "unknown" for n in names}, "entries": entries, "why": f"the console's menu entries could not be listed ({e})"}
    own = own_entries(c); why = None
    if not r.get("complete"):
        why = "not every menu entry on the console could be read" + (f" ({', '.join((r.get('unreadable') or [])[:3])})" if r.get("unreadable") else "")
        if r.get("linked"): why += f"; a folder link leads where the entries are not read ({', '.join(r['linked'][:3])})"
    if own is None: why = why or f"{HP.JOURNAL} cannot be read"
    elif any(p.startswith("/media/") for p in own) and (r.get("roots") or {}).get("/media/hakchi") != "listed":
        why = why or "Classic Cart Studio's games were added to a menu on a USB drive or SD card that cannot be read now"
    for x in r.get("refs") or []:
        n, p = x.get("name"), x.get("path")
        if n not in named: continue
        if rewritten and own and own.get(p) == x.get("sha256"): continue   # the console program's own entry: this installation rewrites it
        named[n] = "yes"; entries[n].append(p)
    if why:
        for n in names:
            if named[n] == "no": named[n] = "unknown"
    return {"named": named, "entries": entries, "why": why}

RESTORE_EARLIER_TEXT = ("The backup made before this installation keeps every file of this copy: CCS Console Workbench's Console Restore can put "
                        "them back on this console.")

def _copy_label(c, v: str) -> dict:
    """a copy's collection name, games and logical size from its own MANIFEST.json (unknown when it cannot be read)"""
    b = c.read(f"{PKG_DIR}/{v}/MANIFEST.json", 4_000_000)
    try: m = json.loads(b) if b else {}
    except ValueError: m = {}
    return {"label": m.get("label") if isinstance(m.get("label"), str) else None, "games": m.get("games") if isinstance(m.get("games"), int) else None,
            "bytes": m.get("total_bytes") + len(b) if isinstance(m.get("total_bytes"), int) and b else None}

def _named_words(ret: dict, named: dict, prefix: str) -> dict:
    """the shared rule's reason for a copy an entry only may name says so (the rule's own words are for an entry that does)"""
    for k, w in list((ret.get("why") or {}).items()):
        if w == NAMED_YES and named.get(prefix + k, "unknown") == "unknown": ret["why"][k] = NAMED_UNKNOWN
    return ret

def earlier_review(c, recs: dict, current: str, prior: str | None, keep, named: dict | None = None) -> dict:
    """the earlier CCS HOME versions as they would be after this installation, for its review: each recorded copy with its collection
    name, games, logical size and when it was installed (its record's installed_utc; the space it takes is not measured here: unknown),
    which one is in use now and which is the new one, whether an entry of the console's menus names it (named: menu_names()' "named";
    a copy not in it counts as maybe named), the ones the setting keeps and the ones that would go (each is checked file by file, and
    against the menu entries again, before it goes)"""
    named = named or {}
    copies = [{"copy": v, "owned": True, "unchanged": True, "referenced": v != current and named.get("pkg/" + v, "unknown") != "no", "previous": r.get("previous")} for v, r in recs["versions"].items()]
    if current not in recs["versions"]: copies.append({"copy": current, "owned": True, "unchanged": True})
    ret = _named_words(SM.retention({"keep": keep, "current": current, "prior": prior, "copies": copies}), named, "pkg/")
    ret["copies"] = [{"copy": v, "in_use": v == current, "in_use_now": v == prior, "new": v == current and v not in recs["versions"], "allocated_bytes": None,
                      "installed_utc": (recs["versions"].get(v) or {}).get("installed_utc") if isinstance((recs["versions"].get(v) or {}).get("installed_utc"), str) else None,
                      "named": None if v == current else named.get("pkg/" + v, "unknown"),
                      **(_copy_label(c, v) if v in recs["versions"] else {"label": None, "games": None, "bytes": None})} for v in sorted({x["copy"] for x in copies})]
    return ret

def earlier_cleanup(c, recs: dict, current: str, prior: str | None, keep, named: dict | None = None) -> dict:
    """after the switch is read back: every earlier copy's files checked against its record and whether an entry of the console's menus
    names it (named: menu_names()' "named", read after this collection's entries were written; a copy not in it counts as maybe named),
    then the shared retention rule decides (storage_model.retention): only an unchanged copy of this program's that nothing names can go,
    the most recent earlier versions stay. kept_files: the changed or unknown files of each copy kept because it changed (reported to the
    person, never removed)"""
    named = named or {}; copies, kept_files = [], []
    for v, r in recs["versions"].items():
        st = copy_state(c, v, r) if v != current else {"changed": [], "missing": [], "unknown": []}
        copies.append({"copy": v, "owned": True, "unchanged": not (st["changed"] or st["missing"] or st["unknown"]), "referenced": v != current and named.get("pkg/" + v, "unknown") != "no", "previous": r.get("previous")})
        kept_files += [f"{PKG_DIR}/{v}/{x}" for x in st["changed"] + st["unknown"]]
    return _named_words(SM.retention({"keep": keep, "current": current, "prior": prior, "copies": copies}), named, "pkg/") | {"kept_files": kept_files, "named": {v: named.get("pkg/" + v, "unknown") for v in recs["versions"] if v != current}}

def games_copy_of(c, recs: dict, plan: dict) -> str | None:
    """the games copy on the console that is exactly this plan's (its record names the plan's files, and every one of them is there unchanged);
    None when there is none"""
    want = {f["path"]: f["sha256"] for f in plan["files"]}
    for g, r in sorted(recs["games"].items()):
        if r.get("version") != plan["version"] or r.get("files") != want: continue
        s = copy_state(c, g, r, GAMES_DIR)
        if not s["changed"] and not s["missing"]: return g
    return None

def _addon_current(c, a: str) -> bool:
    """hakchi lists the add-on with this installation's package, every file unchanged"""
    ap = addon_package(a); mf = c.method_files(ap["name"])
    return mf is not None and mf.get("package") == ap["package_id"] and all((mf.get("files") or {}).get(p) == s for p, s in ap["files"].items())

# ---------------------------------------------------------------- the installer
class Installer:
    """one installation attempt of one plan on one console. journal: a new file (created exclusively; an existing journal belongs to an
    earlier attempt and is never reused) or an existing folder (a new home-<attempt>.jsonl is made in it). device: the hardware id this
    operation is bound to (the console that was checked and confirmed): compared with the console answering now at the preflight and again
    before each commit point (the switch, each add-on, CCS Nucleus's start, the tidy). policy: "stop" (the app's physical operations and every real console: a
    failure stops all writes; recovery is its own approved operation, G3) or "rollback" (a disposable stand-in in a rehearsal: undone at once)"""
    def __init__(self, console, plan: dict, package_dir: str, journal: str, emit=None, cancel=None, attempt: str | None = None, device: str | None = None, policy: str | None = None):
        if getattr(console, "simulated", False) is not True: gate.require("inspect", "use a real console")   # and run() needs the install tier
        if policy not in (None, "stop", "rollback"): raise ValueError("policy: stop or rollback")
        if policy == "rollback" and getattr(console, "simulated", False) is not True: raise ValueError("only a disposable stand-in is rolled back by itself")
        self.c, self.plan, self.pkg = console, plan, package_dir; self.emit = emit or (lambda ev: None); self.cancel = cancel or (lambda: False)
        self.games = plan.get("delivery") == "games"   # without CCS Mod: a games copy (games_only.py), never CCS HOME's
        self.where = GAMES_DIR if self.games else PKG_DIR; self._made: dict | None = None
        self.attempt = attempt or secrets.token_hex(4); self.v = plan["version"]; self._name(self.attempt)
        self.journal = os.path.join(journal, f"home-{self.attempt}.jsonl") if os.path.isdir(journal) else journal
        self.auto_rollback = (policy == "rollback") if policy else console.simulated is True
        self.device = device
    def _name(self, attempt: str):
        """this attempt's copy, its folder and the record that makes it this program's"""
        self.copy = f"{self.v}.{attempt}"; self.target = f"{self.where}/{self.copy}"
        self.copy_record = f"{INST_DIR}/{'games' if self.games else 'v'}-{self.copy}.json"
    def _data(self, f: dict) -> bytes:
        """a planned file's bytes on this computer: the package's own file, or (a games copy's notices) the file made from the package and
        this program's records, exactly as the plan was made (games_only.files_for)"""
        if f.get("from") == "made":
            if self._made is None:
                sp = HP.spec(self.pkg, self.plan["package_files"])
                _files, self._made = GO.files_for(self.pkg, self.plan["package_files"], sp, str(self.plan.get("label") or "Classic Cart Studio")[:80])
            if f["path"] not in self._made: raise InstallStopped("stage", f"{f['path']} cannot be made again on this computer (prepare it again)")
            return self._made[f["path"]]
        return _read(os.path.join(self.pkg, *f["path"].split("/")))
    def _source(self, f: dict) -> str | None:
        """the file on this computer a planned file is streamed from (the worker reads it in its own package folder); None: sent as bytes"""
        return None if f.get("from") == "made" else os.path.join(self.pkg, *f["path"].split("/"))
    def empty_games(self) -> bool:
        """a games copy with no files (every Hakchi entry carries its own game file or names a built-in game): no folder is made for it"""
        return self.games and not self.plan["files"]
    # the journal (PC side): one line per intent / result, written through to the disk before the change it announces (fsync)
    def log(self, **k):
        with open(self.journal, "a", encoding="utf-8") as f:
            f.write(json.dumps({"t": round(time.time(), 3), "attempt": self.attempt, "plan": self.plan["hash"], **k}) + "\n"); f.flush(); os.fsync(f.fileno())
    def _open_journal(self):
        try:
            with open(self.journal, "x", encoding="utf-8") as f:
                f.write(json.dumps({"t": round(time.time(), 3), "attempt": self.attempt, "plan": self.plan["hash"], "op": "begin", "copy": self.copy, "device": self.device}) + "\n"); f.flush(); os.fsync(f.fileno())
        except FileExistsError: raise Refused("journal", "this journal belongs to an earlier attempt and is never reused; each attempt keeps its own record") from None
    @staticmethod
    def entries(journal: str) -> list[dict]:
        """the journal's lines. A last line cut short (this computer stopped while writing it) is left out: its change had not started, since
        every line is on the disk before the change it announces. A damaged line before the end raises JournalCorrupt (nothing is assumed)"""
        try: raw = _read(journal)
        except FileNotFoundError: return []
        lines = raw.split(b"\n"); out = []
        for k, l in enumerate(lines):
            if not l.strip(): continue
            try: e = json.loads(l.decode("utf-8"))
            except (ValueError, UnicodeDecodeError):
                if k == len(lines) - 1: break
                raise JournalCorrupt(f"line {k + 1} of the journal {os.path.basename(journal)} is damaged") from None
            if not isinstance(e, dict): raise JournalCorrupt(f"line {k + 1} of the journal {os.path.basename(journal)} is not a record")
            out.append(e)
        return out
    def _progress(self, stage: str, **k): self.emit({"event": "progress", "stage": stage, **k})
    def _sync(self, drop: bool = False, why: str = ""):
        """what was written so far becomes persistent on the console (its sync; with drop, the clean page cache is dropped too, so the next
        read-back reads the storage rather than memory). Readback is not a power-loss test: it shows the bytes the console reads now"""
        s = getattr(self.c, "sync", None)
        if s is None: raise InstallStopped("sync", "this console session cannot make its writes persistent; stopped before the next change")
        self.log(op="sync", why=why, drop_caches=drop); s(drop_caches=drop); self.log(op="sync", state="done")

    # ---- preflight: reads only
    def preflight(self) -> dict:
        if getattr(self.c, "simulated", False) is not True: gate.require("inspect", "read a real console before installing")
        c = self.c; dev = check_device(c.identity(fresh=True), "preflight")
        if self.device and dev["hwid"] != self.device: raise Refused("device", "this is not the console that was checked and confirmed; nothing was changed")
        self.device = dev["hwid"]; i = c.identity()
        why = model_refusal(dev["model"])   # a model whose CCS add-ons this version does not install (the registry's capability), before anything else is read
        if why: raise Refused("device", why)
        for d in (ROOT_DIR, PKG_DIR, INST_DIR, DATA_DIR, GAMES_DIR):
            t = c.stat(d)["type"]
            if t not in ("none", "dir"): raise Refused("preflight", f"{d} is {'a link' if t == 'link' else 'not a folder'}; it is not this program's, so nothing was changed")
        recs = read_records(c)
        if recs["problems"]: raise Refused("preflight", "this program's records on the console cannot all be read (" + "; ".join(recs["problems"][:3]) + "); nothing was changed")
        if recs["attempts"]: a = sorted(recs["attempts"])[0]; raise Refused("preflight", f"an earlier attempt ({a}) did not finish; recover it first (nothing was changed)")
        pst = c.stat(POINTER); cur = None; psha = None
        if pst["type"] == "file":
            b = c.read(POINTER, 64); cur = (b or b"").decode("ascii", "replace").strip(); psha = _sha(b) if b is not None else None
            if not COPY_RE.match(cur) or cur not in recs["versions"] or b != pointer_bytes(cur): raise Refused("preflight", f"{POINTER} names {cur!r}, which this program's records do not know; nothing was changed")
            if c.stat(f"{PKG_DIR}/{cur}")["type"] != "dir": raise Refused("preflight", f"the copy in use ({cur}) is not a real folder; nothing was changed")
        elif pst["type"] != "none": raise Refused("preflight", f"{POINTER} is {'a link' if pst['type'] == 'link' else 'not a plain file'}, not this program's pointer; nothing was changed")
        # the add-ons: this installation's packages are the ones the plan names; the console's add-ons now (read; Refused when they cannot be
        # established); a choice never removes one (that is the CCS Console Workbench's Console Restore)
        want = self.plan["addons"]
        for a, p in self.plan["packages"].items():
            try: ok = addon_package(a)["sha256"] == p["sha256"]
            except ChooserUnavailable: ok = False
            if not ok: raise Refused("preflight", f"this installation's {ADDON_LABELS[a]} package is not the one the plan names: prepare the HOME menu and review the plan again. Nothing was changed")
        now = console_addons(c); have = now["choice"]
        gone = removes(have, want)
        if gone: raise Refused("addons", REMOVES_TEXT, addons_now=have, addons_now_text=now["text"], would_remove=[ADDONS[a] for a in gone], keep_choice=keep_choice(want, have))
        add = [a for a in choice_addons(want) if (now["packages"].get(a) or {}).get("state") in (None, "older")]
        start = None
        if self.plan["start_line"]:   # CCS Nucleus's start as the plan chose it (its own command changes it only when it differs; read again after the add-ons)
            line = now["start"].get("line") if "nucleus" in now["packages"] else None
            start = {"now": line, "want": self.plan["start_line"], "change": line != self.plan["start_line"], "text": self.plan["start_text"]}
        legacy = legacy_state(c, recs)
        if legacy == "foreign": raise Refused("preflight", f"a {LEGACY_NAME} module is installed that this program did not make (or that was changed); nothing was changed")
        ident = c.read(f"{DATA_DIR}/IDENTITY.json", 65536) if not self.games else None; data = {"state": "new"}   # (CCS HOME's saves: a games copy has none)
        if ident is not None:
            try: d = json.loads(ident)
            except ValueError: d = None
            if not isinstance(d, dict) or d.get("schema") != "ccs-console-data/1": raise Refused("preflight", f"{DATA_DIR}/IDENTITY.json cannot be read; the saves there are kept and nothing was changed")
            if d.get("state_identity") != self.plan["state_identity"]:
                raise Refused("preflight", f"the saves on the console were made by another player build ({d.get('state_identity')}); this package's player ({self.plan['state_identity']}) would not load them. Nothing was changed")
            data = {"state": "kept", "identity": d.get("state_identity")}
        elif c.stat(DATA_DIR)["type"] == "dir" and c.listdir(DATA_DIR): data = {"state": "kept-unidentified"}
        same = None; ref = None
        if self.games:   # this exact games copy already there (by its record, every file unchanged), or none needed at all: nothing copied again
            same = f"{self.v}.{'0' * 8}" if self.empty_games() else games_copy_of(c, recs, self.plan)
            ref = f"games/{same}" if same else None
        elif cur and recs["versions"][cur].get("manifest_sha256") == self.plan["manifest_sha256"]:   # this exact package in use (by its record): adding an add-on or changing the start alone copies nothing again
            st = copy_state(c, cur, recs["versions"][cur]); same = cur if not st["changed"] and not st["missing"] else None; ref = same
        # this collection's Hakchi games: added (or brought up to date) when the plan asks for them, removed when it does not and this
        # program added them before; nothing when they are already this plan's for the copy whose game files they launch
        hkp = self.plan.get("hakchi") or {"publish": False}; hs = hakchi_state(c, recs)
        if hs["index_type"] not in ("none", "file"): raise Refused("preflight", f"{HP.INDEX_PATH} is not a plain file; it is kept, and nothing was changed")
        published = bool(hs["journal"] or hs["index_sha256"])
        if hkp["publish"] and (hkp["review"]["games"] or published):   # (none of the collection's games can be a Hakchi game, none were: nothing to do)
            up = bool(ref) and hs["program"] and hs["index"] is not None and (hs["index"].get("producer") or {}).get("projection") == projection_id(hkp["projection"], ref)
            hk_action = "none" if up else "publish"
            if hk_action == "publish" and not MODELS[dev["model"]]["nes_emulator"]: raise Refused("hakchi", no_nes_emulator_text(dev["model"]))
        else: hk_action = "unpublish" if published and not hkp["publish"] else "none"
        free = c.free_bytes(ROOT_DIR)
        room = space_account(self.plan, add, hk_action == "publish", bool(same), {"source": "simulated" if getattr(c, "simulated", False) is True else "measured", "available_bytes": free}, dev["model"])
        need = room["peak_bytes"]
        if not room["write_ok"]: raise Refused("preflight", space_refusal(room))
        unknown = [f"{PKG_DIR}/{x}" for x in c.listdir(PKG_DIR) if x != "current" and x not in recs["versions"]]
        unknown += [f"{GAMES_DIR}/{x}" for x in c.listdir(GAMES_DIR) if x not in recs["games"]]
        unknown += [f"{ROOT_DIR}/{x}" for x in c.listdir(ROOT_DIR) if x not in KNOWN_ROOT]
        older = [v for v in recs["games"] if v != same] if self.games else [v for v in recs["versions"] if v != cur]
        words = ([f"Adds {' and '.join(ADDON_LABELS[a] for a in add if now['packages'].get(a) is None)}." ] if any(now["packages"].get(a) is None for a in add) else [])               + ([f"Updates {' and '.join(ADDON_LABELS[a] for a in add if now['packages'].get(a) is not None)} to this version of its package."] if any(now["packages"].get(a) is not None for a in add) else [])               + ([("Updates" if start["now"] else "Sets") + " CCS Nucleus's start setting: " + start["text"]] if start and start["change"] else [])
        words += [self.plan["hakchi_text"]] if hk_action == "publish" else ["Removes the Hakchi games Classic Cart Studio added to Original Menu earlier."] if hk_action == "unpublish" else []
        if self.games and same and hk_action == "none" and not words: words = ["This collection's games are already in Original Menu as Hakchi games."]
        # the review's earlier versions: whether an entry of the console's menus names each one now (this collection's own entries, which
        # this installation rewrites or removes, left out); the clean-up reads the entries again after they are written, and once more
        # just before each copy goes
        earlier = None
        if not self.games:
            after = cur if same else self.copy
            rn = menu_names(c, ["pkg/" + v for v in recs["versions"] if v != after], rewritten=hk_action in ("publish", "unpublish"))
            earlier = earlier_review(c, recs, after, None if same else cur, self.plan.get("earlier_home_versions"), rn["named"]) | {"named_why": rn["why"]}
        return {"identity": i, "device": dev, "free_bytes": free, "needs_bytes": need, "in_use": cur, "pointer_sha256": psha, "delivery": "games" if self.games else "home",
                "hakchi": {"action": hk_action, "publish": hkp["publish"], "program": hs["program"], "index_sha256": hs["index_sha256"], "generation": hs["generation"], "journal": hs["journal"]},
                "already": bool(same and not add and legacy == "absent" and not (start and start["change"]) and hk_action == "none"), "same_copy": bool(same),
                "games_copy": same if self.games else None, "home_kept": cur if self.games else None,
                "addons": want, "addons_now": have, "addons_now_text": now["text"], "packages_now": now["packages"], "start_now": now["start"].get("line"),
                "add": [ADDONS[a] for a in add if now["packages"].get(a) is None], "update": [ADDONS[a] for a in add if now["packages"].get(a) is not None], "add_addons": add,
                "start": start, "changes_text": " ".join(words) or "The add-ons and what opens at power-on stay as they are.",
                "legacy": legacy, "data": data, "kept_unknown": unknown, "earlier_copies": older, "records": len(recs["files"]),
                "earlier": earlier}

    # ---- run
    def run(self) -> dict:
        """install (or update to, or repair) this plan's version. Returns what happened; never claims more than it read back"""
        if getattr(self.c, "simulated", False) is not True: gate.require("install", "install on a real console")
        try: self._open_journal()
        except Refused as e: return {"ok": False, "result": "refused", "at": e.step, "why": e.why, "attempt": self.attempt, "changed": False}
        self.undo: list[dict] = []; self.prior = None
        try:
            pf = self.preflight(); self.log(op="preflight", **{k: v for k, v in pf.items() if k != "identity"}, identity=pf["identity"]); self._progress("preflight", ok=True)
        except Refused as e:
            self.log(op="end", result="refused", at=e.step, why=e.why)
            return {"ok": False, "result": "refused", "at": e.step, "why": e.why, "attempt": self.attempt, "changed": False, **e.detail}
        if pf["already"]:
            self.log(op="end", result="no-change"); return {"ok": True, "result": "no-change", "attempt": self.attempt, "written_bytes": 0, "preflight": pf, "changed": False}
        try:
            self._install(pf)
        except (Refused, InstallStopped, Cancelled, ConnectionError, OSError, PermissionError, ValueError) as e:
            step = getattr(e, "step", None) or ("cancelled" if isinstance(e, Cancelled) else "transfer"); why = getattr(e, "why", None) or str(e) or type(e).__name__
            self.log(op="stopped", at=step, why=why)
            if not self.auto_rollback:
                self.log(op="end", result="stopped-needs-recovery")
                return {"ok": False, "result": "cancelled" if isinstance(e, Cancelled) else "stopped", "at": step, "why": why, "attempt": self.attempt, "rolled_back": None, "changed": bool(self.undo),
                        "switched": self._switch_state(),
                        "next": "No further writes. Collect the diagnostics; recovering this attempt is its own approved operation (G3)."}
            rb = self._rollback(self.undo, self.prior)
            self.log(op="end", result="rolled-back" if rb["ok"] else "rollback-incomplete", rolled_back=rb)
            return {"ok": False, "result": "cancelled" if isinstance(e, Cancelled) else "stopped", "at": step, "why": why, "attempt": self.attempt, "rolled_back": rb}
        return self.done

    def _switch_state(self) -> str:
        """whether the console now starts this attempt's copy: "no" (the switch never began), "yes" (it was done), "unknown" (it began and its
        answer was lost: read the console to know)"""
        es = [e for e in self.entries(self.journal) if e.get("op") == "switch"]
        return "no" if not es else ("yes" if any(e.get("state") == "done" for e in es) else "unknown")

    def _do(self, op: str, act, undo: dict | None = None, **info):
        """one console change: its intent (and how to undo it) in the journal and the undo list first, then the change, then its result"""
        if undo is not None: self.undo.append(undo)
        self.log(op=op, state="intent", **info, **({"undo": undo} if undo else {}))
        r = act()   # a put answers with the SHA-256 of what the console now holds there: recorded, so an undo knows this attempt's own bytes
        self.log(op=op, state="done", **({"written_sha256": r} if op == "put" and isinstance(r, str) and len(r) == 64 else {})); return r

    def _nucleus_start(self, line: str) -> str:
        """CCS Nucleus's own command on the console (ccs-nucleus start DEFAULT CHOOSER HOME SELECTION TIMEOUT, the worker's nucleus-start), then
        its registry read back; anything but its plain success, read back as asked, stops the attempt"""
        r = str(self.c.nucleus_start(*line.split(" ")) or "")
        if not r.startswith("result start ok "): raise InstallStopped("start", f"CCS Nucleus's own command answered: {r}")
        got = nucleus_start_state(self.c).get("line")
        if got != line: raise InstallStopped("start", f"CCS Nucleus's start does not read back as asked ({got or 'none'} instead of {line})")
        return r

    def _install(self, pf):
        c = self.c; plan = self.plan; g = self.games
        cur = pf["in_use"]; same = pf.get("same_copy") is True   # this exact copy already there: only the add-ons, the start and the Hakchi games change
        before = {a: {"state": x["state"], "package": x["package"]} for a, x in (pf.get("packages_now") or {}).items()}
        self.prior = {"pointer": cur, "pointer_sha256": pf["pointer_sha256"], "addons": before, "start": pf.get("start_now") if "nucleus" in before else None}
        rec = {"schema": "ccs-install-attempt/1", "attempt": self.attempt, "plan": plan["hash"], "version": self.v, "copy": self.copy, "kind": "games" if g else "install",
               "device": {k: pf["device"].get(k) for k in ("hwid", "system", "kernel", "hakchi")}, "started": _now(),
               "prior": {"pointer": cur, "addons": before, "start": self.prior["start"]}}
        rec_b = _record_bytes(rec); self.attempt_path = f"{INST_DIR}/attempt-{self.attempt}.json"
        # 1. this program's folders, then the attempt record (with what was there before), persistent before anything else changes
        for d in (ROOT_DIR, INST_DIR) + (() if g and (same or self.empty_games()) else (self.where,)):
            if c.stat(d)["type"] == "none": self._do("mkdir", lambda d=d: c.mkdir(d), {"op": "rmdir-if-empty", "path": d}, path=d)
        self._do("record-attempt", lambda: c.put(self.attempt_path, rec_b), {"op": "remove", "path": self.attempt_path, "sha256": _sha(rec_b)}, path=self.attempt_path)
        self._sync(why="the attempt record and what it keeps of the earlier state, before anything else changes")
        # 2. the copy's record, then the copy (a new folder of this attempt, beside the one in use); not when this exact copy is there. Without
        # CCS Mod the copy is a games copy (games_only.py): the game files the Hakchi entries launch and their notices, nothing of CCS HOME
        files = {f["path"]: f["sha256"] for f in plan["files"]}
        if not same and not self.empty_games():
            vrec = ({"schema": GO.RECORD, "copy": self.copy, "plan": plan["hash"], "version": self.v, "attempt": self.attempt, "manifest_sha256": plan["manifest_sha256"], "files": files} if g else
                    {"schema": "ccs-owned-copy/1", "copy": self.copy, "plan": plan["hash"], "version": self.v, "attempt": self.attempt, "manifest_sha256": plan["manifest_sha256"], "files": files,
                     "previous": pf.get("in_use"), "installed_utc": _now()})   # the copy in use before it (the order of earlier versions), and when (this PC's time)
            vrec_b = _record_bytes(vrec); vpath = self.copy_record
            self._do("record-copy", lambda: c.put(vpath, vrec_b), {"op": "remove", "path": vpath, "sha256": _sha(vrec_b)}, path=vpath)
            self._do("mkdir", lambda: c.mkdir(self.target), {"op": "remove-copy", "copy": self.copy}, path=self.target)
            stage = "games" if g else "stage"
            self._progress(stage, done=0, total=len(plan["files"]), bytes=0, total_bytes=plan["bytes"]); sent = 0
            made = set()
            for k, f in enumerate(plan["files"]):
                if self.cancel(): raise Cancelled("canceled before the games were added" if g else "canceled before the switch")
                parts = f["path"].split("/")[:-1]
                for i in range(1, len(parts) + 1):
                    d = "/".join(parts[:i])
                    if d not in made: self._do("mkdir", lambda d=d: c.mkdir(f"{self.target}/{d}"), None, path=f"{self.target}/{d}"); made.add(d)
                data = self._data(f)
                if _sha(data) != f["sha256"]: raise InstallStopped("stage", f"{f['path']} on this computer changed after the plan was made (prepare it again)")
                src = self._source(f)
                self._do("put", lambda f=f, data=data, src=src: c.put(f"{self.target}/{f['path']}", data, f["mode"], source=src), None, path=f"{self.target}/{f['path']}", sha256=f["sha256"], size=f["size"])
                sent += f["size"]; self._progress(stage, done=k + 1, total=len(plan["files"]), bytes=sent, total_bytes=plan["bytes"])
            # 3. the data folder and its identity (CCS HOME's saves; only when there is none: existing saves are kept as they are)
            if not g:
                if c.stat(DATA_DIR)["type"] == "none": self._do("mkdir", lambda: c.mkdir(DATA_DIR), {"op": "rmdir-if-empty", "path": DATA_DIR}, path=DATA_DIR)
                if c.stat(f"{DATA_DIR}/IDENTITY.json")["type"] == "none":
                    ib = _record_bytes({"schema": "ccs-console-data/1", "state_identity": plan["state_identity"], "created": _now(), "note": "saves, slots and settings made on this console by Classic Cart Studio's console player"})
                    self._do("data-identity", lambda: c.put(f"{DATA_DIR}/IDENTITY.json", ib), {"op": "remove", "path": f"{DATA_DIR}/IDENTITY.json", "sha256": _sha(ib)}, path=f"{DATA_DIR}/IDENTITY.json")
            # 4. persistent, then read back every file from the storage (the page cache dropped), before the switch
            self._sync(drop=True, why="the new copy, before it is read back" + ("" if g else " and switched to"))
            bad = [rel for rel, h in files.items() if c.sha256(f"{self.target}/{rel}") != h]; self.log(op="readback", mismatches=bad[:20], files=len(files))
            self._progress("readback", ok=not bad, files=len(files))
            if bad: raise InstallStopped("readback", f"{len(bad)} file(s) on the console differ from the plan ({', '.join(bad[:3])})")
            if self.cancel(): raise Cancelled("canceled before the games were added" if g else "canceled before the switch")
            # 5. the switch (CCS HOME only): one rename of the pointer, only over exactly what the preflight read there; then persistent. A games
            # copy has no pointer: the Hakchi entries written after the verification name it
            if not g:
                same_console(c, self.device, "switch")
                expect = pf["pointer_sha256"] if cur else "absent"
                self._do("switch", lambda: c.set_pointer(POINTER, self.copy, expect=expect), {"op": "pointer", "value": cur}, previous=cur, value=self.copy); self._progress("switch", ok=True)
                self._sync(why="the switch")
        # 6. each add-on the choice includes and the console lacks (or has in an older version of its package), in install order (CCS Mod,
        # CCS Nucleus, its startup chooser), through hakchi's own pack_install; this program's record of it first
        for a in pf["add_addons"]:
            same_console(c, self.device, "addons")
            ap = addon_package(a); pb4 = before.get(a) or {}
            if ap["sha256"] != plan["packages"][a]["sha256"]: raise InstallStopped("addons", f"{ADDON_LABELS[a]}'s package on this computer is not the one the plan names (prepare it again)")
            mrec_b = _record_bytes({"schema": "ccs-owned-module/2", "name": ap["name"], "addon": a, "sha256": ap["sha256"], "package_id": ap["package_id"]})
            mpath = f"{INST_DIR}/module-{ap['sha256'][:16]}.json"
            if c.stat(mpath)["type"] == "none": self._do("record-module", lambda mpath=mpath, mrec_b=mrec_b: c.put(mpath, mrec_b), {"op": "remove", "path": mpath, "sha256": _sha(mrec_b)}, path=mpath)
            self._sync(why=f"the record of {ap['name']}, before its package")
            self._do("module", lambda ap=ap: c.pack_install(ap["name"], ap["hmod"]), {"op": "module", "name": ap["name"], "addon": a, "prior": pb4.get("state") or "absent", "prior_package": pb4.get("package")},
                     module=ap["name"], prior=pb4.get("state") or "absent")
            self._progress("module", ok=True, name=ap["name"])
        # 6b. CCS Nucleus's start as the plan chose it, by CCS Nucleus's own command, read again after the add-ons (installing CCS Nucleus writes
        # its first registry when it has none); only when it differs. Its undo puts back the start read here
        line = plan["start_line"]
        if line:
            now = nucleus_start_state(c)
            if now["state"] == "invalid": raise InstallStopped("start", "CCS Nucleus's settings on the console cannot be read now; they are kept as they are")
            if now["line"] != line:
                same_console(c, self.device, "start")
                self._do("nucleus-start", lambda: self._nucleus_start(line), {"op": "nucleus-start", "prior": now["line"], "want": line, "nucleus_before": (before.get("nucleus") or {}).get("state") or "absent"},
                         want=line, prior=now["line"])
            self._progress("start", ok=True)
        # 7. verify everything as the storage holds it: the copy (with CCS HOME, the pointer naming it), every add-on of the choice (this
        # installation's package, every file unchanged) and CCS Nucleus's start
        self._sync(drop=True, why="before the final verification")
        vcopy = (pf.get("games_copy") or self.copy) if g else (cur if same else self.copy)
        bad = [rel for rel, h in files.items() if c.sha256(f"{self.where}/{vcopy}/{rel}") != h]; pb = None if g else c.read(POINTER, 64)
        addons_bad = [a for a in choice_addons(plan["addons"]) if not _addon_current(c, a)]
        st = nucleus_start_state(c).get("line") if line else None
        ok = not bad and (g or pb == pointer_bytes(vcopy)) and not addons_bad and st == line
        self.log(op="verify", ok=ok, mismatches=bad[:20], pointer=None if g else (pb or b"").decode(errors="replace").strip(), addons_not_as_planned=[ADDONS[a] for a in addons_bad], start=st); self._progress("verify", ok=ok)
        if not ok: raise InstallStopped("verify", ("the game files, " if g else "after the switch, the copy, the pointer, ") + (", ".join(ADDONS[a] for a in addons_bad) + "'s package" if addons_bad else "an add-on's package") + " or CCS Nucleus's start is not as planned")
        # installed: from here nothing is undone. 8. this collection's Hakchi games, then the tidy of what this program owns of the earlier
        # state (the earlier start-up module, earlier copies, console programs and records), then the attempt's record. A removal the console
        # does not confirm stops every write that would follow: the attempt's record stays open and the result says what is known
        self.undo = []; self.log(op="installed"); tidy = []
        # 7b. this collection's Hakchi games (or their removal), by this program's console program; a failure here leaves the earlier copies
        # (an entry may still name one) and the earlier console program in place, and says so
        hk = self._hakchi(pf, vcopy) if (pf.get("hakchi") or {}).get("action", "none") != "none" else {"action": "none", "ok": True}
        stopped = None
        try:
            self._tidy(pf, hk, vcopy, tidy)
            self._finish(rec_b)   # 9. the attempt is finished: its record goes last, after the tidy is persistent
        except CleanupStopped as e: stopped = e
        except (Refused, InstallStopped, ConnectionError, OSError, ValueError) as e:   # a read during the clean-up: nothing more is written either
            stopped = CleanupStopped("reading the console during the clean-up", getattr(e, "why", None) or str(e) or type(e).__name__)
        # without CCS Mod the Hakchi games are what this installation delivers: when they could not be added it is not complete (the game files
        # stay, checked; installing the same plan again finishes it)
        result = "cleanup-stopped" if stopped else "incomplete" if g and not hk["ok"] else "installed"
        if result == "incomplete": self.log(op="games-not-added", why=hk.get("why"))
        kept = [k for t in tidy for k in t.get("kept", [])]
        self.done = {"ok": result == "installed", "result": result, "delivery": "games" if g else "home", "version": self.v, "copy": vcopy, "attempt": self.attempt, "addons": plan["addons"], "added": pf["add"], "updated": pf["update"], "start": st,
                     "start_text": plan["start_text"], "written_bytes": getattr(c, "written_bytes", None), "preflight": pf,
                     "tidy": tidy, "kept_changed_or_unknown": kept + pf["kept_unknown"], "record_closed": stopped is None, "changed": True, "device": pf["device"]["hwid"],
                     "hakchi": hk, "warnings": [] if hk["ok"] else [hk["text"]], "verified": True}
        if stopped:
            self.log(op="cleanup-stopped", at=stopped.at, why=stopped.why, uncertain=stopped.uncertain, partial=stopped.partial, tidy=tidy)
            self.log(op="end", result="cleanup-stopped"); self._progress("done", ok=False)
            what = "This collection's game files were copied and checked" if g else "The new HOME is installed and checked: the console starts it"
            self.done.update(cleanup={"stopped": True, "at": stopped.at, "why": stopped.why, "uncertain": stopped.uncertain, "partial": stopped.partial},
                             why=f"{what}. The clean-up afterwards stopped while {stopped.at}: {stopped.why}."
                                 + (f" Whether this changed is not known: {', '.join(stopped.uncertain)}." if stopped.uncertain else "")
                                 + (f" {stopped.partial['removed_files']} file(s) of {stopped.partial['copy']} were removed before it stopped." if stopped.partial else ""),
                             next="No further writes were made. This attempt's record stays open on the console; recovering it is its own approved operation. "
                                  "The backup made before this installation keeps every file of the earlier copies.")
            return
        self.log(op="end", result="installed"); self._progress("done", ok=result == "installed")
        if result == "incomplete": self.done.update(why=hk["text"], next="Install the collection again to finish adding its games to Original Menu.")

    def _tidy(self, pf: dict, hk: dict, vcopy: str, tidy: list):
        """after the installation is verified: what this program owns of the earlier state, each removal only of exactly what its record
        names. A copy that an entry of the console's menus names (or may name), or that changed, is kept: a refusal, reported, and the tidy
        goes on. A removal the console does not confirm (lost before it acted, or after it may have acted) raises CleanupStopped: nothing
        more is written"""
        c = self.c; g = self.games; plan = self.plan
        if hk.get("stage_failed"): raise CleanupStopped("removing the entry files staged for Original Menu", hk["stage_failed"], [f"{HP.STAGE_ROOT}/{self.attempt}"])
        try: same_console(c, self.device, "tidy")
        except Refused as e: raise CleanupStopped("checking that the same console answers before the clean-up", e.why) from None
        if pf.get("legacy") == "mine":   # this program's earlier start-up module (hakchi deletes its file at every start anyway)
            self.log(op="retire", state="intent", module=LEGACY_NAME)
            try: c.pack_uninstall(LEGACY_NAME)
            except (ConnectionError, OSError) as e:
                tidy.append({"module": LEGACY_NAME, "removed": False, "why": str(e)})
                raise CleanupStopped(f"removing the earlier start-up module {LEGACY_NAME}", str(e), [f"hakchi's package {LEGACY_NAME}"]) from e
            self.log(op="retire", state="done")
        recs = read_records(c)
        # CCS HOME's earlier copies (never the one in use; without CCS Mod none of CCS HOME's is touched): the setting keeps the most recent;
        # the others go when nothing names them, read after this collection's entries were written and again just before each one goes
        if not g and hk["ok"]:
            prior = pf.get("in_use") if pf.get("in_use") != vcopy else (recs["versions"].get(vcopy) or {}).get("previous")
            names = menu_names(c, ["pkg/" + v for v in recs["versions"] if v != vcopy])
            ret = earlier_cleanup(c, recs, vcopy, prior, plan.get("earlier_home_versions"), names["named"])
            tidy.append({"earlier": ret, "kept": ret["kept_files"], "named_why": names["why"], "named_by": {k[4:]: v for k, v in names["entries"].items() if v}})
            for v in ret["remove"]:
                t = self._remove_checked(v, recs["versions"][v], "pkg", PKG_DIR, whole=True, tidy=tidy)
                tidy.append(t | ({"restore": RESTORE_EARLIER_TEXT} if t.get("removed") else {}))
        # every games copy but this plan's (a games copy only ever serves the Hakchi entries), when nothing names it
        if hk["ok"]:
            games = [v for v in sorted(recs["games"]) if not (g and v == vcopy)]
            gn = menu_names(c, ["games/" + v for v in games]) if games else {"named": {}, "entries": {}}
            for v in games:
                n = gn["named"].get("games/" + v, "unknown")
                if n != "no": tidy.append({"copy": v, "removed": False, "refused": True, "why": NAMED_YES if n == "yes" else NAMED_UNKNOWN, "named_by": gn["entries"].get("games/" + v) or []}); continue
                tidy.append(self._remove_checked(v, recs["games"][v], "games", GAMES_DIR, whole=False, tidy=tidy))
            if c.stat(GAMES_DIR)["type"] == "dir" and not c.listdir(GAMES_DIR):
                self.log(op="rmdir", path=GAMES_DIR)
                try: c.rmdir(GAMES_DIR)
                except (ConnectionError, OSError) as e: raise CleanupStopped(f"removing the empty folder {GAMES_DIR}", str(e), [GAMES_DIR]) from e
            self._tidy_programs(recs, keep_current=hk["action"] == "publish", tidy=tidy)
        for k16, r in recs["modules"].items():
            if record_gone(c, r):   # an earlier one's record, once nothing of it is installed (removed since, or replaced by another version)
                b = c.read(r["_record"], 65536)
                if b is None: continue
                self.log(op="remove-record", path=r["_record"], sha256=_sha(b))
                try: c.remove(r["_record"], _sha(b))
                except (ConnectionError, OSError) as e: raise CleanupStopped(f"removing the record {r['_record']}", str(e), [r["_record"]]) from e
        self.log(op="tidy", result=tidy)

    def _remove_checked(self, v: str, rec: dict, kind: str, where: str, whole: bool, tidy: list) -> dict:
        """one earlier copy removed, checked again just before it goes: no entry of the console's menus names it, and (whole: a CCS HOME copy)
        it is still exactly what its record names; otherwise it is kept, a refusal reported as such. Each file goes only while it is still its
        recorded bytes (remove_copy). A removal the console does not confirm is added to tidy and raises CleanupStopped with what was removed
        before it"""
        n = menu_names(self.c, [f"{kind}/{v}"])
        if n["named"][f"{kind}/{v}"] != "no":
            return {"copy": v, "removed": False, "refused": True, "why": NAMED_YES if n["named"][f"{kind}/{v}"] == "yes" else NAMED_UNKNOWN, "named_by": n["entries"][f"{kind}/{v}"]}
        if whole:
            st = copy_state(self.c, v, rec, where)
            if st["changed"] or st["missing"] or st["unknown"]:
                return {"copy": v, "removed": False, "refused": True, "why": "it changed since it was checked", "kept": [f"{where}/{v}/{x}" for x in st["changed"] + st["unknown"]]}
        try: return remove_copy(self.c, v, rec, journal=lambda **k: self.log(**k), where=where)
        except RemovalFailed as e:
            tidy.append({"copy": v, "removed": False, "stopped": True, "removed_files": e.removed_files, "uncertain": [e.path], "why": str(e.cause)})
            raise CleanupStopped(f"removing the earlier copy {where}/{v}", str(e.cause), [e.path], {"copy": f"{where}/{v}", "removed_files": e.removed_files}) from e

    def _finish(self, rec_b: bytes):
        """the attempt's record removed, last, once the tidy is persistent; then that too. A step the console does not confirm raises
        CleanupStopped (the record is then open, or whether it is closed is not known)"""
        c = self.c
        try: self._sync(why="the tidy, before the attempt record is closed")
        except (ConnectionError, OSError, InstallStopped) as e: raise CleanupStopped("making the clean-up persistent", getattr(e, "why", None) or str(e)) from e
        self.log(op="finish", state="intent", path=self.attempt_path)
        try: ok = c.remove(self.attempt_path, _sha(rec_b))
        except (ConnectionError, OSError) as e: raise CleanupStopped("closing this attempt's record", str(e), [self.attempt_path]) from e
        if not ok: raise CleanupStopped("closing this attempt's record", f"{self.attempt_path} is not the record this attempt wrote; it is kept")
        try: self._sync(why="the closed attempt")
        except (ConnectionError, OSError, InstallStopped) as e: raise CleanupStopped("making the closed record persistent", getattr(e, "why", None) or str(e), [self.attempt_path]) from e
        self.log(op="finish", state="done")

    # ---- this collection's Hakchi games (hakchi_publish.py; the console program tools/console-nucleus/console/collections/ccs-collections)
    def _hakchi(self, pf: dict, vcopy: str) -> dict:
        """after the installation is verified: the console program put in place (its record first), then either the publication (where the
        console's menu lists games; the entries staged under integration/stage/<attempt>; checked; the index made from what can be written;
        applied; the index read back) or the removal of the earlier one. Never undone: a failure is reported in plain words, and installing
        the plan again finishes it"""
        c = self.c; act = pf["hakchi"]["action"]; out = {"action": act, "ok": False}; stage = f"{HP.STAGE_ROOT}/{self.attempt}"; staged: dict[str, bytes] = {}
        self._progress("hakchi", action=act)
        try:
            same_console(c, self.device, "hakchi")
            self._program_put()
            if act == "unpublish":
                r = self._run_program("remove")
                out.update(ok=True, removed=[l.split(" ", 1)[1] for l in r["lines"] if l.startswith("removed ")], kept=[l.split(" ", 1)[1] for l in r["lines"] if l.startswith("kept")],
                           text="The Hakchi games Classic Cart Studio added earlier were removed from Original Menu.")
                return out
            hkp = self.plan["hakchi"]; files = self.plan.get("package_files") or {f["path"]: f["sha256"] for f in self.plan["files"]}
            sp = HP.spec(self.pkg, files)
            if HP.summary(sp) != hkp["projection"]: raise InstallStopped("hakchi", "the collection's games on this computer are not the ones the plan names (prepare it again)")
            w = self._run_program("where")["where"]
            if w.get("mode") not in HP.MODES or not w.get("menu"): raise InstallStopped("hakchi", "the console program could not say where the console's menu lists games")
            where = {"mode": w["mode"], "menu": w["menu"], "store": None if w.get("store") in (None, "-") else w["store"], "system": w.get("system")}
            lay = HP.layout(sp, where["mode"], vcopy, self.where); staged = HP.stage_files(lay)
            self._stage_put(stage, staged, HP.stage_sources(lay))
            ck = self._run_program("check", stage)
            foreign = [m for m, s in ck["menus"].items() if s != "free" and s != "ours"]
            if foreign: raise InstallStopped("hakchi", f"a folder in hakchi's game storage ({where['store']}/{foreign[0]}) is not Classic Cart Studio's; it is kept, and no Hakchi games were added")
            skip = sorted(k for k, s in ck["entries"].items() if s not in ("free", "ours"))
            top = f"root/{sp['collection']['code']}"
            if where["mode"] == "storage" and top in skip:   # the collection's folder entry is what opens all the others
                raise InstallStopped("hakchi", f"an entry with the code of this collection's folder ({sp['collection']['code']}) is already in the console menu's first page; it is kept, and no Hakchi games were added")
            gen = int(pf["hakchi"].get("generation") or 0) + 1
            doc = HP.index_doc(sp, lay, where, ck["entries"], gen, producer={"name": "Classic Cart Studio", "projection": projection_id(hkp["projection"], self._ref(vcopy))})
            ib = HP.index_bytes(doc); ab = HP.apply_file(skip, _sha(ib), pf["hakchi"].get("index_sha256"), gen)
            staged |= {"index.json": ib, "apply": ab}; self._stage_put(stage, {"index.json": ib, "apply": ab})
            r = self._run_program("apply", stage)
            if c.sha256(HP.INDEX_PATH) != _sha(ib): raise InstallStopped("hakchi", "the index on the console does not read back as written")
            pub = [g for g in doc["games"].values() if g["status"] == "published"]
            notp = [{"title": g["title"], "why": g.get("reason", "")} for g in doc["games"].values() if g["status"] != "published"]
            out.update(ok=True, mode=where["mode"], published=len(pub), not_published=notp, generation=gen, index_sha256=_sha(ib), result=r["result"],
                       text=(f"{len(pub)} of this collection's games were added to Original Menu as Hakchi games" + (f', in a folder named "{sp["collection"]["name"]}".' if where["mode"] == "storage" and pub else ".")
                             + ("" if not notp else f" {len(notp)} could not be added.")))
        except (Refused, InstallStopped, ConnectionError, OSError, ValueError, HP.PublishError) as e:
            why = getattr(e, "why", None) or str(e)
            self.log(op="hakchi", ok=False, why=why)
            out.update(ok=False, why=why, text=(f"This collection's game files were copied and checked, but its games could not be added to Original Menu as Hakchi games: {why}. Install the collection again to finish it."
                                                if self.games and act != "unpublish" else
                                                f"Classic Cart Studio's HOME was installed, but this collection's games could not be {'removed from' if act == 'unpublish' else 'added to'} Original Menu as Hakchi games: {why}. Install the collection again to try once more."))
        finally:
            if staged:
                try: self._stage_remove(stage, staged)
                except (ConnectionError, OSError, InstallStopped) as e: out.setdefault("kept", []).append(f"{stage} ({e})"); out["stage_failed"] = getattr(e, "why", None) or str(e)
        self.log(op="hakchi", **{k: v for k, v in out.items() if k != "not_published"}); self._progress("hakchi", ok=out["ok"])
        return out

    def _ref(self, vcopy: str) -> str:
        """the copy the Hakchi entries launch game files from, as the index's producer names it (a games copy: games/<copy>)"""
        return f"games/{vcopy}" if self.games else vcopy

    def _run_program(self, command: str, stage: str | None = None) -> dict:
        """the console program, as the transport worker runs it (only the pinned files): its output read; anything but its success stops"""
        self.log(op="program", command=command, state="intent", stage=stage)
        r = HP.parse_output(self.c.collections(command, stage))
        self.log(op="program", command=command, state="done", result=r["result"])
        if not r["ok"]: raise InstallStopped("hakchi", r["result"][len("result " + command + " "):] or f"the console program did not answer {command}")
        return r

    def _program_put(self):
        """the console program and its library in their folder (named by the program's SHA-256), its record first; nothing when they are
        already there exactly"""
        c = self.c; prog = HP.program()
        if prog["pins"] != HP.PROGRAM_PINS: raise InstallStopped("hakchi", "this installation's console program for Hakchi games is not the pinned one (install Classic Cart Studio again)")
        d = prog["dir"]; k16 = d.rsplit("-", 1)[1]; want = {f"{d}/{n}": _sha(b) for n, b in prog["files"].items()}
        if all(c.sha256(p) == s for p, s in want.items()) and k16 in read_records(c)["collections"]: return
        rb = _record_bytes({"schema": COLLECTIONS_RECORD, "dir": d, "files": want}); rp = f"{INST_DIR}/collections-{k16}.json"
        for q in (ROOT_DIR, INST_DIR):
            if c.stat(q)["type"] == "none": self.log(op="mkdir", state="intent", path=q); c.mkdir(q)
        if c.stat(rp)["type"] == "none": self.log(op="record-program", state="intent", path=rp); c.put(rp, rb)
        self._sync(why="the console program's record, before its files")
        for q in (HP.INTEGRATION, d):
            if c.stat(q)["type"] == "none": self.log(op="mkdir", state="intent", path=q); c.mkdir(q)
        for n, b in prog["files"].items():
            p = f"{d}/{n}"
            if c.sha256(p) == _sha(b): continue
            if c.stat(p)["type"] != "none": raise InstallStopped("hakchi", f"{p} is there but is not this program's file; it is kept")
            self.log(op="put", state="intent", path=p, sha256=_sha(b)); c.put(p, b, "0o644")
        self._sync(why="the console program")

    def _stage_put(self, stage: str, files: dict[str, bytes], sources: dict[str, str] | None = None):
        """the stage files, each checked against its SHA-256 where it is written; sources: those sent from the package's own file on this
        computer (hakchi_publish.stage_sources), which the transport worker reads inside the package folder and checks before writing"""
        c = self.c; made = set()
        for rel in sorted(files):
            p = f"{stage}/{rel}"; parts = p.split("/")
            for i in range(len(HP.INTEGRATION.split("/")), len(parts)):
                q = "/".join(parts[:i])
                if q in made: continue
                if c.stat(q)["type"] == "none": self.log(op="mkdir", state="intent", path=q); c.mkdir(q)
                made.add(q)
            if c.sha256(p) == _sha(files[rel]): continue
            src = (sources or {}).get(rel)
            self.log(op="put", state="intent", path=p, sha256=_sha(files[rel]), **({"source": src} if src else {}))
            c.put(p, files[rel], "0o644", source=os.path.join(self.pkg, *src.split("/")) if src else None)
        self._sync(why="the staged publication")

    def _stage_remove(self, stage: str, files: dict[str, bytes]):
        """the staged files, each only while it is still the bytes put there; then their folders when empty"""
        c = self.c
        for rel, b in sorted(files.items()):
            p = f"{stage}/{rel}"
            if c.stat(p)["type"] == "file": c.remove(p, _sha(b))
        dirs = sorted({f"{stage}/" + "/".join(rel.split("/")[:i]) for rel in files for i in range(1, rel.count("/") + 1)} | {stage}, key=lambda d: -d.count("/"))
        for d in dirs + [HP.STAGE_ROOT]:
            if c.stat(d)["type"] == "dir" and not c.listdir(d): c.rmdir(d)

    def _tidy_programs(self, recs: dict, keep_current: bool, tidy: list):
        """earlier console programs this program put there (their records), and this one too when no Hakchi games remain: removed file by
        file while unchanged, then their record (each appended to tidy). A removal the console does not confirm raises CleanupStopped"""
        c = self.c; cur = HP.PROGRAM_PINS["ccs-collections"][:16]
        if not keep_current and (c.stat(HP.JOURNAL)["type"] != "none" or c.stat(HP.INDEX_PATH)["type"] != "none"): keep_current = True   # still needed to remove what is published
        for k16, r in sorted(recs.get("collections", {}).items()):
            if k16 == cur and keep_current: continue
            path = r.get("_record") or k16
            try:
                for p, s in sorted((r.get("files") or {}).items()):
                    path = p
                    if c.stat(p)["type"] == "file": self.log(op="remove-file", path=p, sha256=s); c.remove(p, s)
                for d in (r["dir"], HP.INTEGRATION):
                    path = d
                    if c.stat(d)["type"] == "dir" and not c.listdir(d): self.log(op="rmdir", path=d); c.rmdir(d)
                left = [p for p in (r.get("files") or {}) if c.stat(p)["type"] != "none"]
                if not left:
                    path = r["_record"]; b = c.read(r["_record"], 65536)
                    if b is not None: self.log(op="remove-record", path=r["_record"], sha256=_sha(b)); c.remove(r["_record"], _sha(b))
                tidy.append({"program": k16, "removed": not left, "kept": left})
            except (ConnectionError, OSError) as e:
                tidy.append({"program": k16, "removed": False, "why": str(e)})
                raise CleanupStopped(f"removing the earlier console program {k16}", str(e), [path]) from e

    # ---- undo (stand-ins automatically; a real console only through resume(), its own approved operation)
    def _put_intents(self) -> set:
        try: return {e["path"] for e in self.entries(self.journal) if e.get("op") == "put" and e.get("state") == "intent" and isinstance(e.get("path"), str)}
        except JournalCorrupt: return set()
    def _put_written(self) -> dict:
        """each path this attempt wrote, with the SHA-256 the console reported right after that write (a damaged transfer included)"""
        out, last = {}, None
        try:
            for e in self.entries(self.journal):
                if e.get("op") != "put": continue
                if e.get("state") == "intent": last = e.get("path")
                elif e.get("state") == "done" and last and isinstance(e.get("written_sha256"), str): out[last] = e["written_sha256"]
        except JournalCorrupt: return {}
        return out
    def _partial_sha(self, rel: str, f: dict, size) -> str | None:
        """a transfer cut short leaves the first bytes of the planned file: the SHA-256 of that many first bytes of the plan's file as this
        computer still has it (only when that file is still exactly the plan's); None otherwise"""
        if not isinstance(size, int) or size < 0 or size >= f["size"]: return None
        try: data = self._data(f)
        except (OSError, InstallStopped, ValueError, HP.PublishError): return None
        return _sha(data[:size]) if _sha(data) == f["sha256"] else None
    def _remove_attempt_copy(self) -> list[str]:
        """this attempt's own copy, file by file: a file goes only when its bytes are the plan's, or exactly what the console reported holding
        right after this attempt's own write of it (a damaged transfer), or when this attempt began writing it and it holds exactly the first
        bytes of the plan's file (a transfer cut short). Anything else is kept and reported: a current hash is never a reason to remove
        something"""
        c = self.c; base = self.target; problems = []
        st = c.stat(base)["type"]
        if st == "none": return []
        if st != "dir": return [f"{base} is a {st}, not this attempt's folder; it is kept"]
        planned = {f["path"]: f for f in self.plan["files"]}; begun = self._put_intents(); written = self._put_written()
        for rel, f in sorted(planned.items()):
            p = f"{base}/{rel}"; s = c.stat(p)
            if s["type"] == "none": continue
            if s["type"] != "file": problems.append(f"{p} is a {s['type']}, not a file this attempt wrote; it is kept"); continue
            h = c.sha256(p)
            if h == f["sha256"] or (h is not None and h == written.get(p)) or (p in begun and h is not None and h == self._partial_sha(rel, f, s.get("size"))):
                if not c.remove(p, h): problems.append(f"{p} changed while it was being removed; it is kept")
            else: problems.append(f"{p} holds bytes this attempt did not write (neither the plan's file nor a transfer cut short); it is kept")
        r = remove_copy(c, self.copy, {"files": {k: v["sha256"] for k, v in planned.items()}}, where=self.where)
        if not r["removed"] and not problems: problems.append(f"{base} keeps {len(r['kept'])} item(s) this attempt did not write; they are kept")
        return problems

    def _rollback(self, undo: list[dict], prior: dict | None) -> dict:
        """each change undone in reverse order, only over exactly what this attempt made; at the first thing that is not, nothing more is
        written: the attempt's records stay on the console and the result says what is known"""
        c = self.c; done, problems, kept = [], [], []
        mine_ptr = _sha(pointer_bytes(self.copy))
        for u in reversed(undo):
            try:
                op = u["op"]
                if op == "pointer":
                    st = c.stat(POINTER)["type"]; now = c.sha256(POINTER) if st == "file" else None
                    if u["value"]:
                        if now == _sha(pointer_bytes(u["value"])): pass   # never switched, or already back
                        elif now == mine_ptr: c.set_pointer(POINTER, u["value"], expect=mine_ptr)
                        else: problems.append(f"{POINTER} names neither the earlier copy nor this attempt's; it is kept as it is")
                    elif st != "none":
                        if now != mine_ptr or not c.remove(POINTER, mine_ptr): problems.append(f"{POINTER} is not this attempt's pointer; it is kept as it is")
                elif op == "module":   # an add-on this attempt installed: removed by hakchi's pack_uninstall (its own removal), only while exactly this attempt's
                    a = u.get("addon") or next((k for k, n in ADDONS.items() if n == u.get("name")), None)
                    ap = addon_package(a); now = c.method_files(ap["name"])
                    if u["prior"] == "absent":
                        if now is not None and now.get("package") == ap["package_id"] and all((now.get("files") or {}).get(p) == s for p, s in ap["files"].items()):
                            c.pack_uninstall(ap["name"])
                            if a == "nucleus": kept.append(f"CCS Nucleus's own settings ({ROOT_DIR}/nucleus) stay, as its own removal keeps them; without CCS Nucleus they change nothing at power-on")
                        elif now is not None: problems.append(f"{ap['name']}'s package is not exactly the one this attempt installed (or its files were changed since); it is left as it is")
                    elif now is None or now.get("package") != u.get("prior_package"):
                        problems.append(f"{ap['name']}'s earlier version was updated by this attempt; it is not put back (the newer one stays)")
                elif op == "nucleus-start":   # CCS Nucleus's start back as it was before this attempt set it, by its own command, only while it is still this attempt's
                    now = nucleus_start_state(c)
                    if u["prior"] is None:
                        if u.get("nucleus_before", "absent") != "absent": problems.append("CCS Nucleus had no start setting before this attempt set one; that one is left as it is")
                        # (CCS Nucleus arrived with this attempt: its own removal, next, takes it out of the console's start)
                    elif now.get("line") == u["prior"]: pass
                    elif now.get("line") == u["want"] and c.method_files(ADDONS["nucleus"]) is not None:
                        parts = u["prior"].split(" ")
                        if parts[0] not in STARTS: problems.append("CCS Nucleus's start could not be put back: it began with one of your own system images, which only the CCS Console Workbench sets; it is left as this attempt set it")
                        else:
                            r = str(c.nucleus_start(*parts) or "")
                            if not r.startswith("result start ok ") or nucleus_start_state(c).get("line") != u["prior"]: problems.append("CCS Nucleus's start could not be put back; it is left as it is")
                    else: problems.append("CCS Nucleus's start is neither as it was nor as this attempt set it; it is left as it is")
                elif op == "remove":
                    st = c.stat(u["path"])["type"]
                    if st == "file" and not c.remove(u["path"], u["sha256"]): problems.append(f"{u['path']} changed after it was written; it is kept")
                    elif st not in ("file", "none"): problems.append(f"{u['path']} is now a {st}, not the file written; it is kept")
                elif op == "rmdir-if-empty":
                    if c.stat(u["path"])["type"] == "dir" and not c.listdir(u["path"]): c.rmdir(u["path"])
                elif op == "remove-copy":
                    problems += self._remove_attempt_copy()
                if problems: break
                done.append(u.get("path") or (self.target if op == "remove-copy" else op))
            except (ConnectionError, OSError, PermissionError, ValueError) as e:
                problems.append(f"undoing {u.get('op')} failed: {e}"); break
        if not problems:
            try: self._sync(drop=True, why="the undo, before it is verified")
            except (ConnectionError, OSError, InstallStopped) as e: problems.append(f"the undo could not be made persistent: {e}")
        v = self._verify_prior(prior) if not problems else {"ok": False, "checks": {}}
        ok = not problems and v["ok"]
        out = {"ok": ok, "undone": done, "verified": v["checks"]}
        if kept: out["kept"] = kept
        if problems: out["problems"] = problems
        if not ok: out["next"] = "The rollback is not complete: no further writes. The journal and the console's attempt record stay and say what is where; collect diagnostics."
        return out

    def _verify_prior(self, prior: dict | None) -> dict:
        c = self.c; checks = {}
        if prior is None: return {"ok": True, "checks": {"nothing": "changed"}}
        b = c.read(POINTER, 64); ptr = b.decode(errors="replace").strip() if b is not None else None
        checks["pointer"] = ptr == prior["pointer"]
        for a, n in ADDONS.items():   # every add-on as it was: not listed, or listed with the same package
            was = (prior.get("addons") or {}).get(a); mf = c.method_files(n)
            checks[f"addon {n}"] = mf is None if was is None else (mf or {}).get("package") == was.get("package")
        if "nucleus" in (prior.get("addons") or {}) and prior.get("start") is not None: checks["start"] = nucleus_start_state(c).get("line") == prior["start"]
        checks["copy_removed"] = c.stat(self.target)["type"] == "none"
        checks["attempt_record_removed"] = c.stat(f"{INST_DIR}/attempt-{self.attempt}.json")["type"] == "none"
        checks["copy_record_removed"] = c.stat(self.copy_record)["type"] == "none"
        return {"ok": all(checks.values()), "checks": checks}

    def resume(self) -> dict:
        """recover an interrupted attempt from ITS journal: only on the console that attempt was on (read now, compared with the journal, the
        console's attempt record and the confirmed console) and only for its plan; a finished attempt is never undone. On a real console a
        recovery is its own confirmed operation (G3), in a session opened for the install tier"""
        if getattr(self.c, "simulated", False) is not True: gate.require("install", "recover an attempt on a real console")
        try: es = self.entries(self.journal)
        except JournalCorrupt as e: return {"ok": False, "result": "unknown", "why": f"{e}; nothing was changed. Collect diagnostics"}
        if not es: return {"ok": True, "result": "nothing to resume", "why": "no journal"}
        begin = es[0]; attempt = begin.get("attempt"); ends = [e for e in es if e.get("op") == "end"]
        if begin.get("op") != "begin" or not re.match(r"^[0-9a-f]{8}$", str(attempt)): return {"ok": False, "result": "unknown", "why": "the journal does not begin with its attempt; nothing was changed"}
        if begin.get("plan") != self.plan["hash"]: return {"ok": False, "result": "refused", "why": "this journal belongs to another plan"}
        if ends and ends[-1].get("result") not in ("stopped-needs-recovery", "installed-record-open", "cleanup-stopped", "rollback-incomplete"): return {"ok": True, "result": "nothing to resume", "why": f"that attempt ended ({ends[-1].get('result')})"}
        try: dev = check_device(self.c.identity(fresh=True))
        except Refused as e: return {"ok": False, "result": "refused", "why": e.why}
        pf = next((e for e in es if e.get("op") == "preflight"), None); jh = ((pf or {}).get("identity") or {}).get("hwid")
        if self.device and dev["hwid"] != self.device: return {"ok": False, "result": "refused", "why": "this is not the console that was checked and confirmed; nothing was changed"}
        if jh and dev["hwid"] != jh: return {"ok": False, "result": "refused", "why": "the console answering now is not the one this attempt changed; nothing was changed"}
        self.device = dev["hwid"]
        self.attempt = attempt; self._name(attempt)
        if COPY_RE.match(str(begin.get("copy") or "")): self.copy = begin["copy"]; self.target = f"{self.where}/{self.copy}"; self.copy_record = f"{INST_DIR}/{'games' if self.games else 'v'}-{self.copy}.json"
        path = f"{INST_DIR}/attempt-{attempt}.json"; b = self.c.read(path, 1_000_000)
        rec = None
        try: rec = json.loads(b) if b is not None else None
        except ValueError: pass
        if rec is None:
            if any(e.get("op") == "record-attempt" and e.get("state") == "intent" for e in es) and self.c.stat(path)["type"] != "none":
                return {"ok": False, "result": "unknown", "why": f"{path} cannot be read; nothing was changed"}
            if any(e.get("op") == "installed" for e in es):
                self.log(op="end", result="installed", how="its record was already closed"); return {"ok": True, "result": "installed", "why": "the attempt had finished; only its last journal line was missing"}
            undo = [e["undo"] for e in es if e.get("state") == "intent" and e.get("undo")]
            if not undo: self.log(op="end", result="nothing-changed"); return {"ok": True, "result": "nothing to resume", "why": "the attempt stopped before changing the console"}
            # the console has no record of it: if it kept none of the attempt's changes (it lost power before they were persistent), or only
            # empty folders this attempt meant to make, those folders are the whole of it; anything else is unknown
            intended = [e["path"] for e in es if e.get("state") == "intent" and isinstance(e.get("path"), str) and e.get("op") in ("mkdir", "record-attempt", "record-copy", "put", "data-identity", "record-module")]
            folders = {e["path"] for e in es if e.get("state") == "intent" and e.get("op") == "mkdir" and isinstance(e.get("path"), str)}
            left = [q for q in dict.fromkeys(intended) if self.c.stat(q)["type"] != "none"]
            if left and all(q in folders and self.c.stat(q)["type"] == "dir" for q in left):
                for q in sorted(left, key=lambda q: -q.count("/")):
                    if not self.c.listdir(q): self.log(op="rmdir", state="intent", path=q); self.c.rmdir(q)
                left = [q for q in left if self.c.stat(q)["type"] != "none"]
            if not left: self.log(op="end", result="nothing-changed", how="the console kept none of this attempt's changes"); return {"ok": True, "result": "nothing to resume", "why": "the console kept none of this attempt's changes"}
            return {"ok": False, "result": "unknown", "why": "the journal shows changes but the console has no record of this attempt; nothing was changed. Collect diagnostics"}
        if not jh or rec.get("attempt") != attempt or rec.get("plan") != self.plan["hash"] or (rec.get("device") or {}).get("hwid") != dev["hwid"]:
            return {"ok": False, "result": "refused", "why": "the console's attempt record is for another attempt, plan or console; nothing was changed"}
        if any(e.get("op") == "installed" for e in es):   # the switch was verified: finish instead of undoing
            try: self.log(op="finish", state="intent", path=path); self.c.remove(path, _sha(b)); self._sync(why="the closed attempt")
            except (ConnectionError, OSError, InstallStopped) as e: return {"ok": False, "result": "unknown", "why": str(e)}
            self.log(op="end", result="installed", how="finished after an interruption"); return {"ok": True, "result": "installed", "why": "the installation had been verified; its record was closed"}
        pr = rec.get("prior") or {}; prior = {"pointer": pr.get("pointer"), "addons": pr.get("addons") or {}, "start": pr.get("start")}
        undo = [e["undo"] for e in es if e.get("state") == "intent" and e.get("undo")]
        rb = self._rollback(undo, prior); self.log(op="end", result="rolled-back" if rb["ok"] else "rollback-incomplete", rolled_back=rb, how="after an interruption")
        return {"ok": rb["ok"], "result": "rolled-back" if rb["ok"] else "rollback-incomplete", "rolled_back": rb, "attempt": attempt}

# ---------------------------------------------------------------- inspect, verify, uninstall, export
def inspect_console(c, device: str | None = None) -> dict:
    """what is on the console now (reads only): its identity against the supported console, this program's records, the copy in use, its
    add-ons (each package hakchi lists, as a choice, and CCS Nucleus's start, with both in plain words), attempts that did not finish, the
    saves folder and the room left. A refusal is reported, never a guess"""
    if getattr(c, "simulated", False) is not True: gate.require("inspect", "read a real console")
    try: dev = check_device(c.identity(fresh=True), "inspect")
    except Refused as e: return {"ok": False, "why": e.why, "identity": {k: v for k, v in c.identity().items() if k != "hwid"}}
    if device and dev["hwid"] != device: return {"ok": False, "why": "this is not the console that was checked before; check it again"}
    recs = read_records(c); pst = c.stat(POINTER); pb = c.read(POINTER, 64) if pst["type"] == "file" else None
    pv = pb.decode("ascii", "replace").strip() if pb is not None else None
    in_use = pv if pb is not None and COPY_RE.match(pv or "") and pv in recs["versions"] and pb == pointer_bytes(pv) else None
    ms = installed_addons(c); module = "; ".join(f"{x['name']} ({x['state']})" for x in ms) or "absent"
    copy = None
    if in_use:
        s = copy_state(c, in_use, recs["versions"][in_use]); copy = {"copy": in_use, "plan": recs["versions"][in_use].get("plan"), "files": len(s["ours"]), "changed": s["changed"][:20], "missing": s["missing"][:20]}
    data = {"present": c.stat(DATA_DIR)["type"] == "dir"}
    try: ad, ad_why = console_addons(c), None
    except Refused as e: ad, ad_why = None, e.why
    return {"ok": True, "device": dev, "in_use": copy, "pointer": "this program's" if in_use else ("absent" if pst["type"] == "none" else "not this program's"), "module": module, "packages": ms,
            "addons": (ad or {}).get("choice"), "addons_text": (ad or {}).get("text"), "addons_problem": ad_why, "start": ((ad or {}).get("start") or {}).get("line"),
            "earlier_module": legacy_state(c, recs), "attempts": sorted(recs["attempts"]), "copies": sorted(recs["versions"]), "games_copies": sorted(recs["games"]), "record_problems": recs["problems"][:10],
            "data": data, "free_bytes": c.free_bytes(ROOT_DIR), "hakchi_games": _hakchi_summary(c, recs)}

def _hakchi_summary(c, recs: dict) -> dict:
    """this collection's Hakchi games as the console's index says (reads only): none, or the index's generation and how many are published"""
    hs = hakchi_state(c, recs); idx = hs["index"]
    if not idx: return {"state": "none" if not hs["index_sha256"] and not hs["journal"] else "unreadable", "program": hs["program"]}
    pub = sum(1 for g in idx["games"].values() if isinstance(g, dict) and g.get("status") == "published")
    return {"state": "published", "generation": idx["generation"], "games": pub, "not_published": len(idx["games"]) - pub, "program": hs["program"]}

def verify_installed(c, plan: dict, device: str | None = None) -> dict:
    """the console has exactly this plan's copy: with CCS HOME, the pointer naming it; without CCS Mod, this plan's games copy (games_only.py);
    every file of the copy (after a sync with the page cache dropped, as the storage holds them), every add-on of the plan's choice (this
    installation's package, unchanged), CCS Nucleus's start and the Hakchi games as planned. Reads only (sync writes nothing)"""
    if getattr(c, "simulated", False) is not True: gate.require("inspect", "read a real console")
    try: dev = check_device(c.identity(fresh=True), "verify")
    except Refused as e: return {"ok": False, "verified": False, "why": e.why}
    if device and dev["hwid"] != device: return {"ok": False, "verified": False, "why": "this is not the console that was checked before; check it again"}
    recs = read_records(c); games = plan.get("delivery") == "games"
    s = getattr(c, "sync", None)
    if games:
        if s: s(drop_caches=True)
        pv = f"{plan['version']}.{'0' * 8}" if not plan["files"] else games_copy_of(c, recs, plan)
        if not pv: return {"ok": True, "verified": False, "why": "the console does not have this plan's game files", "in_use": None}
        base = f"{GAMES_DIR}/{pv}"; ref = f"games/{pv}"
    else:
        pb = c.read(POINTER, 64); pv = (pb or b"").decode("ascii", "replace").strip()
        rec = recs["versions"].get(pv) if pb is not None and pb == pointer_bytes(pv) else None
        if not rec or rec.get("manifest_sha256") != plan["manifest_sha256"]: return {"ok": True, "verified": False, "why": "the console does not start this plan's copy", "in_use": pv if rec else None}
        if s: s(drop_caches=True)
        base = f"{PKG_DIR}/{pv}"; ref = pv
    files = {f["path"]: f["sha256"] for f in plan["files"]}
    bad = [rel for rel, h in files.items() if c.sha256(f"{base}/{rel}") != h]
    try: addons_ok = all(_addon_current(c, a) for a in choice_addons(plan["addons"]))
    except ChooserUnavailable: addons_ok = False
    st = nucleus_start_state(c).get("line") if plan["start_line"] else None
    hkp = plan.get("hakchi") or {"publish": False}; hs = hakchi_state(c, recs)
    published = bool(hs["journal"] or hs["index_sha256"])
    if hkp["publish"] and (hkp["review"]["games"] or published): hk_ok = bool(hs["program"] and hs["index"] is not None and (hs["index"].get("producer") or {}).get("projection") == projection_id(hkp["projection"], ref))
    else: hk_ok = not published
    ok = not bad and addons_ok and st == plan["start_line"] and hk_ok
    return {"ok": True, "verified": ok, "copy": pv, "delivery": "games" if games else "home", "mismatches": bad[:20], "module": "current" if addons_ok else "not as planned", "addons": plan["addons"], "start": st,
            "start_as_planned": st == plan["start_line"], "hakchi_as_planned": hk_ok, "attempts": sorted(recs["attempts"]), "device": dev["hwid"]}

def uninstall_plan(c, keep_data: bool = True, device: str | None = None) -> dict:
    """exactly what an uninstall would do, read from the console (nothing is changed): its actions in order, each naming the bytes it removes
    (by SHA-256), what is kept, and a hash over all of it for the confirmation. uninstall() rebuilds this and runs it only when it is still
    the same; a pointer, copy file or record is removed only when it is this program's by its records and its bytes. An add-on is removed
    only when this program's records say it installed that exact package (one another tool added, such as the CCS Console Workbench, stays),
    CCS Nucleus's startup chooser before CCS Nucleus, and CCS Nucleus never while a startup chooser that stays needs it"""
    if getattr(c, "simulated", False) is not True: gate.require("inspect", "read a real console")
    try: dev = check_device(c.identity(fresh=True), "uninstall")
    except Refused as e: return {"ok": False, "why": e.why}
    if device and dev["hwid"] != device: return {"ok": False, "why": "this is not the console that was checked and confirmed; nothing would be changed"}
    for d in (ROOT_DIR, PKG_DIR, INST_DIR, DATA_DIR, GAMES_DIR):
        t = c.stat(d)["type"]
        if t not in ("none", "dir"): return {"ok": False, "why": f"{d} is {'a link' if t == 'link' else 'not a folder'}; nothing would be changed"}
    recs = read_records(c)
    if recs["problems"]: return {"ok": False, "why": "this program's records on the console cannot all be read: " + "; ".join(recs["problems"][:3])}
    if recs["attempts"]: return {"ok": False, "why": f"an attempt ({sorted(recs['attempts'])[0]}) did not finish; recover it first"}
    actions, remove, keep = [], [], []
    # first this collection's Hakchi games (they name the copies removed below): by this program's console program, only the entries it
    # added and are unchanged; then the program itself, file by file, and its record
    hs = hakchi_state(c, recs)
    if hs["journal"] or hs["index_sha256"]:
        if hs["program"]: actions.append({"op": "collections-remove"}); remove.append("this collection's Hakchi games in Original Menu (only the entries Classic Cart Studio added, while unchanged; every other game and entry stays)")
        else: keep.append("this collection's Hakchi games in Original Menu: the console program that removes them is not on the console (install the collection again, then remove it)")
    for k16, r in sorted(recs["collections"].items()):
        rb = c.read(r["_record"], 65536); fl = r.get("files") or {}
        actions += [{"op": "remove", "path": p, "sha256": s} for p, s in sorted(fl.items()) if c.sha256(p) == s]
        keep += [f"{p} (changed)" for p, s in sorted(fl.items()) if c.stat(p)["type"] != "none" and c.sha256(p) != s]
        actions.append({"op": "rmdir-if-empty", "path": r["dir"]})
        if rb is not None: actions.append({"op": "remove-record", "path": r["_record"], "sha256": _sha(rb), "when_gone": r["dir"]})
        remove.append(f"{r['dir']}: the console program for Hakchi games")
    listed = {x["addon"]: x for x in installed_addons(c)}; legacy = legacy_state(c, recs); mine = studio_installed(recs); going = []
    what = {"nucleus-chooser": "the startup chooser and its menu entry leave the console", "nucleus": "CCS Nucleus no longer decides what opens at power-on",
            "mod": "the CCS HOME tile leaves Original Menu"}
    for a in ("nucleus-chooser", "nucleus", "mod"):   # the reverse of the install order: CCS Nucleus's startup chooser before CCS Nucleus
        x = listed.get(a)
        if not x: continue
        if x["state"] == "not this program's": keep.append(f"{x['name']} (a package this installation does not recognize)"); continue
        if (x["name"], x["package"]) not in mine: keep.append(f"{ADDON_LABELS[a]} ({x['name']}): Classic Cart Studio did not add it (another tool did, such as the CCS Console Workbench); it stays as it is"); continue
        if a == "nucleus" and "nucleus-chooser" in listed and "nucleus-chooser" not in going: keep.append(f"CCS Nucleus ({x['name']}): its startup chooser, which stays, needs it; it stays as it is"); continue
        actions.append({"op": "addon-uninstall", "name": x["name"], "addon": a, "package": x["package"]}); going.append(a)
        remove.append(f"{ADDON_LABELS[a]} ({x['name']}, through hakchi's pack_uninstall: its own removal takes only its unchanged files; its settings stay): {what[a]}")
    if "mod" in going and "nucleus" in listed and "nucleus" not in going:
        try: home_offered = nucleus_start_state(c).get("home") == "on"
        except (ConnectionError, OSError): home_offered = False
        if home_offered: keep.append("CCS Nucleus's settings stay as they are: while CCS HOME is not installed, its CCS HOME choice cannot start and Original Menu opens instead")
    if legacy == "mine":
        actions.append({"op": "legacy-uninstall"}); remove.append(f"this program's earlier start-up module {LEGACY_NAME} (hakchi's own list for it)")
    elif legacy == "foreign": keep.append(f"{LEGACY_NAME} (a module this program did not make)")
    pst = c.stat(POINTER); pb = c.read(POINTER, 64) if pst["type"] == "file" else None; pv = pb.decode("ascii", "replace").strip() if pb is not None else None
    owned_ptr = pb is not None and bool(COPY_RE.match(pv or "")) and pv in recs["versions"] and pb == pointer_bytes(pv)
    if owned_ptr: actions.append({"op": "remove", "path": POINTER, "sha256": _sha(pb)}); remove.append(POINTER)
    elif pst["type"] != "none": keep.append(f"{POINTER} (not this program's pointer)")
    for v, r in sorted(recs["versions"].items()):
        base = f"{PKG_DIR}/{v}"; s = copy_state(c, v, r); files = r.get("files") or {}
        if (hs["journal"] or hs["index_sha256"]) and not hs["program"]:   # Hakchi games that stay may launch this copy's game files: kept with them
            keep.append(f"{base}: CCS HOME's copy, whose game files the Hakchi games in Original Menu open, which stay with them"); continue
        actions += [{"op": "remove", "path": f"{base}/{rel}", "sha256": files[rel]} for rel in sorted(s["ours"])]
        dirs = sorted({"/".join(rel.split("/")[:i]) for rel in files for i in range(1, rel.count("/") + 1)}, key=lambda d: (-d.count("/"), d))
        actions += [{"op": "rmdir-if-empty", "path": f"{base}/{d}" if d else base} for d in dirs + [""]]
        rb = c.read(r["_record"], 1_000_000)
        others = [f"{base}/{x} (changed)" for x in s["changed"]] + [f"{base}/{x} (not this program's)" for x in s["unknown"]]
        if others: keep += others + [f"{r['_record']} (the record of {base}, which keeps those items)"]
        elif rb is not None: actions.append({"op": "remove-record", "path": r["_record"], "sha256": _sha(rb), "when_gone": base})
        remove.append(f"{base}: {len(s['ours'])} file(s) of this program")
    for v, r in sorted(recs["games"].items()):   # the games copies (games_only.py), after the Hakchi games that launch their files
        base = f"{GAMES_DIR}/{v}"; s = copy_state(c, v, r, GAMES_DIR); files = r.get("files") or {}
        if (hs["journal"] or hs["index_sha256"]) and not hs["program"]:   # Hakchi games that stay may launch these files: kept with them
            keep.append(f"{base}: the game files the Hakchi games in Original Menu open, which stay with them"); continue
        actions += [{"op": "remove", "path": f"{base}/{rel}", "sha256": files[rel]} for rel in sorted(s["ours"])]
        dirs = sorted({"/".join(rel.split("/")[:i]) for rel in files for i in range(1, rel.count("/") + 1)}, key=lambda d: (-d.count("/"), d))
        actions += [{"op": "rmdir-if-empty", "path": f"{base}/{d}" if d else base} for d in dirs + [""]]
        rb = c.read(r["_record"], 1_000_000)
        others = [f"{base}/{x} (changed)" for x in s["changed"]] + [f"{base}/{x} (not this program's)" for x in s["unknown"]]
        if others: keep += others + [f"{r['_record']} (the record of {base}, which keeps those items)"]
        elif rb is not None: actions.append({"op": "remove-record", "path": r["_record"], "sha256": _sha(rb), "when_gone": base})
        remove.append(f"{base}: {len(s['ours'])} file(s) this program copied for the Hakchi games (game files and their notices)")
    for k16, r in sorted(recs["modules"].items()):   # a module record goes once nothing it names is installed (the add-ons removed here, the earlier module retired)
        rb = c.read(r["_record"], 65536)
        if rb is None: continue
        if record_gone(c, r) or (r.get("name"), r.get("package_id")) in {(ADDONS[a], listed[a]["package"]) for a in going} or (legacy == "mine" and (r.get("schema") == "ccs-owned-module/1" or r.get("name") == LEGACY_NAME)):
            actions.append({"op": "remove-record", "path": r["_record"], "sha256": _sha(rb), "when_gone_module": r.get("name") or LEGACY_NAME, "package_id": r.get("package_id")})
        else: keep.append(f"{r['_record']} (the record of {r.get('name')}, which stays)")
    keep += [f"{PKG_DIR}/{x} (not this program's)" for x in c.listdir(PKG_DIR) if x != "current" and x not in recs["versions"]]
    keep += [f"{GAMES_DIR}/{x} (not this program's)" for x in c.listdir(GAMES_DIR) if x not in recs["games"]]
    keep += [f"{ROOT_DIR}/{x} (not this program's)" for x in c.listdir(ROOT_DIR) if x not in KNOWN_ROOT]
    keep += [f"{ROOT_DIR}/{x} (the add-ons' own records and settings: kept)" for x in c.listdir(ROOT_DIR) if x in ADDON_RECORDS]
    if keep_data: keep.append(f"{DATA_DIR} (the console's saves, slots and settings: kept)")
    elif c.stat(DATA_DIR)["type"] == "dir":
        dfiles, ddirs = [], []
        def walk(p):
            for n in c.listdir(p):
                q = f"{p}/{n}"; t = c.stat(q)["type"]
                if t == "dir": ddirs.append(q); walk(q)
                elif t == "file": dfiles.append(q)
                else: keep.append(f"{q} ({t}, not followed)")
        walk(DATA_DIR)
        actions += [{"op": "remove", "path": q, "sha256": c.sha256(q)} for q in sorted(dfiles)]
        actions += [{"op": "rmdir-if-empty", "path": q} for q in sorted(ddirs, key=lambda d: (-d.count("/"), d)) + [DATA_DIR]]
        remove.append(f"{DATA_DIR}: {len(dfiles)} file(s) (the console's saves, slots and settings, as asked)")
    actions += [{"op": "rmdir-if-empty", "path": d} for d in (PKG_DIR, GAMES_DIR, INST_DIR, HP.STAGE_ROOT, HP.INTEGRATION, ROOT_DIR)]
    body = {"schema": "ccs-uninstall-review/1", "device": dev["hwid"], "keep_data": keep_data, "actions": actions, "keep": keep, "pointer_sha256": _sha(pb) if pb is not None else None}
    return {"ok": True, "remove": remove, "keep": keep, "module": bool([a for a in actions if a["op"] in ("addon-uninstall", "legacy-uninstall")]), "addons_removed": [ADDONS[a] for a in going],
            "addons_kept": [listed[a]["name"] for a in listed if a not in going], "copies": sorted(recs["versions"]), "games_copies": sorted(recs["games"]), "pointer": {"owned": owned_ptr, "value": pv if owned_ptr else None},
            "actions": actions, "device": dev["hwid"], "keep_data": keep_data, "pointer_sha256": body["pointer_sha256"], "hash": _sha(_canon(body))}

def uninstall(console, keep_data: bool = True, journal: str | None = None, expect: str | None = None, device: str | None = None) -> dict:
    """the reviewed removal, exactly: uninstall_plan() is read again and must still be the reviewed one (expect: its hash); then each action
    runs over exactly the bytes it names (the worker's remove compares the SHA-256 in the same command), so something changed since the review
    is kept and reported. The add-ons this program installed go first (CCS Nucleus's startup chooser before CCS Nucleus), then the earlier
    start-up module. Interrupted, the review is made again and shows what is left; every step is safe to repeat"""
    if getattr(console, "simulated", False) is not True: gate.require("install", "uninstall from a real console")
    c = console; plan = uninstall_plan(c, keep_data, device)
    if not plan["ok"]: return {"ok": False, "result": "refused", "why": plan["why"], "changed": False}
    if expect is not None and plan["hash"] != expect: return {"ok": False, "result": "refused", "why": "the console is not as it was when the removal was reviewed (or another console answers); review it again. Nothing was changed", "changed": False}
    def log(**k):
        if journal:
            with open(journal, "a", encoding="utf-8") as f: f.write(json.dumps({"t": round(time.time(), 3), "uninstall": plan["hash"], **k}) + "\n"); f.flush(); os.fsync(f.fileno())
    def sync(drop=False):
        s = getattr(c, "sync", None)
        if s is None: raise InstallStopped("sync", "this console session cannot make its writes persistent")
        log(op="sync", drop_caches=drop); s(drop_caches=drop)
    log(op="begin", device=plan["device"], keep_data=keep_data, actions=len(plan["actions"]))
    removed, kept, problems = [], [], []
    for a in plan["actions"]:
        op, path = a["op"], a.get("path")
        try:
            if op == "collections-remove":
                log(op=op, state="intent"); r = HP.parse_output(c.collections("remove")); sync()
                if not r["ok"]: problems.append(f"the Hakchi games could not be removed: {r['result']}"); break
                removed += [f"Hakchi game entry {l.split(' ', 1)[1]}" for l in r["lines"] if l.startswith("removed ")]
                kept += [f"{l.split(' ', 1)[1]} (a Hakchi game entry changed since Classic Cart Studio added it)" for l in r["lines"] if l.startswith("kept")]
            elif op == "addon-uninstall":
                if (c.method_files(a["name"]) or {}).get("package") != a["package"]: problems.append(f"{a['name']} changed since the review; it is kept and nothing more is changed"); break
                log(op=op, name=a["name"], state="intent"); c.pack_uninstall(a["name"]); sync(); removed.append(f"module {a['name']}")
            elif op == "legacy-uninstall":
                if legacy_state(c, read_records(c)) != "mine": problems.append(f"{LEGACY_NAME} changed since the review; it is kept and nothing more is changed"); break
                log(op=op, state="intent"); c.pack_uninstall(LEGACY_NAME); sync(); removed.append(f"module {LEGACY_NAME}")
            elif op == "remove":
                if c.stat(path)["type"] == "none": continue
                log(op=op, path=path, sha256=a["sha256"], state="intent")
                if c.remove(path, a["sha256"]): removed.append(path)
                else: kept.append(f"{path} (changed since the review, or no longer a plain file)")
            elif op == "rmdir-if-empty":
                if c.stat(path)["type"] == "dir" and not c.listdir(path): log(op=op, path=path, state="intent"); c.rmdir(path)
            elif op == "remove-record":
                if a.get("when_gone") and c.stat(a["when_gone"])["type"] != "none": kept.append(f"{path} (its copy keeps items, so its record stays)"); continue
                if a.get("when_gone_module") and not record_gone(c, {"name": a["when_gone_module"], "package_id": a.get("package_id")}): kept.append(f"{path} (what it names is still there)"); continue
                if c.stat(path)["type"] == "none": continue
                log(op=op, path=path, sha256=a["sha256"], state="intent")
                if c.remove(path, a["sha256"]): removed.append(path)
                else: kept.append(f"{path} (changed since the review)")
        except (ConnectionError, OSError, PermissionError, InstallStopped) as e:
            problems.append(f"{op} {path or ''} stopped: {e}"); break
    if not problems:
        try: sync(drop=True)
        except (ConnectionError, OSError, InstallStopped) as e: problems.append(f"the removal could not be made persistent: {e}")
    try:
        mod_after = [n for n in ADDONS.values() if c.method_files(n) is not None and n in {a["name"] for a in plan["actions"] if a["op"] == "addon-uninstall"}] + ([LEGACY_NAME] if c.pack_files(LEGACY_NAME) and any(a["op"] == "legacy-uninstall" for a in plan["actions"]) else []); pa = c.stat(POINTER)["type"]
        ptr_ok = (pa == "none") if plan["pointer"]["owned"] else ((c.sha256(POINTER) if pa == "file" else None) == plan["pointer_sha256"])
        gone = [a["path"] for a in plan["actions"] if a["op"] == "remove" and c.stat(a["path"])["type"] != "none"]
        checks = {"module_removed": not mod_after, "pointer": ptr_ok, "planned_removals_left": len(gone)}
    except (ConnectionError, OSError) as e: checks = {"unreadable": str(e)}; problems.append(f"the result could not be read back: {e}")
    complete = not problems and not kept and checks.get("module_removed") is True and checks.get("pointer") is True and checks.get("planned_removals_left") == 0
    data = "kept" if keep_data else ("removed" if c.stat(DATA_DIR)["type"] == "none" else "partly kept")
    log(op="end", result="uninstalled" if complete else "incomplete", problems=problems[:10], kept=kept[:50])
    return {"ok": complete, "result": "uninstalled" if complete else "incomplete", "removed": removed, "kept_changed": kept, "kept_unknown": sorted(x for x in plan["keep"] if "not this program's" in x or "not followed" in x),
            "problems": problems, "data": data, "module": "removed" if plan["module"] else "absent", "verified": checks, "changed": bool(removed),
            "addons_removed": plan["addons_removed"], "addons_kept": plan["addons_kept"],
            "after_restart": (_after_text(plan["addons_kept"], bool(plan["copies"]) or ADDONS["mod"] in plan["addons_removed"] or plan["pointer"]["owned"]) if checks.get("module_removed") else
                              "Not everything was removed: the console may still offer Classic Cart Studio's HOME. Review the removal again.")}

def _after_text(kept: list[str], home: bool = True) -> str:
    """what the console does after a complete removal, in plain words (home: there was CCS HOME to remove, not only Hakchi games)"""
    if not home: t = ("CCS Nucleus stays as it is and still decides what opens at power-on. " if ADDONS["nucleus"] in kept else "") + "The console's own menu keeps its own games and saves; the games Classic Cart Studio added to it are gone."
    elif ADDONS["nucleus"] in kept: t = "CCS Nucleus stays as it is and still decides what opens at power-on; Classic Cart Studio's HOME no longer starts, and the console's own menu keeps its games and saves."
    else: t = "The console starts with its own menu, games and saves; CCS HOME's tile is gone and Classic Cart Studio's HOME no longer starts."
    if ADDONS["mod"] in kept: t += " CCS Mod stays, but its CCS HOME tile has no HOME to open until one is installed again."
    return t

EXPORT_CHUNK = 262144   # bytes per read (64 blocks of 4,096): one bounded message each, whatever the file's size
def export_data(c, out_dir: str, device: str | None = None, max_file: int = MAX_FILE, max_total: int = MAX_TOTAL) -> dict:
    """the console's Classic Cart Studio data (saves, slots, settings, HOME choices) copied to a new folder on this computer. Each file is read
    in bounded pieces; it counts only when the bytes received hash to what the console reported before and after the read (a file changed
    while it was read is reported, not copied). The result says complete (every file), partial (named files left out, with why), incomplete
    (interrupted) or empty; EXPORT.json lists each copied file's size and SHA-256. Reads only; links are not followed; nothing is removed"""
    if getattr(c, "simulated", False) is not True: gate.require("inspect", "read the console's data")
    try: dev = check_device(c.identity(fresh=True), "export")
    except Refused as e: return {"ok": False, "status": "refused", "why": e.why, "files": 0}
    if device and dev["hwid"] != device: return {"ok": False, "status": "refused", "why": "this is not the console that was checked before; check it again", "files": 0}
    os.makedirs(out_dir, exist_ok=False); files, skipped = {}, []; state = {"total": 0, "interrupted": None}
    def copy_one(r: str):
        p = f"{DATA_DIR}/{r}"; size = c.stat(p).get("size")
        if not isinstance(size, int): skipped.append({"path": r, "why": "its size could not be read"}); return
        if size > max_file: skipped.append({"path": r, "why": f"larger than {max_file / 1e6:.0f} MB, the largest file this export copies"}); return
        if state["total"] + size > max_total: skipped.append({"path": r, "why": f"the export would exceed {max_total / 1e6:.0f} MB"}); return
        h0 = c.sha256(p); q = os.path.join(out_dir, "data", *r.split("/")); os.makedirs(os.path.dirname(q), exist_ok=True); part = q + ".part"; off = 0; why = None
        try:
            with open(part, "xb") as f:
                while True:
                    ch = c.read_chunk(p, off, EXPORT_CHUNK)
                    if ch is None: why = "it is no longer a regular file"; break
                    if ch["file_size"] != size: why = "it changed while it was read"; break
                    f.write(ch["data"]); off += len(ch["data"])
                    if len(ch["data"]) < EXPORT_CHUNK or off >= size: break
            if why is None:
                hl = _sha(_read(part)); h1 = c.sha256(p)
                if off != size or hl != h0 or h1 != h0: why = "it changed while it was read"
            if why is None: os.replace(part, q); files[r] = {"size": size, "sha256": h0}; state["total"] += size
            else: skipped.append({"path": r, "why": why})
        finally:
            if os.path.exists(part): os.remove(part)
    def walk(rel: str):
        base = f"{DATA_DIR}/{rel}" if rel else DATA_DIR
        for n in c.listdir(base):
            r = f"{rel}/{n}" if rel else n; t = c.stat(f"{DATA_DIR}/{r}")["type"]
            if t == "dir": walk(r); continue
            if t != "file": skipped.append({"path": r, "why": f"a {t}, not followed"}); continue
            try: check_rel(r)
            except PlanError: skipped.append({"path": r, "why": "a name this export does not copy"}); continue
            copy_one(r)
    os.makedirs(os.path.join(out_dir, "data"))
    try:
        if c.stat(DATA_DIR)["type"] == "dir": walk("")
        ident = c.read(f"{DATA_DIR}/IDENTITY.json", 65536)
    except (ConnectionError, OSError) as e: state["interrupted"] = str(e); ident = None
    status = "incomplete" if state["interrupted"] else ("partial" if skipped else ("complete" if files else "empty"))
    try: ident_j = json.loads(ident) if ident else None
    except ValueError: ident_j = None
    man = {"schema": "ccs-console-data-export/2", "created": _now(), "status": status, "interrupted": state["interrupted"],
           "console": {"system": dev["system"], "kernel": dev["kernel"], "hakchi": dev["hakchi"], "console_id": _sha(dev["hwid"].encode())[:16], "simulated": dev["simulated"]},
           "identity": ident_j, "files": files, "skipped": skipped, "bytes": sum(f["size"] for f in files.values())}
    with open(os.path.join(out_dir, "EXPORT.json"), "w", encoding="utf-8") as f: json.dump(man, f, indent=1)
    v = verify_export(out_dir)
    return {"ok": v["ok"] and status in ("complete", "empty"), "status": status, "files": len(files), "bytes": man["bytes"], "skipped": skipped, "interrupted": state["interrupted"], "verified": v}

def verify_export(out_dir: str) -> dict:
    """every listed file present with its size and SHA-256 (the folder holds no other data file); the status as the export recorded it"""
    with open(os.path.join(out_dir, "EXPORT.json"), encoding="utf-8") as f: man = json.load(f)
    bad = []
    for rel, info in man["files"].items():
        q = os.path.join(out_dir, "data", *rel.split("/"))
        if not os.path.isfile(q) or os.path.getsize(q) != info["size"] or _sha(_read(q)) != info["sha256"]: bad.append(rel)
    present = {os.path.relpath(os.path.join(d, n), os.path.join(out_dir, "data")).replace(os.sep, "/") for d, _, ns in os.walk(os.path.join(out_dir, "data")) for n in ns}
    extra = sorted(present - set(man["files"]))
    return {"ok": not bad and not extra, "problems": bad, "extra": extra, "files": len(man["files"]), "status": man.get("status")}

# ---------------------------------------------------------------- stand-in profiles (the worker's home-preflight / home-simulate)
IDENTITY_KEYS = ("kernel", "hakchi", "system", "hwid", "can_interact", "custom_firmware", "board", "region", "data_mount")   # what a session's identity() answers
_READY = {"kernel": HAKCHI_KERNEL, "hakchi": "v1.0.4-126", "system": "nes-usa", "can_interact": True, "custom_firmware": False, "board": "dp-nes", "region": "EUR_USA", "data_mount": DATA_MOUNT}
_SNES = _READY | {"system": "snes-usa", "board": "dp-shvc", "region": "USA", "stock": "snes", "storage": "snes-usa"}
HOME_PROFILES = {   # what the console would be, never a real one
    "hakchi-ready": _READY | {"hwid": "sim-0001", "free_bytes": 200_000_000},
    "stock": {"kernel": "3.4.113", "hakchi": None, "system": "nes-usa", "can_interact": None, "custom_firmware": None, "board": "dp-nes", "region": "EUR_USA", "data_mount": DATA_MOUNT, "hwid": "sim-0002", "free_bytes": 200_000_000},
    "nearly-full": _READY | {"hwid": "sim-0003", "free_bytes": 5_000_000},
    # add-ons already there (another tool added them, such as the CCS Console Workbench), with CCS Nucleus's start set by its own command
    "with-mod": _READY | {"hwid": "sim-0008", "free_bytes": 200_000_000, "addons": "mod"},
    "with-nucleus": _READY | {"hwid": "sim-0004", "free_bytes": 200_000_000, "addons": "nucleus"},   # Original Menu, started directly
    "with-mod-nucleus": _READY | {"hwid": "sim-0009", "free_bytes": 200_000_000, "addons": "mod+nucleus", "start": "ccs-home never on default 10"},   # CCS HOME, started directly
    "with-chooser": _READY | {"hwid": "sim-0010", "free_bytes": 200_000_000, "addons": "nucleus+nucleus-chooser", "start": "native always off default 10"},
    "with-all": _READY | {"hwid": "sim-0011", "free_bytes": 200_000_000, "addons": "mod+nucleus+nucleus-chooser", "start": "ccs-home always on last 10"},
    "snes-ready": _SNES | {"hwid": "sim-0021", "free_bytes": 200_000_000},   # a North American Super NES Classic with hakchi
}
STORAGE_PROFILES = {   # hakchi's game storage (tests; STORAGE_SAMPLE): every other profile has Hakchi2 CE's separate storage per system
    "flat-storage": _READY | {"hwid": "sim-0012", "free_bytes": 200_000_000, "storage": "flat"},
    "no-storage": _READY | {"hwid": "sim-0013", "free_bytes": 200_000_000, "storage": "none"},
}
MODEL_PROFILES = {   # a Super NES Classic Edition with hakchi (tests): North American and European, and the same with CCS Mod already there
    "snes-usa": _SNES | {"hwid": "sim-0014", "free_bytes": 200_000_000},
    "snes-eur": _SNES | {"system": "snes-eur", "region": "EUR", "storage": "snes-eur", "hwid": "sim-0005", "free_bytes": 200_000_000},
    "snes-with-mod": _SNES | {"hwid": "sim-0015", "free_bytes": 200_000_000, "addons": "mod"},
}
IDENTITY_PROFILES = {   # refusals by identity (tests): another model or region, a model its software and hakchi disagree on, data storage
                        # elsewhere, an older hakchi, a custom firmware, an identity that cannot be read
    "sfc-jpn": _SNES | {"system": "shvc-jpn", "region": "JPN", "hwid": "sim-0016", "free_bytes": 200_000_000},
    "nes-eur": _READY | {"system": "nes-eur", "region": "EUR", "hwid": "sim-0017", "free_bytes": 200_000_000},
    "model-mismatch": _READY | {"system": "snes-usa", "hwid": "sim-0018", "free_bytes": 200_000_000},
    "no-model": _READY | {"board": None, "region": None, "hwid": "sim-0019", "free_bytes": 200_000_000},
    "data-elsewhere": _READY | {"data_mount": "/dev/sda1 vfat", "hwid": "sim-0020", "free_bytes": 200_000_000},
    "old-hakchi": _READY | {"hakchi": "v1.0.0-000", "can_interact": False, "hwid": "sim-0006", "free_bytes": 200_000_000},
    "custom-firmware": _READY | {"custom_firmware": True, "hwid": "sim-0007", "free_bytes": 200_000_000},
    "no-identity": _READY | {"hwid": None, "free_bytes": 200_000_000},
}
STOCK_SAMPLE = {"usr/share/games/nes/kachikachi/CLV-P-NAAAE/CLV-P-NAAAE.nes": b"stock game", "var/saves/CLV-P-NAAAE/save.sav": b"a save",
                "etc/init.d/S81clover-mcp": b"#!/bin/sh\nclover-mcp\n", "var/lib/hakchi/hmod/uninstall-clovercon": b'rm -f "$rootfs/lib/x"\n',
                "var/lib/hakchi/rootfs/lib/x": b"clovercon module file", "var/lib/ccs/pkg/someone-elses/keep.txt": b"unknown",
                # hakchi's read-only view of the console's system with a stand-in entry for a built-in game (written for these tests)
                "var/squashfs/usr/share/games/nes/kachikachi/CLV-P-NAAAE/CLV-P-NAAAE.desktop": (b"[Desktop Entry]\nType=Application\nExec=/usr/bin/clover-kachikachi "
                    b"/usr/share/games/nes/kachikachi/CLV-P-NAAAE/CLV-P-NAAAE.nes --volume 75\nPath=/var/lib/clover/profiles/0/CLV-P-NAAAE\nName=A built-in game\n"
                    b"Icon=/usr/share/games/nes/kachikachi/CLV-P-NAAAE/CLV-P-NAAAE.png\n\n[X-CLOVER Game]\nCode=CLV-P-NAAAE\n")}
# a Super NES Classic's built-in game, its save and suspend point, its start script and hakchi's read-only view of its system, in the
# stock layout (the games directly under /usr/share/games, Nintendo's Super NES launcher; saves under /var/lib/clover/profiles): written
# for these tests, never a real game
SNES_STOCK_SAMPLE = {"usr/share/games/CLV-P-SAAAE/CLV-P-SAAAE.sfrom": b"stock game", "var/lib/clover/profiles/0/CLV-P-SAAAE/cartridge.sram": b"a save",
                     "var/lib/clover/profiles/0/CLV-P-SAAAE/suspendpoint1/state.sstate": b"a suspend point",
                     "etc/init.d/S81clover-mcp": b"#!/bin/sh\nclover-mcp\n", "var/lib/hakchi/hmod/uninstall-clovercon": b'rm -f "$rootfs/lib/x"\n',
                     "var/lib/hakchi/rootfs/lib/x": b"clovercon module file", "var/lib/ccs/pkg/someone-elses/keep.txt": b"unknown",
                     "var/squashfs/usr/share/games/CLV-P-SAAAE/CLV-P-SAAAE.desktop": (b"[Desktop Entry]\nType=Application\nExec=/usr/bin/clover-canoe-shvc-wr "
                         b"-rom /usr/share/games/CLV-P-SAAAE/CLV-P-SAAAE.sfrom --volume 100\nPath=/var/lib/clover/profiles/0/CLV-P-SAAAE\nName=A built-in game\n"
                         b"Icon=/usr/share/games/CLV-P-SAAAE/CLV-P-SAAAE.png\n\n[X-CLOVER Game]\nCode=CLV-P-SAAAE\n")}
STOCK_SAMPLES = {"nes": STOCK_SAMPLE, "snes": SNES_STOCK_SAMPLE}
# hakchi's game storage as the profile has it (Hakchi2 CE syncs to a folder per system, its first page "000", a folder's games in "001"...;
# "flat": the storage itself holds the games; "none": no game storage, the console's menu lists its own game folder): another tool's games
STORAGE_SAMPLE = {
    "nes-usa": {"var/lib/hakchi/games/nes-usa/000/CLV-H-AAAAA/CLV-H-AAAAA.desktop": b"[Desktop Entry]\nName=A game Hakchi added\n",
                "var/lib/hakchi/games/nes-usa/000/CLV-H-AAAAA/CLV-H-AAAAA.nes": b"NES\x1a a game Hakchi added",
                "var/lib/hakchi/games/nes-usa/000/CLV-S-00001/CLV-S-00001.desktop": b"[Desktop Entry]\nExec=/bin/chmenu 001 /var/games\nName=A Hakchi folder\n",
                "var/lib/hakchi/games/nes-usa/001/CLV-H-AAAAB/CLV-H-AAAAB.desktop": b"[Desktop Entry]\nName=A game in a Hakchi folder\n"},
    "snes-usa": {"var/lib/hakchi/games/snes-usa/000/CLV-H-SAAAA/CLV-H-SAAAA.desktop": b"[Desktop Entry]\nName=A game Hakchi added\n",
                 "var/lib/hakchi/games/snes-usa/000/CLV-H-SAAAA/CLV-H-SAAAA.sfrom": b"a game Hakchi added"},
    "snes-eur": {"var/lib/hakchi/games/snes-eur/000/CLV-H-SAAAB/CLV-H-SAAAB.desktop": b"[Desktop Entry]\nName=A game Hakchi added\n"},
    "flat": {"var/lib/hakchi/games/CLV-H-AAAAA/CLV-H-AAAAA.desktop": b"[Desktop Entry]\nName=A game Hakchi added\n"},
    "none": {}}

def make_stand_in(root: str, profile: str, durable: bool = False) -> FakeConsole:
    """a fresh disposable stand-in (a stock game and save, the stock start script, another hakchi module, an unknown folder), with the
    profile's add-ons installed by hakchi's pack_install and CCS Nucleus's start set by its own command (no record of this program's: they
    are another tool's)"""
    prof = HOME_PROFILES.get(profile) or MODEL_PROFILES.get(profile) or IDENTITY_PROFILES.get(profile) or STORAGE_PROFILES.get(profile)
    if prof is None: raise KeyError(f"unknown profile {profile!r}")
    os.makedirs(root, exist_ok=True); info = dict(prof)
    with open(os.path.join(root, "console.json"), "w", encoding="utf-8") as f: json.dump(info, f)
    sample = dict(STOCK_SAMPLES[info.get("stock", "nes")]) | STORAGE_SAMPLE[info.get("storage", "nes-usa")]
    if info.get("foreign_s81"): sample["var/lib/hakchi/rootfs/etc/init.d/S81clover-mcp"] = b"#!/bin/sh\n# another module's start script\n"
    for rel, b in sample.items():
        q = os.path.join(root, *rel.split("/")); os.makedirs(os.path.dirname(q), exist_ok=True)
        with open(q, "wb") as f: f.write(b)
    os.makedirs(os.path.join(root, "var/lib/hakchi/rootfs/etc/init.d"), exist_ok=True)   # hakchi's own overlay carries its start scripts' folder
    for a in (info.get("addons") or "").split("+"):
        if a: ap = addon_package(a); FakeConsole(root).pack_install(ap["name"], ap["hmod"])
    if info.get("start"): FakeConsole(root).nucleus_start(*info["start"].split(" "))
    return FakeConsole(root, durable=durable)

def storage_digest(root: str) -> dict:   # the stand-in's hakchi game storage sample (another tool's games and folders) as it is now
    out = {}
    for rel in [r for x in STORAGE_SAMPLE.values() for r in x]:
        q = os.path.join(root, *rel.split("/")); out[rel] = _sha(_read(q)) if os.path.isfile(q) else None
    return out

def stock_digest(root: str) -> dict:   # the stand-in's stock and foreign sample (its own model's) as it is now (compared before and after)
    out = {}
    try: stock = json.loads(_read(os.path.join(root, "console.json"))).get("stock") or "nes"
    except (OSError, ValueError): stock = "nes"
    for rel in STOCK_SAMPLES.get(stock, STOCK_SAMPLE):
        q = os.path.join(root, *rel.split("/")); out[rel] = _sha(_read(q)) if os.path.isfile(q) else None
    return out
