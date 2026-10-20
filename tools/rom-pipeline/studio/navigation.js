// ---- top navigation (one implementation for the website and the Windows app): My Collections (left), the collection's console
//      actions (Tray, Preview on TV, HOME Setup, Install), the Studio menu (help, backup and restore); the Game Library tools below
//      (search, Filter & Sort, Select with Favorites, List/Grid, Add Your Own Game). Capability differences only hide what cannot work.
(function(){
 const on=(id,fn)=>{ const b=$(id); if(b) b.onclick=fn; };
 const closeMenus=()=>document.querySelectorAll('.menu').forEach(x=>x.hidden=true);
 on('homeBtn',()=>{ closeMenus(); if(typeof openFolders==='function') openFolders(); });
 on('ownBtn',()=>{ closeMenus(); addOwnGame(); });
 // the legacy hidden controls other parts still click
 on('export',()=>csExport()); on('importBtn',()=>csImportPick());
 // Favorites from the Select menu: a view of the library (the Selection filter "Favorites only"); it never changes what is selected
 function favActive(){ const f=$('fSel'); return !!f&&f.value==='fav'; }
 function favLabel(){ const b=$('favView'); if(!b) return; const n=fav.size; b.textContent=favActive()?'Show All Games':`★ Show Favorites${n?` (${n})`:''}`; b.setAttribute('aria-pressed',String(favActive())); }
 window.showFavorites=function(onOff){ const f=$('fSel'); if(!f) return; f.value=onOff?'fav':''; try{ f.dispatchEvent(new Event('change',{bubbles:true})); }catch(e){} render(); favLabel();
  if(onOff&&!fav.size) toast('No favorites yet: click the ★ on any game to add it'); };
 on('favView',e=>{ e.stopPropagation(); closeMenus(); window.showFavorites(!favActive()); });
 const sb=$('selectBtn'); if(sb) sb.addEventListener('click',favLabel);
 const fs=$('fSel'); if(fs) fs.addEventListener('change',favLabel); favLabel();
 // My Collections: the menu shows the open collection and the quick list each time it opens
 const cb=$('collBtn'); if(cb) cb.addEventListener('click',()=>{ csSaveActiveNow(); csMenuRefresh(); const h=$('collNameHead'); if(h) h.textContent=COLL.name; updateStatusLine(); });
 // capability differences
 function caps(){ const desk=!!(CO&&CO.desktop), web=!!(CO&&CO.hosted&&!CO.desktop);
  const ll=$('mLoadLocal'); if(ll) ll.hidden=!(desk||(CO&&CO.served));
  const tw=$('mToWeb'); if(tw) tw.hidden=!desk;
  const dk=$('mDesktop'); if(dk) dk.hidden=!(typeof desktopAvailable==='function'&&desktopAvailable());
  const sep=$('mDesktopSep'); if(sep) sep.hidden=!((tw&&!tw.hidden)||(dk&&!dk.hidden)); }
 caps(); setTimeout(caps,1500); setTimeout(caps,5000); document.addEventListener('visibilitychange',caps);
 // Share Collection appears only when the sharing service is switched on (share-config.json); never a button that cannot work
 const shareVis=on=>{ document.body.classList.toggle('noshare',!on); };   // Share or Export stays: exporting a .ccs needs no service
 shareVis(false); if(typeof shareConfig==='function') shareConfig().then(shareVis);
 // Continue in Desktop App: choose one, several or all collections (website); Continue on the Website (app)
 on('mDesktop',()=>{ closeMenus(); if(csList().length>1) csTransferPick(ids=>{ closeColl(); startHandoff(ids); }); else desktopInvite(); });
 on('mToWeb',()=>{ closeMenus(); csToWebsite(); });
 // the Store: shown only once it has approved listings; on the website it opens the store page, in the app the user's own browser
 // (the app never shows the store or its links inside its window)
 const STORE_URL='https://classiccartstudio.com/store/';
 async function storeCheck(){ const b=$('mStore'); if(!b) return; if(CO&&CO.desktop){ b.hidden=true; return; }
  try{ const r=await fetch('store/store.json',{cache:'no-store'}); if(!r.ok) return; const d=await r.json(); const live=d&&d.published===true&&(d.products||[]).some(p=>p&&p.enabled); b.hidden=!live; }catch(e){} }
 on('mStore',()=>{ closeMenus(); window.open(location.protocol==='https:'?'store/':STORE_URL,'_blank','noopener'); });
 if(location.protocol!=='file:') setTimeout(storeCheck,1200);
 // phones: the ⋯ menu of the library row (Favorites, add/remove all shown, list/grid, add your own game) uses the same controls
 wireMenu('moreBtn','moreMenu');
 const mm=$('moreMenu'); if(mm){ const vt=mm.querySelector('[data-view]'); const vtLabel=()=>{ if(vt) vt.textContent=view==='grid'?'List View':'Grid View'; };
  $('moreBtn').addEventListener('click',()=>{ vtLabel(); const fv=mm.querySelector('[data-proxy=favView]'); if(fv&&$('favView')) fv.textContent=$('favView').textContent; ['selVis','unselVis'].forEach(id=>{ const b=mm.querySelector(`[data-proxy=${id}]`); if(b&&$(id)) b.textContent=$(id).textContent; }); });
  mm.addEventListener('click',e=>{ const b=e.target.closest('.mi'); if(!b) return; mm.hidden=true; if(b.dataset.view){ (view==='grid'?$('vList'):$('vGrid')).click(); return; } const t=$(b.dataset.proxy); if(t) t.click(); }); }
 // the phone action bar: the same actions as the top bar's buttons (collection view: Tray, Loadouts, Share, Menu; loadout editor: Tray, HOME, Install, Menu)
 on('mbTray',()=>{ closeMenus(); $('trayBtn').click(); });
 on('mbLo',e=>{ e.stopPropagation(); const m=$('loMenu'); const open=!!m&&!m.hidden; closeMenus(); if(!open) $('loBtn').click(); });
 on('mbShare',()=>{ closeMenus(); if(typeof ccsShareDialog==='function') ccsShareDialog(); });
 on('mbHome',()=>{ closeMenus(); $('homeBtn').click(); });
 on('mbInstall',()=>{ closeMenus(); $('hkBtn').click(); });
 on('mbMenu',e=>{ e.stopPropagation(); const m=$('appMenu'); const open=!!m&&!m.hidden; closeMenus(); if(!open) $('appBtn').click(); });
 const _render=render; render=function(){ const r=_render.apply(this,arguments); try{ const n=$('mbTrayN'); if(n) n.textContent=$('trayBadge').textContent; }catch(e){} return r; };
 // technical columns in the list (built-in status, ROM identity, emulator, size): off unless asked for in Filter & Sort
 const TKEY='studio-techcols-v1'; const tc=$('techCols'); const techOn=v=>{ document.body.classList.toggle('techcols',!!v); try{ localStorage.setItem(TKEY,v?'1':'0'); }catch(e){} };
 let t0=false; try{ t0=localStorage.getItem(TKEY)==='1'; }catch(e){} techOn(t0); if(tc){ tc.checked=t0; tc.onchange=()=>techOn(tc.checked); }
 // phones open on covers (grid) unless the person chose a view before
 try{ if(matchMedia('(max-width:640px)').matches&&!localStorage.getItem(VKEY)&&view!=='grid'){ $('vGrid').click(); } }catch(e){}
 // keyboard: the new menus behave like the existing ones (arrows, Escape)
 // the no-results state: one tap back to games
if($('nmSearch')) $('nmSearch').onclick=()=>{ $('q').value=''; render(); $('q').focus(); };
if($('nmFilters')) $('nmFilters').onclick=()=>{ FILTERS.forEach(([id])=>$(id).value=FDEF[id]||''); render(); };
updateStatusLine(); csMenuRefresh();
})();
