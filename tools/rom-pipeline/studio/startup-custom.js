// ---- your own console startup picture: HOME Settings › Startup screen › Choose Image…. One picture per collection, made on this
//      device from a PNG, JPEG or WebP file the person picks (the file is only read: never changed, uploaded or kept): turned upright as
//      the file says, framed 16:9 (Fit: the whole picture on black; Fill: the screen filled, the edges cut evenly), transparency on black,
//      saved as the 1280×720 JPEG the console shows, with no metadata. Its bytes are kept in this browser's own storage (IndexedDB, under
//      their SHA-256), not in the collection records (those live in the small localStorage); the collection keeps a short reference
//      (home.startup_custom) and its .ccs and Full Studio Backup files and the console package carry the bytes themselves (csRecordOut,
//      nhHome), so the picture travels with the collection. Random stays the five Classic Cart Studio pictures; the person's own picture,
//      when chosen, is shown every time. A picture no collection uses any more is removed from this browser's storage (never a file).
const SU_LIM={file:25e6, pixels:50e6, side:12000, minW:320, minH:180, out:450e3, b64:620e3};   // a picked file; its decoded size; the saved picture
// ---- a HOME design made in CCS Console Workbench's HOME Designer, carried by a collection ----
// home.design is a reference {format:'ccs-home-design-embed/1', sha256 (of the .ccshome file), then whatever the Workbench wrote: id,
// revision, name, theme, content_sha256, bytes, and anything a newer version adds, all kept as they are}. The .ccshome bytes are kept
// apart in this browser's storage (the same store as the startup pictures, under their SHA-256) and travel inside .ccs and Full Studio
// Backup files as data (Base64), exactly as they arrived: this page never changes a design (HOME Designer is its one editor, its compiler
// the one converter), and the collection's own settings beside it (theme, startup screen, folders) never change it either.
const SU_DESIGN='ccs-home-design-embed/1', SU_DESIGN_MAX=64e6;
function suDesignRef(d){ if(!d||typeof d!=='object'||Array.isArray(d)||d.format!==SU_DESIGN||typeof d.sha256!=='string'||!/^[0-9a-f]{64}$/.test(d.sha256)) return null;
 const o={format:SU_DESIGN, sha256:d.sha256}; for(const k of Object.keys(d)) if(k!=='format'&&k!=='sha256'&&k!=='data') o[k]=d[k]; return o; }
async function suVerifyStoreDesign(sha, b64, name){ SU.verifying.add(sha); try{ const u=suBytes(b64); if(u.length>SU_DESIGN_MAX||await suSha(u)!==sha) throw new Error('the HOME design in the backup is damaged');
  await suPut(sha, u, name||'HOME design'); SU.b64.set(sha, b64); SU.bad.delete(sha); }
 catch(e){ SU.bad.set(sha, String(e&&e.message||e)); } finally{ SU.verifying.delete(sha); } }
const SU={db:null, b64:new Map(), bad:new Map(), pending:new Map(), names:new Map(), fresh:new Map(), verifying:new Set(), quiet:0, ready:null, draft:null};
function suIsId(v){ return typeof v==='string'&&/^custom-[0-9a-f]{16}$/.test(v); }
// the reference a collection keeps (never the bytes): checked field by field, anything else dropped
function suClean(c){ if(!c||typeof c!=='object'||Array.isArray(c)) return null; const sha=typeof c.sha256==='string'&&/^[0-9a-f]{64}$/.test(c.sha256)?c.sha256:null; if(!sha) return null;
 const id='custom-'+sha.slice(0,16); if(c.id!==id) return null;
 return {id, sha256:sha, name:String(c.name||'My picture').replace(/[\u0000-\u001f]/g,' ').trim().slice(0,80)||'My picture', fit:c.fit==='fill'?'fill':'fit', bytes:Number.isSafeInteger(c.bytes)&&c.bytes>0?c.bytes:0, width:1280, height:720}; }
function suB64(u8){ let s=''; for(let i=0;i<u8.length;i+=0x8000) s+=String.fromCharCode.apply(null, u8.subarray(i,i+0x8000)); return btoa(s); }
function suBytes(b64){ const s=atob(b64); const u=new Uint8Array(s.length); for(let i=0;i<s.length;i++) u[i]=s.charCodeAt(i); return u; }
async function suSha(u8){ const d=await crypto.subtle.digest('SHA-256', u8); return [...new Uint8Array(d)].map(b=>b.toString(16).padStart(2,'0')).join(''); }
// ---- this browser's storage for the pictures ----
function suDb(){ if(SU.db) return SU.db; SU.db=new Promise((res,rej)=>{ if(typeof indexedDB==='undefined'){ rej(new Error('this browser has no storage for pictures')); return; }
 const q=indexedDB.open('ccs-startup-images', 1); q.onupgradeneeded=()=>{ q.result.createObjectStore('images', {keyPath:'sha256'}); }; q.onsuccess=()=>res(q.result); q.onerror=()=>rej(q.error||new Error('the picture storage could not be opened')); });
 SU.db.catch(()=>{ SU.db=null; }); return SU.db; }
async function suTx(mode, fn){ const db=await suDb(); return new Promise((res,rej)=>{ const t=db.transaction('images', mode), s=t.objectStore('images'); let out; const r=fn(s); if(r) r.onsuccess=()=>{ out=r.result; }; t.oncomplete=()=>res(out); t.onerror=t.onabort=()=>rej(t.error||new Error('the picture storage did not complete')); }); }
async function suPut(sha, u8, name){ await suTx('readwrite', s=>s.put({sha256:sha, bytes:u8.buffer.slice(u8.byteOffset, u8.byteOffset+u8.length), name, at:Date.now()})); const back=await suTx('readonly', s=>s.get(sha)); if(!back||!back.bytes||back.bytes.byteLength!==u8.length) throw new Error('this browser did not keep the picture'); }
// the pictures the collections in this browser name (every saved copy: the collections, the open HOME, earlier versions kept for Go Back)
function suNamed(){ const out=new Set(), re=/"startup_custom":\{[^{}]*?"sha256":"([0-9a-f]{64})"/g, rd=/"design":\{"format":"ccs-home-design-embed\/1","sha256":"([0-9a-f]{64})"/g; try{ for(let i=0;i<localStorage.length;i++){ const v=localStorage.getItem(localStorage.key(i))||''; if(v.indexOf('startup_custom')<0&&v.indexOf(SU_DESIGN)<0) continue; let m; while((m=re.exec(v))) out.add(m[1]); while((m=rd.exec(v))) out.add(m[1]); } }catch(e){ return null; }
 try{ if(typeof TV!=='undefined'&&TV.model&&TV.model.design&&TV.model.design.sha256) out.add(TV.model.design.sha256); }catch(e){}
 try{ if(typeof TV!=='undefined'&&TV.model&&TV.model.startup_custom&&TV.model.startup_custom.sha256) out.add(TV.model.startup_custom.sha256); }catch(e){} return out; }
async function suLoad(){ const named=suNamed(); if(!named||!named.size) return; for(const sha of named){ if(SU.b64.has(sha)) continue; try{ const r=await suTx('readonly', s=>s.get(sha)); if(r&&r.bytes){ const u=new Uint8Array(r.bytes); if(await suSha(u)===sha) SU.b64.set(sha, suB64(u)); else SU.bad.set(sha, 'the kept copy is damaged'); } }catch(e){} } }
// a picture nothing names any more is removed (never one another tab added in the last ten minutes: its collection may not be saved yet)
async function suTidy(){ if(typeof CS_STATE!=='undefined'&&CS_STATE!=='ok') return; const named=suNamed(); if(!named) return;
 try{ const all=await suTx('readonly', s=>s.getAll()); const old=Date.now()-600e3; const gone=(all||[]).filter(r=>!named.has(r.sha256)&&(r.at||0)<old).map(r=>r.sha256);
  if(gone.length) await suTx('readwrite', s=>{ gone.forEach(k=>s.delete(k)); }); gone.forEach(k=>SU.b64.delete(k)); }catch(e){} }
// ---- what a collection's reference amounts to here: ok (the picture is here), checking, or a problem in plain words ----
function suState(c){ if(!c) return {st:'none'}; if(c.problem) return {st:'bad', why:c.problem}; if(SU.bad.has(c.sha256)) return {st:'bad', why:SU.bad.get(c.sha256)};
 if(SU.b64.has(c.sha256)) return {st:'ok'}; if(SU.pending.has(c.sha256)) return {st:'checking'}; return {st:'bad', why:`it is not in this ${typeof cvApp==='function'&&cvApp()?'app':'browser'} (choose it again, or restore a backup that has it)`}; }
// ---- leaving the browser: the reference with the picture's bytes (a .ccs or Full Studio Backup file, the console package) ----
// Before a backup is made, every picture its collections name is read again from this browser's storage and checked (suEnsure: the
// SHA-256 it is named by, a JPEG, 1280×720); what is in memory or on the screen is not trusted for that. A picture that cannot be read
// stops that backup with the reason: a backup never names a picture without carrying it, and Random never takes its place.
// strict (a backup being made): only the bytes that check just read; anything else is an error, never a reference without its picture.
function suInlineHome(h, strict){ if(!h||typeof h!=='object') return h||null; const raw=h.startup_custom; let o=h;
 if(strict&&raw!=null){ const c=suClean(raw); const b=c&&!raw.problem?SU.fresh.get(c.sha256):null; if(!b) throw new Error('the startup picture was not checked for this backup'); o=Object.assign({}, o, {startup_custom:Object.assign({}, c, {data:b, mime:'image/jpeg'})}); }
 else { const c=suClean(raw); const b=c?SU.b64.get(c.sha256):null; if(c&&b&&!SU.bad.has(c.sha256)) o=Object.assign({}, o, {startup_custom:Object.assign({}, c, {data:b, mime:'image/jpeg'})}); }
 const dr=suDesignRef(h.design);   /* a HOME design travels with its bytes, exactly as kept */
 if(dr){ const b=strict?SU.fresh.get(dr.sha256):(SU.bad.has(dr.sha256)?null:SU.b64.get(dr.sha256)); if(strict&&!b) throw new Error('the HOME design was not checked for this backup'); if(b) o=Object.assign({}, o, {design:Object.assign({}, dr, {data:b})}); }
 return o; }
async function suEnsure(recs){ try{ await SU.ready; }catch(e){}
 const want=new Map(), problems=[], fresh=new Map(), here=typeof cvApp==='function'&&cvApp()?'app':'browser';
 const designs=new Map(); for(const r of recs||[]){ const dr=r&&r.home&&typeof r.home==='object'?suDesignRef(r.home.design):null; if(dr&&!designs.has(dr.sha256)) designs.set(dr.sha256, {r, d:dr}); }
 for(const [sha, v] of designs){ try{ let rec; try{ rec=await suTx('readonly', s=>s.get(sha)); }catch(e){ throw new Error(`this ${here}'s storage could not be read`); }
   if(!rec||!rec.bytes) throw new Error(`it is not in this ${here} any more`); const u=new Uint8Array(rec.bytes); if(u.length>SU_DESIGN_MAX||await suSha(u)!==sha) throw new Error('the kept copy is damaged');
   const b=suB64(u); fresh.set(sha, b); SU.b64.set(sha, b); SU.bad.delete(sha); }
  catch(e){ const why=String(e&&e.message||e); SU.bad.set(sha, why); problems.push({name:v.r.name, picture:'HOME design '+(v.d.name||''), why}); } }
 for(const r of recs||[]){ const raw=r&&r.home&&typeof r.home==='object'?r.home.startup_custom:null; if(raw==null) continue; const c=suClean(raw);
  if(!c||raw.problem){ problems.push({name:r.name, picture:raw&&raw.name, why:raw&&raw.problem||'the collection does not describe its startup picture correctly'}); continue; }
  if(!want.has(c.sha256)) want.set(c.sha256, {r, c}); }
 for(const [sha, v] of want){ if(SU.pending.has(sha)){ try{ await SU.pending.get(sha); }catch(e){} }
  try{ let rec; try{ rec=await suTx('readonly', s=>s.get(sha)); }catch(e){ throw new Error(`this ${here}'s picture storage could not be read`); }
   if(!rec||!rec.bytes) throw new Error(`it is not in this ${here} any more`);
   const u=new Uint8Array(rec.bytes); if(u.length>SU_LIM.out||u.length<4||u[0]!==0xFF||u[1]!==0xD8) throw new Error('the kept copy is not a JPEG picture');
   if(await suSha(u)!==sha) throw new Error('the kept copy is damaged');
   let bm; try{ bm=await createImageBitmap(new Blob([u], {type:'image/jpeg'})); }catch(e){ throw new Error('the kept copy could not be read as a picture'); }
   const good=bm.width===1280&&bm.height===720; try{ bm.close(); }catch(e){} if(!good) throw new Error('the kept copy is not 1280×720');
   const b=suB64(u); fresh.set(sha, b); SU.b64.set(sha, b); SU.bad.delete(sha); }
  catch(e){ const why=String(e&&e.message||e); SU.bad.set(sha, why); problems.push({name:v.r.name, picture:v.c.name, why}); } }
 SU.fresh=fresh;
 if(problems.length){ const p=problems[0]; const e=new Error(`the startup picture${p.picture?` "${p.picture}"`:''} of the collection "${p.name}" could not be read (${p.why})${problems.length>1?` (and ${problems.length-1} more)`:''}. Try again; if it happens again, open that collection's HOME Settings › Startup screen to choose the picture again or remove it`);
  e.code='startup-picture'; e.problems=problems; throw e; }
 return fresh; }
// ---- arriving (a file opened, a backup restored): the bytes are checked (a JPEG, 1280×720, the SHA-256 it is named by) and kept in
//      this browser's storage; the collection keeps only the reference. A picture that fails keeps its reference with the reason, so HOME
//      Settings says so and offers to choose again (it is never silently turned into Random) ----
function suAdoptHome(h, name){ if(!h||typeof h!=='object'||Array.isArray(h)) return null; const o=Object.assign({}, h); const raw=o.startup_custom; delete o.startup_custom;
 if(o.design!=null){ const rd=o.design, dr=suDesignRef(rd);   /* a HOME design arriving with its bytes: checked (its SHA-256) and kept apart; the reference stays exactly as written */
  if(!dr){ const keep=rd&&typeof rd==='object'&&!Array.isArray(rd)?Object.assign({}, rd):{}; delete keep.data; keep.problem='the HOME design in the backup is not described correctly'; o.design=keep; }
  else { o.design=dr; if(!SU.quiet&&typeof rd.data==='string'&&!SU.b64.has(dr.sha256)&&!SU.verifying.has(dr.sha256)){ if(rd.data.length>SU_DESIGN_MAX*1.4||!/^[A-Za-z0-9+/]+={0,2}$/.test(rd.data)) SU.bad.set(dr.sha256, 'the HOME design in the backup is damaged'); else suTrack(dr.sha256, suVerifyStoreDesign(dr.sha256, rd.data, dr.name)); } } }
 const c=suClean(raw);
 if(!c){ if(suIsId(o.startup_image)) o.startup_custom={id:o.startup_image, problem:'the backup does not describe the picture correctly'}; return o; }
 o.startup_custom=c; if(SU.quiet) return o;   // a duplicate check of collections already here: no checks, no messages
 if(name) SU.names.set(c.sha256, name);
 if(raw&&typeof raw.data==='string'&&!SU.b64.has(c.sha256)&&!SU.verifying.has(c.sha256)){   // arriving with its bytes: always checked and kept (even while a "kept here?" check runs)
  if(raw.data.length>SU_LIM.b64||!/^[A-Za-z0-9+/]+={0,2}$/.test(raw.data)) SU.bad.set(c.sha256, 'the picture in the backup is not a valid startup picture');
  else suTrack(c.sha256, suVerifyStore(c.sha256, raw.data, c.name)); }
 else if(!(raw&&typeof raw.data==='string')&&!SU.b64.has(c.sha256)&&!SU.pending.has(c.sha256)) suTrack(c.sha256, suHere(c.sha256));   // named without its bytes: usable only if this browser already keeps it
 return o; }
// the duplicate checks run collections already here through csRecordFromFile; they must not start checks or say anything
function suQuiet(fn){ SU.quiet++; try{ return fn(); } finally{ SU.quiet--; } }
function suTrack(sha, p){ SU.pending.set(sha, p); p.then(()=>{}, ()=>{}).then(()=>{ if(SU.pending.get(sha)===p) SU.pending.delete(sha); try{ if($('hfBody')) hfRender(); }catch(e){} }); return p; }
async function suHere(sha){ const settled=()=>SU.b64.has(sha)||SU.verifying.has(sha);   // its bytes arrived meanwhile: nothing to say
 try{ const r=await suTx('readonly', s=>s.get(sha)); const u=r&&r.bytes?new Uint8Array(r.bytes):null;
  if(u&&await suSha(u)===sha){ SU.b64.set(sha, suB64(u)); SU.bad.delete(sha); } else if(!settled()){ SU.bad.set(sha, `the backup names this picture but does not contain it, and it is not in this ${typeof cvApp==='function'&&cvApp()?'app':'browser'}`); suTell(sha); } }
 catch(e){ if(!settled()){ SU.bad.set(sha, 'the backup names this picture but does not contain it, and this browser\'s picture storage could not be read'); suTell(sha); } } }async function suVerifyStore(sha, b64, name){ SU.verifying.add(sha); try{ const u=suBytes(b64); if(u.length>SU_LIM.out||u[0]!==0xFF||u[1]!==0xD8) throw new Error('it is not a JPEG picture');
  if(await suSha(u)!==sha) throw new Error('it is not the picture the backup names (its content differs)');
  const bm=await createImageBitmap(new Blob([u], {type:'image/jpeg'})); const ok=bm.width===1280&&bm.height===720; try{ bm.close(); }catch(e){} if(!ok) throw new Error('it is not 1280×720');
  await suPut(sha, u, name); SU.b64.set(sha, b64); SU.bad.delete(sha); }
 catch(e){ SU.bad.set(sha, 'the picture in the backup could not be used: '+String(e&&e.message||e)); suTell(sha); }
 finally{ SU.verifying.delete(sha); } }
// a picture that arrived and cannot be used is said once, with its collection's name (HOME Settings keeps saying it until it is fixed)
function suTell(sha){ const n=SU.names.get(sha); const why=SU.bad.get(sha); if(!why) return; try{ toast(`The startup picture of ${n?`"${n}"`:'a restored collection'} cannot be used: ${why}. Its HOME Settings › Startup screen offers to choose it again.`); }catch(e){} }
// ---- reading a picked file: type by its first bytes; animated pictures refused; decoded upright; size limits ----
function suKind(u){ const at=(o,s)=>[...s].every((ch,i)=>u[o+i]===ch.charCodeAt(0));
 if(u.length>8&&u[0]===0x89&&at(1,'PNG')){ for(let p=8;p+8<=u.length;){ const n=((u[p]<<24)|(u[p+1]<<16)|(u[p+2]<<8)|u[p+3])>>>0; const t=String.fromCharCode(u[p+4],u[p+5],u[p+6],u[p+7]); if(t==='acTL') return {bad:'it is an animated PNG (a still picture is needed)'}; if(t==='IDAT'||t==='IEND') break; p+=12+n; } return {type:'image/png'}; }
 if(u.length>3&&u[0]===0xFF&&u[1]===0xD8&&u[2]===0xFF) return {type:'image/jpeg'};
 if(u.length>16&&at(0,'RIFF')&&at(8,'WEBP')){ if(at(12,'VP8X')&&(u[20]&0x02)) return {bad:'it is an animated WebP picture (a still picture is needed)'}; for(let p=12;p+8<=u.length;){ const t=String.fromCharCode(u[p],u[p+1],u[p+2],u[p+3]); if(t==='ANIM'||t==='ANMF') return {bad:'it is an animated WebP picture (a still picture is needed)'}; const n=(u[p+4]|(u[p+5]<<8)|(u[p+6]<<16)|(u[p+7]<<24))>>>0; p+=8+n+(n&1); } return {type:'image/webp'}; }
 if(u.length>5&&(at(0,'GIF87a')||at(0,'GIF89a'))) return {bad:'it is a GIF picture'};
 return {bad:'it is not a PNG, JPEG or WebP picture'}; }
async function suRead(file){ if(!file) throw new Error('no file was chosen'); if(file.size>SU_LIM.file) throw new Error(`it is ${fmtB(file.size)}; a picture of at most ${fmtB(SU_LIM.file)} is needed`);
 const u=new Uint8Array(await file.arrayBuffer()); const k=suKind(u); if(k.bad) throw new Error(k.bad);
 let bm; try{ bm=await createImageBitmap(new Blob([u], {type:k.type}), {imageOrientation:'from-image'}); }catch(e){ throw new Error('it could not be read as a picture (the file may be damaged)'); }
 if(bm.width>SU_LIM.side||bm.height>SU_LIM.side||bm.width*bm.height>SU_LIM.pixels){ const w=bm.width, h=bm.height; try{ bm.close(); }catch(e){} throw new Error(`it is ${w.toLocaleString('en-US')}×${h.toLocaleString('en-US')} pixels; at most ${SU_LIM.side.toLocaleString('en-US')} on a side and ${(SU_LIM.pixels/1e6).toLocaleString('en-US')} million pixels`); }
 if(bm.width<SU_LIM.minW||bm.height<SU_LIM.minH){ const w=bm.width, h=bm.height; try{ bm.close(); }catch(e){} throw new Error(`it is only ${w}×${h} pixels; at least ${SU_LIM.minW}×${SU_LIM.minH} is needed`); }
 return {bm, name:String(file.name||'My picture').replace(/\.[A-Za-z0-9]{1,5}$/,'').slice(0,80)||'My picture', w:bm.width, h:bm.height}; }
// the 1280×720 frame: black first (so transparency is black), then the picture, whole (fit) or covering (fill), centered
function suFrame(bm, fit){ const c=document.createElement('canvas'); c.width=1280; c.height=720; const x=c.getContext('2d'); x.fillStyle='#000'; x.fillRect(0,0,1280,720); x.imageSmoothingEnabled=true; x.imageSmoothingQuality='high';
 const s=fit==='fill'?Math.max(1280/bm.width, 720/bm.height):Math.min(1280/bm.width, 720/bm.height); const w=bm.width*s, h=bm.height*s; x.drawImage(bm, (1280-w)/2, (720-h)/2, w, h); return c; }
async function suEncode(c){ for(const q of [0.9, 0.84, 0.78, 0.7, 0.62, 0.55]){ const b=await new Promise(r=>c.toBlob(r, 'image/jpeg', q)); if(b&&b.size<=SU_LIM.out) return new Uint8Array(await b.arrayBuffer()); } throw new Error('the framed picture could not be made small enough'); }
// ---- HOME Settings: the startup screen's controls (home-folders.js draws them through suHtml and wires them through suWire) ----
function suChoiceName(m){ const v=m.startup_image||'random'; if(v==='random') return 'Random'; const s=HOME_STARTUP.find(x=>x.id===v); if(s) return s.name; const c=m.startup_custom; return c&&c.id===v?`Your picture (${c.name||'My picture'})`:'Your picture'; }
function suHtml(m){ const v=m.startup_image||'random', c=m.startup_custom&&m.startup_custom.id?m.startup_custom:null, cs=suState(c), mine=c&&v===c.id;
 if(SU.draft) return suDraftHtml();
 const opts=`<option value="random" ${v==='random'?'selected':''}>Random</option>${HOME_STARTUP.map(s=>`<option value="${s.id}" ${v===s.id?'selected':''}>${esc(s.name)}</option>`).join('')}${c?`<option value="${esc(c.id)}" ${mine?'selected':''}>Your picture${c.name?': '+esc(c.name):''}</option>`:''}`;
 const prev=mine?(cs.st==='ok'?`<div class="hf-st-one"><img src="data:image/jpeg;base64,${SU.b64.get(c.sha256)}" alt="${esc(c.name||'Your picture')}" width="480" height="270"></div>`:`<div class="msgbox ${cs.st==='checking'?'':'bad'} small" role="${cs.st==='checking'?'status':'alert'}">${cs.st==='checking'?'Checking the picture…':`Your startup picture cannot be used: ${esc(cs.why)}. Until it is fixed, Prepare HOME Menu stops here; choose the picture again or another startup screen.`}</div>`):hfStartupPreview(v);
 return `<label class="hf-row">Startup screen <select id="hfStartup">${opts}</select></label>
  <div class="hf-acts"><button class="btn sm" id="suPick">${c?'Replace Image…':'Choose Image…'}</button>${c?'<button class="btn sm ghost" id="suRemove">Remove Image</button>':''}</div><div class="small" id="suOut" role="status"></div>
  <div class="hf-startup" id="hfStartupPrev">${prev}</div>
  <div class="small">Shown when the console starts, for at least five seconds while its HOME loads. ${v==='random'?'Random picks one of the five Classic Cart Studio pictures each time.':mine?'Your picture every time.':'This picture every time.'} The theme is a separate choice. Your own picture: a PNG, JPEG or WebP file (a still picture of at least ${SU_LIM.minW}×${SU_LIM.minH} pixels, up to ${fmtB(SU_LIM.file)}) is framed for the TV on this ${typeof cvApp==='function'&&cvApp()?'computer':'device'}; the file itself is not changed or sent anywhere.</div>`; }
function suDraftHtml(){ const d=SU.draft; return `<div class="lbl">Frame your picture</div><div class="hf-startup"><div class="hf-st-one hf-st-draft"><img id="suDraftImg" src="${d.url}" alt="How the picture will look on the TV" width="480" height="270"></div></div>
  <div class="small">${esc(d.name)} · ${d.w.toLocaleString('en-US')}×${d.h.toLocaleString('en-US')} pixels. This is exactly what the console shows (1280×720).</div>
  <div class="hf-acts" role="radiogroup" aria-label="Framing"><label class="hf-row"><input type="radio" name="suFit" value="fit" ${d.fit==='fit'?'checked':''}> Fit: the whole picture, with black bars where it does not fill the screen</label><label class="hf-row"><input type="radio" name="suFit" value="fill" ${d.fit==='fill'?'checked':''}> Fill: the whole screen, the edges cut evenly</label></div>
  <div class="hf-acts"><button class="btn sm ghost" id="suCancel">Cancel</button><button class="btn sm hk" id="suUse">Use This Picture</button></div><div class="small" id="suOut" role="status"></div>`; }
function suDraftDraw(){ const d=SU.draft; if(!d) return; const c=suFrame(d.bm, d.fit); d.canvas=c; d.url=c.toDataURL('image/jpeg', 0.85); }
function suWire(m){ const say=t=>{ const o=$('suOut'); if(o) o.textContent=t; };
 if(SU.draft){ document.querySelectorAll('input[name=suFit]').forEach(r=>r.onchange=()=>{ SU.draft.fit=r.value; suDraftDraw(); hfRender(); });
  $('suCancel').onclick=()=>{ try{ SU.draft.bm.close(); }catch(e){} SU.draft=null; hfRender(); };
  $('suUse').onclick=async()=>{ const d=SU.draft; const b=$('suUse'); b.disabled=true; say('Saving the picture…');
   try{ const u=await suEncode(d.canvas||suFrame(d.bm, d.fit)); const sha=await suSha(u); await suPut(sha, u, d.name); SU.b64.set(sha, suB64(u)); SU.bad.delete(sha);
    const old=m.startup_custom; m.startup_custom={id:'custom-'+sha.slice(0,16), sha256:sha, name:d.name, fit:d.fit, bytes:u.length, width:1280, height:720}; m.startup_image=m.startup_custom.id;   // the earlier picture stays until this one is kept
    try{ d.bm.close(); }catch(e){} SU.draft=null; hfSave(); try{ dispatchEvent(new CustomEvent('ccs-content-changed',{detail:{what:'startup'}})); }catch(e){}
    toast(`Startup picture set: ${m.startup_custom.name}`); if(old&&old.sha256!==sha) setTimeout(suTidy, 0); }
   catch(e){ b.disabled=false; say('The picture was not saved: '+String(e&&e.message||e)+'. Nothing was changed.'); } };
  return; }
 const pick=$('suPick'); if(pick) pick.onclick=async()=>{ const files=await cvPick('image/png,image/jpeg,image/webp,.png,.jpg,.jpeg,.webp', false); if(!files||!files.length) return; say('Reading the picture…');
  try{ const r=await suRead(files[0]); SU.draft={bm:r.bm, name:r.name, w:r.w, h:r.h, fit:Math.abs(r.w/r.h-16/9)<0.02?'fill':'fit'}; suDraftDraw(); hfRender(); }
  catch(e){ say(`That picture cannot be used: ${String(e&&e.message||e)}. Nothing was changed.`); } };
 const rm=$('suRemove'); if(rm) rm.onclick=()=>{ const c=m.startup_custom; if(!c) return; if(!confirm(`Remove your startup picture "${c.name||'My picture'}" from this collection? ${m.startup_image===c.id?'The startup screen goes back to Random. ':''}Your own file is not affected.`)) return;
  if(m.startup_image===c.id) m.startup_image='random'; m.startup_custom=null; hfSave(); try{ dispatchEvent(new CustomEvent('ccs-content-changed',{detail:{what:'startup'}})); }catch(e){} toast('Startup picture removed from this collection'); setTimeout(suTidy, 0); };
 const sel=$('hfStartup'); if(sel) sel.onchange=()=>{ const v=sel.value; m.startup_image=v==='random'||HOME_STARTUP.some(s=>s.id===v)||(m.startup_custom&&v===m.startup_custom.id)?v:'random'; hfSave(); try{ dispatchEvent(new CustomEvent('ccs-content-changed',{detail:{what:'startup'}})); }catch(e){} }; }
// at start: the pictures the collections name, from this browser's storage; then pictures nothing names are tidied away
SU.ready=suLoad().then(()=>{ try{ if($('hfBody')) hfRender(); }catch(e){} setTimeout(suTidy, 5000); }).catch(()=>{});
