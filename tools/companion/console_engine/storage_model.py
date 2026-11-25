"""The console storage accounting: one calculation for the loadout meter (studio/storage-model.js computes the same), the installation
plan and install check (plan.py, ccs_install.py), CCS Nucleus's capacity tool (tools/console-nucleus/capacity.py) and the Workbench's
capacity check and Storage report (CcsBackup Engine/StorageSpec.cs). Bytes everywhere; readable units only for display.

The specification below is tools/platform/ccs-storage-spec.json byte for byte (test_storage_model.py checks it): the dated reference
readings of each console model, the policies (block size when none was read, room for saves, the safety margin) and the labeled estimates
used before a package is prepared. account() distinguishes the space available (a reading or the dated reference, with the reserved blocks
left out once), each new file's length and its whole blocks, files freed when a replacement is in place, room for saves, the safety margin,
and the peak while new and old copies are both present. An unknown size or an unknown allowance never counts as zero: the fit is then
unconfirmed. Part of Classic Cart Studio: GNU GPL v3 or later."""
from __future__ import annotations
import copy, hashlib, json, os

SPEC_TEXT = r'''{
  "schema": "ccs-storage-spec/1",
  "about": "The one storage accounting specification. tools/companion/console_engine/storage_model.py carries this file byte for byte, the C# engine embeds it, the website's storage-model.js is built from it, and all of them decide tools/platform/storage-vectors.json the same way. Every figure is bytes. A reference is a dated reading of one console, never a live or universal value; a policy is a chosen allowance; an estimate is worked out from measured packages and says what it is missing.",
  "references": [
    {
      "profile": "nes-classic-edition",
      "date": "2026-10-09",
      "what": "the data storage header (ext4 superblock) recorded in an original, unmodified North American NES Classic's storage capture",
      "total_bytes": 413138944,
      "free_bytes": 395460608,
      "reserved_bytes": 20656128,
      "block_bytes": 4096,
      "available_bytes": 374804480
    },
    {
      "profile": "snes-classic-edition",
      "date": "2026-11-21",
      "what": "the data storage header (ext4 superblock) of a North American Super NES Classic's data storage, copied read-only and not mounted",
      "total_bytes": 314572800,
      "free_bytes": 297934848,
      "reserved_bytes": 15728640,
      "allocated_bytes": 16637952,
      "block_bytes": 4096,
      "available_bytes": 282206208
    }
  ],
  "policies": {
    "default_block_bytes": 4096,
    "saves_allowance_bytes": {
      "nes-classic-edition": 10485760,
      "snes-classic-edition": 10485760
    },
    "saves_allowance_unknown": "No room for this console's saves and suspend points has been established, so whether new files fit cannot be confirmed.",
    "margin_fixed_bytes": 8388608,
    "margin_fraction_ppm": 50000,
    "margin_what": "a safety margin for folders, extent records and the file system's own use: a fixed amount plus a share of the new files' allocated size",
    "earlier_home_versions_default": 1,
    "earlier_home_versions_max": 9,
    "earlier_home_versions_what": "earlier CCS HOME versions kept on the console when CCS HOME is updated, so the console can go back to one; each is removed only after the new version is in use and read back"
  },
  "estimates": {
    "home": {
      "fixed_logical_bytes": 17522892,
      "per_game_logical_bytes": 31392,
      "fixed_allocated_bytes": 17984420,
      "per_game_allocated_bytes": 38063,
      "music_logical_bytes": 11907116,
      "music_allocated_bytes": 11911168,
      "music_held_logical_bytes": 993068,
      "music_held_allocated_bytes": 995328,
      "music_held_player_extra_logical_bytes": 42048,
      "music_held_player_extra_allocated_bytes": 40960,
      "music_held_notices_extra_logical_bytes": 6907,
      "music_held_notices_extra_allocated_bytes": 8192,
      "music_what": "the song CCS HOME plays (included unless Include HOME music is left out; the figures above count it): the 48,000 Hz stereo PCM file this version writes. The held MP3 of the same song is measured apart (its file, and what the game player built with it and its notices add); it is not used until it is approved, and nothing counts it",
      "what": "CCS HOME's package when it has not been prepared on this computer yet: worked out from measured HOME packages of 30, 100 and 300 games, in the encodings this version writes, and never lower than any of them. This collection's own covers, texts and fonts are not measured until its HOME package is prepared."
    },
    "mod_envelope": {
      "logical_bytes": 6014306,
      "allocated_bytes": 6082560,
      "files": 29,
      "what": "the game player, its fonts, the startup pictures, settings and license notices that come with CCS HOME, from a measured console package"
    },
    "notices": {
      "logical_bytes": 187131,
      "allocated_bytes": 212992,
      "files": 13,
      "what": "the license notices that come with the game files when CCS HOME is not installed, from a measured console package"
    },
    "native_entry": {
      "desktop_bytes": 592,
      "cover_bytes": 58292,
      "cover_small_bytes": 2851,
      "what": "a game's Original Menu entry (its menu record, cover and small cover) before it is prepared: the highest per-game average of measured entries for 30, 100 and 300 catalog games. A game file stored again in the form Hakchi needs is counted when the installation plan is prepared"
    },
    "native_entry_held": {
      "desktop_bytes": 592,
      "cover_bytes": 19037,
      "cover_small_bytes": 2163,
      "what": "the same entry with the smaller Original Menu covers (an 8-bit palette, as Hakchi2 CE makes them by default), measured the same way: built and tested in this version but not used until they are compared on the console's own menu and approved; no capacity check, meter or report counts them"
    },
    "hakchi_base": {
      "logical_bytes": 26214400,
      "allocated_bytes": 26214400,
      "what": "an allowance for the Hakchi base on a console that does not have it yet; its installed size is not measured"
    }
  }
}
'''
SPEC_SCHEMA = "ccs-storage-spec/1"; ACCOUNT_SCHEMA = "ccs-storage-account/1"
SPEC = json.loads(SPEC_TEXT)
if SPEC.get("schema") != SPEC_SCHEMA: raise RuntimeError("the storage specification is not " + SPEC_SCHEMA)


def spec_sha256() -> str: return hashlib.sha256(SPEC_TEXT.encode("utf-8")).hexdigest()


def reference(profile: str, spec: dict = SPEC) -> dict | None:
    """the dated reference reading for a console model (a copy), or None when there is none"""
    r = next((x for x in spec["references"] if x["profile"] == profile), None)
    return copy.deepcopy(r) if r else None


def rounded(n, block: int) -> int:
    """a file's data blocks: its length rounded up to whole blocks (an empty file takes none)"""
    return 0 if n <= 0 else -(-n // block) * block


def _int(x) -> bool: return isinstance(x, int) and not isinstance(x, bool)


def account(inp: dict, spec: dict = SPEC) -> dict:
    """One storage account. inp:
      profile     the console model ("nes-classic-edition", "snes-classic-edition")
      storage     None (use the model's dated reference) or a reading: {"source": "measured"|"simulated", "available_bytes"} for a figure
                  that already leaves the reserved blocks out (df's Available), or {"free_bytes", "reserved_bytes"} from the file system's
                  own header (the reserve is subtracted here, once); "block_bytes" when the block size was read
      items       what is written, in order: {"category", "label", "files": [{"bytes": int|None, "key": str|None}], "estimated": bool,
                  "estimate": {"logical_bytes", "allocated_bytes"}, "replaces": [bytes of files it replaces], "what": basis}
                  a file whose key was counted already is not counted again (one copy shared by several collections)
      removed_after  [{"label", "allocated_bytes": int|None, "key": str|None}]: copies or files removed after every new file is in
                  place (they lower the final figure, never the peak); a removal whose key was counted already is not counted again
    Unknown sizes stay unknown: the fit is then unconfirmed and no write plan may rely on it."""
    pol = spec["policies"]; prof = inp.get("profile"); ref = next((x for x in spec["references"] if x["profile"] == prof), None)
    st = inp.get("storage"); unknown = []
    if st is None:
        if ref: available, asrc = ref["available_bytes"], "reference"
        else: available, asrc = None, "unknown"; unknown.append("the space available on this console")
    else:
        asrc = st.get("source") if st.get("source") in ("measured", "simulated") else "measured"
        if _int(st.get("available_bytes")): available = st["available_bytes"]
        elif _int(st.get("free_bytes")) and _int(st.get("reserved_bytes")): available = max(0, st["free_bytes"] - st["reserved_bytes"])
        else: available = None; unknown.append("the space available on this console")
    if st is not None and _int(st.get("block_bytes")) and st["block_bytes"] > 0: block, bsrc = st["block_bytes"], "measured"
    elif ref: block, bsrc = ref["block_bytes"], "reference"
    else: block, bsrc = pol["default_block_bytes"], "default"
    seen = set(); rows = []; steps = []
    for it in inp.get("items") or []:
        kind = "estimate" if it.get("estimated") or it.get("estimate") is not None else "exact"
        lg = al = 0
        est = it.get("estimate")
        if est is not None:
            if _int(est.get("logical_bytes")) and _int(est.get("allocated_bytes")): lg += est["logical_bytes"]; al += est["allocated_bytes"]
            else: kind = "unknown"
        for f in it.get("files") or []:
            k = f.get("key")
            if k is not None:
                if k in seen: continue
                seen.add(k)
            b = f.get("bytes")
            if not _int(b) or b < 0: kind = "unknown"; continue
            lg += b; al += rounded(b, block)
        fr = sum(rounded(b, block) for b in it.get("replaces") or [] if _int(b))
        if kind == "unknown": unknown.append(it.get("label") or it.get("category") or "a file")
        rows.append({"category": it.get("category"), "label": it.get("label"), "kind": kind, "logical_bytes": lg, "allocated_bytes": al, "freed_bytes": fr, "what": it.get("what")})
        steps.append((al, fr))
    run = peak = 0
    for a, fr in steps: peak = max(peak, run + a); run += a - fr
    removed = 0; gone = set()
    for r in inp.get("removed_after") or []:
        k = r.get("key")
        if k is not None:
            if k in gone: continue
            gone.add(k)
        if _int(r.get("allocated_bytes")): removed += r["allocated_bytes"]
        else: unknown.append(r.get("label") or "a copy to remove")
    run -= removed
    logical = sum(r["logical_bytes"] for r in rows); allocated = sum(r["allocated_bytes"] for r in rows)
    nothing = allocated == 0 and not any(r["kind"] == "unknown" for r in rows)
    saves = pol["saves_allowance_bytes"].get(prof) if prof in pol["saves_allowance_bytes"] else None
    if saves is None and not nothing: unknown.append("room for saves and suspend points")
    margin = 0 if nothing else pol["margin_fixed_bytes"] + allocated * pol["margin_fraction_ppm"] // 1000000
    extra = 0 if nothing else (saves or 0) + margin
    steady = run + extra; peak_total = max(peak, run) + extra
    if available is None: status = "unconfirmed"
    elif steady > available: status = "over"
    elif peak_total > available: status = "over-at-peak"
    elif unknown: status = "unconfirmed"
    elif available - peak_total < margin: status = "near"
    else: status = "fits"
    return {"schema": ACCOUNT_SCHEMA, "profile": prof, "reference": {"date": ref["date"], "what": ref["what"]} if ref and asrc == "reference" else None,
            "available_bytes": available, "available_source": asrc, "block_bytes": block, "block_source": bsrc, "rows": rows,
            "logical_bytes": logical, "allocated_bytes": allocated, "freed_bytes": sum(r["freed_bytes"] for r in rows), "removed_bytes": removed,
            "saves_allowance_bytes": 0 if nothing else saves, "margin_bytes": margin, "steady_bytes": steady, "peak_bytes": peak_total,
            "remaining_bytes": None if available is None else available - peak_total, "unknown": unknown, "status": status,
            "write_ok": status in ("fits", "near")}


def home_estimate(games: int, spec: dict = SPEC, music: bool = True) -> dict:
    """CCS HOME before it is prepared; music False: Include HOME music left out (the song's own bytes not counted)"""
    h = spec["estimates"]["home"]; n = max(0, int(games)); lm, am = (0, 0) if music else (h["music_logical_bytes"], h["music_allocated_bytes"])
    return {"logical_bytes": h["fixed_logical_bytes"] + h["per_game_logical_bytes"] * n - lm, "allocated_bytes": h["fixed_allocated_bytes"] + h["per_game_allocated_bytes"] * n - am}


NATIVE_COVER_ESTIMATES = {"standard": "native_entry", "palette": "native_entry_held"}   # hakchi_publish.NATIVE_COVER_VARIANTS


def native_entry_files(spec: dict = SPEC, variant: str = "standard") -> list:
    """an Original Menu entry's files before they are prepared; variant "palette" is the held smaller covers (only a test or a comparison
    asks for it: what this version writes is "standard")"""
    e = spec["estimates"][NATIVE_COVER_ESTIMATES[variant]]; return [e["desktop_bytes"], e["cover_bytes"], e["cover_small_bytes"]]


def _est(e: dict) -> dict: return {"logical_bytes": e["logical_bytes"], "allocated_bytes": e["allocated_bytes"]}


def collection_items(inp: dict, spec: dict = SPEC) -> list:
    """what a collection puts on a console, before it is prepared there: the loadout meter's and the installation plan's items.
    inp: games [{bytes: int|None (the game file with its 16-byte header), install: bool (False: a built-in game kept as it is)}],
    home {measured: int|None, allocated: int|None (the prepared package's files rounded to blocks), games: int (the games shown), music: bool
    (Include HOME music; default True; a prepared package's measurement already says what it holds)},
    mod (CCS Mod selected: CCS HOME and its player go to the console; default True), native_entries (the games become Original Menu
    entries; default: when CCS Mod is not selected), hakchi_present"""
    mod = inp.get("mod", True); native = inp.get("native_entries", not mod); es = spec["estimates"]
    added = [g for g in inp.get("games") or [] if g.get("install")]
    items = [{"category": "game-files", "label": "Game files", "files": [{"bytes": g.get("bytes") if _int(g.get("bytes")) and g["bytes"] > 0 else None} for g in added]}]
    if native and added:   # native_covers: "standard" (what this version writes) unless a test or a comparison asks for the held "palette" covers
        nv = inp.get("native_covers") or "standard"
        items.append({"category": "native-entries", "label": "Original Menu entries", "estimated": True, "what": es[NATIVE_COVER_ESTIMATES[nv]]["what"],
                      "files": [{"bytes": b} for _ in added for b in native_entry_files(spec, nv)]})
    if mod:
        h = inp.get("home") or {}
        if _int(h.get("measured")) and h["measured"] > 0:
            al = h["allocated"] if _int(h.get("allocated")) and h["allocated"] >= h["measured"] else None
            items.append({"category": "home", "label": "CCS HOME", "estimate": {"logical_bytes": h["measured"], "allocated_bytes": al if al is not None else h["measured"]},
                          "what": "measured: the package prepared on this computer" + ("" if al is not None else " (its files' rounding to blocks is not known)")})
        else:
            items.append({"category": "home", "label": "CCS HOME", "estimate": home_estimate(h.get("games") or 0, spec, h.get("music") is not False),
                          "what": es["home"]["what"] + ("" if h.get("music") is not False else " Include HOME music is left out: the song is not counted.")})
        items.append({"category": "player", "label": "Game player, startup pictures and notices", "estimate": _est(es["mod_envelope"]), "what": es["mod_envelope"]["what"]})
    else:
        items.append({"category": "notices", "label": "License notices", "estimate": _est(es["notices"]), "what": es["notices"]["what"]})
    if not inp.get("hakchi_present"):
        items.append({"category": "hakchi", "label": "Hakchi (needed once per console)", "estimate": _est(es["hakchi_base"]), "what": es["hakchi_base"]["what"]})
    return items


def estimate(inp: dict, spec: dict = SPEC, extra_items: list | None = None) -> dict:
    """the loadout meter's and the installation plan's estimate (storage-model.js computes the same): collection_items() through
    account(), with the figures the meter shows. inp: as collection_items(), plus profile (default the NES Classic) and storage (a
    reading, see account(); None: the dated reference); free: a measured available figure (the same as storage {"available_bytes"});
    earlier: how many earlier CCS HOME versions the console keeps (default: the specification's). Counted: what the collection writes now,
    the earlier versions kept, and room to send its next update beside the copy in use (the peak; the oldest copy goes afterwards).
    extra_items: more items a caller writes besides the collection's (the simulated plan's own records)."""
    storage = inp.get("storage")
    if storage is None and _int(inp.get("free")): storage = {"source": "measured", "available_bytes": inp["free"]}
    items = collection_items(inp, spec) + list(extra_items or [])
    acc = {"profile": inp.get("profile") or "nes-classic-edition", "storage": storage, "items": items}
    # updating it later: the console copy (with CCS Mod: CCS HOME, its player and the game files; without: the games copy) is sent beside the
    # one in use, and with CCS Mod the earlier versions the setting keeps stay too; after the switch the oldest one goes
    first = account(acc, spec); by = {r["category"]: r for r in first["rows"]}; mod = inp.get("mod", True)
    part = ("home", "player", "game-files") if mod else ("game-files", "notices")
    copy = {"logical_bytes": sum((by.get(k) or {}).get("logical_bytes", 0) for k in part), "allocated_bytes": sum((by.get(k) or {}).get("allocated_bytes", 0) for k in part)}
    keep = earlier_keep(inp.get("earlier"), spec) if mod else 0
    if keep:
        items.append({"category": "earlier-home", "label": f"Earlier CCS HOME versions kept ({keep})", "estimate": {k: v * keep for k, v in copy.items()},
                      "what": spec["policies"]["earlier_home_versions_what"]})
    items.append({"category": "next-update", "label": "Room to send the next update beside the one in use", "estimate": dict(copy),
                  "what": "the new copy is sent and checked before the switch; the oldest copy goes only afterwards"})
    acc["removed_after"] = [{"label": "the oldest copy, removed after the switch", "allocated_bytes": copy["allocated_bytes"]}]
    a = account(acc, spec)
    by = {r["category"]: r for r in a["rows"]}
    added = [g for g in inp.get("games") or [] if g.get("install")]
    a.update({"rom": by["game-files"]["logical_bytes"], "added": len(added), "unknown_games": sum(1 for g in added if not (_int(g.get("bytes")) and g["bytes"] > 0)),
              "files": (by.get("native-entries") or {}).get("logical_bytes", 0), "home": (by.get("home") or {}).get("logical_bytes", 0),
              "home_measured": (by.get("home") or {}).get("what", "").startswith("measured"), "player": (by.get("player") or {}).get("logical_bytes", 0),
              "hakchi": (by.get("hakchi") or {}).get("logical_bytes", 0), "earlier_versions": keep, "copy_bytes": copy["logical_bytes"],
              "used": a["peak_bytes"], "available": a["available_bytes"], "remaining": a["remaining_bytes"],
              "source": "reference" if a["available_source"] == "reference" else "device"})
    return a


def capacity(profile: str = "nes-classic-edition", spec: dict = SPEC) -> int | None:
    """the dated reference's space outside the reserve (None: no reference for this model)"""
    r = next((x for x in spec["references"] if x["profile"] == profile), None); return r["available_bytes"] if r else None


def earlier_keep(value=None, spec: dict = SPEC) -> int:
    """how many earlier CCS HOME versions to keep: a whole number from 0 to the specification's maximum; anything else is the default"""
    pol = spec["policies"]
    return value if _int(value) and 0 <= value <= pol["earlier_home_versions_max"] else pol["earlier_home_versions_default"]


def earlier_setting() -> int | None:
    """the number of earlier CCS HOME versions to keep, as CCS Console Workbench's settings hold it (Settings > CCS HOME > Earlier HOME
    versions: the Workbench Classic Cart Studio starts saves its settings in this file); None when it is not set there or cannot be read"""
    p = os.environ.get("CCS_WORKBENCH_SETTINGS") or os.path.join(os.environ.get("LOCALAPPDATA") or "", "CCS Console Backup", "settings.json")
    try:
        with open(p, "rb") as f: v = json.loads(f.read(1_000_000)).get("earlier_home_versions")
    except (OSError, ValueError, AttributeError): return None
    return v if isinstance(v, int) and not isinstance(v, bool) else None


def retention(inp: dict, spec: dict = SPEC) -> dict:
    """Which earlier CCS HOME copies stay and which go once a new copy is in use and read back. inp: keep (earlier versions to keep),
    current (the copy in use after the switch), prior (the copy in use before it, or None), copies [{"copy", "owned" (this program's by
    its record), "unchanged" (every recorded file as recorded), "referenced" (something still names it), "previous" (the copy that was in
    use before it, from its record, or None)}], unfinished (an installation that did not finish). Only an owned, unchanged, unreferenced
    earlier copy can go; the most recent earlier versions stay: the prior, then the one before it by the records, then the rest by name.
    With an unfinished installation nothing goes."""
    keep = earlier_keep(inp.get("keep"), spec); cur = inp.get("current"); prior = inp.get("prior")
    copies = {c["copy"]: c for c in inp.get("copies") or [] if isinstance(c, dict) and isinstance(c.get("copy"), str)}
    why = {}; order = []
    if cur in copies: why[cur] = "in use"
    if inp.get("unfinished"):
        for k in copies:
            why.setdefault(k, "an installation did not finish")
        return {"keep": keep, "kept": sorted(copies), "remove": [], "why": dict(sorted(why.items()))}
    seen = set(); c = prior
    while isinstance(c, str) and c in copies and c not in seen and c != cur:   # the chain of earlier versions, most recent first
        order.append(c); seen.add(c); c = copies[c].get("previous")
    order += sorted(k for k in copies if k not in seen and k != cur)
    eligible = []
    for k in order:
        x = copies[k]
        if not x.get("owned"): why[k] = "not recorded as this program's"
        elif not x.get("unchanged"): why[k] = "changed since it was installed"
        elif x.get("referenced"): why[k] = "something on the console still names it"
        else: eligible.append(k)
    kept_eligible = eligible[:keep]
    for k in kept_eligible: why[k] = "an earlier version kept"
    remove = eligible[keep:]
    return {"keep": keep, "kept": sorted(k for k in copies if k not in remove), "remove": remove, "why": dict(sorted(why.items()))}
