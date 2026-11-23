r"""One storage accounting: the loadout meter (studio/storage-model.js), the installation plan and install checks
(console_engine/storage_model.py, ccs_install.py) and CCS Nucleus's capacity tool (tools/console-nucleus/capacity.py) decide the same
cases the same way, and the accounting counts what it should (the C# engine runs the same vectors: CcsBackup.Tests storage-spec).
  spec           storage_model.py carries tools/platform/ccs-storage-spec.json byte for byte; the vectors were made from this spec
  vectors        every case of tools/platform/storage-vectors.json gives exactly its recorded result in Python and in the website's script
  reserve once   a file system header's reserve is subtracted once; a df reading that already leaves it out is not reduced again
  blocks         each file takes whole blocks (small texts rounded one by one); an empty file takes none; a measured block size is used
  shared         a file counted under one collection is not counted again under another (one stored copy)
  unknown        an unknown size, an unknown allowance (SNES saves) or an unknown console model never counts as zero: unconfirmed
  peak           a replacement that fits once it is done can still not fit while both copies are present
  old/new        a new HOME beside an earlier one of another size: the earlier copy lowers only the final figure, never the peak
  meter          CCS HOME is counted only when CCS Mod is chosen; Original Menu entries only when the games become entries
  nucleus        capacity.py's figures are the shared account of its measured inputs; the rows add up to what it needs
  install        ccs_install's room check and plan figure come from the same account (nothing is charged for a copy already there)
  page           the website is built with this specification
  python test_storage_model.py      Exit 0 = PASS."""
from __future__ import annotations
import copy, hashlib, json, os, shutil, subprocess, sys, tempfile
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.join(HERE, "..", "companion")); sys.path.insert(0, os.path.join(REPO, "tools", "console-nucleus"))
from console_engine import storage_model as SM, ccs_install as CI   # noqa: E402
import capacity as CAP   # noqa: E402

PLATFORM = os.path.join(REPO, "tools", "platform")


def run_js(prog: str):
    node = shutil.which("node")
    if not node: return None
    js = open(os.path.join(HERE, "studio", "storage-model.js"), encoding="utf-8").read()
    return json.loads(subprocess.run([node, "-"], input=js + "\n" + prog, capture_output=True, text=True, encoding="utf-8", check=True).stdout)


def main():
    checks = []
    def ok(k, v, n=""): checks.append((k, bool(v))); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:400]})" if n and not v else ""))
    raw = open(os.path.join(PLATFORM, "ccs-storage-spec.json"), "rb").read()
    ok("spec_is_the_file_byte_for_byte", SM.SPEC_TEXT.encode("utf-8") == raw and SM.spec_sha256() == hashlib.sha256(raw).hexdigest())
    V = json.load(open(os.path.join(PLATFORM, "storage-vectors.json"), encoding="utf-8"))
    ok("vectors_made_from_this_spec", V["spec_sha256"] == SM.spec_sha256())
    A = {v["name"]: v for v in V["account"]}; E = {v["name"]: v for v in V["estimate"]}
    T = {v["name"]: v for v in V["retention"]}
    bad = ([n for n, v in A.items() if SM.account(copy.deepcopy(v["input"])) != v["result"]] + [n for n, v in E.items() if SM.estimate(copy.deepcopy(v["input"])) != v["result"]]
           + [n for n, v in T.items() if SM.retention(copy.deepcopy(v["input"])) != v["result"]])
    ok("python_decides_every_vector_as_recorded", not bad and len(A) >= 18 and len(E) >= 13 and len(T) >= 8, bad)
    js = run_js("const V=" + json.dumps(V) + "; const S=" + json.dumps(SM.SPEC) + ";\nprocess.stdout.write(JSON.stringify({a:V.account.map(v=>ccsStorageAccount(v.input,S)), e:V.estimate.map(v=>ccsStorageEstimate(v.input,S)),"
                " items:V.estimate.map(v=>ccsStorageCollectionItems(v.input,S))}));")
    if js is None: ok("website_decides_every_vector_as_recorded (Node.js missing: NOT run)", False)
    else:
        badj = [v["name"] for v, r in zip(V["account"], js["a"]) if r != v["result"]] + [v["name"] for v, r in zip(V["estimate"], js["e"]) if r != v["result"]]
        ok("website_decides_every_vector_as_recorded", not badj, badj)
        ok("website_builds_the_same_items_as_python", all(r == json.loads(json.dumps(SM.collection_items(v["input"]))) for v, r in zip(V["estimate"], js["items"])))
    r = lambda n: A[n]["result"]; ref = SM.reference("nes-classic-edition"); sref = SM.reference("snes-classic-edition")
    ok("references_are_dated_and_consistent", ref["date"] == "2026-10-09" and ref["available_bytes"] == ref["free_bytes"] - ref["reserved_bytes"] == 374804480 and ref["total_bytes"] == 413138944
       and sref["date"] == "2026-11-21" and sref["available_bytes"] == sref["free_bytes"] - sref["reserved_bytes"] == 282206208 and sref["allocated_bytes"] == 16637952 and sref["block_bytes"] == 4096)
    ok("reserve_counted_once", r("reserve-counted-once-superblock")["available_bytes"] == 374804480 and r("df-reading-reserve-already-out")["available_bytes"] == 50000000
       and r("nes-reference-nothing")["available_bytes"] == 374804480 and r("nes-reference-nothing")["available_source"] == "reference")
    ok("each_small_file_rounded_to_a_block", r("small-files-rounded-each")["logical_bytes"] == 10 and r("small-files-rounded-each")["allocated_bytes"] == 40960
       and r("zero-length-and-block-edges")["allocated_bytes"] == 4096 + 8192 and r("measured-block-size")["allocated_bytes"] == 2048 and r("measured-block-size")["block_source"] == "measured"
       and r("nes-reference-nothing")["block_source"] == "reference" and r("unknown-profile")["block_source"] == "default")
    sh = r("shared-asset-counted-once")
    ok("shared_asset_counted_once", sh["allocated_bytes"] == 102400 + 8192 + 8192 and sh["rows"][1]["logical_bytes"] == 7000)
    ok("unknown_size_is_not_zero", r("unknown-file-size")["status"] == "unconfirmed" and not r("unknown-file-size")["write_ok"] and r("unknown-file-size")["rows"][0]["kind"] == "unknown"
       and r("estimate-missing")["status"] == "unconfirmed" and r("removed-copy-size-unknown")["status"] == "unconfirmed")
    sn = r("snes-reference-saves-unknown")
    ok("snes_saves_allowance_unknown_blocks_a_write", sn["available_bytes"] == 282206208 and sn["saves_allowance_bytes"] is None and sn["status"] == "unconfirmed" and not sn["write_ok"]
       and "room for saves and suspend points" in sn["unknown"] and r("measured-two-game-package-on-snes-reference")["status"] == "unconfirmed")
    up = r("unknown-profile")
    ok("unknown_console_model_is_unknown", up["available_bytes"] is None and up["remaining_bytes"] is None and up["status"] == "unconfirmed" and not up["write_ok"])
    pk = r("peak-does-not-fit-final-would")
    ok("peak_refused_although_final_fits", pk["status"] == "over-at-peak" and pk["steady_bytes"] <= pk["available_bytes"] < pk["peak_bytes"] and pk["freed_bytes"] == 32768 and not pk["write_ok"])
    on = r("old-and-new-package-sizes-differ")
    ok("old_and_new_package_sizes", on["removed_bytes"] == 20004864 and on["peak_bytes"] - on["steady_bytes"] == 20004864 and on["allocated_bytes"] == 30003200)
    sg = [r("sega-pictures-removed-each-file-once-nes"), r("sega-pictures-removed-each-file-once-snes")]
    ok("removed_files_counted_once_on_both_references", all(x["removed_bytes"] == 86016 + 81920 and x["steady_bytes"] == -(86016 + 81920) and x["peak_bytes"] == 0
       and x["status"] == "fits" and x["write_ok"] and x["unknown"] == [] for x in sg) and sg[0]["available_bytes"] == 374804480 and sg[1]["available_bytes"] == 282206208)
    ok("over_by_the_exact_excess", r("over")["status"] == "over" and r("over")["remaining_bytes"] == 10000000 - r("over")["peak_bytes"] < 0)
    ok("nothing_written_needs_no_allowance", r("nes-reference-nothing")["peak_bytes"] == 0 and r("nes-reference-nothing")["status"] == "fits")
    pol = SM.SPEC["policies"]; mg = r("measured-two-game-console-package")
    ok("saves_and_margin_from_the_policy", mg["saves_allowance_bytes"] == pol["saves_allowance_bytes"]["nes-classic-edition"] == 10485760
       and mg["margin_bytes"] == pol["margin_fixed_bytes"] + mg["allocated_bytes"] * pol["margin_fraction_ppm"] // 1000000 and mg["logical_bytes"] == 23142274)
    e = lambda n: E[n]["result"]
    cats = lambda n: [x["category"] for x in e(n)["rows"]]
    ok("meter_home_only_with_ccs_mod", "home" in cats("mod-home-estimated") and "home" not in cats("mod-absent-no-home") and "player" not in cats("mod-absent-no-home") and "earlier-home" not in cats("mod-absent-no-home")
       and "notices" in cats("mod-absent-no-home") and "native-entries" in cats("mod-absent-no-home") and "native-entries" not in cats("mod-home-estimated") and "native-entries" in cats("mod-with-original-menu-entries"))
    h = SM.SPEC["estimates"]["home"]
    ok("meter_home_estimate_labeled_and_from_measured_cohorts", e("mod-home-estimated")["home"] == h["fixed_logical_bytes"] + 3 * h["per_game_logical_bytes"] and not e("mod-home-estimated")["home_measured"]
       and e("three-hundred-games-estimated")["home"] >= 26834763 and h["fixed_logical_bytes"] + 30 * h["per_game_logical_bytes"] >= 18359066 and h["fixed_logical_bytes"] + 100 * h["per_game_logical_bytes"] >= 20662092
       and h["fixed_allocated_bytes"] + 300 * h["per_game_allocated_bytes"] >= 29315072 and h["fixed_allocated_bytes"] + 30 * h["per_game_allocated_bytes"] >= 19038208
       and h["fixed_allocated_bytes"] + 100 * h["per_game_allocated_bytes"] >= 21790720)   # the measured HOME packages of 30, 100 and 300 games with the console covers
    ok("meter_measured_home_used_when_prepared", e("mod-home-measured")["home"] == 23142274 and e("mod-home-measured")["home_measured"] and [x for x in e("mod-home-measured")["rows"] if x["category"] == "home"][0]["allocated_bytes"] == 23748608)
    ok("meter_builtin_games_add_no_files", e("built-in-kept")["rom"] == 262160 and e("built-in-kept")["added"] == 1)
    ok("meter_unknown_game_size", e("game-size-unknown")["status"] == "unconfirmed" and e("game-size-unknown")["unknown_games"] == 1)
    ok("meter_hakchi_once_none_when_present", e("hakchi-present")["hakchi"] == 0 and e("mod-home-estimated")["hakchi"] == SM.SPEC["estimates"]["hakchi_base"]["logical_bytes"])
    ok("meter_measured_free_used", e("measured-free")["available"] == 20000000 and e("measured-free")["source"] == "device" and e("measured-free")["status"] == "over")
    ok("meter_snes_unconfirmed", e("snes-profile")["status"] == "unconfirmed" and e("snes-profile")["available"] == 282206208)
    # Include HOME music (selected by default): left out, CCS HOME's song is not counted, in the copy in use, the earlier version kept and
    # the next update alike (both references); the held MP3 has its own figures, counted nowhere
    hm = SM.SPEC["estimates"]["home"]; hrow = lambda n, c="home": next(x for x in e(n)["rows"] if x["category"] == c)
    ok("meter_include_home_music_left_out_drops_the_song",
       (hm["music_logical_bytes"], hm["music_allocated_bytes"], hm["music_held_logical_bytes"], hm["music_held_allocated_bytes"]) == (11907116, 11911168, 993068, 995328)
       and all(e(f"{r}-home-estimated-music-left-out")["home"] == e("mod-home-estimated")["home"] - 11907116
               and hrow(f"{r}-home-estimated-music-left-out")["allocated_bytes"] == hrow("mod-home-estimated")["allocated_bytes"] - 11911168
               and hrow(f"{r}-home-estimated-music-left-out", "earlier-home")["allocated_bytes"] == hrow("mod-home-estimated", "earlier-home")["allocated_bytes"] - 11911168
               for r in ("nes", "snes"))
       and "music" not in E["mod-home-estimated"]["input"]["home"]
       and e("nes-home-estimated-music-left-out")["available"] == 374804480 and e("snes-home-estimated-music-left-out")["available"] == 282206208)
    # Original Menu entries: the meter counts the covers this version writes; the held smaller covers have their own figures and are counted
    # only when asked for (both references)
    ne, nh = SM.SPEC["estimates"]["native_entry"], SM.SPEC["estimates"]["native_entry_held"]
    blk = lambda n: -(-n // 4096) * 4096
    ent = lambda x: blk(x["desktop_bytes"]) + blk(x["cover_bytes"]) + blk(x["cover_small_bytes"])
    nrow = lambda n: next(x for x in e(n)["rows"] if x["category"] == "native-entries")
    ok("meter_original_menu_entries_standard_by_default_held_apart",
       (ne["desktop_bytes"], ne["cover_bytes"], ne["cover_small_bytes"]) == (592, 58292, 2851) and (nh["cover_bytes"], nh["cover_small_bytes"]) == (19037, 2163)
       and all(nrow(f"{r}-original-menu-entries-standard-covers")["allocated_bytes"] == 20 * ent(ne) and nrow(f"{r}-original-menu-entries-held-palette-covers")["allocated_bytes"] == 20 * ent(nh)
               for r in ("nes", "snes"))
       and "native_covers" not in E["nes-original-menu-entries-standard-covers"]["input"] and "native_covers" not in E["mod-with-original-menu-entries"]["input"]
       and SM.collection_items(dict(E["nes-original-menu-entries-standard-covers"]["input"], native_covers="standard")) == SM.collection_items(E["nes-original-menu-entries-standard-covers"]["input"])
       and e("nes-original-menu-entries-standard-covers")["available"] == 374804480 and e("snes-original-menu-entries-standard-covers")["available"] == 282206208)

    # ---- earlier CCS HOME versions: the meter counts the versions kept and room for the next update; the retention rule keeps the most recent
    pol = SM.SPEC["policies"]; t = lambda n: T[n]["result"]
    ok("earlier_versions_default_one", pol["earlier_home_versions_default"] == 1 and e("mod-home-estimated")["earlier_versions"] == 1 and e("keep-no-earlier-version")["earlier_versions"] == 0
       and e("keep-three-earlier-versions")["earlier_versions"] == 3 and e("earlier-setting-out-of-range-is-the-default")["earlier_versions"] == 1)
    r0, r3 = e("keep-no-earlier-version"), e("keep-three-earlier-versions"); cp = r0["copy_bytes"]
    row = lambda r, c: next((x for x in r["rows"] if x["category"] == c), None)
    ok("earlier_versions_counted_as_whole_copies", row(r3, "earlier-home")["logical_bytes"] == 3 * cp and row(r0, "earlier-home") is None and row(r0, "next-update")["logical_bytes"] == cp
       and r0["peak_bytes"] - r0["steady_bytes"] == row(r0, "next-update")["allocated_bytes"])
    ok("retention_default_keeps_the_prior", t("default-one-earlier")["remove"] == ["c1"] and t("keep-none")["remove"] == ["c2", "c1"] and t("keep-two-different-sizes")["remove"] == ["z"]
       and t("zero-eligible")["remove"] == [])
    oo = t("other-program-changed-referenced-kept")
    ok("retention_never_removes_unknown_changed_referenced_current", oo["remove"] == ["s"] and oo["why"]["p"].startswith("not recorded") and oo["why"]["q"].startswith("changed")
       and oo["why"]["r"].startswith("something") and oo["why"]["n"] == "in use")
    ok("retention_unfinished_installation_removes_nothing", t("unfinished-installation-removes-nothing")["remove"] == [] and t("prior-changed-keeps-the-next-verified")["remove"] == ["m"])

    # ---- CCS Nucleus's capacity tool: its figures are the shared account of its measured inputs
    t = tempfile.mkdtemp(prefix="ccs-storage-")
    try:
        man = {"packages": {"nucleus": {"file": "ccs-nucleus.hmod", "overlay_files": 3, "menu_entry_bytes": 300, "members": {"install": {"bytes": 900}, "bin/ccs-nucleus": {"bytes": 70000}, "etc/a": {"bytes": 10}, "etc/b": {"bytes": 5000}}},
                            "nucleus-chooser": {"file": "ccs-nucleus-chooser.hmod", "overlay_files": 2, "menu_entry_bytes": 250, "members": {"bin/ccs-home": {"bytes": 900000}, "fonts/x": {"bytes": 4097}}},
                            "mod": {"file": "ccs-mod.hmod", "overlay_files": 1, "menu_entry_bytes": 280, "members": {"bin/ccs-mod": {"bytes": 20000}}}}}
        json.dump(man, open(os.path.join(t, "MANIFEST.json"), "w"))
        c = CAP.compute(t, "nucleus-chooser", 30_000_000, None, 0, [67108864], False)
        c2 = CAP.compute(t, "mod", 300_000_000, 4096, 0, [], True, [1000, 20000, 5000], 20_000_000)
        ok("nucleus_capacity_is_the_shared_account", c["account"]["result"] == SM.account(c["account"]["input"]) and c["required_bytes"] == c["account"]["result"]["peak_bytes"]
           and c["required_bytes"] == sum(x["bytes"] for x in c["rows"]) and c2["required_bytes"] == sum(x["bytes"] for x in c2["rows"]))
        ok("nucleus_capacity_block_size_labeled", c["block_source"] == "reference" and c2["block_source"] == "measured")
        ok("nucleus_capacity_old_copy_not_charged_again", any("already counted" in x["what"] and "20.0 MB" in x["what"] and x["bytes"] == 0 for x in c2["rows"]) and c2["fits"])
        ok("nucleus_capacity_shortfall_shown", c["fits"] is False and c["status"] == "over" and c["shortfall_bytes"] == c["required_bytes"] - 30_000_000)
        c3 = CAP.compute(t, "nucleus", 300_000_000, 4096, profile="snes-classic-edition")
        ok("nucleus_capacity_snes_unconfirmed", c3["status"] == "unconfirmed" and not c3["fits"])
        if js is not None:
            j = run_js("const S=" + json.dumps(SM.SPEC) + "; const I=" + json.dumps([c["account"]["input"], c2["account"]["input"]]) + "; process.stdout.write(JSON.stringify(I.map(i=>ccsStorageAccount(i,S))));")
            ok("nucleus_capacity_same_in_website_script", j == [c["account"]["result"], c2["account"]["result"]])
    finally: shutil.rmtree(t, ignore_errors=True)

    # ---- the installer's room: plan figure and console check from the same account
    plan = {"delivery": "home", "files": [{"size": s} for s in (1000, 5000, 4096, 0)], "packages": {"mod": {"label": "CCS Mod", "overlay_bytes": 20100, "overlay_sizes": [100, 20000]}},
            "hakchi": {"publish": True, "bytes": 123456}}
    it = CI.space_items(plan, ["mod"], True, False); it_same = CI.space_items(plan, [], False, True)
    ra = CI.space_account(plan, ["mod"], True, False, {"source": "measured", "available_bytes": 30_000_000})
    ok("install_items_exact_files_and_labeled_estimate", [x["category"] for x in it] == ["home", "addon", "hakchi-games"] and it[2]["estimate"]["allocated_bytes"] == 123456 and not it_same
       and ra == SM.account({"profile": "nes-classic-edition", "storage": {"source": "measured", "available_bytes": 30_000_000}, "items": it}) and ra["allocated_bytes"] == 4096 + 8192 + 4096 + 0 + 4096 + 20480 + 123456)
    ok("install_nothing_to_copy_needs_nothing", CI.space_account(plan, [], False, True, {"source": "measured", "available_bytes": 1})["write_ok"])
    gplan = dict(plan, delivery="games", hakchi={"publish": False})
    ok("install_without_ccs_mod_charges_no_home", [x["category"] for x in CI.space_items(gplan, [], False, False)] == ["game-files"])
    ok("install_refusal_words", "not enough room" in CI.space_refusal(CI.space_account(plan, ["mod"], True, False, {"source": "measured", "available_bytes": 1000}))
       and "cannot be confirmed" in CI.space_refusal(SM.account({"profile": "md-mini", "storage": None, "items": it})))

    # ---- the meter's page script (console-fit.js) with stand-in page data: CCS Mod chosen or not, a prepared HOME package, the dialog
    node = shutil.which("node")
    if node is None: ok("meter_page_script (Node.js missing: NOT run)", False)
    else:
        fit = open(os.path.join(HERE, "studio", "console-fit.js"), encoding="utf-8").read(); sm = open(os.path.join(HERE, "studio", "storage-model.js"), encoding="utf-8").read()
        stub = ("const STORAGE_MODEL=" + json.dumps(SM.SPEC) + ";\nlet HTML='';\nconst sel=new Set(['a','b','c']);\nconst byId={a:{payload_size:262144,display_title:'A'},b:{payload_size:40960,display_title:'B'},c:{payload_size:0,display_title:'C'}};\n"
                "const installRequired=g=>true; const csMissing=id=>({size:0,title:id}); const esc=x=>String(x); const COLL={name:'Test'}; const CS={active:'x'};\n"
                "const openColl=(t,s,h)=>{HTML=h;}; const $=id=>({}); const closeColl=()=>{}; const toast=()=>{}; const nhFp=()=>'fp';\nlet NHP=undefined; let CONSOLE={addons:null,hakchi:null}; const ADDON_DEFAULT={mod:true}; let CO={info:null};\n")
        prog = sm + "\n" + stub + fit + ("\nconst out={}; out.def=cfEstimate(); CONSOLE.addons={mod:false}; out.nomod=cfEstimate(); CONSOLE.addons={mod:true}; CONSOLE.hakchi=true; out.native=cfEstimate(); CONSOLE.hakchi=null;\n"
                "NHP={fp:'fp', result:{ok:true, manifest:{sizes:{total_with_manifest:23142274, allocated:{block_bytes:4096, bytes:23748608}}}}}; out.meas=cfEstimate(); cfDetails(false); out.html=HTML; out.text=cfStatusText(out.def);\n"
                "CO.info={earlier_home_versions:3}; out.wb=cfEstimate(); CONSOLE.plan={earlier_home_versions:0}; out.plan=cfEstimate(); CONSOLE.plan=null; CO.info=null;\n"
                "process.stdout.write(JSON.stringify({def:out.def.rows.map(r=>r.category), nomod:out.nomod.rows.map(r=>r.category), native:out.native.rows.map(r=>r.category), meas:out.meas.home, measAl:out.meas.rows.find(r=>r.category==='home').allocated_bytes,"
                " unk:out.def.unknown_games, status:out.def.status, text:out.text, html:out.html.length, hasSaves:out.html.includes('Room kept for saves')&&out.html.includes('Earlier CCS HOME versions kept')&&out.html.includes('next update'), hasDate:out.html.includes('October 9, 2026'), retro:out.html.includes('RetroArch'), def_earlier:out.def.earlier_versions, wb_earlier:out.wb.earlier_versions, plan_earlier:out.plan.earlier_versions}));")
        mj = json.loads(subprocess.run([node, "-"], input=prog, capture_output=True, text=True, encoding="utf-8", check=True).stdout)
        ok("meter_page_counts_home_only_with_ccs_mod", mj["def"] == ["game-files", "home", "player", "hakchi", "earlier-home", "next-update"] and mj["nomod"] == ["game-files", "native-entries", "notices", "hakchi", "next-update"]
           and mj["native"] == ["game-files", "native-entries", "home", "player", "hakchi", "earlier-home", "next-update"], mj)
        ok("meter_page_uses_the_prepared_package_and_its_blocks", mj["meas"] == 23142274 and mj["measAl"] == 23748608, mj)
        ok("meter_page_uses_the_workbench_setting", mj["def_earlier"] == 1 and mj["wb_earlier"] == 3 and mj["plan_earlier"] == 0, mj)   # the setting the app's status reports; a plan's own first
        ok("meter_page_unknown_size_and_dialog", mj["unk"] == 1 and mj["status"] == "unconfirmed" and "1 game without a known size" in mj["text"] and mj["html"] > 1000 and mj["hasSaves"] and mj["hasDate"] and not mj["retro"], mj)

    # ---- Include HOME music on the install page: selected by default, sent with the HOME the backend prepares, and a change makes a prepared
    # package out of date (prepared again)
    ins = open(os.path.join(HERE, "studio", "install.js"), encoding="utf-8").read()
    ok("install_page_include_home_music_default_selected_and_sent", "music:true" in ins and "> Include HOME music</label>" in ins and "include_music:NHP.music!==false" in ins
       and "(NHP.music===false?'|no-music':'')" in ins and "NHP.music=mu.checked" in ins)

    # ---- the website is built with this specification
    import importlib.util
    spec = importlib.util.spec_from_file_location("bh_storage", os.path.join(HERE, "build_html.py"))
    src = open(os.path.join(HERE, "build_html.py"), encoding="utf-8").read()
    ok("page_built_with_this_spec", "const STORAGE_MODEL=__STORAGE_MODEL__;" in src and ".replace(\"__STORAGE_MODEL__\", json.dumps(_storage_model()))" in src and "return m.SPEC" in src)
    bad = [k for k, v in checks if not v]; print("RESULT:", "PASS" if not bad else "FAIL " + json.dumps(bad), f"({len(checks)} checks)"); return 0 if not bad else 1


if __name__ == "__main__": sys.exit(main())
