"""ccsboot tests: the test build (file-backed device) under qemu-arm (in WSL on Windows, directly on Linux), and the console build's refusal without the driver.
Usage: python test_ccsboot.py BUILD_DIR [PINNED_UBOOT_BIN]. BUILD_DIR holds ccsboot and ccsboot-test (build.sh). Nothing here opens a
device: the console build is only started where /dev/nanda does not exist (qemu-arm user mode in WSL) and must refuse."""
import hashlib, os, struct, subprocess, sys, tempfile

BUILD = os.path.abspath(sys.argv[1]); PINNED = sys.argv[2] if len(sys.argv) > 2 else None
SECTOR, UBOOT_BASE, BOOT_AREA = 0x20000, 0x100000, 0x20000 * 0x80
results = []

def check(ok, name):
    results.append((bool(ok), name)); print(("ok   " if ok else "FAIL ") + name, flush=True)

def wsl_path(p): return p if os.name != "nt" else "/mnt/" + p[0].lower() + p[2:].replace("\\", "/")   # Linux (the release workflow): paths as they are

def run(args, nand=None, bad=None, test=True, stdin_file=None, faults=None):
    exe = wsl_path(os.path.join(BUILD, "ccsboot-test" if test else "ccsboot"))
    env = ("CCSBOOT_TEST_NAND=" + wsl_path(nand) + " " if nand else "") + ("CCSBOOT_TEST_BAD=" + bad + " " if bad else "")
    env += "".join("CCSBOOT_TEST_" + k + "=" + v + " " for k, v in (faults or {}).items())
    out = os.path.join(TMP, "out.bin"); err = os.path.join(TMP, "err.txt")
    cmd = "q=$(ls $HOME/.cache/ccs-sim/toolchain/deb/usr/bin/qemu-arm 2>/dev/null || ls $HOME/.cache/ccs-sim/toolchain/deb/usr/bin/qemu-armhf); " \
          "env " + env + "$q " + exe + " " + " ".join(wsl_path(a) if os.path.isabs(a) else a for a in args) + " > " + wsl_path(out) + " 2> " + wsl_path(err) + "; echo rc=$?"
    script = os.path.join(TMP, "run.sh"); open(script, "w", newline="\n").write(cmd + "\n")
    r = subprocess.run((["wsl.exe", "-d", "Ubuntu", "--"] if os.name == "nt" else []) + ["sh", wsl_path(script)], stdin=subprocess.DEVNULL, capture_output=True)
    text = r.stdout.decode("utf-8", "replace").replace("\0", "")
    rc = int(text.strip().split("rc=")[-1]) if "rc=" in text else -1
    return rc, open(out, "rb").read(), open(err, encoding="utf-8", errors="replace").read()

def words_sum(b): return sum(struct.unpack("<%dI" % (len(b) // 4), b)) & 0xffffffff

def image(kind, size, seed=1):
    """a valid synthetic boot image: kind boot0 (eGON.BT0) or boot1 (uboot); size a multiple of 4"""
    body = bytearray(hashlib.sha256(bytes([seed])).digest() * (size // 32 + 1))[:size]
    body[0:4] = b"\xea\x00\x00\x0e"
    if kind == "boot0": body[4:12] = b"eGON.BT0"; struct.pack_into("<I", body, 16, size)
    else: body[4:10] = b"uboot\0"; struct.pack_into("<I", body, 20, size)
    struct.pack_into("<I", body, 12, 0)
    struct.pack_into("<I", body, 12, (words_sum(body) + 0x5f0a6c39) & 0xffffffff)
    return bytes(body)

def nandinfo(size, seed=7):
    b = bytearray(hashlib.sha256(bytes([seed])).digest() * (size // 32 + 1))[:size]
    struct.pack_into("<III", b, 0, 0xaa55a5a5, size, 0)
    struct.pack_into("<I", b, 8, (words_sum(b) + 0xae15bc34) & 0xffffffff)
    return bytes(b)

def blank_nand(path):
    with open(path, "wb") as f: f.write(b"\xff" * BOOT_AREA)

TMP = tempfile.mkdtemp(prefix="ccsboot-", dir=os.environ.get("CCS_TEST_TMP"))
b1 = image("boot1", 300000) + nandinfo(4096)            # u-boot spanning three blocks, a nandinfo block after it
b0 = image("boot0", 24576)
p1 = os.path.join(TMP, "boot1.bin"); open(p1, "wb").write(b1)
p0 = os.path.join(TMP, "boot0.bin"); open(p0, "wb").write(b0)

# check: complete valid images only, never fixed
rc, out, err = run(["check", p1]); check(rc == 0 and out.decode().strip() == "ok %d boot1" % len(b1), "check: a valid u-boot with its nandinfo block")
rc, out, _ = run(["check", p0]); check(rc == 0 and out.decode().strip() == "ok %d boot0" % len(b0), "check: a valid boot0")
bad = bytearray(b1); bad[1000] ^= 1; pb = os.path.join(TMP, "bad.bin"); open(pb, "wb").write(bad)
rc, out, err = run(["check", pb]); check(rc == 7 and "checksum" in err, "check: one changed byte is refused (checksum), never fixed")
open(pb, "wb").write(b1 + b"\0" * 8); rc, _, err = run(["check", pb]); check(rc == 7, "check: bytes after the image are refused (" + err.strip() + ")")
open(pb, "wb").write(b1[:-4]); rc, _, err = run(["check", pb]); check(rc == 7, "check: a shortened image is refused (" + err.strip() + ")")
open(pb, "wb").write(b"default\n"); rc, _, err = run(["check", pb]); check(rc == 7, "check: the text \"default\" is not an image")

# burn1 and read1 round trip on a blank boot area
nand = os.path.join(TMP, "nand.img"); blank_nand(nand)
rc, out, err = run(["burn1", p1], nand=nand); check(rc == 0 and out.decode().strip() == "written 3", "burn1: written once, three blocks (" + (out.decode().strip() or err.strip()) + ")")
area = open(nand, "rb").read()
check(area[UBOOT_BASE:UBOOT_BASE + len(b1)] == b1 and area[:UBOOT_BASE] == b"\xff" * UBOOT_BASE and set(area[UBOOT_BASE + 3 * SECTOR:]) == {0xff}, "burn1: exactly the image at u-boot's place, nothing else changed")
rc, out, err = run(["read1"], nand=nand); check(rc == 0 and out == b1, "read1: the same bytes back, nandinfo block included (" + err.strip() + ")")
rc, out, _ = run(["burn1", p1], nand=nand); check(rc == 0 and out.decode().strip() == "written 0", "burn1 again: equal blocks are not written again")

# a bad block: the image moves to the next block, the reader follows it
nand2 = os.path.join(TMP, "nand2.img"); blank_nand(nand2)
rc, out, err = run(["burn1", p1], nand=nand2, bad="9"); a2 = open(nand2, "rb").read()
check(rc == 0 and a2[UBOOT_BASE:UBOOT_BASE + SECTOR] == b1[:SECTOR] and a2[UBOOT_BASE + 2 * SECTOR:UBOOT_BASE + 3 * SECTOR] == b1[SECTOR:2 * SECTOR] and set(a2[UBOOT_BASE + SECTOR:UBOOT_BASE + 2 * SECTOR]) == {0xff},
      "burn1 with block 9 bad: that block skipped, the rest one block later (" + err.strip().replace("\n", "; ") + ")")
rc, out, _ = run(["read1"], nand=nand2, bad="9"); check(rc == 0 and out == b1, "read1 with block 9 bad: the same image")

# refusals before any write
for name, data in (("damaged", bytes(bad)), ("default-text", b"default\n"), ("boot0-as-boot1", b0)):
    nd = os.path.join(TMP, "nand-" + name + ".img"); blank_nand(nd); pf = os.path.join(TMP, name + ".bin"); open(pf, "wb").write(data)
    rc, out, err = run(["burn1", pf], nand=nd)
    check(rc == 7 and open(nd, "rb").read() == b"\xff" * BOOT_AREA, "burn1 refuses " + name + " before writing anything (" + err.strip() + ")")
big = image("boot1", BOOT_AREA - UBOOT_BASE + 4096); pbig = os.path.join(TMP, "big.bin"); open(pbig, "wb").write(big)
nd = os.path.join(TMP, "nand-big.img"); blank_nand(nd); rc, _, err = run(["burn1", pbig], nand=nd)
check(rc != 0 and open(nd, "rb").read() == b"\xff" * BOOT_AREA, "burn1 refuses an image larger than the boot area (" + err.strip() + ")")
# read1 of a damaged stored image: reported, non-zero
nd = os.path.join(TMP, "nand-dmg.img"); blank_nand(nd)
with open(nd, "r+b") as f: f.seek(UBOOT_BASE); f.write(bytes(bad))
rc, out, err = run(["read1"], nand=nd); check(rc == 5 and "not valid" in err, "read1 of a damaged stored u-boot: reported as not valid, non-zero")

# boot0 through the driver's boot0 writer
nd = os.path.join(TMP, "nand-b0.img"); blank_nand(nd)
rc, out, err = run(["burn0", p0], nand=nd); check(rc == 0 and out.decode().strip() == "written 1", "burn0: one write through the boot0 writer")
rc, out, _ = run(["read0"], nand=nd); check(rc == 0 and out == b0, "read0: the same boot0 back")
rc, _, err = run(["burn0", p1], nand=nd); check(rc == 7, "burn0 refuses a u-boot image (" + err.strip() + ")")

# the console build: without the driver it refuses, writes nothing
rc, out, err = run(["read1"], test=False); check(rc == 2 and out == b"" and "could not be opened" in err, "console build without /dev/nanda: refuses, prints nothing")
rc, out, err = run(["burn1", p1], test=False); check(rc == 2 and "could not be opened" in err, "console build burn1 without the driver: refuses")

if PINNED and os.path.exists(PINNED):
    rc, out, err = run(["check", os.path.abspath(PINNED)])
    check(rc == 0, "the pinned hakchi u-boot (boot/uboot.bin) passes ccsboot check as a complete image (" + (out.decode().strip() or err.strip()) + ")")

# ---- a block that cannot be read before its write (Codex review F1, 2026-11-15): never written, the stop reported with the blocks before it
def area_of(path): return open(path, "rb").read()
def boot(path): return area_of(path)[UBOOT_BASE:UBOOT_BASE + 3 * SECTOR]
old = image("boot1", 300000, seed=3) + nandinfo(4096, seed=9)          # another valid u-boot already on the console
def with_old(name):
    nd = os.path.join(TMP, name + ".img"); blank_nand(nd)
    with open(nd, "r+b") as f: f.seek(UBOOT_BASE); f.write(old)
    return nd
nd = with_old("nand-eio-all"); before = area_of(nd)
rc, out, err = run(["burn1", p1], nand=nd, faults={"EIO": "8,9,a"})
check(rc == 10 and out.decode().strip() == "stopped 0" and area_of(nd) == before, "burn1: every read failing before its write (the review's case): stopped at the first block, nothing written (rc %d, %s)" % (rc, (out.decode().strip() or err.strip())))
nd = with_old("nand-eio-first"); before = area_of(nd)
rc, out, err = run(["burn1", p1], nand=nd, faults={"EIO": "8"})
check(rc == 10 and out.decode().strip() == "stopped 0" and area_of(nd) == before and "before writing it" in err, "burn1: a read error at the first block: nothing written (%s)" % err.strip())
nd = with_old("nand-eio-second"); before = area_of(nd)
rc, out, err = run(["burn1", p1], nand=nd, faults={"EIO": "9"}); a = area_of(nd)
check(rc == 10 and out.decode().strip() == "stopped 1" and a[UBOOT_BASE:UBOOT_BASE + SECTOR] == b1[:SECTOR] and a[UBOOT_BASE + SECTOR:] == before[UBOOT_BASE + SECTOR:] and a[:UBOOT_BASE] == before[:UBOOT_BASE],
      "burn1: a read error after an earlier block: that block and every later one untouched, the stop says one block was written (%s)" % out.decode().strip())
rc, _, _ = run(["read1"], nand=nd); check(rc != 0, "read1 after that partial write: the stage is reported as not valid (its state must be read again, not assumed)")
nd = os.path.join(TMP, "nand-wfail.img"); blank_nand(nd)
rc, out, err = run(["burn1", p1], nand=nd, faults={"WFAIL": "9"}); a = area_of(nd)
check(rc == 8 and out.decode().strip() == "stopped 1" and a[UBOOT_BASE:UBOOT_BASE + SECTOR] == b1[:SECTOR] and set(a[UBOOT_BASE + SECTOR:]) == {0xff}, "burn1: a write failure stops there, nothing retried (%s)" % out.decode().strip())
nd = os.path.join(TMP, "nand-wcorrupt.img"); blank_nand(nd)
rc, out, err = run(["burn1", p1], nand=nd, faults={"WCORRUPT": "8"}); a = area_of(nd)
check(rc == 11 and out.decode().strip() == "stopped 1" and set(a[UBOOT_BASE + SECTOR:]) == {0xff} and "read back" in err, "burn1: a block that does not read back as written stops there, before the next block (%s)" % err.strip())
nd = os.path.join(TMP, "nand-bad-read.img"); blank_nand(nd)
rc, out, err = run(["burn1", p1], nand=nd, bad="8"); a = area_of(nd)
check(rc == 0 and set(a[UBOOT_BASE:UBOOT_BASE + SECTOR]) == {0xff} and a[UBOOT_BASE + SECTOR:UBOOT_BASE + 4 * SECTOR][:len(b1)] == b1 and "not written" in err, "burn1: a block the driver reports bad is skipped unwritten, the image follows in the next (" + err.strip().replace("\n", "; ") + ")")
rc, out, _ = run(["read1"], nand=nd, bad="8"); check(rc == 0 and out == b1, "read1 with that block bad: the same image")

# ---- the typed exception: burn1-unreadable, only for a stage the diagnosis found unreadable (Console Rescue, acknowledged)
nd = with_old("nand-unread"); before = area_of(nd)
rc, out, err = run(["burn1", p1], nand=nd, faults={"EIO_UNTIL_WRITE": "8,9"})
check(rc == 10 and area_of(nd) == before, "ordinary burn1 on an unreadable stage: refused at its first block, nothing written (Return never writes past a read error)")
rc, out, err = run(["burn1-unreadable", p1], nand=nd, faults={"EIO_UNTIL_WRITE": "8,9"})
check(rc == 0 and out.decode().strip() == "written 3 unread 2", "burn1-unreadable: the two unreadable blocks written without a comparison and read back; the readable one compared (%s)" % (out.decode().strip() or err.strip()))
rc, out, _ = run(["read1"], nand=nd); check(rc == 0 and out == b1, "read1 after burn1-unreadable: exactly the wanted image")
nd = with_old("nand-unread-perm")
rc, out, err = run(["burn1-unreadable", p1], nand=nd, faults={"EIO": "8"})
check(rc == 11 and out.decode().strip() == "stopped 1", "burn1-unreadable: a block that still cannot be read after its write stops there (no readback, no success) (%s)" % out.decode().strip())
nd = os.path.join(TMP, "nand-unread-blank.img"); blank_nand(nd)
rc, out, _ = run(["burn1-unreadable", p1], nand=nd); check(rc == 0 and out.decode().strip() == "written 3 unread 0", "burn1-unreadable on a readable stage: every block compared first, as burn1")
for name, data in (("damaged", bytes(bad)), ("default-text", b"default\n")):
    nd = os.path.join(TMP, "nand-u-" + name + ".img"); blank_nand(nd); pf = os.path.join(TMP, "u-" + name + ".bin"); open(pf, "wb").write(data)
    rc, out, err = run(["burn1-unreadable", pf], nand=nd)
    check(rc == 7 and open(nd, "rb").read() == b"\xff" * BOOT_AREA, "burn1-unreadable refuses " + name + " before writing anything")

# ---- typed read results (the diagnosis maps them; never one word for all)
nd = os.path.join(TMP, "nand-read-eio.img"); blank_nand(nd)
with open(nd, "r+b") as f: f.seek(UBOOT_BASE); f.write(b1)
rc, _, _ = run(["read1"], nand=nd, faults={"EIO": "9"}); check(rc == 3, "read1 with a block that cannot be read: 3 (unreadable), not 5 (invalid)")
nb = os.path.join(TMP, "nand-blank-read.img"); blank_nand(nb)
rc, _, _ = run(["read1"], nand=nb); check(rc == 4, "read1 of an erased boot area: 4 (no recognizable image)")
rc, _, _ = run(["read1"], nand=os.path.join(TMP, "no-such-nand.img")); check(rc == 2, "read1 when the driver cannot be opened: 2 (driver), never a stage diagnosis")

fails = [n for ok, n in results if not ok]
print("RESULT %d/%d" % (len(results) - len(fails), len(results)))
sys.exit(1 if fails else 0)
