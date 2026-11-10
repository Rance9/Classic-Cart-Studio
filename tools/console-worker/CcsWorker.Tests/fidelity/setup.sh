#!/bin/bash
# Test only (CcsWorker.Tests): a disposable folder standing in for a console's root, where commands run with the console's own stock
# BusyBox 1.24.1 and hakchi's own module functions (b0000_defines, b0010_functions, b0030_package from the pinned hakchi.hmod, run by
# hakchi's BusyBox 1.29 and rsync), all ARM binaries under qemu-arm. Nothing here touches a device. Prints the folder.
set -e
QEMU="$HOME/.cache/ccs-sim/toolchain/deb/usr/bin/qemu-arm"
ROOTFS=/mnt/c/NES/work/h0/rootfs
INITRAMFS=/mnt/c/NES/work/h0/hakchi/initramfs
HK=$INITRAMFS/hakchi/rootfs/etc/preinit.d
[ -x "$QEMU" ] && [ -f "$ROOTFS/bin/busybox" ] && [ -f "$INITRAMFS/bin/busybox" ] && [ -f "$HK/b0030_package" ] || { echo "unavailable" >&2; exit 3; }
SYS=/tmp/ccs-hksys
if [ ! -e $SYS/lib/ld-linux-armhf.so.3 ]; then rm -rf $SYS; mkdir -p $SYS; cp -r $INITRAMFS/lib32 $SYS/lib; for f in $SYS/lib/*.symlink; do ln -s "$(cat "$f")" "${f%.symlink}"; done; fi
STOCK=/tmp/ccs-stocksys   # the stock root's libraries, and /etc/mtab as on the console (df finds its mount point there)
if [ ! -e $STOCK/etc/mtab ]; then rm -rf $STOCK; mkdir -p $STOCK/etc; ln -s $ROOTFS/lib $STOCK/lib; ln -s /proc/mounts $STOCK/etc/mtab; fi
SIM=$(mktemp -d /tmp/ccsfid-XXXXXXXX)
mkdir -p $SIM/.bin $SIM/.hkbin $SIM/var/lib/hakchi/rootfs $SIM/etc/init.d $SIM/tmp
printf '#!/bin/sh\n# stock (stand-in)\nclover-mcp\n' > $SIM/etc/init.d/S81clover-mcp
for a in $("$QEMU" -L $STOCK $ROOTFS/bin/busybox --list); do
  printf '#!/bin/sh\nexec %s -L %s %s/bin/busybox %s "$@"\n' "$QEMU" "$STOCK" "$ROOTFS" "$a" > $SIM/.bin/$a; chmod 755 $SIM/.bin/$a
done
for a in $("$QEMU" -L $SYS $INITRAMFS/bin/busybox --list); do
  printf '#!/bin/sh\nexec %s -L %s %s/bin/busybox %s "$@"\n' "$QEMU" "$SYS" "$INITRAMFS" "$a" > $SIM/.hkbin/$a; chmod 755 $SIM/.hkbin/$a
done
printf '#!/bin/sh\nexec %s -L %s %s/bin/rsync "$@"\n' "$QEMU" "$SYS" "$INITRAMFS" > $SIM/.hkbin/rsync; chmod 755 $SIM/.hkbin/rsync
# the console's "hakchi" command: like hakchi/rootfs/bin/hakchi it sources /etc/preinit and the preinit.d parts it needs, then calls the
# function by name; mountpoint and temppath put hakchi's own paths ($installpath, $rootfs) inside the folder
cat > $SIM/.bin/hakchi <<EOF
#!/bin/sh
exec $QEMU -L $SYS $INITRAMFS/bin/busybox ash -c 'PATH=$SIM/.hkbin; . $HK/../preinit; mountpoint=$SIM; . $HK/b0000_defines; temppath=$SIM/tmp; . $HK/b0010_functions; . $HK/b0030_package; "\$@"' hakchi "\$@"
EOF
chmod 755 $SIM/.bin/hakchi
echo $SIM
