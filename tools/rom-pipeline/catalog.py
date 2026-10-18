"""Stage C — classify, deduplicate, assess compatibility, enrich (genre/year/players/built-in/art), write reports.

Inputs: <WORK>\\inventory.sqlite (archives, payloads, ident); <WORK>\\db\\libretro_*.dat (genre, year);
reports\\builtin.json (factory-game comparison); reports\\art\\index.json (thumbnail cache). Outputs in reports\\.
Confidence is reported in three separate dimensions: identity verification, header validation, compatibility evidence.
No title is console-tested; compatibility is documentation/community based only.
"""
from __future__ import annotations
import re
import os, sys, sqlite3, csv, json, re, time, hashlib, struct
from collections import Counter, defaultdict
sys.path.insert(0, os.path.dirname(__file__))
from common import *
from input_requirements import classify as classify_input, load_overrides as load_input_overrides, INPUT_COLUMNS

LOGFILE = os.path.join(LOGS, "catalog.log")
KACHI_CMD = "/bin/clover-kachikachi-wr {rom} --guest-overscan-dimensions 0,0,9,3 --initial-fadein-durations 3,2 --volume 75 --enable-armet"
RA_CMD = "/bin/km_fceumm {rom}"
# Documented kachikachi problem titles (hakchi FAQ / changelog / community). Keys are normalized titles.
PROBLEMS = {
    "battletoads": ("kachikachi", "crashes on level 2 on kachikachi; hakchi applies an automatic IPS patch on import (FAQ, since 2.09)"),
    "robocop 3": ("RetroArch", "FAQ: 'should work but not working at all' on kachikachi (auto-patch listed but unreliable) — RetroArch recommended"),
    "snow bros": ("kachikachi", "MMC1 consecutive-write bug on kachikachi; hakchi auto-patch (FAQ)"),
    "bill and ted s excellent video game adventure": ("kachikachi", "MMC1 consecutive-write bug on kachikachi; hakchi auto-patch (FAQ)"),
    "startropics ii zoda s revenge": ("RetroArch", "uses MMC6 (reported as mapper 4); kachikachi incompatible (FAQ) — RetroArch"),
    "mega man 4": ("kachikachi", "community: minor sound glitches on kachikachi (floor-touch SFX, buster); RetroArch/FCEUmm optional"),
    "mega man 5": ("kachikachi", "community: minor sound glitches on kachikachi; RetroArch/FCEUmm optional"),
    "mega man 6": ("kachikachi", "community: minor sound glitches on kachikachi (intro bells); RetroArch/FCEUmm optional"),
}
import genre_overrides   # owner-approved, sourced corrections where the libretro genre database is wrong
GENRE_MAP = {"Platform": "Platformer", "Shoot'em Up": "Shooter", "Lightgun Shooter": "Shooter (light gun)", "Beat'em Up": "Action", "Role-playing (RPG)": "RPG", "Hunting and Fishing": "Sports", "Sports with Animals": "Sports", "Music / Dancing": "Music", "Board": "Board/Card", "Card": "Board/Card", "Gambling": "Board/Card", "Quiz": "Puzzle"}

def norm_title(t: str) -> str:
    t = t.lower().replace("&", "and"); t = re.sub(r"[^a-z0-9]+", " ", t).strip(); return t
def rev_key(rev: str):
    if not rev: return 0.0
    m = re.search(r"([0-9]+(?:\.[0-9]+)?)", rev)
    if m: return float(m.group(1))
    m = re.search(r"Rev\s+([A-Z])", rev); return float(ord(m.group(1)) - 64) if m else 0.0

def load_dat(path):
    """libretro metadat: game ( comment "Name (USA)" <key> "value" rom ( crc X ) ) → {name: value}"""
    out = {}
    if not os.path.exists(path): return out
    txt = open(path, encoding="utf-8", errors="replace").read()
    for block in re.findall(r"game \((.*?)\n\)", txt, re.S):
        n = re.search(r'comment "([^"]+)"', block);
        if not n: n = re.search(r'name "([^"]+)"', block)
        if not n: continue
        for k in ("genre", "releaseyear", "developer", "publisher"):
            v = re.search(rf'\b{k} "([^"]+)"', block) or re.search(rf"\b{k} (\d+)", block)
            if v: out.setdefault(n.group(1), {})[k] = v.group(1)
    return out

def compat(row: dict) -> dict:
    m = row.get("mapper"); four = row.get("four_screen"); t = norm_title(row.get("title", ""))
    out = {"recommended_emulator": "", "core": "", "launch_command": "", "compat_confidence": "", "compat_evidence": "documentation and community reports", "compat_notes": "", "compat_warning": 0}
    notes = []
    if m is None:
        out.update(recommended_emulator="unknown", compat_confidence="none", compat_notes="header/mapper not established"); return out
    sysname = row.get("cart_system", "") or row.get("mesen_system", "")
    if sysname.startswith("VS") or "PlayChoice" in sysname or sysname in ("VsSystem", "Playchoice"):
        out.update(recommended_emulator="RetroArch", core="FCEUmm", launch_command=RA_CMD, compat_confidence="low", compat_notes=f"arcade board ({sysname}), not a home cartridge", compat_warning=1); return out
    emu, conf = "kachikachi", "medium"
    pref = PROBLEMS.get(t)
    if m in KACHIKACHI_MAPPERS and not four:
        emu = "kachikachi"
        conf = "medium" if m in (0, 1, 2, 3, 4, 7) else "low"
        if m not in (0, 1, 2, 3, 4, 7): notes.append(f"mapper {m} is on hakchi's kachikachi list but rarely exercised")
        if m == 5 and "castlevania iii" not in t: emu, conf = "RetroArch", "medium"; notes.append("MMC5: kachikachi support is 'only good enough for Castlevania III' (community) — RetroArch")
        if pref: emu = pref[0]; notes.append(pref[1]); conf = "medium"
        if row.get("licensed") == "unlicensed" and emu == "kachikachi": conf = "low"; notes.append("unlicensed cartridge hardware; kachikachi behavior undocumented")
        if m == 4 and row.get("mesen_mapper") in (118, 119) or row.get("cart_board", "").endswith(("TKSROM", "TLSROM", "TQROM")): notes.append(f"board {row.get('cart_board')} (MMC3 variant, iNES {row.get('mesen_mapper') or m}); the factory Kirby's Adventure uses TKSROM, so kachikachi handles that variant, but other variants may differ"); conf = "low" if conf != "low" else conf
    elif m == 71:
        emu, conf = "kachikachi", "low"; notes.append("mapper 71 (Camerica): hakchi auto-patches to mapper 2 on import (changelog 2.08); verify on console")
    else:
        emu, conf = "RetroArch", "medium"
        notes.append("four-screen mirroring: kachikachi unsupported" if four else f"mapper {m} not supported by kachikachi")
        if row.get("licensed") == "unlicensed": notes.append("unlicensed board; FCEUmm supports most of these mappers")
    if emu == "RetroArch": out["core"], out["launch_command"] = "FCEUmm", RA_CMD
    else: out["launch_command"] = KACHI_CMD
    out.update(recommended_emulator=emu, compat_confidence=conf, compat_notes="; ".join(notes), compat_warning=int(bool(notes) and conf != "medium" or emu == "RetroArch"))
    return out

def main():
    ensure_dirs(); con = sqlite3.connect(DB_PATH); con.row_factory = sqlite3.Row
    arch = {r["relpath"]: dict(r) for r in con.execute("SELECT * FROM archives")}
    pays = [dict(r) for r in con.execute("SELECT * FROM payloads ORDER BY relpath, entry")]
    ident = {r["sha256"]: dict(r) for r in con.execute("SELECT * FROM ident")}
    genre = load_dat(os.path.join(WORK, "db", "libretro_genre.dat")); years = load_dat(os.path.join(WORK, "db", "libretro_releaseyear.dat"))
    players_db = {}
    import xml.etree.ElementTree as ET
    for g in ET.parse(os.path.join(HAKCHI, "data", "nescarts.xml")).getroot().findall("game"):
        for c in g.findall("cartridge"):
            if c.get("sha1"): players_db[c.get("sha1").upper()] = g.get("players", "")
    builtin = {}
    bj = os.path.join(REPORTS, "builtin.json")
    if os.path.exists(bj):
        for b in json.load(open(bj, encoding="utf-8"))["games"]:
            if b.get("nointro_gamedata_sha256"): builtin[b["nointro_gamedata_sha256"]] = b
    art = json.load(open(os.path.join(REPORTS, "art", "index.json"), encoding="utf-8")) if os.path.exists(os.path.join(REPORTS, "art", "index.json")) else {}
    _dec = os.path.join(REPORTS, "art", "art-decisions.json")   # art_audit.py
    art_hidden = {k: d.get("reason") for k, d in (json.load(open(_dec, encoding="utf-8")).get("versions") or {}).items() if d.get("show") is False} if os.path.exists(_dec) else {}
    log(f"archives {len(arch)}, payloads {len(pays)}, ident {len(ident)}, genre {len(genre)}, years {len(years)}, builtin {len(builtin)}, art {len(art)}", LOGFILE)
    for p in pays:
        i = ident.get(p["sha256"])
        if i and i.get("header_hex") and not i.get("converted_sha256"):
            with open(p["stored"], "rb") as f: data = f.read()
            i["converted_sha256"] = hashlib.sha256(bytes.fromhex(i["header_hex"]) + data).hexdigest()
            con.execute("UPDATE ident SET converted_sha256=? WHERE sha256=?", (i["converted_sha256"], p["sha256"]))
    con.commit()
    master = []
    for p in pays:
        i = ident.get(p["sha256"], {}); a = arch.get(p["relpath"], {})
        row = {"archive": p["relpath"], "archive_status": a.get("status", ""), "entry": p["entry"], "payload_size": p["size"], "payload_sha256": p["sha256"], "payload_sha1": p["sha1"], "payload_crc32": p["crc32"], "format": p["format"]}
        for k in ("nointro_name","title","regions","rev","flags","cart_match","cart_game","cart_region","cart_class","cart_publisher","cart_date","cart_board","cart_system","cart_dump","cart_catalog","mesen_match","mesen_mapper","mesen_submapper","mesen_mirror","mesen_battery","mesen_board","mesen_system","mapper","submapper","prg","chr","vertical","battery","four_screen","nes2","header_hex","header_source","header_validation","header_status","headered_crc32","nointro_match","converted_sha256","notes"):
            row[k] = i.get(k, "" if k not in ("mapper","submapper","prg","chr","vertical","battery","four_screen","nes2","nointro_match","mesen_match") else None)
        if not row["nointro_name"]: row["nointro_name"] = os.path.basename(p["relpath"])[:-4]
        row["identity_verification"] = "No-Intro verified (payload CRC32 in No-Intro DB)" if row["nointro_match"] else ("cartridge DB only (NesCartDB/Mesen hash match)" if (row["cart_match"] or row["mesen_match"]) else "unverified")
        row["players"] = players_db.get((p["sha1"] or "").upper(), "")
        gi = genre.get(row["nointro_name"], {}); yi = years.get(row["nointro_name"], {})
        row["genre"] = ", ".join(sorted({GENRE_MAP.get(x.strip(), x.strip()) for x in gi.get("genre", "").split("/") if x.strip()})) if gi.get("genre") else ""
        row["genre_source"] = "libretro-database (metadat/genre)" if row["genre"] else ""
        row["year"] = (row["cart_date"] or "")[:4] or (yi.get("releaseyear", "") if yi else "")
        row["year_source"] = "NesCartDB" if (row["cart_date"] or "")[:4] else ("libretro-database" if row["year"] else "")
        b = builtin.get(p["sha256"])
        row["builtin_code"] = b["code"] if b else ""; row["builtin_title"] = b["factory_title"] if b else ""
        row["builtin_status"] = ("BUILT-IN: EXACT MATCH" if b and b["match_case"].startswith("1") else "NOT BUILT-IN")
        if b:
            row["players"] = b.get("players") or row["players"]; row["simultaneous"] = {"1": "simultaneous", "0": "alternating"}.get(b.get("simultaneous", ""), "")
        else: row["simultaneous"] = ""
        ai = art.get(p["sha256"][:16], {}); row["art"] = f"art/{p['sha256'][:16]}.jpg" if ai.get("source") in ("libretro-thumbnails", "oem-backup") else ""; row["art_source"] = ai.get("source", "")
        hid = art_hidden.get(p["sha256"][:16])   # N3.1 art audit: a scan that visibly shows another release's box is not shown (the file is kept)
        if row["art"] and hid: row["art"] = ""; row["art_source"] = ""; row["art_hidden"] = hid
        master.append(row)
    # built-in "same title, different content" (case 2) marks: factory title present but hash differs
    bt = {}
    if os.path.exists(bj):
        for b in json.load(open(bj, encoding="utf-8"))["games"]:
            bt[norm_title(b["factory_title"].replace("™", "").replace("®", ""))] = b
    for r in master:
        if r["builtin_status"] == "NOT BUILT-IN":
            b = bt.get(norm_title(r["title"]))
            if b and ("USA" in r["regions"] or "World" in r["regions"]):
                fp = (b.get("publisher") or "").lower().split()[0] if b.get("publisher") else ""
                cp = (r["cart_publisher"] or "").lower().split()[0] if r["cart_publisher"] else ""
                same_pub = bool(fp and cp and (fp == cp or fp.startswith(cp) or cp.startswith(fp)))
                r["builtin_status"] = "BUILT-IN: DIFFERENT REVISION" if same_pub else "BUILT-IN: IDENTITY ONLY"
                r["builtin_code"] = b["code"]; r["builtin_title"] = b["factory_title"]
    # ---- classification ----
    retail, excluded, ambiguous = [], [], []
    for r in master:
        reasons = []
        if r["archive_status"] != "ok": reasons.append(f"archive {r['archive_status']}")
        if r["format"] not in ("headerless", "iNES", "NES2.0"): reasons.append(f"not a NES ROM payload ({r['format']})")
        regs = [x for x in (r["regions"] or "").split(",") if x]
        if not (("USA" in regs) or ("World" in regs)): reasons.append("non-US region: " + (",".join(regs) or "unknown"))
        flags = [x for x in (r["flags"] or "").split("|") if x]
        nr = nonretail_reasons(flags) + bracket_reasons(r["nointro_name"])
        if nr: reasons.append("non-retail/variant: " + ", ".join(nr))
        sysname = r["cart_system"] or r["mesen_system"] or ""
        if sysname.startswith("VS") or "PlayChoice" in sysname or sysname in ("VsSystem", "Playchoice"): reasons.append("arcade system " + sysname)
        unl = any(f.strip().lower() in ("unl", "unlicensed") for f in flags) or r["cart_class"] == "Unlicensed"
        r["licensed"] = "unlicensed" if unl else "licensed"
        r["exclusion_reasons"] = "; ".join(reasons)
        if reasons: excluded.append(r); continue
        amb = []
        if not r["header_hex"]: amb.append("no evidence-based header: " + (r["header_status"] or "unidentified"))
        if r["cart_match"] and r["cart_region"] not in ("USA", "") and "USA" in regs: amb.append(f"No-Intro says USA but NesCartDB cartridge region is {r['cart_region']}")
        if "payload shared by NesCartDB games" in (r["notes"] or ""): amb.append("same ROM data listed under several NesCartDB game names")
        if (r["header_validation"] or "").startswith("CONFLICT"): amb.append(r["header_validation"])
        r["ambiguity"] = "; ".join(amb)
        if not r["header_hex"]: ambiguous.append(r); continue
        if amb: ambiguous.append(r)
        retail.append(r)
    # ---- dedupe: distinct game = normalized title + publisher (when known) + licensed status ----
    groups = defaultdict(list)
    for r in retail:
        pub = (r["cart_publisher"] or "").strip().lower()
        if not pub:
            tag = next((f for f in (r["flags"] or "").split("|") if f in ("Tengen", "Namco", "Jaleco", "Acclaim", "Color Dreams", "ShareData", "Hudson", "Kemco", "Sunsoft", "HAL Laboratory")), "")
            pub = tag.lower()
        groups[(norm_title(r["title"]), pub, r["licensed"])].append(r)
    # merge publisher-less singletons into a same-title group of the same licensed status if exactly one exists
    keys = list(groups.keys())
    for k in keys:
        if k[1] == "" and k in groups:
            others = [o for o in groups if o != k and o[0] == k[0] and o[2] == k[2] and o[1] != ""]
            if len(others) == 1: groups[others[0]].extend(groups.pop(k))
    catalog = []; dup_excluded = []
    for key, rows in sorted(groups.items()):
        rows.sort(key=lambda x: (1 if x["nointro_match"] else 0, 1 if "USA" in x["regions"] else 0, rev_key(x["rev"]), 1 if x["cart_dump"] == "ok" else 0, -len(x["flags"] or "")), reverse=True)
        best = rows[0]; gid = f"{key[0]}|{key[1]}|{key[2]}"
        best["group_id"] = gid; best["group_size"] = len(rows); best["alternates"] = " | ".join(x["nointro_name"] for x in rows[1:])
        best["recommended_reason"] = "only version" if len(rows) == 1 else ("latest verified US revision" if rows[0]["rev"] else "verified US version preferred over alternates")
        catalog.append(best)
        for x in rows[1:]: x["group_id"] = gid; x["exclusion_reasons"] = f"duplicate/revision of '{best['nointro_name']}'"; dup_excluded.append(x)
    # ---- controller / accessory requirements (Mesen NES 2.0 default expansion device + curated overrides) ----
    mesen_ctrl = {}
    try:
        for line in open(os.path.join(WORK, "db", "MesenNesDB.txt"), encoding="utf-8", errors="replace"):
            if line.startswith("#") or not line.strip(): continue
            p = line.rstrip(chr(10)).split(",")
            if len(p) > 13 and p[13].strip().isdigit(): mesen_ctrl[p[0].upper()] = int(p[13])
    except OSError as e: log(f"input: Mesen DB unavailable ({e})", LOGFILE)
    overrides = load_input_overrides(); log(f"input overrides loaded: {len(overrides)} keys", LOGFILE)
    for r in catalog:
        r.update(classify_input(r, mesen_ctrl.get((r.get("payload_crc32") or "").upper()), overrides))
    for r in catalog:
        r.update(compat(r)); r["game_id"] = r["payload_sha256"][:16]; genre_overrides.apply_row(r)   # documented genre corrections (genre_overrides.py), applied after the source genre is read
        r["display_title"] = r["title"]; r["publisher"] = r["cart_publisher"] or ""
        r["selection_status"] = "review" if r.get("ambiguity") else "candidate"
        r["header_validation_short"] = ("cross-validated" if (r["header_validation"] or "").startswith("cross") else "conflict" if (r["header_validation"] or "").startswith("CONFLICT") else "single-source" if r["header_hex"] else "none")
    # ---- authoritative built-in linking: factory inventory first (exact payload, then the factory payload as a revision
    #      alternate of a catalog row), then normalized title + publisher-token overlap for factory builds that the catalog
    #      excludes as re-release variants (e.g. Switch Online / Virtual Console builds) ----
    for r in catalog: r["builtin_factory_version"] = ""
    if os.path.exists(bj):
        fac = json.load(open(bj, encoding="utf-8"))["games"]
        by_sha = {r["payload_sha256"]: r for r in catalog}
        name_to_row = {}
        for r in catalog:
            name_to_row.setdefault(r["nointro_name"], r)
            for alt in (r["alternates"] or "").split(" | "):
                if alt: name_to_row.setdefault(alt, r)
        def tnorm(t):
            t = norm_title((t or "").replace("™", "").replace("®", ""))
            t = re.sub(r"^the\s+", "", t); t = re.sub(r",\s*the$", "", t); return t
        def pub_overlap(a, b):
            ta = set(re.findall(r"[a-z0-9]+", (a or "").lower())) - {"co", "ltd", "inc", "of", "america", "corp", "corporation"}
            tb = set(re.findall(r"[a-z0-9]+", (b or "").lower())) - {"co", "ltd", "inc", "of", "america", "corp", "corporation"}
            return bool(ta & tb)
        for b in fac:
            if not b["match_case"].startswith("1"): continue
            if b["nointro_gamedata_sha256"] in by_sha:
                by_sha[b["nointro_gamedata_sha256"]]["builtin_factory_version"] = b["nointro_name"]; continue   # EXACT already
            row = name_to_row.get(b["nointro_name"])
            if not row:
                cands = [x for x in catalog if tnorm(x["title"]) == tnorm(b["factory_title"]) and pub_overlap(b.get("publisher"), x["cart_publisher"])]
                row = cands[0] if len(cands) == 1 else None
            if row:
                row["builtin_status"] = "BUILT-IN: DIFFERENT REVISION"; row["builtin_code"] = b["code"]; row["builtin_title"] = b["factory_title"]; row["builtin_factory_version"] = b["nointro_name"]
        for r in catalog:
            if r["builtin_status"] in ("BUILT-IN: DIFFERENT REVISION", "BUILT-IN: IDENTITY ONLY") and not r["builtin_factory_version"]:
                b = next((x for x in fac if x["code"] == r["builtin_code"]), None)
                if b: r["builtin_factory_version"] = b["nointro_name"]
        linked = {r["builtin_code"] for r in catalog if r["builtin_status"].startswith("BUILT-IN") and r["builtin_status"] != "BUILT-IN: IDENTITY ONLY"}
        log(f"factory games represented in catalog (exact or different revision): {len(linked)} of {len(fac)}; missing: {[x['code'] for x in fac if x['code'] not in linked]}", LOGFILE)
    # ---- storage model (measured + conservative overhead) ----
    sb_free_mib, sb_total_mib, sb_rsv_mib = 377.1, 394.0, 19.7
    try:
        f = open(os.path.join(PROJECT, "backups", "primary", "2026-10-09_stage2", "nand-full.bin"), "rb"); best = None; size = f.seek(0, 2)
        for off in range(0, size - 4096, 65536):
            f.seek(off + 0x438)
            if f.read(2) == b"\x53\xef":
                f.seek(off + 0x400); sb = f.read(1024); mnt = struct.unpack_from("<H", sb, 0x34)[0]
                if sb[0x78:0x88].split(b"\0")[0] == b"data" and (best is None or mnt > best[0]): best = (mnt, sb)
        if best:
            sb = best[1]; bs = 1024 << struct.unpack_from("<I", sb, 0x18)[0]
            sb_total_mib = struct.unpack_from("<I", sb, 0x04)[0] * bs / 1048576; sb_free_mib = struct.unpack_from("<I", sb, 0x0C)[0] * bs / 1048576; sb_rsv_mib = struct.unpack_from("<I", sb, 0x08)[0] * bs / 1048576
        f.close()
    except Exception as e: log(f"storage: fallback constants ({e})", LOGFILE)
    storage = {"measured_partition_mib": round(sb_total_mib, 1), "measured_free_mib_before_mod": round(sb_free_mib, 1), "root_reserved_mib": round(sb_rsv_mib, 1), "model": "reference-estimate", "source": "reference model: ext4 superblock of the data partition of a stock 2018 NES Classic Edition (CLV-101), read before any modification; not a measurement of any visitor's console",
               "overhead_estimates_mib": {"hakchi_install": 25, "retroarch_plus_cores": 60, "saves_and_states_reserve": 10, "per_game_metadata_art_kib": 80, "filesystem_slack_pct": 5},
               "note": "Free space is measured; overhead figures are conservative estimates (hakchi/RetroArch module sizes are confirmed only at install). Hakchi's own free-space indicator is authoritative before a sync."}
    base = sb_free_mib - sb_rsv_mib - storage["overhead_estimates_mib"]["hakchi_install"] - storage["overhead_estimates_mib"]["retroarch_plus_cores"] - storage["overhead_estimates_mib"]["saves_and_states_reserve"]
    storage["conservative_usable_mib"] = round(base * (1 - storage["overhead_estimates_mib"]["filesystem_slack_pct"] / 100), 1)
    # ---- missing titles (NesCartDB USA licensed names absent from the collection) ----
    present = {r["cart_game"] for r in master if r["cart_game"]}
    by_sha1, _ = load_nescarts(); usa_games = sorted({c.game for cl in by_sha1.values() for c in cl if c.region == "USA" and c.cls == "Licensed" and not c.prototype}); missing = [g for g in usa_games if g not in present]
    # ---- write ----
    def write_csv(path, rows, cols):
        with open(path, "w", newline="", encoding="utf-8") as f:
            w = csv.DictWriter(f, fieldnames=cols, extrasaction="ignore"); w.writeheader(); [w.writerow(r) for r in rows]
    mcols = ["archive","archive_status","entry","payload_size","payload_sha256","payload_sha1","payload_crc32","format","nointro_name","title","regions","rev","flags","identity_verification","cart_match","cart_game","cart_region","cart_class","cart_publisher","cart_date","cart_board","cart_system","cart_dump","cart_catalog","mesen_match","mesen_mapper","mesen_submapper","mesen_mirror","mesen_battery","mesen_board","mapper","submapper","prg","chr","vertical","battery","four_screen","nes2","header_hex","header_source","header_validation","header_status","headered_crc32","nointro_match","converted_sha256","licensed","genre","year","players","builtin_status","builtin_code","exclusion_reasons","ambiguity","notes"]
    write_csv(os.path.join(REPORTS, "nes-rom-master-inventory.csv"), master, mcols)
    ccols = ["game_id","display_title","nointro_name","regions","rev","publisher","year","year_source","licensed","genre","genre_source","players","simultaneous","builtin_status","builtin_code","builtin_title","builtin_factory_version","archive","entry","payload_sha256","payload_size","recommended_reason","group_id","group_size","alternates","identity_verification","nointro_match","header_source","header_validation","header_validation_short","header_status","cart_match","cart_game","cart_board","mesen_board","mapper","submapper","prg","chr","vertical","battery","four_screen","nes2","converted_sha256","recommended_emulator","core","launch_command","compat_confidence","compat_evidence","compat_notes","compat_warning","ambiguity","selection_status","art","art_source"] + INPUT_COLUMNS
    cat_sorted = sorted(catalog, key=lambda r: norm_title(r["display_title"]))
    write_csv(os.path.join(REPORTS, "nes-us-retail-catalog.csv"), cat_sorted, ccols)
    write_csv(os.path.join(REPORTS, "nes-excluded-variants.csv"), excluded + dup_excluded, ["nointro_name","archive","regions","flags","cart_game","cart_region","cart_class","exclusion_reasons","group_id","payload_sha256","header_status"])
    amb_rows = [dict(r, in_catalog=("yes" if r in catalog else "no")) for r in ambiguous]
    write_csv(os.path.join(REPORTS, "nes-ambiguous-releases.csv"), amb_rows, ["nointro_name","archive","regions","flags","in_catalog","identity_verification","header_status","header_validation","mesen_mapper","mesen_board","cart_game","cart_region","cart_class","ambiguity","notes","payload_sha256"])
    md = ["# NES US retail catalog — one recommended version per distinct game", "", f"Generated {time.strftime('%Y-%m-%d %H:%M')} from `{SOURCE}` (No-Intro headerless). {len(catalog)} games ({sum(1 for r in catalog if r['licensed']=='licensed')} licensed, {sum(1 for r in catalog if r['licensed']=='unlicensed')} unlicensed). Identity, header validation and compatibility are separate columns; compatibility is documentation/community based — nothing is console-tested.", "",
          "| Title | No-Intro name | Pub. | Year | Lic. | Genre | Players | Built-in | Identity | Header | Mapper | Emulator | Compat. | Status | Notes |", "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|"]
    for r in cat_sorted: md.append(f"| {r['display_title']} | {r['nointro_name']} | {r['publisher']} | {r['year']} | {r['licensed']} | {r['genre']} | {r['players']} {r['simultaneous']} | {r['builtin_status'].replace('BUILT-IN: ','').replace('NOT BUILT-IN','')} | {'verified' if r['nointro_match'] else 'cart-DB'} | {r['header_validation_short']} | {r['mapper']} | {r['recommended_emulator']} | {r['compat_confidence']} | {r['selection_status']} | {(r['compat_notes'] + ('; ' + r['ambiguity'] if r.get('ambiguity') else '')).strip('; ')} |")
    open(os.path.join(REPORTS, "nes-us-retail-catalog.md"), "w", encoding="utf-8").write("\n".join(md))
    k = [r for r in catalog if r["recommended_emulator"] == "kachikachi"]; ra = [r for r in catalog if r["recommended_emulator"] == "RetroArch"]
    cm = ["# Compatibility report (planning only — no title has been tested on the console)", "", f"- kachikachi (Nintendo's emulator): {len(k)}", f"- RetroArch / FCEUmm: {len(ra)}", "",
          "Evidence basis: hakchi FAQ mapper list (0,1,2,3,4,5,7,9,10,86,87,184 + FDS; four-screen unsupported), hakchi changelog (mapper 71 auto-patch, IPS auto-patches), hakchi issue tracker and community reports. Routing is explicit (D-15). Confidence levels: medium = mapper on the documented list and no known reports; low = rare mapper, unlicensed hardware, MMC3 board variants or auto-patch dependence; none = no mapper evidence.", "",
          "## Mapper distribution", ""] + [f"- mapper {m}: {c} ({'kachikachi' if m in KACHIKACHI_MAPPERS else 'RetroArch'})" for m, c in sorted(Counter(r["mapper"] for r in catalog).items(), key=lambda x: (-x[1], str(x[0])))]
    cm += ["", "## Exceptions: kachikachi-mapper titles routed to RetroArch", ""] + [f"- {r['display_title']} (mapper {r['mapper']}): {r['compat_notes']}" for r in sorted(ra, key=lambda r: norm_title(r['display_title'])) if r["mapper"] in KACHIKACHI_MAPPERS]
    cm += ["", "## Other RetroArch-routed titles", ""] + [f"- {r['display_title']} — mapper {r['mapper']}: {r['compat_notes']}" for r in sorted(ra, key=lambda r: norm_title(r['display_title'])) if r["mapper"] not in KACHIKACHI_MAPPERS]
    cm += ["", "## kachikachi titles with documented concerns or low confidence", ""] + [f"- {r['display_title']} (mapper {r['mapper']}, {r['compat_confidence']}): {r['compat_notes']}" for r in sorted(k, key=lambda r: norm_title(r['display_title'])) if r["compat_notes"]]
    cm += ["", "## Header validation conflicts in the catalog (NesCartDB vs Mesen/NES2.0 DB)", ""] + [f"- {r['display_title']}: {r['header_validation']}" for r in cat_sorted if (r['header_validation'] or '').startswith('CONFLICT')]
    open(os.path.join(REPORTS, "nes-compatibility-report.md"), "w", encoding="utf-8").write("\n".join(cm))
    open(os.path.join(REPORTS, "nes-missing-titles.md"), "w", encoding="utf-8").write("# Licensed US NES games in NesCartDB not represented in the collection (informational)\n\n" + f"{len(missing)} of {len(usa_games)} NesCartDB USA-licensed game names have no matching payload.\n\n" + "\n".join(f"- {g}" for g in missing))
    factory_ref = None
    if os.path.exists(bj):
        fb = open(bj, "rb").read(); fj = json.loads(fb.decode("utf-8"))
        factory_ref = {"file": "reports/builtin.json", "sha256": hashlib.sha256(fb).hexdigest(), "count": len(fj["games"]), "codes": sorted(x["code"] for x in fj["games"]), "source": "verified system-partition backup (nandb-decrypted.bin), see reports/nes-builtin-games.md"}
    json.dump({"generated": time.strftime("%Y-%m-%dT%H:%M:%S"), "source": SOURCE, "storage": storage, "factory_inventory": factory_ref, "games": [{c: r.get(c) for c in ccols} for r in cat_sorted]}, open(os.path.join(REPORTS, "catalog.json"), "w", encoding="utf-8"), indent=1)
    amb_not_cat = [r for r in ambiguous if r not in catalog and r not in dup_excluded]
    from collections import Counter as _C
    input_stats = {"by_level": dict(_C(r["input_level"] for r in catalog)), "by_support": dict(_C(r["input_nes_classic_support"] for r in catalog)), "special_required": sum(1 for r in catalog if r["input_accessory_requirement"] == "required"), "special_optional": sum(1 for r in catalog if r["input_accessory_requirement"] in ("optional", "mode-specific")), "unknown": sum(1 for r in catalog if r["input_level"] == "unknown"), "light_gun": sum(1 for r in catalog if "light-gun" in r["input_modes"])}
    counts = {"input_requirements": input_stats, "archives": len(arch), "archives_ok": sum(1 for a in arch.values() if a["status"] == "ok"), "payloads": len(pays), "distinct_payloads": len({p['sha256'] for p in pays}),
              "nointro_verified_payloads": sum(1 for r in master if r["nointro_match"]), "payloads_with_header": sum(1 for r in master if r["header_hex"]), "headers_cross_validated": sum(1 for r in master if (r["header_validation"] or "").startswith("cross")), "headers_single_source": sum(1 for r in master if r["header_hex"] and (r["header_validation"] or "").startswith("single")), "headers_conflict": sum(1 for r in master if (r["header_validation"] or "").startswith("CONFLICT")),
              "us_retail_games": len(catalog), "licensed": sum(1 for r in catalog if r["licensed"] == "licensed"), "unlicensed_in_catalog": sum(1 for r in catalog if r["licensed"] == "unlicensed"), "review_in_catalog": sum(1 for r in catalog if r["selection_status"] == "review"),
              "duplicates_revisions_excluded": len(dup_excluded), "regional_nonretail_excluded": len(excluded), "ambiguous_total": len(ambiguous), "ambiguous_not_in_catalog": len(amb_not_cat), "ambiguous_in_catalog": len(ambiguous) - len(amb_not_cat),
              "kachikachi": len(k), "retroarch": len(ra), "unknown_emulator": sum(1 for r in catalog if r["recommended_emulator"] == "unknown"),
              "builtin_exact": sum(1 for r in catalog if r["builtin_status"] == "BUILT-IN: EXACT MATCH"), "builtin_other": sum(1 for r in catalog if r["builtin_status"].startswith("BUILT-IN") and r["builtin_status"] != "BUILT-IN: EXACT MATCH"),
              "with_genre": sum(1 for r in catalog if r["genre"]), "with_players": sum(1 for r in catalog if r["players"]), "with_simultaneous_info": sum(1 for r in catalog if r["simultaneous"]), "with_year": sum(1 for r in catalog if r["year"]), "with_art": sum(1 for r in catalog if r["art"]),
              "missing_usa_licensed": len(missing), "storage": storage,
              "reconciliation": f"{len(pays)} payloads = {len(catalog)} catalog + {len(dup_excluded)} revision duplicates + {len(excluded)} regional/non-retail + {len(amb_not_cat)} ambiguous-not-in-catalog = {len(catalog)+len(dup_excluded)+len(excluded)+len(amb_not_cat)}; the {len(ambiguous)-len(amb_not_cat)} 'review' catalog rows are counted inside the catalog total (overlap), not added."}
    json.dump(counts, open(os.path.join(REPORTS, "nes-rom-processing-counts.json"), "w"), indent=1)
    log(f"counts: {json.dumps({k: v for k, v in counts.items() if k != 'storage'})}", LOGFILE)
    con.close()

if __name__ == "__main__":
    main()
