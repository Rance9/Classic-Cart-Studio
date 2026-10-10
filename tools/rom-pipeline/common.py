"""Shared helpers for the NES ROM pipeline (PC-only; never touches the console).

Paths: source archives under <SOURCE> (see dev-paths.json) (read-only); working files under <WORK>;
reports under the project's reports\\ folder. Nothing here writes into roms\\incoming.
"""
from __future__ import annotations
import hashlib, os, re, sqlite3, struct, zlib, json, sys, time
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field, asdict
from typing import Optional

PROJECT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
HAKCHI = os.path.join(PROJECT, "hakchi", "hakchi2-ce-3.9.3")
# Development-only locations (the private ROM archive and the pipeline work folder) are never written into source: they come from
# tools/rom-pipeline/dev-paths.json (git-ignored) or the environment; defaults are inside the checkout so public builds and tests run.
_DEVP = os.path.join(os.path.dirname(os.path.abspath(__file__)), "dev-paths.json")
_DEV = json.load(open(_DEVP, encoding="utf-8")) if os.path.exists(_DEVP) else {}
SOURCE = os.environ.get("NES_ROM_SOURCE") or _DEV.get("source") or os.path.join(PROJECT, "work", "source")
WORK = os.environ.get("NES_WORK") or _DEV.get("work") or os.path.join(PROJECT, "work")
PAYLOADS = os.path.join(WORK, "payloads")
LOGS = os.path.join(WORK, "logs")
DB_PATH = os.path.join(WORK, "inventory.sqlite")
REPORTS = os.path.join(PROJECT, "reports")
INCOMING = os.path.join(PROJECT, "roms", "incoming")
# ---- runtime roots: the packaged Windows companion reads catalog/builtin/library data from <app>\site and keeps user data under
#      %LOCALAPPDATA%\NESClassicCollectionStudio; in the development checkout both are the project's reports\ folder ----
FROZEN = bool(getattr(sys, "frozen", False))
APP_ROOT = os.path.dirname(sys.executable) if FROZEN else PROJECT
BUNDLE = getattr(sys, "_MEIPASS", APP_ROOT)   # PyInstaller one-folder: bundled data lives in <app>\_internal
SITE_ROOT = os.environ.get("NES_STUDIO_SITE") or (os.path.join(BUNDLE, "site") if FROZEN else REPORTS)
DATA_ROOT = os.environ.get("NES_STUDIO_DATA") or (os.path.join(os.environ.get("LOCALAPPDATA") or APP_ROOT, "NESClassicCollectionStudio") if FROZEN else REPORTS)
DEV_MODE = os.path.exists(DB_PATH) and not FROZEN
def site_path(*p): return os.path.join(SITE_ROOT, *p)
def data_path(*p): return os.path.join(DATA_ROOT, *p)

KACHIKACHI_MAPPERS = {0, 1, 2, 3, 4, 5, 7, 9, 10, 86, 87, 184}
MAX_ENTRY_BYTES = 8 * 1024 * 1024      # no NES ROM payload is larger than ~4 MiB; cap guards zip bombs
MAX_ENTRIES_PER_ARCHIVE = 32

def ensure_dirs():
    for d in (WORK, PAYLOADS, LOGS, REPORTS):
        os.makedirs(d, exist_ok=True)

def log(msg: str, logfile: Optional[str] = None):
    line = f"{time.strftime('%Y-%m-%d %H:%M:%S')} | {msg}"
    print(line, flush=True)
    if logfile:
        with open(logfile, "a", encoding="utf-8") as f:
            f.write(line + "\n")

def sha256_bytes(b: bytes) -> str: return hashlib.sha256(b).hexdigest()
def sha1_bytes(b: bytes) -> str: return hashlib.sha1(b).hexdigest().upper()
def crc32_bytes(b: bytes) -> str: return f"{zlib.crc32(b) & 0xFFFFFFFF:08X}"

# ---------- CRC32 combine (zlib algorithm) so headered CRCs can be derived without re-hashing payloads ----------
def _gf2_matrix_times(mat, vec):
    s = 0; i = 0
    while vec:
        if vec & 1: s ^= mat[i]
        vec >>= 1; i += 1
    return s
def _gf2_matrix_square(square, mat):
    for n in range(32): square[n] = _gf2_matrix_times(mat, mat[n])
def crc32_combine(crc1: int, crc2: int, len2: int) -> int:
    if len2 <= 0: return crc1
    even = [0]*32; odd = [0]*32
    odd[0] = 0xEDB88320; row = 1
    for n in range(1, 32): odd[n] = row; row <<= 1
    _gf2_matrix_square(even, odd); _gf2_matrix_square(odd, even)
    while True:
        _gf2_matrix_square(even, odd)
        if len2 & 1: crc1 = _gf2_matrix_times(even, crc1)
        len2 >>= 1
        if len2 == 0: break
        _gf2_matrix_square(odd, even)
        if len2 & 1: crc1 = _gf2_matrix_times(odd, crc1)
        len2 >>= 1
        if len2 == 0: break
    return (crc1 ^ crc2) & 0xFFFFFFFF

class Crc32Combiner:
    """Precomputes the linear operator for crc32_combine(crc1, crc2, len2) for a fixed len2 so that many
    candidate prefixes (headers) can be combined with one payload CRC cheaply: combined = M·crc1 ^ crc2."""
    def __init__(self, len2: int):
        self.len2 = len2
        self.M = [crc32_combine(1 << i, 0, len2) for i in range(32)]   # image of each basis vector
    def combine(self, crc1: int, crc2: int) -> int:
        s = 0; i = 0; v = crc1
        while v:
            if v & 1: s ^= self.M[i]
            v >>= 1; i += 1
        return (s ^ crc2) & 0xFFFFFFFF

# ---------- Format detection from bytes ----------
def detect_format(b: bytes) -> str:
    if len(b) >= 16 and b[:4] == b"NES\x1a":
        return "NES2.0" if (b[7] & 0x0C) == 0x08 else "iNES"
    if len(b) >= 16 and b[:4] == b"FDS\x1a": return "FDS(headered)"
    if len(b) >= 4 and b[:4] == b"UNIF": return "UNIF"
    if len(b) >= 2 and b[0] == 0x01 and b[1:15] == b"*NINTENDO-HVC*"[:14]: return "FDS(raw)"
    if len(b) == 0: return "empty"
    if len(b) % 8192 == 0: return "headerless"
    return "unknown"

# ---------- Databases bundled with hakchi ----------
@dataclass
class Cart:
    game: str; region: str; cls: str; publisher: str; developer: str; date: str; catalog: str
    system: str; crc: str; sha1: str; dump: str; board: str; pcb: str; mapper: Optional[int]
    prg_sizes: list = field(default_factory=list); chr_sizes: list = field(default_factory=list)
    pad_h: Optional[str] = None; pad_v: Optional[str] = None
    vram: list = field(default_factory=list); wram: list = field(default_factory=list)
    battery: bool = False; chips: list = field(default_factory=list); prototype: bool = False; revision: str = ""

def _size(s: str) -> int:
    s = s.strip().lower()
    if s.endswith("k"): return int(float(s[:-1]) * 1024)
    if s.endswith("m"): return int(float(s[:-1]) * 1024 * 1024)
    return int(s)

def load_nescarts():
    root = ET.parse(os.path.join(HAKCHI, "data", "nescarts.xml")).getroot()
    by_sha1, by_crc = {}, {}
    for g in root.findall("game"):
        for c in g.findall("cartridge"):
            b = c.find("board")
            if b is None: continue
            m = b.get("mapper")
            cart = Cart(game=g.get("name",""), region=g.get("region",""), cls=g.get("class",""), publisher=g.get("publisher",""),
                        developer=g.get("developer",""), date=g.get("date",""), catalog=g.get("catalog",""), system=c.get("system",""),
                        crc=(c.get("crc") or "").upper(), sha1=(c.get("sha1") or "").upper(), dump=c.get("dump",""), board=b.get("type",""),
                        pcb=b.get("pcb",""), mapper=int(m) if m not in (None, "") else None,
                        prg_sizes=[_size(p.get("size")) for p in b.findall("prg") if p.get("size")],
                        chr_sizes=[_size(p.get("size")) for p in b.findall("chr") if p.get("size")],
                        vram=[_size(v.get("size")) for v in b.findall("vram") if v.get("size")],
                        wram=[_size(w.get("size")) for w in b.findall("wram") if w.get("size")],
                        battery=any(w.get("battery") == "1" for w in b.findall("wram")) or any(ch.get("battery") == "1" for ch in b.findall("chip")),
                        chips=[ch.get("type","") for ch in b.findall("chip")], prototype=(c.get("prototype") == "1"), revision=c.get("revision",""))
            p = b.find("pad")
            if p is not None: cart.pad_h, cart.pad_v = p.get("h"), p.get("v")
            if cart.sha1: by_sha1.setdefault(cart.sha1, []).append(cart)
            if cart.crc: by_crc.setdefault(cart.crc, []).append(cart)
    return by_sha1, by_crc

def load_romfiles():
    """No-Intro headered .nes entries: name -> CRC32 (of the headered file) and CRC32 -> name."""
    root = ET.parse(os.path.join(HAKCHI, "data", "romfiles.xml")).getroot()
    by_name, by_crc = {}, {}
    for f in root.findall("file"):
        n = f.get("name", "")
        if n.lower().endswith(".nes"):
            c = (f.get("crc32") or "").upper()
            by_name[n] = c; by_crc.setdefault(c, []).append(n)
    return by_name, by_crc

# ---------- iNES header construction ----------
def build_ines_header(prg_bytes: int, chr_bytes: int, mapper: int, vertical: bool, battery: bool, four_screen: bool, trainer: bool = False, nes2: bool = False, submapper: int = 0) -> bytes:
    if prg_bytes % 16384 or chr_bytes % 8192: raise ValueError("PRG/CHR not multiples of 16K/8K")
    prg_units, chr_units = prg_bytes // 16384, chr_bytes // 8192
    if not nes2 and (prg_units > 255 or chr_units > 255 or mapper > 255): nes2 = True
    h = bytearray(16)
    h[0:4] = b"NES\x1a"; h[4] = prg_units & 0xFF; h[5] = chr_units & 0xFF
    h[6] = ((mapper & 0x0F) << 4) | (0x08 if four_screen else 0) | (0x04 if trainer else 0) | (0x02 if battery else 0) | (0x01 if vertical else 0)
    h[7] = (mapper & 0xF0) | (0x08 if nes2 else 0)
    if nes2:
        h[8] = ((submapper & 0x0F) << 4) | ((mapper >> 8) & 0x0F)
        h[9] = ((chr_units >> 8) & 0x0F) << 4 | ((prg_units >> 8) & 0x0F)
    return bytes(h)

# ---------- No-Intro name parsing ----------
TAG_RE = re.compile(r"\s*\(([^()]*)\)")
def parse_nointro(name: str) -> dict:
    base = name[:-4] if name.lower().endswith(".zip") or name.lower().endswith(".nes") else name
    tags = [t.strip() for t in TAG_RE.findall(base)]
    title = TAG_RE.sub("", base).strip()
    regions = []
    for t in tags:
        for part in [x.strip() for x in t.split(",")]:
            if part in REGIONS: regions.append(part)
    rev = next((t for t in tags if re.match(r"^(Rev|v)\s*[0-9A-Za-z.]+$", t)), "")
    flags = [t for t in tags if t not in regions and t != rev]
    return {"title": title, "regions": regions, "rev": rev, "tags": tags, "flags": flags}

REGIONS = {"USA","Europe","Japan","World","Asia","China","Korea","Australia","Canada","Germany","France","Spain","Italy","Netherlands","Sweden","Brazil","Russia","Scandinavia","United Kingdom","Hong Kong","Taiwan","Unknown","Mexico","Argentina","Denmark","Finland","Norway","Poland","Portugal","Greece","Latin America"}
# Tag rules derived from the actual tag inventory of this collection (2026-10-09). Matching is on whole tags
# (case-insensitive, prefix match for numbered variants such as "Beta 2", "Proto 1").
NONRETAIL_PREFIX = ["Proto", "Possible Proto", "Beta", "Demo", "Sample", "Kiosk", "Test Program", "Program", "Competition Cart", "Pirate", "Alt", "Hack", "Translation", "Homebrew", "Aftermarket", "Bootleg", "Debug", "Promo", "Pre-Release", "Tech Demo", "PAL", "PC10", "PlayChoice", "Vs.", "Arcade"]
RERELEASE_PREFIX = ["Virtual Console", "Wii Virtual Console", "Wii U Virtual Console", "3DS Virtual Console", "Switch", "Switch Online", "e-Reader", "Evercade", "Animal Crossing", "Namcot Collection", "Namco Museum", "Castlevania Anniversary Collection", "Contra Anniversary Collection", "Limited Run Games", "Retro-Bit", "Konami Collector's Series", "The Disney Afternoon Collection", "Mega Man Legacy Collection", "Ninja JaJaMaru Retro Collection", "The Cowabunga Collection", "Digital", "Capcom Classics Mini Mix", "Capcom Town", "iam8bit", "QUByte Classics", "GameCube Edition", "Metal Gear Solid Collection", "Red Art Games", "8-bit Adventure Anthology", "Pixel Heart", "RetroZone", "Retro Collection", "SNK 40th Anniversary Collection", "Disney Classic Games", "Piko Interactive", "Broke Studio", "Aladdin Compact Cartridge", "Plug-Thru", "Hamster", "NES Mini", "Classic Mini", "Nintendo Power", "Multicart", "Collection", "Compilation"]
DATE_TAG = re.compile(r"^\d{4}-\d{2}-(\d{2}|xx)$")
BUILD_TAG = re.compile(r"^v\d+\.\d+\.\d{3}$")   # e.g. v3.11.088 — internal build numbers on prototypes
def nonretail_reasons(flags: list) -> list:
    reasons = []
    for fl in flags:
        f = fl.strip(); fl_ = f.lower()
        if DATE_TAG.match(f): reasons.append("date-stamped build"); continue
        if BUILD_TAG.match(f): reasons.append("internal build number"); continue
        if any(fl_ == k.lower() or fl_.startswith(k.lower() + " ") for k in NONRETAIL_PREFIX): reasons.append(f); continue
        if any(fl_ == k.lower() or fl_.startswith(k.lower()) for k in RERELEASE_PREFIX): reasons.append(f"re-release/compilation: {f}"); continue
    return sorted(set(reasons))
def bracket_reasons(name: str) -> list:
    out = []
    for b in re.findall(r"\[([^\[\]]*)\]", name):
        bl = b.lower()
        if bl.startswith("b"): out.append("bad dump [b]")
        elif bl == "bios": out.append("BIOS/firmware")
        else: out.append(f"bracket tag [{b}]")
    return out


def fmt_size(nbytes) -> str:
    """User-facing size in decimal units (1 KB = 1,000 B; 1 MB = 1,000,000 B; 1 GB = 1e9 B) from an exact byte count.
    KB with up to 1 decimal, MB/GB with up to 2; trailing zeros dropped; thousands separators."""
    n = float(nbytes or 0)
    if n >= 1e9: v, u, d = n / 1e9, "GB", 2
    elif n >= 1e6: v, u, d = n / 1e6, "MB", 2
    else: v, u, d = n / 1e3, "KB", 1
    t = f"{v:,.{d}f}"
    if "." in t: t = t.rstrip("0").rstrip(".")
    return f"{t} {u}"

def mib_to_mb(mib) -> float:
    """A binary-unit figure (MiB) kept in the data files for compatibility, converted to decimal MB for display."""
    return float(mib or 0) * 1048576 / 1e6
