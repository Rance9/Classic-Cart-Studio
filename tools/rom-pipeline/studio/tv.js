// ---- "As Seen on TV": Preview on TV, now the console HOME runtime (four approved themes) over the user's own collection ------
// Two layers, kept apart on purpose:
//   1. tvProject(): a pure projection of the current collection into "proposed menu entries" (what the plan would show, with
//      known / estimated / unknown labels). It reuses the collection rules (installClass, factoryVisible, decisions, installRequired,
//      needsHardware). It feeds homeBuild(): the HOME model (home-model.js) that all four themes render.
//   2. The HOME runtime: home-state.js (original-controller state machine) + home-themes.js (renderers). The preview hosts it in
//      the same small modal the toolbar badge opens; host keys and the on-screen pad are emulations of D-pad, A, B, START, SELECT.
// Nothing here talks to a console, scans the computer or uploads anything. A launch is SIMULATED and labeled as such.
const TV_KEY='studio-tv-v1', HOME_KEY='studio-home-v1';
const TV_SAMPLE_NOTICE='This is a sample: the 30 built-in games every NES Classic ships with. Add games to see your own menu.';
// what the preview shows: the open NES Classic Loadout (from a collection, Preview on TV asks for one first), else the collection
function tvLoadout(){ try{ return (typeof loCur==='function')?loCur():null; }catch(e){ return null; } }
function tvPlanWord(){ return tvLoadout()?'loadout':'collection'; }
function tvPlanName(){ const l=tvLoadout(); return l?l.name:COLL.name; }
function tvPl(n,w){ return `${n} ${w}${n===1?'':'s'}`; }
function tvCoreText(c){ const s=String(c||'unknown'); return /kachikachi/i.test(s)?'Built-in NES Classic emulator (kachikachi)':/retroarch/i.test(s)?'RetroArch (add-on module)':s==='unknown'?'Not known for this game':s; }   // F15 (D-97): the recommendation for the console; the PC and the browser play with FCEUmm
const TV_SECTION_NAMES={allgames:'All Games',browse:'Browse',favorites:'Favorites',history:'Recently Played',search:'Search',settings:'Settings'};
let TV={open:false,mode:'proposed',treatment:'clean',appearance:'browser',proj:null,returnTo:null,focusId:null,model:null,S:null,held:null,clock:0,repeatTimer:0,launching:null,hold:0,holdUp:0,drawLater:null};
function tvSortTitle(t){ t=String(t||'').toLowerCase().trim(); return t.startsWith('the ')?t.slice(4):t; }
function tvFactoryTitle(code){ return (typeof FACTORY_TITLES!=='undefined'&&FACTORY_TITLES[code])||code; }
// sample entries when the tray is empty: the 30 built-in games in their factory versions
function tvSampleEntries(){ return [...FACTORY_CODES].sort().map(cd=>{ const g=factoryRowFor(cd); const title=tvFactoryTitle(cd); return {id:g?g.game_id:'factory:'+cd,title_id:g?g.title_id:'factory:'+cd,title,version:'factory version',kind:'builtin',core:'clover-kachikachi-wr',ready:'n/a',art:g?g.art||'':'',players:g?g.players||'':'',publisher:g?g.publisher||'':'',year:g?g.year||'':'',genre:g?g.genre||'':'',hardware:'',install:false,personal:false,ext:false,sort:tvSortTitle(title),builtin_code:cd}; }); }
// the games of this plan as Preview shows them: every selected id, Game Library rows and the collection's kept entries (imported games this
// device cannot resolve: their sanitized details from csCleanMissing, never added to byId). Only an empty selection shows the sample (M2-4)
function tvChosen(){ const s=[]; sel.forEach(id=>{ let g=byId[id]||(typeof keptRow==='function'?keptRow(id):null);
  if(!g&&typeof perRow==='function') g=Object.assign(perRow({id, user:{title:'Unknown game'}, file_present:false}), {kept:true, source:'unknown', kind:'kept'});
  if(g) s.push(g); }); return s.sort((a,b)=>String(a.display_title||'').localeCompare(String(b.display_title||''))); }
function tvEntryFor(g){
 const c=installClass(g); const dec=decisionOf(g); const alt=(c==='revision'||c==='review')&&dec==='alternate';
 const kind=c==='builtin'?'builtin':(c==='revision'||c==='review')?(alt?'alternate':'factory-kept'):'added';
 const title=kind==='factory-kept'?(g.builtin_title||tvFactoryTitle(g.builtin_code)||g.display_title):g.display_title;
 const version=kind==='factory-kept'?(g.builtin_factory_version||'factory version'):(g.source==='personal'?(g.rev||'personal game'):(g.nointro_name||g.display_title));
 const core=g.recommended_emulator||'unknown';
 const kept=!!g.kept, app=typeof COMPANION!=='undefined'&&COMPANION.ok;   // a kept entry: its file is checked like Back Up Collection does (keptAvail)
 const ka=kept&&typeof keptAvail==='function'?keptAvail(g.game_id):null;
 const ready=kept?(app?(ka==='here'?'ready':ka==='none'?'missing':'unknown'):'unknown'):(app&&installRequired(g))?(fileAvailable(g.game_id)?'ready':'missing'):(installRequired(g)?'unknown':'n/a');
 return {id:g.game_id,title_id:g.title_id,title,version,kind,core,ready,art:g.art||'',players:g.players||'',publisher:g.publisher||'',year:g.year||'',genre:g.genre||'',hardware:g.input_level!=='unknown'&&needsHardware(g)?(g.input_special_accessory||'special input hardware'):'',install:installRequired(g),personal:g.source==='personal',kept,ext:!!g.ext,sort:tvSortTitle(title),builtin_code:g.builtin_code||null};
}
// The projection. mode 'proposed' = the menu the plan proposes (selected games; unselected built-ins hidden by the plan). Deterministic; memoized by collection fingerprint + mode + backend state.
function tvProject(mode){
 mode=mode||'proposed'; const fp=collFingerprint()+'|'+mode+'|'+((typeof COMPANION!=='undefined'&&COMPANION.ok)?'desk':'web');
 if(TV.proj&&TV.proj.fp===fp) return TV.proj;
 const chosen=tvChosen(); const sample=chosen.length===0; const entries=sample?tvSampleEntries():chosen.map(tvEntryFor); entries.sort((a,b)=>a.sort<b.sort?-1:a.sort>b.sort?1:(a.id<b.id?-1:1));
 const hiddenCodes=sample?[]:FACTORY_CODES.filter(cd=>!chosen.some(g=>g.builtin_code===cd&&factoryVisible(g)));
 const assumptions=sample?[
  {level:'known',text:`This ${tvPlanWord()} has no games yet, so this is a sample: the 30 built-in games every NES Classic ships with, in their factory versions. Add games to see your own HOME.`},
  {level:'unknown',text:'What is on your console right now is not checked by this preview.'}]:[
  {level:'known',text:`The games, their exact versions and the built-in games kept come from this ${tvPlanWord()}.`},
  {level:'known',text:`All Games uses the ${tvPlanWord()}'s order; a sort chosen in the HOME changes only the view.`},
  {level:'estimated',text:`${tvPl(hiddenCodes.length,'built-in game')} left out ${hiddenCodes.length===1?'is':'are'} hidden. Hiding is applied when the ${tvPlanWord()} is prepared on this computer, not by the console itself.`},
  {level:(typeof COMPANION!=='undefined'&&COMPANION.ok)?'known':'unknown',text:(typeof COMPANION!=='undefined'&&COMPANION.ok)?'Game file status comes from this computer.':'Game files are unknown on the website; the Windows app shows which files are ready.'},
  {level:'unknown',text:'What is on your console right now is not checked by this preview.'}];
 const nk=entries.filter(e=>e.kept).length;   // imported games shown from the collection's own details: say where their ROM files are
 if(!sample&&nk){ const app=typeof COMPANION!=='undefined'&&COMPANION.ok, nr=entries.filter(e=>e.kept&&e.ready==='ready').length;
  assumptions.splice(1,0,{level:'known',text:`${tvPl(nk,'imported game')} ${nk===1?'shows':'show'} the details kept with this collection. `+(app?`${nr} of them ${nr===1?'has its':'have their'} ROM file on this computer.`:(()=>{ const nb=entries.filter(e=>e.kept&&typeof tpFileText==='function'&&/ready to play/.test(tpFileText(e))).length; return `${nb} of them ${nb===1?'is':'are'} kept in this browser for playing.`; })())}); }
 const proj={fp,mode,sample,entries,count:entries.length,hidden:hiddenCodes.map(cd=>({code:cd,title:tvFactoryTitle(cd)})),assumptions,
  counts:{builtin:entries.filter(e=>e.kind==='builtin').length,kept:entries.filter(e=>e.kind==='factory-kept').length,added:entries.filter(e=>e.kind==='added').length,alternate:entries.filter(e=>e.kind==='alternate').length,missing:entries.filter(e=>e.ready==='missing').length,ready:entries.filter(e=>e.ready==='ready').length,hardware:entries.filter(e=>e.hardware).length}};
 TV.proj=proj; return proj;
}
function tvEntries(){ return tvProject(TV.mode).entries.slice(); }

// ---- HOME model from the projection (one collection model; folders, favorites, history, sorts and theme persist beside it) ----
function homeExtrasLoad(){ try{ return JSON.parse(localStorage.getItem(HOME_KEY)||'{}')||{}; }catch(e){ return {}; } }
function homeExtrasSave(){ if(!TV.model) return; const m=TV.model; try{ const saved=JSON.parse(localStorage.getItem(HOME_KEY)||'null'); if(saved&&saved.history) m.history=homeReconcileHistory(saved.history,m.history); if(saved&&saved.favorites) m.favorites=homeReconcileFavorites(saved.favorites,m.favorites); }catch(e){}   // another tab's History clear or play, and its Favorites changes (per game: higher revision, then later change), are never overwritten by stale memory
 try{ localStorage.setItem(HOME_KEY,JSON.stringify({v:1,folders:m.folders,favorites:m.favorites,history:m.history,map_shortcuts:m.map_shortcuts,sorts:m.sorts,theme:m.theme,settings:m.settings,startup_image:m.startup_image||'random',state:TV.S})); }catch(e){} }
// another tab saved the HOME: take its History and Favorites into this tab at once (the same reconciliation), so the screen never shows cleared plays
window.addEventListener('storage',e=>{ if(e.key!==HOME_KEY||!TV.model||!e.newValue) return; try{ const x=JSON.parse(e.newValue); if(x&&(x.history||x.favorites)){ if(x.history) TV.model.history=homeReconcileHistory(x.history,TV.model.history); if(x.favorites) TV.model.favorites=homeReconcileFavorites(x.favorites,TV.model.favorites); if(TV.open){ TV.S=homeReconcileState(TV.S,TV.model,tvSearch()); tvDraw(true); } } }catch(err){} });
// a kept entry's row for the HOME: the sanitized kept row plus its own kept description (plain text; the HOME escapes every text)
function tvChosenRow(id){ const r=typeof keptRow==='function'?keptRow(id):null; if(!r) return null; const u=(r.personal&&r.personal.user)||{};
 return Object.assign({}, r, {description:typeof u.description==='string'&&u.description?u.description.slice(0,4000):null}); }
function homeModelFromStudio(){
 const p=tvProject(TV.mode); const prev=homeExtrasLoad(); const sels=p.entries.map(e=>({game_id:e.id,title_id:e.title_id,display_title:e.title,nointro_name:e.version==='factory version'?'':e.version,builtin_code:e.builtin_code,install_required:e.install,source:e.personal?'personal':'catalog',art:e.art,builtin_factory_version:e.version==='factory version'?'factory':null}));
 const readiness={}; let known=false; p.entries.forEach(e=>{ if(e.ready==='ready'||e.ready==='missing'){ readiness[e.id]=e.ready; known=true; } });
 const generated={}; p.entries.forEach(e=>{ const g=byId[e.id]; const s=g&&!g.art?(g.art_generated||(typeof mockCoverUrl==='function'?mockCoverUrl(g):'')):''; if(s) generated[e.id]=s; });   // rows without authentic art or the person's image: the pre-made placeholder or the page-drawn mock cover
 // kept entries resolve from their sanitized rows (with their own description), never through byId; ids stay data (own-property lookups only)
 const kr=new Map(); p.entries.forEach(e=>{ if(e.kept){ const r=tvChosenRow(e.id); if(r) kr.set(e.id, r); } });
 const look=kr.size?new Proxy(byId,{get:(t,k)=>typeof k!=='string'?undefined:Object.prototype.hasOwnProperty.call(t,k)?t[k]:kr.get(k)}):byId;
 const m=homeBuild({schema:'nes-classic-collection/4',name:p.sample?'Sample: the 30 built-in games':COLL.name,selections:sels},look,prev,{readiness:known?readiness:undefined,generated});
 m.sample=p.sample; return m;
}

// ---- markup (created once) ----
function tvEnsure(){
 if($('tv')) return;
 const m=document.createElement('div'); m.id='tv'; m.className='tvmodal'; m.setAttribute('role','dialog'); m.setAttribute('aria-modal','true'); m.setAttribute('aria-labelledby','tvTitle'); m.hidden=true;
 m.innerHTML=`<div class="tvbox">
  <div class="tvhd"><div class="tvt"><div><h2 id="tvTitle">Preview on TV</h2><span class="small" id="tvSub"></span></div></div>
   <div class="tvtools">
    <div class="tvmenu"><button type="button" class="btn sm tvmb" id="tvThemeBtn" aria-haspopup="true" aria-expanded="false" aria-controls="tvThemeMenu"><span class="lbl">Theme:</span> <b id="tvThemeName"></b> <span class="car" aria-hidden="true">▾</span></button>
     <div class="tvdrop" id="tvThemeMenu" role="menu" aria-label="Theme (on the console: START, then Change Theme)" hidden>${HOME_THEME_META.map(t=>`<button type="button" role="menuitemradio" aria-checked="false" class="tvtheme" data-theme="${t.id}"><span>${esc(t.name)}</span><small>${esc(t.note)}</small></button>`).join('')}</div></div>
    <div class="tvmenu"><button type="button" class="btn sm tvmb" id="tvDisplayBtn" aria-haspopup="true" aria-expanded="false" aria-controls="tvDisplayMenu">Display <span class="car" aria-hidden="true">▾</span></button>
     <div class="tvdrop" id="tvDisplayMenu" role="menu" aria-label="Display" hidden><button type="button" role="menuitemradio" aria-checked="true" id="tvTrC" data-tr="clean"><span>Clean</span><small>The picture as it is</small></button><button type="button" role="menuitemradio" aria-checked="false" id="tvTrR" data-tr="retro"><span>Retro</span><small>A CRT-style look; the HOME stays 16:9</small></button></div></div>
    <div class="tvmenu"><button type="button" class="btn sm tvmb" id="tvMoreBtn" aria-haspopup="true" aria-expanded="false" aria-controls="tvMoreMenu">More <span class="car" aria-hidden="true">▾</span></button>
     <div class="tvdrop" id="tvMoreMenu" role="menu" aria-label="More" hidden><button type="button" role="menuitem" id="tvSave"><span>Save Preview Image</span><small>The picture as a PNG file</small></button></div></div>
    <button type="button" class="btn sm" id="tvPop" hidden>Pop Out</button>
   </div>
   <button class="tvclose" id="tvClose" type="button" aria-label="Close the preview" title="Close (Esc)">✕</button></div>
  <div class="tvmain">
   <div class="tvstagewrap"><div class="tvstage" id="tvStage" tabindex="0" aria-label="Simulated NES Classic HOME, 16 by 9" aria-describedby="tvLive"><div class="hmwrap" id="tvHome"></div><div class="tvsim" id="tvSim" hidden></div><div class="tvcrt" aria-hidden="true"></div></div>
    <div class="tvpad" id="tvPad" aria-label="On-screen controls"><div class="dpad"><button class="pb up" data-act="up" aria-label="Up">▲</button><button class="pb left" data-act="left" aria-label="Left">◀</button><button class="pb right" data-act="right" aria-label="Right">▶</button><button class="pb down" data-act="down" aria-label="Down">▼</button></div>
     <div class="ssh"><button class="pb sel" data-act="select" aria-label="SELECT, game details">SELECT</button><button class="pb start" data-act="start" aria-label="START, main menu">START</button></div>
     <div class="abh"><button class="pb b" data-act="b" aria-label="B, back">B</button><button class="pb a" data-act="a" aria-label="A, launch or choose">A</button></div>
     <div class="small tvhint">Arrows D-pad · Enter / Z = A · Backspace / X = B · S = SELECT · M = START · hold a key to repeat · Esc: the HOME's or game's menu · a controller works too</div></div>
   </div>
   <aside class="tvside" id="tvSide"></aside>
  </div>
  <div class="tvfoot" id="tvFoot"></div>
  <div class="vis-hidden" id="tvLive" aria-live="polite"></div>
 </div>`;
 document.body.appendChild(m);
 $('tvClose').onclick=()=>closeTv(); $('tvSave').onclick=()=>tvSaveImage(); $('tvPop').onclick=()=>{ if(typeof TP!=='undefined'&&TP.popped) tpFront(); else if(typeof tpPopOut==='function') tpPopOut(); };
 $('tvTrC').onclick=()=>tvSetTreatment('clean'); $('tvTrR').onclick=()=>tvSetTreatment('retro');
 m.querySelectorAll('.tvtheme').forEach(b=>{ b.onclick=()=>{ tvApplyEffects([{type:'theme',id:b.dataset.theme},{type:'persist'}]); TV.S.theme=b.dataset.theme; tvDraw(); $('tvStage').focus({preventScroll:true}); }; });
 const pad=$('tvPad'); pad.addEventListener('pointerdown',e=>{ const b=e.target.closest('[data-act]'); if(!b) return; e.preventDefault(); tvPress(b.dataset.act); });
 ['pointerup','pointercancel','pointerleave'].forEach(ev=>pad.addEventListener(ev,()=>tvReleaseAll()));
 ['pointerup','touchend'].forEach(ev=>pad.addEventListener(ev,()=>{ if(typeof CCSPLAY!=='undefined') CCSPLAY.wakeAudio(); },{passive:true}));   // the release of the A that launched a game: a phone's browser starts its sound on a release, not on a touch press (ccs-play.js)
 tvHoldWhilePressed($('tvHome'), window);
 $('tvHome').addEventListener('click',e=>{ const t=(e.composedPath?e.composedPath():[e.target]).find(x=>x&&x.dataset&&x.dataset.id); if(!t||!TV.S||TV.S.overlay) return; const L=homeListing(TV.model,TV.S,tvSearch()); const i=L.ids.indexOf(t.dataset.id); if(i<0) return; if(TV.S.zone==='games'&&i===TV.S.index) tvAct('a'); else { TV.S.index=i; TV.S.zone='games'; tvDraw(); } });
 m.addEventListener('keydown',e=>tvKey(e)); m.addEventListener('keyup',e=>tvKeyUp(e));   // the current tvKey (tv-player.js wraps it): one listener, one key line per press
 m.addEventListener('pointerdown',e=>{ TV.downOnBackdrop=e.target===m; },true); m.addEventListener('click',e=>{ if(e.target===m&&TV.downOnBackdrop) closeTv(); });   // only a click that also began outside the Preview closes it (a press inside released out here is a canceled gesture)
 if(window.ResizeObserver) new ResizeObserver(()=>homeFit($('tvHome'))).observe($('tvStage'));
 // one scroll area: re-measured whenever the area below the header, the stage or the pad changes size (window, header wrap, footer)
 if(window.ResizeObserver){ const ro=new ResizeObserver(tvFlowSoon); [m.querySelector('.tvmain'),$('tvStage'),$('tvPad')].forEach(e=>ro.observe(e)); }
 try{ const s=JSON.parse(localStorage.getItem(TV_KEY)||'{}'); if(s.treatment) TV.treatment=s.treatment; }catch(e){}
}
// who scrolls: the side panel by itself while the stage's column fits below the header; otherwise (#tv.tvflow, tv.css) the side panel
// flows and the area below the header is the only scroll area. Measured from the layout, after it settles (next frame), and always as
// the side panel scrolling by itself: switching adds or removes this area's scroll bar, which changes the stage's width and so the
// column's height, so a fit measured in the state it chose could undo the choice, frame after frame (Codex NF7-F1)
function tvFlowSoon(){ if(TV.flowRaf) return; TV.flowRaf=requestAnimationFrame(()=>{ TV.flowRaf=0; tvFlow(); }); }
function tvFlow(){ const m=$('tv'); if(!m||!TV.open) return; const main=m.querySelector('.tvmain'), wrap=m.querySelector('.tvstagewrap'); if(!main||!wrap) return;
 const was=m.classList.contains('tvflow'), y=main.scrollTop; if(was) m.classList.remove('tvflow');
 const cs=getComputedStyle(main), room=main.clientHeight-parseFloat(cs.paddingTop)-parseFloat(cs.paddingBottom), top=wrap.getBoundingClientRect().top;
 const need=[...wrap.children].reduce((b,c)=>Math.max(b,c.getBoundingClientRect().bottom-top),0), flow=need>room+1;
 m.classList.toggle('tvflow', flow); if(was&&flow) main.scrollTop=y; }   // no paint in between; the reader's place is kept
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
 const m=$('tv'); m.hidden=false; m.classList.add('open'); document.body.classList.add('tv-open');
 tvApplyTreatment(); tvDraw(); tvFlowSoon(); $('tvStage').focus({preventScroll:true}); if(!TV.clock) TV.clock=setInterval(()=>{ if(TV.open&&!TV.held) tvDraw(true); },30000);
 if(opts.focus&&!TV.model.order.includes(opts.focus)){ const g=byId[opts.focus]; tvSay(`${g?g.display_title:'That game'} is not in this ${tvPlanWord()}, so it is not in this HOME.`); TV.subNote=`${g?g.display_title:'That game'} is not in this ${tvPlanWord()}, so the preview cannot show it`; $('tvSub').textContent=TV.subNote; }
 if(typeof hoLog==='function') hoLog('tv open n='+TV.model.order.length+' theme='+TV.S.theme);
}
function closeTv(){ const m=$('tv'); if(!m||!TV.open) return; tvReleaseAll(); TV.open=false; TV.subNote=null; homeExtrasSave(); m.classList.remove('open'); m.hidden=true; document.body.classList.remove('tv-open'); const r=TV.returnTo; TV.returnTo=null; try{ if(r&&r.focus) r.focus({preventScroll:true}); }catch(e){} }
function tvSetMode(mode){ tvDraw(); }
function tvSetTreatment(t){ TV.treatment=t; tvPersist(); tvApplyTreatment(); }
function tvApplyTreatment(){ const st=$('tvStage'); if(!st) return; st.classList.toggle('retro',TV.treatment==='retro'); [['tvTrC','clean'],['tvTrR','retro']].forEach(([id,t])=>{ const b=$(id); b.classList.toggle('active',TV.treatment===t); b.setAttribute('aria-checked',TV.treatment===t?'true':'false'); }); }
function tvSay(t){ const l=$('tvLive'); if(l) l.textContent=t; }

// ---- input: host keys and the on-screen pad are emulations of the five console controls; held buttons repeat with measured acceleration ----
const TV_KEYMAP={ArrowUp:'up',ArrowDown:'down',ArrowLeft:'left',ArrowRight:'right',Enter:'a',z:'a',Z:'a',' ':'a',Backspace:'b',x:'b',X:'b',s:'select',S:'select',m:'start',M:'start'};
// during a simulated launch, a NEW press of any control returns at once; the press that started the launch (still held, or its
// auto-repeat) does not. The returning press does nothing else: it is held without acting or repeating until it is released.
function tvPress(act){ if(!TV.open) return; if(TV.launching){ if(TV.held&&TV.held.act===act) return; tvReleaseAll(); TV.held={act,t0:performance.now()}; tvLaunchDone(); return; } if(TV.held&&TV.held.act===act) return; tvReleaseAll(); TV.held={act,t0:performance.now()}; tvAct(act); if(['up','down','left','right'].includes(act)){ const st=TV.S; homeRelease(st); homeRepeatStep(st,act,0); const held=TV.held;   // each repeat is timed for its planned moment (400 ms, then 110 ms, 55 ms after six)
  const tick=()=>{ if(TV.held!==held) return; if(homeRepeatStep(TV.S,act,performance.now()-held.t0)) tvAct(act); if(TV.held!==held) return; TV.repeatTimer=setTimeout(tick,Math.max(0,Math.ceil(TV.S.repeat.next-(performance.now()-held.t0)))); };   // rounded up: a timer never fires before the repeat is due
  TV.repeatTimer=setTimeout(tick,HOME_REPEAT.delay); } }
function tvReleaseAll(){ if(TV.repeatTimer){ clearTimeout(TV.repeatTimer); TV.repeatTimer=0; } if(TV.S) homeRelease(TV.S); TV.held=null; }
function tvKey(e){
 const t=e.target; if(t&&t.matches&&t.matches('input,textarea,select')) return;
 if(e.key==='Escape'){ e.preventDefault(); closeTv(); return; }
 if(e.key==='Tab'){ tvTrapTab(e); return; }
 const act=TV_KEYMAP[e.key]; if(!act) return; if(t&&t.closest&&(t.closest('.tvtools,.tvside')||(t.closest('button,a[href],select')&&(!t.closest('#tvPad')||t.closest('[data-hmnote]'))))&&(e.key==='Enter'||e.key===' ')) return;   // Enter or Space on a focused control presses that control (Close and the pad's HOME Music button included), not A on the HOME
 e.preventDefault(); if(e.repeat) return; tvPress(act);
}
function tvKeyUp(e){ const act=TV_KEYMAP[e.key]; if(act&&TV.held&&TV.held.act===act) tvReleaseAll(); }
function tvTrapTab(e){ const m=$('tv'); const f=[...m.querySelectorAll('button:not([disabled]),[tabindex="0"],select,input,a[href]')].filter(x=>x.offsetParent!==null); if(!f.length) return; const first=f[0], last=f[f.length-1]; if(e.shiftKey&&document.activeElement===first){ e.preventDefault(); last.focus(); } else if(!e.shiftKey&&document.activeElement===last){ e.preventDefault(); first.focus(); } }

// ---- the state machine drives everything; effects are applied here ----
function tvAct(act){ if(!TV.open||!TV.model) return; TV.subNote=null;   /* the excluded game's explanation stays until the person acts (a later redraw keeps it) */ if(TV.launching){ tvLaunchDone(); return; } const r=homeStep(TV.S,TV.model,act,{search:tvSearch(),geometry:homeGeometry(TV.S.theme,TV.model),descMax:homeDescMax($('tvHome')),music:!!(typeof hmMusicBadge==='function'&&hmMusicBadge())}); TV.S=r.state; tvApplyEffects(r.effects); TV.S=homeReconcileState(TV.S,TV.model,tvSearch()); tvDraw(); }
function tvApplyEffects(fx){ let persist=false; for(const f of fx){
 if(f.type==='launch') tvLaunchSim(f.id);
 else if(f.type==='favorite') homeSetFavorite(TV.model,f.id,f.on);
 else if(f.type==='sort') homeSetSort(TV.model,f.location,f.sort);
 else if(f.type==='theme'){ TV.model.theme=f.id; }
 else if(f.type==='setting'){ TV.model.settings[f.key]=f.value; }
 else if(f.type==='clear-history') homeClearHistory(TV.model);
 else if(f.type==='say') tvSay(f.text);
 else if(f.type==='game_settings'){ const g=f.id&&TV.model.games.find(x=>x.id===f.id); if(typeof gsOpen==='function'){ if(g) gsOpen(g.id, Object.assign({}, g, typeof byId!=='undefined'&&byId[g.id]?byId[g.id]:{})); else gsChoose(TV.model.games); } }
 else if(f.type==='gameplay_defaults'){ if(typeof gsDefaults==='function') gsDefaults(); }
 else if(f.type==='controllers'){ if(typeof gsControllers==='function') gsControllers(); }
 else if(f.type==='persist') persist=true; }
 if(persist) homeExtrasSave(); }
// a launch is simulated: the console would start the game now; here a labeled interstitial shows, the launch is recorded in the history, and the HOME returns to the same spot
function tvLaunchSim(id){ const g=TV.model.games.find(x=>x.id===id); if(!g) return; homeRecordLaunch(TV.model,id); homeExtrasSave(); TV.launching=id; const el=$('tvSim'); el.hidden=false; el.innerHTML=`<div class="box"><b>${esc(g.title)}</b><p>Starts on your NES Classic.</p><span class="small">Press any button to return</span></div>`; tvSay(`${g.title}. Starts on your NES Classic. Press any button to return.`); if(TV.simTimer) clearTimeout(TV.simTimer); TV.simTimer=setTimeout(tvLaunchDone,2200); }
function tvLaunchDone(){ if(!TV.launching) return; TV.launching=null; const el=$('tvSim'); if(el){ el.hidden=true; el.innerHTML=''; } if(TV.simTimer){ clearTimeout(TV.simTimer); TV.simTimer=0; } tvDraw(); }

// ---- draw ----
// A press on the HOME holds its picture still until that gesture's click: a redraw (the 30 s clock, a music or state change) replaces
// the HOME's elements, and a click whose press and release land on two copies of a control is never delivered (the music note, a game).
// The click releases the hold before its own handlers run (they draw at once); a waiting redraw follows. No click (released elsewhere,
// dragged off, canceled): released 0.4 s after the release. A press held 1.5 s no longer holds. Nothing acts on the press itself.
function tvHoldWhilePressed(home, win){
 home.addEventListener('pointerdown',e=>{ if(e.isPrimary){ TV.hold=performance.now(); clearTimeout(TV.holdUp); } },true);
 win.addEventListener('pointerup',()=>{ if(TV.hold){ clearTimeout(TV.holdUp); TV.holdUp=setTimeout(tvHoldEnd,400); } },true);
 win.addEventListener('pointercancel',()=>{ if(TV.hold) tvHoldEnd(); },true);
 win.addEventListener('click',()=>{ if(TV.hold){ TV.hold=0; clearTimeout(TV.holdUp); TV.holdUp=setTimeout(tvHoldEnd,0); } },true); }
function tvHeld(quiet){ if(!TV.hold||performance.now()-TV.hold>1500) return false; TV.drawLater=TV.drawLater==null?!!quiet:TV.drawLater&&!!quiet; return true; }
function tvHoldEnd(){ clearTimeout(TV.holdUp); TV.hold=0; if(TV.drawLater!=null){ const q=TV.drawLater; TV.drawLater=null; tvDraw(q); } }
function tvDraw(quiet){
 if(!TV.open||!TV.model||!TV.S) return; if(tvHeld(quiet)) return; const m=TV.model; const S=TV.S; const sub=$('tvSub'); const smp=m.sample;
 sub.textContent=TV.subNote||(smp?`No games in this ${tvPlanWord()} yet · Sample: the 30 built-in games`:`${tvPlanName()} · ${(HOME_THEME_META.find(t=>t.id===tvShownTheme())||{}).name}`);
 document.querySelectorAll('.tvtheme').forEach(b=>{ const on=b.dataset.theme===S.theme; b.classList.toggle('active',on); b.setAttribute('aria-checked',on?'true':'false'); }); const tn=$('tvThemeName'); if(tn&&!(typeof TP!=='undefined'&&TP.app&&TP.on)) tn.textContent=(HOME_THEME_META.find(t=>t.id===S.theme)||{}).name||'';
 homeRender($('tvHome'),m,S,tvCtx());
 const L=homeListing(m,S,tvSearch()); const id=S.overlay&&S.overlay.type==='details'?S.overlay.id:(S.section.type==='search'?L.ids[S.resultIndex]:L.ids[S.index]); TV.focusId=id||null;
 tvSide(id?tvEntries().find(e=>e.id===id)||null:null); tvFoot();
 if(!quiet){ const g=id?m.games.find(x=>x.id===id):null; tvSay(S.overlay?`${S.overlay.type} open`:g?`${g.title}, ${S.index+1} of ${L.ids.length}${smp?', sample':''}`:L.empty||'HOME'); }
 tvKeepFocus();
}
function tvKeepFocus(){ const a=document.activeElement; const m=$('tv'); if(!m||!TV.open) return; if(!a||a===document.body||!m.contains(a)||!a.isConnected) { try{ $('tvStage').focus({preventScroll:true}); }catch(e){} } }
function tvKindText(k){ return {builtin:'built-in game',"factory-kept":'built-in game, factory version',added:'added game',alternate:'Game Library version instead of the factory version (needs your approval)'}[k]||k; }
// the theme the picture shows: the Windows app's player can show another one than the HOME's own choice (the toolbar's Theme menu)
function tvShownTheme(){ return (typeof TP!=='undefined'&&TP.app&&TP.on&&typeof TVN!=='undefined'&&TVN.theme)||(TV.S&&TV.S.theme); }
function tvSide(e){
 const p=tvProject(TV.mode); const s=$('tvSide'); if(!s) return; const m=TV.model, S=TV.S;
 const th=(HOME_THEME_META.find(t=>t.id===tvShownTheme())||{}).name||tvShownTheme();
 let h=p.sample?`<div class="tvs sample"><div class="lbl">No games yet</div><b>Sample: the 30 built-in games</b><div class="small">${esc(TV_SAMPLE_NOTICE)}</div></div>`:`<div class="tvs"><div class="lbl">${tvLoadout()?'NES Classic Loadout':'Collection'}</div><b>${esc(tvPlanName())}</b><div class="small">${tvPl(p.count,'game')} in this HOME · ${p.counts.builtin+p.counts.kept} built-in kept · ${p.counts.added+p.counts.alternate} added · ${p.hidden.length} built-in hidden</div></div>`;
 h+=`<div class="tvs"><div class="lbl">HOME</div><b>${esc(th)}</b><div class="small">${esc(S.section.type==='folder'?('Folder: '+((homeFolder(m,S.section.id)||{}).name||'')):S.section.type==='category'?('Category: '+((homeCategory(m,S.section.id)||{}).label||'')):(TV_SECTION_NAMES[S.section.type]||S.section.type))} · ${tvPl((m.folders||[]).length,'folder')} · ${tvPl(homeFavorites(m).length,'favorite')} · ${homeHistoryEvents(m).length} launch${homeHistoryEvents(m).length===1?'':'es'} recorded</div><div class="small">Folders come from Console Menu Setup; favorites and play history are kept with this ${tvPlanWord()}. START → Change Theme switches themes; the Theme menu above is a shortcut.</div></div>`;
 if(e){ h+=`<div class="tvs"><div class="lbl">Selected game</div><b>${esc(e.title)}</b><div class="small">${esc(e.version)}</div>
  <dl class="tvkv"><dt>Status</dt><dd>${esc(tvKindText(e.kind))}</dd>
  <dt>Game file</dt><dd>${e.ready==='n/a'?'not needed (built into the console)':typeof tpFileText==='function'&&tpFileText(e)?tpFileText(e):e.ready==='ready'?'ready on this computer':e.ready==='missing'?'missing on this computer':'shown in the Windows app'}</dd>
  <dt>On the NES Classic</dt><dd>${esc(tvCoreText(e.core))}</dd><dt>Here (Play)</dt><dd>FCEUmm, the emulator built into Classic Cart Studio</dd>${e.hardware?`<dt>Controller</dt><dd>${esc(e.hardware)}</dd>`:''}${e.personal?`<dt>Source</dt><dd>your own game</dd>`:e.kept?`<dt>Source</dt><dd>imported with this collection</dd>`:''}
  <dt>Cover</dt><dd>${(()=>{ const g=m.games.find(x=>x.id===e.id); const pv=g&&g.art?g.art.provenance:'none'; if(typeof OVR!=='undefined'&&OVR.games&&OVR.games[e.id]&&OVR.games[e.id].art) return 'your replacement artwork'; return pv==='generated'?'generated placeholder (marked GENERATED COVER)':e.kept&&pv!=='none'?'the image kept with this collection':pv==='authentic'?'Game Library box art':pv==='uploaded'?'your own image':'none'; })()}</dd></dl></div>`; }
 h+=`<div class="tvs"><div class="lbl">Controls</div><div class="small">The console has only the D-pad, A, B, START and SELECT. A launches, SELECT opens the game's details, START opens the menu, B goes back.</div></div>`;
 if(!p.sample) h+=tvCompareHtml();
 const was=s.querySelector('#tvSnap'), keep=was?was.value:'';   // the chosen comparison survives a redraw (a new highlight, a theme, the games kept here arriving)
 s.innerHTML=h; const sel=s.querySelector('#tvSnap'); if(sel){ sel.onchange=()=>{ s.querySelector('#tvDiff').innerHTML=tvDiffHtml(sel.value); }; if(keep&&[...sel.options].some(o=>o.value===keep)){ sel.value=keep; sel.onchange(); } }
}
function tvFoot(){ const f=$('tvFoot'); if(f) f.innerHTML=''; }

// ---- comparison (read-only, honest): no console facts are invented ----
function tvCompareHtml(){
 let snaps=[]; try{ snaps=JSON.parse(localStorage.getItem(SKEY)||'[]'); }catch(e){}
 const opts=snaps.map((s,i)=>`<option value="${i}">${esc(nowShort(s.at))} · ${esc(s.name)} · ${(s.sel||[]).length} games</option>`).join('');
 return `<div class="tvs"><div class="lbl">Compare</div>${snaps.length?`<div class="small">Compare with a snapshot:</div><select id="tvSnap" class="tvsel"><option value="">Choose a snapshot…</option>${opts}</select><div id="tvDiff"></div>`:'<div class="small">No snapshots yet. They are saved automatically before bigger changes.</div>'}</div>`;
}
function tvDiffHtml(i){
 if(i==='') return ''; let snaps=[]; try{ snaps=JSON.parse(localStorage.getItem(SKEY)||'[]'); }catch(e){} const s=snaps[+i]; if(!s) return '';
 const a=new Set(s.sel||[]), b=new Set([...sel]); const added=[...b].filter(x=>!a.has(x)), removed=[...a].filter(x=>!b.has(x));
 const nm=id=>{ const g=byId[id]; return g?g.display_title+(g.nointro_name&&g.nointro_name!==g.display_title?' ('+g.nointro_name+')':''):id; };
 return `<div class="small tvdiff"><div><b>${added.length}</b> added: ${added.length?esc(added.map(nm).join(', ')):'none'}</div><div><b>${removed.length}</b> removed: ${removed.length?esc(removed.map(nm).join(', ')):'none'}</div><div class="mut">Compared with the snapshot, not with a console.</div></div>`;
}

// ---- Preview for: Browser HOME (high resolution, the default each time the Studio opens) or NES Classic Console HOME (the console's pixel
//      appearance). Appearance only: the theme, the selected game, the collection or loadout and its order, the open menu and every setting
//      stay as they are; nothing launches and the console's chosen theme is never changed. Play This HOME on PC is always high resolution. ----
let tvAppearanceHook=null;   // tv-native.js: the Windows app's native renderer switches its own appearance
function tvSetAppearance(ap){ TV.appearance=ap==='console'?'console':'browser'; HM_HD=TV.appearance==='browser';
 document.querySelectorAll('.tvap').forEach(b=>{ const on=b.dataset.ap===TV.appearance; b.classList.toggle('active',on); b.setAttribute('aria-pressed',on?'true':'false'); });
 if(!(typeof tvAppearanceHook==='function'&&tvAppearanceHook(TV.appearance))) tvDraw(true); const st=$('tvStage'); if(st) st.focus({preventScroll:true}); }
// ---- Save Preview Image: the stage drawn at 1920x1080 from the model (scenery + the covers near the selection), not a DOM capture ----
async function tvSaveImage(){
 const m=TV.model, S=TV.S; const L=homeListing(m,S,tvSearch()); const ids=L.ids; const W=1920,H=1080; const c=document.createElement('canvas'); c.width=W; c.height=H; const x=c.getContext('2d');
 const load=src=>new Promise(res=>{ const im=new Image(); if(/\/overrides\//.test(src)&&/^https?:/.test(src)&&!src.startsWith(location.origin)) im.crossOrigin='anonymous'; im.onload=()=>res(im); im.onerror=()=>res(null); im.src=src; });   // the person's images come from the backend with CORS, so the saved image stays exportable
 const bg=await load('brand/home/'+hmSceneFile(S.theme)); if(bg) x.drawImage(bg,0,0,W,H); else { x.fillStyle='#0b0b0e'; x.fillRect(0,0,W,H); }   /* the theme's own background, at its full resolution */
 x.fillStyle='rgba(0,0,0,.55)'; x.fillRect(0,0,W,H); x.fillStyle='#fff'; x.font='700 44px "Segoe UI", system-ui, sans-serif'; x.textBaseline='middle'; x.fillText('Classic Cart Studio',60,60); x.font='500 28px "Segoe UI", system-ui, sans-serif'; x.fillText((m.sample?'Sample of the 30 built-in games':tvPlanName())+' · '+((HOME_THEME_META.find(t=>t.id===S.theme)||{}).name||S.theme),60,112);
 const vis=7; let start=Math.max(0,Math.min(S.index-Math.floor(vis/2),ids.length-vis)); if(ids.length<=vis) start=0; const shown=ids.slice(start,start+vis); const tw=226, th=320, gap=24; const total=shown.length*tw+(shown.length-1)*gap; let px=(W-total)/2; const ty=H*0.30;
 for(let k=0;k<shown.length;k++){ const id=shown[k]; const g=m.games.find(v=>v.id===id); const selx=start+k===S.index; const sc=selx?1.18:1; const w=tw*sc, h=th*sc; const xx=px-(w-tw)/2, yy=ty+(th-h)/2;
  x.save(); if(selx){ x.shadowColor='rgba(0,0,0,.45)'; x.shadowBlur=30; x.shadowOffsetY=12; } x.fillStyle='#111'; x.fillRect(xx,yy,w,h); x.restore();
  const im=g&&g.art&&g.art.src?await load(g.art.src):null; if(im){ try{ x.drawImage(im,xx,yy,w,h); }catch(err){} } else { x.fillStyle='#2a2a33'; x.fillRect(xx,yy,w,h); x.fillStyle='#fff'; x.font=`700 ${Math.round(w*0.09)}px "Segoe UI", system-ui, sans-serif`; x.textAlign='center'; x.fillText(g?g.title.slice(0,18):'',xx+w/2,yy+h*0.5); x.textAlign='left'; }
  if(g&&g.art&&g.art.provenance==='generated'){ x.fillStyle='rgba(0,0,0,.75)'; x.fillRect(xx+w-120,yy+h-26,116,22); x.fillStyle='#ffd166'; x.font='700 11px "Segoe UI", system-ui, sans-serif'; x.fillText('GENERATED COVER',xx+w-114,yy+h-15); }
  if(selx){ x.strokeStyle='#e4000f'; x.lineWidth=8; x.strokeRect(xx-4,yy-4,w+8,h+8); }
  px+=tw+gap; }
 const g=ids.length?m.games.find(v=>v.id===ids[S.index]):null; if(g){ x.fillStyle='#fff'; x.font='700 48px "Segoe UI", system-ui, sans-serif'; x.textAlign='center'; x.fillText(g.title,W/2,ty+th*1.18+70); x.textAlign='left'; }
 x.fillStyle='#d6d4ce'; x.font='500 26px "Segoe UI", system-ui, sans-serif'; x.fillText('A launch · SELECT details · START menu · B back',60,H-50);
 c.toBlob(async b=>{ if(!b) { toast('The preview image could not be created. Try again.'); return; } const res=await ccsSaveFile(csSafeName(`${tvPlanName()||'Collection'} - Preview on TV`,'.png'), b, 'Preview Image'); if(res.ok) toast(res.download?`Your browser is saving "${res.name}" (1920 × 1080) to its downloads.`:`Saved "${res.name}" (1920 × 1080)`); },'image/png');
}
// test hook: the same drawing as a data size without a download
async function tvExportSize(){ const ids=TV.model?homeListing(TV.model,TV.S,tvSearch()).ids:[]; const c=document.createElement('canvas'); c.width=1920; c.height=1080; const x=c.getContext('2d'); x.fillStyle='#0b0b0e'; x.fillRect(0,0,1920,1080); return {w:c.width,h:c.height,entries:ids.length}; }

// ---- entry points ----
function tvWire(){
 const hb=$('tvBtn'); if(hb) hb.onclick=()=>openTv();
 const mi=$('mTv'); if(mi) mi.onclick=()=>{ document.querySelectorAll('.menu').forEach(x=>x.hidden=true); openTv(); };
 // the collection changes while the preview is open: rebuild the model (folders, favorites, history kept), keep the focused game by id
 const _r=window.render; window.render=function(){ const out=_r.apply(this,arguments); if(TV.open){ const cur=TV.focusId; TV.proj=null; homeExtrasSave(); TV.model=homeModelFromStudio(); const L=homeListing(TV.model,TV.S,tvSearch()); const k=cur?L.ids.indexOf(cur):-1; if(k>=0&&TV.S.section.type!=='search') TV.S.index=k; else if(cur&&k<0) tvSay(`The ${tvPlanWord()} changed; the preview was updated.`); TV.S=homeReconcileState(TV.S,TV.model,tvSearch()); tvDraw(); } return out; };
 // details dialog: a button that opens the preview focused on that game (added when the dialog opens)
 document.body.addEventListener('click',e=>{ const b=e.target.closest&&e.target.closest('[data-tvfocus]'); if(b){ openTv({focus:b.dataset.tvfocus}); } });
}
tvWire();
