// ---- Console HOME navigation state machine (pixel-free, deterministic) ----------------------------------------------------
// Original controller only: up down left right a b start select. homeDispatch(S, model, action, env) returns {state, effects}.
// Theme geometry comes in through env.geometry (grid columns, carousel, shelf, map graph); pixels never do. Every rule of
// spec sections 4, 4.1 to 4.3, 5, 5.1, 7, 8, 9, 11 and 12 lives here so that one test suite covers all four themes.
//
// Sections ("where am I"): allgames | folder:<id> | category:<id> | favorites | recent | history | search | browse | settings
// Zones ("what has focus"): games (the listing) | nav (theme navigation) | map (landmarks) | results | suggest | keyboard |
//                            browse (folder/category tiles) | history | viewall | settings
// Overlays (above the section, context kept): start | theme | mapdest | details | confirm
const HOME_NAV=[{id:'allgames',label:'All Games'},{id:'browse',label:'Browse'},{id:'search',label:'Search'},{id:'favorites',label:'Favorites'},{id:'recent',label:'Recently Played'},{id:'settings',label:'Settings'}];
const HOME_START_MENU=[{id:'return',label:'Return to Games'},{id:'theme',label:'Change Theme'},{id:'sort',label:'Sort Games'},{id:'favorites',label:'Favorites'},{id:'recent',label:'Recently Played'},{id:'settings',label:'Settings'},{id:'allgames',label:'All Games'},{id:'browse',label:'Browse'},{id:'search',label:'Search'}];
const HOME_KEYS='ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789'.split('');   // six columns: A-Z then 0-9, then Space / Clear Search / Back
const HOME_KEY_COLS=6, HOME_KEY_ROWS=6; const HOME_UTIL=['space','clear','back'];
const HOME_REPEAT={delay:400,rate:110,fast_after:6,fast_rate:55};   // held D-pad: responsive, accelerates in long lists, never skips on a tap
const HOME_MARQUEE_DELAY=900;

function homeInitialState(theme){ return {v:1, theme:theme||'arcade-grid', section:{type:'allgames'}, zone:'games', overlay:null, index:0, navIndex:0, landmark:0, keyRow:0, keyCol:0, suggestIndex:0, resultIndex:0, browseIndex:0, historyIndex:0, settingsIndex:0, overlayIndex:0, descScroll:0, detailsFocus:'play', query:'', memo:{}, lastGame:null, launchCount:0, repeat:{button:null,since:0,count:0}}; }
function homeLocKey(sec){ return sec.type+(sec.id?':'+sec.id:''); }
function homeSortable(sec){ return ['allgames','folder','category','favorites'].includes(sec.type); }

// ---- listing for a section: ids in display order, plus the empty message ----
function homeListing(model, S, search){
 const sec=S.section; const sort=typeof homeSortFor==='function'?homeSortFor(model,homeLocKey(sec)):'custom'; const sorted=ids=>homeSorted(model,ids,sort);
 if(sec.type==='allgames') return {ids:sorted(model.order.slice()), empty:HOME_EMPTY.allgames};
 if(sec.type==='favorites') return {ids:sorted(homeFavorites(model)), empty:HOME_EMPTY.favorites};
 if(sec.type==='folder') return {ids:sorted(homeFolderGames(model,sec.id)), empty:HOME_EMPTY.folder};
 if(sec.type==='category'){ const c=homeCategory(model,sec.id); return {ids:sorted(c?c.order.slice():[]), empty:HOME_EMPTY.category}; }
 if(sec.type==='recent') return {ids:homeRecent(model,20), empty:HOME_EMPTY.recent};
 if(sec.type==='search') return homeSearchResults(model,S.query,search);
 return {ids:[], empty:''};
}
// search: relevance first (the shared engine's score 1..4), ties in master order; installed and playable games only
function homeSearchResults(model, query, search){
 const q=query?search.searchQuery(query):null; const cand=model.order.map(id=>model.games.find(g=>g.id===id)).filter(g=>g&&g.playable);
 if(!q) return {ids:cand.map(g=>g.id), empty:HOME_EMPTY.allgames, scored:false};
 const rows=cand.map(g=>({g,sc:0})); const fz=search.searchFuzzy(q,rows.map(r=>({display_title:r.g.title,nointro_name:r.g.version_label,publisher:r.g.publisher,year:r.g.year,genre:r.g.genre,_si:null,id:r.g.id})));
 const hits=[]; cand.forEach((g,i)=>{ const sc=search.searchScore({display_title:g.title,nointro_name:g.version_label,publisher:g.publisher,year:g.year,genre:g.genre},q,fz); if(sc) hits.push({id:g.id,sc,i}); });
 hits.sort((a,b)=>a.sc-b.sc||a.i-b.i); return {ids:hits.map(h=>h.id), empty:HOME_EMPTY.search, scored:true};
}
// suggestions: installed titles the shared engine already matches, best first, distinct, at most six; never an uninstalled title
function homeSuggestions(model, query, search){ if(!query) return []; const r=homeSearchResults(model,query,search); const out=[]; const seen=new Set(); for(const id of r.ids){ const g=model.games.find(x=>x.id===id); if(!g||seen.has(g.title)) continue; if(search.searchNorm(g.title)===search.searchNorm(query)) continue; seen.add(g.title); out.push({id,title:g.title}); if(out.length>=6) break; } return out; }

// ---- browse listing: folders first, then automatic categories ----
function homeBrowseItems(model, S){ const sec=S.section; const parent=sec.type==='browse'&&sec.id?sec.id:null; const folders=(parent?homeChildFolders(model,parent):homeTopFolders(model)).map(f=>({type:'folder',id:f.id,label:f.name,count:homeFolderGames(model,f.id).length,children:homeChildFolders(model,f.id).length})); const cats=parent?[]:homeCategories(model).map(c=>({type:'category',id:c.id,label:c.label,kind:c.kind,count:c.order.length})); return {folders,cats,items:[...folders,...cats]}; }

// ---- geometry helpers ----
function homeGridMove(index, n, cols, dir){ if(!n) return 0; const r=Math.floor(index/cols), c=index%cols; const rows=Math.ceil(n/cols);
 if(dir==='left') return c>0?index-1:index; if(dir==='right') return c<cols-1&&index+1<n?index+1:index;
 if(dir==='up') return r>0?index-cols:index; if(dir==='down'){ if(r>=rows-1) return index; const t=index+cols; return t<n?t:n-1; }   // short last row: nearest column, clamped
 return index; }
function homeLineMove(index,n,dir,before,after){ if(!n) return 0; if(dir===before) return index>0?index-1:index; if(dir===after) return index<n-1?index+1:index; return index; }

// ---- held-button repeat: pure timing over the simulation clock (ms) ----
function homeRepeatStep(S, button, now){ const r=S.repeat; if(r.button!==button){ S.repeat={button,since:now,count:0,next:now+HOME_REPEAT.delay}; return true; } if(now<r.next) return false; r.count++; r.next=now+(r.count>=HOME_REPEAT.fast_after?HOME_REPEAT.fast_rate:HOME_REPEAT.rate); return true; }
function homeRelease(S){ S.repeat={button:null,since:0,count:0}; }

// ---- the dispatcher ----
function homeDispatch(S0, model, action, env){
 const S=JSON.parse(JSON.stringify(S0)); const fx=[]; const search=env.search; const geo=env.geometry||{kind:'grid',cols:5}; const say=t=>fx.push({type:'say',text:t});
 const list=()=>homeListing(model,S,search); const remember=()=>{ S.memo[homeLocKey(S.section)]={index:S.index,zone:S.zone,scroll:S.scroll||0}; };
 const go=(sec,opts)=>{ remember(); S.section=sec; const m=S.memo[homeLocKey(sec)]; S.index=m?m.index:0; S.zone=sec.type==='search'?(m&&m.zone?m.zone:'keyboard'):sec.type==='browse'?'browse':sec.type==='history'?'history':sec.type==='settings'?'settings':'games'; S.overlay=null; if(opts&&opts.focus!==undefined){ const ids=list().ids; const k=ids.indexOf(opts.focus); if(k>=0) S.index=k; } fx.push({type:'persist'}); };
 const dest=id=>{ if(id==='allgames') go({type:'allgames'}); else if(id==='browse') go({type:'browse'}); else if(id==='search') go({type:'search'}); else if(id==='favorites') go({type:'favorites'}); else if(id==='recent') go({type:'recent'}); else if(id==='settings') go({type:'settings'}); else if(id==='history') go({type:'history'}); else if(id&&id.startsWith('folder:')) go({type:'folder',id:id.slice(7)}); };
 const currentGame=()=>{ if(S.section.type==='history'){ const ev=homeHistoryEvents(model)[S.historyIndex]; return ev?ev.game:null; } if(S.section.type==='search') return (list().ids)[S.resultIndex]||null; const ids=list().ids; return ids[S.index]||null; };
 const launch=id=>{ if(!id) return; if(!homeGamePlayable(model,id)){ say('This game is no longer installed and cannot be launched.'); return; } S.lastGame=id; S.launchCount++; fx.push({type:'launch',id}); fx.push({type:'persist'}); };
 const openDetails=id=>{ if(!id) return; S.overlay={type:'details',id,from:{zone:S.zone}}; S.detailsFocus='play'; S.descScroll=0; };

 // ---- overlays first ----
 if(S.overlay){ const o=S.overlay;
  if(o.type==='details'){ const g=model.games.find(x=>x.id===o.id); const hasDesc=!!(g&&g.description);
   if(action==='b'){ S.overlay=null; return {state:S,effects:fx}; }
   if(action==='start'){ S.overlay={type:'start',index:0,below:o}; return {state:S,effects:fx}; }
   if(action==='select') return {state:S,effects:fx};
   if(action==='up'){ if(S.detailsFocus==='desc'){ if(S.descScroll>0) S.descScroll--; else S.detailsFocus='fav'; } else if(S.detailsFocus==='fav') S.detailsFocus='play'; }
   else if(action==='down'){ if(S.detailsFocus==='play') S.detailsFocus='fav'; else if(S.detailsFocus==='fav'&&hasDesc) S.detailsFocus='desc'; else if(S.detailsFocus==='desc') S.descScroll++; }
   else if(action==='a'){ if(S.detailsFocus==='play'){ S.overlay=null; launch(o.id); } else if(S.detailsFocus==='fav'){ const on=!homeIsFavorite(model,o.id); fx.push({type:'favorite',id:o.id,on}); fx.push({type:'persist'}); say(on?'Added to Favorites':'Removed from Favorites'); } }
   return {state:S,effects:fx}; }
  if(o.type==='start'){ const items=HOME_START_MENU.filter(it=>it.id!=='sort'||homeSortable(S.section)); const n=items.length;
   if(action==='b'||action==='start'){ S.overlay=o.below||null; return {state:S,effects:fx}; }
   if(action==='up') o.index=Math.max(0,o.index-1); else if(action==='down') o.index=Math.min(n-1,o.index+1);
   else if(action==='a'){ const it=items[o.index]; if(it.id==='return') S.overlay=o.below||null; else if(it.id==='theme') S.overlay={type:'theme',index:HOME_THEMES.indexOf(S.theme),below:o.below||null}; else if(it.id==='sort'){ const cur=homeSortFor(model,homeLocKey(S.section)); S.overlay={type:'sort',index:Math.max(0,HOME_SORTS.findIndex(s=>s.id===cur))}; } else { S.overlay=null; dest(it.id); } }
   return {state:S,effects:fx}; }
  if(o.type==='theme'){ if(action==='b'||action==='start'){ S.overlay=o.below||null; return {state:S,effects:fx}; } if(action==='left'||action==='up') o.index=Math.max(0,o.index-1); else if(action==='right'||action==='down') o.index=Math.min(HOME_THEMES.length-1,o.index+1); else if(action==='a'){ S.theme=HOME_THEMES[o.index]; S.overlay=null; fx.push({type:'theme',id:S.theme}); fx.push({type:'persist'}); say('Theme: '+S.theme); } return {state:S,effects:fx}; }
  if(o.type==='sort'){ if(action==='b'||action==='start'){ S.overlay=null; return {state:S,effects:fx}; } if(action==='up') o.index=Math.max(0,o.index-1); else if(action==='down') o.index=Math.min(HOME_SORTS.length-1,o.index+1); else if(action==='a'){ const cur=currentGame(); fx.push({type:'sort',location:homeLocKey(S.section),sort:HOME_SORTS[o.index].id}); S.overlay=null; S.pendingFocus=cur; fx.push({type:'persist'}); } return {state:S,effects:fx}; }
  if(o.type==='mapdest'){ const items=HOME_NAV; if(action==='b'||action==='select'){ S.overlay=null; return {state:S,effects:fx}; } if(action==='start'){ S.overlay={type:'start',index:0,below:null}; return {state:S,effects:fx}; } if(action==='up') o.index=Math.max(0,o.index-1); else if(action==='down') o.index=Math.min(items.length-1,o.index+1); else if(action==='a'){ S.overlay=null; dest(items[o.index].id); } return {state:S,effects:fx}; }
  if(o.type==='confirm'){ if(action==='b'||action==='start'){ S.overlay=null; return {state:S,effects:fx}; } if(action==='left'||action==='right'||action==='up'||action==='down') o.index=o.index?0:1; else if(action==='a'){ if(o.index===1&&o.what==='clear-history'){ fx.push({type:'clear-history'}); say('Play history cleared on this console and, when connected, on Windows.'); } S.overlay=null; } return {state:S,effects:fx}; }
 }
 // ---- START anywhere opens the main menu overlay ----
 if(action==='start'){ S.overlay={type:'start',index:0,below:null}; return {state:S,effects:fx}; }

 const sec=S.section; const L=list(); const ids=L.ids; const n=ids.length;
 if(S.index>=n) S.index=Math.max(0,n-1);
 // ---- theme navigation zone (tabs / sidebar / top nav) ----
 if(S.zone==='nav'){ const navN=HOME_NAV.length;
  if(geo.navAxis==='vertical'){ if(action==='up') S.navIndex=Math.max(0,S.navIndex-1); else if(action==='down') S.navIndex=Math.min(navN-1,S.navIndex+1); else if(action==='right'||action==='b') S.zone=sec.type==='browse'?'browse':'games'; }
  else { if(action==='left') S.navIndex=Math.max(0,S.navIndex-1); else if(action==='right') S.navIndex=Math.min(navN-1,S.navIndex+1); else if(action==='down'||action==='b') S.zone=sec.type==='browse'?'browse':sec.type==='search'?'keyboard':'games'; }
  if(action==='a'){ dest(HOME_NAV[S.navIndex].id); } return {state:S,effects:fx}; }
 // ---- Adventure Map landmarks ----
 if(S.zone==='map'){ const graph=geo.map||{landmarks:[],neighbors:{}}; const lm=graph.landmarks; const cur=lm[S.landmark]||lm[0]; const nb=(graph.neighbors[cur&&cur.id]||{})[action];
  if(['up','down','left','right'].includes(action)){ if(nb){ const k=lm.findIndex(l=>l.id===nb); if(k>=0){ S.landmark=k; fx.push({type:'walk',to:nb}); } } else if(action==='down'){ S.zone='games'; } return {state:S,effects:fx}; }
  if(action==='a'){ const l=lm[S.landmark]; if(l){ if(l.dest&&l.dest.startsWith('folder:')&&!homeFolder(model,l.dest.slice(7))){ say('No folder is assigned to this shortcut. Assign one in Classic Cart Studio for Windows.'); } else dest(l.dest); } return {state:S,effects:fx}; }
  if(action==='select'){ S.overlay={type:'mapdest',index:0}; return {state:S,effects:fx}; }
  if(action==='b'){ const prev=S.memo.__prevSection; if(prev&&homeLocKey(prev)!==homeLocKey(sec)) go(prev); else S.zone='games'; return {state:S,effects:fx}; }
  return {state:S,effects:fx}; }
 // ---- Browse (folders first, then categories) ----
 if(sec.type==='browse'){ const B=homeBrowseItems(model,S); const items=B.items; const m=items.length; const cols=geo.browseCols||4;
  if(S.browseIndex>=m) S.browseIndex=Math.max(0,m-1);
  if(['up','down','left','right'].includes(action)){ const before=S.browseIndex; S.browseIndex=homeGridMove(S.browseIndex,m,cols,action); if(action==='up'&&S.browseIndex===before&&Math.floor(before/cols)===0) S.zone=!!geo.map?'map':'nav'; return {state:S,effects:fx}; }
  if(action==='left'&&geo.navAxis==='vertical'&&S.browseIndex%cols===0){ S.zone='nav'; return {state:S,effects:fx}; }
  if(action==='a'){ const it=items[S.browseIndex]; if(!it) return {state:S,effects:fx}; if(it.type==='folder'){ const kids=homeChildFolders(model,it.id); S.memo['browse-parent:'+it.id]={index:S.browseIndex}; if(kids.length) go({type:'browse',id:it.id}); else go({type:'folder',id:it.id}); } else go({type:'category',id:it.id}); return {state:S,effects:fx}; }
  if(action==='b'){ if(sec.id){ const f=homeFolder(model,sec.id); const up=f&&f.parent?{type:'browse',id:f.parent}:{type:'browse'}; const mem=S.memo['browse-parent:'+sec.id]; go(up); if(mem) S.browseIndex=mem.index; } else { const prev=S.memo.__prevSection; go(prev&&prev.type!=='browse'?prev:{type:'allgames'}); } return {state:S,effects:fx}; }
  if(action==='select'){ return {state:S,effects:fx}; }
  return {state:S,effects:fx}; }
 // ---- Search: three zones ----
 if(sec.type==='search'){ const sugg=homeSuggestions(model,S.query,search); const rn=n;
  if(S.zone==='keyboard'){ const rows=HOME_KEY_ROWS+1;
   if(action==='up'){ if(S.keyRow===0){ if(sugg.length){ S.zone='suggest'; S.suggestIndex=Math.min(S.suggestIndex,sugg.length-1); } else if(rn){ S.zone='results'; S.resultIndex=Math.min(S.resultIndex,rn-1); } } else S.keyRow--; }
   else if(action==='down'){ if(S.keyRow<rows-1) S.keyRow++; }
   else if(action==='left'){ if(S.keyRow===HOME_KEY_ROWS){ S.keyCol=Math.max(0,Math.floor(S.keyCol/2)*2-2); } else if(S.keyCol>0) S.keyCol--; else if(geo.navAxis==='vertical') S.zone='nav'; }
   else if(action==='right'){ if(S.keyRow===HOME_KEY_ROWS){ S.keyCol=Math.min(4,Math.floor(S.keyCol/2)*2+2); } else if(S.keyCol<HOME_KEY_COLS-1) S.keyCol++; }
   else if(action==='a'){ if(S.keyRow===HOME_KEY_ROWS){ const u=HOME_UTIL[Math.floor(S.keyCol/2)]; if(u==='space'){ if(S.query&&!S.query.endsWith(' ')) S.query+=' '; } else if(u==='clear'){ S.query=''; S.resultIndex=0; } else if(u==='back'){ const prev=S.memo.__prevSection; go(prev&&prev.type!=='search'?prev:{type:'allgames'}); return {state:S,effects:fx}; } } else { const k=HOME_KEYS[S.keyRow*HOME_KEY_COLS+S.keyCol]; if(k) S.query+=k.toLowerCase(); S.resultIndex=0; } fx.push({type:'persist'}); }
   else if(action==='b'){ if(S.query){ S.query=S.query.slice(0,-1); S.resultIndex=0; fx.push({type:'persist'}); } }
   else if(action==='select'){ if(rn){ S.zone='results'; S.resultIndex=0; } }
   return {state:S,effects:fx}; }
  if(S.zone==='suggest'){ const m=sugg.length; if(!m){ S.zone='keyboard'; return {state:S,effects:fx}; }
   if(action==='down'){ S.zone='keyboard'; } else if(action==='up'){ if(rn){ S.zone='results'; S.resultIndex=Math.min(S.resultIndex,rn-1); } }
   else if(action==='left') S.suggestIndex=Math.max(0,S.suggestIndex-1); else if(action==='right') S.suggestIndex=Math.min(m-1,S.suggestIndex+1);
   else if(action==='a'){ const s=sugg[Math.min(S.suggestIndex,m-1)]; S.query=s.title.toLowerCase(); const r=homeSearchResults(model,S.query,search); const k=r.ids.indexOf(s.id); S.resultIndex=k>=0?k:0; S.zone='results'; fx.push({type:'persist'}); }
   else if(action==='b'){ S.zone='keyboard'; } else if(action==='select'){ if(rn){ S.zone='results'; S.resultIndex=0; } }
   return {state:S,effects:fx}; }
  if(S.zone==='results'){ if(!rn){ S.zone='keyboard'; return {state:S,effects:fx}; } const cols=geo.resultCols||1; if(S.resultIndex>=rn) S.resultIndex=rn-1;
   if(action==='down'){ const t=homeGridMove(S.resultIndex,rn,cols,'down'); if(t===S.resultIndex){ S.zone=sugg.length?'suggest':'keyboard'; } else S.resultIndex=t; }
   else if(action==='up'){ const t=homeGridMove(S.resultIndex,rn,cols,'up'); if(t===S.resultIndex&&geo.navAxis!=='vertical') S.zone='nav'; else S.resultIndex=t; }
   else if(action==='left'||action==='right') S.resultIndex=homeGridMove(S.resultIndex,rn,cols,action);
   else if(action==='a') launch(ids[S.resultIndex]); else if(action==='select') openDetails(ids[S.resultIndex]); else if(action==='b') S.zone='keyboard';
   return {state:S,effects:fx}; }
 }
 // ---- Recently Played with the View All History control ----
 if(sec.type==='recent'){ if(S.zone==='viewall'){ if(action==='down'){ if(n) S.zone='games'; } else if(action==='up'){ S.zone=!!geo.map?'map':'nav'; } else if(action==='a') go({type:'history'}); else if(action==='b'){ const prev=S.memo.__prevSection; go(prev&&prev.type!=='recent'?prev:{type:'allgames'}); } return {state:S,effects:fx}; } }
 // ---- History timeline ----
 if(sec.type==='history'){ const evs=homeHistoryEvents(model); const m=evs.length; if(S.historyIndex>=m) S.historyIndex=Math.max(0,m-1);
  if(action==='up') S.historyIndex=Math.max(0,S.historyIndex-1); else if(action==='down') S.historyIndex=Math.min(Math.max(0,m-1),S.historyIndex+1);
  else if(action==='right'||action==='left'){ const day=i=>String(evs[i].at).slice(0,10); if(m){ let i=S.historyIndex; const d=day(i); if(action==='right'){ while(i<m-1&&day(i)===d) i++; if(day(i)===d) i=S.historyIndex; } else { while(i>0&&day(i)===d) i--; if(i<S.historyIndex){ const d2=day(i); while(i>0&&day(i-1)===d2) i--; } } S.historyIndex=i; } }
  else if(action==='a'){ const ev=evs[S.historyIndex]; if(ev) launch(ev.game); } else if(action==='select'){ const ev=evs[S.historyIndex]; if(ev) openDetails(ev.game); }
  else if(action==='b'){ go({type:'recent'}); S.zone='viewall'; }
  return {state:S,effects:fx}; }
 // ---- Settings ----
 if(sec.type==='settings'){ const items=homeSettingsItems(model); const m=items.length; if(S.settingsIndex>=m) S.settingsIndex=m-1;
  if(action==='up'){ if(S.settingsIndex>0) S.settingsIndex--; else S.zone=!!geo.map?'map':'nav'; } else if(action==='down') S.settingsIndex=Math.min(m-1,S.settingsIndex+1);
  else if(action==='left'&&geo.navAxis==='vertical') S.zone='nav';
  else if(action==='a'){ const it=items[S.settingsIndex]; if(it.id==='theme') S.overlay={type:'theme',index:HOME_THEMES.indexOf(S.theme),below:null}; else if(it.id==='reduced'){ fx.push({type:'setting',key:'reduced_motion',value:!model.settings.reduced_motion}); fx.push({type:'persist'}); } else if(it.id==='hints'){ fx.push({type:'setting',key:'hints',value:!model.settings.hints}); fx.push({type:'persist'}); } else if(it.id==='history') go({type:'history'}); else if(it.id==='clear') S.overlay={type:'confirm',what:'clear-history',index:0,text:'Clear Play History? This removes the launch history on this console and, when connected, in Classic Cart Studio for Windows. Favorites, games and folders are not affected. This cannot be undone.'}; else if(it.id==='sort'&&false){} }
  else if(action==='b'){ const prev=S.memo.__prevSection; go(prev&&prev.type!=='settings'?prev:{type:'allgames'}); }
  return {state:S,effects:fx}; }
 // ---- game listings (All Games, folder, category, Favorites, Recently Played) ----
 if(!n){ if(action==='up') S.zone=!!geo.map?'map':'nav'; else if(action==='b'&&sec.type!=='allgames'){ const prev=S.memo.__prevSection; go(prev&&homeLocKey(prev)!==homeLocKey(sec)?prev:{type:'allgames'}); } else if(action==='left'&&geo.navAxis==='vertical') S.zone='nav'; return {state:S,effects:fx}; }
 if(['up','down','left','right'].includes(action)){ const before=S.index;
  if(geo.kind==='grid'){ S.index=homeGridMove(S.index,n,geo.cols||5,action); if(action==='up'&&S.index===before&&Math.floor(before/(geo.cols||5))===0) S.zone='nav'; if(action==='left'&&S.index===before&&geo.navAxis==='vertical') S.zone='nav'; if(action==='up'&&sec.type==='recent'&&S.index===before) S.zone='viewall'; }
  else { S.index=homeLineMove(S.index,n,action,'left','right'); if(action==='up'){ if(sec.type==='recent') S.zone='viewall'; else S.zone=!!geo.map?'map':'nav'; } if(action==='left'&&S.index===before&&geo.navAxis==='vertical') S.zone='nav'; }
  if(S.index!==before) fx.push({type:'persist'}); return {state:S,effects:fx}; }
 if(action==='a') launch(ids[S.index]);
 else if(action==='select') openDetails(ids[S.index]);
 else if(action==='b'){ if(sec.type!=='allgames'){ const prev=S.memo.__prevSection; if(sec.type==='folder'){ const f=homeFolder(model,sec.id); go(f&&f.parent?{type:'browse',id:f.parent}:{type:'browse'}); const mem=S.memo['browse-parent:'+sec.id]; if(mem) S.browseIndex=mem.index; } else if(sec.type==='category') go({type:'browse'}); else go(prev&&homeLocKey(prev)!==homeLocKey(sec)?prev:{type:'allgames'}); } }
 return {state:S,effects:fx};
}
function homeSettingsItems(model){ return [{id:'theme',label:'Change Theme',group:'Appearance & Controls'},{id:'reduced',label:'Reduced motion',value:model.settings&&model.settings.reduced_motion?'On':'Off',group:'Appearance & Controls'},{id:'hints',label:'On-screen control hints',value:model.settings&&model.settings.hints!==false?'On':'Off',group:'Appearance & Controls'},{id:'history',label:'View All History',group:'Play History'},{id:'clear',label:'Clear Play History',group:'Play History'},{id:'info',label:'Information',value:'HOME runtime ccs-home/1 · model '+HOME_SCHEMA,group:'Information'}]; }
// after a model change (game removed, sort changed): clamp indexes, keep a pending focus, drop stale search focus
function homeReconcileState(S, model, search){ const L=homeListing(model,S,search); const n=L.ids.length; if(S.pendingFocus){ const k=L.ids.indexOf(S.pendingFocus); if(k>=0) S.index=k; delete S.pendingFocus; } if(S.index>=n) S.index=Math.max(0,n-1); if(S.resultIndex>=n) S.resultIndex=Math.max(0,n-1); if(S.overlay&&S.overlay.type==='details'&&!model.games.some(g=>g.id===S.overlay.id)) S.overlay=null; if(S.zone==='games'&&!n&&S.section.type!=='allgames'){} return S; }
// track "where did I come from" for B
function homeTrackPrev(Sbefore, Safter){ if(homeLocKey(Sbefore.section)!==homeLocKey(Safter.section)) Safter.memo.__prevSection=Sbefore.section; return Safter; }
function homeStep(S, model, action, env){ const r=homeDispatch(S,model,action,env); homeTrackPrev(S,r.state); return r; }

if(typeof module!=='undefined'&&module.exports){ module.exports={HOME_NAV,HOME_START_MENU,HOME_KEYS,HOME_KEY_COLS,HOME_KEY_ROWS,HOME_UTIL,HOME_REPEAT,HOME_MARQUEE_DELAY,homeInitialState,homeLocKey,homeSortable,homeListing,homeSearchResults,homeSuggestions,homeBrowseItems,homeGridMove,homeLineMove,homeRepeatStep,homeRelease,homeDispatch,homeStep,homeSettingsItems,homeReconcileState,homeTrackPrev}; }
