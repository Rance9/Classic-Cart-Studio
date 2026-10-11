"""Regression suite for reports/nes-catalog.html.

Part 1 (static): every asset reference is a relative local path that exists (no CDN), the page embeds no ROM bytes,
the record count matches catalog.json, every brand sprite referenced exists and (for online-sourced sprites) is
documented in provenance.json, reduced-motion CSS present.
Part 2 (behaviour, headless Microsoft Edge at a 1920x1080 viewport): compact hero metrics, artwork rendering, search,
every filter, grid/list switching, alphabetical jump, row/card/checkbox/keyboard selection, favorites, persistence,
tray (empty state, counts, revision decisions), details, hover preview, built-in/install semantics, compatibility icons
and tooltips, manifest export (schema 2) and import (schemas 1 and 2), rankings (when ranking data is present), sorting,
no console errors. Uses a throw-away Edge profile; the owner's real selection and favorites are never touched.
Exit code 0 = PASS."""
from __future__ import annotations
import sys as _sys
try: _sys.stdout.reconfigure(encoding="utf-8")
except Exception: pass
import os, sys, subprocess, tempfile, re, html, json
sys.path.insert(0, os.path.dirname(__file__))
from common import *

TEST = r"""
<script>
(function(){
 VL.all=true; render(); const out=[]; const log=(k,v)=>out.push(k+'='+v); const errors=[]; window.confirm=()=>true; window.alert=()=>{};
 window.addEventListener('error', e=>errors.push(e.message)); const ce=console.error; console.error=(...a)=>{ errors.push(a.join(' ')); ce(...a); };
 const click=(el,o)=>el.dispatchEvent(new MouseEvent('click',Object.assign({bubbles:true,cancelable:true,clientX:300,clientY:300},o||{})));
 const setSel=(id,v)=>{ const s=$(id); s.value=v; s.dispatchEvent(new Event('input',{bubbles:true})); if(typeof fapply==='function') fapply(); };
 const shown=()=>visible().length; const cs=el=>getComputedStyle(el);
 try{
  localStorage.clear(); sel.clear(); fav.clear(); decisions={}; persist(); render();
  const status=()=>$('stats').textContent;
  log('start', status());
  // ---- compact hero ----
  const hero=$('hero').getBoundingClientRect(); log('hero_height_px', Math.round(hero.height)); log('hero_le_half_of_543', hero.height<=272);
  log('hero_no_collapse_control', !$('hero').querySelector('button,details,[aria-expanded]') && !$('hero').className.includes('collaps'));
  log('hero_title_one_line', Math.round(document.querySelector('.hero h1').getBoundingClientRect().height/parseFloat(cs(document.querySelector('.hero h1')).lineHeight))===1);
  const pimgs=[...document.querySelectorAll('.parade img')].filter(i=>cs(i).display!=='none'); log('hero_parade_sprites', pimgs.length>=15);
  log('hero_no_clipping', pimgs.every(i=>{const b=i.getBoundingClientRect(); return b.left>=0&&b.right<=innerWidth&&b.top>=hero.top;}));
  log('hero_no_overlap_text', (()=>{ const p=document.querySelector('.pills').getBoundingClientRect(); return pimgs.every(i=>i.getBoundingClientRect().top>=p.bottom-2); })());
  log('hero_pixelated', pimgs.every(i=>/pixelated|crisp/.test(cs(i).imageRendering)) && [...document.querySelectorAll('footer .cast img')].every(i=>/pixelated|crisp/.test(cs(i).imageRendering)));
  log('hero_keeps_branding', !!document.querySelector('.wordmark') && !!document.querySelector('.hardware svg') && !!document.querySelector('.hardware .pad') && !!document.querySelector('.parade .ground') && document.querySelector('.hero h1').textContent.toUpperCase().includes('CLASSIC CART'));
  log('above_fold_rows', [...document.querySelectorAll('#tb tr.pick[data-g]')].filter(t=>t.getBoundingClientRect().bottom<innerHeight).length);
  log('footer_parade_sprites', document.querySelectorAll('footer .cast img').length>=60);
  // ---- default filter shows all 30 factory games ----
  log('builtin_default_show_all', $('fBuilt').value==='' && shown()===DATA.games.length);
  const exactRows=DATA.games.filter(g=>g.builtin_status==='BUILT-IN: EXACT MATCH').length, revRows=DATA.games.filter(g=>g.builtin_status==='BUILT-IN: DIFFERENT REVISION').length;
  log('builtin_30_represented', exactRows===27 && revRows===3 && new Set(DATA.games.filter(g=>['BUILT-IN: EXACT MATCH','BUILT-IN: DIFFERENT REVISION'].includes(g.builtin_status)).map(g=>g.builtin_code)).size===30);
  log('builtin_badges_visible', document.querySelectorAll('#tb .inst.i-builtin').length===27 && document.querySelectorAll('#tb .inst.i-revision').length===3 && document.querySelectorAll('#tb .inst.i-review').length===2);
  // ---- compat pills gone, notes rows gone, icons present ----
  log('no_notes_subrows', !document.querySelector('#tb td[colspan]') && document.querySelectorAll('#tb tr').length===shown());
  log('no_confidence_pills', ![...document.querySelectorAll('#tb .tag, #gridView .tag')].some(t=>/compat:|confidence/i.test(t.textContent)));
  const rowsH=[...document.querySelectorAll('#tb tr.pick')].slice(0,40).map(r=>Math.round(r.getBoundingClientRect().height)); log('rows_consistent_spacing', Math.max(...rowsH)-Math.min(...rowsH)<=24);
  const supergun=DATA.games.find(g=>g.nointro_name==='Super Gun (USA) (Unl)'), smbdh=DATA.games.find(g=>g.nointro_name==='Super Mario Bros. + Duck Hunt (USA)');
  const sgRow=document.querySelector('#tb tr[data-g="'+supergun.game_id+'"]'), dhRow=document.querySelector('#tb tr[data-g="'+smbdh.game_id+'"]');
  log('icon_super_gun_mapper81', !!sgRow.querySelector('button.ci.ci-alt') && supergun.mapper===81);
  log('icon_smb_duckhunt_mapper66', !!dhRow.querySelector('button.ci') && smbdh.mapper===66);
  log('icons_only_when_meaningful', DATA.games.filter(g=>compatInfo(g).level!=='none').length===document.querySelectorAll('#tb button.ci[data-ci]').length && DATA.games.filter(g=>compatInfo(g).level==='none').length>0);
  // tooltip: hover
  document.documentElement.style.scrollBehavior='auto'; sgRow.scrollIntoView({block:'center'}); const ci=sgRow.querySelector('button.ci'); ci.dispatchEvent(new MouseEvent('mouseover',{bubbles:true})); const tipEl=$('tip');
  log('tooltip_hover_shows', tipEl.classList.contains('open') && /RetroArch required/.test(tipEl.textContent) && /Mapper 81/.test(tipEl.textContent) && /FCEUmm/.test(tipEl.textContent) && /not tested on this console/.test(tipEl.textContent));
  log('tooltip_no_invented_claims', !/verified on this console|confirmed compatible/i.test(tipEl.textContent));
  const tr=tipEl.getBoundingClientRect(); log('tooltip_in_viewport', tr.left>=0&&tr.top>=0&&tr.right<=innerWidth&&tr.bottom<=innerHeight);
  log('tooltip_aria', ci.getAttribute('aria-describedby')==='tip' && tipEl.getAttribute('role')==='tooltip' && /RetroArch required/.test(ci.getAttribute('aria-label')));
  ci.dispatchEvent(new MouseEvent('mouseout',{bubbles:true})); log('tooltip_hover_hides', !tipEl.classList.contains('open'));
  // tooltip: keyboard focus
  ci.focus(); ci.dispatchEvent(new FocusEvent('focusin',{bubbles:true})); log('tooltip_focus_shows', tipEl.classList.contains('open')); ci.dispatchEvent(new FocusEvent('focusout',{bubbles:true})); log('tooltip_blur_hides', !tipEl.classList.contains('open'));
  // tooltip: click pins, does not select
  const selBefore=sel.has(supergun.game_id); click(ci); log('tooltip_click_pins', tipEl.classList.contains('open')); log('tooltip_click_no_select', sel.has(supergun.game_id)===selBefore);
  document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape'})); log('tooltip_escape_hides', !tipEl.classList.contains('open'));
  // tooltip vs hover preview: hovering a cover still shows the preview
  const img=sgRow.querySelector('img'); if(img){ img.dispatchEvent(new MouseEvent('mouseover',{bubbles:true,clientX:200,clientY:200})); log('cover_preview_still_works', $('hover').style.display==='block'); img.dispatchEvent(new MouseEvent('mouseout',{bubbles:true})); } else log('cover_preview_still_works', 'true');
  // details retains full compat info
  click(sgRow.querySelector('.det')); log('details_compat_full', /RetroArch required/.test($('detailsBox').textContent) && /mapper 81/i.test($('detailsBox').textContent) && /Confidence/.test($('detailsBox').textContent) && /unlicensed board/i.test($('detailsBox').textContent)); $('details').classList.remove('open');
  // ---- search and filters ----
  $('q').value='contra'; $('q').dispatchEvent(new Event('input')); log('search_contra', shown()>0 && visible().every(g=>(g.display_title+' '+g.nointro_name).toLowerCase().includes('contra'))); $('q').value=''; $('q').dispatchEvent(new Event('input'));
  const y=$('fYear').options[3].value; setSel('fYear',y); log('filter_year', shown()>0 && visible().every(g=>g.year===y)); setSel('fYear','');
  const pub=$('fPub').options[2].value; setSel('fPub',pub); log('filter_publisher', shown()>0 && visible().every(g=>g.publisher===pub)); setSel('fPub','');
  setSel('fLic','unlicensed'); log('filter_unlicensed', shown()>0 && visible().every(g=>g.licensed==='unlicensed')); setSel('fLic','');
  const ge=$('fGenre').options[2].value; setSel('fGenre',ge); log('filter_genre', shown()>0 && visible().every(g=>(g.genre||'').split(',').map(x=>x.trim()).includes(ge))); setSel('fGenre','');
  setSel('fPlayers','2sim'); log('filter_players_2sim', shown()>0 && visible().every(g=>g.players==='2'&&g.simultaneous==='simultaneous')); setSel('fPlayers','');
  setSel('fEmu','RetroArch'); log('filter_emulator', shown()>0 && visible().every(g=>g.recommended_emulator==='RetroArch')); setSel('fEmu','');
  setSel('fVer','cartdb'); log('filter_identity_cartdb', visible().every(g=>!g.nointro_match)); setSel('fVer','single'); log('filter_header_single', shown()>0 && visible().every(g=>g.header_validation_short==='single-source')); setSel('fVer','');
  setSel('fBuilt','hideexact'); const hid=shown(); setSel('fBuilt','exact'); const ex=shown(); setSel('fBuilt','other'); const oth=shown(); setSel('fBuilt','');
  log('filter_builtin', ex===27 && hid===DATA.games.length-27 && oth===5);
  // ---- jump letters ----
  log('jump_letters', $('letters').querySelectorAll('a[data-l]').length>20 && !!document.querySelector('#tb [data-l="M"]'));
  document.documentElement.style.scrollBehavior='auto'; scrollTo(0,0); click($('letters').querySelector('a[data-l="M"]')); const mrow=document.querySelector('#tb [data-l="M"]'); const mt=mrow.getBoundingClientRect().top;
  log('jump_scrolls_list', scrollY>500 && mt>=stickyH()-2 && mt<stickyH()+120 && byId[mrow.dataset.g].display_title.toUpperCase().startsWith('M'));
  $('vGrid').click(); scrollTo(0,0); click($('letters').querySelector('a[data-l="T"]')); const tcard=document.querySelector('#gridView [data-l="T"]'); log('jump_scrolls_grid', scrollY>500 && Math.abs(tcard.getBoundingClientRect().top-stickyH())<40); $('vList').click(); scrollTo(0,0);
  // ---- selection via row, image, checkbox, keyboard; star; details ----
  const id=document.querySelector('#tb tr.pick').dataset.g; const row=()=>document.querySelector('#tb tr.pick[data-g="'+id+'"]');
  click(row().querySelectorAll('td')[5]); log('1_rowclick_on', sel.has(id));
  click(row().querySelectorAll('td')[6]); log('2_rowclick_off', !sel.has(id));
  click(row().querySelector('img,.ph')); log('3_imgclick_on', sel.has(id)); log('3_no_details', !$('details').classList.contains('open'));
  row().querySelector('input[type=checkbox]').click(); log('4_checkbox_off', !sel.has(id));
  row().querySelector('input[type=checkbox]').click(); log('4_checkbox_on', sel.has(id));
  row().dispatchEvent(new KeyboardEvent('keydown',{key:' ',bubbles:true})); log('4b_keyboard_space_off', !sel.has(id));
  row().dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',bubbles:true})); log('4b_keyboard_enter_on', sel.has(id));
  click(row().querySelector('.star')); log('5_star_fav', fav.has(id)); log('5_star_keeps_sel', sel.has(id));
  click(row().querySelector('.det')); log('6_details_open', $('details').classList.contains('open')); log('6_keeps_sel', sel.has(id));
  log('6_details_fields', ['Year','Publisher','Genre','Players','Description','Licensed','Built-in','Installation','ROM identity','Header','Cartridge','Emulator','Status','Confidence','Evidence','Alternates','Source'].every(t=>[...document.querySelectorAll('#detailsBox dt')].some(d=>d.textContent===t)));
  document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape'})); log('6_escape_closes', !$('details').classList.contains('open'));
  click(row().querySelector('.tag')||row().querySelectorAll('td')[10]); log('7_tagclick_off', !sel.has(id));
  // ---- grid ----
  $('vGrid').click(); log('8_grid_shown', !$('gridView').hidden && $('listView').hidden && localStorage.getItem('nes-classic-view-v1')==='grid');
  const card=()=>document.querySelector('#gridView .card[data-g="'+id+'"]');
  click(card().querySelector('.m')); log('8_card_on', sel.has(id)); log('8_card_badge', cs(card().querySelector('.selbadge')).display!=='none');
  click(card().querySelector('img,.ph')); log('8_cardimg_off', !sel.has(id));
  click(card().querySelector('.t')); log('8_cardtitle_on', sel.has(id));
  click(card().querySelector('.det')); log('9_card_details', $('details').classList.contains('open')); log('9_keeps_sel', sel.has(id)); $('details').classList.remove('open');
  const im=card().querySelector('img'); im.dispatchEvent(new MouseEvent('mouseover',{bubbles:true,clientX:200,clientY:200})); log('10_hover_shown', $('hover').style.display==='block'); log('10_hover_src', $('hoverImg').getAttribute('src')===byId[id].art); im.dispatchEvent(new MouseEvent('mouseout',{bubbles:true})); log('10_hover_hidden', $('hover').style.display==='none');
  const sgCard=document.querySelector('#gridView .card[data-g="'+supergun.game_id+'"]'); log('grid_compat_icon', !!sgCard.querySelector('button.ci.ci-alt') && !sgCard.querySelector('.warnline'));
  click(sgCard.querySelector('button.ci')); log('grid_icon_click_no_select', !sel.has(supergun.game_id) && tipEl.classList.contains('open')); hideTip(true);
  log('grid_no_long_compat_text', ![...document.querySelectorAll('#gridView .card')].some(c=>/mapper \d+ not supported/i.test(c.textContent)));
  $('vList').click();
  // ---- controller / accessory requirements ----
  const duck=DATA.games.find(g=>g.nointro_name==='Duck Hunt (World)'), ark=DATA.games.find(g=>g.display_title==='Arkanoid');
  log('input_fields_valid', games.every(g=>['none','info','optional','partial','required','unknown'].includes(g.input_level)&&['supported','partial','unverified','unsupported','unknown'].includes(g.input_nes_classic_support)&&['true','false','partial','unknown'].includes(g.input_standard_controller_playable)&&Array.isArray(g.input_modes)));
  log('input_separate_from_compat', games.some(g=>g.input_level==='required'&&compatInfo(g).level==='none') && games.some(g=>compatInfo(g).level==='alt'&&g.input_level==='none'));
  log('input_duck_hunt_required', duck.input_level==='required' && (duck.input_modes||[]).includes('light-gun') && duck.input_standard_controller_playable==='false' && duck.input_nes_classic_support!=='supported');
  log('input_lightgun_count', DATA.games.filter(g=>(g.input_modes||[]).includes('light-gun')).length);
  log('input_unknown_not_supported', DATA.games.filter(g=>g.input_level==='unknown').every(g=>g.input_nes_classic_support==='unknown'&&g.input_standard_controller_playable==='unknown'));
  log('input_optional_keeps_standard', !ark || (ark.input_level!=='required' && ark.input_standard_controller_playable==='true'));
  const duckRow=document.querySelector('#tb tr[data-g="'+duck.game_id+'"]'); duckRow.scrollIntoView({block:'center'}); const ib=duckRow.querySelector('button.ci.ci-input'); log('input_icon_list', !!ib);
  ib.dispatchEvent(new MouseEvent('mouseover',{bubbles:true})); log('input_tooltip_hover', tipEl.classList.contains('open') && /Requires NES Zapper/.test(tipEl.textContent) && /not directly compatible/.test(tipEl.textContent) && /No compatible alternative has been verified/.test(tipEl.textContent)); ib.dispatchEvent(new MouseEvent('mouseout',{bubbles:true}));
  ib.focus(); ib.dispatchEvent(new FocusEvent('focusin',{bubbles:true})); log('input_tooltip_focus', tipEl.classList.contains('open')); ib.dispatchEvent(new FocusEvent('focusout',{bubbles:true}));
  const wasSel=sel.has(duck.game_id); click(ib); log('input_tooltip_click_no_select', tipEl.classList.contains('open') && sel.has(duck.game_id)===wasSel); hideTip(true);
  $('vGrid').click(); const dc=document.querySelector('#gridView .card[data-g="'+duck.game_id+'"]'); log('input_icon_grid', !!dc.querySelector('button.ci.ci-input')); click(dc.querySelector('button.ci.ci-input')); log('input_grid_click_no_select', sel.has(duck.game_id)===wasSel && tipEl.classList.contains('open')); hideTip(true); $('vList').click();
  click(document.querySelector('#tb tr[data-g="'+duck.game_id+'"] .det')); log('input_details_section', /Controllers & accessories/.test($('detailsBox').textContent) && /NES Zapper/.test($('detailsBox').textContent) && /NES Classic support/.test($('detailsBox').textContent) && /TV collection/.test($('detailsBox').textContent)); $('details').classList.remove('open');
  setSel('fInput','standard'); log('input_filter_standard_excludes_required', !visible().some(g=>g.input_level==='required') && !visible().some(g=>g.input_level==='unknown') && visible().length>500);
  setSel('fInput','required'); log('input_filter_required', visible().length>0 && visible().every(g=>g.input_accessory_requirement==='required') && visible().includes(duck));
  setSel('fInput','optional'); log('input_filter_optional', visible().every(g=>['optional','mode-specific'].includes(g.input_accessory_requirement)));
  setSel('fInput','lightgun'); log('input_filter_lightgun', visible().length>0 && visible().every(g=>(g.input_modes||[]).includes('light-gun')));
  setSel('fInput','unknown'); log('input_filter_unknown', visible().every(g=>g.input_level==='unknown'));
  if(SRC.length){ setSel('fInput','standard'); setSel('fRank','top25'); log('input_filter_combines_rank', visible().length>0 && visible().every(g=>rankOf(g).consensus<=25 && ['true','partial'].includes(g.input_standard_controller_playable))); setSel('fRank',''); }
  setSel('fInput','');
  sel.clear(); persist(); render(); toggleSel(duck.game_id,true); log('tray_hw_warning', /1 selected game requires special input hardware/.test($('traySub').textContent) && /Duck Hunt/.test($('traySub').textContent));
  log('tray_policy_summary', /Desired TV collection: selected games only/.test($('traySub').textContent) && /Console-side enforcement has not yet been configured/.test($('traySub').textContent) && /30 of 30 factory games would be hidden/.test($('traySub').textContent));
  toggleSel(DATA.games.find(g=>g.builtin_status==='BUILT-IN: EXACT MATCH'&&g.display_title==='Super Mario Bros.').game_id,true); log('tray_policy_counts_visible', /29 of 30 factory games would be hidden/.test($('traySub').textContent));
  sel.clear(); persist(); render(); log('tray_empty_wording', /Choose the games you want available on your NES Classic/.test($('trayList').textContent));
  // ---- built-in / install semantics ----
  const smb=DATA.games.find(g=>g.builtin_status==='BUILT-IN: EXACT MATCH'&&g.display_title==='Super Mario Bros.'), zelda=DATA.games.find(g=>g.nointro_name==='Legend of Zelda, The (USA) (Rev 1)'), tengen=DATA.games.find(g=>g.nointro_name==='Pac-Man (USA) (Tengen)'), contra=DATA.games.find(g=>g.nointro_name==='Contra (USA)');
  sel.clear(); persist(); render(); log('tray_empty_state', !!document.querySelector('#trayList li.empty') && /collection starts here/i.test($('trayList').textContent) && document.querySelectorAll('#trayList li.empty .cast img').length>=3 && !!document.querySelector('#trayList li.empty svg'));
  toggleSel(smb.game_id,true); log('builtin_selectable', sel.has(smb.game_id) && !document.querySelector('#trayList li.empty'));
  log('builtin_toast', $('toast').classList.contains('show') && /already installed/.test($('toast').textContent));
  log('builtin_in_tray_no_install', /NO INSTALL NEEDED/.test($('trayList').textContent) && /1 already installed/.test($('traySub').textContent) && /0 to add/.test($('traySub').textContent));
  log('builtin_zero_storage', /0 to install/.test(status()) && /~0 KB/.test(status()));
  toggleSel(zelda.game_id,true); log('revision_default_factory', !installRequired(zelda) && /factory revision kept/.test($('traySub').textContent) && /DIFFERENT REVISION/.test($('trayList').textContent) && !!document.querySelector('#trayList select.dec[data-dec="'+zelda.game_id+'"]'));
  const dsel=document.querySelector('#trayList select.dec[data-dec="'+zelda.game_id+'"]'); dsel.value='alternate'; dsel.dispatchEvent(new Event('change',{bubbles:true}));
  log('revision_alternate_requires_approval', installRequired(zelda) && decisions[zelda.game_id]==='alternate' && /pending approval/.test($('traySub').textContent) && /1 to add/.test($('traySub').textContent));
  toggleSel(tengen.game_id,true); log('identity_review_required', !installRequired(tengen) && /REVIEW REQUIRED/.test($('trayList').textContent) && /1 need review/.test($('traySub').textContent));
  toggleSel(contra.game_id,true); log('mixed_counts', /4 games in your collection/.test($('traySub').textContent) && /2 already installed/.test($('traySub').textContent) && /2 to add/.test($('traySub').textContent));
  log('storage_counts_install_only', /2 to install/.test(status()) && Math.abs(DATA.games.filter(g=>sel.has(g.game_id)&&installRequired(g)).reduce((a,g)=>a+g.payload_size+16,0)-(contra.payload_size+16+zelda.payload_size+16))===0);
  // ---- bulk actions ----
  $('q').value='mega man'; $('q').dispatchEvent(new Event('input')); const n=shown(); $('selVis').click(); log('12_select_visible', visible().every(g=>sel.has(g.game_id)) && n>0);
  $('unselVis').click(); log('12_unselect_visible', visible().every(g=>!sel.has(g.game_id)) && sel.has(contra.game_id));
  $('selKachi').click(); log('12_select_kachi', visible().filter(g=>g.recommended_emulator==='kachikachi'&&g.nointro_match&&g.selection_status!=='review').every(g=>sel.has(g.game_id)));
  $('q').value=''; $('q').dispatchEvent(new Event('input'));
  // ---- tray controls ----
  $('trayBtn').click(); log('13_tray_open', $('tray').classList.contains('open') && $('trayList').querySelectorAll('li').length===sel.size && $('traySub').textContent.includes(' MB'));
  click(document.querySelector('#trayList .tl')); log('13_tray_title_details', $('details').classList.contains('open')); $('details').classList.remove('open');
  const before=sel.size; click(document.querySelector('#trayList .x')); log('13_tray_remove', sel.size===before-1);
  document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape'})); log('13_escape_closes_tray', !$('tray').classList.contains('open'));
  // ---- filters dialog, chips, select menu ----
  $('filtersBtn').click(); log('20_filters_modal_open', $('filters').classList.contains('open') && $('filtersCount').textContent.includes('games match'));
  setSel('fYear',y); setSel('fLic','licensed'); $('filtersDone').click(); log('20_chips', !$('chips').hidden && $('chips').querySelectorAll('.chip').length===2 && $('filtersBadge').textContent==='2');
  $('filtersDone').click(); log('20_filters_done_closes', !$('filters').classList.contains('open'));
  click($('chips').querySelector('.rm')); log('20_chip_removes', $('chips').querySelectorAll('.chip').length===1 && $('fYear').value==='');
  $('filtersBtn').click(); $('filtersReset').click(); $('filtersDone').click(); log('20_reset', activeFilters().length===0 && $('fBuilt').value==='' && $('chips').hidden); document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape'})); log('20_escape_closes_filters', !$('filters').classList.contains('open'));
  log('21_menu_closed_on_load', cs($('selectMenu')).display==='none' && cs($('filtersBadge')).display==='none' && cs($('chips')).display==='none'); $('selectBtn').click(); log('21_menu_open', cs($('selectMenu')).display!=='none' && ['selVis','unselVis','selKachi','clear'].every(i=>$('selectMenu').contains($(i)))); $('selVis').click(); log('21_menu_closes_after_action', cs($('selectMenu')).display==='none'); $('clear').click();
  // ---- rankings (only when ranking data is embedded) ----
  if(SRC.length){ const ranked=DATA.games.filter(g=>rankOf(g)&&rankOf(g).consensus); const cons=ranked.map(g=>rankOf(g).consensus);
   log('rank_sources', SRC.length); log('rank_matched', ranked.length);
   log('rank_positions_unique', new Set(cons).size===cons.length && cons.every(c=>c>=1));
   log('rank_source_positions_unique', SRC.every(sc=>{ const ps=games.map(g=>(rankOf(g)||{sources:{}}).sources[sc.key]).filter(Boolean); return sc.ties ? ps.length>=90 : new Set(ps).size===ps.length; }));
   log('rank_source_ties_documented', SRC.every(sc=>typeof sc.ties==='boolean') && SRC.some(sc=>sc.ties));
   log('rank_unranked_no_position', DATA.games.filter(g=>!rankOf(g)).length>0 && DATA.games.filter(g=>!rankOf(g)).every(g=>!document.querySelector('#tb tr[data-g="'+g.game_id+'"] .rk')||/—/.test(document.querySelector('#tb tr[data-g="'+g.game_id+'"] .rk').textContent)));
   log('rank_builtin_participates', !!(rankOf(smb)&&rankOf(smb).consensus) && installRequired(smb)===false);
   log('rank_column_visible', !$('thRank').hidden && document.querySelectorAll('#tb .rk').length===shown());
   $('sortSel').value='rank:consensus'; $('sortDir').value='asc'; fapply(); const order=[...document.querySelectorAll('#tb tr.pick')].map(r=>rankOf(byId[r.dataset.g])?rankOf(byId[r.dataset.g]).consensus:null);
   log('rank_sort_list', order.slice(0,ranked.length).every((v,i)=>v===i+1) && order.slice(ranked.length).every(v=>v===null));
   $('vGrid').click(); const gorder=[...document.querySelectorAll('#gridView .card')].slice(0,10).map(c=>rankOf(byId[c.dataset.g]).consensus); log('rank_sort_grid', gorder.every((v,i)=>v===i+1)); log('rank_badge_top10', document.querySelectorAll('#gridView .rankbadge.top').length===10 && [...document.querySelectorAll('#gridView .card')].slice(0,3).every(c=>{const b=c.querySelector('.rankbadge').getBoundingClientRect(), cv=c.querySelector('.cover').getBoundingClientRect(); return b.height<cv.height*0.25;}));
   log('rank_badge_no_select', (()=>{ const c=document.querySelector('#gridView .card'); const g=c.dataset.g; const was=sel.has(g); click(c.querySelector('.rankbadge')); return sel.has(g)!==was; })()); $('clear').click(); $('vList').click();
   $('sortSel').value='display_title'; $('sortDir').value='asc'; fapply();
   setSel('fRank','top10'); log('rank_filter_top10', shown()===10 && visible().every(g=>rankOf(g).consensus<=10)); setSel('fRank','two'); log('rank_filter_two', shown()>0 && visible().every(g=>rankOf(g).count>=2)); setSel('fEmu','kachikachi'); log('rank_filter_combines', visible().every(g=>rankOf(g).count>=2&&g.recommended_emulator==='kachikachi')); setSel('fEmu',''); setSel('fRank','unranked'); log('rank_filter_unranked', visible().every(g=>!rankOf(g))); setSel('fRank','');
   click(document.querySelector('#tb tr[data-g="'+smb.game_id+'"] .det')); log('rank_details_section', /Rankings/.test($('detailsBox').textContent) && SRC.every(sc=>$('detailsBox').textContent.includes(sc.name)) && [...$('detailsBox').querySelectorAll('.ranks a')].every(a=>/^https?:\/\//.test(a.href))); $('details').classList.remove('open');
  } else log('rank_skipped_no_data','true');
  // ---- export manifest (schema 2) and import (schema 2 + legacy 1) ----
  sel.clear(); decisions={}; [smb,zelda,tengen,contra].forEach(g=>sel.add(g.game_id)); decisions[zelda.game_id]='alternate'; persist(); render();
  let blob=null; const oc=URL.createObjectURL; URL.createObjectURL=b=>{ blob=b; return 'blob:test'; }; const oclick=HTMLAnchorElement.prototype.click; HTMLAnchorElement.prototype.click=function(){}; const oconf=window.confirm; window.confirm=()=>true; $('export').click();
  const keepSel=new Set(sel); sel.clear(); persist(); render(); let blob2=null; URL.createObjectURL=b=>{ blob2=b; return 'blob:x'; }; window.confirm=()=>false; $('export').click(); log('export_empty_requires_confirm', blob2===null); window.confirm=oconf; keepSel.forEach(x=>sel.add(x)); persist(); render(); HTMLAnchorElement.prototype.click=oclick; URL.createObjectURL=oc;
  blob.text().then(t=>{ const m=JSON.parse(t); const by=Object.fromEntries(m.selections.map(g=>[g.game_id,g]));
   log('14_export_schema4', m.schema==='nes-classic-collection/4' && m.collection_mode==='exclusive' && m.factory_game_policy==='hide_unselected' && m.count===4 && m.selections.length===4 && m.selections.every(g=>g.game_id&&g.version_id===g.game_id&&g.title_id&&g.source&&g.payload_sha256&&g.nointro_name&&'install_required' in g&&'comparison_status' in g&&'builtin_code' in g&&g.input_requirements&&'nes_classic_support' in g.input_requirements) && Array.isArray(m.personal_library) && Array.isArray(m.external_dependencies) && m.status && 'installed_on_console' in m.status && m.name);
   log('14_export_factory_inventory', m.factory_inventory && m.factory_inventory.count===30 && m.factory_inventory.codes.length===30 && /^[0-9a-f]{64}$/.test(m.factory_inventory.sha256));
   log('14_export_hidden_factory', m.hidden_factory_games.length===28 && m.hidden_factory_games.every(h=>h.code&&h.title) && !m.hidden_factory_games.some(h=>h.code===smb.builtin_code||h.code===tengen.builtin_code) && m.hidden_factory_games.some(h=>h.code===zelda.builtin_code));   // zelda: catalog revision chosen -> factory copy hidden; Tengen Pac-Man keeps the factory Namco title visible
   log('14_export_deployment_pending', m.deployment && m.deployment.console_enforcement==='pending' && /not yet been configured/.test(m.deployment.note) && m.deployment.required_capabilities.includes('preserve_factory_roms'));
   log('14_export_no_selection_change', sel.size===4 && [smb,zelda,tengen,contra].every(g=>sel.has(g.game_id)));
   log('14_export_builtin_flags', by[smb.game_id].install_required===false && by[smb.game_id].comparison_status==='exact-match' && by[smb.game_id].builtin_code && by[zelda.game_id].install_required===true && by[zelda.game_id].revision_decision==='alternate' && by[tengen.game_id].install_required===false && by[tengen.game_id].revision_decision==='factory' && by[contra.game_id].install_required===true && by[contra.game_id].comparison_status==='not-built-in');
   log('14_export_summary', m.summary.collection===4 && m.summary.already_installed===1 && m.summary.to_install===2 && m.selections.filter(g=>g.comparison_status==='identity-only'&&!g.install_required).length===1 && m.selections.filter(g=>g.comparison_status!=='not-built-in'&&g.install_required).length===1);
   const keep=[...sel]; sel.clear(); decisions={}; persist(); render();
   const f=new File([t],'selection.json',{type:'application/json'}); const dt=new DataTransfer(); dt.items.add(f); $('import').files=dt.files; $('import').dispatchEvent(new Event('change'));
   let tries=0; const wait=()=>{ if(sel.size===0 && tries++<40){ setTimeout(wait,100); return; } log('15_import_restores', keep.every(x=>sel.has(x)) && sel.size===keep.length && decisions[zelda.game_id]==='alternate');
    const legacy={schema:'nes-classic-selection/1',count:1,games:[{game_id:contra.game_id}]}; log('15_v3_import_restores_decisions', decisions[zelda.game_id]==='alternate'); sel.clear(); persist(); render(); const f1=new File([JSON.stringify(legacy)],'old.json',{type:'application/json'}); const dt1=new DataTransfer(); dt1.items.add(f1); $('import').files=dt1.files; $('import').dispatchEvent(new Event('change'));
    let t2=0; const wait2=()=>{ if(sel.size===0 && t2++<40){ setTimeout(wait2,100); return; } log('15_legacy_import', sel.has(contra.game_id) && sel.size===1);
     log('16_persisted_selection', JSON.parse(localStorage.getItem('nes-classic-selection-v1')).length===sel.size); log('16_persisted_favorites', JSON.parse(localStorage.getItem('nes-classic-favorites-v1')).includes(id)); log('16_persisted_decisions', typeof JSON.parse(localStorage.getItem('nes-classic-decisions-v1'))==='object');
        log('units_decimal_formatting', fmt(40976)==='41 KB' && fmt(262144)==='262.1 KB' && fmt(2056336)==='2.06 MB' && fmt(1500000000)==='1.5 GB' && fmt(0)==='0 KB');
     log('units_no_binary_in_ui', !/(MiB|KiB|GiB)/.test(document.body.textContent) && !/(MiB|KiB|GiB)/.test($('storage').title));
     log('units_storage_mb_converted', Math.abs(mibToMb(DATA.storage.conservative_usable_mib) - DATA.storage.conservative_usable_mib*1.048576) < 1e-6 && /MB/.test($('storage').textContent));
     log('units_bytes_untouched', games.every(g=>Number.isInteger(g.payload_size)) && typeof DATA.storage.conservative_usable_mib==='number');
  log('17_hero_counters', $('heroSel').textContent==String(sel.size) && $('heroFav').textContent==String(fav.size) && $('heroGames').textContent==String(DATA.games.length));
     log('18_sort', (()=>{ const th=document.querySelector('th[data-k="year"]'); th.click(); const ys=[...document.querySelectorAll('#tb tr.pick')].map(r=>byId[r.dataset.g].year||''); th.click(); return ys.length>10 && ys.slice(0,-1).every((v,i)=>v<=ys[i+1]); })());
     log('18_sort_select', (()=>{ $('sortSel').value='year'; $('sortDir').value='desc'; fapply(); const ys=[...document.querySelectorAll('#tb tr.pick')].map(r=>byId[r.dataset.g].year||''); $('sortSel').value='display_title'; $('sortDir').value='asc'; fapply(); return ys.slice(0,-1).every((v,i)=>v>=ys[i+1]); })());
     log('19_no_console_errors', errors.length===0 ? 'true' : 'false:'+errors.join('|').slice(0,200));
     log('final', status()); document.title='TEST '+out.join(';'); }; setTimeout(wait2,100); }; setTimeout(wait,100); });
 }catch(e){ log('ERROR', e.message+' '+(e.stack||'').split('\n')[1]); document.title='TEST '+out.join(';'); }
})();
</script>"""

def static_checks(page: str) -> list[str]:
    checks = []
    refs = set(re.findall(r'(?:src|href)="([^"]+)"', page)) | set(re.findall(r'url\("([^"]+)"\)', page))
    refs = {r for r in refs if not r.startswith(("data:", "#", "blob:")) and "${" not in r}
    ext = [r for r in refs if re.match(r"^[a-z]+:", r) and not re.match(r"^http://127\.0\.0\.1:\d+/", r) and not r.startswith(("https://github.com/TeamShinkansen/", "https://github.com/Rance9/", "https://github.com/sponsors/Rance9", "https://ClassicCartStudio.com/", "https://classiccartstudio.com/", "https://www.ebay.com/itm/302788782864"))]   # loopback companion link, the Hakchi2 CE credit anchors and this project's own repository link are not assets
    refs = {r for r in refs if not re.match(r"^http://127\.0\.0\.1:\d+/", r)}
    refs = {r for r in refs if not r.startswith(("https://github.com/TeamShinkansen/", "https://github.com/Rance9/", "https://github.com/sponsors/Rance9", "https://ClassicCartStudio.com/", "https://classiccartstudio.com/", "https://www.ebay.com/itm/302788782864"))}   # credit links to the Hakchi2 CE project and this project's repository (plain anchors, not assets)
    checks.append(f"static_no_external_assets={'true' if not ext else 'false:'+','.join(ext)[:200]}")
    missing = [r for r in refs if not os.path.exists(os.path.join(REPORTS, r.split('?')[0]))]
    checks.append(f"static_assets_exist={'true' if not missing else 'false:'+','.join(missing)[:200]}")
    checks.append(f"static_no_rom_bytes={'true' if 'NES\\u001a' not in page and page.count('payload_sha256')>700 else 'false'}")
    data = json.load(open(os.path.join(REPORTS, "catalog.json"), encoding="utf-8"))
    checks.append(f"static_record_count={'true' if page.count('\"game_id\"')==len(data['games']) else 'false'}")
    checks.append(f"static_reduced_motion={'true' if 'prefers-reduced-motion' in page else 'false'}")
    sprites = re.findall(r'src="(brand/[^"]+\.png)"', page)
    checks.append(f"static_sprites_exist={'true' if sprites and all(os.path.exists(os.path.join(REPORTS, s)) for s in sprites) else 'false'}")
    prov = os.path.join(REPORTS, "brand", "web", "provenance.json")
    if os.path.exists(prov):
        pv = {e["slug"]: e for e in json.load(open(prov, encoding="utf-8"))}
        web = {re.sub(r".*/", "", s)[:-4] for s in sprites if s.startswith("brand/web/")}
        documented = all(slug in pv and pv[slug].get("source_page_url") and pv[slug].get("game_or_origin") and pv[slug].get("width") for slug in web)
        checks.append(f"static_web_sprites_documented={'true' if web and documented else 'false'}")
    # ranking source links are plain href attributes inside the JSON data (never fetched at run time); nothing else may be external
    return checks

def main():
    src = os.path.join(REPORTS, "nes-catalog.html"); page = open(src, encoding="utf-8").read()
    checks = static_checks(page)
    data = json.load(open(os.path.join(REPORTS, "catalog.json"), encoding="utf-8"))
    art_missing = [g["art"] for g in data["games"] if g.get("art") and not os.path.exists(os.path.join(REPORTS, g["art"]))]
    checks.append(f"static_art_cache={'true' if not art_missing else 'false:'+str(len(art_missing))+' missing'}")
    tmp = os.path.join(tempfile.gettempdir(), "nes-catalog-test"); os.makedirs(tmp, exist_ok=True)
    out = os.path.join(tmp, "t.html"); open(out, "w", encoding="utf-8").write(page.replace("</body>", TEST + "</body>", 1))
    edge = next(p for p in [r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", r"C:\Program Files\Microsoft\Edge\Application\msedge.exe"] if os.path.exists(p))
    prof = tempfile.mkdtemp(prefix="nes-catalog-prof-")   # fresh profile per run: a profile left locked by a killed Edge makes headless hang
    try:
        r = subprocess.run([edge, "--headless=new", "--disable-gpu", "--allow-file-access-from-files", f"--user-data-dir={prof}", "--window-size=1920,1080", "--virtual-time-budget=60000", "--dump-dom", "file:///" + out.replace(chr(92), "/")], capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=240)
    finally:
        import shutil; shutil.rmtree(prof, ignore_errors=True)
    m = re.search(r"<title>TEST ([^<]*)", r.stdout)
    if not m: print("\n".join(checks)); print("no behaviour result; edge stderr:", r.stderr[-500:]); sys.exit(2)
    checks += html.unescape(m.group(1)).split(";")
    bad = [c for c in checks if "=false" in c or c.startswith("ERROR")]
    print("\n".join(checks)); print(f"RESULT: {'PASS' if not bad else 'FAIL '+str(bad)} ({len(checks)} checks)"); sys.exit(1 if bad else 0)

if __name__ == "__main__":
    main()
