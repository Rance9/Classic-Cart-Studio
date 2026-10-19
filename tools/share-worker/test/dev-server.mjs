// Local development server for the share Worker (tests and previews only): the Worker's own fetch handler behind node:http, with an
// in-memory KV namespace. Nothing is stored on disk; stopping the process forgets every share.
//   node tools/share-worker/test/dev-server.mjs <port> [site origin]
import http from 'node:http';
import worker from '../src/worker.js';

const port = Number(process.argv[2] || 8787);
const siteOrigin = process.argv[3] || 'https://classiccartstudio.com';
const mem = new Map();
const SHARES = {
  async get(key, opts) { const e = mem.get(key); if (!e || (e.exp && e.exp < Date.now())) return null; const t = typeof opts === 'string' ? opts : (opts && opts.type) || 'text';
    if (t === 'json') return JSON.parse(Buffer.from(e.v).toString('utf8')); if (t === 'arrayBuffer') return e.v.buffer.slice(e.v.byteOffset, e.v.byteOffset + e.v.byteLength); return Buffer.from(e.v).toString('utf8'); },
  async put(key, value, opts) { const v = typeof value === 'string' ? Buffer.from(value, 'utf8') : Buffer.from(value instanceof ArrayBuffer ? new Uint8Array(value) : value);
    mem.set(key, { v, exp: opts && opts.expirationTtl ? Date.now() + opts.expirationTtl * 1000 : 0 }); },
  async delete(key) { mem.delete(key); },
};
const env = { SHARES, SITE_ORIGIN: siteOrigin, SHARE_ORIGIN: `http://127.0.0.1:${port}` };
http.createServer(async (req, res) => {
  const chunks = []; for await (const c of req) chunks.push(c);
  const headers = new Headers(); for (const [k, v] of Object.entries(req.headers)) if (v !== undefined) headers.set(k, Array.isArray(v) ? v.join(', ') : v);
  headers.set('CF-Connecting-IP', req.socket.remoteAddress || '127.0.0.1');
  const body = ['GET', 'HEAD'].includes(req.method) ? undefined : Buffer.concat(chunks);
  const r = await worker.fetch(new Request(`http://127.0.0.1:${port}${req.url}`, { method: req.method, headers, body }), env, { waitUntil() {} });
  const out = Buffer.from(await r.arrayBuffer()); const h = {}; r.headers.forEach((v, k) => { h[k] = v; });
  res.writeHead(r.status, h); res.end(out);
}).listen(port, '127.0.0.1', () => console.log(`share dev server on http://127.0.0.1:${port} (site ${siteOrigin})`));
