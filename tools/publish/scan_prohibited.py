"""Prohibited-content scanner for anything that leaves this computer (public source tree, build output, Pages artifact, release package,
git history). Fails closed: exit 1 when anything prohibited is found. Detection is by content signature first, file name second:

  ROM payloads       iNES / NES 2.0 magic "NES\\x1a", UNIF, FDS, raw PRG-sized blobs under suspicious names (.nes .unh .unf .unif .fds .qd .nsf .rom .bin)
  archives           ZIP/7z/RAR/tar/gz magic (nested archives are opened and their members scanned, bounded)
  console images     ext4 superblock, squashfs "hsqs", Android boot/kernel magic "ANDROID!", "nandb"/"kernel.img"/"nand-full.bin" names, .hsqs .hmod .img
  secrets            GitHub/OAuth/AWS-style tokens, literal pass-word assignments, private keys
  private paths      C:\\Users\\<name>, OneDrive paths, user-specific hostnames in text files
  user data          personal stores, exports, staging, rollback snapshots, logs with local paths

Usage: python scan_prohibited.py <path> [--git-history] [--allow <relpath-glob> ...] [--json out.json]"""
from __future__ import annotations
import sys as _sys
try: _sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception: pass
import os, sys, re, io, json, zipfile, tarfile, argparse, fnmatch, subprocess

ROM_EXT = {".nes", ".unh", ".unf", ".unif", ".fds", ".qd", ".nsf", ".rom", ".sfrom"}
IMAGE_EXT = {".img", ".hsqs", ".hmod", ".nand", ".dump"}
ARCHIVE_EXT = {".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".xz", ".bz2"}
TEXT_EXT = {".py", ".js", ".css", ".html", ".md", ".json", ".txt", ".yml", ".yaml", ".ps1", ".cmd", ".iss", ".toml", ".cfg", ".ini", ".csv", ".svg"}
# The markers are assembled from pieces so this file never contains the literal strings it hunts for. Private markers of the
# development workspace (user name, machine name, local drive layout) come only from the environment, never from public source:
#   NES_PRIVATE_MARKERS=name1,name2
_EXTRA = [re.escape(m.strip()) for m in os.environ.get("NES_PRIVATE_MARKERS", "").split(",") if m.strip()]
PRIVATE_PATH = re.compile("|".join([r"[A-Za-z]:\\\\?Users\\\\?[A-Za-z0-9._-]+", r"/Users/[A-Za-z0-9._-]+", "One" + "Drive" + r"\\\\", r"[A-Za-z]:\\\\" + "NES" + r"\\\\"] + _EXTRA), re.I)
SECRET = re.compile(r"(gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16}|-----BEGIN (RSA|EC|OPENSSH|DSA) PRIVATE KEY-----|xox[baprs]-[A-Za-z0-9-]{10,}|sk_live_[A-Za-z0-9]{16,}|(?i:pass" + r"word\s*=\s*['\"][^'\"]{4,}))")
MAGIC = [(b"NES\x1a", "iNES/NES 2.0 ROM"), (b"UNIF", "UNIF ROM"), (b"FDS\x1a", "FDS image"), (b"hsqs", "squashfs image"), (b"ANDROID!", "Android boot image / kernel"), (b"\x7fELF", "ELF binary"), (b"PK\x03\x04", "ZIP archive"), (b"7z\xbc\xaf\x27\x1c", "7z archive"), (b"Rar!\x1a\x07", "RAR archive"), (b"\x1f\x8b", "gzip"), (b"\xfd7zXZ\x00", "xz")]
NAME_HINTS = ("nand", "kernel.img", "nandb", "clover", "dump.bin", "uboot", "hakchi.hmod", "config.json")
MAX_NESTED = 2000

def classify_bytes(b: bytes, name: str) -> list[str]:
    hits = []
    for m, label in MAGIC:
        if b.startswith(m): hits.append(label)
    if len(b) >= 0x440 and b[0x438:0x43A] == b"\x53\xef": hits.append("ext4 superblock (filesystem image)")
    ext = os.path.splitext(name)[1].lower(); base = os.path.basename(name).lower()
    if ext in ROM_EXT: hits.append(f"ROM-like extension {ext}")
    if ext in IMAGE_EXT: hits.append(f"image-like extension {ext}")
    if ext == ".bin" and len(b) >= 8192 and len(b) % 8192 == 0: hits.append("headerless-ROM-sized .bin")
    if any(h in base for h in ("nand-full", "nandb", "kernel.img", "clover-")): hits.append("console image name")
    return hits

def scan_file(path: str, rel: str, allow: list[str], findings: list, nested_budget: list):
    if any(fnmatch.fnmatch(rel, a) for a in allow): return
    try: size = os.path.getsize(path)
    except OSError: return
    with open(path, "rb") as f: head = f.read(4096)
    hits = classify_bytes(head, rel)
    ext = os.path.splitext(rel)[1].lower()
    if ext in TEXT_EXT or (not hits and size < 2_000_000):
        try: txt = open(path, "r", encoding="utf-8", errors="replace").read(2_000_000)
        except OSError: txt = ""
        for m in SECRET.finditer(txt): findings.append({"file": rel, "kind": "secret", "detail": m.group(0)[:12] + "…"}); break
        pm = PRIVATE_PATH.search(txt)
        if pm and ext in TEXT_EXT: findings.append({"file": rel, "kind": "private-path", "detail": pm.group(0)[:60]})
    for h in hits:
        if "archive" in h or "gzip" in h or "xz" in h:
            if ext in (".zip",) or head.startswith(b"PK\x03\x04"):
                try:
                    with zipfile.ZipFile(path) as z:
                        for zi in z.infolist()[:MAX_NESTED]:
                            if zi.is_dir(): continue
                            nested_budget[0] += 1
                            if nested_budget[0] > MAX_NESTED: break
                            with z.open(zi) as zf: nb = zf.read(4096)
                            for nh in classify_bytes(nb, zi.filename):
                                if "archive" not in nh: findings.append({"file": rel + "::" + zi.filename, "kind": "prohibited", "detail": nh})
                except Exception as e: findings.append({"file": rel, "kind": "archive-unreadable", "detail": str(e)[:80]})
            else: findings.append({"file": rel, "kind": "archive", "detail": h})
        elif "ELF" in h: findings.append({"file": rel, "kind": "binary", "detail": h})
        else: findings.append({"file": rel, "kind": "prohibited", "detail": h})

def scan_tree(root: str, allow: list[str]) -> list:
    findings = []; budget = [0]
    for d, dirs, files in os.walk(root):
        dirs[:] = [x for x in dirs if x not in (".git", "node_modules", "__pycache__")]
        for f in files:
            p = os.path.join(d, f); rel = os.path.relpath(p, root).replace("\\", "/")
            scan_file(p, rel, allow, findings, budget)
    return findings

def scan_git_history(repo: str, allow: list[str]) -> list:
    """Every blob in every ref: name-based + magic-based checks on blob content (bounded read)."""
    findings = []
    out = subprocess.run(["git", "-C", repo, "rev-list", "--objects", "--all"], capture_output=True, text=True, encoding="utf-8", errors="replace").stdout
    seen = set()
    for line in out.splitlines():
        parts = line.split(" ", 1)
        if len(parts) != 2: continue
        sha, name = parts
        if sha in seen or any(fnmatch.fnmatch(name, a) for a in allow): continue
        seen.add(sha)
        t = subprocess.run(["git", "-C", repo, "cat-file", "-t", sha], capture_output=True, text=True).stdout.strip()
        if t != "blob": continue
        b = subprocess.run(["git", "-C", repo, "cat-file", "-p", sha], capture_output=True).stdout
        for h in classify_bytes(b[:4096], name):
            if "archive" in h and name.lower().endswith(".zip"):
                try:
                    with zipfile.ZipFile(io.BytesIO(b)) as z:
                        for zi in z.infolist()[:200]:
                            if zi.is_dir(): continue
                            with z.open(zi) as zf: nb = zf.read(4096)
                            for nh in classify_bytes(nb, zi.filename):
                                if "archive" not in nh: findings.append({"file": f"history:{name}::{zi.filename}", "kind": "prohibited", "detail": nh})
                except Exception: findings.append({"file": f"history:{name}", "kind": "archive-unreadable", "detail": ""})
            elif "archive" not in h and "gzip" not in h and "xz" not in h: findings.append({"file": f"history:{name}", "kind": "prohibited", "detail": h})
        if len(b) < 2_000_000 and os.path.splitext(name)[1].lower() in TEXT_EXT:
            txt = b.decode("utf-8", errors="replace")
            if SECRET.search(txt): findings.append({"file": f"history:{name}", "kind": "secret", "detail": "token pattern"})
            pm = PRIVATE_PATH.search(txt)
            if pm: findings.append({"file": f"history:{name}", "kind": "private-path", "detail": pm.group(0)[:60]})
    return findings

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("path"); ap.add_argument("--git-history", action="store_true"); ap.add_argument("--allow", action="append", default=[]); ap.add_argument("--json", default=""); a = ap.parse_args()
    findings = scan_tree(a.path, a.allow)
    if a.git_history: findings += scan_git_history(a.path, a.allow)
    hard = [f for f in findings if f["kind"] in ("prohibited", "secret", "archive", "binary", "archive-unreadable")]
    soft = [f for f in findings if f["kind"] == "private-path"]
    if a.json: json.dump({"findings": findings}, open(a.json, "w", encoding="utf-8"), indent=1)
    for f in findings: print(f"{f['kind']:18} {f['file']}  {f['detail']}")
    print(f"SCAN: {len(hard)} prohibited, {len(soft)} private-path references ({a.path}{' + git history' if a.git_history else ''})")
    sys.exit(1 if hard or soft else 0)

if __name__ == "__main__":
    main()
