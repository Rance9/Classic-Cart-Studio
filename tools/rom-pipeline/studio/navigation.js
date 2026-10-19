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
 const shareVis=on=>{ const m=$('mShare'); if(m) m.hidden=!on; document.body.classList.toggle('noshare',!on); };
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
 // keyboard: the new menus behave like the existing ones (arrows, Escape)
 updateStatusLine(); csMenuRefresh();
})();
