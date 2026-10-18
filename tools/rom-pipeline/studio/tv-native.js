// ---- Real TV Preview (N3): in Classic Cart Studio for Windows, Preview on TV is drawn by the console's own native HOME renderer ----
// The backend (tools/companion/native_preview.py) builds a TV Preview package from this collection and its HOME settings with the
// same just-in-time packager the console installation uses (effective customized artwork and texts included), runs the native
// core, and returns 1280 x 720 frames. The five console controls and the theme buttons go to the core; the picture is scaled
// into the 16:9 stage without stretching or cropping. The website (no backend) keeps the browser HOME preview.
const TVN={on:false,timer:0,inflight:false,url:null,focus:null,theme:null,staged:null,rebuild:0,starting:false,error:null};
function tvNativeCapable(){ return typeof COMPANION!=='undefined'&&COMPANION.ok&&COMPANION.desktop&&(COMPANION.capabilities||[]).includes('native-preview'); }
function tvnHome(){ const m=homeModelFromStudio(); const h=(typeof nhHome==='function')?nhHome():{theme:m.theme,order:m.order,games:(m.games||[]).map(g=>({id:g.id,title:g.title,version_label:g.version_label,kind:g.kind})),folders:m.folders||[],favorites:m.favorites||{},settings:m.settings||{},sample:!!m.sample};
 h.map_shortcuts=m.map_shortcuts||[null,null]; return h; }
function tvnLayer(){ let im=$('tvNative'); if(!im){ const st=$('tvStage'); im=document.createElement('img'); im.id='tvNative'; im.alt='The console HOME, drawn by its native renderer'; im.className='tvnative'; st.appendChild(im);
  const sty=document.createElement('style'); sty.textContent='#tvStage .tvnative{position:absolute;inset:0;width:100%;height:100%;object-fit:contain;background:#000;image-rendering:auto;z-index:2}#tvStage.native #tvHome{visibility:hidden}#tvStage .tvnmsg{position:absolute;left:12px;right:12px;bottom:12px;z-index:3;padding:10px 14px;border-radius:8px;background:rgba(0,0,0,.78);color:#e9e7e1;font-size:13px}'; document.head.appendChild(sty); }
 return im; }
function tvnMsg(t){ const st=$('tvStage'); let m=st.querySelector('.tvnmsg'); if(!t){ if(m) m.remove(); return; } if(!m){ m=document.createElement('div'); m.className='tvnmsg'; st.appendChild(m); } m.textContent=t; }
async function tvnFetchFrame(){
 if(!TVN.on||TVN.inflight) return; TVN.inflight=true;
 try{ const init=lnaInit({method:'GET',headers:hdrs({}),cache:'no-store'}); const r=await fetch(CO.base+'/api/preview/frame',init);
  if(r.ok){ const b=await r.blob(); const u=URL.createObjectURL(b); const im=tvnLayer(); im.onload=()=>{ if(TVN.url&&TVN.url!==u) URL.revokeObjectURL(TVN.url); TVN.url=u; }; im.src=u;
   const f=r.headers.get('X-Preview-Focus')||null, th=r.headers.get('X-Preview-Theme')||null;
   if(th&&th!==TVN.theme){ TVN.theme=th; document.querySelectorAll('.tvtheme').forEach(x=>x.classList.toggle('active',x.dataset.theme===th)); tvnSub(); }
   if(f!==TVN.focus){ TVN.focus=f; TV.focusId=f; try{ tvSide(f?tvEntries().find(e=>e.id===f)||null:null); }catch(e){} } }
 }catch(e){} finally{ TVN.inflight=false; } }
function tvnLoop(){ clearInterval(TVN.timer); TVN.timer=setInterval(()=>{ if(!TV.open||!TVN.on){ clearInterval(TVN.timer); TVN.timer=0; return; } tvnFetchFrame(); },TV.held?60:200); }
function tvnSub(){ const name=(HOME_THEME_META.find(t=>t.id===TVN.theme)||{}).name||''; $('tvSub').textContent='Drawn by the console’s native HOME renderer · '+name+' · on this computer, not a console capture'; tvnFoot(); }
function tvnFoot(){ const f=$('tvFoot'); if(!f) return; const s=TVN.staged;
 f.innerHTML=`<span><b>Real preview.</b> This picture comes from the same native HOME program the console runs, with your games, order, artwork, descriptions and theme. Switch themes here or with START › Change Theme, exactly as on the console.</span><span class="small">Prepared installation package: <b>${s==='current'?'up to date':s==='outdated'?'out of date — prepare it again in Install on NES Classic':'not prepared yet'}</b>.</span>`; }
async function tvnStart(theme){
 if(TVN.starting) return; TVN.starting=true; tvnMsg('Preparing the preview with the console’s renderer…');
 const st=$('tvStage'); st.classList.add('native'); tvnLayer();
 let r; try{ r=await capi('/api/preview/start',{method:'POST',body:{manifest:buildManifest(),home:tvnHome()},timeout:180000}); }catch(e){ r={ok:false,error:String(e)}; }
 TVN.starting=false;
 if(!r.ok){ TVN.on=false; st.classList.remove('native'); const im=$('tvNative'); if(im) im.remove(); tvnMsg(null); TVN.error=r.error||'the preview could not start';
  if(!(TV.model&&TV.model.sample)) tvSay('The native preview is not available: '+TVN.error); return; }
 TVN.on=true; TVN.theme=r.theme; TVN.staged=r.staged; tvnMsg(null);
 if(theme&&theme!==r.theme){ await capi('/api/preview/theme',{method:'POST',body:{theme}}); TVN.theme=theme; }
 tvnSub(); await tvnFetchFrame(); tvnLoop(); $('tvStage').focus({preventScroll:true}); }
// controls: presses and releases go to the native core (it repeats held directions with the console's timing)
(function(){
 const _open=openTv, _close=closeTv, _press=tvPress, _release=tvReleaseAll, _save=tvSaveImage;
 openTv=function(opts){ _open(opts); if(tvNativeCapable()&&!(TV.model&&TV.model.sample)) tvnStart(opts&&opts.theme); };
 closeTv=function(){ if(TVN.on){ TVN.on=false; clearInterval(TVN.timer); TVN.timer=0; capi('/api/preview/stop',{method:'POST',body:{}}); const st=$('tvStage'); if(st) st.classList.remove('native'); } _close(); };
 tvPress=function(act){ if(!TVN.on) return _press(act); if(!TV.open) return; if(TV.held&&TV.held.act===act) return; if(TV.held) tvReleaseAll(); TV.held={act,t0:performance.now()}; tvnLoop();
  capi('/api/preview/input',{method:'POST',body:{button:act,action:'press'}}).then(()=>tvnFetchFrame()); };
 tvReleaseAll=function(){ if(!TVN.on) return _release(); const h=TV.held; TV.held=null; if(h) capi('/api/preview/input',{method:'POST',body:{button:h.act,action:'release'}}).then(()=>{ tvnFetchFrame(); tvnLoop(); }); };
 tvSaveImage=async function(){ if(!TVN.on) return _save(); const im=$('tvNative'); if(!im||!im.src){ toast('No preview picture yet'); return; } const a=document.createElement('a'); a.href=im.src; a.download=(COLL.name||'collection').replace(/[^\w\- ]+/g,'').trim().replace(/\s+/g,'-')+'-'+(TVN.theme||'home')+'-native-preview.jpg'; a.click(); toast('Preview picture saved (1280×720, as the console draws it)'); };
 // the theme buttons: in the native preview they switch the renderer's theme (the console's own choice is never touched)
 document.addEventListener('click',e=>{ const b=e.target.closest&&e.target.closest('.tvtheme'); if(!b||!TVN.on) return; e.stopImmediatePropagation(); e.preventDefault();
  capi('/api/preview/theme',{method:'POST',body:{theme:b.dataset.theme}}).then(r=>{ if(r.ok){ TVN.theme=b.dataset.theme; document.querySelectorAll('.tvtheme').forEach(x=>x.classList.toggle('active',x.dataset.theme===TVN.theme)); tvnSub(); tvnFetchFrame(); } }); $('tvStage').focus({preventScroll:true}); },true);
 // the collection, its artwork or texts change while the preview is open: rebuild the preview package (only changed content is
 // derived again) and reload the renderer, keeping the focused game
 const _r=window.render; window.render=function(){ const out=_r.apply(this,arguments); if(TV.open&&TVN.on){ clearTimeout(TVN.rebuild); TVN.rebuild=setTimeout(()=>{ const d=TVN.useDefault; TVN.useDefault=false; tvnStart(d?null:TVN.theme); },700); } return out; };
 // folders, folder artwork, Adventure Map shortcuts (home-folders.js hfSave) and the Windows-chosen start theme (install.js nhSetTheme)
 // also rebuild the preview; a new start theme is shown at once
 window.addEventListener('ccs-content-changed',e=>{ if(TV.open&&TVN.on){ if(e&&e.detail&&e.detail.theme) TVN.useDefault=true; clearTimeout(TVN.rebuild); TVN.rebuild=setTimeout(()=>{ const d=TVN.useDefault; TVN.useDefault=false; tvnStart(d?null:TVN.theme); },400); } });   // a new start theme: the rebuilt package opens in it (the frame loop keeps TVN.theme at the running theme, so it cannot carry the request)
})();
