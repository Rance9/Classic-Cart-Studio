// ---- NES Classic Loadouts (one implementation for the website and the Windows app) ----
//      Game Library → My Collections (any size, never limited by the console) → NES Classic Loadouts: named subsets of one collection that
//      the person plans to install on a physical NES Classic Edition. A loadout is a plan, never a claim about what a console holds.
//      A loadout keeps exact version ids that are all in its collection, its own revision decisions and its own console HOME settings
//      (folders, theme, HOME favorites); it references the collection's games and never copies game files. Loadouts are stored inside
//      their collection's record (`loadouts`), so exports, transfers, backups and shares carry them.
//      Editing a loadout opens it in the working copy (selection, decisions, HOME settings) while the collection waits in LO
//      (collections.js), so the Tray, Preview on TV, HOME Setup and Install on NES Classic all work on exactly that loadout.
//      Opening, switching or importing a collection never opens a loadout; Back to Collection always returns.
const LO_DEFAULT_NAME='My NES Classic Setup';
function loList(r){ r=r||csActive(); return (r&&Array.isArray(r.loadouts))?r.loadouts:[]; }
function loGet(lid, r){ return loList(r).find(l=>l.id===lid)||null; }
function loCur(){ return LO?loGet(LO.lid):null; }
function loNewId(){ return 'l'+csId().slice(1); }
function loUniqueName(name, except){ const base=(name||LO_DEFAULT_NAME).trim().slice(0,56)||LO_DEFAULT_NAME; const have=new Set(loList().filter(l=>l.id!==except).map(l=>l.name.toLowerCase())); if(!have.has(base.toLowerCase())) return base; for(let i=2;i<999;i++){ const n=`${base} ${i}`; if(!have.has(n.toLowerCase())) return n; } return base+' '+Date.now(); }
const loPl=(n,w)=>`${n.toLocaleString('en-US')} ${w}${n===1?'':'s'}`;
// ---- storage: the one NES Classic storage model (studio/storage-model.js, the same calculation as the installation plan) applied to
//      the loadout being edited. Inputs come from catalog metadata and what the Studio already knows (no file scanning): each game's file
//      size (16-byte header included), whether it is a built-in game kept as it is, whether it needs RetroArch, the native HOME package
//      when it has been prepared for this loadout on this computer. Shown only in the loadout editor. ----
function loStorage(){ const games=[], unknownT=[], noFile=[]; let built=0;
 sel.forEach(id=>{ const g=byId[id];
  if(g){ const inst=installRequired(g); if(!inst){ built++; games.push({install:false}); return; } const b=g.payload_size>0?g.payload_size+16:null; if(b==null) unknownT.push(g.display_title); if(g.source==='personal'&&g.file_present===false) noFile.push(g.display_title); games.push({bytes:b, install:true, retroarch:g.recommended_emulator==='RetroArch'}); }
  else { const x=csMissing(id); const b=x.size>0?x.size+16:null; if(b==null) unknownT.push(x.title); noFile.push(x.title); games.push({bytes:b, install:true, retroarch:false}); } });
 let measured=null; try{ if(typeof NHP!=='undefined'&&NHP.result&&NHP.result.ok&&NHP.fp===nhFp()){ const S=(NHP.result.manifest||{}).sizes||{}; const b=S.total_with_manifest||S.total; if(b>0) measured=b; } }catch(e){}
 const e=ccsStorageEstimate({games, home:{measured, games:sel.size}}, STORAGE_MODEL); e.n=sel.size; e.built=built; e.unknownTitles=unknownT; e.noFile=noFile; return e; }
const loMB=b=>(b/1e6).toLocaleString('en-US',{maximumFractionDigits:b<1e7?1:0})+' MB';   // decimal megabytes for display; bytes everywhere else
function loStatusText(e){ return e.status==='fits'?'Estimated to fit':e.status==='near'?`Close to capacity: about ${loMB(e.remaining)} remaining`:e.status==='over'?`Estimated to exceed available space by ${loMB(-e.remaining)}`:`Fit not confirmed: ${loPl(e.unknown,'game')} without a known size`; }
function loStatusIcon(e){ return {fits:'✓', near:'!', over:'✕', unconfirmed:'?'}[e.status]; }
function loStatusCls(e){ return {fits:'ok',near:'warn',over:'bad',unconfirmed:'warn'}[e.status]; }
function loLine(l){ const n=(LO&&l.id===LO.lid)?sel.size:(l.sel||[]).length; return loPl(n,'game'); }
// ---- open / close ----
function loResetConsoleState(){ try{ if(typeof TV!=='undefined'){ if(TV.open&&typeof closeTv==='function') closeTv(); TV.model=null; TV.proj=null; } }catch(e){} try{ if(typeof INST!=='undefined'){ INST.manifest=null; INST.preview=null; INST.prepared=null; } if(typeof CONSOLE!=='undefined'){ CONSOLE.plan=null; CONSOLE.preflight=null; CONSOLE.result=null; } if(typeof NHP!=='undefined'){ NHP.result=null; NHP.fp=null; } }catch(e){} }
function loWriteWorking(){ try{ localStorage.setItem(KEY, JSON.stringify([...sel])); localStorage.setItem(DKEY, JSON.stringify(decisions)); }catch(e){} try{ if(typeof desktopStateDirty==='function') desktopStateDirty(); }catch(e){} }
function loOpen(lid, quiet){ if(!CS) return; if(LO&&LO.lid===lid){ loUi(); return; } csSaveActiveNow(); const rec=CS.items[CS.active]; const l=loGet(lid, rec); if(!rec||!l) return;
 const coll=new Set(rec.sel||[]);
 LO={cid:rec.id, lid, collSel:coll, collDecisions:JSON.parse(JSON.stringify(rec.decisions||{})), collHome:rec.home?JSON.parse(JSON.stringify(rec.home)):null};
 sel=new Set((l.sel||[]).filter(id=>coll.has(id))); decisions=JSON.parse(JSON.stringify(l.decisions||{}));
 try{ if(l.home) localStorage.setItem(HOME_KEY, JSON.stringify(l.home)); else localStorage.removeItem(HOME_KEY); }catch(e){}
 rec.openLoadout=lid; csWrite(); loWriteWorking(); loResetConsoleState();
 render(); updateStatusLine(); try{ hkLabelUpdate(); }catch(e){} csMenuRefresh(); loUi(); if(!quiet) toast(`Editing NES Classic Loadout "${l.name}" · ${loPl(sel.size,'game')}`); }
function loClose(quiet){ if(!LO) return; csSaveActiveNow(); const rec=CS.items[CS.active]; if(!rec){ LO=null; return; } rec.openLoadout=null; csWrite(); loResetConsoleState(); csLoadWorking(rec); loWriteWorking(); if(!quiet) toast(`Back to "${rec.name}" · ${loPl(sel.size,'game')}`); }
// the whole collection, also while a loadout is open (collection-level actions)
function loAsCollection(fn){ if(!LO) return fn(); const s0=sel, d0=decisions; CS_COLLVIEW++; try{ sel=new Set(LO.collSel); decisions=LO.collDecisions; return fn(); } finally { sel=s0; decisions=d0; CS_COLLVIEW--; } }
// ---- create, rename, duplicate, delete ----
function loCreate(name, mode){ csSaveActiveNow(); const rec=CS.items[CS.active]; if(!rec) return null; const now=csNow(); const all=mode==='all'; const ids=all?(rec.sel||[]).slice():[];
 const dec={}; if(all) Object.entries(rec.decisions||{}).forEach(([k,v])=>{ if(ids.includes(k)) dec[k]=v; });
 const l={id:loNewId(), target:'nes-classic', name:loUniqueName(name), created:now, modified:now, sel:ids, decisions:dec, home:null, origin:all?'all':'choose'};
 rec.loadouts=loList(rec).concat([l]); csWrite(); loOpen(l.id, true);
 toast(all?`Created "${l.name}" with all ${loPl(ids.length,'game')}`:`Created "${l.name}". ${clickWord(1)} games to add them.`); return l; }
function loRename(lid, name){ const l=loGet(lid); if(!l) return; name=(name||'').trim().slice(0,60); if(!name) return; l.name=loUniqueName(name, lid); l.modified=csNow(); csWrite(); csDirty(); loUi(); }
function loDuplicate(lid){ if(LO&&LO.lid===lid) csSaveActiveNow(); const rec=CS.items[CS.active]; const src=loGet(lid, rec); if(!src) return null; const now=csNow();
 const c=Object.assign(JSON.parse(JSON.stringify(src)), {id:loNewId(), name:loUniqueName(src.name+' copy'), created:now, modified:now, origin:'duplicate'}); const i=rec.loadouts.indexOf(src); rec.loadouts.splice(i+1, 0, c); csWrite(); loUi(); toast(`Duplicated as "${c.name}" (${loPl(c.sel.length,'game')})`); return c; }
function loDelete(lid){ const rec=CS.items[CS.active]; const l=loGet(lid, rec); if(!l) return; if(LO&&LO.lid===lid) loClose(true); const r2=CS.items[CS.active]; r2.loadouts=loList(r2).filter(x=>x.id!==lid); csWrite(); loUi(); toast(`Deleted loadout "${l.name}"; the collection is unchanged`); }
// ---- the dialogs ----
function loNameField(v){ return `<div class="fld"><label for="loNm">Loadout name</label><input type="text" id="loNm" maxlength="60" value="${esc(v)}" spellcheck="false"></div>`; }
// one creation dialog (a name, then All N Collection Games or Choose Games) and the Choose a Loadout step of the console actions. A
// loadout is created only by Create Loadout; opening, canceling or closing creates nothing. `then` (the action that asked: Preview on
// TV, HOME Setup, Install on NES Classic) runs exactly once, for the loadout that was opened or created, and never after Cancel.
// opts: {newOnly, mode:'all'|'choose' (caller's preselection), then, lead, back (where Cancel returns, e.g. the manager)}
function loPrepare(opts){ opts=opts||{}; if(typeof closeVerPop==='function') closeVerPop(); document.querySelectorAll('.menu').forEach(x=>x.hidden=true); csSaveActiveNow(); const rec=CS.items[CS.active]; const list=loList(rec); const n=(rec.sel||[]).length;
 const mode=opts.mode==='all'&&n?'all':opts.mode==='choose'?'choose':(n?'all':'choose'); const then=typeof opts.then==='function'?opts.then:null; let done=false;
 const once=f=>{ if(done) return; done=true; f(); if(then) then(); };
 const choose=!opts.newOnly&&list.length;
 openColl(opts.newOnly?'New NES Classic Loadout':'Choose a Loadout', `"${rec.name}" · ${loPl(n,'game')}`, `${opts.lead?`<div class="msgbox">${esc(opts.lead)}</div>`:''}${choose?'':'<p class="small">An <b>NES Classic Loadout</b> is a named plan of games from this collection for your console. The collection itself never changes.</p>'}
  ${choose?`<div class="dsec" id="loListL">Your Loadouts</div><div class="lolist" role="group" aria-labelledby="loListL">${list.map(l=>miHtml({ic:'📺', label:esc(l.name), sub:`${loLine(l)} · changed ${esc(nowShort(l.modified))}`, role:'button', attrs:`data-lo-open="${esc(l.id)}"`, trail:'Open ›'})).join('')}</div><div class="dsec">Or Create a New One</div>`:''}
  <div class="${choose?'locreate':''}">${loNameField(loUniqueName(LO_DEFAULT_NAME))}
  <div class="lochoice" role="radiogroup" aria-label="Start the loadout with">
   <label class="lc${n?'':' off'}"><input type="radio" name="loMode" value="all" ${mode==='all'?'checked':''} ${n?'':'disabled'}><span class="mic" aria-hidden="true">🎮</span><span><b>${n?`All ${loPl(n,'Collection Game')}`:'All Collection Games'}</b><span class="small">${n?'Start with the whole collection, then remove games.':'This collection has no games yet, so a loadout starts empty.'}</span></span></label>
   <label class="lc"><input type="radio" name="loMode" value="choose" ${mode==='choose'?'checked':''}><span class="mic" aria-hidden="true">👆</span><span><b>Choose Games</b><span class="small">Start empty, then add games from this collection.</span></span></label>
  </div>
  <div class="acts"><span class="sp"></span><button class="btn" id="loNo" data-cancel>Cancel</button><button class="btn hk" id="loGo">Create Loadout</button></div></div>`);
 const inp=$('loNm'); inp.onkeydown=e=>{ if(e.key==='Enter') $('loGo').click(); }; if(!choose){ inp.focus(); inp.select(); }
 $('loNo').onclick=()=>{ done=true; if(typeof opts.back==='function') opts.back(); else closeColl(); };
 $('loGo').onclick=()=>{ const nm=inp.value.trim(); if(!nm){ inp.focus(); return; } const m=(document.querySelector('input[name=loMode]:checked')||{}).value||'choose'; once(()=>{ closeColl(); loCreate(nm, m); }); };
 $('collBody').onclick=e=>{ const b=e.target.closest('[data-lo-open]'); if(!b) return; once(()=>{ closeColl(); loOpen(b.dataset.loOpen); }); }; }
function loNameDialog(title, value, action, done, html, back){ openColl(title,'', `${html||''}${value!==null?loNameField(value):''}<div class="acts"><span class="sp"></span><button class="btn" id="loNo" data-cancel>Cancel</button><button class="btn ${action==='Delete'?'danger':'hk'}" id="loGo">${esc(action)}</button></div>`);
 const inp=$('loNm'); if(inp){ inp.focus(); inp.select(); inp.onkeydown=e=>{ if(e.key==='Enter') $('loGo').click(); }; }
 $('loNo').onclick=()=>{ if(back) back(); else closeColl(); }; $('loGo').onclick=()=>{ const v=inp?inp.value.trim():''; if(inp&&!v){ inp.focus(); return; } closeColl(); done(v); if(back) back(); }; }
function loRenameDialog(lid, back){ const l=loGet(lid); if(l) loNameDialog('Rename NES Classic Loadout', l.name, 'Rename', v=>loRename(lid, v), '', back); }
function loDeleteDialog(lid, back){ const l=loGet(lid); if(!l) return; const rec=csActive();
 loNameDialog(`Delete "${l.name}"?`, null, 'Delete', ()=>loDelete(lid), `<p class="small">The NES Classic Loadout "${esc(l.name)}" (${loPl((LO&&LO.lid===lid?sel.size:(l.sel||[]).length),'game')}) and its HOME settings will be deleted. The collection "${esc(rec.name)}" and its other loadouts stay as they are; nothing on a console changes.</p>`, back); }
// Manage Loadouts: every loadout of the open collection as a card (Open; Rename, Duplicate and Delete in its ⋯ menu)
function loManager(){ csSaveActiveNow(); const rec=csActive(); const list=loList(rec);
 openColl('Manage Loadouts', `"${rec.name}" · ${loPl(list.length,'loadout')}`, `<div class="dquick">${miHtml({ic:'✨', label:'New Loadout…', sub:(rec.sel||[]).length?`From the ${loPl((rec.sel||[]).length,'game')} in this collection`:'This collection has no games yet', cls:'new', attrs:'id="loNewB"'})}</div>
  ${list.length?`<div class="cslist" role="list">${list.map(l=>{ const on=LO&&LO.lid===l.id; return `<div class="csrow${on?' on':''}" role="listitem" data-lid="${esc(l.id)}"><span class="mic" aria-hidden="true">📺</span><div class="csn"><b>${esc(l.name)}</b><span class="small">${loLine(l)} · changed ${esc(nowShort(l.modified))}</span></div>${on?'<span class="opentag"><span aria-hidden="true">✓</span> Editing</span>':''}
   <div class="csacts">${on?'':`<button class="btn sm hk" data-a="open" aria-label="Open ${esc(l.name)}">Open</button>`}<span class="grow"></span>${rowMore(l.name)}</div></div>`; }).join('')}</div>`:'<div class="mempty"><b>No loadouts yet.</b><br>A loadout is a named plan of games from this collection for one NES Classic.</div>'}
  <p class="small">Each loadout is a separate plan for one NES Classic; changing one never changes another or the collection.</p>`);
 $('loNewB').onclick=()=>loPrepare({newOnly:true, back:loManager});
 rowMoreWire($('collBody'));
 $('collBody').onclick=e=>{ const b=e.target.closest('[data-a]'); if(!b) return; const row=b.closest('[data-lid]'); if(!row) return; const lid=row.dataset.lid; const a=b.dataset.a; if(['rename','dup','del'].includes(a)) ROW_RET=`.csrow[data-lid="${lid}"] details.csmore>summary`;
  if(a==='open'){ closeColl(); loOpen(lid); } else if(a==='rename'){ loRenameDialog(lid, loManager); } else if(a==='dup'){ loDuplicate(lid); loManager(); } else if(a==='del'){ loDeleteDialog(lid, loManager); } }; }
// what the estimate is made of, in plain words first; the reference figures on request
function loStorageDetails(){ const l=loCur(); if(!l) return; const e=loStorage(); const M=STORAGE_MODEL, R=M.reference;
 const row=(a,b)=>`<tr><td>${a}</td><td>${b}</td></tr>`;
 openColl('Will it fit?', `NES Classic Loadout "${l.name}"`, `<div class="msgbox ${loStatusCls(e)}"><b>${loStatusIcon(e)} ${esc(loStatusText(e))}.</b> Estimated use is about <b>${loMB(e.used)}</b> of about <b>${loMB(e.available)}</b> available on a stock NES Classic.${e.status==='over'?' Remove games from this loadout to bring it under the limit; the collection keeps them.':''}${e.status==='unconfirmed'?' Games without a known size are not counted, so the total may be low.':''}</div>
  <table class="pv lofit"><tbody>${row(`ROM files (${loPl(e.added,'game')} to add; ${e.built} built in, no file needed)`, loMB(e.rom))}${row('Cover art and menu entries', loMB(e.files))}${row(`HOME menu (${e.home_measured?'measured from the package prepared on this computer':'estimated until prepared in Install on NES Classic'})`, loMB(e.home))}${row('Hakchi (needed once per console)', loMB(e.components.hakchi))}${row(`RetroArch and emulator cores (${e.components.retroarch?'needed by some of these games':'not needed'})`, loMB(e.components.retroarch))}${row('Safety margin (file-system overhead)', loMB(e.margin))}${row('<b>Estimated use</b>', '<b>'+loMB(e.used)+'</b>')}${row('<b>Estimated available</b>', '<b>'+loMB(e.available)+'</b>')}${row(e.remaining>=0?'Estimated remaining':'Estimated excess', loMB(Math.abs(e.remaining)))}</tbody></table>
  ${e.unknownTitles.length?`<div class="msgbox warn"><b>${loPl(e.unknownTitles.length,'game')} without a known size</b>: ${esc(e.unknownTitles.slice(0,12).join(', '))}${e.unknownTitles.length>12?' …':''}</div>`:''}
  ${e.noFile.length?`<div class="msgbox warn"><b>${loPl(e.noFile.length,'ROM file')} not on this computer</b>: ${esc(e.noFile.slice(0,12).join(', '))}${e.noFile.length>12?' …':''}.${e.unknownTitles.length<e.noFile.length?' Their known sizes are counted.':''} Add the files with Add Your Own Game before installing.</div>`:''}
  <details class="lotech"><summary>Technical Details</summary><p class="small">An estimate from the Studio's reference model of a stock 2018 NES Classic Edition (CLV-101), not a measurement of your console. Available: ${loMB(R.free_before_mod)} free on the console's data partition before any change, minus ${loMB(R.root_reserved)} the file system reserves and ${loMB(R.saves_reserve)} kept for save data. Each added game costs its file (16-byte header included) plus about ${loMB(M.per_game.desktop+M.per_game.cover+M.per_game.cover_small+M.per_game.metadata)} of cover art and menu files; the HOME menu estimate is ${loMB(M.home_estimate.base)} plus ${loMB(M.home_estimate.per_game)} per game; Hakchi and RetroArch are counted once each when needed (${loMB(M.components.hakchi)}, ${loMB(M.components.retroarch)}); the safety margin is ${loMB(M.margin.fixed)} plus ${Math.round(M.margin.fraction*100)}%. The installation plan uses this same calculation, and checks capacity and every file again before anything is installed. MB = 1,000,000 bytes.</p></details>
  <div class="acts"><span class="sp"></span><button class="btn" id="loWhyOk">Close</button></div>`); $('loWhyOk').onclick=closeColl; }
// ---- removing a game from a collection while loadouts use it: an explicit choice, never a silent change ----
// prev = the collection's games before the change: a version counts as a replacement only when it was just added
let LO_RECON=false;
function loReconcile(prev){ if(LO||LO_RECON||!CS) return; const rec=csActive(); const list=loList(rec); if(!list.length) return; prev=prev||new Set(rec.sel||[]);
 const hit=list.map(l=>({l, gone:(l.sel||[]).filter(id=>!sel.has(id))})).filter(x=>x.gone.length); if(!hit.length) return;
 const titleOf2=id=>{ const g=byId[id]; return g?g.display_title:csMissing(id).title; };
 const swap=id=>{ const g=byId[id]; if(!g||!g.title_id) return null; const t=TITLES[g.title_id]; if(!t) return null; return t.versions.find(v=>v!==id&&sel.has(v)&&!prev.has(v))||null; };
 const all=[...new Set(hit.flatMap(x=>x.gone))]; const allSwap=all.every(id=>swap(id)); LO_RECON=true;
 openColl(allSwap?'Update NES Classic Loadouts?':'Remove from NES Classic Loadouts Too?', `"${rec.name}"`, `<p class="small">${allSwap?`You changed the version of ${loPl(all.length,'game')}; these loadouts still use the earlier one.`:`You removed ${loPl(all.length,'game')} that these loadouts still use. A loadout can only hold games from its collection.`}</p>
  <div class="lolist">${hit.map(x=>`<div class="lorow"><div class="lon"><b>${esc(x.l.name)}</b><span class="small">${x.gone.slice(0,10).map(id=>{ const s=swap(id); return esc(titleOf2(id))+(s?` → ${esc(shortVer(byId[s]))}`:''); }).join(', ')}${x.gone.length>10?` and ${x.gone.length-10} more`:''}</span></div></div>`).join('')}</div>
  <div class="acts"><button class="btn" id="loKeep">${allSwap?'Undo Change':'Keep in Collection'}</button><span class="sp"></span><button class="btn hk" id="loDrop">${allSwap?'Use New Version':'Remove from Loadouts'}</button></div>`);
 const keep=()=>{ if(!LO_RECON) return; LO_RECON=false; all.forEach(id=>sel.add(id)); if(allSwap) all.forEach(id=>{ const s=swap(id); if(s&&!COLL.multi[(byId[s]||{}).title_id]) sel.delete(s); }); persist(); render(); closeColl(); toast('Nothing removed: the collection and its loadouts are unchanged'); };
 $('loKeep').onclick=keep;   // closing the dialog any other way also keeps everything
 const mo=new MutationObserver(()=>{ if($('collm').classList.contains('open')) return; mo.disconnect(); if(LO_RECON) keep(); }); mo.observe($('collm'),{attributes:true, attributeFilter:['class']});
 $('loDrop').onclick=()=>{ LO_RECON=false; csSaveActiveNow(); hit.forEach(h=>{ const x={l:loGet(h.l.id), gone:h.gone}; if(!x.l) return; const out=[]; (x.l.sel||[]).forEach(id=>{ if(sel.has(id)){ if(!out.includes(id)) out.push(id); return; } const s=swap(id); if(s&&!out.includes(s)){ out.push(s); if(x.l.decisions&&x.l.decisions[id]) x.l.decisions[s]=x.l.decisions[id]; } if(x.l.decisions) delete x.l.decisions[id]; }); x.l.sel=out; x.l.modified=csNow(); if(x.l.home&&Array.isArray(x.l.home.folders)) x.l.home.folders.forEach(f=>{ if(Array.isArray(f.order)) f.order=f.order.map(id=>out.includes(id)?id:(swap(id)||id)).filter(id=>out.includes(id)); }); });
  csWrite(); closeColl(); csDirty(); loUi(); toast(allSwap?`Updated ${loPl(hit.length,'loadout')} to the new version`:`Removed from the collection and from ${loPl(hit.length,'loadout')}`); }; }
// while a loadout is open, a game it gains that is not in the collection yet (Add Your Own Game, a details checkbox) joins the collection too
function loAdopt(){ if(!LO) return; const extra=[...sel].filter(id=>!LO.collSel.has(id)); extra.forEach(id=>{ LO.collSel.add(id); if(LO.collDecisions[id]===undefined&&decisions[id]) LO.collDecisions[id]=decisions[id]; });
 [...sel].forEach(id=>{ if(decisions[id]===undefined&&LO.collDecisions[id]) decisions[id]=LO.collDecisions[id]; });
 if(extra.length){ const t=byId[extra[0]]; toast(`${extra.length===1?`"${t?t.display_title:extra[0]}"`:loPl(extra.length,'game')} also added to the collection "${COLL.name}"`); csMenuRefresh(); } }
(function(){ const _p=persist; persist=function(){ const r=_p.apply(this,arguments); try{ if(LO) loAdopt(); else { const prev=window.LO_PREV&&window.LO_PREV.cid===CS.active?window.LO_PREV.sel:null; window.LO_PREV={cid:CS.active, sel:new Set(sel)}; setTimeout(()=>loReconcile(prev),0); } }catch(e){} return r; }; })();
// ---- the library while a loadout is open: only the collection's games, shown in the collection's own versions; checked = in this loadout ----
(function(){ const _v=visible; visible=function(){ if(!LO) return _v.apply(this,arguments); return _visibleBase().filter(g=>LO.collSel.has(g.game_id)); };
 const _c=chosenVersion; chosenVersion=function(t){ if(LO){ for(const v of t.versions){ if(sel.has(v)) return v; } for(const v of t.versions){ if(LO.collSel.has(v)) return v; } } return _c(t); };
 const _o=openVerPop; openVerPop=function(btn){ _o(btn); if(!LO) return; vp.querySelectorAll('[data-pick]').forEach(b=>{ if(!LO.collSel.has(b.dataset.pick)) b.remove(); }); const ft=vp.querySelector('.ft'); if(ft) ft.innerHTML='<span class="small">Only versions in your collection are shown.</span>'; };
 const _d=openDetails; openDetails=function(id){ _d.apply(this,arguments); const box=$('detailsBox'); const cb=box&&box.querySelector('label input[type=checkbox][data-id]'); if(cb&&cb.parentNode) cb.parentNode.lastChild.nodeValue=LO?' In this loadout':' In this collection'; };
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
 loPrepare({then, lead:`${what} an NES Classic Loadout. Choose one, or create one.`}); }, true);
// ---- the interface ----
const LO_ICON='<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"><rect x="1.5" y="4.5" width="13" height="8" rx="1.2"/><path d="M4 9.5h3M10.5 9.5h1.5"/><path d="M5 4.5V3h6v1.5"/></svg>';
// the saved loadouts (the one being edited is checked), ONE New Loadout… (the dialog asks All N Collection Games or Choose Games),
// Manage Loadouts… for rename/duplicate/delete, and Back to Collection while one is open
function loMenuHtml(){ const rec=csActive(); if(!rec) return ''; const list=loList(rec); const n=csCollSel().size;
 return `<div class="mhead"><span class="mbadge" aria-hidden="true">${LO_ICON}</span><div class="mht"><b>NES Classic Loadouts</b><span>From "${esc(rec.name)}"</span></div><button type="button" class="mclose" aria-label="Close NES Classic Loadouts" title="Close">✕</button></div>
  ${list.length?`<div class="mlabel">${LO?'Switch Loadout':'Your Saved Loadouts'}</div><div class="mlist" role="group">${(LO?[loCur()].concat(list.filter(l=>l.id!==LO.lid)):list).filter(Boolean).slice(0,6).map(l=>{ const on=LO&&LO.lid===l.id; return miHtml({ic:'📺', label:esc(l.name), sub:loLine(l)+(on?' · editing now':''), role:'menuitemradio', attrs:`aria-checked="${!!on}" data-lo="${esc(l.id)}"`, trail:on?'✓':'›'}); }).join('')}${list.length>6?miHtml({ic:'🗂️', label:`${list.length-6} more in Manage Loadouts…`, cls:'quiet', attrs:'data-lo-act="manage"'}):''}</div>`
   :`<div class="mempty"><b>No loadouts yet.</b><br>${n?`Plan what goes on your NES Classic from the ${loPl(n,'game')} in this collection.`:'Add games to this collection, then plan what goes on your NES Classic.'}</div>`}
  <div class="sep"></div>${miHtml({ic:'✨', label:'New Loadout…', cls:'feature', attrs:'data-lo-new="1"', trail:'+'})}${list.length?miHtml({ic:'🗂️', label:'Manage Loadouts…', attrs:'data-lo-act="manage"', trail:'›'}):''}
  ${LO?`<div class="sep"></div>${miHtml({ic:'↩️', label:'Back to Collection', sub:`"${esc(rec.name)}" · ${loPl(n,'game')}`, attrs:'data-lo-act="back"'})}`:''}`; }
function loMenuClick(e){ const m=e.currentTarget; const q=e.target.closest('[data-lo]'); if(q){ m.hidden=true; loOpen(q.dataset.lo); return; }
 if(e.target.closest('[data-lo-new]')){ m.hidden=true; loPrepare({newOnly:true}); return; }
 const a=e.target.closest('[data-lo-act]'); if(!a) return; m.hidden=true; const act=a.dataset.loAct;
 if(act==='back') loClose(); else if(act==='manage') loManager(); }
function loUi(){ if(!CS) return; const on=!!LO; const rec=csActive(); document.body.classList.toggle('lomode', on);
 const list=loList(rec); const cx=$('loCtx'); if(cx) cx.textContent=list.length?`${list.length.toLocaleString('en-US')} saved`:'Choose or create one';
 ['homeBtn','hkBtn'].forEach(id=>{ const b=$(id); if(b) b.hidden=!on; }); const tv=$('tvBtn'); if(tv){ tv.hidden=false; tv.title=on?`Preview the HOME menu of "${(loCur()||{}).name||''}"`:'Preview a loadout\'s HOME menu'; } const w=$('loWrap'); if(w) w.hidden=on;
 const band=$('loBand'); if(band) band.hidden=!on;
 const l=loCur(); if(on&&l&&band){ const e=loStorage(); const pct=e.available>0?Math.min(100, e.used/e.available*100):100;
  $('loName').firstChild.nodeValue=l.name+' '; $('loFrom').innerHTML=`from <b>${esc(rec.name)}</b>`;
  $('loFig').innerHTML=`<b>${sel.size.toLocaleString('en-US')}</b> of ${loPl(csCollSel().size,'game')}`; if($('loCountM')) $('loCountM').innerHTML=$('loFig').innerHTML;
  const m=$('loMeter'); m.className='lometer '+loStatusCls(e); m.setAttribute('aria-valuenow', String(Math.round(pct))); m.setAttribute('aria-valuetext', `${loMB(e.used)} of ${loMB(e.available)} estimated, ${loStatusText(e)}`); m.firstElementChild.style.width=pct.toFixed(1)+'%';
  $('loUse').innerHTML=`Estimated <b>${loMB(e.used)}</b> used of <b>${loMB(e.available)}</b> available`;
  $('loStat').className='lost '+loStatusCls(e); $('loStat').textContent=loStatusIcon(e)+' '+loStatusText(e); }
 const hs=$('heroSelL'); if(hs) hs.textContent=on?'in loadout':'in collection';
 const sv=$('selVis'), us=$('unselVis'), sk=$('selKachi'), cl=$('clear'); const tgt=on?'this loadout':'the collection';
 // Game Library actions name their scope: the group label and each row's accessible name say collection or loadout
 document.querySelectorAll('[data-libscope]').forEach(x=>x.textContent=on?'In This Loadout':'In This Collection');
 document.querySelectorAll('[data-libfor]').forEach(x=>x.textContent=on?`For the loadout "${(l||{}).name||''}"`:`For "${(rec||{}).name||COLL.name}"`);
 if(sv){ miLabel(sv,'Add All Shown'); sv.setAttribute('aria-label',`Add All Shown to ${tgt}`); } if(us){ miLabel(us,'Remove All Shown'); us.setAttribute('aria-label',`Remove All Shown from ${tgt}`); }
 if(sk){ miLabel(sk,'Add Shown Console-Ready Games'); sk.setAttribute('aria-label',`Add Shown Console-Ready Games to ${tgt}`); sk.title=`Adds the shown games that are verified and run on the console's built-in emulator to ${tgt}`; }
 if(cl) miLabel(cl, on?'Empty This Loadout':'Remove All Games…');
 const th=$('trayHk'); if(th) th.textContent=on?'Install on NES Classic':'Install on NES Classic…'; const tx=$('trayExport'); if(tx) tx.hidden=on;
 const sto=$('storage'); if(sto) sto.hidden=true;
 const q=$('q'); if(q) q.placeholder='Search games';
 ['loMenu','loMenu2'].forEach(id=>{ const m=$(id); if(m&&!m.hidden) m.innerHTML=loMenuHtml(); }); }
// the tray, stats and title say which one is being edited
(function(){ const _t=renderTray; renderTray=function(){ const r=_t.apply(this,arguments); try{ const sub=$('traySub'), ul=$('trayList'); const l=loCur();
  if(ul) ul.querySelectorAll('[data-rm]').forEach(x=>x.title=l?'Remove from this loadout; it stays in the collection':'Remove from collection');
  if(!l&&sub){ const n=sel.size, own=[...sel].filter(id=>byId[id]&&byId[id].source==='personal').length, away=[...sel].filter(id=>!byId[id]).length, b=[...sel].filter(id=>byId[id]&&(byId[id].builtin_status||'').startsWith('BUILT-IN')).length; const nl=loList().length;
   sub.innerHTML=n?`<div class="small"><b style="color:#fff;font-size:13px">${loPl(n,'game')} in this collection</b>${b?` · ${b} built into the NES Classic`:''}${own?` · ${own} personal`:''}${away?` · ${away} ${typeof csAwayWord==='function'?csAwayWord():'not on this computer'}`:''}</div><div class="lotray small">${nl?`This collection has ${loPl(nl,'NES Classic Loadout')}. `:'Choose what goes on the console in an <b>NES Classic Loadout</b>. '}<button type="button" class="btn sm" data-lo-prep="1"><span aria-hidden="true">📺</span> Choose a Loadout…</button></div>`:''; }
  else if(l&&sub){ sub.insertAdjacentHTML('afterbegin', `<div class="lotray small">Planned for your NES Classic, from "${esc(COLL.name)}".</div>`); }
  const none=!l&&!sel.size; ['trayExport','trayHk'].forEach(id=>{ const b=$(id); if(b){ b.disabled=none; if(none) b.title='Add games to this collection first'; else b.removeAttribute('title'); } });
 }catch(e){} return r; }; })();
document.addEventListener('click',e=>{ const b=e.target.closest&&e.target.closest('[data-lo-prep]'); if(!b) return; $('tray').classList.remove('open'); loPrepare(); });
(function(){ const _u=updateStatusLine; updateStatusLine=function(){ const r=_u.apply(this,arguments); try{ const t=document.querySelector('#tray .ttl'); const l=loCur(); if(t&&l) t.lastChild.nodeValue=' NES Classic Loadout · '+l.name; }catch(e){} return r; }; })();
window.statsHtml=function(shown){ if(!LO) return `${shown.toLocaleString('en-US')} shown · <b>${loPl(sel.size,'game')} in collection</b> · <span class="mut">${window.libCountText?libCountText():''}</span> · ${fav.size} ★`;
 return `${shown.toLocaleString('en-US')} shown · <b>${sel.size.toLocaleString('en-US')} of ${loPl(csCollSel().size,'game')} in loadout</b> · ${fav.size} ★`; };
// Remove every game from the collection: asked first (Restore Previous Version keeps the earlier state)
document.addEventListener('click',e=>{ const b=e.target.closest&&e.target.closest('#clear'); if(!b||LO||!sel.size) return; if(!confirm(`Remove all ${loPl(sel.size,'game')} from the collection "${COLL.name}"? Restore Previous Version can bring them back.`)){ e.preventDefault(); e.stopImmediatePropagation(); document.querySelectorAll('.menu').forEach(x=>x.hidden=true); return; } try{ snapshotNow('before removing every game from "'+COLL.name+'"'); }catch(err){} }, true);
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
// one storage display: the tray's older storage notice and line are not shown; the meter updates with every change that can move it
storageTrayLine=function(){ return ''; };
storageNote=function(){ return LO?'This loadout is empty. '+clickWord(1)+' games to add them.':''; };
(function(){ if(typeof homeExtrasSave==='function'){ const h=homeExtrasSave; homeExtrasSave=function(){ const r=h.apply(this,arguments); try{ if(LO) loUi(); }catch(e){} return r; }; }
 if(typeof nhDraw==='function'){ const d=nhDraw; nhDraw=function(){ const r=d.apply(this,arguments); try{ if(LO) loUi(); }catch(e){} return r; }; } })();
