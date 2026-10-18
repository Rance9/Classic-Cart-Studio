"""Transports for the console engine.

Only one transport class exists in this milestone: `FakeTransport`, an in-memory NES Classic that answers the exact
command surface the pinned Hakchi2 CE 3.9.3 PC code uses after a shell connects (hakchi.cs Shell_OnConnected,
MemoryStats.Refresh, SyncTask, ModTasks, MembootTasks). It never opens a USB device, a socket or a file outside the
staging root. `open_transport()` is the last boundary: real transport kinds are refused by the gate in code, and no real
transport is implemented here, so even a caller that reached this function with "ssh", "clovershell" or "fel" gets
`GateClosed` (and would get `NotImplementedError` after the gate milestone until the derived worker exists).

The fake keeps a tiny device filesystem (path -> size) for the game storage so that `CalculateRemoteDiff`-style listing,
transfer and independent read-back comparison behave like the real sequence, including deletion of files that are not in
the local set, which is why foreign content must be added to the local set by the plan (architecture section 9).
"""
from __future__ import annotations
import re, time
from . import gate
from .profiles import get as get_profile

REAL_KINDS = ("ssh", "clovershell", "fel")


class TransportError(RuntimeError): pass
class Disconnected(TransportError): pass


class FakeTransport:
    kind = "fake"
    simulated = True

    def __init__(self, profile: str | dict):
        self.p = get_profile(profile) if isinstance(profile, str) else profile
        self.online = False
        self.opened_at = None
        self.commands: list[str] = []            # journal of every command the engine issued (for tests and diagnostics)
        self.stage = None                        # set by the engine so failure injection can key on it
        self.transferred = 0
        self.fs: dict[str, int] = {}             # game storage: relpath under the sync root -> size
        for code, g in (self.p.get("games") or {}).items():
            for rel, size in g["files"].items(): self.fs[rel] = size
        self.cfg = dict(self.p.get("config") or {})
        self.modules = list(self.p.get("modules") or [])

    # ---- connection ----
    def usb_present(self) -> dict | None: return self.p.get("usb")
    def driver(self) -> str: return self.p.get("driver", "ok")
    def fel_board(self) -> int: return int(self.p.get("fel_board") or 0)

    def open(self):
        """Connect the shell the profile offers. A FEL-only or absent device has no shell."""
        if self.p.get("shell") not in ("ssh", "clovershell"): raise Disconnected("no shell: the console offers no clovershell or SSH connection in this state")
        self.online = True; self.opened_at = time.time(); return self

    def close(self): self.online = False

    def _fail_check(self, stage: str):
        f = self.p.get("fail")
        if f and f.get("at") == stage and f.get("kind") == "disconnect" and f.get("before"):
            self.online = False; raise Disconnected(f"device went offline before {stage}")

    # ---- the command surface the pinned PC code uses ----
    def execute(self, command: str) -> tuple[int, str]:
        """Return (exit code, stdout) for one shell command. Unknown commands fail like a shell would."""
        if not self.online: raise Disconnected("NES Mini is offline")
        self.commands.append(command)
        c = command.strip()
        if c == "hakchi ismdPartitioning": return (0 if self.p.get("md_partitioning") else 1, "")
        if c.startswith("source /hakchi/config;"): return (0 if self.p.get("minimal_memboot") else 1, "")
        if c == "hakchi hwid": return 0, self.p.get("hwid") or ""
        if c.startswith("[ -f /var/version ]"): return (0, "yes") if self.p.get("versions") else (1, "")
        if c.startswith("source /var/version"):
            v = self.p.get("versions") or {}; return 0, f"{v.get('bootVersion','')} {v.get('kernelVersion','')} {v.get('hakchiVersion','')}"
        if c.startswith("hakchi eval 'echo \"$sftype-$sfregion\"'"): return 0, self.p.get("system_code") or "unknown"
        if c == "hakchi currentFirmware": return 0, self.p.get("firmware") or "_nand_"
        if c == "hakchi findGameSyncStorage": return 0, "/var/lib/hakchi/games"
        if c == "hakchi get gamepath": return 0, "/usr/share/games"
        if c == "hakchi get rootfs": return 0, "/var/lib/hakchi/rootfs"
        if c == "hakchi get squashfs": return 0, "/var/squashfs"
        if c.startswith("cat /etc/preinit.d/p0000_config"): return 0, "".join(f"cfg_{k}='{v}'\n" for k, v in self.cfg.items())
        if c == "hakchi haveBackup2": return (0 if self.p.get("backup2") else 1, "")
        if c == "hakchi pack_list": return 0, "\n".join(self.modules)
        if c.startswith("df "):
            st = self.p["storage"]; used = st["used_other"] + st["saves"] + self.games_bytes(); total = st["total"]
            return 0, f"{total // 1024} | {used // 1024} | {max(0, total - used) // 1024}"
        if c.startswith("du -s"): return 0, str(self.p["storage"]["saves"] // 1024)
        if c.startswith("du -d 1"):
            tops: dict[str, int] = {}
            for rel, size in self.fs.items(): tops[rel.split("/")[0]] = tops.get(rel.split("/")[0], 0) + size
            return 0, "\n".join(f"/var/lib/hakchi/games/{k} | {v // 1024}" for k, v in sorted(tops.items()))
        if c.startswith("mkdir -p") and "find . -type f" in c:
            return 0, "\n".join(f"./{rel} {size} 2024-01-01 00:00:00.000000000 +0000" for rel, size in sorted(self.fs.items()))
        if c == "hakchi eval 'umount \"$gamepath\"'" or c.startswith("hakchi overmount_games") or c == "uistop" or c == "uistart": return 0, ""
        if c.startswith("hakchi mount_base") or c.startswith("hakchi umount_base"): return 0, ""
        if c.startswith("hakchi packs_install"): return 0, ""
        if c.startswith("hakchi pack_uninstall"): return 0, ""
        if c.startswith("hakchi eval") or c.startswith("find ") or c.startswith("for f in") or c.startswith("rm "): return 0, ""
        if c.startswith("ls /user-recovery.flag"): return 1, ""
        if c.startswith("sync; umount -ar; reboot"): self.online = False; return 0, ""
        return 127, f"sh: {c.split(' ')[0]}: not found"

    def execute_simple(self, command: str) -> str: return self.execute(command)[1].strip()

    # ---- transfer, modules and read-back (the parts SyncTask/ModTasks do with streams) ----
    def delete_files(self, rels: list[str]):
        for r in rels: self.fs.pop(r, None)

    def transfer(self, files: dict[str, int], on_progress=None):
        """Upload the given relpath -> size set (tar -x or FTP upstream). Honors injected disconnects."""
        if not self.online: raise Disconnected("NES Mini is offline")
        self._fail_check("transfer")
        f = self.p.get("fail"); total = sum(files.values()) or 1; done = 0
        for rel, size in sorted(files.items()):
            if f and f.get("at") == "transfer" and f.get("kind") == "disconnect" and not f.get("before") and done / total >= float(f.get("fraction", 0.5)):
                self.online = False; raise Disconnected(f"device went offline during the transfer at {done} of {total} bytes")
            self.fs[rel] = size; done += size; self.transferred += size
            if on_progress: on_progress(done, total, rel)

    def install_modules(self, names: list[str]):
        if not self.online: raise Disconnected("NES Mini is offline")
        for n in names:
            if n not in self.modules: self.modules.append(n)
        f = self.p.get("fail")
        if f and f.get("at") == "modules" and f.get("kind") == "error" and f.get("after"): raise TransportError("hakchi packs_install returned exit code 1 (simulated failure after the module stage)")

    def read_back(self) -> dict[str, int]:
        """Independent listing of the game storage after an operation (what a verify step reads)."""
        if not self.online: raise Disconnected("NES Mini is offline")
        f = self.p.get("fail")
        if f and f.get("at") == "verify" and f.get("kind") == "timeout": raise TransportError("clovershell read timeout during the read-back (simulated)")
        return dict(self.fs)

    def games_bytes(self) -> int: return sum(self.fs.values())


def open_transport(kind: str, target=None):
    """The last boundary. Only the fake transport can be opened; real kinds are refused by Gate G1 in code."""
    if kind == "fake": return FakeTransport(target or "no-device")
    if kind in REAL_KINDS:
        gate.require("inspect" if kind in ("ssh", "clovershell") else "install", f"open {kind} transport")
        raise NotImplementedError(f"the {kind} transport is provided by the derived worker, which is not part of this build")
    raise ValueError(f"unknown transport kind {kind!r}")
