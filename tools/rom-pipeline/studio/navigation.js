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
// ---- toasts never cover an open dialog: opening a dialog dismisses a leftover toast; a toast raised while a dialog is open goes to
//      free space above or below it, or (no room) is announced to screen readers only ----
const TOAST_DLG='.modal.open .mbox, .modal.open>.box, #details.open .box, #hkpanel.open .mbox, .ccsack.open>*, #tv.open .tvbox, #tray.open, .menu:not([hidden]), #glight.open';
function openDialogRect(){ const r=[...document.querySelectorAll(TOAST_DLG)].map(e=>e.getBoundingClientRect()).filter(b=>b.width>0&&b.height>0); if(!r.length) return null;
 return r.reduce((a,b)=>({top:Math.min(a.top,b.top), bottom:Math.max(a.bottom,b.bottom), left:Math.min(a.left,b.left), right:Math.max(a.right,b.right)})); }
toast=(function(orig){ return function(m){ const t=$('toast'); orig(m); const d=openDialogRect(); t.classList.remove('sr'); t.style.removeProperty('top'); t.style.removeProperty('bottom');
 if(!d) return; const h=t.offsetHeight||40, gap=8;
 if(d.top>=h+2*gap){ t.style.setProperty('top',gap+'px','important'); t.style.setProperty('bottom','auto','important'); }
 else if(innerHeight-d.bottom>=h+2*gap){ t.style.setProperty('top','auto','important'); t.style.setProperty('bottom',gap+'px','important'); }
 else t.classList.add('sr'); }; })(toast);
(function(){ let wasOpen=false; const mo=new MutationObserver(()=>{ const open=!!document.querySelector(TOAST_DLG); if(open&&!wasOpen){ const t=$('toast'); if(t&&t.classList.contains('show')){ t.classList.remove('show'); clearTimeout(t._h); } } wasOpen=open; });
 mo.observe(document.body,{subtree:true, attributes:true, attributeFilter:['class','hidden']}); })();
// Install on NES Classic: the compact step header's "All Steps" shows or hides the eight-step list (phones)
document.addEventListener('click',e=>{ const b=e.target.closest&&e.target.closest('.stepall'); if(!b) return; INST.showSteps=!INST.showSteps; const w=b.closest('.stepwrap'); if(w) w.classList.toggle('all', !!INST.showSteps); b.setAttribute('aria-expanded', INST.showSteps?'true':'false'); });
// ---- Help & User Guide on narrow screens: one header row with a chapter selector and Previous/Next; every screenshot opens enlarged ----
(function(){ const hd=document.querySelector('#guide .ghd'); if(!hd||!$('guideClose')) return;
 const box=document.createElement('div'); box.className='gmob';
 box.innerHTML='<b class="gmt" aria-hidden="true">Help</b><button type="button" class="btn sm gstep" id="gPrev" aria-label="Previous chapter">‹</button><label class="gsel"><span class="vh">Chapter</span><select id="guideSel"></select></label><button type="button" class="btn sm gstep" id="gNext" aria-label="Next chapter">›</button>';
 hd.insertBefore(box, $('guideClose'));
 $('guideSel').onchange=()=>showChapter($('guideSel').value); $('gPrev').onclick=()=>guideStep(-1); $('gNext').onclick=()=>guideStep(1);
 const fill=()=>{ let html='', grp=null; GUIDE_CH.forEach(([k,l],n)=>{ if(GUIDE_GROUPS[n]){ if(grp) html+='</optgroup>'; grp=GUIDE_GROUPS[n]; html+=`<optgroup label="${esc(grp)}">`; } html+=`<option value="${k}">${String(n+1).padStart(2,'0')} · ${esc(l)}</option>`; }); if(grp) html+='</optgroup>'; $('guideSel').innerHTML=html; };
 const _open=openGuide; openGuide=function(ch){ if(!$('guideSel').options.length) fill(); _open(ch);
  $('guideArt').querySelectorAll('.shot img, .hero-fig img').forEach(im=>{ if(im.dataset.zoom) return; im.dataset.zoom='1'; im.tabIndex=0; im.setAttribute('role','button'); im.title='Enlarge'; im.setAttribute('aria-label','Enlarge: '+(im.alt||'screenshot')); }); };
 const _show=showChapter; showChapter=function(k){ _show(k); const s=$('guideSel'); if(s) s.value=k; const n=GUIDE_CH.findIndex(x=>x[0]===k); if($('gPrev')) $('gPrev').disabled=n<=0; if($('gNext')) $('gNext').disabled=n>=GUIDE_CH.length-1; };
 // the enlarged view: fit to the screen, or actual size with scrolling in both directions
 const lb=document.createElement('div'); lb.id='glight'; lb.setAttribute('role','dialog'); lb.setAttribute('aria-modal','true'); lb.setAttribute('aria-label','Enlarged screenshot');
 lb.innerHTML='<div class="glbar"><span class="glcap" id="glCap"></span><button type="button" class="btn sm" id="glSize">Actual Size</button><button type="button" class="btn sm" id="glClose">Close</button></div><div class="glview" id="glView"><img id="glImg" alt=""></div>';
 document.body.appendChild(lb); let back=null;
 const setFit=fit=>{ lb.classList.toggle('actual',!fit); $('glSize').textContent=fit?'Actual Size':'Fit to Screen'; };
 const openLb=im=>{ back=im; $('glImg').src=im.currentSrc||im.src; $('glImg').alt=im.alt||''; $('glCap').textContent=im.alt||''; lb.classList.add('open'); setFit(true); requestAnimationFrame(()=>{ if($('glImg').complete) autoSize(); }); $('glClose').focus(); };
 const autoSize=()=>{ const v=$('glView'), i=$('glImg'), w=i.naturalWidth||0, h=i.naturalHeight||0; if(w&&h&&Math.min(v.clientWidth/w, v.clientHeight/h)<0.45) setFit(false); };   /* a very wide figure starts at actual size, scrollable */
 $('glImg').addEventListener('load',()=>{ if(lb.classList.contains('open')) autoSize(); });
 const closeLb=()=>{ lb.classList.remove('open'); if(back&&back.focus) back.focus(); back=null; };
 $('glClose').onclick=closeLb; $('glSize').onclick=()=>setFit(lb.classList.contains('actual')); $('glImg').onclick=()=>setFit(lb.classList.contains('actual'));
 lb.addEventListener('click',e=>{ if(e.target===lb||e.target.id==='glView') closeLb(); });
 $('guideArt').addEventListener('click',e=>{ const im=e.target.closest('.shot img, .hero-fig img'); if(im){ e.preventDefault(); openLb(im); } });
 $('guideArt').addEventListener('keydown',e=>{ const im=e.target.closest&&e.target.closest('.shot img, .hero-fig img'); if(im&&(e.key==='Enter'||e.key===' ')){ e.preventDefault(); openLb(im); } });
 document.addEventListener('keydown',e=>{ if(!lb.classList.contains('open')) return; if(e.key==='Escape'){ e.stopImmediatePropagation(); e.preventDefault(); closeLb(); } else if(e.key==='ArrowLeft'||e.key==='ArrowRight') e.stopImmediatePropagation(); else if(e.key==='Tab'){ e.preventDefault(); (document.activeElement===$('glClose')?$('glSize'):$('glClose')).focus(); } }, true);
})();
updateStatusLine(); csMenuRefresh();
})();
