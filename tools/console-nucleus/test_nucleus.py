"""Tests of CCS Mod and CCS Nucleus (tests only; no console, no USB, nothing runs on a real console). Copyright (C) 2026 Rance9,
GPL-3.0-or-later.

  python test_nucleus.py --hakchi-release REL      (REL: the pinned Hakchi2 CE 3.9.3 release folder or its portable package .zip)

Windows (WSL Ubuntu with the project's qemu-arm) or Linux. Two kinds of check:
1. On the PC (Python): the two packages build reproducibly and carry only Classic Cart Studio's own files; the registry and journal
   formats; the static rules (no hakchi transfer/firmware branch, nothing that writes a kernel, bootloader or storage table, no exit or
   return at the top of a p-file); where the hooks sort among the pinned hakchi start files.
2. Under qemu-arm with the PINNED image's own BusyBox 1.29 (the shell hakchi uses on the console), in a disposable folder in WSL:
   a stand-in console built from the pinned hakchi files (its rootfs, preinit, preinit.d, script/removed, rcS, the hakchi command), a
   stand-in Nintendo system folder (two start scripts written for this test, no Nintendo file), a stand-in game, saves and CCS HOME
   package. The console's storage, display and power tools are logging stand-ins; flash tools are TRIPWIRES. What runs is hakchi's own
   code: its preinit (every b- and p-file, ours among them, in its real order), its pack_install / pack_uninstall (the .hmod files
   exactly as built), its rcS (the start scripts in name order). The test copies of hakchi's files differ from the pinned ones only in
   the absolute paths that name the console's RAM root (checked by mapping them back: byte for byte the pinned files), because a test
   cannot move mounts or write the PC's own /etc and /tmp. overlayfs is emulated for rcS by a merged start-script folder.
   BusyBox semantics matter everywhere here, so the host's shells run only the stand-ins written for this test (bash: the logging tool
   stand-ins; sh: the stand-in player). Every CCS script, every hakchi script and every start script (their first line is pointed at
   the image's BusyBox) runs on the pinned BusyBox.
QEMU is not the console: this shows what the scripts and hakchi's own code do with these files, not the hardware, the real Nintendo menu,
real third-party packages or timing. Runtime on real hardware is unknown."""
from __future__ import annotations
import argparse, gzip, hashlib, io, json, lzma, os, re, shutil, subprocess, sys, tarfile, tempfile, time
HERE = os.path.dirname(os.path.abspath(__file__)); TOOLS = os.path.dirname(HERE)
sys.path.insert(0, HERE); sys.path.insert(0, os.path.join(TOOLS, "console-backup", "ram"))
import build_packages as BP, formats as F, capacity as CAP   # noqa: E402
import build_images as B   # noqa: E402  (the pinned hakchi inputs: read_inputs, boot image and cpio readers)

QEMU = None; R = None; WORK = None
passed = failed = 0; failures = []
def check(ok: bool, what: str):
    global passed, failed
    if ok: passed += 1
    else: failed += 1; failures.append(what)
    print(("ok   " if ok else "FAIL ") + what, flush=True)

def wpath(p: str) -> str:
    p = os.path.abspath(p)
    return "/mnt/" + p[0].lower() + p[2:].replace("\\", "/") if os.name == "nt" else p

_n = 0
def sh(text: str, timeout: int = 600) -> str:
    """a bash script in WSL (written to a file, as the project's WSL tests do), @R@ = the disposable folder; its stdout"""
    global _n
    _n += 1; f = os.path.join(WORK, f"s{_n:04d}.sh")
    open(f, "w", newline="\n").write(text.replace("@R@", R or "").replace("@Q@", QEMU or ""))
    cmd = ["wsl.exe", "-d", "Ubuntu", "--", "bash", wpath(f)] if os.name == "nt" else ["bash", f]
    r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout)
    return r.stdout + (("\n[stderr] " + r.stderr[-3000:]) if os.environ.get("CCSTEST_DEBUG") and r.stderr else "")

def bb(cmd: str, env: str = "", mp: str = "") -> str:
    """cmd run by the image's own BusyBox sh (qemu-arm), the stand-ins first on PATH and nothing else of WSL's; CCS_ROOT the stand-in"""
    q = cmd.replace("'", "'\\''")
    return sh(f"cd '@R@' && env -i HOME=/ PATH='@R@/stubs:@R@/wrap' CCS_ROOT='@R@/newroot' mountpoint='{mp}' {env} '@Q@' -L '@R@/root' '@R@/root/bin/busybox' sh -c '{q}' 2>&1")

HKR = "@R@/newroot/var/lib/hakchi/rootfs"
def nuc(args: str, env: str = "") -> str: return bb(f"sh {HKR}/bin/ccs-nucleus {args}", env)
def mod(args: str, env: str = "") -> str: return bb(f"sh {HKR}/bin/ccs-mod {args}", env)
def hakchi(args: str, env: str = "") -> str: return bb(f"hakchi {args}", env)
def result(out: str) -> str: return next((l for l in reversed(out.splitlines()) if l.startswith("result ")), "(no result) " + out.strip()[-300:])
def log() -> str: return sh("cat '@R@/log' 2>/dev/null")
def clear_log(): sh(": > '@R@/log'")
def trips() -> list[str]: return [l for l in log().splitlines() if l.startswith("TRIPWIRE")]

# ---------------------------------------------------------------- stand-ins
STUBS = r'''#!/bin/bash
# the console's storage, display and power tools as logging stand-ins (tests only)
R="__R__"; Q="__Q__"; L="$R/log"; n=$(basename "$0")
trip(){ echo "TRIPWIRE $n $*" >> "$L"; exit 1; }
case "$n" in
  mount)
    [ $# -eq 0 ] && { cat "$R/loops.mount" 2>/dev/null; exit 0; }
    case "$*" in
      "-o loop,ro,noatime "*) f="$3"; echo "LOOPMOUNT $f" >> "$L"
        [ -f "$R/attempt-snap" ] || { cat "$R/newroot/var/lib/ccs/nucleus/state/attempt" 2>/dev/null | grep -E '^(id|stage)=' | tr '\n' ' ' > "$R/attempt-snap"; }
        [ "$CCSTEST_LOOP_FAIL" = 1 ] && { echo "LOOPMOUNT-FAILED" >> "$L"; exit 1; }
        [ -f "$R/newroot$f" ] || { echo "LOOPMOUNT-NOFILE" >> "$L"; exit 1; }
        echo "/dev/loop0: 0 $f" > "$R/loops"; echo "/dev/loop0 on $4 type squashfs (ro)" > "$R/loops.mount"; exit 0 ;;
    esac
    case "$*" in *nand*|*mmc*|*/dev/sd*|*mapper*) trip "$@" ;; esac
    echo "mount $*" >> "$R/mounts.log"; exit 0 ;;
  umount) exit 0 ;;
  mountpoint) [ "$1" = -q ] && shift; grep -qxF "$1" "$R/mounted" 2>/dev/null; exit $? ;;
  losetup) cat "$R/loops" 2>/dev/null; exit 0 ;;
  lsmod|lsusb|modprobe|insmod|rmmod|decodepng|aplay|udevadm|blockdev|sync) exit 0 ;;
  uname) case "$1" in -r) echo 3.4.113.29-madmonkey ;; *) echo "Linux stand-in 3.4.113.29-madmonkey armv7l" ;; esac; exit 0 ;;
  lsof) if [ -f "$R/fb-held" ]; then h=$(cat "$R/fb-held"); if [ "$h" = always ]; then echo "123 /dev/fb0"; elif [ "$h" -gt 0 ] 2>/dev/null; then echo $((h - 1)) > "$R/fb-held"; echo "123 /dev/fb0"; fi; fi; exit 0 ;;
  sntool) case "$1" in isRecovery|ismd|mdui) exit 1 ;; devmem) exit 0 ;; esac; trip "$@" ;;
  poweroff) echo "POWEROFF $*" >> "$L"; kill -9 $PPID; exit 0 ;;
  hakchi) case "$1" in reboot) echo "REBOOT" >> "$L"; kill -9 0; exit 0 ;; reboot_kexec|memboot|recovery|shutdown) trip "$@" ;; esac
          exec env mountpoint="$R/newroot" temppath="$R/tmp" "$Q" -L "$R/root" "$R/root/bin/busybox" sh "$R/hk/hakchi" "$@" ;;
  mv) if [ -n "$CCSTEST_KILL_MV" ]; then for a in "$@"; do case "$a" in *.new.*|*.candidate.*) ;; *"$CCSTEST_KILL_MV"*) echo "KILLED-AT-MV $*" >> "$L"; kill -9 $PPID; exit 1 ;; esac; done; fi
      exec "$Q" -L "$R/root" "$R/root/bin/busybox" mv "$@" ;;
  # hakchi's restart after applying its transfer folder (p7080_hmods -> reboot_kexec) first reads the running kernel: logged, never read
  sunxi-flash) [ "$1" = read_boot2 ] && [ $# -eq 1 ] && { echo "RESTART reboot_kexec" >> "$L"; exit 1; }; trip "$@" ;;
  sunxi-part|kexec|mke2fs|e2fsck|tune2fs|resize2fs|fsck|getty|cryptsetup|switch_root|reboot|dd|flash_erase|nandwrite) trip "$@" ;;
esac
trip "$@"
'''
STUB_NAMES = ("mount umount mountpoint losetup lsmod lsusb modprobe insmod rmmod decodepng aplay udevadm blockdev sync uname lsof sntool "
              "poweroff hakchi mv sunxi-flash sunxi-part kexec mke2fs e2fsck tune2fs resize2fs fsck getty cryptsetup switch_root reboot dd "
              "flash_erase nandwrite").split()

PLAYER = r'''#!/bin/sh
# stand-in for CCS HOME's console player (tests only): records how it was started
R="__R__"
echo "PLAYER $*" >> "$R/log"
case "$*" in *--startup*) echo "HOME startup" >> "$R/log" ;; *) echo "HOME menu" >> "$R/log" ;; esac
[ -f "$R/player-sleep" ] && sleep "$(cat "$R/player-sleep")"
rc=0; [ -f "$R/player-rc" ] && rc=$(cat "$R/player-rc")
exit $rc
'''
CHOOSER = r'''#!/bin/sh
# stand-in for the chooser mode of CCS HOME's native HOME program (tests only; the real program runs in chooser_real): records how it was
# started and answers from the test's list (an id: "choice ID a"; or "back" / "action KEY" as written); with no list it ends without an
# answer, as a chooser that cannot run
R="__R__"
echo "CHOOSER $*" >> "$R/log"
cp "$2" "$R/last-choices" 2>/dev/null
if [ -s "$R/picks" ]; then a=$(head -n 1 "$R/picks"); tail -n +2 "$R/picks" > "$R/picks.n"; mv "$R/picks.n" "$R/picks"
  case "$a" in back|action*) echo "$a" ;; *) echo "choice $a a" ;; esac; exit 0; fi
echo "no answer" >&2; exit 2
'''
STOCK_INITD = {   # stand-ins for the console system's own start scripts (written for this test; the real ones are Nintendo's)
    "S80test-drivers": '#!/bin/sh\ncase "$1" in start) echo "S80 drivers" >> "$CCSTEST_LOG" ;; esac\n',
    "S81clover-mcp": '#!/bin/sh\ncase "$1" in start) echo "S81clover-mcp the console menu" >> "$CCSTEST_LOG" ;; esac\n',
}
FOREIGN = {   # a stand-in third-party package, written for this test: copies its files with transfer_default, then returns 1, as real ones do
    "install": "# a stand-in third-party hakchi package (tests only)\ntransfer_default\nreturn 1\n",
    "readme.txt": "stand-in package\n",
    "bin/foreign-tool": "#!/bin/sh\necho foreign tool\n",
    "etc/init.d/S95foreign-autostart": '#!/bin/sh\ncase "$1" in start) echo "S95 foreign autostart" >> "$CCSTEST_LOG" ;; esac\n',
    "etc/preinit.d/p8000_foreign": 'echo "p8000 foreign preinit" >> "$mountpoint/../log"\n',
}

# the test copies of hakchi's files: (old, new) replacements of absolute RAM-root paths only (@R@: the disposable folder)
HKRP = "@R@/newroot/var/lib/hakchi/rootfs"
MAPS = {
    "etc/preinit": [('"/etc/preinit.d', '"' + HKRP + '/etc/preinit.d'), ('[ -z "$temppath" ] && temppath="/tmp"', '[ -z "$temppath" ] && temppath="@R@/tmp"')],
    "etc/preinit.d/b0000_defines": [('modpath="/$modname"', 'modpath="@R@/root/hakchi"'), ('temppath="/tmp"', 'temppath="@R@/tmp"')],
    "etc/preinit.d/b0030_package": [('"/etc/hmodblacklist"', '"' + HKRP + '/etc/hmodblacklist"')],
    # remount_root: after "mount --move" the image is at /var/...; a test cannot move mounts, so the same file is named under the stand-in
    "etc/preinit.d/b0010_functions": [('[ -f "$loopfile" ] || loopfile=', '[ -f "$mountpoint$loopfile" ] || loopfile='), ('local preinitf="/${preinit##$mountpoint/}"', 'local preinitf="$preinit"')],
}
HAKCHI_CMD_MAP = [("source /etc/preinit", "source " + HKRP + "/etc/preinit")]
RCS_MAP = [('mkdir -p "/var/cache"', 'mkdir -p "@R@/run/cache"'), ('cd "/var/cache"', 'cd "@R@/run/cache"'), ("for i in /etc/init.d/S??*", "for i in @R@/etc-merged/init.d/S??*")]

def mapped(text: str, pairs, Rp: str) -> tuple[str, list[str]]:
    bad = []; t = text
    for old, new in pairs:
        new = new.replace("@R@", Rp)
        if old not in text or new in text: bad.append(old)
        t = t.replace(old, new)
    back = t
    for old, new in pairs: back = back.replace(new.replace("@R@", Rp), old)
    if back != text: bad.append("does not map back")
    return t, bad

def tar_add(t: tarfile.TarFile, name: str, data: bytes | None = None, mode: int = 0o644, link: str | None = None, d: bool = False):
    ti = tarfile.TarInfo(name); ti.mtime = BP.MTIME; ti.mode = mode
    if d: ti.type = tarfile.DIRTYPE; ti.mode = 0o755; t.addfile(ti)
    elif link is not None: ti.type = tarfile.SYMTYPE; ti.linkname = link; t.addfile(ti)
    else: ti.size = len(data); t.addfile(ti, io.BytesIO(data))

def rd_entries(inp) -> list[dict]:
    orig = B.bootimg_parse(inp["bootimg"]); return B.cpio_parse(lzma.decompress(orig["ramdisk"]))[:-1]

def ramdisk_tar(ents) -> bytes:
    o = io.BytesIO()
    with tarfile.open(fileobj=o, mode="w") as t:
        for e in ents:
            n, m = e["name"], e["mode"]
            if n in ("", "."): continue
            if m & 0o170000 == 0o040000: tar_add(t, n, d=True)
            elif m & 0o170000 == 0o120000: tar_add(t, n, link=e["data"].decode())
            elif m & 0o170000 == 0o100000: tar_add(t, n, e["data"], m & 0o7777)
    return o.getvalue()

PKGV = "0123456789abcdef.0a1b2c3d"
def standin_tar(ents, Rp: str) -> tuple[bytes, list[str]]:
    """the stand-in console (its data storage and Nintendo-system folder), as a tar to unpack at <R>/pristine"""
    by = {e["name"]: e for e in ents}; o = io.BytesIO(); bad = []
    with tarfile.open(fileobj=o, mode="w") as t:
        base = "newroot/var/lib/hakchi/rootfs/"
        for e in ents:
            n = e["name"]
            if not n.startswith("hakchi/rootfs/") or n == "hakchi/rootfs/": continue
            rel = n[len("hakchi/rootfs/"):]; m = e["mode"]
            if m & 0o170000 == 0o040000: tar_add(t, base + rel, d=True)
            elif m & 0o170000 == 0o120000: tar_add(t, base + rel, link=e["data"].decode())
            else:
                data = e["data"]
                if rel in MAPS:
                    txt, b = mapped(data.decode(), MAPS[rel], Rp); bad += [f"{rel}: {x}" for x in b]; data = txt.encode()
                tar_add(t, base + rel, data, m & 0o7777)
        tar_add(t, base + "etc/preinit.d/p0000_config", b"#!/bin/sh\n\ncfg_boot_logo=''\ncfg_boot_stock='n'\ncfg_disable_armet='y'\ncfg_firmware='auto'\ncfg_usb_host='y'\ncfg_usb_rw='n'\ncfg_wood='n'\n")
        tar_add(t, base + "lib/modules/3.4.113.29-madmonkey/extra/clovercon.ko", b"stand-in module\n")
        for d in ("newroot/media", "newroot/var/lib/hakchi/firmware", "newroot/var/lib/hakchi/hmod", "newroot/var/lib/clover/profiles/0/hakchi"):
            tar_add(t, d, d=True)
        tar_add(t, "newroot/var/squashfs/etc/clover/boardtype", b"dp-nes\n"); tar_add(t, "newroot/var/squashfs/etc/clover/REGION", b"EUR_USA\n")
        for k, v in STOCK_INITD.items(): tar_add(t, "newroot/var/squashfs/etc/init.d/" + k, v.encode(), 0o755)
        tar_add(t, "newroot/var/lib/hakchi/games/000/CLV-H-AAAAA/CLV-H-AAAAA.desktop", b"[Desktop Entry]\nName=Stand-in Game\n")
        tar_add(t, "newroot/var/lib/hakchi/games/000/CLV-H-AAAAA/CLV-H-AAAAA.nes", b"NES\x1a" + bytes(range(256)) * 64)
        tar_add(t, "newroot/var/lib/clover/profiles/0/CLV-H-AAAAA/cartridge.sav", bytes(range(256)) * 32)
        p = f"newroot/var/lib/ccs/pkg/{PKGV}/"
        tar_add(t, p + "bin/ccs-player-armv7", PLAYER.replace("__R__", Rp).encode(), 0o755)
        tar_add(t, p + "home/bin/ccs-home-armv7", CHOOSER.replace("__R__", Rp).encode(), 0o755)
        tar_add(t, p + "session.json", b'{"schema": "ccs-play-session/1"}\n'); tar_add(t, p + "config/controller-map.txt", b"key 304 a\n")
        tar_add(t, p + "startup/startup.json", b'{"schema": "ccs-startup/1"}\n')
        tar_add(t, "newroot/var/lib/ccs/pkg/current", (PKGV + "\n").encode())
        tar_add(t, "newroot/var/lib/ccs/data/saves/stand-in.sav", b"S" * 8192)
        txt, b = mapped(by["hakchi/rootfs/bin/hakchi"]["data"].decode(), HAKCHI_CMD_MAP, Rp); bad += [f"bin/hakchi: {x}" for x in b]
        tar_add(t, "hk/hakchi", txt.encode(), 0o755)
        txt, b = mapped(by["hakchi/rootfs/etc/init.d/rcS"]["data"].decode(), RCS_MAP, Rp); bad += [f"rcS: {x}" for x in b]
        tar_add(t, "rcS", txt.encode(), 0o755)
    return o.getvalue(), bad

def foreign_hmod() -> bytes:
    raw = io.BytesIO()
    with tarfile.open(fileobj=raw, mode="w", format=tarfile.USTAR_FORMAT) as t:
        for d in ("bin", "etc", "etc/init.d", "etc/preinit.d"): tar_add(t, "./" + d, d=True)
        for n, v in sorted(FOREIGN.items()): tar_add(t, "./" + n, v.encode(), 0o755 if n.startswith(("bin/", "etc/init.d/")) else 0o644)
    out = io.BytesIO()
    with gzip.GzipFile(filename="", mode="wb", fileobj=out, mtime=0) as g: g.write(raw.getvalue())
    return out.getvalue()

SETUP = r'''set -e
R=$(mktemp -d /tmp/ccsnu-XXXXXX)
mkdir -p "$R/root" "$R/wrap" "$R/stubs" "$R/tmp" "$R/run/cache" "$R/etc-merged/init.d" "$R/hmods"
tar -xf '@RDTAR@' -C "$R/root"
for a in $('@Q@' -L "$R/root" "$R/root/bin/busybox" --list); do
  printf '#!/bin/sh\nexec %s -L "%s/root" "%s/root/bin/busybox" %s "$@"\n' '@Q@' "$R" "$R" "$a" > "$R/wrap/$a"; chmod 755 "$R/wrap/$a"
done
printf '#!/bin/sh\nexec %s -L "%s/root" "%s/root/bin/busybox" "$@"\n' '@Q@' "$R" "$R" > "$R/wrap/busybox"; chmod 755 "$R/wrap/busybox"
printf '#!/bin/sh\nexec %s -L "%s/root" "%s/root/bin/rsync" "$@"\n' '@Q@' "$R" "$R" > "$R/wrap/rsync"; chmod 755 "$R/wrap/rsync"
sed "s#__R__#$R#g; s#__Q__#@Q@#g" '@STUB@' > "$R/stubs/_stub"; chmod 755 "$R/stubs/_stub"
for n in @STUBNAMES@; do ln -s _stub "$R/stubs/$n"; done
echo "$R"
'''

def chooser_standin_inputs(rp: str) -> dict:
    """the chooser package's inputs for the tests: the stand-in chooser program (for the stand-in console at rp) and the menu fonts
    (--fonts, or one stand-in font file and its license file)"""
    d = os.path.join(WORK, "chooser-in-" + hashlib.sha256(rp.encode()).hexdigest()[:8]); os.makedirs(os.path.join(d, "fonts"), exist_ok=True)
    prog = os.path.join(d, "ccs-home-armv7"); open(prog, "w", newline="\n").write(CHOOSER.replace("__R__", rp))
    fonts = FONTS_DIR
    if not fonts:
        fonts = os.path.join(d, "fonts"); open(os.path.join(fonts, "silk17.ccsf"), "wb").write(b"CCSF stand-in font for the tests")
        open(os.path.join(fonts, "OFL-Stand-in.txt"), "w", newline="\n").write("stand-in license file for the tests\n")
    return {"program": prog, "fonts": fonts}

def setup(inp, ents, pk_dir) -> list[str]:
    global R
    rt = os.path.join(WORK, "ramdisk.tar"); open(rt, "wb").write(ramdisk_tar(ents))
    st = os.path.join(WORK, "stub.sh"); open(st, "w", newline="\n").write(STUBS)
    out = sh(SETUP.replace("@RDTAR@", wpath(rt)).replace("@STUB@", wpath(st)).replace("@STUBNAMES@", " ".join(STUB_NAMES)))
    R = out.strip().splitlines()[-1]
    if not R.startswith("/tmp/ccsnu-"): raise SystemExit("setup failed: " + out[-2000:])
    tb, bad = standin_tar(ents, R)
    tp = os.path.join(WORK, "standin.tar"); open(tp, "wb").write(tb)
    fh = os.path.join(WORK, "foreign.hmod"); open(fh, "wb").write(foreign_hmod())
    sh(f"mkdir -p '@R@/pristine' && tar -xf '{wpath(tp)}' -C '@R@/pristine' && mv '@R@/pristine/hk' '@R@/hk' && mv '@R@/pristine/rcS' '@R@/rcS' && "
       f"cp '{wpath(os.path.join(pk_dir, 'ccs-mod.hmod'))}' '{wpath(os.path.join(pk_dir, 'ccs-nucleus.hmod'))}' '{wpath(fh)}' '@R@/hmods/' && echo DONE")
    # the chooser package with the stand-in chooser program for this stand-in console
    BP.CHOOSER_INPUTS = chooser_standin_inputs(R); pkc = os.path.join(WORK, "pkc"); BP.build(pkc)
    sh(f"cp '{wpath(os.path.join(pkc, 'ccs-nucleus-chooser.hmod'))}' '@R@/hmods/' && echo DONE")
    return bad

def fresh(foreign: bool = True):
    """a new stand-in console from the pristine one; optionally with the stand-in third-party package installed by hakchi's pack_install"""
    sh("rm -rf '@R@/newroot' '@R@/tmp' && cp -a '@R@/pristine/newroot' '@R@/newroot' && mkdir -p '@R@/tmp' && "
       "rm -f '@R@/log' '@R@/picks' '@R@/keys' '@R@/player-rc' '@R@/player-sleep' '@R@/fb-held' '@R@/loops' '@R@/loops.mount' '@R@/attempt-snap' '@R@/last-choices' '@R@'/choices-* '@R@'/shot-*.ppm && touch '@R@/log' && "
       "printf '%s\\n' '@R@/newroot' '@R@/newroot/var' '@R@/newroot/var/lib' '@R@/newroot/var/squashfs' > '@R@/mounted'")
    if foreign: hakchi("pack_install @R@/hmods/foreign.hmod")

def snapshot(paths: str = "") -> dict:
    """SHA-256 of every file the owner and other packages have (everything but Classic Cart Studio's own folders and files, RAM, logs)"""
    out = sh(r"cd '@R@/newroot' && find . -type f ! -path './var/lib/ccs/nucleus/*' ! -path './var/lib/ccs/owned/*' ! -path './var/lib/ccs/mod/*' "
             r"! -path './var/lib/ccs/policy' ! -path './var/lib/hakchi/rootfs/etc/ccs/*' ! -name 'ccs-*' ! -name 'S81ccs-*' ! -name 'p70??_ccsnucleus' "
             r"! -path '*/CLV-H-CCS*' ! -path './tmp/*' ! -path './var/ccs-nucleus/*' ! -path './var/version' ! -path './var/games' "
             r"! -path './var/lib/hakchi/rootfs/etc/preinit.d/p0000_config' ! -path './var/lib/hakchi/hmod/uninstall-ccs-*' ! -name '*.new.*' "
             r"| sort | xargs -r sha256sum")
    return dict((l.split()[1], l.split()[0]) for l in out.splitlines() if len(l.split()) == 2)

def preinit(env: str = "") -> str:
    """hakchi's own preinit (b- and p-files of the stand-in's preinit.d, in its own order), as hakchi/init runs it after mount_base"""
    return sh("cd '@R@' && env -i HOME=/ PATH='@R@/stubs:@R@/wrap' mountpoint='@R@/newroot' modname=hakchi modpath='@R@/root/hakchi' "
              f"temppath='@R@/tmp' CCSTEST_LOG='@R@/log' {env} '@Q@' -L '@R@/root' '@R@/root/bin/busybox' sh -c 'source {HKR}/etc/preinit && preinit' > '@R@/preinit.out' 2>&1; "
              "rm -f '@R@/loops.mount'; cat '@R@/preinit.out' | tail -n 30")

def rcs(env: str = "") -> str:
    """hakchi's rcS over a merged start-script folder (the overlay emulated): the stand-in system's scripts, then the overlay's CCS
    and stand-in third-party scripts on top (hakchi's own start scripts are left out of this emulation)"""
    return sh("rm -rf '@R@/etc-merged/init.d' && mkdir -p '@R@/etc-merged/init.d' && cp -a '@R@/newroot/var/squashfs/etc/init.d/.' '@R@/etc-merged/init.d/' && "
              f"for f in {HKR}/etc/init.d/S81ccs-* {HKR}/etc/init.d/S95foreign-* {HKR}/etc/init.d/S81clover-mcp; do [ -f \"$f\" ] && cp -a \"$f\" '@R@/etc-merged/init.d/'; done; "
              "for f in '@R@'/etc-merged/init.d/*; do sed -i \"1s#^\\#!/bin/sh#\\#!@R@/wrap/sh#\" \"$f\"; done; "
              f"cd '@R@' && env -i HOME=/ PATH='@R@/stubs:@R@/wrap' CCS_ROOT='@R@/newroot' CCSTEST_LOG='@R@/log' {env} '@Q@' -L '@R@/root' '@R@/root/bin/busybox' sh '@R@/rcS' 2>&1 | tail -n 20")

def boot(env_pre: str = "", env_rc: str = "") -> tuple[str, str]:
    """one console start: hakchi's preinit, then its rcS (RAM is new at every start: the stand-in's RAM folders are cleared first)"""
    sh("rm -rf '@R@/newroot/var/ccs-nucleus' '@R@/newroot/tmp' '@R@/attempt-snap' '@R@/loops'")
    a = preinit(env_pre)
    if "POWEROFF" in log().split("BOOT-MARK")[-1]: return a, ""
    return a, rcs(env_rc)

def mark(): sh("echo BOOT-MARK >> '@R@/log'")
def since_mark() -> str: return log().split("BOOT-MARK")[-1]
def cfg_firmware() -> str: return sh(f"grep '^cfg_firmware=' {HKR}/etc/preinit.d/p0000_config").strip()
def exists(p: str) -> bool: return sh(f"test -e '{p}' && echo YES").strip() == "YES"
def fsha(p: str) -> str: return sh(f"sha256sum '{p}' 2>/dev/null | cut -d' ' -f1").strip()
def install_registry(default="native", selection="default", chooser="never", timeout=10, home=True) -> str:
    """a registry made on the PC (formats.registry_text) put in force with registry-install, as the Workbench does: Original Menu, and
    CCS HOME while CCS Mod is installed; the next generation after the one in force"""
    g = re.search(r"registry generation=(\d+)", nuc("status")); gen = int(g.group(1)) + 1 if g else 1
    opts = [{"id": "native", "kind": "native", "label": "Original Menu", "fallback": "-"}]
    if home: opts.append({"id": "ccs-home", "kind": "ccs-home", "label": "CCS HOME", "package": "current", "kernel": F.KERNEL, "fallback": "native"})
    cur = sh("cat '@R@/newroot/var/lib/ccs/nucleus/registry' 2>/dev/null").encode()
    if cur:
        try:
            have = F.registry_read(cur); opts += [o for o in have["options"] if o["kind"] == "owner-root"]
        except F.FormatError: pass
    p = os.path.join(WORK, f"reg-{gen}"); open(p, "wb").write(F.registry_text({"schema": "ccs-boot-registry/1", "generation": gen, "default": default,
        "selection": selection, "chooser": chooser, "timeout": timeout, "options": opts}))
    sh(f"cp '{wpath(p)}' '@R@/reg-in'")
    return result(nuc("registry-install '@R@/reg-in'"))

# ---------------------------------------------------------------- 1. PC-side checks
def code_lines(text: str) -> list[str]: return [l for l in text.splitlines() if not l.lstrip().startswith("#")]

FORBIDDEN = [r"script/transfer", r"transfer/firmware", r"firmwarepath", r"-delete\b", r"\bmount\b(?!point)", r"\bumount\b", r"sunxi", r"burn_boot",
             r"\bflash", r"\bkexec\b", r"\bmemboot\b", r"reboot_kexec", r"\bdd\b", r"\bmkfs", r"\bmke2fs\b", r"\be2fsck\b", r"\btune2fs\b",
             r"\bkill\s+-(9|KILL)\b", r"killall\s+-(9|s\s+KILL)", r"\buistop\b", r"\bpack_install\b", r"\bpackage_install\b", r"rm -rf /",
             r"/dev/nand", r"/dev/mmc", r"\bsave_config\b", r"p0000_config"]

def pc_checks(pk1: str, pk2: str, inp, ents):
    man = json.load(open(os.path.join(pk1, "MANIFEST.json")))
    same = all(open(os.path.join(pk1, f), "rb").read() == open(os.path.join(pk2, f), "rb").read() for f in ("ccs-mod.hmod", "ccs-nucleus.hmod", "ccs-nucleus-chooser.hmod", "MANIFEST.json"))
    check(same, "packages: two builds from the same sources are byte-identical (ccs-mod.hmod, ccs-nucleus.hmod, ccs-nucleus-chooser.hmod, MANIFEST.json)")
    check(not BP.verify(pk1), f"packages: each .hmod unpacks to exactly the members its manifest lists ({BP.verify(pk1) or 'verified'})")
    # the console's rcS runs /etc/init.d/S??* in byte order (BusyBox's glob, the C locale): CCS Nucleus's foreground start must come after the
    # background services of the community add-ons it may share the console with (Options Menu's optiond and its tasks, the Gamepad Remapping
    # Tool's PadMapper: their actual script names, from their packages) and before the console's own menu start, which it never replaces
    inits = [n.rsplit("/", 1)[1] for n in BP.members("nucleus") if n.startswith("etc/init.d/")]
    order = sorted(inits + ["S80", "S8103OptionsMenu", "S8104OptionsMenuTasks", "S8105PadMapper", "S81clover-mcp"])
    check(inits == ["S81ccs-nucleus"] and order == ["S80", "S8103OptionsMenu", "S8104OptionsMenuTasks", "S8105PadMapper", "S81ccs-nucleus", "S81clover-mcp"],
          f"packages: CCS Nucleus's one start script sorts after the community add-ons' background services and before the console's own menu start ({order})")
    check(not [n for n in BP.members("mod") if n.startswith(("etc/init.d/", "etc/preinit.d/"))],
          "packages: CCS Mod carries no start script and no preinit file (nothing of its own runs at power-on)")
    # only Classic Cart Studio's own bytes: every member is a listed source file or one of the two generated records, and none equals a
    # file of the pinned hakchi.hmod or ramdisk
    hk = set()
    hm = inp["hmod"]
    with tarfile.open(fileobj=io.BytesIO(gzip.decompress(hm) if hm[:2] == b"\x1f\x8b" else hm)) as t:
        for m in t.getmembers():
            if m.isfile(): hk.add(hashlib.sha256(t.extractfile(m).read()).hexdigest())
    for e in ents:
        if e["mode"] & 0o170000 == 0o100000 and e["data"]: hk.add(hashlib.sha256(e["data"]).hexdigest())
    for method, p in man["packages"].items():
        srcs = {n: hashlib.sha256(BP.source_bytes(s)).hexdigest() for n, (s, _) in BP.PACKAGES[method].items()}
        if method == "nucleus-chooser": srcs.update({n: hashlib.sha256(b).hexdigest() for n, (b, _) in BP.chooser_members().items()})
        gen = {f"etc/ccs/{method}/VERSION", f"etc/ccs/{method}/FILES"}
        mem = p["members"]
        ok = set(mem) == set(srcs) | gen and all(mem[n]["sha256"] == srcs[n] for n in srcs)
        check(ok and not any(v["sha256"] in hk for v in mem.values()),
              f"packages: ccs-{method}.hmod holds only its {len(srcs)} listed Classic Cart Studio files and its two generated records; none is a hakchi or Nintendo file ({p['bytes']} bytes)")
    # registry and journal formats: the PC's form and the console's form are the same bytes
    reg = {"schema": "ccs-boot-registry/1", "generation": 3, "default": "ccs-home", "selection": "last", "chooser": "always", "timeout": 10,
           "options": [{"id": "native", "kind": "native", "label": "Your Console's Own Menu", "fallback": "-"},
                       {"id": "ccs-home", "kind": "ccs-home", "label": "CCS HOME", "package": "current", "kernel": F.KERNEL, "fallback": "native"},
                       {"id": "owner-1", "kind": "owner-root", "label": "My Other System", "image": "/var/lib/ccs/nucleus/roots/owner-1.hsqs",
                        "sha256": "a" * 64, "bytes": "4096", "kernel": F.KERNEL, "drivers": ["clovercon"], "requires": ["/media/hakchi/games"], "fallback": "native"}]}
    txt = F.registry_text(reg); back = F.registry_read(txt)
    check(back["options"][2]["requires"] == ["/media/hakchi/games"] and F.registry_text(back) == txt, "formats: a registry made from JSON reads back to the same registry and the same bytes")
    bad_cases = {"no native option": lambda r: r["options"].pop(0), "owner image outside the allowed places": lambda r: r["options"][2].__setitem__("image", "/var/lib/hakchi/rootfs/x.hsqs"),
                 "default not listed": lambda r: r.__setitem__("default", "nope"), "an owner image as the start without chooser": lambda r: (r.__setitem__("chooser", "never"), r.__setitem__("default", "owner-1")),
                 "duplicate id": lambda r: r["options"].append(dict(r["options"][1]))}
    refused = []
    for name, f in bad_cases.items():
        r = json.loads(json.dumps(reg)); f(r)
        try: F.registry_text(r)
        except F.FormatError: refused.append(name)
    check(refused == list(bad_cases), f"formats: the PC refuses invalid registries ({', '.join(refused)})")
    tampered = txt.replace(b"timeout=10", b"timeout=11")
    try: F.registry_read(tampered); ok = False
    except F.FormatError: ok = True
    check(ok, "formats: a registry whose bytes changed after sealing is refused (its seal no longer matches)")
    # static rules on what runs (comments excluded)
    srcs = {r: open(os.path.join(BP.SRC, *r.split("/")), encoding="utf-8").read() for r in ("lib.sh", "mod/bin/ccs-mod", "mod/install", "mod/uninstall",
            "nucleus-chooser/install", "nucleus-chooser/uninstall", "nucleus/bin/ccs-nucleus", "nucleus/p7060_ccsnucleus", "nucleus/p7071_ccsnucleus", "nucleus/S81ccs-nucleus", "nucleus/install", "nucleus/uninstall",
            "collections/ccs-collections")}
    hits = {r: [p for p in FORBIDDEN if re.search(p, "\n".join(code_lines(s)))] for r, s in srcs.items()}
    check(not any(hits.values()), f"static: no CCS script uses hakchi's transfer/firmware branch, deletes with find, mounts, flashes, kexecs, force-kills, stops the console's menu or saves hakchi's settings ({ {k: v for k, v in hits.items() if v} or 'none'})")
    for pf in ("nucleus/p7060_ccsnucleus", "nucleus/p7071_ccsnucleus"):
        top, depth = [], 0
        for l in code_lines(srcs[pf]):
            s = l.strip()
            if re.match(r"^\w+\(\)\s*\{", s): depth += 1; continue
            if s == "}": depth -= 1; continue
            if depth == 0 and s: top.append(s)
        check(len(top) == 1 and re.match(r"^ccsn_(early|after)$", top[0]) is not None, f"static: {pf.split('/')[1]} only defines its function and calls it once (no exit, return or command at the top of hakchi's joined preinit script): {top}")
    # where the hooks sort among the pinned hakchi start files
    pd = sorted(e["name"].split("/")[-1] for e in ents if e["name"].startswith("hakchi/rootfs/etc/preinit.d/p") and e["mode"] & 0o170000 == 0o100000)
    allp = sorted(pd + ["p7060_ccsnucleus", "p7071_ccsnucleus"]); i60, i71 = allp.index("p7060_ccsnucleus"), allp.index("p7071_ccsnucleus")
    check(allp[i60 - 1] == "p7020_usb" and allp[i60 + 1] == "p7070_firmware" and allp[i71 - 1] == "p7070_firmware" and allp[i71 + 1] == "p7072_overlayfs"
          and all(not re.match(r"^p????_.*firmware.*$".replace("????", "...."), n) for n in ("p7060_ccsnucleus", "p7071_ccsnucleus")),
          f"insertion point: in hakchi's own order the early stage sorts between p7020_usb and p7070_firmware and its second half between p7070_firmware and p7072_overlayfs; neither name has 'firmware' (p7000_preinit's boot-stock path sources p????_*firmware*) ({', '.join(allp)})")
    removed = next(e for e in ents if e["name"] == "hakchi/script/removed")["data"].decode()
    check('rm -f "$rootfs/etc/init.d/S81clover-mcp"' in removed and "S81ccs" not in removed and "ccs" not in removed,
          "insertion point: the pinned hakchi/script/removed (sourced at every start by p7070_firmware -> mod_repair_etc -> remove_old) deletes an overlay S81clover-mcp, and names nothing of CCS's")
    # the start scripts of the audited community packages (Options Menu, its tasks, PadMapper) are named S8103OptionsMenu,
    # S8104OptionsMenuTasks and S8105PadMapper: CCS Nucleus's start script runs after them and before the console's own menu start
    names = sorted(["S80joydriver", "S80mali", "S81clover-mcp", "S81ccs-nucleus", "S82remote-exec", "S78clvcon", "S8103OptionsMenu", "S8104OptionsMenuTasks", "S8105PadMapper"])
    check(names.index("S81ccs-nucleus") > max(names.index(n) for n in ("S80mali", "S8103OptionsMenu", "S8104OptionsMenuTasks", "S8105PadMapper"))
          and names.index("S81ccs-nucleus") < names.index("S81clover-mcp"),
          f"insertion point: rcS runs CCS Nucleus's start script S81ccs-nucleus after the S80 drivers and the Options Menu and PadMapper start scripts, and before S81clover-mcp ({', '.join(names)})")
    # capacity from the built packages
    cap = CAP.compute(packages=pk1, method="nucleus-chooser", home_bytes=23_000_000, image_bytes=[150_000_000], free_bytes=170_000_000, block=4096)
    ok2 = CAP.compute(packages=pk1, method="mod", home_bytes=23_000_000, free_bytes=170_000_000, block=4096)["fits"]
    check(cap["required_bytes"] == sum(r["bytes"] for r in cap["rows"]) and cap["fits"] is False and cap["shortfall_bytes"] > 0
          and any(r["what"].startswith("save reserve") and r["bytes"] == CAP.SM.SPEC["policies"]["saves_allowance_bytes"]["nes-classic-edition"] for r in cap["rows"]) and ok2
          and cap["account"]["result"] == CAP.SM.account(cap["account"]["input"]),
          f"capacity: measured inputs (built packages, given image and free space) add up; a shortfall is shown, not hidden ({cap['summary']})")

# ---------------------------------------------------------------- 2. runs on the pinned BusyBox
def syntax():
    for rel in ("lib.sh", "mod/bin/ccs-mod", "mod/install", "mod/uninstall", "nucleus/bin/ccs-nucleus", "nucleus/p7060_ccsnucleus",
                "nucleus/p7071_ccsnucleus", "nucleus/S81ccs-nucleus", "nucleus/install", "nucleus/uninstall", "nucleus-chooser/install", "nucleus-chooser/uninstall",
                "collections/ccs-collections"):
        p = os.path.join(WORK, "syn-" + rel.replace("/", "_")); open(p, "wb").write(BP.source_bytes(rel))
        out = bb(f"sh -n '{wpath(p)}' && echo SYNTAX-OK")
        check("SYNTAX-OK" in out, f"syntax: {rel} parses with the pinned BusyBox 1.29 sh ({out.strip()[-160:]})")

def insertion_point():
    """hakchi's own preinit, with CCS Nucleus's p-files among its own: the order shown by what each step leaves behind"""
    fresh(foreign=True)
    hakchi("pack_install @R@/hmods/ccs-nucleus.hmod"); hakchi("pack_install @R@/hmods/ccs-nucleus-chooser.hmod")
    # a CCS-made image imported the supported way, and a request for it (as the chooser leaves one before restarting)
    sh("mkdir -p '@R@/newroot/var/lib/ccs/nucleus/staging' && { printf hsqs; head -c 65532 /dev/urandom; } > '@R@/newroot/var/lib/ccs/nucleus/staging/up1'")
    s = fsha("@R@/newroot/var/lib/ccs/nucleus/staging/up1")
    out = nuc(f"import /var/lib/ccs/nucleus/staging/up1 owner-1 {s} 65536 'My Other System'")
    check(result(out).startswith("result import ok owner-1"), f"import: an owner's image moves from staging into Nucleus's own folder and becomes an option ({result(out)})")
    sh(f"printf '#!/bin/sh\\n# an overlay override of the menu start (as an older start method installed)\\n' > {HKR}/etc/init.d/S81clover-mcp")
    bb(f". {HKR}/etc/ccs/nucleus/lib.sh; ccs_paths; printf 'id=owner-1\\n' | {{ echo schema=ccs-nucleus-state/1; cat; }} | ccs_put_sealed $CCS_ROOT/var/lib/ccs/nucleus/state/next")
    clear_log(); mark()
    pre = preinit()
    L = since_mark()
    snap = sh("cat '@R@/attempt-snap' 2>/dev/null")
    check("LOOPMOUNT /var/lib/ccs/nucleus/roots/owner-1.hsqs" in L and "id=owner-1 stage=selected" in snap,
          f"insertion point: p7060_ccsnucleus ran BEFORE p7070_firmware: when hakchi's loadFirmware mounted the image, Nucleus had already chosen it and recorded the attempt ({snap.strip()})")
    att = sh("cat '@R@/newroot/var/lib/ccs/nucleus/state/attempt' 2>/dev/null")
    check("stage=mounted" in att and cfg_firmware() == "cfg_firmware='auto'",
          f"insertion point: p7071_ccsnucleus ran AFTER p7070_firmware: the attempt is recorded as mounted and the owner's cfg_firmware ('auto') is what hakchi's pffff_config saved ({cfg_firmware()})")
    check("p8000 foreign preinit" in L and not trips(), "insertion point: the other package's p8000 preinit file still runs after Nucleus's (hakchi's joined preinit script is not cut short); no tripwire")
    check(not exists(f"{HKR}/etc/init.d/S81clover-mcp") and exists(f"{HKR}/etc/init.d/S81ccs-nucleus"),
          "insertion point (finding): hakchi's own start removed the overlay S81clover-mcp (script/removed via p7070_firmware -> mod_repair_etc), while S81ccs-nucleus stays: an S81clover-mcp override in hakchi's overlay does not survive a start")

def mod_tests():
    fresh(foreign=True)
    before = snapshot(); fl = sh(f"ls {HKR}/etc/init.d/ | sort").split()
    out = hakchi("pack_install @R@/hmods/ccs-mod.hmod")
    r = result(out)
    check(r.startswith("result postinstall ok installed") and "entry ok /var/lib/hakchi/games/000/CLV-H-CCSMD" in r, f"mod: installed by hakchi's own pack_install, with its CCS HOME tile in the root menu ({r})")
    after = snapshot()
    check(after == before, f"mod: every file of the owner, the console's system folder and the other package is unchanged ({len(before)} files hashed)")
    check(sh(f"ls {HKR}/etc/init.d/ | sort").split() == fl, "mod: no start script was added or changed (CCS Mod has no power-on action: the console starts exactly as before)")
    j = F.journal_read(sh("cat '@R@/newroot/var/lib/ccs/owned/mod.own'").encode())
    check(len(j["files"]) == 9 and j["files"]["/var/lib/hakchi/rootfs/bin/ccs-mod"]["mode"] == "0755" and "/var/lib/hakchi/games/000/CLV-H-CCSMD" in j["dirs"],
          f"mod: the ownership journal names the 5 files the package copies, its file list and the 3 menu-entry files, each with its SHA-256 ({len(j['files'])} files)")
    un = sh("cat '@R@/newroot/var/lib/hakchi/hmod/uninstall-ccs-mod'")
    check(un.startswith("# CCS Mod for Hakchi: removal") and "# auto-generated" in un, "mod: hakchi keeps its own uninstall list for the package (CCS's script first, hakchi's generated list after it)")
    # Classic Cart Studio's installer takes this same route on a running console (hakchi's pack_install): the next start keeps it
    clear_log(); mark(); boot(); st = mod("status"); L = since_mark()
    check(not [x for x in st.splitlines() if x.startswith(("missing", "changed"))] and "chooser=none" in st and exists("@R@/newroot/var/lib/hakchi/games/000/CLV-H-CCSMD/CLV-H-CCSMD.desktop")
          and "S81clover-mcp the console menu" in L and not trips(),
          "mod: installed by hakchi's pack_install on a running console (Classic Cart Studio's installer's route), it survives hakchi's next start (preinit, p7070_firmware's script/removed, mod_repair_etc) unchanged, and the console's own menu still starts")
    out = hakchi("pack_install @R@/hmods/ccs-mod.hmod")
    check(result(out).startswith("result postinstall ok") and F.journal_read(sh("cat '@R@/newroot/var/lib/ccs/owned/mod.own'").encode()) == j,
          f"mod: installing the same package again changes nothing (the same journal) ({result(out)})")
    # the menu entry: CCS HOME once the console's menu has given up the screen; back to that menu
    clear_log(); sh("echo 3 > '@R@/fb-held' && echo 2 > '@R@/player-sleep'")
    out = bb("sh /var/lib/hakchi/rootfs/bin/ccs-mod launch".replace("/var/lib", "@R@/newroot/var/lib"))
    L = log()
    check("HOME menu" in L and "--startup" not in L and result(out).startswith("result launch ok home-ended 0") and exists("@R@/newroot/tmp/.heartbeat"),
          f"mod: the menu entry waits until the console's menu has handed over the screen, opens CCS HOME (no startup picture), keeps hakchi's game-hang watch fed, and returns to that menu ({result(out)})")
    clear_log(); sh("echo always > '@R@/fb-held'")
    out = mod("launch")
    check("PLAYER" not in log() and "screen-busy" in result(out), f"mod: when the screen stays in use, CCS HOME is not opened and nothing is stopped by force ({result(out)})")
    sh("rm -f '@R@/fb-held' '@R@/player-sleep'")
    # CCS Mod has no power-on action: the console starts its own menu; CCS Mod has no command for an optional start
    clear_log(); rcs()
    check("PLAYER" not in log() and "HOME startup" not in log() and "S81clover-mcp the console menu" in log() and not exists(f"{HKR}/etc/init.d/S81ccs-mod")
          and not [x for x in sh(f"ls -1 {HKR}/etc/preinit.d/; ls -1 {HKR}/etc/init.d/").split() if "ccs" in x],
          f"mod: CCS Mod adds no start script, start-stage hook or service: at start the console's own menu starts, not CCS HOME ({[x for x in log().splitlines()][-6:]}; {sh(f'ls {HKR}/etc/preinit.d/ {HKR}/etc/init.d/ 2>/dev/null').split()})")
    out = mod("autostart on")
    check(result(out).endswith("refused unknown command"), f"mod: CCS Mod has no command for a start at power-on ({result(out)})")
    # removal through hakchi: only unchanged own files; the changed one kept; the other package, the owner's files and CCS HOME untouched
    sh(f"echo '# changed by someone' >> {HKR}/etc/ccs/mod/entry-small.png")
    before = snapshot()
    out = hakchi("pack_uninstall ccs-mod")
    left = sh(f"ls {HKR}/bin/ccs-mod {HKR}/etc/ccs/mod/ {HKR}/etc/init.d/S81ccs-mod @R@/newroot/var/lib/hakchi/games/000/ @R@/newroot/var/lib/hakchi/hmod/ 2>&1")
    check("kept-changed /var/lib/hakchi/rootfs/etc/ccs/mod/entry-small.png" in out
          and "removed /var/lib/hakchi/rootfs/bin/ccs-mod" in out and "entry-small.png" in left and "CLV-H-CCSMD" not in left
          and "uninstall-ccs-mod" not in left and "uninstall-foreign" in left,
          "mod: hakchi's pack_uninstall removes CCS Mod's unchanged files and its menu entry, keeps the changed file and reports it; hakchi's own list for it is gone; the other package's is not")
    check(snapshot() == before and exists(f"@R@/newroot/var/lib/ccs/pkg/{PKGV}/bin/ccs-player-armv7"), "mod: removing CCS Mod leaves the owner's files, the other package and CCS HOME's package and saves exactly as they were")
    check(not trips(), "mod: no tripwire during installation, starts or removal")
    # Hakchi2 CE's own game storage is games/<type>-<region> (games/nes-usa here: this console's board dp-nes, region EUR_USA): the CCS HOME
    # tile goes where the menu reads it, not into the overlay's built-in folder, which that storage covers at every start
    fresh(foreign=True)
    sh("cd '@R@/newroot/var/lib/hakchi/games' && mkdir -p nes-usa && mv 000 nes-usa/000")
    r = result(hakchi("pack_install @R@/hmods/ccs-mod.hmod"))
    check(r.startswith("result postinstall ok installed") and "entry ok /var/lib/hakchi/games/nes-usa/000/CLV-H-CCSMD" in r
          and exists("@R@/newroot/var/lib/hakchi/games/nes-usa/000/CLV-H-CCSMD/CLV-H-CCSMD.desktop") and not exists(f"{HKR}/usr/share/games/nes/kachikachi/CLV-H-CCSMD"),
          f"mod: with Hakchi2 CE's own game storage (games/nes-usa) the CCS HOME tile is written in its root menu, where the console's menu reads it ({r})")
    out = hakchi("pack_uninstall ccs-mod")
    check(not exists("@R@/newroot/var/lib/hakchi/games/nes-usa/000/CLV-H-CCSMD") and exists("@R@/newroot/var/lib/hakchi/games/nes-usa/000/CLV-H-AAAAA"),
          "mod: and its removal takes the tile out of that storage again, leaving the games there")

def foreign_semantics():
    """the stand-in third-party package through hakchi's own pack_install/pack_uninstall, without and with CCS Nucleus installed"""
    def run_case(with_ccs: bool):
        fresh(foreign=False)
        if with_ccs: hakchi("pack_install @R@/hmods/ccs-nucleus.hmod"); hakchi("pack_install @R@/hmods/ccs-nucleus-chooser.hmod"); hakchi("pack_install @R@/hmods/ccs-mod.hmod")
        out = hakchi("pack_install @R@/hmods/foreign.hmod")
        files = sh(f"cd {HKR} && sha256sum bin/foreign-tool etc/init.d/S95foreign-autostart etc/preinit.d/p8000_foreign 2>&1; ls -l etc/init.d/S95foreign-autostart | cut -c1-10; cat @R@/newroot/var/lib/hakchi/hmod/uninstall-foreign")
        un = hakchi("pack_uninstall foreign")
        gone = sh(f"cd {HKR} && ls bin/foreign-tool etc/init.d/S95foreign-autostart etc/preinit.d/p8000_foreign @R@/newroot/var/lib/hakchi/hmod/uninstall-foreign 2>&1 | grep -c 'No such'")
        ccs = sh(f"sha256sum {HKR}/bin/ccs-nucleus {HKR}/etc/preinit.d/p7060_ccsnucleus 2>/dev/null | wc -l")
        return out, files.replace(R, "<R>"), gone.strip(), ccs.strip()
    o1, f1, g1, c1 = run_case(False); o2, f2, g2, c2 = run_case(True)
    check(f1 == f2 and "installed" in o1 and "installed" in o2 and "transfer_default" not in f1,
          "third-party: the stand-in package (transfer_default, then return 1) installs to the same files, modes and hakchi uninstall list with and without CCS Nucleus and CCS Mod installed")
    check(g1 == g2 == "4" and c2 == "2", f"third-party: hakchi's pack_uninstall removes it the same way in both; CCS's files are untouched ({g1}/{g2} removed, CCS files {c2})")

def nucleus_tests():
    fresh(foreign=True)
    hakchi("pack_install @R@/hmods/ccs-mod.hmod")   # CCS Mod (the CCS HOME tile), so CCS Nucleus offers CCS HOME
    before = snapshot()
    out = hakchi("pack_install @R@/hmods/ccs-nucleus.hmod")
    r = result(out)
    check(r == "result postinstall ok installed" and not exists(f"{HKR}/etc/preinit.d/p7060_ccsnucleus") and not exists("@R@/newroot/var/lib/hakchi/games/000/CLV-H-CCSNU"),
          f"nucleus: installed by hakchi's own pack_install: its start script only (no start-stage hook, no menu entry) ({r})")
    st = nuc("status")
    check("registry generation=1 default=native selection=default chooser=never timeout=10" in st and "option native kind=native ok" in st and "option ccs-home kind=ccs-home ok" in st,
          "nucleus: its first registry: Original Menu started directly; CCS HOME offered because CCS Mod is installed")
    out = hakchi("pack_install @R@/hmods/ccs-nucleus-chooser.hmod")
    r = result(out)
    check(r.startswith("result chooser-postinstall ok installed") and "CLV-H-CCSNU" in r and exists(f"{HKR}/etc/preinit.d/p7060_ccsnucleus")
          and exists(f"{HKR}/etc/ccs/nucleus/chooser/bin/ccs-home-armv7") and "chooser=installed" in nuc("status"),
          f"nucleus: its chooser package, installed by hakchi's own pack_install: the start-stage hooks, its own chooser program and the menu entry ({r})")
    r = install_registry(default="ccs-home", selection="last", chooser="always")
    check(r.startswith("result registry-install ok") and "default=ccs-home selection=last chooser=always timeout=10" in nuc("status"),
          f"nucleus: the registry the PC writes (registry-install): the chooser at every start, CCS HOME first ({r})")
    check(snapshot() == before, "nucleus: installing changed no file of the owner, the console's system folder or the other package")
    # the owner's images: one imported, one already in hakchi's own firmware folder (referenced, never moved)
    sh("mkdir -p '@R@/newroot/var/lib/ccs/nucleus/staging' && { printf hsqs; head -c 65532 /dev/urandom; } > '@R@/newroot/var/lib/ccs/nucleus/staging/up1' && "
       "{ printf hsqs; head -c 32764 /dev/urandom; } > '@R@/newroot/var/lib/hakchi/firmware/zz-owner.hsqs' && { printf hsqs; head -c 1000 /dev/urandom; } > '@R@/newroot/var/lib/hakchi/firmware/aa-owner.hsqs'")
    fw_before = sh("cd '@R@/newroot/var/lib/hakchi/firmware' && sha256sum * | sort")
    s1 = fsha("@R@/newroot/var/lib/ccs/nucleus/staging/up1")
    sh("printf 'echo TRIPWIRE script/transfer sourced >> @R@/log\\n' | cat - '@R@/root/hakchi/script/transfer' > '@R@/tx' && cp '@R@/tx' '@R@/root/hakchi/script/transfer'")
    clear_log()
    out = nuc(f"import /var/lib/ccs/nucleus/staging/up1 owner-1 {s1} 65536 'My Other System'")
    check(result(out) == "result import ok owner-1" and exists("@R@/newroot/var/lib/ccs/nucleus/roots/owner-1.hsqs") and not exists("@R@/newroot/var/lib/ccs/nucleus/staging/up1"),
          f"nucleus: an uploaded image is moved (one rename) into Nucleus's own folder and becomes option owner-1 ({result(out)})")
    out = nuc("register-foreign /var/lib/hakchi/firmware/zz-owner.hsqs owner-2 'Image In Hakchi'")
    check(result(out).startswith("result register-foreign ok owner-2"), f"nucleus: an image the owner keeps in hakchi's firmware folder is added by reference ({result(out)})")
    out = nuc(f"import /var/lib/hakchi/firmware/aa-owner.hsqs owner-3 {'0'*64} 1004 'Wrong Place'")
    check("refused only an image uploaded to /var/lib/ccs/nucleus/staging/" in result(out), "nucleus: an import from anywhere but Nucleus's staging folder is refused")
    check(sh("cd '@R@/newroot/var/lib/hakchi/firmware' && sha256sum * | sort") == fw_before and not trips(),
          "nucleus: hakchi's firmware folder is exactly as before (nothing pruned, moved or claimed) and hakchi's generic transfer script was never sourced (tripwire)")
    j = F.journal_read(sh("cat '@R@/newroot/var/lib/ccs/owned/nucleus.own'").encode())
    check("/var/lib/ccs/nucleus/roots/owner-1.hsqs" in j["data"] and not any("hakchi/firmware" in p for p in list(j["data"]) + list(j["files"])),
          "nucleus: the imported image is recorded as Nucleus's data; the referenced image is not claimed")
    # the starts below run with hakchi's firmware folder empty (with cfg_firmware 'auto', hakchi itself would start the first image there:
    # the console's own system); the images are put back for the last start
    sh("mkdir -p '@R@/fw-aside' && mv '@R@/newroot/var/lib/hakchi/firmware/'*.hsqs '@R@/fw-aside/'"); nuc("forget owner-2")

    # ---- starts
    def pick(*ids): sh("printf '%s\\n' " + " ".join("'" + i + "'" for i in ids) + " > '@R@/picks'" if ids else "rm -f '@R@/picks'")
    clear_log(); mark(); pick("ccs-home", "native")
    boot()
    L = since_mark(); seq = [l.split(" ")[0] if l.startswith("CHOOSER") else l for l in L.splitlines() if l.startswith(("CHOOSER", "HOME", "S81clover", "S95", "LOOPMOUNT", "REBOOT"))]
    check(seq[:4] == ["CHOOSER", "HOME startup", "CHOOSER", "S81clover-mcp the console menu"] and "LOOPMOUNT" not in L,
          f"nucleus: start: the chooser, CCS HOME (startup picture), the chooser again after leaving it, then the console's own system: its menu start and the other package's autostart, unchanged ({[x.replace(R, '<R>') for x in seq]})")
    args = next((l for l in L.splitlines() if l.startswith("CHOOSER ")), "").replace(R, "<R>")
    C = "<R>/newroot/var/lib/hakchi/rootfs/etc/ccs/nucleus/chooser"
    check(args == f"CHOOSER --choose <R>/newroot/tmp/ccs/run/choices --fonts {C}/fonts --map {C}/config/controller-map.txt",
          f"nucleus: the chooser is the chooser package's own native HOME program (--choose), with its own menu fonts and controller codes ({args})")
    ch = sh("cat '@R@/last-choices'")
    check("highlight=ccs-home" in ch and "timeout=0" in ch and "\nauto=\n" in ch and "choice=owner-1|owner-root|restart|ok|My Other System|" in ch and "choice=native|native|same|ok|Original Menu|" in ch
          and "choice=ccs-home|ccs-home|same|ok|CCS HOME|" in ch and "settings=Recovery and Settings" in ch and "setting=native|Start Original Menu" in ch,
          "nucleus: the chooser lists every option (an owner's image as a restart); after CCS HOME it waits for a choice (no countdown); Recovery and Settings is offered")
    # an owner's image: chosen, the console restarts, the start stage selects it for that start only
    clear_log(); mark(); pick("owner-1")
    boot()
    L = since_mark()
    check("REBOOT" in L and "LOOPMOUNT" not in L and "S81clover" not in L and sh("cat '@R@/newroot/var/lib/ccs/nucleus/state/next'").startswith("schema=ccs-nucleus-state/1\nid=owner-1"),
          "nucleus: choosing an owner's image writes a one-time request and restarts the console through hakchi (nothing else is started)")
    clear_log(); mark(); pick()
    boot()
    L = since_mark()
    check("LOOPMOUNT /var/lib/ccs/nucleus/roots/owner-1.hsqs" in L and "PLAYER" not in L and "CHOOSER" not in L and "S81clover-mcp the console menu" in L and cfg_firmware() == "cfg_firmware='auto'"
          and not exists("@R@/newroot/var/lib/ccs/nucleus/state/next") and not exists("@R@/newroot/var/lib/ccs/nucleus/state/attempt"),
          "nucleus: the next start mounts that image through hakchi's own loadFirmware, its own start runs with nothing of Nucleus on the TV, the start is confirmed, and the owner's cfg_firmware stays 'auto'")
    check("id=owner-1" in sh("cat '@R@/newroot/var/lib/ccs/nucleus/state/last'"), "nucleus: a confirmed start into an owner's image becomes the last choice")
    clear_log(); mark(); pick("native")
    boot()
    L = since_mark(); ch = sh("cat '@R@/last-choices'")
    check("LOOPMOUNT" not in L and "S81clover-mcp the console menu" in L and "highlight=owner-1" in ch and "timeout=10" in ch and "\nauto=owner-1\n" in ch,
          "nucleus: the start after that returns to the console's own system (an owner's image is never started without a choice); the chooser highlights the last choice with its countdown")
    # a chooser that ends without an answer: the registry's own choice in the console's own system
    nuc("set selection default"); clear_log(); mark(); pick()
    boot()
    L = since_mark()
    check("HOME startup" in L and "S81clover-mcp the console menu" in L and "LOOPMOUNT" not in L,
          "nucleus: when the chooser gives no answer (it cannot run), the console starts the registry's default (CCS HOME), then its own system")
    nuc("set chooser never"); clear_log(); mark(); boot(); L = since_mark()
    check("HOME startup" in L and "CHOOSER" in L and L.index("HOME startup") < L.index("CHOOSER") and "S81clover-mcp the console menu" in L,
          "nucleus: with chooser never, CCS HOME starts directly; leaving it shows the chooser (CCS HOME's leave item: Choose Another System)")
    nuc("set default native"); clear_log(); mark(); boot(); L = since_mark()
    check("PLAYER" not in L and "CHOOSER" not in L and "S81clover-mcp the console menu" in L, "nucleus: default native with chooser never: the console starts exactly as without Nucleus")
    out = nuc("set default owner-1")
    check("refused" in result(out) and "default=native" in nuc("status"), f"nucleus: an owner's image cannot be the start without the chooser; the registry in force is unchanged ({result(out)})")
    nuc("set chooser always"); nuc("set default ccs-home")
    # failures fall back to the console's own system, deterministically and only once
    def request(i): bb(f". {HKR}/etc/ccs/nucleus/lib.sh; ccs_paths; printf 'id={i}\\n' | {{ echo schema=ccs-nucleus-state/1; cat; }} | ccs_put_sealed $CCS_ROOT/var/lib/ccs/nucleus/state/next")
    sh("mv '@R@/newroot/var/lib/ccs/nucleus/roots/owner-1.hsqs' '@R@/o1.keep'"); request("owner-1"); clear_log(); mark(); pick("native")
    boot(); L = since_mark(); st = nuc("status")
    check("LOOPMOUNT" not in L and "S81clover-mcp the console menu" in L and "failed-before:image-missing" in st and cfg_firmware() == "cfg_firmware='auto'",
          "nucleus: a requested image that is missing: the console's own system starts, the reason is recorded, the owner's setting is unchanged")
    sh("mv '@R@/o1.keep' '@R@/newroot/var/lib/ccs/nucleus/roots/owner-1.hsqs'")
    sh("f='@R@/newroot/var/lib/ccs/nucleus/roots/owner-1.hsqs'; printf X | dd of=\"$f\" bs=1 seek=1000 conv=notrunc 2>/dev/null; touch -d '2030-01-01' \"$f\"")
    request("owner-1"); clear_log(); mark(); boot(); L = since_mark()
    check("LOOPMOUNT" not in L and "failed-before:image-changed" in nuc("status"), "nucleus: an image whose bytes changed since it was imported is not started (its SHA-256 is checked again when its size or time changed)")
    # a good image, but hakchi cannot mount it: hakchi's own answer (cfg_firmware=_nand_, power off); the next start puts the owner's value back
    sh("rm -f '@R@/newroot/var/lib/ccs/nucleus/state/verified-owner-1'; mkdir -p '@R@/newroot/var/lib/ccs/nucleus/staging' && { printf hsqs; head -c 65532 /dev/urandom; } > '@R@/newroot/var/lib/ccs/nucleus/staging/up4'")
    s4 = fsha("@R@/newroot/var/lib/ccs/nucleus/staging/up4"); nuc(f"import /var/lib/ccs/nucleus/staging/up4 owner-4 {s4} 65536 'Unmountable'")
    request("owner-4"); clear_log(); mark()
    boot(env_pre="CCSTEST_LOOP_FAIL=1"); L = since_mark()
    check("LOOPMOUNT-FAILED" in L and "POWEROFF" in L and cfg_firmware() == "cfg_firmware='_nand_'",
          "nucleus: (hakchi's own behavior) an image hakchi cannot mount makes hakchi save cfg_firmware='_nand_' and switch the console off")
    clear_log(); mark(); pick("native"); boot(); L = since_mark()
    check(cfg_firmware() == "cfg_firmware='auto'" and "LOOPMOUNT" not in L and "S81clover-mcp the console menu" in L and "failed-before:not-loaded" in nuc("status"),
          "nucleus: the next start judges that attempt once: the owner's cfg_firmware ('auto') is put back, the console's own system starts, the failure is recorded")
    clear_log(); mark(); pick("native"); boot(); L = since_mark()
    check("LOOPMOUNT" not in L and "S81clover-mcp the console menu" in L, "nucleus: and it is not tried again by itself (no start loop)")
    # mounted, but the image never reached its start scripts
    sh("{ printf hsqs; head -c 65532 /dev/urandom; } > '@R@/newroot/var/lib/ccs/nucleus/staging/up5'")
    out = nuc(f"import /var/lib/ccs/nucleus/staging/up5 owner-5 {fsha('@R@/newroot/var/lib/ccs/nucleus/staging/up5')} 65536 'Fifth System'")
    request("owner-5"); clear_log(); mark()
    sh("rm -rf '@R@/newroot/var/ccs-nucleus' '@R@/newroot/tmp'"); preinit()   # the start stage only: the image's own start never reached rcS
    check("LOOPMOUNT /var/lib/ccs/nucleus/roots/owner-5.hsqs" in since_mark(), "nucleus: (set-up) the image was mounted, and its start then stopped before its start scripts")
    clear_log(); mark(); pick("native"); boot(); L = since_mark()
    check("LOOPMOUNT" not in L and "failed-before:did-not-start" in nuc("status") and "S81clover-mcp the console menu" in L,
          "nucleus: an image that was mounted but never reached its start scripts is recorded as failed at the next start, which is the console's own system")
    # an external dependency (USB storage)
    reg_body = sh("sed '$d' '@R@/newroot/var/lib/ccs/nucleus/registry'")
    gen = int(re.search(r"^generation=(\d+)$", reg_body, re.M).group(1))
    reg_new = re.sub(r"^generation=\d+$", f"generation={gen + 1}", reg_body, flags=re.M).replace("opt.owner-5.requires=", "opt.owner-5.requires=/media/hakchi/games")
    p = os.path.join(WORK, "reg-new"); open(p, "wb").write(F.seal(reg_new))
    out = nuc(f"registry-install {wpath(p)}")
    check(result(out).startswith("result registry-install ok"), f"nucleus: a registry made on the PC (sealed with the PC's formats.py) is checked and put in force ({result(out)})")
    out = nuc(f"registry-install {wpath(p)}")
    check("refused older than the registry in force" in result(out), "nucleus: the same registry again (not newer than the one in force) is refused")
    sh("rm -f '@R@/newroot/var/lib/ccs/nucleus/state/failed-owner-5'"); request("owner-5"); clear_log(); mark(); pick("native"); boot(); L = since_mark()
    check("LOOPMOUNT" not in L and "failed-before:needs-/media/hakchi/games" in nuc("status"), "nucleus: an image that needs USB storage that is not connected is not started; the console's own system starts")
    sh("mkdir -p '@R@/newroot/media/hakchi/games' && rm -f '@R@/newroot/var/lib/ccs/nucleus/state/failed-owner-5'"); request("owner-5"); clear_log(); mark(); boot(); L = since_mark()
    check("LOOPMOUNT /var/lib/ccs/nucleus/roots/owner-5.hsqs" in L, "nucleus: with the USB storage there, the same image starts")
    # CCS HOME failing at start: not started by itself again until it has run once more
    sh("echo 3 > '@R@/player-rc'"); nuc("set chooser never"); clear_log(); mark(); boot(); L = since_mark()
    check("HOME startup" in L and "S81clover-mcp the console menu" in L and "failed-before:ended-3" in nuc("status"), "nucleus: CCS HOME that ends with an error at start: the console's own start continues; recorded")
    clear_log(); mark(); boot(); L = since_mark()
    check("PLAYER" not in L and "CHOOSER" not in L and "S81clover-mcp the console menu" in L, "nucleus: the next start does not start the failed CCS HOME by itself (no loop)")
    sh("rm -f '@R@/player-rc'"); nuc("set chooser always"); clear_log(); mark(); pick("ccs-home", "native"); boot()
    check("failed-before" not in [l for l in nuc("status").splitlines() if l.startswith("option ccs-home")][0], "nucleus: choosing CCS HOME on the TV again and leaving it normally clears its failure")
    # Recovery and Settings, carried out by ccs-nucleus itself (the stand-in answers its rows): Start With, Countdown, Clear Start Problems
    bb(f". {HKR}/etc/ccs/nucleus/lib.sh; ccs_paths; printf 'id=owner-1\\nwhy=not-loaded\\n' | {{ echo schema=ccs-nucleus-state/1; cat; }} | ccs_put_sealed $CCS_ROOT/var/lib/ccs/nucleus/state/failed-owner-1")
    clear_log(); mark(); pick("action start-with", "action countdown", "action clear", "native"); boot(); L = since_mark(); st = nuc("status"); ch = sh("cat '@R@/last-choices'")
    check("default=owner-1 selection=default chooser=always timeout=20" in st and "failed-before" not in [l for l in st.splitlines() if l.startswith("option owner-1")][0]
          and "\npanel=settings\n" in ch and "\nhighlight=clear\n" in ch and "notice=Start problems cleared: a system that did not start before can start by itself again." in ch
          and "setting=start-with|Start With: My Other System" in ch and "setting=countdown|Countdown: 20 Seconds" in ch and L.count("CHOOSER ") == 4 and "S81clover-mcp the console menu" in L,
          f"nucleus: Recovery and Settings: Start With moves to the next option (an owner's image while the chooser shows at start), the countdown to 20 seconds, Clear Start Problems forgets a recorded failure; the panel comes back after each with what was done ({[l for l in st.splitlines() if l.startswith('registry')]})")
    nuc("set default ccs-home"); nuc("set timeout 10")
    # interrupted updates: the registry in force stays the old one; a broken registry means the console's own system
    reg_before = fsha("@R@/newroot/var/lib/ccs/nucleus/registry")
    out = nuc("set default native", env="CCSTEST_KILL_MV=nucleus/registry")
    check("KILLED-AT-MV" in log() and fsha("@R@/newroot/var/lib/ccs/nucleus/registry") == reg_before and "default=ccs-home" in nuc("status"),
          "nucleus: a registry update cut off right before its rename (power lost) leaves the registry in force unchanged and valid")
    clear_log(); mark(); bb(f". {HKR}/etc/ccs/nucleus/lib.sh; ccs_paths; printf 'id=owner-1\\n' | {{ echo schema=ccs-nucleus-state/1; cat; }} | ccs_put_sealed $CCS_ROOT/var/lib/ccs/nucleus/state/next", env="CCSTEST_KILL_MV=state/next")
    pick("native"); boot(); L = since_mark()
    check("LOOPMOUNT" not in L and "S81clover-mcp the console menu" in L, "nucleus: a one-time request cut off before its rename does not exist: the start follows the registry")
    sh("cp '@R@/newroot/var/lib/ccs/nucleus/registry' '@R@/reg.ok' && head -c 200 '@R@/reg.ok' > '@R@/newroot/var/lib/ccs/nucleus/registry'")
    request("owner-1"); clear_log(); mark(); boot(); L = since_mark()
    check("LOOPMOUNT" not in L and "PLAYER" not in L and "CHOOSER" not in L and "S81clover-mcp the console menu" in L and "this-start id=native kind=native src=policy why=registry-invalid" in nuc("status"),
          "nucleus: a damaged registry (cut short): nothing of Nucleus starts; the console's own system does")
    check(not exists("@R@/newroot/var/lib/ccs/nucleus/state/next"), "nucleus: a request is used up even when the registry is damaged (it cannot act later by surprise)")
    sh("cp '@R@/reg.ok' '@R@/newroot/var/lib/ccs/nucleus/registry'")
    # the console's own system is exactly what hakchi starts without Nucleus: with images in hakchi's firmware folder and 'auto', hakchi's choice
    sh("mv '@R@/fw-aside/'*.hsqs '@R@/newroot/var/lib/hakchi/firmware/'"); clear_log(); mark(); pick("native"); boot(); L = since_mark()
    check("LOOPMOUNT /var/lib/hakchi/firmware/aa-owner.hsqs" in L and cfg_firmware() == "cfg_firmware='auto'" and "S81clover-mcp the console menu" in L,
          "nucleus: choosing the console's own system leaves hakchi's own firmware choice in force ('auto': hakchi starts the first image in its folder, as without Nucleus)")
    check(not [t for t in trips() if "foreign" not in t], f"nucleus: no tripwire in any start ({trips()})")

def transitions():
    """CCS Mod and CCS Nucleus are independent add-ons: each is added or removed without the other, and CCS Nucleus's chooser is added
    or removed on its own; games, saves, settings and other packages stay as they were"""
    fresh(foreign=True)
    sh("mkdir -p '@R@/newroot/var/lib/ccs/data/saves' && echo newer-save > '@R@/newroot/var/lib/ccs/data/saves/new.sav'")
    before = snapshot()
    out = hakchi("pack_install @R@/hmods/ccs-nucleus.hmod"); st = nuc("status")
    check(result(out) == "result postinstall ok installed" and "default=native selection=default chooser=never" in st and "option ccs-home" not in st and "chooser=none" in st,
          f"independent: CCS Nucleus alone starts Original Menu directly and offers no CCS HOME without CCS Mod ({result(out)})")
    clear_log(); mark(); boot(); L = since_mark()
    check("CHOOSER" not in L and "HOME startup" not in L and "S81clover-mcp the console menu" in L and "S95 foreign autostart" in L,
          "independent: with CCS Nucleus alone (direct) the console's own start continues unchanged, nothing on the TV first")
    out = hakchi("pack_install @R@/hmods/ccs-mod.hmod")
    clear_log(); mark(); boot(); L = since_mark()
    check(result(out).startswith("result postinstall ok installed") and "HOME startup" not in L and "S81clover-mcp the console menu" in L and exists(f"{HKR}/bin/ccs-nucleus"),
          f"independent: adding CCS Mod adds its tile only; what opens at power-on is still CCS Nucleus's setting ({result(out)})")
    r = install_registry(default="ccs-home")
    clear_log(); mark(); boot(); L = since_mark()
    seq = [l for l in L.splitlines() if l.startswith(("CHOOSER", "HOME", "S81clover"))]
    check(r.startswith("result registry-install ok") and seq == ["HOME startup", "S81clover-mcp the console menu"],
          f"independent: CCS Nucleus set to CCS HOME (direct): CCS HOME opens at power-on, then the console's own start continues; no chooser is installed, none is shown ({seq})")
    out = mod("launch")
    check(result(out).startswith("result launch ok home-ended 0 (back to Original Menu)") and "CHOOSER" not in log(),
          f"independent: leaving CCS HOME from its tile goes back to Original Menu while no chooser is installed ({result(out)})")
    out = hakchi("pack_install @R@/hmods/ccs-nucleus-chooser.hmod")
    clear_log(); sh("printf '%s\\n' back > '@R@/picks'")
    out2 = mod("launch")
    check(result(out).startswith("result chooser-postinstall ok") and result(out2).startswith("result launch ok home-ended 0 (to the chooser)") and "CHOOSER --choose" in log(),
          f"independent: with the chooser added, Choose Another Menu in CCS HOME opens CCS Nucleus's chooser ({result(out2)})")
    sh("rm -f '@R@/picks'")
    # the chooser removed on its own: CCS Nucleus, its registry, CCS Mod and CCS HOME stay
    out = hakchi("pack_uninstall ccs-nucleus-chooser"); st = nuc("status")
    check(not exists(f"{HKR}/etc/preinit.d/p7060_ccsnucleus") and not exists(f"{HKR}/etc/ccs/nucleus/chooser") and not exists("@R@/newroot/var/lib/hakchi/games/000/CLV-H-CCSNU")
          and not exists("@R@/newroot/var/lib/hakchi/hmod/uninstall-ccs-nucleus-chooser") and exists(f"{HKR}/bin/ccs-nucleus") and "chooser=none" in st and "chooser=never" in st
          and exists(f"{HKR}/bin/ccs-mod"), f"independent: removing the chooser package leaves CCS Nucleus (direct), its registry and CCS Mod ({result(out)}; {st.splitlines()[:3]}; "
          f"{sh(f'ls -R {HKR}/etc/ccs/nucleus {HKR}/etc/preinit.d @R@/newroot/var/lib/hakchi/hmod @R@/newroot/var/lib/hakchi/games/000 2>&1').split()})")
    # CCS Mod removed while CCS Nucleus would start CCS HOME: the console's own start continues (nothing is left waiting on the TV)
    hakchi("pack_uninstall ccs-mod")
    clear_log(); mark(); boot(); L = since_mark()
    check(not exists(f"{HKR}/bin/ccs-mod") and exists(f"{HKR}/bin/ccs-nucleus") and "S81clover-mcp the console menu" in L and "CHOOSER" not in L,
          "independent: removing CCS Mod leaves CCS Nucleus; a start that was set to CCS HOME continues to the console's own menu")
    check(snapshot() == before and not trips(), "independent: games, saves (also a newer one), settings, the other package and the console's system folder are unchanged throughout")
    # removals: CCS Nucleus with its chooser (the chooser goes first), keeping or removing its data; CCS HOME's package and saves stay
    fresh(foreign=True); hakchi("pack_install @R@/hmods/ccs-nucleus.hmod"); hakchi("pack_install @R@/hmods/ccs-nucleus-chooser.hmod")
    before = snapshot()
    out = nuc("remove keep")
    check(result(out).startswith("result remove ok keep") and not exists(f"{HKR}/etc/preinit.d/p7060_ccsnucleus") and not exists(f"{HKR}/etc/init.d/S81ccs-nucleus")
          and not exists("@R@/newroot/var/lib/hakchi/games/000/CLV-H-CCSNU") and exists("@R@/newroot/var/lib/ccs/nucleus/registry")
          and snapshot() == before and exists(f"@R@/newroot/var/lib/ccs/pkg/{PKGV}/session.json"),
          "removal: removing CCS Nucleus removes its chooser first, then only its own files; CCS HOME's package, the owner's files and the other package stay")
    fresh(foreign=True); hakchi("pack_install @R@/hmods/ccs-nucleus.hmod")
    out = nuc("remove remove-data")
    check(not exists("@R@/newroot/var/lib/ccs/nucleus") and not exists("@R@/newroot/var/lib/ccs/owned/nucleus.own") and exists(f"@R@/newroot/var/lib/ccs/pkg/{PKGV}"),
          f"removal: with data removal its registry, state and folders go too, still not CCS HOME's package or saves ({result(out)})")
    # what opens at power-on, set by the PC's reviewed change with CCS Nucleus's own start command (the preparation image's nstart step):
    # before hakchi installs it (the first registry), then changed in place; an owner's system image stays listed through it
    fresh(foreign=True)
    reg = "@R@/newroot/var/lib/ccs/nucleus/registry"
    def rline(): return sh(f"grep -E '^(default|chooser|selection|timeout|options)=' '{reg}' 2>/dev/null").split()
    sh("rm -rf '@R@/npkg' && mkdir -p '@R@/npkg' && tar -xzf '@R@/hmods/ccs-nucleus.hmod' -C '@R@/npkg'")
    def pstart(a: str) -> str: return bb(f"sh @R@/npkg/bin/ccs-nucleus start {a}")   # the package's own program, as the preparation image runs it
    out = pstart("ccs-home always off default 10")
    check(result(out) == "result start refused CCS HOME is not offered" and not exists(reg), f"start: CCS HOME is never what opens first while it is not offered ({result(out)})")
    for bad in ("other never off default 10", "native sometimes off default 10", "native never maybe default 10", "native never off first 10", "native never off default 100"):
        out = pstart(bad)
        check(result(out).startswith("result start refused") and not exists(reg), f"start: only the listed words and a countdown up to 99 ({bad}: {result(out)})")
    out = pstart("ccs-home always on last 10")
    check(result(out) == "result start ok first default=ccs-home chooser=always home=on selection=last timeout=10" and rline() == ["default=ccs-home", "selection=last", "chooser=always", "timeout=10", "options=native", "ccs-home"]
          and "opt.ccs-home.kind=ccs-home" in sh(f"cat '{reg}'"), f"start: before CCS Nucleus is installed, the first registry: CCS HOME offered and opened first, the chooser at start, the menu used last ({result(out)}; {rline()})")
    out = hakchi("pack_install @R@/hmods/ccs-nucleus.hmod")
    check(result(out) == "result postinstall ok installed" and rline()[:4] == ["default=ccs-home", "selection=last", "chooser=always", "timeout=10"],
          f"start: CCS Nucleus's install keeps the registry the PC wrote ({result(out)}; {rline()})")
    out = nuc("start native never off default 0")
    reg_text = sh(f"cat '{reg}'")
    check(result(out) == "result start ok default=native chooser=never home=off selection=default timeout=0" and "ccs-home" not in reg_text and "options=native" in reg_text and "registry generation=" in nuc("status"),
          f"start: changed in place: Original Menu directly, CCS HOME's option and every line of it removed, wait for a choice; the registry is still valid ({result(out)})")
    out = nuc("start native never off default 0")
    check(result(out) == "result start ok default=native chooser=never home=off selection=default timeout=0" and sh(f"cat '{reg}'") == reg_text,
          f"start: the same setting again changes nothing ({result(out)})")
    out = nuc("start ccs-home never on default 10")
    check(result(out) == "result start ok default=ccs-home chooser=never home=on selection=default timeout=10" and "options=native ccs-home" in sh(f"cat '{reg}'"),
          f"start: CCS HOME offered again and opened first ({result(out)})")
    sh(f"echo broken >> '{reg}'")
    out = nuc("start native never off default 10")
    check(result(out) == "result start refused the registry on the console is not valid (it is kept as it is)" and sh(f"tail -n 1 '{reg}'").strip() == "broken",
          f"start: a registry that is not valid is never overwritten ({result(out)})")

def transfer_route():
    """hakchi's own route for both packages (what the preparation's and Console Restore's ccsprep steps place on the data storage): at the
    next normal start hakchi's p7080_hmods runs packs_uninstall on the names in $installpath/transfer/uninstall, then pack_install on every
    *.hmod there, removes the folder, saves its settings and restarts (reboot_kexec: logged here, not run)"""
    T = "@R@/newroot/var/lib/hakchi/transfer"
    def place(*items):
        sh(f"mkdir -p '{T}'")
        for it in items:
            if it.endswith(".hmod"): sh(f"cp '@R@/hmods/{it}' '{T}/{it}'")
            else: sh(f"echo '{it}' > '{T}/uninstall'")
    fresh(foreign=True)
    before = snapshot()
    place("ccs-mod.hmod"); clear_log(); mark()
    out = preinit(); L = since_mark()
    st = mod("status")
    check("RESTART reboot_kexec" in L and not exists(T) and exists("@R@/newroot/var/lib/hakchi/hmod/uninstall-ccs-mod") and "chooser=none" in st and "missing" not in st and "changed" not in st and "entry" in sh(f"ls @R@/newroot/var/lib/hakchi/games/000/CLV-H-CCSMD 2>/dev/null; echo entry"),
          "transfer: at the next start hakchi's own p7080_hmods installs CCS Mod from its transfer folder (CCS Mod's install script in hakchi's start stage), removes the folder and restarts")
    check(snapshot() == before and not [t for t in trips()], f"transfer: nothing of the owner's files or the other package changed; no tripwire ({trips()})")
    place("ccs-nucleus.hmod", "ccs-mod"); clear_log(); mark()
    preinit(); L = since_mark()
    st = nuc("status")
    check("RESTART reboot_kexec" in L and not exists(T) and not exists(f"{HKR}/bin/ccs-mod") and not exists("@R@/newroot/var/lib/hakchi/hmod/uninstall-ccs-mod") and exists("@R@/newroot/var/lib/hakchi/hmod/uninstall-ccs-nucleus")
          and "registry generation=1" in st and not exists(f"{HKR}/etc/preinit.d/p7060_ccsnucleus"),
          "transfer: a removal list naming ccs-mod and CCS Nucleus's package: hakchi runs CCS Mod's own removal first, then installs CCS Nucleus (direct)")
    check(snapshot() == before, "transfer: the change from CCS Mod to CCS Nucleus left games, saves, settings and the other package as they were")
    place("ccs-nucleus-chooser.hmod"); clear_log(); mark()
    preinit()
    check(not exists(T) and exists(f"{HKR}/etc/preinit.d/p7060_ccsnucleus") and exists("@R@/newroot/var/lib/hakchi/hmod/uninstall-ccs-nucleus-chooser") and "chooser=installed" in nuc("status"),
          "transfer: hakchi installs CCS Nucleus's chooser package from its transfer folder the same way")
    place("ccs-nucleus-chooser ccs-nucleus"); clear_log(); mark()
    preinit()
    check(not exists(T) and not exists(f"{HKR}/etc/preinit.d/p7060_ccsnucleus") and not exists("@R@/newroot/var/lib/hakchi/hmod/uninstall-ccs-nucleus") and exists("@R@/newroot/var/lib/ccs/nucleus/registry")
          and not exists("@R@/newroot/var/lib/hakchi/hmod/uninstall-ccs-nucleus-chooser") and snapshot() == before,
          "transfer: a removal list naming the chooser and CCS Nucleus: hakchi runs each one's own removal (their files gone, the registry kept as data)")
    clear_log(); mark(); boot(); L = since_mark()
    check("LOOPMOUNT" not in L and "PLAYER" not in L and "CHOOSER" not in L and "S81clover-mcp the console menu" in L and not [t for t in trips()], "transfer: the next start is the console's own, exactly as before either add-on")

# ---------------------------------------------------------------- the real chooser in the start sequence
REAL_CHOOSER = r"""#!/bin/bash
# tests only: the REAL chooser program (the console build of the native HOME program, ccs-home-armv7 --choose) under qemu-arm, with
# exactly the arguments CCS Nucleus gave it, plus a test framebuffer file and a scripted controller (a FIFO fed with the test's next line
# of key presses, in the console's 16-byte event records)
R="__R__"; Q="__Q__"
echo "CHOOSER $*" >> "$R/log"; /bin/cp "$2" "$R/last-choices"; /bin/cp "$2" "$R/choices-$(/bin/date +%s%N)"
keys=""; if [ -s "$R/keys" ]; then keys=$(/usr/bin/head -n 1 "$R/keys"); /usr/bin/tail -n +2 "$R/keys" > "$R/keys.n"; /bin/mv "$R/keys.n" "$R/keys"; fi
[ "$keys" = - ] && keys=""
/bin/rm -f "$R/pad.fifo"; /usr/bin/mkfifo "$R/pad.fifo"
/usr/bin/python3 "$R/feeder.py" "$R/pad.fifo" 16 "$keys" & fp=$!
# (the console's shutdown asks the chooser to end with TERM: passed on to it, as if this wrapper were not there)
"$Q" "$R/chooser-arm" "$@" --fb-file "$R/fb.raw" --fb-geometry pan=1 --input "$R/pad.fifo" --dump-ppm "$R/shot-$(/bin/date +%s%N).ppm" & cp=$!
trap 'term=1; kill -TERM $cp 2>/dev/null' TERM
wait $cp; rc=$?
[ -n "${term:-}" ] && { wait $cp; rc=$?; }
kill $fp 2>/dev/null; wait $fp 2>/dev/null
echo "CHOOSER-EXIT $rc" >> "$R/log"
exit $rc
"""
FEEDER = """import os, struct, sys, time
fifo, size, keys = sys.argv[1], int(sys.argv[2]), (sys.argv[3] if len(sys.argv) > 3 else "")
CODES = {"a": 304, "b": 305, "select": 314, "start": 315, "left": 704, "right": 705, "up": 706, "down": 707}
fd = os.open(fifo, os.O_WRONLY)
t0 = time.monotonic()
for tok in keys.split():
    ms, btn, st = tok.split(":"); wait = t0 + int(ms) / 1000 - time.monotonic()
    if wait > 0: time.sleep(wait)
    now = time.time(); tv = struct.pack("<II" if size == 16 else "<QQ", int(now), int((now % 1) * 1e6))
    os.write(fd, tv + struct.pack("<HHi", 1, CODES[btn], 1 if st == "p" else 0))
time.sleep(120)
"""
CONTROLLER_MAP = ("device Nintendo Clovercon - controller1\ndevice Nintendo Clovercon - controller2\nkey 304 a\nkey 305 b\nkey 314 select\nkey 315 start\n"
                  "key 704 left\nkey 705 right\nkey 706 up\nkey 707 down\n")   # native_home/console_package.py's CONTROLLER_MAP codes
CHOOSER_BIN = FONTS_DIR = None
def keys(*lines): sh("printf '%s\\n' " + " ".join("'" + (l or "-") + "'" for l in lines) + " > '@R@/keys'")
def trace() -> list[dict]:
    out = sh("cat '@R@/newroot/tmp/ccs/logs/chooser.txt' 2>/dev/null")
    return [json.loads(l) for l in out.splitlines() if l.startswith("{")]
def keys_tok(*steps, start=1000, gap=250):
    """controller steps (button names) as feeder tokens: the first after the controllers' settling, each pressed then let go"""
    t, out = start, []
    for b in steps: out += [f"{t}:{b}:p", f"{t + 60}:{b}:r"]; t += gap
    return " ".join(out)

def chooser_real():
    """the console's start with CCS Nucleus and the REAL chooser program (ccs-home-armv7, the ARM build, under qemu-arm): hakchi's own
    preinit and rcS, S81ccs-nucleus, ccs-nucleus, the chooser on a test framebuffer with a scripted controller"""
    if not CHOOSER_BIN: check(False, "chooser (real): --chooser (the ARM build ccs-home-armv7) was not given"); return
    fresh(foreign=True)
    sh(f"cp '{wpath(CHOOSER_BIN)}' '@R@/chooser-arm' && chmod 755 '@R@/chooser-arm' && head -c 7372800 /dev/zero > '@R@/fb.raw'")
    open(os.path.join(WORK, "feeder.py"), "w", newline="\n").write(FEEDER); sh(f"cp '{wpath(os.path.join(WORK, 'feeder.py'))}' '@R@/feeder.py'")
    w = os.path.join(WORK, "real-chooser"); open(w, "w", newline="\n").write(REAL_CHOOSER.replace("__R__", R).replace("__Q__", QEMU))
    hakchi("pack_install @R@/hmods/ccs-mod.hmod"); hakchi("pack_install @R@/hmods/ccs-nucleus.hmod"); hakchi("pack_install @R@/hmods/ccs-nucleus-chooser.hmod")
    install_registry(default="ccs-home", selection="last", chooser="always")
    pk = f"{HKR}/etc/ccs/nucleus/chooser"
    sh(f"cp '{wpath(w)}' '{pk}/bin/ccs-home-armv7' && chmod 755 '{pk}/bin/ccs-home-armv7'")
    m = os.path.join(WORK, "map.txt"); open(m, "w", newline="\n").write(CONTROLLER_MAP); sh(f"cp '{wpath(m)}' '{pk}/config/controller-map.txt'")
    if FONTS_DIR: sh(f"mkdir -p '{pk}/fonts' && cp '{wpath(FONTS_DIR)}'/silk17.ccsf '{wpath(FONTS_DIR)}'/sans600_22.ccsf '{wpath(FONTS_DIR)}'/sans400_17.ccsf '{pk}/fonts/'")
    sh("mkdir -p '@R@/newroot/var/lib/ccs/nucleus/staging' && { printf hsqs; head -c 65532 /dev/urandom; } > '@R@/newroot/var/lib/ccs/nucleus/staging/up1' && "
       "mkdir -p '@R@/newroot/media/hakchi/firmware' && { printf hsqs; head -c 4092 /dev/urandom; } > '@R@/newroot/media/hakchi/firmware/snes.hsqs'")
    out = nuc(f"import /var/lib/ccs/nucleus/staging/up1 owner-1 {fsha('@R@/newroot/var/lib/ccs/nucleus/staging/up1')} 65536 'My Other System'")
    out2 = nuc("register-foreign /media/hakchi/firmware/snes.hsqs owner-2 'My SNES System'")
    sh("rm -f '@R@/newroot/media/hakchi/firmware/snes.hsqs'")   # its USB storage is taken out
    nuc("set selection default"); nuc("set timeout 2")   # (2 s for the first start: no key is pressed there; 20 s from the second on, longer than the chooser takes to come up under qemu)
    check(result(out).startswith("result import ok") and result(out2).startswith("result register-foreign ok"), "chooser (real): set-up: an imported image and one on USB storage (then unplugged); default CCS HOME, a 2-second countdown")
    # 1. nothing pressed: the countdown starts the default; leaving CCS HOME shows the chooser again, which then waits; UP, A: the console's own
    clear_log(); mark(); keys("", keys_tok("up", "a"))
    boot(); L = since_mark(); t = trace()
    seq = [x for x in L.splitlines() if x.startswith(("CHOOSER-EXIT", "HOME", "S81clover", "S95", "REBOOT", "LOOPMOUNT"))]
    ans = [x["answer"] for x in t if x["ev"] == "answer"]
    check(seq == ["CHOOSER-EXIT 0", "HOME startup", "CHOOSER-EXIT 0", "S81clover-mcp the console menu", "S95 foreign autostart"] and ans == ["choice ccs-home timeout", "choice native a"],
          f"chooser (real): at start the chooser's countdown starts the default (CCS HOME, with its startup picture); leaving it shows the chooser again, which waits; UP and A start the console's own system, whose start continues unchanged ({seq}, {ans})")
    ch = sorted(sh("ls '@R@'/choices-* 2>/dev/null").split())
    c1, c2 = (sh(f"cat '{ch[-2]}'"), sh(f"cat '{ch[-1]}'")) if len(ch) >= 2 else ("", "")
    check("auto=ccs-home\ntimeout=2\n" in c1 and "auto=\ntimeout=0\n" in c2 and "choice=owner-2|owner-root|restart|unavailable|My SNES System|Its system image is on USB storage that is not connected." in c1,
          "chooser (real): the first chooser has the countdown, the one after CCS HOME none; the image on unplugged USB storage is listed as unavailable with its reason")
    shots = sh("ls -1 '@R@'/shot-*.ppm 2>/dev/null | wc -l").strip()
    check(shots == "2" and sh("od -An -tu1 -j $((5120 * 360 + 640 * 4)) -N 3 '@R@/fb.raw' | tr -s ' '").strip() not in ("", "0 0 0"), f"chooser (real): it drew on the framebuffer ({shots} last pictures kept)")
    # 2. DOWN DOWN A on the image whose USB storage is unplugged: refused with the reason; UP, A: the imported image; the console restarts
    nuc("set timeout 20"); sh("rm -f '@R@'/choices-* '@R@'/shot-*.ppm"); clear_log(); mark(); keys(keys_tok("down", "down", "a", "up", "a"))
    boot(); L = since_mark(); t = trace()
    ref = [x["text"] for x in t if x["ev"] == "refused"]; ans = [x["answer"] for x in t if x["ev"] == "answer"]
    check(ref == ["My SNES System cannot start now. Its system image is on USB storage that is not connected."] and ans == ["choice owner-1 a"] and "REBOOT" in L and "HOME" not in L and "S81clover" not in L
          and sh("cat '@R@/newroot/var/lib/ccs/nucleus/state/next'").startswith("schema=ccs-nucleus-state/1\nid=owner-1"),
          f"chooser (real): A on the unavailable image is refused with its reason on the TV; the imported image chosen next: a one-time request, then hakchi's restart, nothing else started ({ref}, {ans})")
    clear_log(); mark(); keys()
    boot(); L = since_mark()
    check("LOOPMOUNT /var/lib/ccs/nucleus/roots/owner-1.hsqs" in L and "CHOOSER" not in L and "S81clover-mcp the console menu" in L and cfg_firmware() == "cfg_firmware='auto'",
          "chooser (real): the restart starts that image through hakchi's own loadFirmware; nothing of CCS Nucleus is on the TV there; the owner's cfg_firmware is unchanged")
    # 3. from that image's menu (the menu entry CCS Nucleus): the console's own system means a restart back (a different root)
    clear_log(); keys(keys_tok("up", "a"))
    out = nuc("choose"); L = log(); t = trace()
    check("REBOOT" in L and [x["answer"] for x in t if x["ev"] == "answer"][-1:] == ["choice native a"] and "id=native" in sh("cat '@R@/newroot/var/lib/ccs/nucleus/state/next'")
          and "choice=native|native|restart|ok|" in sh("cat '@R@/last-choices'") and "choice=owner-1|owner-root|same|ok|" in sh("cat '@R@/last-choices'"),
          "chooser (real): in an owner's image the chooser shows the console's own system and CCS HOME as restarts and that image as the system running now; choosing the console's own system restarts into it")
    clear_log(); mark(); keys(keys_tok("a"))
    boot(); L = since_mark()
    check("LOOPMOUNT" not in L and "CHOOSER" not in L and "S81clover-mcp the console menu" in L and "this-start id=native kind=native src=request" in nuc("status"),
          "chooser (real): that restart is the console's own system, straight away (the request is used once; no chooser in between)")
    # 4. Recovery and Settings: turn the chooser off at start; the panel comes back with what was done; then its recovery entry
    sh("rm -f '@R@'/choices-*"); clear_log(); mark(); keys(keys_tok("up", "up", "a", "down", "down", "a"), keys_tok("up", "up", "a"))
    boot(); L = since_mark(); t = trace(); st = nuc("status"); ch = sorted(sh("ls '@R@'/choices-* 2>/dev/null").split())
    c2 = sh(f"cat '{ch[-1]}'") if ch else ""
    check([x["answer"] for x in t if x["ev"] == "answer"] == ["action show", "choice native a"] and "chooser=never" in st and "panel=settings\n" in c2 and "highlight=show\n" in c2
          and "notice=This screen no longer shows at start. It still opens when you leave CCS HOME, or from CCS Nucleus in your console's menu." in c2 and "setting=show|Show This Screen at Start: Off" in c2
          and "HOME" not in L and "S81clover-mcp the console menu" in L,
          "chooser (real): Recovery and Settings turns the chooser off at start (the registry changes; the panel shows again with what was done); its recovery entry starts the console's own system")
    clear_log(); mark(); keys(keys_tok("up", "a"))
    boot(); L = since_mark()
    seq = [x for x in L.splitlines() if x.startswith(("CHOOSER-EXIT", "HOME", "S81clover"))]
    check(seq == ["HOME startup", "CHOOSER-EXIT 0", "S81clover-mcp the console menu"] and [x["answer"] for x in trace() if x["ev"] == "answer"] == ["choice native a"],
          f"chooser (real): the next start has no chooser before CCS HOME: the default (CCS HOME) starts; leaving it shows the chooser (its leave item: Choose Another System); UP, A: the console's own start ({seq})")
    # 5. from the console's menu: B goes back without starting anything; hakchi's game-hang watch is fed while it waits
    sh("rm -f '@R@/newroot/tmp/.heartbeat'"); clear_log(); keys(keys_tok("b", start=2500))
    out = nuc("choose"); L = log()
    check("CHOOSER-EXIT 0" in L and "HOME" not in L and "REBOOT" not in L and [x["answer"] for x in trace() if x["ev"] == "answer"][-1:] == ["back"] and exists("@R@/newroot/tmp/.heartbeat"),
          "chooser (real): opened from the console's menu, B goes back to it; nothing is started; the menu's game-hang watch was fed while the chooser waited")
    # 6. the console shuts down while the chooser waits: it is asked to end (TERM), answers nothing, nothing is started
    clear_log(); keys()
    out = bb(f"sh {HKR}/bin/ccs-nucleus choose & sleep 8; sh {HKR}/bin/ccs-nucleus ui-stop; wait; echo DONE")
    L = log()
    check("DONE" in out and "CHOOSER-EXIT 15" in L and "HOME" not in L and "REBOOT" not in L and any(x["ev"] == "stopped" for x in trace()),
          f"chooser (real): at shutdown (ui-stop) the waiting chooser ends at once without an answer and nothing more is started ({[x for x in L.splitlines() if x.startswith('CHOOSER-EXIT')]})")
    # 7. CCS HOME ends with a problem at start: the chooser comes back with the reason and counts down to the console's own system
    nuc("set chooser always"); nuc("set timeout 2"); sh("echo 3 > '@R@/player-rc'"); sh("rm -f '@R@'/choices-*"); clear_log(); mark(); keys("", "")
    boot(); L = since_mark(); t = trace(); ch = sorted(sh("ls '@R@'/choices-* 2>/dev/null").split()); c2 = sh(f"cat '{ch[-1]}'") if ch else ""
    seq = [x for x in L.splitlines() if x.startswith(("CHOOSER-EXIT", "HOME", "S81clover"))]
    check(seq == ["CHOOSER-EXIT 0", "HOME startup", "CHOOSER-EXIT 0", "S81clover-mcp the console menu"] and [x["answer"] for x in t if x["ev"] == "answer"] == ["choice ccs-home timeout", "choice native timeout"]
          and "notice=CCS HOME ended with a problem.\n" in c2 and "highlight=native\nauto=native\ntimeout=2\n" in c2 and "choice=ccs-home|ccs-home|same|unavailable|CCS HOME|It ended with a problem just now." in c2,
          f"chooser (real): CCS HOME that ends with a problem at start: the chooser shows why, CCS HOME cannot be chosen again in this start, and an unattended console counts down to its own system ({seq})")
    sh("rm -f '@R@/player-rc'")
    # 8. no CCS HOME package (CCS Mod removed, say): the chooser has its own program, shows CCS HOME as unavailable with its reason, and
    #    its countdown at start goes to the console's own system instead (nothing waits on the TV)
    sh("rm -f '@R@'/newroot/var/lib/ccs/nucleus/state/failed-* && mv '@R@/newroot/var/lib/ccs/pkg/current' '@R@/pkg-current.keep'"); clear_log(); mark(); keys()
    boot(); L = since_mark(); ch = sh("cat '@R@/last-choices'")
    check("CHOOSER-EXIT 0" in L and "HOME startup" not in L and "S81clover-mcp the console menu" in L and "auto=native" in ch
          and "choice=ccs-home|ccs-home|same|unavailable|CCS HOME|CCS HOME is not completely installed." in ch,
          f"chooser (real): without a CCS HOME package the chooser still shows (its own program), lists CCS HOME as unavailable with its reason, and its countdown starts the console's own system ({[x for x in L.splitlines() if x.startswith(('CHOOSER-EXIT', 'HOME', 'S81'))]})")
    sh("mv '@R@/pkg-current.keep' '@R@/newroot/var/lib/ccs/pkg/current'")
    check(not [x for x in trips() if "foreign" not in x], f"chooser (real): no tripwire ({trips()})")

# ---------------------------------------------------------------- the installation chain with the package's own console player
# The console's display and controllers for the package's real ARM player (tests only): the player's own documented test seams (a framebuffer
# file, an input FIFO that nobody writes, no sound device), added by this stand-in for the hardware; every argument the launcher gives is
# passed unchanged. It runs when the launcher (BusyBox under qemu-arm) starts the package's bin/ccs-player-armv7: inside a user namespace of
# its own, a binfmt_misc entry names this file as the interpreter of 32-bit ARM programs (nothing outside that namespace sees it). The HOME is
# left the way the console's shutdown ends it (SIGTERM: ccs_home_stop) once it shows its games, so the launcher's own return is exercised.
PLAYER_SHIM = r'''#!/bin/bash
R="__R__"; Q="__Q__"; bin="$1"; shift
case "$bin" in
  */bin/ccs-player-armv7)
    tr=""; a=("$@"); for ((i=0; i<${#a[@]}; i++)); do [ "${a[$i]}" = --trace ] && tr="${a[$((i+1))]}"; done
    echo "PLAYER-ARM $bin $*" >> "$R/log"
    rm -f "$R/in.fifo"; mkfifo "$R/in.fifo"; ( sleep 600 > "$R/in.fifo" ) & keep=$!
    head -c 7372800 /dev/zero > "$R/fb.raw"
    QEMU_LD_PREFIX="$R/newroot" "$Q" "$bin" "$@" --fb-file "$R/fb.raw" --fb-geometry "xres=1280,yres=720,vxres=1280,vyres=1440,bpp=32,line=5120,smem=7372800,r=16,g=8,b=0,a=-1,pan=1" \
      --input "$R/in.fifo" --audio off --test-dump-ppm "$R/frame.ppm" > "$R/player.out" 2>&1 &
    p=$!
    for i in $(seq 1 180); do sleep 1; grep -q '"screen":"grid"' "$tr" 2>/dev/null && break; kill -0 $p 2>/dev/null || break; done
    sleep 2; kill -TERM $p 2>/dev/null; wait $p; rc=$?; kill $keep 2>/dev/null; rm -f "$R/in.fifo"
    echo "PLAYER-ARM-END $rc" >> "$R/log"; exit $rc ;;
  *) exec "$Q" "$bin" "$@" ;;
esac
'''
NS_HEAD = r'''set -e
/usr/bin/mount -t binfmt_misc binfmt_misc /proc/sys/fs/binfmt_misc
printf '%s' ':ccsarm:M::\x7fELF\x01\x01\x01\x00\x00\x00\x00\x00\x00\x00\x00\x00\x02\x00\x28\x00:\xff\xff\xff\xff\xff\xff\xff\x00\xff\xff\xff\xff\xff\xff\xff\xff\xfe\xff\xff\xff:@R@/playershim:' > /proc/sys/fs/binfmt_misc/register
set +e
'''
CHAIN = [None]

def sh_ns(text: str, timeout: int = 900) -> str:
    """a bash script run in a user and mount namespace of its own where 32-bit ARM programs start through PLAYER_SHIM (binfmt_misc in that
    namespace only); the qemu runs inside see the same files"""
    global _n
    _n += 1; f = os.path.join(WORK, f"ns{_n:04d}.sh")
    open(f, "w", newline="\n").write((NS_HEAD + text).replace("@R@", R or "").replace("@Q@", QEMU or ""))
    return sh(f"unshare --user --map-root-user --mount bash '{wpath(f)}'", timeout)

def chain():
    """the whole chain on PC-emulated fixtures: the data storage as the Workbench's preparation image left it (test_recovery_images.py
    --home-archive --export-data: the shared engine's own CCS HOME archive, CCS Mod's and CCS Nucleus's packages and CCS Nucleus's start
    (CCS HOME straight away), placed by the image's own ccsprep steps), then hakchi's own start installs both add-ons from its transfer
    folder; the next start opens the package's own console player first through CCS Nucleus, then the console's start continues to
    Original Menu; CCS Mod adds nothing of its own at power-on; the CCS HOME entry starts the same player from the console's menu; CCS
    Nucleus's own start command switches what opens first"""
    if not CHAIN[0]: check(False, "chain: --chain-data (test_recovery_images.py --export-data) is needed"); return
    fresh(foreign=True)
    sh("rm -rf '@R@/newroot/var/lib/ccs'"); sh(f"tar -xf '{wpath(CHAIN[0])}' -C '@R@/newroot/var/lib' && echo OK")
    p = "@R@/newroot/var/lib/ccs/pkg"; cur = sh(f"cat '{p}/current'").strip(); T = "@R@/newroot/var/lib/hakchi/transfer"
    elf = sh(f"head -c 20 '{p}/{cur}/bin/ccs-player-armv7' | od -An -tx1 | tr -d ' \\n'").strip()
    sums = sh(f"cd '{p}/{cur}' && sed -n 's/^  \"\\([^\"]*\\)\": \"\\([0-9a-f]*\\)\",\\{{0,1\\}}$/\\2  \\1/p' '../../install/v-{cur}.json' > /tmp/ccsnu-sums.$$ && sha256sum -c --quiet /tmp/ccsnu-sums.$$ && echo VERIFIED; wc -l < /tmp/ccsnu-sums.$$; rm -f /tmp/ccsnu-sums.$$")
    reg = sh("cat '@R@/newroot/var/lib/ccs/nucleus/registry' 2>/dev/null")
    check(bool(re.match(r"^[0-9a-f]{16}\.[0-9a-f]{8}$", cur)) and "VERIFIED" in sums and fsha(f"{T}/ccs-mod.hmod") == B.METHOD_SHAS["ccs-mod"] and fsha(f"{T}/ccs-nucleus.hmod") == B.METHOD_SHAS["ccs-nucleus"]
          and elf.startswith("7f454c46010101") and elf[36:40] == "2800" and "default=ccs-home" in reg and "chooser=never" in reg and not exists(f"{HKR}/etc/init.d/S81ccs-mod"),
          f"chain: the data storage as the preparation image left it: CCS Mod's and CCS Nucleus's pinned packages waiting for hakchi, CCS Nucleus's start (CCS HOME straight away) written, CCS HOME's copy {cur} in use, every file as its record lists ({sums.split()[-1]} files), its player a 32-bit ARM program")
    before = snapshot()
    clear_log(); mark(); preinit(); L = since_mark(); st = mod("status"); ns = nuc("status")
    check("RESTART reboot_kexec" in L and not exists(T) and exists("@R@/newroot/var/lib/hakchi/hmod/uninstall-ccs-mod") and exists("@R@/newroot/var/lib/hakchi/hmod/uninstall-ccs-nucleus")
          and "package=" in st and "chooser=none" in st and f"home=/var/lib/ccs/pkg/{cur}" in st and "default=ccs-home" in ns
          and exists("@R@/newroot/var/lib/hakchi/games/000/CLV-H-CCSMD/CLV-H-CCSMD.desktop") and not [x for x in st.splitlines() if x.startswith(("changed", "missing"))],
          f"chain: the next normal start: hakchi's own p7080_hmods installs CCS Mod and CCS Nucleus from its transfer folder (their own install scripts), CCS Mod adds CCS HOME's entry to the root menu, CCS Nucleus keeps the start written for it, and the console restarts ({[x for x in st.splitlines() if x.startswith(('package', 'chooser', 'home'))]})")
    check(snapshot() == before and not trips(), f"chain: the owner's files, the other package and CCS HOME's copy unchanged by that start; no tripwire ({trips()})")
    open(os.path.join(WORK, "playershim"), "w", newline="\n").write(PLAYER_SHIM.replace("__R__", R).replace("__Q__", QEMU))
    sh(f"cp '{wpath(os.path.join(WORK, 'playershim'))}' '@R@/playershim' && chmod 755 '@R@/playershim'")
    rc_cmd = ("rm -rf '@R@/etc-merged/init.d' && mkdir -p '@R@/etc-merged/init.d' && cp -a '@R@/newroot/var/squashfs/etc/init.d/.' '@R@/etc-merged/init.d/' && "
              f"for f in {HKR}/etc/init.d/S81ccs-* {HKR}/etc/init.d/S95foreign-*; do [ -f \"$f\" ] && cp -a \"$f\" '@R@/etc-merged/init.d/'; done; "
              "for f in '@R@'/etc-merged/init.d/*; do sed -i \"1s#^\\#!/bin/sh#\\#!@R@/wrap/sh#\" \"$f\"; done; "
              "cd '@R@' && env -i HOME=/ PATH='@R@/stubs:@R@/wrap' CCS_ROOT='@R@/newroot' CCSTEST_LOG='@R@/log' '@Q@' -L '@R@/root' '@R@/root/bin/busybox' sh '@R@/rcS' 2>&1 | tail -n 20")
    def started():   # one console start with the real ARM player able to run: hakchi's preinit, then its rcS in the player's namespace
        clear_log(); mark(); sh("rm -rf '@R@/newroot/tmp/ccs/logs' '@R@/frame.ppm'")
        sh("rm -rf '@R@/newroot/var/ccs-nucleus' '@R@/newroot/tmp' '@R@/attempt-snap' '@R@/loops'"); preinit()
        sh_ns(rc_cmd); L = since_mark(); tr = sh("cat '@R@/newroot/tmp/ccs/logs/trace.jsonl' 2>/dev/null")
        return [l.split(" ")[0] if l.startswith("PLAYER-ARM ") else l for l in L.splitlines() if l.startswith(("S80", "PLAYER-ARM ", "PLAYER-ARM-END", "S81clover", "S95"))], tr
    inits = sh(f"ls '{HKR}/etc/init.d/' 2>/dev/null").split()
    check([x for x in inits if "ccs" in x] == ["S81ccs-nucleus"] and not [x for x in st.splitlines() if "/etc/init.d/" in x or "/etc/preinit.d/" in x],
          f"chain: CCS Nucleus's one start script is the only one; CCS Mod's install adds nothing that runs at power-on ({inits})")
    order, tr = started()
    check(order == ["S80 drivers", "PLAYER-ARM", "PLAYER-ARM-END 0", "S81clover-mcp the console menu", "S95 foreign autostart"] and '"count":2' in tr,
          f"chain: CCS HOME first, set with CCS Nucleus: at the next start rcS runs S81ccs-nucleus, which opens the package's own player with its HOME; leaving it, the console's own menu start and the other package's autostart continue unchanged ({order})")
    desk = sh("cat '@R@/newroot/var/lib/hakchi/games/000/CLV-H-CCSMD/CLV-H-CCSMD.desktop'"); ex = next((l[5:] for l in desk.splitlines() if l.startswith("Exec=")), "")
    check(ex == "/bin/sh /var/lib/hakchi/rootfs/bin/ccs-mod launch", f"chain: the entry's Exec line is CCS Mod's launcher ({ex})")
    clear_log(); sh("rm -rf '@R@/newroot/tmp/ccs/logs' '@R@/frame.ppm'")
    q = f"sh {HKR}/bin/ccs-mod launch".replace("'", "'\\''")
    out = sh_ns(f"cd '@R@' && env -i HOME=/ PATH='@R@/stubs:@R@/wrap' CCS_ROOT='@R@/newroot' mountpoint='' '@Q@' -L '@R@/root' '@R@/root/bin/busybox' sh -c '{q}' 2>&1")
    L = log(); tr = sh("cat '@R@/newroot/tmp/ccs/logs/trace.jsonl' 2>/dev/null")
    home = next((l for l in tr.splitlines() if '"screen":"grid"' in l), "")
    frame = sh("f='@R@/frame.ppm'; [ -f \"$f\" ] && { wc -c < \"$f\"; tail -c 2764800 \"$f\" | tr -d '\\000' | wc -c; }").split()
    check(f"PLAYER-ARM {R}/newroot/tmp/ccs/pkg/bin/ccs-player-armv7 --session {R}/newroot/tmp/ccs/pkg/session.json" in L and '"count":2' in home and '"theme":"featured-shelf"' in home and "PLAYER-ARM-END 0" in L
          and result(out).startswith("result launch ok home-ended 0") and len(frame) == 2 and int(frame[0]) > 2764800 and int(frame[1]) > 100000,
          f"chain: CCS HOME's entry starts the package's own console player (pkg/current -> {cur}): its HOME shows the package's 2 games on the display, ends cleanly and returns to the console's menu ({result(out)}; frame {frame})")
    check(exists("@R@/newroot/tmp/.heartbeat") and '"startup_shown"' not in tr, "chain: from the menu: no startup picture, and hakchi's game-hang watch is kept fed while it runs")
    out = nuc("start native never on default 10"); order, tr = started()
    check(result(out).startswith("result start ok default=native") and order == ["S80 drivers", "S81clover-mcp the console menu", "S95 foreign autostart"],
          f"chain: CCS Nucleus's own start command set to Original Menu: the next start is the console's own menu, no CCS HOME player ({result(out)}; {order})")
    out = nuc("start ccs-home never on default 10"); order, tr = started()
    check(result(out).startswith("result start ok default=ccs-home") and order == ["S80 drivers", "PLAYER-ARM", "PLAYER-ARM-END 0", "S81clover-mcp the console menu", "S95 foreign autostart"] and '"count":2' in tr,
          f"chain: set to CCS HOME again: the next start opens CCS HOME first, then the console's own start continues ({result(out)}; {order})")
    out = mod("autostart on")
    check(result(out).startswith("result autostart refused unknown command") and not exists(f"{HKR}/etc/init.d/S81ccs-mod"), f"chain: CCS Mod has no start of its own to turn on ({result(out)})")
    check(not trips(), f"chain: no tripwire in any start ({trips()})")

# ---------------------------------------------------------------- the collection as ordinary Hakchi games (console/collections/ccs-collections)
def collections_tests():
    """the console program for Hakchi games on the pinned BusyBox, with hakchi's own functions: a publication made on the PC
    (tools/companion/console_engine/hakchi_publish.py) checked and applied on a stand-in console with Hakchi2 CE's separate game storage,
    compared byte for byte with the PC-side emulation the installer's tests use, opened by hakchi's own overmount_games, read by the
    separately licensed kit's reader with the console's own BusyBox awk, updated, removed (a changed entry kept); then a console without
    hakchi game storage, through hakchi's next start"""
    sys.path.insert(0, os.path.join(TOOLS, "companion"))
    from console_engine import hakchi_publish as HP, ccs_install as CI
    from PIL import Image
    def sha(b): return hashlib.sha256(b).hexdigest()
    def rom(mapper, seed, header=True):
        h = bytearray(16); h[0:4] = b"NES\x1a"; h[4], h[5] = 1, 1; h[6] = (mapper & 0x0F) << 4; h[7] = mapper & 0xF0
        body = bytes((seed * 7 + i * 13) & 0xFF for i in range(16384 + 8192)); return (bytes(h) if header else b"") + body
    def package(d, games, folders):
        """a console package with these synthetic games (made for this test; not games)"""
        files = {}; sg, gd = [], []
        for g in games:
            b = g.get("rom"); s = {"id": g["id"], "title": g["title"], "rom": "", "header_hex": g.get("hdr", ""), "content_sha256": "", "unavailable": ""}
            if b is not None:
                cs = sha(b[16:] if b[:4] == b"NES\x1a" else b); files[f"roms/{cs}.nes"] = b; s.update(rom=f"/tmp/ccs/pkg/roms/{cs}.nes", content_sha256=cs)
            sg.append(s); rec = {"id": g["id"], "title": g["title"], "players": "1-2", "year": "1990", "publisher": "Test", "builtin_code": g.get("builtin"), "home_kind": "factory" if g.get("builtin") else "added"}
            if g.get("cover"):
                o = io.BytesIO(); Image.new("RGB", (120, 170), g["cover"]).save(o, "JPEG"); files[f"home/cover/{g['id']}.jpg"] = o.getvalue(); rec["cover"] = f"cover/{g['id']}.jpg"
            gd.append(rec)
        files["session.json"] = json.dumps({"games": sg}).encode(); files["home/games.json"] = json.dumps({"games": gd}).encode()
        files["home/config.json"] = json.dumps({"games": [g["id"] for g in games], "folders": folders, "favorites": [games[0]["id"]]}).encode()
        files["MANIFEST.json"] = json.dumps({"label": "Test Shelf", "console_paths": {"pkg": "/tmp/ccs/pkg"}}).encode()
        for rel, b in files.items(): os.makedirs(os.path.dirname(os.path.join(d, rel)), exist_ok=True); open(os.path.join(d, rel), "wb").write(b)
        return files
    def put_dir(local, target):   # a folder made on the PC, copied into the stand-in
        sh(f"mkdir -p '{target}' && cp -r '{wpath(local)}/.' '{target}/' && echo DONE")
    def tree_of(sub):   # path -> SHA-256 of every file under a stand-in folder
        out = sh(f"cd '@R@/newroot' && find {sub} -type f 2>/dev/null | sort | xargs -r sha256sum")
        return dict((l.split()[1].lstrip("./"), l.split()[0]) for l in out.splitlines() if len(l.split()) == 2)
    def others():   # every file of the owner and other tools: everything but this program's folders, records and the stage
        out = sh(r"cd '@R@/newroot' && find . -type f ! -path '*/ccs-[0-9a-f]*' ! -path './var/lib/ccs/*' ! -path './tmp/*' ! -path './var/lib/hakchi/rootfs/etc/preinit.d/p0000_config' | sort | xargs -r sha256sum")
        return dict((l.split()[1], l.split()[0]) for l in out.splitlines() if len(l.split()) == 2)
    def jpaths():   # the files this program's journal names now
        t = sh("cat '@R@/newroot/var/lib/ccs/owned/published.own' 2>/dev/null")
        return {"." + l.split(" ", 2)[2] for l in t.splitlines() if l.startswith("file=")}
    def kept(before, jb):   # every file of the owner and other tools as it was (this program's own files, by its journal, apart): (ok, what differs)
        now = others(); ours = jb | jpaths(); bad = sorted(k for k in set(before) | set(now) if before.get(k) != now.get(k) and k not in ours)
        return not bad, bad[:5]
    fresh(foreign=True)
    sh("cd '@R@/newroot/var/lib/hakchi/games' && mkdir nes-usa && mv 000 nes-usa/ && mkdir -p '@R@/newroot/var/squashfs/usr/share/games/nes/kachikachi/CLV-P-NAAAE' && "
       "printf '[Desktop Entry]\\nType=Application\\nExec=/usr/bin/clover-kachikachi /usr/share/games/nes/kachikachi/CLV-P-NAAAE/CLV-P-NAAAE.nes --volume 75\\nPath=/var/lib/clover/profiles/0/CLV-P-NAAAE\\nName=A stand-in built-in game\\nIcon=/usr/share/games/nes/kachikachi/CLV-P-NAAAE/CLV-P-NAAAE.png\\n' "
       "> '@R@/newroot/var/squashfs/usr/share/games/nes/kachikachi/CLV-P-NAAAE/CLV-P-NAAAE.desktop' && echo DONE")
    prog = HP.program(); pd = os.path.join(WORK, "prog"); os.makedirs(pd, exist_ok=True)
    for n, b in prog["files"].items(): open(os.path.join(pd, n), "wb").write(b)
    P = "@R@/newroot" + prog["dir"]; put_dir(pd, P)
    def coll(args, env=""): return bb(f"sh {P}/ccs-collections {args}", env)
    games = [{"id": "a", "title": "Alpha", "rom": rom(0, 1), "cover": (200, 30, 30)}, {"id": "b", "title": "Bravo", "rom": rom(4, 2)},
             {"id": "c", "title": "Charlie Headerless", "rom": rom(1, 3, header=False), "hdr": rom(1, 3)[:16].hex()}, {"id": "d", "title": "Delta 30", "rom": rom(30, 4)},
             {"id": "e", "title": "A built-in game", "builtin": "CLV-P-NAAAE"}]
    folders = [{"id": "f1", "name": "First", "parent": None, "pos": 0, "order": ["a", "b"]}, {"id": "f2", "name": "Second", "parent": "f1", "pos": 0, "order": ["c", "a"]}]
    pk = os.path.join(WORK, "cpkg"); pf = package(pk, games, folders); sp = HP.spec(pk)
    roms = os.path.join(WORK, "croms"); os.makedirs(roms, exist_ok=True)
    for rel, b in pf.items():
        if rel.startswith("roms/"): open(os.path.join(roms, rel[5:]), "wb").write(b)
    put_dir(roms, f"@R@/newroot/var/lib/ccs/pkg/{PKGV}/roms")
    # where: hakchi's own findGameStorage finds Hakchi2 CE's separate storage (nes-usa), its first page "000"
    w = HP.parse_output(coll("where"))
    check(w["ok"] and w["where"].get("mode") == "storage" and w["where"].get("store") == "/var/lib/hakchi/games/nes-usa" and w["where"].get("menu") == "/var/lib/hakchi/games/nes-usa/000"
          and w["where"].get("system") == "/var/squashfs/usr/share/games/nes/kachikachi" and w["where"].get("index") == "none",
          f"collections: where the console's menu lists games, by hakchi's own findGameStorage and softwareCheck's reads: Hakchi2 CE's separate storage ({w['where']})")
    where = {"mode": "storage", "store": w["where"]["store"], "menu": w["where"]["menu"], "system": w["where"]["system"]}
    def stage(lay, sid):
        st = HP.stage_files(lay); d = os.path.join(WORK, "stage-" + sid)
        for rel, b in st.items(): os.makedirs(os.path.dirname(os.path.join(d, rel)), exist_ok=True); open(os.path.join(d, rel), "wb").write(b)
        sp_ = f"/var/lib/ccs/integration/stage/{sid}"; put_dir(d, "@R@/newroot" + sp_); return sp_, d
    def finish(spec_, lay, sp_, d, prev, gen):
        ck = HP.parse_output(coll(f"check {sp_}")); doc = HP.index_doc(spec_, lay, where, ck["entries"], gen); ib = HP.index_bytes(doc)
        skip = [k for k, s in ck["entries"].items() if s not in ("free", "ours")]
        open(os.path.join(d, "index.json"), "wb").write(ib); open(os.path.join(d, "apply"), "wb").write(HP.apply_file(skip, sha(ib), prev, gen))
        put_dir(d, "@R@/newroot" + sp_); return ck, doc, ib
    lay = HP.layout(sp, "storage", PKGV); sp1, d1 = stage(lay, "0a0b0c0d")
    ck, doc, ib = finish(sp, lay, sp1, d1, None, 1)
    check(ck["ok"] and set(ck["entries"].values()) == {"free"} and set(ck["menus"].values()) == {"free"} and len(ck["entries"]) == len(lay["entries"]),
          f"collections: check reads only and finds every entry and folder free ({ck['result']}; {len(ck['entries'])} entries; {[k + ' ' + v for k, v in ck['entries'].items() if v != 'free']})")
    owners = others()
    sh("cd '@R@/newroot' && tar -cf '@R@/pre.tar' var/lib/hakchi/games var/squashfs/usr/share/games var/lib/ccs && echo DONE")
    jb = jpaths(); out = coll(f"apply {sp1}"); r = HP.parse_output(out); ok, bad = kept(owners, jb)
    check(r["ok"] and f"entries={len(lay['entries'])}" in r["result"] and ok,
          f"collections: apply writes the collection's entries and folders and nothing of the owner's or another tool's ({r['result']}; {len(owners)} other files unchanged; differ: {bad})")
    gs = "@R@/newroot/var/lib/hakchi/games/nes-usa"; by = {g["ccs_id"]: g for g in sp["games"]}; col = sp["collection"]
    check(exists(f"{gs}/000/{col['code']}/{col['code']}.desktop") and exists(f"{gs}/000/CLV-H-AAAAA/CLV-H-AAAAA.desktop") and exists(f"{gs}/{by['a']['menu']}/{by['a']['code']}/{by['a']['code']}_small.png")
          and not exists(f"{gs}/{by['a']['menu']}/{by['a']['code']}/{by['a']['code']}.nes") and exists(f"{gs}/{by['c']['menu']}/{by['c']['code']}/{by['c']['code']}.nes")
          and sh(f"grep -c 'Exec=/bin/clover-kachikachi-wr /var/lib/ccs/pkg/{PKGV}/roms/' '{gs}/{by['a']['menu']}/{by['a']['code']}/{by['a']['code']}.desktop'").strip() == "1",
          "collections: the collection's folder in the first page beside Hakchi's own game; a game's entry names the installed copy's file (no second copy); a file without its header gets Hakchi's copy")
    bi = sh(f"cat '{gs}/{by['e']['menu']}/CLV-P-NAAAE/CLV-P-NAAAE.desktop'")
    check("Exec=/usr/bin/clover-kachikachi /var/squashfs/usr/share/games/nes/kachikachi/CLV-P-NAAAE/CLV-P-NAAAE.nes --volume 75" in bi and "Path=/var/saves/CLV-P-NAAAE" in bi
          and "Icon=/var/squashfs/usr/share/games/nes/kachikachi/CLV-P-NAAAE/CLV-P-NAAAE.png" in bi and sh(f"ls '{gs}/{by['e']['menu']}/CLV-P-NAAAE'").split() == ["CLV-P-NAAAE.desktop"],
          f"collections: a built-in game's entry is made on the console from its own entry, pointed at hakchi's view of the system (nothing copied) ({bi.strip()[:120]})")
    check(fsha("@R@/newroot/var/lib/ccs/integration/v1/index.json") == sha(ib) and by["d"]["status"] == "skip" and "mapper 30" in by["d"]["reason"] and doc["games"][by["d"]["id"]]["status"] == "not-published",
          "collections: the index is the staged one, put in place by one rename; a game the console's own emulator does not run has no entry")
    # the same publication on the PC-side emulation (the installer's stand-in): the same files, byte for byte, and the same journal
    import tarfile as _tf
    fcr = os.path.join(WORK, "fc"); shutil.rmtree(fcr, ignore_errors=True); os.makedirs(fcr)
    sh(f"cp '@R@/pre.tar' '{wpath(os.path.join(WORK, 'pre.tar'))}' && echo DONE")
    with _tf.open(os.path.join(WORK, "pre.tar")) as t: t.extractall(fcr, filter="data")
    json.dump({"hwid": "sim-fidelity"}, open(os.path.join(fcr, "console.json"), "w"))
    em = HP.parse_output(HP.emulate(CI.FakeConsole(fcr), "apply", sp1))
    def local_tree(root, sub):
        base = os.path.join(root, *sub.split("/")); out = {}
        for dd, _, fs in os.walk(base):
            for f in fs: q = os.path.join(dd, f); out[os.path.relpath(q, root).replace(os.sep, "/")] = hashlib.sha256(open(q, "rb").read()).hexdigest()
        return out
    subs = ("var/lib/hakchi/games", "var/lib/ccs/integration/v1", "var/lib/ccs/owned")
    q_tree = {k: v for s in subs for k, v in tree_of(s).items()}; e_tree = {k: v for s in subs for k, v in local_tree(fcr, s).items()}
    check(em["ok"] and em["result"] == r["result"] and q_tree == e_tree,
          f"collections: the PC-side emulation (the installer's stand-in) gives the same result line and the same files and journal, byte for byte ({len(q_tree)} files; {sorted(set(q_tree) ^ set(e_tree))[:3] or 'none differ in name'})")
    # hakchi's own functions open the collection's folder: overmount_games binds it over the console's game folder and links /var/games
    sh(": > '@R@/mounts.log'")
    hakchi(f"overmount_games {col['menu']}")
    ml = sh("cat '@R@/mounts.log'")
    check(f"/var/lib/hakchi/games/nes-usa/{col['menu']}" in ml and "/usr/share/games/nes/kachikachi" in ml,
          f"collections: hakchi's own overmount_games accepts the collection's folder (it holds games) and binds it over the console's game folder ({ml.strip()[-200:]})")
    # the separately licensed kit's reader, with the console's own BusyBox sh and awk
    kit = os.path.join(TOOLS, "collection-interface-kit", "reader"); put_dir(kit, "@R@/kit")
    rd_out = bb("sh @R@/kit/ccs-ci -f @R@/newroot/var/lib/ccs/integration/v1/index.json games installed", "TMPDIR='@R@/tmp'")
    exp = "".join(f"{gid}\t{doc['games'][gid]['title']}\t" + (doc["bases"]["entries"] + "/" + doc["games"][gid]["entry"]["path"] if doc["games"][gid].get("entry") else "-") + "\n"
                  for gid in doc["collections"][0]["games"])
    check(rd_out == exp, f"collections: the kit's reference reader on the console's own BusyBox reads the index CCS wrote ({rd_out.strip()[:160]!r})")
    fo = bb("sh @R@/kit/ccs-ci -f @R@/newroot/var/lib/ccs/integration/v1/index.json folder-games installed " + doc["collections"][0]["folders"][1]["id"], "TMPDIR='@R@/tmp'")
    check(fo.split() == [by["c"]["id"], by["a"]["id"]], f"collections: a folder lists every game it has, also one Hakchi's folders show in another ({fo.split()})")
    # an update: one game less, one more: only this program's entries change, the earlier entry is removed, Hakchi's own entries stay
    g2 = [g for g in games if g["id"] != "b"] + [{"id": "g", "title": "Golf New", "rom": rom(2, 7)}]
    pk2 = os.path.join(WORK, "cpkg2"); pf2 = package(pk2, g2, folders); sp2 = HP.spec(pk2)
    for rel, b in pf2.items():
        if rel.startswith("roms/"): open(os.path.join(roms, rel[5:]), "wb").write(b)
    put_dir(roms, f"@R@/newroot/var/lib/ccs/pkg/{PKGV}/roms")
    lay2 = HP.layout(sp2, "storage", PKGV); sp2s, d2 = stage(lay2, "1a1b1c1d"); ck2, doc2, ib2 = finish(sp2, lay2, sp2s, d2, sha(ib), 2)
    owners = others(); jb = jpaths(); r2 = HP.parse_output(coll(f"apply {sp2s}"))
    by2 = {g["ccs_id"]: g for g in sp2["games"]}
    check(r2["ok"] and "removed=" in r2["result"] and not exists(f"{gs}/{by['b']['menu']}/{by['b']['code']}") and exists(f"{gs}/{by2['g']['menu']}/{by2['g']['code']}/{by2['g']['code']}.desktop")
          and kept(owners, jb)[0] and "ours" in ck2["entries"].values(),
          f"collections: an update changes only this program's entries (one removed, one added; the entries still there are its own) ({r2['result']})")
    old = HP.parse_output(coll(f"apply {sp1}"))
    check(not old["ok"] and "index changed since it was read" in old["result"] and fsha("@R@/newroot/var/lib/ccs/integration/v1/index.json") == sha(ib2),
          f"collections: an older publication is refused: its index is not the one in place (nothing changed) ({old['result']})")
    # removal: a changed entry of this program's is kept and reported; everything else it wrote goes; Hakchi's own entries stay
    tgt = f"{gs}/{by2['a']['menu']}/{by2['a']['code']}/{by2['a']['code']}.desktop"; sh(f"echo '# changed by someone' >> '{tgt}'")
    owners = others(); jb = jpaths(); rm = HP.parse_output(coll("remove"))
    left = sh(f"find '{gs}' -path '*ccs-*' -type f").split()
    check(rm["ok"] and any(l.startswith("kept-changed") and by2["a"]["code"] in l for l in rm["lines"]) and [x.rsplit("/", 1)[1] for x in left] == [by2["a"]["code"] + ".desktop"]
          and not exists("@R@/newroot/var/lib/ccs/integration/v1/index.json") and exists(f"{gs}/000/CLV-H-AAAAA/CLV-H-AAAAA.desktop") and kept(owners, jb)[0],
          f"collections: remove takes every unchanged file and folder this program wrote and the index, keeps the changed entry and reports it ({[l for l in rm['lines'] if l.startswith('kept')]}; left {left}; {rm['result']})")
    st = HP.parse_output(coll("status"))
    check(st["ok"] and "files=0 changed=1 missing=0 index=none" in st["result"], f"collections: status names what is left: the changed entry ({st['result']})")
    # a console without hakchi game storage: the entries go directly into the console menu's own game folder, never over its own games;
    # hakchi's next start keeps them
    fresh(foreign=False); sh("rm -rf '@R@/newroot/var/lib/hakchi/games' && mkdir -p '@R@/newroot/var/squashfs/usr/share/games/nes/kachikachi/CLV-P-NAAAE' && echo DONE")
    put_dir(pd, P); put_dir(roms, f"@R@/newroot/var/lib/ccs/pkg/{PKGV}/roms")
    w = HP.parse_output(coll("where"))["where"]; where = {"mode": w.get("mode"), "store": None, "menu": w.get("menu"), "system": w.get("system")}
    check(w.get("mode") == "overlay" and w.get("menu") == "/var/lib/hakchi/rootfs/usr/share/games/nes/kachikachi", f"collections: no game storage: the console menu's own game folder in hakchi's overlay ({w})")
    lay3 = HP.layout(sp, "overlay", PKGV); sp3, d3 = stage(lay3, "2a2b2c2d"); ck3, doc3, ib3 = finish(sp, lay3, sp3, d3, None, 1)
    owners = others(); jb = jpaths(); r3 = HP.parse_output(coll(f"apply {sp3}"))
    ov = f"{HKR}/usr/share/games/nes/kachikachi"
    check(r3["ok"] and exists(f"{ov}/{by['a']['code']}/{by['a']['code']}.desktop") and not exists(f"{ov}/CLV-P-NAAAE") and not exists("@R@/newroot/var/lib/hakchi/games") and kept(owners, jb)[0],
          f"collections: the games directly in the console menu's own game folder, no folder, no built-in game covered, no game storage made ({r3['result']})")
    boot()
    check(exists(f"{ov}/{by['a']['code']}/{by['a']['code']}.desktop") and HP.parse_output(coll("status"))["result"].find("changed=0 missing=0") > 0 and not trips(),
          "collections: hakchi's next start (preinit, script/removed, rcS) keeps the entries unchanged; no tripwire")

def main() -> int:
    global QEMU, WORK, CHOOSER_BIN, FONTS_DIR
    ap = argparse.ArgumentParser(); ap.add_argument("--hakchi-release", required=True); ap.add_argument("--keep", action="store_true")
    ap.add_argument("--chooser", help="the console build of the native HOME program (ccs-home-armv7: scripts/wsl_build.sh arm-musl-device) for the chooser part")
    ap.add_argument("--fonts", help="the player's menu fonts (silk17, sans600_22, sans400_17 .ccsf) for the chooser part (optional)")
    ap.add_argument("--chain-data", help="for --only chain: the data storage tar test_recovery_images.py --home-archive --export-data wrote")
    ap.add_argument("--only", help="comma-separated: syntax,insertion,mod,third-party,nucleus,transitions,transfer,chooser,collections,chain (default: all but chain)"); a = ap.parse_args()
    CHOOSER_BIN = os.path.abspath(a.chooser) if a.chooser else None; FONTS_DIR = os.path.abspath(a.fonts) if a.fonts else None
    CHAIN[0] = os.path.abspath(a.chain_data) if a.chain_data else None
    WORK = tempfile.mkdtemp(prefix="ccs-nu-")
    inp = B.read_inputs(a.hakchi_release); ents = rd_entries(inp)
    BP.CHOOSER_INPUTS = chooser_standin_inputs("__R__")
    pk1, pk2 = os.path.join(WORK, "pk1"), os.path.join(WORK, "pk2"); BP.build(pk1); BP.build(pk2)
    print("== 1. on the PC")
    pc_checks(pk1, pk2, inp, ents)
    q = sh("echo $HOME/.cache/ccs-sim/toolchain/deb/usr/bin/qemu-arm").strip()
    if not q or sh(f"test -x '{q}' && echo yes").strip() != "yes": raise SystemExit("qemu-arm (the project's WSL toolchain) is not available")
    QEMU = q
    if sh("id -u").strip() == "0": raise SystemExit("the qemu runs must not run as root (absolute paths outside the test folder must fail)")
    print("== 2. on the pinned BusyBox (qemu-arm)")
    bad = setup(inp, ents, pk1)
    check(not bad, f"stand-in: the test copies of hakchi's preinit, b0000_defines, b0010_functions, b0030_package, rcS and hakchi command differ from the pinned files only in RAM-root paths ({bad or 'mapped back exactly'})")
    try:
        parts = {"syntax": syntax, "insertion": insertion_point, "mod": mod_tests, "third-party": foreign_semantics, "nucleus": nucleus_tests, "transitions": transitions, "transfer": transfer_route,
                 "chooser": chooser_real, "collections": collections_tests, "chain": chain}
        for k in (a.only.split(",") if a.only else [x for x in parts if x != "chain"]): parts[k]()
    finally:
        if not a.keep:
            if R: sh("rm -rf '@R@'")
            import shutil; shutil.rmtree(WORK, ignore_errors=True)
        else: print("kept:", R, WORK)
    for f in failures: print("FAILED:", f)
    print(f"RESULT {passed}/{passed + failed}")
    return 0 if failed == 0 else 1

if __name__ == "__main__": sys.exit(main())
