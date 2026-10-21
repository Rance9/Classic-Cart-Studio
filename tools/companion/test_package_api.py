r"""The backend's .ccs package routes (package_api.py) for schema ccs-package/2. Python only, no browser; isolated data folders; synthetic
ROMs; the Save As dialog and File Explorer are replaced by recorders (no window opens); nothing touches Hakchi or the console.
  rom-check      available (exact identity) / missing (no file, or no identity) / mismatched (a file of a different version): never substituted
  export         complete packages only: anything missing or mismatched -> 409 with both lists and NO file; include_roms is ignored
                 (ROM files are always included); Studio backups are not packages; not enough free space -> 507
  save, reveal   Save As copies the staged package (streamed) and returns {ok, name, path}; cancel -> {ok:false, canceled:true};
                 reveal opens Explorer only for a path the save route produced
  stage, open    no size limit: 507 only when the drive lacks the space; schema-1 and incomplete packages refused plainly
  handoff        the website's staging route refuses incomplete packages
  import-roms    the staged package's ROM files into the (isolated) managed stores under their exact identities
  no network     every route above runs with socket calls patched to fail and record
  http           the headless backend: save/reveal are desktop-principal only; capabilities advertise ccs-package-2
  python test_package_api.py      Exit 0 = PASS."""
from __future__ import annotations
import io, json, os, secrets, shutil, subprocess, sys, tempfile, time, urllib.error, urllib.request, zipfile
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path[:0] = [HERE, os.path.join(HERE, "..", "rom-pipeline")]
TMP = tempfile.mkdtemp(prefix="ccs-api-")
os.environ["NES_PERSONAL_DIR"] = os.path.join(TMP, "personal"); os.environ["NES_STUDIO_DATA"] = os.path.join(TMP, "studio-data")
import ccs_package as cp, package_api as pk
from test_ccs_package import make_rom, collection, craft, NoNetwork

class FH:
    """a stand-in for the HTTP handler: headers, request body, recorded JSON response"""
    def __init__(self, body=None, raw=None, length=None):
        data = raw if raw is not None else json.dumps(body if body is not None else {}).encode()
        self.headers = {"Content-Length": str(len(data) if length is None else length)}; self.rfile = io.BytesIO(data); self.wfile = io.BytesIO(); self.code = None; self.obj = None
    def _json(self, code, obj): self.code, self.obj = code, obj
    def send_response(self, code): self.code = code
    def send_header(self, *a): pass
    def end_headers(self): pass

def main():
    checks = []
    def ok(k, v, n=""): checks.append((k, bool(v))); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:300]})" if n and not v else ""), flush=True)
    data_path = lambda *p: os.path.join(TMP, "data", *p)
    post = lambda path, body=None, raw=None, length=None, cfg=None: (lambda h: (pk.handle_post(h, path, data_path, "0.7.0-test", cfg or {}), h)[1])(FH(body, raw, length))
    staged = lambda: sorted(n for n in os.listdir(pk.stage_dir(data_path)) if n.endswith(".ccs"))
    try:
        import personal as ps
        put = lambda name, d: (lambda p: (open(p, "wb").write(d), p)[1])(os.path.join(TMP, name))
        r1, c1 = make_rom("api-1"); r2, c2 = make_rom("api-2"); r3, c3 = make_rom("api-3"); r5, c5 = make_rom("api-p5", header=False); r6, c6 = make_rom("api-p6")
        L1, L2, L3, P5, P6 = c1[:16], c2[:16], c3[:16], "p_" + c5[:16], "p_" + c6[:16]
        f1, f2wrong, f5, f6wrong = put("one.nes", r1), put("two-is-really-one.nes", r1), put("p5.unh", r5), put("p6-other.nes", r2)
        ps.save_store({L1: {"version_id": L1, "path": f1}, L2: {"version_id": L2, "path": f2wrong}})
        ps.save_index({"version": 1, "entries": {P5: {"id": P5, "managed_copy": f5, "source_path": f5}, P6: {"id": P6, "managed_copy": f6wrong, "source_path": f6wrong}}})
        titles = {L1: "Game One", L2: "Game Two (Rev 1)", L3: "Game Three", P5: "My Homebrew", P6: "Other Homebrew"}
        with NoNetwork() as nn:
            # ---- rom-check ----
            h = post("/api/package/rom-check", {"games": [{"version_id": i, "title": titles.get(i, i)} for i in (L1, L2, L3, "x_unknown", P5, P6, "../evil")]})
            o = h.obj or {}; st = {k: [x["version_id"] for x in o.get(k, [])] for k in ("available", "missing", "mismatched")}
            ok("rom_check_classifies_exactly", h.code == 200 and st == {"available": [L1, P5], "missing": [L3, "x_unknown", "../evil"], "mismatched": [L2, P6]}, st)
            ok("rom_check_reasons", [x["reason"] for x in o["missing"]] == ["not_found", "no_identity", "no_identity"] and all(x["reason"] == "different_version" for x in o["mismatched"]) and o["available"][0]["content_sha256"] == c1)
            # ---- export: complete only ----
            before = staged(); h = post("/api/package/export", {"collection": collection([L1, L2, L3, P5], titles, "Mixed"), "include_roms": True, "roms": [{"version_id": i, "title": titles[i]} for i in (L1, L2, L3, P5)]})
            ok("export_incomplete_is_409_with_lists", h.code == 409 and h.obj["ok"] is False and [x["version_id"] for x in h.obj["missing"]] == [L3] and [x["version_id"] for x in h.obj["mismatched"]] == [L2] and "Nothing was saved" in h.obj["error"], h.obj)
            ok("export_incomplete_writes_nothing", staged() == before and not [n for n in os.listdir(pk.stage_dir(data_path)) if n.endswith(".part")])
            h = post("/api/package/export", {"collection": collection([L1, "x_placeholder"], titles, "Placeholder")})
            ok("export_placeholder_is_409_missing_no_identity", h.code == 409 and h.obj["missing"] == [{"version_id": "x_placeholder", "title": "x_placeholder", "reason": "no_identity"}], h.obj)
            coll = collection([L1, P5], titles, "Two: good games"); coll["ccs_collection"]["loadouts"] = [{"id": "l1", "name": "All", "sel": [L1, P5]}]
            h = post("/api/package/export", {"collection": coll, "include_roms": False, "art": {P5 + ".jpg": "data:image/jpeg;base64,/9j/AAAA"}, "summary": {"loadouts": 1}})
            ex = h.obj or {}
            ok("export_complete_ignores_include_roms_false", h.code == 200 and ex.get("ok") and ex["games"] == 2 and [x["version_id"] for x in ex["included"]] == [L1, P5] and ex["name"] == "Two good games.ccs", ex)
            pid = ex.get("id", ""); pp = os.path.join(pk.stage_dir(data_path), pid + ".ccs")
            info = cp.inspect(pp); ok("exported_package_verifies", info["ok"] and info["data"] == coll and all(r["verified"] for r in info["roms"]) and info["art"] == [f"art/{P5}.jpg"])
            g = FH(); pk.handle_get(g, "/api/package/preview", "id=" + pid, data_path)
            ok("preview_reports_valid_and_identities", g.code == 200 and g.obj["valid"] is True and g.obj["games"] == 2 and {r["version_id"]: r["content_sha256"] for r in g.obj["roms"]} == {L1: c1, P5: c5} and "missing_roms" not in g.obj, g.obj)
            g = FH(); pk.handle_get(g, "/api/package/file", "id=" + pid, data_path); ok("file_route_streams_the_package", g.code == 200 and g.wfile.getvalue() == open(pp, "rb").read())
            h = post("/api/package/export", {"backup": {"collections": []}}); ok("backup_export_refused", h.code == 400 and "backup file" in h.obj["error"])
            h = post("/api/package/export", {"collection": {"schema": "nes-classic-collection/4"}}); ok("export_without_game_list_refused", h.code == 400 and "no game list" in h.obj["error"])
            real_du = shutil.disk_usage
            try:
                shutil.disk_usage = lambda p: real_du(p)._replace(free=1024); before = staged()
                h = post("/api/package/export", {"collection": coll}); ok("export_without_free_space_is_507", h.code == 507 and "free space" in h.obj["error"] and staged() == before, h.obj)
                h = post("/api/package/open", {"path": pp}); ok("open_without_free_space_is_507", h.code == 507, h.obj)
            finally: shutil.disk_usage = real_du
            # ---- save / reveal ----
            seen = {}; dest_dir = os.path.join(TMP, "Saved ünï folder"); os.makedirs(dest_dir); dest = os.path.join(dest_dir, "My pack.ccs")
            pk.pick_save = lambda title, name, d="": (seen.update(title=title, name=name), dest)[1]
            h = post("/api/package/save", {"id": pid, "name": "My: pack"})
            ok("save_copies_to_the_chosen_path", h.code == 200 and h.obj == {"ok": True, "name": "My pack.ccs", "path": dest} and open(dest, "rb").read() == open(pp, "rb").read() and seen["name"] == "My pack.ccs", (h.obj, seen))
            ok("save_leaves_no_partial_file", not os.path.exists(dest + ".part"))
            pk.pick_save = lambda *a: ""
            h = post("/api/package/save", {"id": pid, "name": "x"}); ok("save_canceled", h.code == 200 and h.obj == {"ok": False, "canceled": True}, h.obj)
            h = post("/api/package/save", {"id": "A" * 24, "name": "x"}); ok("save_unknown_package_404", h.code == 404)
            def no_dialog(*a): raise AssertionError("dialog")
            pk.pick_save = no_dialog; d2 = os.path.join(dest_dir, "rehearsal copy")
            h = post("/api/package/save", {"id": pid, "name": "x", "path": d2}, cfg={"rehearsal_tree": True}); ok("save_path_override_only_in_rehearsal", h.code == 200 and h.obj["path"] == d2 + ".ccs" and os.path.isfile(d2 + ".ccs"), h.obj)
            try: h = post("/api/package/save", {"id": pid, "name": "x", "path": d2}); ok("save_path_ignored_outside_rehearsal", False, h.obj)
            except AssertionError: ok("save_path_ignored_outside_rehearsal", True)
            opened = []; pk.open_explorer = lambda p: opened.append(p)
            h = post("/api/package/reveal", {"id": pid}); ok("reveal_selects_the_last_saved_file", h.code == 200 and opened == [d2 + ".ccs"], (h.obj, opened))
            h = post("/api/package/reveal", {"id": "B" * 24}); ok("reveal_unknown_id_404", h.code == 404 and len(opened) == 1)
            h = post("/api/package/reveal", {"id": "../../x"}); ok("reveal_bad_id_404", h.code == 404 and len(opened) == 1)
            os.remove(d2 + ".ccs"); h = post("/api/package/reveal", {"id": pid}); ok("reveal_moved_file_404", h.code == 404 and "moved or deleted" in h.obj["error"] and len(opened) == 1)
            # ---- stage / open / handoff: no size cap, space-checked, complete packages only ----
            before = staged(); h = post("/api/package/stage", raw=b"", length=10 ** 15)
            ok("stage_beyond_free_space_is_507", h.code == 507 and "free space" in h.obj["error"] and staged() == before, h.obj)
            if real_du(pk.stage_dir(data_path)).free > 3.3 * 1024 ** 3:
                h = post("/api/package/stage", raw=b"PK", length=3 * 1024 ** 3); ok("stage_has_no_2gb_limit", h.code == 400 and "did not arrive completely" in h.obj["error"] and staged() == before, h.obj)
            else: print("SKIP stage_has_no_2gb_limit: less than 3.3 GB free")
            h = post("/api/package/stage", raw=open(pp, "rb").read()); ok("stage_valid_package", h.code == 200 and h.obj["ok"] and h.obj["valid"], h.obj)
            v1 = craft(os.path.join(TMP, "v1.ccs"), collection([L1]), {L1: r1}, man_patch=lambda m: dict(m, schema="ccs-package/1"))
            h = post("/api/package/open", {"path": v1}); ok("open_schema1_refused_plainly", h.code == 200 and h.obj["ok"] is False and "Ask for a new .ccs file." in h.obj["error"], h.obj)
            h = post("/api/package/open", {"path": pp}); ok("open_valid_package", h.code == 200 and h.obj["ok"] and h.obj["file_name"] == os.path.basename(pp))
            inc = craft(os.path.join(TMP, "inc.ccs"), collection([L1, L3]), {L1: r1})
            hh = FH(raw=open(inc, "rb").read()); pk.handle_website_package(hh, data_path); ok("handoff_refuses_incomplete_package", hh.code == 400 and "every game" in hh.obj["error"], hh.obj)
            hh = FH(raw=open(v1, "rb").read()); pk.handle_website_package(hh, data_path); ok("handoff_refuses_schema1", hh.code == 400 and "older format" in hh.obj["error"], hh.obj)
            hh = FH(raw=open(pp, "rb").read()); pk.handle_website_package(hh, data_path); ok("handoff_stages_complete_package", hh.code == 200 and hh.obj["ok"], hh.obj)
            # ---- import ----
            h = post("/api/package/import-roms", {"id": pid}); ent = ps.load_index()["entries"]
            ok("import_stores_exact_identities", h.code == 200 and sorted(x["version_id"] for x in h.obj["imported"]) == sorted([L1, P5]) and not h.obj["rejected"] and all(x["exact"] for x in h.obj["imported"]), h.obj)
        ok("no_network_attempted_by_package_routes", nn.attempts == [], nn.attempts[:3])
        http_checks(ok, pp)
    finally:
        shutil.rmtree(TMP, ignore_errors=True)
    bad = [k for k, v in checks if not v]; print("RESULT:", "PASS" if not bad else "FAIL " + json.dumps(bad), f"({len(checks)} checks)"); return 0 if not bad else 1

def http_checks(ok, package_path):
    from version import SITE_ORIGINS
    port = int(os.environ.get("NES_TEST_PORT", "8796")); B = f"http://127.0.0.1:{port}"; site = SITE_ORIGINS[0]; work = os.path.join(TMP, "http")
    os.makedirs(work); token = secrets.token_hex(32)
    cfgp = os.path.join(work, "companion.json"); json.dump({"hakchi_exe": os.path.join(work, "no-hakchi", "hakchi.exe"), "hakchi_args": ["/portable"], "exports_dir": os.path.join(work, "e"), "staging_dir": os.path.join(work, "s"), "port": port, "no_launch": True}, open(cfgp, "w"))
    env = dict(os.environ, NES_COMPANION_CONFIG=cfgp, NES_PERSONAL_DIR=os.path.join(work, "personal"), NES_STUDIO_DATA=work, CCS_HOST_TOKEN=token, CCS_HOST_PID=str(os.getpid()), PYTHONIOENCODING="utf-8")
    log = open(os.path.join(work, "companion.log"), "w", encoding="utf-8")
    proc = subprocess.Popen([sys.executable, "-u", os.path.join(HERE, "companion_app.py"), "--headless", "--port", str(port)], env=env, stdout=log, stderr=subprocess.STDOUT)
    def req(method, path, body=None, headers=None, raw=None):
        data = raw if raw is not None else (json.dumps(body).encode() if body is not None else None)
        r = urllib.request.Request(B + path, data=data, headers={"Content-Type": "application/octet-stream" if raw is not None else "application/json", **(headers or {})}, method=method)
        try:
            with urllib.request.urlopen(r, timeout=60) as resp: return resp.status, json.loads(resp.read() or b"{}")
        except urllib.error.HTTPError as e:
            try: return e.code, json.loads(e.read() or b"{}")
            except Exception: return e.code, {}
        except Exception as e: return 0, {"error": str(e)}
    desk = {"Origin": site, "Authorization": "Bearer " + token}; web = {"Origin": site}
    try:
        for _ in range(160):
            try: urllib.request.urlopen(B + "/api/ping", timeout=1); break
            except Exception: time.sleep(0.25)
        s, j = req("GET", "/api/version", headers=desk); ok("http_capabilities_advertise_ccs_package_2", s == 200 and "ccs-package-2" in (j.get("capabilities") or []) and "package-save" in j["capabilities"], j)
        ok("http_save_refused_for_website", req("POST", "/api/package/save", {"id": "A" * 24}, headers=web)[0] in (401, 403))
        ok("http_reveal_refused_for_website", req("POST", "/api/package/reveal", {"id": "A" * 24}, headers=web)[0] in (401, 403))
        ok("http_save_refused_for_other_origin", req("POST", "/api/package/save", {"id": "A" * 24}, headers={"Origin": "https://evil.example", "Authorization": "Bearer " + token})[0] in (401, 403))
        ok("http_reveal_unknown_404_for_desktop", req("POST", "/api/package/reveal", {"id": "A" * 24}, headers=desk)[0] == 404)
        s, j = req("POST", "/api/package/export", {"collection": collection(["0123456789abcdef"], {}, "Nothing here")}, headers=desk)
        ok("http_export_incomplete_409", s == 409 and j.get("missing") and j["missing"][0]["reason"] == "not_found", (s, j))
        s, j = req("POST", "/api/package/stage", raw=open(package_path, "rb").read(), headers=desk); ok("http_stage_valid", s == 200 and j.get("valid") is True, (s, j))
        s, j = req("POST", "/api/handoff/package", raw=open(package_path, "rb").read(), headers=web); ok("http_handoff_valid", s == 200 and j.get("ok"), (s, j))
        req("POST", "/api/shutdown", {}, headers=desk)
        try: proc.wait(timeout=15)
        except subprocess.TimeoutExpired: pass
    finally:
        if proc.poll() is None: proc.kill()
        log.close()

if __name__ == "__main__": sys.exit(main())
