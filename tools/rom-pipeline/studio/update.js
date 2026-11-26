// ---- Classic Cart Studio for Windows: updates (product and channel streams, 0.11.4). The app's backend reads the
//      website's release streams shortly after the app starts and once a day while it is open, unless Check for Updates Automatically is
//      off (Studio menu), and offers only a newer Classic Cart Studio of the app's own channel (a preview only newer previews), compared
//      with both parts it installed (the app and its bundled Workbench): one Classic Cart Studio update. A production release is offered in
//      a dialog; Update Now downloads its installer, which the backend checks against the release's own checksums, then the app closes,
//      the installer updates it quietly and starts it again. An unsigned preview is offered with Open Download Page instead (the website's
//      download page, in the browser); the app downloads nothing for it. Nothing is installed without Update Now (app_update.py). An answer
//      that cannot be read is "could not check", never "up to date". ----
const UPD={auto:true, shown:'', busy:false, started:false};
// a version as people see it: "0.11.4 (Preview)" for the preview channel
function updLabel(v, ch){ return v+(ch==='preview'?' (Preview)':''); }
function updApp(){ return typeof CO!=='undefined'&&!!CO.desktop; }
function updMenu(){ const on=updApp(), c=$('mUpdChk'), a=$('mUpdAuto'), l=$('mUpdL'); if(c) c.hidden=!on; if(a){ a.hidden=!on; a.setAttribute('aria-checked', String(!!UPD.auto)); } if(l) l.hidden=!on; }
async function updSettings(auto){ const r=await hkPost('/api/update/settings', typeof auto==='boolean'?{auto}:{}); if(r&&r.ok) UPD.auto=r.auto!==false; updMenu(); return r; }
async function updCheck(manual){ if(!updApp()||UPD.busy) return;
 if(manual&&UPD.asking){ toast('Checking for updates…'); return; }   /* one check the person asked for at a time (a second click while it runs) */
 if(manual){ UPD.asking=true; toast('Checking for updates…'); }
 let r; try{ r=await hkPost('/api/update/check', {force:!!manual}); } finally{ if(manual) UPD.asking=false; }
 if(!r||!r.ok){ if(manual) toast((r&&r.error)||'Classic Cart Studio could not check for updates. Check the internet connection and try again.'); return; }
 if(!r.available){ if(manual) toast(r.ahead?`Classic Cart Studio is up to date: you have ${updLabel(r.current, r.channel)}, newer than the newest published version.`:`Classic Cart Studio is up to date (${updLabel(r.current, r.channel)}).`); return; }
 if(!manual){ if(UPD.shown===r.version) return; UPD.shown=r.version; updNotice(r); return; } updKnown(r); if(!updOffer(r)) updLater(r); }
// an update is known: the Studio button shows a dot, the menu item reads Update to x.y.z… and the window's status bar reads Update Available
function updKnown(r){ UPD.avail=r; const b=$('appBtn'); if(b){ b.classList.add('hasupd'); b.title=`Classic Cart Studio update available: ${updLabel(r.version, r.channel)}`; } const c=$('mUpdChk'); if(c&&typeof miLabel==='function') miLabel(c, `Update to ${r.version}…`); appBarSoon(); }
// an automatic check never puts a dialog over what the person is doing: the Studio menu offers the update and a short note says so;
// the menu item (or Check for Updates…) opens the same Update Available dialog, and nothing is downloaded or installed without Update Now
function updNotice(r){ updKnown(r);
 toast(`Classic Cart Studio update available: ${updLabel(r.version, r.channel)}. When you are ready, choose Update Available at the bottom of the window, or Studio › Update to ${r.version}….`); }
// asked for, but another dialog is open: the update is not offered over it; a short note says what to do (nothing opens over that dialog)
function updLater(r){ toast(`Classic Cart Studio update available: ${updLabel(r.version, r.channel)}. Close what is open first, then choose Update Available at the bottom of the window.`); }
// the Update Available dialog; false when it was not opened (another dialog is open: never over it)
function updOffer(r){ if($('collm')&&$('collm').classList.contains('open')&&!/Update Available$/.test(($('collTitle')||{}).textContent||'')) return false;
 // an unsigned preview: the website's download page, opened in the browser (the app downloads and installs nothing for it)
 if(r.manual){ openColl('Classic Cart Studio Update Available', '', `<p><b>Classic Cart Studio update available:</b> version ${esc(updLabel(r.version, r.channel))}. You have ${esc(updLabel(r.current, r.channel))}.</p>
  <ul class="fbitems"><li><b>Open Download Page</b> opens the Classic Cart Studio download page in your web browser. Download the new installer there and run it.</li><li>Your collections, games, saved games and settings stay as they are.</li></ul>
  <div class="acts"><span class="sp"></span><button class="btn" id="updNo">Not Now</button><a class="btn hk" id="updGo" href="${CCS_APP_DOWNLOAD}" target="_blank" rel="noopener">Open Download Page</a></div>`);
  $('updNo').onclick=closeColl; $('updGo').addEventListener('click', ()=>setTimeout(closeColl, 0)); return true; }
 openColl('Classic Cart Studio Update Available', '', `<p><b>Classic Cart Studio update available:</b> version ${esc(updLabel(r.version, r.channel))}. You have ${esc(updLabel(r.current, r.channel))}.</p>
  <ul class="fbitems"><li><b>Update Now</b> downloads it (${fmt(r.size)}), checks it, then closes Classic Cart Studio, updates it and opens it again.</li><li>Your collections, games, saved games and settings stay as they are.</li>${r.terms_changed?'<li>This version comes with new Installation Terms. The setup shows them, and updates once you agree.</li>':''}</ul>
  <div class="acts"><span class="sp"></span><button class="btn" id="updNo">Not Now</button><button class="btn hk" id="updGo">Update Now</button></div>`);
 $('updNo').onclick=closeColl; $('updGo').onclick=()=>updRun(r); return true; }
function updFail(msg, r){ UPD.busy=false; procSet('collm',null); $('collBody').innerHTML=`<div class="msgbox bad"><b>The update did not finish.</b> ${esc(msg)} Classic Cart Studio is as it was.</div><div class="acts"><span class="sp"></span><button class="btn" id="updNo">Close</button>${r?'<button class="btn hk" id="updAgain">Try Again</button>':''}</div>`; $('updNo').onclick=closeColl; if($('updAgain')) $('updAgain').onclick=()=>updRun(r); }
async function updRun(r){ if(UPD.busy) return;
 // a game open in the Preview or in its own window is closed by the person first, from its own menu (nothing unsaved is lost)
 const ps=await capi('/api/play/status'); const tvOpen=typeof TV!=='undefined'&&!!TV.open;
 if((ps&&ps.running)||tvOpen){ $('collBody').innerHTML=`<div class="msgbox warn"><b>Close ${tvOpen?'HOME':'the game window'} first.</b> Then choose Update Now again.</div><div class="acts"><span class="sp"></span><button class="btn" id="updNo">Not Now</button><button class="btn hk" id="updGo">Update Now</button></div>`; $('updNo').onclick=closeColl; $('updGo').onclick=()=>updRun(r); return; }
 UPD.busy=true; procSet('collm','running');
 $('collBody').innerHTML=`<div class="msgbox" id="updProg" role="status">Starting the download…</div><div class="acts"><span class="sp"></span><button class="btn" id="updStop">Cancel</button></div>`;
 $('updStop').onclick=async()=>{ await hkPost('/api/update/cancel', {}); };
 const s=await hkPost('/api/update/download', {version:r.version}); if(!s||!s.ok) return updFail((s&&s.error)||'The download could not start.', r);
 let st;
 for(;;){ await new Promise(z=>setTimeout(z, 400)); st=await hkPost('/api/update/status', {}); if(!st||!st.ok) continue; const p=$('updProg');
  if(st.phase==='downloading'){ if(p) p.textContent=`Downloading the update… ${fmt(st.bytes_done)} of ${fmt(st.bytes_total)}`; continue; }
  if(st.phase==='checking'){ if(p) p.textContent='Checking the download…'; continue; }
  break; }
 if(st.phase==='canceled'){ UPD.busy=false; procSet('collm',null); closeColl(); toast('Update canceled. Nothing was installed.'); return; }
 if(st.phase!=='ready') return updFail(st.error||'The download did not finish.', r);
 $('collBody').innerHTML=`<div class="msgbox ok" role="status"><b>Classic Cart Studio closes now${st.terms_changed?'. The setup opens with the new Installation Terms.':', updates and opens again in a moment.'}</b></div>`;
 const i=await hkPost('/api/update/install', {}); if(!i||!i.ok) return updFail((i&&i.error)||'The installer could not be started.', r);
 setTimeout(()=>{ try{ window.chrome.webview.postMessage('app-update-exit'); }catch(e){ updFail('Classic Cart Studio could not close by itself. Close it to finish the update.'); } }, 900); }
// App Tools › Check for Updates, and CCS Console Workbench's Update Classic Cart Studio… (the host calls it, also right after it starts):
// the same check as Check for Updates…, once the app's backend answers
window.__ccsCheckUpdates=()=>{ let n=0; (function go(){ if(updApp()&&CO.ok) return updCheck(true); if(++n>=120){ toast('Classic Cart Studio could not check for updates. Try again in a moment.'); return; } setTimeout(go, 500); })(); };
document.addEventListener('click', e=>{ const b=e.target.closest&&e.target.closest('#mUpdChk,#mUpdAuto'); if(!b) return; e.preventDefault(); document.querySelectorAll('.menu').forEach(x=>x.hidden=true);
 if(b.id==='mUpdChk'){ if(UPD.avail) updOffer(UPD.avail); else updCheck(true); } else updSettings(!UPD.auto).then(r=>{ if(r&&r.ok) toast(UPD.auto?'Classic Cart Studio checks for updates automatically.':'Automatic update checks are off. Check for Updates… still checks when you choose it.'); }); });
// start once the app's backend answers; then once a day while the app is open (the backend answers from its last hour's check)
(function updStart(){ if(!updApp()) return; let n=0; const t=setInterval(()=>{ if(!CO.ok){ if(++n>240) clearInterval(t); return; } clearInterval(t); if(UPD.started) return; UPD.started=true;
 updSettings().then(()=>{ setTimeout(()=>{ if(UPD.auto) updCheck(false); }, 10000); }); setInterval(()=>{ if(UPD.auto) updCheck(false); }, 24*3600*1000); }, 2500); updMenu(); })();
// ---- Classic Cart Studio for Windows: the window's status bar (the window draws it below this page: StatusBar.cs). This page tells the
//      window the open collection (its name and the game count the collection shows) and whether an update is known, nothing else; the
//      window adds its own running version. Only this page inside the app sends it (CO.desktop, over the window's own message channel);
//      the website and the game player have no status bar and send nothing. The bar's Check for Updates is the same check as Check for
//      Updates…; once an update is known it reads Update Available and opens the same Update Available dialog. ----
const APPBAR={last:'', t:0};
function appBarOn(){ return updApp()&&typeof window!=='undefined'&&!!(window.chrome&&window.chrome.webview&&typeof window.chrome.webview.postMessage==='function'); }
// what the bar shows: no collection open (none, or the collections could not be read) is null, never another collection
function appBarState(){ const r=typeof csActive==='function'?csActive():null;
 return {collection:r?{name:String((typeof COLL!=='undefined'&&COLL&&COLL.name)||r.name||'My Collection'), games:typeof sel!=='undefined'&&sel?sel.size:0}:null,
  update:UPD.avail?{version:String(UPD.avail.version||'')}:null}; }
function appBarSend(){ APPBAR.t=0; if(!appBarOn()) return; let s; try{ s=JSON.stringify(appBarState()); }catch(e){ return; } if(s===APPBAR.last) return; APPBAR.last=s;
 try{ window.chrome.webview.postMessage('status-bar '+s); }catch(e){} }
function appBarSoon(){ if(!APPBAR.t) APPBAR.t=setTimeout(appBarSend, 0); }
// the collection changes through these (opened, switched, renamed, games added or removed, restored, removed): the bar follows at once
(function(){ if(typeof window==='undefined') return; ['persist','saveColl','render','updateStatusLine','csMenuRefresh'].forEach(n=>{ const f=window[n]; if(typeof f!=='function') return;
 window[n]=function(){ const r=f.apply(this,arguments); try{ appBarSoon(); }catch(e){} return r; }; }); })();
// the status bar's Check for Updates (the window calls it): the same check as Check for Updates…; with an update already known, the same
// Update Available dialog (never over another dialog, and not while that update is already under way)
window.__ccsAppUpdate=()=>{ if(UPD.busy){ toast('The update is already under way.'); return; } if(UPD.avail){ if(!updOffer(UPD.avail)) updLater(UPD.avail); return; } window.__ccsCheckUpdates(); };
appBarSoon();
