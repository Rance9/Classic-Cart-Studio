# Deploying the Classic Cart Studio share service

The Worker serves `https://share.classiccartstudio.com`: share pages (`/c/<id>`), card images, and the publish/unpublish API used by the website and the Windows app. It stores data in a Cloudflare KV namespace. Nothing in this folder is a secret.

## Prerequisites

- The Cloudflare account that manages the `classiccartstudio.com` zone.
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
2. Create the KV namespace:
   ```
   npx wrangler@4 kv namespace create SHARES
   ```
   Copy `wrangler.toml.example` to `wrangler.toml` and replace `REPLACE_WITH_KV_NAMESPACE_ID` with the `id` printed by the command.
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

- KV is eventually consistent: a new share can take up to about 60 seconds to appear in other regions, and an unpublished share can stay visible for about the same time.
- Card images are served with a one-year cache. Unpublishing deletes the stored image, but copies already cached by browsers or social networks are outside our control.
- To view logs: `npx wrangler@4 tail`.
