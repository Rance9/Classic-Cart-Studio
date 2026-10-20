r"""The .ccs package format (ccs_package.py): round trip, game files with checksums, and untrusted-input safety.
  round trip       a collection with loadouts, art and two game files is written and read back identically; the README is listed
  integrity        a changed byte, a missing listed file, an unlisted file, a size that differs: all reported, nothing imported
  safety           '..', absolute, drive-letter and backslash names; duplicate entries; symlinks; a decompression bomb; a wrong format,
                   a newer schema, a malformed manifest: all refused with a plain reason
  rom import       only after verification, only the accepted files, each kept under its own identity (never a substitute); a failed
                   package imports nothing; a cancelled export leaves no file
  python test_ccs_package.py      Exit 0 = PASS."""
from __future__ import annotations
import hashlib, io, json, os, shutil, stat, sys, tempfile, zipfile
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import ccs_package as cp

def main():
    checks = []
    def ok(k, v, n=""): checks.append((k, bool(v))); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:300]})" if n and not v else ""))
    tmp = tempfile.mkdtemp(prefix="ccs-pkg-")
    try:
        rom_a = b"NES\x1a" + bytes(12) + bytes(range(256)) * 160; rom_b = bytes(range(255, -1, -1)) * 128   # one headered, one headerless
        pa, pb = os.path.join(tmp, "a.nes"), os.path.join(tmp, "b.unh"); open(pa, "wb").write(rom_a); open(pb, "wb").write(rom_b)
        coll = {"schema": "nes-classic-collection/4", "name": "Héllo Wörld collection", "count": 3, "selections": [{"version_id": "abcdef0123456789"}, {"version_id": "p_1111222233334444"}, {"version_id": "x_unknown"}],
                "ccs_collection": {"schema": "ccs-collection/1", "name": "Héllo Wörld collection", "sel": ["abcdef0123456789", "p_1111222233334444", "x_unknown"], "loadouts": [{"id": "l1", "name": "Party", "sel": ["abcdef0123456789"], "home": {"theme": "arcade-grid"}}]}}
        out = os.path.join(tmp, "My Collection (test) ünïcode.ccs")
        man = cp.write(out, collection=coll, art={"p_1111222233334444.jpg": b"\xff\xd8\xff" + b"x" * 100 + b"\xff\xd9"},
                       roms=[{"version_id": "abcdef0123456789", "path": pa, "title": "A"}, {"version_id": "p_1111222233334444", "path": pb, "title": "B"}],
                       missing=[{"version_id": "x_unknown", "title": "Unknown", "reason": "the game file is not on this computer"}], summary={"games": 3, "loadouts": 1}, app_version="0.7.0")
        info = cp.inspect(out)
        ok("round_trip_collection_and_loadouts_identical", info["ok"] and info["data"] == coll and info["kind"] == "collection")
        ok("roms_listed_with_sha256_and_verified", info["rom_count"] == 2 and all(r["verified"] for r in info["roms"]) and {r["sha256"] for r in info["roms"]} == {hashlib.sha256(rom_a).hexdigest(), hashlib.sha256(rom_b).hexdigest()})
        ok("missing_roms_reported_not_included", man["roms"]["missing"][0]["version_id"] == "x_unknown" and "x_unknown" not in [r["version_id"] for r in man["roms"]["included"]])
        ok("every_archive_file_listed_in_the_manifest", set(zipfile.ZipFile(out).namelist()) == {f["path"] for f in man["files"]} | {cp.MANIFEST} and "README.txt" in zipfile.ZipFile(out).namelist())
        free = os.path.join(tmp, "free.ccs"); cp.write(free, collection=coll)
        fi = cp.inspect(free); ok("rom_free_package_valid", fi["ok"] and fi["rom_count"] == 0 and fi["data"] == coll)
        # ---- integrity ----
        def rewrite(src, dst, mutate):
            with zipfile.ZipFile(src) as zi, zipfile.ZipFile(dst, "w") as zo:
                items = [(i, zi.read(i.filename)) for i in zi.infolist()]
                for i, data in mutate(items): zo.writestr(i, data)
        def expect_problem(name, mutate, needle):
            dst = os.path.join(tmp, name + ".ccs"); rewrite(out, dst, mutate)
            try: r = cp.inspect(dst); got = "; ".join(p["problem"] for p in r["problems"]) if not r["ok"] else "accepted"
            except cp.PackageError as e: got = str(e)
            ok(name, needle in got, got); return dst
        flip = lambda items: [(i, (d[:-1] + bytes([d[-1] ^ 1])) if i.filename.startswith("roms/a") or i.filename == "roms/abcdef0123456789.nes" else d) for i, d in items]
        bad = expect_problem("changed_byte_detected", flip, "checksum does not match")
        expect_problem("missing_listed_file_detected", lambda items: [(i, d) for i, d in items if not i.filename.startswith("roms/p_")], "missing from the package")
        expect_problem("unlisted_file_refused", lambda items: items + [(zipfile.ZipInfo("art/extra.jpg"), b"\xff\xd8x\xff\xd9")], "does not list")
        # ---- safety ----
        def raw(name, entries):
            dst = os.path.join(tmp, name + ".ccs")
            with zipfile.ZipFile(dst, "w") as z:
                for n, d in entries:
                    zi = n if isinstance(n, zipfile.ZipInfo) else zipfile.ZipInfo(n); z.writestr(zi, d)
            return dst
        good_man = zipfile.ZipFile(out).read(cp.MANIFEST)
        def refused(name, path, needle):
            try: cp.inspect(path); ok(name, False, "accepted")
            except cp.PackageError as e: ok(name, needle in str(e), str(e))
        import warnings; warnings.simplefilter("ignore")
        for n, bad_name in (("dotdot", "../evil.nes"), ("absolute", "/etc/evil"), ("drive", "C:/evil.nes"), ("backslash", "roms\\..\\..\\x.nes"), ("exe", "roms/tool.exe")):
            refused(f"unsafe_name_refused_{n}", raw(n, [(cp.MANIFEST, good_man), (bad_name, b"x")]), "unsafe")
        dup = os.path.join(tmp, "dup.ccs")
        with zipfile.ZipFile(dup, "w") as z: z.writestr(cp.MANIFEST, good_man); z.writestr("collection.json", b"{}"); z.writestr("collection.json", b"{}")
        refused("duplicate_entries_refused", dup, "duplicate")
        ln = zipfile.ZipInfo("roms/link.nes"); ln.external_attr = (stat.S_IFLNK | 0o777) << 16
        refused("symlink_refused", raw("link", [(cp.MANIFEST, good_man), (ln, b"/etc/passwd")]), "links")
        bomb = os.path.join(tmp, "bomb.ccs")
        with zipfile.ZipFile(bomb, "w", compression=zipfile.ZIP_DEFLATED) as z: z.writestr(cp.MANIFEST, good_man); z.writestr("collection.json", b"\0" * (40 * 1024 * 1024))
        refused("decompression_bomb_refused", bomb, "ratio")
        refused("wrong_format_refused", raw("fmt", [(cp.MANIFEST, json.dumps({"format": "other"}).encode())]), "not a Classic Cart Studio package")
        refused("newer_schema_refused", raw("schema", [(cp.MANIFEST, json.dumps({"format": cp.FORMAT, "schema": "ccs-package/9", "files": []}).encode())]), "newer")
        refused("malformed_manifest_refused", raw("malformed", [(cp.MANIFEST, b"{not json")]), "not valid")
        refused("not_a_zip_refused", (lambda p: (open(p, "wb").write(b"hello"), p)[1])(os.path.join(tmp, "plain.ccs")), "not a valid archive")
        # ---- ROM import ----
        calls = []
        def fake_ingest(data, fname, src, note, origin):
            calls.append(fname); h = hashlib.sha256(data).hexdigest()
            return {"known": {"version_id": "abcdef0123456789"}} if data == rom_a else {"entry": {"id": "p_1111222233334444" if data == rom_b else "p_other"}}
        try: cp.import_roms(bad, None, ingest=fake_ingest); ok("damaged_package_imports_nothing", False)
        except cp.PackageError: ok("damaged_package_imports_nothing", calls == [])
        r = cp.import_roms(out, ["abcdef0123456789"], ingest=fake_ingest)
        ok("only_accepted_files_imported", [x["version_id"] for x in r["imported"]] == ["abcdef0123456789"] and len(calls) == 1)
        r = cp.import_roms(out, None, ingest=fake_ingest)
        ok("each_file_kept_under_its_own_exact_identity", all(x["exact"] for x in r["imported"]) and len(r["imported"]) == 2 and not r["rejected"])
        wrong = lambda data, *a: {"entry": {"id": "p_other"}}
        r = cp.import_roms(out, ["abcdef0123456789"], ingest=wrong)
        ok("a_different_version_is_never_substituted", not r["imported"] and r["rejected"][0]["stored_as"] == "p_other")
        cancelled = os.path.join(tmp, "cancel.ccs")
        try: cp.write(cancelled, collection=coll, roms=[{"version_id": "abcdef0123456789", "path": pa}], cancel=lambda: True)
        except cp.PackageError: pass
        ok("cancelled_export_leaves_no_file", not os.path.exists(cancelled) and not os.path.exists(cancelled + ".part"))
    finally: shutil.rmtree(tmp, ignore_errors=True)
    bad = [k for k, v in checks if not v]; print("RESULT:", "PASS" if not bad else "FAIL " + json.dumps(bad), f"({len(checks)} checks)"); return 0 if not bad else 1

if __name__ == "__main__": sys.exit(main())
