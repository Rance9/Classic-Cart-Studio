// ---- Optional usage counts (D-123): a few counts of what happened, only after this device's own choice (Studio menu > Usage Reporting) ----
// Counted: the Studio opened (once per page), a HOME ready (once per Preview opening, whatever hands it on: browser HOME, native HOME,
// embedded player, Pop Out), a game really started (its first played frame, or the Windows game window open with it; never a resume),
// a backup or restore that finished, a failure in a fixed category, a click on the installer download. Each carries only: the event, the
// release version, the surface (website, installed app, Windows app), the browser and system family, and for a failure a fixed operation
// and reason. No game, collection, file, save, controller or identifier. Nothing is sent before a choice, or after Off (requests still
// in flight are abandoned); offline, refused or slow requests are dropped (no queue, no retry); none of this is on the game's picture or
// sound path, and nothing waits for it. The website reaches the collector itself; Classic Cart Studio for Windows hands each count to its
// own backend, which checks its own copy of the choice and is the only part that contacts the collector. The choice stays on this device:
// never in a backup, an export or the Windows app's synced page state. The website's basic visit statistics are a separate service.
// Turning it Off takes effect at once, in this page and in every page of this browser that shares the choice, before anything is saved;
// if the choice cannot be saved it stays Off for now (held) and says so. The choice is read again just before each count leaves.
const CCSM={url:'https://metrics.classiccartstudio.com/v1/events', version:'__CCS_VERSION__', key:'ccs-usage-reporting-v1', heldKey:'ccs-usage-off-for-now', choice:null, held:false, home:false, inflight:0, acs:new Set(), loaded:false, busy:false, seq:0, chan:null};
const CCSM_ENUM={event:['studio_opened','home_opened','play_started','backup_completed','restore_completed','operation_failed','download_clicked'],
 operation:['home','play','backup','restore','storage'], reason:['not_ready','missing_rom','storage_unavailable','read_failed','write_failed','invalid_data','unsupported','other'],
 elapsed:['under_1s','1_to_5s','5_to_15s','15_to_60s','over_60s']};
function ccsmApp(){ return typeof window!=='undefined'&&!!window.__CCS_DESKTOP; }
function ccsmSurface(){ return ccsmApp()?'windows':(typeof pwaStandalone==='function'&&pwaStandalone())?'installed_web':'web'; }
function ccsmBrowser(){ if(ccsmApp()) return 'edge'; const u=navigator.userAgent||''; return /Edg\//.test(u)?'edge':/Firefox\//.test(u)?'firefox':/(Chrome|CriOS)\//.test(u)?'chrome':/Safari\//.test(u)?'safari':'other'; }
function ccsmOs(){ if(ccsmApp()) return 'windows'; const u=navigator.userAgent||''; return /iPhone|iPad|iPod/.test(u)||(/Macintosh/.test(u)&&navigator.maxTouchPoints>1)?'ios':/Android/.test(u)?'android':/Windows NT/.test(u)?'windows':/Mac OS X/.test(u)?'macos':/Linux/.test(u)?'linux':'other'; }
function ccsmStored(){ try{ const v=localStorage.getItem(CCSM.key); return v==='on'||v==='off'?v:null; }catch(e){ return undefined; } }   // undefined: storage unreadable
function ccsmLoad(){ if(ccsmApp()) return; let held=false; try{ held=sessionStorage.getItem(CCSM.heldKey)==='1'; }catch(e){}
 const v=ccsmStored(); CCSM.held=held; CCSM.choice=held?'off':(v===undefined?null:v); CCSM.loaded=true; }
function ccsmStop(){ CCSM.acs.forEach(a=>{ try{ a.abort(); }catch(e){} }); CCSM.acs.clear(); }   // what has not reached the collector yet is abandoned
function ccsmHold(on){ CCSM.held=on; try{ if(on) sessionStorage.setItem(CCSM.heldKey,'1'); else sessionStorage.removeItem(CCSM.heldKey); }catch(e){} }
// just before a count leaves: Off held in this page, or (website) the choice this browser has now (another page may have changed it)
function ccsmPermitted(){ if(CCSM.held) return false; if(ccsmApp()) return CCSM.choice==='on';
 const v=ccsmStored(); if(v===undefined) return false; CCSM.choice=v; return v==='on'; }
// the event, checked against the same fixed lists as the collector: anything else is never sent
function ccsmEvent(ev, o){ o=o||{}; if(!CCSM_ENUM.event.includes(ev)||!/^\d{1,3}\.\d{1,3}\.\d{1,3}(?:-[a-z0-9]{1,16})?$/.test(CCSM.version)) return null;
 const d={schema:1, event:ev, app_version:CCSM.version, surface:ccsmSurface(), browser:ccsmBrowser(), os:ccsmOs()};
 if(ev==='operation_failed'){ if(!CCSM_ENUM.operation.includes(o.operation)||!CCSM_ENUM.reason.includes(o.reason)) return null; d.operation=o.operation; d.reason=o.reason; }
 if(o.elapsed&&CCSM_ENUM.elapsed.includes(o.elapsed)) d.elapsed=o.elapsed; return d; }
function ccsmTrack(ev, o){ try{
  if(navigator.onLine===false||CCSM.inflight>=2||!ccsmPermitted()) return;   /* offline, two still on their way, no choice or Off: dropped */
  const d=ccsmEvent(ev, o); if(!d) return;
  if(ccsmApp()){ if(typeof capi==='function'){ CCSM.inflight++; capi('/api/metrics/event',{method:'POST',body:d,timeout:4000}).catch(()=>{}).finally(()=>{ CCSM.inflight--; }); } return; }
  const ac=new AbortController(), t=setTimeout(()=>ac.abort(),4000); CCSM.inflight++; CCSM.acs.add(ac);
  fetch(CCSM.url,{method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify(d), mode:'cors', credentials:'omit', cache:'no-store', referrerPolicy:'no-referrer', keepalive:true, signal:ac.signal})
   .catch(()=>{}).finally(()=>{ clearTimeout(t); CCSM.inflight--; CCSM.acs.delete(ac); });
 }catch(e){} }
function ccsmFail(operation, reason){ ccsmTrack('operation_failed', {operation, reason}); }
// the choice: Windows in its backend (the setup's choice, or this dialog's; none saved: off), the website and installed app in this browser's own storage (no choice: nothing sent)
// returns {ok, choice}: ok only when the choice was kept. Off stops counting first, whatever happens to the save; a failed On changes nothing
async function ccsmSet(v){ if(v!=='on'&&v!=='off') return {ok:false, choice:ccsmEffective()}; const seq=++CCSM.seq;
 if(v==='off'){ CCSM.choice='off'; ccsmHold(true); ccsmStop(); try{ CCSM.chan&&CCSM.chan.postMessage({choice:'off'}); }catch(e){} }   /* every page of this browser stops now */
 let kept=false, r=null;
 if(ccsmApp()){ try{ r=await capi('/api/metrics/choice',{method:'POST',body:{choice:v},timeout:8000}); }catch(e){ r=null; } kept=!!(r&&r.ok&&r.choice===v); }
 else { try{ localStorage.setItem(CCSM.key, v); kept=localStorage.getItem(CCSM.key)===v; }catch(e){ kept=false; } }
 if(kept&&seq===CCSM.seq){ CCSM.choice=v; ccsmHold(false); if(ccsmApp()){ CCSM.chosen=true; CCSM.source=r.source||'app'; } }   /* a later request decides, not this one */
 return {ok:kept, choice:ccsmEffective()}; }
function ccsmEffective(){ return CCSM.held?'off':CCSM.choice; }
async function ccsmLoadApp(){ if(!ccsmApp()||typeof capi!=='function') return; let r; try{ r=await capi('/api/metrics/choice',{timeout:8000}); }catch(e){ r=null; } CCSM.choice=r&&r.ok&&(r.choice==='on'||r.choice==='off')?r.choice:'off'; CCSM.held=!!(r&&r.ok&&r.held); CCSM.chosen=!!(r&&r.ok&&r.chosen); CCSM.source=r&&r.ok&&r.chosen?r.source||null:null; CCSM.loaded=true; }   /* no answer or no decision: Off; Off held by the backend: Off */
function ccsmNow(){ if(CCSM.held) return 'Off for now (not saved)';
 const how=ccsmApp()&&CCSM.chosen?(CCSM.source==='installer'?' (chosen when installing)':' (chosen here)'):'';
 return CCSM.choice==='on'?'On'+how:ccsmApp()?(CCSM.chosen?'Off'+how:'Off (no choice made)'):CCSM.choice==='off'?'Off':'Off (you have not chosen)'; }
function ccsmDialog(){ document.querySelectorAll('.menu').forEach(x=>x.hidden=true); const on=ccsmEffective()==='on';
 openColl('Usage Reporting','', `<p class="lead">Help improve Classic Cart Studio by sending basic usage and error counts. No game files, game names, collections, saves or personal file details are included.</p>
  <div class="kv"><dt>Now</dt><dd id="umNow">${ccsmNow()}</dd></div>
  <p class="small">When it is on, ${ccsmApp()?'this app':'this '+(ccsmSurface()==='installed_web'?'installed app':'browser')} counts when the Studio or a HOME opens, a game starts, a backup or restore finishes, and a failure of a fixed kind. Each count says only which of these happened, the release version, ${ccsmApp()?'that it is the Windows app':'website or installed app'}, and the browser and system family. They go to Classic Cart Studio's own collector at Cloudflare. Nothing is queued: a count that cannot be sent at once is dropped.</p>
  <p class="small">${ccsmApp()?'Nothing is sent unless it is on (it can be chosen when installing or here).':'Nothing is sent until you choose.'} The choice is for this ${ccsmApp()?'computer':'browser on this device'} only: backups and exports never carry it. Everything works the same either way. ${ccsmApp()?'':'The website\'s basic visit statistics are separate; '}<a href="#" id="umLegal">Legal &amp; Privacy</a> explains what is collected and kept.</p>
  <p class="msgbox warn" id="umMsg" role="alert" hidden></p>
  <div class="acts"><button class="btn ghost" id="umOff">${on?'Turn Off':'Keep Off'}</button><span class="sp"></span><button class="btn hk" id="umOn"${on?' disabled':''}>Turn On</button></div>`);
 const act=async v=>{ if(CCSM.busy) return; CCSM.busy=true; $('umOff').disabled=true; $('umOn').disabled=true; $('umMsg').hidden=true;   /* one request at a time */
  let r; try{ r=await ccsmSet(v); } finally { CCSM.busy=false; }
  if(r.ok){ closeColl(); toast(v==='off'?'Usage reporting is off on this device.':'Usage reporting is on. Turn it off here at any time.'); return; }
  const m=v==='off'?`Usage reporting is off for now, but this choice could not be saved, so it may be on again the next time ${ccsmApp()?'Classic Cart Studio opens':'this page opens'}. Try again.`
   :'Usage reporting was not turned on, because this choice could not be saved. Try again.';   /* the dialog stays open to try again */
  $('umNow').textContent=ccsmNow(); $('umMsg').textContent=m; $('umMsg').hidden=false; $('umOff').disabled=false; $('umOn').disabled=ccsmEffective()==='on'; toast(m); };
 $('umOff').onclick=()=>act('off');
 $('umOn').onclick=()=>act('on');
 $('umLegal').onclick=e=>{ e.preventDefault(); closeColl(); if(typeof openGuide==='function') openGuide('legal'); }; }
// ---- what is counted (hooks call these; each also works when nothing is counted) ----
CCSM.homeReady=function(){ if(CCSM.home) return; CCSM.home=true; ccsmTrack('home_opened'); };   // once per Preview opening
CCSM.played=function(){ ccsmTrack('play_started'); };
// a game on the website: counted at its first played frame (the start choices are not play), checked a few times a second outside its loop
CCSM.watchWebPlay=function(){ if(typeof CCSPLAY==='undefined') return; let n=0, seen=false; const t=setInterval(()=>{ n++; const v=CCSPLAY.view(), on=CCSPLAY.running(); if(on) seen=true;
 if(v===1){ clearInterval(t); CCSM.played(); } else if((seen&&!on)||(!seen&&n>240)||n>4800) clearInterval(t); },250); };   /* the session starts asynchronously: it ends the watch only once it has run (k3f first run: a check before it began ended it) */
CCSM.track=ccsmTrack; CCSM.fail=ccsmFail;
(function(){
 if(typeof openTv==='function'){ const _o=openTv; openTv=function(){ CCSM.home=false; const r=_o.apply(this,arguments);
   try{ const nat=typeof tvNativeCapable==='function'&&tvNativeCapable(), emb=typeof tpAppCapable==='function'&&tpAppCapable(); if(!nat&&!emb&&typeof TV!=='undefined'&&TV.open) CCSM.homeReady(); }catch(e){}   /* the browser HOME is drawn by openTv itself */
   return r; }; }
 if(typeof closeTv==='function'){ const _c=closeTv; closeTv=function(){ const r=_c.apply(this,arguments); CCSM.home=false; return r; }; }
 document.addEventListener('click',e=>{ const b=e.target.closest&&e.target.closest('#mUsage'); if(b){ e.preventDefault(); ccsmDialog(); return; }
  const a=e.target.closest&&e.target.closest('a[href*="/releases/download/"]'); if(a) ccsmTrack('download_clicked'); });
 if(!ccsmApp()){   // the website's pages share one choice: a change in one reaches the others at once (Off held there too, when it could not be saved)
  try{ if(typeof BroadcastChannel!=='undefined'){ CCSM.chan=new BroadcastChannel('ccs-usage-choice-change'); CCSM.chan.onmessage=e=>{ if(e&&e.data&&e.data.choice==='off'){ CCSM.choice='off'; ccsmHold(true); ccsmStop(); } }; } }catch(e){}
  try{ window.addEventListener('storage',e=>{ if(e.key!==CCSM.key&&e.key!==null) return; const v=ccsmStored(); if(v===undefined) return;
   if(v==='on'){ ccsmHold(false); CCSM.choice='on'; } else { CCSM.choice=v; ccsmStop(); } }); }catch(e){} }   /* On chosen in another page is that person's latest choice */
 const start=async()=>{ ccsmLoad(); if(ccsmApp()) await ccsmLoadApp();
  let seen=false; try{ seen=sessionStorage.getItem('ccs-usage-studio')==='1'; sessionStorage.setItem('ccs-usage-studio','1'); }catch(e){}   /* a reload of the same tab is the same visit */
  if(!seen) ccsmTrack('studio_opened'); };
 if(ccsmApp()&&typeof CO!=='undefined'&&!CO.ok){ let k=0; const w=setInterval(()=>{ k++; if(CO.ok||k>120){ clearInterval(w); start(); } },500); }   /* the Windows backend answers first */
 else if(document.readyState==='loading') document.addEventListener('DOMContentLoaded', start); else start();
})();
