// ---- Mobile HOME is landscape only (D-74), and the HOME uses the largest usable area ----
// On a phone or tablet (touch is the primary input and it cannot hover: touchscreen laptops and narrow desktop windows are not mobile),
// opening Preview on TV asks, from the opening tap, for full screen and a landscape lock. Where the browser refuses or cannot lock (an
// ordinary iPhone Safari page cannot), a portrait phone gets a blocking "Rotate your phone to landscape" screen with a way back: no
// portrait HOME is shown or driven behind it, held buttons are released, and the HOME is exactly where it was when landscape returns.
// In landscape the HOME stage is the largest 16:9 area of the visible page: the visual viewport's whole rectangle (its size and its
// offsets within the layout, as navigation.js measures it; while zoomed in, the last unzoomed one) less the safe areas. Close, the options
// button, the touch controls and the rotate screen are placed inside that same rectangle (the controls beside the stage when there is
// room, over its edges otherwise). On the computer, Full Screen gives the same largest-HOME layout.
// Full screen and the lock belong to the opening of the Preview that asked for them (TVL.gen, one per opening and closing): a request that
// settles after that Preview closed, or after a newer one opened, never becomes the open Preview's; what it obtained is given back at
// once unless the open Preview asked for the same itself (then that Preview's own request decides, and closing gives it back). Closing
// gives back only what the Preview's own requests obtained, never full screen or a lock that was there before.
const TVL={mobile:false,max:false,blocked:false,fsErr:null,lockErr:null,listening:false,
 gen:0, rid:0, timer:0, fsWant:0, fsBy:0, fsOwned:false, lockWant:0, lockBy:0, lockOwned:false, vis:null};
function tvlMobile(){ const coarse=matchMedia('(pointer:coarse)').matches, hover=matchMedia('(any-hover:hover)').matches; return coarse&&!hover&&Math.min(screen.width,screen.height)<=1100; }
function tvlPortrait(){ const t=screen.orientation&&screen.orientation.type; if(t) return t.indexOf('portrait')===0; if(typeof window.orientation==='number') return Math.abs(window.orientation)!==90; return screen.height>screen.width; }
function tvlInsets(){ let p=$('tvlSafe'); if(!p){ p=document.createElement('div'); p.id='tvlSafe'; p.style.cssText='position:fixed;left:0;top:0;width:0;height:0;visibility:hidden;pointer-events:none;padding:env(safe-area-inset-top) env(safe-area-inset-right) env(safe-area-inset-bottom) env(safe-area-inset-left)'; document.body.appendChild(p); }
 const c=getComputedStyle(p); return {t:parseFloat(c.paddingTop)||0,r:parseFloat(c.paddingRight)||0,b:parseFloat(c.paddingBottom)||0,l:parseFloat(c.paddingLeft)||0}; }
// the visible page in layout coordinates; while pinch-zoomed in, the last unzoomed rectangle stands (zooming enlarges the HOME instead of moving it)
function tvlVisible(){ const vv=window.visualViewport; if(vv&&vv.scale>1.01&&TVL.vis) return TVL.vis;
 const r=vv?{x:vv.offsetLeft||0, y:vv.offsetTop||0, w:vv.width, h:vv.height}:{x:0, y:0, w:innerWidth, h:innerHeight}; TVL.vis=r; return r; }
const TVL_VARS=['--tvl-vx','--tvl-vy','--tvl-vw','--tvl-vh','--tvl-w','--tvl-h','--tvl-x','--tvl-y','--tvl-cx','--tvl-cy','--tvl-mx','--tvl-my','--tvl-dx','--tvl-dy','--tvl-abx','--tvl-aby','--tvl-ssx','--tvl-ssy','--tvl-tx','--tvl-ty','--tvl-tw','--tvl-th','--tvl-mux','--tvl-muy'];
// the largest 16:9 stage in the usable rectangle, every control inside it: Close top right, the options button top left (beside Close when
// the page is too short to keep it above the D-pad), the D-pad at the left and A/B at the right centered on the page's height (never
// above the top row or below the page), SELECT/START at the bottom right, HOME Music's button (a touch screen) at the bottom left, or
// beside the D-pad where the D-pad reaches the bottom row; controls beside the stage when each side has 150 px, over its edges otherwise
// (44 px touch targets: the D-pad is 3 x 44 plus gaps)
function tvlLayout(){ const m=$('tv'); if(!m) return; const had=HM_BAND; HM_BAND=null; if(!TV.open){ TVL_VARS.forEach(k=>m.style.removeProperty(k)); m.classList.remove('tvl-over','tvl-short'); return; }
 const v=tvlVisible(), I=tvlInsets(), px=(k,n)=>m.style.setProperty(k, Math.round(n)+'px');
 px('--tvl-vx',v.x); px('--tvl-vy',v.y); px('--tvl-vw',v.w); px('--tvl-vh',v.h);   // the rotate screen covers exactly the visible page
 if(!TVL.max){ TVL_VARS.slice(4).forEach(k=>m.style.removeProperty(k)); m.classList.remove('tvl-over','tvl-short'); if(had){ try{ tvDraw(true); }catch(e){} } return; }
 const ux=v.x+I.l, uy=v.y+I.t, uw=Math.max(0,v.w-I.l-I.r), uh=Math.max(0,v.h-I.t-I.b);
 const w=Math.min(uw,uh*16/9), h=w*9/16, side=(uw-w)/2, over=side<150, g=over?8:Math.max(8,(side-140)/2);
 px('--tvl-w',Math.floor(w)); px('--tvl-h',Math.floor(h)); px('--tvl-x',Math.floor(ux+(uw-w)/2)); px('--tvl-y',Math.floor(uy+(uh-h)/2));
 const box=sel=>{ const e=m.querySelector(sel), r=e&&e.getBoundingClientRect(); return r&&r.width?{w:r.width,h:r.height}:{w:0,h:0}; };
 const L=ux+g, R=ux+uw-g, top=uy+6, row=top+44+6, short=uh<56+140+8, inY=(y,hh,lo)=>Math.max(lo, Math.min(y, uy+uh-4-hh));
 px('--tvl-cx',R-44); px('--tvl-cy',top); px('--tvl-mx',short?R-44-8-44:L); px('--tvl-my',top);
 const dY=inY(uy+uh/2-70, 140, short?uy+4:row); px('--tvl-dx',L); px('--tvl-dy',dY);
 const ab=box('#tvPad .abh'), ss=box('#tvPad .ssh'), ssY=Math.max(row, uy+uh-8-(ss.h||44));
 px('--tvl-ssx',R-ss.w); px('--tvl-ssy',ssY); px('--tvl-abx',R-ab.w); px('--tvl-aby',Math.min(inY(uy+uh/2-(ab.h||54)/2, ab.h||54, row), Math.max(row, ssY-6-(ab.h||54))));
 px('--tvl-mux',ssY>=dY+140+8?L:L+140+8); px('--tvl-muy',ssY);
 px('--tvl-tx',L); px('--tvl-ty',top+50); px('--tvl-tw',Math.max(0,R-L)); px('--tvl-th',Math.max(0,uy+uh-8-(top+50)));
 m.classList.toggle('tvl-over',over); m.classList.toggle('tvl-short',short);
 // the stretch of the HOME's bottom row (its hints: rows 650 to 720 of 720) that the pad's controls leave free, in the HOME's own pixels,
 // with 8 px of room on the screen beside each of them (home-themes.js HM_BAND)
 const s=$('tvStage').getBoundingClientRect(), k=s.width/1280; let a=0, b=1280;
 if(over&&k>0) ['#tvPad .dpad','#tvPad .muh','#tvPad .ssh','#tvPad .abh'].forEach(sel=>{ const e=m.querySelector(sel), r=e&&e.getBoundingClientRect();
  if(!r||!r.width||r.bottom<=s.top+650*k||r.top>=s.bottom) return; const l=(r.left-s.left)/k, rr=(r.right-s.left)/k; if(rr<=0||l>=1280) return;
  if((l+rr)/2<640) a=Math.max(a,rr+8/k); else b=Math.min(b,l-8/k); });
 HM_BAND=a>0||b<1280?[Math.ceil(a),Math.floor(b)]:null;
 try{ tvDraw(true); }catch(e){} }   // the HOME fits itself to the stage when drawn
function tvlGate(){ const m=$('tv'); if(!m) return; let g=$('tvRotate');
 const need=TV.open&&TVL.mobile&&tvlPortrait();
 if(need&&!g){ g=document.createElement('div'); g.id='tvRotate'; g.className='tvrotate'; g.setAttribute('role','alertdialog'); g.setAttribute('aria-modal','true'); g.setAttribute('aria-labelledby','tvRotT'); g.setAttribute('aria-describedby','tvRotD');
  g.innerHTML='<div class="tvrico" aria-hidden="true"><span></span></div><h3 id="tvRotT">Rotate your phone to landscape</h3><p id="tvRotD">The HOME is shown in landscape only. Turn your phone sideways; everything stays where you left it.</p><button type="button" class="btn" id="tvRotBack">Back to Collection</button>';
  m.appendChild(g); $('tvRotBack').onclick=()=>closeTv(); }
 const game=typeof TP!=='undefined'&&TP.webGame;
 if(need&&g){ const d=$('tvRotD'), b=$('tvRotBack'); if(d) d.textContent=game?'The game is paused in its menu. Turn your phone sideways to go on; nothing is lost.':'The HOME is shown in landscape only. Turn your phone sideways; everything stays where you left it.'; if(b) b.hidden=!!game; }   // a game: its own menu is the way out (Close Game)
 if(need&&!TVL.blocked){ TVL.blocked=true; try{ tvReleaseAll(); }catch(e){} if(game&&typeof CCSPLAY!=='undefined') CCSPLAY.hold(true); m.classList.add('tvl-blocked'); g.hidden=false; const b=$('tvRotBack'); if(b&&!b.hidden) b.focus({preventScroll:true}); }
 else if(!need&&TVL.blocked){ TVL.blocked=false; if(typeof CCSPLAY!=='undefined') CCSPLAY.hold(false); m.classList.remove('tvl-blocked'); if(g) g.hidden=true; const st=$('tvStage'); if(st&&TV.open&&!game) st.focus({preventScroll:true}); }
 else if(!need&&g) g.hidden=true; }
function tvlSync(){ tvlLayout(); tvlGate(); }
function tvlListen(on){ if(on===TVL.listening) return; TVL.listening=on; const f=on?'addEventListener':'removeEventListener';
 window[f]('resize',tvlSync); window[f]('orientationchange',tvlSync); if(window.visualViewport){ visualViewport[f]('resize',tvlSync); visualViewport[f]('scroll',tvlSync); }
 if(screen.orientation&&screen.orientation.addEventListener) screen.orientation[f]('change',tvlSync); document[f]('fullscreenchange',tvlFsChange); document[f]('visibilitychange',tvlHidden); }
function tvlHidden(){ if(document.hidden&&TV.open){ try{ tvReleaseAll(); }catch(e){} } }   // backgrounded: no button stays held
function tvlFsChange(){ if(!document.fullscreenElement){ TVL.fsBy=0; TVL.fsOwned=false; if(!TVL.mobile&&TVL.max){ TVL.fsWant=0; TVL.max=false; const m=$('tv'); if(m) m.classList.remove('tvmax'); tvlFsButton(); } } tvlSync(); }
function tvlFsButton(){ const b=$('tvFull'); if(!b) return; const on=TVL.max&&!TVL.mobile; b.textContent=on?'Exit Full Screen':'Full Screen'; b.setAttribute('aria-pressed',on?'true':'false'); b.hidden=TVL.mobile; }
// ---- full screen and the landscape lock, each request with its own id (TVL.fsWant / lockWant: the one the open Preview wants) ----
function tvlFsAsk(){ const el=document.documentElement; if(!(document.fullscreenEnabled&&!document.fullscreenElement&&el.requestFullscreen)) return null;   // already full screen (not ours) or not offered
 const id=++TVL.rid; TVL.fsWant=id; let p;
 try{ p=el.requestFullscreen({navigationUI:'hide'}); }catch(e){ TVL.fsErr=e&&e.name||String(e); TVL.fsWant=0; return null; }
 return Promise.resolve(p).then(()=>{ tvlFsGot(id); return true; }, e=>{ if(id===TVL.fsWant){ TVL.fsErr=e&&e.name||String(e); TVL.fsWant=0; tvlSync(); } return false; }); }
function tvlFsGot(id){ TVL.fsBy=TVL.fsBy||id;
 if(id===TVL.fsWant&&TV.open){ TVL.fsBy=id; TVL.fsOwned=true; tvlSync(); return; }
 if(TV.open&&TVL.fsWant) return;   // a late one while the open Preview's own request is pending: that request decides; closing gives it back
 tvlFsExit(); }                    // the Preview that asked is gone or no longer wants it: given back at once
function tvlFsExit(){ try{ if(TVL.fsBy&&document.fullscreenElement&&document.exitFullscreen) document.exitFullscreen().catch(()=>{}); }catch(e){} TVL.fsBy=0; TVL.fsOwned=false; }
function tvlLockAsk(gen){ if(gen!==TVL.gen||!TVL.mobile) return;   // only for the opening that asked, while it is open (closing starts a new generation)
 try{ if(!(screen.orientation&&typeof screen.orientation.lock==='function')){ TVL.lockErr='unsupported'; tvlSync(); return; }
  const id=++TVL.rid; TVL.lockWant=id;
  screen.orientation.lock('landscape').then(()=>tvlLockGot(id), e=>{ if(id===TVL.lockWant){ TVL.lockErr=e&&e.name||String(e); TVL.lockWant=0; tvlSync(); } }); }
 catch(e){ TVL.lockErr=e&&e.name||String(e); TVL.lockWant=0; } tvlSync(); }
function tvlLockGot(id){ TVL.lockBy=TVL.lockBy||id;
 if(id===TVL.lockWant&&TV.open){ TVL.lockBy=id; TVL.lockOwned=true; tvlSync(); return; }
 if(TV.open&&TVL.lockWant) return;
 tvlUnlock(); }
function tvlUnlock(){ try{ if(TVL.lockBy&&screen.orientation&&screen.orientation.unlock) screen.orientation.unlock(); }catch(e){} TVL.lockBy=0; TVL.lockOwned=false; }
// from the opening tap (a user activation): full screen first (Android needs it for a lock), then the landscape lock; refusals are expected.
// A full-screen request that never settles does not hold the lock back (1.5 s), and neither path asks for this opening once it is closed.
function tvlRequest(){ const gen=TVL.gen, p=tvlFsAsk();
 if(p){ let done=false; const once=()=>{ if(done) return; done=true; if(TVL.timer&&gen===TVL.gen){ clearTimeout(TVL.timer); TVL.timer=0; } tvlLockAsk(gen); }; p.then(once, once); TVL.timer=setTimeout(once,1500); }
 else tvlLockAsk(gen); }
function tvlRelease(){ TVL.gen++; if(TVL.timer){ clearTimeout(TVL.timer); TVL.timer=0; } TVL.fsWant=0; TVL.lockWant=0; tvlUnlock(); tvlFsExit(); }
(function(){
 const _open=openTv, _close=closeTv, _press=tvPress, _act=tvAct;
 openTv=function(opts){ TVL.gen++; if(TVL.timer){ clearTimeout(TVL.timer); TVL.timer=0; } TVL.mobile=tvlMobile(); TVL.max=TVL.mobile; TVL.blocked=false; TVL.fsErr=TVL.lockErr=null;
  if(TVL.mobile) tvlRequest();   // first, while the tap still counts as the person's own action
  _open(opts); const m=$('tv'); if(!m) return;
  if(!$('tvFull')){ const tools=$('tvDisplayMenu')||m.querySelector('.tvtools'); if(tools){ const b=document.createElement('button'); b.type='button'; b.className=tools.id==='tvDisplayMenu'?'':'btn sm'; b.id='tvFull'; b.setAttribute('role','menuitem'); b.textContent='Full Screen'; b.title='The HOME as large as this screen allows (Esc leaves full screen)';
    b.onclick=()=>{ if(TVL.max){ TVL.fsWant=0; tvlFsExit(); TVL.max=false; m.classList.remove('tvmax'); tvlFsButton(); tvlSync(); return; }
     TVL.max=true; m.classList.add('tvmax'); tvlFsButton(); const p=tvlFsAsk(); if(p) p.then(()=>tvlSync()); tvlSync(); $('tvStage').focus({preventScroll:true}); };
    tools.appendChild(b); } }
  if(!$('tvMore')){ const hd=m.querySelector('.tvhd'); if(hd){ const b=document.createElement('button'); b.type='button'; b.className='tvmore'; b.id='tvMore'; b.setAttribute('aria-label','Preview options'); b.setAttribute('aria-expanded','false'); b.textContent='⋯';
    b.onclick=()=>{ const on=!m.classList.contains('tvl-tools'); m.classList.toggle('tvl-tools',on); b.setAttribute('aria-expanded',on?'true':'false'); if(!on) $('tvStage').focus({preventScroll:true}); }; hd.appendChild(b); } }
  m.classList.toggle('tvm',TVL.mobile); m.classList.toggle('tvmax',TVL.max); m.classList.remove('tvl-tools'); tvlFsButton(); tvlListen(true); tvlSync(); };
 closeTv=function(){ const m=$('tv'); tvlRelease(); TVL.blocked=false; TVL.max=false; tvlListen(false); if(m){ m.classList.remove('tvm','tvmax','tvl-blocked','tvl-tools','tvl-over','tvl-short'); const g=$('tvRotate'); if(g) g.hidden=true; } _close(); tvlLayout(); };
 tvPress=function(act){ if(TVL.blocked) return; return _press(act); };   // nothing reaches the HOME while the phone is portrait
 tvAct=function(act){ if(TVL.blocked) return; return _act(act); };
})();
