"""CCS Mod and CCS Nucleus record formats on the PC side: the sealed records the console scripts read (console/lib.sh), the boot
registry (ccs-boot-registry/1) from its JSON form, and the ownership journal (ccs-ownership/1). The console checks every record again
itself; this module makes and reads exactly the same bytes. Copyright (C) 2026 Rance9, GPL-3.0-or-later.

Sealed record: lines "key=value" (LF), the first "schema=<name>", the last "end=<SHA-256 of every byte before that line>"."""
from __future__ import annotations
import hashlib, re

ID_RE = re.compile(r"^[a-z0-9][a-z0-9-]{0,31}$")
PATH_RE = re.compile(r"^/[A-Za-z0-9._/-]{1,199}$")
LABEL_RE = re.compile(r"^[A-Za-z0-9 .,'!&()+:-]{1,60}$")
HEX64 = re.compile(r"^[0-9a-f]{64}$")
KINDS = ("native", "ccs-home", "owner-root")
IMAGE_PLACES = ("/var/lib/ccs/nucleus/roots/", "/var/lib/hakchi/firmware/", "/media/hakchi/firmware/")
OPTION_FIELDS = ("kind", "label", "image", "sha256", "bytes", "package", "kernel", "drivers", "requires", "overlay", "saves", "compat",
                 "owner", "fallback", "auto")
KERNEL = "3.4.113.29-madmonkey"   # hakchi 3.9.3's kernel (hakchi.hmod var/version), the only one CCS HOME's player is built for

class FormatError(ValueError): pass

def path_ok(p: str) -> bool:
    return bool(PATH_RE.match(p or "")) and "//" not in p and "/../" not in p + "/" and not p.endswith("/")

def seal(body: str) -> bytes:
    if body and not body.endswith("\n"): body += "\n"
    b = body.encode("ascii")
    return b + b"end=" + hashlib.sha256(b).hexdigest().encode() + b"\n"

def unseal(data: bytes, schema: str) -> list[tuple[str, str]]:
    """the key/value lines of a sealed record of this schema (FormatError when it is not one)"""
    text = data.decode("ascii")
    lines = text.split("\n")
    if lines and lines[-1] == "": lines.pop()
    if not lines or lines[0] != f"schema={schema}": raise FormatError(f"not a {schema} record")
    last = lines[-1]
    if not last.startswith("end=") or not HEX64.match(last[4:]): raise FormatError("no seal")
    body = text[: text.rindex("\nend=") + 1].encode("ascii")
    if hashlib.sha256(body).hexdigest() != last[4:]: raise FormatError("the seal does not match (incomplete or changed)")
    out = []
    for l in lines[:-1]:
        if "=" not in l: raise FormatError(f"not a key=value line: {l!r}")
        k, v = l.split("=", 1); out.append((k, v))
    return out

def first(kv: list[tuple[str, str]], key: str, default: str = "") -> str:
    return next((v for k, v in kv if k == key), default)

# ---------------------------------------------------------------- the boot registry (ccs-boot-registry/1)
def registry_text(reg: dict) -> bytes:
    """the console's form of a registry given as JSON:
    {"schema": "ccs-boot-registry/1", "generation": 1, "default": "native", "selection": "default"|"last", "chooser": "always"|"never",
     "timeout": 0..99, "options": [{"id": ..., "kind": "native"|"ccs-home"|"owner-root", "label": ..., <OPTION_FIELDS>}, ...]}
    (lists: "drivers" and "requires" as JSON lists, written space-separated)"""
    validate(reg)
    L = ["schema=ccs-boot-registry/1", f"generation={int(reg['generation'])}", f"default={reg['default']}", f"selection={reg['selection']}",
         f"chooser={reg['chooser']}", f"timeout={int(reg['timeout'])}", "options=" + " ".join(o["id"] for o in reg["options"])]
    for o in reg["options"]:
        for f in OPTION_FIELDS:
            if f not in o: continue
            v = o[f]
            if isinstance(v, list): v = " ".join(v)
            L.append(f"opt.{o['id']}.{f}={v}")
    return seal("\n".join(L) + "\n")

def registry_read(data: bytes) -> dict:
    kv = unseal(data, "ccs-boot-registry/1")
    reg = {"schema": "ccs-boot-registry/1", "generation": int(first(kv, "generation", "x")) if first(kv, "generation").isdigit() else -1,
           "default": first(kv, "default"), "selection": first(kv, "selection"), "chooser": first(kv, "chooser"),
           "timeout": int(first(kv, "timeout")) if first(kv, "timeout").isdigit() else -1, "options": []}
    for i in first(kv, "options").split():
        o = {"id": i}
        for f in OPTION_FIELDS:
            v = first(kv, f"opt.{i}.{f}", None)
            if v is None: continue
            o[f] = v.split() if f in ("drivers", "requires") else v
        reg["options"].append(o)
    validate(reg)
    return reg

def validate(reg: dict):
    """the same rules as the console's reg_ok (console/nucleus/bin/ccs-nucleus)"""
    if reg.get("schema") != "ccs-boot-registry/1": raise FormatError("schema")
    if not isinstance(reg.get("generation"), int) or reg["generation"] < 0: raise FormatError("generation")
    if reg.get("selection") not in ("default", "last"): raise FormatError("selection is default or last")
    if reg.get("chooser") not in ("always", "never"): raise FormatError("chooser is always or never")
    if not isinstance(reg.get("timeout"), int) or not 0 <= reg["timeout"] <= 99: raise FormatError("timeout is 0 to 99 seconds")
    ids = [o.get("id") for o in reg.get("options") or []]
    if len(set(ids)) != len(ids): raise FormatError("an option id is listed twice")
    if "native" not in ids: raise FormatError("the console's own system (option native) is always a choice")
    by = {}
    for o in reg["options"]:
        i, k = o.get("id"), o.get("kind")
        if not ID_RE.match(i or ""): raise FormatError(f"option id {i!r}")
        if k not in KINDS: raise FormatError(f"{i}: kind")
        if (k == "native") != (i == "native"): raise FormatError("exactly one native option, with id native")
        if not LABEL_RE.match(o.get("label") or ""): raise FormatError(f"{i}: label (plain words, 1 to 60 characters)")
        for f, v in o.items():
            if f != "id" and f not in OPTION_FIELDS: raise FormatError(f"{i}: unknown field {f}")
            for x in (v if isinstance(v, list) else [v]):
                if not isinstance(x, str) or any(c in x for c in "\n\r\t\x00") or (isinstance(v, list) and " " in x): raise FormatError(f"{i}.{f}: value")
        if k == "owner-root":
            img = o.get("image") or ""
            if not (path_ok(img) and img.endswith(".hsqs") and img.startswith(IMAGE_PLACES)): raise FormatError(f"{i}: image place")
            if not HEX64.match(o.get("sha256") or ""): raise FormatError(f"{i}: sha256")
            if not str(o.get("bytes") or "").isdigit(): raise FormatError(f"{i}: bytes")
            if o.get("fallback") != "native": raise FormatError(f"{i}: fallback is native")
        for p in (o.get("requires") or []):
            if not path_ok(p): raise FormatError(f"{i}: requires {p!r}")
        by[i] = o
    if reg.get("default") not in by: raise FormatError("the default is not listed")
    if reg["chooser"] == "never" and by[reg["default"]]["kind"] == "owner-root":
        raise FormatError("without the chooser the console can only start in its own system (an owner's image starts only when chosen)")

# ---------------------------------------------------------------- the ownership journal (ccs-ownership/1)
def journal_read(data: bytes) -> dict:
    kv = unseal(data, "ccs-ownership/1")
    j = {"method": first(kv, "method"), "package": first(kv, "package"), "files": {}, "data": {}, "dirs": []}
    for k, v in kv:
        if k == "file": sha, mode, p = v.split(" "); j["files"][p] = {"sha256": sha, "mode": mode}
        elif k == "data": sha, p = v.split(" "); j["data"][p] = {"sha256": sha}
        elif k == "dir": j["dirs"].append(v)
    return j
