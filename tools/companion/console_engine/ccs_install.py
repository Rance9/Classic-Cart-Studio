"""Installing Classic Cart Studio's HOME on a console that runs hakchi (K3). Implemented and tested here against disposable stand-ins; a real
console is closed by Gate G1 in code (open_console refuses every real kind; the installer refuses a console that is not a simulation unless the
install tier is open, and even then never rolls back by itself: docs/console-install-architecture.md sections 13 and 14).

Where (all under /var/lib/ccs; nothing else on the console is written except through hakchi's own module commands):
  pkg/<version>.<attempt>/   one immutable copy of a console package per installation attempt (version = the first 16 hex of the SHA-256 of
                             its MANIFEST.json, attempt = 8 hex): a repair or an update never changes the copy in use
  pkg/current                a small file naming the copy in use ("<version>.<attempt>"), replaced in one rename. A file, not a link: the
                             console's BusyBox 1.24.1 has no "mv -T", and "mv" onto a link to a folder moves into that folder
  install/                   this program's records on the console, each written before what it records: v-<copy>.json (the files of a
                             copy and their SHA-256), module-<sha16>.json (the start-up module's file), attempt-<attempt>.json (an attempt
                             in progress, with what it found before changing anything)
  data/                      the console's own saves, Game Settings and HOME choices (kept across updates; kept by uninstall unless removal
                             is asked for), with IDENTITY.json naming the player build that made them
Start-up: the hakchi module "ccs-home" carries one file, /etc/init.d/S81clover-mcp (hakchi's uistart runs it in the foreground, as the stock
script runs clover-mcp). It reads pkg/current, links /tmp/ccs/pkg (RAM) to that copy, runs the HOME, and always ends by exec'ing Nintendo's
clover-mcp: after "Switch to Nintendo HOME", any failure or a missing package, Nintendo's menu runs for the rest of that power-on. hakchi's
pack_install writes the file into its overlay (/var/lib/hakchi/rootfs) and its uninstall list (/var/lib/hakchi/hmod/uninstall-ccs-home);
pack_uninstall removes both, and the stock script is in effect again from the next start.

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

PLAN_SCHEMA = "ccs-home-install-plan/2"
ROOT_DIR = "/var/lib/ccs"; PKG_DIR = ROOT_DIR + "/pkg"; DATA_DIR = ROOT_DIR + "/data"; INST_DIR = ROOT_DIR + "/install"; POINTER = PKG_DIR + "/current"
BIN_DIR = ROOT_DIR + "/bin"   # p3a1's launcher place: never written now (a file there is someone else's and is kept)
HOOK_NAME = "ccs-home"; HOOK_FILE = "/etc/init.d/S81clover-mcp"
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
def module_sha() -> str: return _sha(S81_OVERRIDE.encode())
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

def build_plan(package_dir: str) -> dict:
    """the immutable plan for one console package (install layout): its files with their SHA-256 and modes (validated), the start-up module,
    the room it needs, the identity its saves will carry; named by the SHA-256 of its own content"""
    man_b = _read(os.path.join(package_dir, "MANIFEST.json")); man = json.loads(man_b)
    if man.get("schema") != "ccs-console-package/1": raise PlanError("not a Classic Cart Studio console package")
    if man.get("layout") != "install": raise PlanError(f"a {man.get('layout')!r} package is not for installing (the RAM test's package never is)")
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
    version = _sha(man_b)[:16]
    plan = {"schema": PLAN_SCHEMA, "version": version, "label": man.get("label"), "games": man.get("games"), "playable": man.get("playable"), "startup": man.get("startup"),
            "player": man.get("player"), "manifest_sha256": _sha(man_b), "state_identity": dict(STATE_IDENTITY),
            "paths": {"root": ROOT_DIR, "package": f"{PKG_DIR}/{version}.<attempt>", "pointer": POINTER, "data": DATA_DIR, "records": INST_DIR},
            "files": files, "bytes": total, "needs_free_bytes": total + RESERVE,
            "module": {"name": HOOK_NAME, "files": {HOOK_FILE: {"sha256": module_sha(), "mode": "0o755"}},
                       "where": f"hakchi's pack_install writes {HOOK_FILE} into {OVERLAY} and its uninstall list {HMOD_DIR}/uninstall-{HOOK_NAME}"},
            "requires": {"kernel": HAKCHI_KERNEL, "note": "hakchi's kernel must already be installed (its first installation is a separate step with the pinned Hakchi2 CE, under G1)"},
            "never_written": ["/usr (the stock games and system)", "/var/saves (the stock saves)",
                              "/etc and /var/lib/hakchi other than through hakchi's own pack_install / pack_uninstall of the ccs-home module "
                              f"(which write {OVERLAY}{HOOK_FILE} and {HMOD_DIR}/uninstall-{HOOK_NAME}, and nothing else there)",
                              "/bin, /sbin, /lib, /boot, /dev, /proc, /sys, /newroot",
                              f"anything under {ROOT_DIR} that is not this program's by its records (kept as it is and reported)"],
            "steps": ["preflight", "record", "stage", "readback", "switch", "module", "verify", "tidy", "finish"]}
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
    def pack_install(self, name: str, files: dict):
        def act():   # b0030_package: copy into $rootfs, then "rm -f" for every file and "rmdir --ignore-fail-on-non-empty" for every folder, deepest first
            lst, dirs = [], set()
            for path, (b, mode) in files.items():
                q = self._ov(path); os.makedirs(os.path.dirname(q), exist_ok=True)
                with open(q, "wb") as f: f.write(b)
                lst.append(f'rm -f "$rootfs/.{path}"'); parts = path.strip("/").split("/")[:-1]
                dirs |= {"/" + "/".join(parts[:i]) for i in range(1, len(parts) + 1)}
            lst += [f'rmdir --ignore-fail-on-non-empty "$rootfs/.{d}"' for d in sorted(dirs, key=lambda d: -d.count("/"))]
            u = self._ulist(name); os.makedirs(os.path.dirname(u), exist_ok=True)
            with open(u, "w", newline="\n") as f: f.write("\n# auto-generated\n" + "\n".join(lst) + "\n")
            self.writes.append(f"hakchi:pack_install {name}")
        return self._change(f"hakchi pack_install {name}", act)
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
    def pack_uninstall(self, name: str):
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
        if m and rec.get("schema") == "ccs-owned-module/1" and str(rec.get("sha256", "")).startswith(m.group(1)): out["modules"][m.group(1)] = rec | {"_record": path}; continue
        m = re.match(r"^attempt-([0-9a-f]{8})\.json$", name)
        if m and rec.get("schema") == "ccs-install-attempt/1" and rec.get("attempt") == m.group(1): out["attempts"][m.group(1)] = rec | {"_record": path}; continue
        out["problems"].append(f"{path} is not one of this program's records")
    return out

def owned_module_shas(recs: dict) -> set: return {r["sha256"] for r in recs["modules"].values()} | {module_sha()}

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
        mod = c.pack_files(HOOK_NAME); mine = owned_module_shas(recs); module_state = "absent"
        if mod:
            if set(mod) != {HOOK_FILE} or mod[HOOK_FILE] not in mine: raise Refused("preflight", "a ccs-home module is installed that this program did not make (or that was changed); nothing was changed")
            module_state = "current" if mod[HOOK_FILE] == module_sha() else "older"
        elif c.overlay_sha256(HOOK_FILE) is not None: raise Refused("preflight", f"another hakchi module already replaces {HOOK_FILE}; nothing was changed")
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
        if cur and recs["versions"][cur].get("plan") == self.plan["hash"] and module_state == "current":
            st = copy_state(c, cur, recs["versions"][cur]); same = not st["changed"] and not st["missing"]
        need = 0 if same else self.plan["needs_free_bytes"]; free = c.free_bytes(ROOT_DIR)
        if not same and free < need: raise Refused("preflight", f"not enough room on the console: {free / 1e6:.1f} MB free, {need / 1e6:.1f} MB needed")
        unknown = [f"{PKG_DIR}/{x}" for x in c.listdir(PKG_DIR) if x != "current" and x not in recs["versions"]]
        unknown += [f"{ROOT_DIR}/{x}" for x in c.listdir(ROOT_DIR) if x not in ("pkg", "data", "install")]
        older = [v for v in recs["versions"] if v != cur]
        return {"identity": i, "device": dev, "free_bytes": free, "needs_bytes": need, "in_use": cur, "pointer_sha256": psha, "already": bool(same), "module": module_state, "data": data,
                "kept_unknown": unknown, "earlier_copies": older, "records": len(recs["files"])}

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

    def _install(self, pf):
        c = self.c; plan = self.plan
        cur = pf["in_use"]; mod_prev = c.overlay_read(HOOK_FILE) if pf["module"] == "older" else None
        if pf["module"] == "older" and (mod_prev is None or _sha(mod_prev) != c.overlay_sha256(HOOK_FILE)): raise InstallStopped("preflight", "the start-up module in use could not be read in full; nothing was changed")
        self.prior = {"pointer": cur, "pointer_sha256": pf["pointer_sha256"], "module": pf["module"], "module_sha256": c.overlay_sha256(HOOK_FILE) if pf["module"] != "absent" else None}
        rec = {"schema": "ccs-install-attempt/1", "attempt": self.attempt, "plan": plan["hash"], "version": self.v, "copy": self.copy, "kind": "install",
               "device": {k: pf["device"].get(k) for k in ("hwid", "system", "kernel", "hakchi")}, "started": _now(),
               "prior": {"pointer": cur, "module": pf["module"], "module_sha256": self.prior["module_sha256"], "module_b64": base64.b64encode(mod_prev).decode() if mod_prev else None}}
        rec_b = _record_bytes(rec); self.attempt_path = f"{INST_DIR}/attempt-{self.attempt}.json"
        # 1. this program's folders, then the attempt record (with what was there before), persistent before anything else changes
        for d in (ROOT_DIR, INST_DIR, PKG_DIR):
            if c.stat(d)["type"] == "none": self._do("mkdir", lambda d=d: c.mkdir(d), {"op": "rmdir-if-empty", "path": d}, path=d)
        self._do("record-attempt", lambda: c.put(self.attempt_path, rec_b), {"op": "remove", "path": self.attempt_path, "sha256": _sha(rec_b)}, path=self.attempt_path)
        self._sync(why="the attempt record and what it keeps of the earlier state, before anything else changes")
        # 2. the copy's record, then the copy (a new folder of this attempt, beside the one in use)
        files = {f["path"]: f["sha256"] for f in plan["files"]}
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
        # 6. the start-up module (when absent or older): its record persistent first
        if pf["module"] != "current":
            same_console(c, self.device, "module")
            mb = S81_OVERRIDE.encode(); mrec_b = _record_bytes({"schema": "ccs-owned-module/1", "name": HOOK_NAME, "path": HOOK_FILE, "sha256": _sha(mb)})
            mpath = f"{INST_DIR}/module-{_sha(mb)[:16]}.json"
            if c.stat(mpath)["type"] == "none": self._do("record-module", lambda: c.put(mpath, mrec_b), {"op": "remove", "path": mpath, "sha256": _sha(mrec_b)}, path=mpath)
            self._sync(why="the module record, before the module")
            self._do("module", lambda: c.pack_install(HOOK_NAME, {HOOK_FILE: (mb, "0o755")}),
                     {"op": "module", "prior": pf["module"], "prior_b64": base64.b64encode(mod_prev).decode() if mod_prev else None}, module=HOOK_NAME, prior=pf["module"])
            self._progress("module", ok=True)
        # 7. verify everything the console will start from, as the storage holds it
        self._sync(drop=True, why="before the final verification")
        bad = [rel for rel, h in files.items() if c.sha256(f"{self.target}/{rel}") != h]; pb = c.read(POINTER, 64); mod = c.pack_files(HOOK_NAME)
        ok = not bad and pb == pointer_bytes(self.copy) and mod == {HOOK_FILE: module_sha()} and c.overlay_sha256(HOOK_FILE) == module_sha()
        self.log(op="verify", ok=ok, mismatches=bad[:20], pointer=(pb or b"").decode(errors="replace").strip(), module=mod); self._progress("verify", ok=ok)
        if not ok: raise InstallStopped("verify", "after the switch, the copy, the pointer or the start-up module is not as planned")
        # installed: from here nothing is undone. 8. tidy what this program owns of the earlier copies and module records
        self.undo = []; self.log(op="installed"); tidy = []
        same_console(c, self.device, "tidy")
        recs = read_records(c)
        for v, r in sorted(recs["versions"].items()):
            if v == self.copy: continue
            try: tidy.append(remove_copy(c, v, r, journal=lambda **k: self.log(**k)))
            except (ConnectionError, OSError) as e: tidy.append({"copy": v, "removed": False, "why": str(e)})
        for k16, r in recs["modules"].items():
            if r["sha256"] != module_sha():
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
        self.done = {"ok": True, "result": "installed", "version": self.v, "copy": self.copy, "attempt": self.attempt, "written_bytes": getattr(c, "written_bytes", None), "preflight": pf,
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
                    now = c.pack_files(HOOK_NAME); ours = {HOOK_FILE: module_sha()}
                    if u["prior"] == "absent":
                        if now == ours or (not now and c.overlay_sha256(HOOK_FILE) == module_sha()): c.pack_uninstall(HOOK_NAME)
                        elif now or c.overlay_sha256(HOOK_FILE) is not None: problems.append("the start-up module is not the one this attempt installed; it is left as it is")
                    else:
                        pb = base64.b64decode(u["prior_b64"]) if u.get("prior_b64") else None
                        if pb is not None and now == {HOOK_FILE: _sha(pb)}: pass
                        elif pb is not None and now == ours: c.pack_install(HOOK_NAME, {HOOK_FILE: (pb, "0o755")})
                        else: problems.append("the start-up module is neither the earlier one nor this attempt's; it is left as it is")
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
        mod = c.pack_files(HOOK_NAME)
        checks["module"] = (mod == {} and c.overlay_sha256(HOOK_FILE) is None) if prior["module"] == "absent" else (mod.get(HOOK_FILE) == prior["module_sha256"])
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
        pr = rec.get("prior") or {}; prior = {"pointer": pr.get("pointer"), "module": pr.get("module"), "module_sha256": pr.get("module_sha256")}
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
    mod = c.pack_files(HOOK_NAME); module = "absent"
    if mod: module = ("current" if mod == {HOOK_FILE: module_sha()} else "this program's, older") if set(mod) == {HOOK_FILE} and mod[HOOK_FILE] in owned_module_shas(recs) else "not this program's"
    elif c.overlay_sha256(HOOK_FILE) is not None: module = "another module replaces the start script"
    copy = None
    if in_use:
        s = copy_state(c, in_use, recs["versions"][in_use]); copy = {"copy": in_use, "plan": recs["versions"][in_use].get("plan"), "files": len(s["ours"]), "changed": s["changed"][:20], "missing": s["missing"][:20]}
    data = {"present": c.stat(DATA_DIR)["type"] == "dir"}
    return {"ok": True, "device": dev, "in_use": copy, "pointer": "this program's" if in_use else ("absent" if pst["type"] == "none" else "not this program's"), "module": module,
            "attempts": sorted(recs["attempts"]), "copies": sorted(recs["versions"]), "record_problems": recs["problems"][:10], "data": data, "free_bytes": c.free_bytes(ROOT_DIR)}

def verify_installed(c, plan: dict, device: str | None = None) -> dict:
    """the console starts exactly this plan's copy: pointer, every file of the copy (after a sync with the page cache dropped, as the storage
    holds them) and the start-up module. Reads only (sync writes nothing)"""
    if getattr(c, "simulated", False) is not True: gate.require("inspect", "read a real console")
    try: dev = check_device(c.identity(fresh=True), "verify")
    except Refused as e: return {"ok": False, "verified": False, "why": e.why}
    if device and dev["hwid"] != device: return {"ok": False, "verified": False, "why": "this is not the console that was checked before; check it again"}
    recs = read_records(c); pb = c.read(POINTER, 64); pv = (pb or b"").decode("ascii", "replace").strip()
    rec = recs["versions"].get(pv) if pb is not None and pb == pointer_bytes(pv) else None
    if not rec or rec.get("plan") != plan["hash"]: return {"ok": True, "verified": False, "why": "the console does not start this plan's copy", "in_use": pv if rec else None}
    s = getattr(c, "sync", None)
    if s: s(drop_caches=True)
    files = {f["path"]: f["sha256"] for f in plan["files"]}; base = f"{PKG_DIR}/{pv}"
    bad = [rel for rel, h in files.items() if c.sha256(f"{base}/{rel}") != h]; mod = c.pack_files(HOOK_NAME)
    ok = not bad and mod == {HOOK_FILE: module_sha()} and c.overlay_sha256(HOOK_FILE) == module_sha()
    return {"ok": True, "verified": ok, "copy": pv, "mismatches": bad[:20], "module": "current" if mod == {HOOK_FILE: module_sha()} else "not as planned", "attempts": sorted(recs["attempts"]), "device": dev["hwid"]}

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
    mod = c.pack_files(HOOK_NAME); mine = owned_module_shas(recs)
    if mod and (set(mod) != {HOOK_FILE} or mod[HOOK_FILE] not in mine): return {"ok": False, "why": "the ccs-home module was not made by this program or was changed; it is not removed and nothing else is"}
    actions, remove, keep = [], [], []
    if mod:
        actions.append({"op": "module-uninstall", "sha256": mod[HOOK_FILE]})
        remove.append(f"the start-up module {HOOK_NAME} ({HOOK_FILE} in hakchi's overlay, through hakchi's pack_uninstall): the console starts in Nintendo's HOME again")
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
    keep += [f"{ROOT_DIR}/{x} (not this program's)" for x in c.listdir(ROOT_DIR) if x not in ("pkg", "data", "install")]
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
    return {"ok": True, "remove": remove, "keep": keep, "module": bool(mod), "copies": sorted(recs["versions"]), "pointer": {"owned": owned_ptr, "value": pv if owned_ptr else None},
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
            if op == "module-uninstall":
                if c.pack_files(HOOK_NAME) != {HOOK_FILE: a["sha256"]}: problems.append("the start-up module changed since the review; it is kept and nothing more is changed"); break
                log(op=op, state="intent"); c.pack_uninstall(HOOK_NAME); sync(); removed.append(f"module {HOOK_NAME}")
            elif op == "remove":
                if c.stat(path)["type"] == "none": continue
                log(op=op, path=path, sha256=a["sha256"], state="intent")
                if c.remove(path, a["sha256"]): removed.append(path)
                else: kept.append(f"{path} (changed since the review, or no longer a plain file)")
            elif op == "rmdir-if-empty":
                if c.stat(path)["type"] == "dir" and not c.listdir(path): log(op=op, path=path, state="intent"); c.rmdir(path)
            elif op == "remove-record":
                if a.get("when_gone") and c.stat(a["when_gone"])["type"] != "none": kept.append(f"{path} (its copy keeps items, so its record stays)"); continue
                if a.get("when_module_gone") and (c.pack_files(HOOK_NAME) or c.overlay_sha256(HOOK_FILE) is not None): kept.append(f"{path} (the module is still there)"); continue
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
        mod_after = c.pack_files(HOOK_NAME); pa = c.stat(POINTER)["type"]
        ptr_ok = (pa == "none") if plan["pointer"]["owned"] else ((c.sha256(POINTER) if pa == "file" else None) == plan["pointer_sha256"])
        gone = [a["path"] for a in plan["actions"] if a["op"] == "remove" and c.stat(a["path"])["type"] != "none"]
        checks = {"module_removed": not mod_after and c.overlay_sha256(HOOK_FILE) is None, "pointer": ptr_ok, "planned_removals_left": len(gone)}
    except (ConnectionError, OSError) as e: checks = {"unreadable": str(e)}; problems.append(f"the result could not be read back: {e}")
    complete = not problems and not kept and checks.get("module_removed") is True and checks.get("pointer") is True and checks.get("planned_removals_left") == 0
    data = "kept" if keep_data else ("removed" if c.stat(DATA_DIR)["type"] == "none" else "partly kept")
    log(op="end", result="uninstalled" if complete else "incomplete", problems=problems[:10], kept=kept[:50])
    return {"ok": complete, "result": "uninstalled" if complete else "incomplete", "removed": removed, "kept_changed": kept, "kept_unknown": sorted(x for x in plan["keep"] if "not this program's" in x or "not followed" in x),
            "problems": problems, "data": data, "module": "removed" if plan["module"] else "absent", "verified": checks, "changed": bool(removed),
            "after_restart": ("The console starts in Nintendo's HOME with its own games and saves; Classic Cart Studio's HOME no longer starts." if checks.get("module_removed") else
                              "Not everything was removed: the console may still start Classic Cart Studio's HOME. Review the removal again.")}

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
    "other-module": _READY | {"hwid": "sim-0004", "free_bytes": 200_000_000, "foreign_s81": True},
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
    return FakeConsole(root, durable=durable)

def stock_digest(root: str) -> dict:   # the stand-in's stock and foreign sample as it is now (compared before and after)
    out = {}
    for rel in STOCK_SAMPLE:
        q = os.path.join(root, *rel.split("/")); out[rel] = _sha(_read(q)) if os.path.isfile(q) else None
    return out
