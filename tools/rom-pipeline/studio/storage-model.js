// ---- the console storage accounting: a line-by-line port of tools/companion/console_engine/storage_model.py, so the loadout meter,
//      the installation plan and the install checks compute the same bytes. The specification (STORAGE_MODEL) is
//      tools/platform/ccs-storage-spec.json, written into the page at build time; test_storage_model.py proves both implementations (and
//      the C# engine) decide tools/platform/storage-vectors.json identically. Pure: no page state, runs under node as well.
function ccsStorageInt(x){ return typeof x==='number'&&Number.isInteger(x); }
function ccsStorageRounded(n, block){ return n<=0?0:Math.ceil(n/block)*block; }
function ccsStorageAccount(inp, spec){ const pol=spec.policies, prof=inp.profile===undefined?null:inp.profile; const ref=spec.references.find(x=>x.profile===prof)||null;
 const st=inp.storage===undefined?null:inp.storage; const unknown=[]; let available, asrc;
 if(st===null){ if(ref){ available=ref.available_bytes; asrc='reference'; } else { available=null; asrc='unknown'; unknown.push('the space available on this console'); } }
 else { asrc=(st.source==='measured'||st.source==='simulated')?st.source:'measured';
  if(ccsStorageInt(st.available_bytes)) available=st.available_bytes;
  else if(ccsStorageInt(st.free_bytes)&&ccsStorageInt(st.reserved_bytes)) available=Math.max(0, st.free_bytes-st.reserved_bytes);
  else { available=null; unknown.push('the space available on this console'); } }
 let block, bsrc; if(st!==null&&ccsStorageInt(st.block_bytes)&&st.block_bytes>0){ block=st.block_bytes; bsrc='measured'; } else if(ref){ block=ref.block_bytes; bsrc='reference'; } else { block=pol.default_block_bytes; bsrc='default'; }
 const seen=new Set(), rows=[], steps=[];
 for(const it of (inp.items||[])){ let kind=(it.estimated||(it.estimate!==undefined&&it.estimate!==null))?'estimate':'exact'; let lg=0, al=0; const est=it.estimate===undefined?null:it.estimate;
  if(est!==null){ if(ccsStorageInt(est.logical_bytes)&&ccsStorageInt(est.allocated_bytes)){ lg+=est.logical_bytes; al+=est.allocated_bytes; } else kind='unknown'; }
  for(const f of (it.files||[])){ const k=f.key===undefined?null:f.key; if(k!==null){ if(seen.has(k)) continue; seen.add(k); } const b=f.bytes; if(!ccsStorageInt(b)||b<0){ kind='unknown'; continue; } lg+=b; al+=ccsStorageRounded(b, block); }
  let fr=0; for(const b of (it.replaces||[])) if(ccsStorageInt(b)) fr+=ccsStorageRounded(b, block);
  if(kind==='unknown') unknown.push(it.label||it.category||'a file');
  rows.push({category:it.category===undefined?null:it.category, label:it.label===undefined?null:it.label, kind, logical_bytes:lg, allocated_bytes:al, freed_bytes:fr, what:it.what===undefined?null:it.what}); steps.push([al, fr]); }
 let run=0, peak=0; for(const [a, fr] of steps){ peak=Math.max(peak, run+a); run+=a-fr; }
 let removed=0; const gone=new Set(); for(const r of (inp.removed_after||[])){ const rk=r.key===undefined?null:r.key; if(rk!==null){ if(gone.has(rk)) continue; gone.add(rk); } if(ccsStorageInt(r.allocated_bytes)) removed+=r.allocated_bytes; else unknown.push(r.label||'a copy to remove'); }
 run-=removed;
 const logical=rows.reduce((s,r)=>s+r.logical_bytes,0), allocated=rows.reduce((s,r)=>s+r.allocated_bytes,0);
 const nothing=allocated===0&&!rows.some(r=>r.kind==='unknown');
 const sv=pol.saves_allowance_bytes, saves=Object.prototype.hasOwnProperty.call(sv, prof)?sv[prof]:null;
 if(saves===null&&!nothing) unknown.push('room for saves and suspend points');
 const margin=nothing?0:pol.margin_fixed_bytes+Math.floor(allocated*pol.margin_fraction_ppm/1000000);
 const extra=nothing?0:(saves||0)+margin; const steady=run+extra, peakTotal=Math.max(peak, run)+extra;
 let status; if(available===null) status='unconfirmed'; else if(steady>available) status='over'; else if(peakTotal>available) status='over-at-peak'; else if(unknown.length) status='unconfirmed'; else if(available-peakTotal<margin) status='near'; else status='fits';
 return {schema:'ccs-storage-account/1', profile:prof, reference:ref&&asrc==='reference'?{date:ref.date, what:ref.what}:null, available_bytes:available, available_source:asrc, block_bytes:block, block_source:bsrc, rows,
  logical_bytes:logical, allocated_bytes:allocated, freed_bytes:rows.reduce((s,r)=>s+r.freed_bytes,0), removed_bytes:removed, saves_allowance_bytes:nothing?0:saves, margin_bytes:margin, steady_bytes:steady, peak_bytes:peakTotal,
  remaining_bytes:available===null?null:available-peakTotal, unknown, status, write_ok:status==='fits'||status==='near'}; }
function ccsStorageHomeEstimate(games, spec, music){ const h=spec.estimates.home, n=Math.max(0, Math.trunc(games)), off=music===false;   // music false: Include HOME music left out
 return {logical_bytes:h.fixed_logical_bytes+h.per_game_logical_bytes*n-(off?h.music_logical_bytes:0), allocated_bytes:h.fixed_allocated_bytes+h.per_game_allocated_bytes*n-(off?h.music_allocated_bytes:0)}; }
const CCS_NATIVE_COVER_ESTIMATES={standard:'native_entry', palette:'native_entry_held'};   // storage_model.py NATIVE_COVER_ESTIMATES
function ccsStorageNativeEntryFiles(spec, variant){ const e=spec.estimates[CCS_NATIVE_COVER_ESTIMATES[variant||'standard']]; return [e.desktop_bytes, e.cover_bytes, e.cover_small_bytes]; }
function ccsStorageCollectionItems(inp, spec){ const mod=inp.mod===undefined?true:inp.mod, native=inp.native_entries===undefined?!mod:inp.native_entries, es=spec.estimates;
 const est=e=>({logical_bytes:e.logical_bytes, allocated_bytes:e.allocated_bytes});
 const added=(inp.games||[]).filter(g=>g.install);
 const items=[{category:'game-files', label:'Game files', files:added.map(g=>({bytes:ccsStorageInt(g.bytes)&&g.bytes>0?g.bytes:null}))}];
 if(native&&added.length){ const nv=inp.native_covers||'standard', f=[]; for(const _ of added) for(const b of ccsStorageNativeEntryFiles(spec, nv)) f.push({bytes:b}); items.push({category:'native-entries', label:'Original Menu entries', estimated:true, what:es[CCS_NATIVE_COVER_ESTIMATES[nv]].what, files:f}); }
 if(mod){ const h=inp.home||{};
  if(ccsStorageInt(h.measured)&&h.measured>0){ const al=ccsStorageInt(h.allocated)&&h.allocated>=h.measured?h.allocated:null;
   items.push({category:'home', label:'CCS HOME', estimate:{logical_bytes:h.measured, allocated_bytes:al!==null?al:h.measured}, what:'measured: the package prepared on this computer'+(al!==null?'':" (its files' rounding to blocks is not known)")}); }
  else items.push({category:'home', label:'CCS HOME', estimate:ccsStorageHomeEstimate(h.games||0, spec, h.music!==false), what:es.home.what+(h.music!==false?'':' Include HOME music is left out: the song is not counted.')});
  items.push({category:'player', label:'Game player, startup pictures and notices', estimate:est(es.mod_envelope), what:es.mod_envelope.what}); }
 else items.push({category:'notices', label:'License notices', estimate:est(es.notices), what:es.notices.what});
 if(!inp.hakchi_present) items.push({category:'hakchi', label:'Hakchi (needed once per console)', estimate:est(es.hakchi_base), what:es.hakchi_base.what});
 return items; }
function ccsStorageEarlierKeep(value, spec){ const p=spec.policies; return ccsStorageInt(value)&&value>=0&&value<=p.earlier_home_versions_max?value:p.earlier_home_versions_default; }
function ccsStorageEstimate(inp, spec){ let storage=inp.storage===undefined?null:inp.storage; if(storage===null&&ccsStorageInt(inp.free)) storage={source:'measured', available_bytes:inp.free};
 const items=ccsStorageCollectionItems(inp, spec); const acc={profile:inp.profile||'nes-classic-edition', storage, items};
 const first=ccsStorageAccount(acc, spec); const fb={}; for(const r of first.rows) fb[r.category]=r; const mod=inp.mod===undefined?true:inp.mod;
 const part=mod?['home','player','game-files']:['game-files','notices'];
 const copy={logical_bytes:part.reduce((s,k)=>s+((fb[k]||{}).logical_bytes||0),0), allocated_bytes:part.reduce((s,k)=>s+((fb[k]||{}).allocated_bytes||0),0)};
 const keep=mod?ccsStorageEarlierKeep(inp.earlier===undefined?null:inp.earlier, spec):0;
 if(keep) items.push({category:'earlier-home', label:`Earlier CCS HOME versions kept (${keep})`, estimate:{logical_bytes:copy.logical_bytes*keep, allocated_bytes:copy.allocated_bytes*keep}, what:spec.policies.earlier_home_versions_what});
 items.push({category:'next-update', label:'Room to send the next update beside the one in use', estimate:{logical_bytes:copy.logical_bytes, allocated_bytes:copy.allocated_bytes}, what:'the new copy is sent and checked before the switch; the oldest copy goes only afterwards'});
 acc.removed_after=[{label:'the oldest copy, removed after the switch', allocated_bytes:copy.allocated_bytes}];
 const a=ccsStorageAccount(acc, spec);
 const by={}; for(const r of a.rows) by[r.category]=r; const added=(inp.games||[]).filter(g=>g.install);
 return Object.assign(a, {rom:by['game-files'].logical_bytes, added:added.length, unknown_games:added.filter(g=>!(ccsStorageInt(g.bytes)&&g.bytes>0)).length,
  files:(by['native-entries']||{}).logical_bytes||0, home:(by.home||{}).logical_bytes||0, home_measured:((by.home||{}).what||'').startsWith('measured'), player:(by.player||{}).logical_bytes||0,
  hakchi:(by.hakchi||{}).logical_bytes||0, earlier_versions:keep, copy_bytes:copy.logical_bytes, used:a.peak_bytes, available:a.available_bytes, remaining:a.remaining_bytes, source:a.available_source==='reference'?'reference':'device'}); }
function ccsStorageCapacity(profile, spec){ const r=spec.references.find(x=>x.profile===(profile||'nes-classic-edition')); return r?r.available_bytes:null; }
if(typeof module!=='undefined') module.exports={ccsStorageAccount, ccsStorageEstimate, ccsStorageEarlierKeep, ccsStorageCapacity, ccsStorageCollectionItems, ccsStorageHomeEstimate, ccsStorageRounded};
