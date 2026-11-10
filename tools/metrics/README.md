# Optional usage counts: the collector

A small Cloudflare Worker, `collector.mjs`, that writes the optional usage counts of the Classic Cart Studio website, its installed web app and Classic Cart Studio for Windows into Workers Analytics Engine. People choose it themselves: in the Windows setup wizard or in **Studio menu › Usage Reporting**. Nothing is sent until a person has chosen On; with no valid stored choice, Windows and the browser send nothing. Licensed GPL-3.0-or-later.

## What it accepts

One JSON object per request, at most 1 KB, read within 2 s. Every field is on a fixed list:

| Field | Values |
|---|---|
| `schema` | `1` |
| `event` | `studio_opened`, `home_opened`, `play_started`, `backup_completed`, `restore_completed`, `operation_failed`, `download_clicked` |
| `app_version` | a release version such as `0.8.0` |
| `surface` | `web`, `installed_web`, `windows` |
| `browser`, `os` | `chrome`, `edge`, `firefox`, `safari`, `other` / `windows`, `macos`, `ios`, `android`, `linux`, `other` |
| `operation`, `reason` (failures only) | `home`, `play`, `backup`, `restore`, `storage` / `not_ready`, `missing_rom`, `storage_unavailable`, `read_failed`, `write_failed`, `invalid_data`, `unsupported`, `other` |
| `elapsed` (optional) | `under_1s`, `1_to_5s`, `5_to_15s`, `15_to_60s`, `over_60s` |

Anything else is refused with a fixed message:
- an unknown field or value;
- a game, collection, file, save or identifier;
- a query string, another method or content type;
- a larger or slower body.

A refused request is not stored or logged, and it is never echoed back.

## What it stores

One data point per accepted event, in a fixed order: the fields above, then `double1 = 1`, with the surface as the index.

It stores no request body, IP address, user agent, URL, game, collection, file, save, controller or identifier. Cloudflare still handles network data to deliver the request; see the Privacy notice.

## Origins, abuse and cost

- **Browsers:** only `https://classiccartstudio.com` (configurable in `ALLOWED_ORIGINS`) may send from a web page. The Windows app sends through its own backend, without an origin.
- **Origin checks prove nothing:** they are not authentication, and anyone can send a valid-looking count, so counts are approximate.
- **Per-instance cap:** each Worker instance accepts at most 120 events per 10 s and answers 429 beyond that. It is weak, since every instance counts alone.
- **Free plan limits:**
  - Workers Free: 100,000 requests a day. Beyond that the Worker answers error 1027; there is no charge.
  - Analytics Engine: 100,000 data points written and 10,000 read queries a day included. Cloudflare currently does not bill Analytics Engine, and has said billing will start later.
  - One free WAF rate-limiting rule (by IP, 10 s) is available on the zone. It is not configured.

## Test and production

- **`wrangler.test.jsonc`:** `classic-cart-studio-metrics-test` → dataset `ccs_usage_test`, on workers.dev, for verification only.
- **`wrangler.prod.jsonc`:** `classic-cart-studio-metrics` → dataset `ccs_usage`, on `metrics.classiccartstudio.com`, promoted with a reviewed release.

The test traffic never enters the production dataset. Datasets appear on the first write after the binding exists, and Analytics Engine keeps data for three months.

## Reports

**Saved queries:** `queries.sql`, for the SQL API, which needs Account › Account Analytics › Read. They use sampling-aware sums (`SUM(_sample_interval * double1)`).

**Other sources:**
- **Website visits, referrers, devices and performance:** the existing Cloudflare Web Analytics site in the dashboard.
- **Installer downloads:** GitHub's release-asset counters (`gh api repos/Rance9/Classic-Cart-Studio/releases`).

None of these counts people, installations or retention.

## Tests

`node --test collector.test.mjs`
