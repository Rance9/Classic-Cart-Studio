"""Installing Classic Cart Studio's HOME on a console that runs hakchi (K3). Implemented and tested here against disposable stand-ins; a real
console is closed by Gate G1 in code (open_console refuses every real kind; the installer refuses a console that is not a simulation unless the
install tier is open, and even then never rolls back by itself: docs/console-install-architecture.md sections 13 and 14).

Where (all under /var/lib/ccs; nothing else on the console is written except through hakchi's own module commands):
  pkg/<version>.<attempt>/   one immutable copy of a console package per installation attempt (version = the first 16 hex of the SHA-256 of
                             its MANIFEST.json, attempt = 8 hex): a repair or an update never changes the copy in use
  pkg/current                a small file naming the copy in use ("<version>.<attempt>"), replaced in one rename. A file, not a link: the
                             console's BusyBox 1.24.1 has no "mv -T", and "mv" onto a link to a folder moves into that folder
  install/                   this program's records on the console, each written before what it records: v-<copy>.json (the files of a
                             copy and their SHA-256), module-<sha16>.json (a method package this program installed, by its SHA-256; or an
                             earlier version's start-up module), attempt-<attempt>.json (an attempt in progress, with what it found first)
  owned/, policy, mod/, nucleus/   the method package's own records and settings (its scripts keep them; this program only reads them)
  data/                      the console's own saves, Game Settings and HOME choices (kept across updates; kept by uninstall unless removal
                             is asked for), with IDENTITY.json naming the player build that made them
Start-up: the shared install policy of the CCS Console Workbench (tools/console-nucleus/DESIGN.md): CCS Mod for Hakchi ("ccs-mod": CCS HOME
as an entry in the console's own menu) or CCS Nucleus ("ccs-nucleus": a start chooser), the package's layout names which. Its hakchi package
is the same bytes the Workbench's preparation image carries (tools/console-nucleus/build_packages.py; pinned there and by the transport
worker), installed by hakchi's own pack_install: its install script checks that every file it would copy is free or its own, hakchi copies
them into its overlay (/var/lib/hakchi/rootfs) and keeps its uninstall list (/var/lib/hakchi/hmod/uninstall-<name>), and the script records
its files in its own journal (/var/lib/ccs/owned) and adds the menu entry. hakchi's pack_uninstall runs the package's own removal (only its
unchanged files). Both methods start the copy pkg/current names. Earlier versions of this program installed a module "ccs-home" whose one
file, an overlay /etc/init.d/S81clover-mcp, hakchi's own script/removed deletes at every start (p7070_firmware -> mod_repair_etc): that
module is retired (hakchi's pack_uninstall of its list) once the new method is verified, and only when it is this program's by its records.

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
from . import gate

PLAN_SCHEMA = "ccs-home-install-plan/4"
ROOT_DIR = "/var/lib/ccs"; PKG_DIR = ROOT_DIR + "/pkg"; DATA_DIR = ROOT_DIR + "/data"; INST_DIR = ROOT_DIR + "/install"; POINTER = PKG_DIR + "/current"
BIN_DIR = ROOT_DIR + "/bin"   # p3a1's launcher place: never written now (a file there is someone else's and is kept)
METHODS = {"mod": "ccs-mod", "nucleus": "ccs-nucleus"}   # the package layout names the method (native_home/console_package.py LAYOUTS)
METHOD_LABELS = {"mod": "CCS Mod", "nucleus": "CCS Nucleus"}
# the app's install policy: the method it adds to a console that has neither. It installs a collection into the method a console already
# uses (CCS Mod or CCS Nucleus, read at the check) and never adds CCS Nucleus or changes a console from one method to the other (that is the
# CCS Console Workbench's Prepare Console and Console Restore)
DEFAULT_METHOD = "mod"
# CCS Mod's optional start at power-on (tools/console-nucleus/DESIGN.md section 3): its own command "ccs-mod autostart on|off" writes or
# removes its start script (from the package's template, only where nothing else is) and its setting; this program reads both and asks
# for that command (the worker's mod-start), never writes them itself
MOD_START = "/etc/init.d/S81ccs-mod"; MOD_START_TEMPLATE = "/etc/ccs/mod/S81ccs-mod"; MOD_CONFIG = ROOT_DIR + "/mod/config"   # (overlay, overlay, console)
METHOD_RECORDS = ("owned", "policy", "mod", "nucleus")    # what the method package's own scripts keep in /var/lib/ccs (read, never written here)
# the start-up module of earlier versions: one overlay file that hakchi itself deletes at every start (script/removed); retired, never installed
LEGACY_NAME = "ccs-home"; LEGACY_FILE = "/etc/init.d/S81clover-mcp"
OVERLAY = "/var/lib/hakchi/rootfs"; HMOD_DIR = "/var/lib/hakchi/hmod"
HAKCHI_KERNEL = "3.4.113.29-madmonkey"
# the one console this program installs on (approved scope): a North American NES Classic running the pinned hakchi (Hakchi2 CE 3.9.3's
# bundled hakchi.hmod v1.0.4-126 with its kernel) on its own firmware. Anything else, or anything unreadable, is refused before any change
SUPPORTED = {"system": "nes-usa", "kernel": HAKCHI_KERNEL, "hakchi": "v1.0.4-126"}
HWID_RE = re.compile(r"^[0-9A-Za-z-]{4,64}$")
PROTECTED =("/usr", "/var/saves", "/etc", "/var/lib/hakchi", "/bin", "/sbin", "/lib", "/boot", "/dev", "/proc", "/sys", "/newroot")
WRITE_ROOTS = (ROOT_DIR + "/", "/tmp/ccs/")
RESERVE = 8 * 1000 * 1000   # room kept for saves, Game Settings and the HOME's choices, beyond the package itself (decimal MB)
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
class Refused(RuntimeError):
    """a precondition does not hold: nothing was changed"""
    def __init__(self, step, why):
        self.step, self.why = step, why; super().__init__(f"{step}: {why}")
class InstallStopped(RuntimeError):
    def __init__(self, step, why):
        self.step, self.why = step, why; super().__init__(f"{step}: {why}")
class Cancelled(RuntimeError): pass
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

_METHOD_CACHE: dict = {}
def method_package(method: str) -> dict:
    """the method's hakchi package, byte for byte the one the CCS Console Workbench's preparation image carries (tools/console-nucleus/
    build_packages.py builds it reproducibly; build_images.METHOD_SHAS and the transport worker pin it): {"method", "name", "hmod" (bytes),
    "sha256", "package_id" (its VERSION's package), "files" ({overlay path: SHA-256}: what hakchi copies into its overlay)}"""
    if method not in METHODS: raise PlanError(f"unknown install method {method!r}")
    if method not in _METHOD_CACHE:
        import importlib.util
        d = _nucleus_dir(); spec = importlib.util.spec_from_file_location("ccs_method_packages", os.path.join(d, "build_packages.py")); BP = importlib.util.module_from_spec(spec); spec.loader.exec_module(BP)
        mem = BP.members(method); hb = BP.hmod_bytes(mem)
        files = {}
        for line in mem[f"etc/ccs/{method}/FILES"][0].decode().splitlines():
            s, _mode, rel = line.split(" ", 2); files["/" + rel] = s
        files[f"/etc/ccs/{method}/FILES"] = _sha(mem[f"etc/ccs/{method}/FILES"][0])
        pid = dict(l.split("=", 1) for l in mem[f"etc/ccs/{method}/VERSION"][0].decode().splitlines())["package"]
        _METHOD_CACHE[method] = {"method": method, "name": METHODS[method], "hmod": hb, "sha256": _sha(hb), "package_id": pid, "files": files,
                                 "overlay_bytes": sum(len(b) for n, (b, _m) in mem.items() if n not in BP.NOT_COPIED)}
    return _METHOD_CACHE[method]

def staged_packages(root: str | None) -> dict:
    """the console packages the app staged, {layout: folder}: one per install method (root/mod and root/nucleus, the same package in each
    method's layout, nes_companion.console_package_stage). A single package folder (root/MANIFEST.json: a test's, or a staging made before
    there were two) is the one for its own layout"""
    if not root or not os.path.isdir(root): return {}
    def layout(d):
        try:
            with open(os.path.join(d, "MANIFEST.json"), "rb") as f: return json.loads(f.read()).get("layout")
        except (OSError, ValueError): return None
    if os.path.isfile(os.path.join(root, "MANIFEST.json")): return {layout(root): root} if layout(root) else {}
    return {m: os.path.join(root, m) for m in METHODS if layout(os.path.join(root, m)) == m}

def same_collection(a: dict, b: dict) -> bool:
    """two plans of one staged collection in two methods' layouts: every file the same but session.json (whose leave item differs)"""
    fa = {f["path"]: f["sha256"] for f in a["files"] if f["path"] not in ("session.json", "MANIFEST.json")}
    fb = {f["path"]: f["sha256"] for f in b["files"] if f["path"] not in ("session.json", "MANIFEST.json")}
    return fa == fb and a.get("games") == b.get("games") and a.get("startup") == b.get("startup")

def console_method(c) -> str:
    """the install method the console uses now, read from it: "mod", "nucleus" or "none" (hakchi lists neither package and neither program
    is in its overlay). Both listed, a program hakchi does not list, a package this program does not recognize, a changed one, or a state that
    cannot be read raise Refused: then nothing is installed rather than a method guessed (nothing is changed)"""
    found = []
    for m, n in METHODS.items():
        try:
            mf = c.method_files(n); prog = c.overlay_sha256(f"/bin/{n}") if mf is None else None
        except (ConnectionError, OSError, ValueError) as e: raise Refused("preflight", f"which method this console uses could not be read ({e}); nothing was changed") from None
        if mf is None:
            if prog is not None: raise Refused("preflight", f"{n}'s program is on the console but hakchi does not list its package; it is not this program's to change. Nothing was changed")
            continue
        mp = method_package(m)
        if mf.get("package") == mp["package_id"]:
            bad = [p for p, s in mp["files"].items() if (mf.get("files") or {}).get(p) != s]
            if bad: raise Refused("preflight", f"files of {n} on the console were changed ({', '.join(bad[:3])}); they are kept, and nothing was changed")
        elif not re.match(r"^[0-9a-f]{64}$", str(mf.get("package") or "")):
            raise Refused("preflight", f"a {n} package this program does not recognize (no package record of its own) is on the console; it is kept, and nothing was changed")
        found.append(m)
    if len(found) > 1: raise Refused("preflight", "both CCS Mod and CCS Nucleus are installed on this console, so which one starts CCS HOME is not clear (a change between them may not have finished). Start the console normally once, or finish the change in the CCS Console Workbench (Console Restore), then check again. Nothing was changed")
    return found[0] if found else "none"

def _sealed(b: bytes | None, schema: str) -> dict | None:
    """a sealed record of CCS's console library (lib.sh ccs_sealed): its first line schema=..., its last end=<SHA-256 of every line before>"""
    if b is None: return None
    t = b.decode("utf-8", "replace"); lines = t.split("\n")
    if not t.endswith("\n") or len(lines) < 3 or lines[0] != f"schema={schema}" or not lines[-2].startswith("end="): return None
    body = "\n".join(lines[:-2]) + "\n"
    if _sha(body.encode()) != lines[-2][4:]: return None
    return dict(l.split("=", 1) for l in lines[1:-2] if "=" in l)

def mod_start_state(c) -> dict:
    """CCS Mod's optional start at power-on as the console holds it (reads only): {"state", "script", "setting"}. script: "absent", "ours"
    (exactly the package's own template, which its command writes) or "changed" (another file under that name: never CCS's to change);
    setting: "on", "off" or "none" (its sealed setting, /var/lib/ccs/mod/config). state: "on" when CCS HOME opens at power-on (the start script
    and the setting both on), "changed" when the start script is not CCS Mod's own, else "off" (CCS Mod's start script runs CCS HOME only
    while its setting says on)"""
    tpl = method_package("mod")["files"].get(MOD_START_TEMPLATE)
    s = c.overlay_sha256(MOD_START); script = "absent" if s is None else ("ours" if s == tpl else "changed")
    cfg = _sealed(c.read(MOD_CONFIG, 4096) if c.stat(MOD_CONFIG)["type"] == "file" else None, "ccs-mod-config/1")
    setting = "none" if cfg is None else ("on" if cfg.get("autostart") == "on" else "off")
    state = "changed" if script == "changed" else ("on" if script == "ours" and setting == "on" else "off")
    return {"state": state, "script": script, "setting": setting}

def method_state(c, pkg: dict, recs: dict | None = None) -> str:
    """what the console holds of the plan's method: "absent", "current" (this exact package, every file unchanged), "older" (another version
    of the same method's CCS package, by its own VERSION record: its own install script updates it, and refuses any file that is not its own
    unchanged file); anything else raises Refused (nothing is changed)"""
    other = next((m for m, n in METHODS.items() if n != pkg["name"]), None)
    o = c.method_files(METHODS[other]) if other else None
    if o is not None: raise Refused("preflight", f"this console uses {METHOD_LABELS[other]}; this plan installs the HOME for {METHOD_LABELS[pkg['method']]}. Classic Cart Studio never changes a console's method to install games: check the plan again and it is made for {METHOD_LABELS[other]}. Nothing was changed")
    m = c.method_files(pkg["name"])
    if m is None:
        if c.overlay_sha256(f"/bin/{pkg['name']}") is not None: raise Refused("preflight", f"{pkg['name']}'s program is on the console but hakchi does not list its package; it is not this program's to change. Nothing was changed")
        return "absent"
    if m.get("package") == pkg["package_id"]:
        bad = [p for p, s in pkg["files"].items() if (m.get("files") or {}).get(p) != s]
        if bad: raise Refused("preflight", f"files of {pkg['name']} on the console were changed ({', '.join(bad[:3])}); they are kept, and nothing was changed")
        return "current"
    if re.match(r"^[0-9a-f]{64}$", str(m.get("package") or "")): return "older"
    raise Refused("preflight", f"a {pkg['name']} package this program does not recognize (no package record of its own) is on the console; it is kept, and nothing was changed")
def pointer_bytes(copy: str) -> bytes: return (copy + "\n").encode()

def check_device(ident: dict | None, step: str = "device") -> dict:
    """the console this session reads now, against the one supported console (SUPPORTED): every value present and exact, hakchi's own
    CanInteract rule true and its stock firmware in use; a missing, unreadable or different value refuses (fail closed, nothing changed)"""
    i = ident or {}
    hwid = str(i.get("hwid") or "")
    if not HWID_RE.match(hwid): raise Refused(step, "the console's identity could not be read; nothing was changed")
    if i.get("kernel") != SUPPORTED["kernel"]: raise Refused(step, f"the console runs kernel {i.get('kernel')!r}: hakchi must be installed first (its own, separate step under G1)")
    if i.get("system") != SUPPORTED["system"]: raise Refused(step, f"this console reports {i.get('system')!r}; Classic Cart Studio installs only on a North American NES Classic ({SUPPORTED['system']}). Nothing was changed")
    if i.get("hakchi") != SUPPORTED["hakchi"]: raise Refused(step, f"this console runs hakchi {i.get('hakchi')!r}; only the pinned hakchi {SUPPORTED['hakchi']} (Hakchi2 CE 3.9.3) is supported. Nothing was changed")
    if i.get("can_interact") is not True: raise Refused(step, "hakchi reports that this console cannot be used as it is (its boot, kernel or scripts are too old); nothing was changed")
    if i.get("custom_firmware") is not False: raise Refused(step, "this console runs a custom firmware (or it could not be read); only its own firmware is supported. Nothing was changed")
    return {"hwid": hwid, "system": i["system"], "kernel": i["kernel"], "hakchi": i["hakchi"], "simulated": i.get("simulated") is True}

def same_console(c, hwid: str, step: str = "device"):
    """the console answering now is still the bound one (read again, not from a cached answer): otherwise stop before the next change"""
    i = c.identity(fresh=True)
    if str(i.get("hwid") or "") != hwid: raise InstallStopped(step, "another console answers now (or its identity cannot be read): stopped before the next change")

def check_rel(rel: str) -> str:
    """a package path: relative, plain names (letters, digits, . _ -), no empty, "." or ".." part, at most 8 deep"""
    if not isinstance(rel, str) or not REL_RE.match(rel) or any(p in (".", "..") or p.startswith("..") for p in rel.split("/")): raise PlanError(f"package path not allowed: {rel!r}")
    return rel

def build_plan(package_dir: str, mod_start: str | None = None) -> dict:
    """the immutable plan for one console package (install layout): its files with their SHA-256 and modes (validated), the start-up module,
    the room it needs, the identity its saves will carry; named by the SHA-256 of its own content. mod_start (CCS Mod only): "on" or "off"
    (the default), whether CCS HOME opens when the console is turned on (CCS Mod's own optional start); CCS Nucleus's chooser decides what
    starts there, so a CCS Nucleus plan has none"""
    man_b = _read(os.path.join(package_dir, "MANIFEST.json")); man = json.loads(man_b)
    if man.get("schema") != "ccs-console-package/1": raise PlanError("not a Classic Cart Studio console package")
    import platform_ids   # SNES-GROUNDWORK.md: a package for a game system, core or method this version does not support is never planned
    tgt = platform_ids.resolve_target(man, "console-package", man.get("games") if isinstance(man.get("games"), list) else None)
    if not tgt["ok"]: raise PlanError(tgt["text"])
    if man.get("layout") == "install": raise PlanError("this package was made for the earlier start-up module, which hakchi removes at every start: prepare the HOME again (CCS Mod or CCS Nucleus)")
    if man.get("layout") not in METHODS: raise PlanError(f"a {man.get('layout')!r} package is not for installing (the RAM test's package never is)")
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
    version = _sha(man_b)[:16]; method = man["layout"]; mp = method_package(method)
    if method == "mod": mod_start = mod_start or "off"
    if mod_start not in (("on", "off") if method == "mod" else (None,)): raise PlanError("CCS Mod's start at power-on is on or off; it is not offered with CCS Nucleus, whose start chooser decides what starts")
    plan = {"schema": PLAN_SCHEMA, "version": version, "label": man.get("label"), "games": man.get("games"), "playable": man.get("playable"), "startup": man.get("startup"),
            "player": man.get("player"), "manifest_sha256": _sha(man_b), "state_identity": dict(STATE_IDENTITY), "method_label": METHOD_LABELS[method], "mod_start": mod_start,
            "paths": {"root": ROOT_DIR, "package": f"{PKG_DIR}/{version}.<attempt>", "pointer": POINTER, "data": DATA_DIR, "records": INST_DIR},
            "files": files, "bytes": total, "needs_free_bytes": total + RESERVE + mp["overlay_bytes"],
            "method": method, "module": {"name": mp["name"], "sha256": mp["sha256"], "package_id": mp["package_id"], "files": mp["files"],
                       "where": f"hakchi's pack_install of {mp['name']}.hmod: its install script copies its files into {OVERLAY}, records them in {ROOT_DIR}/owned and adds its menu entry; hakchi keeps its uninstall list {HMOD_DIR}/uninstall-{mp['name']}"},
            "requires": {"kernel": HAKCHI_KERNEL, "note": "hakchi's kernel must already be installed (its first installation is a separate step: the CCS Console Workbench's Prepare Console)"},
            "never_written": ["/usr (the stock games and system)", "/var/saves (the stock saves)",
                              f"/etc and /var/lib/hakchi other than through hakchi's own pack_install / pack_uninstall of {mp['name']} (its own files, menu entry and records) "
                              f"and the retirement of this program's earlier start-up module {LEGACY_NAME} (hakchi's pack_uninstall of its own list)",
                              "/bin, /sbin, /lib, /boot, /dev, /proc, /sys, /newroot",
                              f"anything under {ROOT_DIR} that is not this program's by its records (kept as it is and reported)"],
            "steps": ["preflight", "record", "stage", "readback", "switch", "method"] + (["start"] if method == "mod" else []) + ["verify", "retire", "tidy", "finish"]}
    plan["hash"] = _sha(_canon(plan)); return plan

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
    in one rename; hakchi's pack_install / pack_uninstall mirrored (its overlay and uninstall list).
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
        return {k: self.info.get(k) for k in ("kernel", "hakchi", "system", "hwid", "can_interact", "custom_firmware")} | {"simulated": True}
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
        every folder, deepest first). hmod: the .hmod's bytes, or (tests of the earlier start-up module only) {path: (bytes, mode)}. The
        package's own install script is not run here: what CCS's does on a console (its checks, journal and menu entry) is shown by
        tools/console-nucleus/test_nucleus.py under qemu-arm with hakchi's own code"""
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
            for path, (b, mode) in files.items():
                q = self._ov(path); os.makedirs(os.path.dirname(q), exist_ok=True)
                with open(q, "wb") as f: f.write(b)
                lst.append(f'rm -f "$rootfs/.{path}"'); parts = path.strip("/").split("/")[:-1]
                dirs |= {"/" + "/".join(parts[:i]) for i in range(1, len(parts) + 1)}
            lst += [f'rmdir --ignore-fail-on-non-empty "$rootfs/.{d}"' for d in sorted(dirs, key=lambda d: -d.count("/"))]
            u = self._ulist(name); os.makedirs(os.path.dirname(u), exist_ok=True)
            with open(u, "w", newline="\n") as f: f.write(script.decode() + "\n# auto-generated\n" + "\n".join(lst) + "\n")
            if name == METHODS["mod"] and not os.path.isfile(self._ulist(METHODS["nucleus"])):   # its postinstall applies the start setting it keeps (in charge)
                cfg = _sealed(_read(self.p(MOD_CONFIG)) if os.path.isfile(self.p(MOD_CONFIG)) else None, "ccs-mod-config/1"); tpl = self.overlay_read(MOD_START_TEMPLATE); q = self._ov(MOD_START)
                if cfg and cfg.get("autostart") == "on" and tpl is not None and not os.path.exists(q):
                    os.makedirs(os.path.dirname(q), exist_ok=True)
                    with open(q, "wb") as f: f.write(tpl)
                elif not (cfg and cfg.get("autostart") == "on") and tpl is not None and os.path.isfile(q) and _read(q) == tpl: os.remove(q)
            self.writes.append(f"hakchi:pack_install {name}")
        return self._change(f"hakchi pack_install {name}", act)
    def method_files(self, name: str) -> dict | None:
        """a CCS method package as the console holds it (the worker's module-files): None when hakchi does not list it; else its VERSION's
        package id and every file its own FILES list names, with the SHA-256 each has now (None: missing)"""
        if not os.path.isfile(self._ulist(name)): return None
        m = name[4:]; ver = self.overlay_read(f"/etc/ccs/{m}/VERSION"); fl = self.overlay_read(f"/etc/ccs/{m}/FILES")
        pkg = dict(l.split("=", 1) for l in ver.decode().splitlines() if "=" in l).get("package") if ver else None
        files = {"/" + l.split(" ", 2)[2]: self.overlay_sha256("/" + l.split(" ", 2)[2]) for l in (fl or b"").decode().splitlines() if l.count(" ") >= 2}
        if fl is not None: files[f"/etc/ccs/{m}/FILES"] = _sha(fl)
        return {"package": pkg, "files": files}
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
    def mod_start(self, state: str) -> str:
        """CCS Mod's own command "ccs-mod autostart on|off" (the worker's mod-start runs it on a console): on writes its start script
        S81ccs-mod from the package's template (only where nothing else is, and only while CCS Mod is the method installed) and its setting
        on; off sets its setting off and removes the start script while it is still the one CCS Mod wrote. Its one result line"""
        if state not in ("on", "off"): raise ValueError("state: on or off")
        if self.method_files(METHODS["mod"]) is None or self.method_files(METHODS["nucleus"]) is not None: return "result autostart refused CCS Mod is not in charge"
        tpl = self.overlay_read(MOD_START_TEMPLATE); q = self._ov(MOD_START)
        if state == "on" and (tpl is None or (os.path.exists(q) and _read(q) != tpl)): return "result autostart refused belongs-to-someone-else " + OVERLAY + MOD_START
        def act():
            body = f"schema=ccs-mod-config/1\nautostart={state}\n"; cf = self.p(MOD_CONFIG); os.makedirs(os.path.dirname(cf), exist_ok=True)
            with open(cf, "w", newline="\n") as f: f.write(body + f"end={_sha(body.encode())}\n")
            if state == "on":
                os.makedirs(os.path.dirname(q), exist_ok=True)
                with open(q, "wb") as f: f.write(tpl)
            elif os.path.exists(q) and _read(q) == tpl: os.remove(q)
            elif os.path.exists(q): self.writes.append(f"ccs-mod:autostart {state}"); return f"result autostart ok off kept-changed {OVERLAY}{MOD_START}"
            self.writes.append(f"ccs-mod:autostart {state}"); return f"result autostart ok {state}"
        return self._change(f"ccs-mod autostart {state}", act)
    def pack_uninstall(self, name: str):
        """hakchi's pack_uninstall: the uninstall list sourced. A CCS method package's own script returns before hakchi's generated list and
        removes only its unchanged files (here: the files its FILES list names with those bytes, and CCS Mod's start script while it is the one
        its command wrote: its journal names it); the earlier module's list removes its file"""
        mf = self.method_files(name) if name in METHODS.values() else None
        if mf is not None:
            m = name[4:]; fl = self.overlay_read(f"/etc/ccs/{m}/FILES") or b""
            want = {"/" + l.split(" ", 2)[2]: l.split(" ", 1)[0] for l in fl.decode().splitlines() if l.count(" ") >= 2} | {f"/etc/ccs/{m}/FILES": _sha(fl)}
            if m == "mod" and self.overlay_sha256(MOD_START) is not None and self.overlay_sha256(MOD_START) == want.get(MOD_START_TEMPLATE): want[MOD_START] = want[MOD_START_TEMPLATE]
            def act_ccs():
                for path, s in want.items():
                    q = self._ov(path)
                    if os.path.isfile(q) and _sha(_read(q)) == s: os.remove(q)
                # the folders left empty (here up to the overlay; on a console CCS's own removal keeps the empty folders hakchi made for it)
                top = self._ov("/"); dirs = set()
                for q in want:
                    d = os.path.dirname(self._ov(q))
                    while len(d) > len(top.rstrip(os.sep)) and d.startswith(top.rstrip(os.sep)): dirs.add(d); d = os.path.dirname(d)
                for d in sorted(dirs, key=lambda d: -len(d)):
                    if os.path.isdir(d) and not os.listdir(d): os.rmdir(d)
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
    """the last boundary: only the disposable stand-in exists here; a real console (ssh, clovershell) goes through the console worker
    (worker_console.open_worker_console), which the gate refuses before any device is looked for"""
    if kind == "fake": return FakeConsole(target)
    gate.require("install", f"open a {kind} console")
    from .worker_console import open_worker_console
    return open_worker_console(kind)

# ---------------------------------------------------------------- records on the console
def _record_bytes(obj: dict) -> bytes: return (json.dumps(obj, sort_keys=True, indent=1) + "\n").encode()
def read_records(c) -> dict:
    """this program's records on the console: {"versions": {copy: record}, "modules": {sha16: record}, "attempts": {id: record},
    "problems": [...]} (a record that cannot be read is a problem, never ownership)"""
    out = {"versions": {}, "modules": {}, "attempts": {}, "problems": [], "files": {}}
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
        m = re.match(r"^module-([0-9a-f]{16})\.json$", name)
        if m and rec.get("schema") in ("ccs-owned-module/1", "ccs-owned-module/2") and str(rec.get("sha256", "")).startswith(m.group(1)): out["modules"][m.group(1)] = rec | {"_record": path}; continue
        m = re.match(r"^attempt-([0-9a-f]{8})\.json$", name)
        if m and rec.get("schema") == "ccs-install-attempt/1" and rec.get("attempt") == m.group(1): out["attempts"][m.group(1)] = rec | {"_record": path}; continue
        out["problems"].append(f"{path} is not one of this program's records")
    return out

def owned_module_shas(recs: dict) -> set: return {r["sha256"] for r in recs["modules"].values()} | {module_sha()}

def legacy_state(c, recs: dict) -> str:
    """the earlier start-up module: "absent", "mine" (hakchi lists ccs-home with exactly its one file, which is gone (hakchi's own start removed
    it) or still this program's recorded bytes), or "foreign" (kept, and nothing is changed)"""
    mod = c.pack_files(LEGACY_NAME)
    if not mod: return "absent"
    return "mine" if set(mod) == {LEGACY_FILE} and (mod[LEGACY_FILE] is None or mod[LEGACY_FILE] in owned_module_shas(recs)) else "foreign"

def installed_methods(c, recs: dict) -> list[dict]:
    """each CCS method package hakchi lists on the console: {"name", "method", "package", "state"}: "current" (this installation's package,
    every file unchanged), "changed" (its files were changed since), "older" (another version of the method's CCS package, by its own
    VERSION record: its own removal takes only its own unchanged files), or "not this program's" (no package record: kept)"""
    out = []
    for m, n in METHODS.items():
        mf = c.method_files(n)
        if mf is None: continue
        mp = method_package(m)
        if mf.get("package") == mp["package_id"]: st = "current" if all((mf.get("files") or {}).get(p) == s for p, s in mp["files"].items()) else "changed"
        elif re.match(r"^[0-9a-f]{64}$", str(mf.get("package") or "")): st = "older"
        else: st = "not this program's"
        out.append({"name": n, "method": m, "package": mf.get("package"), "state": st})
    return out

def copy_state(c, copy: str, rec: dict) -> dict:
    """a copy's files against its record: ours (recorded and unchanged), changed (recorded, bytes differ), missing, unknown (not recorded)"""
    base = f"{PKG_DIR}/{copy}"; files = rec.get("files") or {}; ours, changed, missing = [], [], []
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

def remove_copy(c, copy: str, rec: dict, journal=None) -> dict:
    """a copy removed file by file: only its recorded, unchanged files; then its folders that are empty, deepest first; its record only
    when the folder is gone. Everything else is kept and reported"""
    base = f"{PKG_DIR}/{copy}"; files = rec.get("files") or {}; removed = 0
    for rel, h in sorted(files.items()):
        if c.stat(f"{base}/{rel}")["type"] != "file": continue   # not there (its folder may be missing too), or not a file: kept as it is
        if journal: journal(op="remove-file", path=f"{base}/{rel}", sha256=h)
        if c.remove(f"{base}/{rel}", h): removed += 1
    dirs = sorted({"/".join(rel.split("/")[:i]) for rel in files for i in range(1, rel.count("/") + 1)}, key=lambda d: -d.count("/"))
    for d in dirs + [""]:
        p = f"{base}/{d}" if d else base
        if c.stat(p)["type"] == "dir" and not c.listdir(p):
            if journal: journal(op="rmdir", path=p)
            c.rmdir(p)
    gone = c.stat(base)["type"] == "none"; kept = [] if gone else [f"{base}/{x}" for x in copy_state(c, copy, rec)["changed"] + copy_state(c, copy, rec)["unknown"]]
    rp = rec.get("_record")
    if gone and rp:
        b = c.read(rp, 1_000_000)
        if b is not None:
            if journal: journal(op="remove-record", path=rp, sha256=_sha(b))
            c.remove(rp, _sha(b))
    return {"copy": copy, "removed_files": removed, "removed": gone, "kept": kept}

# ---------------------------------------------------------------- the installer
class Installer:
    """one installation attempt of one plan on one console. journal: a new file (created exclusively; an existing journal belongs to an
    earlier attempt and is never reused) or an existing folder (a new home-<attempt>.jsonl is made in it). device: the hardware id this
    operation is bound to (the console that was checked and confirmed): compared with the console answering now at the preflight and again
    before each commit point (the switch, the module, the tidy). policy: "stop" (the app's physical operations and every real console: a
    failure stops all writes; recovery is its own approved operation, G3) or "rollback" (a disposable stand-in in a rehearsal: undone at once)"""
    def __init__(self, console, plan: dict, package_dir: str, journal: str, emit=None, cancel=None, attempt: str | None = None, device: str | None = None, policy: str | None = None):
        if getattr(console, "simulated", False) is not True: gate.require("inspect", "use a real console")   # and run() needs the install tier
        if policy not in (None, "stop", "rollback"): raise ValueError("policy: stop or rollback")
        if policy == "rollback" and getattr(console, "simulated", False) is not True: raise ValueError("only a disposable stand-in is rolled back by itself")
        self.c, self.plan, self.pkg = console, plan, package_dir; self.emit = emit or (lambda ev: None); self.cancel = cancel or (lambda: False)
        self.attempt = attempt or secrets.token_hex(4); self.v = plan["version"]; self.copy = f"{self.v}.{self.attempt}"; self.target = f"{PKG_DIR}/{self.copy}"
        self.journal = os.path.join(journal, f"home-{self.attempt}.jsonl") if os.path.isdir(journal) else journal
        self.auto_rollback = (policy == "rollback") if policy else console.simulated is True
        self.device = device
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
        for d in (ROOT_DIR, PKG_DIR, INST_DIR, DATA_DIR):
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
        mp = method_package(self.plan["method"]); meth = self.plan["method"]
        if mp["sha256"] != self.plan["module"]["sha256"]: raise Refused("preflight", "this installation's CCS Mod / CCS Nucleus package is not the one the plan names (prepare it again); nothing was changed")
        # the method the console uses now: the plan must be for it (the check chose it); the app never changes or adds CCS Nucleus itself
        cm = console_method(c)
        if cm not in ("none", meth): raise Refused("preflight", f"this console uses {METHOD_LABELS[cm]}; this plan installs the HOME for {METHOD_LABELS[meth]}. Classic Cart Studio never changes a console's method to install games: check the plan again and it is made for {METHOD_LABELS[cm]}. Nothing was changed")
        if cm == "none" and meth != DEFAULT_METHOD: raise Refused("preflight", f"{METHOD_LABELS[meth]} is not installed on this console (any more). Classic Cart Studio installs a collection into the method a console already uses and adds only CCS Mod itself: check the plan again. Nothing was changed")
        module_state = "absent" if cm == "none" else method_state(c, mp, recs); legacy = legacy_state(c, recs)
        start = None
        if meth == "mod":   # CCS Mod's start at power-on: as the plan chose it (its own command changes it only when it differs)
            ms = mod_start_state(c)
            if ms["script"] == "changed": raise Refused("preflight", f"a start script named S81ccs-mod is on the console that CCS Mod did not write (or it was changed since); it is kept, and nothing was changed")
            start = {"now": ms["state"], "want": self.plan["mod_start"], "change": ms["state"] != self.plan["mod_start"]}
        if legacy == "foreign": raise Refused("preflight", f"a {LEGACY_NAME} module is installed that this program did not make (or that was changed); nothing was changed")
        ident = c.read(f"{DATA_DIR}/IDENTITY.json", 65536); data = {"state": "new"}
        if ident is not None:
            try: d = json.loads(ident)
            except ValueError: d = None
            if not isinstance(d, dict) or d.get("schema") != "ccs-console-data/1": raise Refused("preflight", f"{DATA_DIR}/IDENTITY.json cannot be read; the saves there are kept and nothing was changed")
            if d.get("state_identity") != self.plan["state_identity"]:
                raise Refused("preflight", f"the saves on the console were made by another player build ({d.get('state_identity')}); this package's player ({self.plan['state_identity']}) would not load them. Nothing was changed")
            data = {"state": "kept", "identity": d.get("state_identity")}
        elif c.stat(DATA_DIR)["type"] == "dir" and c.listdir(DATA_DIR): data = {"state": "kept-unidentified"}
        same = None
        if cur and recs["versions"][cur].get("manifest_sha256") == self.plan["manifest_sha256"] and module_state == "current":   # this exact package in use (by its record; a start change alone copies nothing again)
            st = copy_state(c, cur, recs["versions"][cur]); same = not st["changed"] and not st["missing"]
        need = 0 if same else self.plan["needs_free_bytes"]; free = c.free_bytes(ROOT_DIR)
        if not same and free < need: raise Refused("preflight", f"not enough room on the console: {free / 1e6:.1f} MB free, {need / 1e6:.1f} MB needed")
        unknown = [f"{PKG_DIR}/{x}" for x in c.listdir(PKG_DIR) if x != "current" and x not in recs["versions"]]
        unknown += [f"{ROOT_DIR}/{x}" for x in c.listdir(ROOT_DIR) if x not in ("pkg", "data", "install") + METHOD_RECORDS]
        older = [v for v in recs["versions"] if v != cur]
        return {"identity": i, "device": dev, "free_bytes": free, "needs_bytes": need, "in_use": cur, "pointer_sha256": psha, "already": bool(same and legacy == "absent" and not (start and start["change"])), "same_copy": bool(same),
                "module": module_state, "method": meth, "console_method": cm, "start": start, "legacy": legacy, "data": data, "kept_unknown": unknown, "earlier_copies": older, "records": len(recs["files"])}

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
            return {"ok": False, "result": "refused", "at": e.step, "why": e.why, "attempt": self.attempt, "changed": False}
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

    def _mod_start(self, want: str) -> str:
        """CCS Mod's own command on the console (ccs-mod autostart on|off); anything but its plain success stops the attempt"""
        r = self.c.mod_start(want)
        if r != f"result autostart ok {want}": raise InstallStopped("start", f"CCS Mod's own command answered: {r}")
        return r

    def _install(self, pf):
        c = self.c; plan = self.plan
        cur = pf["in_use"]; mp = method_package(plan["method"]); mf = c.method_files(mp["name"]) if pf["module"] != "absent" else None
        same = pf.get("same_copy") is True; start = pf.get("start")   # this exact package already in use: only the method and the start change
        self.prior = {"pointer": cur, "pointer_sha256": pf["pointer_sha256"], "module": pf["module"], "module_package": (mf or {}).get("package"), "start": (start or {}).get("now")}
        rec = {"schema": "ccs-install-attempt/1", "attempt": self.attempt, "plan": plan["hash"], "version": self.v, "copy": self.copy, "kind": "install",
               "device": {k: pf["device"].get(k) for k in ("hwid", "system", "kernel", "hakchi")}, "started": _now(),
               "prior": {"pointer": cur, "module": pf["module"], "method": plan["method"], "module_package": self.prior["module_package"], "start": self.prior["start"]}}
        rec_b = _record_bytes(rec); self.attempt_path = f"{INST_DIR}/attempt-{self.attempt}.json"
        # 1. this program's folders, then the attempt record (with what was there before), persistent before anything else changes
        for d in (ROOT_DIR, INST_DIR, PKG_DIR):
            if c.stat(d)["type"] == "none": self._do("mkdir", lambda d=d: c.mkdir(d), {"op": "rmdir-if-empty", "path": d}, path=d)
        self._do("record-attempt", lambda: c.put(self.attempt_path, rec_b), {"op": "remove", "path": self.attempt_path, "sha256": _sha(rec_b)}, path=self.attempt_path)
        self._sync(why="the attempt record and what it keeps of the earlier state, before anything else changes")
        # 2. the copy's record, then the copy (a new folder of this attempt, beside the one in use); not when this exact package is in use
        files = {f["path"]: f["sha256"] for f in plan["files"]}
        if not same:
            vrec = {"schema": "ccs-owned-copy/1", "copy": self.copy, "plan": plan["hash"], "version": self.v, "attempt": self.attempt, "manifest_sha256": plan["manifest_sha256"], "files": files}
            vrec_b = _record_bytes(vrec); vpath = f"{INST_DIR}/v-{self.copy}.json"
            self._do("record-copy", lambda: c.put(vpath, vrec_b), {"op": "remove", "path": vpath, "sha256": _sha(vrec_b)}, path=vpath)
            self._do("mkdir", lambda: c.mkdir(self.target), {"op": "remove-copy", "copy": self.copy}, path=self.target)
            self._progress("stage", done=0, total=len(plan["files"]), bytes=0, total_bytes=plan["bytes"]); sent = 0
            made = set()
            for k, f in enumerate(plan["files"]):
                if self.cancel(): raise Cancelled("canceled before the switch")
                parts = f["path"].split("/")[:-1]
                for i in range(1, len(parts) + 1):
                    d = "/".join(parts[:i])
                    if d not in made: self._do("mkdir", lambda d=d: c.mkdir(f"{self.target}/{d}"), None, path=f"{self.target}/{d}"); made.add(d)
                data = _read(os.path.join(self.pkg, *f["path"].split("/")))
                if _sha(data) != f["sha256"]: raise InstallStopped("stage", f"{f['path']} on this computer changed after the plan was made (prepare it again)")
                src = os.path.join(self.pkg, *f["path"].split("/"))
                self._do("put", lambda f=f, data=data, src=src: c.put(f"{self.target}/{f['path']}", data, f["mode"], source=src), None, path=f"{self.target}/{f['path']}", sha256=f["sha256"], size=f["size"])
                sent += f["size"]; self._progress("stage", done=k + 1, total=len(plan["files"]), bytes=sent, total_bytes=plan["bytes"])
            # 3. the data folder and its identity (only when there is none: existing saves are kept as they are)
            if c.stat(DATA_DIR)["type"] == "none": self._do("mkdir", lambda: c.mkdir(DATA_DIR), {"op": "rmdir-if-empty", "path": DATA_DIR}, path=DATA_DIR)
            if c.stat(f"{DATA_DIR}/IDENTITY.json")["type"] == "none":
                ib = _record_bytes({"schema": "ccs-console-data/1", "state_identity": plan["state_identity"], "created": _now(), "note": "saves, slots and settings made on this console by Classic Cart Studio's console player"})
                self._do("data-identity", lambda: c.put(f"{DATA_DIR}/IDENTITY.json", ib), {"op": "remove", "path": f"{DATA_DIR}/IDENTITY.json", "sha256": _sha(ib)}, path=f"{DATA_DIR}/IDENTITY.json")
            # 4. persistent, then read back every file from the storage (the page cache dropped), before the switch
            self._sync(drop=True, why="the new copy, before it is read back and switched to")
            bad = [rel for rel, h in files.items() if c.sha256(f"{self.target}/{rel}") != h]; self.log(op="readback", mismatches=bad[:20], files=len(files))
            self._progress("readback", ok=not bad, files=len(files))
            if bad: raise InstallStopped("readback", f"{len(bad)} file(s) on the console differ from the plan ({', '.join(bad[:3])})")
            if self.cancel(): raise Cancelled("canceled before the switch")
            # 5. the switch: one rename of the pointer, only over exactly what the preflight read there; then persistent
            same_console(c, self.device, "switch")
            expect = pf["pointer_sha256"] if cur else "absent"
            self._do("switch", lambda: c.set_pointer(POINTER, self.copy, expect=expect), {"op": "pointer", "value": cur}, previous=cur, value=self.copy); self._progress("switch", ok=True)
            self._sync(why="the switch")
        # 6. the install method's package (when absent or this program's older version), through hakchi's own pack_install; its record first
        if pf["module"] != "current":
            same_console(c, self.device, "module")
            mb = mp["hmod"]
            if _sha(mb) != plan["module"]["sha256"]: raise InstallStopped("module", "the method's package on this computer is not the one the plan names (prepare it again)")
            mrec_b = _record_bytes({"schema": "ccs-owned-module/2", "name": mp["name"], "method": plan["method"], "sha256": mp["sha256"], "package_id": mp["package_id"]})
            mpath = f"{INST_DIR}/module-{mp['sha256'][:16]}.json"
            if c.stat(mpath)["type"] == "none": self._do("record-module", lambda: c.put(mpath, mrec_b), {"op": "remove", "path": mpath, "sha256": _sha(mrec_b)}, path=mpath)
            self._sync(why="the module record, before the module")
            self._do("module", lambda: c.pack_install(mp["name"], mb), {"op": "module", "name": mp["name"], "prior": pf["module"], "prior_package": self.prior["module_package"]}, module=mp["name"], prior=pf["module"])
            self._progress("module", ok=True)
        # 6b. CCS Mod's start at power-on as the plan chose it, by CCS Mod's own command, read again after its package step (installing CCS Mod
        # applies the setting it keeps); only when it differs
        if start is not None:
            now = mod_start_state(c)
            if now["script"] == "changed": raise InstallStopped("start", "a start script named S81ccs-mod that CCS Mod did not write is on the console now; it is kept")
            if now["state"] != plan["mod_start"]:
                same_console(c, self.device, "start")
                self._do("mod-start", lambda: self._mod_start(plan["mod_start"]), {"op": "mod-start", "prior": now["state"], "want": plan["mod_start"]}, want=plan["mod_start"], prior=now["state"])
            self._progress("start", ok=True)
        # 7. verify everything the console will start from, as the storage holds it
        self._sync(drop=True, why="before the final verification")
        vcopy = cur if same else self.copy
        bad = [rel for rel, h in files.items() if c.sha256(f"{PKG_DIR}/{vcopy}/{rel}") != h]; pb = c.read(POINTER, 64); mf = c.method_files(mp["name"])
        st = mod_start_state(c)["state"] if start is not None else None
        ok = not bad and pb == pointer_bytes(vcopy) and mf is not None and mf.get("package") == mp["package_id"] and all((mf.get("files") or {}).get(p) == s for p, s in mp["files"].items()) and st == plan.get("mod_start")
        self.log(op="verify", ok=ok, mismatches=bad[:20], pointer=(pb or b"").decode(errors="replace").strip(), method=mp["name"], method_package=(mf or {}).get("package"), start=st); self._progress("verify", ok=ok)
        if not ok: raise InstallStopped("verify", f"after the switch, the copy, the pointer, {mp['name']}'s package or its start at power-on is not as planned")
        # installed: from here nothing is undone. 8. retire this program's earlier start-up module (hakchi deletes its file at every start anyway);
        # then tidy what this program owns of the earlier copies and module records
        self.undo = []; self.log(op="installed"); tidy = []
        same_console(c, self.device, "tidy")
        if pf.get("legacy") == "mine":
            try: self.log(op="retire", state="intent", module=LEGACY_NAME); c.pack_uninstall(LEGACY_NAME); self.log(op="retire", state="done")
            except (ConnectionError, OSError) as e: tidy.append({"module": LEGACY_NAME, "removed": False, "why": str(e)})
        recs = read_records(c)
        for v, r in sorted(recs["versions"].items()):
            if v in (self.copy, vcopy): continue
            try: tidy.append(remove_copy(c, v, r, journal=lambda **k: self.log(**k)))
            except (ConnectionError, OSError) as e: tidy.append({"copy": v, "removed": False, "why": str(e)})
        for k16, r in recs["modules"].items():
            if r["sha256"] != mp["sha256"] and (r.get("schema") == "ccs-owned-module/2" or not c.pack_files(LEGACY_NAME)):   # an earlier one's record, once nothing of it is installed
                b = c.read(r["_record"], 65536)
                try:
                    if b is not None: self.log(op="remove-record", path=r["_record"], sha256=_sha(b)); c.remove(r["_record"], _sha(b))
                except (ConnectionError, OSError): pass
        self.log(op="tidy", result=tidy)
        # 9. the attempt is finished: its record goes last, after the tidy is persistent
        try: self._sync(why="the tidy, before the attempt record is closed"); c.remove(self.attempt_path, _sha(rec_b)); self._sync(why="the closed attempt"); finished = True
        except (ConnectionError, OSError, InstallStopped) as e: finished = False; self.log(op="finish", ok=False, why=str(e))
        self.log(op="end", result="installed" if finished else "installed-record-open"); self._progress("done", ok=True)
        kept = [k for t in tidy for k in t.get("kept", [])]
        self.done = {"ok": True, "result": "installed", "version": self.v, "copy": vcopy, "attempt": self.attempt, "mod_start": st, "written_bytes": getattr(c, "written_bytes", None), "preflight": pf,
                     "tidy": tidy, "kept_changed_or_unknown": kept + pf["kept_unknown"], "record_closed": finished, "changed": True, "device": pf["device"]["hwid"]}

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
        try: data = _read(os.path.join(self.pkg, *rel.split("/")))
        except OSError: return None
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
        r = remove_copy(c, self.copy, {"files": {k: v["sha256"] for k, v in planned.items()}})
        if not r["removed"] and not problems: problems.append(f"{base} keeps {len(r['kept'])} item(s) this attempt did not write; they are kept")
        return problems

    def _rollback(self, undo: list[dict], prior: dict | None) -> dict:
        """each change undone in reverse order, only over exactly what this attempt made; at the first thing that is not, nothing more is
        written: the attempt's records stay on the console and the result says what is known"""
        c = self.c; done, problems = [], []
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
                elif op == "module":
                    mp = method_package(self.plan["method"]); now = c.method_files(u.get("name") or mp["name"])
                    if u["prior"] == "absent":
                        if now is not None and now.get("package") == mp["package_id"] and all((now.get("files") or {}).get(p) == s for p, s in mp["files"].items()): c.pack_uninstall(mp["name"])
                        elif now is not None: problems.append(f"{mp['name']}'s package is not exactly the one this attempt installed (or its files were changed since); it is left as it is")
                    elif now is None or now.get("package") != u.get("prior_package"):
                        problems.append(f"{mp['name']}'s earlier version was updated by this attempt; it is not put back (the newer one stays)")
                elif op == "mod-start":   # CCS Mod's start back as it was, by its own command, only while it is still this attempt's
                    now = mod_start_state(c)["state"]
                    if now == u["prior"]: pass
                    elif now == u["want"] and c.method_files(METHODS["mod"]) is not None:
                        if c.mod_start(u["prior"]) != f"result autostart ok {u['prior']}": problems.append("CCS Mod's start at power-on could not be put back; it is left as it is")
                    else: problems.append("CCS Mod's start at power-on is neither as it was nor as this attempt set it; it is left as it is")
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
        if problems: out["problems"] = problems
        if not ok: out["next"] = "The rollback is not complete: no further writes. The journal and the console's attempt record stay and say what is where; collect diagnostics."
        return out

    def _verify_prior(self, prior: dict | None) -> dict:
        c = self.c; checks = {}
        if prior is None: return {"ok": True, "checks": {"nothing": "changed"}}
        b = c.read(POINTER, 64); ptr = b.decode(errors="replace").strip() if b is not None else None
        checks["pointer"] = ptr == prior["pointer"]
        mf = c.method_files(method_package(self.plan["method"])["name"])
        checks["module"] = mf is None if prior["module"] == "absent" else (mf or {}).get("package") == prior.get("module_package")
        if self.plan["method"] == "mod" and prior.get("start") is not None: checks["start"] = mod_start_state(c)["state"] == prior["start"]
        checks["copy_removed"] = c.stat(self.target)["type"] == "none"
        checks["attempt_record_removed"] = c.stat(f"{INST_DIR}/attempt-{self.attempt}.json")["type"] == "none"
        checks["copy_record_removed"] = c.stat(f"{INST_DIR}/v-{self.copy}.json")["type"] == "none"
        return {"ok": all(checks.values()), "checks": checks}

    def resume(self) -> dict:
        """recover an interrupted attempt from ITS journal: only on the console that attempt was on (read now, compared with the journal, the
        console's attempt record and the confirmed console) and only for its plan; a finished attempt is never undone. A real console is refused
        here by the gate: its recovery is its own approved operation (G3)"""
        if getattr(self.c, "simulated", False) is not True: gate.require("install", "recover an attempt on a real console")
        try: es = self.entries(self.journal)
        except JournalCorrupt as e: return {"ok": False, "result": "unknown", "why": f"{e}; nothing was changed. Collect diagnostics"}
        if not es: return {"ok": True, "result": "nothing to resume", "why": "no journal"}
        begin = es[0]; attempt = begin.get("attempt"); ends = [e for e in es if e.get("op") == "end"]
        if begin.get("op") != "begin" or not re.match(r"^[0-9a-f]{8}$", str(attempt)): return {"ok": False, "result": "unknown", "why": "the journal does not begin with its attempt; nothing was changed"}
        if begin.get("plan") != self.plan["hash"]: return {"ok": False, "result": "refused", "why": "this journal belongs to another plan"}
        if ends and ends[-1].get("result") not in ("stopped-needs-recovery", "installed-record-open", "rollback-incomplete"): return {"ok": True, "result": "nothing to resume", "why": f"that attempt ended ({ends[-1].get('result')})"}
        try: dev = check_device(self.c.identity(fresh=True))
        except Refused as e: return {"ok": False, "result": "refused", "why": e.why}
        pf = next((e for e in es if e.get("op") == "preflight"), None); jh = ((pf or {}).get("identity") or {}).get("hwid")
        if self.device and dev["hwid"] != self.device: return {"ok": False, "result": "refused", "why": "this is not the console that was checked and confirmed; nothing was changed"}
        if jh and dev["hwid"] != jh: return {"ok": False, "result": "refused", "why": "the console answering now is not the one this attempt changed; nothing was changed"}
        self.device = dev["hwid"]
        self.attempt = attempt; self.copy = begin.get("copy") or f"{self.v}.{attempt}"; self.target = f"{PKG_DIR}/{self.copy}"
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
        pr = rec.get("prior") or {}; prior = {"pointer": pr.get("pointer"), "module": pr.get("module"), "module_package": pr.get("module_package"), "start": pr.get("start")}
        undo = [e["undo"] for e in es if e.get("state") == "intent" and e.get("undo")]
        rb = self._rollback(undo, prior); self.log(op="end", result="rolled-back" if rb["ok"] else "rollback-incomplete", rolled_back=rb, how="after an interruption")
        return {"ok": rb["ok"], "result": "rolled-back" if rb["ok"] else "rollback-incomplete", "rolled_back": rb, "attempt": attempt}

# ---------------------------------------------------------------- inspect, verify, uninstall, export
def inspect_console(c, device: str | None = None) -> dict:
    """what is on the console now (reads only): its identity against the supported console, this program's records, the copy in use, the
    start-up module, attempts that did not finish, the saves folder and the room left. A refusal is reported, never a guess"""
    if getattr(c, "simulated", False) is not True: gate.require("inspect", "read a real console")
    try: dev = check_device(c.identity(fresh=True), "inspect")
    except Refused as e: return {"ok": False, "why": e.why, "identity": {k: v for k, v in c.identity().items() if k != "hwid"}}
    if device and dev["hwid"] != device: return {"ok": False, "why": "this is not the console that was checked before; check it again"}
    recs = read_records(c); pst = c.stat(POINTER); pb = c.read(POINTER, 64) if pst["type"] == "file" else None
    pv = pb.decode("ascii", "replace").strip() if pb is not None else None
    in_use = pv if pb is not None and COPY_RE.match(pv or "") and pv in recs["versions"] and pb == pointer_bytes(pv) else None
    ms = installed_methods(c, recs); module = "; ".join(f"{x['name']} ({x['state']})" for x in ms) or "absent"
    copy = None
    if in_use:
        s = copy_state(c, in_use, recs["versions"][in_use]); copy = {"copy": in_use, "plan": recs["versions"][in_use].get("plan"), "files": len(s["ours"]), "changed": s["changed"][:20], "missing": s["missing"][:20]}
    data = {"present": c.stat(DATA_DIR)["type"] == "dir"}
    try: cm, cm_why = console_method(c), None
    except Refused as e: cm, cm_why = None, e.why
    return {"ok": True, "device": dev, "in_use": copy, "pointer": "this program's" if in_use else ("absent" if pst["type"] == "none" else "not this program's"), "module": module, "methods": ms,
            "method": cm, "method_problem": cm_why, "mod_start": mod_start_state(c)["state"] if cm == "mod" else None,
            "earlier_module": legacy_state(c, recs), "attempts": sorted(recs["attempts"]), "copies": sorted(recs["versions"]), "record_problems": recs["problems"][:10], "data": data, "free_bytes": c.free_bytes(ROOT_DIR)}

def verify_installed(c, plan: dict, device: str | None = None) -> dict:
    """the console starts exactly this plan's copy: pointer, every file of the copy (after a sync with the page cache dropped, as the storage
    holds them) and the plan's method package, current and unchanged. Reads only (sync writes nothing)"""
    if getattr(c, "simulated", False) is not True: gate.require("inspect", "read a real console")
    try: dev = check_device(c.identity(fresh=True), "verify")
    except Refused as e: return {"ok": False, "verified": False, "why": e.why}
    if device and dev["hwid"] != device: return {"ok": False, "verified": False, "why": "this is not the console that was checked before; check it again"}
    recs = read_records(c); pb = c.read(POINTER, 64); pv = (pb or b"").decode("ascii", "replace").strip()
    rec = recs["versions"].get(pv) if pb is not None and pb == pointer_bytes(pv) else None
    if not rec or rec.get("manifest_sha256") != plan["manifest_sha256"]: return {"ok": True, "verified": False, "why": "the console does not start this plan's copy", "in_use": pv if rec else None}
    s = getattr(c, "sync", None)
    if s: s(drop_caches=True)
    files = {f["path"]: f["sha256"] for f in plan["files"]}; base = f"{PKG_DIR}/{pv}"
    bad = [rel for rel, h in files.items() if c.sha256(f"{base}/{rel}") != h]
    try: mstate = method_state(c, method_package(plan["method"]), recs)
    except Refused: mstate = "not as planned"
    st = mod_start_state(c)["state"] if plan["method"] == "mod" else None
    ok = not bad and mstate == "current" and st == plan.get("mod_start")
    return {"ok": True, "verified": ok, "copy": pv, "mismatches": bad[:20], "module": "current" if mstate == "current" else "not as planned", "method": plan["method"], "mod_start": st, "attempts": sorted(recs["attempts"]), "device": dev["hwid"]}

def uninstall_plan(c, keep_data: bool = True, device: str | None = None) -> dict:
    """exactly what an uninstall would do, read from the console (nothing is changed): its actions in order, each naming the bytes it removes
    (by SHA-256), what is kept, and a hash over all of it for the confirmation. uninstall() rebuilds this and runs it only when it is still
    the same; a pointer, copy file or record is removed only when it is this program's by its records and its bytes"""
    if getattr(c, "simulated", False) is not True: gate.require("inspect", "read a real console")
    try: dev = check_device(c.identity(fresh=True), "uninstall")
    except Refused as e: return {"ok": False, "why": e.why}
    if device and dev["hwid"] != device: return {"ok": False, "why": "this is not the console that was checked and confirmed; nothing would be changed"}
    for d in (ROOT_DIR, PKG_DIR, INST_DIR, DATA_DIR):
        t = c.stat(d)["type"]
        if t not in ("none", "dir"): return {"ok": False, "why": f"{d} is {'a link' if t == 'link' else 'not a folder'}; nothing would be changed"}
    recs = read_records(c)
    if recs["problems"]: return {"ok": False, "why": "this program's records on the console cannot all be read: " + "; ".join(recs["problems"][:3])}
    if recs["attempts"]: return {"ok": False, "why": f"an attempt ({sorted(recs['attempts'])[0]}) did not finish; recover it first"}
    actions, remove, keep = [], [], []
    ms = installed_methods(c, recs); legacy = legacy_state(c, recs)
    for x in ms:
        if x["state"] == "not this program's": keep.append(f"{x['name']} (a package this installation does not recognize)"); continue
        actions.append({"op": "method-uninstall", "name": x["name"], "package": x["package"]})
        remove.append(f"{'CCS Nucleus' if x['method'] == 'nucleus' else 'CCS Mod'} ({x['name']}, through hakchi's pack_uninstall: its own removal takes only its unchanged files; its settings stay): the console starts with its own menu, without CCS HOME's entry"
                      + (" and without CCS HOME at power-on" if x["method"] == "mod" and mod_start_state(c)["state"] == "on" else ""))
    if legacy == "mine":
        actions.append({"op": "legacy-uninstall"}); remove.append(f"this program's earlier start-up module {LEGACY_NAME} (hakchi's own list for it)")
    elif legacy == "foreign": keep.append(f"{LEGACY_NAME} (a module this program did not make)")
    pst = c.stat(POINTER); pb = c.read(POINTER, 64) if pst["type"] == "file" else None; pv = pb.decode("ascii", "replace").strip() if pb is not None else None
    owned_ptr = pb is not None and bool(COPY_RE.match(pv or "")) and pv in recs["versions"] and pb == pointer_bytes(pv)
    if owned_ptr: actions.append({"op": "remove", "path": POINTER, "sha256": _sha(pb)}); remove.append(POINTER)
    elif pst["type"] != "none": keep.append(f"{POINTER} (not this program's pointer)")
    for v, r in sorted(recs["versions"].items()):
        base = f"{PKG_DIR}/{v}"; s = copy_state(c, v, r); files = r.get("files") or {}
        actions += [{"op": "remove", "path": f"{base}/{rel}", "sha256": files[rel]} for rel in sorted(s["ours"])]
        dirs = sorted({"/".join(rel.split("/")[:i]) for rel in files for i in range(1, rel.count("/") + 1)}, key=lambda d: (-d.count("/"), d))
        actions += [{"op": "rmdir-if-empty", "path": f"{base}/{d}" if d else base} for d in dirs + [""]]
        rb = c.read(r["_record"], 1_000_000)
        others = [f"{base}/{x} (changed)" for x in s["changed"]] + [f"{base}/{x} (not this program's)" for x in s["unknown"]]
        if others: keep += others + [f"{r['_record']} (the record of {base}, which keeps those items)"]
        elif rb is not None: actions.append({"op": "remove-record", "path": r["_record"], "sha256": _sha(rb), "when_gone": base})
        remove.append(f"{base}: {len(s['ours'])} file(s) of this program")
    for k16, r in sorted(recs["modules"].items()):
        rb = c.read(r["_record"], 65536)
        if rb is not None: actions.append({"op": "remove-record", "path": r["_record"], "sha256": _sha(rb), "when_module_gone": True})
    keep += [f"{PKG_DIR}/{x} (not this program's)" for x in c.listdir(PKG_DIR) if x != "current" and x not in recs["versions"]]
    keep += [f"{ROOT_DIR}/{x} (not this program's)" for x in c.listdir(ROOT_DIR) if x not in ("pkg", "data", "install") + METHOD_RECORDS]
    keep += [f"{ROOT_DIR}/{x} (the install method's own records and settings: kept)" for x in c.listdir(ROOT_DIR) if x in METHOD_RECORDS]
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
    actions += [{"op": "rmdir-if-empty", "path": d} for d in (PKG_DIR, INST_DIR, ROOT_DIR)]
    body = {"schema": "ccs-uninstall-review/1", "device": dev["hwid"], "keep_data": keep_data, "actions": actions, "keep": keep, "pointer_sha256": _sha(pb) if pb is not None else None}
    return {"ok": True, "remove": remove, "keep": keep, "module": bool([a for a in actions if a["op"] in ("method-uninstall", "legacy-uninstall")]), "copies": sorted(recs["versions"]), "pointer": {"owned": owned_ptr, "value": pv if owned_ptr else None},
            "actions": actions, "device": dev["hwid"], "keep_data": keep_data, "pointer_sha256": body["pointer_sha256"], "hash": _sha(_canon(body))}

def uninstall(console, keep_data: bool = True, journal: str | None = None, expect: str | None = None, device: str | None = None) -> dict:
    """the reviewed removal, exactly: uninstall_plan() is read again and must still be the reviewed one (expect: its hash); then each action
    runs over exactly the bytes it names (the worker's remove compares the SHA-256 in the same command), so something changed since the review
    is kept and reported. The start-up module goes first (the console starts Nintendo's HOME from then on). Interrupted, the review is made
    again and shows what is left; every step is safe to repeat"""
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
            if op == "method-uninstall":
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
                if a.get("when_module_gone") and (c.pack_files(LEGACY_NAME) or any(c.method_files(n) is not None for n in METHODS.values())): kept.append(f"{path} (the module is still there)"); continue
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
        mod_after = [n for n in METHODS.values() if c.method_files(n) is not None and n in {a["name"] for a in plan["actions"] if a["op"] == "method-uninstall"}] + ([LEGACY_NAME] if c.pack_files(LEGACY_NAME) and any(a["op"] == "legacy-uninstall" for a in plan["actions"]) else []); pa = c.stat(POINTER)["type"]
        ptr_ok = (pa == "none") if plan["pointer"]["owned"] else ((c.sha256(POINTER) if pa == "file" else None) == plan["pointer_sha256"])
        gone = [a["path"] for a in plan["actions"] if a["op"] == "remove" and c.stat(a["path"])["type"] != "none"]
        checks = {"module_removed": not mod_after, "pointer": ptr_ok, "planned_removals_left": len(gone)}
    except (ConnectionError, OSError) as e: checks = {"unreadable": str(e)}; problems.append(f"the result could not be read back: {e}")
    complete = not problems and not kept and checks.get("module_removed") is True and checks.get("pointer") is True and checks.get("planned_removals_left") == 0
    data = "kept" if keep_data else ("removed" if c.stat(DATA_DIR)["type"] == "none" else "partly kept")
    log(op="end", result="uninstalled" if complete else "incomplete", problems=problems[:10], kept=kept[:50])
    return {"ok": complete, "result": "uninstalled" if complete else "incomplete", "removed": removed, "kept_changed": kept, "kept_unknown": sorted(x for x in plan["keep"] if "not this program's" in x or "not followed" in x),
            "problems": problems, "data": data, "module": "removed" if plan["module"] else "absent", "verified": checks, "changed": bool(removed),
            "after_restart": ("The console starts with its own menu, games and saves; CCS HOME's entry is gone and Classic Cart Studio's HOME no longer starts." if checks.get("module_removed") else
                              "Not everything was removed: the console may still offer Classic Cart Studio's HOME. Review the removal again.")}

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
_READY = {"kernel": HAKCHI_KERNEL, "hakchi": "v1.0.4-126", "system": "nes-usa", "can_interact": True, "custom_firmware": False}
HOME_PROFILES = {   # what the console would be, never a real one
    "hakchi-ready": _READY | {"hwid": "sim-0001", "free_bytes": 200_000_000},
    "stock": {"kernel": "3.4.113", "hakchi": None, "system": "nes-usa", "can_interact": None, "custom_firmware": None, "hwid": "sim-0002", "free_bytes": 200_000_000},
    "nearly-full": _READY | {"hwid": "sim-0003", "free_bytes": 5_000_000},
    "other-module": _READY | {"hwid": "sim-0004", "free_bytes": 200_000_000, "other_method": "nucleus"},   # CCS Nucleus already there (the Workbench installed it)
    "mod-module": _READY | {"hwid": "sim-0008", "free_bytes": 200_000_000, "other_method": "mod"},       # CCS Mod already there
    "both-methods": _READY | {"hwid": "sim-0009", "free_bytes": 200_000_000, "other_method": "mod+nucleus"},   # both: a change between them that did not finish
}
IDENTITY_PROFILES = {   # refusals by identity (tests): another system, an older hakchi, a custom firmware, an identity that cannot be read
    "snes-eur": _READY | {"system": "snes-eur", "hwid": "sim-0005", "free_bytes": 200_000_000},
    "old-hakchi": _READY | {"hakchi": "v1.0.0-000", "can_interact": False, "hwid": "sim-0006", "free_bytes": 200_000_000},
    "custom-firmware": _READY | {"custom_firmware": True, "hwid": "sim-0007", "free_bytes": 200_000_000},
    "no-identity": _READY | {"hwid": None, "free_bytes": 200_000_000},
}
STOCK_SAMPLE = {"usr/share/games/nes/kachikachi/CLV-P-NAAAE/CLV-P-NAAAE.nes": b"stock game", "var/saves/CLV-P-NAAAE/save.sav": b"a save",
                "etc/init.d/S81clover-mcp": b"#!/bin/sh\nclover-mcp\n", "var/lib/hakchi/hmod/uninstall-clovercon": b'rm -f "$rootfs/lib/x"\n',
                "var/lib/hakchi/rootfs/lib/x": b"clovercon module file", "var/lib/ccs/pkg/someone-elses/keep.txt": b"unknown"}

def make_stand_in(root: str, profile: str, durable: bool = False) -> FakeConsole:
    """a fresh disposable stand-in (a stock game and save, the stock start script, another hakchi module, an unknown folder)"""
    prof = HOME_PROFILES.get(profile) or IDENTITY_PROFILES.get(profile)
    if prof is None: raise KeyError(f"unknown profile {profile!r}")
    os.makedirs(root, exist_ok=True); info = dict(prof)
    with open(os.path.join(root, "console.json"), "w", encoding="utf-8") as f: json.dump(info, f)
    sample = dict(STOCK_SAMPLE)
    if info.get("foreign_s81"): sample["var/lib/hakchi/rootfs/etc/init.d/S81clover-mcp"] = b"#!/bin/sh\n# another module's start script\n"
    for rel, b in sample.items():
        q = os.path.join(root, *rel.split("/")); os.makedirs(os.path.dirname(q), exist_ok=True)
        with open(q, "wb") as f: f.write(b)
    os.makedirs(os.path.join(root, "var/lib/hakchi/rootfs/etc/init.d"), exist_ok=True)   # hakchi's own overlay carries its start scripts' folder
    for m in (info.get("other_method") or "").split("+"):
        if m: mp = method_package(m); FakeConsole(root).pack_install(mp["name"], mp["hmod"])
    return FakeConsole(root, durable=durable)

def stock_digest(root: str) -> dict:   # the stand-in's stock and foreign sample as it is now (compared before and after)
    out = {}
    for rel in STOCK_SAMPLE:
        q = os.path.join(root, *rel.split("/")); out[rel] = _sha(_read(q)) if os.path.isfile(q) else None
    return out
