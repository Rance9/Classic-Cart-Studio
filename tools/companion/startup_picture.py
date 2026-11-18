"""A collection's own console startup picture (D-174), checked where it leaves the app: the console package
(native_home/console_package.py) and every Classic Cart Studio backup this app writes (ccs_package.py: a collection's .ccs and the Full
Studio Backup). A collection names its picture in home.startup_custom (id custom-<first 16 hex of its SHA-256>, sha256) and carries the
bytes beside it (data: base64), exactly as studio/startup-custom.js makes it: a still 1280x720 JPEG of at most 450 kB whose SHA-256 is the
one it is named by. A backup that names a picture without carrying it, or with anything else in its place, is refused before a file is
written: it could not bring the picture back, and the choice would quietly become Random somewhere else."""
from __future__ import annotations
import base64, hashlib, io, re

MAX = 450_000   # the saved picture's size limit (studio/startup-custom.js SU_LIM.out)
SHA_RE = re.compile(r"^[0-9a-f]{64}$")

def check(c) -> tuple[bytes | None, str | None]:
    """(the picture's bytes, None), or (None, why) in plain words"""
    if not isinstance(c, dict): return None, "the collection does not describe its startup picture"
    sha = c.get("sha256")
    if not isinstance(sha, str) or not SHA_RE.match(sha) or c.get("id") != "custom-" + sha[:16]: return None, "the collection does not describe its startup picture correctly"
    data = c.get("data")
    if not isinstance(data, str) or not data: return None, "the picture itself is not with the collection"
    if len(data) > MAX * 4 // 3 + 8: return None, "the picture is larger than a startup picture can be"
    try: raw = base64.b64decode(data, validate=True)
    except Exception: return None, "the picture's data is damaged"
    if hashlib.sha256(raw).hexdigest() != sha: return None, "the picture is not the one the collection names (its content differs)"
    try:
        from PIL import Image
        im = Image.open(io.BytesIO(raw))
        if im.format != "JPEG": return None, "the picture is not a JPEG"
        if getattr(im, "n_frames", 1) != 1: return None, "the picture is animated"
        if im.size != (1280, 720): return None, f"the picture is {im.size[0]}x{im.size[1]}, not 1280x720"
        im.load()
    except Exception as e: return None, f"the picture could not be read ({type(e).__name__})"
    return raw, None

def records(package: dict, studio_schema: str) -> list:
    """the collection records a package carries: a collection's .ccs (ccs_collection) or every collection of a Full Studio Backup"""
    if not isinstance(package, dict): return []
    if package.get("schema") == studio_schema:
        sd = package.get("studio_data"); cols = sd.get("collections") if isinstance(sd, dict) else None
        return [r for r in cols if isinstance(r, dict)] if isinstance(cols, list) else []
    cc = package.get("ccs_collection")
    return [cc] if isinstance(cc, dict) else []

def problems(package: dict, studio_schema: str) -> list[tuple[str, str]]:
    """[(collection name, why)] for every named picture that is not carried correctly"""
    out = []
    for r in records(package, studio_schema):
        home = r.get("home")
        c = home.get("startup_custom") if isinstance(home, dict) else None
        if c is None: continue
        _, why = check(c)
        if why: out.append((str(r.get("name") or "a collection")[:80], why))
    return out
