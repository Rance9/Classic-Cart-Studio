// ---- The collection view: the open collection's games are the main screen, on the website and in the Windows app, on every start.
//      A collection holds only games added with a verified ROM file (Add ROM Files, a backup, the Included Games); the Game Catalog is a
//      lookup inside Add Games (CV_MODE 'catalog', never remembered), where a game is added by choosing its ROM file. Every game of the
//      collection stays on screen; one whose ROM file is no longer here says so (ROM missing) with Locate ROM File… and Remove from This
//      Collection, and HOME refuses the whole collection until it is resolved (rdyGate: every way into HOME, and preparing for an NES Classic).
//      Readiness (RDY) is checked against the real game stores: this browser's game store (CCSROMS, by exact content identity) or the app's
//      ROM check (/api/package/rom-check). A store that cannot be read is "could not be checked", never "missing" and never "empty".
var RDY;   // made on first use: collections.js opens a collection (and draws the list) before this file has run
function rdyS(){ return RDY||(RDY={st:new Map(), gen:0, busy:null, store:'ok', why:'', sig:'', at:0}); }
function cvApp(){ return typeof ccsAppPackages==='function'&&ccsAppPackages(); }
function cvTitle(id){ const g=gameOf(id); return (g&&g.display_title)||String(id); }
// every game of the open collection as a row (Game Library versions, personal games, and entries kept from their details)
function cvRows(){ const out=[]; sel.forEach(id=>{ const g=byId[id]||(typeof keptRow==='function'?keptRow(id):null); if(g) out.push(g); }); return out; }
function rdyOf(id){ return rdyS().st.get(id)||(rdyS().busy?'checking':null); }
function rdyCount(k){ let n=0; sel.forEach(id=>{ if(rdyS().st.get(id)===k) n++; }); return n; }
function rdyProblems(){ const o={missing:[], wrong:[], unknown:[]}; sel.forEach(id=>{ const s=rdyS().st.get(id); if(o[s]) o[s].push(id); }); return o; }
// the check itself: every game of the open collection, from the store that plays it here
async function rdyCheck(){ const gen=++rdyS().gen; const ids=[...sel]; const st=new Map(); let store='ok', why='';
 if(!ids.length){ rdyS().st=st; rdyS().store='ok'; rdyS().why=''; rdyS().at=Date.now(); return rdyS(); }
 if(cvApp()){ let r=null; try{ r=await hkPost('/api/package/rom-check', {games:ids.map(id=>({version_id:id, title:cvTitle(id)}))}); }catch(e){ r=null; }
  if(!r||!r.ok){ store='unchecked'; why=(r&&r.error)||'Classic Cart Studio could not check its game files right now.'; ids.forEach(id=>st.set(id,'unknown')); }
  else { ids.forEach(id=>st.set(id,'missing')); (r.available||[]).forEach(g=>st.set(g.version_id,'ready')); (r.mismatched||[]).forEach(g=>st.set(g.version_id,'wrong')); (r.unreadable||[]).forEach(g=>st.set(g.version_id,'unknown')); if((r.unreadable||[]).length){ store='unreadable-files'; why='Some game files on this computer could not be read.'; } } }
 else if(typeof CCSROMS==='undefined'||typeof CCSPLAY==='undefined'||!CCSPLAY.supported()){ store='unsupported'; why='This browser cannot keep games for playing here.'; ids.forEach(id=>st.set(id,'unknown')); }
 else { let keys; try{ CCSROMS._have=null; keys=await CCSROMS.keys(true); }catch(e){ keys=null; store='unreadable'; why=String(e&&e.message||e); }
  if(!keys) ids.forEach(id=>st.set(id,'unknown'));
  else { const pre=[...keys].map(s=>s.slice(0,16)); const have=new Set(pre); ids.forEach(id=>st.set(id, have.has(String(id).replace(/^p_/,'').slice(0,16))?'ready':'missing')); } }
 if(gen!==rdyS().gen) return RDY;   // a newer check started meanwhile: that one decides
 rdyS().st=st; rdyS().store=store; rdyS().why=why; rdyS().at=Date.now(); return rdyS(); }
// a check soon (the collection, its games or the files changed); the cards and the status line follow
function rdyChanged(now){ clearTimeout(rdyChanged.t); rdyChanged.t=setTimeout(()=>{ rdyS().busy=rdyCheck().finally(()=>{ rdyS().busy=null; rdyS().sig=[...sel].sort().join('|'); try{ render(); }catch(e){} cvPanel(); cvDetailsRefresh(); }); }, now?0:120); }
// the person comes back to the page (files may have been removed meanwhile): checked again
addEventListener('focus', ()=>{ if(CS_STATE==='ok'&&sel.size&&Date.now()-rdyS().at>4000) rdyChanged(); });
document.addEventListener('visibilitychange', ()=>{ if(!document.hidden&&CS_STATE==='ok'&&sel.size&&Date.now()-rdyS().at>4000) rdyChanged(); });
// ---- the mark on a card or row, and what it explains (hover, keyboard focus and a tap or click all reach it) ----
function rdyTexts(){ return {missing:['Game file missing','Its game file is no longer here.'], wrong:['Wrong game version','The file here is a different version of this game.'], unknown:['Not checked','Its game file could not be checked.']}; }   // a function: the start-up render reaches the marks before this file has run
function rdyBadge(g){ if(CV_MODE!=='collection'||!sel.has(g.game_id)) return ''; const s=rdyS().st.get(g.game_id); const t=rdyTexts()[s]; if(!t) return '';
 return `<button type="button" class="rdyx rdy-${s}" data-rdy="${esc(g.game_id)}" title="${esc(t[0]+': '+t[1]+' Click for what to do.')}" aria-label="${esc(t[0]+': '+g.display_title+'. What to do')}"><span aria-hidden="true">${s==='unknown'?'?':'⚠'}</span> ${esc(t[0])}</button>`; }
function rdyExplain(id){ const s=rdyS().st.get(id)||'missing', g=gameOf(id), title=g?g.display_title:id, app=cvApp();
 const what=s==='wrong'?`The game file Classic Cart Studio has for <b>${esc(title)}</b> is a different version of the game, so it cannot be used for this one.`
  :s==='unknown'?`Classic Cart Studio could not check the game file of <b>${esc(title)}</b>${rdyS().why?`: ${esc(rdyS().why)}`:'.'} Nothing was changed. Try again in a moment.`
  :`The game file of <b>${esc(title)}</b> is no longer ${app?'on this computer':'kept in this browser'}. The game stays in this collection with its details${g&&g.kept?'':', saved games and Game Settings'}.`;
 openColl('Game File Missing', title, `<div class="msgbox ${s==='unknown'?'warn':'bad'}" role="alert">${what}</div>
  <p class="small">While a game of this collection has no usable game file, HOME cannot open this collection. Add the file, or remove the game from this collection. Removing it keeps its saved games and Game Settings, and it stays in any other collection.</p>
  <div class="acts"><button class="btn" id="rdyRm">Remove from This Collection</button><span class="sp"></span>${s==='unknown'?'<button class="btn" id="rdyAgain">Check Again</button>':''}<button class="btn hk" id="rdyLoc">Locate Game File…</button></div>`);
 $('rdyRm').onclick=()=>{ cvRemove(id); closeColl(); }; $('rdyLoc').onclick=()=>rdyLocate(id); if($('rdyAgain')) $('rdyAgain').onclick=()=>{ closeColl(); rdyChanged(true); }; }
// ---- Locate ROM File…: the file must be exactly this game and version (its content identity), else nothing is kept ----
async function rdyLocate(id){ const g=gameOf(id), title=g?g.display_title:id;
 if(cvApp()){ closeColl(); toast('Choose the game file in the window that opened');
  const kc=((COLL.missing||{})[id]||{}).content_sha256||((g&&g.personal)||{}).content_sha256||'';   // a game the collection names by its content, with no record here yet (Codex v1p4 F2)
  const r=/^p_/.test(id)?await hkPost('/api/personal/relink',kc?{id, content_sha256:kc}:{id}):await hkPost('/api/library/locate',{version_id:id});
  if(r&&(r.canceled||r.cancelled)) return; if(!r||!r.ok){ cvSaid('Game File Not Added', `<div class="msgbox bad">${esc((r&&r.error)||'That file does not match this game.')} Nothing was changed.</div>`); return; }
  try{ await refreshPersonal(); await companionStatus(); }catch(e){} rdyChanged(true); toast(`Game file found and verified: ${title}`); return; }
 const files=await cvPick('.nes,.unh,.zip', false); if(!files||!files.length) return;
 const want=String(id).replace(/^p_/,'').slice(0,16); let found=null, other=null; const kp=(COLL.missing||{})[id]||{}, pe=(g&&g.personal)||kp.personal||{};
 const tgt={sha:pe.content_sha256||kp.content_sha256||'', header_hex:(pe.detected&&pe.detected.header_hex)||''}; let list;
 try{ list=await cvReadRoms(files, null, tgt); for(const r of list){ if(r.content_sha256.slice(0,16)===want){ found=r; break; } other=other||r; } }catch(e){ cvSaid('Game File Not Added', `<div class="msgbox bad">${esc(String(e&&e.message||e))} Nothing was changed.</div>`); return; }
 if(!found){ cvSaid('Game File Not Added', `<div class="msgbox bad">${other?`That file is not <b>${esc(title)}</b> in this version (it is ${esc(cvKnownName(other))}).`:list&&list.bad&&list.bad.length?`That file was not used: ${esc(list.bad[0].why)}.`:'That file has no NES game in it.'} Nothing was changed.</div><p class="small">Choose the exact file this game was added with. A different version of the game can be added as its own game with <b>Add Game Files</b>.</p>`); return; }
 try{ await CCSROMS.put(found.u8, {sha:found.content_sha256, title, name:found.name, header_hex:found.header_hex||''}); await CCSROMS.persist(); }catch(e){ cvSaid('Game File Not Added', `<div class="msgbox bad">${esc(String(e&&e.message||e))}</div>`); return; }
 closeColl(); rdyChanged(true); if(typeof incRefresh==='function') incRefresh(); toast(`Game file verified and kept in this browser: ${title}`); }
function cvSaid(title, html){ openColl(title, '', html+'<div class="acts"><span class="sp"></span><button class="btn" id="cvOk">OK</button></div>'); $('cvOk').onclick=closeColl; }
// Remove from This Collection: the membership only (saved games, Game Settings, files and other collections are untouched)
function cvRemove(id){ if(!sel.has(id)) return; const t=cvTitle(id); sel.delete(id); persist(); render(); rdyChanged(); try{ if($('details').classList.contains('open')&&$('details').dataset.game===id) $('details').classList.remove('open'); }catch(e){}
 toast(`Removed "${t}" from "${COLL.name}". Its saved games and Game Settings are kept.`); }
// ---- reading ROM files in the browser (the files and ZIP archives a person chose; nothing leaves this browser) ----
function cvPick(accept, multi){ return new Promise(res=>{ const inp=document.createElement('input'); inp.type='file'; inp.accept=accept; inp.multiple=!!multi; inp.onchange=()=>res([...inp.files]); inp.addEventListener('cancel',()=>res([])); inp.click(); }); }
function cvIsNes(u8){ return u8.length>=16&&u8[0]===0x4E&&u8[1]===0x45&&u8[2]===0x53&&u8[3]===0x1A; }
// a file without the iNES header (.unh): only exact content whose cartridge hardware is already known can be used (F2). Returns
// {header_hex} (an Included Games or known build, or the located game's recorded header), {catalog:true} (a Game Catalog version: the
// catalog supplies its header), or null. Never guessed from a file name or extension.
function cvHeaderFor(content, want){ const k=String(content||'').slice(0,16); if(!/^[0-9a-f]{16}$/.test(k)) return null;
 const g=typeof byId!=='undefined'?byId[k]:null; if(g&&g.source!=='personal') return {catalog:true};
 const kb=typeof incBuild==='function'?incBuild(content):null; if(kb&&/^[0-9a-f]{32}$/.test(kb.header_hex||'')) return {header_hex:kb.header_hex};
 if(want&&want.sha===content&&/^[0-9a-f]{32}$/.test(want.header_hex||'')) return {header_hex:want.header_hex};
 return null; }
const CV_UNH_WHY='has no NES header, and its contents are not a game Classic Cart Studio knows, so the cartridge hardware it needs cannot be determined. Use the .nes file with its header';
async function cvOneRom(u8, name, from, want){ const id=await ccsIdentifyBytes(u8, name);
 if(!cvIsNes(u8)){ const h=u8.length>=8192&&u8.length<=16e6?cvHeaderFor(id.content_sha256, want):null; if(!h) return {bad:CV_UNH_WHY}; if(h.header_hex) id.header_hex=h.header_hex; id.headerless=true; }
 return {rom:Object.assign(id, {u8, from})}; }
// want (Locate): {sha: the game's exact content SHA-256, header_hex: its recorded header}, so the exact game's headerless file is usable too
async function cvReadRoms(files, onEach, want){ const out=[], bad=[];
 for(const f of files){ const nm=f.name||'file';
  try{ if(/\.zip$/i.test(nm)){ const z=await ccsUnzip(f); if(ccsIsConsoleBackup(z)){ bad.push({name:nm, why:CCS_CONSOLE_BACKUP_WHY}); continue; } let n=0; for(const en of z.entries){ if(!/\.(nes|unh)$/i.test(en.name)) continue; if(en.method!==0&&en.method!==8||(en.flags&1)||(en.csize&&en.usize/Math.max(1,en.csize)>CCS_LIM.ratio)||en.usize>16e6){ bad.push({name:nm+' › '+en.name, why:'cannot be read safely'}); continue; } const u8=await z.read(en); const one=await cvOneRom(u8, en.name.split('/').pop(), nm, want); if(one.bad){ bad.push({name:nm+' › '+en.name, why:one.bad}); continue; } out.push(one.rom); n++; if(onEach) onEach(out.length); } if(!n&&!bad.some(b=>b.name.startsWith(nm+' ›'))) bad.push({name:nm, why:'no NES game file in it'}); continue; }
   if(f.size>16e6){ bad.push({name:nm, why:'larger than any NES game'}); continue; }
   const u8=new Uint8Array(await f.arrayBuffer()); if(!cvIsNes(u8)&&!/\.unh$/i.test(nm)){ bad.push({name:nm, why:'not an NES game file'}); continue; }   /* a .nes file must carry its header */
   const one=await cvOneRom(u8, nm, nm, want); if(one.bad){ bad.push({name:nm, why:one.bad}); continue; } out.push(one.rom); if(onEach) onEach(out.length); }
  catch(e){ bad.push({name:nm, why:String(e&&e.message||e).slice(0,120)}); } }
 out.bad=bad; return out; }
function cvKnownName(r){ const g=byId[r.content_sha256.slice(0,16)]; return g?`${g.display_title} (${g.nointro_name})`:`a game that is not in the Game Catalog (${r.name})`; }
// one ROM file read in the browser becomes a game of the open collection: a Game Catalog version when its content is one (exactly), else a
// personal game known by its content (p_ + the first 16 hex digits), with its details kept with the collection
function cvAdmitWeb(r){ const vid=r.content_sha256.slice(0,16); if(byId[vid]&&byId[vid].source!=='personal'){ const g=byId[vid]; return {id:vid, title:g.display_title, known:true}; }
 const pid='p_'+vid, title=String(r.name||'Game').replace(/\.(nes|unh)$/i,'').replace(/[_]+/g,' ').trim().slice(0,120)||'Game';
 const ib=typeof incBuild==='function'?incBuild(r.content_sha256):null; const keep=(COLL.missing||{})[pid];
 if(!keep&&ib){ COLL.missing=Object.assign({}, COLL.missing||{}); COLL.missing[pid]=incKept(ib); saveColl(); return {id:pid, title:ib.title, known:true}; }
 if(!keep){ COLL.missing=Object.assign({}, COLL.missing||{}); COLL.missing[pid]={title, version:'', description:'', year:'', publisher:'', source:'personal', content_sha256:r.content_sha256, file_sha256:r.sha256, size:r.size,
  personal:{id:pid, title, version_label:'', content_sha256:r.content_sha256, file_sha256:r.sha256, content_size:Math.max(0, r.size-16), file_size:r.size, format:'ines', file_name:r.name, note:'Added from a game file', user:{title}}}; saveColl(); }
 return {id:pid, title:(keep&&keep.title)||title, known:false}; }
// ---- Add ROM Files ----
async function cvAddFiles(){ if(CS_STATE!=='ok'){ rdyStoreProblem(); return; }
 if(cvApp()||(typeof CO!=='undefined'&&CO.desktop)){ if(!csEnsureActive()) return; return importGames('files', true); }
 if(typeof CCSPLAY==='undefined'||!CCSPLAY.supported()){ cvSaid('Add Game Files', '<div class="msgbox bad">This browser cannot keep games for playing here (it lacks WebAssembly or private storage). Use a current Edge, Chrome, Firefox or Safari, or Classic Cart Studio for Windows.</div>'); return; }
 const files=await cvPick('.nes,.unh,.zip', true); if(!files||!files.length) return; if(!csEnsureActive()) return;
 openColl('Add Game Files', COLL.name, '<div class="msgbox" id="cvAddMsg" role="status">Checking the files…</div>');
 let list; try{ list=await cvReadRoms(files, n=>{ const m=$('cvAddMsg'); if(m) m.textContent=`Checking the files… ${n} game${n===1?'':'s'} found`; }); }catch(e){ cvSaid('Add Game Files', `<div class="msgbox bad">${esc(String(e&&e.message||e))} Nothing was added.</div>`); return; }
 const added=[], already=[], failed=list.bad.slice(), seen=new Set();
 for(const r of list){ if(seen.has(r.content_sha256)) continue; seen.add(r.content_sha256);
  try{ await CCSROMS.put(r.u8, {sha:r.content_sha256, title:r.name, name:r.name, header_hex:r.header_hex||''}); }catch(e){ failed.push({name:r.from===r.name?r.name:r.from+' › '+r.name, why:String(e&&e.message||e).slice(0,160)}); continue; }   // only a file this browser kept joins the collection
  const a=cvAdmitWeb(r); if(sel.has(a.id)) already.push(a); else { sel.add(a.id); added.push(a); } }
 try{ await CCSROMS.persist(); }catch(e){}
 persist(); render(); rdyChanged(true); if(typeof csSaveActiveNow==='function') csSaveActiveNow();
 cvAddSummary(added, already, failed); }
function cvAddSummary(added, already, failed){ const li=a=>`<li>${esc(a.title)}${a.known===false?' <span class="small">(personal game)</span>':''}</li>`;
 openColl('Add Game Files', COLL.name, `<div class="msgbox ${added.length?'ok':failed.length?'warn':''}">${added.length?`Added ${added.length} game${added.length===1?'':'s'} to <b>${esc(COLL.name)}</b>.`:'No game was added.'}${already.length?` ${already.length} ${already.length===1?'was':'were'} already in it.`:''}</div>
  ${added.length?`<ul class="fbitems">${added.slice(0,40).map(li).join('')}${added.length>40?`<li>…and ${added.length-40} more</li>`:''}</ul>`:''}
  ${failed.length?`<div class="msgbox warn"><b>Not added (${failed.length}):</b>${failed.slice(0,20).map(b=>`<div>${esc(b.name)}: ${esc(b.why)}</div>`).join('')}${failed.length>20?'<div>…</div>':''}</div>`:''}
  <p class="small">Games whose file matches a version in the Game Catalog get its details; others are added as personal games under their file name. A private copy of each file is kept ${cvApp()?'on this computer':'in this browser'} for playing; keep your own files or a backup too.</p>
  <div class="acts"><span class="sp"></span><button class="btn hk" id="cvOk">Done</button></div>`); $('cvOk').onclick=closeColl; }
// ---- the Game Catalog lookup: browsing it never changes a collection; a game joins only with its ROM file ----
function cvCatalog(on){ CV_MODE=on?'catalog':'collection'; document.body.classList.toggle('cvcat', !!on); try{ const q=$('q'); if(q) q.placeholder=on?'Search the Game Catalog':'Search this collection'; }catch(e){} window.scrollTo({top:0, behavior:'instant'}); render(); cvPanel(); }
async function cvAddForTitle(id){ const g=gameOf(id); const t=g&&TITLES[g.title_id]; if(!g||!t) return; if(CS_STATE!=='ok'){ rdyStoreProblem(); return; }
 if(cvApp()){ if(!csEnsureActive()) return; openColl('Add a Game File', g.display_title, '<div class="msgbox">Choose the game file for this game in the window that opened. Your file is copied, never changed.</div>'); procSet('collm','native');
  const r=await hkPost('/api/personal/pick',{}); procSet('collm',null); if(r&&(r.canceled||r.cancelled)){ closeColl(); return; }
  if(!r||!r.ok){ cvSaid('Game File Not Added', `<div class="msgbox bad">${esc((r&&r.error)||'Could not read the file.')}</div>`); return; }
  const vid=r.known&&r.known.version_id; if(vid&&t.versions.includes(vid)){ if(!sel.has(vid)){ sel.add(vid); persist(); render(); } closeColl(); rdyChanged(true); toast(`Added ${byId[vid]?byId[vid].nointro_name:g.display_title} to "${COLL.name}"`); return; }
  await refreshPersonal(); const oid=vid||(r.entry&&r.entry.id); cvSaid('Not This Game', `<div class="msgbox warn">That file is not a version of <b>${esc(g.display_title)}</b>${oid&&gameOf(oid)?` (it is ${esc(gameOf(oid).display_title)})`:''}. It was not added to this collection.</div><p class="small">To add a different game, use <b>Add Game Files</b>.</p>`); return; }
 if(typeof CCSPLAY==='undefined'||!CCSPLAY.supported()){ cvSaid('Add a Game File', '<div class="msgbox bad">This browser cannot keep games for playing here.</div>'); return; }
 const files=await cvPick('.nes,.unh,.zip', false); if(!files||!files.length) return; let list; try{ list=await cvReadRoms(files); }catch(e){ list=[]; list.bad=[{why:String(e&&e.message||e)}]; }
 const hit=list.find(r=>t.versions.includes(r.content_sha256.slice(0,16)));
 if(!hit){ cvSaid('Not This Game', `<div class="msgbox warn">${list.length?`That file is not a version of <b>${esc(g.display_title)}</b> (it is ${esc(cvKnownName(list[0]))}).`:list.bad&&list.bad.length?`That file was not used: ${esc(list.bad[0].why)}.`:'That file has no NES game in it.'} Nothing was added.</div><p class="small">To add a different game, use <b>Add Game Files</b>.</p>`); return; }
 if(!csEnsureActive()) return; try{ await CCSROMS.put(hit.u8, {sha:hit.content_sha256, title:g.display_title, name:hit.name, header_hex:hit.header_hex||''}); await CCSROMS.persist(); }catch(e){ cvSaid('Game File Not Added', `<div class="msgbox bad">${esc(String(e&&e.message||e))}</div>`); return; }
 const vid=hit.content_sha256.slice(0,16); if(!sel.has(vid)){ sel.add(vid); persist(); render(); } rdyChanged(true); toast(`Added ${byId[vid]?byId[vid].nointro_name:g.display_title} to "${COLL.name}"`); }
// ---- Play: one game, by itself (not HOME): its file is checked first; the player's own start menu offers its save slots and Start New Game ----
async function cvPlay(id){ const g=gameOf(id); if(!g) return; if(CS_STATE!=='ok'){ rdyStoreProblem(); return; }
 if(typeof CCSPLAY!=='undefined') try{ CCSPLAY.unlockAudio(); }catch(e){}   // the press itself (browsers start sound only from a person's action)
 await rdyCheck(); render(); const s=rdyS().st.get(id); if(s&&s!=='ready'){ rdyExplain(id); return; }
 if(cvApp()||(typeof CO!=='undefined'&&CO.desktop)){ if(typeof playCapable==='function'&&playCapable()) return playDirect(id); cvSaid('Play', '<div class="msgbox bad">The game player is not available in this installation. Run the Classic Cart Studio installer again to repair it.</div>'); return; }
 return cvPlayWeb(id); }
async function cvPlayWeb(id){ if(CCSPLAY.running()){ toast('A game is already open.'); return; }
 if(typeof libFull==='function'&&(typeof TP!=='undefined')&&!TP.lib){ try{ TP.lib=await libFull(); }catch(e){} }
 const g=tpWebGame(id); if(!g.sha){ const k=(COLL.missing||{})[id]; if(k&&k.content_sha256) g.sha=k.content_sha256; }
 if(!g.sha){ cvSaid('Play', `<div class="msgbox bad">${esc(g.title)} has no exact game identity, so it cannot be played here.</div>`); return; }
 return cvPlayWebGame(g); }
// one exact game in the browser's player: {id, title, version_label, sha (content SHA-256), header_hex}; its own start menu offers the save
// slots and Start New Game (nothing is loaded or overwritten by starting)
async function cvPlayWebGame(g){ const id=g.id; if(CCSPLAY.running()){ toast('A game is already open.'); return; }
 const box=$('playv'); const host=$('playvHost'); $('playvTitle').textContent=g.title; box.hidden=false; box.classList.add('open'); document.body.classList.add('playv-open');
 const full=matchMedia('(pointer:coarse)').matches; const surf=CCSPLAY.surface(host, {full}); let res=null;
 try{ res=await CCSPLAY.start(g, surf); }catch(e){ res={error:String(e&&e.message||e), code:e&&e.code}; }
 try{ surf.remove(); }catch(e){} box.classList.remove('open'); box.hidden=true; document.body.classList.remove('playv-open');
 if(typeof gsWebPull==='function') gsWebPull().catch(()=>{});
 if(res&&res.error){ if(res.code==='no_rom'){ rdyChanged(true); rdyExplain(id); return; } cvSaid(`${g.title} did not start`, `<div class="msgbox bad">${esc(res.error)}</div>`); }
 else if(res&&res.writes_failed) cvSaid('A save was not written', `<div class="msgbox warn">${esc(res.writes_failed)}</div>`); }
// ---- every way into HOME (Open HOME, a game's "Show in HOME", HOME Settings' Open HOME, a pop-out) and preparing a collection for an
//      NES Classic go through one check: every game of the collection must have its usable ROM file here. Otherwise nothing opens and the
//      games that need attention are named, with what to do. A storage problem says so (never "missing") ----
async function rdyGate(what){ if(CS_STATE!=='ok'){ rdyStoreProblem(); return false; } if(!csActive()){ cvSaid(what==='console'?'Prepare for NES Classic':'HOME', '<div class="msgbox">Open or create a collection first. HOME shows the open collection.</div>'); return false; }
 if(!sel.size) return true;   // an empty collection: HOME shows the built-in sample, as before
 await rdyCheck(); try{ render(); }catch(e){} const p=rdyProblems(); if(!p.missing.length&&!p.wrong.length&&!p.unknown.length) return true;
 const list=(ids,cls,t)=>ids.length?`<div class="msgbox ${cls}"><b>${t} (${ids.length}):</b><ul class="rdylist">${ids.slice(0,30).map(id=>`<li><button type="button" class="linkbtn" data-rdyfix="${esc(id)}">${esc(cvTitle(id))}</button></li>`).join('')}${ids.length>30?`<li>…and ${ids.length-30} more</li>`:''}</ul></div>`:'';
 const head=what==='console'?`"${esc(COLL.name)}" cannot be prepared for an NES Classic yet.`:`HOME cannot open "${esc(COLL.name)}" yet.`;
 const only=p.unknown.length&&!p.missing.length&&!p.wrong.length;
 openColl(what==='console'?'Prepare for NES Classic':'HOME', COLL.name, `<p><b>${head}</b> ${only?`Classic Cart Studio could not check ${p.unknown.length===1?'one game\'s game file':'the game files of some games'}${rdyS().why?`: ${esc(rdyS().why)}`:'.'} Nothing was changed. Try again in a moment.`:'Every game of a collection needs its game file. Choose a game below to locate its game file or remove it from this collection.'}</p>
  ${list(p.missing,'bad','Game file missing')}${list(p.wrong,'bad','Wrong game version')}${list(p.unknown,'warn','Could not be checked')}
  <div class="acts"><span class="sp"></span><button class="btn" id="rdyClose">Close</button>${p.unknown.length?'<button class="btn hk" id="rdyAgain">Check Again</button>':''}</div>`);
 $('rdyClose').onclick=closeColl; if($('rdyAgain')) $('rdyAgain').onclick=async()=>{ closeColl(); if(await rdyGate(what)) cvToast(what); };
 $('collBody').querySelectorAll('[data-rdyfix]').forEach(b=>b.onclick=()=>rdyExplain(b.dataset.rdyfix));
 if(typeof CCSM!=='undefined'){ try{ CCSM.fail(what==='console'?'install':'play', 'missing_rom'); }catch(e){} }
 return false; }
function cvToast(what){ toast(what==='console'?'Every game has its game file. Choose Prepare for NES Classic again.':'Every game has its game file. Choose Open HOME again.'); }
// the collections themselves could not be read: said plainly, nothing is changed (Try Again reloads; a backup can still be restored)
function rdyStoreProblem(){ openColl('Collections Could Not Be Read', '', `<div class="msgbox bad" role="alert"><b>Classic Cart Studio could not read your collections ${cvApp()||(typeof CO!=='undefined'&&CO.desktop)?'on this computer':'in this browser'}.</b> Nothing was changed or deleted, and nothing new was set up in their place.</div>
  <p class="small">Close other Classic Cart Studio tabs or windows, then choose Try Again. If it keeps happening, your browser's storage for this site may be damaged or blocked; a backup (.ccs) can be restored in another browser or in Classic Cart Studio for Windows.</p>
  <div class="acts"><span class="sp"></span><button class="btn" id="spClose">Close</button><button class="btn hk" id="spAgain">Try Again</button></div>`); $('spClose').onclick=closeColl; $('spAgain').onclick=()=>location.reload(); }
// ---- the panel above the games: what the open collection needs, or what an empty Studio offers (no popup, no tour) ----
function cvPanel(){ const box=$('cvPanel'); if(!box) return; let h='';
 if(CV_MODE==='catalog') h=`<div class="cvbar cat"><div><b>Game Catalog</b><span class="small">Look up a game and add it with its game file. Browsing the catalog never changes a collection.</span></div><button type="button" class="btn hk" id="cvBack">Back to ${esc(COLL.name||'My Collections')}</button></div>`;
 else if(CS_STATE!=='ok') h=`<div class="cvbar bad" role="alert"><div><b>Your collections could not be read.</b><span class="small">Nothing was changed or deleted. Close other Classic Cart Studio tabs or windows, then try again.</span></div><button type="button" class="btn hk" id="cvRetry">Try Again</button></div>`;
 else if(!csActive()){ const st=CS&&CS.starter;
  if(st&&st.state==='preparing') h=`<div class="cvbar" role="status"><div><b>Getting the Included Games ready…</b><span class="small">${esc(st.note||'Checking and keeping the game files.')}</span></div></div>`;
  else if(st&&st.state==='failed') h=`<div class="cvbar warn" role="alert"><div><b>The Included Games could not be prepared.</b><span class="small">${esc(st.why||'')} You can try again, add your own game files or restore a backup.</span></div><button type="button" class="btn hk" id="cvStartRetry">Try Again</button></div>`;
  else if(st&&st.state==='pending') h=`<div class="cvbar" role="status"><div><b>Getting the Included Games ready…</b></div></div>`;
  h+=`<div class="cvempty"><h2>${csList().length?'No collection is open':'You have no collections'}</h2><p>A collection is a set of your games with its own HOME. Add your game files to start one, restore a backup, or create an empty collection.</p>
   <div class="cvacts"><button type="button" class="btn hk" data-cv="add">Add Game Files…</button><button type="button" class="btn" data-cv="restore">Restore a Backup…</button><button type="button" class="btn" data-cv="new">New Collection…</button>${csList().length?'<button type="button" class="btn" data-cv="manage">Choose a Collection…</button>':''}</div></div>`; }
 else { const p=rdyProblems(), n=p.missing.length+p.wrong.length;
  if(n) h=`<div class="cvbar bad" role="status"><div><b>${n} game${n===1?' needs its':'s need their'} game file.</b><span class="small">HOME cannot open this collection until ${n===1?'it is':'they are'} added again or removed from the collection. Choose the mark on a game for what to do.</span></div></div>`;
  else if(p.unknown.length&&rdyS().store!=='ok') h=`<div class="cvbar warn" role="status"><div><b>The game files could not be checked.</b><span class="small">${esc(rdyS().why||'')} Nothing was changed.</span></div><button type="button" class="btn" id="cvCheck">Check Again</button></div>`;
  if(!sel.size) h+=`<div class="cvempty small1"><p><b>${esc(COLL.name)}</b> has no games yet. Add your game files to it, or open another collection.</p><div class="cvacts"><button type="button" class="btn hk" data-cv="add">Add Game Files…</button><button type="button" class="btn" data-cv="catalog">Look Up a Game…</button></div></div>`;
  else if(typeof inclIsStarterView==='function'&&inclIsStarterView()) h+=`<div class="cvintro small">These games are included with Classic Cart Studio. Choose <b>Play</b> on one to start, or add your own game files.</div>`; }
 box.innerHTML=h; box.hidden=!h; const lt=$('letters'); if(lt) lt.hidden=CV_MODE==='collection'&&sel.size<=60;   /* the A–Z jump bar helps a long list only */
 const on=(i,f)=>{ const b=$(i); if(b) b.onclick=f; }; on('cvBack', ()=>cvCatalog(false)); on('cvRetry', ()=>location.reload()); on('cvCheck', ()=>rdyChanged(true)); on('cvStartRetry', ()=>{ if(typeof inclStarter==='function') inclStarter(true); });
 box.querySelectorAll('[data-cv]').forEach(b=>b.onclick=()=>cvAct(b.dataset.cv)); }
function cvAct(a){ if(a==='add') cvAddFiles(); else if(a==='restore') cvRestore(); else if(a==='new') csNewDialog(); else if(a==='manage') csManager(); else if(a==='catalog') cvCatalog(true); else if(a==='home') $('tvBtn').click(); }
// Restore a Backup: the .ccs (or older backup) file chooser that every restore uses (studio/ccs.js)
function cvRestore(){ if(CS_STATE!=='ok'){ rdyStoreProblem(); return; } if(typeof ccsImportPick==='function') ccsImportPick(); else if(typeof csImportPick==='function') csImportPick(); }
// ---- a card's or row's actions: Play in the collection; Add ROM File… in the catalog (never a checkbox: membership is explicit) ----
function cvCardActs(g){ if(CV_MODE==='catalog'&&g.inc){ const inC=sel.has(g.game_id); return `<button type="button" class="btn sm cvplay" data-incplay="${esc(g.inc)}" aria-label="Play ${esc(g.display_title)}"><span aria-hidden="true">▶</span> Play</button><button type="button" class="btn sm cvadd" data-incadd="${esc(g.inc)}" title="Add ${esc(g.display_title)} to ${esc(COLL.name||'a collection')}">${inC?'<span aria-hidden="true">✓</span> In Collection':'Add to Collection'}</button>`; }
 if(CV_MODE==='catalog'){ const inC=(TITLES[g.title_id]||{versions:[]}).versions.some(v=>sel.has(v)); return `<button type="button" class="btn sm cvadd" data-cvadd="${esc(g.game_id)}" title="Add this game to ${esc(COLL.name||'a collection')} with its game file">${inC?'<span aria-hidden="true">✓</span> In Collection':'Add Game File…'}</button>`; }
 const s=rdyS().st.get(g.game_id); return `<button type="button" class="btn sm cvplay${s&&s!=='ready'?' off':''}" data-cvplay="${esc(g.game_id)}" aria-label="Play ${esc(g.display_title)}"><span aria-hidden="true">▶</span> Play</button>`; }
document.addEventListener('click', e=>{ const t=e.target.closest&&e.target.closest('[data-cvplay],[data-cvadd],[data-rdy],[data-cvrm],[data-cvloc]'); if(!t) return; e.preventDefault(); e.stopPropagation(); if(typeof hvHide==='function') hvHide();
 if(t.dataset.cvplay) cvPlay(t.dataset.cvplay); else if(t.dataset.cvadd) cvAddForTitle(t.dataset.cvadd); else if(t.dataset.rdy) rdyExplain(t.dataset.rdy); else if(t.dataset.cvrm) cvRemove(t.dataset.cvrm); else if(t.dataset.cvloc) rdyLocate(t.dataset.cvloc); }, true);
// ---- Game Details: Play, Show in HOME and Remove from This Collection for a game of the collection; Add ROM File… in the catalog ----
function cvDetailsActs(id){ const g=gameOf(id); if(!g) return ''; if(CV_MODE==='catalog'||!sel.has(id)) return `<div class="dacts">${cvCardActs(Object.assign({}, g, {title_id:g.title_id}))}</div>`;
 const s=rdyS().st.get(id); return `<div class="dacts">${s&&s!=='ready'?`<div class="msgbox bad small">${esc((rdyTexts()[s]||rdyTexts().missing)[0])}. ${esc((rdyTexts()[s]||rdyTexts().missing)[1])}</div><button type="button" class="btn sm hk" data-cvloc="${esc(id)}">Locate Game File…</button>`:`<button type="button" class="btn sm hk cvplay" data-cvplay="${esc(id)}"><span aria-hidden="true">▶</span> Play</button>`}<button class="btn sm" data-tvfocus="${esc(id)}" title="Open HOME with this game selected">Show in HOME</button><button type="button" class="btn sm" data-cvrm="${esc(id)}">Remove from This Collection</button></div>`; }
function cvDetailsRefresh(){ try{ const d=$('details'), box=$('detailsBox'); if(!d.classList.contains('open')) return; const a=box&&box.querySelector('.dacts'); const id=d.dataset.game; if(a&&id){ a.outerHTML=cvDetailsActs(id); } }catch(e){} }
// ---- wrappers: HOME, a game's Show in HOME and a pop-out pass the readiness check first; so does preparing for an NES Classic ----
(function(){ const _ot=openTv; openTv=async function(opts){ if(!(await rdyGate('home'))) return; return _ot.apply(this, arguments); };
 if(typeof tpPopOut==='function'){ const _pp=tpPopOut; tpPopOut=async function(){ await rdyCheck(); const p=rdyProblems(); if(p.missing.length||p.wrong.length){ if(typeof tvSay==='function') tvSay('A game of this collection has no game file now, so HOME cannot move to its own window. Close HOME to see which.'); return; } return _pp.apply(this, arguments); }; }
 if(typeof openInstall==='function'){ const _oi=openInstall; openInstall=async function(){ if(COMPANION.hosted&&!COMPANION.desktop&&!COMPANION.served) return _oi.apply(this, arguments); if(!(await rdyGate('console'))) return; if(typeof cfCheck==='function'&&!(await cfCheck())) return; return _oi.apply(this, arguments); }; }
 const _od=openDetails; openDetails=function(id){ const r=_od.apply(this, arguments); try{ $('details').dataset.game=id; const box=$('detailsBox'), slot=box&&box.querySelector('.dpickslot'); if(slot){ const ib=typeof incOfId==='function'?incOfId(id):null; slot.outerHTML=cvDetailsActs(id)+(ib&&ib.edition==='demo'?`<div class="incbuy" data-inc-buy-id="${esc(id)}">Checking your library…</div>`:''); if(ib&&ib.edition==='demo') incRefresh(); } }catch(e){} return r; }; })();
// the readiness follows the collection: another collection opened, games added or removed (persist), a restore or a relink
(function(){ const _p=persist; persist=function(){ const r=_p.apply(this, arguments); const sig=[...sel].sort().join('|'); if(sig!==rdyS().sig){ rdyS().sig=sig; rdyChanged(); } return r; }; })();
// ---- start: the collection view; the Included Games for a new Studio (studio/included.js); the first check of the open collection ----
cvCatalog(false); if(CS_STATE==='ok'&&sel.size) rdyChanged(true); if(typeof inclStarter==='function') inclStarter(false);
$('playvMenu').onclick=()=>{ try{ CCSPLAY.menu(); }catch(e){} };
// the list's own text: the open collection's count (and what needs attention), or the catalog's
function statsHtml(shown){ if(CV_MODE==='catalog') return `${shown.toLocaleString('en-US')} shown · <span class="mut">${typeof libCountText==='function'?libCountText():''}</span>`; const n=sel.size, p=rdyProblems(), bad=p.missing.length+p.wrong.length;
 return `${shown<n?`${shown.toLocaleString('en-US')} of `:''}${n.toLocaleString('en-US')} game${n===1?'':'s'} in this collection${bad?` · <span class="over">${bad===1?'1 needs its game file':`${bad} need their game files`}</span>`:''} · ${fav.size} ★`; }
