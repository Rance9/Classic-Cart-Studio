"""Deterministic synthetic fixtures (schema ccs-sim-fixture/1) for the Console Simulator. No ROM bytes, no firmware, no
private data: synthetic titles and identities only. Regenerate with `python make_fixtures.py [out_dir]`; the output is
byte-identical on every run (fixed seed, sorted keys).

  factory-only.json   30 factory entries, all visible (the console as shipped; titles are synthetic placeholders)
  mixed.json          a modest mixed collection: factory kept / hidden / alternate, added, personal; duplicate display
                      names, two versions of one game, missing files, unusual characters
  stress-5000.json    5,000 entries with large and missing artwork references
  malformed.json      records that must be rejected one by one (missing id, missing version, bad kind, duplicate id,
                      command smuggling, wrong types) around a few valid ones
  not-json.json       not JSON at all
  wrong-schema.json   valid JSON with another schema name
"""
from __future__ import annotations
import json, os, random, sys, hashlib

SCHEMA = "ccs-sim-fixture/1"

def entry(i: int, kind: str, title: str, version: str, **kw) -> dict:
    e = {"id": f"sim:{kind}:{i:05d}", "version_id": f"{hashlib.sha256(f'{i}:{version}'.encode()).hexdigest()[:16]}:{version}", "title": title, "kind": kind, "order": i}
    e.update(kw); return e

def factory_only() -> dict:
    return {"schema": SCHEMA, "name": "factory-only (synthetic)", "source": "synthetic", "note": "30 synthetic factory placeholders; not Nintendo titles",
            "entries": [entry(i, "factory-visible", f"Factory Title {i + 1:02d}", "factory", ready="n/a", art_ref=f"art/factory-{i + 1:02d}.png") for i in range(30)]}

def mixed() -> dict:
    es = []
    for i in range(30):
        kind = "factory-hidden" if i % 4 == 1 else "factory-visible"
        es.append(entry(i, kind, f"Factory Title {i + 1:02d}", "factory", ready="n/a", art_ref=f"art/factory-{i + 1:02d}.png"))
    es.append(entry(100, "alternate", "Factory Title 03", "rev-1", ready="ready", group="Favorites", art_ref="art/alt-03.png"))             # different version of a factory game (same display name)
    es.append(entry(101, "added", "Added Game A", "usa-rev0", ready="ready", group="Favorites", art_ref="art/added-a.png"))
    es.append(entry(102, "added", "Added Game A", "usa-rev1", ready="ready", group="Favorites", art_ref="art/added-a-rev1.png"))          # duplicate display name, different version
    es.append(entry(103, "added", "Café Déjà Vu — ∞ (日本語)", "jpn", ready="missing", art_ref=""))                                         # unusual characters, missing file
    es.append(entry(104, "personal", "My Homebrew", "sha:abc", ready="ready", group="Personal", art_ref="art/missing-on-purpose.png"))
    es.append(entry(105, "added", "the Lowercase Article", "usa", ready="unknown", art_ref="art/lower.png"))                               # sort key drops the article
    es.append(entry(106, "added", "Zebra Last", "usa", ready="ready", sort_key="zzz", art_ref="art/zebra.png"))
    return {"schema": SCHEMA, "name": "mixed (synthetic)", "source": "synthetic", "entries": es}

def stress(n: int = 5000) -> dict:
    rnd = random.Random(20261012); es = []
    for i in range(n):
        kind = rnd.choice(["added", "added", "added", "personal", "factory-visible"])
        art = "" if rnd.random() < 0.1 else (f"art/huge-{i}.png" if rnd.random() < 0.05 else f"art/{i}.png")
        es.append(entry(i, kind, f"Stress Title {rnd.randint(0, 99999):05d} {chr(65 + i % 26)}", f"v{rnd.randint(0, 9)}", ready=rnd.choice(["ready", "ready", "missing", "unknown"]), art_ref=art, group=f"Group {i % 37}"))
    return {"schema": SCHEMA, "name": f"stress-{n} (synthetic)", "source": "synthetic", "entries": es}

def malformed() -> dict:
    return {"schema": SCHEMA, "name": "malformed (synthetic)", "source": "synthetic", "entries": [
        entry(0, "added", "Valid One", "v1", ready="ready"),
        {"version_id": "v", "title": "No Id", "kind": "added"},
        {"id": "sim:bad:1", "title": "No Version", "kind": "added"},
        {"id": "sim:bad:2", "version_id": "v", "title": "Bad Kind", "kind": "carousel-tile"},
        {"id": "sim:added:00000", "version_id": "v2", "title": "Duplicate Of Valid One", "kind": "added"},
        {"id": "sim:bad:3", "version_id": "v", "title": "Smuggled Command", "kind": "added", "launch_command": "/bin/sh -c 'echo no'"},
        {"id": "sim:bad:4", "version_id": "v", "title": "Visible Not Bool", "kind": "added", "visible": "yes"},
        {"id": "sim:bad:5", "version_id": "v", "title": "Order Not Number", "kind": "added", "order": "first"},
        {"id": "sim:bad:6", "version_id": "v", "title": "Bad Ready", "kind": "added", "ready": "soon"},
        "not an object",
        entry(9, "personal", "Valid Two", "v9", ready="missing"),
    ]}

def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__)); os.makedirs(out, exist_ok=True)
    for name, doc in (("factory-only.json", factory_only()), ("mixed.json", mixed()), ("stress-5000.json", stress()), ("malformed.json", malformed()), ("wrong-schema.json", {"schema": "something-else/9", "entries": []})):
        with open(os.path.join(out, name), "w", encoding="utf-8", newline="\n") as f: json.dump(doc, f, ensure_ascii=False, indent=0, sort_keys=True); f.write("\n")
    with open(os.path.join(out, "not-json.json"), "w", encoding="utf-8", newline="\n") as f: f.write("{this is not json\n")
    print("fixtures written to", out)

if __name__ == "__main__": main()
