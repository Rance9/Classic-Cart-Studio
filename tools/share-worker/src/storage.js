// Storage adapter. The R2 bucket SHARE_BUCKET is the primary store; the KV
// namespace SHARES is only a fallback when no bucket is bound.
//   R2 keys: c/<id>.json, c/<id>/card.png, c/<id>/t/<n>.jpg
//   KV keys: c:<id>,      img:<id>,        t:<id>:<n>
// Callers use the record/card/thumb methods; getJSON/putJSON exist for counters.

export function getStore(env) {
  if (env && env.SHARE_BUCKET) return r2Store(env.SHARE_BUCKET);
  if (env && env.SHARES) return kvStore(env.SHARES);
  throw new Error('No storage binding: bind the R2 bucket SHARE_BUCKET (or, as a fallback, the KV namespace SHARES).');
}

function withShareMethods(base, keys) {
  return {
    ...base,
    keys,
    getRecord(id) {
      return base.getJSON(keys.record(id));
    },
    putRecord(id, record) {
      return base.putJSON(keys.record(id), record);
    },
    getCard(id) {
      return base.getBytes(keys.card(id));
    },
    putCard(id, bytes) {
      return base.putBytes(keys.card(id), bytes, 'image/png');
    },
    getThumb(id, n) {
      return base.getBytes(keys.thumb(id, n));
    },
    putThumb(id, n, bytes) {
      return base.putBytes(keys.thumb(id, n), bytes, 'image/jpeg');
    },
    // Deletes the card and thumbs 0..thumbCount-1. The record is left alone.
    async deleteAssets(id, thumbCount) {
      const list = [keys.card(id)];
      for (let n = 0; n < thumbCount; n++) list.push(keys.thumb(id, n));
      await base.deleteMany(list);
    },
  };
}

const CHUNK = 50;

async function inChunks(items, fn) {
  for (let i = 0; i < items.length; i += CHUNK) {
    await Promise.all(items.slice(i, i + CHUNK).map(fn));
  }
}

export { inChunks };

function kvStore(kv) {
  return withShareMethods(
    {
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
      async deleteMany(list) {
        await inChunks(list, (k) => kv.delete(k));
      },
    },
    {
      record: (id) => `c:${id}`,
      card: (id) => `img:${id}`,
      thumb: (id, n) => `t:${id}:${n}`,
    },
  );
}

function r2Store(bucket) {
  return withShareMethods(
    {
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
      async deleteMany(list) {
        // R2 deletes up to 1000 keys per call.
        for (let i = 0; i < list.length; i += 1000) await bucket.delete(list.slice(i, i + 1000));
      },
    },
    {
      record: (id) => `c/${id}.json`,
      card: (id) => `c/${id}/card.png`,
      thumb: (id, n) => `c/${id}/t/${n}.jpg`,
    },
  );
}

// Hourly abuse counters. KV (SHARES) when bound, because KV supports
// expirationTtl. With only the R2 bucket bound, counters fall back to R2
// objects keyed by hour (add an R2 lifecycle rule for the "rl" prefix to clean them up).
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
