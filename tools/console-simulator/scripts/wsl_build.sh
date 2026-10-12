#!/bin/bash
# Build the shared launcher core for every target (runs inside WSL). One source tree, four outputs:
#   linux-x86_64      gcc (native WSL)                 -> host test runner on Linux
#   windows-x86_64    zig cc -target x86_64-windows-gnu -> the interactive Windows harness
#   arm-musl          zig cc -target arm-linux-musleabihf -mcpu=cortex_a7, static -> primary ARMv7 proof under qemu-arm
#   arm-glibc         zig cc -target arm-linux-gnueabihf.2.31 -mcpu=cortex_a7, dynamic -> optional: exercises the armhf runtime path
# Outputs: $CCS_SIM_BUILD/<target>/ccs-sim-core[.exe] plus build.log and build-info.json per target.
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; SRC="$HERE/../core"
TC="${CCS_SIM_TOOLCHAIN:-$HOME/.cache/ccs-sim/toolchain}"; OUT="${CCS_SIM_BUILD:-/mnt/c/NES/work/console-simulator/build}"
ZIG="$TC/zig/zig"; TARGETS="${1:-all}"
SRCS="$SRC/main.c $SRC/json_min.c $SRC/model.c $SRC/input.c $SRC/fb.c"; CFLAGS="-std=c11 -O2 -Wall -Wextra -Wno-unused-parameter"
mkdir -p "$OUT"; rc=0
build() {   # name, compiler command..., output name
  local name="$1"; shift; local exe="$1"; shift; local d="$OUT/$name"; mkdir -p "$d"
  echo "== $name"; echo "$*" > "$d/build.log"
  if "$@" >> "$d/build.log" 2>&1; then echo "ok: $d/$exe ($(stat -c %s "$d/$exe") bytes)"; else echo "FAILED: see $d/build.log"; tail -5 "$d/build.log"; rc=1; return; fi
  python3 - "$d" "$name" "$exe" "$*" <<'EOF'
import json, os, sys, subprocess, hashlib, datetime
d, name, exe, cmd = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
p = os.path.join(d, exe); info = {"schema": "ccs-sim-build-info/1", "target": name, "executable": exe, "built": datetime.datetime.now(datetime.timezone.utc).isoformat(), "command": cmd,
    "sha256": hashlib.sha256(open(p, "rb").read()).hexdigest(), "size": os.path.getsize(p)}
try: info["file"] = subprocess.run(["file", "-b", p], capture_output=True, text=True).stdout.strip()
except Exception as e: info["file"] = f"error: {e}"
if not exe.endswith(".exe"):
    try:
        h = subprocess.run(["readelf", "-h", p], capture_output=True, text=True).stdout
        info["readelf"] = {k.strip(): v.strip() for k, v in (l.split(":", 1) for l in h.splitlines() if ":" in l)}
        info["interpreter"] = subprocess.run(["readelf", "-l", p], capture_output=True, text=True).stdout
        info["interpreter"] = next((l.split("interpreter:")[1].strip(" ]") for l in info["interpreter"].splitlines() if "interpreter:" in l), "none (static)")
        dyn = subprocess.run(["readelf", "-d", p], capture_output=True, text=True).stdout
        info["needed"] = [l.split("[")[1].rstrip("]") for l in dyn.splitlines() if "NEEDED" in l]
        a = subprocess.run(["readelf", "-A", p], capture_output=True, text=True).stdout
        info["arm_attributes"] = [l.strip() for l in a.splitlines() if any(k in l for k in ("Tag_CPU_name", "Tag_CPU_arch", "Tag_ABI_VFP_args", "Tag_FP_arch"))]
    except Exception as e: info["readelf_error"] = str(e)
json.dump(info, open(os.path.join(d, "build-info.json"), "w"), indent=1)
EOF
}
want() { [ "$TARGETS" = all ] || [[ " $TARGETS " == *" $1 "* ]]; }
want linux-x86_64 && build linux-x86_64 ccs-sim-core gcc $CFLAGS -DCCS_BUILD_TARGET='"linux-x86_64-gcc"' -o "$OUT/linux-x86_64/ccs-sim-core" $SRCS -lm
if [ -x "$ZIG" ]; then
  want windows-x86_64 && build windows-x86_64 ccs-sim-core.exe "$ZIG" cc -target x86_64-windows-gnu $CFLAGS -DCCS_BUILD_TARGET='"windows-x86_64-zig"' -o "$OUT/windows-x86_64/ccs-sim-core.exe" $SRCS -lpsapi
  want arm-musl && build arm-musl ccs-sim-core "$ZIG" cc -target arm-linux-musleabihf -mcpu=cortex_a7 $CFLAGS -DCCS_BUILD_TARGET='"arm-linux-musleabihf-zig"' -static -o "$OUT/arm-musl/ccs-sim-core" $SRCS
  want arm-glibc && build arm-glibc ccs-sim-core "$ZIG" cc -target arm-linux-gnueabihf.2.31 -mcpu=cortex_a7 $CFLAGS -DCCS_BUILD_TARGET='"arm-linux-gnueabihf-zig"' -o "$OUT/arm-glibc/ccs-sim-core" $SRCS
else echo "zig not installed at $ZIG: run setup first (only linux-x86_64 built)"; rc=1; fi
exit $rc
