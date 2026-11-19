"""Classic Cart Studio for Windows: the update check and the in-place update (product and channel streams,
0.11.4).

The only internet addresses: the website's release streams (STREAMS: release-streams.json on classiccartstudio.com, written from this
project's GitHub releases by tools/publish/release_streams.py) and, only after the person chooses Update Now, files of the offered release
(DOWNLOADS: its checksums, its build information, its Installation Terms and its installer). The check runs shortly after the app starts and
once a day while it is open, unless the person turned it off in the Studio menu (update-settings.json in the data folder); nothing about the
person or this computer is sent.

What is offered: only a newer Classic Cart Studio release of this app's own channel (CHANNEL in version.py: a preview is offered only newer
previews, a production release only newer production releases), never a CCS Console Workbench release, never a lower version, never a
draft or a superseded release (the streams leave them out). The release is the Studio bundle: it carries the Workbench this installation
also holds (installed as workbench/CCS-Console-Workbench.exe), so both installed parts are compared with it and the person sees one Classic
Cart Studio update. A bundle whose Workbench is older than the installed one, or a production bundle carrying a preview Workbench, is not
offered. An answer that cannot be read, is incomplete or names another product or channel is "could not check", never "up to date".

How it is installed: a production release (or a signed preview) is installed in place: the installer is kept in this app's data folder
(never in the program folder, which the installer replaces); before it runs, its size and SHA-256 must equal what the release's
SHA256SUMS.txt and build-info.json give for it, and it is read again at the moment it runs. It runs quietly with /CCSUPDATE=1: it waits
for this app to close, installs, and starts the app again. When the release's Installation Terms differ from the ones installed with this
app, the installer runs with its pages instead, so the person reads and agrees to them. An unsigned preview is not installed by the app:
the offer opens the website's download page (/app/, from the page) in the browser instead ("manual"), and nothing is downloaded.
CCS_UPDATE_FEED (tests only) may name a loopback test server instead of the two addresses; nothing else changes them.

  handle(h, path, body, data_path, version): /api/update/settings | check | download | status | cancel | install (desktop principal)"""
from __future__ import annotations
import hashlib, json, os, re, struct, subprocess, sys, threading, time, urllib.request
import authenticode
from version import CHANNEL

STREAMS = "https://classiccartstudio.com/release-streams.json"
DOWNLOADS = "https://github.com/Rance9/Classic-Cart-Studio/releases/download/"
_FEED = os.environ.get("CCS_UPDATE_FEED", "")
if not re.fullmatch(r"http://127\.0\.0\.1:\d{2,5}/", _FEED): _FEED = ""
API, DL = (_FEED + "release-streams.json", _FEED) if _FEED else (STREAMS, DOWNLOADS)
SCHEMA = "ccs-release-streams/1"
WORKBENCH_EXE = os.path.join("workbench", "CCS-Console-Workbench.exe")   # the Workbench bundled with this installation: <installation>\workbench\ (installer.iss)
SETTINGS = "update-settings.json"
CHECK_EVERY = 3600   # a check answered within the hour is reused (the page asks at start and daily; Check for Updates… asks now)
COULD_NOT = "Classic Cart Studio could not check for updates. Check the internet connection and try again."

_lock = threading.Lock()
STATE = {"phase": "idle", "version": "", "bytes_done": 0, "bytes_total": 0, "error": "", "terms_changed": False}
_plan = {"at": 0.0, "result": None, "release": None}
_job = {"cancel": False, "path": "", "sha256": "", "size": 0}
# Authenticode publisher of Classic Cart Studio's releases: the certificate subject's CN or O, set once the release identity is validated
# (the intended publisher is Rance9 LLC; the pin is taken from the actual validated certificate, never typed in ahead of it).
# When set, every update must be signed by one of these names with a valid chain and a timestamp (never a certificate thumbprint: the
# signing certificates are short-lived and renewed). While empty, a release that says it is signed is held to the publisher it names.
EXPECTED_PUBLISHERS: list[str] = []

def _signed_by(path, names):
    """True when Windows reports a Valid, timestamped, embedded Authenticode signature on path by one of names (CN or O of the signer):
    authenticode.py, the release build's own check, whatever shell started this app. Anything that cannot be checked is False."""
    if os.name != "nt" or not names: return False
    try: return not authenticode.problems(authenticode.inspect([path], timeout=60)[0], names)
    except Exception: return False


def _ver(v):
    m = re.fullmatch(r"v?(\d{1,4})\.(\d{1,4})\.(\d{1,4})", str(v or "").strip())
    return tuple(int(x) for x in m.groups()) if m else None


def _get(url, timeout, out=None, progress=None):
    """the only internet access: the fixed release streams, or a file of one of this project's releases (followed to GitHub's own
    download host). Returns the bytes, or with out= streams to that file and returns (sha256, size)."""
    if not (url == API or url.startswith(DL)): raise ValueError("not an address of this project's releases")
    req = urllib.request.Request(url, headers={"Accept": "application/json" if url == API else "application/octet-stream", "User-Agent": "ClassicCartStudio-Update"})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        if out is None: return r.read(8 << 20)
        h, done, total = hashlib.sha256(), 0, int(r.headers.get("Content-Length") or 0)
        while True:
            b = r.read(1 << 20)
            if not b: break
            out.write(b); h.update(b); done += len(b)
            if progress and not progress(done, total): raise InterruptedError("canceled")
        return h.hexdigest(), done


def _settings(data_path, auto=None):
    p = data_path(SETTINGS); s = {"schema": "ccs-update-settings/1", "auto": True}
    try:
        with open(p, encoding="utf-8") as f: d = json.load(f)
        if isinstance(d, dict) and isinstance(d.get("auto"), bool): s["auto"] = d["auto"]
    except Exception: pass
    if auto is not None:
        s["auto"] = bool(auto); tmp = p + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f: json.dump(s, f)
        os.replace(tmp, p)
    return s


def _local_terms():
    """the Installation Terms installed with this app ({app}\\legal), or this checkout's maintained copy when run from source"""
    here = os.path.dirname(os.path.abspath(sys.executable if getattr(sys, "frozen", False) else __file__))
    for p in (os.path.join(here, "legal", "INSTALLATION-TERMS.txt"), os.path.join(here, "..", "publish", "assets", "legal", "INSTALLATION-TERMS.txt")):
        try:
            with open(p, "rb") as f: return f.read()
        except OSError: pass
    return None


def _norm(b):
    return (b or b"").lstrip(b"\xef\xbb\xbf").replace(b"\r\n", b"\n").strip()


def _assets(entry, vs):
    """the release's own files for version vs (from its stream entry), each at its fixed download address, or None"""
    want = {"exe": f"ClassicCartStudio-Setup-{vs}.exe", "sums": "SHA256SUMS.txt", "info": "build-info.json"}
    out = {}
    for a in entry.get("files") or []:
        if not isinstance(a, dict): continue
        n, u = str(a.get("name") or ""), str(a.get("url") or "")
        if not u.startswith(f"{DL}v{vs}/"): continue
        for k, w in want.items():
            if n == w: out[k] = {"name": n, "url": u, "size": int(a.get("size") or 0)}
        if re.fullmatch(r"ClassicCartStudio-Installation-Terms-\d+(\.\d+)*\.txt", n): out["terms"] = {"name": n, "url": u, "size": int(a.get("size") or 0)}
    return out if all(k in out for k in want) else None


def _file_version(path):
    """the ProductVersion text of a Windows program's version information, or None (reads only)"""
    if os.name != "nt" or not os.path.isfile(path): return None
    try:
        import ctypes
        from ctypes import wintypes
        v = ctypes.WinDLL("version")
        v.GetFileVersionInfoSizeW.argtypes = [wintypes.LPCWSTR, ctypes.POINTER(wintypes.DWORD)]; v.GetFileVersionInfoSizeW.restype = wintypes.DWORD
        v.GetFileVersionInfoW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p]; v.GetFileVersionInfoW.restype = wintypes.BOOL
        v.VerQueryValueW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR, ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(wintypes.UINT)]; v.VerQueryValueW.restype = wintypes.BOOL
        n = v.GetFileVersionInfoSizeW(path, None)
        if not n or n > (1 << 20): return None
        buf = ctypes.create_string_buffer(n)
        if not v.GetFileVersionInfoW(path, 0, n, buf): return None
        p, ln = ctypes.c_void_p(), wintypes.UINT()
        if not v.VerQueryValueW(buf, "\\VarFileInfo\\Translation", ctypes.byref(p), ctypes.byref(ln)) or ln.value < 4: return None
        lang, cp = struct.unpack("<HH", ctypes.string_at(p.value, 4))
        if not v.VerQueryValueW(buf, "\\StringFileInfo\\%04x%04x\\ProductVersion" % (lang, cp), ctypes.byref(p), ctypes.byref(ln)) or not ln.value: return None
        return ctypes.wstring_at(p.value, ln.value).rstrip("\0").strip()
    except Exception: return None


def installed_workbench():
    """the CCS Console Workbench installed with this app (its version from the program's own version information), or None when this
    is not an installation that bundles it (run from source, or an earlier layout)"""
    if not getattr(sys, "frozen", False): return None
    vs = _file_version(os.path.join(os.path.dirname(os.path.abspath(sys.executable)), WORKBENCH_EXE))
    return {"version": vs} if _ver(vs) and "%d.%d.%d" % _ver(vs) == vs else None


def _bundle_ok(e, channel, workbench):
    """an app release may replace this installation's two parts: its bundled Workbench is never older than the installed one, and a
    production release never carries a preview Workbench. A release that names no bundle (every release up to 0.11.3) is accepted."""
    b = e.get("bundle")
    if b is None: return True
    wb = b.get("workbench") if isinstance(b, dict) else None
    if not isinstance(wb, dict) or wb.get("channel") not in ("production", "preview"): return False
    v = _ver(wb.get("version"))
    if not v or "%d.%d.%d" % v != wb.get("version"): return False
    if channel == "production" and wb["channel"] != "production": return False
    iv = _ver((workbench or {}).get("version"))
    return not (iv and v < iv)


def plan(doc, current, channel=CHANNEL, workbench=None):
    """the update decision from the release streams (pure; the tests call it): None when the answer cannot be used (not the streams
    document, incomplete, no app stream of this channel, or only entries that are not well formed); otherwise {"newest": the newest
    release this installation may take (or None), "entry": its stream entry when it is newer than current, "ahead": current is newer
    than every release listed}. Only app entries of this channel count, each checked before its version is compared."""
    cur = _ver(current)
    if not cur or channel not in ("production", "preview"): return None
    if not isinstance(doc, dict) or doc.get("schema") != SCHEMA or doc.get("complete") is not True or not isinstance(doc.get("streams"), list): return None
    st = [x for x in doc["streams"] if isinstance(x, dict) and x.get("product") == "app" and x.get("channel") == channel]
    if len(st) != 1 or not isinstance(st[0].get("releases"), list): return None
    listed, ok = st[0]["releases"], []
    for e in listed:
        if not isinstance(e, dict) or e.get("product") != "app" or e.get("channel") != channel: continue
        v = _ver(e.get("version"))
        if not v or "%d.%d.%d" % v != e.get("version") or e.get("tag") != "v" + e["version"]: continue
        a = e.get("asset") if isinstance(e.get("asset"), dict) else {}
        if a.get("name") != f"ClassicCartStudio-Setup-{e['version']}.exe": continue
        ok.append((v, e))
    if listed and not ok: return None
    fit = [(v, e) for v, e in ok if _bundle_ok(e, channel, workbench)]
    best = max(fit, key=lambda x: x[0]) if fit else None
    return {"newest": best[1]["version"] if best else None, "entry": best[1] if best and best[0] > cur else None, "ahead": bool(ok) and all(v < cur for v, _ in ok)}


def check(current, force=False):
    now = time.time()
    with _lock:
        if not force and _plan["result"] and now - _plan["at"] < CHECK_EVERY: return _plan["result"]
    try: doc = json.loads(_get(API, 8).decode("utf-8"))
    except Exception: return {"ok": False, "error": COULD_NOT}
    wb = installed_workbench()
    pl = plan(doc, current, CHANNEL, wb)
    if pl is None: return {"ok": False, "error": COULD_NOT}
    res = {"ok": True, "available": False, "current": current, "channel": CHANNEL, "newest": pl["newest"], "ahead": pl["ahead"], "workbench": (wb or {}).get("version")}
    rel = None
    e = pl["entry"]
    if e:
        vs = e["version"]; a = _assets(e, vs)
        res.update(available=True, version=vs, size=int((e.get("asset") or {}).get("size") or 0), manual=True, terms_changed=False)
        b = e.get("bundle") if isinstance(e.get("bundle"), dict) else None
        if b: res["bundle_workbench"] = b["workbench"]["version"]
        # installed by the app: a production release, or a preview that says it is signed (its publisher is then checked before it runs);
        # an unsigned preview is downloaded by the person from the download page
        if a and (e.get("channel") == "production" or e.get("signed") is True):
            changed = True
            if "terms" in a and a["terms"]["size"] <= (1 << 20):
                try: changed = _norm(_get(a["terms"]["url"], 15)) != _norm(_local_terms())
                except Exception: changed = True   # not known: the installer shows its pages (the person sees the terms either way)
            res.update(size=a["exe"]["size"], manual=False, terms_changed=changed)
            rel = {"version": vs, "assets": a, "terms_changed": changed}
    with _lock: _plan.update(at=now, result=res, release=rel)
    return res


def _download(data_path):
    rel = _plan["release"]; a = rel["assets"]; vs = rel["version"]; d = data_path("updates"); os.makedirs(d, exist_ok=True)
    for n in os.listdir(d):   # an earlier, unfinished or older update is never reused
        try: os.remove(os.path.join(d, n))
        except OSError: pass
    try:
        sums = _get(a["sums"]["url"], 30).decode("utf-8", "replace"); info = json.loads(_get(a["info"]["url"], 30).decode("utf-8"))
        line = re.search(r"(?m)^([0-9a-fA-F]{64})\s+\*?" + re.escape(a["exe"]["name"]) + r"\s*$", sums)
        want = line.group(1).lower() if line else ""
        if not want or str(info.get("version")) != vs or str(info.get("installer")) != a["exe"]["name"] or str(info.get("sha256", "")).lower() != want or int(info.get("size") or -1) != a["exe"]["size"]:
            raise ValueError("The release's checksums do not describe its installer.")
        part = os.path.join(d, a["exe"]["name"] + ".part")
        def prog(done, total):
            with _lock: STATE.update(bytes_done=done, bytes_total=total or a["exe"]["size"])
            return not _job["cancel"]
        with open(part, "wb") as f: sha, size = _get(a["exe"]["url"], 60, out=f, progress=prog)
        with _lock: STATE.update(phase="checking")
        if sha != want or size != a["exe"]["size"]:
            os.remove(part); raise ValueError("The downloaded installer does not match the release's checksum. Nothing was installed.")
        names = EXPECTED_PUBLISHERS or ([x.strip() for x in str(info.get("publisher") or "").split(";") if x.strip()] if info.get("signed") else [])
        if (EXPECTED_PUBLISHERS or info.get("signed")) and not _signed_by(part, names):
            os.remove(part); raise ValueError("The downloaded installer is not signed by Classic Cart Studio's publisher. Nothing was installed.")
        final = os.path.join(d, a["exe"]["name"]); os.replace(part, final)
        with _lock: _job.update(path=final, sha256=sha, size=size); STATE.update(phase="ready")
    except InterruptedError:
        with _lock: STATE.update(phase="canceled", error="")
    except Exception as e:
        with _lock: STATE.update(phase="error", error=str(e) if isinstance(e, ValueError) else "The update could not be downloaded. Check the internet connection and try again.")


def _sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""): h.update(b)
    return h.hexdigest()


# the names CCS Console Workbench's windows and console tasks hold while they run (installed file, installed window, console lock): Setup
# refuses to update while one exists, so Studio does not close itself for an update that would only be refused
WORKBENCH_MUTEXES = (r"Local\CCS.ConsoleWorkbench.InstalledFile", r"Local\CCS.ConsoleWorkbench.Installed", r"Local\CCS.ConsoleOperation")


def workbench_running():
    """True when an installed CCS Console Workbench window or a console task holds one of WORKBENCH_MUTEXES (False when it cannot tell)"""
    if os.name != "nt": return False
    try:
        import ctypes
        k = ctypes.WinDLL("kernel32", use_last_error=True)
        k.OpenMutexW.restype = ctypes.c_void_p; k.OpenMutexW.argtypes = (ctypes.c_uint32, ctypes.c_int, ctypes.c_wchar_p)
        for n in WORKBENCH_MUTEXES:
            h = k.OpenMutexW(0x00100000, 0, n)   # SYNCHRONIZE
            if h: k.CloseHandle(ctypes.c_void_p(h)); return True
            if ctypes.get_last_error() == 5: return True   # ERROR_ACCESS_DENIED: it exists
    except Exception:
        return False
    return False


def install():
    if workbench_running():
        return {"ok": False, "error": "CCS Console Workbench is open. Close it, then choose Update Now again. Nothing was installed."}
    with _lock:
        if STATE["phase"] != "ready" or not _job["path"]: return {"ok": False, "error": "The update is not ready yet."}
        p, want, size, terms = _job["path"], _job["sha256"], _job["size"], STATE["terms_changed"]
    try:
        if os.path.getsize(p) != size or _sha(p) != want: return {"ok": False, "error": "The downloaded installer changed since it was checked. Nothing was installed."}
        args = [p, "/CCSUPDATE=1", "/NORESTART"] + ([] if terms else ["/SILENT", "/SUPPRESSMSGBOXES", "/CLOSEAPPLICATIONS"])
        flags = 0x00000008 | 0x00000200   # DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP: it keeps running when this app closes
        try: subprocess.Popen(args, creationflags=flags | 0x01000000, close_fds=True)   # CREATE_BREAKAWAY_FROM_JOB where allowed
        except OSError: subprocess.Popen(args, creationflags=flags, close_fds=True)
    except Exception as e:
        return {"ok": False, "error": f"The installer could not be started ({e}). Nothing was installed."}
    with _lock: STATE.update(phase="installing")
    return {"ok": True, "quiet": not terms}


def handle(h, path, body, data_path, version):
    body = body if isinstance(body, dict) else {}
    if path == "/api/update/settings":
        auto = body.get("auto") if isinstance(body.get("auto"), bool) else None
        try: return h._json(200, {"ok": True, "auto": _settings(data_path, auto)["auto"]})
        except Exception as e: return h._json(500, {"ok": False, "error": f"The setting could not be saved ({e})."})
    if path == "/api/update/check":
        force = body.get("force") is True
        if not force and not _settings(data_path)["auto"]: return h._json(200, {"ok": True, "available": False, "current": version, "off": True})
        return h._json(200, check(version, force))
    if path == "/api/update/download":
        with _lock:
            rel = _plan["release"]
            if not rel or body.get("version") != rel["version"]: return h._json(409, {"ok": False, "error": "Check for updates again first."})
            if STATE["phase"] in ("downloading", "checking", "installing"): return h._json(200, {"ok": True, "busy": True})
            _job.update(cancel=False, path="", sha256="", size=0)
            STATE.update(phase="downloading", version=rel["version"], bytes_done=0, bytes_total=rel["assets"]["exe"]["size"], error="", terms_changed=rel["terms_changed"])
        threading.Thread(target=_download, args=(data_path,), daemon=True).start()
        return h._json(200, {"ok": True})
    if path == "/api/update/status":
        with _lock: return h._json(200, {"ok": True, **STATE})
    if path == "/api/update/cancel":
        with _lock: _job["cancel"] = True
        return h._json(200, {"ok": True})
    if path == "/api/update/install": return h._json(200, install())
    return h._json(404, {"ok": False, "error": "unknown endpoint"})
