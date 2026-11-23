"""HOME Music: the selected theme, the whole song looping, in every HOME, On by default.

One preference for this computer's user, not for a collection, a loadout or a backup: <data folder>/home-music.json
({"schema": "ccs-home-music/1", "music": "on" | "off"}). Absent: On (the default). A file that cannot be read or is not this
schema: "unreadable", played as Off (never an unexpected burst of sound) until the person chooses again, which writes a new file.
Full Studio Backups, collection files and loadouts never carry it, so no import or restore changes it.

The music itself (tools/publish/assets/home-music/music.json records its identity):
  native  ccs-home-theme.wav   PCM, 16-bit, 48,000 Hz, stereo: the Windows game window's HOME and the console's HOME package
  browser brand/home/music/ccs-home-theme-<hash>.m4a: the song's original AAC stream, copied (the website and the app's Preview)"""
import json, os
from common import FROZEN, APP_ROOT, PROJECT, data_path

SCHEMA = "ccs-home-music/1"
FILE = "ccs-home-theme.wav"
# The same song as MP3 for the console (128 kbit/s, 48,000 Hz stereo, an Info tag with its encoder delay 576 and padding 576), made from
# FILE by FFmpeg's libmp3lame: built and proven with the game player's MP3 build (tools/player/core/gp_mp3.c) but not used. The console's
# HOME Music stays FILE until this exact file passes its console recording and smoothness checks and is approved after a final listen.
# Only this file is accepted (by its SHA-256); it is not part of this version's sources or installation.
MP3_FILE = "ccs-home-theme.mp3"; MP3_SHA256 = "fd6d49336bf7e68f541d323a7cf5b2b0ad61d69ee169f40a619e34af033907ab"; MP3_BYTES = 993068; MP3_FRAMES = 2976768

def mp3_asset() -> str | None:
    """the held MP3 for a private build that proves it (CCS_HOME_MUSIC_MP3 names it); None unless it is exactly the file above"""
    p = os.environ.get("CCS_HOME_MUSIC_MP3") or ""
    if not os.path.isfile(p) or os.path.getsize(p) != MP3_BYTES: return None
    import hashlib
    with open(p, "rb") as f: return p if hashlib.sha256(f.read()).hexdigest() == MP3_SHA256 else None

def asset_dir() -> str:
    if FROZEN: return os.path.join(APP_ROOT, "_internal", "home-music")
    return os.path.join(PROJECT, "tools", "publish", "assets", "home-music")

def asset() -> str | None:
    """the native PCM file installed with the application (or the development copy); None when it is missing"""
    env = os.environ.get("CCS_HOME_MUSIC")
    p = env if env else os.path.join(asset_dir(), FILE)
    return p if os.path.isfile(p) else None

def info() -> dict:
    """the recorded identity of the music (frames, rate, hashes), or {} when the record is missing"""
    try:
        with open(os.path.join(asset_dir(), "music.json"), encoding="utf-8") as f: return json.load(f)
    except (OSError, ValueError): return {}

def pref_path() -> str: return data_path("home-music.json")

def get() -> dict:
    """{"music": "on" | "off", "source": "default" | "saved" | "unreadable"}: what HOME plays and why"""
    p = pref_path()
    try:
        with open(p, encoding="utf-8") as f: d = json.load(f)
    except FileNotFoundError: return {"music": "on", "source": "default"}
    except (OSError, ValueError): return {"music": "off", "source": "unreadable"}
    if isinstance(d, dict) and d.get("schema") == SCHEMA and d.get("music") in ("on", "off"): return {"music": d["music"], "source": "saved"}
    return {"music": "off", "source": "unreadable"}

def save(value: str) -> dict:
    """save the person's choice (written to a temporary file, then renamed into place); {"ok": False, "error": ...} when it cannot be written"""
    if value not in ("on", "off"): return {"ok": False, "error": "HOME Music is On or Off."}
    p = pref_path(); tmp = p + ".tmp"
    try:
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(tmp, "w", encoding="utf-8") as f: json.dump({"schema": SCHEMA, "music": value}, f); f.flush(); os.fsync(f.fileno())
        os.replace(tmp, p)
    except OSError as e:
        try: os.remove(tmp)
        except OSError: pass
        return {"ok": False, "error": f"The HOME Music choice could not be saved on this computer ({e.strerror or e})."}
    return {"ok": True, "music": value, "source": "saved"}
