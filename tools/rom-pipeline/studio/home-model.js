// ---- Console HOME data model (schema ccs-home-collection/1) -------------------------------------------------------------
// ONE installed-collection model for all four themes, Preview on TV, the Windows app and the future console runtime.
// Built from the Studio's collection manifest (nes-classic-collection/4) and the catalog; folders, favorites, launch history,
// map shortcuts, per-location sorts and the selected theme live beside it. Nothing here renders; nothing here invents data:
// a value the Studio does not know is null and stays null (callers omit it or say "Unknown").
//
// Invariants (spec 3 / 3.1 / 6 / 7):
//   order[]            the canonical All Games order (Windows-owned); never rewritten by a display sort
//   folders[].order    each folder's own game order (Windows-owned); games may be in many folders (ids only, no payload copies)
//   favorites.items    per-game revisions {on, rev, at}: reconciled per item, never as a whole list
//   history.events     launch events with unique ids; clears are generations (tombstone high-water mark)
//   sorts              display sorts per browsing location; view-only
const HOME_SCHEMA='ccs-home-collection/1';
const HOME_THEMES=['arcade-grid','box-art-showcase','featured-shelf','adventure-map'];
const HOME_DEFAULT_THEME='arcade-grid';
const HOME_SORTS=[{id:'custom',label:'Custom Order'},{id:'title-az',label:'Title A–Z'},{id:'title-za',label:'Title Z–A'},{id:'year-old',label:'Release Year: Oldest First'},{id:'year-new',label:'Release Year: Newest First'},{id:'pub-az',label:'Publisher A–Z'},{id:'pub-za',label:'Publisher Z–A'}];
const HOME_MAX_FOLDER_DEPTH=5;
const HOME_EMPTY={
 allgames:'No games installed yet. Use Classic Cart Studio for Windows to add games to your console.',
 favorites:'No favorites yet. Select a game and press SELECT to add it to your Favorites.',
 recent:'Your recently played games will appear here once you start playing.',
 folder:'This folder is empty. Add games using Classic Cart Studio for Windows.',
 category:'No installed games match this category.',
 search:'No matching games found'};

function homeSortTitle(t){ t=String(t||'').toLowerCase().trim(); return t.startsWith('the ')?t.slice(4):t; }
function homeNow(){ return new Date().toISOString(); }
function homeId(prefix){ return prefix+':'+Math.random().toString(36).slice(2,10)+Date.now().toString(36); }

// ---- build: manifest + catalog -> installed collection ----
// manifest.selections carry game_id / display_title / builtin_* / install_required / source; catalog rows (byId) carry
// publisher, year, genre, players, art, art_source, recommended_emulator, launch_command. `prev` is the previous model (to keep
// folders, favorites, history, sorts, theme, shortcuts); `opts.readiness` maps game_id -> 'ready'|'missing' when known.
function homeBuild(manifest, byId, prev, opts){
 opts=opts||{}; prev=prev||{}; const sels=(manifest&&manifest.selections)||[]; const games=[]; const seen=new Set();
 for(const s of sels){ const id=s.game_id||s.version_id; if(!id||seen.has(id)) continue; seen.add(id); const g=byId[id]||{};
  const installed=!s.install_required||opts.readiness===undefined?true:(opts.readiness[id]!=='missing');
  const artSrc=s.art||g.art||null; const prov=g.art_source==='none'||!artSrc?(opts.generated&&opts.generated[id]?'generated':'none'):(g.art_source==='uploaded'?'uploaded':'authentic');
  games.push({id, title_id:s.title_id||g.title_id||null, title:s.display_title||g.display_title||s.title||id, sort_title:homeSortTitle(s.display_title||g.display_title||s.title),
   version_id:s.payload_sha256||s.builtin_factory_version||id, version_label:s.nointro_name||s.version_label||(s.builtin_code&&!s.install_required?'factory version':''),
   publisher:g.publisher||null, developer:g.developer||null, genre:g.genre||null, players:g.players||null, year:g.year||null, description:g.description||null,
   art:{src:prov==='generated'?opts.generated[id]:artSrc, provenance:prov}, snap:g.art_snap||null, title_screen:g.art_title||null, hero:g.art_hero||null, kind:s.builtin_code&&!s.install_required?'factory':(s.source==='personal'?'personal':'added'),
   playable:installed, launch:{adapter:'mock', core:g.recommended_emulator||null, ref:id}}); }
 const order=games.map(g=>g.id);
 const m={schema:HOME_SCHEMA, generated:homeNow(), source:{schema:manifest&&manifest.schema||null, name:manifest&&manifest.name||null}, games, order,
  folders:homeKeepFolders(prev.folders||[], new Set(order)), favorites:homeKeepFavorites(prev.favorites, new Set(order)), history:prev.history||{events:[],cleared_gen:0,cleared_at:null},
  map_shortcuts:(prev.map_shortcuts||[null,null]).slice(0,2), sorts:Object.assign({},prev.sorts||{}), theme:HOME_THEMES.includes(prev.theme)?prev.theme:HOME_DEFAULT_THEME, settings:Object.assign({reduced_motion:false,hints:true},prev.settings||{})};
 if(m.map_shortcuts.length<2) m.map_shortcuts.push(null);
 homeDefaultShortcuts(m); return m;
}
function homeKeepFolders(folders, ids){ return folders.map(f=>Object.assign({},f,{order:(f.order||[]).filter(id=>ids.has(id)), children:(f.children||[]).slice()})); }
function homeKeepFavorites(fav, ids){ const out={gen:(fav&&fav.gen)||0, items:{}}; if(fav&&fav.items) for(const id in fav.items) if(ids.has(id)) out.items[id]=fav.items[id]; return out; }
function homeDefaultShortcuts(m){ const top=homeTopFolders(m); for(let k=0;k<2;k++){ const cur=m.map_shortcuts[k]; if(cur&&homeFolder(m,cur)) continue; const cand=top.filter(f=>!m.map_shortcuts.includes(f.id)); m.map_shortcuts[k]=cand.length?cand[0].id:null; } }

// ---- validation: problems are reported, never silently fixed ----
function homeValidate(m){
 const p=[]; if(!m||m.schema!==HOME_SCHEMA) return [`schema is not ${HOME_SCHEMA}`];
 const ids=new Set(); for(const g of m.games||[]){ if(!g.id) p.push('game without id'); else if(ids.has(g.id)) p.push(`duplicate game id ${g.id}`); else ids.add(g.id); if(!g.version_id) p.push(`game ${g.id} without version_id`); }
 if((m.order||[]).length!==ids.size||!m.order.every(id=>ids.has(id))) p.push('order does not list every installed game exactly once');
 const fids=new Set((m.folders||[]).map(f=>f.id));
 for(const f of m.folders||[]){ if(!f.id||!f.name) p.push('folder without id or name'); if(f.parent&&!fids.has(f.parent)) p.push(`folder ${f.id} has a missing parent`); for(const id of f.order||[]) if(!ids.has(id)) p.push(`folder ${f.name} lists uninstalled game ${id}`); const d=homeFolderDepth(m,f.id); if(d>HOME_MAX_FOLDER_DEPTH) p.push(`folder ${f.name} is nested ${d} deep (max ${HOME_MAX_FOLDER_DEPTH})`); if(d<0) p.push(`folder ${f.name} is in a parent cycle`); }
 for(const ev of (m.history&&m.history.events)||[]){ if(!ev.id||!ev.game||!ev.at) p.push('history event without id, game or time'); }
 return p;
}

// ---- folders (browsed on the console, edited only on Windows) ----
function homeFolder(m,id){ return (m.folders||[]).find(f=>f.id===id)||null; }
function homeTopFolders(m){ return (m.folders||[]).filter(f=>!f.parent).sort((a,b)=>(a.pos||0)-(b.pos||0)); }
function homeChildFolders(m,id){ return (m.folders||[]).filter(f=>f.parent===id).sort((a,b)=>(a.pos||0)-(b.pos||0)); }
function homeFolderDepth(m,id){ let d=0, cur=homeFolder(m,id), guard=0; while(cur){ d++; if(!cur.parent) return d; cur=homeFolder(m,cur.parent); if(++guard>64) return -1; } return d; }
function homeFolderPath(m,id){ const out=[]; let cur=homeFolder(m,id), guard=0; while(cur&&guard++<64){ out.unshift(cur); cur=cur.parent?homeFolder(m,cur.parent):null; } return out; }
function homeFolderCreate(m,name,parent){ if(parent&&homeFolderDepth(m,parent)>=HOME_MAX_FOLDER_DEPTH) throw new Error(`folders can be nested ${HOME_MAX_FOLDER_DEPTH} levels deep`); const f={id:homeId('folder'),name:String(name||'New folder').trim()||'New folder',parent:parent||null,order:[],children:[],pos:(m.folders||[]).filter(x=>(x.parent||null)===(parent||null)).length,art:{src:null,provenance:'generated'}}; m.folders.push(f); if(parent) homeFolder(m,parent).children.push(f.id); homeDefaultShortcuts(m); return f; }
function homeFolderRename(m,id,name){ const f=homeFolder(m,id); if(f) f.name=String(name||'').trim()||f.name; return f; }
function homeFolderDelete(m,id){ const f=homeFolder(m,id); if(!f) return; for(const c of homeChildFolders(m,id)) homeFolderDelete(m,c.id); m.folders=m.folders.filter(x=>x.id!==id); for(const x of m.folders) x.children=(x.children||[]).filter(c=>c!==id); m.map_shortcuts=m.map_shortcuts.map(s=>s===id?null:s); homeDefaultShortcuts(m); }
function homeFolderMove(m,id,newParent){ if(newParent===id) throw new Error('a folder cannot be its own parent'); for(const a of homeFolderPath(m,newParent)) if(a.id===id) throw new Error('a folder cannot be moved into its own subfolder'); const f=homeFolder(m,id); const subtree=homeFolderDepth(m,id)>=0?homeSubtreeDepth(m,id):1; if(newParent&&homeFolderDepth(m,newParent)+subtree>HOME_MAX_FOLDER_DEPTH) throw new Error(`that would nest deeper than ${HOME_MAX_FOLDER_DEPTH} levels`); if(f.parent) homeFolder(m,f.parent).children=homeFolder(m,f.parent).children.filter(c=>c!==id); f.parent=newParent||null; if(newParent) homeFolder(m,newParent).children.push(id); }
function homeSubtreeDepth(m,id){ const kids=homeChildFolders(m,id); return 1+(kids.length?Math.max(...kids.map(k=>homeSubtreeDepth(m,k.id))):0); }
function homeFolderAddGame(m,fid,gid){ const f=homeFolder(m,fid); if(f&&!f.order.includes(gid)&&m.order.includes(gid)) f.order.push(gid); }
function homeFolderRemoveGame(m,fid,gid){ const f=homeFolder(m,fid); if(f) f.order=f.order.filter(x=>x!==gid); }
function homeFolderMoveGame(m,fid,gid,delta){ const f=homeFolder(m,fid); if(!f) return; const i=f.order.indexOf(gid); const j=i+delta; if(i<0||j<0||j>=f.order.length) return; f.order.splice(i,1); f.order.splice(j,0,gid); }
function homeFolderGames(m,fid){ const f=homeFolder(m,fid); return f?f.order.filter(id=>m.order.includes(id)):[]; }
function homeFoldersOf(m,gid){ return (m.folders||[]).filter(f=>(f.order||[]).includes(gid)); }

// ---- automatic categories: only from metadata the installed games really have; canonical relative order ----
function homeCategories(m){
 const by=id=>m.games.find(g=>g.id===id); const groups=[]; const add=(kind,key,label)=>{ let c=groups.find(x=>x.kind===kind&&x.key===key); if(!c){ c={id:`cat:${kind}:${key}`,kind,key,label,order:[]}; groups.push(c); } return c; };
 for(const id of m.order){ const g=by(id); if(!g) continue;
  for(const ge of String(g.genre||'').split(',').map(x=>x.trim()).filter(Boolean)) add('genre',ge,ge).order.push(id);
  if(g.year) add('year',String(g.year),String(g.year)).order.push(id);
  if(g.publisher) add('publisher',g.publisher,g.publisher).order.push(id);
  if(g.players){ const n=String(g.players); const lab=/^1$/.test(n)?'1 player':(/2/.test(n)?'2 players':n+' players'); add('players',lab,lab).order.push(id); } }
 const kindOrder={genre:0,year:1,publisher:2,players:3};
 return groups.sort((a,b)=>kindOrder[a.kind]-kindOrder[b.kind]||(a.kind==='year'?b.key.localeCompare(a.key):a.label.localeCompare(b.label)));
}
function homeCategory(m,id){ return homeCategories(m).find(c=>c.id===id)||null; }

// ---- display sorts (view-only; unknown values last; ties keep the source order) ----
function homeSorted(m, ids, sortId){
 if(!sortId||sortId==='custom') return ids.slice();
 const by=new Map(m.games.map(g=>[g.id,g])); const idx=new Map(ids.map((id,i)=>[id,i]));
 const key=g=>{ if(sortId.startsWith('title')) return g.sort_title||null; if(sortId.startsWith('year')) return g.year?Number(g.year):null; return g.publisher?g.publisher.toLowerCase():null; };
 const desc=sortId.endsWith('-za')||sortId==='year-new';
 return ids.slice().sort((a,b)=>{ const ka=key(by.get(a)), kb=key(by.get(b)); const na=ka===null||ka===undefined||Number.isNaN(ka), nb=kb===null||kb===undefined||Number.isNaN(kb);
  if(na&&nb) return idx.get(a)-idx.get(b); if(na) return 1; if(nb) return -1; if(ka<kb) return desc?1:-1; if(ka>kb) return desc?-1:1; return idx.get(a)-idx.get(b); });
}
function homeSortFor(m, locKey){ return (m.sorts&&m.sorts[locKey])||'custom'; }
function homeSetSort(m, locKey, sortId){ if(!m.sorts) m.sorts={}; if(sortId==='custom') delete m.sorts[locKey]; else m.sorts[locKey]=sortId; }

// ---- favorites: per-item revisions ----
function homeIsFavorite(m,id){ const it=m.favorites&&m.favorites.items&&m.favorites.items[id]; return !!(it&&it.on); }
function homeSetFavorite(m,id,on,at){ if(!m.favorites) m.favorites={gen:0,items:{}}; const cur=m.favorites.items[id]||{on:false,rev:0}; if(!!cur.on===!!on) return cur; const it={on:!!on,rev:(cur.rev||0)+1,at:at||homeNow()}; m.favorites.items[id]=it; m.favorites.gen=(m.favorites.gen||0)+1; return it; }
function homeFavorites(m){ return m.order.filter(id=>homeIsFavorite(m,id)); }

// ---- launch history: events with unique ids; clearing is a generation with a high-water mark ----
function homeRecordLaunch(m,id,at,eventId){ if(!m.history) m.history={events:[],cleared_gen:0,cleared_at:null}; const ev={id:eventId||homeId('ev'),game:id,at:at||homeNow(),gen:m.history.cleared_gen||0}; m.history.events.push(ev); return ev; }
function homeHistoryEvents(m){ const h=m.history||{events:[]}; const g=h.cleared_gen||0; return (h.events||[]).filter(e=>(e.gen||0)>=g&&(!h.cleared_at||e.at>h.cleared_at)).sort((a,b)=>b.at<a.at?-1:b.at>a.at?1:(a.id<b.id?-1:1)); }
function homeRecent(m,n){ n=n||20; const out=[]; const seen=new Set(); for(const e of homeHistoryEvents(m)){ if(seen.has(e.game)) continue; seen.add(e.game); out.push(e.game); if(out.length>=n) break; } return out; }
function homeHistoryGroups(m){ const groups=[]; for(const e of homeHistoryEvents(m)){ const day=String(e.at).slice(0,10); let g=groups[groups.length-1]; if(!g||g.day!==day){ g={day,events:[]}; groups.push(g); } g.events.push(e); } return groups; }
function homeClearHistory(m,at){ if(!m.history) m.history={events:[],cleared_gen:0,cleared_at:null}; m.history.cleared_gen=(m.history.cleared_gen||0)+1; m.history.cleared_at=at||homeNow(); m.history.events=m.history.events.filter(e=>e.at>m.history.cleared_at); return m.history.cleared_gen; }
function homeGamePlayable(m,id){ const g=m.games.find(x=>x.id===id); return !!(g&&g.playable&&m.order.includes(id)); }

// ---- reconciliation (console <-> Windows), conflict-safe and per item; pure: returns the merged pieces ----
function homeReconcileFavorites(a,b){ const out={gen:Math.max((a&&a.gen)||0,(b&&b.gen)||0),items:{}}; const ids=new Set([...Object.keys((a&&a.items)||{}),...Object.keys((b&&b.items)||{})]);
 for(const id of ids){ const x=a&&a.items&&a.items[id], y=b&&b.items&&b.items[id]; if(!x) out.items[id]=y; else if(!y) out.items[id]=x; else if(x.rev!==y.rev) out.items[id]=x.rev>y.rev?x:y; else out.items[id]=(x.at||'')>=(y.at||'')?x:y; } return out; }
function homeReconcileHistory(a,b){ a=a||{events:[],cleared_gen:0,cleared_at:null}; b=b||{events:[],cleared_gen:0,cleared_at:null};
 const gen=Math.max(a.cleared_gen||0,b.cleared_gen||0); const at=[a.cleared_at,b.cleared_at].filter(Boolean).sort().pop()||null;   // the newest clear wins: a stale peer cannot resurrect cleared events
 const seen=new Map(); for(const e of [...(a.events||[]),...(b.events||[])]){ if(at&&e.at<=at) continue; if(!seen.has(e.id)) seen.set(e.id,e); }
 return {events:[...seen.values()].sort((x,y)=>x.at<y.at?-1:x.at>y.at?1:0).map(e=>Object.assign({},e,{gen})),cleared_gen:gen,cleared_at:at}; }
function homeReconcile(local, remote){ return {favorites:homeReconcileFavorites(local.favorites,remote.favorites), history:homeReconcileHistory(local.history,remote.history)}; }

// ---- removal: a game that left the collection must not remain launchable anywhere ----
function homeRemoveGame(m,id){ m.order=m.order.filter(x=>x!==id); m.games=m.games.filter(g=>g.id!==id); for(const f of m.folders||[]) f.order=(f.order||[]).filter(x=>x!==id); if(m.favorites&&m.favorites.items) delete m.favorites.items[id]; }

// ---- fixtures for development and tests (clearly synthetic) ----
function homeFixture(n, opts){ opts=opts||{}; const games=[]; const pubs=['Nintendo','Capcom','Konami','Tecmo','Sunsoft','Taito',null]; const genres=['Action','Platformer','Puzzle','Sports','Shooter','RPG',null];
 for(let i=0;i<n;i++){ const id='fx:'+String(i).padStart(4,'0'); const title=opts.titles&&opts.titles[i]||`Synthetic Game ${i+1}`; games.push({id,title_id:'t:'+id,title,sort_title:homeSortTitle(title),version_id:'v:'+id,version_label:'(USA)',publisher:pubs[i%pubs.length],developer:null,genre:genres[i%genres.length],players:i%3===0?'1':'2',year:i%9===8?null:String(1985+(i%10)),description:i%4===0?('A synthetic description for fixture game '+(i+1)+'. ').repeat(1+(i%5)):null,art:{src:null,provenance:'none'},kind:i%7===0?'factory':'added',playable:true,launch:{adapter:'mock',core:null,ref:id}}); }
 const m={schema:HOME_SCHEMA,generated:homeNow(),source:{schema:'fixture',name:`fixture-${n}`},games,order:games.map(g=>g.id),folders:[],favorites:{gen:0,items:{}},history:{events:[],cleared_gen:0,cleared_at:null},map_shortcuts:[null,null],sorts:{},theme:HOME_DEFAULT_THEME,settings:{reduced_motion:false,hints:true}};
 if(opts.folders&&n){ const a=homeFolderCreate(m,'Favorites of the 80s'); const b=homeFolderCreate(m,'Puzzle Night'); const c=homeFolderCreate(m,'Deep',a.id); for(const id of m.order.slice(0,Math.min(6,n))) homeFolderAddGame(m,a.id,id); for(const id of m.order.filter((x,i)=>i%2).slice(0,5)) homeFolderAddGame(m,b.id,id); for(const id of m.order.slice(0,2)) homeFolderAddGame(m,c.id,id); homeDefaultShortcuts(m); }
 if(opts.favorites&&n) for(const id of m.order.filter((x,i)=>i%5===0)) homeSetFavorite(m,id,true,'2026-10-01T00:00:00.000Z');
 if(opts.history&&n){ let t=Date.parse('2026-10-10T20:00:00Z'); for(let k=0;k<opts.history;k++){ homeRecordLaunch(m,m.order[(k*7)%n],new Date(t-k*3600000*5).toISOString(),'ev:'+k); } }
 return m; }

if(typeof module!=='undefined'&&module.exports){ module.exports={HOME_SCHEMA,HOME_THEMES,HOME_DEFAULT_THEME,HOME_SORTS,HOME_MAX_FOLDER_DEPTH,HOME_EMPTY,homeSortTitle,homeBuild,homeValidate,homeFolder,homeTopFolders,homeChildFolders,homeFolderDepth,homeFolderPath,homeFolderCreate,homeFolderRename,homeFolderDelete,homeFolderMove,homeFolderAddGame,homeFolderRemoveGame,homeFolderMoveGame,homeFolderGames,homeFoldersOf,homeCategories,homeCategory,homeSorted,homeSortFor,homeSetSort,homeIsFavorite,homeSetFavorite,homeFavorites,homeRecordLaunch,homeHistoryEvents,homeRecent,homeHistoryGroups,homeClearHistory,homeGamePlayable,homeReconcileFavorites,homeReconcileHistory,homeReconcile,homeRemoveGame,homeFixture,homeDefaultShortcuts}; }
