r"""A Classic Cart Studio collection as ordinary Hakchi games and the CCS Collection Interface v1 index (console_engine/hakchi_publish.py and
its use by console_engine/ccs_install.py), PC only: every console here is a disposable folder standing in for a console that runs hakchi (no
device, socket or shell; a real console is refused by Gate G1 in code). The console program itself runs under qemu-arm with hakchi's own
code in tools/console-nucleus/test_nucleus.py (part "collections"); here its rules run as the stand-in's emulation.
  format     Hakchi's own conventions: the code from the game file (CRC-32 of PRG and CHR), the file Hakchi would store, the entry file's form,
             folders and Back, pictures fitted as Hakchi fits them, names Hakchi can carry; a game the console's own NES emulator does not run
             is left out with the reason
  projection stable ids, one entry per game in the first folder that lists it, built-in games as references, the plan's projection and
             review in plain words, the default (on when CCS Mod is not chosen)
  console    publication on hakchi's separate game storage, on a flat storage and without one; foreign entries and folders kept (an entry with
             the same code is never replaced); update and removal of only this program's entries; a changed entry kept and reported; no second
             copy of a game file the package already holds; the index's generation and its one-rename replacement; path safety of everything
             staged and indexed; uninstall removes the entries first; a later plan without Hakchi games removes them
  games      without CCS Mod: only the game files the entries launch where they are and their notices (an Included Game's license text
             with it), never CCS HOME (nothing under pkg/, no pointer, data, player or tile); each game file once on the console; the copy's
             own record, verification and removal; a changed copy replaced; adding CCS Mod later moves the entries into CCS HOME's copy and
             removes the games copy; a CCS HOME already there kept as it is; every change failing in turn undone or finished; a crash
             recovered; neither CCS Mod nor Hakchi games refused; the rehearsals
  pins       the console program's pins equal here, in the transport worker and from the sources
  python tools/companion/test_hakchi_publish.py      Exit 0 = PASS."""
from __future__ import annotations
import hashlib, io, json, os, re, shutil, sys, tempfile, unittest, zlib
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path[:0] = [HERE, os.path.join(HERE, "..", "rom-pipeline")]
import console_test_guard   # noqa: E402,F401  (first: the controlled console boundary)
from console_engine import ccs_install as CI, hakchi_publish as HP   # noqa: E402
from test_ccs_install import rd, wr, tree, changes   # noqa: E402  (also sets the chooser's stand-in inputs)

def sha(b): return hashlib.sha256(b).hexdigest()
def rom(mapper: int = 0, battery: bool = False, seed: int = 1, prg: int = 1, chr_: int = 1, header: bool = True, trailer: bytes = b"", flags7: int | None = None) -> bytes:
    """a synthetic NES game file made for these tests (pseudo-random data, not a game)"""
    h = bytearray(16); h[0:4] = b"NES\x1a"; h[4], h[5] = prg, chr_; h[6] = ((mapper & 0x0F) << 4) | (2 if battery else 0); h[7] = (mapper & 0xF0) if flags7 is None else flags7
    body = bytes((seed * 7 + i * 13) & 0xFF for i in range(prg * 16384 + chr_ * 8192))
    return (bytes(h) if header else b"") + body + trailer
def cover(color=(200, 40, 40), size=(300, 420)) -> bytes:
    from PIL import Image
    out = io.BytesIO(); Image.new("RGB", size, color).save(out, "JPEG", quality=90); return out.getvalue()

def make_package(d: str, games: list[dict], folders: list | None = None, favorites: list | None = None, label: str = "My Shelf", native: dict | None = None, copies: bool = True) -> str:
    """a console package in CCS HOME's layout (mod) with these games: [{"id", "title", "rom": bytes | None, "header_hex", "builtin_code",
    "cover": bytes | None, "players", "year", "publisher"}]. native: {game id: JPEG}, this computer's pictures Original Menu's covers are
    made from (PC.json and native-covers/, named by the manifest's pc_sha256, as native_home/console_package.py writes them). copies: also
    this computer's Hakchi-ready copies too large for one message (PC.json hakchi_copies and hakchi-copies/, as console_package.py writes them;
    False: a package made before it did)"""
    files: dict[str, bytes] = {"bin/ccs-player-armv7": b"\x7fELF\x01 stand-in player", "config/controller-map.txt": b"key 304 a\n", "home/pack.json": b'{"schema":"ccs-native-home-pack/2"}'}
    sess_games, gdoc = [], []
    for g in games:
        sg = {"id": g["id"], "title": g["title"], "rom": "", "header_hex": g.get("header_hex", ""), "content_sha256": "", "unavailable": ""}
        if g.get("rom") is not None:
            b = g["rom"]; skip = 16 if b[:4] == b"NES\x1a" else 0; cs = sha(b[skip:]); rel = f"roms/{cs}.nes"; files[rel] = b
            sg.update(rom=f"/tmp/ccs/pkg/{rel}", content_sha256=cs)
        elif not g.get("builtin_code"): sg["unavailable"] = "The game file for this game is not in this package."
        sess_games.append(sg)
        rec = {"id": g["id"], "title": g["title"], "year": g.get("year"), "players": g.get("players"), "publisher": g.get("publisher"), "builtin_code": g.get("builtin_code"),
               "home_kind": "factory" if g.get("builtin_code") else "added", "cover": None}
        if g.get("cover"): files[f"home/cover/{sha(g['cover'])[:16]}.jpg"] = g["cover"]; rec["cover"] = f"cover/{sha(g['cover'])[:16]}.jpg"
        gdoc.append(rec)
    files["home/games.json"] = json.dumps({"schema": "ccs-native-home-games/2", "games": gdoc}).encode()
    files["home/config.json"] = json.dumps({"schema": "ccs-native-home-config/1", "collection": {"name": label}, "games": [g["id"] for g in games], "folders": folders or [], "favorites": favorites or []}).encode()
    files["session.json"] = json.dumps({"schema": "ccs-play-session/1", "games": sess_games}).encode()
    for rel, b in files.items(): wr(os.path.join(d, *rel.split("/")), b)
    man = {"schema": "ccs-console-package/1", "layout": "mod", "label": label, "console_paths": {"pkg": "/tmp/ccs/pkg", "data": "/var/lib/ccs/data"}, "games": len(games), "playable": len(games),
           "files": {rel: {"size": len(b), "sha256": sha(b), "kind": "x", "mode": "0o755" if rel.startswith("bin/") else "0o644"} for rel, b in files.items()}}
    hc = {}
    for g in games if copies else []:
        if g.get("rom") is None: continue
        r = HP.nes_rom(g["rom"], g.get("header_hex", ""))
        if r.get("ok") and not r["same"] and len(r["ready"]) > HP.INLINE_MAX: hc[f"{HP.COPY_DIR}/{sha(r['ready'])[:16]}.nes"] = r["ready"]
    if native is not None or hc:
        pc = {f"native-covers/{sha(b)[:16]}.jpg": b for b in (native or {}).values()} | hc
        for rel, b in pc.items(): wr(os.path.join(d, *rel.split("/")), b)
        pcb = json.dumps({"schema": "ccs-console-package-pc/1", "native_covers": {g: f"native-covers/{sha(b)[:16]}.jpg" for g, b in (native or {}).items()},
                          **({"hakchi_copies": {sha(b): rel for rel, b in hc.items()}} if hc else {}),
                          "files": {rel: {"size": len(b), "sha256": sha(b)} for rel, b in pc.items()}}).encode()
        wr(os.path.join(d, HP.PC_FILE), pcb); man["pc_sha256"] = sha(pcb)
    wr(os.path.join(d, "MANIFEST.json"), json.dumps(man)); return d

GAMES = [{"id": "g1", "title": "Alpha Quest", "rom": rom(0, seed=1), "cover": cover(), "players": "1-2", "year": "1990", "publisher": "Studio A"},
         {"id": "g2", "title": "Beta's 2nd Run", "rom": rom(4, battery=True, seed=2), "cover": None, "players": "1", "year": "1991"},
         {"id": "g3", "title": "Gamma", "rom": rom(30, seed=3), "players": "1"},                                  # mapper 30: not for the console's own emulator
         {"id": "g4", "title": "Delta Headerless", "rom": rom(1, seed=4)[16:], "header_hex": rom(1, seed=4)[:16].hex(), "players": "2"},
         {"id": "g5", "title": "Epsilon", "rom": rom(2, seed=5, flags7=0x08), "players": "1"},                  # a NES 2.0 header: Hakchi stores its own copy
         {"id": "g6", "title": "A built-in game", "builtin_code": "CLV-P-NAAAE"},
         {"id": "g7", "title": "Missing", "rom": None}]
FOLDERS = [{"id": "fa", "name": "Action", "parent": None, "pos": 0, "order": ["g1", "g2"]}, {"id": "fb", "name": "Favorites Too", "parent": None, "pos": 1, "order": ["g2", "g6"]},
           {"id": "fc", "name": "Inner", "parent": "fa", "pos": 0, "order": ["g4"]}]

class Format(unittest.TestCase):
    def test_01_hakchi_code_and_rom_rules(self):
        b = rom(0, seed=9); r = HP.nes_rom(b)
        self.assertTrue(r["ok"]); self.assertTrue(r["same"], "a clean iNES file is stored by Hakchi as it is: the package's file is used where it is")
        crc = zlib.crc32(b[16:]) & 0xFFFFFFFF
        self.assertEqual(HP.hakchi_code(crc), "CLV-H-" + "".join(chr(65 + ((crc >> s) % 26)) for s in (0, 5, 10, 15, 20)), "NesApplication.GenerateCode")
        self.assertEqual(r["crc32"], crc)
        self.assertFalse(HP.nes_rom(rom(30))["ok"]); self.assertIn("does not run its cartridge type", HP.nes_rom(rom(30))["why"])
        four = bytearray(rom(0)); four[6] |= 8; self.assertFalse(HP.nes_rom(bytes(four))["ok"], "four-screen boards are not for the console's own emulator (NesGame.cs)")
        tr = bytearray(rom(0)); tr[6] |= 4; self.assertFalse(HP.nes_rom(bytes(tr) + bytes(512))["ok"])
        self.assertFalse(HP.nes_rom(b"NES\x1a" + bytes(12))["ok"]); self.assertFalse(HP.nes_rom(rom(0)[:5000])["ok"]); self.assertFalse(HP.nes_rom(b"not a game")["ok"])
        n2 = rom(2, flags7=0x08); r2 = HP.nes_rom(n2); self.assertTrue(r2["ok"]); self.assertFalse(r2["same"]); self.assertEqual(r2["ready"][7], 0, "Hakchi's GetRaw header (no NES 2.0 marks)")
        self.assertEqual(r2["ready"][16:], n2[16:])
        hl = rom(1, seed=4); r3 = HP.nes_rom(hl[16:], hl[:16].hex()); self.assertTrue(r3["ok"]); self.assertFalse(r3["same"], "a file kept without its header gets the Hakchi-ready copy")
        arch = bytearray(rom(1)); arch[7] = 0x40; arch[12:16] = b"Dude"; ra = HP.nes_rom(bytes(arch)); self.assertEqual(ra["mapper"], 1, "an archaic header's bytes 7-15 are ignored, as Hakchi's NesFile does")
        self.assertTrue(HP.nes_rom(rom(0) + b"junk")["ok"]); self.assertFalse(HP.nes_rom(rom(0) + b"junk")["same"], "bytes after PRG and CHR: Hakchi stores without them")
        bat = HP.nes_rom(rom(4, battery=True)); self.assertTrue(bat["battery"])

    def test_02_entry_files_are_hakchis_form(self):
        g = {"code": "CLV-H-ABCDE", "name": "The Game", "players": 2, "release": "1990-01-01", "save_count": 3, "publisher": "Studio"}
        t = HP.desktop_game(g, "/var/lib/ccs/pkg/0123456789abcdef.0a1b2c3d/roms/x.nes").decode()
        self.assertTrue(t.startswith("[Desktop Entry]\nType=Application\nExec=/bin/clover-kachikachi-wr /var/lib/ccs/pkg/0123456789abcdef.0a1b2c3d/roms/x.nes --guest-overscan-dimensions 0,0,9,3"))
        for line in ("Path=/var/saves/CLV-H-ABCDE", "Name=The Game", "Icon=/var/games/CLV-H-ABCDE/CLV-H-ABCDE.png", "Code=CLV-H-ABCDE", "TestID=0", "Players=2", "Simultaneous=1",
                     "SaveCount=3", "SortRawTitle=game", "SortRawPublisher=STUDIO", "[Description]\nText = \n"):
            self.assertIn(line, t)
        f = HP.desktop_folder("CLV-S-ABCDE", "Action", "ccs-0123456789").decode()
        self.assertIn("Exec=/bin/chmenu ccs-0123456789 /var/games\nPath=/var/saves/FOLDER\n", f); self.assertIn("TestID=777", f); self.assertIn("SortRawTitle=\u042daction", f)
        b = HP.desktop_folder("CLV-S-00000", "Back", "000", back=True).decode(); self.assertIn("Exec=/bin/chmenu 000 /var/games", b); self.assertIn("ReleaseDate=9999-99-99", b)
        self.assertEqual(HP.clean_name("Mega\nMan '2\x00"), "Mega Man `2", "one line; an apostrophe before a digit is a back quote (NesApplication.cs)")
        self.assertEqual(HP.plain_name("Mega\nMan '2"), "Mega Man '2", "the index keeps the title itself")
        self.assertEqual(HP.sort_title("The Legend"), "legend")
        from PIL import Image
        big, small = HP.art_pngs(cover(size=(300, 420)))
        bi, si = Image.open(io.BytesIO(big)), Image.open(io.BytesIO(small))
        self.assertEqual(bi.format, "PNG"); self.assertLessEqual(bi.size[0], 228); self.assertEqual(bi.size[1], 204); self.assertEqual(bi.size[0] % 2, 0)
        self.assertLessEqual(max(si.size), 40)
        self.assertEqual(HP._fit(100, 50, 228, 204, True), (228, 114)); self.assertEqual(HP._fit(30, 30, 40, 40, False), (30, 30))
        fb, fs = HP.art_pngs(None, "folder"); self.assertEqual(Image.open(io.BytesIO(fb)).format, "PNG")

    def test_02e_held_palette_covers_built_but_not_used(self):
        from PIL import Image
        from console_engine import wu_quantize as WQ
        self.assertEqual(HP.NATIVE_COVERS, "standard", "Original Menu keeps its standard pictures until the palette ones are compared on the console and approved")
        def kind(b): return b[24:26]   # PNG bit depth and color type
        def chunk_len(b, name): i = b.find(name); return int.from_bytes(b[i - 4:i], "big") if i > 0 else None
        src = cover(size=(300, 420)); grad = io.BytesIO(); g = Image.new("RGB", (300, 420)); px = g.load()
        for y in range(420):
            for x in range(300): px[x, y] = (x * 255 // 300, y * 255 // 420, (x + y) % 256)
        g.save(grad, "JPEG", quality=92); grad = grad.getvalue()
        for s in (src, grad):
            self.assertEqual(HP.art_pngs(s), HP.art_pngs(s, variant="standard"))
            st, ss = HP.art_pngs(s); pa, ps = HP.art_pngs(s, variant="palette")
            self.assertEqual(kind(st), bytes([8, 2]), "standard: the picture's own RGB colors")
            for b, ref in ((pa, st), (ps, ss)):
                self.assertEqual(kind(b), bytes([8, 3]), "palette: an 8-bit indexed PNG, the mode Hakchi2 CE writes"); self.assertEqual((chunk_len(b, b"PLTE"), chunk_len(b, b"tRNS")), (768, 256))
                self.assertEqual(Image.open(io.BytesIO(b)).size, Image.open(io.BytesIO(ref)).size, "the same sizes as the standard pictures")
            im = Image.open(io.BytesIO(s)).convert("RGB"); big = im.resize(HP._fit(im.width, im.height, *HP.COVER_BOX, True), Image.Resampling.BICUBIC).convert("RGBA")
            pal, idx = WQ.quantize(big.tobytes(), big.width, big.height)
            self.assertEqual(Image.open(io.BytesIO(pa)).convert("RGBA").tobytes(), b"".join(bytes(pal[i]) for i in idx), "it decodes to exactly the quantizer's colors")
            self.assertLessEqual(len(pal), 256)
        self.assertLess(len(HP.art_pngs(grad, variant="palette")[0]), len(HP.art_pngs(grad)[0]))
        for k in ("folder", "back"):
            b, sm = HP.art_pngs(None, k, "palette"); d = Image.open(io.BytesIO(b)).convert("RGBA")
            self.assertEqual(kind(b), bytes([8, 3])); self.assertEqual(d.getpixel((0, 0))[3], 0, "transparency kept")
        with self.assertRaises(ValueError): HP.art_pngs(src, variant="webp")
        self.assertEqual(WQ.quantize(bytes([10, 20, 30, 255]) * 4, 2, 2)[1], bytes(4), "one color: every pixel the first entry")
        self.assertEqual(WQ.quantize(bytes([10, 20, 30, 5]) * 4, 2, 2), ([(0, 0, 0, 0), (0, 0, 0, 0)], bytes([1]) * 4),
                         "below the alpha threshold: nQuant's one empty color, then its transparent color, which every pixel takes")

class Projection(unittest.TestCase):
    def setUp(self): self.t = tempfile.mkdtemp(prefix="ccs-hp-"); self.pkg = make_package(os.path.join(self.t, "pkg"), GAMES, FOLDERS, ["g1", "g9"])
    def tearDown(self): shutil.rmtree(self.t, ignore_errors=True)

    def test_02b_no_picture_for_the_sega_mini_menu_in_any_entry(self):
        sp = HP.spec(self.pkg)
        for mode in ("storage", "overlay"):
            out = HP.layout(sp, mode, "0123456789abcdef.0a1b2c3d")
            kinds = {e["kind"] for e in out["entries"]}
            if mode == "storage": self.assertTrue({"game", "folder", "back"} <= kinds, "games, folders and Back entries are all checked")
            for e in out["entries"]:
                self.assertTrue(set(e["files"]) <= set(HP.entry_names(e["code"])), (mode, e["kind"], sorted(e["files"])))
                self.assertFalse(any(n.lower().endswith("_mdmini.png") for n in e["files"]), (mode, e["kind"]))
        bad = {"mode": "overlay", "menus": [], "entries": [{"kind": "game", "menu": "root", "code": "CLV-H-ABCDE", "files": {"CLV-H-ABCDE.png": b"x", "CLV-H-ABCDE_mdmini.png": b"x"}}]}
        with self.assertRaises(HP.PublishError): HP._entries_checked(bad)
        self.assertNotIn("mdmini", HP.entry_names("CLV-H-ABCDE"), "no inclusion: the only entry files are Hakchi's own form")
        studio = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "rom-pipeline", "studio")
        offers = [n for n in os.listdir(studio) if n.endswith((".js", ".html", ".css", ".json")) and re.search(r"mdmini|Sega Mini", open(os.path.join(studio, n), encoding="utf-8").read(), re.I)]
        self.assertEqual(offers, [], "no Studio page, script or setting offers pictures for the Sega Mini menu")

    def test_02f_original_menu_covers_from_this_computers_copy(self):
        native = cover(color=(10, 200, 10), size=(340, 466)); pkg = make_package(os.path.join(self.t, "pkgn"), GAMES, FOLDERS, ["g1"], native={"g1": native})
        sp = HP.spec(pkg); lay = HP.layout(sp, "storage", "0123456789abcdef.0a1b2c3d")
        g1 = next(g for g in sp["games"] if g["ccs_id"] == "g1"); e = next(x for x in lay["entries"] if x["code"] == g1["code"])
        self.assertEqual(e["files"][g1["code"] + ".png"], HP.art_pngs(native)[0], "made from the picture PC.json names")
        self.assertNotEqual(e["files"][g1["code"] + ".png"], HP.art_pngs(GAMES[0]["cover"])[0])
        p = CI.build_plan(pkg, {"mod": True}, True); sent = [f["path"] for f in p["files"]]
        self.assertFalse([x for x in sent if x == HP.PC_FILE or x.startswith("native-covers/")], "this computer's pictures are never sent")
        self.assertIn(HP.PC_FILE, p["package_files"]); self.assertTrue(any(x.startswith("native-covers/") for x in p["package_files"]))
        self.assertEqual(sum(f["size"] for f in p["files"]), p["bytes"])
        pic = os.path.join(pkg, *next(x for x in p["package_files"] if x.startswith("native-covers/")).split("/")); keep = rd(pic)
        wr(pic, keep + b"x")
        with self.assertRaises(CI.PlanError): CI.build_plan(pkg, {"mod": True}, True)
        wr(pic, keep); pcf = os.path.join(pkg, HP.PC_FILE); pcb = rd(pcf); os.remove(pcf)
        with self.assertRaises(CI.PlanError): CI.build_plan(pkg, {"mod": True}, True)
        with self.assertRaises(HP.PublishError): HP.spec(pkg)
        wr(pcf, pcb); self.assertTrue(CI.build_plan(pkg, {"mod": True}, True)["hakchi"]["publish"])

    def test_03_projection_ids_placement_and_reasons(self):
        sp = HP.spec(self.pkg); by = {g["ccs_id"]: g for g in sp["games"]}
        self.assertEqual(sp, HP.spec(self.pkg), "the same package gives the same projection")
        self.assertEqual(by["g1"]["status"], "publish"); self.assertIsNone(by["g1"]["copy"], "its file is used where it is")
        self.assertEqual(by["g1"]["rom"], f"roms/{by['g1']['content_sha256']}.nes"); self.assertEqual(by["g1"]["id"], "nes-" + by["g1"]["content_sha256"][:32])
        self.assertEqual(by["g2"]["save_count"], 3, "a battery game keeps Hakchi's three save slots")
        self.assertEqual((by["g3"]["status"], by["g3"]["reason"][:40]), ("skip", "Nintendo's built-in NES emulator, which "))
        self.assertIsNotNone(by["g4"]["copy"]); self.assertIsNotNone(by["g5"]["copy"])
        self.assertEqual((by["g6"]["kind"], by["g6"]["code"], by["g6"]["id"]), ("builtin", "CLV-P-NAAAE", "nes-builtin-clv-p-naaae"))
        self.assertEqual(by["g7"]["status"], "skip")
        # one entry per game: in the first folder (browse order) that lists it; a folder's folder after its parent
        menus = {f["ccs_id"]: f["menu"] for f in sp["folders"]}
        self.assertEqual([f["ccs_id"] for f in sp["folders"]], ["fa", "fc", "fb"])
        self.assertEqual((by["g1"]["menu"], by["g2"]["menu"], by["g4"]["menu"], by["g6"]["menu"]), (menus["fa"], menus["fa"], menus["fc"], menus["fb"]))
        self.assertEqual(by["g5"]["menu"], sp["collection"]["menu"], "a game in no folder is in the collection's own folder")
        self.assertEqual(sp["favorites"], [by["g1"]["id"]], "favorites name only the collection's games")
        codes = [g["code"] for g in sp["games"] if g["status"] == "publish"] + [f["code"] for f in sp["folders"]] + [sp["collection"]["code"]]
        self.assertEqual(len(codes), len(set(codes))); self.assertTrue(all(HP.CODE_RE.match(c) for c in codes))
        self.assertTrue(all(HP.MENU_RE.match(f["menu"]) for f in sp["folders"]))
    def test_04_codes_collide_deterministically_and_duplicates_are_one_entry(self):
        same = rom(0, seed=11); other = bytearray(same); other[16 + 100] ^= 1   # a different file whose CRC-32 is made to collide is unlikely: force it
        d = make_package(os.path.join(self.t, "dup"), [{"id": "a", "title": "A", "rom": same}, {"id": "b", "title": "B", "rom": same}, {"id": "c", "title": "C", "rom": bytes(other)}])
        orig = HP.hakchi_code; crcs = {zlib.crc32(same[16:]) & 0xFFFFFFFF, zlib.crc32(bytes(other)[16:]) & 0xFFFFFFFF}
        try:
            HP.hakchi_code = lambda crc, prefix="H": "CLV-H-SAMEE" if crc in crcs else orig(crc, prefix)
            sp = HP.spec(d)
        finally: HP.hakchi_code = orig
        pub = [g for g in sp["games"] if g["status"] == "publish"]
        self.assertEqual(len(pub), 2, "the same game file listed twice is one entry"); self.assertEqual(pub[0]["code"], "CLV-H-SAMEE"); self.assertNotEqual(pub[1]["code"], "CLV-H-SAMEE")
    def test_04b_a_copy_larger_than_one_message_comes_from_the_package(self):
        """the required case: an original NTSC MMC3 game (mapper 4, 512 KiB PRG, 256 KiB CHR, 8 KiB battery RAM, no trainer), 786,448 bytes,
        as Hakchi stores it already (launched where it is), with a header Hakchi rewrites (iNES byte 8; NES 2.0) and kept without its header"""
        canon = bytes.fromhex("4e45531a202042000000000000000000")   # the header Hakchi stores: 32 x 16 KiB PRG, 32 x 8 KiB CHR, mapper 4, battery
        big = {k: rom(4, battery=True, seed=s, prg=32, chr_=32) for k, s in (("same", 5), ("ram", 7), ("nes2", 8), ("headerless", 9))}
        for k, b in big.items(): self.assertEqual((len(b), b[:16]), (786_448, canon), k)
        files = {"same": big["same"], "ram": big["ram"][:8] + b"\x01" + big["ram"][9:],                  # iNES 1.0 byte 8: 8 KiB of PRG RAM
                 "nes2": big["nes2"][:7] + b"\x08\x00\x00\x70" + big["nes2"][11:]}                       # NES 2.0: 8 KiB of battery-backed PRG RAM (64 << 7)
        games = [{"id": k, "title": k, "rom": v} for k, v in files.items()] + [{"id": "headerless", "title": "headerless", "rom": big["headerless"][16:], "header_hex": canon.hex()},
                                                                                 {"id": "small", "title": "Small", "rom": rom(0, seed=6)[:8] + b"\x01" + rom(0, seed=6)[9:]}]
        d = make_package(os.path.join(self.t, "big"), games)
        roms = [r for r in json.loads(rd(os.path.join(d, "MANIFEST.json")))["files"] if r.startswith("roms/")]; before = {r: sha(rd(os.path.join(d, *r.split("/")))) for r in roms}
        sp = HP.spec(d); by = {g["ccs_id"]: g for g in sp["games"]}
        self.assertTrue(all(g["status"] == "publish" for g in by.values()), [(g["ccs_id"], g["reason"]) for g in by.values()])
        self.assertIsNone(by["same"]["copy"], "a file already in Hakchi's form is launched where it is: no copy"); self.assertNotIn("copy_source", by["same"])
        for k in ("ram", "nes2", "headerless"):
            g = by[k]; self.assertEqual(g["copy"], canon + big[k][16:], f"{k}: Hakchi's header and the game's own PRG and CHR, exactly"); self.assertEqual(len(g["copy"]), 786_448)
            self.assertTrue(g["copy_source"].startswith("hakchi-copies/"), k); self.assertEqual(rd(os.path.join(d, *g["copy_source"].split("/"))), g["copy"], k)
            self.assertEqual(g["save_count"], 3, "a battery game: Hakchi's save slots"); self.assertEqual(g["code"], HP.hakchi_code(zlib.crc32(big[k][16:]) & 0xFFFFFFFF))
        self.assertIsNotNone(by["small"]["copy"]); self.assertNotIn("copy_source", by["small"], "a small copy is still sent within its message")
        lay = HP.layout(sp, "storage", "0" * 16 + "." + "0" * 8); src = HP.stage_sources(lay); staged = HP.stage_files(lay)
        self.assertEqual(sorted(src.values()), sorted(by[k]["copy_source"] for k in ("ram", "nes2", "headerless")))
        self.assertTrue(all(staged[n] == rd(os.path.join(d, *s.split("/"))) for n, s in src.items()), "each staged copy is the package's own copy")
        self.assertEqual({r: sha(rd(os.path.join(d, *r.split("/")))) for r in roms}, before, "the package's own game files are never changed")
        # a package made before the copies were kept, or one whose copy changed: left out with the way to fix it, never sent another way
        old = make_package(os.path.join(self.t, "old"), [{"id": "ram", "title": "ram", "rom": files["ram"]}], copies=False)
        g = HP.spec(old)["games"][0]; self.assertEqual(g["status"], "skip"); self.assertIn("prepare it again", g["reason"])
        bad = make_package(os.path.join(self.t, "bad"), [{"id": "ram", "title": "ram", "rom": files["ram"]}]); cp_ = os.path.join(bad, *HP.spec(bad)["games"][0]["copy_source"].split("/"))
        man = json.loads(rd(os.path.join(bad, "MANIFEST.json"))); listed = {r: i["sha256"] for r, i in man["files"].items()} | {HP.PC_FILE: man["pc_sha256"], "MANIFEST.json": sha(rd(os.path.join(bad, "MANIFEST.json")))} | {r: i["sha256"] for r, i in json.loads(rd(os.path.join(bad, HP.PC_FILE)))["files"].items()}
        with open(cp_, "r+b") as f: f.seek(100); f.write(b"\xff")
        with self.assertRaises(HP.PublishError, msg="the plan's checked list: a changed file stops the projection"): HP.spec(bad, listed)
        g = HP.spec(bad)["games"][0]; self.assertEqual(g["status"], "skip", "a copy that is not exactly the bytes is not used"); self.assertIn("prepare it again", g["reason"])
        self.assertEqual(HP.INLINE_MAX, 600_000); from console_engine import worker_console as WC; self.assertLessEqual(HP.INLINE_MAX, WC.MAX_B64)
    def test_05_plan_default_review_and_flag(self):
        p = CI.build_plan(self.pkg, {"mod": False})
        self.assertTrue(p["hakchi"]["publish"], "Studio's default: Hakchi games when CCS Mod is not chosen"); self.assertIn("hakchi", p["steps"])
        self.assertFalse(CI.build_plan(self.pkg)["hakchi"]["publish"], "with CCS Mod (Studio's default choice) they are not added unless asked for")
        self.assertTrue(CI.build_plan(self.pkg, {"mod": True}, True)["hakchi"]["publish"])
        with self.assertRaises(CI.Refused): CI.build_plan(self.pkg, {"mod": False}, False)   # without CCS Mod the Hakchi games are how the collection reaches the console
        with self.assertRaises(CI.Refused): CI.build_plan(self.pkg, {"mod": False}, "yes")
        rv = p["hakchi"]["review"]
        self.assertEqual(rv["games"], 5); self.assertEqual(rv["builtin"], 1); self.assertEqual(len(rv["left_out"]), 2)
        self.assertTrue(p["hakchi_text"].startswith('Adds this collection\'s 5 games to Original Menu as Hakchi games, in a folder named "My Shelf". '), p["hakchi_text"])
        self.assertTrue(any("each game once" in n for n in p["notes"])); self.assertTrue(any("Hakchi2 CE" in n for n in p["notes"]))
        self.assertTrue(any(n.startswith("2 games cannot be added as Hakchi games: Gamma (") for n in p["notes"]))
        for g in p["hakchi"]["projection"]["games"]: self.assertFalse({"art", "copy"} & set(g), "the plan carries no picture or file bytes, only their SHA-256")
        json.dumps(p)   # (the whole plan is JSON)
        self.assertGreater(CI.build_plan(self.pkg, {"mod": True}, True)["needs_free_bytes"], CI.build_plan(self.pkg, {"mod": True}, False)["needs_free_bytes"])
        for line in p["notes"] + [p["hakchi_text"]]: self.assertNotRegex(line, r"tested|verified on|qualified", "no device-testing account in product text")

class Console(unittest.TestCase):
    def setUp(self):
        self.t = tempfile.mkdtemp(prefix="ccs-hp-"); self.pkg = make_package(os.path.join(self.t, "pkg"), GAMES, FOLDERS, ["g1"]); self.n = 0
        self.jd = os.path.join(self.t, "j"); os.makedirs(self.jd)
    def tearDown(self): shutil.rmtree(self.t, ignore_errors=True)
    def console(self, profile="hakchi-ready", name="c"):
        d = os.path.join(self.t, name); CI.make_stand_in(d, profile); return d
    def run_plan(self, root, plan, pkg=None):
        self.n += 1; return CI.Installer(CI.FakeConsole(root), plan, pkg or self.pkg, os.path.join(self.jd, f"j{self.n}.jsonl")).run()
    def index(self, root): return json.loads(rd(os.path.join(root, "var/lib/ccs/integration/v1/index.json")))
    def foreign(self, root): return {k: v for k, v in tree(root).items() if "/hakchi/games/" in "/" + k and "ccs-" not in k and v != "dir"}

    def test_06b_this_computers_pictures_stay_here(self):
        native = cover(color=(10, 200, 10), size=(340, 466)); pkg = make_package(os.path.join(self.t, "pkgn"), GAMES, FOLDERS, ["g1"], native={"g1": native})
        root = self.console(); r = self.run_plan(root, CI.build_plan(pkg, {"mod": False}), pkg); self.assertTrue(r["ok"], r)
        names = [k for k in tree(root) if k.rsplit("/", 1)[-1] == HP.PC_FILE or "native-covers" in k]
        self.assertEqual(names, [], "PC.json and the pictures it lists never reach the console")
        code = next(g["code"] for g in HP.spec(pkg)["games"] if g["ccs_id"] == "g1")
        pngs = [v for k, v in tree(root).items() if k.endswith("/" + code + ".png")]
        self.assertTrue(pngs and all(v == sha(HP.art_pngs(native)[0]) for v in pngs), "Original Menu's cover made from this computer's picture")

    def test_06c_a_large_hakchi_copy_is_sent_from_the_package(self):
        """the required 786,448-byte MMC3 game whose header Hakchi rewrites: published without CCS Mod, its Hakchi entry holds exactly Hakchi's
        header and the game's own PRG and CHR; the stage file is put naming the package's own copy (the transport worker streams it and checks its
        SHA-256); the source files are unchanged and the stage is emptied"""
        big = rom(4, battery=True, seed=21, prg=32, chr_=32); src = big[:8] + b"\x01" + big[9:]
        pkg = make_package(os.path.join(self.t, "pkg-big"), [{"id": "mmc3", "title": "Large Test Game", "rom": src, "players": "1", "year": "2026"}])
        keep = {k: v for k, v in tree(pkg).items()}
        puts = []
        class Rec(CI.FakeConsole):
            def put(self, path, data, mode="0o644", source=None): puts.append((path, len(data), source)); return super().put(path, data, mode, source)
        root = self.console(); self.n += 1; p = CI.build_plan(pkg, {"mod": False})
        r = CI.Installer(Rec(root), p, pkg, os.path.join(self.jd, f"j{self.n}.jsonl")).run(); self.assertTrue(r["ok"], r); self.assertTrue(r["hakchi"]["ok"], r["hakchi"])
        g = HP.spec(pkg)["games"][0]; code = g["code"]
        entry = os.path.join(root, "var/lib/hakchi/games/nes-usa", g["menu"], code)
        want = bytes.fromhex("4e45531a202042000000000000000000") + big[16:]
        got = rd(os.path.join(entry, code + ".nes")); self.assertEqual((len(got), sha(got)), (786_448, sha(want)), "exactly Hakchi's form of the game")
        self.assertIn(f"Exec=/bin/clover-kachikachi-wr /var/games/{code}/{code}.nes ", rd(os.path.join(entry, code + ".desktop")).decode())
        sent = [x for x in puts if x[0].endswith(f"/{code}/{code}.nes")]
        self.assertEqual(len(sent), 1); self.assertEqual(sent[0][1], 786_448)
        self.assertEqual(os.path.normcase(sent[0][2] or ""), os.path.normcase(os.path.join(pkg, *g["copy_source"].split("/"))), "named by its place in the package")
        self.assertTrue(all(s is None for (pth, n, s) in puts if not pth.endswith(f"/{code}.nes") and n <= HP.INLINE_MAX and "/integration/stage/" in pth), "everything else staged is sent within its message")
        self.assertEqual(tree(pkg), keep, "the package (its game file and its copy) is never changed")
        self.assertFalse(os.path.exists(os.path.join(root, "var/lib/ccs/integration/stage")), "the stage folder is emptied and removed")
        self.assertTrue(CI.verify_installed(CI.FakeConsole(root), p)["verified"])
        self.assertEqual(self.index(root)["games"][g["id"]]["status"], "published")

    def test_06_storage_publication_foreign_kept_no_second_copy(self):
        root = self.console(); stock = CI.stock_digest(root) | CI.storage_digest(root); fb = self.foreign(root)
        p = CI.build_plan(self.pkg, {"mod": False}); r = self.run_plan(root, p); self.assertTrue(r["ok"], r)
        hk = r["hakchi"]; self.assertTrue(hk["ok"], hk); self.assertEqual((hk["mode"], hk["published"]), ("storage", 5)); self.assertEqual(CI.stock_digest(root) | CI.storage_digest(root), stock, "another tool's games and folders unchanged")
        self.assertEqual({k: v for k, v in self.foreign(root).items() if k in fb}, fb)
        gs = os.path.join(root, "var/lib/hakchi/games/nes-usa"); sp = HP.spec(self.pkg)
        col = sp["collection"]; d0 = os.path.join(gs, "000", col["code"])
        self.assertTrue(os.path.isfile(os.path.join(d0, col["code"] + ".desktop")), "the collection's folder entry in the first page")
        self.assertIn(f"Exec=/bin/chmenu {col['menu']} /var/games", rd(os.path.join(d0, col["code"] + ".desktop")).decode())
        by = {g["ccs_id"]: g for g in sp["games"]}; g1 = by["g1"]
        e1 = os.path.join(gs, g1["menu"], g1["code"]); desk = rd(os.path.join(e1, g1["code"] + ".desktop")).decode()
        self.assertEqual(p["delivery"], "games", "without CCS Mod: the game files only (games_only.py)")
        self.assertIn(f"Exec=/bin/clover-kachikachi-wr /var/lib/ccs/games/{r['copy']}/roms/{g1['rom'][5:]} ", desk, "the installed copy's own game file: no second copy")
        self.assertEqual(sorted(os.listdir(e1)), sorted([g1["code"] + x for x in (".desktop", ".png", "_small.png")]))
        nes = [k for k in tree(root) if k.endswith(".nes") and "/hakchi/games/" in k and "AAAA" not in k]
        self.assertEqual(sorted(os.path.basename(k) for k in nes), sorted([by["g4"]["code"] + ".nes", by["g5"]["code"] + ".nes"]), "only the files Hakchi would store differently get their copy")
        self.assertIn(f"Exec=/bin/clover-kachikachi-wr /var/games/{by['g4']['code']}/{by['g4']['code']}.nes ", rd(os.path.join(gs, by["g4"]["menu"], by["g4"]["code"], by["g4"]["code"] + ".desktop")).decode())
        bi = rd(os.path.join(gs, by["g6"]["menu"], "CLV-P-NAAAE", "CLV-P-NAAAE.desktop")).decode()
        self.assertIn("Exec=/usr/bin/clover-kachikachi /var/squashfs/usr/share/games/nes/kachikachi/CLV-P-NAAAE/CLV-P-NAAAE.nes --volume 75", bi, "a built-in game: a reference to the console's own game")
        self.assertIn("Path=/var/saves/CLV-P-NAAAE", bi); self.assertEqual(os.listdir(os.path.join(gs, by["g6"]["menu"], "CLV-P-NAAAE")), ["CLV-P-NAAAE.desktop"], "nothing of the game is copied")
        for f in sp["folders"]:   # each folder of the collection: its storage folder with its games and Back
            self.assertTrue(any(x.startswith("CLV-S-") for x in os.listdir(os.path.join(gs, f["menu"]))), f)
        back = rd(os.path.join(gs, col["menu"], "CLV-S-00000", "CLV-S-00000.desktop")).decode(); self.assertIn("Exec=/bin/chmenu 000 /var/games", back)
        idx = self.index(root); self.assertEqual((idx["schema"], idx["version"], idx["generation"]), ("ccs-collection-interface", 1, 1)); self.assertEqual(HP.check_index(idx), [])
        self.assertEqual(idx["bases"]["entries"], "/var/lib/hakchi/games/nes-usa")
        gi = idx["games"][g1["id"]]; self.assertEqual(gi["entry"], {"base": "entries", "path": f"{g1['menu']}/{g1['code']}/{g1['code']}.desktop", "code": g1["code"]})
        self.assertTrue(os.path.isfile(os.path.join(gs, *gi["art"]["cover"]["path"].split("/"))), "the index's references resolve from their base")
        self.assertEqual(idx["games"][by["g3"]["id"]]["status"], "not-published")
        c = idx["collections"][0]; self.assertEqual(c["games"], [by[x]["id"] for x in ("g1", "g2", "g3", "g4", "g5", "g6", "g7")], "the collection's order")
        self.assertEqual(next(f["games"] for f in c["folders"] if f["name"] == "Favorites Too"), [by["g2"]["id"], by["g6"]["id"]], "a folder lists every game it has, also one shown in another Hakchi folder")
        self.assertEqual([f["name"] for f in c["folders"]], ["Action", "Inner", "Favorites Too"], "folders in order, a parent before its folders")
        self.assertTrue(CI.verify_installed(CI.FakeConsole(root), p)["verified"])
        st = HP.parse_output(CI.FakeConsole(root).collections("status")); self.assertTrue(st["ok"]); self.assertIn("changed=0 missing=0", st["result"])
        self.assertEqual(self.run_plan(root, p)["result"], "no-change", "the same plan again changes nothing")
        self.assertEqual(CI.inspect_console(CI.FakeConsole(root))["hakchi_games"]["games"], 5)

    def test_07_foreign_code_kept_update_and_removal_of_only_ours(self):
        root = self.console(); sp = HP.spec(self.pkg); by = {g["ccs_id"]: g for g in sp["games"]}
        # another tool's entry with the code of one of the collection's games, in the first page
        other = os.path.join(root, "var/lib/hakchi/games/nes-usa/000", by["g5"]["code"], by["g5"]["code"] + ".desktop"); wr(other, b"[Desktop Entry]\nName=Hakchi's own copy\n")
        p = CI.build_plan(self.pkg, {"mod": False}); r = self.run_plan(root, p); self.assertTrue(r["hakchi"]["ok"], r["hakchi"])
        self.assertEqual(rd(other), b"[Desktop Entry]\nName=Hakchi's own copy\n", "an entry with the same code in another folder is someone else's and is kept")
        idx = self.index(root); self.assertEqual(idx["games"][by["g5"]["id"]]["status"], "published", "its own entry is in the collection's folder (a different folder)")
        # someone else's entry exactly where one of the collection's would go (the console menu's own folder, without game storage): kept,
        # the game listed as not published, every other game added
        root2 = self.console("no-storage", "c2"); pos = os.path.join(root2, "var/lib/hakchi/rootfs/usr/share/games/nes/kachikachi", by["g5"]["code"], "keep.desktop"); wr(pos, b"theirs")
        r2 = self.run_plan(root2, p); self.assertTrue(r2["hakchi"]["ok"], r2["hakchi"]); self.assertEqual(rd(pos), b"theirs")
        i2 = self.index(root2); self.assertEqual(i2["games"][by["g5"]["id"]]["status"], "not-published"); self.assertIn("already in Hakchi", i2["games"][by["g5"]["id"]]["reason"])
        self.assertNotIn("entry", i2["games"][by["g5"]["id"]]); self.assertEqual(r2["hakchi"]["published"], 3)
        # a folder in hakchi's game storage with this collection's folder name that is not this program's: nothing is added
        root3 = self.console(name="c3"); fd = os.path.join(root3, "var/lib/hakchi/games/nes-usa", sp["collection"]["menu"]); wr(os.path.join(fd, "theirs.txt"), b"x")
        before3 = tree(os.path.join(root3, "var/lib/hakchi/games")); r3 = self.run_plan(root3, p)
        self.assertEqual((r3["result"], r3["ok"]), ("incomplete", False), "without CCS Mod the games are what is delivered: not complete, and said so")
        self.assertFalse(r3["hakchi"]["ok"]); self.assertIn("is not Classic Cart Studio's", r3["hakchi"]["why"])
        self.assertEqual(tree(os.path.join(root3, "var/lib/hakchi/games")), before3, "nothing in hakchi's game storage changed")
        # an update: a game removed from the collection, another added: only this program's entries change
        g2 = [g for g in GAMES if g["id"] not in ("g1",)] + [{"id": "g8", "title": "New One", "rom": rom(3, seed=8), "players": "1"}]
        pk2 = make_package(os.path.join(self.t, "pkg2"), g2, FOLDERS, []); p2 = CI.build_plan(pk2, {"mod": False})
        e1 = os.path.join(root, "var/lib/hakchi/games/nes-usa", by["g1"]["menu"], by["g1"]["code"])
        self.assertTrue(os.path.isdir(e1)); before = self.foreign(root)
        r3 = self.run_plan(root, p2, pk2); self.assertTrue(r3["hakchi"]["ok"], r3["hakchi"]); self.assertFalse(os.path.exists(e1), "the game no longer in the collection: its entry removed")
        self.assertEqual({k: v for k, v in self.foreign(root).items() if k in before and "CLV-H-AAAA" in k}, {k: v for k, v in before.items() if "CLV-H-AAAA" in k})
        self.assertEqual(self.index(root)["generation"], 2, "every coherent rewrite: the next generation")
        # a changed entry of ours: kept and reported on removal
        sp2 = HP.spec(pk2); g8 = next(g for g in sp2["games"] if g["ccs_id"] == "g8"); d8 = os.path.join(root, "var/lib/hakchi/games/nes-usa", g8["menu"], g8["code"], g8["code"] + ".desktop")
        wr(d8, rd(d8) + b"edited\n")
        p3 = CI.build_plan(pk2, {"mod": True}, False); r4 = self.run_plan(root, p3, pk2)
        self.assertTrue(r4["hakchi"]["ok"], r4["hakchi"]); self.assertEqual(r4["hakchi"]["action"], "unpublish")
        self.assertTrue(os.path.isfile(d8), "a changed entry is kept"); self.assertTrue(any(g8["code"] in k for k in r4["hakchi"]["kept"]))
        left = [k for k in tree(root) if "/hakchi/games/" in k and "ccs-" in k and not k.endswith("/")]
        self.assertEqual([os.path.basename(k) for k in left], [g8["code"] + ".desktop"], "everything else this program added is gone")
        self.assertFalse(os.path.exists(os.path.join(root, "var/lib/ccs/integration/v1/index.json")))
        self.assertEqual({k: v for k, v in self.foreign(root).items() if "CLV-H-AAAA" in k}, {k: v for k, v in before.items() if "CLV-H-AAAA" in k})

    def test_08_flat_and_no_storage(self):
        root = self.console("flat-storage", "flat"); p = CI.build_plan(self.pkg, {"mod": False}); r = self.run_plan(root, p); self.assertTrue(r["hakchi"]["ok"], r["hakchi"])
        sp = HP.spec(self.pkg); col = sp["collection"]
        self.assertTrue(os.path.isfile(os.path.join(root, "var/lib/hakchi/games", col["code"], col["code"] + ".desktop")), "a flat storage: the collection's folder entry in it")
        self.assertEqual(self.index(root)["collections"][0]["entry"]["path"], f"{col['code']}/{col['code']}.desktop")
        root = self.console("no-storage", "none"); r = self.run_plan(root, p); self.assertTrue(r["hakchi"]["ok"], r["hakchi"]); self.assertEqual(r["hakchi"]["mode"], "overlay")
        md = os.path.join(root, "var/lib/hakchi/rootfs/usr/share/games/nes/kachikachi"); by = {g["ccs_id"]: g for g in sp["games"]}
        self.assertTrue(os.path.isfile(os.path.join(md, by["g1"]["code"], by["g1"]["code"] + ".desktop")), "without game storage: directly in the console menu's own game folder")
        self.assertFalse(os.path.exists(os.path.join(md, "CLV-P-NAAAE")), "a built-in game is already there: never covered")
        self.assertFalse([x for x in os.listdir(md) if x.startswith("CLV-S-")], "no folders without hakchi's game storage")
        idx = self.index(root); self.assertEqual(idx["games"][by["g6"]["id"]]["reason"], "already in Original Menu"); self.assertNotIn("folders", idx["capabilities"])
        self.assertFalse(os.path.exists(os.path.join(root, "var/lib/hakchi/games")), "no game storage is made (that would hide the console's own games)")

    def test_08b_a_european_nes_classic_gets_them_in_its_own_game_storage(self):
        # its software names REGION EUR: hakchi's findGameStorage finds games/nes-eur (games/$sftype-$sfregion), and that is where the console
        # program puts the collection's games; another tool's game there is kept, nothing goes into another region's folder
        root = self.console("nes-eur", "eur"); fb = self.foreign(root); stock = CI.stock_digest(root) | CI.storage_digest(root)
        p = CI.build_plan(self.pkg, {"mod": False}); r = self.run_plan(root, p); self.assertTrue(r["ok"], r)
        hk = r["hakchi"]; self.assertTrue(hk["ok"], hk); self.assertEqual((hk["mode"], hk["published"]), ("storage", 5))
        col = HP.spec(self.pkg)["collection"]
        self.assertTrue(os.path.isfile(os.path.join(root, "var/lib/hakchi/games/nes-eur/000", col["code"], col["code"] + ".desktop")), "the collection's folder entry in its first page")
        self.assertFalse(os.path.exists(os.path.join(root, "var/lib/hakchi/games/nes-usa")))
        self.assertEqual({k: v for k, v in self.foreign(root).items() if k in fb}, fb); self.assertEqual(CI.stock_digest(root) | CI.storage_digest(root), stock)
        self.assertTrue(CI.verify_installed(CI.FakeConsole(root), p)["verified"])
        up = CI.uninstall_plan(CI.FakeConsole(root)); self.assertTrue(up["ok"], up); u = CI.uninstall(CI.FakeConsole(root), expect=up["hash"]); self.assertTrue(u["ok"], u)
        self.assertFalse([k for k in tree(root) if "/hakchi/games/" in k and "ccs-" in k]); self.assertEqual(CI.stock_digest(root) | CI.storage_digest(root), stock)

    def test_09_uninstall_removes_entries_first(self):
        root = self.console(); p = CI.build_plan(self.pkg, {"mod": False}); r = self.run_plan(root, p); self.assertTrue(r["hakchi"]["ok"])
        stock = CI.stock_digest(root) | CI.storage_digest(root); up = CI.uninstall_plan(CI.FakeConsole(root)); self.assertTrue(up["ok"], up)
        self.assertEqual(up["actions"][0]["op"], "collections-remove"); self.assertTrue(any("Hakchi games" in x for x in up["remove"]))
        u = CI.uninstall(CI.FakeConsole(root), expect=up["hash"]); self.assertTrue(u["ok"], u)
        self.assertFalse([k for k in tree(root) if "/hakchi/games/" in k and "ccs-" in k], "no entry or folder of this program left")
        self.assertFalse(os.path.exists(os.path.join(root, "var/lib/ccs/integration")), "the console program, the stage and the index are gone")
        self.assertEqual(CI.stock_digest(root) | CI.storage_digest(root), stock)

    def test_10_interrupted_and_failing_publication(self):
        p = CI.build_plan(self.pkg, {"mod": False})
        # the console stops answering at the publication's apply: the installation stands, the earlier copy stays (an entry may name it),
        # and installing the same plan again finishes the publication
        root = self.console(name="late"); first = self.run_plan(root, p); self.assertTrue(first["hakchi"]["ok"])
        pk2 = make_package(os.path.join(self.t, "pk2"), GAMES[:2] + [{"id": "g9", "title": "Nine", "rom": rom(2, seed=9)}], [], []); p2 = CI.build_plan(pk2, {"mod": False})   # another game file: another games copy
        class Late(CI.FakeConsole):
            def collections(s, command, stage=None):
                if command == "apply": raise ConnectionError("the console stopped answering")
                return super().collections(command, stage)
        r = CI.Installer(Late(root), p2, pk2, os.path.join(self.jd, "late.jsonl")).run(); self.assertEqual((r["result"], r["ok"]), ("incomplete", False), r)
        self.assertFalse(r["hakchi"]["ok"]); self.assertIn("could not be added", r["warnings"][0]); self.assertIn("Install the collection again", r["next"])
        self.assertEqual(len(CI.read_records(CI.FakeConsole(root))["games"]), 2, "the earlier copy stays (an entry may name it)")
        self.assertFalse(os.path.exists(os.path.join(root, "var/lib/ccs/integration/stage", r["attempt"])), "the staged publication is removed")
        self.assertIs(CI.verify_installed(CI.FakeConsole(root), p2)["verified"], False)
        r2 = self.run_plan(root, p2, pk2); self.assertTrue(r2["hakchi"]["ok"], r2); self.assertEqual(len(CI.read_records(CI.FakeConsole(root))["games"]), 1, "installed again: finished")
        self.assertTrue(CI.verify_installed(CI.FakeConsole(root), p2)["verified"])
        # the index changed by someone between the check and the apply: refused, nothing changed
        root = self.console(name="cas"); first = self.run_plan(root, p); self.assertTrue(first["hakchi"]["ok"])
        class Swap(CI.FakeConsole):
            def collections(s, command, stage=None):
                if command == "apply": wr(s.p(HP.INDEX_PATH), rd(s.p(HP.INDEX_PATH)) + b" ")
                return super().collections(command, stage)
        pk3 = make_package(os.path.join(self.t, "pk3"), GAMES[:3], [], []); p3 = CI.build_plan(pk3, {"mod": False}); before = tree(os.path.join(root, "var/lib/hakchi/games"))
        r = CI.Installer(Swap(root), p3, pk3, os.path.join(self.jd, "cas.jsonl")).run(); self.assertFalse(r["hakchi"]["ok"]); self.assertIn("index changed", r["hakchi"]["why"])
        self.assertEqual(tree(os.path.join(root, "var/lib/hakchi/games")), before, "nothing in hakchi's game storage changed")

    def test_11_path_safety_of_stage_and_index(self):
        sp = HP.spec(self.pkg); lay = HP.layout(sp, "storage", "0123456789abcdef.0a1b2c3d"); st = HP.stage_files(lay)
        for rel in st: self.assertTrue(rel == "publication" or re.match(r"^files/(root|ccs-[0-9a-f]{10})/CLV-[A-Z0-9]-[A-Z0-9]{5}/CLV-[A-Z0-9]-[A-Z0-9]{5}(\.desktop|\.png|_small\.png|\.nes)$", rel), rel)
        doc = HP.index_doc(sp, lay, {"store": "/var/lib/hakchi/games/nes-usa", "menu": "/var/lib/hakchi/games/nes-usa/000", "system": "/var/squashfs/usr/share/games/nes/kachikachi"}, {}, 3)
        text = json.dumps(doc)
        self.assertNotRegex(text, r"[A-Za-z]:\\\\|/Users/|\.\./|/tmp/ccs/pkg|Exec=|sha256sum|\$\(", "no PC path, traversal, launch line or command in the index")
        for bad in ({"base": "entries", "path": "../x"}, {"base": "entries", "path": "/abs"}, {"base": "nowhere", "path": "a"}):
            d = json.loads(text); g = next(iter(d["games"].values())); g["entry"] = bad; self.assertTrue(HP.check_index(d), bad)
        d = json.loads(text); d["bases"]["entries"] = "C:\\Users\\x"; self.assertTrue(HP.check_index(d))
        d = json.loads(text); d["collections"][0]["games"].append("nes-missing"); self.assertTrue(HP.check_index(d))
        # the stand-in's console program refuses a stage outside its place, a damaged stage and a forged apply list
        root = self.console(); r = self.run_plan(root, CI.build_plan(self.pkg, {"mod": False})); self.assertTrue(r["hakchi"]["ok"])
        c = CI.FakeConsole(root)
        with self.assertRaises(OSError): c.collections("check", "/var/lib/ccs/integration/v1")
        stage = "/var/lib/ccs/integration/stage/0a0b0c0d"; base = os.path.join(root, *stage.strip("/").split("/"))
        for rel, b in st.items(): wr(os.path.join(base, *rel.split("/")), b)
        self.assertTrue(HP.parse_output(c.collections("check", stage))["ok"])
        f0 = next(k for k in st if k.endswith(".desktop")); wr(os.path.join(base, *f0.split("/")), st[f0] + b"x")
        self.assertIn("not complete or not well formed", HP.parse_output(c.collections("check", stage))["result"])
        wr(os.path.join(base, *f0.split("/")), st[f0]); pub = st["publication"].replace(b"mode=storage", b"mode=overlay"); wr(os.path.join(base, "publication"), pub)
        self.assertIn("not complete or not well formed", HP.parse_output(c.collections("check", stage))["result"], "a seal that does not match: refused")
        wr(os.path.join(base, "publication"), st["publication"]); wr(os.path.join(base, "index.json"), b"{}"); wr(os.path.join(base, "apply"), HP.apply_file([], "ab" * 32, None, 9))
        self.assertIn("staged index is not the one listed", HP.parse_output(c.collections("apply", stage))["result"])

class WorkerFlag(unittest.TestCase):
    """the plan flag through the worker's protocol (the backend's home-plan passes it): taken, its default, refused when not true or false;
    a rehearsal on a simulated console adds the Hakchi games and removes them with the rest"""
    def setUp(self):
        from console_engine.worker import Worker
        self.t = tempfile.mkdtemp(prefix="ccs-hpw-"); st = os.path.join(self.t, "staged"); make_package(os.path.join(st, "mod"), GAMES, FOLDERS, ["g1"])
        self.w = Worker(os.path.join(self.t, "root"), console_package=st)
    def tearDown(self): shutil.rmtree(self.t, ignore_errors=True)
    def ask(self, cmd, args=None):
        out = io.StringIO(); self.w.out = out; self.w.handle(json.dumps({"v": 1, "id": "t", "cmd": cmd, "args": args or {}}))
        return [json.loads(l) for l in out.getvalue().splitlines() if l.strip()][-1]
    def test_13_plan_flag_and_rehearsal(self):
        r = self.ask("home-plan", {"mod": False}); self.assertEqual(r["event"], "result", r)
        p = r["result"]; self.assertTrue(p["hakchi"]["publish"]); self.assertIn("as Hakchi games", p["hakchi_text"]); self.assertNotIn("files", p)
        r0 = self.ask("home-plan", {"mod": False, "hakchi_games": False}); self.assertEqual((r0["event"], r0["code"], r0["text"]), ("error", "refused", CI.NOTHING_TEXT), "neither CCS Mod nor Hakchi games: nothing to install")
        self.assertFalse(self.ask("home-plan", {"mod": True, "hakchi_games": False})["result"]["hakchi"]["publish"])
        self.assertTrue(self.ask("home-plan", {"hakchi_games": True})["result"]["hakchi"]["publish"])
        self.assertNotEqual(self.ask("home-plan", {"hakchi_games": "yes"})["event"], "result", "only true or false")
        self.assertNotEqual(self.ask("home-plan", {"games": []})["event"], "result", "nothing else is taken")
        pf = self.ask("home-preflight", {"plan_hash": p["hash"], "profile": "hakchi-ready"})["result"]
        self.assertEqual(pf["hakchi"]["action"], "publish"); self.assertIn(p["hakchi_text"], pf["changes_text"])
        sim = self.ask("home-simulate", {"plan_hash": p["hash"], "profile": "hakchi-ready", "then": ["uninstall"]})["result"]
        self.assertEqual(sim["install"]["result"], "installed"); self.assertTrue(sim["install"]["hakchi"]["ok"], sim["install"]["hakchi"]); self.assertEqual(sim["install"]["hakchi"]["published"], 5)
        self.assertTrue(sim["stock_unchanged"]); self.assertTrue(sim["uninstall"]["ok"], sim["uninstall"]); self.assertTrue(sim["stock_unchanged_after_uninstall"])

class GamesOnly(unittest.TestCase):
    """without CCS Mod: only what the Hakchi entries need (games_only.py), never CCS HOME; its own copy, record and removal; adding CCS Mod
    later moves the entries to CCS HOME's copy and removes the games copy; a CCS HOME already on the console is kept as it is"""
    def setUp(self):
        self.t = tempfile.mkdtemp(prefix="ccs-go-"); self.pkg = make_package(os.path.join(self.t, "pkg"), GAMES, FOLDERS, ["g1"]); self.n = 0
        self.jd = os.path.join(self.t, "j"); os.makedirs(self.jd); self.p = CI.build_plan(self.pkg, {"mod": False})
    def tearDown(self): shutil.rmtree(self.t, ignore_errors=True)
    def console(self, profile="hakchi-ready", name="c"):
        d = os.path.join(self.t, name); CI.make_stand_in(d, profile); return d
    def jnew(self): self.n += 1; return os.path.join(self.jd, f"j{self.n}.jsonl")
    def run_plan(self, root, plan=None, pkg=None, **faults): return CI.Installer(CI.FakeConsole(root, faults), plan or self.p, pkg or self.pkg, self.jnew()).run()
    @staticmethod
    def under(root, rel): return {k: v for k, v in tree(root).items() if k.startswith(rel)}
    def entries(self, root):   # the launch lines of this program's Hakchi game entries
        gs = os.path.join(root, "var/lib/hakchi/games/nes-usa"); out = {}
        for d, _, fs in os.walk(gs):
            for f in fs:
                if f.endswith(".desktop") and "ccs-" in os.path.relpath(d, gs): t = rd(os.path.join(d, f)).decode(); out[f[:-8]] = next((l[5:] for l in t.split("\n") if l.startswith("Exec=")), "")
        return out

    def test_14_only_what_the_hakchi_entries_need(self):
        p = self.p; sp = HP.spec(self.pkg); by = {g["ccs_id"]: g for g in sp["games"]}
        self.assertEqual((p["delivery"], p["layout"]), ("games", "mod")); self.assertTrue(p["hakchi"]["publish"])
        self.assertEqual(sorted(f["path"] for f in p["files"]), sorted(["licenses/NOTICE.txt", by["g1"]["rom"], by["g2"]["rom"]]),
                         "only the files the entries launch where they are: a game Hakchi stores its own copy of, a built-in game and a game left out need nothing here")
        bad = [f["path"] for f in p["files"] if f["path"].split("/")[0] in ("bin", "home", "fonts", "startup", "config") or f["path"] in ("session.json", "MANIFEST.json")]
        self.assertEqual(bad, [], "nothing of CCS HOME: no player, menu, themes, pictures, fonts, startup screen or session")
        self.assertNotIn("pointer", p["paths"]); self.assertTrue(p["paths"]["games"].startswith("/var/lib/ccs/games/")); self.assertNotIn("switch", p["steps"])
        self.assertIsNone(p["startup"]); self.assertIsNone(p["player"]); self.assertEqual(p["playable"], 5)
        self.assertEqual(p["games_only"], {"files": 3, "bytes": p["bytes"], "game_files": 2, "rom_bytes": len(GAMES[0]["rom"]) + len(GAMES[1]["rom"])})
        self.assertIn("Their game files (2 files, 49.2 KB) go into a folder of Classic Cart Studio's own", p["hakchi_text"]); self.assertTrue(p["hakchi_text"].endswith("CCS HOME is not installed."))
        self.assertIn("go into Hakchi's game folder", p["hakchi_text"]); self.assertNotRegex(p["hakchi_text"], r"KiB|MiB")
        self.assertTrue(p["notes"][0].startswith("Without CCS Mod, CCS HOME is not installed"))
        self.assertIn("They use the game files already in CCS HOME's copy; their entries (launch files and pictures, and 2 game files in the form Hakchi stores: ", CI.build_plan(self.pkg, {"mod": True}, True)["hakchi_text"])
        self.assertEqual(CI.build_plan(self.pkg, {"mod": False}), p, "the same package always gives the same plan"); json.dumps(p)
        with self.assertRaises(CI.Refused) as e: CI.build_plan(self.pkg, {"mod": False}, False)
        self.assertEqual(e.exception.why, CI.NOTHING_TEXT)
        none = make_package(os.path.join(self.t, "none"), [g for g in GAMES if g["id"] in ("g3", "g7")])
        with self.assertRaises(CI.Refused) as e: CI.build_plan(none, {"mod": False})
        self.assertEqual(e.exception.why, CI.NO_HAKCHI_GAMES_TEXT)
        self.assertEqual(CI.build_plan(none)["delivery"], "home", "with CCS Mod the collection plays in CCS HOME")
        # an Included Game's own unchanged file travels with its license text, as the records hold them
        import common
        old = common.SITE_ROOT; common.SITE_ROOT = os.path.normpath(os.path.join(HERE, "..", "..", "site-data"))
        try:
            thw = rd(os.path.join(common.SITE_ROOT, "included", "thwaite.nes")); inc = make_package(os.path.join(self.t, "inc"), [{"id": "thw", "title": "Thwaite", "rom": thw}] + GAMES[:1])
            gi = CI.build_plan(inc, {"mod": False}); names = [f["path"] for f in gi["files"]]
            self.assertIn("licenses/thwaite-gpl-3.0.txt", names); self.assertIn(f"roms/{sha(thw[16:])}.nes", names)
            lic = next(f for f in gi["files"] if f["path"] == "licenses/thwaite-gpl-3.0.txt"); self.assertEqual(lic["sha256"], sha(rd(os.path.join(common.SITE_ROOT, "included", "source", "thwaite", "gpl-3.0.txt"))))
            root = self.console(name="inc"); r = self.run_plan(root, gi, inc); self.assertTrue(r["ok"], r)
            notice = rd(os.path.join(root, "var/lib/ccs/games", r["copy"], "licenses", "NOTICE.txt")).decode()
            self.assertIn("Thwaite 0.03 (2011) by Damian Yerrick", notice); self.assertIn("No CCS HOME is in this folder.", notice)
        finally: common.SITE_ROOT = old

    def test_15_installed_without_ccs_home_and_without_a_second_copy(self):
        root = self.console(); before = tree(root); stock = CI.stock_digest(root) | CI.storage_digest(root); c = CI.FakeConsole(root)
        r = CI.Installer(c, self.p, self.pkg, self.jnew()).run(); self.assertTrue(r["ok"], r)
        self.assertEqual((r["result"], r["delivery"], r["hakchi"]["published"]), ("installed", "games", 5)); copy = r["copy"]
        self.assertEqual(self.under(root, "var/lib/ccs/pkg"), {k: v for k, v in before.items() if k.startswith("var/lib/ccs/pkg")}, "nothing under CCS HOME's pkg folder")
        self.assertFalse([w for w in c.writes if w.startswith(("/var/lib/ccs/pkg", "/var/lib/ccs/data"))]); self.assertFalse(os.path.exists(os.path.join(root, "var/lib/ccs/pkg/current")))
        self.assertFalse(os.path.exists(os.path.join(root, "var/lib/ccs/data")), "no CCS HOME saves folder"); self.assertFalse(any("ccs-mod" in w for w in c.writes), "no CCS HOME tile")
        g = os.path.join(root, "var/lib/ccs/games", copy); self.assertEqual(sorted(self.under(root, f"var/lib/ccs/games/{copy}/")), sorted([f"var/lib/ccs/games/{copy}/" + f["path"] for f in self.p["files"]] + [f"var/lib/ccs/games/{copy}/", f"var/lib/ccs/games/{copy}/licenses/", f"var/lib/ccs/games/{copy}/roms/"]))
        for f in self.p["files"]: self.assertEqual(sha(rd(os.path.join(g, *f["path"].split("/")))), f["sha256"])
        rec = CI.read_records(CI.FakeConsole(root))["games"][copy]; self.assertEqual(rec["files"], {f["path"]: f["sha256"] for f in self.p["files"]}); self.assertEqual(rec["schema"], "ccs-owned-games/1")
        sp = HP.spec(self.pkg); by = {x["ccs_id"]: x for x in sp["games"]}; ex = self.entries(root)
        for k in ("g1", "g2"): self.assertTrue(ex[by[k]["code"]].startswith(f"/bin/clover-kachikachi-wr /var/lib/ccs/games/{copy}/{by[k]['rom']} "), (k, ex[by[k]["code"]]))
        nes = sorted(os.path.basename(k) for k in tree(root) if k.endswith(".nes") and ("/hakchi/games/" in k or "/ccs/" in k) and "AAAA" not in k)
        self.assertEqual(nes, sorted([by["g1"]["rom"][5:], by["g2"]["rom"][5:], by["g4"]["code"] + ".nes", by["g5"]["code"] + ".nes"]), "each game file once on the console")
        self.assertEqual(CI.stock_digest(root) | CI.storage_digest(root), stock); self.assertTrue(CI.verify_installed(CI.FakeConsole(root), self.p)["verified"])
        ins = CI.inspect_console(CI.FakeConsole(root)); self.assertEqual((ins["games_copies"], ins["in_use"], ins["pointer"], ins["hakchi_games"]["games"]), ([copy], None, "absent", 5))
        c2 = CI.FakeConsole(root); self.assertEqual(CI.Installer(c2, self.p, self.pkg, self.jnew()).run()["result"], "no-change"); self.assertEqual(c2.writes, [])
        # a game file of the copy changed on the console: a new copy, the entries name it, the earlier copy goes (its changed file kept and named)
        f0 = os.path.join(g, *self.p["files"][1]["path"].split("/")); wr(f0, rd(f0) + b"x")
        r2 = self.run_plan(root); self.assertTrue(r2["ok"], r2); self.assertNotEqual(r2["copy"], copy); self.assertTrue(CI.verify_installed(CI.FakeConsole(root), self.p)["verified"])
        self.assertTrue(any(self.p["files"][1]["path"] in k for k in r2["kept_changed_or_unknown"]), r2["kept_changed_or_unknown"]); self.assertTrue(os.path.isfile(f0))

    def test_16_adding_ccs_mod_later_moves_the_entries_into_ccs_homes_copy(self):
        root = self.console(); r = self.run_plan(root); self.assertTrue(r["ok"], r); gcopy = r["copy"]
        home = CI.build_plan(self.pkg, {"mod": True}, True); r2 = self.run_plan(root, home); self.assertTrue(r2["ok"], r2); self.assertEqual(r2["delivery"], "home")
        sp = HP.spec(self.pkg); by = {x["ccs_id"]: x for x in sp["games"]}; ex = self.entries(root)
        self.assertTrue(ex[by["g1"]["code"]].startswith(f"/bin/clover-kachikachi-wr /var/lib/ccs/pkg/{r2['copy']}/{by['g1']['rom']} "), ex[by["g1"]["code"]])
        self.assertFalse(os.path.exists(os.path.join(root, "var/lib/ccs/games")), "the games copy is removed once no entry names it: one copy of each game")
        self.assertEqual(CI.read_records(CI.FakeConsole(root))["games"], {}); self.assertTrue(any(t.get("copy") == gcopy and t.get("removed") for t in r2["tidy"]), r2["tidy"])
        self.assertTrue(CI.verify_installed(CI.FakeConsole(root), home)["verified"])
        # Studio's default with CCS Mod (no Hakchi games): the entries and the games copy both go
        root = self.console(name="d"); self.assertTrue(self.run_plan(root)["ok"]); r3 = self.run_plan(root, CI.build_plan(self.pkg)); self.assertTrue(r3["ok"], r3)
        self.assertEqual(r3["hakchi"]["action"], "unpublish"); self.assertFalse(os.path.exists(os.path.join(root, "var/lib/ccs/games"))); self.assertFalse(self.entries(root))

    def test_17_a_ccs_home_already_on_the_console_stays_as_it_is(self):
        root = self.console(); home = CI.build_plan(self.pkg, {"mod": True}, True); r = self.run_plan(root, home); self.assertTrue(r["ok"], r); hc = r["copy"]
        CI.FakeConsole(root).pack_uninstall("ccs-mod")   # another tool removed CCS Mod and kept CCS HOME's files, as its ordinary removal does
        pkg_before = self.under(root, "var/lib/ccs/pkg"); data_before = self.under(root, "var/lib/ccs/data")
        pf = CI.Installer(CI.FakeConsole(root), self.p, self.pkg, self.jnew()).preflight(); self.assertEqual((pf["home_kept"], pf["delivery"], pf["hakchi"]["action"]), (hc, "games", "publish"))
        r2 = self.run_plan(root); self.assertTrue(r2["ok"], r2)
        self.assertEqual(self.under(root, "var/lib/ccs/pkg"), pkg_before, "CCS HOME's copy and its pointer are kept as they are"); self.assertEqual(self.under(root, "var/lib/ccs/data"), data_before)
        sp = HP.spec(self.pkg); g1 = next(x for x in sp["games"] if x["ccs_id"] == "g1")
        self.assertTrue(self.entries(root)[g1["code"]].startswith(f"/bin/clover-kachikachi-wr /var/lib/ccs/games/{r2['copy']}/"), "the entries name the games copy, which this program keeps for them")

    def test_18_every_change_fails_in_turn_and_is_undone_or_finished(self):
        base = self.console(name="base"); cnt = os.path.join(self.t, "count"); shutil.copytree(base, cnt); c = CI.FakeConsole(cnt); marks = []; orig = c._change
        def change(w, act): marks.append(w); return orig(w, act)
        c._change = change; self.assertTrue(CI.Installer(c, self.p, self.pkg, self.jnew()).run()["ok"]); total = c.nchanges; seen = {"rolled": 0, "finished": 0}
        # every change up to the verification (each must be undone exactly), then the publication's own changes every fifth one and the last
        # four (the console program, the staged files, the apply, their removal, the tidy and the closed attempt record: each finished by
        # installing again); the publication's own failures are covered change by change in test_10 and by test_nucleus.py under qemu
        first = next(i for i, w in enumerate(marks, 1) if "/install/collections-" in w)
        for n in [n for n in range(1, total + 1) if n <= first or (n - first) % 5 == 0 or n > total - 4]:
            for kind in ("before", "after"):
                with self.subTest(change=n, kind=kind):
                    root = os.path.join(self.t, f"{kind}-{n}"); shutil.copytree(base, root); before = tree(root); j = self.jnew()
                    r = CI.Installer(CI.FakeConsole(root, {kind: n}), self.p, self.pkg, j).run()
                    ops = [e.get("op") for e in CI.Installer.entries(j)]
                    if "installed" in ops:
                        seen["finished"] += 1
                        if "cleanup-stopped" in ops:   # a change after the verification was not confirmed: nothing more was written, the attempt stays open
                            self.assertEqual(r["result"], "cleanup-stopped", r); self.assertFalse(r["ok"]); self.assertTrue(r["verified"]); self.assertFalse(r["record_closed"]); self.assertEqual(ops[-2:], ["cleanup-stopped", "end"])
                        else: self.assertIn(r["result"], ("installed", "incomplete"), r)
                        if r["result"] == "incomplete": self.assertIn("Install the collection again", r["next"]); self.assertFalse(r["ok"])
                        if CI.read_records(CI.FakeConsole(root))["attempts"]: self.assertEqual(CI.Installer(CI.FakeConsole(root), self.p, self.pkg, j).resume()["result"], "installed")
                        again = self.run_plan(root); self.assertIn(again["result"], ("installed", "no-change"), again); self.assertTrue(CI.verify_installed(CI.FakeConsole(root), self.p)["verified"])
                    else:
                        seen["rolled"] += 1; self.assertTrue(r["rolled_back"]["ok"], r); self.assertEqual(changes(before, tree(root)), [], f"change {n} {kind}")
                    shutil.rmtree(root, ignore_errors=True)
        self.assertGreaterEqual(seen["rolled"], 10); self.assertGreater(seen["finished"], 10)
        # a crash after each change before the verification: recovered from that attempt's journal, the stand-in exactly as it was
        for n in range(1, first):   # (a crash after the verification is finished by resume, as test_18's sweep above shows)
            with self.subTest(crash=n):
                root = os.path.join(self.t, f"crash-{n}"); shutil.copytree(base, root); before = tree(root); j = self.jnew()
                with self.assertRaises(SystemExit): CI.Installer(CI.FakeConsole(root, {"crash_after": n}), self.p, self.pkg, j).run()
                rr = CI.Installer(CI.FakeConsole(root), self.p, self.pkg, j).resume(); self.assertIn(rr["result"], ("rolled-back", "nothing to resume"), rr)
                self.assertEqual(changes(before, tree(root)), []); shutil.rmtree(root, ignore_errors=True)

    def test_19_removal_takes_the_entries_then_the_games_copy(self):
        root = self.console(); stock = CI.stock_digest(root) | CI.storage_digest(root); r = self.run_plan(root); self.assertTrue(r["ok"], r)
        up = CI.uninstall_plan(CI.FakeConsole(root)); self.assertTrue(up["ok"], up); ops = [a["op"] for a in up["actions"]]
        self.assertEqual(ops[0], "collections-remove"); self.assertEqual(up["games_copies"], [r["copy"]])
        gi = [i for i, a in enumerate(up["actions"]) if a.get("path", "").startswith("/var/lib/ccs/games/")]; self.assertTrue(gi and min(gi) > 0)
        self.assertTrue(any("the Hakchi games (game files and their notices)" in x for x in up["remove"]), up["remove"])
        u = CI.uninstall(CI.FakeConsole(root), expect=up["hash"]); self.assertTrue(u["ok"], u)
        self.assertEqual(u["after_restart"], "The console's own menu keeps its own games and saves; the games Classic Cart Studio added to it are gone.")
        self.assertFalse(os.path.exists(os.path.join(root, "var/lib/ccs/games"))); left = sorted(os.listdir(os.path.join(root, "var/lib/ccs")))
        self.assertEqual([x for x in left if x != "owned"], ["pkg"], "only someone else's folder stays (and the add-ons' records folder, kept as every removal keeps it)")
        self.assertEqual(os.listdir(os.path.join(root, "var/lib/ccs/owned")) if "owned" in left else [], [], "no record of the Hakchi games is left")
        self.assertEqual(CI.stock_digest(root) | CI.storage_digest(root), stock)

    def test_20_rehearsals_without_ccs_mod(self):
        from console_engine.worker import Worker
        st = os.path.join(self.t, "staged"); make_package(os.path.join(st, "mod"), GAMES, FOLDERS, ["g1"]); w = Worker(os.path.join(self.t, "root"), console_package=st)
        def ask(cmd, args):
            out = io.StringIO(); w.out = out; w.handle(json.dumps({"v": 1, "id": "t", "cmd": cmd, "args": args}))
            return [json.loads(l) for l in out.getvalue().splitlines() if l.strip()][-1]
        p = ask("home-plan", {"mod": False})["result"]; self.assertEqual(p["delivery"], "games"); self.assertNotIn("package_files", p, "the summary carries no file lists")
        r = ask("home-plan", {"mod": False, "hakchi_games": False}); self.assertEqual((r["event"], r["code"], r["text"]), ("error", "refused", CI.NOTHING_TEXT))
        for sc, want in (("first-install", "installed"), ("repeat", "no-change"), ("update", "installed"), ("repair", "installed")):
            with self.subTest(scenario=sc):
                x = ask("home-simulate", {"plan_hash": p["hash"], "profile": "hakchi-ready", "scenario": sc, "then": ["uninstall"]})["result"]
                self.assertEqual(x["install"]["result"], want, x); self.assertTrue(x["stock_unchanged"]); self.assertTrue(x["uninstall"]["ok"], x["uninstall"]); self.assertTrue(x["stock_unchanged_after_uninstall"])
                if sc == "update": self.assertTrue(any(t.get("removed") for t in x["install"]["tidy"]), x["install"]["tidy"])
                if sc == "repair": self.assertIn("NOTICE.txt", x["setup"][1]["changed_on_console"])
        x = ask("home-simulate", {"plan_hash": p["hash"], "profile": "hakchi-ready", "scenario": "interrupted"})["result"]
        self.assertTrue(x["install"]["rolled_back"]["ok"], x); self.assertEqual(x["after_recovery"], "installed")


class Pins(unittest.TestCase):
    def test_12_program_pins_equal_everywhere(self):
        prog = HP.program(); self.assertEqual(prog["pins"], HP.PROGRAM_PINS, "hakchi_publish.PROGRAM_PINS: the console program's sources as they are")
        cs = rd(os.path.join(HERE, "..", "console-worker", "CcsWorker", "Ops", "ConsolePath.cs")).decode()
        for n, s in HP.PROGRAM_PINS.items(): self.assertIn(f'{{ "{n}", "{s}" }}', cs, f"the transport worker runs only {n} as pinned")
        self.assertEqual(prog["dir"], f"/var/lib/ccs/integration/program-{HP.PROGRAM_PINS['ccs-collections'][:16]}")
        self.assertEqual(prog["files"]["lib.sh"], rd(os.path.join(HERE, "..", "console-nucleus", "console", "lib.sh")).replace(b"\r\n", b"\n"), "the add-on packages' own library, unchanged")

if __name__ == "__main__": unittest.main(verbosity=1)
