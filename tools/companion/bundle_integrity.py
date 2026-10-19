"""Integrity of the files installed with Classic Cart Studio for Windows (the PyInstaller bundle, <app>\\_internal).

Everything the application needs ships in its installer: the Hakchi2 CE 3.9.3 package (console tools and the console USB driver),
the native HOME runtime for the console, the Windows preview renderer, the interface and game library, fonts and helper scripts.
Nothing is ever downloaded at run time. The release build writes bundle-manifest.json (every bundled file with its size and SHA-256,
build_manifest() below, called from companion.spec); the installed application checks itself against it:

  quick   every listed file is present with its recorded size (cheap; runs with every status request, cached)
  full    SHA-256 of the files (on request, and for one component right before it is used: require())

A missing or damaged component is reported with a plain instruction to repair the installation (run the installer again). There is
no fallback to another copy and no download. A development checkout (not frozen) has no manifest and is reported as such."""
from __future__ import annotations
import hashlib, json, os, sys, threading, time

FROZEN = bool(getattr(sys, "frozen", False))
BUNDLE = getattr(sys, "_MEIPASS", os.path.dirname(os.path.abspath(sys.executable if FROZEN else __file__)))
MANIFEST_NAME = "bundle-manifest.json"
SCHEMA = 1
REPAIR = ("Classic Cart Studio needs to be repaired: run the Classic Cart Studio installer again (your games, collections and settings "
          "are kept). Nothing is downloaded to replace it.")

# required components: key -> (label, published paths or prefixes inside the bundle). Every path listed in the manifest belongs to one.
COMPONENTS = {
    "hakchi": ("Hakchi2 CE 3.9.3 package (console tools and the console USB driver)", ["packages/hakchi2-ce-3.9.3-portable.zip"]),
    "native-runtime": ("Native HOME runtime for the console (32-bit ARM)", ["native/ccs-home-armv7"]),
    "preview-core": ("Native HOME renderer for Preview on TV (Windows)", ["native/ccs-sim-core.exe"]),
    "interface": ("Interface, game library and artwork", ["site/"]),
    "fonts": ("HOME fonts", ["native_home/fonts/"]),
    "scripts": ("Hakchi helper scripts", ["Read-HakchiSelection.ps1", "Get-ProcessWindows.ps1", "Close-Hakchi.ps1"]),
    "notices": ("License notices", ["THIRD_PARTY_NOTICES.md"]),
}
# files that must be in every release build (companion.spec refuses to build without them)
REQUIRED_FILES = ["packages/hakchi2-ce-3.9.3-portable.zip", "native/ccs-home-armv7", "native/ccs-sim-core.exe", "site/index.html", "site/catalog.json",
                  "site/library.json", "site/builtin.json", "site/brand/home/home-art.json", "Read-HakchiSelection.ps1", "Get-ProcessWindows.ps1",
                  "Close-Hakchi.ps1", "THIRD_PARTY_NOTICES.md"]


def sha256(path: str) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""): h.update(chunk)
    return h.hexdigest()


def component_of(rel: str) -> str:
    for key, (_, paths) in COMPONENTS.items():
        if any(rel == p or (p.endswith("/") and rel.startswith(p)) for p in paths): return key
    return "other"


def build_manifest(datas: list[tuple[str, str]], version: str = "") -> dict:
    """the manifest for a release build: datas are PyInstaller (source, destination folder) pairs (files or folders)"""
    files = {}
    for src, dest in datas:
        if os.path.isdir(src):
            for d, _, names in os.walk(src):
                for n in names:
                    p = os.path.join(d, n); rel = os.path.join(dest, os.path.relpath(p, src)).replace("\\", "/").lstrip("./")
                    files[rel] = {"size": os.path.getsize(p), "sha256": sha256(p)}
        elif os.path.isfile(src):
            rel = (os.path.basename(src) if dest in (".", "") else dest.rstrip("/\\") + "/" + os.path.basename(src)).replace("\\", "/")
            files[rel] = {"size": os.path.getsize(src), "sha256": sha256(src)}
    missing = [r for r in REQUIRED_FILES if r not in files]
    return {"schema": SCHEMA, "version": version, "files": dict(sorted(files.items())), "required_missing": missing}


_lock = threading.Lock()
_cache: dict = {"quick": None, "at": 0.0, "full": {}}


def manifest(bundle: str | None = None) -> dict | None:
    try:
        with open(os.path.join(bundle or BUNDLE, MANIFEST_NAME), encoding="utf-8") as f: m = json.load(f)
        return m if isinstance(m, dict) and isinstance(m.get("files"), dict) else None
    except (OSError, ValueError): return None


def _report(problems: list[dict], checked: int, mode: str, dev: bool = False) -> dict:
    comps = {}
    for p in problems: comps.setdefault(p["component"], []).append(p)
    items = [{"component": k, "label": COMPONENTS.get(k, ("Other application files", []))[0], "problems": len(v),
              "examples": [f"{x['path']} ({x['state']})" for x in v[:3]]} for k, v in comps.items()]
    ok = not problems
    msg = ("the installation is complete" if ok else
           "Some files installed with Classic Cart Studio are missing or damaged: " + "; ".join(i["label"] for i in items) + ". " + REPAIR)
    return {"ok": ok, "mode": mode, "checked": checked, "components": items, "message": msg, "repair_required": not ok, "development": dev}


def check(full: bool = False, bundle: str | None = None, force: bool = False) -> dict:
    """the installation's integrity; quick results are cached for 60 s, full results per manifest file state"""
    if not FROZEN and bundle is None:
        return {"ok": True, "mode": "development", "checked": 0, "components": [], "message": "development checkout: components come from the build folders",
                "repair_required": False, "development": True}
    root = bundle or BUNDLE
    if not full and not force:
        with _lock:
            if _cache["quick"] and time.time() - _cache["at"] < 60 and _cache.get("root") == root: return _cache["quick"]
    m = manifest(root)
    if m is None:
        r = _report([{"component": "other", "path": MANIFEST_NAME, "state": "missing"}], 0, "full" if full else "quick")
    else:
        problems = []
        for rel, rec in m["files"].items():
            p = os.path.join(root, *rel.split("/"))
            try: size = os.path.getsize(p)
            except OSError: problems.append({"component": component_of(rel), "path": rel, "state": "missing"}); continue
            if size != rec.get("size"): problems.append({"component": component_of(rel), "path": rel, "state": "damaged"}); continue
            if full and sha256(p) != rec.get("sha256"): problems.append({"component": component_of(rel), "path": rel, "state": "damaged"})
        r = _report(problems, len(m["files"]), "full" if full else "quick")
    if not full:
        with _lock: _cache.update(quick=r, at=time.time(), root=root)
    return r


class ComponentError(RuntimeError):
    """a required bundled component is missing or damaged: the installation must be repaired (never downloaded or substituted)"""
    def __init__(self, key: str, detail: str):
        self.key = key; self.label = COMPONENTS.get(key, (key, []))[0]
        super().__init__(f"{self.label}: {detail}. {REPAIR}")


def require(key: str, bundle: str | None = None) -> list[str]:
    """verify one component by SHA-256 against the manifest right before it is used; returns its absolute paths.
    Development checkouts (not frozen, no bundle given) are not checked here."""
    if not FROZEN and bundle is None: return []
    root = bundle or BUNDLE; m = manifest(root)
    if m is None: raise ComponentError(key, f"the installation's file list ({MANIFEST_NAME}) is missing")
    rels = [r for r in m["files"] if component_of(r) == key]
    if not rels: raise ComponentError(key, "it is not part of this installation")
    out = []
    for rel in rels:
        p = os.path.join(root, *rel.split("/")); rec = m["files"][rel]
        try: st = os.stat(p)
        except OSError: raise ComponentError(key, f"{rel} is missing") from None
        ck = (p, st.st_size, int(st.st_mtime))
        if _cache["full"].get(ck) is not True:
            if st.st_size != rec.get("size") or sha256(p) != rec.get("sha256"): raise ComponentError(key, f"{rel} is damaged (it does not match the installed file list)")
            _cache["full"][ck] = True
        out.append(p)
    return out


if __name__ == "__main__":   # python bundle_integrity.py <bundle folder> [--full]
    print(json.dumps(check(full="--full" in sys.argv, bundle=sys.argv[1] if len(sys.argv) > 1 and not sys.argv[1].startswith("--") else None), indent=1))
