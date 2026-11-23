#!/usr/bin/env python3
"""Check the vendored FCEUmm (tools/player/third_party/fceumm) against the upstream commit it is based on.

    python tools/player/scripts/check_fceumm_vendor.py --upstream <a libretro-fceumm git clone that contains the base commit>

Every vendored file must have the content it has at the base commit, except the files the recorded patch series changes, which must
equal the base content with every patch applied in order. Content is compared exactly, or after turning CRLF into LF in both (this
repository stores text with LF; upstream stores some files with CRLF). Upstream files left out of the copy must be in the removed list
(VENDORED.md); files the copy adds must be the recorded metadata. The patch files must have the SHA-256 recorded here and in VENDORED.md,
and the player's core build identity (tools/player/core/gp.h GP_CORE_BUILD) must name the base and this patch set.
Exit status 0 when everything matches."""
import argparse, hashlib, os, re, subprocess, sys, tempfile

BASE = "ac0d00a7fc01166f84c7bac01d1084ebfd2b865e"
PATCHES = [("0001-Mapper-30-bus-conflicts-latch-the-AND-of-the-written.patch", "f844a1eb7b0a573fef99138b3dafbc8c06e7929df9bb15e89a7a6584d40c1088"),
           ("0002-Mapper-30-submapper-4-takes-flash-commands-when-flas.patch", "7a805ec420044a902fd9071b8fddfb74b9662da23dd333ae8fa68b9907bc7768")]
# copied from upstream besides src/
ROOT_COPIED = ["Copying", "Authors", "README.md"]
# added by this repository
ADDED = {"VENDORED.md", "LICENSE.emu2413.txt", "LICENSE.pic16c5x.txt"} | {"patches/" + n for n, _ in PATCHES}
# upstream parts this repository does not compile (VENDORED.md)
REMOVED_PREFIXES = ("src/hdpack/", "src/ntsc/", "src/drivers/libretro/libretro-common/formats/", "src/drivers/libretro/libretro-common/include/formats/",
                    "src/drivers/libretro/libretro-common/include/compat/msvc/", "src/drivers/libretro/libretro-common/streams/trans_stream",
                    "src/drivers/libretro/libretro-common/include/streams/trans_stream.h",
                    "src/drivers/libretro/libretro-common/encodings/encoding_deflate.c")


def patch_set_id() -> str:
    """The patch set's identity: the first 8 hex digits of the SHA-256 of the patches' SHA-256 values, one per line, in series order."""
    return hashlib.sha256("".join(h + "\n" for _, h in PATCHES).encode()).hexdigest()[:8]


def core_build() -> str:
    return BASE[:8] + "+patches." + patch_set_id()


def git(up, *args, data=None) -> bytes:
    return subprocess.run(["git", "-C", up, *args], input=data, capture_output=True, check=True).stdout


def lf(b: bytes) -> bytes:
    return b.replace(b"\r\n", b"\n")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0]); ap.add_argument("--upstream", required=True); a = ap.parse_args()
    repo = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
    vend = os.path.join(repo, "tools", "player", "third_party", "fceumm"); problems = []
    git(a.upstream, "cat-file", "-e", BASE + "^{commit}")
    # the patch files
    for name, want in PATCHES:
        p = os.path.join(vend, "patches", name)
        got = hashlib.sha256(open(p, "rb").read()).hexdigest() if os.path.isfile(p) else "missing"
        if got != want: problems.append(f"patches/{name}: SHA-256 {got}, recorded {want}")
    on_disk = sorted(n for n in os.listdir(os.path.join(vend, "patches")) if not n.startswith(".")) if os.path.isdir(os.path.join(vend, "patches")) else []
    if on_disk != sorted(n for n, _ in PATCHES): problems.append(f"patches/ holds {on_disk}, the recorded series is {[n for n, _ in PATCHES]}")
    vmd = open(os.path.join(vend, "VENDORED.md"), encoding="utf-8").read()
    for name, h in PATCHES:
        if name not in vmd or h not in vmd: problems.append(f"VENDORED.md does not record {name} with its SHA-256")
    if core_build() not in vmd: problems.append(f"VENDORED.md does not name the core build {core_build()}")
    gph = open(os.path.join(repo, "tools", "player", "core", "gp.h"), encoding="utf-8").read()
    m = re.search(r'#define GP_CORE_BUILD "([^"]+)"', gph)
    if not m or m.group(1) != core_build(): problems.append(f"gp.h GP_CORE_BUILD is {m.group(1) if m else 'missing'}, expected {core_build()}")
    # the files the series changes, and their expected content: the base content with every patch applied in order
    affected = []
    for name, _ in PATCHES:
        for line in open(os.path.join(vend, "patches", name), "rb").read().decode("utf-8").splitlines():
            if line.startswith("+++ b/") and line[6:] not in affected: affected.append(line[6:])
    expected = {}
    with tempfile.TemporaryDirectory() as t:
        for f in affected:
            os.makedirs(os.path.dirname(os.path.join(t, f)), exist_ok=True); open(os.path.join(t, f), "wb").write(git(a.upstream, "show", f"{BASE}:{f}"))
        for name, _ in PATCHES:
            r = subprocess.run(["git", "apply", "--unsafe-paths", os.path.join(vend, "patches", name)], cwd=t, capture_output=True, text=True)
            if r.returncode: problems.append(f"patches/{name} does not apply to the base: {r.stderr.strip()[:300]}")
        for f in affected: expected[f] = open(os.path.join(t, f), "rb").read()
    # every upstream file at the base, against the copy
    listing = [l.split("\t", 1) for l in git(a.upstream, "ls-tree", "-r", "--full-tree", BASE).decode("utf-8").splitlines()]
    up_files = {path: meta.split()[2] for meta, path in listing if path.startswith("src/") or path in ROOT_COPIED}
    counts = {"identical": 0, "line endings only": 0, "patched as recorded": 0, "left out (removed list)": 0}
    blobs = git(a.upstream, "cat-file", "--batch", data="".join(sha + "\n" for sha in up_files.values()).encode())
    pos, content = 0, {}
    for path, sha in up_files.items():
        head_end = blobs.index(b"\n", pos); size = int(blobs[pos:head_end].split()[2]); content[path] = blobs[head_end + 1:head_end + 1 + size]; pos = head_end + 1 + size + 1
    for path, base_bytes in content.items():
        mine = os.path.join(vend, *path.split("/"))
        if not os.path.isfile(mine):
            if path.startswith(REMOVED_PREFIXES): counts["left out (removed list)"] += 1
            else: problems.append(f"{path}: missing from the copy and not in the removed list")
            continue
        have = open(mine, "rb").read()
        if path in expected:
            if have == expected[path] or lf(have) == lf(expected[path]): counts["patched as recorded"] += 1
            else: problems.append(f"{path}: differs from the base with the recorded patches applied")
        elif have == base_bytes: counts["identical"] += 1
        elif lf(have) == lf(base_bytes): counts["line endings only"] += 1
        else: problems.append(f"{path}: differs from the base commit (not a recorded patch)")
    # files the copy adds
    for dp, _, fs in os.walk(vend):
        for f in fs:
            rel = os.path.relpath(os.path.join(dp, f), vend).replace(os.sep, "/")
            if rel not in up_files and rel not in ADDED: problems.append(f"{rel}: in the copy but not at the base commit and not recorded metadata")
    for k, v in counts.items(): print(f"{k}: {v}")
    print(f"patch series: {len(PATCHES)} ({', '.join(n for n, _ in PATCHES)}); files changed: {', '.join(affected)}; core build {core_build()}")
    for p in problems: print("PROBLEM:", p)
    print("FCEUmm copy: " + ("matches the base commit and the recorded patches" if not problems else f"{len(problems)} problem(s)"))
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
