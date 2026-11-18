// Classic Cart Studio's service worker (W2): the installed app and offline use. It keeps ONE version of the page, its data, the browser
// game player and the HOME's art in a cache named after that version and answers from it, so a page never mixes two builds. A new version
// is fetched completely beside the old one (all or nothing: one missing file and the old version simply stays) and then waits: it takes
// over only when no window of the Studio is open, or when the page asks (Restart to Update, offered only while no game plays and no other
// window of the Studio is open). It never reads, changes or removes this browser's games, saved games or settings (they live in IndexedDB
// and the page's own storage, not in these caches), and it sends nothing anywhere: it only fetches this site's own files.
// self.CCS_SW = {version, shell:[...], home:[...], sha:{file: first 16 hex of its SHA-256}, nav?:{address: file}, moved?:{address: address}}
// is written above this line by tools/publish/build_site_ci.py. sha: every file is checked against it before this version keeps any (a
// file of another version, e.g. one deployed while this one was being fetched, discards the whole version). nav: the page addresses
// answered from this version's cache (the public website's entry pages, tools/publish/public_entry.py: the root page and the app at
// preview/); without it, the root page only, as before. moved: page addresses that now live elsewhere (at the launch: preview/ is the
// root), answered with a permanent redirect that keeps the query, never from a cache. An address without its final slash goes to the one with it.
const M = self.CCS_SW, V = 'ccs-' + M.version, MEDIA = 'ccs-media', META = 'ccs-meta';
const own = (o, p) => !!o && Object.prototype.hasOwnProperty.call(o, p);
const NAV = M.nav || {'': 'index.html', 'index.html': 'index.html'}, navTo = p => own(NAV, p) ? NAV[p] : null, MOVED = M.moved || {};
const SCOPE = new URL(self.registration.scope), at = p => new URL(p, SCOPE).href;
const fresh = p => new Request(at(p), {cache: 'reload'});   // never a stale copy from the browser's HTTP cache
async function prepared(){ return !!(await (await caches.open(META)).match(at('__prepared'))); }
const hex = async b => [...new Uint8Array(await crypto.subtle.digest('SHA-256', b))].map(x => x.toString(16).padStart(2, '0')).join('');
// fetch every file, check each against this version's list, and only then keep them all (one missing or different file: none is kept)
async function fill(c, list){ const got = await Promise.all(list.map(async p => { const r = await fetch(fresh(p)); if(!r.ok) throw new Error(p + ': ' + r.status);
    if(M.sha && own(M.sha, p) && (await hex(await r.clone().arrayBuffer())).slice(0, 16) !== M.sha[p]) throw new Error(p + ' is not this version\'s file');
    return [p, r]; }));
  for(const [p, r] of got) await c.put(at(p), r); }
self.addEventListener('install', e => { e.waitUntil((async () => {
  const c = await caches.open(V);
  try{ await fill(c, M.shell); if(await prepared()) await fill(c, M.home); }   // an app that was ready for offline use stays ready after an update
  catch(err){ await caches.delete(V); throw err; }   // one file could not be fetched: this version is discarded whole, nothing of it stays
})()); });
// earlier versions' files only (never the covers, never this browser's games or saves); a page's requests wait while this version
// activates, so the clean-up holds the switch for at most 2 s and goes on after it (it runs again at the next status question).
// Never while another version is installing or waits: its files are in a cache of its own (versions before this one removed it then,
// so the new version took over with no files kept: online it worked, offline it did not)
const RETIRED = /\/art\/hero\//;   // the retired per-game backgrounds, removed from the kept pictures too (covers stay)
const tidy = async () => { const r = self.registration; if(r.installing || (r.waiting && r.waiting !== self.serviceWorker)) return;
  for(const k of await caches.keys()) if(k.startsWith('ccs-') && k !== V && k !== MEDIA && k !== META) await caches.delete(k);
  const m = await caches.open(MEDIA); for(const q of await m.keys()) if(RETIRED.test(new URL(q.url).pathname)) await m.delete(q); };
// this version's files, complete again when some went missing (an earlier version's clean-up removed them while this one waited): fetched
// only while the site still serves this same version (its sw.js names it), so a version never holds another version's files
async function complete(){ const c = await caches.open(V), want = M.shell.concat(await prepared() ? M.home : []), miss = [];
  for(const p of want) if(!(await c.match(at(p)))) miss.push(p);
  if(!miss.length) return 0;
  try{ const t = await (await fetch(fresh('sw.js'))).text(); if(!t.includes('"version":"' + M.version + '"')) return -1; await fill(c, miss); return miss.length; }
  catch(_){ return -1; } }   // offline, or the site has moved on: tried again at the next status question
self.addEventListener('activate', e => { e.waitUntil(Promise.race([tidy().then(complete), new Promise(r => setTimeout(r, 2000))])); });
// covers and guide pictures: the copy kept here at once, refreshed from the site when it can be reached
async function media(q){ const c = await caches.open(MEDIA), hit = await c.match(q, {ignoreSearch: true});
  const net = fetch(q).then(r => { if(r.ok) c.put(q, r.clone()); return r; }).catch(() => null);
  return hit || (await net) || new Response('', {status: 504, statusText: 'Not available offline'}); }
self.addEventListener('fetch', e => { const q = e.request; if(q.method !== 'GET') return; const u = new URL(q.url); if(u.origin !== SCOPE.origin || !u.pathname.startsWith(SCOPE.pathname)) return;
  let p = decodeURIComponent(u.pathname.slice(SCOPE.pathname.length));
  if(q.mode === 'navigate'){ if(own(MOVED, p)){ e.respondWith(Response.redirect(at(MOVED[p]) + u.search, 301)); return; }
    if(navTo(p)) p = navTo(p); else if(p && navTo(p + '/')){ e.respondWith(Response.redirect(at(p + '/') + u.search, 301)); return; } else return; }
  if(M.shell.includes(p) || M.home.includes(p)){ e.respondWith((async () => { const c = await caches.open(V), hit = await c.match(at(p)); if(hit) return hit;
    const r = await fetch(q); if(r.ok && M.home.includes(p)) c.put(at(p), r.clone()); return r; })()); return; }
  if(/^(art|brand\/help|included)\//.test(p)) e.respondWith(media(q)); });   // included/: the Included Games and their source
self.addEventListener('message', e => { const d = e.data || {}, port = e.ports && e.ports[0], reply = o => { if(port) port.postMessage(o); };
  if(d.type === 'status') e.waitUntil((async () => { if(self.registration.active === self.serviceWorker){ await tidy(); reply({version: M.version, prepared: await prepared()}); await complete(); return; } reply({version: M.version, prepared: await prepared()}); })());
  // ready for offline use: the HOME's art in this version's cache, and the covers of the page's own games (named by the page, this site only)
  else if(d.type === 'prepare') e.waitUntil((async () => { try{
      const c = await caches.open(V), miss = []; for(const p of M.home) if(!(await c.match(at(p)))) miss.push(p); if(miss.length) await fill(c, miss);   // only what this version lacks
      const m = await caches.open(MEDIA); let covers = 0;
      for(const a of (Array.isArray(d.art) ? d.art : []).slice(0, 8000)){ if(typeof a !== 'string' || !/^art\/[A-Za-z0-9_./-]+\.(jpg|png|webp)$/.test(a) || a.includes('..')) continue;
        if(await m.match(at(a))) { covers++; continue; } try{ const r = await fetch(at(a)); if(r.ok){ await m.put(at(a), r); covers++; } }catch(_){} }
      await (await caches.open(META)).put(at('__prepared'), new Response(JSON.stringify({version: M.version, covers})));
      reply({ok: true, version: M.version, covers}); }catch(err){ reply({ok: false, error: String(err && err.message || err)}); } })());
  // Restart to Update: only when this is the Studio's only open window (another window would be left on the earlier version's files)
  else if(d.type === 'skip-waiting') e.waitUntil((async () => { const all = await self.clients.matchAll({type: 'window', includeUncontrolled: true});   // a waiting worker controls none yet: every window of this site
      if(all.length > 1){ reply({ok: false, why: 'other-windows', windows: all.length}); return; } reply({ok: true});
      self.skipWaiting(); })()); });   // not awaited inside this event: the switch waits for the events in flight, this one included
