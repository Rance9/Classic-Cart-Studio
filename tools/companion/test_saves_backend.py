r"""The Windows app's routes for this package, from the source backend on loopback with throwaway data folders (never the person's):
  saves      /api/studio/saves-export writes a Game Saves file (path given: rehearsal configuration, no dialog); a second backend stages it
             (/api/studio/saves-stage, the bytes as the page sends them), plans it and imports it (/api/studio/saves-import): the files
             arrive byte for byte; the website's origin without the desktop principal is refused
  included   /api/personal/included with keys keeps only the games asked for
  headerless the headerless Included Game Thwaite is kept with its recorded header; headerless bytes that are no known game are refused with
             the reason (Add ROM Files' path); nothing else is kept
  python test_saves_backend.py <built site folder> [--out results.json]      Exit 0 = PASS. Starts and stops only its own backends."""
from __future__ import annotations
import argparse, hashlib, json, os, shutil, sys, tempfile, urllib.error, urllib.request, zlib
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from test_included_backend import Backend

def state(sha, payload, created):
    h = json.dumps({"schema": "ccs-save-state/1", "kind": "manual", "rom_content_sha256": sha, "title": "T", "core": "fceumm", "core_version": "ac0d00a7", "platform": "windows-x86_64",
                    "size": len(payload), "crc32": f"{zlib.crc32(payload) & 0xFFFFFFFF:08x}", "frames": 1, "created": created}).encode()
    return b"CCSSTATE" + len(h).to_bytes(4, "little") + h + payload

def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")   # check names carry arrows and dashes; any console prints them
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); a = ap.parse_args()
    site = os.path.abspath(a.site); idx = json.load(open(os.path.join(site, "included", "index.json"), encoding="utf-8")); th = idx["games"][0]
    work = tempfile.mkdtemp(prefix="ccs-saves-backend-"); cfg = os.path.join(work, "config.json"); res = []
    os.makedirs(os.path.join(work, "tree")); json.dump({"rehearsal_tree": os.path.join(work, "tree")}, open(cfg, "w"))
    def ok(n, c, d=""): res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:300]})" if d and not c else ""), flush=True)
    A = B = None; S = th["content_sha256"]
    try:
        for n in ("A", "B"): os.makedirs(os.path.join(work, n))
        d = os.path.join(work, "A", "saves", S); os.makedirs(d); s1 = state(S, b"\x11" * 200, "2026-11-12T10:00:00Z"); cart = b"\xA5" * 8192
        open(os.path.join(d, "slot1.ccsstate"), "wb").write(s1); open(os.path.join(d, "cart.sav"), "wb").write(cart)
        A = Backend(site, os.path.join(work, "A"), cfg); B = Backend(site, os.path.join(work, "B"), cfg)
        caps = A.call("/api/version", get=True).get("capabilities") or []
        ok("the app says it can move Game Saves (capability game-saves)", "game-saves" in caps, caps)
        out = os.path.join(work, "saves.zip"); r = A.call("/api/studio/saves-export", {"path": out})
        ok("saves: export writes the file", r.get("ok") and os.path.isfile(out) and r.get("games") == 1 and r.get("files") == 2, r)
        body = open(out, "rb").read()
        q = urllib.request.Request(B.base + "/api/studio/saves-stage", data=body, headers={"Content-Type": "application/octet-stream", "Origin": B.base, "Authorization": "Bearer " + B.tok})
        with urllib.request.urlopen(q, timeout=60) as resp: p = json.loads(resp.read())
        ok("saves: the second app stages and plans it (nothing written yet)", p.get("ok") and p["games"][0]["sha"] == S and not os.path.exists(os.path.join(work, "B", "saves", S)), p)
        r = B.call("/api/studio/saves-import", {"id": p["id"], "token": p["token"], "choices": {}})
        bd = os.path.join(work, "B", "saves", S)
        ok("saves: imported byte for byte", r.get("ok") and open(os.path.join(bd, "slot1.ccsstate"), "rb").read() == s1 and open(os.path.join(bd, "cart.sav"), "rb").read() == cart, r)
        r = B.call("/api/studio/saves-import", {"id": p["id"], "token": "0" * 64, "choices": {}})
        ok("saves: a file changed since it was looked at is refused", r.get("ok") is False and "changed since" in r.get("error", ""), r)
        try:
            q = urllib.request.Request(B.base + "/api/studio/saves-export", data=b"{}", headers={"Content-Type": "application/json", "Origin": B.base}); urllib.request.urlopen(q, timeout=20); refused = False
        except urllib.error.HTTPError as e: refused = e.code in (401, 403)
        ok("saves: without the Windows app's own principal the routes are refused", refused)
        # included: only the games asked for
        r = B.call("/api/personal/included", {"keys": ["thwaite"]}); E = B.entries()
        ok("included: keys keeps only the game asked for", r.get("ok") and ("p_" + S[:16]) in E and not any(e.get("origin") == "included" and e["id"] != "p_" + S[:16] for e in E.values()), list(E))
        # headerless: the known Included build gets its recorded header; unknown headerless bytes are refused with the reason
        full = open(os.path.join(site, "included", th["file_name"]), "rb").read()
        unh = os.path.join(work, "thwaite.unh"); open(unh, "wb").write(full[16:])
        r = A.call("/api/personal/ingest-path", {"path": unh, "origin": "file"}); e = (r.get("entry") or {})
        ok("headerless: the known build is kept with its recorded header", r.get("ok") and ((e.get("detected") or {}).get("header_hex") or "").lower() == th["header_hex"], r)
        junk = os.path.join(work, "mystery.unh"); open(junk, "wb").write(hashlib.sha256(b"q").digest() * 768)
        try: r = A.call("/api/personal/ingest-path", {"path": junk, "origin": "file"}); err = r.get("error", "") if not r.get("ok") else ""
        except urllib.error.HTTPError as x: err = json.loads(x.read() or b"{}").get("error", "")
        ok("headerless: bytes that are no known game are refused with the reason", "no NES header" in err and "cannot be determined" in err, err)
    finally:
        for b in (A, B):
            if b: b.stop()
        shutil.rmtree(work, ignore_errors=True)
    n = sum(1 for _, c in res if c); print(f"RESULT: {'PASS' if n == len(res) else 'FAIL'} ({n}/{len(res)} checks)", flush=True)
    if a.out: json.dump([{"check": k, "pass": v} for k, v in res], open(a.out, "w", encoding="utf-8"), indent=1)
    return 0 if n == len(res) else 1

if __name__ == "__main__": sys.exit(main())
