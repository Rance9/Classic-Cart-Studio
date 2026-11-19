// ---- My Collections: many named collections, one active. The same code runs on the website and in Classic Cart Studio for Windows.
//      Storage: this browser's (or the app's WebView2) local storage under one key; the app also mirrors it to its data folder through
//      the backend (desktop.js DESKTOP_KEYS), so collections survive a lost browser profile. The active collection is "worked on" in the
//      established single-collection keys (selection, decisions, collection details, HOME settings): every existing feature keeps working
//      unchanged, and the active record is updated from them automatically (Save Collection = automatic). Switching stores the current
//      collection first, then loads the other one: nothing is ever discarded. Library favorites are global; HOME favorites, folders and
//      the HOME theme belong to each collection.
//      Collections are the one way games are organized (D-146): a collection holds its games, their order and folders and its HOME
//      appearance, and HOME shows the open collection. There are no separate lineups: different arrangements are different collections
//      (Duplicate makes a starting point; no game file is copied). Stores, backups and files written by earlier versions that still carry
//      lineups are read through csLineupCollections(): each distinct lineup becomes a collection of its own.
const CSKEY='studio-collections-v2', CS_SCHEMA='ccs-collections-store/1', CREC_SCHEMA='ccs-collection/1', CBUNDLE_SCHEMA='ccs-collections/1', SBACKUP_SCHEMA='ccs-studio-backup/2';
const CS_MODEL='collections/2';   // a store written by this version: collections only (no lineups), `active` may be null (no collection at all)
if(typeof DESKTOP_KEYS!=='undefined'&&!DESKTOP_KEYS.includes(CSKEY)) DESKTOP_KEYS.push(CSKEY);   // the app mirrors the collections to its data folder
let CS=null, CS_T=null;
// CS_STATE: 'ok', or 'unreadable' when the stored collections (or the open collection's own keys) could not be read: then nothing is
// written, nothing is set up as new, and the page says so (a storage problem is never taken for an empty Studio). CS_FRESH: this browser
// or app had nothing of the person's at start (no collections and none of the earlier single-collection keys).
let CS_STATE='ok', CS_FRESH=false;
// earlier versions' lineup state: never set now; kept so older code paths that still name it read "no lineup"
let LO=null, CS_COLLVIEW=0;
function csCollSel(){ return sel; }
function csNow(){ return new Date().toISOString(); }
function csId(){ return 'c'+(typeof uuid==='function'?uuid():String(Math.random()).slice(2)).replace(/-/g,'').slice(0,15); }
// the stored collections: {state:'absent'} (nothing stored), {state:'ok', store} or {state:'unreadable'} (storage could not be read, or what
// it holds is not a collections store)
function csReadState(){ let raw; try{ raw=localStorage.getItem(CSKEY); }catch(e){ return {state:'unreadable', why:'read'}; }
 if(raw==null) return {state:'absent'};
 try{ const s=JSON.parse(raw); if(s&&s.schema===CS_SCHEMA&&s.items&&typeof s.items==='object'&&!Array.isArray(s.items)) return {state:'ok', store:s}; }catch(e){}
 return {state:'unreadable', why:'content'}; }
function csRead(){ const s=csReadState(); return s.state==='ok'?s.store:null; }
function csWrite(){ if(CS_STATE!=='ok') return;   /* an unreadable store is never overwritten */
 csMarkSaved();   /* `saved`: when the store's content last changed (desktop.js compares it with the app's durable copy at start) */
 try{ localStorage.setItem(CSKEY, JSON.stringify(CS)); }catch(e){ toast('Could not save collections (browser storage may be full). Choose Back Up All Collections now (Studio menu).'); } try{ if(typeof desktopStateDirty==='function') desktopStateDirty(); }catch(e){} }
function csMarkSaved(){ const bare=o=>{ if(!o||typeof o!=='object') return ''; const c=Object.assign({}, o); delete c.saved; return JSON.stringify(c); };
 let old=null; try{ old=JSON.parse(localStorage.getItem(CSKEY)||'null'); }catch(e){}
 CS.saved=(old&&typeof old.saved==='string'&&bare(old)===bare(CS))?old.saved:csNow(); }   // an identical rewrite (a start, a redraw) keeps the time
function csHomeRead(){ try{ return JSON.parse(localStorage.getItem(HOME_KEY)||'null'); }catch(e){ return null; } }
function csCollFields(c){ const o={}; ['created','modified','savedToFile','prepared','baseline','pref','multi','avail','placeholders','missing','included'].forEach(k=>{ if(c&&c[k]!==undefined) o[k]=c[k]; }); return JSON.parse(JSON.stringify(o)); }
// the working copy (the established keys) -> a record
function csFromWorking(id, name){ const S=sel, D=decisions; const coll=csCollFields(COLL); const mi={}; [...S].forEach(x=>{ if(coll.missing&&coll.missing[x]&&(!byId[x]||csPendingPersonal(x, coll.missing[x]))) mi[x]=coll.missing[x]; });   /* a personal game's details stay until the store confirms them (csMergePersonal) */ coll.missing=mi; return {id, name:(name||COLL.name||'My Collection').slice(0,80), created:COLL.created||csNow(), modified:COLL.modified||csNow(), sel:[...S], decisions:JSON.parse(JSON.stringify(D||{})), coll, home:csHomeRead()}; }
function csActive(){ return CS&&CS.active&&CS.items[CS.active]||null; }
function csList(){ return CS?Object.values(CS.items).sort((a,b)=>(b.modified||'').localeCompare(a.modified||'')):[]; }
// ---- start (idempotent): the stored collections, made collections-only once (csMigrateStore); the earlier single collection becomes
//      the first named one; a Studio with nothing in it gets the Included Games once (studio/included.js inclStarter) ----
function csInit(){ const st=csReadState(); const bad=(typeof LS_BAD!=='undefined'&&LS_BAD.length)?LS_BAD.slice():[];
 if(st.state==='unreadable'||bad.length){ CS_STATE='unreadable'; CS={schema:CS_SCHEMA, model:CS_MODEL, active:null, items:{}}; csEmptyWorking(); return; }   // nothing is written or set up as new
 if(st.state==='ok'){ CS=st.store; const moved=csMigrateStore();
  if(!csActive()){ const l=csList(); if(l.length){ CS.active=l[0].id; csWrite(); csLoadWorking(CS.items[CS.active]); } else { CS.active=null; csEmptyWorking(); } }
  else if(moved) csLoadWorking(csActive());   // a lineup that was open is now its own collection, and that one is open
  return; }
 let legacy={}; try{ [KEY,DKEY,CKEY,HOME_KEY].forEach(k=>{ const v=localStorage.getItem(k); if(v!=null) legacy[k]=v; }); }catch(e){ legacy={}; }
 if(csLegacyHas(legacy)){ const id=csId(); CS={schema:CS_SCHEMA, model:CS_MODEL, active:id, items:{}, migrated:{at:csNow(), from:'single collection (Classic Cart Studio 0.6.1 and earlier)'}};
  CS.items[id]=csFromWorking(id); csWrite();
  try{ localStorage.setItem(CSKEY+'.before-migration', JSON.stringify({at:csNow(), keys:legacy})); }catch(e){}   // the original keys also stay where they were: nothing is removed
  return; }
 // nothing of the person's here yet: no collection is open, and nothing is written until the Included Games are ready (inclStarter
 // prepares them once, under a lock shared with other tabs) or the person creates, adds or restores something themselves
 CS_FRESH=true; CS={schema:CS_SCHEMA, model:CS_MODEL, active:null, items:{}, starter:{state:'pending', at:csNow()}}; csEmptyWorking(); }
// the earlier single collection's keys hold something of the person's (games, decisions, a named or changed collection, HOME settings);
// keys an earlier start wrote with nothing in them do not make a Studio established
function csLegacyHas(l){ const js=(k,d)=>{ try{ return JSON.parse(l[k]); }catch(e){ return d; } };
 const s=js(KEY, []), d=js(DKEY, {}), c=js(CKEY, {});
 return (Array.isArray(s)&&s.length>0)||(d&&typeof d==='object'&&Object.keys(d).length>0)||(c&&typeof c==='object'&&(!!c.modified||(!!c.name&&c.name!=='My Collection')))||l[HOME_KEY]!=null; }
// the working copy when no collection is open (a new Studio, or every collection removed on purpose): no games, default HOME settings.
// A new Studio writes nothing here (until the Included Games are ready or the person acts); an unreadable store keeps the earlier keys as they are
function csEmptyWorking(){ sel=new Set(); decisions={}; KEPT_OFF=null;
 COLL=Object.assign({}, COLL||{}, {name:'', created:csNow(), modified:null, savedToFile:null, prepared:null, baseline:null, pref:{}, multi:{}, avail:[], placeholders:[], missing:{}, included:undefined});
 if(CS_STATE!=='ok'||CS_FRESH) return;
 try{ localStorage.removeItem(HOME_KEY); localStorage.setItem(KEY, '[]'); localStorage.setItem(DKEY, '{}'); localStorage.setItem(CKEY, JSON.stringify(COLL)); }catch(e){} }
// a collection to add games to: the open one, or a new one when none is open (Add ROM Files and restores on an empty Studio)
function csEnsureActive(name){ if(CS_STATE!=='ok') return null; const r=csActive(); if(r) return r; const n=csCreate(name||'My Collection'); csSwitch(n.id, true); return CS.items[n.id]; }
// ---- automatic save: the active record follows the working copy ----
function csSaveActiveNow(){ if(!CS) return; clearTimeout(CS_T); CS_T=null; const r=csActive(); if(!r) return; const n=csFromWorking(r.id, COLL.name); n.shares=r.shares||[]; n.created=r.created||n.created; ['fromLineup'].forEach(k=>{ if(r[k]) n[k]=r[k]; }); CS.items[r.id]=n; csWrite(); csMenuRefresh(); }
// ---- collections only (D-146): every distinct lineup of an earlier version becomes a collection of its own, once ----
// A lineup (a named subset of one collection planned for an NES Classic) becomes "<collection>: <lineup>" with its own games, versions,
// HOME settings (theme, folders, favorites) and the collection's kept details for those games. A lineup that is the same as its collection,
// or as another lineup already converted, adds nothing. The new collection remembers where it came from (fromLineup), so running this
// again, from another tab, or on a backup made before it never adds the same lineup twice. Nothing is copied but the record: game files,
// saves and Game Settings belong to the games' exact identities and stay where they are.
function csLineupKey(sel0, dec, home){ return JSON.stringify(csCanon({sel:[...(sel0||[])].sort(), decisions:dec||{}, home:csHomeKey(home)})); }
// .alias: every lineup's id -> the index in the result of the record made for it, or -1 when it is the same as its collection. A lineup
// identical to an earlier one (or to its collection) adds nothing, but its identity is kept here, so the open choice is never lost (F1).
function csLineupCollections(r, list, taken){ const out=[]; out.alias={}; const ids=new Set(r.sel||[]); const seen=new Map([[csLineupKey(r.sel, r.decisions, r.home), -1]]);
 (Array.isArray(list)?list:[]).forEach(l=>{ if(!l||typeof l!=='object') return; const ls=(Array.isArray(l.sel)?l.sel:[]).filter(id=>ids.has(id)); const dec={};
  ls.forEach(id=>{ const v=(l.decisions||{})[id]||(r.decisions||{})[id]; if(v==='factory'||v==='alternate') dec[id]=v; });
  const home=l.home&&typeof l.home==='object'?JSON.parse(JSON.stringify(l.home)):null; const k=csLineupKey(ls, dec, home); const lid=l.id==null?'':String(l.id);
  if(seen.has(k)){ if(lid&&!(lid in out.alias)) out.alias[lid]=seen.get(k); return; } seen.set(k, out.length); if(lid&&!(lid in out.alias)) out.alias[lid]=out.length;
  const c=JSON.parse(JSON.stringify(r.coll||{})); delete c.included; const keep=new Set(ls); const sub=o=>{ const x={}; Object.entries(o||{}).forEach(([id,v])=>{ if(keep.has(id)) x[id]=v; }); return x; };
  c.missing=sub(c.missing); c.avail=(c.avail||[]).filter(id=>keep.has(id)); c.placeholders=[]; c.savedToFile=null; c.prepared=null;
  const nm=String(`${r.name||'Collection'}: ${String(l.name||'Lineup').trim()||'Lineup'}`).slice(0,72); const name=taken?csCopyName(nm, taken, 72):nm; if(taken) taken.add(name.toLowerCase());
  out.push({name, created:l.created||r.created||csNow(), modified:l.modified||r.modified||csNow(), sel:ls, decisions:dec, coll:c, home, fromLineup:{collection:String(r.id||''), lineup:String(l.id||'')}, lineupId:l.id}); });
 return out; }
function csMigrateStore(){ if(!CS||CS_STATE!=='ok') return false; const recs=Object.values(CS.items); const has=recs.some(r=>Array.isArray(r.loadouts)&&r.loadouts.length||r.openLoadout);
 if(!has&&CS.model===CS_MODEL) return false;
 if(has){ try{ if(localStorage.getItem(CSKEY+'.before-collections-only')==null) localStorage.setItem(CSKEY+'.before-collections-only', JSON.stringify({at:csNow(), store:CS})); }catch(e){} }   // the store exactly as it was, once (never overwritten)
 const taken=new Set(recs.map(r=>String(r.name||'').toLowerCase())); const done=new Set(recs.filter(r=>r.fromLineup).map(r=>r.fromLineup.collection+'|'+r.fromLineup.lineup)); let active=CS.active;
 const map=Object.assign({}, CS.lineupMap&&typeof CS.lineupMap==='object'?CS.lineupMap:{});
 recs.forEach(r=>{ const open=r.openLoadout; const made=csLineupCollections(r, r.loadouts, null); const got=[]; const rid=String(r.id||'');
  // what this collection's lineups already became (another tab, an earlier run, a store restored over a converted one): the same content
  const prior=new Map(); recs.forEach(x=>{ if(x.fromLineup&&x.fromLineup.collection===rid){ const k=csLineupKey(x.sel, x.decisions, x.home); if(!prior.has(k)) prior.set(k, x.id); } });
  made.forEach((m,i)=>{ const key=m.fromLineup.collection+'|'+m.fromLineup.lineup; delete m.lineupId;
   if(done.has(key)){ const ex=recs.find(x=>x.fromLineup&&x.fromLineup.collection+'|'+x.fromLineup.lineup===key); got[i]=ex?ex.id:null; return; }
   const eq=prior.get(csLineupKey(m.sel, m.decisions, m.home)); if(eq&&CS.items[eq]){ got[i]=eq; done.add(key); return; }
   const name=csCopyName(m.name, taken, 72); taken.add(name.toLowerCase()); let id=csId(); while(CS.items[id]) id=csId();
   CS.items[id]=Object.assign({}, m, {id, name, shares:[]}); done.add(key); got[i]=id; });
  Object.entries(made.alias).forEach(([lid,i])=>{ const t=i<0?r.id:got[i]; if(t) map[rid+'|'+lid]=t; });
  if(open!=null&&open!==''&&r.id===CS.active){ const t=map[rid+'|'+String(open)]; if(t&&CS.items[t]) active=t; }
  delete r.loadouts; delete r.openLoadout; });
 if(Object.keys(map).length) CS.lineupMap=map;   /* every earlier lineup and the collection it is now (kept, never needed to read the store) */
 CS.model=CS_MODEL; CS.active=active; csWrite(); return true; }
function csDirty(){ if(!CS) return; clearTimeout(CS_T); CS_T=setTimeout(csSaveActiveNow,400); }
(function(){ ['persist','saveColl'].forEach(n=>{ const f=window[n]; if(typeof f==='function') window[n]=function(){ const r=f.apply(this,arguments); try{ csDirty(); }catch(e){} return r; }; });
 if(typeof homeExtrasSave==='function'){ const h=homeExtrasSave; homeExtrasSave=function(){ const r=h.apply(this,arguments); try{ csDirty(); }catch(e){} return r; }; }
 addEventListener('beforeunload',()=>{ try{ if(CS_T) csSaveActiveNow(); }catch(e){} }); document.addEventListener('visibilitychange',()=>{ if(document.hidden&&CS_T) csSaveActiveNow(); }); })();
// ---- switching: store the current collection, then load the other one into the working copy ----
function csLoadWorking(r){ KEPT_OFF=null; sel=new Set(r.sel||[]); decisions=JSON.parse(JSON.stringify(r.decisions||{}));
 COLL=Object.assign({name:r.name, created:r.created, modified:r.modified, savedToFile:null, prepared:null, baseline:null, pref:{}, multi:{}, avail:[], placeholders:[], missing:{}, personalCache:(COLL&&COLL.personalCache)||{}}, csCollFields(r.coll||{}), {name:r.name}); if(r.coll&&r.coll.personalCache){ COLL.missing=csCacheIntoKept(COLL.missing, r.coll.personalCache, r.sel||[]); setTimeout(()=>{ try{ if(CS&&CS.active===r.id) csSaveActiveNow(); }catch(e){} }, 0); }   /* the record is saved once without its cache */
 try{ if(r.home) localStorage.setItem(HOME_KEY, JSON.stringify(r.home)); else localStorage.removeItem(HOME_KEY); }catch(e){}
 localStorage.setItem(KEY, JSON.stringify([...sel])); localStorage.setItem(DKEY, JSON.stringify(decisions)); localStorage.setItem(CKEY, JSON.stringify(COLL));
 if((r.sel||[]).some(id=>byId[id]&&byId[id].ext)&&typeof setExt==='function') setExt(true);
 try{ if(typeof TV!=='undefined'){ if(TV.open&&typeof closeTv==='function') closeTv(); TV.model=null; TV.proj=null; } }catch(e){}
 try{ if(typeof desktopStateDirty==='function') desktopStateDirty(); }catch(e){}
 render(); updateStatusLine(); try{ hkLabelUpdate(); }catch(e){} csMenuRefresh(); try{ if(typeof rdyChanged==='function') rdyChanged(); }catch(e){} }
function csSwitch(id, quiet){ if(!CS||!CS.items[id]) return; if(id===CS.active){ if(!quiet) toast(`"${CS.items[id].name}" is already open`); return; }
 csSaveActiveNow(); CS.active=id; csWrite(); csLoadWorking(CS.items[id]); if(typeof desktopStatePush==='function') setTimeout(desktopStatePush, 0);   // the app's durable copy follows at once (a quick close keeps the choice)
 if(!quiet) toast(`Opened "${CS.items[id].name}" · ${sel.size} game${sel.size===1?'':'s'}`); }
// D-63: an automatically generated name for a copy or a name collision reads "Name (Copy 1)", "Name (Copy 2)"... (the first free number,
// unique ignoring case, within the field's length). A free name is kept as asked; a name that already ends in a generated "(Copy n)" is
// not stacked ("Name (Copy 1)" copies to "Name (Copy 2)"). Stored names are never changed by this (only new names are generated)
function csCopyName(name, taken, max){ const want=String(name||'').trim().slice(0, max); if(want&&!taken.has(want.toLowerCase())) return want;
 const root=want.replace(/\s*\(Copy \d+\)$/i,'').trim()||want;
 for(let i=1;i<1000;i++){ const suf=` (Copy ${i})`; const n=root.slice(0, Math.max(1, max-suf.length)).trimEnd()+suf; if(!taken.has(n.toLowerCase())) return n; }
 return root.slice(0, max-14)+' '+Date.now(); }
function csUniqueName(name){ const base=(name||'My Collection').trim().slice(0,72)||'My Collection'; return csCopyName(base, new Set(csList().map(r=>r.name.toLowerCase())), 72); }
function csCreate(name, rec){ csSaveActiveNow(); let id=csId(); while(CS.items[id]) id=csId(); const now=csNow(); const r=Object.assign({sel:[],decisions:{},coll:{},home:null}, rec||{}, {id, name:csUniqueName(name), created:(rec&&rec.created)||now, modified:now, shares:[]}); delete r.loadouts; delete r.openLoadout; delete r.lineupId; CS.items[id]=r; csWrite(); return r; }
function csNew(name){ const r=csCreate(name||'New Collection'); csSwitch(r.id, true); toast(`Created "${r.name}"`); return r; }
function csRename(id, name){ const r=CS.items[id]; if(!r) return; name=(name||'').trim().slice(0,80); if(!name) return; if(id===CS.active){ COLL.name=name; saveColl(); updateStatusLine(); csSaveActiveNow(); } else { r.name=name; r.modified=csNow(); csWrite(); } csMenuRefresh(); }
function csDuplicate(id){ if(id===CS.active) csSaveActiveNow(); const src=CS.items[id]; if(!src) return null; const copy=JSON.parse(JSON.stringify(src)); delete copy.shares; if(copy.coll) delete copy.coll.included;   /* a copy is the person's own collection, not the Included Games one */ const r=csCreate(src.name, copy);   /* the copy's name: "Name (Copy 1)" (csUniqueName) */ toast(`Duplicated as "${r.name}"`); return r; }
function csDelete(id){ const r=CS.items[id]; if(!r) return; if(typeof snapshotNow==='function'&&id===CS.active) snapshotNow('before deleting "'+r.name+'"');
 else { try{ const snaps=JSON.parse(localStorage.getItem(SKEY)||'[]'); if((r.sel||[]).length) snaps.unshift({at:csNow(),reason:'before deleting "'+r.name+'"',name:r.name,sel:r.sel,decisions:r.decisions||{},pref:(r.coll||{}).pref||{},multi:(r.coll||{}).multi||{},avail:(r.coll||{}).avail||[],fav:[...fav],baseline:(r.coll||{}).baseline||null,fp:null}); localStorage.setItem(SKEY, JSON.stringify(snaps.slice(0,12))); }catch(e){} }
 delete CS.items[id];
 if(!Object.keys(CS.items).length){ CS.active=null; csWrite(); csEmptyWorking(); render(); updateStatusLine(); csMenuRefresh(); try{ if(typeof rdyChanged==='function') rdyChanged(); }catch(e){} }   // no collection at all, as the person chose: the empty Studio says what to do next (nothing is added back)
 else if(id===CS.active){ CS.active=csList()[0].id; csWrite(); csLoadWorking(CS.items[CS.active]); }
 else csWrite();
 toast(`Deleted "${r.name}"${(r.sel||[]).length?'. Go Back to an Earlier Version can bring it back for a while':''}`); }
// a collection's portable file: the schema-4 collection (readable by every earlier version) plus the exact Studio record (HOME folders,
// theme and HOME favorites travel with it). Built for any collection, not only the open one.
function csWithRecord(r, fn){ if(r.id===CS.active){ csSaveActiveNow(); return fn(); }
 const s0=sel, d0=decisions, c0=COLL; try{ sel=new Set(r.sel||[]); decisions=r.decisions||{}; COLL=Object.assign({}, c0, {savedToFile:null,prepared:null,baseline:null,pref:{},multi:{},avail:[],placeholders:[],missing:{},included:undefined}, r.coll||{}, {name:r.name}); return fn(); } finally { sel=s0; decisions=d0; COLL=c0; } }
function csRecordOut(r, o){ return {schema:CREC_SCHEMA, id:r.id, name:r.name, created:r.created, modified:r.modified, sel:r.sel||[], decisions:r.decisions||{}, coll:r.coll||{}, home:typeof suInlineHome==='function'?suInlineHome(r.home||null, !!(o&&o.export===true)):(r.home||null)}; }   // collections only: files written now carry no lineups; a startup picture of the collection's own travels inside (startup-custom.js; o.export: a backup being made, after suEnsure, never without its picture)
// an NES Classic Loadout in a file: its name, its games (exact version ids, every one of them in the collection), decisions and HOME settings
// a file's records: the collection itself and, for files written by earlier versions, one more collection for each distinct lineup it
// carried (csLineupCollections). Each goes through csRecordFromFile, so every value is checked the same way.
function csFileRecords(x, fallbackName){ const one=csRecordFromFile(x, fallbackName); const out=[one]; out.open=0;
 if(x&&Array.isArray(x.loadouts)&&x.loadouts.length){ const base=Object.assign({}, one.rec, {id:typeof x.id==='string'?x.id.slice(0,64):''});
  const made=csLineupCollections(base, csLoadoutsIn(x.loadouts, one.rec.sel), null);
  made.forEach(m=>{ const fl=m.fromLineup; const r=csRecordFromFile(m, m.name); r.rec.fromLineup=fl; out.push(r); });
  const a=x.openLoadout!=null?made.alias[String(x.openLoadout)]:undefined; if(typeof a==='number'&&a>=0) out.open=a+1; }   /* the file's open lineup: what it became (F1) */
 return out; }
// earlier versions' lineups as they were written (read only: lineups are never written now)
function csLoadoutsIn(list, ids){ const set=new Set(ids); const seen=new Set(); return (Array.isArray(list)?list:[]).slice(0,200).filter(l=>l&&typeof l==='object'&&(!l.target||l.target==='nes-classic')).map(l=>{ const id=typeof l.id==='string'&&/^[A-Za-z0-9_-]{1,40}$/.test(l.id)&&!seen.has(l.id)?l.id:'l'+csId().slice(1); seen.add(id);
  const s=[...new Set((Array.isArray(l.sel)?l.sel:[]).filter(x=>typeof x==='string'&&set.has(x)))]; const dec={}; Object.entries(l.decisions||{}).forEach(([k,v])=>{ if(s.includes(k)&&(v==='factory'||v==='alternate')) dec[k]=v; });
  return {id, target:'nes-classic', name:String(l.name||'Lineup').trim().slice(0,60)||'Lineup', created:l.created||csNow(), modified:l.modified||csNow(), sel:s, decisions:dec, home:(l.home&&typeof l.home==='object')?l.home:null}; }); }
function csFileFor(r, o){ return csWithRecord(r, ()=>{ const m=buildManifest(); m.ccs_collection=csRecordOut(CS.items[r.id]||r, o); return m; }); }
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
  $('savemS').onclick=()=>{ const name=csSafeName(inp.value, ext); const a=document.createElement('a'); a.href=URL.createObjectURL(blob); a.download=name; document.body.appendChild(a); a.click(); a.remove(); setTimeout(()=>URL.revokeObjectURL(a.href),60000); done({ok:true,name,download:true}); }; }); }   // download: handed to the browser's downloads (no path or finished write is known)
window.ccsSaveFile=ccsSaveFile;
// ---- Export Collection ----
async function csExport(id){ id=id||CS.active; const r=CS.items[id]; if(!r) return; if(id===CS.active&&typeof confirmExportWarnings==='function'&&!confirmExportWarnings()) return;
 try{ if(typeof suEnsure==='function') await suEnsure([r]); }catch(e){ toast(`The file was not saved: ${e.message}.`); return; }
 const m=csFileFor(r, {export:true}); const res=await ccsSaveFile(`Classic Cart Studio Collection Backup ${csStamp()}.json`, new Blob([JSON.stringify(m,null,1)],{type:'application/json'}), 'Collection File');
 if(!res.ok) return; if(id===CS.active){ COLL.savedToFile={at:csNow(),fp:collFingerprint()}; saveColl(); updateStatusLine(); }
 toast(`Exported "${r.name}" (${m.count} game${m.count===1?'':'s'}) to ${res.name}`); }
// ---- Import Collection: always a new collection (never overwrites); duplicates get a distinct name ----
function csRecordFromFile(rec, fallbackName){ const ids=[...new Set((rec.sel||[]).filter(id=>typeof id==='string'&&id&&id.length<=128))]; const missing=ids.filter(id=>!byId[id]); const dec={}; Object.entries(rec.decisions||{}).forEach(([k,v])=>{ if(ids.includes(k)&&(v==='factory'||v==='alternate')) dec[k]=v; });
 const coll=csCleanColl(rec.coll, ids); missing.forEach(id=>{ if(!coll.missing[id]) coll.missing[id]={title:id, source:'unknown'}; });
 return {rec:{name:String(rec.name||fallbackName||'Imported collection').slice(0,80), created:rec.created||csNow(), sel:ids, decisions:dec, coll, home:(rec.home&&typeof rec.home==='object')?(typeof suAdoptHome==='function'?suAdoptHome(rec.home, String(rec.name||fallbackName||'')):rec.home):null}, missing}; }   // a startup picture arriving inside is checked and kept apart (startup-custom.js)
// a collection's own fields read from a file (collection file, Studio backup): known fields only, each of its type; kept details through
// csCleanMissing; never another device's personal-games cache or anything else the file carries
function csCleanColl(c, ids){ c=(c&&typeof c==='object'&&!Array.isArray(c))?c:{}; const ob=v=>(v&&typeof v==='object'&&!Array.isArray(v))?v:{}; const idOk=v=>typeof v==='string'&&!!v&&v.length<=128;
 const mark=v=>(v&&typeof v==='object'&&typeof v.at==='string'&&(typeof v.fp==='string'||v.fp===null))?{at:v.at.slice(0,40), fp:v.fp}:null; const o={pref:{}, multi:{}, missing:{}};
 Object.entries(ob(c.pref)).forEach(([k,v])=>{ if(idOk(k)&&idOk(v)) o.pref[k]=v; }); Object.entries(ob(c.multi)).forEach(([k,v])=>{ if(idOk(k)&&v===true) o.multi[k]=true; });
 o.avail=(Array.isArray(c.avail)?c.avail:[]).filter(idOk).slice(0,20000);
 o.placeholders=(Array.isArray(c.placeholders)?c.placeholders:[]).filter(p=>p&&typeof p==='object').slice(0,20000).map(p=>({code:csCleanText(p.code, 40), name:csCleanText(p.name, csLim().title), checked:p.checked===true})).filter(p=>p.code);
 ['created','modified'].forEach(k=>{ if(typeof c[k]==='string') o[k]=c[k].slice(0,40); }); o.savedToFile=mark(c.savedToFile); o.prepared=mark(c.prepared); o.baseline=c.baseline&&typeof c.baseline==='object'&&!Array.isArray(c.baseline)?JSON.parse(JSON.stringify(c.baseline)):null;
 Object.entries(ob(c.missing)).forEach(([k,v])=>{ if(idOk(k)&&ids.includes(k)) o.missing[k]=csCleanMissing(v, k); }); o.missing=csCacheIntoKept(o.missing, c.personalCache, ids);
 const inc=ob(c.included), ig=(Array.isArray(inc.games)?inc.games:[]).filter(x=>typeof x==='string'&&/^[0-9a-f]{64}$/.test(x)).slice(0,100); if(inc.schema==='ccs-included/1'&&ig.length) o.included={schema:'ccs-included/1', games:ig};   /* the Included Games collection's mark (studio/included.js) */
 return o; }
// a legacy Studio backup (nes-classic-studio-data/1 saved the whole working collection) and stores written by 0.7.5 carry the details of
// their selected personal games only in personalCache. Those details (never the cache itself, never other cached games) become the kept
// details of the selected ids: the selected key is the identity, every value goes through csCleanPersonal, file-presence claims are
// ignored, and details the collection already keeps win (the cache only fills what they leave empty)
function csCacheIntoKept(missing, cache, ids){ const out=Object.assign({}, missing||{}); if(!cache||typeof cache!=='object'||Array.isArray(cache)) return out;
 (ids||[]).forEach(id=>{ if(typeof id!=='string'||!/^p_[0-9a-f]{16}$/.test(id)||!Object.prototype.hasOwnProperty.call(cache, id)) return; const e=csFromCache(cache[id], id); if(!e) return;
  const m=out[id]&&typeof out[id]==='object'?csCleanMissing(out[id], id):null; out[id]=m?csKeptFallback(m, e, id):e; }); return out; }
function csFromCache(p, id){ if(!p||typeof p!=='object'||Array.isArray(p)) return null; const pc=csCleanPersonal(p, id), u=pc.user, d=pc.detected||{}; const L=csLim();
 const art=p.user&&typeof p.user==='object'&&p.user.art==='personal/art/'+id+'.jpg'?p.user.art:'';   /* the app's own artwork copy for this id: a reference, never a file */
 return {title:csFirst(id, [u.title, pc.title, d.title], L.title)||String(id).slice(0,120), version:csFirst(id, [u.version_label, pc.version_label], L.version), description:csFirst(id, [u.description], L.description, true),
  year:csFirst(id, [u.year], L.year), publisher:csFirst(id, [u.publisher], L.publisher), source:'personal', content_sha256:pc.content_sha256, file_sha256:pc.file_sha256, payload_sha256:'', size:pc.content_size||0, art:'', thumb_url:art, personal:pc}; }
// kept details m with e as the fallback: m's meaningful text wins (csMergeEntry), and e fills only empty values, also inside personal
function csKeptFallback(m, e, id){ const o=csMergeEntry(m, e, id); ['content_sha256','file_sha256','payload_sha256'].forEach(k=>{ o[k]=m[k]||e[k]||''; }); o.size=m.size||e.size||0; if(m.source==='unknown'&&e.source) o.source=e.source;
 if(m.personal&&e.personal){ const p=Object.assign({}, m.personal), q=e.personal, empty=v=>v==null||v===''||v===0;   /* top-level personal fields: text, or a size where 0 means unknown (csInt||0) */
  Object.keys(q).forEach(k=>{ if(k==='user'||k==='detected') return; if(empty(p[k])) p[k]=q[k]; });
  p.user=Object.assign({}, p.user||{}); const top={title:'title', version_label:'version', description:'description', year:'year', publisher:'publisher'};   /* the collection's own top-level value counts as filled */
  const pe=new Set(csKeptEdits(m)), qe=new Set(csKeptEdits(e));   /* a field its person chose (a blank one too) is filled; one the fallback's person chose comes with its mark */
  Object.entries(q.user||{}).forEach(([k,v])=>{ if(k==='edited'||pe.has(k)||csMeaningful(p.user[k], id)||(top[k]&&csMeaningful(m[top[k]], id))) return; if(qe.has(k)){ p.user[k]=v; pe.add(k); } else if(csMeaningful(v, id)) p.user[k]=v; });
  if(qe.has('art')&&!m.thumb_url&&e.thumb_url) pe.add('art'); if(pe.size) p.user.edited=[...pe].sort();
  pe.forEach(f=>{ const t=top[f]; if(t&&!csMeaningful(m[t], id)) o[t]=p.user[f]||(t==='title'?o[t]:''); });   /* what the person chose (a deliberate clear too) is what the entry shows */
  if(q.detected){ p.detected=Object.assign({}, p.detected||{}); const unset=v=>v==null||v===''; Object.entries(q.detected).forEach(([k,v])=>{ if(unset(p.detected[k])||(k==='emulator'&&p.detected[k]==='unknown')||(k==='compat_confidence'&&p.detected[k]==='none')) p.detected[k]=v; }); }   /* header values: 0 (mapper, submapper, CHR RAM) and false are values; only absent/invalid ones (null) or empty text are filled */
  o.personal=p; }
 return o; }
function csNotCollection(fileName){ if(typeof ccsFail==='function') ccsFail(fileName||'', 'It is not a Classic Cart Studio backup file (.ccs or .json).', 'This file cannot be opened.'); else toast('That file is not a collection file'); }
async function csImportObject(obj, fileName){ if(!obj||typeof obj!=='object'){ csNotCollection(fileName); return 0; }
 if(!Array.isArray(obj)){ const t=ccsTargetResolve(obj,'collection',obj.selections,obj.games,obj.personal_library); if(!t.ok){ if(typeof ccsFail==='function') ccsFail(fileName||'', t.text, 'This file cannot be opened.'); else toast(t.text); return 0; } }   // SNES-GROUNDWORK.md
 if(obj.schema===SBACKUP_SCHEMA||obj.schema==='nes-classic-studio-data/1'){ csRestoreDialog(obj, fileName); return 0; }
 const recs=obj.schema===CBUNDLE_SCHEMA&&Array.isArray(obj.collections)?obj.collections:(obj.ccs_collection&&obj.ccs_collection.schema===CREC_SCHEMA?[obj.ccs_collection]:null);
 if(recs){ let n=0, miss=0, last=null; recs.slice(0,200).forEach(x=>{ if(!x||!Array.isArray(x.sel)) return; const fr=csFileRecords(x, fileName); fr.forEach(({rec,missing},i)=>{ Object.keys(rec.coll.missing).forEach(k=>{ rec.coll.missing[k]=csNoEdits(rec.coll.missing[k]); }); const c=csCreate(rec.name, rec); if(!last&&i===fr.open) last=c; miss+=missing.length; n++; }); });
  if(!last){ csNotCollection(fileName); return 0; }
  csSwitch(last.id, true); toast(`Imported ${n} collection${n===1?'':'s'}${miss?` · ${miss} game${miss===1?'':'s'} ${csAwayWord()}`:''}`); csMenuRefresh(); return n; }
 if(typeof KNOWN_SCHEMAS!=='undefined'&&KNOWN_SCHEMAS.includes(obj.schema)){ const r=csCreate(obj.name||(fileName||'Imported collection').replace(/\.json$/i,'')); csSwitch(r.id, true); const res=await applyManifest(obj, fileName, 'before importing a collection file'); if(res){ COLL.name=r.name; saveColl(); updateStatusLine(); csSaveActiveNow(); } return res?1:0; }
 csNotCollection(fileName); return 0; }
function csImportPick(){ const inp=document.createElement('input'); inp.type='file'; inp.accept='application/json,.json'; inp.onchange=()=>{ const f=inp.files[0]; if(!f) return; f.text().then(t=>{ let o; try{ o=JSON.parse(t); }catch(e){ csNotCollection(f.name); return; } csImportObject(o, f.name); }); }; inp.click(); }
// ---- Save Lists and Settings / Restore Lists and Settings: every collection, library favorites, settings and recovery snapshots ----
function csBackupObject(o){ csSaveActiveNow(); return {schema:SBACKUP_SCHEMA, exported:csNow(), app:'Classic Cart Studio', origin:location.origin, active:CS.active, collections:csList().map(r=>csRecordOut(r, o)), favorites:[...fav], view:(typeof view==='string'?view:'list'), extended:localStorage.getItem(EKEY)||'', welcomed:!!localStorage.getItem(WKEY), snapshots:JSON.parse(localStorage.getItem(SKEY)||'[]')}; }
async function csBackup(){ csSaveActiveNow(); try{ if(typeof suEnsure==='function') await suEnsure(csList()); }catch(e){ toast(`The file was not saved: ${e.message}.`); return; } const d=csBackupObject({export:true});
 if(typeof gsCurrent==='function'&&!(typeof gsApp==='function'&&gsApp())){ try{ d.game_settings=await gsCurrent(); }catch(e){ toast(`The file was not saved. ${e.message}`); return; } }   /* the Game Settings as they are now (the browser player's files): an unread or older copy is never saved as current */ const res=await ccsSaveFile(`Classic Cart Studio Collections Without Game Files ${csStamp()}.json`, new Blob([JSON.stringify(d,null,1)],{type:'application/json'}), 'Collections without game files');
 if(res.ok) toast(`${res.download?'Your browser is saving':'Saved'} ${d.collections.length} collection${d.collections.length===1?'':'s'}, favorites and settings (no game files) ${res.download?'to its downloads as':'to'} ${res.name}`); }
function csRestorePick(){ const inp=document.createElement('input'); inp.type='file'; inp.accept='application/json,.json'; inp.onchange=()=>{ const f=inp.files[0]; if(!f) return; f.text().then(t=>{ let o; try{ o=JSON.parse(t); }catch(e){ toast('That file is not a Classic Cart Studio backup file'); return; } csRestoreDialog(o, f.name); }); }; inp.click(); }
function csRestoreDialog(d, fileName){ const v1=d&&d.schema==='nes-classic-studio-data/1', v2=d&&d.schema===SBACKUP_SCHEMA; if(!v1&&!v2){ if(d&&(d.ccs_collection||(typeof KNOWN_SCHEMAS!=='undefined'&&KNOWN_SCHEMAS.includes(d.schema))||d.schema===CBUNDLE_SCHEMA)){ csImportObject(d, fileName); return; } toast('That file is not a Classic Cart Studio backup file'); return; }
 const cols=v2?(d.collections||[]):((Array.isArray(d.selection)&&d.collection)?[{name:(d.collection||{}).name||'Restored collection', sel:d.selection, decisions:d.decisions||{}, coll:d.collection||{}, home:null}]:[]);
 const have=new Set(Object.keys(CS.items));
 openColl('Restore From an Older Backup File', fileName||'', `<p>This older backup file has your collections, favorites, earlier versions and Game Settings, but no game files. Restoring adds them to what you have. Nothing you have is deleted or replaced: collections you already have are skipped, and changed ones are added as copies.</p>
  <div class="kv"><dt>Backup file made</dt><dd>${esc(nowShort(d.exported)||'unknown')}</dd><dt>Collections</dt><dd>${cols.length}</dd><dt>Favorites</dt><dd>${(d.favorites||[]).length}</dd><dt>Earlier versions</dt><dd>${(d.snapshots||[]).length}</dd></div>
  <div class="acts"><span class="sp"></span><button class="btn" id="rsCancel">Cancel</button><button class="btn hk" id="rsGo">Restore</button></div>`);
 $('rsCancel').onclick=closeColl;
 $('rsGo').onclick=async()=>{ let gbase=null; if(typeof gsCurrent==='function'&&!(typeof gsApp==='function'&&gsApp())){ try{ gbase=await gsCurrent(); }   // the Game Settings as they are now: the restore decides with them (an unread store stops it)
   catch(e){ openColl('Restore From an Older Backup File', fileName||'', `<div class="msgbox bad"><b>Nothing was restored.</b> ${esc(e.message)}</div><div class="acts"><span class="sp"></span><button class="btn" id="rsOk">OK</button></div>`); $('rsOk').onclick=closeColl; return; } }
  const r=csMergeStudioData(d, cols, gbase);
  if(!r.ok){ openColl('Restore From an Older Backup File', fileName||'', `<div class="msgbox bad"><b>Nothing was restored.</b> ${esc(r.error)}.${r.left&&r.left.length?` These could not be put back as they were: ${esc(r.left.join(', '))}. Nothing of yours was removed (a restore only adds, so what it added may still be there), and a copy of your collections from before the restore is kept in this browser.`:' Everything was put back as it was.'}</div><div class="acts"><span class="sp"></span><button class="btn" id="rsOk">OK</button></div>`); $('rsOk').onclick=closeColl; return; }
  const gp=r.settings&&r.settings.writes.length&&typeof gsWebOk==='function'&&gsWebOk()?await gsWebPush(r.settings.writes):{ok:true};   // only the restored choices reach the browser's game player (its files decide), as a Full Studio Backup's do
  closeColl(); const p=r.plan; toast(`Restored ${r.added} collection${r.added===1?'':'s'}${r.skipped?` (${r.skipped} already here)`:''}; ${p.fav.add.length} favorite${p.fav.add.length===1?'':'s'} and ${p.snaps.add.length} earlier version${p.snaps.add.length===1?'':'s'} added${p.view.act==='keep'?'; your library view was kept':''}`);
  if(!gp.ok){ openColl('Restore From an Older Backup File', fileName||'', `<div class="msgbox warn"><b>The file's Game Settings were not restored:</b> this browser could not store them. ${esc(gp.error)} The Game Settings here are as they were; restore the file again to add them.</div><div class="acts"><span class="sp"></span><button class="btn" id="rsOk">OK</button></div>`); $('rsOk').onclick=closeColl; } }; }
// ---- restoring a Studio backup (Restore Lists and Settings, a Full Studio Backup): plan first (read only), then one checked write ----
// The plan compares each backed-up collection by its content with every collection here: the same games, decisions, HOME settings,
// loadouts, its own details (pref, multi, available games, placeholders, baseline) and the kept details of games this Studio cannot
// resolve itself. An equivalent one is skipped; a changed one (even with the same id, name and games) is added as a copy; nothing here is
// replaced. Bookkeeping never makes a copy: ids, times, the saved/prepared marks, a generated "(Copy n)" ending, the HOME's position.
function csCanon(v){ if(Array.isArray(v)) return v.map(csCanon); if(v&&typeof v==='object'){ const o={}; Object.keys(v).sort().forEach(k=>{ if(v[k]!==undefined) o[k]=csCanon(v[k]); }); return o; } return v; }
function csHomeKey(h){ if(!h||typeof h!=='object') return null; const o=Object.assign({}, h); delete o.state; return o; }   // TV.S: where the HOME's cursor was
function csContentKey(rec){ const c=rec.coll||{}; const kept={}; Object.entries(c.missing||{}).forEach(([id,m])=>{ if(!byId[id]&&m&&typeof m==='object'){ const k=csKept(m, id); if(!(k.title===id&&k.source==='unknown'&&!k.personal)) kept[id]=k; } });
 return JSON.stringify(csCanon({name:String(rec.name||'').replace(/\s*\(Copy \d+\)$/i,'').trim().toLowerCase(), sel:[...(rec.sel||[])].sort(), decisions:rec.decisions||{}, home:csHomeKey(rec.home),
  pref:c.pref||{}, multi:c.multi||{}, avail:[...(c.avail||[])].sort(), placeholders:c.placeholders||[], baseline:c.baseline||null, kept})); }
// a record as it stands now, without writing anything (the open collection from the working copy)
function csRecordNow(r){ if(!r||r.id!==CS.active) return r; const n=csFromWorking(r.id, COLL.name); n.created=r.created||n.created; return n; }
// a Studio with nothing of the person's in it yet: no collection, one empty collection, or only the Included Games exactly as Classic Cart
// Studio prepared them (inclPristine: not renamed, no game added, removed or changed, no HOME settings); and no favorites or earlier versions.
// A restore treats such a Studio as new (the backup's view, Extended choice and open collection apply), as before the Included Games came.
function csFresh(){ const l=csList(); let snaps=[]; try{ snaps=JSON.parse(localStorage.getItem(SKEY)||'[]'); }catch(e){ return false; }   /* unread earlier versions: not known to be new */
 if(fav.size||(Array.isArray(snaps)&&snaps.length)) return false; if(!l.length) return true; if(l.length!==1) return false; const r=csRecordNow(l[0]);
 if(typeof inclPristine==='function'&&inclPristine(r)) return true;
 return !(r.sel||[]).length&&!r.home&&!csHomeRead(); }
// a restored collection's kept details for its personal games from a Full Studio Backup's personal records (the key is the identity;
// csFromCache cleans and bounds every value and ignores file-presence claims); the collection's own kept details win and the record fills
// what they leave empty; the backup's own artwork for the game becomes its kept cover. Never put into the Game Library or the personal store.
function csEnrichKept(rec, personal, art){ const miss=rec.coll.missing||{};
 rec.sel.forEach(id=>{ if(!/^p_[0-9a-f]{16}$/.test(id)) return; const p=personal&&typeof personal==='object'&&Object.prototype.hasOwnProperty.call(personal, id)?personal[id]:null; const e=p?csFromCache(p, id):null;
  let m=miss[id]&&typeof miss[id]==='object'?csCleanMissing(miss[id], id):null; if(m&&m.title===String(id).slice(0,120)&&m.source==='unknown'&&!m.personal) m=null;   /* the placeholder csRecordFromFile writes */
  if(e) m=m?csKeptFallback(m, e, id):e;
  const a=art&&typeof art==='object'&&Object.prototype.hasOwnProperty.call(art, id)?csThumbOk(art[id]):''; if(m&&a&&!m.thumb_url) m.thumb_url=a;
  if(m) miss[id]=m; });
 rec.coll.missing=miss; return rec; }
// what a restore would do (nothing is written): {add:[{rec, name, wasActive}], skip:[{name, as}], fav, snaps, view, ext, open, fresh}
function csMergePlan(d, opt){ opt=opt||{}; const cols=(opt.cols||d.collections||[]); const fresh=csFresh(); const keys=new Map(); const files=[];   /* each file record, plus a collection for each distinct lineup an earlier version's backup carried */
 cols.slice(0,2000).forEach(x=>{ if(!x||typeof x!=='object'||!Array.isArray(x.sel)) return; const fr=csFileRecords(x); fr.forEach((f,i)=>files.push({x, f, lineup:i>0, opened:i===fr.open})); });
 Object.values(CS.items).forEach(r=>{ const n=csRecordNow(r); keys.set(csContentKey(typeof suQuiet==='function'?suQuiet(()=>csRecordFromFile(csRecordOut(n), n.name).rec):csRecordFromFile(csRecordOut(n), n.name).rec), r.name); });
 const taken=new Set(csList().map(r=>r.name.toLowerCase())); const add=[], skip=[]; let actAs=null;
 files.forEach(({x, f, lineup, opened})=>{ let rec=f.rec; if(opt.personal||opt.art) rec=csEnrichKept(rec, opt.personal, opt.art); const was=opened&&!!(x.id&&x.id===d.active);
  const k=csContentKey(rec); if(keys.has(k)){ skip.push({name:rec.name, as:keys.get(k)}); if(was) actAs=keys.get(k); return; }
  const name=csCopyName(rec.name, taken, 72); taken.add(name.toLowerCase()); keys.set(k, name); add.push({rec, name, wasActive:was, lineup}); });
 const listed=new Set(); add.forEach(a=>a.rec.sel.forEach(id=>listed.add(id))); Object.values(CS.items).forEach(r=>(r.sel||[]).forEach(id=>listed.add(id)));
 const fv=[...new Set((Array.isArray(d.favorites)?d.favorites:[]).filter(id=>typeof id==='string'&&id.length<=128))].filter(id=>!fav.has(id));
 const favAdd=fv.filter(id=>byId[id]||listed.has(id)), favOut=fv.length-favAdd.length;
 let snaps=[]; try{ snaps=JSON.parse(localStorage.getItem(SKEY)||'[]'); if(!Array.isArray(snaps)) snaps=[]; }catch(e){ snaps=[]; } const at=new Set(snaps.map(s=>s&&s.at));
 const inSn=(Array.isArray(d.snapshots)?d.snapshots:[]).filter(s=>s&&typeof s==='object'&&typeof s.at==='string'&&!at.has(s.at)).sort((a,b)=>a.at<b.at?1:-1); const room=Math.max(0, 30-snaps.length);
 const want=d.view==='grid'||d.view==='list'?d.view:null; const vAct=!want||want===view?'same':(fresh||opt.replace)?'apply':'keep';
 let curExt=''; try{ curExt=localStorage.getItem(EKEY)||''; }catch(e){} const ew=typeof d.extended==='string'?d.extended.slice(0,8):''; const eAct=!ew||ew===curExt?'same':(fresh||opt.replace)?'apply':'keep';
 const act=add.find(a=>a.wasActive); const open=fresh?(act?act.name:actAs):null;
 return {add, skip, fav:{add:favAdd, out:favOut}, snaps:{have:snaps, add:inSn.slice(0, room), out:Math.max(0, inSn.length-room)}, view:{want, act:vAct}, ext:{want:ew, act:eAct}, open, welcomed:!!d.welcomed, fresh}; }
// one checked write to this browser's (or the app's) storage: every new value is written and read back; on any failure every key written
// so far gets its earlier value back, each read back; a key that cannot be put back is named (never "nothing changed" when it is not so)
const CS_TXN_NAMES={[CSKEY]:'collections', [FKEY]:'favorites', [VKEY]:'library view', [SKEY]:'earlier versions', [EKEY]:'Extended library choice', [WKEY]:'welcome choice', 'ccs-game-settings-v1':'Game Settings'};
function csTxn(){ const before=new Map(), done=[]; const get=k=>{ try{ return localStorage.getItem(k); }catch(e){ return undefined; } };
 return {
  set(k, v){ if(!before.has(k)) before.set(k, get(k)); done.push(k); localStorage.setItem(k, v); if(get(k)!==v) throw new Error(`this browser did not keep the ${CS_TXN_NAMES[k]||k}`); },
  keys(){ return [...new Set(done)]; },
  rollback(){ const left=[]; [...new Set(done)].reverse().forEach(k=>{ const v=before.get(k); if(v===undefined){ left.push(CS_TXN_NAMES[k]||k); return; }   /* judged by what the storage holds afterwards */
   try{ if(v===null) localStorage.removeItem(k); else localStorage.setItem(k, v); }catch(e){} if(get(k)!==v) left.push(CS_TXN_NAMES[k]||k); }); if(!left.length) done.length=0; return left; } }; }
// the plan written (storage only; the page's own state is adopted by csAdopt once every part of the restore succeeded)
function csApplyPlan(plan, tx, extra){ csSaveActiveNow(); const now=csNow(); const next=JSON.parse(JSON.stringify(CS)); const ids=[];
 try{ localStorage.setItem(CSKEY+'.before-restore', JSON.stringify({at:now, store:CS, favorites:[...fav]})); }catch(e){}   // an extra copy of what was here (when there is room); the transaction itself puts everything back
 plan.add.forEach(a=>{ let id=csId(); while(next.items[id]) id=csId(); next.items[id]=Object.assign({sel:[],decisions:{},coll:{},home:null}, JSON.parse(JSON.stringify(a.rec)), {id, name:a.name, created:a.rec.created||now, modified:now, shares:[]}); ids.push(id); a.id=id; });
 next.model=CS_MODEL; if(next.starter&&next.starter.state!=='done') next.starter={state:'done', at:now, by:'restore'};   /* a restored Studio is the person's own: the Included Games are not added to it later */
 tx.set(CSKEY, JSON.stringify(next));
 const nf=new Set([...fav, ...plan.fav.add]); if(plan.fav.add.length) tx.set(FKEY, JSON.stringify([...nf]));
 if(plan.snaps.add.length) tx.set(SKEY, JSON.stringify(plan.snaps.have.concat(plan.snaps.add).sort((a,b)=>a.at<b.at?1:-1)));
 const nv=plan.view.act==='apply'?plan.view.want:view; if(plan.view.act==='apply') tx.set(VKEY, nv);
 if(plan.ext.act==='apply') tx.set(EKEY, plan.ext.want); if(plan.welcomed) tx.set(WKEY, '1');
 if(extra) extra(tx);
 return {next, fav:nf, view:nv, ids}; }
// the page takes the written state as its own (the open collection stays open unless a new Studio opens the backup's own)
function csAdopt(plan, res){ CS=res.next; fav.clear(); res.fav.forEach(x=>fav.add(x)); if(view!==res.view){ view=res.view; try{ $('vList').classList.toggle('active', view==='list'); $('vGrid').classList.toggle('active', view==='grid'); }catch(e){} }
 if(plan.ext.act==='apply'&&typeof setExt==='function'){ try{ setExt(plan.ext.want==='1'); }catch(e){} }
 let opened=null; const a=(plan.open&&(plan.add.find(x=>x.name===plan.open&&x.id)||Object.values(CS.items).find(x=>x.name===plan.open)))||(!csActive()&&plan.add.find(x=>x.id));   /* with no collection open, the first restored one opens */
 if(a){ try{ csSwitch(a.id, true); opened=a.name; }catch(e){ opened=null; } }
 try{ if(typeof desktopStateDirty==='function') desktopStateDirty(); }catch(e){}
 render(); csMenuRefresh(); return opened; }
// the earlier state back in the page after a rolled-back restore
function csUnadopt(old){ CS=old.CS; fav.clear(); old.fav.forEach(x=>fav.add(x)); view=old.view; try{ if(typeof desktopStateDirty==='function') desktopStateDirty(); }catch(e){} render(); csMenuRefresh(); }
// Restore Lists and Settings (JSON): the same plan and checked write, with the file's Game Settings on the website
function csMergeStudioData(d, cols, gbase){ const plan=csMergePlan(d, {cols}); const old={CS, fav:new Set(fav), view}; const tx=csTxn(); const gs=(d.game_settings&&typeof fbSettingsPlan==='function'&&!(typeof gsApp==='function'&&gsApp()))?fbSettingsPlan({web:{game_settings:d.game_settings}, platform:'web'}):null; let gres=null;
 try{ const res=csApplyPlan(plan, tx, gs?(t=>{ gres=fbSettingsWrite(gs, false, t, gbase); }):null); csAdopt(plan, res); return {ok:true, added:plan.add.length, skipped:plan.skip.length, plan, settings:gres}; }
 catch(e){ const left=tx.rollback(); csUnadopt(old); return {ok:false, error:String(e&&e.message||e), left, added:0, skipped:0, plan}; } }
// ---- the My Collections menu (quick switch) and the manager ----
// the collection menu: the open collection first (checked), then up to seven others to switch to; Manage Collections lists every one
// (the bounded list never limits what is stored). Each row is a name and a game count, nothing more.
const csGames=n=>`${n.toLocaleString('en-US')} game${n===1?'':'s'}`;
function csMenuRefresh(){ const box=$('csQuick'); if(!box||!CS) return; const list=csList(); const others=list.filter(r=>r.id!==CS.active); const shown=others.slice(0,7); const cn=sel.size;
 const cur=csActive(), num=v=>v.toLocaleString('en-US');
 box.innerHTML=(cur?miHtml({label:esc(cur.name), pill:num(cn), cls:'cur', role:'menuitemradio', attrs:'aria-checked="true" data-cs-cur="1"'}):'')
  +shown.map(r=>miHtml({label:esc(r.name), pill:num((r.sel||[]).length), role:'menuitemradio', attrs:`aria-checked="false" data-cs="${r.id}"`})).join('')
  +(others.length>shown.length?miHtml({label:`${others.length-shown.length} More…`, cls:'more', attrs:'data-cs-all="1"'}):'')
  +(!list.length?`<p class="small mempty">${CS_STATE==='ok'?'You have no collections.':'Your collections could not be read.'}</p>`:'');
 ['mRename','mDup','mDel','mShare'].forEach(k=>{ const b=$(k); if(b) b.hidden=!cur; }); const nm=$('collName'); if(nm) nm.textContent=cur?cur.name:(CS_STATE==='ok'?'No Collection':'Collections Unavailable');
 const t=$('csTotal'); if(t) t.textContent=`${list.length.toLocaleString('en-US')} collection${list.length===1?'':'s'}`;
 const h=$('collNameHead'), a=csActive(); if(h&&a) h.textContent=a.name; const g=$('collGames'); if(g) g.textContent=cn?csGames(cn):'No games yet';
 const c=$('collCount'); if(c) c.textContent=csGames(cn);
 const sh=$('mShare'); if(sh){ sh.disabled=!cn; const d=sh.querySelector('.d'); if(d) d.textContent=cn?'A complete .ccs file, every game with its game file':'Add games to this collection first'; } }
// a card's ⋯ menu (Manage Collections and Manage Loadouts): the less frequent actions, destructive last
function rowMore(name){ return `<details class="csmore"><summary class="btn sm" aria-label="More actions for ${esc(name)}" title="More actions"><span aria-hidden="true">⋯</span></summary><div class="csmenu" role="menu">${miHtml({ic:'✏️', label:'Rename…', attrs:'data-a="rename"'})}${miHtml({ic:'📑', label:'Duplicate', attrs:'data-a="dup"'})}<div class="sep" role="separator"></div>${miHtml({ic:'🗑️', label:'Delete…', cls:'danger', attrs:'data-a="del"'})}</div></details>`; }
// a manager redrawn after a row's action (Rename, Duplicate, Delete, or their Cancel) puts focus back on that row's ⋯ (when the row still exists)
let ROW_RET=null;
function rowMoreWire(box){ box.querySelectorAll('details.csmore').forEach(d=>d.addEventListener('toggle',()=>{ if(d.open){ box.querySelectorAll('details.csmore[open]').forEach(o=>{ if(o!==d) o.open=false; }); rowMorePlace(d); const f=d.querySelector('.mi'); if(f) f.focus({preventScroll:true}); } }));
 if(ROW_RET){ const r=box.querySelector(ROW_RET); ROW_RET=null; if(r){ r.focus({preventScroll:true}); r.scrollIntoView({block:'nearest'}); } } }   // one ⋯ menu at a time
function csManager(){ if(CS_STATE!=='ok'){ if(typeof rdyStoreProblem==='function') rdyStoreProblem(); return; } csSaveActiveNow(); const list=csList(); const where=CO&&CO.desktop?'on this computer':'in this browser';
 openColl('Manage Collections', `${list.length.toLocaleString('en-US')} collection${list.length===1?'':'s'} · saved automatically ${where}`, `<div class="dquick">${miHtml({ic:'✨', label:'New Collection…', cls:'new', attrs:'id="csNewB"'})}${miHtml({ic:'📂', label:'Restore From a Backup File…', attrs:'id="csImpB"'})}</div>
  <div class="dlinks">${miHtml({ic:'🕘', label:'Go Back to an Earlier Version…', attrs:'id="csRestB"'})}${CO&&(CO.desktop||CO.served)?miHtml({ic:'🖥️', label:'Games Already on This Computer…', attrs:'id="csLocalB"'}):''}</div>
  <div class="cslist" role="list">${list.map(r=>{ const on=r.id===CS.active; const n=on?sel.size:(r.sel||[]).length; return `<div class="csrow${on?' on':''}" role="listitem" data-id="${r.id}"><span class="mic" aria-hidden="true">${on?'🎮':'🕹️'}</span><div class="csn"><b>${esc(r.name)}</b><span class="small">${csGames(n)} · changed ${esc(nowShort(r.modified))}</span></div>${on?'<span class="opentag"><span aria-hidden="true">✓</span> Open</span>':''}
   <div class="csacts">${on?'':`<button class="btn sm hk" data-a="open" aria-label="Open ${esc(r.name)}">Open</button>`}<button class="btn sm grow" data-a="share"><span aria-hidden="true">💾</span> Back Up…</button>${rowMore(r.name)}</div></div>`; }).join('')}</div>
  ${list.length?'':'<p class="small">You have no collections. Create one, or restore a backup.</p>'}<p class="small">Collections are kept only ${where}. To keep a copy, use <b>Back Up This Collection…</b> in the collection menu or <b>Back Up All Collections…</b> in the Studio menu${CO&&!CO.desktop&&!CO.file?', or <b>Open or Install the Windows App</b>':''}.</p>`);
 $('csNewB').onclick=()=>csNewDialog(); $('csImpB').onclick=()=>{ closeColl(); csImportPick(); }; $('csRestB').onclick=()=>restoreMenu(); if($('csLocalB')) $('csLocalB').onclick=()=>loadLocalLibrary();
 rowMoreWire($('collBody'));
 $('collBody').onclick=e=>{ const b=e.target.closest('[data-a]'); if(!b) return; const id=b.closest('.csrow').dataset.id; const r=CS.items[id]; if(!r) return; const a=b.dataset.a; if(['rename','dup','del'].includes(a)) ROW_RET=`.csrow[data-id="${id}"] details.csmore>summary`;
  if(a==='open'){ csSwitch(id); csManager(); }
  else if(a==='rename'){ csNameDialog('Rename Collection', r.name, 'Rename', nm=>{ csRename(id, nm); csManager(); }, '', true); }
  else if(a==='dup'){ csDuplicate(id); csManager(); }
  else if(a==='export'){ csExport(id); }
  else if(a==='share'){ closeColl(); ccsBackupDialog(id); }
  else if(a==='del'){ csDeleteDialog(id, true); } }; }
// Remove a collection: only the collection goes; its games' files, saved games, Game Settings and the other collections stay
function csDeleteDialog(id, back){ const r=CS.items[id]; if(!r) return; const c=id===CS.active?sel.size:(r.sel||[]).length; const last=csList().length===1;
 csNameDialog(`Remove "${r.name}"?`, null, 'Remove', ()=>{ csDelete(id); if(back) csManager(); else closeColl(); }, `<p class="small">"${esc(r.name)}" (${csGames(c)}) will be removed.${c?' <b>Go Back to an Earlier Version</b> can bring it back for a while.':''} The game files, saved games and Game Settings stay, and so do your other collections.${last?' It is your only collection: afterwards you can create a new one, add game files or restore a backup.':''}</p>`, back); }
function csNameDialog(title, value, action, then, html, back){ openColl(title,'', `${html||''}${value!==null?`<div class="fld"><label for="csNm">Collection name</label><input type="text" id="csNm" maxlength="80" value="${esc(value||'')}"></div>`:''}<div class="acts"><span class="sp"></span><button class="btn" id="csNo" data-cancel>Cancel</button><button class="btn ${action==='Delete'||action==='Remove'?'danger':'hk'}" id="csYes">${esc(action)}</button></div>`);
 const inp=$('csNm'); if(inp){ if(!TOUCH||title.startsWith('Rename')){ inp.focus(); inp.select(); } inp.onkeydown=e=>{ if(e.key==='Enter') $('csYes').click(); }; }
 $('csNo').onclick=()=>{ if(back) csManager(); else closeColl(); };
 $('csYes').onclick=()=>{ const v=inp?inp.value.trim():''; if(inp&&!v){ inp.focus(); return; } then(v); }; }
function csNewDialog(){ if(CS_STATE!=='ok'){ if(typeof rdyStoreProblem==='function') rdyStoreProblem(); return; } const cur=csActive(); csNameDialog('New Collection', csUniqueName('New Collection'), 'Create', nm=>{ csNew(nm); closeColl(); }, `<p class="small">Opens a new, empty collection${cur?`; "${esc(cur.name)}" stays as it is`:''}. Then choose <b>Add Game Files</b> to add games to it, or start from a copy with <b>Duplicate</b>.</p>`); }
// ---- transfer to and from Classic Cart Studio for Windows ----
function csBundle(ids, o){ csSaveActiveNow(); return {schema:CBUNDLE_SCHEMA, exported:csNow(), app:'Classic Cart Studio', collections:ids.map(id=>CS.items[id]).filter(Boolean).map(r=>csRecordOut(r, o))}; }
function csTransferPick(then){ csSaveActiveNow(); const list=csList();
 openColl('Open or Install the Windows App','', `<p class="small">Choose collections to copy into the Windows app. They arrive as new collections; nothing there is overwritten or kept in sync.</p>
  <div class="cspick">${list.map(r=>`<label><input type="checkbox" value="${r.id}" ${r.id===CS.active?'checked':''}> <b>${esc(r.name)}</b> <span class="small">${(c=>`${c} game${c===1?'':'s'}`)(r.id===CS.active?csCollSel().size:(r.sel||[]).length)}</span></label>`).join('')}</div>
  <div class="acts"><button class="btn ghost" id="cpAll">Select All</button><span class="sp"></span><button class="btn" id="cpNo">Cancel</button><button class="btn hk" id="cpGo">Continue</button></div>`);
 $('cpAll').onclick=()=>document.querySelectorAll('.cspick input').forEach(x=>x.checked=true); $('cpNo').onclick=closeColl;
 $('cpGo').onclick=()=>{ const ids=[...document.querySelectorAll('.cspick input:checked')].map(x=>x.value); if(!ids.length){ toast('Choose at least one collection'); return; } then(ids); }; }
async function csToWebsite(){ csSaveActiveNow(); const ids=csList().map(r=>r.id); try{ if(typeof suEnsure==='function') await suEnsure(csList()); }catch(e){ toast(`The file was not saved: ${e.message}.`); return; } const res=await ccsSaveFile(`Classic Cart Studio Collections ${csStamp()}.json`, new Blob([JSON.stringify(csBundle(ids, {export:true}),null,1)],{type:'application/json'}), 'Collections File');
 if(!res.ok) return; openColl('Open Your Collections on the Website','', `<p class="small">Saved ${ids.length} collection${ids.length===1?'':'s'} to <b>${esc(res.name)}</b>. On <b>ClassicCartStudio.com</b>, choose <b>My Collections → Restore From a Backup File…</b> and pick that file; they arrive as new collections.</p><div class="acts"><span class="sp"></span><a class="btn hk" href="https://classiccartstudio.com/preview/" target="_blank" rel="noopener">Open ClassicCartStudio.com</a><button class="btn" id="twOk">Done</button></div>`); $('twOk').onclick=closeColl; }
// ---- start ----
csInit();
// the app's durable copy may arrive after start (a new WebView2 profile): adopt it, or bring the restored single collection into the store
if(typeof desktopApplyState==='function'){ const _da=desktopApplyState; desktopApplyState=function(ls){ const r=_da.apply(this,arguments); try{ const st=csReadState();
  if(ls&&ls[CSKEY]&&st.state==='ok'){ CS_STATE='ok'; CS_FRESH=false; CS=st.store; csMigrateStore(); if(csActive()) csLoadWorking(csActive()); else { const l=csList(); if(l.length){ CS.active=l[0].id; csWrite(); csLoadWorking(CS.items[CS.active]); } else { csEmptyWorking(); render(); updateStatusLine(); } } }
  else if(CS&&csActive()&&!(csActive().sel||[]).length&&sel.size){ csSaveActiveNow(); }
  csMenuRefresh(); try{ if(typeof rdyChanged==='function') rdyChanged(); }catch(e){} }catch(e){} return r; }; }
// the store's active collection is authoritative for the working copy when the two disagree (a store written by another tab or restored)
(function(){ const r=csActive(); if(r&&JSON.stringify([...sel].sort())!==JSON.stringify((r.sel||[]).filter(id=>byId[id]).slice().sort())&&r.modified&&COLL.modified&&r.modified>COLL.modified) csLoadWorking(r); })();
// another tab wrote the collections: this tab takes them as they are (an unreadable value is left alone; this tab writes nothing over it)
addEventListener('storage',e=>{ if(e.key!==CSKEY||!e.newValue||CS_STATE!=='ok') return; const s=csRead(); if(!s) return; clearTimeout(CS_T); CS_T=null; const switched=!CS||s.active!==CS.active; CS=s; CS_FRESH=false;
 if(switched){ if(csActive()) csLoadWorking(csActive()); else { csEmptyWorking(); render(); updateStatusLine(); } } else if(csActive()&&JSON.stringify([...sel].sort())!==JSON.stringify((csActive().sel||[]).slice().sort())) csLoadWorking(csActive());
 csMenuRefresh(); try{ if(typeof rdyChanged==='function') rdyChanged(); }catch(e){} });
// Restore Previous Version adds the earlier state as its own collection: restoring never overwrites the open collection (or any other)
restoreSnapshot=function(i){ const snaps=JSON.parse(localStorage.getItem(SKEY)||'[]'); const s=snaps[i]; if(!s) return; const known=(s.sel||[]).slice(); const dec=Object.assign({}, s.decisions||{});
 const r=csCreate(s.name||'Restored collection', {sel:known, decisions:dec, coll:{pref:s.pref||{}, multi:s.multi||{}, avail:s.avail||[], baseline:s.baseline||null, missing:s.missing||{}}, home:null}); csSwitch(r.id, true); closeColl();
 toast(`Restored "${s.name}" from ${nowShort(s.at)} as "${r.name}" (${known.length} game${known.length===1?'':'s'})`); };

// ---- every entry stays: games this computer cannot resolve (not in this Game Library, or a personal game whose file is not here) keep
//      their identity and description in the collection's `missing` list; they are counted, listed, exported, transferred and shared like
//      any other entry, and become ordinary entries as soon as the matching file is added (personal ids come from the file's content hash)
// descriptive details from packages and collection files are untrusted text: control/formatting characters removed, each field bounded;
// automatic values (a game id, a ROM file name, "Personal game") never count as a description (same rule as ccs_package.generic_text)
var CS_META_LIM;   // a hoisted var set on first use: the start-up render cleans kept details (keptRow) before this line runs
function csLim(){ return CS_META_LIM=CS_META_LIM||{title:120, version:160, description:4000, year:8, publisher:80}; }
function csMeaningful(v, id){ const s=String(v==null?'':v).trim(); return !!s&&s!==id&&!/^(p_)?[0-9a-f]{16}(\.(nes|unh|bin|fds|zip))?$/i.test(s)&&!/\.(nes|unh|bin|fds|zip)$/i.test(s)&&!/^(personal game|unknown)$/i.test(s); }
function csCleanText(v, n, multi){ if(v==null||typeof v==='object'||typeof v==='boolean') return ''; const s=String(v).replace(/\r\n/g,'\n').replace(/\t/g,' ');
 return (multi?s.replace(/[\u0000-\u0009\u000b-\u001f\u007f-\u009f​-‏‪-‮⁠-⁯﻿]/g,''):s.replace(/[\u0000-\u001f\u007f-\u009f​-‏‪-‮⁠-⁯﻿]/g,'')).trim().slice(0,n).trim(); }
function csFirst(id, vals, n, multi){ for(const v of vals){ const c=csCleanText(v, n, multi); if(csMeaningful(c, id)) return c; } return ''; }
// artwork a kept entry may point to: an https cover, a package's own JPEG/PNG thumbnail (data URL, website) or the copy the app keeps in personal/art
function csThumbOk(u){ u=String(u||''); return (/^https:\/\/[^\s"'<>]+$/.test(u)&&u.length<2048)||(/^data:image\/(jpeg|png);base64,[A-Za-z0-9+/=]+$/.test(u)&&u.length<=1400000)||/^personal\/art\/p_[0-9a-f]{16}\.jpg$/.test(u)?u:''; }
function csEntryFromSelection(s, plib){ const id=s.version_id||s.game_id; const p=(plib||[]).find(x=>x&&x.id===id)||null; const pu=(p&&p.user&&typeof p.user==='object')?p.user:{};
 const src=s.source==='personal'||/^p_/.test(id||'')?'personal':s.source==='extended'?'extended':s.source==='catalog'?'catalog':(s.source||'unknown');
 const o={title:csFirst(id, [s.display_title, s.title, pu.title, p&&p.title], csLim().title)||String(id).slice(0,120), version:csFirst(id, [s.version_label, s.nointro_name, s.version, pu.version_label, p&&p.version_label], csLim().version),
  description:csFirst(id, [s.description, pu.description], csLim().description, true), year:csFirst(id, [s.year, pu.year], csLim().year), publisher:csFirst(id, [s.publisher, pu.publisher], csLim().publisher), source:src,
  content_sha256:/^[0-9a-f]{64}$/.test((p&&p.content_sha256)||s.content_sha256||'')?((p&&p.content_sha256)||s.content_sha256):'', file_sha256:/^[0-9a-f]{64}$/.test((p&&p.file_sha256)||s.file_sha256||'')?((p&&p.file_sha256)||s.file_sha256):'',
  payload_sha256:/^[0-9a-f]{64}$/.test(s.payload_sha256||'')?s.payload_sha256:'', size:Number((p&&p.content_size)||s.size||0)||0, art:/^art\/[0-9a-f]{16}\.jpg$/.test(s.art||'')?s.art:'', thumb_url:/^https:\/\//.test(s.thumb_url||'')?csThumbOk(s.thumb_url):''};
 if(p) o.personal=csCleanPersonal(p, id);
 return o; }
// the personal-game record a kept entry carries for packages and collection files (portable fields only; never a path). user.edited:
// the fields its person chose (a blank one a deliberate clear, D-80), kept only when strictly valid (csEdits)
function csCleanPersonal(p, id){ const u=(p.user&&typeof p.user==='object')?p.user:{}; const hx=v=>/^[0-9a-f]{64}$/.test(v||'')?v:''; const num=v=>csInt(v, 4e9)||0;
 const o={id, title:csCleanText(p.title, csLim().title), version_label:csCleanText(p.version_label, csLim().version), content_sha256:hx(p.content_sha256), file_sha256:hx(p.file_sha256), content_size:num(p.content_size), file_size:num(p.file_size),
  format:csCleanText(p.format, 16), file_name:csCleanText(String(p.file_name||'').split(/[\\/]/).pop(), 200), note:csCleanText(p.note, 200), content_crc32:/^[0-9A-Fa-f]{8}$/.test(p.content_crc32||'')?String(p.content_crc32).toUpperCase():'',
  user:{title:csCleanText(u.title, csLim().title), version_label:csCleanText(u.version_label, csLim().version), description:csCleanText(u.description, csLim().description, true), year:csCleanText(u.year, csLim().year), publisher:csCleanText(u.publisher, csLim().publisher)}};
 const ed=csEdits(u); if(ed) o.user.edited=ed;
 const d=csCleanDetected(p.detected); if(d) o.detected=d; return o; }
// the person's own edits a personal record names (user.edited; personal.portable_edits checks the same): distinct names of the editable
// fields, each edited text field's value text or absent. Anything else and the edits are not used (the details still are, as automatic ones)
function csEdits(u){ const F=['title','version_label','description','year','publisher','art'], e=u&&u.edited;   /* a local list: the start-up render can reach this before a top-level const runs */
 if(!Array.isArray(e)||!e.length||e.length>F.length||new Set(e).size!==e.length||!e.every(x=>typeof x==='string'&&F.includes(x))) return null;
 return e.some(f=>f!=='art'&&u[f]!=null&&typeof u[f]!=='string')?null:[...e].sort(); }
function csKeptEdits(x){ const u=x&&x.personal&&x.personal.user; return u&&Array.isArray(u.edited)?u.edited:[]; }
// a kept entry from an ordinary package or collection file: its details only, never another Studio's record of what its person edited
// (only a Studio backup restores those: Restore Lists and Settings, Restore Full Studio Backup)
function csNoEdits(m){ if(!csKeptEdits(m).length) return m; const u=Object.assign({}, m.personal.user); delete u.edited; return Object.assign({}, m, {personal:Object.assign({}, m.personal, {user:u})}); }
// a whole non-negative number (a number, or a string of digits) up to max; anything else is unknown (null)
function csInt(v, max){ const n=typeof v==='number'?v:(typeof v==='string'&&/^\s*\d{1,13}\s*$/.test(v)?Number(v):NaN); return Number.isSafeInteger(n)&&n>=0&&n<=max?n:null; }
// what the app read from a personal game's iNES / NES 2.0 header and its compatibility rules (personal.py): header numbers within their
// ranges, fixed vocabularies, bounded text; anything else becomes unknown (never shown as it came)
function csCleanDetected(d){ if(!d||typeof d!=='object'||Array.isArray(d)) return null; const one=(v, list, dflt)=>list.includes(v)?v:dflt;
 const o={title:csCleanText(d.title, csLim().title), mapper:csInt(d.mapper, 4095), submapper:csInt(d.submapper, 15), prg:csInt(d.prg, 1e9), chr:csInt(d.chr, 1e9), mirroring:one(d.mirroring, ['horizontal','vertical','four-screen'], ''),
  header_hex:/^[0-9a-f]{32}$/.test(typeof d.header_hex==='string'?d.header_hex:'')?d.header_hex:'', emulator:one(d.emulator, ['kachikachi','RetroArch','unknown'], 'unknown'), core:csCleanText(d.core, 40), launch_command:csCleanText(d.launch_command, 400),
  compat_notes:csCleanText(d.compat_notes, 400), compat_confidence:one(d.compat_confidence, ['none','low','medium','high'], 'none')};
 if(typeof d.battery==='boolean') o.battery=d.battery; if(typeof d.trainer==='boolean') o.trainer=d.trainer; return o; }
// a kept entry read from a package or collection file: known fields only, bounded and checked
function csCleanMissing(m, id){ m=(m&&typeof m==='object')?m:{}; const hx=v=>/^[0-9a-f]{64}$/.test(v||'')?v:'';
 const o={title:csCleanText(m.title, csLim().title)||String(id).slice(0,120), version:csCleanText(m.version, csLim().version), description:csCleanText(m.description, csLim().description, true), year:csCleanText(m.year, csLim().year), publisher:csCleanText(m.publisher, csLim().publisher),
  source:['catalog','extended','personal','unknown'].includes(m.source)?m.source:(/^p_/.test(id)?'personal':'unknown'), content_sha256:hx(m.content_sha256), file_sha256:hx(m.file_sha256), payload_sha256:hx(m.payload_sha256), size:csInt(m.size, 4e9)||0, art:/^art\/[0-9a-f]{16}\.jpg$/.test(m.art||'')?csArtAlias(m.art):'', thumb_url:csThumbOk(m.thumb_url)};
 if(m.personal&&typeof m.personal==='object') o.personal=csCleanPersonal(m.personal, id);
 return o; }
// a package's (or collection's) entry and the selection describing the same game: field by field, the first real description wins
function csMergeEntry(m, e, id){ if(!m) return e; if(!e) return m; const o=Object.assign({}, e, m); ['title','version','description','year','publisher'].forEach(k=>{ o[k]=csMeaningful(m[k], id)?m[k]:(csMeaningful(e[k], id)?e[k]:(m[k]||e[k]||'')); }); if(!csMeaningful(o.title, id)) o.title=String(id).slice(0,120); o.thumb_url=m.thumb_url||e.thumb_url||''; o.art=m.art||e.art||''; if(!o.personal&&e.personal) o.personal=e.personal; return o; }
// personal games whose ROM file is on this computer keep their details in the app's personal store: a collection's kept details and
// artwork are merged into it there (personal.merge_metadata: what the person edited and real local details always win). The kept entry
// stays in the collection (pending) until the store confirms the merge: a refused, failed or interrupted request keeps it through
// autosaves, switching collections and restarts, and the next reconcile (every personal-games refresh, e.g. when the app connects or
// after an import) sends it again. Only a request in flight is not sent twice; nothing is remembered as "done" without confirmation.
function csHasDetails(x, id){ return !!x&&(['title','version','description','year','publisher'].some(k=>csMeaningful(x[k], id))||!!x.thumb_url||csKeptEdits(x).length>0); }
function csPendingPersonal(id, x){ return /^p_[0-9a-f]{16}$/.test(id||'')&&csHasDetails(x, id); }
function csMetaKey(id, x){ return id+'|'+JSON.stringify([x.title||'', x.version||'', x.description||'', x.year||'', x.publisher||'', x.thumb_url||'', csKeptEdits(x)]); }
// the store confirmed this exact entry: drop it from every collection that still holds the same details (others stay pending)
function csSettlePersonal(id, key){ let n=0; const drop=m=>{ if(m&&m[id]&&typeof m[id]==='object'&&csMetaKey(id, csKept(m[id], id))===key){ delete m[id]; n++; } };
 drop(COLL.missing); Object.values((typeof CS!=='undefined'&&CS.items)||{}).forEach(r=>drop(r&&r.coll&&r.coll.missing)); if(n){ saveColl(); csWrite(); } }
const CS_INFLIGHT=new Map();   // key -> the request's promise (a second call for the same details waits for it instead of sending again)
async function csMergePersonal(list){ const res={sent:0, ok:0, changed:0, failed:[]}; if(!(typeof COMPANION!=='undefined'&&COMPANION.ok)||typeof hkPost!=='function') return res;
 for(const [id, x] of list){ if(!/^p_[0-9a-f]{16}$/.test(id)||!PERSONAL[id]||!x) continue; const key=csMetaKey(id, x);
  let p=CS_INFLIGHT.get(key);
  if(!p){ const art=/^data:image\/(jpeg|png);base64,/.test(x.thumb_url||'')?x.thumb_url:''; res.sent++; const ed=csKeptEdits(x), u=(x.personal&&x.personal.user)||{};
   const body={id, meta:{title:x.title, version_label:x.version, description:x.description, year:x.year, publisher:x.publisher}, art}; if(ed.length){ body.edited=ed; body.user={}; ed.forEach(f=>{ if(f!=='art') body.user[f]=u[f]||''; }); }   /* a Studio backup's own edits (D-80) */
   p=(async()=>{ try{ return await hkPost('/api/personal/merge', body); }catch(e){ return {ok:false, error:String(e)}; } })();
   CS_INFLIGHT.set(key, p); p.finally(()=>{ if(CS_INFLIGHT.get(key)===p) CS_INFLIGHT.delete(key); }); }
  const r=await p;
  if(r&&r.ok){ res.ok++; if((r.changed||[]).length) res.changed++; csSettlePersonal(id, key); } else res.failed.push(id); }
 return res; }
// every pending entry for a personal game that is on this computer (one request per distinct set of details)
function csPendingList(){ const todo=new Map(); const scan=miss=>Object.entries(miss||{}).forEach(([id,x])=>{ x=x&&typeof x==='object'?csKept(x, id):null; if(x&&PERSONAL[id]&&csPendingPersonal(id, x)){ const k=csMetaKey(id, x); if(!todo.has(k)) todo.set(k, [id, x]); } });
 scan(COLL.missing); Object.values((typeof CS!=='undefined'&&CS.items)||{}).forEach(r=>scan(r&&r.coll&&r.coll.missing)); return [...todo.values()]; }
// held while a Full Studio Backup restores (the backup's details reach this computer's own games only inside the restore's transaction)
var CS_HOLD=0;
function csReconcilePersonal(){ const l=CS_HOLD?[]:csPendingList(); return l.length?csMergePersonal(l):Promise.resolve({sent:0, ok:0, changed:0, failed:[]}); }
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
// a cover kept under another name of the same picture (the built site keeps one file of byte-identical covers: art/aliases.json, in the page)
function csArtAlias(p){ const A=(typeof DATA!=='undefined'&&DATA&&DATA.art_aliases)||null; return A&&typeof p==='string'&&Object.prototype.hasOwnProperty.call(A,p)?A[p]:p; }
function ccsArtSrc(p){ p=csArtAlias(String(p||'')); if(!p) return ''; if(/^(https?:|data:|blob:)/.test(p)||/^art\/[0-9a-f]{16}\.jpg$/.test(p)) return p; return artUrl(p); }
// a kept entry's details as the page uses them: always through csCleanMissing, whatever stored them (an import, a backup, an older
// version); cached per stored object (entries are replaced, never changed in place)
function csKept(m, id){ const w=csKept.w=csKept.w||new WeakMap(); let c=w.get(m); if(!c||c.id!==id){ c={id, v:csCleanMissing(m, id)}; w.set(m, c); } return c.v; }
function csMissing(id){ const m=(COLL.missing||{})[id]; return m&&typeof m==='object'?csKept(m, id):{title:String(id).slice(0,120), source:'unknown'}; }
// ---- kept entries in the Game Library: an entry of the open collection that this device cannot resolve (a personal game whose ROM file
//      is not here, which is always so on the website, or a game this Game Library does not know) still shows as a game card, from the
//      details kept with the collection (title, year, publisher, its own cover), with the checkbox of the open collection or loadout and
//      its Game Details; it says plainly that its ROM file is not on this device. It is never added to byId: export, the Tray's list of
//      entries without a file and storage still treat it exactly as before (iPhone finding: they used to disappear from the grid) ----
var KEPT;   // id -> {m, row}: rebuilt only when the kept details change. A hoisted var made on first use: the start-up render reaches keptRow() before this line runs
function keptRow(id){ const m0=(COLL.missing||{})[id]; if(!m0||typeof m0!=='object'||byId[id]) return null; KEPT=KEPT||new Map(); const c=KEPT.get(id); if(c&&c.m===m0) return c.row; const m=csKept(m0, id);
 const pr=m.personal&&typeof m.personal==='object'?m.personal:{}; const u=Object.assign({}, pr.user||{});
 [['title','title'],['year','year'],['publisher','publisher'],['description','description'],['version','version_label']].forEach(([k,f])=>{ if(!csMeaningful(u[f], id)&&csMeaningful(m[k], id)) u[f]=m[k]; });
 if(!csMeaningful(u.title, id)) u.title=m.source==='personal'||/^p_/.test(id)?'Personal game':'Unknown game';
 const row=perRow(Object.assign({}, pr, {id, user:u, file_present:false, content_size:pr.content_size||m.size||0, file_name:pr.file_name||''}));
 const cover=[m.thumb_url, m.art].find(x=>x&&/^(https:|data:image\/(jpeg|png);base64,|art\/[0-9a-f]{16}\.jpg$)/.test(x)&&csThumbOk(x)||/^art\/[0-9a-f]{16}\.jpg$/.test(x||''));
 Object.assign(row, {art:cover?ccsArtSrc(cover):row.art, art_source:cover?'kept':row.art_source, kept:true, file_present:false, source:m.source==='personal'||/^p_/.test(id)?'personal':(m.source||'unknown'), kind:m.source==='personal'||/^p_/.test(id)?'personal':'kept', identity:'kept details (no game file here)', identity_verification:'details kept with the collection; the game file is not on this device'});
 KEPT.set(id, {m:m0, row}); return row; }
// the kept entries of the open collection
function keptRows(){ if(!COLL||!COLL.missing) return []; const out=[]; sel.forEach(id=>{ const r=keptRow(id); if(r) out.push(r); }); return out; }
// a kept entry unchecked in the collection view stays on screen, unchecked, for the rest of this visit, so the uncheck can be undone like a
// Game Library game's (its kept details stay in the collection data); cleared when another collection opens
var KEPT_OFF;
function keptOff(id, off){ KEPT_OFF=KEPT_OFF||new Set(); if(off) KEPT_OFF.add(id); else KEPT_OFF.delete(id); }
// ---- whether a kept entry's ROM file is available right now, from the same sources Back Up Collection uses (never inferred from "not in
//      byId"): the website holds the files chosen in this visit (ROMSRC: references only, nothing is stored, gone after a reload); the
//      Windows app checks this computer (the Back Up Collection ROM check). States: visit | here | none | checking | unknown ----
var KEPT_AV, KEPT_AVQ;   // app: id -> true/false (null: the check failed); ids already asked
function keptAvail(id){ if(typeof ccsAppPackages==='function'&&ccsAppPackages()){ KEPT_AV=KEPT_AV||new Map(); if(!KEPT_AV.has(id)){ keptAvailAsk(id); return 'checking'; } const v=KEPT_AV.get(id); return v===null?'unknown':v?'here':'none'; }
  let held=false; try{ held=ROMSRC.has(id); }catch(e){}   // ROMSRC is a const of ccs.js: the start-up render can reach this before that line has run (and typeof throws then)
 let have=null; try{ have=CCSROMS._have; }catch(e){}   // CCSROMS is a const of ccs-play.js: the same start-up render reaches this before it exists (its keys arrive later: keptAvailChanged)
 if(!held&&have){ const k=String(id).replace(/^p_/,''); for(const s of have) if(s.startsWith(k)) return 'browser'; }   // kept for playing in this browser (W1)
 return held?'visit':'none'; }
function keptAvailAsk(id){ KEPT_AVQ=KEPT_AVQ||new Set(); if(KEPT_AVQ.has(id)) return; KEPT_AVQ.add(id); clearTimeout(keptAvailAsk.t);
 keptAvailAsk.t=setTimeout(async()=>{ const ids=[...KEPT_AVQ].filter(x=>!KEPT_AV.has(x)); if(!ids.length) return; let chk=null;
  try{ chk=await hkPost('/api/package/rom-check', {games:ids.map(x=>({version_id:x, title:csMissing(x).title||x}))}); }catch(e){}
  const yes=new Set(((chk&&chk.ok&&chk.available)||[]).map(g=>g.version_id)); ids.forEach(x=>KEPT_AV.set(x, chk&&chk.ok?yes.has(x):null)); keptAvailChanged(); }, 60); }
// a Back Up Collection check (either source) is the newest word on the games it checked
function keptAvailNoted(ready, missing){ if(typeof ccsAppPackages==='function'&&ccsAppPackages()){ KEPT_AV=KEPT_AV||new Map(); (ready||[]).forEach(x=>KEPT_AV.set(x, true)); (missing||[]).forEach(x=>KEPT_AV.set(x, false)); } keptAvailChanged(); }
// cards and an open Game Details follow the new state
function keptAvailChanged(){ clearTimeout(keptAvailChanged.t); keptAvailChanged.t=setTimeout(()=>{ if(!keptRows().length) return; render();
 const box=$('detailsBox'), n=box&&box.querySelector('.keptnote'); if(n&&$('details').classList.contains('open')&&n.dataset.id){ const k=keptNote(n.dataset.id); n.className='msgbox keptnote'+(k.warn?' warn':''); n.innerHTML=k.html; } }, 30); }
function keptNote(id){ const a=keptAvail(id), app=csAwayHere(), kept="This game's details are kept with the collection, and it stays in the collection.";
 if(a==='visit') return {html:'<b>Game file held for this visit.</b> Ready to back up. After reloading or closing this page, choose the game file or .ccs package again. Game details stay saved.'};
 if(a==='browser') return {html:`<b>Kept in this browser for playing.</b> It plays here and is ready to back up. That copy is not a backup: keep your own file or a .ccs backup. ${kept}`};
 if(a==='here') return {html:`<b>Game file on this computer.</b> This game is not in the Game Library, but its game file is on this computer, so it can be backed up with the collection. ${kept}`};
 if(a==='checking') return {html:`<b>Checking this computer for its game file…</b> ${kept}`};
 if(a==='unknown') return {warn:true, html:`<b>Its game file could not be checked.</b> ${kept}`};
 return {warn:true, html:`<b>Game file missing.</b> ${app?'Its game file is not on this computer':'This browser has no copy of its game file'}. ${kept} Choose <b>Locate Game File…</b> to add its game file, or <b>Remove from This Collection</b>.`}; }
// the card/row mark: [text, tooltip, held (true when the file can be exported now)]
function keptBadge(id){ const a=keptAvail(id); return a==='visit'?['Game file held for this visit','Its game file was chosen in this browser visit. The website does not keep it: after a reload, choose it again.',true]
 :a==='browser'?['Kept in this browser','A private copy of its game file is kept in this browser for playing; it is ready to back up.',true]
 :a==='here'?['Game file on this computer','Its game file is on this computer. This game is not in the Game Library.',true]:a==='checking'?['Checking game file…','Checking this computer for its game file',false]
 :a==='unknown'?['Game file not checked','Its game file could not be checked',false]:['No game file here',(csAwayHere()?'The game file is not on this computer':'This browser has no copy of its game file')+': its details are kept with this collection',false]; }
// Game Details of a kept entry say so first
(function(){ const _d=openDetails; openDetails=function(id){ const r=_d.apply(this, arguments); const g=!byId[id]&&keptRow(id), box=$('detailsBox'); if(!g||!box) return r; const h=box.querySelector('h2');
 if(h&&!box.querySelector('.keptnote')){ const k=keptNote(id); h.insertAdjacentHTML('afterend', `<div class="msgbox keptnote${k.warn?' warn':''}" data-id="${esc(id)}">${k.html}</div>`); } return r; }; })();
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
   ul.insertAdjacentHTML('beforeend', `<li class="trmiss-h"><div class="small"><b>${miss.length} game${miss.length===1?'':'s'} ${csAwayWord()}</b> · ${csAwayHere()?'add their game files with Add Game Files or Locate Game File…':'their details are kept with this collection; the website does not keep game files'}</div></li>`+miss.map(id=>{ const x=csMissing(id); const img=x.art?ccsArtSrc(x.art):ccsArtSrc(x.thumb_url||''); return `<li class="trmiss">${img?`<img loading="lazy" src="${esc(img)}" alt="" onerror="this.style.visibility='hidden'">`:'<div class="ph" style="width:36px;height:51px;padding:0;font-size:7px"><span style="border:none">NES</span></div>'}<div style="min-width:0;flex:1"><button type="button" class="tl topen" data-id="${esc(id)}" data-open="${esc(id)}">${esc(x.title)}</button><div class="small">${[x.version, x.year, x.publisher].filter(Boolean).map(v=>esc(String(v))).join(' · ')}</div><div class="small"><span class="tag">${x.source==='personal'?'personal game':'not in this Game Library'}</span></div></div><span class="x" data-rm="${esc(id)}" title="Remove from collection" role="button" tabindex="0">✕</span></li>`; }).join('')); } }catch(e){} return r; }; })();
