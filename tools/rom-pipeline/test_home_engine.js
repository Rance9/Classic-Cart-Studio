// Console HOME engine suite (node): shared search engine, data model, navigation state machine. Pixel-free: the same rules
// drive all four themes, so this suite is the controller-contract proof (spec 18.1, 18.2, 18.3).   node test_home_engine.js
const fs=require('fs'), path=require('path'), vm=require('vm');
const ctx={console, module:{exports:{}}, Date, Math, JSON, Number, String, Object, Array, Set, Map, Error, RegExp, isFinite, parseFloat, parseInt};
ctx.global=ctx; vm.createContext(ctx);
for(const f of ['search.js','home-model.js','home-state.js']){ ctx.module={exports:{}}; vm.runInContext(fs.readFileSync(path.join(__dirname,'studio',f),'utf8'),ctx,{filename:f}); Object.assign(ctx,ctx.module.exports); }   // const declarations are script-scoped: take them from the exports
const H=ctx; const search={searchQuery:H.searchQuery,searchFuzzy:H.searchFuzzy,searchScore:H.searchScore,searchNorm:H.searchNorm};
let pass=0, fail=0; const out=[];
function ok(name, cond, note){ if(cond){ pass++; out.push('ok '+name); } else { fail++; out.push('FAIL '+name+(note?': '+JSON.stringify(note).slice(0,300):'')); } }
const env=(geometry)=>({search, geometry:geometry||{kind:'grid',cols:5}});
function run(S, model, actions, geometry){ let s=S; const fx=[]; for(const a of actions){ const r=H.homeStep(s,model,a,env(geometry)); s=r.state; fx.push(...r.effects); s=H.homeReconcileState(s,model,search); } return {s,fx}; }
const titles=['Super Mario Bros. 3','Dr. Mario','The Legend of Zelda','Zelda II: The Adventure of Link','Contra','Mega Man 2','Castlevania','Metroid','Kirby\'s Adventure','Punch-Out!!','Excitebike','Balloon Fight','Ice Climber','Donkey Kong','Galaga','Pac-Man','Tecmo Bowl','Double Dragon II','Ninja Gaiden','Final Fantasy','StarTropics','Super C','Ghosts\'n Goblins','Gradius','Bubble Bobble','Mario Bros.','Super Mario Bros.','Super Mario Bros. 2','Yoshi','Dr. Mario (Rev 1)'];

// ---- model ----
{ const m=H.homeFixture(30,{titles,folders:true,favorites:true,history:40}); ok('model_valid', H.homeValidate(m).length===0, H.homeValidate(m));
  ok('order_is_canonical', m.order.length===30 && m.order[0]==='fx:0000');
  ok('folders_independent_order', H.homeFolderGames(m,m.folders[0].id).length===6 && H.homeFolderGames(m,m.folders[1].id)[0]==='fx:0001');
  const fav=H.homeFavorites(m); ok('favorites_inherit_master_order', fav.join()===m.order.filter((x,i)=>i%5===0).join());
  const cats=H.homeCategories(m); ok('categories_only_from_real_metadata', cats.every(c=>c.order.length>0) && cats.some(c=>c.kind==='genre') && cats.some(c=>c.kind==='year') && !cats.some(c=>c.key==='null'));
  const yearCat=cats.find(c=>c.kind==='year'); ok('category_inherits_master_relative_order', yearCat.order.join()===m.order.filter(id=>yearCat.order.includes(id)).join());
  const az=H.homeSorted(m,m.order,'title-az'); const byId=id=>m.games.find(g=>g.id===id); ok('sort_title_az_the_ignored', byId(az[0]).title==='Balloon Fight' && az.indexOf('fx:0002')<az.indexOf('fx:0003') && byId(az[az.length-1]).title==='Zelda II: The Adventure of Link');
  const yo=H.homeSorted(m,m.order,'year-old'); const unknownYear=m.order.filter(id=>!byId(id).year); ok('sort_year_unknown_last_both_directions', unknownYear.every(id=>yo.indexOf(id)>=yo.length-unknownYear.length) && unknownYear.every(id=>H.homeSorted(m,m.order,'year-new').indexOf(id)>=yo.length-unknownYear.length));
  const pa=H.homeSorted(m,m.order,'pub-az'); const ties=pa.filter(id=>byId(id).publisher==='Nintendo'); ok('sort_ties_keep_source_order', ties.join()===m.order.filter(id=>byId(id).publisher==='Nintendo').join());
  ok('sort_is_view_only', H.homeSorted(m,m.order,'title-za').join()!==m.order.join() && m.order[0]==='fx:0000');
  ok('recent_20_unique_newest_first', H.homeRecent(m).length<=20 && new Set(H.homeRecent(m)).size===H.homeRecent(m).length && H.homeRecent(m)[0]==='fx:0000');
  const groups=H.homeHistoryGroups(m); ok('history_grouped_by_date_newest_first', groups.length>1 && groups[0].day>groups[1].day && groups.reduce((a,g)=>a+g.events.length,0)===40);
  H.homeSetFavorite(m,'fx:0001',true,'2026-10-02T00:00:00Z'); ok('favorite_toggle_revisions', m.favorites.items['fx:0001'].rev===1 && H.homeIsFavorite(m,'fx:0001'));
  const depthOk=(()=>{ try{ let p=null; for(let i=0;i<5;i++) p=H.homeFolderCreate(m,'L'+i,p).id; H.homeFolderCreate(m,'L6',p); return false; }catch(e){ return /5 levels/.test(e.message); } })(); ok('folder_depth_limited_to_5', depthOk);
  const before=JSON.stringify(m.order); H.homeFolderMoveGame(m,m.folders[0].id,'fx:0003',-2); ok('folder_reorder_does_not_touch_master', JSON.stringify(m.order)===before && H.homeFolderGames(m,m.folders[0].id)[1]==='fx:0003');
  H.homeRemoveGame(m,'fx:0001'); ok('removed_game_gone_everywhere', !m.order.includes('fx:0001') && !m.folders.some(f=>f.order.includes('fx:0001')) && !H.homeIsFavorite(m,'fx:0001') && H.homeValidate(m).length===0);
  // reconciliation
  const A=H.homeFixture(10,{history:5}), B=JSON.parse(JSON.stringify(A));
  H.homeSetFavorite(A,'fx:0002',true,'2026-10-11T10:00:00Z'); H.homeSetFavorite(B,'fx:0003',true,'2026-10-11T09:00:00Z'); H.homeSetFavorite(B,'fx:0002',true,'2026-10-11T08:00:00Z'); H.homeSetFavorite(B,'fx:0002',false,'2026-10-11T11:00:00Z');
  const R=H.homeReconcile(A,B); ok('favorites_per_item_merge', R.favorites.items['fx:0003'].on===true && R.favorites.items['fx:0002'].on===false && R.favorites.items['fx:0002'].rev===2);
  H.homeRecordLaunch(A,'fx:0004','2026-10-11T12:00:00Z','ev:a1'); H.homeRecordLaunch(B,'fx:0005','2026-10-11T12:30:00Z','ev:b1'); H.homeRecordLaunch(B,'fx:0004','2026-10-11T12:00:00Z','ev:a1');
  const R2=H.homeReconcileHistory(A.history,B.history); ok('history_union_dedupes_by_event_id', R2.events.filter(e=>e.id==='ev:a1').length===1 && R2.events.some(e=>e.id==='ev:b1') && R2.events.length===7);
  H.homeClearHistory(A,'2026-10-11T13:00:00Z'); H.homeRecordLaunch(A,'fx:0006','2026-10-11T14:00:00Z','ev:a2'); H.homeRecordLaunch(B,'fx:0007','2026-10-11T12:45:00Z','ev:b2');   // B is a stale peer that never saw the clear
  const R3=H.homeReconcileHistory(A.history,B.history); ok('clear_is_a_generation_stale_peer_cannot_resurrect', R3.cleared_gen===1 && R3.events.length===1 && R3.events[0].id==='ev:a2');
  const R3b=H.homeReconcileHistory(B.history,A.history); ok('clear_reconciliation_is_symmetric', JSON.stringify(R3b.events.map(e=>e.id))===JSON.stringify(R3.events.map(e=>e.id)));
  ok('clear_does_not_touch_favorites', R.favorites.items['fx:0003'].on===true && A.favorites.items['fx:0002'].on===true);
}
// ---- search parity: the HOME engine IS the website engine (same module) ----
{ const m=H.homeFixture(30,{titles}); const S=H.homeInitialState(); S.section={type:'search'}; const q=t=>H.homeSearchResults(m,t,search).ids.map(id=>m.games.find(g=>g.id===id).title);
  ok('search_smb_acronym_master_order_ties', q('smb').slice(0,3).join('|')==='Super Mario Bros. 3|Super Mario Bros.|Super Mario Bros. 2');
  ok('search_out_of_order', q('mario super').includes('Super Mario Bros. 3') && q('mario super').includes('Super Mario Bros.'));
  ok('search_abbrev_both_ways', q('dr')[0].startsWith('Dr. Mario') && q('doctor')[0].startsWith('Dr. Mario'));
  ok('search_typo_tolerated', q('contrra').includes('Contra'));
  ok('search_partial_progressive', q('zel').length===2 && q('zelda ii')[0]==='Zelda II: The Adventure of Link');
  ok('search_relevance_then_master_order', q('mario')[0]==='Mario Bros.' && q('mario').indexOf('Super Mario Bros. 3')<q('mario').indexOf('Super Mario Bros.'));
  const web=t=>{ const Q=H.searchQuery(t); const rows=m.games.map(g=>({display_title:g.title,nointro_name:g.version_label,publisher:g.publisher,year:g.year,genre:g.genre})); const fz=H.searchFuzzy(Q,rows); return rows.map((r,i)=>({t:r.display_title,sc:H.searchScore(r,Q,fz),i})).filter(x=>x.sc).sort((a,b)=>a.sc-b.sc||a.i-b.i).map(x=>x.t); };
  let same=0; const QS=['smb','mario super','dr','doctor','contrra','zel','mega','kirby','punch out','final','ghosts','castle','x']; for(const t of QS) if(JSON.stringify(web(t))===JSON.stringify(q(t))) same++; ok('search_parity_with_website_engine', same===QS.length, {same, of:QS.length});
  m.games[0].playable=false; ok('search_excludes_unplayable', !q('smb').includes('Super Mario Bros. 3'));
  const sg=H.homeSuggestions(m,'zel',search); ok('suggestions_installed_titles_only', sg.length===2 && sg.every(s=>m.games.some(g=>g.id===s.id)));
}
// ---- navigation: grid ----
{ const m=H.homeFixture(23,{titles}); let S=H.homeInitialState(); const g={kind:'grid',cols:5};
  ok('first_run_allgames_arcade', S.section.type==='allgames' && S.theme==='arcade-grid' && S.zone==='games');
  let r=run(S,m,['right','right','right','right','right'],g); ok('right_stops_at_row_end', r.s.index===4);
  r=run(r.s,m,['down','down','down','down','down'],g); ok('down_clamps_short_last_row', r.s.index===22);
  r=run(r.s,m,['right'],g); ok('no_wrap_at_last', r.s.index===22);
  r=run(r.s,m,['up'],g); ok('up_keeps_column', r.s.index===17);
  r=run(H.homeInitialState(),m,['left'],g); ok('left_at_row_start_stays', r.s.index===0);
  r=run(H.homeInitialState(),m,['up'],g); ok('up_from_top_row_enters_nav', r.s.zone==='nav');
  r=run(r.s,m,['right','right','a'],g); ok('nav_a_opens_destination', r.s.section.type==='search' && r.s.zone==='keyboard');
  r=run(H.homeInitialState(),m,['down','right','a'],g); ok('a_launches_immediately', r.fx.some(f=>f.type==='launch'&&f.id==='fx:0006') && !r.s.overlay);
  r=run(H.homeInitialState(),m,['select'],g); ok('select_opens_details_play_default', r.s.overlay&&r.s.overlay.type==='details' && r.s.detailsFocus==='play');
  r=run(r.s,m,['down','a'],g); ok('details_fav_toggle_stays', r.fx.some(f=>f.type==='favorite'&&f.on===true) && r.s.overlay && r.s.overlay.type==='details');
  r=run(r.s,m,['b'],g); ok('details_b_returns_exact_position', !r.s.overlay && r.s.index===0 && r.s.section.type==='allgames');
  r=run(H.homeInitialState(),m,['down','down','start'],g); ok('start_opens_overlay_context_kept', r.s.overlay&&r.s.overlay.type==='start' && r.s.index===10);
  r=run(r.s,m,['b'],g); ok('start_b_closes_restores', !r.s.overlay && r.s.index===10);
  r=run(H.homeInitialState(),m,['start','start'],g); ok('start_start_closes', !r.s.overlay);
  r=run(H.homeInitialState(),m,['start','down','a'],g); ok('change_theme_from_start', r.s.overlay&&r.s.overlay.type==='theme');
  r=run(r.s,m,['right','a'],g); ok('theme_a_applies_immediately', r.s.theme==='box-art-showcase' && r.fx.some(f=>f.type==='theme') && !r.s.overlay && r.s.section.type==='allgames');
  r=run(H.homeInitialState(),m,['start','down','down','a'],g); ok('sort_games_from_start_when_sortable', r.s.overlay&&r.s.overlay.type==='sort');
  r=run(r.s,m,['down','a'],g); ok('sort_choice_is_effect_not_mutation', r.fx.some(f=>f.type==='sort'&&f.sort==='title-az'&&f.location==='allgames') && m.order[0]==='fx:0000');
  let S2=H.homeInitialState(); S2.section={type:'search'}; S2.zone='keyboard'; const items=H.HOME_START_MENU.filter(it=>it.id!=='sort'||H.homeSortable(S2.section)); ok('sort_absent_in_search_context', !items.some(it=>it.id==='sort'));
  r=run(H.homeInitialState(),m,['start','down','down','down','a'],g); ok('favorites_from_start', r.s.section.type==='favorites');
  ok('empty_favorites_message', H.homeListing(m,r.s,search).ids.length===0 && H.homeListing(m,r.s,search).empty.startsWith('No favorites yet'));
  r=run(r.s,m,['b'],g); ok('b_returns_to_previous_section', r.s.section.type==='allgames');
}
// ---- position memory, removal, zero/one game ----
{ const m=H.homeFixture(12,{titles}); const g={kind:'grid',cols:5}; let r=run(H.homeInitialState(),m,['down','right','right'],g); const pos=r.s.index;
  r=run(r.s,m,['start','down','down','down','a'],g); r=run(r.s,m,['b'],g); ok('position_restored_after_section_change', r.s.index===pos);
  const S=r.s; S.theme='featured-shelf'; ok('position_survives_theme_change', S.index===pos);
  H.homeRemoveGame(m,m.order[pos]); const S3=H.homeReconcileState(JSON.parse(JSON.stringify(S)),m,search); ok('removal_selects_nearest_surviving', S3.index===pos && H.homeListing(m,S3,search).ids.length===11);
  const m0=H.homeFixture(0); let r0=run(H.homeInitialState(),m0,['a','right','down','select'],g); ok('zero_games_no_launch_no_crash', !r0.fx.some(f=>f.type==='launch') && !r0.s.overlay && H.homeListing(m0,r0.s,search).empty.startsWith('No games installed'));
  const m1=H.homeFixture(1); let r1=run(H.homeInitialState(),m1,['right','left','down','a'],{kind:'carousel'}); ok('one_game_no_wrap_launches', r1.s.index===0 && r1.fx.some(f=>f.type==='launch'));
}
// ---- search zones and keyboard ----
{ const m=H.homeFixture(30,{titles}); const g={kind:'grid',cols:5,resultCols:4}; let S=H.homeInitialState(); S.section={type:'search'}; S.zone='keyboard';
  ok('keyboard_six_columns', H.HOME_KEYS.length===36 && H.HOME_KEY_COLS===6 && H.HOME_KEY_ROWS===6 && H.HOME_UTIL.join()==='space,clear,back');
  let r=run(S,m,['a'],g); ok('type_a', r.s.query==='a');
  r=run(S,m,['down','down','down','right','a'],g); ok('type_t', r.s.query==='t');   // row 3 (S T U V W X), col 1 = T
  let z=run(S,m,['down','down','down','down','right','a','up','up','up','up','right','right','right','a','down','right','a'],g); ok('type_zel_progressive', z.s.query==='zel', z.s.query);
  r=run(z.s,m,['b'],g); ok('b_deletes_last_char', r.s.query==='ze');
  r=run(S,m,['b'],g); ok('b_on_empty_is_noop', r.s.query==='' && r.s.zone==='keyboard');
  r=run(z.s,m,['select'],g); ok('select_on_keyboard_jumps_first_result', r.s.zone==='results' && r.s.resultIndex===0);
  r=run(r.s,m,['a'],g); ok('result_a_launches', r.fx.some(f=>f.type==='launch'));
  r=run(z.s,m,['up','up'],g); ok('up_from_keyboard_enters_suggestions', r.s.zone==='suggest');
  r=run(r.s,m,['a'],g); ok('suggestion_a_completes_and_focuses_no_launch', r.s.query==='zelda ii: the adventure of link' && r.s.zone==='results' && r.s.resultIndex===0 && !r.fx.some(f=>f.type==='launch'), r.s.query);
  r=run(r.s,m,['a'],g); ok('then_a_launches_highlighted', r.fx.some(f=>f.type==='launch'));
  r=run(z.s,m,['up','up','select'],g); ok('select_on_suggestion_jumps_without_accepting', r.s.zone==='results' && r.s.query==='zel');
  r=run(z.s,m,['up','up','b'],g); ok('b_on_suggestion_returns_keyboard_query_intact', r.s.zone==='keyboard' && r.s.query==='zel');
  r=run(z.s,m,['up','up','up','select'],g); ok('result_select_opens_details', r.s.overlay&&r.s.overlay.type==='details');
  r=run(z.s,m,['up','up','up','b'],g); ok('b_on_results_returns_keyboard', r.s.zone==='keyboard');
  let nores=JSON.parse(JSON.stringify(z.s)); nores.query='qqqq'; r=run(nores,m,['up'],g); ok('no_results_keeps_query_and_keyboard', r.s.query==='qqqq' && r.s.zone==='keyboard' && H.homeListing(m,r.s,search).empty==='No matching games found');
  r=run(z.s,m,['down','down','down','down','down','left','a'],g); ok('clear_search_resets_to_master_order', r.s.query==='' && H.homeListing(m,r.s,search).ids.join()===m.order.join());
  r=run(z.s,m,['down','down','down','down','down','a'],g); ok('back_key_exits_search', r.s.section.type==='allgames');
  S.memo.__prevSection={type:'allgames'}; r=run(z.s,m,['start','a'],g); ok('start_overlay_from_search_returns', !r.s.overlay && r.s.section.type==='search' && r.s.query==='zel');
}
// ---- Browse, folders, categories ----
{ const m=H.homeFixture(20,{titles,folders:true}); const g={kind:'grid',cols:5,browseCols:4}; let S=H.homeInitialState(); let r=run(S,m,['up','right','a'],g); ok('browse_is_primary_destination', r.s.section.type==='browse' && r.s.zone==='browse');
  const B=H.homeBrowseItems(m,r.s); ok('browse_folders_first_then_categories', B.items[0].type==='folder' && B.items[B.folders.length].type==='category' && B.folders.length===2);
  r=run(r.s,m,['a'],g); ok('a_enters_folder_with_children_shows_subfolders', r.s.section.type==='browse' && r.s.section.id===m.folders[0].id);
  r=run(r.s,m,['b'],g); ok('b_ascends_remembers_child', r.s.section.type==='browse' && !r.s.section.id && r.s.browseIndex===0);
  r=run(r.s,m,['right','a'],g); ok('a_enters_leaf_folder', r.s.section.type==='folder' && H.homeListing(m,r.s,search).ids.length===5);
  r=run(r.s,m,['right','right','b'],g); ok('folder_b_returns_to_browse_position', r.s.section.type==='browse' && r.s.browseIndex===1);
  r=run(r.s,m,['right','a'],g); ok('a_enters_category', r.s.section.type==='category' && H.homeListing(m,r.s,search).ids.length>0);
  const empty=H.homeFolderCreate(m,'Empty'); let S2=H.homeInitialState(); S2.section={type:'folder',id:empty.id}; ok('empty_folder_message', H.homeListing(m,S2,search).empty.startsWith('This folder is empty'));
}
// ---- Recently Played and History ----
{ const m=H.homeFixture(10,{titles,history:30}); const g={kind:'grid',cols:5}; let S=H.homeInitialState(); S.section={type:'recent'}; S.zone='games';
  let r=run(S,m,['up'],g); ok('up_from_first_row_focuses_view_all', r.s.zone==='viewall');
  r=run(r.s,m,['a'],g); ok('view_all_enters_history', r.s.section.type==='history');
  const evs=H.homeHistoryEvents(m); r=run(r.s,m,['down','down','right'],g); const dayOf=i=>String(evs[i].at).slice(0,10); ok('right_jumps_to_older_group', dayOf(r.s.historyIndex)<dayOf(2));
  r=run(r.s,m,['left'],g); ok('left_jumps_to_newer_group_start', r.s.historyIndex===0);
  r=run(r.s,m,['left','up'],g); ok('history_no_wrap_at_newest', r.s.historyIndex===0);
  let last=JSON.parse(JSON.stringify(r.s)); last.historyIndex=evs.length-1; r=run(last,m,['down','right'],g); ok('history_no_wrap_at_oldest', r.s.historyIndex===evs.length-1);
  r=run(r.s,m,['b'],g); ok('history_b_returns_to_recent_viewall', r.s.section.type==='recent' && r.s.zone==='viewall');
  H.homeRemoveGame(m,evs[0].game); let S3=H.homeInitialState(); S3.section={type:'history'}; S3.zone='history'; r=run(S3,m,['a'],g); ok('uninstalled_history_game_not_launchable', !r.fx.some(f=>f.type==='launch') && r.fx.some(f=>f.type==='say'));
  let S4=H.homeInitialState(); S4.section={type:'settings'}; S4.zone='settings'; S4.settingsIndex=4; r=run(S4,m,['a'],g); ok('clear_history_needs_confirmation', r.s.overlay&&r.s.overlay.type==='confirm');
  r=run(r.s,m,['right','a'],g); ok('clear_history_confirmed_effect', r.fx.some(f=>f.type==='clear-history'));
}
// ---- Adventure Map ----
{ const m=H.homeFixture(12,{titles,folders:true}); const map={landmarks:[{id:'allgames',dest:'allgames'},{id:'browse',dest:'browse'},{id:'favorites',dest:'favorites'},{id:'folder1',dest:'folder:'+m.map_shortcuts[0]},{id:'folder2',dest:'folder:'+m.map_shortcuts[1]},{id:'settings',dest:'settings'}],neighbors:{allgames:{right:'browse'},browse:{left:'allgames',right:'favorites'},favorites:{left:'browse',right:'folder1'},folder1:{left:'favorites',right:'folder2'},folder2:{left:'folder1',right:'settings'},settings:{left:'folder2'}}};
  const g={kind:'shelf',map}; let S=H.homeInitialState(); S.theme='adventure-map'; let r=run(S,m,['up'],g); ok('shelf_up_enters_map', r.s.zone==='map');
  r=run(r.s,m,['right','right','right'],g); ok('map_dpad_follows_neighbor_graph', r.s.landmark===3 && r.fx.filter(f=>f.type==='walk').length===3);
  r=run(r.s,m,['up'],g); ok('map_edge_no_wrap', r.s.landmark===3);
  r=run(r.s,m,['a'],g); ok('folder_shortcut_opens_folder', r.s.section.type==='folder' && r.s.section.id===m.map_shortcuts[0]);
  r=run(S,m,['up','select'],g); ok('select_on_landmark_opens_destination_overlay', r.s.overlay&&r.s.overlay.type==='mapdest');
  r=run(r.s,m,['down','down','a'],g); ok('destination_overlay_a_opens', r.s.section.type==='search');
  r=run(S,m,['select'],g); ok('select_on_game_opens_details_not_overlay', r.s.overlay&&r.s.overlay.type==='details');
  const m2=H.homeFixture(3); const map2={landmarks:[{id:'folder1',dest:'folder:none'}],neighbors:{}}; r=run(Object.assign(H.homeInitialState(),{zone:'map'}),m2,['a'],{kind:'shelf',map:map2}); ok('unassigned_shortcut_is_graceful', r.s.section.type==='allgames' && r.fx.some(f=>f.type==='say'));
}
// ---- Featured Shelf / Box Art line geometry ----
{ const m=H.homeFixture(8,{titles}); const g={kind:'shelf',navAxis:'vertical'}; let r=run(H.homeInitialState(),m,['left'],g); ok('shelf_left_from_first_enters_sidebar', r.s.zone==='nav');
  r=run(r.s,m,['down','down','right'],g); ok('sidebar_right_returns_to_shelf_restores_game', r.s.zone==='games' && r.s.index===0 && r.s.navIndex===2);
  r=run(H.homeInitialState(),m,['right','right','right','right','right','right','right','right','right'],{kind:'carousel'}); ok('carousel_no_wrap_right', r.s.index===7);
  r=run(r.s,m,['up'],{kind:'carousel'}); ok('carousel_up_focuses_nav', r.s.zone==='nav');
  r=run(r.s,m,['down'],{kind:'carousel'}); ok('nav_down_returns_carousel', r.s.zone==='games' && r.s.index===7);
}
// ---- held repeat timing ----
{ const S=H.homeInitialState(); let t=0; const hits=[]; for(t=0;t<=1500;t+=10){ if(H.homeRepeatStep(S,'down',t)) hits.push(t); } ok('repeat_delay_then_rate_then_faster', hits[0]===0 && hits[1]===400 && hits[2]===510 && hits.length>8 && (hits[hits.length-1]-hits[hits.length-2])<=60, hits.slice(0,10));
  H.homeRelease(S); ok('release_resets_repeat', S.repeat.button===null && H.homeRepeatStep(S,'down',2000));
}
console.log(out.join('\n')); console.log(`RESULT: ${fail?'FAIL':'PASS'} (${pass} passed, ${fail} failed)`); process.exit(fail?1:0);
