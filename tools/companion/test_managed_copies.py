r"""Game files kept by the Studio (managed copies) are what preparation uses, with or without a development inventory database.

A synthetic site (catalog.json, library.json and the factory inventory from site-data/builtin.json) and synthetic game files only; isolated
data folders; nothing touches Hakchi, a console or the network. Cases:
  no development database    curated games with headerless, dirty-headered and NES 2.0 headered managed copies, an Extended Library
                             version and a personal game are planned, then prepared into a disposable Hakchi tree: every prepared file
                             is the catalog's evidence header and the content (one header), a personal game keeps its own file
  stale development database a database naming paths that no longer exist does not hide the managed copies
  missing / wrong file       refused in plain words with the Locate Game File guidance; never "inventory.py" or a work-store path
  Locate                     after Locate Game File the next plan sees the new file, at the same managed path (no stale result)
  console package            play.rom_for (the console package's and the player's resolver) finds the same managed copies
  source files               every game file the test made is unchanged afterwards
  python test_managed_copies.py      Exit 0 = PASS."""
from __future__ import annotations
import hashlib, json, os, shutil, sqlite3, sys, tempfile, unittest, zlib
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
TMP = tempfile.mkdtemp(prefix="ccs-managed-")
SITE, WORK, DATA, PERS = (os.path.join(TMP, n) for n in ("site", "work", "data", "personal"))
for d in (SITE, WORK, DATA, PERS): os.makedirs(d)
os.environ.update(CCS_STUDIO_SITE=SITE, CCS_WORK=WORK, CCS_STUDIO_DATA=DATA, CCS_PERSONAL_DIR=PERS)
sys.path[:0] = [HERE, os.path.join(HERE, "..", "rom-pipeline")]

def content(seed: str, size: int) -> bytes:
    out = bytearray(); n = 0
    while len(out) < size: out += hashlib.sha256(f"{seed}|{n}".encode()).digest(); n += 1
    return bytes(out[:size])
def sha(b: bytes) -> str: return hashlib.sha256(b).hexdigest()
H = bytes.fromhex
# games: id is the content SHA-256[:16]; "ev" the evidence header the Game Library records; "file" what the managed copy holds
EV0 = "4e45531a020101000000000000000000"   # 32 KB PRG, 8 KB CHR, vertical, mapper 0
EV2 = "4e45531a080020000000000000000000"   # 128 KB PRG, CHR RAM, mapper 2
G = {}
def game(key, seed, size, ev, file_header=None, curated=True, title=None):
    c = content(seed, size); G[key] = {"key": key, "content": c, "id": sha(c)[:16], "sha": sha(c), "ev": ev, "title": title or f"Synthetic {key}", "curated": curated,
                                        "file": (file_header + c) if file_header is not None else c}
game("plain", "plain", 40960, EV0)                                            # headerless managed copy (Locate from a headerless archive)
game("dirty", "dirty", 131072, EV2, b"NES\x1a\x08\x00\x20" + b"DiskDude!")      # headered copy whose own header differs from the evidence
game("nes2", "nes2", 40960, EV0, H("4e45531a020100080000000000000000"))       # NES 2.0 headered copy
game("ext", "ext", 40960, EV0, curated=False)                                  # Extended Library version (library.json only)
game("miss", "miss", 40960, EV0)                                               # its record names a file that is gone
game("wrong", "wrong", 40960, EV0)                                             # its managed path holds another game's content
OWN = H("4e45531a020101000000000000000000"); PC = content("personal", 40960); PFILE = OWN + PC; PID = "p_" + sha(PC)[:16]

def write_site():
    shutil.copy(os.path.join(ROOT, "site-data", "builtin.json"), os.path.join(SITE, "builtin.json"))
    rows, versions, titles, payloads = [], [], [], {}
    for g in G.values():
        conv = sha(H(g["ev"]) + g["content"]); tid = "t_" + g["id"][:12]
        payloads[g["sha"]] = {"sha256": g["sha"], "size": len(g["content"]), "format": "headerless", "content_sha256": g["sha"], "header": {"hex": g["ev"], "format": "iNES"}, "converted_sha256": conv}
        versions.append({"id": g["id"], "sha256": g["sha"], "name": g["title"] + " (World)", "title": g["title"], "title_id": tid, "kind": "homebrew", "identity": "verified", "emulator": "kachikachi", "core": "",
                         "builtin_status": "NOT BUILT-IN", "builtin_code": "", "curated": g["curated"], "size": len(g["content"]), "year": "2026", "publisher": "Test", "art": "", "input_level": "none",
                         "input_support": "supported", "input_requirement": "none", "input_modes": ["standard-controller"]})
        titles.append({"id": tid, "title": g["title"], "versions": [g["id"]], "curated": g["curated"]})
        if g["curated"]:
            rows.append({"game_id": g["id"], "display_title": g["title"], "nointro_name": g["title"] + " (World)", "payload_sha256": g["sha"], "payload_size": len(g["content"]), "converted_sha256": conv,
                         "builtin_status": "NOT BUILT-IN", "builtin_code": "", "builtin_factory_version": "", "recommended_emulator": "kachikachi", "core": "", "mapper": H(g["ev"])[6] >> 4, "battery": 0,
                         "players": "1", "simultaneous": "", "year": "2026", "publisher": "Test", "art": "", "input_primary": "standard-controller", "input_modes": ["standard-controller"], "input_special_accessory": "",
                         "input_accessory_requirement": "none", "input_standard_controller_playable": "true", "input_nes_classic_support": "supported", "input_level": "none"})
    json.dump({"games": rows}, open(os.path.join(SITE, "catalog.json"), "w", encoding="utf-8"))
    json.dump({"titles": titles, "versions": versions, "payloads": payloads}, open(os.path.join(SITE, "library.json"), "w", encoding="utf-8"))
write_site()

import hakchi_bridge as hb, personal as ps, package_api as PA, play, manifest_v4 as M4
from console_engine import plan as EP, hakchi_publish as HP
SRC = os.path.join(TMP, "sources"); os.makedirs(SRC)
LIB = os.path.join(PERS, "library"); os.makedirs(LIB)
def managed(key):
    g = G[key]; return os.path.join(LIB, g["id"] + (".nes" if g["file"][:4] == b"NES\x1a" else ".unh"))
def put(p, b):
    with open(p, "wb") as f: f.write(b)
    return p
def store_record(key, path):
    """the record as Locate wrote it when the file was right (a file changed or removed since is found by its content)"""
    g = G[key]; b = g["file"]
    return {"version_id": g["id"], "path": path, "format": "iNES" if b[:4] == b"NES\x1a" else "headerless", "file_sha256": sha(b), "content_sha256": g["sha"], "file_size": len(b), "source_name": key, "added": "2026-01-01T00:00:00", "seen": 1}

def manifest(keys, personal=False):
    fref, factory = hb.factory_reference(os.path.join(SITE, "builtin.json")); sels = []
    for k in keys: sels.append(M4.selection_from_row(hb.resolve_version(G[k]["id"]), None, "catalog" if G[k]["curated"] else "extended", factory))
    if personal: sels.append(M4.selection_from_row(hb.resolve_version(PID), None, "personal", factory))
    return {"schema": M4.SCHEMA4, "name": "Managed copies", "collection_mode": "exclusive", "factory_game_policy": "hide_unselected", "factory_inventory": dict(fref), "selections": sels,
            "personal_library": [{"id": PID}] if personal else [], "hidden_factory_games": [{"code": cd, "title": factory[cd]["factory_title"]} for cd in fref["codes"]]}

def all_words(pv) -> str: return json.dumps({"problems": pv["problems"], "games": pv["games"]}, ensure_ascii=False)

class ManagedCopies(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        for k in ("plain", "dirty", "nes2", "ext"): put(managed(k), G[k]["file"])
        put(managed("wrong"), G["plain"]["content"][:20480] + G["ext"]["content"][:20480])   # same size as the right file, another game's bytes
        st = {k_id: store_record(k, managed(k)) for k, k_id in ((k, G[k]["id"]) for k in ("plain", "dirty", "nes2", "ext", "wrong"))}
        st[G["miss"]["id"]] = store_record("miss", os.path.join(LIB, "gone.unh"))
        ps.save_store(st)
        put(os.path.join(PERS, "p.nes"), PFILE)
        ps.save_index({"version": 1, "entries": {PID: {"id": PID, "content_sha256": sha(PC), "content_size": len(PC), "file_sha256": sha(PFILE), "file_size": len(PFILE), "format": "iNES",
                                                         "managed_copy": os.path.join(PERS, "p.nes"), "source_path": os.path.join(SRC, "my game.nes"),
                                                         "detected": {"emulator": "kachikachi", "core": "", "mapper": 0, "prg": 32768, "chr": 8192, "battery": False, "header_hex": OWN.hex()},
                                                         "user": {"title": "My Own Game"}, "file_name": "my game.nes"}}})
        put(os.path.join(SRC, "my game.nes"), PFILE)
        cls.before = {os.path.join(r, f): sha(open(os.path.join(r, f), "rb").read()) for r, _, fs in os.walk(TMP) for f in fs if f.endswith((".nes", ".unh"))}

    def setUp(self):
        db = os.path.join(WORK, "inventory.sqlite")
        if os.path.exists(db): os.remove(db)

    def stale_db(self):
        con = sqlite3.connect(os.path.join(WORK, "inventory.sqlite"))
        con.execute("CREATE TABLE payloads (sha256 TEXT, stored TEXT)"); con.execute("CREATE TABLE ident (sha256 TEXT, header_hex TEXT, converted_sha256 TEXT, mapper INT, prg INT, chr INT, battery INT)")
        for g in G.values():
            con.execute("INSERT INTO payloads VALUES (?, ?)", (g["sha"], os.path.join(TMP, "no-such-work-store", g["sha"][:2], g["sha"] + ".unh")))
            con.execute("INSERT INTO ident VALUES (?, ?, ?, ?, ?, ?, ?)", (g["sha"], g["ev"], sha(H(g["ev"]) + g["content"]), H(g["ev"])[6] >> 4, 0, 0, 0))
        con.commit(); con.close()

    def prepared(self, keys, personal=False):
        """plan, then prepare into a new disposable Hakchi tree: {game id: prepared file bytes}"""
        man = manifest(keys, personal); pv = hb.plan(man)
        self.assertTrue(pv["can_apply"], pv["problems"])
        tree = tempfile.mkdtemp(dir=TMP, prefix="hakchi-"); fref, _ = hb.factory_reference(os.path.join(SITE, "builtin.json"))
        os.makedirs(os.path.join(tree, "config")); json.dump({"gamesCollectionSettings": {"NES": {"SelectedGames": [], "OriginalGames": list(fref["codes"])}}}, open(os.path.join(tree, "config", "config.json"), "w"))
        cfg = {"hakchi_exe": os.path.join(tree, "hakchi.exe"), "staging_dir": os.path.join(tree + "-staging"), "rehearsal_tree": True, "hakchi_args": []}
        res = hb.apply(man, "managed.json", cfg, launch=False, tree_root=tree)
        self.assertTrue(res["ok"], res.get("message")); self.assertTrue(res["verification"]["match"], res["verification"])
        out = {}
        for code in os.listdir(os.path.join(tree, "games")):
            d = os.path.join(tree, "games", code); mk = json.load(open(os.path.join(d, hb.MARKER), encoding="utf-8"))
            out[mk["game_id"]] = open(os.path.join(d, code + ".nes"), "rb").read()
            self.assertEqual(code, hb.code_for(zlib.crc32(out[mk["game_id"]][16:]) & 0xffffffff), "the Hakchi code comes from the content")
        return pv, out

    def check_prepared(self, out, keys):
        for k in keys:
            g = G[k]; b = out[g["id"]]
            self.assertEqual(b, H(g["ev"]) + g["content"], f"{k}: the evidence header and the content, nothing else")
            self.assertEqual(len(b), 16 + len(g["content"]), f"{k}: one header")
            self.assertTrue(HP.nes_rom(b)["ok"], k)

    def test_01_no_development_database(self):
        self.assertFalse(os.path.exists(os.path.join(WORK, "inventory.sqlite")))
        keys = ["plain", "dirty", "nes2", "ext"]
        pv, out = self.prepared(keys, personal=True)
        self.check_prepared(out, keys)
        self.assertEqual(out[PID], PFILE, "a personal game keeps its own file as it is")
        items = {it["game"]["game_id"]: it for it in pv["custom"]}
        for k in keys: self.assertEqual(os.path.normcase(items[G[k]["id"]]["payload_path"]), os.path.normcase(managed(k)), k)
        self.assertEqual(pv["summary"]["install_bytes"], sum(16 + len(G[k]["content"]) for k in keys) + 16 + len(PC))
        for k in keys:   # the console plan's code and size agree with what preparation writes
            code, size, crc = EP._code_for_custom(hb, items[G[k]["id"]])
            self.assertEqual((size, crc), (len(G[k]["content"]), zlib.crc32(G[k]["content"]) & 0xffffffff), k)

    def test_02_stale_development_database(self):
        self.stale_db(); keys = ["plain", "dirty", "nes2", "ext"]
        pv, out = self.prepared(keys, personal=True)
        self.check_prepared(out, keys); self.assertEqual(out[PID], PFILE)

    def test_03_missing_and_wrong_refused_plainly(self):
        for db in (False, True):
            self.setUp()
            if db: self.stale_db()
            pv = hb.plan(manifest(["plain", "miss", "wrong"]))
            self.assertFalse(pv["can_apply"]); words = all_words(pv)
            self.assertNotIn("inventory.py", words); self.assertNotIn("work store", words); self.assertNotIn("no-such-work-store", words); self.assertNotIn(TMP.replace("\\", "/"), words.replace("\\\\", "/"))
            rows = {r["game_id"]: r for r in pv["games"]}
            self.assertIn("missing ROM payload", rows[G["miss"]["id"]]["statuses"]); self.assertIn("not on this computer", rows[G["miss"]["id"]]["detail"]); self.assertIn("Locate Game File", rows[G["miss"]["id"]]["detail"])
            self.assertIn("different version", rows[G["wrong"]["id"]]["detail"]); self.assertIn("Locate Game File", rows[G["wrong"]["id"]]["detail"])
            self.assertIn("ready to prepare", rows[G["plain"]["id"]]["statuses"])
            self.assertEqual(len(pv["problems"]), 2, pv["problems"])

    def test_04_locate_refreshes_the_next_plan(self):
        man = manifest(["plain", "miss", "wrong"])
        self.assertFalse(hb.plan(man)["can_apply"])
        for k in ("miss", "wrong"):   # the person's own files, chosen with Locate Game File (the wrong one's managed path is reused)
            src = put(os.path.join(SRC, f"{k} (located).unh"), G[k]["file"]); before = sha(open(src, "rb").read())
            r = ps.locate_version(G[k]["id"], src); self.assertEqual(r["version_id"], G[k]["id"])
            self.assertEqual(sha(open(src, "rb").read()), before, "the chosen file is never changed")
        self.assertEqual(os.path.normcase(ps.load_store()[G["wrong"]["id"]]["path"]), os.path.normcase(managed("wrong")))
        pv, out = self.prepared(["plain", "miss", "wrong"])
        self.check_prepared(out, ["plain", "miss", "wrong"])

    def test_05_console_package_resolver(self):
        for db in (False, True):
            self.setUp()
            if db: self.stale_db()
            for k in ("plain", "dirty", "nes2", "ext"):
                r = play.rom_for(G[k]["id"], k, {})
                self.assertTrue(r["playable"], (k, r)); self.assertEqual(os.path.normcase(r["rom"]), os.path.normcase(managed(k)), k); self.assertEqual(r["content_sha256"], G[k]["sha"])
                self.assertEqual(r["header_hex"], "" if G[k]["file"][:4] == b"NES\x1a" else G[k]["ev"], "the player's contract: a header for a headerless file only")
                ready = HP.nes_rom(open(r["rom"], "rb").read(), r["header_hex"])   # the Hakchi copy made from the console package's file
                self.assertTrue(ready["ok"], k); self.assertEqual(ready["ready"][16:], G[k]["content"], k); self.assertEqual(len(ready["ready"]), 16 + len(G[k]["content"]), k)
            self.assertFalse(play.rom_for(G["wrong"]["id"], "wrong", {})["playable"] if sha(open(managed("wrong"), "rb").read()) != G["wrong"]["sha"] else False)

    def test_99_sources_unchanged(self):
        for p, h in self.before.items():
            if os.path.exists(p) and os.path.dirname(p) != LIB: self.assertEqual(sha(open(p, "rb").read()), h, p)

if __name__ == "__main__":
    try: r = unittest.main(verbosity=2, exit=False).result
    finally: shutil.rmtree(TMP, ignore_errors=True)
    sys.exit(0 if r.wasSuccessful() else 1)
