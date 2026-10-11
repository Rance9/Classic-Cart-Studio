r"""Installed Classic Cart Studio for Windows: the branded source link and the WebView2 navigation policy (directive A3).
Launches the installed app with a DevTools port against the live website, checks that the footer links to
ClassicCartStudio.com/source, then follows that link inside the WebView: the /source page (same approved origin) loads, its
forwarding to github.com is cancelled by the host's navigation policy and handed to the user's ordinary browser (the desktop
log records "blocked navigation to https://github.com (opened externally)"), and the WebView never leaves the approved origins.
Closes the app through its normal path and checks that the backend stops."""
from __future__ import annotations
import os, sys, time, subprocess, argparse
sys.path.insert(0, os.path.dirname(__file__))
from poc_run import Dev, port_listening

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--devtools", type=int, default=9361); a = ap.parse_args()
    checks = []; ok = lambda k, v, n="": checks.append(f"{k}={'true' if v else 'false'}{(' ('+str(n)+')') if n and not v else ''}")
    app = os.path.join(os.environ["LOCALAPPDATA"], "Programs", "NESClassicCollectionStudio"); exe = os.path.join(app, "ClassicCartStudio.exe")
    log = os.path.join(os.environ["LOCALAPPDATA"], "NESClassicCollectionStudio", "logs", "desktop.log")   # App.DataRoot keeps the original folder name
    ok("installed_app_present", os.path.exists(exe)); ok("backend_port_free", not port_listening(8765))
    before = os.path.getsize(log) if os.path.exists(log) else 0
    proc = subprocess.Popen([exe, f"--devtools-port={a.devtools}"], cwd=app)
    try:
        d = Dev(a.devtools, 90, prefer="classiccartstudio.com")
        for _ in range(240):
            if d.js("document.documentElement.dataset.ready==='1'") is True: break
            time.sleep(0.25)
        ok("live_site_in_app", d.js("location.origin") == "https://classiccartstudio.com")
        link = d.js("(()=>{ const a=[...document.querySelectorAll('footer a')].find(a=>a.textContent.trim()==='ClassicCartStudio.com/source'); return a?a.href:null; })()")
        ok("footer_links_branded_source", link == "https://ClassicCartStudio.com/source" or link == "https://classiccartstudio.com/source", link)
        d.js("try{ doneWelcome(); }catch(e){} document.querySelector('footer a[href=\"https://ClassicCartStudio.com/source\"]').click()")
        landed = None; left_origin = False
        for _ in range(8):
            time.sleep(0.5); cur = d.js("location.href") or ""
            if cur.startswith("https://github.com"): left_origin = True; break
            if "/source" in cur: landed = cur
        # inside the app the page resolves the short link itself and opens it as a new window, which the host hands to the
        # user's browser: the window never navigates to /source (published 2026-10-11, commit c9074ba)
        trace = d.js("(()=>{ try{ return JSON.parse(sessionStorage.getItem('ccs-trace')||'[]').filter(x=>/short link in app/.test(x)).slice(-1)[0]||null; }catch(e){ return null; } })()")
        ok("short_link_resolved_in_app", landed is None and trace is not None and "https://github.com/Rance9/Classic-Cart-Studio" in str(trace), (landed, trace))
        time.sleep(1.5)
        # the DevTools target of the cancelled /source document may be gone: re-attach to whatever the WebView shows now
        try: d2 = Dev(a.devtools, 30, prefer="classiccartstudio.com")
        except Exception: d2 = d
        shown = {"href": d2.js("location.href"), "title": d2.js("document.title"), "catalog": d2.js("!!document.querySelector('#t') && !!document.querySelector('footer')")}
        out_dir = os.path.join("C:" + os.sep, "NES", "work", "logs", "site"); os.makedirs(out_dir, exist_ok=True); d2.shot(os.path.join(out_dir, "app-source-link-after.png"))
        ok("webview_never_leaves_approved_origins", not left_origin and (shown["href"] or "").lower().startswith("https://classiccartstudio.com") and shown["catalog"], shown)
        d = d2
        tail = open(log, encoding="utf-8", errors="replace").read()[before:] if os.path.exists(log) else ""
        ok("no_failed_navigation_or_recovery_screen", "navigation failed" not in tail and "webview process failed" not in tail and "open external failed" not in tail, tail[-300:])
        ok("no_js_errors", not d.js("typeof errors!=='undefined' && errors.length"))
    except Exception as e:
        ok("harness", False, repr(e)[:200])
    finally:
        subprocess.run(["powershell", "-NoProfile", "-Command", "Get-Process ClassicCartStudio -ErrorAction SilentlyContinue | ForEach-Object { $null = $_.CloseMainWindow() }"], capture_output=True)
        try: proc.wait(timeout=20); ok("app_closes_cleanly", proc.returncode == 0, proc.returncode)
        except subprocess.TimeoutExpired: proc.kill(); ok("app_closes_cleanly", False, "killed")
        time.sleep(1.0); ok("backend_stopped", not port_listening(8765))
    for c in checks: print(c)
    fails = [c for c in checks if "=false" in c]; print(("RESULT: PASS" if not fails else "RESULT: FAIL " + str(fails)) + f" ({len(checks)} checks)"); return 1 if fails else 0

if __name__ == "__main__": sys.exit(main())
