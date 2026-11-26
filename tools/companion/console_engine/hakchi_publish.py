"""Classic Cart Studio's collection as ordinary Hakchi games (standard Hakchi publication) and the CCS Collection Interface v1 index, the
PC side. Copyright (C) 2026 Rance9, GPL-3.0-or-later.

What a console gets (written there by the console program tools/console-nucleus/console/collections/ccs-collections, which this module
stages for; ccs_install.py runs it):
  entries   one Hakchi game entry per game of the collection: a folder named by the game's code holding CODE.desktop, CODE.png and
            CODE_small.png (Hakchi's own layout: Apps/NesApplication.cs lines 540-551, 686-691; Apps/DesktopFile.cs lines 390-424), in
            hakchi's game storage (the folder hakchi's own findGameStorage names; the console's menu shows it) or, when the console has none,
            in the console menu's own game folder in hakchi's mod folder. A game file in the collection's installed copy (CCS HOME's, or
            without CCS Mod the games copy that holds only these files: games_only.py) is used
            where it is (the entry's launch line names it: Hakchi's NES launcher reads any file it is given, clover-kachikachi-wr's
            "readlink -f"), so no second copy is made; only a file whose bytes differ from what Hakchi itself would store (a file without its
            header, or a header Hakchi rewrites) gets the Hakchi-ready copy inside its entry, as Hakchi would make it. A built-in game is a
            reference to the console's own installed game (its entry is made on the console from the console's own entry, as Hakchi does
            for its original games: NesApplication.cs lines 1314-1320), never a copy.
  folders   Hakchi folders (Apps/NesMenuFolder.cs): one folder for the collection in the console menu's first page, the collection's own
            folders inside it, each with Hakchi's "Back" entry (NesMenuCollection.cs lines 307-330). Hakchi's folders hold a game once, so
            a game in several of the collection's folders appears in the first of them; the v1 index carries every folder's full list.
            Without hakchi's game storage the console's menu cannot change folders (hakchi's chmenu switches between storage folders), so
            the games are listed directly there.
  index     /var/lib/ccs/integration/v1/index.json: the CCS Collection Interface v1 (schema, version, generation, stable game and
            collection ids, ordered membership, folders, favorites, references to the entries and their art from named bases). Its rules and
            reader are the separately licensed kit in tools/collection-interface-kit.
Stable ids: a game's Hakchi code is Hakchi's own name for that game file (NesApplication.GenerateCode over the CRC-32 of its PRG and CHR
data, prefix H: NesApplication.cs lines 1236-1245, NesGame.cs lines 15 and 41), so a game Hakchi already lists under that code is seen as
present; a folder's code and menu are made from the collection's and folder's own ids. Games Nintendo's built-in NES emulator cannot run
(Hakchi's supported cartridge types: NesGame.cs line 21; four-screen boards: lines 70-81) are left out with the reason, never substituted.
Hakchi2 CE's own game sync replaces everything in its game storage (Tasks/SyncTask.cs lines 293-311): entries this program adds there are
removed by a later sync with Hakchi2 CE, and installing the collection again adds them back."""
from __future__ import annotations
import hashlib, io, json, os, re, zlib

PUB_SCHEMA = "ccs-hakchi-publication/1"; APPLY_SCHEMA = "ccs-hakchi-apply/1"
INDEX_SCHEMA = "ccs-collection-interface"; INDEX_VERSION = 1
INTEGRATION = "/var/lib/ccs/integration"; INDEX_PATH = INTEGRATION + "/v1/index.json"; STAGE_ROOT = INTEGRATION + "/stage"
JOURNAL = "/var/lib/ccs/owned/published.own"   # the console program's own record of what it wrote (lib.sh ccs-ownership/1, method "published")
COLLECTION_ID = "installed"   # the one collection Classic Cart Studio installs on a console at a time
# Hakchi's NES conventions (Hakchi2 CE 3.9.3)
HAKCHI_NES_EXEC = "/bin/clover-kachikachi-wr"   # NesGame.cs line 48: Hakchi's launcher for NES games (the console's own NES emulator)
HAKCHI_NES_ARGS = "--guest-overscan-dimensions 0,0,9,3 --initial-fadein-durations 3,2 --volume 75 --enable-armet"   # NesGame.cs line 17
SUPPORTED_MAPPERS = (0, 1, 2, 3, 4, 5, 7, 9, 10, 86, 87, 184)   # NesGame.cs line 21
GAMES_PATH = "/var/games"; PROFILE_PATH = "/var/saves"   # hakchi.cs lines 349-351 (GamesPath, GamesProfilePath)
CHMENU = "/bin/chmenu"   # NesMenuFolder.cs line 233: a folder entry switches the menu to its storage folder
ROOT_MENU = "000"   # the console menu's first page (hakchi's chmenu and overmount_games)
COVER_BOX = (228, 204); THUMB_BOX = (40, 40)   # NesMenuElementBase.cs lines 236-271
FOLDER_SORT = "Э"; BACK_SORT = "Я"   # NesMenuFolder.cs lines 224-238: the sort prefixes of a folder (Right) and of Back
CODE_RE = re.compile(r"^CLV-[A-Z0-9]-[A-Z0-9]{5}$"); MENU_RE = re.compile(r"^(root|ccs-[0-9a-f]{10})$")
ID_RE = re.compile(r"^[a-z0-9][a-z0-9-]{0,63}$")
REL_RE = re.compile(r"^[A-Za-z0-9_][A-Za-z0-9._-]{0,119}(/[A-Za-z0-9_][A-Za-z0-9._-]{0,119}){0,7}$")
MODES = ("storage", "overlay")
PC_FILE = "PC.json"   # a console package's list of this computer's files (native_home/console_package.py): what Original Menu's covers are made from
INLINE_MAX = 600_000   # a Hakchi-ready copy up to this size is sent within one message to the console (the transport worker's inline limit, worker_console.MAX_B64)
COPY_DIR = "hakchi-copies"   # a larger one: the console package's own copy on this computer (PC.json "hakchi_copies", by the copy's SHA-256), which
                             # the transport worker reads by its place in the package and checks by its SHA-256 before writing it

class PublishError(ValueError): pass

def _sha(b: bytes) -> str: return hashlib.sha256(b).hexdigest()
def _read(p: str) -> bytes:
    with open(p, "rb") as f: return f.read()
def _json(p: str):
    try: return json.loads(_read(p))
    except (OSError, ValueError): return None

# ---------------------------------------------------------------- a game file as Hakchi sees it
def nes_rom(data: bytes, header_hex: str = "") -> dict:
    """the game file as Hakchi would store it (Apps/NesFile.cs: its header read, then GetRaw lines 88-124) and whether Nintendo's built-in NES
    emulator runs it: {"ok", "why" (plain words when not), "mapper", "battery", "crc32" (PRG and CHR, NesFile.cs CRC32), "ready" (the bytes
    Hakchi would store), "same" (the file is already exactly those bytes)}. header_hex: the 16-byte header of a file kept without one (the
    package's session gives it)"""
    full = (bytes.fromhex(header_hex) + data) if header_hex else data
    if len(full) < 16 or full[:4] != b"NES\x1a": return {"ok": False, "why": "its file is not a complete NES game file"}
    h = full[:16]; nes2 = (h[7] & 0x0C) == 0x08; archaic = not nes2 and any(h[12:16])   # NesFile.cs lines 37-41: an archaic header's bytes 7-15 are ignored
    if nes2:
        if h[9] or h[8] & 0x0F: return {"ok": False, "why": "Nintendo's built-in NES emulator, which Hakchi games use, does not run its cartridge type"}
        mapper = (h[6] >> 4) | (h[7] & 0xF0); pal = (h[12] & 3) == 1
    else: mapper = (h[6] >> 4) | (0 if archaic else h[7] & 0xF0); pal = not archaic and bool(h[9] & 1)
    vs = not archaic and bool(h[7] & 1); pc10 = not archaic and bool(h[7] & 2)
    battery = bool(h[6] & 2); trainer = bool(h[6] & 4); four = bool(h[6] & 8); vertical = bool(h[6] & 1)
    prg, chr_ = h[4] * 16384, h[5] * 8192; off = 16 + (512 if trainer else 0)
    if prg == 0 or len(full) < off + prg + chr_: return {"ok": False, "why": "its file is not a complete NES game file"}
    p, c = full[off:off + prg], full[off + prg:off + prg + chr_]; crc = zlib.crc32(p + c) & 0xFFFFFFFF
    base = {"mapper": mapper, "battery": battery, "crc32": crc}
    if mapper not in SUPPORTED_MAPPERS or four:
        return {**base, "ok": False, "why": "Nintendo's built-in NES emulator, which Hakchi games use, does not run its cartridge type" + (f" (mapper {mapper})" if not four else " (four-screen)")}
    if trainer: return {**base, "ok": False, "why": "its file carries a trainer, which Hakchi does not keep in place"}
    hd = bytearray(16); hd[0:4] = b"NES\x1a"; hd[4], hd[5] = h[4], h[5]
    hd[6] = (1 if vertical else 0) | (2 if battery else 0) | ((mapper << 4) & 0xF0)
    hd[7] = (1 if vs else 0) | (2 if pc10 else 0) | (mapper & 0xF0); hd[9] = 1 if pal else 0
    ready = bytes(hd) + p + c
    return {**base, "ok": True, "why": "", "ready": ready, "same": ready == data}

def hakchi_code(crc32: int, prefix: str = "H") -> str:
    """NesApplication.GenerateCode (NesApplication.cs lines 1236-1245): CLV-<prefix>-<five letters from the CRC-32>"""
    return "CLV-%s-%s" % (prefix, "".join(chr(ord("A") + ((crc32 >> s) % 26)) for s in (0, 5, 10, 15, 20)))

def letters_code(seed: str, prefix: str) -> str:
    """a code in Hakchi's form made from a stable seed (folders: prefix S, as Hakchi's folders; a game whose Hakchi code another game of the
    collection already has: prefix H)"""
    return hakchi_code(int(_sha(seed.encode())[:8], 16), prefix)

def plain_name(s, fallback: str = "Game") -> str:
    """one line of printable text, at most 100 characters"""
    return re.sub(r"\s+", " ", re.sub(r"[\x00-\x1f\x7f]", " ", str(s or ""))).strip()[:100] or fallback

def clean_name(s, fallback: str = "Game") -> str:
    """a name as a Hakchi entry can carry it: plain_name, and an apostrophe before a digit becomes a back quote (NesApplication.cs lines
    650-652: the console's menu stops on it)"""
    return re.sub(r"'(\d)", r"`\1", plain_name(s, fallback))

def sort_title(name: str) -> str:
    """NesApplication.Save (lines 653-659): the name in lower case, a leading "the " left out"""
    s = name.lower(); return s[4:] if s.startswith("the ") else s

# ---------------------------------------------------------------- entry files (Hakchi's DesktopFile.GetConfig, lines 390-424)
def _desktop(exec_: str, path: str, name: str, code: str, test_id: int, players: int, simultaneous: bool, release: str, save_count: int, sort: str, publisher: str) -> bytes:
    return ("[Desktop Entry]\nType=Application\n" f"Exec={exec_}\nPath={path}\nName={name}\nCePrefix=\nIcon={GAMES_PATH}/{code}/{code}.png\n\n"
            "[X-CLOVER Game]\n" f"Code={code}\nTestID={test_id}\nID=0\nPlayers={players}\nSimultaneous={1 if simultaneous else 0}\nReleaseDate={release}\n"
            f"SaveCount={save_count}\nSortRawTitle={sort}\nSortRawPublisher={publisher.upper()}\nCopyright=\n\n"
            "[m2engage]\nregionTag=\nsortRawGenre=\nindex=\ndemo_time=\ncountry=\n\n[Description]\nText = \n").encode("utf-8")

def desktop_game(g: dict, rom_path: str) -> bytes:
    """a game's entry: Hakchi's NES launcher with the game file and Hakchi's arguments (NesApplication.cs line 546), its saves in Hakchi's
    profile folder (Path=/var/saves/CODE: DesktopFile.cs line 396)"""
    return _desktop(f"{HAKCHI_NES_EXEC} {rom_path} {HAKCHI_NES_ARGS}", f"{PROFILE_PATH}/{g['code']}", g["name"], g["code"], 0, g["players"], g["players"] > 1,
                    g["release"], g["save_count"], sort_title(g["name"]), g["publisher"])

def desktop_folder(code: str, name: str, menu: str, back: bool = False) -> bytes:
    """a Hakchi folder entry (NesMenuFolder.getAdjustedDesktopFile, lines 220-239, saved without the code in its profile path): it switches
    the console's menu to the folder's storage folder (chmenu). A new folder sorts to the right (Priority.Right); Back after it"""
    name = clean_name(name, "Folder")
    if back: return _desktop(f"{CHMENU} {menu} {GAMES_PATH}", f"{PROFILE_PATH}/FOLDER", name, code, 777, 1, False, "9999-99-99", 0, BACK_SORT + name.lower(), "Z" * 10)
    return _desktop(f"{CHMENU} {menu} {GAMES_PATH}", f"{PROFILE_PATH}/FOLDER", name, code, 777, 2, True, "0000-00-00", 0, FOLDER_SORT + name.lower(), "!" * 10)

# ---------------------------------------------------------------- pictures (PNG, as Hakchi stores them)
def _fit(w: int, h: int, tw: int, th: int, upscale: bool) -> tuple[int, int]:
    """Shared.ResizeImage with keepProportions (Shared.cs lines 41-66): even sizes, banker's rounding as .NET's Math.Round"""
    if not upscale and w <= tw and h <= th: return w, h
    if w / h > tw / th:
        y = round(tw * h / w); return tw, y + (y % 2)
    x = round(th * w / h); return x + (x % 2), th

# Original Menu's pictures. "standard" (what this version uses): each picture as a PNG of its own colors. "palette": the smaller
# pictures Hakchi2 CE makes by default (NesMenuElementBase.ProcessImage with CompressCover: nQuant's Wu quantizer, wu_quantize.py) as
# an 8-bit indexed PNG with a full 256-color palette and its transparency, the mode Hakchi2 CE writes; the same sizes. Built and tested
# here, but not used: Original Menu keeps the standard pictures until these are compared on the console's own menu and approved.
NATIVE_COVER_VARIANTS = ("standard", "palette"); NATIVE_COVERS = "standard"

def _png(im, variant: str = "standard") -> bytes:
    if variant == "palette": return _palette_png(im)
    out = io.BytesIO(); im.save(out, "PNG", optimize=True); return out.getvalue()

def _palette_png(im) -> bytes:
    """the picture as Hakchi2 CE's 8-bit palette PNG: its colors from nQuant's Wu quantizer (as Hakchi2 CE makes them), a palette of
    256 entries (the unused ones black and opaque) with each entry's transparency, compressed as small as Pillow makes it; checked to
    decode to exactly the quantizer's colors"""
    from PIL import Image
    from . import wu_quantize as WQ
    rgba = im.convert("RGBA"); pal, idx = WQ.quantize(rgba.tobytes(), rgba.width, rgba.height)
    if len(pal) > 256: raise ValueError("more than 256 colors")
    out = Image.frombytes("P", rgba.size, idx); full = pal + [(0, 0, 0, 255)] * (256 - len(pal))
    out.putpalette(bytes(c for e in full for c in e[:3]), rawmode="RGB")
    b = io.BytesIO(); out.save(b, "PNG", optimize=True, transparency=bytes(e[3] for e in full)); data = b.getvalue()
    back = Image.open(io.BytesIO(data)); back.load()
    want = b"".join(bytes(pal[i]) for i in idx)
    if back.mode != "P" or back.convert("RGBA").tobytes() != want: raise ValueError("the palette picture does not decode to its colors")
    return data

def art_pngs(src: bytes | None, kind: str = "game", variant: str | None = None) -> tuple[bytes, bytes]:
    """(CODE.png, CODE_small.png): a game's cover fitted into 228 x 204 (enlarged when smaller, as Hakchi's SetImage) and into 40 x 40;
    without a cover, or for a folder or Back, a plain picture drawn here (no text, no other maker's artwork). variant: NATIVE_COVERS
    (what this version uses) unless a test or a comparison asks for another of NATIVE_COVER_VARIANTS"""
    from PIL import Image, ImageDraw
    variant = variant or NATIVE_COVERS
    if variant not in NATIVE_COVER_VARIANTS: raise ValueError(f"unknown picture variant {variant!r}")
    if src:
        try: im = Image.open(io.BytesIO(src)).convert("RGB")
        except Exception: im = None
        if im is not None:
            big = im.resize(_fit(im.width, im.height, *COVER_BOX, True), Image.Resampling.BICUBIC)
            small = im.resize(_fit(im.width, im.height, *THUMB_BOX, False), Image.Resampling.BICUBIC)
            return _png(big, variant), _png(small, variant)
    im = Image.new("RGBA", (160, 204), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    if kind == "folder":
        d.rounded_rectangle((8, 40, 152, 180), 14, fill=(228, 32, 53, 255)); d.rounded_rectangle((8, 24, 76, 56), 10, fill=(176, 20, 38, 255))
        d.rounded_rectangle((8, 52, 152, 180), 14, fill=(228, 32, 53, 255))
    elif kind == "back":
        d.rounded_rectangle((8, 40, 152, 180), 14, fill=(52, 52, 60, 255)); d.polygon([(36, 110), (84, 66), (84, 94), (126, 94), (126, 126), (84, 126), (84, 154)], fill=(232, 232, 238, 255))
    else:
        d.rounded_rectangle((12, 8, 148, 196), 10, fill=(70, 70, 80, 255)); d.rectangle((30, 30, 130, 120), fill=(110, 110, 124, 255))
    small = im.resize(_fit(160, 204, *THUMB_BOX, False), Image.Resampling.BICUBIC)
    return _png(im, variant), _png(small, variant)

# ---------------------------------------------------------------- the plan-time projection (reads the package only)
def _players(v) -> int:
    n = [int(x) for x in re.findall(r"\d+", str(v or ""))]; return max(1, min(4, max(n))) if n else 1

def _release(v) -> str:
    m = re.match(r"^(\d{4})(?:-(\d{2}))?(?:-(\d{2}))?", str(v or "").strip())
    return f"{m.group(1)}-{m.group(2) or '01'}-{m.group(3) or '01'}" if m else "1900-01-01"   # NesGame.TryAutofill / DesktopFile's default

def _menu_id(*parts: str) -> str: return "ccs-" + _sha(("/".join(parts)).encode())[:10]

def spec(package_dir: str, files: dict | None = None) -> dict:
    """the collection's Hakchi projection, from its console package (MANIFEST.json, session.json, home/games.json, home/config.json):
    every game with its interface id, Hakchi code, game file (used where it is, or the Hakchi-ready copy), pictures (made from the picture
    PC.json names for the game, this computer's own copy of its cover as CCS HOME carried it before console covers became smaller; a
    package without PC.json: CCS HOME's cover) and why it is left out
    when it is; the collection's folders in their order and nesting, each game placed in the first folder that has it; favorites. files:
    the package's {path: SHA-256} (build_plan's checked list), so only listed files are read. Deterministic: the same package always gives
    the same projection"""
    def rd(rel):
        if files is not None and rel not in files: return None
        p = os.path.join(package_dir, *rel.split("/"))
        try: b = _read(p)
        except OSError: return None
        if files is not None and _sha(b) != files[rel]: raise PublishError(f"{rel} differs from the package's manifest")
        return b
    man = json.loads(rd("MANIFEST.json") or b"{}"); sess = json.loads(rd("session.json") or b"{}")
    gdoc = json.loads(rd("home/games.json") or b"{}"); cfg = json.loads(rd("home/config.json") or b"{}")
    meta = {str(g.get("id")): g for g in gdoc.get("games") or [] if isinstance(g, dict)}
    native, copies = {}, {}   # the pictures Original Menu's covers are made from and the Hakchi-ready copies too large for one message (console_package.py PC.json, this computer's only)
    if man.get("pc_sha256"):
        pcb = rd(PC_FILE)
        if pcb is None or _sha(pcb) != man["pc_sha256"]: raise PublishError("the package's list of the pictures for Original Menu's covers is missing or changed")
        pcd = json.loads(pcb); native = (pcd.get("native_covers") or {}); copies = pcd.get("hakchi_copies") or {}
        if not isinstance(copies, dict): copies = {}
    order = [str(x) for x in cfg.get("games") or []] or [str(g.get("id")) for g in sess.get("games") or [] if isinstance(g, dict)]
    sgames = {str(g.get("id")): g for g in sess.get("games") or [] if isinstance(g, dict)}
    pkg_prefix = ((man.get("console_paths") or {}).get("pkg") or "/tmp/ccs/pkg").rstrip("/") + "/"
    games, used_codes, by_ccs = [], {}, {}
    for vid in dict.fromkeys(order):
        m = meta.get(vid) or {}; s = sgames.get(vid) or {}
        raw = m.get("title") or s.get("title") or vid; title = plain_name(raw)
        g = {"ccs_id": vid, "title": title, "name": clean_name(raw), "players": _players(m.get("players")), "release": _release(m.get("year")), "publisher": clean_name(m.get("publisher") or "UNKNOWN", "UNKNOWN"),
             "save_count": 0, "status": "publish", "reason": ""}
        code = str(m.get("builtin_code") or "")
        if code and m.get("home_kind") == "factory":   # the console's own installed game: a reference, never a copy
            if not CODE_RE.match(code): continue
            g.update(id="nes-builtin-" + code.lower(), kind="builtin", code=code)
        else:
            cs = str(s.get("content_sha256") or ""); rom = str(s.get("rom") or "")
            rel = rom[len(pkg_prefix):] if rom.startswith(pkg_prefix) else ""
            if not re.match(r"^[0-9a-f]{64}$", cs): g.update(id="nes-" + _sha(vid.encode())[:32], kind="file", status="skip", reason=str(s.get("unavailable") or "its game file is not in the console package"), code="")
            else:
                g.update(id="nes-" + cs[:32], kind="file", content_sha256=cs)
                data = rd(rel) if REL_RE.match(rel or "") and rel.startswith("roms/") else None
                if data is None: g.update(status="skip", reason="its game file is not in the console package", code="")
                else:
                    try: r = nes_rom(data, str(s.get("header_hex") or ""))
                    except ValueError: r = {"ok": False, "why": "its file is not a complete NES game file"}
                    src = None
                    if r.get("ok") and not r["same"] and len(r["ready"]) > INLINE_MAX:   # sent from the package's own copy: only when it is exactly these bytes
                        src = copies.get(_sha(r["ready"]))
                        cb = rd(src) if isinstance(src, str) and REL_RE.match(src) and src.startswith(COPY_DIR + "/") else None
                        if cb != r["ready"]: r = {"ok": False, "why": "the copy Hakchi needs of its file is not in this console package (prepare it again)"}
                    if not r.get("ok"): g.update(status="skip", reason=r["why"], code="")
                    else:
                        g.update(code=hakchi_code(r["crc32"]), rom=rel, rom_sha256=_sha(data), copy=None if r["same"] else r["ready"], save_count=3 if r["battery"] else 0)   # NesGame.cs lines 88-89
                        if src: g["copy_source"] = src
        if g["id"] in by_ccs: continue   # one entry per game (the same game file listed twice)
        by_ccs[g["id"]] = g; games.append(g)
    # a Hakchi code another game of the collection already has: a code made from the game's own id instead (deterministic, in the
    # collection's order); a built-in game keeps the console's own code
    for g in games:
        if g["status"] != "publish": continue
        c, k = g["code"], 0
        while c in used_codes and used_codes[c] != g["id"]:
            if g["kind"] == "builtin": g.update(status="skip", reason="another game of the collection has its code"); break
            k += 1; c = letters_code(f"{g['id']}/{k}", "H")
        if g["status"] == "publish": g["code"] = c; used_codes[c] = g["id"]
    for g in games:
        if g["status"] == "publish" and g["kind"] == "file":
            cover = None
            mrec = meta.get(g["ccs_id"]) or {}
            nrel = native.get(g["ccs_id"])
            if isinstance(nrel, str):   # the picture Original Menu's cover is made from (console_package.py native-covers: this computer's only)
                cover = rd(nrel) if REL_RE.match(nrel) else None
                if cover is None: raise PublishError(f"the picture for {g['name']}'s Original Menu cover is not in the package")
            elif isinstance(mrec.get("cover"), str) and REL_RE.match(mrec["cover"]): cover = rd("home/" + mrec["cover"])
            big, small = art_pngs(cover)
            g["art"] = {"png": big, "small": small}
            if g.get("copy") is not None: g["copy_sha256"] = _sha(g["copy"])
    ids = {g["ccs_id"]: g["id"] for g in games}
    # the collection's folders, in the order and nesting CCS HOME browses them (native_home/package.py browse_index)
    flist = [f for f in cfg.get("folders") or [] if isinstance(f, dict) and f.get("id")]; fids = {str(f["id"]) for f in flist}
    def kids(pid): return sorted([f for f in flist if (str(f.get("parent")) if f.get("parent") in fids else None) == pid], key=lambda f: f.get("pos") or 0)
    ordered = []
    def walk(pid, depth=0):
        for f in kids(pid):
            if f in ordered or depth > 6: continue
            ordered.append(f); walk(str(f["id"]), depth + 1)
    walk(None)
    folders = []
    for f in ordered:
        fid = "f-" + _sha(str(f["id"]).encode())[:16]; parent = next((x["id"] for x in folders if x["ccs_id"] == str(f.get("parent"))), None)
        folders.append({"id": fid, "ccs_id": str(f["id"]), "name": plain_name(f.get("name"), "Folder"), "parent": parent,
                        "games": list(dict.fromkeys(ids[x] for x in f.get("order") or [] if x in ids)), "menu": _menu_id(COLLECTION_ID, str(f["id"]))})
    # each published game in the first folder (browse order) that lists it, otherwise in the collection's own folder
    place = {}
    for f in folders:
        for gid in f["games"]: place.setdefault(gid, f["id"])
    top = _menu_id(COLLECTION_ID); fmenu = {f["id"]: f["menu"] for f in folders}
    for g in games:
        if g["status"] == "publish": g["menu"] = fmenu.get(place.get(g["id"]), top)
    used = set(used_codes)
    for f in [{"id": None, "menu": top}] + folders:   # each folder's own code (Hakchi's folders: prefix S)
        c, k = letters_code(f["menu"], "S"), 0
        while c in used: k += 1; c = letters_code(f"{f['menu']}/{k}", "S")
        used.add(c); f["code"] = c
        if f["id"] is None: top_code = c
    favs = [ids[x] for x in cfg.get("favorites") or [] if x in ids]
    name = plain_name(man.get("label") or (cfg.get("collection") or {}).get("name") or "Classic Cart Studio", "Classic Cart Studio")
    return {"collection": {"id": COLLECTION_ID, "name": name, "menu": top, "code": top_code}, "games": games, "folders": folders, "favorites": favs,
            "order": [g["id"] for g in games]}

def summary(sp: dict) -> dict:
    """the projection without picture or file bytes (what a plan carries and the installer compares: the same package gives the same one)"""
    gs = [{k: v for k, v in g.items() if k not in ("art", "copy")} | ({"art_sha256": [_sha(g["art"]["png"]), _sha(g["art"]["small"])]} if g.get("art") else {}) for g in sp["games"]]
    return {"collection": sp["collection"], "games": gs, "folders": sp["folders"], "favorites": sp["favorites"], "order": sp["order"]}

def review(sp: dict) -> dict:
    """the plan's review of the projection, in plain words: {"games", "builtin", "left_out", "folders", "text", "notes"}"""
    pub = [g for g in sp["games"] if g["status"] == "publish"]; out = [g for g in sp["games"] if g["status"] != "publish"]
    n = len(pub); name = sp["collection"]["name"]
    if not n: text = "None of this collection's games can be added to Original Menu as Hakchi games."
    else: text = ("Adds this collection's game" if n == 1 else f"Adds this collection's {n} games") + f' to Original Menu as Hakchi games, in a folder named "{name}".'
    notes = []
    if n:
        if sp["folders"]: notes.append("Hakchi's folders show each game once: a game in several of your folders appears in the first one. Classic Cart Studio keeps the full lists for other menus.")
        notes.append("If your console's menu has no Hakchi game folders yet, the games appear directly in Original Menu instead of in a folder.")
        notes.append("If you later sync games with Hakchi2 CE, it replaces everything in its game folder, these games included. Install this collection again to add them back.")
    if out: notes.append(f"{len(out)} game{'' if len(out) == 1 else 's'} cannot be added as Hakchi games: " + "; ".join(f"{g['title']} ({g['reason']})" for g in out[:5]) + ("; and more" if len(out) > 5 else "") + ".")
    return {"games": n, "builtin": sum(1 for g in pub if g["kind"] == "builtin"), "left_out": [{"title": g["title"], "why": g["reason"]} for g in out], "folders": len(sp["folders"]), "text": text, "notes": notes}

# ---------------------------------------------------------------- the console-time layout (the mode the console program reports)
COPY_ROOTS = ("/var/lib/ccs/pkg", "/var/lib/ccs/games")   # CCS HOME's package copies, and the games copies made without CCS Mod (games_only.py)

def entry_names(code: str) -> tuple:
    """the only files a game, folder or Back entry has: Hakchi's own form. Classic Cart Studio never writes Hakchi2 CE's picture for the Sega
    Mini menu (CODE_mdmini.png) or any other file in an entry, and has no setting to include one."""
    return (f"{code}.desktop", f"{code}.png", f"{code}_small.png", f"{code}.nes")

def _entries_checked(out: dict) -> dict:
    for e in out["entries"]:
        extra = [n for n in e["files"] if n not in entry_names(e["code"])]
        if extra: raise PublishError("an entry would hold a file that is not Hakchi's own form: " + ", ".join(sorted(extra)))
    return out

def layout(sp: dict, mode: str, copy: str, root: str = COPY_ROOTS[0]) -> dict:
    """the entries for one console: mode "storage" (hakchi's game storage: the collection's folder in the first page, its folders inside,
    each with Back) or "overlay" (no game storage: the games directly in the console menu's own game folder; a built-in game is already
    there). copy: the installed copy whose game files the entries name ("<version>.<attempt>" under root: CCS HOME's package copies, or the
    games copies without CCS Mod, which hold the same roms/ files). Returns {"mode", "menus": [(menu, parent)], "entries": [{"kind", "menu",
    "code", "files": {name: bytes}, "game"|"folder"}]}"""
    if mode not in MODES: raise PublishError(f"unknown mode {mode!r}")
    if not re.match(r"^[0-9a-f]{16}\.[0-9a-f]{8}$", copy or ""): raise PublishError("not a package copy name")
    if root not in COPY_ROOTS: raise PublishError("not a place for copies")
    base = f"{root}/{copy}"; entries, menus = [], []
    def game_entry(g, menu):
        code = g["code"]
        if g["kind"] == "builtin": return {"kind": "builtin", "menu": menu, "code": code, "files": {}, "game": g["id"]}
        files = {f"{code}.png": g["art"]["png"], f"{code}_small.png": g["art"]["small"]}
        if g.get("copy") is not None: files[f"{code}.nes"] = g["copy"]; rom = f"{GAMES_PATH}/{code}/{code}.nes"   # Hakchi's own place (NesApplication.cs line 546)
        else: rom = f"{base}/{g['rom']}"
        files[f"{code}.desktop"] = desktop_game(g, rom)
        return {"kind": "game", "menu": menu, "code": code, "files": files, "game": g["id"], **({"sources": {f"{code}.nes": g["copy_source"]}} if g.get("copy") is not None and g.get("copy_source") else {})}
    pub = [g for g in sp["games"] if g["status"] == "publish"]
    if mode == "overlay":
        for g in pub:
            if g["kind"] == "file": entries.append(game_entry(g, "root"))
        return _entries_checked({"mode": mode, "menus": [], "entries": entries})
    col = sp["collection"]; top = col["menu"]; fmenu = {f["id"]: f for f in sp["folders"]}
    menus.append((top, "root"))
    big, small = art_pngs(None, "folder"); bbig, bsmall = art_pngs(None, "back")
    def folder_entry(code, name, menu_in, target):
        return {"kind": "folder", "menu": menu_in, "code": code, "files": {f"{code}.desktop": desktop_folder(code, name, target), f"{code}.png": big, f"{code}_small.png": small}}
    def back_entry(menu_in, parent, parent_code):
        target = ROOT_MENU if parent == "root" else parent; code = parent_code
        return {"kind": "back", "menu": menu_in, "code": code, "files": {f"{code}.desktop": desktop_folder(code, "Back", target, back=True), f"{code}.png": bbig, f"{code}_small.png": bsmall}}
    entries.append(folder_entry(col["code"], col["name"], "root", top) | {"folder": None})
    entries.append(back_entry(top, "root", "CLV-S-00000"))   # Hakchi's Back to the first page: its root menu's folder code (NesMenuFolder.ChildIndex 0)
    for f in sp["folders"]:
        pm = fmenu[f["parent"]]["menu"] if f["parent"] else top; pcode = fmenu[f["parent"]]["code"] if f["parent"] else col["code"]
        menus.append((f["menu"], pm))
        entries.append(folder_entry(f["code"], f["name"], pm, f["menu"]) | {"folder": f["id"]})
        entries.append(back_entry(f["menu"], pm, pcode))
    for g in pub: entries.append(game_entry(g, g["menu"]))
    # a folder that would hold nothing but Back is not made (hakchi's chmenu opens only a storage folder that holds games: b0010_functions
    # containsGames; Hakchi leaves such folders out too: Tasks/SyncTask.cs line 691)
    while True:
        holds = {}
        for e in entries:
            if e["kind"] != "back": holds[e["menu"]] = holds.get(e["menu"], 0) + 1
        empty = [m for m, _ in menus if not holds.get(m)]
        if not empty: break
        menus = [(m, p) for m, p in menus if m not in empty]
        entries = [e for e in entries if e["menu"] not in empty and not (e["kind"] == "folder" and _target(e) in empty)]
    return _entries_checked({"mode": mode, "menus": menus, "entries": entries})

def _target(e: dict) -> str:
    t = e["files"][f"{e['code']}.desktop"].decode().split("\n")[2]; return t.split(" ")[1]

def stage_files(lay: dict) -> dict[str, bytes]:
    """the stage folder's files (relative to it): files/<menu>/<code>/<name> for every entry file, and "publication", the sealed list the
    console program reads (schema, mode, menus, entries, files)"""
    out, lines = {}, [f"schema={PUB_SCHEMA}", f"mode={lay['mode']}"]
    for m, p in lay["menus"]: lines.append(f"menu={m} {p}")
    for e in lay["entries"]:
        lines.append(f"entry={e['kind']} {e['menu']} {e['code']}")
        for n, b in sorted(e["files"].items()):
            out[f"files/{e['menu']}/{e['code']}/{n}"] = b; lines.append(f"file={_sha(b)} {e['menu']} {e['code']} {n}")
    out["publication"] = sealed(lines); return out

def stage_sources(lay: dict) -> dict[str, str]:
    """the stage files sent from the console package's own file on this computer instead of within a message (a Hakchi-ready copy larger
    than INLINE_MAX): {place in the stage folder: path in the package}"""
    return {f"files/{e['menu']}/{e['code']}/{n}": s for e in lay["entries"] for n, s in sorted((e.get("sources") or {}).items())}

def sealed(lines: list[str]) -> bytes:
    """a sealed record (lib.sh ccs_sealed): its lines, then end=<SHA-256 of every line before>"""
    body = "".join(l + "\n" for l in lines).encode(); return body + f"end={_sha(body)}\n".encode()

def apply_file(skip: list[str], index_sha: str, previous: str | None, generation: int) -> bytes:
    return sealed([f"schema={APPLY_SCHEMA}", f"generation={generation}", f"index={index_sha}", f"previous={previous or 'none'}"] + [f"skip={s}" for s in sorted(skip)])

# ---------------------------------------------------------------- the CCS Collection Interface v1 index
def index_doc(sp: dict, lay: dict | None, where: dict | None, states: dict | None, generation: int, producer: dict | None = None) -> dict:
    """the v1 index for what the console holds after the apply: states {"<menu>/<code>": free|ours|changed|foreign|absent} from the console
    program's check (an entry that is not free or ours is left out of the apply and its game is listed as not published, with why). lay
    None: no entries (the collection is not published as Hakchi games)"""
    ok = {"free", "ours"}; entry_of = {}
    bases = {}
    if lay is not None and where:
        bases["entries"] = where["store"] if lay["mode"] == "storage" else where["menu"]
        if where.get("system"): bases["system"] = where["system"]
    def ref_dir(menu: str) -> str:
        if lay["mode"] == "overlay": return ""
        return (where["menu"][len(where["store"]):].lstrip("/") + "/") if menu == "root" and where["menu"] != where["store"] else ("" if menu == "root" else menu + "/")
    if lay is not None:
        for e in lay["entries"]:
            st = (states or {}).get(f"{e['menu']}/{e['code']}", "free")
            if st in ok: entry_of[(e["kind"], e.get("game") or e.get("folder"))] = (e, ref_dir(e["menu"]) + e["code"])
    games = {}
    for g in sp["games"]:
        rec = {"title": g["title"], "system": "nes", "kind": g["kind"]}
        if g.get("content_sha256"): rec["content_sha256"] = g["content_sha256"]
        hit = entry_of.get(("builtin" if g["kind"] == "builtin" else "game", g["id"])) if g["status"] == "publish" else None
        if hit:
            e, d = hit; rec["status"] = "published"
            rec["entry"] = {"base": "entries", "path": f"{d}/{e['code']}.desktop", "code": e["code"]}
            if g["kind"] == "builtin": rec["art"] = {"cover": {"base": "system", "path": f"{e['code']}/{e['code']}.png"}}
            else: rec["art"] = {"cover": {"base": "entries", "path": f"{d}/{e['code']}.png"}, "thumbnail": {"base": "entries", "path": f"{d}/{e['code']}_small.png"}}
        else:
            rec["status"] = "not-published"
            rec["reason"] = g["reason"] if g["status"] != "publish" else ("not added as a Hakchi game" if lay is None else
                            "already in Original Menu" if lay["mode"] == "overlay" and g["kind"] == "builtin" else
                            "the console's game for it is not there" if (states or {}).get(f"{g.get('menu')}/{g['code']}") == "absent" else
                            "an entry with its code is already in Hakchi's game folder and is kept")
        games[g["id"]] = rec
    col = {"id": sp["collection"]["id"], "name": sp["collection"]["name"], "games": list(sp["order"])}
    folders = []
    for f in sp["folders"]:
        fr = {"id": f["id"], "name": f["name"], "parent": f["parent"], "games": f["games"]}
        hit = entry_of.get(("folder", f["id"]))
        if hit: fr["entry"] = {"base": "entries", "path": f"{hit[1]}/{hit[0]['code']}.desktop", "code": hit[0]["code"]}
        folders.append(fr)
    col["folders"] = folders
    if sp["favorites"]: col["favorites"] = sp["favorites"]
    hit = entry_of.get(("folder", None))
    if hit: col["entry"] = {"base": "entries", "path": f"{hit[1]}/{hit[0]['code']}.desktop", "code": hit[0]["code"]}
    caps = ["hakchi-entries"] + (["folders"] if lay is not None and lay["mode"] == "storage" else []) + (["favorites"] if sp["favorites"] else [])
    doc = {"schema": INDEX_SCHEMA, "version": INDEX_VERSION, "generation": int(generation), "producer": producer or {"name": "Classic Cart Studio"},
           "bases": bases, "capabilities": caps, "collections": [col], "games": games}
    problems = check_index(doc)
    if problems: raise PublishError("the index would not be valid: " + "; ".join(problems[:3]))
    return doc

def index_bytes(doc: dict) -> bytes: return (json.dumps(doc, indent=1, ensure_ascii=False) + "\n").encode("utf-8")

def check_index(doc) -> list[str]:
    """what this program requires of an index it writes (the kit's conformance checker is the public definition): v1's mandatory fields,
    ids, references from named bases with plain relative paths, membership naming listed games, folders naming listed parents"""
    p = []
    if not isinstance(doc, dict): return ["not an object"]
    if doc.get("schema") != INDEX_SCHEMA or doc.get("version") != INDEX_VERSION: p.append("schema/version")
    if not isinstance(doc.get("generation"), int) or isinstance(doc.get("generation"), bool) or doc["generation"] < 1: p.append("generation")
    bases = doc.get("bases")
    if not isinstance(bases, dict) or any(not ID_RE.match(k) or not isinstance(v, str) or not _abs_ok(v) for k, v in bases.items()): p.append("bases")
    games = doc.get("games") if isinstance(doc.get("games"), dict) else {}
    if not isinstance(doc.get("games"), dict): p.append("games")
    def ref(r, what):
        if not isinstance(r, dict) or r.get("base") not in (bases or {}) or not isinstance(r.get("path"), str) or not REL_RE.match(r["path"]) or ".." in r["path"].split("/"): p.append(f"{what}: reference")
    for gid, g in games.items():
        if not ID_RE.match(gid) or not isinstance(g, dict) or not isinstance(g.get("title"), str): p.append(f"game {gid}"); continue
        if g.get("entry") is not None: ref(g["entry"], f"game {gid} entry")
        for k, r in ((g.get("art") or {}).items() if isinstance(g.get("art"), dict) else []): ref(r, f"game {gid} {k}")
    cols = doc.get("collections")
    if not isinstance(cols, list): p.append("collections"); cols = []
    for c in cols:
        if not isinstance(c, dict) or not ID_RE.match(str(c.get("id"))) or not isinstance(c.get("games"), list): p.append("collection"); continue
        if any(x not in games for x in c["games"]) or len(set(c["games"])) != len(c["games"]): p.append(f"collection {c['id']}: games")
        seen = set()
        for f in c.get("folders") or []:
            if not isinstance(f, dict) or not ID_RE.match(str(f.get("id"))) or f.get("parent") not in seen | {None} or any(x not in games for x in f.get("games") or []): p.append(f"collection {c['id']}: folder"); continue
            if f.get("entry") is not None: ref(f["entry"], f"folder {f['id']} entry")
            seen.add(f["id"])
        if any(x not in games for x in c.get("favorites") or []): p.append(f"collection {c['id']}: favorites")
        if c.get("entry") is not None: ref(c["entry"], f"collection {c['id']} entry")
    return p

def _abs_ok(v: str) -> bool:
    return v.startswith("/") and len(v) <= 200 and re.match(r"^(/[A-Za-z0-9_][A-Za-z0-9._-]*)+$", v) is not None and ".." not in v.split("/")

def parse_index(b: bytes | None) -> dict | None:
    """an index as the console holds it: the document when it is a v1 index this program would write, else None"""
    if b is None: return None
    try: d = json.loads(b.decode("utf-8"))
    except (ValueError, UnicodeDecodeError): return None
    return d if not check_index(d) else None

# ---------------------------------------------------------------- the console program (staged by the installer, pinned by the worker)
def _nucleus_dir() -> str:
    import sys
    for d in (os.environ.get("CCS_CONSOLE_NUCLEUS"), os.path.join(getattr(sys, "_MEIPASS", ""), "console-nucleus") if getattr(sys, "frozen", False) else None,
              os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "console-nucleus"))):
        if d and os.path.isfile(os.path.join(d, "console", "collections", "ccs-collections")): return d
    raise FileNotFoundError("the console program for Hakchi games (tools/console-nucleus/console/collections) is not part of this installation")

def program() -> dict:
    """the console program and its library as the console gets them (LF line ends): {"files": {name: bytes}, "pins": {name: SHA-256},
    "dir": its folder on the console, named by its program's SHA-256 (the transport worker derives the same folder from its own pin)}"""
    d = os.path.join(_nucleus_dir(), "console")
    files = {"ccs-collections": _read(os.path.join(d, "collections", "ccs-collections")).replace(b"\r\n", b"\n"), "lib.sh": _read(os.path.join(d, "lib.sh")).replace(b"\r\n", b"\n")}
    pins = {n: _sha(b) for n, b in files.items()}
    return {"files": files, "pins": pins, "dir": f"{INTEGRATION}/program-{pins['ccs-collections'][:16]}"}

# the program's pins: the transport worker runs only these exact files (tools/console-worker/CcsWorker/Ops/ConsolePath.cs CollectionsProgram;
# tools/companion/test_hakchi_publish.py keeps the three equal)
PROGRAM_PINS = {"ccs-collections": "9f17bc6420635f0055ddb431f617e6cf613fbbe0b7ed9cdf6766fcc07f62f047",
                "lib.sh": "760ef83cb696ab8c5c98884ceaabfc83284ab4671506693576752956fb3bae1c"}

# ---------------------------------------------------------------- the console program on a disposable stand-in (ccs_install.FakeConsole)
# The same rules as tools/console-nucleus/console/collections/ccs-collections (tools/console-nucleus/test_nucleus.py runs that program itself
# under qemu-arm with hakchi's own findGameStorage and compares the result with this emulation): the stand-in's hakchi game storage is found
# as hakchi's findGameStorage finds it (games/$sftype-$sfregion, games/$sftype, games) for the stand-in's own software type and region
# (nes-usa for a North American NES Classic, nes-eur for one whose software names REGION EUR).
GAMEPATH = "/usr/share/games/nes/kachikachi"; SQUASHFS = "/var/squashfs"; OVERLAY_ROOT = "/var/lib/hakchi/rootfs"

def _sealed_lines(b: bytes | None, schema: str) -> list[str] | None:
    if b is None: return None
    t = b.decode("utf-8", "replace"); ls = t.split("\n")
    if not t.endswith("\n") or len(ls) < 3 or ls[0] != f"schema={schema}" or not ls[-2].startswith("end="): return None
    return ls[1:-2] if _sha(("\n".join(ls[:-2]) + "\n").encode()) == ls[-2][4:] else None

def derive_builtin(src: bytes, code: str) -> bytes:
    """the console's own entry for its built-in game as a Hakchi reference entry (the program's derive): game and picture paths pointed at
    hakchi's read-only view of the system, the saves in /var/saves"""
    pre = f"{GAMEPATH}/{code}/"; o = []; t = src.decode("utf-8", "replace")
    for line in (t[:-1] if t.endswith("\n") else t).split("\n"):
        line = line.rstrip("\r")
        if line.startswith(("Exec=", "Icon=")): line = line[:5] + " ".join(SQUASHFS + x if x.startswith(pre) else x for x in line[5:].split())
        elif line.startswith("Path="): line = f"Path={PROFILE_PATH}/{code}"
        o.append(line)
    return ("\n".join(o) + "\n").encode()

def emulate(fc, command: str, stage: str | None = None) -> str:
    """the console program's output (its lines and its result line) on the stand-in fc. A refusal or failure is its result line, as on a
    console; the caller reads it"""
    out: list[str] = []
    def p(path): return fc.p(path)
    def exists(path): return fc.stat(path)["type"] != "none"
    def isdir(path): return fc.stat(path)["type"] == "dir"
    def isfile(path): return fc.stat(path)["type"] == "file"
    def contains(d): return isdir(d) and any(n.startswith("CLV-") for n in fc.listdir(d))
    def where():
        gs = None
        t = fc.console_type() if hasattr(fc, "console_type") else "nes-usa"; t = t if t.startswith("nes-") else "nes-usa"
        for base in ("/media/hakchi/games", "/var/lib/hakchi/games"):
            for sub in ("/" + t, "/nes", ""):
                if contains(base + sub) or contains(base + sub + "/000"): gs = base + sub; break
            if gs: break
        if gs: return {"mode": "storage", "store": gs, "menu": gs + "/000" if isdir(gs + "/000") else gs, "system": SQUASHFS + GAMEPATH}
        return {"mode": "overlay", "store": None, "menu": OVERLAY_ROOT + GAMEPATH, "system": SQUASHFS + GAMEPATH}
    def journal() -> list[str]:
        ls = _sealed_lines(fc.read(JOURNAL, 4_000_000), "ccs-ownership/1")
        if not ls or "method=published" not in ls: return []
        return [l for l in ls if l.startswith(("file=", "data=", "dir="))]
    def jwrite(lines):
        body = "schema=ccs-ownership/1\nmethod=published\npackage=collections\n" + "".join(l + "\n" for l in sorted(set(x for x in lines if x)))
        q = p(JOURNAL); os.makedirs(os.path.dirname(q), exist_ok=True)
        with open(q + ".new", "w", newline="\n", encoding="utf-8") as f: f.write(body + f"end={_sha(body.encode())}\n")
        os.replace(q + ".new", q)
    def jfile(J, path): return isfile(path) and f"file={fc.sha256(path)} 0644 {path}" in J
    def res(cmd, what): out.append(f"result {cmd} {what}"); return "\n".join(out) + "\n"
    w = where()
    if command == "where":
        i = fc.sha256(INDEX_PATH) if isfile(INDEX_PATH) else "none"
        out.append(f"where mode={w['mode']} menu={w['menu']} store={w['store'] or '-'} system={w['system']} index={i} journal={'valid' if journal() else 'none'}")
        return res("where", "ok")
    if command in ("remove", "status"):
        J = journal()
        if command == "status":
            paths = sorted({l.split(" ", 2)[2] for l in J if l.startswith("file=")}); ok = ch = mi = 0
            for x in paths:
                if jfile(J, x): out.append(f"ok {x}"); ok += 1
                elif exists(x): out.append(f"changed {x}"); ch += 1
                else: out.append(f"missing {x}"); mi += 1
            i = fc.sha256(INDEX_PATH) if isfile(INDEX_PATH) else "none"
            return res("status", f"ok journal={'valid' if J else 'none'} files={ok} changed={ch} missing={mi} index={i}")
        if not J: out.append("no-journal published"); return res("remove", "ok")
        kept = []
        for l in J:   # lib.sh ccs_own_remove: files while unchanged, then folders deepest first while empty
            if l.startswith("file="):
                s, _m, x = l[5:].split(" ", 2)
                if not exists(x): out.append(f"gone {x}")
                elif isfile(x) and fc.sha256(x) == s: os.remove(p(x)); out.append(f"removed {x}")
                else: out.append(f"kept-changed {x}"); kept.append(l)
        for x in sorted((l[4:] for l in J if l.startswith("dir=")), reverse=True):
            if isdir(x):
                if not os.listdir(p(x)): os.rmdir(p(x)); out.append(f"removed {x}/")
                else: out.append(f"kept-folder {x}/"); kept.append("dir=" + x)
        if kept: jwrite(kept)
        else: os.remove(p(JOURNAL))
        return res("remove", "ok")
    # check and apply: the staged publication
    bad = lambda: res(command, "refused the staged publication is not complete or not well formed")
    if not (isinstance(stage, str) and stage.startswith(STAGE_ROOT + "/") and isdir(stage)): return bad()
    ls = _sealed_lines(fc.read(stage + "/publication", 4_000_000), PUB_SCHEMA)
    if ls is None: return bad()
    mode, menus, entries, files = None, [], [], []
    for l in ls:
        k, _, v = l.partition("="); a = v.split(" ")
        if k == "mode" and mode is None and v in MODES: mode = v
        elif k == "menu" and len(a) == 2 and MENU_RE.match(a[0]) and a[0] != "root" and MENU_RE.match(a[1]): menus.append(tuple(a))
        elif k == "entry" and len(a) == 3 and a[0] in ("game", "folder", "back", "builtin") and MENU_RE.match(a[1]) and CODE_RE.match(a[2]): entries.append(tuple(a))
        elif k == "file" and len(a) == 4 and re.match(r"^[0-9a-f]{64}$", a[0]) and MENU_RE.match(a[1]) and CODE_RE.match(a[2]) and a[3] in entry_names(a[2]):
            f = f"{stage}/files/{a[1]}/{a[2]}/{a[3]}"
            if not isfile(f) or fc.sha256(f) != a[0] or not any(e[1:] == (a[1], a[2]) and e[0] != "builtin" for e in entries): return bad()
            files.append(tuple(a))
        else: return bad()
    if mode is None or any(e[1] != "root" and e[1] not in {m[0] for m in menus} for e in entries) or (mode == "overlay" and (menus or any(e[1] != "root" or e[0] != "game" for e in entries))): return bad()
    if mode != w["mode"]: return res(command, f"refused the console's game folders are not the ones this was prepared for (mode {w['mode']})")
    J = journal(); sysd = w["system"]; derived = {}
    def mdir(m): return w["menu"] if m == "root" else f"{w['store']}/{m}"
    def state(kind, m, code):
        d = f"{mdir(m)}/{code}"
        if kind == "builtin":
            src = fc.read(f"{sysd}/{code}/{code}.desktop", 1_000_000) if mode == "storage" else None
            if src is None: return "absent"
            derived[code] = derive_builtin(src, code)
        if w["mode"] == "overlay" and exists(f"{sysd}/{code}"): return "foreign"
        if not exists(d): return "free"
        if not isdir(d) or f"dir={d}" not in J: return "foreign"
        want = {f"{code}.desktop": _sha(derived[code])} if kind == "builtin" else {f[3]: f[0] for f in files if f[1:3] == (m, code)}
        for n, s in want.items():
            t = f"{d}/{n}"
            if exists(t) and not jfile(J, t) and not (isfile(t) and fc.sha256(t) == s): return "changed"
        return "ours"
    def mstate(m):
        d = f"{w['store']}/{m}"
        if not exists(d): return "free"
        return "ours" if isdir(d) and f"dir={d}" in J else "foreign"
    if command == "check":
        out += [f"menu {m} {mstate(m)}" for m, _ in menus] + [f"entry {m} {c} {state(k, m, c)}" for k, m, c in entries]
        return res("check", "ok")
    al = _sealed_lines(fc.read(stage + "/apply", 4_000_000), APPLY_SCHEMA)
    if al is None: return res("apply", "refused the apply list is missing or not sealed")
    kv = {}
    for l in al: k, _, v = l.partition("="); kv.setdefault(k, v)
    skip = {l[5:] for l in al if l.startswith("skip=")}
    gen, isha, prev = kv.get("generation", ""), kv.get("index", ""), kv.get("previous", "")
    if not gen.isdigit() or not re.match(r"^[0-9a-f]{64}$", isha) or not (prev == "none" or re.match(r"^[0-9a-f]{64}$", prev)): return res("apply", "refused the apply list is not well formed")
    if fc.sha256(stage + "/index.json") != isha: return res("apply", "refused the staged index is not the one listed")
    for m, _ in menus:
        if mstate(m) not in ("free", "ours"): return res("apply", f"refused changed {w['store']}/{m} is not this program's folder (nothing was changed)")
    go = []
    for k, m, c in entries:
        if f"{m}/{c}" in skip: continue
        s = state(k, m, c)
        if s not in ("free", "ours"): return res("apply", f"refused changed {mdir(m)}/{c} is {s} now (nothing was changed)")
        go.append((k, m, c))
    if exists(INDEX_PATH) and not isfile(INDEX_PATH): return res("apply", f"refused {INDEX_PATH} is not a plain file (nothing was changed)")
    cur = fc.sha256(INDEX_PATH) if isfile(INDEX_PATH) else "none"
    if cur != prev: return res("apply", "refused the index changed since it was read (nothing was changed)")
    new = set()
    if w["mode"] == "storage": new |= {f"dir={w['store']}/{m}" for m, _ in menus}
    for k, m, c in go:
        d = f"{mdir(m)}/{c}"; new.add(f"dir={d}")
        if k == "builtin": new.add(f"file={_sha(derived[c])} 0644 {d}/{c}.desktop")
        else: new |= {f"file={f[0]} 0644 {d}/{f[3]}" for f in files if f[1:3] == (m, c)}
    new |= {"dir=/var/lib/ccs/integration/v1", f"file={isha} 0644 {INDEX_PATH}"}
    made = []   # without game storage: the folders missing on the way to the console menu's own game folder, made and recorded
    if w["mode"] == "overlay" and not isdir(w["menu"]):
        q = w["menu"]
        while q and not exists(q): made.append(q); q = q.rsplit("/", 1)[0]
        if not isdir(q): return res("apply", f"refused {q} is not a folder (nothing was changed)")
        new |= {f"dir={x}" for x in made}
    jwrite(J + sorted(new))
    for x in sorted(made): os.makedirs(p(x), exist_ok=True)
    if w["mode"] == "storage":
        for m, _ in menus: os.makedirs(p(f"{w['store']}/{m}"), exist_ok=True)
    wrote = 0
    for k, m, c in go:
        d = f"{mdir(m)}/{c}"; os.makedirs(p(d), exist_ok=True)
        items = [(f"{c}.desktop", derived[c])] if k == "builtin" else [(f[3], fc.read(f"{stage}/files/{m}/{c}/{f[3]}", 64_000_000)) for f in files if f[1:3] == (m, c)]
        for n, b in items:
            t = f"{d}/{n}"
            if not isfile(t) or fc.sha256(t) != _sha(b):
                with open(p(t), "wb") as fh: fh.write(b)
                wrote += 1
    newpaths = {l.split(" ", 2)[2] for l in new if l.startswith("file=")}; keep = []; removed = 0
    for l in J:
        if not l.startswith("file="): continue
        s, _m, x = l[5:].split(" ", 2)
        if x in newpaths or not exists(x): continue
        if isfile(x) and fc.sha256(x) == s: os.remove(p(x)); removed += 1; out.append(f"removed {x}")
        elif any(o.startswith("file=") and o.split(" ", 2)[2] == x for o in J if o != l) and jfile(J, x): pass
        else: out.append(f"kept-changed {x}"); keep.append(l)
    for x in sorted((l[4:] for l in J if l.startswith("dir=")), reverse=True):
        if f"dir={x}" in new or not isdir(x): continue
        for n in [n for n in os.listdir(p(x)) if n.endswith(".new")]: os.remove(os.path.join(p(x), n))
        if not os.listdir(p(x)): os.rmdir(p(x)); out.append(f"removed {x}/")
        else: out.append(f"kept-folder {x}/"); keep.append("dir=" + x)
    os.makedirs(p("/var/lib/ccs/integration/v1"), exist_ok=True)
    ib = fc.read(stage + "/index.json", 64_000_000)
    with open(p(INDEX_PATH) + ".new", "wb") as fh: fh.write(ib)
    os.replace(p(INDEX_PATH) + ".new", p(INDEX_PATH))
    jwrite(sorted(new) + keep)
    return res("apply", f"ok entries={len(go)} wrote={wrote} removed={removed} generation={gen}")

def parse_output(text: str) -> dict:
    """the console program's output: {"result": its result line, "ok", "lines": the other lines, "where": the where line's fields, "menus":
    {id: state}, "entries": {"MENU/CODE": state}}"""
    ls = [l.strip() for l in (text or "").replace("\r", "").split("\n") if l.strip()]
    res = next((l for l in reversed(ls) if l.startswith("result ")), "")
    out = {"result": res, "ok": res.split(" ")[2:3] == ["ok"], "lines": [l for l in ls if not l.startswith("result ")], "where": {}, "menus": {}, "entries": {}}
    for l in out["lines"]:
        a = l.split(" ")
        if a[0] == "where": out["where"] = dict(x.split("=", 1) for x in a[1:] if "=" in x)
        elif a[0] == "menu" and len(a) == 3: out["menus"][a[1]] = a[2]
        elif a[0] == "entry" and len(a) == 4: out["entries"][f"{a[1]}/{a[2]}"] = a[3]
    return out
