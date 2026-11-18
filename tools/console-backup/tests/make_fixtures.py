"""Console backup test fixtures (tests only; nothing here touches a device). Builds, in a NEW folder:
  data-stock.img        a 403,456 KiB ext4 data partition shaped like a stock NES Classic's (Nintendo's saves and settings), with
                        unknown files, nested folders, names with spaces and special characters, a hard link, links, a FIFO,
                        permissions and owners other than the builder's
  data-modified.img     the same, plus hakchi's folder and settings, other programs' files (an emulator, a module, a custom menu
                        theme, scripts), a Classic Cart Studio HOME (pkg/current, a package copy with MANIFEST.json, session.json and two
                        ROM files, one personal), its saves, and a file named like a Classic Cart Studio record outside its folder
  data-later.img        data-modified after "later use": a new save, a new folder, a changed setting, a removed file
  data-modern.img       a small image with today's default ext4 features (64-bit, metadata checksums) to exercise those paths
  listing-<name>.json   for each image, an independent listing by e2fsprogs' debugfs (path, type, mode, uid, gid, size, links, inode,
                        link target, SHA-256 of regular files): the tests compare the engine's own reader against it
  kernel-modified.img   a fake Android boot image standing in for a modified (hakchi) kernel
  rom-*.nes             the two synthetic ROM files in the HOME package (not real games)
Run from Windows with WSL (Ubuntu) and e2fsprogs, and Pillow: python make_fixtures.py OUT. Copyright (C) 2026 Rance9, GPL-3.0-or-later."""
from __future__ import annotations
import hashlib, json, os, shutil, subprocess, sys, tempfile

def wsl_path(p: str) -> str:
    p = os.path.abspath(p); return "/mnt/" + p[0].lower() + p[2:].replace("\\", "/")

def wsl(script: str) -> str:
    r = subprocess.run(["wsl", "-e", "bash", "-c", script], capture_output=True, text=True, encoding="utf-8", errors="strict")
    if r.returncode != 0: raise SystemExit("WSL step failed:\n" + script[:400] + "\n" + r.stdout[-2000:] + r.stderr[-2000:])
    return r.stdout

def startup_jpeg() -> bytes:
    """a 1280x720 JPEG standing in for a collection's own startup picture (stripes; made with Pillow)"""
    import io
    from PIL import Image
    im = Image.new("RGB", (1280, 720), (30, 90, 160))
    for x in range(0, 1280, 80): im.paste((200, 60, 40), (x, 0, x + 40, 720))
    b = io.BytesIO(); im.save(b, "JPEG", quality=90); return b.getvalue()

def rom(seed: int, prg_kb: int = 32, header: bool = True) -> bytes:
    body = bytearray(); x = seed * 2654435761 & 0xFFFFFFFF
    for _ in range(prg_kb * 1024 + 8192):
        x ^= x << 13 & 0xFFFFFFFF; x ^= x >> 17; x ^= x << 5 & 0xFFFFFFFF; body.append(x & 0xFF)
    h = b"NES\x1a" + bytes([prg_kb // 16, 1, 0, 0]) + bytes(8) if header else b""
    return h + bytes(body)

def content_sha(b: bytes) -> str:
    off = 16 if b[:4] == b"NES\x1a" and len(b) >= 16 else 0
    if off and b[6] & 4 and len(b) >= 528: off += 512
    return hashlib.sha256(b[off:]).hexdigest()

def tree(root: str, kind: str, out: str) -> list[tuple[str, int, int, str]]:
    """writes the source tree; returns (path, uid, gid, mode) changes debugfs applies after mke2fs (it copies the builder's owner)"""
    own = []
    def f(rel, data=b"", mode="0644", uid=0, gid=0):
        p = os.path.join(root, *rel.split("/")); os.makedirs(os.path.dirname(p), exist_ok=True)
        open(p, "wb").write(data if isinstance(data, bytes) else data.encode()); own.append(("/" + rel, uid, gid, mode))
    def d(rel, mode="0755", uid=0, gid=0):
        os.makedirs(os.path.join(root, *rel.split("/")), exist_ok=True); own.append(("/" + rel, uid, gid, mode))
    # Nintendo's own data (stock): saves per game, settings
    for code in ("CLV-P-NAAAE", "CLV-P-NAACE", "CLV-P-NABRE"):
        d(f"clover/profiles/0/{code}")
        f(f"clover/profiles/0/{code}/save.sav", bytes(range(256)) * 32)
        f(f"clover/profiles/0/{code}/cartridge.sram", os.urandom(8192))
    f("clover/profiles/0/settings.json", '{"language":"en","display":"4:3"}')
    f("clover/system/version", "release-v1.0.7\n")
    d("lost+found", "0700")
    # unknown content, names with spaces and special characters, deep nesting, odd owners and modes
    f("unknown/notes for later (draft).txt", "keep this file exactly\n", "0600", 1000, 1000)
    f("unknown/ünïcödé & spaces/file #1 [x].bin", os.urandom(3000), "0640", 1234, 100)
    f("unknown/a/b/c/d/e/f/g/deep.dat", os.urandom(70000), "0444")
    f("unknown/exec.sh", "#!/bin/sh\necho hi\n", "0755")
    d("unknown/empty folder", "0700", 42, 42)
    f("unknown/sparse-ish.bin", bytes(200000) + b"end")
    if kind in ("modified", "later"):
        # hakchi, its settings (USB host on), modules and other programs' files
        f("hakchi/config", "cfg_usb_host='y'\ncfg_usb_rw='y'\ncfg_boot_logo='y'\n")
        f("hakchi/rootfs/etc/preinit.d/b0000_defines", "#!/bin/sh\nmodname=\"hakchi\"\nhakchiVersion='v1.0.4-126'\n")   # as hakchi 3.9.3 installs it
        f("hakchi/rootfs/etc/preinit.d/p9999_custom", "# a user script\n", "0755")
        f("hakchi/hmod/uninstall-retroarch", "/bin/retroarch\n")
        f("hakchi/rootfs/bin/retroarch", os.urandom(50000), "0755")
        f("hakchi/rootfs/etc/libretro/retroarch.cfg", 'video_smooth = "false"\ninput_player1_a = "x"\n')
        f("hakchi/rootfs/etc/libretro/core/fceumm_libretro.so", os.urandom(40000), "0755")
        f("hakchi/games/nes-usa/CLV-H-AAAAA/CLV-H-AAAAA.desktop", "[Desktop Entry]\nExec=/bin/clover-kachikachi /usr/share/games/CLV-H-AAAAA.nes\n")
        f("hakchi/games/nes-usa/CLV-H-AAAAA/CLV-H-AAAAA.nes", rom(7, 16))
        f("hakchi/games/nes-usa/CLV-H-AAAAA/save/1.state", os.urandom(9000), "0644", 0, 0)
        f("hakchi/themes/custom menu/background.png", b"\x89PNG\r\n\x1a\n" + os.urandom(5000))
        # Classic Cart Studio's HOME on the console (an installed package copy) and its saves
        r1, r2 = rom(1), rom(2, 16, header=False)
        c1, c2 = content_sha(r1), content_sha(r2)
        open(os.path.join(out, "rom-1.nes"), "wb").write(r1); open(os.path.join(out, "rom-2.nes"), "wb").write(r2)
        copy = "0123456789abcdef.89abcdef"
        f("ccs/pkg/current", copy + "\n")
        f(f"ccs/pkg/{copy}/MANIFEST.json", json.dumps({"schema": "ccs-console-package/1", "label": "Living Room HOME", "layout": "install", "games": 3}))
        f(f"ccs/pkg/{copy}/session.json", json.dumps({"schema": "ccs-play-session/1", "games": [
            {"id": c1[:16], "title": "Test Cart One", "rom": f"/tmp/ccs/pkg/roms/{c1}.nes", "content_sha256": c1, "unavailable": ""},
            {"id": "p_" + c2[:16], "title": "My Own Test Cart", "rom": f"/tmp/ccs/pkg/roms/{c2}.nes", "content_sha256": c2, "unavailable": ""},
            {"id": "abcdefabcdefabcd", "title": "Missing Cart", "rom": "", "content_sha256": "", "unavailable": "The ROM file of this game is not in this package."}]}))
        f(f"ccs/pkg/{copy}/roms/{c1}.nes", r1); f(f"ccs/pkg/{copy}/roms/{c2}.nes", r2)
        f(f"ccs/pkg/{copy}/home/config.json", '{"theme":"arcade-grid"}')
        sj = startup_jpeg(); ssha = hashlib.sha256(sj).hexdigest(); sid = "custom-" + ssha[:16]   # the collection's own startup picture
        f(f"ccs/pkg/{copy}/startup/startup-{sid}.jpg", sj)
        f(f"ccs/pkg/{copy}/startup/startup.json", json.dumps({"schema": "ccs-startup/1", "choice": sid, "min_visible_ms": 5000, "images": [
            {"id": sid, "name": "Living room", "file": f"startup-{sid}.jpg", "sha256": ssha}, {"id": "forgotten-keep", "name": "Forgotten Keep", "file": "startup-forgotten-keep.jpg", "sha256": "0" * 64}]}))
        f("ccs/data/saves/" + c1 + "/cart.sav", os.urandom(8192))
        f("ccs/install/v-" + copy + ".json", '{"files":{}}')
        f("ccs-install-attempt.json", '{"not":"a Classic Cart Studio record: outside its folder"}')
    if kind == "later":
        f("clover/profiles/0/CLV-P-NAAAE/save.sav", bytes(reversed(range(256))) * 32)
        f("ccs/data/saves/new-game/cart.sav", os.urandom(4096))
        d("added since/folder")
        f("added since/folder/new file.txt", "made after the snapshot\n")
        f("hakchi/rootfs/etc/libretro/retroarch.cfg", 'video_smooth = "true"\n')
    return own

def build(out: str, name: str, kind: str, kib: int, features: str):
    src = tempfile.mkdtemp(prefix="ccsfx-")
    try:
        own = tree(src, kind, out)
        if kind == "later":   # a file present before is removed
            os.remove(os.path.join(src, "unknown", "exec.sh")); own = [o for o in own if o[0] != "/unknown/exec.sh"]
        img = os.path.join(out, name + ".img")
        cmds = []
        for p, uid, gid, mode in own:
            q = '"' + p + '"'
            cmds += [f"set_inode_field {q} uid {uid}", f"set_inode_field {q} gid {gid}"]
            t = "0100000" if os.path.isfile(os.path.join(src, *p.strip("/").split("/"))) else "040000"
            cmds.append(f"set_inode_field {q} mode 0{format(int(t, 8) | int(mode, 8), 'o')}")
        cf = os.path.join(out, name + ".debugfs"); open(cf, "w", newline="\n").write("\n".join(cmds) + "\n")
        s = wsl_path(src); i = wsl_path(img)
        # the tree is copied into WSL's own file system first: links, a hard link and a FIFO behave there as on the console
        links = ("ln -s 'unknown/notes for later (draft).txt' link-to-notes && ln -s /usr/share/games dangling-absolute && "
                 "ln 'clover/profiles/0/settings.json' 'clover/profiles/0/settings-hardlink.json' && mkfifo unknown/a-fifo")
        wsl(f"set -e; w=$(mktemp -d /tmp/ccsfx-XXXXXX); cp -r '{s}/.' \"$w/t\" 2>/dev/null || {{ mkdir -p \"$w/t\"; cp -r '{s}/.' \"$w/t\"; }}; cd \"$w/t\" && {links} && "
            f"mke2fs -q -F -t ext4 {features} -L data -E root_owner=0:0 -d \"$w/t\" \"$w/img\" {kib}k && debugfs -w -f '{wsl_path(cf)}' \"$w/img\" > \"$w/dbg.log\" 2>&1 </dev/null; ! grep -iE 'error|invalid|not found|usage' \"$w/dbg.log\"; "
            f"e2fsck -fn \"$w/img\" >/dev/null && cp \"$w/img\" '{i}' && rm -rf \"$w\"")
        os.remove(cf)
        listing(out, name)
    finally:
        shutil.rmtree(src, ignore_errors=True)

LIST_SH = r'''
img="$1"
walk(){
  debugfs -R "ls -p \"$1\"" "$img" 2>/dev/null </dev/null | while IFS=/ read -r _ ino mode uid gid name size _; do
    [ -z "$name" ] && continue; [ "$name" = "." ] || [ "$name" = ".." ] && continue
    p="$1/$name"; [ "$1" = "/" ] && p="/$name"
    st=$(debugfs -R "stat \"$p\"" "$img" 2>/dev/null </dev/null)
    links=$(printf '%s\n' "$st" | sed -n 's/.*Links: \([0-9]*\).*/\1/p' | head -1)
    target=""; sha=""
    case "$mode" in
      0120*|120*) target=$(debugfs -R "stat \"$p\"" "$img" 2>/dev/null </dev/null | sed -n 's/^Fast link dest: "\(.*\)"$/\1/p') ;;
      0100*|100*) sha=$(debugfs -R "cat \"$p\"" "$img" 2>/dev/null </dev/null | sha256sum | cut -d' ' -f1) ;;
    esac
    printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' "$p" "$mode" "$uid" "$gid" "$size" "$links" "$ino" "$target" "$sha"
    case "$mode" in 040*|40*) walk "$p" ;; esac
  done
}
walk /
'''

def listing(out: str, name: str):
    sh = os.path.join(out, "list.sh"); open(sh, "w", newline="\n").write(LIST_SH)
    raw = wsl(f"bash '{wsl_path(sh)}' '{wsl_path(os.path.join(out, name + '.img'))}'")
    rows = []
    for line in raw.splitlines():
        p, mode, uid, gid, size, links, ino, target, sha = line.split("\t")
        m = int(mode, 8); t = {0o100000: "f", 0o040000: "d", 0o120000: "l", 0o010000: "p", 0o020000: "c", 0o060000: "b", 0o140000: "s"}.get(m & 0o170000, "?")
        rows.append({"path": p, "type": t, "mode": format(m & 0o7777, "04o"), "uid": int(uid), "gid": int(gid), "size": int(size or 0), "links": int(links or 0), "inode": int(ino), "target": target or None, "sha256": sha or None})
    json.dump({"tool": "e2fsprogs debugfs", "entries": rows}, open(os.path.join(out, f"listing-{name}.json"), "w"), indent=1)
    os.remove(sh)

def fake_kernel(seed: bytes) -> bytes:
    """an Android boot image v0 header with a kernel and a ramdisk payload (not a real kernel)"""
    page = 2048; k = hashlib.sha256(seed).digest() * 4096; rd = b"070701" + b"hakchi" * 3000
    hdr = bytearray(page); hdr[0:8] = b"ANDROID!"
    import struct
    struct.pack_into("<IIIIIIII", hdr, 8, len(k), 0x40008000, len(rd), 0x41000000, 0, 0x40F00000, 0x40000100, page)
    hdr[64:64 + 40] = b"root=/dev/nandb decrypt ro hakchi-shell".ljust(40, b"\0")
    pad = lambda b: b + bytes((-len(b)) % page)
    return bytes(hdr) + pad(k) + pad(rd)

def main():
    out = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else "fixtures")
    if os.path.exists(out) and os.listdir(out): raise SystemExit(out + " exists and is not empty")
    os.makedirs(out, exist_ok=True)
    console = "-O ^metadata_csum,^64bit,^huge_file"
    build(out, "data-stock", "stock", 403456, console)
    build(out, "data-modified", "modified", 403456, console)
    build(out, "data-later", "later", 403456, console)
    build(out, "data-modern", "modified", 65536, "")
    open(os.path.join(out, "kernel-modified.img"), "wb").write(fake_kernel(b"modified"))
    open(os.path.join(out, "kernel-other.img"), "wb").write(fake_kernel(b"other"))
    print("fixtures in", out)

if __name__ == "__main__": main()
