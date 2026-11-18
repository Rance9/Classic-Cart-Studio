// ---- Export Game Saves… / Import Game Saves…: a person's saved games carried privately between Classic Cart Studio in a browser
//      and Classic Cart Studio for Windows, as one ordinary ZIP file they move themselves (Files, Drive, email, AirDrop…). No ROM file, box
//      art, screenshot, game player, setting or collection travels in it, and nothing is sent anywhere: no account, no server.
//      The file (schema ccs-game-saves/1, the same in both; the Windows app's side is tools/companion/saves_transfer.py): ccs-game-saves.json
//      (when and where it was made, and for each game its exact content identity, title and files: size, SHA-256, kind, a slot's name and the
//      time the save was made) and saves/<content sha>/<file> (cart.sav, slot1..4.ccsstate, slot-names.json, last-used.json, game.json),
//      exactly as the game player wrote them. Browser and Windows saves load in either (the same pinned player); NES Classic saves are not
//      part of this. A save can hold bytes of the game's own memory: "no ROM files" means no ROM file is included.
//      Import checks the whole file and every save by the player's own rules (fbFileCheck) before anything is written; the person chooses,
//      for each save that differs from one here, what happens (nothing here is replaced without that choice); the write is one storage
//      transaction holding the game player's lock (fbWebLock), refused while a game is open, and every save it replaces is kept in that same
//      transaction (replaced-<id>, below; the Windows app keeps its replaced-<tx> folders). Saves of a game whose ROM file is not here are
//      kept anyway (they belong to the exact game, not to a collection; nothing joins a collection): adding that exact ROM file later, or
//      getting an Included Game, makes them playable. ----
const GX_SCHEMA='ccs-game-saves/1', GX_MANIFEST='ccs-game-saves.json', GX_SLOTS=['slot1.ccsstate','slot2.ccsstate','slot3.ccsstate','slot4.ccsstate'];
const GX_FILES=['cart.sav',...GX_SLOTS,'slot-names.json','last-used.json','game.json'];
const GX_MEMBER=/^saves\/([0-9a-f]{64})\/(cart\.sav|slot[1-4]\.ccsstate|slot-names\.json|last-used\.json|game\.json)$/;
const GX_NOTE='Saved games from Classic Cart Studio. No game files are included: each game\'s saves load only with that exact game, added separately. A save can hold bytes from the game\'s own memory.';
const GX={file:null, plan:null, app:null};
function gxApp(){ return typeof fbApp==='function'&&fbApp()&&((COMPANION.info&&COMPANION.info.capabilities)||COMPANION.capabilities||[]).includes('game-saves'); }
function gxLimit(n){ return /\.ccsstate$/.test(n)?(8<<20)+65536:/\.sav$/.test(n)?(1<<20):65536; }
function gxJson(u8){ try{ return JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(u8)); }catch(e){ return null; } }
function gxNames(u8){ const j=u8?gxJson(u8):null, n=j&&j.names&&typeof j.names==='object'?j.names:{}; const o={}; ['1','2','3','4'].forEach(k=>{ if(typeof n[k]==='string') o[k]=n[k].slice(0,96); }); return o; }
function gxEq(a,b){ return !!a&&!!b&&a.length===b.length&&a.every((v,i)=>v===b[i]); }
function gxWhen(s){ const d=s?new Date(s):null; return d&&!isNaN(d)?d.toLocaleString('en-US',{dateStyle:'medium', timeStyle:'short'}):''; }
function gxDescribe(name, u8, sha, names, core, at){ const d={name, kind:name==='cart.sav'?'cart':/\.ccsstate$/.test(name)?'slot':name.slice(0,-5), bytes:u8.length};
 if(/\.ccsstate$/.test(name)){ const n=name[4]; const h=fbFileCheck(name, u8, sha, core)[2]||{}; d.slot=+n; d.saved_at=String(h.created||'').slice(0,40); d.slot_name=(names[n]||'').slice(0,96); }
 if(at) d.modified=String(at).slice(0,40); return d; }
// this browser's saved games, read now ({sha: {name: {u8, at}}}); a read that fails stops (never taken for "no saves")
async function gxHere(){ let all; try{ all=await cpTx(['files'],'readonly',t=>cpReq(t.objectStore('files').getAll(IDBKeyRange.bound('/data/saves/','/data/saves/￿')))); }catch(e){ throw cpReadError(e, 'its saved games'); }
 const by={}; for(const f of all||[]){ const m=/^\/data\/saves\/([0-9a-f]{64})\/([^/]+)$/.exec(f.path); if(!m||!GX_FILES.includes(m[2])) continue; (by[m[1]]=by[m[1]]||{})[m[2]]={u8:new Uint8Array(f.bytes), at:f.at||''}; }
 return by; }
// ---------------- Export Game Saves… ----------------
async function gxExport(){ if(gxApp()) return gxExportApp(); if(typeof fbWebPlay!=='function'||!fbWebPlay()){ cvSaid('Export Game Saves', '<div class="msgbox bad">This browser cannot keep games or saved games here, so there are none to export.</div>'); return; }
 openColl('Export Game Saves', '', '<div class="msgbox" role="status">Reading your saved games…</div>');
 let here, core; try{ here=await gxHere(); core=await fbCore(); }catch(e){ cvSaid('Export Game Saves', `<div class="msgbox bad">${esc(e.message)}</div><div class="acts"><span class="sp"></span><button class="btn hk" data-gx-retry="out">Try Again</button></div>`); return; }
 const games=[], skipped=[], entries=[];
 for(const sha of Object.keys(here).sort()){ const fs=here[sha], ok={};
  for(const n of GX_FILES){ if(!fs[n]) continue; const [st,why]=fbFileCheck(n, fs[n].u8, sha, core); if(st==='ok') ok[n]=fs[n]; else skipped.push({sha, name:n, why}); }
  if(!ok['cart.sav']&&!GX_SLOTS.some(n=>ok[n])) continue;   // a game's notes alone are not saves
  const names=gxNames(ok['slot-names.json']&&ok['slot-names.json'].u8), t=ok['game.json']?gxJson(ok['game.json'].u8):null;
  const g={content_sha256:sha, title:String((t&&t.last_title)||'').slice(0,120), files:[]};
  for(const n of GX_FILES){ if(!ok[n]) continue; const u8=ok[n].u8; const d=gxDescribe(n, u8, sha, names, core, ok[n].at); d.sha256=await ccsSha(u8); g.files.push(d);
   entries.push({name:`saves/${sha}/${n}`, method:0, crc:ccsCrc32(u8), usize:u8.length, csize:u8.length, body:u8}); }
  games.push(g); }
 if(!games.length){ cvSaid('Export Game Saves', '<div class="msgbox">There are no saved games to export yet. Save a game in a slot, or play a game that keeps a cartridge save, first.</div>'); return; }
 const man={schema:GX_SCHEMA, app:'Classic Cart Studio', app_version:String((typeof CCSM!=='undefined'&&CCSM.version)||''), made:new Date().toISOString(), platform:'web-wasm32', core:core?`${core.id} ${core.ver}`:'', note:GX_NOTE, games};
 const mj=new TextEncoder().encode(JSON.stringify(man, null, 1)+'\n'); entries.unshift({name:GX_MANIFEST, method:0, crc:ccsCrc32(mj), usize:mj.length, csize:mj.length, body:mj});
 const blob=new Blob([ccsZipBlob(entries)], {type:'application/zip'}); const d=new Date(); const stamp=`${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
 const res=await ccsSaveFile(`Classic Cart Studio Game Saves ${stamp}.zip`, blob, 'Game saves'); if(!res.ok){ closeColl(); return; }
 gxExported(games.length, entries.length-1, res.name, skipped, res.download); }
async function gxExportApp(){ openColl('Export Game Saves', '', '<div class="msgbox" role="status">Choose where to save the file in the window that opened.</div>'); procSet('collm','native');
 const r=await hkPost('/api/studio/saves-export', {}); procSet('collm',null);
 if(r&&r.canceled){ closeColl(); return; } if(!r||!r.ok){ cvSaid('Export Game Saves', `<div class="msgbox ${r&&/no saved games/.test(r.error||'')?'':'bad'}">${esc((r&&r.error)||'The file could not be saved.')}</div>${r&&r.read_error?'<div class="acts"><span class="sp"></span><button class="btn hk" data-gx-retry="out">Try Again</button></div>':''}`); return; }
 gxExported(r.games, r.files, r.name, r.skipped||[], false); }
function gxExported(games, files, name, skipped, download){ const s=n=>n===1?'':'s';
 cvSaid('Game Saves Exported', `<div class="msgbox ok">${download?'Your browser is saving':'Saved'} the saved games of ${games} game${s(games)} (${files} file${s(files)}) ${download?'to its downloads as':'to'} ${esc(name)}.</div>
  <p class="small">No game files are in it. To continue on another device, move this file there yourself (for example with Files, a cloud drive or email), then choose <b>Import Game Saves…</b> in Classic Cart Studio there. Each game's saves load only with that exact game, added there separately.</p>
  ${skipped.length?`<div class="msgbox warn">${skipped.length} file${s(skipped.length)} not included because the game player would not load ${skipped.length===1?'it':'them'}: ${esc(skipped.slice(0,6).map(x=>x.name+' ('+x.why+')').join('; '))}</div>`:''}`); }
// ---------------- Import Game Saves…: read and check (nothing is written) ----------------
async function gxRead(f){ let z; try{ z=await ccsUnzip(f); }catch(e){ throw new Error('This file is not a Classic Cart Studio Game Saves file (it is not a ZIP file).'); }
 const seen=new Set(); let total=0;
 if(z.entries.length>4001) throw new Error('The file holds more than a Game Saves file can.');
 for(const en of z.entries){ const n=en.name; if(seen.has(n)) throw new Error(`The file names ${n.slice(0,80)} twice, so it was not used.`); seen.add(n);
  if(n.endsWith('/')||((en.ext>>>16)&0xF000)===0xA000||(en.flags&1)||(en.method!==0&&en.method!==8)) throw new Error('The file holds a folder, a link, an encrypted or unusually compressed entry, so it was not used.');
  if(n!==GX_MANIFEST&&!GX_MEMBER.test(n)) throw new Error(`The file holds something that is not a saved game (${n.slice(0,80)}), so it was not used.`);
  const lim=n===GX_MANIFEST?4e6:gxLimit(n); if(en.usize>lim||(en.csize&&en.usize/Math.max(1,en.csize)>400)) throw new Error(`The file is damaged (${n.slice(0,80)} is larger than it can be).`); total+=en.usize; }
 if(total>2e9) throw new Error('The file holds more than a Game Saves file can.');
 const me=z.entries.find(e=>e.name===GX_MANIFEST); if(!me) throw new Error('This file is not a Classic Cart Studio Game Saves file (it has no ccs-game-saves.json).');
 let man; try{ man=gxJson(await z.read(me, 4e6)); }catch(e){ man=null; } if(!man||typeof man!=='object'||man.schema!==GX_SCHEMA) throw new Error(man&&man.schema?'This file is not a Game Saves file this version can read.':'The file\'s list of saves is damaged, so it was not used.');
 { const t=ccsTargetResolve(man,'game-saves',man.games); if(!t.ok) throw new Error(t.text); }   // saves for a game system this version does not support
 const listed=new Map();
 for(const g of Array.isArray(man.games)?man.games:[]){ const sha=g&&g.content_sha256; if(typeof sha!=='string'||!/^[0-9a-f]{64}$/.test(sha)||!Array.isArray(g.files)) throw new Error('The file\'s list of saves is damaged, so it was not used.');
  for(const x of g.files){ const k=`saves/${sha}/${x&&x.name}`; if(!x||typeof x.name!=='string'||!GX_MEMBER.test(k)||listed.has(k)||!Number.isInteger(x.bytes)||typeof x.sha256!=='string') throw new Error('The file\'s list of saves is damaged, so it was not used.'); listed.set(k, x); } }
 const inZip=[...seen].filter(n=>n!==GX_MANIFEST); if(inZip.length!==listed.size||inZip.some(n=>!listed.has(n))) throw new Error('The file\'s list of saves does not match what it holds, so it was not used.');
 const members={};
 for(const en of z.entries){ if(en.name===GX_MANIFEST) continue; let u8; try{ u8=await z.read(en, gxLimit(en.name)); }catch(e){ throw new Error(`The file is damaged (${en.name.split('/').pop()} could not be read).`); }
  const x=listed.get(en.name); if(u8.length!==x.bytes||await ccsSha(u8)!==x.sha256) throw new Error(`The file is damaged (${en.name.split('/').pop()} does not match its check value).`); members[en.name]=u8; }
 return {manifest:man, members}; }
// what an import would do here: {games:[{sha, title, rom, free_slots, files:[{name, kind, slot, slot_name, saved_at, bytes, status, current}], ok:{name:u8}, cur:{name:u8}}], rejected}
async function gxPlan(r){ const core=await fbCore(); const here=await gxHere(); const games=[], rejected=[]; let keys=null; try{ keys=await CCSROMS.keys(true); }catch(e){ keys=null; }
 for(const g of r.manifest.games){ const sha=g.content_sha256, title=String(g.title||'').slice(0,120), ok={};
  for(const x of g.files){ const u8=r.members[`saves/${sha}/${x.name}`]; const [st,why]=fbFileCheck(x.name, u8, sha, core); if(st==='ok') ok[x.name]=u8; else rejected.push({sha, title, name:x.name, why, status:st}); }
  const curF=here[sha]||{}, cur={}; Object.entries(curF).forEach(([k,v])=>{ cur[k]=v.u8; }); const inn=gxNames(ok['slot-names.json']), cn=gxNames(cur['slot-names.json']); const files=[];
  for(const n of ['cart.sav',...GX_SLOTS]){ if(!ok[n]) continue; const it=gxDescribe(n, ok[n], sha, inn, core), c=cur[n];
   if(n==='cart.sav'&&c&&c.length!==ok[n].length){ rejected.push({sha, title, name:n, why:'this game\'s cartridge save here has a different size, so the imported one belongs to a different cartridge layout', status:'incompatible'}); continue; }
   it.status=!c?'add':gxEq(c, ok[n])?'same':'conflict'; if(it.status==='conflict') it.current=gxDescribe(n, c, sha, cn, core); files.push(it); }
  if(!files.length) continue;
  const t=ok['game.json']?gxJson(ok['game.json']):null; const rom=keys?(keys.has(sha)?'here':'missing'):'unknown';
  games.push({sha, title:title||String((t&&t.last_title)||'').slice(0,120)||gxTitleFor(sha), rom, files, free_slots:[1,2,3,4].filter(k=>!cur['slot'+k+'.ccsstate']), ok, cur}); }
 return {ok:true, games, rejected}; }
// a game the file names by its content only: its title from the Included Games, the Game Catalog or a collection here
function gxTitleFor(sha){ const k=String(sha).slice(0,16); const ib=typeof incBuild==='function'?incBuild(sha):null; if(ib) return ib.title;
 const g=typeof byId!=='undefined'?(byId[k]||byId['p_'+k]):null; if(g&&g.display_title) return g.display_title;
 for(const c of (typeof csList==='function'?csList():[])){ const m=c.coll&&c.coll.missing&&c.coll.missing['p_'+k]; if(m&&m.title) return String(m.title).slice(0,120); }
 return ''; }
// the files one game's import writes from the person's choices (saves_transfer.py _decide, the same rules); throws when a choice is no longer possible
function gxDecide(sha, ok, cur, choice){ const out=[]; const taken=new Set(); const inn=gxNames(ok['slot-names.json']), cn=gxNames(cur['slot-names.json']); const names=Object.assign({}, cn), moved={};
 if(ok['cart.sav']){ const c=cur['cart.sav'], ch=choice['cart.sav']||(c?'keep':'add');
  if(!c&&(ch==='add'||ch==='replace')) out.push(['cart.sav', ok['cart.sav']]);
  else if(c&&!gxEq(c, ok['cart.sav'])&&ch==='replace'){ if(c.length!==ok['cart.sav'].length) throw new Error('The cartridge save here has a different size, so it was not replaced.'); out.push(['cart.sav', ok['cart.sav']]); out.push([`cart-set-aside-${Date.now()}.sav`, c]); } }   // the cartridge save it replaces is set aside, never lost
 for(const n of GX_SLOTS){ if(!ok[n]) continue; const k=n[4], u8=ok[n], c=cur[n], ch=choice[n]||(c?'keep':'slot'+k);
  if(ch==='keep'||(c&&gxEq(c,u8)&&(ch==='slot'+k||ch==='replace'))) continue; let tgt;
  if(ch==='replace') tgt=n; else if(/^slot[1-4]$/.test(ch)){ tgt=ch+'.ccsstate'; if(cur[tgt]) throw new Error(`Slot ${ch[4]} is not empty any more, so nothing was imported. Look at the choices again.`); }
  else throw new Error('A choice for the import was not understood, so nothing was imported.');
  if(taken.has(tgt)) throw new Error(`Two saves were set to go into Slot ${tgt[4]}, so nothing was imported.`); taken.add(tgt); out.push([tgt, u8]); moved[k]=tgt[4];
  if(inn[k]) names[tgt[4]]=inn[k]; else delete names[tgt[4]]; }
 if(Object.keys(moved).length&&JSON.stringify(names)!==JSON.stringify(cn)){ const o={}; Object.keys(names).sort().forEach(k=>{ o[k]=names[k]; }); out.push(['slot-names.json', new TextEncoder().encode(JSON.stringify({schema:'ccs-slot-names/1', rom_content_sha256:sha, names:o})+'\n')]); }
 const lu=ok['last-used.json']; if(lu&&!cur['last-used.json']){ const j=gxJson(lu); const k=j&&String(j.slot); if(moved[k]===k) out.push(['last-used.json', lu]); }
 if(ok['game.json']&&!cur['game.json']&&out.length) out.push(['game.json', ok['game.json']]);
 return out; }
async function gxImport(){ if(CS_STATE!=='ok'){ rdyStoreProblem(); return; }
 if(!gxApp()&&(typeof fbWebPlay!=='function'||!fbWebPlay())){ cvSaid('Import Game Saves', '<div class="msgbox bad">This browser cannot keep games or saved games here. Use a current Edge, Chrome, Firefox or Safari, or Classic Cart Studio for Windows.</div>'); return; }
 const files=await cvPick('.zip,application/zip', false); const f=files&&files[0]; if(!f) return;
 openColl('Import Game Saves', f.name, '<div class="msgbox" role="status">Checking the file…</div>');
 if(gxApp()){ procSet('collm','running');
  const up=await fetch(CO.base+'/api/studio/saves-stage', lnaInit({method:'POST', headers:hdrs({'Content-Type':'application/octet-stream'}), body:f, cache:'no-store'})).then(r=>r.json()).catch(e=>({ok:false, error:String(e)})); procSet('collm',null);
  if(!up.ok){ cvSaid('Import Game Saves', `<div class="msgbox bad">${esc(up.error||'The file could not be checked.')} Nothing was changed.</div>`); return; }
  GX.app=up; GX.plan=up; GX.file=null; return gxChoose(f.name); }
 try{ GX.file=await gxRead(f); GX.plan=await gxPlan(GX.file); GX.app=null; }catch(e){ cvSaid('Import Game Saves', `<div class="msgbox bad">${esc(e.message)} Nothing was changed.</div>`); return; }
 gxChoose(f.name); }
// the choices: a save that is new here goes to its own slot; one that differs from the one here is kept as it is unless the person picks an
// empty slot for it or replaces the one here (said plainly); the cartridge save likewise (the one here is set aside when replaced)
function gxChoose(fname){ const p=GX.plan, s=n=>n===1?'':'s'; const games=p.games||[], rej=p.rejected||[];
 if(!games.length){ cvSaid('Import Game Saves', `<div class="msgbox ${rej.length?'warn':''}">${rej.length?'None of the saves in this file can be used here.':'Every save in this file is already here. Nothing was changed.'}</div>${gxRejHtml(rej)}`); return; }
 const label=f=>f.kind==='cart'?'Cartridge save (battery)':`Slot ${f.slot}${f.slot_name?` "${esc(f.slot_name)}"`:''}`;
 const when=f=>f.saved_at?`saved ${esc(gxWhen(f.saved_at))}`:f.kind==='cart'?`${(f.bytes/1000).toLocaleString('en-US',{maximumFractionDigits:1})} KB`:'';
 const row=(g,f)=>{ const id=`gx_${g.sha.slice(0,12)}_${f.name.replace(/\W/g,'')}`; const free=g.free_slots.filter(k=>k!==f.slot);
  let opts;
  if(f.kind==='cart') opts=f.status==='add'?`<option value="add">Add it</option><option value="keep">Don't import it</option>`:f.status==='same'?`<option value="keep">Already here</option>`:`<option value="keep" selected>Keep the one here</option><option value="replace">Replace the one here (it is set aside, not deleted)</option>`;
  else opts=f.status==='add'?`<option value="slot${f.slot}">Put it in Slot ${f.slot}</option>${free.map(k=>`<option value="slot${k}">Put it in Slot ${k}</option>`).join('')}<option value="keep">Don't import it</option>`
   :f.status==='same'?`<option value="keep">Already here</option>`
   :`<option value="keep" selected>Keep the save here, don't import this one</option>${free.map(k=>`<option value="slot${k}">Put it in empty Slot ${k}</option>`).join('')}<option value="replace">Replace the save in Slot ${f.slot}${f.current&&f.current.slot_name?` "${esc(f.current.slot_name)}"`:''} (it is kept, not deleted)</option>`;
  return `<div class="gxrow"><div><b>${label(f)}</b> <span class="small">${when(f)}</span>${f.status==='conflict'?`<div class="small">Here now: ${f.kind==='cart'?'a different cartridge save':`a different save${f.current&&f.current.saved_at?`, saved ${esc(gxWhen(f.current.saved_at))}`:''}`}. The newer date does not always mean more progress.</div>`:''}</div>
   <select id="${id}" data-gx-sha="${g.sha}" data-gx-file="${f.name}" aria-label="${label(f).replace(/"/g,'')}"${f.status==='same'?' disabled':''}>${opts}</select></div>`; };
 const romLine=g=>{ const ib=typeof incBuild==='function'?incBuild(g.sha):null;
  return g.rom==='here'?'':g.rom==='unknown'?'<div class="small">Classic Cart Studio could not check whether this game is here.</div>'
   :`<div class="small">The game file for this game is not ${gxApp()?'on this computer':'in this browser'}. Its saves are kept anyway and become playable when ${ib&&ib.hosted?`you get <b>${esc(ib.title)}</b> from the Included Games`:'you add that exact game file'}. It is not added to any collection.</div>`; };
 openColl('Import Game Saves', fname||'', `<p class="small">Choose what happens with each save. Nothing here is replaced unless you choose it. No game files are in this file.</p>
  ${games.map(g=>`<div class="gxgame"><h3>${esc(g.title||gxTitleFor(g.sha)||'Game '+g.sha.slice(0,12))}</h3>${romLine(g)}${g.files.map(f=>row(g,f)).join('')}</div>`).join('')}
  ${gxRejHtml(rej)}<div class="acts"><button class="btn" id="gxNo">Cancel</button><span class="sp"></span><button class="btn hk" id="gxGo">Import</button></div>`);
 $('gxNo').onclick=closeColl; $('gxGo').onclick=()=>gxApply(fname); }
function gxRejHtml(rej){ return rej&&rej.length?`<div class="msgbox warn"><b>Not usable (${rej.length}):</b>${rej.slice(0,12).map(x=>`<div>${esc(x.title||x.sha.slice(0,12))} › ${esc(x.name)}: ${esc(x.why)}</div>`).join('')}</div>`:''; }
function gxChoices(){ const c={}; document.querySelectorAll('#collBody select[data-gx-sha]').forEach(s=>{ if(s.disabled) return; (c[s.dataset.gxSha]=c[s.dataset.gxSha]||{})[s.dataset.gxFile]=s.value; }); return c; }
async function gxApply(fname){ const choices=gxChoices(); const b=$('gxGo'); if(b) b.disabled=true;
 if(GX.app){ procSet('collm','running'); const r=await hkPost('/api/studio/saves-import', {id:GX.app.id, token:GX.app.token, choices}); procSet('collm',null);
  if(!r||!r.ok){ cvSaid('Saves Not Imported', `<div class="msgbox bad">${esc((r&&r.error)||'The import did not finish.')}</div>`); return; }
  return gxDone((r.written||[]).length, r.replaced_folder); }
 const lk=await fbWebLock(); if(!lk.ok){ cvSaid('Saves Not Imported', `<div class="msgbox bad">${esc(lk.why)}. Nothing was changed.</div>`); return; }
 let n=0, kept=0; const tx=gxTxId();
 try{ const now=await gxPlan(GX.file); const todo=[];   // read again under the lock: the choices apply to what is here now
  for(const g of now.games){ const out=gxDecide(g.sha, g.ok, g.cur, choices[g.sha]||{}), names=new Set(out.map(x=>x[0]));
   for(const [name,u8] of out) todo.push({path:`/data/saves/${g.sha}/${name}`, u8, cur:g.cur[name]||null, keep:g.cur[name]?gxKeepPath(g.sha, tx, name):null});
   // a replaced save's slot names as they were (its name comes back with it), even when the import does not change them
   if(g.cur['slot-names.json']&&!names.has('slot-names.json')&&out.some(([name])=>GX_SLOTS.includes(name)&&g.cur[name])) todo.push({path:`/data/saves/${g.sha}/slot-names.json`, u8:null, cur:g.cur['slot-names.json'], keep:gxKeepPath(g.sha, tx, 'slot-names.json')}); }
  if(todo.length) await gxWrite(todo);
  n=todo.filter(x=>x.u8).length; kept=todo.filter(x=>x.keep&&x.u8&&x.path.split('/').pop()!=='slot-names.json').length; }
 catch(e){ cvSaid('Saves Not Imported', `<div class="msgbox bad">${esc(e&&e.message||String(e))} Nothing was changed.</div>`); return; }
 finally{ if(lk.release) lk.release(); }
 gxDone(n, null, kept?tx:null, kept); }
// ---------------- the saves an import replaced in this browser (Codex v1p4 F1) ----------------
// Before an import replaces a save here, the save it replaces is kept in the SAME storage transaction, exactly as it was (its bytes and its
// record's own time), at /data/saves/<game>/replaced-<id>/<file>, with the game's slot names as they were (slot-names.json): its slot, its
// name and the time it was saved stay with it. <id> is the import's own (date, time and a random part); each copy is added, never written
// over, so a clash or any failure stops the whole import with nothing changed, and repeated imports keep every original apart. The game
// player reads only its own files there. Replaced Game Saves… (Studio menu) lists them after any reload and exports one import's originals
// as an ordinary Game Saves file; Import Game Saves… puts a save back (into an empty slot, or replacing, which keeps that save the same way).
// Like the Windows app's replaced-<tx> folders they are not part of a backup; they stay until this site's data is cleared in the browser.
const GX_KEPT=/^\/data\/saves\/([0-9a-f]{64})\/replaced-([0-9]{8}-[0-9]{6}-[0-9a-f]{8})\/(cart\.sav|slot[1-4]\.ccsstate|slot-names\.json)$/;
function gxTxId(){ const d=new Date(), p=v=>String(v).padStart(2,'0'), r=new Uint8Array(4); crypto.getRandomValues(r);
 return `${d.getFullYear()}${p(d.getMonth()+1)}${p(d.getDate())}-${p(d.getHours())}${p(d.getMinutes())}${p(d.getSeconds())}-${[...r].map(b=>b.toString(16).padStart(2,'0')).join('')}`; }
function gxKeepPath(sha, tx, name){ return `/data/saves/${sha}/replaced-${tx}/${name}`; }
function gxTxWhen(tx){ const m=/^(\d{4})(\d\d)(\d\d)-(\d\d)(\d\d)(\d\d)/.exec(tx||''); return m?new Date(+m[1], +m[2]-1, +m[3], +m[4], +m[5], +m[6]):null; }
// one transaction: each file being replaced is read again (it must still be the one the person saw), kept first, then replaced; a new file
// is added only where there is none; any failure aborts all of it
function gxWrite(todo){ let why=''; const at=new Date().toISOString();
 return cpTx(['files'],'readwrite',t=>{ const st=t.objectStore('files'); const stop=w=>{ if(!why) why=w; try{ t.abort(); }catch(e){} };
  const add=(rec, w)=>{ const q=st.add(rec); q.onerror=ev=>{ ev.preventDefault(); ev.stopPropagation(); if(!(q.error&&q.error.name==='AbortError')) stop(w); }; };   // an abort's own cause is reported by the transaction
  for(const x of todo){ const file=x.path.split('/').pop();
   if(!x.cur){ add({path:x.path, bytes:x.u8.slice().buffer, at}, `${file} appeared while the import was being prepared`); continue; }
   const g=st.get(x.path); g.onsuccess=()=>{ if(why) return; const r=g.result, b=r&&r.bytes?new Uint8Array(r.bytes):null;
    if(!b||!gxEq(b, x.cur)){ stop(`${file} changed while the import was being prepared`); return; }
    add({path:x.keep, bytes:r.bytes, at:r.at||'', replaced_at:at}, 'a copy of the save it replaces could not be kept');
    if(x.u8) st.put({path:x.path, bytes:x.u8.slice().buffer, at}); }; } })
  .catch(e=>{ throw why?new Error(`Nothing was imported: ${why}. Look at the choices again.`):new Error(cpWhyStore(e)); }); }
// {tx, games:{sha:{file:{u8, at}}}} newest first; a read that fails stops (never taken for "none")
async function gxKept(){ let all; try{ all=await cpTx(['files'],'readonly',t=>cpReq(t.objectStore('files').getAll(IDBKeyRange.bound('/data/saves/','/data/saves/\uffff')))); }catch(e){ throw cpReadError(e, 'the saves an import replaced'); }
 const by={}; for(const f of all||[]){ const m=GX_KEPT.exec(f.path); if(!m) continue; const x=by[m[2]]=by[m[2]]||{tx:m[2], games:{}}; (x.games[m[1]]=x.games[m[1]]||{})[m[3]]={u8:new Uint8Array(f.bytes), at:f.at||''}; }
 return Object.values(by).filter(x=>Object.values(x.games).some(fs=>fs['cart.sav']||GX_SLOTS.some(n=>fs[n]))).sort((a,b)=>a.tx<b.tx?1:-1); }
function gxKeptLabel(name, d){ return name==='cart.sav'?'Cartridge save (battery)':`Slot ${d.slot}${d.slot_name?` "${esc(d.slot_name)}"`:''}`; }
async function gxKeptOpen(){
 if(gxApp()){ cvSaid('Replaced Game Saves', '<div class="msgbox">In Classic Cart Studio for Windows, each save that an import or a restore replaced is kept in a folder named "replaced-…" beside it, among the app\'s saved games under %LOCALAPPDATA%\\ClassicCartStudio.</div>'); return; }
 if(typeof fbWebPlay!=='function'||!fbWebPlay()){ cvSaid('Replaced Game Saves', '<div class="msgbox bad">This browser cannot keep games or saved games here, so there are none.</div>'); return; }
 openColl('Replaced Game Saves', '', '<div class="msgbox" role="status">Reading the saves an import replaced…</div>');
 let list, core; try{ list=await gxKept(); core=await fbCore(); }catch(e){ cvSaid('Replaced Game Saves', `<div class="msgbox bad">${esc(e.message)}</div><div class="acts"><span class="sp"></span><button class="btn hk" data-gx-retry="kept">Try Again</button></div>`); return; }
 if(!list.length){ cvSaid('Replaced Game Saves', '<div class="msgbox">No save has been replaced by Import Game Saves in this browser.</div>'); return; }
 const group=x=>{ const w=gxTxWhen(x.tx);
  return `<div class="gxgame"><h3>Replaced ${esc(w?gxWhen(w.toISOString()):x.tx)}</h3>${Object.entries(x.games).map(([sha, fs])=>{ const nm=gxNames(fs['slot-names.json']&&fs['slot-names.json'].u8);
   const items=['cart.sav',...GX_SLOTS].filter(n=>fs[n]).map(n=>{ const d=gxDescribe(n, fs[n].u8, sha, nm, core); return `<div class="gxrow"><div>${gxKeptLabel(n, d)} <span class="small">${d.saved_at?'saved '+esc(gxWhen(d.saved_at)):''}</span></div></div>`; }).join('');
   return items?`<div class="small"><b>${esc(gxTitleFor(sha)||'Game '+sha.slice(0,12))}</b></div>${items}`:''; }).join('')}
   <div class="acts"><span class="sp"></span><button class="btn hk" data-gx-kept="${esc(x.tx)}">Export These Saves…</button></div></div>`; };
 openColl('Replaced Game Saves', '', `<p class="small">Saves that Import Game Saves replaced in this browser, kept exactly as they were. To put one back, export it here, then choose <b>Import Game Saves…</b> with that file and put it in an empty slot (or replace, which keeps that save here the same way). They are not part of a backup: export them to keep them somewhere else.</p>
  ${list.map(group).join('')}<div class="acts"><span class="sp"></span><button class="btn" id="gxKeptClose">Close</button></div>`);
 $('gxKeptClose').onclick=closeColl; }
// one import's originals as an ordinary Game Saves file: each save under its own slot, with its slot names and its record's time
async function gxKeptExport(tx){ let list, core; try{ list=await gxKept(); core=await fbCore(); }catch(e){ cvSaid('Replaced Game Saves', `<div class="msgbox bad">${esc(e.message)}</div><div class="acts"><span class="sp"></span><button class="btn hk" data-gx-kept="${esc(tx)}">Try Again</button></div>`); return; }
 const x=list.find(k=>k.tx===tx); if(!x){ cvSaid('Replaced Game Saves', '<div class="msgbox bad">These saves are no longer in this browser.</div>'); return; }
 const games=[], entries=[], w=gxTxWhen(tx);
 for(const sha of Object.keys(x.games).sort()){ const fs=x.games[sha]; if(!fs['cart.sav']&&!GX_SLOTS.some(n=>fs[n])) continue; const nm=gxNames(fs['slot-names.json']&&fs['slot-names.json'].u8);
  const g={content_sha256:sha, title:String(gxTitleFor(sha)||'').slice(0,120), files:[]};
  for(const n of ['cart.sav',...GX_SLOTS,'slot-names.json']){ if(!fs[n]) continue; const u8=fs[n].u8; const d=gxDescribe(n, u8, sha, nm, core, fs[n].at); d.sha256=await ccsSha(u8); g.files.push(d);
   entries.push({name:`saves/${sha}/${n}`, method:0, crc:ccsCrc32(u8), usize:u8.length, csize:u8.length, body:u8}); }
  games.push(g); }
 const man={schema:GX_SCHEMA, app:'Classic Cart Studio', app_version:String((typeof CCSM!=='undefined'&&CCSM.version)||''), made:new Date().toISOString(), platform:'web-wasm32', core:core?`${core.id} ${core.ver}`:'',
  note:`Saves that Import Game Saves replaced in this browser${w?' on '+gxWhen(w.toISOString()):''}, kept as they were. `+GX_NOTE, games};
 const mj=new TextEncoder().encode(JSON.stringify(man, null, 1)+'\n'); entries.unshift({name:GX_MANIFEST, method:0, crc:ccsCrc32(mj), usize:mj.length, csize:mj.length, body:mj});
 const d=w||new Date(), p=v=>String(v).padStart(2,'0'); const stamp=`${d.getFullYear()}-${p(d.getMonth()+1)}-${p(d.getDate())} ${p(d.getHours())}${p(d.getMinutes())}`;
 const res=await ccsSaveFile(`Classic Cart Studio Replaced Saves ${stamp}.zip`, new Blob([ccsZipBlob(entries)], {type:'application/zip'}), 'Game saves'); if(!res.ok) return;
 const k=entries.length-1, s=k===1?'':'s';
 cvSaid('Replaced Saves Exported', `<div class="msgbox ok">${res.download?'Your browser is saving':'Saved'} ${k} file${s} ${res.download?'to its downloads as':'to'} ${esc(res.name)}.</div>
  <p class="small">To put a save back, choose <b>Import Game Saves…</b>, pick this file and put the save in an empty slot (or replace the one there, which keeps it the same way). The copies stay in this browser too.</p>`); }
function gxDone(n, folder, keptTx, kept){ const p=GX.plan; GX.file=null; GX.plan=null; GX.app=null; const s=n===1?'':'s', ks=kept===1;
 // the Included Games among them whose build is not here yet: offered, the exact approved build of those saves (never another version)
 const get=((p&&p.games)||[]).filter(g=>g.rom==='missing').map(g=>typeof incBuild==='function'?incBuild(g.sha):null).filter(b=>b&&b.hosted);
 cvSaid('Game Saves Imported', `<div class="msgbox ok">${n?`${n} file${s} imported.`:'Nothing needed to change: the saves you kept are as they were.'}</div>${folder?`<p class="small">Each save that was replaced is kept in a folder named "${esc(folder)}" beside it.</p>`:''}${keptTx?`<p class="small">The ${ks?'save':`${kept} saves`} this import replaced ${ks?'is':'are'} kept in this browser, exactly as ${ks?'it was':'they were'} (with ${ks?'its':'their'} slot, name and save time). To get ${ks?'it':'them'} back, export ${ks?'it':'them'}, then import that file. <b>Replaced Game Saves…</b> in the Studio menu keeps this offer.</p><div class="acts"><button class="btn" data-gx-kept="${esc(keptTx)}">Export Replaced Saves…</button></div>`:''}<p class="small">Open a game and choose <b>Load a Save Slot…</b> to continue.</p>`
  +(get.length?`<p class="small">${get.length===1?'This game is':'These games are'} one of the Included Games, not here yet. Classic Cart Studio can get ${get.length===1?'it':'them'} for you (nothing is added to a collection):</p><div class="acts">${get.map(b=>`<button class="btn hk" data-gx-get="${esc(b.key)}">Get ${esc(b.title)}</button>`).join('')}</div>`:'')); }
document.addEventListener('click', async e=>{ const t=e.target.closest&&e.target.closest('[data-gx-get]'); if(!t) return; e.preventDefault(); const g=typeof incByKey==='function'?incByKey(t.dataset.gxGet):null; if(!g) return;
 t.disabled=true; try{ await incAcquire(g); t.textContent=`${g.title} is ready`; toast(`${g.title} is ready to play with its saved games`); }catch(x){ t.disabled=false; cvSaid(`${g.title} Is Not Ready`, `<div class="msgbox bad">${esc(String(x&&x.message||x))}.</div><p class="small">Its saved games are kept. Try again when the connection is back.</p>`); } });
document.addEventListener('click', e=>{ const t=e.target.closest&&e.target.closest('#mSavesOut,#mSavesIn,#mSavesKept,[data-gx-retry],[data-gx-kept]'); if(!t) return; e.preventDefault();
 if(t.dataset.gxKept) gxKeptExport(t.dataset.gxKept); else if(t.id==='mSavesKept'||t.dataset.gxRetry==='kept') gxKeptOpen(); else if(t.id==='mSavesOut'||t.dataset.gxRetry==='out') gxExport(); else gxImport(); });
