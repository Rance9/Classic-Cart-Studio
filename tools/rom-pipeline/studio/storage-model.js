// ---- the NES Classic storage model: a line-by-line port of tools/companion/console_engine/storage_model.py (the installation plan's
//      storage budget), so the NES Classic Loadout editor's meter and the installation preflight compute the same bytes. The model's
//      figures (STORAGE_MODEL) are written into the page from the Python module at build time; test_storage_model.py proves the two
//      implementations return identical results. Pure: no page state, runs under node as well.
function ccsStorageCapacity(model){ const r=model.reference; return r.free_before_mod-r.root_reserved-r.saves_reserve; }
function ccsStorageEstimate(inp, model){ const pg=model.per_game; const per=pg.desktop+pg.cover+pg.cover_small+pg.metadata;
 let rom=0, files=0, added=0, unknown=0, ra=false;
 for(const g of (inp.games||[])){ if(!g.install) continue; added++; files+=per; const b=g.bytes; if(Number.isInteger(b)&&b>0) rom+=b; else unknown++; if(g.retroarch) ra=true; }
 const h=inp.home||{}, he=model.home_estimate; const homeMeasured=Number.isInteger(h.measured)&&h.measured>0;
 const home=homeMeasured?h.measured:he.base+he.per_game*Math.trunc(h.games||0);
 const comp={hakchi:inp.hakchi_present?0:model.components.hakchi, retroarch:ra&&!inp.retroarch_present?model.components.retroarch:0};
 const content=rom+files+home+comp.hakchi+comp.retroarch; const margin=model.margin.fixed+Math.trunc(model.margin.fraction*content);
 const used=content+margin; const free=inp.free; const available=Number.isInteger(free)?free:ccsStorageCapacity(model); const remaining=available-used;
 const status=remaining<0?'over':unknown?'unconfirmed':remaining<margin?'near':'fits';
 return {rom, files, added, unknown, home, home_measured:homeMeasured, components:comp, content, margin, used, available, remaining, status, source:Number.isInteger(free)?'device':'reference'}; }
if(typeof module!=='undefined') module.exports={ccsStorageEstimate, ccsStorageCapacity};
