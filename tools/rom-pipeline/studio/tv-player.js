// ---- Play in Preview on TV (D-104, F18, F21, F22): games run inside the TV picture; Pop Out moves the whole HOME and game surface ----
// Classic Cart Studio for Windows: the Preview IS the game player (ccs-player.exe in HOME mode, embedded; PlayerBridge.cs, host_win.c).
//   Its pictures arrive in a shared frame buffer and are drawn here; keys, the mouse and the mock pad go to it; it reads controllers itself
//   while the Preview owns input; it plays the game's sound and HOME Music. Pop Out shows the same player's own window (the session, the
//   game in memory and its saves are the same: nothing reloads, no slot is written); closing that window brings it back here.
// The website: the browser HOME stays the HOME; a launch starts the WebAssembly player (ccs-play.js) inside the TV picture, and Pop Out
//   moves the HOME and the running game's picture to a separate window of this same page (the session stays here).
// One loading panel over the TV picture until the requested picture is really shown, with Try Again and Close on a failure (F18).
const TP={gen:0, on:false, app:false, buf:null, hdr:null, seq:0, shown:false, st:-1, game:false, popped:false, own:-1, focusSent:-1, closing:false, closeAfter:false,
  home:null, music:null, webGame:false, surf:null, pop:null, popHome:null, popSurf:null, menuOpen:false, rebuild:0, err:''};
function tpAppCapable(){ return typeof COMPANION!=='undefined'&&COMPANION.ok&&COMPANION.desktop&&(COMPANION.capabilities||[]).includes('play-embedded')&&!!(window.chrome&&chrome.webview); }
function tpStage(){ return $('tvStage'); }
// ---- the loading panel (F18): centered over the TV picture; input to the picture waits; a failure says what happened ----
function tpLoading(title, sub, opts={}){ const st=tpStage(); if(!st) return; let m=st.querySelector('.tpload');
 if(!title){ if(m) m.remove(); st.classList.remove('tpbusy'); return; }
 if(!m){ m=document.createElement('div'); m.className='tpload'; m.setAttribute('role', opts.error?'alert':'status'); m.setAttribute('aria-live','polite'); st.appendChild(m); }
 m.classList.toggle('err', !!opts.error); st.classList.toggle('tpbusy', !opts.error);
 m.innerHTML=`<div class="tpbox">${opts.error?'<i class="tpx" aria-hidden="true">!</i>':'<i class="tpspin" aria-hidden="true"></i>'}<b>${esc(title)}</b>${sub?`<span>${esc(sub)}</span>`:''}${opts.error?'<div class="tpacts"><button type="button" class="btn sm" data-tp="close">Close</button><button type="button" class="btn sm hk" data-tp="retry">Try Again</button></div>':''}</div>`;
 m.querySelectorAll('[data-tp]').forEach(b=>b.onclick=e=>{ e.stopPropagation(); if(b.dataset.tp==='retry'){ tpLoading(null); (TP.app?tpAppStart:()=>{})(); } else closeTv(); });
 if(opts.error){ const r=m.querySelector('[data-tp=retry]'); if(r) r.focus({preventScroll:true}); } }
function tpPlaceholder(on){ const st=tpStage(); if(!st) return; let p=st.querySelector('.tppop');
 if(!on){ if(p) p.remove(); return; }
 if(!p){ p=document.createElement('div'); p.className='tppop'; p.setAttribute('role','status'); st.appendChild(p); }
 p.innerHTML='<div class="tpbox"><b>HOME is open in a separate window</b><span>Play there; closing that window brings it back here.</span><div class="tpacts"><button type="button" class="btn sm" data-tp="here">Bring It Back Here</button><button type="button" class="btn sm hk" data-tp="front">Bring to Front</button></div></div>';
 p.querySelector('[data-tp=front]').onclick=e=>{ e.stopPropagation(); tpFront(); };
 p.querySelector('[data-tp=here]').onclick=e=>{ e.stopPropagation(); tpPopIn(); }; }

// ================= Classic Cart Studio for Windows: the embedded player =================
function tpPost(s){ try{ chrome.webview.postMessage(s); }catch(e){} }
function tpCmd(line){ if(TP.on&&TP.app) tpPost('embed-cmd '+line); }
function tpAppBuffer(gen){ return new Promise(res=>{ let done=false; const t=setTimeout(()=>{ if(!done){ done=true; res(null); } },15000);
  TP.bufWait=(b)=>{ if(done) return; done=true; clearTimeout(t); res(b); }; tpPost('embed-open'); }); }
async function tpAppStart(){
 const gen=++TP.gen; TP.on=true; TP.app=true; TP.seq=0; TP.musicSeq=null; TP.shown=false; TP.st=-1; TP.game=false; TP.popped=false; TP.own=-1; TP.focusSent=-1; TP.closing=false; TP.closeAfter=false; TP.err=''; TP.ended=false;
 const st=tpStage(); st.classList.add('tpapp'); tpCanvases(); tpPlaceholder(false);
 tpLoading('Starting the HOME…','The first start on this computer takes a little longer.');
 const b=await tpAppBuffer(gen); if(gen!==TP.gen||!TV.open) return;
 if(!b){ tpFail('The Preview could not prepare its picture.','Close the Preview and open it again. If it keeps happening, restart Classic Cart Studio.'); return; }
 if(typeof HM!=='undefined'&&HM.syncP){ try{ await HM.syncP; }catch(e){} }
 let r; try{ r=await capi('/api/play/home',{method:'POST',body:{manifest:tvnManifest(),home:tvnHome(),embed:{pipe:b.pipe},select:TV.focusId||null},timeout:180000}); }catch(e){ r={ok:false,error:String(e)}; }
 if(gen!==TP.gen||!TV.open) return;
 if(!r||!r.ok){ tpFail(r&&r.busy?'A game is already open':'The HOME could not start', (r&&r.error)||'Try again.'); tpPost('embed-dispose'); return; }
 tpLoop(); }
function tpFail(title, sub){ TP.err=sub||title; tpLoading(title, sub, {error:true}); tvSay(title+'. '+(sub||'')); }
function tpCanvases(){ const st=tpStage(); if(!$('tpHome')){ const c=document.createElement('canvas'); c.id='tpHome'; c.className='tpcv'; c.width=2560; c.height=1440; c.setAttribute('aria-label','The HOME'); st.appendChild(c);
  const g=document.createElement('canvas'); g.id='tpGame'; g.className='tpcv tpgame'; g.width=256; g.height=240; g.hidden=true; g.setAttribute('aria-label','The game'); st.appendChild(g);
  [c,g].forEach(cv=>tpPointer(cv)); } }
// frames: the header's sequence number says a new picture is in the newest slot; only a new one is drawn
function tpLoop(){ cancelAnimationFrame(TP.raf); const tick=()=>{ if(!TP.on||!TV.open) return; TP.raf=requestAnimationFrame(tick); tpOwn(); if(!TP.hdr) return;
  const seq=TP.hdr[1]; if(seq===TP.seq) return; TP.seq=seq; const s=TP.hdr[2]&1, w=TP.hdr[4+s*4], h=TP.hdr[5+s*4], kind=TP.hdr[6+s*4]; if(!w||!h||w*h*4>TP.slot) return;
  const px=new Uint8ClampedArray(TP.buf, TP.header+s*TP.slot, w*h*4), cv=kind===1?$('tpGame'):$('tpHome'), other=kind===1?$('tpHome'):$('tpGame');
  if(cv.width!==w||cv.height!==h){ cv.width=w; cv.height=h; } cv.getContext('2d').putImageData(new ImageData(px,w,h),0,0); if(cv.hidden){ cv.hidden=false; other.hidden=true; tpFitGame(); }
  if(!TP.shown){ TP.shown=true; tpLoading(null); tvSay('The HOME is ready.'); tpOwn(true); if(typeof CCSM!=='undefined') CCSM.homeReady(); } else if(TP.reloading){ TP.reloading=false; tpLoading(null); }
  if(TP.waitTheme&&TP.themeSeen){ TP.waitTheme=null; tpLoading(null); } };
 TP.raf=requestAnimationFrame(tick); }
function tpFitGame(){ const g=$('tpGame'), st=tpStage(); if(!g||g.hidden||!st) return; const r=st.getBoundingClientRect(); const tv=TP.hdr?TP.hdr[18]:1, int=TP.hdr?TP.hdr[19]:0;
 const gw=256*(tv?8/7:1), gh=240; let s=Math.min(r.width/gw, r.height/gh); if(int&&s>=1) s=Math.floor(s); g.style.width=Math.round(gw*s)+'px'; g.style.height=Math.round(gh*s)+'px'; }
// who owns input (F21): the Preview, in front, not popped out, no toolbar menu open; controllers count only then
function tpOwnNow(){ return TP.on&&TV.open&&!TP.popped&&TP.shown&&!document.hidden&&document.hasFocus()&&!TP.menuOpen&&!tpCovered(); }
function tpCovered(){ return [...document.querySelectorAll('.modal.open,#collm.open,#gsModal:not([hidden]),.dlg.open')].some(x=>x.offsetParent!==null&&!x.contains($('tv'))); }
function tpOwn(force){ if(!TP.app) return; const o=tpOwnNow()?1:0; if(o!==TP.own||force){ TP.own=o; tpCmd('own '+o); } }
// the mouse over the picture: stage pixels through the canvas's real box (object-fit contain, size, zoom, DPI); a movement that did not
// move is never sent (F3); single click = details, double click = play: the HOME decides (home.c)
function tpPointer(cv){ let last=null;
 const at=e=>{ const r=cv.getBoundingClientRect(); const iw=cv.width, ih=cv.height; const s=Math.min(r.width/iw, r.height/ih), w=iw*s, h=ih*s, ox=r.left+(r.width-w)/2, oy=r.top+(r.height-h)/2;
  const x=e.clientX-ox, y=e.clientY-oy; if(x<0||y<0||x>=w||y>=h) return [-1,-1]; return cv.id==='tpGame'?[-1,-1]:[Math.floor(x/w*1280), Math.floor(y/h*720)]; };
 const send=(k,e,d)=>{ if(!TP.shown||!TP.own) return; const [x,y]=at(e); tpCmd(`pt ${k} ${x} ${y} ${d|0}`); };
 cv.addEventListener('pointermove',e=>{ if(e.pointerType!=='mouse') return; if(last&&last[0]===e.screenX&&last[1]===e.screenY) return; last=[e.screenX,e.screenY]; send(0,e); });
 cv.addEventListener('click',e=>{ tpStage().focus({preventScroll:true}); tpOwn(); send(e.detail>=2?2:1,e); });
 cv.addEventListener('contextmenu',e=>{ e.preventDefault(); send(3,e); });
 cv.addEventListener('wheel',e=>{ e.preventDefault(); send(4,e, e.deltaY<0?1:-1); },{passive:false}); }
// keys: Windows key codes (the player's own key settings apply, as in its window). Escape is the player's menu key when the picture has
// focus; with a toolbar menu open it closes that menu first; on the toolbar it closes the Preview
const TP_VK={ArrowUp:38,ArrowDown:40,ArrowLeft:37,ArrowRight:39,Enter:13,Backspace:8,Space:32,Tab:9,Escape:27,ShiftLeft:160,ShiftRight:161,ControlLeft:162,ControlRight:163,Home:36,End:35,PageUp:33,PageDown:34,Insert:45,Delete:46,
 Comma:188,Period:190,Slash:191,Semicolon:186,Quote:222,BracketLeft:219,BracketRight:221,Minus:189,Equal:187,Backslash:220,Backquote:192};
function tpVk(code){ if(TP_VK[code]) return TP_VK[code]; let m=/^Key([A-Z])$/.exec(code); if(m) return m[1].charCodeAt(0); m=/^Digit([0-9])$/.exec(code); if(m) return 48+ +m[1]; m=/^F([0-9]{1,2})$/.exec(code); if(m&&+m[1]>=1&&+m[1]<=12) return 111+ +m[1]; m=/^Numpad([0-9])$/.exec(code); if(m) return 96+ +m[1]; return 0; }
function tpKeyTarget(e){ const t=e.target; return !(t&&t.closest&&(t.closest('.tvtools,.tvside,input,textarea,select,.tvmenu,.tpload,.tppop')||(t.closest('button,a[href]')&&!t.closest('#tvPad')))); }
const TP_DOWN=new Set();
function tpKey(e){ if(!TP.on||!TP.app||!TV.open) return false;
 if(TP.waitTheme&&e.key!=='Escape'&&e.key!=='Tab'){ e.preventDefault(); return true; }   // F18: nothing reaches the HOME while its picture changes
 if(e.key==='Tab') return false; if(!TP.shown) { if(e.key!=='Escape'){ e.preventDefault(); return true; } return false; }   // nothing reaches the HOME while it loads
 if(!tpKeyTarget(e)||TP.popped||TP.menuOpen) return false;
 const vk=tpVk(e.code); if(!vk) return false; e.preventDefault(); e.stopPropagation(); if(e.repeat) return true; TP_DOWN.add(vk); tpCmd('key d '+vk); return true; }
function tpKeyUp(e){ if(!TP.on||!TP.app) return false; const vk=tpVk(e.code); if(!vk||!TP_DOWN.has(vk)) return false; TP_DOWN.delete(vk); tpCmd('key u '+vk); e.preventDefault(); return true; }
function tpReleaseKeys(){ for(const vk of TP_DOWN) tpCmd('key u '+vk); TP_DOWN.clear(); }
// what the player says
function tpAppMsg(line){ const sp=line.indexOf(' '), k=sp<0?line:line.slice(0,sp), rest=sp<0?'':line.slice(sp+1);
 if(k==='state'){ const p=rest.split(' '); TP.st=+p[0]; TP.game=p[1]==='1'; if(!TP.game) TP.played=false; else if(TP.st===2&&!TP.played){ TP.played=true; if(typeof CCSM!=='undefined') CCSM.played(); }   /* a game really playing (state 2), once per game: not its menus or a Pop Out */
 tpToolbar(); if(typeof hmApply==='function') hmApply(); }
 else if(k==='home'){ try{ const h=JSON.parse(rest); TP.home=h; if(TP.waitTheme&&h.theme===TP.waitTheme) TP.themeSeen=true; if(h.theme&&h.theme!==TVN.theme){ TVN.theme=h.theme; } TVN.focus=h.id||null; TV.focusId=h.id||TV.focusId; TVN.badge=Array.isArray(h.music_badge)?h.music_badge:null;
   if(h.music==='on'||h.music==='off') tpMusicFromPlayer(h.music, h.music_seq); tpToolbar(); try{ tvSide(h.id?tvEntries().find(x=>x.id===h.id)||null:null); }catch(e){} }catch(e){} }
 else if(k==='popped'){ TP.popped=rest==='1'; tpPlaceholder(TP.popped); tpOwn(true); tpToolbar(); if(typeof hmUi==='function') hmUi(); tvSay(TP.popped?'HOME is open in a separate window.':'HOME is back in the Preview.'); if(!TP.popped) tpStage().focus({preventScroll:true}); }
 else if(k==='music'){ TP.music=rest; const p=rest.split(' ');
  if((p[0]==='music_choice'||p[0]==='music_pref_changed')&&(p[1]==='on'||p[1]==='off')&&typeof HM!=='undefined'){ HM.gen++; HM.stale=false; HM.pref=p[1]; HM.source=p[2]==='unreadable'?'unreadable':'saved'; HM.blocked=false; }   // the player changed the choice itself (its own Settings, or the saved file changed) and saved it: the page shows it, sends nothing back
  if(typeof hmUi==='function') hmUi(); }
 else if(k==='close-preview'){ TP.closeAfter=true; }
 else if(k==='reloaded'){ TP.reloading=true; }
 else if(k==='reload-failed'){ tpLoading(null); tvSay('The HOME could not be updated: '+rest); }
 else if(k==='mapped'){ TP.mapped=true; }
 else if(k==='map-failed'||k==='gone'){ tpFail('The HOME could not be shown here', 'Close the Preview and open it again.'); }
 else if(k==='bye'||k==='ended'){ if(TP.ended) return; TP.ended=true; tpReleaseKeys(); const wanted=TP.closing||TP.closeAfter; TP.on=false; tpPost('embed-dispose');
  if(wanted){ TP.closing=false; tpReallyClose(); } else if(TV.open){ tpFail('The HOME closed', 'Choose Try Again to open it again.'); } } }
// ================= the website: the WebAssembly player inside the browser HOME's picture =================
function tpWebGame(id){ const g=(typeof byId!=='undefined'&&byId[id])||(window.keptRow&&keptRow(id))||null; const m=TV.model&&TV.model.games.find(x=>x.id===id);
 const e=g&&g.personal; let sha=(e&&e.content_sha256)||'', hdr=(e&&e.detected&&e.detected.header_hex)||'';
 if(!sha&&g&&g.payload_sha256){ const pl=TP.lib&&TP.lib.payloads&&TP.lib.payloads[g.payload_sha256]; sha=(pl&&pl.content_sha256)||g.payload_sha256; hdr=(pl&&pl.header&&pl.header.hex)||''; }
 return {id, title:(m&&m.title)||(g&&g.display_title)||id, version_label:(g&&g.nointro_name)||'', sha, header_hex:hdr}; }
async function tpWebLaunch(id){
 if(TP.webGame) return; if(!CCSPLAY.supported()){ tpNotice('This browser cannot play games here','It lacks WebAssembly or private storage. Use a current Edge, Chrome, Firefox or Safari.'); return; }
 CCSPLAY.unlockAudio();   // the press that launched: browsers start sound only from a person's own action (a touch press does not count: its release does, tv.js, else Tap for game sound)
 const full=typeof TVL!=='undefined'&&TVL.mobile&&!(TP.pop&&!TP.pop.closed);   // a phone: the game takes the whole screen (landscape only)
 if(full&&!document.fullscreenElement&&typeof tvlRequest==='function') tvlRequest();   // asked again from this same tap when full screen was left
 if(!TP.lib&&typeof libFull==='function'){ try{ TP.lib=await libFull(); }catch(e){} }
 const g=tpWebGame(id); if(!g.sha){ tpNotice(g.title+' cannot be played here', 'This entry has no exact game identity.'); return; }
 let have; try{ have=await CCSROMS.has(g.sha); }catch(e){ tpNotice(g.title+' did not start', String(e&&e.message||e)); return; }   /* an unread store is said, never taken for a game not kept here */
 if(!have){ if(typeof CCSM!=='undefined') CCSM.fail('play','missing_rom'); tpNeedRom(g); return; }
 homeRecordLaunch(TV.model,id); homeExtrasSave();
 TP.webGame=true; TP.closeAfter=false; tpStage().classList.add('tpweb'); const host=TP.pop&&!TP.pop.closed?TP.popStage:full?$('tv'):tpStage(); const surf=CCSPLAY.surface(host,{full}); TP.surf=surf; if(typeof hmApply==='function') hmApply();
 tpLoading('Starting '+g.title+'…',''); tvSay(g.title+' is starting.');
 let res=null; const done=CCSPLAY.start(g, surf).then(r=>{ res=r; }, e=>{ res={error:String(e&&e.message||e), code:e&&e.code}; }); if(typeof CCSM!=='undefined') CCSM.watchWebPlay();
 const wait=setInterval(()=>{ if(CCSPLAY.view()>=0){ clearInterval(wait); tpLoading(null); } },50);
 await done; clearInterval(wait); tpLoading(null); try{ surf.remove(); }catch(e){} TP.surf=null; TP.webGame=false; if(typeof PWA!=='undefined'&&PWA.reloadAfterGame){ location.reload(); return; } tpPadMaps(); if(typeof gsWebPull==='function') gsWebPull().catch(()=>{});   /* a change made in the game's own menu reaches the page's copy (a failed read leaves the copy as it was) */ tpStage().classList.remove('tpweb'); if(typeof hmApply==='function') hmApply();
 if(res&&res.error&&typeof CCSM!=='undefined') CCSM.fail('play', res.code==='no_rom'?'missing_rom':res.code==='read'?'read_failed':res.code==='busy'?'not_ready':'other');
 if(res&&res.error&&res.code==='no_rom'){ tpNeedRom(g); return; }
 if(res&&res.error) tpNotice(res.code==='busy'||res.code==='read'?g.title+' did not start':'The game stopped', res.error); else if(res&&res.writes_failed) tpNotice('A save was not written', res.writes_failed);
 if(TP.closeAfter){ TP.closeAfter=false; tpReallyClose(); return; }
 tvDraw(true); tpStage().focus({preventScroll:true}); }
function tpNotice(title, sub){ const st=TP.pop&&!TP.pop.closed?TP.popStage:tpStage(); let m=st.querySelector('.tpnote'); if(!m){ m=st.ownerDocument.createElement('div'); m.className='tpload tpnote'; m.setAttribute('role','alert'); st.appendChild(m); }
 m.innerHTML=`<div class="tpbox"><b>${esc(title)}</b>${sub?`<span>${esc(sub)}</span>`:''}<div class="tpacts"><button type="button" class="btn sm hk" data-tp="ok">OK</button></div></div>`;
 const b=m.querySelector('[data-tp=ok]'); b.onclick=e=>{ e.stopPropagation(); m.remove(); tpStage().focus({preventScroll:true}); }; b.focus({preventScroll:true}); }
// a game whose file this browser has not kept yet: choose that file (checked by its content: only the exact version is kept)
function tpNeedRom(g){ const st=TP.pop&&!TP.pop.closed?TP.popStage:tpStage(); let m=st.querySelector('.tpnote'); if(!m){ m=st.ownerDocument.createElement('div'); m.className='tpload tpnote'; m.setAttribute('role','dialog'); st.appendChild(m); }
 m.innerHTML=`<div class="tpbox"><b>${esc(g.title)} is not ready to play in this browser</b><span>Choose this game's file (the exact version) once: a private copy is kept in this browser so it plays here. That copy is not a backup, so keep your own file. Restoring a Full Studio Backup or a collection backup (.ccs) here keeps its games too.</span><div class="tpacts"><button type="button" class="btn sm" data-tp="no">Not Now</button><button type="button" class="btn sm hk" data-tp="pick">Choose the Game File…</button></div><span class="tpmsg small" role="status"></span></div>`;
 const close=()=>{ m.remove(); tpStage().focus({preventScroll:true}); };
 m.querySelector('[data-tp=no]').onclick=e=>{ e.stopPropagation(); close(); };
 m.querySelector('[data-tp=pick]').onclick=e=>{ e.stopPropagation(); const inp=st.ownerDocument.createElement('input'); inp.type='file'; inp.accept='.nes,.unh,.bin,application/octet-stream';
  inp.onchange=async()=>{ const f=inp.files[0]; if(!f) return; const msg=m.querySelector('.tpmsg'); msg.textContent='Checking the file…';
   try{ if(f.size>16e6) throw new Error('This file is larger than any NES game.'); const u8=new Uint8Array(await f.arrayBuffer()); await CCSROMS.put(u8,{sha:g.sha, title:g.title, name:f.name, header_hex:g.header_hex}); await CCSROMS.persist();
    msg.textContent='Kept in this browser.'; close(); tpWebLaunch(g.id); }
   catch(err){ msg.textContent=String(err&&err.message||err); } }; inp.click(); };
 m.querySelector('[data-tp=pick]').focus({preventScroll:true}); }
// the website's side panel: whether this browser keeps the game's file for playing here (CCSROMS)
function tpFileText(e){ if(tpAppCapable()||typeof CCSROMS==='undefined'||!TP.romKeys) return ''; const g=tpWebGame(e.id); if(!g.sha) return ''; return TP.romKeys.has(g.sha)?'ready to play in this browser':'not in this browser yet: choose its file when you play it'; }
// the player's HOME changed its music itself (its badge or its own Settings; music_seq counts only those changes, never the Studio's own
// command): the Studio's remembered choice follows it. A report that only repeats the state is never taken as a choice
function tpMusicFromPlayer(v, seq){ if(typeof seq!=='number') return; const was=TP.musicSeq; TP.musicSeq=seq; if(was==null||seq===was) return;
 if(typeof HM!=='undefined'&&HM.pref!==v&&typeof hmSave==='function') hmSave(v); if(typeof hmUi==='function') hmUi(); }
// ================= Pop Out =================
function tpCanPop(){ return !matchMedia('(pointer:coarse)').matches&&!/Android|iPhone|iPad|Mobile/i.test(navigator.userAgent); }
function tpPopOut(){ if(TP.app){ tpCmd('popout'); return; }
 if(!tpCanPop()) return; if(TP.pop&&!TP.pop.closed){ TP.pop.focus(); return; }
 const w=window.open('', 'ccs-home-'+Date.now(), 'popup,width=1280,height=760');
 if(!w){ tpNotice('The separate window was blocked', 'Your browser blocked it. Allow pop-up windows for this site, or keep playing here.'); return; }
 const d=w.document; d.open(); d.write('<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>HOME · Classic Cart Studio</title></head><body class="tppopbody"><div class="tvstage tppopstage" id="tpPopStage" tabindex="0" aria-label="The HOME"><div class="hmwrap" id="tpPopHome"></div></div></body></html>'); d.close();
 document.querySelectorAll('style,link[rel=stylesheet]').forEach(s=>d.head.appendChild(d.importNode(s,true)));
 TP.pop=w; TP.popStage=d.getElementById('tpPopStage'); TP.popHome=d.getElementById('tpPopHome'); TP.popped=true; tpPlaceholder(true); tpToolbar();
 if(TV.treatment==='retro') TP.popStage.classList.add('retro');
 if(TP.surf){ const ns=CCSPLAY.surface(TP.popStage); CCSPLAY.retarget(ns); try{ TP.surf.remove(); }catch(e){} TP.surf=ns; }
 w.addEventListener('keydown',e=>{ if(TP.webGame) return; if(e.key==='Escape'){ e.preventDefault(); return; } tvKey(e); }); w.addEventListener('keyup',e=>{ if(!TP.webGame) tvKeyUp(e); });
 TP.popHome.addEventListener('click',e=>{ if(e.detail>=2&&tpHomeDouble()) return; const t=(e.composedPath?e.composedPath():[e.target]).find(x=>x&&x.dataset&&x.dataset.id); if(t) tpHomeClick(t.dataset.id, e.detail); });
 tvHoldWhilePressed(TP.popHome, w);   // the separate window's HOME too (tv.js)
 w.addEventListener('pagehide',()=>setTimeout(tpPopBack,0)); TP.popWatch=setInterval(()=>{ if(TP.pop&&TP.pop.closed) tpPopBack(); },400);
 if(window.ResizeObserver) new w.ResizeObserver(()=>{ if(typeof homeFit==='function') homeFit(TP.popHome); }).observe(TP.popStage);
 tvDraw(true); TP.popStage.focus(); tvSay('HOME is open in a separate window.'); }
// the separate window closed (by its X, by the browser, or Bring It Back Here): the HOME and a running game come back here; a game that
// was running waits in its game menu (nothing is saved or lost by moving it)
function tpPopBack(){ if(!TP.pop) return; const w=TP.pop; TP.pop=null; clearInterval(TP.popWatch); TP.popped=false; TP.popHome=null; TP.padRaf=0; tpPadLoop();   // the controller loop ran on that window's frames
 if(TP.surf){ const ns=CCSPLAY.surface(tpStage()); CCSPLAY.retarget(ns); try{ TP.surf.remove(); }catch(e){} TP.surf=ns; CCSPLAY.menu(); }
 try{ if(!w.closed) w.close(); }catch(e){} tpPlaceholder(false); tpToolbar(); if(TV.open){ tvDraw(true); tpStage().focus({preventScroll:true}); } }
function tpPopIn(){ if(TP.app){ tpCmd('popin'); return; } tpPopBack(); }
function tpFront(){ if(TP.app){ tpCmd('front'); return; } if(TP.pop&&!TP.pop.closed) TP.pop.focus(); }
function tpHomeClick(id, n){ const L=homeListing(TV.model,TV.S,tvSearch()); const i=L.ids.indexOf(id); if(i<0||TV.S.overlay) return;
 TV.S.index=i; TV.S.zone='games'; tvDraw(true); tvAct('select'); TP.clickOpened=id; TP.clickAt=performance.now(); }
// a double click plays: its first click opened that game's details (as home.c: any second click of the double click, wherever it lands)
function tpHomeDouble(){ const o=TV.S&&TV.S.overlay; if(o&&o.type==='details'&&o.id===TP.clickOpened&&performance.now()-TP.clickAt<1500){ const id=o.id; TV.S.overlay=null; TP.clickOpened=null; tvApplyEffects([{type:'launch',id}]); return true; } return false; }
// ================= the toolbar (F11): Theme, Display and More menus; Pop Out; HOME Music; Close =================
function tpToolbar(){ const name=(HOME_THEME_META.find(t=>t.id===(TP.app&&TP.on?(TVN.theme||TV.S&&TV.S.theme):TV.S&&TV.S.theme))||{}).name||'';
 const tn=$('tvThemeName'); if(tn) tn.textContent=name;
 if(TP.app&&TP.on&&TV.model&&!TV.model.sample){ const s=$('tvSub'); if(s) s.textContent=TV.subNote||tvPlanName()+(name?' · '+name:''); }   /* the title line names the theme the picture shows */ const cur=TP.app&&TP.on?TVN.theme:TV.S&&TV.S.theme;
 document.querySelectorAll('.tvtheme').forEach(b=>{ const on=b.dataset.theme===cur; b.classList.toggle('active',on); b.setAttribute('aria-checked',on?'true':'false'); });
 const p=$('tvPop'); if(p){ p.hidden=!(TP.app?TP.on:tpCanPop()); p.textContent=TP.popped?'Bring to Front':'Pop Out'; p.title=TP.popped?'The HOME is in a separate window: bring it in front':'Move the HOME (and a game you are playing) to its own window'; }
 const c=$('tvClose'); if(c){ const g=TP.app?TP.game:TP.webGame; c.title=g?'Close (the game asks first: save to a slot or leave)':'Close (Esc)'; c.setAttribute('aria-label',g?'Close the preview (the game asks first)':'Close the preview'); } }
function tpMenus(){ const m=$('tv'); if(!m||m.dataset.tpmenus) return; m.dataset.tpmenus='1';
 const closeAll=(back)=>{ m.querySelectorAll('.tvmenu').forEach(x=>{ const d=x.querySelector('.tvdrop'), b=x.querySelector('.tvmb'); if(d&&!d.hidden){ d.hidden=true; b.setAttribute('aria-expanded','false'); if(back) b.focus({preventScroll:true}); } }); TP.menuOpen=false; tpOwn(); };
 TP.closeMenus=closeAll;
 m.querySelectorAll('.tvmenu').forEach(x=>{ const b=x.querySelector('.tvmb'), d=x.querySelector('.tvdrop');
  b.addEventListener('click',e=>{ e.stopPropagation(); const open=d.hidden; closeAll(false); if(open){ d.hidden=false; b.setAttribute('aria-expanded','true'); TP.menuOpen=true; tpOwn(); tpReleaseKeys(); if(typeof tvReleaseAll==='function') tvReleaseAll(); const f=d.querySelector('[aria-checked=true]')||d.querySelector('button'); if(f) f.focus({preventScroll:true}); } });
  d.addEventListener('keydown',e=>{ const items=[...d.querySelectorAll('button:not([disabled]):not([hidden])')]; const i=items.indexOf(document.activeElement);
   if(e.key==='ArrowDown'||e.key==='ArrowUp'){ e.preventDefault(); e.stopPropagation(); const n=items[(i+(e.key==='ArrowDown'?1:items.length-1))%items.length]; if(n) n.focus(); }
   else if(e.key==='Escape'){ e.preventDefault(); e.stopPropagation(); closeAll(true); }
   else if(e.key==='Home'||e.key==='End'){ e.preventDefault(); e.stopPropagation(); const n=e.key==='Home'?items[0]:items[items.length-1]; if(n) n.focus(); }
   else if(['ArrowLeft','ArrowRight','z','Z','x','X','s','S','m','M','Backspace'].includes(e.key)) e.stopPropagation(); });   // an open menu owns the keys: none reaches the HOME
  d.addEventListener('click',e=>{ if(e.target.closest('button')) setTimeout(()=>closeAll(true),0); }); });
 document.addEventListener('click',e=>{ if(TP.menuOpen&&!e.target.closest('.tvmenu')) closeAll(false); },true); }
// ================= F21: a physical controller drives the website's HOME (the game player's own controller model, ccs-play.js cpPads) =================
// only while the Preview is the thing in front and owns input: no game running (it reads controllers itself), no toolbar menu, the window
// focused. Any connected controller moves through the HOME, as on the console: one with the standard layout or a known or saved setup
// fully, one with no setup by its D-pad and stick only. Each press is one HOME press; directions repeat as the keyboard's do
// the controller setups saved in this browser, read again (a setup made in a game's own menu counts here after it closes)
function tpPadMaps(){ if(typeof cpPadMapsLoad==='function') cpPadMapsLoad(); }
function tpPadLoop(){ if(TP.padRaf) return; let held=new Set(); tpPadMaps(); TP.padTold=new Set();
 const tick=()=>{ TP.padRaf=0; if(!TV.open||TP.app) { held.clear(); return; }
  const win=TP.pop&&!TP.pop.closed?TP.pop:window, ok=!TP.webGame&&!TP.menuOpen&&win.document.hasFocus()&&!win.document.hidden&&!tpCovered();
  let now=new Set();
  if(ok&&typeof cpPads==='function'){ for(const p of cpPads(win)){ const act=cpPadActs(p); for(const a of CP_ACT) if(act[a]) now.add(a);
    // F17 (D-99): a controller with no known layout and no setup moves with its D-pad; its buttons are not guessed: the first press says how to set it up
    if(p.kind===2&&!p.configured&&p.raw.btn.some(Boolean)&&!TP.padTold.has(p.key)){ TP.padTold.add(p.key); tpNotice('This controller needs a one-time setup', 'Its D-pad already works here. Its buttons are not guessed: start any game and press one of them, and the player walks you through setting it up. After that it works here too.'); } } }
  const note=(TP.pop&&!TP.pop.closed?TP.popStage:tpStage()).querySelector('.tpnote [data-tp=ok]');   // a notice over the picture owns the controller: A or B is its OK
  if(note){ if(TV.held) tvReleaseAll(); for(const a of now) if(!held.has(a)&&(a==='a'||a==='b')) note.click(); }
  else { for(const a of now) if(!held.has(a)) tvPress(a);
   for(const a of held) if(!now.has(a)&&TV.held&&TV.held.act===a) tvReleaseAll(); }
  held=now; TP.padRaf=(win.requestAnimationFrame||requestAnimationFrame)(tick); };
 TP.padRaf=requestAnimationFrame(tick); }
// ================= hooks into the Preview =================
(function(){
 const _open=openTv, _close=closeTv, _press=tvPress, _release=tvReleaseAll, _save=tvSaveImage, _eff=tvApplyEffects, _draw=tvDraw, _key=tvKey, _keyUp=tvKeyUp;
 if(window.chrome&&chrome.webview){
  chrome.webview.addEventListener('sharedbufferreceived',e=>{ let d={}; try{ d=e.additionalData||{}; if(typeof d==='string') d=JSON.parse(d); }catch(err){} if(d.kind!=='ccs-frames') return;
   TP.buf=e.getBuffer(); TP.hdr=new Uint32Array(TP.buf,0,64); TP.header=d.header|0; TP.slot=d.slot|0; if(TP.bufWait) TP.bufWait({pipe:d.pipe}); });
  chrome.webview.addEventListener('message',e=>{ if(typeof e.data==='string'&&e.data.startsWith('player:')) tpAppMsg(e.data.slice(7)); }); }
 window.__ccsPlayerBusy=()=>{ if(TP.bufWait) TP.bufWait(null); };
 openTv=function(opts){ TP.opening=tpAppCapable(); _open(opts); TP.opening=false;   // opening in the app: the player plays the HOME music, so the page's own element never starts meanwhile (hmEmbedded)
  tpMenus(); tpToolbar(); if(!tpAppCapable()&&navigator.getGamepads) tpPadLoop(); if(tpAppCapable()&&!(TV.model&&TV.model.sample)) tpAppStart();
  else if(typeof CCSROMS!=='undefined'){ CCSROMS._have=null; CCSROMS.keys().then(k=>{ TP.romKeys=k; if(!TP.lib&&typeof libFull==='function') libFull().then(l=>{ TP.lib=l; if(TV.open) tvDraw(true); },()=>{}); if(TV.open) tvDraw(true); }); }
  if(typeof hmApply==='function') hmApply(); };
 // Close: a running game asks first through its own menu (save to a slot or leave; Keep Playing cancels); the Preview closes after it
 closeTv=function(){ if(TP.app&&TP.on&&!TP.ended){ if(!TP.shown){ TP.gen++; TP.on=false; tpPost('embed-dispose'); tpLoading(null); return tpReallyClose(); } TP.closing=true; tpCmd('close'); setTimeout(()=>{ if(TP.closing&&!TP.game&&TP.on) tpReallyClose(); },4000); return; }
  if(TP.webGame){ TP.closeAfter=true; CCSPLAY.requestClose(); return; }
  tpReallyClose(); };
 window.tpReallyClose=function(){ TP.gen++; TP.on=false; TP.app=false; cancelAnimationFrame(TP.raf); tpReleaseKeys(); if(TP.pop) tpPopBack(); const st=tpStage(); if(st){ st.classList.remove('tpapp','tpweb','tpbusy'); ['tpHome','tpGame'].forEach(i=>{ const c=$(i); if(c) c.hidden=true; }); }
  tpLoading(null); tpPlaceholder(false); if(TP.closeMenus) TP.closeMenus(false); _close(); };
 tvPress=function(act){ if(TP.app&&TP.on){ if(!TV.open||!TP.shown||TP.popped) return; if(TV.held&&TV.held.act===act) return; if(TV.held) tvReleaseAll(); TV.held={act,t0:performance.now()}; tpCmd('pad d '+act); return; } if(TP.webGame) return; return _press(act); };
 tvReleaseAll=function(){ if(TP.app&&TP.on){ const h=TV.held; TV.held=null; if(h) tpCmd('pad u '+h.act); return; } return _release(); };
 tvKey=function(e){ if(TP.webGame) return; if(e.key==='Escape'&&TP.menuOpen){ e.preventDefault(); TP.closeMenus(true); return; } if(tpKey(e)) return;
  if(TP.app&&TP.on&&e.key==='Escape'&&tpKeyTarget(e)&&!TP.popped) return;   // the picture's Escape went to the player
  if(e.target&&e.target.closest&&e.target.closest('.tvmenu')) return; return _key(e); };
 tvKeyUp=function(e){ if(tpKeyUp(e)) return; return _keyUp(e); };
 const _o2=openTv; openTv=function(opts){ tvEnsure(); return _o2(opts); };   // tv.js's own key listeners call the current tvKey/tvKeyUp
 // the website's launches are real now (D-104): the game plays in the picture
 tvApplyEffects=function(fx){ const rest=[]; for(const f of fx||[]){ if(f.type==='launch'&&!TP.app) tpWebLaunch(f.id); else rest.push(f); } return _eff(rest); };
 tvDraw=function(quiet){ if(TV.open&&TV.model&&TV.S&&tvHeld(quiet)) return; if(TP.popHome&&TV.open&&TV.model&&TV.S){ homeRender(TP.popHome,TV.model,TV.S,tvCtx()); } return _draw(quiet); };   // a held press: tv.js tvHoldWhilePressed
 tvSaveImage=async function(){ if(!(TP.app&&TP.on&&TP.shown)) return _save();
  const c=$('tpGame')&&!$('tpGame').hidden?$('tpGame'):$('tpHome'); c.toBlob(async b=>{ if(!b){ toast('No preview picture yet. Try again in a moment.'); return; } const name=(HOME_THEME_META.find(t=>t.id===TVN.theme)||{}).name||'HOME';
   const res=await ccsSaveFile(csSafeName(`${tvPlanName()||'Collection'} - ${name} preview`,'.png'), b, 'Preview Image'); if(res.ok) toast(`Saved "${res.name}" (${c.width} × ${c.height})`); },'image/png'); };
 // the theme menu: in the embedded HOME it switches the player's theme (the console's own choice is never touched)
 document.addEventListener('click',e=>{ const b=e.target.closest&&e.target.closest('.tvtheme'); if(!b||!(TP.app&&TP.on)) return; e.stopImmediatePropagation(); e.preventDefault();
  if(TP.closeMenus) TP.closeMenus(false); tpStage().focus({preventScroll:true});   // the menu closes as any choice closes it (this listener runs before the menu's own)
  const th=b.dataset.theme; if(th===TVN.theme) return; TP.waitTheme=th; TP.themeSeen=false; tpCmd('theme '+th); TVN.theme=th; tpToolbar();
  // F18: the picture changes when the player has drawn the new theme; if that takes longer than a moment the loading panel says so
  setTimeout(()=>{ if(TP.waitTheme===th) tpLoading('Changing the theme…',''); },150); },true);
 // the collection, its artwork or texts change while the embedded HOME is open: its package is rebuilt, then the player reloads it
 const again=()=>{ if(!(TV.open&&TP.app&&TP.on&&TP.shown)) return; clearTimeout(TP.rebuild); TP.rebuild=setTimeout(async()=>{ TP.reloading=false; tpLoading('Updating the HOME…','');
   const r=await capi('/api/play/home-refresh',{method:'POST',body:{manifest:tvnManifest(),home:tvnHome()},timeout:180000}).catch(e=>({ok:false,error:String(e)}));
   if(!r||!r.ok){ tpLoading(null); tvSay('The HOME could not be updated: '+((r&&r.error)||'')); return; } tpCmd('reload'); setTimeout(()=>{ if(TP.reloading||!TP.st) tpLoading(null); },8000); },700); };
 const _r=window.render; window.render=function(){ const out=_r.apply(this,arguments); again(); return out; }; window.addEventListener('ccs-content-changed',again);
 window.addEventListener('focus',()=>tpOwn()); window.addEventListener('blur',()=>{ tpReleaseKeys(); tpOwn(); }); document.addEventListener('visibilitychange',()=>{ tpReleaseKeys(); tpOwn(); });
 window.addEventListener('resize',()=>{ tpFitGame(); }); window.addEventListener('pagehide',()=>{ if(TP.pop) try{ TP.pop.close(); }catch(e){} });
 // the HOME's mouse in the browser HOME: single click opens the game's details, double click plays it (as the native HOME does, F21)
 document.addEventListener('click',e=>{ const h=$('tvHome'); if(!h||!TV.open||TP.app||TP.webGame||!h.contains(e.target)) return;
  if(e.detail>=2&&tpHomeDouble()){ e.stopImmediatePropagation(); return; }
  const t=(e.composedPath?e.composedPath():[e.target]).find(x=>x&&x.dataset&&x.dataset.id); if(!t) return; e.stopImmediatePropagation(); tpHomeClick(t.dataset.id, e.detail);
  if(typeof hmUnlock==='function') hmUnlock(); },true);   // an ordinary click: HOME Music waiting for a tap starts (home-music.js hmGesture never sees this click)
})();
// the website: which games this browser keeps for playing, known from the start (cards of kept games say so)
window.addEventListener('load',()=>{ if(typeof CCSROMS!=='undefined'&&!tpAppCapable()&&CCSPLAY.supported()) CCSROMS.keys().then(()=>{ if(typeof keptAvailChanged==='function') keptAvailChanged(); }); });
