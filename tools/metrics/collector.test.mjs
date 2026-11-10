// Classic Cart Studio optional usage counts: the collector's boundary tests (node --test). Copyright (C) 2026 Rance9. SPDX-License-Identifier: GPL-3.0-or-later
import test from "node:test";
import assert from "node:assert/strict";
import collector, { resetCap } from "./collector.mjs";

const sample = () => ({ schema: 1, event: "play_started", app_version: "0.7.7", surface: "web" });
function setup() {
  resetCap();
  const points = [];
  return { points, env: { METRICS_ENABLED: "true", CCS_METRICS: { writeDataPoint: data => points.push(data) } } };
}
function req(body = sample(), options = {}) {
  return new Request("https://metrics.example/v1/events" + (options.query || ""), {
    method: options.method || "POST",
    headers: { "Content-Type": "application/json", ...(options.native ? {} : { Origin: "https://classiccartstudio.com" }), ...options.headers },
    body: options.method === "GET" ? undefined : typeof body === "string" ? body : JSON.stringify(body)
  });
}

test("one real accepted event writes the fixed non-content schema only", async () => {
  const { points, env } = setup();
  const r = await collector.fetch(req(sample(), { headers: { "CF-Connecting-IP": "192.0.2.1", "User-Agent": "private-user-agent", Referer: "https://elsewhere.example/private" } }), env);
  assert.equal(r.status, 202);
  assert.deepEqual(points, [{ indexes: ["web"], blobs: ["1", "play_started", "0.7.7", "web", "other", "other", "none", "none", "none"], doubles: [1] }]);
  assert.equal(r.headers.get("Access-Control-Allow-Credentials"), null);
  assert.equal(r.headers.get("Cache-Control"), "no-store");
});

for (const field of ["rom", "rom_hash", "game_title", "collection", "path", "save", "controller_id", "user_id", "url", "error_message", "__proto__"]) {
  test(`refuses sensitive or unknown field ${field} without storing or echoing it`, async () => {
    const { points, env } = setup();
    const body = JSON.stringify(sample()).replace(/}$/, `,"${field}":"PRIVATE-SENTINEL"}`);
    const r = await collector.fetch(req(body), env);
    assert.equal(r.status, 400); assert.equal(points.length, 0);
    assert.ok(!(await r.text()).includes("PRIVATE-SENTINEL"));
  });
}

for (const body of [null, [], {}, { ...sample(), schema: "1" }, { ...sample(), app_version: "C:/private.rom" }, { ...sample(), event: "a game title" }, { ...sample(), browser: "a device fingerprint" }, { ...sample(), reason: "other" }, { ...sample(), event: "operation_failed", operation: "restore", reason: "private error" }]) {
  test(`refuses invalid schema ${JSON.stringify(body)}`, async () => {
    const { points, env } = setup();
    assert.equal((await collector.fetch(req(body), env)).status, 400);
    assert.equal(points.length, 0);
  });
}

test("a native client without Origin can send a coarse failure count", async () => {
  const { points, env } = setup();
  const data = { ...sample(), surface: "windows", event: "operation_failed", operation: "restore", reason: "read_failed", elapsed: "1_to_5s" };
  assert.equal((await collector.fetch(req(data, { native: true }), env)).status, 202);
  assert.deepEqual(points[0].blobs.slice(-3), ["restore", "read_failed", "1_to_5s"]);
});

test("disabled collector or missing binding writes nothing", async () => {
  const { points, env } = setup();
  env.METRICS_ENABLED = "false";
  assert.equal((await collector.fetch(req(), env)).status, 503);
  env.METRICS_ENABLED = "true"; delete env.CCS_METRICS;
  assert.equal((await collector.fetch(req(), env)).status, 503);
  assert.equal(points.length, 0);
});

test("CORS accepts one canonical origin and refuses null/unrelated origins", async () => {
  const { points, env } = setup();
  for (const origin of ["null", "https://classiccartstudio.com.attacker.example", "http://classiccartstudio.com"]) {
    const r = await collector.fetch(req(sample(), { headers: { Origin: origin } }), env);
    assert.equal(r.status, 403); assert.equal(r.headers.get("Access-Control-Allow-Origin"), null);
  }
  const r = await collector.fetch(new Request("https://metrics.example/v1/events", { method: "OPTIONS", headers: { Origin: "https://classiccartstudio.com", "Access-Control-Request-Method": "POST", "Access-Control-Request-Headers": "content-type" } }), env);
  assert.equal(r.status, 204); assert.equal(points.length, 0);
});

test("query strings, arbitrary methods and content types do not write", async () => {
  const { points, env } = setup();
  assert.equal((await collector.fetch(req(sample(), { query: "?private=sentinel" }), env)).status, 404);
  assert.equal((await collector.fetch(req(sample(), { method: "GET" }), env)).status, 405);
  assert.equal((await collector.fetch(req(sample(), { headers: { "Content-Type": "text/plain" } }), env)).status, 415);
  assert.equal((await collector.fetch(req("not json"), env)).status, 400);
  assert.equal(points.length, 0);
});

test("advertised and streamed bodies over the limit do not write", async () => {
  const { points, env } = setup();
  assert.equal((await collector.fetch(req(sample(), { headers: { "Content-Length": "1025" } }), env)).status, 413);
  const stream = new ReadableStream({ start(controller) { controller.enqueue(new Uint8Array(700)); controller.enqueue(new Uint8Array(700)); controller.close(); } });
  const r = await collector.fetch(new Request("https://metrics.example/v1/events", { method: "POST", body: stream, duplex: "half", headers: { "Content-Type": "application/json" } }), env);
  assert.equal(r.status, 413); assert.equal(points.length, 0);
});

test("an unfinished upload has a deadline", async () => {
  const { points, env } = setup();
  let cancelled = false;
  const stream = new ReadableStream({ cancel() { cancelled = true; } });
  const r = await collector.fetch(new Request("https://metrics.example/v1/events", { method: "POST", body: stream, duplex: "half", headers: { "Content-Type": "application/json" } }), env);
  assert.equal(r.status, 408); assert.equal(points.length, 0); assert.equal(cancelled, true);
});

test("binding failure returns a fixed message without exception details", async () => {
  const env = { METRICS_ENABLED: "true", CCS_METRICS: { writeDataPoint() { throw new Error("PRIVATE-SENTINEL"); } } };
  const r = await collector.fetch(req(), env);
  assert.equal(r.status, 503); assert.ok(!(await r.text()).includes("PRIVATE-SENTINEL"));
});

test("health never writes an event or reveals credentials", async () => {
  const { points, env } = setup();
  const r = await collector.fetch(new Request("https://metrics.example/healthz"), env);
  assert.deepEqual(await r.json(), { service: "ccs-metrics", collecting: true });
  assert.equal(points.length, 0);
});

test("configured origins: only https origins are taken, the canonical website by default", async () => {
  const { points, env } = setup();
  env.ALLOWED_ORIGINS = "https://classiccartstudio.com, http://insecure.example, javascript:alert(1)";
  assert.equal((await collector.fetch(req(sample(), { headers: { Origin: "http://insecure.example" } }), env)).status, 403);
  assert.equal((await collector.fetch(req(), env)).status, 202);
  env.ALLOWED_ORIGINS = "not a list";
  assert.equal((await collector.fetch(req(), env)).status, 202, "an unusable list falls back to the canonical website");
  assert.equal(points.length, 2);
});

test("a flood beyond the per-instance cap is refused with 429 and writes nothing more", async () => {
  const { points, env } = setup();
  let refused = 0;
  for (let i = 0; i < 130; i++) { const r = await collector.fetch(req(sample(), { native: true }), env); if (r.status === 429) refused++; }
  assert.equal(points.length, 120); assert.equal(refused, 10);
});

test("an accepted preflight is cached by the browser for a day", async () => {
  const { env } = setup();
  const r = await collector.fetch(new Request("https://metrics.example/v1/events", { method: "OPTIONS", headers: { Origin: "https://classiccartstudio.com", "Access-Control-Request-Method": "POST", "Access-Control-Request-Headers": "content-type" } }), env);
  assert.equal(r.status, 204); assert.equal(r.headers.get("Access-Control-Max-Age"), "86400");
});

test("every refusal answers a fixed message (no echo of the request)", async () => {
  const { env } = setup();
  for (const body of ['{"schema":1,"event":"PRIVATE-SENTINEL"}', '{"schema":1,"event":"play_started","app_version":"0.8.0","surface":"web","game":"PRIVATE-SENTINEL"}', "PRIVATE-SENTINEL"]) {
    const r = await collector.fetch(req(body), env); const t = await r.text();
    assert.ok(r.status >= 400 && !t.includes("PRIVATE-SENTINEL"));
  }
});
