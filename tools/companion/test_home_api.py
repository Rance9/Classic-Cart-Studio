r"""HOME routes on the backend (desktop principal only): /api/home/state GET/POST, /api/home/console GET, /api/home/sync POST
against the MOCK console store. Starts a headless backend on 8772 with a host token; website origin without the token is
refused; invalid manifests are refused without writing.   python test_home_api.py"""
from __future__ import annotations
import os, sys, json, time, subprocess, tempfile, shutil, urllib.request, secrets
HERE = os.path.dirname(os.path.abspath(__file__)); PORT = 8772; B = f"http://127.0.0.1:{PORT}"; SITE = "https://classiccartstudio.com"

def req(method, path, body=None, headers=None):
    data = json.dumps(body).encode() if body is not None else None
    r = urllib.request.Request(B + path, data=data, method=method, headers={"Content-Type": "application/json", **(headers or {})})
    try:
        with urllib.request.urlopen(r, timeout=20) as resp: return resp.status, json.loads(resp.read().decode() or "{}")
    except urllib.error.HTTPError as e:
        try: return e.code, json.loads(e.read().decode() or "{}")
        except Exception: return e.code, {}

def main():
    checks = []; ok = lambda k, v, n="": checks.append(f"{k}={'true' if v else 'false'}{(' ('+str(n)[:200]+')') if n and not v else ''}")
    work = tempfile.mkdtemp(prefix="ccs-home-api-"); token = secrets.token_urlsafe(24); cfgp = os.path.join(work, "companion.json"); json.dump({"hakchi_root": os.path.join(work, "hakchi"), "rehearsal_tree": work}, open(cfgp, "w"))
    env = dict(os.environ, NES_COMPANION_CONFIG=cfgp, NES_PERSONAL_DIR=os.path.join(work, "personal"), NES_STUDIO_DATA=work, CCS_HOST_TOKEN=token, CCS_HOST_PID=str(os.getpid()), PYTHONIOENCODING="utf-8")
    logf = open(os.path.join(work, "server.log"), "w"); proc = subprocess.Popen([sys.executable, "-u", os.path.join(HERE, "companion_app.py"), "--headless", "--port", str(PORT)], env=env, stdout=logf, stderr=subprocess.STDOUT)
    desk = {"Origin": SITE, "Authorization": "Bearer " + token}; web = {"Origin": SITE}
    try:
        for _ in range(100):
            try:
                if req("GET", "/api/ping")[0] == 200: break
            except Exception: pass
            time.sleep(0.2)
        ok("website_cannot_read_home_state", req("GET", "/api/home/state", headers=web)[0] in (401, 403))
        ok("website_cannot_sync", req("POST", "/api/home/sync", {"manifest": {}, "state": {}}, headers=web)[0] in (401, 403))
        s, r = req("GET", "/api/home/state", headers=desk); ok("desktop_reads_empty_state", s == 200 and r["ok"] and r["home"] == {}, (s, r))
        home = {"v": 1, "folders": [{"id": "f1", "name": "One", "parent": None, "order": ["g1"], "children": []}], "favorites": {"gen": 1, "items": {"g1": {"on": True, "rev": 1, "at": "2026-10-11T10:00:00Z"}}}, "history": {"events": [], "cleared_gen": 0, "cleared_at": None}, "theme": "featured-shelf", "sorts": {}, "map_shortcuts": ["f1", None], "settings": {}}
        s, r = req("POST", "/api/home/state", {"home": home}, headers=desk); ok("desktop_saves_state", s == 200 and r["ok"] and r.get("saved_at"), (s, r))
        s, r = req("GET", "/api/home/state", headers=desk); h = r.get("home") or {}; ok("state_round_trips", s == 200 and h.get("theme") == "featured-shelf" and (h.get("folders") or [{}])[0].get("name") == "One", (s, str(r)[:300]))
        ok("bad_state_refused", req("POST", "/api/home/state", {"home": "nope"}, headers=desk)[0] == 400)
        man = {"schema": "ccs-home-sync-manifest/1", "games": [{"id": "g1", "version_id": "v1", "title": "One"}, {"id": "g2", "version_id": "v2", "title": "Two"}], "order": ["g1", "g2"], "folders": [{"id": "f1", "name": "One", "parent": None, "order": ["g1"]}], "map_shortcuts": ["f1", None], "settings": {}, "theme": "featured-shelf"}
        st = {"schema": "ccs-home-sync-state/1", "favorites": home["favorites"], "history": {"events": [{"id": "e1", "game": "g2", "at": "2026-10-11T11:00:00Z"}], "cleared_gen": 0, "cleared_at": None}}
        s, r = req("POST", "/api/home/sync", {"manifest": man, "state": st}, headers=desk); ok("sync_against_mock_console", s == 200 and r["ok"] and r["simulation"] and r["state"]["favorites"]["items"]["g1"]["on"] and len(r["state"]["history"]["events"]) == 1 and "no device" in r["note"], (s, r))
        s, c = req("GET", "/api/home/console", headers=desk); ok("mock_console_readable_and_labeled", s == 200 and c["simulation"] and c["console"]["simulation"] and c["console"]["manifest"]["folders"][0]["name"] == "One" and c["console"]["generation"] >= 1, (s, c))
        s, r2 = req("POST", "/api/home/sync", {"manifest": man, "state": r["state"]}, headers=desk); ok("second_sync_idempotent", s == 200 and r2["changed"] is False)
        bad = dict(man, order=["g1"]); s, r3 = req("POST", "/api/home/sync", {"manifest": bad, "state": st}, headers=desk); ok("invalid_manifest_refused", s == 422 and not r3["ok"] and r3["problems"])
        s, r4 = req("GET", "/api/home/state", headers=desk); ok("sync_result_adopted_into_windows_state", s == 200 and r4["home"]["history"]["events"][0]["id"] == "e1")
        # ---- custom folder artwork: validation, derivatives, replace, remove (desktop only)
        from PIL import Image
        good = os.path.join(work, "art-good.png"); Image.new("RGB", (640, 400), (200, 40, 40)).save(good)
        small = os.path.join(work, "art-small.png"); Image.new("RGB", (80, 80), (0, 0, 0)).save(small)
        notimg = os.path.join(work, "art-fake.png"); open(notimg, "wb").write(b"not an image at all")
        ok("website_cannot_set_folder_art", req("POST", "/api/home/folder-art", {"folder": "f1", "op": "set", "path": good}, headers=web)[0] in (401, 403))
        s, r = req("POST", "/api/home/folder-art", {"folder": "f1", "op": "set", "path": good}, headers=desk)
        ok("folder_art_accepted_with_derivatives", s == 200 and r["ok"] and r["art"]["provenance"] == "custom" and r["art"]["src"].startswith("personal/art/folders/f1-cover.jpg") and r["art"]["wide"].startswith("personal/art/folders/f1-wide.jpg"), (s, r))
        fdir = os.path.join(work, "personal", "art", "folders")
        with Image.open(os.path.join(fdir, "f1-cover.jpg")) as cov, Image.open(os.path.join(fdir, "f1-wide.jpg")) as wide: sizes = (cov.size, wide.size)   # closed again: an open handle would block the server's removal on Windows
        ok("derivative_sizes", sizes == ((480, 720), (810, 447)) and os.path.exists(os.path.join(fdir, "f1-src.png")), sizes)
        try:
            with urllib.request.urlopen(urllib.request.Request(B + "/personal/art/folders/f1-cover.jpg", headers=desk), timeout=20) as resp: ok("derivative_served", resp.status == 200 and resp.read()[:2] == bytes([0xFF, 0xD8]), resp.status)
        except Exception as e: ok("derivative_served", False, e)
        s, r = req("POST", "/api/home/folder-art", {"folder": "f1", "op": "set", "path": small}, headers=desk); ok("too_small_refused_keeps_existing", s == 400 and "smaller" in r.get("error", "") and os.path.exists(os.path.join(fdir, "f1-cover.jpg")), (s, r))
        s, r = req("POST", "/api/home/folder-art", {"folder": "f1", "op": "set", "path": notimg}, headers=desk); ok("non_image_refused", s == 400 and "readable image" in r.get("error", ""), (s, r))
        s, r = req("POST", "/api/home/folder-art", {"folder": "f1", "op": "set", "path": os.path.join(work, "missing.png")}, headers=desk); ok("missing_file_refused", s == 400, (s, r))
        s, r = req("POST", "/api/home/folder-art", {"folder": "f1", "op": "describe"}, headers=desk); ok("describe_reports_custom", s == 200 and r["art"]["provenance"] == "custom", (s, r))
        s, r = req("POST", "/api/home/folder-art", {"folder": "f1", "op": "clear"}, headers=desk); ok("clear_removes_files", s == 200 and r["art"]["provenance"] == "generated" and r["art"]["removed"] == 3 and not os.path.exists(os.path.join(fdir, "f1-cover.jpg")), (s, r))
        s, r = req("POST", "/api/home/folder-art", {"folder": "f1", "op": "describe"}, headers=desk); ok("describe_reports_generated_after_clear", s == 200 and r["art"]["provenance"] == "generated", (s, r))
        ok("no_device_endpoint_in_log", "169.254" not in open(os.path.join(work, "server.log"), encoding="utf-8", errors="replace").read())
    finally:
        try: req("POST", "/api/shutdown", {}, headers=desk)
        except Exception: pass
        try: proc.wait(timeout=10)
        except Exception: proc.kill()
        logf.close(); shutil.rmtree(work, ignore_errors=True)
    bad = [c for c in checks if "=false" in c]; print("\n".join(checks)); print(f"RESULT: {'PASS' if not bad else 'FAIL '+str(bad)} ({len(checks)} checks)"); return 1 if bad else 0

if __name__ == "__main__": sys.exit(main())
