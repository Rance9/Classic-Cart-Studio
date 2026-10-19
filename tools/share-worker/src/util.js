// Shared helpers: errors, responses, CORS, crypto.

export class HttpError extends Error {
  constructor(status, code, message, headers) {
    super(message);
    this.status = status;
    this.code = code;
    this.headers = headers || {};
  }
}

export function jsonResponse(obj, status = 200, headers = {}) {
  return new Response(JSON.stringify(obj), {
    status,
    headers: {
      'Content-Type': 'application/json; charset=utf-8',
      'X-Content-Type-Options': 'nosniff',
      'Cache-Control': 'no-store',
      ...headers,
    },
  });
}

export function errorResponse(status, code, message, headers = {}) {
  return jsonResponse({ ok: false, error: message, code }, status, headers);
}

export function textResponse(body, status, headers = {}) {
  return new Response(body, {
    status,
    headers: {
      'Content-Type': 'text/plain; charset=utf-8',
      'X-Content-Type-Options': 'nosniff',
      'Cache-Control': 'no-store',
      ...headers,
    },
  });
}

export function escapeHtml(value) {
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

// ---- Origins and CORS ----

const FIXED_ORIGINS = new Set(['https://classiccartstudio.com', 'https://www.classiccartstudio.com']);
const LOCAL_ORIGIN = /^http:\/\/(?:127\.0\.0\.1|localhost):([0-9]{1,5})$/;

export function normalizeOrigin(value, fallback) {
  try {
    const u = new URL(value);
    if (u.protocol === 'https:' || u.protocol === 'http:') return u.origin;
  } catch {
    /* fall through */
  }
  return fallback;
}

export function isAllowedOrigin(origin, siteOrigin) {
  if (!origin) return false;
  if (FIXED_ORIGINS.has(origin) || origin === siteOrigin) return true;
  const m = LOCAL_ORIGIN.exec(origin);
  if (!m) return false;
  const port = Number(m[1]);
  return port >= 1 && port <= 65535;
}

export function corsHeaders(origin, siteOrigin) {
  const h = { Vary: 'Origin' };
  if (isAllowedOrigin(origin, siteOrigin)) h['Access-Control-Allow-Origin'] = origin;
  return h;
}

export function preflightHeaders(origin, siteOrigin) {
  const h = corsHeaders(origin, siteOrigin);
  if (h['Access-Control-Allow-Origin']) {
    h['Access-Control-Allow-Methods'] = 'GET, POST, OPTIONS';
    h['Access-Control-Allow-Headers'] = 'Content-Type';
    h['Access-Control-Max-Age'] = '86400';
  }
  return h;
}

export function withHeaders(response, headers) {
  for (const [k, v] of Object.entries(headers)) response.headers.set(k, v);
  return response;
}

// ---- Crypto ----

const BASE62 = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz';

export function randomId(length = 10) {
  let out = '';
  while (out.length < length) {
    const buf = crypto.getRandomValues(new Uint8Array(length * 2));
    for (const b of buf) {
      if (b < 248 && out.length < length) out += BASE62[b % 62]; // 248 = 4*62, unbiased
    }
  }
  return out;
}

export function randomToken() {
  const bytes = crypto.getRandomValues(new Uint8Array(32));
  let bin = '';
  for (const b of bytes) bin += String.fromCharCode(b);
  return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

export async function sha256Hex(text) {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text));
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, '0')).join('');
}

export function constantTimeEqual(a, b) {
  if (typeof a !== 'string' || typeof b !== 'string') return false;
  const len = Math.max(a.length, b.length);
  let diff = a.length ^ b.length;
  for (let i = 0; i < len; i++) diff |= (a.charCodeAt(i) || 0) ^ (b.charCodeAt(i) || 0);
  return diff === 0;
}

// ---- Request body ----

export function requireJsonContentType(request) {
  const ct = (request.headers.get('content-type') || '').split(';')[0].trim().toLowerCase();
  if (ct !== 'application/json') {
    throw new HttpError(415, 'unsupported_media_type', 'Content-Type must be application/json.');
  }
}

export async function readJsonBody(request, limit) {
  const tooLarge = () => new HttpError(413, 'payload_too_large', `Request body must be at most ${limit} bytes.`);
  const cl = request.headers.get('content-length');
  if (cl !== null) {
    const n = Number(cl);
    if (!Number.isFinite(n) || n < 0) throw new HttpError(400, 'bad_request', 'Invalid Content-Length.');
    if (n > limit) throw tooLarge();
  }
  if (!request.body) throw new HttpError(400, 'invalid_json', 'Request body is empty.');
  const reader = request.body.getReader();
  const chunks = [];
  let total = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    total += value.byteLength;
    if (total > limit) {
      try {
        await reader.cancel();
      } catch {
        /* ignore */
      }
      throw tooLarge();
    }
    chunks.push(value);
  }
  const bytes = new Uint8Array(total);
  let off = 0;
  for (const c of chunks) {
    bytes.set(c, off);
    off += c.byteLength;
  }
  let textBody;
  try {
    textBody = new TextDecoder('utf-8', { fatal: true }).decode(bytes);
  } catch {
    throw new HttpError(400, 'invalid_json', 'Request body is not valid UTF-8.');
  }
  try {
    return JSON.parse(textBody);
  } catch {
    throw new HttpError(400, 'invalid_json', 'Request body is not valid JSON.');
  }
}

export function clientIp(request) {
  const raw = request.headers.get('cf-connecting-ip') || '';
  const ip = raw.trim().slice(0, 64);
  return /^[0-9A-Fa-f:.]+$/.test(ip) ? ip : 'unknown';
}
