// Tests for the Classic Cart Studio share Worker. Run from the workspace root:
//   node tools/share-worker/test/worker.test.mjs
import { deflateSync } from 'node:zlib';
import { createHash, randomBytes } from 'node:crypto';
import worker from '../src/worker.js';

const SITE = 'https://classiccartstudio.com';
const SHARE = 'https://share.classiccartstudio.com';

// ---------- mocks ----------

class MemoryKV {
  constructor() {
    this.map = new Map();
  }
  _live(key) {
    const e = this.map.get(key);
    if (!e) return null;
    if (e.expires && e.expires <= Date.now()) {
      this.map.delete(key);
      return null;
    }
    return e;
  }
  async get(key, opts) {
    const type = typeof opts === 'string' ? opts : (opts && opts.type) || 'text';
    const e = this._live(key);
    if (!e) return null;
    if (type === 'arrayBuffer') return e.bytes.slice().buffer;
    const text = new TextDecoder().decode(e.bytes);
    if (type === 'json') return JSON.parse(text);
    if (type === 'text') return text;
    throw new Error(`unsupported type ${type}`);
  }
  async put(key, value, opts = {}) {
    let bytes;
    if (typeof value === 'string') bytes = new TextEncoder().encode(value);
    else if (value instanceof ArrayBuffer) bytes = new Uint8Array(value.slice(0));
    else if (ArrayBuffer.isView(value)) bytes = new Uint8Array(value.buffer.slice(value.byteOffset, value.byteOffset + value.byteLength));
    else throw new Error('unsupported value');
    if (opts.expirationTtl !== undefined && opts.expirationTtl < 60) throw new Error('expirationTtl must be >= 60');
    this.map.set(key, { bytes, expires: opts.expirationTtl ? Date.now() + opts.expirationTtl * 1000 : 0, ttl: opts.expirationTtl });
  }
  async delete(key) {
    this.map.delete(key);
  }
}

class MemoryR2 {
  constructor() {
    this.map = new Map();
  }
  async get(key) {
    const e = this.map.get(key);
    if (!e) return null;
    return {
      key,
      size: e.bytes.byteLength,
      httpMetadata: e.meta,
      async arrayBuffer() { return e.bytes.slice().buffer; },
      async text() { return new TextDecoder().decode(e.bytes); },
      async json() { return JSON.parse(new TextDecoder().decode(e.bytes)); },
    };
  }
  async put(key, value, opts = {}) {
    const bytes = typeof value === 'string' ? new TextEncoder().encode(value) : new Uint8Array(ArrayBuffer.isView(value) ? value.buffer.slice(value.byteOffset, value.byteOffset + value.byteLength) : value);
    this.map.set(key, { bytes, meta: opts.httpMetadata || {} });
    return { key };
  }
  async delete(keys) {
    const list = Array.isArray(keys) ? keys : [keys];
    if (list.length > 1000) throw new Error('R2 deletes at most 1000 keys per call');
    for (const k of list) this.map.delete(k);
  }
  async list({ prefix = '' } = {}) {
    return { objects: [...this.map.keys()].filter((k) => k.startsWith(prefix)).map((key) => ({ key })), truncated: false };
  }
  async json(key) {
    const o = await this.get(key);
    return o ? o.json() : null;
  }
}

function makeLimiter(allow) {
  const calls = [];
  return { calls, async limit(arg) { calls.push(arg); return { success: allow(calls.length, arg) }; } };
}

// R2 is the primary store; KV is optional (counters) or a fallback store.
function makeEnv(extra = {}) {
  return { SHARE_BUCKET: new MemoryR2(), SITE_ORIGIN: SITE, SHARE_ORIGIN: SHARE, ...extra };
}

const ctx = { waitUntil() {}, passThroughOnException() {} };

// ---------- PNG encoder ----------

const CRC_TABLE = (() => {
  const t = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c >>> 0;
  }
  return t;
})();

function crc32(buf) {
  let c = 0xffffffff;
  for (const b of buf) c = CRC_TABLE[(c ^ b) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
  const out = Buffer.alloc(12 + data.length);
  out.writeUInt32BE(data.length, 0);
  out.write(type, 4, 'ascii');
  Buffer.from(data).copy(out, 8);
  out.writeUInt32BE(crc32(out.subarray(4, 8 + data.length)), 8 + data.length);
  return out;
}

function makePng(width, height, { padBytes = 0 } = {}) {
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8; // bit depth
  ihdr[9] = 2; // RGB
  const row = 1 + width * 3;
  const raw = Buffer.alloc(row * height);
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const o = y * row + 1 + x * 3;
      raw[o] = 0x15 + ((x >> 6) & 1) * 0xcf; raw[o + 1] = 0x16; raw[o + 2] = 0x1a;
    }
  }
  const parts = [Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk('IHDR', ihdr)];
  if (padBytes) parts.push(chunk('prVt', randomBytes(padBytes)));
  parts.push(chunk('IDAT', deflateSync(raw)), chunk('IEND', Buffer.alloc(0)));
  return Buffer.concat(parts);
}

const GOOD_PNG = makePng(1200, 630);
const GOOD_B64 = GOOD_PNG.toString('base64');

// ---------- helpers ----------

const hex16 = (n) => n.toString(16).padStart(16, '0');
const hex64 = (n) => createHash('sha256').update(String(n)).digest('hex');

// Minimal JPEG-shaped bytes: SOI + APP0 marker ... EOI (validation checks the markers).
function makeJpeg(padBytes = 64) {
  return Buffer.concat([Buffer.from([0xff, 0xd8, 0xff, 0xe0, 0x00, 0x10, 0x4a, 0x46, 0x49, 0x46, 0x00]), randomBytes(padBytes), Buffer.from([0xff, 0xd9])]);
}

function payload(over = {}) {
  return {
    schema: 'ccs-share/2',
    name: 'My Picks',
    description: 'A few favorites.',
    games: [
      { id: 'a1b2c3d4e5f60718', title: 'Super Mario Bros.', version: 'USA (Rev 1)', year: '1985', publisher: 'Nintendo', art: 'art/a1b2c3d4e5f60718.jpg' },
      { id: '0011223344556677', title: 'Homebrew Quest', version: '', year: '', publisher: '', art: '' },
    ],
    stats: { games: 32, factory: 30, added: 2 },
    image: GOOD_B64,
    ...over,
  };
}

let ipCounter = 1;
const freshIp = () => `198.51.100.${ipCounter++}`;

function req(method, path, { body, headers = {}, ip } = {}) {
  const h = new Headers(headers);
  if (ip !== undefined) h.set('cf-connecting-ip', ip);
  const init = { method, headers: h };
  if (body !== undefined) init.body = typeof body === 'string' || body instanceof Uint8Array ? body : JSON.stringify(body);
  return new Request(SHARE + path, init);
}

function call(env, method, path, opts = {}) {
  return worker.fetch(req(method, path, opts), env, ctx);
}

function publish(env, body, { ip = freshIp(), origin = SITE, contentType = 'application/json', extraHeaders = {} } = {}) {
  const headers = { 'content-type': contentType, ...extraHeaders };
  if (origin) headers.origin = origin;
  return call(env, 'POST', '/api/publish', { body, headers, ip });
}

const sha256 = (s) => createHash('sha256').update(s).digest('hex');

// ---------- runner ----------

const results = [];
function check(name, cond, detail = '') {
  results.push({ name, ok: !!cond });
  console.log(`${cond ? 'PASS' : 'FAIL'}  ${name}${!cond && detail ? `  -- ${detail}` : ''}`);
}

async function expectStatus(name, resPromise, status, codeExpected) {
  const res = await resPromise;
  let detail = `got ${res.status}`;
  let codeOk = true;
  if (codeExpected) {
    const j = await res.clone().json().catch(() => ({}));
    codeOk = j.ok === false && j.code === codeExpected && typeof j.error === 'string';
    detail += ` code=${j.code}`;
  }
  check(name, res.status === status && codeOk, detail);
  return res;
}

// ---------- tests ----------

async function main() {
  const env = makeEnv();

  // --- publish ---
  const nasty = '  Best <script>alert("x")</script> & \'friends\'\u0007  ';
  const pub = await publish(env, payload({ name: nasty, games: [...payload().games, { id: 'ffffffffffffffff', title: 'Line\nBreak\tGame\u0000', art: '' }] }));
  check('publish returns 201', pub.status === 201, `got ${pub.status}`);
  const pj = await pub.json();
  check('publish ok:true', pj.ok === true);
  check('publish id is 10 base62 chars', /^[0-9A-Za-z]{10}$/.test(pj.id || ''), pj.id);
  check('publish url', pj.url === `${SHARE}/c/${pj.id}`, pj.url);
  check('publish image_url', pj.image_url === `${SHARE}/c/${pj.id}/card.png`, pj.image_url);
  check('publish manage_token is 32-byte base64url', /^[A-Za-z0-9_-]{43}$/.test(pj.manage_token || ''));
  check('publish response has ACAO for site origin', pub.headers.get('access-control-allow-origin') === SITE);
  check('publish JSON nosniff', pub.headers.get('x-content-type-options') === 'nosniff');
  const id = pj.id;
  const token = pj.manage_token;

  const stored = await env.SHARE_BUCKET.json(`c/${id}.json`);
  check('stored record has SHA-256(token)', stored && stored.token_hash === sha256(token));
  check('stored record never contains the raw token', !JSON.stringify(stored).includes(token));
  check('stored record has ISO created time', stored && !Number.isNaN(Date.parse(stored.created)) && stored.created.endsWith('Z'));
  check('stored PNG under c/<id>/card.png (R2, image/png)', env.SHARE_BUCKET.map.get(`c/${id}/card.png`)?.bytes.byteLength === GOOD_PNG.length && env.SHARE_BUCKET.map.get(`c/${id}/card.png`).meta.contentType === 'image/png');
  check('stored record content type JSON', env.SHARE_BUCKET.map.get(`c/${id}.json`).meta.contentType === 'application/json');

  // a second publish gets a different id
  const pub2 = await publish(env, payload());
  const pj2 = await pub2.json();
  check('ids are not sequential/repeated', pub2.status === 201 && pj2.id !== id);

  // --- public JSON ---
  const gj = await call(env, 'GET', `/api/c/${id}`, { headers: { origin: SITE } });
  check('GET /api/c/<id> 200', gj.status === 200, `got ${gj.status}`);
  check('GET /api/c/<id> ACAO for allowed origin', gj.headers.get('access-control-allow-origin') === SITE);
  const snapText = await gj.text();
  const snap = JSON.parse(snapText);
  check('JSON name sanitized (trimmed, control chars removed)', snap.name === 'Best <script>alert("x")</script> & \'friends\'', JSON.stringify(snap.name));
  check('JSON game title newline/tab/NUL sanitized', snap.games[2].title === 'Line Break Game', JSON.stringify(snap.games[2].title));
  check('JSON games match input', snap.games.length === 3 && snap.games[0].id === 'a1b2c3d4e5f60718' && snap.games[0].art === 'art/a1b2c3d4e5f60718.jpg' && snap.games[0].version === 'USA (Rev 1)' && snap.games[1].art === '');
  check('JSON optional game fields default to ""', snap.games[2].version === '' && snap.games[2].year === '' && snap.games[2].publisher === '');
  check('JSON stats and description kept', snap.stats.added === 2 && snap.description === 'A few favorites.');
  check('JSON schema/id/url/image_url/created present', snap.schema === 'ccs-share/2' && snap.id === id && snap.url === `${SHARE}/c/${id}` && snap.image_url.endsWith('/card.png') && !!snap.created);
  check('JSON omits token hash and token', !('token_hash' in snap) && !snapText.includes(stored.token_hash) && !snapText.includes(token) && !/token/i.test(snapText));
  check('JSON without Origin has no ACAO', (await call(env, 'GET', `/api/c/${id}`)).headers.get('access-control-allow-origin') === null);
  await expectStatus('GET /api/c/<unknown> 404', call(env, 'GET', '/api/c/ZZZZZZZZZZ'), 404, 'not_found');

  // --- HTML page ---
  const page = await call(env, 'GET', `/c/${id}`);
  const html = await page.text();
  const escTitle = 'Best &lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt; &amp; &#39;friends&#39;';
  check('page 200 text/html', page.status === 200 && page.headers.get('content-type').startsWith('text/html'));
  check('page escapes title in <h1>', html.includes(`<h1>${escTitle}</h1>`));
  check('page contains no <script> at all', !/<script/i.test(html));
  check('page og:title escaped', html.includes(`<meta property="og:title" content="${escTitle}">`));
  check('page <title> escaped', html.includes(`<title>${escTitle} · Classic Cart Studio</title>`));
  check('page og:image absolute card URL', html.includes(`<meta property="og:image" content="${SHARE}/c/${id}/card.png">`));
  check('page og:image size 1200x630', html.includes('<meta property="og:image:width" content="1200">') && html.includes('<meta property="og:image:height" content="630">'));
  check('page og:url', html.includes(`<meta property="og:url" content="${SHARE}/c/${id}">`));
  check('page og:type/site_name', html.includes('<meta property="og:type" content="website">') && html.includes('<meta property="og:site_name" content="Classic Cart Studio">'));
  check('page og:image:alt present', /<meta property="og:image:alt" content="[^"]+">/.test(html));
  check('page twitter:card summary_large_image', html.includes('<meta name="twitter:card" content="summary_large_image">'));
  check('page twitter:image/title', html.includes(`<meta name="twitter:image" content="${SHARE}/c/${id}/card.png">`) && html.includes(`<meta name="twitter:title" content="${escTitle}">`));
  check('page canonical link', html.includes(`<link rel="canonical" href="${SHARE}/c/${id}">`));
  check('page meta description', html.includes('<meta name="description" content="A few favorites.">'));
  check('page robots noindex', html.includes('<meta name="robots" content="noindex">'));
  check('page Save to My Collections link', html.includes(`href="${SITE}/?share=${id}">Save to My Collections</a>`));
  check('page Open Classic Cart Studio link', html.includes(`href="${SITE}">Open Classic Cart Studio</a>`));
  check('page cover art from SITE_ORIGIN', html.includes(`src="${SITE}/art/a1b2c3d4e5f60718.jpg"`));
  check('page shows version text', html.includes('USA (Rev 1)'));
  check('page empty art uses fallback box (no img)', /<div class="cover"><div class="fb" aria-hidden="true"><i><\/i>Homebrew Quest<\/div><\/div>/.test(html));
  check('page footer', html.includes('Shared with Classic Cart Studio &middot; not affiliated with Nintendo'));
  check('page has no third-party URLs', [...html.matchAll(/(?:src|href)="(https?:[^"]+)"/g)].every((m) => m[1].startsWith(SITE) || m[1].startsWith(SHARE)));
  const csp = page.headers.get('content-security-policy') || '';
  check('page CSP', csp.includes("default-src 'none'") && csp.includes(`img-src 'self' ${SHARE} ${SITE} data:`) && csp.includes("frame-ancestors 'none'") && csp.includes("base-uri 'none'") && csp.includes("form-action 'none'"), csp);
  check('page nosniff + referrer policy', page.headers.get('x-content-type-options') === 'nosniff' && page.headers.get('referrer-policy') === 'strict-origin-when-cross-origin');

  // description fallback
  const pubD = await publish(env, payload({ description: '   ', stats: undefined }));
  const idD = (await pubD.json()).id;
  const htmlD = await (await call(env, 'GET', `/c/${idD}`)).text();
  check('description fallback when empty', htmlD.includes('<meta name="description" content="2 NES games collected in Classic Cart Studio.">'));

  // --- card.png ---
  const card = await call(env, 'GET', `/c/${id}/card.png`);
  const cardBytes = Buffer.from(await card.arrayBuffer());
  check('card.png 200 image/png', card.status === 200 && card.headers.get('content-type') === 'image/png');
  check('card.png bytes match upload', cardBytes.equals(GOOD_PNG));
  check('card.png long cache', /max-age=31536000/.test(card.headers.get('cache-control') || ''));
  await expectStatus('card.png unknown 404', call(env, 'GET', '/c/ZZZZZZZZZZ/card.png'), 404);
  const head = await call(env, 'HEAD', `/c/${id}`);
  check('HEAD page 200 without body', head.status === 200 && head.body === null);

  // --- unpublish ---
  const unpub = (body, opts = {}) => call(env, 'POST', '/api/unpublish', { body, headers: { 'content-type': 'application/json', origin: SITE, ...(opts.headers || {}) }, ip: opts.ip || freshIp() });
  const wrong = randomBytes(32).toString('base64url');
  await expectStatus('unpublish wrong token 403', unpub({ id, token: wrong }), 403, 'forbidden');
  await expectStatus('unpublish unknown id 404', unpub({ id: 'ZZZZZZZZZZ', token: wrong }), 404, 'not_found');
  await expectStatus('unpublish malformed body 400', unpub({ id, token: wrong, extra: 1 }), 400, 'unknown_field');
  await expectStatus('unpublish from foreign origin 403', unpub({ id, token }, { headers: { origin: 'https://evil.example' } }), 403, 'origin_not_allowed');
  const okUn = await expectStatus('unpublish right token 200', unpub({ id, token }), 200);
  const okUnJ = await okUn.json();
  check('unpublish response ok', okUnJ.ok === true && okUnJ.unpublished === true);
  const tomb = await env.SHARE_BUCKET.json(`c/${id}.json`);
  check('tombstone replaces snapshot', tomb.unpublished === true && !!tomb.at && !('games' in tomb) && !('token_hash' in tomb));
  check('image deleted on unpublish', !env.SHARE_BUCKET.map.has(`c/${id}/card.png`));
  const gone = await expectStatus('page after unpublish 410', call(env, 'GET', `/c/${id}`), 410);
  const goneHtml = await gone.text();
  check('410 page styled + noindex', goneHtml.includes('<meta name="robots" content="noindex">') && goneHtml.includes('unpublished') && goneHtml.includes('<style>'));
  await expectStatus('JSON after unpublish 410', call(env, 'GET', `/api/c/${id}`), 410, 'unpublished');
  const goneImg = await call(env, 'GET', `/c/${id}/card.png`);
  check('image after unpublish 410/404', goneImg.status === 410 || goneImg.status === 404, `got ${goneImg.status}`);
  await expectStatus('second unpublish 410', unpub({ id, token }), 410, 'unpublished');

  const nf = await call(env, 'GET', '/c/ZZZZZZZZZZ');
  const nfHtml = await nf.text();
  check('unknown page 404 styled + noindex', nf.status === 404 && nfHtml.includes('<meta name="robots" content="noindex">') && nfHtml.includes('Collection not found'));
  check('bad id page 404', (await call(env, 'GET', '/c/bad!id')).status === 404);

  // --- schema violations ---
  const bad = async (name, body, code, status = 400, opts = {}) => expectStatus(name, publish(env, body, opts), status, code);
  await bad('reject unknown top-level key', payload({ extra: 1 }), 'unknown_field');
  await bad('reject unknown game key', payload({ games: [{ id: hex16(1), title: 'X', rom: 'x.nes' }] }), 'unknown_field');
  await bad('reject art "../x"', payload({ games: [{ id: hex16(1), title: 'X', art: '../x' }] }), 'bad_art_path');
  await bad('reject art http:// URL', payload({ games: [{ id: hex16(1), title: 'X', art: 'http://evil.example/x.jpg' }] }), 'bad_art_path');
  await bad('reject art with uppercase/other ext', payload({ games: [{ id: hex16(1), title: 'X', art: 'art/A1B2C3D4E5F60718.png' }] }), 'bad_art_path');
  await bad('reject id starting with "_"', payload({ games: [{ id: '_abc', title: 'X' }] }), 'bad_game_id');
  await bad('reject id with "/"', payload({ games: [{ id: 'a/b', title: 'X' }] }), 'bad_game_id');
  await bad('reject id with space', payload({ games: [{ id: 'a b', title: 'X' }] }), 'bad_game_id');
  await bad('reject 81-char id', payload({ games: [{ id: 'a'.repeat(81), title: 'X' }] }), 'bad_game_id');
  await bad('reject missing id', payload({ games: [{ title: 'X' }] }), 'bad_game_id');
  await bad('reject missing title', payload({ games: [{ id: 'abc' }] }), 'invalid_payload');
  await bad('reject unknown source', payload({ games: [{ id: 'abc', title: 'X', source: 'homebrew' }] }), 'bad_source');
  await bad('reject bad content_sha256', payload({ games: [{ id: 'abc', title: 'X', content_sha256: 'ABC' }] }), 'bad_hash');
  await bad('reject uppercase file_sha256', payload({ games: [{ id: 'abc', title: 'X', file_sha256: hex64(1).toUpperCase() }] }), 'bad_hash');
  await bad('reject size over 16 MiB', payload({ games: [{ id: 'abc', title: 'X', size: 16777217 }] }), 'invalid_payload');
  await bad('reject negative size', payload({ games: [{ id: 'abc', title: 'X', size: -1 }] }), 'invalid_payload');
  const dup = await bad('reject duplicate game id', payload({ games: [{ id: 'p_0123456789abcdef', title: 'A' }, { id: 'p_0123456789abcdef', title: 'B' }] }), 'duplicate_game');
  check('duplicate message names the id', ((await dup.json()).error || '').includes('p_0123456789abcdef'));
  const over = await bad('reject 50001 games (resource safeguard)', payload({ games: Array.from({ length: 50001 }, (_, i) => ({ id: hex16(i), title: `G${i}` })) }), 'too_many_games');
  check('50001 games message', (await over.json()).error === 'A shared collection can hold at most 50000 games; this one has 50001.');
  await bad('reject thumb index past thumbs', payload({ games: [{ id: 'abc', title: 'X', thumb: 1 }], thumbs: [makeJpeg().toString('base64')] }), 'bad_thumb_index');
  await bad('reject thumb index with no thumbs', payload({ games: [{ id: 'abc', title: 'X', thumb: 0 }] }), 'bad_thumb_index');
  await bad('reject thumb index -2', payload({ games: [{ id: 'abc', title: 'X', thumb: -2 }], thumbs: [makeJpeg().toString('base64')] }), 'bad_thumb_index');
  await bad('reject non-integer thumb index', payload({ games: [{ id: 'abc', title: 'X', thumb: '0' }], thumbs: [makeJpeg().toString('base64')] }), 'bad_thumb_index');
  await bad('reject non-JPEG thumb (PNG bytes)', payload({ thumbs: [makePng(8, 8).toString('base64')] }), 'bad_thumb');
  await bad('reject thumb without EOI marker', payload({ thumbs: [makeJpeg().subarray(0, 40).toString('base64')] }), 'bad_thumb');
  await bad('reject thumb not base64', payload({ thumbs: ['***'] }), 'bad_thumb');
  await bad('reject thumb not a string', payload({ thumbs: [5] }), 'bad_thumb');
  await bad('reject thumb over 48 KB', payload({ thumbs: [makeJpeg(48 * 1024).toString('base64')] }), 'thumb_too_large');
  await bad('reject 10001 thumbs', payload({ thumbs: Array.from({ length: 10001 }, () => makeJpeg(4).toString('base64')) }), 'too_many_thumbs');
  await bad('reject home folder with unknown game id', payload({ home: { theme: 'arcade-grid', folders: [{ name: 'F', order: ['a1b2c3d4e5f60718', 'nope'] }] } }), 'bad_home');
  await bad('reject home unknown key', payload({ home: { theme: '', layout: 1 } }), 'unknown_field');
  await bad('reject home folder unknown key', payload({ home: { folders: [{ name: 'F', order: [], color: 'red' }] } }), 'unknown_field');
  await bad('reject home bad theme', payload({ home: { theme: 'neon' } }), 'bad_home');
  await bad('reject home 101 folders', payload({ home: { folders: Array.from({ length: 101 }, (_, i) => ({ name: `F${i}`, order: [] })) } }), 'bad_home');
  await bad('reject home folder empty name', payload({ home: { folders: [{ name: ' ', order: [] }] } }), 'invalid_payload');
  await bad('reject 0 games', payload({ games: [] }), 'invalid_payload');
  await bad('reject 81-char name', payload({ name: 'N'.repeat(81) }), 'invalid_payload');
  await bad('reject empty name', payload({ name: ' \u0001 ' }), 'invalid_payload');
  await bad('reject non-string name', payload({ name: 42 }), 'invalid_payload');
  await bad('reject 281-char description', payload({ description: 'd'.repeat(281) }), 'invalid_payload');
  await bad('reject 121-char title', payload({ games: [{ id: hex16(1), title: 't'.repeat(121) }] }), 'invalid_payload');
  await bad('reject 9-char year', payload({ games: [{ id: hex16(1), title: 'X', year: '198501011' }] }), 'invalid_payload');
  await bad('reject wrong schema', payload({ schema: 'ccs-share/1' }), 'bad_schema');
  await bad('reject stats out of range', payload({ stats: { games: 50001 } }), 'invalid_payload');
  await bad('reject stats non-integer', payload({ stats: { games: 1.5 } }), 'invalid_payload');
  await bad('reject stats unknown key', payload({ stats: { games: 1, extra: 2 } }), 'unknown_field');
  await bad('reject games not array', payload({ games: {} }), 'invalid_payload');
  await bad('reject wrong image size 1200x631', payload({ image: makePng(1200, 631).toString('base64') }), 'bad_image_size');
  await bad('reject wrong image size 630x1200', payload({ image: makePng(630, 1200).toString('base64') }), 'bad_image_size');
  await bad('reject non-PNG image', payload({ image: Buffer.from([0xff, 0xd8, 0xff, 0xe0, ...randomBytes(200)]).toString('base64') }), 'bad_image');
  await bad('reject invalid base64', payload({ image: '***not-base64***' }), 'bad_image');
  await bad('reject truncated PNG', payload({ image: GOOD_PNG.subarray(0, GOOD_PNG.length - 12).toString('base64') }), 'bad_image');
  await bad('reject missing image', payload({ image: undefined }), 'bad_image');
  const bigPng = makePng(1200, 630, { padBytes: 3 * 1024 * 1024 + 5000 });
  const cardPng = makePng(1200, 630, { padBytes: 1500 * 1024 });
  check('accept a 1.5 MB card image (a mosaic of real box art)', (await publish(env, payload({ image: cardPng.toString('base64') }))).status === 201);
  await bad('reject PNG over 3 MB', payload({ image: bigPng.toString('base64') }), 'image_too_large');
  await bad('reject wrong content type', JSON.stringify(payload()), 'unsupported_media_type', 415, { contentType: 'text/plain' });
  await bad('reject missing content type', JSON.stringify(payload()), 'unsupported_media_type', 415, { contentType: '' });
  await bad('reject invalid JSON', '{"schema":', 'invalid_json');
  await bad('reject JSON array body', [], 'invalid_payload');
  const huge = JSON.stringify(payload({ description: 'x'.repeat(24_000_001) }));
  const big413 = await bad('oversized body (streamed) 413', huge, 'payload_too_large', 413);
  check('413 message', (await big413.json()).error === 'The shared copy is larger than the sharing service accepts in one request (limit 24 MB).');
  await bad('oversized Content-Length 413', huge, 'payload_too_large', 413, { extraHeaders: { 'content-length': String(Buffer.byteLength(huge)) } });
  const okCharset = await publish(env, payload(), { contentType: 'application/json; charset=utf-8' });
  check('accept application/json; charset=utf-8', okCharset.status === 201, `got ${okCharset.status}`);
  const okData = await publish(env, payload({ image: `data:image/png;base64,${GOOD_B64}`, name: 'N'.repeat(80), stats: undefined, description: undefined }));
  check('accept data: URL image, 80-char name, no stats/description', okData.status === 201, `got ${okData.status}`);
  // a complete library with personal games: more games than the old 2,000 limit, with three NES Classic Loadouts
  const big = Array.from({ length: 6000 }, (_, i) => ({ id: i < 5990 ? hex16(i) : `p_${hex16(i)}`, title: `Game ${i}`, source: i < 5990 ? 'catalog' : 'personal', thumb: i < 100 ? i : -1 }));
  const los = [{ name: 'Family Favorites', games: big.slice(0, 35).map((g) => g.id) }, { name: 'Everything', games: big.map((g) => g.id), home: { theme: 'adventure-map', folders: [{ name: 'Mine', order: [big[5995].id] }] } }, { name: 'Personal', games: big.slice(5990).map((g) => g.id) }];
  const max = await publish(env, payload({ games: big, thumbs: Array.from({ length: 100 }, () => makeJpeg(40 * 1024).toString('base64')), loadouts: los }));
  check('accept 6000 games with 100 x 40 KB thumbs and 3 loadouts', max.status === 201, `got ${max.status}`);
  const maxId = (await max.json()).id; const maxJ = await (await call(env, 'GET', `/api/c/${maxId}`)).json();
  check('6000-game snapshot returns all 6000 games', maxJ.games.length === 6000 && maxJ.games[5999].id === big[5999].id);
  check('snapshot returns every loadout with its exact games and HOME settings', JSON.stringify(maxJ.loadouts) === JSON.stringify([{ name: 'Family Favorites', games: los[0].games }, { name: 'Everything', games: los[1].games, home: los[1].home }, { name: 'Personal', games: los[2].games }]));
  const maxPage = await (await call(env, 'GET', `/c/${maxId}`, { headers: { 'user-agent': 'Mozilla/5.0' } })).text();
  check('share page lists the loadouts', maxPage.includes('NES Classic Loadouts') && maxPage.includes('Family Favorites') && maxPage.includes('35 games'));
  await bad('reject loadout with a game not in the collection', payload({ games: [{ id: 'abc', title: 'X' }], loadouts: [{ name: 'L', games: ['zzz'] }] }), 'bad_loadout');
  await bad('reject loadout listing a game twice', payload({ games: [{ id: 'abc', title: 'X' }], loadouts: [{ name: 'L', games: ['abc', 'abc'] }] }), 'bad_loadout');
  await bad('reject loadout without a name', payload({ games: [{ id: 'abc', title: 'X' }], loadouts: [{ games: ['abc'] }] }), 'invalid_payload');
  await bad('reject unknown loadout field', payload({ games: [{ id: 'abc', title: 'X' }], loadouts: [{ name: 'L', games: ['abc'], path: 'C:/x' }] }), 'unknown_field');
  await bad('reject 101 loadouts', payload({ games: [{ id: 'abc', title: 'X' }], loadouts: Array.from({ length: 101 }, (_, i) => ({ name: `L${i}`, games: ['abc'] })) }), 'too_many_loadouts');
  const t400 = await publish(env, payload({ games: [{ id: 'abc', title: 'X', thumb: 399 }], thumbs: Array.from({ length: 400 }, () => makeJpeg(8).toString('base64')) }));
  check('accept exactly 400 thumbs', t400.status === 201, `got ${t400.status}`);
  check('stats up to 50000 accepted', (await publish(env, payload({ stats: { games: 50000, factory: 30, added: 49970, personal: 5000, unavailable: 0 } }))).status === 201);

  // --- CORS ---
  const pre = (origin) => call(env, 'OPTIONS', '/api/publish', { headers: { origin, 'access-control-request-method': 'POST', 'access-control-request-headers': 'content-type' } });
  const preOk = await pre(SITE);
  check('preflight allowed origin 204 + ACAO', preOk.status === 204 && preOk.headers.get('access-control-allow-origin') === SITE);
  check('preflight methods/headers', preOk.headers.get('access-control-allow-methods') === 'GET, POST, OPTIONS' && preOk.headers.get('access-control-allow-headers') === 'Content-Type');
  check('preflight no credentials header', preOk.headers.get('access-control-allow-credentials') === null);
  check('preflight www origin allowed', (await pre('https://www.classiccartstudio.com')).headers.get('access-control-allow-origin') === 'https://www.classiccartstudio.com');
  check('preflight 127.0.0.1:8765 allowed', (await pre('http://127.0.0.1:8765')).headers.get('access-control-allow-origin') === 'http://127.0.0.1:8765');
  check('preflight localhost:5173 allowed', (await pre('http://localhost:5173')).headers.get('access-control-allow-origin') === 'http://localhost:5173');
  for (const o of ['https://evil.example', 'https://classiccartstudio.com.evil.example', 'http://classiccartstudio.com', 'http://127.0.0.1.evil.example:80', 'https://127.0.0.1:8765', 'null', 'http://localhost:99999']) {
    check(`preflight disallowed origin ${o} gets no ACAO`, (await pre(o)).headers.get('access-control-allow-origin') === null);
  }
  const foreign = await publish(env, payload(), { origin: 'https://evil.example' });
  check('POST from disallowed origin 403', foreign.status === 403);
  check('POST from disallowed origin has no ACAO', foreign.headers.get('access-control-allow-origin') === null);
  const local = await publish(env, payload(), { origin: 'http://127.0.0.1:8765' });
  check('POST from 127.0.0.1:8765 201 + ACAO', local.status === 201 && local.headers.get('access-control-allow-origin') === 'http://127.0.0.1:8765');
  const noOrigin = await publish(env, payload(), { origin: null });
  check('POST without Origin (server tool) allowed, no ACAO', noOrigin.status === 201 && noOrigin.headers.get('access-control-allow-origin') === null);
  const errCors = await publish(env, payload({ extra: 1 }), { origin: SITE });
  check('error responses carry ACAO for allowed origin', errCors.status === 400 && errCors.headers.get('access-control-allow-origin') === SITE);

  // --- rate limits ---
  const rlEnv = makeEnv({ SHARES: new MemoryKV() });
  const rip = '203.0.113.9';
  let okCount = 0;
  for (let i = 0; i < 20; i++) if ((await publish(rlEnv, payload(), { ip: rip })).status === 201) okCount++;
  check('20 publishes from one IP succeed', okCount === 20, `ok=${okCount}`);
  const r21 = await publish(rlEnv, payload(), { ip: rip });
  const r21j = await r21.json();
  check('21st publish from same IP 429', r21.status === 429 && r21j.code === 'rate_limited' && r21j.ok === false);
  check('429 has Retry-After', !!r21.headers.get('retry-after'));
  check('other IP still allowed', (await publish(rlEnv, payload(), { ip: '203.0.113.10' })).status === 201);
  const rlKey = [...rlEnv.SHARES.map.keys()].find((k) => k.startsWith(`rl:${rip}:`));
  check('counter key rl:<ip>:<hour> with 3600 s TTL (KV when bound)', !!rlKey && rlEnv.SHARES.map.get(rlKey).ttl === 3600, rlKey);
  check('counters not written to R2 when KV is bound', ![...rlEnv.SHARE_BUCKET.map.keys()].some((k) => k.startsWith('rl')));
  const r2rl = makeEnv();
  await publish(r2rl, payload(), { ip: '203.0.113.11' });
  check('counters fall back to R2 objects without KV', [...r2rl.SHARE_BUCKET.map.keys()].some((k) => k.startsWith('rl:203.0.113.11:')));
  const invEnv = makeEnv();
  for (let i = 0; i < 25; i++) await publish(invEnv, payload({ extra: 1 }), { ip: '203.0.113.20' });
  check('invalid publishes do not consume the quota', (await publish(invEnv, payload(), { ip: '203.0.113.20' })).status === 201);

  const unEnv = makeEnv();
  const uip = '203.0.113.30';
  let un403 = 0;
  for (let i = 0; i < 60; i++) {
    const r = await call(unEnv, 'POST', '/api/unpublish', { body: { id: 'ZZZZZZZZZZ', token: wrong }, headers: { 'content-type': 'application/json' }, ip: uip });
    if (r.status === 404) un403++;
  }
  const u61 = await call(unEnv, 'POST', '/api/unpublish', { body: { id: 'ZZZZZZZZZZ', token: wrong }, headers: { 'content-type': 'application/json' }, ip: uip });
  check('60 unpublish attempts allowed, 61st 429', un403 === 60 && u61.status === 429, `allowed=${un403} last=${u61.status}`);

  const denyLimiter = makeLimiter(() => false);
  const limEnv = makeEnv({ PUBLISH_LIMITER: denyLimiter });
  const lim = await publish(limEnv, payload(), { ip: '203.0.113.40' });
  check('PUBLISH_LIMITER denial -> 429', lim.status === 429);
  check('PUBLISH_LIMITER called with CF-Connecting-IP key', denyLimiter.calls[0]?.key === '203.0.113.40');
  const allowLimiter = makeLimiter(() => true);
  check('PUBLISH_LIMITER success -> 201', (await publish(makeEnv({ PUBLISH_LIMITER: allowLimiter }), payload())).status === 201 && allowLimiter.calls.length === 1);

  // --- methods and routing ---
  const put = await call(env, 'PUT', '/api/publish', { body: '{}', headers: { 'content-type': 'application/json' } });
  check('PUT /api/publish 405 with Allow', put.status === 405 && put.headers.get('allow') === 'POST, OPTIONS');
  const del = await call(env, 'DELETE', `/c/${idD}`);
  check('DELETE /c/<id> 405 with Allow', del.status === 405 && del.headers.get('allow') === 'GET, HEAD');
  check('GET /api/publish 405', (await call(env, 'GET', '/api/publish')).status === 405);
  check('DELETE /api/c/<id> 405', (await call(env, 'DELETE', `/api/c/${idD}`)).status === 405);
  const root = await call(env, 'GET', '/');
  check('GET / redirects 302 to SITE_ORIGIN', root.status === 302 && root.headers.get('location') === SITE);
  await expectStatus('unknown path 404', call(env, 'GET', '/wp-admin'), 404, 'not_found');
  await expectStatus('unknown api path 404', call(env, 'GET', '/api/nope'), 404, 'not_found');

  // --- mixed 120-game collection with thumbs (R2 primary) ---
  const mixEnv = makeEnv();
  const thumbs = Array.from({ length: 5 }, (_, i) => makeJpeg(100 + i));
  const mixed = [];
  for (let i = 0; i < 120; i++) {
    const kind = i % 4;
    if (kind === 0) mixed.push({ id: hex16(0xa000 + i), title: `Catalog <Game> ${i}`, version: 'USA', year: '1988', publisher: 'Nintendo', source: 'catalog', art: `art/${hex16(0xa000 + i)}.jpg` });
    else if (kind === 1) mixed.push({ id: `p_${hex16(0xb000 + i)}`, title: `Personal Hack ${i}`, version: 'v1.2 (hack)', source: 'personal', content_sha256: hex64(`c${i}`), file_sha256: hex64(`f${i}`), size: 40976 + i });
    else if (kind === 2) mixed.push({ id: `unk:${i}.rom-x`, title: `Unknown Dump ${i}`, source: 'unknown', file_sha256: hex64(`u${i}`), size: 24592 });
    else mixed.push({ id: hex16(0xc000 + i), title: `Extended ${i}`, source: 'extended', year: '1990' });
  }
  mixed[118] = { id: hex16(0xd001), title: 'Same Title', version: 'USA (Rev 0)', source: 'catalog' };
  mixed[119] = { id: hex16(0xd002), title: 'Same Title', version: 'USA (Rev 1)', source: 'catalog' };
  // Five personal games carry uploaded thumbs; one catalog game has no "source" (defaults to catalog).
  const thumbGames = [1, 5, 9, 13, 17];
  thumbGames.forEach((gi, n) => { mixed[gi].thumb = n; });
  mixed[3].thumb = -1;
  delete mixed[4].source;
  const personalIds = mixed.filter((g) => g.source === 'personal').map((g) => g.id);
  const mixBody = payload({
    name: 'Everything I Own',
    games: mixed,
    thumbs: thumbs.map((t, i) => (i === 0 ? `data:image/jpeg;base64,${t.toString('base64')}` : t.toString('base64'))),
    stats: { games: 120, factory: 30, added: 90, personal: 30, unavailable: 30 },
    home: { theme: 'featured-shelf', folders: [{ name: 'My hacks', order: personalIds }, { name: 'Empty', order: [] }] },
  });
  const mixPub = await publish(mixEnv, mixBody);
  check('mixed 120-game collection publishes (201)', mixPub.status === 201, `got ${mixPub.status} ${mixPub.status !== 201 ? await mixPub.clone().text() : ''}`);
  const mixJ = await mixPub.json();
  const mixId = mixJ.id;
  const mixUrl = `${SHARE}/c/${mixId}`;
  const mSnap = await (await call(mixEnv, 'GET', `/api/c/${mixId}`)).json();
  check('mixed: all 120 games returned', mSnap.games.length === 120);
  check('mixed: order preserved', mSnap.games.every((g, i) => g.id === mixed[i].id));
  check('mixed: personal p_ ids, hashes and size kept', mSnap.games[1].id === mixed[1].id && mSnap.games[1].source === 'personal' && mSnap.games[1].content_sha256 === hex64('c1') && mSnap.games[1].file_sha256 === hex64('f1') && mSnap.games[1].size === 40977);
  check('mixed: unknown ids kept with source unknown', mSnap.games[2].id === 'unk:2.rom-x' && mSnap.games[2].source === 'unknown');
  check('mixed: extended source kept', mSnap.games[3].source === 'extended');
  check('mixed: missing source defaults to catalog', mSnap.games[4].source === 'catalog');
  check('mixed: two versions of one title both kept', mSnap.games[118].title === 'Same Title' && mSnap.games[119].title === 'Same Title' && mSnap.games[118].version !== mSnap.games[119].version);
  check('mixed: thumb_url absolute for thumbed games', thumbGames.every((gi, n) => mSnap.games[gi].thumb === n && mSnap.games[gi].thumb_url === `${mixUrl}/t/${n}.jpg`));
  check('mixed: no thumb_url without thumb (incl. thumb -1)', mSnap.games.filter((g, i) => !thumbGames.includes(i)).every((g) => !('thumb_url' in g) && !('thumb' in g)));
  check('mixed: home kept', mSnap.home && mSnap.home.theme === 'featured-shelf' && mSnap.home.folders.length === 2 && mSnap.home.folders[0].name === 'My hacks' && mSnap.home.folders[0].order.join() === personalIds.join());
  check('mixed: stats kept (personal, unavailable)', mSnap.stats.personal === 30 && mSnap.stats.unavailable === 30);
  check('mixed: public JSON has no token hash / thumb_count', !/token|thumb_count/.test(JSON.stringify(mSnap)));
  for (let n = 0; n < 5; n++) {
    const t = await call(mixEnv, 'GET', `/c/${mixId}/t/${n}.jpg`);
    const tb = Buffer.from(await t.arrayBuffer());
    check(`mixed: thumb ${n} served as image/jpeg with long cache`, t.status === 200 && t.headers.get('content-type') === 'image/jpeg' && /max-age=31536000/.test(t.headers.get('cache-control') || '') && tb.equals(thumbs[n]));
  }
  check('mixed: thumbs stored under c/<id>/t/<n>.jpg', [0, 1, 2, 3, 4].every((n) => mixEnv.SHARE_BUCKET.map.get(`c/${mixId}/t/${n}.jpg`)?.meta.contentType === 'image/jpeg'));
  await expectStatus('mixed: thumb index past count 404', call(mixEnv, 'GET', `/c/${mixId}/t/5.jpg`), 404);
  await expectStatus('mixed: thumb with leading zero 404', call(mixEnv, 'GET', `/c/${mixId}/t/01.jpg`), 404);
  await expectStatus('thumb for unknown share 404', call(mixEnv, 'GET', '/c/ZZZZZZZZZZ/t/0.jpg'), 404);
  check('POST thumb 405', (await call(mixEnv, 'POST', `/c/${mixId}/t/0.jpg`)).status === 405);
  const mHtml = await (await call(mixEnv, 'GET', `/c/${mixId}`)).text();
  check('mixed page: lists all 120 games', (mHtml.match(/<li class="game">/g) || []).length === 120);
  check('mixed page: every title present (escaped)', mixed.every((g) => mHtml.includes(`<span class="t">${g.title.replace(/</g, '&lt;').replace(/>/g, '&gt;')}</span>`)));
  check('mixed page: thumb cover uses thumb_url', mHtml.includes(`src="${mixUrl}/t/0.jpg"`) && mHtml.includes(`src="${mixUrl}/t/4.jpg"`));
  check('mixed page: catalog art cover from SITE_ORIGIN', mHtml.includes(`src="${SITE}/art/${hex16(0xa000)}.jpg"`));
  check('mixed page: fallback box for unknown without art', /<div class="cover"><div class="fb" aria-hidden="true"><i><\/i>Unknown Dump 2<\/div><\/div>/.test(mHtml));
  check('mixed page: "Personal game" label', (mHtml.match(/<span class="s">Personal game<\/span>/g) || []).length === 30);
  check('mixed page: unknown label', (mHtml.match(/<span class="s">Not in the Classic Cart Studio library<\/span>/g) || []).length === 29); // game 118 became a catalog version
  check('mixed page: version lines shown', mHtml.includes('<span class="v">v1.2 (hack)</span>') && mHtml.includes('USA (Rev 0)') && mHtml.includes('USA (Rev 1)'));
  check('mixed page: stats use total games', mHtml.includes('<li><b>120</b>games</li>') && mHtml.includes('<li><b>30</b>personal</li>'));
  check('mixed page: header count 120', mHtml.includes('Games in this collection <span>120</span>'));
  check('mixed page: no script', !/<script/i.test(mHtml));
  // Unpublish removes card and thumbs; the tombstone answers 410.
  const mixUn = await call(mixEnv, 'POST', '/api/unpublish', { body: { id: mixId, token: mixJ.manage_token }, headers: { 'content-type': 'application/json', origin: SITE }, ip: freshIp() });
  check('mixed: unpublish 200', mixUn.status === 200);
  check('mixed: unpublish deleted card and all thumbs', [...mixEnv.SHARE_BUCKET.map.keys()].filter((k) => k.startsWith(`c/${mixId}/`)).length === 0);
  check('mixed: tombstone kept', (await mixEnv.SHARE_BUCKET.json(`c/${mixId}.json`)).unpublished === true);
  for (const n of [0, 4]) {
    const g = await call(mixEnv, 'GET', `/c/${mixId}/t/${n}.jpg`);
    check(`mixed: thumb ${n} after unpublish 410/404`, g.status === 410 || g.status === 404, `got ${g.status}`);
  }
  await expectStatus('mixed: page after unpublish 410', call(mixEnv, 'GET', `/c/${mixId}`), 410);

  // --- KV fallback smoke test (no R2 bucket bound) ---
  const kvEnv = { SHARES: new MemoryKV(), SITE_ORIGIN: SITE, SHARE_ORIGIN: SHARE };
  const kvPub = await publish(kvEnv, payload({ games: [{ id: 'p_00000000000000aa', title: 'KV Personal', source: 'personal', thumb: 0 }, { id: 'zz', title: 'KV Unknown', source: 'unknown' }], thumbs: [thumbs[0].toString('base64')] }));
  const kvJ = await kvPub.json();
  check('KV fallback: publish 201 stored in KV', kvPub.status === 201 && kvEnv.SHARES.map.has(`c:${kvJ.id}`) && kvEnv.SHARES.map.has(`img:${kvJ.id}`) && kvEnv.SHARES.map.has(`t:${kvJ.id}:0`));
  const kvSnap = await (await call(kvEnv, 'GET', `/api/c/${kvJ.id}`)).json();
  check('KV fallback: snapshot round-trips', kvSnap.games.length === 2 && kvSnap.games[0].thumb_url === `${SHARE}/c/${kvJ.id}/t/0.jpg`);
  check('KV fallback: card served', Buffer.from(await (await call(kvEnv, 'GET', `/c/${kvJ.id}/card.png`)).arrayBuffer()).equals(GOOD_PNG));
  check('KV fallback: thumb served', Buffer.from(await (await call(kvEnv, 'GET', `/c/${kvJ.id}/t/0.jpg`)).arrayBuffer()).equals(thumbs[0]));
  const kvUn = await call(kvEnv, 'POST', '/api/unpublish', { body: { id: kvJ.id, token: kvJ.manage_token }, headers: { 'content-type': 'application/json', origin: SITE }, ip: freshIp() });
  check('KV fallback: unpublish removes card and thumb', kvUn.status === 200 && !kvEnv.SHARES.map.has(`img:${kvJ.id}`) && !kvEnv.SHARES.map.has(`t:${kvJ.id}:0`));
  check('KV fallback: thumb after unpublish 410', (await call(kvEnv, 'GET', `/c/${kvJ.id}/t/0.jpg`)).status === 410);
  const quiet = console.error;
  console.error = () => {};
  const noStore = await call({ SITE_ORIGIN: SITE }, 'GET', `/c/${kvJ.id}`).finally(() => { console.error = quiet; });
  check('no storage binding -> 500 (no crash)', noStore.status === 500);

  // --- summary ---
  const failed = results.filter((r) => !r.ok).map((r) => r.name);
  if (failed.length) {
    console.log(`RESULT: FAIL [${failed.join('; ')}]`);
    process.exit(1);
  }
  console.log(`RESULT: PASS (${results.length} checks)`);
}

main().catch((err) => {
  console.error(err);
  console.log(`RESULT: FAIL [exception: ${err && err.message}]`);
  process.exit(1);
});
