# The backup session: read-only chain audit

What runs on the console during **Back Up Console**, from the moment the PC sends the backup image into the console's RAM until the
console is restarted or unplugged, and why none of it writes the console's storage. Source: the built image's own files
(`ram/build_images.py` records every change in the image's MANIFEST.json), the pinned Hakchi2 CE 3.9.3 recovery image
(`hakchi.hmod` boot/boot.img, SHA-256 `9e921c32…`), and the PC side (`CcsBackup`). Checked by `ram/test_recovery_images.py` (static audit
and runs on the image's own BusyBox with write tripwires) and `CcsBackup.Tests` (gate, capture, faults, lifecycle).

## 1. Loading (PC → console RAM)

The first-party FEL loader (`tools/console-loader/CcsConsole`, Hakchi2 CE's memboot sequence) writes fes1, u-boot and the boot image into
RAM over FEL and runs `boota 47400000`. No flash command exists in that code; any other u-boot command is refused. Nothing is written to
NAND. (Gate.ConsoleSessions is closed in this version: the loader is not reached.)

## 2. Kernel and `/init` (hakchi's, two reviewed changes)

| Step | Effect | Writes storage? |
|---|---|---|
| kernel 3.4.113.29-madmonkey, command line byte-identical (`root=/dev/nandb decrypt ro … hakchi-shell`) | — | No |
| busybox applet links in `/bin`; devtmpfs, proc, sysfs, devpts, tmpfs mounted | RAM file systems | No |
| `insmod nand.ko` | the console's NAND driver loads (the same driver Nintendo's system loads at every start) | Not by any command; the driver's internal flash-translation maintenance is the driver's own and outside what any program controls |
| key file from the command line, `sunxi-flash cmdline`, or the stock kernel's ramdisk (`sunxi-flash ramdisk … \| cpio -iu`) | extracted into RAM | No (reads) |
| `cryptsetup open … --readonly` (the command line has `ro`) | a read-only mapping of the system partition | No |
| system squashfs mounted `ro,noatime` at `/newroot` | read-only file system | No |
| ~~SEGA fallback `mount -o ro,noatime /dev/nandd`~~ | **removed** by the builder (on an NES Classic nandd is the private storage; a mount could replay a journal) | — |
| `sh /hakchi/init` | **replaced** by `ccs-init` (below) | — |
| ~~`mount --move …; exec switch_root /newroot /sbin/init`~~ | **replaced** by `exec /ccs/bin/ccs-idle`: the normal system never starts from this image, whatever happened before | — |

## 3. `ccs-init` (replaces hakchi's `/hakchi/init`)

hakchi's own init sources `script/config`, `script/base` and `script/shell`; its default path calls `mount_base`, which mounts the data
storage **read-write** (the cause of the 2026-10-09 deviation in the dump tool). `ccs-init` sources none of them. It:
1. reads `/ccs/env` (on failure: straight to ccs-idle);
2. marks every NAND block device read-only (`blockdev --setro`; an extra guard whose enforcement is the kernel's: nothing writes either way);
3. sets the USB port to clovershell (1F3A:EFE8, function `clover`: sysfs, RAM) and starts hakchi's clovershell daemon;
4. shows a status picture on the TV (`decodepng` to the framebuffer);
5. `exec`s ccs-idle (waits in RAM forever).
No service, network, installer, module or game starts. Removed from the image: `S92rndis` (USB network).

## 4. What the PC asks (`ccsbr`, backup image variant)

The backup image's `ccsbr` is built **without** its write section (the builder removes it and refuses to build an image whose backup helper
still contains `burn_boot`, `of=`, `--setrw` or `conv=fsync`). The PC sends only fixed verbs with target names from a fixed list
(`RecoveryConsole`; never a path, device or shell text):

| Verb | Reads | Writes |
|---|---|---|
| `identify` | `uname`, the SID (`sntool devmem`), `sunxi-flash nandinfo`, `/proc/partitions`, the storage table (`sunxi-part` with no argument prints it), board/region/version files on the read-only system, `/proc/mounts`, `blockdev --getro`, `read_boot2`, `read_boot2 68`, the active kernel's ramdisk listing | RAM only (`/tmp/ccsbr`) |
| `read TARGET` | `sunxi-flash read_boot0/1/2 [68]`, `dd if=/dev/nandX`, `sntool sunxi_flash phy_read 0`, `sunxi-part` | RAM only (its own MD5 and byte count) |
| `sum`, `digest` | the recorded check; a second read | RAM only |
| `card` | a picture to the framebuffer | No |
| `finish` | — | No (a restart into the normal start, after `sync`; nothing is mounted to sync) |

The PC refuses to start reading when the console reports any storage device mounted other than the read-only system mapping, an
unexpected program, layout, model or identity (`Support.CheckForBackup`).

## 5. Ending

- **Normal end:** the PC asks `finish` (the console restarts into its normal start, as Hakchi2 CE does after its dumps) or the person
  unplugs the USB cable.
- **Window closed, window or worker ended, cable pulled, power lost:** the console stays in ccs-idle (or loses power). No cleanup path
  exists on the console, so none can write; the PC's unfinished file keeps its `.partial` name (FORMAT.md).

## Not established here

The NAND driver's internal behavior on reads, the console's USB gadget behavior and the real timing are hardware facts: they are observed
only in the separately approved first backup session (the owner's qualification packet). QEMU runs show what the image's own scripts and
tools do with this text, not what the console's hardware does.
