#!/bin/bash
# Test only: runs one command file with the console's stock BusyBox 1.24.1 sh (qemu-arm) inside the disposable folder $1.
SIM="$1"; case "$SIM" in /tmp/ccsfid-*) ;; *) echo "not a fidelity folder" >&2; exit 125;; esac
QEMU="$HOME/.cache/ccs-sim/toolchain/deb/usr/bin/qemu-arm"; ROOTFS=/mnt/c/NES/work/h0/rootfs
cd "$SIM" && exec env -i PATH="$SIM/.bin" HOME=/ "$QEMU" -L /tmp/ccs-stocksys $ROOTFS/bin/busybox sh "$2"
