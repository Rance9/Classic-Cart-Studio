"""The browser game player (W1) for the website build: tools/player/scripts/build_player.sh web, compiled by the pinned zig
(tools/console-simulator/toolchain.lock.json: version, URL and SHA-256 of the archive; FCEUmm is vendored).
  Linux (GitHub Actions): the zig archive is downloaded into a cache folder, checked against its SHA-256 (a mismatch stops the build),
  unpacked and used. Windows (the private release preflight on this PC): the same build inside WSL, with the toolchain wsl_setup.sh
  installed there (nothing is downloaded).
  python tools/publish/build_web_player.py <out dir>      -> <out dir>/ccs-player.wasm"""
from __future__ import annotations
import hashlib, json, os, subprocess, sys, tarfile, tempfile, urllib.request
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.normpath(os.path.join(HERE, "..", ".."))

def wsl_path(p: str) -> str:
    p = os.path.abspath(p); return "/mnt/" + p[0].lower() + p[2:].replace("\\", "/")

def main(out: str) -> int:
    out = os.path.abspath(out); os.makedirs(out, exist_ok=True)
    if os.name == "nt":
        cmd = f"cd '{wsl_path(REPO)}' && bash tools/player/scripts/build_player.sh '{wsl_path(out)}' web"
        return subprocess.run(["wsl", "bash", "-lc", cmd]).returncode
    lock = json.load(open(os.path.join(REPO, "tools", "console-simulator", "toolchain.lock.json"), encoding="utf-8"))["zig"]
    url, sha, ver = lock["linux_x86_64"]["url"], lock["linux_x86_64"]["sha256"], lock["version"]
    tc = os.environ.get("CCS_SIM_TOOLCHAIN") or os.path.join(os.environ.get("RUNNER_TEMP") or tempfile.gettempdir(), "ccs-toolchain"); zd = os.path.join(tc, "zig")
    if not os.path.isfile(os.path.join(zd, "zig")):
        arc = os.path.join(tc, f"zig-x86_64-linux-{ver}.tar.xz"); os.makedirs(tc, exist_ok=True)
        if not os.path.isfile(arc): urllib.request.urlretrieve(url, arc + ".part"); os.replace(arc + ".part", arc)
        got = hashlib.sha256(open(arc, "rb").read()).hexdigest()
        if got != sha: os.remove(arc); print(f"zig archive SHA-256 MISMATCH ({got}): refusing to build"); return 2
        print("zig archive SHA-256 verified", got)
        tmp = zd + ".tmp"; os.makedirs(tmp, exist_ok=True)
        with tarfile.open(arc) as t:
            top = t.getmembers()[0].name.split("/")[0]
            t.extractall(tmp, filter="tar")
        os.replace(os.path.join(tmp, top), zd); os.rmdir(tmp)
    env = dict(os.environ, CCS_SIM_TOOLCHAIN=tc)
    return subprocess.run(["bash", os.path.join(REPO, "tools", "player", "scripts", "build_player.sh"), out, "web"], env=env).returncode

if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(REPO, "build", "web-player")))
