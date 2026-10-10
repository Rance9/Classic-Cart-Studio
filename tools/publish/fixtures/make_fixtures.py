"""Synthetic test fixtures for the public repository: ROM-shaped files that are NOT games (random bytes under a valid iNES header,
plus one headerless blob), a ZIP containing some of them, a duplicate, a too-small junk file and a compression-bomb ZIP. They
exercise the import, identification, dedupe and safety paths without any copyrighted content. Deterministic (seeded).

  python make_fixtures.py <out_dir>   -> writes files and fixtures.json (names, sizes, sha-256 of file and content)
"""
from __future__ import annotations
import os, sys, json, random, hashlib, zipfile

def ines(prg_banks: int, chr_banks: int, mapper: int, body: bytes) -> bytes:
    return b"NES\x1a" + bytes([prg_banks, chr_banks, (mapper & 0x0F) << 4, mapper & 0xF0]) + b"\0" * 8 + body

def main(out: str):
    os.makedirs(os.path.join(out, "sub"), exist_ok=True); rnd = random.Random(20261010); rec = []
    def put(rel: str, data: bytes, note: str):
        p = os.path.join(out, rel); open(p, "wb").write(data)
        content = data[16:] if data[:4] == b"NES\x1a" else data
        rec.append({"file": rel, "bytes": len(data), "file_sha256": hashlib.sha256(data).hexdigest(), "content_sha256": hashlib.sha256(content).hexdigest(), "note": note})
        return data
    a = put("alpha.nes", ines(2, 1, 0, bytes(rnd.getrandbits(8) for _ in range(40960))), "synthetic headered file (not a game)")
    b = put("sub/beta.unh", bytes(rnd.getrandbits(8) for _ in range(32768)), "synthetic headerless file")
    c = put("gamma.nes", ines(1, 1, 3, bytes(rnd.getrandbits(8) for _ in range(24576))), "synthetic headered file, mapper 3")
    d = ines(4, 0, 1, bytes(rnd.getrandbits(8) for _ in range(65536)))
    with zipfile.ZipFile(os.path.join(out, "pack.zip"), "w", zipfile.ZIP_DEFLATED) as z: z.writestr("delta.nes", d); z.writestr("dup-of-alpha.nes", a); z.writestr("readme.txt", "not a rom")
    rec.append({"file": "pack.zip", "members": ["delta.nes", "dup-of-alpha.nes", "readme.txt"], "delta_content_sha256": hashlib.sha256(d[16:]).hexdigest(), "note": "archive with two synthetic files and a duplicate of alpha.nes"})
    put("junk.nes", b"hello", "too small to be a game: must be rejected")
    with zipfile.ZipFile(os.path.join(out, "bomb.zip"), "w", zipfile.ZIP_DEFLATED) as z: z.writestr("zeros.nes", b"\0" * (4 * 1024 * 1024))
    rec.append({"file": "bomb.zip", "note": "suspicious compression ratio: must be rejected"})
    open(os.path.join(out, "notes.txt"), "w").write("ignored by the importer\n")
    json.dump(rec, open(os.path.join(out, "fixtures.json"), "w"), indent=1); print("fixtures written to", out, len(rec))

if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out"))
