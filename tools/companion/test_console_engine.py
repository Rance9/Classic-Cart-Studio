r"""Console engine suite (simulation tier, Gate G1 closed): plan fidelity, every fake device profile, preflight blocks,
storage budget, no-op repeat, incremental diff, foreign content preserved, failure injection and recovery states,
cancel at safe checkpoints only, worker protocol hygiene, and the negative gate tests (nothing opens a real transport
whatever the caller passes). Runs on the PC only.   python test_console_engine.py"""
from __future__ import annotations
import os, sys, json, io, tempfile, shutil, time, re
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE); sys.path.insert(0, os.path.join(HERE, "..", "rom-pipeline"))
import hakchi_bridge as hb
from console_engine import gate, profiles, transport, plan as planmod, journal

def man_for(names, decisions, cat, fref, factory):
    """Schema-3 manifest built the same way the page does (same visibility rule); copied from test_hakchi_bridge so this
    suite stands alone in the public repository."""
    by = {g["nointro_name"]: g for g in cat}; decisions = decisions or {}
    chosen = sorted([by[n] for n in names], key=lambda g: g["game_id"])
    def cls(g):
        st = g.get("builtin_status") or ""
        return "builtin" if st == "BUILT-IN: EXACT MATCH" else "revision" if st == "BUILT-IN: DIFFERENT REVISION" else "review" if st == "BUILT-IN: IDENTITY ONLY" else "new"
    def dec(g): return decisions.get(g["game_id"], "factory") if cls(g) in ("revision", "review") else None
    def inst(g): c = cls(g); return True if c == "new" else False if c == "builtin" else dec(g) == "alternate"
    vis = {g["builtin_code"] for g in chosen if g.get("builtin_code") and (cls(g) == "builtin" or (cls(g) in ("revision", "review") and dec(g) == "factory"))}
    hidden = [{"code": cd, "title": factory[cd]["factory_title"]} for cd in fref["codes"] if cd not in vis]
    games = [{"game_id": g["game_id"], "display_title": g["display_title"], "nointro_name": g["nointro_name"], "payload_sha256": g["payload_sha256"], "recommended_emulator": g["recommended_emulator"], "builtin_status": g["builtin_status"], "builtin_code": g.get("builtin_code") or None,
              "builtin_factory_version": g.get("builtin_factory_version") or None, "comparison_status": {"builtin": "exact-match", "revision": "different-revision", "review": "identity-only", "new": "not-built-in"}[cls(g)], "install_required": inst(g), "revision_decision": dec(g),
              "input_requirements": {k[6:]: g.get(k) for k in g if k.startswith("input_")}} for g in chosen]
    return {"schema": "nes-classic-selection/3", "collection_mode": "exclusive", "factory_game_policy": "hide_unselected", "exported": time.strftime("%Y-%m-%dT%H:%M:%S"), "factory_inventory": dict(fref), "count": len(games), "games": games, "hidden_factory_games": hidden, "deployment": {"console_enforcement": "pending"}}
from console_engine.engine import Engine, new_run_id, SAFE_CANCEL_STAGES
from console_engine.worker import Worker, PROTOCOL_VERSION

checks = []
def ok(k, v, n=""):
    checks.append(f"{k}={'true' if v else 'false'}{(' ('+str(n)[:300]+')') if n and not v else ''}")

def main():
    cat = json.load(open(hb.site_path("catalog.json"), encoding="utf-8"))["games"]; fref, factory = hb.factory_reference(hb.site_path("builtin.json"))
    exact = [g for g in cat if g.get("builtin_status") == "BUILT-IN: EXACT MATCH"]; new = [g for g in cat if not g.get("builtin_code") and g.get("recommended_emulator") == "kachikachi"]
    ra = [g for g in cat if not g.get("builtin_code") and g.get("recommended_emulator") == "RetroArch"]
    names_a = [g["nointro_name"] for g in exact[:5]] + [g["nointro_name"] for g in new[:6]]
    names_b = names_a + [g["nointro_name"] for g in new[6:9]]
    names_ra = [g["nointro_name"] for g in exact[:2]] + [g["nointro_name"] for g in ra[:1]]
    man_a, man_b, man_ra = (man_for(n, None, cat, fref, factory) for n in (names_a, names_b, names_ra))
    tmp = tempfile.mkdtemp(prefix="ccs-engine-"); jpath = lambda: os.path.join(tmp, "j", new_run_id() + ".jsonl")
    def eng(plan_hash=""): return Engine(journal.Journal(jpath(), new_run_id(), plan_hash))

    # ---- gate: constants only; nothing a caller controls opens a real transport ----
    ok("gate_closed_in_code", gate.PHYSICAL_INSPECT_ENABLED is False and gate.PHYSICAL_WRITE_ENABLED is False and gate.state()["install"] is False)
    for k, v in (("CCS_PHYSICAL", "1"), ("PHYSICAL_WRITE_ENABLED", "true"), ("CCS_GATE", "open"), ("HAKCHI_ALLOW_WRITE", "yes")): os.environ[k] = v
    refused = 0
    for kind in ("ssh", "clovershell", "fel"):
        for target in (None, "modified-compatible", {"host": "169.254.13.37"}, "?physical=1", "C:\\Windows"):
            try: transport.open_transport(kind, target);
            except gate.GateClosed: refused += 1
            except Exception as e: ok("gate_refusal_type_" + kind, False, repr(e))
    ok("gate_refuses_every_real_transport_regardless_of_args_and_env", refused == 15, refused)
    for k in ("CCS_PHYSICAL", "PHYSICAL_WRITE_ENABLED", "CCS_GATE", "HAKCHI_ALLOW_WRITE"): os.environ.pop(k, None)
    ok("only_fake_transport_class_exists", not any(n for n in dir(transport) if n.lower().endswith("transport") and n not in ("FakeTransport", "open_transport", "TransportError")), [n for n in dir(transport) if "ransport" in n])
    src = open(os.path.join(HERE, "console_engine", "transport.py"), encoding="utf-8").read() + open(os.path.join(HERE, "console_engine", "engine.py"), encoding="utf-8").read()
    ok("no_usb_socket_or_ssh_imports_in_engine", not any(w in src for w in ("import usb", "import socket", "paramiko", "ftplib", "libusb", "serial")))

    # ---- identify: every profile yields a truthful state ----
    states = {}
    for name in profiles.names():
        t = transport.open_transport("fake", name); d = eng().identify(t); states[name] = (d.get("state"), d.get("state_code"), d.get("simulated"))
    ok("every_profile_identifies", all(s[0] for s in states.values()), {k: v for k, v in states.items() if not v[0]})
    ok("every_identification_is_marked_simulated", all(s[2] is True for s in states.values()))
    ok("no_device_state", states["no-device"][1] == "no-device" and states["stock-normal"][1] == "no-device")
    ok("fel_state_needs_first_time_setup", states["stock-fel"][1] == "fel" and states["stock-fel"][0] == "Console recognized, mode known")
    ok("driver_states", states["stock-fel-driver-missing"][1] == "driver-missing" and states["stock-fel-driver-wrong"][1] == "driver-wrong-binding")
    ok("wrong_board_state", states["ambiguous-vidpid"][1] == "fel-wrong-board")
    ok("compatible_state", states["modified-compatible"][1] == "compatible" and states["modified-compatible"][0] == "Compatible modified environment already present")
    ok("wrong_model_states", states["wrong-model-snes"][1] == "wrong-model" and states["wrong-model-famicom"][1] == "wrong-model" and states["md-partitioning"][1] == "md-partitioning")
    ok("outdated_and_unknown_states", states["modified-outdated"][1] == "outdated" and states["modified-unknown-kernel"][1] == "no-version")
    ok("clovershell_cannot_sync", states["modified-clovershell-only"][1] == "clovershell-only")
    d = eng().identify(transport.open_transport("fake", "modified-compatible-with-games"))
    ok("identify_reads_storage_modules_backup_foreign", d["storage"]["total"] > 0 and d["storage"]["free"] > 0 and "_km_retroarch_1910_ozone" in d["modules"] and d["backup_state"] == "verified" and len(d["foreign"]) == 14, {k: d.get(k) for k in ("storage", "modules", "backup_state")})

    # ---- plan fidelity ----
    def mk(man, profile, **kw):
        t = transport.open_transport("fake", profile); dev = eng().identify(t); foreign = dev.pop("foreign", None)
        return planmod.build(man, device=dev, foreign=foreign, **kw), t
    pa, ta = mk(man_a, "modified-compatible")
    ok("plan_schema_and_hash", pa["schema"] == planmod.SCHEMA and len(pa["hash"]) == 64 and pa["can_proceed"], pa["problems"][:2])
    kinds = {e["kind"] for e in pa["entries"]}
    ok("plan_kinds_cover_factory_visible_hidden_added", {"factory-visible", "factory-hidden", "added"} <= kinds, kinds)
    ok("plan_counts", pa["summary"]["adds"] == 6 and pa["summary"]["keeps"] == 5 and pa["summary"]["hides"] == 25, pa["summary"])
    adds = [e for e in pa["entries"] if e["action"] == "add"]
    ok("added_entries_carry_exact_identity", all(e["sha256"] and e["version_id"] and e["code"].startswith("CLV-H-") and "?" not in e["code"] and e["size"] > 0 and len(e["files"]) == 5 for e in adds), [e["code"] for e in adds][:3])
    ok("same_plan_same_hash", planmod.build(man_a, device=pa["device"], foreign=None)["hash"] == pa["hash"])
    ok("different_collection_different_hash", mk(man_b, "modified-compatible")[0]["hash"] != pa["hash"])
    ok("budget_simulated_labeled", pa["budget"]["source"] == "simulated" and "not a real measurement" in pa["budget"]["text"] and pa["budget"]["fits"] is True, pa["budget"])
    pr = planmod.build(man_a, device=None)
    ok("budget_reference_when_no_device", pr["budget"]["source"] == "reference" and pr["budget"]["fits"] is None and pr["device"]["source"] == "none")
    pra, _ = mk(man_ra, "modified-compatible")
    ok("retroarch_route_needs_modules_and_is_flagged_not_cleared", any(m["name"] == "_km_retroarch_1910_ozone" and m["action"] == "install" and not m["cleared"] for m in pra["modules"]) and any("not cleared" in w for w in pra["warnings"]), pra["modules"])
    pf, _ = mk(man_a, "modified-compatible-with-games")
    ok("foreign_games_kept_in_plan", pf["summary"]["foreign"] == 14 and all(e["action"] == "keep-unknown" for e in pf["entries"] if e["kind"] == "foreign-keep"))
    folder = os.path.join(tmp, "plans"); path = planmod.save(pa, folder)
    ok("plan_saved_and_loaded_by_hash", planmod.load(folder, pa["hash"])["hash"] == pa["hash"] and planmod.load(folder, "0" * 64) is None)

    # ---- preflight blocks per profile ----
    def pre(man, profile):
        p, t = mk(man, profile); return eng(p["hash"]).preflight(p), p, t
    r, _, _ = pre(man_a, "modified-compatible"); ok("preflight_ready_for_simulated_rehearsal", r["ok"] and r["state"] == "Ready for simulated rehearsal" and r["physical"]["install"] is False, r)
    exp = {"no-device": "no-device", "stock-fel": "first-time-setup-required", "stock-fel-driver-missing": "driver-missing", "ambiguous-vidpid": "fel-wrong-board", "wrong-model-snes": "wrong-model",
           "modified-outdated": "first-time-setup-required", "modified-unknown-kernel": "first-time-setup-required", "modified-clovershell-only": "link-cannot-sync", "backup-missing": "backup-missing", "storage-insufficient": "storage-insufficient"}
    got = {}
    for prof, code in exp.items():
        r, _, _ = pre(man_a, prof); got[prof] = [b["code"] for b in r["blocks"]]
    ok("preflight_blocks_each_bad_profile_with_the_right_code", all(exp[k] in got[k] for k in exp), {k: got[k] for k in exp if exp[k] not in got[k]})
    r, _, _ = pre(man_a, "storage-marginal"); ok("preflight_marginal_storage_warns", r["ok"] and any(w["code"] == "storage-marginal" for w in r["warnings"]), r["warnings"])
    r, _, _ = pre(man_ra, "modified-compatible"); ok("preflight_blocks_uncleared_module", any(b["code"] == "module-not-cleared" for b in r["blocks"]))
    r, _, _ = pre(man_a, "modified-compatible-with-games"); ok("preflight_warns_foreign_kept", r["ok"] and any(w["code"] == "foreign-kept" for w in r["warnings"]))
    pm, _ = mk(man_a, "modified-compatible"); pm["entries"][0]["kind"] = "unresolved"; pm["entries"][0]["action"] = "none"
    ok("preflight_collection_incomplete_state", eng().preflight(pm)["state"] == "Collection incomplete")

    # ---- simulate: success, no-op repeat, incremental, foreign preserved, read-back ----
    pa, ta = mk(man_a, "modified-compatible"); e1 = eng(pa["hash"]); e1.identify(ta); r1 = e1.simulate(pa, ta)
    ok("simulate_succeeds_and_is_labeled_simulated", r1["state"] == "Simulated install succeeded" and r1["simulated"] is True and r1["verify"]["match"] and r1["verify"]["read_back_files"] == 30, r1)
    ok("simulate_never_claims_physical", "physical" not in r1["state"].lower() and "Installed" not in r1["state"])
    ok("simulated_result_hidden_config", len(r1["verify"]["hidden_config"]) == 25)
    e2 = eng(pa["hash"]); e2.identify(ta); r2 = e2.simulate(pa, ta)
    ok("repeat_same_plan_is_noop", r2.get("noop") is True and ta.transferred == sum(sum(e["files"].values()) for e in pa["entries"] if e["action"] == "add"), (r2.get("state"), ta.transferred))
    dev_after = eng().identify(ta); foreign_after = dev_after.pop("foreign", None)
    pb = planmod.build(man_b, device=dev_after, foreign=None); before = ta.transferred; e3 = eng(pb["hash"]); e3.identify(ta); r3 = e3.simulate(pb, ta)
    delta = sum(sum(e["files"].values()) for e in pb["entries"] if e["action"] == "add") - sum(sum(e["files"].values()) for e in pa["entries"] if e["action"] == "add")
    ok("changed_collection_transfers_only_the_difference", r3["state"] == "Simulated install succeeded" and ta.transferred - before == delta, (ta.transferred - before, delta))
    pf, tf = mk(man_a, "modified-compatible-with-games"); fs_before = dict(tf.fs); e4 = eng(pf["hash"]); e4.identify(tf); r4 = e4.simulate(pf, tf)
    ok("foreign_games_and_saves_preserved", r4["state"] == "Simulated install succeeded" and all(tf.fs.get(k) == v for k, v in fs_before.items()) and tf.p["storage"]["saves"] == 30 * 1024 * 1024, r4.get("verify"))
    pfr, tfr = mk(man_a, "modified-compatible-with-games", keep_foreign=False); e5 = eng(pfr["hash"]); e5.identify(tfr); r5 = e5.simulate(pfr, tfr)
    ok("explicit_removal_removes_foreign_only", r5["state"] == "Simulated install succeeded" and not any(re.match(r"CLV-H-F\d{4}/", k) for k in tfr.fs) and len(tfr.fs) == 30)
    t_rb = transport.open_transport("fake", "modified-compatible"); e6 = eng(); e6.identify(t_rb); ok("verify_before_install_reports_mismatch", (lambda rb, ex: not (rb and not [r for r in ex if r not in rb]))(t_rb.read_back(), planmod.expected_after(pa)))

    # ---- failure injection and recovery states ----
    def sim(profile, man=man_a):
        p, t = mk(man, profile); e = eng(p["hash"]); e.identify(t); return e.simulate(p, t), p, t
    r, _, _ = sim("disconnect-before-transfer"); ok("disconnect_before_transfer_recoverable", r["error"]["code"] == "disconnected" and r["state"].startswith("Potentially recoverable") and "run the same plan again" in r["error"]["recovery"], r)
    r, p, t = sim("disconnect-during-transfer"); ok("disconnect_during_transfer_partial_and_recoverable", r["error"]["code"] == "disconnected" and 0 < t.transferred < sum(sum(e["files"].values()) for e in p["entries"] if e["action"] == "add"), (t.transferred,))
    t.p["fail"] = None; t.open(); e7 = eng(p["hash"]); e7.identify(t); r7 = e7.simulate(p, t)
    ok("rerun_after_disconnect_completes_without_resending_everything", r7["state"] == "Simulated install succeeded" and r7["verify"]["match"], r7.get("verify"))
    saved_pins = dict(planmod.MODULE_PINS); planmod.MODULE_PINS.update({"_km_retroarch_1910_ozone": "f" * 64, "_km_fceumm_xtreme": "e" * 64})   # test only: pretend the modules are cleared
    try:
        r, pm_, tm_ = sim("fail-after-modules", man_ra); ok("error_after_module_stage", r.get("error", {}).get("code") == "device-error" and r["error"]["stage"] == "modules" and tm_.transferred == 0, r)
        r, pm_, tm_ = sim("modified-compatible", man_ra); ok("module_installed_before_games_that_need_it", r["state"] == "Simulated install succeeded" and "_km_retroarch_1910_ozone" in tm_.modules and r["verify"]["modules_present"], r)
    finally: planmod.MODULE_PINS.clear(); planmod.MODULE_PINS.update(saved_pins)
    r, _, _ = sim("fail-before-verify"); ok("verify_timeout_is_unverified_not_success", r["error"]["stage"] == "verify" and "unverified" not in r.get("state", "").lower() and r["state"].startswith("Potentially recoverable"))
    r, _, _ = sim("interrupted-first-time-setup"); ok("first_time_setup_profile_blocked_not_run", r["state"] == "Blocked" and any(b["code"] == "first-time-setup-required" for b in r["preflight"]["blocks"]))

    # ---- cancel only at safe checkpoints ----
    p, t = mk(man_a, "modified-compatible"); e8 = eng(p["hash"]); e8.identify(t); e8.cancel_requested = True; r8 = e8.simulate(p, t)
    ok("cancel_honored_at_safe_checkpoint", r8["state"].startswith("Cancelled") and r8["error"]["code"] == "cancelled" and t.transferred == 0)
    e9 = eng(); e9.stage = "transfer"; c9 = e9.cancel_if_safe(); ok("cancel_during_transfer_deferred", c9["accepted"] and c9["safe_now"] is False and "transfer" not in SAFE_CANCEL_STAGES)
    for s in ("memboot", "config", "link"): ok(f"stage_{s}_not_cancellable", s not in SAFE_CANCEL_STAGES)

    # ---- execute is refused twice ----
    p, t = mk(man_a, "modified-compatible"); e10 = eng(p["hash"]); rx = e10.execute(p, "ssh")
    ok("execute_refused_physical_disabled", rx["code"] == "physical-disabled" and "G1" in rx["state"])
    ok("execute_refused_for_every_transport_kind", all(eng().execute(p, k)["code"] == "physical-disabled" for k in ("ssh", "clovershell", "fel", "fake", "anything")))

    # ---- journal hygiene ----
    jfile = e1.j.path; lines = journal.Journal.read(jfile)
    ok("journal_has_stage_boundaries", {l["stage"] for l in lines} >= {"identify", "preflight", "transfer", "verify"}, sorted({l["stage"] for l in lines}))
    j2 = journal.Journal(os.path.join(tmp, "j", "scrub.jsonl"), "scrub"); rec = j2.append("x", "y", {"token": "SECRET123", "Authorization": "Bearer abc", "ok": 1, "blob": b"\x00" * 10})
    raw = open(j2.path, encoding="utf-8").read()
    ok("journal_scrubs_secrets_and_bytes", "SECRET123" not in raw and "Bearer abc" not in raw and "[redacted]" in raw and "<10 bytes>" in raw)
    j3 = journal.Journal(os.path.join(tmp, "j", "bound.jsonl"), "bound")
    for i in range(journal.MAX_LINES + 50): j3.append("s", "e", {"i": i})
    ok("journal_bounded", len(open(j3.path, encoding="utf-8").read().splitlines()) == journal.MAX_LINES + 1)

    # ---- worker protocol ----
    out = io.StringIO(); w = Worker(os.path.join(tmp, "worker"), out=out)
    def ask(obj):
        out.seek(0); out.truncate(0); w.handle(json.dumps(obj) if not isinstance(obj, str) else obj); return [json.loads(l) for l in out.getvalue().splitlines()]
    r = ask({"v": 1, "id": "c1", "cmd": "capabilities"})[-1]
    ok("worker_capabilities", r["event"] == "result" and r["result"]["capability"] == "console-sim" and r["result"]["transports"] == ["fake"] and r["result"]["tiers"]["install"] is False and len(r["result"]["profiles"]) == len(profiles.names()))
    ok("worker_rejects_bad_json", ask("{not json")[-1]["code"] == "bad-json")
    ok("worker_rejects_wrong_protocol", ask({"v": 2, "id": "x", "cmd": "capabilities"})[-1]["code"] == "bad-version")
    ok("worker_rejects_unknown_command", ask({"v": 1, "id": "x", "cmd": "flash", "args": {}})[-1]["code"] == "bad-command")
    ok("worker_rejects_oversized", ask('{"v":1,"id":"x","cmd":"capabilities","args":{"pad":"' + "a" * 1_100_000 + '"}}')[-1]["code"] == "too-large")
    ok("worker_rejects_unknown_profile", ask({"v": 1, "id": "x", "cmd": "identify", "args": {"profile": "../real-console"}})[-1]["code"] == "not-found")
    r = ask({"v": 1, "id": "p1", "cmd": "plan", "args": {"manifest": man_a, "profile": "modified-compatible"}})[-1]; h = r["result"]["plan"]["hash"] if r["event"] == "result" else ""
    ok("worker_builds_plan_by_manifest", r["event"] == "result" and len(h) == 64, r)
    ok("worker_plan_addressed_by_hash_only", ask({"v": 1, "id": "x", "cmd": "simulate", "args": {"plan_hash": "C:\\plans\\plan.json", "profile": "modified-compatible"}})[-1]["code"] == "not-found" and ask({"v": 1, "id": "x", "cmd": "simulate", "args": {"plan_hash": "0" * 64, "profile": "modified-compatible"}})[-1]["code"] == "not-found")
    evs = ask({"v": 1, "id": "s1", "cmd": "simulate", "args": {"plan_hash": h, "profile": "modified-compatible"}})
    ok("worker_simulate_streams_progress_then_result", any(e["event"] == "progress" for e in evs) and evs[-1]["event"] == "result" and evs[-1]["result"]["state"] == "Simulated install succeeded" and all(e.get("id") == "s1" for e in evs), [e["event"] for e in evs][-4:])
    r = ask({"v": 1, "id": "e1", "cmd": "execute", "args": {"plan_hash": h, "transport": "ssh", "force": True, "physical": 1, "token": "x"}})[-1]
    ok("worker_execute_refused", r["event"] == "result" and r["result"]["code"] == "physical-disabled")
    r = ask({"v": 1, "id": "v1", "cmd": "verify", "args": {"plan_hash": h, "profile": "modified-compatible"}})[-1]
    ok("worker_verify_is_independent_read_back", r["event"] == "result" and r["result"]["simulated"] is True and r["result"]["match"] is False)
    r = ask({"v": 1, "id": "r1", "cmd": "recover", "args": {"stage": "memboot"}})[-1]; ok("worker_recover_advice", "FEL" in r["result"]["advice"] and r["result"]["physical"]["install"] is False)

    shutil.rmtree(tmp, ignore_errors=True)
    for c in checks: print(c)
    fails = [c for c in checks if "=false" in c]; print(("RESULT: PASS" if not fails else "RESULT: FAIL " + str(fails)) + f" ({len(checks)} checks)"); return 1 if fails else 0

if __name__ == "__main__": sys.exit(main())
