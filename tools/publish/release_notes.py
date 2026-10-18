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

def notes(version: str, bi: dict) -> str:
    return f"""# Classic Cart Studio for Windows {version}

Build your NES Classic Edition collection on Windows: browse the game library, choose games and versions, arrange folders,
preview the console HOME on your TV screen in four themes, and prepare the collection for your console. Everything works
offline; the console tools it uses are included.

## Files

| File | SHA-256 | Size |
|---|---|---|
| `{bi.get('installer','')}` | `{bi.get('sha256','')}` | {bi.get('size', 0):,} bytes |

`SHA256SUMS.txt` lists the same hash. Verify on Windows with:

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
includes Hakchi2 CE 3.9.3 (GPL v3) unmodified; its source archive is attached to this release.
"""

def latest_release_json() -> dict:
    try:
        out = subprocess.run(["gh", "release", "view", "--repo", RELEASES_REPO, "--json", "tagName,publishedAt,url,assets,isDraft,isPrerelease"], capture_output=True, text=True, encoding="utf-8", timeout=60)
        if out.returncode != 0: return {"version": None, "note": "no companion release published yet"}
        r = json.loads(out.stdout); a = next((x for x in r.get("assets", []) if x["name"].lower().endswith(".exe")), None); s = next((x for x in r.get("assets", []) if x["name"].upper().startswith("SHA256SUMS")), None)
        sha = None
        if s:
            t = subprocess.run(["gh", "release", "download", r["tagName"], "--repo", RELEASES_REPO, "--pattern", "SHA256SUMS.txt", "--output", "-"], capture_output=True, text=True, encoding="utf-8", timeout=60).stdout
            for l in t.splitlines():
                if a and a["name"] in l: sha = l.split()[0].lower()
        return {"version": r["tagName"].lstrip("v"), "tag": r["tagName"], "published": r.get("publishedAt"), "url": r.get("url"), "asset": {"name": a["name"], "size": a["size"], "url": a["url"]} if a else None, "sums_url": s["url"] if s else None, "sha256": sha, "protocol": PROTOCOL, "source": "github-release"}
    except Exception as e:
        return {"version": None, "note": f"release lookup unavailable ({type(e).__name__})"}

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--version"); ap.add_argument("--build-info"); ap.add_argument("--out"); ap.add_argument("--write-release-json"); a = ap.parse_args()
    if a.write_release_json:
        j = latest_release_json(); json.dump(j, open(a.write_release_json, "w", encoding="utf-8"), indent=1); print("release.json:", json.dumps(j)[:200]); return
    bi = json.load(open(a.build_info, encoding="utf-8")) if a.build_info and os.path.exists(a.build_info) else {}
    txt = notes(a.version or bi.get("version", ""), bi)
    if a.out: open(a.out, "w", encoding="utf-8").write(txt); print("wrote", a.out)
    else: print(txt)

if __name__ == "__main__":
    main()
