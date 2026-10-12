"""Release notes and the website's release.json.

  python release_notes.py --version 0.1.0 --build-info build/release/build-info.json --out NOTES.md
  python release_notes.py --write-release-json build/site/release.json      (reads the latest GitHub release through `gh`; writes a
                                                                             placeholder when there is none or gh is unavailable)
"""
from __future__ import annotations
import os, sys, json, argparse, subprocess
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(REPO, "tools", "companion"))
from version import PROTOCOL, RELEASES_REPO

def changelog_section(version: str) -> str:
    p = os.path.join(REPO, "CHANGELOG.md")
    if not os.path.exists(p): return ""
    lines = open(p, encoding="utf-8").read().splitlines(); out = []; on = False
    for l in lines:
        if l.startswith("## "):
            if on: break
            on = version in l
            continue
        if on: out.append(l)
    return "\n".join(out).strip()

def notes(version: str, bi: dict) -> str:
    body = changelog_section(version) or "_No changelog section for this version._"
    return f"""# Classic Cart Studio for Windows {version}

{body}

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

Per-user install under `%LOCALAPPDATA%\\Programs\\ClassicCartStudio`; no administrator rights. Running a newer installer upgrades in place and keeps your data (`%LOCALAPPDATA%\\ClassicCartStudio`; the earlier `NESClassicCollectionStudio` folder is moved there on first start). Uninstall from Windows Settings → Apps; your imported games, personal games and collections are kept.
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
