// Install on NES Classic: the console steps (state, review, authorization, progress, verification, complete), driven by one
// installation plan from the backend's console engine (docs/console-install-architecture.md). Desktop app only: the public
// website keeps the Continue in Desktop App invitation. Physical installation is disabled pending Gate G1 owner approval;
// the only operation that runs is Rehearse Installation, an unmistakable simulation against a chosen fake console profile.
const CONSOLE={available:false, profiles:[], tiers:null, profile:'', device:null, plan:null, preflight:null, run:null, cursor:0, events:[], result:null, timer:null, verify:null};
const CSTEP={inspect:'Console State', review:'Review Changes', install:'Install', verify:'Verify', ready:'Complete'};
function consoleCapable(){ return !!(COMPANION.ok&&COMPANION.desktop&&(COMPANION.capabilities||[]).includes('console-sim')); }
async function consoleCaps(){ if(CONSOLE.profiles.length) return true; try{ const r=await capi('/api/console/capabilities'); if(r.ok){ CONSOLE.available=true; CONSOLE.profiles=r.capabilities.profiles||[]; CONSOLE.tiers=r.capabilities.tiers||null; return true; } }catch(e){} return false; }
const GATE_TEXT='Not available in this version';
function lv(l){ return `<span class="lv ${l}">${l}</span>`; }
function fmtB(n){ if(n==null) return 'unknown'; const u=['B','KB','MB','GB']; let i=0; let v=n; while(v>=1024&&i<3){ v/=1024; i++; } return (i?v.toFixed(1):v)+' '+u[i]; }
function simBadge(){ return '<span class="simtag" title="A simulated console, not your NES Classic">SIMULATION</span>'; }
function profileSelect(){ const opts=CONSOLE.profiles.map(p=>`<option value="${esc(p.name)}" ${p.name===CONSOLE.profile?'selected':''}>${esc(p.label)}</option>`).join('');
 return `<div class="msg sim"><b>Simulated console</b> ${simBadge()}<br><span class="small">Reading a real console is not available in this version. Choose a simulated console to rehearse the steps; nothing here touches your NES Classic.</span><br><select id="cProfile" style="margin-top:6px;max-width:100%"><option value="">— None —</option>${opts}</select></div>`; }
function wireProfile(onchange){ const s=$('cProfile'); if(!s) return; s.onchange=()=>{ CONSOLE.profile=s.value; CONSOLE.device=null; CONSOLE.plan=null; CONSOLE.preflight=null; CONSOLE.result=null; CONSOLE.verify=null; onchange(); }; }
function stateClass(code){ return code==='compatible'?'ok':['no-device','fel','clovershell-only','outdated','no-version'].includes(code)?'warn':code?'bad':''; }
function deviceHtml(d){ if(!d) return `<div class="msg na"><b>Console inspection unavailable.</b> This version does not read a real console. Choose a simulated console above.</div>`;
 const st=d.storage; const rows=[['State',`<b>${esc(d.state||'')}</b> ${d.simulated?simBadge():''}`],['Detail',esc(d.detail||'')]];
 if(d.mode) rows.push(['Mode',esc(d.mode)]); if(d.shell) rows.push(['Link',esc(d.shell==='ssh'?'USB network (SSH)':'USB (legacy clovershell)')]);
 if(d.versions) rows.push(['Console software',`boot ${esc(d.versions.boot)} · kernel ${esc(d.versions.kernel)} · script ${esc(d.versions.script)} ${lv('known')}`]);
 if(d.system_code) rows.push(['Model code',`${esc(d.system_code)} ${lv('known')}`]); if(d.hwid) rows.push(['Hardware id',`<code>${esc(d.hwid)}</code>`]);
 rows.push(['First-time setup',d.needs_first_time_setup===true?'<b>required</b> (a separate step that is not available in this version)':d.needs_first_time_setup===false?'not needed':'unknown']);
 rows.push(['Backup',d.backup_state==='verified'?`stock kernel backup present on the console ${lv('known')}`:d.backup_state==='missing'?'<b>missing</b>: nothing can be written without a verified backup':'unknown']);
 if(st) rows.push(['Capacity',`${fmtB(st.free)} free of ${fmtB(st.total)} (${fmtB(st.games)} games, ${fmtB(st.saves)} saves) ${d.simulated?lv('estimated')+' simulated figures':lv('known')}`]); else rows.push(['Capacity','unknown '+lv('unknown')]);
 if(d.modules) rows.push(['Modules',d.modules.length?esc(d.modules.join(', ')):'none']); if(d.foreign_count!=null) rows.push(['Other games on it',String(d.foreign_count)]);
 return `<div class="msg ${stateClass(d.state_code)}"><dl class="tvkv">${rows.map(([k,v])=>`<dt>${k}</dt><dd>${v}</dd>`).join('')}</dl></div>`; }
function planHtml(p,pf){ if(!p) return ''; const S=p.summary||{}; const B=p.budget||{};
 const li=(arr)=>arr.length?`<ul>${arr.map(e=>`<li>${esc(e.title)}${e.code?` <code>${esc(e.code)}</code>`:''}${e.core&&e.core!=='clover-kachikachi-wr'?` · ${esc(e.core)}`:''}</li>`).join('')}</ul>`:'<span class="small">none</span>';
 const by=k=>p.entries.filter(e=>e.action===k||e.kind===k);
 return `<div class="sum"><div class="sb"><b>${S.adds||0}</b><span>games to add</span></div><div class="sb"><b>${S.keeps||0}</b><span>built-in kept</span></div><div class="sb"><b>${S.hides||0}</b><span>built-in hidden</span></div><div class="sb"><b>${S.foreign||0}</b><span>other games kept</span></div><div class="sb"><b>${fmtB(B.proposed_add_bytes)}</b><span>to copy</span></div></div>
  <div class="diff"><div class="d"><b>Added (${by('add').length})</b>${li(by('add'))}</div><div class="d"><b>Hidden built-in (${by('hide').length})</b>${li(by('hide'))}</div><div class="d"><b>Kept as is (${by('keep').length+by('keep-unknown').length})</b>${li(by('keep').concat(by('keep-unknown')))}</div><div class="d"><b>Modules</b>${p.modules.length?`<ul>${p.modules.map(m=>`<li><code>${esc(m.name)}</code> · ${esc(m.action)}${m.cleared?'':' · <b>not cleared for distribution</b>'}</li>`).join('')}</ul>`:'<span class="small">none needed</span>'}</div></div>
  <div class="msg"><b>Storage</b> ${lv(B.source==='measured'?'known':B.source==='simulated'?'estimated':'estimated')} ${esc(B.text||'')}: ${B.free!=null?`${fmtB(B.free)} free`:'free space unknown'}, ${fmtB(B.proposed_add_bytes)} of games to add, ${B.native_home_bytes!=null?`${fmtB(B.native_home_bytes)} HOME menu ${lv('known')} measured`:`HOME menu not prepared yet ${lv('unknown')}`}, ${fmtB(B.margin)} safety margin → <b>${B.fits===true?'Estimated to fit':B.fits===false?'Estimated to exceed available space':'Fit not confirmed'}</b></div>
  ${pf?`<div class="msg ${pf.ok?'ok':'bad'}"><b>Pre-install check: ${esc(pf.state||'')}</b>${pf.blocks.length?`<ul>${pf.blocks.map(b=>`<li>${esc(b.text)}</li>`).join('')}</ul>`:''}${pf.warnings.length?`<ul class="small">${pf.warnings.map(w=>`<li>${esc(w.text)}</li>`).join('')}</ul>`:''}</div>`:''}
  <div class="small">Plan <code>${esc((p.hash||'').slice(0,16))}</code></div>`; }
async function ensurePlan(){ if(CONSOLE.plan&&CONSOLE.plan._profile===CONSOLE.profile) return CONSOLE.plan; const m=INST.manifest||buildManifest(); INST.manifest=m;
 const r=await hkPost('/api/console/plan',{manifest:m, profile:CONSOLE.profile||undefined}); if(!r.ok){ CONSOLE.plan=null; CONSOLE.preflight={ok:false,state:'Plan failed',blocks:[{code:'plan',text:r.error||'The plan could not be built. Try again.'}],warnings:[]}; return null; }
 CONSOLE.plan=r.plan; CONSOLE.plan._profile=CONSOLE.profile; CONSOLE.preflight=r.preflight; return CONSOLE.plan; }
function stopPoll(){ clearInterval(CONSOLE.timer); CONSOLE.timer=null; }
// ---- native HOME package (N2): the console's native HOME built just in time from this collection and its HOME settings
// (theme, order, titles, folders, favorites), staged on this computer only. Nothing is sent to a console (Gate G1). ----
const NHP={job:null,timer:null,progress:null,result:null,fp:null,apply:false};
const NH_CAT=[['executable','HOME menu program'],['shared','Shared artwork, icons and theme thumbnails'],['theme-arcade-grid','Theme: Arcade Grid'],['theme-box-art-showcase','Theme: Box Art Showcase'],['theme-featured-shelf','Theme: Featured Shelf'],['theme-adventure-map','Theme: Adventure Map'],
 ['game-covers','Game covers (one per game, all themes)'],['game-details-artwork','Game Details backdrops'],['game-theme-artwork','Featured Shelf key art'],['folder-artwork','Folder artwork'],['font','Fonts and licenses'],['catalog','Game list'],['descriptions','Descriptions'],['configuration','Settings'],['other','Other']];
// the console's initial HOME theme (N3 addendum): chosen here, stored with the HOME settings, packaged as the default; the console
// keeps the player's own later choice unless "apply at the next installation" is ticked for a build
function nhTheme(){ try{ return homeModelFromStudio().theme; }catch(e){ return 'arcade-grid'; } }
function nhSetTheme(t){ try{ setTimeout(()=>dispatchEvent(new CustomEvent('ccs-content-changed',{detail:{what:'theme',theme:t}})),0); }catch(e){} try{ const ex=homeExtrasLoad(); ex.theme=t; localStorage.setItem(HOME_KEY,JSON.stringify(ex)); if(typeof TV!=='undefined'){ TV.proj=null; if(TV.model) TV.model.theme=t; } }catch(e){} nhDraw(); }
function nhThemeHtml(){ const cur=nhTheme(); return `<div class="row" style="display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin-top:6px"><label class="small" for="nhThemeSel"><b>Starting HOME theme</b></label>
  <select id="nhThemeSel">${HOME_THEME_META.map(t=>`<option value="${t.id}" ${t.id===cur?'selected':''}>${esc(t.name)}</option>`).join('')}</select>
  <label class="small" style="display:inline-flex;gap:6px;align-items:center"><input type="checkbox" id="nhApply" ${NHP.apply?'checked':''}> Also apply it at the next installation</label></div>
  <span class="small">All four themes are included; players switch with START › Change Theme. After an update, the console keeps the player’s choice unless this box is checked.</span>`; }
function nhHome(){ const m=homeModelFromStudio(); return {schema:m.schema, theme:m.theme, order:m.order, games:(m.games||[]).map(g=>({id:g.id,title:g.title,version_label:g.version_label,kind:g.kind})),
 folders:(m.folders||[]).map(f=>({id:f.id,name:f.name,parent:f.parent||null,order:f.order||[],art:f.art&&f.art.src?{src:f.art.src}:null})), favorites:m.favorites||{}, settings:m.settings||{}, sample:!!m.sample, map_shortcuts:m.map_shortcuts||[null,null], apply_theme:!!NHP.apply}; }
function nhFp(){ try{ const m=homeModelFromStudio(); return collFingerprint()+'|'+m.theme+'|'+(NHP.apply?'apply':'')+'|'+JSON.stringify(m.map_shortcuts||[])+'|'+(typeof OV!=='undefined'&&OV&&OV.stamp?OV.stamp:'')+'|'+JSON.stringify((m.folders||[]).map(f=>[f.id,f.name,f.order]))+'|'+JSON.stringify(m.favorites&&m.favorites.items||{}); }catch(e){ return ''; } }
function nhHtml(){ const r=NHP.result; const stale=r&&NHP.fp!==nhFp();
 const head=`<b>Console HOME menu</b> <span class="small">· built from this collection and its HOME Setup</span>`+nhThemeHtml();
 if(NHP.job&&!r){ const p=NHP.progress||{}; const pct=p.total?Math.round(100*p.done/p.total):0;
  return `<div class="msg sim">${head}<div class="pbar"><i style="width:${pct}%"></i></div><span class="small">Preparing: ${esc(p.stage||'starting')}${p.text?' · '+esc(p.text):''}${p.total?` (${p.done} of ${p.total})`:''}</span></div>`; }
 const btn=`<div class="acts" style="margin-top:6px"><span class="sp"></span><button class="btn sm ${r&&!stale?'secondary':'hk'}" id="nhBuild">${r?'Prepare Again':'Prepare HOME Menu'}</button></div>`;
 if(!r) return `<div class="msg">${head}<br><span class="small">Not prepared yet. Preparing builds it on this computer and measures its size; nothing is sent to the console.</span>${btn}</div>`;
 if(!r.ok) return `<div class="msg bad">${head}<br><b>Not prepared:</b> ${esc(r.error||r.status||'failed')}${btn}</div>`;
 const m=r.manifest||{}; const S=m.sizes||{}; const by=S.by_category||{}; const ready=r.ready&&!stale;
 const rows=NH_CAT.map(([k,l])=>`<tr><td>${esc(l)}</td><td style="text-align:right">${fmtB(by[k]||0)}</td></tr>`).join('');
 return `<div class="msg ${ready?'ok':'warn'}">${head}<br><b>${stale?'Out of date: the collection or HOME Setup changed. Prepare again.':ready?'Ready':'Not ready'}</b>${ready?` · ${r.games} games · <b>${fmtB(S.total_with_manifest||S.total)}</b> · ${r.files} files checked`:''}
  ${(m.blockers||[]).length?`<ul>${m.blockers.map(b=>`<li>${esc(b.text)}</li>`).join('')}</ul>`:''}${(m.warnings||[]).length?`<ul class="small">${m.warnings.map(w=>`<li>${esc(w)}</li>`).join('')}</ul>`:''}
  <details class="small" style="margin-top:6px"><summary>Size Details</summary><table class="small" style="margin-top:6px;min-width:300px"><tbody>${rows}<tr><td><b>Total</b> (ROM files not included)</td><td style="text-align:right"><b>${fmtB(S.total_with_manifest||S.total)}</b></td></tr></tbody></table></details>
  <span class="small">Starting theme: ${esc((HOME_THEME_META.find(t=>t.id===((m.configuration||{}).theme_default))||{}).name||'')}${(m.configuration||{}).theme_apply?' (applied at installation)':''}. Prepared on this computer; installing it on the console is not available in this version.</span>${btn}</div>`; }
function nhDraw(){ const el=$('nhPkg'); if(!el){ clearInterval(NHP.timer); NHP.timer=null; return; } el.innerHTML=nhHtml(); const b=$('nhBuild'); if(b) b.onclick=nhStart;
 const ts=$('nhThemeSel'); if(ts) ts.onchange=()=>nhSetTheme(ts.value); const ap=$('nhApply'); if(ap) ap.onchange=()=>{ NHP.apply=ap.checked; nhDraw(); }; }
async function nhStart(){ const b=$('nhBuild'); if(b) b.disabled=true; NHP.result=null; NHP.progress=null; NHP.fp=nhFp();
 let r; try{ r=await hkPost('/api/home/native-package',{manifest:INST.manifest||buildManifest(), home:nhHome()}); }catch(e){ r={ok:false,error:String(e)}; }
 if(!r.ok&&!r.busy){ NHP.job=null; NHP.result={ok:false,status:'failed',error:r.error||'Could not start. Try again.'}; nhDraw(); return; }
 NHP.job=r.job; nhDraw(); clearInterval(NHP.timer);
 NHP.timer=setInterval(async()=>{ if(!$('nhPkg')){ clearInterval(NHP.timer); NHP.timer=null; return; } try{ const q=await capi(`/api/home/native-package?job=${encodeURIComponent(NHP.job)}`); if(!q.ok){ clearInterval(NHP.timer); return; }
  NHP.progress=q.progress; if(q.state==='done'){ clearInterval(NHP.timer); NHP.timer=null; NHP.result=q.result; NHP.job=null; } nhDraw(); }catch(e){} },300); }
async function renderConsoleStep(){ const body=$('hkBody'); const step=INST.step; $('hkTitle').textContent='Install on NES Classic';
 if(!consoleCapable()){ $('hkSub').textContent=CSTEP[step]||''; body.innerHTML=stepperHtml()+`<div class="msg warn"><b>These steps need the latest Classic Cart Studio for Windows.</b> ${COMPANION.desktop?'This version does not include them: install the latest version from <a href="https://ClassicCartStudio.com/app">ClassicCartStudio.com/app</a>.':'Use <b>Open in Windows App</b>.'}</div><div class="acts"><button class="btn secondary" id="hkBack">Back</button><span class="sp"></span><button class="btn secondary" id="hkDone">Close</button></div>`;
  $('hkDone').onclick=()=>$('hkpanel').classList.remove('open'); $('hkBack').onclick=()=>{ INST.step='connect'; renderInstall(); }; return; }
 await consoleCaps(); const nav=(back,next,nextLabel,nextOk)=>`<div class="acts"><button class="btn secondary" id="hkBack">Back</button><span class="sp"></span><button class="btn secondary" id="hkDone">Close</button>${next?`<button class="btn hk" id="hkNext" ${nextOk?'':'disabled'}>${esc(nextLabel)}</button>`:''}</div>`;
 const wire=(back,next)=>{ $('hkDone').onclick=()=>{ stopPoll(); $('hkpanel').classList.remove('open'); }; $('hkBack').onclick=()=>{ stopPoll(); INST.step=back; renderInstall(); }; if($('hkNext')) $('hkNext').onclick=()=>{ stopPoll(); INST.step=next; renderInstall(); }; };
 if(step==='inspect'){ $('hkSub').textContent='Console state (simulated)'; const draw=async()=>{ if(CONSOLE.profile&&!CONSOLE.device){ const r=await hkPost('/api/console/identify',{profile:CONSOLE.profile}); CONSOLE.device=r.ok?r.device:{state:'Console inspection unavailable',detail:r.error||'',state_code:''}; }
   body.innerHTML=stepperHtml()+profileSelect()+deviceHtml(CONSOLE.device)+`<div class="msg na"><b>Real consoles:</b> reading a real console is not available in this version. The states shown here are the ones a real check would use.</div>`+nav('connect','review','Next: Review Changes',!!CONSOLE.device||!CONSOLE.profile);
   wireProfile(draw); wire('connect','review'); }; await draw(); return; }
 if(step==='review'){ $('hkSub').textContent='What installing would change'; body.innerHTML=stepperHtml()+'<div class="msg">Building the plan…</div>';
  const p=await ensurePlan(); const pf=CONSOLE.preflight;
  body.innerHTML=stepperHtml()+(CONSOLE.profile?`<div class="msg sim"><b>Reviewing against the simulated console</b> ${simBadge()} <span class="small">(${esc((CONSOLE.profiles.find(x=>x.name===CONSOLE.profile)||{}).label||CONSOLE.profile)})</span></div>`:`<div class="msg na"><b>No console chosen:</b> compared with a standard NES Classic (storage estimated). To rehearse, choose a simulated console in the previous step.</div>`)
   +planHtml(p,pf)+`<div id="nhPkg" style="margin-top:8px"></div><div class="small" style="margin-top:8px">On this computer: ${INST.prepared?'prepared for Hakchi2 CE':'not prepared yet (step 2)'}.</div>`+nav('inspect','install','Next: Install',!!p);
  wire('inspect','install'); nhDraw(); return; }
 if(step==='install'){ $('hkSub').textContent='Install'; const p=await ensurePlan(); const pf=CONSOLE.preflight||{ok:false,blocks:[],warnings:[]}; const canSim=!!(p&&pf.ok&&CONSOLE.profile);
  body.innerHTML=stepperHtml()+`<div class="msg bad"><b>Installing on your NES Classic is not available in this version.</b> This version cannot write to a console; you can rehearse the installation on a simulated console instead.</div>
   <div class="msg ${canSim?'ok':'na'}"><b>Rehearse Installation</b> ${simBadge()}<br><span class="small">Runs every installation step against the simulated console, with real progress, cancel and error handling. Nothing physical happens. ${canSim?'':(!CONSOLE.profile?'Choose a simulated console in Console State first.':'The pre-install check found problems; see Review Changes.')}</span></div>
   ${pf.blocks&&pf.blocks.length?`<div class="msg warn"><b>Blocked:</b><ul>${pf.blocks.map(b=>`<li>${esc(b.text)}</li>`).join('')}</ul></div>`:''}
   <div class="acts"><button class="btn secondary" id="hkBack">Back</button><span class="sp"></span><button class="btn secondary" id="hkDone">Close</button><button class="btn" id="hkPhys" disabled title="${esc(GATE_TEXT)}">Install on NES Classic</button><button class="btn hk" id="hkSim" ${canSim?'':'disabled'}>Rehearse Installation</button></div>
   <div id="cProgress"></div>`;
  wire('review','verify'); $('hkPhys').onclick=null;
  $('hkSim').onclick=async()=>{ $('hkSim').disabled=true; const r=await hkPost('/api/console/simulate',{plan_hash:p.hash, profile:CONSOLE.profile}); if(!r.ok){ $('cProgress').innerHTML=`<div class="msg bad">${esc(r.error||'Could not start the rehearsal. Try again.')}</div>`; $('hkSim').disabled=false; return; }
   CONSOLE.run=r.run; CONSOLE.cursor=0; CONSOLE.events=[]; CONSOLE.result=null; $('hkSub').textContent='Rehearsing (simulation)…';
   const bar=()=>{ const last=[...CONSOLE.events].reverse().find(e=>e.event==='progress'); const stages=CONSOLE.events.filter(e=>e.event==='stage').map(e=>e.stage); const pct=last&&last.total?Math.round(100*last.done/last.total):0;
     $('cProgress').innerHTML=`<div class="msg sim"><b>Rehearsal in progress</b> ${simBadge()} · stages: ${esc(stages.join(' → ')||'starting')}<div class="pbar"><i style="width:${pct}%"></i></div><span class="small">${last?`${esc(last.stage)}: ${fmtB(last.done)} of ${fmtB(last.total)}${last.detail?' · '+esc(last.detail):''}`:'Starting…'}</span><div class="acts" style="margin-top:6px"><span class="sp"></span><button class="btn sm secondary" id="cCancel" title="Stops at the next safe point">Cancel Safely</button></div></div>`;
     $('cCancel').onclick=async()=>{ $('cCancel').disabled=true; await hkPost('/api/console/cancel',{run:CONSOLE.run}); }; };
   bar(); stopPoll(); CONSOLE.timer=setInterval(async()=>{ try{ const q=await capi(`/api/console/run?id=${encodeURIComponent(CONSOLE.run)}&cursor=${CONSOLE.cursor}`); if(!q.ok){ stopPoll(); return; } CONSOLE.events=CONSOLE.events.concat(q.events||[]); CONSOLE.cursor=q.cursor; bar();
     if(q.done){ stopPoll(); CONSOLE.result=q.result; INST.step='verify'; renderInstall(); } }catch(e){} },400); };
  return; }
 if(step==='verify'){ const res=CONSOLE.result; const r=res&&res.event==='result'?res.result:null; const err=res&&res.event==='error'?res:null; $('hkSub').textContent='Verify (simulated)';
  let h='';
  if(!res) h=`<div class="msg na"><b>Nothing to verify yet.</b> Rehearse the installation first.</div>`;
  else if(err) h=`<div class="msg bad"><b>The rehearsal failed:</b> ${esc(err.text||err.code||'')}</div>`;
  else { const v=r.verify||{}; const e=r.error; const cls=r.state&&r.state.startsWith('Simulated install succeeded')?'ok':r.state&&r.state.startsWith('Cancelled')?'warn':e?'bad':'warn';
   h=`<div class="msg ${cls}"><b>${esc(r.state||'')}</b> ${simBadge()}${r.noop?'<br>Nothing needed to change.':''}${e?`<br>${esc(e.text||'')}<br><b>What to do:</b> ${esc(e.recovery||'')}<details class="small"><summary>Technical Details</summary>${esc(e.code)} at stage <code>${esc(e.stage||'')}</code></details>`:''}</div>
    ${r.verify?`<div class="msg"><dl class="tvkv"><dt>Read-back</dt><dd>${v.read_back_files} files on the simulated console, ${v.expected_files} expected → <b>${v.match?'match':'MISMATCH'}</b> ${lv('known')} <span class="small">(simulated)</span></dd><dt>Modules</dt><dd>${v.modules_present?'all required modules present':'missing modules'}</dd><dt>Hidden built-in</dt><dd>${(v.hidden_config||[]).length} in the console configuration</dd>${v.missing&&v.missing.length?`<dt>Missing</dt><dd>${esc(v.missing.join(', '))}</dd>`:''}${v.extra&&v.extra.length?`<dt>Unexpected</dt><dd>${esc(v.extra.join(', '))}</dd>`:''}</dl></div>`:''}
    <div class="msg na"><b>This was a simulation,</b> not a physical install. Nothing was written to your NES Classic.</div>`; }
  body.innerHTML=stepperHtml()+h+nav('install','ready','Next: Complete',!!r&&!err); wire('install','ready'); return; }
 if(step==='ready'){ const r=CONSOLE.result&&CONSOLE.result.event==='result'?CONSOLE.result.result:null; $('hkSub').textContent='Complete (simulated)';
  body.innerHTML=stepperHtml()+(r&&String(r.state).startsWith('Simulated install succeeded')?`<div class="msg ok"><b>Rehearsal complete.</b> ${simBadge()} Every step worked on the simulated console. Nothing was installed on a physical NES Classic.</div>`:`<div class="msg na"><b>Not complete.</b> The rehearsal did not finish. Go back to Verify to see why and what to do.</div>`)
   +`<div class="msg"><b>Meanwhile</b><ul><li>Keep editing; the plan and Preview on TV follow the collection.</li><li>Prepare Games (step 2) still prepares the collection for Hakchi2 CE.</li><li>Export the collection (.ccs) to keep a copy.</li></ul></div>`+nav('verify',null,'',false);
  wire('verify',null); return; }
}
(function(){ const _r=renderInstall; window.renderInstallBase=_r; renderInstall=async function(){ if(consoleCapable()&&['inspect','review','install','verify','ready'].includes(INST.step)) return renderConsoleStep(); if(['inspect','install','verify','ready'].includes(INST.step)) return renderConsoleStep(); return _r.apply(this,arguments); };
 try{ const lab={inspect:'Console State', ready:'Complete'}; STEPS.forEach(s=>{ if(lab[s[0]]) s[1]=lab[s[0]]; }); }catch(e){}
 const _s=stepperHtml; stepperHtml=function(){ const h=_s(); if(!consoleCapable()) return h; return h.replace(/ na"/g,'"').replace(/<br><span class="small">not in this version<\/span>/g,'<br><span class="small">simulated</span>'); };
 // the Connect Console step hands over to Console State instead of jumping straight to the review
 document.addEventListener('click',e=>{ const b=e.target.closest&&e.target.closest('#hkNext'); if(!b||INST.step!=='connect'||!consoleCapable()) return; e.stopImmediatePropagation(); e.preventDefault(); clearInterval(INST.pollTimer); INST.step='inspect'; renderInstall(); },true);
})();
