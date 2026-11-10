"""Static audit: the installed application never fetches software or required files from the internet (PC-only, offline).

Every source file that ships in Classic Cart Studio for Windows (the backend's Python modules, the desktop host's C# and the interface's
JavaScript) is scanned. Network calls may only go to this computer (127.0.0.1 / localhost); the only exception is the public website's
download box, which asks GitHub for the latest release and never runs inside the desktop application (guarded by !CO.desktop), and the
optional usage reports (D-123/D-124): one fixed collector address, contacted only while the person's choice is On, never needed for any
feature and never a download (the app's backend relay, the website's own fetch; checked exactly in optional_usage_reports_only). Ordinary links the user clicks (help, source code, licenses) are not downloads and are not counted. Exit code 0 = PASS.

  python test_offline_runtime.py"""
from __future__ import annotations
import os, re, sys
HERE = os.path.dirname(os.path.abspath(__file__)); TOOLS = os.path.dirname(HERE)
DEV_ONLY = re.compile(r"(^|[\\/])(test_|shot_|e2e_|dbg_|poc_|check_|websocket_min)[^\\/]*$|[\\/](__pycache__|bin|obj|tests?)[\\/]")
PY_NET = re.compile(r"urlopen\(|urllib\.request\.Request\(|http\.client|requests\.(get|post)|socket\.create_connection|Invoke-WebRequest|Invoke-RestMethod|DownloadFile|Start-BitsTransfer")
CS_NET = re.compile(r"\b(HttpClient|WebClient|HttpWebRequest|WebRequest\.Create|DownloadFile(Async)?|BackgroundDownloader)\b")
JS_NET = re.compile(r"\bfetch\(\s*([^,)]+)|XMLHttpRequest|navigator\.sendBeacon|new\s+WebSocket\(\s*([^,)]+)")
LOOPBACK = re.compile(r"127\.0\.0\.1|localhost|CO\.base|location\.|^'/|^`/|^'[a-z0-9_./-]+(\.json|\.html)'|^path\b|^url\b|^u\b|^src\b")

def files(root, exts):
    for d, _, ns in os.walk(root):
        for n in ns:
            p = os.path.join(d, n)
            if n.endswith(exts) and not DEV_ONLY.search(p): yield p

COLLECTOR = "https://metrics.classiccartstudio.com/v1/events"
URL = re.compile(r"https?://[^\s\"'`)<>]+")
def usage_reports():
    """the optional usage reports, exactly: the backend relay (companion/metrics_relay.py) sends only to COLLECTOR, from _send alone, which
    only relay() calls, which handle() reaches only after the choice in force is On, and whose sending thread checks again (no decision
    since admission, still On) at the moment it would send; the page (studio/metrics.js) fetches only CCSM.url = COLLECTOR, once, in
    ccsmTrack after its permission check (Off held, or the choice read again) and after the desktop app's return (in the app every count
    goes to the backend)"""
    import ast
    d = {}
    with open(os.path.join(HERE, "metrics_relay.py"), encoding="utf-8") as f: rs = f.read()
    with open(os.path.join(TOOLS, "rom-pipeline", "studio", "metrics.js"), encoding="utf-8") as f: ms = f.read()
    t = ast.parse(rs); fn = {n.name: n for n in t.body if isinstance(n, ast.FunctionDef)}
    consts = [n for n in t.body if isinstance(n, ast.Assign) and any(getattr(x, "id", "") == "COLLECTOR" for x in n.targets)]
    d["relay_one_fixed_address"] = len(consts) == 1 and isinstance(consts[0].value, ast.Constant) and consts[0].value.value == COLLECTOR and URL.findall(rs) == [COLLECTOR]
    send, rl = fn.get("_send"), rs.splitlines(); lines = [i for i, line in enumerate(rl, 1) if PY_NET.search(line)]
    d["relay_net_only_in_send"] = bool(send) and bool(lines) and all(send.lineno <= i <= send.end_lineno for i in lines) and "COLLECTOR" in ast.get_source_segment(rs, send)
    calls = [n for n in ast.walk(t) if isinstance(n, ast.Call) and getattr(n.func, "id", "") == "_send"]
    d["send_only_from_relay"] = len(calls) == 1 and "relay" in fn and fn["relay"].lineno <= calls[0].lineno <= fn["relay"].end_lineno
    h = ast.get_source_segment(rs, fn["handle"]) if "handle" in fn else ""
    gate, use = h.find("if not _permitted(data_path): return"), h.find("relay(d, data_path)")
    d["relay_only_after_choice_on"] = 0 <= gate < use and h.count("relay(") == 1
    pm = ast.get_source_segment(rs, fn["_permitted"]) if "_permitted" in fn else ""
    d["permitted_is_held_off_and_the_file"] = 'return not _held_off and load_choice(data_path)["choice"] == "on"' in pm
    rr = ast.get_source_segment(rs, fn["relay"]) if "relay" in fn else ""
    chk, snd = rr.find("go = epoch == _epoch and _permitted(data_path)"), rr.find("if go: _send(d)")
    d["relay_checks_again_at_the_send"] = 0 <= chk < snd and "epoch = _epoch" in rr
    i = ms.find("function ccsmTrack("); j = ms.find("\nfunction ", i + 1) if i >= 0 else -1; tr = ms[i:j] if 0 <= i < j else ""
    on, app, ret, fe = tr.find("||!ccsmPermitted()) return;"), tr.find("if(ccsmApp()){"), tr.find("return; }"), tr.find("fetch(CCSM.url,")
    k = ms.find("function ccsmPermitted("); pg = ms[k:ms.find("\n", ms.find("\n", k) + 1)] if k >= 0 else ""
    d["page_permission_held_off_and_read_again"] = "if(CCSM.held) return false;" in pg and "const v=ccsmStored();" in pg and "return v==='on';" in pg
    d["page_one_fixed_address"] = f"const CCSM={{url:'{COLLECTOR}'," in ms and URL.findall(ms) == [COLLECTOR] and ms.count("fetch(") == 1
    d["page_fetch_only_on_website_after_on_check"] = 0 <= on < app < ret < fe
    good = all(d.values())
    relay_lines = {f"companion{os.sep}metrics_relay.py:{k}: {rl[k - 1].strip()[:140]}" for k in lines} if good else set()
    return {"ok": good, "page_ok": good, "relay_lines": relay_lines, "detail": d}

def main():
    checks = []; ok = lambda k, v, n="": checks.append((k, bool(v), n))
    # Python modules bundled with the backend (tools/companion and the rom-pipeline modules it imports)
    hits = []
    for p in list(files(HERE, (".py", ".ps1"))) + list(files(os.path.join(TOOLS, "rom-pipeline"), (".py",))):
        if os.path.basename(p) in ("companion.spec",): continue
        for i, line in enumerate(open(p, encoding="utf-8", errors="replace"), 1):
            if PY_NET.search(line) and not re.search(r"127\.0\.0\.1|localhost|f\"http://127|\{B\}|\bB \+", line): hits.append(f"{os.path.relpath(p, TOOLS)}:{i}: {line.strip()[:140]}")
    rp = os.path.join(TOOLS, "rom-pipeline")   # pipeline tools that run only on the developer's PC (not imported by the backend)
    bundled_pipeline = {"common.py", "import_selected.py", "manifest_v4.py", "library_lookup.py", "personal_store.py", "catalog.py"}
    hits = [h for h in hits if not h.startswith("rom-pipeline") or os.path.basename(h.split(":")[0]) in bundled_pipeline]
    usage = usage_reports(); hits = [h for h in hits if h not in usage["relay_lines"]]   # the optional usage reports' one send (checked below)
    ok("backend_no_external_network_calls", not hits, hits[:5])
    # the desktop host
    # (WebRequest to Base / baseUrl: the application's own backend on 127.0.0.1, Backend.cs)
    cs = [f"{os.path.relpath(p, TOOLS)}:{i}" for p in files(os.path.join(TOOLS, "desktop", "ClassicCartStudio"), (".cs",)) for i, l in enumerate(open(p, encoding="utf-8", errors="replace"), 1)
          if CS_NET.search(l) and not re.search(r"WebRequest\.Create\((Base|baseUrl) \+", l)]
    ok("desktop_backend_base_is_loopback", re.search(r'Base => "http://127\.0\.0\.1:"', open(os.path.join(TOOLS, "desktop", "ClassicCartStudio", "Backend.cs"), encoding="utf-8").read()))
    ok("desktop_host_no_downloads", not cs, cs[:5])
    # the interface
    ext = []
    for p in files(os.path.join(TOOLS, "rom-pipeline", "studio"), (".js", ".html")):
        src = open(p, encoding="utf-8", errors="replace").read()
        for m in JS_NET.finditer(src):
            arg = (m.group(1) or m.group(2) or "").strip()
            if not arg or LOOPBACK.search(arg): continue
            ext.append((os.path.basename(p), arg[:60], src[max(0, m.start() - 200):m.start()]))
    allowed = [e for e in ext if e[1] in ("RELEASES_API", "s.browser_download_url") and "!CO.desktop" in e[2] + "".join(x[2] for x in ext if x[1] == "RELEASES_API")]
    # Share Collection: the sharing service is called only from the share dialog's own actions (publish, unpublish) and when a shared
    # link is opened; nothing the application needs to work is fetched from it
    shared = [e for e in ext if e[0] == "tv-native.js" and e[1] == "im.src"]   # im.src: the preview frame already on screen (a local blob); public share links are retired
    # the website's game player (W1): cpAsset() only puts the page's own relative folder before a file name (checked below, exactly), and
    # the service worker fetches only this site's own files (its fetch handler returns for any other origin; covers only as art/... paths)
    st = os.path.join(TOOLS, "rom-pipeline", "studio"); cps = open(os.path.join(st, "ccs-play.js"), encoding="utf-8").read() if os.path.exists(os.path.join(st, "ccs-play.js")) else ""
    sws = open(os.path.join(st, "sw.js"), encoding="utf-8").read() if os.path.exists(os.path.join(st, "sw.js")) else ""
    cp_same = "function cpAsset(p){ return (typeof CP_ASSET_BASE==='string'?CP_ASSET_BASE:'play/')+p; }" in cps and not any(re.search(r"\bCP_ASSET_BASE\s*=(?!=)", open(f, encoding="utf-8", errors="replace").read()) for f in files(st, (".js", ".html")))   # nothing in the shipped page sets another base
    sw_same = "if(u.origin !== SCOPE.origin || !u.pathname.startsWith(SCOPE.pathname)) return;" in sws and r"/^art\/[A-Za-z0-9_./-]+\.(jpg|png|webp)$/.test(a)" in sws
    ok("web_player_fetches_only_this_sites_files", cp_same and sw_same, {"cpAsset_same_site": cp_same, "service_worker_same_origin": sw_same})
    shared += [e for e in ext if (cp_same and e[0] == "ccs-play.js" and e[1].startswith("cpAsset(")) or (sw_same and e[0] == "sw.js" and e[1] in ("q", "at(a"))]   # (the scan stops at the first parenthesis)
    shared += [e for e in ext if usage["page_ok"] and e[0] == "metrics.js" and e[1] == "CCSM.url"]   # the website's optional usage count (below)
    ok("optional_usage_reports_only", usage["ok"], usage["detail"])
    ok("interface_external_fetch_only_website_download_box", len(allowed) + len(shared) == len(ext) and len(allowed) <= 2 and len([e for e in shared if e[0] == "tv-native.js"]) <= 1, [(e[0], e[1]) for e in ext if e not in allowed and e not in shared][:5])
    src = open(os.path.join(TOOLS, "rom-pipeline", "studio", "share.js"), encoding="utf-8").read() if os.path.exists(os.path.join(TOOLS, "rom-pipeline", "studio", "share.js")) else ""
    ok("no_sharing_service_calls", not re.search(r"\bfetch\(|XMLHttpRequest|sendBeacon|WebSocket|share\.classiccartstudio\.com/api", src) and not os.path.exists(os.path.join(TOOLS, "publish", "site-static", "share-config.json")))   # .ccs files are made locally; nothing is uploaded
    # the Hakchi bridge has no download source at all
    sys.path[:0] = [HERE, os.path.join(TOOLS, "rom-pipeline")]
    import hakchi_bridge as hb
    src = open(hb.__file__, encoding="utf-8").read()
    ok("hakchi_bridge_has_no_download_source", "url" not in hb.HAKCHI_PACKAGE and not re.search(r"https?://", src) and "urllib" not in src)
    for k, v, n in checks: print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({n})" if n and not v else ""))
    bad = [k for k, v, _ in checks if not v]; print("RESULT:", "PASS" if not bad else "FAIL " + str(bad), f"({len(checks)} checks)"); return 0 if not bad else 1

if __name__ == "__main__": sys.exit(main())
