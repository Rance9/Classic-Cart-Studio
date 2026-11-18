// ---- Included Games: games Classic Cart Studio hosts and includes because each author's published license or written grant covers it
// (included/index.json, ccs-included/2, embedded below as INCL_DATA: each build's exact identity, credits, full/demo edition, links and the
// uses its grant covers; each game file is the author's own exact build, unchanged, with its source beside it where the license asks).
// A person never has to find, download, unzip or import an Included Game: Play and Add to Collection get the build from this site (or, in
// the Windows app, from its own installation), check it against its record and keep it in the same stores as a person's own games (this
// browser's game store CCSROMS by exact content identity; the app's personal store, /api/personal/included), so play, Game Settings, saves,
// backups and HOME treat them like any other game. Nothing is fetched from anywhere else: a link names an approved game by its key only.
// The collection the Studio prepares on a new start is known by its mark (coll.included: the content identities it was made with), never by
// its name. Classic Cart Studio supplies no original commercial NES game.
const INCL_NAME = 'Included Games', INCL_SCHEMA = 'ccs-included/1';   // INCL_SCHEMA: the collection's mark (unchanged); the list itself is ccs-included/2
const INCL_DATA = __INCL__;   // build_html.py: site-data/included/index.json (games and known_builds)
let INCL_BUSY = null;
async function inclIndex(){ const d = INCL_DATA; if(d && /^ccs-included\/[12]$/.test(d.schema||'') && Array.isArray(d.games) && d.games.length) return d;
 const r = await fetch('included/index.json', {cache:'no-cache'}); if(!r.ok) throw new Error('index ' + r.status);
 const x = await r.json(); if(!x || !/^ccs-included\/[12]$/.test(x.schema||'') || !Array.isArray(x.games) || !x.games.length) throw new Error('index'); return x; }
// ---------------- the approved builds ----------------
function incGames(){ return (INCL_DATA && INCL_DATA.games || []).filter(g => g && g.hosted !== false); }
function incKnown(){ return (INCL_DATA && INCL_DATA.known_builds) || []; }
function incId(g){ return 'p_' + String(g.content_sha256).slice(0, 16); }
function incByKey(k){ return incGames().find(g => g.key === k) || null; }
// the approved or known build of exactly this content ({…record, hosted}), else null
function incBuild(content){ if(!content) return null; const c = String(content); const h = incGames().find(g => g.content_sha256 === c); if(h) return Object.assign({hosted:true}, h);
 const k = incKnown().find(g => g.content_sha256 === c); return k ? Object.assign({}, k, {hosted:false}) : null; }
function incOfId(id){ const k = String(id||'').replace(/^p_/, ''); if(!/^[0-9a-f]{16}$/.test(k)) return null; return incGames().concat(incKnown()).find(g => String(g.content_sha256).slice(0, 16) === k) || null; }
function incEdition(g){ return g && g.edition === 'demo' ? 'Demo' : 'Full game'; }
// each game's details text (its description, author and year, license and where its source is): the record's own "text", the same
// words the Windows app keeps for it (/api/personal/included)
function inclText(g){ if(typeof g.text !== 'string' || !g.text) throw new Error(g.key + ' has no text'); return g.text; }
// the details a collection keeps for an approved or known build (its personal record), the same for the starter, Add to Collection and a
// person's own file of exactly that build
function incKept(g){ const id = incId(g), text = typeof g.text === 'string' && g.text ? g.text : (g.description || '');
 return {title:g.title, version:g.version, description:text, year:g.year||'', publisher:g.creator, source:'personal', content_sha256:g.content_sha256, file_sha256:g.file_sha256, size:g.size,
  personal:{id, title:g.title, version_label:g.version, content_sha256:g.content_sha256, file_sha256:g.file_sha256, content_size:g.content_size, file_size:g.size, format:'ines', file_name:g.file_name,
   note:'Included with Classic Cart Studio', user:{title:g.title, version_label:g.version, description:text, year:g.year||'', publisher:g.creator}}}; }
// the build's exact bytes from this site, checked against its record, then kept in this browser's game store (never presented as ready first)
async function incFetch(g, step){ if(step) step(`Getting ${g.title}…`);
 const u = 'included/' + encodeURIComponent(g.file_name);   // this site's own folder only (a relative path; never another address)
 let r; try{ r = await fetch(u); }catch(e){ throw new Error(`${g.title} could not be downloaded (no connection to ClassicCartStudio.com)`); }
 if(!r.ok) throw new Error(`${g.title} could not be downloaded (${r.status})`);
 const u8 = new Uint8Array(await r.arrayBuffer()); const ident = await ccsIdentifyBytes(u8, g.file_name);
 if(ident.sha256 !== g.file_sha256 || ident.content_sha256 !== g.content_sha256 || u8.length !== g.size) throw new Error(`${g.file_name} does not match its record, so it was not kept`);
 await CCSROMS.put(u8, {sha:ident.content_sha256, title:g.title, name:g.file_name, header_hex:g.header_hex}); return ident; }
// Play and Add to Collection: the build here, ready (kept already, or got and checked now). The Windows app keeps it in its personal store.
async function incAcquire(g, step){ if(!g || g.hosted === false) throw new Error('That game is not one Classic Cart Studio hosts');
 if(typeof CO !== 'undefined' && CO.desktop){ if(step) step(`Getting ${g.title} ready…`); const r = await hkPost('/api/personal/included', {keys:[g.key]}).catch(() => ({ok:false}));
  if(!r.ok) throw new Error(r.error || `the app could not keep ${g.title}`); if(typeof refreshPersonal === 'function') await refreshPersonal(); return {id:incId(g)}; }
 if(typeof CCSPLAY === 'undefined' || !CCSPLAY.supported()) throw new Error('This browser cannot keep games for playing here (it lacks WebAssembly or private storage). Use a current Edge, Chrome, Firefox or Safari');
 if(!await CCSROMS.has(g.content_sha256)) await incFetch(g, step);   // a read that fails throws (never taken for "not here")
 CCSROMS.persist(); return {id:incId(g)}; }
// the Included Games' files, each checked against its record and kept (all of them: the starter and "Add All as a Collection"): {d, ids, missing}
async function inclKeep(step){
  const d = await inclIndex(); const games = (d.games||[]).filter(g => g.hosted !== false); const ids = [], missing = {}; let n = 0;
  for(const g of games){ if(step) step(`Checking ${g.title} (${++n} of ${games.length})…`);
   if(!(typeof CO !== 'undefined' && CO.desktop)) await incFetch(g);
   inclText(g); const id = incId(g); ids.push(id); missing[id] = incKept(g); }
  if(CCSROMS.persist) CCSROMS.persist();
  if(typeof CO !== 'undefined' && CO.desktop){   // the Windows app plays, backs up and prepares collections from its own personal store
   if(step) step('Keeping the games on this computer…');
   const r = await hkPost('/api/personal/included', {}).catch(() => ({ok:false}));
   if(!r.ok) throw new Error(r.error || 'the app could not keep the included games');
   if(typeof refreshPersonal === 'function') await refreshPersonal();
  }
  return {d:Object.assign({}, d, {games}), ids, missing}; }
// the collections carrying the Included Games mark, the open one as it is right now
function inclMarked(){ if(typeof csSaveActiveNow === 'function') csSaveActiveNow();
 return (typeof csList === 'function' ? csList() : []).filter(c => c.coll && c.coll.included && c.coll.included.schema === INCL_SCHEMA); }
function inclAdd(){ return INCL_BUSY || (INCL_BUSY = inclAddOnce().finally(() => { INCL_BUSY = null; })); }   // one at a time: a second start joins the first
async function inclAddOnce(){
 try{
  const {d, ids, missing} = await inclKeep();
  const marked = inclMarked(), named = csList().find(c => c.name.toLowerCase() === INCL_NAME.toLowerCase() && !marked.includes(c));
  const whole = marked.find(c => ids.every(id => (c.sel || []).includes(id)));
  if(whole){ inclDone(whole, ids, 'already'); return; }
  const {rec} = csRecordFromFile({name:INCL_NAME, sel:ids, coll:{missing}}, INCL_NAME);
  rec.coll.included = {schema:INCL_SCHEMA, games:d.games.map(g => g.content_sha256)};
  const c = csCreate(INCL_NAME, rec);   // a free name when one is taken ("Included Games (Copy 1)"): nothing of the person's is replaced
  if(typeof csMenuRefresh === 'function') csMenuRefresh();
  inclDone(c, ids, marked.length ? 'changed' : named ? 'named' : 'new', marked[0] || named);
 }catch(e){ console.warn('included games', e); toast('The Included Games could not be added right now. Try again in a moment.'); }
}
// the result, read back from the collection as it is now saved: its name and how many of the Included Games it holds
function inclDone(c0, ids, how, other){
 const c = (CS && CS.items[c0.id]) || c0, n = ids.filter(id => (c.sel || []).includes(id)).length, where = typeof CO !== 'undefined' && CO.desktop ? 'app' : 'browser';
 const games = `${n} game${n === 1 ? '' : 's'}`;
 const msg = how === 'already' ? `"${esc(c.name)}" is already in My Collections with all ${n} Included Games, ready to play in this ${where}.`
  : how === 'changed' ? `You changed "${esc(other.name)}", so it stays as it is. A complete copy, "${esc(c.name)}" (${games}), is now in My Collections.`
  : how === 'named' ? `You already have a collection called "${esc(other.name)}", so it stays as it is. The Included Games are now in My Collections as "${esc(c.name)}" (${games}).`
  : `"${esc(c.name)}" (${games}) is now in My Collections. The collection you had open is unchanged.`;
 openColl('Included Games', '', `<div class="msgbox ok">${msg}</div>`
  + `<p class="small">Their authors' licenses let Classic Cart Studio share these exact games, and their source code comes with them. Classic Cart Studio supplies no original commercial NES game.</p>`
  + `<div class="acts"><button class="btn ghost" id="inclStay">Stay Here</button><button class="btn hk" id="inclOpen">Open It Now</button></div>`);
 $('inclStay').onclick = closeColl; $('inclOpen').onclick = () => { closeColl(); csSwitch(c.id, true); };
}
// ---------------- Play and Add to Collection (no ROM file to find: the build is got and checked automatically) ----------------
async function incPlay(key){ const g = incByKey(key); if(!g) return; if(typeof CCSPLAY !== 'undefined') try{ CCSPLAY.unlockAudio(); }catch(e){}   // the press itself starts sound
 if(typeof CCSPLAY !== 'undefined' && !(typeof CO !== 'undefined' && CO.desktop) && CCSPLAY.running()){ toast('A game is already open.'); return; }
 const box = document.querySelector(`[data-inc-state="${CSS.escape(key)}"]`); const say = t => { if(box){ box.textContent = t; box.hidden = !t; } };
 try{ await incAcquire(g, say); }catch(e){ say(''); cvSaid(`${g.title} Is Not Ready`, `<div class="msgbox bad">${esc(String(e && e.message || e))}.</div><p class="small">Nothing was added to any collection. Check the connection, then try again.</p>`); return; }
 say(''); if(typeof CO !== 'undefined' && CO.desktop){ if(typeof playCapable === 'function' && playCapable()) return playDirect(incId(g)); cvSaid('Play', '<div class="msgbox bad">The game player is not available in this installation. Run the Classic Cart Studio installer again to repair it.</div>'); return; }
 return cvPlayWebGame({id:incId(g), title:g.title, version_label:g.version, sha:g.content_sha256, header_hex:g.header_hex}); }
// adds the build to the chosen collection only once it is here and checked; nothing else of the collection changes
async function incAddTo(key, cid){ const g = incByKey(key); if(!g || !CS || CS_STATE !== 'ok') return; const id = incId(g);
 cid = cid || CS.active; if(!cid && typeof csEnsureActive === 'function' && csEnsureActive()) cid = CS.active; const r0 = cid && CS.items[cid]; if(!r0) return;
 const box = document.querySelector(`[data-inc-state="${CSS.escape(key)}"]`); const say = t => { if(box){ box.textContent = t; box.hidden = !t; } };
 try{ await incAcquire(g, say); }catch(e){ say(''); cvSaid(`${g.title} Was Not Added`, `<div class="msgbox bad">${esc(String(e && e.message || e))}.</div><p class="small">Nothing was added to any collection. Try again when the connection is back.</p>`); return; }
 say('');
 if(cid === CS.active){ if(sel.has(id)){ toast(`${g.title} is already in "${COLL.name}"`); return; }
  sel.add(id); COLL.missing = Object.assign({}, COLL.missing || {}); if(!COLL.missing[id]) COLL.missing[id] = incKept(g); persist(); render(); if(typeof rdyChanged === 'function') rdyChanged(true); if(typeof csSaveActiveNow === 'function') csSaveActiveNow(); }
 else { const r = CS.items[cid]; if((r.sel || []).includes(id)){ toast(`${g.title} is already in "${r.name}"`); return; }
  r.sel = [...(r.sel || []), id]; r.coll = Object.assign({}, r.coll || {}); r.coll.missing = Object.assign({}, r.coll.missing || {}); if(!r.coll.missing[id]) r.coll.missing[id] = incKept(g); r.modified = csNow(); csWrite(); }
 if(typeof csMenuRefresh === 'function') csMenuRefresh(); incRefresh(); toast(`Added ${g.title} to "${CS.items[cid].name}"`); }
// ---------------- the link a creator shares: ClassicCartStudio.com/?play=<key> opens straight into that game ----------------
function incLink(g){ return (typeof SITE_URL === 'string' ? SITE_URL : 'https://classiccartstudio.com/') + '?play=' + encodeURIComponent(g.key); }
async function incCopyLink(key){ const g = incByKey(key); if(!g) return; const u = incLink(g);
 try{ await navigator.clipboard.writeText(u); toast(`Link copied: ${u}`); }catch(e){ cvSaid('Game Link', `<p class="small">Copy this link to share ${esc(g.title)}. It opens the game directly on ClassicCartStudio.com.</p><div class="fld"><input type="text" readonly value="${esc(u)}" onfocus="this.select()"></div>`); } }
// a visit with ?play=<key>: one approved game, its title, author and edition, and one Play button (a browser starts sound and full screen only
// from a person's press). Nothing about the open collection changes, nothing is added to a collection, and no saved game is touched: the
// game's own start menu offers its save slots and Start New Game. Any other value is ignored (never a web address to fetch).
function incDirectFromUrl(){ let key = null; try{ key = new URLSearchParams(location.search).get('play'); }catch(e){} if(!key || (typeof CO !== 'undefined' && CO.desktop)) return;
 try{ const u = new URL(location.href); u.searchParams.delete('play'); history.replaceState(history.state, '', u.pathname + (u.search || '') + u.hash); }catch(e){}   // a reload opens the Studio itself
 const g = /^[a-z0-9-]{1,40}$/.test(key) ? incByKey(key) : null;
 if(!g){ setTimeout(() => toast('That game link is not one of the Included Games. Here is Classic Cart Studio.'), 400); return; }
 openColl(g.title, `${incEdition(g)} · by ${g.creator}`, `<div class="incdirect"><p>${esc(g.description || '')}</p>
  <p class="small">${esc(g.title)} ${esc(g.version)} by ${esc(g.creator)}, included with Classic Cart Studio under ${esc(g.license)}.${g.edition === 'demo' ? ' This is a demo: the full game is sold by its author.' : ''}</p>
  <div class="small" data-inc-state="${esc(g.key)}" role="status" hidden></div>
  <div class="acts"><button class="btn ghost" id="incDirNo">Not Now</button><span class="sp"></span><button class="btn hk big" id="incDirGo" data-incplay="${esc(g.key)}"><span aria-hidden="true">▶</span> Play</button></div></div>`);
 $('incDirNo').onclick = closeColl; setTimeout(() => { try{ $('incDirGo').focus(); }catch(e){} }, 50); }
// ---------------- the Included Games (My Collections › Included Games…) ----------------
function incCard(g){ const ed = incEdition(g); const links = [g.links && g.links.author ? `<a href="${esc(g.links.author)}" target="_blank" rel="noopener">${esc(g.creator)}'s page ↗</a>` : ''].filter(Boolean).join(' · ');
 const colls = (typeof csList === 'function' ? csList() : []); const opts = colls.map(c => `<option value="${esc(c.id)}"${c.id === CS.active ? ' selected' : ''}>${esc(c.name)}</option>`).join('');
 return `<div class="inccard" data-inc-key="${esc(g.key)}"><div class="inchd"><b>${esc(g.title)}</b> <span class="incbadge${g.edition === 'demo' ? ' demo' : ''}">${ed}</span></div>
  <div class="small">${esc(g.version)} · by ${esc(g.creator)}${g.year ? ' · ' + esc(g.year) : ''}${g.genre ? ' · ' + esc(g.genre) : ''}${links ? ' · ' + links : ''}</div>
  <p class="small">${esc(g.description || '')}</p>
  ${g.edition === 'demo' ? `<div class="incbuy" data-inc-buy="${esc(g.key)}">Checking your library…</div>` : ''}
  <div class="small" data-inc-state="${esc(g.key)}" role="status" hidden></div>
  <div class="acts"><button class="btn hk" data-incplay="${esc(g.key)}"><span aria-hidden="true">▶</span> Play</button>
   ${opts ? `<label class="small incto">Add to <select data-inc-to="${esc(g.key)}" aria-label="Collection to add ${esc(g.title)} to">${opts}</select></label><button class="btn" data-incadd="${esc(g.key)}">Add to Collection</button>` : `<button class="btn" data-incadd="${esc(g.key)}">Add to a New Collection</button>`}
   <button class="btn ghost" data-inclink="${esc(g.key)}">Copy Game Link</button></div></div>`; }
function incBrowse(){ const gs = incGames();
 openColl('Included Games', `${gs.length} game${gs.length === 1 ? '' : 's'}`, `<p class="small">Homebrew games Classic Cart Studio includes with their authors' permission, free to play. Play starts a game right away; Add to Collection puts it in the collection you choose. Classic Cart Studio gets each game for you and checks it first: there is no file to find or import.</p>
  ${gs.map(incCard).join('')}
  <div class="acts"><button class="btn ghost" id="incAll">Add All as a Collection</button><span class="sp"></span><a href="#" class="small" data-guide="homebrew">For homebrew developers</a><button class="btn" id="incClose">Close</button></div>`);
 $('incClose').onclick = closeColl; $('incAll').onclick = () => { closeColl(); inclAdd(); }; incRefresh(); }
// ---------------- a demo and its full game: never offer to buy a full game that is already here ----------------
// The full build is recognized only by its exact content identity in the approved/known list (same family, edition "full"), never by a
// name. "Here" (Codex v1p4 F2): in the Windows app, either of its game stores, the Game Library's copies (the catalog id, content
// SHA-256[:16]) or the person's own games ("p_" + that), in one answer from the app (/api/package/inventory), with or without any
// collection; a kept record whose file is gone is "missing" (Locate, never Buy) even when no collection selects it. In a browser, its game
// store by exact content, and any collection selecting either id of the build. An answer that cannot be read is an error, never "none".
// This is what the Studio can see on this device, not a record of a purchase.
function incFulls(g){ return incGames().concat(incKnown()).filter(x => x && g && x.family && x.family === g.family && x.edition === 'full' && x.content_sha256 !== g.content_sha256); }
function incCatId(g){ return String(g.content_sha256).slice(0, 16); }
// the id a build is kept under, the way Add ROM Files keeps it: a Game Library version under its catalog id, any other build as "p_"
function incHeldId(g){ const c = incCatId(g), r = typeof byId !== 'undefined' ? byId[c] : null; return r && r.source !== 'personal' ? c : incId(g); }
function incIdOf(g, id){ return id === incId(g) || id === incCatId(g) ? id : incHeldId(g); }
async function incFullState(g){ const fulls = incFulls(g); if(!fulls.length) return {state:'none', fulls};
 const newest = (a, b) => String(b.f.version).localeCompare(String(a.f.version), 'en', {numeric:true});
 try{ let avail = [], kept = [];
  if(typeof CO !== 'undefined' && CO.desktop){ const r = await hkPost('/api/package/inventory', {contents:fulls.map(f => f.content_sha256)});
   if(!r || !r.ok || !Array.isArray(r.items)) throw new Error((r && r.error) || 'the check did not answer');
   const by = {}; r.items.forEach(x => { if(x && typeof x.content_sha256 === 'string') by[x.content_sha256] = x; });
   if(fulls.some(f => !by[f.content_sha256])) throw new Error('the check did not answer for every build');
   avail = fulls.filter(f => by[f.content_sha256].state === 'available').map(f => ({f, id:incIdOf(f, by[f.content_sha256].id)}));
   if(!avail.length && fulls.some(f => by[f.content_sha256].state === 'unreadable')) throw new Error('a game file could not be read');
   kept = fulls.filter(f => by[f.content_sha256].state === 'missing').map(f => ({f, id:incIdOf(f, by[f.content_sha256].id)})); }
  else { CCSROMS._have = null; const keys = await CCSROMS.keys(true); avail = fulls.filter(f => keys.has(f.content_sha256)).map(f => ({f, id:incHeldId(f)})); }
  const latest = fulls.slice().sort((a, b) => String(b.version).localeCompare(String(a.version), 'en', {numeric:true}))[0];
  if(avail.length){ const b = avail.sort(newest)[0]; return {state:'available', build:b.f, id:b.id, latest, fulls}; }
  const ids = new Set(); (typeof csList === 'function' ? csList() : []).forEach(c => ((typeof csRecordNow === 'function' ? csRecordNow(c) : c).sel || []).forEach(x => ids.add(x)));
  const inColl = fulls.map(f => ({f, id:[incId(f), incCatId(f)].find(i => ids.has(i))})).filter(x => x.id);
  const rec = kept.sort(newest)[0] || inColl.sort(newest)[0]; if(rec) return {state:'missing', build:rec.f, id:rec.id, latest, fulls};
  return {state:'none', latest, fulls}; }
 catch(e){ return {state:'error', why:String(e && e.message || e).slice(0, 160), fulls}; } }
function incStoreUrl(g, st){ const f = st && st.latest; return (f && f.links && f.links.store) || (g.links && g.links.store) || ''; }
async function incBuyFill(el){ const g = incByKey(el.dataset.incBuy) || incOfId(el.dataset.incBuyId); if(!g){ el.innerHTML = ''; return; }
 const st = await incFullState(g); if(!el.isConnected) return; const store = incStoreUrl(g, st);
 if(st.state === 'available'){ const b = st.build, older = st.latest && st.latest.content_sha256 !== b.content_sha256;
  el.innerHTML = `<div class="msgbox ok">Full game already in your library${older || b.version ? ` (version ${esc(b.version)})` : ''}.</div><div class="acts"><button class="btn hk" data-inc-gofull="${esc(b.content_sha256)}" data-inc-id="${esc(st.id || '')}">Go to Full Game</button>${older && store ? `<button class="btn ghost" data-inc-store="${esc(g.key)}" data-inc-store-anyway="1">Visit the developer's store (updates and other editions) ↗</button>` : ''}</div>`; return; }
 if(st.state === 'missing'){ const b = st.build;
  el.innerHTML = `<div class="msgbox warn">Full game previously added — game file missing.</div><div class="acts"><button class="btn hk" data-inc-locate="${esc(b.content_sha256)}" data-inc-id="${esc(st.id || '')}">Locate Full Game File…</button><button class="btn" data-inc-gofull="${esc(b.content_sha256)}" data-inc-id="${esc(st.id || '')}">Full Game Details</button></div>`; return; }
 if(st.state === 'error'){ el.innerHTML = `<div class="msgbox warn">Classic Cart Studio could not check whether the full game is already here (${esc(st.why)}).</div><div class="acts"><button class="btn" data-inc-retry="1">Try Again</button>${store ? `<button class="btn ghost" data-inc-store="${esc(g.key)}" data-inc-store-anyway="1">Open the developer's store anyway ↗</button>` : ''}</div>`; return; }
 el.innerHTML = store ? `<div class="acts"><button class="btn" data-inc-store="${esc(g.key)}">Get the Full Game ↗</button></div><p class="small">Already bought it? Add its game file with <b>Add Game Files</b> first, and this demo shows it.</p>` : ''; }
function incRefresh(){ document.querySelectorAll('[data-inc-buy],[data-inc-buy-id]').forEach(el => { incBuyFill(el).catch(() => {}); }); }
// a store link opens only after a fresh check finds no full game here (a click, Enter, a touch or a controller all come here: there is no live link)
async function incOpenStore(key, anyway){ const g = incByKey(key); if(!g) return; const st = anyway ? null : await incFullState(g);
 if(st && st.state !== 'none'){ incRefresh(); toast(st.state === 'available' ? 'The full game is already in your library.' : st.state === 'missing' ? 'The full game was added before: locate its game file first.' : 'Classic Cart Studio could not check your library. Try again.'); return; }
 const u = incStoreUrl(g, st || {latest:incFulls(g)[0]}); if(!/^https:\/\//.test(u)) return; window.open(u, '_blank', 'noopener'); }
async function incLocateFull(content, held){ const f = incGames().concat(incKnown()).find(x => x.content_sha256 === content); if(!f) return; const id = incIdOf(f, held);
 if(typeof CO !== 'undefined' && CO.desktop){ toast('Choose the game file in the window that opened');
  const r = /^p_/.test(id) ? await hkPost('/api/personal/relink', {id, content_sha256:f.content_sha256}) : await hkPost('/api/library/locate', {version_id:id});   // the id the check found it under
  if(r && (r.canceled || r.cancelled)) return; if(!r || !r.ok){ cvSaid('Game File Not Added', `<div class="msgbox bad">${esc((r && r.error) || 'That file does not match this game.')} Nothing was changed.</div>`); return; }
  try{ await refreshPersonal(); if(typeof companionStatus === 'function') await companionStatus(); }catch(e){} if(typeof rdyChanged === 'function') rdyChanged(true); incRefresh(); toast(`Game file found and verified: ${f.title}`); return; }
 const files = await cvPick('.nes,.unh,.zip', false); if(!files || !files.length) return; let list;
 try{ list = await cvReadRoms(files, null, {sha:f.content_sha256, header_hex:f.header_hex}); }catch(e){ cvSaid('Game File Not Added', `<div class="msgbox bad">${esc(String(e && e.message || e))} Nothing was changed.</div>`); return; }
 const hit = list.find(r => r.content_sha256 === f.content_sha256);
 if(!hit){ cvSaid('Game File Not Added', `<div class="msgbox bad">That file is not ${esc(f.title)} ${esc(f.version)} (the full game this demo belongs to).${list.bad && list.bad.length ? ' ' + esc(list.bad[0].why) + '.' : ''} Nothing was changed.</div>`); return; }
 try{ await CCSROMS.put(hit.u8, {sha:hit.content_sha256, title:f.title, name:hit.name, header_hex:hit.header_hex || f.header_hex || ''}); await CCSROMS.persist(); }catch(e){ cvSaid('Game File Not Added', `<div class="msgbox bad">${esc(String(e && e.message || e))}</div>`); return; }
 if(typeof rdyChanged === 'function') rdyChanged(true); incRefresh(); toast(`Game file verified and kept in this browser: ${f.title}`); }
// Go to Full Game: its Details where it is in the open collection, else its own page here with Play and Add to Collection
function incGoFull(content, held){ const f = incGames().concat(incKnown()).find(x => x.content_sha256 === content); if(!f) return; const id = incIdOf(f, held);
 const open = [id, incId(f), incCatId(f)].find(i => sel && sel.has(i)); if(open && typeof openDetails === 'function'){ closeColl(); openDetails(open); return; }
 const hosted = incGames().some(x => x.content_sha256 === content);
 openColl(f.title, `${incEdition(f)} · by ${f.creator}`, `<p class="small">${esc(f.description || '')}</p><p class="small">Version ${esc(f.version)} by ${esc(f.creator)}.</p><div class="incbuy" data-inc-full-state="${esc(content)}"></div>
  <div class="small" data-inc-state="${esc(f.key || '')}" role="status" hidden></div>
  <div class="acts"><span class="sp"></span>${hosted ? `<button class="btn" data-incadd="${esc(f.key)}">Add to Collection</button><button class="btn hk" data-incplay="${esc(f.key)}">▶ Play</button>` : `<button class="btn" data-inc-addown="${esc(content)}" data-inc-id="${esc(id)}">Add to "${esc(COLL.name || 'this collection')}"</button><button class="btn hk" data-inc-playown="${esc(content)}" data-inc-id="${esc(id)}">▶ Play</button>`}</div>`); }
// a full build the person has here (their own ROM, not hosted): Play it and add it without asking for the file again
async function incOwn(content, play, held){ const f = incKnown().find(x => x.content_sha256 === content); if(!f) return; const id = incIdOf(f, held);
 if(play){ if(typeof CO !== 'undefined' && CO.desktop) return playDirect(id); return cvPlayWebGame({id, title:f.title, version_label:f.version, sha:f.content_sha256, header_hex:f.header_hex}); }
 if(!csEnsureActive()) return; if(!sel.has(id)){ sel.add(id); if(/^p_/.test(id)){ COLL.missing = Object.assign({}, COLL.missing || {}); if(!COLL.missing[id]) COLL.missing[id] = incKept(f); } persist(); render(); if(typeof rdyChanged === 'function') rdyChanged(true); if(typeof csSaveActiveNow === 'function') csSaveActiveNow(); }
 closeColl(); toast(`${f.title} is in "${COLL.name}"`); }
document.addEventListener('click', e => { const t = e.target.closest && e.target.closest('[data-incplay],[data-incadd],[data-inclink],[data-inc-gofull],[data-inc-locate],[data-inc-retry],[data-inc-store],[data-inc-addown],[data-inc-playown]'); if(!t) return;
 e.preventDefault(); e.stopPropagation(); const d = t.dataset;
 if(d.incplay) incPlay(d.incplay); else if(d.incadd){ const s = document.querySelector(`select[data-inc-to="${CSS.escape(d.incadd)}"]`); incAddTo(d.incadd, s ? s.value : null); }
 else if(d.inclink) incCopyLink(d.inclink); else if(d.incGofull) incGoFull(d.incGofull, d.incId); else if(d.incLocate) incLocateFull(d.incLocate, d.incId);
 else if(d.incRetry) incRefresh(); else if(d.incStore) incOpenStore(d.incStore, d.incStoreAnyway === '1'); else if(d.incAddown) incOwn(d.incAddown, false, d.incId); else if(d.incPlayown) incOwn(d.incPlayown, true, d.incId); }, true);
document.addEventListener('click', e => { const t = e.target.closest && e.target.closest('#mIncluded'); if(!t) return; e.preventDefault(); incBrowse(); });
// the lists follow what changes here: a game kept or removed, a Locate, a collection's games
(function(){ const wrap = n => { const f = window[n]; if(typeof f !== 'function') return; window[n] = function(){ const r = f.apply(this, arguments); try{ incRefresh(); }catch(e){} return r; }; };
 ['keptAvailChanged', 'rdyChanged'].forEach(wrap); })();
// ---------------- the Game Catalog knows the Included Games (their canonical title, author, edition, version and exact identity) ----------------
function incRow(g){ const id = incId(g); if(typeof perRow !== 'function') return null;
 const row = perRow({id, user:{title:g.title, version_label:g.version, year:g.year || '', publisher:g.creator, description:g.text || g.description || ''}, detected:{header_hex:g.header_hex, mapper:g.mapper}, content_sha256:g.content_sha256, content_size:g.content_size, file_name:g.file_name, file_present:false});
 return Object.assign(row, {title_id:'ti_' + g.key, genre:g.genre || '', players:g.players || '', kept:true, inc:g.key, edition:g.edition, identity:'Included Games', identity_verification:'an Included Games build, identified by its exact content'}); }
let INC_ROWS = null;
function incRows(){ if(!INC_ROWS) INC_ROWS = incGames().map(incRow).filter(Boolean); return INC_ROWS.filter(r => !byId[r.game_id]); }
(function(){ if(typeof listRows === 'function'){ const lr = listRows; listRows = function(){ const out = lr.apply(this, arguments); return CV_MODE === 'catalog' ? out.concat(incRows()) : out; }; }
 if(typeof gameOf === 'function'){ const go = gameOf; gameOf = function(id){ return go(id) || (CV_MODE === 'catalog' ? incRows().find(r => r.game_id === id) || null : null); }; } })();
// ---- a new Studio: nothing of the person's is here (collections.js CS_FRESH: no collections and none of the earlier keys), so the
//      Included Games are prepared once and their collection opens, ready to play: no menu, popup or tour first. Once only: the store
//      remembers it (CS.starter), so games or collections the person removes later are never added back, and a restored or established
//      Studio never gets them. Other tabs wait on one lock and take what the first one made. In the Windows app the durable copy of the
//      person's data (desktop-state.json) is read first: a Studio that has data there is not new. A failure is said with Try Again;
//      nothing is written, so the next start tries again too. Storage that cannot be read is never taken for a new Studio. ----
let INCL_START = null;
function inclPristine(r){ const m = r && r.coll && r.coll.included; if(!m || m.schema !== INCL_SCHEMA || !Array.isArray(m.games) || r.name !== INCL_NAME) return false;
 const want = m.games.map(s => 'p_' + String(s).slice(0, 16)).sort(), have = [...(r.sel || [])].sort(); if(JSON.stringify(want) !== JSON.stringify(have)) return false;
 if(r.home || Object.keys(r.decisions || {}).length) return false; return !Object.values((r.coll && r.coll.missing) || {}).some(x => x && x.personal && x.personal.user && Array.isArray(x.personal.user.edited) && x.personal.user.edited.length); }
function inclIsStarterView(){ const r = typeof csActive === 'function' && csActive(); return !!(r && CS.starter && CS.starter.state === 'done' && CS.starter.id === r.id && inclPristine(csRecordNow(r))); }
function inclLocked(fn){ return navigator.locks && navigator.locks.request ? navigator.locks.request('ccs-starter', {mode:'exclusive'}, fn) : fn(); }
function inclStarter(retry){ if(INCL_START) return INCL_START; if(CS_STATE !== 'ok' || !CS || !CS.starter || CS.starter.state === 'done' || (CS.starter.state === 'failed' && !retry)) return Promise.resolve(false);
 return INCL_START = (async () => { try{ if(typeof desktopSettled === 'function') await desktopSettled();   // the app: its durable copy decides first (it may hold the person's collections)
  return await inclLocked(async () => {
   const st = csReadState(); if(st.state === 'unreadable'){ CS_STATE = 'unreadable'; cvPanel(); return false; }
   if(st.state === 'ok'){ const s = st.store; if(!s.starter || s.starter.state === 'done'){ CS = s; CS_FRESH = false; csMigrateStore(); if(csActive()) csLoadWorking(csActive()); else { csEmptyWorking(); render(); } cvPanel(); return false; }   // another tab (or a restore) settled it
    CS = s; }
   if(!CS.starter || CS.starter.state === 'done') return false;
   CS.starter = {state:'preparing', at:csNow(), note:'Checking the game files…'}; cvPanel();
   let got; try{ got = await inclKeep(t => { if(CS.starter && CS.starter.state === 'preparing'){ CS.starter.note = t; cvPanel(); } }); }
   catch(e){ console.warn('included games', e); CS.starter = {state:'failed', at:csNow(), why:'They could not be checked and kept ' + (typeof CO !== 'undefined' && CO.desktop ? 'on this computer' : 'in this browser') + ' (' + String(e && e.message || e).slice(0, 160) + ').'}; cvPanel(); return false; }
   const {d, ids, missing} = got; const {rec} = csRecordFromFile({name:INCL_NAME, sel:ids, coll:{missing}}, INCL_NAME); rec.coll.included = {schema:INCL_SCHEMA, games:d.games.map(g => g.content_sha256)};
   CS.starter = {state:'done', at:csNow(), id:null}; CS_FRESH = false; const c = csCreate(INCL_NAME, rec); CS.starter.id = c.id; csSwitch(c.id, true);   // written once, with the collection, then opened
   if(typeof rdyChanged === 'function') rdyChanged(true); cvPanel(); return true; }); }
  finally { INCL_START = null; } })(); }
document.addEventListener('click', e => { const t = e.target.closest && e.target.closest('#mDevs,#mCredits'); if(!t || typeof openGuide !== 'function') return; e.preventDefault(); openGuide(t.id === 'mDevs' ? 'homebrew' : 'credits'); });
if(document.readyState === 'loading') document.addEventListener('DOMContentLoaded', () => setTimeout(incDirectFromUrl, 300)); else setTimeout(incDirectFromUrl, 300);
// For Homebrew Developers: the submission email (the address and the questions; copyable when this device has no email program)
document.addEventListener('click', e => { const t = e.target.closest && e.target.closest('[data-hbmail],[data-hbcopy]'); if(!t) return; e.preventDefault();
 const addr = 'rance9@classiccartstudio.com', form = (document.getElementById('hbForm') || {}).textContent || '';
 if(t.dataset.hbmail){ location.href = 'mailto:' + addr + '?subject=' + encodeURIComponent('Game for Classic Cart Studio: ') + '&body=' + encodeURIComponent(form + '\n'); return; }
 const text = t.dataset.hbcopy === 'address' ? addr : form; navigator.clipboard.writeText(text).then(() => toast(t.dataset.hbcopy === 'address' ? 'Address copied' : 'Questions copied'), () => toast('Select the text and copy it with Ctrl+C (or a long press)')); });
