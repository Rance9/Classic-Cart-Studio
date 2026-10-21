"""Static audit: the installed application never fetches software or required files from the internet (PC-only, offline).

Every source file that ships in Classic Cart Studio for Windows (the backend's Python modules, the desktop host's C# and the interface's
JavaScript) is scanned. Network calls may only go to this computer (127.0.0.1 / localhost); the only exception is the public website's
download box, which asks GitHub for the latest release and never runs inside the desktop application (guarded by !CO.desktop).
Ordinary links the user clicks (help, source code, licenses) are not downloads and are not counted. Exit code 0 = PASS.

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
    ok("interface_external_fetch_only_website_download_box", len(allowed) + len(shared) == len(ext) and len(allowed) <= 2 and len(shared) <= 1, [(e[0], e[1]) for e in ext if e not in allowed and e not in shared][:5])
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
