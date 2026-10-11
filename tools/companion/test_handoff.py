"""Desktop channel and website-to-desktop handoff checks (Python only, no browser): the backend started headless with a host token
(as Classic Cart Studio for Windows starts it) must treat that token as the desktop principal for the website origin only, expose
capabilities, accept bounded/validated collection offers from the exact website origin without a bearer, keep them non-destructively
and idempotently in an inbox that only the desktop principal can read/take/discard, store and return durable desktop state, refuse
unauthorized origins, oversize bodies, bad schemas and malformed ids, and stop on /api/shutdown. Isolated data folder, synthetic
collection, nothing touches Hakchi or the console. Exit 0 = PASS."""
from __future__ import annotations
import os, sys, json, subprocess, tempfile, time, secrets, shutil, urllib.request, urllib.error, uuid
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from version import PUBLIC_ORIGIN, SITE_ORIGINS
PORT = int(os.environ.get("NES_TEST_PORT", "8798")); B = f"http://127.0.0.1:{PORT}"; SITE = SITE_ORIGINS[0]

def req(method, path, body=None, headers=None, raw=None):
    data = raw if raw is not None else (json.dumps(body).encode() if body is not None else None)
    r = urllib.request.Request(B + path, data=data, headers={**({"Content-Type": "application/json"} if data is not None else {}), **(headers or {})}, method=method)
    try:
        with urllib.request.urlopen(r, timeout=20) as resp: return resp.status, dict(resp.headers), (json.loads(resp.read() or b"{}") if "json" in resp.headers.get("Content-Type", "") else {})
    except urllib.error.HTTPError as e:
        try: return e.code, dict(e.headers), json.loads(e.read() or b"{}")
        except Exception: return e.code, dict(e.headers), {}
    except Exception as e: return 0, {}, {"error": str(e)}

def collection(n=20, name="My NES Favorites"):
    games = [{"game_id": f"g{i:04d}", "version_id": f"v{i:04d}", "source": "catalog"} for i in range(n)]
    games[1]["version_id"] = "v0001-rev1"; games[2]["version_id"] = "v0002-jp"
    return {"schema": "nes-classic-collection/4", "name": name, "games": games, "factory_visible": ["CLV-P-NAAAE"], "decisions": {"g0001": "alternate"}}

def main():
    work = os.path.join(tempfile.gettempdir(), "nes-handoff-test"); shutil.rmtree(work, ignore_errors=True); os.makedirs(work)
    cfg = {"hakchi_exe": os.path.join(work, "no-hakchi", "hakchi.exe"), "hakchi_args": ["/portable"], "exports_dir": os.path.join(work, "exports"), "staging_dir": os.path.join(work, "staging"), "port": PORT, "no_launch": True}
    cfgp = os.path.join(work, "companion.json"); json.dump(cfg, open(cfgp, "w"))
    token = secrets.token_hex(32)
    env = dict(os.environ, NES_COMPANION_CONFIG=cfgp, NES_PERSONAL_DIR=os.path.join(work, "personal"), NES_STUDIO_DATA=work, CCS_HOST_TOKEN=token, CCS_HOST_PID=str(os.getpid()), PYTHONIOENCODING="utf-8")
    logf = open(os.path.join(work, "companion.log"), "w", encoding="utf-8")
    proc = subprocess.Popen([sys.executable, "-u", os.path.join(HERE, "companion_app.py"), "--headless", "--port", str(PORT)], env=env, stdout=logf, stderr=subprocess.STDOUT)
    checks = []; ok = lambda k, v, n="": checks.append(f"{k}={'true' if v else 'false'}{(' ('+str(n)[:220]+')') if n and not v else ''}")
    desk = {"Origin": SITE, "Authorization": "Bearer " + token}; web = {"Origin": SITE}
    try:
        up = False; t0 = time.time()
        for _ in range(120):
            try: urllib.request.urlopen(B + "/api/ping", timeout=1); up = True; break
            except Exception: time.sleep(0.25)
        ok("headless_backend_starts", up, f"{time.time()-t0:.1f}s"); print(f"boot {time.time()-t0:.1f}s")
        s, h, j = req("GET", "/api/version", headers=desk); ok("desktop_principal_recognized", s == 200 and j.get("desktop") is True and j.get("paired") is True and isinstance(j.get("capabilities"), list) and "handoff" in j["capabilities"], str(j)[:200])
        s, h, j = req("GET", "/api/version", headers=web); ok("website_without_token_not_desktop", s == 200 and j.get("desktop") is False and j.get("paired") is False and j.get("handoff_pending") is None)
        ok("desktop_token_bound_to_site_origin", req("GET", "/api/status", headers={"Origin": "https://evil.example", "Authorization": "Bearer " + token})[0] in (401, 403))
        ok("desktop_token_not_valid_for_wrong_value", req("GET", "/api/status", headers={"Origin": SITE, "Authorization": "Bearer " + "0" * len(token)})[0] == 401)
        ok("desktop_status_ok", req("GET", "/api/status", headers=desk)[0] == 200)
        # ---- offers from the website (no bearer) ----
        oid = str(uuid.uuid4()); coll = collection()
        s, h, j = req("POST", "/api/handoff/offer", {"id": oid, "collection": coll}, headers=web); ok("offer_accepted_from_site_origin", s == 200 and j.get("id") == oid and j.get("duplicate") is False and h.get("Access-Control-Allow-Origin") == SITE, f"{s} {j}")
        s, h, j2 = req("POST", "/api/handoff/offer", {"id": oid, "collection": coll}, headers=web); ok("offer_idempotent_same_id", s == 200 and j2.get("duplicate") is True and j2.get("pending") == 1)
        s, h, j3 = req("POST", "/api/handoff/offer", {"id": str(uuid.uuid4()), "collection": coll}, headers=web); ok("offer_same_content_other_id_deduplicated", s == 200 and j3.get("duplicate") is True and j3.get("id") == oid)
        ok("offer_refused_from_untrusted_origin", req("POST", "/api/handoff/offer", {"id": str(uuid.uuid4()), "collection": coll}, headers={"Origin": "https://evil.example"})[0] == 403)
        ok("offer_refused_from_plain_http_site", req("POST", "/api/handoff/offer", {"id": str(uuid.uuid4()), "collection": coll}, headers={"Origin": "http://classiccartstudio.com"})[0] == 403)
        ok("offer_refused_without_origin", req("POST", "/api/handoff/offer", {"id": str(uuid.uuid4()), "collection": coll})[0] == 403)
        ok("offer_bad_id_refused", req("POST", "/api/handoff/offer", {"id": "../etc", "collection": coll}, headers=web)[0] == 400)
        ok("offer_bad_schema_refused", req("POST", "/api/handoff/offer", {"id": str(uuid.uuid4()), "collection": {"schema": "something-else/1", "games": []}}, headers=web)[0] == 400)
        ok("offer_games_must_have_ids", req("POST", "/api/handoff/offer", {"id": str(uuid.uuid4()), "collection": {"schema": "nes-classic-collection/4", "games": [{"x": 1}]}}, headers=web)[0] == 400)
        big = json.dumps({"id": str(uuid.uuid4()), "collection": {"schema": "nes-classic-collection/4", "games": [{"game_id": "g" * 100}] * 12000}}).encode()
        ok("offer_oversize_refused", req("POST", "/api/handoff/offer", raw=big, headers=web)[0] == 413, len(big))
        ok("offer_invalid_json_refused", req("POST", "/api/handoff/offer", raw=b"{not json", headers=web)[0] == 400)
        s4 = {"schema": "nes-classic-collection/4", "name": "Schema four", "selections": [{"version_id": "v1", "game_id": "v1", "title": "A"}, {"version_id": "v2-rev1", "game_id": "v2-rev1", "revision_decision": "alternate"}]}
        s, h, j4 = req("POST", "/api/handoff/offer", {"id": str(uuid.uuid4()), "collection": s4}, headers=web); ok("offer_accepts_schema4_selections", s == 200 and j4.get("id"), f"{s} {j4}")
        req("POST", "/api/handoff/discard", {"id": j4.get("id")}, headers=desk)
        # ---- inbox: only the desktop principal ----
        ok("pending_refused_for_website", req("GET", "/api/handoff/pending", headers=web)[0] in (401, 403))
        s, h, j = req("GET", "/api/handoff/pending", headers=desk); ok("pending_lists_offer", s == 200 and len(j.get("pending", [])) == 1 and j["pending"][0]["id"] == oid and j["pending"][0]["count"] == 20 and j["pending"][0]["name"] == "My NES Favorites", str(j)[:200])
        ok("take_refused_for_website", req("POST", "/api/handoff/take", {"id": oid}, headers=web)[0] in (401, 403))
        s, h, j = req("POST", "/api/handoff/take", {"id": oid}, headers=desk); got = (j.get("offer") or {}).get("collection") or {}
        ok("take_returns_exact_collection", s == 200 and got == coll and got["games"][1]["version_id"] == "v0001-rev1" and got["games"][2]["version_id"] == "v0002-jp", str(j)[:200])
        s, h, j = req("POST", "/api/handoff/take", {"id": oid}, headers=desk); ok("take_is_repeatable", s == 200 and (j.get("offer") or {}).get("taken_at"))
        ok("take_unknown_404", req("POST", "/api/handoff/take", {"id": "nope-nope-nope"}, headers=desk)[0] == 404)
        ok("inbox_persisted_to_disk", os.path.exists(os.path.join(work, "handoff-inbox.json")) and oid in open(os.path.join(work, "handoff-inbox.json"), encoding="utf-8").read())
        ok("discard_removes", req("POST", "/api/handoff/discard", {"id": oid}, headers=desk)[0] == 200 and req("GET", "/api/handoff/pending", headers=desk)[2].get("pending") == [])
        # cap: at most 8 pending, oldest evicted, never an error for the website
        for i in range(10): req("POST", "/api/handoff/offer", {"id": str(uuid.uuid4()), "collection": collection(3 + i, f"c{i}")}, headers=web)
        s, h, j = req("GET", "/api/handoff/pending", headers=desk); ok("inbox_capped_at_8", s == 200 and len(j.get("pending", [])) == 8, len(j.get("pending", [])))
        # ---- durable desktop state ----
        ok("state_refused_for_website", req("GET", "/api/desktop/state", headers=web)[0] in (401, 403) and req("POST", "/api/desktop/state", {"state": {"a": 1}}, headers=web)[0] in (401, 403))
        s, h, j = req("GET", "/api/desktop/state", headers=desk); ok("state_empty_at_first", s == 200 and j.get("state") == {})
        st = {"collection": {"name": "My NES Favorites", "schema": "nes-classic-collection/4", "games": coll["games"]}, "favorites": ["g0001"], "snapshots": [], "view": "list"}
        s, h, j = req("POST", "/api/desktop/state", {"state": st}, headers=desk); ok("state_saved", s == 200 and j.get("saved_at"))
        s, h, j = req("GET", "/api/desktop/state", headers=desk); ok("state_round_trip", s == 200 and j["state"].get("collection") == st["collection"] and j["state"].get("favorites") == ["g0001"] and j["state"].get("saved_at"))
        req("POST", "/api/desktop/state", {"state": dict(st, view="grid")}, headers=desk)
        ok("state_backup_kept", os.path.exists(os.path.join(work, "desktop-state.json.bak")) and json.load(open(os.path.join(work, "desktop-state.json.bak"), encoding="utf-8")).get("view") == "list")
        ok("state_rejects_non_object", req("POST", "/api/desktop/state", {"state": [1, 2]}, headers=desk)[0] == 400)
        # ---- shutdown ----
        ok("shutdown_refused_for_website", req("POST", "/api/shutdown", {}, headers=web)[0] in (401, 403))
        s, h, j = req("POST", "/api/shutdown", {}, headers=desk); ok("shutdown_accepted", s == 200 and j.get("stopping") is True)
        try: proc.wait(timeout=10); ok("process_exits_after_shutdown", proc.returncode == 0, proc.returncode)
        except subprocess.TimeoutExpired: ok("process_exits_after_shutdown", False, "still running")
        log = open(os.path.join(work, "companion.log"), encoding="utf-8").read(); ok("host_token_never_logged", token not in log)
    finally:
        if proc.poll() is None: proc.kill()
    for c in checks: print(c)
    fails = [c for c in checks if "=false" in c]; print(("RESULT: PASS" if not fails else "RESULT: FAIL " + str(fails)) + f" ({len(checks)} checks)"); return 1 if fails else 0

if __name__ == "__main__": sys.exit(main())
