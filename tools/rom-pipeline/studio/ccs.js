// ---- Classic Cart Studio packages (.ccs) on the website and in the Windows app. Contract: tools/companion/CCS-FORMAT.md (ccs-package/2);
//      the Windows reader and writer: ccs_package.py. A package is always complete: every game it lists has its ROM file, verified by
//      content (exact version). Packages are made and read on this device only: nothing here sends a package or a ROM byte anywhere but,
//      when the person asks, to Classic Cart Studio on this same computer (http://127.0.0.1, allow-listed origin) or to a sharing app the
//      person picks in the device's own share sheet. There is no package size, ROM size or game-count limit (ZIP64 when needed); ROM data is
//      handled one file at a time and copied by reference (Blob slices) into a new package, so a large package is never held in memory.
const CCS_FORMAT='classic-cart-studio-package', CCS_SCHEMA='ccs-package/2', CCS_MANIFEST='ccs-manifest.json';
const CCS_LIM={json:1<<30, art:1<<20, ratio:400};   // JSON is parsed in memory; art is thumbnails; the ratio stops decompression bombs
const CCS_ROM_RE=/^roms\/[A-Za-z0-9_.-]{1,96}\.(nes|unh)$/, CCS_ART_RE=/^art\/[A-Za-z0-9_.-]{1,96}\.(jpg|png)$/, CCS_ID_RE=/^(p_)?[0-9a-f]{16}$/;
const CCS_FIXED={'ccs-manifest.json':'manifest','collection.json':'collection','README.txt':'readme'};
const CCS_README='Classic Cart Studio package (.ccs)\r\n\r\nThis file holds a game collection made with Classic Cart Studio: every game with its ROM file and exact version,\r\nthe collection\'s NES Classic Loadouts, HOME settings and artwork.\r\n\r\nOpen it with Classic Cart Studio for Windows (double-click the file once the app is installed), or on the website:\r\nhttps://classiccartstudio.com  ->  My Collections  ->  Import .ccs Collection\r\n\r\nClassic Cart Studio does not provide or host game files. Whoever includes or receives game files is responsible for having the\r\nrights to copy, distribute and use them.\r\n';
function ccsSafeName(n){ if(!n||n.length>200||/^[\/\\]/.test(n)||/[\\:\u0000-\u001f]/.test(n)) return false; if(n.split('/').some(p=>p===''||p==='.'||p==='..')) return false; return n in CCS_FIXED||CCS_ROM_RE.test(n)||CCS_ART_RE.test(n); }
const CCS_CRC=(()=>{ const t=new Uint32Array(256); for(let n=0;n<256;n++){ let c=n; for(let k=0;k<8;k++) c=c&1?0xEDB88320^(c>>>1):c>>>1; t[n]=c>>>0; } return t; })();
function ccsCrcUpdate(c, u8){ for(let i=0;i<u8.length;i++) c=CCS_CRC[(c^u8[i])&255]^(c>>>8); return c; }
function ccsCrc32(u8){ return (ccsCrcUpdate(0xFFFFFFFF, u8)^0xFFFFFFFF)>>>0; }
async function ccsCrcBlob(b){ let c=0xFFFFFFFF; for(let p=0;p<b.size;p+=4<<20) c=ccsCrcUpdate(c, new Uint8Array(await b.slice(p, Math.min(b.size,p+(4<<20))).arrayBuffer())); return (c^0xFFFFFFFF)>>>0; }
async function ccsSha(u8){ const h=await crypto.subtle.digest('SHA-256', u8); return [...new Uint8Array(h)].map(b=>b.toString(16).padStart(2,'0')).join(''); }
async function ccsDeflate(u8){ if(typeof CompressionStream==='undefined') return null; try{ return new Uint8Array(await new Response(new Blob([u8]).stream().pipeThrough(new CompressionStream('deflate-raw'))).arrayBuffer()); }catch(e){ return null; } }
async function ccsInflate(blob, declared){ if(typeof DecompressionStream==='undefined') throw new Error('This browser cannot open compressed packages; use a current browser or the Windows app.');
 const rd=blob.stream().pipeThrough(new DecompressionStream('deflate-raw')).getReader(); const parts=[]; let n=0;
 for(;;){ const {done, value}=await rd.read(); if(done) break; n+=value.length; if(n>declared){ try{ rd.cancel(); }catch(e){} throw new Error('more data than declared'); } parts.push(value); }
 const o=new Uint8Array(n); let p=0; for(const x of parts){ o.set(x,p); p+=x.length; } return o; }
// ROM identity (CCS-FORMAT.md): content = the file without its iNES header (and trainer); a library version's id is the first 16 hex digits
// of the content SHA-256, a personal game's id is p_ + the same
function ccsRomContent(u8){ if(u8.length>=16&&u8[0]===0x4E&&u8[1]===0x45&&u8[2]===0x53&&u8[3]===0x1A) return u8.subarray((u8[6]&4)?16+512:16); return u8; }
function ccsIdMatches(id, contentSha){ return id===contentSha.slice(0,16)||id==='p_'+contentSha.slice(0,16); }
function ccsRomExt(u8){ return u8.length>=4&&u8[0]===0x4E&&u8[1]===0x45&&u8[2]===0x53&&u8[3]===0x1A?'.nes':'.unh'; }
// ---- ZIP writing: stored or deflate entries, UTF-8 names, ZIP64 records when needed (or forced for tests); bodies are Blobs or bytes ----
function ccsX64(vals){ const d=new DataView(new ArrayBuffer(4+8*vals.length)); d.setUint16(0,1,true); d.setUint16(2,8*vals.length,true); vals.forEach((v,i)=>d.setBigUint64(4+8*i, BigInt(v), true)); return new Uint8Array(d.buffer); }
function ccsZipBlob(entries, force){ force=force||!!window.CCS_FORCE_ZIP64; const enc=new TextEncoder(); const now=new Date(); const dt=((now.getHours()<<11)|(now.getMinutes()<<5)|(now.getSeconds()>>1))&0xFFFF, dd=(((now.getFullYear()-1980)<<9)|((now.getMonth()+1)<<5)|now.getDate())&0xFFFF;
 const parts=[], cd=[]; let off=0, cdSize=0; const M=0xFFFFFFFF;
 for(const e of entries){ const name=enc.encode(e.name); const z=force||e.usize>=M||e.csize>=M||off>=M; const ver=z?45:20;
  const lx=z?ccsX64([e.usize,e.csize]):new Uint8Array(0); const lh=new DataView(new ArrayBuffer(30));
  lh.setUint32(0,0x04034b50,true); lh.setUint16(4,ver,true); lh.setUint16(6,0x0800,true); lh.setUint16(8,e.method,true); lh.setUint16(10,dt,true); lh.setUint16(12,dd,true); lh.setUint32(14,e.crc,true); lh.setUint32(18,z?M:e.csize,true); lh.setUint32(22,z?M:e.usize,true); lh.setUint16(26,name.length,true); lh.setUint16(28,lx.length,true);
  parts.push(new Uint8Array(lh.buffer), name, lx, e.body);
  const cx=z?ccsX64([e.usize,e.csize,off]):new Uint8Array(0); const ch=new DataView(new ArrayBuffer(46));
  ch.setUint32(0,0x02014b50,true); ch.setUint16(4,ver,true); ch.setUint16(6,ver,true); ch.setUint16(8,0x0800,true); ch.setUint16(10,e.method,true); ch.setUint16(12,dt,true); ch.setUint16(14,dd,true); ch.setUint32(16,e.crc,true); ch.setUint32(20,z?M:e.csize,true); ch.setUint32(24,z?M:e.usize,true); ch.setUint16(28,name.length,true); ch.setUint16(30,cx.length,true); ch.setUint32(42,z?M:off,true);
  cd.push(new Uint8Array(ch.buffer), name, cx); cdSize+=46+name.length+cx.length; off+=30+name.length+lx.length+e.csize; }
 const n=entries.length, z64=force||n>=0xFFFF||off>=M||cdSize>=M; const tail=[];
 if(z64){ const r=new DataView(new ArrayBuffer(56)); r.setUint32(0,0x06064b50,true); r.setBigUint64(4,44n,true); r.setUint16(12,45,true); r.setUint16(14,45,true); r.setBigUint64(24,BigInt(n),true); r.setBigUint64(32,BigInt(n),true); r.setBigUint64(40,BigInt(cdSize),true); r.setBigUint64(48,BigInt(off),true);
  const l=new DataView(new ArrayBuffer(20)); l.setUint32(0,0x07064b50,true); l.setBigUint64(8,BigInt(off+cdSize),true); l.setUint32(16,1,true); tail.push(new Uint8Array(r.buffer), new Uint8Array(l.buffer)); }
 const e=new DataView(new ArrayBuffer(22)); e.setUint32(0,0x06054b50,true); e.setUint16(8,z64?0xFFFF:n,true); e.setUint16(10,z64?0xFFFF:n,true); e.setUint32(12,z64?M:cdSize,true); e.setUint32(16,z64?M:off,true);
 return new Blob([...parts, ...cd, ...tail, new Uint8Array(e.buffer)], {type:'application/vnd.classiccartstudio.collection+zip'}); }
async function ccsJsonEntry(name, obj){ const u8=new TextEncoder().encode(typeof obj==='string'?obj:JSON.stringify(obj)); const d=u8.length>512?await ccsDeflate(u8):null; const z=d&&d.length<u8.length;
 return {entry:{name, method:z?8:0, crc:ccsCrc32(u8), usize:u8.length, csize:z?d.length:u8.length, body:z?d:u8}, sha256:await ccsSha(u8), size:u8.length}; }
// ---- ZIP reading (ZIP64 aware): the central directory, then only the entries asked for, one at a time ----
async function ccsUnzip(blob){ const size=blob.size; if(size<22) throw new Error('not a valid archive'); const tl=Math.min(size,65557); const tail=new Uint8Array(await blob.slice(size-tl).arrayBuffer()); let e=-1;
 for(let i=tail.length-22;i>=0;i--){ if(tail[i]===0x50&&tail[i+1]===0x4b&&tail[i+2]===5&&tail[i+3]===6){ e=i; break; } } if(e<0) throw new Error('not a valid archive');
 const ev=new DataView(tail.buffer, tail.byteOffset+e); let n=ev.getUint16(10,true), cdSize=ev.getUint32(12,true), cdOff=ev.getUint32(16,true);
 if(n===0xFFFF||cdSize===0xFFFFFFFF||cdOff===0xFFFFFFFF){ const lp=size-tl+e-20; if(lp<0) throw new Error('not a valid archive'); const lv=new DataView(await blob.slice(lp, lp+20).arrayBuffer());
  if(lv.getUint32(0,true)!==0x07064b50) throw new Error('not a valid archive'); const ro=Number(lv.getBigUint64(8,true)); const rv=new DataView(await blob.slice(ro, ro+56).arrayBuffer());
  if(rv.getUint32(0,true)!==0x06064b50) throw new Error('not a valid archive'); n=Number(rv.getBigUint64(32,true)); cdSize=Number(rv.getBigUint64(40,true)); cdOff=Number(rv.getBigUint64(48,true)); }
 if(cdOff+cdSize>size) throw new Error('not a valid archive');
 const cdb=new Uint8Array(await blob.slice(cdOff, cdOff+cdSize).arrayBuffer()); const v=new DataView(cdb.buffer); const dec=new TextDecoder('utf-8',{fatal:false}); const entries=[]; let p=0;
 for(let k=0;k<n;k++){ if(p+46>cdb.length||v.getUint32(p,true)!==0x02014b50) throw new Error('not a valid archive'); const nl=v.getUint16(p+28,true), xl=v.getUint16(p+30,true), cl=v.getUint16(p+32,true);
  const en={name:dec.decode(cdb.subarray(p+46,p+46+nl)), flags:v.getUint16(p+8,true), method:v.getUint16(p+10,true), crc:v.getUint32(p+16,true), csize:v.getUint32(p+20,true), usize:v.getUint32(p+24,true), ext:v.getUint32(p+38,true), lho:v.getUint32(p+42,true)};
  if(en.usize===0xFFFFFFFF||en.csize===0xFFFFFFFF||en.lho===0xFFFFFFFF){ let q=p+46+nl; const qe=q+xl; while(q+4<=qe){ const id=v.getUint16(q,true), sz=v.getUint16(q+2,true); if(id===1){ let r=q+4;
     if(en.usize===0xFFFFFFFF){ en.usize=Number(v.getBigUint64(r,true)); r+=8; } if(en.csize===0xFFFFFFFF){ en.csize=Number(v.getBigUint64(r,true)); r+=8; } if(en.lho===0xFFFFFFFF){ en.lho=Number(v.getBigUint64(r,true)); r+=8; } break; } q+=4+sz; } }
  entries.push(en); p+=46+nl+xl+cl; }
 const dataStart=async en=>{ const lh=new DataView(await blob.slice(en.lho, en.lho+30).arrayBuffer()); if(lh.getUint32(0,true)!==0x04034b50) throw new Error('not a valid archive'); return en.lho+30+lh.getUint16(26,true)+lh.getUint16(28,true); };
 return {size, entries,
  async raw(en){ const st=await dataStart(en); if(st+en.csize>size) throw new Error('not a valid archive'); return blob.slice(st, st+en.csize); },
  async read(en, limit){ if(en.usize>limit) throw new Error(en.name+': larger than allowed'); const raw=await this.raw(en); const d=en.method===0?new Uint8Array(await raw.arrayBuffer()):await ccsInflate(raw, en.usize); if(d.length!==en.usize) throw new Error(en.name+': size differs'); return d; } }; }
// ---- validate a package completely (the same rules as the Windows app's reader); ROMs are read one at a time ----
async function ccsInspect(blob, opt){ opt=opt||{}; let z; try{ z=await ccsUnzip(blob); }catch(e){ throw new Error('This file is not a Classic Cart Studio package, or it is damaged.'); }
 const E=z.entries; const names=E.map(x=>x.name); if(new Set(names).size!==names.length) throw new Error('The package has duplicate entries.');
 for(const x of E){ if(x.name.endsWith('/')) throw new Error('The package contains an unexpected folder entry.'); if(!ccsSafeName(x.name)) throw new Error(`The package contains an unsafe or unexpected entry (${x.name.slice(0,60)}).`);
  if(((x.ext>>>16)&0xF000)===0xA000) throw new Error('The package contains a link, which is not allowed.'); if(x.flags&1) throw new Error('Encrypted packages are not supported.'); if(x.method!==0&&x.method!==8) throw new Error('The package uses an unsupported compression method.');
  if(x.csize&&x.usize/Math.max(1,x.csize)>CCS_LIM.ratio) throw new Error('The package contains an entry with a suspicious compression ratio.'); }
 const by=Object.fromEntries(E.map(x=>[x.name,x])); if(!by[CCS_MANIFEST]) throw new Error('This is not a Classic Cart Studio package.');
 let man; try{ man=JSON.parse(new TextDecoder().decode(await z.read(by[CCS_MANIFEST], CCS_LIM.json))); }catch(e){ throw new Error('The package contents list is damaged.'); }
 if(!man||man.format!==CCS_FORMAT) throw new Error('This is not a Classic Cart Studio package.');
 if(man.schema==='ccs-package/1') throw new Error('This package does not include its ROM files, so it can\'t be opened. Ask for a new .ccs file.');
 if(man.schema!==CCS_SCHEMA) throw new Error('This package was made by a newer Classic Cart Studio; update to open it.'); if(man.kind!=='collection') throw new Error('This is not a Classic Cart Studio package.');
 if(!Array.isArray(man.files)) throw new Error('The package contents list is damaged.'); const lp=man.files.map(f=>f&&f.path); if(new Set(lp).size!==lp.length) throw new Error('The package contents list names a file twice.');
 if(names.some(x=>x!==CCS_MANIFEST&&!lp.includes(x))) throw new Error('The package contains files its contents list does not name.');
 const problems=[], roms=[], art={}; let data=null; const romFiles=man.files.filter(f=>f&&typeof f.path==='string'&&f.path.startsWith('roms/'));
 for(const f of man.files){ const p=f&&f.path; if(typeof p!=='string'||!ccsSafeName(p)||p===CCS_MANIFEST) throw new Error('The package contents list names an unsafe file.'); if(!/^[0-9a-f]{64}$/.test(f.sha256||'')||!Number.isSafeInteger(f.size)||f.size<0) throw new Error('The package contents list has an invalid checksum or size.');
  const x=by[p]; if(!x){ problems.push({path:p, problem:'listed but missing from the package'}); continue; } if(x.usize!==f.size){ problems.push({path:p, problem:'size differs from the listed size'}); continue; }
  if(p.startsWith('roms/')) continue;
  const d=await z.read(x, p.startsWith('art/')?CCS_LIM.art:CCS_LIM.json); if(await ccsSha(d)!==f.sha256){ problems.push({path:p, problem:'checksum does not match (damaged or altered)'}); continue; }
  if(p.startsWith('art/')){ const id=p.slice(4).replace(/\.(jpg|png)$/,''); let s=''; for(let i=0;i<d.length;i+=0x8000) s+=String.fromCharCode.apply(null, d.subarray(i,i+0x8000)); art[id]=`data:image/${p.endsWith('.png')?'png':'jpeg'};base64,`+btoa(s); }
  else if(p==='collection.json'){ try{ data=JSON.parse(new TextDecoder().decode(d)); }catch(e){ throw new Error('The collection inside the package is not valid.'); } } }
 if(!data) throw new Error('The package holds no collection.');
 const ids=((data.ccs_collection&&data.ccs_collection.sel)||(data.selections||[]).map(s=>s.version_id||s.game_id)||[]).map(String); const want=new Set(ids); const seen=new Map();
 for(const f of romFiles){ const id=String(f.version_id||''); if(!want.has(id)){ problems.push({path:f.path, problem:'is not a game of this collection'}); continue; } if(seen.has(id)){ problems.push({path:f.path, problem:'a second ROM file for the same game'}); continue; } seen.set(id, f); }
 const missing=ids.filter(id=>!seen.has(id)); if(missing.length) problems.push({path:'collection', problem:`${missing.length} game${missing.length===1?' has':'s have'} no ROM file in the package`});
 let done=0; const total=seen.size;
 for(const [id,f] of seen){ if(opt.cancel&&opt.cancel()) throw new Error('canceled'); const x=by[f.path]; if(!x||problems.some(q=>q.path===f.path)) continue;
  const rec={path:f.path, version_id:id, title:String(f.title||''), size:f.size, sha256:f.sha256, content_sha256:'', verified:null, entry:x};
  if(opt.verifyRoms!==false){ let d; try{ d=await z.read(x, f.size); }catch(e){ problems.push({path:f.path, problem:'cannot be read ('+e.message+')'}); continue; }
   if(await ccsSha(d)!==f.sha256){ problems.push({path:f.path, problem:'checksum does not match (damaged or altered)'}); continue; }
   rec.content_sha256=await ccsSha(ccsRomContent(d)); if(!ccsIdMatches(id, rec.content_sha256)){ problems.push({path:f.path, problem:'is a different version than the game it is listed for'}); continue; } rec.verified=true; rec.ext=ccsRomExt(d); }
  roms.push(rec); done++; if(opt.progress) opt.progress(done, total); }
 return {manifest:man, kind:'collection', data, art, roms, problems, size:z.size, ok:!problems.length, romBytes:roms.reduce((a,r)=>a+r.size,0), zip:z, blob}; }
// ---- ROM files available to this page (website): chosen by the person (ROM files, ZIPs of ROMs, or .ccs packages), kept for this visit
//      only as references to those files (nothing is copied into storage or sent anywhere) ----
const ROMSRC=new Map();   // game id -> {name, ext, sha256, content_sha256, size, raw:{blob, method, crc, csize, usize} | file}
function ccsSrcAdd(rec){ const k=rec.content_sha256.slice(0,16); [k, 'p_'+k].forEach(id=>{ if(!ROMSRC.has(id)) ROMSRC.set(id, rec); }); }
async function ccsIdentifyBytes(u8, name){ const sha=await ccsSha(u8); return {name, ext:ccsRomExt(u8), sha256:sha, content_sha256:await ccsSha(ccsRomContent(u8)), size:u8.length}; }
async function ccsAddRomSources(files, progress){ let found=0, ignored=0;
 for(const f of files){ const nm=f.name||'';
  try{ if(/\.ccs$/i.test(nm)){ const info=await ccsInspect(f, {progress}); if(!info.ok) { ignored++; continue; } for(const r of info.roms){ ccsSrcAdd({name:r.path, ext:r.ext, sha256:r.sha256, content_sha256:r.content_sha256, size:r.size, raw:{blob:await info.zip.raw(r.entry), method:r.entry.method, crc:r.entry.crc, csize:r.entry.csize, usize:r.entry.usize}}); found++; } continue; }
   if(/\.zip$/i.test(nm)){ const z=await ccsUnzip(f); for(const en of z.entries){ if(!/\.(nes|unh)$/i.test(en.name)||(en.method!==0&&en.method!==8)||(en.flags&1)||(en.csize&&en.usize/Math.max(1,en.csize)>CCS_LIM.ratio)) continue; const d=await z.read(en, en.usize); const rec=await ccsIdentifyBytes(d, en.name); rec.raw={blob:await z.raw(en), method:en.method, crc:en.crc, csize:en.csize, usize:en.usize}; ccsSrcAdd(rec); found++; } continue; }
   if(/\.(nes|unh)$/i.test(nm)){ const d=new Uint8Array(await f.arrayBuffer()); const rec=await ccsIdentifyBytes(d, nm); rec.file=f; ccsSrcAdd(rec); found++; continue; }
   ignored++; }catch(e){ ignored++; } if(progress) progress(); }
 return {found, ignored}; }
function ccsPickRoms(){ return new Promise(res=>{ const inp=document.createElement('input'); inp.type='file'; inp.multiple=true; inp.accept='.nes,.unh,.zip,.ccs'; inp.onchange=()=>res([...inp.files]); inp.addEventListener('cancel',()=>res([])); inp.click(); }); }
// ---- what a collection's package holds: its file (every game, loadouts, HOME settings) and thumbnails of its own artwork ----
async function ccsCollectionParts(r){ const file=csFileFor(r); const games=csWithRecord(r, ()=>shGames()); const art={}, artB64={};
 for(const g of games){ if(!g.own_art||!/^[A-Za-z0-9_.-]{1,96}$/.test(g.id)) continue; const t=await shThumb(g.own_art); if(t){ artB64[g.id+'.jpg']=t; const bin=atob(t); const u=new Uint8Array(bin.length); for(let i=0;i<bin.length;i++) u[i]=bin.charCodeAt(i); art[g.id+'.jpg']=u; } }
 const ids=((file.ccs_collection||{}).sel||[]).map(String); const titles={}; games.forEach(g=>{ titles[g.id]=g.display_title||g.title||g.id; });
 const summary={name:r.name, games:ids.length, loadouts:((file.ccs_collection||{}).loadouts||[]).length, personal:games.filter(g=>g.source==='personal').length};
 return {file, games, ids, titles, art, artB64, summary}; }
// a smaller share selection the person chose explicitly: the same collection limited to some of its games (loadouts and folders follow)
function ccsSubsetRecord(r, keep){ const k=new Set(keep); csSaveActiveNow(); const o=JSON.parse(JSON.stringify(csRecordOut(CS.items[r.id]||r))); o.id='share_'+r.id; o.sel=(o.sel||[]).filter(x=>k.has(x));
 o.loadouts=(o.loadouts||[]).map(l=>Object.assign(l, {sel:(l.sel||[]).filter(x=>k.has(x))})); const fold=h=>{ if(h&&Array.isArray(h.folders)) h.folders.forEach(f=>{ f.order=(f.order||[]).filter(x=>k.has(x)); }); return h; }; o.home=fold(o.home); o.loadouts.forEach(l=>fold(l.home)); return o; }
// the package itself (website): collection.json, artwork, one ROM per game (copied by reference), README and the manifest
async function ccsBuild(parts, progress, cancel){ const entries=[], listed=[], included=[];
 const c=await ccsJsonEntry('collection.json', parts.file); entries.push(c.entry); listed.push({path:'collection.json', sha256:c.sha256, size:c.size, kind:'collection'});
 for(const [n,u8] of Object.entries(parts.art||{})){ const p='art/'+n; if(!CCS_ART_RE.test(p)||u8.length>CCS_LIM.art) continue; entries.push({name:p, method:0, crc:ccsCrc32(u8), usize:u8.length, csize:u8.length, body:u8}); listed.push({path:p, sha256:await ccsSha(u8), size:u8.length, kind:'art'}); }
 let done=0, bytes=0;
 for(const id of parts.ids){ if(cancel&&cancel()) throw new Error('canceled'); const s=ROMSRC.get(id); if(!s) throw new Error('missing ROM for '+id); const p='roms/'+id+s.ext;
  if(s.raw) entries.push({name:p, method:s.raw.method, crc:s.raw.crc, usize:s.raw.usize, csize:s.raw.csize, body:s.raw.blob});
  else entries.push({name:p, method:0, crc:await ccsCrcBlob(s.file), usize:s.file.size, csize:s.file.size, body:s.file});
  const rec={path:p, sha256:s.sha256, size:s.size, kind:'rom', version_id:id, title:String(parts.titles[id]||id).slice(0,120), content_sha256:s.content_sha256}; listed.push(rec); included.push({version_id:id, title:rec.title, path:p, sha256:s.sha256, size:s.size, content_sha256:s.content_sha256});
  done++; bytes+=s.size; if(progress) progress(done, parts.ids.length); }
 const rd=await ccsJsonEntry('README.txt', CCS_README); entries.push(rd.entry); listed.push({path:'README.txt', sha256:rd.sha256, size:rd.size, kind:'readme'});
 const man={format:CCS_FORMAT, schema:CCS_SCHEMA, kind:'collection', created:new Date().toISOString().replace(/\.\d+Z$/,'Z'), app:{name:'Classic Cart Studio', version:(typeof COMPANION!=='undefined'&&COMPANION.version)||''}, summary:Object.assign({}, parts.summary, {games:parts.ids.length, roms:included.length, rom_bytes:bytes}), files:listed, roms:{included}};
 entries.push((await ccsJsonEntry(CCS_MANIFEST, JSON.stringify(man,null,1))).entry); return ccsZipBlob(entries); }
function ccsAppPackages(){ return !!(typeof CO!=='undefined'&&CO.desktop&&CO.ok&&((COMPANION.info&&COMPANION.info.capabilities)||COMPANION.capabilities||[]).includes('ccs-package')); }
function ccsName(r, n){ return csSafeName((r.name||'Classic Cart Studio Collection')+(n?` (${n} games)`:''),'.ccs'); }
function ccsPhone(){ return /Android|iPhone|iPad|iPod/i.test(navigator.userAgent)||(navigator.maxTouchPoints>1&&/Macintosh/.test(navigator.userAgent)); }
// ---- the modal for ROM acknowledgments: centered, blurred and dimmed background, focus kept inside, nothing behind reachable ----
function ccsAck(o){ return new Promise(res=>{ let m=$('ccsAck'); if(!m){ m=document.createElement('div'); m.id='ccsAck'; m.className='ccsack'; m.setAttribute('role','dialog'); m.setAttribute('aria-modal','true'); m.setAttribute('aria-labelledby','ccsAckT'); document.body.appendChild(m); }
  m.innerHTML=`<div class="mbox ccsackbox"><h2 id="ccsAckT">${esc(o.title)}</h2>${o.body.map(p=>`<p>${esc(p)}</p>`).join('')}<label class="ccsackchk"><input type="checkbox" id="ccsAckC"> <span>${esc(o.check)}</span></label>
   <div class="acts">${o.actions.map((a,i)=>`<button class="btn ${a.primary?'hk':a.secondary?'secondary':''}" data-i="${i}" ${a.needsAck?'disabled':''}>${esc(a.label)}</button>`).join('')}</div></div>`;
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
// ---- Share or Export: one complete package, saved (Export .ccs) or handed to the device's share sheet (Send to Phone / Share Games) ----
const CCS={cid:null, ids:[], titles:{}, ready:[], missing:[], mismatched:[], busy:false, cancel:false};
function ccsShareDialog(cid){ csSaveActiveNow(); cid=cid||CS.active; const r=CS.items[cid]; if(!r) return; document.querySelectorAll('.menu').forEach(x=>x.hidden=true); CCS.cid=cid; CCS.subset=null; ccsCheck(); }
function ccsShareLabel(){ return ccsPhone()?'Share Games':'Send to Phone'; }
async function ccsCheck(){ const r=CS.items[CCS.cid]; if(!r) return; const p=await ccsCollectionParts(r); CCS.parts=p; const ids=CCS.subset||p.ids; CCS.ids=ids; CCS.titles=p.titles;
 openColl('Share or Export Collection', `"${r.name}" · ${ids.length} game${ids.length===1?'':'s'}`, '<div class="msgbox">Checking ROM files…</div>');
 if(ccsAppPackages()){ const chk=await hkPost('/api/package/rom-check', {games:ids.map(id=>({version_id:id, title:p.titles[id]||id}))});
  if(!chk.ok){ $('collBody').innerHTML=`<div class="msgbox bad">${esc(chk.error||'The ROM files could not be checked. Try again.')}</div>`; return; }
  CCS.ready=(chk.available||[]).map(g=>g.version_id); CCS.bytes=(chk.available||[]).reduce((a,g)=>a+(g.size||0),0); CCS.missing=(chk.missing||[]).map(g=>g.version_id); CCS.mismatched=(chk.mismatched||[]).map(g=>g.version_id); }
 else { CCS.ready=ids.filter(id=>ROMSRC.has(id)); CCS.missing=ids.filter(id=>!ROMSRC.has(id)); CCS.mismatched=[]; CCS.bytes=CCS.ready.reduce((a,id)=>a+ROMSRC.get(id).size,0); }
 ccsRender(); }
function ccsRender(){ const r=CS.items[CCS.cid]; const n=CCS.ids.length, need=CCS.missing.length+CCS.mismatched.length, ok=!need&&n>0; const app=ccsAppPackages(); const t=id=>esc(CCS.titles[id]||id);
 const list=arr0=>{ const arr=arr0.slice().sort((x,y)=>(CCS_ID_RE.test(x)?1:0)-(CCS_ID_RE.test(y)?1:0)); return `<ul class="small ccsmiss">${arr.slice(0,12).map(id=>`<li>${t(id)}${CCS_ID_RE.test(id)?'':' <i>(file unknown)</i>'}</li>`).join('')}${arr.length>12?`<li>and ${arr.length-12} more</li>`:''}</ul>`; };   /* entries with no known file come first: they can never be packaged */
 $('collBody').innerHTML=(ok?`<div class="msgbox ok"><b>${n.toLocaleString('en-US')} ROM file${n===1?' is':'s are'} ready</b> (${fmt(CCS.bytes)}). The package is made on this ${app?'computer':'device'}; nothing is uploaded.</div>`
   :!n?`<div class="msgbox warn">This collection has no games yet.</div>`
   :`<div class="msgbox warn"><b>${need} of ${n} game${n===1?'':'s'} ${need===1?'needs its':'need their'} ROM file.</b> A .ccs file always includes every game's ROM.${CCS.missing.length?list(CCS.missing):''}${CCS.mismatched.length?`<div class="small">A different version is on this computer for:</div>${list(CCS.mismatched)}`:''}</div>
    <div class="ccsfix">${app?'<button class="btn" id="ccsAdd">Add ROM Files…</button>':'<button class="btn" id="ccsPick">Choose ROM Files…</button>'}${!app&&typeof desktopAvailable==='function'&&desktopAvailable()?'<button class="btn ghost" id="ccsWinApp">Open in Windows App</button>':''}${CCS.ready.length?`<button class="btn ghost" id="ccsOnly">Share Only the ${CCS.ready.length} Ready Game${CCS.ready.length===1?'':'s'}</button>`:''}</div>
    ${app?'':'<p class="small">Choose the ROM files, a ZIP of them, or a .ccs file that has them. They are read on this device only.</p>'}`)
  +`<div class="acts"><span class="sp"></span><button class="btn" id="ccsNo">Cancel</button><button class="btn" id="ccsExp" ${ok?'':'disabled'}>Export .ccs</button><button class="btn hk" id="ccsSend" ${ok?'':'disabled'}>${ccsShareLabel()}</button></div>`;
 $('ccsNo').onclick=closeColl; $('ccsExp').onclick=()=>ccsMake('export'); $('ccsSend').onclick=()=>ccsMake('share');
 if($('ccsPick')) $('ccsPick').onclick=async()=>{ const fs=await ccsPickRoms(); if(!fs.length) return; $('ccsPick').disabled=true; $('ccsPick').textContent='Reading files…'; const res=await ccsAddRomSources(fs); await ccsCheck(); if(!res.found) toast('No ROM files were found in those files'); };
 if($('ccsAdd')) $('ccsAdd').onclick=()=>{ closeColl(); if(typeof importGames==='function') importGames('files'); };
 if($('ccsWinApp')) $('ccsWinApp').onclick=()=>{ closeColl(); if(typeof startHandoff==='function') startHandoff([CCS.cid]); };
 if($('ccsOnly')) $('ccsOnly').onclick=()=>{ CCS.subset=CCS.ready.slice(); ccsCheck(); }; }
async function ccsMake(mode){ const r=CS.items[CCS.cid]; if(!r) return; const a=await ccsAck(CCS_EXPORT_ACK); if(a!=='export') return;
 const sub=CCS.subset&&CCS.subset.length!==CCS.parts.ids.length; const rec=sub?ccsSubsetRecord(r, CCS.subset):r; const parts=sub?await ccsCollectionParts(rec):CCS.parts; const name=ccsName(r, sub?CCS.subset.length:0);
 CCS.cancel=false; $('collBody').innerHTML=`<div class="msgbox" id="ccsProg">Creating the package…</div><div class="acts"><span class="sp"></span><button class="btn" id="ccsStop">Cancel</button></div>`; $('ccsStop').onclick=()=>{ CCS.cancel=true; closeColl(); toast('Canceled; nothing was saved'); }; procSet('collm','running');
 const prog=(d,t)=>{ const p=$('ccsProg'); if(p) p.textContent=`Creating the package… ${d.toLocaleString('en-US')} of ${t.toLocaleString('en-US')} ROM files`; };
 if(ccsAppPackages()&&ccsAppJobs()){ const req={collection:parts.file, art:parts.artB64, roms:parts.ids.map(id=>({version_id:id, title:parts.titles[id]||id})), summary:parts.summary, job:true, save:mode==='export', name};
  if(mode==='export'){ $('collBody').innerHTML=ccsNativeWait(name); procSet('collm','native'); }
  const st=await hkPost('/api/package/export', req);
  if(st.canceled){ closeColl(); toast('Canceled; nothing was saved'); return; } if(!st.ok){ procSet('collm',null); return ccsJobFail(st.error); }
  const j=await ccsWatch(st.job, 'Creating '+st.name);
  if(j.phase==='canceled'){ closeColl(); toast('Export canceled; nothing was saved'); return; } if(j.phase!=='done'){ procSet('collm',null); return ccsJobFail(j.error); }
  if(mode==='export'){ ccsMarkSaved(parts); return ccsSavedView(j, parts); }
  return ccsDoneApp(mode, {id:j.stage_id, size:j.size}, name, parts); }
 if(ccsAppPackages()){ const ex=await hkPost('/api/package/export', {collection:parts.file, art:parts.artB64, roms:parts.ids.map(id=>({version_id:id, title:parts.titles[id]||id})), summary:parts.summary});
  if(CCS.cancel) return; if(!ex.ok){ $('collBody').innerHTML=`<div class="msgbox bad">The package was not created: ${esc(ex.error||'unknown error')}. Nothing was saved.</div><div class="acts"><span class="sp"></span><button class="btn" id="ccsOk">OK</button></div>`; $('ccsOk').onclick=closeColl; return; }
  return ccsDoneApp(mode, ex, name, parts); }
 let blob; try{ blob=await ccsBuild(parts, prog, ()=>CCS.cancel); }catch(e){ if(CCS.cancel) return; $('collBody').innerHTML=`<div class="msgbox bad">The package was not created: ${esc(e.message||String(e))}. Nothing was saved.</div><div class="acts"><span class="sp"></span><button class="btn" id="ccsOk">OK</button></div>`; $('ccsOk').onclick=closeColl; return; }
 if(CCS.cancel) return; ccsDoneWeb(mode, blob, name, parts); }
function ccsMarkSaved(parts){ if(CCS.cid===CS.active&&!LO&&!(CCS.subset&&CCS.subset.length!==CCS.parts.ids.length)){ COLL.savedToFile={at:new Date().toISOString(), fp:collFingerprint()}; saveColl(); if(typeof updateStatusLine==='function') updateStatusLine(); } }
function ccsDoneMsg(parts, size){ return `<b>${parts.ids.length} game${parts.ids.length===1?'':'s'}</b>, every ROM file included (${fmt(size)}).`; }
// sharing advice names no services: the device's own share options list what it actually has
const CCS_SEND_TIP='Share it with your usual messaging, email, cloud storage or file-transfer app.';
const CCS_SEND_AFTER='Save the .ccs file, then share it with your usual messaging, email, cloud storage or file-transfer app.';
const CCS_SEND_WIN='Share it with your usual messaging, email, cloud storage or file-transfer app, or right-click it in File Explorer and choose <b>Share</b>.';
// whether this browser or app can hand a file to the system's share options (feature detection, never the device type)
function ccsCanShareFiles(f){ try{ return !!(navigator.canShare&&navigator.canShare({files:[f||new File([new Uint8Array(1)], 'test.ccs', {type:'application/octet-stream'})]})); }catch(e){ return false; } }
function ccsNativeWait(name){ return `<div class="msgbox" id="ccsProg"><b>Choose where to save ${esc(name)}.</b> The Windows Save dialog is open in its own window; this continues when you save or cancel there.</div>`; }
async function ccsDoneWeb(mode, blob, name, parts){ procSet('collm',null); const file=new File([blob], name, {type:blob.type}); const canShare=ccsCanShareFiles(file);
 if(mode==='export'){ closeColl(); const res=await ccsSaveFile(name, blob, 'Classic Cart Studio Package'); if(res.ok){ ccsMarkSaved(parts); toast(`Saved "${res.name}" (${parts.ids.length} games, ${fmt(blob.size)})`); } else toast('Canceled; nothing was saved'); return; }
 $('collBody').innerHTML=`<div class="msgbox ok">The package is ready: ${ccsDoneMsg(parts, blob.size)}</div>${canShare?'':`<div class="msgbox">This browser can't hand files to other apps. ${CCS_SEND_AFTER}</div>`}<div id="ccsShareOut"></div>
  <div class="acts"><span class="sp"></span><button class="btn" id="ccsSave">Save .ccs…</button>${canShare?`<button class="btn hk" id="ccsShare">Share…</button>`:''}</div>`;
 $('ccsSave').onclick=async()=>{ const res=await ccsSaveFile(name, blob, 'Classic Cart Studio Package'); if(res.ok){ ccsMarkSaved(parts); closeColl(); toast(`Saved "${res.name}"`); } };
 if($('ccsShare')) $('ccsShare').onclick=async()=>{ try{ await navigator.share({files:[file], title:name}); closeColl(); }
  catch(e){ if(e&&e.name==='AbortError') return; $('ccsShareOut').innerHTML=`<div class="msgbox warn">The app you picked didn't take the file${blob.size>25e6?' (it may be too large for it)':''}. Your package is complete: save it, or share it with another app.</div>`; } }; }
// ---- export jobs in the Windows app: progress from the Background Helper, Cancel honored at its next 1 MB checkpoint ----
function ccsAppJobs(){ return ((COMPANION.info&&COMPANION.info.capabilities)||COMPANION.capabilities||[]).includes('package-jobs'); }
function ccsShown(){ return $('collm').classList.contains('open'); }
function ccsJobFail(err){ const m=String(err||'unknown error'); if(!ccsShown()){ toast('The package was not created'); return; }
 $('collBody').innerHTML=`<div class="msgbox bad">The package was not created: ${esc(m.replace(/\.$/,''))}${/Nothing was saved/.test(m)?'.':'. Nothing was saved.'}</div><div class="acts"><span class="sp"></span><button class="btn" id="ccsOk">OK</button></div>`; $('ccsOk').onclick=closeColl; }
// the counts are finished work: games whose ROM file has been checked or completely written into the package; the bytes are the ROM
// bytes read so far (checking) or written so far (packaging), so they keep moving while one large game is in progress
function ccsJobDraw(j){ const L=$('ccsJobLine'), B=$('ccsJobBar'), Y=$('ccsJobBytes'); if(!L||!B||!Y) return; const n=x=>Number(x||0).toLocaleString('en-US'); let pct=null, line='Starting…', by='';
 if(j.phase==='verify'){ line=`Checking ROM files: ${n(j.games_done)} of ${n(j.games)} done`; pct=j.games?j.games_done/j.games:null; by=j.bytes_done?fmt(j.bytes_done)+' read':''; }
 else if(j.phase==='write'){ line=`Packaging games: ${n(j.games_done)} of ${n(j.games)} done`; pct=j.bytes_total?j.bytes_done/j.bytes_total:null; by=`${fmt(j.bytes_done)} of ${fmt(j.bytes_total)} written`; }
 else if(j.phase==='check'){ line='Checking the finished package…'; procSet('collm','finalizing'); }
 else if(j.phase==='save'){ line='Saving the package…'; pct=j.bytes_total?j.bytes_done/j.bytes_total:null; by=`${fmt(j.bytes_done)} of ${fmt(j.bytes_total)} saved`; }
 if(j.cancel_requested) line='Canceling…';
 L.textContent=line; B.classList.toggle('ind', pct==null); B.firstElementChild.style.width=pct==null?'':Math.min(100, pct*100).toFixed(1)+'%'; Y.textContent=by||' '; }
async function ccsWatch(job, title){ CCS.job=job; procSet('collm','running');
 $('collBody').innerHTML=`<div class="ccsjob" role="status" aria-live="polite"><b class="ccsjt">${esc(title)}</b><div id="ccsJobLine">Starting…</div><div class="pbar ind" id="ccsJobBar"><i></i></div><div class="small" id="ccsJobBytes">&nbsp;</div></div><div class="acts"><span class="sp"></span><button class="btn" id="ccsStop">Cancel</button></div>`;
 const stop=()=>{ if(PROC.collm==='canceling') return; procSet('collm','canceling'); const b=$('ccsStop'); if(b){ b.disabled=true; b.textContent='Canceling…'; } hkPost('/api/package/cancel', {job}); };
 $('ccsStop').onclick=stop;   // the only way to stop it: the dialog stays until the job confirms canceled (or done/failed)
 let j; for(;;){ const r=await hkPost('/api/package/job', {job}); if(!r.ok||!r.job){ j={phase:'error', error:r.error||'The Background Helper stopped answering'}; break; }
  j=r.job; ccsJobDraw(j); if(j.phase==='done'||j.phase==='error'||j.phase==='canceled') break; await new Promise(res=>setTimeout(res, 300)); }
 procSet('collm',null); CCS.job=null; return j; }
function ccsSavedView(j, parts){ if(!ccsShown()){ toast(`Saved "${j.saved_name}" (${fmt(j.size)})`); return; }
 $('collBody').innerHTML=`<div class="msgbox ok">Saved <b>${esc(j.saved_name)}</b>: ${ccsDoneMsg(parts, j.size)}</div><p class="small ccspath">${esc(j.saved_path||'')}</p><p class="small">${CCS_SEND_WIN}</p>
  <div class="acts"><span class="sp"></span><button class="btn" id="ccsShow">Show in Folder</button><button class="btn hk" id="ccsOk">Done</button></div>`;
 $('ccsOk').onclick=closeColl; $('ccsShow').onclick=async()=>{ const r=await hkPost('/api/package/reveal', {id:j.id}); if(!r.ok) toast(r.error||'File Explorer could not be opened'); }; }
async function ccsDoneApp(mode, ex, name, parts){ let saved=null;
 const save=async()=>{ $('collBody').innerHTML=ccsNativeWait(name); procSet('collm','native'); const s=await hkPost('/api/package/save', ccsAppJobs()?{id:ex.id, name, job:true}:{id:ex.id, name}); if(!s.job) procSet('collm',null);
  if(s.ok&&s.job){ const j=await ccsWatch(s.job, 'Saving '+s.name); if(j.phase==='done'){ saved={name:j.saved_name, path:j.saved_path}; ccsMarkSaved(parts); return true; } toast(j.phase==='canceled'?'Canceled; nothing was saved':(j.error||'The package was not saved')); return false; }
  if(s.ok){ saved=s; ccsMarkSaved(parts); return true; } if(!s.canceled) toast(s.error||'The package was not saved'); return false; };
 if(mode==='export'){ closeColl(); if(await save()) toast(`Saved "${saved.name}" (${parts.ids.length} games, ${fmt(ex.size)})`); return; }
 const canShare=ccsCanShareFiles();
 const draw=()=>{ $('collBody').innerHTML=`<div class="msgbox ok">The package is ready: ${ccsDoneMsg(parts, ex.size)}</div>${saved?`<div class="msgbox">Saved as <b>${esc(saved.name)}</b>.${saved.path?`<div class="small ccspath">${esc(saved.path)}</div>`:''} ${CCS_SEND_WIN}</div>`:`<p class="small">${CCS_SEND_AFTER}</p>`}<div id="ccsShareOut"></div>
   <div class="acts"><span class="sp"></span>${saved?'<button class="btn" id="ccsShow">Show in Folder</button>':'<button class="btn hk" id="ccsSave">Save .ccs…</button>'}${canShare?'<button class="btn" id="ccsShare">Share…</button>':''}<button class="btn" id="ccsOk">Done</button></div>`;
  $('ccsOk').onclick=closeColl; if($('ccsSave')) $('ccsSave').onclick=async()=>{ await save(); if(ccsShown()) draw(); }; if($('ccsShow')) $('ccsShow').onclick=async()=>{ const r=await hkPost('/api/package/reveal', {id:ex.id}); if(!r.ok) toast(r.error||'File Explorer could not be opened'); };
  if($('ccsShare')) $('ccsShare').onclick=async()=>{ try{ const rr=await fetch(CO.base+'/api/package/file?id='+encodeURIComponent(ex.id), lnaInit({headers:hdrs(), cache:'no-store'})); if(!rr.ok) throw new Error('HTTP '+rr.status); const f=new File([await rr.blob()], name, {type:'application/vnd.classiccartstudio.collection+zip'});
    if(!navigator.canShare({files:[f]})) throw new Error('unsupported'); await navigator.share({files:[f], title:name}); }
   catch(e){ if(e&&e.name==='AbortError') return; $('ccsShareOut').innerHTML=`<div class="msgbox warn">That didn't work here${ex.size>25e6?' (the file may be too large for the app)':''}. Save the package and share it from File Explorer instead.</div>`; } }; };
 draw(); }
// ---- Import .ccs Collection ----
function ccsImportPick(){ const inp=document.createElement('input'); inp.type='file'; inp.accept='.ccs,application/json,.json'; inp.onchange=()=>{ const f=inp.files[0]; if(f) ccsOpenFile(f); }; inp.click(); }
async function ccsOpenFile(f){ if(/\.json$/i.test(f.name)){ const t=await f.text(); let o; try{ o=JSON.parse(t); }catch(e){ toast('That file is not a collection file'); return; } if(o&&o.schema===SBACKUP_SCHEMA){ csRestoreDialog(o, f.name); return; } csImportObject(o, f.name); return; }
 if(CO.desktop&&ccsAppPackages()){   // the app verifies the package (and its ROM files) in its own folder first
  const ac=new AbortController(); openColl('Import .ccs Collection', f.name, '<div class="msgbox">Checking the package…</div><div class="acts"><span class="sp"></span><button class="btn" id="ccsStop">Cancel</button></div>');
  procSet('collm','running'); $('ccsStop').onclick=()=>{ ac.abort(); closeColl(); toast('Canceled; nothing was imported'); };   // the helper deletes a partly received package
  const up=await fetch(CO.base+'/api/package/stage', lnaInit({method:'POST', headers:hdrs({'Content-Type':'application/octet-stream'}), body:f, cache:'no-store', signal:ac.signal})).then(r=>r.json()).catch(e=>({ok:false, aborted:e&&e.name==='AbortError', error:String(e)}));
  if(up.aborted) return; procSet('collm',null); if(!up.ok){ ccsFail(f.name, up.error||'The package could not be checked.'); return; } return ccsPreview(ccsFromStaged(up), f.name); }
 let stop=false; openColl('Import .ccs Collection', f.name, `<div class="msgbox" id="ccsProg">Checking the package…</div><div class="acts"><span class="sp"></span><button class="btn" id="ccsStop">Cancel</button></div>`); $('ccsStop').onclick=()=>{ stop=true; closeColl(); }; procSet('collm','running');
 let info; try{ info=await ccsInspect(f, {cancel:()=>stop, progress:(d,t)=>{ const p=$('ccsProg'); if(p) p.textContent=`Checking ROM files… ${d.toLocaleString('en-US')} of ${t.toLocaleString('en-US')}`; }}); }catch(e){ if(!stop) ccsFail(f.name, e.message); return; }
 if(stop) return; procSet('collm',null); if(!info.ok){ ccsFail(f.name, 'It is incomplete or damaged: '+info.problems.slice(0,3).map(p=>`${p.path} ${p.problem}`).join('; ')+(info.problems.length>3?` (and ${info.problems.length-3} more)`:'')+'.'); return; }
 ccsPreview(Object.assign(info, {source:'file', file:f}), f.name); }
function ccsFromStaged(d){ return {manifest:{created:d.created}, kind:d.kind, data:d.data, art:{}, roms:d.roms||[], problems:d.problems||[], size:d.size, ok:d.ok!==false&&!(d.problems||[]).length, romBytes:d.rom_bytes||0, source:'staged', id:d.id}; }
function ccsFail(name, why){ openColl('Import .ccs Collection', name||'', `<div class="msgbox bad"><b>This package cannot be opened.</b> ${esc(why||'')}</div><p class="small">Nothing was imported.</p><div class="acts"><span class="sp"></span><button class="btn" id="ccsOk">OK</button></div>`); $('ccsOk').onclick=closeColl; }
function ccsCounts(info){ const rec=(info.data&&info.data.ccs_collection)||{}; return {name:rec.name||info.data.name||'Imported collection', games:(rec.sel||[]).length||(info.data.selections||[]).length, loadouts:(rec.loadouts||[]).length}; }
function ccsPreview(info, fname){ if(!info.ok){ ccsFail(fname, 'It is incomplete or damaged: '+(info.problems||[]).slice(0,3).map(p=>`${p.path} ${p.problem}`).join('; ')+'.'); return; }
 const c=ccsCounts(info); const roms=info.roms.length; const web=!CO.desktop; const canRoms=!web&&ccsAppPackages()&&info.source==='staged';
 const rows=[['Collection',esc(c.name)],['Games',String(c.games)],['NES Classic Loadouts',String(c.loadouts)],['ROM files',`${roms} verified (${fmt(info.romBytes)})`],['Package size', fmt(info.size)]];
 openColl('Import .ccs Collection', fname||'', `<dl class="kv">${rows.map(([k,v])=>`<dt>${k}</dt><dd>${v}</dd>`).join('')}</dl>
  ${web?`<div class="msgbox">The website checks the ROM files but does not keep them. Keep this .ccs file: to share or export the collection again, choose it when asked.</div>`:''}
  <p class="small">It is added as a new collection; nothing here is replaced.</p>
  <div class="acts">${web&&typeof desktopAvailable==='function'&&desktopAvailable()?`<button class="btn" id="ccsWin">Open in Windows App</button>`:''}<span class="sp"></span><button class="btn" id="ccsNo">Cancel</button><button class="btn hk" id="ccsGo">${web?'Save to My Collections':canRoms?'Import…':'Import Collection'}</button></div>`);
 $('ccsNo').onclick=closeColl; if($('ccsWin')) $('ccsWin').onclick=()=>ccsToApp(info.file, fname);
 $('ccsGo').onclick=async()=>{ if(canRoms){ const a=await ccsAck(CCS_IMPORT_ACK); if(a==='cancel') return; if(a==='with'){ const ok=await ccsImportRoms(info); if(!ok) return; } }
  if(info.source==='staged'&&info.id&&ccsAppPackages()){ const ka=await hkPost('/api/package/keep-art', {id:info.id}); if(ka&&ka.ok) info.art=ka.art||{}; }   /* the package's artwork for its personal games, kept in the app's own folder */
  if(web&&info.zip){ for(const x of info.roms) if(x.verified) ccsSrcAdd({name:x.path, ext:x.ext, sha256:x.sha256, content_sha256:x.content_sha256, size:x.size, raw:{blob:await info.zip.raw(x.entry), method:x.entry.method, crc:x.entry.crc, csize:x.entry.csize, usize:x.entry.usize}}); }
  closeColl(); const r=ccsSaveCollection(info); if(!r) return;
  openColl('Collection Imported', '', `<div class="msgbox ok">"${esc(r.name)}" (${(r.sel||[]).length} game${(r.sel||[]).length===1?'':'s'}${(r.loadouts||[]).length?`, ${(r.loadouts||[]).length} NES Classic Loadout${(r.loadouts||[]).length===1?'':'s'}`:''}) is in My Collections.${info._romResult?` ${info._romResult.imported.length} ROM file${info._romResult.imported.length===1?' was':'s were'} added to this computer.`:canRoms?' Saved without its ROM files.':''}</div>${info._romResult&&info._romResult.rejected.length?`<div class="msgbox warn">${info._romResult.rejected.length} file${info._romResult.rejected.length===1?' was':'s were'} not used: ${esc(info._romResult.rejected.map(x=>x.reason).join('; '))}</div>`:''}<div class="acts"><button class="btn ghost" id="ccsStay">Stay Here</button><span class="sp"></span><button class="btn hk" id="ccsOpen">Open It Now</button></div>`);
  $('ccsStay').onclick=closeColl; $('ccsOpen').onclick=()=>{ closeColl(); csSwitch(r.id); }; }; }
async function ccsImportRoms(info){ $('collBody').querySelectorAll('.acts button').forEach(b=>b.disabled=true); $('collBody').insertAdjacentHTML('beforeend','<div class="msgbox" id="ccsBusy">Checking and adding the ROM files… This can take a while for a large package.</div>');
 procSet('collm','running'); const r=await hkPost('/api/package/import-roms', {id:info.id}); procSet('collm',null); const b=$('ccsBusy'); if(b) b.remove(); $('collBody').querySelectorAll('.acts button').forEach(b=>b.disabled=false);
 if(!r.ok){ $('collBody').insertAdjacentHTML('beforeend', `<div class="msgbox bad">The ROM files were not imported: ${esc(r.error||'unknown error')}. You can still import the collection without them.</div>`); return false; }
 info._romResult=r; await refreshPersonal(); return true; }
// a new, independent collection from a package (every entry kept; artwork of games not on this computer kept with their entries)
function ccsSaveCollection(info){ const d=info.data||{}; const rec0=d.ccs_collection; if(!rec0||!Array.isArray(rec0.sel)){ csImportObject(d, 'package'); return csActive(); }
 const {rec}=csRecordFromFile(rec0, rec0.name); const sels=Object.fromEntries((d.selections||[]).map(s=>[s.version_id||s.game_id, s]));
 const kept=[]; const given=(rec0.coll&&typeof rec0.coll==='object'&&rec0.coll.missing&&typeof rec0.coll.missing==='object')?rec0.coll.missing:{};
 rec.sel.forEach(id=>{ const s=sels[id]; let m=given[id]?csCleanMissing(given[id], id):null; if(s) m=csMergeEntry(m, csEntryFromSelection(s, d.personal_library), id); if(!m) m=rec.coll.missing[id]||null;
  if(m&&info.art&&info.art[id]&&!m.thumb_url) m.thumb_url=csThumbOk(info.art[id]);
  if(byId[id]){ if(m&&/^p_[0-9a-f]{16}$/.test(id)) kept.push([id, m]); delete rec.coll.missing[id]; return; }   /* on this computer: its details go to the personal store */
  if(m) rec.coll.missing[id]=m; });
 Object.keys(rec.coll.missing).forEach(id=>{ if(!rec.sel.includes(id)) delete rec.coll.missing[id]; });
 const r=csCreate(rec.name, rec); if(kept.length) csMergePersonal(kept).then(n=>{ if(n) refreshPersonal().then(()=>{ if(typeof render==='function') render(); }); }); return r; }
// the website hands a package to Classic Cart Studio on this computer (local only, allow-listed origin), then opens the app on it
async function ccsToApp(file, fname){ if(!file){ ccsAppFallback(fname); return; }
 const post=()=>fetch(CO.base+'/api/handoff/package', lnaInit({method:'POST', headers:{'Content-Type':'application/octet-stream','X-NES-Protocol':String(CO.REQ_PROTOCOL)}, body:file, cache:'no-store'})).then(r=>r.json()).catch(()=>({ok:false, unreachable:true}));
 openColl('Open in Windows App', fname||'', '<div class="msgbox">Looking for Classic Cart Studio on this computer…</div>');
 let r=await post();
 if(r.unreachable){ ccsLaunch('classiccartstudio://open'); $('collBody').innerHTML='<div class="msgbox">Opening Classic Cart Studio… If your browser asks, choose <b>Open</b>. Waiting up to a minute.</div>';
  $('collBody').insertAdjacentHTML('beforeend','<div class="acts"><span class="sp"></span><button class="btn" id="ccsStop">Cancel</button></div>'); procSet('collm','running'); $('ccsStop').onclick=closeColl;
  for(let i=0;i<(window.CCS_WAIT_TRIES||20)&&r.unreachable;i++){ await new Promise(s=>setTimeout(s, 3000)); if(!$('collm').classList.contains('open')) return; r=await post(); } procSet('collm',null); }
 if(!r.ok){ ccsAppFallback(fname, r.unreachable?'':r.error); return; }
 ccsLaunch('classiccartstudio://package?offer='+encodeURIComponent(r.id));
 $('collBody').innerHTML=`<div class="msgbox ok"><b>Sent to Classic Cart Studio on this computer.</b> Switch to its window; it asks before importing anything.</div><div class="acts"><span class="sp"></span><button class="btn" id="ccsOk">Done</button></div>`; $('ccsOk').onclick=closeColl; }
function ccsAppFallback(fname, err){ openColl('Open in Windows App', fname||'', `<div class="msgbox warn">${err?esc(/[.!?]$/.test(err)?err:err+'.')+' Classic Cart Studio could not open this package.':'Classic Cart Studio for Windows did not answer on this computer.'}</div><ol class="small"><li>Install Classic Cart Studio (below) if needed, and start it.</li><li>Double-click <b>${esc(fname||'the .ccs file')}</b> in File Explorer, or choose <b>Open in Windows App</b> here again.</li></ol><div class="msgbox" id="dl"><b>Download Classic Cart Studio</b><div class="small">Looking up the latest release…</div></div><div class="acts"><span class="sp"></span><button class="btn" id="ccsOk">Close</button></div>`); if(typeof renderDownload==='function') renderDownload($('dl')); $('ccsOk').onclick=closeColl; }
function ccsLaunch(u){ let f=$('ccsLaunch'); if(!f){ f=document.createElement('iframe'); f.id='ccsLaunch'; f.setAttribute('aria-hidden','true'); f.style.cssText='display:none;width:0;height:0;border:0'; document.body.appendChild(f); } try{ f.src=u; }catch(e){} }
// Studio data backups stay JSON files (collections.js); importing .ccs packages and backups shares one picker
csImportPick=function(){ ccsImportPick(); };
csExport=function(id){ return ccsShareDialog(id||CS.active); };
// ---- links and files handed over by Windows (protocol, file association) and by the website ----
window.__ccsLink=function(kind, value){ if(kind==='share'){ openColl('Shared Collection Links','', `<div class="msgbox">Shared collection links are no longer used. Ask the sender for the collection's .ccs file and open it here.</div><div class="acts"><span class="sp"></span><button class="btn" id="ccsOk">OK</button></div>`); $('ccsOk').onclick=closeColl; return; }
 const go=()=>{ if(kind==='package'&&/^[A-Za-z0-9_-]{16,40}$/.test(value)){ capi('/api/package/preview?id='+encodeURIComponent(value)).then(d=>d.ok?ccsPreview(ccsFromStaged(d), 'from ClassicCartStudio.com'):ccsFail('', d.error)); }
  else if(kind==='file'&&/\.ccs$/i.test(value)){ openColl('Import .ccs Collection', value.split(/[\\/]/).pop(), '<div class="msgbox">Checking the package…</div>'); hkPost('/api/package/open', {path:value}).then(d=>d.ok?ccsPreview(ccsFromStaged(d), d.file_name):ccsFail(value.split(/[\\/]/).pop(), d.error)); } };
 if(CO.ok) go(); else { let n=0; const t=setInterval(()=>{ if(CO.ok||++n>120){ clearInterval(t); if(CO.ok) go(); } }, 500); } };
(window.__ccsLinkQ||[]).forEach(x=>window.__ccsLink(x[0], x[1])); window.__ccsLinkQ=[];
// drag and drop a .ccs onto the page
document.addEventListener('dragover', e=>{ if([...(e.dataTransfer&&e.dataTransfer.items||[])].some(i=>i.kind==='file')){ e.preventDefault(); document.body.classList.add('ccsdrop'); } });
document.addEventListener('dragleave', e=>{ if(!e.relatedTarget) document.body.classList.remove('ccsdrop'); });
document.addEventListener('drop', e=>{ const f=e.dataTransfer&&e.dataTransfer.files&&e.dataTransfer.files[0]; document.body.classList.remove('ccsdrop'); if(f&&/\.(ccs|json)$/i.test(f.name)){ e.preventDefault(); ccsOpenFile(f); } });
