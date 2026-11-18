"""The build environment is exactly the one in requirements-build.txt: the Python version named on its "# python==" line and every
package at its pinned version (the release workflow, the website workflow and the local candidate builds and release preflight run this
after installing the requirements, so a local build and a CI build package the same runtime). Prints what it found; exit 1 on any
difference. pywin32-ctypes and pefile matter only on Windows but are pinned everywhere (pip installs them on every platform).
  python tools/publish/check_build_env.py
Part of Classic Cart Studio: GNU GPL v3 or later."""
from __future__ import annotations
import importlib.metadata as md, json, os, platform, re, sys

REQ = os.path.join(os.path.dirname(os.path.abspath(__file__)), "requirements-build.txt")

def pins() -> tuple[str, dict]:
    py, pk = "", {}
    for line in open(REQ, encoding="utf-8"):
        m = re.match(r"#\s*python==(\d+\.\d+\.\d+)\s*$", line)
        if m: py = m.group(1); continue
        line = line.split("#", 1)[0].strip()
        if not line: continue
        m = re.fullmatch(r"([A-Za-z0-9_.-]+)==([A-Za-z0-9_.+-]+)", line)
        if not m: raise SystemExit(f"requirements-build.txt: not an exact pin: {line!r}")
        pk[m.group(1).lower()] = m.group(2)
    if not py or not pk: raise SystemExit("requirements-build.txt names no Python version or no packages")
    return py, pk

def main() -> int:
    py, pk = pins(); have_py = platform.python_version(); problems = []
    if have_py != py: problems.append(f"Python {have_py}, not {py}")
    found = {}
    for name, v in pk.items():
        try: found[name] = md.version(name)
        except md.PackageNotFoundError: found[name] = None
        if found[name] != v: problems.append(f"{name} {found[name] or 'missing'}, not {v}")
    print(json.dumps({"python": have_py, "implementation": platform.python_implementation(), "executable_bits": 64 if sys.maxsize > 2**32 else 32,
                      "packages": found, "result": "OK" if not problems else "DIFFERENT", "problems": problems}, indent=1))
    return 0 if not problems else 1

if __name__ == "__main__": sys.exit(main())
