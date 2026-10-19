// ---- NES Classic Loadouts (one implementation for the website and the Windows app) ----
//      Game Library → My Collections (any size, never limited by the console) → NES Classic Loadouts: named subsets of one collection that
//      the person plans to install on a physical NES Classic Edition. A loadout is a plan, never a claim about what a console holds.
//      A loadout keeps exact version ids that are all in its collection, its own revision decisions and its own console HOME settings
//      (folders, theme, HOME favorites); it references the collection's games and never copies game files. Loadouts are stored inside
//      their collection's record (`loadouts`), so exports, transfers, backups and shares carry them.
//      Editing a loadout opens it in the working copy (selection, decisions, HOME settings) while the collection waits in LO
//      (collections.js), so the Tray, Preview on TV, HOME Setup and Install on NES Classic all work on exactly that loadout.
//      Opening, switching or importing a collection never opens a loadout; Back to Collection always returns.
const LO_DEFAULT_NAME='Loadout', LO_MIGRATED_NAME='My NES Classic Setup';
function loList(r){ r=r||csActive(); return (r&&Array.isArray(r.loadouts))?r.loadouts:[]; }
function loGet(lid, r){ return loList(r).find(l=>l.id===lid)||null; }
function loCur(){ return LO?loGet(LO.lid):null; }
function loNewId(){ return 'l'+csId().slice(1); }
function loUniqueName(name, except){ const base=(name||LO_DEFAULT_NAME).trim().slice(0,56)||LO_DEFAULT_NAME; const have=new Set(loList().filter(l=>l.id!==except).map(l=>l.name.toLowerCase())); if(base===LO_DEFAULT_NAME){ for(let i=1;i<999;i++){ const n=`${base} ${i}`; if(!have.has(n.toLowerCase())) return n; } } if(!have.has(base.toLowerCase())) return base; for(let i=2;i<999;i++){ const n=`${base} ${i}`; if(!have.has(n.toLowerCase())) return n; } return base+' '+Date.now(); }
const loPl=(n,w)=>`${n.toLocaleString('en-US')} ${w}${n===1?'':'s'}`;
// ---- migration (idempotent): every existing collection keeps all its games; one that has games or HOME settings gets one loadout with all of
//      them and a copy of its HOME settings, so the console set-up made before loadouts existed is still one click away. Nothing is removed. ----
(function loMigrate(){ if(!CS||CS.loadoutsMigrated) return; const todo=Object.values(CS.items).filter(r=>!Array.isArray(r.loadouts)); if(todo.length){ try{ localStorage.setItem(CSKEY+'.before-loadouts', JSON.stringify({at:csNow(), store:CS})); }catch(e){} }
 let n=0; const now=csNow();
 todo.forEach(r=>{ r.loadouts=[]; const h=r.home; const homeSet=!!(h&&(((h.folders||[]).length)||h.theme||Object.keys((h.favorites||{}).items||{}).length)); if((r.sel||[]).length||homeSet){ r.loadouts.push({id:loNewId(), target:'nes-classic', name:LO_MIGRATED_NAME, created:now, modified:now, sel:(r.sel||[]).slice(), decisions:JSON.parse(JSON.stringify(r.decisions||{})), home:h?JSON.parse(JSON.stringify(h)):null, origin:'migrated'}); n++; } r.openLoadout=null; });
 CS.loadoutsMigrated={at:now, collections:todo.length, loadouts:n}; csWrite(); })();
// ---- storage estimate: the existing NES Classic storage model (DATA.storage, studio "storage" notice), applied to one loadout ----
//      needed = game files to add (built-in games already on the console add nothing) + artwork and menu data per added game + the native HOME
//      package (measured when it was prepared for this loadout in the app, otherwise estimated); available = the reference model's game space
//      (stock 2018 console, after Hakchi, RetroArch with its cores, a saves reserve and file-system slack). Unknown sizes are never counted as 0.
const LO_NH_BASE=1.5*1048576, LO_NH_GAME=160*1024;   // native HOME estimate when no package is prepared, from a measured package (1.3 MB program, themes and fonts + 154 KB of covers, backdrops and texts per game), rounded up
function loEstimate(ids, dec, opts){ const st=DATA.storage||{}; const o=st.overhead_estimates_mib||{}; const model=storageModel(); const cap=model==='unknown'?null:Math.round((st.conservative_usable_mib||0)*1048576);
 const d0=decisions; if(dec) decisions=dec; let rom=0, add=0, built=0; const unknown=[], noFile=[];
 try{ (ids||[]).forEach(id=>{ const g=byId[id]; if(g){ if(!installRequired(g)){ built++; return; } add++; if(g.source==='personal'&&g.file_present===false) noFile.push(g.display_title); if(g.payload_size>0) rom+=g.payload_size+16; else unknown.push(g.display_title); }
   else { const x=csMissing(id); add++; noFile.push(x.title); if(x.size>0) rom+=x.size+16; else unknown.push(x.title); } }); } finally { decisions=d0; }
 const meta=add*(o.per_game_metadata_art_kib||80)*1024; let nh=LO_NH_BASE+(ids||[]).length*LO_NH_GAME, nhMeasured=false;
 try{ if(opts&&opts.open&&typeof NHP!=='undefined'&&NHP.result&&NHP.result.ok&&NHP.fp===nhFp()){ const S=(NHP.result.manifest||{}).sizes||{}; const b=S.total_with_manifest||S.total; if(b>0){ nh=b; nhMeasured=true; } } }catch(e){}
 const need=rom+meta+nh; const status=cap==null?'unknown':need>cap?'over':unknown.length?'unconfirmed':need>cap*0.9?'near':'fits';
 return {n:(ids||[]).length, add, built, rom, meta, nh, nhMeasured, need, cap, model, unknown, noFile, status}; }
function loStatusText(e){ return e.status==='fits'?'Estimated to fit':e.status==='near'?'Close to the limit':e.status==='over'?`Too large: ~${fmt(e.need-e.cap)} over`:e.status==='unconfirmed'?`Fit not confirmed: ${loPl(e.unknown.length,'game size')} unknown`:'Free space unknown'; }
function loStatusCls(e){ return {fits:'ok',near:'warn',over:'bad',unconfirmed:'warn',unknown:'na'}[e.status]; }
function loEstimateFor(l){ if(LO&&l.id===LO.lid) return loEstimate([...sel], decisions, {open:true}); return loEstimate(l.sel||[], l.decisions||{}); }
function loLine(l){ const e=loEstimateFor(l); return `${loPl(e.n,'game')} · ~${fmt(e.need)} · ${esc(loStatusText(e))}`; }
// ---- open / close ----
function loResetConsoleState(){ try{ if(typeof TV!=='undefined'){ if(TV.open&&typeof closeTv==='function') closeTv(); TV.model=null; TV.proj=null; } }catch(e){} try{ if(typeof INST!=='undefined'){ INST.manifest=null; INST.preview=null; INST.prepared=null; } if(typeof CONSOLE!=='undefined'){ CONSOLE.plan=null; CONSOLE.preflight=null; CONSOLE.result=null; } if(typeof NHP!=='undefined'){ NHP.result=null; NHP.fp=null; } }catch(e){} }
function loWriteWorking(){ try{ localStorage.setItem(KEY, JSON.stringify([...sel])); localStorage.setItem(DKEY, JSON.stringify(decisions)); }catch(e){} try{ if(typeof desktopStateDirty==='function') desktopStateDirty(); }catch(e){} }
function loOpen(lid, quiet){ if(!CS) return; if(LO&&LO.lid===lid){ loUi(); return; } csSaveActiveNow(); const rec=CS.items[CS.active]; const l=loGet(lid, rec); if(!rec||!l) return;
 const coll=new Set(rec.sel||[]);
 LO={cid:rec.id, lid, collSel:coll, collDecisions:JSON.parse(JSON.stringify(rec.decisions||{})), collHome:rec.home?JSON.parse(JSON.stringify(rec.home)):null};
 sel=new Set((l.sel||[]).filter(id=>coll.has(id))); decisions=JSON.parse(JSON.stringify(l.decisions||{}));
 try{ if(l.home) localStorage.setItem(HOME_KEY, JSON.stringify(l.home)); else localStorage.removeItem(HOME_KEY); }catch(e){}
 rec.openLoadout=lid; csWrite(); loWriteWorking(); loResetConsoleState();
 render(); updateStatusLine(); try{ hkLabelUpdate(); }catch(e){} csMenuRefresh(); loUi(); if(!quiet) toast(`Editing the NES Classic Loadout "${l.name}" · ${loPl(sel.size,'game')} from "${rec.name}"`); }
function loClose(quiet){ if(!LO) return; csSaveActiveNow(); const rec=CS.items[CS.active]; if(!rec){ LO=null; return; } rec.openLoadout=null; csWrite(); loResetConsoleState(); csLoadWorking(rec); loWriteWorking(); if(!quiet) toast(`Back to the collection "${rec.name}" · ${loPl(sel.size,'game')}`); }
// the whole collection, also while a loadout is open (collection-level actions)
function loAsCollection(fn){ if(!LO) return fn(); const s0=sel, d0=decisions; CS_COLLVIEW++; try{ sel=new Set(LO.collSel); decisions=LO.collDecisions; return fn(); } finally { sel=s0; decisions=d0; CS_COLLVIEW--; } }
// ---- create, rename, duplicate, delete ----
function loCreate(name, mode){ csSaveActiveNow(); const rec=CS.items[CS.active]; if(!rec) return null; const now=csNow(); const all=mode==='all'; const ids=all?(rec.sel||[]).slice():[];
 const dec={}; if(all) Object.entries(rec.decisions||{}).forEach(([k,v])=>{ if(ids.includes(k)) dec[k]=v; });
 const l={id:loNewId(), target:'nes-classic', name:loUniqueName(name), created:now, modified:now, sel:ids, decisions:dec, home:null, origin:all?'all':'choose'};
 rec.loadouts=loList(rec).concat([l]); csWrite(); loOpen(l.id, true);
 toast(all?`Created "${l.name}" with all ${loPl(ids.length,'game')} of "${rec.name}"`:`Created "${l.name}": check the games from "${rec.name}" that should go on your NES Classic`); return l; }
function loRename(lid, name){ const l=loGet(lid); if(!l) return; name=(name||'').trim().slice(0,60); if(!name) return; l.name=loUniqueName(name, lid); l.modified=csNow(); csWrite(); csDirty(); loUi(); }
function loDuplicate(lid){ if(LO&&LO.lid===lid) csSaveActiveNow(); const rec=CS.items[CS.active]; const src=loGet(lid, rec); if(!src) return null; const now=csNow();
 const c=Object.assign(JSON.parse(JSON.stringify(src)), {id:loNewId(), name:loUniqueName(src.name+' copy'), created:now, modified:now, origin:'duplicate'}); const i=rec.loadouts.indexOf(src); rec.loadouts.splice(i+1, 0, c); csWrite(); loUi(); toast(`Duplicated as "${c.name}" (${loPl(c.sel.length,'game')})`); return c; }
function loDelete(lid){ const rec=CS.items[CS.active]; const l=loGet(lid, rec); if(!l) return; if(LO&&LO.lid===lid) loClose(true); const r2=CS.items[CS.active]; r2.loadouts=loList(r2).filter(x=>x.id!==lid); csWrite(); loUi(); toast(`Deleted the NES Classic Loadout "${l.name}". The collection "${r2.name}" still has all ${loPl((r2.sel||[]).length,'game')}.`); }
// ---- the dialogs ----
function loNameField(v){ return `<div class="fld"><label for="loNm">Loadout name</label><input type="text" id="loNm" maxlength="60" value="${esc(v)}" spellcheck="false"></div>`; }
// Prepare for NES Classic: open a loadout or create one (all of the collection's games, or chosen ones); `then` continues the action that asked
function loPrepare(opts){ opts=opts||{}; if(typeof closeVerPop==='function') closeVerPop(); document.querySelectorAll('.menu').forEach(x=>x.hidden=true); csSaveActiveNow(); const rec=CS.items[CS.active]; const list=loList(rec); const n=(rec.sel||[]).length; const all=loEstimate(rec.sel||[], rec.decisions||{});
 const mode=opts.mode||(n?'all':'choose'); const then=typeof opts.then==='function'?opts.then:null;
 openColl(opts.title||'Prepare for NES Classic', `from the collection "${rec.name}" · ${loPl(n,'game')}`, `${opts.lead?`<div class="msgbox">${esc(opts.lead)}</div>`:''}<p class="small">Games go on your NES Classic from an <b>NES Classic Loadout</b>: a named set of games from this collection that you plan to install. Your collection itself is never trimmed or changed, and a loadout is only a plan until it is installed.</p>
  ${list.length&&!opts.newOnly?`<div class="lohead">Continue with a loadout</div><div class="lolist">${list.map(l=>{ const e=loEstimateFor(l); return `<div class="lorow"><div class="lon"><b>${esc(l.name)}</b><span class="small">${loPl(e.n,'game')} · ~${fmt(e.need)} · <span class="lost ${loStatusCls(e)}">${esc(loStatusText(e))}</span></span></div><button class="btn sm hk" data-lo-open="${esc(l.id)}">Open</button></div>`; }).join('')}</div><div class="lohead">Or create a new loadout</div>`:''}
  ${loNameField(loUniqueName(LO_DEFAULT_NAME))}
  <div class="lochoice" role="radiogroup" aria-label="Start the loadout with">
   <label class="lc${n?'':' off'}"><input type="radio" name="loMode" value="all" ${mode==='all'&&n?'checked':''} ${n?'':'disabled'}><span><b>Use all ${loPl(n,'collection game')}</b><span class="small">${n?`Start with every game of "${esc(rec.name)}" · ~${fmt(all.need)} · ${esc(loStatusText(all))}. Remove any you like afterwards.`:'This collection has no games yet.'}</span></span></label>
   <label class="lc"><input type="radio" name="loMode" value="choose" ${mode==='choose'||!n?'checked':''}><span><b>Choose games</b><span class="small">Start empty, then check the games from this collection that should go on the console.</span></span></label>
  </div>
  <div class="acts"><span class="sp"></span><button class="btn" id="loNo">Cancel</button><button class="btn hk" id="loGo">Create Loadout</button></div>`);
 const inp=$('loNm'); inp.onkeydown=e=>{ if(e.key==='Enter') $('loGo').click(); }; if(!list.length||opts.newOnly){ inp.focus(); inp.select(); }
 $('loNo').onclick=closeColl;
 $('loGo').onclick=()=>{ const nm=inp.value.trim(); if(!nm){ inp.focus(); return; } const m=(document.querySelector('input[name=loMode]:checked')||{}).value||'choose'; closeColl(); loCreate(nm, m); if(then) then(); };
 $('collBody').onclick=e=>{ const b=e.target.closest('[data-lo-open]'); if(!b) return; closeColl(); loOpen(b.dataset.loOpen); if(then) then(); }; }
function loNameDialog(title, value, action, done, html){ openColl(title,'', `${html||''}${value!==null?loNameField(value):''}<div class="acts"><span class="sp"></span><button class="btn" id="loNo">Cancel</button><button class="btn ${action==='Delete'?'danger':'hk'}" id="loGo">${esc(action)}</button></div>`);
 const inp=$('loNm'); if(inp){ inp.focus(); inp.select(); inp.onkeydown=e=>{ if(e.key==='Enter') $('loGo').click(); }; }
 $('loNo').onclick=closeColl; $('loGo').onclick=()=>{ const v=inp?inp.value.trim():''; if(inp&&!v){ inp.focus(); return; } closeColl(); done(v); }; }
function loRenameDialog(lid){ const l=loGet(lid); if(l) loNameDialog('Rename NES Classic Loadout', l.name, 'Rename', v=>loRename(lid, v)); }
function loDeleteDialog(lid){ const l=loGet(lid); if(!l) return; const rec=csActive();
 loNameDialog(`Delete "${l.name}"?`, null, 'Delete', ()=>loDelete(lid), `<p class="small">The NES Classic Loadout "${esc(l.name)}" (${loPl((LO&&LO.lid===lid?sel.size:(l.sel||[]).length),'game')}) and its HOME settings are deleted. The collection "${esc(rec.name)}" keeps all of its ${loPl(csCollSel().size,'game')}, and its other loadouts are not changed. Nothing on a console changes.</p>`); }
function loManager(){ csSaveActiveNow(); const rec=csActive(); const list=loList(rec);
 openColl('NES Classic Loadouts', `in the collection "${rec.name}" · ${loPl((rec.sel||[]).length,'game')}`, `<p class="small">Each loadout is a plan for one NES Classic set-up, chosen from this collection. Loadouts are independent of each other: changing one never changes another or the collection.</p>
  <div class="csbar"><button class="btn hk" id="loNewB">New Loadout…</button></div>
  ${list.length?`<div class="cslist" role="list">${list.map(l=>{ const on=LO&&LO.lid===l.id; const e=loEstimateFor(l); return `<div class="csrow${on?' on':''}" role="listitem" data-lid="${esc(l.id)}"><div class="csn"><b>${esc(l.name)}</b>${on?'<span class="tag">open</span>':''}<span class="small">${loPl(e.n,'game')} · ~${fmt(e.need)} of ~${e.cap!=null?fmt(e.cap):'?'} · <span class="lost ${loStatusCls(e)}">${esc(loStatusText(e))}</span> · changed ${esc(nowShort(l.modified))}</span></div>
   <div class="csacts">${on?'':`<button class="btn sm hk" data-a="open">Open</button>`}<button class="btn sm" data-a="rename">Rename</button><button class="btn sm" data-a="dup">Duplicate</button><button class="btn sm danger" data-a="del">Delete</button></div></div>`; }).join('')}</div>`:'<p class="small">No loadouts yet.</p>'}`);
 $('loNewB').onclick=()=>loPrepare({newOnly:true, title:'New NES Classic Loadout'});
 $('collBody').onclick=e=>{ const b=e.target.closest('[data-a]'); if(!b) return; const row=b.closest('[data-lid]'); if(!row) return; const lid=row.dataset.lid; const a=b.dataset.a;
  if(a==='open'){ closeColl(); loOpen(lid); } else if(a==='rename'){ loRenameDialog(lid); } else if(a==='dup'){ loDuplicate(lid); loManager(); } else if(a==='del'){ loDeleteDialog(lid); } }; }
// what the estimate is made of, in plain words first; the reference model's figures on request
function loStorageDetails(){ const l=loCur(); if(!l) return; const e=loEstimate([...sel], decisions, {open:true}); const st=DATA.storage||{}; const o=st.overhead_estimates_mib||{};
 const row=(a,b)=>`<tr><td>${a}</td><td style="text-align:right;white-space:nowrap">${b}</td></tr>`;
 openColl('Will it fit?', `NES Classic Loadout "${l.name}"`, `<div class="msgbox ${loStatusCls(e)}"><b>${esc(loStatusText(e))}.</b> This loadout needs about <b>${fmt(e.need)}</b>; a stock NES Classic has about <b>${e.cap!=null?fmt(e.cap):'an unknown amount'}</b> for games. ${e.status==='over'?'The loadout is saved as it is; remove games here (the collection keeps them) or make a smaller loadout to install it.':''}</div>
  <table class="pv lofit"><tbody>${row(`Game files to add (${loPl(e.add,'game')}; ${e.built} built into the console need no space)`, '~'+fmt(e.rom))}${row(`Artwork and menu data for the added games (estimate, ${o.per_game_metadata_art_kib||80} KB each)`, '~'+fmt(e.meta))}${row(`Native HOME menu (${e.nhMeasured?'measured from the package prepared on this computer':'estimate; prepare it in Install on NES Classic to measure it'})`, '~'+fmt(e.nh))}${row('<b>Needed</b>', '<b>~'+fmt(e.need)+'</b>')}${e.cap!=null?row('<b>Available for games</b> (estimate for a stock NES Classic)', '<b>~'+fmt(e.cap)+'</b>'):''}</tbody></table>
  ${e.unknown.length?`<div class="msgbox warn"><b>${loPl(e.unknown.length,'game')} without a known size</b> (not counted, so the total may be too low): ${esc(e.unknown.slice(0,12).join(', '))}${e.unknown.length>12?' …':''}</div>`:''}
  ${e.noFile.length?`<div class="msgbox warn"><b>${loPl(e.noFile.length,'game file')} not on this computer</b>: ${esc(e.noFile.slice(0,12).join(', '))}${e.noFile.length>12?' …':''}. They stay in the loadout; add the files with Add Your Own Game before installing.</div>`:''}
  <details class="lotech"><summary>Technical details</summary><p class="small">Available space comes from the Studio's reference model of a stock 2018 NES Classic Edition, not from your console: ${fmt(mibToMb(st.measured_free_mib_before_mod||0)*1e6)} free on its data partition before any change, minus ${fmt(mibToMb(st.root_reserved_mib||0)*1e6)} reserved by the file system, ~${o.hakchi_install||0} MiB for Hakchi, ~${o.retroarch_plus_cores||0} MiB for RetroArch and its cores, ~${o.saves_and_states_reserve||0} MiB for saves, and ${o.filesystem_slack_pct||0}% file-system slack. Games, modules or saves already added to your console by other tools are not known until the console is inspected, which this version does not do; the installation plan uses measured free space once inspection is approved.</p></details>
  <div class="acts"><span class="sp"></span><button class="btn" id="loWhyOk">Close</button></div>`); $('loWhyOk').onclick=closeColl; }
// ---- removing a game from a collection while loadouts use it: an explicit choice, never a silent change ----
// prev = the collection's games before the change: a version counts as a replacement only when it was just added
let LO_RECON=false;
function loReconcile(prev){ if(LO||LO_RECON||!CS) return; const rec=csActive(); const list=loList(rec); if(!list.length) return; prev=prev||new Set(rec.sel||[]);
 const hit=list.map(l=>({l, gone:(l.sel||[]).filter(id=>!sel.has(id))})).filter(x=>x.gone.length); if(!hit.length) return;
 const titleOf2=id=>{ const g=byId[id]; return g?g.display_title:csMissing(id).title; };
 const swap=id=>{ const g=byId[id]; if(!g||!g.title_id) return null; const t=TITLES[g.title_id]; if(!t) return null; return t.versions.find(v=>v!==id&&sel.has(v)&&!prev.has(v))||null; };
 const all=[...new Set(hit.flatMap(x=>x.gone))]; const allSwap=all.every(id=>swap(id)); LO_RECON=true;
 openColl(allSwap?'Update your NES Classic Loadouts?':'These games are in NES Classic Loadouts', `collection "${rec.name}"`, `<p class="small">${allSwap?`You changed the version of ${loPl(all.length,'game')} in the collection. The loadouts below still use the earlier version.`:`You removed ${loPl(all.length,'game')} from the collection that ${all.length===1?'is':'are'} still part of the loadouts below. A loadout can only hold games of its collection.`}</p>
  <div class="lolist">${hit.map(x=>`<div class="lorow"><div class="lon"><b>${esc(x.l.name)}</b><span class="small">${x.gone.slice(0,10).map(id=>{ const s=swap(id); return esc(titleOf2(id))+(s?` → ${esc(shortVer(byId[s]))}`:''); }).join(', ')}${x.gone.length>10?` and ${x.gone.length-10} more`:''}</span></div></div>`).join('')}</div>
  <div class="acts"><button class="btn" id="loKeep">${allSwap?'Undo the version change':'Keep them in the collection'}</button><span class="sp"></span><button class="btn hk" id="loDrop">${allSwap?'Use the new version in the loadouts':'Remove them from the loadouts too'}</button></div>`);
 const keep=()=>{ if(!LO_RECON) return; LO_RECON=false; all.forEach(id=>sel.add(id)); if(allSwap) all.forEach(id=>{ const s=swap(id); if(s&&!COLL.multi[(byId[s]||{}).title_id]) sel.delete(s); }); persist(); render(); closeColl(); toast('Nothing was removed: the collection and its loadouts are unchanged'); };
 $('loKeep').onclick=keep;   // closing the dialog any other way also keeps everything
 const mo=new MutationObserver(()=>{ if($('collm').classList.contains('open')) return; mo.disconnect(); if(LO_RECON) keep(); }); mo.observe($('collm'),{attributes:true, attributeFilter:['class']});
 $('loDrop').onclick=()=>{ LO_RECON=false; csSaveActiveNow(); hit.forEach(h=>{ const x={l:loGet(h.l.id), gone:h.gone}; if(!x.l) return; const out=[]; (x.l.sel||[]).forEach(id=>{ if(sel.has(id)){ if(!out.includes(id)) out.push(id); return; } const s=swap(id); if(s&&!out.includes(s)){ out.push(s); if(x.l.decisions&&x.l.decisions[id]) x.l.decisions[s]=x.l.decisions[id]; } if(x.l.decisions) delete x.l.decisions[id]; }); x.l.sel=out; x.l.modified=csNow(); if(x.l.home&&Array.isArray(x.l.home.folders)) x.l.home.folders.forEach(f=>{ if(Array.isArray(f.order)) f.order=f.order.map(id=>out.includes(id)?id:(swap(id)||id)).filter(id=>out.includes(id)); }); });
  csWrite(); closeColl(); csDirty(); loUi(); toast(allSwap?`Updated ${loPl(hit.length,'loadout')} to the new version`:`Removed from the collection and from ${loPl(hit.length,'loadout')}`); }; }
// while a loadout is open, a game it gains that is not in the collection yet (Add Your Own Game, a details checkbox) joins the collection too
function loAdopt(){ if(!LO) return; const extra=[...sel].filter(id=>!LO.collSel.has(id)); extra.forEach(id=>{ LO.collSel.add(id); if(LO.collDecisions[id]===undefined&&decisions[id]) LO.collDecisions[id]=decisions[id]; });
 [...sel].forEach(id=>{ if(decisions[id]===undefined&&LO.collDecisions[id]) decisions[id]=LO.collDecisions[id]; });
 if(extra.length){ const t=byId[extra[0]]; toast(`${extra.length===1?`"${t?t.display_title:extra[0]}" was`:loPl(extra.length,'game')+' were'} also added to the collection "${COLL.name}"`); csMenuRefresh(); } }
(function(){ const _p=persist; persist=function(){ const r=_p.apply(this,arguments); try{ if(LO) loAdopt(); else { const prev=window.LO_PREV&&window.LO_PREV.cid===CS.active?window.LO_PREV.sel:null; window.LO_PREV={cid:CS.active, sel:new Set(sel)}; setTimeout(()=>loReconcile(prev),0); } }catch(e){} return r; }; })();
// ---- the library while a loadout is open: only the collection's games, shown in the collection's own versions; checked = in this loadout ----
(function(){ const _v=visible; visible=function(){ if(!LO) return _v.apply(this,arguments); return _visibleBase().filter(g=>LO.collSel.has(g.game_id)); };
 const _c=chosenVersion; chosenVersion=function(t){ if(LO){ for(const v of t.versions){ if(sel.has(v)) return v; } for(const v of t.versions){ if(LO.collSel.has(v)) return v; } } return _c(t); };
 const _o=openVerPop; openVerPop=function(btn){ _o(btn); if(!LO) return; vp.querySelectorAll('[data-pick]').forEach(b=>{ if(!LO.collSel.has(b.dataset.pick)) b.remove(); }); const ft=vp.querySelector('.ft'); if(ft) ft.innerHTML='<span class="small">The versions of this game in your collection. Add other versions to the collection first.</span>'; };
 const _d=openDetails; openDetails=function(id){ _d.apply(this,arguments); const box=$('detailsBox'); const cb=box&&box.querySelector('label input[type=checkbox][data-id]'); if(cb&&cb.parentNode) cb.parentNode.lastChild.nodeValue=LO?' In this NES Classic Loadout':' In this collection'; };
})();
// ---- collection-level actions use the collection, also while a loadout is open ----
(function(){ ['desktopInvite','startHandoff'].forEach(n=>{ const f=window[n]; if(typeof f!=='function') return; window[n]=function(){ return loAsCollection(()=>f.apply(this,arguments)); }; });
 const ll=window.loadLocalLibrary; if(typeof ll==='function'){ loadLocalLibrary=function(){ if(LO) loClose(true); return ll.apply(this,arguments); }; }
 if(typeof shHome==='function'){ shHome=function(ids){ const h=(csActive()||{}).home||{}; const set=new Set(ids); const folders=(h.folders||[]).filter(f=>f&&f.name).slice(0,100).map(f=>({name:String(f.name).slice(0,60), order:(f.order||[]).filter(x=>set.has(x))})).filter(f=>f.order.length); const theme=['arcade-grid','box-art-showcase','featured-shelf','adventure-map'].includes(h.theme)?h.theme:''; return (folders.length||theme)?{theme, folders}:null; }; }
 // the installation plan and the native HOME package name the loadout they were made from
 const _bm=buildManifest; buildManifest=function(){ const m=_bm.apply(this,arguments); const l=CS_COLLVIEW?null:loCur(); if(l&&csActive()){ m.loadout={id:l.id, target:'nes-classic', name:l.name, collection:csActive().name}; m.name=`${l.name} (${csActive().name})`; } return m; };
})();
// ---- console actions need a loadout: Preview on TV, HOME Setup and Install on NES Classic work on the open loadout; from the collection
//      they ask which loadout (or create one) first, so a collection is never prepared for the console by accident ----
document.addEventListener('click',e=>{ if(LO||!CS) return; const t=e.target.closest&&e.target.closest('#hkBtn,#trayHk,#tvBtn,#mTv,#homeBtn,[data-tvfocus]'); if(!t) return; e.preventDefault(); e.stopImmediatePropagation();
 const id=t.id, focus=t.dataset&&t.dataset.tvfocus; try{ $('details').classList.remove('open'); $('tray').classList.remove('open'); }catch(err){}
 const then=id==='tvBtn'||id==='mTv'||focus?()=>openTv(focus?{focus}:undefined):id==='homeBtn'?()=>{ if(typeof openFolders==='function') openFolders(); }:()=>openInstall();
 const what=id==='tvBtn'||id==='mTv'||focus?'Preview on TV shows':id==='homeBtn'?'HOME Setup arranges':'Install on NES Classic installs';
 loPrepare({then, lead:`${what} an NES Classic Loadout. Choose one, or create one from this collection.`}); }, true);
// ---- the interface ----
function loMenuHtml(){ const rec=csActive(); if(!rec) return ''; const list=loList(rec); const n=csCollSel().size;
 return `<div class="mhead"><div class="nm">NES Classic Loadouts</div><div class="small mut">Games from "${esc(rec.name)}" you plan to install on your NES Classic</div></div>
  ${list.length?list.map(l=>{ const on=LO&&LO.lid===l.id; return `<button class="mi csq${on?' on':''}" role="menuitemradio" aria-checked="${!!on}" data-lo="${esc(l.id)}"><b>${on?'✓ ':''}${esc(l.name)}</b><span class="d">${loLine(l)}</span></button>`; }).join(''):`<div class="mempty small">No loadouts yet. A loadout is the part of this collection you want on the console; the collection keeps everything.</div>`}
  <div class="sep"></div>
  <button class="mi" role="menuitem" data-lo-new="all" ${n?'':'disabled'}><b>New Loadout with All ${loPl(n,'Game')}…</b><span class="d">Start with every game of this collection; remove any you like.</span></button>
  <button class="mi" role="menuitem" data-lo-new="choose"><b>New Loadout, Choose Games…</b><span class="d">Start empty and check games from this collection.</span></button>
  ${LO?`<div class="sep"></div><button class="mi" role="menuitem" data-lo-act="rename"><b>Rename This Loadout…</b></button><button class="mi" role="menuitem" data-lo-act="dup"><b>Duplicate This Loadout</b></button><button class="mi danger" role="menuitem" data-lo-act="del"><b>Delete This Loadout…</b></button><div class="sep"></div><button class="mi" role="menuitem" data-lo-act="back"><b>Back to the Collection</b><span class="d">"${esc(rec.name)}" · ${loPl(n,'game')}</span></button>`:''}
  ${list.length?`<button class="mi" role="menuitem" data-lo-act="manage"><b>Manage Loadouts…</b><span class="d">Rename, duplicate or delete any loadout of this collection.</span></button>`:''}`; }
function loMenuClick(e){ const m=e.currentTarget; const q=e.target.closest('[data-lo]'); if(q){ m.hidden=true; loOpen(q.dataset.lo); return; }
 const nw=e.target.closest('[data-lo-new]'); if(nw){ m.hidden=true; loPrepare({newOnly:true, mode:nw.dataset.loNew, title:'New NES Classic Loadout'}); return; }
 const a=e.target.closest('[data-lo-act]'); if(!a) return; m.hidden=true; const act=a.dataset.loAct; const lid=LO&&LO.lid;
 if(act==='back') loClose(); else if(act==='manage') loManager(); else if(act==='rename'&&lid) loRenameDialog(lid); else if(act==='dup'&&lid){ const c=loDuplicate(lid); if(c) loOpen(c.id, true); } else if(act==='del'&&lid) loDeleteDialog(lid); }
function loUi(){ if(!CS) return; const on=!!LO; const rec=csActive(); document.body.classList.toggle('lomode', on);
 const list=loList(rec); const lc=$('loCount'); if(lc){ lc.textContent=list.length; lc.hidden=!list.length; }
 ['tvBtn','homeBtn','hkBtn'].forEach(id=>{ const b=$(id); if(b) b.hidden=!on; }); const w=$('loWrap'); if(w) w.hidden=on;
 const band=$('loBand'); if(band) band.hidden=!on;
 const l=loCur(); if(on&&l&&band){ const e=loEstimate([...sel], decisions, {open:true}); const pct=e.cap?Math.min(100, e.need/e.cap*100):0;
  $('loName').firstChild.nodeValue=l.name+' '; $('loFrom').innerHTML=`from the collection <b>${esc(rec.name)}</b>`;
  $('loFig').innerHTML=`<b>${loPl(sel.size,'game')}</b> of ${csCollSel().size.toLocaleString('en-US')} in the collection<span class="dot">·</span><span class="meter ${loStatusCls(e)}" aria-hidden="true"><i style="width:${pct.toFixed(1)}%"></i></span>~${fmt(e.need)} of ~${e.cap!=null?fmt(e.cap):'?'} <span class="mut">estimated space</span><span class="dot">·</span><span class="lost ${loStatusCls(e)}">${esc(loStatusText(e))}</span>`; }
 const hs=$('heroSelL'); if(hs) hs.textContent=on?'in loadout':'in collection';
 const sv=$('selVis'), us=$('unselVis'), sk=$('selKachi'), cl=$('clear'); const tgt=on?'this loadout':'the collection';
 if(sv) sv.textContent=`Add all shown to ${tgt}`; if(us) us.textContent=`Remove all shown from ${tgt}`; if(sk) sk.textContent=`Add shown kachikachi + verified games to ${tgt}`; if(cl) cl.lastChild.nodeValue=on?'Empty this loadout':'Remove every game from the collection';
 const th=$('trayHk'); if(th) th.textContent=on?'Install on NES Classic':'Prepare for NES Classic…'; const tx=$('trayExport'); if(tx) tx.hidden=on;
 const sto=$('storage'); if(sto) sto.hidden=!on;
 const q=$('q'); if(q) q.placeholder=on?`Search the games of "${rec.name}"`:'Search games (dr mario, smb3, zelda 2, megaman…)';
 ['loMenu','loMenu2'].forEach(id=>{ const m=$(id); if(m&&!m.hidden) m.innerHTML=loMenuHtml(); }); }
// the tray, stats and title say which one is being edited
(function(){ const _t=renderTray; renderTray=function(){ const r=_t.apply(this,arguments); try{ const sub=$('traySub'), ul=$('trayList'); const l=loCur();
  if(ul) ul.querySelectorAll('[data-rm]').forEach(x=>x.title=l?'remove from this loadout (the collection keeps it)':'remove from the collection');
  if(!l&&sub){ const n=sel.size, own=[...sel].filter(id=>byId[id]&&byId[id].source==='personal').length, away=[...sel].filter(id=>!byId[id]).length, b=[...sel].filter(id=>byId[id]&&(byId[id].builtin_status||'').startsWith('BUILT-IN')).length; const nl=loList().length;
   sub.innerHTML=n?`<div class="small"><b style="color:#fff;font-size:13px">${loPl(n,'game')} in this collection</b>${b?` · ${b} built into the NES Classic`:''}${own?` · ${own} personal`:''}${away?` · ${away} not on this computer`:''}</div><div class="lotray small">${nl?`This collection has ${loPl(nl,'NES Classic Loadout')}. `:''}A collection can be any size; what goes on the console is chosen in an <b>NES Classic Loadout</b>. <button type="button" class="btn sm" data-lo-prep="1">Prepare for NES Classic…</button></div>`:`<div class="small">No games in this collection yet: click games in the Game Library to add them.</div>`; }
  else if(l&&sub){ sub.insertAdjacentHTML('afterbegin', `<div class="lotray small"><b>NES Classic Loadout "${esc(l.name)}"</b>: the games planned for your NES Classic, from the collection "${esc(COLL.name)}". Removing a game here keeps it in the collection.</div>`); }
 }catch(e){} return r; }; })();
document.addEventListener('click',e=>{ const b=e.target.closest&&e.target.closest('[data-lo-prep]'); if(!b) return; $('tray').classList.remove('open'); loPrepare(); });
(function(){ const _u=updateStatusLine; updateStatusLine=function(){ const r=_u.apply(this,arguments); try{ const t=document.querySelector('#tray .ttl'); const l=loCur(); if(t&&l) t.lastChild.nodeValue=' NES Classic Loadout · '+l.name; }catch(e){} return r; }; })();
window.statsHtml=function(shown){ if(!LO) return `${shown.toLocaleString('en-US')} shown · <b>${loPl(sel.size,'game')} in this collection</b> · <span class="mut">${window.libCountText?libCountText():''}</span> · ${fav.size} ★`;
 return `${shown.toLocaleString('en-US')} shown · <b>${sel.size.toLocaleString('en-US')} of ${loPl(csCollSel().size,'collection game')} in this loadout</b> · ${fav.size} ★`; };
// Remove every game from the collection: asked first (Restore Previous Version keeps the earlier state)
document.addEventListener('click',e=>{ const b=e.target.closest&&e.target.closest('#clear'); if(!b||LO||!sel.size) return; if(!confirm(`Remove all ${loPl(sel.size,'game')} from the collection "${COLL.name}"?`)){ e.preventDefault(); e.stopImmediatePropagation(); document.querySelectorAll('.menu').forEach(x=>x.hidden=true); return; } try{ snapshotNow('before removing every game from "'+COLL.name+'"'); }catch(err){} }, true);
// ---- wiring ----
(function(){ ['loBtn','loName'].forEach(id=>{ const b=$(id); if(b) b.addEventListener('click',()=>{ csSaveActiveNow(); const m=$(id==='loBtn'?'loMenu':'loMenu2'); if(m) m.innerHTML=loMenuHtml(); }); });
 wireMenu('loBtn','loMenu'); wireMenu('loName','loMenu2');
 ['loMenu','loMenu2'].forEach(id=>{ const m=$(id); if(m) m.addEventListener('click',loMenuClick); });
 const bk=$('loBack'); if(bk) bk.onclick=()=>loClose(); const wy=$('loWhy'); if(wy) wy.onclick=loStorageDetails;
 const _r=render; render=function(){ const x=_r.apply(this,arguments); try{ if(LO) loUi(); }catch(e){} return x; };
 // the app's durable copy can arrive after start (a new WebView2 profile): reopen the loadout that was open
 if(typeof desktopApplyState==='function'){ const _da=desktopApplyState; desktopApplyState=function(){ const r=_da.apply(this,arguments); try{ loResume(); }catch(e){} return r; }; }
})();
function loResume(){ const r=csActive(); if(r&&r.openLoadout&&loGet(r.openLoadout, r)&&!LO){ csLoadWorking(r); loOpen(r.openLoadout, true); } else loUi(); }
loResume();
