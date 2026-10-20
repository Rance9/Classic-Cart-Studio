// Classic Cart Studio share service (Cloudflare Worker, ES module).
// Routes: POST /api/publish, POST /api/unpublish, GET /api/c/<id>,
//         GET /c/<id>, GET /c/<id>/card.png, GET /c/<id>/t/<n>.jpg, GET / (redirect).
import { getStore, getCounterStore, inChunks } from './storage.js';
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

export const MAX_BODY_BYTES = 24_000_000;   // the service's resource limit for one publish request (game list, loadouts, thumbnails, card image)
const BODY_TOO_LARGE = 'The shared copy is larger than the sharing service accepts in one request (limit 24 MB).';
export const PUBLISH_PER_HOUR = 20;
export const UNPUBLISH_PER_HOUR = 60;
const ID_PATTERN = /^[0-9A-Za-z]{10}$/;
const PAGE_CACHE = 'public, max-age=60';
const IMAGE_CACHE = 'public, max-age=31536000, immutable';

function config(env, request) {
  const siteOrigin = normalizeOrigin(env.SITE_ORIGIN, 'https://classiccartstudio.com');
  const shareOrigin = normalizeOrigin(env.SHARE_ORIGIN, new URL(request.url).origin);
  return { siteOrigin, shareOrigin };
}

function methodNotAllowed(allow, extra = {}) {
  return errorResponse(405, 'method_not_allowed', 'Method not allowed.', { Allow: allow, ...extra });
}

function notFoundJson(extra = {}) {
  return errorResponse(404, 'not_found', 'This shared collection was not found. It may have been removed already.', extra);
}

async function loadRecord(env, id) {
  if (!ID_PATTERN.test(id)) return null;
  return getStore(env).getRecord(id);
}

// ---- Abuse limits ----

async function hourlyLimit(env, prefix, ip, max) {
  const counters = getCounterStore(env);
  const hour = Math.floor(Date.now() / 3_600_000);
  const key = `${prefix}:${ip}:${hour}`;
  const n = await counters.get(key);
  if (n >= max) {
    throw new HttpError(429, 'rate_limited', 'Too many requests from this connection in the last hour. Try again later.', { 'Retry-After': '3600' });
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
    throw new HttpError(429, 'rate_limited', 'Too many requests from this connection. Wait a minute and try again.', { 'Retry-After': '60' });
  }
}

function rejectForeignOrigin(request, siteOrigin) {
  const origin = request.headers.get('origin');
  if (origin !== null && !isAllowedOrigin(origin, siteOrigin)) {
    throw new HttpError(403, 'origin_not_allowed', 'This website cannot use the Classic Cart Studio sharing service.');
  }
}

// ---- Handlers ----

async function handlePublish(request, env, cfg) {
  rejectForeignOrigin(request, cfg.siteOrigin);
  requireJsonContentType(request);
  const ip = clientIp(request);
  await edgeLimit(env, ip);
  const commit = await hourlyLimit(env, 'rl', ip, PUBLISH_PER_HOUR);

  const body = await readJsonBody(request, MAX_BODY_BYTES, BODY_TOO_LARGE);
  const { snapshot, png, thumbs } = validatePublish(body);
  await commit();

  const store = getStore(env);
  let id = null;
  for (let attempt = 0; attempt < 5 && !id; attempt++) {
    const candidate = randomId(10);
    if (!(await store.getRecord(candidate))) id = candidate;
  }
  if (!id) throw new HttpError(503, 'id_unavailable', 'Could not create a link this time. Try again.');

  const token = randomToken();
  const record = {
    ...snapshot,
    id,
    created: new Date().toISOString(),
    thumb_count: thumbs.length,
    token_hash: await sha256Hex(token),
  };

  try {
    await store.putCard(id, png);
    await inChunks(thumbs.map((bytes, n) => [n, bytes]), ([n, bytes]) => store.putThumb(id, n, bytes));
    await store.putRecord(id, record);
  } catch (err) {
    await store.deleteAssets(id, thumbs.length).catch(() => {});
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
  const record = await store.getRecord(id);
  if (!record) return notFoundJson();
  if (record.unpublished) return errorResponse(410, 'unpublished', 'This collection was already unpublished.');

  const presented = await sha256Hex(token);
  if (!constantTimeEqual(presented, String(record.token_hash || ''))) {
    return errorResponse(403, 'forbidden', 'The key saved for this link does not match, so the link was not unpublished.');
  }

  const thumbCount = Number.isInteger(record.thumb_count) ? record.thumb_count : 0;
  await store.deleteAssets(id, thumbCount);
  const at = new Date().toISOString();
  await store.putRecord(id, { unpublished: true, at });
  return jsonResponse({ ok: true, id, unpublished: true, at });
}

export function thumbUrl(pageUrl, n) {
  return `${pageUrl}/t/${n}.jpg`;
}

function publicGame(g, pageUrl) {
  return Number.isInteger(g.thumb) && g.thumb >= 0 ? { ...g, thumb_url: thumbUrl(pageUrl, g.thumb) } : g;
}

function publicSnapshot(record, shareOrigin) {
  const url = `${shareOrigin}/c/${record.id}`;
  const out = {
    schema: record.schema,
    id: record.id,
    name: record.name,
    description: record.description,
    games: record.games.map((g) => publicGame(g, url)),
  };
  if (record.stats) out.stats = record.stats;
  if (record.home) out.home = record.home;
  if (Array.isArray(record.loadouts) && record.loadouts.length) out.loadouts = record.loadouts;
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
  const headers = htmlHeaders(cfg.siteOrigin, cfg.shareOrigin);
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
  const bytes = await getStore(env).getCard(id);
  return imageResponse(bytes, 'image/png');
}

async function handleThumb(env, id, nText) {
  const record = await loadRecord(env, id);
  if (!record) return textResponse('Not found.', 404);
  if (record.unpublished) return textResponse('This collection was unpublished.', 410);
  const n = Number(nText);
  if (!/^(0|[1-9][0-9]{0,3})$/.test(nText) || !Number.isInteger(record.thumb_count) || n >= record.thumb_count) {
    return textResponse('Not found.', 404);
  }
  return imageResponse(await getStore(env).getThumb(id, n), 'image/jpeg');
}

function imageResponse(bytes, contentType) {
  if (!bytes) return textResponse('Not found.', 404);
  return new Response(bytes, {
    status: 200,
    headers: {
      'Content-Type': contentType,
      'Cache-Control': IMAGE_CACHE,
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

  if ((m = /^\/c\/([^/]+)\/t\/([0-9]+)\.jpg$/.exec(path))) {
    if (method !== 'GET' && method !== 'HEAD') return methodNotAllowed('GET, HEAD');
    return handleThumb(env, m[1], m[2]);
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
        res = errorResponse(500, 'internal_error', 'The sharing service had a problem. Try again later.');
      }
    }
    if (request.method.toUpperCase() === 'HEAD' && res.body) {
      res = new Response(null, { status: res.status, statusText: res.statusText, headers: res.headers });
    }
    return res;
  },
};
