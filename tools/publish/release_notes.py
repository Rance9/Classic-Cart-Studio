"""Release text (a product description, file hashes, build and license information; never a list of changes) and the
website's release.json.

  python release_notes.py --version 0.1.0 --build-info build/release/build-info.json --out NOTES.md
  python release_notes.py --write-release-json build/site/release.json      (reads the latest GitHub release through `gh`; writes a
                                                                             placeholder when there is none or gh is unavailable)
"""
from __future__ import annotations
import os, sys, json, argparse, subprocess
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(REPO, "tools", "companion"))
from version import PROTOCOL, RELEASES_REPO
import re
UTILITY_PREFIX = "CCS-Console-Backup-Restore-Utility-"   # earlier app releases carried the standalone Utility (CCS-Console-Backup-Restore-Utility-<version>.exe)

# Two products share this repository's GitHub releases, which are repository-wide: Classic Cart Studio (tags v<version>, installer
# ClassicCartStudio-Setup-<version>.exe) and CCS Console Workbench (tags workbench-v<version>, file CCS-Console-Workbench-<version>.exe,
# published with "latest" turned off). GitHub's "latest release" is never trusted to be the right product: each feed lists releases and
# takes the newest published, non-draft one (prereleases require explicit opt-in) whose tag AND installer file both belong to that product.
PRODUCTS = {
    "app": {"tag": re.compile(r"^v(\d+\.\d+\.\d+)$"), "asset": re.compile(r"^ClassicCartStudio-Setup-(\d+\.\d+\.\d+)\.exe$", re.I)},
    "workbench": {"tag": re.compile(r"^workbench-v(\d+\.\d+\.\d+)$"), "asset": re.compile(r"^CCS-Console-Workbench-(\d+\.\d+\.\d+)\.exe$", re.I)},
}

def select_release(releases: list, product: str, include_prereleases: bool = False):
    """releases: [{tagName, isDraft, isPrerelease, publishedAt, assets:[{name,...}]}]. The newest release of this product, with its installer
    asset; (None, None) when there is none. A release whose tag or file names another product is never chosen."""
    p = PRODUCTS[product]; best = None
    for r in releases or []:
        if r.get("isDraft") or (r.get("isPrerelease") and not include_prereleases): continue
        m = p["tag"].match(r.get("tagName") or "")
        if not m: continue
        a = next((x for x in r.get("assets") or [] if (am := p["asset"].match(x.get("name") or "")) and am.group(1) == m.group(1)), None)
        if a is None: continue
        if any(PRODUCTS[o]["asset"].match(x.get("name") or "") for o in PRODUCTS if o != product for x in r.get("assets") or []): continue
        if best is None or (r.get("publishedAt") or "") > (best[0].get("publishedAt") or ""): best = (r, a)
    return best or (None, None)

def utility_row(bi: dict) -> str:
    u = bi.get("utility") or {}
    return f"| `{u.get('name','')}` | `{u.get('sha256','')}` | {u.get('size', 0):,} bytes |\n" if u.get("name") else ""

def utility_note(bi: dict) -> str:
    if not (bi.get("utility") or {}).get("name"): return ""
    return ("The second file is the **CCS Console Backup & Restore Utility**: Console Backup & Restore on its own, as one program that\n"
            "needs no installation (the Windows app has the same feature built in).\n\n")

def notes(version: str, bi: dict, preview: bool = False) -> str:
    warning = ("**Development preview — unsigned.** All console connections, console writes and driver actions are disabled in this build. "
               "Real-console and Windows on Arm qualification are not complete. Offline backup-file tools and the PC interface are available.\n\n") if preview else ""
    return f"""# Classic Cart Studio for Windows {version}

{warning}

Build your NES Classic Edition collection on Windows: browse the game library, choose games and versions, arrange folders,
preview the console HOME on your TV screen in four themes, and prepare the collection for your console. Everything works
offline; the console tools it uses are included.

## Files

| File | SHA-256 | Size |
|---|---|---|
| `{bi.get('installer','')}` | `{bi.get('sha256','')}` | {bi.get('size', 0):,} bytes |
{utility_row(bi)}
{utility_note(bi)}`SHA256SUMS.txt` lists the same hashes. Verify on Windows with:

```powershell
Get-FileHash -Algorithm SHA256 "$env:USERPROFILE\\Downloads\\{bi.get('installer','')}"
```

## Build information

- Source revision: `{bi.get('source_revision','')}`
- Built by GitHub Actions: {bi.get('workflow_run','')}
- Built at: {bi.get('built','')} (UTC)
- Protocol version: {PROTOCOL}
- Code signing: **not signed** (see README → Installation → Why the installer is not signed). A build-provenance attestation is attached to the workflow run.

## Install / upgrade / uninstall

Per-user install under `%LOCALAPPDATA%\\Programs\\ClassicCartStudio`; no administrator rights. Running a newer installer upgrades in place and keeps your data (`%LOCALAPPDATA%\\ClassicCartStudio`). Uninstall from Windows Settings → Apps; your imported games, personal games and collections are kept.

## Source code and licenses

Classic Cart Studio is licensed under the GNU General Public License v3 or later (`GPL-3.0-or-later`). Its source code is this
repository at the tag of this release. Third-party components and their licenses: `THIRD_PARTY_NOTICES.md`. The installer
includes Hakchi2 CE 3.9.3 (GPL v3) unmodified; the source of Hakchi2 CE's own program (tag v3.9.3 with its submodules) is attached to
this release, and the prebuilt files the package also contains are listed in `THIRD_PARTY_NOTICES.md`. The source of the LibUsbDotNet
library the app carries (the commit its package names) is attached as well. CCS Console Workbench (in the installer; also released on
its own as one program) sends console programs built from Hakchi2 CE 3.9.3's prebuilt hakchi package with a changed start-up: Classic
Cart Studio's changes and tools are `tools/console-backup` in this repository and are recorded in each program's MANIFEST.json; the
hakchi package's programs they carry keep their own licenses, listed with their notices in `tools/console-backup/NOTICE`.

Several prebuilt upstream components have unresolved corresponding-source or redistribution questions. They remain bundled in this
preview; available source archives do not resolve those gaps or establish complete source availability. The component-by-component
record is `tools/console-backup/SOURCE-AUDIT.md`. PAR2 library source is attached separately. Included Games source is in
`site-data/included/source` at this release's source revision.
"""

def _releases(include_prereleases: bool = False) -> list:
    out = subprocess.run(["gh", "release", "list", "--repo", RELEASES_REPO, "--limit", "50", "--json", "tagName,isDraft,isPrerelease,publishedAt"], capture_output=True, text=True, encoding="utf-8", timeout=60)
    if out.returncode != 0: return []
    full = []
    for r in json.loads(out.stdout):
        if r.get("isDraft") or (r.get("isPrerelease") and not include_prereleases): continue
        v = subprocess.run(["gh", "release", "view", r["tagName"], "--repo", RELEASES_REPO, "--json", "tagName,publishedAt,url,assets,isDraft,isPrerelease"], capture_output=True, text=True, encoding="utf-8", timeout=60)
        if v.returncode == 0: full.append(json.loads(v.stdout))
    return full

def latest_release_json(product: str = "app", include_prereleases: bool = False) -> dict:
    try:
        r, a0 = select_release(_releases(include_prereleases), product, include_prereleases)
        if r is None: return {"version": None, "product": product, "note": "no " + ("companion" if product == "app" else "Workbench") + " release published yet"}
        assets = r.get("assets", [])
        # a release carries two programs: the installer (by its name, never "the first .exe") and the CCS Console Backup & Restore Utility
        a = a0
        u = next((x for x in assets if x["name"].startswith(UTILITY_PREFIX) and x["name"].lower().endswith(".exe")), None) if product == "app" else None
        s = next((x for x in assets if x["name"].upper().startswith("SHA256SUMS")), None)
        sha = usha = None
        if s:
            t = subprocess.run(["gh", "release", "download", r["tagName"], "--repo", RELEASES_REPO, "--pattern", "SHA256SUMS.txt", "--output", "-"], capture_output=True, text=True, encoding="utf-8", timeout=60).stdout
            for l in t.splitlines():
                if a and l.rstrip().endswith("  " + a["name"]): sha = l.split()[0].lower()
                if u and l.rstrip().endswith("  " + u["name"]): usha = l.split()[0].lower()
        return {"product": product, "version": PRODUCTS[product]["tag"].match(r["tagName"]).group(1), "tag": r["tagName"], "prerelease": bool(r.get("isPrerelease")), "published": r.get("publishedAt"), "url": r.get("url"), "asset": {"name": a["name"], "size": a["size"], "url": a["url"]} if a else None, "sums_url": s["url"] if s else None, "sha256": sha,
                "utility": {"name": u["name"], "size": u["size"], "url": u["url"], "sha256": usha} if u else None, "protocol": PROTOCOL, "source": "github-release"}
    except Exception as e:
        return {"version": None, "note": f"release lookup unavailable ({type(e).__name__})"}

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--version"); ap.add_argument("--build-info"); ap.add_argument("--out"); ap.add_argument("--write-release-json"); ap.add_argument("--write-workbench-json"); ap.add_argument("--include-prereleases", action="store_true"); ap.add_argument("--preview", action="store_true"); a = ap.parse_args()
    if a.write_release_json:
        j = latest_release_json("app", a.include_prereleases); json.dump(j, open(a.write_release_json, "w", encoding="utf-8"), indent=1); print("release.json:", json.dumps(j)[:200]); return
    if a.write_workbench_json:   # CCS Console Workbench's own feed (workbench-release.json): never read by the app's update or download code
        j = latest_release_json("workbench", a.include_prereleases); json.dump(j, open(a.write_workbench_json, "w", encoding="utf-8"), indent=1); print("workbench-release.json:", json.dumps(j)[:200]); return
    bi = json.load(open(a.build_info, encoding="utf-8")) if a.build_info and os.path.exists(a.build_info) else {}
    txt = notes(a.version or bi.get("version", ""), bi, a.preview)
    if a.out: open(a.out, "w", encoding="utf-8").write(txt); print("wrote", a.out)
    else: print(txt)

if __name__ == "__main__":
    main()
