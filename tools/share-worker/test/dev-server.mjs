// Local development server for the share Worker (tests and previews only): the Worker's own fetch handler behind node:http, with an
// in-memory R2-like bucket bound as SHARE_BUCKET (the primary store). Set SHARE_DEV_KV=1 to also bind an in-memory KV namespace
// SHARES for the hourly counters. Nothing is stored on disk; stopping the process forgets every share.
//   node tools/share-worker/test/dev-server.mjs <port> [site origin]
import http from 'node:http';
import worker from '../src/worker.js';

const port = Number(process.argv[2] || 8787);
const siteOrigin = process.argv[3] || 'https://classiccartstudio.com';

const toBytes = (value) => (typeof value === 'string' ? Buffer.from(value, 'utf8') : Buffer.from(value instanceof ArrayBuffer ? new Uint8Array(value) : value));
const toArrayBuffer = (b) => b.buffer.slice(b.byteOffset, b.byteOffset + b.byteLength);

const objects = new Map();
const SHARE_BUCKET = {
  async get(key) {
    const e = objects.get(key); if (!e) return null;
    return { key, size: e.v.byteLength, httpMetadata: e.meta,
      async text() { return e.v.toString('utf8'); }, async json() { return JSON.parse(e.v.toString('utf8')); },
      async arrayBuffer() { return toArrayBuffer(Buffer.from(e.v)); } };
  },
  async put(key, value, opts) { objects.set(key, { v: Buffer.from(toBytes(value)), meta: (opts && opts.httpMetadata) || {} }); return { key }; },
  async delete(keys) { for (const k of Array.isArray(keys) ? keys : [keys]) objects.delete(k); },
  async list(opts) { const prefix = (opts && opts.prefix) || ''; return { objects: [...objects.keys()].filter((k) => k.startsWith(prefix)).map((key) => ({ key })), truncated: false }; },
};

const mem = new Map();
const SHARES = {
  async get(key, opts) { const e = mem.get(key); if (!e || (e.exp && e.exp < Date.now())) return null; const t = typeof opts === 'string' ? opts : (opts && opts.type) || 'text';
    if (t === 'json') return JSON.parse(e.v.toString('utf8')); if (t === 'arrayBuffer') return toArrayBuffer(e.v); return e.v.toString('utf8'); },
  async put(key, value, opts) { mem.set(key, { v: toBytes(value), exp: opts && opts.expirationTtl ? Date.now() + opts.expirationTtl * 1000 : 0 }); },
  async delete(key) { mem.delete(key); },
};

const env = { SHARE_BUCKET, SITE_ORIGIN: siteOrigin, SHARE_ORIGIN: `http://127.0.0.1:${port}` };
if (process.env.SHARE_DEV_KV === '1') env.SHARES = SHARES;
http.createServer(async (req, res) => {
  const chunks = []; for await (const c of req) chunks.push(c);
  const headers = new Headers(); for (const [k, v] of Object.entries(req.headers)) if (v !== undefined) headers.set(k, Array.isArray(v) ? v.join(', ') : v);
  headers.set('CF-Connecting-IP', req.socket.remoteAddress || '127.0.0.1');
  const body = ['GET', 'HEAD'].includes(req.method) ? undefined : Buffer.concat(chunks);
  const r = await worker.fetch(new Request(`http://127.0.0.1:${port}${req.url}`, { method: req.method, headers, body }), env, { waitUntil() {} });
  const out = Buffer.from(await r.arrayBuffer()); const h = {}; r.headers.forEach((v, k) => { h[k] = v; });
  res.writeHead(r.status, h); res.end(out);
}).listen(port, '127.0.0.1', () => console.log(`share dev server on http://127.0.0.1:${port} (site ${siteOrigin}; R2 in memory${env.SHARES ? ', KV counters' : ''})`));
