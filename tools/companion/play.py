r"""Play on PC (Classic Cart Studio for Windows): the game player, ccs-player.exe (tools/player), started for one game (Direct Game Mode) or
for the configured HOME (HOME Mode). The player is given a session file and nothing else: the games it may run (each by its exact content
identity, with the file this computer holds and, for a headerless file, the iNES header from the Game Library's own data; nothing is
guessed and nothing is matched by title), the per-user folder for saved games (independent of the installation, collections, loadouts and
HOME), its settings and where to write its result. One player at a time.

  GET  /api/play/availability?ids=a,b   -> {ok, games: {id: {playable, reason}}}          (desktop principal only)
  POST /api/play/direct  {id}           -> {ok, mode, title} | {ok: false, error, busy?}
  POST /api/play/home    {manifest, home, embed?, select?} -> {ok, mode, games, playable, unavailable}
  POST /api/play/home-refresh {manifest, home} -> {ok}   the running embedded HOME's package rebuilt (the page then tells the player "reload")
       embed: {"pipe": "ccs-embed-<32 hex>"}: the Studio window's pipe for the Preview's game player (D-104); the player draws into the
       Preview and takes its input from there. select: the game id highlighted in the Preview (kept when the HOME opens)
  GET  /api/play/status                 -> {ok, running, mode, title, last: <the last result>}
  GET  /api/play/settings / POST /api/play/settings {keys, display, swap_pads} -> the player's settings (validated key names)
  GET  /api/play/game-settings?sha=<64 hex> -> {ok, defaults, record, display}   one exact game's Game Settings (record: only its choices)
  POST /api/play/game-settings {sha, title, version, record}                   replaced atomically; an empty record removes it (Reset)
  GET  /api/play/game-defaults / POST /api/play/game-defaults {rapid}          the Gameplay Defaults (rapid fire for every game)
The game settings are the game player's own files (tools/player/core/gp.h): game-settings/<sha>.json and game-settings/defaults.json.
"""
from __future__ import annotations
import json, os, re, shutil, subprocess, sys, threading, time
HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path: sys.path.insert(0, HERE)
from common import FROZEN, APP_ROOT, WORK, data_path   # noqa: E402

PROBLEM: str | None = None
def player_binary() -> str | None:
    """the player installed with the application (verified against the installation's file list), or a development build"""
    global PROBLEM; PROBLEM = None
    env = os.environ.get("CCS_PLAYER_EXE")
    if env and os.path.isfile(env): return env
    if FROZEN:
        import bundle_integrity as bi
        try: return next(p for p in bi.require("player") if p.lower().endswith("ccs-player.exe"))
        except bi.ComponentError as e: PROBLEM = str(e); return None
        except StopIteration: PROBLEM = "the game player is missing from this installation"; return None
    p = os.path.join(WORK, "m23", "build", "ccs-player.exe")
    return p if os.path.isfile(p) else None
def fonts_dir() -> str:
    env = os.environ.get("CCS_PLAYER_FONTS")
    if env and os.path.isdir(env): return env
    if FROZEN: return os.path.join(APP_ROOT, "_internal", "player", "fonts")
    return os.path.join(WORK, "m23", "build", "fonts")

# ---------------- exact ROMs ----------------
def _header_for(vid: str, content_sha256: str) -> str:
    """the iNES header for a headerless file of this exact content: personal games carry their own; library versions use the header the
    Game Library records for that payload (library.json); none means the game cannot be played (never guessed)"""
    sys.path.insert(0, os.path.join(HERE, "..", "rom-pipeline"))
    try:
        import library_lookup as LL
        if vid.startswith("p_"):
            e = (LL.personal_index().get("entries") or {}).get(vid) or {}; h = ((e.get("detected") or {}).get("header_hex") or "")
        else:
            pl = (LL._library().get("payloads") or {}).get(content_sha256) or {}; h = ((pl.get("header") or {}).get("hex") or "")
            if not h: r = LL.resolve(vid) or {}; h = r.get("header_hex") or ""
    except Exception: h = ""
    return h if isinstance(h, str) and re.fullmatch(r"(?i)4e45531a[0-9a-f]{24}", h or "") else ""
REASONS = {"missing": "The ROM file of this game is not on this computer. Add it with Add Your Own Game (Choose Files… or Choose a Folder…): a file is matched to this version by its content.",
           "mismatched": "The ROM file on this computer is a different version of this game, so it is not used (nothing is substituted). Add the file of this exact version with Add Your Own Game.",
           "no_identity": "This entry has no exact game identity, so no ROM file can be matched to it.",
           "no_header": "This ROM file has no header and the Game Library has no header information for it, so it cannot be played (nothing is guessed)."}
def rom_for(vid: str, title: str = "", ctx: dict | None = None) -> dict:
    import package_api as PA, ccs_package as cp
    if not cp.is_identity_id(vid): return {"playable": False, "code": "no_identity", "reason": REASONS["no_identity"]}
    status, rec, path, ident = PA.classify(vid, title, ctx if ctx is not None else {})
    if status != "available" or not path or not ident: return {"playable": False, "code": status if status in REASONS else "missing", "reason": REASONS.get(status, REASONS["missing"])}
    hdr = "" if ident.get("headered") else _header_for(vid, ident["content_sha256"])
    if not ident.get("headered") and not hdr: return {"playable": False, "code": "no_header", "reason": REASONS["no_header"]}
    return {"playable": True, "rom": path, "header_hex": hdr, "content_sha256": ident["content_sha256"]}

# ---------------- the player process (one at a time) ----------------
class Run:
    def __init__(self): self.lock = threading.RLock(); self.proc = None; self.mode = None; self.title = None; self.dir = None; self.last = None; self.started = None
RUN = Run()
def _running() -> bool: return RUN.proc is not None and RUN.proc.poll() is None
def _watch(proc, d):
    rc = proc.wait(); res = None
    try: res = json.load(open(os.path.join(d, "result.json"), encoding="utf-8"))
    except Exception: res = {"exit": "no_result", "error": "The game player closed without a result."}
    with RUN.lock:
        RUN.last = {"rc": rc, "result": res, "mode": RUN.mode, "title": RUN.title, "ended": time.time()}
        if RUN.proc is proc: RUN.proc = None
def _prune_sessions(root: str, keep: int = 5):
    try: ds = sorted(d for d in os.listdir(root) if d.startswith("session-"))
    except OSError: return
    for d in ds[:-keep]: shutil.rmtree(os.path.join(root, d), ignore_errors=True)
def embed_pipe(body) -> str:
    """the Studio window's pipe name for the embedded player, or "" (only the exact form the window makes is accepted)"""
    em = body.get("embed") if isinstance(body, dict) else None; n = em.get("pipe") if isinstance(em, dict) else None
    return n if isinstance(n, str) and re.fullmatch(r"ccs-embed-[0-9a-f]{32}", n) else ""
def _pipe_listening(name: str) -> bool:
    """the Studio window still listens on the Preview's pipe (listed, never opened: opening it would take the player's place)"""
    try: return name in os.listdir("\\\\.\\pipe\\")
    except OSError: return True   # not listed here: the player finds out itself (it ends when the pipe is gone)
def _start(mode: str, title: str, games: list, direct_id: str = "", pack: str = "", state_dir: str = "", embed: str = "", select: str = "") -> dict:
    with RUN.lock:
        if _running(): return {"ok": False, "busy": True, "error": f"{RUN.title or 'A game'} is already open in the player. Close it first."}
        exe = player_binary()
        if not exe: return {"ok": False, "error": PROBLEM or "The game player is not available in this installation.", "repair_required": bool(PROBLEM)}
        root = data_path("play"); os.makedirs(root, exist_ok=True); _prune_sessions(root)
        d = os.path.join(root, time.strftime("session-%Y%m%d-%H%M%S-") + os.urandom(3).hex()); os.makedirs(os.path.join(d, "scratch"))
        sess = {"schema": "ccs-play-session/1", "mode": mode, "saves_root": data_path("saves"), "fonts_dir": fonts_dir(), "scratch_dir": os.path.join(d, "scratch"),
                "result_path": os.path.join(d, "result.json"), "settings_path": data_path("player-settings.json"), "display_path": data_path("player-display.json"), "game_settings_dir": data_path("game-settings"),
                "studio_pid": int(os.environ.get("CCS_HOST_PID") or os.getppid() or 0), "games": games, "direct_id": direct_id, "home": {"pack": pack, "state_dir": state_dir or (os.path.join(d, "home-state") if mode == "home" else "")}}
        if embed: sess["embed"] = {"pipe": "\\\\.\\pipe\\" + embed}   # the Preview's player (D-104): pictures and input through the Studio window
        if select and isinstance(select, str) and len(select) <= 40: sess["home"]["select"] = select
        if mode == "home": import home_music as HMU; sess["home"]["music"] = {"asset": HMU.asset() or "", "pref": HMU.pref_path()}   # D-81: the game window plays HOME Music itself; its On / Off is the one saved for this computer
        os.makedirs(sess["saves_root"], exist_ok=True)
        p = os.path.join(d, "session.json"); json.dump(sess, open(p, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        args = [exe, "--session", p]
        if os.environ.get("CCS_PLAYER_TRACE") == "1": args += ["--trace", os.path.join(d, "trace.jsonl")]   # tests: the player's own event trace
        if os.environ.get("CCS_PLAYER_TEST_INPUT") == "1": args += ["--test-posted-input"]   # tests: keys posted to the player's window count without focus (labeled in its trace)
        if embed and not _pipe_listening(embed): return {"ok": False, "error": "The Preview was closed before its HOME started."}   # no late start: the Preview closed while this HOME was prepared
        try: proc = subprocess.Popen(args, cwd=d, close_fds=True)
        except OSError as e: return {"ok": False, "error": f"The game player could not start ({e})."}
        RUN.proc = proc; RUN.mode = mode; RUN.title = title; RUN.dir = d; RUN.started = time.time(); RUN.embedded = bool(embed)
        threading.Thread(target=_watch, args=(proc, d), daemon=True).start()
        return {"ok": True, "mode": mode, "title": title, "session": os.path.basename(d)}
def start_direct(vid: str, embed: str = "") -> dict:
    sys.path.insert(0, os.path.join(HERE, "..", "rom-pipeline")); import library_lookup as LL
    row = None
    try: row = LL.resolve(vid)
    except Exception: row = None
    title = (row or {}).get("display_title") or vid; label = (row or {}).get("nointro_name") or ""
    r = rom_for(vid, title)
    if not r["playable"]: return {"ok": False, "error": r["reason"], "code": r["code"]}
    g = {"id": vid, "title": title, "version_label": label, "rom": r["rom"], "header_hex": r["header_hex"], "content_sha256": r["content_sha256"], "unavailable": ""}
    return _start("direct", title, [g], direct_id=vid, embed=embed)
def start_home(manifest: dict, home: dict, embed: str = "", select: str = "") -> dict:
    with RUN.lock:
        if _running(): return {"ok": False, "busy": True, "error": f"{RUN.title or 'A game'} is already open in the player. Close it first."}
    from native_home import package as P
    res = P.build(manifest, home, mode="play")
    if not res.get("ok"): return {"ok": False, "error": res.get("error") or "; ".join(res.get("problems") or []) or "The HOME could not be prepared."}
    pack = res["package_dir"]; pg = json.load(open(os.path.join(pack, "games.json"), encoding="utf-8")).get("games") or []
    games, ctx, ready, missing = [], {}, 0, []
    for e in pg:
        vid = str(e.get("id") or ""); title = str(e.get("title") or vid)[:200]
        r = rom_for(vid, title, ctx)
        g = {"id": vid, "title": title, "version_label": str(e.get("version_label") or "")[:200], "rom": r.get("rom", ""), "header_hex": r.get("header_hex", ""),
             "content_sha256": r.get("content_sha256", ""), "unavailable": "" if r["playable"] else r["reason"]}
        games.append(g); ready += 1 if r["playable"] else 0
        if not r["playable"]: missing.append({"id": vid, "title": title, "code": r["code"]})
    if not games: return {"ok": False, "error": "This HOME has no games."}
    out = _start("home", (manifest.get("name") or "HOME"), games, pack=pack, embed=embed, select=select)   # HOME preferences in the session's own folder: as Preview on TV, no theme carried over
    if out.get("ok"): out.update({"games": len(games), "playable": ready, "unavailable": missing[:50], "unavailable_count": len(missing)})
    return out
def status() -> dict:
    with RUN.lock: return {"ok": True, "running": _running(), "mode": RUN.mode if _running() else None, "title": RUN.title if _running() else None, "embedded": bool(_running() and getattr(RUN, "embedded", False)), "last": RUN.last}
def refresh_home(manifest: dict, home: dict) -> dict:
    """the embedded HOME's package again (same folder): the collection, loadout or HOME settings changed while it is open"""
    from native_home import package as P
    res = P.build(manifest, home, mode="play")
    return {"ok": True} if res.get("ok") else {"ok": False, "error": res.get("error") or "; ".join(res.get("problems") or []) or "The HOME could not be prepared."}

# ---------------- settings (keys by name; Esc is the menu key and cannot be assigned) ----------------
KEYS = {*(chr(c) for c in range(65, 91)), *(str(d) for d in range(10)), *(f"F{i}" for i in range(1, 13)), *(f"Numpad{d}" for d in range(10)),
        "Up", "Down", "Left", "Right", "Enter", "Backspace", "Space", "Tab", "Shift", "LShift", "RShift", "Ctrl", "LCtrl", "RCtrl", "Home", "End", "PageUp", "PageDown",
        "Insert", "Delete", "Comma", "Period", "Slash", "Semicolon", "Quote", "LeftBracket", "RightBracket", "Minus", "Equals", "Backslash", "Backquote"}
ACTIONS = ("up", "down", "left", "right", "a", "b", "select", "start")
DEFAULT = {"schema": "ccs-player-settings/1", "keys": {"p1": {"up": "Up", "down": "Down", "left": "Left", "right": "Right", "a": "Z|Enter", "b": "X|Backspace", "select": "S", "start": "M"},
                                                       "p2": {"up": "I", "down": "K", "left": "J", "right": "L", "a": "H", "b": "G", "select": "T", "start": "Y"}},
           "display": {"aspect": "tv", "integer": False}, "swap_pads": False}
def settings_load() -> dict:
    try: s = json.load(open(data_path("player-settings.json"), encoding="utf-8"))
    except Exception: s = {}
    out = json.loads(json.dumps(DEFAULT))
    if isinstance(s, dict):
        for p in ("p1", "p2"):
            for a in ACTIONS:
                v = ((s.get("keys") or {}).get(p) or {}).get(a)
                if isinstance(v, str) and (v == "" or all(k in KEYS for k in v.split("|")) and len(v.split("|")) <= 2): out["keys"][p][a] = v
        d = s.get("display") or {}
        if d.get("aspect") in ("tv", "square"): out["display"]["aspect"] = d["aspect"]
        if isinstance(d.get("integer"), bool): out["display"]["integer"] = d["integer"]
        if isinstance(s.get("swap_pads"), bool): out["swap_pads"] = s["swap_pads"]
    return out
def settings_save(body: dict) -> dict:
    cur = settings_load(); keys = body.get("keys") if isinstance(body, dict) else None; bad = []
    if isinstance(keys, dict):
        for p in ("p1", "p2"):
            for a in ACTIONS:
                v = (keys.get(p) or {}).get(a)
                if v is None: continue
                if not isinstance(v, str) or len(v.split("|")) > 2 or not all(k in KEYS for k in v.split("|") if k) : bad.append(f"{p}.{a}"); continue
                cur["keys"][p][a] = v
    d = body.get("display") if isinstance(body, dict) else None
    if isinstance(d, dict):
        if d.get("aspect") in ("tv", "square"): cur["display"]["aspect"] = d["aspect"]
        if isinstance(d.get("integer"), bool): cur["display"]["integer"] = d["integer"]
    if isinstance(body.get("swap_pads") if isinstance(body, dict) else None, bool): cur["swap_pads"] = body["swap_pads"]
    if bad: return {"ok": False, "error": "These keys cannot be used: " + ", ".join(bad)}
    p = data_path("player-settings.json"); os.makedirs(os.path.dirname(p), exist_ok=True); tmp = p + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f: json.dump(cur, f, indent=1); f.flush(); os.fsync(f.fileno())
    os.replace(tmp, p); return {"ok": True, "settings": cur}

# ---------------- Game Settings (the player's model: one record per exact game, the defaults; tools/player/core/gp.h) ----------------
SPEEDS = ("slow", "medium", "fast")
BUILTIN_DEFAULTS = {"rapid": {"p1": {"a": False, "b": False}, "p2": {"a": False, "b": False}, "speed": "medium"}}
def _gs_dir() -> str: return data_path("game-settings")
def _sha_ok(sha) -> bool: return isinstance(sha, str) and re.fullmatch(r"[0-9a-f]{64}", sha) is not None
def _atomic_json(path: str, obj) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True); tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f: f.write(json.dumps(obj, ensure_ascii=False, separators=(",", ":")) + "\n"); f.flush(); os.fsync(f.fileno())
    os.replace(tmp, path)
def clean_record(r) -> dict:
    """only well-formed choices (unknown keys dropped); False is a choice, a missing field means Use Default"""
    out: dict = {}
    if not isinstance(r, dict): return out
    rp = r.get("rapid")
    if isinstance(rp, dict):
        q: dict = {}
        for p in ("p1", "p2"):
            x = rp.get(p); y = {k: x[k] for k in ("a", "b") if isinstance(x, dict) and isinstance(x.get(k), bool)}
            if y: q[p] = y
        if rp.get("speed") in SPEEDS: q["speed"] = rp["speed"]
        if q: out["rapid"] = q
    c = r.get("controls")
    if isinstance(c, dict) and isinstance(c.get("swap_ab"), bool): out["controls"] = {"swap_ab": c["swap_ab"]}
    pi = r.get("picture")
    if isinstance(pi, dict):
        q = {}
        if pi.get("aspect") in ("tv", "square"): q["aspect"] = pi["aspect"]
        if isinstance(pi.get("integer"), bool): q["integer"] = pi["integer"]
        if q: out["picture"] = q
    return out
def clean_defaults(d) -> dict:
    out = json.loads(json.dumps(BUILTIN_DEFAULTS)); rp = d.get("rapid") if isinstance(d, dict) else None
    if isinstance(rp, dict):
        for p in ("p1", "p2"):
            for k in ("a", "b"):
                v = (rp.get(p) or {}).get(k) if isinstance(rp.get(p), dict) else None
                if isinstance(v, bool): out["rapid"][p][k] = v
        if rp.get("speed") in SPEEDS: out["rapid"]["speed"] = rp["speed"]
    return out
def defaults_load() -> dict:
    try: d = json.load(open(os.path.join(_gs_dir(), "defaults.json"), encoding="utf-8"))
    except Exception: d = None
    return clean_defaults(d if isinstance(d, dict) and d.get("schema") == "ccs-game-defaults/1" else None)
def defaults_save(body) -> dict:
    d = clean_defaults(body if isinstance(body, dict) else None)
    try: _atomic_json(os.path.join(_gs_dir(), "defaults.json"), {"schema": "ccs-game-defaults/1", **d})
    except OSError as e: return {"ok": False, "error": f"The defaults could not be saved on this computer ({e.strerror or e}). The earlier defaults are unchanged."}
    return {"ok": True, "defaults": d}
def record_load(sha: str) -> dict | None:
    try: r = json.load(open(os.path.join(_gs_dir(), sha + ".json"), encoding="utf-8"))
    except FileNotFoundError: return {}
    except Exception: return None   # unreadable: shown as such, never guessed
    if not isinstance(r, dict) or r.get("schema") != "ccs-game-prefs/1" or r.get("rom_content_sha256") != sha: return None
    return clean_record(r)
def record_save(sha: str, title, version, rec) -> dict:
    clean = clean_record(rec); path = os.path.join(_gs_dir(), sha + ".json")
    try:
        if not clean:
            if os.path.exists(path): os.remove(path)
            return {"ok": True, "record": {}}
        _atomic_json(path, {"schema": "ccs-game-prefs/1", "rom_content_sha256": sha, "title": str(title or "")[:200], "version": str(version or "")[:200], **clean})
    except OSError as e: return {"ok": False, "error": f"The game settings could not be saved on this computer ({e.strerror or e}). The earlier settings are unchanged."}
    return {"ok": True, "record": clean}

# ---------------- HTTP routes (desktop principal only; dispatched from nes_companion.py) ----------------
def _desktop(h) -> bool:
    b = h._bearer(); return bool(b and b.get("desktop"))
def handle_get(h, path: str, qs: str):
    if not _desktop(h): return h._json(403, {"ok": False, "error": "Play on PC works only in Classic Cart Studio for Windows."})
    if path == "/api/play/status": return h._json(200, status())
    if path == "/api/play/settings": return h._json(200, {"ok": True, "settings": settings_load()})
    if path == "/api/play/home-music": import home_music as HMU; return h._json(200, {"ok": True, **HMU.get(), "available": bool(HMU.asset())})   # D-81: this computer's HOME Music choice
    if path == "/api/play/game-defaults": return h._json(200, {"ok": True, "defaults": defaults_load()})
    if path == "/api/play/game-settings":
        import urllib.parse
        sha = (urllib.parse.parse_qs(qs).get("sha") or [""])[0]
        if not _sha_ok(sha): return h._json(400, {"ok": False, "error": "the game's exact identity is required"})
        rec = record_load(sha); disp = settings_load()["display"]
        if rec is None: return h._json(200, {"ok": True, "defaults": defaults_load(), "record": {}, "unreadable": True, "display": disp})
        return h._json(200, {"ok": True, "defaults": defaults_load(), "record": rec, "display": disp})
    if path == "/api/play/availability":
        import urllib.parse
        q = urllib.parse.parse_qs(qs); ids = [x for x in ",".join(q.get("ids", [""])).split(",") if x][:400]; ctx = {}; out = {}
        for vid in ids:
            r = rom_for(vid, "", ctx); out[vid] = {"playable": r["playable"], "code": r.get("code"), "reason": r.get("reason")}
        return h._json(200, {"ok": True, "games": out, "player": bool(player_binary()), "problem": PROBLEM})
    return h._json(404, {"ok": False, "error": "unknown play endpoint"})
def handle_post(h, path: str, body: dict):
    if not _desktop(h): return h._json(403, {"ok": False, "error": "Play on PC works only in Classic Cart Studio for Windows."})
    body = body if isinstance(body, dict) else {}
    if path == "/api/play/home-music":
        import home_music as HMU; r = HMU.save(body.get("music") if isinstance(body.get("music"), str) else "")
        return h._json(200 if r.get("ok") else 400 if "On or Off" in r.get("error", "") else 500, r)
    if path == "/api/play/direct":
        vid = body.get("id")
        if not isinstance(vid, str) or len(vid) > 40: return h._json(400, {"ok": False, "error": "the game is required"})
        try: r = start_direct(vid, embed_pipe(body))
        except Exception as e: r = {"ok": False, "error": f"The game could not start ({e})."}
        return h._json(200 if r.get("ok") else 409, r)
    if path == "/api/play/home":
        man, home = body.get("manifest"), body.get("home")
        if not isinstance(man, dict) or not isinstance(home, dict): return h._json(400, {"ok": False, "error": "the collection and the HOME configuration are required"})
        try: r = start_home(man, home, embed_pipe(body), body.get("select") if isinstance(body.get("select"), str) else "")
        except Exception as e: r = {"ok": False, "error": f"The HOME could not start ({e})."}
        return h._json(200 if r.get("ok") else 409, r)
    if path == "/api/play/home-refresh":
        man, home = body.get("manifest"), body.get("home")
        if not isinstance(man, dict) or not isinstance(home, dict): return h._json(400, {"ok": False, "error": "the collection and the HOME configuration are required"})
        try: r = refresh_home(man, home)
        except Exception as e: r = {"ok": False, "error": f"The HOME could not be prepared ({e})."}
        return h._json(200 if r.get("ok") else 409, r)
    if path == "/api/play/settings": r = settings_save(body); return h._json(200 if r.get("ok") else 400, r)
    if path == "/api/play/game-defaults": r = defaults_save(body); return h._json(200 if r.get("ok") else 500, r)
    if path == "/api/play/game-settings":
        sha = body.get("sha")
        if not _sha_ok(sha): return h._json(400, {"ok": False, "error": "the game's exact identity is required"})
        r = record_save(sha, body.get("title"), body.get("version"), body.get("record")); return h._json(200 if r.get("ok") else 500, r)
    return h._json(404, {"ok": False, "error": "unknown play endpoint"})
