#!/usr/bin/env python3
"""Builds the CCS Collection Interface kit's versioned archive and its manifest. Part of the CCS Collection Interface kit.
Copyright (c) 2026 Rance9. MIT License (see LICENSE).

  python3 build_kit.py --out DIR

Writes DIR/ccs-collection-interface-kit-VERSION.zip (every kit file under one folder of that name; the same files always give the same
bytes: fixed times, name order, no build leftovers) and DIR/ccs-collection-interface-kit-VERSION.json, the manifest a downloads page lists:
{"name", "version", "interface": 1, "file", "sha256", "size", "license": "MIT"}."""
from __future__ import annotations
import argparse, hashlib, json, os, sys, zipfile

KIT = os.path.dirname(os.path.abspath(__file__))
NAME = "ccs-collection-interface-kit"
FIXED = (2026, 1, 1, 0, 0, 0)
SKIP_DIRS = {"__pycache__", ".pytest_cache"}

def version() -> str:
    with open(os.path.join(KIT, "VERSION"), encoding="utf-8") as f: v = f.read().strip()
    if not v or any(c not in "0123456789." for c in v) or v.count(".") != 2: raise SystemExit(f"VERSION is not MAJOR.MINOR.PATCH: {v!r}")
    return v

def files() -> list[str]:
    out = []
    for d, ds, fs in os.walk(KIT):
        ds[:] = sorted(x for x in ds if x not in SKIP_DIRS)
        for f in sorted(fs):
            if f.endswith((".pyc", ".pyo")): continue
            out.append(os.path.relpath(os.path.join(d, f), KIT).replace(os.sep, "/"))
    return sorted(out)

def build(out: str) -> dict:
    v = version(); top = f"{NAME}-{v}"; os.makedirs(out, exist_ok=True)
    zp = os.path.join(out, f"{top}.zip")
    with zipfile.ZipFile(zp, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for rel in files():
            with open(os.path.join(KIT, *rel.split("/")), "rb") as f: data = f.read()
            if not rel.endswith((".png", ".zip")): data = data.replace(b"\r\n", b"\n")   # text as the repository keeps it (LF)
            zi = zipfile.ZipInfo(f"{top}/{rel}", FIXED); zi.compress_type = zipfile.ZIP_DEFLATED; zi.create_system = 3
            zi.external_attr = (0o100755 if rel in ("reader/ccs-ci", "build_kit.py", "checker/ccs_ci_check.py", "tests/test_kit.py") else 0o100644) << 16
            z.writestr(zi, data)
    with open(zp, "rb") as f: b = f.read()
    man = {"name": NAME, "version": v, "interface": 1, "file": os.path.basename(zp), "sha256": hashlib.sha256(b).hexdigest(), "size": len(b), "license": "MIT"}
    with open(os.path.join(out, f"{top}.json"), "w", encoding="utf-8", newline="\n") as f: json.dump(man, f, indent=1); f.write("\n")
    return man

def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter); ap.add_argument("--out", required=True)
    m = build(ap.parse_args().out); print(f"{m['file']}: {m['size']} bytes  sha256 {m['sha256']}"); return 0

if __name__ == "__main__": sys.exit(main())
