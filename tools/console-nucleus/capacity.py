"""Room needed on the console's data storage for CCS Mod or CCS Nucleus, from measured inputs only. Copyright (C) 2026 Rance9,
GPL-3.0-or-later.

  python capacity.py --packages DIR --method mod|nucleus --free-bytes N [--block-size N] [--home-package DIR | --home-bytes N]
                     [--image FILE ... | --image-bytes N ...] [--update]

Every input is a measurement, never an illustrative figure:
  --packages      build_packages.py's output: the exact files each package puts into hakchi's overlay and its menu entry
  --home-package  CCS HOME's console package (its MANIFEST.json total_bytes), or --home-bytes when it is installed already (0)
  --image         the owner's system images to import (their file sizes), or --image-bytes
  --free-bytes    the data storage's available space as the console reports it now (the Workbench reads it: df on /var/lib)
  --block-size    the data storage's block size as the console reports it (statfs); every file takes whole blocks
What is added (decimal units, as everything people read):
  package         the method's files in hakchi's overlay, its menu entry and its small records (journal, registry, state)
  CCS HOME        the HOME package (the shared engine installs a new copy beside the one in use: --update counts the old copy as rollback)
  system images   each image the owner imports (uploaded once to Nucleus's staging folder, then moved: one rename, no second copy)
  staging         the largest single upload while it is on the storage before its move (an image is counted once: it is moved, not copied)
  rollback        the copy kept until an update switches over (the previous HOME package)
  save reserve    the engine's reserve for saves, Game Settings and HOME choices (console_engine.ccs_install.RESERVE)
No count of boot choices and no speed follows from this: only the space each actual file takes."""
from __future__ import annotations
import argparse, json, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(os.path.dirname(HERE), "companion"))
try:
    from console_engine.ccs_install import RESERVE as SAVE_RESERVE   # the shared engine's own reserve (decimal MB)
except Exception:   # the engine is not importable here (a stripped tree): the same value, stated where it comes from
    SAVE_RESERVE = 8 * 1000 * 1000
RECORDS = 4 * 1024   # the journal, registry and state files together stay under one block each; counted as blocks below

def blocks(n: int, block: int) -> int: return 0 if n <= 0 else -(-n // block) * block

def mb(n: int) -> str:
    for unit, d in (("GB", 1e9), ("MB", 1e6), ("KB", 1e3)):
        if abs(n) >= d: return f"{n / d:.1f} {unit}"
    return f"{n} bytes"

def compute(packages: str, method: str, free_bytes: int, block: int = 4096, home_bytes: int = 0, image_bytes: list[int] | None = None,
            update: bool = False) -> dict:
    man = json.load(open(os.path.join(packages, "MANIFEST.json")))
    p = man["packages"][method]
    copied = [v["bytes"] for n, v in p["members"].items() if n not in ("install", "uninstall", "readme.md")]
    pkg = sum(blocks(b, block) for b in copied) + blocks(p["menu_entry_bytes"], block) + 3 * blocks(RECORDS, block)
    imgs = [int(x) for x in (image_bytes or [])]
    if imgs and method != "nucleus": raise ValueError("system images are CCS Nucleus options")
    rows = [{"what": f"package ({p['file']}: {p['overlay_files']} files, menu entry, records)", "bytes": pkg},
            {"what": "CCS HOME package", "bytes": blocks(home_bytes, block)}]
    rows += [{"what": f"system image {i + 1}", "bytes": blocks(b, block)} for i, b in enumerate(imgs)]
    rows.append({"what": "staging (the hmod is unpacked in RAM; images are moved, not copied)", "bytes": 0})
    rows.append({"what": "rollback (the previous CCS HOME package until the update switches over)", "bytes": blocks(home_bytes, block) if update else 0})
    rows.append({"what": "save reserve (saves, Game Settings and HOME choices)", "bytes": SAVE_RESERVE})
    need = sum(r["bytes"] for r in rows)
    fits = need <= free_bytes
    return {"schema": "ccs-method-capacity/1", "method": method, "block_size": block, "rows": rows, "required_bytes": need, "available_bytes": free_bytes,
            "fits": fits, "shortfall_bytes": 0 if fits else need - free_bytes,
            "summary": f"{mb(need)} needed, {mb(free_bytes)} available" + ("" if fits else f": {mb(need - free_bytes)} more is needed")}

def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--packages", required=True); ap.add_argument("--method", required=True, choices=("mod", "nucleus"))
    ap.add_argument("--free-bytes", required=True, type=int); ap.add_argument("--block-size", type=int, default=4096)
    ap.add_argument("--home-package"); ap.add_argument("--home-bytes", type=int, default=0)
    ap.add_argument("--image", nargs="*", default=[]); ap.add_argument("--image-bytes", nargs="*", type=int, default=[]); ap.add_argument("--update", action="store_true")
    a = ap.parse_args()
    home = a.home_bytes
    if a.home_package: home = int(json.load(open(os.path.join(a.home_package, "MANIFEST.json")))["total_bytes"])
    imgs = [os.path.getsize(f) for f in a.image] + list(a.image_bytes)
    r = compute(a.packages, a.method, a.free_bytes, a.block_size, home, imgs, a.update)
    for row in r["rows"]: print(f"{mb(row['bytes']):>10}  {row['what']}")
    print(r["summary"]); print(json.dumps(r, indent=1))
    return 0 if r["fits"] else 3

if __name__ == "__main__": sys.exit(main())
