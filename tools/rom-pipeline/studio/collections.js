// ---- My Collections: many named collections, one active. The same code runs on the website and in Classic Cart Studio for Windows.
//      Storage: this browser's (or the app's WebView2) local storage under one key; the app also mirrors it to its data folder through
//      the backend (desktop.js DESKTOP_KEYS), so collections survive a lost browser profile. The active collection is "worked on" in the
//      established single-collection keys (selection, decisions, collection details, HOME settings): every existing feature keeps working
//      unchanged, and the active record is updated from them automatically (Save Collection = automatic). Switching stores the current
//      collection first, then loads the other one: nothing is ever discarded. Library favorites are global; HOME favorites, folders and
//      the HOME theme belong to each collection.
const CSKEY='studio-collections-v2', CS_SCHEMA='ccs-collections-store/1', CREC_SCHEMA='ccs-collection/1', CBUNDLE_SCHEMA='ccs-collections/1', SBACKUP_SCHEMA='ccs-studio-backup/2';
if(typeof DESKTOP_KEYS!=='undefined'&&!DESKTOP_KEYS.includes(CSKEY)) DESKTOP_KEYS.push(CSKEY);   // the app mirrors the collections to its data folder
let CS=null, CS_T=null;
// the NES Classic Loadout open for editing (studio/loadouts.js): while one is open, the working copy holds the loadout and the collection's own
// games, decisions and HOME settings wait in LO; every collection-level action reads them through csCollSel()
let LO=null, CS_COLLVIEW=0;   // CS_COLLVIEW > 0: the working copy is temporarily the collection (a file or handoff is being built)
function csCollSel(){ return LO?LO.collSel:sel; }
function csNow(){ return new Date().toISOString(); }
function csId(){ return 'c'+(typeof uuid==='function'?uuid():String(Math.random()).slice(2)).replace(/-/g,'').slice(0,15); }
function csRead(){ try{ const s=JSON.parse(localStorage.getItem(CSKEY)||'null'); if(s&&s.schema===CS_SCHEMA&&s.items&&typeof s.items==='object') return s; }catch(e){} return null; }
function csWrite(){ try{ localStorage.setItem(CSKEY, JSON.stringify(CS)); }catch(e){ toast('Could not save collections (browser storage may be full). Back Up Studio Data now.'); } try{ if(typeof desktopStateDirty==='function') desktopStateDirty(); }catch(e){} }
function csHomeRead(){ try{ return JSON.parse(localStorage.getItem(HOME_KEY)||'null'); }catch(e){ return null; } }
function csCollFields(c){ const o={}; ['created','modified','savedToFile','prepared','baseline','pref','multi','avail','placeholders','missing'].forEach(k=>{ if(c&&c[k]!==undefined) o[k]=c[k]; }); return JSON.parse(JSON.stringify(o)); }
// the working copy (the established keys) -> a record
function csFromWorking(id, name){ const S=csCollSel(), D=LO?LO.collDecisions:decisions; const coll=csCollFields(COLL); const mi={}; [...S].forEach(x=>{ if(coll.missing&&coll.missing[x]&&(!byId[x]||csPendingPersonal(x, coll.missing[x]))) mi[x]=coll.missing[x]; });   /* a personal game's details stay until the store confirms them (csMergePersonal) */ coll.missing=mi; return {id, name:(name||COLL.name||'My Collection').slice(0,80), created:COLL.created||csNow(), modified:COLL.modified||csNow(), sel:[...S], decisions:JSON.parse(JSON.stringify(D||{})), coll, home:LO?LO.collHome:csHomeRead()}; }
function csActive(){ return CS&&CS.items[CS.active]; }
function csList(){ return CS?Object.values(CS.items).sort((a,b)=>(b.modified||'').localeCompare(a.modified||'')):[]; }
// ---- migration (idempotent): the first start with this version turns the existing single collection into the first named one ----
function csInit(){ CS=csRead(); if(CS&&CS.items[CS.active]) return;
 if(CS&&Object.keys(CS.items).length){ CS.active=csList()[0].id; csWrite(); return; }
 const id=csId(); const legacy={}; [KEY,DKEY,CKEY,HOME_KEY].forEach(k=>{ const v=localStorage.getItem(k); if(v!=null) legacy[k]=v; });
 CS={schema:CS_SCHEMA, active:id, items:{}, migrated:{at:csNow(), from:Object.keys(legacy).length?'single collection (Classic Cart Studio 0.6.1 and earlier)':'new'}};
 CS.items[id]=csFromWorking(id); csWrite();
 if(Object.keys(legacy).length){ try{ localStorage.setItem(CSKEY+'.before-migration', JSON.stringify({at:csNow(), keys:legacy})); }catch(e){} } }   // the original keys also stay where they were: nothing is removed
// ---- automatic save: the active record follows the working copy ----
function csSaveActiveNow(){ if(!CS) return; clearTimeout(CS_T); CS_T=null; const r=csActive(); if(!r) return; const n=csFromWorking(r.id, COLL.name); n.shares=r.shares||[]; n.created=r.created||n.created; n.loadouts=csLoadoutsNow(r); n.openLoadout=LO&&n.loadouts.some(l=>l.id===LO.lid)?LO.lid:null; CS.items[r.id]=n; csWrite(); csMenuRefresh(); }
// the record's loadouts, the open one taken from the working copy
function csLoadoutsNow(r){ const list=JSON.parse(JSON.stringify(r.loadouts||[])); if(LO&&LO.cid===r.id){ const l=list.find(x=>x.id===LO.lid); if(l){ const ns=[...sel], nd=JSON.parse(JSON.stringify(decisions||{})), nh=csHomeRead(); if(JSON.stringify([l.sel,l.decisions||{},l.home||null])!==JSON.stringify([ns,nd,nh])){ l.sel=ns; l.decisions=nd; l.home=nh; l.modified=csNow(); } } } return list; }
function csDirty(){ if(!CS) return; clearTimeout(CS_T); CS_T=setTimeout(csSaveActiveNow,400); }
(function(){ ['persist','saveColl'].forEach(n=>{ const f=window[n]; if(typeof f==='function') window[n]=function(){ const r=f.apply(this,arguments); try{ csDirty(); }catch(e){} return r; }; });
 if(typeof homeExtrasSave==='function'){ const h=homeExtrasSave; homeExtrasSave=function(){ const r=h.apply(this,arguments); try{ csDirty(); }catch(e){} return r; }; }
 addEventListener('beforeunload',()=>{ try{ if(CS_T) csSaveActiveNow(); }catch(e){} }); document.addEventListener('visibilitychange',()=>{ if(document.hidden&&CS_T) csSaveActiveNow(); }); })();
// ---- switching: store the current collection, then load the other one into the working copy ----
function csLoadWorking(r){ LO=null; window.LO_PREV={cid:r.id, sel:new Set(r.sel||[])}; sel=new Set(r.sel||[]); decisions=JSON.parse(JSON.stringify(r.decisions||{}));
 COLL=Object.assign({name:r.name, created:r.created, modified:r.modified, savedToFile:null, prepared:null, baseline:null, pref:{}, multi:{}, avail:[], placeholders:[], missing:{}, personalCache:(COLL&&COLL.personalCache)||{}}, JSON.parse(JSON.stringify(r.coll||{})), {name:r.name});
 try{ if(r.home) localStorage.setItem(HOME_KEY, JSON.stringify(r.home)); else localStorage.removeItem(HOME_KEY); }catch(e){}
 localStorage.setItem(KEY, JSON.stringify([...sel])); localStorage.setItem(DKEY, JSON.stringify(decisions)); localStorage.setItem(CKEY, JSON.stringify(COLL));
 if((r.sel||[]).some(id=>byId[id]&&byId[id].ext)&&typeof setExt==='function') setExt(true);
 try{ if(typeof TV!=='undefined'){ if(TV.open&&typeof closeTv==='function') closeTv(); TV.model=null; TV.proj=null; } }catch(e){}
 try{ if(typeof desktopStateDirty==='function') desktopStateDirty(); }catch(e){}
 render(); updateStatusLine(); try{ hkLabelUpdate(); }catch(e){} csMenuRefresh(); try{ if(typeof loUi==='function') loUi(); }catch(e){} }
function csSwitch(id, quiet){ if(!CS||!CS.items[id]) return; if(id===CS.active){ if(!quiet) toast(`"${CS.items[id].name}" is already open`); return; }
 csSaveActiveNow(); const was=CS.items[CS.active]; if(was) was.openLoadout=null; CS.active=id; CS.items[id].openLoadout=null; csWrite(); csLoadWorking(CS.items[id]); if(!quiet) toast(`Opened "${CS.items[id].name}" · ${sel.size} game${sel.size===1?'':'s'}`); }
function csUniqueName(name){ const base=(name||'My Collection').trim().slice(0,72)||'My Collection'; const have=new Set(csList().map(r=>r.name.toLowerCase())); if(!have.has(base.toLowerCase())) return base; for(let i=2;i<999;i++){ const n=`${base} (${i})`; if(!have.has(n.toLowerCase())) return n; } return base+' '+Date.now(); }
function csCreate(name, rec){ csSaveActiveNow(); const id=csId(); const now=csNow(); const r=Object.assign({sel:[],decisions:{},coll:{},home:null,loadouts:[]}, rec||{}, {id, name:csUniqueName(name), created:(rec&&rec.created)||now, modified:now, shares:[], openLoadout:null}); CS.items[id]=r; csWrite(); return r; }
function csNew(name){ const r=csCreate(name||'New Collection'); csSwitch(r.id, true); toast(`Created "${r.name}"`); return r; }
function csRename(id, name){ const r=CS.items[id]; if(!r) return; name=(name||'').trim().slice(0,80); if(!name) return; if(id===CS.active){ COLL.name=name; saveColl(); updateStatusLine(); csSaveActiveNow(); } else { r.name=name; r.modified=csNow(); csWrite(); } csMenuRefresh(); }
function csDuplicate(id){ if(id===CS.active) csSaveActiveNow(); const src=CS.items[id]; if(!src) return null; const copy=JSON.parse(JSON.stringify(src)); delete copy.shares; const r=csCreate(src.name+' copy', copy); toast(`Duplicated as "${r.name}"`); return r; }
function csDelete(id){ const r=CS.items[id]; if(!r) return; if(id===CS.active&&LO&&typeof loClose==='function') loClose(true); if(typeof snapshotNow==='function'&&id===CS.active) snapshotNow('before deleting "'+r.name+'"');
 else { try{ const snaps=JSON.parse(localStorage.getItem(SKEY)||'[]'); if((r.sel||[]).length) snaps.unshift({at:csNow(),reason:'before deleting "'+r.name+'"',name:r.name,sel:r.sel,decisions:r.decisions||{},pref:(r.coll||{}).pref||{},multi:(r.coll||{}).multi||{},avail:(r.coll||{}).avail||[],fav:[...fav],baseline:(r.coll||{}).baseline||null,fp:null}); localStorage.setItem(SKEY, JSON.stringify(snaps.slice(0,12))); }catch(e){} }
 delete CS.items[id];
 if(!Object.keys(CS.items).length){ const n=csCreate('My Collection'); CS.active=n.id; csWrite(); csLoadWorking(n); }
 else if(id===CS.active){ CS.active=csList()[0].id; csWrite(); csLoadWorking(CS.items[CS.active]); }
 else csWrite();
 toast(`Deleted "${r.name}"${(r.sel||[]).length?'. Restore Previous Version can bring it back for a while':''}`); }
// a collection's portable file: the schema-4 collection (readable by every earlier version) plus the exact Studio record (HOME folders,
// theme and HOME favorites travel with it). Built for any collection, not only the open one.
function csWithRecord(r, fn){ if(r.id===CS.active){ csSaveActiveNow(); if(!LO) return fn(); const s0=sel, d0=decisions; CS_COLLVIEW++; try{ sel=new Set(LO.collSel); decisions=LO.collDecisions; return fn(); } finally { sel=s0; decisions=d0; CS_COLLVIEW--; } }
 const s0=sel, d0=decisions, c0=COLL, l0=LO; try{ LO=null; sel=new Set(r.sel||[]); decisions=r.decisions||{}; COLL=Object.assign({}, c0, {savedToFile:null,prepared:null,baseline:null,pref:{},multi:{},avail:[],placeholders:[],missing:{}}, r.coll||{}, {name:r.name}); return fn(); } finally { sel=s0; decisions=d0; COLL=c0; LO=l0; } }
function csRecordOut(r){ return {schema:CREC_SCHEMA, id:r.id, name:r.name, created:r.created, modified:r.modified, sel:r.sel||[], decisions:r.decisions||{}, coll:r.coll||{}, home:r.home||null, loadouts:(r.loadouts||[]).map(csLoadoutOut)}; }
// an NES Classic Loadout in a file: its name, its games (exact version ids, every one of them in the collection), decisions and HOME settings
function csLoadoutOut(l){ return {id:l.id, target:'nes-classic', name:l.name, created:l.created, modified:l.modified, sel:(l.sel||[]).slice(), decisions:l.decisions||{}, home:l.home||null}; }
function csLoadoutsIn(list, ids){ const set=new Set(ids); const seen=new Set(); return (Array.isArray(list)?list:[]).slice(0,200).filter(l=>l&&typeof l==='object'&&(!l.target||l.target==='nes-classic')).map(l=>{ const id=typeof l.id==='string'&&/^[A-Za-z0-9_-]{1,40}$/.test(l.id)&&!seen.has(l.id)?l.id:'l'+csId().slice(1); seen.add(id);
  const s=[...new Set((Array.isArray(l.sel)?l.sel:[]).filter(x=>typeof x==='string'&&set.has(x)))]; const dec={}; Object.entries(l.decisions||{}).forEach(([k,v])=>{ if(s.includes(k)&&(v==='factory'||v==='alternate')) dec[k]=v; });
  return {id, target:'nes-classic', name:String(l.name||'NES Classic Loadout').trim().slice(0,60)||'NES Classic Loadout', created:l.created||csNow(), modified:l.modified||csNow(), sel:s, decisions:dec, home:(l.home&&typeof l.home==='object')?l.home:null}; }); }
function csFileFor(r){ return csWithRecord(r, ()=>{ const m=buildManifest(); m.ccs_collection=csRecordOut(CS.items[r.id]||r); return m; }); }
// ---- file names (always editable) ----
function csStamp(){ const d=new Date().toISOString(); return d.slice(0,10)+' '+d.slice(11,19).replace(/:/g,'-')+' UTC'; }
function csSafeName(n, ext){ n=String(n||'').replace(/[\\/:*?"<>|\u0000-\u001f]+/g,' ').replace(/\s+/g,' ').trim().slice(0,150); if(!n) n='Classic Cart Studio'; if(ext&&!n.toLowerCase().endsWith(ext)) n+=ext; return n; }
// Save As: the system's own dialog when the browser or the app supports it (name and folder chosen there); otherwise an editable name
// first, then the browser's download (its own settings decide the folder)
async function ccsSaveFile(defName, blob, kind){ const ext=(defName.match(/\.[a-z0-9]+$/i)||[''])[0].toLowerCase(); const mime=blob.type||'application/octet-stream';
 if(window.showSaveFilePicker){ try{ const h=await window.showSaveFilePicker({suggestedName:defName, types:[{description:kind||'File', accept:{[mime]:[ext]}}]}); const w=await h.createWritable(); await w.write(blob); await w.close(); return {ok:true, name:h.name}; }
  catch(e){ if(e&&e.name==='AbortError') return {ok:false, canceled:true}; } }
 return new Promise(res=>{ let m=document.getElementById('savem'); if(!m){ m=document.createElement('div'); m.id='savem'; m.className='modal'; m.setAttribute('role','dialog'); m.setAttribute('aria-modal','true'); m.setAttribute('aria-labelledby','savemT'); document.body.appendChild(m); }
  m.innerHTML=`<div class="mbox" style="max-width:560px"><div class="mhd"><h2 id="savemT">Save ${esc(kind||'File')}</h2></div><div class="fld"><label for="savemN">File name</label><input type="text" id="savemN" value="${esc(defName)}" spellcheck="false"></div><p class="small">Your browser saves it to its usual download location.</p><div class="acts"><span class="sp"></span><button class="btn" id="savemC">Cancel</button><button class="btn hk" id="savemS">Save</button></div></div>`;
  m.classList.add('open'); const inp=$('savemN'); inp.focus(); inp.setSelectionRange(0, Math.max(0, inp.value.length-ext.length));
  const done=r=>{ m.classList.remove('open'); res(r); };
  $('savemC').onclick=()=>done({ok:false,canceled:true}); m.onkeydown=e=>{ if(e.key==='Escape') done({ok:false,canceled:true}); if(e.key==='Enter'&&e.target===inp) $('savemS').click(); };
  $('savemS').onclick=()=>{ const name=csSafeName(inp.value, ext); const a=document.createElement('a'); a.href=URL.createObjectURL(blob); a.download=name; document.body.appendChild(a); a.click(); a.remove(); setTimeout(()=>URL.revokeObjectURL(a.href),4000); done({ok:true,name}); }; }); }
window.ccsSaveFile=ccsSaveFile;
// ---- Export Collection ----
async function csExport(id){ id=id||CS.active; const r=CS.items[id]; if(!r) return; if(id===CS.active&&typeof confirmExportWarnings==='function'&&!confirmExportWarnings()) return;
 const m=csFileFor(r); const res=await ccsSaveFile(`Classic Cart Studio Collection Backup ${csStamp()}.json`, new Blob([JSON.stringify(m,null,1)],{type:'application/json'}), 'Collection File');
 if(!res.ok) return; if(id===CS.active){ COLL.savedToFile={at:csNow(),fp:collFingerprint()}; saveColl(); updateStatusLine(); }
 toast(`Exported "${r.name}" (${m.count} game${m.count===1?'':'s'}) to ${res.name}`); }
// ---- Import Collection: always a new collection (never overwrites); duplicates get a distinct name ----
function csRecordFromFile(rec, fallbackName){ const ids=[...new Set((rec.sel||[]).filter(id=>typeof id==='string'&&id&&id.length<=128))]; const missing=ids.filter(id=>!byId[id]); const dec={}; Object.entries(rec.decisions||{}).forEach(([k,v])=>{ if(ids.includes(k)&&(v==='factory'||v==='alternate')) dec[k]=v; });
 const coll=(rec.coll&&typeof rec.coll==='object')?JSON.parse(JSON.stringify(rec.coll)):{}; coll.missing=Object.assign({}, coll.missing||{}); missing.forEach(id=>{ if(!coll.missing[id]) coll.missing[id]={title:id, source:'unknown'}; });
 return {rec:{name:String(rec.name||fallbackName||'Imported collection').slice(0,80), created:rec.created||csNow(), sel:ids, decisions:dec, coll, home:(rec.home&&typeof rec.home==='object')?rec.home:null, loadouts:csLoadoutsIn(rec.loadouts, ids)}, missing}; }
function csNotCollection(fileName){ if(typeof ccsFail==='function') ccsFail(fileName||'', 'It is not a Classic Cart Studio package (.ccs) or collection file.', 'This file cannot be opened.'); else toast('That file is not a collection file'); }
async function csImportObject(obj, fileName){ if(!obj||typeof obj!=='object'){ csNotCollection(fileName); return 0; }
 if(obj.schema===SBACKUP_SCHEMA||obj.schema==='nes-classic-studio-data/1'){ csRestoreDialog(obj, fileName); return 0; }
 const recs=obj.schema===CBUNDLE_SCHEMA&&Array.isArray(obj.collections)?obj.collections:(obj.ccs_collection&&obj.ccs_collection.schema===CREC_SCHEMA?[obj.ccs_collection]:null);
 if(recs){ let n=0, miss=0, last=null; recs.slice(0,200).forEach(x=>{ if(!x||!Array.isArray(x.sel)) return; const {rec,missing}=csRecordFromFile(x, fileName); last=csCreate(rec.name, rec); miss+=missing.length; n++; });
  if(!last){ csNotCollection(fileName); return 0; }
  csSwitch(last.id, true); toast(`Imported ${n} collection${n===1?'':'s'}${miss?` · ${miss} game${miss===1?'':'s'} ${csAwayWord()}`:''}`); csMenuRefresh(); const t=$('tray'); if(t) t.classList.add('open'); return n; }
 if(typeof KNOWN_SCHEMAS!=='undefined'&&KNOWN_SCHEMAS.includes(obj.schema)){ const r=csCreate(obj.name||(fileName||'Imported collection').replace(/\.json$/i,'')); csSwitch(r.id, true); const res=await applyManifest(obj, fileName, 'before importing a collection file'); if(res){ COLL.name=r.name; saveColl(); updateStatusLine(); csSaveActiveNow(); } return res?1:0; }
 csNotCollection(fileName); return 0; }
function csImportPick(){ const inp=document.createElement('input'); inp.type='file'; inp.accept='application/json,.json'; inp.onchange=()=>{ const f=inp.files[0]; if(!f) return; f.text().then(t=>{ let o; try{ o=JSON.parse(t); }catch(e){ csNotCollection(f.name); return; } csImportObject(o, f.name); }); }; inp.click(); }
// ---- Back Up Studio Data / Restore Studio Data: every collection, library favorites, settings and recovery snapshots ----
function csBackupObject(){ csSaveActiveNow(); return {schema:SBACKUP_SCHEMA, exported:csNow(), app:'Classic Cart Studio', origin:location.origin, active:CS.active, collections:csList().map(csRecordOut), favorites:[...fav], view:(typeof view==='string'?view:'list'), extended:localStorage.getItem(EKEY)||'', welcomed:!!localStorage.getItem(WKEY), snapshots:JSON.parse(localStorage.getItem(SKEY)||'[]')}; }
async function csBackup(){ const d=csBackupObject(); const res=await ccsSaveFile(`Classic Cart Studio Data Backup ${csStamp()}.json`, new Blob([JSON.stringify(d,null,1)],{type:'application/json'}), 'Studio Data Backup'); if(res.ok) toast(`Backed up ${d.collections.length} collection${d.collections.length===1?'':'s'}, favorites and snapshots to ${res.name}`); }
function csRestorePick(){ const inp=document.createElement('input'); inp.type='file'; inp.accept='application/json,.json'; inp.onchange=()=>{ const f=inp.files[0]; if(!f) return; f.text().then(t=>{ let o; try{ o=JSON.parse(t); }catch(e){ toast('That file is not a Studio data backup'); return; } csRestoreDialog(o, f.name); }); }; inp.click(); }
function csRestoreDialog(d, fileName){ const v1=d&&d.schema==='nes-classic-studio-data/1', v2=d&&d.schema===SBACKUP_SCHEMA; if(!v1&&!v2){ if(d&&(d.ccs_collection||(typeof KNOWN_SCHEMAS!=='undefined'&&KNOWN_SCHEMAS.includes(d.schema))||d.schema===CBUNDLE_SCHEMA)){ csImportObject(d, fileName); return; } toast('That file is not a Studio data backup'); return; }
 const cols=v2?(d.collections||[]):((Array.isArray(d.selection)&&d.collection)?[{name:(d.collection||{}).name||'Restored collection', sel:d.selection, decisions:d.decisions||{}, coll:d.collection||{}, home:null}]:[]);
 const have=new Set(Object.keys(CS.items));
 openColl('Restore Studio Data', fileName||'', `<p class="small">Adds the backup's collections, favorites and snapshots to what is here; nothing here is deleted or replaced. Unchanged collections are skipped; changed ones are added as copies.</p>
  <div class="kv"><dt>Backup made</dt><dd>${esc(nowShort(d.exported)||'unknown')}</dd><dt>Collections</dt><dd>${cols.length}</dd><dt>Library favorites</dt><dd>${(d.favorites||[]).length}</dd><dt>Snapshots</dt><dd>${(d.snapshots||[]).length}</dd></div>
  <div class="acts"><span class="sp"></span><button class="btn" id="rsCancel">Cancel</button><button class="btn hk" id="rsGo">Restore</button></div>`);
 $('rsCancel').onclick=closeColl;
 $('rsGo').onclick=()=>{ try{ localStorage.setItem(CSKEY+'.before-restore', JSON.stringify({at:csNow(), store:CS, favorites:[...fav]})); }catch(e){}
  let added=0, skipped=0;
  cols.forEach(x=>{ if(!x||!Array.isArray(x.sel)) return; const same=x.id&&have.has(x.id)&&JSON.stringify((CS.items[x.id].sel||[]).slice().sort())===JSON.stringify(x.sel.slice().sort())&&CS.items[x.id].name===x.name; if(same){ skipped++; return; } const {rec}=csRecordFromFile(x); csCreate(rec.name, rec); added++; });
  (d.favorites||[]).forEach(id=>{ if(byId[id]) fav.add(id); });
  try{ const snaps=JSON.parse(localStorage.getItem(SKEY)||'[]'); const at=new Set(snaps.map(s=>s.at)); (d.snapshots||[]).forEach(s=>{ if(s&&s.at&&!at.has(s.at)) snaps.push(s); }); snaps.sort((a,b)=>a.at<b.at?1:-1); localStorage.setItem(SKEY, JSON.stringify(snaps.slice(0,30))); }catch(e){}
  if(typeof d.extended==='string'&&d.extended){ try{ localStorage.setItem(EKEY,d.extended); }catch(e){} } if(d.welcomed){ try{ localStorage.setItem(WKEY,'1'); }catch(e){} }
  persist(); render(); csMenuRefresh(); closeColl(); toast(`Restored ${added} collection${added===1?'':'s'}${skipped?` (${skipped} already here)`:''}; favorites and snapshots merged`); }; }
// ---- the My Collections menu (quick switch) and the manager ----
// the menu shows the open collection once (a card with its games and where it is saved), the commands, and up to six OTHER
// collections to switch to (five); Manage Collections lists every one (the bounded list never limits what is stored)
const csGames=n=>`${n.toLocaleString('en-US')} game${n===1?'':'s'}`;
function csMenuRefresh(){ const box=$('csQuick'); if(!box||!CS) return; const list=csList(); const others=list.filter(r=>r.id!==CS.active); const shown=others.slice(0,5); const cn=csCollSel().size;
 box.innerHTML=shown.map(r=>miHtml({ic:'🕹️', label:esc(r.name), pill:csGames((r.sel||[]).length), role:'menuitemradio', attrs:`aria-checked="false" data-cs="${r.id}"`})).join('')
  +(others.length>shown.length?miHtml({ic:'🗂️', label:`${others.length-shown.length} more in Manage Collections…`, cls:'quiet', attrs:'data-cs-all="1"'}):'');
 box.hidden=!others.length; const sl=$('csSwitchL'); if(sl) sl.hidden=!others.length;
 const t=$('csTotal'); if(t) t.textContent=`${list.length.toLocaleString('en-US')} collection${list.length===1?'':'s'}`;
 const h=$('collNameHead'), a=csActive(); if(h&&a) h.textContent=a.name; const g=$('collGames'); if(g) g.textContent=cn?csGames(cn):'No games yet';
 const c=$('collCount'); if(c) c.textContent=csGames(cn);
 const sh=$('mShare'); if(sh){ sh.disabled=!cn; const d=sh.querySelector('.d'); if(d) d.textContent=cn?'A complete .ccs file, every game with its ROM':'Add games to this collection first'; } }
// a card's ⋯ menu (Manage Collections and Manage Loadouts): the less frequent actions, destructive last
function rowMore(name){ return `<details class="csmore"><summary class="btn sm" aria-label="More actions for ${esc(name)}" title="More actions"><span aria-hidden="true">⋯</span></summary><div class="csmenu" role="menu">${miHtml({ic:'✏️', label:'Rename…', attrs:'data-a="rename"'})}${miHtml({ic:'📑', label:'Duplicate', attrs:'data-a="dup"'})}<div class="sep" role="separator"></div>${miHtml({ic:'🗑️', label:'Delete…', cls:'danger', attrs:'data-a="del"'})}</div></details>`; }
// a manager redrawn after a row's action (Rename, Duplicate, Delete, or their Cancel) puts focus back on that row's ⋯ (when the row still exists)
let ROW_RET=null;
function rowMoreWire(box){ box.querySelectorAll('details.csmore').forEach(d=>d.addEventListener('toggle',()=>{ if(d.open){ box.querySelectorAll('details.csmore[open]').forEach(o=>{ if(o!==d) o.open=false; }); rowMorePlace(d); const f=d.querySelector('.mi'); if(f) f.focus({preventScroll:true}); } }));
 if(ROW_RET){ const r=box.querySelector(ROW_RET); ROW_RET=null; if(r){ r.focus({preventScroll:true}); r.scrollIntoView({block:'nearest'}); } } }   // one ⋯ menu at a time
function csManager(){ csSaveActiveNow(); const list=csList(); const where=CO&&CO.desktop?'on this computer':'in this browser';
 openColl('Manage Collections', `${list.length.toLocaleString('en-US')} collection${list.length===1?'':'s'} · saved automatically ${where}`, `<div class="dquick">${miHtml({ic:'✨', label:'New Collection…', cls:'new', attrs:'id="csNewB"'})}${miHtml({ic:'📂', label:'Import Collection…', sub:'.ccs package or collection file', attrs:'id="csImpB"'})}</div>
  <div class="dlinks">${miHtml({ic:'🕘', label:'Restore Previous Version…', attrs:'id="csRestB"'})}${CO&&(CO.desktop||CO.served)?miHtml({ic:'🖥️', label:'Games Already on This Computer…', attrs:'id="csLocalB"'}):''}</div>
  <div class="cslist" role="list">${list.map(r=>{ const on=r.id===CS.active; const n=on?csCollSel().size:(r.sel||[]).length; const nl=(r.loadouts||[]).length; return `<div class="csrow${on?' on':''}" role="listitem" data-id="${r.id}"><span class="mic" aria-hidden="true">${on?'🎮':'🕹️'}</span><div class="csn"><b>${esc(r.name)}</b><span class="small">${csGames(n)}${nl?` · ${nl} NES Classic Loadout${nl===1?'':'s'}`:''} · changed ${esc(nowShort(r.modified))}</span></div>${on?'<span class="opentag"><span aria-hidden="true">✓</span> Open</span>':''}
   <div class="csacts">${on?'':`<button class="btn sm hk" data-a="open" aria-label="Open ${esc(r.name)}">Open</button>`}<button class="btn sm grow" data-a="share"><span aria-hidden="true">📤</span> Share or Export…</button>${rowMore(r.name)}</div></div>`; }).join('')}</div>
  <p class="small">Collections are kept only ${where}. To move them, use <b>Share or Export…</b> or <b>Back Up Studio Data</b> in the Studio menu${CO&&!CO.desktop&&!CO.file?', or <b>Open in Windows App</b>':''}.</p>`);
 $('csNewB').onclick=()=>csNewDialog(); $('csImpB').onclick=()=>{ closeColl(); csImportPick(); }; $('csRestB').onclick=()=>restoreMenu(); if($('csLocalB')) $('csLocalB').onclick=()=>loadLocalLibrary();
 rowMoreWire($('collBody'));
 $('collBody').onclick=e=>{ const b=e.target.closest('[data-a]'); if(!b) return; const id=b.closest('.csrow').dataset.id; const r=CS.items[id]; if(!r) return; const a=b.dataset.a; if(['rename','dup','del'].includes(a)) ROW_RET=`.csrow[data-id="${id}"] details.csmore>summary`;
  if(a==='open'){ csSwitch(id); csManager(); }
  else if(a==='rename'){ csNameDialog('Rename Collection', r.name, 'Rename', nm=>{ csRename(id, nm); csManager(); }); }
  else if(a==='dup'){ csDuplicate(id); csManager(); }
  else if(a==='export'){ csExport(id); }
  else if(a==='share'){ closeColl(); ccsShareDialog(id); }
  else if(a==='del'){ csNameDialog(`Delete "${r.name}"?`, null, 'Delete', ()=>{ csDelete(id); csManager(); }, `<p class="small">${(c=>`"${esc(r.name)}" (${c} game${c===1?'':'s'}${(r.loadouts||[]).length?` and ${(r.loadouts||[]).length} NES Classic Loadout${(r.loadouts||[]).length===1?'':'s'}`:''}) will be deleted.${c?' <b>Restore Previous Version</b> can bring it back for a while.':''}`)(id===CS.active?csCollSel().size:(r.sel||[]).length)} ROM files and personal games are not affected.</p>`); } }; }
function csNameDialog(title, value, action, then, html){ openColl(title,'', `${html||''}${value!==null?`<div class="fld"><label for="csNm">Collection name</label><input type="text" id="csNm" maxlength="80" value="${esc(value||'')}"></div>`:''}<div class="acts"><span class="sp"></span><button class="btn" id="csNo" data-cancel>Cancel</button><button class="btn ${action==='Delete'?'danger':'hk'}" id="csYes">${esc(action)}</button></div>`);
 const inp=$('csNm'); if(inp){ inp.focus(); inp.select(); inp.onkeydown=e=>{ if(e.key==='Enter') $('csYes').click(); }; }
 $('csNo').onclick=()=>{ if(title.startsWith('Delete')||title.startsWith('Rename')) csManager(); else closeColl(); };
 $('csYes').onclick=()=>{ const v=inp?inp.value.trim():''; if(inp&&!v){ inp.focus(); return; } then(v); }; }
function csNewDialog(){ csNameDialog('New Collection', csUniqueName('New Collection'), 'Create', nm=>{ csNew(nm); closeColl(); }, `<p class="small">Opens a new, empty collection; "${esc(COLL.name)}" stays in <b>My Collections</b>.</p>`); }
// ---- transfer to and from Classic Cart Studio for Windows ----
function csBundle(ids){ csSaveActiveNow(); return {schema:CBUNDLE_SCHEMA, exported:csNow(), app:'Classic Cart Studio', collections:ids.map(id=>CS.items[id]).filter(Boolean).map(csRecordOut)}; }
function csTransferPick(then){ csSaveActiveNow(); const list=csList();
 openColl('Open in Windows App','', `<p class="small">Choose collections to copy into the Windows app. They arrive as new collections; nothing there is overwritten or kept in sync.</p>
  <div class="cspick">${list.map(r=>`<label><input type="checkbox" value="${r.id}" ${r.id===CS.active?'checked':''}> <b>${esc(r.name)}</b> <span class="small">${(c=>`${c} game${c===1?'':'s'}`)(r.id===CS.active?csCollSel().size:(r.sel||[]).length)}</span></label>`).join('')}</div>
  <div class="acts"><button class="btn ghost" id="cpAll">Select All</button><span class="sp"></span><button class="btn" id="cpNo">Cancel</button><button class="btn hk" id="cpGo">Continue</button></div>`);
 $('cpAll').onclick=()=>document.querySelectorAll('.cspick input').forEach(x=>x.checked=true); $('cpNo').onclick=closeColl;
 $('cpGo').onclick=()=>{ const ids=[...document.querySelectorAll('.cspick input:checked')].map(x=>x.value); if(!ids.length){ toast('Choose at least one collection'); return; } then(ids); }; }
async function csToWebsite(){ csSaveActiveNow(); const ids=csList().map(r=>r.id); const res=await ccsSaveFile(`Classic Cart Studio Collections ${csStamp()}.json`, new Blob([JSON.stringify(csBundle(ids),null,1)],{type:'application/json'}), 'Collections File');
 if(!res.ok) return; openColl('Open Your Collections on the Website','', `<p class="small">Saved ${ids.length} collection${ids.length===1?'':'s'} to <b>${esc(res.name)}</b>. On <b>ClassicCartStudio.com</b>, choose <b>My Collections → Import .ccs Collection…</b> and pick that file; they arrive as new collections.</p><div class="acts"><span class="sp"></span><a class="btn hk" href="https://classiccartstudio.com/" target="_blank" rel="noopener">Open ClassicCartStudio.com</a><button class="btn" id="twOk">Done</button></div>`); $('twOk').onclick=closeColl; }
// ---- start ----
csInit();
// the app's durable copy may arrive after start (a new WebView2 profile): adopt it, or bring the restored single collection into the store
if(typeof desktopApplyState==='function'){ const _da=desktopApplyState; desktopApplyState=function(ls){ const r=_da.apply(this,arguments); try{ const s=csRead(); if(ls&&ls[CSKEY]&&s){ CS=s; if(CS.items[CS.active]) csLoadWorking(CS.items[CS.active]); } else if(CS&&csActive()&&!(csActive().sel||[]).length&&sel.size){ csSaveActiveNow(); } csMenuRefresh(); }catch(e){} return r; }; }
// the store's active collection is authoritative for the working copy when the two disagree (a store written by another tab or restored)
(function(){ const r=csActive(); if(r&&JSON.stringify([...sel].sort())!==JSON.stringify((r.sel||[]).filter(id=>byId[id]).slice().sort())&&r.modified&&COLL.modified&&r.modified>COLL.modified) csLoadWorking(r); })();
addEventListener('storage',e=>{ if(e.key!==CSKEY||!e.newValue) return; const s=csRead(); if(!s) return; const switched=CS&&s.active!==CS.active; CS=s; if(switched&&CS.items[CS.active]) csLoadWorking(CS.items[CS.active]); else csMenuRefresh(); });
// Restore Previous Version adds the earlier state as its own collection: restoring never overwrites the open collection (or any other)
restoreSnapshot=function(i){ const snaps=JSON.parse(localStorage.getItem(SKEY)||'[]'); const s=snaps[i]; if(!s) return; const known=(s.sel||[]).slice(); const dec=Object.assign({}, s.decisions||{});
 const r=csCreate(s.name||'Restored collection', {sel:known, decisions:dec, coll:{pref:s.pref||{}, multi:s.multi||{}, avail:s.avail||[], baseline:s.baseline||null, missing:s.missing||{}}, home:null}); csSwitch(r.id, true); closeColl();
 toast(`Restored "${s.name}" from ${nowShort(s.at)} as "${r.name}" (${known.length} game${known.length===1?'':'s'})`); };

// ---- every entry stays: games this computer cannot resolve (not in this Game Library, or a personal game whose file is not here) keep
//      their identity and description in the collection's `missing` list; they are counted, listed, exported, transferred and shared like
//      any other entry, and become ordinary entries as soon as the matching file is added (personal ids come from the file's content hash)
// descriptive details from packages and collection files are untrusted text: control/formatting characters removed, each field bounded;
// automatic values (a game id, a ROM file name, "Personal game") never count as a description (same rule as ccs_package.generic_text)
const CS_META_LIM={title:120, version:160, description:4000, year:8, publisher:80};
function csMeaningful(v, id){ const s=String(v==null?'':v).trim(); return !!s&&s!==id&&!/^(p_)?[0-9a-f]{16}(\.(nes|unh|bin|fds|zip))?$/i.test(s)&&!/\.(nes|unh|bin|fds|zip)$/i.test(s)&&!/^(personal game|unknown)$/i.test(s); }
function csCleanText(v, n, multi){ if(v==null||typeof v==='object'||typeof v==='boolean') return ''; const s=String(v).replace(/\r\n/g,'\n').replace(/\t/g,' ');
 return (multi?s.replace(/[\u0000-\u0009\u000b-\u001f\u007f-\u009f​-‏‪-‮⁠-⁯﻿]/g,''):s.replace(/[\u0000-\u001f\u007f-\u009f​-‏‪-‮⁠-⁯﻿]/g,'')).trim().slice(0,n).trim(); }
function csFirst(id, vals, n, multi){ for(const v of vals){ const c=csCleanText(v, n, multi); if(csMeaningful(c, id)) return c; } return ''; }
// artwork a kept entry may point to: an https cover, a package's own JPEG/PNG thumbnail (data URL, website) or the copy the app keeps in personal/art
function csThumbOk(u){ u=String(u||''); return (/^https:\/\/[^\s"'<>]+$/.test(u)&&u.length<2048)||(/^data:image\/(jpeg|png);base64,[A-Za-z0-9+/=]+$/.test(u)&&u.length<=1400000)||/^personal\/art\/p_[0-9a-f]{16}\.jpg$/.test(u)?u:''; }
function csEntryFromSelection(s, plib){ const id=s.version_id||s.game_id; const p=(plib||[]).find(x=>x&&x.id===id)||null; const pu=(p&&p.user&&typeof p.user==='object')?p.user:{};
 const src=s.source==='personal'||/^p_/.test(id||'')?'personal':s.source==='extended'?'extended':s.source==='catalog'?'catalog':(s.source||'unknown');
 const o={title:csFirst(id, [s.display_title, s.title, pu.title, p&&p.title], CS_META_LIM.title)||String(id).slice(0,120), version:csFirst(id, [s.version_label, s.nointro_name, s.version, pu.version_label, p&&p.version_label], CS_META_LIM.version),
  description:csFirst(id, [s.description, pu.description], CS_META_LIM.description, true), year:csFirst(id, [s.year, pu.year], CS_META_LIM.year), publisher:csFirst(id, [s.publisher, pu.publisher], CS_META_LIM.publisher), source:src,
  content_sha256:/^[0-9a-f]{64}$/.test((p&&p.content_sha256)||s.content_sha256||'')?((p&&p.content_sha256)||s.content_sha256):'', file_sha256:/^[0-9a-f]{64}$/.test((p&&p.file_sha256)||s.file_sha256||'')?((p&&p.file_sha256)||s.file_sha256):'',
  payload_sha256:/^[0-9a-f]{64}$/.test(s.payload_sha256||'')?s.payload_sha256:'', size:Number((p&&p.content_size)||s.size||0)||0, art:/^art\/[0-9a-f]{16}\.jpg$/.test(s.art||'')?s.art:'', thumb_url:/^https:\/\//.test(s.thumb_url||'')?csThumbOk(s.thumb_url):''};
 if(p) o.personal=csCleanPersonal(p, id);
 return o; }
// the personal-game record a kept entry carries for packages and collection files (portable fields only; never a path)
function csCleanPersonal(p, id){ const u=(p.user&&typeof p.user==='object')?p.user:{}; const hx=v=>/^[0-9a-f]{64}$/.test(v||'')?v:''; const num=v=>Math.max(0, Number(v)||0);
 return {id, title:csCleanText(p.title, CS_META_LIM.title), version_label:csCleanText(p.version_label, CS_META_LIM.version), content_sha256:hx(p.content_sha256), file_sha256:hx(p.file_sha256), content_size:num(p.content_size), file_size:num(p.file_size),
  format:csCleanText(p.format, 16), file_name:csCleanText(String(p.file_name||'').split(/[\\/]/).pop(), 200), user:{title:csCleanText(u.title, CS_META_LIM.title), version_label:csCleanText(u.version_label, CS_META_LIM.version), description:csCleanText(u.description, CS_META_LIM.description, true), year:csCleanText(u.year, CS_META_LIM.year), publisher:csCleanText(u.publisher, CS_META_LIM.publisher)}}; }
// a kept entry read from a package or collection file: known fields only, bounded and checked
function csCleanMissing(m, id){ m=(m&&typeof m==='object')?m:{}; const hx=v=>/^[0-9a-f]{64}$/.test(v||'')?v:'';
 const o={title:csCleanText(m.title, CS_META_LIM.title)||String(id).slice(0,120), version:csCleanText(m.version, CS_META_LIM.version), description:csCleanText(m.description, CS_META_LIM.description, true), year:csCleanText(m.year, CS_META_LIM.year), publisher:csCleanText(m.publisher, CS_META_LIM.publisher),
  source:['catalog','extended','personal','unknown'].includes(m.source)?m.source:(/^p_/.test(id)?'personal':'unknown'), content_sha256:hx(m.content_sha256), file_sha256:hx(m.file_sha256), payload_sha256:hx(m.payload_sha256), size:Math.max(0, Number(m.size)||0), art:/^art\/[0-9a-f]{16}\.jpg$/.test(m.art||'')?m.art:'', thumb_url:csThumbOk(m.thumb_url)};
 if(m.personal&&typeof m.personal==='object') o.personal=csCleanPersonal(m.personal, id);
 return o; }
// a package's (or collection's) entry and the selection describing the same game: field by field, the first real description wins
function csMergeEntry(m, e, id){ if(!m) return e; if(!e) return m; const o=Object.assign({}, e, m); ['title','version','description','year','publisher'].forEach(k=>{ o[k]=csMeaningful(m[k], id)?m[k]:(csMeaningful(e[k], id)?e[k]:(m[k]||e[k]||'')); }); if(!csMeaningful(o.title, id)) o.title=String(id).slice(0,120); o.thumb_url=m.thumb_url||e.thumb_url||''; o.art=m.art||e.art||''; if(!o.personal&&e.personal) o.personal=e.personal; return o; }
// personal games whose ROM file is on this computer keep their details in the app's personal store: a collection's kept details and
// artwork are merged into it there (personal.merge_metadata: what the person edited and real local details always win). The kept entry
// stays in the collection (pending) until the store confirms the merge: a refused, failed or interrupted request keeps it through
// autosaves, switching collections and restarts, and the next reconcile (every personal-games refresh, e.g. when the app connects or
// after an import) sends it again. Only a request in flight is not sent twice; nothing is remembered as "done" without confirmation.
function csHasDetails(x, id){ return !!x&&(['title','version','description','year','publisher'].some(k=>csMeaningful(x[k], id))||!!x.thumb_url); }
function csPendingPersonal(id, x){ return /^p_[0-9a-f]{16}$/.test(id||'')&&csHasDetails(x, id); }
function csMetaKey(id, x){ return id+'|'+JSON.stringify([x.title||'', x.version||'', x.description||'', x.year||'', x.publisher||'', x.thumb_url||'']); }
// the store confirmed this exact entry: drop it from every collection that still holds the same details (others stay pending)
function csSettlePersonal(id, key){ let n=0; const drop=m=>{ if(m&&m[id]&&csMetaKey(id, m[id])===key){ delete m[id]; n++; } };
 drop(COLL.missing); Object.values((typeof CS!=='undefined'&&CS.items)||{}).forEach(r=>drop(r&&r.coll&&r.coll.missing)); if(n){ saveColl(); csWrite(); } }
const CS_INFLIGHT=new Map();   // key -> the request's promise (a second call for the same details waits for it instead of sending again)
async function csMergePersonal(list){ const res={sent:0, ok:0, changed:0, failed:[]}; if(!(typeof COMPANION!=='undefined'&&COMPANION.ok)||typeof hkPost!=='function') return res;
 for(const [id, x] of list){ if(!/^p_[0-9a-f]{16}$/.test(id)||!PERSONAL[id]||!x) continue; const key=csMetaKey(id, x);
  let p=CS_INFLIGHT.get(key);
  if(!p){ const art=/^data:image\/(jpeg|png);base64,/.test(x.thumb_url||'')?x.thumb_url:''; res.sent++;
   p=(async()=>{ try{ return await hkPost('/api/personal/merge', {id, meta:{title:x.title, version_label:x.version, description:x.description, year:x.year, publisher:x.publisher}, art}); }catch(e){ return {ok:false, error:String(e)}; } })();
   CS_INFLIGHT.set(key, p); p.finally(()=>{ if(CS_INFLIGHT.get(key)===p) CS_INFLIGHT.delete(key); }); }
  const r=await p;
  if(r&&r.ok){ res.ok++; if((r.changed||[]).length) res.changed++; csSettlePersonal(id, key); } else res.failed.push(id); }
 return res; }
// every pending entry for a personal game that is on this computer (one request per distinct set of details)
function csPendingList(){ const todo=new Map(); const scan=miss=>Object.entries(miss||{}).forEach(([id,x])=>{ if(PERSONAL[id]&&csPendingPersonal(id, x)){ const k=csMetaKey(id, x); if(!todo.has(k)) todo.set(k, [id, x]); } });
 scan(COLL.missing); Object.values((typeof CS!=='undefined'&&CS.items)||{}).forEach(r=>scan(r&&r.coll&&r.coll.missing)); return [...todo.values()]; }
function csReconcilePersonal(){ const l=csPendingList(); return l.length?csMergePersonal(l):Promise.resolve({sent:0, ok:0, changed:0, failed:[]}); }
// while details are still pending, the game shows them where its own are only automatic (never over what the person edited)
function csShowPending(){ csPendingList().forEach(([id, x])=>{ const g=byId[id], e=PERSONAL[id]; if(!g||!e||g.source!=='personal') return; const u=e.user||{}, ed=u.edited||[];
  const auto=t=>!csMeaningful(t, id)||t===(e.detected||{}).title||t===String(e.file_name||'').replace(/\.[^.]*$/,'');
  if(!ed.includes('title')&&auto(g.display_title)&&csMeaningful(x.title, id)) g.display_title=x.title;
  if(!ed.includes('version_label')&&!u.version_label&&csMeaningful(x.version, id)) g.rev=g.nointro_name=x.version;
  if(!ed.includes('year')&&!g.year&&x.year) g.year=x.year;
  if(!ed.includes('publisher')&&!g.publisher&&x.publisher) g.publisher=x.publisher;
  if(!ed.includes('art')&&!g.art&&x.thumb_url) g.art=ccsArtSrc(x.thumb_url); }); }
(function(){ if(typeof refreshPersonal!=='function') return; const _rp=refreshPersonal; let busy=false; refreshPersonal=async function(){ const r=await _rp.apply(this, arguments); if(busy){ csShowPending(); return r; } busy=true;
  try{ const m=await csReconcilePersonal(); if(m.changed) await _rp.apply(this, arguments); csShowPending(); if(typeof render==='function') render(); }catch(e){} finally{ busy=false; } return r; }; })();
// a cover's address: catalog box art is part of the site (relative, the same on the website and in the app); other images (personal
// artwork) are served by the app's backend (artUrl); full URLs and data/blob images as they are
function ccsArtSrc(p){ p=String(p||''); if(!p) return ''; if(/^(https?:|data:|blob:)/.test(p)||/^art\/[0-9a-f]{16}\.jpg$/.test(p)) return p; return artUrl(p); }
function csMissing(id){ return (COLL.missing||{})[id]||{title:id, source:'unknown'}; }
// where the games of a collection that are not in this Game Library are: the Windows app keeps ROM files on this computer; the website keeps none
function csAwayHere(){ return typeof CO!=='undefined'&&!!(CO.ok||CO.desktop); }
function csAwayWord(){ return csAwayHere()?'not on this computer':'not in the Game Library'; }
// every entry of the open collection, in collection order: {id, g (when resolvable here), m (metadata otherwise)}
function csEntries(){ return [...csCollSel()].map(id=>byId[id]?{id, g:byId[id]}:{id, m:csMissing(id)}); }
window.csEntries=csEntries;
// the collection file lists every entry: entries this computer cannot resolve are written from their kept metadata (available: false)
(function(){ const _bm=buildManifest; buildManifest=function(){ const m=_bm.apply(this,arguments); const have=new Set(m.selections.map(s=>s.version_id));
  [...sel].filter(id=>!byId[id]&&!have.has(id)).forEach(id=>{ const x=csMissing(id); m.selections.push({title_id:null, version_id:id, game_id:id, payload_sha256:x.payload_sha256||x.content_sha256||null, title:x.title, display_title:x.title, version_label:x.version||'', nointro_name:x.version||'', description:x.description||'', year:x.year||'', publisher:x.publisher||'', source:x.source||'unknown', available:false, content_sha256:x.content_sha256||null, file_sha256:x.file_sha256||null, size:x.size||0, art:x.art||'', thumb_url:x.thumb_url||'', revision_decision:decisions[id]||null, install_required:true, comparison_status:'unknown'});
   if(x.personal&&!m.personal_library.some(p=>p.id===id)) m.personal_library.push(Object.assign({identity:'personal', file_present:false}, x.personal)); });
  (m.personal_library||[]).forEach(p=>{ if(p&&p.file_name) p.file_name=String(p.file_name).split(/[\\/]/).pop(); delete p.path; delete p.source_path; });   /* a file name only: never where the file is */
  /* details still pending for a game on this computer go out with the package as the store will have them (same rules as csShowPending) */
  (m.personal_library||[]).forEach(p=>{ const x=p&&(COLL.missing||{})[p.id]; if(!csPendingPersonal(p.id, x)) return; const e=PERSONAL[p.id]||{}, ed=((e.user||{}).edited)||[]; const u=Object.assign({}, p.user||{});
   const auto=t=>!csMeaningful(t, p.id)||t===(e.detected||{}).title||t===String(e.file_name||'').replace(/\.[^.]*$/,'');
   if(!ed.includes('title')&&auto(u.title)&&csMeaningful(x.title, p.id)) u.title=p.title=x.title;
   [['version_label','version'],['description','description'],['year','year'],['publisher','publisher']].forEach(([f,k])=>{ if(!ed.includes(f)&&!String(u[f]||'').trim()&&csMeaningful(x[k], p.id)){ u[f]=x[k]; if(f==='version_label') p.version_label=x[k]; } });
   p.user=u; });
  m.count=m.selections.length; if(m.summary) m.summary.collection=m.selections.length; m.unavailable=[...sel].filter(id=>!byId[id]).length; return m; }; })();
// the tray lists the entries whose file or library record is not here, below the others
(function(){ if(typeof renderTray!=='function') return; const _rt=renderTray; renderTray=function(){ const r=_rt.apply(this,arguments); try{ const miss=[...sel].filter(id=>!byId[id]); const ul=$('trayList'); if(ul&&miss.length){ ul.querySelectorAll('li.empty').forEach(x=>x.remove());
   ul.insertAdjacentHTML('beforeend', `<li class="trmiss-h"><div class="small"><b>${miss.length} game${miss.length===1?'':'s'} ${csAwayWord()}</b> · ${csAwayHere()?'add their ROM files with Add Your Own Game':'their details are kept with this collection; the website does not keep ROM files'}</div></li>`+miss.map(id=>{ const x=csMissing(id); const img=x.art?ccsArtSrc(x.art):ccsArtSrc(x.thumb_url||''); return `<li class="trmiss">${img?`<img loading="lazy" src="${esc(img)}" alt="" onerror="this.style.visibility='hidden'">`:'<div class="ph" style="width:36px;height:51px;padding:0;font-size:7px"><span style="border:none">NES</span></div>'}<div style="min-width:0;flex:1"><b class="tl">${esc(x.title)}</b><div class="small">${[x.version, x.year, x.publisher].filter(Boolean).map(v=>esc(String(v))).join(' · ')}</div><div class="small"><span class="tag">${x.source==='personal'?'personal game':'not in this Game Library'}</span></div></div><span class="x" data-rm="${esc(id)}" title="Remove from collection" role="button" tabindex="0">✕</span></li>`; }).join('')); } }catch(e){} return r; }; })();
