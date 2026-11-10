// ---- Real TV Preview (N3): in Classic Cart Studio for Windows, Preview on TV is drawn by the console's own native HOME renderer ----
// The backend (tools/companion/native_preview.py) builds a TV Preview package from this collection and its HOME settings with the
// same just-in-time packager the console installation uses (effective customized artwork and texts included), runs the native
// core, and returns 1280 x 720 frames. The five console controls and the theme buttons go to the core; the picture is scaled
// into the 16:9 stage without stretching or cropping. The website (no backend) keeps the browser HOME preview.
const TVN={on:false,timer:0,inflight:false,url:null,focus:null,theme:null,staged:null,rebuild:0,starting:false,startGen:-1,error:null,gen:0,shown:false};
function tvNativeCapable(){ return typeof COMPANION!=='undefined'&&COMPANION.ok&&COMPANION.desktop&&(COMPANION.capabilities||[]).includes('native-preview')&&!(typeof tpAppCapable==='function'&&tpAppCapable()); }   // the embedded game player (tv-player.js) is the Preview where it exists (D-104)
// the manifest Preview's native HOME is built from: the install manifest plus the collection's kept entries (imported games this computer
// cannot resolve), so the native Preview shows exactly the games the browser Preview shows (M2-4); never used to install
function tvnManifest(){ const m=buildManifest(); const have=new Set((m.selections||[]).map(x=>x.game_id));
 tvProject(TV.mode).entries.forEach(e=>{ if(e.kept&&!have.has(e.id)) m.selections.push({game_id:e.id,version_id:e.id,title_id:e.title_id||null,title:e.title,display_title:e.title,version_label:e.version,nointro_name:e.version,source:'kept',install_required:true}); }); return m; }
function tvnHome(){ const m=homeModelFromStudio(); const h=(typeof nhHome==='function')?nhHome(true):{theme:m.theme,order:m.order,games:(m.games||[]).map(g=>({id:g.id,title:g.title,version_label:g.version_label,kind:g.kind})),folders:m.folders||[],favorites:m.favorites||{},settings:m.settings||{},sample:!!m.sample};
 h.map_shortcuts=m.map_shortcuts||[null,null]; return h; }
function tvnLayer(){ let im=$('tvNative'); if(!im){ const st=$('tvStage'); im=document.createElement('img'); im.id='tvNative'; im.alt='The HOME'; im.className='tvnative pending'; im.decoding='sync'; st.appendChild(im);
  const sty=document.createElement('style'); sty.textContent='#tvStage .tvnative{position:absolute;inset:0;width:100%;height:100%;object-fit:contain;background:#000;image-rendering:auto;z-index:2}#tvStage .tvnative.pending{visibility:hidden}#tvStage.native #tvHome{visibility:hidden}#tvStage .tvnmsg{position:absolute;left:12px;right:12px;bottom:12px;z-index:3;padding:10px 14px;border-radius:8px;background:rgba(0,0,0,.78);color:#e9e7e1;font-size:13px}'
   +'#tvStage .tvnload{position:absolute;inset:0;z-index:4;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:10px;background:radial-gradient(ellipse at 50% 40%,#1c1a2a 0,#0b0a12 70%);color:#e9e7e1;text-align:center;padding:16px}'
   +'#tvStage .tvnload b{font-family:var(--display,inherit);letter-spacing:1.5px;text-transform:uppercase;font-size:15px}#tvStage .tvnload span{font-size:12.5px;color:#b9b6c8;max-width:34em}'
   +'#tvStage .tvnload i{width:30px;height:30px;border-radius:50%;border:3px solid rgba(255,255,255,.18);border-top-color:#e4000f;animation:tvnspin .9s linear infinite}@keyframes tvnspin{to{transform:rotate(360deg)}}'
   +'@media (prefers-reduced-motion:reduce){#tvStage .tvnload i{animation:none;border-top-color:rgba(255,255,255,.18)}}'; document.head.appendChild(sty); }
 return im; }
// the deliberate loading state while the native HOME prepares: a panel over the stage (role status), shown until the first decoded frame;
// t=null removes it. The browser HOME stays hidden beneath it, so no half-ready picture shows either
function tvnLoading(t, sub){ const st=$('tvStage'); if(!st) return; let m=st.querySelector('.tvnload'); if(!t){ if(m) m.remove(); return; }
 if(!m){ m=document.createElement('div'); m.className='tvnload'; m.setAttribute('role','status'); m.setAttribute('aria-live','polite'); m.innerHTML='<i aria-hidden="true"></i><b></b><span></span>'; st.appendChild(m); }
 m.querySelector('b').textContent=t; m.querySelector('span').textContent=sub||''; }
function tvnMsg(t){ const st=$('tvStage'); let m=st.querySelector('.tvnmsg'); if(!t){ if(m) m.remove(); return; } if(!m){ m=document.createElement('div'); m.className='tvnmsg'; st.appendChild(m); } m.textContent=t; }
async function tvnFetchFrame(){
 if(!TVN.on||TVN.inflight) return; TVN.inflight=true;
 try{ const init=lnaInit({method:'GET',headers:hdrs({}),cache:'no-store'}); const r=await fetch(CO.base+'/api/preview/frame',init);
  if(r.ok){ const gen=TVN.gen; const b=await r.blob(); const u=URL.createObjectURL(b); const pre=new Image(); pre.src=u;
   try{ await pre.decode(); }catch(e){ URL.revokeObjectURL(u); return; }
   if(!TVN.on||gen!==TVN.gen||!(pre.naturalWidth>0)){ URL.revokeObjectURL(u); return; }
   const im=tvnLayer(); im.src=u; if(TVN.url&&TVN.url!==u) URL.revokeObjectURL(TVN.url); TVN.url=u; im.classList.remove('pending'); if(!TVN.shown){ TVN.shown=true; tvnLoading(null); if(typeof CCSM!=='undefined') CCSM.homeReady(); }
   const f=r.headers.get('X-Preview-Focus')||null, th=r.headers.get('X-Preview-Theme')||null, mu=r.headers.get('X-Preview-Music')||'';
   if(mu&&typeof hmFromCore==='function'){ const [mv,ms,mb]=mu.split(';'); const b=(mb||'').split(',').map(Number); TVN.badge=b.length===4&&b.every(n=>Number.isFinite(n))&&b[2]>b[0]?b:null; hmFromCore(mv, +ms||0); }   // HOME Music changed in the HOME's own Settings or its badge; where the badge is
   if(th&&th!==TVN.theme){ TVN.theme=th; document.querySelectorAll('.tvtheme').forEach(x=>x.classList.toggle('active',x.dataset.theme===th)); tvnSub(); }
   if(f!==TVN.focus){ TVN.focus=f; TV.focusId=f; try{ tvSide(f?tvEntries().find(e=>e.id===f)||null:null); }catch(e){} } }
 }catch(e){} finally{ TVN.inflight=false; } }
function tvnLoop(){ clearInterval(TVN.timer); TVN.timer=setInterval(()=>{ if(!TV.open||!TVN.on){ clearInterval(TVN.timer); TVN.timer=0; return; } tvnFetchFrame(); },TV.held?60:200); }
function tvnSub(){ const name=(HOME_THEME_META.find(t=>t.id===TVN.theme)||{}).name||''; $('tvSub').textContent=tvPlanName()+(name?' · '+name:''); tvnFoot(); }
function tvnFoot(){ const f=$('tvFoot'); if(f) f.innerHTML=''; }
async function tvnStart(theme){
 if(TVN.starting&&TVN.startGen===TVN.gen) return; TVN.starting=true; const gen=TVN.gen; TVN.startGen=gen;   // a start from before a Close never blocks a new one
 const st=$('tvStage'); st.classList.add('native'); tvnLayer();
 if(TVN.shown) tvnMsg('Updating the preview…'); else if(TV.appearance==='console') tvnLoading('Starting the HOME…','');
 else tvnLoading('Starting the HOME…','The first start on this computer takes a little longer.');
 if(typeof HM!=='undefined'&&HM.syncP){ try{ await HM.syncP; }catch(e){} }   // HOME Music read again after a game window: start with that answer
 const mu=typeof HM!=='undefined'&&HM.pref?HM.pref:undefined;
 let r; try{ r=await capi('/api/preview/start',{method:'POST',body:{manifest:tvnManifest(),home:tvnHome(),appearance:TV.appearance,focus:TVN.focus||TV.focusId||null,music:mu},timeout:180000}); }catch(e){ r={ok:false,error:String(e)}; }
 if(TVN.startGen===gen) TVN.starting=false;
 if(gen!==TVN.gen||!TV.open){ if(r&&r.ok&&!TVN.on&&!TVN.starting) capi('/api/preview/stop',{method:'POST',body:{}}); return; }
 if(typeof HM!=='undefined') HM.coreSeq=0;   // a new core counts its own Settings changes from zero   // closed meanwhile: no late surface, no renderer left running
 if(!r.ok){ TVN.on=false; TVN.shown=false; st.classList.remove('native'); const im=$('tvNative'); if(im) im.remove(); tvnMsg(null); tvnLoading(null); TVN.error=r.error||'the preview could not start';
  if(!(TV.model&&TV.model.sample)) tvSay('The real preview could not start, so the browser preview is shown: '+TVN.error); return; }
 TVN.on=true; TVN.theme=r.theme; TVN.staged=r.staged; tvnMsg(null); if(typeof HM!=='undefined'&&HM.pref&&HM.pref!==mu&&typeof hmCore==='function') hmCore(HM.pref);   // changed while it started
 if(theme&&theme!==r.theme){ await capi('/api/preview/theme',{method:'POST',body:{theme}}); TVN.theme=theme; }
 tvnSub(); await tvnFetchFrame(); tvnLoop(); $('tvStage').focus({preventScroll:true}); }
// controls: presses and releases go to the native core (it repeats held directions with the console's timing)
(function(){
 const _open=openTv, _close=closeTv, _press=tvPress, _release=tvReleaseAll, _save=tvSaveImage, _draw=tvDraw;
 // the browser HOME still redraws underneath (its clock, collection changes): while the native renderer shows the picture, the
 // labels around it (subtitle, theme buttons, the selected game, the footer) describe the native picture, not the hidden browser one
 tvDraw=function(quiet){ if(!TVN.on) return _draw(quiet); _draw(true);
  document.querySelectorAll('.tvtheme').forEach(x=>x.classList.toggle('active',x.dataset.theme===TVN.theme)); tvnSub();
  try{ tvSide(TVN.focus?tvEntries().find(e=>e.id===TVN.focus)||null:null); }catch(e){} };
 openTv=function(opts){ _open(opts); if(tvNativeCapable()&&!(TV.model&&TV.model.sample)) tvnStart(opts&&opts.theme); };
 closeTv=function(){ TVN.gen++; const was=TVN.on||TVN.starting; TVN.on=false; TVN.shown=false; clearInterval(TVN.timer); TVN.timer=0; clearTimeout(TVN.rebuild);
  if(was) capi('/api/preview/stop',{method:'POST',body:{}}); const st=$('tvStage'); if(st) st.classList.remove('native'); const im=$('tvNative'); if(im){ im.classList.add('pending'); im.removeAttribute('src'); } tvnLoading(null); tvnMsg(null);
  if(TVN.url){ URL.revokeObjectURL(TVN.url); TVN.url=null; } _close(); };
 tvPress=function(act){ if(!TVN.on) return _press(act); if(!TV.open) return; if(TV.held&&TV.held.act===act) return; if(TV.held) tvReleaseAll(); TV.held={act,t0:performance.now()}; tvnLoop();
  capi('/api/preview/input',{method:'POST',body:{button:act,action:'press'}}).then(()=>tvnFetchFrame()); };
 tvReleaseAll=function(){ if(!TVN.on) return _release(); const h=TV.held; TV.held=null; if(h) capi('/api/preview/input',{method:'POST',body:{button:h.act,action:'release'}}).then(()=>{ tvnFetchFrame(); tvnLoop(); }); };
 tvSaveImage=async function(){ if(!TVN.on) return _save(); const im=$('tvNative'); if(!im||!im.src){ toast('No preview picture yet. Try again in a moment.'); return; } const b=await (await fetch(im.src)).blob(); const name=(HOME_THEME_META.find(t=>t.id===TVN.theme)||{}).name||'HOME'; const res=await ccsSaveFile(csSafeName(`${tvPlanName()||'Collection'} - ${name} preview`,'.jpg'), b, 'Preview Picture'); if(res.ok) toast(`Saved "${res.name}" (${im.naturalWidth} × ${im.naturalHeight}, ${TV.appearance==='console'?'as the console draws it':'the computer’s high-resolution HOME'})`); };
 // Preview for: the native renderer starts again in the other appearance (Console: the console's own package at 1280 x 720; Browser HOME:
 // the computer's high-resolution files at 2560 x 1440), keeping the theme and the highlighted game
 tvAppearanceHook=function(ap){ if(!TVN.on&&!TVN.starting) return false; tvnStart(TVN.theme); return true; };
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
