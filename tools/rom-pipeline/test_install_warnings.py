r"""Install on Console's warnings before a collection is checked or saved to a file, and its Check Collection step, from the page's own code
(build_html.py's page script, studio.js, connect.js), run in node with a small stand-in for the page:
  names     a game added from a file and still named after it reads with its file ("active (from the file active.nes)"); a game the person
            named, and any Game Library game, by its title
  words     plain words: what is not known (whether ordinary NES controllers work), what it means for playing on the NES Classic; a game
            that needs a special controller names it; an empty collection says the built-in games are hidden, not removed
  dialog    the question is asked in Classic Cart Studio's own dialog (the shared one), never the browser's: Continue resolves true; Cancel,
            the close button or anything that closes the dialog resolves false; nothing is asked when there is nothing to warn about
  included  the Included Games carry their authors' own published controls (included/index.json "input", each fact cited): neither is
            counted as a game whose controller needs are not known; an own game with no record still is
  flow      no browser confirm() is left in the install flow, the collection-file saves or the USB driver button; the Check Collection step
            runs one check at a time, waits for it with its elapsed time shown, lets a check grow with the collection (no 12 s limit) and
            follows this computer's own checks before it offers Prepare
  python test_install_warnings.py      Exit 0 = PASS."""
from __future__ import annotations
import json, os, re, shutil, subprocess, sys
HERE = os.path.dirname(os.path.abspath(__file__))
checks = []
def ok(k, v, n=""): checks.append((k, bool(v))); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:400]})" if n and not v else ""), flush=True)
page = open(os.path.join(HERE, "build_html.py"), encoding="utf-8").read()
studio = open(os.path.join(HERE, "studio", "studio.js"), encoding="utf-8").read()
connect = open(os.path.join(HERE, "studio", "connect.js"), encoding="utf-8").read()
colls = open(os.path.join(HERE, "studio", "collections.js"), encoding="utf-8").read()
def fn(src, name):   # one top-level function of the page script, by its name, up to the next top-level line
    i = src.index("function " + name + "(")
    j = re.search(r"\n(function |const |let |var |\$\(|//)", src[i + 1:]); return src[i:i + 1 + j.start()] if j else src[i:]
JS = "\n".join(fn(page, n) for n in ("needsHardware", "gameLabel", "exportWarnings", "exportWarningsHtml", "exportWarningsAny", "ccsAskHtml", "confirmExportWarnings"))
# the Included Games' records as the page gets them (build_html._included_covers over the real site-data/included/index.json), and the page's
# own personal-game row (studio.js perRow) that a kept Included Game becomes
sys.path.insert(0, HERE); import build_html as BH
INDEX = os.path.join(HERE, "..", "..", "site-data", "included", "index.json")
with open(INDEX, encoding="utf-8") as f: INCL = json.load(f)["games"]
JS += "\nconst INCL_COVERS=" + BH._included_covers(INDEX) + ";\n" + re.search(r"^const INCL_INPUT_KEYS=.*$", studio, re.M).group(0) + "\n" + "\n".join(fn(studio, n) for n in ("prT", "prN", "inclInput", "perRow"))
HARNESS = r"""
const JS=process.argv[1]; const vm=require('vm');
const els={}; const obsList=[];
function mkEl(id){ const cls=new Set(); const e={id, innerHTML:'', textContent:'', onclick:null, classList:{add(c){ cls.add(c); obsList.forEach(o=>o.t===e&&o.f()); }, remove(c){ cls.delete(c); obsList.forEach(o=>o.t===e&&o.f()); }, contains(c){ return cls.has(c); }}}; return e; }
const $=id=>els[id]||(els[id]=mkEl(id));
function MutationObserver(f){ this.f=f; } MutationObserver.prototype.observe=function(t){ this.t=t; obsList.push(this); }; MutationObserver.prototype.disconnect=function(){ const i=obsList.indexOf(this); if(i>=0) obsList.splice(i,1); };
let asked=0;
const ctx=vm.createContext({console, Promise, MutationObserver, $, window:{}, sel:new Set(), games:[], esc:s=>String(s==null?'':s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;'),
  openColl:(t,s,b)=>{ asked++; $('collTitle').textContent=t; $('collBody').innerHTML=b; $('collm').classList.add('open'); }, closeColl:()=>{ $('collm').classList.remove('open'); }});
vm.runInContext(JS, ctx); const R=c=>vm.runInContext(c, ctx);
const own={game_id:'p_0123456789abcdef', display_title:'active', archive:'active.nes', source:'personal', input_level:'unknown', input_standard_controller_playable:'unknown'};
const named={game_id:'p_1111111111111111', display_title:'Star Route', archive:'active (v2).nes', source:'personal', input_level:'unknown', input_standard_controller_playable:'unknown'};
const zap={game_id:'aaaaaaaaaaaaaaaa', display_title:'Duck Hunt', source:'catalog', input_level:'required', input_special_accessory:'NES Zapper', input_standard_controller_playable:'false'};
const fine={game_id:'bbbbbbbbbbbbbbbb', display_title:'Thwaite', source:'catalog', input_level:'none', input_standard_controller_playable:'true'};
const incl={game_id:'p_2222222222222222', display_title:'Thwaite', archive:'thwaite.nes', source:'personal', personal:{origin:'included'}};   // an Included Game in a collection
const INCL=JSON.parse(process.argv[2]);
(async()=>{ const out={};
 ctx.window.selectedGames=ctx.selectedGames=()=>ctx.SEL; const set=a=>{ ctx.SEL=a; };
 out.labels=[R('gameLabel')(own), R('gameLabel')(named), R('gameLabel')(zap), R('gameLabel')({display_title:'', archive:'x.nes', source:'personal'}), R('gameLabel')(incl)];
 set([fine]); asked=0; out.nothing={answer:await R('confirmExportWarnings()'), asked};
 set([own, zap]); asked=0; const p=R('confirmExportWarnings()'); out.title=$('collTitle').textContent; out.html=$('collBody').innerHTML; out.open=$('collm').classList.contains('open');
 $('askYes').onclick(); out.yes=await p; out.closed_after=!$('collm').classList.contains('open');
 const p2=R('confirmExportWarnings()'); $('askNo').onclick(); out.no=await p2;
 const p3=R('confirmExportWarnings()'); $('collm').classList.remove('open'); out.closed=await p3;   // the close button, Escape: the dialog closes
 set([]); const p4=R('confirmExportWarnings()'); out.empty=$('collBody').innerHTML; $('askNo').onclick(); await p4;
 // each Included Game as a collection keeps it (its personal record, as /api/personal/included and the browser's starter make it), and an
 // own game with no record
 const rows=INCL.map(g=>R('perRow')({id:'p_'+g.content_sha256.slice(0,16), file_name:g.file_name, content_sha256:g.content_sha256, content_size:g.content_size, origin:'included', user:{title:g.title}, detected:{header_hex:g.header_hex, mapper:g.mapper}}));
 const ownRow=R('perRow')({id:'p_3333333333333333', file_name:'active.nes', content_sha256:'3'.repeat(64), content_size:786432, user:{title:'active'}, detected:{}});
 out.incl=rows.map(r=>({title:r.display_title, level:r.input_level, sc:r.input_standard_controller_playable, support:r.input_nes_classic_support, acc:r.input_special_accessory, req:r.input_accessory_requirement, ev:(r.input_evidence||[]).map(e=>e.url), needs:R('needsHardware')(r)}));
 out.own={level:ownRow.input_level, needs:R('needsHardware')(ownRow)};
 set(rows.concat([ownRow])); const p5=R('confirmExportWarnings()'); out.inclHtml=$('collBody').innerHTML; $('askNo').onclick(); await p5;
 set(rows); asked=0; out.inclOnly={answer:await R('confirmExportWarnings()'), asked};
 console.log(JSON.stringify(out)); })().catch(e=>{ console.log(JSON.stringify({error:String(e&&e.stack||e)})); process.exit(1); });
"""
node = shutil.which("node")
if not node: print("SKIPPED: node is not installed (not a pass)"); sys.exit(2)
r = subprocess.run([node, "-e", HARNESS, JS, json.dumps(INCL)], capture_output=True, text=True, encoding="utf-8", timeout=120)
out = json.loads(r.stdout.strip().splitlines()[-1]) if r.stdout.strip() else {"error": r.stderr[-800:]}
ok("harness_ran", "error" not in out, out.get("error"))
if "error" not in out:
    ok("name_from_a_file_shows_the_file", out["labels"][0] == "active (from the file active.nes)", out["labels"])
    ok("a_named_own_game_and_a_library_game_by_title", out["labels"][1:3] == ["Star Route", "Duck Hunt"], out["labels"])
    ok("an_untitled_own_game_is_never_blank", out["labels"][3] == "x (from the file x.nes)", out["labels"])
    ok("an_included_game_in_a_collection_by_its_title", out["labels"][4] == "Thwaite", out["labels"])
    ok("nothing_to_warn_nothing_asked", out["nothing"] == {"answer": True, "asked": 0}, out["nothing"])
    h = out["html"]
    ok("asked_in_the_ccs_dialog", out["open"] and out["title"] == "Before You Continue" and 'id="askYes"' in h and 'id="askNo"' in h, out["title"])
    ok("unknown_said_plainly", "Controller needs not known for 1 game" in h and "can be played with an ordinary controller, like the console's own" in h and "may not be playable on the console" in h and "NES Classic" not in h, h)
    ok("the_own_game_listed_with_its_file", "active (from the file active.nes)" in h, h)
    ok("special_controller_named", "1 game needs a special controller" in h and "Duck Hunt</span>: NES Zapper" in h and "The console cannot connect it" in h, h)
    ok("continue_true_and_closed", out["yes"] is True and out["closed_after"], out)
    ok("cancel_false", out["no"] is False, out)
    ok("closing_the_dialog_is_cancel", out["closed"] is False, out)
    ok("empty_collection_said", "This collection has no games." in out["empty"] and "they stay on the console" in out["empty"], out["empty"])
    by = {x["title"]: x for x in out["incl"]}
    ok("both_included_games_have_their_authors_controls", sorted(by) == ["Concentration Room", "Thwaite"] and all(not x["needs"] and x["sc"] == "true" and x["support"] == "supported" and x["ev"] for x in out["incl"]), out["incl"])
    ok("thwaite_plays_with_the_nes_controller_its_mouse_is_optional", by.get("Thwaite", {}).get("level") == "optional" and by["Thwaite"]["req"] == "optional" and "Super NES Mouse" in by["Thwaite"]["acc"] and by["Thwaite"]["ev"] == ["https://pineight.com/nes/"], by.get("Thwaite"))
    ok("concentration_room_needs_only_nes_controllers", by.get("Concentration Room", {}).get("level") == "none" and by["Concentration Room"]["acc"] == "" and by["Concentration Room"]["ev"] == ["https://pineight.com/croom/"], by.get("Concentration Room"))
    ok("an_own_game_without_a_record_stays_not_known", out["own"] == {"level": "unknown", "needs": True}, out["own"])
    ok("the_warning_lists_only_the_own_game", "Controller needs not known for 1 game" in out["inclHtml"] and "active (from the file active.nes)" in out["inclHtml"] and "Thwaite" not in out["inclHtml"] and "Concentration Room" not in out["inclHtml"], out["inclHtml"])
    ok("a_collection_of_the_included_games_asks_nothing", out["inclOnly"] == {"answer": True, "asked": 0}, out["inclOnly"])
# the flow itself (static: the page's own source)
cew = fn(page, "confirmExportWarnings"); drv = fn(page, "hkWireDriver")
ok("no_browser_confirm_in_the_warnings_or_the_driver_button", "confirm(" not in fn(page, "exportWarningsHtml") and "ccsAskHtml(" in cew and "confirm(" not in drv.replace("confirmExport", "") and "ccsAskHtml(" in drv)
check = studio[studio.index("if(INST.step==='check'){"):studio.index("if(INST.step==='prepare'){")]
ok("check_step_asks_in_the_ccs_dialog", "await confirmExportWarnings()" in check and "confirm(" not in check.replace("confirmExportWarnings", ""))
ok("check_step_one_check_at_a_time", "if(INST.checking) return;" in check and "tok!==INST.checkTok" in check)
ok("check_step_shows_its_wait", "id=\"hkChecking\"" in check and "setInterval" in check)
ok("check_step_follows_this_computers_checks", "checkEnvFollow(can, cf)" in check and "function checkEnvFollow" in studio)
ok("a_failed_check_says_what_failed", "The collection could not be checked." in check and "Check Again" in check)
hw = re.search(r"const HK_WAITS=/(.*)/;", connect).group(1)
ok("the_check_is_a_long_wait", "|preview|" in hw, hw[:200])
ok("saves_await_the_dialog", "await confirmExportWarnings()" in colls and "$('export').onclick=async()=>{ if(!(await confirmExportWarnings()))" in studio and "$('export').onclick=async()=>{ if(!(await confirmExportWarnings()))" in page)
bad = [k for k, v in checks if not v]
print(f"RESULT: {'PASS' if not bad else 'FAIL ' + str(bad)} ({len(checks)} checks)"); sys.exit(1 if bad else 0)
