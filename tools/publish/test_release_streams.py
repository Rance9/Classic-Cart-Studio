"""The release streams and both update checks' selection, offline (made-up release lists shaped like this repository's own, plus the
published releases as of 0.11.3). Checks release_streams.py (what the website publishes, release-streams.json, and the two earlier feeds
built from it) and companion/app_update.py (Classic Cart Studio's own decision, including what it installs and what it sends to the download
page). CCS Console Workbench's decision (CcsBackup.Library.ReleaseStreams) is checked by the engine tests' "updates" group with the same cases.
  python test_release_streams.py      Exit 0 = every check passed. Needs no network: nothing here opens a connection."""
import copy, json, os, re, sys
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path[:0] = [HERE, os.path.join(REPO, "tools", "companion")]
import release_streams as RS
import release_notes as RN
import app_update as AU
import version as V

results = []
def check(ok, name): results.append((bool(ok), name)); print(("ok   " if ok else "FAIL ") + name)

DL = RS.DOWNLOADS
def rel(tag, pre, when, *names, draft=False):
    return {"tagName": tag, "isDraft": draft, "isPrerelease": pre, "publishedAt": when, "url": "https://github.com/" + RS.REPO + "/releases/tag/" + tag,
            "assets": [{"name": n, "size": 1000 + len(n), "url": DL + tag + "/" + n} for n in names]}
def app(v, pre, when, *extra, draft=False): return rel("v" + v, pre, when, f"ClassicCartStudio-Setup-{v}.exe", "SHA256SUMS.txt", "build-info.json", "ClassicCartStudio-Installation-Terms-1.3.txt", *extra, draft=draft)
def wb(v, pre, when, *extra): return rel("workbench-v" + v, pre, when, f"CCS-Console-Workbench-{v}.exe", f"CCS-Console-Workbench-{v}-arm64.exe", "SHA256SUMS.txt", "build-record.json", *extra)

# the published releases as of 0.11.3 (tags, prerelease marks and main files as GitHub lists them; build information without a channel)
PUBLISHED = [app("0.11.3", True, "2026-11-19T11:12:52Z"), app("0.11.2", True, "2026-11-19T05:14:16Z"), wb("0.11.3", True, "2026-11-19T11:12:54Z"),
             wb("0.11.2", True, "2026-11-19T05:14:18Z"), app("0.11.1", True, "2026-11-19T00:07:51Z"), app("0.11.0", True, "2026-11-18T13:44:46Z"),
             wb("1.0.0", True, "2026-11-18T13:44:48Z"), wb("0.11.1", True, "2026-11-19T00:07:53Z"), wb("0.11.0", True, "2026-11-18T14:04:16Z", "version-correction.json"),
             app("0.10.1", False, "2026-11-11T23:36:25Z"), app("0.10.0", False, "2026-11-11T17:07:28Z"), app("0.9.0", False, "2026-11-11T11:58:08Z"),
             app("0.8.0", False, "2026-11-11T00:26:38Z"), app("0.7.7", False, "2026-11-02T09:10:05Z"), app("0.6.0", False, "2026-10-19T01:31:52Z")]
META = {}   # tag -> build information (absent: the release's file exists and holds no channel, as every release up to 0.11.3)
def meta(r, name):
    m = META.get(r["tagName"])
    if isinstance(m, Exception): raise m
    return m if m is not None else {"version": r["tagName"].split("v")[-1], "signed": False}
def sums(r): return "".join(f"{'%064x' % (i + 1)}  {a['name']}\n" for i, a in enumerate(r["assets"]))

def heads(doc): return {(s["product"], s["channel"]): [e["version"] for e in s["releases"]] for s in doc["streams"]}

# ---- what the website publishes
doc = RS.build(PUBLISHED, meta, sums, "2026-11-19T12:00:00Z")
h = heads(doc)
check(doc["schema"] == "ccs-release-streams/1" and doc["complete"] is True and len(doc["streams"]) == 4, "four streams, complete")
check(h[("app", "preview")][:4] == ["0.11.3", "0.11.2", "0.11.1", "0.11.0"], "legacy 0.11.0 to 0.11.3 of the app are previews (GitHub prerelease, no channel in their metadata)")
check(h[("app", "production")][0] == "0.10.1" and "0.11.3" not in h[("app", "production")], "v0.10.1 heads app/production; no preview is a production release")
check(h[("workbench", "preview")] == ["0.11.3", "0.11.2", "0.11.1", "0.11.0"], "workbench/preview is 0.11.0 to 0.11.3 newest first")
check("1.0.0" not in h[("workbench", "preview")] and "1.0.0" not in h[("workbench", "production")], "the superseded workbench-v1.0.0 is in no stream, although its number is larger")
check(any(x["tag"] == "workbench-v1.0.0" for x in doc["excluded"]), "workbench-v1.0.0 is listed as excluded, with its reason")
check(h[("workbench", "production")] == [], "workbench/production lists nothing (no production Workbench was published)")
e = next(s for s in doc["streams"] if s["product"] == "app" and s["channel"] == "preview")["releases"][0]
check(e["channel_from"] == "github-prerelease" and e["asset"]["sha256"] == "%064x" % 1 and e["asset"]["url"] == DL + "v0.11.3/ClassicCartStudio-Setup-0.11.3.exe", "the newest entry carries its file, its checksum and the source of its channel")

NEW = copy.deepcopy(PUBLISHED) + [app("0.11.4", True, "2026-11-20T10:00:00Z"), wb("0.11.4", True, "2026-11-20T10:00:01Z"),
                                  app("0.11.9", True, "2026-11-21T10:00:00Z"), app("0.11.10", True, "2026-11-20T09:00:00Z")]
META.update({"v0.11.4": {"product": "app", "version": "0.11.4", "channel": "preview", "signed": False,
                         "bundle": {"workbench": {"version": "0.11.4", "channel": "preview", "assets": [{"arch": "x64", "name": "CCS-Console-Workbench.exe", "sha256": "ab" * 32, "nested": {"x": 1}}]}, "engine_source": {"commit": "c" * 40}}},
             "workbench-v0.11.4": {"product": "workbench", "version": "0.11.4", "channel": "preview"},
             "v0.11.9": {"product": "app", "version": "0.11.9", "channel": "preview"}, "v0.11.10": {"product": "app", "version": "0.11.10", "channel": "preview"}})
d2 = RS.build(NEW, meta, sums, "x"); h2 = heads(d2)
check(h2[("app", "preview")][:3] == ["0.11.10", "0.11.9", "0.11.4"], "0.11.10 is newer than 0.11.9 (numbers, not text and not publication date)")
e4 = next(x for x in next(s for s in d2["streams"] if s["product"] == "app" and s["channel"] == "preview")["releases"] if x["version"] == "0.11.4")
check(e4["channel_from"] == "build-info.json" and e4["bundle"]["workbench"]["version"] == "0.11.4" and "nested" not in e4["bundle"]["workbench"]["assets"][0], "a new app release's channel comes from its build information, with its Studio bundle (plain values only)")
check(h2[("workbench", "preview")][0] == "0.11.4", "a new Workbench release's channel comes from its build record")

def only(*rels, **m):
    META.clear(); META.update(m); return RS.build(list(rels), meta, sums, "x")
d = only(app("0.12.0", True, "2027", draft=True)); check(d["streams"][0]["releases"] == [] and heads(d)[("app", "preview")] == [], "drafts are never listed")
d = only(app("0.12.0", False, "2027"), **{"v0.12.0": {"channel": "preview"}}); check(heads(d)[("app", "preview")] == [] and heads(d)[("app", "production")] == [] and d["excluded"], "metadata saying preview on a release GitHub does not mark prerelease is refused")
d = only(app("0.12.0", True, "2027"), **{"v0.12.0": {"channel": "nightly"}}); check(sum(heads(d).values(), []) == [], "an unknown channel is refused")
d = only(app("0.12.0", True, "2027"), **{"v0.12.0": {"channel": "preview", "product": "workbench"}}); check(sum(heads(d).values(), []) == [], "metadata naming the other product is refused")
d = only(rel("v0.12.0", True, "2027", "ClassicCartStudio-Setup-0.12.0.exe", "CCS-Console-Workbench-0.12.0.exe")); check(sum(heads(d).values(), []) == [], "a release carrying both products' files is refused")
d = only(rel("v0.12.0", True, "2027", "CCS-Console-Workbench-0.12.0.exe")); check(sum(heads(d).values(), []) == [], "an app tag carrying only the Workbench's file is refused")
d = only(rel("v0.12.0", True, "2027", "ClassicCartStudio-Setup-0.11.0.exe")); check(sum(heads(d).values(), []) == [], "an installer whose version differs from its tag is refused")
d = only(rel("v0.12.0-rc1", True, "2027", "ClassicCartStudio-Setup-0.12.0.exe")); check(sum(heads(d).values(), []) == [], "a suffixed tag is not a plain version and is refused")
d = only(app("0.12.0", True, "2027"), **{"v0.12.0": {"channel": "preview", "bundle": {"workbench": {"version": "0.12", "channel": "preview"}}}}); check(sum(heads(d).values(), []) == [], "an app release whose bundle is not well formed is refused")
d = only(app("0.12.0", True, "2027"), **{"v0.12.0": RuntimeError("download failed")}); check(d["complete"] is False and d["streams"] == [], "metadata that cannot be read makes the document incomplete: nothing listed")

# ---- the two earlier feeds, built from the streams (the same keys in the same order as before)
LIVE_KEYS = ["product", "version", "tag", "prerelease", "published", "url", "asset", "sums_url", "sha256", "utility", "protocol", "source"]
META.clear(); doc = RS.build(PUBLISHED, meta, sums, "x")
j = RN.latest_release_json("app", True, doc); w = RN.latest_release_json("workbench", True, doc)
check(list(j) == LIVE_KEYS and j["version"] == "0.11.3" and j["tag"] == "v0.11.3" and j["prerelease"] is True and j["asset"] == {"name": "ClassicCartStudio-Setup-0.11.3.exe", "size": 1000 + len("ClassicCartStudio-Setup-0.11.3.exe"), "url": DL + "v0.11.3/ClassicCartStudio-Setup-0.11.3.exe"} and j["sums_url"] == DL + "v0.11.3/SHA256SUMS.txt" and j["utility"] is None and j["source"] == "github-release", "release.json keeps its shape and names the app's current preview")
check(list(w) == LIVE_KEYS and w["version"] == "0.11.3" and w["tag"] == "workbench-v0.11.3" and w["asset"]["name"] == "CCS-Console-Workbench-0.11.3.exe", "workbench-release.json keeps its shape and names the Workbench's current preview (never 1.0.0)")
check(RN.latest_release_json("app", False, doc)["version"] == "0.10.1", "without previews, release.json names the newest production release")
check(RN.latest_release_json("app", True, {"schema": RS.SCHEMA, "complete": False, "streams": []})["version"] is None, "an incomplete lookup names no release")
live = os.environ.get("CCS_LIVE_RELEASE_JSON")   # optional: compare the key order with a saved copy of the live file
if live and os.path.exists(live): check(list(json.load(open(live, encoding="utf-8"))) == LIVE_KEYS, "the saved live release.json has the same keys in the same order")
check(RN.select_release([r for r in PUBLISHED if r["tagName"].startswith("workbench")], "workbench", True)[0]["tagName"] == "workbench-v0.11.3", "select_release takes the highest Workbench version and never the superseded 1.0.0")

# ---- Classic Cart Studio's decision (app_update.plan): the app stream of its own channel only
P = lambda d, cur, ch="preview", wbv=None: AU.plan(d, cur, ch, {"version": wbv} if wbv else None)
check(P(doc, "0.11.3")["entry"] is None and P(doc, "0.11.3")["newest"] == "0.11.3" and not P(doc, "0.11.3")["ahead"], "current: 0.11.3 preview is up to date")
check(P(d2, "0.11.3")["entry"]["version"] == "0.11.10", "newer: 0.11.3 preview is offered the newest preview, 0.11.10")
x = P(doc, "0.11.5"); check(x["entry"] is None and x["ahead"] is True, "running ahead of the feed: nothing offered (never a lower version)")
check(P(doc, "0.9.0", "preview")["newest"] == "0.11.3", "a preview build is never offered production v0.10.1")
check(P(doc, "0.10.0", "production")["entry"]["version"] == "0.10.1" and P(doc, "0.10.1", "production")["entry"] is None, "a production build is offered production v0.10.1 and never a preview")
wrong = copy.deepcopy(doc); s = next(s for s in wrong["streams"] if s["product"] == "app" and s["channel"] == "preview")
s["releases"].insert(0, dict(copy.deepcopy(s["releases"][0]), version="9.9.9", tag="v9.9.9", channel="production", asset={"name": "ClassicCartStudio-Setup-9.9.9.exe"}))
s["releases"].insert(0, dict(copy.deepcopy(s["releases"][1]), version="9.9.8", tag="workbench-v9.9.8", product="workbench", asset={"name": "CCS-Console-Workbench-9.9.8.exe"}))
check(P(wrong, "0.11.3")["entry"] is None, "an entry of another product or channel inside the app/preview stream is never offered")
wbig = copy.deepcopy(doc); next(s for s in wbig["streams"] if s["product"] == "workbench" and s["channel"] == "preview")["releases"].insert(0, {"product": "workbench", "channel": "preview", "version": "5.0.0", "tag": "workbench-v5.0.0", "asset": {"name": "CCS-Console-Workbench-5.0.0.exe"}})
check(P(wbig, "0.11.3")["entry"] is None, "a higher Workbench number is never compared with the app's")
check(P({"schema": RS.SCHEMA, "complete": False, "streams": []}, "0.11.3") is None, "incomplete: could not check")
check(P({"schema": "something-else", "complete": True, "streams": doc["streams"]}, "0.11.3") is None, "not the streams document: could not check")
check(P({"schema": RS.SCHEMA, "complete": True, "streams": [s for s in doc["streams"] if s["product"] != "app"]}, "0.11.3") is None, "no app stream: could not check")
bad = copy.deepcopy(doc); [e.update(version="0.11", tag="v0.11") for s in bad["streams"] if s["product"] == "app" and s["channel"] == "preview" for e in s["releases"]]
check(P(bad, "0.11.3") is None, "only entries that are not well formed: could not check")
check(P(doc, "0.11") is None and P(doc, "0.11.3", "beta") is None, "an unreadable running version or channel: could not check")
empty = copy.deepcopy(doc); next(s for s in empty["streams"] if s["product"] == "app" and s["channel"] == "preview")["releases"] = []
check(P(empty, "0.11.3")["entry"] is None and P(empty, "0.11.3")["newest"] is None, "a stream that lists nothing: no update")
# the Studio bundle
b = copy.deepcopy(d2)
check(P(b, "0.11.3", "preview", "0.11.3")["entry"]["version"] == "0.11.10", "a bundle-less newer preview is offered with an installed Workbench known")
st = next(s for s in b["streams"] if s["product"] == "app" and s["channel"] == "preview"); st["releases"] = [x for x in st["releases"] if x["version"] in ("0.11.4", "0.11.3")]
check(P(b, "0.11.3", "preview", "0.11.3")["entry"]["version"] == "0.11.4", "the 0.11.4 bundle (Workbench 0.11.4) is offered to Studio 0.11.3 with Workbench 0.11.3")
check(P(b, "0.11.3", "preview", "0.11.5")["entry"] is None, "a bundle whose Workbench is older than the installed one is not offered")
prod = copy.deepcopy(doc); ps = next(s for s in prod["streams"] if s["product"] == "app" and s["channel"] == "production")
ps["releases"].insert(0, dict(copy.deepcopy(ps["releases"][0]), version="0.10.2", tag="v0.10.2", asset={"name": "ClassicCartStudio-Setup-0.10.2.exe"}, bundle={"workbench": {"version": "0.11.4", "channel": "preview"}}))
check(P(prod, "0.10.1", "production")["entry"] is None, "a production bundle carrying a preview Workbench is never offered")
ps["releases"][0]["bundle"]["workbench"]["channel"] = "production"
check(P(prod, "0.10.1", "production")["entry"]["version"] == "0.10.2", "the same bundle with a production Workbench is offered")

# ---- check(): the answer, what is installed by the app and what goes to the download page (no network: _get answers from memory)
def serve(docs, terms=b"terms"):
    def g(url, timeout, out=None, progress=None):
        if url == AU.API:
            if isinstance(docs, Exception): raise docs
            return docs if isinstance(docs, bytes) else json.dumps(docs).encode()
        return terms
    return g
real_get, real_ch = AU._get, AU.CHANNEL
try:
    import socket
    for label, answer in (("offline", OSError("no network")), ("timeout", socket.timeout("timed out")), ("malformed JSON", b"{not json"), ("an HTML page", b"<html>")):
        AU._get = serve(answer); AU._plan.update(at=0.0, result=None, release=None)
        r = AU.check("0.11.3", True); check(r == {"ok": False, "error": AU.COULD_NOT}, f"{label}: Could not check, never up to date")
    AU._get = serve(d2); AU._plan.update(at=0.0, result=None, release=None); AU.CHANNEL = "preview"
    r = AU.check("0.11.3", True)
    check(r["ok"] and r["available"] and r["version"] == "0.11.10" and r["manual"] is True and r["channel"] == "preview" and AU._plan["release"] is None, "an unsigned preview is offered with the download page (manual); nothing is prepared for Update Now")
    class H:
        def _json(self, code, body): return code, body
    check(AU.handle(H(), "/api/update/download", {"version": "0.11.10"}, lambda n: n, "0.11.3")[0] == 409, "Update Now is refused for a preview offered by the download page")
    AU._get = serve(doc); AU._plan.update(at=0.0, result=None, release=None); AU.CHANNEL = "production"
    r = AU.check("0.10.0", True)
    check(r["ok"] and r["available"] and r["version"] == "0.10.1" and r["manual"] is False and AU._plan["release"]["version"] == "0.10.1", "a production release is offered for Update Now, with its checked files")
    AU._get = serve(doc); AU._plan.update(at=0.0, result=None, release=None)
    r = AU.check("0.10.1", True); check(r["ok"] and not r["available"] and r["newest"] == "0.10.1", "production 0.10.1 is up to date: the 0.11 previews are not offered")
finally:
    AU._get, AU.CHANNEL = real_get, real_ch

# ---- one channel per release, in the files the builds read
ch = open(os.path.join(REPO, "CHANNEL"), encoding="utf-8").read().strip()
check(ch in RS.CHANNELS and V.CHANNEL == ch, "CHANNEL names preview or production, and the backend's version.py says the same")
wv = open(os.path.join(REPO, "tools", "console-backup", "WORKBENCH-VERSION"), encoding="utf-8").read().strip()
wc = open(os.path.join(REPO, "tools", "console-backup", "WORKBENCH-CHANNEL"), encoding="utf-8").read().strip()
check(RS.version(wv) is not None and wc in RS.CHANNELS, "WORKBENCH-VERSION is a plain N.N.N and WORKBENCH-CHANNEL a known channel")
check(re.fullmatch(r"\d+\.\d+\.\d+", open(os.path.join(REPO, "VERSION"), encoding="utf-8").read().strip()) is not None, "VERSION is a plain N.N.N (no preview suffix)")

# ---- the page's part (studio/update.js, run in Node with stand-ins for the page): what the person sees for each answer
import shutil, subprocess
node = shutil.which("node") or (r"C:\Program Files\nodejs\node.exe" if os.path.exists(r"C:\Program Files\nodejs\node.exe") else None)
upd = open(os.path.join(REPO, "tools", "rom-pipeline", "studio", "update.js"), encoding="utf-8").read()
harness = r"""
const DOM={}, TOASTS=[]; let LAST=null, NEXT=null;
const $=id=>DOM[id]||null; const esc=s=>String(s); const fmt=n=>n+' bytes'; const toast=t=>TOASTS.push(t);
function openColl(title, sub, html){ LAST={title, html}; DOM.collTitle={textContent:title}; DOM.collm={classList:{contains:()=>true}}; DOM.updNo={}; DOM.updGo={addEventListener(){}}; }
function closeColl(){} function procSet(){} async function capi(){ return {running:false}; }
async function hkPost(path, body){ return NEXT; }
const CO={desktop:false, ok:true}; const CCS_APP_DOWNLOAD='https://classiccartstudio.com/app/';
const document={addEventListener(){}}; const window={};
""" + upd + r"""
CO.desktop=true;
(async()=>{ const out={};
 async function run(key, answer, manual){ NEXT=answer; TOASTS.length=0; LAST=null; UPD.shown=''; await updCheck(manual); out[key]={toasts:TOASTS.slice(), dialog:LAST}; }
 await run('couldnot', {ok:false, error:'Classic Cart Studio could not check for updates. Check the internet connection and try again.'}, true);
 await run('quiet_couldnot', {ok:false, error:'x'}, false);
 await run('uptodate', {ok:true, available:false, current:'0.11.4', channel:'preview'}, true);
 await run('ahead', {ok:true, available:false, ahead:true, current:'0.11.5', channel:'preview'}, true);
 await run('manual', {ok:true, available:true, version:'0.11.6', current:'0.11.4', channel:'preview', manual:true, size:5}, true);
 await run('install', {ok:true, available:true, version:'1.0.1', current:'1.0.0', channel:'production', manual:false, size:5}, true);
 await run('notice', {ok:true, available:true, version:'0.11.6', current:'0.11.4', channel:'preview', manual:true, size:5}, false);
 console.log(JSON.stringify(out)); })();
"""
if node:
    r = subprocess.run([node, "-e", harness], capture_output=True, text=True, timeout=60)
    try: o = json.loads(r.stdout.strip().splitlines()[-1])
    except Exception: o = {}; print(r.stdout[-400:], r.stderr[-800:])
    t = lambda k: " | ".join((o.get(k) or {}).get("toasts") or [])
    dlg = lambda k: (o.get(k) or {}).get("dialog") or {}
    check("could not check" in t("couldnot") and "up to date" not in t("couldnot").lower(), "page: a failed check says it could not check, never up to date")
    check(t("quiet_couldnot") == "" and not dlg("quiet_couldnot"), "page: the automatic check says nothing when it could not check")
    check("Classic Cart Studio is up to date (0.11.4 (Preview))." in t("uptodate"), "page: up to date, with the running version and (Preview)")
    check("newer than the newest published version" in t("ahead"), "page: a version ahead of the published ones says so")
    m = dlg("manual")
    check(m.get("title") == "Classic Cart Studio Update Available" and "Classic Cart Studio update available:" in m.get("html", "") and 'href="https://classiccartstudio.com/app/"' in m.get("html", "")
          and "Open Download Page" in m.get("html", "") and "Update Now" not in m.get("html", "") and "0.11.6 (Preview)" in m.get("html", ""), "page: an unsigned preview is offered with Open Download Page (the website's /app/), never Update Now")
    i = dlg("install")
    check("Update Now" in i.get("html", "") and "classiccartstudio.com/app/" not in i.get("html", "") and "version 1.0.1." in i.get("html", ""), "page: a production release keeps Update Now (checked download and install)")
    check(not dlg("notice") and any("Classic Cart Studio update available: 0.11.6 (Preview)" in x for x in (o.get("notice") or {}).get("toasts") or []), "page: the automatic check never opens a dialog; it says Classic Cart Studio update available")
else:
    check(False, "Node was not found to run update.js")

fails =[n for ok, n in results if not ok]
print("RESULT %d/%d" % (len(results) - len(fails), len(results)))
sys.exit(1 if fails else 0)
