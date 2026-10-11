r"""Console engine through the backend (simulation tier): the desktop principal reaches /api/console/*, the website
principal and unauthenticated callers never do; capabilities advertise console-sim with Gate G1 closed; identify, plan and
preflight; a simulated run streams events and ends in a simulated state; cancel; verify; execute is refused end to end
with every flag a client could send; nothing physical and no real transport exists in the worker.   python test_console_api.py"""
from __future__ import annotations
import os, sys, json, time, shutil, secrets, subprocess, tempfile, urllib.request, urllib.error
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE); sys.path.insert(0, os.path.join(HERE, "..", "rom-pipeline"))
PORT = 8771; B = f"http://127.0.0.1:{PORT}"; SITE = "https://classiccartstudio.com"

def req(method, path, body=None, headers=None):
    data = json.dumps(body).encode() if body is not None else None
    r = urllib.request.Request(B + path, data=data, method=method, headers={"Content-Type": "application/json", "X-NES-Protocol": "1", **(headers or {})})
    try:
        with urllib.request.urlopen(r, timeout=200) as resp: return resp.status, dict(resp.headers), json.loads(resp.read() or b"{}")
    except urllib.error.HTTPError as e:
        try: return e.code, dict(e.headers), json.loads(e.read() or b"{}")
        except Exception: return e.code, dict(e.headers), {}
    except Exception as e: return 0, {}, {"error": str(e)}

def main():
    import hakchi_bridge as hb
    from test_console_engine import man_for
    cat = json.load(open(hb.site_path("catalog.json"), encoding="utf-8"))["games"]; fref, factory = hb.factory_reference(hb.site_path("builtin.json"))
    exact = [g for g in cat if g.get("builtin_status") == "BUILT-IN: EXACT MATCH"]; new = [g for g in cat if not g.get("builtin_code") and g.get("recommended_emulator") == "kachikachi"]
    man = man_for([g["nointro_name"] for g in exact[:4]] + [g["nointro_name"] for g in new[:5]], None, cat, fref, factory)
    work = os.path.join(tempfile.gettempdir(), "nes-console-api-test"); shutil.rmtree(work, ignore_errors=True); os.makedirs(work)
    cfg = {"hakchi_exe": os.path.join(work, "no-hakchi", "hakchi.exe"), "hakchi_args": ["/portable"], "exports_dir": os.path.join(work, "exports"), "staging_dir": os.path.join(work, "staging"), "port": PORT, "no_launch": True}
    cfgp = os.path.join(work, "companion.json"); json.dump(cfg, open(cfgp, "w")); token = secrets.token_hex(32)
    env = dict(os.environ, NES_COMPANION_CONFIG=cfgp, NES_PERSONAL_DIR=os.path.join(work, "personal"), NES_STUDIO_DATA=work, CCS_HOST_TOKEN=token, CCS_HOST_PID=str(os.getpid()), PYTHONIOENCODING="utf-8")
    logf = open(os.path.join(work, "companion.log"), "w", encoding="utf-8")
    proc = subprocess.Popen([sys.executable, "-u", os.path.join(HERE, "companion_app.py"), "--headless", "--port", str(PORT)], env=env, stdout=logf, stderr=subprocess.STDOUT)
    checks = []; ok = lambda k, v, n="": checks.append(f"{k}={'true' if v else 'false'}{(' ('+str(n)[:260]+')') if n and not v else ''}")
    desk = {"Origin": SITE, "Authorization": "Bearer " + token}; web = {"Origin": SITE}; evil = {"Origin": "https://evil.example", "Authorization": "Bearer " + token}
    try:
        up = False
        for _ in range(160):
            try: urllib.request.urlopen(B + "/api/ping", timeout=1); up = True; break
            except Exception: time.sleep(0.25)
        ok("backend_starts", up)
        s, h, j = req("GET", "/api/version", headers=desk); ok("version_advertises_console_sim", s == 200 and "console-sim" in (j.get("capabilities") or []) and j.get("desktop") is True, j.get("capabilities"))
        # ---- principals ----
        ok("website_principal_cannot_reach_console_api", req("GET", "/api/console/capabilities", headers=web)[0] in (401, 403) and req("POST", "/api/console/identify", {"profile": "modified-compatible"}, headers=web)[0] in (401, 403))
        ok("no_auth_cannot_reach_console_api", req("POST", "/api/console/simulate", {"plan_hash": "0" * 64, "profile": "modified-compatible"})[0] in (401, 403))
        ok("wrong_origin_with_token_refused", req("GET", "/api/console/capabilities", headers=evil)[0] in (401, 403))
        s, h, j = req("GET", "/api/console/capabilities", headers=desk)
        ok("capabilities_from_isolated_worker", s == 200 and j["capabilities"]["transports"] == ["fake"] and j["capabilities"]["tiers"]["install"] is False and j["capabilities"]["tiers"]["inspect"] is False and j["capabilities"]["protocol"] == 1, j)
        profiles = [p["name"] for p in j["capabilities"]["profiles"]]; ok("profiles_listed_as_simulated", len(profiles) >= 20 and all(p["simulated"] for p in j["capabilities"]["profiles"]))
        # ---- identify ----
        s, h, j = req("POST", "/api/console/identify", {"profile": "modified-compatible"}, headers=desk); ok("identify_simulated_compatible", s == 200 and j["device"]["simulated"] is True and j["device"]["state_code"] == "compatible", j)
        s, h, j = req("POST", "/api/console/identify", {"profile": "no-device"}, headers=desk); ok("identify_no_device", s == 200 and j["device"]["state"] == "Console not connected")
        s, h, j = req("POST", "/api/console/identify", {"profile": "../real"}, headers=desk); ok("identify_rejects_bad_profile", s == 400 or (s == 200 and j["device"]["state_code"] == "no-device"))
        # ---- plan + preflight ----
        s, h, j = req("POST", "/api/console/plan", {"manifest": man, "profile": "modified-compatible"}, headers=desk)
        ok("plan_built_with_preflight", s == 200 and len(j["plan"]["hash"]) == 64 and j["plan"]["budget"]["source"] == "simulated" and j["preflight"]["ok"] is True and j["preflight"]["state"] == "Ready for simulated rehearsal", (s, str(j)[:200]))
        h64 = j["plan"]["hash"] if s == 200 else "0" * 64
        s, h, j2 = req("POST", "/api/console/plan", {"manifest": man, "profile": "stock-fel"}, headers=desk); ok("plan_on_fel_blocked_first_time_setup", s == 200 and j2["preflight"]["ok"] is False and any(b["code"] == "first-time-setup-required" for b in j2["preflight"]["blocks"]))
        s, h, j3 = req("POST", "/api/console/plan", {"manifest": man}, headers=desk); ok("plan_without_device_is_reference", s == 200 and j3["plan"]["budget"]["source"] == "reference" and j3["preflight"]["ok"] is False and any(b["code"] == "no-device-state" for b in j3["preflight"]["blocks"]))
        ok("plan_requires_manifest", req("POST", "/api/console/plan", {"profile": "modified-compatible"}, headers=desk)[0] == 400)
        # ---- simulate: streams, ends simulated ----
        s, h, j = req("POST", "/api/console/simulate", {"plan_hash": h64, "profile": "modified-compatible"}, headers=desk); ok("simulate_starts_run", s == 200 and j.get("run") and j.get("simulated") is True, j)
        rid = j.get("run", ""); last = None; cur = 0; seen = []
        for _ in range(240):
            s, h, r = req("GET", f"/api/console/run?id={rid}&cursor={cur}", headers=desk)
            if s != 200: break
            seen += r["events"]; cur = r["cursor"]; last = r
            if r["done"]: break
            time.sleep(0.25)
        ok("run_polls_events_then_result", last is not None and last["done"] and last["result"]["event"] == "result" and any(e["event"] == "progress" for e in seen) and any(e["event"] == "stage" and e["stage"] == "transfer" for e in seen), (len(seen), (last or {}).get("state")))
        ok("run_result_is_simulated_success", last and last["result"]["result"]["state"] == "Simulated install succeeded" and last["result"]["result"]["simulated"] is True and last["result"]["result"]["verify"]["match"] is True)
        ok("run_unknown_id_404", req("GET", "/api/console/run?id=nope", headers=desk)[0] == 404 and req("GET", f"/api/console/run?id={rid}", headers=web)[0] in (401, 403))
        s, h, j = req("POST", "/api/console/simulate", {"plan_hash": h64, "profile": "disconnect-during-transfer"}, headers=desk); rid2 = j.get("run", "")
        for _ in range(240):
            s, h, r = req("GET", f"/api/console/run?id={rid2}", headers=desk)
            if r.get("done"): break
            time.sleep(0.25)
        ok("failure_injection_reports_recoverable_state", r.get("done") and r["result"]["result"]["state"].startswith("Potentially recoverable") and r["result"]["result"]["error"]["code"] == "disconnected" and "run the same plan again" in r["result"]["result"]["error"]["recovery"], str(r.get("result"))[:200])
        ok("simulate_rejects_bad_hash", req("POST", "/api/console/simulate", {"plan_hash": "C:\\plans\\x.json", "profile": "modified-compatible"}, headers=desk)[0] == 400 and req("POST", "/api/console/simulate", {"plan_hash": h64, "profile": ""}, headers=desk)[0] == 400)
        # ---- cancel on a finished run, verify, recover ----
        s, h, j = req("POST", "/api/console/cancel", {"run": rid}, headers=desk); ok("cancel_finished_run_is_noop", s == 200 and j.get("requested") is not True and "no running" in (j.get("error") or ""))
        s, h, j = req("POST", "/api/console/verify", {"plan_hash": h64, "profile": "modified-compatible"}, headers=desk); ok("verify_independent_read_back", s == 200 and j["verify"]["simulated"] is True and j["verify"]["match"] is False)
        s, h, j = req("POST", "/api/console/recover", {"stage": "memboot"}, headers=desk); ok("recover_advice", s == 200 and "FEL" in j.get("advice", "") and j["physical"]["install"] is False)
        # ---- execute refused end to end, whatever the client sends ----
        refused = []
        for extra in ({}, {"transport": "ssh"}, {"transport": "fel", "force": True}, {"physical": True, "approve": "yes", "gate": "open"}, {"PHYSICAL_WRITE_ENABLED": True}, {"transport": "fake"}):
            s, h, j = req("POST", "/api/console/execute", {"plan_hash": h64, **extra}, headers=desk); refused.append(s == 403 and j.get("code") == "physical-disabled" and j.get("physical") is False)
        ok("execute_refused_physical_disabled_for_every_payload", all(refused), refused)
        ok("execute_refused_for_website_principal_too", req("POST", "/api/console/execute", {"plan_hash": h64}, headers=web)[0] in (401, 403))
        # ---- nothing physical in the worker's reach ----
        s, h, j = req("GET", "/api/console/capabilities", headers=desk); ok("still_only_fake_transport_after_runs", j["capabilities"]["transports"] == ["fake"])
        ok("plans_live_under_backend_data_root", os.path.isdir(os.path.join(work, "console", "plans")) and any(f.startswith("plan-") for f in os.listdir(os.path.join(work, "console", "plans"))))
        ok("journals_written_without_secrets", any(f.endswith(".jsonl") for f in os.listdir(os.path.join(work, "console", "journals"))) and token not in "".join(open(os.path.join(work, "console", "journals", f), encoding="utf-8").read() for f in os.listdir(os.path.join(work, "console", "journals"))))
        # ---- shutdown stops the workers ----
        s, h, j = req("POST", "/api/shutdown", {}, headers=desk); ok("shutdown_accepted", s == 200 and j.get("stopping") is True)
        try: proc.wait(timeout=20); ok("backend_exits", proc.returncode == 0, proc.returncode)
        except subprocess.TimeoutExpired: ok("backend_exits", False, "timeout")
        time.sleep(0.5)
    except Exception as e:
        ok("harness", False, repr(e)[:200])
    finally:
        if proc.poll() is None: proc.kill()
        logf.close()
    for c in checks: print(c)
    fails = [c for c in checks if "=false" in c]; print(("RESULT: PASS" if not fails else "RESULT: FAIL " + str(fails)) + f" ({len(checks)} checks)"); return 1 if fails else 0

if __name__ == "__main__": sys.exit(main())
