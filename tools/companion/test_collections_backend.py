r"""Collections only, readiness and backups in the Windows app's backend, from the source backend on loopback with throwaway data
folders (never the person's). Every check reads what the backend stored or answered. Games are added through the import route (the one Add
ROM Files uses; its no-dialog form exists only in test configurations).
  import      an import job lists every game it verified (Add ROM Files adds them all)
  rom-check   ready, ROM missing (the copy and the original file gone) and unreadable (the only copy held locked by another program) are
              three different answers; an unreadable file is never reported as missing
  home        the HOME player refuses a collection with a game whose ROM file is not usable here and names the game; the console package
              stage does the same and removes its stage (play.start_home / console_package_stage with the package build replaced by its
              result, so no console runtime is needed)
  slots       a Full Studio Backup of a game with named save slots (slot-names.json) is written, and restores them into a second, fresh
              backend (exact v1p2 refused that file name when writing the backup)
  python test_collections_backend.py <built site folder> [--out results.json]      Exit 0 = PASS. Starts and stops only its own backends."""
from __future__ import annotations
import argparse, hashlib, importlib, json, msvcrt, os, shutil, sys, tempfile, time
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from test_included_backend import Backend

def rom(tag: str) -> bytes:
    return bytes([0x4E, 0x45, 0x53, 0x1A, 1, 1, 0, 0]) + bytes(8) + hashlib.sha256(("CCS-SENTINEL-ROM-BYTES " + tag).encode()).digest() * (24576 // 32)

def copies(root: str, sha: str) -> list[str]:
    out = []
    for d0, _, fs in os.walk(root):
        for f in fs:
            p = os.path.join(d0, f)
            if f.lower().endswith((".nes", ".unh")):
                try: b = open(p, "rb").read()
                except OSError: continue
                if hashlib.sha256(b[16:]).hexdigest() == sha: out.append(p)
    return out

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); a = ap.parse_args()
    site = os.path.abspath(a.site); work = tempfile.mkdtemp(prefix="ccs-coll-backend-"); cfg = os.path.join(work, "config.json")
    os.makedirs(os.path.join(work, "tree")); json.dump({"rehearsal_tree": os.path.join(work, "tree")}, open(cfg, "w"))
    res, out = [], {}
    def ok(n, c, d=""): res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:300]})" if d and not c else ""), flush=True)
    A = B = None; lock = None
    def importing(be, folder):
        j = be.call("/api/import/start", {"paths": [folder]})["job"]
        for _ in range(300):
            s = be.call("/api/import/status?job=" + j, get=True)
            if s.get("ok") and s["job"]["finished"]: return s["job"]
            time.sleep(0.1)
        return None
    try:
        src = os.path.join(work, "roms"); os.makedirs(src); body = {n: rom(n) for n in ("alpha", "beta", "gamma")}
        for n, b in body.items(): open(os.path.join(src, n + ".nes"), "wb").write(b)
        sha = {n: hashlib.sha256(b[16:]).hexdigest() for n, b in body.items()}; gid = {n: "p_" + s[:16] for n, s in sha.items()}
        os.makedirs(os.path.join(work, "A")); A = Backend(site, os.path.join(work, "A"), cfg)
        js = importing(A, src); out["import"] = {k: (js or {}).get(k) for k in ("known", "personal", "ids", "errors")}
        ok("import: the job lists every game it verified", js and sorted(js.get("ids") or []) == sorted(gid.values()), out["import"])
        games = [{"version_id": gid[n], "title": n} for n in ("alpha", "beta", "gamma")]
        r0 = A.call("/api/package/rom-check", {"games": games}); out["ready"] = r0
        ok("rom-check: all three are ready", sorted(g["version_id"] for g in r0["available"]) == sorted(gid.values()) and r0.get("unreadable") == [], r0)
        # missing: beta's original file and the app's copy are gone
        os.remove(os.path.join(src, "beta.nes")); gone = copies(os.path.join(work, "A"), sha["beta"])
        for p in gone: os.remove(p)
        # unreadable: gamma's original is gone and the app's copy is held locked by another program (locked before anything read it again)
        os.remove(os.path.join(src, "gamma.nes")); held = copies(os.path.join(work, "A"), sha["gamma"])
        A.stop(); A = Backend(site, os.path.join(work, "A"), cfg)   # a fresh process on the same data: no file identity remembered from before
        lock = []
        for hp in held: fd = os.open(hp, os.O_RDWR | os.O_BINARY); msvcrt.locking(fd, msvcrt.LK_NBLCK, os.path.getsize(hp)); lock.append((fd, os.path.getsize(hp)))
        r1 = A.call("/api/package/rom-check", {"games": games}); out["mixed"] = {"result": r1, "deleted": gone, "locked": held}
        ok("rom-check: a game whose files are gone is ROM missing", [g["version_id"] for g in r1["missing"]] == [gid["beta"]], r1)
        ok("rom-check: a copy that cannot be read is reported as unreadable, never missing", [g["version_id"] for g in r1.get("unreadable", [])] == [gid["gamma"]], r1)
        ok("rom-check: the untouched game is still ready", [g["version_id"] for g in r1["available"]] == [gid["alpha"]], r1)
        for fd, n in lock: msvcrt.locking(fd, msvcrt.LK_UNLCK, n); os.close(fd)
        lock = None
        # home and console: the whole collection or nothing
        os.environ.update({"CCS_STUDIO_SITE": site, "CCS_STUDIO_DATA": os.path.join(work, "A"), "CCS_COMPANION_CONFIG": cfg}); sys.path.insert(0, os.path.join(HERE, "..", "rom-pipeline"))
        import play as PL; importlib.reload(PL); import native_home.package as NP
        pk = os.path.join(work, "pack"); os.makedirs(pk); json.dump({"games": [{"id": gid[n], "title": n.title()} for n in ("alpha", "beta")]}, open(os.path.join(pk, "games.json"), "w"))
        real, realstart, started = NP.build, PL._start, []
        NP.build = lambda manifest, home, **k: {"ok": True, "package_dir": pk}; PL._start = lambda *x, **k: started.append(x) or {"ok": True}
        try: h = PL.start_home({"name": "Two"}, {})
        finally: NP.build, PL._start = real, realstart
        out["home"] = h
        ok("home: the HOME player refuses the collection and names the game without its file", h.get("ok") is False and h.get("code") == "incomplete" and "Beta" in h.get("error", "") and not started, h)
        import nes_companion as NC, native_home.console_package as CPK
        stages = []
        def fake(manifest, home, stage, **k): os.makedirs(stage, exist_ok=True); stages.append(stage); return {"ok": True, "manifest": {"unavailable": [{"id": gid["beta"], "title": "Beta", "code": "missing"}]}}
        realc = CPK.build_console; CPK.build_console = fake
        try: c = NC.console_package_stage({"name": "Two"}, {}, pk)
        finally: CPK.build_console = realc
        out["console"] = c
        ok("console: the console package is refused and its stage removed when a game has no usable file", c.get("ok") is False and c.get("code") == "incomplete" and "Beta" in c.get("error", "") and stages and not os.path.exists(stages[0]), c)
        # slots: named save slots in a Full Studio Backup, restored into a fresh backend
        sd = os.path.join(work, "A", "saves", sha["alpha"]); os.makedirs(sd, exist_ok=True)
        names = {"schema": "ccs-slot-names/1", "rom_content_sha256": sha["alpha"], "names": {"1": "Before the boss"}}
        open(os.path.join(sd, "slot-names.json"), "w", encoding="utf-8").write(json.dumps(names)); open(os.path.join(sd, "cart.sav"), "wb").write(b"\x01\x02\x03")
        bk = os.path.join(work, "backup.ccs")
        j = A.call("/api/studio/backup", {"studio": {"schema": "ccs-studio-backup/2", "collections": [{"name": "Alpha", "sel": [gid["alpha"]]}]}, "games": [games[0]], "path": bk})
        st = None
        for _ in range(600):
            st = A.call("/api/package/job", {"job": j.get("job")})["job"]
            if st["phase"] in ("done", "error", "canceled"): break
            time.sleep(0.2)
        out["backup"] = st
        ok("slots: a Full Studio Backup with named save slots is written", st and st["phase"] == "done" and os.path.isfile(bk), json.dumps(st)[:300])
        os.makedirs(os.path.join(work, "B")); B = Backend(site, os.path.join(work, "B"), cfg)
        o = B.call("/api/package/open", {"path": bk}); B.call("/api/studio/restore", {"id": o.get("id"), "check": True}); B.call("/api/package/import-roms", {"id": o.get("id")}); rs = B.call("/api/studio/restore", {"id": o.get("id")})
        got = os.path.join(work, "B", "saves", sha["alpha"], "slot-names.json"); out["restore"] = {"open": o.get("ok"), "restore": rs.get("ok"), "file": os.path.isfile(got), "response": rs, "entries": __import__("zipfile").ZipFile(bk).namelist(), "B_saves": [os.path.join(d0, f) for d0, _, fs in os.walk(os.path.join(work, "B")) for f in fs if "saves" in d0]}
        ok("slots: the slot names arrive in the fresh backend unchanged", o.get("ok") and rs.get("ok") and os.path.isfile(got) and json.load(open(got, encoding="utf-8")) == names, out["restore"])
    finally:
        for fd, n in (lock or []):
            try: msvcrt.locking(fd, msvcrt.LK_UNLCK, n); os.close(fd)
            except OSError: pass
        out["stopped"] = [x.stop() for x in (A, B) if x]
        if a.out: open(a.out, "w", encoding="utf-8").write(json.dumps({"checks": res, "observed": out}, indent=1, default=str))
        shutil.rmtree(work, ignore_errors=True)
    passed = sum(1 for _, c in res if c); print(f"RESULT: {'PASS' if passed == len(res) else 'FAIL'} ({passed}/{len(res)} checks)")
    return 0 if passed == len(res) else 1

if __name__ == "__main__": sys.exit(main())
