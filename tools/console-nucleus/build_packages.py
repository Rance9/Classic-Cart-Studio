"""Builds the two console packages of the CCS Console Workbench's install methods, as hakchi packages (.hmod: a gzip tar that hakchi's
own pack_install unpacks): ccs-mod.hmod (CCS Mod for Hakchi) and ccs-nucleus.hmod (CCS Nucleus). Copyright (C) 2026 Rance9,
GPL-3.0-or-later.

  python build_packages.py --out DIR [--home-package DIR]
  python build_packages.py --verify DIR

Reproducible: every member has a fixed time (MTIME), owner 0:0 and mode, the members are in name order, scripts are stored with LF line
ends and the gzip header carries no name or time; the same sources always give the same bytes. Each package carries ONLY Classic Cart
Studio's own files, listed one by one below (no Nintendo file, no hakchi file, no other package's file): the scripts in console/, the
two menu pictures in console/assets/, and two generated records:
  etc/ccs/<method>/VERSION   schema=ccs-method-package/1, the method, its package id (SHA-256 over the other members' names and
                             contents) and the hakchi files the scripts were checked against (DESIGN.md "Insertion point")
  etc/ccs/<method>/FILES     "<sha256> <mode> <path>" for every file hakchi copies into its overlay (all but install, uninstall,
                             readme.md and FILES itself): what the console's install script checks after copying and records
CCS HOME's own package (tools/companion/native_home/console_package.py) is NOT inside: with --home-package its MANIFEST.json is
referenced by SHA-256, layout and size in this build's MANIFEST.json (both methods run the CCS HOME package the shared engine installs at
/var/lib/ccs/pkg).
Output (a new or empty folder): ccs-mod.hmod, ccs-nucleus.hmod, MANIFEST.json (schema ccs-console-method-packages/1: each package's
SHA-256, size, members, the bytes it puts on the console's data storage) and SHA256SUMS."""
from __future__ import annotations
import argparse, gzip, hashlib, io, json, os, sys, tarfile

HERE = os.path.dirname(os.path.abspath(__file__)); SRC = os.path.join(HERE, "console")
MTIME = 1763164800                      # 2025-11-15 00:00 UTC: fixed
VERSION = "1"
SCHEMA = "ccs-console-method-packages/1"
# the pinned hakchi files whose start sequence these packages were written and tested against (tools/console-backup/ram/build_images.py)
HAKCHI_PINS = {"hakchi.hmod": "003994b46504959895f40b0b9e91b16951ef4a638feb6312314c8fc4fdbff592",
               "boot.img": "9e921c326be65aa4a3100270e86fdb15fe4b8563d586941b9f6c640eef064e4c", "hakchi": "v1.0.4-126", "kernel": "3.4.113.29-madmonkey"}

# package member -> (source under console/, mode). Directories are added for every parent.
PACKAGES = {
    "mod": {
        "install": ("mod/install", 0o644), "uninstall": ("mod/uninstall", 0o644), "readme.md": ("mod/readme.md", 0o644),
        "bin/ccs-mod": ("mod/bin/ccs-mod", 0o755),
        "etc/ccs/mod/lib.sh": ("lib.sh", 0o644),
        "etc/ccs/mod/S81ccs-mod": ("mod/S81ccs-mod", 0o755),
        "etc/ccs/mod/entry.png": ("assets/ccs-mod-entry.png", 0o644),
        "etc/ccs/mod/entry-small.png": ("assets/ccs-mod-entry-small.png", 0o644),
    },
    "nucleus": {
        "install": ("nucleus/install", 0o644), "uninstall": ("nucleus/uninstall", 0o644), "readme.md": ("nucleus/readme.md", 0o644),
        "bin/ccs-nucleus": ("nucleus/bin/ccs-nucleus", 0o755),
        "etc/ccs/nucleus/lib.sh": ("lib.sh", 0o644),
        "etc/ccs/nucleus/entry.png": ("assets/ccs-nucleus-entry.png", 0o644),
        "etc/ccs/nucleus/entry-small.png": ("assets/ccs-nucleus-entry-small.png", 0o644),
        "etc/preinit.d/p7060_ccsnucleus": ("nucleus/p7060_ccsnucleus", 0o644),
        "etc/preinit.d/p7071_ccsnucleus": ("nucleus/p7071_ccsnucleus", 0o644),
        "etc/init.d/S81ccs-nucleus": ("nucleus/S81ccs-nucleus", 0o755),
    },
}
NOT_COPIED = ("install", "uninstall", "readme.md")   # hakchi's transfer_default never copies the first two; readme files are deleted first

def sha(b: bytes) -> str: return hashlib.sha256(b).hexdigest()

def source_bytes(rel: str) -> bytes:
    b = open(os.path.join(SRC, *rel.split("/")), "rb").read()
    return b if rel.endswith(".png") else b.replace(b"\r\n", b"\n")

def members(method: str) -> dict[str, tuple[bytes, int]]:
    m = {name: (source_bytes(src), mode) for name, (src, mode) in PACKAGES[method].items()}
    pid = hashlib.sha256()
    for name in sorted(m): pid.update(name.encode() + b"\0" + m[name][0] + b"\0")
    version = (f"schema=ccs-method-package/1\nmethod={method}\nversion={VERSION}\npackage={pid.hexdigest()}\n"
               f"hakchi={HAKCHI_PINS['hakchi']}\nhakchi_hmod_sha256={HAKCHI_PINS['hakchi.hmod']}\nboot_img_sha256={HAKCHI_PINS['boot.img']}\n").encode()
    m[f"etc/ccs/{method}/VERSION"] = (version, 0o644)
    files = "".join(f"{sha(b)} {mode:04o} {n}\n" for n, (b, mode) in sorted(m.items()) if n not in NOT_COPIED)
    m[f"etc/ccs/{method}/FILES"] = (files.encode(), 0o644)
    return m

def hmod_bytes(mem: dict[str, tuple[bytes, int]]) -> bytes:
    dirs = sorted({"/".join(n.split("/")[:k]) for n in mem for k in range(1, len(n.split("/")))})
    entries = sorted([(d, None, 0o755) for d in dirs] + [(n, b, mode) for n, (b, mode) in mem.items()], key=lambda e: e[0])
    raw = io.BytesIO()
    with tarfile.open(fileobj=raw, mode="w", format=tarfile.USTAR_FORMAT) as t:
        for name, data, mode in entries:
            ti = tarfile.TarInfo("./" + name); ti.mtime = MTIME; ti.uid = ti.gid = 0; ti.uname = ti.gname = ""; ti.mode = mode
            if data is None: ti.type = tarfile.DIRTYPE; t.addfile(ti)
            else: ti.size = len(data); t.addfile(ti, io.BytesIO(data))
    out = io.BytesIO()
    with gzip.GzipFile(filename="", mode="wb", fileobj=out, compresslevel=9, mtime=0) as g: g.write(raw.getvalue())
    return out.getvalue()

def home_reference(d: str | None) -> dict | None:
    if not d: return None
    mb = open(os.path.join(d, "MANIFEST.json"), "rb").read(); m = json.loads(mb)
    if m.get("schema") != "ccs-console-package/1": raise SystemExit("--home-package is not a Classic Cart Studio console package")
    return {"manifest_sha256": sha(mb), "layout": m.get("layout"), "total_bytes": m.get("total_bytes"), "files": len(m.get("files") or {}),
            "games": m.get("games"), "note": "referenced, not included: the shared engine installs it at /var/lib/ccs/pkg/<version>.<attempt>"}

def build(out: str, home: str | None = None) -> dict:
    if os.path.exists(out) and os.listdir(out): raise SystemExit(f"{out} exists and is not empty: packages are always built into a new folder")
    os.makedirs(out, exist_ok=True)
    man = {"schema": SCHEMA, "version": VERSION, "hakchi": HAKCHI_PINS, "home_package": home_reference(home), "packages": {}}
    sums = []
    for method in PACKAGES:
        mem = members(method); hb = hmod_bytes(mem); name = f"ccs-{method}.hmod"
        open(os.path.join(out, name), "wb").write(hb)
        copied = {n: b for n, (b, _) in mem.items() if n not in NOT_COPIED}
        man["packages"][method] = {
            "file": name, "sha256": sha(hb), "bytes": len(hb),
            "package_id": dict(l.split("=", 1) for l in mem[f"etc/ccs/{method}/VERSION"][0].decode().splitlines())["package"],
            "members": {n: {"sha256": sha(b), "bytes": len(b), "mode": f"{mode:04o}"} for n, (b, mode) in sorted(mem.items())},
            "overlay_bytes": sum(len(b) for b in copied.values()), "overlay_files": len(copied),
            # the menu entry: its desktop file (at most this size: the longest console path and name) and its two pictures
            "menu_entry_bytes": 640 + len(mem[f"etc/ccs/{method}/entry.png"][0]) + len(mem[f"etc/ccs/{method}/entry-small.png"][0]),
        }
        sums.append(f"{sha(hb)}  {name}")
    mb = (json.dumps(man, indent=1) + "\n").encode()
    open(os.path.join(out, "MANIFEST.json"), "wb").write(mb)
    with open(os.path.join(out, "SHA256SUMS"), "w", newline="\n") as f: f.write("\n".join(sums + [f"{sha(mb)}  MANIFEST.json"]) + "\n")
    return man

def verify(d: str) -> list[str]:
    """every listed package is there with its SHA-256, unpacks to exactly its listed members, and nothing else is in the folder"""
    man = json.load(open(os.path.join(d, "MANIFEST.json"))); bad = []
    for method, p in man["packages"].items():
        b = open(os.path.join(d, p["file"]), "rb").read()
        if sha(b) != p["sha256"]: bad.append(f"{p['file']} changed"); continue
        with tarfile.open(fileobj=io.BytesIO(b), mode="r:gz") as t:
            got = {m.name[2:]: sha(t.extractfile(m).read()) for m in t.getmembers() if m.isfile()}
        if got != {n: v["sha256"] for n, v in p["members"].items()}: bad.append(f"{p['file']}: members differ from the manifest")
    extra = set(os.listdir(d)) - {"MANIFEST.json", "SHA256SUMS"} - {p["file"] for p in man["packages"].values()}
    bad += [f"extra {x}" for x in sorted(extra)]
    return bad

def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out"); ap.add_argument("--home-package"); ap.add_argument("--verify"); a = ap.parse_args()
    if a.verify: bad = verify(a.verify); print("\n".join(bad) or "verified"); return 1 if bad else 0
    if not a.out: ap.error("--out or --verify")
    man = build(a.out, a.home_package)
    for m, p in man["packages"].items(): print(f"{p['file']}: {p['bytes']} bytes  sha256 {p['sha256']}  package {p['package_id'][:16]}")
    return 0

if __name__ == "__main__": sys.exit(main())
