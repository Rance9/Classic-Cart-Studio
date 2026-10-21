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
  $('guideArt').querySelectorAll('.shot img, .hero-fig img, .hw .card img').forEach(im=>{ if(im.dataset.zoom) return; im.dataset.zoom='1'; im.tabIndex=0; im.setAttribute('role','button'); im.title='Enlarge'; im.setAttribute('aria-label','Enlarge: '+(im.alt||'screenshot')); }); };
 const _show=showChapter; showChapter=function(k){ _show(k); const s=$('guideSel'); if(s) s.value=k; const n=GUIDE_CH.findIndex(x=>x[0]===k); if($('gPrev')) $('gPrev').disabled=n<=0; if($('gNext')) $('gNext').disabled=n>=GUIDE_CH.length-1; };
 // ---- the screenshot viewer: Fit, 100% and zoom steps; pan by touch, drag or scroll; the original image file is shown unchanged ----
 const lb=document.createElement('div'); lb.id='glight'; lb.setAttribute('role','dialog'); lb.setAttribute('aria-modal','true'); lb.setAttribute('aria-label','Screenshot viewer');
 lb.innerHTML='<div class="glbar"><span class="glcap" id="glCap"></span><div class="glz" role="group" aria-label="Zoom"><button type="button" class="glb" id="glOut" aria-label="Zoom out">−</button><span class="glpct" id="glPct" aria-live="polite">100%</span><button type="button" class="glb" id="glIn" aria-label="Zoom in">+</button><button type="button" class="glb glm" id="glFit" aria-pressed="true">Fit</button><button type="button" class="glb glm" id="glOne" aria-pressed="false">100%</button></div><button type="button" class="glb glx" id="glClose">Close ✕</button></div><div class="glview" id="glView" tabindex="0" aria-label="Screenshot. Scroll or drag to move around."><img id="glImg" alt="" draggable="false"></div><div class="glhint" id="glHint" aria-hidden="true">Drag or scroll to see the rest</div>';
 document.body.appendChild(lb);
 const V=$('glView'), I=$('glImg'); const Z={scale:1, fit:1, mode:'fit', back:null};
 const STEPS=[0.25,0.5,0.75,1,1.5,2,3];
 const nat=()=>[I.naturalWidth||1, I.naturalHeight||1];
 const fitScale=()=>{ const [w,h]=nat(); return Math.min(1, (V.clientWidth-16)/w, (V.clientHeight-16)/h); };
 const over=()=>V.scrollWidth>V.clientWidth+2||V.scrollHeight>V.clientHeight+2;
 function apply(scale, mode, anchor){ const [w]=nat(); const prev=Z.scale; Z.scale=Math.max(0.1, Math.min(4, scale)); Z.mode=mode;
  // keep the point under the anchor (or the center) in place while zooming
  const ax=anchor?anchor.x:V.clientWidth/2, ay=anchor?anchor.y:V.clientHeight/2; const fx=(V.scrollLeft+ax)/Math.max(1,V.scrollWidth), fy=(V.scrollTop+ay)/Math.max(1,V.scrollHeight);
  I.style.width=Math.round(w*Z.scale)+'px'; I.style.height='auto';
  $('glPct').textContent=Math.round(Z.scale*100)+'%'; $('glFit').setAttribute('aria-pressed', String(mode==='fit')); $('glOne').setAttribute('aria-pressed', String(mode==='one'));
  $('glOut').disabled=Z.scale<=Math.min(Z.fit, STEPS[0])+1e-3; $('glIn').disabled=Z.scale>=STEPS[STEPS.length-1]-1e-3;
  if(prev!==Z.scale){ V.scrollLeft=fx*V.scrollWidth-ax; V.scrollTop=fy*V.scrollHeight-ay; }
  lb.classList.toggle('pan', over()); }
 const step=(dir, anchor)=>{ const cur=Z.scale; const lo=Math.min(Z.fit, STEPS[0]); const next=dir>0?STEPS.find(x=>x>cur*1.25):[...STEPS].reverse().find(x=>x<cur/1.25);   /* each step is a visible change */
  apply(next===undefined?(dir>0?cur:lo):Math.max(lo,next), Math.abs((next||0)-1)<1e-3?'one':'zoom', anchor); };
 function initial(){ Z.fit=fitScale(); const [w,h]=nat();
  // a very wide or very tall figure would be an unreadable strip when fitted: start at a readable size instead, at its beginning
  const minSide=Math.min(w,h)*Z.fit, readable=Math.min(1.5, Math.max(Z.fit, 170/Math.min(w,h)));
  if(Z.fit<0.75&&minSide<120&&readable>Z.fit*1.3){ apply(readable, Math.abs(readable-1)<1e-3?'one':'zoom'); V.scrollLeft=0; V.scrollTop=0; const hint=$('glHint'); hint.classList.add('show'); clearTimeout(hint._t); hint._t=setTimeout(()=>hint.classList.remove('show'), 2600); }
  else apply(Z.fit, 'fit'); }
 const openLb=im=>{ Z.back=im; I.removeAttribute('style'); I.onload=null; I.src=im.currentSrc||im.src; I.alt=im.alt||''; $('glCap').textContent=im.alt||''; lb.classList.add('open'); $('glHint').classList.remove('show');
  const go=()=>requestAnimationFrame(initial); if(I.complete&&I.naturalWidth) go(); else I.onload=go; $('glClose').focus(); };
 const closeLb=()=>{ lb.classList.remove('open'); $('glHint').classList.remove('show'); const b=Z.back; Z.back=null; if(b&&b.focus) b.focus({preventScroll:true}); };
 $('glClose').onclick=closeLb; $('glFit').onclick=()=>apply(fitScale(),'fit'); $('glOne').onclick=()=>apply(1,'one'); $('glIn').onclick=()=>step(1); $('glOut').onclick=()=>step(-1);
 addEventListener('resize',()=>{ if(!lb.classList.contains('open')) return; Z.fit=fitScale(); if(Z.mode==='fit') apply(Z.fit,'fit'); else lb.classList.toggle('pan', over()); });
 // double-click or double-tap switches between Fit and 100% at that point
 I.addEventListener('dblclick',e=>{ const r=V.getBoundingClientRect(); const a={x:e.clientX-r.left, y:e.clientY-r.top}; if(Z.mode==='fit') apply(1,'one',a); else apply(fitScale(),'fit',a); });
 // mouse: drag to pan; Ctrl + wheel zooms (touch scrolling is native)
 let drag=null; V.addEventListener('pointerdown',e=>{ if(e.pointerType!=='mouse'||e.button!==0||!over()) return; drag={x:e.clientX,y:e.clientY,l:V.scrollLeft,t:V.scrollTop}; V.setPointerCapture(e.pointerId); lb.classList.add('dragging'); e.preventDefault(); });
 V.addEventListener('pointermove',e=>{ if(!drag) return; V.scrollLeft=drag.l-(e.clientX-drag.x); V.scrollTop=drag.t-(e.clientY-drag.y); });
 const endDrag=()=>{ drag=null; lb.classList.remove('dragging'); }; V.addEventListener('pointerup',endDrag); V.addEventListener('pointercancel',endDrag);
 V.addEventListener('wheel',e=>{ if(!e.ctrlKey) return; e.preventDefault(); const r=V.getBoundingClientRect(); step(e.deltaY<0?1:-1,{x:e.clientX-r.left,y:e.clientY-r.top}); },{passive:false});
 lb.addEventListener('click',e=>{ if(e.target===lb) closeLb(); });
 $('guideArt').addEventListener('click',e=>{ const im=e.target.closest('.shot img, .hero-fig img, .hw .card img'); if(im){ e.preventDefault(); openLb(im); } });
 $('guideArt').addEventListener('keydown',e=>{ const im=e.target.closest&&e.target.closest('.shot img, .hero-fig img, .hw .card img'); if(im&&(e.key==='Enter'||e.key===' ')){ e.preventDefault(); openLb(im); } });
 // keys while the viewer is open: Esc closes only the viewer; + and - zoom, 0 fits, 1 shows 100%; arrows move the image; Tab stays inside
 document.addEventListener('keydown',e=>{ if(!lb.classList.contains('open')) return; const k=e.key; const stop=()=>{ e.preventDefault(); e.stopImmediatePropagation(); };
  if(k==='Escape'){ stop(); closeLb(); return; }
  if(k==='+'||k==='='){ stop(); step(1); return; }
  if(k==='-'||k==='_'){ stop(); step(-1); return; }
  if(k==='0'){ stop(); apply(fitScale(),'fit'); return; }
  if(k==='1'){ stop(); apply(1,'one'); return; }
  if(k.startsWith('Arrow')){ stop(); const d=80; V.scrollBy(k==='ArrowLeft'?-d:k==='ArrowRight'?d:0, k==='ArrowUp'?-d:k==='ArrowDown'?d:0); return; }
  if(k==='Tab'){ const f=[...lb.querySelectorAll('button:not([disabled]), #glView')]; const i=f.indexOf(document.activeElement); stop(); f[(i+(e.shiftKey?-1:1)+f.length)%f.length].focus(); } }, true);
})();
updateStatusLine(); csMenuRefresh();
})();
