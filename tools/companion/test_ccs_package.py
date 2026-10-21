r"""The .ccs package format, schema ccs-package/2 (ccs_package.py; CCS-FORMAT.md is the specification). Python only, no browser.
  round trip       a complete collection (several games, two revisions of one title, a headered ROM with a trainer, a headerless ROM,
                   personal games, artwork, loadouts) is written and read back identically; every ROM is verified by content identity
  subset           a share selection (subset collection) packages exactly its games; extra ROM files offered are not included
  completeness     a game without a ROM file, a ROM of a different revision, a placeholder id: refused by write() BEFORE anything is
                   written, naming the games; packages that break the same rules are refused by inspect()
  integrity        a tampered ROM, a missing listed file, an unlisted entry, a size that differs: reported, nothing imported
  safety           unsafe names, duplicates, links, a decompression bomb, wrong format, schema-1 and backup packages, a newer schema,
                   a malformed manifest, roms.missing: refused with a plain reason
  rom import       only after verification, only the accepted files, exact identity (never a substitute); real personal-store ingest
  scale            3,000 games; a 64 MB ROM; more than 65,535 entries (ZIP64 entry count); ZIP64 records accepted on read
  no network       write, inspect and import_roms with every socket call patched to fail and record; ROM fixtures carry a sentinel
  python test_ccs_package.py      Exit 0 = PASS. (The > 4 GB archive test is test_ccs_package_big.py.)"""
from __future__ import annotations
import hashlib, json, os, shutil, socket, stat, sys, tempfile, time, zipfile
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path[:0] = [HERE, os.path.join(HERE, "..", "rom-pipeline")]
import ccs_package as cp

SENTINEL = b"CCS-SENTINEL-NO-NETWORK-7f3a9c"

# ---------------- fixtures (shared with test_package_api.py and test_ccs_package_big.py) ----------------
def make_content(seed, size: int = 16384) -> bytes:
    s = seed if isinstance(seed, bytes) else str(seed).encode()
    body = hashlib.shake_256(b"ccs-fixture|" + s).digest(max(0, size - len(SENTINEL)))
    return (SENTINEL + body)[:size]

def make_rom(seed, size: int = 16384, header: bool = True, trainer: bool = False) -> tuple[bytes, str]:
    """(file bytes, content_sha256): an NES file of at least personal.MIN_CONTENT bytes of content, with or without an iNES header"""
    content = make_content(seed, size)
    if not header: return content, hashlib.sha256(content).hexdigest()
    hdr = b"NES\x1a" + bytes([max(1, size // 16384), 1, 0x04 if trainer else 0x00]) + bytes(9)
    return hdr + (make_content(b"trainer|" + str(seed).encode(), 512) if trainer else b"") + content, hashlib.sha256(content).hexdigest()

def collection(ids: list, titles: dict | None = None, name: str = "Test collection", loadouts: list | None = None) -> dict:
    titles = titles or {}
    return {"schema": "nes-classic-collection/4", "name": name, "count": len(ids), "selections": [{"version_id": i, "title": titles.get(i, i)} for i in ids],
            "ccs_collection": {"schema": "ccs-collection/1", "name": name, "sel": list(ids), "loadouts": loadouts or []}}

def craft(path: str, coll: dict, roms: dict, art: dict | None = None, man_patch=lambda m: m, extra: list | None = None, method=zipfile.ZIP_DEFLATED, force_zip64=False):
    """A package assembled by hand (for refusal tests): roms {id: file bytes}; the manifest describes the bytes truthfully unless patched."""
    body = json.dumps(coll).encode(); files = [{"path": "collection.json", "sha256": hashlib.sha256(body).hexdigest(), "size": len(body), "kind": "collection"}]; ents = [("collection.json", body)]; inc = []
    for n, d in sorted((art or {}).items()): files.append({"path": "art/" + n, "sha256": hashlib.sha256(d).hexdigest(), "size": len(d), "kind": "art"}); ents.append(("art/" + n, d))
    for vid, d in roms.items():
        ident = cp.identify_bytes(d); p = f"roms/{vid}{'.nes' if d[:4] == b'NES' + bytes([26]) else '.unh'}"
        rec = {"path": p, "sha256": ident["sha256"], "size": len(d), "kind": "rom", "version_id": vid, "title": vid, "content_sha256": ident["content_sha256"]}
        files.append(rec); inc.append({k: rec[k] for k in ("version_id", "title", "path", "sha256", "size", "content_sha256")}); ents.append((p, d))
    rd = cp.README.encode(); files.append({"path": "README.txt", "sha256": hashlib.sha256(rd).hexdigest(), "size": len(rd), "kind": "readme"}); ents.append(("README.txt", rd))
    man = man_patch({"format": cp.FORMAT, "schema": cp.SCHEMA, "kind": "collection", "created": "2026-10-21T00:00:00Z", "app": {"name": "test", "version": "0"}, "summary": {}, "files": files, "roms": {"included": inc}})
    with zipfile.ZipFile(path, "w", compression=method) as z:
        for n, d in ents + (extra or []) + [(cp.MANIFEST, json.dumps(man).encode())]:
            zi = n if isinstance(n, zipfile.ZipInfo) else zipfile.ZipInfo(n); zi.compress_type = method
            with z.open(zi, "w", force_zip64=force_zip64) as w: w.write(d)
    return path

class NoNetwork:
    """Patches every socket entry point to record the attempt and fail (loopback included: this module must not use sockets at all)."""
    def __init__(self): self.attempts = []; self.saved = {}
    def __enter__(self):
        def fail(name):
            def f(*a, **k):
                self.attempts.append((name, repr(a)[:200])); raise OSError("network blocked by test")
            return f
        for obj, attr in ((socket.socket, "connect"), (socket.socket, "connect_ex"), (socket.socket, "sendto"), (socket.socket, "sendall"), (socket.socket, "send"), (socket, "create_connection"), (socket, "getaddrinfo")):
            self.saved[(obj, attr)] = getattr(obj, attr); setattr(obj, attr, fail(attr))
        return self
    def __exit__(self, *a):
        for (obj, attr), v in self.saved.items(): setattr(obj, attr, v)

def main():
    checks = []; timings = {}
    def ok(k, v, n=""): checks.append((k, bool(v))); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:300]})" if n and not v else ""), flush=True)
    tmp = tempfile.mkdtemp(prefix="ccs-pkg-")
    os.environ["NES_PERSONAL_DIR"] = os.path.join(tmp, "personal-store"); os.environ.setdefault("NES_STUDIO_DATA", os.path.join(tmp, "studio-data"))
    import warnings; warnings.simplefilter("ignore")
    try:
        def put(name, data):
            p = os.path.join(tmp, name); open(p, "wb").write(data); return p
        # ---- a complete collection: two revisions of one title (library ids), a trainer ROM, a headerless ROM, personal games ----
        r1, c1 = make_rom("smb-rev0"); r2, c2 = make_rom("smb-rev1"); r3, c3 = make_rom("trainer-game", trainer=True); r4, c4 = make_rom("headerless", header=False)
        r5, c5 = make_rom("homebrew-a", size=40960); r6, c6 = make_rom("homebrew-b", header=False, size=8192)
        L1, L2, L3, L4, P5, P6 = c1[:16], c2[:16], c3[:16], c4[:16], "p_" + c5[:16], "p_" + c6[:16]
        src = {L1: put("smb0.nes", r1), L2: put("smb1.nes", r2), L3: put("trainer.nes", r3), L4: put("plain.unh", r4), P5: put("hb a.nes", r5), P6: put("hb b.unh", r6)}
        ids = [L1, L2, L3, L4, P5, P6]; titles = {L1: "Super Game (Rev 0)", L2: "Super Game (Rev 1)", L3: "Trainer Game", L4: "Plain", P5: "Homebrew Ä", P6: "Homebrew B"}
        coll = collection(ids, titles, "Héllo Wörld collection", [{"id": "l1", "name": "Party", "sel": [L1, P5], "home": {"theme": "arcade-grid"}}, {"id": "l2", "name": "Solo", "sel": [L2], "home": {"theme": "classic"}}])
        roms = [{"version_id": i, "path": src[i], "title": titles[i]} for i in ids]
        art = {P5 + ".jpg": b"\xff\xd8\xff" + b"x" * 100 + b"\xff\xd9", P6 + ".png": b"\x89PNG\r\n\x1a\n" + b"y" * 50}
        out = os.path.join(tmp, "My Collection (test) ünïcode.ccs"); prog = []
        man = cp.write(out, collection=coll, roms=roms, art=art, summary={"loadouts": 2}, app_version="0.7.0", progress=lambda d, t: prog.append((d, t)))
        info = cp.inspect(out)
        ok("round_trip_collection_loadouts_identical", info["ok"] and info["data"] == coll and info["kind"] == "collection" and man["schema"] == "ccs-package/2")
        ok("every_game_has_one_verified_rom", [r["version_id"] for r in info["roms"]] == ids and all(r["verified"] is True for r in info["roms"]))
        ok("rom_sha256_and_content_sha256_exact", {r["version_id"]: (r["sha256"], r["content_sha256"]) for r in info["roms"]} == {i: (hashlib.sha256(open(src[i], "rb").read()).hexdigest(), c) for i, c in zip(ids, [c1, c2, c3, c4, c5, c6])})
        ok("two_revisions_of_one_title_kept_apart", L1 != L2 and {L1, L2} <= {r["version_id"] for r in info["roms"]})
        ok("extensions_follow_the_header", {r["version_id"]: r["path"][-4:] for r in info["roms"]} == {L1: ".nes", L2: ".nes", L3: ".nes", L4: ".unh", P5: ".nes", P6: ".unh"})
        names = zipfile.ZipFile(out).namelist()
        ok("every_archive_file_listed_in_the_manifest", set(names) == {f["path"] for f in man["files"]} | {cp.MANIFEST} and "README.txt" in names and sorted(info["art"]) == sorted("art/" + k for k in art))
        ok("no_missing_list_and_summary_counts", "missing" not in man["roms"] and man["summary"]["games"] == 6 and man["summary"]["roms"] == 6 and man["summary"]["personal"] == 2 and man["summary"]["loadouts"] == 2)
        ok("rom_bytes_inside_are_the_exact_files", all(zipfile.ZipFile(out).read(r["path"]) == open(src[r["version_id"]], "rb").read() for r in info["roms"]))
        ok("progress_reported_to_completion", prog and prog[-1][0] == prog[-1][1] and all(a[0] <= b[0] for a, b in zip(prog, prog[1:])), prog[-3:])
        # ---- subset (share selection) ----
        sub = collection([L2, P6], titles, "Just two"); subp = os.path.join(tmp, "subset.ccs")
        cp.write(subp, collection=sub, roms=roms)   # all six offered; only the two selected are packaged
        si = cp.inspect(subp); ok("subset_package_has_exactly_its_games", si["ok"] and [r["version_id"] for r in si["roms"]] == [L2, P6] and not [n for n in zipfile.ZipFile(subp).namelist() if n.startswith("roms/") and L1 in n])
        empty = os.path.join(tmp, "empty.ccs"); cp.write(empty, collection=collection([], {}, "Empty"), roms=[])
        ok("empty_collection_package_valid", cp.inspect(empty)["ok"] and cp.inspect(empty)["games"] == 0)
        # ---- completeness and identity on write: refused BEFORE anything is written ----
        def refused_write(name, c, rs, key, needle):
            dst = os.path.join(tmp, name + ".ccs")
            try: cp.write(dst, collection=c, roms=rs); ok(name, False, "written")
            except cp.PackageError as e: ok(name, needle in str(e) and e.details.get(key) and not os.path.exists(dst) and not os.path.exists(dst + ".part"), f"{e} {e.details}")
        refused_write("write_refuses_missing_rom", coll, [r for r in roms if r["version_id"] != P5], "missing", "Homebrew Ä")
        refused_write("write_refuses_missing_file_on_disk", coll, [dict(r, path=os.path.join(tmp, "gone.nes")) if r["version_id"] == L3 else r for r in roms], "missing", "Trainer Game")
        refused_write("write_refuses_wrong_revision", coll, [dict(r, path=src[L1]) if r["version_id"] == L2 else r for r in roms], "mismatched", "Super Game (Rev 1)")
        refused_write("write_refuses_placeholder_id", collection(ids + ["x_unknown"], dict(titles, x_unknown="Unknown Game")), roms, "unidentified", "Unknown Game")
        refused_write("write_refuses_library_id_for_personal_content", collection([c5[:16]], {}), [{"version_id": c5[:16], "path": src[P6]}], "mismatched", c5[:16])
        try: cp.write(os.path.join(tmp, "nolist.ccs"), collection={"schema": "nes-classic-collection/4", "name": "x"}, roms=[]); ok("write_refuses_collection_without_game_list", False)
        except cp.PackageError as e: ok("write_refuses_collection_without_game_list", "no game list" in str(e))
        cancelled = os.path.join(tmp, "cancel.ccs")
        try: cp.write(cancelled, collection=coll, roms=roms, cancel=lambda: True)
        except cp.PackageError: pass
        ok("cancelled_export_leaves_no_file", not os.path.exists(cancelled) and not os.path.exists(cancelled + ".part"))
        # ---- refusals on read ----
        def refused(name, path, needle):
            try: r = cp.inspect(path); ok(name, False, "accepted: " + json.dumps(r["problems"])[:200])
            except cp.PackageError as e: ok(name, needle in str(e), str(e))
        def problem(name, path, needle):
            try: r = cp.inspect(path); got = "; ".join(p["problem"] for p in r["problems"]) if not r["ok"] else "accepted"
            except cp.PackageError as e: got = "refused: " + str(e)
            ok(name, needle in got, got); return path
        two = collection([L1, L2], titles); good2 = {L1: r1, L2: r2}
        ok("crafted_reference_package_accepted", cp.inspect(craft(os.path.join(tmp, "ref.ccs"), two, good2))["ok"])
        refused("read_refuses_wrong_revision_listed", craft(os.path.join(tmp, "wrongrev.ccs"), two, {L1: r1, L2: r1}), "not the version its game names")
        lie = lambda m: (m["roms"]["included"][1].update(content_sha256=c2), [f.update(content_sha256=c2) for f in m["files"] if f.get("version_id") == L2], m)[-1]
        wr = problem("read_detects_wrong_revision_bytes_with_a_lying_list", craft(os.path.join(tmp, "wrongrev2.ccs"), two, {L1: r1, L2: r1}, man_patch=lie), "not the version its game names")
        refused("read_refuses_game_without_rom", craft(os.path.join(tmp, "norom.ccs"), two, {L1: r1}), "doesn't include a ROM file for every game")
        refused("read_refuses_extra_rom_not_in_collection", craft(os.path.join(tmp, "extrarom.ccs"), collection([L1]), good2), "doesn't include a ROM file for every game")
        refused("read_refuses_placeholder_id", craft(os.path.join(tmp, "placeholder.ccs"), collection([L1, "x_unknown"]), {L1: r1}), "without a known ROM file")
        refused("read_refuses_missing_list", craft(os.path.join(tmp, "missinglist.ccs"), two, good2, man_patch=lambda m: (m["roms"].update(missing=[{"version_id": "abc"}]), m)[1]), "leaves out some ROM files")
        refused("schema1_package_refused_plainly", craft(os.path.join(tmp, "v1.ccs"), two, good2, man_patch=lambda m: dict(m, schema="ccs-package/1")), "doesn't always include its ROM files, so it can't be opened. Ask for a new .ccs file.")
        refused("backup_package_refused", craft(os.path.join(tmp, "backup.ccs"), two, good2, man_patch=lambda m: dict(m, kind="backup")), "old Studio backup")
        v1b = os.path.join(tmp, "v1backup.ccs")
        with zipfile.ZipFile(v1b, "w") as z: z.writestr("studio-backup.json", b"{}"); z.writestr(cp.MANIFEST, json.dumps({"format": cp.FORMAT, "schema": "ccs-package/1", "kind": "backup", "files": []}))
        refused("schema1_backup_refused_plainly_not_as_unsafe", v1b, "older format")
        refused("newer_schema_refused", craft(os.path.join(tmp, "v9.ccs"), two, good2, man_patch=lambda m: dict(m, schema="ccs-package/9")), "newer version")
        refused("rom_listed_under_wrong_game_refused", craft(os.path.join(tmp, "under.ccs"), two, good2, man_patch=lambda m: (m["roms"]["included"][0].update(version_id=L2), m)[1]), "wrong game")
        # integrity
        def rewrite(srcp, dst, mutate):
            with zipfile.ZipFile(srcp) as zi, zipfile.ZipFile(dst, "w") as zo:
                for i, d in mutate([(i, zi.read(i.filename)) for i in zi.infolist()]): zo.writestr(i, d)
            return dst
        flip = lambda items: [(i, (d[:-1] + bytes([d[-1] ^ 1])) if i.filename.startswith("roms/" + L3) else d) for i, d in items]
        bad = problem("tampered_rom_detected", rewrite(out, os.path.join(tmp, "tampered.ccs"), flip), "checksum does not match")
        problem("missing_listed_rom_refused", rewrite(out, os.path.join(tmp, "dropped.ccs"), lambda it: [(i, d) for i, d in it if not i.filename.startswith("roms/" + P5)]), "missing from the package")
        refused("unlisted_entry_refused", rewrite(out, os.path.join(tmp, "unlisted.ccs"), lambda it: it + [(zipfile.ZipInfo("art/extra.jpg"), b"\xff\xd8x\xff\xd9")]), "does not name")
        problem("tampered_collection_detected", rewrite(out, os.path.join(tmp, "tcoll.ccs"), lambda it: [(i, d.replace(b"Party", b"Partz") if i.filename == "collection.json" else d) for i, d in it]), "damaged")
        # safety
        good_man = zipfile.ZipFile(out).read(cp.MANIFEST)
        def raw(name, entries):
            dst = os.path.join(tmp, name + ".ccs")
            with zipfile.ZipFile(dst, "w") as z:
                for n, d in entries: z.writestr(n if isinstance(n, zipfile.ZipInfo) else zipfile.ZipInfo(n), d)
            return dst
        for n, bad_name in (("dotdot", "../evil.nes"), ("absolute", "/etc/evil"), ("drive", "C:/evil.nes"), ("backslash", "roms\\..\\..\\x.nes"), ("exe", "roms/tool.exe"), ("dot", "roms/./x.nes"), ("control", "roms/a\x01.nes"), ("badid", "roms/zz.nes")):
            refused(f"unsafe_name_refused_{n}", raw(n, [(cp.MANIFEST, good_man), (bad_name, b"x")]), "unsafe")
        dup = os.path.join(tmp, "dup.ccs")
        with zipfile.ZipFile(dup, "w") as z: z.writestr(cp.MANIFEST, good_man); z.writestr("collection.json", b"{}"); z.writestr("collection.json", b"{}")
        refused("duplicate_entries_refused", dup, "duplicate")
        ln = zipfile.ZipInfo(f"roms/{L1}.nes"); ln.external_attr = (stat.S_IFLNK | 0o777) << 16
        refused("symlink_refused", raw("link", [(cp.MANIFEST, good_man), (ln, b"/etc/passwd")]), "a link")
        encp = raw("enc", [(cp.MANIFEST, good_man), ("collection.json", b"{}")]); eb = bytearray(open(encp, "rb").read())
        cd = eb.rfind(b"PK\x01\x02"); assert eb[cd + 46:cd + 61] == b"collection.json"   # the last central directory record
        eb[cd + 8] |= 0x01; open(encp, "wb").write(bytes(eb))   # zipfile clears the flag when writing, so set the "encrypted" bit in the central directory
        refused("encrypted_entry_refused", encp, "Encrypted")
        bomb = os.path.join(tmp, "bomb.ccs")
        with zipfile.ZipFile(bomb, "w", compression=zipfile.ZIP_DEFLATED) as z: z.writestr(cp.MANIFEST, good_man); z.writestr("collection.json", b"\0" * (40 * 1024 * 1024))
        refused("decompression_bomb_refused", bomb, "ratio")
        bz = os.path.join(tmp, "bzip.ccs")
        with zipfile.ZipFile(bz, "w", compression=zipfile.ZIP_BZIP2) as z: z.writestr(cp.MANIFEST, good_man)
        refused("unsupported_method_refused", bz, "unsupported compression")
        refused("wrong_format_refused", raw("fmt", [(cp.MANIFEST, json.dumps({"format": "other"}).encode())]), "not a Classic Cart Studio package")
        refused("malformed_manifest_refused", raw("malformed", [(cp.MANIFEST, b"{not json")]), "is damaged")
        refused("not_a_zip_refused", (lambda p: (open(p, "wb").write(b"hello"), p)[1])(os.path.join(tmp, "plain.ccs")), "or it is damaged")
        # declared size: an entry that inflates to more than its header says stops at the declared size
        big = os.path.join(tmp, "liar.ccs"); shutil.copyfile(craft(os.path.join(tmp, "liar0.ccs"), two, good2, method=zipfile.ZIP_STORED), big)
        with zipfile.ZipFile(big) as z: zi = z.getinfo(f"roms/{L1}.nes")
        with zipfile.ZipFile(big) as zf:
            i2 = zf.getinfo(zi.filename); i2.file_size -= 10
            try: cp._stream(zf, i2, lambda b: None); ok("entry_with_more_data_than_declared_stops", False)
            except (cp.PackageError, zipfile.BadZipFile) as e: ok("entry_with_more_data_than_declared_stops", True)
        # ZIP64 records on every entry are accepted
        z64 = craft(os.path.join(tmp, "zip64rec.ccs"), two, good2, force_zip64=True); ok("zip64_records_accepted_on_read", cp.inspect(z64)["ok"])
        # ---- ROM import ----
        calls = []
        def fake_ingest(data, fname, srcn, note, origin):
            calls.append(fname); c = cp.identify_bytes(data)["content_sha256"]
            return {"known": {"version_id": c[:16]}} if fname.startswith(tuple(x for x in (L1, L2, L3, L4))) else {"entry": {"id": "p_" + c[:16]}}
        try: cp.import_roms(bad, None, ingest=fake_ingest); ok("tampered_package_imports_nothing", False)
        except cp.PackageError: ok("tampered_package_imports_nothing", calls == [])
        try: cp.import_roms(wr, None, ingest=fake_ingest); ok("wrong_revision_package_imports_nothing", False)
        except cp.PackageError: ok("wrong_revision_package_imports_nothing", calls == [])
        r = cp.import_roms(out, [L2], ingest=fake_ingest)
        ok("only_accepted_files_imported", [x["version_id"] for x in r["imported"]] == [L2] and len(calls) == 1)
        r = cp.import_roms(out, None, ingest=fake_ingest)
        ok("each_file_kept_under_its_own_exact_identity", all(x["exact"] for x in r["imported"]) and len(r["imported"]) == 6 and not r["rejected"])
        r = cp.import_roms(out, [L1], ingest=lambda data, *a: {"entry": {"id": "p_0000000000000000"}})
        ok("a_different_identity_is_never_substituted", not r["imported"] and r["rejected"][0]["stored_as"] == "p_0000000000000000")
        r = cp.import_roms(out, [L1], ingest=lambda data, *a: {"entry": {"id": "p_" + L1}})
        ok("library_version_unknown_here_kept_as_exact_personal_copy", r["imported"] and r["imported"][0]["stored_as"] == "p_" + L1)
        # real personal-store ingest (isolated store), with the network blocked
        import personal as ps
        with NoNetwork() as nn:
            t0 = time.time(); nw = os.path.join(tmp, "nonet.ccs"); cp.write(nw, collection=collection([P5, P6], titles), roms=roms)
            ni = cp.inspect(nw); rr = cp.import_roms(nw, None)
            timings["no_network_write_inspect_import_s"] = round(time.time() - t0, 2)
        stored = ps.load_index()["entries"]
        ok("real_ingest_stores_exact_personal_games", sorted(x["stored_as"] for x in rr["imported"]) == sorted([P5, P6]) and P5 in stored and P6 in stored and open(stored[P5]["managed_copy"], "rb").read() == r5, rr)
        ok("no_network_attempted_by_write_inspect_import", ni["ok"] and nn.attempts == [], nn.attempts[:3])
        ok("fixtures_carry_the_sentinel", all(SENTINEL in open(src[i], "rb").read() for i in ids) and SENTINEL in zipfile.ZipFile(nw).read(f"roms/{P5}.nes"))
        # ---- scale ----
        # 3,000 games (small synthetic personal ROMs), streamed write, full verification, import with a fake store
        many = os.path.join(tmp, "many"); os.makedirs(many); mids, mroms = [], []
        t0 = time.time()
        for k in range(3000):
            d, c = make_rom(f"many-{k}", size=8192, header=k % 3 != 0); pid = "p_" + c[:16]; p = os.path.join(many, f"{k}.nes"); open(p, "wb").write(d); mids.append(pid); mroms.append({"version_id": pid, "path": p, "title": f"Game {k}"})
        for r_ in mroms: open(r_["path"], "rb").read(4)   # the first open of a just-created file is scanned by the antivirus (~35 ms each here); time the package code, not that
        timings["games_3000_fixture_create_and_first_open_s"] = round(time.time() - t0, 2)
        mp = os.path.join(tmp, "many.ccs"); t1 = time.time(); mm = cp.write(mp, collection=collection(mids, {}, "Three thousand"), roms=mroms); t2 = time.time(); mi = cp.inspect(mp); t3 = time.time()
        mr = cp.import_roms(mp, None, ingest=lambda data, *a: {"entry": {"id": "p_" + cp.identify_bytes(data)["content_sha256"][:16]}}); t4 = time.time()
        timings.update(games_3000_write_s=round(t2 - t1, 2), games_3000_inspect_s=round(t3 - t2, 2), games_3000_import_s=round(t4 - t3, 2), games_3000_bytes=os.path.getsize(mp))
        ok("three_thousand_games_round_trip", mi["ok"] and mi["games"] == 3000 and mi["rom_count"] == 3000 and len(mr["imported"]) == 3000 and mm["summary"]["games"] == 3000)
        # one 64 MB ROM (no size limit), streamed
        bigc = os.urandom(64 * 1024 * 1024 - 16); bigd = b"NES\x1a" + bytes([255, 0, 0]) + bytes(9) + SENTINEL + bigc[len(SENTINEL):]
        bp = put("huge.nes", bigd); bc = hashlib.sha256(bigd[16:]).hexdigest(); bid = "p_" + bc[:16]; del bigc
        bo = os.path.join(tmp, "huge.ccs"); t0 = time.time(); cp.write(bo, collection=collection([bid]), roms=[{"version_id": bid, "path": bp}], rom_compression=zipfile.ZIP_STORED); t1 = time.time(); bi = cp.inspect(bo); t2 = time.time()
        timings.update(rom_64mb_write_s=round(t1 - t0, 2), rom_64mb_inspect_s=round(t2 - t1, 2))
        ok("rom_of_64_mb_round_trip", bi["ok"] and bi["roms"][0]["size"] == len(bigd) and bi["roms"][0]["verified"] is True)
        del bigd
        # more than 65,535 entries (ZIP64 entry count): 65,600 thumbnails plus a game
        art_many = {f"a{k:05d}.png": b"\x89PNG" + k.to_bytes(4, "big") for k in range(65600)}
        ep = os.path.join(tmp, "entries.ccs"); t0 = time.time(); cp.write(ep, collection=collection([P5]), roms=roms, art=art_many); t1 = time.time(); ei = cp.inspect(ep); t2 = time.time()
        with open(ep, "rb") as f: f.seek(-22, 2); eocd = f.read(22)
        timings.update(entries_65604_write_s=round(t1 - t0, 2), entries_65604_inspect_s=round(t2 - t1, 2))
        ok("more_than_65535_entries_round_trip", ei["ok"] and len(ei["art"]) == 65600 and len(zipfile.ZipFile(ep).infolist()) == 65604)
        ok("entry_count_uses_zip64_end_record", eocd[:4] == b"PK\x05\x06" and int.from_bytes(eocd[10:12], "little") == 0xFFFF, eocd.hex())
    finally: shutil.rmtree(tmp, ignore_errors=True)
    print("TIMINGS:", json.dumps(timings))
    bad = [k for k, v in checks if not v]; print("RESULT:", "PASS" if not bad else "FAIL " + json.dumps(bad), f"({len(checks)} checks)"); return 0 if not bad else 1

if __name__ == "__main__": sys.exit(main())
