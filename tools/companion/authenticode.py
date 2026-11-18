"""Windows' own view of Authenticode signatures, for the release build (tools/publish/signing.py) and the in-app update (app_update.py).

One implementation for both: Get-AuthenticodeSignature in Windows PowerShell 5.1, started by its full path in the Windows system folder
with only its own module folder, importing its own Microsoft.PowerShell.Security by path, so the caller's shell never decides which
PowerShell or which module answers. (A PowerShell 7 parent puts its own module folders first in PSModulePath and every child inherits
it; Windows PowerShell 5.1 then loads PowerShell 7's Microsoft.PowerShell.Security, which fails: Codex v1p review F2.) The file paths go
to the child as data (JSON in an environment variable), never as script text.

A file passes only with Windows' Valid status (the whole file covered, a chain to a trusted root), an embedded Authenticode signature,
a timestamp, and a signer whose certificate CN or O is on the expected list; never a certificate thumbprint (Artifact Signing
certificates last three days). Anything that cannot be checked is an error, never a pass.
Part of Classic Cart Studio: GNU GPL v3 or later."""
from __future__ import annotations
import json, os, re, subprocess

CREATE_NO_WINDOW = 0x08000000
SCRIPT = ("$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[Text.Encoding]::UTF8; "
          "Import-Module -Name (Join-Path $PSHOME 'Modules\\Microsoft.PowerShell.Security\\Microsoft.PowerShell.Security.psd1'); "
          "$r=@(); foreach($p in @($env:CCS_SIGN_CHECK | ConvertFrom-Json)){ $s=Microsoft.PowerShell.Security\\Get-AuthenticodeSignature -LiteralPath $p; "
          "$r+=[ordered]@{path=[string]$p; status=[string]$s.Status; type=[string]$s.SignatureType; message=[string]$s.StatusMessage; "
          "subject=$(if($s.SignerCertificate){$s.SignerCertificate.Subject}else{''}); "
          "timestamped=[bool]$s.TimeStamperCertificate; not_after=$(if($s.SignerCertificate){$s.SignerCertificate.NotAfter.ToString('o')}else{''})} }; "
          "ConvertTo-Json -InputObject $r -Depth 3 -Compress")


def _system_dir() -> str:
    import ctypes
    buf = ctypes.create_unicode_buffer(32768)
    if not ctypes.windll.kernel32.GetSystemDirectoryW(buf, len(buf)): raise OSError("the Windows system folder is not known")
    return buf.value


def windows_powershell() -> tuple[str, dict]:
    """the exact program and environment every check runs with: Windows PowerShell 5.1 from the system folder, its module folder only"""
    home = os.path.join(_system_dir(), "WindowsPowerShell", "v1.0")
    env = {k: v for k, v in os.environ.items() if k.upper() not in ("PSMODULEPATH", "CCS_SIGN_CHECK")}
    env["PSModulePath"] = os.path.join(home, "Modules")
    return os.path.join(home, "powershell.exe"), env


def inspect(paths: list[str], timeout: float = 120) -> list[dict]:
    """one record per path: status, type, message, subject, timestamped, not_after. Raises when Windows could not be asked."""
    if os.name != "nt": raise OSError("Authenticode signatures are checked on Windows only")
    exe, env = windows_powershell()
    env["CCS_SIGN_CHECK"] = json.dumps([os.path.abspath(p) for p in paths])
    r = subprocess.run([exe, "-NoProfile", "-NonInteractive", "-Command", SCRIPT], capture_output=True, env=env, timeout=timeout,
                       creationflags=CREATE_NO_WINDOW)
    out, err = r.stdout.decode("utf-8", "replace"), r.stderr.decode("utf-8", "replace")
    if r.returncode != 0: raise RuntimeError(f"Get-AuthenticodeSignature failed: {err.strip()[:300]}")
    recs = json.loads(out)
    if not isinstance(recs, list) or len(recs) != len(paths): raise RuntimeError("Get-AuthenticodeSignature did not answer for every file")
    return recs


def subject_names(subject: str) -> set[str]:
    """CN and O of a certificate subject such as 'CN=Rance9 LLC, O=Rance9 LLC, L=..., S=..., C=US' (quoted values allowed)"""
    out = set()
    for m in re.finditer(r'(?:^|,\s*)(CN|O)=("(?:[^"]|"")*"|[^,]*)', subject or ""):
        v = m.group(2).strip()
        if v.startswith('"') and v.endswith('"'): v = v[1:-1].replace('""', '"')
        out.add(v.strip())
    return out


def problems(rec: dict, names: list[str]) -> list[str]:
    """why a record does not pass for the expected names (empty: it passes)"""
    why = []
    if not names: why.append("no expected publisher")
    if rec.get("status") != "Valid": why.append(f"status {rec.get('status')} ({str(rec.get('message') or '')[:120]})")
    if rec.get("type") != "Authenticode": why.append(f"signature type {rec.get('type') or 'none'} (an embedded Authenticode signature is required)")
    if not (subject_names(rec.get("subject") or "") & set(names)): why.append(f"publisher {sorted(subject_names(rec.get('subject') or '')) or 'none'} is not {names}")
    if not rec.get("timestamped"): why.append("no timestamp (the signature would stop validating when the short-lived certificate expires)")
    return why
