// ---- Back Up Everything: a Full Studio Backup (.ccs, kind "studio"; tools/companion/CCS-FORMAT.md) and its restore ----
// A Collection Backup holds one collection; a Full Studio Backup holds every collection, every game any of them lists with its exact ROM
// file (the same completeness rule: it is not made while a listed game's ROM is missing), the details and artwork of every listed personal
// game, favorites, view and Game Settings, and in the Windows app also the saved games of every game (including games in no collection),
// the Play on PC Settings, controller setups and the custom artwork and text. Nothing is uploaded. Restoring shows what it would add, skip,
// find different or not restore (and why) before anything changes; current data is kept where it differs unless the person chooses to
// replace it (the Windows app first keeps the current files). The restore is one checked operation: the backup is checked first, then
// the ROM files are added (they only add), then this Studio's own data is written and read back, then (Windows app) the saved games,
// settings and the details and artwork of this computer's own games; if a later part fails, the earlier ones are put back and the result
// says exactly what is as it was.
const FB={ids:[], titles:{}, ready:[], missing:[], mismatched:[], bytes:0, contents:null};
// ---- the website (W1): the games this browser keeps for playing, and its game player's saved games (saves/<content sha>/<file>, the
//      Windows app's layout and files). A backup includes both; a restore keeps the games for playing here and adds saved games that pass
//      the player's own checks, all in one write; a saved game already here that differs is never replaced (it is kept and said). What is
//      here is read before anything is decided: a read that fails stops the backup or the restore and says so (never "nothing here yet"),
//      and a restore holds the game player's lock for its whole run, so no game is open in any tab or window of this browser meanwhile ----
const FB_PROG=/^(cart\.sav|slot[1-4]\.ccsstate|last-used\.json|continue\.ccsstate|game\.json|slot-names\.json|cart-set-aside-[0-9]{1,20}(-[0-9]{1,3})?\.sav)$/;
function fbWebPlay(){ return !fbApp()&&typeof CCSROMS!=='undefined'&&typeof CCSPLAY!=='undefined'&&CCSPLAY.supported(); }
// a game kept for playing here goes into the backup from its exact kept bytes when its file was not chosen during this visit
async function fbKeptSources(){ if(!fbWebPlay()) return 0; let n=0; CCSROMS._have=null;   /* read now: a game kept from another tab counts too */
 for(const sha of await CCSROMS.keys(true)){ if(ROMSRC.has(sha.slice(0,16))) continue; const r=await CCSROMS.get(sha); if(!r) continue; const u8=new Uint8Array(r.bytes); const id=await ccsIdentifyBytes(u8, r.name||sha.slice(0,16)+'.nes');
  if(id.content_sha256!==sha) continue; ccsSrcAdd(Object.assign(id, {file:new Blob([u8])})); n++; }
 return n; }
async function fbWebSaves(){ if(!fbWebPlay()) return []; let all; try{ all=await cpTx(['files'],'readonly',t=>cpReq(t.objectStore('files').getAll(IDBKeyRange.bound('/data/saves/','/data/saves/\uffff')))); }catch(e){ throw cpReadError(e, 'its saved games'); } const by={};
 for(const f of all||[]){ const m=/^\/data\/saves\/([0-9a-f]{64})\/([^/]+)$/.exec(f.path); if(!m||!FB_PROG.test(m[2])) continue; (by[m[1]]=by[m[1]]||{sha:m[1], title:'', files:[]}).files.push({name:m[2], u8:new Uint8Array(f.bytes)}); }
 for(const g of Object.values(by)){ const gj=g.files.find(x=>x.name==='game.json'); if(gj){ try{ g.title=String(JSON.parse(new TextDecoder().decode(gj.u8)).last_title||'').slice(0,200); }catch(e){} } }
 return Object.values(by).sort((a,b)=>a.sha<b.sha?-1:1); }
// the player's own checks (gp_save.c; studio_backup.py file_check for the Windows app): ['ok'|'invalid'|'incompatible', why, header/note]
async function fbCore(){ if(FB.core) return FB.core; try{ const j=await (await fetch('play/engine.json',{cache:'no-cache'})).json(); const [id,ver]=String(j.core||'').split(' '); FB.core={id, ver}; }catch(e){ FB.core=null; } return FB.core; }
function fbFileCheck(name, u8, sha, core){ const dec=b=>new TextDecoder('utf-8',{fatal:true}).decode(b);
 if(/\.ccsstate$/.test(name)){ if(u8.length<12||dec(u8.subarray(0,8))!=='CCSSTATE') return ['invalid','it is not a Classic Cart Studio save',{}];
  const hl=new DataView(u8.buffer,u8.byteOffset+8,4).getUint32(0,true); if(!hl||hl>65536||12+hl>u8.length||u8.length>(8<<20)+65536) return ['invalid','it is not a Classic Cart Studio save',{}];
  let h; try{ h=JSON.parse(dec(u8.subarray(12,12+hl))); }catch(e){ return ['invalid','the save\'s description is damaged',{}]; }
  if(!h||typeof h!=='object'||h.schema!=='ccs-save-state/1') return ['invalid','the save\'s description is damaged',{}];
  if(h.rom_content_sha256!==sha) return ['invalid','it belongs to a different game file',h];
  if(!core||h.core!==core.id||h.core_version!==core.ver||!['windows-x86_64','web-wasm32'].includes(h.platform)) return ['incompatible',`it was made by another game player version or platform (${String(h.platform||'unknown').slice(0,40)}, ${String(h.core_version||'unknown').slice(0,20)})`,h];
  const pay=u8.subarray(12+hl); if(h.size!==pay.length||h.crc32!==ccsCrc32(pay).toString(16).padStart(8,'0')) return ['invalid','the save is damaged (its check does not match)',h];
  return ['ok','',h]; }
 if(/\.sav$/.test(name)) return !u8.length?['invalid','the cartridge save is empty',{}]:u8.length>(1<<20)?['invalid','the file is larger than any cartridge save',{}]:['ok','',{}];
 if(u8.length>65536) return ['invalid','the file is too large',{}]; let j; try{ j=JSON.parse(dec(u8)); }catch(e){ return ['invalid','the file is damaged',{}]; } if(!j||typeof j!=='object'||Array.isArray(j)) return ['invalid','the file is damaged',{}];
 if(name==='game.json') return j.rom_content_sha256!==undefined&&j.rom_content_sha256!==sha?['invalid','it belongs to a different game file',j]:['ok','',j];
 if(name==='slot-names.json'){ const nm=j.names; return j.schema!=='ccs-slot-names/1'||j.rom_content_sha256!==sha||!nm||typeof nm!=='object'||Object.entries(nm).some(([k,v])=>!['1','2','3','4'].includes(k)||typeof v!=='string'||new TextEncoder().encode(v).length>96)?['invalid','the slot names are damaged or belong to another game file',j]:['ok','',j]; }
 const sl=j.slot; return j.schema!=='ccs-last-used/1'||j.rom_content_sha256!==sha||!Number.isInteger(sl)||sl<1||sl>4?['invalid','the note of the last used save is damaged or belongs to another game file',j]:['ok','',j]; }
// what a restore would do with the backup's saved games here (nothing is written): {todo:[{path,u8}], counts, rejected}
async function fbWebSavesPlan(info){ const core=await fbCore(), d=info.data||{}, titles={}; [...((d.app&&d.app.progress)||[]),...((d.web&&d.web.progress)||[])].forEach(x=>{ if(x&&typeof x.sha==='string') titles[x.sha]=String(x.title||'').slice(0,120); });
 const here={}; for(const g of await fbWebSaves()) for(const f of g.files) here[g.sha+'/'+f.name]=f.u8;
 const eq=(a,b)=>a&&b&&a.length===b.length&&a.every((v,i)=>v===b[i]); const todo=[], rejected=[], c={added:0,same:0,kept:0,replaced:0}, notes={}, will={};
 for(const p of info.progress||[]){ const [,sha,f]=p.split('/'); let u8; try{ const x=info.zip.entries?info.zip.entries.find(e=>e.name===p):null; u8=x?await info.zip.read(x,(8<<20)+65536):null; }catch(e){ u8=null; }
  if(!u8){ rejected.push({sha, title:titles[sha]||'', file:f, why:'it could not be read from the backup'}); continue; }
  const mf=((info.manifest&&info.manifest.files)||[]).find(x=>x.path===p); if(!mf||await ccsSha(u8)!==mf.sha256){ rejected.push({sha, title:titles[sha]||'', file:f, why:'the file is damaged (its checksum does not match the list in the backup)'}); continue; }
  if(f==='last-used.json'){ notes[sha]=u8; continue; }
  const [st,why]=fbFileCheck(f, u8, sha, core); if(st!=='ok'){ rejected.push({sha, title:titles[sha]||'', file:f, why}); continue; }
  const cur=here[sha+'/'+f]; if(!cur){ todo.push({path:`/data/saves/${sha}/${f}`, u8}); c.added++; will[sha+'/'+f]=u8; } else if(eq(cur,u8)){ c.same++; will[sha+'/'+f]=cur; } else { c.kept++; will[sha+'/'+f]=cur; } }
 for(const [sha,u8] of Object.entries(notes)){ const [st,why,j]=fbFileCheck('last-used.json', u8, sha, core); if(st!=='ok'){ rejected.push({sha, title:titles[sha]||'', file:'last-used.json', why}); continue; }
  const s=will[sha+'/slot'+j.slot+'.ccsstate']||here[sha+'/slot'+j.slot+'.ccsstate']; const [s2,,h]=s?fbFileCheck('slot'+j.slot+'.ccsstate', s, sha, core):['absent','',{}];
  if(!(s2==='ok'&&h.crc32===j.slot_crc32&&h.created===j.slot_created)){ rejected.push({sha, title:titles[sha]||'', file:'last-used.json', why:`it names Slot ${j.slot}, but ${s?'that slot holds a different save here (kept)':'this backup does not hold a usable save there'}`}); continue; }
  const cur=here[sha+'/last-used.json']; if(!cur){ todo.push({path:`/data/saves/${sha}/last-used.json`, u8}); c.added++; } else if(eq(cur,u8)) c.same++; else c.kept++; }
 return {todo, counts:c, rejected, games:new Set((info.progress||[]).map(p=>p.split('/')[1])).size}; }
function fbApp(){ return ccsAppPackages()&&((COMPANION.info&&COMPANION.info.capabilities)||COMPANION.capabilities||[]).includes('studio-backup'); }
function fbGames(){ csSaveActiveNow(); const ids=[], titles={}, seen=new Set();
 const add=(id,t)=>{ if(!id||seen.has(id)) return; seen.add(id); ids.push(id); titles[id]=t||((typeof byId!=='undefined'&&byId[id])?byId[id].display_title:'')||id; };
 csList().forEach(r=>{ const o=csRecordOut(r); const kt={}; Object.values((o.coll&&o.coll.kept)||{}).forEach(k=>{ if(k&&k.id) kt[k.id]=k.title; }); (o.sel||[]).forEach(id=>add(id, kt[id])); (o.loadouts||[]).forEach(l=>(l.sel||[]).forEach(id=>add(id, kt[id]))); });
 return {ids, titles}; }
// The Windows app: the ROM step only adds ROM files (nothing already on this computer changes); the backup's details and artwork for
// this computer's own games are written inside the restore's own transaction with the saved games and settings (studio_backup.py).
// the website's record of every listed personal game: what this page knows of it now (a game it shows from its own list) with the details
// any collection keeps for it filling what that leaves empty, cleaned as a collection file's are (csCleanPersonal: portable fields only,
// header numbers as numbers, never a path); kept artwork (a package's JPEG/PNG thumbnail) goes into the backup as art/<id>.<ext>.
// The Windows app writes these from this computer's own games instead (studio_backup.personal_parts).
// A field the person chose (user.edited, a blank one a deliberate clear: D-80) counts as filled: no other collection's details fill it,
// and the mark travels with the value; the artwork mark only with artwork the backup holds.
function fbPersonal(ids){ const out={}, art={}, artEd=new Set(); const recs=csList().map(r=>csRecordOut(csRecordNow(r)));
 const fill=(p, q)=>{ ['title','version_label','content_sha256','file_sha256','format','file_name','note','content_crc32'].forEach(k=>{ if(!p[k]&&q[k]) p[k]=q[k]; }); ['content_size','file_size'].forEach(k=>{ if(!p[k]&&q[k]) p[k]=q[k]; });
  p.user=Object.assign({}, p.user||{}); const pe=new Set(p.user.edited||[]), qe=new Set((q.user&&q.user.edited)||[]);
  Object.entries(q.user||{}).forEach(([k,v])=>{ if(k==='edited'||pe.has(k)||csMeaningful(p.user[k], p.id)) return; if(qe.has(k)){ p.user[k]=v; pe.add(k); } else if(csMeaningful(v, p.id)) p.user[k]=v; });
  pe.delete('art'); if(pe.size) p.user.edited=[...pe].sort(); else delete p.user.edited; if(!p.detected&&q.detected) p.detected=q.detected; return p; };
 ids.forEach(id=>{ if(!/^p_[0-9a-f]{16}$/.test(id)) return; let p=(typeof PERSONAL!=='undefined'&&PERSONAL[id])?csCleanPersonal(PERSONAL[id], id):null; if(p&&(p.user.edited||[]).includes('art')) artEd.add(id);
  recs.forEach(r=>{ const m=r.coll&&r.coll.missing&&r.coll.missing[id]; if(!m||typeof m!=='object') return; const k=csKept(m, id); const q=Object.assign({id}, k.personal||{}); q.user=Object.assign({}, q.user||{}); const qe=new Set(q.user.edited||[]);
   [['title','title'],['version_label','version'],['description','description'],['year','year'],['publisher','publisher']].forEach(([f,t])=>{ if(!qe.has(f)&&!csMeaningful(q.user[f], id)&&csMeaningful(k[t], id)) q.user[f]=k[t]; });
   ['content_sha256','file_sha256'].forEach(h=>{ if(!q[h]&&k[h]) q[h]=k[h]; }); if(!q.content_size&&k.size) q.content_size=k.size; if(qe.has('art')) artEd.add(id);
   p=p?fill(p, q):csCleanPersonal(q, id);
   if(!art[id]&&/^data:image\/(jpeg|png);base64,/.test(k.thumb_url||'')&&csThumbOk(k.thumb_url)) art[id]=k.thumb_url; });
  if(p){ const ed=(p.user.edited||[]).filter(f=>f!=='art'); if(artEd.has(id)&&art[id]) ed.push('art'); if(ed.length) p.user.edited=ed.sort(); else delete p.user.edited; out[id]=p; } });
 return {personal:out, art}; }
function fbArtBytes(art){ const o={}; Object.entries(art||{}).forEach(([id,u])=>{ const m=/^data:image\/(jpeg|png);base64,([A-Za-z0-9+/=]+)$/.exec(u||''); if(!m) return; try{ const b=atob(m[2]); const a=new Uint8Array(b.length); for(let i=0;i<b.length;i++) a[i]=b.charCodeAt(i); o[id+(m[1]==='png'?'.png':'.jpg')]=a; }catch(e){} }); return o; }
async function fbOpen(){ document.querySelectorAll('.menu').forEach(x=>x.hidden=true); const g=fbGames(); FB.ids=g.ids; FB.titles=g.titles; const app=fbApp();
 openColl('Back Up Everything', `${csList().length} collection${csList().length===1?'':'s'} · ${g.ids.length} game${g.ids.length===1?'':'s'}`, '<div class="msgbox">Checking ROM files and saved games…</div>');
 if(app){ const chk=await hkPost('/api/package/rom-check', {games:g.ids.map(id=>({version_id:id, title:g.titles[id]||id}))});
  if(!chk.ok){ $('collBody').innerHTML=`<div class="msgbox bad">${esc(chk.error||'The ROM files could not be checked. Try again.')}</div>`; return; }
  FB.ready=(chk.available||[]).map(x=>x.version_id); FB.bytes=(chk.available||[]).reduce((a,x)=>a+(x.size||0),0); FB.missing=(chk.missing||[]).map(x=>x.version_id); FB.mismatched=(chk.mismatched||[]).map(x=>x.version_id);
  const c=await hkPost('/api/studio/contents', {ids:g.ids}); FB.contents=c&&c.ok?c:null; }
 else { try{ await fbKeptSources(); FB.saves=await fbWebSaves(); FB.gs=typeof gsCurrent==='function'?await gsCurrent():null; }   /* the Game Settings as the browser player's files hold them now */
  catch(e){ $('collBody').innerHTML=`<div class="msgbox bad"><b>The backup cannot be made right now.</b> ${esc(e.message)}</div><div class="acts"><span class="sp"></span><button class="btn" id="fbNo">Cancel</button><button class="btn hk" id="fbAgain">Try Again</button></div>`; $('fbNo').onclick=closeColl; $('fbAgain').onclick=()=>fbOpen(); return; }
  FB.ready=g.ids.filter(id=>ROMSRC.has(id)); FB.missing=g.ids.filter(id=>!ROMSRC.has(id)); FB.mismatched=[]; FB.bytes=FB.ready.reduce((a,id)=>a+ROMSRC.get(id).size,0); FB.contents=null; }
 fbRender(); }
function fbRender(){ const app=fbApp(), n=FB.ids.length, need=FB.missing.length+FB.mismatched.length, ok=!need; const t=id=>esc(FB.titles[id]||id); const c=FB.contents&&FB.contents.summary;
 const gsl=(!app&&FB.gs)||{games:{}}; const nGs=app?(c?c.game_settings:0):Object.keys(gsl.games||{}).length; const np=FB.ids.filter(id=>/^p_/.test(id)).length;
 const list=arr=>`<ul class="small ccsmiss">${arr.slice(0,12).map(id=>`<li>${t(id)}</li>`).join('')}${arr.length>12?`<li>and ${arr.length-12} more</li>`:''}</ul>`;
 const items=[`<b>${csList().length} collection${csList().length===1?'':'s'}</b> with their loadouts and HOME settings`, `<b>${n} game${n===1?'':'s'}</b>${ok&&n?` with their ROM files (${fmt(FB.bytes)})`:''}`];
 if(np) items.push(`The details and artwork of ${np} personal game${np===1?'':'s'}`);
 items.push('Favorites and the library view', `Game Settings: the Gameplay Defaults and ${nGs} game${nGs===1?'':'s'} with their own choices`);
 if(app&&c){ items.push(`Saved games of <b>${c.progress_games} game${c.progress_games===1?'':'s'}</b>${c.orphaned_progress?` (${c.orphaned_progress} not in any collection: their saves are included, their game files are not)`:''}${FB.contents.progress_bytes?` · ${fmt(FB.contents.progress_bytes)}`:''}`);
  if(c.legacy_saves) items.push(`${c.legacy_saves} earlier automatic save${c.legacy_saves===1?'':'s'} (kept for recovery only)`);
  if(c.player_settings) items.push('Play on PC Settings (keys and picture)'); if(c.controller_setups) items.push('Controller setups (they work only with the same controllers)'); if(c.custom_games) items.push(`Custom artwork and text of ${c.custom_games} game${c.custom_games===1?'':'s'}`); }
 else if(!app){ const sv=FB.saves||[]; items.push(sv.length?`Saved games of <b>${fbN(sv.length,'game')}</b> played in this browser (save slots, cartridge saves and slot names)`:'Saved games: none in this browser yet'); }
 $('collBody').innerHTML=`<p class="small">A Full Studio Backup holds everything needed to rebuild this Studio. A Collection Backup (Back Up Collection) holds one collection.</p><ul class="fbitems">${items.map(x=>`<li>${x}</li>`).join('')}</ul>`
  +(ok?(n?`<div class="msgbox ok"><b>Every ROM file is ready.</b> The backup is made on this ${app?'computer':'device'}; nothing is uploaded.</div>`:'')
     :`<div class="msgbox warn"><b>${need} of ${n} game${n===1?'':'s'} ${need===1?'needs its':'need their'} ROM file.</b> A backup always includes every listed game's ROM file.${FB.missing.length?list(FB.missing):''}${FB.mismatched.length?`<div class="small">A different version is on this computer for:</div>${list(FB.mismatched)}`:''}</div>
      <div class="ccsfix">${app?'<button class="btn" id="fbAdd">Add ROM Files…</button>':'<button class="btn" id="fbPick">Choose ROM Files…</button>'}</div>${app?'':'<p class="small">Choose the ROM files, a ZIP of them, or a .ccs file that has them. They are read on this device only.</p>'}`)
  +`<div class="acts"><span class="sp"></span><button class="btn" id="fbNo">Cancel</button><button class="btn hk" id="fbGo" ${ok?'':'disabled'}>Create Backup…</button></div>`;
 $('fbNo').onclick=closeColl; $('fbGo').onclick=()=>fbMake();
 if($('fbPick')) $('fbPick').onclick=async()=>{ const fs=await ccsPickRoms(); if(!fs.length) return; $('fbPick').disabled=true; $('fbPick').textContent='Reading files…'; const res=await ccsAddRomSources(fs); await fbOpen(); if(!res.found) toast('No ROM files were found in those files'); };
 if($('fbAdd')) $('fbAdd').onclick=()=>{ closeColl(); if(typeof importGames==='function') importGames('files'); }; }
const FB_ACK=Object.assign({}, CCS_EXPORT_ACK, {title:'Back Up Everything', body:['This backup will contain the ROM files, artwork, collections and settings of this Studio'+'.', 'Classic Cart Studio creates and saves the backup on your device. No ROM files are uploaded to Classic Cart Studio.', CCS_EXPORT_ACK.body[2]]});
function fbName(){ return csSafeName(`Classic Cart Studio Full Backup ${csStamp()}`, '.ccs'); }
async function fbMake(){ const a=await ccsAck(FB_ACK); if(a!=='export') return; const name=fbName(), app=fbApp();
 if(app){ $('collBody').innerHTML=ccsNativeWait(name); procSet('collm','native');
  const st=await hkPost('/api/studio/backup', {studio:csBackupObject(), games:FB.ids.map(id=>({version_id:id, title:FB.titles[id]||id})), name});
  if(st.canceled){ procSet('collm',null); closeColl(); toast('Canceled; nothing was saved'); return; } if(!st.ok){ procSet('collm',null); return ccsJobFail(st.error); }
  const j=await ccsWatch(st.job, 'Creating '+st.name);
  if(j.phase==='canceled'){ closeColl(); toast('Backup canceled; nothing was saved'); return; } if(j.phase!=='done'){ return ccsJobFail(j.error); }
  if(typeof CCSM!=='undefined') CCSM.track('backup_completed'); return ccsSavedView(j, {ids:FB.ids}); }
 const fp=fbPersonal(FB.ids);
 let saves, gs=null; try{ gs=typeof gsCurrent==='function'?await gsCurrent():null; saves=fbWebPlay()?await fbWebSaves():[]; }   /* read again now: a read that fails stops it, no incomplete or older backup is made */
 catch(e){ openColl('Back Up Everything', '', `<div class="msgbox bad"><b>The backup was not created, and no file was saved.</b> ${esc(e.message)}</div><div class="acts"><span class="sp"></span><button class="btn" id="ccsOk">OK</button><button class="btn hk" id="fbAgain">Try Again</button></div>`); $('ccsOk').onclick=closeColl; $('fbAgain').onclick=()=>fbOpen(); return; }
 const extra=[]; saves.forEach(g=>g.files.forEach(f=>extra.push({path:`progress/${g.sha}/${f.name}`, u8:f.u8})));
 const sd={schema:CCS_STUDIO_SCHEMA, games:FB.ids, titles:FB.titles, studio_data:csBackupObject(), personal:fp.personal, web:{game_settings:gs, progress:saves.map(g=>({sha:g.sha, title:g.title, files:g.files.map(f=>f.name)}))}, platform:'web', created:new Date().toISOString().replace(/\.\d+Z$/,'Z')};
 const parts={kind:'studio', file:JSON.stringify(sd), ids:FB.ids, titles:FB.titles, art:fbArtBytes(fp.art), extra, summary:{name:'Full Studio Backup', collections:csList().length, personal_games:Object.keys(fp.personal).length, progress_games:saves.length}};
 CCS.cancel=false; $('collBody').innerHTML=`<div class="msgbox" id="ccsProg">Creating the backup…</div><div class="acts"><span class="sp"></span><button class="btn" id="ccsStop">Cancel</button></div>`; $('ccsStop').onclick=()=>{ CCS.cancel=true; closeColl(); toast('Canceled; nothing was saved'); }; procSet('collm','running');
 let blob; try{ blob=await ccsBuild(parts, (d,t)=>{ const p=$('ccsProg'); if(p) p.textContent=`Creating the backup… ${d.toLocaleString('en-US')} of ${t.toLocaleString('en-US')} ROM files`; }, ()=>CCS.cancel); }
 catch(e){ procSet('collm',null); if(CCS.cancel) return; $('collBody').innerHTML=`<div class="msgbox bad">The backup was not created: ${esc(e.message||String(e))}. Nothing was saved.</div><div class="acts"><span class="sp"></span><button class="btn" id="ccsOk">OK</button></div>`; $('ccsOk').onclick=closeColl; return; }
 if(CCS.cancel) return; procSet('collm',null); closeColl(); const res=await ccsSaveFile(name, blob, 'Classic Cart Studio Full Backup');
 if(!res.ok){ if(!res.canceled&&typeof CCSM!=='undefined') CCSM.fail('backup','write_failed'); toast(res.canceled?'Canceled; nothing was saved':'The backup was not saved. Try again.'); return; }
 if(typeof CCSM!=='undefined') CCSM.track('backup_completed'); toast(res.download?`Your browser is saving "${res.name}" (${FB.ids.length} games, ${fmt(blob.size)}) to its downloads. Where that is depends on your browser's settings.`:`Saved "${res.name}" (${FB.ids.length} games, ${fmt(blob.size)})`); }
document.addEventListener('click',e=>{ const b=e.target.closest&&e.target.closest('#mBackupAll'); if(!b) return; e.preventDefault(); fbOpen(); });
// ---- Game Settings from a backup, on the website (the Windows app restores them as the game player's own files: studio_backup.py) ----
// Both origins are read (made in the Windows app: app.game_settings; on the website: web.game_settings), each record cleaned as the
// player reads it (gsClean: a missing field means Use Default, Off is a choice); the backup's own platform wins where both name a game.
function fbSettingsPlan(d){ const ap=(d.app&&d.app.game_settings)||{}, wb=(d.web&&d.web.game_settings)||{}; const order=d.platform==='web'?[wb,ap]:[ap,wb]; const games={}, bad=[]; let defaults=null;
 order.forEach(src=>{ const gm=src&&src.games&&typeof src.games==='object'&&!Array.isArray(src.games)?src.games:{};
  Object.entries(gm).forEach(([sha,r])=>{ if(games[sha]) return; const what=(r&&typeof r==='object'&&typeof r.title==='string'&&r.title)?r.title.slice(0,120):String(sha).slice(0,64);
   if(!/^[0-9a-f]{64}$/.test(sha)){ bad.push({what, why:'it does not name an exact game'}); return; } if(!r||typeof r!=='object'||Array.isArray(r)){ bad.push({what, why:'the record is damaged'}); return; }
   if(r.rom_content_sha256!=null&&r.rom_content_sha256!==sha){ bad.push({what, why:'the record belongs to a different game file'}); return; }
   const clean=gsClean(r); if(!Object.keys(clean).length){ bad.push({what, why:'it holds no choices of its own (every setting uses the default)'}); return; }
   games[sha]={title:String(r.title||'').slice(0,200), version:String(r.version||'').slice(0,200), clean}; });
  const df=src&&src.defaults; if(defaults===null&&df&&typeof df==='object'&&df.rapid&&typeof df.rapid==='object') defaults=gsCleanDefaults(df); else if(defaults===null&&df!=null&&!(df&&typeof df==='object'&&df.rapid)) bad.push({what:'Gameplay Defaults', why:'they are damaged'}); });
 return {games, defaults, bad}; }
// what the plan does against this browser's Game Settings as they are now (base: gsCurrent, read by the caller; never the page's copy, which may
// be older, A7-F1): counts, the new store, and writes (each choice to add or replace, with what it replaces, for gsWebPush)
function fbSettingsDecide(plan, replace, base){ const o=JSON.parse(JSON.stringify(base||gsBlank())), c={'game settings':{added:0,same:0,kept:0,replaced:0}, 'gameplay defaults':{added:0,same:0,kept:0,replaced:0}}; const k=c['game settings'], kd=c['gameplay defaults']; let changed=false; const writes=[];
 Object.entries(plan.games).forEach(([sha,g])=>{ const cur=o.games[sha];
  if(!cur){ o.games[sha]=Object.assign({title:g.title, version:g.version}, g.clean); k.added++; changed=true; writes.push({sha, title:g.title, version:g.version, base:null, next:g.clean}); }
  else if(JSON.stringify(csCanon(gsClean(cur)))===JSON.stringify(csCanon(g.clean))) k.same++; else if(replace){ o.games[sha]=Object.assign({title:cur.title, version:cur.version}, g.clean); k.replaced++; changed=true; writes.push({sha, title:cur.title, version:cur.version, base:gsClean(cur), next:g.clean}); } else k.kept++; });
 if(plan.defaults){ const cur=o.defaults; if(!cur){ o.defaults=plan.defaults; kd.added++; changed=true; writes.push({sha:null, base:null, next:plan.defaults}); } else if(JSON.stringify(csCanon(gsCleanDefaults(cur)))===JSON.stringify(csCanon(plan.defaults))) kd.same++; else if(replace){ o.defaults=plan.defaults; kd.replaced++; changed=true; writes.push({sha:null, base:gsCleanDefaults(cur), next:plan.defaults}); } else kd.kept++; }
 if(!plan.defaults) delete c['gameplay defaults']; return {counts:c, store:o, changed, writes}; }
// inside the restore's transaction: where this page has no browser player its own store is the Game Settings, written here; with the browser
// player the choices are written to its files afterwards (gsWebPush), and the page's copy follows them
function fbSettingsWrite(plan, replace, tx, base){ const r=fbSettingsDecide(plan, replace, base); if(r.changed&&!gsWebOk()) tx.set(GS.key, JSON.stringify(r.store)); return r; }
// the counts as they turned out once the player's files were written (gp: gsWebPush's answer): a choice found changed meanwhile was kept
function fbSettingsOutcome(r, gp){ const c=JSON.parse(JSON.stringify(r.counts)); if(gp&&gp.ok) (gp.kept||[]).forEach(w=>{ const x=c[w.sha?'game settings':'gameplay defaults']; if(!x) return; if(w.base===null) x.added--; else x.replaced--; x.kept++; }); return c; }
// ---- restoring a Full Studio Backup: what it would do first (nothing is written), then one confirmed restore ----
function fbIds(d){ return new Set((Array.isArray(d.games)?d.games:[]).filter(x=>typeof x==='string')); }
function fbPreviewPlan(info, replace){ const d=info.data||{}, sd=d.studio_data||{}; return csMergePlan(sd, {personal:d.personal, art:info.art||{}, replace}); }
const fbN=(n,w)=>`${n} ${w}${n===1?'':'s'}`;
function fbList(arr, f){ return arr.length?`<ul class="small ccsmiss">${arr.slice(0,8).map(x=>`<li>${f(x)}</li>`).join('')}${arr.length>8?`<li>and ${arr.length-8} more</li>`:''}</ul>`:''; }
function fbRejected(rej){ return fbList(rej||[], x=>`${esc(x.title||String(x.sha||'').slice(0,16)||'A game')} · ${esc(x.file)}: ${esc(x.why)}`); }
async function fbRestorePreview(info, fname){ const d=info.data||{}, sd=d.studio_data||{}, app=fbApp()&&info.source==='staged', web=!app; const cols=sd.collections||[]; const pv=info.restore_preview||{}; const pc=pv.counts||{}; const ap=d.app||{};
 let sp=null;   // the website: this browser's Game Settings and saved games are read before the preview counts them (a failed read is said, with Try Again)
 if(web&&info.zip&&fbWebPlay()){ openColl('Restore Full Studio Backup', fname||'', '<div class="msgbox" role="status">Checking the backup against this browser…</div>');
 }
 let gbase=null; if(web){ try{ gbase=typeof gsCurrent==='function'?await gsCurrent():null; if(info.zip&&fbWebPlay()&&(info.progress||[]).length) sp=await fbWebSavesPlan(info); }   /* the Game Settings as they are now (a page without the browser player: its own store) */
  catch(e){ openColl('Restore Full Studio Backup', fname||'', `<div class="msgbox bad"><b>The backup cannot be restored right now.</b> ${esc(e.message)}</div><div class="acts"><span class="sp"></span><button class="btn" id="fbRNo">Cancel</button><button class="btn hk" id="fbRAgain">Try Again</button></div>`); $('fbRNo').onclick=closeColl; $('fbRAgain').onclick=()=>fbRestorePreview(info, fname); return; } }
 const plan=fbPreviewPlan(info, false); const gsp=web?fbSettingsPlan(d):null; const gsd=gsp?fbSettingsDecide(gsp, false, gbase).counts:null; const np=Object.keys((d.personal&&typeof d.personal==='object')?d.personal:{}).length;
 const row=(k,v)=>`<dt>${k}</dt><dd>${v}</dd>`; const cnt=k=>pc[k]?`${pc[k].new} new · ${pc[k].same} already here · ${pc[k].different} different here`:'';
 const wcnt=k=>gsd&&gsd[k]&&(gsd[k].added+gsd[k].same+gsd[k].kept)?`${gsd[k].added} new · ${gsd[k].same} already here · ${gsd[k].kept} different here`:'';
 const prog=app?(ap.progress||[]):[...new Set((info.progress||[]).map(p=>p.split('/')[1]))].map(sha=>({sha, files:(info.progress||[]).filter(p=>p.split('/')[1]===sha).map(p=>p.split('/')[2])})); const rej=app?(pv.rejected||[]):(sp?sp.rejected:[]); const bad=app?(pv.not_applicable||[]):(gsp?gsp.bad:[]);
 const vw={same:'the same as here', apply:`${plan.view.want==='grid'?'Grid':'List'} (used: this Studio is new)`, keep:`${plan.view.want==='grid'?'Grid':'List'}: this Studio's ${view==='grid'?'Grid':'List'} view is kept unless you choose Replace`}[plan.view.act];
 openColl('Restore Full Studio Backup', fname||'', `<dl class="kv">${row('Backup made', esc(nowShort(d.created||(info.manifest||{}).created)||'unknown'))}${row('Made in', d.platform==='windows'?'Classic Cart Studio for Windows':'the website')}
  ${row('Collections', `${cols.length}: ${plan.add.length} to add as new collections${plan.skip.length?`, ${plan.skip.length} already here (skipped)`:''}. Nothing here is replaced.`)}
  ${row('Games and ROM files', `${(d.games||[]).length} · ${info.roms?info.roms.length:0} verified (${fmt(info.romBytes||info.rom_bytes||0)})`)}${np?row('Personal games', `${fbN(np,'game')} with ${np===1?'its':'their'} details${web?' (kept with the collections)':' (saved with this computer\'s own games)'}${!web&&cnt('personal games')?' · '+cnt('personal games'):''}`):''}
  ${row('Game Settings', web?(wcnt('game settings')||'none'):(cnt('game settings')||'none'))}${(web?(gsd&&gsd['gameplay defaults']):pc['gameplay defaults'])?row('Gameplay Defaults', web?wcnt('gameplay defaults'):cnt('gameplay defaults')):''}
  ${prog.length?row('Saved games', `${fbN(prog.length,'game')}${app&&cnt('progress')?' · files: '+cnt('progress'):''}${sp?` · files: ${sp.counts.added} new · ${sp.counts.same} already here · ${sp.counts.kept} different here`:''}${prog.some(x=>x.orphaned)?` · ${prog.filter(x=>x.orphaned).length} not in any collection`:''}`):''}${prog.some(x=>(x.files||[]).includes('continue.ccsstate'))?row('Earlier automatic saves', 'restored for recovery only (the game window offers Recover an Earlier Automatic Save)'):''}
  ${row('Library view', vw)}${row('Open collection', plan.open?`"${esc(plan.open)}" opens (as in the backup)`:'the collection open now stays open')}
  ${row('Favorites', `${plan.fav.add.length} to add${plan.fav.out?` · ${plan.fav.out} not in this Game Library (not added)`:''}`)}${(sd.snapshots||[]).length?row('Earlier versions', `${plan.snaps.add.length} to add${plan.snaps.out?` · ${plan.snaps.out} not added (this Studio keeps up to 30)`:''}`):''}</dl>
  ${rej.length?`<div class="msgbox warn"><b>${fbN(rej.length,'saved-game file')} will not be restored.</b> They stay in the backup file.${fbRejected(rej)}</div>`:''}
  ${bad.length?`<div class="msgbox warn"><b>${fbN(bad.length,'Game Settings record')} cannot be used.</b>${fbList(bad, x=>`${esc(x.what)}: ${esc(x.why)}`)}</div>`:''}
  ${app&&(pv.personal_not_used||[]).length?`<div class="msgbox warn"><b>${fbN(pv.personal_not_used.length,'personal game')}: part of the backup's record cannot be used.</b>${fbList(pv.personal_not_used, x=>`${esc(x.what)}: ${esc(x.why)}`)}</div>`:''}
  ${app&&(pv.personal_kept||[]).length?`<div class="msgbox"><b>Your own games whose details here differ from the backup</b> (yours are kept unless you choose Replace):${fbList(pv.personal_kept, x=>`${esc(x.title)}: ${esc((x.fields||[]).join(', '))}`)}</div>`:''}
  ${app&&(pv.pending||[]).length?`<div class="msgbox warn"><b>An earlier restore did not finish putting everything back.</b> <button class="btn sm" id="fbRec" data-tx="${esc(pv.pending[0])}">Try Again</button></div>`:''}
  ${web&&prog.length?(fbWebPlay()?'<div class="msgbox">Saved games are added to this browser\'s game storage, each checked as the game player checks it. A saved game already here that differs is kept as it is.</div>':'<div class="msgbox">This browser cannot keep saved games, so they stay in the backup file.</div>'):''}
  ${web?(fbWebPlay()?'<div class="msgbox">The games are kept privately in this browser so they play here. That copy is not a backup: keep this .ccs file.</div>':'<div class="msgbox">This browser cannot keep games for playing here: it checks the ROM files but does not keep them after this visit. Keep this .ccs file.</div>'):''}
  <label class="chk"><input type="checkbox" id="fbReplace"> Replace my current ${app?'settings, library view, saved games and the details of my own games':'Game Settings and library view'} where they differ from the backup${app?' (the current files are kept first in a "replaced" folder beside them)':''}</label>
  ${app&&(ap.controllers&&ap.controllers.pads)?'<label class="chk"><input type="checkbox" id="fbCtl"> Restore controller setups (they work only with the same controllers)</label>':''}
  <p class="small">Nothing is deleted and nothing changes until you choose Restore. Where the backup and this Studio differ, this Studio's data is kept unless you choose to replace it above.</p>
  <div class="acts"><span class="sp"></span><button class="btn" id="fbRNo">Cancel</button><button class="btn hk" id="fbRGo">Restore…</button></div>`);
 $('fbRNo').onclick=closeColl; if($('fbRec')) $('fbRec').onclick=()=>fbRecover($('fbRec').dataset.tx);
 $('fbRGo').onclick=async()=>{ const replace=$('fbReplace').checked, controllers=!!($('fbCtl')&&$('fbCtl').checked); const a=await ccsAck(CCS_IMPORT_ACK); if(a!=='with') return; await fbRestoreRun(info, fname, replace, controllers); }; }
function fbBusy(t){ const b=$('collBody'); if(b) b.innerHTML=`<div class="msgbox" role="status">${esc(t)}</div>`; }
// the website: no game is open in any tab or window of this browser while its game storage is restored, and none opens until the restore has
// finished. The restore takes the game player's lock (ccs-play.js CP_LOCK) at once or is refused, before anything changes, and holds it to the end.
async function fbWebLock(){ if(!fbWebPlay()) return {ok:true, release:null};   /* this browser keeps no games or saved games here: nothing is shared */
 if(typeof CCSPLAY!=='undefined'&&CCSPLAY.running()) return {ok:false, why:'A game is open in Preview on TV. Close it from its own menu first, then restore'};
 if(!(navigator.locks&&navigator.locks.request)) return {ok:false, why:'This browser cannot make sure that no game is open in another of its tabs or windows. Use a current version of Edge, Chrome, Safari or Firefox to restore this backup'};
 const r=await cpLock(CP_LOCK); if(!r) return {ok:false, why:(await cpLockHolder())==='restore'?'Another tab or window of this browser is restoring a backup. Wait until it has finished, then restore':'A game is open in another tab or window of this browser. Close it from its own menu there first, then restore'};
 const r2=await cpLock(CP_RESTORE_LOCK); return {ok:true, release:()=>{ if(r2) r2(); r(); }}; }   /* the second lock only names what holds the first */
async function fbRestoreRun(info, fname, replace, controllers){ const d=info.data||{}, sd=d.studio_data||{}, app=fbApp()&&info.source==='staged'; const R={app, roms:null, native:null, page:null};
 procSet('collm','running'); let held=false, unlock=null; if(app){ CS_HOLD++; held=true; }   /* the backup's details reach this computer's own games only inside the restore's transaction (no reconcile meanwhile) */
 try{
  if(app){ fbBusy('Checking the backup against this Studio…');
   const chk=await hkPost('/api/studio/restore', {id:info.id, replace, controllers, check:true}); if(!chk.ok) return fbResult(R, {stopped:chk.error||'The backup could not be checked.', pending:chk.pending&&chk.txid||null, notStarted:true});
   fbBusy('Adding the ROM files…'); const ir=await hkPost('/api/package/import-roms', {id:info.id}); R.roms=ir;   /* a Full Studio Backup's ROM step only adds: nothing already here changes */
   if(!ir.ok) return fbResult(R, {stopped:'The ROM files could not all be added ('+(ir.error||'unknown error').replace(/\.$/,'')+')'});
   await refreshPersonal(); }
  else { if(info.zip){ const lk=await fbWebLock(); if(!lk.ok) return fbResult(R, {stopped:lk.why, notStarted:true, retry:()=>fbRestorePreview(info, fname)}); unlock=lk.release; }
   try{ if(info.zip&&fbWebPlay()){ fbBusy('Checking the saved games…'); R.saves=await fbWebSavesPlan(info); }   /* what is here, read before anything changes (a failed read stops it) */
    R.gbase=typeof gsCurrent==='function'?await gsCurrent():null; }   /* the Game Settings as they are now: the restore decides with them, never with the page's copy */
   catch(e){ R.saves=null; return fbResult(R, {stopped:e.message, notStarted:true, retry:()=>fbRestorePreview(info, fname)}); }
   if(info.zip&&fbWebPlay()){ fbBusy('Keeping the games for playing in this browser…'); R.kept=await ccsKeepForPlay(info); } }
  if(!app&&info.zip){ for(const x of info.roms) if(x.verified) ccsSrcAdd({name:x.path, ext:x.ext, sha256:x.sha256, content_sha256:x.content_sha256, size:x.size, raw:{blob:await info.zip.raw(x.entry), method:x.entry.method, crc:x.entry.crc, csize:x.entry.csize, usize:x.entry.usize}}); }
  fbBusy('Restoring the collections and settings…');
  const plan=csMergePlan(sd, {personal:d.personal, art:info.art||{}, replace}); const gsp=app?null:fbSettingsPlan(d); const old={CS, fav:new Set(fav), view}; const tx=csTxn(); let res=null, gres=null;
  try{ res=csApplyPlan(plan, tx, gsp?(t=>{ gres=fbSettingsWrite(gsp, replace, t, R.gbase); }):null); }
  catch(e){ const left=tx.rollback(); return fbResult(R, {stopped:`This ${app?'app':'browser'} did not keep the restored data (${String(e&&e.message||e)})`, pageLeft:left}); }
  R.page={plan, res, gcounts:gres&&gres.counts, gsp};
  if(!app&&R.saves&&R.saves.todo.length){ fbBusy('Restoring the saved games in this browser…');
   try{ const at=new Date().toISOString(); await cpTx(['files'],'readwrite',t=>{ const st=t.objectStore('files'); for(const x of R.saves.todo) st.put({path:x.path, bytes:x.u8.slice().buffer, at}); }); }
   catch(e){ const left=tx.rollback(); R.saves.failed=true; return fbResult(R, {stopped:`This browser did not keep the saved games (${typeof cpWhyStore==='function'?cpWhyStore(e).replace(/\.$/,''):String(e&&e.message||e)})`, pageLeft:left}); } }
  if(app){ CS=res.next;   // the page holds the restored collections while the saved games are restored (an autosave meanwhile writes the same)
   fbBusy('Restoring saved games and settings on this computer…'); const nr=await hkPost('/api/studio/restore', {id:info.id, replace, controllers}); R.native=nr;
   if(nr.timeout) return fbResult(R, {unknown:true});
   if(!nr.ok){ const left=tx.rollback(); csUnadopt(old); return fbResult(R, {stopped:nr.reason?`The saved games, settings and the details and artwork of your own games on this computer could not be restored (${nr.reason})`:(nr.error||'The saved games and settings could not be restored.'), pageLeft:left, native:nr, pending:nr.pending&&nr.txid||null}); } }
  R.opened=csAdopt(plan, res);
  if(app){ CS_HOLD--; held=false; try{ await refreshPersonal(); }catch(e){} }   /* the restored details as this computer now has them; kept details still pending reconcile now */
  else { try{ csReconcilePersonal(); }catch(e){}   /* the restored Game Settings reach the browser's game player: only what was added or replaced */
   if(gres&&gres.writes.length&&gsWebOk()){ const gp=await gsWebPush(gres.writes); if(!gp.ok) R.gsPush=gp.error; else R.page.gcounts=fbSettingsOutcome(gres, gp); } }
  return fbResult(R, {ok:true});
 } finally { if(held) CS_HOLD--; procSet('collm',null); if(unlock) unlock(); } }
async function fbRecover(tx){ fbBusy('Putting back what the earlier restore changed…'); const r=await hkPost('/api/studio/restore-recover', {txid:tx});
 openColl('Restore Full Studio Backup', '', `<div class="msgbox ${r.ok?'ok':'bad'}">${esc(r.ok?(r.message||'Everything was put back.'):(r.error||'It could not be finished.'))}</div>${r.changed?fbList(r.changed, x=>`${esc(x.path)}${x.original?` (original: ${esc(x.original)})`:''}`):''}${!r.ok&&r.txid?`<div class="acts"><button class="btn" id="fbRec2">Try Again</button></div>`:''}<div class="acts"><span class="sp"></span><button class="btn hk" id="fbOk">OK</button></div>`);
 $('fbOk').onclick=closeColl; if($('fbRec2')) $('fbRec2').onclick=()=>fbRecover(r.txid); }
// the result, exactly as it happened: what was added, kept, replaced, applied or not restored (and why); after a failure, what is as it was
const FB_LABELS={'personal games':'Details and artwork of your own games', 'game settings':'Game Settings', 'gameplay defaults':'Gameplay Defaults', 'progress':'Saved-game files', 'play on pc settings':'Play on PC Settings', 'controller setups':'Controller setups', 'controller choices':'Controller choices', 'custom games':'Custom artwork and text (games)', 'custom image':'Custom images'};
function fbCountLine(k, v){ return `${esc(FB_LABELS[k]||k)}: ${v.added} added, ${v.same} already here${v.kept?`, ${v.kept} kept as they were`:''}${v.replaced?`, ${v.replaced} replaced`:''}`; }
// the ROM step's result exactly: files added (they stay; a new game among them has only automatic details until a restore finishes) and
// files already here (never changed by a Full Studio Backup's ROM step)
function fbRomLine(R, done){ const ir=R.roms; if(!ir) return ''; const im=ir.imported||[]; const here=im.filter(x=>x.state==='present'||x.state==='different').length, add=im.length-here;
 const fresh=im.filter(x=>x.state==='added'&&/^p_/.test(x.stored_as||'')).length; const out=[];
 if(add) out.push(`${fbN(add,'ROM file')} ${add===1?'was':'were'} added to this computer and ${add===1?'stays':'stay'} (each is a complete, checked copy)${!done&&fresh?`; ${fresh===1?'the new game shows':'the new games show'} only automatic details because the restore did not finish`:''}.`);
 if(here) out.push(`${fbN(here,'ROM file')} ${here===1?'was':'were'} already on this computer and ${here===1?'was':'were'} not changed.`);
 return out.join(' '); }
function fbResult(R, o){ const lines=[]; let head, cls='ok', extra='';
 if(typeof CCSM!=='undefined'&&!o.unknown){ if(o.stopped) CCSM.fail('restore', o.notStarted?'invalid_data':'write_failed'); else CCSM.track('restore_completed'); }   // a restore whose outcome is known (not one the app did not answer in time)
 if(o.unknown){ cls='warn'; head='The app did not answer in time, so whether the saved games and settings were restored is not known.'; extra='<p class="small">Open the backup again and choose Restore: what is already here is skipped, and the preview shows what is still different.</p>'; }
 else if(o.stopped){ cls='bad'; const left=o.pageLeft||[]; const nat=o.native||{};
  head=`The restore did not ${o.notStarted?'start':'finish'}. ${esc(String(o.stopped).replace(/\.$/,''))}.`;   /* refused by the check: nothing started */
  if(nat.partial){ head=`The restore did not finish${nat.reason?` (${esc(nat.reason)})`:''}, and ${(nat.changed||[]).length===1?'1 file':`${(nat.changed||[]).length} files`} on this computer could not be put back. Choose Try Again to finish putting ${(nat.changed||[]).length===1?'it':'them'} back.`; extra+=fbList(nat.changed||[], x=>`${esc(x.path)} (${esc(x.what)})${x.original?`: its original is kept as ${esc(x.original)}`:''}`)+(nat.txid?`<div class="acts"><button class="btn" id="fbRec" data-tx="${esc(nat.txid)}">Try Again</button></div>`:''); }
  else if(nat.rolled_back) lines.push('Saved games, settings and the details and artwork of your own games on this computer: everything the restore changed was put back and checked.');
  else if(R.app) lines.push('Saved games, settings and the details and artwork of your own games on this computer: nothing was changed.');
  if(o.pageLeft) lines.push(left.length?`These could not be put back as they were: ${esc(left.join(', '))}. Nothing of yours was removed (a restore only adds, so what it added may still be there), and a copy of your collections from before the restore is kept in this ${R.app?'app':'browser'}.`:'Collections, favorites and settings here: put back and checked; they are as they were.');   /* the page's own write ran: its rollback says what is so */
  else lines.push('Collections, favorites and settings here: nothing was changed.');
  const rl=fbRomLine(R, false); if(rl) lines.push(rl); else if(R.app) lines.push('No ROM files were added.');
  if(o.pending&&!nat.partial) extra+=`<div class="acts"><button class="btn" id="fbRec" data-tx="${esc(o.pending)}">Try Again</button></div>`; }
 else { const p=R.page.plan, n=R.native||{}; head=`${fbN(p.add.length,'collection')} added${p.skip.length?`, ${p.skip.length} already here`:''}.`;
  if(p.add.length) lines.push('Added as new collections: '+p.add.slice(0,8).map(a=>`"${esc(a.name)}"`).join(', ')+(p.add.length>8?` and ${p.add.length-8} more`:'')+'. Every collection that was here is unchanged.');
  const pids=new Set(); p.add.forEach(a=>a.rec.sel.forEach(id=>{ if(/^p_/.test(id)) pids.add(id); })); const np=pids.size; if(np&&!R.app) lines.push(`Personal games: ${np} with ${np===1?'its':'their'} details (kept with the collections).`);
  const counts=R.app?(n.counts||{}):(R.page.gcounts||{}); Object.entries(counts).forEach(([k,v])=>{ if(!R.app&&R.gsPush&&(k==='game settings'||k==='gameplay defaults')){ lines.push(`${esc(FB_LABELS[k]||k)}: not restored (see below).`); return; } if(v.added||v.same||v.kept||v.replaced) lines.push(fbCountLine(k, v)); });
  const bad=R.app?(n.not_applicable||[]):((R.page.gsp&&R.page.gsp.bad)||[]); if(bad.length) extra+=`<div class="msgbox warn"><b>${fbN(bad.length,'Game Settings record')} could not be used.</b>${fbList(bad, x=>`${esc(x.what)}: ${esc(x.why)}`)}</div>`;
  const pk=R.app?(n.personal_kept||[]):[]; if(pk.length) lines.push(`Kept here where the backup's details differ: ${pk.slice(0,8).map(x=>`${esc(x.title)} (${esc((x.fields||[]).join(', '))})`).join(', ')}${pk.length>8?` and ${pk.length-8} more`:''}.`);
  const pnu=R.app?(n.personal_not_used||[]):[]; if(pnu.length) extra+=`<div class="msgbox warn"><b>${fbN(pnu.length,'personal game')}: part of the backup's record could not be used.</b>${fbList(pnu, x=>`${esc(x.what)}: ${esc(x.why)}`)}</div>`;
  const rej=n.rejected||[]; if(rej.length) extra+=`<div class="msgbox warn"><b>${fbN(rej.length,'saved-game file')} ${rej.length===1?'was':'were'} not restored.</b> They stay in the backup file.${fbRejected(rej)}</div>`;
  lines.push({same:'Library view: the same as the backup.', apply:`Library view: ${p.view.want==='grid'?'Grid':'List'}, as in the backup.`, keep:`Library view: your ${view==='grid'?'Grid':'List'} view was kept (the backup's is ${p.view.want==='grid'?'Grid':'List'}).`}[p.view.act]);
  lines.push(R.opened?`Opened "${esc(R.opened)}", the collection open in the backup.`:'The collection open before the restore is still open.');
  lines.push(`Favorites: ${p.fav.add.length} added${p.fav.out?`, ${p.fav.out} not in this Game Library (not added)`:''}.`); if(p.snaps.add.length||p.snaps.out) lines.push(`Earlier versions: ${p.snaps.add.length} added${p.snaps.out?`, ${p.snaps.out} not added (this Studio keeps up to 30)`:''}.`);
  const rl=fbRomLine(R, true); if(rl) lines.push(rl);
  if(!R.app){ if(R.kept) lines.push(R.kept.text?esc(R.kept.text.trim()):'Games kept for playing in this browser: none.'); else lines.push('ROM files: held for this visit only (this browser cannot keep games for playing here).');
   if(R.saves){ const c=R.saves.counts; if(c.added||c.same||c.kept) lines.push(`Saved-game files in this browser: ${c.added} added, ${c.same} already here${c.kept?`, ${c.kept} kept as they were (different here)`:''}.`);
    if(R.saves.rejected.length) extra+=`<div class="msgbox warn"><b>${fbN(R.saves.rejected.length,'saved-game file')} ${R.saves.rejected.length===1?'was':'were'} not restored.</b> They stay in the backup file.${fbRejected(R.saves.rejected)}</div>`; }
   if(R.gsPush) extra+=`<div class="msgbox warn"><b>The backup's Game Settings were not restored:</b> this browser could not store them. ${esc(R.gsPush)} The Game Settings here are as they were; restore the backup again to add them.</div>`; }
  if(n.replaced_folder) lines.push(`Replaced files were kept first in folders named "${esc(n.replaced_folder)}" beside them.`); }
 openColl(o.ok?'Full Studio Backup Restored':'Restore Full Studio Backup', '', `<div class="msgbox ${cls}">${head}</div>${lines.length?`<ul class="small fbres">${lines.map(x=>`<li>${x}</li>`).join('')}</ul>`:''}${extra}<div class="acts"><span class="sp"></span>${o.retry?'<button class="btn" id="fbRetry">Try Again</button>':''}<button class="btn hk" id="fbOk">${o.ok?'Done':'OK'}</button></div>`);
 $('fbOk').onclick=closeColl; if($('fbRetry')) $('fbRetry').onclick=()=>o.retry();   /* refused before anything changed: the same backup, checked again */ if($('fbRec')) $('fbRec').onclick=()=>fbRecover($('fbRec').dataset.tx); return o; }
