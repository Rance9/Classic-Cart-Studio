// ---- "As Seen on TV": an interactive, read-only preview of the proposed NES Classic HOME menu -------------------------------
// Two layers, kept apart on purpose:
//   1. tvProject(): a pure projection of the current collection into "proposed menu entries" (what the plan would show, in what
//      order, with known / estimated / unknown labels). It reuses the collection rules (installClass, factoryVisible, decisions,
//      installRequired, needsHardware) and the same sort rule the PC-side preparation writes into Hakchi's .desktop files.
//   2. TV_SKIN + the renderer: the temporary stock-inspired 16:9 look. Replace the skin (class names, slots, tile geometry, bands)
//      without touching the projection, the navigation state machine, the controls or the export.
// Nothing here talks to a console, scans the computer or uploads anything. The backend (desktop app) may enrich entries with
// local file readiness when it is connected; the preview works without it.
const TV_KEY='studio-tv-v1';
const TV_SKIN={name:'stock-inspired',visible:7,tileW:0.118,tileH:0.46,gap:0.012,bands:{top:0.09,shelf:0.70,bottom:0.09}};
let TV={open:false,mode:'proposed',view:'menu',i:0,treatment:'clean',proj:null,returnTo:null,focusId:null,trap:null};

function tvSortTitle(t){ return String(t||'').toLowerCase().replace(/^the\s+/,''); }   // Hakchi's SortRawTitle rule (what prepare writes)
function tvFactoryTitle(code){ return (window.FACTORY_TITLES&&FACTORY_TITLES[code])||code; }
function tvEntryFor(g){
 const c=installClass(g); const dec=decisionOf(g); const alt=(c==='revision'||c==='review')&&dec==='alternate';
 const kind=c==='builtin'?'builtin':(c==='revision'||c==='review')?(alt?'alternate':'factory-kept'):'added';
 const title=kind==='factory-kept'?(g.builtin_title||tvFactoryTitle(g.builtin_code)||g.display_title):g.display_title;
 const version=kind==='factory-kept'?(g.builtin_factory_version||'factory version'):(g.source==='personal'?(g.rev||'personal game'):(g.nointro_name||g.display_title));
 const core=g.recommended_emulator||'unknown';
 const ready=(typeof COMPANION!=='undefined'&&COMPANION.ok&&installRequired(g))?(fileAvailable(g.game_id)?'ready':'missing'):(installRequired(g)?'unknown':'n/a');
 return {id:g.game_id,title_id:g.title_id,title,version,kind,core,ready,art:g.art||'',players:g.players||'',publisher:g.publisher||'',year:g.year||'',hardware:needsHardware(g)?(g.input_special_accessory||'special input hardware'):'',install:installRequired(g),personal:g.source==='personal',ext:!!g.ext,sort:tvSortTitle(title)};
}
// The projection. mode 'proposed' = the menu the plan proposes (selected games; unselected built-ins hidden by the plan);
// mode 'selected' = the selected games only, isolated (not a menu claim). Deterministic; memoized by collection fingerprint + mode + backend state.
function tvProject(mode){
 mode=mode||'proposed'; const fp=collFingerprint()+'|'+mode+'|'+((typeof COMPANION!=='undefined'&&COMPANION.ok)?'desk':'web');
 if(TV.proj&&TV.proj.fp===fp) return TV.proj;
 const chosen=selectedGames(); const entries=chosen.map(tvEntryFor); entries.sort((a,b)=>a.sort<b.sort?-1:a.sort>b.sort?1:(a.id<b.id?-1:1));
 const hiddenCodes=FACTORY_CODES.filter(cd=>!chosen.some(g=>g.builtin_code===cd&&factoryVisible(g)));
 const assumptions=[
  {level:'known',text:'Which games are in the plan, their exact versions and which built-in games the plan keeps come from your collection.'},
  {level:'estimated',text:'Order: by title (the sort name the preparation writes for Hakchi2 CE, "The" ignored). Hakchi’s sort setting can change this.'},
  {level:'estimated',text:`Unselected built-in games (${hiddenCodes.length}) are hidden by the plan; hiding is applied during preparation on this computer, not by the console itself.`},
  {level:'unknown',text:'Folders and pages: Hakchi2 CE’s folder scheme depends on its settings on this computer; the preview shows one continuous list.'},
  {level:(typeof COMPANION!=='undefined'&&COMPANION.ok)?'known':'unknown',text:(typeof COMPANION!=='undefined'&&COMPANION.ok)?'Game file readiness comes from the Studio on this computer.':'Game files: unknown here (the website cannot see your computer); the desktop app shows which files are ready.'},
  {level:'unknown',text:'What is on the physical console right now is not verified by this preview.'}];
 const proj={fp,mode,entries,count:entries.length,hidden:hiddenCodes.map(cd=>({code:cd,title:tvFactoryTitle(cd)})),assumptions,
  counts:{builtin:entries.filter(e=>e.kind==='builtin').length,kept:entries.filter(e=>e.kind==='factory-kept').length,added:entries.filter(e=>e.kind==='added').length,alternate:entries.filter(e=>e.kind==='alternate').length,missing:entries.filter(e=>e.ready==='missing').length,ready:entries.filter(e=>e.ready==='ready').length,hardware:entries.filter(e=>e.hardware).length}};
 TV.proj=proj; return proj;
}
function tvEntries(){ return tvProject(TV.mode).entries; }

// ---- markup (created once) ----
function tvEnsure(){
 if($('tv')) return;
 const m=document.createElement('div'); m.id='tv'; m.className='tvmodal'; m.setAttribute('role','dialog'); m.setAttribute('aria-modal','true'); m.setAttribute('aria-labelledby','tvTitle'); m.hidden=true;
 m.innerHTML=`<div class="tvbox">
  <div class="tvhd"><div class="tvt"><img class="tvlogo" src="brand/public/as-seen-on-tv.svg" alt="" width="44" height="34"><div><h2 id="tvTitle">Proposed Menu Preview</h2><span class="small" id="tvSub"></span></div></div>
   <div class="tvtools"><div class="seg" role="group" aria-label="What to show"><button class="btn sm active" id="tvModeP" data-mode="proposed" title="The menu the plan proposes">Proposed menu</button><button class="btn sm" id="tvModeS" data-mode="selected" title="Only the games you selected, isolated; not the full menu">Selected games only</button></div>
   <div class="seg" role="group" aria-label="Display treatment"><button class="btn sm active" id="tvTrC" data-tr="clean">Clean</button><button class="btn sm" id="tvTrR" data-tr="retro" title="A cosmetic CRT-style look around the same 16:9 menu">Retro</button></div>
   <button class="btn sm" id="tvSave" title="Save the simulated TV surface as a 1920×1080 PNG">Save Preview Image</button><button class="btn sm ghost" id="tvClose" aria-label="Close the preview">Close</button></div></div>
  <div class="tvmain">
   <div class="tvstagewrap"><div class="tvstage" id="tvStage" tabindex="0" aria-label="Simulated NES Classic HOME menu, 16 by 9" aria-describedby="tvLive"><div class="tvscreen" id="tvScreen"></div><div class="tvcrt" aria-hidden="true"></div></div>
    <div class="tvpad" id="tvPad" aria-label="On-screen controls"><div class="dpad"><button class="pb up" data-act="up" aria-label="Up">▲</button><button class="pb left" data-act="left" aria-label="Left">◀</button><button class="pb right" data-act="right" aria-label="Right">▶</button><button class="pb down" data-act="down" aria-label="Down">▼</button></div>
     <div class="abh"><button class="pb b" data-act="b" aria-label="B, back">B</button><button class="pb a" data-act="a" aria-label="A, select">A</button><button class="pb home" data-act="home" aria-label="HOME, back to the menu">HOME</button></div>
     <div class="small tvhint">← → browse · Enter / A open · Backspace / B back · Esc close</div></div>
   </div>
   <aside class="tvside" id="tvSide"></aside>
  </div>
  <div class="tvfoot" id="tvFoot"></div>
  <div class="vis-hidden" id="tvLive" aria-live="polite"></div>
 </div>`;
 document.body.appendChild(m);
 $('tvClose').onclick=closeTv; $('tvSave').onclick=tvSaveImage;
 $('tvModeP').onclick=()=>tvSetMode('proposed'); $('tvModeS').onclick=()=>tvSetMode('selected'); $('tvTrC').onclick=()=>tvSetTreatment('clean'); $('tvTrR').onclick=()=>tvSetTreatment('retro');
 $('tvPad').addEventListener('click',e=>{ const b=e.target.closest('[data-act]'); if(b) tvAct(b.dataset.act); });
 $('tvScreen').addEventListener('click',e=>{ const t=e.target.closest('[data-i]'); if(!t) return; const i=+t.dataset.i; if(TV.view==='menu'){ if(i===TV.i) tvAct('a'); else { TV.i=i; tvDraw(); } } });
 $('tvScreen').addEventListener('dblclick',e=>{ const t=e.target.closest('[data-i]'); if(t&&TV.view==='menu'){ TV.i=+t.dataset.i; tvAct('a'); } });
 m.addEventListener('keydown',tvKey); m.addEventListener('click',e=>{ if(e.target===m) closeTv(); });
 try{ const s=JSON.parse(localStorage.getItem(TV_KEY)||'{}'); if(s.treatment) TV.treatment=s.treatment; }catch(e){}
}
function tvPersist(){ try{ localStorage.setItem(TV_KEY,JSON.stringify({treatment:TV.treatment})); }catch(e){} }

// ---- open / close ----
function openTv(opts){
 opts=opts||{}; tvEnsure(); TV.returnTo=document.activeElement; TV.mode=opts.mode||'proposed'; TV.view='menu'; TV.open=true; TV.proj=null;
 const ents=tvEntries(); TV.i=0; if(opts.focus){ const k=ents.findIndex(e=>e.id===opts.focus); if(k>=0) TV.i=k; }
 const m=$('tv'); m.hidden=false; m.classList.add('open'); document.body.classList.add('tv-open'); document.body.style.overflow='hidden';
 tvApplyTreatment(); tvDraw(); $('tvStage').focus({preventScroll:true});
 if(opts.focus&&!ents.some(e=>e.id===opts.focus)){ const g=byId[opts.focus]; tvSay(`${g?g.display_title:'That game'} is not in the proposed menu: it is not selected.`); $('tvSub').textContent=`${g?g.display_title:'That game'} is not part of the proposed menu (not selected)`; }
 if(typeof hoLog==='function') hoLog('tv open '+TV.mode+' n='+ents.length);
}
function closeTv(){ const m=$('tv'); if(!m||!TV.open) return; TV.open=false; m.classList.remove('open'); m.hidden=true; document.body.classList.remove('tv-open'); document.body.style.overflow=''; const r=TV.returnTo; TV.returnTo=null; try{ if(r&&r.focus) r.focus({preventScroll:true}); }catch(e){} }
function tvSetMode(mode){ TV.mode=mode; TV.view='menu'; const cur=(tvEntries()[TV.i]||{}).id; TV.proj=null; const ents=tvEntries(); const k=ents.findIndex(e=>e.id===cur); TV.i=k>=0?k:Math.min(TV.i,Math.max(0,ents.length-1)); tvDraw(); }
function tvSetTreatment(t){ TV.treatment=t; tvPersist(); tvApplyTreatment(); }
function tvApplyTreatment(){ const st=$('tvStage'); if(!st) return; st.classList.toggle('retro',TV.treatment==='retro'); $('tvTrC').classList.toggle('active',TV.treatment==='clean'); $('tvTrR').classList.toggle('active',TV.treatment==='retro'); }
function tvSay(t){ const l=$('tvLive'); if(l) l.textContent=t; }

// ---- navigation: one state machine for keys, pointer, touch and the on-screen pad ----
function tvAct(act){
 const ents=tvEntries(); const n=ents.length;
 if(TV.view==='details'){ if(act==='b'||act==='home'||act==='left'){ TV.view='menu'; tvDraw(); } return; }
 if(!n){ if(act==='b'||act==='home') closeTv(); return; }
 if(act==='left') TV.i=(TV.i-1+n)%n; else if(act==='right') TV.i=(TV.i+1)%n;
 else if(act==='up') TV.i=Math.max(0,TV.i-TV_SKIN.visible); else if(act==='down') TV.i=Math.min(n-1,TV.i+TV_SKIN.visible);
 else if(act==='home') TV.i=0; else if(act==='end') TV.i=n-1;
 else if(act==='a'){ TV.view='details'; tvDraw(); return; }
 else if(act==='b'){ closeTv(); return; }
 tvDraw();
}
function tvKey(e){
 const t=e.target; if(t&&t.matches&&t.matches('input,textarea,select')) return;
 const k=e.key; let act=null;
 if(k==='ArrowLeft') act='left'; else if(k==='ArrowRight') act='right'; else if(k==='ArrowUp') act='up'; else if(k==='ArrowDown') act='down';
 else if(k==='Home') act='home'; else if(k==='End') act='end'; else if(k==='Enter'||k===' '||k==='a'||k==='A') act='a'; else if(k==='Backspace'||k==='b'||k==='B') act='b';
 else if(k==='Escape'){ e.preventDefault(); if(TV.view==='details'){ TV.view='menu'; tvDraw(); } else closeTv(); return; }
 else if(k==='Tab'){ tvTrapTab(e); return; }
 if(act){ if(act==='a'&&t&&t.closest&&t.closest('.tvtools,.tvside,.tvpad')&&k!=='a'&&k!=='A') return; e.preventDefault(); tvAct(act); }
}
function tvTrapTab(e){ const m=$('tv'); const f=[...m.querySelectorAll('button:not([disabled]),[tabindex="0"],select,input,a[href]')].filter(x=>x.offsetParent!==null); if(!f.length) return; const first=f[0], last=f[f.length-1]; if(e.shiftKey&&document.activeElement===first){ e.preventDefault(); last.focus(); } else if(!e.shiftKey&&document.activeElement===last){ e.preventDefault(); first.focus(); } }

// ---- renderer (temporary stock-inspired skin) ----
function tvTile(e,i,sel){
 const art=e.art?`<img src="${esc(e.art)}" alt="" loading="lazy" onerror="this.replaceWith(Object.assign(document.createElement('div'),{className:'tvph'}))">`:'<div class="tvph" aria-hidden="true"><span>NES</span></div>';
 return `<button class="tvtile${sel?' sel':''}${e.kind==='builtin'||e.kind==='factory-kept'?' fac':''}" data-i="${i}" aria-label="${esc(e.title)}${sel?', selected':''}" tabindex="-1">${art}</button>`;
}
function tvDraw(){
 const ents=tvEntries(); const n=ents.length; const scr=$('tvScreen'); if(!scr) return;
 if(TV.i>=n) TV.i=Math.max(0,n-1); const e=ents[TV.i];
 $('tvSub').textContent=TV.mode==='selected'?'Selected games only · this is not the full menu':'Simulation of the menu this collection proposes · stock-inspired look, not a console capture';
 $('tvModeP').classList.toggle('active',TV.mode==='proposed'); $('tvModeS').classList.toggle('active',TV.mode==='selected');
 TV.focusId=null; if(!n){ scr.innerHTML=`<div class="tvtop"><span class="nm">${esc(COLL.name)}</span><span class="cnt">0 games</span></div><div class="tvempty"><div class="tvph big"><span>NES</span></div><b>Nothing to preview yet</b><span>Select games in the library and they appear here, laid out the way the plan would show them on your NES Classic.</span></div><div class="tvbar"><span><i>B</i> close</span></div>`; tvSide(null); tvFoot(); tvSay('Nothing to preview yet'); return; }
 if(TV.view==='details'){
  scr.innerHTML=`<div class="tvtop"><span class="nm">${esc(e.title)}</span><span class="cnt">${TV.i+1} / ${n}</span></div>
   <div class="tvdet"><div class="tvdetart">${e.art?`<img src="${esc(e.art)}" alt="">`:'<div class="tvph big"><span>NES</span></div>'}</div>
    <div class="tvdetinfo"><div class="tvdettitle">${esc(e.title)}</div><div class="tvdetmeta">${esc([e.year,e.publisher,e.players?e.players+' player(s)':''].filter(Boolean).join(' · '))}</div>
     <div class="tvslots" aria-label="Suspend points (illustration only)"><span class="lbl">SUSPEND POINTS</span><div class="slots"><i></i><i></i><i></i><i></i></div><span class="note">empty slots shown as an illustration; the preview never reads save data</span></div>
    </div></div>
   <div class="tvbar"><span><i>B</i> back to the menu</span><span class="r">menu details only · no gameplay</span></div>`;
  tvSide(e); tvFoot(); tvSay(`${e.title} details. B or Backspace returns to the menu.`); TV.focusId=e.id; tvKeepFocus(); return;
 }
 const vis=TV_SKIN.visible; let start=Math.max(0,Math.min(TV.i-Math.floor(vis/2),n-vis)); if(n<=vis) start=0;
 const shown=ents.slice(start,start+vis).map((x,k)=>tvTile(x,start+k,start+k===TV.i)).join('');
 scr.innerHTML=`<div class="tvtop"><span class="nm">${esc(COLL.name)}</span><span class="cnt">${TV.i+1} / ${n}</span></div>
  <div class="tvplate"><span class="pt">${esc(e.title)}</span>${e.kind==='factory-kept'||e.kind==='builtin'?'<span class="pk">built-in</span>':e.kind==='alternate'?'<span class="pk alt">alternate version</span>':''}</div>
  <div class="tvshelf" style="--vis:${vis}">${start>0?'<span class="more l" aria-hidden="true">◀</span>':''}${shown}${start+vis<n?'<span class="more r" aria-hidden="true">▶</span>':''}</div>
  <div class="tvbar"><span><i>◀ ▶</i> browse</span><span><i>A</i> open</span><span class="r">${n} game${n===1?'':'s'}</span></div>`;
 tvSide(e); tvFoot(); tvSay(`${e.title}, ${TV.i+1} of ${n}`); TV.focusId=e.id;
 const selEl=scr.querySelector('.tvtile.sel'); if(selEl&&window.matchMedia&&!matchMedia('(prefers-reduced-motion: reduce)').matches) selEl.classList.add('pop');
 tvKeepFocus();
}
// re-rendering replaces the screen's elements: keep keyboard focus inside the dialog (on the stage) unless a side-panel control has it
function tvKeepFocus(){ const a=document.activeElement; const m=$('tv'); if(!m||!TV.open) return; if(!a||a===document.body||!m.contains(a)||!a.isConnected) { try{ $('tvStage').focus({preventScroll:true}); }catch(e){} } }
function tvKindText(k){ return {builtin:'built-in game, kept as it is',"factory-kept":'built-in game kept in its factory version',added:'added game',alternate:'catalog version chosen over the factory version (needs approval at import)'}[k]||k; }
function tvSide(e){
 const p=tvProject(TV.mode); const s=$('tvSide'); if(!s) return;
 const lab=l=>`<span class="lv ${l}">${l}</span>`;
 let h=`<div class="tvs"><div class="lbl">Collection</div><b>${esc(COLL.name)}</b><div class="small">${p.count} game${p.count===1?'':'s'} in the ${TV.mode==='selected'?'selection':'proposed menu'} · ${p.counts.builtin+p.counts.kept} built-in kept · ${p.counts.added+p.counts.alternate} added · ${p.hidden.length} built-in hidden by the plan</div></div>`;
 if(e){ h+=`<div class="tvs"><div class="lbl">Selected</div><b>${esc(e.title)}</b><div class="small">${esc(e.version)}</div>
  <dl class="tvkv"><dt>Position</dt><dd>${TV.i+1} of ${p.count} ${lab('estimated')}</dd><dt>Status</dt><dd>${esc(tvKindText(e.kind))} ${lab('known')}</dd>
  <dt>Game file</dt><dd>${e.ready==='n/a'?'not needed (already on the console)':e.ready==='ready'?'ready on this computer':e.ready==='missing'?'missing on this computer':'unknown here'} ${lab(e.ready==='unknown'?'unknown':'known')}</dd>
  <dt>Planned core</dt><dd>${esc(e.core)} ${lab(e.core==='unknown'?'unknown':'known')}</dd>${e.hardware?`<dt>Controller</dt><dd>${esc(e.hardware)} ${lab('known')}</dd>`:''}${e.personal?'<dt>Source</dt><dd>your own game ${lab(\'known\')}</dd>':''}</dl></div>`; }
 h+=`<div class="tvs"><div class="lbl">How sure is this?</div><ul class="tvassume">${p.assumptions.map(a=>`<li>${lab(a.level)} ${esc(a.text)}</li>`).join('')}</ul></div>`;
 h+=tvCompareHtml();
 s.innerHTML=h; const sel=s.querySelector('#tvSnap'); if(sel) sel.onchange=()=>{ s.querySelector('#tvDiff').innerHTML=tvDiffHtml(sel.value); };
}
function tvFoot(){ const f=$('tvFoot'); if(!f) return; f.innerHTML=`<span><b>Simulation.</b> Stock-inspired look; your console may differ with Hakchi2 CE settings and future custom menus.</span><span class="r"><span class="lv known">known</span><span class="lv estimated">estimated</span><span class="lv unknown">unknown</span></span>`; }

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

// ---- Save Preview Image: the simulated surface at 1920x1080, drawn from the projection (not a DOM capture) ----
async function tvSaveImage(){
 const ents=tvEntries(); const e=ents[TV.i]; const W=1920,H=1080; const c=document.createElement('canvas'); c.width=W; c.height=H; const x=c.getContext('2d');
 const g=x.createLinearGradient(0,0,0,H); g.addColorStop(0,'#eef0f3'); g.addColorStop(1,'#c9ced5'); x.fillStyle=g; x.fillRect(0,0,W,H);
 x.fillStyle='#1b1b20'; x.fillRect(0,0,W,H*TV_SKIN.bands.top); x.fillStyle='#f3f1ec'; x.font='600 38px "Segoe UI", system-ui, sans-serif'; x.textBaseline='middle'; x.fillText(COLL.name,48,H*TV_SKIN.bands.top/2); x.textAlign='right'; x.fillText(ents.length?`${TV.i+1} / ${ents.length}`:'0 games',W-48,H*TV_SKIN.bands.top/2); x.textAlign='left';
 x.fillStyle='#1b1b20'; x.fillRect(0,H*(1-TV_SKIN.bands.bottom),W,H*TV_SKIN.bands.bottom); x.fillStyle='#d6d4ce'; x.font='500 28px "Segoe UI", system-ui, sans-serif'; x.fillText('◀ ▶ browse     A open     Proposed Menu Preview · simulation',48,H*(1-TV_SKIN.bands.bottom/2));
 if(e){ x.fillStyle='rgba(27,27,32,.9)'; const pw=Math.min(W-200,Math.max(420,x.measureText(e.title).width+120)); x.fillRect((W-pw)/2,H*0.13,pw,76); x.fillStyle='#fff'; x.font='700 40px "Segoe UI", system-ui, sans-serif'; x.textAlign='center'; x.fillText(e.title,W/2,H*0.13+38); x.textAlign='left'; }
 const vis=TV_SKIN.visible; let start=Math.max(0,Math.min(TV.i-Math.floor(vis/2),ents.length-vis)); if(ents.length<=vis) start=0; const shown=ents.slice(start,start+vis);
 const tw=W*TV_SKIN.tileW, th=tw*4.3/3, gap=W*TV_SKIN.gap; const total=shown.length*tw+(shown.length-1)*gap; let px=(W-total)/2; const ty=H*0.30;
 const load=src=>new Promise(res=>{ const im=new Image(); im.onload=()=>res(im); im.onerror=()=>res(null); im.src=src; });
 for(let k=0;k<shown.length;k++){ const en=shown[k]; const selx=start+k===TV.i; const sc=selx?1.18:1; const w=tw*sc, h=th*sc; const xx=px-(w-tw)/2, yy=ty+(th-h)/2;
  x.save(); if(selx){ x.shadowColor='rgba(0,0,0,.45)'; x.shadowBlur=30; x.shadowOffsetY=12; }
  x.fillStyle='#111'; x.fillRect(xx,yy,w,h); x.restore();
  const im=en.art?await load(en.art):null; if(im){ try{ x.drawImage(im,xx,yy,w,h); }catch(err){} } else { x.fillStyle='#2a2a33'; x.fillRect(xx,yy,w,h); x.fillStyle='#e4000f'; x.fillRect(xx+w*0.2,yy+h*0.38,w*0.6,h*0.2); x.fillStyle='#fff'; x.font=`700 ${Math.round(w*0.16)}px "Segoe UI", system-ui, sans-serif`; x.textAlign='center'; x.fillText('NES',xx+w/2,yy+h*0.49); x.textAlign='left'; }
  if(selx){ x.strokeStyle='#e4000f'; x.lineWidth=8; x.strokeRect(xx-4,yy-4,w+8,h+8); }
  px+=tw+gap; }
 c.toBlob(b=>{ if(!b) { toast('The image could not be created'); return; } const a=document.createElement('a'); a.href=URL.createObjectURL(b); a.download=(COLL.name||'collection').replace(/[^\w\- ]+/g,'').trim().replace(/\s+/g,'-')+'-tv-preview.png'; a.click(); setTimeout(()=>URL.revokeObjectURL(a.href),4000); toast('Preview image saved (1920×1080)'); },'image/png');
}
// test hook: the same drawing as a data size without a download
async function tvExportSize(){ const ents=tvEntries(); const c=document.createElement('canvas'); c.width=1920; c.height=1080; const x=c.getContext('2d'); x.fillStyle='#eef0f3'; x.fillRect(0,0,1920,1080); return {w:c.width,h:c.height,entries:ents.length}; }

// ---- entry points ----
function tvWire(){
 const hb=$('tvBtn'); if(hb) hb.onclick=()=>openTv();
 const mi=$('mTv'); if(mi) mi.onclick=()=>{ document.querySelectorAll('.menu').forEach(x=>x.hidden=true); openTv(); };
 // the collection changes while the preview is open: re-project, keep the focused game by id
 const _r=window.render; window.render=function(){ const out=_r.apply(this,arguments); if(TV.open){ const cur=TV.focusId; TV.proj=null; const ents=tvEntries(); const k=ents.findIndex(e=>e.id===cur); if(k>=0) TV.i=k; else { TV.i=Math.min(TV.i,Math.max(0,ents.length-1)); tvSay('The collection changed; the preview was updated.'); } tvDraw(); } return out; };
 // details dialog: a button that opens the preview focused on that game (added when the dialog opens)
 document.body.addEventListener('click',e=>{ const b=e.target.closest&&e.target.closest('[data-tvfocus]'); if(b){ openTv({focus:b.dataset.tvfocus}); } });
}
tvWire();
