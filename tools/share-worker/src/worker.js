// Classic Cart Studio share service (Cloudflare Worker, ES module).
// Routes: POST /api/publish, POST /api/unpublish, GET /api/c/<id>,
//         GET /c/<id>, GET /c/<id>/card.png, GET / (redirect).
import { getStore, getCounterStore } from './storage.js';
import { validatePublish, validateUnpublish } from './validate.js';
import { renderSharePage, renderMessagePage, htmlHeaders } from './render.js';
import {
  HttpError,
  jsonResponse,
  errorResponse,
  textResponse,
  normalizeOrigin,
  isAllowedOrigin,
  corsHeaders,
  preflightHeaders,
  withHeaders,
  randomId,
  randomToken,
  sha256Hex,
  constantTimeEqual,
  requireJsonContentType,
  readJsonBody,
  clientIp,
} from './util.js';

export const MAX_BODY_BYTES = 1_000_000;
export const PUBLISH_PER_HOUR = 20;
export const UNPUBLISH_PER_HOUR = 60;
const ID_PATTERN = /^[0-9A-Za-z]{10}$/;
const PAGE_CACHE = 'public, max-age=60';

function config(env, request) {
  const siteOrigin = normalizeOrigin(env.SITE_ORIGIN, 'https://classiccartstudio.com');
  const shareOrigin = normalizeOrigin(env.SHARE_ORIGIN, new URL(request.url).origin);
  return { siteOrigin, shareOrigin };
}

function methodNotAllowed(allow, extra = {}) {
  return errorResponse(405, 'method_not_allowed', 'Method not allowed.', { Allow: allow, ...extra });
}

function notFoundJson(extra = {}) {
  return errorResponse(404, 'not_found', 'Not found.', extra);
}

async function loadRecord(env, id) {
  if (!ID_PATTERN.test(id)) return null;
  return getStore(env).getJSON(`c:${id}`);
}

// ---- Abuse limits ----

async function hourlyLimit(env, prefix, ip, max) {
  const counters = getCounterStore(env);
  const hour = Math.floor(Date.now() / 3_600_000);
  const key = `${prefix}:${ip}:${hour}`;
  const n = await counters.get(key);
  if (n >= max) {
    throw new HttpError(429, 'rate_limited', 'Too many requests. Try again later.', { 'Retry-After': '3600' });
  }
  return async () => {
    try {
      await counters.put(key, n + 1, 3600);
    } catch {
      /* KV write limits must not break the request */
    }
  };
}

async function edgeLimit(env, key) {
  if (!env.PUBLISH_LIMITER || typeof env.PUBLISH_LIMITER.limit !== 'function') return;
  let outcome;
  try {
    outcome = await env.PUBLISH_LIMITER.limit({ key });
  } catch {
    return; // fail open; the KV counter still applies
  }
  if (!outcome || !outcome.success) {
    throw new HttpError(429, 'rate_limited', 'Too many requests. Try again later.', { 'Retry-After': '60' });
  }
}

function rejectForeignOrigin(request, siteOrigin) {
  const origin = request.headers.get('origin');
  if (origin !== null && !isAllowedOrigin(origin, siteOrigin)) {
    throw new HttpError(403, 'origin_not_allowed', 'This origin may not use the share service.');
  }
}

// ---- Handlers ----

async function handlePublish(request, env, cfg) {
  rejectForeignOrigin(request, cfg.siteOrigin);
  requireJsonContentType(request);
  const ip = clientIp(request);
  await edgeLimit(env, ip);
  const commit = await hourlyLimit(env, 'rl', ip, PUBLISH_PER_HOUR);

  const body = await readJsonBody(request, MAX_BODY_BYTES);
  const { snapshot, png } = validatePublish(body);
  await commit();

  const store = getStore(env);
  let id = null;
  for (let attempt = 0; attempt < 5 && !id; attempt++) {
    const candidate = randomId(10);
    if (!(await store.getJSON(`c:${candidate}`))) id = candidate;
  }
  if (!id) throw new HttpError(503, 'id_unavailable', 'Could not allocate an id. Try again.');

  const token = randomToken();
  const record = {
    ...snapshot,
    id,
    created: new Date().toISOString(),
    token_hash: await sha256Hex(token),
  };

  await store.putBytes(`img:${id}`, png, 'image/png');
  try {
    await store.putJSON(`c:${id}`, record);
  } catch (err) {
    await store.delete(`img:${id}`).catch(() => {});
    throw err;
  }

  const url = `${cfg.shareOrigin}/c/${id}`;
  return jsonResponse(
    { ok: true, id, url, image_url: `${url}/card.png`, manage_token: token },
    201,
    { Location: url },
  );
}

async function handleUnpublish(request, env, cfg) {
  rejectForeignOrigin(request, cfg.siteOrigin);
  requireJsonContentType(request);
  const ip = clientIp(request);
  const commit = await hourlyLimit(env, 'rlu', ip, UNPUBLISH_PER_HOUR);
  await commit(); // every attempt counts

  const { id, token } = validateUnpublish(await readJsonBody(request, 4096));
  const store = getStore(env);
  const record = await store.getJSON(`c:${id}`);
  if (!record) return notFoundJson();
  if (record.unpublished) return errorResponse(410, 'unpublished', 'This collection was already unpublished.');

  const presented = await sha256Hex(token);
  if (!constantTimeEqual(presented, String(record.token_hash || ''))) {
    return errorResponse(403, 'forbidden', 'The manage token does not match.');
  }

  await store.delete(`img:${id}`);
  const at = new Date().toISOString();
  await store.putJSON(`c:${id}`, { unpublished: true, at });
  return jsonResponse({ ok: true, id, unpublished: true, at });
}

function publicSnapshot(record, shareOrigin) {
  const url = `${shareOrigin}/c/${record.id}`;
  const out = {
    schema: record.schema,
    id: record.id,
    name: record.name,
    description: record.description,
    games: record.games,
  };
  if (record.stats) out.stats = record.stats;
  out.created = record.created;
  out.url = url;
  out.image_url = `${url}/card.png`;
  return out;
}

async function handleSnapshotJson(env, cfg, id) {
  const record = await loadRecord(env, id);
  if (!record) return notFoundJson();
  if (record.unpublished) return errorResponse(410, 'unpublished', 'This collection was unpublished.');
  return jsonResponse(publicSnapshot(record, cfg.shareOrigin), 200, {
    'Cache-Control': PAGE_CACHE,
    'X-Robots-Tag': 'noindex',
  });
}

async function handlePage(env, cfg, id) {
  const record = await loadRecord(env, id);
  const headers = htmlHeaders(cfg.siteOrigin);
  if (!record || record.unpublished) {
    const status = record ? 410 : 404;
    return new Response(renderMessagePage(status, cfg), {
      status,
      headers: { ...headers, 'Cache-Control': PAGE_CACHE },
    });
  }
  return new Response(renderSharePage(record, cfg), {
    status: 200,
    headers: { ...headers, 'Cache-Control': PAGE_CACHE },
  });
}

async function handleCard(env, id) {
  const record = await loadRecord(env, id);
  if (!record) return textResponse('Not found.', 404);
  if (record.unpublished) return textResponse('This collection was unpublished.', 410);
  const bytes = await getStore(env).getBytes(`img:${id}`);
  if (!bytes) return textResponse('Not found.', 404);
  return new Response(bytes, {
    status: 200,
    headers: {
      'Content-Type': 'image/png',
      'Cache-Control': 'public, max-age=31536000, immutable',
      'X-Content-Type-Options': 'nosniff',
    },
  });
}

// ---- Router ----

async function route(request, env) {
  const cfg = config(env, request);
  const url = new URL(request.url);
  const path = url.pathname;
  const method = request.method.toUpperCase();
  const origin = request.headers.get('origin');
  let m;

  if (path === '/' || path === '') {
    if (method !== 'GET' && method !== 'HEAD') return methodNotAllowed('GET, HEAD');
    return new Response(null, { status: 302, headers: { Location: cfg.siteOrigin } });
  }

  // API routes (CORS-aware)
  const isPublish = path === '/api/publish';
  const isUnpublish = path === '/api/unpublish';
  const apiSnapshot = /^\/api\/c\/([^/]+)\/?$/.exec(path);
  if (isPublish || isUnpublish || apiSnapshot) {
    const cors = corsHeaders(origin, cfg.siteOrigin);
    if (method === 'OPTIONS') {
      return new Response(null, { status: 204, headers: preflightHeaders(origin, cfg.siteOrigin) });
    }
    let res;
    if (apiSnapshot) {
      if (method !== 'GET' && method !== 'HEAD') return methodNotAllowed('GET, HEAD, OPTIONS', cors);
      res = await handleSnapshotJson(env, cfg, apiSnapshot[1]);
    } else {
      if (method !== 'POST') return methodNotAllowed('POST, OPTIONS', cors);
      try {
        res = isPublish ? await handlePublish(request, env, cfg) : await handleUnpublish(request, env, cfg);
      } catch (err) {
        if (!(err instanceof HttpError)) throw err;
        // A foreign origin never receives CORS headers.
        const extra = err.code === 'origin_not_allowed' ? { Vary: 'Origin' } : cors;
        return errorResponse(err.status, err.code, err.message, { ...err.headers, ...extra });
      }
    }
    return withHeaders(res, cors);
  }

  if ((m = /^\/c\/([^/]+)\/card\.png$/.exec(path))) {
    if (method !== 'GET' && method !== 'HEAD') return methodNotAllowed('GET, HEAD');
    return handleCard(env, m[1]);
  }

  if ((m = /^\/c\/([^/]+)\/?$/.exec(path))) {
    if (method !== 'GET' && method !== 'HEAD') return methodNotAllowed('GET, HEAD');
    return handlePage(env, cfg, m[1]);
  }

  return notFoundJson();
}

export default {
  async fetch(request, env, ctx) {
    let res;
    try {
      res = await route(request, env, ctx);
    } catch (err) {
      if (err instanceof HttpError) {
        res = errorResponse(err.status, err.code, err.message, err.headers);
      } else {
        console.error('share-worker error', err && err.stack ? err.stack : err);
        res = errorResponse(500, 'internal_error', 'Internal error.');
      }
    }
    if (request.method.toUpperCase() === 'HEAD' && res.body) {
      res = new Response(null, { status: res.status, statusText: res.statusText, headers: res.headers });
    }
    return res;
  },
};
