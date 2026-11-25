"""Classic Cart Studio recovery images: Hakchi2 CE 3.9.3's recovery boot.img (its kernel and command line byte for byte) with a ramdisk
changed so that it can only do what Console Backup & Restore needs. Copyright (C) 2026 Rance9, GPL-3.0-or-later (hakchi parts GPL-3.0,
kernel GPL-2.0).

  python build_images.py --hakchi-release DIR|ZIP --ccsboot FILE --chooser-program ccs-home-armv7 --out DIR [--chooser-fonts DIR] [--variants ccs-backup,ccs-restore,ccs-prepare]

DIR (--hakchi-release): the pinned Hakchi2 CE 3.9.3 release files basehmods.tar and hakchi.exe, or ZIP: the official portable package they
come in (hakchi2-ce-3.9.3-portable.zip, which tools/publish/fetch_hakchi_package.py downloads and verifies); every input is checked by
SHA-256 and never changed. The preparation image (ccs-prepare) also carries the pinned hakchi.hmod's files, ccsprep, ccscomm (Community
Mods' steps: a listed author's package placed and removed through hakchi's transfer folder; it never carries such a package), and the three
add-on packages (CCS Mod, CCS Nucleus and CCS Nucleus's chooser: tools/console-nucleus/build_packages.py, built here from their sources
and, for the chooser, the console build of the native HOME program and the player's menu fonts given with --chooser-program and
--chooser-fonts; pinned by METHOD_SHAS) at ccs/prep/methods/<name>.hmod, which ccsprep's "method" step places in hakchi's transfer folder.
For each variant a NEW folder <out>/<variant>/ with the image (ccs-<variant>-boot.img), fes1.bin and uboot-sd.bin (hakchi 3.9.3's,
pinned), MANIFEST.json (schema ccs-h1-ram-image/2, which the first-party FEL loader CcsConsole.RamBoot accepts, plus image_id and the
audit of every ramdisk change) and SHA256SUMS. Building authorizes nothing: loading an image on a console is a separately approved session.

Ramdisk changes (everything else byte-identical, in order):
  /init                       hakchi's, changed in two places: the "SEGA" fallback that mounts /dev/nandd is removed (on an NES Classic
                              it is the private storage; a mount could replay a journal), and the hand-over to the console's normal
                              system (mount --move ... switch_root) is replaced by ccs-idle: the console never starts its normal system
                              from this image, whatever happens before
  /hakchi/init                replaced by ccs-init (tools/console-backup/ram/ccs-init): hakchi's own init sources script/config,
                              script/base and script/shell, whose default path mounts the data storage read-write (mount_base)
  /hakchi/rootfs/etc/init.d/S92rndis   removed (no USB network)
  SESSION_UNUSED              removed from the backup, restore, rescue and files images only (programs none of their steps runs and the
                              libraries only those need); the preparation image keeps them: hakchi's installation copies its /bin
  /ccs/...                    added: bin/ccsbr (the backup variant WITHOUT its write section), bin/ccs-idle, env, profile, image-id,
                              cards/*.png (the TV status pictures)
image_id = "<variant>/<version>/<first 16 hex of the SHA-256 of every /ccs file and the two replaced scripts>": the console reports it
back (ccsbr identify) and the PC refuses any other program."""
from __future__ import annotations
import argparse, hashlib, io, json, lzma, os, re, struct, sys, tarfile

HERE = os.path.dirname(os.path.abspath(__file__)); TOOLS = os.path.normpath(os.path.join(HERE, "..", ".."))
VERSION = "1"
BASEHMODS_SHA = "7f7a6b8ef26783753b63f60bdf40ba8bd9278ae7ca9d89715b9103aa0b7dc77c"
HMOD_SHA = "003994b46504959895f40b0b9e91b16951ef4a638feb6312314c8fc4fdbff592"
BOOTIMG_SHA = "9e921c326be65aa4a3100270e86fdb15fe4b8563d586941b9f6c640eef064e4c"
HAKCHI_EXE_SHA = "8e1fe79585ae2aceacab21157d10a74e0b3e64456e1efdd750c1eb61e3391a2b"
FES1_SHA = "d78688b70e022c53e6a49a18994f59946fda7791d22bc0e6409ff630553b491c"; FES1_LEN = 12656
UBOOT_SD_SHA = "3bfc4a56417aade7d3fa3afcf3d2344b07ea38d9f42de504eda3014844f86b5b"
# ccsboot (ram/ccsboot): Classic Cart Studio's boot-stage reader/writer, built from ccsboot.c by ccsboot/build.sh (zig 0.17.0, static
# armv7). Both hashes are pinned: a changed source or a different build fails here until the pins are deliberately updated.
CCSBOOT_SRC_SHA = "6b18c2ea41ab576b05fbd3df90dd388abced95f3b22bf60b230262bc7a60df9c"
CCSBOOT_SHA = "6c67045275ba7c19857540149b23f70419f23109825c032db88955a7f62ab5b4"
# CCS Mod, CCS Nucleus and its chooser (tools/console-nucleus): built here (reproducible), pinned like ccsboot: a changed package
# fails here until the pins are deliberately updated (and the C# engine reads them back from this image's MANIFEST.json "methods")
METHOD_SHAS = {"ccs-mod": "5c5318b62efc712a4fb5b36f123ecd8b17953d770233a2674dbe6cb41668530c",
               "ccs-nucleus": "0074f2ec6bba1f7f329a3eafdfac4beaef3681e87802680e38aa435043eef2b7",
               "ccs-nucleus-chooser": "1c6abd0f646332ccd59300d85cb427333aa39e72792d68cf895c3756ed6d3a27"}
FEL_MAX = 0x2000000; FEL_SECTOR = 0x20000
MTIME = 1763078400                       # 2025-11-14 00:00 UTC: fixed, so the same sources give the same image
VARIANTS = ("ccs-backup", "ccs-restore", "ccs-prepare", "ccs-rescue", "ccs-files")
# Hakchi's ramdisk programs that no step of the backup, restore, rescue or files image runs (none of /init, ccs-init, ccsbr, ccsrescue,
# ccsfiles, ccsdev or ccs-idle names them; the PC runs only those scripts; no network, no file-system tool, no restart through kexec), and
# the libraries only they load (ELF NEEDED; cryptsetup keeps libuuid, every glibc library stays): left out of those four images
# (SOURCE-AUDIT.md section 7; /init loads only nand.ko). The preparation image keeps every file: hakchi's mod_repair copies its /bin to
# the console and its copy step runs rsync. A script of a trimmed image that names one of these programs stops the build.
SESSION_UNUSED = ("bin/dropbear", "bin/rsync", "bin/llmnrd", "bin/tinysvcmdns", "bin/kexec", "bin/e2fsck", "bin/fsck.ext2", "bin/fsck.ext3",
                  "bin/fsck.ext4", "bin/mke2fs", "bin/mkfs.ext2", "bin/mkfs.ext3", "bin/mkfs.ext4", "bin/resize2fs", "bin/tune2fs", "bin/sunxi-fexc",
                  "bin/fex2bin", "bin/bin2fex", "bin/sunxi-nand-part", "lib32/e2initrd_helper", "lib32/libext2fs.so", "lib32/libext2fs.so.2",
                  "lib32/libext2fs.so.2.4", "lib32/libe2p.so", "lib32/libe2p.so.2", "lib32/libe2p.so.2.3", "lib32/libcom_err.so", "lib32/libcom_err.so.2",
                  "lib32/libcom_err.so.2.1", "lib32/libss.so", "lib32/libss.so.2", "lib32/libss.so.2.0", "lib32/libblkid.so.1", "lib32/libblkid.so.1.1.0",
                  "lib32/modules-ramfs/kernel/drivers/scsi/sd_mod.ko")
FONTS = os.path.join(TOOLS, "companion", "native_home", "fonts"); ICON = os.path.join(TOOLS, "publish", "assets", "app-256.png")
ENV = b"DEV=/dev\nDEVKIND=block\nNEWROOT=/newroot\nSYSFS=/sys\nPROCSYS=/proc/sys\nMOUNTS=/proc/mounts\nPARTS=/proc/partitions\nST=/tmp/ccsbr\nFB=/dev/fb0\nHK=/hakchi\n"

SEGA_BLOCK = ('# SEGA\nif ! mountpoint -q "$mountpoint"; then\n  mount -o ro,noatime "/dev/nandd" "$mountpoint"\n'
              '  [ -f "$mountpoint/etc/init.d/moon-game" ] || umount "$mountpoint"\nfi\n')
TAIL = ('exec 0</dev/console\nexec 1>/dev/console\nexec 2>/dev/console\nmount --move /dev "$mountpoint/dev"\n'
        'mount --move /proc "$mountpoint/proc"\nmount --move /sys "$mountpoint/sys"\nexec switch_root "$mountpoint" /sbin/init\n')
NEW_TAIL = ('# Classic Cart Studio recovery image: never the console\'s normal system from here (ccs-idle waits in RAM until the USB cable is\n'
            '# unplugged or the PC asks ccsbr to restart the console)\nexec /ccs/bin/ccs-idle\n')

CARDS = {   # name: (title, line, sub, kind)
    "starting":   ("Classic Cart Studio", "Starting the console program...", "Keep the USB cable connected.", "info"),
    "connected":  ("Classic Cart Studio", "Connected to your PC", "Follow the steps on the computer.", "info"),
    "reading":    ("Console Backup", "Backing up...", "Keep the USB cable connected until the PC says it is done.", "info"),
    "writing":    ("Console Restore", "Restoring... do not unplug", "The PC shows the progress. Keep the USB cable connected.", "warn"),
    "done":       ("Classic Cart Studio", "Finished", "Follow the PC's last step.", "ok"),
    "stopped":    ("Classic Cart Studio", "Stopped", "Nothing more will be written. Follow the PC's instructions.", "bad"),
    "restarting": ("Classic Cart Studio", "Restarting...", "The console starts normally in a moment.", "ok"),
}
FOOT = {"ccs-backup": "Backup mode: runs from RAM and only reads the console's storage.",
        "ccs-restore": "Restore mode: the storage is written only as confirmed on the PC.",
        "ccs-prepare": "Preparation mode: the console is changed only as confirmed on the PC.",
        "ccs-rescue": "Rescue mode: a startup stage is written only as confirmed on the PC.",
        "ccs-files": "File mode: files and system parts are changed only as confirmed on the PC."}

def sha(b: bytes) -> str: return hashlib.sha256(b).hexdigest()
def lf(p: str) -> bytes: return open(p, "rb").read().replace(b"\r\n", b"\n")

# ---------------------------------------------------------------- Android boot image v0, cpio newc, xz (as the H1/K2 images: they ran on the console)
def bootimg_parse(b: bytes) -> dict:
    if b[:8] != b"ANDROID!": raise ValueError("not an Android boot image")
    ks, ka, rs, ra, ss, sa, ta, ps = struct.unpack_from("<8I", b, 8); al = lambda x: (x + ps - 1) // ps * ps
    k0 = ps; r0 = k0 + al(ks); s0 = r0 + al(rs)
    return {"kernel": b[k0:k0 + ks], "ramdisk": b[r0:r0 + rs], "second": b[s0:s0 + ss], "page": ps, "header": b[:ps], "cmdline": b[64:576].rstrip(b"\0").decode()}

def bootimg_build(orig: dict, ramdisk: bytes) -> bytes:
    ps = orig["page"]; hdr = bytearray(orig["header"]); struct.pack_into("<I", hdr, 16, len(ramdisk))
    h = hashlib.sha1()
    for part in (orig["kernel"], ramdisk, orig["second"]): h.update(part); h.update(struct.pack("<I", len(part)))
    hdr[576:608] = h.digest().ljust(32, b"\0")
    pad = lambda x: x + b"\0" * ((-len(x)) % ps)
    return bytes(hdr) + pad(orig["kernel"]) + pad(ramdisk) + (pad(orig["second"]) if orig["second"] else b"")

FIELDS = ("ino", "mode", "uid", "gid", "nlink", "mtime", "filesize", "devmajor", "devminor", "rdevmajor", "rdevminor", "namesize", "check")
def cpio_parse(b: bytes) -> list[dict]:
    out, p = [], 0
    while True:
        if b[p:p + 6] != b"070701": raise ValueError(f"bad cpio magic at {p}")
        f = {k: int(b[p + 6 + 8 * i:p + 14 + 8 * i], 16) for i, k in enumerate(FIELDS)}; p += 110
        name = b[p:p + f["namesize"] - 1].decode(); p = (p + f["namesize"] + 3) & ~3
        data = b[p:p + f["filesize"]]; p = (p + f["filesize"] + 3) & ~3
        out.append({"name": name, **f, "data": data})
        if name == "TRAILER!!!": return out

def cpio_build(entries: list[dict]) -> bytes:
    o = bytearray()
    for e in entries:
        nm = e["name"].encode() + b"\0"; e = {**e, "namesize": len(nm), "filesize": len(e["data"])}
        o += b"070701" + b"".join(b"%08X" % e[k] for k in FIELDS) + nm; o += b"\0" * ((-len(o)) % 4)
        o += e["data"]; o += b"\0" * ((-len(o)) % 4)
    o += b"\0" * ((-len(o)) % 512)
    return bytes(o)

def xz(data: bytes) -> bytes:   # the original ramdisk's parameters: LZMA2 only, 32 MiB dictionary, CRC32 (the kernel's decoder)
    return lzma.compress(data, format=lzma.FORMAT_XZ, check=lzma.CHECK_CRC32, filters=[{"id": lzma.FILTER_LZMA2, "preset": 9, "dict_size": 32 << 20}])

def entry(name: str, mode: int, data: bytes, ino: int) -> dict:
    return {"name": name, "ino": ino, "mode": mode, "uid": 0, "gid": 0, "nlink": 2 if mode & 0o040000 else 1, "mtime": MTIME, "filesize": len(data),
            "devmajor": 0, "devminor": 0, "rdevmajor": 0, "rdevminor": 0, "namesize": 0, "check": 0, "data": data}

# ---------------------------------------------------------------- inputs (pinned)
def read_inputs(rel: str) -> dict:
    """rel: the pinned release's folder, or its official portable package (hakchi2-ce-3.9.3-portable.zip, as fetch_hakchi_package.py verifies it)"""
    def member(name: str) -> bytes:
        if not rel.lower().endswith(".zip"): return open(os.path.join(rel, name), "rb").read()
        import zipfile
        with zipfile.ZipFile(rel) as z: return z.read(name)
    tb = member("basehmods.tar")
    if sha(tb) != BASEHMODS_SHA: raise SystemExit("basehmods.tar is not the pinned Hakchi2 CE 3.9.3 file")
    with tarfile.open(fileobj=io.BytesIO(tb)) as t: hm = t.extractfile("./hakchi.hmod").read()
    if sha(hm) != HMOD_SHA: raise SystemExit("hakchi.hmod differs from the pinned file")
    with tarfile.open(fileobj=io.BytesIO(hm)) as h: bi = h.extractfile("boot/boot.img").read(); ub = h.extractfile("boot/uboot.bin").read()
    if sha(bi) != BOOTIMG_SHA: raise SystemExit("boot.img differs from the pinned file")
    exe = member("hakchi.exe")
    if sha(exe) != HAKCHI_EXE_SHA: raise SystemExit("hakchi.exe is not the pinned Hakchi2 CE 3.9.3 file")
    key = b"\x20" + struct.pack("<i", FES1_LEN); hits = []; i = exe.find(key)   # .NET resource record: type 0x20 (byte[]), int32 length
    while i >= 0:
        if sha(exe[i + 5:i + 5 + FES1_LEN]) == FES1_SHA: hits.append(i + 5)
        i = exe.find(key, i + 1)
    if len(hits) != 1: raise SystemExit("fes1 was not found exactly once in hakchi.exe")
    ub_sd = ub[:-8] + ub[-4:] + ub[-8:-4]          # Hmod.Uboot(UbootType.SD): the last two 32-bit words swapped (as hakchi's memboot)
    if sha(ub_sd) != UBOOT_SD_SHA: raise SystemExit("u-boot (SD variant) differs from the pinned file")
    return {"basehmods": tb, "hmod": hm, "bootimg": bi, "fes1": exe[hits[0]:hits[0] + FES1_LEN], "uboot_sd": ub_sd}

# ---------------------------------------------------------------- TV status pictures (1280x720 palette PNG, like hakchi.png)
def cards(variant: str) -> dict:
    from PIL import Image, ImageDraw, ImageFont
    bold = os.path.join(FONTS, "Barlow-Bold.ttf"); reg = os.path.join(FONTS, "Barlow-Regular.ttf")
    bg = {"info": (17, 19, 26), "ok": (16, 44, 30), "warn": (70, 44, 8), "bad": (92, 18, 18)}
    icon = Image.open(ICON).convert("RGBA").resize((96, 96), Image.Resampling.LANCZOS); out = {}
    for name, (title, line, sub, kind) in CARDS.items():
        if variant == "ccs-backup" and name == "writing": continue   # (the preparation image shows "writing" while it changes the console)
        if variant == "ccs-rescue" and name == "writing": title, line = "Console Rescue", "Repairing... do not unplug"
        if variant == "ccs-files" and name == "writing": title, line = "Classic Cart Studio", "Writing... do not unplug"
        im = Image.new("RGB", (1280, 720), bg[kind]); im.paste(icon, (592, 96), icon); d = ImageDraw.Draw(im)
        for txt, fnt in ((title, ImageFont.truetype(bold, 44)), (line, ImageFont.truetype(bold, 72)), (sub, ImageFont.truetype(reg, 40))): assert d.textlength(txt, font=fnt) <= 1200, txt
        d.text((640, 250), title, font=ImageFont.truetype(bold, 44), fill=(205, 212, 226), anchor="mm")
        d.text((640, 360), line, font=ImageFont.truetype(bold, 72), fill=(255, 255, 255), anchor="mm")
        d.text((640, 460), sub, font=ImageFont.truetype(reg, 40), fill=(228, 232, 240), anchor="mm")
        f = ImageFont.truetype(reg, 26); assert d.textlength(FOOT[variant], font=f) <= 1180
        d.text((640, 660), FOOT[variant], font=f, fill=(165, 174, 190), anchor="mm")
        buf = io.BytesIO(); im.quantize(colors=64, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).save(buf, format="PNG", optimize=True); out[name] = buf.getvalue()
    return out

def ccsbr_for(variant: str) -> bytes:
    src = lf(os.path.join(HERE, "ccsbr")).decode()
    if variant in ("ccs-backup", "ccs-prepare", "ccs-rescue", "ccs-files"):   # these images read with the backup helper; their changes are their own scripts' steps only
        a = src.index("#<write>\n"); b = src.index("#</write>\n") + len("#</write>\n")
        src = src[:a] + src[b:]
        for bad in ("burn_boot", "of=", "--setrw", "conv=fsync", "write)"): assert bad not in src, f"the backup helper still contains {bad!r}"
    return src.encode()

_methods = None
CHOOSER_INPUTS: dict | None = None   # {"program": the console build of the native HOME program, "fonts": the player's menu fonts}
def method_packages() -> dict:
    """ccs-mod.hmod, ccs-nucleus.hmod and ccs-nucleus-chooser.hmod, built (byte-identical every time) and checked against METHOD_SHAS"""
    global _methods
    if _methods is None:
        sys.path.insert(0, os.path.join(TOOLS, "console-nucleus")); import build_packages as BP
        if CHOOSER_INPUTS: BP.CHOOSER_INPUTS = CHOOSER_INPUTS
        _methods = {f"ccs-{m}": BP.hmod_bytes(BP.members(m)) for m in BP.PACKAGES}
        for n, b in _methods.items():
            if sha(b) != METHOD_SHAS.get(n): raise SystemExit(f"{n}.hmod is not the pinned package (its sources changed: update METHOD_SHAS deliberately)")
    return _methods

def patch_init(init: bytes) -> bytes:
    s = init.decode()
    if s.count(SEGA_BLOCK) != 1 or s.count(TAIL) != 1 or not s.endswith(TAIL): raise SystemExit("the ramdisk's /init is not the pinned text this builder patches")
    s = s.replace(SEGA_BLOCK, "# (Classic Cart Studio: the SEGA nandd fallback mount is removed)\n").replace(TAIL, NEW_TAIL)
    return s.encode()

def build_variant(inp: dict, variant: str, out: str) -> dict:
    orig = bootimg_parse(inp["bootimg"]); cp = lzma.decompress(orig["ramdisk"]); ents = cpio_parse(cp)
    trailer = ents.pop(); assert trailer["name"] == "TRAILER!!!"
    byname = {e["name"]: e for e in ents}
    for need in ("init", "hakchi/init", "hakchi/rootfs/etc/init.d/S92rndis", "hakchi/shell/clovershell"): assert need in byname, need
    changes = {"replaced": [], "removed": [], "added": []}
    byname["init"]["data"] = patch_init(byname["init"]["data"]); changes["replaced"].append("init (SEGA nandd mount removed; normal-system hand-over replaced by ccs-idle)")
    byname["hakchi/init"]["data"] = lf(os.path.join(HERE, "ccs-init")); byname["hakchi/init"]["mode"] = 0o100755; changes["replaced"].append("hakchi/init (ccs-init)")
    ents = [e for e in ents if e["name"] != "hakchi/rootfs/etc/init.d/S92rndis"]; changes["removed"].append("hakchi/rootfs/etc/init.d/S92rndis")
    profile = {"ccs-backup": "backup", "ccs-restore": "restore", "ccs-prepare": "prepare", "ccs-rescue": "rescue", "ccs-files": "files"}[variant]
    files = {"ccs/bin/ccsbr": (0o100755, ccsbr_for(variant)), "ccs/bin/ccs-idle": (0o100755, lf(os.path.join(HERE, "ccs-idle"))),
             "ccs/env": (0o100644, ENV), "ccs/profile": (0o100644, profile.encode() + b"\n")}
    dirs = ["ccs", "ccs/bin", "ccs/cards"]
    if variant == "ccs-prepare":
        files["ccs/bin/ccsprep"] = (0o100755, lf(os.path.join(HERE, "ccsprep")))
        files["ccs/bin/ccscomm"] = (0o100755, lf(os.path.join(HERE, "ccscomm")))   # Community Mods: a listed author's package placed and removed through hakchi's route
        files["ccs/bin/ccsboot"] = (0o100755, inp["ccsboot"])
        # the pinned hakchi.hmod's files (its install and uninstall scripts are left out: ccsprep does their work as checked steps)
        import gzip
        with tarfile.open(fileobj=io.BytesIO(gzip.decompress(inp["hmod"]) if inp["hmod"][:2] == b"\x1f\x8b" else inp["hmod"])) as t:
            for m in t.getmembers():
                n = m.name.lstrip("./")
                if not n or n in ("install", "uninstall"): continue
                if m.isdir(): dirs.append("ccs/prep/hmod/" + n)
                elif m.isfile(): files["ccs/prep/hmod/" + n] = (0o100000 | (m.mode & 0o7777), t.extractfile(m).read())
                elif m.issym(): files["ccs/prep/hmod/" + n] = (0o120777, m.linkname.encode())
        for name, b in method_packages().items(): files[f"ccs/prep/methods/{name}.hmod"] = (0o100644, b)
        for d0 in ("ccs/prep", "ccs/prep/hmod", "ccs/prep/methods"):
            if d0 not in dirs: dirs.append(d0)
        for n in list(files):   # every parent folder of an added file is in the archive
            parts = n.split("/")
            for k in range(1, len(parts)):
                d1 = "/".join(parts[:k])
                if d1 not in dirs: dirs.append(d1)
    if variant == "ccs-files":   # Expert and Developer Mode's checked file steps (ccsfiles) and Developer Mode's startup-software and system-part steps (ccsdev); nothing else writes
        files["ccs/bin/ccsfiles"] = (0o100755, lf(os.path.join(HERE, "ccsfiles")))
        files["ccs/bin/ccsdev"] = (0o100755, lf(os.path.join(HERE, "ccsdev")))
    if variant == "ccs-rescue":   # ccsrescue's steps and Classic Cart Studio's boot-stage reader/writer; no hakchi installation files
        files["ccs/bin/ccsrescue"] = (0o100755, lf(os.path.join(HERE, "ccsrescue")))
        files["ccs/bin/ccsboot"] = (0o100755, inp["ccsboot"])
    unused = [] if variant == "ccs-prepare" else [e["name"] for e in ents if e["name"] in SESSION_UNUSED]
    if variant != "ccs-prepare":
        if len(unused) != len(SESSION_UNUSED): raise SystemExit("the ramdisk is not the pinned one these images are trimmed from")
        scripts = [b for n, (m, b) in files.items() if n.startswith("ccs/bin/") and b[:4] != b"\x7fELF"] + [byname["init"]["data"], byname["hakchi/init"]["data"]]
        code = b"\n".join(l for s in scripts for l in s.splitlines() if not l.lstrip().startswith(b"#"))
        for n in SESSION_UNUSED:
            if n.startswith("bin/") and re.search(rb"(?<![\w.-])" + re.escape(n[4:].encode()) + rb"(?![\w.-])", code): raise SystemExit(f"a script of {variant} runs {n}, which this image leaves out (SESSION_UNUSED)")
        ents = [e for e in ents if e["name"] not in unused]; changes["removed"] += sorted(unused)
    pics = cards(variant)
    for n, b in sorted(pics.items()): files[f"ccs/cards/{n}.png"] = (0o100644, b)
    h = hashlib.sha256()
    for n in sorted(files): h.update(n.encode() + b"\0" + files[n][1] + b"\0")
    h.update(byname["init"]["data"]); h.update(byname["hakchi/init"]["data"])
    for n in sorted(unused): h.update(b"-" + n.encode() + b"\0")   # the left-out files are part of the image's identity
    image_id = f"{variant}/{VERSION}/{h.hexdigest()[:16]}"
    files["ccs/image-id"] = (0o100644, image_id.encode() + b"\n")
    ino = max(e["ino"] for e in ents) + 1; add = []
    for d in sorted(set(dirs), key=lambda x: (x.count("/"), x)): add.append(entry(d, 0o040755, b"", ino)); ino += 1
    for n in sorted(files): add.append(entry(n, files[n][0], files[n][1], ino)); ino += 1
    changes["added"] = [e["name"] for e in add]
    new_cpio = cpio_build(ents + add + [trailer]); rd = xz(new_cpio)
    img = bootimg_build(orig, rd)
    if bootimg_parse(img)["cmdline"] != orig["cmdline"] or bootimg_parse(img)["kernel"] != orig["kernel"]: raise SystemExit("the kernel or command line changed")
    padded = (len(img) + FEL_SECTOR - 1) // FEL_SECTOR * FEL_SECTOR
    if padded > FEL_MAX: raise SystemExit(f"image {padded} exceeds the FEL transfer limit")
    d = os.path.join(out, variant); os.makedirs(d)
    name = f"{variant}-boot.img"
    outs = {name: img, "fes1.bin": inp["fes1"], "uboot-sd.bin": inp["uboot_sd"]}
    for n, b in outs.items(): open(os.path.join(d, n), "wb").write(b)
    man = {"schema": "ccs-h1-ram-image/2", "image": name, "variant": variant, "image_id": image_id, "profile": profile,
           "purpose": "Classic Cart Studio Console Backup & Restore: " + {"ccs-backup": "reads the console's built-in storage for a backup; contains no code that writes it",
                      "ccs-restore": "puts back a captured state, only the parts and bytes a confirmed restore names; also reads (the protective backup)",
                      "ccs-prepare": "Classic Cart Studio's first-time console preparation (Hakchi2 CE 3.9.3's installation as checked steps, ccsprep); also reads",
                      "ccs-rescue": "Console Rescue: this console's own boot0/boot1 from its verified backup, staged, written once and read back (ccsrescue, ccsboot); also reads",
                      "ccs-files": "Expert and Developer Mode: one checked file change at a time on the data storage (ccsfiles), and Developer Mode's startup software and system part, each staged, written once and read back (ccsdev), under a session restore point; also reads"}[variant],
           "fel": {"vid": "1F3A", "pid": "EFE8", "board_id": "0x00166700", "fes1_base": "0x2000", "uboot_base": "0x47000000", "image_base": "0x47400000",
                   "image_max_bytes": FEL_MAX, "sector_bytes": FEL_SECTOR, "boot_command": "boota 47400000", "fes1": "fes1.bin", "uboot": "uboot-sd.bin"},
           "inputs": {"basehmods.tar": BASEHMODS_SHA, "hakchi.hmod": HMOD_SHA, "boot.img": BOOTIMG_SHA, "hakchi.exe": HAKCHI_EXE_SHA,
                      **({"ccsboot.c": CCSBOOT_SRC_SHA, "ccsboot": CCSBOOT_SHA} if variant in ("ccs-prepare", "ccs-rescue") else {})},
           **({"methods": {n: {"file": f"ccs/prep/methods/{n}.hmod", "sha256": sha(b), "size": len(b)} for n, b in method_packages().items()}} if variant == "ccs-prepare" else {}),
           "outputs": {n: {"sha256": sha(b), "size": len(b)} for n, b in outs.items()},
           "boot_image": {"kernel_bytes": len(orig["kernel"]), "ramdisk_xz_bytes": len(rd), "ramdisk_cpio_bytes": len(new_cpio), "fel_padded_bytes": padded, "cmdline": orig["cmdline"]},
           "ramdisk_changes": changes, "ccs_files": {n: sha(b) for n, (m, b) in sorted(files.items())},
           "license": "Hakchi2 CE components: GPL-3.0 (github.com/TeamShinkansen/Hakchi2-CE v3.9.3 / 25bfcfd); kernel and modules GPL-2.0; fes1/u-boot: binaries as distributed by Hakchi2 CE; Classic Cart Studio parts GPL-3.0-or-later"}
    mb = (json.dumps(man, indent=1) + "\n").encode()
    open(os.path.join(d, "MANIFEST.json"), "wb").write(mb)
    with open(os.path.join(d, "SHA256SUMS"), "w", newline="\n") as f:
        for n, b in sorted(outs.items()): f.write(f"{sha(b)}  {n}\n")
    return {"variant": variant, "image_id": image_id, "image_sha256": sha(img), "bytes": len(img), "dir": d}

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--hakchi-release", required=True); ap.add_argument("--out", required=True); ap.add_argument("--variants", default=",".join(VARIANTS))
    ap.add_argument("--ccsboot", required=True, help="the console build of ram/ccsboot (ccsboot/build.sh), pinned by CCSBOOT_SHA")
    ap.add_argument("--chooser-program", required=True, help="the console build of the native HOME program (ccs-home-armv7: tools/console-simulator, reproducible), for CCS Nucleus's chooser package")
    ap.add_argument("--chooser-fonts", help="the player's menu fonts (.ccsf and their OFL license files), for CCS Nucleus's chooser package; default: baked here from the HOME theme's own fonts (tools/publish/assets/home, as the site's build does)")
    a = ap.parse_args()
    global CHOOSER_INPUTS
    fonts = a.chooser_fonts
    if not fonts:   # the same fonts the site's build bakes (tools/player/bake_fonts.py from brand/home, a copy of tools/publish/assets/home)
        import subprocess, tempfile
        fonts = os.path.join(tempfile.mkdtemp(prefix="ccs-chooser-fonts-"), "fonts")
        subprocess.run([sys.executable, os.path.join(TOOLS, "player", "bake_fonts.py"), os.path.join(TOOLS, "publish", "assets", "home"), fonts], check=True, stdout=subprocess.DEVNULL, stdin=subprocess.DEVNULL)
    CHOOSER_INPUTS = {"program": a.chooser_program, "fonts": fonts}
    if os.path.exists(a.out) and os.listdir(a.out): raise SystemExit(f"{a.out} exists and is not empty: images are always built into a new folder")
    if sha(open(os.path.join(HERE, "ccsboot", "ccsboot.c"), "rb").read()) != CCSBOOT_SRC_SHA: raise SystemExit("ccsboot.c is not the pinned source (update CCSBOOT_SRC_SHA and CCSBOOT_SHA deliberately)")
    cb = open(a.ccsboot, "rb").read()
    if sha(cb) != CCSBOOT_SHA: raise SystemExit("the ccsboot program is not the pinned build of ccsboot.c")
    os.makedirs(a.out, exist_ok=True); inp = read_inputs(a.hakchi_release); inp["ccsboot"] = cb; res = []
    for v in a.variants.split(","):
        if v not in VARIANTS: raise SystemExit(f"unknown variant {v}")
        res.append(build_variant(inp, v, a.out))
    for r in res: print(f"{r['variant']}: {r['image_id']}  {r['bytes']} bytes  sha256 {r['image_sha256']}")
    return 0

if __name__ == "__main__": sys.exit(main())
