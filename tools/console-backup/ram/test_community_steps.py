"""Tests of ccscomm, the preparation image's Community Mods steps (tests only; no console, no USB). Copyright (C) 2026 Rance9, GPL-3.0-or-later.

  python test_community_steps.py --images DIR   (DIR: build_images.py's output; its ccs-prepare image must carry ccs/bin/ccscomm)

1. Static: the image carries ccscomm exactly as tools/console-backup/ram/ccscomm (executable); it refuses outside the preparation image
   before anything else; it never runs, unpacks or sources a package or any of hakchi's install code; it mounts the data storage only
   through its one helper, read-write only in its five write steps; its one folder removal is confined to Classic Cart Studio's own kept
   settings.
2. Runs on the image's OWN BusyBox under qemu-arm (test_recovery_images.py's harness: the storage tools are logging stand-ins, the data
   storage a folder, every write-capable call without permission a TRIPWIRE), with small packages made by this test: every step, every
   refusal, the readbacks; then hakchi 3.9.3's own pack_install and packs_uninstall (the image's pinned b0030_package, as p7080_hmods
   runs them at the console's next start) install and remove those packages, and ccscomm reads the result back (cverify), keeps the
   settings the package's own removal deletes (ckeep) and puts them back (crestore).
QEMU is not the console: this shows what the image's own scripts and tools do with this text, not the console's hardware."""
from __future__ import annotations
import argparse, gzip, hashlib, io, json, os, re, sys, tarfile, tempfile
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import test_recovery_images as TRI   # noqa: E402

passed = failed = 0; failures = []
def check(ok: bool, what: str):
    global passed, failed
    if ok: passed += 1; print("ok   " + what)
    else: failed += 1; failures.append(what); print("FAIL " + what)

def hmod(files: dict, dirs=(), links: dict | None = None) -> bytes:
    """a small package made by this test: files {path: text}, folders, links {path: target}; gzip-compressed tar as hakchi reads it"""
    b = io.BytesIO()
    with tarfile.open(fileobj=b, mode="w", format=tarfile.GNU_FORMAT) as t:
        for d in ("./",) + tuple(dirs):
            ti = tarfile.TarInfo(d); ti.type = tarfile.DIRTYPE; ti.mode = 0o755; t.addfile(ti)
        for p, text in files.items():
            data = text.encode(); ti = tarfile.TarInfo("./" + p); ti.size = len(data); ti.mode = 0o755 if p.startswith(("bin/", "etc/init.d/")) else 0o644; t.addfile(ti, io.BytesIO(data))
        for p, target in (links or {}).items():
            ti = tarfile.TarInfo("./" + p); ti.type = tarfile.SYMTYPE; ti.linkname = target; t.addfile(ti)
    return gzip.compress(b.getvalue(), mtime=0)

def fixture(name: str, version: str) -> bytes:
    return hmod({f"bin/{name}": f"#!/bin/sh\necho {name} {version}\n", f"etc/init.d/S81{name}": f"#!/bin/sh\n{name} &\n", f"etc/{name}/defaults.cfg": f"version={version}\n",
                 "install": f"echo installing {name}\n", "uninstall": f'rm -f "$rootfs/etc/{name}/settings.cfg"\nrm -f "$rootfs/etc/{name}/favorites.txt"\n', "readme.md": f"{name} {version}\n"},
                dirs=("./bin", "./etc", "./etc/init.d", f"./etc/{name}"), links={f"etc/{name}/current.cfg": "defaults.cfg"})

HK_START = r'''R='@R@'
# the console's next normal start, as hakchi 3.9.3's p7080_hmods applies its transfer folder: the image's own pinned preinit and base
# functions (b0030_package: packs_uninstall, packs_install, pack_install), the data storage a folder here
export modname=hakchi modpath="$R/root/hakchi" mountpoint="$R/newroot" temppath="$R/tmp/hk"
mkdir -p "$temppath"
. "$R/root/hakchi/rootfs/etc/preinit" || exit 91
source_base "$R/root/hakchi/rootfs/etc/preinit.d" || exit 92
temppath="$R/tmp/hk"; mountpoint="$R/newroot"; installpath="$mountpoint/var/lib/hakchi"; rootfs="$installpath/rootfs"
transferpath="$installpath/transfer"
if [ -d "$transferpath" ]; then
  cd "$transferpath"
  [ -f "$transferpath/uninstall" ] && packs_uninstall $(cat "$transferpath/uninstall")
  rm -f "$transferpath/uninstall"
  [ -f "$transferpath/hmod/uninstall" ] && packs_uninstall $(cat "$transferpath/hmod/uninstall")
  rm -f "$transferpath/hmod/uninstall"
  packs_install "$transferpath"
  if [ -d "$transferpath/hmod" ]; then packs_install "$transferpath/hmod"; rm -rf "$transferpath/hmod"; fi
  cd /; rm -rf "$transferpath"
fi
echo HK-START-DONE
'''

def static(by: dict):
    e = by.get("ccs/bin/ccscomm")
    src = open(os.path.join(HERE, "ccscomm"), "rb").read().replace(b"\r\n", b"\n")
    check(e is not None and e["data"] == src and e["mode"] & 0o111, "ccs-prepare: the image carries ccscomm exactly as tools/console-backup/ram/ccscomm, executable")
    if e is None: return
    c = TRI.code(e["data"].decode())
    head = c[:c.index('case "$1" in')]
    check('[ "$(cat "$CCS/profile" 2>/dev/null)" = prepare ] || { echo' in head.split("r(){")[0], "ccscomm refuses in any image but the preparation image, before anything else")
    forbidden = [r"\bpacks?_install\b", r"\bpacks?_uninstall\b", r"\bmod_install\b", r"\bmod_uninstall\b", r"transfer_file", r"\bsource\b", r"^\s*\.\s", r"tar\s+-x", r"\bsunxi", r"\bflash", r"\bof=",
                 r"\breboot\b", r"\bkexec\b", r"\bmke2fs\b", r"\be2fsck\b", r"\bburn", r"/dev/nand", r"\bsh\s+\"\$"]
    hits = [x for x in forbidden if re.search(x, c.replace('. "$CCS/env"', ""), re.M)]
    check(not hits, f"ccscomm never runs, unpacks or sources a package or hakchi's install code, and touches no other storage ({hits or 'none'})")
    mounts = re.findall(r"^\s*mount\b|[;&|{]\s*mount\b", c, re.M)
    check(len(mounts) == 1 and 'dmount(){ mountpoint -q "$D" && return 1; mkdir -p "$D"; mount -o "$1,noatime" "$DEV/nandc" "$D"; }' in c, "ccscomm mounts the data storage only through its one helper")
    body = c[c.index('case "$1" in'):]
    steps = {m.group(1): m.group(2) for m in re.finditer(r"^  ([\w-]+)\)\n(.*?);;", body, re.M | re.S)}
    check(list(steps) == ["cstate", "cverify", "cput", "cunlist", "ckeep", "cforget", "crestore"], f"ccscomm has exactly its seven steps ({', '.join(steps)})")
    rw = [k for k, v in steps.items() if "dmount rw" in v]
    check(rw == ["cput", "cunlist", "ckeep", "cforget", "crestore"] and all("dmount rw" not in steps[k] for k in ("cstate", "cverify")) and all(v.count("dmount rw") == 1 for k, v in steps.items() if k in rw),
          f"only the five write steps mount the data storage read-write, once each; cstate and cverify only read ({rw})")
    check(c.count("rm -rf") == 1 and 'kgone(){ case "$1" in "$K"/?*) rm -rf "$1" ;; esac; }' in c and 'K="$D/ccs/community/kept"' in c,
          "ccscomm's one folder removal is confined to Classic Cart Studio's own kept settings (kgone)")
    check(all(re.search(r"\.ccs-new\b", steps[k]) and "mv -f" in steps[k] for k in ("cput", "cunlist")) and all("readback" in steps[k] for k in ("cput", "cunlist", "ckeep", "cforget", "crestore")),
          "every write goes through a new name and a rename, and every write step reads back from the storage mounted again")

def runs(R: str, work: str):
    A = "CCSTEST_ALLOW_WRITE=1"; D = "@R@/newroot/var/lib"; T = D + "/hakchi/transfer"
    TRI.sh_out(R, "rm -rf '@R@/newroot/var'; mkdir -p '@R@/newroot/var/lib/hakchi/rootfs/etc' '@R@/newroot/var/lib/hakchi/hmod'; touch '@R@/newroot/var/lib/hakchi/rootfs/etc/preinit'; printf '%s\\n' '@R@/newroot' > '@R@/mounted'")
    def step(s: str, env: str = "", stdin: str | None = None):
        TRI.sh_out(R, "rm -f '@R@/log'")
        r = TRI.run(R, f"sh '{R}/root/ccs/bin/ccscomm' {s}; echo rc=$?", env, TRI.wpath(stdin) if stdin else None)
        log = TRI.sh_out(R, "cat '@R@/log' 2>/dev/null").splitlines()
        return r, [l for l in log if l.startswith("WRITE")], [l for l in log if l.startswith("TRIPWIRE")]
    def last(r):
        lines = r.stdout.strip().splitlines(); return next((l for l in reversed(lines) if l.startswith("result ")), lines[-1] if lines else "(nothing)")
    def mounted(): return TRI.sh_out(R, "cat '@R@/mounted'").split()
    def put(name: str, b: bytes) -> str:
        p = os.path.join(work, name); open(p, "wb").write(b); return p
    sha = lambda b: hashlib.sha256(b).hexdigest()
    a, b2, a2 = fixture("fx_a", "1.0"), fixture("fx_b", "2.0"), fixture("fx_a", "1.1")
    fa, fb, fa2 = put("fx_a.hmod", a), put("fx_b.hmod", b2), put("fx_a2.hmod", a2)

    TRI.sh_out(R, "printf 'backup\\n' > '@R@/root/ccs/profile'")
    r, w, t = step("cstate fx_a")
    check("rc=13" in r.stdout and not w and not t, f"in an image whose profile is not \"prepare\", ccscomm refuses before anything ({r.stdout.strip()[:80]})")
    TRI.sh_out(R, "printf 'prepare\\n' > '@R@/root/ccs/profile'")
    r, w, t = step("format", A)
    check("rc=2" in r.stdout and not w, "an unknown step is refused")
    r, w, t = step("cstate fx_a fx_b")
    check("cpkg=fx_a absent kept=none" in r.stdout and "cpkg=fx_b absent kept=none" in r.stdout and "result cstate ok" in r.stdout and not w and not t and mounted() == [R + "/newroot"],
          f"cstate reads each package's state with the storage mounted read-only, then unmounts it ({last(r)})")
    for bad in ("cstate ccs-mod", "cstate hakchi", "cstate ../x", "cput ccs-mod 0 10 " + "a" * 64 + " 10", "cput fx_a 2 10 " + "a" * 64 + " 10", "cunlist ccs-nucleus", "cunlist fx_a fx_a", "ckeep fx_a ccs/data/x", "ckeep fx_a hakchi/rootfs/../x", "cforget x.hmod"):
        r, w, t = step(bad, A)
        check("refused request" in last(r) and not w and not t, f"{bad.split()[0]} with {' '.join(bad.split()[1:])[:40]}: refused before anything ({last(r)})")
    r, w, t = step(f"cput fx_a 0 {len(a)} {sha(a)} 100000", "", fa)
    check("failed mount" in last(r) and any("nandc" in x for x in t) and TRI.sh_out(R, f"test -e '{T}/fx_a.hmod' && echo THERE").strip() == "", f"without write permission the read-write mount is a tripwire and nothing is placed ({last(r)})")
    r, w, t = step(f"cput fx_a 0 {len(a)} {sha(b2)} 100000", A, fa)
    check("refused received-mismatch" in last(r) and TRI.sh_out(R, f"ls '{T}' 2>/dev/null").strip() == "", f"bytes that are not the named SHA-256: refused, the partial file removed ({last(r)})")
    notgz = put("plain.bin", b"x" * 100)
    r, w, t = step(f"cput fx_a 0 100 {sha(b'x' * 100)} 100000", A, notgz)
    check("refused received-mismatch" in last(r), f"bytes that are not a gzip package: refused ({last(r)})")
    r, w, t = step(f"cput fx_a 0 {len(a)} {sha(a)} 100000", A, fa)
    same = TRI.sh_out(R, f"sha256sum '{T}/fx_a.hmod' | cut -d' ' -f1").strip()
    check(f"result cput ok {sha(a)}" in r.stdout and same == sha(a) and w == ["WRITE mount nandc rw"] and not t and mounted() == [R + "/newroot"],
          f"cput places the package in hakchi's transfer folder (a new name, a rename), reads it back and unmounts ({last(r)}; {w})")
    r, w, t = step(f"cput fx_a 0 {len(a)} {sha(a)} 100000", A, fa)
    check(f"result cput ok already {sha(a)}" in r.stdout and "began cput" not in r.stdout and not w, f"the same package already waiting is not written again ({last(r)})")
    r, w, t = step(f"cput fx_a 0 {len(a2)} {sha(a2)} 100000", A, fa2)
    check("refused another fx_a.hmod already waits" in last(r) and not w, f"another package of that name waiting is never replaced ({last(r)})")
    r, w, t = step(f"cput fx_b 1 {len(b2)} {sha(b2)} 100000", A, fb)
    check(f"result cput ok {sha(b2)}" in r.stdout and TRI.sh_out(R, f"test -f '{T}/hmod/fx_b.hmod' && echo HMOD").strip() == "HMOD", f"a package placed in tier 1 waits in the transfer folder's hmod/ folder ({last(r)})")
    # hakchi's own next start installs both (fx_a from transfer/, fx_b from transfer/hmod/ afterwards)
    hk = os.path.join(work, "hk-start.sh"); open(hk, "w", newline="\n").write(HK_START.replace("@R@", R))
    TRI.wsl(f"cp '{TRI.wpath(hk)}' '{R}/hk-start.sh'")
    st = TRI.run(R, f"sh '{R}/hk-start.sh'")
    lst = TRI.sh_out(R, f"cd '{D}/hakchi' && ls hmod; test -f rootfs/bin/fx_a && echo BIN_A; test -L rootfs/etc/fx_a/current.cfg && echo LINK_A; test -f rootfs/bin/fx_b && echo BIN_B; test -e transfer && echo TRANSFER").split()
    order = [l for l in st.stdout.splitlines() if l.startswith("installing ") and l.endswith("...")]   # hakchi's own lines (the packages' install scripts echo too)
    check("HK-START-DONE" in st.stdout and "uninstall-fx_a" in lst and "uninstall-fx_b" in lst and "BIN_A" in lst and "LINK_A" in lst and "BIN_B" in lst and "TRANSFER" not in lst and order == ["installing fx_a...", "installing fx_b..."],
          f"hakchi's own pack_install (the image's pinned b0030_package) installs fx_a, then fx_b from hmod/, writes their uninstall lists and removes the transfer folder ({order}; {lst})")
    r, w, t = step("cstate fx_a fx_b")
    m = re.search(r"cpkg=fx_a listed ([0-9a-f]{64}) ([0-9]+) kept=none", r.stdout)
    check(m and "cpkg=fx_b listed" in r.stdout and not w, f"cstate reads hakchi's uninstall lists of the installed packages ({last(r)})")
    r, w, t = step("cverify fx_a")
    files = dict(reversed(l[6:].split(" ", 1)) for l in r.stdout.splitlines() if l.startswith("cfile="))
    want = {"bin/fx_a": hashlib.sha256(b"#!/bin/sh\necho fx_a 1.0\n").hexdigest(), "etc/fx_a/defaults.cfg": hashlib.sha256(b"version=1.0\n").hexdigest(), "etc/fx_a/current.cfg": "link"}
    check("result cverify ok" in r.stdout and all(files.get(k) == v for k, v in want.items()) and "install" not in files and not w,
          f"cverify reads hakchi's own generated uninstall lines and gives each installed file's SHA-256 (or link) now ({last(r)}; {len(files)} files)")
    r, w, t = step("cverify fx_c")
    check("result cverify failed not-installed" in r.stdout, f"cverify of a package hakchi does not list: not installed ({last(r)})")
    # the person's settings, kept before the package's own removal deletes them
    TRI.sh_out(R, f"printf 'lang=en\\n' > '{D}/hakchi/rootfs/etc/fx_a/settings.cfg'; mkdir -p '{D}/clover/profiles/0'; printf 'fav\\n' > '{D}/clover/profiles/0/fx_a.fav'")
    r, w, t = step("ckeep fx_a hakchi/rootfs/etc/fx_a/settings.cfg hakchi/rootfs/etc/fx_a/favorites.txt clover/profiles/0/fx_a.fav")
    check("failed mount" in last(r) and any("nandc" in x for x in t), f"ckeep without write permission: a tripwire, nothing kept ({last(r)})")
    r, w, t = step("ckeep fx_a hakchi/rootfs/etc/fx_a/settings.cfg hakchi/rootfs/etc/fx_a/favorites.txt clover/profiles/0/fx_a.fav", A)
    km = re.search(r"result ckeep ok ([0-9a-f]{64}) 2$", last(r))
    kl = TRI.sh_out(R, f"cat '{D}/ccs/community/kept/fx_a.list'")
    check(km and w == ["WRITE mount nandc rw"] and "  hakchi/rootfs/etc/fx_a/settings.cfg" in kl and "  clover/profiles/0/fx_a.fav" in kl and "favorites.txt" not in kl,
          f"ckeep copies the settings that exist (two of three places) into Classic Cart Studio's own folder with their list, read back ({last(r)})")
    r, w, t = step("ckeep fx_a hakchi/rootfs/etc/fx_a/settings.cfg clover/profiles/0/fx_a.fav", A)
    check(f"result ckeep ok already {km.group(1) if km else '?'} 2" in r.stdout and not w, f"the same settings kept again: ok already, nothing written ({last(r)})")
    TRI.sh_out(R, f"printf 'lang=fr\\n' > '{D}/hakchi/rootfs/etc/fx_a/settings.cfg'")
    r, w, t = step("ckeep fx_a hakchi/rootfs/etc/fx_a/settings.cfg clover/profiles/0/fx_a.fav", A)
    check("refused settings kept earlier for fx_a are still there" in last(r) and not w, f"different settings while an earlier kept copy is there: refused, the kept copy never replaced ({last(r)})")
    TRI.sh_out(R, f"printf 'lang=en\\n' > '{D}/hakchi/rootfs/etc/fx_a/settings.cfg'")
    r, w, t = step("ckeep fx_b hakchi/rootfs/etc/fx_b/settings.cfg", A)
    check("result ckeep ok none 0" in r.stdout and not w, f"nothing to keep: ok none, nothing written ({last(r)})")
    r, w, t = step("cunlist fx_b fx_a", A)
    lsha = hashlib.sha256(b"fx_b fx_a\n").hexdigest()
    check(f"result cunlist ok {lsha}" in r.stdout and TRI.sh_out(R, f"cat '{T}/uninstall'") == "fx_b fx_a\n" and w == ["WRITE mount nandc rw"], f"cunlist writes hakchi's removal list in the order given ({last(r)})")
    r, w, t = step("cunlist fx_a", A)
    check("refused another removal list already waits" in last(r) and not w, f"a different removal list already waiting is never overwritten ({last(r)})")
    st = TRI.run(R, f"sh '{R}/hk-start.sh'")
    gone = TRI.sh_out(R, f"cd '{D}' && ls hakchi/hmod; test -e hakchi/rootfs/bin/fx_a && echo BIN_A; test -e hakchi/rootfs/etc/fx_a/settings.cfg && echo SETTINGS; test -f ccs/community/kept/fx_a/hakchi/rootfs/etc/fx_a/settings.cfg && echo KEPT; test -f clover/profiles/0/fx_a.fav && echo FAV").split()
    check("HK-START-DONE" in st.stdout and "uninstall-fx_a" not in gone and "uninstall-fx_b" not in gone and "BIN_A" not in gone and "SETTINGS" not in gone and "KEPT" in gone and "FAV" in gone,
          f"hakchi's own packs_uninstall runs each package's removal (deleting its settings, as the audited packages' removals do); Classic Cart Studio's kept copy stays ({gone})")
    r, w, t = step("crestore fx_a", A)
    check("refused fx_a is not installed" in last(r) and not w, f"kept settings are put back only while the package is installed ({last(r)})")
    r, w, t = step(f"cput fx_a 0 {len(a)} {sha(a)} 100000", A, fa); TRI.run(R, f"sh '{R}/hk-start.sh'")
    TRI.sh_out(R, f"rm -f '{D}/clover/profiles/0/fx_a.fav'; printf 'made after\\n' > '{D}/clover/profiles/0/fx_a.fav'")
    r, w, t = step("crestore fx_a", A)
    back = TRI.sh_out(R, f"cat '{D}/hakchi/rootfs/etc/fx_a/settings.cfg'; cat '{D}/clover/profiles/0/fx_a.fav'; test -f '{D}/ccs/community/kept/fx_a.list' && echo STILL")
    check("result crestore ok 1 1" in r.stdout and back == "lang=en\nmade after\nSTILL\n" and w == ["WRITE mount nandc rw"],
          f"installed again: crestore puts back the settings where nothing is, never replaces the file made since, and keeps the copy while something was not put back ({last(r)})")
    TRI.sh_out(R, f"rm -f '{D}/clover/profiles/0/fx_a.fav'")
    r, w, t = step("crestore fx_a", A)
    left = TRI.sh_out(R, f"test -e '{D}/ccs/community/kept/fx_a.list' && echo LIST; test -e '{D}/ccs/community/kept/fx_a' && echo DIR; cat '{D}/clover/profiles/0/fx_a.fav'")
    check("result crestore ok 2 0" in r.stdout and left == "fav\n", f"the second time the other file returns too and the first, already back exactly as kept, counts as back; every kept file back, the kept copy removed ({last(r)}; {left.split()})")
    r, w, t = step("ckeep fx_a hakchi/rootfs/etc/fx_a/settings.cfg", A)
    r, w, t = step("cforget fx_a", A)
    left = TRI.sh_out(R, f"test -e '{D}/ccs/community/kept/fx_a.list' && echo LIST; test -e '{D}/ccs/community/kept/fx_a' && echo DIR; test -f '{D}/hakchi/rootfs/etc/fx_a/settings.cfg' && echo LIVE")
    check("result cforget ok" in r.stdout and left.split() == ["LIVE"] and w == ["WRITE mount nandc rw"], f"cforget removes only Classic Cart Studio's kept copy; the mod's own settings stay ({last(r)})")
    r, w, t = step("cforget fx_a", A)
    check("result cforget ok already" in r.stdout and not w, f"nothing kept: ok already, nothing written ({last(r)})")
    TRI.sh_out(R, f"rm -rf '{D}/ccs/community/kept'; mkdir -p '{D}/ccs/community'; ln -s /tmp '{D}/ccs/community/kept'")
    r, w, t = step("ckeep fx_a hakchi/rootfs/etc/fx_a/settings.cfg", A)
    check("refused a link is on the way" in last(r) and not w, f"a link on the way to the kept settings: refused before any write ({last(r)})")
    TRI.sh_out(R, f"rm -f '{D}/ccs/community/kept'; rm -rf '{T}'; ln -s /tmp '{T}'")
    r, w, t = step(f"cput fx_b 0 {len(b2)} {sha(b2)} 100000", A, fb)
    check("refused a link is on the way" in last(r) and not w, f"hakchi's transfer folder being a link: refused before any write ({last(r)})")
    TRI.sh_out(R, f"rm -f '{T}'")

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--images", required=True); a = ap.parse_args()
    TRI.QEMU = TRI.wsl("echo $HOME/.cache/ccs-sim/toolchain/deb/usr/bin/qemu-arm").stdout.strip()
    if not TRI.QEMU or TRI.wsl(f"test -x '{TRI.QEMU}' && echo yes").stdout.strip() != "yes": raise SystemExit("qemu-arm (the project's WSL toolchain) is not available")
    work = tempfile.mkdtemp(prefix="ccs-comm-"); TRI.WORKDIR[0] = work
    d = os.path.join(a.images, "ccs-prepare"); man = json.load(open(os.path.join(d, "MANIFEST.json")))
    ents, cmdline = TRI.ramdisk(os.path.join(d, man["image"]))
    print(f"== ccs-prepare {man['image_id']}")
    static({e["name"]: e for e in ents})
    R = TRI.setup("ccs-prepare", ents, cmdline, work)
    try: runs(R, work)
    finally: TRI.wsl(f'rm -rf "{R}"')
    for f in failures: print("FAILED:", f)
    print(f"RESULT {passed}/{passed + failed}")
    return 0 if failed == 0 else 1

if __name__ == "__main__": sys.exit(main())
