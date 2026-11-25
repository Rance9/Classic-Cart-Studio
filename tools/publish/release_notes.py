"""Release text (a product description, file hashes, build and license information; never a list of changes) and the website's
release information files.

  python release_notes.py --version 0.1.0 --build-info build/release/build-info.json --out NOTES.md
  python release_notes.py --write-streams-json build/site/release-streams.json [--write-release-json build/site/release.json]
                          [--write-workbench-json build/site/workbench-release.json] [--include-prereleases]
      reads this repository's GitHub releases through `gh` once and writes: release-streams.json, the four product/channel streams the
      update checks read (release_streams.py), and the two files earlier releases read, each naming one product's newest release of one
      channel (the preview channel with --include-prereleases, production without); an incomplete lookup writes files that list nothing
"""
from __future__ import annotations
import os, sys, json, argparse, subprocess
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(REPO, "tools", "companion")); sys.path.insert(0, HERE)
from version import PROTOCOL, RELEASES_REPO
import release_streams as RS
import re
UTILITY_PREFIX = "CCS-Console-Backup-Restore-Utility-"   # earlier app releases carried the standalone Utility (CCS-Console-Backup-Restore-Utility-<version>.exe)

# Two products share this repository's GitHub releases, which are repository-wide: Classic Cart Studio (tags v<version>, installer
# ClassicCartStudio-Setup-<version>.exe) and CCS Console Workbench (tags workbench-v<version>, file CCS-Console-Workbench-<version>.exe,
# published with "latest" turned off). GitHub's "latest release" is never trusted to be the right product: each feed lists releases and
# takes the published, non-draft one (prereleases require explicit opt-in) with the highest version number whose tag AND installer file
# both belong to that product; a tag in release_streams.EXCLUDED is never taken.
PRODUCTS = {
    "app": {"tag": re.compile(r"^v(\d+\.\d+\.\d+)$"), "asset": re.compile(r"^ClassicCartStudio-Setup-(\d+\.\d+\.\d+)\.exe$", re.I)},
    "workbench": {"tag": re.compile(r"^workbench-v(\d+\.\d+\.\d+)$"), "asset": re.compile(r"^CCS-Console-Workbench-(\d+\.\d+\.\d+)\.exe$", re.I)},
}

def select_release(releases: list, product: str, include_prereleases: bool = False):
    """releases: [{tagName, isDraft, isPrerelease, publishedAt, assets:[{name,...}]}]. The release of this product with the highest version
    number, with its installer asset; (None, None) when there is none. A release whose tag or file names another product is never chosen,
    nor an excluded tag; never decided by publication date."""
    p = PRODUCTS[product]; best = None
    for r in releases or []:
        if r.get("isDraft") or (r.get("isPrerelease") and not include_prereleases): continue
        if (r.get("tagName") or "") in RS.EXCLUDED: continue
        m = p["tag"].match(r.get("tagName") or "")
        if not m: continue
        a = next((x for x in r.get("assets") or [] if (am := p["asset"].match(x.get("name") or "")) and am.group(1) == m.group(1)), None)
        if a is None: continue
        if any(PRODUCTS[o]["asset"].match(x.get("name") or "") for o in PRODUCTS if o != product for x in r.get("assets") or []): continue
        if best is None or RS.version(m.group(1)) > RS.version(p["tag"].match(best[0]["tagName"]).group(1)): best = (r, a)
    return best or (None, None)

def utility_row(bi: dict) -> str:
    u = bi.get("utility") or {}
    return f"| `{u.get('name','')}` | `{u.get('sha256','')}` | {u.get('size', 0):,} bytes |\n" if u.get("name") else ""

def utility_note(bi: dict) -> str:
    if not (bi.get("utility") or {}).get("name"): return ""
    return ("The second file is the **CCS Console Backup & Restore Utility**: Console Backup & Restore on its own, as one program that\n"
            "needs no installation (the Windows app has the same feature built in).\n\n")

def notes(version: str, bi: dict, preview: bool = False) -> str:
    warning = ("**Development preview — unsigned.** Classic Cart Studio and the included CCS Console Workbench work with a real console: "
               "each change is backed up first, reviewed and confirmed before anything is written. The tasks that change a console have not "
               "all been tried on real consoles yet. Games prepared on this computer go on the console with Classic Cart Studio's own console "
               "steps, or you transfer them in the included Hakchi2 CE 3.9.3 (Continue in Hakchi2 CE), under your control.\n\n") if preview else ""
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

def _gh(*args, timeout=60):
    return subprocess.run(["gh", *args], capture_output=True, text=True, encoding="utf-8", timeout=timeout)

def _releases(include_prereleases: bool = True) -> list:
    """every published release with its files (drafts are left out); raises when GitHub cannot be read"""
    out = _gh("release", "list", "--repo", RELEASES_REPO, "--limit", "100", "--json", "tagName,isDraft,isPrerelease,publishedAt")
    if out.returncode != 0: raise RuntimeError("gh release list failed")
    full = []
    for r in json.loads(out.stdout):
        if r.get("isDraft") or (r.get("isPrerelease") and not include_prereleases): continue
        v = _gh("release", "view", r["tagName"], "--repo", RELEASES_REPO, "--json", "tagName,publishedAt,url,assets,isDraft,isPrerelease")
        if v.returncode != 0: raise RuntimeError("gh release view failed for " + r["tagName"])
        full.append(json.loads(v.stdout))
    return full

def _download_text(r: dict, name: str) -> str:
    t = _gh("release", "download", r["tagName"], "--repo", RELEASES_REPO, "--pattern", name, "--output", "-")
    if t.returncode != 0: raise RuntimeError(f"{name} of {r['tagName']} could not be read")
    return t.stdout

def _meta(r: dict, name: str):
    return json.loads(_download_text(r, name).lstrip("\ufeff"))   # build-info.json: written by PowerShell (a byte-order mark, if any, is ignored)

def _sums(r: dict):
    return _download_text(r, "SHA256SUMS.txt") if any((a.get("name") or "").upper().startswith("SHA256SUMS") for a in r.get("assets") or []) else None

def streams_json() -> dict:
    import datetime
    now = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    try: rels = _releases(True)
    except Exception as e:
        return {"schema": RS.SCHEMA, "generated": now, "repository": RS.REPO, "download_page": RS.DOWNLOAD_PAGE, "complete": False, "note": f"release lookup unavailable ({type(e).__name__})", "excluded": [], "streams": []}
    return RS.build(rels, _meta, _sums, now)

def latest_release_json(product: str = "app", include_prereleases: bool = False, doc: dict | None = None) -> dict:
    """the earlier releases' feed (release.json, workbench-release.json): this product's newest release of the preview channel (with
    include_prereleases) or of the production channel, from the release streams"""
    try:
        return RS.legacy_json(doc if doc is not None else streams_json(), product, "preview" if include_prereleases else "production", PROTOCOL, UTILITY_PREFIX)
    except Exception as e:
        return {"version": None, "note": f"release lookup unavailable ({type(e).__name__})"}

def _write(path: str, j: dict, label: str):
    with open(path, "w", encoding="utf-8", newline="\n") as f: json.dump(j, f, indent=1)   # the same bytes on any build machine
    print(label + ":", json.dumps(j)[:200])

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--version"); ap.add_argument("--build-info"); ap.add_argument("--out"); ap.add_argument("--write-release-json"); ap.add_argument("--write-workbench-json"); ap.add_argument("--write-streams-json"); ap.add_argument("--include-prereleases", action="store_true"); ap.add_argument("--preview", action="store_true"); a = ap.parse_args()
    if a.write_release_json or a.write_workbench_json or a.write_streams_json:
        doc = streams_json()   # GitHub is read once for all three files
        if a.write_streams_json: _write(a.write_streams_json, doc, "release-streams.json" + ("" if doc.get("complete") else " (INCOMPLETE: lists nothing)"))
        if a.write_release_json: _write(a.write_release_json, latest_release_json("app", a.include_prereleases, doc), "release.json")
        # CCS Console Workbench's own feed (workbench-release.json): never read by the app's update or download code
        if a.write_workbench_json: _write(a.write_workbench_json, latest_release_json("workbench", a.include_prereleases, doc), "workbench-release.json")
        return
    bi = json.load(open(a.build_info, encoding="utf-8-sig")) if a.build_info and os.path.exists(a.build_info) else {}
    txt = notes(a.version or bi.get("version", ""), bi, a.preview)
    if a.out: open(a.out, "w", encoding="utf-8").write(txt); print("wrote", a.out)
    else: print(txt)

if __name__ == "__main__":
    main()
