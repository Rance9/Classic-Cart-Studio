// Storage adapter. Uses the R2 bucket SHARE_BUCKET when it is bound, otherwise
// the KV namespace SHARES. Callers only see getJSON/putJSON/getBytes/putBytes/delete.

export function getStore(env) {
  if (env && env.SHARE_BUCKET) return r2Store(env.SHARE_BUCKET);
  if (env && env.SHARES) return kvStore(env.SHARES);
  throw new Error('No storage binding: bind the KV namespace SHARES or the R2 bucket SHARE_BUCKET.');
}

function kvStore(kv) {
  return {
    kind: 'kv',
    async getJSON(key) {
      return kv.get(key, { type: 'json' });
    },
    async putJSON(key, value) {
      await kv.put(key, JSON.stringify(value));
    },
    async getBytes(key) {
      return kv.get(key, { type: 'arrayBuffer' });
    },
    async putBytes(key, bytes) {
      await kv.put(key, bytes);
    },
    async delete(key) {
      await kv.delete(key);
    },
  };
}

function r2Store(bucket) {
  return {
    kind: 'r2',
    async getJSON(key) {
      const obj = await bucket.get(key);
      return obj ? obj.json() : null;
    },
    async putJSON(key, value) {
      await bucket.put(key, JSON.stringify(value), {
        httpMetadata: { contentType: 'application/json' },
      });
    },
    async getBytes(key) {
      const obj = await bucket.get(key);
      return obj ? obj.arrayBuffer() : null;
    },
    async putBytes(key, bytes, contentType) {
      await bucket.put(key, bytes, {
        httpMetadata: { contentType: contentType || 'application/octet-stream' },
      });
    },
    async delete(key) {
      await bucket.delete(key);
    },
  };
}

// Hourly abuse counters. Always KV (SHARES) when bound, because KV supports
// expirationTtl. With only an R2 bucket bound, counters fall back to R2 objects
// keyed by hour (add an R2 lifecycle rule for the "rl" prefix to clean them up).
export function getCounterStore(env) {
  if (env && env.SHARES) {
    const kv = env.SHARES;
    return {
      async get(key) {
        const v = await kv.get(key, { type: 'text' });
        const n = parseInt(v || '0', 10);
        return Number.isFinite(n) && n > 0 ? n : 0;
      },
      async put(key, n, ttlSeconds) {
        await kv.put(key, String(n), { expirationTtl: ttlSeconds });
      },
    };
  }
  const store = getStore(env);
  return {
    async get(key) {
      const v = await store.getJSON(key);
      return v && Number.isFinite(v.n) ? v.n : 0;
    },
    async put(key, n) {
      await store.putJSON(key, { n });
    },
  };
}
