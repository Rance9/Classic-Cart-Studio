#!/usr/bin/env python3
"""Conformance checker for CCS Collection Interface v1 indexes. Part of the CCS Collection Interface kit.
Copyright (c) 2026 Rance9. MIT License (see LICENSE).

  python3 ccs_ci_check.py INDEX.json [...]       exit 0: every index conforms; 1: a problem (one line each); 2: not readable

Checks, in this order: the file is UTF-8 JSON with no repeated names (at most 16 MB); it matches the v1 JSON Schema
(schema/ccs-collection-interface-v1.schema.json, checked by the small validator below: Python's standard library only); and the rules
the schema cannot say: every reference names a listed base; every game a collection, folder or favorite names is listed; a folder's parent
comes before it; no command, PC path, embedded file or encoded payload anywhere in the document. Unknown fields are allowed (readers ignore
them), but not under names that carry commands or file contents."""
from __future__ import annotations
import json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
SCHEMA_PATH = os.path.join(HERE, "..", "schema", "ccs-collection-interface-v1.schema.json")
MAX_BYTES = 16 * 1000 * 1000; MAX_STRING = 4096
FORBIDDEN_NAMES = re.compile(r"^(exec|command|cmd|commands|script|shell|run|launch|args|argv|data|payload|rom|save|saves|key|keys|serial|hwid|password)$", re.I)
PC_PATH = re.compile(r"(^[A-Za-z]:[/\\])|(^\\\\)|(^|[\s\"'])/(Users|home|mnt/[a-z])/|%USERPROFILE%|%APPDATA%", re.I)
SHELL = re.compile(r"\$\(|`[^`]*`|;\s*(rm|sh|wget|curl|dd|mount)\b|\|\s*(sh|bash)\b|\brm\s+-[rf]")
DATA_URI = re.compile(r"^data:[a-z]+/[a-z0-9.+-]+;base64,", re.I)

class Unreadable(ValueError): pass

def load(path: str):
    """the document, or Unreadable: not UTF-8, not JSON, too large, or a name repeated in one object"""
    if os.path.getsize(path) > MAX_BYTES: raise Unreadable(f"larger than {MAX_BYTES} bytes")
    with open(path, "rb") as f: raw = f.read()
    try: text = raw.decode("utf-8")
    except UnicodeDecodeError: raise Unreadable("not UTF-8") from None
    def pairs(ps):
        d = {}
        for k, v in ps:
            if k in d: raise Unreadable(f"the name {k!r} appears twice in one object")
            d[k] = v
        return d
    try: return json.loads(text, object_pairs_hook=pairs)
    except json.JSONDecodeError as e: raise Unreadable(f"not JSON ({e.msg} at line {e.lineno})") from None

# ---------------------------------------------------------------- a small JSON Schema validator (the keywords the v1 schema uses)
def _type_ok(v, t: str) -> bool:
    return {"object": isinstance(v, dict), "array": isinstance(v, list), "string": isinstance(v, str), "null": v is None, "boolean": isinstance(v, bool),
            "integer": isinstance(v, int) and not isinstance(v, bool), "number": isinstance(v, (int, float)) and not isinstance(v, bool)}[t]

def validate(v, s: dict, root: dict, where: str = "$") -> list[str]:
    out: list[str] = []
    if "$ref" in s:
        ref = s["$ref"]
        if not ref.startswith("#/$defs/"): return [f"{where}: unsupported reference {ref}"]
        return validate(v, root["$defs"][ref[len("#/$defs/"):]], root, where)
    if "const" in s and not (v == s["const"] and type(v) is type(s["const"])): return [f"{where}: must be {json.dumps(s['const'])}"]
    if "enum" in s and v not in s["enum"]: return [f"{where}: must be one of {', '.join(json.dumps(x) for x in s['enum'])}"]
    if "type" in s:
        ts = s["type"] if isinstance(s["type"], list) else [s["type"]]
        if not any(_type_ok(v, t) for t in ts): return [f"{where}: must be {' or '.join(ts)}"]
    if isinstance(v, str):
        if "minLength" in s and len(v) < s["minLength"]: out.append(f"{where}: too short")
        if "maxLength" in s and len(v) > s["maxLength"]: out.append(f"{where}: too long")
        if "pattern" in s and not re.search(s["pattern"], v): out.append(f"{where}: does not match {s['pattern']}")
    if isinstance(v, (int, float)) and not isinstance(v, bool):
        if "minimum" in s and v < s["minimum"]: out.append(f"{where}: below {s['minimum']}")
        if "maximum" in s and v > s["maximum"]: out.append(f"{where}: above {s['maximum']}")
    if isinstance(v, list):
        if "maxItems" in s and len(v) > s["maxItems"]: out.append(f"{where}: more than {s['maxItems']} items")
        if s.get("uniqueItems") and len({json.dumps(x, sort_keys=True) for x in v}) != len(v): out.append(f"{where}: an item appears twice")
        if "items" in s:
            for i, x in enumerate(v): out += validate(x, s["items"], root, f"{where}[{i}]")
    if isinstance(v, dict):
        for k in s.get("required", []):
            if k not in v: out.append(f"{where}: {k!r} is missing")
        props = s.get("properties", {})
        for k, x in v.items():
            if "propertyNames" in s: out += [f"{where}: the name {k!r}: " + e.split(": ", 1)[-1] for e in validate(k, s["propertyNames"], root, where)]
            if k in props: out += validate(x, props[k], root, f"{where}.{k}")
            elif isinstance(s.get("additionalProperties"), dict): out += validate(x, s["additionalProperties"], root, f"{where}.{k}")
            elif s.get("additionalProperties") is False: out.append(f"{where}: {k!r} is not allowed")
    return out

# ---------------------------------------------------------------- the rules beyond the schema
def rules(doc: dict) -> list[str]:
    out: list[str] = []
    bases = doc.get("bases") if isinstance(doc.get("bases"), dict) else {}
    games = doc.get("games") if isinstance(doc.get("games"), dict) else {}
    def ref(r, where):
        if isinstance(r, dict) and isinstance(r.get("base"), str) and r["base"] not in bases: out.append(f"{where}: the base {r['base']!r} is not listed in bases")
        if isinstance(r, dict) and isinstance(r.get("path"), str) and any(x in (".", "..") for x in r["path"].split("/")): out.append(f"{where}: a path may not name . or ..")
    for k, v in bases.items():
        if isinstance(v, str) and any(x in (".", "..") for x in v.split("/")[1:]): out.append(f"$.bases.{k}: a path may not name . or ..")
    for gid, g in games.items():
        if not isinstance(g, dict): continue
        if g.get("entry") is not None: ref(g["entry"], f"$.games.{gid}.entry")
        for k, r in (g.get("art") or {}).items() if isinstance(g.get("art"), dict) else []: ref(r, f"$.games.{gid}.art.{k}")
        if g.get("status") == "published" and not isinstance(g.get("entry"), dict): out.append(f"$.games.{gid}: a published game names its entry")
    seen_c = set()
    for i, c in enumerate(doc.get("collections") or []):
        if not isinstance(c, dict): continue
        w = f"$.collections[{i}]"
        if c.get("id") in seen_c: out.append(f"{w}: the id {c.get('id')!r} is used twice")
        seen_c.add(c.get("id"))
        for k in ("games", "favorites"):
            for x in c.get(k) or []:
                if isinstance(x, str) and x not in games: out.append(f"{w}.{k}: {x!r} is not in games")
        if c.get("entry") is not None: ref(c["entry"], f"{w}.entry")
        seen_f = set()
        for j, f in enumerate(c.get("folders") or []):
            if not isinstance(f, dict): continue
            fw = f"{w}.folders[{j}]"
            if f.get("id") in seen_f: out.append(f"{fw}: the id {f.get('id')!r} is used twice")
            if f.get("parent") is not None and f.get("parent") not in seen_f: out.append(f"{fw}: its parent {f.get('parent')!r} must be a folder listed before it")
            seen_f.add(f.get("id"))
            for x in f.get("games") or []:
                if isinstance(x, str) and x not in games: out.append(f"{fw}.games: {x!r} is not in games")
            if f.get("entry") is not None: ref(f["entry"], f"{fw}.entry")
    def walk(v, where):
        if isinstance(v, dict):
            for k, x in v.items():
                if FORBIDDEN_NAMES.match(k): out.append(f"{where}.{k}: commands, game files, saves, keys and console identifiers are not part of the interface")
                walk(x, f"{where}.{k}")
        elif isinstance(v, list):
            for i, x in enumerate(v): walk(x, f"{where}[{i}]")
        elif isinstance(v, str):
            if len(v) > MAX_STRING: out.append(f"{where}: a text longer than {MAX_STRING} characters (no embedded files)")
            if DATA_URI.match(v): out.append(f"{where}: an embedded file")
            if PC_PATH.search(v): out.append(f"{where}: a computer's own path")
            if SHELL.search(v): out.append(f"{where}: looks like a command")
    walk(doc, "$")
    return out

def check(path: str, schema: dict | None = None) -> list[str]:
    """every problem of one index (empty: it conforms); Unreadable when it cannot be read"""
    if schema is None:
        with open(SCHEMA_PATH, encoding="utf-8") as f: schema = json.load(f)
    doc = load(path)
    if not isinstance(doc, dict): return ["$: the index is a JSON object"]
    out = validate(doc, schema, schema)
    return out + [p for p in rules(doc) if p not in out]

def main(argv: list[str]) -> int:
    if not argv: print(__doc__.strip().splitlines()[2]); return 2
    worst = 0
    with open(SCHEMA_PATH, encoding="utf-8") as f: schema = json.load(f)
    for p in argv:
        try: probs = check(p, schema)
        except (OSError, Unreadable) as e: print(f"{p}: cannot be read: {e}"); worst = 2; continue
        for x in probs: print(f"{p}: {x}")
        if probs: worst = max(worst, 1)
        else: print(f"{p}: conforms to CCS Collection Interface v1")
    return worst

if __name__ == "__main__": sys.exit(main(sys.argv[1:]))
