"""The installer's console, reached through the console transport worker (tools/console-worker: ccs-console-worker.exe, GPL-3.0-or-later, derived
from Hakchi2 CE 3.9.3's clovershell and SSH code). The same operations as the stand-in (ccs_install.FakeConsole), sent as the worker's
typed commands over its line protocol (version 1): the worker composes and quotes every console command itself, checks every path against
its own roots (writes only under /var/lib/ccs and /tmp/ccs) and never takes shell text, a host or a device from here.

open_worker_console is the only way to a real console and the gate refuses it first (console_engine.gate in this program; the worker's own
Gate.cs refuses "connect" again before any device is looked for). A session keeps the tier it was opened for: one opened to inspect refuses
every change here, before the worker is asked, and the worker asks its own write tier again for each changing command ("write-tier"). The test host (CcsWorker.Tests.exe --serve-busybox, test project only)
serves the same protocol with the console's own BusyBox running in a disposable folder: a stand-in, constructed with simulated=True by the
tests, never by the application. Copyright (C) 2026 Rance9, GPL-3.0-or-later."""
from __future__ import annotations
import base64, hashlib, json, os, subprocess, threading
from . import gate

PROTOCOL = 1
MAX_B64 = 600_000   # a file sent inline (the worker's line limit is 1,000,000 bytes); larger files go by their place in the package
HOOK_OVERLAY = "/var/lib/hakchi/rootfs"

class WorkerError(OSError):
    def __init__(self, code: str, text: str): self.code = code; super().__init__(f"{code}: {text}")

def _raise(code: str, text: str):
    if code == "cancelled": raise ConnectionError("canceled: the transfer stopped before the file was complete; the partial file stays where it was being written")   # (US wording; the worker's own text is not shown)
    if code in ("connection-lost", "timeout", "not-connected"): raise ConnectionError(f"{code}: {text}")
    if code == "exists": raise FileExistsError(text)
    if code == "physical-disabled": raise gate.GateClosed("install", text)
    if code == "path-refused": raise PermissionError(text)
    if code == "pointer-changed":
        from .ccs_install import PointerChanged
        raise PointerChanged(text)
    raise WorkerError(code, text)

class ReadOnlySession(gate.GateClosed):
    """a change asked of a console session opened to inspect: refused here, whatever the gate says (the worker answers physical-disabled too)"""
    def __init__(self, what: str): self.tier = "install"; PermissionError.__init__(self, f"{what}: this console session was opened to inspect, and it never changes the console")

class WorkerConsole:
    """one worker process, one console session; every call is one request and its one answer (progress events are passed to on_progress)"""
    kind = "worker"
    def __init__(self, proc: subprocess.Popen, simulated: bool, kind: str = "worker", package_root: str | None = None, on_progress=None, tier: str = "install"):
        self.p, self.simulated, self.kind = proc, simulated, kind; self.n = 0; self.lock = threading.Lock(); self.on_progress = on_progress
        self.tier = tier   # what the session was opened for: "inspect" (reads only) or "install" (a stand-in from the tests is always simulated)
        self.package_root = os.path.abspath(package_root) if package_root else None   # the folder the worker was given (--package)
        self.writes: list[str] = []; self.written_bytes = 0; self._ident = None
    def call(self, cmd: str, args: dict | None = None, raw: bool = False) -> dict:
        with self.lock:
            self.n += 1; rid = f"r{self.n}"
            self.p.stdin.write((json.dumps({"v": PROTOCOL, "id": rid, "cmd": cmd, "args": args or {}}) + "\n").encode()); self.p.stdin.flush()
            while True:
                line = self.p.stdout.readline()
                if not line: raise ConnectionError("the console worker ended")
                ev = json.loads(line)
                if ev.get("id") != rid: continue
                if ev.get("event") == "progress":
                    if self.on_progress: self.on_progress(ev)
                    continue
                if ev.get("event") == "result": return ev.get("result") or {}
                if raw: return {"error": ev.get("code"), "text": ev.get("text")}
                _raise(str(ev.get("code")), str(ev.get("text")))
    def close(self):
        try: self.call("shutdown")
        except Exception: pass
        for s in (self.p.stdin, self.p.stdout, self.p.stderr):
            try: s and s.close()
            except OSError: pass
        try: self.p.wait(15)
        except subprocess.TimeoutExpired: self.p.kill(); self.p.wait(10)
    # reading
    def identity(self, fresh: bool = False) -> dict:
        """the console's identity as hakchi's own identification reads it (the worker's identify); fresh: read again now, not the session's
        first answer (the installer does at each commit point, so another console answering is noticed)"""
        if self._ident is None or fresh:
            r = self.call("identify"); self._ident = {"kernel": r.get("kernel"), "hakchi": r.get("hakchi_version"), "system": r.get("system"), "hwid": r.get("hwid"),
                                                      "can_interact": r.get("can_interact"), "custom_firmware": r.get("custom_firmware"), "simulated": self.simulated}
        return self._ident
    def free_bytes(self, path: str) -> int: return int(self.call("df", {"path": "/var/lib"})["free_bytes"])
    def stat(self, path: str) -> dict:
        r = self.call("stat", {"path": path}); return {k: r[k] for k in ("type", "size", "target") if r.get(k) is not None}
    def exists(self, path: str) -> bool: return self.stat(path)["type"] != "none"
    def readlink(self, path: str) -> str | None: s = self.stat(path); return s.get("target") if s["type"] == "link" else None
    def listdir(self, path: str) -> list[str]:
        r = self.call("list", {"path": path}, raw=True)
        if r.get("error") == "not-a-folder": return []
        if r.get("error"): _raise(r["error"], r.get("text", ""))
        return sorted(r.get("names") or [])
    def sha256(self, path: str) -> str | None: return (self.call("sha256", {"paths": [path]}).get("hashes") or {}).get(path)
    def read(self, path: str, max_bytes: int = 1_000_000) -> bytes | None:
        r = self.call("read", {"path": path, "max_bytes": min(max_bytes, 1_000_000)}, raw=True)
        if r.get("error") in ("not-a-file", "too-large"): return None
        if r.get("error"): _raise(r["error"], r.get("text", ""))
        return base64.b64decode(r.get("b64") or "")
    def read_chunk(self, path: str, offset: int, length: int) -> dict | None:
        """one bounded piece of a file (the worker's chunked read: whole 4,096-byte blocks, at most 262,144 bytes per answer)"""
        r = self.call("read", {"path": path, "offset": offset, "length": length}, raw=True)
        if r.get("error") == "not-a-file": return None
        if r.get("error"): _raise(r["error"], r.get("text", ""))
        d = base64.b64decode(r.get("b64") or "")
        if len(d) != r.get("size") or r.get("offset") != offset or not isinstance(r.get("file_size"), int): raise WorkerError("bad-answer", f"the worker's answer for {path} at {offset} does not add up")
        return {"data": d, "file_size": r["file_size"]}
    def sync(self, drop_caches: bool = False):
        r = self.call("sync", {"drop_caches": bool(drop_caches)})
        if r.get("synced") is not True: raise WorkerError("not-synced", "the console did not confirm its sync")
    # changing: each asks the write tier first (a real session opened to inspect never writes; the stand-in is the tests' own)
    def _write(self, what: str):
        if self.simulated is True: return
        if self.tier != "install": raise ReadOnlySession(what)
        gate.require("install", what)
    def put(self, path: str, data: bytes, mode: str = "0o644", source: str | None = None) -> str:
        self._write("put"); h = hashlib.sha256(data).hexdigest(); a = {"path": path, "mode": {"0o644": "0644", "0o755": "0755"}[mode], "sha256": h}
        rel = os.path.relpath(os.path.abspath(source), self.package_root).replace(os.sep, "/") if source and self.package_root else None
        if rel and not rel.startswith(".."): a["source"] = rel   # the worker reads it inside its own package folder and checks the hash
        elif len(data) <= MAX_B64: a["b64"] = base64.b64encode(data).decode()
        else: raise WorkerError("too-large", f"{path}: larger files are sent by their place in the package")
        r = self.call("put", a); self.writes.append(path); self.written_bytes += len(data); return r.get("sha256") or ""
    def mkdir(self, path: str): self._write("mkdir"); self.call("mkdir", {"path": path}); self.writes.append(path + "/")
    def rmdir(self, path: str): self._write("rmdir"); self.call("rmdir", {"path": path}); self.writes.append("-" + path + "/")
    def rename(self, a: str, b: str): self._write("rename"); self.call("rename", {"from": a, "to": b}); self.writes.append(b)
    def remove(self, path: str, sha256: str) -> bool:
        self._write("remove"); r = self.call("remove", {"path": path, "sha256": sha256}); ok = bool(r.get("removed"))
        if ok: self.writes.append("-" + path)
        return ok
    def set_pointer(self, path: str, value: str, expect: str | None = None):
        self._write("pointer"); self.call("pointer", {"path": path, "value": value, **({"expect": expect} if expect else {})}); self.writes.append(path)
    # hakchi's own module commands (pack_install / pack_uninstall through the worker; it confirms with the uninstall list)
    def pack_install(self, name: str, files: dict):
        self._write("module-install"); fs = {p: {"b64": base64.b64encode(b).decode(), "mode": {"0o644": "0644", "0o755": "0755"}[m], "sha256": hashlib.sha256(b).hexdigest()} for p, (b, m) in files.items()}
        self.call("module-install", {"name": name, "files": fs}); self.writes.append(f"hakchi:pack_install {name}")
    def pack_files(self, name: str) -> dict:
        r = self.call("module-files", {"name": name}); return dict(r.get("files") or {}) if r.get("present") else {}
    def pack_uninstall(self, name: str): self._write("module-uninstall"); self.call("module-uninstall", {"name": name}); self.writes.append(f"hakchi:pack_uninstall {name}")
    def overlay_sha256(self, path: str) -> str | None: return self.sha256(HOOK_OVERLAY + path)
    def overlay_read(self, path: str) -> bytes | None: return self.read(HOOK_OVERLAY + path, 65536)

def worker_exe() -> str | None:
    """the transport worker installed with the app (console-worker inside the bundle, covered by its file list), never a path from a client"""
    import sys
    base = os.path.join(getattr(sys, "_MEIPASS", os.path.dirname(os.path.abspath(sys.executable))), "console-worker")
    exe = os.path.join(base, "ccs-console-worker.exe")
    return exe if getattr(sys, "frozen", False) and os.path.isfile(exe) else None

def open_worker_console(kind: str, package_dir: str | None = None, tier: str = "install") -> WorkerConsole:
    """a real console through the shipped worker (kind: the transport, "ssh" or "clovershell"; tier: what the session is for, "inspect" or
    "install"): refused by the gate here first, and by the worker's own gate at "connect", before any device is looked for"""
    if tier not in ("inspect", "install"): raise ValueError("tier: inspect or install")
    gate.require(tier, f"open a {kind} console")
    exe = worker_exe()
    if not exe: raise FileNotFoundError("the console worker is not installed with this app")
    p = subprocess.Popen([exe] + (["--package", package_dir] if package_dir else []), stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    c = WorkerConsole(p, simulated=False, kind=kind, package_root=package_dir, tier=tier)
    try:
        if tier == "inspect" and "write-tier" not in (c.call("capabilities").get("features") or []):   # before connect: a worker that cannot keep a session read-only is not used
            raise gate.GateClosed("install", "this console worker does not ask its write tier per command")
        c.call("connect", {"transport": kind})
    except BaseException: c.close(); raise
    return c
