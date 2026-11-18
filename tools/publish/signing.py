r"""Authenticode signing and verification of Classic Cart Studio's own Windows files, for a release (v1.0 release path).

Configuration (environment; nothing secret: the signing service authenticates the build through the CI identity, never a stored key):
  CCS_SIGNING          "" (default): no signing. The build is honestly unsigned and says so (exit 3 from "sign").
                       "artifact-signing": Microsoft Artifact Signing through SignTool and its signing library (dlib).
  CCS_SIGNTOOL         signtool.exe (Windows SDK build tools, x64)
  CCS_SIGN_DLIB        Azure.CodeSigning.Dlib.dll (x64) of the Artifact Signing client
  CCS_SIGN_METADATA    metadata.json: {"Endpoint": ..., "CodeSigningAccountName": ..., "CertificateProfileName": ...}
  CCS_SIGN_PUBLISHERS  the expected publisher names, separated by ";" (the certificate subject's CN or O, e.g. the validated company
                       name). Verification accepts nothing else. Required whenever CCS_SIGNING is set.
  CCS_REQUIRE_SIGNED   "1": an unsigned build is an error (an intended signed release never falls back to unsigned)

Only Classic Cart Studio's own files are signed (the caller names them: the Windows host, the frozen backend's launcher, the native
preview core and game player, the console worker and its library, the installer). Third-party files keep their own signatures or none;
nothing someone else made is signed as Classic Cart Studio, and the pinned Hakchi2 CE package is never touched.

  python signing.py status                              mode and expected publishers (JSON)
  python signing.py sign FILE...                        sign and timestamp, then verify; exit 3 when no signing is configured
  python signing.py verify FILE... [--publisher NAME]   Valid Authenticode signature, a publisher on the list, an RFC 3161 timestamp
  python signing.py iscc-args                           the ISCC arguments that make Inno Setup sign the uninstaller and setup itself
                                                        (empty when unsigned)
Verification checks the signer's name and a valid chain with a timestamp, never a certificate thumbprint: Artifact Signing certificates
last three days and are renewed daily, and their issuing intermediate can change. It is tools/companion/authenticode.py, the same code the
app's updater uses: Windows PowerShell 5.1 by its full path with only its own modules, whatever shell started the build.
Part of Classic Cart Studio: GNU GPL v3 or later."""
from __future__ import annotations
import importlib.util, json, os, subprocess, sys

def _load_authenticode():
    p = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "companion", "authenticode.py")   # by its path: no search path change
    spec = importlib.util.spec_from_file_location("authenticode", p); m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
    return m
A = _load_authenticode()

TIMESTAMP_URL = "http://timestamp.acs.microsoft.com"
NOT_CONFIGURED = 3

def mode() -> str: return os.environ.get("CCS_SIGNING", "").strip().lower()
def publishers(extra=()) -> list[str]:
    return [p for p in [x.strip() for x in os.environ.get("CCS_SIGN_PUBLISHERS", "").split(";")] + list(extra) if p]

def _need(name: str) -> str:
    v = os.environ.get(name, "").strip()
    if not v or not os.path.isfile(v): raise SystemExit(f"signing: {name} must name an existing file (got {v!r})")
    return v

_subject_names = A.subject_names

def inspect(paths: list[str]) -> list[dict]:
    """Windows' own view of each file's Authenticode signature (authenticode.inspect); any failure to ask stops the build"""
    try: return A.inspect(paths)
    except Exception as e: raise SystemExit(f"signing: {e}")

def verify(paths: list[str], names: list[str]) -> list[dict]:
    """Every file: a Valid Authenticode signature (whole file covered, chain to a trusted root), signed by one of the expected names,
    with a timestamp. Returns the records; raises SystemExit listing every problem."""
    if not names: raise SystemExit("signing: no expected publisher (CCS_SIGN_PUBLISHERS or --publisher)")
    recs, bad = inspect(paths), []
    for rec in recs:
        why = A.problems(rec, names)
        rec["ok"] = not why; rec["problems"] = why
        if why: bad.append(f"{os.path.basename(rec['path'])}: " + "; ".join(why))
    if bad: raise SystemExit("signing: verification failed: " + " | ".join(bad))
    return recs

def sign(paths: list[str]) -> list[dict]:
    m = mode()
    if not m:
        if os.environ.get("CCS_REQUIRE_SIGNED") == "1": raise SystemExit("signing: this build must be signed, but CCS_SIGNING is not set")
        print(f"UNSIGNED (no signing configured): {len(paths)} file(s) left as built"); sys.exit(NOT_CONFIGURED)
    if m != "artifact-signing": raise SystemExit(f"signing: unknown CCS_SIGNING {m!r}")
    names = publishers()
    if not names: raise SystemExit("signing: CCS_SIGN_PUBLISHERS must name the expected publisher before anything is signed")
    for p in paths:
        if not os.path.isfile(p): raise SystemExit(f"signing: {p} does not exist")
    cmd = [_need("CCS_SIGNTOOL"), "sign", "/v", "/fd", "SHA256", "/tr", TIMESTAMP_URL, "/td", "SHA256",
           "/dlib", _need("CCS_SIGN_DLIB"), "/dmdf", _need("CCS_SIGN_METADATA")] + paths
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode != 0: raise SystemExit(f"signing: signtool failed ({r.returncode}): {(r.stdout + r.stderr).strip()[-600:]}")
    return verify(paths, names)

def iscc_args() -> list[str]:
    """/DCCS_SIGN and the SignTool definition Inno Setup uses for the uninstaller, its temporary copies and the setup itself"""
    if not mode(): return []
    if not publishers(): raise SystemExit("signing: CCS_SIGN_PUBLISHERS must be set")
    st, dl, md = _need("CCS_SIGNTOOL"), _need("CCS_SIGN_DLIB"), _need("CCS_SIGN_METADATA")
    return ["/DCCS_SIGN", f"/Sccs=$q{st}$q sign /v /fd SHA256 /tr {TIMESTAMP_URL} /td SHA256 /dlib $q{dl}$q /dmdf $q{md}$q $f"]

def main(argv: list[str]) -> int:
    if not argv: print(__doc__); return 2
    cmd, rest = argv[0], argv[1:]
    extra = [rest[i + 1] for i, a in enumerate(rest) if a == "--publisher" and i + 1 < len(rest)]
    files = [a for i, a in enumerate(rest) if a != "--publisher" and (i == 0 or rest[i - 1] != "--publisher")]
    if cmd == "status": print(json.dumps({"mode": mode() or "unsigned", "publishers": publishers(), "require_signed": os.environ.get("CCS_REQUIRE_SIGNED") == "1"})); return 0
    if cmd == "sign": print(json.dumps(sign(files), indent=1)); return 0
    if cmd == "verify": print(json.dumps(verify(files, publishers(extra)), indent=1)); return 0
    if cmd == "iscc-args": print(json.dumps(iscc_args())); return 0
    print(__doc__); return 2

if __name__ == "__main__": sys.exit(main(sys.argv[1:]))
