"""CCS Console Workbench native host: build it, run it, and measure it. Copyright (C) 2026 Rance9. GPL-3.0-or-later.

  python test_host.py --work <new folder> [--managed PATH] [--host-out DIR] [--ram-images <recovery images folder>]
                      [--big-mb 0]

1. the managed Workbench: builds tools/console-backup/ccs-backup-utility (Release, with --ram-images) unless --managed names an exe
2. the native host: build_host.sh in WSL for x64 and Arm64 (with --measure) unless --host-out names a finished build
3. static checks of both executables (machine, GUI subsystem, imported DLLs, the ccs_host_get_abi export, the payload resource) and of the
   vtable slot numbers in src/clr_hosting.h against this PC's mscorlib.tlb (tools/dump_vtables.ps1)
4. runs the x64 host from a NEW folder holding only the executable: --host-info, --version, --engine-info (compared with the managed
   executable's own output), the controlled worker (--backup-worker, one request it refuses before any console code runs; no console
   request is ever sent), --parity-selftest (the PAR2 round
   trip through the managed wrapper and the host's ABI), and the WPF window (--simulated-console with test settings; found by its process,
   captured, closed with WM_CLOSE; --no-window leaves it out). Peak working set of every run is read from its process handle after it ends.
   What the host needs from the PC (prerequisite_checks): missing, absent, not-found, unusable and unsupported-Windows cases for both
   editions' rules, a restart waiting, a start failure and a damaged copy, through host_main.c's stricter-only test hooks, every run with
   its output redirected (so none can show a window); the hooks are shown unable to loosen anything. The setup window itself is checked
   inside Windows Sandbox only.
5. while those run, ReadDirectoryChangesW watches %TEMP%, %APPDATA% and %LOCALAPPDATA% (whole trees) and the executable's folder; every
   change is recorded with its time. Windows cannot say which process made a change without administrator tracing, so the method is:
   any change at all beside the executable fails; any change under the watched trees whose path names this product (CCS, Workbench,
   par2, the executable's or the libraries' names) fails; any .exe/.dll/.sys created there during the runs fails and is listed (an
   unrelated program updating itself in that minute would need a rerun); everything else is counted as other programs' activity and a
   sample is listed. Files created and deleted within the runs are caught too (the watch sees the events, not a before/after listing).
6. sizes (payload and its parts, host with and without libpar2, compression the host does NOT apply) and a results JSON in --work.
The Arm64 executable is built and checked statically; this PC is x64 and cannot execute it (stated in the results, never claimed).
Nothing here touches a console, USB or drivers."""
from __future__ import annotations

import argparse, ctypes, ctypes.wintypes as wt, hashlib, json, lzma, os, re, shutil, struct, subprocess, sys, threading, time, zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
CONSOLE_BACKUP = HERE.parent
REPO = CONSOLE_BACKUP.parent.parent
DOTNET = Path(os.environ.get("DOTNET_ROOT") or "dotnet-sdk-10.0.403")   # the pinned SDK (global.json); set DOTNET_ROOT

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
u32 = ctypes.WinDLL("user32", use_last_error=True)
psapi = ctypes.WinDLL("psapi", use_last_error=True)
gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)

FAILS: list[str] = []
RESULTS: dict = {"checks": []}


def check(name: str, ok: bool, detail: str = "") -> bool:
    RESULTS["checks"].append({"name": name, "ok": bool(ok), "detail": detail})
    print(("PASS " if ok else "FAIL ") + name + ("  - " + detail if detail else ""), flush=True)
    if not ok:
        FAILS.append(name)
    return ok


def wsl_path(p: Path) -> str:
    s = str(Path(p).resolve())
    return "/mnt/" + s[0].lower() + s[2:].replace("\\", "/")


def sha256(p: Path) -> str:
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


# ---------------------------------------------------------------- PE reading (enough for the checks)

def pe_facts(path: Path) -> dict:
    d = Path(path).read_bytes()
    pe = struct.unpack_from("<I", d, 0x3C)[0]
    machine, nsec = struct.unpack_from("<HH", d, pe + 4)
    soh = struct.unpack_from("<H", d, pe + 20)[0]
    opt = pe + 24
    magic = struct.unpack_from("<H", d, opt)[0]
    subsystem = struct.unpack_from("<H", d, opt + 68)[0]
    ddo = opt + (96 if magic == 0x10B else 112)
    secs = []
    for i in range(nsec):
        name, vs, va, rs, ra = struct.unpack_from("<8sIIII", d, opt + soh + i * 40)
        secs.append((name.rstrip(b"\0").decode(), va, vs, ra, rs))

    def off(rva):
        for _, va, vs, ra, rs in secs:
            if va <= rva < va + max(vs, rs):
                return rva - va + ra
        return None

    def cstr(o):
        return d[o:d.index(b"\0", o)].decode()

    exp_rva = struct.unpack_from("<I", d, ddo)[0]
    imp_rva = struct.unpack_from("<I", d, ddo + 8)[0]
    res_rva, res_size = struct.unpack_from("<II", d, ddo + 16)
    exports, imports = [], []
    if exp_rva:
        o = off(exp_rva)
        n_names, _, names_rva = struct.unpack_from("<III", d, o + 24)[0], 0, struct.unpack_from("<I", d, o + 32)[0]
        no = off(names_rva)
        for i in range(n_names):
            exports.append(cstr(off(struct.unpack_from("<I", d, no + 4 * i)[0])))
    if imp_rva:
        o = off(imp_rva)
        while True:
            ilt, ts, fc, name_rva, iat = struct.unpack_from("<IIIII", d, o)
            if name_rva == 0:
                break
            imports.append(cstr(off(name_rva)))
            o += 20
    # resources: type -> id -> size (first language)
    resources = {}
    if res_rva:
        base = off(res_rva)

        def entries(o):
            named, ids = struct.unpack_from("<HH", d, o + 12)
            for i in range(named + ids):
                nid, ptr = struct.unpack_from("<II", d, o + 16 + 8 * i)
                yield nid, ptr

        for tid, tptr in entries(base):
            if not tptr & 0x80000000:
                continue
            for rid, rptr in entries(base + (tptr & 0x7FFFFFFF)):
                if not rptr & 0x80000000:
                    continue
                for _, lptr in entries(base + (rptr & 0x7FFFFFFF)):
                    drva, dsize = struct.unpack_from("<II", d, base + lptr)
                    resources[(tid, rid)] = dsize
                    break
    return {"machine": machine, "subsystem": subsystem, "exports": exports, "imports": imports, "resources": resources,
            "sections": {s[0]: s[4] for s in secs}, "bytes": len(d)}


# ---------------------------------------------------------------- watching the folders

FILE_LIST_DIRECTORY = 1
FILE_SHARE_ALL = 7
OPEN_EXISTING = 3
FILE_FLAG_BACKUP_SEMANTICS = 0x02000000
NOTIFY = 0x1 | 0x2 | 0x8 | 0x10 | 0x40  # file name, dir name, size, last write, creation
ACTIONS = {1: "added", 2: "removed", 3: "modified", 4: "renamed-from", 5: "renamed-to"}
k32.CreateFileW.restype = wt.HANDLE
k32.CreateFileW.argtypes = [wt.LPCWSTR, wt.DWORD, wt.DWORD, ctypes.c_void_p, wt.DWORD, wt.DWORD, wt.HANDLE]
k32.ReadDirectoryChangesW.argtypes = [wt.HANDLE, ctypes.c_void_p, wt.DWORD, wt.BOOL, wt.DWORD, ctypes.POINTER(wt.DWORD), ctypes.c_void_p, ctypes.c_void_p]
k32.CancelIoEx.argtypes = [wt.HANDLE, ctypes.c_void_p]
k32.CloseHandle.argtypes = [wt.HANDLE]


class Watch(threading.Thread):
    def __init__(self, folder: Path):
        super().__init__(daemon=True)
        self.folder = Path(folder)
        self.events: list[tuple[float, str, str]] = []
        self.overflows = 0
        self.h = k32.CreateFileW(str(self.folder), FILE_LIST_DIRECTORY, FILE_SHARE_ALL, None, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, None)
        if self.h in (None, wt.HANDLE(-1).value):
            raise OSError(ctypes.get_last_error(), "cannot watch " + str(folder))
        self.stop = False

    def run(self):
        buf = ctypes.create_string_buffer(1 << 20)
        got = wt.DWORD()
        while not self.stop:
            ok = k32.ReadDirectoryChangesW(self.h, buf, len(buf), True, NOTIFY, ctypes.byref(got), None, None)
            now = time.time()
            if not ok:
                break
            if got.value == 0:
                self.overflows += 1
                continue
            o = 0
            raw = buf.raw
            while True:
                nxt, action, nlen = struct.unpack_from("<III", raw, o)
                name = raw[o + 12:o + 12 + nlen].decode("utf-16-le")
                self.events.append((now, ACTIONS.get(action, str(action)), str(self.folder / name)))
                if nxt == 0:
                    break
                o += nxt

    def close(self):
        self.stop = True
        k32.CancelIoEx(self.h, None)
        self.join(5)
        k32.CloseHandle(self.h)


# ---------------------------------------------------------------- running a process and reading its peak memory

class PMC(ctypes.Structure):
    _fields_ = [("cb", wt.DWORD), ("PageFaultCount", wt.DWORD), ("PeakWorkingSetSize", ctypes.c_size_t), ("WorkingSetSize", ctypes.c_size_t),
                ("QuotaPeakPagedPoolUsage", ctypes.c_size_t), ("QuotaPagedPoolUsage", ctypes.c_size_t), ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t),
                ("QuotaNonPagedPoolUsage", ctypes.c_size_t), ("PagefileUsage", ctypes.c_size_t), ("PeakPagefileUsage", ctypes.c_size_t)]


psapi.GetProcessMemoryInfo.argtypes = [wt.HANDLE, ctypes.POINTER(PMC), wt.DWORD]


def peak_ws(proc: subprocess.Popen) -> tuple[int, int]:
    m = PMC(); m.cb = ctypes.sizeof(m)
    if not psapi.GetProcessMemoryInfo(int(proc._handle), ctypes.byref(m), m.cb):
        return -1, -1
    return m.PeakWorkingSetSize, m.PeakPagefileUsage


def run(cmd: list, stdin: bytes | None = None, timeout: int = 600, cwd: Path | None = None) -> dict:
    t = time.time()
    p = subprocess.Popen([str(c) for c in cmd], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, cwd=cwd)
    out, err = p.communicate(stdin, timeout=timeout)
    ws, pf = peak_ws(p)
    return {"code": p.returncode, "out": out.decode("utf-8", "replace"), "err": err.decode("utf-8", "replace"), "seconds": round(time.time() - t, 2),
            "peak_working_set": ws, "peak_private": pf}


# ---------------------------------------------------------------- the WPF window

EnumWindowsProc = ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
u32.EnumWindows.argtypes = [EnumWindowsProc, wt.LPARAM]
u32.GetWindowThreadProcessId.argtypes = [wt.HWND, ctypes.POINTER(wt.DWORD)]
u32.GetWindowTextW.argtypes = [wt.HWND, wt.LPWSTR, ctypes.c_int]
u32.GetClassNameW.argtypes = [wt.HWND, wt.LPWSTR, ctypes.c_int]
u32.IsWindowVisible.argtypes = [wt.HWND]
u32.PostMessageW.argtypes = [wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM]
u32.GetWindowRect.argtypes = [wt.HWND, ctypes.POINTER(wt.RECT)]
u32.PrintWindow.argtypes = [wt.HWND, wt.HDC, wt.UINT]
u32.GetDC.argtypes = [wt.HWND]; u32.GetDC.restype = wt.HDC
u32.ReleaseDC.argtypes = [wt.HWND, wt.HDC]
gdi32.CreateCompatibleDC.argtypes = [wt.HDC]; gdi32.CreateCompatibleDC.restype = wt.HDC
gdi32.CreateCompatibleBitmap.argtypes = [wt.HDC, ctypes.c_int, ctypes.c_int]; gdi32.CreateCompatibleBitmap.restype = wt.HBITMAP
gdi32.SelectObject.argtypes = [wt.HDC, wt.HGDIOBJ]; gdi32.SelectObject.restype = wt.HGDIOBJ
gdi32.GetDIBits.argtypes = [wt.HDC, wt.HBITMAP, wt.UINT, wt.UINT, ctypes.c_void_p, ctypes.c_void_p, wt.UINT]
gdi32.DeleteObject.argtypes = [wt.HGDIOBJ]
gdi32.DeleteDC.argtypes = [wt.HDC]


def windows_of(pid: int) -> list[tuple[int, str, str]]:
    found = []

    def cb(hwnd, _):
        p = wt.DWORD()
        u32.GetWindowThreadProcessId(hwnd, ctypes.byref(p))
        if p.value == pid and u32.IsWindowVisible(hwnd):
            t = ctypes.create_unicode_buffer(512); c = ctypes.create_unicode_buffer(256)
            u32.GetWindowTextW(hwnd, t, 512); u32.GetClassNameW(hwnd, c, 256)
            found.append((hwnd, t.value, c.value))
        return True

    u32.EnumWindows(EnumWindowsProc(cb), 0)
    return found


def capture(hwnd: int, out: Path) -> tuple[int, int, int]:
    r = wt.RECT(); u32.GetWindowRect(hwnd, ctypes.byref(r))
    w, h = r.right - r.left, r.bottom - r.top
    sdc = u32.GetDC(None); mdc = gdi32.CreateCompatibleDC(sdc); bmp = gdi32.CreateCompatibleBitmap(sdc, w, h)
    old = gdi32.SelectObject(mdc, bmp)
    u32.PrintWindow(hwnd, mdc, 2)  # PW_RENDERFULLCONTENT
    hdr = struct.pack("<IiiHHIIiiII", 40, w, -h, 1, 32, 0, 0, 0, 0, 0, 0)
    bi = ctypes.create_string_buffer(hdr, 40 + 12)
    bits = ctypes.create_string_buffer(w * h * 4)
    gdi32.GetDIBits(mdc, bmp, 0, h, bits, bi, 0)
    gdi32.SelectObject(mdc, old); gdi32.DeleteObject(bmp); gdi32.DeleteDC(mdc); u32.ReleaseDC(None, sdc)
    pix = bits.raw
    nonblank = len(set(pix[i:i + 3] for i in range(0, len(pix), 4 * 97)))
    # a BMP file (bottom-up rows) so no imaging library is needed
    rows = [pix[y * w * 4:(y + 1) * w * 4] for y in range(h)][::-1]
    body = b"".join(rows)
    out.write_bytes(b"BM" + struct.pack("<IHHI", 54 + len(body), 0, 0, 54) + struct.pack("<IiiHHIIiiII", 40, w, h, 1, 32, 0, len(body), 2835, 2835, 0, 0) + body)
    return w, h, nonblank


def _burn(stop_at: float) -> None:
    x = 0
    while time.time() < stop_at:
        x = (x * 1103515245 + 12345) & 0xFFFFFFFF


# ---------------------------------------------------------------- what the host needs from the PC (simulated; host_main.c's test hooks)

URL48 = "https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48"
URL481 = "https://dotnet.microsoft.com/en-us/download/dotnet-framework/net481"
NONINTERACTIVE = ["--host-info", "--backup-worker", "--driver-action", "--raw-disk", "--scheduled-check", "--version", "--engine-info",
                  "--design-compile", "--home-render-selftest", "--parity-selftest"]


def prerequisite_checks(exe: Path, work: Path, runs: dict, info: dict) -> None:
    """The host's own requirement checks, every one with standard output and error redirected to pipes: a redirected start never shows a
    window or waits, so nothing appears on this PC's screen even if a case went wrong (the setup window itself is exercised only inside
    Windows Sandbox). The hooks can only make a check stricter or simulate a failure; the first two checks show that."""
    check("host-info-prerequisite-ready", info.get("prerequisite") == "ready" and info.get("netfx_download") == URL48 and info.get("simulated") == ""
          and info.get("clr_step") == 0, "prerequisite %s, page %s, simulated %r" % (info.get("prerequisite"), info.get("netfx_download"), info.get("simulated")))
    check("host-info-noninteractive-commands", info.get("noninteractive_commands") == NONINTERACTIVE, json.dumps(info.get("noninteractive_commands")))
    sim = work / "simulate.txt"
    os.environ["CCS_HOST_SIMULATE_FILE"] = str(sim)
    try:
        def case(name, lines, args, stdin=None):
            sim.write_text("\n".join(lines) + "\n", encoding="ascii")
            r = runs["prereq-" + name] = run([exe] + args, stdin=stdin, timeout=60)
            return r

        # the hooks cannot loosen anything: a higher Release value or a newer Windows than the real one changes nothing
        os.environ["CCS_HOST_SIMULATE_NETFX_RELEASE"] = "999999999"
        try:
            r = case("higher-release-ignored", ["netfx_release=999999999", "os=99.0.99999"], ["--host-info"])
        finally:
            del os.environ["CCS_HOST_SIMULATE_NETFX_RELEASE"]
        hi = json.loads(r["out"]) if r["code"] == 0 else {}
        check("hook-cannot-raise-release-or-windows", hi.get("netfx_release") == info.get("netfx_release") and hi.get("os") == info.get("os")
              and hi.get("prerequisite") == "ready", "real %s / %s, with the hooks %s / %s" % (info.get("netfx_release"), info.get("os"), hi.get("netfx_release"), hi.get("os")))

        # this PC's own newer runtime is accepted (the x64 edition needs 4.8; host-info above says what this PC has)
        check("newer-runtime-accepted", info.get("netfx_ok") and info.get("netfx_release", 0) >= 528040, "Release %s (%s)" % (info.get("netfx_release"), info.get("netfx")))

        # missing on a Windows that Microsoft makes 4.8 for (Windows 10 version 1809 with 4.7.2): the download words and page
        r = case("missing-x64", ["netfx_release=461814", "os=10.0.17763"], ["--version"])
        check("missing-x64", r["code"] == 90 and "needs Microsoft .NET Framework 4.8 or later" in r["err"] and URL48 in r["err"] and "Runtime" in r["err"]
              and "This PC has: 4.7.2" in r["err"] and "Nothing was installed or changed" in r["err"] and r["out"] == "", r["err"].strip()[:400])
        r = case("absent-x64", ["netfx_release=0", "os=10.0.17763"], ["--version"])
        check("absent-x64", r["code"] == 90 and "This PC has: none" in r["err"] and URL48 in r["err"], r["err"].strip()[:300])
        # the Arm64 edition's rule (4.8.1; its native runtime is for Windows 11), applied by an x64 file through the hook: rule and words only,
        # not an Arm64 run
        r = case("missing-arm64-rule", ["rules=arm64", "netfx_release=528449", "os=10.0.22000"], ["--version"])
        check("missing-arm64-rule", r["code"] == 90 and "This Arm64 edition of CCS Console Workbench needs Microsoft .NET Framework 4.8.1" in r["err"]
              and URL481 in r["err"], r["err"].strip()[:400])
        # part of this Windows, yet not found (this PC's own Windows includes 4.8): no download, Windows Update and a restart instead
        r = case("not-found", ["netfx_release=461814"], ["--version"])
        check("not-found-part-of-windows", r["code"] == 90 and "part of this version of Windows" in r["err"] and "Windows updates" in r["err"]
              and URL48 not in r["err"], r["err"].strip()[:400])
        # unsupported Windows: no installer offered
        r = case("windows-x64-1511", ["os=10.0.10586", "netfx_release=394254"], ["--version"])   # 4.6.1, as Windows 10 version 1511 has
        check("unsupported-windows-x64", r["code"] == 90 and "Windows 10 version 1607" in r["err"] and "Windows 10 version 1511 (build 10586)" in r["err"]
              and URL48 not in r["err"], r["err"].strip()[:400])
        r = case("windows-81", ["os=6.3.9600"], ["--version"])
        check("unsupported-windows-81", r["code"] == 90 and "needs Windows 10 or Windows 11" in r["err"] and "Windows 8.1" in r["err"], r["err"].strip()[:300])
        r = case("windows-arm64-on-10", ["rules=arm64", "os=10.0.19045", "netfx_release=528372"], ["--version"])   # 4.8, as Windows 10 has
        check("unsupported-windows-arm64-rule", r["code"] == 90 and "Arm-based PCs with Windows 11" in r["err"] and URL481 not in r["err"], r["err"].strip()[:400])
        # present but unusable runtime, told apart from an absent one
        r = case("clr-unusable", ["clr=unusable"], ["--version"])
        check("runtime-unusable", r["code"] == 90 and "cannot start this PC's Microsoft .NET Framework" in r["err"] and "missing or turned off" in r["err"]
              and "Restart this PC" in r["err"] and URL48 not in r["err"], r["err"].strip()[:400])
        r = case("clr-missing", ["clr=missing"], ["--version"])
        check("runtime-mscoree-missing", r["code"] == 90 and "mscoree.dll" in r["err"], r["err"].strip()[:300])
        # a restart Windows is waiting for is said, not done
        r = case("restart", ["netfx_release=461814", "os=10.0.17763", "restart_pending=1"], ["--version"])
        check("restart-waiting-said", r["code"] == 90 and "restart waiting" in r["err"], r["err"].strip()[:400])
        # every simulated value is named in the output
        check("simulated-values-named", "[simulated for testing: netfx_release=461814; os=10.0.17763; restart_pending=1]" in r["err"], r["err"].strip()[-200:])

        # noninteractive commands: the documented exit code at once, no window, no waiting (the window's own process for a worker gets JSON)
        lines = ["netfx_release=461814", "os=10.0.17763"]
        for args in (["--version"], ["--engine-info"], ["--scheduled-check", "--settings", str(work / "none.json"), "--report", str(work / "none")],
                     ["--design-compile"], ["--home-render-selftest", str(work / "none"), "--work", str(work / "none")], ["--parity-selftest", str(work / "none")]):
            r = case("nonint" + args[0], lines, args)
            check("noninteractive" + args[0], r["code"] == 90 and r["seconds"] < 20 and "needs Microsoft .NET Framework 4.8" in r["err"], "exit %s in %s s" % (r["code"], r["seconds"]))
        r = case("worker", lines, ["--backup-worker"], stdin=b"")
        ev = json.loads(r["out"].strip() or "{}")
        check("noninteractive-worker-json", r["code"] == 90 and ev.get("event") == "error" and URL48 in ev.get("text", ""), r["out"].strip()[:300])
        # a window start whose output is redirected (automation) gets the same words and never a window; run only once --host-info has
        # shown the simulation in effect (otherwise this start would open the Workbench window on this PC's screen)
        r = case("redirected-window-start-precheck", lines, ["--host-info"])
        pre = json.loads(r["out"]) if r["out"].strip().startswith("{") else {}
        if pre.get("prerequisite") == "netfx-missing":
            r = case("redirected-window-start", lines, ["--simulated-console", str(work / "none")])
            check("redirected-window-start-no-window", r["code"] == 90 and r["seconds"] < 20 and URL48 in r["err"], "exit %s in %s s" % (r["code"], r["seconds"]))
        else:
            check("redirected-window-start-no-window", False, "not run: the simulation was not in effect (%s)" % pre.get("prerequisite"))

        # a start failure after the checks (simulated before the runtime is bound): its own code and words
        r = case("start-fail", ["start=fail"], ["--version"])
        check("start-failure-exit-92", r["code"] == 92 and "could not start .NET Framework" in r["err"] and "simulated" in r["err"], r["err"].strip()[:300])
        # no simulation file: everything as before
        sim.unlink()
        r = run([exe, "--version"])
        check("no-simulation-file-is-real", r["code"] == 0 and r["out"].strip() != "", r["out"].strip())
    finally:
        os.environ.pop("CCS_HOST_SIMULATE_FILE", None)
        if sim.exists():
            sim.unlink()

    # a damaged copy is not a missing part of Windows: one byte of the carried program changed (a copy in its own folder)
    damaged_dir = work / "damaged"; damaged_dir.mkdir()
    damaged = damaged_dir / exe.name
    data = bytearray(exe.read_bytes())
    pe = pe_facts(exe)
    off = payload_offset(bytes(data))
    data[off + 4096] ^= 0x5A
    damaged.write_bytes(bytes(data))
    r = runs["damaged-version"] = run([damaged, "--version"])
    check("damaged-payload-words", r["code"] == 91 and "damaged" in r["err"] and "download it again" in r["err"] and ".NET Framework" not in r["err"],
          r["err"].strip()[:300])
    r = runs["damaged-host-info"] = run([damaged, "--host-info"])
    hi = json.loads(r["out"]) if r["out"].strip().startswith("{") else {}
    check("damaged-payload-host-info", r["code"] == 91 and hi.get("payload_ok") is False and hi.get("prerequisite") == "ready", "exit %s payload_ok %s" % (r["code"], hi.get("payload_ok")))
    RESULTS["damaged_payload"] = {"byte_changed_at": off + 4096, "resource_bytes": pe["resources"].get((10, 101))}
    shutil.rmtree(damaged_dir, ignore_errors=True)


def payload_offset(d: bytes) -> int:
    """the file offset of the carried program (RCDATA 101)"""
    pe = struct.unpack_from("<I", d, 0x3C)[0]
    nsec, soh = struct.unpack_from("<H", d, pe + 6)[0], struct.unpack_from("<H", d, pe + 20)[0]
    opt = pe + 24
    ddo = opt + (96 if struct.unpack_from("<H", d, opt)[0] == 0x10B else 112)
    secs = [struct.unpack_from("<8sIIII", d, opt + soh + i * 40)[1:] for i in range(nsec)]

    def off(rva):
        for vs, va, rs, ra in secs:
            if va <= rva < va + max(vs, rs):
                return rva - va + ra
        raise ValueError(rva)

    base = off(struct.unpack_from("<I", d, ddo + 16)[0])

    def entries(o):
        named, ids = struct.unpack_from("<HH", d, o + 12)
        for i in range(named + ids):
            yield struct.unpack_from("<II", d, o + 16 + 8 * i)

    for tid, tptr in entries(base):
        for rid, rptr in entries(base + (tptr & 0x7FFFFFFF)):
            for _, lptr in entries(base + (rptr & 0x7FFFFFFF)):
                if (tid, rid) == (10, 101):
                    return off(struct.unpack_from("<I", d, base + lptr)[0])
    raise ValueError("no RCDATA 101")


# ---------------------------------------------------------------- main

def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--work", required=True)
    ap.add_argument("--managed", help="an already built managed Workbench executable (else it is built)")
    ap.add_argument("--host-out", help="a finished build_host.sh output folder (else it is built)")
    ap.add_argument("--ram-images")
    ap.add_argument("--par2-cache")
    ap.add_argument("--big-mb", type=int, default=0, help="also run a timed parity round trip of this many MB (decimal)")
    ap.add_argument("--no-window", action="store_true")
    ap.add_argument("--stress-rounds", type=int, default=6, help="--engine-info runs with every core busy (0 = skip)")
    a = ap.parse_args()
    if a.ram_images is None: raise SystemExit("--ram-images is required (the recovery images folder; the size report and a managed build need it)")
    if a.par2_cache is None and not a.host_out: raise SystemExit("--par2-cache is required to build the host (where the pinned par2cmdline tarball is kept)")
    work = Path(a.work)
    if work.exists():
        shutil.rmtree(work)
    work.mkdir(parents=True)

    # 1. managed
    if a.managed:
        managed = Path(a.managed)
    else:
        env = dict(os.environ, DOTNET_ROOT=str(DOTNET), PATH=str(DOTNET) + os.pathsep + os.environ["PATH"])
        r = subprocess.run([str(DOTNET / "dotnet.exe"), "build", str(CONSOLE_BACKUP / "ccs-backup-utility" / "ccs-backup-utility.csproj"), "-c", "Release",
                            "-nologo", "-v", "q", "-p:CcsRamImages=" + a.ram_images], env=env, capture_output=True, text=True)
        check("managed-build", r.returncode == 0, r.stdout[-400:] if r.returncode else "")
        managed = REPO / "build" / "console-backup" / "bin" / "ccs-backup-utility" / "Release" / "net48" / "CCS-Console-Backup-Restore-Utility.exe"
    check("managed-exists", managed.is_file(), str(managed))
    mp = pe_facts(managed)
    RESULTS["managed"] = {"path": str(managed), "bytes": managed.stat().st_size, "sha256": sha256(managed)}

    # 2. host
    if a.host_out:
        out = Path(a.host_out)
    else:
        out = work / "host"
        t = time.time()
        r = subprocess.run(["wsl.exe", "-d", "Ubuntu", "--", "bash", wsl_path(HERE / "build_host.sh"), "--payload", wsl_path(managed), "--out", wsl_path(out),
                            "--cache", wsl_path(Path(a.par2_cache)), "--measure"],
                           capture_output=True)
        log = (r.stdout + r.stderr).replace(b"\0", b"").decode("utf-8", "replace")
        (work / "build.log").write_text(log, encoding="utf-8")
        check("host-build", r.returncode == 0, "%.0f s; log %s" % (time.time() - t, work / "build.log"))
    record = json.loads((out / "build-record.json").read_text(encoding="utf-8"))
    RESULTS["build_record"] = record
    check("build-record-payload", record["payload"]["sha256"] == RESULTS["managed"]["sha256"], "the host carries exactly the managed executable tested here")

    # 3. static checks
    allowed = {"kernel32.dll", "user32.dll", "gdi32.dll", "advapi32.dll", "shell32.dll", "ole32.dll", "oleaut32.dll", "bcrypt.dll"}   # gdi32: the setup window
    exes = {}
    for o in record["outputs"]:
        exe = out / o["exe"]
        f = pe_facts(exe)
        exes[o["arch"]] = exe
        want = {"x64": 0x8664, "arm64": 0xAA64}[o["arch"]]
        check(o["arch"] + "-machine", f["machine"] == want, hex(f["machine"]))
        check(o["arch"] + "-gui-subsystem", f["subsystem"] == 2, str(f["subsystem"]))
        odd = [i for i in f["imports"] if i.lower() not in allowed and not i.lower().startswith("api-ms-win-crt-")]
        check(o["arch"] + "-imports-only-windows", not odd, ", ".join(f["imports"]))
        check(o["arch"] + "-exports-abi", "ccs_host_get_abi" in f["exports"], ", ".join(f["exports"]))
        check(o["arch"] + "-payload-resource", f["resources"].get((10, 101)) == RESULTS["managed"]["bytes"], "RCDATA 101 = %s bytes" % f["resources"].get((10, 101)))
        check(o["arch"] + "-manifest-resource", (24, 1) in f["resources"], "")
        o["imports"] = f["imports"]
    dump = subprocess.run(["pwsh", "-NoProfile", "-File", str(HERE / "tools" / "dump_vtables.ps1")], capture_output=True, text=True)
    slots = {}
    cur = None
    for line in dump.stdout.splitlines():
        m = re.match(r"^(_\w+) \{", line)
        if m:
            cur = m.group(1)
        m = re.match(r"^\s+slot (\d+)\s+(\S+)( \(get\))?", line)
        if m and cur:
            slots[(cur, m.group(2) + ("_get" if m.group(3) else ""))] = int(m.group(1))
    header = (HERE / "src" / "clr_hosting.h").read_text(encoding="utf-8")
    for const, key in [("SLOT__AppDomain_Load_3", ("_AppDomain", "Load_3")), ("SLOT__AppDomain_SetData", ("_AppDomain", "SetData")),
                       ("SLOT__Assembly_get_EntryPoint", ("_Assembly", "EntryPoint_get")), ("SLOT__MethodInfo_Invoke_3", ("_MethodInfo", "Invoke_3"))]:
        m = re.search(r"#define " + const + r"\s+(\d+)", header)
        check("vtable-" + const, m is not None and slots.get(key) == int(m.group(1)), "header %s, mscorlib.tlb %s" % (m and m.group(1), slots.get(key)))

    # the Arm64 executable: run it if this PC can (an Arm64 PC); on an x64 PC Windows refuses to start it, which is recorded, not hidden
    if exes.get("arm64"):
        try:
            r = run([exes["arm64"], "--host-info"])
            RESULTS["arm64_run"] = {"ran": True, "code": r["code"], "out": r["out"][:2000], "err": r["err"][:500]}
            check("arm64-host-info", r["code"] == 0 and json.loads(r["out"]).get("payload_ok"), r["out"][:400])
        except OSError as e:
            RESULTS["arm64_run"] = {"ran": False, "winerror": e.winerror, "text": str(e)}
            print("NOTE arm64 executable not executed here: WinError %s (%s)" % (e.winerror, e.strerror), flush=True)

    # 4. runs (x64; this PC is x64)
    x64 = exes.get("x64")
    run_dir = work / "run-x64"; run_dir.mkdir()
    exe = run_dir / x64.name
    shutil.copy2(x64, exe)
    before_beside = sorted(p.name for p in run_dir.iterdir())
    watched = {"TEMP": Path(os.environ["TEMP"]), "APPDATA": Path(os.environ["APPDATA"]), "LOCALAPPDATA": Path(os.environ["LOCALAPPDATA"]), "BESIDE_EXE": run_dir}
    # the test's own output folders are outside the watched trees (checked) so the self-test's writes are not mixed in
    for n, w in watched.items():
        if n != "BESIDE_EXE":
            check("work-outside-" + n, not str(work.resolve()).lower().startswith(str(w.resolve()).lower()), str(w))
    watches = {}
    for n, p in watched.items():
        try:
            watches[n] = Watch(p)
        except OSError as e:
            if n != "TEMP":
                raise
            # this process's TEMP cannot be watched without administrator rights (a shared folder such as C:\Windows\Temp): every program
            # run below gets TEMP and TMP pointing at a new empty folder instead, which is watched (GetTempPathW and .NET's Path.GetTempPath
            # read TMP/TEMP); the profile's own Temp folder is inside %LOCALAPPDATA%, watched anyway
            sentinel = work / "sentinel-temp"; sentinel.mkdir()
            os.environ["TEMP"] = os.environ["TMP"] = str(sentinel)
            RESULTS["temp_method"] = "TEMP %s not watchable (%s); children ran with TEMP=TMP=%s, watched" % (p, e, sentinel)
            print("NOTE " + RESULTS["temp_method"], flush=True)
            watched["TEMP"] = sentinel
            watches[n] = Watch(sentinel)
    for w in watches.values():
        w.start()
    t_start = time.time()
    runs = {}

    r = runs["host-info"] = run([exe, "--host-info"])
    info = json.loads(r["out"]) if r["code"] == 0 else {}
    check("host-info", r["code"] == 0 and info.get("payload_ok") and info.get("netfx_ok") and info.get("clr_loadable"), r["out"].strip()[:600] + r["err"])
    RESULTS["host_info"] = info

    # the plain ".NET Framework missing" path, simulated (the host's test hook; nothing on the PC is changed)
    os.environ["CCS_HOST_SIMULATE_NETFX_RELEASE"] = "461808"   # what a PC with only .NET Framework 4.7.2 reports
    try:
        r = runs["netfx-missing"] = run([exe, "--version"])
        check("netfx-missing-message", r["code"] == 90 and "needs Microsoft .NET Framework 4.8" in r["err"] and "Nothing was installed or changed" in r["err"]
              and r["out"] == "", r["err"].strip())
        r = runs["netfx-missing-worker"] = run([exe, "--backup-worker"], stdin=b"")
        ev = json.loads(r["out"].strip() or "{}")
        check("netfx-missing-worker-event", r["code"] == 90 and ev.get("event") == "error" and "4.8" in ev.get("text", ""), r["out"].strip()[:300])
    finally:
        del os.environ["CCS_HOST_SIMULATE_NETFX_RELEASE"]
    prerequisite_checks(exe, work, runs, info)

    r = runs["version"] = run([exe, "--version"])
    direct = run([managed, "--version"])
    check("version", r["code"] == 0 and r["out"].strip() == direct["out"].strip() and r["out"].strip() != "", "hosted %r, direct %r" % (r["out"].strip(), direct["out"].strip()))
    runs["direct-version"] = direct

    r = runs["engine-info"] = run([exe, "--engine-info"])
    direct = runs["direct-engine-info"] = run([managed, "--engine-info"])
    try:
        hosted_report, direct_report = json.loads(r["out"]), json.loads(direct["out"])
        same = hosted_report == direct_report
    except Exception as e:  # noqa: BLE001
        hosted_report, same = {"error": str(e), "out": r["out"][:300], "err": r["err"][:300]}, False
    check("engine-info", r["code"] == 0 and same, "identical to the managed executable's own report" if same else json.dumps(hosted_report)[:500])
    RESULTS["engine_info"] = hosted_report

    # the worker is sent only a request it refuses before any console code runs. No console request is ever sent from here: with the
    # console tasks available, a request such as prepare-plan without "console":"simulated" looks for a real console on this PC's USB.
    r = runs["worker-unknown"] = run([exe, "--backup-worker"], stdin=b'{"cmd":"no-such-request"}\n')
    check("worker-unknown-request", r["code"] == 2 and '"unknown request"' in r["out"], "exit %s: %s" % (r["code"], r["out"].strip()[:300]))

    st_dir = work / "parity-selftest-üé-テスト"   # non-ASCII on purpose: paths go UTF-16 -> UTF-8 -> UTF-16 through libpar2
    r = runs["parity-selftest"] = run([exe, "--parity-selftest", st_dir], timeout=900)
    lines = [json.loads(x) for x in r["out"].splitlines() if x.startswith("{")]
    RESULTS["parity_selftest"] = lines
    for ln in lines:
        if "check" in ln:
            check("par2-" + ln["check"], ln["ok"], ln["detail"][:400])
    env_line = next((ln for ln in lines if "process_image" in ln), {})
    check("hosted-process-image-is-host", env_line.get("process_image", "").lower() == str(exe).lower(), env_line.get("process_image", ""))
    check("hosted-apartment-sta", env_line.get("apartment") == "STA", str(env_line.get("apartment")))
    # loaded from memory there is no entry assembly; the program gives the domain its own target framework (Program.HostedTargetFramework),
    # without which .NET Framework applies 4.0 compatibility defaults such as legacy path handling
    check("hosted-target-framework", env_line.get("target_framework") == ".NETFramework,Version=v4.8" and env_line.get("legacy_path_handling") is not True
          and env_line.get("block_long_paths") is not True, "target framework %s, legacy path handling %s, block long paths %s" % (
              env_line.get("target_framework"), env_line.get("legacy_path_handling"), env_line.get("block_long_paths")))
    check("parity-selftest", r["code"] == 0 and any(ln.get("selftest") == "passed" for ln in lines), "exit %s %s" % (r["code"], r["err"][:300]))
    direct = runs["direct-parity-selftest"] = run([managed, "--parity-selftest", work / "parity-unhosted"])
    check("unhosted-is-null-safe", direct["code"] == 1 and "needs the standalone CCS Console Workbench or Classic Cart Studio" in direct["out"] and not (work / "parity-unhosted").exists(),
          direct["out"].strip().splitlines()[1][:200] if direct["out"].strip() else direct["err"][:200])

    if a.big_mb:
        big = work / "parity-big"
        r = runs["parity-big"] = run([exe, "--parity-selftest", big, "--parity-selftest-bytes", str(a.big_mb * 1000000)], timeout=3600)
        check("parity-big-%dMB" % a.big_mb, r["code"] == 0, "%s s, peak working set %s" % (r["seconds"], r["peak_working_set"]))
        RESULTS["parity_big"] = [json.loads(x) for x in r["out"].splitlines() if x.startswith("{")]
        shutil.rmtree(big, ignore_errors=True)

    if not a.no_window:
        ui = work / "window"; (ui / "backups").mkdir(parents=True); (ui / "sim").mkdir()
        settings = ui / "settings.json"
        settings.write_text(json.dumps({"schema": "ccs-console-backup-settings/1", "primary_folder": str(ui / "backups"), "archive_folder": ""}), encoding="utf-8")
        for label, prog in (("window", exe), ("direct-window", managed)):
            t = time.time()
            p = subprocess.Popen([str(prog), "--settings", str(settings), "--simulated-console", str(ui / "sim")])
            found = []
            while time.time() - t < 90 and p.poll() is None:
                found = [w for w in windows_of(p.pid) if w[1]]
                if found:
                    break
                time.sleep(0.25)
            shown = time.time() - t
            detail = "no window"
            if found:
                hwnd, title, cls = found[0]
                time.sleep(3.0)  # let the first frames render
                cw, ch, colors = capture(hwnd, work / (label + ".bmp"))
                detail = "title %r, class %r, shown after %.1f s, %d x %d, %d distinct sampled colors, capture %s" % (
                    title, cls, shown, cw, ch, colors, work / (label + ".bmp"))
                u32.PostMessageW(hwnd, 0x0010, 0, 0)  # WM_CLOSE
            try:
                p.wait(30)
            except subprocess.TimeoutExpired:
                p.kill()
            ws, pf = peak_ws(p)
            runs[label] = {"code": p.returncode, "seconds": round(time.time() - t, 2), "shown_after": round(shown, 2), "peak_working_set": ws, "peak_private": pf}
            check("wpf-" + label, bool(found) and found[0][2].startswith("HwndWrapper") and p.returncode == 0, detail + "; exit %s" % p.returncode)

    # the exit code survives a fully loaded CPU (the runtime's own shutdown path lost it in most such runs)
    if a.stress_rounds:
        import multiprocessing as mp
        stop_at = time.time() + 600
        burners = [mp.Process(target=_burn, args=(stop_at,)) for _ in range((os.cpu_count() or 4) * 2)]
        for b in burners:
            b.start()
        codes = []
        try:
            for i in range(a.stress_rounds):
                r = run([exe, "--engine-info"])
                codes.append(r["code"] & 0xFFFFFFFF)
        finally:
            for b in burners:
                b.terminate()
        check("exit-code-under-cpu-load", all(c == 0 for c in codes), "%d runs of --engine-info with every core busy: exit codes %s" % (
            len(codes), ", ".join("0x%08x" % c for c in codes)))

    t_end = time.time()
    time.sleep(1.0)
    for w in watches.values():
        w.close()

    # 5. what changed on disk during the runs
    markers = ["ccs-", "ccs ", "ccs_", "ccsbackup", "ccsconsole", "ccsnative", "classic cart", "workbench", "par2", "libusbdotnet", exe.stem.lower(), managed.stem.lower()]
    report = {}
    for n, w in watches.items():
        ev = [e for e in w.events if t_start - 0.5 <= e[0] <= t_end + 1.0]
        ours = [e for e in ev if any(m in e[2].lower() for m in markers)]
        pe = [e for e in ev if e[1] in ("added", "renamed-to", "modified") and e[2].lower().endswith((".exe", ".dll", ".sys", ".winmd"))]
        report[n] = {"events": len(ev), "overflows": w.overflows, "product_named": [e[1:] for e in ours], "executables": [e[1:] for e in pe],
                     "sample_other": sorted({e[2] for e in ev})[:15]}
        if n == "BESIDE_EXE":
            after = sorted(p.name for p in run_dir.iterdir())
            check("nothing-written-beside-exe", not ev and after == before_beside, "%d events; folder now %s" % (len(ev), after))
        else:
            check("no-product-files-in-" + n, not ours, json.dumps([e[1:] for e in ours])[:600])
            check("no-executables-created-in-" + n, not pe, json.dumps([e[1:] for e in pe])[:600])
            check("watch-complete-" + n, w.overflows == 0, "%d buffer overflows" % w.overflows)
    RESULTS["disk_watch"] = report

    # 6. sizes
    payload = managed.read_bytes()
    parts = {k: v.get("bytes") for k, v in (RESULTS.get("engine_info") or {}).get("libraries", {}).items() if isinstance(v, dict)}
    sizes = {"managed_payload_bytes": len(payload), "managed_payload_parts": parts,
             "managed_payload_parts_total": sum(v for v in parts.values() if v),
             "payload_zlib9_bytes": len(zlib.compress(payload, 9)), "payload_xz9_bytes": len(lzma.compress(payload, preset=9)),
             "hosts": {}}
    for o in record["outputs"]:
        full, stub = o["bytes"], o["without_par2_bytes"]
        sizes["hosts"][o["arch"]] = {"exe_bytes": full, "without_par2_bytes": stub, "par2_and_cxx_runtime_bytes": full - stub if stub else None,
                                     "native_code_and_resources_without_payload_bytes": full - len(payload)}
    # the recovery images inside the engine (CcsBackup.csproj embeds the variants it names): repeated files, and what xz would make of them
    variants = re.findall(r"\$\(CcsRamImages\)\\([\w-]+)\\\*",(CONSOLE_BACKUP / "CcsBackup" / "CcsBackup.csproj").read_text(encoding="utf-8"))
    ram = [f for v in variants for f in sorted((Path(a.ram_images) / v).iterdir()) if f.is_file()]
    seen, dup = {}, 0
    for f in ram:
        h = sha256(f)
        if h in seen:
            dup += f.stat().st_size
        seen.setdefault(h, f.name)
    sizes["recovery_images"] = {"variants": variants, "files": len(ram), "bytes": sum(f.stat().st_size for f in ram), "duplicate_bytes": dup,
                                "xz9_bytes": len(lzma.compress(b"".join(f.read_bytes() for f in ram), preset=9))}
    RESULTS["sizes"] = sizes
    RESULTS["runs"] = {k: {kk: vv for kk, vv in v.items() if kk not in ("out", "err")} for k, v in runs.items()}
    RESULTS["arm64"] = ("built, checked statically (machine, subsystem, imports, export, payload) and executed" if RESULTS.get("arm64_run", {}).get("ran")
                        else "built and checked statically (machine, subsystem, imports, export, payload); NOT executed: Windows on this PC refused to start it (%s)"
                        % RESULTS.get("arm64_run", {}).get("text", "not attempted"))
    (work / "results.json").write_text(json.dumps(RESULTS, indent=1, default=str), encoding="utf-8")
    print()
    print("sizes:", json.dumps(sizes, indent=1))
    print("peak working set (bytes):", {k: v.get("peak_working_set") for k, v in RESULTS["runs"].items()})
    print("disk watch:", {k: {"events": v["events"], "product_named": len(v["product_named"]), "executables": len(v["executables"])} for k, v in report.items()})
    print("results:", work / "results.json")
    print("FAILED: " + ", ".join(FAILS) if FAILS else "ALL PASSED")
    return 1 if FAILS else 0


if __name__ == "__main__":
    sys.exit(main())
