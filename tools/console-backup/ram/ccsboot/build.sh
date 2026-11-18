#!/bin/sh
# Builds ccsboot for the console (armv7 hard-float, static musl) and its test build (file-backed device, run under qemu-arm only).
# Usage (in WSL): build.sh OUTDIR [ZIG]. Reproducible: fixed flags, no build date, source paths mapped.
set -eu
here=$(cd "$(dirname "$0")" && pwd); out=$1; zig=${2:-$HOME/.cache/ccs-sim/toolchain/zig/zig}
mkdir -p "$out"
flags="-target arm-linux-musleabihf -mcpu=cortex_a7 -Os -static -Wall -Wextra -Werror -ffile-prefix-map=$here=. -Wl,--build-id=none"
cd "$here"
"$zig" cc $flags -o "$out/ccsboot" ccsboot.c
"$zig" cc $flags -DCCSBOOT_TEST_DEVICE -o "$out/ccsboot-test" ccsboot.c
sha256sum ccsboot.c "$out/ccsboot" "$out/ccsboot-test"
