"""The official Hakchi2 CE 3.9.3 portable package (with its console USB driver), for bundling with the Windows application.

Downloads the pinned release asset (tools/companion/hakchi_bridge.py HAKCHI_PACKAGE: URL, size, SHA-256), refuses any file that
does not match, and writes CCS_HAKCHI_PACKAGE=<path> to $GITHUB_ENV (or prints it) for the PyInstaller step (companion.spec).

  python fetch_hakchi_package.py <out-dir>"""
from __future__ import annotations
import hashlib, os, sys, urllib.request
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path[:0] = [os.path.join(REPO, "tools", "companion"), os.path.join(REPO, "tools", "rom-pipeline")]
from hakchi_bridge import HAKCHI_PACKAGE as P   # noqa: E402

def main(out: str) -> int:
    out = os.path.abspath(out); os.makedirs(out, exist_ok=True); dest = os.path.join(out, P["name"]); part = dest + ".part"   # absolute: PyInstaller resolves spec data paths from the spec's folder
    req = urllib.request.Request(P["url"], headers={"User-Agent": "classic-cart-studio-release"})
    with urllib.request.urlopen(req, timeout=120) as r, open(part, "wb") as f:
        for chunk in iter(lambda: r.read(1 << 20), b""): f.write(chunk)
    size = os.path.getsize(part); h = hashlib.sha256(open(part, "rb").read()).hexdigest()
    if size != P["size"] or h != P["sha256"]:
        os.remove(part); print(f"refused: size {size} (expected {P['size']}), SHA-256 {h} (expected {P['sha256']})"); return 1
    os.replace(part, dest); line = f"CCS_HAKCHI_PACKAGE={dest}"
    if os.environ.get("GITHUB_ENV"):
        with open(os.environ["GITHUB_ENV"], "a", encoding="utf-8") as f: f.write(line + "\n")
    print(f"verified {P['name']} ({size} bytes, SHA-256 {h})"); print(line); return 0

if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else os.path.join("build", "packages")))
