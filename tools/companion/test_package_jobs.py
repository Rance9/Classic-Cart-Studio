r"""Export progress, cancel and the Background Helper's inactivity timeout (package_api.py, nes_companion.py). Python only; isolated data
folders; synthetic ROMs (random bytes behind an iNES header); the Save As dialog and File Explorer are replaced by recorders.
  jobs       a small export and a large (about 1.2 GB) export run as jobs: verify -> write -> check -> done, progress that only moves forward
             and matches the file, a package that passes full verification, about the speed of the same export without a job
  cancel     during verification, during writing and during Save: stops within seconds, no file under the final name, no partial
             files, the ROM files unchanged; the next export works
  send       Send to Phone stages the package; Save copies it as a job; Show in Folder opens only the saved path
  timeout    the headless helper (CCS_HELPER_IDLE_TIMEOUT=4): an upload that keeps arriving for 3 timeouts succeeds, one with short
             pauses succeeds, one that stops gets 408 and its connection closed, no partial file, the helper keeps answering;
             untrusted origins, forged tokens and the removed /pair routes stay refused; the website handoff still works
  python test_package_jobs.py [--work C:\NES\work\ux\jobs-test] [--http-only]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, hashlib, io, json, os, random, re, secrets, shutil, socket, subprocess, sys, tempfile, threading, time, urllib.error, urllib.request
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path[:0] = [HERE, os.path.join(HERE, "..", "rom-pipeline")]
ap = argparse.ArgumentParser(); ap.add_argument("--work", default=""); ap.add_argument("--http-only", action="store_true"); A = ap.parse_args()
TMP = A.work or tempfile.mkdtemp(prefix="ccs-jobs-"); shutil.rmtree(TMP, ignore_errors=True); os.makedirs(TMP)
os.environ["NES_PERSONAL_DIR"] = os.path.join(TMP, "personal"); os.environ["NES_STUDIO_DATA"] = os.path.join(TMP, "studio-data")
import ccs_package as cp, package_api as pk, personal as ps
from test_ccs_package import collection
from test_package_api import FH
MB = 10**6   # decimal, like everything people see

def rom(seed, size: int) -> bytes:
    return b"NES\x1a" + bytes([min(255, max(1, size // 16384)), 1, 0, 0]) + bytes(8) + random.Random(seed).randbytes(size)

def make_set(tag: str, sizes: list) -> list:
    """personal games with managed copies: [(id, path, title)]"""
    out = []; idx = ps.load_index()
    for i, n in enumerate(sizes):
        d = rom(f"{tag}-{i}", n); c = cp.identify_bytes(d)["content_sha256"]; pid = "p_" + c[:16]
        p = os.path.join(TMP, "roms", tag, f"{tag} game {i}.nes"); os.makedirs(os.path.dirname(p), exist_ok=True); open(p, "wb").write(d)
        idx["entries"][pid] = {"id": pid, "managed_copy": p, "source_path": p}; out.append((pid, p, f"{tag.title()} Game {i + 1}"))
    ps.save_index(idx); return out

def sha(p: str) -> str:
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(MB), b""): h.update(b)
    return h.hexdigest()

class _Skip(Exception):
    pass

def main():
    checks = []
    def ok(k, v, n=""): checks.append((k, bool(v))); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:400]})" if n and not v else ""), flush=True)
    data_path = lambda *p: os.path.join(TMP, "data", *p)
    REH = {"rehearsal_tree": True}
    post = lambda path, body=None, cfg=None: (lambda h: (pk.handle_post(h, path, data_path, "0.7.0-test", cfg or REH), h)[1])(FH(body))
    revealed = []; pk.open_explorer = lambda p: revealed.append(p); pk.pick_save = lambda *a, **k: (_ for _ in ()).throw(AssertionError("no dialog in tests"))
    def body(games, name, **kw):
        ids = [g[0] for g in games]
        return {"collection": collection(ids, {g[0]: g[2] for g in games}, name=name), "roms": [{"version_id": g[0], "title": g[2]} for g in games], "summary": {"name": name}, "name": name + ".ccs", **kw}
    def run(b, cancel_when=None, save=None):
        """start an export (or save) job and poll it every 20 ms; cancel_when(sample) -> True asks to cancel. Returns (final, samples, seconds after cancel)"""
        h = post(save or "/api/package/export", b); assert h.code == 200 and h.obj.get("job"), h.obj
        jid = h.obj["job"]; samples = []; t_cancel = None
        while True:
            j = post("/api/package/job", {"job": jid}).obj["job"]; samples.append(dict(j, t=time.time()))
            if j["phase"] in ("done", "error", "canceled"): break
            if cancel_when and t_cancel is None and cancel_when(j): post("/api/package/cancel", {"job": jid}); t_cancel = time.time()
            time.sleep(0.02)
        return j, samples, (time.time() - t_cancel) if t_cancel else None
    def clean(folder: str, base: str) -> list:   # anything left under the destination name: the file, .part or .part.part
        return sorted(n for n in os.listdir(folder) if n.startswith(base))
    out = os.path.join(TMP, "Saved"); os.makedirs(out)
    try:
        if A.http_only: raise _Skip()
        small = make_set("small", [16384, 40000, 24576])
        large = make_set("large", [100 * MB] * 12)
        large_sha = {p: sha(p) for _, p, _ in large}; total = sum(os.path.getsize(p) for _, p, _ in large)
        # ---- a small export as a job: the Save dialog first (here: the rehearsal path), then the file ----
        dest = os.path.join(out, "Small Set.ccs")
        j, s, _ = run(body(small, "Small Set", job=True, save=True, path=dest))
        info = cp.inspect(dest, verify_roms=True)
        ok("small_export_job_completes", j["phase"] == "done" and j["saved_path"] == dest and j["saved_name"] == "Small Set.ccs" and info["ok"] and info["rom_count"] == 3 and clean(out, "Small Set") == ["Small Set.ccs"], j)
        r = post("/api/package/reveal", {"id": j["id"]}); ok("show_in_folder_opens_the_saved_file", r.code == 200 and revealed[-1:] == [dest], (r.obj, revealed))
        # ---- a large export: progress that is accurate and only moves forward ----
        pk._IDENT.clear(); dest = os.path.join(out, "Large Set.ccs"); t0 = time.time()
        j, s, _ = run(body(large, "Large Set", job=True, save=True, path=dest)); t_job = time.time() - t0
        phases = [x["phase"] for i, x in enumerate(s) if i == 0 or x["phase"] != s[i - 1]["phase"]]
        wr = [x for x in s if x["phase"] == "write"]; vf = [x for x in s if x["phase"] == "verify"]
        mono = lambda xs, k: all(b[k] >= a[k] for a, b in zip(xs, xs[1:]))
        ok("large_export_shows_each_stage", [p for p in phases if p != "starting"] == ["verify", "write", "check", "done"], phases)
        ok("verification_progress_counts_games_and_bytes", vf and mono(vf, "games_done") and mono(vf, "bytes_done") and vf[-1]["games"] == 12 and len({x["games_done"] for x in vf}) >= 4, [(x["games_done"], x["bytes_done"]) for x in vf[::max(1, len(vf) // 8)]])
        ok("writing_progress_is_accurate_and_advances", wr and mono(wr, "bytes_done") and mono(wr, "games_done") and all(x["bytes_total"] == total and x["bytes_done"] <= total for x in wr) and len({x["bytes_done"] for x in wr}) >= 10 and len({x["games_done"] for x in wr}) >= 6,
           [(x["games_done"], cp.fmt_size(x["bytes_done"])) for x in wr[::max(1, len(wr) // 10)]])
        info = cp.inspect(dest, verify_roms=True)
        ok("large_export_passes_full_verification", j["phase"] == "done" and j["size"] == os.path.getsize(dest) and info["ok"] and info["rom_count"] == 12 and all(x["verified"] for x in info["roms"]) and clean(out, "Large Set") == ["Large Set.ccs"], (j.get("error"), info.get("problems")))
        print(f"   large export: {cp.fmt_size(total)} of ROM files, package {cp.fmt_size(os.path.getsize(dest))}, {t_job:.1f} s as a job, {len(s)} progress samples")
        pk._IDENT.clear(); os.remove(dest); t0 = time.time()
        h = post("/api/package/export", body(large, "Large Set")); t_sync = time.time() - t0
        ok("progress_costs_little_time", h.code == 200 and t_job <= t_sync * 1.15 + 3, {"job_s": round(t_job, 1), "without_job_s": round(t_sync, 1)})
        print(f"   the same export without a job (no progress): {t_sync:.1f} s")
        # ---- cancel during verification, during writing; then export again ----
        for label, when in (("verification", lambda x: x["phase"] == "verify" and x["bytes_done"] > 200 * MB), ("writing", lambda x: x["phase"] == "write" and x["bytes_done"] > 300 * MB)):
            pk._IDENT.clear(); dest = os.path.join(out, f"Canceled during {label}.ccs")
            j, s, lag = run(body(large, "Canceled", job=True, save=True, path=dest), cancel_when=when)
            ok(f"cancel_during_{label}_stops_promptly_and_leaves_nothing", j["phase"] == "canceled" and lag is not None and lag < 3 and clean(out, f"Canceled during {label}") == [] and "No file was saved" in (j.get("error") or ""),
               {"phase": j["phase"], "seconds_to_stop": lag and round(lag, 2), "left": clean(out, f"Canceled during {label}")})
            print(f"   canceled during {label}: stopped {lag:.2f} s after Cancel")
        ok("canceling_never_changes_the_rom_files", all(sha(p) == v for p, v in large_sha.items()))
        dest = os.path.join(out, "After Cancel.ccs"); j, s, _ = run(body(large[:3], "After Cancel", job=True, save=True, path=dest))
        ok("export_works_again_after_cancel", j["phase"] == "done" and cp.inspect(dest, verify_roms=True)["ok"])
        # ---- a game whose ROM is gone: an error with the list, no file ----
        gone = make_set("gone", [16384, 16384]); os.remove(gone[1][1]); dest = os.path.join(out, "Gone.ccs")
        j, s, _ = run(body(gone, "Gone", job=True, save=True, path=dest))
        ok("missing_rom_ends_in_an_error_without_a_file", j["phase"] == "error" and [x["version_id"] for x in j.get("missing") or []] == [gone[1][0]] and clean(out, "Gone") == [], j)
        # ---- Send to Phone: staged, then Save as a job (and a canceled Save) ----
        j, s, _ = run(body(large[:4], "Phone Set", job=True, save=False))
        sid = j.get("stage_id"); staged = pk._path_for(pk.stage_dir(data_path), sid or "")
        ok("send_to_phone_stages_a_complete_package", j["phase"] == "done" and staged and cp.inspect(staged)["ok"] and j.get("saved_path") is None, j)
        dest = os.path.join(out, "Phone Set.ccs")
        j2, s2, _ = run({"id": sid, "name": "Phone Set.ccs", "job": True, "path": dest}, save="/api/package/save")
        sv = [x for x in s2 if x["phase"] == "save"]
        ok("save_copies_the_package_as_a_job", j2["phase"] == "done" and sha(dest) == sha(staged) and j2["saved_path"] == dest and (not sv or mono(sv, "bytes_done")), j2)
        r = post("/api/package/reveal", {"id": sid}); ok("show_in_folder_after_save", r.code == 200 and revealed[-1] == dest)
        d2 = os.path.join(out, "Phone Set canceled.ccs")
        j3, s3, lag = run({"id": sid, "name": "x.ccs", "job": True, "path": d2}, save="/api/package/save", cancel_when=lambda x: True)
        ok("canceled_save_leaves_nothing", j3["phase"] in ("canceled", "done") and (j3["phase"] == "done" or clean(out, "Phone Set canceled") == []), (j3["phase"], clean(out, "Phone Set canceled")))
        ok("cancel_after_done_changes_nothing", post("/api/package/cancel", {"job": j2["id"]}).obj["job"]["phase"] == "done" and os.path.isfile(dest))
        ok("unknown_job_is_404", post("/api/package/job", {"job": "x" * 16}).code == 404 and post("/api/package/cancel", {"job": "nope"}).code == 404)
    except _Skip: pass
    # ---- the headless helper over HTTP: inactivity timeout ----
    http_checks(ok)
    shutil.rmtree(TMP, ignore_errors=True)
    bad = [k for k, v in checks if not v]; print("RESULT:", "PASS" if not bad else "FAIL " + str(bad), f"({len(checks)} checks)"); return 0 if not bad else 1

def http_checks(ok):
    work = os.path.join(TMP, "helper"); os.makedirs(work); s = socket.socket(); s.bind(("127.0.0.1", 0)); port = s.getsockname()[1]; s.close(); token = secrets.token_hex(16)
    cfgp = os.path.join(work, "companion.json"); json.dump({"hakchi_exe": os.path.join(work, "no", "hakchi.exe"), "hakchi_args": ["/portable"], "exports_dir": os.path.join(work, "e"), "staging_dir": os.path.join(work, "s"), "port": port, "no_launch": True}, open(cfgp, "w"))
    env = dict(os.environ, NES_COMPANION_CONFIG=cfgp, NES_PERSONAL_DIR=os.path.join(work, "personal"), NES_STUDIO_DATA=work, CCS_HOST_TOKEN=token, CCS_HOST_PID=str(os.getpid()), PYTHONIOENCODING="utf-8", CCS_HELPER_IDLE_TIMEOUT="4")
    log = open(os.path.join(TMP, "helper.log"), "wb")
    proc = subprocess.Popen([sys.executable, "-u", os.path.join(HERE, "companion_app.py"), "--headless", "--port", str(port)], env=env, stdout=log, stderr=subprocess.STDOUT)
    SITE = "https://classiccartstudio.com"; desk = {"Authorization": "Bearer " + token, "Origin": SITE}
    def req(method, path, body=None, headers=None):
        d = json.dumps(body).encode() if body is not None else None
        r = urllib.request.Request(f"http://127.0.0.1:{port}{path}", data=d, method=method, headers={**({"Content-Type": "application/json"} if d else {}), **(headers or {})})
        try:
            with urllib.request.urlopen(r, timeout=30) as x: return x.status, json.loads(x.read() or b"{}")
        except urllib.error.HTTPError as e: return e.code, json.loads(e.read() or b"{}")
    def upload(path, headers, data: bytes, piece: int, gap: float, stop_after: int | None = None, wait: float = 30):
        """send headers + data in pieces with a pause after each; stop_after: stop sending (keep the connection open) after that many bytes.
        Returns (status or 0, seconds from the last byte sent to the answer or close, closed by the helper)"""
        c = socket.create_connection(("127.0.0.1", port), timeout=wait); c.settimeout(wait)
        hdr = "".join(f"{k}: {v}\r\n" for k, v in headers.items())
        c.sendall(f"POST {path} HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\n{hdr}Content-Type: application/octet-stream\r\nContent-Length: {len(data)}\r\n\r\n".encode())
        sent = 0; limit = len(data) if stop_after is None else stop_after
        while sent < limit:
            n = min(piece, limit - sent); c.sendall(data[sent:sent + n]); sent += n
            if gap and sent < limit: time.sleep(gap)
        t = time.time(); buf = b""; closed = False
        try:
            while b"\r\n\r\n" not in buf:
                b = c.recv(65536)
                if not b: break
                buf += b
            head, _, rest = buf.partition(b"\r\n\r\n"); m = re.search(rb"Content-Length: (\d+)", head); need = int(m.group(1)) if m else 0
            while len(rest) < need:
                b = c.recv(65536)
                if not b: break
                rest += b
            answered = time.time() - t
            if stop_after is not None:   # a stalled upload: the helper must also close the connection
                c.settimeout(3)
                try: closed = c.recv(1) == b""
                except socket.timeout: closed = False
        except socket.timeout: return 0, time.time() - t, False
        finally: c.close()
        return (int(buf[9:12]) if buf.startswith(b"HTTP/1.") else 0), answered, closed
    try:
        for _ in range(120):
            try:
                if req("GET", "/api/ping")[0] == 200: break
            except Exception: time.sleep(0.5)
        # a real package of about 60 MB (incompressible ROMs, stored)
        games = make_set("upload", [20 * MB] * 3); pkg = os.path.join(TMP, "Upload.ccs")
        cp.write(pkg, collection=collection([g[0] for g in games]), roms=[{"version_id": g[0], "path": g[1], "title": g[2]} for g in games], rom_compression=0)
        data = open(pkg, "rb").read(); staged = lambda: sorted(n for n in os.listdir(os.path.join(work, "packages")) if n.endswith((".ccs", ".part"))) if os.path.isdir(os.path.join(work, "packages")) else []
        # active: about 13 s in 1 MB pieces (three times the 4 s limit), never quiet for more than 0.2 s
        t0 = time.time(); code, _, _ = upload("/api/package/stage", desk, data, MB, 0.2); dur = time.time() - t0
        ok("long_active_upload_succeeds", code == 200 and dur > 3 * 4, {"status": code, "seconds": round(dur, 1)})
        print(f"   active upload: {cp.fmt_size(len(data))} over {dur:.1f} s with an inactivity limit of 4 s")
        code, _, _ = upload("/api/package/stage", desk, data, 16 * MB, 2.5)
        ok("pauses_shorter_than_the_limit_are_fine", code == 200, code)
        before = staged()
        code, waited, closed = upload("/api/package/stage", desk, data, MB, 0.05, stop_after=10 * MB)
        ok("stalled_upload_times_out_with_408_and_closes", code == 408 and closed and 3 <= waited <= 12, {"status": code, "seconds_after_last_byte": round(waited, 1), "closed": closed})
        time.sleep(0.5); ok("stalled_upload_leaves_no_partial_file", staged() == before, (before, staged()))
        code, waited, closed = upload("/api/handoff/package", {"Origin": SITE}, data, MB, 0.0, stop_after=5 * MB)
        ok("stalled_website_handoff_times_out_too", code == 408 and closed, {"status": code, "seconds": round(waited, 1)})
        c = socket.create_connection(("127.0.0.1", port)); c.settimeout(20); t = time.time()
        c.sendall(f"POST /api/package/rom-check HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nAuthorization: Bearer {token}\r\nOrigin: {SITE}\r\nContent-Type: application/json\r\nContent-Length: 100\r\n\r\n{{\"games\"".encode())
        r = b""
        try:
            while True:
                b = c.recv(4096)
                if not b: break
                r += b
        except socket.timeout: pass
        c.close(); ok("stalled_json_request_is_answered_and_closed", r.startswith(b"HTTP/1.1 408") and time.time() - t < 12, r[:60])
        c = socket.create_connection(("127.0.0.1", port)); c.settimeout(20); t = time.time()
        try: idle = c.recv(10) == b""
        except socket.timeout: idle = False
        c.close(); ok("idle_connection_is_closed_after_the_limit", idle and 3 <= time.time() - t <= 12, round(time.time() - t, 1))
        logtxt = "".join(open(f, "rb").read().decode("utf-8", "replace") for f in (os.path.join(TMP, "helper.log"), os.path.join(work, "logs", "server.log")) if os.path.exists(f))   # the headless helper logs to <data>\\logs\\server.log
        ok("stall_is_logged_without_content", "stalled" in logtxt and "partial file was removed" in logtxt and "NES\x1a" not in logtxt, [l for l in logtxt.splitlines() if "stall" in l][:3])
        ok("helper_keeps_answering", req("GET", "/api/ping")[0] == 200)
        # unchanged security
        code, j = req("GET", "/api/version", headers=desk); ok("capabilities_include_package_jobs", code == 200 and "package-jobs" in j.get("capabilities", []) and j.get("desktop") is True, j.get("capabilities"))
        ok("forged_token_refused", req("POST", "/api/package/job", {"job": "x"}, headers={"Authorization": "Bearer " + "0" * 32, "Origin": SITE})[0] in (401, 403))
        ok("untrusted_origin_refused", req("POST", "/api/handoff/package", None, headers={"Origin": "https://evil.example", "Content-Type": "application/octet-stream"})[0] == 403)
        ok("pair_routes_stay_removed", req("GET", "/pair")[0] == 404 and req("POST", "/api/pair/claim", {"code": "x"})[0] in (401, 403, 404))
        code, j = req("POST", "/api/package/job", {"job": "x"}, headers={"Origin": SITE}); ok("jobs_are_desktop_only", code in (401, 403), code)
        code, _, _ = upload("/api/handoff/package", {"Origin": SITE}, data, 4 * MB, 0.0)
        ok("website_handoff_still_works", code == 200, code)
        # a refused request's small body is read and dropped before the answer, so the connection closes cleanly (Windows resets a
        # socket closed with unread bytes, and the client then lost the answer now and then)
        errors = []
        for _ in range(500):
            for path, body, hdr in (("/api/pair/claim", {"code": "x"}, {}), ("/api/package/job", {"job": "x"}, {"Authorization": "Bearer " + "0" * 32, "Origin": SITE}), ("/api/package/cancel", {"job": "x"}, {"Origin": SITE})):
                try: req("POST", path, body, hdr)
                except Exception as e: errors.append((path, repr(e)[:120]))
        ok("refused_requests_always_get_their_answer", not errors, f"{len(errors)} of 1500 lost: {errors[:3]}")
    finally:
        proc.terminate()
        try: proc.wait(10)
        except Exception: proc.kill()
        log.close()

if __name__ == "__main__": sys.exit(main())
