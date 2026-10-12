"""Platform adapter for the simulator core: starts ccs-sim-core as a child process on one of three execution paths and
exposes the line protocol as Python calls and event lists.

  path "windows"  the native Windows build (tools run from Windows Python)
  path "linux"    the x86-64 Linux build inside WSL (wsl.exe -d <distro>)
  path "arm"      the 32-bit ARM build inside WSL under qemu-arm user mode (real ARMv7 code; see ARCHITECTURE.md)

The adapter also plays the "console services" role the core deliberately lacks: when the core asks to launch an entry it
starts the DUMMY game stub (the same core binary with --game-stub, on the same execution path) and reports its exit code
back. It never runs anything named in a fixture, never contacts a device, never opens a socket.

Safety guard: refuses to start if the environment or arguments mention a device endpoint (see FORBIDDEN).
"""
from __future__ import annotations
import json, os, subprocess, sys, time, threading, queue, re

HERE = os.path.dirname(os.path.abspath(__file__)); SIM = os.path.dirname(HERE)
BUILD = os.environ.get("CCS_SIM_BUILD_WIN", r"C:\NES\work\console-simulator\build")
BUILD_WSL = os.environ.get("CCS_SIM_BUILD", "/mnt/c/NES/work/console-simulator/build")
ARTIFACTS = os.environ.get("CCS_SIM_ARTIFACTS_WIN", r"C:\NES\work\console-simulator\artifacts")
DISTRO = os.environ.get("CCS_SIM_DISTRO", "Ubuntu")
TOOLCHAIN_WSL = os.environ.get("CCS_SIM_TOOLCHAIN", "$HOME/.cache/ccs-sim/toolchain")
FORBIDDEN = ("169.254.13.37", "clovershell", "fel", "hakchi", "ssh://", "usb:", "/dev/")

def wsl_path(win: str) -> str:
    """C:\\x\\y -> /mnt/c/x/y (only for paths the core must open from inside WSL)"""
    p = win.replace("\\", "/")
    m = re.match(r"^([A-Za-z]):/(.*)$", p)
    return f"/mnt/{m.group(1).lower()}/{m.group(2)}" if m else p

def guard(*texts: str):
    for t in texts:
        low = (t or "").lower()
        for f in FORBIDDEN:
            if f in low: raise RuntimeError(f"refusing: simulator input mentions a device endpoint or console tool ({f!r})")
    for k, v in os.environ.items():
        if k.startswith("CCS_SIM_") and any(f in v.lower() for f in FORBIDDEN): raise RuntimeError(f"refusing: environment {k} mentions a device endpoint")

class Core:
    """One core process on one execution path. Commands are sent as lines; events are read on a thread into a queue."""
    def __init__(self, path: str = "windows", args: list[str] | None = None, timeout: float = 10.0):
        self.path = path; self.args = list(args or []); self.timeout = timeout; self.events: list[dict] = []; self.pending: list[dict] = []; self.q: queue.Queue = queue.Queue(); self.proc = None; self.stderr = ""
        guard(" ".join(self.args))
        self.cmd = self.command(path, self.args)
        self.proc = subprocess.Popen(self.cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, bufsize=0)
        self.t = threading.Thread(target=self._reader, daemon=True); self.t.start()
        self.te = threading.Thread(target=self._stderr, daemon=True); self.te.start()

    @staticmethod
    def exe(path: str) -> str:
        if path == "windows": return os.path.join(BUILD, "windows-x86_64", "ccs-sim-core.exe")
        if path == "linux": return f"{BUILD_WSL}/linux-x86_64/ccs-sim-core"
        if path == "arm": return f"{BUILD_WSL}/arm-musl/ccs-sim-core"
        raise ValueError(path)

    @classmethod
    def command(cls, path: str, args: list[str]) -> list[str]:
        if path == "windows": return [cls.exe(path)] + args
        if path == "linux": return ["wsl.exe", "-d", DISTRO, "--", cls.exe(path)] + args
        if path == "arm": return ["wsl.exe", "-d", DISTRO, "--", "bash", "-c", f'exec "{TOOLCHAIN_WSL}/deb/usr/bin/qemu-arm" "{cls.exe(path)}" ' + " ".join(f'"{a}"' for a in args)]
        raise ValueError(path)

    def _reader(self):
        for raw in self.proc.stdout:
            line = raw.decode("utf-8", "replace").strip()
            if not line or not line.startswith("{"): continue
            try: ev = json.loads(line)
            except json.JSONDecodeError: ev = {"ev": "unparsable", "raw": line}
            self.events.append(ev); self.q.put(ev)
        self.q.put({"ev": "__eof__"})

    def _stderr(self):
        try: self.stderr = self.proc.stderr.read().decode("utf-8", "replace")
        except Exception: pass

    def fixture_arg(self, win_path: str) -> str: return win_path if self.path == "windows" else wsl_path(win_path)

    def send(self, line: str):
        guard(line)
        try: self.proc.stdin.write((line + "\n").encode("utf-8")); self.proc.stdin.flush()
        except (BrokenPipeError, OSError): pass

    def _next(self, timeout: float) -> dict | None:
        if self.pending: return self.pending.pop(0)
        try: return self.q.get(timeout=timeout)
        except queue.Empty: return None

    def wait_for(self, name: str | tuple, timeout: float | None = None) -> dict | None:
        """return the next event whose ev is in `name`; events passed over stay available to drain() (nothing is dropped)"""
        names = (name,) if isinstance(name, str) else tuple(name); end = time.time() + (timeout or self.timeout); skipped = []
        try:
            while True:
                left = end - time.time()
                if left <= 0: return None
                ev = self._next(left)
                if ev is None or ev["ev"] == "__eof__": return None
                if ev["ev"] in names: return ev
                skipped.append(ev)
        finally: self.pending = skipped + self.pending

    def drain(self, settle: float = 0.15) -> list[dict]:
        """collect the events passed over so far plus whatever arrives until none arrive for `settle` seconds"""
        out = list(self.pending); self.pending = []
        while True:
            try: ev = self.q.get(timeout=settle)
            except queue.Empty: return out
            if ev["ev"] == "__eof__": return out
            out.append(ev)

    def ready(self, timeout: float | None = None) -> dict | None: return self.wait_for("ready", timeout)
    def load(self, win_fixture: str) -> dict | None: self.send("load " + self.fixture_arg(win_fixture)); return self.wait_for(("fixture_loaded", "fixture_rejected"))
    def press(self, b: str): self.send(f"input press {b}")
    def release(self, b: str): self.send(f"input release {b}")
    def tick(self, ms: int) -> dict | None: self.send(f"tick {ms}"); return self.wait_for("tick")
    def frame(self, win_out: str) -> dict | None: self.send("frame " + self.fixture_arg(win_out)); return self.wait_for(("frame_written", "frame_failed"))
    def stats(self) -> dict | None: self.send("stats"); return self.wait_for("stats")
    def shutdown(self, timeout: float = 5.0) -> int | None:
        self.send("shutdown")
        try: self.proc.wait(timeout=timeout)
        except subprocess.TimeoutExpired: self.proc.kill(); self.proc.wait(timeout=5)
        return self.proc.returncode
    def kill(self):
        try: self.proc.kill(); self.proc.wait(timeout=5)
        except Exception: pass
    def exit_code(self) -> int | None: return self.proc.poll()

    def launch_cycle(self, stub_exit: int = 0, stub_delay_ms: int = 30, stub_crash: bool = False, stub_timeout: float = 10.0) -> dict:
        """ask the core to launch the focused entry, run the DUMMY stub on the same path, report back; returns a record"""
        self.send("launch"); ev = self.wait_for(("launch_requested", "launch_refused"))
        rec = {"request": ev}
        if not ev or ev["ev"] != "launch_requested": return rec
        self.wait_for("state")   # suspended
        stub_args = ["--game-stub", f"--exit-code={stub_exit}", f"--delay-ms={stub_delay_ms}"] + (["--crash"] if stub_crash else [])
        t0 = time.time()
        try:
            r = subprocess.run(self.command(self.path, stub_args), capture_output=True, timeout=stub_timeout)
            rec["stub"] = {"exit": r.returncode, "ms": int((time.time() - t0) * 1000), "events": [json.loads(l) for l in r.stdout.decode("utf-8", "replace").splitlines() if l.startswith("{")]}
            if r.returncode == 0 or (r.returncode == stub_exit and not stub_crash): self.send(f"game-exit {r.returncode}"); rec["result"] = self.wait_for(("resumed", "launch_failed"))
            else: self.send(f"game-failed stub exited with {r.returncode}"); rec["result"] = self.wait_for(("resumed", "launch_failed"))
        except subprocess.TimeoutExpired:
            rec["stub"] = {"exit": None, "timeout": True}; self.send("game-failed stub timeout"); rec["result"] = self.wait_for(("resumed", "launch_failed"))
        self.wait_for("state")   # ready
        return rec

def ppm_to_png(ppm: str, png: str) -> tuple[int, int]:
    from PIL import Image
    im = Image.open(ppm); im.save(png); return im.size

if __name__ == "__main__":
    path = sys.argv[1] if len(sys.argv) > 1 else "windows"
    c = Core(path); print("ready:", c.ready()); print("load:", c.load(os.path.join(SIM, "fixtures", "mixed.json")))
    c.press("down"); c.tick(500); c.release("down"); print("launch:", json.dumps(c.launch_cycle(), default=str)[:300]); print("stats:", c.stats()); print("exit", c.shutdown())
