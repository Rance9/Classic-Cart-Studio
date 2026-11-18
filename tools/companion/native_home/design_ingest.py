r"""A compiled HOME design (ccs-home-design-compiled/1, or /2 when it moves the HOME's controls) in the Python HOME package builder.

There is ONE compiler: CcsBackup.Compose.DesignCompiler (C#), used by both Windows hosts. This module never makes or changes a compiled
design: it takes the compiler's folder exactly as written, checks it (design.json's schema, every picture listed with its size and
SHA-256, named by its fingerprint, nothing else in the folder), and gives the package builder the same bytes, so the package's design/
folder is byte for byte the compiler's output (cross-checked by the engine tests, group "designer").

  python -m native_home.design_ingest verify DIR      prints the design's id (SHA-256 of design.json) and every file's SHA-256
Copyright (C) 2026 Rance9, GPL-3.0-or-later."""
from __future__ import annotations
import hashlib, json, os, re, sys

SCHEMA = "ccs-home-design-compiled/1"; SCHEMA2 = "ccs-home-design-compiled/2"; SCHEMAS = (SCHEMA, SCHEMA2); PROFILE = "nes-classic-home-1280x720/1"; JSON_NAME = "design.json"
MAX_JSON = 2_000_000; MAX_FILE = 4_000_000; MAX_TOTAL = 64_000_000; MAX_FILES = 48
NAME_RE = re.compile(r"^img/([0-9a-f]{64})\.(png|jpg)$")

class DesignError(ValueError): pass

def _sha(b: bytes) -> str: return hashlib.sha256(b).hexdigest()

def read(folder: str) -> dict:
    """{"id", "theme", "name", "files": {name: bytes}, "manifest": [{"name", "sha256", "bytes"}]} of a compiled design folder; DesignError
    when it is not one exactly as the compiler wrote it"""
    jp = os.path.join(folder, JSON_NAME)
    if not os.path.isfile(jp) or os.path.getsize(jp) > MAX_JSON: raise DesignError("this is not a compiled HOME design (no design.json)")
    jb = open(jp, "rb").read()
    try: doc = json.loads(jb.decode("utf-8"))
    except (UnicodeDecodeError, ValueError) as e: raise DesignError(f"design.json is not valid JSON ({e})")
    if not isinstance(doc, dict) or doc.get("schema") not in SCHEMAS or doc.get("profile") != PROFILE: raise DesignError(f"design.json is not {SCHEMA} or {SCHEMA2} for {PROFILE}")
    files = {JSON_NAME: jb}; manifest = [{"name": JSON_NAME, "sha256": _sha(jb), "bytes": len(jb)}]; listed = doc.get("files")
    if not isinstance(listed, list) or len(listed) > MAX_FILES: raise DesignError("the picture list is missing or too long")
    for f in listed:
        name, sha, size = (f or {}).get("name"), (f or {}).get("sha256"), (f or {}).get("bytes")
        m = NAME_RE.match(name or "")
        if not m or m.group(1) != sha or not isinstance(size, int) or not 0 < size <= MAX_FILE: raise DesignError(f"a picture is listed wrongly ({name!r})")
        p = os.path.join(folder, "img", m.group(0)[4:])
        if not os.path.isfile(p): raise DesignError(f"the picture {name} is missing")
        b = open(p, "rb").read()
        if len(b) != size or _sha(b) != sha: raise DesignError(f"the picture {name} is not the one design.json names (changed or damaged)")
        files[name] = b; manifest.append({"name": name, "sha256": sha, "bytes": size})
    if sum(len(b) for k, b in files.items() if k != JSON_NAME) > MAX_TOTAL: raise DesignError("the pictures are larger than a design may be")
    for r, _, fs in os.walk(folder):
        for fn in fs:
            rel = os.path.relpath(os.path.join(r, fn), folder).replace(os.sep, "/")
            if rel not in files: raise DesignError(f"the folder holds a file the design does not list ({rel})")
    manifest[1:] = sorted(manifest[1:], key=lambda x: x["name"])
    src = doc.get("source") or {}
    return {"id": _sha(jb), "theme": doc.get("theme"), "name": src.get("name"), "source": src, "files": files, "manifest": manifest}

def place(builder, folder: str, prefix: str = "design") -> dict:
    """puts the compiled design into a native HOME package being built (package.Builder), unchanged; returns pack.json's "design" entry"""
    d = read(folder)
    for name in [m["name"] for m in d["manifest"]]: builder.put_bytes(f"{prefix}/{name}", d["files"][name], "home-design", "design-json" if name == JSON_NAME else "design-picture")
    return {"id": d["id"], "dir": prefix, "theme": d["theme"], "files": [f"{prefix}/{m['name']}" for m in d["manifest"]]}

def main():
    if len(sys.argv) != 3 or sys.argv[1] != "verify": sys.exit("usage: python -m native_home.design_ingest verify DIR")
    try: d = read(sys.argv[2])
    except DesignError as e: print(json.dumps({"ok": False, "error": str(e)})); sys.exit(1)
    print(json.dumps({"ok": True, "id": d["id"], "theme": d["theme"], "manifest": d["manifest"]}))

if __name__ == "__main__": main()
