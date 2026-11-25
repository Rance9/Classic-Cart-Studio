// Install on NES Classic: the console steps, one flow for Classic Cart Studio's own HOME on the console (K3; docs/console-install-
// architecture.md sections 13 and 14). Prepare the exact console package of this collection and its HOME Settings; review the
// immutable plan (what would be added, changed and kept, and the room it needs) against a simulated console; rehearse every operation on a
// disposable stand-in through the same plan, worker and installation engine a console would use; see each real-console operation and what it
// needs. Desktop app only: the public website keeps the Continue in Desktop App invitation. The operations on a real console (check, install,
// update, verify, removal, recovery, the saves copy) run through the same dispatcher and engine as the rehearsal, each on the console that was
// checked, with a verified backup of it and a confirmation for exactly that operation; a rehearsal is always labeled as one.
const CONSOLE={tok:0, status:null, profile:'hakchi-ready', plan:null, planErr:null, preflight:null, scenario:'first-install', then:[], run:null, cursor:0, events:[], result:null, timer:null, addons:null, cfg:1, prefilled:false, addonsNow:null, nowText:'', keepNote:null, startReplaced:false, nucTouched:false, hakchi:null};
// the add-ons (CCS Mod: CCS HOME and its tile in Original Menu; CCS Nucleus: what opens at power-on, straight away or with its startup
// chooser) are chosen on two pages before the review. They start from what the console has (the check's addons_now) and this app never
// removes an add-on: removing one is in CCS Console Workbench's Console Restore. The choice is part of the plan, so the confirmation covers it
const ADDON_DEFAULT={mod:true, nucleus:'none', start:'native', auto:'menu'};
const MENU_NAME={native:'Original Menu', 'ccs-home':'CCS HOME'};
const ADDON_LABEL={'ccs-mod':'CCS Mod', 'ccs-nucleus':'CCS Nucleus', 'ccs-nucleus-chooser':'CCS Nucleus’s startup chooser'};
function addonReset(){ Object.assign(CONSOLE,{addons:Object.assign({},ADDON_DEFAULT), cfg:1, prefilled:false, addonsNow:null, nowText:'', keepNote:null, startReplaced:false, nucTouched:false, hakchi:null, plan:null, preflight:null}); }
// the collection's games as ordinary Hakchi games in Original Menu: not an add-on but how this collection is delivered. null follows the plan's
// default (on without CCS Mod); a choice made on the review stays. Without CCS Mod they are the only way the collection reaches the console:
// only the game files they need are copied, never CCS HOME (the plan refuses a choice with neither)
function hakchiOn(p){ return CONSOLE.hakchi!==null?CONSOLE.hakchi:(p&&p.hakchi?!!p.hakchi.publish:!CONSOLE.addons.mod); }
// earlier CCS HOME versions on the console after this installation: the copy in use, the earlier versions the setting keeps, the ones that
// go once the new copy is in use and read back (each checked file by file first). The setting is CCS Console Workbench's.
// the earlier CCS HOME versions in the review, in four groups: the copy in use now, the update (sent first beside it and checked before
// the switch), the copies kept as earlier versions afterwards and the ones removed then; each with its name, games, date and size
function earlierRow(c){ const nm=c.label?esc(c.label):'a copy whose name is not known';
 const t=typeof c.installed_utc==='string'&&/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$/.test(c.installed_utc)?new Date(c.installed_utc):null;
 const when=t?'installed '+esc(t.toLocaleString('en-US',{month:'long',day:'numeric',year:'numeric',hour:'numeric',minute:'2-digit'})):'date not known';
 return `${nm}${c.games!=null?` · ${c.games} game${c.games===1?'':'s'}`:''} · ${when} · ${c.bytes!=null?fmtB(c.bytes)+(c.allocated_bytes!=null?` (${fmtB(c.allocated_bytes)} on the storage)`:' (space on the storage not known)'):'size not known'}`; }
function earlierHtml(e){ if(!e||!Array.isArray(e.copies)||!e.copies.length) return ''; const rm=new Set(e.remove||[]), why=e.why||{};
 const now=e.copies.find(c=>c.in_use_now), nu=e.copies.find(c=>c.in_use), rest=e.copies.filter(c=>!c.in_use);
 if(!now&&!rest.length) return `<div class="small" style="margin-top:6px"><b>Earlier HOME versions on this console</b>: this console has no CCS HOME yet, so there is no earlier version to keep or remove. (Keep ${e.keep===0?'none':e.keep===1?'one':e.keep}; change it in CCS Console Workbench: Settings &gt; CCS HOME &gt; Earlier HOME versions.)</div>`;
 const grp=(t,rows)=>`<div style="margin-top:4px"><b>${t}</b><ul>${(rows.length?rows:['None']).map(r=>`<li>${r}</li>`).join('')}</ul></div>`;
 const kept=rest.filter(c=>!rm.has(c.copy)).map(c=>(c.in_use_now?`The copy in use now (${c.label?esc(c.label):'name not known'})`:earlierRow(c))+(why[c.copy]&&why[c.copy]!=='an earlier version kept'&&why[c.copy]!=='in use'?` · kept: ${esc(why[c.copy])}`:''));
 const gone=rest.filter(c=>rm.has(c.copy)).map(c=>c.in_use_now?`The copy in use now (${c.label?esc(c.label):'name not known'}), after the switch`:earlierRow(c));
 return `<div class="small" style="margin-top:6px"><b>Earlier HOME versions on this console</b> (keep ${e.keep===0?'none':e.keep===1?'one':e.keep}; change it in CCS Console Workbench: Settings &gt; CCS HOME &gt; Earlier HOME versions). Your backups on this computer are kept separately.
  ${grp('In use now',[now?earlierRow(now):'No CCS HOME yet'])}
  ${grp('The update: sent first, beside the copy in use, and checked before the switch',[nu?(nu.new?'The new CCS HOME copy; in use after the switch':earlierRow(nu)+' · in use after the switch'):'The new CCS HOME copy'])}
  ${grp('Kept on the console as earlier versions after the update',kept)}
  ${grp('Removed after the new copy is in use and read back',gone)}</div>`; }
function planBody(){ return Object.assign({},CONSOLE.addons,CONSOLE.hakchi===null?{}:{hakchi_games:CONSOLE.hakchi}); }
function hakchiBoxHtml(p){ const on=hakchiOn(p), mod=!!CONSOLE.addons.mod, rv=p&&p.hakchi&&p.hakchi.review, left=(rv&&rv.left_out)||[];
 const notes=((p&&p.notes)||[]).filter(n=>!/cannot be added as Hakchi games:/.test(n));
 return `<div class="msg"><b>This collection’s games in Original Menu</b>
  <div style="margin-top:4px"><label style="display:inline-flex;gap:8px;align-items:center"><input type="checkbox" id="cHakchi" ${on?'checked':''}> Also add this collection’s games to Original Menu as Hakchi games</label></div>
  <div class="small">${mod?'They open from Original Menu without CCS HOME. CCS HOME has every game of the collection either way.':'Without CCS Mod, this is how the collection gets to your console: only the game files these games need are copied, never CCS HOME.'}</div>
  ${p?`<div style="margin-top:4px">${esc(p.hakchi_text||'')}</div>`:''}
  ${on&&notes.length?`<ul class="small">${notes.map(n=>`<li>${esc(n)}</li>`).join('')}</ul>`:''}
  ${on&&left.length?`<details class="small"><summary>Not added to Original Menu (${left.length})</summary><ul>${left.map(g=>`<li><span data-no-terms>${esc(g.title)}</span>: ${esc(g.why)}</li>`).join('')}</ul></details>`:''}</div>`; }
function wireHakchiBox(draw){ const h=$('cHakchi'); if(h) h.onchange=async()=>{ CONSOLE.hakchi=!!h.checked; CONSOLE.plan=null; CONSOLE.preflight=null; await draw(); }; }
addonReset();
// the review's plain summary of the add-ons after the change (a) beside what the console has (n)
function addonSummary(a,n){ n=n||{}; const rows=[['CCS HOME', a.mod?(n.mod?'Keep, with CCS Mod':'Add with CCS Mod'):'Not selected']];
 if(a.mod) rows.push(['Original Menu','Keep, with the CCS HOME tile']);
 if(a.nucleus==='none') rows.push(['Startup','Keep current startup']);
 else { const m=MENU_NAME[a.start]||a.start; rows.push(['Startup','CCS Nucleus — '+(a.nucleus==='direct'?'open '+m+' immediately':'show the startup chooser')]);
  rows.push(a.nucleus==='direct'?['Startup chooser','Off']:['If you do nothing', a.auto==='wait'?'Nothing starts until you choose':a.auto==='last'?'The menu used last starts after 10 seconds ('+m+' until you have used one)':m+' starts after 10 seconds']); }
 return `<dl class="tvkv">${rows.map(r=>`<dt>${esc(r[0])}</dt><dd>${esc(r[1])}</dd>`).join('')}</dl>`; }
function addonsPageHtml(){ const a=CONSOLE.addons, inst=!!(CONSOLE.addonsNow||{}).mod;
 return `<h3 style="margin:8px 0 2px">Choose your add-ons</h3><div class="small">Choose what you want to add. Your console's startup is on the next page.</div>
  <div class="msg" style="border:2px solid #e4002b;margin-top:8px"><label style="display:inline-flex;gap:8px;align-items:center"><input type="checkbox" id="cMod" ${a.mod?'checked':''} ${inst?'disabled':''}> <b>CCS Mod — CCS HOME</b></label>
   <div style="margin-top:4px"><b>A menu for your game collections</b></div><div>Browse your games in four themes, arrange folders, and choose your own pictures and colors. Adds a CCS HOME tile to Original Menu. What opens at power-on stays unchanged unless you choose CCS Nucleus on the next page.</div>
   <div style="margin-top:4px"><b>Opens from:</b> Original Menu. CCS Nucleus can open it too.</div><div><b>By <a href="https://Rance9.com" target="_blank" rel="noopener">Rance9</a> · Included with Classic Cart Studio</b></div>
   ${inst?'<div class="small">Installed on this console. It stays: removing it is in CCS Console Workbench’s Console Restore.</div>':''}
   <div class="acts" style="margin-top:6px"><button class="btn sm secondary" id="cSeeHome">See CCS HOME</button></div></div>
  <div class="acts"><span class="sp"></span><button class="btn hk" id="cNextStart">Next: Startup</button></div>`; }
function startupPageHtml(){ const a=CONSOLE.addons, n=CONSOLE.addonsNow||{}; const nucInst=!!n.nucleus&&n.nucleus!=='none', choInst=n.nucleus==='chooser';
 if(!a.mod&&a.start==='ccs-home'){ a.start='native'; CONSOLE.startReplaced=true; } else if(a.mod&&CONSOLE.startReplaced){ a.start='ccs-home'; CONSOLE.startReplaced=false; }
 const menus=['native'].concat(a.mod?['ccs-home']:[]); const opt=(v,sel)=>`<option value="${v}" ${v===sel?'selected':''}>${esc(MENU_NAME[v]||v)}</option>`;
 const on=a.nucleus!=='none', m=MENU_NAME[a.start]||a.start;
 const autoText=a.auto==='wait'?'Nothing starts until you choose.':a.auto==='last'?`If you do nothing, the menu you used last starts in 10 seconds. Until you have used one, that is ${m}.`:`If you do nothing, ${m} starts in 10 seconds.`;
 return `<h3 style="margin:8px 0 2px">What should open when you turn on the console?</h3>
  <div class="msg"><label style="display:inline-flex;gap:8px;align-items:center"><input type="checkbox" id="cNuc" ${on?'checked':''} ${nucInst?'disabled':''}> <b>Choose what opens first with CCS Nucleus</b></label>
   <div class="small">CCS Nucleus can open a menu straight away or show a startup chooser. It can be used with or without CCS HOME.</div>
   ${nucInst?'<div class="small">CCS Nucleus is installed on this console and stays: removing it is in CCS Console Workbench’s Console Restore.</div>':''}
   ${CONSOLE.startReplaced?'<div class="msg">CCS HOME is not chosen, so Original Menu opens first instead. To open CCS HOME, go back and choose CCS Mod.</div>':''}
   ${on?`<div style="margin-top:6px"><b>When the console turns on</b></div>
    <div><label><input type="radio" name="cNucMode" value="direct" ${a.nucleus==='direct'?'checked':''} ${choInst?'disabled':''}> Open a menu straight away</label></div>
    <div style="margin-left:24px">Open first: <select id="cFirst" ${a.nucleus!=='direct'?'disabled':''}>${menus.map(v=>opt(v,a.start)).join('')}</select><div class="small">This menu opens immediately. The startup chooser is off.</div>${choInst?'<div class="small">The startup chooser is installed on this console and stays: turning it off is in CCS Console Workbench’s Console Restore.</div>':''}</div>
    <div><label><input type="radio" name="cNucMode" value="chooser" ${a.nucleus==='chooser'?'checked':''}> Show the startup chooser</label></div>
    <div style="margin-left:24px">Start automatically with: <select id="cAuto" ${a.nucleus!=='chooser'?'disabled':''}>${menus.map(v=>opt(v,a.auto==='menu'?a.start:'')).join('')}<option value="last" ${a.auto==='last'?'selected':''}>Last menu used</option><option value="wait" ${a.auto==='wait'?'selected':''}>Wait for me to choose</option></select><div class="small">${esc(autoText)}</div></div>`
   :`<div style="margin-top:6px"><b>Keep the console's current startup.</b></div>${CONSOLE.nowText?`<div class="small">${esc(CONSOLE.nowText)}</div>`:''}`}</div>
  <div class="acts"><button class="btn secondary" id="cBackAddons">Back: Add-ons</button><span class="sp"></span><button class="btn hk" id="cReview">Review Changes</button></div>`; }
function wireAddonPages(draw){ const a=CONSOLE.addons;
 const md=$('cMod'); if(md) md.onchange=()=>{ a.mod=md.checked; };
 const sh=$('cSeeHome'); if(sh) sh.onclick=()=>{ try{ openTv(); }catch(e){} };
 const nx=$('cNextStart'); if(nx) nx.onclick=()=>{ CONSOLE.cfg=2; draw(); };
 const nu=$('cNuc'); if(nu) nu.onchange=()=>{ if(nu.checked){ a.nucleus='direct'; if(!CONSOLE.nucTouched) a.start=a.mod?'ccs-home':'native'; } else a.nucleus='none'; CONSOLE.nucTouched=true; draw(); };
 document.querySelectorAll('input[name="cNucMode"]').forEach(r=>{ r.onchange=()=>{ a.nucleus=r.value; draw(); }; });
 const fi=$('cFirst'); if(fi) fi.onchange=()=>{ a.start=fi.value; CONSOLE.startReplaced=false; draw(); };
 const au=$('cAuto'); if(au) au.onchange=()=>{ const v=au.value; if(v==='last'||v==='wait') a.auto=v; else { a.auto='menu'; a.start=v; CONSOLE.startReplaced=false; } draw(); };
 const bk=$('cBackAddons'); if(bk) bk.onclick=()=>{ CONSOLE.cfg=1; draw(); };
 const rv=$('cReview'); if(rv) rv.onclick=()=>{ CONSOLE.cfg=0; CONSOLE.plan=null; CONSOLE.preflight=null; draw(); }; }
// a console whose Hakchi setup is not Hakchi2 CE 3.9.3 (ccs_install.hakchi_required): the notice, the official release page in a new tab
// (the Windows app opens it in the default browser) and what was found under Technical details; any other text as it came
const HAKCHI_MARK='[hakchi-3.9.3-required]', HAKCHI_URL='https://github.com/TeamShinkansen/Hakchi2-CE/releases/tag/v3.9.3';
function hakchiNotice(t){ t=String(t||''); if(!t.startsWith(HAKCHI_MARK)) return esc(t); const l=t.split('\n'); const det=(l.find(x=>x.startsWith('Detected: '))||'').slice(10);
 return `<b>${esc(l[1]||'')}</b><br>${esc(l[2]||'')}<br>${esc(l[3]||'')}<br><a class="btn" href="${HAKCHI_URL}" target="_blank" rel="noopener">Get Hakchi2 CE 3.9.3</a>`
  +`<details class="small"><summary>Technical details</summary>What the console reports: ${esc(det||'not reported')}. Classic Cart Studio needs Hakchi2 CE 3.9.3's console components: hakchi v1.0.4-126 with kernel 3.4.113.29-madmonkey. After you update the console with Hakchi2 CE, check the console again: it is read again before anything is written. Hakchi2 CE's own update is not covered by this app's backup and checks.</details>`; }
const CSTEP={inspect:'Console State', review:'Review Changes', install:'Install', verify:'Verify', ready:'Complete'};
function consoleCapable(){ return !!(COMPANION.ok&&COMPANION.desktop&&(COMPANION.capabilities||[]).includes('console-sim')); }
async function consoleStatus(){ const r=await capi('/api/console/home/status'); if(r.ok){ CONSOLE.status=r; if(!(r.profiles||[]).some(p=>p.name===CONSOLE.profile)) CONSOLE.profile='hakchi-ready'; } return !!r.ok; }
const GATE_TEXT='Turned off in this build';
// progress labels for the installation engine's stages (console_engine/ccs_install.py) and the HOME package build (native_home/package.py); ids stay internal
const STAGE_LABEL={preflight:'Checking the console', stage:'Copying the HOME', games:'Copying the game files', hakchi:'Adding the games to Original Menu', readback:'Reading back every file', switch:'Switching to the new HOME', module:'Adding CCS HOME to the console’s menu', start:'Setting CCS HOME at power-on', verify:'Checking the result', done:'Done',
 recover:'Recovering the interrupted attempt', uninstall:'Rehearsing removal', export:'Rehearsing the copy of the saves', transfer:'Copying the HOME', cancelled:'Canceled', journal:'Starting', configuration:'Reading Game Menu Setup', assemble:'Building the HOME menu'};
const STEP_TEXT={themes:'theme artwork', fonts:'fonts', runtime:'menu program', manifest:'package contents'};
function stLabel(s){ return STAGE_LABEL[s]||'Working'; }
function lv(l){ return `<span class="lv ${l}">${l}</span>`; }
function fmtB(n){ return n==null?'unknown':fmt(n); }   // decimal, from the exact byte count (it divided by 1,024 under decimal labels before)
function simBadge(){ return '<span class="simtag" title="A simulated console, not your own console">SIMULATION</span>'; }
// the checked console's model (its own software's board and region, read by the check), for the words that depend on it
function physModel(){ const c=((CONSOLE.status||{}).console||{}).checked; return (c&&c.identity&&c.identity.model)||null; }
function stockGames(m){ return (m||physModel())==='snes-classic-edition'?21:30; }   // m: the model the review was checked against (a simulated console's, when it is one)
function stopPoll(){ clearInterval(CONSOLE.timer); CONSOLE.timer=null; }
function profileSelect(){ const ps=(CONSOLE.status&&CONSOLE.status.profiles)||[];
 return `<div class="msg sim"><b>Simulated console for the review and the rehearsal</b> ${simBadge()}<br><span class="small">The review below and the rehearsal use the simulated console you choose here; they never touch your own console. Your own console is checked under On your console.</span><br><select id="cProfile" style="margin-top:6px;max-width:100%">${ps.map(p=>`<option value="${esc(p.name)}" ${p.name===CONSOLE.profile?'selected':''}>${esc(p.label)}</option>`).join('')}</select></div>`; }
function wireProfile(onchange){ const s=$('cProfile'); if(!s) return; s.onchange=()=>{ CONSOLE.profile=s.value; CONSOLE.preflight=null; CONSOLE.result=null; onchange(); }; }
function opsHtml(){ const ops=(CONSOLE.status&&CONSOLE.status.operations)||[];
 return `<div class="msg"><b>What this app can do with a console</b><ul>${ops.map(o=>`<li><b>${esc(o.label)}</b> ${!o.available?`<span class="lv unknown">${esc(GATE_TEXT)}</span>`:o.tier==='simulate'?simBadge():o.tier==='workbench'?'<span class="small">(in CCS Console Workbench)</span>':''}<br><span class="small">${esc(o.text)}</span></li>`).join('')}</ul></div>`; }
function when(iso){ try{ return new Date(iso).toLocaleString(); }catch(e){ return iso||''; } }
function recordsHtml(){ const r=(CONSOLE.status&&CONSOLE.status.records)||{}; const lp=r.last_plan&&r.last_plan.plan; const op=(r.operations||[])[0];
 if(!lp&&!op) return '';
 return `<div class="msg"><b>Last review and rehearsal</b><dl class="tvkv">${lp?`<dt>Plan</dt><dd><code>${esc((lp.hash||'').slice(0,16))}</code> · <span data-no-terms>${esc(lp.label||'')}</span> · ${r.last_plan_current?'matches the prepared package':'<b>out of date</b>: the package was prepared again since; review the plan again'}</dd>`:''}
  ${op?`<dt>${op.kind==='rehearsal'||!op.kind?'Rehearsal':'Last console operation'}</dt><dd>${esc(OUTCOME[op.outcome]||op.outcome||'')} ${op.simulated?simBadge():''} · ${esc(op.kind==='rehearsal'||!op.kind?SCENARIO_NAME(op.scenario):op.kind)} · ${esc(when(op.ended||op.started))}</dd>`:''}</dl></div>`; }
const OUTCOME={installed:'Installed and checked', 'cleanup-stopped':'Installed and checked; the clean-up stopped', 'no-change':'Nothing needed to change', 'incomplete-install':'Game files copied, not yet in Original Menu', refused:'Refused before any change', stopped:'Stopped and undone', cancelled:'Canceled and undone', 'rolled-back':'Undone and checked', 'rollback-incomplete':'Undo incomplete', unknown:'Unknown',
 uninstalled:'Removed and checked', incomplete:'Not complete', complete:'Every file copied and checked', partial:'Copied, with files left out', empty:'No saves there', failed:'Failed'};
function SCENARIO_NAME(s){ return ((CONSOLE.status&&CONSOLE.status.scenarios)||{})[s]||s||''; }
function dataText(d){ return d.state==='kept'?'kept as they are':d.state==='kept-unidentified'?'kept as they are':'none there yet'; }
function planHtml(p,pf){ if(!p) return '';
 const games=p.delivery==='games', go=p.games_only||{}, hk=(pf&&pf.ok&&pf.hakchi)||{};
 const st=p.startup||{}; const sn=st.choice==='random'?'Random':/^custom-/.test(st.choice||'')?`Your picture${st.custom&&st.custom.name?' ('+st.custom.name+')':''}`:((typeof HOME_STARTUP!=='undefined'&&HOME_STARTUP.find(s=>s.id===st.choice))||{}).name||st.choice||'';
 const keep=(pf&&pf.ok&&pf.kept_unknown)||[]; const same=!!(pf&&pf.ok&&pf.same_copy); const now=pf&&pf.ok?pf.addons_now:CONSOLE.addonsNow;
 return `<div class="msg"><b>Your add-ons</b>${addonSummary(p.addons||CONSOLE.addons, now)}${CONSOLE.keepNote?`<div class="small">${esc(CONSOLE.keepNote)} The add-ons it has are kept.</div>`:''}<div class="acts" style="margin-top:4px"><span class="sp"></span><button class="btn sm secondary" id="cChangeAddons">Change Add-ons</button></div></div>
  ${hakchiBoxHtml(p)}
  <div class="sum"><div class="sb"><b>${p.playable==null?'?':p.playable}</b><span>${games?'Hakchi games':'games to play'}</span></div><div class="sb"><b>${p.file_count}</b><span>files</span></div><div class="sb"><b>${fmtB(p.bytes)}</b><span>to copy</span></div><div class="sb"><b>${fmtB(p.needs_free_bytes)}</b><span>room needed</span></div></div>
  <div class="diff"><div class="d"><b>Added</b><ul>${games?(same||!p.file_count?'<li>No game files to copy: they are already on the console, or travel inside their Hakchi entries</li>':`<li>This collection’s game files for its Hakchi games (${go.game_files||0} game file${go.game_files===1?'':'s'}, ${fmtB(go.rom_bytes||0)}) and their license notices, in a folder of their own: ${fmtB(p.bytes)} in all. No CCS HOME.</li>`)
     :same?'<li>Nothing new: this exact HOME is already on the console</li>':`<li>This HOME as a new copy (${p.file_count} files, ${fmtB(p.bytes)}), beside the one in use</li><li>Startup screen: ${esc(sn)}</li>`}${hk.action==='publish'?`<li>This collection’s games in Original Menu, as Hakchi games</li>`:''}${(pf&&pf.ok?(pf.add||[]):[]).map(x=>`<li>${esc(ADDON_LABEL[x]||x)}, through hakchi’s own package install</li>`).join('')}</ul></div>
   <div class="d"><b>Changed</b><ul>${(pf&&pf.ok?(pf.update||[]):[]).map(x=>`<li>${esc(ADDON_LABEL[x]||x)} updated to this version, through hakchi’s own package install</li>`).join('')}${pf&&pf.ok&&pf.start&&pf.start.change?`<li>${esc(pf.start.text||p.start_text||'')}</li>`:''}${hk.action==='unpublish'?'<li>The Hakchi games Classic Cart Studio added to Original Menu earlier are removed</li>':''}${same?'':games?'<li>Game files copied earlier for these Hakchi games are removed once the games use the new ones, and only their own unchanged files.</li>':'<li>An earlier copy of this HOME is removed only after the new one is checked, and only its own unchanged files.</li>'}</ul></div>
   <div class="d"><b>Kept as is</b><ul><li>The ${stockGames((pf&&pf.device&&pf.device.model)||(/^snes/.test(CONSOLE.profile)?'snes-classic-edition':''))} built-in games and their saves</li><li>Other hakchi modules</li>${pf&&pf.ok&&pf.home_kept?'<li>CCS HOME’s files already on this console</li>':''}${now&&now.nucleus&&now.nucleus!=='none'?'<li>Your own system images and CCS Nucleus’s other settings</li>':''}<li>Saves, slots and settings made on the console${pf&&pf.ok&&pf.data?` (${esc(dataText(pf.data))})`:''}</li>${keep.map(k=>`<li><code>${esc(k)}</code> (not made by this app)</li>`).join('')}</ul></div></div>
  ${pf?(pf.ok?`<div class="msg ok"><b>Check against the simulated console: ready</b> ${simBadge()}<br>${pf.already?(games?'These games are already there: nothing would change.':'This exact HOME is already there: nothing would change.'):`${fmtB(pf.free_bytes)} free, ${fmtB(pf.needs_bytes)} needed.`} ${pf.changes_text?esc(pf.changes_text):''}${earlierHtml(pf.earlier)}</div>`
    :`<div class="msg bad"><b>Check against the simulated console: blocked</b> ${simBadge()}<br>${hakchiNotice(pf.why||pf.error||'')}<br><span class="small">Nothing would be changed.</span></div>`):''}
  <details class="small"><summary>Never Written</summary><ul>${(p.never_written||[]).map(x=>`<li>${esc(x)}</li>`).join('')}</ul></details>
  <div class="small">Plan <code>${esc((p.hash||'').slice(0,16))}</code> · ${games?'Game files':'HOME'} version <code>${esc(p.version||'')}</code>${(p.packages||[]).length?' · '+p.packages.map(x=>esc(x.label||x.name)).join(', '):''}</div>`; }
async function ensurePlan(force){ if(CONSOLE.plan&&!force) return CONSOLE.plan; CONSOLE.planErr=null; CONSOLE.preflight=null;
 const r=await hkPost('/api/console/home/plan',planBody()); if(!r.ok){ CONSOLE.plan=null; CONSOLE.planErr=r.code==='not-found'?'Prepare the HOME menu first (above).':(r.error||'The plan could not be made. Try again.'); return null; }
 CONSOLE.plan=r.result; return CONSOLE.plan; }
async function ensurePreflight(){ if(!CONSOLE.plan) return null; if(CONSOLE.preflight&&CONSOLE.preflight._p===CONSOLE.profile+CONSOLE.plan.hash) return CONSOLE.preflight;
 const r=await hkPost('/api/console/home/preflight',{plan_hash:CONSOLE.plan.hash, profile:CONSOLE.profile});
 if(r.status===409||r.code==='plan-obsolete'){ CONSOLE.plan=null; CONSOLE.planErr=r.error||'The package changed since this plan: review it again.'; return null; }
 CONSOLE.preflight=r.ok?r.result:{ok:false,why:r.error||'The check could not run. Try again.'};
 CONSOLE.preflight._p=CONSOLE.profile+CONSOLE.plan.hash; return CONSOLE.preflight; }
// ---- on your console: the physical operations through the app's one dispatcher (console_home.py). Each asks Gate G1 first, in code (both
// tiers open in this version), and runs the same engine the rehearsal uses, on the checked console, with a verified backup of that console
// and a confirmation made by the backend for exactly one operation, plan (or reviewed removal, or attempt) and console, once.
const PHYS={busy:false, op:null, run:null, cursor:0, events:[], result:null, check:null, confirm:null, review:null, recovery:null, keepData:true, err:null, written:null};
function physAvail(id){ return !!(((CONSOLE.status&&CONSOLE.status.operations)||[]).find(o=>o.id===id)||{}).available; }
function physConsole(){ return ((CONSOLE.status||{}).console||{}).checked||null; }
function pbtn(id,label,op,on,cls){ const av=physAvail(op); return `<button class="btn sm ${av&&cls||'secondary'}" id="${id}" ${on&&av&&!PHYS.busy?'':'disabled'} ${av?'':`title="${esc(GATE_TEXT)}"`}>${esc(label)}</button>`; }   // an unavailable step never looks like the next one
const PHYS_OUT={installed:'Installed and checked', 'cleanup-stopped':'Installed and checked; the clean-up stopped', 'no-change':'Nothing needed to change', refused:'Refused before any change', cancelled:'Canceled before the switch', stopped:'Stopped',
 'rolled-back':'Recovered: undone and checked', 'rollback-incomplete':'Recovery not complete', unknown:'Not known: nothing was changed', uninstalled:'Removed and checked', incomplete:'Removal not complete',
 complete:'Every file copied and checked', partial:'Copied, with files left out', empty:'No saves on the console'};
function physOutcome(){ const q=PHYS.result; if(!q) return ''; if(q.event==='error') return `<div class="msg ${q.code==='plan-obsolete'?'warn':'bad'}"><b>${q.code==='plan-obsolete'?'The plan is out of date':q.code==='physical-disabled'?esc(GATE_TEXT):'The operation could not run'}</b><br>${esc(q.text||q.code||'')}</div>`;
 const r=q.result||{}; const k=r.result||r.status||(r.ok?'complete':'refused'); const good=['installed','no-change','rolled-back','uninstalled','complete','empty'].includes(k)||(PHYS.op==='verify'&&r.verified)||(PHYS.op==='inspect'&&r.ok);
 let h=`<div class="msg ${good?'ok':k==='refused'||k==='partial'||k==='cancelled'||k==='cleanup-stopped'?'warn':'bad'}"><b>${esc(PHYS.op==='verify'?(r.verified?(r.delivery==='games'?'The console has this plan’s games, every file checked':'The console starts this plan’s HOME, every file checked'):'Not as planned'):PHYS.op==='inspect'?(r.ok?'Console checked':'This console cannot be used'):k==='incomplete'&&['install','update'].includes(PHYS.op)?OUTCOME['incomplete-install']:PHYS_OUT[k]||k)}</b>`;
 if(r.why) h+=`<br>${hakchiNotice(r.why)}`;
 if(k==='cleanup-stopped') h+=`<br>No further writes were made. Recovering attempt <code>${esc(r.attempt||'')}</code> is its own confirmed step below.`;
 if(k==='stopped'||k==='cancelled') h+=`<br>${r.switched==='yes'?'The console now starts the new HOME.':r.switched==='unknown'?'Whether the console starts the new HOME is not known: check the console.':'The console still starts as before.'} No further writes were made. Recovering attempt <code>${esc(r.attempt||'')}</code> is its own confirmed step below.`;
 if(r.rolled_back&&r.rolled_back.problems) h+=`<ul>${r.rolled_back.problems.map(p=>`<li>${esc(p)}</li>`).join('')}</ul>`;
 if((r.kept_changed||[]).length) h+=`<br>Kept, because they changed since the review:<ul>${r.kept_changed.slice(0,12).map(x=>`<li><code>${esc(x)}</code></li>`).join('')}</ul>`;
 if((r.problems||[]).length&&!r.rolled_back) h+=`<ul>${r.problems.map(p=>`<li>${esc(p)}</li>`).join('')}</ul>`;
 if(PHYS.op==='export') h+=`<br>${r.files||0} file${r.files===1?'':'s'}, ${fmtB(r.bytes||0)}${r.folder?` · folder <code>${esc(r.folder)}</code>`:''}${(r.skipped||[]).length?`<br>Left out:<ul>${r.skipped.slice(0,12).map(s=>`<li><code>${esc(s.path)}</code>: ${esc(s.why)}</li>`).join('')}</ul>`:''}${r.interrupted?`<br>Interrupted: ${esc(r.interrupted)}`:''}`;
 if(r.after_restart) h+=`<br>${esc(r.after_restart)}`;
 return h+'</div>'; }
function physHtml(){ const c=physConsole(); const p=CONSOLE.plan; const any=['inspect','install','recover','export'].some(physAvail);
 const prog=PHYS.busy&&PHYS.waitText?`<div class="msg">${esc(PHYS.waitText)}</div>`:PHYS.busy?(()=>{ const ev=PHYS.events.filter(e=>e.event==='progress'); const last=ev[ev.length-1]||{}; const st=[...ev].reverse().find(e=>(e.stage==='stage'||e.stage==='games')&&e.total); const pct=last.stage==='done'?100:st?Math.round(100*st.done/st.total):0;
   return `<div class="pbar"><i style="width:${pct}%"></i></div><span class="small">${esc(last.stage?stLabel(last.stage):'Working')}${st?` · ${st.done} of ${st.total} files`:''}</span>${['install','update'].includes(PHYS.op)?`<div class="acts"><span class="sp"></span><button class="btn sm secondary" id="pCancel">Cancel</button></div><span class="small">${(CONSOLE.plan||{}).delivery==='games'?'Cancel stops before the games are added; the console stays as it was.':'Cancel stops before the switch; the console keeps starting as before.'}</span>`:''}`; })():'';
 const conf=PHYS.confirm?`<div class="msg warn"><b>Confirm</b><br>${esc(PHYS.confirm.covers)}${PHYS.confirm.backup&&PHYS.confirm.backup.data_only?`<br><b>${esc(PHYS.confirm.backup.title||'')}</b><br>${esc(PHYS.confirm.backup.notice||'')}`:''}<div class="acts" style="margin-top:6px"><span class="sp"></span><button class="btn sm secondary" id="pConfNo">Cancel</button><button class="btn sm hk" id="pConfYes">${esc({install:'Confirm and Install',update:'Confirm and Update',uninstall:'Confirm Removal',recover:'Confirm Recovery'}[PHYS.confirm.operation])}</button></div></div>`:'';
 const rv=PHYS.review&&PHYS.review.ok?`<div class="msg"><b>Removal, as it would run</b><br><b>Removes:</b> ${PHYS.review.remove.map(esc).join('; ')||'nothing of this app'}<br><b>Keeps:</b> ${PHYS.review.keep.map(esc).join('; ')||'nothing else'}</div>`:PHYS.review?`<div class="msg warn">${esc(PHYS.review.why||'')}</div>`:'';
 const rc=PHYS.recovery&&PHYS.recovery.ok?`<div class="msg"><b>Interrupted attempts on this console</b>${(PHYS.recovery.attempts||[]).length?`<ul>${PHYS.recovery.attempts.map(a=>`<li><code>${esc(a.attempt)}</code> · ${a.changes} change${a.changes===1?'':'s'} recorded · switched: ${esc(a.switched)}${a.recoverable?` <button class="btn sm secondary pRecover" data-a="${esc(a.attempt)}" ${physAvail('recover')&&!PHYS.busy?'':'disabled'}>Recover</button>`:` · ${esc(a.why||'')}`}</li>`).join('')}</ul>`:'<br>None.'}</div>`:'';
 return `<div class="msg ${any?'':'na'}"><b>On your console</b>${any?'':` <span class="lv unknown">${esc(GATE_TEXT)}</span>`}<br><span class="small">${c?`Checked: ${esc(c.label)}, ${Math.round(c.checked_seconds_ago/60)} min ago${c.written_since?' (changed since: check it again before the next change)':''}.`:'Connect your NES Classic or Super NES Classic with Hakchi2 CE 3.9.3’s hakchi installed, then check it. Every change needs a verified backup of that console (Back Up Console in CCS Console Workbench) and your confirmation, made for exactly that plan and that console, once.'}</span>
  <div class="acts" style="margin-top:6px;flex-wrap:wrap">${pbtn('pInspect','Check the Console','inspect',true)}${pbtn('pCheck','Check This Plan','inspect',!!(c&&p))}${pbtn('hkPhys','Install on Console','install',!!(c&&p&&PHYS.check&&PHYS.check.ok&&PHYS.check.plan_hash===p.hash&&!c.written_since),'hk')}${pbtn('pVerify','Verify','inspect',!!(c&&p))}</div>
  <div class="acts" style="margin-top:6px;flex-wrap:wrap">${pbtn('pExport','Copy the Console’s Saves','export',!!c)}${pbtn('pReview','Review Removal','recover',!!c)}<label class="small" style="display:inline-flex;gap:6px;align-items:center"><input type="checkbox" id="pKeep" ${PHYS.keepData?'checked':''} ${physAvail('recover')?'':'disabled'}> Keep the console's saves</label>${pbtn('pRemove','Remove from Console','recover',!!(c&&PHYS.review&&PHYS.review.ok&&!c.written_since))}${pbtn('pRecRev','Review Recovery','recover',!!c)}</div>
  ${prog}${conf}${rv}${rc}${PHYS.check?(PHYS.check.ok?`<div class="small">This plan against the checked console: ready${PHYS.check.changes_text?' ('+esc(PHYS.check.changes_text)+')':''}${PHYS.check.already?' (already installed: nothing would change)':''}.${earlierHtml(PHYS.check.earlier)}</div>`:`<div class="msg warn"><b>This plan against the checked console: blocked</b><br>${hakchiNotice(PHYS.check.why||'')}</div>`):''}${physOutcome()}</div>`; }
function physWritten(){ return PHYS.written&&PHYS.written.plan===(CONSOLE.plan||{}).hash?PHYS.written:null; }
function physDoneHtml(w){ const r=(w.result&&w.result.result)||{}; const k=r.result||r.status||'';
 const good=['installed','no-change','uninstalled','rolled-back'].includes(k);
 return `<div class="msg ${good?'ok':'warn'}"><b>${esc(PHYS_OUT[k]||k||'Not complete')}</b> on ${esc(w.console||'the checked console')}.${k==='installed'||k==='no-change'?' Every file was read back.':''}${r.why?`<br>${hakchiNotice(r.why)}`:''}</div>`; }
async function physRun(op,body,after){ PHYS.busy=true; PHYS.op=op; PHYS.events=[]; PHYS.cursor=0; PHYS.result=null; PHYS.waitText=['install','update','uninstall','recover'].includes(op)?'Checking this console’s backups on this computer, then starting…':''; physDraw();
 const r=await hkPost('/api/console/home/'+op,body||{}); PHYS.waitText=''; if(!r.ok){ PHYS.busy=false; PHYS.result={event:'error',code:r.code,text:r.error}; physDraw(); return null; }
 PHYS.run=r.run; procSet('hkpanel','running');
 return new Promise(res=>{ const t=setInterval(async()=>{ let q; try{ q=await capi(`/api/console/run?id=${encodeURIComponent(PHYS.run)}&cursor=${PHYS.cursor}`); }catch(e){ return; }
   if(!q.ok){ clearInterval(t); PHYS.busy=false; procSet('hkpanel',null); PHYS.result={event:'error',text:q.error||'The operation stopped answering.'}; physDraw(); res(null); return; }
   PHYS.events=PHYS.events.concat(q.events||[]); PHYS.cursor=q.cursor;
   if(q.done){ clearInterval(t); PHYS.busy=false; procSet('hkpanel',null); PHYS.result=q.result; if(['install','update','uninstall','recover'].includes(op)) PHYS.written={op, plan:(body||{}).plan_hash||(CONSOLE.plan||{}).hash, result:q.result, console:(physConsole()||{}).label};
    await consoleStatus(); if(after) after(q.result); physDraw(); if(['install','update'].includes(op)&&$('hkNext')) $('hkNext').disabled=false; res(q.result); } else physDraw(); },400); }); }
async function physConfirm(operation,subject){ PHYS.busy=true; PHYS.waitText='Checking this console’s backups on this computer…'; physDraw(); let r; try{ r=await hkPost('/api/console/home/confirm',{operation,...subject}); } finally{ PHYS.busy=false; PHYS.waitText=''; } if(!r.ok){ PHYS.result={event:'error',code:r.code,text:r.error}; physDraw(); return; } PHYS.confirm={...r,subject}; physDraw(); }
function physDraw(){ const el=$('cPhys'); if(!el) return; el.innerHTML=physHtml(); const on=(id,f)=>{ const b=$(id); if(b) b.onclick=f; }; const p=CONSOLE.plan;
 on('pInspect',()=>physRun('inspect',{},()=>{ PHYS.check=null; PHYS.review=null; PHYS.recovery=null; }));
 on('pCheck',()=>physRun('check',{plan_hash:p.hash},q=>{ PHYS.check=q&&q.event==='result'?q.result:{ok:false,why:(q||{}).text}; if(PHYS.check.ok&&PHYS.check.plan) CONSOLE.plan=PHYS.check.plan; }));
 on('hkPhys',()=>{ const s=(physConsole()||{}).state||{}; physConfirm(p&&(s.in_use||(s.games_copies||[]).length)?'update':'install',{plan_hash:p.hash}); });
 on('pVerify',()=>physRun('verify',{plan_hash:p.hash}));
 on('pExport',()=>physRun('export',{}));
 const kd=$('pKeep'); if(kd) kd.onchange=()=>{ PHYS.keepData=kd.checked; PHYS.review=null; physDraw(); };
 on('pReview',()=>physRun('uninstall-review',{keep_data:PHYS.keepData},q=>{ PHYS.review=q&&q.event==='result'?q.result:null; }));
 on('pRemove',()=>physConfirm('uninstall',{review:PHYS.review.hash,keep_data:PHYS.keepData}));
 on('pRecRev',()=>physRun('recover-review',{},q=>{ PHYS.recovery=q&&q.event==='result'?q.result:null; }));
 document.querySelectorAll('#cPhys .pRecover').forEach(b=>b.onclick=()=>physConfirm('recover',{attempt:b.dataset.a}));
 on('pCancel',async()=>{ const b=$('pCancel'); if(b){ b.disabled=true; b.textContent='Canceling…'; } await hkPost('/api/console/home/cancel',{run:PHYS.run}); });
 on('pConfNo',()=>{ PHYS.confirm=null; physDraw(); });
 on('pConfYes',()=>{ const c=PHYS.confirm; PHYS.confirm=null; physRun(c.operation,{...c.subject,confirmation:c.confirmation},()=>{ PHYS.check=null; PHYS.review=null; PHYS.recovery=null; }); }); }
function rehearseHtml(canSim){ const sc=(CONSOLE.status&&CONSOLE.status.scenarios)||{};
 return `<div class="msg ${canSim?'sim':'na'}"><b>Rehearse on a simulated console</b> ${simBadge()}<br><span class="small">Runs every step of the installation against a disposable stand-in on this computer, with real progress, checks and undo. Nothing physical happens.</span>
  <div class="row" style="display:flex;gap:10px;flex-wrap:wrap;align-items:center;margin-top:6px"><label class="small" for="cScenario"><b>Rehearse</b></label><select id="cScenario">${Object.keys(sc).map(k=>`<option value="${esc(k)}" ${k===CONSOLE.scenario?'selected':''}>${esc(sc[k])}</option>`).join('')}</select>
   <label class="small" style="display:inline-flex;gap:6px;align-items:center"><input type="checkbox" id="cThenU" ${CONSOLE.then.includes('uninstall')?'checked':''}> Then rehearse removal</label>
   <label class="small" style="display:inline-flex;gap:6px;align-items:center"><input type="checkbox" id="cThenE" ${CONSOLE.then.includes('export')?'checked':''}> Then rehearse copying the saves</label></div>
  <div class="acts" style="margin-top:6px"><span class="sp"></span><button class="btn hk" id="hkSim" ${canSim?'':'disabled'}>Rehearse</button></div></div><div id="cProgress"></div>`; }
function progressHtml(){ const ev=CONSOLE.events.filter(e=>e.event==='progress'); const last=ev[ev.length-1]||{}; const st=[...ev].reverse().find(e=>(e.stage==='stage'||e.stage==='games')&&e.total);
 const pct=last.stage==='done'?100:st?Math.round(100*st.done/st.total):0;
 return `<div class="msg sim"><b>Rehearsal in progress</b> ${simBadge()} · ${esc(last.stage?stLabel(last.stage):'Starting')}<div class="pbar"><i style="width:${pct}%"></i></div><span class="small">${st?`${st.done} of ${st.total} files · ${fmtB(st.bytes)} of ${fmtB(st.total_bytes)}`:'Starting…'}</span>
  <div class="acts" style="margin-top:6px"><span class="sp"></span><button class="btn sm secondary" id="cCancel">Cancel</button></div><span class="small">${(CONSOLE.plan||{}).delivery==='games'?'Cancel stops before the games are added and undoes what the rehearsal changed.':'Cancel stops before the switch and undoes what the rehearsal changed.'}</span></div>`; }
function outcomeHtml(res){ if(!res) return `<div class="msg na"><b>Nothing to verify yet.</b> Rehearse first.</div>`;
 if(res.event==='error') return `<div class="msg ${res.code==='plan-obsolete'?'warn':'bad'}"><b>${res.code==='plan-obsolete'?'The plan is out of date':'The rehearsal could not run'}</b><br>${esc(res.text||res.code||'')}</div>`;
 const r=res.result||{}; const i=r.install||{}; const rb=i.rolled_back; const ok=!!i.ok;
 const head=i.result==='installed'?'Installed and checked on the simulated console':i.result==='no-change'?'Nothing needed to change':i.result==='refused'?'Refused before any change':i.result==='cancelled'?'Canceled before the switch':i.result==='incomplete'?'Game files copied and checked, but not yet in Original Menu':'Stopped';
 let h=`<div class="msg ${ok?'ok':rb&&rb.ok?'warn':i.result==='refused'?'warn':'bad'}"><b>${esc(head)}</b> ${simBadge()}<span class="small"> · ${esc(SCENARIO_NAME(r.scenario))}</span>`;
 if(i.why) h+=`<br>${esc(i.why)}`;
 if(rb) h+=`<br>${rb.ok?'Everything this attempt changed was undone and read back: the simulated console is as it was.':'<b>The undo could not finish.</b> On a real console nothing more would be written; the record of this attempt is kept for diagnosis.'}`;
 if(r.recovered) h+=`<br>Recovered from its own record: ${esc(r.recovered.result==='rolled-back'?'undone and read back':r.recovered.result||'')}. Then installed again: ${esc(OUTCOME[r.after_recovery]||r.after_recovery||'')}.`;
 h+=`</div>`;
 const rows=[]; if(i.result==='installed'||i.result==='incomplete') rows.push([i.delivery==='games'?'Game files on the console':'Copy on the console',`<code>${esc(i.copy||'')}</code> · every file read back and matched`]);
 if(i.hakchi&&i.hakchi.action&&i.hakchi.action!=='none') rows.push(['Original Menu',esc(i.hakchi.text||'')]);
 if(r.setup&&r.setup.length) rows.push(['Before the rehearsal',esc(r.setup.map(s=>s.installed_first?`an earlier HOME was installed (${s.installed_first})`:s.changed_on_console?`changed on the console: ${s.changed_on_console}`:'').filter(Boolean).join('; '))]);
 const kept=i.kept_changed_or_unknown||[]; if(kept.length) rows.push(['Kept as they were',`<ul>${kept.slice(0,12).map(k=>`<li><code>${esc(k)}</code></li>`).join('')}</ul>`]);
 rows.push(['Built-in games and saves',r.stock_unchanged?'unchanged':'<b>CHANGED</b>']);
 if(r.uninstall){ const u=r.uninstall; rows.push(['Removal',`${u.ok?'removed and checked':'<b>not complete</b>'} · ${esc(u.after_restart||'')} · saves ${esc(u.data)}${(u.kept_unknown||[]).length?` · kept: ${u.kept_unknown.map(esc).join(', ')}`:''}${(u.kept_changed||[]).length?` · kept (changed since the review): ${u.kept_changed.map(esc).join(', ')}`:''}`]);
  if(r.uninstall_plan&&r.uninstall_plan.ok) rows.push(['What removal shows first',`<b>Removes:</b> ${r.uninstall_plan.remove.map(esc).join('; ')}<br><b>Keeps:</b> ${r.uninstall_plan.keep.map(esc).join('; ')||'nothing else'}`]); }
 if(r.export) rows.push(['Copy of the saves',`<b>${esc(OUTCOME[r.export.status]||r.export.status||'')}</b> · ${r.export.files} file${r.export.files===1?'':'s'}, ${fmtB(r.export.bytes)} · ${r.export.verified?'every copy matched the console':'<b>a copy did not match</b>'}${(r.export.skipped||[]).length?`<br>Left out: ${r.export.skipped.slice(0,8).map(x=>`<code>${esc(x.path)}</code> (${esc(x.why)})`).join(', ')}`:''}`]);
 rows.push(['Record',`<code>${esc(r.journal||'')}</code> (kept on this computer)`]);
 return h+`<div class="msg"><dl class="tvkv">${rows.map(([k,v])=>`<dt>${k}</dt><dd>${v}</dd>`).join('')}</dl></div><div class="msg na"><b>This was a rehearsal.</b> Nothing was written to your console.</div>`; }
// ---- native HOME package (N2): the console's native HOME built just in time from this collection and its HOME settings
// (theme, order, titles, folders, favorites), staged on this computer only. Nothing is sent to a console (Gate G1). ----
const NHP={job:null,timer:null,progress:null,result:null,fp:null,apply:false,music:true,onDone:null};   // music: Include HOME music (selected by default)
const NH_CAT=[['executable','HOME menu program'],['shared','Shared artwork, icons and theme thumbnails'],['theme-arcade-grid','Theme: Arcade Grid'],['theme-box-art-showcase','Theme: Box Art Showcase'],['theme-featured-shelf','Theme: Featured Shelf'],['theme-adventure-map','Theme: Adventure Map'],
 ['game-covers','Game covers (one per game, all themes)'],['game-details-artwork','Game Details backdrops'],['game-theme-artwork','Featured Shelf game screens'],['folder-artwork','Folder artwork'],['font','Fonts and licenses'],['music','HOME Music (the whole song, played by the console)'],['catalog','Game list'],['descriptions','Descriptions'],['configuration','Settings'],['other','Other']];
// the console's initial HOME theme (N3 addendum): chosen here, stored with the HOME settings, packaged as the default; the console
// keeps the player's own later choice unless "apply at the next installation" is ticked for a build
function nhTheme(){ try{ return homeModelFromStudio().theme; }catch(e){ return 'featured-shelf'; } }   // the default theme by its id (home-model.js HOME_DEFAULT_THEME)
function nhSetTheme(t){ try{ setTimeout(()=>dispatchEvent(new CustomEvent('ccs-content-changed',{detail:{what:'theme',theme:t}})),0); }catch(e){} try{ const ex=homeExtrasLoad(); ex.theme=t; localStorage.setItem(HOME_KEY,JSON.stringify(ex)); if(typeof TV!=='undefined'){ TV.proj=null; if(TV.model) TV.model.theme=t; } }catch(e){} nhDraw(); }
function nhThemeHtml(){ const cur=nhTheme(); return `<div class="row" style="display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin-top:6px"><label class="small" for="nhThemeSel"><b>Starting HOME theme</b></label>
  <select id="nhThemeSel">${HOME_THEME_META.map(t=>`<option value="${t.id}" ${t.id===cur?'selected':''}>${esc(t.name)}</option>`).join('')}</select>
  <label class="small" style="display:inline-flex;gap:6px;align-items:center"><input type="checkbox" id="nhApply" ${NHP.apply?'checked':''}> Also apply it at the next installation</label></div>
  <span class="small">All four themes are included; players switch with START › Change Theme. After an update, the console keeps the player’s choice unless this box is checked.</span>
  <div style="margin-top:6px"><label class="small" style="display:inline-flex;gap:6px;align-items:center"><input type="checkbox" id="nhMusic" ${NHP.music!==false?'checked':''}> Include HOME music</label></div>
  <span class="small">The song CCS HOME plays on the console. Leaving it out keeps it off the console and saves its space; HOME Music then has no song to play there.</span>`; }
// the HOME the backend packages. withKept (HOME, playing the HOME on this computer): the collection's kept entries too, with their
// sanitized details (M2-4); without (installing on the console): only games this computer resolves, as the install manifest lists them
function nhHome(withKept){ const m=homeModelFromStudio(); const keep=id=>withKept||!!byId[id]; const order=(m.order||[]).filter(keep);
 return {schema:m.schema, theme:m.theme, order, games:(m.games||[]).filter(g=>keep(g.id)).map(g=>{ const o={id:g.id,title:g.title,version_label:g.version_label,kind:g.kind};
   if(!byId[g.id]) o.kept={year:g.year||null,publisher:g.publisher||null,players:g.players||null,genre:g.genre||null,description:g.description||null,art:g.art&&g.art.src||null}; return o; }),
 folders:(m.folders||[]).map(f=>({id:f.id,name:f.name,parent:f.parent||null,order:(f.order||[]).filter(keep),art:f.art&&f.art.src?{src:f.art.src}:null})), favorites:m.favorites||{}, settings:m.settings||{}, sample:!!m.sample, map_shortcuts:m.map_shortcuts||[null,null], apply_theme:!!NHP.apply, include_music:NHP.music!==false, startup_image:m.startup_image||'random', startup_custom:m.startup_custom&&m.startup_custom.id===m.startup_image?((typeof suInlineHome==='function'?suInlineHome({startup_custom:m.startup_custom}):{}).startup_custom||m.startup_custom):null, ...(m.design?{design:(typeof suInlineHome==='function'?suInlineHome({design:m.design}):{}).design||m.design}:{})}; }   // the chosen own picture and the HOME design travel with their bytes; the backend checks them again (a design is compiled by HOME Designer's compiler, never left out)
function nhFp(){ try{ const m=homeModelFromStudio(); return collFingerprint()+'|'+m.theme+'|'+(m.startup_image||'random')+'|'+((m.startup_custom&&m.startup_custom.sha256)||'')+'|'+((m.design&&m.design.sha256)||'')+'|'+(NHP.apply?'apply':'')+(NHP.music===false?'|no-music':'')+'|'+JSON.stringify(m.map_shortcuts||[])+'|'+(typeof OV!=='undefined'&&OV&&OV.stamp?OV.stamp:'')+'|'+JSON.stringify((m.folders||[]).map(f=>[f.id,f.name,f.order]))+'|'+JSON.stringify(m.favorites&&m.favorites.items||{}); }catch(e){ return ''; } }
function nhHtml(){ const r=NHP.result; const stale=r&&NHP.fp!==nhFp();
 const head=`<b>Console HOME menu</b> <span class="small">· built from this collection and its HOME Settings</span>`+nhThemeHtml();
 if(NHP.job&&!r){ const p=NHP.progress||{}; const pct=p.total?Math.round(100*p.done/p.total):0;
  return `<div class="msg sim">${head}<div class="pbar"><i style="width:${pct}%"></i></div><span class="small">${p.stage?esc(stLabel(p.stage)):'Starting'}${p.text?' · '+esc(STEP_TEXT[p.text]||p.text):''}${p.total?` (${p.done} of ${p.total})`:''}</span></div>`; }
 const btn=`<div class="acts" style="margin-top:6px"><span class="sp"></span><button class="btn sm ${r&&!stale?'secondary':'hk'}" id="nhBuild">${r?'Prepare Again':'Prepare HOME Menu'}</button></div>`;
 if(!r) return `<div class="msg">${head}<br><span class="small">Not prepared yet. Preparing builds it on this computer and measures its size; nothing is sent to the console.</span>${btn}</div>`;
 if(!r.ok) return `<div class="msg bad">${head}<br><b>Not prepared:</b> ${esc(r.error||r.status||'failed')}${btn}</div>`;
 const m=r.manifest||{}; const S=m.sizes||{}; const by=S.by_category||{}; const ready=r.ready&&!stale;
 const rows=NH_CAT.map(([k,l])=>`<tr><td>${esc(l)}</td><td style="text-align:right">${fmtB(by[k]||0)}</td></tr>`).join('');
 return `<div class="msg ${ready?'ok':'warn'}">${head}<br><b>${stale?'Out of date: the collection or its HOME Settings changed. Prepare again.':ready?'Ready':'Not ready'}</b>${ready?` · ${r.games} games · <b>${fmtB(S.total_with_manifest||S.total)}</b> · ${r.files} files checked`:''}
  ${(m.blockers||[]).length?`<ul>${m.blockers.map(b=>`<li>${esc(b.text)}</li>`).join('')}</ul>`:''}${(m.warnings||[]).length?`<ul class="small">${m.warnings.map(w=>`<li>${esc(w)}</li>`).join('')}</ul>`:''}
  <details class="small" style="margin-top:6px"><summary>Size Details</summary><table class="small" style="margin-top:6px;min-width:300px"><tbody>${rows}<tr><td><b>Total</b> (game files not included)</td><td style="text-align:right"><b>${fmtB(S.total_with_manifest||S.total)}</b></td></tr></tbody></table></details>
  ${nhConsoleHtml(r.console)}
  <span class="small">Starting theme: ${esc((HOME_THEME_META.find(t=>t.id===((m.configuration||{}).theme_default))||{}).name||'')}${(m.configuration||{}).theme_apply?' (applied at installation)':''}. Prepared on this computer; the review below reads its plan.</span>${btn}</div>`; }
// the console package beside it: the games with their exact ROM files, the console's game player and the startup screen
function nhConsoleHtml(c){ if(!c) return ''; if(!c.ok) return `<div class="small"><b>Console package:</b> not prepared (${esc(c.error||'failed')}).</div>`;
 const st=c.startup||{}; const sn=st.choice==='random'?'Random':/^custom-/.test(st.choice||'')?`Your picture${st.custom&&st.custom.name?' ('+st.custom.name+')':''}`:((HOME_STARTUP.find(s=>s.id===st.choice)||{}).name||st.choice);
 const un=c.unavailable||[]; return `<div class="small"><b>Console package:</b> ${c.playable} of ${c.games} game${c.games===1?'':'s'} with ${c.playable===1?'its':'their'} game file${c.playable===1?'':'s'} · ${fmtB(c.total_bytes||0)} with the game player and the startup screen (${esc(sn)}) · ${c.files} files checked${un.length?`<br>Without a game file here (the console says so when one is chosen): ${un.slice(0,6).map(u=>esc(u.title)).join(', ')}${un.length>6?` and ${un.length-6} more`:''}`:''}</div>`; }
function nhDraw(){ const el=$('nhPkg'); if(!el){ clearInterval(NHP.timer); NHP.timer=null; return; } el.innerHTML=nhHtml(); const b=$('nhBuild'); if(b) b.onclick=nhStart;
 const ts=$('nhThemeSel'); if(ts) ts.onchange=()=>nhSetTheme(ts.value); const ap=$('nhApply'); if(ap) ap.onchange=()=>{ NHP.apply=ap.checked; nhDraw(); }; const mu=$('nhMusic'); if(mu) mu.onchange=()=>{ NHP.music=mu.checked; nhDraw(); }; }
async function nhStart(){ const b=$('nhBuild'); if(b) b.disabled=true; NHP.result=null; NHP.progress=null; NHP.fp=nhFp(); procSet('hkpanel','running');
 let r; try{ const m=homeModelFromStudio(); if(typeof suEnsure==='function'&&m.startup_custom&&m.startup_custom.id===m.startup_image) await suEnsure([{name:(CS.items[CS.active]||{}).name||'this collection', home:{startup_custom:m.startup_custom}}]);   /* the chosen own picture read again and checked now */
  r=await hkPost('/api/home/native-package',{manifest:INST.manifest||buildManifest(), home:nhHome()}); }catch(e){ r={ok:false,error:String(e&&e.message||e)}; }
 if(!r.ok&&!r.busy){ procSet('hkpanel',null); NHP.job=null; NHP.result={ok:false,status:'failed',error:r.error||'Could not start. Try again.'}; nhDraw(); return; }
 NHP.job=r.job; nhDraw(); clearInterval(NHP.timer);
 NHP.timer=setInterval(async()=>{ if(!$('nhPkg')){ clearInterval(NHP.timer); NHP.timer=null; procSet('hkpanel',null); return; } try{ const q=await capi(`/api/home/native-package?job=${encodeURIComponent(NHP.job)}`); if(!q.ok){ clearInterval(NHP.timer); procSet('hkpanel',null); return; }
  NHP.progress=q.progress; if(q.state==='done'){ clearInterval(NHP.timer); NHP.timer=null; NHP.result=q.result; NHP.job=null; procSet('hkpanel',null); nhDraw(); if(NHP.onDone) NHP.onDone(); return; } nhDraw(); }catch(e){} },300); }
// each render owns a token: a render that waited (the console status, the plan, the check) stops when a newer one started or the person
// moved to another step, so a slower earlier render never draws over the step on screen
async function renderConsoleStep(){ const body=$('hkBody'); const step=INST.step; const tok=++CONSOLE.tok; const live=()=>tok===CONSOLE.tok&&INST.step===step; $('hkTitle').textContent='Install on NES Classic';
 if(!consoleCapable()){ $('hkSub').textContent=CSTEP[step]||''; body.innerHTML=stepperHtml()+`<div class="msg warn"><b>These steps need the latest Classic Cart Studio for Windows.</b> ${COMPANION.desktop?'This version does not include them: install the latest version from <a href="https://ClassicCartStudio.com/app">ClassicCartStudio.com/app</a>.':'Use <b>Open or Install the Windows App</b>.'}</div><div class="acts"><button class="btn secondary" id="hkBack">Back</button><span class="sp"></span><button class="btn secondary" id="hkDone">Close</button></div>`;
  $('hkDone').onclick=()=>$('hkpanel').classList.remove('open'); $('hkBack').onclick=()=>{ INST.step='connect'; renderInstall(); }; return; }
 if(!CONSOLE.status&&['inspect','review','install'].includes(step)){ await consoleStatus(); if(!live()) return; }   // Verify and Complete draw at once
 const nav=(back,next,nextLabel,nextOk)=>`<div class="acts"><button class="btn secondary" id="hkBack">Back</button><span class="sp"></span><button class="btn secondary" id="hkDone">Close</button>${next?`<button class="btn hk" id="hkNext" ${nextOk?'':'disabled'}>${esc(nextLabel)}</button>`:''}</div>`;
 const wire=(back,next)=>{ $('hkDone').onclick=()=>{ stopPoll(); $('hkpanel').classList.remove('open'); }; $('hkBack').onclick=()=>{ stopPoll(); INST.step=back; renderInstall(); }; if($('hkNext')) $('hkNext').onclick=()=>{ stopPoll(); INST.step=next; renderInstall(); }; };
 if(step==='inspect'){ $('hkSub').textContent='Console state'; await consoleStatus(); if(!live()) return;
  body.innerHTML=stepperHtml()+`<div id="cPhys"></div>`+profileSelect()+opsHtml()+recordsHtml()+nav('connect','review','Next: Review Changes',true); wireProfile(()=>{}); wire('connect','review'); physDraw(); return; }
 if(step==='review'){ $('hkSub').textContent='What installing would change'; const st=CONSOLE.status||{};
  const draw=async()=>{ const t=++CONSOLE.tok; const ok=()=>t===CONSOLE.tok&&INST.step==='review';
   body.innerHTML=stepperHtml()+profileSelect()+`<div id="nhPkg" style="margin-top:8px"></div><div id="cPlan"><div class="msg">Reading the plan…</div></div>`+nav('inspect','install','Next: Install',false); wire('inspect','install'); wireProfile(()=>{ addonReset(); draw(); }); nhDraw();
   await consoleStatus(); if(!ok()) return; const staged=(CONSOLE.status||{}).staged; let p=null;
   if(staged&&staged.layout==='mod'&&!(CONSOLE.prefilled&&CONSOLE.cfg)){ const sm=(CONSOLE.status.records||{}).staged_manifest_sha256; p=await ensurePlan(!CONSOLE.plan||(sm&&CONSOLE.plan.manifest_sha256&&CONSOLE.plan.manifest_sha256!==sm)); if(!ok()) return; }
   const pf=p?await ensurePreflight():null; if(!ok()||!$('cPlan')) return; if(p) p=CONSOLE.plan||p;
   // the pages start from what the console has; a choice that would remove an add-on is made again with what the console keeps
   if(pf&&!CONSOLE.prefilled){ CONSOLE.prefilled=true; const n=pf.addons_now||null; CONSOLE.addonsNow=n; CONSOLE.nowText=pf.addons_now_text||''; if(n){ const a=CONSOLE.addons; a.mod=a.mod||!!n.mod; if(n.nucleus&&n.nucleus!=='none'){ a.nucleus=n.nucleus; a.start=n.start||'native'; a.auto=n.auto||'menu'; } }
    if(pf.ok&&CONSOLE.hakchi===null&&pf.hakchi&&(pf.hakchi.journal||pf.hakchi.index_sha256)) CONSOLE.hakchi=true; }
   if(pf&&!pf.ok&&pf.at==='addons'&&pf.keep_choice&&!CONSOLE.cfg&&!CONSOLE.keepNote){ CONSOLE.addons=Object.assign({},ADDON_DEFAULT,pf.keep_choice); CONSOLE.keepNote=pf.why||''; CONSOLE.plan=null; CONSOLE.preflight=null; await draw(); return; }
   if(staged&&CONSOLE.cfg){ $('cPlan').innerHTML=CONSOLE.cfg===1?addonsPageHtml():startupPageHtml(); wireAddonPages(draw); return; }
   $('cPlan').innerHTML=!staged?`<div class="msg na"><b>No console package yet.</b> Prepare the HOME menu above: it builds the console package of this collection and its HOME Settings on this computer.</div>`
    :CONSOLE.planErr?`<div class="msg warn"><b>No plan.</b> ${esc(CONSOLE.planErr)}<div class="acts" style="margin-top:6px"><span class="sp"></span><button class="btn sm secondary" id="cChangeAddons">Change Add-ons</button><button class="btn sm hk" id="cReplan">Review Plan Again</button></div></div>`+hakchiBoxHtml(null)
    :planHtml(p,pf)+`<div class="acts" style="margin-top:6px"><span class="sp"></span><button class="btn sm secondary" id="cReplan">Review Plan Again</button></div>`;
   const rp=$('cReplan'); if(rp) rp.onclick=async()=>{ CONSOLE.plan=null; CONSOLE.preflight=null; await draw(); }; wireHakchiBox(draw);
   const ca=$('cChangeAddons'); if(ca) ca.onclick=async()=>{ CONSOLE.cfg=1; await draw(); };
   const nx=$('hkNext'); if(nx) nx.disabled=!(p&&pf&&pf.ok); };
  NHP.onDone=async()=>{ CONSOLE.plan=null; CONSOLE.preflight=null; CONSOLE.result=null; if(INST.step==='review') await draw(); };
  await draw(); return; }
 if(step==='install'){ $('hkSub').textContent='Install'; let p=CONSOLE.plan; const pf=p?await ensurePreflight():null; if(!live()) return; p=CONSOLE.plan; const canSim=!!(p&&pf);
  body.innerHTML=stepperHtml()+(p?`<div class="msg"><b>Plan</b> <code>${esc(p.hash.slice(0,16))}</code> · <span data-no-terms>${esc(p.label||'')}</span> · ${fmtB(p.bytes)} · ${esc(p.addons_text||'')} ${pf&&pf.ok?'':`<br><span class="small">Against this simulated console the installation would be refused before any change (${esc((pf||{}).why||'')}); rehearsing shows exactly that.</span>`}</div>`:`<div class="msg warn"><b>No plan yet.</b> Go back to Review Changes.</div>`)
   +`<div id="cPhys"></div>`+rehearseHtml(canSim)+nav('review','verify','Next: Verify',!!(CONSOLE.result||physWritten()));
  wire('review','verify'); physDraw();
  const sc=$('cScenario'); if(sc) sc.onchange=()=>{ CONSOLE.scenario=sc.value; };
  const tu=$('cThenU'), te=$('cThenE'); const then=()=>{ CONSOLE.then=[tu&&tu.checked?'uninstall':null, te&&te.checked?'export':null].filter(Boolean); }; if(tu) tu.onchange=then; if(te) te.onchange=then;
  $('hkSim').onclick=async()=>{ $('hkSim').disabled=true;
   const r=await hkPost('/api/console/home/rehearse',{plan_hash:p.hash, profile:CONSOLE.profile, scenario:CONSOLE.scenario, then:CONSOLE.then});
   if(!r.ok){ $('cProgress').innerHTML=`<div class="msg bad">${esc(r.error||'Could not start the rehearsal. Try again.')}</div>`; $('hkSim').disabled=false; return; }
   CONSOLE.run=r.run; CONSOLE.cursor=0; CONSOLE.events=[]; CONSOLE.result=null; $('hkSub').textContent='Rehearsing (simulation)…'; procSet('hkpanel','running'); ['hkBack','hkDone','hkNext'].forEach(x=>{ if($(x)) $(x).disabled=true; });
   const bar=()=>{ if(!$('cProgress')) return; $('cProgress').innerHTML=progressHtml(); if(PROC.hkpanel==='canceling'){ $('cCancel').disabled=true; $('cCancel').textContent='Canceling…'; }
     $('cCancel').onclick=async()=>{ if(PROC.hkpanel==='canceling') return; procSet('hkpanel','canceling'); $('cCancel').disabled=true; $('cCancel').textContent='Canceling…'; await hkPost('/api/console/home/cancel',{run:CONSOLE.run}); }; };
   bar(); stopPoll(); CONSOLE.timer=setInterval(async()=>{ try{ const q=await capi(`/api/console/run?id=${encodeURIComponent(CONSOLE.run)}&cursor=${CONSOLE.cursor}`); if(!q.ok){ stopPoll(); procSet('hkpanel',null); CONSOLE.result={event:'error',text:q.error||'The rehearsal stopped answering.'}; INST.step='verify'; renderInstall(); return; }
     CONSOLE.events=CONSOLE.events.concat(q.events||[]); CONSOLE.cursor=q.cursor; bar();
     if(q.done){ stopPoll(); procSet('hkpanel',null); CONSOLE.result=q.result; INST.step='verify'; renderInstall(); } }catch(e){} },400); };
  return; }
 if(step==='verify'){ const w=physWritten(); $('hkSub').textContent=w?'Verify':'Verify (simulated)';
  body.innerHTML=stepperHtml()+(w?`<div id="cPhys"></div>`:'')+(CONSOLE.result||!w?outcomeHtml(CONSOLE.result):'')+nav('install','ready','Next: Complete',!!(CONSOLE.result||w)); wire('install','ready'); if(w) physDraw(); return; }
 if(step==='ready'){ const r=CONSOLE.result&&CONSOLE.result.event==='result'?CONSOLE.result.result:null; const i=(r||{}).install||{}; const w=physWritten(); $('hkSub').textContent=w?'Complete':'Complete (simulated)';
  body.innerHTML=stepperHtml()+(w?physDoneHtml(w):i.ok?`<div class="msg ok"><b>Rehearsal complete.</b> ${simBadge()} Every step worked on the simulated console, and every file was read back. Nothing was installed on your console.</div>`:`<div class="msg na"><b>Not complete.</b> Go back to Verify to see what happened.</div>`)
   +`<div class="msg"><b>On a console, later</b><ul><li>${i.delivery==='games'?'This collection’s games are in Original Menu, the console’s own menu, as Hakchi games beside its own games and saves. CCS HOME is not installed.':(r||{}).addons&&r.addons.mod===false?'Your console’s own menu stays as it is, with its own games and saves.':'CCS HOME is a tile in Original Menu, the console’s own menu, which stays as it is with its own games and saves.'}${(r||{}).start_text?' '+esc(r.start_text):''}</li><li>Removing this HOME shows exactly what it removes and keeps first; it keeps the console's saves unless you choose otherwise, and CCS HOME's entry then leaves the console's menu.</li><li>Installing on your own console: it needs Hakchi2 CE 3.9.3’s hakchi first (Prepare Console in CCS Console Workbench) and a verified backup of it (Back Up Console). Check it under On your console, check this plan against it, then confirm and install; every file is read back.</li></ul></div>`+nav('verify',null,'',false);
  wire('verify',null); return; }
}
(function(){ const _r=renderInstall; window.renderInstallBase=_r; renderInstall=async function(){ if(['inspect','review','install','verify','ready'].includes(INST.step)) return renderConsoleStep(); return _r.apply(this,arguments); };
 try{ const lab={inspect:'Console State', ready:'Complete'}; STEPS.forEach(s=>{ if(lab[s[0]]) s[1]=lab[s[0]]; }); }catch(e){}
 const _s=stepperHtml; stepperHtml=function(){ const h=_s(); if(!consoleCapable()) return h; return h.replace(/ na"/g,'"').replace(/<br><span class="small">Windows app<\/span>/g,''); };
 // the Connect Console step hands over to Console State instead of jumping straight to the review
 document.addEventListener('click',e=>{ const b=e.target.closest&&e.target.closest('#hkNext'); if(!b||INST.step!=='connect'||!consoleCapable()) return; e.stopImmediatePropagation(); e.preventDefault(); clearInterval(INST.pollTimer); INST.step='inspect'; renderInstall(); },true);
})();
