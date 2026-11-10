// ---- HOME Music (owner decision D-81): the owner-selected song in every HOME, the whole song and then again, On by default ----
// One choice for this device, never part of a collection, a loadout or a backup: the website keeps it in this browser (localStorage),
// Classic Cart Studio for Windows on this computer (its data folder; the game window's HOME uses the same choice). Absent: On; a saved
// value that cannot be read, or a store that cannot be read at all: Off (nothing plays unexpectedly) until the person chooses again.
// The page plays it only while Preview on TV is open, in front and showing the HOME. It pauses while a game window runs (that window
// plays its own HOME's music and may change the choice: the page reads it again before it plays), while the phone shows the rotate
// screen, and when the preview closes, and goes on from where it was (one audio element for the whole visit). The TV Preview's pictures
// are drawn without sound. A browser may refuse sound before the person interacts: then "Tap to play HOME music" shows, the saved
// choice stays On, and the next ordinary press starts it; the HOME Music controls themselves always do what they say.
const HM_SRC='brand/home/music/ccs-home-theme-7d07857f25e3.m4a', HM_KEY='ccs-home-music-v1';
const HM={pref:null, source:'', el:null, blocked:false, error:'', saveError:'', focus:true, timer:0, coreSeq:0, wasWanted:false, gen:0, stale:false, syncP:null, session:false};
function hmApp(){ return typeof window!=='undefined'&&!!window.__CCS_DESKTOP; }   // Classic Cart Studio for Windows (its host injects this before the page runs)
function hmRead(v){ if(v===null) return {pref:'on',source:'default'}; if(v==='on'||v==='off') return {pref:v,source:'saved'}; return {pref:'off',source:'unreadable'}; }
async function hmLoad(){   // the saved choice: before any HOME opens, so a remembered Off never makes a sound (the HOME's badge shows it once known)
 try{ await hmLoad0(); }finally{ if(typeof TV!=='undefined'&&TV.open&&typeof tvDraw==='function') tvDraw(true); } }
async function hmLoad0(){
 if(hmApp()){ for(let i=0;i<120&&!(typeof COMPANION!=='undefined'&&COMPANION.ok);i++) await new Promise(r=>setTimeout(r,250));   // the app's backend answers first
  try{ const r=await capi('/api/play/home-music',{timeout:8000}); if(r&&r.ok&&(r.music==='on'||r.music==='off')){ HM.pref=r.music; HM.source=r.source||'saved'; HM.avail=r.available!==false; hmUi(); return; } }catch(e){} HM.pref='off'; HM.source='unreadable'; hmUi(); return; }
 let r; try{ r=hmRead(localStorage.getItem(HM_KEY)); }catch(e){ r={pref:'off',source:'unreadable'}; }   // a store that cannot be read says nothing about the choice
 HM.pref=r.pref; HM.source=r.source; hmUi(); }
// the person's choice: it wins over a reading still on its way; if it cannot be kept, it lasts for this visit and says so
async function hmSave(v){ HM.gen++; HM.stale=false; HM.pref=v; HM.source='saved'; HM.saveError=''; let ok=true;
 if(hmApp()){ let r; try{ r=await capi('/api/play/home-music',{method:'POST',body:{music:v},timeout:8000}); }catch(e){ r={ok:false}; } if(!r||!r.ok){ ok=false; HM.saveError=(r&&r.error)||'The choice could not be saved on this computer, so it lasts until Classic Cart Studio closes.'; } }
 else { try{ localStorage.setItem(HM_KEY,v); }catch(e){ ok=false; HM.saveError='This browser could not remember the choice, so it lasts until you leave the page.'; } }
 if(HM.pref===v){ HM.session=!ok; HM.source=ok?'saved':'session'; } hmUi(); }
// the saved choice read again where another owner may have changed it (Windows app: the game window's own HOME Settings). The page stays
// silent until the answer is in (the old choice never sounds meanwhile); a choice made here meanwhile wins; an answer that cannot be read is Off
function hmSync(){ if(HM.syncP) return HM.syncP; const gen=HM.gen;
 HM.syncP=(async()=>{ let r={pref:'off',source:'unreadable'}, avail=HM.avail;
  try{ const x=await capi('/api/play/home-music',{timeout:8000}); if(x&&x.ok&&(x.music==='on'||x.music==='off')){ r={pref:x.music,source:x.source||'saved'}; avail=x.available!==false; } }catch(e){}
  if(gen===HM.gen){ HM.stale=false; if(!HM.session){ const was=HM.pref; HM.pref=r.pref; HM.source=r.source; HM.avail=avail;
   if(r.pref!==was){ HM.blocked=false; hmCore(r.pref); if(typeof TV!=='undefined'&&TV.open&&typeof tvDraw==='function') tvDraw(true); } } }   // the HOME's own Settings and the native preview show it too
  HM.syncP=null; hmApply(); })();
 return HM.syncP; }
function hmEl(){ if(HM.el) return HM.el; const a=document.createElement('audio'); a.id='hmAudio'; a.loop=true; a.preload='none'; a.setAttribute('aria-hidden','true');
 a.addEventListener('playing',()=>{ HM.blocked=false; HM.error=''; hmUi(); }); a.addEventListener('pause',()=>hmUi());
 a.addEventListener('error',()=>{ HM.error='The HOME music could not be loaded.'; hmUi(); });
 document.body.appendChild(a); HM.el=a; return a; }
function hmGameWindow(){ return typeof PLAY!=='undefined'&&!!PLAY.running; }
function hmEmbedded(){ return typeof TP!=='undefined'&&(TP.opening||TP.app&&TP.on); }   // the Windows app's Preview is the game player: it plays the music (from the moment it opens)
function hmWebGame(){ return typeof TP!=='undefined'&&TP.webGame; }         // a game plays in the website's Preview: its own sound only
function hmWanted(){ return !hmEmbedded()&&!hmWebGame()&&HM.pref==='on'&&!HM.error&&HM.avail!==false&&!HM.stale&&!HM.syncP&&typeof TV!=='undefined'&&TV.open&&!document.hidden&&HM.focus&&!hmGameWindow()&&!(typeof TVL!=='undefined'&&TVL.blocked); }
// gesture: called inside the person's own click or key press (opening the preview, the toggle, the tap control): play() starts there.
// While a browser waits for a tap, only those start it (focus, visibility and the timer never try again: no sound before a click's own action)
function hmApply(){
 if(hmApp()&&hmGameWindow()) HM.stale=true;   // the game window plays its HOME's music now and may change the choice
 else if(HM.stale&&!HM.syncP) hmSync();
 const want=hmWanted();
 if(want){ const a=hmEl(); if(!a.getAttribute('src')){ a.src=HM_SRC; a.preload='auto'; }
  if(a.paused&&!HM.blocked){ let p; try{ p=a.play(); }catch(e){ p=null; } if(p&&p.then) p.then(()=>{ HM.blocked=false; hmUi(); }, e=>{ if(e&&e.name==='NotAllowedError') HM.blocked=true; else if(e&&e.name!=='AbortError') HM.error='The HOME music could not be played.'; hmUi(); }); } }
 else if(HM.el&&!HM.el.paused) HM.el.pause();
 if(want!==HM.wasWanted){ HM.wasWanted=want; if(typeof hoLog==='function') hoLog('home music '+(want?'play':'pause')); }
 hmUi(); }
// the toggle and the HOME's own Settings: always exactly what the person chose (Off is silent at once, even before a first tap)
function hmToggle(){ const v=HM.pref==='on'?'off':'on'; HM.error=''; if(v==='off') HM.blocked=false; hmSave(v); hmApply(); hmCore(v); if(typeof TV!=='undefined'&&TV.open&&typeof tvDraw==='function') tvDraw(true); }   // the HOME's own Settings shows the new value
// the toolbar's music button (F12): while the browser waits for a tap it is "Play Music" and plays (the choice stays On; no Off-then-On
// is needed); otherwise it turns the music off or on
function hmButton(){ if(HM.pref==='on'&&HM.blocked&&!hmEmbedded()){ HM.blocked=false; HM.error=''; hmApply(); return; } if(HM.pref==='on'&&HM.error&&!hmEmbedded()){ HM.error=''; hmApply(); return; } hmToggle(); }
// a browser that waited for a tap: the person's next ordinary tap or key press starts the music
function hmUnlock(){ if(!HM.blocked||!hmWanted()) return; HM.blocked=false; hmApply(); }
// after the page's own handlers (bubbling), so a press that changes the choice or closes the preview has done so first. The HOME Music
// controls decide for themselves; More only opens the options where HOME Music is; Enter or Space on a button is that button's click
function hmGesture(e){ if(!HM.blocked) return; const t=e.target&&e.target.closest?e.target:null;
 if(t&&t.closest('#tvMusic,#tvMusicTap,#tvMore')||hmBadgeHit(e)) return;
 if(e.type==='keydown'&&(e.key==='Escape'||e.key==='Tab'||e.repeat||((e.key==='Enter'||e.key===' ')&&t&&t.closest('button,a[href],input,select,textarea,[role=button]')))) return;
 hmUnlock(); }
// the HOME's own music badge: in the browser HOME (inside its shadow root), or where the native HOME's frame says it is (the app's
// Preview); on a touch screen also the on-screen pad's HOME Music button (data-hmnote)
function hmBadgeHit(e){ return (e.composedPath?e.composedPath():[e.target]).some(x=>x&&x.classList&&(x.classList.contains('hmmu')||x.hasAttribute('data-hmnote'))); }
function hmNativeHit(e){ const im=e.target; if(!(im&&im.id==='tvNative'&&typeof TVN!=='undefined'&&TVN.on&&TVN.badge)) return false; const r=im.getBoundingClientRect(); if(!r.width) return false;
 const x=(e.clientX-r.left)/r.width*1280, y=(e.clientY-r.top)/r.height*720, b=TVN.badge; return x>=b[0]-4&&x<b[2]+4&&y>=b[1]-4&&y<b[3]+4; }
function hmCore(v){ if(typeof TVN!=='undefined'&&TVN.on&&typeof capi==='function') capi('/api/preview/music',{method:'POST',body:{music:v}}).catch(()=>{}); if(hmEmbedded()&&typeof tpCmd==='function') tpCmd('music '+v); }   // the native preview's own Settings shows the same choice
// what is happening now (status) and what the button does (action): the remembered choice, and whether the music is really heard
function hmState(){ const a=HM.el, playing=!!(a&&!a.paused&&!a.ended);
 if(HM.pref===null) return {status:'Music: checking…', action:'Mute Music', note:'', on:false, playing:false};
 if(HM.pref==='off') return {status:'Music off', action:'Play Music', note:HM.source==='unreadable'?'The saved choice could not be read; choose Play Music to hear it.':'', on:false, playing:false};
 if(HM.avail===false) return {status:'Music not available', action:'Mute Music', note:'Not part of this installation.', on:true, playing:false};
 if(hmEmbedded()){ const m=String((typeof TP!=='undefined'&&TP.music)||''), ev=m.split(' ')[0];
  if(ev==='music_unavailable') return {status:'Music not available', action:'Mute Music', note:'The HOME Music file could not be read.', on:true, playing:false};
  if(ev==='music_no_output') return {status:'Music cannot play', action:'Mute Music', note:'No sound output could be opened on this computer. Check that a speaker or headphones are connected and turned on.', on:true, playing:false};
  if(typeof TP!=='undefined'&&TP.game) return {status:'Music paused while a game plays', action:'Mute Music', note:'', on:true, playing:false};
  if(typeof TP!=='undefined'&&TP.popped) return {status:ev==='music_play'||ev==='music_loop'?'Music playing in the separate window':'Music paused', action:'Mute Music', note:'', on:true, playing:ev==='music_play'||ev==='music_loop'};
  if(/ away$/.test(m)) return {status:'Music paused (Classic Cart Studio is not in front)', action:'Mute Music', note:'', on:true, playing:false};
  if(ev==='music_play'||ev==='music_loop'||ev==='music_pref_changed') return {status:'Music playing', action:'Mute Music', note:'', on:true, playing:true};
  return {status:'Music starting…', action:'Mute Music', note:'', on:true, playing:false}; }
 if(HM.error) return {status:'Music could not play', action:'Try Again', note:HM.error, on:true, playing:false};
 if(HM.blocked) return {status:'Music waiting for a tap', action:'Play Music', note:'This browser plays sound only after you tap or click.', on:true, playing:false};
 if(hmWebGame()||hmGameWindow()) return {status:'Music paused while a game plays', action:'Mute Music', note:'', on:true, playing:false};
 if(!HM.focus||document.hidden) return {status:'Music paused (this window is not in front)', action:'Mute Music', note:'', on:true, playing:false};
 if(playing) return {status:'Music playing', action:'Mute Music', note:'', on:true, playing:true};
 return {status:HM.stale||HM.syncP?'Music: checking…':'Music starting…', action:'Mute Music', note:'', on:true, playing:false}; }
const HM_ICON_ON='<svg viewBox="3 3 24 24" width="18" height="18" aria-hidden="true"><g fill="currentColor"><ellipse cx="10.4" cy="20.6" rx="3.6" ry="2.7" transform="rotate(-20 10.4 20.6)"/><ellipse cx="19.4" cy="18.6" rx="3.6" ry="2.7" transform="rotate(-20 19.4 18.6)"/><rect x="13" y="8.6" width="1.8" height="11.6"/><rect x="22" y="6.6" width="1.8" height="11.6"/><polygon points="13,8.6 23.8,6.6 23.8,9.8 13,11.8"/></g></svg>';
const HM_ICON_OFF='<svg viewBox="3 3 24 24" width="18" height="18" aria-hidden="true"><g fill="currentColor" opacity=".5"><ellipse cx="10.4" cy="20.6" rx="3.6" ry="2.7" transform="rotate(-20 10.4 20.6)"/><ellipse cx="19.4" cy="18.6" rx="3.6" ry="2.7" transform="rotate(-20 19.4 18.6)"/><rect x="13" y="8.6" width="1.8" height="11.6"/><rect x="22" y="6.6" width="1.8" height="11.6"/><polygon points="13,8.6 23.8,6.6 23.8,9.8 13,11.8"/></g><path d="M5.5 5.5L24.5 24.5" stroke="currentColor" stroke-width="2.4"/></svg>';   // the HOME badge's music note (home-themes.js), here in the text color
function hmUi(){ const b=$('tvMusic'); const s=hmState();
 if(b){ b.innerHTML=(s.playing?HM_ICON_ON:HM_ICON_OFF)+`<span>${esc(s.action)}</span>`; b.removeAttribute('aria-pressed');
  b.setAttribute('aria-label',s.action+'. HOME Music: '+s.status+(s.note?'. '+s.note:'')+(HM.saveError?'. '+HM.saveError:'')); b.title=s.status+(s.note?' · '+s.note:''); b.classList.toggle('active',s.playing); b.dataset.state=s.on?(s.playing?'playing':'on'):'off'; }
 const st=$('tvStage'); if(st){ let c=$('tvMusicTap'); const show=TV.open&&HM.pref==='on'&&HM.blocked;
  if(show&&!c){ c=document.createElement('button'); c.type='button'; c.id='tvMusicTap'; c.className='tvmusictap'; c.innerHTML=HM_ICON_OFF+'<span>Tap to play HOME music</span>'; c.onclick=e=>{ e.stopPropagation(); HM.blocked=false; hmApply(); $('tvStage').focus({preventScroll:true}); }; st.appendChild(c); }
  if(c){ c.hidden=!show; if(show){ hmTapPlace(); requestAnimationFrame(hmTapPlace); } } }
 const p=$('tvPadMusic'); if(p){ const on=HM.pref==='on', t=on?'Mute HOME Music ('+s.status+')':'Play HOME Music ('+s.status+')'; p.innerHTML=(on?HM_ICON_ON:HM_ICON_OFF).replace('width="18" height="18"','width="22" height="22"');
  p.setAttribute('aria-pressed',on?'true':'false'); p.setAttribute('aria-label',t); p.title=t; p.classList.toggle('off',!on); }
 const l=$('tvMusicNote'); if(l) l.textContent=s.status+(s.note?' · '+s.note:'')+(HM.saveError?' · '+HM.saveError:''); }
// "Tap to play HOME music" sits at the bottom of the stage, centered, just above the HOME's button hints (every theme's hints start
// below 650 of its 720 rows): clear of the HOME's brand and navigation at its top, of Close and More, and of the on-screen pad, which on
// a phone lies over the stage's edges (the control moves sideways off any of them)
function hmTapPlace(){ const c=$('tvMusicTap'), st=$('tvStage'); if(!c||c.hidden||!st) return; const s=st.getBoundingClientRect(); if(!s.width) return;
 c.style.transform='none'; c.style.bottom='auto'; const w=c.offsetWidth, h=c.offsetHeight, y=Math.max(4, s.height*642/720-h);
 const vv=window.visualViewport, vl=vv?vv.offsetLeft:0, vr=vv?vv.offsetLeft+vv.width:innerWidth, lo=Math.max(4,vl-s.left+4), hi=Math.min(s.width-4,vr-s.left-4)-w;
 const obs=[...document.querySelectorAll('#tvPad .pb,#tvClose,#tvMore')].map(e=>e.getBoundingClientRect()).filter(r=>r.width&&r.height);
 const clear=x=>obs.every(r=>r.right<=s.left+x-4||r.left>=s.left+x+w+4||r.bottom<=s.top+y-4||r.top>=s.top+y+h+4), mid=(s.width-w)/2;
 let best=Math.max(lo,Math.min(hi,mid)); for(let d=0; d<=s.width; d+=4){ if(mid-d>=lo&&mid-d<=hi&&clear(mid-d)){ best=mid-d; break; } if(mid+d>=lo&&mid+d<=hi&&clear(mid+d)){ best=mid+d; break; } }
 c.style.left=Math.round(best)+'px'; c.style.top=Math.round(y)+'px'; }
(function(){
 const _open=openTv, _close=closeTv, _eff=tvApplyEffects, _started=typeof playStarted==='function'?playStarted:null;
 openTv=function(opts){ if(hmApp()&&HM.stale&&!hmGameWindow()) hmSync();   // before the preview starts: the native preview waits for this answer
  HM.blocked=false;   // opening the preview is the person's own action: the browser is asked again
  _open(opts); const m=$('tv'); if(m&&!$('tvMusic')){ const tools=m.querySelector('.tvtools');
   if(tools){ const b=document.createElement('button'); b.type='button'; b.className='btn sm tvmusic'; b.id='tvMusic'; b.onclick=()=>{ hmButton(); };
    const n=document.createElement('span'); n.id='tvMusicNote'; n.className='small tvmusicnote'; n.setAttribute('role','status'); n.setAttribute('aria-live','polite'); tools.appendChild(b); tools.appendChild(n); } }
  // a touch screen: the on-screen pad has HOME Music too, a full-size button (the HOME's own note is small where the HOME is small); on a
  // phone it sits at the bottom left of the screen, mirroring SELECT and START (tv-landscape.js), never over another control. Its whole
  // 44 x 44 square answers (the round button's corners too), not only the circle
  const pad=$('tvPad'); if(pad&&!$('tvPadMusic')){ const g=document.createElement('div'); g.className='muh'; g.setAttribute('data-hmnote',''); g.innerHTML='<button type="button" class="pb mu" id="tvPadMusic" data-hmnote aria-pressed="false" aria-label="HOME Music"></button>'; pad.insertBefore(g,pad.querySelector('.ssh')); }
  hmApply(); clearInterval(HM.timer); HM.timer=setInterval(()=>{ if(!TV.open){ clearInterval(HM.timer); HM.timer=0; return; }
   if(hmWanted()!==HM.wasWanted||(HM.wasWanted&&HM.el&&HM.el.paused&&!HM.blocked)||(hmApp()&&(hmGameWindow()?!HM.stale:HM.stale&&!HM.syncP))) hmApply(); },500); };
 closeTv=function(){ _close(); clearInterval(HM.timer); HM.timer=0; hmApply(); };
 if(_started) playStarted=function(){ if(hmApp()) HM.stale=true; return _started.apply(this,arguments); };   // a game window opens: it may change the choice
 // the HOME's own Settings (the browser HOME) toggles the same choice
 tvApplyEffects=function(fx){ const rest=[]; for(const f of fx||[]){ if(f.type==='music') hmToggle(); else rest.push(f); } return _eff(rest); };
 document.addEventListener('visibilitychange',()=>hmApply()); document.addEventListener('click',e=>{ if(!(TV.open&&(hmBadgeHit(e)||hmNativeHit(e)))) return; e.preventDefault(); e.stopPropagation(); hmToggle(); const st=$('tvStage'); if(st) st.focus({preventScroll:true}); },true);
 document.addEventListener('click',hmGesture); document.addEventListener('keydown',hmGesture);
 window.addEventListener('blur',()=>{ HM.focus=false; hmApply(); }); window.addEventListener('focus',()=>{ HM.focus=true; hmApply(); });
 const place=()=>requestAnimationFrame(hmTapPlace); window.addEventListener('resize',place); if(window.visualViewport) visualViewport.addEventListener('resize',place);   // after the pad's own layout
 // the website: another page of this site changed the choice (the browser tells every other page)
 window.addEventListener('storage',e=>{ if(hmApp()||e.key!==HM_KEY) return; const r=hmRead(e.newValue); HM.gen++; HM.session=false; HM.saveError='';
  const was=HM.pref; HM.pref=r.pref; HM.source=r.source; if(r.pref==='off') HM.blocked=false; hmApply(); if(r.pref!==was&&typeof TV!=='undefined'&&TV.open&&typeof tvDraw==='function') tvDraw(true); });
 // the native preview (Windows app): HOME Music changed in its own Settings reaches this page with the frame (seq counts those changes)
 window.hmFromCore=function(v,seq){ if(!(seq>HM.coreSeq)) return; HM.coreSeq=seq; if((v==='on'||v==='off')&&v!==HM.pref){ hmSave(v); hmApply(); } };
 if(typeof COMPANION!=='undefined'&&typeof addEventListener==='function') setTimeout(hmLoad,0);
})();
