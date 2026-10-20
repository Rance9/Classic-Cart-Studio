// ---- Classic Cart Studio collection packages (.ccs) on the website and in the Windows app (format: tools/companion/CCS-FORMAT.md and ccs_package.py;
//      the Windows reader). A ZIP archive: ccs-manifest.json (format, schema, every file with its SHA-256 and size), collection.json (or
//      studio-backup.json), art/ (thumbnails of the collection's own artwork), README.txt and, only in packages made by the Windows app with
//      the sender's explicit acknowledgment, roms/ (game files). This file reads and writes packages in the page itself: nothing is uploaded.
//      The website never reads the game files inside a package (only their names and sizes from the manifest), never stores them and never
//      sends them anywhere but, when the person asks, to Classic Cart Studio on this same computer (http://127.0.0.1, allow-listed origin).
//      Share or Export Collection: one dialog with three choices: a public link (no game files), a .ccs file, a .ccs file with game files.
const CCS_FORMAT='classic-cart-studio-package', CCS_SCHEMA='ccs-package/1', CCS_MANIFEST='ccs-manifest.json';
const CCS_LIM={manifest:8<<20, collection:96<<20, art:1<<20, rom:16<<20, entries:60000, ratio:400};
const CCS_ROM_RE=/^roms\/[A-Za-z0-9_.-]{1,96}\.(nes|unh)$/, CCS_ART_RE=/^art\/[A-Za-z0-9_.-]{1,96}\.(jpg|png)$/;
const CCS_FIXED={'ccs-manifest.json':'manifest','collection.json':'collection','studio-backup.json':'backup','README.txt':'readme'};
const CCS_README='Classic Cart Studio collection package (.ccs)\r\n\r\nThis file holds a game collection made with Classic Cart Studio: its games and exact versions, its NES Classic Loadouts,\r\nHOME settings and artwork (no game files).\r\n\r\nOpen it with Classic Cart Studio for Windows (double-click the file once the app is installed), or on the website:\r\nhttps://classiccartstudio.com  ->  My Collections  ->  Import .ccs Collection\r\n';
function ccsSafeName(n){ if(!n||n.length>200||/^[\/\\]/.test(n)||/[\\:\u0000-\u001f]/.test(n)) return false; if(n.split('/').some(p=>p===''||p==='.'||p==='..')) return false; return n in CCS_FIXED||CCS_ROM_RE.test(n)||CCS_ART_RE.test(n); }
const CCS_CRC=(()=>{ const t=new Uint32Array(256); for(let n=0;n<256;n++){ let c=n; for(let k=0;k<8;k++) c=c&1?0xEDB88320^(c>>>1):c>>>1; t[n]=c>>>0; } return t; })();
function ccsCrc32(u8){ let c=0xFFFFFFFF; for(let i=0;i<u8.length;i++) c=CCS_CRC[(c^u8[i])&255]^(c>>>8); return (c^0xFFFFFFFF)>>>0; }
async function ccsSha(u8){ const h=await crypto.subtle.digest('SHA-256', u8); return [...new Uint8Array(h)].map(b=>b.toString(16).padStart(2,'0')).join(''); }
async function ccsDeflate(u8){ if(typeof CompressionStream==='undefined') return null; try{ return new Uint8Array(await new Response(new Blob([u8]).stream().pipeThrough(new CompressionStream('deflate-raw'))).arrayBuffer()); }catch(e){ return null; } }
async function ccsInflate(u8, limit){ if(typeof DecompressionStream==='undefined') throw new Error('this browser cannot open compressed packages; use a current browser or the Windows app');
 const rd=new Blob([u8]).stream().pipeThrough(new DecompressionStream('deflate-raw')).getReader(); const parts=[]; let n=0;
 for(;;){ const {done, value}=await rd.read(); if(done) break; n+=value.length; if(n>limit){ try{ rd.cancel(); }catch(e){} throw new Error('more data than declared'); } parts.push(value); }
 const o=new Uint8Array(n); let p=0; for(const x of parts){ o.set(x,p); p+=x.length; } return o; }
// ---- ZIP writing (stored or deflate entries, UTF-8 names) ----
async function ccsZip(files){ const enc=new TextEncoder(); const now=new Date(); const dt=((now.getHours()<<11)|(now.getMinutes()<<5)|(now.getSeconds()>>1))&0xFFFF, dd=(((now.getFullYear()-1980)<<9)|((now.getMonth()+1)<<5)|now.getDate())&0xFFFF;
 const parts=[], cd=[]; let off=0;
 for(const f of files){ const name=enc.encode(f.name); const crc=ccsCrc32(f.data); let method=0, body=f.data; if(f.data.length>512){ const d=await ccsDeflate(f.data); if(d&&d.length<f.data.length){ method=8; body=d; } }
  const lh=new DataView(new ArrayBuffer(30)); lh.setUint32(0,0x04034b50,true); lh.setUint16(4,20,true); lh.setUint16(6,0x0800,true); lh.setUint16(8,method,true); lh.setUint16(10,dt,true); lh.setUint16(12,dd,true); lh.setUint32(14,crc,true); lh.setUint32(18,body.length,true); lh.setUint32(22,f.data.length,true); lh.setUint16(26,name.length,true);
  parts.push(new Uint8Array(lh.buffer), name, body);
  const ch=new DataView(new ArrayBuffer(46)); ch.setUint32(0,0x02014b50,true); ch.setUint16(4,20,true); ch.setUint16(6,20,true); ch.setUint16(8,0x0800,true); ch.setUint16(10,method,true); ch.setUint16(12,dt,true); ch.setUint16(14,dd,true); ch.setUint32(16,crc,true); ch.setUint32(20,body.length,true); ch.setUint32(24,f.data.length,true); ch.setUint16(28,name.length,true); ch.setUint32(42,off,true);
  cd.push(new Uint8Array(ch.buffer), name); off+=30+name.length+body.length; }
 const cdSize=cd.reduce((a,b)=>a+b.length,0); const e=new DataView(new ArrayBuffer(22)); e.setUint32(0,0x06054b50,true); e.setUint16(8,files.length,true); e.setUint16(10,files.length,true); e.setUint32(12,cdSize,true); e.setUint32(16,off,true);
 return new Blob([...parts, ...cd, new Uint8Array(e.buffer)], {type:'application/vnd.classiccartstudio.collection+zip'}); }
// ---- ZIP reading: only the central directory and the entries asked for are read (a package's game files are never loaded here) ----
async function ccsUnzip(blob){ const size=blob.size; if(size<22) throw new Error('not a valid archive'); const tl=Math.min(size,65557); const tail=new Uint8Array(await blob.slice(size-tl).arrayBuffer()); let e=-1;
 for(let i=tail.length-22;i>=0;i--){ if(tail[i]===0x50&&tail[i+1]===0x4b&&tail[i+2]===5&&tail[i+3]===6){ e=i; break; } } if(e<0) throw new Error('not a valid archive');
 const ev=new DataView(tail.buffer, tail.byteOffset+e); const n=ev.getUint16(10,true), cdSize=ev.getUint32(12,true), cdOff=ev.getUint32(16,true);
 if(n===0xFFFF||cdOff===0xFFFFFFFF||cdSize===0xFFFFFFFF) throw new Error('this package uses a format this version cannot open (ZIP64)'); if(cdOff+cdSize>size) throw new Error('not a valid archive');
 const cdb=new Uint8Array(await blob.slice(cdOff, cdOff+cdSize).arrayBuffer()); const v=new DataView(cdb.buffer); const dec=new TextDecoder('utf-8',{fatal:false}); const entries=[]; let p=0;
 for(let k=0;k<n;k++){ if(p+46>cdb.length||v.getUint32(p,true)!==0x02014b50) throw new Error('not a valid archive'); const nl=v.getUint16(p+28,true), xl=v.getUint16(p+30,true), cl=v.getUint16(p+32,true);
  entries.push({name:dec.decode(cdb.subarray(p+46,p+46+nl)), flags:v.getUint16(p+8,true), method:v.getUint16(p+10,true), csize:v.getUint32(p+20,true), usize:v.getUint32(p+24,true), ext:v.getUint32(p+38,true), lho:v.getUint32(p+42,true)}); p+=46+nl+xl+cl; }
 return {size, entries, async read(en, limit){ if(en.usize>limit) throw new Error(en.name+': larger than allowed'); const lh=new DataView(await blob.slice(en.lho, en.lho+30).arrayBuffer()); if(lh.getUint32(0,true)!==0x04034b50) throw new Error('not a valid archive');
  const st=en.lho+30+lh.getUint16(26,true)+lh.getUint16(28,true); const raw=new Uint8Array(await blob.slice(st, st+en.csize).arrayBuffer()); const d=en.method===0?raw:await ccsInflate(raw, Math.min(limit, en.usize)); if(d.length!==en.usize) throw new Error(en.name+': size differs'); return d; } }; }
// ---- validate and describe a package (the same rules as the Windows app's reader) ----
async function ccsInspect(blob){ let z; try{ z=await ccsUnzip(blob); }catch(e){ throw new Error(/ZIP64/.test(e.message)?e.message:'This is not a Classic Cart Studio package (not a valid archive).'); }
 const E=z.entries; if(E.length>CCS_LIM.entries) throw new Error('The package has too many entries.'); const names=E.map(x=>x.name); if(new Set(names).size!==names.length) throw new Error('The package has duplicate entries.');
 for(const x of E){ if(x.name.endsWith('/')) throw new Error('The package contains an unexpected folder entry.'); if(!ccsSafeName(x.name)) throw new Error(`The package contains an unsafe or unexpected entry (${x.name.slice(0,60)}).`);
  if(((x.ext>>>16)&0xF000)===0xA000) throw new Error('The package contains a link, which is not allowed.'); if(x.flags&1) throw new Error('Encrypted packages are not supported.'); if(x.method!==0&&x.method!==8) throw new Error('The package uses an unsupported compression method.');
  if(x.csize&&x.usize/Math.max(1,x.csize)>CCS_LIM.ratio) throw new Error('The package contains an entry with a suspicious compression ratio.'); }
 const by=Object.fromEntries(E.map(x=>[x.name,x])); if(!by[CCS_MANIFEST]) throw new Error('This is not a Classic Cart Studio package (no ccs-manifest.json).');
 let man; try{ man=JSON.parse(new TextDecoder().decode(await z.read(by[CCS_MANIFEST], CCS_LIM.manifest))); }catch(e){ throw new Error('The package manifest is not valid.'); }
 if(!man||man.format!==CCS_FORMAT) throw new Error('This is not a Classic Cart Studio package.'); if(man.schema!==CCS_SCHEMA) throw new Error('This package was made by a newer Classic Cart Studio; update to open it.');
 if(!Array.isArray(man.files)) throw new Error('The package manifest is not valid.'); const lp=man.files.map(f=>f&&f.path); if(new Set(lp).size!==lp.length) throw new Error('The package manifest lists a file twice.');
 const extra=names.filter(x=>x!==CCS_MANIFEST&&!lp.includes(x)); if(extra.length) throw new Error('The package contains files its manifest does not list.');
 const problems=[], roms=[], art={}; let main=null;
 for(const f of man.files){ const p=f&&f.path; if(typeof p!=='string'||!ccsSafeName(p)||p===CCS_MANIFEST) throw new Error('The package manifest lists an unsafe file name.'); if(!/^[0-9a-f]{64}$/.test(f.sha256||'')||!Number.isInteger(f.size)||f.size<0) throw new Error('The package manifest has an invalid checksum or size.');
  const x=by[p]; if(!x){ problems.push({path:p, problem:'listed in the manifest but missing from the package'}); continue; } if(x.usize!==f.size){ problems.push({path:p, problem:'size differs from the manifest'}); continue; }
  if(p.startsWith('roms/')){ roms.push({path:p, version_id:String(f.version_id||''), title:String(f.title||''), size:f.size, sha256:f.sha256}); continue; }   // never read here
  const d=await z.read(x, p.startsWith('art/')?CCS_LIM.art:CCS_LIM.collection); if(await ccsSha(d)!==f.sha256){ problems.push({path:p, problem:'checksum does not match (damaged or altered)'}); continue; }
  if(p.startsWith('art/')){ const id=p.slice(4).replace(/\.(jpg|png)$/,''); let s=''; for(let i=0;i<d.length;i+=0x8000) s+=String.fromCharCode.apply(null, d.subarray(i,i+0x8000)); art[id]=`data:image/${p.endsWith('.png')?'png':'jpeg'};base64,`+btoa(s); }
  else if(p==='collection.json'||p==='studio-backup.json'){ try{ main={kind:p==='collection.json'?'collection':'backup', data:JSON.parse(new TextDecoder().decode(d))}; }catch(e){ throw new Error('The collection inside the package is not valid.'); } } }
 if(!main) throw new Error('The package holds no collection.'); if(man.kind!==main.kind) throw new Error('The package manifest does not match its contents.');
 return {manifest:man, kind:main.kind, data:main.data, art, roms, missingRoms:((man.roms||{}).missing)||[], problems, size:z.size, ok:!problems.length, romBytes:roms.reduce((a,r)=>a+r.size,0)}; }
async function ccsMake(kind, payload, art, summary){ const enc=new TextEncoder(); const files=[], listed=[]; const main=kind==='collection'?'collection.json':'studio-backup.json'; const body=enc.encode(JSON.stringify(payload));
 files.push({name:main, data:body}); listed.push({path:main, sha256:await ccsSha(body), size:body.length, kind});
 for(const [n,u8] of Object.entries(art||{})){ const p='art/'+n; if(!CCS_ART_RE.test(p)||u8.length>CCS_LIM.art) continue; files.push({name:p, data:u8}); listed.push({path:p, sha256:await ccsSha(u8), size:u8.length, kind:'art'}); }
 const rd=enc.encode(CCS_README); files.push({name:'README.txt', data:rd}); listed.push({path:'README.txt', sha256:await ccsSha(rd), size:rd.length, kind:'readme'});
 const man={format:CCS_FORMAT, schema:CCS_SCHEMA, kind, created:new Date().toISOString().replace(/\.\d+Z$/,'Z'), app:{name:'Classic Cart Studio', version:(typeof COMPANION!=='undefined'&&COMPANION.version)||''}, summary:summary||{}, files:listed, roms:{included:[], missing:[]}};
 files.push({name:CCS_MANIFEST, data:enc.encode(JSON.stringify(man,null,1))}); return ccsZip(files); }
// ---- what a collection's package holds: its file (every game, loadouts, HOME settings) and thumbnails of its own artwork ----
async function ccsCollectionParts(r){ const file=csFileFor(r); const games=csWithRecord(r, ()=>shGames()); const art={}, artB64={};
 for(const g of games){ if(!g.own_art||!/^[A-Za-z0-9_.-]{1,96}$/.test(g.id)) continue; const t=await shThumb(g.own_art); if(t){ artB64[g.id+'.jpg']=t; const bin=atob(t); const u=new Uint8Array(bin.length); for(let i=0;i<bin.length;i++) u[i]=bin.charCodeAt(i); art[g.id+'.jpg']=u; } }
 const summary={name:r.name, games:(file.selections||[]).length, loadouts:((file.ccs_collection||{}).loadouts||[]).length, personal:games.filter(g=>g.source==='personal').length};
 return {file, games, art, artB64, summary}; }
function ccsAppPackages(){ return !!(typeof CO!=='undefined'&&CO.desktop&&CO.ok&&((COMPANION.info&&COMPANION.info.capabilities)||COMPANION.capabilities||[]).includes('ccs-package')); }
function ccsName(r){ return csSafeName(r.name||'Classic Cart Studio Collection','.ccs'); }
// ---- the modal for game-file acknowledgments: centered, blurred and dimmed background, focus kept inside, nothing behind reachable ----
function ccsAck(o){ return new Promise(res=>{ let m=$('ccsAck'); if(!m){ m=document.createElement('div'); m.id='ccsAck'; m.className='ccsack'; m.setAttribute('role','dialog'); m.setAttribute('aria-modal','true'); m.setAttribute('aria-labelledby','ccsAckT'); document.body.appendChild(m); }
  m.innerHTML=`<div class="mbox ccsackbox"><h2 id="ccsAckT">${esc(o.title)}</h2>${o.body.map(p=>`<p>${esc(p)}</p>`).join('')}<label class="ccsackchk"><input type="checkbox" id="ccsAckC"> <span>${esc(o.check)}</span></label>
   <div class="acts">${o.actions.map((a,i)=>`${i===1&&o.actions.length>2?'':''}<button class="btn ${a.primary?'hk':a.secondary?'secondary':''}" data-i="${i}" ${a.needsAck?'disabled':''}>${esc(a.label)}</button>`).join('')}</div></div>`;
  const others=[...document.body.children].filter(x=>x!==m&&!x.hasAttribute('inert')); others.forEach(x=>x.setAttribute('inert','')); m.classList.add('open');
  const btns=[...m.querySelectorAll('button')]; const chk=$('ccsAckC'); chk.checked=false; chk.focus();
  chk.onchange=()=>btns.forEach((b,i)=>{ if(o.actions[i].needsAck) b.disabled=!chk.checked; });
  const done=v=>{ m.classList.remove('open'); others.forEach(x=>x.removeAttribute('inert')); m.onkeydown=null; res(v); };
  btns.forEach((b,i)=>b.onclick=()=>{ if(b.disabled) return; done(o.actions[i].value); });
  m.onkeydown=e=>{ if(e.key==='Escape'){ e.preventDefault(); done('cancel'); return; } if(e.key==='Tab'){ const f=[chk,...btns.filter(b=>!b.disabled)]; const i=f.indexOf(document.activeElement); e.preventDefault(); (f[(i+(e.shiftKey?-1:1)+f.length)%f.length]).focus(); } }; }); }
const CCS_EXPORT_ACK={title:'Export Collection with ROM Files', body:['You are about to create a Classic Cart Studio collection package containing ROM files from your computer.','Classic Cart Studio does not provide or host these ROM files.','You are responsible for ensuring that you have the necessary rights to copy and distribute the ROM files included in this package.','Owning an original game cartridge or possessing a ROM copy does not necessarily grant permission to distribute it.'],
 check:'I confirm that I have the necessary rights to distribute the ROM files included in this package.', actions:[{label:'Cancel', value:'cancel', secondary:true},{label:'Export with ROMs', value:'export', primary:true, needsAck:true}]};
const CCS_IMPORT_ACK={title:'Import Collection with ROM Files', body:['This Classic Cart Studio collection package contains ROM files supplied by another user.','Classic Cart Studio does not provide or host these ROM files.','You are responsible for ensuring that you are legally permitted to receive and use the ROM files included in this package.','You may also import the collection without its ROM files.'],
 check:'I understand and accept responsibility for the lawful receipt and use of the included ROM files.', actions:[{label:'Cancel', value:'cancel', secondary:true},{label:'Import Without ROMs', value:'without'},{label:'Import Collection with ROMs', value:'with', primary:true, needsAck:true}]};
// ---- Share or Export Collection ----
function ccsShareDialog(cid){ csSaveActiveNow(); cid=cid||CS.active; const r=CS.items[cid]; if(!r) return; document.querySelectorAll('.menu').forEach(x=>x.hidden=true); const n=(cid===CS.active?csCollSel().size:(r.sel||[]).length); const nl=(r.loadouts||[]).length; const app=ccsAppPackages();
 const opt=(v,t,d,dis,note)=>`<label class="lc${dis?' off':''}"><input type="radio" name="ccsOpt" value="${v}" ${dis?'disabled':''}><span><b>${t}</b><span class="small">${d}${note?`<br><i>${note}</i>`:''}</span></span></label>`;
 openColl('Share or Export Collection', `"${r.name}" · ${n} game${n===1?'':'s'}${nl?` · ${nl} NES Classic Loadout${nl===1?'':'s'}`:''}`, `<div class="lochoice" role="radiogroup" aria-label="How to share">
   ${SHARE_ON?opt('link','Share Collection Link','A public page anyone with the link can browse, save to their own collections or open in Classic Cart Studio. Every game, version, loadout and its artwork; no game files.'):''}
   ${opt('ccs','Export Collection (.ccs)','A file to keep or pass on yourself: every game, version, loadout, HOME setting and your own artwork. No game files. Opens on ClassicCartStudio.com and in Classic Cart Studio for Windows.')}
   ${opt('roms','Export with ROMs (.ccs)','The same file, plus the game files on this computer that you choose to include. Made on this computer only; you decide how to pass it on. Classic Cart Studio never uploads it.', !app, app?'':'Game files are only on your computer in Classic Cart Studio for Windows.')}
  </div><div class="acts"><span class="sp"></span><button class="btn" id="ccsNo">Cancel</button><button class="btn hk" id="ccsGo">Continue</button></div>`);
 const first=document.querySelector('input[name=ccsOpt]:not([disabled])'); if(first){ first.checked=true; first.focus(); }
 $('ccsNo').onclick=closeColl;
 $('ccsGo').onclick=async()=>{ const v=(document.querySelector('input[name=ccsOpt]:checked')||{}).value; if(!v) return; closeColl();
  if(v==='link'){ if(cid!==CS.active) csSwitch(cid, true); if(LO) loClose(true); openShare(); return; }
  if(v==='ccs'){ await ccsExport(cid); return; }
  if(v==='roms'){ const a=await ccsAck(CCS_EXPORT_ACK); if(a==='export') await ccsExportWithRoms(cid); } }; }
async function ccsExport(cid){ const r=CS.items[cid]; if(!r) return; toast('Preparing the package…');
 try{ const p=await ccsCollectionParts(r); const blob=await ccsMake('collection', p.file, p.art, p.summary); const res=await ccsSaveFile(ccsName(r), blob, 'Classic Cart Studio Collection');
  if(res.ok&&cid===CS.active&&!LO){ COLL.savedToFile={at:new Date().toISOString(), fp:collFingerprint()}; saveColl(); updateStatusLine(); }
  if(res.ok) toast(`Exported "${r.name}" (${p.summary.games} game${p.summary.games===1?'':'s'}${p.summary.loadouts?`, ${p.summary.loadouts} loadout${p.summary.loadouts===1?'':'s'}`:''}, ${fmt(blob.size)}) to ${res.name}`); }
 catch(e){ openColl('Export Collection','', `<div class="msgbox bad">The package could not be made: ${esc(e.message||String(e))}. Your collection is unchanged.</div><div class="acts"><span class="sp"></span><button class="btn" id="ccsOk">OK</button></div>`); $('ccsOk').onclick=closeColl; } }
async function ccsExportWithRoms(cid){ const r=CS.items[cid]; if(!r) return; openColl('Export Collection with ROM Files', `"${r.name}"`, '<div class="msgbox">Looking for the game files on this computer…</div>');
 const p=await ccsCollectionParts(r); const games=(p.file.selections||[]).map(s=>({version_id:s.version_id, title:s.display_title||s.title||s.version_id}));
 const chk=await hkPost('/api/package/rom-check', {games}); if(!chk.ok){ $('collBody').innerHTML=`<div class="msgbox bad">${esc(chk.error||'The game files could not be checked.')}</div>`; return; }
 const bytes=chk.available.reduce((a,g)=>a+(g.size||0),0); let cancelled=false;
 $('collBody').innerHTML=`<div class="msgbox ok"><b>${chk.available.length} game file${chk.available.length===1?'':'s'}</b> can be included (${fmt(bytes)}).</div>${chk.missing.length?`<div class="msgbox warn"><b>${chk.missing.length} game file${chk.missing.length===1?' is':'s are'} not on this computer</b> and will not be in the package; ${chk.missing.length===1?'its game stays':'their games stay'} in the collection and ${chk.missing.length===1?'is':'are'} listed in the package as not included.<div class="small">${esc(chk.missing.slice(0,20).map(g=>g.title).join(', '))}${chk.missing.length>20?' …':''}</div></div>`:''}
  <p class="small">The package is made on this computer. Classic Cart Studio does not upload it; you choose where to save it and how to pass it on.</p><div class="acts"><span class="sp"></span><button class="btn" id="ccsNo">Cancel</button><button class="btn hk" id="ccsGo" ${chk.available.length?'':'disabled'}>Create Package</button></div>`;
 $('ccsNo').onclick=()=>{ cancelled=true; closeColl(); };
 $('ccsGo').onclick=async()=>{ $('ccsGo').disabled=true; $('ccsGo').textContent='Creating…'; $('ccsNo').onclick=()=>{ cancelled=true; closeColl(); toast('Export cancelled: nothing was saved'); };
  const ex=await hkPost('/api/package/export', {collection:p.file, art:p.artB64, include_roms:true, roms:games, summary:Object.assign({}, p.summary, {roms:chk.available.length})});
  if(cancelled) return; if(!ex.ok){ $('collBody').innerHTML=`<div class="msgbox bad">The package could not be made: ${esc(ex.error||'unknown error')}. Nothing was saved.</div>`; return; }
  let blob; try{ const rr=await fetch(CO.base+'/api/package/file?id='+encodeURIComponent(ex.id), lnaInit({headers:hdrs(), cache:'no-store'})); if(!rr.ok) throw new Error('HTTP '+rr.status); blob=await rr.blob(); }catch(e){ $('collBody').innerHTML=`<div class="msgbox bad">The package could not be read back (${esc(String(e.message||e))}). Nothing was saved.</div>`; return; }
  if(cancelled) return; closeColl(); const res=await ccsSaveFile(ccsName(r), blob, 'Classic Cart Studio Collection');
  if(res.ok) toast(`Exported "${r.name}" with ${ex.included.length} game file${ex.included.length===1?'':'s'} (${fmt(blob.size)}) to ${res.name}`); else toast('Export cancelled: nothing was saved'); }; }
// ---- Import .ccs Collection ----
function ccsImportPick(){ const inp=document.createElement('input'); inp.type='file'; inp.accept='.ccs,application/json,.json'; inp.onchange=()=>{ const f=inp.files[0]; if(f) ccsOpenFile(f); }; inp.click(); }
async function ccsOpenFile(f){ if(/\.json$/i.test(f.name)){ const t=await f.text(); let o; try{ o=JSON.parse(t); }catch(e){ toast('That file is not a collection file'); return; } csImportObject(o, f.name); return; }
 openColl('Import .ccs Collection', f.name, '<div class="msgbox">Checking the package…</div>');
 let info; try{ info=await ccsInspect(f); }catch(e){ ccsFail(f.name, e.message); return; }
 if(CO.desktop&&info.roms.length&&ccsAppPackages()){   // the app verifies the game files too: the package is staged in its own folder first
  const up=await fetch(CO.base+'/api/package/stage', lnaInit({method:'POST', headers:hdrs({'Content-Type':'application/octet-stream'}), body:f, cache:'no-store'})).then(r=>r.json()).catch(e=>({ok:false, error:String(e)}));
  if(!up.ok){ ccsFail(f.name, up.error||'the package could not be checked'); return; } return ccsPreview(ccsFromStaged(up), f.name); }
 ccsPreview(Object.assign(info, {source:'file', file:f}), f.name); }
function ccsFromStaged(d){ return {manifest:{created:d.created}, kind:d.kind, data:d.data, art:{}, roms:d.roms||[], missingRoms:d.missing_roms||[], problems:d.problems||[], size:d.size, ok:!(d.problems||[]).length, romBytes:d.rom_bytes||0, source:'staged', id:d.id}; }
function ccsFail(name, why){ openColl('Import .ccs Collection', name||'', `<div class="msgbox bad"><b>This package cannot be opened.</b> ${esc(why||'')}</div><p class="small">Nothing was imported; your collections are unchanged.</p><div class="acts"><span class="sp"></span><button class="btn" id="ccsOk">OK</button></div>`); $('ccsOk').onclick=closeColl; }
function ccsCounts(info){ if(info.kind==='backup'){ const c=(info.data&&info.data.collections)||[]; return {name:'Studio data backup', games:c.reduce((a,x)=>a+((x.sel||[]).length),0), loadouts:c.reduce((a,x)=>a+((x.loadouts||[]).length),0), collections:c.length}; }
 const rec=(info.data&&info.data.ccs_collection)||{}; return {name:rec.name||info.data.name||'Imported collection', games:(rec.sel||[]).length||(info.data.selections||[]).length, loadouts:(rec.loadouts||[]).length, collections:1}; }
function ccsPreview(info, fname){ const c=ccsCounts(info); const roms=info.roms.length; const web=!CO.desktop; const canRoms=!web&&ccsAppPackages()&&info.source==='staged';
 const unverified=info.roms.filter(r=>r.verified===false).length; const bad=info.problems||[];
 const rows=[['Collection',esc(c.name)],[info.kind==='backup'?'Collections':'Games', info.kind==='backup'?`${c.collections} (${c.games} games)`:String(c.games)],['NES Classic Loadouts',String(c.loadouts)],['ROM files included', roms?`${roms} (${fmt(info.romBytes)})`:'none'],['Package size', fmt(info.size)]];
 openColl(info.kind==='backup'?'Restore Studio Data':'Import .ccs Collection', fname||'', `<dl class="kv">${rows.map(([k,v])=>`<dt>${k}</dt><dd>${v}</dd>`).join('')}</dl>
  ${bad.length?`<div class="msgbox bad"><b>Integrity problems</b> (these parts are not used):<ul>${bad.slice(0,8).map(p=>`<li>${esc(p.path)}: ${esc(p.problem)}</li>`).join('')}</ul></div>`:''}
  ${info.missingRoms.length?`<div class="msgbox warn">${info.missingRoms.length} game file${info.missingRoms.length===1?' was':'s were'} not included by the sender; ${info.missingRoms.length===1?'its game stays':'those games stay'} in the collection without a file.</div>`:''}
  ${roms&&web?`<div class="msgbox">This package contains ROM files. The website uses only the collection, its loadouts and artwork: the ROM files are not read, kept or sent anywhere. To use them, open the complete package in Classic Cart Studio for Windows.</div>`:''}
  <p class="small">${info.kind==='backup'?'Collections from the backup are added; nothing here is replaced.':'The collection is added as a new, independent collection; nothing here is replaced or merged.'}</p>
  <div class="acts">${web&&roms?`<button class="btn" id="ccsWin">Open Complete Package in Windows App</button>`:''}<span class="sp"></span><button class="btn" id="ccsNo">Cancel</button><button class="btn hk" id="ccsGo">${info.kind==='backup'?'Restore…':web?(roms?'Save Collection Without ROMs':'Save to My Collections'):(roms&&canRoms?'Import…':'Import Collection')}</button></div>`);
 $('ccsNo').onclick=closeColl; if($('ccsWin')) $('ccsWin').onclick=()=>ccsToApp(info.file, fname);
 $('ccsGo').onclick=async()=>{ if(info.kind==='backup'){ closeColl(); csRestoreDialog(info.data, fname); return; }
  if(roms&&canRoms){ const a=await ccsAck(CCS_IMPORT_ACK); if(a==='cancel') return; if(a==='with'){ const ok=await ccsImportRoms(info); if(!ok) return; } }
  closeColl(); const r=ccsSaveCollection(info); if(!r) return;
  openColl('Collection imported', '', `<div class="msgbox ok">"${esc(r.name)}" (${(r.sel||[]).length} game${(r.sel||[]).length===1?'':'s'}${(r.loadouts||[]).length?`, ${(r.loadouts||[]).length} NES Classic Loadout${(r.loadouts||[]).length===1?'':'s'}`:''}) is now one of your collections.${info._romResult?` ${info._romResult.imported.length} ROM file${info._romResult.imported.length===1?' was':'s were'} added to this computer's game files.`:''}</div>${info._romResult&&info._romResult.rejected.length?`<div class="msgbox warn">${info._romResult.rejected.length} file${info._romResult.rejected.length===1?' was':'s were'} not used: ${esc(info._romResult.rejected.map(x=>x.reason).join('; '))}</div>`:''}<div class="acts"><button class="btn ghost" id="ccsStay">Keep my current collection open</button><span class="sp"></span><button class="btn hk" id="ccsOpen">Open it now</button></div>`);
  $('ccsStay').onclick=closeColl; $('ccsOpen').onclick=()=>{ closeColl(); csSwitch(r.id); }; }; }
async function ccsImportRoms(info){ $('collBody').insertAdjacentHTML('beforeend','<div class="msgbox" id="ccsBusy">Checking and adding the ROM files…</div>');
 const r=await hkPost('/api/package/import-roms', {id:info.id}); const b=$('ccsBusy'); if(b) b.remove();
 if(!r.ok){ $('collBody').insertAdjacentHTML('beforeend', `<div class="msgbox bad">The ROM files were not imported: ${esc(r.error||'unknown error')}. Nothing was changed; you can still import the collection without them.</div>`); return false; }
 info._romResult=r; await refreshPersonal(); return true; }
// a new, independent collection from a package (every entry kept; artwork of games not on this computer kept with their entries)
function ccsSaveCollection(info){ const d=info.data||{}; const rec0=d.ccs_collection; if(!rec0||!Array.isArray(rec0.sel)){ csImportObject(d, 'package'); return csActive(); }
 const {rec}=csRecordFromFile(rec0, rec0.name); const sels=Object.fromEntries((d.selections||[]).map(s=>[s.version_id||s.game_id, s]));
 rec.sel.forEach(id=>{ if(byId[id]) { delete rec.coll.missing[id]; return; } const s=sels[id]; const m=rec.coll.missing[id]; if(s&&(!m||m.title===id)) rec.coll.missing[id]=csEntryFromSelection(s, d.personal_library); if(info.art&&info.art[id]&&rec.coll.missing[id]) rec.coll.missing[id].thumb_url=info.art[id]; });
 return csCreate(rec.name, rec); }
// the website hands a ROM package to Classic Cart Studio on this computer (local only, allow-listed origin), then opens the app on it
async function ccsToApp(file, fname){ if(!file){ ccsAppFallback(fname); return; }
 const post=()=>fetch(CO.base+'/api/handoff/package', lnaInit({method:'POST', headers:{'Content-Type':'application/octet-stream','X-NES-Protocol':String(CO.REQ_PROTOCOL)}, body:file, cache:'no-store'})).then(r=>r.json()).catch(()=>({ok:false, unreachable:true}));
 openColl('Open Complete Package in Windows App', fname||'', '<div class="msgbox">Looking for Classic Cart Studio on this computer…</div>');
 let r=await post();
 if(r.unreachable){ ccsLaunch('classiccartstudio://open'); $('collBody').innerHTML='<div class="msgbox">Opening Classic Cart Studio… If it is installed, your browser asks whether to open it; choose <b>Open</b>. Waiting for the app (up to a minute).</div>';
  for(let i=0;i<(window.CCS_WAIT_TRIES||20)&&r.unreachable;i++){ await new Promise(s=>setTimeout(s, 3000)); if(!$('collm').classList.contains('open')) return; r=await post(); } }
 if(!r.ok){ ccsAppFallback(fname, r.unreachable?'':r.error); return; }
 ccsLaunch('classiccartstudio://package?offer='+encodeURIComponent(r.id));
 $('collBody').innerHTML=`<div class="msgbox ok"><b>The package was handed to Classic Cart Studio on this computer.</b> Switch to its window: it shows the package and asks before anything is imported. The ROM files went only to the app on this computer.</div><div class="acts"><span class="sp"></span><button class="btn" id="ccsOk">Done</button></div>`; $('ccsOk').onclick=closeColl; }
function ccsAppFallback(fname, err){ openColl('Open the package in Classic Cart Studio', fname||'', `<div class="msgbox warn">${err?esc(err)+'. ':''}Classic Cart Studio for Windows did not answer on this computer.</div><ol class="small"><li>Install Classic Cart Studio (below) if you have not yet, and start it.</li><li>Then double-click <b>${esc(fname||'the .ccs file')}</b> in File Explorer (or come back here and choose <b>Open Complete Package in Windows App</b> again).</li></ol><div class="msgbox" id="dl"><b>Download Classic Cart Studio</b><div class="small">Looking up the latest release…</div></div><div class="acts"><span class="sp"></span><button class="btn" id="ccsOk">Close</button></div>`); if(typeof renderDownload==='function') renderDownload($('dl')); $('ccsOk').onclick=closeColl; }
function ccsLaunch(u){ let f=$('ccsLaunch'); if(!f){ f=document.createElement('iframe'); f.id='ccsLaunch'; f.setAttribute('aria-hidden','true'); f.style.cssText='display:none;width:0;height:0;border:0'; document.body.appendChild(f); } try{ f.src=u; }catch(e){} }
// ---- Back Up Studio Data and Restore: .ccs as well ----
csBackup=async function(){ const d=csBackupObject(); const blob=await ccsMake('backup', d, {}, {collections:d.collections.length}); const res=await ccsSaveFile(`Classic Cart Studio Data Backup ${csStamp()}.ccs`, blob, 'Studio Data Backup'); if(res.ok) toast(`Backed up ${d.collections.length} collection${d.collections.length===1?'':'s'}, ${d.favorites.length} favorites and ${d.snapshots.length} snapshots to ${res.name}`); };
csRestorePick=function(){ ccsImportPick(); };
csImportPick=function(){ ccsImportPick(); };
csExport=function(id){ return ccsExport(id||CS.active); };
// ---- links and files handed over by Windows (protocol, file association) and by the website ----
window.__ccsLink=function(kind, value){ const go=()=>{ if(kind==='share'&&/^[A-Za-z0-9]{6,32}$/.test(value)){ if(typeof shReceive==='function') shareConfig().then(()=>shReceive(value)); }
  else if(kind==='package'&&/^[A-Za-z0-9_-]{16,40}$/.test(value)){ capi('/api/package/preview?id='+encodeURIComponent(value)).then(d=>d.ok?ccsPreview(ccsFromStaged(d), 'from ClassicCartStudio.com'):ccsFail('', d.error)); }
  else if(kind==='file'&&/\.ccs$/i.test(value)){ openColl('Import .ccs Collection', value.split(/[\\/]/).pop(), '<div class="msgbox">Checking the package…</div>'); hkPost('/api/package/open', {path:value}).then(d=>d.ok?ccsPreview(ccsFromStaged(d), d.file_name):ccsFail(value.split(/[\\/]/).pop(), d.error)); } };
 if(CO.ok) go(); else { let n=0; const t=setInterval(()=>{ if(CO.ok||++n>120){ clearInterval(t); if(CO.ok) go(); } }, 500); } };
(window.__ccsLinkQ||[]).forEach(x=>window.__ccsLink(x[0], x[1])); window.__ccsLinkQ=[];
// drag and drop a .ccs onto the page
document.addEventListener('dragover', e=>{ if([...(e.dataTransfer&&e.dataTransfer.items||[])].some(i=>i.kind==='file')){ e.preventDefault(); document.body.classList.add('ccsdrop'); } });
document.addEventListener('dragleave', e=>{ if(!e.relatedTarget) document.body.classList.remove('ccsdrop'); });
document.addEventListener('drop', e=>{ const f=e.dataTransfer&&e.dataTransfer.files&&e.dataTransfer.files[0]; document.body.classList.remove('ccsdrop'); if(f&&/\.(ccs|json)$/i.test(f.name)){ e.preventDefault(); ccsOpenFile(f); } });
