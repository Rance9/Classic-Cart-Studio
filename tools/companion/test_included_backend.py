r"""The Included Games in the Windows app's own store (Codex v1p F4), from the source backend on loopback with throwaway data folders
(never the person's): every check reads what the app stored and answers, then compares it with the published record.
  add        /api/personal/included keeps both games with their record's details: title, version, year, author (publisher), and the
             description text that names the license and where the source is (the same text the website uses, included/index.json)
  edits      the person renames one game and clears its description; adding the games again changes neither (a deliberate blank stays
             blank), and fills nothing the person wrote
  native     the native HOME's configuration resolver (native_home/config.resolve, the game records a console package and the TV Preview
             are built from) gives the stored details for both games
  backup     a Full Studio Backup of both, opened in a second, fresh backend (open, check, add the game files, restore): the same
             details and the same edits there; adding the games again there changes nothing
  python test_included_backend.py <built site folder> [--out results.json]      Exit 0 = PASS. Starts and stops only its own backends."""
from __future__ import annotations
import argparse, json, os, re, secrets, shutil, socket, subprocess, sys, tempfile, time, urllib.request
HERE = os.path.dirname(os.path.abspath(__file__))
FIELDS = {"title": "title", "version_label": "version", "description": "text", "year": "year", "publisher": "creator"}

class Backend:
    def __init__(self, site, data, cfg):
        with socket.socket() as s: s.bind(("127.0.0.1", 0)); self.port = s.getsockname()[1]
        self.base = f"http://127.0.0.1:{self.port}"; self.tok = secrets.token_urlsafe(24)
        env = {**os.environ, "NES_STUDIO_SITE": site, "NES_STUDIO_DATA": data, "NES_COMPANION_CONFIG": cfg, "CCS_HOST_TOKEN": self.tok}
        self.log = open(os.path.join(data, "..", os.path.basename(data) + "-backend.log"), "w", encoding="utf-8")
        self.p = subprocess.Popen([sys.executable, os.path.join(HERE, "nes_companion.py"), "--port", str(self.port), "--no-browser"], cwd=HERE, env=env, stdout=self.log, stderr=subprocess.STDOUT)
        for _ in range(150):
            try: self.call("/api/ping", get=True); return
            except Exception: time.sleep(0.2)
        raise RuntimeError("the backend did not start")
    def call(self, path, body=None, get=False, timeout=120):
        q = urllib.request.Request(self.base + path, data=None if get else json.dumps(body or {}).encode(), headers={"Content-Type": "application/json", "Origin": self.base, "Authorization": "Bearer " + self.tok})
        with urllib.request.urlopen(q, timeout=timeout) as r: return json.loads(r.read())
    def entries(self): return {e["id"]: e for e in self.call("/api/personal", get=True)["entries"]}
    def stop(self):
        self.p.terminate()
        try: self.p.wait(timeout=10)
        except subprocess.TimeoutExpired: self.p.kill(); self.p.wait(timeout=10)
        self.log.close(); return self.p.poll() is not None

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); a = ap.parse_args()
    site = os.path.abspath(a.site); idx = json.load(open(os.path.join(site, "included", "index.json"), encoding="utf-8"))
    rec = {"p_" + g["content_sha256"][:16]: g for g in idx["games"]}; ids = list(rec)
    want = lambda g: {f: str(g[k]) for f, k in FIELDS.items()}
    work = tempfile.mkdtemp(prefix="ccs-included-backend-"); cfg = os.path.join(work, "config.json")
    os.makedirs(os.path.join(work, "tree")); json.dump({"rehearsal_tree": os.path.join(work, "tree")}, open(cfg, "w"))   # path-based backup and open, no dialogs
    res, out = [], {}
    def ok(n, c, d=""): res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({d})" if d and not c else ""))
    def user(e): u = e.get("user") or {}; return {f: str(u.get(f) or "") for f in FIELDS}
    th = next(i for i, g in rec.items() if g["key"] == "thwaite"); other = [i for i in ids if i != th]
    A = B = None
    try:
        os.makedirs(os.path.join(work, "A")); A = Backend(site, os.path.join(work, "A"), cfg)
        r = A.call("/api/personal/included"); E = A.entries(); out["A_add"] = {"response": r, "stored": {i: E.get(i) for i in ids}}
        ok("add: both games stored as personal games from the included files", all(i in E and E[i].get("origin") == "included" and E[i].get("file_present") for i in ids))
        for i in ids:
            ok(f"add: {rec[i]['title']}: stored details are the record's (title, version, year, author, description with license and source)", user(E[i]) == want(rec[i]), json.dumps(user(E[i]))[:240])
        ok("add: the stored description names the license and the source folder", all("General Public License" in user(E[i])["description"] and f"included/source/{rec[i]['key']}/" in user(E[i])["description"] for i in ids))

        A.call("/api/personal/update", {"id": th, "user": {"title": "My Thwaite", "description": ""}})
        A.call("/api/personal/included"); E2 = A.entries(); out["A_readd"] = {i: E2.get(i) for i in ids}
        exp_th = dict(want(rec[th]), title="My Thwaite", description="")
        ok("edits: adding again keeps the person's title and their deliberately blank description", user(E2[th]) == exp_th, json.dumps(user(E2[th]))[:240])
        ok("edits: the person's fields are marked as theirs", set((E2[th].get("user") or {}).get("edited") or []) >= {"title", "description"})
        ok("edits: the other game is unchanged", all(user(E2[i]) == user(E[i]) for i in other))

        code = ("import json, sys; sys.path.insert(0, %r); from native_home import config as CF\n"
                "ids = json.loads(sys.argv[1]); m = {'schema': 'nes-classic-collection/4', 'selections': [{'game_id': i, 'source': 'personal'} for i in ids]}\n"
                "r = CF.resolve(m, {'theme': 'arcade-grid', 'order': ids, 'games': []})\n"
                "print(json.dumps([{k: g.get(k) for k in ('id', 'title', 'version_label', 'year', 'publisher', 'texts', 'source')} for g in r['games']]))") % HERE
        nr = subprocess.run([sys.executable, "-c", code, json.dumps(ids)], capture_output=True, text=True, cwd=HERE, timeout=120,
                            env={**os.environ, "NES_STUDIO_SITE": site, "NES_STUDIO_DATA": os.path.join(work, "A"), "NES_COMPANION_CONFIG": cfg})
        try: games = {g["id"]: g for g in json.loads(nr.stdout.strip().splitlines()[-1])}
        except Exception: games = {}; print(nr.stdout[-600:], nr.stderr[-1200:])
        out["native"] = games
        def nat(g): return {"title": g.get("title") or "", "version_label": g.get("version_label") or "", "description": ((g.get("texts") or {}).get("description")) or "", "year": g.get("year") or "", "publisher": g.get("publisher") or ""}
        ok("native: the HOME's game records carry the stored details of both games (the person's edits included)", games and all(nat(games[i]) == user(E2[i]) for i in ids), json.dumps(games)[:300])

        bk = os.path.join(work, "backup.ccs")
        j = A.call("/api/studio/backup", {"studio": {"schema": "ccs-studio-backup/2", "collections": [{"name": "Included Games", "sel": ids}]}, "games": [{"version_id": i, "title": user(E2[i])["title"]} for i in ids], "path": bk})
        st = None
        for _ in range(600):
            st = A.call("/api/package/job", {"job": j.get("job")})["job"]
            if st["phase"] in ("done", "error", "canceled"): break
            time.sleep(0.2)
        ok("backup: a Full Studio Backup was written", st and st["phase"] == "done" and os.path.isfile(bk), json.dumps(st)[:200])

        os.makedirs(os.path.join(work, "B")); B = Backend(site, os.path.join(work, "B"), cfg)
        o = B.call("/api/package/open", {"path": bk}); chk = B.call("/api/studio/restore", {"id": o.get("id"), "check": True})
        roms = B.call("/api/package/import-roms", {"id": o.get("id")}); rs = B.call("/api/studio/restore", {"id": o.get("id")})
        EB = B.entries(); out["B_restored"] = {"open": {k: o.get(k) for k in ("ok", "kind", "error")}, "check": chk.get("ok"), "roms": roms.get("ok"), "restore": rs.get("ok"), "stored": {i: EB.get(i) for i in ids}}
        ok("backup: the restore ran (check, game files, restore)", o.get("ok") and chk.get("ok") and roms.get("ok") and rs.get("ok"), json.dumps(out["B_restored"])[:300])
        ok("backup: both games are in the fresh app with the same details and edits", all(i in EB and user(EB[i]) == user(E2[i]) for i in ids), json.dumps({i: user(EB.get(i) or {}) for i in ids})[:300])
        ok("backup: the person's fields are still marked as theirs", set(((EB.get(th) or {}).get("user") or {}).get("edited") or []) >= {"title", "description"})
        B.call("/api/personal/included"); EB2 = B.entries()
        ok("backup: adding the games again there changes nothing", all(user(EB2[i]) == user(EB[i]) for i in ids))
    finally:
        out["stopped"] = [x.stop() for x in (A, B) if x]
        if a.out: open(a.out, "w", encoding="utf-8").write(json.dumps({"checks": res, "observed": out}, indent=1, default=str))
        shutil.rmtree(work, ignore_errors=True)
    passed = sum(1 for _, c in res if c); print(f"RESULT: {'PASS' if passed == len(res) else 'FAIL'} ({passed}/{len(res)} checks)")
    return 0 if passed == len(res) else 1

if __name__ == "__main__": sys.exit(main())
