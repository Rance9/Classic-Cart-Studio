// Install on NES Classic: the console steps, one flow for Classic Cart Studio's own HOME on the console (K3; docs/console-install-
// architecture.md sections 13 and 14). Prepare the exact console package of this collection, its loadout and Console Menu Setup; review the
// immutable plan (what would be added, changed and kept, and the room it needs) against a simulated console; rehearse every operation on a
// disposable stand-in through the same plan, worker and installation engine a console would use; see each real-console operation and what it
// needs. Desktop app only: the public website keeps the Continue in Desktop App invitation. The operations on a real console (check, install,
// update, verify, removal, recovery, the saves copy) run through the same dispatcher and engine as the rehearsal; every one is closed in this
// version (Gate G1, in code): nothing here installs anything, and a rehearsal is always labeled as one.
const CONSOLE={tok:0, status:null, profile:'hakchi-ready', plan:null, planErr:null, preflight:null, scenario:'first-install', then:[], run:null, cursor:0, events:[], result:null, timer:null};
const CSTEP={inspect:'Console State', review:'Review Changes', install:'Install', verify:'Verify', ready:'Complete'};
function consoleCapable(){ return !!(COMPANION.ok&&COMPANION.desktop&&(COMPANION.capabilities||[]).includes('console-sim')); }
async function consoleStatus(){ const r=await capi('/api/console/home/status'); if(r.ok){ CONSOLE.status=r; if(!(r.profiles||[]).some(p=>p.name===CONSOLE.profile)) CONSOLE.profile='hakchi-ready'; } return !!r.ok; }
const GATE_TEXT='Not available in this version';
// progress labels for the installation engine's stages (console_engine/ccs_install.py) and the HOME package build (native_home/package.py); ids stay internal
const STAGE_LABEL={preflight:'Checking the console', stage:'Copying the HOME', readback:'Reading back every file', switch:'Switching to the new HOME', module:'Setting the console to start it', verify:'Checking the result', done:'Done',
 recover:'Recovering the interrupted attempt', uninstall:'Rehearsing removal', export:'Rehearsing the copy of the saves', transfer:'Copying the HOME', cancelled:'Canceled', journal:'Starting', configuration:'Reading Console Menu Setup', assemble:'Building the HOME menu'};
const STEP_TEXT={themes:'theme artwork', fonts:'fonts', runtime:'menu program', manifest:'package contents'};
function stLabel(s){ return STAGE_LABEL[s]||'Working'; }
function lv(l){ return `<span class="lv ${l}">${l}</span>`; }
function fmtB(n){ return n==null?'unknown':fmt(n); }   // decimal, from the exact byte count (it divided by 1,024 under decimal labels before)
function simBadge(){ return '<span class="simtag" title="A simulated console, not your NES Classic">SIMULATION</span>'; }
function stopPoll(){ clearInterval(CONSOLE.timer); CONSOLE.timer=null; }
function profileSelect(){ const ps=(CONSOLE.status&&CONSOLE.status.profiles)||[];
 return `<div class="msg sim"><b>Simulated console</b> ${simBadge()}<br><span class="small">Checking a real console is not available in this version. The review and the rehearsal use the console you choose here; nothing touches your NES Classic.</span><br><select id="cProfile" style="margin-top:6px;max-width:100%">${ps.map(p=>`<option value="${esc(p.name)}" ${p.name===CONSOLE.profile?'selected':''}>${esc(p.label)}</option>`).join('')}</select></div>`; }
function wireProfile(onchange){ const s=$('cProfile'); if(!s) return; s.onchange=()=>{ CONSOLE.profile=s.value; CONSOLE.preflight=null; CONSOLE.result=null; onchange(); }; }
function opsHtml(){ const ops=(CONSOLE.status&&CONSOLE.status.operations)||[];
 return `<div class="msg"><b>What this app can do with a console</b><ul>${ops.map(o=>`<li><b>${esc(o.label)}</b> ${o.available?(o.tier==='simulate'?simBadge():lv('known')):'<span class="lv unknown">not in this version</span>'}<br><span class="small">${esc(o.text)}</span></li>`).join('')}</ul></div>`; }
function when(iso){ try{ return new Date(iso).toLocaleString(); }catch(e){ return iso||''; } }
function recordsHtml(){ const r=(CONSOLE.status&&CONSOLE.status.records)||{}; const lp=r.last_plan&&r.last_plan.plan; const op=(r.operations||[])[0];
 if(!lp&&!op) return '';
 return `<div class="msg"><b>Last review and rehearsal</b><dl class="tvkv">${lp?`<dt>Plan</dt><dd><code>${esc((lp.hash||'').slice(0,16))}</code> · ${esc(lp.label||'')} · ${r.last_plan_current?'matches the prepared package':'<b>out of date</b>: the package was prepared again since; review the plan again'}</dd>`:''}
  ${op?`<dt>${op.kind==='rehearsal'||!op.kind?'Rehearsal':'Last console operation'}</dt><dd>${esc(OUTCOME[op.outcome]||op.outcome||'')} ${op.simulated?simBadge():''} · ${esc(op.kind==='rehearsal'||!op.kind?SCENARIO_NAME(op.scenario):op.kind)} · ${esc(when(op.ended||op.started))}</dd>`:''}</dl></div>`; }
const OUTCOME={installed:'Installed and checked', 'no-change':'Nothing needed to change', refused:'Refused before any change', stopped:'Stopped and undone', cancelled:'Canceled and undone', 'rolled-back':'Undone and checked', 'rollback-incomplete':'Undo incomplete', unknown:'Unknown',
 uninstalled:'Removed and checked', incomplete:'Not complete', complete:'Every file copied and checked', partial:'Copied, with files left out', empty:'No saves there', failed:'Failed'};
function SCENARIO_NAME(s){ return ((CONSOLE.status&&CONSOLE.status.scenarios)||{})[s]||s||''; }
function dataText(d){ return d.state==='kept'?'kept as they are':d.state==='kept-unidentified'?'kept as they are':'none there yet'; }
function planHtml(p,pf){ if(!p) return '';
 const st=p.startup||{}; const sn=st.choice==='random'?'Random':((typeof HOME_STARTUP!=='undefined'&&HOME_STARTUP.find(s=>s.id===st.choice))||{}).name||st.choice||'';
 const keep=(pf&&pf.ok&&pf.kept_unknown)||[];
 return `<div class="sum"><div class="sb"><b>${p.playable==null?'?':p.playable}</b><span>games to play</span></div><div class="sb"><b>${p.file_count}</b><span>files</span></div><div class="sb"><b>${fmtB(p.bytes)}</b><span>to copy</span></div><div class="sb"><b>${fmtB(p.needs_free_bytes)}</b><span>room needed</span></div></div>
  <div class="diff"><div class="d"><b>Added</b><ul><li>This HOME as a new copy (${p.file_count} files, ${fmtB(p.bytes)}), beside the one in use</li><li>Startup screen: ${esc(sn)}</li></ul></div>
   <div class="d"><b>Changed</b><ul><li>The console starts this HOME (hakchi module <code>ccs-home</code>). After <b>Switch to Nintendo HOME</b>, or if anything goes wrong, Nintendo's HOME runs until the next start.</li><li>An earlier copy of this HOME is removed only after the new one is checked, and only its own unchanged files.</li></ul></div>
   <div class="d"><b>Kept as is</b><ul><li>The 30 built-in games and their saves</li><li>Other hakchi modules</li><li>Saves, slots and settings made on the console${pf&&pf.ok&&pf.data?` (${esc(dataText(pf.data))})`:''}</li>${keep.map(k=>`<li><code>${esc(k)}</code> (not made by this app)</li>`).join('')}</ul></div></div>
  ${pf?(pf.ok?`<div class="msg ok"><b>Check against the simulated console: ready</b> ${simBadge()}<br>${pf.already?'This exact HOME is already there: nothing would change.':`${fmtB(pf.free_bytes)} free, ${fmtB(pf.needs_bytes)} needed.`} ${pf.module==='current'?'':pf.module==='older'?'The start-up module would be updated.':'The start-up module would be added.'}</div>`
    :`<div class="msg bad"><b>Check against the simulated console: blocked</b> ${simBadge()}<br>${esc(pf.why||pf.error||'')}<br><span class="small">Nothing would be changed.</span></div>`):''}
  <details class="small"><summary>Never Written</summary><ul>${(p.never_written||[]).map(x=>`<li>${esc(x)}</li>`).join('')}</ul></details>
  <div class="small">Plan <code>${esc((p.hash||'').slice(0,16))}</code> · HOME version <code>${esc(p.version||'')}</code></div>`; }
async function ensurePlan(force){ if(CONSOLE.plan&&!force) return CONSOLE.plan; CONSOLE.planErr=null; CONSOLE.preflight=null;
 const r=await hkPost('/api/console/home/plan',{}); if(!r.ok){ CONSOLE.plan=null; CONSOLE.planErr=r.code==='not-found'?'Prepare the HOME menu first (above).':(r.error||'The plan could not be made. Try again.'); return null; }
 CONSOLE.plan=r.result; return CONSOLE.plan; }
async function ensurePreflight(){ if(!CONSOLE.plan) return null; if(CONSOLE.preflight&&CONSOLE.preflight._p===CONSOLE.profile+CONSOLE.plan.hash) return CONSOLE.preflight;
 const r=await hkPost('/api/console/home/preflight',{plan_hash:CONSOLE.plan.hash, profile:CONSOLE.profile});
 if(r.status===409||r.code==='plan-obsolete'){ CONSOLE.plan=null; CONSOLE.planErr=r.error||'The package changed since this plan: review it again.'; return null; }
 CONSOLE.preflight=r.ok?r.result:{ok:false,why:r.error||'The check could not run. Try again.'}; CONSOLE.preflight._p=CONSOLE.profile+CONSOLE.plan.hash; return CONSOLE.preflight; }
// ---- on your NES Classic: the physical operations through the app's one dispatcher (console_home.py). Each asks Gate G1 first, in code:
// in this version every button below stays disabled and the backend refuses them. When a reviewed release opens a tier, the same buttons run
// the same engine the rehearsal uses, on the checked console, with a confirmation made by the backend for exactly one operation, plan (or
// reviewed removal, or attempt) and console, once.
const PHYS={busy:false, op:null, run:null, cursor:0, events:[], result:null, check:null, confirm:null, review:null, recovery:null, keepData:true, err:null};
function physAvail(id){ return !!(((CONSOLE.status&&CONSOLE.status.operations)||[]).find(o=>o.id===id)||{}).available; }
function physConsole(){ return ((CONSOLE.status||{}).console||{}).checked||null; }
function pbtn(id,label,op,on,cls){ const av=physAvail(op); return `<button class="btn sm ${av&&cls||'secondary'}" id="${id}" ${on&&av&&!PHYS.busy?'':'disabled'} ${av?'':`title="${esc(GATE_TEXT)}"`}>${esc(label)}</button>`; }   // an unavailable step never looks like the next one
const PHYS_OUT={installed:'Installed and checked', 'no-change':'Nothing needed to change', refused:'Refused before any change', cancelled:'Canceled before the switch', stopped:'Stopped',
 'rolled-back':'Recovered: undone and checked', 'rollback-incomplete':'Recovery not complete', unknown:'Not known: nothing was changed', uninstalled:'Removed and checked', incomplete:'Removal not complete',
 complete:'Every file copied and checked', partial:'Copied, with files left out', empty:'No saves on the console'};
function physOutcome(){ const q=PHYS.result; if(!q) return ''; if(q.event==='error') return `<div class="msg ${q.code==='plan-obsolete'?'warn':'bad'}"><b>${q.code==='plan-obsolete'?'The plan is out of date':q.code==='physical-disabled'?esc(GATE_TEXT):'The operation could not run'}</b><br>${esc(q.text||q.code||'')}</div>`;
 const r=q.result||{}; const k=r.result||r.status||(r.ok?'complete':'refused'); const good=['installed','no-change','rolled-back','uninstalled','complete','empty'].includes(k)||(PHYS.op==='verify'&&r.verified)||(PHYS.op==='inspect'&&r.ok);
 let h=`<div class="msg ${good?'ok':k==='refused'||k==='partial'||k==='cancelled'?'warn':'bad'}"><b>${esc(PHYS.op==='verify'?(r.verified?'The console starts this plan’s HOME, every file checked':'Not as planned'):PHYS.op==='inspect'?(r.ok?'Console checked':'This console cannot be used'):PHYS_OUT[k]||k)}</b>`;
 if(r.why) h+=`<br>${esc(r.why)}`;
 if(k==='stopped'||k==='cancelled') h+=`<br>${r.switched==='yes'?'The console now starts the new HOME.':r.switched==='unknown'?'Whether the console starts the new HOME is not known: check the console.':'The console still starts as before.'} No further writes were made. Recovering attempt <code>${esc(r.attempt||'')}</code> is its own confirmed step below.`;
 if(r.rolled_back&&r.rolled_back.problems) h+=`<ul>${r.rolled_back.problems.map(p=>`<li>${esc(p)}</li>`).join('')}</ul>`;
 if((r.kept_changed||[]).length) h+=`<br>Kept, because they changed since the review:<ul>${r.kept_changed.slice(0,12).map(x=>`<li><code>${esc(x)}</code></li>`).join('')}</ul>`;
 if((r.problems||[]).length&&!r.rolled_back) h+=`<ul>${r.problems.map(p=>`<li>${esc(p)}</li>`).join('')}</ul>`;
 if(PHYS.op==='export') h+=`<br>${r.files||0} file${r.files===1?'':'s'}, ${fmtB(r.bytes||0)}${r.folder?` · folder <code>${esc(r.folder)}</code>`:''}${(r.skipped||[]).length?`<br>Left out:<ul>${r.skipped.slice(0,12).map(s=>`<li><code>${esc(s.path)}</code>: ${esc(s.why)}</li>`).join('')}</ul>`:''}${r.interrupted?`<br>Interrupted: ${esc(r.interrupted)}`:''}`;
 if(r.after_restart) h+=`<br>${esc(r.after_restart)}`;
 return h+'</div>'; }
function physHtml(){ const c=physConsole(); const p=CONSOLE.plan; const any=['inspect','install','recover','export'].some(physAvail);
 const prog=PHYS.busy?(()=>{ const ev=PHYS.events.filter(e=>e.event==='progress'); const last=ev[ev.length-1]||{}; const st=[...ev].reverse().find(e=>e.stage==='stage'&&e.total); const pct=last.stage==='done'?100:st?Math.round(100*st.done/st.total):0;
   return `<div class="pbar"><i style="width:${pct}%"></i></div><span class="small">${esc(last.stage?stLabel(last.stage):'Working')}${st?` · ${st.done} of ${st.total} files`:''}</span>${['install','update'].includes(PHYS.op)?`<div class="acts"><span class="sp"></span><button class="btn sm secondary" id="pCancel">Cancel</button></div><span class="small">Cancel stops before the switch; the console keeps starting as before.</span>`:''}`; })():'';
 const conf=PHYS.confirm?`<div class="msg warn"><b>Confirm</b><br>${esc(PHYS.confirm.covers)}<div class="acts" style="margin-top:6px"><span class="sp"></span><button class="btn sm secondary" id="pConfNo">Cancel</button><button class="btn sm hk" id="pConfYes">${esc({install:'Confirm and Install',update:'Confirm and Update',uninstall:'Confirm Removal',recover:'Confirm Recovery'}[PHYS.confirm.operation])}</button></div></div>`:'';
 const rv=PHYS.review&&PHYS.review.ok?`<div class="msg"><b>Removal, as it would run</b><br><b>Removes:</b> ${PHYS.review.remove.map(esc).join('; ')||'nothing of this app'}<br><b>Keeps:</b> ${PHYS.review.keep.map(esc).join('; ')||'nothing else'}</div>`:PHYS.review?`<div class="msg warn">${esc(PHYS.review.why||'')}</div>`:'';
 const rc=PHYS.recovery&&PHYS.recovery.ok?`<div class="msg"><b>Interrupted attempts on this console</b>${(PHYS.recovery.attempts||[]).length?`<ul>${PHYS.recovery.attempts.map(a=>`<li><code>${esc(a.attempt)}</code> · ${a.changes} change${a.changes===1?'':'s'} recorded · switched: ${esc(a.switched)}${a.recoverable?` <button class="btn sm secondary pRecover" data-a="${esc(a.attempt)}" ${physAvail('recover')&&!PHYS.busy?'':'disabled'}>Recover</button>`:` · ${esc(a.why||'')}`}</li>`).join('')}</ul>`:'<br>None.'}</div>`:'';
 return `<div class="msg ${any?'':'na'}"><b>On your NES Classic</b>${any?'':` <span class="lv unknown">not in this version</span>`}<br><span class="small">${c?`Checked: ${esc(c.label)}, ${Math.round(c.checked_seconds_ago/60)} min ago${c.written_since?' (changed since: check it again before the next change)':''}.`:'Connect your NES Classic with hakchi installed, then check it. Every change needs your confirmation, made for exactly that plan and that console, once.'}</span>
  <div class="acts" style="margin-top:6px;flex-wrap:wrap">${pbtn('pInspect','Check the Console','inspect',true)}${pbtn('pCheck','Check This Plan','inspect',!!(c&&p))}${pbtn('hkPhys','Install on NES Classic','install',!!(c&&p&&PHYS.check&&PHYS.check.ok&&!c.written_since),'hk')}${pbtn('pVerify','Verify','inspect',!!(c&&p))}</div>
  <div class="acts" style="margin-top:6px;flex-wrap:wrap">${pbtn('pExport','Copy the Console’s Saves','export',!!c)}${pbtn('pReview','Review Removal','recover',!!c)}<label class="small" style="display:inline-flex;gap:6px;align-items:center"><input type="checkbox" id="pKeep" ${PHYS.keepData?'checked':''} ${physAvail('recover')?'':'disabled'}> Keep the console's saves</label>${pbtn('pRemove','Remove from NES Classic','recover',!!(c&&PHYS.review&&PHYS.review.ok&&!c.written_since))}${pbtn('pRecRev','Review Recovery','recover',!!c)}</div>
  ${prog}${conf}${rv}${rc}${PHYS.check?(PHYS.check.ok?`<div class="small">This plan against the checked console: ready${PHYS.check.already?' (already installed: nothing would change)':''}.</div>`:`<div class="msg warn"><b>This plan against the checked console: blocked</b><br>${esc(PHYS.check.why||'')}</div>`):''}${physOutcome()}</div>`; }
async function physRun(op,body,after){ PHYS.busy=true; PHYS.op=op; PHYS.events=[]; PHYS.cursor=0; PHYS.result=null; physDraw();
 const r=await hkPost('/api/console/home/'+op,body||{}); if(!r.ok){ PHYS.busy=false; PHYS.result={event:'error',code:r.code,text:r.error}; physDraw(); return null; }
 PHYS.run=r.run; procSet('hkpanel','running');
 return new Promise(res=>{ const t=setInterval(async()=>{ let q; try{ q=await capi(`/api/console/run?id=${encodeURIComponent(PHYS.run)}&cursor=${PHYS.cursor}`); }catch(e){ return; }
   if(!q.ok){ clearInterval(t); PHYS.busy=false; procSet('hkpanel',null); PHYS.result={event:'error',text:q.error||'The operation stopped answering.'}; physDraw(); res(null); return; }
   PHYS.events=PHYS.events.concat(q.events||[]); PHYS.cursor=q.cursor;
   if(q.done){ clearInterval(t); PHYS.busy=false; procSet('hkpanel',null); PHYS.result=q.result; await consoleStatus(); if(after) after(q.result); physDraw(); res(q.result); } else physDraw(); },400); }); }
async function physConfirm(operation,subject){ const r=await hkPost('/api/console/home/confirm',{operation,...subject}); if(!r.ok){ PHYS.result={event:'error',code:r.code,text:r.error}; physDraw(); return; } PHYS.confirm={...r,subject}; physDraw(); }
function physDraw(){ const el=$('cPhys'); if(!el) return; el.innerHTML=physHtml(); const on=(id,f)=>{ const b=$(id); if(b) b.onclick=f; }; const p=CONSOLE.plan;
 on('pInspect',()=>physRun('inspect',{},()=>{ PHYS.check=null; PHYS.review=null; PHYS.recovery=null; }));
 on('pCheck',()=>physRun('check',{plan_hash:p.hash},q=>{ PHYS.check=q&&q.event==='result'?q.result:{ok:false,why:(q||{}).text}; }));
 on('hkPhys',()=>physConfirm(p&&(physConsole()||{}).state&&physConsole().state.in_use?'update':'install',{plan_hash:p.hash}));
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
function progressHtml(){ const ev=CONSOLE.events.filter(e=>e.event==='progress'); const last=ev[ev.length-1]||{}; const st=[...ev].reverse().find(e=>e.stage==='stage'&&e.total);
 const pct=last.stage==='done'?100:st?Math.round(100*st.done/st.total):0;
 return `<div class="msg sim"><b>Rehearsal in progress</b> ${simBadge()} · ${esc(last.stage?stLabel(last.stage):'Starting')}<div class="pbar"><i style="width:${pct}%"></i></div><span class="small">${st?`${st.done} of ${st.total} files · ${fmtB(st.bytes)} of ${fmtB(st.total_bytes)}`:'Starting…'}</span>
  <div class="acts" style="margin-top:6px"><span class="sp"></span><button class="btn sm secondary" id="cCancel">Cancel</button></div><span class="small">Cancel stops before the switch and undoes what the rehearsal changed.</span></div>`; }
function outcomeHtml(res){ if(!res) return `<div class="msg na"><b>Nothing to verify yet.</b> Rehearse first.</div>`;
 if(res.event==='error') return `<div class="msg ${res.code==='plan-obsolete'?'warn':'bad'}"><b>${res.code==='plan-obsolete'?'The plan is out of date':'The rehearsal could not run'}</b><br>${esc(res.text||res.code||'')}</div>`;
 const r=res.result||{}; const i=r.install||{}; const rb=i.rolled_back; const ok=!!i.ok;
 const head=i.result==='installed'?'Installed and checked on the simulated console':i.result==='no-change'?'Nothing needed to change':i.result==='refused'?'Refused before any change':i.result==='cancelled'?'Canceled before the switch':'Stopped';
 let h=`<div class="msg ${ok?'ok':rb&&rb.ok?'warn':i.result==='refused'?'warn':'bad'}"><b>${esc(head)}</b> ${simBadge()}<span class="small"> · ${esc(SCENARIO_NAME(r.scenario))}</span>`;
 if(i.why) h+=`<br>${esc(i.why)}`;
 if(rb) h+=`<br>${rb.ok?'Everything this attempt changed was undone and read back: the simulated console is as it was.':'<b>The undo could not finish.</b> On a real console nothing more would be written; the record of this attempt is kept for diagnosis.'}`;
 if(r.recovered) h+=`<br>Recovered from its own record: ${esc(r.recovered.result==='rolled-back'?'undone and read back':r.recovered.result||'')}. Then installed again: ${esc(OUTCOME[r.after_recovery]||r.after_recovery||'')}.`;
 h+=`</div>`;
 const rows=[]; if(i.result==='installed') rows.push(['Copy on the console',`<code>${esc(i.copy||'')}</code> · every file read back and matched`]);
 if(r.setup&&r.setup.length) rows.push(['Before the rehearsal',esc(r.setup.map(s=>s.installed_first?`an earlier HOME was installed (${s.installed_first})`:s.changed_on_console?`changed on the console: ${s.changed_on_console}`:'').filter(Boolean).join('; '))]);
 const kept=i.kept_changed_or_unknown||[]; if(kept.length) rows.push(['Kept as they were',`<ul>${kept.slice(0,12).map(k=>`<li><code>${esc(k)}</code></li>`).join('')}</ul>`]);
 rows.push(['Built-in games and saves',r.stock_unchanged?'unchanged':'<b>CHANGED</b>']);
 if(r.uninstall){ const u=r.uninstall; rows.push(['Removal',`${u.ok?'removed and checked':'<b>not complete</b>'} · ${esc(u.after_restart||'')} · saves ${esc(u.data)}${(u.kept_unknown||[]).length?` · kept: ${u.kept_unknown.map(esc).join(', ')}`:''}${(u.kept_changed||[]).length?` · kept (changed since the review): ${u.kept_changed.map(esc).join(', ')}`:''}`]);
  if(r.uninstall_plan&&r.uninstall_plan.ok) rows.push(['What removal shows first',`<b>Removes:</b> ${r.uninstall_plan.remove.map(esc).join('; ')}<br><b>Keeps:</b> ${r.uninstall_plan.keep.map(esc).join('; ')||'nothing else'}`]); }
 if(r.export) rows.push(['Copy of the saves',`<b>${esc(OUTCOME[r.export.status]||r.export.status||'')}</b> · ${r.export.files} file${r.export.files===1?'':'s'}, ${fmtB(r.export.bytes)} · ${r.export.verified?'every copy matched the console':'<b>a copy did not match</b>'}${(r.export.skipped||[]).length?`<br>Left out: ${r.export.skipped.slice(0,8).map(x=>`<code>${esc(x.path)}</code> (${esc(x.why)})`).join(', ')}`:''}`]);
 rows.push(['Record',`<code>${esc(r.journal||'')}</code> (kept on this computer)`]);
 return h+`<div class="msg"><dl class="tvkv">${rows.map(([k,v])=>`<dt>${k}</dt><dd>${v}</dd>`).join('')}</dl></div><div class="msg na"><b>This was a rehearsal.</b> Nothing was written to your NES Classic.</div>`; }
// ---- native HOME package (N2): the console's native HOME built just in time from this collection and its HOME settings
// (theme, order, titles, folders, favorites), staged on this computer only. Nothing is sent to a console (Gate G1). ----
const NHP={job:null,timer:null,progress:null,result:null,fp:null,apply:false,onDone:null};
const NH_CAT=[['executable','HOME menu program'],['shared','Shared artwork, icons and theme thumbnails'],['theme-arcade-grid','Theme: Arcade Grid'],['theme-box-art-showcase','Theme: Box Art Showcase'],['theme-featured-shelf','Theme: Featured Shelf'],['theme-adventure-map','Theme: Adventure Map'],
 ['game-covers','Game covers (one per game, all themes)'],['game-details-artwork','Game Details backdrops'],['game-theme-artwork','Featured Shelf key art'],['folder-artwork','Folder artwork'],['font','Fonts and licenses'],['music','HOME Music (the whole song, played by the console)'],['catalog','Game list'],['descriptions','Descriptions'],['configuration','Settings'],['other','Other']];
// the console's initial HOME theme (N3 addendum): chosen here, stored with the HOME settings, packaged as the default; the console
// keeps the player's own later choice unless "apply at the next installation" is ticked for a build
function nhTheme(){ try{ return homeModelFromStudio().theme; }catch(e){ return 'arcade-grid'; } }
function nhSetTheme(t){ try{ setTimeout(()=>dispatchEvent(new CustomEvent('ccs-content-changed',{detail:{what:'theme',theme:t}})),0); }catch(e){} try{ const ex=homeExtrasLoad(); ex.theme=t; localStorage.setItem(HOME_KEY,JSON.stringify(ex)); if(typeof TV!=='undefined'){ TV.proj=null; if(TV.model) TV.model.theme=t; } }catch(e){} nhDraw(); }
function nhThemeHtml(){ const cur=nhTheme(); return `<div class="row" style="display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin-top:6px"><label class="small" for="nhThemeSel"><b>Starting HOME theme</b></label>
  <select id="nhThemeSel">${HOME_THEME_META.map(t=>`<option value="${t.id}" ${t.id===cur?'selected':''}>${esc(t.name)}</option>`).join('')}</select>
  <label class="small" style="display:inline-flex;gap:6px;align-items:center"><input type="checkbox" id="nhApply" ${NHP.apply?'checked':''}> Also apply it at the next installation</label></div>
  <span class="small">All four themes are included; players switch with START › Change Theme. After an update, the console keeps the player’s choice unless this box is checked.</span>`; }
// the HOME the backend packages. withKept (Preview on TV, playing the HOME on this computer): the collection's kept entries too, with their
// sanitized details (M2-4); without (installing on the console): only games this computer resolves, as the install manifest lists them
function nhHome(withKept){ const m=homeModelFromStudio(); const keep=id=>withKept||!!byId[id]; const order=(m.order||[]).filter(keep);
 return {schema:m.schema, theme:m.theme, order, games:(m.games||[]).filter(g=>keep(g.id)).map(g=>{ const o={id:g.id,title:g.title,version_label:g.version_label,kind:g.kind};
   if(!byId[g.id]) o.kept={year:g.year||null,publisher:g.publisher||null,players:g.players||null,genre:g.genre||null,description:g.description||null,art:g.art&&g.art.src||null}; return o; }),
 folders:(m.folders||[]).map(f=>({id:f.id,name:f.name,parent:f.parent||null,order:(f.order||[]).filter(keep),art:f.art&&f.art.src?{src:f.art.src}:null})), favorites:m.favorites||{}, settings:m.settings||{}, sample:!!m.sample, map_shortcuts:m.map_shortcuts||[null,null], apply_theme:!!NHP.apply, startup_image:m.startup_image||'random'}; }
function nhFp(){ try{ const m=homeModelFromStudio(); return collFingerprint()+'|'+m.theme+'|'+(m.startup_image||'random')+'|'+(NHP.apply?'apply':'')+'|'+JSON.stringify(m.map_shortcuts||[])+'|'+(typeof OV!=='undefined'&&OV&&OV.stamp?OV.stamp:'')+'|'+JSON.stringify((m.folders||[]).map(f=>[f.id,f.name,f.order]))+'|'+JSON.stringify(m.favorites&&m.favorites.items||{}); }catch(e){ return ''; } }
function nhHtml(){ const r=NHP.result; const stale=r&&NHP.fp!==nhFp();
 const head=`<b>Console HOME menu</b> <span class="small">· built from this collection and its Console Menu Setup</span>`+nhThemeHtml();
 if(NHP.job&&!r){ const p=NHP.progress||{}; const pct=p.total?Math.round(100*p.done/p.total):0;
  return `<div class="msg sim">${head}<div class="pbar"><i style="width:${pct}%"></i></div><span class="small">${p.stage?esc(stLabel(p.stage)):'Starting'}${p.text?' · '+esc(STEP_TEXT[p.text]||p.text):''}${p.total?` (${p.done} of ${p.total})`:''}</span></div>`; }
 const btn=`<div class="acts" style="margin-top:6px"><span class="sp"></span><button class="btn sm ${r&&!stale?'secondary':'hk'}" id="nhBuild">${r?'Prepare Again':'Prepare HOME Menu'}</button></div>`;
 if(!r) return `<div class="msg">${head}<br><span class="small">Not prepared yet. Preparing builds it on this computer and measures its size; nothing is sent to the console.</span>${btn}</div>`;
 if(!r.ok) return `<div class="msg bad">${head}<br><b>Not prepared:</b> ${esc(r.error||r.status||'failed')}${btn}</div>`;
 const m=r.manifest||{}; const S=m.sizes||{}; const by=S.by_category||{}; const ready=r.ready&&!stale;
 const rows=NH_CAT.map(([k,l])=>`<tr><td>${esc(l)}</td><td style="text-align:right">${fmtB(by[k]||0)}</td></tr>`).join('');
 return `<div class="msg ${ready?'ok':'warn'}">${head}<br><b>${stale?'Out of date: the collection or its Console Menu Setup changed. Prepare again.':ready?'Ready':'Not ready'}</b>${ready?` · ${r.games} games · <b>${fmtB(S.total_with_manifest||S.total)}</b> · ${r.files} files checked`:''}
  ${(m.blockers||[]).length?`<ul>${m.blockers.map(b=>`<li>${esc(b.text)}</li>`).join('')}</ul>`:''}${(m.warnings||[]).length?`<ul class="small">${m.warnings.map(w=>`<li>${esc(w)}</li>`).join('')}</ul>`:''}
  <details class="small" style="margin-top:6px"><summary>Size Details</summary><table class="small" style="margin-top:6px;min-width:300px"><tbody>${rows}<tr><td><b>Total</b> (ROM files not included)</td><td style="text-align:right"><b>${fmtB(S.total_with_manifest||S.total)}</b></td></tr></tbody></table></details>
  ${nhConsoleHtml(r.console)}
  <span class="small">Starting theme: ${esc((HOME_THEME_META.find(t=>t.id===((m.configuration||{}).theme_default))||{}).name||'')}${(m.configuration||{}).theme_apply?' (applied at installation)':''}. Prepared on this computer; the review below reads its plan.</span>${btn}</div>`; }
// the console package beside it (D-113): the games with their exact ROM files, the console's game player and the startup screen
function nhConsoleHtml(c){ if(!c) return ''; if(!c.ok) return `<div class="small"><b>Console package:</b> not prepared (${esc(c.error||'failed')}).</div>`;
 const st=c.startup||{}; const sn=st.choice==='random'?'Random':((HOME_STARTUP.find(s=>s.id===st.choice)||{}).name||st.choice);
 const un=c.unavailable||[]; return `<div class="small"><b>Console package:</b> ${c.playable} of ${c.games} game${c.games===1?'':'s'} with ${c.playable===1?'its':'their'} ROM file${c.playable===1?'':'s'} · ${fmtB(c.total_bytes||0)} with the game player and the startup screen (${esc(sn)}) · ${c.files} files checked${un.length?`<br>Without a ROM file here (the console says so when one is chosen): ${un.slice(0,6).map(u=>esc(u.title)).join(', ')}${un.length>6?` and ${un.length-6} more`:''}`:''}</div>`; }
function nhDraw(){ const el=$('nhPkg'); if(!el){ clearInterval(NHP.timer); NHP.timer=null; return; } el.innerHTML=nhHtml(); const b=$('nhBuild'); if(b) b.onclick=nhStart;
 const ts=$('nhThemeSel'); if(ts) ts.onchange=()=>nhSetTheme(ts.value); const ap=$('nhApply'); if(ap) ap.onchange=()=>{ NHP.apply=ap.checked; nhDraw(); }; }
async function nhStart(){ const b=$('nhBuild'); if(b) b.disabled=true; NHP.result=null; NHP.progress=null; NHP.fp=nhFp(); procSet('hkpanel','running');
 let r; try{ r=await hkPost('/api/home/native-package',{manifest:INST.manifest||buildManifest(), home:nhHome()}); }catch(e){ r={ok:false,error:String(e)}; }
 if(!r.ok&&!r.busy){ procSet('hkpanel',null); NHP.job=null; NHP.result={ok:false,status:'failed',error:r.error||'Could not start. Try again.'}; nhDraw(); return; }
 NHP.job=r.job; nhDraw(); clearInterval(NHP.timer);
 NHP.timer=setInterval(async()=>{ if(!$('nhPkg')){ clearInterval(NHP.timer); NHP.timer=null; procSet('hkpanel',null); return; } try{ const q=await capi(`/api/home/native-package?job=${encodeURIComponent(NHP.job)}`); if(!q.ok){ clearInterval(NHP.timer); procSet('hkpanel',null); return; }
  NHP.progress=q.progress; if(q.state==='done'){ clearInterval(NHP.timer); NHP.timer=null; NHP.result=q.result; NHP.job=null; procSet('hkpanel',null); nhDraw(); if(NHP.onDone) NHP.onDone(); return; } nhDraw(); }catch(e){} },300); }
// each render owns a token: a render that waited (the console status, the plan, the check) stops when a newer one started or the person
// moved to another step, so a slower earlier render never draws over the step on screen
async function renderConsoleStep(){ const body=$('hkBody'); const step=INST.step; const tok=++CONSOLE.tok; const live=()=>tok===CONSOLE.tok&&INST.step===step; $('hkTitle').textContent='Install on NES Classic';
 if(!consoleCapable()){ $('hkSub').textContent=CSTEP[step]||''; body.innerHTML=stepperHtml()+`<div class="msg warn"><b>These steps need the latest Classic Cart Studio for Windows.</b> ${COMPANION.desktop?'This version does not include them: install the latest version from <a href="https://ClassicCartStudio.com/app">ClassicCartStudio.com/app</a>.':'Use <b>Open in Windows App</b>.'}</div><div class="acts"><button class="btn secondary" id="hkBack">Back</button><span class="sp"></span><button class="btn secondary" id="hkDone">Close</button></div>`;
  $('hkDone').onclick=()=>$('hkpanel').classList.remove('open'); $('hkBack').onclick=()=>{ INST.step='connect'; renderInstall(); }; return; }
 if(!CONSOLE.status&&['inspect','review','install'].includes(step)){ await consoleStatus(); if(!live()) return; }   // Verify and Complete draw at once
 const nav=(back,next,nextLabel,nextOk)=>`<div class="acts"><button class="btn secondary" id="hkBack">Back</button><span class="sp"></span><button class="btn secondary" id="hkDone">Close</button>${next?`<button class="btn hk" id="hkNext" ${nextOk?'':'disabled'}>${esc(nextLabel)}</button>`:''}</div>`;
 const wire=(back,next)=>{ $('hkDone').onclick=()=>{ stopPoll(); $('hkpanel').classList.remove('open'); }; $('hkBack').onclick=()=>{ stopPoll(); INST.step=back; renderInstall(); }; if($('hkNext')) $('hkNext').onclick=()=>{ stopPoll(); INST.step=next; renderInstall(); }; };
 if(step==='inspect'){ $('hkSub').textContent='Console state (simulated)'; await consoleStatus(); if(!live()) return;
  body.innerHTML=stepperHtml()+profileSelect()+opsHtml()+recordsHtml()+nav('connect','review','Next: Review Changes',true); wireProfile(()=>{}); wire('connect','review'); return; }
 if(step==='review'){ $('hkSub').textContent='What installing would change'; const st=CONSOLE.status||{};
  const draw=async()=>{ const t=++CONSOLE.tok; const ok=()=>t===CONSOLE.tok&&INST.step==='review';
   body.innerHTML=stepperHtml()+profileSelect()+`<div id="nhPkg" style="margin-top:8px"></div><div id="cPlan"><div class="msg">Reading the plan…</div></div>`+nav('inspect','install','Next: Install',false); wire('inspect','install'); wireProfile(draw); nhDraw();
   await consoleStatus(); if(!ok()) return; const staged=(CONSOLE.status||{}).staged; let p=null;
   if(staged&&staged.layout==='install'){ p=await ensurePlan(!CONSOLE.plan||CONSOLE.plan.manifest_sha256===undefined||((CONSOLE.status.records||{}).staged_manifest_sha256&&CONSOLE.plan.manifest_sha256!==CONSOLE.status.records.staged_manifest_sha256)); if(!ok()) return; }
   const pf=p?await ensurePreflight():null; if(!ok()||!$('cPlan')) return;
   $('cPlan').innerHTML=!staged?`<div class="msg na"><b>No console package yet.</b> Prepare the HOME menu above: it builds the console package of this collection, its lineup and Console Menu Setup on this computer.</div>`
    :CONSOLE.planErr?`<div class="msg warn"><b>No plan.</b> ${esc(CONSOLE.planErr)}<div class="acts" style="margin-top:6px"><span class="sp"></span><button class="btn sm hk" id="cReplan">Review Plan Again</button></div></div>`
    :planHtml(p,pf)+`<div class="acts" style="margin-top:6px"><span class="sp"></span><button class="btn sm secondary" id="cReplan">Review Plan Again</button></div>`;
   const rp=$('cReplan'); if(rp) rp.onclick=async()=>{ CONSOLE.plan=null; CONSOLE.preflight=null; await draw(); };
   const nx=$('hkNext'); if(nx) nx.disabled=!(p&&pf&&pf.ok); };
  NHP.onDone=async()=>{ CONSOLE.plan=null; CONSOLE.preflight=null; CONSOLE.result=null; if(INST.step==='review') await draw(); };
  await draw(); return; }
 if(step==='install'){ $('hkSub').textContent='Install'; const p=CONSOLE.plan; const pf=p?await ensurePreflight():null; if(!live()) return; const canSim=!!(p&&pf);
  body.innerHTML=stepperHtml()+(p?`<div class="msg"><b>Plan</b> <code>${esc(p.hash.slice(0,16))}</code> · ${esc(p.label||'')} · ${fmtB(p.bytes)} ${pf&&pf.ok?'':`<br><span class="small">Against this simulated console the installation would be refused before any change (${esc((pf||{}).why||'')}); rehearsing shows exactly that.</span>`}</div>`:`<div class="msg warn"><b>No plan yet.</b> Go back to Review Changes.</div>`)
   +rehearseHtml(canSim)+`<div id="cPhys"></div>`+nav('review','verify','Next: Verify',!!CONSOLE.result);
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
 if(step==='verify'){ $('hkSub').textContent='Verify (simulated)';
  body.innerHTML=stepperHtml()+outcomeHtml(CONSOLE.result)+nav('install','ready','Next: Complete',!!CONSOLE.result); wire('install','ready'); return; }
 if(step==='ready'){ const r=CONSOLE.result&&CONSOLE.result.event==='result'?CONSOLE.result.result:null; const i=(r||{}).install||{}; $('hkSub').textContent='Complete (simulated)';
  body.innerHTML=stepperHtml()+(i.ok?`<div class="msg ok"><b>Rehearsal complete.</b> ${simBadge()} Every step worked on the simulated console, and every file was read back. Nothing was installed on a physical NES Classic.</div>`:`<div class="msg na"><b>Not complete.</b> Go back to Verify to see what happened.</div>`)
   +`<div class="msg"><b>On a console, later</b><ul><li>Nintendo's HOME stays one choice away: <b>Switch to Nintendo HOME</b> opens it with its own games and saves until the next start, and Nintendo's HOME also runs if anything goes wrong.</li><li>Removing this HOME shows exactly what it removes and keeps first; it keeps the console's saves unless you choose otherwise, and the console then starts in Nintendo's HOME.</li><li>Installing on a console needs steps this version does not include: its own approval, and hakchi's own first-time setup with the pinned Hakchi2 CE.</li></ul></div>`+nav('verify',null,'',false);
  wire('verify',null); return; }
}
(function(){ const _r=renderInstall; window.renderInstallBase=_r; renderInstall=async function(){ if(['inspect','review','install','verify','ready'].includes(INST.step)) return renderConsoleStep(); return _r.apply(this,arguments); };
 try{ const lab={inspect:'Console State', ready:'Complete'}; STEPS.forEach(s=>{ if(lab[s[0]]) s[1]=lab[s[0]]; }); }catch(e){}
 const _s=stepperHtml; stepperHtml=function(){ const h=_s(); if(!consoleCapable()) return h; return h.replace(/ na"/g,'"').replace(/<br><span class="small">not in this version<\/span>/g,'<br><span class="small">simulated</span>'); };
 // the Connect Console step hands over to Console State instead of jumping straight to the review
 document.addEventListener('click',e=>{ const b=e.target.closest&&e.target.closest('#hkNext'); if(!b||INST.step!=='connect'||!consoleCapable()) return; e.stopImmediatePropagation(); e.preventDefault(); clearInterval(INST.pollTimer); INST.step='inspect'; renderInstall(); },true);
})();
