#!/bin/bash
# Build Classic Cart Studio's game player with the project's zig toolchain (inside WSL; the same toolchain as the native HOME core).
#   build_player.sh <out dir> [targets]      targets: windows (ccs-player.exe, the Windows app's player), headless (ccs-player-headless.exe,
#                                            tests), arm (ccs-player-headless built for arm-linux-musleabihf Cortex-A7: feasibility only),
#                                            web (ccs-player.wasm: the browser player, wasm32-wasi, the page runtime calls it step by step),
#                                            headless-wasm (ccs-player-headless.wasm: the headless player as WebAssembly, run under node:wasi,
#                                            for frame and save parity with the native headless player),
#                                            console (ccs-player-armv7: the player on the NES Classic itself, host_dev.c + devio.c + console_libc.c, static, stripped),
#                                            console-sym (the same with its symbols: for the system-call check), console-x86 (ccs-player-dev-x86_64:
#                                            the same console host for x86-64 Linux, for fast host tests in WSL)
# The same pinned zig compiles every target (WebAssembly included, with the wasi-libc zig bundles); nothing else is installed.
# The sources are copied to a Linux folder first (compiling through /mnt/c is slow); FCEUmm is compiled once per target into a static
# library and reused while its sources are unchanged.
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; TOOLS="$(cd "$HERE/../.." && pwd)"; OUT="${1:?out dir}"; TARGETS="${2:-windows headless}"
TC="${CCS_SIM_TOOLCHAIN:-$HOME/.cache/ccs-sim/toolchain}"; ZIG="$TC/zig/zig"; W="${CCS_PLAYER_WORK:-$HOME/.cache/ccs-player-build}"
[ -x "$ZIG" ] || { echo "zig not installed at $ZIG: run tools/console-simulator/scripts/wsl_setup.sh first"; exit 2; }
mkdir -p "$W/src/player" "$W/src/console-simulator/core" "$OUT"
rsync -a --delete --checksum "$TOOLS/player/" "$W/src/player/" && rsync -a --delete --checksum "$TOOLS/console-simulator/core/" "$W/src/console-simulator/core/" || { echo "copy failed"; exit 2; }
cp "$TOOLS/publish/assets/app.ico" "$W/src/app.ico" && printf '1 ICON "app.ico"\n' > "$W/src/player.rc" || { echo "icon copy failed"; exit 2; }   # the Classic Cart Studio icon, embedded in ccs-player.exe
F="$W/src/player/third_party/fceumm/src"; LC="$F/drivers/libretro/libretro-common"
FSRC="$(ls $F/boards/*.c $F/input/*.c) $F/drivers/libretro/libretro.c $F/drivers/libretro/libretro_dipswitch.c $F/cart.c $F/cheat.c $F/crc32.c $F/fceu-endian.c $F/fceu-memory.c $F/fceu.c $F/fds.c $F/fds_apu.c $F/file.c $F/filter.c $F/general.c $F/input.c $F/md5.c $F/nsf.c $F/palette.c $F/ppu.c $F/sound.c $F/state.c $F/video.c $F/vsuni.c $F/ines.c $F/unif.c $F/x6502.c $LC/streams/memory_stream.c $LC/compat/compat_posix_string.c $LC/compat/compat_snprintf.c $LC/compat/compat_strcasestr.c $LC/compat/compat_strl.c $LC/compat/fopen_utf8.c $LC/encodings/encoding_utf.c $LC/file/file_path.c $LC/file/file_path_io.c $LC/streams/file_stream.c $LC/streams/file_stream_transforms.c $LC/string/stdstring.c $LC/time/rtime.c $LC/vfs/vfs_implementation.c"
FDEF="-D__LIBRETRO__ -DPATH_MAX=1024 -DFCEU_VERSION_NUMERIC=9900 -DFRONTEND_SUPPORTS_RGB565 -DLSB_FIRST -DLOCAL_LE=1"
FINC="-I$F/drivers/libretro -I$LC/include -I$F -I$F/input -I$F/boards"
C="$W/src/console-simulator/core"; HSRC="$C/json_min.c $C/input.c $C/fb.c $C/gfx.c $C/font.c $C/home.c $C/persist.c $C/theme_arcade.c $C/theme_boxart.c $C/theme_shelf.c $C/theme_map.c $C/gfx3d.c"
P="$W/src/player/core"; PSRC="$P/gp_util.c $P/gp_save.c $P/gp_settings.c $P/gp_pads.c $P/gp_emu.c $P/gp_session.c $P/player_main.c"
LIBC_WRAP="-Wl,--wrap=__fstatat -Wl,--wrap=__fstatat_time64 -Wl,--wrap=__clock_gettime -Wl,--wrap=__clock_gettime64"   # the console targets: musl's stat and clock calls in Linux 3.4's form (core/console_libc.c)
HFLAGS="-std=c11 -O2 -Wall -Wextra -Wno-unused-parameter -Wno-misleading-indentation -ffp-contract=off"
rc=0
corelib() {   # target, extra flags -> $W/<target>/libfceumm.a (rebuilt only when the vendored sources change)
  local t="$1"; shift; local d="$W/$t"; mkdir -p "$d/obj"; local stamp; stamp=$(cat $FSRC | sha256sum | cut -c1-16)
  if [ -f "$d/libfceumm.a" ] && [ "$(cat "$d/stamp" 2>/dev/null)" = "$stamp" ]; then return 0; fi
  rm -f "$d/obj/"*.o; echo "== FCEUmm for $t"
  printf '%s\n' $FSRC | xargs -P 8 -I{} sh -c 'o="'"$d"'/obj/$(echo "{}" | sha1sum | cut -c1-12).o"; '"$ZIG"' cc '"$*"' -O2 -std=gnu99 -w -fno-strict-aliasing '"$FDEF $FINC"' -c "{}" -o "$o"' || return 1
  "$ZIG" ar rcs "$d/libfceumm.a" "$d/obj/"*.o && echo "$stamp" > "$d/stamp"; }
build() {   # name, target flags, host source, output, extra libs
  local name="$1" tf="$2" hostsrc="$3" exe="$4" libs="$5"
  corelib "$name" $tf || { echo "FAILED: FCEUmm for $name"; rc=1; return; }
  echo "== $exe"
  if "$ZIG" cc $tf $HFLAGS -I"$F/drivers/libretro" -I"$LC/include" -DCCS_PLAYER -o "$OUT/$exe" $PSRC $hostsrc $HSRC "$W/$name/libfceumm.a" $libs > "$OUT/$exe.build.log" 2>&1
  then echo "ok: $OUT/$exe ($(stat -c %s "$OUT/$exe") bytes, sha256 $(sha256sum "$OUT/$exe" | cut -c1-16))"; else echo "FAILED: $exe"; tail -30 "$OUT/$exe.build.log"; rc=1; fi; }
for t in $TARGETS; do case "$t" in
  windows) build win "-target x86_64-windows-gnu" "$P/host_win.c $W/src/player.rc" ccs-player.exe "-lgdi32 -luser32 -lwinmm -lole32 -ldinput8 -ldxguid -lshell32 -Wl,--subsystem,windows" ;;
  headless) build win "-target x86_64-windows-gnu" "$P/host_headless.c" ccs-player-headless.exe "-lshell32" ;;
  arm) build arm "-target arm-linux-musleabihf -mcpu=cortex_a7" "$P/host_headless.c" ccs-player-headless-armv7 "-static" ;;
  console) build armdev "-target arm-linux-musleabihf -mcpu=cortex_a7 -DCCS_CONSOLE" "$P/host_dev.c $C/devio.c $P/console_libc.c" ccs-player-armv7 "-static -s $LIBC_WRAP" ;;
  console-sym) build armdev "-target arm-linux-musleabihf -mcpu=cortex_a7 -DCCS_CONSOLE" "$P/host_dev.c $C/devio.c $P/console_libc.c" ccs-player-armv7.sym "-static $LIBC_WRAP" ;;
  console-x86) build linuxdev "-target x86_64-linux-musl -DCCS_CONSOLE" "$P/host_dev.c $C/devio.c" ccs-player-dev-x86_64 "-static" ;;
  web) PSRC="${PSRC/ $P\/player_main.c/}" build wasm "-target wasm32-wasi -D_WASI_EMULATED_MMAN -D_WASI_EMULATED_SIGNAL" "-DCCS_WEB -mexec-model=reactor $P/host_web.c" ccs-player.wasm "-lwasi-emulated-mman -lwasi-emulated-signal -Wl,-z,stack-size=8388608 -Wl,--strip-debug" ;;
  headless-wasm) build wasm "-target wasm32-wasi -D_WASI_EMULATED_MMAN -D_WASI_EMULATED_SIGNAL" "$P/host_headless.c" ccs-player-headless.wasm "-lwasi-emulated-mman -lwasi-emulated-signal -Wl,-z,stack-size=8388608 -Wl,--strip-debug" ;;
  *) echo "unknown target $t"; rc=1 ;; esac; done
exit $rc
