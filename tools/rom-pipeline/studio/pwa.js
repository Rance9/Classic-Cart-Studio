// ---- The installed app and offline use (W2) ----
// The service worker (sw.js) is registered only when the Studio runs as an installed app or the person asks for offline use (Studio menu >
// Install the Website App…), never in Classic Cart Studio for Windows (its own files are already on the computer) and never silently for every
// visitor. It keeps one version of the page, data, game player and HOME art; an update waits and is used on the next start, or at once
// with Restart to Update (only while no game plays). Games, saved games and settings stay in this browser's storage; nothing is uploaded.
// An app on a phone's Home Screen may keep its own storage, separate from the browser (iPhone and iPad do): when the app starts with
// nothing in it, a note on the page says how to bring the Studio over with a Full Studio Backup (a complete .ccs file, made and opened on
// this device).
const PWA={reg:null, ask:null, status:null, busy:false};
function pwaAllowed(){ return 'serviceWorker' in navigator&&!window.__CCS_DESKTOP&&(location.protocol==='https:'||location.hostname==='127.0.0.1'||location.hostname==='localhost'); }
function pwaStandalone(){ return matchMedia('(display-mode: standalone)').matches||matchMedia('(display-mode: fullscreen)').matches||matchMedia('(display-mode: minimal-ui)').matches||navigator.standalone===true; }
function pwaApple(){ return /iPhone|iPad|iPod/.test(navigator.userAgent)||(navigator.maxTouchPoints>1&&/Macintosh/.test(navigator.userAgent)); }
function pwaSend(w, msg){ return new Promise(res=>{ if(!w){ res(null); return; } const ch=new MessageChannel(); const t=setTimeout(()=>res(null), 600000); ch.port1.onmessage=e=>{ clearTimeout(t); res(e.data); }; w.postMessage(msg, [ch.port2]); }); }
async function pwaRegister(){ if(!pwaAllowed()) return null; if(PWA.reg) return PWA.reg;
 try{ PWA.reg=await navigator.serviceWorker.register('sw.js',{updateViaCache:'none'}); }catch(e){ PWA.err=String(e&&e.message||e); return null; }
 const r=PWA.reg; r.addEventListener('updatefound',()=>{ const w=r.installing; if(w) w.addEventListener('statechange',()=>pwaUi()); }); pwaUi(); return r; }
// the covers of this Studio's own games (the HOME shows them); everything else for the HOME is in the version itself
function pwaArt(){ const ids=new Set(); try{ for(const c of csList()) for(const id of (c.sel||[])) ids.add(id); }catch(e){}
 return [...ids].map(id=>{ const g=typeof byId!=='undefined'&&byId[id]; return g&&g.art?String(g.art).replace(/^\.?\//,''):null; }).filter(a=>a&&/^art\//.test(a)); }
async function pwaPrepare(){ const r=await pwaRegister(); if(!r) return {ok:false, error:PWA.err||'This browser does not offer offline use for websites.'};
 const w=await navigator.serviceWorker.ready; const res=await pwaSend(w.active, {type:'prepare', art:pwaArt()}); PWA.status=await pwaSend(w.active, {type:'status'}); pwaUi(); return res||{ok:false, error:'The browser stopped before the Studio was ready for offline use.'}; }
async function pwaUpdate(){ const r=PWA.reg; if(!r||!r.waiting) return {ok:false, why:'none'}; if(typeof CCSPLAY!=='undefined'&&CCSPLAY.running()){ toast('Close the game from its own menu first, then restart to update.'); return {ok:false, why:'game'}; }
 const a=await pwaSend(r.waiting, {type:'skip-waiting'}); if(!a||!a.ok){ toast(a&&a.why==='other-windows'?'Close the Studio\'s other tabs and windows first, then restart to update.':'The update could not start. It is used the next time the Studio starts.'); return a||{ok:false, why:'no-answer'}; }
 PWA.updating=true; toast('Restarting with the new version…');
 // the browser switches once the earlier version has finished what it was doing; if it has not within 3 s, opening the page again ends this
 // page's own requests (the page that opens then reloads once more when the switch comes: pwaSwitched)
 setTimeout(()=>{ if(PWA.updating) location.reload(); }, 3000);
 return a; }
// the new version took over (Restart to Update, here or in another window): this page reloads into it, never mixing the two (a game that is
// open here keeps playing; this page reloads when it closes)
function pwaSwitched(){ if(typeof CCSPLAY!=='undefined'&&CCSPLAY.running()){ PWA.reloadAfterGame=true; return; } PWA.updating=false; location.reload(); }
function pwaUi(){ const i=$('mInstall'), u=$('mUpdate'), s=$('mDevL'); if(!i) return; const ok=pwaAllowed();
 i.hidden=!ok; if(s) s.hidden=!ok; if(u) u.hidden=!(PWA.reg&&PWA.reg.waiting);
 miLabel(i, pwaStandalone()?(PWA.status&&PWA.status.prepared?'Ready for Offline Use':'Getting Ready for Offline Use…'):'Install the Website App…'); }
// the website app (this website, added to the device by the browser) is not Classic Cart Studio for Windows: the dialog says so first, then
// compares the two side by side, each with its own way to get it
const PWA_WEB_SVG='<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3c2.5 2.6 3.8 5.6 3.8 9s-1.3 6.4-3.8 9c-2.5-2.6-3.8-5.6-3.8-9S9.5 5.6 12 3z"/></svg>';
const PWA_WIN_SVG='<svg viewBox="0 0 16 16" fill="currentColor" aria-hidden="true"><path d="M1 1h6.6v6.6H1zM8.4 1H15v6.6H8.4zM1 8.4h6.6V15H1zM8.4 8.4H15V15H8.4z"/></svg>';
function pwaDialog(){ const app=pwaStandalone(), apple=pwaApple(), st=PWA.status, win=!apple&&/Windows/.test(navigator.userAgent);
 const yes=t=>`<span class="cy" aria-label="Yes">✓</span>${t?`<span>${t}</span>`:''}`, no='<span class="cn" aria-label="No">—</span>';
 const getWeb=app?yes('On this device'):PWA.ask?'<button type="button" class="btn sm hk" id="pwaGo">Add to This Device</button>'
  :apple?'<span>In Safari: <b>Share</b>, then <b>Add to Home Screen</b></span>':'<span>Keep Ready for Offline Use, below</span>';
 const getWin=win?'<button type="button" class="btn sm" id="pwaDl">Download CCS</button>':'<span>On a Windows 10 or 11 PC</span>';
 const rows=[['What it is','<span>This website, added to this device by your browser</span>','<span>A program you download and install on your PC</span>'],
  ['Works on','<span>Windows, Mac, iPhone, iPad and Android</span>','<span>Windows 10 and 11</span>'],
  ['Without the internet',yes('Once it is ready for offline use'),yes('Always')],
  ['Collections, lineups and Preview Your Lineup',yes(),yes()],
  ['Play games',yes('Once this browser keeps the game file'),yes('Also in its own game window')],
  ['Add Your Own Game and Find ROM Files',no,yes()],
  ['Install on NES Classic',no,yes('With Hakchi2 CE, included')],
  ['Your collections are kept','<span>In this browser, on this device</span>','<span>On this computer</span>'],
  ['Get it',getWeb,getWin]];
 openColl('Install the Website App', '', `<div class="pwacall" role="note"><span class="pwaci" aria-hidden="true">!</span><div><b>This is not the Windows app.</b> The website app is this website, added to this device by your browser: it opens in its own window like an app, even without the internet. Classic Cart Studio for Windows is a separate program you download and install.</div></div>
  <div class="pwacmp" role="table" aria-label="The website app and the Windows app compared">
   <div class="pwa-h" role="row"><div class="pwa-c pwa-l" role="columnheader"><span class="sr">Feature</span></div><div class="pwa-c pwa-w" role="columnheader">${PWA_WEB_SVG}<b>Website App</b></div><div class="pwa-c pwa-x" role="columnheader">${PWA_WIN_SVG}<b>Windows App</b></div></div>
   ${rows.map(([l,w,x])=>`<div class="pwa-r" role="row"><div class="pwa-c pwa-l" role="rowheader">${l}</div><div class="pwa-c pwa-w" role="cell">${w}</div><div class="pwa-c pwa-x" role="cell">${x}</div></div>`).join('')}
  </div>
  ${apple?'<p class="small pwanote2">On an iPhone or iPad, the Home Screen app keeps its own storage, separate from Safari. To bring your Studio over, choose <b>Back Up Everything…</b> in Safari, then in the app choose <b>Import Collection…</b> and pick that file.</p>':''}
  <div class="msgbox${st&&st.prepared?' ok':''}" id="pwaState" role="status">${st&&st.prepared?'The website app is ready for offline use on this device.':'The website app is not yet ready for offline use on this device.'}</div>
  <div class="acts"><span class="sp"></span><button class="btn" id="pwaNo">Close</button>${!st||!st.prepared?'<button class="btn" id="pwaPrep">Keep Ready for Offline Use</button>':''}</div>`);
 $('pwaNo').onclick=closeColl; try{ $('pwaNo').focus({preventScroll:true}); const cb=$('collBody'); cb.scrollTop=0; const mb=cb.closest('.mbox'); if(mb) mb.scrollTop=0; }catch(e){}
 const d=$('pwaDl'); if(d) d.onclick=()=>{ closeColl(); if(typeof ccsDownloadLatest==='function') ccsDownloadLatest(); else location.href='https://github.com/Rance9/Classic-Cart-Studio/releases/latest'; };
 const p=$('pwaPrep'); if(p) p.onclick=async()=>{ p.disabled=true; p.textContent='Getting ready…'; const s=$('pwaState'); s.className='msgbox'; s.textContent='Getting ready: the Studio, the game player and the HOME art are being kept on this device…';
  const r=await pwaPrepare(); s.className='msgbox '+(r.ok?'ok':'bad'); s.textContent=r.ok?`The website app is ready for offline use on this device (with ${r.covers} cover${r.covers===1?'':'s'} of your games).`:'Not ready for offline use: '+(r.error||'unknown error'); p.remove(); };
 const g=$('pwaGo'); if(g) g.onclick=async()=>{ const a=PWA.ask; PWA.ask=null; try{ await a.prompt(); const c=await a.userChoice; if(c&&c.outcome==='accepted'){ closeColl(); toast('Adding the website app to this device. Open it from your Start menu, taskbar or Home Screen.'); pwaPrepare(); } }catch(e){} pwaUi(); }; }
function pwaUpdateDialog(){ const play=typeof CCSPLAY!=='undefined'&&CCSPLAY.running();
 openColl('Update Ready', '', `<p>A new version of Classic Cart Studio is ready. Your collections, games and saved games stay as they are.</p>${play?'<div class="msgbox warn">A game is open. Close it from its own menu first.</div>':''}
  <div class="acts"><span class="sp"></span><button class="btn" id="pwuLater">Later</button><button class="btn hk" id="pwuGo" ${play?'disabled':''}>Restart to Update</button></div>`);
 $('pwuLater').onclick=closeColl; $('pwuGo').onclick=()=>{ closeColl(); pwaUpdate(); }; }
// the installed app started with nothing in this storage: how to bring the Studio from the browser (a note on the page, not a popup)
async function pwaFresh(){ if(!pwaStandalone()) return; let any=false; try{ any=csList().some(c=>(c.sel||[]).length); }catch(e){}
 if(!any&&typeof CCSROMS!=='undefined'){ try{ any=(await CCSROMS.keys(true)).size>0; }catch(e){ any=true; } } if(any) return;   /* an unread store is not an empty one */
 const m=$('main'); if(!m||$('pwaNote')) return; const n=document.createElement('div'); n.id='pwaNote'; n.className='msgbox pwanote'; n.setAttribute('role','note');
 n.innerHTML='<b>Used Classic Cart Studio in your browser before?</b> This app may keep its own storage. To bring everything here, choose <b>Back Up Everything…</b> in the browser, then here choose <b>Import Collection…</b> and pick that file. <button type="button" class="btn sm" id="pwaNoteX">OK</button>';
 m.prepend(n); $('pwaNoteX').onclick=()=>n.remove(); }
if('serviceWorker' in navigator) navigator.serviceWorker.addEventListener('controllerchange', ()=>{ if(navigator.serviceWorker.controller) pwaSwitched(); });
window.addEventListener('beforeinstallprompt', e=>{ e.preventDefault(); PWA.ask=e; pwaUi(); });
window.addEventListener('appinstalled', ()=>{ PWA.ask=null; pwaUi(); });
document.addEventListener('click', e=>{ const b=e.target.closest&&e.target.closest('#mInstall,#mUpdate'); if(!b) return; e.preventDefault(); document.querySelectorAll('.menu').forEach(x=>x.hidden=true); if(b.id==='mUpdate') pwaUpdateDialog(); else pwaDialog(); });
window.addEventListener('load', ()=>{ pwaUi(); if(!pwaAllowed()) return;
 // the installed app is kept ready for offline use from its first start; a page already served by the worker keeps it current
 if(pwaStandalone()) pwaRegister().then(r=>r&&navigator.serviceWorker.ready).then(w=>w&&pwaSend(w.active,{type:'status'})).then(s=>{ PWA.status=s; pwaUi(); if(!(s&&s.prepared)) pwaPrepare(); }); else if(navigator.serviceWorker.controller) pwaRegister().then(r=>r&&navigator.serviceWorker.ready).then(w=>w&&pwaSend(w.active,{type:'status'})).then(s=>{ PWA.status=s; pwaUi(); });
 setTimeout(()=>pwaFresh(), 1500); });
