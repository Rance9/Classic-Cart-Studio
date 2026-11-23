"""Room needed on the console's data storage for CCS Mod, CCS Nucleus (direct) or CCS Nucleus with its chooser, worked out by the shared
storage accounting (tools/companion/console_engine/storage_model.py, tools/platform/ccs-storage-spec.json). Copyright (C) 2026 Rance9,
GPL-3.0-or-later.

  python capacity.py --packages DIR --method mod|nucleus|nucleus-chooser --free-bytes N [--block-size N] [--profile MODEL]
                     [--home-package DIR | --home-bytes N] [--image FILE ... | --image-bytes N ...]
                     [--update [--old-home-package DIR | --old-home-bytes N]]

What is measured and what is not:
  --packages      build_packages.py's output: the exact files each package puts into hakchi's overlay and its menu entry (measured)
  --home-package  CCS HOME's console package: each of its files (its MANIFEST.json), each rounded to whole blocks (measured); --home-bytes
                  gives only a total, so the files' rounding is not known (labeled); 0 when it is installed already
  --image         the owner's system images to import (their file sizes), or --image-bytes (only with the chooser: an image starts
                  only when it is chosen on the TV)
  --free-bytes    the data storage's available space as the console reports it now (the Workbench reads it: df on /var/lib, which
                  already leaves the reserved blocks out; they are not subtracted again)
  --block-size    the data storage's block size as the console reports it (statfs); without it the model's dated reference's block size
                  is used and labeled as such
  --profile       the console model (default nes-classic-edition); a model with no established room for saves cannot be confirmed
What is added:
  package         each package's files in hakchi's overlay and its menu entry; its install records (journal, and for CCS Nucleus its
                  registry and state) as an allowance of one block each (labeled)
  CCS HOME        the HOME package (a new copy beside the one in use)
  system images   each image the owner imports (uploaded once to Nucleus's staging folder, then moved: one rename, no second copy)
  staging         nothing more on the data storage: the hmod is unpacked in RAM and images are moved, not copied
  earlier copy    with --update, the HOME copy in use stays beside the new one; it is already counted in the console's free space reading,
                  so it is shown (its actual size when given) and not charged again
  saves, margin   room kept for saves and a safety margin, from the shared specification
No count of boot choices and no speed follows from this: only the space each actual file takes."""
from __future__ import annotations
import argparse, json, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(os.path.dirname(HERE), "companion"))
from console_engine import storage_model as SM   # noqa: E402  (the shared storage accounting)

RECORDS = 4 * 1024   # an install record (journal, registry, state): an allowance of one block each, labeled as an estimate
PROFILES = {"mod": ("mod",), "nucleus": ("nucleus",), "nucleus-chooser": ("nucleus", "nucleus-chooser")}


def mb(n: int) -> str:
    for unit, d in (("GB", 1e9), ("MB", 1e6), ("KB", 1e3)):
        if abs(n) >= d: return f"{n / d:.1f} {unit}"
    return f"{n} bytes"


def home_files(package: str) -> list[int]:
    """each file of a CCS HOME console package (its MANIFEST.json and the files it lists)"""
    p = os.path.join(package, "MANIFEST.json"); man = json.load(open(p, encoding="utf-8"))
    fs = man.get("files") or {}
    return [os.path.getsize(p)] + [int(f["size"]) for f in (fs.values() if isinstance(fs, dict) else fs)]


def compute(packages: str, method: str, free_bytes: int, block: int | None = None, home_bytes: int = 0, image_bytes: list[int] | None = None,
            update: bool = False, home_sizes: list[int] | None = None, old_home_bytes: int | None = None, profile: str = "nes-classic-edition") -> dict:
    man = json.load(open(os.path.join(packages, "MANIFEST.json")))
    imgs = [int(x) for x in (image_bytes or [])]
    if imgs and method != "nucleus-chooser": raise ValueError("system images are options of CCS Nucleus's chooser")
    items = []
    for k in PROFILES[method]:
        p = man["packages"][k]
        copied = [v["bytes"] for n, v in p["members"].items() if n not in ("install", "uninstall", "readme.md")]
        records = 1 if k == "nucleus-chooser" else 3
        items.append({"category": "package", "label": f"package ({p['file']}: {p['overlay_files']} files, menu entry)", "files": [{"bytes": b} for b in copied + [p["menu_entry_bytes"]]]})
        items.append({"category": "records", "label": f"{p['file']} install records", "estimated": True, "files": [{"bytes": RECORDS}] * records,
                      "what": "an allowance of one block for each install record"})
    if home_sizes: items.append({"category": "home", "label": "CCS HOME package", "files": [{"bytes": b} for b in home_sizes]})
    elif home_bytes: items.append({"category": "home", "label": "CCS HOME package", "estimate": {"logical_bytes": home_bytes, "allocated_bytes": SM.rounded(home_bytes, block or 4096)},
                                   "what": "its total only: the rounding of its files to blocks is not known"})
    items += [{"category": "image", "label": f"system image {i + 1}", "files": [{"bytes": b}]} for i, b in enumerate(imgs)]
    storage = {"source": "measured", "available_bytes": int(free_bytes)}
    if block: storage["block_bytes"] = int(block)
    inp = {"profile": profile, "storage": storage, "items": items}
    a = SM.account(inp)
    rows = [{"what": r["label"] + (" (estimated)" if r["kind"] == "estimate" else " (unknown)" if r["kind"] == "unknown" else ""), "bytes": r["allocated_bytes"]} for r in a["rows"]]
    rows.append({"what": "staging (the hmod is unpacked in RAM; images are moved, not copied)", "bytes": 0})
    rows.append({"what": "earlier CCS HOME copy kept until the update switches over: already counted in the free space reading"
                 + (f" ({mb(old_home_bytes)})" if update and old_home_bytes else "" if update else " (not an update)"), "bytes": 0})
    rows.append({"what": "save reserve (room kept for saves and suspend points)", "bytes": a["saves_allowance_bytes"] or 0})
    rows.append({"what": "safety margin", "bytes": a["margin_bytes"]})
    fits = a["write_ok"]; need = a["peak_bytes"]
    return {"schema": "ccs-method-capacity/2", "method": method, "profile": profile, "block_size": a["block_bytes"], "block_source": a["block_source"], "rows": rows,
            "required_bytes": need, "available_bytes": free_bytes, "fits": fits, "status": a["status"], "unknown": a["unknown"],
            "shortfall_bytes": 0 if fits or a["status"] == "unconfirmed" else need - free_bytes,
            "account": {"input": inp, "result": a},
            "summary": f"{mb(need)} needed, {mb(free_bytes)} available" + ("" if fits else f": {mb(need - free_bytes)} more is needed" if a["status"] != "unconfirmed" else ": the fit cannot be confirmed")}


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--packages", required=True); ap.add_argument("--method", required=True, choices=tuple(PROFILES))
    ap.add_argument("--free-bytes", required=True, type=int); ap.add_argument("--block-size", type=int, default=None); ap.add_argument("--profile", default="nes-classic-edition")
    ap.add_argument("--home-package"); ap.add_argument("--home-bytes", type=int, default=0)
    ap.add_argument("--image", nargs="*", default=[]); ap.add_argument("--image-bytes", nargs="*", type=int, default=[]); ap.add_argument("--update", action="store_true")
    ap.add_argument("--old-home-package"); ap.add_argument("--old-home-bytes", type=int, default=None)
    a = ap.parse_args()
    sizes = home_files(a.home_package) if a.home_package else None
    old = sum(home_files(a.old_home_package)) if a.old_home_package else a.old_home_bytes
    imgs = [os.path.getsize(f) for f in a.image] + list(a.image_bytes)
    r = compute(a.packages, a.method, a.free_bytes, a.block_size, a.home_bytes, imgs, a.update, sizes, old, a.profile)
    for row in r["rows"]: print(f"{mb(row['bytes']):>10}  {row['what']}")
    print(r["summary"]); print(json.dumps({k: v for k, v in r.items() if k != "account"}, indent=1))
    return 0 if r["fits"] else 3


if __name__ == "__main__": sys.exit(main())
