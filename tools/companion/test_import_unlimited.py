r"""ROM imports in the Windows app have no size or count limit (personal.py): loose ROM files and ZIPs of any size and ZIPs with many ROMs
import, each ROM streamed in 1 MB pieces while it is identified (memory stays bounded), and unsafe or damaged archives are still refused
with nothing imported; a revision is never substituted. Python only; isolated data folders; synthetic ROMs (random bytes behind an iNES
header, not games); no network.
  loose          1 MB, 12 MB and 64 MB ROM files import under their exact content identity
  zip            a 40 MB ZIP (one ROM) and a ZIP of 1,000 ROMs (Add Games from Files) import completely
  revision       an exact content match becomes the known version; Locate and relink accept only the exact revision
  refused        damaged and truncated archives, unsafe paths, links, encryption, duplicate names, a decompression bomb, a member that
                 lies about its size, a drive without the space: refused, nothing imported, no temporary files left
  memory         peak Python memory while importing the 64 MB file and the 40 MB ZIP stays near the 1 MB read size
  package        a ZIP64 .ccs package with a 12 MB ROM imports exactly through the same streaming path
  python test_import_unlimited.py      Exit 0 = PASS."""
from __future__ import annotations
import hashlib, io, json, os, random, shutil, struct, sys, tempfile, time, tracemalloc, zipfile
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path[:0] = [HERE, os.path.join(HERE, "..", "rom-pipeline")]
TMP = tempfile.mkdtemp(prefix="ccs-import-")
os.environ["NES_PERSONAL_DIR"] = os.path.join(TMP, "personal"); os.environ["NES_STUDIO_DATA"] = os.path.join(TMP, "studio-data")
import ccs_package as cp, personal as ps
from test_ccs_package import collection, NoNetwork
MB = 10**6   # decimal, like everything people see (12 MB > the old 8,388,608-byte cap; the 40 MB ZIP > the old 33,554,432-byte cap)

def rom(seed, size: int, header: bool = True) -> bytes:
    content = random.Random(seed).randbytes(size)
    return (b"NES\x1a" + bytes([min(255, max(1, size // 16384)), 1, 0, 0]) + bytes(8) + content) if header else content

def put(name: str, data: bytes) -> str:
    p = os.path.join(TMP, "src", name); os.makedirs(os.path.dirname(p), exist_ok=True); open(p, "wb").write(data); return p

def zipped(name: str, members: list, compression=zipfile.ZIP_STORED) -> str:
    p = os.path.join(TMP, "src", name); os.makedirs(os.path.dirname(p), exist_ok=True)
    with zipfile.ZipFile(p, "w", compression) as z:
        for m in members:
            if isinstance(m, zipfile.ZipInfo): z.writestr(m, b"x" * 20000)
            else: z.writestr(m[0], m[1])
    return p

def patch_zip(path: str, fn):
    """fn(local_header_offset, central_record_offset, buf) edits a ZIP's single member in place (for archives no writer would make)"""
    with zipfile.ZipFile(path) as z: lo, cd = z.infolist()[0].header_offset, z.start_dir
    b = bytearray(open(path, "rb").read()); fn(lo, cd, b); open(path, "wb").write(b)

def zip64_entries(path: str) -> tuple[int, int]:
    """(entries whose local header carries a ZIP64 extra field, all entries)"""
    b = open(path, "rb").read(); n = 0
    with zipfile.ZipFile(path) as z: infos = z.infolist()
    for i in infos:
        o = i.header_offset; fl, el = struct.unpack_from("<HH", b, o + 26); ex = b[o + 30 + fl:o + 30 + fl + el]; k = 0
        while k + 4 <= len(ex):
            hid, hl = struct.unpack_from("<HH", ex, k)
            if hid == 1: n += 1; break
            k += 4 + hl
    return n, len(infos)

def ident(data: bytes) -> str: return cp.identify_bytes(data)["content_sha256"]
def sha(p: str) -> str: return hashlib.sha256(open(p, "rb").read()).hexdigest()
def leftovers() -> list: return os.listdir(ps.INCOMING) if os.path.isdir(ps.INCOMING) else []

def refused(path: str, *, single: bool = True) -> str:
    try: (ps.ingest(path) if single else list(ps._candidates(path)))
    except ValueError as e: return str(e)
    return ""

def wait_job(jid: str, timeout: float = 600) -> dict:
    t0 = time.time()
    while time.time() - t0 < timeout:
        st = ps.job_status(jid)
        if st and st["finished"]: return st
        time.sleep(0.2)
    return ps.job_status(jid) or {}

def main():
    checks = []
    def ok(k, v, n=""): checks.append((k, bool(v))); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:300]})" if n and not v else ""), flush=True)
    # a synthetic Game Library with one known version (exact content identity only)
    known = rom("known-rev1", 128 * 1024); other_rev = rom("known-rev0", 128 * 1024); K = ident(known)[:16]
    ps._LIB = {"titles": {"t1": {"id": "t1", "title": "Known Game", "curated": True}}, "by_sha": {}, "by_id": {}, "by_norm": {}}
    v = {"id": K, "name": "Known Game (Rev 1)", "title_id": "t1", "curated": True, "sha256": hashlib.sha256(known).hexdigest(), "content_sha256": ident(known)}
    ps._LIB["by_sha"][v["content_sha256"]] = v; ps._LIB["by_id"][K] = v
    try:
        with NoNetwork() as nn:
            # ---- loose files of any size ----
            res = {}
            for label, size in (("1mb", 1 * MB), ("12mb", 12 * MB), ("64mb", 64 * MB)):
                data = rom("loose-" + label, size); p = put(f"Loose {label}.nes", data); c = ident(data)
                if label == "64mb":
                    tracemalloc.start(); r = ps.ingest(p); peak_loose = tracemalloc.get_traced_memory()[1]; tracemalloc.stop()
                else: r = ps.ingest(p)
                e = r.get("entry") or {}; idx = ps.load_index()["entries"].get(e.get("id"), {})
                res[label] = e.get("id") == "p_" + c[:16] and idx.get("content_sha256") == c and idx.get("file_size") == len(data) and sha(idx["managed_copy"]) == hashlib.sha256(data).hexdigest()
            ok("loose_rom_below_8_mb_imports", res["1mb"], res)
            ok("loose_rom_above_8_mb_imports", res["12mb"] and res["64mb"], res)
            # ---- ZIPs of any size and with many ROMs ----
            big = rom("zip-40mb", 40 * MB); zp = zipped("Big archive.zip", [("Big Game (USA).nes", big)])
            tracemalloc.start(); r = ps.ingest(zp); peak_zip = tracemalloc.get_traced_memory()[1]; tracemalloc.stop()
            ok("zip_above_8_and_32_mb_imports", (r.get("entry") or {}).get("id") == "p_" + ident(big)[:16] and os.path.getsize(zp) > 32 * 1024 * 1024 and "from archive" in r["entry"]["note"], r.get("entry", {}).get("id"))
            many = [(f"Pack/Game {i:04d}.nes", rom(f"many-{i}", 16384)) for i in range(1000)]
            mz = zipped("Thousand games.zip", many, zipfile.ZIP_DEFLATED)
            st = wait_job(ps.start_import([mz]))
            ids = {x.get("personal_id") for x in ps.JOBS[st["id"]]["items"] if x.get("personal_id")}
            ok("zip_with_1000_roms_imports_every_rom", st["personal"] == 1000 and not st["errors"] and len(ids) == 1000 and ids == {"p_" + ident(d)[:16] for _, d in many}, {k: st.get(k) for k in ("personal", "duplicates", "errors")})
            mixed = zipped("Mixed.zip", [("a.nes", rom("mixed-a", 16384)), ("tiny.nes", b"NES\x1a" + bytes(200)), ("b.nes", rom("mixed-b", 16384))])
            st = wait_job(ps.start_import([mixed]))
            ok("one_unsuitable_member_does_not_stop_its_zip", st["personal"] == 2 and len(st["errors"]) == 1 and st["errors"][0].get("member") == "tiny.nes", st.get("errors"))
            # ---- exact revisions only ----
            r = ps.ingest(put("Known Game (Rev 1).nes", known))
            ok("exact_content_match_is_the_known_version", (r.get("known") or {}).get("version_id") == K and r["library_copy"]["content_sha256"] == ident(known), r.get("known"))
            ps.save_store({}); wrong = put("Known Game (Rev 0).nes", other_rev)
            try: ps.locate_version(K, wrong); loc_wrong = False
            except ValueError as e: loc_wrong = "not this version" in str(e)
            ok("locate_refuses_a_different_revision", loc_wrong and K not in ps.load_store())
            both = zipped("Both revisions.zip", [("Rev 0.nes", other_rev), ("Rev 1.nes", known)])
            got = ps.locate_version(K, both)
            ok("locate_picks_the_exact_revision_from_a_zip", got["library_copy"]["content_sha256"] == ident(known) and sha(got["library_copy"]["path"]) == hashlib.sha256(known).hexdigest(), got)
            pe = ps.ingest(put("Homebrew.nes", rom("homebrew", 32768)))["entry"]; before = sha(ps.load_index()["entries"][pe["id"]]["managed_copy"])
            try: ps.relink(pe["id"], put("Not the homebrew.nes", rom("not-homebrew", 32768))); rel = False
            except ValueError: rel = True
            ok("relink_refuses_a_different_rom_and_keeps_the_copy", rel and sha(ps.load_index()["entries"][pe["id"]]["managed_copy"]) == before)
            # ---- refused archives: nothing imported, nothing left behind ----
            n0 = len(ps.load_index()["entries"]); bad = {}
            good = rom("damaged", 64 * 1024); dz = zipped("Damaged.zip", [("Damaged.nes", good)], zipfile.ZIP_DEFLATED)
            def flip(lo, cd, b): n = struct.unpack_from("<HH", b, lo + 26); o = lo + 30 + n[0] + n[1] + 5000; b[o] ^= 0xFF
            patch_zip(dz, flip); bad["damaged_member"] = refused(dz)
            tz = put("Truncated.zip", open(zipped("t.zip", [("T.nes", rom("trunc", 64 * 1024))]), "rb").read()[:40000]); bad["truncated"] = refused(tz)
            bad["parent_path"] = refused(zipped("Evil1.zip", [("../../evil.nes", rom("e1", 16384))]))
            bad["absolute_path"] = refused(zipped("Evil2.zip", [("/abs/evil.nes", rom("e2", 16384))]))
            bad["drive_path"] = refused(zipped("Evil3.zip", [("C:/Windows/evil.nes", rom("e3", 16384))]))
            li = zipfile.ZipInfo("link.nes"); li.external_attr = (0o120777 << 16); bad["link"] = refused(zipped("Link.zip", [li]))
            ez = zipped("Encrypted.zip", [("Locked.nes", rom("enc", 16384))])
            def enc(lo, cd, b): b[lo + 6] |= 1; b[cd + 8] |= 1
            patch_zip(ez, enc); bad["encrypted"] = refused(ez)
            dup = zipfile.ZipFile(os.path.join(TMP, "src", "Duplicate.zip"), "w")
            import warnings
            with warnings.catch_warnings():
                warnings.simplefilter("ignore"); dup.writestr("Same.nes", rom("d1", 16384)); dup.writestr("Same.nes", rom("d2", 16384))
            dup.close(); bad["duplicate_names"] = refused(os.path.join(TMP, "src", "Duplicate.zip"), single=False)
            bad["decompression_bomb"] = refused(zipped("Bomb.zip", [("Bomb.nes", b"NES\x1a" + bytes(12) + bytes(50 * MB))], zipfile.ZIP_DEFLATED))
            lz = zipped("Lies.zip", [("Lies.nes", rom("lies", 64 * 1024))])
            def lie(lo, cd, b): struct.pack_into("<I", b, lo + 22, 32 * 1024); struct.pack_into("<I", b, cd + 24, 32 * 1024)
            patch_zip(lz, lie); bad["declared_size_lie"] = refused(lz)
            mz2 = zipped("Mostly fine.zip", [("ok1.nes", rom("mf1", 16384)), ("../escape.nes", rom("mf2", 16384)), ("ok2.nes", rom("mf3", 16384))])
            st = wait_job(ps.start_import([mz2])); bad["bulk_unsafe_zip_imports_nothing"] = "unsafe path" in json.dumps(st["errors"]) and st["personal"] == 0
            for k, m in bad.items(): print(f"   refused {k}: {m}")
            ok("damaged_archives_refused", bad["damaged_member"] and bad["truncated"] and bad["declared_size_lie"], {k: bad[k] for k in ("damaged_member", "truncated", "declared_size_lie")})
            ok("unsafe_archives_refused", all(bad[k] for k in ("parent_path", "absolute_path", "drive_path", "link", "encrypted", "duplicate_names", "decompression_bomb", "bulk_unsafe_zip_imports_nothing")), bad)
            ok("refused_archives_import_nothing_and_leave_nothing", len(ps.load_index()["entries"]) == n0 and not leftovers(), (len(ps.load_index()["entries"]) - n0, leftovers()))
            real = shutil.disk_usage
            try:
                ps.shutil.disk_usage = lambda p: type("U", (), {"free": 10 * MB})(); space = refused(put("Needs space.nes", rom("space", 16 * MB)))
            finally: ps.shutil.disk_usage = real
            ok("a_drive_without_the_space_is_refused_plainly", "free space" in space and not leftovers(), space)
            # ---- identity and memory ----
            idx = ps.load_index()["entries"]
            ok("every_entry_is_its_exact_content", all(e["id"] == "p_" + e["content_sha256"][:16] and sha(e["managed_copy"]) == e["file_sha256"] for e in idx.values()), len(idx))
            print(f"   peak Python memory: 64 MB file {cp.fmt_size(peak_loose)}, 40 MB ZIP {cp.fmt_size(peak_zip)}")
            ok("memory_stays_bounded_for_large_files", peak_loose < 12 * MB and peak_zip < 12 * MB, {"64 MB file peak": cp.fmt_size(peak_loose), "40 MB zip peak": cp.fmt_size(peak_zip)})
            # ---- .ccs packages (ZIP64) through the same streaming path ----
            rs = [rom("pkg-1", 12 * MB), rom("pkg-2", 16384), rom("pkg-3", 16384, header=False)]
            pids = ["p_" + ident(d)[:16] for d in rs]; files = [put(f"pkg{i}.{'nes' if i < 2 else 'unh'}", d) for i, d in enumerate(rs)]
            pk1 = os.path.join(TMP, "Package.ccs"); cp.write(pk1, collection=collection(pids), roms=[{"version_id": v, "path": f, "title": f"Package Game {i}"} for i, (v, f) in enumerate(zip(pids, files))])
            pk64 = os.path.join(TMP, "Package ZIP64.ccs")
            with zipfile.ZipFile(pk1) as zi, zipfile.ZipFile(pk64, "w", allowZip64=True) as zo:
                for i in zi.infolist():
                    ni = zipfile.ZipInfo(i.filename, i.date_time); ni.compress_type = i.compress_type
                    with zi.open(i) as s, zo.open(ni, "w", force_zip64=True) as d: shutil.copyfileobj(s, d, MB)
            is64 = zip64_entries(pk64); print(f"   ZIP64 entries in the package: {is64[0]} of {is64[1]}")
            idx = ps.load_index(); [idx["entries"].pop(p, None) for p in pids]; ps.save_index(idx)
            r = cp.import_roms(pk64, None)
            idx = ps.load_index()["entries"]
            ok("zip64_package_imports_exactly_through_the_streaming_path", cp.inspect(pk64)["ok"] and [x["stored_as"] for x in r["imported"]] == pids and not r["rejected"] and all(sha(idx[p]["managed_copy"]) == hashlib.sha256(d).hexdigest() for p, d in zip(pids, rs)) and not leftovers() and is64[0] == is64[1] > 0, (is64, r))
            ok("no_network_used", not nn.attempts, nn.attempts[:3])
    finally:
        shutil.rmtree(TMP, ignore_errors=True)
    bad = [k for k, v in checks if not v]; print("RESULT:", "PASS" if not bad else "FAIL " + str(bad), f"({len(checks)} checks)"); return 0 if not bad else 1

if __name__ == "__main__": sys.exit(main())
