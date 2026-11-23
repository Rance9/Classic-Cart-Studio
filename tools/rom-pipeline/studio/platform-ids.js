// ---- The platform registry and its one decision rule, on the website and in the Windows app's page.
// Independent identifiers say what a file is for: game system, core, console model, console software, installation method, storage layout;
// plus controllers, boot options, save formats, drivers and capabilities. An entry is "supported" (this version may use it) or "reserved"
// (named, and denied everywhere). The registry text is tools/platform/ccs-platform-registry.json, put in by build_html.py; it is parsed
// afresh on every call and nothing is cached, so no page state, URL, imported file or setting can add an entry or change a status. The C#
// engine and the Windows app's backend (platform_ids.py) read the same file and decide tools/platform/target-vectors.json the same way.
//   ccsTargetResolve(doc, docKind, ...lists)  {ok, code, text, field, value, legacy, target}: a document's optional ccs_target (absent: the
//                                             document kind's NES defaults); items of the lists may carry game_system, which must be supported
//   ccsTargetRequest(req)                     the same for a request's arguments (ccs_target, the fields, capability, provider {kind, id}...)
//   ccsAllowed(kind, id)                      true only for a supported entry
// Part of Classic Cart Studio: GNU GPL v3 or later.
function ccsPlatformRegistryText(){ return __PLATFORM__; }
function ccsPlatformRegistry(){
 const r=JSON.parse(ccsPlatformRegistryText()); if(!r||r.schema!=='ccs-platform-registry/1') throw new Error('The platform registry is missing.');
 const entries=new Map(); for(const e of r.entries) entries.set(e.kind+'\u0000'+e.id, e);
 const defaults=new Map(); for(const k of Object.keys(r.document_defaults)){ const d=new Map(); for(const f of Object.keys(r.document_defaults[k])) if(typeof r.document_defaults[k][f]==='string') d.set(f, r.document_defaults[k][f]); defaults.set(k, d); }
 return {entries, defaults, list:r.entries};
}
// (functions, not constants: everything here is usable from any part of the page, whatever order the parts run in)
function ccsTargetFields(){ return [['game_system','game-system'],['core','core'],['physical_model','physical-model'],['firmware','firmware'],['method','method'],['layout','layout']]; }
function ccsRequestKeys(){ return [['capability','capability'],['boot_option','boot-option'],['input','input'],['save_adapter','save-adapter'],['driver','driver']]; }
function ccsTargetText(request){ return request?'This request names a game system, console or feature that this version does not support. Nothing was done.':'This file is for a game system or console that this version of Classic Cart Studio does not support, so it was not used. Nothing was changed.'; }
function ccsPfOwn(o,k){ return !!o&&typeof o==='object'&&Object.prototype.hasOwnProperty.call(o,k); }
function ccsPfName(v){ return typeof v==='string'&&v.length>0; }
function ccsPfEntry(R, kind, id){ return typeof kind==='string'&&typeof id==='string'?R.entries.get(kind+'\u0000'+id)||null:null; }
function ccsAllowed(kind, id){ const e=ccsPfEntry(ccsPlatformRegistry(), kind, id); return !!e&&e.status==='supported'; }
function ccsInputActions(id){ const e=ccsPfEntry(ccsPlatformRegistry(), 'input', id); return e&&Array.isArray(e.actions)?e.actions.slice():[]; }
function ccsModelForBoard(board){ return ccsModelFor(board, null); }
function ccsModelFor(board, region){ if(!ccsPfName(board)) return null; const e=ccsPlatformRegistry().list.find(x=>x.kind==='physical-model'&&x.status==='supported'&&Array.isArray(x.boards)&&x.boards.indexOf(board)>=0&&(!Array.isArray(x.regions)||!x.regions.length||(ccsPfName(region)&&x.regions.indexOf(region)>=0))); return e?e.id:null; }
function ccsTargetResolve(doc, docKind, ...lists){
 const R=ccsPlatformRegistry(); const defs=R.defaults.get(docKind); if(!defs) throw new Error('No defaults for document kind '+docKind);
 const target={}; defs.forEach((v,k)=>{ target[k]=v; });
 const d={ok:false, code:null, text:null, field:null, value:null, legacy:false, target};
 const deny=(code, field, value, text)=>{ d.ok=false; d.code=code; d.field=field||null; d.value=value===undefined?null:value; d.text=text||ccsTargetText(false); return d; };
 if(!doc||typeof doc!=='object'||Array.isArray(doc)) return deny('damaged');
 if(!ccsPfOwn(doc,'ccs_target')) d.legacy=true;
 else {
  const t=doc.ccs_target;
  if(!t||typeof t!=='object'||Array.isArray(t)) return deny('damaged','ccs_target');
  if(!ccsPfOwn(t,'schema')||t.schema!=='ccs-target/1') return deny('target-schema','schema', typeof t.schema==='string'?t.schema:null);
  for(const k of Object.keys(t)) if(k!=='schema'&&!ccsTargetFields().some(f=>f[0]===k)) return deny('unknown-field',k);
  for(const [f] of ccsTargetFields()){ if(!ccsPfOwn(t,f)) continue; if(!ccsPfName(t[f])) return deny('damaged',f); target[f]=t[f]; }
 }
 for(const [f,kind] of ccsTargetFields()){ if(!ccsPfOwn(target,f)) continue; const e=ccsPfEntry(R,kind,target[f]); if(!e) return deny('unknown-id',f,target[f]); if(e.status!=='supported') return deny('reserved',f,target[f]); }
 if(ccsPfOwn(target,'core')&&ccsPfOwn(target,'game_system')&&ccsPfEntry(R,'core',target.core).game_system!==target.game_system) return deny('inconsistent','core',target.core);
 if(ccsPfOwn(target,'physical_model')) for(const f of ['firmware','method','layout']) if(ccsPfOwn(target,f)&&ccsPfEntry(R,f,target[f]).physical_model!==target.physical_model) return deny('inconsistent',f,target[f]);
 for(const list of lists){
  if(!Array.isArray(list)) continue;
  for(const it of list){
   if(!it||typeof it!=='object'||!ccsPfOwn(it,'game_system')) continue;
   const v=it.game_system; if(!ccsPfName(v)) return deny('damaged','game_system');
   const e=ccsPfEntry(R,'game-system',v); if(!e) return deny('unknown-id','game_system',v); if(e.status!=='supported') return deny('reserved','game_system',v);
   if(ccsPfOwn(target,'game_system')&&target.game_system!==v) return deny('inconsistent','game_system',v);
  }
 }
 d.ok=true; d.code='ok'; return d;
}
function ccsTargetRequest(req){
 if(!req||typeof req!=='object'||Array.isArray(req)) return {ok:false, code:'damaged', text:ccsTargetText(true), field:null, value:null, legacy:false, target:{}};
 const r=ccsTargetResolve(ccsPfOwn(req,'ccs_target')?{ccs_target:req.ccs_target}:{}, 'worker-request');
 if(!r.ok){ r.text=ccsTargetText(true); return r; }
 const R=ccsPlatformRegistry(); const deny=(code, field, value)=>{ r.ok=false; r.code=code; r.field=field; r.value=value===undefined?null:value; r.text=ccsTargetText(true); return r; };
 for(const [f,kind] of ccsTargetFields().concat(ccsRequestKeys())){ if(!ccsPfOwn(req,f)) continue; const v=req[f]; if(!ccsPfName(v)) return deny('damaged',f); const e=ccsPfEntry(R,kind,v); if(!e) return deny('unknown-id',f,v); if(e.status!=='supported') return deny('reserved',f,v); }
 if(ccsPfOwn(req,'provider')){ const p=req.provider; if(!p||typeof p!=='object'||Array.isArray(p)||!ccsPfName(p.kind)||!ccsPfName(p.id)) return deny('damaged','provider'); const e=ccsPfEntry(R,p.kind,p.id); if(!e) return deny('unknown-id','provider',p.kind+'/'+p.id); if(e.status!=='supported') return deny('reserved','provider',p.kind+'/'+p.id); }
 r.ok=true; r.code='ok'; r.text=null; return r;
}
