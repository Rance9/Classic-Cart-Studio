r"""A real .ccs package larger than 4 GB (ZIP64 sizes and offsets), written and read back by ccs_package.py. Python only, no browser.
  70 synthetic personal games of 64 MB each (incompressible content, a sentinel, iNES header), stored entries: about 4.5 GB.
  write (streamed), inspect with full verification (every ROM's SHA-256 and content identity), the website-handoff fast path
  (verify_roms=False), import_roms with a counting store, ZIP64 end records present, the last entry's offset past 4 GiB, and the
  process's peak memory well below the package size (nothing reads the whole archive into memory).
  Work folder C:\NES\work\ccs-big (created, deleted afterwards). Needs 12 GB free, otherwise prints a SKIP line and exits 0.
  python test_ccs_package_big.py [--games 70] [--mb 64]      Exit 0 = PASS (or SKIP)."""
from __future__ import annotations
import argparse, ctypes, hashlib, json, os, shutil, sys, time, zipfile
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path[:0] = [HERE, os.path.join(HERE, "..", "rom-pipeline")]
import ccs_package as cp
from test_ccs_package import SENTINEL, collection, NoNetwork

WORK = r"C:\NES\work\ccs-big"

def peak_mb() -> float:
    class PMC(ctypes.Structure):
        _fields_ = [("cb", ctypes.c_ulong), ("PageFaultCount", ctypes.c_ulong)] + [(n, ctypes.c_size_t) for n in ("PeakWorkingSetSize", "WorkingSetSize", "QuotaPeakPagedPoolUsage", "QuotaPagedPoolUsage", "QuotaPeakNonPagedPoolUsage", "QuotaNonPagedPoolUsage", "PagefileUsage", "PeakPagefileUsage")]
    c = PMC(); c.cb = ctypes.sizeof(c); k = ctypes.windll.kernel32
    k.GetCurrentProcess.restype = ctypes.c_void_p; k.K32GetProcessMemoryInfo.argtypes = [ctypes.c_void_p, ctypes.POINTER(PMC), ctypes.c_ulong]
    if not k.K32GetProcessMemoryInfo(k.GetCurrentProcess(), ctypes.byref(c), c.cb): return -1.0
    return c.PeakWorkingSetSize / 1024 ** 2

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--games", type=int, default=70); ap.add_argument("--mb", type=int, default=64); a = ap.parse_args()
    os.makedirs(os.path.dirname(WORK), exist_ok=True); free = shutil.disk_usage(os.path.dirname(WORK)).free
    if free < 12 * 10**9: print(f"SKIP: only {cp.fmt_size(free)} free on the drive holding {WORK}; this test needs 12 GB."); return 0
    checks = []; t = {}
    def ok(k, v, n=""): checks.append((k, bool(v))); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:300]})" if n and not v else ""), flush=True)
    shutil.rmtree(WORK, ignore_errors=True); os.makedirs(os.path.join(WORK, "src"))
    try:
        size = a.mb * 1024 ** 2; base = bytearray(os.urandom(size)); ids, roms = [], []
        t0 = time.time()
        for k in range(a.games):
            uniq = SENTINEL + hashlib.sha256(f"big-{k}".encode()).digest()
            base[:len(uniq)] = uniq
            hdr = b"NES\x1a" + bytes([255, 0, 0]) + bytes(9)
            c = hashlib.sha256(base).hexdigest(); pid = "p_" + c[:16]; p = os.path.join(WORK, "src", f"g{k:03d}.nes")
            with open(p, "wb") as f: f.write(hdr); f.write(base)
            ids.append(pid); roms.append({"version_id": pid, "path": p, "title": f"Big game {k}"})
        del base
        for r in roms:
            with open(r["path"], "rb") as f: f.read(4)
        t["fixtures_s"] = round(time.time() - t0, 1)
        pk = os.path.join(WORK, "big collection.ccs"); coll = collection(ids, {}, "Big collection")
        with NoNetwork() as nn:
            t0 = time.time(); man = cp.write(pk, collection=coll, roms=roms, rom_compression=zipfile.ZIP_STORED); t["write_s"] = round(time.time() - t0, 1)
            n = os.path.getsize(pk); t["package_bytes"] = n; t["package_size"] = cp.fmt_size(n)
            t0 = time.time(); info = cp.inspect(pk); t["inspect_verify_s"] = round(time.time() - t0, 1)
            t0 = time.time(); fast = cp.inspect(pk, verify_roms=False); t["inspect_listing_only_s"] = round(time.time() - t0, 2)
            seen = []
            def count_ingest(data, fname, *rest):
                seen.append(len(data)); return {"entry": {"id": "p_" + cp.identify_bytes(data)["content_sha256"][:16]}}
            t0 = time.time(); imp = cp.import_roms(pk, None, ingest=count_ingest); t["import_s"] = round(time.time() - t0, 1)
        ok("package_larger_than_4_gib", n > 4 * 1024 ** 3, n)   # the ZIP32 boundary (2^32 bytes) is binary by definition
        ok("written_package_verifies_completely", info["ok"] and info["games"] == a.games and all(r["verified"] is True for r in info["roms"]) and info["rom_bytes"] == a.games * (size + 16))
        ok("listing_only_path_accepts_it", fast["ok"] and all(r["verified"] is None for r in fast["roms"]))
        ok("import_receives_every_exact_rom", len(imp["imported"]) == a.games and not imp["rejected"] and all(x["exact"] for x in imp["imported"]))
        ok("no_network_attempted", nn.attempts == [], nn.attempts[:3])
        with open(pk, "rb") as f:
            f.seek(-(22 + 20 + 56), 2); tail = f.read()
        ok("zip64_end_records_present", tail[:4] == b"PK\x06\x06" and tail[56:60] == b"PK\x06\x07", tail[:4] + tail[56:60])
        with zipfile.ZipFile(pk) as z: last = max(z.infolist(), key=lambda i: i.header_offset)
        ok("entries_beyond_4_gib_offset", last.header_offset > 0xFFFFFFFF, last.header_offset)
        ok("summary_matches", man["summary"]["games"] == a.games and man["summary"]["rom_bytes"] == a.games * (size + 16))
        # a tampered byte deep inside the package (past 4 GiB) is found
        with zipfile.ZipFile(pk) as z: zi = z.getinfo(f"roms/{ids[-1]}.nes"); off = zi.header_offset + 30 + len(zi.filename.encode()) + len(zi.extra) + 1000
        with open(pk, "r+b") as f: f.seek(off); b = f.read(1); f.seek(off); f.write(bytes([b[0] ^ 0xFF]))
        t0 = time.time()
        try: ti = cp.inspect(pk); found = not ti["ok"] and any("checksum" in p["problem"] or "damaged" in p["problem"] for p in ti["problems"])
        except cp.PackageError: found = True
        t["inspect_tampered_s"] = round(time.time() - t0, 1)
        ok("tampered_byte_past_4_gib_detected", found)
        t["peak_working_set"] = cp.fmt_size(int(peak_mb() * 1024 * 1024))
        ok("peak_memory_far_below_package_size", 0 < peak_mb() * 1024 * 1024 < 10**9, t["peak_working_set"])
    finally:
        shutil.rmtree(WORK, ignore_errors=True)
        ok("work_folder_deleted", not os.path.exists(WORK))
    print("TIMINGS:", json.dumps(t))
    bad = [k for k, v in checks if not v]; print("RESULT:", "PASS" if not bad else "FAIL " + json.dumps(bad), f"({len(checks)} checks)"); return 0 if not bad else 1

if __name__ == "__main__": sys.exit(main())
