r"""The release workflow's signing tools (only when CCS_SIGNING is artifact-signing): SignTool from the Windows SDK build tools and Microsoft's
Artifact Signing client library (dlib), at pinned versions, each package checked against its recorded SHA-512 before anything is extracted,
then the signing metadata from repository variables. Writes CCS_SIGNTOOL, CCS_SIGN_DLIB and CCS_SIGN_METADATA to GITHUB_ENV for signing.py.
  python signing_setup.py <work folder>
The SHA-512 of each package is recorded at signing onboarding, after review (NuGet publishes it as packageHash in the package's catalog
entry). Until then this step stops: an intended signed release never continues with unverified tools.
Part of Classic Cart Studio: GNU GPL v3 or later."""
from __future__ import annotations
import base64, hashlib, json, os, sys, urllib.request, zipfile

PACKAGES = {   # name: (version, SHA-512 of the .nupkg as NuGet's packageHash (base64), the file inside it)
    "Microsoft.Windows.SDK.BuildTools": ("10.0.26100.4188", "", "bin/10.0.26100.0/x64/signtool.exe"),
    "Microsoft.ArtifactSigning.Client": ("1.0.128", "", "bin/x64/Azure.CodeSigning.Dlib.dll"),
}

def main(work: str) -> int:
    missing = [n for n, (_, h, _) in PACKAGES.items() if not h]
    if missing: raise SystemExit("signing setup: record the reviewed SHA-512 (NuGet packageHash) of " + ", ".join(missing) + " in tools/publish/signing_setup.py first")
    os.makedirs(work, exist_ok=True); out = {}
    for name, (ver, want, inner) in PACKAGES.items():
        url = f"https://api.nuget.org/v3-flatcontainer/{name.lower()}/{ver}/{name.lower()}.{ver}.nupkg"
        with urllib.request.urlopen(url, timeout=120) as r: data = r.read()
        got = base64.b64encode(hashlib.sha512(data).digest()).decode()
        if got != want: raise SystemExit(f"signing setup: {name} {ver} has SHA-512 {got}, not the recorded {want}")
        pkg = os.path.join(work, f"{name}.{ver}.nupkg"); open(pkg, "wb").write(data)
        with zipfile.ZipFile(pkg) as z:
            names = {n.replace("\\", "/"): n for n in z.namelist()}
            if inner not in names: raise SystemExit(f"signing setup: {name} {ver} has no {inner}")
            dest = os.path.join(work, name); z.extractall(dest)
        out[name] = os.path.join(dest, *inner.split("/"))
    meta = {"Endpoint": os.environ.get("CCS_SIGN_ENDPOINT", ""), "CodeSigningAccountName": os.environ.get("CCS_SIGN_ACCOUNT", ""), "CertificateProfileName": os.environ.get("CCS_SIGN_PROFILE", "")}
    if not all(meta.values()): raise SystemExit("signing setup: CCS_SIGN_ENDPOINT, CCS_SIGN_ACCOUNT and CCS_SIGN_PROFILE (repository variables) are required")
    mp = os.path.join(work, "metadata.json"); json.dump(meta, open(mp, "w", encoding="utf-8"))
    env = os.environ.get("GITHUB_ENV")
    lines = [f"CCS_SIGNTOOL={out['Microsoft.Windows.SDK.BuildTools']}", f"CCS_SIGN_DLIB={out['Microsoft.ArtifactSigning.Client']}", f"CCS_SIGN_METADATA={mp}"]
    if env: open(env, "a", encoding="utf-8").write("\n".join(lines) + "\n")
    print("\n".join(lines)); return 0

if __name__ == "__main__": sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else "signing-tools"))
