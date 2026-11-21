"""The website's release streams (release-streams.json): the published releases of Classic Cart Studio and of CCS Console Workbench, by
product and channel, for the update checks of both programs.

Two products share this repository's GitHub releases: Classic Cart Studio (tags v<version>, installer ClassicCartStudio-Setup-<version>.exe)
and CCS Console Workbench (tags workbench-v<version>, program CCS-Console-Workbench-<version>.exe). Each product has two channels,
production and preview, so there are four streams: app/production, app/preview, workbench/production, workbench/preview. A program offers
only a newer release of its own product AND its own channel; nothing here crosses products or channels, and nothing uses GitHub's
repository-wide "latest" release or a release date to decide what is newest.

Which releases are listed (build(): pure, offline; the GitHub reading is separate, in release_notes.py):
- published releases only (drafts never), whose tag names one product with a plain version (N.N.N) and whose main file is that product's,
  of the same version; a release that also carries the other product's main file is refused;
- never a tag in EXCLUDED (a published release kept available but never offered by an update check; none at present);
- the channel is the release's own metadata: build-info.json "channel" (the app) or build-record.json "channel" (the Workbench), with its
  "product" and "version" when given matching the release, and agreeing with GitHub's prerelease mark (preview = prerelease). A release
  whose metadata names no channel (every release up to 0.11.3) is preview when GitHub marks it a prerelease, production otherwise: 0.11.0
  to 0.11.3 of both products are previews, v0.10.1 is the app's newest production release;
- each stream is ordered newest first by numeric version (0.11.10 is newer than 0.11.9).
App entries carry their Studio bundle when the build information names one (bundle.workbench version, channel and files, and the engine
source): an app release is the one installable unit for Studio users; a Workbench entry is never a Studio update.

When the releases cannot be read completely, the document says "complete": false and lists nothing: a program then says "Could not check",
never "Up to date".
"""
from __future__ import annotations
import re

SCHEMA = "ccs-release-streams/1"
REPO = "Rance9/Classic-Cart-Studio"
DOWNLOADS = "https://github.com/" + REPO + "/releases/download/"
DOWNLOAD_PAGE = "https://classiccartstudio.com/app/"
CHANNELS = ("production", "preview")
VER = re.compile(r"(\d{1,4})\.(\d{1,4})\.(\d{1,4})")
PRODUCTS = {
    "app": {"tag": re.compile(r"^v(\d{1,4}\.\d{1,4}\.\d{1,4})$"), "main": re.compile(r"^ClassicCartStudio-Setup-(\d{1,4}\.\d{1,4}\.\d{1,4})\.exe$", re.I),
            "meta": "build-info.json"},
    "workbench": {"tag": re.compile(r"^workbench-v(\d{1,4}\.\d{1,4}\.\d{1,4})$"), "main": re.compile(r"^CCS-Console-Workbench-(\d{1,4}\.\d{1,4}\.\d{1,4})\.exe$", re.I),
                  "meta": "build-record.json"},
}
# published releases that are kept available but never offered by an update check (their numbers may be larger than the current ones)
EXCLUDED = {}


def version(s) -> tuple | None:
    m = VER.fullmatch(str(s or "").strip())
    return tuple(int(x) for x in m.groups()) if m else None


def product_of(r: dict):
    """(product, version string) for a release whose tag names one product, else (None, None)"""
    tag = r.get("tagName") or ""
    for p, d in PRODUCTS.items():
        m = d["tag"].match(tag)
        if m: return p, m.group(1)
    return None, None


def _scalars(d, keys=None, limit=16) -> dict:
    """a copy of d with only plain values (text, numbers, true/false), at most limit of them"""
    out = {}
    for k, v in (d or {}).items() if isinstance(d, dict) else []:
        if len(out) >= limit: break
        if keys is not None and k not in keys: continue
        if isinstance(k, str) and (isinstance(v, (str, int, float, bool)) or v is None): out[k] = v
    return out


def _bundle(meta: dict):
    """the Studio bundle an app release's build information names: ("none", None) when it names none (every release up to 0.11.3),
    ("ok", bundle) when it is well formed, ("bad", reason) otherwise"""
    b = meta.get("bundle") if isinstance(meta, dict) else None
    if b is None: return "none", None
    if not isinstance(b, dict): return "bad", "its bundle is not an object"
    wb = b.get("workbench")
    if not isinstance(wb, dict): return "bad", "its bundle names no Workbench"
    if version(wb.get("version")) is None or str(wb.get("version")).strip() != str(wb.get("version")): return "bad", "its bundled Workbench has no plain version"
    if wb.get("channel") not in CHANNELS: return "bad", "its bundled Workbench has no known channel"
    assets = wb.get("assets") if isinstance(wb.get("assets"), list) else []
    es = b.get("engine_source")
    return "ok", {"workbench": {"version": wb["version"], "channel": wb["channel"], "assets": [_scalars(a) for a in assets[:8] if isinstance(a, dict)]},
                  "engine_source": _scalars(es) if isinstance(es, dict) else (es if isinstance(es, str) else None)}


def _sums(text) -> dict:
    out = {}
    for l in str(text or "").splitlines():
        m = re.match(r"^([0-9a-fA-F]{64})\s+\*?(\S.*?)\s*$", l)
        if m: out[m.group(2)] = m.group(1).lower()
    return out


def classify(r: dict, meta_loader):
    """(entry fields, None) for an eligible release, or (None, reason) when it is not listed. meta_loader(release, file name) returns the
    parsed metadata file, None when the release has no such file, or raises when it has one that cannot be read."""
    if r.get("isDraft"): return None, "a draft"
    tag = r.get("tagName") or ""
    p, v = product_of(r)
    if p is None: return None, "its tag names no product with a plain version"
    if tag in EXCLUDED: return None, EXCLUDED[tag]
    assets = r.get("assets") or []
    main = next((a for a in assets if (m := PRODUCTS[p]["main"].match(a.get("name") or "")) and m.group(1) == v), None)
    if main is None: return None, "it has no " + ("installer" if p == "app" else "program") + " of its own version"
    if any(PRODUCTS[o]["main"].match(a.get("name") or "") for o in PRODUCTS if o != p for a in assets): return None, "it also carries the other product's file"
    has_meta = any((a.get("name") or "") == PRODUCTS[p]["meta"] for a in assets)
    meta = meta_loader(r, PRODUCTS[p]["meta"]) if has_meta else None   # raises when it exists but cannot be read
    pre = bool(r.get("isPrerelease"))
    if isinstance(meta, dict) and "channel" in meta:
        ch = meta.get("channel")
        if ch not in CHANNELS: return None, "its metadata names an unknown channel"
        if meta.get("product") not in (None, p): return None, "its metadata names another product"
        if meta.get("version") not in (None, v): return None, "its metadata names another version"
        if (ch == "preview") != pre: return None, "its metadata's channel disagrees with GitHub's prerelease mark"
        source = PRODUCTS[p]["meta"]
    elif has_meta and not isinstance(meta, dict): return None, "its metadata is not a JSON object"
    else:
        ch, source = ("preview" if pre else "production"), "github-prerelease"
    e = {"product": p, "channel": ch, "version": v, "tag": tag, "prerelease": pre, "published": r.get("publishedAt"), "release_url": r.get("url"),
         "channel_from": source, "signed": bool(meta.get("signed")) if isinstance(meta, dict) else False, "download_page": DOWNLOAD_PAGE,
         "asset": {"name": main["name"], "size": int(main.get("size") or 0), "url": main.get("url"), "sha256": None},
         "files": [{"name": a.get("name"), "size": int(a.get("size") or 0), "url": a.get("url")} for a in assets
                   if isinstance(a.get("name"), str) and str(a.get("url") or "").startswith(DOWNLOADS + tag + "/")]}
    if not str(main.get("url") or "").startswith(DOWNLOADS + tag + "/"): return None, "its file is not at this release's download address"
    if p == "app":
        k, b = _bundle(meta)
        if k == "bad": return None, b
        e["bundle"] = b
    return e, None


def build(releases: list, meta_loader, sums_loader=None, generated: str | None = None) -> dict:
    """the release-streams document from GitHub releases ([{tagName, isDraft, isPrerelease, publishedAt, url, assets:[{name, size, url}]}]).
    sums_loader(release) returns that release's SHA256SUMS.txt text (or None); it is asked only for the newest entry of each stream.
    A metadata file that exists but cannot be read makes the document incomplete (nothing listed)."""
    doc = {"schema": SCHEMA, "generated": generated, "repository": REPO, "download_page": DOWNLOAD_PAGE, "complete": True,
           "rules": "same product and same channel only; newest by numeric version; drafts and excluded tags are never listed",
           "excluded": [], "streams": []}
    streams = {(p, c): [] for p in PRODUCTS for c in CHANNELS}
    try:
        for r in releases or []:
            e, why = classify(r, meta_loader)
            if e is None:
                if r.get("tagName") and not r.get("isDraft"): doc["excluded"].append({"tag": r.get("tagName"), "reason": why})
                continue
            streams[(e["product"], e["channel"])].append(e)
    except Exception as x:   # a release's metadata exists but could not be read: nothing is listed, so no program says "Up to date"
        return {"schema": SCHEMA, "generated": generated, "repository": REPO, "download_page": DOWNLOAD_PAGE, "complete": False,
                "note": f"release information could not be read completely ({type(x).__name__})", "excluded": [], "streams": []}
    for (p, c), lst in streams.items():
        lst.sort(key=lambda e: version(e["version"]), reverse=True)
        seen = set(); uniq = []
        for e in lst:
            if e["version"] in seen: continue
            seen.add(e["version"]); uniq.append(e)
        if uniq and sums_loader:
            head = uniq[0]
            try: sums = _sums(sums_loader(next(r for r in releases if r.get("tagName") == head["tag"])))
            except Exception: sums = {}
            head["asset"]["sha256"] = sums.get(head["asset"]["name"])
            for f in head["files"]: f["sha256"] = sums.get(f["name"])
        doc["streams"].append({"product": p, "channel": c, "releases": uniq})
    return doc


def stream(doc: dict, product: str, channel: str):
    """the releases of one stream, newest first ([] when it lists none); None when the document is incomplete or has no such stream"""
    if not isinstance(doc, dict) or doc.get("schema") != SCHEMA or doc.get("complete") is not True: return None
    for s in doc.get("streams") or []:
        if isinstance(s, dict) and s.get("product") == product and s.get("channel") == channel: return s.get("releases") or []
    return None


def legacy_json(doc: dict, product: str, channel: str, protocol: int, utility_prefix: str) -> dict:
    """/release.json or /workbench-release.json as earlier releases read them (the same keys, in the same order): the newest release of
    one product's stream; "version": null when there is none or the document is incomplete"""
    lst = stream(doc, product, channel)
    if not lst:
        return {"version": None, "product": product, "note": ("release lookup unavailable" if lst is None else "no " + ("companion" if product == "app" else "Workbench") + " release published yet")}
    e = lst[0]
    files = e.get("files") or []
    s = next((f for f in files if str(f.get("name") or "").upper().startswith("SHA256SUMS")), None)
    u = next((f for f in files if product == "app" and str(f.get("name") or "").startswith(utility_prefix) and str(f.get("name")).lower().endswith(".exe")), None)
    return {"product": product, "version": e["version"], "tag": e["tag"], "prerelease": bool(e.get("prerelease")), "published": e.get("published"), "url": e.get("release_url"),
            "asset": {"name": e["asset"]["name"], "size": e["asset"]["size"], "url": e["asset"]["url"]}, "sums_url": s["url"] if s else None, "sha256": e["asset"].get("sha256"),
            "utility": {"name": u["name"], "size": u["size"], "url": u["url"], "sha256": u.get("sha256")} if u else None, "protocol": protocol, "source": "github-release"}
