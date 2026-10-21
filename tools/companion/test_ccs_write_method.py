r"""How the Windows app's package writer stores each ROM (ccs_package.write): one compression pass for ordinary ROMs, none for ROMs that do
not compress, and never an entry that readers would refuse. Python only; synthetic ROMs; no network.
  incompressible   random bytes are stored without compressing (no full measuring pass, no compression pass)
  ordinary         NES-like ROMs are compressed once while written (no separate measuring pass) and shrink as before
  mostly padding   samples that shrink more than 20:1 measure the whole file first; beyond 200:1 the ROM is stored
  missed padding   a ROM whose samples look ordinary but whose unsampled bytes are padding is compressed, found beyond 200:1 after
                   writing, taken back and stored; the package is valid and progress never moves backward
  every package    reads back completely with every ROM verified; readers accept every entry's ratio
  python test_ccs_write_method.py      Exit 0 = PASS."""
from __future__ import annotations
import hashlib, os, random, shutil, sys, tempfile, zipfile
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path[:0] = [HERE, os.path.join(HERE, "..", "rom-pipeline")]
import ccs_package as cp
MB = 10**6

def nes(content: bytes) -> bytes: return b"NES\x1a" + bytes([8, 1, 0, 0]) + bytes(8) + content

def nes_like(seed, size):
    rng = random.Random(seed); b = bytearray()
    while len(b) < size:
        k = rng.random()
        if k < 0.4: b += rng.randbytes(rng.randint(32, 300))
        elif k < 0.54: b += bytes([0xFF]) * rng.randint(16, 400)
        else: t = rng.randbytes(rng.randint(2, 24)); b += t * rng.randint(2, 12)
    return bytes(b[:size])

def main():
    checks = []
    def ok(k, v, n=""): checks.append((k, bool(v))); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:300]})" if n and not v else ""), flush=True)
    tmp = tempfile.mkdtemp(prefix="ccs-method-")
    try:
        def put(name, data):
            p = os.path.join(tmp, name); open(p, "wb").write(data); return p
        missed = bytearray(200 * MB)   # samples at the start, middle and end look like an ordinary ROM; everything else is zeros
        for o in (0, (len(missed) - cp.SAMPLE) // 2, len(missed) - cp.SAMPLE): missed[o:o + cp.SAMPLE] = nes_like(f"m{o}", cp.SAMPLE)
        roms = {"random": put("random.nes", nes(random.Random(1).randbytes(3 * MB))), "ordinary": put("ordinary.nes", nes(nes_like("o", 262144))),
                "padding": put("padding.nes", nes(bytes(40 * MB))), "missed": put("missed.nes", nes(bytes(missed)))}
        calls = []; real = cp._deflate_ok
        cp._deflate_ok = lambda p, c=lambda: False: (calls.append(os.path.basename(p)), real(p, c))[1]
        plan = [{"version_id": "p_" + cp.identify_file(p)["content_sha256"][:16], "path": p, "title": k} for k, p in roms.items()]
        coll = {"name": "Methods", "ccs_collection": {"schema": "ccs-collection/1", "name": "Methods", "sel": [r["version_id"] for r in plan]}}
        seen = []; out = os.path.join(tmp, "Methods.ccs")
        cp.write(out, collection=coll, roms=plan, progress=lambda d, t: seen.append(d))
        cp._deflate_ok = real
        with zipfile.ZipFile(out) as z: info = {next(r["title"] for r in plan if i.filename == f"roms/{r['version_id']}.nes"): i for i in z.infolist() if i.filename.startswith("roms/")}
        m = {k: ("stored" if i.compress_type == 0 else "deflate", round(i.compress_size / i.file_size, 4)) for k, i in info.items()}
        print("   entries:", m, "| full measuring pass for:", calls)
        ok("incompressible_rom_stored_without_compressing", m["random"][0] == "stored" and "random.nes" not in calls)
        ok("ordinary_rom_compressed_in_one_pass", m["ordinary"][0] == "deflate" and m["ordinary"][1] < 0.8 and "ordinary.nes" not in calls)
        ok("mostly_padding_measured_first_then_stored", m["padding"][0] == "stored" and "padding.nes" in calls)
        ok("missed_padding_taken_back_and_stored", m["missed"][0] == "stored" and "missed.nes" not in calls)
        ok("every_entry_within_the_readers_ratio", all(i.compress_type == 0 or i.file_size / i.compress_size <= cp.MAX_RATIO / 2 for i in info.values()))
        res = cp.inspect(out, verify_roms=True)
        ok("package_reads_back_with_every_rom_verified", res["ok"] and res["rom_count"] == 4 and all(r["verified"] for r in res["roms"]), res.get("problems"))
        ok("progress_never_moves_backward", all(b >= a for a, b in zip(seen, seen[1:])), [(a, b) for a, b in zip(seen, seen[1:]) if b < a][:3])
        with zipfile.ZipFile(out) as z: names = [i.filename for i in z.infolist()]
        ok("no_leftover_entry_from_the_taken_back_write", len(names) == len(set(names)) == 7, names)
    finally: shutil.rmtree(tmp, ignore_errors=True)
    bad = [k for k, v in checks if not v]; print("RESULT:", "PASS" if not bad else "FAIL " + str(bad), f"({len(checks)} checks)"); return 0 if not bad else 1

if __name__ == "__main__": sys.exit(main())
