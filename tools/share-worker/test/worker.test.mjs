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
      httpMetadata: e.meta,
      async arrayBuffer() { return e.bytes.slice().buffer; },
      async json() { return JSON.parse(new TextDecoder().decode(e.bytes)); },
    };
  }
  async put(key, value, opts = {}) {
    const bytes = typeof value === 'string' ? new TextEncoder().encode(value) : new Uint8Array(ArrayBuffer.isView(value) ? value.buffer.slice(value.byteOffset, value.byteOffset + value.byteLength) : value);
    this.map.set(key, { bytes, meta: opts.httpMetadata || {} });
  }
  async delete(key) {
    this.map.delete(key);
  }
}

function makeLimiter(allow) {
  const calls = [];
  return { calls, async limit(arg) { calls.push(arg); return { success: allow(calls.length, arg) }; } };
}

function makeEnv(extra = {}) {
  return { SHARES: new MemoryKV(), SITE_ORIGIN: SITE, SHARE_ORIGIN: SHARE, ...extra };
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

function payload(over = {}) {
  return {
    schema: 'ccs-share/1',
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

  const stored = await env.SHARES.get(`c:${id}`, { type: 'json' });
  check('stored record has SHA-256(token)', stored && stored.token_hash === sha256(token));
  check('stored record never contains the raw token', !JSON.stringify(stored).includes(token));
  check('stored record has ISO created time', stored && !Number.isNaN(Date.parse(stored.created)) && stored.created.endsWith('Z'));
  check('stored PNG under img:<id>', (await env.SHARES.get(`img:${id}`, { type: 'arrayBuffer' }))?.byteLength === GOOD_PNG.length);

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
  check('JSON schema/id/url/image_url/created present', snap.schema === 'ccs-share/1' && snap.id === id && snap.url === `${SHARE}/c/${id}` && snap.image_url.endsWith('/card.png') && !!snap.created);
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
  check('page CSP', csp.includes("default-src 'none'") && csp.includes(`img-src 'self' ${SITE} data:`) && csp.includes("frame-ancestors 'none'") && csp.includes("base-uri 'none'") && csp.includes("form-action 'none'"), csp);
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
  const tomb = await env.SHARES.get(`c:${id}`, { type: 'json' });
  check('tombstone replaces snapshot', tomb.unpublished === true && !!tomb.at && !('games' in tomb) && !('token_hash' in tomb));
  check('image deleted on unpublish', (await env.SHARES.get(`img:${id}`)) === null);
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
  await bad('reject personal p_ id', payload({ games: [{ id: 'p_0123456789abcdef', title: 'Mine' }] }), 'bad_game_id');
  await bad('reject uppercase hex id', payload({ games: [{ id: 'A1B2C3D4E5F60718', title: 'X' }] }), 'bad_game_id');
  await bad('reject 501 games', payload({ games: Array.from({ length: 501 }, (_, i) => ({ id: hex16(i), title: `G${i}` })) }), 'too_many_games');
  await bad('reject 0 games', payload({ games: [] }), 'invalid_payload');
  await bad('reject 81-char name', payload({ name: 'N'.repeat(81) }), 'invalid_payload');
  await bad('reject empty name', payload({ name: ' \u0001 ' }), 'invalid_payload');
  await bad('reject non-string name', payload({ name: 42 }), 'invalid_payload');
  await bad('reject 281-char description', payload({ description: 'd'.repeat(281) }), 'invalid_payload');
  await bad('reject 121-char title', payload({ games: [{ id: hex16(1), title: 't'.repeat(121) }] }), 'invalid_payload');
  await bad('reject 9-char year', payload({ games: [{ id: hex16(1), title: 'X', year: '198501011' }] }), 'invalid_payload');
  await bad('reject wrong schema', payload({ schema: 'ccs-share/2' }), 'bad_schema');
  await bad('reject stats out of range', payload({ stats: { games: 1001 } }), 'invalid_payload');
  await bad('reject stats non-integer', payload({ stats: { games: 1.5 } }), 'invalid_payload');
  await bad('reject stats unknown key', payload({ stats: { games: 1, extra: 2 } }), 'unknown_field');
  await bad('reject games not array', payload({ games: {} }), 'invalid_payload');
  await bad('reject wrong image size 1200x631', payload({ image: makePng(1200, 631).toString('base64') }), 'bad_image_size');
  await bad('reject wrong image size 630x1200', payload({ image: makePng(630, 1200).toString('base64') }), 'bad_image_size');
  await bad('reject non-PNG image', payload({ image: Buffer.from([0xff, 0xd8, 0xff, 0xe0, ...randomBytes(200)]).toString('base64') }), 'bad_image');
  await bad('reject invalid base64', payload({ image: '***not-base64***' }), 'bad_image');
  await bad('reject truncated PNG', payload({ image: GOOD_PNG.subarray(0, GOOD_PNG.length - 12).toString('base64') }), 'bad_image');
  await bad('reject missing image', payload({ image: undefined }), 'bad_image');
  const bigPng = makePng(1200, 630, { padBytes: 705 * 1024 });
  await bad('reject PNG over 700 KB (body under 1 MB)', payload({ image: bigPng.toString('base64') }), 'image_too_large');
  await bad('reject wrong content type', JSON.stringify(payload()), 'unsupported_media_type', 415, { contentType: 'text/plain' });
  await bad('reject missing content type', JSON.stringify(payload()), 'unsupported_media_type', 415, { contentType: '' });
  await bad('reject invalid JSON', '{"schema":', 'invalid_json');
  await bad('reject JSON array body', [], 'invalid_payload');
  const huge = JSON.stringify(payload({ description: 'x'.repeat(1_000_001) }));
  await bad('oversized body (streamed) 413', huge, 'payload_too_large', 413);
  await bad('oversized Content-Length 413', huge, 'payload_too_large', 413, { extraHeaders: { 'content-length': String(Buffer.byteLength(huge)) } });
  const okCharset = await publish(env, payload(), { contentType: 'application/json; charset=utf-8' });
  check('accept application/json; charset=utf-8', okCharset.status === 201, `got ${okCharset.status}`);
  const okData = await publish(env, payload({ image: `data:image/png;base64,${GOOD_B64}`, name: 'N'.repeat(80), stats: undefined, description: undefined }));
  check('accept data: URL image, 80-char name, no stats/description', okData.status === 201, `got ${okData.status}`);
  const max = await publish(env, payload({ games: Array.from({ length: 500 }, (_, i) => ({ id: hex16(i), title: `Game ${i}` })) }));
  check('accept exactly 500 games', max.status === 201, `got ${max.status}`);

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
  const rlEnv = makeEnv();
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
  check('counter key rl:<ip>:<hour> with 3600 s TTL', !!rlKey && rlEnv.SHARES.map.get(rlKey).ttl === 3600, rlKey);
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

  // --- R2 adapter ---
  const r2Env = { SHARE_BUCKET: new MemoryR2(), SHARES: new MemoryKV(), SITE_ORIGIN: SITE, SHARE_ORIGIN: SHARE };
  const r2p = await publish(r2Env, payload());
  const r2j = await r2p.json();
  check('R2 adapter: publish stores in bucket', r2p.status === 201 && r2Env.SHARE_BUCKET.map.has(`c:${r2j.id}`) && r2Env.SHARE_BUCKET.map.has(`img:${r2j.id}`) && !r2Env.SHARES.map.has(`c:${r2j.id}`));
  check('R2 adapter: image content type', r2Env.SHARE_BUCKET.map.get(`img:${r2j.id}`).meta.contentType === 'image/png');
  const r2card = Buffer.from(await (await call(r2Env, 'GET', `/c/${r2j.id}/card.png`)).arrayBuffer());
  check('R2 adapter: card.png served', r2card.equals(GOOD_PNG));

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
