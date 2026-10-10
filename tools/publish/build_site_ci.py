"""Build the public static site inside the PUBLIC repository (CI and local checkouts): original sprites + the page generated from the
public data in site-data/. No private workspace is needed.   python build_site_ci.py <out_dir>"""
from __future__ import annotations
import os, sys, json, shutil, subprocess
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.normpath(os.path.join(HERE, "..", ".."))

def main(out: str):
    out = os.path.abspath(out)
    if os.path.isdir(out): shutil.rmtree(out)
    os.makedirs(out)
    sd = os.path.join(REPO, "site-data")
    for sub in ("oem", "web"): shutil.copytree(os.path.join(sd, "brand", sub), os.path.join(out, "brand", sub), dirs_exist_ok=True)
    shutil.copytree(os.path.join(sd, "art"), os.path.join(out, "art"), dirs_exist_ok=True)
    os.makedirs(os.path.join(out, "brand", "public"), exist_ok=True); shutil.copy2(os.path.join(REPO, "tools", "publish", "assets", "app.ico"), os.path.join(out, "brand", "public", "app.ico"))   # companion icon at the path the installer expects
    for n in ("catalog.json", "builtin.json", "library.json", "rankings.json"):
        if os.path.exists(os.path.join(sd, n)): shutil.copy2(os.path.join(sd, n), out)
    shutil.copytree(os.path.join(sd, "brand", "fonts"), os.path.join(out, "brand", "fonts"), dirs_exist_ok=True)
    subprocess.run([sys.executable, os.path.join(REPO, "tools", "rom-pipeline", "build_html.py"), "--public", "--out", out], check=True, env=dict(os.environ, NES_STUDIO_SITE=out, NES_PUBLIC_BUILD="1"))
    shutil.copy2(os.path.join(out, "nes-catalog.html"), os.path.join(out, "index.html"))
    open(os.path.join(out, ".nojekyll"), "w").close(); open(os.path.join(out, "robots.txt"), "w").write("User-agent: *\nAllow: /\n")
    if not os.path.exists(os.path.join(out, "release.json")): json.dump({"version": None, "note": "no companion release published yet"}, open(os.path.join(out, "release.json"), "w"))
    print("site built at", out)

if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(REPO, "build", "site"))
