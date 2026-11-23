r"""Game Saves export and import: a person's saved games carried privately between Classic Cart Studio in a browser and Classic Cart
Studio for Windows, as one ordinary ZIP file they move themselves (Files, Drive, email, AirDrop...). No ROM file, box art, screenshot, game
player, setting or collection travels in it, and Classic Cart Studio keeps no copy anywhere (no account, no server).

The file ("Classic Cart Studio Game Saves <date>.zip"), schema ccs-game-saves/1:
  ccs-game-saves.json   the manifest: when and where it was made (platform, game player core), and for each game its exact content identity
                        (SHA-256 of the ROM after the iNES header), its title and every file with its size, SHA-256, kind and, for a slot, the
                        slot's name and the time the save was made (from the save itself, not a file date; "modified" is the file's own date)
  saves/<sha>/<file>    the game's saved-games files exactly as the game player wrote them: cart.sav (the cartridge's battery save),
                        slot1..4.ccsstate (the four save slots), slot-names.json, last-used.json, game.json
Saves made in a browser and in the Windows app load in either (the same pinned game player; gp_save.c gp_platform_ok). The NES Classic's own
saves are not part of this. A save can hold bytes of the game's own memory, so "no ROM files" means no ROM file is included, not that a save
holds nothing from the game.

Import checks everything before anything is written: an ordinary ZIP (no folders, links, encryption, unusual compression or oversized
members, no duplicate names, every member named in the manifest with its exact size and SHA-256), each save by the player's own rules
(studio_backup.file_check: the game it belongs to, the player version and platform, its size and check value). The person then chooses, for
each save that differs from one here, what happens: nothing here is replaced without that choice, and every file replaced is kept first
(replaced-<tx> beside it). The write is the full restore's own transaction (studio_backup.apply_items), with the game player's lock held,
refused while a game window is open. Saves of a game whose ROM file is not on this computer are kept anyway (saved games belong to the exact
game identity, not to a collection; nothing joins a collection): adding that exact ROM file later makes them playable.
Part of Classic Cart Studio: GNU GPL v3 or later."""
from __future__ import annotations
import hashlib, json, os, re, stat, time, zipfile, zlib
import studio_backup as sb, platform_ids

SCHEMA = "ccs-game-saves/1"; MANIFEST = "ccs-game-saves.json"
SLOTS = tuple(f"slot{n}.ccsstate" for n in range(1, 5))
FILES = ("cart.sav",) + SLOTS + ("slot-names.json", "last-used.json", "game.json")
MEMBER = re.compile(r"^saves/([0-9a-f]{64})/(cart\.sav|slot[1-4]\.ccsstate|slot-names\.json|last-used\.json|game\.json)$")
MAX_MEMBERS = 4001; MAX_TOTAL = 2_000_000_000; MAX_RATIO = 400; MAX_MANIFEST = 4_000_000
NOTE = ("Saved games from Classic Cart Studio. No game files are included: each game's saves load only with that exact game, added separately. "
        "A save can hold bytes from the game's own memory.")

class SavesError(Exception): pass

def _sha(b: bytes) -> str: return hashlib.sha256(b).hexdigest()
def _iso(t: float) -> str: return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(t))
def _limit(name: str) -> int: return sb.MAX_STATE + 65536 if name.endswith(".ccsstate") else sb.MAX_SRAM if name.endswith(".sav") else sb.MAX_NOTE
def kind_of(name: str) -> str: return "cart" if name == "cart.sav" else "slot" if name.endswith(".ccsstate") else name[:-5]
def _names(b: bytes | None) -> dict:
    try: j = json.loads((b or b"").decode("utf-8")); n = j.get("names") if isinstance(j, dict) else None
    except (UnicodeDecodeError, ValueError): return {}
    return {k: v for k, v in (n or {}).items() if k in ("1", "2", "3", "4") and isinstance(v, str)} if isinstance(n, dict) else {}
def _title(b: bytes | None) -> str:
    try: j = json.loads((b or b"").decode("utf-8")); return str(j.get("last_title") or "")[:120] if isinstance(j, dict) else ""
    except (UnicodeDecodeError, ValueError): return ""
def describe(name: str, b: bytes, sha: str, names: dict, mtime: float | None = None) -> dict:
    d = {"name": name, "kind": kind_of(name), "bytes": len(b), "sha256": _sha(b)}
    if name.endswith(".ccsstate"):
        n = name[4]; d["slot"] = int(n); _, _, h = sb.state_check(b, sha); d["saved_at"] = str(h.get("created") or "")[:40]; d["slot_name"] = names.get(n, "")[:96]
    if mtime is not None: d["modified"] = _iso(mtime)
    return d

# ---------------- export ----------------
class ReadFailed(SavesError):
    """the saved games on this computer could not be read, so what exists is not known (never taken for "no saved games")"""

def _read_failed(e: OSError, what: str) -> ReadFailed:
    return ReadFailed(f"Classic Cart Studio could not read {what} on this computer ({e.strerror or e}), so no Game Saves file was made. "
                      "Nothing was changed. Check that the folder can be opened, then try again.")

def collect(data_path, shas=None) -> tuple[list, list]:
    """every game's saved games on this computer that the player would load: [{sha, title, files: [(name, bytes, mtime)]}], skipped files
    (saves the player would not load, with the reason). Only a saves folder that does not exist yet means no saves: a folder, game folder
    or save file that cannot be listed or read raises ReadFailed (Codex v1p4 F3), so an export never leaves out what it could not see."""
    root = data_path("saves"); games, skipped = [], []
    try: dirs = sorted(os.listdir(root))
    except FileNotFoundError: return [], []   # nothing has been saved on this computer yet
    except OSError as e: raise _read_failed(e, "the saved games folder")
    for sha in dirs:
        if not sb.SHA.match(sha) or (shas is not None and sha not in shas): continue
        d = os.path.join(root, sha); files = []
        try: here = set(os.listdir(d))
        except NotADirectoryError: continue   # a file with a game's name is not a game's saves
        except FileNotFoundError: continue    # removed while being read
        except OSError as e: raise _read_failed(e, "a game's saved games")
        for name in FILES:
            if name not in here: continue
            p = os.path.join(d, name)
            try:
                if not stat.S_ISREG(os.stat(p).st_mode): continue
                b = sb._read(p); mt = os.path.getmtime(p)
            except FileNotFoundError: continue
            except OSError as e: raise _read_failed(e, f"a saved game ({name})")
            st, why, _ = sb.file_check(name, b, sha)
            if st != "ok": skipped.append({"sha": sha, "name": name, "why": why}); continue
            files.append((name, b, mt))
        if any(n == "cart.sav" or n in SLOTS for n, _, _ in files):   # a game with saved progress (its notes alone are not saves)
            games.append({"sha": sha, "title": _title(next((b for n, b, _ in files if n == "game.json"), None)), "files": files})
    return games, skipped

def export(data_path, out: str, shas=None, version: str = "") -> dict:
    try: games, skipped = collect(data_path, shas)
    except ReadFailed as e: return {"ok": False, "read_error": True, "error": str(e)}   # nothing written; a file already at `out` stays as it was
    if not games: return {"ok": False, "error": "There are no saved games to export yet. Save a game in a slot, or play a game that keeps a cartridge save, first."}
    man = {"schema": SCHEMA, "app": "Classic Cart Studio", "app_version": str(version or "")[:40], "made": _iso(time.time()), "platform": sb.PLATFORM,
           "core": f"{sb.CORE_ID} {sb.CORE_VERSION}", "note": NOTE, "games": []}
    for g in games:
        nm = _names(next((b for n, b, _ in g["files"] if n == "slot-names.json"), None))
        man["games"].append({"content_sha256": g["sha"], "title": g["title"], "files": [describe(n, b, g["sha"], nm, mt) for n, b, mt in g["files"]]})
    tmp = out + ".part"
    try:
        with zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as z:
            z.writestr(MANIFEST, json.dumps(man, ensure_ascii=False, indent=1) + "\n")
            for g in games:
                for n, b, _ in g["files"]: z.writestr(f"saves/{g['sha']}/{n}", b, compress_type=_method(b))
        read(tmp)   # the file reads back exactly as an import will read it
        os.replace(tmp, out)
    except (OSError, SavesError) as e:
        try: os.remove(tmp)
        except OSError: pass
        return {"ok": False, "error": f"The Game Saves file could not be written ({getattr(e, 'strerror', None) or e}). Nothing was saved."}
    return {"ok": True, "path": out, "name": os.path.basename(out), "games": len(games), "files": sum(len(g["files"]) for g in games),
            "bytes": os.path.getsize(out), "skipped": skipped}

def _method(b: bytes) -> int:
    """How a saved game is written: compressed, unless it compresses past half the readers' limit (MAX_RATIO). A legitimate save can be
    mostly one repeated byte (a Mapper 30 flash save is 524,800 bytes, nearly all erased), and the readers here and in the browser refuse a
    member that expands more than MAX_RATIO times; such a save is stored as it is, so every file this writer makes is one they accept."""
    if not b: return zipfile.ZIP_STORED
    c = zlib.compressobj(6, zlib.DEFLATED, -15); n = len(c.compress(b)) + len(c.flush())
    return zipfile.ZIP_DEFLATED if len(b) / max(1, n) <= MAX_RATIO / 2 else zipfile.ZIP_STORED

# ---------------- reading a Game Saves file (nothing is written) ----------------
def _member(z: zipfile.ZipFile, name: str, limit: int) -> bytes:
    try:
        with z.open(name) as f: b = f.read(limit + 1)
    except (zipfile.BadZipFile, zlib.error, OSError, RuntimeError) as e: raise SavesError(f"The file is damaged ({name} could not be read: {e}).")
    if len(b) > limit: raise SavesError(f"The file is damaged ({name} is larger than it can be).")
    return b

def read(path: str) -> dict:
    """{manifest, members: {name: bytes}, token: the file's SHA-256}; SavesError (a plain reason) for anything that is not exactly right"""
    try:
        with open(path, "rb") as f: token = hashlib.sha256(f.read()).hexdigest()
        z = zipfile.ZipFile(path)
    except (zipfile.BadZipFile, OSError): raise SavesError("This file is not a Classic Cart Studio Game Saves file (it is not a ZIP file).")
    with z:
        infos = z.infolist(); seen = set(); total = 0
        if len(infos) > MAX_MEMBERS: raise SavesError("The file holds more than a Game Saves file can.")
        for i in infos:
            n = i.filename
            if n in seen: raise SavesError(f"The file names {n[:80]} twice, so it was not used.")
            seen.add(n)
            if i.is_dir() or (i.external_attr >> 16) & 0o170000 == 0o120000 or i.flag_bits & 1 or i.compress_type not in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED):
                raise SavesError("The file holds a folder, a link, an encrypted or unusually compressed entry, so it was not used.")
            if n != MANIFEST and not MEMBER.match(n): raise SavesError(f"The file holds something that is not a saved game ({n[:80]}), so it was not used.")
            lim = MAX_MANIFEST if n == MANIFEST else _limit(n.rsplit("/", 1)[-1])
            if i.file_size > lim or (i.compress_size and i.file_size / max(1, i.compress_size) > MAX_RATIO): raise SavesError(f"The file is damaged ({n[:80]} is larger than it can be).")
            total += i.file_size
        if total > MAX_TOTAL: raise SavesError("The file holds more than a Game Saves file can.")
        if MANIFEST not in seen: raise SavesError("This file is not a Classic Cart Studio Game Saves file (it has no ccs-game-saves.json).")
        try: man = json.loads(_member(z, MANIFEST, MAX_MANIFEST).decode("utf-8"))
        except (UnicodeDecodeError, ValueError): raise SavesError("The file's list of saves is damaged, so it was not used.")
        if not isinstance(man, dict) or man.get("schema") != SCHEMA: raise SavesError("This file is not a Game Saves file this version can read.")
        tgt = platform_ids.resolve_target(man, "game-saves", man.get("games"))   # saves for a game system this version does not support
        if not tgt["ok"]: raise SavesError(tgt["text"])
        listed = {}
        for g in man.get("games") if isinstance(man.get("games"), list) else []:
            sha = g.get("content_sha256") if isinstance(g, dict) else None
            if not isinstance(sha, str) or not sb.SHA.match(sha) or not isinstance(g.get("files"), list): raise SavesError("The file's list of saves is damaged, so it was not used.")
            for f in g["files"]:
                nm = f.get("name") if isinstance(f, dict) else None; key = f"saves/{sha}/{nm}"
                if not isinstance(nm, str) or not MEMBER.match(key) or key in listed or not isinstance(f.get("bytes"), int) or not isinstance(f.get("sha256"), str):
                    raise SavesError("The file's list of saves is damaged, so it was not used.")
                listed[key] = (f["bytes"], f["sha256"])
        if set(listed) != seen - {MANIFEST}: raise SavesError("The file's list of saves does not match what it holds, so it was not used.")
        members = {}
        for n, (size, sh) in listed.items():
            b = _member(z, n, _limit(n.rsplit("/", 1)[-1]))
            if len(b) != size or _sha(b) != sh: raise SavesError(f"The file is damaged ({n.rsplit('/', 1)[-1]} does not match its check value).")
            members[n] = b
    return {"manifest": man, "members": members, "token": token}

# ---------------- what an import would do (nothing is written) ----------------
def _current(d: str) -> dict:
    cur = {}
    for name in FILES:
        p = os.path.join(d, name)
        if os.path.isfile(p): cur[name] = sb._read(p)   # OSError: the caller says the current saves could not be read (never "none")
    return cur

def plan(data_path, path: str, rom_state=None) -> dict:
    """{ok, token, made, platform, games: [{sha, title, rom, free_slots, files: [{name, kind, slot, slot_name, saved_at, bytes, status
    ("add" | "same" | "conflict"), current}]}], rejected: [{sha, title, name, why}]}"""
    r = read(path); man = r["manifest"]; members = r["members"]; root = data_path("saves"); games, rejected = [], []
    for g in man["games"]:
        sha = g["content_sha256"]; title = str(g.get("title") or "")[:120]
        got = {f["name"]: members[f"saves/{sha}/{f['name']}"] for f in g["files"]}
        ok = {}
        for name, b in got.items():
            st, why, _ = sb.file_check(name, b, sha)
            if st != "ok": rejected.append({"sha": sha, "title": title, "name": name, "why": why, "status": st})
            else: ok[name] = b
        try: cur = _current(os.path.join(root, sha))
        except OSError as e: raise SavesError(f"This computer's saved games for {title or 'a game'} could not be read ({e.strerror or e}). Nothing was changed.")
        inn, cn = _names(ok.get("slot-names.json")), _names(cur.get("slot-names.json")); items = []
        for name in ("cart.sav",) + SLOTS:
            if name not in ok: continue
            b = ok[name]; it = describe(name, b, sha, inn); c = cur.get(name)
            if name == "cart.sav" and c is not None and len(c) != len(b):
                rejected.append({"sha": sha, "title": title, "name": name, "why": "this game's cartridge save here has a different size, so the imported one belongs to a different cartridge layout", "status": "incompatible"}); continue
            it["status"] = "add" if c is None else "same" if c == b else "conflict"
            if it["status"] == "conflict": it["current"] = describe(name, c, sha, cn)
            items.append(it)
        if not items: continue
        games.append({"sha": sha, "title": title or _title(ok.get("game.json")), "rom": rom_state(sha) if rom_state else "unknown", "files": items,
                      "free_slots": [n for n in range(1, 5) if f"slot{n}.ccsstate" not in cur]})
    return {"ok": True, "token": r["token"], "made": str(man.get("made") or "")[:40], "platform": str(man.get("platform") or "")[:40], "games": games, "rejected": rejected}

# ---------------- the import ----------------
def _decide(sha: str, ok: dict, cur: dict, choice: dict) -> tuple[list, list]:
    """the files this game's import writes ([(name, bytes)]) from the person's choices; a choice that is not possible now stops the import.
    choice: {"cart.sav": "add" | "replace" | "keep", "slotN.ccsstate": "slotK" | "replace" | "keep"} (missing: "keep" for a save that differs,
    else "add" / its own slot)"""
    out, notes = [], []; taken = set(); inn, cn = _names(ok.get("slot-names.json")), _names(cur.get("slot-names.json")); names = dict(cn); moved = {}
    if "cart.sav" in ok:
        c = cur.get("cart.sav"); ch = choice.get("cart.sav") or ("add" if c is None else "keep")
        if c is None and ch in ("add", "replace"): out.append(("cart.sav", ok["cart.sav"]))
        elif c is not None and c != ok["cart.sav"] and ch == "replace":
            if len(c) != len(ok["cart.sav"]): raise SavesError("The cartridge save here has a different size, so it was not replaced.")
            out.append(("cart.sav", ok["cart.sav"]))
    for name in SLOTS:
        if name not in ok: continue
        n = name[4]; b = ok[name]; c = cur.get(name); ch = choice.get(name) or ("keep" if c is not None else "slot" + n)
        if ch == "keep" or (c is not None and c == b and ch in ("slot" + n, "replace")): continue
        if ch == "replace": tgt = name
        elif re.fullmatch(r"slot[1-4]", ch or ""):
            tgt = ch + ".ccsstate"
            if tgt in cur: raise SavesError(f"Slot {ch[4]} is not empty any more, so nothing was imported. Look at the choices again.")
        else: raise SavesError("A choice for the import was not understood, so nothing was imported.")
        if tgt in taken: raise SavesError(f"Two saves were set to go into Slot {tgt[4]}, so nothing was imported.")
        taken.add(tgt); out.append((tgt, b)); moved[n] = tgt[4]
        if inn.get(n): names[tgt[4]] = inn[n]
        elif tgt[4] in names: names.pop(tgt[4])   # an unnamed imported save: the slot's old name does not describe it
    if moved and names != cn:
        out.append(("slot-names.json", sb._json_bytes({"schema": "ccs-slot-names/1", "rom_content_sha256": sha, "names": {k: names[k] for k in sorted(names)}})))
    lu = ok.get("last-used.json")
    if lu is not None and "last-used.json" not in cur:
        try: j = json.loads(lu.decode("utf-8")); n = str(j.get("slot"))
        except (UnicodeDecodeError, ValueError, AttributeError): n = ""
        if moved.get(n) == n: out.append(("last-used.json", lu))   # the save it names went into that same slot (the player checks its crc/created again)
    if "game.json" in ok and "game.json" not in cur and out: out.append(("game.json", ok["game.json"]))
    return out, notes

def apply(data_path, path: str, token: str, choices: dict, player_open: bool = False) -> dict:
    """one verified import of a staged Game Saves file with the person's choices ({sha: {file: choice}}); refused while a game window is open
    or an earlier restore is unfinished; the file must be the one the person looked at (token)"""
    with sb.player_state() as st:
        player = "open" if player_open else st
        pend = sb.pending(data_path)
        if pend: return {"ok": False, "nothing_changed": True, "pending": pend, "error": "An earlier restore did not finish putting everything back, so nothing was imported. Choose Try Again on that restore first. Nothing was changed."}
        if player != "closed": return {"ok": False, "nothing_changed": True, "player": player, "error": sb._player_refusal(player, "imported") + " Nothing was changed."}
        try: r = read(path)
        except SavesError as e: return {"ok": False, "nothing_changed": True, "error": f"{e} Nothing was changed."}
        if r["token"] != token: return {"ok": False, "nothing_changed": True, "error": "The Game Saves file changed since it was opened. Open it again. Nothing was changed."}
        root = data_path("saves"); todo, written = [], []
        try:
            for g in r["manifest"]["games"]:
                sha = g["content_sha256"]; d = os.path.join(root, sha)
                ok = {}
                for f in g["files"]:
                    b = r["members"][f"saves/{sha}/{f['name']}"]
                    if sb.file_check(f["name"], b, sha)[0] == "ok": ok[f["name"]] = b
                cur = _current(d)
                files, _ = _decide(sha, ok, cur, (choices or {}).get(sha) or {})
                for name, b in files:
                    it = sb.Item(os.path.join(d, name), b, "progress", sha=sha, file=name, title=str(g.get("title") or "")[:120])
                    it.cur = cur.get(name); it.action = "replace" if it.cur is not None else "add"; todo.append(it)
                    written.append({"sha": sha, "file": name, "action": it.action})
        except SavesError as e: return {"ok": False, "nothing_changed": True, "error": str(e)}
        except OSError as e: return {"ok": False, "nothing_changed": True, "error": f"This computer's saved games could not be read ({e.strerror or e}). Nothing was changed."}
        res = sb.apply_items(data_path, todo, {"written": written}, "import")
        return res
