r"""Full Studio Backup in Classic Cart Studio for Windows (a .ccs file of kind "studio", tools/companion/CCS-FORMAT.md): what the
Background Helper adds to the page's own Studio data, and how a Full Studio Backup is restored.

  app_parts(data_path, ids)  -> (studio fields, extra archive entries): the Gameplay Defaults and every game's Game Settings record, the Play
                                on PC Settings, controller setups (player-pads.json) and choices (player-assign.json), every game's saved
                                progress (cart.sav, slot1-4, last-used.json, the legacy continue.ccsstate, set-aside cartridge files),
                                including games in no collection ("orphaned"), the custom artwork and text (overrides), and the details
                                and artwork of every listed personal game from this computer's own games (personal.py)
  preview(data_path, path, info) -> what a restore would add, skip or find different, and which files it would not restore and why;
                                nothing is written
  restore(data_path, path, info, replace, controllers) -> one verified transaction (below)
  recover(data_path, txid)   -> finishes putting back a restore whose own rollback could not finish (its journal says what to do)

Saved progress is checked with the game player's own rules before anything is planned (tools/player/core/gp_save.c): a save state is
restored only when its header names this exact game, this core and core version and this computer's platform and its size and CRC-32
match; a damaged state, one of another game, or one made by another player version or platform is never written (it stays in the backup
file, and the preview and the result say so). A cartridge save is the game's own bytes (no header). A last-used note is restored only
when the slot it names will hold exactly the state it names. Game Settings are read from either origin (made in the Windows app:
app.game_settings; made on the website: web.game_settings) with the player's normalization (play.clean_record / clean_defaults).

The transaction: every planned file is decided against the current data (new: added; identical: skipped; different: kept unless replace),
written beside its target under a name of this restore's own identity and read back; a journal names every change; each current file
being replaced is first copied, write-once, into "replaced-<restore id>" beside it and read back; only then are the files moved into
place, each checked. A failure puts back everything this restore changed and checks each one; if putting back itself fails, the result
names every file still changed, where its original is kept, and the journal stays so that recover() (Try Again) can finish. Originals in
"replaced-..." folders are never deleted or overwritten. Saved progress is never written, by a restore or by its Try Again, unless the
game player says no game window is open (when that cannot be checked, it is not written), and the player's lock is held meanwhile so no
game window opens during it; a journal naming saved progress waits untouched until the window is closed. A restore does not start while
an earlier one is unfinished (Try Again first). Controller setups are device-specific and restored only when asked. Nothing is uploaded;
the page merges the collections itself.

The personal games (D-80): the ROM step only adds ROM files and new records (ccs_package.import_roms); the backup's details and artwork
for the personal games on this computer are part of this transaction: this computer's list of its own games (personal/index.json) is
one replacement and each artwork file an addition or replacement, kept first, verified, put back on failure and recovered by Try Again
like every other file. The merge policy of personal.py applies, and the person's own edits a record names (user.edited, a blank field a
deliberate clear) are restored as edits: taken where the value here is empty or automatic, this computer's own edit or real value kept
unless the person chooses Replace."""
from __future__ import annotations
import contextlib, hashlib, json, os, re, secrets, time, zipfile, zlib
import ccs_package as cp

PROG_FILE = re.compile(r"^(cart\.sav|slot[1-4]\.ccsstate|last-used\.json|continue\.ccsstate|game\.json|slot-names\.json|cart-set-aside-[0-9]{1,20}(-[0-9]{1,3})?\.sav)$")
SHA = re.compile(r"^[0-9a-f]{64}$")
# the game player's save rules (tools/player/core/gp.h, gp_save.c); t_restore_native checks these equal the player's source
STATE_MAGIC = b"CCSSTATE"; STATE_SCHEMA = "ccs-save-state/1"; CORE_ID = "fceumm"; CORE_VERSION = "ac0d00a7"; PLATFORM = "windows-x86_64"
PLATFORMS_OK = ("windows-x86_64", "web-wasm32")   # gp_platform_ok (gp_save.c): the pair whose states are proven byte-identical and cross-loadable (W1)
MAX_STATE = 8 << 20; MAX_SRAM = 1 << 20; MAX_NOTE = 65536
SLOT_NAMES = {f"slot{n}.ccsstate": f"Slot {n}" for n in range(1, 5)}

def _sha(b: bytes) -> str: return hashlib.sha256(b).hexdigest()
def _read_json(p: str):
    try: return json.load(open(p, encoding="utf-8"))
    except Exception: return None
def _in_collections(sha: str, ids: set) -> bool: return sha[:16] in ids or ("p_" + sha[:16]) in ids

# ---------------- saved progress: the player's own rules ----------------
def state_check(b: bytes, sha: str) -> tuple[str, str, dict]:
    """a .ccsstate as the player checks it before loading: ("ok", "", header) | ("invalid", why, header) | ("incompatible", why, header)"""
    if not isinstance(b, (bytes, bytearray)) or len(b) < 12 or bytes(b[:8]) != STATE_MAGIC: return "invalid", "it is not a Classic Cart Studio save", {}
    hl = int.from_bytes(b[8:12], "little")
    if hl == 0 or hl > 65536 or 12 + hl > len(b) or len(b) > MAX_STATE + 65536: return "invalid", "it is not a Classic Cart Studio save", {}
    try: h = json.loads(bytes(b[12:12 + hl]).decode("utf-8"))
    except (UnicodeDecodeError, ValueError): return "invalid", "the save's description is damaged", {}
    if not isinstance(h, dict) or h.get("schema") != STATE_SCHEMA: return "invalid", "the save's description is damaged", {}
    if h.get("rom_content_sha256") != sha: return "invalid", "it belongs to a different game file", h
    if h.get("core") != CORE_ID or h.get("core_version") != CORE_VERSION or h.get("platform") not in PLATFORMS_OK:
        return "incompatible", f"it was made by another game player version or platform ({str(h.get('platform') or 'unknown')[:40]}, {str(h.get('core_version') or 'unknown')[:20]})", h
    pay = bytes(b[12 + hl:]); size = h.get("size")
    if not isinstance(size, int) or isinstance(size, bool) or size != len(pay) or h.get("crc32") != f"{zlib.crc32(pay) & 0xFFFFFFFF:08x}":
        return "invalid", "the save is damaged (its check does not match)", h
    return "ok", "", h

def file_check(name: str, b: bytes, sha: str) -> tuple[str, str, dict]:
    """any saved-progress file of one game: the player's rules for states; a cartridge save is the game's own bytes (no header)"""
    if name.endswith(".ccsstate"): return state_check(b, sha)
    if name.endswith(".sav"):
        if not b: return "invalid", "the cartridge save is empty", {}
        if len(b) > MAX_SRAM: return "invalid", "the file is larger than any cartridge save", {}
        return "ok", "", {}
    if len(b) > MAX_NOTE: return "invalid", "the file is too large", {}
    try: j = json.loads(bytes(b).decode("utf-8"))
    except (UnicodeDecodeError, ValueError): return "invalid", "the file is damaged", {}
    if not isinstance(j, dict): return "invalid", "the file is damaged", {}
    if name == "game.json":
        if j.get("rom_content_sha256") not in (None, sha): return "invalid", "it belongs to a different game file", j
        return "ok", "", j
    if name == "slot-names.json":   # the person's names for the four slots (F4): labels, never part of a save
        nm = j.get("names")
        if j.get("schema") != "ccs-slot-names/1" or j.get("rom_content_sha256") != sha or not isinstance(nm, dict) \
           or any(k not in ("1", "2", "3", "4") or not isinstance(v, str) or len(v.encode("utf-8")) > 96 for k, v in nm.items()):
            return "invalid", "the slot names are damaged or belong to another game file", j
        return "ok", "", j
    sl = j.get("slot")   # last-used.json
    if j.get("schema") != "ccs-last-used/1" or j.get("rom_content_sha256") != sha or not isinstance(sl, int) or isinstance(sl, bool) or not 1 <= sl <= 4:
        return "invalid", "the note of the last used save is damaged or belongs to another game file", j
    return "ok", "", j

def progress_check(path: str, info: dict) -> list:
    """every saved-progress file of a staged backup with the player's verdict: [{sha, title, file, status, why}] (nothing is written)"""
    data = info.get("data") or {}; titles = _titles(data); out = []
    with zipfile.ZipFile(path) as z:
        for n in info.get("progress") or []:
            _, sha, f = n.split("/", 2); st, why, h = file_check(f, z.read(n), sha)
            out.append({"sha": sha, "title": titles.get(sha) or str(h.get("title") or "")[:120], "file": f, "status": st, "why": why})
    return out

def _titles(data: dict) -> dict:
    app = data.get("app") if isinstance(data.get("app"), dict) else {}
    return {str(x.get("sha")): str(x.get("title") or "")[:120] for x in (app.get("progress") or []) if isinstance(x, dict) and isinstance(x.get("sha"), str)}

# ---------------- making a backup ----------------
PERSONAL_FIELDS = ("title", "version_label", "content_sha256", "file_sha256", "content_size", "file_size", "format", "file_name", "note", "content_crc32")
def _portable_personal(e: dict) -> dict:
    """a personal game's record as a backup carries it: its identity, sizes, file name (never a folder), what the person wrote and which of
    those fields the person chose here (user.edited: a blank one is a deliberate clear, D-80), and what the app read from its header (the
    restore cleans and bounds every value again)"""
    from personal import EDITABLE
    out = {"id": e.get("id")}
    for k in PERSONAL_FIELDS:
        if e.get(k) is not None: out[k] = os.path.basename(str(e[k])) if k == "file_name" else e[k]
    u = e.get("user") if isinstance(e.get("user"), dict) else {}
    out["user"] = {k: str(u.get(k) or "") for k in ("title", "version_label", "description", "year", "publisher")}
    ed = u.get("edited") if isinstance(u.get("edited"), list) else []
    ed = sorted({x for x in ed if isinstance(x, str) and x in EDITABLE})
    if ed: out["user"]["edited"] = ed
    if isinstance(e.get("detected"), dict): out["detected"] = {k: v for k, v in e["detected"].items() if k != "path"}
    return out

def personal_parts(ids) -> tuple[dict, list]:
    """the listed personal games that are among this computer's own games: {id: record} and their artwork entries (art/<id>.jpg)"""
    recs, extra = {}, []
    want = [i for i in ids or [] if cp.PERSONAL_RE.match(i or "")]
    if not want: return recs, extra
    try:
        import personal as ps
        idx = ps.load_index().get("entries") or {}
    except Exception: return recs, extra
    for i in want:
        e = idx.get(i)
        if not isinstance(e, dict): continue
        recs[i] = _portable_personal(e)
        a = os.path.join(ps.ART_DIR, i + ".jpg")
        try:
            if os.path.isfile(a) and 0 < os.path.getsize(a) <= cp.MAX_ART: extra.append((f"art/{i}.jpg", "art", a))
        except OSError: pass
    return recs, extra

def app_parts(data_path, ids: list) -> tuple[dict, list]:
    """the Windows app's own part of a Full Studio Backup: fields for studio.json and the extra archive entries"""
    ids_l = list(ids or []); ids = set(ids_l); extra: list = []; fields: dict = {}
    gs = data_path("game-settings"); games = {}
    d = _read_json(os.path.join(gs, "defaults.json"))
    if os.path.isdir(gs):
        for f in sorted(os.listdir(gs)):
            if f.endswith(".json") and SHA.match(f[:-5]):
                r = _read_json(os.path.join(gs, f))
                if isinstance(r, dict) and r.get("rom_content_sha256") == f[:-5]: games[f[:-5]] = r
    fields["game_settings"] = {"defaults": d if isinstance(d, dict) else None, "games": games}
    fields["player_settings"] = _read_json(data_path("player-settings.json"))
    fields["controllers"] = {"pads": _read_json(data_path("player-pads.json")), "assign": _read_json(data_path("player-assign.json"))}
    progress = []; root = data_path("saves")
    if os.path.isdir(root):
        for sha in sorted(os.listdir(root)):
            d2 = os.path.join(root, sha)
            if not SHA.match(sha) or not os.path.isdir(d2): continue
            files = [f for f in sorted(os.listdir(d2)) if PROG_FILE.match(f) and os.path.isfile(os.path.join(d2, f))]
            if not files: continue
            g = _read_json(os.path.join(d2, "game.json")) or {}
            progress.append({"sha": sha, "title": str(g.get("last_title") or "")[:200], "files": files, "orphaned": not _in_collections(sha, ids)})
            for f in files: extra.append((f"progress/{sha}/{f}", "progress", os.path.join(d2, f)))
    fields["progress"] = progress
    try:
        import overrides as ov
        st = ov.load(); n = 0
        if st.get("games"):
            extra.append(("custom/overrides.json", "custom", json.dumps(st, ensure_ascii=False, indent=1).encode("utf-8")))
            for vid, g in st["games"].items():
                a = g.get("art") or {}; m = str(a.get("master") or "")
                if ov.MASTER_RE.match(m) and os.path.isfile(ov._abs(m)) and cp.CUSTOM_RE.match("custom/" + m): extra.append(("custom/" + m, "custom", ov._abs(m))); n += 1
        fields["custom"] = {"games": len(st.get("games") or {}), "images": n}
    except Exception:
        fields["custom"] = {"games": 0, "images": 0}
    recs, art = personal_parts(ids_l)
    fields["personal"] = recs; extra += art
    return fields, extra

def summary_of(fields: dict) -> dict:
    gsf = fields.get("game_settings") or {}; pr = fields.get("progress") or []
    return {"game_settings": len(gsf.get("games") or {}), "gameplay_defaults": bool(gsf.get("defaults")), "progress_games": len(pr),
            "orphaned_progress": sum(1 for x in pr if x.get("orphaned")), "legacy_saves": sum(1 for x in pr if "continue.ccsstate" in x.get("files", [])),
            "player_settings": bool(fields.get("player_settings")), "controller_setups": bool((fields.get("controllers") or {}).get("pads")),
            "custom_games": (fields.get("custom") or {}).get("games", 0), "personal_games": len(fields.get("personal") or {})}

# ---------------- restore: the plan ----------------
PERSONAL_LABEL = "personal games"
def _personal_plan(data: dict, manifest: dict, z: zipfile.ZipFile) -> tuple[list, list]:
    """The backup's details and artwork for each personal game it names: [{id, title, meta, edits, art}] (meta: the package's details,
    package_metadata; edits: the person's own edits of its personal record, personal.portable_edits; art: art/<id>.jpg|png bytes) and the
    records whose own edits cannot be used [{what, why}] (their details still merge as automatic ones). Nothing is written."""
    import personal as ps
    pm = data.get("personal") if isinstance(data.get("personal"), dict) else {}
    meta = cp.package_metadata(data, manifest)
    ids = list(dict.fromkeys([k for k in pm if isinstance(k, str) and cp.PERSONAL_RE.match(k)] + [k for k in meta if cp.PERSONAL_RE.match(k)]))
    out, bad = [], []
    for i in ids:
        rec = pm.get(i) if isinstance(pm.get(i), dict) else {}; u = rec.get("user") if isinstance(rec.get("user"), dict) else {}
        title = ps._clean_text(u.get("title"), "title") or ps._clean_text((meta.get(i) or {}).get("title"), "title") or i
        ed = None
        try:
            if rec.get("id") not in (None, i): raise ValueError("its record names another game")
            ed = ps.portable_edits(u)
        except ValueError as x: bad.append({"what": title[:120], "why": f"{x}, so the person's own edits in it were not used"})
        out.append({"id": i, "title": title[:120], "meta": meta.get(i) or {}, "edits": ed, "art": cp.art_entry(z, i)})
    return out, bad

def _art_plan(ps, g: dict, u: dict, replace: bool, bad: list):
    """what the backup's artwork does for one personal game on this computer (u: its details as planned, changed here when the artwork
    reference or edit mark changes): (an Item to write or None, "added" | "marked" | "replaced" | "kept" | None)"""
    i = g["id"]; data = g["art"]; target = os.path.join(ps.ART_DIR, i + ".jpg"); rel = "personal/art/" + i + ".jpg"
    edited = set(u.get("edited") or []); cur = _current(target); ref = u.get("art")
    ref_ok = bool(ref) and os.path.isfile(os.path.join(ps.ART_DIR, os.path.basename(str(ref))))
    def mark():
        if "art" in edited: return False
        edited.add("art"); u["edited"] = sorted(edited); return True
    def item(b, action):
        it = Item(target, b, PERSONAL_LABEL); it.action = action; it.cur = cur; return it
    if g["edits"] and g["edits"].get("art") and data is not None:   # the image the person chose: kept here unless replace (rule 4 otherwise)
        if cur is not None and cur == data:
            m = mark(); r = u.get("art") != rel
            if r: u["art"] = rel
            return None, "marked" if (m or r) else None
        if cur is not None and not replace: return None, "kept"
        b = ps.thumb(data)
        if not b: bad.append({"what": g["title"], "why": "its artwork is not a usable JPEG or PNG image, so it was not restored"}); return None, None
        if cur is not None and b == cur: return None, None
        u["art"] = rel; mark(); return item(b, "replace" if cur is not None else "add"), "replaced" if cur is not None else "added"
    if "art" in edited or ref_ok: return None, None
    if cur is not None: u["art"] = rel; return None, "added"   # an image already kept for this game: used first
    if data is None: return None, None
    b = ps.thumb(data)
    if not b: bad.append({"what": g["title"], "why": "its artwork is not a usable JPEG or PNG image, so it was not restored"}); return None, None
    u["art"] = rel; return item(b, "add"), "added"

FIELD_WORDS = {"title": "title", "version_label": "version", "description": "description", "year": "year", "publisher": "publisher", "art": "artwork"}
def _decide_personal(pp: list, replace: bool, note, bad: list, kept: list | None = None) -> list:
    """The backup's details and artwork for the personal games that are on this computer now (the ROM step only adds ROM files and new
    records), decided with the merge policy (personal.py) and the person's own edits: artwork files to add or replace, and this computer's
    list of its own games as ONE replacement, both inside the restore's transaction (kept first, verified, put back on failure). A game
    not on this computer is left to the page (its details stay with the collections). Nothing is written here."""
    if not pp: return []
    import personal as ps
    cur = _current(ps.INDEX)
    if cur is None: return []
    try:
        idx = json.loads(cur.decode("utf-8")); ents = idx["entries"]
        if not isinstance(ents, dict): raise ValueError
    except (ValueError, KeyError, TypeError, UnicodeDecodeError): raise OSError(0, "the list of this computer's own games is damaged")
    items, changed = [], False
    for g in pp:
        e = ents.get(g["id"])
        if not isinstance(e, dict): continue
        u, out = ps.plan_details(e, g["meta"], g["edits"], replace); it, aw = _art_plan(ps, g, u, replace, bad)
        fo = dict(out, **({"art": aw} if aw else {})); ws = set(fo.values())   # an edit mark alone ("marked") leaves the details as they are: counted as already here
        note(PERSONAL_LABEL, "replaced" if "replaced" in ws else "added" if "added" in ws else "kept" if "kept" in ws else "same")
        if kept is not None and "kept" in ws: kept.append({"title": g["title"], "fields": [FIELD_WORDS[f] for f in list(ps.META_LIMITS) + ["art"] if fo.get(f) == "kept"]})
        if it is not None: items.append(it)
        if u != (e.get("user") or {}): ents[g["id"]] = dict(e, user=u); changed = True
    if changed:
        ix = Item(ps.INDEX, json.dumps(idx, indent=1, ensure_ascii=False).replace("\n", os.linesep).encode("utf-8"), PERSONAL_LABEL); ix.action = "replace"; ix.cur = cur; items.append(ix)
    return items

class Item:
    """one file a restore may write: its target, the backup's bytes, a label for the counts, and how "identical" is judged"""
    __slots__ = ("target", "data", "label", "same", "sha", "file", "title", "action", "cur", "keep", "staged", "merge")
    def __init__(self, target, data, label, same=None, sha="", file="", title="", merge=None):
        self.target, self.data, self.label, self.sha, self.file, self.title, self.merge = target, data, label, sha, file, title, merge
        self.same = same or (lambda cur, b=data: cur == b); self.action = None; self.cur = None; self.keep = None; self.staged = None

def _json_bytes(o) -> bytes: return (json.dumps(o, ensure_ascii=False, separators=(",", ":")) + "\n").encode("utf-8")
def _loads(b):
    try: return json.loads(b.decode("utf-8"))
    except Exception: return None

def settings_plan(data: dict) -> tuple[dict, dict | None, list]:
    """Game Settings from both origins, normalized as the player reads them: ({sha: {title, version, record}}, defaults or None, not usable).
    The branch of the platform that made the backup wins where both name the same game."""
    import play as pl
    app = data.get("app") if isinstance(data.get("app"), dict) else {}; web = data.get("web") if isinstance(data.get("web"), dict) else {}
    a = app.get("game_settings") if isinstance(app.get("game_settings"), dict) else {}; w = web.get("game_settings") if isinstance(web.get("game_settings"), dict) else {}
    order = [("web", w), ("app", a)] if data.get("platform") == "web" else [("app", a), ("web", w)]
    games, bad, defaults = {}, [], None
    for origin, src in order:   # the backup's own platform first: its records and defaults win
        gm = src.get("games") if isinstance(src.get("games"), dict) else {}
        for sha, rec in gm.items():
            if sha in games: continue
            what = str((rec or {}).get("title") or sha)[:120] if isinstance(rec, dict) else str(sha)[:64]
            if not isinstance(sha, str) or not SHA.match(sha): bad.append({"what": what, "why": "it does not name an exact game"}); continue
            if not isinstance(rec, dict): bad.append({"what": what, "why": "the record is damaged"}); continue
            if rec.get("rom_content_sha256") not in (None, sha): bad.append({"what": what, "why": "the record belongs to a different game file"}); continue
            clean = pl.clean_record(rec)
            if not clean: bad.append({"what": what, "why": "it holds no choices of its own (every setting uses the default)"}); continue
            games[sha] = {"title": str(rec.get("title") or "")[:200], "version": str(rec.get("version") or "")[:200], "record": clean, "origin": origin}
        d = src.get("defaults")
        if defaults is None and isinstance(d, dict) and isinstance(d.get("rapid"), dict) and d.get("schema") in (None, "ccs-game-defaults/1"): defaults = pl.clean_defaults(d)
        elif defaults is None and d is not None and not (isinstance(d, dict) and isinstance(d.get("rapid"), dict)): bad.append({"what": "Gameplay Defaults", "why": "they are damaged"})
    return games, defaults, bad

def _plan(data_path, path: str, info: dict, controllers: bool) -> tuple[list, list, dict, list, list, list]:
    """(items, rejected progress files, last-used notes {sha: bytes}, settings not usable, personal plan, personal records not usable)"""
    import play as pl
    data = info.get("data") or {}; app = data.get("app") if isinstance(data.get("app"), dict) else {}; titles = _titles(data)
    items, rejected, notes = [], [], {}
    with zipfile.ZipFile(path) as z:
        pp, pbad = _personal_plan(data, info.get("manifest") or {}, z)
        present = set(info.get("progress") or [])
        for n in sorted(present):
            _, sha, f = n.split("/", 2); b = z.read(n)
            if f == "last-used.json": notes[sha] = b; continue
            st, why, h = file_check(f, b, sha)
            if st != "ok": rejected.append({"sha": sha, "title": titles.get(sha) or str(h.get("title") or "")[:120], "file": f, "status": st, "why": why}); continue
            items.append(Item(os.path.join(data_path("saves"), sha, f), b, "progress", sha=sha, file=f, title=titles.get(sha, "")))
        for x in app.get("progress") or []:   # a backup that names saved games it does not hold (a partial package): said, never guessed
            if not isinstance(x, dict) or not isinstance(x.get("sha"), str): continue
            for f in x.get("files") or []:
                if isinstance(f, str) and f"progress/{x['sha']}/{f}" not in present: rejected.append({"sha": x["sha"], "title": titles.get(x["sha"], ""), "file": f, "status": "missing", "why": "the backup lists it but does not hold it"})
        custom = [n for n in info.get("custom") or [] if n != "custom/overrides.json"]
        if custom:
            import overrides as ov
            for n in custom: items.append(Item(ov._abs(n[len("custom/"):]), z.read(n), "custom image"))
        custom_json = z.read("custom/overrides.json") if "custom/overrides.json" in (info.get("custom") or []) else None
    games, defaults, bad = settings_plan(data); gsd = data_path("game-settings")
    if defaults is not None:
        def same_def(cur, d=defaults):
            j = _loads(cur); return isinstance(j, dict) and j.get("schema") == "ccs-game-defaults/1" and pl.clean_defaults(j) == d
        items.append(Item(os.path.join(gsd, "defaults.json"), _json_bytes({"schema": "ccs-game-defaults/1", **defaults}), "gameplay defaults", same_def))
    for sha, g in sorted(games.items()):
        def same_rec(cur, sha=sha, r=g["record"]):
            j = _loads(cur); return isinstance(j, dict) and j.get("schema") == "ccs-game-prefs/1" and j.get("rom_content_sha256") == sha and pl.clean_record(j) == r
        items.append(Item(os.path.join(gsd, sha + ".json"), _json_bytes({"schema": "ccs-game-prefs/1", "rom_content_sha256": sha, "title": g["title"], "version": g["version"], **g["record"]}),
                          "game settings", same_rec, sha=sha, title=g["title"]))
    def same_json(cur, o):
        return _loads(cur) == o
    ps = app.get("player_settings")
    if isinstance(ps, dict): items.append(Item(data_path("player-settings.json"), json.dumps(ps, indent=1).encode(), "play on pc settings", lambda cur, o=ps: same_json(cur, o)))
    if controllers:
        c = app.get("controllers") if isinstance(app.get("controllers"), dict) else {}
        if isinstance(c.get("pads"), dict): items.append(Item(data_path("player-pads.json"), json.dumps(c["pads"], indent=1).encode(), "controller setups", lambda cur, o=c["pads"]: same_json(cur, o)))
        if isinstance(c.get("assign"), dict): items.append(Item(data_path("player-assign.json"), json.dumps(c["assign"]).encode(), "controller choices", lambda cur, o=c["assign"]: same_json(cur, o)))
    if custom_json is not None: items.append(Item(_overrides_store(), custom_json, "custom text and artwork", merge=lambda cur, rep, note, nb=custom_json: _merge_overrides(cur, nb, rep, note)))
    return items, rejected, notes, bad, pp, pbad

def _merge_overrides(cur_b: bytes | None, new_b: bytes, replace: bool, note) -> bytes | None:
    """the custom text and artwork store, game by game: games not here are added, identical ones skipped, different ones kept unless
    replace; returns the merged store (None: nothing to write)"""
    try: cur = json.loads(cur_b.decode("utf-8")) if cur_b else {"schema": "ccs-overrides/1", "games": {}}
    except Exception: cur = None
    new = json.loads(new_b.decode("utf-8"))
    if not isinstance(cur, dict) or not isinstance(cur.get("games"), dict): cur = {**new, "games": {}}
    changed = False
    for vid, g in (new.get("games") or {}).items():
        if vid not in cur["games"]: cur["games"][vid] = g; note("added"); changed = True
        elif cur["games"][vid] == g: note("same")
        elif replace: cur["games"][vid] = g; note("replaced"); changed = True
        else: note("kept")
    return json.dumps(cur, ensure_ascii=False, indent=1).encode("utf-8") if changed else None

def _overrides_store() -> str:
    import overrides as ov
    return ov._store()

def _read(p: str) -> bytes:
    with open(p, "rb") as f: return f.read()
def _current(p: str):
    """the current bytes, None when absent; an unreadable file stops the plan (it is never treated as absent)"""
    return _read(p) if os.path.exists(p) else None

def _decide(items: list, notes: dict, replace: bool, counts: dict, rejected: list, titles: dict, saves: str, pp: list | None = None, pbad: list | None = None, pkept: list | None = None) -> list:
    """what happens to each item against the current data; returns the items to write (action add / replace): the personal games'
    artwork and list first, then the files"""
    def note(label, what): counts.setdefault(label, {"added": 0, "same": 0, "kept": 0, "replaced": 0})[what] += 1
    todo = _decide_personal(pp or [], replace, note, pbad if pbad is not None else [], pkept)
    for it in items:
        cur = _current(it.target)
        if it.merge:
            merged = it.merge(cur, replace, lambda what: note("custom games", what))
            if merged is None: continue
            it.data = merged; it.cur = cur; it.action = "replace" if cur is not None else "add"; todo.append(it); continue
        if cur is None: it.action = "add"; note(it.label, "added"); todo.append(it)
        elif it.same(cur): it.action = "same"; note(it.label, "same")
        elif not replace: it.action = "keep"; note(it.label, "kept")
        else: it.action = "replace"; it.cur = cur; note(it.label, "replaced"); todo.append(it)
    by = {(it.sha, it.file): it for it in items if it.label == "progress"}
    for sha, nb in sorted(notes.items()):   # a last-used note: restored only when the slot it names will hold exactly the state it names
        st, why, j = file_check("last-used.json", nb, sha); title = titles.get(sha, "")
        if st != "ok": rejected.append({"sha": sha, "title": title, "file": "last-used.json", "status": st, "why": why}); continue
        slot = f"slot{j['slot']}.ccsstate"; it = by.get((sha, slot))
        if it is not None and it.action in ("add", "replace"): final = it.data
        else: final = _current(os.path.join(saves, sha, slot))
        s2, _, h = state_check(final, sha) if final is not None else ("absent", "", {})
        good = s2 == "ok" and h.get("crc32") == j.get("slot_crc32") and h.get("created") == j.get("slot_created")
        if not good:
            where = ("this backup does not hold a usable save there" if (sha, slot) not in by and final is None else "the slot here holds a different save (kept)" if it is not None and it.action == "keep"
                     else "the slot will hold a different save" if s2 == "ok" else "that slot's save is not usable")
            rejected.append({"sha": sha, "title": title, "file": "last-used.json", "status": "invalid", "why": f"it names {SLOT_NAMES[slot]}, but {where}"}); continue
        lu = Item(os.path.join(saves, sha, "last-used.json"), nb, "progress", sha=sha, file="last-used.json", title=title)
        cur = _current(lu.target)
        if cur is None: lu.action = "add"; note("progress", "added"); todo.append(lu)
        elif cur == nb: note("progress", "same")
        elif not replace: note("progress", "kept")
        else: lu.action = "replace"; lu.cur = cur; note("progress", "replaced"); todo.append(lu)
    return todo

def preview(data_path, path: str, info: dict) -> dict:
    """counts of new / identical / different files per kind, and the files that would not be restored (with why); nothing is written"""
    items, rejected, notes, bad, pp, pbad = _plan(data_path, path, info, controllers=True); res: dict = {}
    for it in items:
        k = res.setdefault(it.label, {"new": 0, "same": 0, "different": 0})
        try: cur = _current(it.target)
        except OSError: k["different"] += 1; continue
        if cur is None: k["new"] += 1
        elif it.merge: k["different" if it.merge(cur, True, lambda w: None) else "same"] += 1
        else: k["same" if it.same(cur) else "different"] += 1
    if pp:   # the personal games: details to add here (also those whose ROM file the restore adds), already the same, or different here
        pc: dict = {}; held = {r.get("version_id") for r in info.get("roms") or []}; pk: list = []
        try:
            import personal as ps
            here = set((ps.load_index().get("entries") or {}).keys())
            _decide_personal(pp, False, lambda label, what: pc.__setitem__(what, pc.get(what, 0) + 1), pbad, pk)
        except (OSError, ValueError): here = set(); pc = {"kept": len(pp)}
        k = res.setdefault(PERSONAL_LABEL, {"new": 0, "same": 0, "different": 0})
        k["new"] += pc.get("added", 0) + sum(1 for g in pp if g["id"] not in here and g["id"] in held); k["same"] += pc.get("same", 0); k["different"] += pc.get("kept", 0) + pc.get("replaced", 0)
    titles = _titles(info.get("data") or {}); by = {(it.sha, it.file): it for it in items if it.label == "progress"}
    for sha, nb in sorted(notes.items()):
        st, why, j = file_check("last-used.json", nb, sha)
        if st == "ok":
            src = by.get((sha, f"slot{j['slot']}.ccsstate")); s2, _, h = state_check(src.data, sha) if src else ("absent", "", {})
            if s2 == "ok" and h.get("crc32") == j.get("slot_crc32") and h.get("created") == j.get("slot_created"):
                k = res.setdefault("progress", {"new": 0, "same": 0, "different": 0}); cur = _current(os.path.join(data_path("saves"), sha, "last-used.json"))
                k["new" if cur is None else "same" if cur == nb else "different"] += 1; continue
            why = "it names " + SLOT_NAMES["slot%d.ccsstate" % j["slot"]] + ", but " + ("this backup does not hold a usable save there" if s2 != "ok" else "that slot holds a different save")
        rejected.append({"sha": sha, "title": titles.get(sha, ""), "file": "last-used.json", "status": "invalid", "why": why})
    return {"counts": res, "rejected": rejected, "not_applicable": bad, "personal_not_used": pbad, "personal_kept": pk if pp else [], "pending": pending(data_path)}

# ---------------- restore: the verified transaction ----------------
class RestoreFailed(Exception): pass

def _txid(data_path) -> str:
    for _ in range(100):
        t = time.strftime("%Y%m%d-%H%M%S") + "-" + secrets.token_hex(4)
        if not os.path.exists(data_path(f"restore-{t}.journal.json")): return t
    raise RestoreFailed("no free restore identity")
def _write_new(p: str, b: bytes):
    """write-once: a new file, never over an existing one (FileExistsError otherwise); flushed to the disk"""
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "xb") as f: f.write(b); f.flush(); os.fsync(f.fileno())
def _verify(p: str, b: bytes, what: str):
    if _read(p) != b: raise RestoreFailed(f"{what} did not read back correctly")
def _move(src: str, dst: str): os.replace(src, dst)
def _put_back(e: dict):
    """undo one applied change and check it: an added file is removed; a replaced file gets its preserved original back"""
    t = e["target"]
    if e["action"] == "add":
        if os.path.exists(t): os.remove(t)
        if os.path.exists(t): raise RestoreFailed("the added file is still there")
        return
    orig = _read(e["keep"])
    if _sha(orig) != e["before"]: raise RestoreFailed("the kept original does not match")
    tmp = t + f".putback-{e['tx']}"
    if os.path.exists(tmp): os.remove(tmp)
    _write_new(tmp, orig); _move(tmp, t)
    if _sha(_read(t)) != e["before"]: raise RestoreFailed("the original did not read back correctly")

def _journal_path(data_path, tx) -> str: return data_path(f"restore-{tx}.journal.json")
def _journal_write(data_path, tx, entries, state):
    p = _journal_path(data_path, tx); tmp = p + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f: json.dump({"schema": "ccs-restore-journal/1", "id": tx, "state": state, "written": time.strftime("%Y-%m-%dT%H:%M:%S"), "entries": entries}, f, indent=1); f.flush(); os.fsync(f.fileno())
    os.replace(tmp, p)

def _rel(data_path, p: str) -> str:
    try:
        r = os.path.relpath(p, data_path()); return p if r.startswith("..") else r
    except ValueError: return p

def _rollback(data_path, tx, entries) -> list:
    """put back every applied change, newest first, each checked; returns the entries that could not be put back"""
    left = []
    for e in reversed(entries):
        if not e.get("applied"): continue
        try: _put_back(e); e["applied"] = False; e["rolled_back"] = True
        except Exception as x: e["error"] = str(getattr(x, "strerror", None) or x)[:200]; left.append(e)
    return left

def _cleanup_staged(items):
    for it in items:
        if it.staged and os.path.exists(it.staged):
            try: os.remove(it.staged)
            except OSError: pass

@contextlib.contextmanager
def player_state():
    """Whether a game window is open, with the game player's own lock held for the whole block (play.RUN.lock: no game window can open
    while a restore or its Try Again runs): "closed" | "open" | "unknown" (the player could not be asked; saved progress is then not written)"""
    try:
        import play as pl
        lock = pl.RUN.lock
    except Exception: lock = None
    if lock is None:
        yield "unknown"; return
    with lock:
        try: st = "open" if pl._running() else "closed"
        except Exception: st = "unknown"
        yield st

def _player_refusal(st: str, retry: str) -> str:
    return (("A game window is open" if st == "open" else "Classic Cart Studio could not check whether a game window is open") + ", so saved games can't be "
            + retry + " now. Close the game window, then " + ("restore again" if retry == "restored" else "choose Try Again") + ".")

def restore(data_path, path: str, info: dict, replace: bool = False, controllers: bool = False, player_open: bool = False, check_only: bool = False) -> dict:
    """One verified restore (module notes). Saved progress is written only when the game player says no game window is open, and its lock
    is held throughout (player_open=True: the caller knows one is open). Refused while an earlier restore is unfinished (Try Again first)."""
    with player_state() as st:
        return _restore(data_path, path, info, replace, controllers, "open" if player_open else st, check_only)

def _restore(data_path, path, info, replace, controllers, player, check_only) -> dict:
    pend = pending(data_path)
    if pend: return {"ok": False, "nothing_changed": True, "pending": pend, "txid": pend[0],
                     "error": "An earlier restore did not finish putting everything back, so this restore did not start. Choose Try Again to finish the earlier one first. Nothing was changed."}
    try: items, rejected, notes, bad, pp, pbad = _plan(data_path, path, info, controllers)
    except (OSError, zipfile.BadZipFile, KeyError) as e: return {"ok": False, "nothing_changed": True, "error": f"The backup could not be read ({getattr(e, 'strerror', None) or e}). Nothing was changed."}
    if player != "closed" and (any(it.label == "progress" for it in items) or notes):
        return {"ok": False, "nothing_changed": True, "player": player, "error": _player_refusal(player, "restored") + " Nothing was changed."}
    counts: dict = {}; titles = _titles(info.get("data") or {}); pkept: list = []
    try: todo = _decide(items, notes, replace, counts, rejected, titles, data_path("saves"), pp, pbad, pkept)
    except OSError as e: return {"ok": False, "nothing_changed": True, "error": f"A current file could not be read ({e.strerror or e}), so nothing was restored. Nothing was changed."}
    base = {"counts": counts, "rejected": rejected, "not_applicable": bad, "personal_not_used": pbad, "personal_kept": pkept}
    if check_only: return {"ok": True, "check": True, **base}
    if not todo: return {"ok": True, "txid": None, "replaced_folder": None, **base}
    tx = _txid(data_path); folder = f"replaced-{tx}"
    # 1. stage: every new content written beside its target under this restore's own name, and read back (no current file is touched)
    try:
        for it in todo:
            it.staged = it.target + f".restoring-{tx}"; _write_new(it.staged, it.data); _verify(it.staged, it.data, "a staged file")
    except Exception as e:
        _cleanup_staged(todo)
        return {"ok": False, "nothing_changed": True, "error": f"The restore could not prepare its files ({getattr(e, 'strerror', None) or e}). Nothing was changed.", **base}
    # 2. the journal, then each current file being replaced kept first (write-once, read back)
    entries = [{"tx": tx, "target": it.target, "action": it.action, "label": it.label, "before": _sha(it.cur) if it.cur is not None else None, "after": _sha(it.data),
                "keep": os.path.join(os.path.dirname(it.target), folder, os.path.basename(it.target)) if it.action == "replace" else None, "applied": False} for it in todo]
    try:
        _journal_write(data_path, tx, entries, "preparing")
        for it, e in zip(todo, entries):
            if it.action != "replace": continue
            now = _read(it.target)
            if now != it.cur: raise RestoreFailed(f"{os.path.basename(it.target)} changed while the restore was being prepared")
            _write_new(e["keep"], now); _verify(e["keep"], now, "a kept original")
        _journal_write(data_path, tx, entries, "applying")
    except Exception as e:
        _cleanup_staged(todo)
        try: os.remove(_journal_path(data_path, tx))
        except OSError: pass
        return {"ok": False, "nothing_changed": True, "error": f"The restore could not keep the current files first ({getattr(e, 'strerror', None) or e}). Nothing was changed.", **base}
    # 3. apply: each staged file moved into place and checked
    try:
        for it, e in zip(todo, entries):
            _move(it.staged, it.target); e["applied"] = True; _verify(it.target, it.data, os.path.basename(it.target))
    except Exception as err:
        reason = str(getattr(err, "strerror", None) or err)
        left = _rollback(data_path, tx, entries); _cleanup_staged(todo)
        if not left:
            try: os.remove(_journal_path(data_path, tx))
            except OSError: pass
            return {"ok": False, "rolled_back": True, "nothing_changed": True, "txid": tx, "reason": reason, "replaced_folder": folder if any(e["keep"] for e in entries) else None,
                    "error": f"The restore stopped before it finished ({reason}). Everything it changed was put back and checked; nothing else was changed.", **base}
        try: _journal_write(data_path, tx, entries, "rollback incomplete")
        except Exception: pass
        changed = [{"path": _rel(data_path, e["target"]), "what": "added by the restore" if e["action"] == "add" else "replaced by the restore",
                    "original": _rel(data_path, e["keep"]) if e["keep"] else None, "error": e.get("error", "")} for e in left]
        return {"ok": False, "rolled_back": False, "partial": True, "txid": tx, "reason": reason, "replaced_folder": folder, "changed": changed, "journal": _rel(data_path, _journal_path(data_path, tx)),
                "error": f"The restore stopped before it finished ({reason}), and " + ("1 file" if len(changed) == 1 else f"{len(changed)} files") + " could not be put back. "
                         + (("Its original is kept in the folder named \"" + folder + "\" beside it. " if len(changed) == 1 else "Their originals are kept in the folders named \"" + folder + "\" beside them. ") if any(c["original"] for c in changed) else "")
                         + "Choose Try Again to finish putting " + ("it" if len(changed) == 1 else "them") + " back.", **base}
    try: os.remove(_journal_path(data_path, tx))
    except OSError: pass
    return {"ok": True, "txid": tx, "replaced_folder": folder if any(e["keep"] for e in entries) else None, **base}

def pending(data_path) -> list:
    """restores whose journal is still here (their rollback could not finish, or the app stopped while one was applying)"""
    try: return sorted(m.group(1) for f in os.listdir(data_path()) for m in [re.fullmatch(r"restore-([0-9]{8}-[0-9]{6}-[0-9a-f]{8})\.journal\.json", f)] if m)
    except OSError: return []

def _is_progress(data_path, e: dict) -> bool:
    t = e.get("target")
    if e.get("label") == "progress": return True
    if not isinstance(t, str): return False
    try: return os.path.commonpath([os.path.normcase(os.path.abspath(t)), os.path.normcase(os.path.abspath(data_path("saves")))]) == os.path.normcase(os.path.abspath(data_path("saves")))
    except ValueError: return False

def recover(data_path, tx: str, player_open: bool = False) -> dict:
    """Try Again after a restore whose rollback could not finish (or that was interrupted): every change its journal names is judged by
    the file's content (its content after the restore: put back; its original: nothing to do; anything else: changed since, left as it
    is) and put back, each checked; the journal is removed only when nothing is left. When the journal names saved progress, it runs only
    while the game player says no game window is open (its lock held throughout); otherwise nothing is touched (current files, kept
    originals, staged files and the journal stay exactly as they are) and the result says to close the game window."""
    if not re.fullmatch(r"[0-9]{8}-[0-9]{6}-[0-9a-f]{8}", tx or ""): return {"ok": False, "error": "That restore is not known."}
    with player_state() as st:
        return _recover(data_path, tx, "open" if player_open else st)

def _recover(data_path, tx, player) -> dict:
    j = _read_json(_journal_path(data_path, tx))
    if not isinstance(j, dict) or not isinstance(j.get("entries"), list): return {"ok": False, "error": "That restore has nothing left to put back."}
    if player != "closed" and any(isinstance(e, dict) and _is_progress(data_path, e) for e in j["entries"]):
        return {"ok": False, "blocked": True, "player": player, "txid": tx, "error": _player_refusal(player, "put back") + " Nothing was changed: the earlier restore's files and the originals it kept stay as they are."}
    entries = j["entries"]; left = []; changed_since = []; kept_since = []
    for e in reversed(entries):
        t = e.get("target")
        if not isinstance(t, str) or e.get("action") not in ("add", "replace"): continue
        try: cur = _sha(_read(t)) if os.path.exists(t) else None
        except OSError as x: e["error"] = str(x.strerror or x)[:200]; e["applied"] = True; left.append(e); continue
        if cur == e.get("before"): e["applied"] = False; continue   # already its original (or, for an added file, not there)
        if cur != e.get("after"): changed_since.append(_rel(data_path, t)); kept_since.extend([_rel(data_path, e["keep"])] if isinstance(e.get("keep"), str) and os.path.isfile(e["keep"]) else []); e["applied"] = False; continue   # changed since: left as it is
        e["applied"] = True
        try: _put_back(e); e["applied"] = False; e["rolled_back"] = True
        except Exception as x: e["error"] = str(getattr(x, "strerror", None) or x)[:200]; left.append(e)
    for e in entries:   # this restore's own staged copies (never an original)
        s = e["target"] + f".restoring-{tx}" if isinstance(e.get("target"), str) else None
        if s and os.path.exists(s):
            try: os.remove(s)
            except OSError: pass
    if left:
        _journal_write(data_path, tx, entries, "rollback incomplete")
        return {"ok": False, "partial": True, "txid": tx, "changed": [{"path": _rel(data_path, e["target"]), "original": _rel(data_path, e["keep"]) if e.get("keep") else None, "error": e.get("error", "")} for e in left],
                "error": f"{len(left)} file{'' if len(left) == 1 else 's'} still could not be put back. Their originals stay in the \"replaced-{tx}\" folders beside them."}
    try: os.remove(_journal_path(data_path, tx))
    except OSError: pass
    return {"ok": True, "txid": tx, "changed_since": changed_since, "kept_originals": kept_since,
            "message": "Everything the restore changed was put back and checked." + (f" {len(changed_since)} file{'' if len(changed_since) == 1 else 's'} had changed since and {'was' if len(changed_since) == 1 else 'were'} left as {'it is' if len(changed_since) == 1 else 'they are'}: "
                                                                                  + ", ".join(changed_since[:6]) + (f" (the originals stay in the \"replaced-{tx}\" folders beside them)" if kept_since else "") + "." if changed_since else "")}
