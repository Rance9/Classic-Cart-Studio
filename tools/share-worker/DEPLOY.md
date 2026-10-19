# Deploying the Classic Cart Studio share service

The Worker serves `https://share.classiccartstudio.com`: share pages (`/c/<id>`), card images, cover thumbnails (`/c/<id>/t/<n>.jpg`), and the publish/unpublish API (`ccs-share/2`) used by the website and the Windows app. It stores shares in a Cloudflare R2 bucket. A shared collection holds up to 2000 games in an 8 MB request; larger requests are rejected with a clear message, never truncated. Nothing in this folder is a secret.

## Prerequisites

- The Cloudflare account that manages the `classiccartstudio.com` zone.
- R2 enabled on that account. The owner does this once in the Cloudflare dashboard (R2 Object Storage > Get started; the free tier is enough, but Cloudflare asks for a payment method).
- Node.js 18 or later. Wrangler runs through `npx wrangler@4`, which downloads Wrangler from npm the first time.
- Run every command from `tools\share-worker\`.

## Test locally first

```
node test/worker.test.mjs
```

Expect `RESULT: PASS (...)` on the last line.

## Steps

1. Sign in (opens a browser):
   ```
   npx wrangler@4 login
   ```
2. Create the R2 bucket:
   ```
   npx wrangler@4 r2 bucket create classic-cart-studio-shares
   ```
   Copy `wrangler.toml.example` to `wrangler.toml`. The `[[r2_buckets]]` block already names this bucket.
   Optional: to keep the hourly abuse counters in KV (they expire on their own), run `npx wrangler@4 kv namespace create SHARES` and uncomment the `[[kv_namespaces]]` block with the printed `id`. Without KV the counters are small R2 objects under the `rl` prefix; add an R2 lifecycle rule (dashboard > R2 > bucket > Settings > Object lifecycle rules) that deletes the `rl` prefix after 1 day.
3. Optional burst protection: uncomment the `[[ratelimits]]` block in `wrangler.toml`. The built-in limit of 20 publishes per IP per hour (60 unpublish attempts) applies either way.
4. Deploy:
   ```
   npx wrangler@4 deploy
   ```
5. Attach the domain: Cloudflare dashboard > Workers & Pages > `classic-cart-studio-share` > Settings > Domains & Routes > Add > Custom Domain > `share.classiccartstudio.com`. (Or uncomment the `routes` block in `wrangler.toml` and deploy again.) This adds a record for the `share` subdomain only. The apex and `www` records that point at GitHub Pages do not change.

## Verify

```
curl -i https://share.classiccartstudio.com/c/ZZZZZZZZZZ
```

Expect `HTTP/2 404` and a styled "Collection not found" HTML page.

```
curl -i https://share.classiccartstudio.com/
```

Expect a `302` redirect to `https://classiccartstudio.com`.

Then publish a collection from the website, open the returned link, and paste it into a chat or social post to confirm the preview card appears. Unpublish it from the site and confirm the link now shows "This collection was unpublished".

## Notes

- R2 is strongly consistent: a new share is visible right away. Share pages and JSON are cached for 60 seconds, so an unpublished share can stay visible for about that long.
- Card images and thumbnails are served with a one-year cache. Unpublishing deletes the stored card and thumbnails (the link then answers 410), but copies already cached by browsers or social networks are outside our control.
- To view logs: `npx wrangler@4 tail`.
