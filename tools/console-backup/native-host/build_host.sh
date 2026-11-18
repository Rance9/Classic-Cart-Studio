#!/usr/bin/env bash
# CCS Console Workbench native host: build the single-file executable for x64 and Arm64 Windows from pinned sources, in WSL with zig.
# Copyright (C) 2026 Rance9. GPL-3.0-or-later.
#
#   bash build_host.sh --payload <managed CCS-Console-Workbench.exe> --out <folder> [--arch x86_64|aarch64|all]
#                      [--name CCS-Console-Workbench] [--version N.N.N; default: repository VERSION] [--cache DIR] [--work DIR] [--measure] [--dll]
#   bash build_host.sh --dll-only --out DIR [--arch ...]      only ccs-par2.dll (Classic Cart Studio for Windows; no payload needed)
#
# --payload   the managed Workbench executable (.NET Framework 4.8, AnyCPU) that the host carries and runs from memory
# --out       where <out>/x64/<name>.exe and <out>/arm64/<name>.exe and <out>/build-record.json are written
# --cache     where the pinned par2cmdline release tarball is kept (downloaded once, checked by SHA-256 every build)
# --work      scratch folder for the extracted sources and objects (default ~/.cache/ccs-native-host/work, on WSL's own file system:
#             compiling on /mnt/c is several times slower)
# --measure   also link each architecture's host WITHOUT libpar2 (src/par2_stub.c) to measure what the PAR2 code adds
# --dll       also link <out>/<arch>/ccs-par2.dll: the same par2_abi.cpp and libpar2 objects for Classic Cart Studio for Windows, which
#             installs it beside the app (src/par2_dll.c supplies the architecture string the host otherwise gives)
#
# Pinned inputs (any difference stops the build):
#   zig 0.17.0 (cross-compiles C/C++ for x86_64-windows-gnu and aarch64-windows-gnu with its bundled mingw-w64, libc++, libc++abi,
#   libunwind; nothing is installed on the PC)
#   par2cmdline 1.4.0, the release tarball published by the Parchive project:
PAR2_VERSION="1.4.0"
PAR2_URL="https://github.com/Parchive/par2cmdline/releases/download/v1.4.0/par2cmdline-1.4.0.tar.gz"
PAR2_SHA256="f738242adf3bbfe0b041b3263e5be4d76e4b16a4e938952a38265efd94c7869d"
PAR2_COMMIT="3a27718ac77e638bd4ad5c2f14a2349759fcec15"   # tag v1.4.0 (annotated tag object 5d66a821f3ebb8df92b6edcddebc0d05cb5cd13d)
ZIG_VERSION="0.17.0"
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ZIG="${ZIG:-$HOME/.cache/ccs-sim/toolchain/zig/zig}"
PAYLOAD=""; OUT=""; ARCH="all"; NAME="CCS-Console-Workbench"; VERSION="$(tr -d '\r\n' < "$HERE/../../../VERSION")"; CACHE=""; WORK=""; MEASURE=0; DLL=0; EXE_ON=1

topath() { case "$1" in [A-Za-z]:\\*|[A-Za-z]:/*) wslpath -u "$1" ;; *) echo "$1" ;; esac; }
while [ $# -gt 0 ]; do
  case "$1" in
    --payload) PAYLOAD="$(topath "$2")"; shift 2 ;;
    --out) OUT="$(topath "$2")"; shift 2 ;;
    --arch) ARCH="$2"; shift 2 ;;
    --name) NAME="$2"; shift 2 ;;
    --version) VERSION="$2"; shift 2 ;;
    --cache) CACHE="$(topath "$2")"; shift 2 ;;
    --work) WORK="$(topath "$2")"; shift 2 ;;
    --measure) MEASURE=1; shift ;;
    --dll) DLL=1; shift ;;
    --dll-only) DLL=1; EXE_ON=0; shift ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
[ "$EXE_ON" = 0 ] || { [ -n "$PAYLOAD" ] && [ -f "$PAYLOAD" ]; } || { echo "--payload: the managed Workbench executable is required" >&2; exit 2; }
[ -n "$OUT" ] || { echo "--out is required" >&2; exit 2; }
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "--version must be N.N.N" >&2; exit 2; }
[[ "$NAME" =~ ^[A-Za-z0-9._-]+$ ]] || { echo "--name: letters, digits, dot, dash, underscore" >&2; exit 2; }
case "$ARCH" in all) ARCHES="x86_64 aarch64" ;; x86_64|aarch64) ARCHES="$ARCH" ;; *) echo "--arch x86_64|aarch64|all" >&2; exit 2 ;; esac
OUT="$(mkdir -p "$OUT" && cd "$OUT" && pwd)"
CACHE="${CACHE:-$OUT/../third_party-cache}"; mkdir -p "$CACHE"; CACHE="$(cd "$CACHE" && pwd)"
WORK="${WORK:-$HOME/.cache/ccs-native-host/work}"; mkdir -p "$WORK"; WORK="$(cd "$WORK" && pwd)"

# ---- toolchain ----
[ -x "$ZIG" ] || { echo "zig not found at $ZIG (set ZIG=...)" >&2; exit 3; }
ZV="$("$ZIG" version)"
[ "$ZV" = "$ZIG_VERSION" ] || { echo "zig $ZIG_VERSION is pinned; found $ZV" >&2; exit 3; }

# ---- par2cmdline source: download once, check every time, extract fresh ----
TARBALL="$CACHE/par2cmdline-$PAR2_VERSION.tar.gz"
if [ ! -f "$TARBALL" ]; then
  echo "downloading $PAR2_URL"
  curl -fsSL -o "$TARBALL.part" "$PAR2_URL"; mv "$TARBALL.part" "$TARBALL"
fi
GOT="$(sha256sum "$TARBALL" | cut -d' ' -f1)"
[ "$GOT" = "$PAR2_SHA256" ] || { echo "par2cmdline tarball SHA-256 $GOT differs from the pinned $PAR2_SHA256" >&2; exit 4; }
SRC="$WORK/par2cmdline-$PAR2_VERSION"
# only what the build and the notices use: the library sources, the Makefile that lists them, the license and the authors
rm -rf "$SRC"; mkdir -p "$WORK"
tar -xzf "$TARBALL" -C "$WORK" "par2cmdline-$PAR2_VERSION/src" "par2cmdline-$PAR2_VERSION/Makefile.am" "par2cmdline-$PAR2_VERSION/COPYING" "par2cmdline-$PAR2_VERSION/AUTHORS"
[ -f "$SRC/src/libpar2.h" ] || { echo "unexpected tarball layout" >&2; exit 4; }
# the library's sources exactly as par2cmdline's Makefile.am lists them for libpar2.a (no command-line front end, no tests)
LIBPAR2_SOURCES="crc creatorpacket criticalpacket datablock descriptionpacket diskfile filechecksummer galois mainpacket md5
  par1fileformat par1repairer par1repairersourcefile par2creator par2creatorsourcefile par2fileformat par2repairer par2repairersourcefile
  recoverypacket reedsolomon verificationhashtable verificationpacket libpar2 utf8"
for s in $LIBPAR2_SOURCES; do grep -q "src/$s.cpp" "$SRC/Makefile.am" || { echo "$s.cpp is not in libpar2_a_SOURCES" >&2; exit 4; }; done

# ---- generated files ----
GEN="$WORK/gen"; rm -rf "$GEN"; mkdir -p "$GEN"
# a private copy, so the bytes hashed are the bytes embedded even if the original is rebuilt meanwhile
PAYLOAD_SHA=""; PAYLOAD_BYTES=0
if [ "$EXE_ON" = 1 ]; then
  cp "$PAYLOAD" "$GEN/payload.bin"
  PAYLOAD_SHA="$(sha256sum "$GEN/payload.bin" | cut -d' ' -f1)"
  PAYLOAD_BYTES="$(stat -c %s "$GEN/payload.bin")"
  printf '%s' "$PAYLOAD_SHA" > "$GEN/payload.sha256"
fi
IFS=. read -r V1 V2 V3 <<< "$VERSION"
cat > "$GEN/host_version.h" <<EOF
/* generated by build_host.sh */
#define CCS_HOST_VERSION_W L"$VERSION"
#define CCS_PAR2_VERSION_W L"$PAR2_VERSION"
EOF
cp "$HERE/src/host.manifest" "$GEN/host.manifest"
cp "$HERE/../../publish/assets/app.ico" "$GEN/app.ico"
cat > "$GEN/host.rc" <<EOF
// generated by build_host.sh: the host's icon, manifest, version and the managed Workbench it carries
#include <windows.h>
1 ICON "app.ico"
1 24 "host.manifest"
101 RCDATA "payload.bin"
102 RCDATA "payload.sha256"
1 VERSIONINFO
FILEVERSION $V1,$V2,$V3,0
PRODUCTVERSION $V1,$V2,$V3,0
FILEFLAGSMASK 0x3fL
FILEFLAGS 0x0L
FILEOS 0x40004L
FILETYPE 0x1L
FILESUBTYPE 0x0L
BEGIN
  BLOCK "StringFileInfo"
  BEGIN
    BLOCK "040904b0"
    BEGIN
      VALUE "CompanyName", "Rance9 LLC"
      VALUE "FileDescription", "CCS Console Workbench"
      VALUE "FileVersion", "$VERSION"
      VALUE "InternalName", "$NAME"
      VALUE "LegalCopyright", "Copyright (C) 2026 Rance9. GPL-3.0-or-later; includes par2cmdline $PAR2_VERSION (GPL-2.0-or-later). Independent; not affiliated with Nintendo."
      VALUE "OriginalFilename", "$NAME.exe"
      VALUE "ProductName", "CCS Console Workbench"
      VALUE "ProductVersion", "$VERSION"
    END
  END
  BLOCK "VarFileInfo"
  BEGIN
    VALUE "Translation", 0x409, 1200
  END
END
EOF

# ---- compile and link per architecture ----
CXXFLAGS_PAR2=(-O2 -std=c++14 -DNDEBUG -include "$HERE/src/par2_prelude.h" '-DPACKAGE="par2cmdline"' "-DVERSION=\"$PAR2_VERSION\"" -ffunction-sections -fdata-sections -w)
CXXFLAGS_HOST=(-O2 -std=c++17 -DNDEBUG -include "$HERE/src/par2_prelude.h" -DUNICODE -D_UNICODE -ffunction-sections -fdata-sections -Wall -Wextra -Wno-unused-parameter
  -Wno-missing-field-initializers "-I$GEN" "-I$HERE/src" "-I$SRC/src" '-DPACKAGE="par2cmdline"' "-DVERSION=\"$PAR2_VERSION\"")
CORE="$HERE/../../console-simulator/core"
CXXFLAGS_HOST+=("-I$CORE")
CFLAGS_HOST=(-O2 -std=c11 -DNDEBUG -DUNICODE -D_UNICODE -ffunction-sections -fdata-sections -Wall -Wextra -Wno-unused-parameter
  -Wno-missing-field-initializers "-I$GEN" "-I$HERE/src" "-I$CORE")
# the native HOME renderer (tools/console-simulator/core) for the HOME Designer's render check, linked into the host only (ABI version 2);
# the same sources and flags as its own builds (no main.c, device.c or devio.c), so its frames are the same bytes
CORE_SOURCES="json_min fb gfx font home persist theme_arcade theme_boxart theme_shelf theme_map gfx3d input model design design_check"
CFLAGS_CORE=(-std=c11 -O2 -Wall -Wextra -Wno-unused-parameter -Wno-misleading-indentation -ffp-contract=off)
LDFLAGS=(-municode -Wl,--subsystem,windows -Wl,--gc-sections -s
  -lole32 -loleaut32 -lshell32 -ladvapi32 -lbcrypt -luser32)

# reproducible output: the PE header's link time is the only difference between two links of the same inputs; it is replaced by the
# first four bytes of the SHA-256 of the file with that field zeroed (the idea of MSVC's /Brepro). The PE checksum field is 0 (unused).
normalize_pe() {
  local f="$1" pe h
  pe=$(od -An -tu4 -j60 -N4 "$f" | tr -d ' ')
  printf '\0\0\0\0' | dd of="$f" bs=1 seek=$((pe + 8)) conv=notrunc status=none
  h=$(sha256sum "$f" | cut -c1-8)
  printf "\x${h:0:2}\x${h:2:2}\x${h:4:2}\x${h:6:2}" | dd of="$f" bs=1 seek=$((pe + 8)) conv=notrunc status=none
}

RECORD_ARCH=""
for A in $ARCHES; do
  T="$A-windows-gnu"; case "$A" in x86_64) D=x64 ;; aarch64) D=arm64 ;; esac
  OBJ="$WORK/obj-$D"; rm -rf "$OBJ"; mkdir -p "$OBJ" "$OUT/$D"
  echo "== $D: libpar2 ($PAR2_VERSION)"
  for s in $LIBPAR2_SOURCES; do
    "$ZIG" c++ -target "$T" "${CXXFLAGS_PAR2[@]}" -c "$SRC/src/$s.cpp" -o "$OBJ/par2-$s.o" &
  done
  wait
  for s in $LIBPAR2_SOURCES; do [ -f "$OBJ/par2-$s.o" ] || { echo "libpar2 $s failed to compile for $D" >&2; exit 5; }; done
  echo "== $D: host"
  "$ZIG" c++ -target "$T" "${CXXFLAGS_HOST[@]}" -c "$HERE/src/par2_abi.cpp" -o "$OBJ/par2_abi.o"   # the table without the renderer (the DLL)
  DLL_JSON=""
  if [ "$DLL" = 1 ]; then
    "$ZIG" cc -target "$T" "${CFLAGS_HOST[@]}" -c "$HERE/src/par2_dll.c" -o "$OBJ/par2_dll.o"
    DLLF="$OUT/$D/ccs-par2.dll"; rm -f "$DLLF"
    "$ZIG" c++ -target "$T" -shared "$OBJ/par2_dll.o" "$OBJ/par2_abi.o" "$OBJ"/par2-*.o -Wl,--gc-sections -s -lshell32 -ladvapi32 -lbcrypt -o "$DLLF"
    rm -f "$OUT/$D/"*.lib "$OUT/$D/"*.pdb
    normalize_pe "$DLLF"
    DLL_JSON=",\"dll\":\"$D/ccs-par2.dll\",\"dll_bytes\":$(stat -c %s "$DLLF"),\"dll_sha256\":\"$(sha256sum "$DLLF" | cut -d' ' -f1)\""
    echo "   $DLLF  $(stat -c %s "$DLLF") bytes"
  fi
  if [ "$EXE_ON" = 0 ]; then RECORD_ARCH="$RECORD_ARCH{\"arch\":\"$D\",\"target\":\"$T\"$DLL_JSON},"; continue; fi
  "$ZIG" cc -target "$T" "${CFLAGS_HOST[@]}" -c "$HERE/src/host_main.c" -o "$OBJ/host_main.o"
  echo "== $D: HOME renderer"
  for s in $CORE_SOURCES; do "$ZIG" cc -target "$T" "${CFLAGS_CORE[@]}" "-DCCS_BUILD_TARGET=\"windows-$D-host\"" -c "$CORE/$s.c" -o "$OBJ/core-$s.o"; done
  "$ZIG" c++ -target "$T" "${CXXFLAGS_HOST[@]}" -DCCS_WITH_HOME -c "$HERE/src/par2_abi.cpp" -o "$OBJ/par2_abi_home.o"
  EXE="$OUT/$D/$NAME.exe"; rm -f "$EXE"
  (cd "$GEN" && "$ZIG" c++ -target "$T" "$OBJ/host_main.o" "$OBJ/par2_abi_home.o" "$OBJ"/par2-*.o "$OBJ"/core-*.o host.rc "${LDFLAGS[@]}" -o "$EXE")
  rm -f "$OUT/$D/"*.lib "$OUT/$D/"*.pdb   # the import library lld writes for the exported ccs_host_get_abi is not needed
  normalize_pe "$EXE"
  SIZE="$(stat -c %s "$EXE")"; SHA="$(sha256sum "$EXE" | cut -d' ' -f1)"
  STUB_SIZE=0
  if [ "$MEASURE" = 1 ]; then
    "$ZIG" cc -target "$T" "${CFLAGS_HOST[@]}" -c "$HERE/src/par2_stub.c" -o "$OBJ/par2_stub.o"
    (cd "$GEN" && "$ZIG" cc -target "$T" "$OBJ/host_main.o" "$OBJ/par2_stub.o" host.rc "${LDFLAGS[@]}" -o "$OBJ/$NAME-without-par2.exe")
    STUB_SIZE="$(stat -c %s "$OBJ/$NAME-without-par2.exe")"
  fi
  echo "   $EXE  $SIZE bytes  sha256 $SHA"
  RECORD_ARCH="$RECORD_ARCH{\"arch\":\"$D\",\"target\":\"$T\",\"exe\":\"$D/$NAME.exe\",\"bytes\":$SIZE,\"sha256\":\"$SHA\",\"without_par2_bytes\":$STUB_SIZE$DLL_JSON},"
done

cat > "$OUT/build-record.json" <<EOF
{"schema":"ccs-native-host-build/1","name":"$NAME","version":"$VERSION","zig":"$ZV",
 "par2cmdline":{"version":"$PAR2_VERSION","url":"$PAR2_URL","sha256":"$PAR2_SHA256","commit":"$PAR2_COMMIT"},
 "payload":{"file":"$(basename "$PAYLOAD")","bytes":$PAYLOAD_BYTES,"sha256":"$PAYLOAD_SHA"},
 "outputs":[${RECORD_ARCH%,}]}
EOF
echo "== build record: $OUT/build-record.json"
