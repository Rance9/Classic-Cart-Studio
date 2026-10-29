// ---- top navigation (one implementation for the website and the Windows app): My Collections (left), the collection's console
//      actions (Tray, NES Classic Loadouts, Preview on TV, Console Menu Setup, Install), the Studio menu (Home, help, backup, legal); the Game Library
//      tools below (search, Filter & Sort, Select = Game Library actions, List/Grid, Add Your Own Game). Capability differences only hide
//      what cannot work.
(function(){
 const on=(id,fn)=>{ const b=$(id); if(b) b.onclick=fn; };
 const closeMenus=()=>document.querySelectorAll('.menu').forEach(x=>x.hidden=true);
 on('homeBtn',()=>{ closeMenus(); if(typeof openFolders==='function') openFolders(); });
 on('ownBtn',()=>{ closeMenus(); addOwnGame(); });
 // the legacy hidden controls other parts still click
 on('export',()=>csExport()); on('importBtn',()=>csImportPick());
 // Favorites from the Select menu: a view of the library (the Selection filter "Favorites only"); it never changes what is selected
 function favActive(){ const f=$('fSel'); return !!f&&f.value==='fav'; }
 function favLabel(){ const b=$('favView'); if(!b) return; const n=fav.size; miLabel(b, favActive()?'Show All Games':`Show Favorites${n?` (${n})`:''}`); b.setAttribute('aria-pressed',String(favActive())); }
 window.showFavorites=function(onOff){ const f=$('fSel'); if(!f) return; f.value=onOff?'fav':''; try{ f.dispatchEvent(new Event('change',{bubbles:true})); }catch(e){} render(); favLabel();
  if(onOff&&!fav.size) toast('No favorites yet: click the ★ on any game to add it'); };
 on('favView',e=>{ e.stopPropagation(); closeMenus(); window.showFavorites(!favActive()); });
 wireMenu('selectBtn','selectMenu'); const sb=$('selectBtn'); if(sb) sb.addEventListener('click',favLabel);
 const fs=$('fSel'); if(fs) fs.addEventListener('change',favLabel); favLabel();
 // My Collections: the menu shows the open collection and the quick list each time it opens
 const cb=$('collBtn'); if(cb) cb.addEventListener('click',()=>{ csSaveActiveNow(); csMenuRefresh(); updateStatusLine(); });
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
 // phones: the ⋯ menu of the library row is the same list of Game Library actions as Select (build_html.py LIB_ACTIONS), plus the
 // controls a phone does not show (List/Grid, Add Your Own Game); each row clicks the real control, so labels and handlers never drift
 const mm=$('moreMenu'); if(mm){ const vt=mm.querySelector('[data-view]');
  $('moreBtn').addEventListener('click',()=>{ favLabel(); if(vt){ miLabel(vt, view==='grid'?'List View':'Grid View'); const ic=vt.querySelector('.mic'); if(ic) ic.textContent=view==='grid'?'☰':'▦'; }
   mm.querySelectorAll('[data-proxy]').forEach(b=>{ const t=$(b.dataset.proxy); if(!t) return; const tb=t.querySelector('.mtx b'); if(tb) miLabel(b, tb.textContent); if(t.hasAttribute('aria-label')) b.setAttribute('aria-label', t.getAttribute('aria-label')); if(t.hasAttribute('aria-pressed')) b.setAttribute('aria-pressed', t.getAttribute('aria-pressed')); b.disabled=t.disabled; }); });
  wireMenu('moreBtn','moreMenu');
  mm.addEventListener('click',e=>{ const b=e.target.closest('.mi'); if(!b) return; mm.hidden=true; if(b.dataset.view){ (view==='grid'?$('vList'):$('vGrid')).click(); return; } const t=$(b.dataset.proxy); if(t) t.click(); }); }
 // the phone action bar: Home first, then the top bar's actions (collection view: Tray, Loadouts, Share, Studio; loadout editor: Tray,
 // Console Menu (Setup), Install, Studio). Home is the title link's action (D-62): an open loadout is saved and closed, the library at the top
 on('mbGoHome',()=>{ closeMenus(); goHome(); }); on('mGoHome',()=>{ closeMenus(); goHome(); });
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
 const openLb=(im, label)=>{ lb.setAttribute('aria-label', label||'Screenshot viewer'); Z.back=im; I.removeAttribute('style'); I.onload=null; I.src=im.currentSrc||im.src; I.alt=im.alt||''; $('glCap').textContent=im.alt||''; lb.classList.add('open'); $('glHint').classList.remove('show');
  const go=()=>requestAnimationFrame(initial); if(I.complete&&I.naturalWidth) go(); else I.onload=go; $('glClose').focus(); };
 const closeLb=()=>{ lb.classList.remove('open'); $('glHint').classList.remove('show'); const b=Z.back; Z.back=null; if(b&&b.focus) b.focus({preventScroll:true}); };
 window.openImageViewer=(im, label, back)=>{ if(!im) return; openLb(im, label); if(back) Z.back=back; };   // also the full box art from Game Details (D-61): Close returns focus to its button
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
// ---- ordinary dialogs (the generic dialog #collm, Install on NES Classic, Filter & Sort, Game Details, the welcome screen, the browser
//      save-name dialog): one keyboard model for all of them. Only the TOP open surface takes part. Focus enters it; Tab and Shift+Tab stay
//      inside it (native ⋯ summaries and links included); Escape closes only it; closing returns focus to what opened it (a menu row
//      counts as its menu's button; a control that was redrawn is found again, a removed one is not used). Surfaces that manage their own
//      keyboard (Technical Details, the ROM acknowledgment, the guide, the screenshot viewer, Preview on TV, the artwork editor) are left
//      to it. A dialog running an operation (PROC) keeps its lock: Escape, the backdrop and the header Close do nothing until its Cancel. ----
// ---- the page stays still behind every blocking surface (website and Windows app, phone to desktop). One lock, derived from what is
//      open: any open dialog or full-screen panel, the open Tray at every width (a phone sheet or a docked right pane alike), and menus
//      where they are sheets over a dimmed page (a desktop dropdown is not blocking). Two layers:
//      (1) the root element stops scrolling (overflow:hidden on <html>, never on <body>, so the sticky toolbar and the game list keep
//      their place; the scroll offset is kept, the scrollbar's width is padded on <html> so nothing reflows and the docked Tray's own
//      padding on <body> is untouched); (2) a touch/wheel guard for browsers
//      that still move the page under a finger (iPhone Safari): a gesture is cancelled unless something inside the topmost surface can
//      scroll that way (also at either end of its content). Two-finger pinch zoom is never touched. Closing the last surface releases it.
//      Operation locks (PROC), focus containment and Escape are separate and unchanged. ----
(function(){
 const BLOCK=['#collm','#hkpanel','#filters','#details','#welcome','#savem','#tech','#guide','#glight','#tv','#ovArt','.ccsack'];
 const SHEETS=matchMedia('(max-width:640px), (max-height:500px) and (max-width:1000px)');
 const open=el=>!!el&&el.classList.contains('open')&&!el.hidden&&getComputedStyle(el).display!=='none';
 const z=el=>{ const v=parseInt(getComputedStyle(el).zIndex,10); return isNaN(v)?0:v; };
 function surfaces(){ const list=[]; BLOCK.forEach(s=>document.querySelectorAll(s).forEach(el=>{ if(open(el)) list.push(el); }));
  if(SHEETS.matches) document.querySelectorAll('.menu:not([hidden])').forEach(m=>{ if(m.getClientRects().length) list.push(m); });
  const t=$('tray'); if(t&&t.classList.contains('open')) list.push(t);   // every width and orientation (D-60): the library behind stays still
  return list; }
 function top(list){ return list.reduce((a,b)=>!a||z(b)>z(a)||(z(b)===z(a)&&(a.compareDocumentPosition(b)&Node.DOCUMENT_POSITION_FOLLOWING))?b:a, null); }
 let locked=false, pad=null, topEl=null;
 const mode=()=>window.__lockTestMode||'full';   // test hook: 'css' or 'guard' exercises one layer alone
 function sync(){ const list=surfaces(); topEl=top(list); const on=list.length>0; const h=document.documentElement, b=document.body;
  const css=on&&mode()!=='guard';
  if(css&&!h.classList.contains('page-locked')){ const sw=innerWidth-h.clientWidth; pad=h.style.paddingRight; if(sw>0) h.style.paddingRight=sw+'px'; h.classList.add('page-locked'); }
  else if(!css&&h.classList.contains('page-locked')){ h.classList.remove('page-locked'); h.style.paddingRight=pad||''; pad=null; }
  locked=on; }
 // would this gesture scroll something inside the topmost surface? dx/dy: the content movement (finger direction)
 // the browser scrolls the NEAREST scrollable element on the gesture's axis (and carries on to the page from there), so only that one counts
 function scrollsInside(target, dx, dy){ if(!topEl||!target||!topEl.contains(target)) return false; const vert=Math.abs(dy)>=Math.abs(dx);
  for(let el=target; el&&el!==document.body; el=el.parentElement){ const cs=getComputedStyle(el);
   if(vert&&/(auto|scroll)/.test(cs.overflowY)&&el.scrollHeight>el.clientHeight+1) return dy<0?el.scrollTop+el.clientHeight<el.scrollHeight-1:dy>0?el.scrollTop>0:false;
   if(!vert&&/(auto|scroll)/.test(cs.overflowX)&&el.scrollWidth>el.clientWidth+1) return dx<0?el.scrollLeft+el.clientWidth<el.scrollWidth-1:dx>0?el.scrollLeft>0:false;
   if(el===topEl) break; }
  return false; }
 // a gesture that starts on a form control or editable text (a slider drag, caret or selection) or inside an element that handles its own
 // touch (touch-action none, e.g. the box-art crop frame) is that control's; the browser does not scroll the page for it, so it is left alone
 function ownsTouch(target){ if(!topEl||!target||!topEl.contains(target)) return false;
  if(target.closest('input,textarea,select,[contenteditable]:not([contenteditable="false"])')) return true;
  for(let el=target; el&&el!==document.body; el=el.parentElement){ if(/^(none|pinch-zoom)$/.test(getComputedStyle(el).touchAction)) return true; if(el===topEl) break; }
  return false; }
 // one-finger gesture state. Each move is judged by its CURRENT direction (since the previous move), so a finger that reaches the end
 // of a list and turns back without lifting scrolls the list again. A second finger (pinch zoom) ends tracking until a new touch starts.
 let g=null;
 document.addEventListener('touchstart',e=>{ const t=e.touches[0]; g=e.touches.length===1?{id:t.identifier, x:t.clientX, y:t.clientY, stop:false}:null; },{capture:true, passive:true});
 const lift=()=>{ g=null; };
 document.addEventListener('touchend',lift,{capture:true, passive:true}); document.addEventListener('touchcancel',lift,{capture:true, passive:true});
 document.addEventListener('touchmove',e=>{ if(!g) return; if(e.touches.length!==1||e.touches[0].identifier!==g.id){ g=null; return; } const t=e.touches[0];
  const dx=t.clientX-g.x, dy=t.clientY-g.y;
  if(Math.abs(dx)<1&&Math.abs(dy)<1){ if(g.stop&&e.cancelable) e.preventDefault(); return; }   // no real movement yet: keep the last decision
  g.x=t.clientX; g.y=t.clientY;
  if(!locked||mode()==='css'||!e.cancelable){ g.stop=false; return; }
  g.stop=!ownsTouch(e.target)&&!scrollsInside(e.target, dx, dy); if(g.stop) e.preventDefault(); },{capture:true, passive:false});
 document.addEventListener('wheel',e=>{ if(!locked||mode()==='css'||e.ctrlKey||!e.cancelable) return; if(!scrollsInside(e.target, -e.deltaX, -e.deltaY)) e.preventDefault(); },{capture:true, passive:false});
 new MutationObserver(sync).observe(document.body,{subtree:true, attributes:true, attributeFilter:['class','hidden']});
 [SHEETS].forEach(m=>{ try{ m.addEventListener('change', sync); }catch(e){ m.addListener(sync); } });
 window.pageLock={sync, state:()=>({locked, css:document.documentElement.classList.contains('page-locked'), top:topEl&&(topEl.id||topEl.className)})}; sync(); })();
// ---- the branding is Home, on every device: the Game Library at the top. A loadout being edited is saved and closed (Back to Collection);
//      menus, panels and dialogs close. A running operation keeps its lock and unsaved text asks first, as their own Close does. The website
//      goes to its root page ("./": the plain "/" in production, sub-path safe) when it is on another address (a test page keeps only its companion port); the Windows app stays in its
//      own window and never navigates. Modified clicks on the website open "/" as a normal link. ----
(function(){ const a=$('brandHome'); if(!a) return;
 const target=()=>{ if(CO.desktop) return null; const keep=new URLSearchParams(location.search).get('companion'); const want=new URL('./', location.href).pathname+(keep?'?companion='+encodeURIComponent(keep):''); return (location.pathname+location.search+location.hash)===want?null:want; };
 window.goHome=function(){
  if(PROC.collm||PROC.hkpanel){ toast('Finish or cancel the current operation first'); return false; }
  if(typeof ovTextDirty==='function'&&ovTextDirty()){ if(!confirm(OV_DISCARD)) return false; try{ OVED=null; }catch(e){} }
  if(typeof OVA!=='undefined'&&OVA&&typeof ovArtClose==='function'&&ovArtClose()===false) return false;
  document.querySelectorAll('.menu').forEach(m=>m.hidden=true); if(typeof closeVerPop==='function') closeVerPop();
  try{ if($('tech').classList.contains('open')&&typeof closeTech==='function') closeTech(); }catch(e){}
  try{ if(typeof TV!=='undefined'&&TV.open&&typeof closeTv==='function') closeTv(); }catch(e){}
  try{ if($('guide').classList.contains('open')&&typeof closeGuide==='function') closeGuide(); }catch(e){}
  try{ if($('filters').classList.contains('open')&&typeof fcancel==='function') fcancel(); }catch(e){}
  ['details','hkpanel','tray','glight'].forEach(id=>{ const x=$(id); if(x) x.classList.remove('open'); });
  if($('collm').classList.contains('open')) closeColl();
  if(typeof LO!=='undefined'&&LO&&typeof loClose==='function') loClose();
  const go=target(); if(go){ location.assign(go); return true; }
  try{ if(typeof VL!=='undefined'&&VL.hold){ VL.hold=false; clearTimeout(VL.holdT); } }catch(e){}   // a list restore still settling never pulls Home back down
  window.scrollTo({top:0, left:0, behavior:'instant'}); return true; };
 a.addEventListener('click', e=>{ if(e.button>0||e.metaKey||e.ctrlKey||e.shiftKey||e.altKey){ if(CO.desktop) e.preventDefault(); return; } e.preventDefault(); goHome(); }); })();
(function(){
 const MANAGED={collm:{esc:()=>{ if(PROC.collm) return; const c=[...$('collBody').querySelectorAll('[data-cancel]')].find(x=>!x.disabled&&x.getClientRects().length); if(c) c.click(); else closeColl(); }},
  hkpanel:{esc:()=>{ if(!PROC.hkpanel) $('hkpanel').classList.remove('open'); }}, filters:{esc:()=>{ if(typeof fcancel==='function') fcancel(); }},
  details:{esc:()=>$('details').classList.remove('open')}, welcome:{esc:()=>doneWelcome()}, savem:{esc:null}};
 const SELF=['tech','ccsAck','guide','glight','tv','ovArt'];
 const isOpen=el=>!!el&&el.classList.contains('open')&&!el.hidden;
 const z=el=>{ const v=parseInt(getComputedStyle(el).zIndex,10); return isNaN(v)?0:v; };
 function topSurface(){ const all=Object.keys(MANAGED).concat(SELF).map(id=>document.getElementById(id)).filter(isOpen); if(!all.length) return null;
  return all.reduce((a,b)=>z(b)>z(a)||(z(b)===z(a)&&(a.compareDocumentPosition(b)&Node.DOCUMENT_POSITION_FOLLOWING))?b:a); }
 window.dlgTop=topSurface;
 const visible=el=>!!el&&el.isConnected&&el.getClientRects().length>0&&getComputedStyle(el).visibility!=='hidden'&&!el.closest('[inert],[hidden]');
 function focusables(root){ return [...root.querySelectorAll('a[href],button,input,select,textarea,summary,[tabindex]')].filter(x=>x.tabIndex>=0&&!x.disabled&&visible(x)&&!(x.type==='radio'&&!x.checked&&root.querySelector(`input[type=radio][name="${x.name}"]:checked`))
  &&!(x.closest('details:not([open])')&&x.tagName!=='SUMMARY')&&!(x.tagName==='SUMMARY'&&x.parentElement.closest('details:not([open])')&&x.parentElement.closest('details:not([open])')!==x.parentElement)); }
 window.dlgFocusables=focusables;
 // the element that should get focus back: a menu row stands for the menu's button; a redrawn control is found again by id or data key
 const KEYS=['data-id','data-g','data-open','data-cs','data-lid','data-lo','data-a'];   // data-open: a game's title (the Game Details button)
 function reattach(el){ if(!el) return null; if(el.isConnected) return el; if(el.id){ const n=document.getElementById(el.id); if(n) return n; }
  for(const k of KEYS){ const v=el.getAttribute&&el.getAttribute(k); if(v){ const n=[...document.querySelectorAll(`${el.tagName.toLowerCase()}[${k}="${CSS.escape(v)}"]`)].find(visible); if(n) return n; } } return null; }
 function opener(el){ const m=el&&el.closest&&el.closest('.menu'); if(m) return m._opener||null; return el; }
 let cur=null, prev=null; document.addEventListener('focusin',e=>{ if(e.target!==cur){ prev=cur; cur=e.target; } }, true);
 const RET={};
 function settle(id){ const m=document.getElementById(id); if(!m) return;
  if(isOpen(m)){ if(!(id in RET)||RET[id]===undefined){ const was=cur&&m.contains(cur)?prev:cur; RET[id]=opener(was)||null; }
   if(!m.contains(document.activeElement)){ const f=focusables(m)[0]; if(f) f.focus({preventScroll:true}); } }
  else if(id in RET){ const r=RET[id]; delete RET[id]; const a=document.activeElement;
   if(a&&a!==document.body&&!m.contains(a)&&visible(a)) return;   // something else (the next dialog, the editor) already took focus
   const t=topSurface(); if(t&&t!==m){ if(!t.contains(document.activeElement)){ const f=focusables(t)[0]; if(f) f.focus({preventScroll:true}); } return; }
   const back=[reattach(r), $('collBtn')].find(visible); if(back) back.focus({preventScroll:true}); } }
 new MutationObserver(ms=>{ const ids=new Set(); ms.forEach(x=>{ if(x.target.id in MANAGED) ids.add(x.target.id); }); ids.forEach(settle); })
  .observe(document.body,{subtree:true, attributes:true, attributeFilter:['class']});
 // wireMenu remembers who opened a menu (a phone bar button opens the same menu through the toolbar button)
 document.addEventListener('click',e=>{ const b=e.target.closest&&e.target.closest('[aria-haspopup="menu"],#mbLo,#mbMenu'); if(!b) return; setTimeout(()=>{ document.querySelectorAll('.menu:not([hidden])').forEach(m=>{ m._opener=b; }); },0); }, true);
 document.addEventListener('keydown',e=>{ if(e.key!=='Tab'&&e.key!=='Escape') return; if(document.querySelector('.menu:not([hidden])')) return;   // an open menu handles its own keys
  if(e.key==='Escape'&&document.querySelector('#tip.open,#verpop.open')) return;   // a pinned tooltip or the version popover is above the dialog: Escape closes it first (their own handlers)
  const t=topSurface(); if(!t||SELF.includes(t.id)) return; const cfg=MANAGED[t.id];
  if(e.key==='Escape'){ if(e.target.closest&&e.target.closest('details.csmore[open]')) return; e.preventDefault(); e.stopImmediatePropagation(); if(cfg.esc) cfg.esc(); else if(t.onkeydown) t.onkeydown(e); return; }
  const f=focusables(t); if(!f.length) return; const i=f.indexOf(document.activeElement); e.preventDefault();
  (i<0?(e.shiftKey?f[f.length-1]:f[0]):f[(i+(e.shiftKey?-1:1)+f.length)%f.length]).focus(); }, true);
})();
// ---- dialogs fit the window that is actually visible (short landscape windows, a software keyboard): --vvh is the visible height and
//      --kb what a keyboard covers at the bottom; studio.css sizes dialogs with them, and a focused field is kept in view ----
(function(){ const vv=window.visualViewport; const set=()=>{ const h=vv?vv.height:innerHeight; document.documentElement.style.setProperty('--vvh', Math.round(h)+'px'); document.documentElement.style.setProperty('--kb', Math.max(0, Math.round(innerHeight-h-(vv?vv.offsetTop:0)))+'px'); };
 set(); (vv||window).addEventListener('resize',set); addEventListener('resize',set);
 document.addEventListener('focusin',e=>{ const t=e.target; if(t&&t.matches&&t.matches('.modal.open input,.modal.open textarea,.modal.open select')) setTimeout(()=>{ try{ t.scrollIntoView({block:'nearest'}); }catch(err){} },250); }); })();
