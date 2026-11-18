// ---- Classic Cart Studio for Windows: updates (owner, 2026-11-11). The app's backend asks GitHub for the latest release shortly after
//      the app starts and once a day while it is open, unless Check for Updates Automatically is off (Studio menu). A newer version is
//      offered in a dialog; Update Now downloads its installer, which the backend checks against the release's own checksums, then the
//      app closes, the installer updates it quietly and starts it again. Nothing is installed without Update Now (app_update.py). ----
const UPD={auto:true, shown:'', busy:false, started:false};
function updApp(){ return typeof CO!=='undefined'&&!!CO.desktop; }
function updMenu(){ const on=updApp(), c=$('mUpdChk'), a=$('mUpdAuto'), l=$('mUpdL'); if(c) c.hidden=!on; if(a){ a.hidden=!on; a.setAttribute('aria-checked', String(!!UPD.auto)); } if(l) l.hidden=!on; }
async function updSettings(auto){ const r=await hkPost('/api/update/settings', typeof auto==='boolean'?{auto}:{}); if(r&&r.ok) UPD.auto=r.auto!==false; updMenu(); return r; }
async function updCheck(manual){ if(!updApp()||UPD.busy) return;
 if(manual) toast('Checking for updates…');
 const r=await hkPost('/api/update/check', {force:!!manual});
 if(!r||!r.ok){ if(manual) toast((r&&r.error)||'Classic Cart Studio could not check for updates. Check the internet connection and try again.'); return; }
 if(!r.available){ if(manual) toast(`You have the newest version of Classic Cart Studio (${r.current}).`); return; }
 if(!manual){ if(UPD.shown===r.version) return; UPD.shown=r.version; updNotice(r); return; } updOffer(r); }
// an automatic check never puts a dialog over what the person is doing (D-146): the Studio menu offers the update and a short note says so;
// the menu item (or Check for Updates…) opens the same Update Available dialog, and nothing is downloaded or installed without Update Now
function updNotice(r){ UPD.avail=r; const b=$('appBtn'); if(b){ b.classList.add('hasupd'); b.title=`Classic Cart Studio ${r.version} is available`; } const c=$('mUpdChk'); if(c&&typeof miLabel==='function') miLabel(c, `Update to ${r.version}…`);
 toast(`Classic Cart Studio ${r.version} is available. When you are ready, choose Studio › Update to ${r.version}….`); }
function updOffer(r){ if($('collm')&&$('collm').classList.contains('open')&&!/^Update/.test(($('collTitle')||{}).textContent||'')) return;   /* never over another dialog: offered again at the next check */
 openColl('Update Available', '', `<p>Classic Cart Studio <b>${esc(r.version)}</b> is available. You have ${esc(r.current)}.</p>
  <ul class="fbitems"><li><b>Update Now</b> downloads it (${fmt(r.size)}), checks it, then closes Classic Cart Studio, updates it and opens it again.</li><li>Your collections, games, saved games and settings stay as they are.</li>${r.terms_changed?'<li>This version comes with new Installation Terms. The setup shows them, and updates once you agree.</li>':''}</ul>
  <div class="acts"><span class="sp"></span><button class="btn" id="updNo">Not Now</button><button class="btn hk" id="updGo">Update Now</button></div>`);
 $('updNo').onclick=closeColl; $('updGo').onclick=()=>updRun(r); }
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
window.__ccsCheckUpdates=()=>updCheck(true);   // App Tools › Check for Updates (the host calls it)
document.addEventListener('click', e=>{ const b=e.target.closest&&e.target.closest('#mUpdChk,#mUpdAuto'); if(!b) return; e.preventDefault(); document.querySelectorAll('.menu').forEach(x=>x.hidden=true);
 if(b.id==='mUpdChk'){ if(UPD.avail) updOffer(UPD.avail); else updCheck(true); } else updSettings(!UPD.auto).then(r=>{ if(r&&r.ok) toast(UPD.auto?'Classic Cart Studio checks for updates automatically.':'Automatic update checks are off. Check for Updates… still checks when you choose it.'); }); });
// start once the app's backend answers; then once a day while the app is open (the backend answers from its last hour's check)
(function updStart(){ if(!updApp()) return; let n=0; const t=setInterval(()=>{ if(!CO.ok){ if(++n>240) clearInterval(t); return; } clearInterval(t); if(UPD.started) return; UPD.started=true;
 updSettings().then(()=>{ setTimeout(()=>{ if(UPD.auto) updCheck(false); }, 10000); }); setInterval(()=>{ if(UPD.auto) updCheck(false); }, 24*3600*1000); }, 2500); updMenu(); })();
