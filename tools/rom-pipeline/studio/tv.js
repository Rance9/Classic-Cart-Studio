// ---- "As Seen on TV": Preview on TV, now the console HOME runtime (four approved themes) over the user's own collection ------
// Two layers, kept apart on purpose:
//   1. tvProject(): a pure projection of the current collection into "proposed menu entries" (what the plan would show, with
//      known / estimated / unknown labels). It reuses the collection rules (installClass, factoryVisible, decisions, installRequired,
//      needsHardware). It feeds homeBuild(): the HOME model (home-model.js) that all four themes render.
//   2. The HOME runtime: home-state.js (original-controller state machine) + home-themes.js (renderers). The preview hosts it in
//      the same small modal the toolbar badge opens; host keys and the on-screen pad are emulations of D-pad, A, B, START, SELECT.
// Nothing here talks to a console, scans the computer or uploads anything. A launch is SIMULATED and labeled as such.
const TV_KEY='studio-tv-v1', HOME_KEY='studio-home-v1';
const TV_SAMPLE_NOTICE='Your tray is empty — this is a sample of the 30 built-in games the NES Classic ships with. Select games to preview your own menu.';
let TV={open:false,mode:'proposed',treatment:'clean',proj:null,returnTo:null,focusId:null,model:null,S:null,held:null,clock:0,repeatTimer:0,launching:null};
function tvSortTitle(t){ t=String(t||'').toLowerCase().trim(); return t.startsWith('the ')?t.slice(4):t; }
function tvFactoryTitle(code){ return (typeof FACTORY_TITLES!=='undefined'&&FACTORY_TITLES[code])||code; }
// sample entries when the tray is empty: the 30 built-in games in their factory versions
function tvSampleEntries(){ return [...FACTORY_CODES].sort().map(cd=>{ const g=(DATA.games||[]).find(x=>x.builtin_code===cd&&x.builtin_status==='BUILT-IN: EXACT MATCH'); const title=tvFactoryTitle(cd); return {id:g?g.game_id:'factory:'+cd,title_id:g?g.title_id:'factory:'+cd,title,version:'factory version',kind:'builtin',core:'clover-kachikachi-wr',ready:'n/a',art:g?g.art||'':'',players:g?g.players||'':'',publisher:g?g.publisher||'':'',year:g?g.year||'':'',genre:g?g.genre||'':'',hardware:'',install:false,personal:false,ext:false,sort:tvSortTitle(title),builtin_code:cd}; }); }
function tvEntryFor(g){
 const c=installClass(g); const dec=decisionOf(g); const alt=(c==='revision'||c==='review')&&dec==='alternate';
 const kind=c==='builtin'?'builtin':(c==='revision'||c==='review')?(alt?'alternate':'factory-kept'):'added';
 const title=kind==='factory-kept'?(g.builtin_title||tvFactoryTitle(g.builtin_code)||g.display_title):g.display_title;
 const version=kind==='factory-kept'?(g.builtin_factory_version||'factory version'):(g.source==='personal'?(g.rev||'personal game'):(g.nointro_name||g.display_title));
 const core=g.recommended_emulator||'unknown';
 const ready=(typeof COMPANION!=='undefined'&&COMPANION.ok&&installRequired(g))?(fileAvailable(g.game_id)?'ready':'missing'):(installRequired(g)?'unknown':'n/a');
 return {id:g.game_id,title_id:g.title_id,title,version,kind,core,ready,art:g.art||'',players:g.players||'',publisher:g.publisher||'',year:g.year||'',genre:g.genre||'',hardware:needsHardware(g)?(g.input_special_accessory||'special input hardware'):'',install:installRequired(g),personal:g.source==='personal',ext:!!g.ext,sort:tvSortTitle(title),builtin_code:g.builtin_code||null};
}
// The projection. mode 'proposed' = the menu the plan proposes (selected games; unselected built-ins hidden by the plan). Deterministic; memoized by collection fingerprint + mode + backend state.
function tvProject(mode){
 mode=mode||'proposed'; const fp=collFingerprint()+'|'+mode+'|'+((typeof COMPANION!=='undefined'&&COMPANION.ok)?'desk':'web');
 if(TV.proj&&TV.proj.fp===fp) return TV.proj;
 const chosen=selectedGames(); const sample=chosen.length===0; const entries=sample?tvSampleEntries():chosen.map(tvEntryFor); entries.sort((a,b)=>a.sort<b.sort?-1:a.sort>b.sort?1:(a.id<b.id?-1:1));
 const hiddenCodes=sample?[]:FACTORY_CODES.filter(cd=>!chosen.some(g=>g.builtin_code===cd&&factoryVisible(g)));
 const assumptions=sample?[
  {level:'known',text:'Your tray is empty, so this is a sample: the 30 built-in games every NES Classic ships with, in their factory versions. Nothing from your collection is shown. Select games in the library and the preview shows your own HOME instead.'},
  {level:'unknown',text:'What is on the physical console right now is not verified by this preview.'}]:[
  {level:'known',text:'Which games are in the plan, their exact versions and which built-in games the plan keeps come from your collection.'},
  {level:'known',text:'All Games opens in your collection order; a sort chosen in the HOME is view-only and never changes that order.'},
  {level:'estimated',text:`Unselected built-in games (${hiddenCodes.length}) are hidden by the plan; hiding is applied during preparation on this computer, not by the console itself.`},
  {level:(typeof COMPANION!=='undefined'&&COMPANION.ok)?'known':'unknown',text:(typeof COMPANION!=='undefined'&&COMPANION.ok)?'Game file readiness comes from the Studio on this computer.':'Game files: unknown here (the website cannot see your computer); the desktop app shows which files are ready.'},
  {level:'unknown',text:'What is on the physical console right now is not verified by this preview. Launching here is simulated; no game runs in the browser.'}];
 const proj={fp,mode,sample,entries,count:entries.length,hidden:hiddenCodes.map(cd=>({code:cd,title:tvFactoryTitle(cd)})),assumptions,
  counts:{builtin:entries.filter(e=>e.kind==='builtin').length,kept:entries.filter(e=>e.kind==='factory-kept').length,added:entries.filter(e=>e.kind==='added').length,alternate:entries.filter(e=>e.kind==='alternate').length,missing:entries.filter(e=>e.ready==='missing').length,ready:entries.filter(e=>e.ready==='ready').length,hardware:entries.filter(e=>e.hardware).length}};
 TV.proj=proj; return proj;
}
function tvEntries(){ return tvProject(TV.mode).entries.slice(); }

// ---- HOME model from the projection (one collection model; folders, favorites, history, sorts and theme persist beside it) ----
function homeExtrasLoad(){ try{ return JSON.parse(localStorage.getItem(HOME_KEY)||'{}')||{}; }catch(e){ return {}; } }
function homeExtrasSave(){ if(!TV.model) return; const m=TV.model; try{ localStorage.setItem(HOME_KEY,JSON.stringify({v:1,folders:m.folders,favorites:m.favorites,history:m.history,map_shortcuts:m.map_shortcuts,sorts:m.sorts,theme:m.theme,settings:m.settings,state:TV.S})); }catch(e){} }
function homeModelFromStudio(){
 const p=tvProject(TV.mode); const prev=homeExtrasLoad(); const sels=p.entries.map(e=>({game_id:e.id,title_id:e.title_id,display_title:e.title,nointro_name:e.version==='factory version'?'':e.version,builtin_code:e.builtin_code,install_required:e.install,source:e.personal?'personal':'catalog',art:e.art,builtin_factory_version:e.version==='factory version'?'factory':null}));
 const readiness={}; let known=false; p.entries.forEach(e=>{ if(e.ready==='ready'||e.ready==='missing'){ readiness[e.id]=e.ready; known=true; } });
 const generated={}; p.entries.forEach(e=>{ const g=byId[e.id]; if(g&&g.art_generated) generated[e.id]=g.art_generated; });
 const m=homeBuild({schema:'nes-classic-collection/4',name:p.sample?'Sample: the 30 built-in games':COLL.name,selections:sels},byId,prev,{readiness:known?readiness:undefined,generated});
 m.sample=p.sample; return m;
}

// ---- markup (created once) ----
function tvEnsure(){
 if($('tv')) return;
 const m=document.createElement('div'); m.id='tv'; m.className='tvmodal'; m.setAttribute('role','dialog'); m.setAttribute('aria-modal','true'); m.setAttribute('aria-labelledby','tvTitle'); m.hidden=true;
 m.innerHTML=`<div class="tvbox">
  <div class="tvhd"><div class="tvt"><div><h2 id="tvTitle">Preview on TV</h2><span class="small" id="tvSub"></span></div></div>
   <div class="tvtools"><div class="seg" role="group" aria-label="Theme (host shortcut; on the console: START, Change Theme)">${HOME_THEME_META.map(t=>`<button class="btn sm tvtheme" data-theme="${t.id}" title="${esc(t.note)}">${esc(t.name)}</button>`).join('')}</div>
   <div class="seg" role="group" aria-label="Display treatment"><button class="btn sm active" id="tvTrC" data-tr="clean">Clean</button><button class="btn sm" id="tvTrR" data-tr="retro" title="A cosmetic CRT-style look around the same 16:9 HOME">Retro</button></div>
   <button class="btn sm" id="tvSave" title="Save the simulated TV surface as a 1920×1080 PNG">Save Preview Image</button><button class="btn sm ghost" id="tvClose" aria-label="Close the preview">Close</button></div></div>
  <div class="tvmain">
   <div class="tvstagewrap"><div class="tvstage" id="tvStage" tabindex="0" aria-label="Simulated NES Classic HOME, 16 by 9" aria-describedby="tvLive"><div class="hmwrap" id="tvHome"></div><div class="tvsim" id="tvSim" hidden></div><div class="tvcrt" aria-hidden="true"></div></div>
    <div class="tvpad" id="tvPad" aria-label="On-screen controls (the console has only these)"><div class="dpad"><button class="pb up" data-act="up" aria-label="Up">▲</button><button class="pb left" data-act="left" aria-label="Left">◀</button><button class="pb right" data-act="right" aria-label="Right">▶</button><button class="pb down" data-act="down" aria-label="Down">▼</button></div>
     <div class="ssh"><button class="pb sel" data-act="select" aria-label="SELECT, game details">SELECT</button><button class="pb start" data-act="start" aria-label="START, main menu">START</button></div>
     <div class="abh"><button class="pb b" data-act="b" aria-label="B, back">B</button><button class="pb a" data-act="a" aria-label="A, launch or choose">A</button></div>
     <div class="small tvhint">Arrows D-pad · Enter / Z = A · Backspace / X = B · S = SELECT · M = START · hold a key to repeat · Esc closes</div></div>
   </div>
   <aside class="tvside" id="tvSide"></aside>
  </div>
  <div class="tvfoot" id="tvFoot"></div>
  <div class="vis-hidden" id="tvLive" aria-live="polite"></div>
 </div>`;
 document.body.appendChild(m);
 $('tvClose').onclick=closeTv; $('tvSave').onclick=tvSaveImage;
 $('tvTrC').onclick=()=>tvSetTreatment('clean'); $('tvTrR').onclick=()=>tvSetTreatment('retro');
 m.querySelectorAll('.tvtheme').forEach(b=>{ b.onclick=()=>{ tvApplyEffects([{type:'theme',id:b.dataset.theme},{type:'persist'}]); TV.S.theme=b.dataset.theme; tvDraw(); $('tvStage').focus({preventScroll:true}); }; });
 const pad=$('tvPad'); pad.addEventListener('pointerdown',e=>{ const b=e.target.closest('[data-act]'); if(!b) return; e.preventDefault(); tvPress(b.dataset.act); });
 ['pointerup','pointercancel','pointerleave'].forEach(ev=>pad.addEventListener(ev,()=>tvReleaseAll()));
 $('tvHome').addEventListener('click',e=>{ const t=(e.composedPath?e.composedPath():[e.target]).find(x=>x&&x.dataset&&x.dataset.id); if(!t||!TV.S||TV.S.overlay) return; const L=homeListing(TV.model,TV.S,tvSearch()); const i=L.ids.indexOf(t.dataset.id); if(i<0) return; if(TV.S.zone==='games'&&i===TV.S.index) tvAct('a'); else { TV.S.index=i; TV.S.zone='games'; tvDraw(); } });
 m.addEventListener('keydown',tvKey); m.addEventListener('keyup',tvKeyUp); m.addEventListener('click',e=>{ if(e.target===m) closeTv(); });
 if(window.ResizeObserver) new ResizeObserver(()=>homeFit($('tvHome'))).observe($('tvStage'));
 try{ const s=JSON.parse(localStorage.getItem(TV_KEY)||'{}'); if(s.treatment) TV.treatment=s.treatment; }catch(e){}
}
function tvPersist(){ try{ localStorage.setItem(TV_KEY,JSON.stringify({treatment:TV.treatment})); }catch(e){} }
function tvSearch(){ return {searchQuery,searchFuzzy,searchScore,searchNorm}; }
function tvCtx(){ return {search:tvSearch(),assetBase:'brand/home/',clock:()=>{ const d=new Date(); return {time:d.toLocaleTimeString([], {hour:'numeric',minute:'2-digit'}),date:d.toLocaleDateString([], {weekday:'short',month:'short',day:'numeric'}).toUpperCase()}; }}; }

// ---- open / close ----
function openTv(opts){
 opts=opts||{}; tvEnsure(); TV.returnTo=document.activeElement; TV.mode='proposed'; TV.open=true; TV.proj=null;
 TV.model=homeModelFromStudio(); const ex=homeExtrasLoad(); TV.S=ex.state&&ex.state.v===1?ex.state:homeInitialState(TV.model.theme); TV.S.theme=TV.model.theme; TV.S.overlay=null;
 if(opts.focus){ TV.S.section={type:'allgames'}; TV.S.zone='games'; const L=homeListing(TV.model,TV.S,tvSearch()); const k=L.ids.indexOf(opts.focus); if(k>=0) TV.S.index=k; }
 if(opts.theme&&HOME_THEMES.includes(opts.theme)){ TV.model.theme=opts.theme; TV.S.theme=opts.theme; }
 TV.S=homeReconcileState(TV.S,TV.model,tvSearch());
 const m=$('tv'); m.hidden=false; m.classList.add('open'); document.body.classList.add('tv-open'); document.body.style.overflow='hidden';
 tvApplyTreatment(); tvDraw(); $('tvStage').focus({preventScroll:true}); if(!TV.clock) TV.clock=setInterval(()=>{ if(TV.open&&!TV.held) tvDraw(true); },30000);
 if(opts.focus&&!TV.model.order.includes(opts.focus)){ const g=byId[opts.focus]; tvSay(`${g?g.display_title:'That game'} is not in the proposed HOME: it is not selected.`); $('tvSub').textContent=`${g?g.display_title:'That game'} is not part of the proposed HOME (not selected)`; }
 if(typeof hoLog==='function') hoLog('tv open n='+TV.model.order.length+' theme='+TV.S.theme);
}
function closeTv(){ const m=$('tv'); if(!m||!TV.open) return; tvReleaseAll(); TV.open=false; homeExtrasSave(); m.classList.remove('open'); m.hidden=true; document.body.classList.remove('tv-open'); document.body.style.overflow=''; const r=TV.returnTo; TV.returnTo=null; try{ if(r&&r.focus) r.focus({preventScroll:true}); }catch(e){} }
function tvSetMode(mode){ tvDraw(); }
function tvSetTreatment(t){ TV.treatment=t; tvPersist(); tvApplyTreatment(); }
function tvApplyTreatment(){ const st=$('tvStage'); if(!st) return; st.classList.toggle('retro',TV.treatment==='retro'); $('tvTrC').classList.toggle('active',TV.treatment==='clean'); $('tvTrR').classList.toggle('active',TV.treatment==='retro'); }
function tvSay(t){ const l=$('tvLive'); if(l) l.textContent=t; }

// ---- input: host keys and the on-screen pad are emulations of the five console controls; held buttons repeat with measured acceleration ----
const TV_KEYMAP={ArrowUp:'up',ArrowDown:'down',ArrowLeft:'left',ArrowRight:'right',Enter:'a',z:'a',Z:'a',' ':'a',Backspace:'b',x:'b',X:'b',s:'select',S:'select',m:'start',M:'start'};
function tvPress(act){ if(!TV.open||TV.launching) return; if(TV.held&&TV.held.act===act) return; tvReleaseAll(); TV.held={act,t0:performance.now()}; tvAct(act); if(['up','down','left','right'].includes(act)){ const st=TV.S; homeRelease(st); homeRepeatStep(st,act,0); TV.repeatTimer=setInterval(()=>{ if(!TV.held) return; const now=performance.now()-TV.held.t0; if(homeRepeatStep(TV.S,act,now)) tvAct(act); },40); } }
function tvReleaseAll(){ if(TV.repeatTimer){ clearInterval(TV.repeatTimer); TV.repeatTimer=0; } if(TV.S) homeRelease(TV.S); TV.held=null; }
function tvKey(e){
 const t=e.target; if(t&&t.matches&&t.matches('input,textarea,select')) return;
 if(e.key==='Escape'){ e.preventDefault(); closeTv(); return; }
 if(e.key==='Tab'){ tvTrapTab(e); return; }
 const act=TV_KEYMAP[e.key]; if(!act) return; if(t&&t.closest&&t.closest('.tvtools,.tvside')&&(e.key==='Enter'||e.key===' ')) return;
 e.preventDefault(); if(e.repeat) return; tvPress(act);
}
function tvKeyUp(e){ const act=TV_KEYMAP[e.key]; if(act&&TV.held&&TV.held.act===act) tvReleaseAll(); }
function tvTrapTab(e){ const m=$('tv'); const f=[...m.querySelectorAll('button:not([disabled]),[tabindex="0"],select,input,a[href]')].filter(x=>x.offsetParent!==null); if(!f.length) return; const first=f[0], last=f[f.length-1]; if(e.shiftKey&&document.activeElement===first){ e.preventDefault(); last.focus(); } else if(!e.shiftKey&&document.activeElement===last){ e.preventDefault(); first.focus(); } }

// ---- the state machine drives everything; effects are applied here ----
function tvAct(act){ if(!TV.open||!TV.model) return; if(TV.launching){ tvLaunchDone(); return; } const r=homeStep(TV.S,TV.model,act,{search:tvSearch(),geometry:homeGeometry(TV.S.theme,TV.model)}); TV.S=r.state; tvApplyEffects(r.effects); TV.S=homeReconcileState(TV.S,TV.model,tvSearch()); tvDraw(); }
function tvApplyEffects(fx){ let persist=false; for(const f of fx){
 if(f.type==='launch') tvLaunchSim(f.id);
 else if(f.type==='favorite') homeSetFavorite(TV.model,f.id,f.on);
 else if(f.type==='sort') homeSetSort(TV.model,f.location,f.sort);
 else if(f.type==='theme'){ TV.model.theme=f.id; }
 else if(f.type==='setting'){ TV.model.settings[f.key]=f.value; }
 else if(f.type==='clear-history') homeClearHistory(TV.model);
 else if(f.type==='say') tvSay(f.text);
 else if(f.type==='persist') persist=true; }
 if(persist) homeExtrasSave(); }
// a launch is simulated: the console would start the game now; here a labeled interstitial shows, the launch is recorded in the history, and the HOME returns to the same spot
function tvLaunchSim(id){ const g=TV.model.games.find(x=>x.id===id); if(!g) return; homeRecordLaunch(TV.model,id); homeExtrasSave(); TV.launching=id; const el=$('tvSim'); el.hidden=false; el.innerHTML=`<div class="box"><div class="k">SIMULATED LAUNCH</div><b>${esc(g.title)}</b><p>On the console the game would start now. Nothing runs in this browser. The launch was recorded in the preview's play history, as the console would record it.</p><span class="small">press any control to return</span></div>`; tvSay(`Simulated launch of ${g.title}. Press any control to return.`); if(TV.simTimer) clearTimeout(TV.simTimer); TV.simTimer=setTimeout(tvLaunchDone,2200); }
function tvLaunchDone(){ if(!TV.launching) return; TV.launching=null; const el=$('tvSim'); if(el){ el.hidden=true; el.innerHTML=''; } if(TV.simTimer){ clearTimeout(TV.simTimer); TV.simTimer=0; } tvDraw(); }

// ---- draw ----
function tvDraw(quiet){
 if(!TV.open||!TV.model||!TV.S) return; const m=TV.model; const S=TV.S; const sub=$('tvSub'); const smp=m.sample;
 sub.textContent=smp?'Tray empty · sample of the 30 built-in games, not your collection · simulation, not a console capture':'Simulation of the HOME this collection proposes · '+(HOME_THEME_META.find(t=>t.id===S.theme)||{}).name+' · not a console capture';
 document.querySelectorAll('.tvtheme').forEach(b=>b.classList.toggle('active',b.dataset.theme===S.theme));
 homeRender($('tvHome'),m,S,tvCtx());
 const L=homeListing(m,S,tvSearch()); const id=S.overlay&&S.overlay.type==='details'?S.overlay.id:(S.section.type==='search'?L.ids[S.resultIndex]:L.ids[S.index]); TV.focusId=id||null;
 tvSide(id?tvEntries().find(e=>e.id===id)||null:null); tvFoot();
 if(!quiet){ const g=id?m.games.find(x=>x.id===id):null; tvSay(S.overlay?`${S.overlay.type} open`:g?`${g.title}, ${S.index+1} of ${L.ids.length}${smp?', sample':''}`:L.empty||'HOME'); }
 tvKeepFocus();
}
function tvKeepFocus(){ const a=document.activeElement; const m=$('tv'); if(!m||!TV.open) return; if(!a||a===document.body||!m.contains(a)||!a.isConnected) { try{ $('tvStage').focus({preventScroll:true}); }catch(e){} } }
function tvKindText(k){ return {builtin:'built-in game, kept as it is',"factory-kept":'built-in game kept in its factory version',added:'added game',alternate:'catalog version chosen over the factory version (needs approval at import)'}[k]||k; }
function tvSide(e){
 const p=tvProject(TV.mode); const s=$('tvSide'); if(!s) return; const m=TV.model, S=TV.S;
 const lab=l=>`<span class="lv ${l}">${l}</span>`; const th=(HOME_THEME_META.find(t=>t.id===S.theme)||{}).name||S.theme;
 let h=p.sample?`<div class="tvs sample"><div class="lbl">Tray is empty</div><b>Sample: the 30 built-in games</b><div class="small">${esc(TV_SAMPLE_NOTICE)}</div></div>`:`<div class="tvs"><div class="lbl">Collection</div><b>${esc(COLL.name)}</b><div class="small">${p.count} game${p.count===1?'':'s'} in the proposed HOME · ${p.counts.builtin+p.counts.kept} built-in kept · ${p.counts.added+p.counts.alternate} added · ${p.hidden.length} built-in hidden by the plan ${lab('known')}</div></div>`;
 h+=`<div class="tvs"><div class="lbl">HOME</div><b>${esc(th)}</b><div class="small">${esc(S.section.type==='folder'?('Folder: '+((homeFolder(m,S.section.id)||{}).name||'')):S.section.type==='category'?('Category: '+((homeCategory(m,S.section.id)||{}).label||'')):S.section.type)} · ${(m.folders||[]).length} folder${(m.folders||[]).length===1?'':'s'} · ${homeFavorites(m).length} favorite${homeFavorites(m).length===1?'':'s'} · ${homeHistoryEvents(m).length} launch${homeHistoryEvents(m).length===1?'':'es'} recorded here ${lab('known')}</div><div class="small">Folders, favorites and the play history shown here live in this browser's preview until the console sync exists. START → Change Theme switches themes; the buttons above are a host shortcut.</div></div>`;
 if(e){ h+=`<div class="tvs"><div class="lbl">Selected</div><b>${esc(e.title)}</b><div class="small">${esc(e.version)}</div>
  <dl class="tvkv"><dt>Status</dt><dd>${esc(tvKindText(e.kind))} ${lab('known')}</dd>
  <dt>Game file</dt><dd>${e.ready==='n/a'?'not needed (already on the console)':e.ready==='ready'?'ready on this computer':e.ready==='missing'?'missing on this computer (launch refused in the HOME)':'unknown here'} ${lab(e.ready==='unknown'?'unknown':'known')}</dd>
  <dt>Planned core</dt><dd>${esc(e.core)} ${lab(e.core==='unknown'?'unknown':'known')}</dd>${e.hardware?`<dt>Controller</dt><dd>${esc(e.hardware)} ${lab('known')}</dd>`:''}${e.personal?'<dt>Source</dt><dd>your own game ${lab(\'known\')}</dd>':''}
  <dt>Cover</dt><dd>${(()=>{ const g=m.games.find(x=>x.id===e.id); const pv=g&&g.art?g.art.provenance:'none'; return pv==='generated'?'generated placeholder (marked GENERATED COVER)':pv==='authentic'?'catalog box art':pv==='uploaded'?'your own image':'none available'; })()} ${lab('known')}</dd></dl></div>`; }
 h+=`<div class="tvs"><div class="lbl">Controls</div><div class="small">The console has only the D-pad, A, B, START and SELECT. A launches at once, SELECT opens Game Details, START opens the menu. Launching here is a simulation ${lab('known')}.</div></div>`;
 h+=`<div class="tvs"><div class="lbl">How sure is this?</div><ul class="tvassume">${p.assumptions.map(a=>`<li>${lab(a.level)} ${esc(a.text)}</li>`).join('')}</ul></div>`;
 h+=p.sample?`<div class="tvs"><div class="lbl">Current vs proposed</div><div class="small">Nothing to compare while the tray is empty: the sample is not a plan.</div></div>`:tvCompareHtml();
 s.innerHTML=h; const sel=s.querySelector('#tvSnap'); if(sel) sel.onchange=()=>{ s.querySelector('#tvDiff').innerHTML=tvDiffHtml(sel.value); };
}
function tvFoot(){ const f=$('tvFoot'); if(!f) return; f.innerHTML=`<span><b>Simulation.</b> The approved HOME themes over your collection; the console build is pending hardware validation (Gate G1 not approved). Launches are simulated.</span><span class="r"><span class="lv known">known</span><span class="lv estimated">estimated</span><span class="lv unknown">unknown</span></span>`; }

// ---- comparison (read-only, honest): no console facts are invented ----
function tvCompareHtml(){
 let snaps=[]; try{ snaps=JSON.parse(localStorage.getItem(SKEY)||'[]'); }catch(e){}
 const opts=snaps.map((s,i)=>`<option value="${i}">${esc(nowShort(s.at))} · ${esc(s.name)} · ${(s.sel||[]).length} games</option>`).join('');
 return `<div class="tvs"><div class="lbl">Current vs proposed</div><div class="small"><span class="lv unknown">unknown</span> Current console state not verified: this preview never reads the console.</div>
  ${snaps.length?`<div class="small" style="margin-top:6px">Saved snapshot vs proposed:</div><select id="tvSnap" class="tvsel"><option value="">choose a snapshot…</option>${opts}</select><div id="tvDiff"></div>`:'<div class="small" style="margin-top:6px">No saved snapshots yet (they are kept automatically when the collection changes).</div>'}</div>`;
}
function tvDiffHtml(i){
 if(i==='') return ''; let snaps=[]; try{ snaps=JSON.parse(localStorage.getItem(SKEY)||'[]'); }catch(e){} const s=snaps[+i]; if(!s) return '';
 const a=new Set(s.sel||[]), b=new Set([...sel]); const added=[...b].filter(x=>!a.has(x)), removed=[...a].filter(x=>!b.has(x));
 const nm=id=>{ const g=byId[id]; return g?g.display_title+(g.nointro_name&&g.nointro_name!==g.display_title?' ('+g.nointro_name+')':''):id; };
 return `<div class="small tvdiff"><div><b>${added.length}</b> added: ${added.length?esc(added.map(nm).join(', ')):'none'}</div><div><b>${removed.length}</b> removed: ${removed.length?esc(removed.map(nm).join(', ')):'none'}</div><div class="mut">Compared with the snapshot, not with a console.</div></div>`;
}

// ---- Save Preview Image: the stage drawn at 1920x1080 from the model (scenery + the covers near the selection), not a DOM capture ----
async function tvSaveImage(){
 const m=TV.model, S=TV.S; const L=homeListing(m,S,tvSearch()); const ids=L.ids; const W=1920,H=1080; const c=document.createElement('canvas'); c.width=W; c.height=H; const x=c.getContext('2d');
 const load=src=>new Promise(res=>{ const im=new Image(); im.onload=()=>res(im); im.onerror=()=>res(null); im.src=src; });
 const bg=await load('brand/home/bg-'+S.theme+'.jpg'); if(bg) x.drawImage(bg,0,0,W,H); else { x.fillStyle='#0b0b0e'; x.fillRect(0,0,W,H); }
 x.fillStyle='rgba(0,0,0,.55)'; x.fillRect(0,0,W,H); x.fillStyle='#fff'; x.font='700 44px "Segoe UI", system-ui, sans-serif'; x.textBaseline='middle'; x.fillText('Classic Cart Studio',60,60); x.font='500 28px "Segoe UI", system-ui, sans-serif'; x.fillText((m.sample?'Sample of the 30 built-in games':COLL.name)+' · '+((HOME_THEME_META.find(t=>t.id===S.theme)||{}).name||S.theme)+' · simulation',60,112);
 const vis=7; let start=Math.max(0,Math.min(S.index-Math.floor(vis/2),ids.length-vis)); if(ids.length<=vis) start=0; const shown=ids.slice(start,start+vis); const tw=226, th=320, gap=24; const total=shown.length*tw+(shown.length-1)*gap; let px=(W-total)/2; const ty=H*0.30;
 for(let k=0;k<shown.length;k++){ const id=shown[k]; const g=m.games.find(v=>v.id===id); const selx=start+k===S.index; const sc=selx?1.18:1; const w=tw*sc, h=th*sc; const xx=px-(w-tw)/2, yy=ty+(th-h)/2;
  x.save(); if(selx){ x.shadowColor='rgba(0,0,0,.45)'; x.shadowBlur=30; x.shadowOffsetY=12; } x.fillStyle='#111'; x.fillRect(xx,yy,w,h); x.restore();
  const im=g&&g.art&&g.art.src?await load(g.art.src):null; if(im){ try{ x.drawImage(im,xx,yy,w,h); }catch(err){} } else { x.fillStyle='#2a2a33'; x.fillRect(xx,yy,w,h); x.fillStyle='#fff'; x.font=`700 ${Math.round(w*0.09)}px "Segoe UI", system-ui, sans-serif`; x.textAlign='center'; x.fillText(g?g.title.slice(0,18):'',xx+w/2,yy+h*0.5); x.textAlign='left'; }
  if(g&&g.art&&g.art.provenance==='generated'){ x.fillStyle='rgba(0,0,0,.75)'; x.fillRect(xx+w-120,yy+h-26,116,22); x.fillStyle='#ffd166'; x.font='700 11px "Segoe UI", system-ui, sans-serif'; x.fillText('GENERATED COVER',xx+w-114,yy+h-15); }
  if(selx){ x.strokeStyle='#e4000f'; x.lineWidth=8; x.strokeRect(xx-4,yy-4,w+8,h+8); }
  px+=tw+gap; }
 const g=ids.length?m.games.find(v=>v.id===ids[S.index]):null; if(g){ x.fillStyle='#fff'; x.font='700 48px "Segoe UI", system-ui, sans-serif'; x.textAlign='center'; x.fillText(g.title,W/2,ty+th*1.18+70); x.textAlign='left'; }
 x.fillStyle='#d6d4ce'; x.font='500 26px "Segoe UI", system-ui, sans-serif'; x.fillText('A launch · SELECT details · START menu · B back · Preview on TV · simulation',60,H-50);
 c.toBlob(b=>{ if(!b) { toast('The image could not be created'); return; } const a=document.createElement('a'); a.href=URL.createObjectURL(b); a.download=(COLL.name||'collection').replace(/[^\w\- ]+/g,'').trim().replace(/\s+/g,'-')+'-tv-preview.png'; a.click(); setTimeout(()=>URL.revokeObjectURL(a.href),4000); toast('Preview image saved (1920×1080)'); },'image/png');
}
// test hook: the same drawing as a data size without a download
async function tvExportSize(){ const ids=TV.model?homeListing(TV.model,TV.S,tvSearch()).ids:[]; const c=document.createElement('canvas'); c.width=1920; c.height=1080; const x=c.getContext('2d'); x.fillStyle='#0b0b0e'; x.fillRect(0,0,1920,1080); return {w:c.width,h:c.height,entries:ids.length}; }

// ---- entry points ----
function tvWire(){
 const hb=$('tvBtn'); if(hb) hb.onclick=()=>openTv();
 const mi=$('mTv'); if(mi) mi.onclick=()=>{ document.querySelectorAll('.menu').forEach(x=>x.hidden=true); openTv(); };
 // the collection changes while the preview is open: rebuild the model (folders, favorites, history kept), keep the focused game by id
 const _r=window.render; window.render=function(){ const out=_r.apply(this,arguments); if(TV.open){ const cur=TV.focusId; TV.proj=null; homeExtrasSave(); TV.model=homeModelFromStudio(); const L=homeListing(TV.model,TV.S,tvSearch()); const k=cur?L.ids.indexOf(cur):-1; if(k>=0&&TV.S.section.type!=='search') TV.S.index=k; else if(cur&&k<0) tvSay('The collection changed; the preview was updated.'); TV.S=homeReconcileState(TV.S,TV.model,tvSearch()); tvDraw(); } return out; };
 // details dialog: a button that opens the preview focused on that game (added when the dialog opens)
 document.body.addEventListener('click',e=>{ const b=e.target.closest&&e.target.closest('[data-tvfocus]'); if(b){ openTv({focus:b.dataset.tvfocus}); } });
}
tvWire();
