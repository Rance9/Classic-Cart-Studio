"""Classic Cart Studio and CCS Console Workbench share this repository's GitHub releases. Checks (offline, with made-up release lists) that
each feed takes only its own product, that GitHub's repository-wide "latest" is never trusted, and that the website's download code
(connect.js ccsAppRelease, run in Node) and the release.json reader refuse a Workbench release or file.
  python test_release_separation.py      (needs node on PATH or in C:\\Program Files\\nodejs)"""
import json, os, re, shutil, subprocess, sys
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import release_notes as R

results = []
def check(ok, name): results.append((bool(ok), name)); print(("ok   " if ok else "FAIL ") + name)

def rel(tag, when, *assets, draft=False, pre=False): return {"tagName": tag, "publishedAt": when, "isDraft": draft, "isPrerelease": pre, "assets": [{"name": a, "size": 1, "url": "u/" + a} for a in assets]}

L = [rel("v1.0.0", "2026-12-01T10:00:00Z", "ClassicCartStudio-Setup-1.0.0.exe", "SHA256SUMS.txt"),
     rel("workbench-v1.0.0", "2026-12-01T11:00:00Z", "CCS-Console-Workbench-1.0.0.exe", "SHA256SUMS.txt"),
     rel("v1.0.1", "2026-12-05T10:00:00Z", "ClassicCartStudio-Setup-1.0.1.exe", draft=True),
     rel("v1.1.0-rc1", "2026-12-06T10:00:00Z", "ClassicCartStudio-Setup-1.1.0.exe", pre=True),
     rel("workbench-v1.0.1", "2026-12-07T10:00:00Z", "CCS-Console-Workbench-1.0.1.exe")]
r, a = R.select_release(L, "app")
check(r and r["tagName"] == "v1.0.0" and a["name"] == "ClassicCartStudio-Setup-1.0.0.exe", "the app feed takes the newest published app release even when a newer Workbench release is the repository's latest")
r, a = R.select_release(L, "workbench")
check(r and r["tagName"] == "workbench-v1.0.1" and a["name"] == "CCS-Console-Workbench-1.0.1.exe", "the Workbench feed takes only Workbench releases")
check(R.select_release([rel("v2.0.0", "2027-01-01T00:00:00Z", "CCS-Console-Workbench-2.0.0.exe")], "app") == (None, None), "an app tag carrying only a Workbench file is refused")
check(R.select_release([rel("workbench-v2.0.0", "2027-01-01T00:00:00Z", "ClassicCartStudio-Setup-2.0.0.exe")], "workbench") == (None, None), "a Workbench tag carrying only the app installer is refused")
check(R.select_release([rel("v2.0.0", "2027-01-01T00:00:00Z", "ClassicCartStudio-Setup-2.0.0.exe", "CCS-Console-Workbench-2.0.0.exe")], "app") == (None, None), "a release mixing both products' files is refused by both feeds")
check(R.select_release([rel("v2.0.0", "2027-01-01T00:00:00Z", "ClassicCartStudio-Setup-1.9.0.exe")], "app") == (None, None), "an installer whose version differs from its tag is refused")
check(R.select_release([rel("v2.0.0", "2027-01-01T00:00:00Z", "ClassicCartStudio-Setup-2.0.0.exe", draft=True)], "app") == (None, None), "drafts and prereleases are never offered")

# Prelaunch feeds explicitly opt in; stable consumers keep their existing policy.
P = [rel("v1.2.0", "2027-02-01", "ClassicCartStudio-Setup-1.2.0.exe", pre=True),
     rel("workbench-v1.2.0", "2027-02-02", "CCS-Console-Workbench-1.2.0.exe", pre=True)]
check(R.select_release(P, "app") == (None, None), "stable app feeds exclude previews")
check(R.select_release(P, "app", True)[0]["tagName"] == "v1.2.0", "explicit preview app feed stays product-specific")
check(R.select_release(P, "workbench", True)[0]["tagName"] == "workbench-v1.2.0", "explicit preview Workbench feed stays product-specific")
check(R.select_release([rel("v1.2.0", "2027", "ClassicCartStudio-Setup-1.2.0.exe", draft=True)], "app", True) == (None, None), "preview opt-in never exposes drafts")

# the website: connect.js's own selection, run in Node with GitHub's API field names
node = shutil.which("node") or (r"C:\Program Files\nodejs\node.exe" if os.path.exists(r"C:\Program Files\nodejs\node.exe") else None)
src = open(os.path.join(HERE, "..", "rom-pipeline", "studio", "connect.js"), encoding="utf-8").read()
m = re.search(r"function ccsAppRelease\(list\)\{.*?\n return best; \}", src, re.S)
check(m is not None, "connect.js has its own app-release selection (ccsAppRelease)")
check("/releases/latest'" not in src and "releases?per_page=" in src, "connect.js never asks GitHub for the repository-wide latest release")
check(re.search(r"r\.product==='app'\)&&/\^ClassicCartStudio-Setup-/i\.test\(r\.asset\.name", src) is not None, "the published release.json is used only when it names the app and an app installer")
if node and m:
    api = [{"tag_name": x["tagName"], "published_at": x["publishedAt"], "draft": x["isDraft"], "prerelease": x["isPrerelease"], "assets": [{"name": y["name"]} for y in x["assets"]]} for x in L]
    js = m.group(0) + "\nconst L=" + json.dumps(api) + ";\nconst a=ccsAppRelease(L); const b=ccsAppRelease([{tag_name:'v3.0.0',published_at:'2027',assets:[{name:'CCS-Console-Workbench-3.0.0.exe'}]}]);\nconsole.log(JSON.stringify([a&&a.tag_name,b]));"
    out = subprocess.run([node, "-e", js], capture_output=True, text=True, timeout=60).stdout.strip()
    check(out == '["v1.0.0",null]', "the website's download code (in Node) takes v1.0.0 over newer Workbench releases and refuses a Workbench file under an app tag (" + out + ")")
else:
    check(False, "Node was not found to run connect.js's selection")

fails = [n for ok, n in results if not ok]
print("RESULT %d/%d" % (len(results) - len(fails), len(results)))
sys.exit(1 if fails else 0)
