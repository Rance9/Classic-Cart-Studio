"""Both hosts carry the same CCS Console Workbench engine. Copyright (C) 2026 Rance9, GPL-3.0-or-later.

  python check_shared_engine.py --app <the Windows app's folder> --utility <CCS-Console-Workbench.exe> [--out report.json]

Runs "--engine-info" in Classic Cart Studio for Windows (ClassicCartStudio.exe in --app: the libraries beside it) and in the standalone
CCS Console Workbench (the copies inside it) and requires: the same four shared libraries, byte for byte (SHA-256 and size), and the same
console programs in both (backup, restore, preparation, rescue and files: image id and SHA-256), since every Workbench function runs in
either host. Run it on the unsigned build (signing changes the files beside the app, not the copies inside the standalone). Exit 0 when
they match; the report names every library and image either way. (--utility keeps its earlier name for the release workflow.)"""
from __future__ import annotations
import argparse, json, os, subprocess, sys

SHARED = ("CcsBackupUi.dll", "CcsBackup.dll", "CcsConsole.dll", "LibUsbDotNet.LibUsbDotNet.dll")
IMAGES = ("ccs-backup", "ccs-restore", "ccs-prepare", "ccs-rescue", "ccs-files")

def report(exe: str) -> dict:
    r = subprocess.run([exe, "--engine-info"], capture_output=True, text=True, timeout=120)
    if r.returncode != 0: raise SystemExit(f"{exe} --engine-info failed ({r.returncode}): {r.stderr[-400:]}")
    return json.loads(r.stdout)

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--app", required=True); ap.add_argument("--utility", required=True); ap.add_argument("--out"); a = ap.parse_args()
    app = report(os.path.join(a.app, "ClassicCartStudio.exe")); wb = report(a.utility)
    problems = []
    for n in SHARED:
        x, y = (app.get("libraries") or {}).get(n), (wb.get("libraries") or {}).get(n)
        if not x or not y: problems.append(f"{n}: missing in {'the Windows app' if not x else 'the standalone Workbench'}")
        elif x != y: problems.append(f"{n}: differs (app {x['sha256'][:12]} {x['bytes']} B, standalone {y['sha256'][:12]} {y['bytes']} B)")
    ai, wi = app.get("recovery_images") or {}, wb.get("recovery_images") or {}
    for v in IMAGES:
        if not ai.get(v) or ai.get(v) != wi.get(v): problems.append(f"{v}: not the same console program in both ({ai.get(v)} / {wi.get(v)})")
    for v in sorted(set(ai) ^ set(wi)): problems.append(f"{v}: carried by only one host")
    out = {"schema": "ccs-shared-engine-check/1", "ok": not problems, "problems": problems, "app": app, "workbench": wb}
    if a.out: json.dump(out, open(a.out, "w", encoding="utf-8"), indent=1)
    for n in SHARED: print(f"{n:32} {(app.get('libraries') or {}).get(n, {}) and app['libraries'][n]['sha256']}")
    for v, i in sorted(ai.items()): print(f"{v:32} {i.get('image_id')} {'(both hosts)' if wi.get(v) == i else '(app only)'}")
    for p in problems: print("PROBLEM", p)
    print("SHARED ENGINE OK: the Windows app and the standalone Workbench carry the same libraries and console programs" if not problems else "SHARED ENGINE CHECK FAILED")
    return 0 if not problems else 1

if __name__ == "__main__": sys.exit(main())
