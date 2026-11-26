"""Tests of the built Classic Cart Studio recovery images (tests only; no console, no USB). Copyright (C) 2026 Rance9, GPL-3.0-or-later.

  python test_recovery_images.py --images DIR --hakchi-release REL   (DIR: build_images.py's output, with ccs-backup/, ccs-restore/ and
  ccs-prepare/; REL: the pinned Hakchi2 CE 3.9.3 release folder or its portable package .zip). Windows (WSL) or Linux.

1. Static audit of the exact files inside each image's ramdisk: /init (hakchi's, with the two reviewed changes and nothing else), the
   replaced /hakchi/init (ccs-init), /ccs/bin/ccsbr and ccs-idle: no mount of persistent storage, no normal-system hand-over, no hakchi
   install/repair/mount_base path, no flash or file-system tool, and (backup and preparation images) no helper write code at all. The
   preparation image's ccsprep: only in that image, its four steps in hakchi's order, one burn per step, the two corrected functions
   defined after hakchi's, and its hakchi files exactly the pinned hakchi.hmod's.
2. Runs on the image's OWN BusyBox (hakchi 3.9.3's 1.29, ARM, under qemu-arm in WSL or on Linux), in a disposable folder, with the console's
   storage tools replaced by logging stand-ins and the storage by plain files: every write-capable call is a TRIPWIRE.
   a) /init then ccs-init, in a copy whose only difference is that the absolute paths point into the folder (checked by mapping them
      back: the copy then equals the image's file byte for byte) and that "-b" (block device) tests read "-e" (plain files stand in for
      the devices here); with and without the system key, and with ccs-init failing: every path ends in ccs-idle, never in the normal
      system, and nothing is written;
   b) ccsbr's reads (identity, every target, the console's own checks) exactly as the engine expects them, the storage unchanged;
   c) the backup image refuses every write; the restore image writes only the named target, checks what it received first, burns a
      startup image once (never twice, even when the readback differs) and reads every write back;
   d) the preparation image's ccsprep steps on a stand-in stock console: hakchi's own mod_install/mod_repair (rsync and all) with the
      data storage as a folder, u-boot, the original kernel to the spare slot, hakchi's kernel with the console's key; each refusal,
      each burn once, a failed readback never burned again, the first-stage loader, physical storage and logical parts never written.
   e) the preparation (with its way back), rescue and files images again on a console shaped like a Super NES Classic (board dp-shvc,
      REGION USA, EUR or JPN, a synthetic key, its stock game folders, its boot-stage sizes): ccsbr identify and hakchi's own
      softwareCheck, hakchi's mod_install, u-boot, spare slot and kernel with that console's key, the way back with its own u-boot, the
      rescue of both stages and Developer Mode's system part through its own mapping; the save and stock game folders unchanged.
QEMU is not the console: this shows what the image's own scripts and tools do with this text, not the console's hardware."""
from __future__ import annotations
import argparse, base64, hashlib, io, json, lzma, os, re, subprocess, sys, tarfile, tempfile
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import build_images as B   # noqa: E402

QEMU = None   # the absolute path in WSL (the runs clear the environment, so $HOME cannot be used inside them)
passed = failed = 0; failures = []
def check(ok: bool, what: str):
    global passed, failed
    if ok: passed += 1
    else: failed += 1; failures.append(what)
    print(("ok   " if ok else "FAIL ") + what, flush=True)

def wsl(script: str, timeout=900) -> subprocess.CompletedProcess:
    """bash: in WSL from Windows, directly on Linux (the release workflow's Linux job)"""
    cmd = ["wsl", "-e", "bash", "-c", script] if os.name == "nt" else ["bash", "-c", script]
    # stdin closed: a step that sends no data must see its input end at once, whatever the caller's own input is (a build runs it in the background)
    return subprocess.run(cmd, stdin=subprocess.DEVNULL, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout)

def wpath(p: str) -> str:
    p = os.path.abspath(p)
    return "/mnt/" + p[0].lower() + p[2:].replace("\\", "/") if os.name == "nt" else p

def code(text: str) -> str:
    """the script without its comments (the audit looks at what runs, not at what the comments explain)"""
    return chr(10).join(l for l in text.splitlines() if not l.lstrip().startswith("#"))

def ramdisk(img_path: str) -> list[dict]:
    b = open(img_path, "rb").read(); orig = B.bootimg_parse(b); return B.cpio_parse(lzma.decompress(orig["ramdisk"]))[:-1], orig["cmdline"]

FORBIDDEN_ALL = [r"switch_root", r"mount_base", r"/hakchi/script", r"\bburn_boot", r"\bmke2fs\b", r"\be2fsck\b", r"\btune2fs\b", r"\bresize2fs\b",
                 r"sunxi-part\s+(tune|shrink)", r"phy_write", r"log_write", r"\bkexec\b", r"rm -rf", r"remount", r"\bpack_install\b", r"\bmod_install\b", r"\bmemboot\b"]
FORBIDDEN_BACKUP = [r"of=", r"--setrw", r"conv=fsync", r"\bwrite\)"]

def static_audit(variant: str, ents: list[dict], cmdline: str):
    by = {e["name"]: e for e in ents}
    init = by["init"]["data"].decode(); ccsinit = by["hakchi/init"]["data"].decode(); ccsbr = by["ccs/bin/ccsbr"]["data"].decode(); idle = by["ccs/bin/ccs-idle"]["data"].decode()
    check("ro" in cmdline.split() and "decrypt" in cmdline.split(), f"{variant}: the kernel command line keeps \"ro\" (read-only encryption mapping and system mount): {cmdline}")
    check(init.endswith(B.NEW_TAIL) and "switch_root" not in init and '"/dev/nandd"' not in init, f"{variant}: /init never hands over to the normal system and never mounts nandd")
    # /init equals hakchi's with exactly the two reviewed replacements
    h = io.BytesIO()
    check(B.patch_init(init.replace(B.NEW_TAIL, B.TAIL).replace("# (Classic Cart Studio: the SEGA nandd fallback mount is removed)\n", B.SEGA_BLOCK).encode()).decode() == init, f"{variant}: /init is hakchi 3.9.3's with only the two reviewed changes")
    mounts = re.findall(r"^\s*(mount\b[^\n]*)", init, re.M)
    ok_mounts = all(re.search(r"-t (devtmpfs|proc|sysfs|devpts|tmpfs)\b", m) or 'mount -o "${MOUNT_OPTS}" "${ROOTFS}" "$mountpoint"' in m for m in mounts)
    check(ok_mounts and 'MOUNT_OPTS="ro,${MOUNT_OPTS}"' in init and '[ "${READONLY}" = "y" ] && READONLY="--readonly"' in init, f"{variant}: /init mounts only RAM file systems, and the system read-only (ro on the command line): {len(mounts)} mounts")
    for name, text in (("ccs-init", code(ccsinit)), ("ccsbr", code(ccsbr)), ("ccs-idle", code(idle))):
        hits = [p for p in FORBIDDEN_ALL + (FORBIDDEN_BACKUP if variant in ("ccs-backup", "ccs-prepare", "ccs-rescue") else []) if re.search(p, text)]
        if variant == "ccs-restore" and name == "ccsbr": hits = [p for p in hits if p not in (r"\bburn_boot",)]
        check(not hits, f"{variant}: {name} has no forbidden step ({', '.join(hits) or 'none'})")
    ci = code(ccsinit)
    check(not re.search(r"^\s*mount\b", ci, re.M) and "source " not in ci and not re.search(r"^\s*\.\s+/hakchi", ci, re.M) and "/hakchi/rootfs/etc/preinit" not in ci, f"{variant}: ccs-init mounts nothing and sources none of hakchi's scripts")
    check(ccsinit.rstrip().endswith('exec sh "$CCS/bin/ccs-idle"') and '|| exec sh "$CCS/bin/ccs-idle"' in ccsinit, f"{variant}: ccs-init ends in ccs-idle, also when its settings cannot be read")
    check("hakchi/rootfs/etc/init.d/S92rndis" not in by, f"{variant}: no USB network start script")
    if variant in ("ccs-backup", "ccs-prepare", "ccs-rescue"): check("#<write>" not in ccsbr and "write)" not in ccsbr, f"{variant}: the helper has no write section at all")
    if variant != "ccs-rescue": check("ccs/bin/ccsrescue" not in by, f"{variant}: no rescue script")
    else: rescue_static(by)
    if variant != "ccs-files": check("ccs/bin/ccsfiles" not in by, f"{variant}: no file-change script")
    else: files_static(by)
    if variant != "ccs-prepare":
        check("ccs/bin/ccsprep" not in by and not any(n.startswith("ccs/prep") for n in by), f"{variant}: no preparation script and no hakchi installation files")
    else: prep_static(by)

# ---------------------------------------------------------------- the preparation image (ccsprep): static
FORBIDDEN_PREP = [r"\bmemboot\b", r"reboot_kexec", r"\bkexec\b", r"switch_root", r"\bpacks?_install\b", r"\bpacks?_uninstall\b", r"\bmod_uninstall\b",
                  r"\brestoreBoot2\b", r"\bbackupBoot2\b", r"\bflashBoot0\b", r"burn_boot0", r"phy_write", r"log_write", r"\bmke2fs\b", r"\be2fsck\b",
                  r"sunxi-part\s+\w", r"rm -rf", r"of=", r"\breboot\b", r"\bshutdown\b", r"^\s*mount\b", r"\bumount\b", r"--setrw", r"\bnandc\b", r"/dev/nand",
                  r"\bburn0\b", r"\bburn1\b", r"echo \"default\"", r"echo[^\n|]*\|\s*sunxi-flash"]
WORKDIR = [None]   # the PC-side work folder (main)
HOME_ARCHIVE = [None]; EXPORT = [None]   # --home-archive DIR (the engine's own CCS HOME archive and its arguments), --export-data FILE
HMOD = None   # the pinned hakchi.hmod's members {name: (kind, bytes)} (--hakchi-release)

def head_of(c: str) -> str: return c[:c.index('case "$1" in')]

def prep_static(by: dict):
    p = by["ccs/bin/ccsprep"]["data"].decode(); c = code(p)
    check(by["ccs/profile"]["data"] == b"prepare\n" and '[ "$(cat "$CCS/profile" 2>/dev/null)" = prepare ] || { echo' in c.split("HMOD=")[0],
          "ccs-prepare: ccsprep refuses to run in any image but the preparation image, before anything else")
    # Return to Original Software runs two of hakchi's uninstall commands itself, each once and only in its own step (the removal of hakchi's
    # files, its sunxi-part shrink), and writes the bootloader only with ccsboot burn1, once, in ret-uboot (never hakchi's "default" u-boot);
    # everything else stays forbidden, in every step
    allowed = {"ret-uboot": 'out=$("$CCS/bin/ccsboot" burn1 "$stage" 2>/dev/null) || r ret-uboot "failed burn $(echo $out) (not burned again)"', "ret-files": 'rm -rf "$installpath"; gone=yes',
               "ret-table": 'sunxi-part shrink >/dev/null || r ret-table "failed shrink"',
               # CCS Mod / CCS Nucleus: the data storage mounted by one helper (read-only without journal replay, or read-write), unmounted by one
               "dmount": 'dmount(){ mountpoint -q "$D" && return 1; mkdir -p "$D"; mount -o "$1,noatime" "$DEV/nandc" "$D"; }', "dumount": 'dumount(){ sync; umount "$D"; }',
               # CCS Nucleus's start setting and an owner's system image as a CCS Nucleus choice: the image's own pinned CCS Nucleus program
               # unpacked in RAM (a fresh folder)
               "npkg": 'X="$ST/nucleus-pkg"; rm -rf "$X"; mkdir -p "$X" && ( cd "$X" && tar -xzf "$CCS/prep/methods/ccs-nucleus.hmod" )',
               # CCS HOME's package: the received archive in RAM, and the attempt's own staging folder (replaced when an earlier try of the
               # same attempt left it), each removed by one helper
               "hram": 'hram(){ rm -rf "$ST/home"; }', "hstage": 'hstage(){ rm -rf "$HC/staging/$copy"; }'}
    audited = c
    for k, v in allowed.items(): audited = audited.replace(v, "", 1)
    hits = [x for x in FORBIDDEN_PREP if re.search(x, audited, re.M)]
    check(not hits and all(c.count(v) == 1 for v in allowed.values()), f"ccs-prepare: ccsprep calls no forbidden step itself but the two uninstall commands of Return to Original Software and ccsboot's one bootloader write, once each ({', '.join(hits) or 'none'}; hakchi's own mod_install tunes the data storage)")
    cb = by.get("ccs/bin/ccsboot", {}).get("data", b"")
    check(hashlib.sha256(cb).hexdigest() == B.CCSBOOT_SHA and by["ccs/bin/ccsboot"]["mode"] & 0o111, "ccs-prepare: the image's ccsboot is the pinned build of ram/ccsboot/ccsboot.c (" + hashlib.sha256(cb).hexdigest()[:16] + ")")
    i_src = max(c.index('source_base "$HK/rootfs/etc/preinit.d"'), c.index('. "$HK/script/install"'))
    i_fb = c.index("flashBoot(){"); fb = c[i_fb:c.index("\n}\n", i_fb)]
    ib = [l for l in c.splitlines() if "installBoot2" in l]
    check(i_fb > i_src and len(ib) == 1 and c.index(ib[0]) > i_src, "ccs-prepare: flashBoot and installBoot2 are redefined AFTER hakchi's functions are sourced (the definitions in force)")
    check(fb.count("burn_boot") == 1 and "isValidBootImageNand" not in fb and '[ "$md5i" = "$md5f" ] && return 0' in fb and "not burned again" in fb,
          "ccs-prepare: flashBoot burns once and reports a failed readback (upstream burns a second time)")
    check(ib[0].startswith("installBoot2(){") and ib[0].rstrip().endswith("return 1; }") and "flash" not in ib[0].split("echo", 1)[0].lower() and "flashBoot" not in ib[0],
          "ccs-prepare: installBoot2 refuses (the original kernel's copy and the kernel are separate checked steps)")
    body = c[c.index('case "$1" in'):]
    steps = {}
    for m in re.finditer(r"^  ([\w|-]+)\)\n(.*?);;\n(?=  [\w|-]+\)\n|  \*\)|  # ---- |esac)", body, re.M | re.S): steps[m.group(1)] = m.group(2)
    check(list(steps) == ["files", "uboot", "slot68", "kernel", "ret-stage", "ret-kernel", "ret-uboot", "ret-files", "ret-table", "mstate", "method", "unlist", "nstart", "nmadd|nmdrop", "rput", "rimport", "rdrop", "hput", "huse", "hdrop"], f"ccs-prepare: exactly the four preparation steps in hakchi's order, then the staging of this console's bootloader and the four return steps in mod_uninstall's order, then the four CCS Mod / CCS Nucleus steps, the two for a community menu's choice, the three for an owner's system image and the three for CCS HOME's package ({', '.join(steps)})")
    hp, hu = steps.get("hput", ""), steps.get("huse", "")
    check(hp.count("dmount rw") == 1 and hu.count("dmount rw") == 1 and 'head -c "$size" > "$A/in.tar"' in hp and hp.find('[ "$n" = "$size" ] && [ "$got" = "$want" ] ||') < hp.find("dmount rw") and hp.find("$((total + 8000000))") < hp.find('echo "began hput"')
          and hp.find("ramfree") < hp.find('head -c "$size"') and 'mv "$S/pkg" "$P/$copy"' in hp and hp.count("hstage") == 1 and hp.find('echo "began hput"') < hp.find("hstage;") and "hcheck \"$P/$copy\" \"$A/sums\"" in hp and "dmount ro,noload || { hram; r hput" in hp
          and "pkg/current" not in hp and "/hakchi/" not in hp + hu and hu.find('[ "$cur" = "$prior" ] || [ "$cur" = "$copy" ] ||') < hu.find('echo "began huse"') < hu.find('mv -f "$P/current.ccs-new" "$P/current"') and hu.find("hcheck") < hu.find("dmount rw") and "dmount ro,noload || r huse" in hu,
          "ccs-prepare: hput receives CCS HOME's archive into RAM (refused unless the RAM holds it) and checks its size and SHA-256 before the data storage is mounted, and the free space before it writes; it unpacks into its own staging folder, moves that in one rename and reads every file back read-only; it never touches the pointer or hakchi's folders. huse switches the pointer in one rename only when the copy reads back complete and the pointer is still the reviewed one")
    rp, ri, rd = steps.get("rput", ""), steps.get("rimport", ""), steps.get("rdrop", "")
    check(all(x.count("dmount rw") == 1 and "why=$(nfree) || { dumount; r " in x for x in (rp, ri, rd)) and 't="$D/ccs/nucleus/staging/$id.hsqs"' in rp and 'head -c "$size" > "$t.ccs-new"' in rp
          and '[ "$n" = "$size" ] && [ "$got" = "$want" ] && [ "$magic" = hsqs ] || { rm -f "$t.ccs-new"' in rp and '[ $((avail * 1024)) -ge $((size + 8000000)) ] ||' in rp and rp.find("$((size + 8000000))") < rp.find('echo "began rput"')
          and 'sh "$X/bin/ccs-nucleus" import' in ri and 'sh "$X/bin/ccs-nucleus" forget' in rd and "npkg ||" in ri and "npkg ||" in rd and "/hakchi/firmware" not in rp + ri + rd and "firmware" not in head_of(c).replace("Firmware", "")
          and rd.find('[ "$out" = kept ] && {') < rd.find('echo "began rdrop"'),
          "ccs-prepare: an owner's system image goes only to CCS Nucleus's own folders (never hakchi's firmware folder): received straight to the data storage under a new name and kept only when its size, SHA-256 and SquashFS magic match and it fits beside 8,000,000 bytes; imported and removed only by this image's own pinned CCS Nucleus program, and removed only when it is Nucleus's own unchanged file")
    ms, mt, ul = steps.get("mstate", ""), steps.get("method", ""), steps.get("unlist", "")
    check("dmount ro,noload" in ms and "dmount rw" not in ms and not re.search(r"\bcp\b|\bmv\b|\brm\b|>", ms.replace("2>/dev/null", "")) and all(x.count("dmount rw") == 1 for x in (mt, ul)) and "dmount rw" not in head_of(c)
          and all(k in ("method", "unlist", "nstart", "nmadd|nmdrop", "rput", "rimport", "rdrop", "hput", "huse", "hdrop") for k, v in steps.items() if "dmount rw" in v),
          "ccs-prepare: mstate only reads (the data storage mounted read-only without journal replay); only method, unlist, nstart, nmadd and nmdrop, rput, rimport, rdrop, hput, huse and hdrop mount it read-write, once each")
    nm = steps.get("nmadd|nmdrop", "")
    check(nm.count("dmount rw") == 1 and "npkg ||" in nm and 'sh "$X/bin/ccs-nucleus" menu-add "$id" "$pkg" "$prog" "$label"' in nm and 'sh "$X/bin/ccs-nucleus" menu-drop "$id" "$pkg"' in nm
          and nm.find("why=$(tfree) || { dumount; r ") < nm.find("dmount rw") and nm.find('nok || { dumount; r ') < nm.find("dmount rw") and nm.find('r "$step" "refused another kind of choice has this id"') < nm.find("dmount rw")
          and nm.find('r nmadd "ok already $id"') < nm.find("dmount rw") and "/hakchi/transfer" not in nm and "transfer" not in nm and 'back=$(mline "$id")' in nm and "dmount ro,noload || r \"$step\" \"failed readback mount\"" in nm,
          "ccs-prepare: nmadd and nmdrop change only a community menu's choice in CCS Nucleus's list, with this image's own pinned CCS Nucleus program; they refuse before the read-write mount when hakchi or CCS Nucleus's list is absent, a link is on the way, the console's CCS Nucleus is an earlier version or another kind of choice has the id; the same choice already there is not written again; read back")
    hd = steps.get("hdrop", "")
    check(hd.count("dmount rw") == 1 and hd.find("hnamed") < hd.find("dmount rw") and hd.find('hcheck "$P/$copy" "$T"') < hd.find("dmount rw") and hd.find('[ "$cur" = "$keep" ]') < hd.find("dmount rw")
          and hd.find("hother") < hd.find("dmount rw") and "pkg/current" not in hd and "> \"$P/current" not in hd and "mv " not in hd and "/hakchi/" not in hd and 'rm -f "$f"' in hd and hd.rfind("dmount ro,noload || r hdrop") > hd.find('echo "began hdrop"'),
          "ccs-prepare: hdrop decides with the data storage read-only (the copy in use is the reviewed one, no installation unfinished, no entry of Original Menu names the copy, every file is its record's) before it mounts it read-write once, checks all of it again then, removes only the files its record lists, never touches the pointer or hakchi's folders, and reads back read-only")
    sn = steps.get("nstart", "")
    check(sn.rstrip().endswith('r nstart "ok $d $c $h $s $t"') and sn.count("dmount rw") == 1 and "rootid \"$d\" && echo \"$c $h $s $t\" | grep -qE '^(always|never) (on|off) (default|last) [0-9]{1,2}$' || r nstart \"refused request\"" in sn and 'sh "$X/bin/ccs-nucleus" start "$d" "$c" "$h" "$s" "$t"' in sn and "npkg ||" in sn
          and sn.find("why=$(tfree) || { dumount; r nstart") < sn.find("dmount rw") and sn.find('[ "$now" = "$d $c $h $s $t" ] && r nstart "ok already') < sn.find("dmount rw")
          and -1 < sn.find('r nstart "refused the default is not one of CCS Nucleus\'s choices"') < sn.find("dmount rw") and -1 < sn.find('r nstart "refused an owner\'s system image opens first only with the chooser at start"') < sn.find("dmount rw")
          and '[ "$back" = "$d $c $h $s $t" ] || r nstart "failed readback' in sn and "/hakchi/" not in sn and "transfer" not in sn and 'npkg(){ X="$ST/nucleus-pkg";' in head_of(c) and 'tar -xzf "$CCS/prep/methods/ccs-nucleus.hmod"' in head_of(c),
          "ccs-prepare: nstart sets what opens at power-on only with this image's own pinned CCS Nucleus program (its start command, CCS Nucleus's own folder on the data storage; never hakchi's overlay or transfer folder), refuses before the read-write mount when hakchi is absent or a link is on the way, writes nothing when the setting is already the one asked for, and reads it back")
    check(all('mv -f "$t.ccs-new" "$t"' in x and 'why=$(tfree) || { dumount; r ' in x and "dmount ro,noload || r" in x and "ok already $want" in x for x in (mt, ul))
          and 't=$(tpath "$name")' in mt and 't="$D/hakchi/transfer/uninstall"' in ul and 'tpath(){ [ "$1" = ccs-nucleus-chooser ] && echo "$D/hakchi/transfer/hmod/$1.hmod" || echo "$D/hakchi/transfer/$1.hmod"; }' in head_of(c)
          and mt.find('= "$want" ] || r method "refused this image does not carry that package"') < mt.find("dmount rw") and '[ "$back" = "$want" ] || r method "failed readback' in mt and '[ "$back" = "$want" ] || r unlist "failed readback' in ul
          and 'pkgname(){ [ "$1" = ccs-mod ] || [ "$1" = ccs-nucleus ] || [ "$1" = ccs-nucleus-chooser ]; }' in head_of(c) and '[ $# -le 3 ]' in ul,
          "ccs-prepare: method and unlist write only into hakchi's transfer folder (a new name, then a rename; the startup chooser in its hmod/ folder, which hakchi installs after CCS Nucleus), only CCS Mod's, CCS Nucleus's or its chooser's own package (the image's copy, checked by SHA-256) or a removal list naming up to three of them, refuse when hakchi is absent, a link is on the way or something else waits there, and read back from the storage mounted again read-only")
    rs = steps.get("ret-stage", "")
    check('head -c "$size" > "$stage"' in rs and '[ "$n" = "$size" ] && [ "$got" = "$want" ] ||' in rs and '"$CCS/bin/ccsboot" check "$stage"' in rs and not re.search(r"burn|flash|sunxi|mount|dd ", rs),
          "ccs-prepare: ret-stage only receives the bootloader into RAM and checks its size, MD5 and image (ccsboot check); it writes nothing")
    check(all(v in steps.get(k, "") for k, v in allowed.items() if k.startswith("ret-")), "ccs-prepare: each uninstall command is in its own return step only")
    rk, ru = steps.get("ret-kernel", ""), steps.get("ret-uboot", "")
    check(0 <= rk.find('= "$want" ] || {') < rk.find('echo "began ret-kernel"') < rk.find("flashBoot2") and rk.find("haveBackup2 ||") >= 0 and "isOriginalBoot2 && r ret-kernel" in rk,
          "ccs-prepare: ret-kernel burns only the spare slot's copy, only when its MD5 is the one the PC reviewed (and not when the original already runs)")
    check(0 <= ru.find('= "$want" ] || r ret-uboot "refused the staged') < ru.find('[ "$a" = "$now" ] && [ "$b" = "$now" ] || r ret-uboot "refused') < ru.find('echo "began ret-uboot"') < ru.find("ccsboot\" burn1") and ru.count("burn1") == 1
          and ru.count('[ "$a" = "$want" ] && [ "$b" = "$want" ]') == 1 and "default" not in ru,
          "ccs-prepare: ret-uboot burns this console's staged bootloader once (ccsboot), only when the staged file is the reviewed one and both readers still see the reviewed bootloader; both readers must read it back")
    head = c[:c.index('case "$1" in')]
    burns = {k: re.findall(r"\bflashBoot[12]\b[^\n|]*", v) for k, v in steps.items()}
    check(all("mount_base" in steps.get(k, "") for k in ("files", "ret-files")) and all("mount_base" not in v for k, v in steps.items() if k not in ("files", "ret-files")) and "mount_base" not in head
          and 'mountpoint -q "$mountpoint/var/lib" || { umount_base; r files "failed mount"; }' in steps.get("files", "") and 'mountpoint -q "$mountpoint/var/lib" || { umount_base; r ret-files "failed mount"; }' in steps.get("ret-files", ""),
          "ccs-prepare: only the files and ret-files steps mount the data storage, and each stops when that mount did not happen")
    check(burns == {"files": [], "uboot": ['flashBoot1 "$HMOD/boot/uboot.bin" && r uboot "ok $(readBoot1 '], "slot68": ['flashBoot2 "$temppath/boot.img" 68 '],
                    "kernel": ['flashBoot2 "$temppath/boot.img" '], "ret-stage": [], "ret-kernel": ['flashBoot2 "$temppath/boot.img" '], "ret-uboot": [], "ret-files": [], "ret-table": [], "mstate": [], "method": [], "unlist": [], "nstart": [], "nmadd|nmdrop": [], "rput": [], "rimport": [], "rdrop": [], "hput": [], "huse": [], "hdrop": []},
          f"ccs-prepare: one burn per step: hakchi's u-boot, the original kernel to slot 68, hakchi's kernel to its place; on the way back the original kernel to its place ({burns})")
    check(steps.get("slot68", "").find("isOriginalBoot2 ||") < steps.get("slot68", "").find("flashBoot2") and steps.get("kernel", "").lstrip().startswith("haveBackup2 ||"),
          "ccs-prepare: the spare slot receives only the original kernel, and hakchi's kernel is refused while the original is not in the spare slot")
    # the hakchi files in the image are the pinned hakchi.hmod's (without its install and uninstall scripts), and its functions are the image's
    if HMOD is None: check(False, "ccs-prepare: the pinned hakchi.hmod was not given (--hakchi-release)"); return
    mine = {n[len("ccs/prep/hmod/"):]: e for n, e in by.items() if n.startswith("ccs/prep/hmod/")}
    want = {n: v for n, v in HMOD.items() if n not in ("install", "uninstall")}
    same = set(mine) == set(want) and all((mine[n]["mode"] & 0o170000 == 0o040000) if k == "dir" else mine[n]["data"] == b for n, (k, b) in want.items())
    check(same, f"ccs-prepare: the image's hakchi files are exactly the pinned hakchi.hmod's, without its install and uninstall scripts ({len(mine)} entries)")
    pk = {n[len("ccs/prep/methods/"):]: e["data"] for n, e in by.items() if n.startswith("ccs/prep/methods/") and e["mode"] & 0o170000 == 0o100000}
    check(sorted(pk) == ["ccs-mod.hmod", "ccs-nucleus-chooser.hmod", "ccs-nucleus.hmod"] and all(hashlib.sha256(b).hexdigest() == B.METHOD_SHAS[n[:-5]] for n, b in pk.items()),
          "ccs-prepare: the image carries exactly CCS Mod's, CCS Nucleus's and its startup chooser's packages, each the pinned build of tools/console-nucleus (build_images.py rebuilt and checked each against its pin) (" + ", ".join(f"{n} {hashlib.sha256(b).hexdigest()[:12]}" for n, b in sorted(pk.items())) + ")")
    fn = [n for n in want if n.startswith("etc/preinit.d/") and want[n][0] == "file"]
    check(bool(fn) and all(by["hakchi/rootfs/" + n]["data"] == want[n][1] for n in fn),
          f"ccs-prepare: hakchi.hmod's own functions ({', '.join(x.split('/')[-1] for x in fn)}) are byte-identical to the image's, which ccsprep sources")

# ---------------------------------------------------------------- the rescue image (ccsrescue): static
FORBIDDEN_RESCUE = [r"\bmount\b", r"\bumount\b", r"sunxi-part", r"\bflashBoot", r"burn_boot", r"phy_write", r"log_write", r"of=", r"\bmke2fs\b", r"\be2fsck\b",
                    r"rm -rf", r"\breboot\b", r"\bkexec\b", r"echo \"default\"", r"/dev/nand", r"\bdd\b", r"sntool", r"source_base", r"/hakchi/script"]

def rescue_static(by: dict):
    c = code(by["ccs/bin/ccsrescue"]["data"].decode())
    check(by["ccs/profile"]["data"] == b"rescue\n" and '[ "$(cat "$CCS/profile" 2>/dev/null)" = rescue ] || { echo' in c.split('case "$1" in')[0],
          "ccs-rescue: ccsrescue refuses to run in any image but the rescue image, before anything else")
    cb = by.get("ccs/bin/ccsboot", {}).get("data", b"")
    check(hashlib.sha256(cb).hexdigest() == B.CCSBOOT_SHA, "ccs-rescue: the image's ccsboot is the pinned build of ram/ccsboot/ccsboot.c")
    check("ccs/bin/ccsprep" not in by and not any(n.startswith("ccs/prep") for n in by), "ccs-rescue: no preparation script and no hakchi installation files")
    hits = [x for x in FORBIDDEN_RESCUE if re.search(x, c, re.M)]
    check(not hits, f"ccs-rescue: ccsrescue mounts nothing, writes no storage part, table or kernel, and never uses the flash tool's writer ({', '.join(hits) or 'none'})")
    body = c[c.index('case "$1" in'):]; steps = {}
    for m in re.finditer(r"^  ([\w|-]+)\)\n(.*?);;", body, re.M | re.S): steps[m.group(1)] = m.group(2)
    check(list(steps) == ["diag", "stage0|stage1", "burn1", "burn0"], f"ccs-rescue: exactly the steps diag, stage0/stage1, burn1, burn0 ({', '.join(steps)})")
    b1, b0 = steps.get("burn1", ""), steps.get("burn0", "")
    check(c.count('"$B" $mode "$stage"') == 1 and '"$B" $mode "$stage"' in b1 and c.count('"$B" burn0 "$stage"') == 1 and '"$B" burn0 "$stage"' in b0 and not re.search(r"burn[01]", steps.get("diag", "") + steps.get("stage0|stage1", "")),
          "ccs-rescue: one ccsboot write in each burn step and none anywhere else (diagnosis and staging write nothing)")
    check(0 <= b1.find('r burn1 "refused the staged') < b1.find('r burn1 "refused boot1 changed') < b1.find('echo "began burn1') < b1.find('"$B" $mode') < b1.find('[ "$a" = "$want" ] && [ "$b" = "$want" ]'),
          "ccs-rescue: burn1 writes only the reviewed staged bytes over the reviewed state, and both readers must read them back")
    code_lines = [l for l in c.splitlines() if not l.lstrip().startswith("#")]
    states = re.findall(r"grep -qE '\^\(\[0-9a-f\]\{32\}\|([a-z|]+)\)\$'", b1 + b0)
    check('mode=burn1; [ "$now" = unreadable ] && mode=burn1-unreadable' in b1 and sum(l.count("burn1-unreadable") for l in code_lines) == 1 and states == ["invalid|noimage|unreadable"] * 2,
          f"ccs-rescue: only a stage reviewed as unreadable is written by the explicit unreadable mode; the states a burn accepts are {states} (never nodriver)")
    check("3) echo unreadable ;; 4) echo noimage ;; 5) echo invalid ;; *) echo nodriver ;;" in c and "echo damaged" not in c.split("state1b()")[0],
          "ccs-rescue: the diagnosis keeps ccsboot's results apart (invalid, noimage, unreadable, nodriver), never one word for every failure")
    check(0 <= b0.find('r burn0 "refused the staged') < b0.find('r burn0 "refused boot0 changed') < b0.find('echo "began burn0"') < b0.find('"$B" burn0') < b0.find('[ "$a" = "$want" ]'),
          "ccs-rescue: burn0 writes only the reviewed staged bytes over the reviewed state and reads them back")

# ---------------------------------------------------------------- the files image (ccsfiles): static
FORBIDDEN_FILES = [r"sunxi-flash", r"sntool", r"burn", r"flashBoot", r"\bdd\b", r"of=", r"\bmke2fs\b", r"\be2fsck\b", r"\bfsck\b", r"\btune2fs\b", r"\bresize2fs\b",
                   r"sunxi-part", r"\breboot\b", r"\bkexec\b", r"rm -rf", r"ccsboot", r"/hakchi/script", r"source_base"]

def files_static(by: dict):
    c = code(by["ccs/bin/ccsfiles"]["data"].decode())
    check(by["ccs/profile"]["data"] == b"files\n" and '[ "$(cat "$CCS/profile" 2>/dev/null)" = files ] || { echo' in c.split("M=")[0], "ccs-files: ccsfiles refuses to run in any image but the files image, before anything else")
    hits = [x for x in FORBIDDEN_FILES if re.search(x, c, re.M)]
    check(not hits, f"ccs-files: ccsfiles never writes a boot stage, kernel, table or raw device and never repairs a file system ({', '.join(hits) or 'none'})")
    mounts = re.findall(r"^\s*.*\bmount -o[^\n]*", c, re.M)
    check(len(mounts) == 1 and '"$DEV/nandc" "$M"' in mounts[0] and "mountdata ro,noload" in c and c.count("mountdata rw") == 2,
          "ccs-files: only the data storage is ever mounted: read-only without journal replay for reading, read-write only inside put and del")
    check("ccs/bin/ccsboot" not in by and "ccs/bin/ccsprep" not in by and "ccs/bin/ccsrescue" not in by, "ccs-files: no boot-stage writer, preparation or rescue script")
    d = code(by["ccs/bin/ccsdev"]["data"].decode())
    dev_static(d)
    check('clover/profiles/?*|hakchi/games/?*|ccs/data/?*) ;; *) r "$step" "refused not a user file"' in c and "*/../*|*/./*|*//*" in c, "ccs-files: Expert Mode's user-file folders and the path rule are enforced on the console too")

def dev_static(d: str):
    """ccsdev (Developer Mode): the startup-software steps write only slot 30; the system-part steps write only the system part (nandb),
    through hakchi 3.9.3's own mapping parameters, after their checks; sinfo and the staging steps write nothing"""
    head, k, sinfo, sstage, sburn = d[:d.index('case "$1" in')], d[d.index("  kstage)"):d.index("  sinfo)")], d[d.index("  sinfo)"):d.index("  sstage)")], d[d.index("  sstage)"):d.index("  sburn)")], d[d.index("  sburn)"):]
    any_bad = r"sunxi-part|mke2fs|e2fsck|\bfsck\b|tune2fs|resize2fs|rm -rf|reboot|kexec|check -f|ccsboot|burn_boot0|burn_boot1|burn_boot2 68|log_write|phy_write|/hakchi/script"
    check('[ "$(cat "$CCS/profile" 2>/dev/null)" = files ] || { echo' in head and not re.search(any_bad, d) and d.count("sunxi-flash burn_boot2") == 1 and "sunxi-flash burn_boot2 30" in k
          and not re.search(r"\bmount\b|of=|cryptsetup|blockdev", k) and k.find('r kburn "refused the staged') < k.find('r kburn "refused the startup software changed') < k.find('echo "began kburn"') < k.find("burn_boot2 30"),
          "ccs-files: ccsdev's startup-software steps write only the startup slot (30), once, the reviewed staged image over the reviewed startup software, without fixing the image")
    check(not re.search(r"of=|cryptsetup|\bmount\b|umount|--setrw|sunxi-flash|>\s*\"\$(SYS|MAP|DEV)", sinfo + sstage), "ccs-files: ccsdev sinfo and sstage write nothing to storage (they read, and keep the image in RAM)")
    opens = re.findall(r"cryptsetup open [^\n;|&}]*", sburn); dds = re.findall(r"\bdd [^\n;|]*of=[^\n;|]*", sburn); mounts = re.findall(r"\bmount -o [^\n;|&]*", sburn)
    check('SYS="$DEV/nandb"' in head and 'MAP="$DEV/mapper/root-crypt"' in head and sburn.count("tgt=") == 2 and 'tgt="$SYS"' in sburn and 'tgt="$MAP"' in sburn
          and dds == ['dd "if=$sstage_f" "of=$tgt" bs=131072 conv=notrunc,fsync 2>/dev/null'] and len(opens) == 2 and sum("--readonly" in o for o in opens) == 1
          and all(o.startswith('cryptsetup open "$SYS" root-crypt') and "--type plain --cipher aes-xts-plain --key-file \"$KEY\"" in o for o in opens)
          and all(m.startswith("mount -o ro,noatime") for m in mounts) and "sunxi-flash" not in sburn,
          "ccs-files: ccsdev sburn writes only the system part (nandb), once, through the console's own mapping with hakchi 3.9.3's parameters (plain, aes-xts-plain, its key file), and mounts the system only read-only")
    order = [sburn.find(x) for x in ('r sburn "refused the staged system', 'r sburn "refused the system part changed', 'umount "$NEWROOT"', 'echo "began sburn"', 'dd "if=$sstage_f"')]
    check(all(i >= 0 for i in order) and order == sorted(order), "ccs-files: ccsdev sburn checks the staged image and the part's current bytes, lets the read-only system go, and only then writes")

# ---------------------------------------------------------------- the disposable folder (built in WSL from a tar of the ramdisk)
def tar_of(ents: list[dict]) -> bytes:
    o = io.BytesIO()
    with tarfile.open(fileobj=o, mode="w") as t:
        for e in ents:
            n = e["name"]; m = e["mode"]
            if n in ("", "."): continue
            ti = tarfile.TarInfo(n); ti.mode = m & 0o7777; ti.mtime = B.MTIME
            if m & 0o170000 == 0o040000: ti.type = tarfile.DIRTYPE; t.addfile(ti)
            elif m & 0o170000 == 0o120000: ti.type = tarfile.SYMTYPE; ti.linkname = e["data"].decode(); t.addfile(ti)
            elif m & 0o170000 == 0o100000: ti.size = len(e["data"]); t.addfile(ti, io.BytesIO(e["data"]))
    return o.getvalue()

# The fixtures' injected corruption: flip FILE OFFSET [COUNT] turns each chosen byte into its complement (XOR 255), so every chosen byte
# always changes, also one that already holds the value a constant would have written (a constant "X" over random content left the
# byte as it was about 1 time in 256). Used in the stand-ins (STUBS) and the fixture scripts; fixture_flip_checks() shows it.
FLIP_SH = r'''flip(){ _i=0; while [ "$_i" -lt "${3:-1}" ]; do _o=$(($2 + _i)); _b=$(command -p od -An -tu1 -j "$_o" -N1 "$1" | command -p tr -d ' \n'); printf "\\$(printf '%03o' $(( ${_b:-0} ^ 255 )))" | command -p dd of="$1" bs=1 seek="$_o" conv=notrunc 2>/dev/null; _i=$((_i + 1)); done; }'''

STUBS = r'''#!/bin/bash
# the console's storage and flash tools as logging stand-ins over plain files in $R/fake (tests only)
R="__R__"; L="$R/log"; n=$(basename "$0"); echo "$n $*" >> "$L"
F="$R/fake"
trip(){ echo "TRIPWIRE $n $*" >> "$L"; exit 1; }
okimg(){ head -c 8 "$1" | grep -q 'ANDROID!' || [ "$(head -c 9 "$1" | tail -c 5)" = uboot ]; }   # a startup image or a u-boot image
__FLIP__
case "$n" in
  mount) case "$*" in "-o ro,noatime $R/dev/mapper/root-crypt $R/newroot"|""|"-o remount $R/newroot"|"-o remount $R/"|"-o ro,noatime $R/dev/nandb $R/newroot") exit 0;;
           "-o bind,ro $R/newroot $R/newroot/var/squashfs") echo "${!#}" >> "$R/mounted"; exit 0;;
           # hakchi's mount_base: the data storage read-write (the preparation's files step only); the folder stands in for it
           "-o ro,noload,noatime $R/dev/nandc $R/newroot/var/lib") echo "${!#}" >> "$R/mounted"; exit 0;;
           "-o rw,noatime $R/dev/nandc $R/newroot/var/lib") [ "$CCSTEST_ALLOW_WRITE" = 1 ] || trip "$@"; echo "WRITE mount nandc rw" >> "$L"; echo "${!#}" >> "$R/mounted"; exit 0;;
           "-o defaults,nosuid,nodev,noatime /dev/nandc $R/newroot/var/lib") [ "$CCSTEST_ALLOW_WRITE" = 1 ] || trip "$@"
                  echo "WRITE mount nandc read-write" >> "$L"; echo "${!#}" >> "$R/mounted"; exit 0;;
         esac
         case "$*" in *-t\ devtmpfs*|*-t\ proc*|*-t\ sysfs*|*-t\ devpts*|*-t\ tmpfs*) echo "${!#}" >> "$R/mounted"; exit 0;; esac
         case "$*" in *nand*|*mmc*|*/dev/sd*|*mapper*|*rw*) trip "$@";; esac; exit 0 ;;
  umount) t="${!#}"; grep -vxF "$t" "$R/mounted" > "$R/mounted.n" 2>/dev/null; mv "$R/mounted.n" "$R/mounted"; exit 0 ;;
  mountpoint) [ "$1" = -q ] && shift; grep -qxF "$1" "$R/mounted" 2>/dev/null; exit $? ;;
  insmod|cryptsetup) [ "$n" = cryptsetup ] && [ "$CCSTEST_MAPPED" = 1 ] && case "$1" in
           # Developer Mode's system part (CCSTEST_MAPPED): a mapping file is the part "decrypted" by a stand-in cipher (each byte +1 on
           # storage); a read-write mapping is written back to the part when it closes
           open) m="$R/dev/mapper/$3"; [ -f "$R/key-file" ] || exit 1
                 case "$*" in *--readonly*) : ;; *) [ "$CCSTEST_ALLOW_WRITE" = 1 ] || trip "$@"; echo "WRITE cryptsetup open $3 read-write" >> "$L"; echo "$2" > "$m.rw";; esac
                 LC_ALL=C /usr/bin/tr '\001-\377\000' '\000-\377' < "$2" > "$m"; exit 0 ;;
           close) m="$R/dev/mapper/$2"; [ -f "$m.rw" ] && { LC_ALL=C /usr/bin/tr '\000-\377' '\001-\377\000' < "$m" > "$(cat "$m.rw")"; rm -f "$m.rw"; }; rm -f "$m"; exit 0 ;;
         esac
         [ "$n" = cryptsetup ] && [ "$1" = open ] && { case "$*" in *--readonly*) : ;; *) trip "$@";; esac; [ -f "$R/key-file" ] && printf 'hsqs' > "$R/dev/mapper/root-crypt"; }; exit 0 ;;
  switch_root|kexec|mke2fs|e2fsck|tune2fs|resize2fs|fsck|hakchi|reboot) [ "$n" = reboot ] && [ "$CCSTEST_ALLOW_REBOOT" = 1 ] && exit 0; trip "$@" ;;
  decodepng) cat > /dev/null 2>&1 < "$1"; exit 0 ;;
  time) [ "$1" = -f ] && shift 2; exec "$@" ;;   # BusyBox time re-executes its own binary for an applet, which qemu-arm cannot do here
  clovershell) echo started > "$R/clovershell.started"; exit 0 ;;
  ccs-idle) echo IDLE >> "$L"; exit 0 ;;
  blockdev) case "$1" in --setro|--setrw|--flushbufs) exit 0;; --getro) echo 1; exit 0;; esac; exit 0 ;;
  sntool)
    case "$1 $2" in
      "devmem 0x01c23800") printf '\x61\xaa\x61\x04\x00\x00\x18\x88\x00\x00\x67\x1e\x00\x80\x11\x6c'; exit 0 ;;
      "sunxi_flash phy_read") cat "$F/phy"; exit $? ;;
      "check -f") okimg "$3"; exit $? ;;
      "check "*) okimg "$2"; exit $? ;;
      "ismd "*) exit 1 ;;
    esac; trip "$@" ;;
  sunxi-flash)
    case "$1" in
      nandinfo) echo "2048 64 4096"; exit 0 ;;
      read_boot0) cat "$F/boot0"; exit $? ;;
      read_boot1) cat "$F/boot1"; [ -f "$F/boot1.damaged" ] || [ -f "$F/boot1.unreadable" ] || [ -f "$F/driver.fails" ] && exit 1; exit 0 ;;
      read_boot2) t=boot2; [ "$2" = 68 ] && t=boot2-68; [ -f "$F/$t" ] || exit 1; cat "$F/$t"; exit 0 ;;
      ramdisk) [ "$2" = 68 ] && exit 1; cat "$F/ramdisk.cpio" 2>/dev/null; exit 0 ;;
      cmdline) cat "$R/proc/cmdline"; exit 0 ;;
      burn_boot1) [ "$CCSTEST_ALLOW_WRITE" = 1 ] || trip "$@"; echo "WRITE burn_boot1 $2" >> "$L"
                  cat > "$F/boot1.new"
                  # "default" (hakchi's uninstall): the flash tool's own u-boot; here the console's factory one, or another with CCSTEST_DEFAULT_DIFFERS
                  [ "$(cat "$F/boot1.new")" = default ] && { if [ "$CCSTEST_DEFAULT_DIFFERS" = 1 ]; then printf 'another u-boot' > "$F/boot1.new"; else cp "$F/boot1.factory" "$F/boot1.new"; fi; }; [ "$CCSTEST_CORRUPT_BURN" = 1 ] && flip "$F/boot1.new" 100; mv "$F/boot1.new" "$F/boot1"; exit 0 ;;
      burn_boot2) [ "$CCSTEST_ALLOW_WRITE" = 1 ] || trip "$@"; echo "WRITE burn_boot2 $2" >> "$L"; t=boot2; [ "$2" = 68 ] && t=boot2-68
                  cat > "$F/$t.new"; [ "$CCSTEST_CORRUPT_BURN" = 1 ] && flip "$F/$t.new" 100; mv "$F/$t.new" "$F/$t"; exit 0 ;;
    esac; trip "$@" ;;
  ccsboot)
    case "$1" in
      check) [ "$(head -c 11 "$2" | tail -c 7)" = eGON.BT ] && { echo "ok $(wc -c < "$2") boot0"; exit 0; }
             okimg "$2" && [ "$(head -c 9 "$2" | tail -c 5)" = uboot ] && { echo "ok $(wc -c < "$2") boot1"; exit 0; }; exit 7 ;;
      read0) [ -f "$F/driver.fails" ] && exit 2; [ -f "$F/boot0.unreadable" ] && exit 3; cat "$F/boot0"; [ -f "$F/boot0.damaged" ] && exit 5; exit 0 ;;
      read1) [ -f "$F/driver.fails" ] && exit 2; [ -f "$F/boot1.unreadable" ] && exit 3; [ -f "$F/boot1.noimage" ] && exit 4; cat "$F/boot1"; [ -f "$F/boot1.damaged" ] && exit 5; exit 0 ;;
      burn0) [ "$CCSTEST_ALLOW_WRITE" = 1 ] || trip "$@"; echo "WRITE ccsboot burn0" >> "$L"; cp "$2" "$F/boot0.new"
             [ "$CCSTEST_CORRUPT_BURN" = 1 ] && flip "$F/boot0.new" 100; mv "$F/boot0.new" "$F/boot0"; rm -f "$F/boot0.damaged"; echo "written 1"; exit 0 ;;
      burn1) [ "$CCSTEST_ALLOW_WRITE" = 1 ] || trip "$@"
             # the real writer stops at a block it cannot read before writing it (exit 10), having written none or some blocks
             [ -f "$F/boot1.unreadable" ] || [ -n "$CCSTEST_READ_FAILS_AT_BLOCK" ] && { echo "stopped ${CCSTEST_READ_FAILS_AT_BLOCK:-0}"; [ "${CCSTEST_READ_FAILS_AT_BLOCK:-0}" -gt 0 ] && echo "WRITE ccsboot burn1 stopped $CCSTEST_READ_FAILS_AT_BLOCK" >> "$L"; exit 10; }
             echo "WRITE ccsboot burn1" >> "$L"; cp "$2" "$F/boot1.new"
             [ "$CCSTEST_CORRUPT_BURN" = 1 ] && flip "$F/boot1.new" 100; mv "$F/boot1.new" "$F/boot1"; rm -f "$F/boot1.damaged"; echo "written 3"; exit 0 ;;
      burn1-unreadable) [ "$CCSTEST_ALLOW_WRITE" = 1 ] || trip "$@"; echo "WRITE ccsboot burn1-unreadable" >> "$L"; cp "$2" "$F/boot1.new"
             mv "$F/boot1.new" "$F/boot1"; rm -f "$F/boot1.damaged" "$F/boot1.unreadable"; echo "written 3 unread 2"; exit 0 ;;
    esac; trip "$@" ;;
  sunxi-part) [ -z "$1" ] && { if [ -f "$F/parttable.tuned" ]; then cat "$F/parttable.tuned"; else printf 'mbr:mbr:32\nnanda:boot:64\nnandb:rootfs:64\nnandc:data:256\nnandd:private:32\nnande:UDISK:32\n'; fi; exit 0; }
              [ "$1" = tune ] && { [ "$CCSTEST_ALLOW_WRITE" = 1 ] || trip "$@"; echo "WRITE sunxi-part tune" >> "$L"; printf 'mbr:mbr:32\nnanda:boot:64\nnandb:rootfs:64\nnandc:data:280\nnandd:private:32\nnande:UDISK:8\n' > "$F/parttable.tuned"; exit 0; }
              [ "$1" = shrink ] && { [ "$CCSTEST_ALLOW_WRITE" = 1 ] || trip "$@"; echo "WRITE sunxi-part shrink" >> "$L"; rm -f "$F/parttable.tuned"; exit 0; }; trip "$@" ;;
  dd) for a in "$@"; do case "$a" in of=*) case "$a" in *"$R/fake"*|*/dev/nand*|*/dev/mapper/*) [ "$CCSTEST_ALLOW_WRITE" = 1 ] || trip "$@"; echo "WRITE dd $a" >> "$L";; esac;; esac; done
      if [ "$CCSTEST_CORRUPT_BURN" = 1 ]; then for a in "$@"; do case "$a" in of=*/dev/mapper/*|of=*/dev/nandb)
        __QEMU__ -L "$R/root" "$R/root/bin/busybox" dd "$@"; rc=$?; flip "${a#of=}" 100; exit $rc;; esac; done; fi
      exec __QEMU__ -L "$R/root" "$R/root/bin/busybox" dd "$@" ;;
esac
trip "$@"
'''.replace("__FLIP__", FLIP_SH)

SETUP_SH = r'''set -e
R=$(mktemp -d /tmp/ccsri-@V@-XXXXXX)
mkdir -p "$R/root" "$R/wrap" "$R/stubs" "$R/fake" "$R/dev/mapper" "$R/proc" "$R/sys/devices/virtual/android_usb/android0" "$R/sys/devices/sunxi_usb" "$R/tmp" "$R/newroot/etc/clover"
tar -xf '@TAR@' -C "$R/root"
for a in $('@QEMU@' -L "$R/root" "$R/root/bin/busybox" --list); do
  printf '#!/bin/sh\nexec %s -L "%s/root" "%s/root/bin/busybox" %s "$@"\n' '@QEMU@' "$R" "$R" "$a" > "$R/wrap/$a"; chmod 755 "$R/wrap/$a"
done
sed "s#__R__#$R#g" '@STUB@' > "$R/stubs/_stub"; chmod 755 "$R/stubs/_stub"
printf '#!/bin/sh
exec %s -L "%s/root" "%s/root/bin/busybox" "$@"
' '@QEMU@' "$R" "$R" > "$R/wrap/busybox"; chmod 755 "$R/wrap/busybox"
for n in time mount umount mountpoint insmod cryptsetup switch_root kexec mke2fs e2fsck tune2fs resize2fs fsck hakchi reboot decodepng clovershell ccs-idle blockdev sntool sunxi-flash sunxi-part dd; do ln -s _stub "$R/stubs/$n"; done
printf '#!/bin/sh\nexec %s -L "%s/root" "%s/root/bin/rsync" "$@"\n' '@QEMU@' "$R" "$R" > "$R/wrap/rsync"; chmod 755 "$R/wrap/rsync"   # the image's own rsync (hakchi's copy)
printf '%s\n' '@CMDLINE@' > "$R/proc/cmdline"; echo 12.34 0 > "$R/proc/uptime"; touch "$R/dev/console"; echo 2 > "$R/sys/devices/sunxi_usb/usb_role"
for f in enable idVendor idProduct functions; do echo 0 > "$R/sys/devices/virtual/android_usb/android0/$f"; done
echo dp-nes > "$R/newroot/etc/clover/boardtype"; echo EUR_USA > "$R/newroot/etc/clover/REGION"; echo VERSION=release-v1.0.7-0-g4ea4041 > "$R/newroot/etc/clover/VERSION"
printf '/dev/mapper/root-crypt %s/newroot squashfs ro,noatime 0 0\nproc /proc proc rw 0 0\n' "$R" > "$R/proc/mounts"
printf 'major minor  #blocks  name\n\n  93        0         64 nand\n  93        8         32 nanda\n  93       16         32 nandb\n  93       24        128 nandc\n  93       32         16 nandd\n  93       40         16 nande\n' > "$R/proc/partitions"
# the test folder's own settings (paths into the folder; plain files as devices) and a ccs-idle that records it was reached instead of waiting
cp "$R/root/ccs/env" "$R/env.image"
printf 'DEV=%s/dev\nDEVKIND=file\nNEWROOT=%s/newroot\nSYSFS=%s/sys\nPROCSYS=%s/procsys\nMOUNTS=%s/proc/mounts\nPARTS=%s/proc/partitions\nST=%s/tmp/ccsbr\nFB=%s/fb0\nHK=%s/root/hakchi\n' "$R" "$R" "$R" "$R" "$R" "$R" "$R" "$R" "$R" > "$R/root/ccs/env"
[ -f "$R/root/ccs/bin/ccsboot" ] && { mv "$R/root/ccs/bin/ccsboot" "$R/ccsboot.image"; ln -s "$R/stubs/_stub" "$R/root/ccs/bin/ccsboot"; }   # the ARM program's stand-in (its own tests: ccsboot/test_ccsboot.py)
cp "$R/root/ccs/bin/ccs-idle" "$R/ccs-idle.image"
printf '#!/bin/sh\necho IDLE >> "%s/log"\n' "$R" > "$R/root/ccs/bin/ccs-idle"
echo "$R"
'''

MAP_SH = r'''R='@R@'; cd "$R/root"
mapit(){ sed -e "s#\"/proc/#\"$R/proc/#g; s#\"/dev/#\"$R/dev/#g; s#\"/key-file\"#\"$R/key-file\"#g; s#\"/hakchi/#\"$R/root/hakchi/#g; s#\"/newroot\"#\"$R/newroot\"#g; s#\"/lib/#\"$R/root/lib/#g; s#^cd /bin\$#cd $R/root/bin#; s#^cd /\$#cd $R#; s#^exec /ccs/bin/ccs-idle\$#exec $R/stubs/ccs-idle#; s#\[ -b #[ -e #g; s#^CCS=/ccs\$#CCS=$R/root/ccs#; s#/hakchi/shell/clovershell#$R/stubs/clovershell#; s#/proc/uptime#$R/proc/uptime#; s#for d in /dev/dm-#for d in $R/dev/dm-#" "$1"; }
back(){ sed -e "s#$R/root/ccs\$#/ccs#; s#$R/stubs/ccs-idle#/ccs/bin/ccs-idle#; s#$R/stubs/clovershell#/hakchi/shell/clovershell#; s#$R/root/#/#g; s#$R/root\$#/#; s#$R/#/#g; s#cd $R\$#cd /#; s#\[ -e #[ -b #g" "$1"; }
mapit init > "$R/init.mapped"; mapit hakchi/init > "$R/root/hakchi/init.mapped"
if back "$R/init.mapped" | cmp -s - init && back "$R/root/hakchi/init.mapped" | cmp -s - hakchi/init; then echo MAPPED_OK
else back "$R/init.mapped" | diff - init | head -12 >&2; back "$R/root/hakchi/init.mapped" | diff - hakchi/init | head -12 >&2; fi
sed -i "s#sh \"$R/root/hakchi/init\"#sh \"$R/root/hakchi/init.mapped\"#" "$R/init.mapped"
'''

def setup(variant: str, ents: list[dict], cmdline: str, work: str) -> str:
    tb = os.path.join(work, variant + ".tar"); open(tb, "wb").write(tar_of(ents))
    st = os.path.join(work, "stub.sh"); open(st, "w", newline="\n").write(STUBS.replace("__QEMU__", QEMU))
    sh = os.path.join(work, "setup-" + variant + ".sh")
    open(sh, "w", newline="\n").write(SETUP_SH.replace("@V@", variant).replace("@TAR@", wpath(tb)).replace("@QEMU@", QEMU).replace("@STUB@", wpath(st)).replace("@CMDLINE@", cmdline))
    r = wsl(f"bash '{wpath(sh)}'")
    if r.returncode != 0: raise SystemExit("setup failed: " + r.stdout[-1500:] + r.stderr[-1500:])
    return r.stdout.strip().splitlines()[-1]

def run(R: str, cmd: str, env: str = "", stdin_file: str | None = None) -> subprocess.CompletedProcess:
    """cmd run by the image's own BusyBox sh (under qemu-arm), with the stand-ins first on PATH and nothing else of WSL's"""
    q = cmd.replace("'", "'\\''")
    return wsl(f"cd '{R}' && env -i HOME=/ {env} PATH='{R}/stubs:{R}/wrap' '{QEMU}' -L '{R}/root' '{R}/root/bin/busybox' sh -c '{q}'" + (f" < '{stdin_file}'" if stdin_file else ""))

def sh_out(R: str, text: str) -> str: return wsl(text.replace("@R@", R)).stdout

def init_runs(variant: str, R: str, work: str):
    mp = os.path.join(work, "map-" + variant + ".sh"); open(mp, "w", newline="\n").write(MAP_SH.replace("@R@", R))
    r = wsl(f"bash '{wpath(mp)}'")
    check("MAPPED_OK" in r.stdout, f"{variant}: the test copies of /init and ccs-init differ from the image's only in their absolute paths and the device tests ({r.stderr.strip()[:300]})")
    cases = (("with the system key", "printf k > '@R@/key-file'"), ("without the system key", "rm -f '@R@/key-file' '@R@/dev/mapper/root-crypt'"),
             ("ccs-init's settings unreadable", "mv '@R@/root/ccs/env' '@R@/root/ccs/env.off'"))
    for case, prep in cases:
        sh_out(R, "rm -f '@R@/log' '@R@/clovershell.started'; for f in enable idVendor idProduct functions; do echo 0 > '@R@/sys/devices/virtual/android_usb/android0/'$f; done; " + prep)
        run(R, f"sh '{R}/init.mapped'")
        log = sh_out(R, "cat '@R@/log' 2>/dev/null")
        trips = [l for l in log.splitlines() if l.startswith("TRIPWIRE")]
        check("IDLE" in log and not trips and "switch_root" not in log, f"{variant} /init {case}: ends in ccs-idle, no tripwire, no normal-system start ({'; '.join(trips) or 'no writes'})")
        if case == "with the system key":
            co = next((l for l in log.splitlines() if l.startswith("cryptsetup open")), "?")
            check("--readonly" in co and any(l.startswith("insmod") and "nand.ko" in l for l in log.splitlines()) and any(l.startswith("mount -o ro,noatime") for l in log.splitlines()),
                  f"{variant} /init {case}: the NAND driver is loaded, the encryption mapping opened read-only and the system mounted read-only ({co})")
        if case != "ccs-init's settings unreadable":
            g = sh_out(R, "cd '@R@/sys/devices/virtual/android_usb/android0' && cat idVendor idProduct functions enable; test -f '@R@/clovershell.started' && echo STARTED").split()
            check(g[:4] == ["1f3a", "efe8", "clover", "1"] and "STARTED" in g, f"{variant} /init {case}: the clovershell gadget (1f3a:efe8) and its daemon are started: {g}")
            st = sh_out(R, "cat '@R@/tmp/ccsbr/start.log' 2>/dev/null")
            check("start " + variant in st and "gadget clover 1" in st, f"{variant} /init {case}: ccs-init recorded its start in RAM")
        else:
            check(sh_out(R, "test -f '@R@/clovershell.started' && echo yes").strip() != "yes", f"{variant} /init {case}: nothing is started; it waits in RAM")
            sh_out(R, "mv '@R@/root/ccs/env.off' '@R@/root/ccs/env'")

FAKE_SH = FLIP_SH + r'''
F='@R@/fake'; cd "$F"
head -c 32768 /dev/urandom > boot0; head -c 65536 /dev/urandom > boot1; { printf 'ANDROID!'; head -c 300000 /dev/urandom; } > boot2
for p in nanda:32768 nandb:32768 nandc:131072 nandd:16384 nande:16384 nand:65536; do head -c ${p#*:} /dev/urandom > "@R@/dev/${p%%:*}"; cp "@R@/dev/${p%%:*}" "$F/${p%%:*}.orig"; done
head -c 1048576 /dev/urandom > phy; printf '070701' > ramdisk.cpio; rm -f boot2-68
sha256sum boot0 boot1 boot2 phy @R@/dev/nand* | sort > "$F/before.sums"
head -c 16384 /dev/urandom > new-nandd; md5sum new-nandd | cut -d' ' -f1 > new-nandd.md5
{ printf 'ANDROID!'; head -c 200000 /dev/urandom; } > new-boot2; md5sum new-boot2 | cut -d' ' -f1 > new-boot2.md5
cp '@R@/dev/nandc' new-nandc; flip new-nandc 5000 3; md5sum new-nandc | cut -d' ' -f1 > new-nandc.md5
echo ok
'''

def ccsbr(R: str, args: str, env: str = "", stdin_file: str | None = None) -> subprocess.CompletedProcess:
    return run(R, f"sh '{R}/root/ccs/bin/ccsbr' {args}", env, stdin_file)

def ccsbr_runs(variant: str, R: str, work: str):
    fk = os.path.join(work, "fake-" + variant + ".sh"); open(fk, "w", newline="\n").write(FAKE_SH.replace("@R@", R)); wsl(f"bash '{wpath(fk)}'")
    sh_out(R, "rm -f '@R@/log'")
    res = ccsbr(R, "identify")
    kv = dict(l.split("=", 1) for l in res.stdout.splitlines() if "=" in l)
    check(res.returncode == 0 and kv.get("ccsbr") == "1" and kv.get("end") == "1" and kv.get("profile") == variant.split("-")[1] and bool(re.match(r"^[0-9a-f]{8}( [0-9a-f]{8}){3}$", kv.get("hwid", "")))
          and kv.get("nandinfo") == "2048 64 4096" and kv.get("slot68") == "absent" and kv.get("storage_mounts") == "" and kv.get("partitions", "").startswith("nand:64,nanda:32,")
          and kv.get("parttable", "").startswith("mbr:mbr:32 nanda:boot:64") and kv.get("system_mounted", "").startswith("/dev/mapper/root-crypt:squashfs:ro") and kv.get("board") == "dp-nes",
          f"{variant}: ccsbr identify answers every field the engine checks (hwid {kv.get('hwid')}, image {kv.get('image')}, partitions {kv.get('partitions')}, mounts '{kv.get('storage_mounts')}')")
    for t, src in (("boot2", "fake/boot2"), ("boot0", "fake/boot0"), ("nandc", "dev/nandc"), ("phy", "fake/phy")):
        got = run(R, f"sh '{R}/root/ccs/bin/ccsbr' read {t} | md5sum").stdout.split()[0:1]
        want = sh_out(R, f"md5sum '@R@/{src}' | cut -d' ' -f1; wc -c < '@R@/{src}'").split()
        summ = ccsbr(R, f"sum {t}").stdout.split()
        check(got == want[:1] and summ == [want[0], want[1], "0", "0"], f"{variant}: ccsbr read {t}: the bytes sent and the console's own check agree ({' '.join(summ)})")
    d = ccsbr(R, "digest nandd").stdout.split()
    check(d[:2] == sh_out(R, "md5sum '@R@/dev/nandd' | cut -d' ' -f1; wc -c < '@R@/dev/nandd'").split(), f"{variant}: ccsbr digest re-reads a part on the console only ({' '.join(d)})")
    r = run(R, f"sh '{R}/root/ccs/bin/ccsbr' read ../../etc/shadow; echo rc=$?")
    check("rc=2" in r.stdout, f"{variant}: an unknown target is refused by the helper too")
    # Console Browser's range reads: a checked piece of a logical part, nothing else
    got = run(R, f"sh '{R}/root/ccs/bin/ccsbr' range nandc 8192 4096 | md5sum").stdout.split()[0:1]
    want = sh_out(R, "dd if='@R@/dev/nandc' bs=4096 skip=2 count=1 2>/dev/null | md5sum | cut -d' ' -f1").split()
    summ = ccsbr(R, "sum nandc").stdout.split()
    check(got == want and summ == [want[0], "4096", "0", "0"], f"{variant}: ccsbr range nandc 8192 4096 sends exactly that piece and its own check agrees ({' '.join(summ)})")
    bad = [run(R, f"sh '{R}/root/ccs/bin/ccsbr' range {a}; echo rc=$?").stdout.strip().splitlines()[-1] for a in ("nandc 100 4096", "nandc 0 16781312", "boot0 0 4096", "phy 0 4096", "nandc 0 0", "nandc x 4096")]
    check(all(b == "rc=2" for b in bad), f"{variant}: range refuses unaligned, oversized or zero pieces and every target but nanda..nande ({', '.join(bad)})")
    r = run(R, f"sh '{R}/root/ccs/bin/ccsbr' read boot2-68; echo rc=$?; sh '{R}/root/ccs/bin/ccsbr' sum boot2-68")
    check("rc=1" in r.stdout and r.stdout.strip().endswith(" 0 1 0"), f"{variant}: an empty spare slot reads as absent, with its status ({r.stdout.strip().splitlines()[-1]})")
    log = sh_out(R, "cat '@R@/log'"); trips = [l for l in log.splitlines() if l.startswith("TRIPWIRE")]
    same = sh_out(R, "cd '@R@/fake' && sha256sum boot0 boot1 boot2 phy @R@/dev/nand* | sort | cmp -s - before.sums && echo SAME")
    check(not trips and "SAME" in same, f"{variant}: every read left the storage unchanged, with no tripwire ({'; '.join(trips) or 'none'})")
    def w(t, f, extra_env=""):
        md5 = sh_out(R, f"cat '@R@/fake/{f}.md5'").strip(); size = sh_out(R, f"wc -c < '@R@/fake/{f}'").strip()
        return run(R, f"sh '{R}/root/ccs/bin/ccsbr' write {t} {size} {md5}; echo rc=$?", extra_env, f"{R}/fake/{f}")
    if variant != "ccs-restore":   # every image but the restore image has no write verb at all
        r = w("nandd", "new-nandd", "CCSTEST_ALLOW_WRITE=1")
        log = sh_out(R, "cat '@R@/log'")
        check(("rc=2" in r.stdout or "rc=13" in r.stdout) and "WRITE" not in log and sh_out(R, "cmp -s '@R@/dev/nandd' '@R@/fake/nandd.orig' && echo SAME").strip() == "SAME",
              f"{variant}: the helper's write verb does not exist ({r.stdout.strip().splitlines()[-1]}); nothing was written")
        return
    r = w("nandd", "new-nandd", "CCSTEST_ALLOW_WRITE=1")
    check("result nandd ok" in r.stdout and sh_out(R, "cmp -s '@R@/dev/nandd' '@R@/fake/new-nandd' && echo SAME").strip() == "SAME", f"ccs-restore: a small part is received, checked, written once and read back ({r.stdout.strip()})")
    r = w("nandc", "new-nandc", "CCSTEST_ALLOW_WRITE=1")
    check("result nandc ok" in r.stdout and sh_out(R, "cmp -s '@R@/dev/nandc' '@R@/fake/new-nandc' && echo SAME").strip() == "SAME", f"ccs-restore: the data storage is written as it arrives and read back ({r.stdout.strip()})")
    bad = run(R, f"sh '{R}/root/ccs/bin/ccsbr' write nanda 16384 00000000000000000000000000000000; echo rc=$?", "CCSTEST_ALLOW_WRITE=1", f"{R}/fake/new-nandd")
    check("received-mismatch" in bad.stdout and sh_out(R, "cmp -s '@R@/dev/nanda' '@R@/fake/nanda.orig' && echo SAME").strip() == "SAME", "ccs-restore: data that does not match its checksum is refused before anything is written")
    sh_out(R, "rm -f '@R@/log'")
    r = w("boot2", "new-boot2", "CCSTEST_ALLOW_WRITE=1")
    log = sh_out(R, "cat '@R@/log'")
    check("result boot2 ok" in r.stdout and log.count("WRITE burn_boot2 30") == 1, f"ccs-restore: a startup image is checked (sntool check), burned once at its place (30) and read back ({r.stdout.strip()})")
    sh_out(R, "rm -f '@R@/log'")
    r = w("boot2", "new-boot2", "CCSTEST_ALLOW_WRITE=1 CCSTEST_CORRUPT_BURN=1")
    log = sh_out(R, "cat '@R@/log'")
    check("failed readback" in r.stdout and log.count("WRITE burn_boot2") == 1, f"ccs-restore: a burn whose readback differs is reported and NOT repeated (upstream flashBoot burns again) ({r.stdout.strip()})")
    r = w("phy", "new-nandd", "CCSTEST_ALLOW_WRITE=1")
    check("not-a-restore-target" in r.stdout, "ccs-restore: the physical image, the bootloaders and the storage table are never write targets")

# ---------------------------------------------------------------- the preparation image (ccsprep): runs
# The test copies differ from the image's files only where an absolute path names the console's RAM root (/, /bin, /etc, /sbin, /key-file,
# /newroot, hakchi's modpath) or sets the PATH (here: the stand-ins and the image's own BusyBox); checked by mapping every copy back.
PREP_MAPS = {
    "ccs/bin/ccsprep": [('[ -e "/$dir" ] || ln -nsf "$NEWROOT/$dir" "/" 2>/dev/null', '[ -e "@R@/root/$dir" ] || ln -nsf "$NEWROOT/$dir" "@R@/root/" 2>/dev/null'),
                        ('ln -nsf "$HK/rootfs/etc" "/" 2>/dev/null', 'ln -nsf "$HK/rootfs/etc" "@R@/root/" 2>/dev/null'),
                        ('export PATH="/sbin:$HK/rootfs/bin:/bin:$NEWROOT/sbin:/usr/sbin:$NEWROOT/bin:/usr/bin"', 'export PATH="@R@/stubs:@R@/wrap"')],
    "hakchi/rootfs/etc/preinit.d/b0000_defines": [('modpath="/$modname"', 'modpath="@R@/root/$modname"')],
    "hakchi/rootfs/etc/preinit.d/b0050_boot": [('local keyfile="/key-file"', 'local keyfile="@R@/key-file"')],
    "hakchi/script/install": [('"/etc/hmodblacklist"', '"@R@/root/etc/hmodblacklist"'), ('copy "/bin/" "$bin/"', 'copy "@R@/root/bin/" "$bin/"'),
                              ('"/sbin/cryptsetup"', '"@R@/root/sbin/cryptsetup"'), ('"/key-file"', '"@R@/key-file"')],
    "hakchi/rootfs/etc/preinit.d/pffff_config": [('"/newroot"', '"@R@/newroot"'), ('mount -o remount "/"', 'mount -o remount "@R@/"')],
}
KEY = b"KEYDATA-ccs-test"

PREP_FAKE_SH = r'''F='@R@/fake'; cd "$F"
rm -f boot2-68 parttable.tuned; printf '070701' > ramdisk.cpio; cp boot2 boot2.orig
{ printf '\352\000\000\016uboot\000'; head -c 65526 /dev/urandom; } > boot1; cp boot1 boot1.factory   # the console's own u-boot (a u-boot header)
printf '%s' '@KEY@' > '@R@/key-file'
rm -rf '@R@/newroot/var'; mkdir -p '@R@/newroot/var/lib/clover/profiles/0/CLV-H-AAAAA'
head -c 8192 /dev/urandom > '@R@/newroot/var/lib/clover/profiles/0/CLV-H-AAAAA/cartridge.sav'
sha256sum '@R@/newroot/var/lib/clover/profiles/0/CLV-H-AAAAA/cartridge.sav' > save.sum
printf '%s\n' '@R@/newroot' > '@R@/mounted'
sha256sum boot0 boot1 boot2 phy @R@/dev/nand* | sort > prep.before; sha256sum boot0 phy @R@/dev/nand* | sort > fixed.before
echo ok
'''

def prep_overlay(R: str, by: dict, work: str):
    out = []; bad = []
    for name, pairs in PREP_MAPS.items():
        orig = by[name]["data"].decode(); t = orig
        for old, new in pairs:
            new = new.replace("@R@", R)
            if old not in orig or new in orig: bad.append(f"{name}: {old}")
            t = t.replace(old, new)
        back = t
        for old, new in pairs: back = back.replace(new.replace("@R@", R), old)
        if back != orig: bad.append(f"{name} does not map back")
        out.append({"name": name, "mode": by[name]["mode"], "data": t.encode()})
    tb = os.path.join(work, "prep-overlay.tar"); open(tb, "wb").write(tar_of(out))
    r = wsl(f"tar -xf '{wpath(tb)}' -C '{R}/root' && echo DONE")
    check(not bad and "DONE" in r.stdout, f"ccs-prepare: the test copies of ccsprep and hakchi's functions differ from the image's only in their RAM-root paths and PATH ({'; '.join(bad) or 'mapped back exactly'})")

def kernel_words(img: bytes, key: bytes) -> list[str]:
    """hakchi's insertKeyFile on img's command line: its hakchi-* words removed, this console's key added (b0050_boot)"""
    words = [w for w in img[64:576].replace(b"\0", b"").decode().split() if "hakchi-" not in w]
    return words + ["hakchi-key-file=base64:" + base64.b64encode(key).decode()]

def method_runs(R: str, step, A: str):
    """ccsprep's CCS Mod / CCS Nucleus steps on the stand-in console just prepared (hakchi's files on its data storage, a folder here)"""
    T = "@R@/newroot/var/lib/hakchi/transfer"
    mod, nuc, cho = B.METHOD_SHAS["ccs-mod"], B.METHOD_SHAS["ccs-nucleus"], B.METHOD_SHAS["ccs-nucleus-chooser"]
    lmod = hashlib.sha256(b"ccs-mod\n").hexdigest()
    def last(r):
        lines = r.stdout.strip().splitlines(); return next((l for l in reversed(lines) if l.startswith("result ")), lines[-1] if lines else "(nothing)")
    def mounted(): return sh_out(R, "cat '@R@/mounted'").split()
    r, w, t = step("mstate")
    check("hakchi=v1.0.4-126" in r.stdout and "registry=none" in r.stdout and "start=none" in r.stdout and "waiting=" not in r.stdout and "result mstate ok" in r.stdout and not w and not t and mounted() == [R + "/newroot"],
          f"ccs-prepare: mstate reads hakchi's version, its packages, CCS Nucleus's start (none yet, reported all the same) and the transfer folder with the storage mounted read-only, then unmounts it ({last(r)})")
    r, w, t = step("method ccs-nucleus " + mod, A)
    check("result method refused this image does not carry that package" in r.stdout and not w and not t, f"ccs-prepare: a package whose SHA-256 is not the image's own copy is refused before anything ({last(r)})")
    r, w, t = step("method ccs-other " + nuc, A)
    check("result method refused request" in r.stdout and not w, f"ccs-prepare: only CCS Mod's or CCS Nucleus's package ({last(r)})")
    r, w, t = step("method ccs-nucleus " + nuc)
    check("failed mount" in last(r) and any("nandc" in x for x in t) and sh_out(R, f"test -e '{T}' && echo THERE").strip() == "", f"ccs-prepare: without write permission the read-write mount is a tripwire and nothing is placed ({last(r)})")
    r, w, t = step("method ccs-nucleus " + nuc, A)
    same = sh_out(R, f"cmp -s '{T}/ccs-nucleus.hmod' '@R@/root/ccs/prep/methods/ccs-nucleus.hmod' && echo SAME").strip()
    check(f"result method ok {nuc}" in r.stdout and same == "SAME" and w == ["WRITE mount nandc rw"] and not t and mounted() == [R + "/newroot"],
          f"ccs-prepare: method places the image's CCS Nucleus package in hakchi's transfer folder (a new name, a rename), reads it back and unmounts ({last(r)}; {w})")
    r, w, t = step("method ccs-nucleus " + nuc, A)
    check(f"result method ok already {nuc}" in r.stdout and "began method" not in r.stdout, f"ccs-prepare: the same package already waiting is not written again ({last(r)})")
    r, w, t = step("method ccs-nucleus-chooser " + cho, A)
    same = sh_out(R, f"cmp -s '{T}/hmod/ccs-nucleus-chooser.hmod' '@R@/root/ccs/prep/methods/ccs-nucleus-chooser.hmod' && echo SAME; test -e '{T}/ccs-nucleus-chooser.hmod' && echo TOP").strip()
    check(f"result method ok {cho}" in r.stdout and same == "SAME" and not t, f"ccs-prepare: the startup chooser's package waits in the transfer folder's hmod/ folder, which hakchi installs after CCS Nucleus ({last(r)})")
    r, w, t = step("unlist ccs-mod", A)
    check(f"result unlist ok {lmod}" in r.stdout and sh_out(R, f"cat '{T}/uninstall'") == "ccs-mod\n" and not t, f"ccs-prepare: unlist writes hakchi's removal list naming ccs-mod ({last(r)})")
    r, w, t = step("unlist ccs-nucleus", A)
    check("result unlist refused another removal list already waits" in r.stdout and sh_out(R, f"cat '{T}/uninstall'") == "ccs-mod\n", f"ccs-prepare: a different removal list already waiting is never overwritten ({last(r)})")
    r, w, t = step("mstate")
    check(f"waiting=ccs-nucleus.hmod {nuc}" in r.stdout and f"waiting=hmod/ccs-nucleus-chooser.hmod {cho}" in r.stdout and f"waiting=uninstall {lmod}" in r.stdout and not w, f"ccs-prepare: mstate shows exactly what waits for the next start ({last(r)})")
    sh_out(R, f"rm -rf '{T}'")
    r, w, t = step("unlist ccs-nucleus-chooser ccs-nucleus ccs-mod", A)
    lall = hashlib.sha256(b"ccs-nucleus-chooser ccs-nucleus ccs-mod\n").hexdigest()
    check(f"result unlist ok {lall}" in r.stdout and sh_out(R, f"cat '{T}/uninstall'") == "ccs-nucleus-chooser ccs-nucleus ccs-mod\n" and not t, f"ccs-prepare: one removal list names up to three packages, in the order hakchi removes them ({last(r)})")
    r, w, t = step("unlist ccs-mod ccs-nucleus ccs-nucleus-chooser ccs-mod", A)
    check("result unlist refused request" in r.stdout and not w, f"ccs-prepare: never more than three names ({last(r)})")
    sh_out(R, f"rm -rf '{T}' && mkdir -p '@R@/elsewhere' && ln -s '@R@/elsewhere' '{T}'")
    r, w, t = step("method ccs-mod " + mod, A)
    check("result method refused a link is on the way" in r.stdout and sh_out(R, "ls '@R@/elsewhere'").strip() == "", f"ccs-prepare: a transfer folder that is a link is refused (nothing written through it) ({last(r)})")
    sh_out(R, f"rm -f '{T}'; rm -rf '@R@/elsewhere'")
    sh_out(R, "mv '@R@/newroot/var/lib/hakchi/rootfs/etc/preinit' '@R@/preinit.aside'")
    r, w, t = step("method ccs-mod " + mod, A)
    check("result method refused hakchi is not installed on the data storage" in r.stdout and sh_out(R, f"test -e '{T}' && echo THERE").strip() == "", f"ccs-prepare: without hakchi's files on the data storage nothing is placed ({last(r)})")
    sh_out(R, "mv '@R@/preinit.aside' '@R@/newroot/var/lib/hakchi/rootfs/etc/preinit'")
    sh_out(R, f"rm -rf '{T}'")
    # ---- what opens at power-on: CCS Nucleus's start setting, set by this image's own CCS Nucleus program (before hakchi installs it)
    reg = "@R@/newroot/var/lib/ccs/nucleus/registry"
    r, w, t = step("nstart Other never off default 10", A)
    check("result nstart refused request" in r.stdout and not w and not t, f"ccs-prepare: nstart takes only the listed words ({last(r)})")
    r, w, t = step("nstart other never off default 10", A)
    check("result nstart refused the default is not one of CCS Nucleus's choices" in r.stdout and not w and not t, f"ccs-prepare: another option as what opens first only when CCS Nucleus's list has it; nothing mounted read-write ({last(r)})")
    r, w, t = step("nstart native never off default 100", A)
    check("result nstart refused request" in r.stdout and not w, f"ccs-prepare: a countdown of at most two digits ({last(r)})")
    r, w, t = step("nstart native never off default 10")
    check("failed mount" in last(r) and any("nandc" in x for x in t) and sh_out(R, f"test -e '{reg}' && echo THERE").strip() == "", f"ccs-prepare: without write permission nstart writes nothing ({last(r)})")
    r, w, t = step("nstart ccs-home always on default 10", A)
    got = sh_out(R, f"head -n 1 '{reg}'; grep -c '^options=native ccs-home$' '{reg}'; ls '@R@/newroot/var/lib/hakchi/rootfs/etc/init.d/' 2>/dev/null | grep -c ccs")
    check("result nstart ok ccs-home always on default 10" in r.stdout and w == ["WRITE mount nandc rw"] and not t and got.split() == ["schema=ccs-boot-registry/1", "1", "0"],
          f"ccs-prepare: nstart writes CCS Nucleus's first start setting (CCS HOME offered and opened first, the chooser at start) in its own folder; nothing in hakchi's overlay ({last(r)}; {got.split()})")
    r, w, t = step("nstart ccs-home always on default 10", A)
    check("result nstart ok already ccs-home always on default 10" in r.stdout and "began nstart" not in r.stdout and not w, f"ccs-prepare: the same setting already there is not written again ({last(r)})")
    check(sh_out(R, f"grep '^opt.ccs-home.compat=' '{reg}'").strip() == "opt.ccs-home.compat=nes-usa",
          "ccs-prepare: CCS HOME's option records the console's own type and region, read from its system (an NES Classic of REGION EUR_USA: hakchi's nes-usa)")
    # the same steps on a Super NES Classic's system (its board dp-shvc, REGION USA, then EUR): the registry names that console
    for rg in ("USA", "EUR"):
        sh_out(R, f"echo dp-shvc > '@R@/newroot/etc/clover/boardtype'; echo {rg} > '@R@/newroot/etc/clover/REGION'")
        r1, w1, _ = step("nstart native never off default 10", A); r2, w2, t2 = step("nstart ccs-home never on default 10", A)
        got = sh_out(R, f"grep '^opt.ccs-home.compat=' '{reg}'").strip()
        check("result nstart ok native never off default 10" in r1.stdout and "result nstart ok ccs-home never on default 10" in r2.stdout and got == f"opt.ccs-home.compat=snes-{rg.lower()}" and not t2,
              f"ccs-prepare: on a Super NES Classic's system ({rg}) nstart offers CCS HOME again and its option records snes-{rg.lower()}, never the NES Classic's ({last(r2)}; {got})")
    sh_out(R, "echo dp-nes > '@R@/newroot/etc/clover/boardtype'; echo EUR_USA > '@R@/newroot/etc/clover/REGION'")
    r, w, t = step("nstart ccs-home always on default 10", A)
    r, w, t = step("nstart native never off last 0", A)
    got = sh_out(R, f"grep -c 'ccs-home' '{reg}'")
    check("result nstart ok native never off last 0" in r.stdout and got.strip() == "0", f"ccs-prepare: a changed setting is written and read back; CCS HOME no longer offered leaves no trace in the registry ({last(r)})")
    r, w, t = step("mstate")
    check("registry=ok" in r.stdout and "start=native never off last 0" in r.stdout and not w, f"ccs-prepare: mstate reads the setting back ({last(r)})")
    sh_out(R, "mkdir -p '@R@/elsewhere' && rm -rf '@R@/newroot/var/lib/ccs/nucleus' && ln -s '@R@/elsewhere' '@R@/newroot/var/lib/ccs/nucleus'")
    r, w, t = step("nstart native never off default 10", A)
    check("result nstart refused a link is on the way (ccs/nucleus)" in r.stdout and sh_out(R, "ls '@R@/elsewhere'").strip() == "" and not w, f"ccs-prepare: a link on the way to CCS Nucleus's folder is refused (nothing written through it) ({last(r)})")
    sh_out(R, "rm -f '@R@/newroot/var/lib/ccs/nucleus'; rm -rf '@R@/elsewhere' '@R@/newroot/var/lib/ccs'")
    # ---- an owner's system image as a CCS Nucleus choice: CCS Nucleus installed on the stand-in data storage first (the image's own package,
    # as hakchi's transfer_default copies it and its install script records it)
    H = "@R@/newroot/var/lib/hakchi/rootfs"
    r, w, t = step("rput owner-1 4096 " + "0" * 64, A)
    check("result rput refused CCS Nucleus is not installed" in r.stdout and not t, f"ccs-prepare: rput without CCS Nucleus installed is refused ({last(r)})")
    sh_out(R, f"cd '{H}' && tar -xzf '@R@/root/ccs/prep/methods/ccs-nucleus.hmod' && rm -f install uninstall readme.md && mkdir -p '@R@/newroot/var/lib/hakchi/hmod' && echo 'return 0' > '@R@/newroot/var/lib/hakchi/hmod/uninstall-ccs-nucleus'")
    pi = run(R, f"CCS_ROOT='@R@/newroot' sh {H}/bin/ccs-nucleus postinstall {H}/etc/ccs/nucleus/FILES".replace("@R@", R))
    check("result postinstall ok installed" in pi.stdout, f"ccs-prepare: (set-up) CCS Nucleus installed on the stand-in data storage ({last(pi)})")
    img = bytearray(4096); img[0:4] = b"hsqs"
    for i in range(96, 4096): img[i] = (i * 7) & 255
    ip = os.path.join(WORKDIR[0], "owner-root.hsqs"); open(ip, "wb").write(bytes(img)); ish = hashlib.sha256(bytes(img)).hexdigest()
    sh_out(R, f"cp '{wpath(ip)}' '@R@/owner.hsqs'"); lab = base64.b64encode(b"My Other System").decode()
    r, w, t = step(f"rput owner-1 4096 {'0' * 64} < '{R}/owner.hsqs'", A)
    check("result rput refused received-mismatch" in r.stdout and sh_out(R, "ls '@R@/newroot/var/lib/ccs/nucleus/staging/' 2>/dev/null").strip() == "", f"ccs-prepare: rput keeps nothing whose SHA-256 is not the reviewed one ({last(r)})")
    r, w, t = step(f"rput owner-1 4096 {ish} < '{R}/owner.hsqs'", A)
    check(f"result rput ok {ish}" in r.stdout and w == ["WRITE mount nandc rw"] and not t and sh_out(R, "cmp -s '@R@/owner.hsqs' '@R@/newroot/var/lib/ccs/nucleus/staging/owner-1.hsqs' && echo SAME").strip() == "SAME",
          f"ccs-prepare: rput receives the image straight to Nucleus's staging folder, checks it and reads it back ({last(r)}; {w})")
    r, w, t = step(f"rput owner-1 4096 {ish} < '{R}/owner.hsqs'", A)
    check(f"result rput ok already {ish}" in r.stdout and "began rput" not in r.stdout, f"ccs-prepare: the same image already there is not written again ({last(r)})")
    r, w, t = step(f"rimport owner-1 {ish} 4096 {lab}", A)
    reg = sh_out(R, "cat '@R@/newroot/var/lib/ccs/nucleus/registry'")
    check(f"result rimport ok owner-1 {ish}" in r.stdout and f"opt.owner-1.sha256={ish}" in reg and "opt.owner-1.label=My Other System" in reg and "opt.owner-1.kernel=3.4.113.29-madmonkey" in reg
          and sh_out(R, "cmp -s '@R@/owner.hsqs' '@R@/newroot/var/lib/ccs/nucleus/roots/owner-1.hsqs' && echo SAME").strip() == "SAME" and not t,
          f"ccs-prepare: rimport runs this image's own CCS Nucleus import: the image in Nucleus's own folder, a registry choice with its SHA-256, read back ({last(r)})")
    r, w, t = step("mstate")
    check(f"option=owner-1 owner-root {ish} 4096" in r.stdout and "registry=ok" in r.stdout and re.search(r"free=[1-9][0-9]*", r.stdout) and not w, f"ccs-prepare: mstate lists the registry's choices and the free space ({last(r)})")
    # the owner's own settings written back as they are: that image as what opens first (only with the chooser at start), a 20-second countdown
    r, w, t = step("nstart owner-1 always off default 20", A); rm_, _, _ = step("mstate")
    check("result nstart ok owner-1 always off default 20" in r.stdout and w == ["WRITE mount nandc rw"] and not t and "start=owner-1 always off default 20" in rm_.stdout
          and f"start_label={base64.b64encode(b'My Other System').decode()}" in rm_.stdout,
          f"ccs-prepare: nstart keeps the owner's own system image as what opens first, with its countdown; mstate names it ({last(r)}; {last(rm_)})")
    r1, w1, _ = step("nstart owner-1 never off default 20", A); r2, w2, _ = step("nstart owner-2 always off default 20", A)
    check("result nstart refused an owner's system image opens first only with the chooser at start" in r1.stdout and "result nstart refused the default is not one of CCS Nucleus's choices" in r2.stdout and not (w1 or w2),
          f"ccs-prepare: that image straight away without the chooser, or an option the list does not have, refused before anything is written ({last(r1)}; {last(r2)})")
    r, w, t = step("nstart native never off last 0", A)
    check("result nstart ok native never off last 0" in r.stdout, f"ccs-prepare: (set-up) Original Menu first again ({last(r)})")
    r, w, t = step(f"rdrop owner-1 {'0' * 64}", A)
    check("result rdrop refused the image is not CCS Nucleus's own unchanged file" in r.stdout and "opt.owner-1.kind" in sh_out(R, "cat '@R@/newroot/var/lib/ccs/nucleus/registry'"), f"ccs-prepare: rdrop with another SHA-256 changes nothing ({last(r)})")
    r, w, t = step(f"rdrop owner-1 {ish}", A)
    check("result rdrop ok owner-1" in r.stdout and "owner-1" not in sh_out(R, "cat '@R@/newroot/var/lib/ccs/nucleus/registry'") and sh_out(R, "test -e '@R@/newroot/var/lib/ccs/nucleus/roots/owner-1.hsqs' && echo THERE").strip() == ""
          and "roots/owner-1.hsqs" not in sh_out(R, "cat '@R@/newroot/var/lib/ccs/owned/nucleus.own'") and not t,
          f"ccs-prepare: rdrop removes the choice, then Nucleus's own copy of the image and its record ({last(r)})")
    # ---- a community menu as a choice in CCS Nucleus's startup chooser: this image's own CCS Nucleus program changes its list, nothing else
    MI = "cm-0123456789ab"; MP = "/var/lib/hakchi/rootfs/bin/fx_menu"; ml = base64.b64encode(b"Fixture Menu").decode()
    regm = lambda: sh_out(R, "cat '@R@/newroot/var/lib/ccs/nucleus/registry'")
    r1, w1, _ = step(f"nmadd {MI} ccs-mod {MP} {ml}", A); r2, w2, _ = step(f"nmadd {MI} fx_menu /etc/passwd {ml}", A); r3, w3, _ = step(f"nmadd native fx_menu {MP} {ml}", A)
    check(all("refused request" in last(x) for x in (r1, r2, r3)) and not (w1 or w2 or w3) and MI not in regm(),
          f"ccs-prepare: nmadd refuses a Classic Cart Studio package, a program outside hakchi's overlay or the console's own system's id before anything ({last(r1)}; {last(r2)}; {last(r3)})")
    r, w, t = step(f"nmadd {MI} fx_menu {MP} {ml}")
    check("failed mount" in last(r) and any("nandc" in x for x in t) and MI not in regm(), f"ccs-prepare: without write permission nmadd writes nothing ({last(r)})")
    r, w, t = step(f"nmadd {MI} fx_menu {MP} {ml}", A); rg = regm()
    check(f"result nmadd ok {MI}" in r.stdout and f"opt.{MI}.kind=community" in rg and f"opt.{MI}.package=fx_menu" in rg and f"opt.{MI}.program={MP}" in rg and f"opt.{MI}.label=Fixture Menu" in rg
          and w == ["WRITE mount nandc rw"] and not t and sh_out(R, "test -e '@R@/newroot/var/lib/hakchi/transfer' && echo THERE").strip() == "",
          f"ccs-prepare: nmadd adds the menu's choice to CCS Nucleus's list with this image's own CCS Nucleus program, reads it back; nothing in hakchi's transfer folder ({last(r)}; {w})")
    r, w, t = step("mstate")
    check(f"menu={MI} fx_menu {MP} {ml}" in r.stdout and f"option={MI} community" in r.stdout and "addon=ccs-nucleus current" in r.stdout and "addon=ccs-mod none" in r.stdout and "addon=ccs-nucleus-chooser none" in r.stdout and not w,
          f"ccs-prepare: mstate lists the menu's choice (package, program, label) and each add-on against this image's own: CCS Nucleus this image's, CCS Mod and the chooser not installed ({last(r)})")
    r, w, t = step(f"nmadd {MI} fx_menu {MP} {ml}", A)
    check(f"result nmadd ok already {MI}" in r.stdout and "began nmadd" not in r.stdout and not w, f"ccs-prepare: the same choice already there is not written again ({last(r)})")
    r, w, t = step(f"nmdrop {MI} other_pkg", A)
    check("result nmdrop refused the choice names another package" in r.stdout and not w and f"opt.{MI}.kind=community" in regm(), f"ccs-prepare: nmdrop never removes a choice naming another package ({last(r)})")
    sh_out(R, f"printf '%s\\n' '# an earlier version' >> '{H}/bin/ccs-nucleus'")
    rs, _, _ = step("mstate"); r, w, t = step(f"nmdrop {MI} fx_menu", A)
    check("addon=ccs-nucleus earlier" in rs.stdout and "result nmdrop refused CCS Nucleus on the console is an earlier version" in r.stdout and not w and f"opt.{MI}.kind=community" in regm(),
          f"ccs-prepare: with an earlier CCS Nucleus program on the console (and this version's not waiting), its list is never changed with this image's ({last(r)})")
    sh_out(R, f"printf 'version=0\\n' >> '{H}/etc/ccs/nucleus/VERSION'")
    rs2, _, _ = step("mstate")
    check("addon=ccs-nucleus earlier" in rs2.stdout, f"ccs-prepare: another package record (VERSION) of CCS Nucleus reads as an earlier version too ({last(rs2)})")
    # this version's CCS Nucleus waiting to be installed over the earlier one (the update in the same change): its list may be changed
    r0, w0, _ = step("method ccs-nucleus " + nuc, A)
    r, w, t = step(f"nmdrop {MI} fx_menu", A); rg = regm()
    sh_out(R, f"rm -rf '@R@/newroot/var/lib/hakchi/transfer'; cd '{H}' && tar -xzf '@R@/root/ccs/prep/methods/ccs-nucleus.hmod' ./bin/ccs-nucleus ./etc/ccs/nucleus/VERSION")
    rs3, _, _ = step("mstate")
    check(f"result method ok {nuc}" in r0.stdout and "addon=ccs-nucleus current" in rs3.stdout, f"ccs-prepare: (set-up) this version's CCS Nucleus placed for the update, then installed ({last(r0)}; {last(rs3)})")
    r2, w2, _ = step(f"nmdrop {MI} fx_menu", A)
    check(f"result nmdrop ok {MI}" in r.stdout and MI not in rg and "opt.native.kind=native" in rg and w == ["WRITE mount nandc rw"] and not t and f"result nmdrop ok already {MI}" in r2.stdout and not w2,
          f"ccs-prepare: nmdrop removes only that menu's choice, reads it back; a choice already gone is ok already ({last(r)}; {last(r2)})")
    sh_out(R, f"cd '{H}' && rm -rf bin/ccs-nucleus etc/ccs etc/preinit.d/p7060_ccsnucleus etc/preinit.d/p7071_ccsnucleus etc/init.d/S81ccs-nucleus; rm -rf '@R@/newroot/var/lib/ccs' '@R@/newroot/var/lib/hakchi/hmod/uninstall-ccs-nucleus' '@R@/newroot/var/lib/hakchi/games/000/CLV-H-CCSNU' '@R@/newroot/var/lib/hakchi/rootfs/usr/share/games/nes/kachikachi/CLV-H-CCSNU'")

def home_archive(tag: str, attempt: str, wrong_record: bool = False, names: dict | None = None) -> tuple[bytes, str, int, str]:
    """CCS HOME's transfer archive as Engine/HomePackage.cs builds it (pkg/<files>, rec/<records>), the records written the way
    ccs_install.py writes them (json.dumps sort_keys indent 1); (bytes, SHA-256, the package's bytes, the copy's name)"""
    files = {"bin/ccs-player-armv7": b"\x7fELF stand-in player " + tag.encode(), "session.json": b'{"schema": "ccs-play-session/1", "tag": "' + tag.encode() + b'"}\n',
             "config/controller-map.txt": b"key 304 a\n", "home/art/scene.bin": bytes((i * 7 + len(tag)) & 255 for i in range(40000))}
    man = (json.dumps({"schema": "ccs-console-package/1", "layout": "mod", "files": {r: {"size": len(b), "sha256": hashlib.sha256(b).hexdigest(), "mode": "0o755" if r.startswith("bin/") else "0o644"} for r, b in files.items()}}, indent=1) + "\n").encode()
    files["MANIFEST.json"] = man; msha = hashlib.sha256(man).hexdigest(); copy = msha[:16] + "." + attempt
    sums = {r: hashlib.sha256(b).hexdigest() for r, b in files.items()}
    if wrong_record: sums["session.json"] = "0" * 64
    rec = lambda o: (json.dumps(o, sort_keys=True, indent=1) + "\n").encode()
    recs = {f"v-{copy}.json": rec({"schema": "ccs-owned-copy/1", "copy": copy, "plan": "home-" + msha, "version": msha[:16], "attempt": attempt, "manifest_sha256": msha, "files": sums}),
            f"attempt-{attempt}.json": rec({"schema": "ccs-install-attempt/1", "attempt": attempt, "plan": "home-" + msha, "version": msha[:16], "copy": copy, "kind": "workbench", "device": {"hwid": "test"}, "started": "2026-11-15T00:00:00Z", "prior": {"pointer": None}}),
            "IDENTITY.json": rec({"schema": "ccs-console-data/1", "state_identity": {"platform": "linux-armv7", "core": "fceumm", "core_version": "ac0d00a7"}, "created": "2026-11-15T00:00:00Z", "note": "test"})}
    o = io.BytesIO()
    with tarfile.open(fileobj=o, mode="w", format=tarfile.USTAR_FORMAT) as tf:
        def add(name, data=None, mode=0o644):
            ti = tarfile.TarInfo((names or {}).get(name, name)); ti.mtime = B.MTIME; ti.uname = ti.gname = "root"; ti.mode = mode
            if data is None: ti.type = tarfile.DIRTYPE; ti.mode = 0o755; tf.addfile(ti)
            else: ti.size = len(data); tf.addfile(ti, io.BytesIO(data))
        for d in ("pkg/", "pkg/bin/", "pkg/config/", "pkg/home/", "pkg/home/art/"): add(d)
        for r, b in sorted(files.items()): add("pkg/" + r, b, 0o755 if r.startswith("bin/") else 0o644)
        add("rec/")
        for n, b in recs.items(): add("rec/" + n, b)
    b = o.getvalue(); return b, hashlib.sha256(b).hexdigest(), sum(len(x) for x in files.values()), copy

def home_runs(R: str, step, A: str):
    """ccsprep's CCS HOME steps (hput, huse) on the stand-in console just prepared; with --home-archive, the archive the shared engine itself
    built and sent (CcsBackup.Tests home group, CCS_HOME_CHAIN_OUT), whose result --export-data keeps for test_nucleus.py --only chain"""
    C = "@R@/newroot/var/lib/ccs"
    def last(r):
        lines = r.stdout.strip().splitlines(); return next((l for l in reversed(lines) if l.startswith("result ")), lines[-1] if lines else "(nothing)")
    def put(name, b): f = os.path.join(WORKDIR[0], name); open(f, "wb").write(b); sh_out(R, f"cp '{wpath(f)}' '@R@/{name}'")
    r, w, t = step("mstate")
    check("home_pointer=absent" in r.stdout and "home_identity=absent" in r.stdout and "home_copy=" not in r.stdout and re.search(r"^ram=[1-9][0-9]*$", r.stdout, re.M) and not w,
          f"ccs-prepare: mstate reports no CCS HOME yet and the RAM free for a transfer ({last(r)})")
    a1, s1, n1, c1 = home_archive("one", "0a1b2c3d"); put("h1.tar", a1)
    r, w, t = step(f"hput {c1} {len(a1)} {'0' * 64} {n1} < '{R}/h1.tar'", A)
    check("result hput refused received-mismatch" in r.stdout and not w and not t and sh_out(R, f"test -e '{C}' && echo THERE").strip() == "", f"ccs-prepare: hput keeps nothing whose SHA-256 is not the reviewed one; the data storage is not even mounted ({last(r)})")
    r, w, t = step(f"hput {c1} {len(a1)} {s1} {n1} < '{R}/h1.tar'")
    check("result hput failed mount" in last(r) and any("nandc" in x for x in t) and sh_out(R, f"test -e '{C}' && echo THERE").strip() == "", f"ccs-prepare: without write permission the read-write mount is a tripwire and nothing is placed ({last(r)})")
    ab, sb_, nb, cb = home_archive("bad", "0badbad0", names={"pkg/home/art/scene.bin": "pkg/home/../../escape.bin"}); put("hb.tar", ab)
    r, w, t = step(f"hput {cb} {len(ab)} {sb_} {nb} < '{R}/hb.tar'", A)
    check("result hput refused not a HOME package archive" in r.stdout and not w, f"ccs-prepare: an archive with a member outside its folders (\"..\") is refused before anything ({last(r)})")
    sh_out(R, f"mkdir -p '{C}' '@R@/elsewhere' && ln -s '@R@/elsewhere' '{C}/pkg'")
    r, w, t = step(f"hput {c1} {len(a1)} {s1} {n1} < '{R}/h1.tar'", A)
    check("result hput refused a link is on the way (ccs/pkg)" in r.stdout and sh_out(R, "ls '@R@/elsewhere'").strip() == "", f"ccs-prepare: a link on the way to the package folder is refused (nothing written through it) ({last(r)})")
    sh_out(R, f"rm -f '{C}/pkg'; rm -rf '@R@/elsewhere'")
    r, w, t = step(f"hput {c1} {len(a1)} {s1} {n1} < '{R}/h1.tar'", A)
    got = sh_out(R, f"cd '{C}/pkg/{c1}' 2>/dev/null && find . -type f | sort | xargs sha256sum; test -f '{C}/install/v-{c1}.json' && echo VREC; test -f '{C}/install/attempt-0a1b2c3d.json' && echo ATT; test -f '{C}/data/IDENTITY.json' && echo IDENT; test -e '{C}/staging' && echo STAGING; test -e '{C}/pkg/current' && echo POINTER")
    check(f"result hput ok {c1} {s1}" in r.stdout and w == ["WRITE mount nandc rw"] and not t and all(x in got for x in ("VREC", "ATT", "IDENT")) and "STAGING" not in got and "POINTER" not in got and got.count("./") == 5,
          f"ccs-prepare: hput receives the archive, writes the attempt's and the copy's records, unpacks the copy beside the HOME in use, reads every file back; the pointer is not touched ({last(r)}; {w})")
    r, w, t = step(f"hput {c1} {len(a1)} {s1} {n1} < '{R}/h1.tar'", A)
    check(f"result hput ok already {c1} {s1}" in r.stdout and "began hput" not in r.stdout, f"ccs-prepare: the same copy already complete is not written again ({last(r)})")
    r, w, t = step(f"huse {c1} 0123456789abcdef.01234567", A)
    check("result huse refused the HOME in use changed since the review (absent)" in r.stdout and not w, f"ccs-prepare: huse refuses when the pointer is not the reviewed one ({last(r)})")
    r, w, t = step(f"huse {c1} absent", A)
    ptr = sh_out(R, f"cat '{C}/pkg/current'; test -e '{C}/install/attempt-0a1b2c3d.json' && echo ATT")
    check(f"result huse ok {c1}" in r.stdout and ptr == c1 + "\n" and w == ["WRITE mount nandc rw"] and not t, f"ccs-prepare: huse switches the pointer in one rename and closes the attempt; read back ({last(r)})")
    r, w, t = step(f"huse {c1} absent", A)
    check(f"result huse ok already {c1}" in r.stdout and not w, f"ccs-prepare: the switch already done is not done again ({last(r)})")
    r, w, t = step("mstate")
    check(f"home_pointer={c1}" in r.stdout and re.search(rf"^home_copy={re.escape(c1)} {c1[:16]}[0-9a-f]{{48}} dir$", r.stdout, re.M) and "home_attempt=" not in r.stdout and re.search(r"^home_identity=[A-Za-z0-9+/=]{20,}$", r.stdout, re.M),
          f"ccs-prepare: mstate reads the copy in use, its record and the saves' identity ({last(r)})")
    # an earlier try of the same attempt stopped while unpacking (its records and a partial staging folder there): finished, not refused
    a2, s2, n2, c2 = home_archive("two", "1a2b3c4d"); put("h2.tar", a2)
    sh_out(R, f"mkdir -p '@R@/h2x' && cd '@R@/h2x' && tar -xf '@R@/h2.tar' rec && cp rec/attempt-1a2b3c4d.json rec/v-{c2}.json '{C}/install/' && mkdir -p '{C}/staging/{c2}/pkg/bin' && echo partial > '{C}/staging/{c2}/pkg/bin/half'")
    r, w, t = step(f"hput {c2} {len(a2)} {s2} {n2} < '{R}/h2.tar'", A)
    got = sh_out(R, f"test -e '{C}/staging' && echo STAGING; cat '{C}/pkg/current'; test -f '{C}/pkg/{c2}/bin/half' && echo HALF")
    check(f"result hput ok {c2} {s2}" in r.stdout and "STAGING" not in got and "HALF" not in got and c1 in got, f"ccs-prepare: an earlier try of the same attempt is finished: its staging folder replaced, the copy complete; the HOME in use still the earlier one ({last(r)})")
    r, w, t = step(f"huse {c2} {c1}", A)
    check(f"result huse ok {c2}" in r.stdout and sh_out(R, f"cat '{C}/pkg/current'") == c2 + "\n" and sh_out(R, f"test -d '{C}/pkg/{c1}' && echo KEPT").strip() == "KEPT", f"ccs-prepare: the update switched to; the earlier copy kept ({last(r)})")
    # the earlier versions: mstate's details of each copy, then hdrop's refusals and the removal of an earlier copy
    r, w, t = step("mstate")
    info = {m.group(1): m.group(0) for m in re.finditer(r"^home_copy_info=(\S+) .*$", r.stdout, re.M)}
    rx = r"^home_copy_info={c} (-|[0-9a-f]{{16}}\.[0-9a-f]{{8}}) exact [1-9][0-9]* [1-9][0-9]* \S+ \S+ (-|[0-9]{{4}}-[0-9]{{2}}-[0-9]{{2}}T[0-9]{{2}}:[0-9]{{2}}:[0-9]{{2}}Z) no$"
    check(set(info) == {c1, c2} and all(re.match(rx.format(c=re.escape(c)), info[c]) for c in (c1, c2)) and not w,
          f"ccs-prepare: mstate gives each recorded copy's details: the copy before it, exactly its record's files, their bytes and the space they take, its name, games, date, and that no entry of Original Menu names it ({'; '.join(info.values())})")
    r, w, t = step(f"hdrop {c2} {c1}", A)
    check(f"result hdrop refused the HOME in use is not {c1} ({c2})" in r.stdout and not w, f"ccs-prepare: hdrop never removes the copy in use ({last(r)})")
    r, w, t = step(f"hdrop {c1} {c1}", A)
    check("result hdrop refused request" in r.stdout and not w, f"ccs-prepare: a copy and the copy in use the same: refused ({last(r)})")
    D = f"@R@/newroot/var/lib/hakchi/games/nes-usa/000/CLV-Z-00001"
    sh_out(R, f"mkdir -p '{D}' && printf '[Desktop Entry]\\nExec=/bin/clover-kachikachi-wr /var/lib/ccs/pkg/{c1}/roms/x.nes\\n' > '{D}/CLV-Z-00001.desktop'")
    rm, _, _ = step("mstate")
    r, w, t = step(f"hdrop {c1} {c2}", A)
    check(f"result hdrop refused an entry of Original Menu may name the copy {c1} (yes; it is kept)" in r.stdout and not w and re.search(rf"^home_copy_info={re.escape(c1)} .* yes$", rm.stdout, re.M)
          and sh_out(R, f"test -d '{C}/pkg/{c1}' && echo KEPT").strip() == "KEPT", f"ccs-prepare: an earlier copy that an entry of Original Menu names is reported and kept ({last(r)})")
    sh_out(R, f"rm -rf '@R@/newroot/var/lib/hakchi/games'; mkdir -p '{C}/owned' && printf 'schema=ccs-ownership/1\\nmethod=published\\nfile={'a' * 64} 0644 /media/hakchi/games/nes-usa/000/CLV-Z-00001/CLV-Z-00001.desktop\\n' > '{C}/owned/published.own'")
    r, w, t = step(f"hdrop {c1} {c2}", A)
    check(f"result hdrop refused an entry of Original Menu may name the copy {c1} (unknown; it is kept)" in r.stdout and not w, f"ccs-prepare: with entries recorded on a USB drive or SD card (not readable now), no earlier copy is removed ({last(r)})")
    sh_out(R, f"rm -rf '{C}/owned'")
    # links in hakchi's folders are never walked. An entry behind a link to a folder this step does not read, a folder or entry that cannot
    # be read, and a link that leads nowhere keep the earlier copy (unknown: the listing is not complete); an entry that is a link is read
    # where it leads. Hakchi2 CE's own links (a game's media folder in its .storage, or in the console's read-only system) and a link to a
    # file hide nothing, and the removal below runs with them in place
    G = "@R@/newroot/var/lib/hakchi/games/nes-usa"
    named = f"[Desktop Entry]\\nExec=/bin/clover-kachikachi-wr /var/lib/ccs/pkg/{c1}/roms/x.nes\\n"
    def kept(label, setup, want, cleanup):
        sh_out(R, setup); rm, _, _ = step("mstate"); r, w, t = step(f"hdrop {c1} {c2}", A); sh_out(R, cleanup)
        check(f"result hdrop refused an entry of Original Menu may name the copy {c1} ({want}; it is kept)" in r.stdout and not w
              and re.search(rf"^home_copy_info={re.escape(c1)} .* {want}$", rm.stdout, re.M) and sh_out(R, f"test -d '{C}/pkg/{c1}' && echo KEPT").strip() == "KEPT",
              f"ccs-prepare: {label}: the earlier copy is kept ({want}) ({last(r)})")
    kept("an entry behind a link to a folder outside hakchi's folders, on the data storage",
         f"mkdir -p '@R@/newroot/var/lib/elsewhere' '{G}/000' && printf '{named}' > '@R@/newroot/var/lib/elsewhere/CLV-Z-00002.desktop' && ln -s /var/lib/elsewhere '{G}/000/CLV-Z-00002'",
         "unknown", f"rm -rf '{G}' '@R@/newroot/var/lib/elsewhere'")
    kept("an entry behind a link to a folder on a USB drive",
         f"mkdir -p '{G}/000' && ln -s /media/hakchi/games/nes-usa/000 '{G}/000/usb'", "unknown", f"rm -rf '{G}'")
    kept("a folder that cannot be read",
         f"mkdir -p '{G}/000/CLV-Z-00003' && printf '{named}' > '{G}/000/CLV-Z-00003/CLV-Z-00003.desktop' && chmod 000 '{G}/000/CLV-Z-00003'",
         "unknown", f"chmod 755 '{G}/000/CLV-Z-00003'; rm -rf '{G}'")
    kept("an entry that cannot be read",
         f"mkdir -p '{G}/000/CLV-Z-00004' && printf '[Desktop Entry]\\n' > '{G}/000/CLV-Z-00004/CLV-Z-00004.desktop' && chmod 000 '{G}/000/CLV-Z-00004/CLV-Z-00004.desktop'",
         "unknown", f"chmod 644 '{G}/000/CLV-Z-00004/CLV-Z-00004.desktop'; rm -rf '{G}'")
    kept("a link that leads nowhere",
         f"mkdir -p '{G}/000/CLV-Z-00005' && ln -s /var/lib/hakchi/games/nes-usa/.storage/CLV-Z-00005/autoplay '{G}/000/CLV-Z-00005/autoplay'", "unknown", f"rm -rf '{G}'")
    kept("a link in hakchi's overlay of the console's own games folder, to a folder this step does not read",
         "mkdir -p '@R@/newroot/var/lib/hakchi/rootfs/usr/share/games' && ln -s /media/hakchi/overlay '@R@/newroot/var/lib/hakchi/rootfs/usr/share/games/nes-linked'",
         "unknown", "rm -f '@R@/newroot/var/lib/hakchi/rootfs/usr/share/games/nes-linked'")
    kept("an entry that is a link, read where it leads",
         f"mkdir -p '@R@/newroot/var/lib/elsewhere' '{G}/000/CLV-Z-00006' && printf '{named}' > '@R@/newroot/var/lib/elsewhere/entry.desktop' && ln -s /var/lib/elsewhere/entry.desktop '{G}/000/CLV-Z-00006/CLV-Z-00006.desktop'",
         "yes", f"rm -rf '{G}' '@R@/newroot/var/lib/elsewhere'")
    sh_out(R, f"mkdir -p '{G}/.storage/CLV-Z-00007/autoplay' '{G}/000/CLV-Z-00007' '{G}/000/CLV-P-NAAAE' '@R@/newroot/usr/share/games/nes/kachikachi/CLV-P-NAAAE/autoplay'"
              f" && printf '[Desktop Entry]\\nExec=/bin/clover-kachikachi-wr /var/lib/hakchi/games/nes-usa/.storage/CLV-Z-00007/x.nes\\n' > '{G}/000/CLV-Z-00007/CLV-Z-00007.desktop'"
              f" && ln -s /var/lib/hakchi/games/nes-usa/.storage/CLV-Z-00007/autoplay '{G}/000/CLV-Z-00007/autoplay'"
              f" && ln -s /var/squashfs/usr/share/games/nes/kachikachi/CLV-P-NAAAE/autoplay '{G}/000/CLV-P-NAAAE/autoplay'"
              f" && printf 'x' > '{G}/.storage/CLV-Z-00007/x.nes' && ln -s ../../.storage/CLV-Z-00007/x.nes '{G}/000/CLV-Z-00007/x.nes'")
    rm, _, _ = step("mstate")
    check(re.search(rf"^home_copy_info={re.escape(c1)} .* no$", rm.stdout, re.M) and sh_out(R, "readlink '@R@/newroot/var/lib/hakchi/rootfs/usr/game/save'").strip() == "/var/lib",
          f"ccs-prepare: Hakchi2 CE's own links (a game's media folder in its .storage or in the console's read-only system), a link to a file and hakchi's links outside the menus' folders (its usr/game/save to the whole data storage) leave the scan complete: nothing names the copy")
    sh_out(R, f"cp '{C}/pkg/{c1}/session.json' '@R@/session.keep' && echo ' ' >> '{C}/pkg/{c1}/session.json'")
    r, w, t = step(f"hdrop {c1} {c2}", A)
    check(f"result hdrop refused the copy {c1} is not exactly what its record names (it is kept)" in r.stdout and not w and sh_out(R, f"test -f '{C}/install/v-{c1}.json' && echo REC").strip() == "REC",
          f"ccs-prepare: a copy that differs from its record is kept whole ({last(r)})")
    sh_out(R, f"cp '@R@/session.keep' '{C}/pkg/{c1}/session.json'")
    r, w, t = step(f"hdrop {c1} {c2}")
    check("result hdrop failed mount" in last(r) and any("nandc" in x for x in t) and sh_out(R, f"test -d '{C}/pkg/{c1}' && echo KEPT").strip() == "KEPT", f"ccs-prepare: without write permission the read-write mount is a tripwire and nothing is removed ({last(r)})")
    r, w, t = step(f"hdrop {c1} {c2}", A)
    got = sh_out(R, f"test -e '{C}/pkg/{c1}' && echo DIR; test -e '{C}/install/v-{c1}.json' && echo REC; cat '{C}/pkg/current'; cd '{C}/pkg/{c2}' && find . -type f | wc -l")
    check(f"result hdrop ok {c1}" in r.stdout and w == ["WRITE mount nandc rw"] and not t and "DIR" not in got and "REC" not in got and got.split()[:2] == [c2, "5"],
          f"ccs-prepare: hdrop removes the earlier copy's files, folders and record, reads back; the copy in use and its pointer untouched ({last(r)}; {w})")
    r, w, t = step(f"hdrop {c1} {c2}", A)
    check(f"result hdrop ok already {c1}" in r.stdout and not w, f"ccs-prepare: a copy already removed is not removed again ({last(r)})")
    # a copy that does not read back as its record: nothing switched; then another package is refused while that attempt is unfinished
    a3, s3, n3, c3 = home_archive("three", "2b3c4d5e", wrong_record=True); put("h3.tar", a3)
    r, w, t = step(f"hput {c3} {len(a3)} {s3} {n3} < '{R}/h3.tar'", A)
    check("result hput failed readback" in r.stdout and sh_out(R, f"cat '{C}/pkg/current'") == c2 + "\n", f"ccs-prepare: a copy whose files are not its record's: reported, nothing switched ({last(r)})")
    a4, s4, n4, c4 = home_archive("four", "3c4d5e6f"); put("h4.tar", a4)
    r, w, t = step(f"hput {c4} {len(a4)} {s4} {n4} < '{R}/h4.tar'", A)
    check("result hput refused an earlier installation of a HOME did not finish (attempt-2b3c4d5e.json)" in r.stdout and not w, f"ccs-prepare: with another installation unfinished, nothing is written ({last(r)})")
    r, w, t = step(f"huse {c3} {c2}", A)
    check(f"result huse refused the copy {c3} is not complete on the console" in r.stdout and not w, f"ccs-prepare: huse never switches to a copy that does not read back complete ({last(r)})")
    r, w, t = step(f"hdrop {c3} {c2}", A)
    check("result hdrop refused an installation did not finish (attempt-2b3c4d5e.json)" in r.stdout and not w, f"ccs-prepare: with an installation unfinished, no earlier copy is removed ({last(r)})")
    # the shared engine's own archive (the C# HomeArchive, sent through the engine's preparation step), for the chain test
    if HOME_ARCHIVE[0]:
        sh_out(R, f"rm -rf '{C}'")
        args = json.load(open(os.path.join(HOME_ARCHIVE[0], "home-archive.json"))); put("chain.tar", open(os.path.join(HOME_ARCHIVE[0], "home-archive.tar"), "rb").read())
        # the add-ons the engine's plan chose (its home-archive.json: addons, start), in the engine's own order: each package, CCS Nucleus's
        # start, then CCS HOME's archive
        ad = args.get("addons") or {"mod": True, "nucleus": "none"}; start = args.get("start")
        pk = [("ccs-mod", B.METHOD_SHAS["ccs-mod"])] if ad.get("mod") else []
        if ad.get("nucleus", "none") != "none": pk.append(("ccs-nucleus", B.METHOD_SHAS["ccs-nucleus"]))
        if ad.get("nucleus") == "chooser": pk.append(("ccs-nucleus-chooser", B.METHOD_SHAS["ccs-nucleus-chooser"]))
        placed = [(n, h, step(f"method {n} {h}", A)[0]) for n, h in pk]
        rs_ = step(f"nstart {start}", A)[0] if start else None
        r, w, t = step(f"hput {args['copy']} {args['size']} {args['sha256']} {args['total']} < '{R}/chain.tar'", A)
        r2, w2, t2 = step(f"huse {args['copy']} absent", A)
        files = sh_out(R, f"cd '{C}/pkg/{args['copy']}' && find . -type f | wc -l").strip()
        check(all(f"result method ok {h}" in x.stdout for n, h, x in placed) and (rs_ is None or f"result nstart ok {start}" in rs_.stdout)
              and f"result hput ok {args['copy']} {args['sha256']}" in r.stdout and f"result huse ok {args['copy']}" in r2.stdout and not t and not t2 and files == str(args["files"]),
              f"ccs-prepare: the engine's own archive ({args['layout']} layout, {args['size']} bytes), the add-ons its plan chose ({', '.join(n for n, h in pk)}) and CCS Nucleus's start ({start or 'none'}), placed by the image's own steps: {files} files checked, in use ({last(r)}; {last(r2)})")
        if EXPORT[0]:
            sh_out(R, f"cd '@R@/newroot/var/lib' && tar -cf '@R@/export.tar' ccs hakchi/transfer")
            out = sh_out(R, f"cp '@R@/export.tar' '{wpath(EXPORT[0])}' && echo COPIED")
            check("COPIED" in out, f"ccs-prepare: the data storage's CCS folder and hakchi's transfer folder kept for the chain test ({EXPORT[0]})")
        sh_out(R, f"rm -rf '@R@/newroot/var/lib/hakchi/transfer'")
    sh_out(R, f"rm -rf '{C}' '@R@/h2x'")

def prep_runs(R: str, by: dict, work: str):
    prep_overlay(R, by, work)
    fk = os.path.join(work, "fake-prep.sh"); open(fk, "w", newline="\n").write(PREP_FAKE_SH.replace("@R@", R).replace("@KEY@", KEY.decode()))
    check("ok" in wsl(f"bash '{wpath(fk)}'").stdout, "ccs-prepare: the stand-in console for the preparation runs (a stock console: original kernel, empty spare slot, a save on the data storage)")
    A = "CCSTEST_ALLOW_WRITE=1"; H = "@R@/newroot/var/lib/hakchi"; trips_all = []
    def step(s: str, env: str = ""):
        sh_out(R, "rm -f '@R@/log'")
        r = run(R, f"sh '{R}/root/ccs/bin/ccsprep' {s}; echo rc=$?", env)
        log = sh_out(R, "cat '@R@/log' 2>/dev/null").splitlines()
        trips = [l for l in log if l.startswith("TRIPWIRE")]; trips_all.extend(trips)
        return r, [l for l in log if l.startswith("WRITE")], trips
    def last(r):
        lines = r.stdout.strip().splitlines(); return next((l for l in reversed(lines) if l.startswith("result ")), lines[-1] if lines else "(nothing)")
    sh_out(R, "printf 'backup\\n' > '@R@/root/ccs/profile'")
    r, w, t = step("uboot", A)
    check("rc=13" in r.stdout and not w, f"ccs-prepare: in an image whose profile is not \"prepare\", ccsprep refuses before anything ({r.stdout.strip()})")
    sh_out(R, "printf 'prepare\\n' > '@R@/root/ccs/profile'")
    r, w, t = step("format", A)
    check("rc=2" in r.stdout and not w, "ccs-prepare: an unknown step is refused")
    r, w, t = step("kernel", A)
    check("result kernel failed the original kernel is not in the spare slot" in r.stdout and "rc=17" in r.stdout and not w and not t,
          f"ccs-prepare: hakchi's kernel is refused while the original kernel is not in the spare slot ({last(r)})")
    r, w, t = step("files")
    trips_all[:] = [x for x in trips_all if "nandc" not in x]   # the expected refusal below
    gone = sh_out(R, f"test -e '{H}' && echo THERE; cat '@R@/mounted'").split()
    check("result files failed mount" in r.stdout and not w and any("nandc" in x for x in t) and "THERE" not in gone and gone == [R + "/newroot"],
          f"ccs-prepare: when the data storage is not mounted, the files step stops and installs nothing (not into RAM either) ({last(r)})")
    files_checks = (r"test -f '@H@/rootfs/etc/preinit' && echo PREINIT; test -f '@H@/rootfs/bin/busybox' && echo BUSYBOX; "
                    r"cmp -s '@H@/rootfs/etc/key-file' '@R@/key-file' && echo KEY; cmp -s '@H@/var/version' '@R@/root/ccs/prep/hmod/var/version' && echo VERSION; "
                    r"grep -q '^cfg_' '@H@/rootfs/etc/preinit.d/p0000_config' && echo CONFIG; test -d '@H@/rootfs/lib/modules/3.4.113.29-madmonkey' && echo MODULES; "
                    r"cmp -s '@H@/rootfs/bin/sntool' '@R@/root/ccs/prep/hmod/bin/sntool' && echo HMODBIN; sha256sum -c '@R@/fake/save.sum' >/dev/null && echo SAVE; "
                    r"cd '@R@/fake' && sha256sum boot0 boot1 boot2 phy @R@/dev/nand* | sort | cmp -s - prep.before && echo STORAGE; cat '@R@/mounted'").replace("@H@", H)
    WANT = ["PREINIT", "BUSYBOX", "KEY", "VERSION", "CONFIG", "MODULES", "HMODBIN", "SAVE", "STORAGE", R + "/newroot"]
    r, w, t = step("files", A)
    got = sh_out(R, files_checks).split()
    check("result files ok" in r.stdout and "rc=0" in r.stdout and "installing hakchi" in r.stdout and w == ["WRITE mount nandc read-write", "WRITE sunxi-part tune"] and not t,
          f"ccs-prepare: the files step runs hakchi's own mod_install on the image's BusyBox: the data storage mounted once, tuned once by hakchi, nothing else written ({last(r)}; {w})")
    check(got == WANT, f"ccs-prepare: hakchi installed on the data storage (its rootfs and preinit, this console's key, the hmod's programs and modules, its version and settings), the console's saves and every other part unchanged, the storage unmounted afterwards ({' '.join(x for x in got if x not in WANT) or 'all'}; missing {[x for x in WANT if x not in got] or 'none'})")
    r, w, t = step("files", A)
    got = sh_out(R, files_checks).split()
    check("result files ok" in r.stdout and "installing hakchi" not in r.stdout and w == ["WRITE mount nandc read-write", "WRITE sunxi-part tune"] and not t and got == WANT,
          f"ccs-prepare: a second files step repairs (hakchi's mod_repair) and keeps the saves ({last(r)})")
    cp = B.cpio_build([B.entry("hakchi", 0o040755, b"", 1), B.entry("hakchi/init", 0o100755, b"#!/bin/sh\n", 2), B.entry("TRAILER!!!", 0, b"", 0)])
    cf = os.path.join(work, "hakchi-ramdisk.cpio"); open(cf, "wb").write(cp)
    wsl(f"cp '{wpath(cf)}' '{R}/fake/ramdisk.cpio'")
    r, w, t = step("slot68", A)
    check("result slot68 failed the running kernel is not the original" in r.stdout and not w and not t,
          f"ccs-prepare: the spare slot never receives a kernel that is not the original ({last(r)})")
    wsl(f"printf '070701' > '{R}/fake/ramdisk.cpio'")
    r, w, t = step("uboot", A + " CCSTEST_CORRUPT_BURN=1")
    check("result uboot failed burn-or-readback" in r.stdout and w == ["WRITE burn_boot1 8"] and "not burned again" in r.stderr,
          f"ccs-prepare: a u-boot burn whose readback differs is reported and NOT burned again (upstream flashBoot burns twice) ({last(r)}; {w})")
    ub = sh_out(R, "md5sum '@R@/root/ccs/prep/hmod/boot/uboot.bin' | cut -d' ' -f1").strip()
    r, w, t = step("uboot", A)
    same = sh_out(R, "cmp -s '@R@/fake/boot1' '@R@/root/ccs/prep/hmod/boot/uboot.bin' && echo SAME").strip()
    check(f"result uboot ok {ub}" in r.stdout and w == ["WRITE burn_boot1 8"] and same == "SAME" and not t,
          f"ccs-prepare: hakchi's u-boot (the pinned hmod's) burned once at its place (8) and read back ({last(r)})")
    r, w, t = step("slot68", A)
    same = sh_out(R, "cmp -s '@R@/fake/boot2-68' '@R@/fake/boot2.orig' && cmp -s '@R@/fake/boot2' '@R@/fake/boot2.orig' && echo SAME").strip()
    check("result slot68 ok " in r.stdout and w == ["WRITE burn_boot2 68"] and same == "SAME" and not t,
          f"ccs-prepare: the console's original kernel copied once to the spare slot (68) and read back; the running kernel unchanged ({last(r)})")
    r, w, t = step("slot68", A)
    check("result slot68 ok already" in r.stdout and not w, f"ccs-prepare: an original kernel already in the spare slot is not written again ({last(r)})")
    r, w, t = step("kernel", A)
    want_words = kernel_words(HMOD["boot/boot.img"][1], KEY)
    got_words = sh_out(R, "dd if='@R@/fake/boot2' bs=64 skip=1 count=8 2>/dev/null | tr -d '\\0'").split()
    outside = sh_out(R, "cmp -s <(head -c 64 '@R@/fake/boot2') <(head -c 64 '@R@/root/ccs/prep/hmod/boot/boot.img') && cmp -s <(tail -c +577 '@R@/fake/boot2') <(tail -c +577 '@R@/root/ccs/prep/hmod/boot/boot.img') && echo SAME; "
                     "cmp -s '@R@/fake/boot2-68' '@R@/fake/boot2.orig' && echo SLOT; md5sum '@R@/fake/boot2' | cut -d' ' -f1").split()
    check("result kernel ok " in r.stdout and w == ["WRITE burn_boot2 30"] and not t and outside[:2] == ["SAME", "SLOT"] and f"result kernel ok {outside[-1]}" in r.stdout,
          f"ccs-prepare: hakchi's kernel (the pinned hmod's) burned once at its place (30) and read back; the original stays in the spare slot ({last(r)})")
    check(got_words == want_words, f"ccs-prepare: the kernel's command line is hakchi's with this console's key added, as hakchi's insertKeyFile does ({' '.join(got_words)[:200]})")
    r, w, t = step("kernel", A + " CCSTEST_CORRUPT_BURN=1")
    check("result kernel failed burn-or-readback" in r.stdout and w == ["WRITE burn_boot2 30"] and "not burned again" in r.stderr,
          f"ccs-prepare: a kernel burn whose readback differs is reported and NOT burned again ({last(r)}; {w})")
    method_runs(R, step, A)
    home_runs(R, step, A)
    trips_all[:] = [x for x in trips_all if "-o rw,noatime" not in x]   # the expected refusals in method_runs and home_runs (no write permission)
    # ---- Return to Original Software: hakchi's uninstall as checked steps (the console as preparation left it: hakchi's u-boot and kernel,
    # the original kernel in the spare slot, the tuned table; its running ramdisk is hakchi's)
    wsl(f"cp '{wpath(cf)}' '{R}/fake/ramdisk.cpio'")
    md5 = lambda f: sh_out(R, f"md5sum '@R@/fake/{f}' | cut -d' ' -f1").strip()
    orig, hk_uboot = md5("boot2.orig"), md5("boot1")
    r, w, t = step("ret-kernel", A)
    check("result ret-kernel refused md5" in r.stdout and not w and not t, f"ccs-prepare: ret-kernel without the reviewed MD5 is refused ({last(r)})")
    r, w, t = step("ret-kernel " + "0" * 32, A)
    check("result ret-kernel refused the spare slot changed since the review" in r.stdout and not w and not t, f"ccs-prepare: ret-kernel refuses when the spare slot is not what the PC reviewed; nothing burned ({last(r)})")
    r, w, t = step("ret-kernel " + orig, A + " CCSTEST_CORRUPT_BURN=1")
    check("result ret-kernel failed burn-or-readback" in r.stdout and w == ["WRITE burn_boot2 30"] and "not burned again" in r.stderr, f"ccs-prepare: a ret-kernel burn whose readback differs is reported and NOT burned again ({last(r)}; {w})")
    r, w, t = step("ret-kernel " + orig, A)
    check(f"result ret-kernel ok {orig}" in r.stdout and w == ["WRITE burn_boot2 30"] and md5("boot2") == orig and md5("boot2-68") == orig and not t,
          f"ccs-prepare: ret-kernel burns the spare slot's original kernel once as the startup software and reads it back; the spare slot keeps its copy ({last(r)})")
    wsl(f"printf '070701' > '{R}/fake/ramdisk.cpio'")
    r, w, t = step("ret-kernel " + orig, A)
    check(f"result ret-kernel ok already {orig}" in r.stdout and not w, f"ccs-prepare: with the original kernel already running, ret-kernel burns nothing ({last(r)})")
    own = md5("boot1.factory"); size = sh_out(R, "wc -c < '@R@/fake/boot1.factory'").strip()
    r, w, t = step("ret-uboot " + hk_uboot + " " + own, A)
    check("result ret-uboot refused the staged bootloader is not the reviewed one" in r.stdout and not w and not t, f"ccs-prepare: ret-uboot without a staged bootloader is refused; nothing burned ({last(r)})")
    r, w, t = step(f"ret-stage {size} {'0' * 32} < '{R}/fake/boot1.factory'", A)
    check("result ret-stage refused received-mismatch" in r.stdout and not w and not t, f"ccs-prepare: ret-stage refuses bytes whose MD5 is not the reviewed one; nothing written ({last(r)})")
    sh_out(R, "printf 'NOT A U-BOOT' > '@R@/notuboot.bin'"); nm = sh_out(R, "md5sum '@R@/notuboot.bin' | cut -d' ' -f1").strip()
    r, w, t = step(f"ret-stage 12 {nm} < '{R}/notuboot.bin'", A)
    check("result ret-stage refused not a complete u-boot image" in r.stdout and not w and not t, f"ccs-prepare: ret-stage refuses what is not a complete u-boot image; nothing written ({last(r)})")
    r, w, t = step(f"ret-stage {size} {own} < '{R}/fake/boot1.factory'", A)
    check(f"result ret-stage ok {own} {size}" in r.stdout and not w and not t, f"ccs-prepare: ret-stage receives this console's own bootloader into RAM and checks it; nothing written ({last(r)})")
    r, w, t = step("ret-uboot " + "0" * 32 + " " + own, A)
    check("result ret-uboot refused the bootloader changed since the review" in r.stdout and not w and not t, f"ccs-prepare: ret-uboot refuses when the bootloader is not what the PC reviewed; nothing burned ({last(r)})")
    r, w, t = step("ret-uboot " + hk_uboot + " " + own, A + " CCSTEST_CORRUPT_BURN=1")
    check("result ret-uboot failed readback" in r.stdout and "not burned again" in r.stdout and w == ["WRITE ccsboot burn1"] and not t,
          f"ccs-prepare: a bootloader burn whose readback differs is reported and NOT burned again ({last(r)}; {w})")
    sh_out(R, "cp '@R@/root/ccs/prep/hmod/boot/uboot.bin' '@R@/fake/boot1'")   # back to the state preparation left, for the next run
    r, w, t = step("ret-uboot " + hk_uboot + " " + own, A)
    check(f"result ret-uboot ok {own}" in r.stdout and md5("boot1") == own and w == ["WRITE ccsboot burn1"] and not t,
          f"ccs-prepare: ret-uboot burns this console's own bootloader once (ccsboot, not the flash tool's default) and both readers read it back ({last(r)})")
    r, w, t = step("ret-files")
    trips_all[:] = [x for x in trips_all if "nandc" not in x]   # the expected refusal below
    there = sh_out(R, f"test -d '{H}' && echo THERE").strip()
    check("result ret-files failed mount" in r.stdout and not w and any("nandc" in x for x in t) and there == "THERE", f"ccs-prepare: ret-files without the data storage mounted removes nothing ({last(r)})")
    r, w, t = step("ret-files", A)
    left = sh_out(R, f"test -e '{H}' && echo THERE; sha256sum -c '@R@/fake/save.sum' >/dev/null && echo SAVE; cat '@R@/mounted'").split()
    check("result ret-files ok" in r.stdout and w == ["WRITE mount nandc read-write"] and left == ["SAVE", R + "/newroot"] and not t,
          f"ccs-prepare: ret-files removes hakchi's files (as Hakchi2 CE's uninstall), keeps the saves and unmounts the storage ({last(r)}; {left})")
    tuned = sh_out(R, "test -f '@R@/fake/parttable.tuned' && echo TUNED").strip()
    r, w, t = step("ret-table", A)
    untuned = sh_out(R, "test -f '@R@/fake/parttable.tuned' || echo GONE").strip()
    check(tuned == "TUNED" and "result ret-table ok" in r.stdout and "rc=0" in r.stdout and w == ["WRITE sunxi-part shrink"] and untuned == "GONE" and not t,
          f"ccs-prepare: ret-table runs hakchi's sunxi-part shrink once (the tuned table is gone; the PC reads the table back through identify) ({last(r)})")
    fixed = sh_out(R, "cd '@R@/fake' && sha256sum boot0 phy @R@/dev/nand* | sort | cmp -s - fixed.before && echo SAME").strip()
    check(fixed == "SAME" and not trips_all, f"ccs-prepare: the first-stage loader, the physical storage and every logical part were never written by any step; no other tripwire ({'; '.join(trips_all) or 'none'})")


# ---------------------------------------------------------------- the rescue image (ccsrescue): runs
RESCUE_FAKE_SH = r'''F='@R@/fake'; cd "$F"
{ printf '\352\000\000\016eGON.BT0'; head -c 24564 /dev/urandom; } > boot0; { printf '\352\000\000\016uboot\000'; head -c 65526 /dev/urandom; } > boot1
cp boot0 boot0.good; cp boot1 boot1.good; rm -f boot0.damaged boot1.damaged
sha256sum boot2 phy @R@/dev/nand* | sort > rescue.fixed
echo ok
'''

def rescue_runs(R: str, work: str):
    fk = os.path.join(work, "fake-rescue.sh"); open(fk, "w", newline="\n").write(RESCUE_FAKE_SH.replace("@R@", R))
    check("ok" in wsl(f"bash '{wpath(fk)}'").stdout, "ccs-rescue: the stand-in console for the rescue runs (valid boot0 and boot1)")
    A = "CCSTEST_ALLOW_WRITE=1"; trips_all = []
    def step(s: str, env: str = ""):
        sh_out(R, "rm -f '@R@/log'")
        r = run(R, f"sh '{R}/root/ccs/bin/ccsrescue' {s}; echo rc=$?", env)
        log = sh_out(R, "cat '@R@/log' 2>/dev/null").splitlines()
        trips = [l for l in log if l.startswith("TRIPWIRE")]; trips_all.extend(trips)
        return r, [l for l in log if l.startswith("WRITE")], trips
    def last(r):
        lines = r.stdout.strip().splitlines(); return next((l for l in reversed(lines) if l.startswith("result ")), lines[-1] if lines else "(nothing)")
    md5 = lambda f: sh_out(R, f"md5sum '@R@/fake/{f}' | cut -d' ' -f1").strip()
    size = lambda f: sh_out(R, f"wc -c < '@R@/fake/{f}'").strip()
    sh_out(R, "printf 'backup\\n' > '@R@/root/ccs/profile'")
    r, w, t = step("diag", A)
    check("rc=13" in r.stdout and not w, f"ccs-rescue: in an image whose profile is not \"rescue\", ccsrescue refuses before anything ({r.stdout.strip()})")
    sh_out(R, "printf 'rescue\\n' > '@R@/root/ccs/profile'")
    r, w, t = step("diag")
    check(f"boot0={md5('boot0')}" in r.stdout and f"boot1={md5('boot1')}" in r.stdout and f"boot1_flash_tool={md5('boot1')}" in r.stdout and "result diag ok" in r.stdout and not w and not t,
          f"ccs-rescue: diag reads both stages with both readers and writes nothing ({last(r)})")
    sh_out(R, FLIP_SH + "; flip '@R@/fake/boot1' 500; touch '@R@/fake/boot1.damaged'")
    r, w, t = step("diag")
    check("boot1=invalid" in r.stdout and "boot1_flash_tool=damaged" in r.stdout and not w, f"ccs-rescue: a damaged boot1 is reported as invalid by ccsboot and damaged by the flash tool ({r.stdout.strip()[:120]})")
    good1, n1 = md5("boot1.good"), size("boot1.good")
    r, w, t = step(f"stage1 {n1} {'0' * 32} < '{R}/fake/boot1.good'")
    check("result stage1 refused received-mismatch" in r.stdout and not w and not t, f"ccs-rescue: stage1 refuses bytes whose MD5 is not the reviewed one ({last(r)})")
    r, w, t = step(f"stage1 {size('boot0.good')} {md5('boot0.good')} < '{R}/fake/boot0.good'")
    check("result stage1 refused not a complete boot1 image" in r.stdout and not w, f"ccs-rescue: a boot0 image is refused as boot1 ({last(r)})")
    r, w, t = step(f"burn1 invalid {good1}", A)
    check("result burn1 refused the staged boot1 is not the reviewed one" in r.stdout and not w and not t, f"ccs-rescue: burn1 without the staged bytes is refused ({last(r)})")
    r, w, t = step(f"stage1 {n1} {good1} < '{R}/fake/boot1.good'")
    check(f"result stage1 ok {good1} {n1}" in r.stdout and not w and not t, f"ccs-rescue: stage1 receives this console's own boot1 into RAM and checks it; nothing written ({last(r)})")
    r, w, t = step(f"burn1 {good1} {good1}", A)
    check("result burn1 refused boot1 changed since the review" in r.stdout and not w and not t, f"ccs-rescue: burn1 refuses when boot1 is not in the reviewed state ({last(r)})")
    r, w, t = step(f"burn1 invalid {good1}", A + " CCSTEST_CORRUPT_BURN=1")
    check("result burn1 failed readback" in r.stdout and "not burned again" in r.stdout and w == ["WRITE ccsboot burn1"] and not t, f"ccs-rescue: a boot1 write whose readback differs is reported and NOT written again ({last(r)}; {w})")
    sh_out(R, FLIP_SH + "; flip '@R@/fake/boot1' 500; touch '@R@/fake/boot1.damaged'")
    r, w, t = step(f"burn1 invalid {good1}")
    check("TRIPWIRE" in " ".join(t) or (not w), f"ccs-rescue: without write permission in the test stand-in, burn1 cannot write ({last(r)})")
    trips_all[:] = [x for x in trips_all if "burn1" not in x]
    sh_out(R, FLIP_SH + "; flip '@R@/fake/boot1' 500; touch '@R@/fake/boot1.damaged'")
    r, w, t = step(f"stage1 {n1} {good1} < '{R}/fake/boot1.good'")
    r, w, t = step(f"burn1 invalid {good1}", A + " CCSTEST_READ_FAILS_AT_BLOCK=1")
    check("result burn1 failed burn stopped 1 (not burned again)" in r.stdout and w == ["WRITE ccsboot burn1 stopped 1"] and not t,
          f"ccs-rescue: ccsboot stopped at a block it could not read before writing it: the result says how many blocks it wrote, nothing retried ({last(r)})")
    sh_out(R, FLIP_SH + "; cp '@R@/fake/boot1.good' '@R@/fake/boot1'; flip '@R@/fake/boot1' 500; touch '@R@/fake/boot1.damaged'")
    r, w, t = step(f"burn1 invalid {good1}", A)
    check(f"result burn1 ok {good1}" in r.stdout and md5("boot1") == good1 and w == ["WRITE ccsboot burn1"] and not t, f"ccs-rescue: burn1 writes this console's own boot1 once and both readers read it back ({last(r)})")
    # typed states: unreadable is written only by the explicit unreadable mode, only when reviewed as unreadable; nodriver never
    sh_out(R, "touch '@R@/fake/boot1.unreadable'")
    r, w, t = step("diag")
    check("boot1=unreadable" in r.stdout and "boot1_flash_tool=damaged" in r.stdout and not w, f"ccs-rescue: a bootloader that cannot be read is diagnosed as unreadable ({r.stdout.strip()[:120]})")
    r, w, t = step(f"stage1 {n1} {good1} < '{R}/fake/boot1.good'")
    r, w, t = step(f"burn1 invalid {good1}", A)
    check("result burn1 refused boot1 changed since the review" in r.stdout and not w, f"ccs-rescue: an unreadable stage is not written under a review that called it invalid ({last(r)})")
    r, w, t = step(f"burn1 unreadable {good1}", A)
    check(f"result burn1 ok {good1}" in r.stdout and w == ["WRITE ccsboot burn1-unreadable"] and md5("boot1") == good1 and not t, f"ccs-rescue: reviewed as unreadable, it is written once by burn1-unreadable and read back by both readers ({last(r)}; {w})")
    sh_out(R, "touch '@R@/fake/driver.fails'")
    r, w, t = step("diag")
    check("boot1=nodriver" in r.stdout and "boot0=nodriver" in r.stdout and not w, f"ccs-rescue: when the driver does not answer, the stages are reported as nodriver, not damaged ({r.stdout.strip()[:120]})")
    r, w, t = step(f"stage1 {n1} {good1} < '{R}/fake/boot1.good'")
    r, w, t = step(f"burn1 nodriver {good1}", A)
    check("result burn1 refused md5" in r.stdout and not w and not t, f"ccs-rescue: nothing is ever written over a state of nodriver ({last(r)})")
    sh_out(R, "rm -f '@R@/fake/driver.fails'")
    good0, n0 = md5("boot0.good"), size("boot0.good")
    r, w, t = step(f"stage0 {n0} {good0} < '{R}/fake/boot0.good'")
    check(f"result stage0 ok {good0} {n0}" in r.stdout and not w, f"ccs-rescue: stage0 receives a boot0 image into RAM and checks it ({last(r)})")
    r, w, t = step(f"burn0 {good0} {good0}", A)
    check(f"result burn0 ok {good0}" in r.stdout and w == ["WRITE ccsboot burn0"] and md5("boot0") == good0 and not t, f"ccs-rescue: burn0 writes the staged boot0 once through the boot0 writer and reads it back ({last(r)})")
    fixed = sh_out(R, "cd '@R@/fake' && sha256sum boot2 phy @R@/dev/nand* | sort | cmp -s - rescue.fixed && echo SAME").strip()
    check(fixed == "SAME" and not trips_all, f"ccs-rescue: the kernel, the physical storage and every logical part were never written; no other tripwire ({'; '.join(trips_all) or 'none'})")


# ---------------------------------------------------------------- the files image (ccsfiles): runs (the folder newroot/var/lib stands in for the data storage)
FILES_FAKE_SH = r'''L='@R@/newroot/var/lib'; rm -rf "$L"; mkdir -p "$L/clover/profiles/0/CLV-P-NAAAE" "$L/hakchi/rootfs/etc" "$L/hakchi/games"
head -c 8192 /dev/urandom > "$L/clover/profiles/0/CLV-P-NAAAE/save.sav"; printf 'x' > "$L/hakchi/rootfs/etc/motd"
ln -s ../../hakchi/rootfs "$L/clover/profiles/linked"; : > '@R@/mounted'
echo ok
'''

def files_runs(R: str, work: str):
    fk = os.path.join(work, "fake-files.sh"); open(fk, "w", newline="\n").write(FILES_FAKE_SH.replace("@R@", R))
    check("ok" in wsl(f"bash '{wpath(fk)}'").stdout, "ccs-files: the stand-in data storage for the file runs")
    A = "CCSTEST_ALLOW_WRITE=1"; L = R + "/newroot/var/lib"; trips_all = []
    def step(s: str, env: str = ""):
        sh_out(R, "rm -f '@R@/log'")
        r = run(R, f"sh '{R}/root/ccs/bin/ccsfiles' {s}; echo rc=$?", env)
        log = sh_out(R, "cat '@R@/log' 2>/dev/null").splitlines()
        trips = [l for l in log if l.startswith("TRIPWIRE")]; trips_all.extend(trips)
        return r, [l for l in log if l.startswith("WRITE")], trips
    def last(r):
        lines = r.stdout.strip().splitlines(); return next((l for l in reversed(lines) if l.startswith("result ")), lines[-1] if lines else "(nothing)")
    sha = lambda p: sh_out(R, f"sha256sum '{L}/{p}' | cut -d' ' -f1").strip()
    sh_out(R, "printf 'backup\\n' > '@R@/root/ccs/profile'")
    r, w, t = step("stat user clover/profiles/0/CLV-P-NAAAE/save.sav")
    check("rc=13" in r.stdout and not w, f"ccs-files: in an image whose profile is not \"files\", ccsfiles refuses before anything ({r.stdout.strip()})")
    sh_out(R, "printf 'files\\n' > '@R@/root/ccs/profile'")
    old = sha("clover/profiles/0/CLV-P-NAAAE/save.sav")
    r, w, t = step("stat user clover/profiles/0/CLV-P-NAAAE/save.sav")
    mounted = sh_out(R, "cat '@R@/mounted'").strip()
    check(f"result stat ok {old} 8192" in r.stdout and not w and not t and mounted == "", f"ccs-files: stat reads a file's state with the storage mounted read-only (no journal replay), then unmounted ({last(r)})")
    for bad in ("../etc/passwd", "/clover/x", "clover/./x", "hakchi/rootfs/etc/motd"):
        r, w, t = step(f"stat user '{bad}'")
        check("refused" in last(r) and not w, f"ccs-files: user scope refuses {bad!r} ({last(r)})")
    sh_out(R, "printf 'NEWSAVE' > '@R@/new.sav'"); nsha = sh_out(R, "sha256sum '@R@/new.sav' | cut -d' ' -f1").strip()
    r, w, t = step(f"put user clover/profiles/0/CLV-P-NAAAE/save.sav 7 {nsha} {'0' * 64} < '{R}/new.sav'", A)
    check("refused the file changed since the review" in last(r) and sha("clover/profiles/0/CLV-P-NAAAE/save.sav") == old and not t, f"ccs-files: put refuses when the file is not the reviewed one; nothing written ({last(r)})")
    r, w, t = step(f"put user clover/profiles/0/CLV-P-NAAAE/save.sav 7 {nsha} {old}", A)
    check("refused received-mismatch" in last(r) and sha("clover/profiles/0/CLV-P-NAAAE/save.sav") == old, f"ccs-files: put refuses bytes that do not arrive exactly ({last(r)})")
    r, w, t = step(f"put user clover/profiles/linked/etc/motd 7 {nsha} absent < '{R}/new.sav'", A)
    check("refused a link on the way" in last(r) and sh_out(R, f"cat '{L}/hakchi/rootfs/etc/motd'") == "x", f"ccs-files: a link on the way is refused (no escape into another folder) ({last(r)})")
    r, w, t = step(f"put user clover/profiles/0/CLV-P-NAAAE/save.sav 7 {nsha} {old} < '{R}/new.sav'", A)
    left = sh_out(R, f"cat '@R@/mounted'; ls '{L}/clover/profiles/0/CLV-P-NAAAE/' | grep -c ccs-new").split()
    check(f"result put ok {nsha}" in r.stdout and sha("clover/profiles/0/CLV-P-NAAAE/save.sav") == nsha and w == ["WRITE mount nandc rw"] and left == ["0"] and not t,
          f"ccs-files: put replaces the save through a new name and a rename, reads it back and unmounts the storage ({last(r)}; {w})")
    r, w, t = step(f"del user clover/profiles/0/CLV-P-NAAAE/save.sav {nsha}", A)
    gone = sh_out(R, f"test -e '{L}/clover/profiles/0/CLV-P-NAAAE/save.sav' || echo GONE").strip()
    check("result del ok absent" in r.stdout and gone == "GONE" and w == ["WRITE mount nandc rw"], f"ccs-files: del removes the file and checks it is gone ({last(r)})")
    r, w, t = step(f"put dev hakchi/rootfs/etc/motd 7 {nsha} {sha('hakchi/rootfs/etc/motd')} < '{R}/new.sav'", A)
    check(f"result put ok {nsha}" in r.stdout and sha("hakchi/rootfs/etc/motd") == nsha, f"ccs-files: dev scope reaches hakchi's system overlay ({last(r)})")
    # ccsdev: Developer Mode's startup-software write
    sh_out(R, "{ printf 'ANDROID!'; head -c 100000 /dev/urandom; } > '@R@/newk.img'; printf 'NOTANIMAGE' > '@R@/bad.img'")
    km = sh_out(R, "md5sum '@R@/newk.img' | cut -d' ' -f1").strip(); kn = sh_out(R, "wc -c < '@R@/newk.img'").strip()
    bm = sh_out(R, "md5sum '@R@/bad.img' | cut -d' ' -f1").strip(); now = sh_out(R, "md5sum '@R@/fake/boot2' | cut -d' ' -f1").strip()
    def dev(s, env=""):
        sh_out(R, "rm -f '@R@/log'"); r = run(R, f"sh '{R}/root/ccs/bin/ccsdev' {s}; echo rc=$?", env)
        log = sh_out(R, "cat '@R@/log' 2>/dev/null").splitlines(); return r, [l for l in log if l.startswith("WRITE")], [l for l in log if l.startswith("TRIPWIRE")]
    r, w, t = dev(f"kstage 10 {bm} < '{R}/bad.img'")
    check("refused not a valid startup image" in last(r) and not w, f"ccs-files: ccsdev kstage refuses what is not a startup image ({last(r)})")
    r, w, t = dev(f"kstage {kn} {km} < '{R}/newk.img'")
    check(f"result kstage ok {km} {kn}" in r.stdout and not w and not t, f"ccs-files: ccsdev kstage keeps the checked image in RAM and writes nothing ({last(r)})")
    r, w, t = dev(f"kburn {'0' * 32} {km}", A)
    check("refused the startup software changed since the review" in last(r) and not w, f"ccs-files: ccsdev kburn refuses when the startup software is not the reviewed one ({last(r)})")
    r, w, t = dev(f"kburn {now} {km}", A)
    same = sh_out(R, "cmp -s '@R@/fake/boot2' '@R@/newk.img' && echo SAME").strip()
    check(f"result kburn ok {km}" in r.stdout and w == ["WRITE burn_boot2 30"] and same == "SAME" and not t, f"ccs-files: ccsdev kburn writes the startup slot once and reads it back ({last(r)}; {w})")
    check(not trips_all, f"ccs-files: no tripwire in any step ({'; '.join(trips_all) or 'none'})")
    dev_system_runs(R, work, last)


# ---------------------------------------------------------------- the files image (ccsdev): Developer Mode's system part
DEV_SYS_SH = r'''R='@R@'; set -e
mk(){ d="$R/sq-$1"; rm -rf "$d"; mkdir -p "$d/etc/clover" "$d/sbin"; printf '%s\n' "$2" > "$d/etc/clover/boardtype"; printf 'init %s\n' "$1" > "$d/sbin/init"
      head -c 200000 /dev/urandom > "$d/big"; /usr/bin/mksquashfs "$d" "$R/$1.hsqs" -comp $3 -noappend -all-root -no-xattrs -quiet > /dev/null; }
mk a dp-nes gzip; mk b dp-nes gzip; mk x dp-nes xz; cat "$R/b.hsqs" > "$R/odd.hsqs"; printf 'tail' >> "$R/odd.hsqs"
# the system part (1 MiB) holding a, stored through the stand-in cipher (+1 per byte); the read-only mapping the image's /init opened
head -c 1048576 /dev/zero > "$R/plain.part"; dd if="$R/a.hsqs" of="$R/plain.part" conv=notrunc 2> /dev/null
LC_ALL=C tr '\000-\377' '\001-\377\000' < "$R/plain.part" > "$R/dev/nandb"; LC_ALL=C tr '\001-\377\000' '\000-\377' < "$R/dev/nandb" > "$R/dev/mapper/root-crypt"
printf 'KEY' > "$R/key-file"; printf 'MemTotal: 250000 kB\nMemFree: 180000 kB\n' > "$R/proc/meminfo"; echo "$R/newroot" >> "$R/mounted"
echo ok
'''

def dev_system_runs(R: str, work: str, last):
    fk = os.path.join(work, "dev-system.sh"); open(fk, "w", newline="\n").write(DEV_SYS_SH.replace("@R@", R))
    check("ok" in wsl(f"bash '{wpath(fk)}'").stdout, "ccs-files: the stand-in system part (an encrypted SquashFS) and the images for Developer Mode's system runs")
    M = "CCSTEST_MAPPED=1"; A = M + " CCSTEST_ALLOW_WRITE=1"; trips_all = []
    def dev(s, env=M):
        sh_out(R, "rm -f '@R@/log'"); r = run(R, f"sh '{R}/root/ccs/bin/ccsdev' {s}; echo rc=$?", env)
        log = sh_out(R, "cat '@R@/log' 2>/dev/null").splitlines(); trips = [l for l in log if l.startswith("TRIPWIRE")]; trips_all.extend(trips)
        return r, [l for l in log if l.startswith("WRITE")], trips, log
    h = lambda p, algo="sha256": sh_out(R, f"{algo}sum '{R}/{p}' | cut -d' ' -f1").strip()
    n = lambda p: sh_out(R, f"wc -c < '{R}/{p}'").strip()
    plain_prefix = lambda size: sh_out(R, f"LC_ALL=C tr '\\001-\\377\\000' '\\000-\\377' < '{R}/dev/nandb' | head -c {size} | sha256sum | cut -d' ' -f1").strip()
    used_a = sh_out(R, f"od -An -t u4 -j 40 -N 4 '{R}/a.hsqs'").strip()
    r, w, t, _ = dev("sinfo")
    lines = set(r.stdout.split())
    check({"system=crypt", "key=yes", "part_bytes=1048576", "comp=1", f"bytes_used={used_a}", "board=dp-nes", "mem_free=184320000", "result", "sinfo", "ok"} <= lines and not w and not t,
          f"ccs-files: ccsdev sinfo reports the system part as the console's startup opens it, reading only ({' '.join(sorted(lines))})")
    sh_out(R, "printf 'backup\\n' > '@R@/root/ccs/profile'"); r, w, t, _ = dev("sinfo"); sh_out(R, "printf 'files\\n' > '@R@/root/ccs/profile'")
    check("rc=13" in r.stdout and not w, "ccs-files: in an image whose profile is not \"files\", ccsdev refuses before anything")
    bs, bn = h("b.hsqs"), n("b.hsqs")
    for what, args, src, expect in (("bytes that do not arrive as reviewed", f"sstage {bn} {'0' * 64}", "b.hsqs", "refused received-mismatch"), ("an xz system", f"sstage {n('x.hsqs')} {h('x.hsqs')}", "x.hsqs", "refused its compression is not the current system's"),
                                    ("a size that is not whole 4,096-byte blocks", f"sstage {n('odd.hsqs')} {h('odd.hsqs')}", "odd.hsqs", "refused not a whole number")):
        r, w, t, _ = dev(args + f" < '{R}/{src}'")
        check(expect in last(r) and not w and not t, f"ccs-files: ccsdev sstage refuses {what}, writing nothing ({last(r)})")
    r, w, t, _ = dev(f"sstage {bn} {bs} < '{R}/b.hsqs'")
    check(f"result sstage ok {bs} {bn}" in r.stdout and not w and not t, f"ccs-files: ccsdev sstage keeps the checked system image in RAM and writes nothing ({last(r)})")
    raw0 = h("dev/nandb", "md5")
    r, w, t, _ = dev(f"sburn {'0' * 32} {bs}", A)
    check("refused the system part changed since the review" in last(r) and not w and h("dev/nandb", "md5") == raw0, f"ccs-files: ccsdev sburn refuses when the system part is not the reviewed one; nothing written ({last(r)})")
    r, w, t, _ = dev(f"sburn {raw0} {bs}", M)
    check("refused the system part could not be opened for writing" in last(r) and t and h("dev/nandb", "md5") == raw0 and not any(l.startswith("WRITE dd") for l in w), f"ccs-files: without write permission the read-write mapping is a tripwire and nothing is written ({last(r)})")
    trips_all.clear()
    r, w, t, log = dev(f"sburn {raw0} {bs}", A)
    raw1 = h("dev/nandb", "md5")
    check(f"result sburn ok {bs} {raw1}" in r.stdout and w == ["WRITE cryptsetup open root-crypt read-write", f"WRITE dd of={R}/dev/mapper/root-crypt"] and plain_prefix(bn) == bs and not t,
          f"ccs-files: ccsdev sburn writes the staged system once through the console's mapping, reads it back and reports the part's new bytes ({last(r)}; {w})")
    rest = sh_out(R, f"LC_ALL=C tr '\\001-\\377\\000' '\\000-\\377' < '{R}/dev/nandb' | tail -c +{int(bn) + 1} | tr -d '\\000' | wc -c").strip()
    check(rest == "0" and any("--readonly" in l and l.startswith("cryptsetup open") for l in log) and any(l.startswith("mount -o ro,noatime") and "root-crypt" in l for l in log),
          "ccs-files: the rest of the system part is left as it was, and the system is opened and mounted read-only again afterwards")
    r, w, t, _ = dev(f"sburn {raw1} {bs}", A)
    check("refused the staged system is not the reviewed one" in last(r) and not w, f"ccs-files: the staged image is gone after its one write; it is never written a second time ({last(r)})")
    as_, an = h("a.hsqs"), n("a.hsqs"); dev(f"sstage {an} {as_} < '{R}/a.hsqs'")
    r, w, t, _ = dev(f"sburn {raw1} {as_}", A + " CCSTEST_CORRUPT_BURN=1")
    check("failed readback" in last(r) and "(not written again)" in last(r) and sum(1 for l in w if l.startswith("WRITE dd")) == 1, f"ccs-files: a write that reads back differently is reported and not written again ({last(r)})")
    raw2 = h("dev/nandb", "md5"); sh_out(R, "rm -f '@R@/key-file'"); dev(f"sstage {an} {as_} < '{R}/a.hsqs'")
    r, w, t, _ = dev("sinfo"); r2, w2, t2, _ = dev(f"sburn {raw2} {as_}", A)
    check("key=no" in r.stdout and "refused the console's key is not available" in last(r2) and not w2 and h("dev/nandb", "md5") == raw2, f"ccs-files: without the console's key nothing is written ({last(r2)})")
    # a plain (unencrypted) system part: written directly, no mapping
    sh_out(R, "cp '@R@/plain.part' '@R@/dev/nandb'; printf 'KEY' > '@R@/key-file'")
    r, w, t, _ = dev("sinfo"); check("system=plain" in r.stdout, "ccs-files: a plain SquashFS system part is reported as plain")
    dev(f"sstage {bn} {bs} < '{R}/b.hsqs'"); rawp = h("dev/nandb", "md5")
    r, w, t, _ = dev(f"sburn {rawp} {bs}", A)
    check("result sburn ok" in last(r) and w == [f"WRITE dd of={R}/dev/nandb"] and sh_out(R, f"head -c {bn} '{R}/dev/nandb' | sha256sum | cut -d' ' -f1").strip() == bs, f"ccs-files: a plain system part is written directly, once, and read back ({last(r)}; {w})")
    check(not trips_all, f"ccs-files: no tripwire in the system-part runs with write permission ({'; '.join(trips_all) or 'none'})")


# ---------------------------------------------------------------- the same programs on a console shaped like a Super NES Classic
# The console programs never read the console's model: its board and region reach the PC through ccsbr identify, and hakchi's own scripts
# tell the models apart at the console's normal start (softwareCheck). These runs give the image's own programs a Super NES Classic's
# shape: board dp-shvc, REGION USA or EUR, its release, a synthetic system key (never a console's), its stock games' folders under
# /usr/share/games (made-up stand-ins), a save under clover/profiles, boot0 of 32,768 bytes and its own u-boot of 573,440 bytes without a
# nandinfo block (the sizes a Super NES Classic's backup holds). The flash tools are the same logging stand-ins: what is shown is the
# programs' control flow and the bytes they hand to those tools, not the console's NAND or its flash tool's own behavior.
SNES_KEY = b"SNESKEY-synthetic-not-a-console-key"
SNES_SHAPE_SH = r'''R='@R@'; F="$R/fake"
echo dp-shvc > "$R/newroot/etc/clover/boardtype"; echo @REGION@ > "$R/newroot/etc/clover/REGION"; echo VERSION=release-v2.0.14-0-gd8b65c6 > "$R/newroot/etc/clover/VERSION"
rm -rf "$R/newroot/usr/share/games"
for g in CLV-P-SAAAE CLV-P-SADKE CLV-P-SABCE; do mkdir -p "$R/newroot/usr/share/games/$g"; printf '[Desktop Entry]\nName=%s\n' "$g" > "$R/newroot/usr/share/games/$g/$g.desktop"; head -c 4096 /dev/urandom > "$R/newroot/usr/share/games/$g/$g.img"; done
(cd "$R/newroot/usr/share/games" && find . -type f | sort | xargs sha256sum) > "$F/games.sum"
{ printf '\352\000\000\016eGON.BT0'; head -c 32756 /dev/urandom; } > "$F/boot0"; { printf '\352\000\000\016uboot\000'; head -c 573430 /dev/urandom; } > "$F/boot1"
cp "$F/boot0" "$F/boot0.good"; cp "$F/boot1" "$F/boot1.good"; cp "$F/boot1" "$F/boot1.factory"; rm -f "$F/boot0.damaged" "$F/boot1.damaged"
printf '%s' '@KEY@' > "$R/key-file"
echo ok
'''
SNES_PREP_SH = r'''R='@R@'; F="$R/fake"; cd "$F"
rm -f boot2-68 parttable.tuned; printf '070701' > ramdisk.cpio; cp boot2 boot2.orig
rm -rf "$R/newroot/var"; mkdir -p "$R/newroot/var/lib/clover/profiles/0/CLV-P-SAAAE"
head -c 8192 /dev/urandom > "$R/newroot/var/lib/clover/profiles/0/CLV-P-SAAAE/cartridge.sav"
sha256sum "$R/newroot/var/lib/clover/profiles/0/CLV-P-SAAAE/cartridge.sav" > save.sum
printf '%s\n' "$R/newroot" > "$R/mounted"
sha256sum boot0 phy "$R"/dev/nand* | sort > fixed.before
echo ok
'''

def snes_shape(R: str, work: str, region: str, name: str):
    fk = os.path.join(work, f"fake-{name}.sh"); open(fk, "w", newline="\n").write(FAKE_SH.replace("@R@", R)); wsl(f"bash '{wpath(fk)}'")
    sh = os.path.join(work, f"snes-shape-{name}.sh"); open(sh, "w", newline="\n").write(SNES_SHAPE_SH.replace("@R@", R).replace("@REGION@", region).replace("@KEY@", SNES_KEY.decode()))
    return "ok" in wsl(f"bash '{wpath(sh)}'").stdout

def snes_identity_runs(variant: str, R: str):
    """ccsbr identify reports the shape's board and region (the PC decides the model from them), and hakchi 3.9.3's own softwareCheck
    (b0010_functions, sourced unchanged) gives this system the type, region and game folder its normal start uses"""
    for region, want in (("USA", "usa"), ("EUR", "eur"), ("JPN", "jpn")):
        sh_out(R, f"echo {region} > '@R@/newroot/etc/clover/REGION'")
        kv = dict(l.split("=", 1) for l in ccsbr(R, "identify").stdout.splitlines() if "=" in l)
        hk = run(R, f"modname=hakchi; mountpoint='{R}/newroot'; . '{R}/root/hakchi/rootfs/etc/preinit.d/b0000_defines'; mountpoint='{R}/newroot'; squashfs='{R}/newroot'; "
                    f". '{R}/root/hakchi/rootfs/etc/preinit.d/b0010_functions'; softwareCheck; echo \"sftype=$sftype sfregion=$sfregion gamepath=$gamepath\"").stdout.strip().splitlines()
        got = hk[-1] if hk else "(nothing)"
        check(kv.get("board") == "dp-shvc" and kv.get("region") == region and kv.get("oem_version") == "release-v2.0.14-0-gd8b65c6" and got == f"sftype=snes sfregion={want} gamepath=/usr/share/games",
              f"{variant} (SNES shape, {region}): ccsbr identify reports board dp-shvc and region {region}; hakchi's own softwareCheck gives \"{got}\" (the PC refuses JPN, the Super Famicom's region)")
    sh_out(R, "echo USA > '@R@/newroot/etc/clover/REGION'")

def snes_prep_runs(R: str, by: dict, work: str):
    """ccsprep's preparation and return steps on a console shaped like a North American Super NES Classic"""
    prep_overlay(R, by, work)
    ok = snes_shape(R, work, "USA", "snes-prep")
    fk = os.path.join(work, "snes-prep.sh"); open(fk, "w", newline="\n").write(SNES_PREP_SH.replace("@R@", R))
    check(ok and "ok" in wsl(f"bash '{wpath(fk)}'").stdout, "ccs-prepare (SNES shape): the stand-in console (board dp-shvc, its original startup software, an empty spare slot, its own 573,440-byte u-boot, a save, three stock game folders)")
    snes_identity_runs("ccs-prepare", R)
    A = "CCSTEST_ALLOW_WRITE=1"; H = "@R@/newroot/var/lib/hakchi"; trips_all = []
    def step(s: str, env: str = ""):
        sh_out(R, "rm -f '@R@/log'")
        r = run(R, f"sh '{R}/root/ccs/bin/ccsprep' {s}; echo rc=$?", env)
        log = sh_out(R, "cat '@R@/log' 2>/dev/null").splitlines()
        trips = [l for l in log if l.startswith("TRIPWIRE")]; trips_all.extend(trips)
        return r, [l for l in log if l.startswith("WRITE")], trips
    def last(r):
        lines = r.stdout.strip().splitlines(); return next((l for l in reversed(lines) if l.startswith("result ")), lines[-1] if lines else "(nothing)")
    md5 = lambda f: sh_out(R, f"md5sum '@R@/fake/{f}' | cut -d' ' -f1").strip()
    kept = lambda: sh_out(R, "sha256sum -c '@R@/fake/save.sum' >/dev/null && echo SAVE; cd '@R@/newroot/usr/share/games' && sha256sum -c --status '@R@/fake/games.sum' && echo GAMES").split()
    r, w, t = step("kernel", A)
    check("result kernel failed the original kernel is not in the spare slot" in r.stdout and not w and not t, f"ccs-prepare (SNES shape): hakchi's kernel is refused while the console's original kernel is not in the spare slot ({last(r)})")
    r, w, t = step("files", A)
    got = sh_out(R, (r"test -f '@H@/rootfs/etc/preinit' && echo PREINIT; cmp -s '@H@/rootfs/etc/key-file' '@R@/key-file' && echo KEY; "
                     r"cmp -s '@H@/var/version' '@R@/root/ccs/prep/hmod/var/version' && echo VERSION; cat '@R@/mounted'").replace("@H@", H)).split()
    check("result files ok" in r.stdout and "installing hakchi" in r.stdout and w == ["WRITE mount nandc read-write", "WRITE sunxi-part tune"] and not t and got == ["PREINIT", "KEY", "VERSION", R + "/newroot"] and kept() == ["SAVE", "GAMES"],
          f"ccs-prepare (SNES shape): hakchi's own mod_install installs hakchi with this console's key on the data storage (mounted once, tuned once by hakchi, unmounted); the save and the stock game folders unchanged ({last(r)}; {w}; {got})")
    r, w, t = step("uboot", A)
    same = sh_out(R, "cmp -s '@R@/fake/boot1' '@R@/root/ccs/prep/hmod/boot/uboot.bin' && echo SAME").strip()
    check("result uboot ok" in r.stdout and w == ["WRITE burn_boot1 8"] and same == "SAME" and not t, f"ccs-prepare (SNES shape): hakchi's u-boot (the pinned hmod's, the same for both models) burned once and read back ({last(r)})")
    r, w, t = step("slot68", A)
    same = sh_out(R, "cmp -s '@R@/fake/boot2-68' '@R@/fake/boot2.orig' && echo SAME").strip()
    check("result slot68 ok " in r.stdout and w == ["WRITE burn_boot2 68"] and same == "SAME" and not t, f"ccs-prepare (SNES shape): this console's own original startup software copied once to the spare slot ({last(r)})")
    r, w, t = step("kernel", A)
    got_words = sh_out(R, "dd if='@R@/fake/boot2' bs=64 skip=1 count=8 2>/dev/null | tr -d '\\0'").split()
    check("result kernel ok " in r.stdout and w == ["WRITE burn_boot2 30"] and not t and got_words == kernel_words(HMOD["boot/boot.img"][1], SNES_KEY),
          f"ccs-prepare (SNES shape): hakchi's kernel burned once with THIS console's key added to its command line by hakchi's insertKeyFile (the key read in the console's RAM from its own startup software; never sent by the PC) ({last(r)})")
    # the way back, on the same shape: the console's own 573,440-byte u-boot staged and written by ccsboot (never the flash tool's default)
    cp = B.cpio_build([B.entry("hakchi", 0o040755, b"", 1), B.entry("hakchi/init", 0o100755, b"#!/bin/sh\n", 2), B.entry("TRAILER!!!", 0, b"", 0)])
    cf = os.path.join(work, "snes-hakchi-ramdisk.cpio"); open(cf, "wb").write(cp); wsl(f"cp '{wpath(cf)}' '{R}/fake/ramdisk.cpio'")
    orig, hk_uboot, own = md5("boot2.orig"), md5("boot1"), md5("boot1.factory"); size = sh_out(R, "wc -c < '@R@/fake/boot1.factory'").strip()
    r, w, t = step("ret-kernel " + orig, A)
    check(f"result ret-kernel ok {orig}" in r.stdout and w == ["WRITE burn_boot2 30"] and md5("boot2") == orig and not t, f"ccs-prepare (SNES shape): ret-kernel puts the console's own original startup software back once from the spare slot ({last(r)})")
    wsl(f"printf '070701' > '{R}/fake/ramdisk.cpio'")
    r, w, t = step(f"ret-stage {size} {own} < '{R}/fake/boot1.factory'", A)
    check(size == "573440" and f"result ret-stage ok {own} {size}" in r.stdout and not w and not t, f"ccs-prepare (SNES shape): ret-stage receives this console's own 573,440-byte u-boot into RAM and checks it; nothing written ({last(r)})")
    r, w, t = step("ret-uboot " + hk_uboot + " " + own, A)
    check(f"result ret-uboot ok {own}" in r.stdout and md5("boot1") == own and w == ["WRITE ccsboot burn1"] and not t, f"ccs-prepare (SNES shape): ret-uboot writes this console's own u-boot once with ccsboot and both readers read it back ({last(r)})")
    r, w, t = step("ret-files", A)
    left = sh_out(R, f"test -e '{H}' && echo THERE; cat '@R@/mounted'").split()
    check("result ret-files ok" in r.stdout and w == ["WRITE mount nandc read-write"] and left == [R + "/newroot"] and kept() == ["SAVE", "GAMES"] and not t,
          f"ccs-prepare (SNES shape): ret-files removes hakchi's files and keeps the save and the stock game folders ({last(r)})")
    r, w, t = step("ret-table", A)
    check("result ret-table ok" in r.stdout and w == ["WRITE sunxi-part shrink"] and not t, f"ccs-prepare (SNES shape): ret-table runs hakchi's shrink once (the PC reads the table back) ({last(r)})")
    fixed = sh_out(R, "cd '@R@/fake' && sha256sum boot0 phy @R@/dev/nand* | sort | cmp -s - fixed.before && echo SAME").strip()
    check(fixed == "SAME" and not trips_all, f"ccs-prepare (SNES shape): boot0, the physical storage and every logical part were never written; no tripwire ({'; '.join(trips_all) or 'none'})")

def snes_rescue_runs(R: str, work: str):
    """ccsrescue on a console shaped like a Super NES Classic: its own 573,440-byte u-boot and its 32,768-byte boot0"""
    check(snes_shape(R, work, "EUR", "snes-rescue"), "ccs-rescue (SNES shape): the stand-in console (board dp-shvc, REGION EUR, boot0 32,768 bytes, u-boot 573,440 bytes)")
    snes_identity_runs("ccs-rescue", R)
    fixed0 = sh_out(R, "cd '@R@/fake' && sha256sum boot2 phy @R@/dev/nand* | sort")
    A = "CCSTEST_ALLOW_WRITE=1"; trips_all = []
    def step(s: str, env: str = ""):
        sh_out(R, "rm -f '@R@/log'")
        r = run(R, f"sh '{R}/root/ccs/bin/ccsrescue' {s}; echo rc=$?", env)
        log = sh_out(R, "cat '@R@/log' 2>/dev/null").splitlines()
        trips = [l for l in log if l.startswith("TRIPWIRE")]; trips_all.extend(trips)
        return r, [l for l in log if l.startswith("WRITE")], trips
    def last(r):
        lines = r.stdout.strip().splitlines(); return next((l for l in reversed(lines) if l.startswith("result ")), lines[-1] if lines else "(nothing)")
    md5 = lambda f: sh_out(R, f"md5sum '@R@/fake/{f}' | cut -d' ' -f1").strip()
    size = lambda f: sh_out(R, f"wc -c < '@R@/fake/{f}'").strip()
    sh_out(R, "printf 'rescue\\n' > '@R@/root/ccs/profile'")
    good0, good1, n0, n1 = md5("boot0.good"), md5("boot1.good"), size("boot0.good"), size("boot1.good")
    r, w, t = step("diag")
    check(f"boot0={good0}" in r.stdout and f"boot1={good1}" in r.stdout and f"boot1_size={n1}" in r.stdout and n1 == "573440" and n0 == "32768" and not w, f"ccs-rescue (SNES shape): diag reads both stages, the u-boot 573,440 bytes ({last(r)})")
    sh_out(R, FLIP_SH + "; flip '@R@/fake/boot1' 300000; touch '@R@/fake/boot1.damaged'; flip '@R@/fake/boot0' 2000; touch '@R@/fake/boot0.damaged'")
    r, w, t = step("diag")
    check("boot1=invalid" in r.stdout and "boot0=invalid" in r.stdout and not w, f"ccs-rescue (SNES shape): both damaged stages diagnosed ({r.stdout.strip()[:160]})")
    r, w, t = step(f"stage1 {n1} {good1} < '{R}/fake/boot1.good'"); check(f"result stage1 ok {good1} {n1}" in r.stdout and not w, f"ccs-rescue (SNES shape): this console's own u-boot staged in RAM and checked ({last(r)})")
    r, w, t = step(f"burn1 invalid {good1}", A)
    check(f"result burn1 ok {good1}" in r.stdout and w == ["WRITE ccsboot burn1"] and md5("boot1") == good1 and not t, f"ccs-rescue (SNES shape): written once and read back by both readers ({last(r)})")
    r, w, t = step(f"stage0 {n0} {good0} < '{R}/fake/boot0.good'"); check(f"result stage0 ok {good0} {n0}" in r.stdout and not w, f"ccs-rescue (SNES shape): this console's own boot0 staged ({last(r)})")
    r, w, t = step(f"burn0 invalid {good0}", A)
    check(f"result burn0 ok {good0}" in r.stdout and w == ["WRITE ccsboot burn0"] and md5("boot0") == good0 and not t, f"ccs-rescue (SNES shape): boot0 written once through the boot0 writer and read back ({last(r)})")
    fixed = sh_out(R, "cd '@R@/fake' && sha256sum boot2 phy @R@/dev/nand* | sort")
    check(fixed == fixed0 and not trips_all, f"ccs-rescue (SNES shape): the startup software, the physical storage and every logical part were never written; no tripwire ({'; '.join(trips_all) or 'none'})")

SNES_DEV_SH = r'''R='@R@'; set -e
mk(){ d="$R/sq-$1"; rm -rf "$d"; mkdir -p "$d/etc/clover" "$d/sbin"; printf '%s\n' "$2" > "$d/etc/clover/boardtype"; printf 'init %s\n' "$1" > "$d/sbin/init"
      head -c 200000 /dev/urandom > "$d/big"; /usr/bin/mksquashfs "$d" "$R/$1.hsqs" -comp gzip -noappend -all-root -no-xattrs -quiet > /dev/null; }
mk sa dp-shvc; mk sb dp-shvc
head -c 1048576 /dev/zero > "$R/plain.part"; dd if="$R/sa.hsqs" of="$R/plain.part" conv=notrunc 2> /dev/null
LC_ALL=C tr '\000-\377' '\001-\377\000' < "$R/plain.part" > "$R/dev/nandb"; LC_ALL=C tr '\001-\377\000' '\000-\377' < "$R/dev/nandb" > "$R/dev/mapper/root-crypt"
printf 'MemTotal: 250000 kB\nMemFree: 180000 kB\n' > "$R/proc/meminfo"; echo "$R/newroot" >> "$R/mounted"
echo ok
'''

def snes_dev_runs(R: str, work: str):
    """ccsdev on a console shaped like a Super NES Classic: its board reported, its system part written through its own mapping with its key"""
    ok = snes_shape(R, work, "USA", "snes-dev")
    fk = os.path.join(work, "snes-dev.sh"); open(fk, "w", newline="\n").write(SNES_DEV_SH.replace("@R@", R))
    check(ok and "ok" in wsl(f"bash '{wpath(fk)}'").stdout, "ccs-files (SNES shape): the stand-in system part (an encrypted SquashFS of board dp-shvc) and a second image")
    sh_out(R, "printf 'files\\n' > '@R@/root/ccs/profile'")
    M = "CCSTEST_MAPPED=1"; A = M + " CCSTEST_ALLOW_WRITE=1"; trips_all = []
    def dev(s, env=M):
        sh_out(R, "rm -f '@R@/log'"); r = run(R, f"sh '{R}/root/ccs/bin/ccsdev' {s}; echo rc=$?", env)
        log = sh_out(R, "cat '@R@/log' 2>/dev/null").splitlines(); trips = [l for l in log if l.startswith("TRIPWIRE")]; trips_all.extend(trips)
        return r, [l for l in log if l.startswith("WRITE")], trips
    def last(r):
        lines = r.stdout.strip().splitlines(); return next((l for l in reversed(lines) if l.startswith("result ")), lines[-1] if lines else "(nothing)")
    h = lambda p, algo="sha256": sh_out(R, f"{algo}sum '{R}/{p}' | cut -d' ' -f1").strip()
    n = lambda p: sh_out(R, f"wc -c < '{R}/{p}'").strip()
    r, w, t = dev("sinfo")
    lines = set(r.stdout.split())
    check({"system=crypt", "key=yes", "comp=1", "board=dp-shvc"} <= lines and not w and not t, f"ccs-files (SNES shape): ccsdev sinfo reports board dp-shvc and an encrypted system part whose key this session holds, reading only ({' '.join(sorted(lines))})")
    bs, bn = h("sb.hsqs"), n("sb.hsqs")
    r, w, t = dev(f"sstage {bn} {bs} < '{R}/sb.hsqs'")
    check(f"result sstage ok {bs} {bn}" in r.stdout and not w, f"ccs-files (SNES shape): the system image staged in RAM and checked ({last(r)})")
    raw0 = h("dev/nandb", "md5")
    r, w, t = dev(f"sburn {raw0} {bs}", A)
    plain = sh_out(R, f"LC_ALL=C tr '\\001-\\377\\000' '\\000-\\377' < '{R}/dev/nandb' | head -c {bn} | sha256sum | cut -d' ' -f1").strip()
    check("result sburn ok" in last(r) and w == ["WRITE cryptsetup open root-crypt read-write", f"WRITE dd of={R}/dev/mapper/root-crypt"] and plain == bs and not t,
          f"ccs-files (SNES shape): ccsdev sburn writes the system part once through the console's own mapping with its key and reads it back ({last(r)}; {w})")
    sh_out(R, "{ printf 'ANDROID!'; head -c 100000 /dev/urandom; } > '@R@/newk.img'")
    km, kn, now = h("newk.img", "md5"), n("newk.img"), h("fake/boot2", "md5")
    r, w, t = dev(f"kstage {kn} {km} < '{R}/newk.img'", "")
    r, w, t = dev(f"kburn {now} {km}", "CCSTEST_ALLOW_WRITE=1")
    check(f"result kburn ok {km}" in r.stdout and w == ["WRITE burn_boot2 30"] and not t, f"ccs-files (SNES shape): ccsdev kburn writes the startup slot once and reads it back ({last(r)})")
    check(not trips_all, f"ccs-files (SNES shape): no tripwire ({'; '.join(trips_all) or 'none'})")


def fixture_flip_checks():
    """the fixtures' corruption changes every chosen byte, also one that already holds "X" (88, which becomes 167) or "ZZZ" (all three of them)"""
    r = wsl(FLIP_SH + """
t=$(mktemp); head -c 200 /dev/zero | tr '\\0' X > "$t"; cp "$t" "$t.0"; flip "$t" 100
"""
            """echo "x $(cmp -l "$t.0" "$t" | wc -l) $(od -An -tu1 -j 100 -N1 "$t" | tr -d ' \\n') $(wc -c < "$t")"
"""
            """head -c 6000 /dev/zero | tr '\\0' Z > "$t"; cp "$t" "$t.0"; flip "$t" 5000 3; echo "z $(cmp -l "$t.0" "$t" | wc -l) $(wc -c < "$t")"
"""
            """head -c 200 /dev/urandom > "$t"; cp "$t" "$t.0"; flip "$t" 100; echo "r $(cmp -l "$t.0" "$t" | wc -l)"; rm -f "$t" "$t.0"
""")
    out = r.stdout.split()
    check(out[:4] == ["x", "1", "167", "200"], f"fixture: the injected corruption changes a byte that already holds X (88 becomes 167), only that byte, and the file's size stays ({r.stdout.strip()} {r.stderr.strip()[:200]})")
    check(out[4:7] == ["z", "3", "6000"], f"fixture: the three-byte change of new-nandc changes all three bytes even where they already hold ZZZ ({r.stdout.strip()})")
    check(out[7:9] == ["r", "1"], f"fixture: over random content the corruption changes exactly the chosen byte ({r.stdout.strip()})")

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--images", required=True); ap.add_argument("--variants", help="comma-separated (default: every image)")
    ap.add_argument("--hakchi-release", required=True, help="the pinned Hakchi2 CE 3.9.3 release folder (basehmods.tar, hakchi.exe): the preparation image's hakchi files are compared with it")
    ap.add_argument("--home-archive", help="a folder with the shared engine's own CCS HOME archive (home-archive.tar, home-archive.json: CcsBackup.Tests home group with CCS_HOME_CHAIN_OUT)")
    ap.add_argument("--export-data", help="with --home-archive: a tar of the data storage's CCS folder and hakchi's transfer folder afterwards (test_nucleus.py --only chain)")
    a = ap.parse_args(); HOME_ARCHIVE[0] = a.home_archive; EXPORT[0] = os.path.abspath(a.export_data) if a.export_data else None
    global QEMU, HMOD
    import gzip
    hm = B.read_inputs(a.hakchi_release)["hmod"]; HMOD = {}
    with tarfile.open(fileobj=io.BytesIO(gzip.decompress(hm) if hm[:2] == b"\x1f\x8b" else hm)) as t:
        for m in t.getmembers():
            n = m.name.lstrip("./")
            if not n: continue
            HMOD[n] = ("dir", b"") if m.isdir() else ("file", t.extractfile(m).read()) if m.isfile() else ("sym", m.linkname.encode())
    QEMU = wsl("echo $HOME/.cache/ccs-sim/toolchain/deb/usr/bin/qemu-arm").stdout.strip()
    if not QEMU or wsl(f"test -x '{QEMU}' && echo yes").stdout.strip() != "yes": raise SystemExit("qemu-arm (the project's WSL toolchain) is not available")
    work = tempfile.mkdtemp(prefix="ccs-ri-"); WORKDIR[0] = work
    fixture_flip_checks()
    for variant in (a.variants.split(",") if a.variants else B.VARIANTS):
        d = os.path.join(a.images, variant); man = json.load(open(os.path.join(d, "MANIFEST.json")))
        ents, cmdline = ramdisk(os.path.join(d, man["image"]))
        print(f"== {variant} {man['image_id']}")
        static_audit(variant, ents, cmdline)
        R = setup(variant, ents, cmdline, work)
        try:
            init_runs(variant, R, work); ccsbr_runs(variant, R, work)
            if variant == "ccs-prepare": prep_runs(R, {e["name"]: e for e in ents}, work)
            if variant == "ccs-rescue": rescue_runs(R, work)
            if variant == "ccs-files": files_runs(R, work)
        finally: wsl(f'rm -rf "{R}"')
        if variant in ("ccs-prepare", "ccs-rescue", "ccs-files"):   # the same image's programs again, on a console shaped like a Super NES Classic
            R = setup(variant, ents, cmdline, work)
            try:
                if variant == "ccs-prepare": snes_prep_runs(R, {e["name"]: e for e in ents}, work)
                if variant == "ccs-rescue": snes_rescue_runs(R, work)
                if variant == "ccs-files": snes_dev_runs(R, work)
            finally: wsl(f'rm -rf "{R}"')
    for f in failures: print("FAILED:", f)
    print(f"RESULT {passed}/{passed + failed}")
    return 0 if failed == 0 else 1

if __name__ == "__main__": sys.exit(main())
