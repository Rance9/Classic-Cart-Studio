"""Companion security and import checks that run anywhere (CI, a fresh clone): Python only, no browser, no Hakchi, no game files.
Starts nes_companion.py on a test port with an isolated data folder and the synthetic fixtures from tools/publish/fixtures, then
checks CORS/preflight/Private Network Access, pairing (token + one-time code), bearer binding, rebinding/host rules, protocol
versioning, the bulk-import job (ZIP members, duplicates, bomb and junk rejection, cancel) and that no token reaches the log.
Exit 0 = PASS."""
from __future__ import annotations
import os, sys, json, subprocess, tempfile, time, re, urllib.request, urllib.error, urllib.parse, shutil
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.normpath(os.path.join(HERE, "..", "..")); sys.path.insert(0, HERE)
from version import PUBLIC_ORIGIN
PORT = int(os.environ.get("NES_TEST_PORT", "8793")); B = f"http://127.0.0.1:{PORT}"

def req(method, path, body=None, headers=None):
    data = json.dumps(body).encode() if body is not None else None
    r = urllib.request.Request(B + path, data=data, headers={**({"Content-Type": "application/json"} if data else {}), **(headers or {})}, method=method)
    try:
        with urllib.request.urlopen(r, timeout=20) as resp: return resp.status, dict(resp.headers), (json.loads(resp.read() or b"{}") if "json" in resp.headers.get("Content-Type", "") else {})
    except urllib.error.HTTPError as e:
        try: return e.code, dict(e.headers), json.loads(e.read() or b"{}")
        except Exception: return e.code, dict(e.headers), {}

def main():
    work = os.path.join(tempfile.gettempdir(), "nes-hosted-public"); shutil.rmtree(work, ignore_errors=True); os.makedirs(work)
    fx = os.path.join(work, "fixtures"); subprocess.run([sys.executable, os.path.join(REPO, "tools", "publish", "fixtures", "make_fixtures.py"), fx], check=True)
    site = os.environ.get("NES_STUDIO_SITE") or os.path.join(REPO, "build", "site")
    cfg = {"hakchi_exe": os.path.join(work, "no-hakchi", "hakchi.exe"), "hakchi_args": ["/portable"], "exports_dir": os.path.join(work, "exports"), "staging_dir": os.path.join(work, "staging"), "port": PORT, "rehearsal_tree": True, "no_launch": True, "allowed_origins": ["http://localhost:4173"]}
    cfgp = os.path.join(work, "companion.json"); json.dump(cfg, open(cfgp, "w"))
    env = dict(os.environ, NES_COMPANION_CONFIG=cfgp, NES_PERSONAL_DIR=os.path.join(work, "personal"), NES_STUDIO_DATA=work, NES_STUDIO_SITE=site, PYTHONIOENCODING="utf-8")
    logf = open(os.path.join(work, "companion.log"), "w", encoding="utf-8")
    proc = subprocess.Popen([sys.executable, os.path.join(HERE, "nes_companion.py"), "--no-browser", "--port", str(PORT)], env=env, stdout=logf, stderr=subprocess.STDOUT)
    checks = []; ok = lambda k, v, n="": checks.append(f"{k}={'true' if v else 'false'}{(' ('+n+')') if n and not v else ''}")
    own = {"Origin": B}; PUBLIC = PUBLIC_ORIGIN
    try:
        up = False
        for _ in range(80):
            try: urllib.request.urlopen(B + "/api/ping", timeout=1); up = True; break
            except Exception: time.sleep(0.25)
        ok("companion_starts_without_hakchi_or_game_files", up)
        s, h, j = req("OPTIONS", "/api/status", headers={"Origin": PUBLIC, "Access-Control-Request-Method": "GET", "Access-Control-Request-Headers": "authorization,x-nes-protocol", "Access-Control-Request-Private-Network": "true"})
        ok("preflight_public_origin", s == 204 and h.get("Access-Control-Allow-Origin") == PUBLIC and "Authorization" in h.get("Access-Control-Allow-Headers", "") and h.get("Access-Control-Allow-Private-Network") == "true" and h.get("Vary") == "Origin", f"{s}")
        ok("preflight_untrusted_refused", req("OPTIONS", "/api/status", headers={"Origin": "https://evil.example", "Access-Control-Request-Method": "GET"})[0] == 403)
        ok("preflight_custom_domain_allowed", req("OPTIONS", "/api/status", headers={"Origin": "https://classiccartstudio.com", "Access-Control-Request-Method": "GET"})[0] == 204 and req("OPTIONS", "/api/status", headers={"Origin": "https://www.classiccartstudio.com", "Access-Control-Request-Method": "GET"})[0] == 204 and req("OPTIONS", "/api/status", headers={"Origin": "http://classiccartstudio.com", "Access-Control-Request-Method": "GET"})[0] == 403)
        ok("preflight_configured_dev_origin", req("OPTIONS", "/api/status", headers={"Origin": "http://localhost:4173", "Access-Control-Request-Method": "GET"})[0] == 204)
        s, h, j = req("GET", "/api/version", headers={"Origin": PUBLIC}); ok("version_public_cors_no_secrets", s == 200 and j.get("protocol") >= 1 and j.get("paired") is False and h.get("Access-Control-Allow-Origin") == PUBLIC and "hakchi" not in json.dumps(j).lower())
        ok("version_no_cors_for_untrusted", "Access-Control-Allow-Origin" not in req("GET", "/api/version", headers={"Origin": "https://evil.example"})[1])
        ok("status_cross_origin_needs_pairing", req("GET", "/api/status", headers={"Origin": PUBLIC})[0] == 401)
        ok("status_cross_site_without_origin_refused", req("GET", "/api/status", headers={"Sec-Fetch-Site": "cross-site"})[0] == 401)
        s, h, j = req("GET", "/api/status"); ok("status_same_origin_ok_and_reports_missing_hakchi", s == 200 and j.get("hakchi_found") is False and j.get("install", {}).get("state") in ("missing", "different-version"), f"{s} {str(j)[:120]}")
        ok("newer_protocol_426", req("GET", "/api/status", headers={"Origin": PUBLIC, "X-NES-Protocol": "99"})[0] == 426)
        ok("rebinding_host_refused", req("GET", "/api/status", headers={"Host": f"evil.example:{PORT}"})[0] == 403)
        ok("approve_from_website_refused", req("POST", "/api/pair/approve", {"origin": PUBLIC}, headers={"Origin": PUBLIC})[0] == 401)
        pair = urllib.request.urlopen(B + "/pair?origin=" + urllib.parse.quote(PUBLIC)).read().decode("utf-8"); tok = re.search(r'nes-companion-token" content="([^"]+)"', pair).group(1)
        hp = dict(urllib.request.urlopen(B + "/pair").headers); ok("pair_page_not_framable", hp.get("X-Frame-Options") == "DENY")
        ok("approve_untrusted_origin_refused", req("POST", "/api/pair/approve", {"origin": "https://evil.example"}, headers={**own, "X-NES-Token": tok})[0] == 403)
        ok("approve_wrong_token_refused", req("POST", "/api/pair/approve", {"origin": PUBLIC}, headers={**own, "X-NES-Token": "nope"})[0] == 403)
        s, h, t = req("POST", "/api/pair/approve", {"origin": PUBLIC}, headers={**own, "X-NES-Token": tok}); ok("approve_issues_token_and_code", s == 200 and len(t.get("token", "")) > 30 and re.match(r"^\d{6}$", t.get("code", "")))
        bearer = {"Origin": PUBLIC, "Authorization": "Bearer " + t["token"]}
        ok("bearer_status_ok", req("GET", "/api/status", headers=bearer)[0] == 200)
        ok("bearer_bound_to_origin", req("GET", "/api/status", headers={"Origin": "http://localhost:4173", "Authorization": "Bearer " + t["token"]})[0] == 401)
        ok("claim_wrong_origin_refused", req("POST", "/api/pair/claim", {"code": t["code"]}, headers={"Origin": "http://localhost:4173"})[0] == 403)
        s, h, c = req("POST", "/api/pair/claim", {"code": t["code"]}, headers={"Origin": PUBLIC}); ok("claim_returns_token_once", s == 200 and c.get("token") == t["token"] and req("POST", "/api/pair/claim", {"code": t["code"]}, headers={"Origin": PUBLIC})[0] == 403)
        s, h, r2 = req("POST", "/api/pair/refresh", {}, headers=bearer); ok("refresh_rotates", s == 200 and r2.get("token") and r2["token"] != t["token"] and req("GET", "/api/status", headers=bearer)[0] == 401)
        bearer = {"Origin": PUBLIC, "Authorization": "Bearer " + r2["token"]}
        # ---- import job with synthetic fixtures: nothing matches the library, so every real file becomes a personal game ----
        s, h, j = req("POST", "/api/import/start", {"paths": [fx]}, headers=bearer); job = j.get("job"); ok("import_starts", s == 200 and bool(job), f"{s} {j}")
        st = {}
        for _ in range(300):
            s, h, st = req("GET", f"/api/import/status?job={job}", headers=bearer); st = st.get("job") or {}
            if st.get("finished"): break
            time.sleep(0.2)
        res = {(i["file"], i["member"]): i["result"] for i in st.get("items", [])}
        ok("import_counts", st.get("finished") and st.get("known") == 0 and st.get("personal") == 4 and st.get("duplicates") == 1 and len(st.get("errors", [])) == 2, json.dumps({k: st.get(k) for k in ("known", "personal", "duplicates", "errors")}))
        ok("zip_members_and_duplicate", res.get(("pack.zip", "delta.nes"), "").startswith("personal") and res.get(("pack.zip", "dup-of-alpha.nes"), "").startswith("duplicate"), json.dumps(list(res.items()))[:300])
        ok("bomb_and_junk_rejected", any(e["file"] == "bomb.zip" and "compression" in e["error"] for e in st["errors"]) and any(e["file"] == "junk.nes" for e in st["errors"]), json.dumps(st["errors"]))
        s, h, p = req("GET", "/api/personal", headers=bearer); names = sorted(e["file_name"] for e in p.get("entries", [])); ok("personal_entries_created", names == ["alpha.nes", "beta.unh", "delta.nes", "gamma.nes"], str(names))
        fxj = json.load(open(os.path.join(fx, "fixtures.json"))); alpha = next(e for e in p["entries"] if e["file_name"] == "alpha.nes"); fa = next(f for f in fxj if f["file"] == "alpha.nes")
        ok("content_identity_exact", alpha["content_sha256"] == fa["content_sha256"] and alpha["file_present"] is True)
        ok("fixture_files_unchanged", open(os.path.join(fx, "alpha.nes"), "rb").read().__len__() == fa["bytes"])
        s, h, j = req("POST", "/api/import/start", {"paths": [fx]}, headers=bearer); job2 = j.get("job")
        for _ in range(300):
            s, h, st2 = req("GET", f"/api/import/status?job={job2}", headers=bearer); st2 = st2.get("job") or {}
            if st2.get("finished"): break
            time.sleep(0.2)
        s, h, p2 = req("GET", "/api/personal", headers=bearer); ok("reimport_does_not_duplicate_personal_entries", len(p2.get("entries", [])) == 4, str(len(p2.get("entries", []))))
        big = os.path.join(work, "big"); os.makedirs(big)
        import random
        for i in range(80): open(os.path.join(big, f"h{i}.nes"), "wb").write(b"NES\x1a" + bytes([2, 0, 1, 0]) + b"\0" * 8 + bytes(random.Random(i).getrandbits(8) for _ in range(32768)))
        s, h, j = req("POST", "/api/import/start", {"paths": [big]}, headers=bearer); job3 = j.get("job"); req("POST", "/api/import/cancel", {"job": job3}, headers=bearer)
        for _ in range(200):
            s, h, st3 = req("GET", f"/api/import/status?job={job3}", headers=bearer); st3 = st3.get("job") or {}
            if st3.get("finished"): break
            time.sleep(0.1)
        ok("cancel_stops_early", st3.get("canceled") is True and st3.get("done", 80) < 80, str(st3.get("done")))
        ok("locate_unknown_version_refused", req("POST", "/api/library/locate", {"version_id": "zz", "path": os.path.join(fx, "alpha.nes")}, headers=bearer)[0] == 409)
        ok("revoke", req("POST", "/api/pair/revoke", {}, headers=bearer)[0] == 200 and req("GET", "/api/status", headers=bearer)[0] == 401)
        logf.flush(); log = open(os.path.join(work, "companion.log"), encoding="utf-8", errors="replace").read(); ok("no_token_in_log", t["token"] not in log and r2["token"] not in log)
        ok("exports_not_served", req("GET", "/exports/x.json")[0] == 403 and req("GET", "/personal/index.json")[0] == 403)
    finally:
        proc.terminate()
        try: proc.wait(timeout=10)
        except Exception: proc.kill()
    bad = [c for c in checks if "=false" in c]; print("\n".join(checks)); print(f"RESULT: {'PASS' if not bad else 'FAIL '+str(bad)} ({len(checks)} checks)"); sys.exit(1 if bad else 0)

if __name__ == "__main__":
    main()
