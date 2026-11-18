"""The Microsoft Edge WebView2 Runtime's offline installers that Classic Cart Studio's setup carries (D-144/D-146).

Setup checks the computer first (ClassicCartStudio.exe with its runtime-check option) and runs one of these, silently, only when no usable
Runtime is there: Microsoft's Evergreen Standalone Installer for the computer's architecture (x64, or ARM64 for Windows on Arm, where
CCS runs as an x64 app). This script gets them from Microsoft's official download links (https://go.microsoft.com/fwlink/?linkid=2124701
x64, 2099616 ARM64; https://developer.microsoft.com/microsoft-edge/webview2/), or takes copies already downloaded from there, and keeps a
file only when Windows reports its Authenticode signature Valid, embedded, timestamped and by Microsoft Corporation
(tools/companion/authenticode.py). It writes webview2-prereq.json beside them: each file's source, file version, size and SHA-256 (the
release information and the third-party notices name them). Nothing else is ever used as a source.
   python webview2_prereq.py <out_dir> [--from x64=PATH] [--from arm64=PATH]      (without --from for an architecture: downloaded)
Part of Classic Cart Studio: GNU GPL v3 or later."""
from __future__ import annotations
import argparse, ctypes, hashlib, importlib.util, json, os, shutil, sys, time, urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
_s = importlib.util.spec_from_file_location("ccs_authenticode", os.path.join(HERE, "..", "companion", "authenticode.py")); A = importlib.util.module_from_spec(_s); _s.loader.exec_module(A)
FILES = {"x64": ("MicrosoftEdgeWebView2RuntimeInstallerX64.exe", "https://go.microsoft.com/fwlink/?linkid=2124701"),
         "arm64": ("MicrosoftEdgeWebView2RuntimeInstallerARM64.exe", "https://go.microsoft.com/fwlink/?linkid=2099616")}
PUBLISHER = ["Microsoft Corporation"]
ALLOWED_HOSTS = ("go.microsoft.com", "msedge.sf.dl.delivery.mp.microsoft.com", "msedge.b.tlu.dl.delivery.mp.microsoft.com", "download.microsoft.com")


def file_version(path: str) -> str:
    """the file's own version resource (FileVersion), read by Windows (version.dll)"""
    v = ctypes.windll.version; n = v.GetFileVersionInfoSizeW(path, None)
    if not n: return ""
    buf = ctypes.create_string_buffer(n)
    if not v.GetFileVersionInfoW(path, 0, n, buf): return ""
    p = ctypes.c_void_p(); ln = ctypes.c_uint()
    if not v.VerQueryValueW(buf, "\\", ctypes.byref(p), ctypes.byref(ln)): return ""
    ms, ls = ctypes.cast(p.value + 8, ctypes.POINTER(ctypes.c_uint32))[0], ctypes.cast(p.value + 12, ctypes.POINTER(ctypes.c_uint32))[0]
    return f"{ms >> 16}.{ms & 0xFFFF}.{ls >> 16}.{ls & 0xFFFF}"


def download(url: str, dest: str) -> str:
    """Microsoft's link, followed only to Microsoft's own download hosts; returns the final URL"""
    class NoForeign(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, req, fp, code, msg, hdrs, newurl):
            host = urllib.request.urlparse(newurl).hostname or ""
            if not newurl.startswith("https://") or host not in ALLOWED_HOSTS: raise SystemExit(f"refusing a redirect away from Microsoft: {newurl}")
            return super().redirect_request(req, fp, code, msg, hdrs, newurl)
    op = urllib.request.build_opener(NoForeign); final = url
    with op.open(urllib.request.Request(url, headers={"User-Agent": "ClassicCartStudio-build"}), timeout=120) as r, open(dest, "wb") as f:
        final = r.geturl(); shutil.copyfileobj(r, f, 1 << 20)
    return final


def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("out"); ap.add_argument("--from", dest="src", action="append", default=[]); a = ap.parse_args()
    given = dict(x.split("=", 1) for x in a.src); os.makedirs(a.out, exist_ok=True); rec = {"schema": "ccs-webview2-prereq/1", "files": {}}
    for arch, (name, url) in FILES.items():
        dest = os.path.join(a.out, name); part = dest + ".part"
        # the installed record names the source without a Microsoft download link: a cautious reading of the WebView2 Runtime license's
        # 2(c)(iii) (download sites or shortcuts), while Microsoft's distribution guide describes such links; open for the owner's legal
        # review, and not by itself the whole of the license's distribution terms. The link stays in this tool
        if arch in given: shutil.copyfile(given[arch], part); src = {"source": "acquired directly from Microsoft (a copy of its official download)", "from": os.path.basename(given[arch])}
        else: download(url, part); src = {"source": "acquired directly from Microsoft (its official download)", "at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}
        sig = A.inspect([part])[0]; bad = A.problems(sig, PUBLISHER)
        if bad: os.remove(part); raise SystemExit(f"{name}: not accepted: {'; '.join(bad)}")
        os.replace(part, dest); b = open(dest, "rb").read()
        rec["files"][arch] = dict(src, file=name, size=len(b), sha256=hashlib.sha256(b).hexdigest(), file_version=file_version(dest),
                                  signature={"status": sig.get("status"), "subject": sig.get("subject"), "timestamped": sig.get("timestamped"), "not_after": sig.get("not_after")})
        print(f"{arch}: {name} {len(b):,} B sha256 {rec['files'][arch]['sha256']} version {rec['files'][arch]['file_version']} signature {sig.get('status')} ({sig.get('subject')})", flush=True)
    json.dump(rec, open(os.path.join(a.out, "webview2-prereq.json"), "w", encoding="utf-8"), indent=1)
    return 0


if __name__ == "__main__": sys.exit(main())
