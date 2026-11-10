// Classic Cart Studio optional usage counts: the collector (a Cloudflare Worker writing to Workers Analytics Engine).
// Copyright (C) 2026 Rance9. SPDX-License-Identifier: GPL-3.0-or-later
//
// It accepts one small JSON event whose every field is on a fixed list (event, release version, surface, coarse browser and system
// family, and for a failure a fixed operation and reason, optionally a timing bucket) and writes one data point in a fixed order. Nothing
// else: no request body, IP address, user agent, URL, game, collection, file or identifier is stored, and a refused request is never
// logged or echoed. The request is bounded (1 KB, 2 s); a small per-instance cap turns floods into 429 (a weak control: each instance
// counts alone; the Workers Free plan's own daily request limit ends a larger flood with error 1027, never a charge). Origin checks keep
// other websites' pages out; they authenticate no one, and counts are approximate. Configuration (vars): METRICS_ENABLED "true" to
// collect, ALLOWED_ORIGINS (comma-separated, default the canonical website), binding CCS_METRICS (the dataset: test or production).
const MAX_BYTES = 1024;
const CANONICAL = "https://classiccartstudio.com";
const EVENTS = new Set(["studio_opened", "home_opened", "play_started", "backup_completed", "restore_completed", "operation_failed", "download_clicked"]);
const SURFACES = new Set(["web", "installed_web", "windows"]);
const BROWSERS = new Set(["chrome", "edge", "firefox", "safari", "other"]);
const SYSTEMS = new Set(["windows", "macos", "ios", "android", "linux", "other"]);
const OPERATIONS = new Set(["home", "play", "backup", "restore", "storage"]);
const REASONS = new Set(["not_ready", "missing_rom", "storage_unavailable", "read_failed", "write_failed", "invalid_data", "unsupported", "other"]);
const ELAPSED = new Set(["under_1s", "1_to_5s", "5_to_15s", "15_to_60s", "over_60s"]);
const FIELDS = new Set(["schema", "event", "app_version", "surface", "browser", "os", "operation", "reason", "elapsed"]);
const CAP = { events: 120, windowMs: 10000 };   // accepted events per instance per 10 s
const recent = { start: 0, count: 0 };

export function validEvent(data) {
  if (!data || Array.isArray(data) || typeof data !== "object") return false;
  if (Object.keys(data).some(key => !FIELDS.has(key))) return false;
  if (data.schema !== 1 || !EVENTS.has(data.event) || !SURFACES.has(data.surface)) return false;
  if (typeof data.app_version !== "string" || !/^\d{1,3}\.\d{1,3}\.\d{1,3}(?:-[a-z0-9]{1,16})?$/.test(data.app_version)) return false;
  if ("browser" in data && !BROWSERS.has(data.browser)) return false;
  if ("os" in data && !SYSTEMS.has(data.os)) return false;
  if ("elapsed" in data && !ELAPSED.has(data.elapsed)) return false;
  if (data.event === "operation_failed") return OPERATIONS.has(data.operation) && REASONS.has(data.reason);
  return !("operation" in data) && !("reason" in data);
}

function origins(env) {
  const list = String(env.ALLOWED_ORIGINS || CANONICAL).split(",").map(x => x.trim()).filter(x => /^https:\/\/[a-z0-9.-]+$/.test(x));
  return new Set(list.length ? list : [CANONICAL]);
}

function response(status, origin, allowed, value) {
  const headers = { "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff", "Vary": "Origin" };
  if (origin && allowed.has(origin)) headers["Access-Control-Allow-Origin"] = origin;
  if (value !== undefined) headers["Content-Type"] = "application/json; charset=utf-8";
  return new Response(value === undefined ? null : JSON.stringify(value), { status, headers });
}

async function smallBody(request) {
  const declared = request.headers.get("Content-Length");
  if (declared !== null && (!/^\d+$/.test(declared) || Number(declared) > MAX_BYTES)) throw 413;
  if (!request.body) throw 400;
  const reader = request.body.getReader();
  const chunks = [];
  let size = 0;
  let timer;
  const deadline = new Promise((_, reject) => { timer = setTimeout(() => reject(408), 2000); });
  try {
    for (;;) {
      const item = await Promise.race([reader.read(), deadline]);
      if (item.done) break;
      size += item.value.byteLength;
      if (size > MAX_BYTES) throw 413;
      chunks.push(item.value);
    }
    const body = new Uint8Array(size);
    let offset = 0;
    for (const chunk of chunks) { body.set(chunk, offset); offset += chunk.byteLength; }
    try { return JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(body)); }
    catch { throw 400; }
  } finally {
    clearTimeout(timer);
    void reader.cancel().catch(() => {});
    reader.releaseLock();
  }
}

function underCap(now) {
  if (now - recent.start >= CAP.windowMs) { recent.start = now; recent.count = 0; }
  if (recent.count >= CAP.events) return false;
  recent.count++; return true;
}

export function resetCap() { recent.start = 0; recent.count = 0; }   // tests only

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const allowed = origins(env);
    const origin = request.headers.get("Origin");
    if (origin && !allowed.has(origin)) return response(403, null, allowed, { error: "origin_refused" });
    if (url.search) return response(404, origin, allowed, { error: "not_found" });
    if (url.pathname === "/healthz" && request.method === "GET") {
      return response(200, origin, allowed, { service: "ccs-metrics", collecting: env.METRICS_ENABLED === "true" && typeof env.CCS_METRICS?.writeDataPoint === "function" });
    }
    if (url.pathname !== "/v1/events") return response(404, origin, allowed, { error: "not_found" });
    if (request.method === "OPTIONS") {
      if (!origin || request.headers.get("Access-Control-Request-Method") !== "POST") return response(403, origin, allowed, { error: "preflight_refused" });
      const requested = (request.headers.get("Access-Control-Request-Headers") || "").split(",").map(x => x.trim().toLowerCase()).filter(Boolean);
      if (requested.some(x => x !== "content-type")) return response(403, origin, allowed, { error: "preflight_refused" });
      const result = response(204, origin, allowed);
      result.headers.set("Access-Control-Allow-Methods", "POST");
      result.headers.set("Access-Control-Allow-Headers", "Content-Type");
      result.headers.set("Access-Control-Max-Age", "86400");
      return result;
    }
    if (request.method !== "POST") return response(405, origin, allowed, { error: "method_refused" });
    if (env.METRICS_ENABLED !== "true" || typeof env.CCS_METRICS?.writeDataPoint !== "function") return response(503, origin, allowed, { error: "disabled" });
    if (!/^application\/json(?:\s*;\s*charset=utf-8)?$/i.test(request.headers.get("Content-Type") || "")) return response(415, origin, allowed, { error: "json_required" });
    let data;
    try { data = await smallBody(request); }
    catch (error) { return response([400, 408, 413].includes(error) ? error : 400, origin, allowed, { error: "invalid_request" }); }
    if (!validEvent(data)) return response(400, origin, allowed, { error: "invalid_event" });
    if (!underCap(Date.now())) return response(429, origin, allowed, { error: "busy" });
    try {
      // No request/IP/UA/URL data, content, free-form labels or user identifier.
      env.CCS_METRICS.writeDataPoint({
        indexes: [data.surface],
        blobs: ["1", data.event, data.app_version, data.surface, data.browser || "other", data.os || "other", data.operation || "none", data.reason || "none", data.elapsed || "none"],
        doubles: [1]
      });
    } catch {
      return response(503, origin, allowed, { error: "unavailable" });
    }
    return response(202, origin, allowed, { accepted: true });
  }
};
