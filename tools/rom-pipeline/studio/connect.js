// ---- Studio backend client: environment-aware. Three ways this page can run (the public website never pairs; it hands collections to the desktop app):
//      served  - the companion on this PC serves the page from http://127.0.0.1:<port>/ (same origin; per-process token in a meta tag)
//      hosted  - the public website (https://rance9.github.io/...) or a local preview; talks to http://127.0.0.1:8765 with CORS + a paired bearer token
//      file    - opened as a file; the browser cannot reach the companion (guide only)
const CO=COMPANION; CO.REQ_PROTOCOL=1; CO.PORT=(()=>{ const q=new URLSearchParams(location.search).get('companion'); return /^\d{4,5}$/.test(q||'')?Number(q):8765; })(); /* ?companion=<port>: a local preview or test may talk to a companion on another loopback port (not a secret) */ CO.desktop=!!(window.__CCS_DESKTOP&&window.__CCS_DESKTOP.token&&window.__CCS_DESKTOP.port); if(CO.desktop){ CO.PORT=window.__CCS_DESKTOP.port; CO.app=window.__CCS_DESKTOP.version||''; CO.served=false; /* the desktop session wins over the companion-served page token (the workspace build has the backend serve the site) */ } CO.base=CO.served?'':'http://127.0.0.1:'+CO.PORT; CO.file=location.protocol==='file:'; CO.hosted=!CO.served&&!CO.file; CO.state='unknown'; CO.bearer=null; CO.expires=0; CO.version=null; CO.protocol=null; CO.store={}; CO.error='';
const PKEY='studio-companion-v1', REPO='Rance9/Classic-Cart-Studio', RELEASES_API='https://api.github.com/repos/'+REPO+'/releases/latest', RELEASE_PAGE='https://github.com/'+REPO+'/releases/latest', SITE_URL='https://classiccartstudio.com/';
function loadPair(){ if(CO.desktop){ CO.bearer=window.__CCS_DESKTOP.token; CO.expires=2147483647; return; } CO.bearer=null; CO.expires=0; try{ localStorage.removeItem(PKEY); }catch(e){} }   /* the website never holds a backend token: it hands collections to the desktop app instead */
function savePair(t,exp){ if(CO.desktop) return; CO.bearer=null; CO.expires=0; }
function hdrs(extra){ const h=Object.assign({'X-NES-Protocol':String(CO.REQ_PROTOCOL)},extra||{}); if(CO.served) h['X-NES-Token']=CO.token; else if(CO.bearer) h['Authorization']='Bearer '+CO.bearer; return h; }
function artUrl(p){ return p?(CO.base?CO.base+'/'+p:p):''; }
/* a secure (https) page may reach http://127.0.0.1 only when it declares the target address space (Chrome/Edge Local Network Access); older browsers ignore the option */
function lnaInit(init){ if(CO.hosted) init.targetAddressSpace='loopback'; return init; }
async function capi(path, opts){ opts=opts||{}; const init=lnaInit({method:opts.method||'GET',headers:hdrs(opts.body?{'Content-Type':'application/json'}:{}),cache:'no-store'}); if(opts.body) init.body=JSON.stringify(opts.body);
 const ac=('AbortController' in window)?new AbortController():null; if(ac){ init.signal=ac.signal; setTimeout(()=>ac.abort(),opts.timeout||12000); }
 const used=CO.bearer; let r; try{ r=await fetch(CO.base+path,init); }catch(e){ if(e&&e.name==='AbortError') return {ok:false,error:'Classic Cart Studio did not answer in time. Try again.',timeout:true}; CO.ok=false; CO.info=null; CO.state=CO.file?'file':'off'; connPill(); return {ok:false,error:'Classic Cart Studio is not reachable on this computer.',unreachable:true}; }
 const j=await r.json().catch(()=>({ok:false,error:'Unexpected response from Classic Cart Studio (HTTP '+r.status+'). Try again.'}));
 if(r.status===401&&!CO.served&&used===CO.bearer){ if(CO.desktop){ CO.ok=false; CO.state='error'; CO.error='the app could not connect to its own service'; } else { savePair(null,0); CO.ok=false; CO.state='unpaired'; } connPill(); }
 if(r.status===426){ CO.ok=false; CO.state='old'; CO.version=j.version||CO.version; CO.protocol=j.protocol||CO.protocol; connPill(); }
 if(!r.ok&&!j.error) j.error='HTTP '+r.status; j.status=r.status; return j; }
const HK_WAITS=/^\/api\/(import\/pick|personal\/(pick|relink)|library\/locate|package\/(save|export))$/;   // these wait for a person in a Windows Open or Save dialog (or build a whole package), so 12 s is far too short
async function hkPost(path, body){ return capi(path,{method:'POST',body:body||{}, timeout:HK_WAITS.test(path)?30*60000:undefined}); }
async function lnaDenied(){ try{ if(navigator.permissions&&navigator.permissions.query){ const p=await navigator.permissions.query({name:'local-network-access'}); return p.state==='denied'; } }catch(e){} return false; }
let STATUS_P=null;
async function companionStatus(){ if(STATUS_P) return STATUS_P; STATUS_P=companionStatusOnce(); try{ return await STATUS_P; } finally{ STATUS_P=null; if(CO.forceRetry){ CO.forceRetry=false; clearTimeout(DESKT); setTimeout(()=>companionStatus(),50); } } }
async function companionStatusOnce(){ CO.ok=false; CO.info=null; CO.error=''; if(CO.state==='unknown'){ CO.state='checking'; connPill(); }
 if(CO.file){ CO.state='file'; try{ await fetch('http://127.0.0.1:'+CO.PORT+'/api/ping',{mode:'no-cors',cache:'no-store',targetAddressSpace:'loopback'}); CO.reachable=true; }catch(e){ CO.reachable=false; } connPill(); return; }
 if(CO.hosted&&!CO.desktop){ CO.state='web'; CO.ok=false; connPill(); return; }   /* the website: no probe, no pairing; anything that needs the computer continues in the desktop app */
 if(CO.desktop&&window.__CCS_DESKTOP.ready===false&&!CO.readySignal){ connDbg('waiting for the backend-ready signal'); CO.state='booting'; connPill(); return; }   /* never probe a port nobody listens on: Chromium backs off from the host for 10-60 s afterwards */
 if(CO.hosted){ loadPair(); let v; try{ const init=lnaInit({headers:hdrs(),cache:'no-store'}); if(CO.desktop&&'AbortController' in window){ const ac=new AbortController(); init.signal=ac.signal; setTimeout(()=>ac.abort(),5000); } const r=await fetch(CO.base+'/api/version',init); v=await r.json(); }catch(e){ if(CO.desktop){ connDbg('handshake failed '+(e&&e.name)); CO.state='booting'; connPill(); desktopRetry(); return; } CO.state=(await lnaDenied())?'denied':'off'; connPill(); return; }
  if(CO.desktop){ connDbg('handshake ok desktop='+v.desktop); CO.bootTries=0; if(!v.desktop){ CO.state='error'; CO.error='the app could not connect to its own service'; connPill(); return; } }
  CO.version=v.version||null; CO.protocol=v.protocol||0; CO.frozen=!!v.frozen; CO.capabilities=Array.isArray(v.capabilities)?v.capabilities:[]; if(!(CO.protocol>=CO.REQ_PROTOCOL)){ CO.state='old'; connPill(); return; } if(!v.paired){ savePair(null,0); CO.state='unpaired'; connPill(); return; } }
 // Stage 3: the handshake above is the connection. Readiness (Hakchi, driver, console, library copies) loads afterwards and never blocks this.
 if(CO.hosted){ CO.ok=true; CO.state='ok'; connPill(); }
 const s=await capi('/api/status',{timeout:8000}); if(!s.ok){ if(s.timeout){ CO.ok=CO.hosted; CO.state=CO.hosted?'ok':'error'; CO.error='the status check timed out'; CO.diag={state:'delayed'}; } else if(!s.unreachable&&s.status!==401&&s.status!==426){ CO.state='error'; CO.error=s.error||''; CO.ok=false; } connPill(); return; }
 CO.info=s; CO.ok=true; CO.state='ok'; CO.version=s.version||CO.version; CO.protocol=s.protocol||CO.protocol; CO.devStore=!!s.dev_store; CO.diag=s.diag||{state:'ok'}; connPill(); bundleNotice(s.bundle);
 capi('/api/library/store',{timeout:8000}).then(st=>{ CO.store=st.ok?(st.store||{}):{}; }).catch(()=>{ CO.store={}; });
 if(CO.diag&&CO.diag.state==='checking'){ diagFollow(); } }
// Stage 4: background readiness — poll the cached diagnostics for a bounded time; the connection stays green either way
let DIAGT=null;
function diagFollow(){ clearTimeout(DIAGT); let tries=0; const tick=async()=>{ if(!CO.ok) return; const s=await capi('/api/status',{timeout:8000}); if(s.ok){ CO.info=s; CO.diag=s.diag||{state:'ok'}; hkLabelUpdate(); if(typeof onDiagUpdate==='function') onDiagUpdate(s); } if(s.ok&&CO.diag.state==='checking'&&tries++<20){ DIAGT=setTimeout(tick,1500); } else if(!s.ok||tries>=20){ CO.diag={state:'delayed'}; if(typeof onDiagUpdate==='function') onDiagUpdate(null); } }; DIAGT=setTimeout(tick,1200); }
/* the installation's integrity (every file installed with the app is present and intact): a missing or damaged component is repaired by
   running the installer again; the app never downloads a replacement */
function bundleNotice(b){ let el=document.getElementById('repairNotice'); if(!b||b.ok){ if(el) el.remove(); return; }
 if(!el){ el=document.createElement('div'); el.id='repairNotice'; el.className='msgbox bad'; el.setAttribute('role','alert'); el.style.cssText='position:fixed;left:16px;right:16px;bottom:16px;z-index:9999;max-width:760px;margin:0 auto;box-shadow:0 6px 24px rgba(0,0,0,.45)'; document.body.appendChild(el); }
 el.innerHTML=`<b>Classic Cart Studio needs to be repaired.</b> ${esc(b.message||'')}${(b.components||[]).length?'<div class="small" style="margin-top:6px">'+(b.components||[]).map(c=>esc(c.label)+(c.examples&&c.examples.length?': '+esc(c.examples.join(', ')):'')).join('<br>')+'</div>':''}`; }
function diagText(){ const d=CO.diag||{}; if(!CO.ok) return ''; if((CO.info||{}).bundle&&CO.info.bundle.ok===false) return 'Classic Cart Studio needs repair'; if(d.state==='checking') return 'Checking console setup…'; if(d.state==='delayed') return 'Console setup check delayed'; const i=(CO.info||{}).install||{}; const dr=(CO.info||{}).driver||{}; if(i.state&&i.state!=='ok') return 'Hakchi2 CE is not set up'; if(dr&&dr.state==='missing') return 'The console USB driver is missing'; return 'Console setup ready'; }
function fileAvailable(id){ const g=byId[id]; if(!g) return false; if(g.source==='personal') return !!(g.personal&&g.personal.file_present!==false); if(CO.devStore) return true; const s=CO.store[id]; return !!(s&&s.present); }
window.CONN_DBG=[]; function connDbg(x){ try{ CONN_DBG.push([Date.now()%100000,x]); if(CONN_DBG.length>80) CONN_DBG.shift(); }catch(e){} }
let DESKT=null; function desktopRetry(){ clearTimeout(DESKT); CO.bootTries=(CO.bootTries||0)+1; connDbg('retry '+CO.bootTries); if(CO.bootTries>60) { CO.state='error'; CO.error='the app could not start its own service'; connPill(); return; } DESKT=setTimeout(()=>companionStatus(),3000); }
window.__ccsBackendReady=function(){ connDbg('backend ready signal'); CO.readySignal=true; clearTimeout(DESKT); if(CO.state==='ok') return; if(STATUS_P){ CO.forceRetry=true; } else companionStatus(); };
const CONN_TXT={web:['off',''],unknown:['','Checking connection…'],checking:['','Checking connection…'],booting:['','Starting…'],ok:['ok','Connected'],off:['off','App not running'],unpaired:['warn','Not available'],pairing:['warn','Waiting for approval'],denied:['bad','Permission needed'],old:['warn','Update needed'],file:['off','Opened as a file'],error:['bad','App problem']};
function connPill(){ const b=$('connPill'); if(!b) return; const [c,t]=CONN_TXT[CO.state]||CONN_TXT.unknown; b.hidden=CO.served?false:CO.desktop?!(CO.state==='error'||CO.state==='old'):true; b.className='conn '+c; b.innerHTML='<span class="dot"></span>'+esc(CO.desktop&&CO.state==='ok'?'Studio for Windows':CO.served&&CO.state==='ok'?'Studio on this computer':t)+(CO.state==='ok'&&CO.version?' <span class="mut">v'+esc(CO.version)+'</span>':'');
 b.title=CO.state==='ok'?('Connected. '+diagText()):CO.state==='denied'?'Your browser blocked access to this computer. Click for how to allow it.':CO.state==='pairing'?'Choose Allow in the approval window':'Connection status. Click for details.'; try{ hkLabelUpdate(); }catch(e){} }
function connSummary(){ switch(CO.state){
 case 'ok': return (CO.desktop?`Classic Cart Studio for Windows ${esc(CO.version||'')} is running. `:CO.served?'Connected to Classic Cart Studio on this computer. ':`Connected to Classic Cart Studio on this computer (version ${esc(CO.version||'?')}). `)+esc(diagText())+'.';
 case 'booting': return 'Classic Cart Studio is starting…';
 case 'checking': return 'Checking the connection…';
 case 'pairing': return 'Choose Allow in the approval window Classic Cart Studio opened. Your ROM files stay on this computer.';
 case 'web': return 'Anything that needs your computer happens in Classic Cart Studio for Windows.';
 case 'off': return 'Classic Cart Studio is not running. Start it from the Start menu, then check again.';
 case 'unpaired': return 'Classic Cart Studio is running on this computer. Use its window.';
 case 'denied': return 'Your browser blocked this website from reaching apps on this computer. Allow local network access for this site, then reload.';
 case 'old': return `Classic Cart Studio on this computer (version ${esc(CO.version||'?')}) is out of date. Download the latest version and run the installer; your games and settings are kept.`;
 case 'file': return 'This page was opened as a file and cannot reach Classic Cart Studio. Open the website or the Windows app instead.';
 case 'error': return 'Classic Cart Studio reported a problem: '+esc(CO.error||'unknown')+'. Restart it, then check again.';
 default: return 'Checking the connection…'; } }
function connectInline(){ if(CO.hosted&&!CO.desktop) return `<div class="msgbox"><b>Installing on your NES Classic needs Classic Cart Studio for Windows.</b> Your collection comes with you.<div class="acts" style="margin-top:8px"><button class="btn hk" id="hkDesktop">Open in Windows App</button></div></div>`;
 return `<div class="msg">${connSummary()}</div><div class="acts"><span class="sp"></span><button class="btn" id="hkConn">Check Again</button></div>`; }
function wireConnectInline(){ const b=$('hkConn'); if(b) b.onclick=()=>{ $('hkpanel').classList.remove('open'); openConnect(); }; }
// Status of the backend on this computer (desktop app). On the website this opens the desktop invitation instead: the website never connects.
async function openConnect(reason){ if(CO.hosted&&!CO.desktop){ if(typeof desktopInvite==='function'&&desktopInvite(reason)) return; }
 openColl('Connection Status','', `<div class="msgbox">${reason?esc(reason)+'<br>':''}Checking the connection…</div>`); await companionStatus(); renderConnect(reason); }
function renderConnect(reason){ const st=CO.state;
 let h=(reason?`<div class="msgbox warn">${esc(reason)}</div>`:'')+`<div class="msgbox ${st==='ok'?'ok':st==='denied'||st==='error'?'bad':''}"><b>${esc((CONN_TXT[st]||CONN_TXT.unknown)[1]||'Connection status')}.</b> ${connSummary()}</div>`;
 if(st==='old'||st==='error') h+=`<div class="msgbox"><b>Get the latest Classic Cart Studio for Windows:</b> <a href="https://ClassicCartStudio.com/app" target="_blank" rel="noopener">ClassicCartStudio.com/app</a>. Your games and settings are kept.</div>`;
 h+=`<div class="acts"><span class="sp"></span><button class="btn sm" id="connAgain">Check Again</button><button class="btn sm ghost" id="connClose">Close</button></div>`;
 $('collTitle').textContent='Connection Status'; $('collSub').textContent=CO.version&&st==='ok'?'Version '+CO.version:''; $('collBody').innerHTML=h; $('connClose').onclick=closeColl; $('connAgain').onclick=()=>openConnect(); }
// ---- download / release information: release.json published with the website (written from the GitHub release), then the GitHub API as a live fallback ----
let RELEASE=null;
async function releaseInfo(){ if(RELEASE) return RELEASE;
 try{ const r=await (await fetch('release.json',{cache:'no-store'})).json(); if(r&&r.version&&r.asset) RELEASE=r; }catch(e){}
 if(!RELEASE&&!CO.desktop){ try{ const r=await (await fetch(RELEASES_API,{headers:{'Accept':'application/vnd.github+json'}})).json(); if(r&&r.tag_name){ const a=(r.assets||[]).find(x=>/\.exe$/i.test(x.name)), s=(r.assets||[]).find(x=>/^SHA256SUMS/i.test(x.name));
  RELEASE={version:r.tag_name.replace(/^v/,''),tag:r.tag_name,published:r.published_at,url:r.html_url,asset:a?{name:a.name,size:a.size,url:a.browser_download_url}:null,sums_url:s?s.browser_download_url:null,sha256:null,source:'github-api'};
  if(s&&a){ try{ const t=await (await fetch(s.browser_download_url)).text(); const m=t.split(/\r?\n/).find(l=>l.includes(a.name)); if(m) RELEASE.sha256=m.trim().split(/\s+/)[0]; }catch(e){} } } }catch(e){} }
 return RELEASE; }
function renderDownload(el){ if(!el) return; releaseInfo().then(R=>{ if(!el.isConnected) return;
 if(!R||!R.asset){ el.innerHTML=`<b>Download Classic Cart Studio</b><div class="small">${R?'This release has no Windows installer yet.':'Release details could not be loaded.'} See the <a href="${RELEASE_PAGE}" target="_blank" rel="noopener">releases page</a>.</div>`; return; }
 const cmd=`Get-FileHash -Algorithm SHA256 "$env:USERPROFILE\\Downloads\\${R.asset.name}"`;
 el.innerHTML=`<b>Download Classic Cart Studio</b> <span class="small">version ${esc(R.version)}${R.published?' · '+esc(String(R.published).slice(0,10)):''}</span>
  <div class="acts" style="margin:8px 0 6px"><a class="btn hk" href="${esc(R.asset.url)}" rel="noopener">Download Classic Cart Studio</a><span class="small">${esc(R.asset.name)} · ${fmt(R.asset.size||0)} · Windows 10/11 · no administrator rights needed</span></div>
  ${R.sha256?`<div class="acts" style="margin-top:6px"><button class="btn sm ghost" id="dlHow">Check the Download</button></div><div class="small" id="dlHowBox" hidden style="margin-top:8px">SHA-256 of the installer:<div class="hash" id="dlHash">${esc(R.sha256)}</div><div class="acts" style="margin:6px 0"><button class="btn sm" id="dlCopy">Copy SHA-256</button></div>In <b>Windows PowerShell</b>, run:<pre class="hash" style="margin:6px 0">${esc(cmd)}</pre>The <code>Hash</code> it prints must match the value above (letter case does not matter). If it differs, delete the download and get it again.${R.sums_url?` The value is also in <a href="${esc(R.sums_url)}" rel="noopener">SHA256SUMS.txt</a>.`:''}</div>`:`<div class="small">To check the download, see SHA256SUMS.txt on the <a href="${esc(R.url||RELEASE_PAGE)}" target="_blank" rel="noopener">release page</a>.</div>`}
  <div class="small" style="margin-top:8px">The installer is not code-signed, so Windows SmartScreen may ask you to confirm. After installing, start <b>Classic Cart Studio</b> from the Start menu.${R.source==='github-api'?'':` <a href="${esc(R.url||RELEASE_PAGE)}" target="_blank" rel="noopener">Release page</a>.`}</div>`;
 if($('dlCopy')) $('dlCopy').onclick=()=>{ navigator.clipboard&&navigator.clipboard.writeText(R.sha256).then(()=>toast('SHA-256 copied'),()=>toast('Could not copy; select the text instead')); };
 if($('dlHow')) $('dlHow').onclick=()=>{ $('dlHowBox').hidden=!$('dlHowBox').hidden; }; }); }
// ---- Add Games from Files: bulk import through the companion (native picker on this computer, background job, progress, cancel) ----
async function importGames(mode){ if(!CO.ok){ await companionStatus(); if(!CO.ok){ if(typeof desktopInvite==='function'&&desktopInvite('Adding ROM files needs the Windows app, which can read files on this computer.')) return; openConnect('Classic Cart Studio is not responding on this computer.'); return; } }
 openColl('Add Games from Files', mode==='folder'?'Choose a folder in the window that opened':'Choose files in the window that opened', `<div class="msgbox">${mode==='folder'?'Every ROM file and ZIP in the folder and its subfolders is checked.':'Choose ROM files (.nes, .unh, .bin, .fds) or ZIP archives; you can select several.'} Known versions are matched exactly; others become personal games. Your files are copied, never changed.</div>`);
 const r=await hkPost('/api/import/pick',{mode}); if((r.canceled||r.cancelled)){ closeColl(); return; } if(!r.ok){ $('collBody').innerHTML=`<div class="msgbox bad">${esc(r.error||'The import could not start. Try again.')}</div>`; return; }
 await watchImport(r.job); }
function importRows(j){ return (j.items||[]).slice(-120).reverse().map(it=>`<div>${esc(it.file)}${it.member&&it.member!==it.file?' › '+esc(it.member):''} — <span class="${/^error/.test(it.result)?'bad':/^known/.test(it.result)?'okc':'mut'}">${esc(it.result)}</span></div>`).join(''); }
async function watchImport(job){ let j=null; const body=$('collBody');
 const draw=()=>{ const pct=j.total?Math.round(100*j.done/j.total):0; body.innerHTML=`<div class="msgbox"><b>${j.finished?(j.canceled?'Stopped.':'Done.'):'Importing…'}</b> ${j.done} of ${j.total} file${j.total===1?'':'s'}${j.current?' · '+esc(j.current):''}<div class="prog"><i style="width:${pct}%"></i></div>
  <div class="isum"><div class="isb ok"><b>${j.known}</b><span>matched in the Game Library</span></div><div class="isb"><b>${j.personal}</b><span>personal games</span></div><div class="isb"><b>${j.duplicates}</b><span>duplicates skipped</span></div><div class="isb ${j.errors.length?"bad":""}"><b>${j.errors.length}</b><span>could not be read</span></div></div></div>
  ${j.errors.length?`<div class="msgbox bad"><b>Could not be read:</b>${j.errors.slice(0,20).map(e=>`<div>${esc(e.file)}: ${esc(e.error)}</div>`).join('')}${j.errors.length>20?'<div>…</div>':''}</div>`:''}
  <div class="implist">${importRows(j)||'<div class="mut">Nothing checked yet.</div>'}</div>
  <div class="acts">${j.finished?'':'<button class="btn sm ghost" id="impCancel">Stop</button>'}<span class="sp"></span>${j.finished&&j.known?`<button class="btn hk" id="impSelect">Add ${j.known} Matched Game${j.known===1?'':'s'}</button>`:''}<button class="btn sm" id="impClose">${j.finished?'Close':'Run in Background'}</button></div>`;
  if($('impCancel')) $('impCancel').onclick=()=>hkPost('/api/import/cancel',{job}); $('impClose').onclick=closeColl;
  if($('impSelect')) $('impSelect').onclick=()=>{ let n=0; (j.items||[]).forEach(it=>{ if(it.version_id&&byId[it.version_id]&&!sel.has(it.version_id)){ sel.add(it.version_id); n++; } }); persist(); render(); toast(n?`Added ${n} game${n===1?'':'s'} to "${COLL.name}"`:'Those games were already in the collection'); closeColl(); }; };
 for(;;){ const s=await capi('/api/import/status?job='+encodeURIComponent(job)); if(!s.ok){ body.innerHTML=`<div class="msgbox bad">${esc(s.error||'Lost contact with the import. Check your collection, then try again.')}</div>`; return; } j=s.job; if(!$('collm').classList.contains('open')){ if(j.finished) break; await new Promise(r=>setTimeout(r,1500)); continue; } draw(); if(j.finished) break; await new Promise(r=>setTimeout(r,650)); }
 await refreshPersonal(); await companionStatus(); render(); if($('collm').classList.contains('open')) draw(); toast(`Import finished: ${j.known} matched, ${j.personal} personal, ${j.errors.length} error${j.errors.length===1?'':'s'}`); }
async function locateFile(versionId){ const g=byId[versionId]; toast('Choose the ROM file in the window that opened'); const r=await hkPost('/api/library/locate',{version_id:versionId}); if((r.canceled||r.cancelled)) return;
 if(!r.ok){ alert('That file was not used: '+(r.error||'it could not be checked')+(g?'\n\nOnly a file that exactly matches '+(g.nointro_name||g.display_title)+' is accepted.':'')); return; }
 toast('ROM file found for '+(r.name||versionId)); await companionStatus(); if($('hkpanel').classList.contains('open')&&INST.step==='check') renderInstall(); else render(); }
document.body.addEventListener('click',e=>{ const t=e.target.closest&&e.target.closest('[data-locate]'); if(t){ locateFile(t.dataset.locate); } });
$('connPill').onclick=()=>openConnect();
document.addEventListener('visibilitychange',()=>{ if(!document.hidden&&CO.hosted&&CO.state!=='ok') companionStatus(); });
// ---- Studio data export / import: everything the browser keeps for this origin (favorites, snapshots, settings, current collection) moves as a file;
//      this is how data crosses origins (the companion-served page <-> the public website) without any account or login ----
const SDKEY='nes-classic-studio-data/1';
function studioData(){ return {schema:SDKEY, exported:new Date().toISOString(), origin:location.origin, app:'Classic Cart Studio', favorites:[...fav], view:(typeof view==='string'?view:'list'), extended:localStorage.getItem(EKEY)||'', welcomed:!!localStorage.getItem(WKEY), selection:[...sel], decisions:decisions||{}, collection:COLL, snapshots:JSON.parse(localStorage.getItem(SKEY)||'[]')}; }
$('sdExport').onclick=()=>{ const d=studioData(); const b=new Blob([JSON.stringify(d,null,1)],{type:'application/json'}); const a=document.createElement('a'); a.href=URL.createObjectURL(b); a.download='nes-classic-studio-data-'+d.exported.slice(0,10)+'.json'; a.click(); setTimeout(()=>URL.revokeObjectURL(a.href),2000); toast(`Studio data backed up: ${d.favorites.length} favorite${d.favorites.length===1?'':'s'}, ${d.snapshots.length} snapshot${d.snapshots.length===1?'':'s'}`); };
$('sdImport').onclick=()=>$('sdFile').click();
$('sdFile').onchange=e=>{ const f=e.target.files[0]; if(!f) return; f.text().then(t=>{ let d; try{ d=JSON.parse(t); }catch(err){ toast('That file is not a Studio data file'); return; } if(!d||d.schema!==SDKEY||!Array.isArray(d.favorites)){ toast('That file is not a Studio data file'); return; }
 const before=fav.size; (d.favorites||[]).forEach(id=>{ if(byId[id]) fav.add(id); });
 const snaps=JSON.parse(localStorage.getItem(SKEY)||'[]'); const have=new Set(snaps.map(s=>s.at)); let ns=0; (d.snapshots||[]).forEach(s=>{ if(s&&s.at&&!have.has(s.at)){ snaps.push(s); ns++; } }); snaps.sort((a,b)=>a.at<b.at?1:-1); try{ localStorage.setItem(SKEY,JSON.stringify(snaps.slice(0,30))); }catch(err){}
 if(typeof d.extended==='string'&&d.extended){ try{ localStorage.setItem(EKEY,d.extended); }catch(err){} } if(d.welcomed){ try{ localStorage.setItem(WKEY,'1'); }catch(err){} }
 let coll=false; if(Array.isArray(d.selection)&&d.collection&&confirm(`Also replace the current collection with "${(d.collection.name||'My Collection')}" (${d.selection.length} games) from the file?

Your current collection is kept as a snapshot first.`)){ snapshotNow('before importing Studio data'); sel=new Set(d.selection.filter(id=>byId[id])); decisions=Object.assign({},d.decisions||{}); Object.assign(COLL,d.collection,{personalCache:COLL.personalCache}); saveColl(); coll=true; }
 persist(); render(); e.target.value=''; toast(`Studio data restored: ${fav.size-before} new favorite${fav.size-before===1?'':'s'}, ${ns} new snapshot${ns===1?'':'s'}${coll?', collection replaced':''}`); }); };
// ---- startup connection check: runs here, after every declaration above is initialized (an earlier placement in the main script ran before
//      CONN_TXT existed, threw, and left the pill on "Checking for companion…" until the user clicked) ----
companionStatus().then(()=>{ try{ hkLabelUpdate(); }catch(e){} connPill(); }).catch(e=>{ CO.state='error'; CO.error=String(e&&e.message||e); connPill(); });
