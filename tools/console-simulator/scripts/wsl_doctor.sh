#!/bin/bash
# Non-destructive environment report for the Console Simulator (WSL side). Prints JSON; never installs or changes anything.
TC="${CCS_SIM_TOOLCHAIN:-$HOME/.cache/ccs-sim/toolchain}"; OUT="${CCS_SIM_BUILD:-/mnt/c/NES/work/console-simulator/build}"
python3 - "$TC" "$OUT" <<'EOF'
import json, os, sys, subprocess, shutil, platform
tc, out = sys.argv[1], sys.argv[2]
def ver(cmd):
    try: return subprocess.run(cmd, capture_output=True, text=True, timeout=30).stdout.strip().splitlines()[0]
    except Exception as e: return None
r = {"schema": "ccs-sim-doctor/1", "side": "wsl", "ok": True, "problems": [], "notes": []}
try: r["os"] = dict(l.rstrip().split("=", 1) for l in open("/etc/os-release") if "=" in l).get("PRETTY_NAME", "").strip('"')
except Exception: r["os"] = "unknown"
r["kernel"] = platform.release(); r["machine"] = platform.machine(); r["user"] = os.environ.get("USER")
r["gcc"] = ver(["gcc", "--version"]); r["make"] = ver(["make", "--version"]); r["python3"] = platform.python_version()
r["file"] = ver(["file", "--version"]); r["readelf"] = ver(["readelf", "--version"])
zig = os.path.join(tc, "zig", "zig"); r["zig"] = ver([zig, "version"]) if os.path.exists(zig) else None
q = os.path.join(tc, "deb", "usr", "bin", "qemu-arm"); r["qemu_arm"] = ver([q, "--version"]) if os.path.exists(q) else None
ld = os.path.join(tc, "deb", "usr", "arm-linux-gnueabihf", "lib", "ld-linux-armhf.so.3"); r["armhf_runtime"] = os.path.exists(ld)
r["toolchain_prefix"] = tc; r["build_dir"] = out; r["build_dir_exists"] = os.path.isdir(out)
r["builds"] = {n: os.path.exists(os.path.join(out, n, "ccs-sim-core" + (".exe" if n.startswith("windows") else ""))) for n in ("linux-x86_64", "windows-x86_64", "arm-musl", "arm-glibc")} if os.path.isdir(out) else {}
r["wslg"] = {"DISPLAY": os.environ.get("DISPLAY"), "WAYLAND_DISPLAY": os.environ.get("WAYLAND_DISPLAY")}
r["disk_free_gb"] = round(shutil.disk_usage("/").free / 1e9, 1)
r["qemu_system_arm"] = ver(["qemu-system-arm", "--version"]); r["notes"].append("qemu-system-arm (generic virt guest) is optional and not installed; user-mode qemu-arm is the ARM proof path")
for k, msg in (("gcc", "gcc missing: install build-essential (needs sudo) or build only via zig"), ("zig", "zig not set up: run `sim.ps1 setup`"), ("qemu_arm", "qemu-arm not set up: run `sim.ps1 setup`")):
    if not r.get(k): r["ok"] = False; r["problems"].append(msg)
print(json.dumps(r, indent=1))
EOF
