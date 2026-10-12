"""Build the PUBLIC repository tree (allow-list driven) and the PUBLIC site from this private development workspace.

Nothing is copied unless it is named here. The result is scanned by scan_prohibited.py before anything is committed or published.

  python build_public_repo.py --repo publish/repo --site build/site [--version 0.1.0]

Outputs
  publish/repo/        the public source tree (git-managed separately; see publish_repo.py)
  build/site/          the static website exactly as deployed to GitHub Pages (also shipped inside the companion as site/)

Public site data: catalog.json, builtin.json, library.json, rankings.json are copied after sanitizing (no local paths, no archive
member/directory names from the private ROM archive, no art references). Box art is never copied; the page shows its placeholder.
"""
from __future__ import annotations
import sys as _sys
try: _sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception: pass
import os, sys, json, shutil, argparse, subprocess, re, hashlib
HERE = os.path.dirname(os.path.abspath(__file__)); PROJECT = os.path.normpath(os.path.join(HERE, "..", "..")); sys.path.insert(0, os.path.join(PROJECT, "tools", "companion"))
from version import VERSION

# ---- allow list: exact files and whole folders (relative to the project root) that may appear in the public repository ----
ALLOW_FILES = [
    "tools/rom-pipeline/common.py", "tools/rom-pipeline/catalog.py", "tools/rom-pipeline/input_requirements.py", "tools/rom-pipeline/library_lookup.py", "tools/rom-pipeline/manifest_v4.py",
    "tools/rom-pipeline/import_selected.py", "tools/rom-pipeline/build_html.py", "tools/rom-pipeline/index_all.py", "tools/rom-pipeline/identity_audit.py",
    "tools/rom-pipeline/test_manifest_v4_pure.py", "tools/rom-pipeline/test_public_site.py",
    "tools/companion/nes_companion.py", "tools/companion/hakchi_bridge.py", "tools/companion/hakchi_inspect.py", "tools/companion/personal.py", "tools/companion/version.py",
    "tools/companion/companion_app.py", "tools/companion/companion.spec", "tools/companion/installer.iss", "tools/companion/Read-HakchiSelection.ps1", "tools/companion/Get-ProcessWindows.ps1", "tools/companion/Close-Hakchi.ps1",
    "tools/companion/test_hosted_public.py", "tools/companion/test_frozen_smoke.py", "tools/companion/test_live_site.py", "tools/companion/websocket_min.py",
    "tools/companion/test_connection_states.py", "tools/companion/test_handoff.py", "tools/desktop/poc_run.py",
    "tools/companion/console_runs.py", "tools/companion/test_console_engine.py", "tools/companion/test_console_api.py", "tools/desktop/test_source_link.py",
    "tools/publish/build_site_ci.py", "tools/publish/write_version_info.py",
    "tools/publish/make_public_sprites.py", "tools/publish/scan_prohibited.py", "tools/publish/build_public_repo.py", "tools/publish/publish_repo.py", "tools/publish/release_notes.py",
    "tools/publish/fixtures/make_fixtures.py",
]
ALLOW_DIRS = ["tools/rom-pipeline/studio", "tools/publish/assets", "tools/publish/repo-files", "tools/publish/site-static", "tools/publish/fixtures", "tools/desktop/ClassicCartStudio", "tools/console-simulator", "tools/companion/console_engine"]
SITE_DATA = ["catalog.json", "builtin.json", "library.json", "rankings.json"]
# strings naming local paths are dropped from the public data; private markers (user/machine names) come only from the environment (NES_PRIVATE_MARKERS)
PRIVATE_RE = re.compile("|".join([r"[A-Za-z]:\\", "One" + "Drive", "/Users/"] + [re.escape(m.strip()) for m in os.environ.get("NES_PRIVATE_MARKERS", "").split(",") if m.strip()]), re.I)

def align_factory_hash(folder: str):
    """catalog.json records factory_inventory.sha256 of reports/builtin.json; after sanitizing, the published builtin.json has other
    bytes, and the backend (which bundles the published files) validates manifests against them. Record the published hash."""
    import hashlib
    bp, cp = os.path.join(folder, "builtin.json"), os.path.join(folder, "catalog.json")
    if not (os.path.exists(bp) and os.path.exists(cp)): return
    h = hashlib.sha256(open(bp, "rb").read()).hexdigest(); c = json.load(open(cp, encoding="utf-8"))
    fi = c.get("factory_inventory") or {}
    if fi.get("sha256") != h:
        fi["sha256"] = h; fi["size"] = os.path.getsize(bp); c["factory_inventory"] = fi
        json.dump(c, open(cp, "w", encoding="utf-8"), separators=(",", ":"), ensure_ascii=False); print(f"factory inventory hash aligned to the published builtin.json ({h[:16]})")

def sanitize(obj):
    """Drop art references and anything that names a local path or an archive member of the private ROM collection."""
    if isinstance(obj, dict):
        out = {}
        for k, v in obj.items():
            if k in ("stored", "source_path", "archive", "archives", "archive_dir", "archive_path"): continue   # local paths and private-archive names only; artwork references are part of the approved presentation
            out[k] = sanitize(v)
        return out
    if isinstance(obj, list): return [sanitize(x) for x in obj]
    if isinstance(obj, str) and PRIVATE_RE.search(obj): return ""
    return obj

def rmtree_retry(path: str, tries: int = 8):
    """Remove a folder even when a sync client or indexer briefly holds a file (seen inside the OneDrive workspace)."""
    import time
    for i in range(tries):
        shutil.rmtree(path, ignore_errors=True)
        if not os.path.exists(path): return
        time.sleep(1.5)
    shutil.rmtree(path)   # final attempt raises the real error

WRITTEN: set = set()   # every file the current build placed in the public tree; anything else there is removed afterwards (sync in place, no folder deletion)
def _reg(path: str): WRITTEN.add(os.path.normcase(os.path.abspath(path)))
def copy_file(rel: str, dst_root: str):
    src = os.path.join(PROJECT, rel); dst = os.path.join(dst_root, rel); os.makedirs(os.path.dirname(dst), exist_ok=True); shutil.copy2(src, dst); _reg(dst)
def copytree_reg(src: str, dst: str):
    shutil.copytree(src, dst, dirs_exist_ok=True)
    for r, _, fs in os.walk(dst):
        for f in fs: _reg(os.path.join(r, f))

def build_site(site: str):
    os.makedirs(site, exist_ok=True)   # built in place: every file is rewritten; folder deletion of freshly written image folders fails intermittently on this PC
    for sub in ("oem", "web"): shutil.copytree(os.path.join(PROJECT, "reports", "brand", sub), os.path.join(site, "brand", sub), dirs_exist_ok=True)   # approved sprite sets, as they are
    shutil.copytree(os.path.join(PROJECT, "reports", "art"), os.path.join(site, "art"), dirs_exist_ok=True)   # approved box art, as it is
    os.makedirs(os.path.join(site, "brand", "public"), exist_ok=True); shutil.copy2(os.path.join(PROJECT, "tools", "publish", "assets", "app.ico"), os.path.join(site, "brand", "public", "app.ico"))   # the companion's window/installer icon (project asset), at the path the installer expects
    os.makedirs(os.path.join(site, "brand", "fonts"), exist_ok=True); shutil.copy2(os.path.join(PROJECT, "reports", "brand", "fonts", "PressStart2P-Regular.ttf"), os.path.join(site, "brand", "fonts"))
    for lic in ("OFL-PressStart2P.txt",):
        p = os.path.join(PROJECT, "reports", "brand", "fonts", lic)
        if os.path.exists(p): shutil.copy2(p, os.path.join(site, "brand", "fonts"))
    for n in SITE_DATA:
        p = os.path.join(PROJECT, "reports", n)
        if not os.path.exists(p): continue
        d = sanitize(json.load(open(p, encoding="utf-8"))); json.dump(d, open(os.path.join(site, n), "w", encoding="utf-8"), separators=(",", ":"), ensure_ascii=False)
    align_factory_hash(site)
    env = dict(os.environ, NES_STUDIO_SITE=site, NES_PUBLIC_BUILD="1")
    subprocess.run([sys.executable, os.path.join(PROJECT, "tools", "rom-pipeline", "build_html.py"), "--public", "--out", site], check=True, env=env)
    shutil.copy2(os.path.join(site, "nes-catalog.html"), os.path.join(site, "index.html"))
    shutil.copytree(os.path.join(PROJECT, "tools", "publish", "site-static"), site, dirs_exist_ok=True)   # static pages such as /app (the Windows app short link)
    for n in ("favicon.ico", "icon-32.png", "apple-touch-icon.png", "icon-192.png"): shutil.copy2(os.path.join(os.path.join(PROJECT, "tools", "publish", "assets"), "app-32.png" if n == "icon-32.png" else n), os.path.join(site, n))   # browser tab / home-screen icons
    shutil.copy2(os.path.join(os.path.join(PROJECT, "tools", "publish", "assets"), "as-seen-on-tv.png"), os.path.join(site, "brand", "public", "as-seen-on-tv.png"))
    if os.path.isdir(os.path.join(PROJECT, "tools", "publish", "assets", "help")): shutil.copytree(os.path.join(PROJECT, "tools", "publish", "assets", "help"), os.path.join(site, "brand", "help"), dirs_exist_ok=True)   # Help & User Guide figures (screenshots of this Studio, project illustrations)   # the Preview on TV badge (Wikimedia Commons PNG, public domain, see THIRD_PARTY_NOTICES)
    open(os.path.join(site, ".nojekyll"), "w").close()
    open(os.path.join(site, "robots.txt"), "w").write("User-agent: *\nAllow: /\n")
    # release.json is written by the release workflow after a companion release exists; a placeholder keeps the page's lookup honest
    if not os.path.exists(os.path.join(site, "release.json")): json.dump({"version": None, "note": "no companion release published yet"}, open(os.path.join(site, "release.json"), "w"))

def build_repo(repo: str, site: str):
    os.makedirs(repo, exist_ok=True); WRITTEN.clear()   # files are copied over the existing tree and stale files removed at the end; folders stay in place
                                                        # (removing freshly written image folders on this machine intermittently fails with "access is denied")
    for rel in ALLOW_FILES:
        if os.path.exists(os.path.join(PROJECT, rel)): copy_file(rel, repo)
        else: print("  (allow-listed file missing, skipped)", rel)
    for d in ALLOW_DIRS:
        src = os.path.join(PROJECT, d)
        if not os.path.isdir(src): continue
        for root, dirs, files in os.walk(src):
            dirs[:] = [x for x in dirs if x not in ("__pycache__", "bin", "obj")]   # build outputs of the desktop host never ship
            for f in files:
                if f.endswith((".pyc",)): continue
                rel = os.path.relpath(os.path.join(root, f), PROJECT).replace("\\", "/"); copy_file(rel, repo)
    # repository root files live in tools/publish/repo-files and are placed at the root
    rf = os.path.join(repo, "tools", "publish", "repo-files")
    if os.path.isdir(rf):
        for root, dirs, files in os.walk(rf):
            for f in files:
                src = os.path.join(root, f); rel = os.path.relpath(src, rf); dst = os.path.join(repo, rel); os.makedirs(os.path.dirname(dst), exist_ok=True); shutil.copy2(src, dst); _reg(dst)
        for root, dirs, files in os.walk(rf):
            for f in files: WRITTEN.discard(os.path.normcase(os.path.abspath(os.path.join(root, f))))   # the staging copy is not part of the tree: removed below
    # the public site's data + sprites (not the page: CI rebuilds it from source, and the data lets tests/companion builds run without the private workspace)
    sd = os.path.join(repo, "site-data"); os.makedirs(sd, exist_ok=True)
    for n in SITE_DATA:
        if os.path.exists(os.path.join(site, n)): shutil.copy2(os.path.join(site, n), sd); _reg(os.path.join(sd, n))
    for sub in ("oem", "web", "fonts"): copytree_reg(os.path.join(site, "brand", sub), os.path.join(sd, "brand", sub))
    copytree_reg(os.path.join(site, "art"), os.path.join(sd, "art"))   # box art is part of the approved presentation
    open(os.path.join(repo, "VERSION"), "w").write(VERSION + "\n"); _reg(os.path.join(repo, "VERSION"))
    # remove stale files (anything this build did not write), then drop folders that became empty; a folder that cannot be removed is harmless to git
    for r, ds, fs in os.walk(repo, topdown=False):
        if ".git" in os.path.relpath(r, repo).replace("/", os.sep).split(os.sep): continue
        for f in fs:
            fp = os.path.join(r, f)
            if os.path.normcase(os.path.abspath(fp)) not in WRITTEN: os.remove(fp)
        if r != repo and not os.listdir(r):
            try: os.rmdir(r)
            except OSError: pass

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--repo", default=os.path.join(PROJECT, "publish", "repo")); ap.add_argument("--site", default=os.path.join(PROJECT, "build", "site")); ap.add_argument("--no-site", action="store_true"); a = ap.parse_args()
    if not a.no_site: build_site(a.site)
    build_repo(a.repo, a.site)
    r = subprocess.run([sys.executable, os.path.join(HERE, "scan_prohibited.py"), a.repo, "--allow", "site-data/brand/fonts/*"], capture_output=True, text=True, encoding="utf-8", errors="replace"); print(r.stdout[-1500:])
    r2 = subprocess.run([sys.executable, os.path.join(HERE, "scan_prohibited.py"), a.site], capture_output=True, text=True, encoding="utf-8", errors="replace"); print(r2.stdout[-1500:])
    print("REPO SCAN:", "clean" if r.returncode == 0 else "FINDINGS"); print("SITE SCAN:", "clean" if r2.returncode == 0 else "FINDINGS")
    sys.exit(0 if r.returncode == 0 and r2.returncode == 0 else 1)

if __name__ == "__main__":
    main()
