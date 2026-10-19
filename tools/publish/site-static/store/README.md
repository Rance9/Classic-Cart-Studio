# Classic Cart Studio Store: how to maintain it

The store is a single page (`index.html`) that reads one data file, `store.json`. It is a curated list of links to third-party
sellers: no cart, checkout, prices, ratings or inventory. Nothing appears on the page until you publish real listings.
This README is for you only; the page never links to it.

## Add a product

1. Open `store.json` and add a block to the `products` list (separate blocks with commas):

   ```json
   {
     "id": "console-ebay-1",
     "name": "",
     "category": "console",
     "seller": "eBay",
     "platform": "ebay",
     "url": "https://...",
     "description": "",
     "compatibility": "",
     "image": "",
     "affiliate": false,
     "commission_note": "",
     "enabled": true,
     "featured": true,
     "recommendation": ""
   }
   ```

2. Fill in the fields (each one is described under `_fields` in `store.json`):
   - `category` is one of `console`, `controllers`, `power`, `storage`, `setup`.
   - `platform` is `ebay`, `amazon` or `other`. The button reads "View on eBay", "View on Amazon" or "View at <seller>".
   - `url` must start with `https://`; any other link is skipped.
   - `image` is optional. Put the picture in `store/img/` and write `img/<file name>`; any other path is skipped.
     Use only pictures you took yourself or are allowed to use.
   - `affiliate` is `true` only for Amazon Associates links.
   - `featured: true` (with `category: "console"`) puts the console listing in the featured section at the top.
   - `recommendation` is an optional short label such as "Personally used".
3. Set `enabled` to `true` and update `updated` (for example `2026-10-19`).

`store.sample.json` holds fake test products only. Do not edit it for real listings; the website never loads it.

## Publish

Set `"published": true` in `store.json` once at least one real product is enabled, then publish the website as usual.
The main page reads `store/store.json` and shows the Store link in its navigation only when `published` is `true` and
there are enabled products. Set `published` back to `false` to close the store again.

The page keeps `<meta name="robots" content="noindex">` so search engines do not list it; remove that line from
`index.html` if you later want the store to appear in search results.

## A listing sold out (eBay)

Set that product's `enabled` to `false` (or delete its block). If it was the only enabled product, the page shows
"The Classic Cart Studio Store is not open yet." until another product is enabled.

## Disclosure rules

Amazon Associates links must be clearly disclosed. Whenever any enabled product has `affiliate: true`, the page shows
"As an Amazon Associate I earn from qualifying purchases." near the top and a "Paid link" label next to each affiliate
button, and those links carry `rel="sponsored"`. Mark every Amazon Associates link `affiliate: true`; never put Amazon
Associates links in the Windows app (the app build leaves the whole `store/` folder out, and the app opens the store in the
browser instead). Do not show Amazon prices or star ratings on the page. For eBay or other links that pay you nothing, you may add a
`commission_note` such as "I receive no commission on eBay purchases." See the
[Amazon Associates Program Policies](https://affiliate-program.amazon.com/help/operating/policies) and the
[FTC Endorsement Guides: What People Are Asking](https://www.ftc.gov/business-guidance/resources/ftcs-endorsement-guides-what-people-are-asking).
