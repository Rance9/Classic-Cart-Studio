#!/bin/bash
# Console Simulator toolchain setup (runs inside the WSL Ubuntu distribution; no root, no sudo, no system-wide change).
#   zig (pinned, SHA-256 verified) -> $CCS_SIM_TOOLCHAIN/zig
#   qemu-user and libc6-armhf-cross (apt-get download = signed index, dpkg-deb -x) -> $CCS_SIM_TOOLCHAIN/deb
# Everything lands under one user-owned prefix (default ~/.cache/ccs-sim/toolchain); `clean --toolchain` removes it.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; LOCK="$HERE/../toolchain.lock.json"
TC="${CCS_SIM_TOOLCHAIN:-$HOME/.cache/ccs-sim/toolchain}"; mkdir -p "$TC/dl" "$TC/deb"
j() { python3 -c "import json,sys; d=json.load(open('$LOCK')); print(eval('d'+sys.argv[1]))" "$1"; }
ZV=$(j "['zig']['version']"); ZU=$(j "['zig']['linux_x86_64']['url']"); ZS=$(j "['zig']['linux_x86_64']['sha256']")
echo "== toolchain prefix: $TC"
if [ ! -x "$TC/zig/zig" ]; then
  f="$TC/dl/zig-x86_64-linux-$ZV.tar.xz"
  if [ ! -f "$f" ]; then echo "downloading zig $ZV"; curl -fsSL -o "$f.part" "$ZU" && mv "$f.part" "$f"; fi
  echo "$ZS  $f" | sha256sum -c - >/dev/null && echo "zig archive SHA-256 verified" || { echo "zig archive SHA-256 MISMATCH: refusing to install"; rm -f "$f"; exit 2; }
  rm -rf "$TC/zig.tmp" && mkdir -p "$TC/zig.tmp" && tar -xJf "$f" -C "$TC/zig.tmp" --strip-components=1 && mv "$TC/zig.tmp" "$TC/zig"
  echo "zig installed: $("$TC/zig/zig" version)"
else echo "zig present: $("$TC/zig/zig" version)"; fi
for pkg in qemu-user libc6-armhf-cross; do
  if [ ! -f "$TC/deb/.$pkg.done" ]; then
    echo "fetching $pkg (apt-get download, no root)"; ( cd "$TC/dl" && rm -f ${pkg}_*.deb && apt-get download "$pkg" >/dev/null )
    deb=$(ls "$TC/dl"/${pkg}_*.deb | head -1); ver=$(dpkg-deb -f "$deb" Version)
    want=$(j "['debian_packages']['$pkg']['version']")
    if [ "$ver" != "$want" ]; then echo "WARNING: $pkg version $ver differs from the lock ($want); recording the actual version"; fi
    dpkg-deb -x "$deb" "$TC/deb"; sha=$(sha256sum "$deb" | cut -c1-64); echo "$pkg $ver sha256=$sha" > "$TC/deb/.$pkg.done"; echo "$pkg $ver extracted (deb sha256 $sha)"
  else echo "$(cat "$TC/deb/.$pkg.done") (present)"; fi
done
Q="$TC/deb/usr/bin/qemu-arm"; [ -x "$Q" ] || { echo "qemu-arm missing after extraction"; exit 3; }
echo "qemu-arm: $("$Q" --version | head -1)"
python3 - "$TC" <<'EOF'
import json, os, subprocess, sys, datetime
tc = sys.argv[1]
def run(*a):
    try: return subprocess.run(a, capture_output=True, text=True, timeout=60).stdout.strip().splitlines()[0]
    except Exception as e: return f"error: {e}"
m = {"schema": "ccs-sim-toolchain-manifest/1", "generated": datetime.datetime.now(datetime.timezone.utc).isoformat(), "prefix": tc,
     "zig": run(os.path.join(tc, "zig", "zig"), "version"), "qemu_arm": run(os.path.join(tc, "deb", "usr", "bin", "qemu-arm"), "--version"),
     "gcc": run("gcc", "--version"), "packages": {n.split(".")[1].replace(".done", ""): open(os.path.join(tc, "deb", n)).read().strip() for n in os.listdir(os.path.join(tc, "deb")) if n.endswith(".done")},
     "armhf_sysroot": os.path.join(tc, "deb", "usr", "arm-linux-gnueabihf")}
json.dump(m, open(os.path.join(tc, "manifest.json"), "w"), indent=1); print("manifest written:", os.path.join(tc, "manifest.json"))
EOF
echo "setup complete (no root used; nothing outside $TC was changed)"
