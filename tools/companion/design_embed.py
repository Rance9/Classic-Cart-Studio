r"""A HOME design carried by a collection (CCS-FORMAT.md "HOME design"; tools/console-backup/HOME-DESIGNER.md).

A collection's HOME settings may hold home.design: the Shared HOME Design file (.ccshome) that CCS Console Workbench's HOME Designer
wrote, byte for byte, as Base64 (data), with its reference: {"format": "ccs-home-design-embed/1", "sha256": the file's SHA-256, then
what the Workbench wrote about it (id, revision, name, theme, content_sha256, bytes) and anything a newer version adds}. Python, like the
Studio page, only checks and carries it: it never opens, edits, re-encodes or re-writes a design (HOME Designer is the one editor and its
compiler the one converter). Every member it does not know is kept exactly.
Copyright (C) 2026 Rance9, GPL-3.0-or-later."""
from __future__ import annotations
import base64, binascii, hashlib, re

FORMAT = "ccs-home-design-embed/1"; MAX_BYTES = 64_000_000
HEX64 = re.compile(r"^[0-9a-f]{64}$"); B64 = re.compile(r"^[A-Za-z0-9+/]*={0,2}$")

def embed(ccshome: bytes, **ref) -> dict:
    """the reference and the file's bytes, as a collection carries them (the Workbench's own description in ref)"""
    if not isinstance(ccshome, (bytes, bytearray)) or not 0 < len(ccshome) <= MAX_BYTES: raise ValueError("not a HOME design file")
    out = {"format": FORMAT, "sha256": hashlib.sha256(ccshome).hexdigest()}
    out.update({k: v for k, v in ref.items() if k not in ("format", "sha256", "data", "bytes")}); out["bytes"] = len(ccshome)
    out["data"] = base64.b64encode(bytes(ccshome)).decode("ascii"); return out

def reference(obj) -> dict | None:
    """the reference without the bytes (every other member kept as it is); None when obj is not a carried design"""
    if not isinstance(obj, dict) or obj.get("format") != FORMAT or not HEX64.match(str(obj.get("sha256") or "")): return None
    return {k: v for k, v in obj.items() if k != "data"}

def check(obj) -> tuple[bytes | None, str | None]:
    """(the .ccshome bytes, None) when the carried design is complete and its bytes are the ones its SHA-256 names; (None, reason) otherwise"""
    if reference(obj) is None: return None, "the HOME design is not described correctly"
    data = obj.get("data")
    if not isinstance(data, str): return None, "the HOME design's file is not with the collection"
    if len(data) > MAX_BYTES * 4 // 3 + 4 or not B64.match(data): return None, "the HOME design's file is damaged"
    try: b = base64.b64decode(data, validate=True)
    except (binascii.Error, ValueError): return None, "the HOME design's file is damaged"
    if hashlib.sha256(b).hexdigest() != obj["sha256"]: return None, "the HOME design's file is not the one its fingerprint names (changed or damaged)"
    if b[:4] != b"PK\x03\x04": return None, "the HOME design's file is not a .ccshome file"
    return b, None
