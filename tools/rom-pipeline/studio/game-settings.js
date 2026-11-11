// ---- Game Settings (one exact game) and Gameplay Defaults: the game player's model (tools/player/core/gp.h) ----
// A record per exact ROM content (SHA-256) holds only the choices made for that game; a field it lacks follows the default (Use Default),
// and Off is a choice of its own. Rapid fire defaults are the Gameplay Defaults; picture defaults are the Play on PC Settings; A/B swap
// defaults to Off. Three ways in edit the same record: Game Details > Game Settings, HOME Settings > Selected Game Settings, and the game
// window's own menu. In Classic Cart Studio for Windows the records are the Background Helper's files (the game window reads them when a
// game starts); on the website they are the browser game player's own files in this browser's game storage (gsWeb*, below).
const GS={key:'ccs-game-settings-v1', ret:null, sha:'', title:'', version:'', rec:null, draft:null, defaults:null, display:{aspect:'tv',integer:false}, mode:null};
const GS_BUILTIN={rapid:{p1:{a:false,b:false},p2:{a:false,b:false},speed:'medium'}};
const GS_SPEEDS=[['slow','Slow'],['medium','Medium'],['fast','Fast']];
const GS_RAPID_ROWS=[['p1','a','Player 1 · Rapid Fire A'],['p1','b','Player 1 · Rapid Fire B'],['p2','a','Player 2 · Rapid Fire A'],['p2','b','Player 2 · Rapid Fire B']];
const GS_DEFAULTS_TEXT='These defaults apply to all games unless you’ve set rapid fire separately for that game. To customize one game, open its Game Details and choose Game Settings.';
function gsApp(){ return typeof playCapable==='function'&&playCapable(); }
function gsBlank(){ return {schema:'ccs-game-settings-local/1',defaults:null,games:{}}; }
function gsLocal(){ try{ const o=JSON.parse(localStorage.getItem(GS.key)||'null'); return o&&o.schema==='ccs-game-settings-local/1'&&o.games&&typeof o.games==='object'?o:gsBlank(); }catch(e){ return gsBlank(); } }   /* a view of the page's store for comparisons; never what decides */
// the page's own store, read: none stored is none; a store this browser cannot read throws (an unread store is never taken for an empty one)
function gsLocalRead(){ let s; try{ s=localStorage.getItem(GS.key); }catch(e){ throw typeof cpReadError==='function'?cpReadError(e, 'its Game Settings'):e; }
 let o=null; try{ o=JSON.parse(s||'null'); }catch(e){} return o&&o.schema==='ccs-game-settings-local/1'&&o.games&&typeof o.games==='object'?o:gsBlank(); }
function gsLocalSave(o){ try{ localStorage.setItem(GS.key, JSON.stringify(o)); return true; }catch(e){ return false; } }
function gsCanon(v){ if(Array.isArray(v)) return v.map(gsCanon); if(v&&typeof v==='object'){ const o={}; Object.keys(v).sort().forEach(k=>{ if(v[k]!==undefined) o[k]=gsCanon(v[k]); }); return o; } return v; }
// ---- the website (W1): the browser's game player reads these choices from its own files in this browser's game storage (/data/game-settings/
//      <sha>.json and defaults.json, the Windows app's file format). Those files decide: Game Settings, Gameplay Defaults, backups and restores
//      read them at that moment (gsCurrent); a read that fails throws (cpReadError). The page also keeps a copy of them (GS.key) when this
//      browser lets it, but the copy never decides and is never saved in a backup: one that could not be updated or read may be older or
//      blank (A7-F1). A change made here writes the player's file first; a change made in a game's own menu is in its files already.
//      Choices saved on the website before games played here move into the player's files once ----
const GS_DIR='/data/game-settings/';
function gsWebOk(){ return !gsApp()&&typeof cpTx==='function'&&typeof CCSPLAY!=='undefined'&&CCSPLAY.supported(); }
function gsFileRec(sha, title, version, clean){ return Object.assign({schema:'ccs-game-prefs/1', rom_content_sha256:sha, title:String(title||'').slice(0,200), version:String(version||'').slice(0,200)}, clean); }
function gsFileDefaults(d){ return Object.assign({schema:'ccs-game-defaults/1'}, gsCleanDefaults(d)); }
async function gsWebWrite(puts, dels, extra){ const enc=new TextEncoder(), at=new Date().toISOString();
 await cpTx(extra?['files','meta']:['files'],'readwrite',t=>{ const st=t.objectStore('files'); for(const [p,o] of puts) st.put({path:p, bytes:enc.encode(JSON.stringify(o)+'\n').buffer, at}); for(const p of dels||[]) st.delete(p); if(extra) t.objectStore('meta').put(extra); }); }
async function gsWebRead(){ const all=await cpTx(['files'],'readonly',t=>cpReq(t.objectStore('files').getAll(IDBKeyRange.bound(GS_DIR, GS_DIR+'\uffff')))); const out={defaults:null, games:{}}, dec=new TextDecoder();
 for(const f of all||[]){ let j=null; try{ j=JSON.parse(dec.decode(new Uint8Array(f.bytes))); }catch(e){ continue; } const n=f.path.slice(GS_DIR.length), m=/^([0-9a-f]{64})\.json$/.exec(n);
  if(n==='defaults.json'&&j&&j.schema==='ccs-game-defaults/1') out.defaults=j; else if(m&&j&&j.schema==='ccs-game-prefs/1'&&j.rom_content_sha256===m[1]) out.games[m[1]]=j; }
 return out; }
// the page's earlier choices (saved before games played here) into the player's files, once; a file the player already has wins
async function gsWebMigrate(){ const done=await cpTx(['meta'],'readonly',t=>cpReq(t.objectStore('meta').get('gs-moved'))); if(done) return;   /* an unread mark is not "not moved yet" */
 let o; try{ o=gsLocalRead(); }catch(e){ return; }   /* an unreadable copy is not "nothing to move": not marked, so it is tried again next time */
 const w=await gsWebRead(), puts=[]; for(const [sha,r] of Object.entries(o.games||{})){ const c=gsClean(r); if(/^[0-9a-f]{64}$/.test(sha)&&!w.games[sha]&&Object.keys(c).length) puts.push([GS_DIR+sha+'.json', gsFileRec(sha,r.title,r.version,c)]); }
 if(o.defaults&&!w.defaults) puts.push([GS_DIR+'defaults.json', gsFileDefaults(o.defaults)]);
 await gsWebWrite(puts, [], {k:'gs-moved', at:new Date().toISOString(), moved:puts.length}); }
// the player's files -> the Game Settings as they are now (they hold the last change: one made here was written there first): returned, or
// false when this page has no browser player; a failed read throws. The page's copy follows them when this browser lets it (gsCopyKeep); a
// copy that cannot be written is left as it was, and nothing uses it
async function gsWebPull(){ if(!gsWebOk()) return false; let n; try{ await gsWebMigrate(); const w=await gsWebRead(), games={};
  for(const [sha,r] of Object.entries(w.games)){ const c=gsClean(r); if(Object.keys(c).length) games[sha]=Object.assign({title:String(r.title||'').slice(0,200), version:String(r.version||'').slice(0,200)}, c); }
  n={schema:'ccs-game-settings-local/1', defaults:w.defaults?gsCleanDefaults(w.defaults):null, games}; }catch(e){ throw cpReadError(e, 'its Game Settings'); }
 gsCopyKeep(n); return JSON.parse(JSON.stringify(n)); }
function gsCopyKeep(n){ const o=gsLocal(); GS.copyKept=JSON.stringify(n)===JSON.stringify({schema:'ccs-game-settings-local/1', defaults:o.defaults||null, games:o.games||{}})||gsLocalSave(n); return GS.copyKept; }
// what decides (the dialogs, backups, restores): the browser player's files read now, or, where this page has no browser player, the page's
// own store read now; a read that fails throws (never an older copy or an empty stand-in)
async function gsCurrent(){ return (await gsWebPull())||gsLocalRead(); }
// restored choices -> the player's files: only what the restore decided to add or replace (fbSettingsDecide's writes), each written only while
// its file still holds what the restore decided with (one transaction: one changed meanwhile, by a game in another tab, is kept and returned
// in kept). Nothing is removed here
async function gsWebPush(changes){ if(!gsWebOk()||!changes||!changes.length) return {ok:true, written:0, kept:[]}; const enc=new TextEncoder(), dec=new TextDecoder(), at=new Date().toISOString(), kept=[]; let written=0;
 try{ await cpTx(['files'],'readwrite',t=>{ const st=t.objectStore('files');
   for(const c of changes){ const p=c.sha?GS_DIR+c.sha+'.json':GS_DIR+'defaults.json', q=st.get(p);
    q.onsuccess=()=>{ let j=null; try{ j=q.result?JSON.parse(dec.decode(new Uint8Array(q.result.bytes))):null; }catch(e){}
     const now=c.sha?(j&&j.schema==='ccs-game-prefs/1'&&j.rom_content_sha256===c.sha&&Object.keys(gsClean(j)).length?gsClean(j):null):(j&&j.schema==='ccs-game-defaults/1'?gsCleanDefaults(j):null);
     if(JSON.stringify(gsCanon(now))!==JSON.stringify(gsCanon(c.base))){ kept.push(c); return; }
     st.put({path:p, bytes:enc.encode(JSON.stringify(c.sha?gsFileRec(c.sha,c.title,c.version,c.next):gsFileDefaults(c.next))+'\n').buffer, at}); written++; }; } }); }
 catch(e){ return {ok:false, error:typeof cpWhyStore==='function'?cpWhyStore(e):String(e&&e.message||e)}; }
 try{ await gsWebPull(); }catch(e){}   /* the page's copy follows (when this browser lets it) */
 return {ok:true, written, kept}; }
function gsShaOf(id){ const g=(typeof byId!=='undefined'&&byId[id])||(window.keptRow&&keptRow(id)); if(!g) return ''; const s=g.payload_sha256||(g.personal&&g.personal.content_sha256)||g.content_sha256||''; return /^[0-9a-f]{64}$/.test(s)?s:''; }
// a record with only well-formed fields (unknown ones dropped); false is kept
function gsClean(r){ const o={}; if(!r||typeof r!=='object') return o; const b=v=>typeof v==='boolean';
 if(r.rapid&&typeof r.rapid==='object'){ const q={}; ['p1','p2'].forEach(p=>{ const x=r.rapid[p]; if(x&&typeof x==='object'){ const y={}; if(b(x.a)) y.a=x.a; if(b(x.b)) y.b=x.b; if(Object.keys(y).length) q[p]=y; } }); if(GS_SPEEDS.some(s=>s[0]===r.rapid.speed)) q.speed=r.rapid.speed; if(Object.keys(q).length) o.rapid=q; }
 if(r.controls&&b(r.controls.swap_ab)) o.controls={swap_ab:r.controls.swap_ab};
 if(r.picture&&typeof r.picture==='object'){ const q={}; if(r.picture.aspect==='tv'||r.picture.aspect==='square') q.aspect=r.picture.aspect; if(b(r.picture.integer)) q.integer=r.picture.integer; if(Object.keys(q).length) o.picture=q; }
 return o; }
function gsCleanDefaults(d){ const o=JSON.parse(JSON.stringify(GS_BUILTIN)); if(d&&d.rapid){ ['p1','p2'].forEach(p=>['a','b'].forEach(k=>{ const v=d.rapid[p]&&d.rapid[p][k]; if(typeof v==='boolean') o.rapid[p][k]=v; })); if(GS_SPEEDS.some(s=>s[0]===d.rapid.speed)) o.rapid.speed=d.rapid.speed; } return o; }
// what a game uses: each field from its record, else the default (per field and player)
function gsResolve(def, rec, display){ const d=gsCleanDefaults(def), r=gsClean(rec), e={rapid:{p1:{},p2:{}}};
 ['p1','p2'].forEach(p=>['a','b'].forEach(k=>{ const v=r.rapid&&r.rapid[p]&&r.rapid[p][k]; e.rapid[p][k]=typeof v==='boolean'?v:d.rapid[p][k]; }));
 e.rapid.speed=(r.rapid&&r.rapid.speed)||d.rapid.speed; e.swap_ab=r.controls?r.controls.swap_ab:false;
 e.aspect=(r.picture&&r.picture.aspect)||(display&&display.aspect)||'tv'; e.integer=r.picture&&typeof r.picture.integer==='boolean'?r.picture.integer:!!(display&&display.integer); return e; }
async function gsLoad(sha){
 if(gsApp()){ const r=await capi('/api/play/game-settings?sha='+sha,{timeout:15000}); if(!r||!r.ok) throw new Error((r&&r.error)||'The game settings could not be read.'); return {defaults:gsCleanDefaults(r.defaults), record:gsClean(r.record), display:r.display||{aspect:'tv',integer:false}}; }
 const o=await gsCurrent(); return {defaults:gsCleanDefaults(o.defaults), record:gsClean(o.games[sha]), display:{aspect:'tv',integer:false}}; }
async function gsSaveRecord(sha, title, version, rec){ const clean=gsClean(rec);
 if(gsApp()){ const r=await capi('/api/play/game-settings',{method:'POST',body:{sha,title,version,record:clean},timeout:15000}); return r&&r.ok?{ok:true}:{ok:false,error:(r&&r.error)||'The game settings could not be saved.'}; }
 if(gsWebOk()){ const has=Object.keys(clean).length; try{ await gsWebWrite(has?[[GS_DIR+sha+'.json', gsFileRec(sha,title,version,clean)]]:[], has?[]:[GS_DIR+sha+'.json']); }   /* the player's file decides */
  catch(e){ return {ok:false,error:(typeof cpWhyStore==='function'?cpWhyStore(e):'This browser did not keep the settings.')+' Nothing was changed.'}; }
  try{ await gsWebPull(); }catch(e){} return {ok:true}; }   /* the page's copy follows it when this browser lets it */
 let o; try{ o=gsLocalRead(); }catch(e){ return {ok:false, error:String(e.message||e)}; }   /* the page's own store (no browser player here): an unread store is never written over */
 if(Object.keys(clean).length) o.games[sha]=Object.assign({title:String(title||'').slice(0,200),version:String(version||'').slice(0,200)},clean); else delete o.games[sha];
 return gsLocalSave(o)?{ok:true}:{ok:false,error:'This browser did not keep the settings (its storage may be full or turned off). Nothing was changed.'}; }
async function gsLoadDefaults(){ if(gsApp()){ const r=await capi('/api/play/game-defaults',{timeout:15000}); if(!r||!r.ok) throw new Error((r&&r.error)||'The defaults could not be read.'); return gsCleanDefaults(r.defaults); } return gsCleanDefaults((await gsCurrent()).defaults); }
async function gsSaveDefaults(d){ const clean=gsCleanDefaults(d);
 if(gsApp()){ const r=await capi('/api/play/game-defaults',{method:'POST',body:clean,timeout:15000}); return r&&r.ok?{ok:true}:{ok:false,error:(r&&r.error)||'The defaults could not be saved.'}; }
 if(gsWebOk()){ try{ await gsWebWrite([[GS_DIR+'defaults.json', gsFileDefaults(clean)]], []); }catch(e){ return {ok:false,error:(typeof cpWhyStore==='function'?cpWhyStore(e):'This browser did not keep the defaults.')+' Nothing was changed.'}; }
  try{ await gsWebPull(); }catch(e){} return {ok:true}; }   /* the player's file decides; the page's copy follows it when this browser lets it */
 let o; try{ o=gsLocalRead(); }catch(e){ return {ok:false, error:String(e.message||e)}; }   /* the page's own store: an unread store is never written over */
 o.defaults=clean; return gsLocalSave(o)?{ok:true}:{ok:false,error:'This browser did not keep the defaults (its storage may be full or turned off). Nothing was changed.'}; }
// ---- the dialog (its own surface above Game Details and Preview on TV; the page behind stays still: navigation.js BLOCK) ----
function gsModal(){ let m=$('gsetm'); if(m) return m;
 m=document.createElement('div'); m.id='gsetm'; m.className='modal'; m.setAttribute('role','dialog'); m.setAttribute('aria-modal','true'); m.setAttribute('aria-labelledby','gsTitle');
 m.innerHTML='<div class="mbox sbox"><div class="mhd"><span class="hbadge" aria-hidden="true">⚙</span><div class="mhdt"><h2 id="gsTitle">Game Settings</h2><span class="small" id="gsSub"></span></div><button type="button" class="mclose" id="gsClose" aria-label="Close" title="Close">✕</button></div><div id="gsBody"></div></div>';
 document.body.appendChild(m); $('gsClose').onclick=()=>gsClose(); m.addEventListener('click',e=>{ if(e.target===m) gsClose(); });
 m.addEventListener('keydown',e=>{ if(e.key==='Escape'){ e.preventDefault(); e.stopPropagation(); gsClose(); } else if(e.key==='Tab'){ const f=[...m.querySelectorAll('button:not([disabled]),select,input,[tabindex="0"]')].filter(x=>x.offsetParent!==null); if(!f.length) return; const a=f[0], z=f[f.length-1];
  if(e.shiftKey&&document.activeElement===a){ e.preventDefault(); z.focus(); } else if(!e.shiftKey&&document.activeElement===z){ e.preventDefault(); a.focus(); } } });
 return m; }
function gsShow(title, sub, html, ret){ const m=gsModal(); if(!m.classList.contains('open')) GS.ret=ret||GS.retNext||document.activeElement; GS.retNext=null; $('gsTitle').textContent=title; $('gsSub').textContent=sub||''; $('gsBody').innerHTML=html; m.classList.add('open');
 const f=m.querySelector('#gsBody select,#gsBody button,#gsBody input')||$('gsClose'); if(f) f.focus({preventScroll:true}); }
function gsClose(){ const m=$('gsetm'); if(!m||!m.classList.contains('open')) return; m.classList.remove('open'); GS.mode=null; const r=GS.ret; GS.ret=null; if(r&&r.isConnected&&r.focus) r.focus({preventScroll:true}); }
function gsWhenText(){ return gsApp()?'The game window uses these when this game starts. A game window that is already open keeps its settings until it closes; its own menu (hold SELECT + START) changes them at once.'
 :'Saved in this browser. A game played here uses these when it starts; its own menu (hold SELECT + START, or X on a touch screen) changes them at once.'; }
function gsSel(f, opts, val){ return `<select data-f="${f}">${opts.map(([v,l])=>`<option value="${v}"${v===val?' selected':''}>${esc(l)}</option>`).join('')}</select>`; }
function gsTri(v){ return v===true?'on':v===false?'off':'default'; }
function gsDraw(){ const d=GS.draft, e=gsResolve(GS.defaults, {}, GS.display), on=x=>x?'On':'Off';
 const rows=GS_RAPID_ROWS.map(([p,k,l])=>`<label class="gsrow"><span>${esc(l)}</span>${gsSel(`rapid.${p}.${k}`,[['default',`Use Default (${on(e.rapid[p][k])})`],['on','On'],['off','Off']],gsTri(d.rapid&&d.rapid[p]&&d.rapid[p][k]))}</label>`).join('');
 const sp=gsSel('rapid.speed',[['default',`Use Default (${(GS_SPEEDS.find(s=>s[0]===e.rapid.speed)||['','Medium'])[1]})`],...GS_SPEEDS],(d.rapid&&d.rapid.speed)||'default');
 const sw=gsSel('controls.swap_ab',[['default','Use Default (Off)'],['on','On'],['off','Off']],gsTri(d.controls&&d.controls.swap_ab));
 const as=gsSel('picture.aspect',[['default',`Use Default (${e.aspect==='tv'?'TV proportions':'Square pixels'})`],['tv','TV proportions'],['square','Square pixels']],(d.picture&&d.picture.aspect)||'default');
 const it=gsSel('picture.integer',[['default',`Use Default (${on(e.integer)})`],['on','On'],['off','Off']],gsTri(d.picture&&d.picture.integer));
 gsShow('Game Settings', GS.title+(GS.version?' · '+GS.version:''), `<div class="gsset">
  <p class="small">For this exact version only. A choice on Use Default follows the Gameplay Defaults (rapid fire) or the Play on PC Settings (picture).</p>
  <fieldset class="gsgrp"><legend>Rapid Fire (Turbo)</legend><p class="small">Hold A or B to repeat presses automatically. Works with your controller or keyboard.</p>${rows}<label class="gsrow"><span>Speed</span>${sp}</label></fieldset>
  <fieldset class="gsgrp"><legend>Controls</legend><label class="gsrow"><span>Swap A and B</span>${sw}</label><p class="small">In this game only; menus and the HOME keep A and B as they are. Keys and controllers are set in Play on PC Settings and the game window's Controllers screen.</p></fieldset>
  <fieldset class="gsgrp"><legend>Picture</legend><label class="gsrow"><span>Proportions</span>${as}</label><label class="gsrow"><span>Whole-number scaling</span>${it}</label></fieldset>
  <p class="small" id="gsWhen">${esc(gsWhenText())}</p><div class="msgbox warn" id="gsErr" hidden></div><div class="gsconfirm" id="gsConfirm" hidden></div>
  <div class="acts row" style="gap:8px;justify-content:flex-end;flex-wrap:wrap"><button type="button" class="btn sm secondary" id="gsReset">Reset Settings for This Game</button><span style="flex:1"></span><button type="button" class="btn sm secondary" id="gsCancel">Cancel</button><button type="button" class="btn sm" id="gsSave">Save Settings</button></div></div>`);
 $('gsBody').querySelectorAll('select[data-f]').forEach(s=>s.onchange=()=>gsSet(s.dataset.f, s.value));
 $('gsCancel').onclick=()=>gsClose(); $('gsSave').onclick=()=>gsSaveNow();
 $('gsReset').onclick=()=>{ const c=$('gsConfirm'); c.hidden=false; c.innerHTML=`<p>Every choice for ${esc(GS.title)} goes back to Use Default. Saved games, save slots and the cartridge save are kept.</p><div class="row" style="gap:8px;justify-content:flex-end"><button type="button" class="btn sm secondary" id="gsResetNo">Cancel</button><button type="button" class="btn sm" id="gsResetYes">Reset Settings</button></div>`;
  $('gsResetNo').onclick=()=>{ c.hidden=true; $('gsReset').focus(); }; $('gsResetYes').onclick=()=>{ GS.draft={}; gsSaveNow(); }; $('gsResetNo').focus(); }; }
function gsSet(path, v){ const [a,b,c]=path.split('.'); const d=GS.draft; d[a]=d[a]||{};
 if(a==='rapid'&&b!=='speed'){ d.rapid[b]=d.rapid[b]||{}; if(v==='default') delete d.rapid[b][c]; else d.rapid[b][c]=v==='on'; if(!Object.keys(d.rapid[b]).length) delete d.rapid[b]; }
 else if(a==='rapid'){ if(v==='default') delete d.rapid.speed; else d.rapid.speed=v; }
 else if(a==='controls'){ if(v==='default') delete d.controls.swap_ab; else d.controls.swap_ab=v==='on'; }
 else if(b==='aspect'){ if(v==='default') delete d.picture.aspect; else d.picture.aspect=v; }
 else { if(v==='default') delete d.picture.integer; else d.picture.integer=v==='on'; }
 if(!Object.keys(d[a]).length) delete d[a]; }
async function gsSaveNow(){ const b=$('gsSave'); if(b) b.disabled=true; const r=await gsSaveRecord(GS.sha, GS.title, GS.version, GS.draft); if(b) b.disabled=false;
 if(!r.ok){ const e=$('gsErr'); if(e){ e.hidden=false; e.textContent=r.error+' Your choices are still here; try again or cancel.'; } return; }
 gsClose(); toast(Object.keys(gsClean(GS.draft)).length?`Game Settings saved for ${GS.title}.`:`${GS.title} follows every default again.`); }
// open Game Settings for a library or personal game (Game Details) or a HOME game (its model row)
async function gsOpen(id, row, ret){ if(ret) GS.retNext=ret; const sha=(row&&/^[0-9a-f]{64}$/.test(row.content_sha256||'')?row.content_sha256:'')||gsShaOf(id); const g=row||(typeof byId!=='undefined'&&byId[id])||(window.keptRow&&keptRow(id))||{};
 const title=g.display_title||g.title||id, version=g.version_label||g.nointro_name||'';
 if(!sha){ toast('This game has no exact identity, so it has no Game Settings.'); return; }
 let L; try{ L=await gsLoad(sha); }catch(e){ toast(String(e.message||e)); return; }
 Object.assign(GS,{sha,title,version,defaults:L.defaults,display:L.display,rec:L.record,draft:JSON.parse(JSON.stringify(L.record||{})),mode:'game'}); gsDraw(); }
// Gameplay Defaults (HOME Settings): rapid fire for every game without its own choice; the required explanation stays beside the controls
async function gsDefaults(){ let d; try{ d=await gsLoadDefaults(); }catch(e){ toast(String(e.message||e)); return; } GS.mode='defaults'; GS.draft=d; gsDrawDefaults(); }
function gsDrawDefaults(){ const d=GS.draft; const rows=GS_RAPID_ROWS.map(([p,k,l])=>`<label class="gsrow"><span>${esc(l)}</span>${gsSel(`rapid.${p}.${k}`,[['on','On'],['off','Off']],d.rapid[p][k]?'on':'off')}</label>`).join('');
 gsShow('Gameplay Defaults','Rapid Fire (Turbo) for every game', `<div class="gsset"><p class="gsnote" id="gsDefText">${esc(GS_DEFAULTS_TEXT)}</p>
  <fieldset class="gsgrp"><legend>Rapid Fire (Turbo)</legend><p class="small">Hold A or B to repeat presses automatically. Works with your controller or keyboard.</p>${rows}<label class="gsrow"><span>Speed</span>${gsSel('rapid.speed',GS_SPEEDS,d.rapid.speed)}</label></fieldset>
  <p class="small">${esc(gsApp()?'The game window uses the defaults when a game starts.':'Saved in this browser. Games played here use the defaults when they start.')}</p><div class="msgbox warn" id="gsErr" hidden></div>
  <div class="acts row" style="gap:8px;justify-content:flex-end;flex-wrap:wrap"><button type="button" class="btn sm secondary" id="gsBuiltin">Restore Built-In Defaults</button><span style="flex:1"></span><button type="button" class="btn sm secondary" id="gsCancel">Cancel</button><button type="button" class="btn sm" id="gsSave">Save Defaults</button></div></div>`);
 $('gsBody').querySelectorAll('select[data-f]').forEach(s=>s.onchange=()=>{ const [,p,k]=s.dataset.f.split('.'); if(p==='speed') GS.draft.rapid.speed=s.value; else GS.draft.rapid[p][k]=s.value==='on'; });
 $('gsBuiltin').onclick=()=>{ GS.draft=JSON.parse(JSON.stringify(GS_BUILTIN)); gsDrawDefaults(); toast('Built-in defaults shown: save to keep them. Games with their own choices keep them.'); };
 $('gsCancel').onclick=()=>gsClose();
 $('gsSave').onclick=async()=>{ const r=await gsSaveDefaults(GS.draft); if(!r.ok){ const e=$('gsErr'); e.hidden=false; e.textContent=r.error; return; } gsClose(); toast('Gameplay Defaults saved. Games with their own choices keep them.'); }; }
// Controllers & Keyboard (HOME Settings): what this computer actually supports, honestly; not pairing or driver installation
function gsControllers(){ GS.mode='controllers';
 const app=gsApp(); gsShow('Controllers & Keyboard', app?'The game window':'This browser', `<div class="gsset">
  ${app?`<p>In the game window, choose who plays: hold SELECT + START (or press Esc) and choose <b>Controllers…</b>, or, in <b>Preview Your Lineup</b>, open START › <b>Settings</b>. Each connected controller can be chosen for Player 1 or Player 2; the keyboard plays the player no controller plays.</p>
   <p>Xbox-compatible controllers work at once. Other USB, Bluetooth or wireless controllers that Windows already lists are set up there with <b>Set Up a Controller…</b>. Pairing a Bluetooth controller is done in Windows Settings; Classic Cart Studio installs no drivers.</p>
   <p>Keys for both keyboard players, the picture and the player order are in <b>Play on PC Settings</b>.</p>`
  :`<p>In this browser the HOME and its games use the keyboard: arrows move, Z or Enter is A, X or Backspace is B, S is SELECT and M is START. A touch screen shows its own buttons.</p>
   <p>Game controllers work too. A standard controller works at once; another controller asks for a one-time setup the first time you press one of its buttons in a game. Some browsers show a controller only after one of its buttons is pressed.</p>`}
  <div class="acts row" style="gap:8px;justify-content:flex-end">${app?'<button type="button" class="btn sm secondary" id="gsKeys">Play on PC Settings…</button>':''}<button type="button" class="btn sm" id="gsOk">OK</button></div></div>`);
 $('gsOk').onclick=()=>gsClose(); const k=$('gsKeys'); if(k) k.onclick=()=>{ gsClose(); if(typeof playSettings==='function') playSettings(); }; }
// Selected Game Settings with no game chosen: Choose a Game from this HOME
function gsChoose(games){ GS.mode='choose'; const list=(games||[]).slice(0,400);
 gsShow('Choose a Game','Selected Game Settings', `<div class="gsset"><p class="small">No game was highlighted when Settings opened. Choose the game whose settings you want to change.</p>
  <div class="gslist">${list.map((g,i)=>`<button type="button" class="btn secondary gspick" data-i="${i}">${esc(g.title||g.id)}${g.version_label?`<span class="small"> · ${esc(g.version_label)}</span>`:''}</button>`).join('')||'<p class="small">This HOME has no games.</p>'}</div>
  <div class="acts row" style="gap:8px;justify-content:flex-end"><button type="button" class="btn sm secondary" id="gsCancel">Cancel</button></div></div>`);
 $('gsCancel').onclick=()=>gsClose(); $('gsBody').querySelectorAll('.gspick').forEach(b=>b.onclick=()=>{ const g=list[+b.dataset.i]; gsOpen(g.id, g); }); }
// ---- Game Details: a generous Game Settings… button beside Technical Details, for the exact version shown ----
(function(){ const _od=openDetails; openDetails=function(id){ const r=_od.apply(this,arguments); try{ gsDetailsButton(id); }catch(e){} return r; }; })();
function gsDetailsButton(id){ const tech=$('dTech'); const bar=tech&&tech.parentElement; if(!bar||$('dGset')) return;
 const b=document.createElement('button'); b.type='button'; b.className='btn sm secondary gsbtn'; b.id='dGset'; b.innerHTML='<span aria-hidden="true">⚙</span> Game Settings…'; b.title='Rapid fire, controls and picture for this exact version';
 if(!gsShaOf(id)){ b.disabled=true; b.title='This game has no exact identity, so it has no Game Settings.'; }
 tech.after(b); b.onclick=()=>gsOpen(id, null, b); }
// the website: the copy follows the player's files when the page opens (a game's own menu may have changed them last time)
window.addEventListener('load',()=>{ setTimeout(()=>{ if(gsWebOk()) gsWebPull().catch(()=>{}); },0); });   /* the page's copy follows the player's files (nothing decides with it) */
