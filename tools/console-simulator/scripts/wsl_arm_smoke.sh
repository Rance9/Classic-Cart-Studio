#!/bin/bash
# Real ARM execution proof (runs inside WSL): inspect the ARM ELF, run the launcher core and the game stub under qemu-arm
# user mode with a scripted protocol session, and show the dynamic-glibc variant failing without its runtime and running
# with it. Writes artifacts to $CCS_SIM_ARTIFACTS/arm-smoke/ and prints a JSON summary line at the end.
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; FX="$HERE/../fixtures"
TC="${CCS_SIM_TOOLCHAIN:-$HOME/.cache/ccs-sim/toolchain}"; B="${CCS_SIM_BUILD:-/mnt/c/NES/work/console-simulator/build}"
ART="${CCS_SIM_ARTIFACTS:-/mnt/c/NES/work/console-simulator/artifacts}/arm-smoke"; mkdir -p "$ART"
Q="$TC/deb/usr/bin/qemu-arm"; SYSROOT="$TC/deb/usr/arm-linux-gnueabihf"; MUSL="$B/arm-musl/ccs-sim-core"; GLIBC="$B/arm-glibc/ccs-sim-core"
[ -x "$Q" ] || { echo '{"ok":false,"error":"qemu-arm not set up"}'; exit 2; }
[ -f "$MUSL" ] || { echo '{"ok":false,"error":"arm-musl build missing"}'; exit 2; }
{
echo "== file"; file -b "$MUSL"; file -b "$GLIBC" 2>/dev/null
echo "== readelf -h (arm-musl)"; readelf -h "$MUSL" | grep -E "Class|Data|Machine|Type|Flags|Entry"
echo "== readelf -A (arm-musl)"; readelf -A "$MUSL" | grep -E "Tag_CPU_name|Tag_CPU_arch:|Tag_ABI_VFP_args|Tag_FP_arch|Tag_ARM_ISA|Tag_THUMB"
echo "== readelf -d/-l (arm-glibc)"; readelf -d "$GLIBC" | grep NEEDED; readelf -l "$GLIBC" | grep -i interpreter
} > "$ART/inspect.txt" 2>&1
cat "$ART/inspect.txt"
echo "== qemu-arm: scripted session (static musl)"
printf "load %s/mixed.json\nmenu\ninput press down\ntick 500\ninput release down\nfocus lost\nfocus gained\nframe %s/arm-frame.ppm\nlaunch\ngame-exit 0\ninput press up\nstats\nshutdown\n" "$FX" "$ART" > "$ART/session.txt"
start=$(date +%s%N); "$Q" "$MUSL" < "$ART/session.txt" > "$ART/events-musl.jsonl" 2> "$ART/stderr-musl.txt"; rc_musl=$?; end=$(date +%s%N)
ms_musl=$(( (end - start) / 1000000 )); echo "exit $rc_musl in ${ms_musl} ms, $(wc -l < "$ART/events-musl.jsonl") events"; head -1 "$ART/events-musl.jsonl"; grep -E '"ev":"(fixture_loaded|frame_written|launch_requested|resumed|stats)"' "$ART/events-musl.jsonl"
echo "== qemu-arm: game stub (static musl)"
"$Q" "$MUSL" --game-stub --exit-code=7 --delay-ms=20 > "$ART/stub.jsonl"; rc_stub=$?; cat "$ART/stub.jsonl"; echo "exit $rc_stub (expected 7)"
echo "== qemu-arm: dynamic glibc WITHOUT its ARM runtime (expected to fail understandably)"
"$Q" "$GLIBC" --version > "$ART/glibc-no-sysroot.txt" 2>&1; rc_nosys=$?; cat "$ART/glibc-no-sysroot.txt"; echo "exit $rc_nosys"
echo "== qemu-arm: dynamic glibc WITH -L $SYSROOT"
"$Q" -L "$SYSROOT" "$GLIBC" --version > "$ART/glibc-sysroot.txt" 2>&1; rc_sys=$?; cat "$ART/glibc-sysroot.txt"; echo "exit $rc_sys"
echo "== qemu-arm: timeout handling (startup-hang fault, 2 s limit)"
timeout 2 "$Q" "$MUSL" --fault=startup-hang > /dev/null 2>&1; rc_to=$?; echo "exit $rc_to (124 = killed by timeout, as expected)"
echo "== qemu-arm: nonzero exit (startup-fail fault)"
"$Q" "$MUSL" --fault=startup-fail > "$ART/startup-fail.jsonl" 2>&1; rc_sf=$?; cat "$ART/startup-fail.jsonl"; echo "exit $rc_sf (expected 3)"
ok=true; [ $rc_musl -eq 0 ] && [ $rc_stub -eq 7 ] && [ $rc_nosys -ne 0 ] && [ $rc_sys -eq 0 ] && [ $rc_to -eq 124 ] && [ $rc_sf -eq 3 ] && grep -q '"arch":"arm"' "$ART/events-musl.jsonl" && grep -q 'ARM' "$ART/inspect.txt" || ok=false
python3 - "$ART" "$ok" "$rc_musl" "$ms_musl" "$rc_stub" "$rc_nosys" "$rc_sys" "$rc_to" "$rc_sf" "$("$Q" --version | head -1)" <<'EOF'
import json, sys
a = sys.argv; s = {"schema": "ccs-sim-arm-smoke/1", "ok": a[2] == "true", "qemu": a[10], "musl_session_exit": int(a[3]), "musl_session_ms": int(a[4]), "stub_exit": int(a[5]), "glibc_without_runtime_exit": int(a[6]), "glibc_with_runtime_exit": int(a[7]), "hang_timeout_exit": int(a[8]), "startup_fail_exit": int(a[9]), "artifacts": a[1],
     "caveat": "QEMU user-mode execution proves the ARMv7 build runs; it proves nothing about the NES Classic's display, Clover, kernel or recovery behavior"}
json.dump(s, open(a[1] + "/summary.json", "w"), indent=1); print(json.dumps(s))
EOF
$ok && exit 0 || exit 1
