"""Classic Cart Studio for Windows carries CCS Console Workbench's own released program, byte for byte. Copyright (C) 2026 Rance9, GPL-3.0-or-later.

  python check_shared_engine.py --host <build_host.sh's out folder> --bundled <the app's folder> [--managed <managed CCS-Console-Workbench.exe>]
                                [--out shared-engine.json]

Since 0.11.4 Studio does not run the Workbench in its own process: its installation carries the single-file Workbench program the standalone
release publishes (workbench\\CCS-Console-Workbench.exe beside ClassicCartStudio.exe; Setup installs the x64 or the Arm64 one) and starts it.
This proves, from the files themselves:
  - the bundled program in the app's folder (--bundled: <folder>\\workbench\\CCS-Console-Workbench.exe, the copy the self-test uses) is byte
    for byte the x64 single-file host this build made (--host: x64\\ and arm64\\CCS-Console-Workbench.exe and build-record.json); the
    installer takes both architectures' files from the host folder itself;
  - each host's size and SHA-256 are the ones its build record names, and the record's payload is the managed program given (--managed),
    when one is given;
  - the bundled x64 program answers --engine-info (read only, no window: its output is read through redirected standard output) with the four
    shared libraries and the five console programs (backup, restore, preparation, rescue and files), and, with --managed, the same libraries
    and console programs as the managed program it carries.
The report keeps the earlier shape where it can: "app" and "workbench" are the bundled program's --engine-info (the publication step compares a
downloaded standalone program's --engine-info with it), "bundle" names both architectures' files. Run it on the unsigned build. Exit 0 when
everything holds."""
from __future__ import annotations
import argparse, hashlib, json, os, subprocess, sys

SHARED = ("CcsBackupUi.dll", "CcsBackup.dll", "CcsConsole.dll", "LibUsbDotNet.LibUsbDotNet.dll")
IMAGES = ("ccs-backup", "ccs-restore", "ccs-prepare", "ccs-rescue", "ccs-files")
EXE = "CCS-Console-Workbench.exe"; FOLDER = "workbench"

def sha(p: str) -> str:
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""): h.update(b)
    return h.hexdigest()

def file_rec(p: str) -> dict: return {"bytes": os.path.getsize(p), "sha256": sha(p)}

def report(exe: str) -> dict:
    # the single-file host is a Windows (GUI) program: its answer comes through the redirected standard output it inherits
    r = subprocess.run([exe, "--engine-info"], stdin=subprocess.DEVNULL, capture_output=True, text=True, timeout=300, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    if r.returncode != 0: raise SystemExit(f"{exe} --engine-info failed ({r.returncode}): {r.stderr[-400:]}")
    try: return json.loads(r.stdout)
    except ValueError: raise SystemExit(f"{exe} --engine-info did not answer with a report: {r.stdout[:400]!r}")

def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", required=True); ap.add_argument("--bundled", required=True); ap.add_argument("--managed"); ap.add_argument("--out")
    a = ap.parse_args()
    problems: list[str] = []
    rec = json.load(open(os.path.join(a.host, "build-record.json"), encoding="utf-8"))
    outs = {o.get("arch"): o for o in rec.get("outputs", [])}
    bundle = {"workbench_version": rec.get("version"), "build_record_payload": rec.get("payload")}
    for arch in ("x64", "arm64"):
        p = os.path.join(a.host, arch, EXE)
        if not os.path.isfile(p): problems.append(f"{arch}: {p} is missing"); continue
        f = file_rec(p); bundle[arch] = dict(f, file=f"{arch}/{EXE}")
        o = outs.get(arch) or {}
        if o.get("sha256") != f["sha256"] or o.get("bytes") != f["bytes"]: problems.append(f"{arch}: the host differs from its build record ({o.get('sha256')} / {f['sha256']})")
    bundled = os.path.join(a.bundled, FOLDER, EXE); app: dict = {}
    if not os.path.isfile(bundled): problems.append(f"the app's folder has no {FOLDER}\\{EXE}")
    else:
        b = file_rec(bundled); bundle["bundled_x64"] = dict(b, file=f"{FOLDER}/{EXE}")
        if b != {k: bundle.get("x64", {}).get(k) for k in ("bytes", "sha256")}: problems.append(f"the bundled program is not byte for byte the x64 single-file host ({b['sha256'][:12]} / {bundle.get('x64', {}).get('sha256', '')[:12]})")
        app = report(bundled)
    libs, imgs = app.get("libraries") or {}, app.get("recovery_images") or {}
    for n in SHARED:
        if not (libs.get(n) or {}).get("sha256"): problems.append(f"{n}: missing in the bundled program")
    for v in IMAGES:
        if not imgs.get(v): problems.append(f"{v}: the bundled program carries no such console program")
    managed = None
    if a.managed:
        m = file_rec(a.managed); managed = dict(m, file=os.path.basename(a.managed))
        pl = rec.get("payload") or {}
        if pl.get("sha256") != m["sha256"] or pl.get("bytes") != m["bytes"]: problems.append(f"the hosts' payload is not the managed program given ({pl.get('sha256')} / {m['sha256']})")
        wm = report(a.managed)
        for n in SHARED:
            if (libs.get(n) or {}) != ((wm.get("libraries") or {}).get(n) or {}): problems.append(f"{n}: differs between the bundled program and the managed program")
        if imgs != (wm.get("recovery_images") or {}): problems.append("the console programs differ between the bundled program and the managed program")
        if (app.get("boundaries"), app.get("operations")) != (wm.get("boundaries"), wm.get("operations")): problems.append("the boundaries or the operations each console allows differ between the bundled program and the managed program")
    out = {"schema": "ccs-shared-engine-check/2", "ok": not problems, "problems": problems, "app": app, "workbench": app, "bundle": bundle, "managed": managed}
    if a.out: json.dump(out, open(a.out, "w", encoding="utf-8"), indent=1)
    for arch in ("x64", "arm64"): print(f"{arch:8} {bundle.get(arch, {}).get('sha256')}  {bundle.get(arch, {}).get('bytes')} B")
    print(f"bundled  {bundle.get('bundled_x64', {}).get('sha256')}")
    for n in SHARED: print(f"{n:32} {(libs.get(n) or {}).get('sha256')}")
    for v, i in sorted(imgs.items()): print(f"{v:32} {i.get('image_id') if isinstance(i, dict) else i}")
    for p in problems: print("PROBLEM", p)
    print("SHARED ENGINE OK: Classic Cart Studio for Windows carries the Workbench's single-file program byte for byte, with its libraries and console programs"
          if not problems else "SHARED ENGINE CHECK FAILED")
    return 0 if not problems else 1

if __name__ == "__main__": sys.exit(main())
