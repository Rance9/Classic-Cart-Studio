// ---- Play in the browser (W1): the game player as WebAssembly. play/ccs-player.wasm is the SAME session, emulator (FCEUmm, pinned),
// saves and settings code as the Windows player (tools/player, host_web.c), built by the same pinned toolchain; this runtime gives it
// what a window gives it on Windows: the picture, sound, the keyboard, controllers, touch controls and storage. Nothing here reaches a
// network: the engine and its fonts come from this site; the games, saves and settings stay in this browser's own storage.
//   CCSROMS   the private game store (IndexedDB "ccs-play", by exact content identity): a gameplay copy of each game, NOT a backup
//   CPV       the save store: a memory copy of the saved files for the running game; each write is staged at once and the player
//             reports it written only after IndexedDB confirmed it (a failed write is undone and said)
//   CCSPLAY   one session at a time: start(game, surface), the frame loop (paced by the sound clock), input ownership, pop-out moves
const CPL={db:null, dbp:null, mod:null, modp:null, s:null, fonts:null};
const CP_DB='ccs-play', CP_VER=1;
function cpDb(){ if(CPL.db) return Promise.resolve(CPL.db); if(CPL.dbp) return CPL.dbp;
  CPL.dbp=new Promise((res,rej)=>{ let q; try{ q=indexedDB.open(CP_DB, CP_VER); }catch(e){ CPL.dbp=null; rej(new Error('This browser does not allow this site to keep games (storage is blocked, for example in a private window).')); return; }
    q.onupgradeneeded=()=>{ const d=q.result; if(!d.objectStoreNames.contains('files')) d.createObjectStore('files',{keyPath:'path'}); if(!d.objectStoreNames.contains('roms')) d.createObjectStore('roms',{keyPath:'sha'}); if(!d.objectStoreNames.contains('meta')) d.createObjectStore('meta',{keyPath:'k'}); };
    q.onsuccess=()=>{ CPL.db=q.result; CPL.db.onversionchange=()=>{ try{ CPL.db.close(); }catch(e){} CPL.db=null; CPL.dbp=null; }; res(CPL.db); };
    q.onerror=()=>{ CPL.dbp=null; rej(new Error('This browser could not open its storage for games ('+((q.error&&q.error.name)||'unknown')+').')); };
    q.onblocked=()=>{}; });
  return CPL.dbp; }
function cpTx(stores, mode, fn){ return cpDb().then(db=>new Promise((res,rej)=>{ let t, out;
  try{ t=db.transaction(stores, mode, mode==='readwrite'?{durability:'strict'}:undefined); }catch(e){ rej(e); return; }
  t.oncomplete=()=>res(out); t.onerror=()=>rej(t.error||new Error('storage error')); t.onabort=()=>rej(t.error||new Error('the browser stopped the write'));
  try{ out=fn(t); }catch(e){ try{ t.abort(); }catch(_){} rej(e); } })); }
function cpReq(r){ return new Promise((res,rej)=>{ r.onsuccess=()=>res(r.result); r.onerror=()=>rej(r.error); }); }
function cpWhyStore(e){ const n=(e&&e.name)||''; return n==='QuotaExceededError'?'This browser has no room left for it (its storage for this site is full). Free space by removing games here, or by clearing other site data, then try again.'
  :n==='InvalidStateError'||n==='UnknownError'?'This browser\'s storage for this site is not available right now (it may be in a private window, or storage was cleared).':'This browser could not write it'+(n?' ('+n+')':'')+'.'; }
// a read that failed is never taken for an empty store: the caller stops and says so (what: "its saved games", "the games it keeps here")
function cpReadError(e, what){ const n=(e&&e.name)||''; const x=new Error(`This browser could not read ${what} (${n==='InvalidStateError'||n==='UnknownError'?'its storage for this site is not available right now':n?n:'its storage did not answer'}). Nothing was changed. Try again.`); x.code='read'; return x; }

// ---- the private game store: exact bytes, by content identity (SHA-256 of the bytes after the iNES header and trainer) ----
async function cpSha256(bytes){ const h=await crypto.subtle.digest('SHA-256', bytes); return [...new Uint8Array(h)].map(b=>b.toString(16).padStart(2,'0')).join(''); }
async function cpContentSha(u8){ const hd=u8.length>=16&&u8[0]===0x4E&&u8[1]===0x45&&u8[2]===0x53&&u8[3]===0x1A, skip=hd?16+((u8[6]&4)?512:0):0; return cpSha256(u8.subarray(Math.min(skip,u8.length))); }
const CCSROMS={
  // keeps a gameplay copy of a game file whose content identity is already known (want: the expected content SHA-256; a different file is refused)
  async put(u8, info){ const sha=await cpContentSha(u8); if(info&&info.sha&&info.sha!==sha) throw new Error('This file is a different version of the game (its content does not match), so it was not kept.');
    const rec={sha, bytes:u8.slice().buffer, size:u8.length, title:(info&&info.title)||'', name:(info&&info.name)||'', header_hex:(info&&info.header_hex)||'', added:new Date().toISOString()};
    try{ await cpTx(['roms'],'readwrite',t=>{ t.objectStore('roms').put(rec); }); }catch(e){ throw new Error(cpWhyStore(e)); }
    CCSROMS._have=null; CCSROMS.keys().then(()=>{ if(typeof keptAvailChanged==='function') keptAvailChanged(); }); return sha; },   /* cards of kept games follow */
  // has, get and keys(true) throw when the store could not be read (cpReadError): "not kept here" is said only after a read that worked
  async has(sha){ try{ return !!(await cpTx(['roms'],'readonly',t=>cpReq(t.objectStore('roms').getKey(sha)))); }catch(e){ throw cpReadError(e, 'the games it keeps for playing here'); } },
  async get(sha){ let r; try{ r=await cpTx(['roms'],'readonly',t=>cpReq(t.objectStore('roms').get(sha))); }catch(e){ throw cpReadError(e, 'the games it keeps for playing here'); } return r||null; },
  // the cards' "ready to play here" marks: a failed read shows none this time and is asked again next time (never remembered as empty)
  async keys(strict){ if(CCSROMS._have) return CCSROMS._have; try{ const k=await cpTx(['roms'],'readonly',t=>cpReq(t.objectStore('roms').getAllKeys())); CCSROMS._have=new Set(k); }catch(e){ if(strict) throw cpReadError(e, 'the games it keeps for playing here'); return new Set(); } return CCSROMS._have; },
  async remove(sha){ await cpTx(['roms'],'readwrite',t=>{ t.objectStore('roms').delete(sha); }); CCSROMS._have=null; },
  async estimate(){ try{ return navigator.storage&&navigator.storage.estimate?await navigator.storage.estimate():null; }catch(e){ return null; } },
  // asks the browser to keep this site's storage (it may say no; Safari decides on its own): true when it is kept
  async persist(){ try{ if(!navigator.storage||!navigator.storage.persist) return false; if(await navigator.storage.persisted()) return true; return await navigator.storage.persist(); }catch(e){ return false; } },
  async persisted(){ try{ return !!(navigator.storage&&navigator.storage.persisted&&await navigator.storage.persisted()); }catch(e){ return false; } },
};

// ---- the save store for one session: /data/... files in memory, each write committed to IndexedDB in order ----
const CPV={files:new Map(), ro:new Map(), q:[], busy:false, fail:null, pending:0,
  async preload(sha){ CPV.files=new Map(); CPV.fail=null; CPV.pending=0; CPV.q=[];
    const want=p=>p.startsWith('/data/saves/'+sha+'/')||p.startsWith('/data/game-settings/')||p==='/data/player-pads.json'||p==='/data/player-assign.json'||p==='/data/player-settings.json'||p==='/data/player-display.json';
    let all; try{ all=await cpTx(['files'],'readonly',t=>cpReq(t.objectStore('files').getAll())); }catch(e){ throw cpReadError(e, 'its saved games and settings'); }   /* the game never starts on an unread store */
    for(const f of all||[]) if(want(f.path)) CPV.files.set(f.path, new Uint8Array(f.bytes)); },
  get(p){ return CPV.files.get(p)||CPV.ro.get(p)||null; },
  write(p, u8){ if(!p.startsWith('/data/')) return -1; const before=CPV.files.has(p)?CPV.files.get(p):null; CPV.files.set(p, u8); CPV._q({op:'put', p, u8, before}); return 0; },
  remove(p){ if(!CPV.files.has(p)) return -1; const before=CPV.files.get(p); CPV.files.delete(p); CPV._q({op:'del', p, before}); return 0; },
  renameNew(a, b){ if(!CPV.files.has(a)||CPV.files.has(b)) return -1; const u8=CPV.files.get(a); CPV.files.delete(a); CPV.files.set(b, u8); CPV._q({op:'move', p:a, to:b, u8}); return 0; },
  _q(job){ CPV.q.push(job); CPV.pending++; CPV._run(); },
  async _run(){ if(CPV.busy) return; CPV.busy=true;
    while(CPV.q.length){ const j=CPV.q.shift();
      try{ if(CPV.failNext){ CPV.failNext--; await null; throw Object.assign(new Error('test'),{name:'QuotaExceededError'}); }   // tests: the next writes fail as a full disk does (later, as the browser's own refusal comes)
        await cpTx(['files'],'readwrite',t=>{ const st=t.objectStore('files'); const at=new Date().toISOString();
          if(j.op==='put') st.put({path:j.p, bytes:j.u8.slice().buffer, at}); else if(j.op==='del') st.delete(j.p); else { st.delete(j.p); st.put({path:j.to, bytes:j.u8.slice().buffer, at}); } }); }
      catch(e){ CPV.fail=cpWhyStore(e);   // undone in memory as well, unless a later write replaced it already
        if(j.op==='put'){ if(CPV.files.get(j.p)===j.u8){ if(j.before) CPV.files.set(j.p, j.before); else CPV.files.delete(j.p); } }
        else if(j.op==='del'){ if(!CPV.files.has(j.p)) CPV.files.set(j.p, j.before); }
        else if(CPV.files.get(j.to)===j.u8){ CPV.files.delete(j.to); CPV.files.set(j.p, j.u8); }
        cpTrace({ev:'store_write_failed', path:j.p.replace(/[0-9a-f]{64}/,'<sha>'), why:(e&&e.name)||''}); }
      CPV.pending--; }
    CPV.busy=false; },
};

// ---- the engine: one compiled module, a fresh instance per session (the emulator keeps global state) ----
function cpAsset(p){ return (typeof CP_ASSET_BASE==='string'?CP_ASSET_BASE:'play/')+p; }
async function cpModule(){ if(CPL.mod) return CPL.mod; if(CPL.modp) return CPL.modp;
  CPL.modp=(async()=>{ const r=await fetch(cpAsset('ccs-player.wasm'),{cache:'force-cache'}); if(!r.ok) throw new Error('The game player could not be loaded ('+r.status+').');
    const b=await r.arrayBuffer(); CPL.mod=await WebAssembly.compile(b); return CPL.mod; })().catch(e=>{ CPL.modp=null; throw e; });
  return CPL.modp; }
async function cpFonts(){ if(CPL.fonts) return CPL.fonts; const names=['silk17','sans700_30','sans600_22','sans400_17']; const m=new Map();
  await Promise.all(names.flatMap(n=>[n+'.ccsf', n+'-2x.ccsf']).map(async f=>{ const r=await fetch(cpAsset('fonts/'+f),{cache:'force-cache'}); if(r.ok) m.set('/fonts/'+f, new Uint8Array(await r.arrayBuffer())); }));
  CPL.fonts=m; return m; }
const CP_TRACE=[]; function cpTrace(o){ CP_TRACE.push(o); if(CP_TRACE.length>4000) CP_TRACE.splice(0,1000); }
function cpUtf8(mem, p){ const u=new Uint8Array(mem.buffer); let e=p; while(u[e]) e++; return new TextDecoder().decode(u.subarray(p,e)); }
function cpPutStr(mem, p, n, s){ const b=new TextEncoder().encode(s); const k=Math.min(b.length, n-1); new Uint8Array(mem.buffer).set(b.subarray(0,k), p); new Uint8Array(mem.buffer)[p+k]=0; }
// "Nov 6, 2026, 4:38:16 AM UTC-6": the same text the Windows player shows (gp_util.c gp_local_time), in this browser's time zone
function cpLocalTime(iso){ const d=new Date(iso); if(isNaN(d)) return null; const M=['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
  const h=d.getHours(), h12=h%12||12, off=-d.getTimezoneOffset(), a=Math.abs(off), z='UTC'+(off<0?'-':'+')+Math.floor(a/60)+(a%60?':'+String(a%60).padStart(2,'0'):'');
  return `${M[d.getMonth()]} ${d.getDate()}, ${d.getFullYear()}, ${h12}:${String(d.getMinutes()).padStart(2,'0')}:${String(d.getSeconds()).padStart(2,'0')} ${h<12?'AM':'PM'} ${z}`; }

// WASI preview1, read-only: the engine's C library opens its fonts and the ROM through fopen; everything else it needs goes through "ccs"
function cpWasi(S){ const E=52, BADF=8, NOENT=44; const fds=new Map(); let next=4; const mem=()=>S.mem; const dv=()=>new DataView(mem().buffer);
  const pathAt=(p,n)=>'/'+new TextDecoder().decode(new Uint8Array(mem().buffer,p,n)).replace(/^\/+/,'');
  const w={
    args_sizes_get:(a,b)=>{ dv().setUint32(a,0,true); dv().setUint32(b,0,true); return 0; }, args_get:()=>0,
    environ_sizes_get:(a,b)=>{ dv().setUint32(a,0,true); dv().setUint32(b,0,true); return 0; }, environ_get:()=>0,
    clock_time_get:(id,prec,out)=>{ const ns=BigInt(Math.round((id===0?Date.now():performance.now())*1e6)); dv().setBigUint64(out,ns,true); return 0; },
    clock_res_get:(id,out)=>{ dv().setBigUint64(out,1000n,true); return 0; },
    random_get:(p,n)=>{ crypto.getRandomValues(new Uint8Array(mem().buffer,p,n)); return 0; },
    proc_exit:(c)=>{ throw new Error('the game player stopped ('+c+')'); }, sched_yield:()=>0,
    fd_prestat_get:(fd,out)=>{ if(fd!==3) return BADF; const d=dv(); d.setUint8(out,0); d.setUint32(out+4,1,true); return 0; },
    fd_prestat_dir_name:(fd,p,n)=>{ if(fd!==3) return BADF; new Uint8Array(mem().buffer)[p]=47; return 0; },
    fd_fdstat_get:(fd,out)=>{ const d=dv(); d.setUint8(out, fd<=2?2:fd===3?3:4); d.setUint16(out+2,0,true); d.setBigUint64(out+8,0xffffffffn,true); d.setBigUint64(out+16,0xffffffffn,true); return fds.has(fd)||fd<=3?0:BADF; },
    fd_fdstat_set_flags:()=>0,
    path_open:(dirfd,dflags,p,n,oflags,rb,ri,fdflags,out)=>{ const path=pathAt(p,n); if(oflags&(1|8)) return 76;   /* no creating or truncating: read-only */
      const data=CPV.get(path); if(!data) return NOENT; const fd=next++; fds.set(fd,{data,pos:0}); dv().setUint32(out,fd,true); return 0; },
    path_filestat_get:(dirfd,fl,p,n,out)=>{ const data=CPV.get(pathAt(p,n)); if(!data) return NOENT; const d=dv(); for(let i=0;i<64;i+=8) d.setBigUint64(out+i,0n,true); d.setUint8(out+16,4); d.setBigUint64(out+32,BigInt(data.length),true); return 0; },
    fd_filestat_get:(fd,out)=>{ const f=fds.get(fd); if(!f) return BADF; const d=dv(); for(let i=0;i<64;i+=8) d.setBigUint64(out+i,0n,true); d.setUint8(out+16,4); d.setBigUint64(out+32,BigInt(f.data.length),true); return 0; },
    fd_read:(fd,iov,cnt,out)=>{ const f=fds.get(fd); if(!f) return BADF; const d=dv(); let tot=0;
      for(let i=0;i<cnt;i++){ const p=d.getUint32(iov+i*8,true), l=d.getUint32(iov+i*8+4,true), k=Math.min(l, f.data.length-f.pos); if(k<=0) break; new Uint8Array(mem().buffer,p,k).set(f.data.subarray(f.pos,f.pos+k)); f.pos+=k; tot+=k; if(k<l) break; }
      d.setUint32(out,tot,true); return 0; },
    fd_seek:(fd,off,wh,out)=>{ const f=fds.get(fd); if(!f) return BADF; const o=Number(off); f.pos=wh===0?o:wh===1?f.pos+o:f.data.length+o; if(f.pos<0) f.pos=0; dv().setBigUint64(out,BigInt(f.pos),true); return 0; },
    fd_tell:(fd,out)=>{ const f=fds.get(fd); if(!f) return BADF; dv().setBigUint64(out,BigInt(f.pos),true); return 0; },
    fd_close:(fd)=>{ fds.delete(fd); return 0; },
    fd_write:(fd,iov,cnt,out)=>{ const d=dv(); let tot=0, s=''; for(let i=0;i<cnt;i++){ const p=d.getUint32(iov+i*8,true), l=d.getUint32(iov+i*8+4,true); s+=new TextDecoder().decode(new Uint8Array(mem().buffer,p,l)); tot+=l; }
      if(fd===1||fd===2) cpTrace({ev:'stdio', text:s.slice(0,400)}); d.setUint32(out,tot,true); return 0; },
  };
  return new Proxy(w,{ get:(o,k)=>k in o?o[k]:(()=>E) }); }

// ---- keyboard, controllers, touch: what the person holds now ----
// The player decides who plays which player, as on Windows (host_web.c: connection order, Swap Players 1 and 2, Choose Player 1's or 2's
// Controller, remembered in player-assign.json): every connected controller goes to it with its state, and the keyboard's two sets of keys
// and the touch controls go to it unrouted (the keyboard plays the player no controller plays: gp_keyboard_players). The browser HOME reads
// the same controllers (cpPads, cpPadActs): any controller moves through the HOME, as on the console.
const CP_ACT=['up','down','left','right','a','b','select','start'];
const CP_KEYS_DEFAULT={p1:{up:'ArrowUp',down:'ArrowDown',left:'ArrowLeft',right:'ArrowRight',a:'KeyZ|Enter',b:'KeyX|Backspace',select:'KeyS',start:'KeyM'}, p2:{up:'KeyI',down:'KeyK',left:'KeyJ',right:'KeyL',a:'KeyH',b:'KeyG',select:'KeyT',start:'KeyY'}};
// controllers this player knows without setup (F17, D-99). The owner's NES Classic Edition controller (CLV-002) through its USB adapter
// (USB 056E:2007, reported as a joystick: A = button 4, B = button 3, SELECT 9, START 10, the D-pad as the hat; recorded in the final8
// physical session, MILESTONES-2-3-FINAL16-CORRECTIONS.md) is the first entry; the Windows player has the same table (gp_pads.c)
const CP_KNOWN=[{vid:0x056e, pid:0x2007, map:[100,102,103,101,3,2,8,9]}];   // [up, down, left, right, A, B, SELECT, START] input codes, as gp_pads.c
// then the SDL_GameControllerDB table (play/pad-db.json, the same one the Windows player is built with), loaded when a controller without a
// standard layout first appears
function cpKnown(vid, pid){ const k=CP_KNOWN.find(x=>x.vid===vid&&x.pid===pid); if(k) return k.map;
 if(CPL.padDb===undefined){ CPL.padDb=null; fetch(cpAsset('pad-db.json'),{cache:'force-cache'}).then(r=>r.ok?r.json():null).then(j=>{ CPL.padDb=j&&j.pads||{}; }).catch(()=>{ CPL.padDb={}; }); }
 const m=CPL.padDb&&CPL.padDb[vid.toString(16).padStart(4,'0')+'-'+pid.toString(16).padStart(4,'0')]; return m||null; }
function cpVidPid(id){ let m=/Vendor:\s*([0-9a-f]{4})\s*Product:\s*([0-9a-f]{4})/i.exec(id||'')||/^([0-9a-f]{1,4})-([0-9a-f]{1,4})-/i.exec(id||''); return m?[parseInt(m[1],16), parseInt(m[2],16)]:null; }
function cpXboxLike(gp){ return /xinput|xbox|045e/i.test(gp.id||''); }
function cpHat(gp){ for(let i=gp.axes.length-1;i>=4;i--){ const v=gp.axes[i]; if(v>1.05&&v<1.5) return -1; if(i===9&&v>=-1.01&&v<=1.01) return Math.round((v+1)*3.5)%8*4500; } return null; }
const CPI={keys:new Set(), touch:{}, pads:new Map(), map:null, nextId:1, saved:{}, savedSrc:null};
const CP_PAD_BYTES=4*4+32+4+6*4+8*4+8;   // one controller in web_in (host_web.c web_pad)
function cpKeyMap(){ if(CPI.map) return CPI.map; let s=null; try{ const f=CPV.get('/data/player-settings.json'); if(f) s=JSON.parse(new TextDecoder().decode(f)); }catch(e){}
  const k=(s&&s.keys)||{}; const m={}; for(const p of ['p1','p2']) for(const a of CP_ACT){ const v=((k[p]||{})[a]!=null)?cpWinKeyToCode(k[p][a]):CP_KEYS_DEFAULT[p][a]; for(const c of String(v||'').split('|')) if(c) m[c]=[p,a]; }
  return CPI.map=m; }
function cpWinKeyToCode(n){ if(!n) return ''; return String(n).split('|').map(x=>x.length===1&&/[A-Za-z]/.test(x)?'Key'+x.toUpperCase():x.length===1&&/[0-9]/.test(x)?'Digit'+x:({Up:'ArrowUp',Down:'ArrowDown',Left:'ArrowLeft',Right:'ArrowRight',Enter:'Enter',Backspace:'Backspace',Space:'Space',Tab:'Tab',Shift:'ShiftLeft',LShift:'ShiftLeft',RShift:'ShiftRight',Ctrl:'ControlLeft',LCtrl:'ControlLeft',RCtrl:'ControlRight',Comma:'Comma',Period:'Period',Slash:'Slash',Semicolon:'Semicolon',Quote:'Quote',Minus:'Minus',Equals:'Equal'})[x]||x).join('|'); }
// the controller setups saved in this browser (player-pads.json, the Windows file format: a controller model -> 8 input codes): while a
// game plays, its own copy (a setup made in its menu counts at once); otherwise the stored file as last read (cpPadMapsLoad). A failed
// read keeps what was known; it never makes a set-up controller look new.
function cpPadMaps(){ if(CPL.s){ const f=CPV.get('/data/player-pads.json'); if(f!==CPI.savedSrc){ CPI.savedSrc=f; let m={}; try{ if(f) m=JSON.parse(new TextDecoder().decode(f)); }catch(e){} CPI.saved=m&&typeof m==='object'?m:{}; } } return CPI.saved||{}; }
function cpPadMapsLoad(){ return cpTx(['files'],'readonly',t=>cpReq(t.objectStore('files').get('/data/player-pads.json'))).then(f=>{ if(CPL.s) return; let m={}; try{ if(f) m=JSON.parse(new TextDecoder().decode(new Uint8Array(f.bytes))); }catch(e){} CPI.saved=m&&typeof m==='object'?m:{}; CPI.savedSrc=null; },()=>{}); }
// every connected controller, in connection order: {id, key (its model: a setup is saved by it), name, kind, configured, ...}
//   kind 1  the standard layout (the browser knows it): its actions are read here, as Windows reads an Xbox-compatible controller; no setup
//   kind 2  any other: its raw state (raw) and its mapping (map): the setup saved in this browser, else a known layout (CP_KNOWN, then the
//           SDL table), else none (configured false: its directions work, a button press opens its setup in the game player)
function cpPads(win){ let list=[]; try{ list=[...((win||window).navigator.getGamepads?(win||window).navigator.getGamepads():[])].filter(Boolean); }catch(e){}
  const seen=new Set(), maps=cpPadMaps();
  for(const gp of list){ const k=gp.index+':'+gp.id; seen.add(k); let st=CPI.pads.get(k); if(!st){ st={id:CPI.nextId++}; CPI.pads.set(k,st); cpTrace({ev:'pad_connected', id:st.id, mapping:gp.mapping||'none'}); }
    const b=i=>!!(gp.buttons[i]&&(gp.buttons[i].pressed||gp.buttons[i].value>0.5)), vp=cpVidPid(gp.id); st.gp=gp;
    st.key='web:'+(vp?vp[0].toString(16).padStart(4,'0')+'-'+vp[1].toString(16).padStart(4,'0'):String(gp.id||'').slice(0,40));
    if(gp.mapping==='standard'){ const x=cpXboxLike(gp); st.kind=1; st.configured=true; st.setup='standard'; st.raw=null; st.map=null; st.menu=b(16);
      st.act={up:b(12)||gp.axes[1]<-0.5, down:b(13)||gp.axes[1]>0.5, left:b(14)||gp.axes[0]<-0.5, right:b(15)||gp.axes[0]>0.5, a:x?(b(0)||b(3)):(b(1)||b(3)), b:x?(b(1)||b(2)):(b(0)||b(2)), select:b(8), start:b(9)}; }
    else { const sv=maps[st.key], saved=Array.isArray(sv)&&sv.length===8&&sv.every(Number.isInteger), kn=saved?null:vp?cpKnown(vp[0],vp[1]):null;
      st.kind=2; st.act=null; st.menu=false; st.map=saved?sv:kn?kn:[-1,-1,-1,-1,1,0,8,9]; st.configured=!!(saved||kn); st.setup=saved?'saved':kn?'known':'none';
      st.raw={btn:Array.from({length:32},(_,i)=>b(i)?1:0), pov:cpHat(gp)??-1, axis:[0,1,2,3,4,5].map(i=>Math.round((gp.axes[i]||0)*1000))}; } }
  for(const [k,st] of CPI.pads) if(!seen.has(k)){ CPI.pads.delete(k); cpTrace({ev:'pad_disconnected', id:st.id}); }
  const out=[...CPI.pads.values()], n={};   // the person's word for each, numbered when two read the same (never the product's own name)
  for(const p of out){ const w=p.kind===1&&cpXboxLike(p.gp)?'Xbox-compatible controller':'game controller'; n[w]=(n[w]||0)+1; p.name=n[w]>1?w+' '+n[w]:w; }
  return out; }
// what a controller holds now as NES actions, for the HOME (gp_pads.c gp_raw_actions: the mapping's input for each action; the directions
// always also from the hat and the stick; a controller that is not set up has no buttons here)
function cpInputOn(r, c){ if(c>=0&&c<32) return !!r.btn[c]; const h=r.pov; if(c>=100&&c<104){ if(h<0||h>=36000) return false; const d=c-100; return d===0?(h>=31500||h<=4500):d===1?(h>=4500&&h<=13500):d===2?(h>=13500&&h<=22500):(h>=22500&&h<=31500); }
 if(c>=200&&c<212){ const a=(c-200)>>1; return (c-200)%2?r.axis[a]>500:r.axis[a]<-500; } return false; }
function cpPadActs(p){ if(p.kind===1) return p.act; const o={}, HAT=[100,102,103,101], STICK=[202,203,200,201];
 CP_ACT.forEach((a,i)=>{ let on=p.map[i]>=0&&cpInputOn(p.raw,p.map[i]); if(i<4) on=on||cpInputOn(p.raw,HAT[i])||cpInputOn(p.raw,STICK[i]); else if(!p.configured) on=false; o[a]=on; }); return o; }

// ---- the session ----
// One game at a time in this browser (every tab, window and installed-app window shares one storage), and none while a backup is being
// restored into that storage. A session holds the Web Lock CP_LOCK from before it reads its files until its last write is stored (cpEnd);
// a restore takes the same lock for its whole run (full-backup.js fbWebLock). The browser grants it to one of them only, so whichever
// asks first goes ahead and the other is refused before it changes anything; a running game's own copy of its files stays true.
const CP_LOCK='ccs-play', CP_RESTORE_LOCK='ccs-restore';
function cpLock(name){ return new Promise((ok,no)=>{ navigator.locks.request(name,{ifAvailable:true},lk=>{ if(!lk){ ok(null); return; } return new Promise(r=>ok(r)); }).catch(no); }); }   // its release, or null: held elsewhere
async function cpLockHolder(){ try{ return ((await navigator.locks.query()).held||[]).some(l=>l.name===CP_RESTORE_LOCK)?'restore':'game'; }catch(e){ return 'game'; } }   // the wording only: the lock decided
async function cpLockBusy(){ const e=new Error((await cpLockHolder())==='restore'?'A backup is being restored in this browser. Try again when it has finished.':'A game is already open in another tab or window of this browser. Close it there first.'); e.code='busy'; return e; }
const CCSPLAY={
  supported(){ return typeof WebAssembly==='object'&&typeof indexedDB==='object'&&!!(window.crypto&&crypto.subtle); },
  running(){ return !!CPL.s; },
  // called from the Play tap itself, before anything asynchronous: browsers start sound only from a person's action
  unlockAudio(){ try{ const AC=window.AudioContext||window.webkitAudioContext; if(!AC) return; if(!CPL.ac){ CPL.ac=new AC({latencyHint:'interactive'}); } if(CPL.ac.state!=='running') CPL.ac.resume().catch(()=>{}); }catch(e){} },
  // a person's release (pointerup, touchend), click or return to the page: sound the browser kept waiting starts. A phone does not count a
  // touch press (the HOME's A acts on it), and an iPhone stops the sound while the page is in the background. Only a sound some game
  // already asked for (CPL.ac) is woken; none is ever started by this
  wakeAudio(){ if(CPL.ac&&CPL.ac.state!=='running') CCSPLAY.unlockAudio(); },
  // game: {id, title, version_label, sha (content SHA-256), header_hex}; surface: CCSPLAY.surface(); returns a promise of the session's result
  async start(game, surface, opts={}){
    if(CPL.s) throw new Error('A game is already open.');
    const rom=await CCSROMS.get(game.sha); if(!rom) { const e=new Error('This game is not ready to play in this browser yet: choose its file again (or restore your backup) so a copy can be kept here.'); e.code='no_rom'; throw e; }
    let release=null; if(navigator.locks&&navigator.locks.request){ release=await cpLock(CP_LOCK); if(!release) throw await cpLockBusy(); }
    try{
      const [mod, fonts]=await Promise.all([cpModule(), cpFonts()]); await CPV.preload(game.sha); CPV.ro=new Map(fonts); CPV.ro.set('/rom/game.nes', new Uint8Array(rom.bytes)); CPI.map=null;
      const sess={schema:'ccs-play-session/1', mode:'direct', saves_root:'/data/saves', fonts_dir:'/fonts', scratch_dir:'/run/scratch', result_path:'/run/result.json', settings_path:'/data/player-settings.json',
        display_path:'/data/player-display.json', game_settings_dir:'/data/game-settings', studio_pid:0, direct_id:game.id, home:{pack:'', state_dir:''},
        games:[{id:game.id, title:game.title||'', version_label:game.version_label||'', rom:'/rom/game.nes', header_hex:game.header_hex||rom.header_hex||'', content_sha256:game.sha}]};
      CPV.ro.set('/run/session.json', new TextEncoder().encode(JSON.stringify(sess)));
      const S={game, surface, mod, gen:(CCSPLAY._gen=(CCSPLAY._gen||0)+1), t0:performance.now(), frames:0, pushed:0, used:0, aq:0, last:null, ended:false, result:null, fail:null, view:0, owned:true, crc:[]};
      const imports={wasi_snapshot_preview1:cpWasi(S), ccs:cpImports(S)}; const inst=await WebAssembly.instantiate(mod, imports); S.inst=inst; S.x=inst.exports; S.mem=inst.exports.memory;
      if(S.x._initialize) S.x._initialize(); S.in=S.x.web_input();
      const sp=S.x.web_malloc(64); cpPutStr(S.mem, sp, 64, '/run/session.json'); const rc=S.x.web_open(sp); S.x.web_free(sp);
      if(rc){ throw new Error('The game player could not open this game ('+rc+').'); }
      CPL.s=S; S.release=release; release=null; surface.attach(S); cpInputBind(S, surface.win||window);
      return await new Promise(res=>{ S.done=res; cpLoop(S); });
    } finally { if(release) release(); } },
  // the X button / the menu key: the same paused game menu as SELECT + START (never saves or exits by itself)
  menu(){ const S=CPL.s; if(S&&(S.view===1||S.view===2)) S.menuKey=1; },   // only over a playing game (a menu that is open stays as it is)
  // the phone turned to portrait (the rotate screen covers everything): the game pauses in its menu, nothing stays held, no input reaches it
  hold(on){ const S=CPL.s; if(!S) return; S.blocked=!!on; if(on){ if(S.view===1||S.view===2) S.menuKey=1; CPI.keys.clear(); if(S.surface&&S.surface.releaseTouch) S.surface.releaseTouch(); } },
  // Pop Out and back: the same session draws on another surface (another window): its frame loop and input move with it, nothing restarts
  retarget(ns){ const S=CPL.s; if(!S||!ns) return; const old=S.surface; try{ (old.win||window).cancelAnimationFrame(S.raf); }catch(e){} S.surface=ns; ns.attach(S); try{ old.detach(S); }catch(e){}
    cpInputMove(ns.win||window); try{ S.x.web_redraw(); }catch(e){} if(!S.ended&&S.tick) S.raf=(ns.win||window).requestAnimationFrame(S.tick); },
  // a close request from the page (the surface closing): the session answers it as the Windows player answers its window's X
  requestClose(){ const S=CPL.s; if(S) S.closeReq=1; },
  focus(on){ const S=CPL.s; if(!S) return; if(on) S.focusGained=1; else { S.focusLost=1; cpClearHeld(); } S.focused=!!on; },
  view(){ const S=CPL.s; return S?S.view:-1; },
  debug(){ const S=CPL.s; return S?{frames:S.frames, view:S.view, crc:S.crc.slice(-8), aq:S.aq, pushed:S.pushed, used:S.used, peak:S.peak||0, audio:CPL.ac?CPL.ac.state:'none', audioReady:!!S.audioReady, lastMenu:S.lastMenu, pending:CPV.pending, fail:CPV.fail, trace:CP_TRACE.slice(-60)}:{trace:CP_TRACE.slice(-60)}; },
};
function cpClearHeld(){ CPI.keys.clear(); CPI.touch={}; }
function cpImports(S){ const mem=()=>S.mem;
  return {
    present:(kind,ptr,w,h,stride)=>{ S.surface.present(kind, new Uint8Array(mem().buffer), ptr, w, h, stride); if(kind===1&&S.debugCrc) S.crc.push(cpCrc16(new Uint8Array(mem().buffer, ptr, 256*h*2))); },
    audio_open:(rate)=>cpAudioOpen(S, rate), audio_push:(ptr,frames)=>cpAudioPush(S, ptr, frames), audio_queued:()=>Math.max(0, S.pushed-S.used), audio_pause:(p)=>cpAudioPause(S, p), audio_close:()=>cpAudioPause(S, 1),
    trace:(p,n)=>{ try{ const o=JSON.parse(new TextDecoder().decode(new Uint8Array(mem().buffer,p,n))); cpTrace(o); if(o.ev==='menu') S.lastMenu=o; }catch(e){} },
    set_title:()=>{}, set_picture:(tv,int)=>{ S.surface.picture(tv, int); }, cursor:(v)=>S.surface.cursor(v), fullscreen:(t)=>S.surface.fullscreen(t),
    pad_name:(id,out,n)=>{ const st=[...CPI.pads.values()].find(x=>x.id===id); cpPutStr(mem(), out, n, st?st.name:'controller'); }, pad_saved:()=>{},
    pad_key:(id,out,n)=>cpPutStr(mem(), out, n, (S.padKeys||{})[id]||('web-'+id)), text_input:(on)=>{ S.textMode=!!on; S.textQ=''; },
    fs_size:(p)=>{ const f=CPV.get(cpUtf8(mem(),p)); return f?f.length:-1; },
    fs_read:(p,dst,n)=>{ const f=CPV.get(cpUtf8(mem(),p)); if(!f) return -1; const k=Math.min(n,f.length); new Uint8Array(mem().buffer,dst,k).set(f.subarray(0,k)); return k; },
    fs_write:(p,src,n)=>{ const path=cpUtf8(mem(),p); if(!path.startsWith('/data/')&&!path.startsWith('/run/')) return -1; const u8=new Uint8Array(mem().buffer,src,n).slice();
      if(path.startsWith('/run/')){ CPV.ro.set(path,u8); return 0; } if(path==='/data/player-pads.json') CPI.map=null; return CPV.write(path,u8); },
    fs_remove:(p)=>CPV.remove(cpUtf8(mem(),p)), fs_rename_new:(a,b)=>CPV.renameNew(cpUtf8(mem(),a), cpUtf8(mem(),b)),
    io_pending:()=>CPV.pending, io_failed:(out,n)=>{ if(!CPV.fail) return 0; cpPutStr(mem(), out, n, CPV.fail); CPV.fail=null; return 1; },
    now_ms:()=>performance.now(), local_time:(iso,out,n)=>{ const t=cpLocalTime(cpUtf8(mem(),iso)); if(!t) return -1; cpPutStr(mem(), out, n, t); return 0; },
  }; }
function cpCrc16(u8){ let c=0xffff; for(let i=0;i<u8.length;i+=7) c=((c<<5)^(c>>>11)^u8[i])&0xffff; return c; }
// sound: the AudioContext the Play tap unlocked, an AudioWorklet that plays and reports what it consumed (the pacing clock)
function cpAudioOpen(S, rate){ const ac=CPL.ac; if(!ac) return -1; S.rate=rate;
  if(!CPL.wl){ CPL.wl=ac.audioWorklet?ac.audioWorklet.addModule(cpAsset('ccs-play-worklet.js')).then(()=>true,()=>false):Promise.resolve(false); }
  CPL.wl.then(ok=>{ if(!ok||S.ended) return; if(!S.node){ S.node=new AudioWorkletNode(ac,'ccs-player',{numberOfInputs:0, outputChannelCount:[2]}); S.node.connect(ac.destination);
    S.node.port.onmessage=e=>{ if(e.data.type==='used'){ S.lastC=e.data.used; S.used=S.pushedAtClear+Math.max(0, e.data.used-(S.cAtClear||0)); S.aq=e.data.queued; S.peak=Math.max(S.peak||0, e.data.peak); } }; S.pushedAtClear=0; S.cAtClear=0; }   // the worklet counts what it consumed since it started: only what came after the last clear counts
    S.node.port.postMessage({type:'rate', src:rate}); S.audioReady=true; });
  return 0; }
function cpAudioPush(S, ptr, frames){ if(!S.audioReady||!S.node||S.audioPaused||!CPL.ac||CPL.ac.state!=='running') return; const src=new Int16Array(S.mem.buffer, ptr, frames*2), f=new Float32Array(frames*2); for(let i=0;i<f.length;i++) f[i]=src[i]/32768;
  S.node.port.postMessage({type:'push', data:f},[f.buffer]); S.pushed+=frames; }
function cpAudioPause(S, p){ S.audioPaused=!!p; if(p) cpAudioClear(S); }
// what is queued is dropped (silence at once) and the pacing starts over from here. k3e: the worklet's consumed count is not reset by a
// clear, so it was counted again on top of what was pushed, and after a SELECT+START hold the game ran up to four frames per display frame
function cpAudioClear(S){ if(!S.node) return; S.node.port.postMessage({type:'clear'}); S.pushedAtClear=S.pushed; S.cAtClear=S.lastC||0; S.used=S.pushed; }
// the loop: a game frame per step while it plays, paced by the sound the worklet consumed (about 3 frames ahead, as on Windows) or by
// the clock while sound is not running; a menu or message is stepped once per display frame
function cpLoop(S){ const tick=S.tick=()=>{ if(S.ended) return;
    const x=S.x; let n=0; const now=performance.now(); cpFeed(S);
    S.view=x.web_view(); const playing=S.view===1; cpSoundGate(S, playing, now);
    if(playing){ const fps=60.0988, ahead=Math.round((S.rate||48000)/fps*3);
      if(S.audioReady&&CPL.ac&&CPL.ac.state==='running'&&!S.audioPaused){ while(S.pushed-S.used<ahead&&n<4){ if(!cpStep(S)) return; n++; cpFeed(S); if(x.web_view()!==1) break; } S.clockT=null; }
      else { if(S.clockT==null) S.clockT=now; const due=Math.min(4, Math.floor((now-S.clockT)*fps/1000)); for(let i=0;i<due;i++){ if(!cpStep(S)) return; n++; cpFeed(S); if(x.web_view()!==1) break; } S.clockT+=due*1000/fps; if(now-S.clockT>200) S.clockT=now; } }
    else { if(!cpStep(S)) return; }
    S.raf=(S.surface.win||window).requestAnimationFrame(tick); };
  S.raf=(S.surface.win||window).requestAnimationFrame(tick); }
// a game playing while the browser keeps its sound waiting (started by a touch press, or stopped in the background): after half a second
// "Tap for game sound" shows over it, and it hides once the sound runs. Not over a menu, a paused sound or a browser without sound. While
// the sound waits nothing is queued for it (cpAudioPush) and the game keeps the clock's pace; when it runs again the pacing starts over
// (a queue built while it waited delayed the game, and one larger than the worklet's ring stopped it for good)
function cpSoundGate(S, playing, now){ const running=!!CPL.ac&&CPL.ac.state==='running';
  if(S.audioReady&&!running) S.soundHeld=true; else if(running&&S.soundHeld){ S.soundHeld=false; cpAudioClear(S); }
  const wait=playing&&S.audioReady&&!S.audioPaused&&!running;
  if(!wait){ S.silentT=0; S.surface.sound(false); return; } if(!S.silentT) S.silentT=now; S.surface.sound(now-S.silentT>=500); }
function cpStep(S){ let r; try{ r=S.x.web_step(); }catch(e){ S.fail='The game player stopped: '+(e&&e.message||e); cpTrace({ev:'engine_error', why:String(e&&e.message||e)}); r=0; }
  if(S.view===1) S.frames++; if(r) return true; cpEnd(S); return false; }
function cpEnd(S){ if(S.ended) return; S.ended=true; try{ (S.surface.win||window).cancelAnimationFrame(S.raf); }catch(e){}
  try{ S.x.web_close(); }catch(e){} let res=null; try{ const f=CPV.ro.get('/run/result.json'); if(f) res=JSON.parse(new TextDecoder().decode(f)); }catch(e){}
  cpAudioPause(S,1); try{ if(S.node) S.node.disconnect(); }catch(e){} cpInputUnbind(S); S.surface.detach(S);
  const finish=()=>{ CPL.s=null; if(S.release) S.release(); S.done({result:res, error:S.fail, writes_failed:CPV.fail}); };
  // the last writes (the cartridge save at exit) must reach storage before the page says the game closed
  const wait=()=>{ if(CPV.pending&&performance.now()-S.endT<15000) setTimeout(wait,50); else finish(); }; S.endT=performance.now(); wait(); }
// what the runtime hands the engine before each step
function cpFeed(S){ const mem=new DataView(S.mem.buffer), b=S.in, down=new Uint8Array(S.mem.buffer, b, 16); down.fill(0);
  const owns=S.focused!==false&&S.owned!==false&&!S.blocked;
  const pads=cpPads(S.surface.win), km=cpKeyMap();
  if(owns){ for(const c of CPI.keys){ const m=km[c]; if(m) down[(m[0]==='p2'?8:0)+CP_ACT.indexOf(m[1])]=1; }   // the keyboard's main keys (0..7) and second keys (8..15): the player routes them
    CP_ACT.forEach((a,i)=>{ if(CPI.touch[a]) down[i]=1; });   // the touch controls play as the keyboard's main keys do
    for(const p of pads) if(p.menu&&!p.menuWas){ S.menuKey=1; } for(const p of pads) p.menuWas=p.menu; }
  // events wait in web_in until a step reads them (the session clears them): a feed with no step after it (the sound queue already full)
  // must not lose a key press, so each one is added to what is still there
  let o=b+16; const set=(v)=>{ mem.setInt32(o,v,true); o+=4; }, or=(v)=>{ mem.setInt32(o, mem.getInt32(o,true)|v, true); o+=4; };
  or(S.closeReq|0); or(S.menuKey|0); or(S.focusLost|0); or(S.focusGained|0); or(0); set(owns?1:0); S.closeReq=S.menuKey=S.focusLost=S.focusGained=0;
  const had=Math.min(16, mem.getInt32(o,true)); const pts=(S.pt||[]).slice(0, 16-had); S.pt=[]; set(had+pts.length); o+=had*16; for(let i=had;i<16;i++){ const p=pts[i-had]||[0,0,0,0]; for(let k=0;k<4;k++) set(p[k]); }
  // every controller (four at most; host_web.c web_pad): id, connected, set up, kind (cpPads), raw state, mapping, and a kind 1 controller's actions
  const list=pads.slice(0,4); set(list.length); S.padKeys={};
  for(let i=0;i<4;i++){ const p=list[i]; if(!p){ o+=CP_PAD_BYTES; continue; }
    set(p.id); set(1); set(p.configured?1:0); set(p.kind); const u=new Uint8Array(S.mem.buffer, o, 32); u.fill(0); if(p.raw) p.raw.btn.forEach((v,k)=>{ if(k<32) u[k]=v; }); o+=32;
    set(p.raw?p.raw.pov:-1); for(let k=0;k<6;k++) set(p.raw?p.raw.axis[k]:0); for(let k=0;k<8;k++) set(p.map?p.map[k]:-1);
    const ac=new Uint8Array(S.mem.buffer, o, 8); CP_ACT.forEach((a,k)=>{ ac[k]=p.kind===1&&p.act[a]?1:0; }); o+=8; S.padKeys[p.id]=p.key; }
  set(0);   // pads_changed
  const tb=new Uint8Array(S.mem.buffer, o+4, 32), had2=Math.min(31, mem.getInt32(o,true)); const tq=(S.textQ||'').slice(0,31-had2); S.textQ=''; set(had2+tq.length); for(let i=0;i<tq.length;i++) tb[had2+i]=tq.charCodeAt(i)&0x7f; tb[had2+tq.length]=0; }
// the mouse over the picture (menus only; a game never sees it): stage pixels through the canvas's real box (letterbox, size, zoom)
function cpPointer(S, kind, ev, delta){ const c=S.surface.menuCanvas(); if(!c) return; const r=c.getBoundingClientRect(); if(!r.width||!r.height) return;
  const x=ev.clientX-r.left, y=ev.clientY-r.top; const sx=x<0||y<0||x>=r.width||y>=r.height?-1:Math.floor(x*1280/r.width), sy=sx<0?-1:Math.floor(y*720/r.height);
  if(kind===0){ if(S.lastPt&&S.lastPt[0]===ev.clientX&&S.lastPt[1]===ev.clientY) return; S.lastPt=[ev.clientX,ev.clientY]; }   // a movement that did not move (F3): never a choice
  (S.pt=S.pt||[]).push([kind, sx, sy, delta|0]); }
function cpInputBind(S, win){ const kd=e=>{ if(!CPL.s||CPL.s!==S||S.blocked) return; if(e.target&&e.target.closest&&e.target.closest('input,textarea,select,[contenteditable]')) return;
    if(e.code==='Escape'){ if(!e.repeat) S.menuKey=1; e.preventDefault(); return; }
    if(S.textMode&&!/^Arrow/.test(e.code)){ const t=e.key==='Backspace'?'\b':e.key==='Enter'?'\n':e.key.length===1&&e.key.charCodeAt(0)>=32&&e.key.charCodeAt(0)<127?e.key:''; if(t){ S.textQ=(S.textQ||'')+t; e.preventDefault(); e.stopPropagation(); } return; }   // naming a slot: typed keys are text
    const m=cpKeyMap()[e.code]; if(m){ CPI.keys.add(e.code); e.preventDefault(); e.stopPropagation(); } };
  const ku=e=>{ if(CPI.keys.delete(e.code)) { e.preventDefault(); } };
  const bl=()=>CCSPLAY.focus(false), fo=()=>CCSPLAY.focus(true), vis=()=>{ if(win.document.visibilityState==='hidden') CCSPLAY.focus(false); else CCSPLAY.wakeAudio(); }, ps=()=>CCSPLAY.wakeAudio();   // back in front: the sound goes on where the browser allows it (else Tap for game sound, cpSoundGate)
  win.addEventListener('keydown',kd,true); win.addEventListener('keyup',ku,true); win.addEventListener('blur',bl); win.addEventListener('focus',fo); win.document.addEventListener('visibilitychange',vis); win.addEventListener('pagehide',bl); win.addEventListener('pageshow',ps);
  S.unbind=()=>{ win.removeEventListener('keydown',kd,true); win.removeEventListener('keyup',ku,true); win.removeEventListener('blur',bl); win.removeEventListener('focus',fo); win.document.removeEventListener('visibilitychange',vis); win.removeEventListener('pagehide',bl); win.removeEventListener('pageshow',ps); };
  S.focused=win.document.hasFocus(); }
function cpInputUnbind(S){ if(S.unbind) S.unbind(); S.unbind=null; cpClearHeld(); }
// moves the session's input to another window (pop-out and back): nothing held carries over
function cpInputMove(win){ const S=CPL.s; if(!S) return; cpInputUnbind(S); cpInputBind(S, win); S.focusGained=1; }

// ---- the surface: where the picture is drawn (the Preview's TV area, a pop-out window, a phone's full screen) ----
// one canvas for the game (256 x 240, scaled with the game's proportions, nearest pixels, black around it) and one for the player's
// menus (2560 x 1440: the 1280 x 720 stage at twice its size, smooth fonts); touch controls on phones; X opens the game menu
function cpSurface(host, opts={}){ const doc=host.ownerDocument, win=doc.defaultView;
  const el=doc.createElement('div'); el.className='cpsurf'+(opts.full?' cpfull':''); el.innerHTML='<canvas class="cpgame" width="256" height="240"></canvas><canvas class="cpmenu" width="2560" height="1440" hidden></canvas>'
    +'<div class="cpbrand" aria-hidden="true">CLASSIC CART <span>STUDIO</span></div><i class="cpsafe" aria-hidden="true"></i>'
    +'<button type="button" class="cpx" aria-label="Game menu (the game pauses)" title="Game menu">✕</button>'
    +'<div class="cptouch" aria-hidden="true"><div class="cpdpad"><i class="u"></i><i class="r"></i><i class="d"></i><i class="l"></i></div><div class="cpss"><b data-k="select">SELECT</b><b data-k="start">START</b></div><div class="cpab"><b data-k="b">B</b><b data-k="a">A</b></div></div>'
    +'<div class="cpsound" hidden><button type="button" class="btn">Tap for game sound</button></div>';
  host.appendChild(el); const cg=el.querySelector('.cpgame'), cm=el.querySelector('.cpmenu'), gx=cg.getContext('2d'), mx=cm.getContext('2d'); const img=gx.createImageData(256,240), mimg=mx.createImageData(2560,1440);
  let tv=1, intg=0, last=0;
  // a phone's whole screen (cpfull with touch controls): the game's picture keeps its proportions between the two control columns (D-pad and
  // SELECT at the left, A/B and START at the right) and inside the safe areas, so no control covers it; the surround stays black
  const fit=()=>{ const r=el.getBoundingClientRect(); let W=r.width, H=r.height; if(!W||!H) return; const par=tv?8/7:1;
      let gw=256*par, gh=240;
      if(opts.full&&win.matchMedia('(pointer:coarse)').matches){ const c=win.getComputedStyle(el.querySelector('.cpsafe')), sl=parseFloat(c.paddingLeft)||0, sr=parseFloat(c.paddingRight)||0, st=parseFloat(c.paddingTop)||0, sb=parseFloat(c.paddingBottom)||0;
        const col=176, side=Math.max(sl,sr)+col; H=Math.max(1,H-st-sb); el.style.setProperty('--cp-col',col+'px'); if(W-2*side>=0.6*H*gw/gh) W=W-2*side; }
      let s=Math.min(W/gw, H/gh); if(intg&&s>=1) s=Math.floor(s); cg.style.width=Math.round(gw*s)+'px'; cg.style.height=Math.round(gh*s)+'px';
      const ms=Math.min(W/1280, H/720); cm.style.width=Math.round(1280*ms)+'px'; cm.style.height=Math.round(720*ms)+'px'; };
  const ro=new win.ResizeObserver(fit); ro.observe(el);
  const surf={el, win, doc,
    attach(S){ surf.S=S; el.classList.add('on'); fit(); },
    detach(){ surf.S=null; el.classList.remove('on'); },
    present(kind, u8, ptr, w, h, stride){ if(kind===1){ const d=img.data; for(let y=0;y<h;y++){ let si=ptr+y*stride*2, di=y*256*4; for(let x=0;x<w;x++,si+=2,di+=4){ const v=u8[si]|(u8[si+1]<<8); const r=v>>11, g=(v>>5)&63, b=v&31; d[di]=(r<<3)|(r>>2); d[di+1]=(g<<2)|(g>>4); d[di+2]=(b<<3)|(b>>2); d[di+3]=255; } }
        gx.putImageData(img,0,0); if(last!==1){ cg.hidden=false; cm.hidden=true; last=1; el.dataset.view='game'; } }
      else { const d=mimg.data; if(w!==2560||h!==1440){ return; } for(let i=0,si=ptr,di=0;i<w*h;i++,si+=3,di+=4){ d[di]=u8[si]; d[di+1]=u8[si+1]; d[di+2]=u8[si+2]; d[di+3]=255; } mx.putImageData(mimg,0,0); if(last!==2){ cm.hidden=false; cg.hidden=true; last=2; el.dataset.view='menu'; } } },
    picture(t, i){ tv=t; intg=i; fit(); }, cursor(v){ el.classList.toggle('nocursor', !v); }, fullscreen(t){ if(t){ const f=doc.fullscreenElement; if(f) doc.exitFullscreen().catch(()=>{}); else if(el.requestFullscreen) el.requestFullscreen().catch(()=>{}); } return doc.fullscreenElement?1:0; },
    menuCanvas(){ return last===2?cm:null; },
    sound(on){ const b=el.querySelector('.cpsound'); if(b.hidden===!on) return; b.hidden=!on; },   // Tap for game sound (cpSoundGate)
    remove(){ ro.disconnect(); el.remove(); } };
  // the menus by mouse; the game by keys, controllers and the touch controls only
  cm.addEventListener('pointermove',e=>{ if(e.pointerType==='mouse'&&surf.S) cpPointer(surf.S,0,e); });
  cm.addEventListener('click',e=>{ if(surf.S) cpPointer(surf.S,e.detail>=2?2:1,e); });
  cm.addEventListener('contextmenu',e=>{ e.preventDefault(); if(surf.S) cpPointer(surf.S,3,e); });
  cm.addEventListener('wheel',e=>{ e.preventDefault(); if(surf.S) cpPointer(surf.S,4,e, e.deltaY<0?1:-1); },{passive:false});
  el.querySelector('.cpx').addEventListener('click',()=>CCSPLAY.menu());
  el.querySelector('.cpsound button').addEventListener('click',()=>{ CCSPLAY.unlockAudio(); el.querySelector('.cpsound').hidden=true; if(surf.S) surf.S.silentT=performance.now(); });   // shown again only if the sound still waits half a second later
  cpTouch(surf, el.querySelector('.cptouch'));
  return surf; }
// touch controls: each finger is tracked on its own (Pointer Events); the D-pad reads the finger's angle (diagonals; sliding changes
// direction), A and B can be held together, a finger that slides off a button releases it; cancel and lost focus release everything
function cpTouch(surf, box){ const fingers=new Map();
  const at=(e)=>{ const t=surf.doc.elementFromPoint(e.clientX,e.clientY); const pad=box.querySelector('.cpdpad');
    if(t&&pad.contains(t)||t===pad){ const r=pad.getBoundingClientRect(), dx=e.clientX-(r.left+r.width/2), dy=e.clientY-(r.top+r.height/2); if(Math.hypot(dx,dy)<r.width*0.12) return [];
      const a=(Math.atan2(dy,dx)*180/Math.PI+360)%360; const s=[]; if(a>=202.5&&a<337.5) s.push('up'); if(a>=22.5&&a<157.5) s.push('down'); if(a>=112.5&&a<247.5) s.push('left'); if(a<67.5||a>=292.5) s.push('right'); return s; }
    const k=t&&t.closest&&t.closest('[data-k]'); return k&&box.contains(k)?[k.dataset.k]:[]; };
  const sync=()=>{ const on={}; for(const ks of fingers.values()) for(const k of ks) on[k]=1; CPI.touch=on; box.querySelectorAll('[data-k]').forEach(b=>b.classList.toggle('on',!!on[b.dataset.k])); box.querySelector('.cpdpad').dataset.on=['up','down','left','right'].filter(k=>on[k]).join(' '); };
  box.addEventListener('pointerdown',e=>{ if(e.pointerType==='mouse') return; e.preventDefault(); try{ box.setPointerCapture(e.pointerId); }catch(_){} fingers.set(e.pointerId, at(e)); sync(); });
  box.addEventListener('pointermove',e=>{ if(!fingers.has(e.pointerId)) return; e.preventDefault(); fingers.set(e.pointerId, at(e)); sync(); });
  const up=e=>{ if(fingers.delete(e.pointerId)) sync(); }; box.addEventListener('pointerup',e=>{ CCSPLAY.wakeAudio(); up(e); }); box.addEventListener('pointercancel',up); box.addEventListener('lostpointercapture',up);
  box.addEventListener('touchend',()=>CCSPLAY.wakeAudio(),{passive:true});   // a release is the person's action a phone's browser accepts for sound
  surf.releaseTouch=()=>{ fingers.clear(); sync(); }; }
CCSPLAY.surface=cpSurface; CCSPLAY.moveInput=cpInputMove;
