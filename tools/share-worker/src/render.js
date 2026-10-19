// Server-rendered HTML for share pages. No scripts, no third-party requests.
import { escapeHtml as e } from './util.js';

// img-src: 'self' (cards, thumbs), the share origin when it is addressed by
// another host name, and SITE_ORIGIN (catalog cover art).
export function htmlHeaders(siteOrigin, shareOrigin) {
  const extra = shareOrigin && shareOrigin !== siteOrigin ? ` ${shareOrigin}` : '';
  return {
    'Content-Type': 'text/html; charset=utf-8',
    'Content-Security-Policy':
      `default-src 'none'; img-src 'self'${extra} ${siteOrigin} data:; style-src 'unsafe-inline'; ` +
      `base-uri 'none'; form-action 'none'; frame-ancestors 'none'`,
    'X-Content-Type-Options': 'nosniff',
    'Referrer-Policy': 'strict-origin-when-cross-origin',
    'X-Robots-Tag': 'noindex',
  };
}

const CSS = `
:root{color-scheme:dark;--bg:#15161a;--panel:#1d1f25;--panel2:#25272f;--line:#30333c;--text:#fff;--muted:#a9adb8;--red:#e4000f;--red2:#ff2a36}
*{box-sizing:border-box}
html,body{margin:0;background:var(--bg);color:var(--text)}
body{font:16px/1.5 system-ui,-apple-system,"Segoe UI",Roboto,"Helvetica Neue",Arial,sans-serif;-webkit-font-smoothing:antialiased}
a{color:inherit}
.wrap{max-width:1160px;margin:0 auto;padding:0 16px}
.top{border-bottom:4px solid var(--red);background:#0f1013}
.top .wrap{display:flex;align-items:center;height:56px}
.brand{display:inline-flex;align-items:center;gap:10px;text-decoration:none;font-weight:800;letter-spacing:.02em}
.brand svg{flex:none}
.hero{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1.15fr);gap:32px;align-items:center;padding:40px 0 32px}
.eyebrow{margin:0 0 8px;color:var(--red2);font-weight:700;font-size:13px;letter-spacing:.14em;text-transform:uppercase}
h1{margin:0 0 12px;font-size:clamp(28px,4.4vw,44px);line-height:1.1;overflow-wrap:anywhere}
.desc{margin:0 0 18px;color:#d9dbe1;font-size:17px;overflow-wrap:anywhere}
.stats{display:flex;flex-wrap:wrap;gap:8px;margin:0 0 22px;padding:0;list-style:none}
.stats li{background:var(--panel2);border:1px solid var(--line);border-radius:6px;padding:4px 12px;color:var(--muted);font-size:14px}
.stats b{color:var(--text);font-size:16px;margin-right:4px}
.actions{display:flex;flex-wrap:wrap;gap:12px;align-items:center}
.btn{display:inline-flex;align-items:center;justify-content:center;min-height:46px;padding:0 20px;border-radius:8px;font-weight:700;text-decoration:none}
.btn.primary{background:var(--red);color:#fff;box-shadow:0 4px 0 #8a0009}
.btn.primary:hover,.btn.primary:focus-visible{background:var(--red2)}
.btn.primary:active{transform:translateY(2px);box-shadow:0 2px 0 #8a0009}
.btn.ghost{border:2px solid var(--line);color:#e6e7ea}
.btn.ghost:hover,.btn.ghost:focus-visible{border-color:var(--muted)}
.when{margin:16px 0 0;color:var(--muted);font-size:13px}
.card{margin:0}
.card img{display:block;width:100%;height:auto;aspect-ratio:1200/630;border-radius:10px;border:1px solid var(--line);background:var(--panel);box-shadow:0 18px 40px rgba(0,0,0,.45)}
.games{padding:8px 0 40px}
.games h2{display:flex;align-items:center;gap:10px;margin:0 0 16px;font-size:20px}
.games h2 span{background:var(--red);border-radius:999px;padding:0 10px;font-size:14px;line-height:24px}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(132px,1fr));gap:16px;margin:0;padding:0;list-style:none}
.game{background:var(--panel);border:1px solid var(--line);border-radius:8px;overflow:hidden;display:flex;flex-direction:column}
.cover{position:relative;aspect-ratio:5/7;background:repeating-linear-gradient(135deg,#23252c 0 10px,#202228 10px 20px)}
.cover .fb{position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:6px;padding:10px;text-align:center;color:var(--muted);font-size:12px;line-height:1.3;overflow:hidden}
.cover .fb i{display:block;width:34px;height:26px;border-radius:3px;background:#3a3d46;border-top:6px solid var(--red)}
.cover img{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;color:transparent}
.meta{padding:8px 10px 10px;display:flex;flex-direction:column;gap:2px;min-width:0}
.meta .t{font-weight:700;font-size:14px;line-height:1.25;overflow-wrap:anywhere}
.meta .v{color:var(--muted);font-size:12px;overflow-wrap:anywhere}
.meta .y{color:#7f8490;font-size:12px}
.meta .s{color:#7f8490;font-size:11px;font-style:italic;overflow-wrap:anywhere}
.msg{padding:72px 0 88px;text-align:center}
.msg .code{display:inline-block;font-weight:800;color:var(--red2);letter-spacing:.14em;font-size:14px;margin-bottom:8px}
.msg h1{margin-bottom:10px}
.msg p{color:var(--muted);margin:0 auto 24px;max-width:520px}
.msg .actions{justify-content:center}
footer{border-top:1px solid var(--line);color:var(--muted);font-size:13px;text-align:center;padding:20px 16px 28px}
@media (max-width:820px){.hero{grid-template-columns:1fr;gap:24px;padding-top:24px}.card{order:-1}.grid{grid-template-columns:repeat(auto-fill,minmax(104px,1fr));gap:12px}}
`;

const LOGO = `<svg width="28" height="20" viewBox="0 0 28 20" aria-hidden="true"><rect x="1" y="1" width="26" height="18" rx="3" fill="#2a2c33" stroke="#e4000f" stroke-width="2"/><rect x="6" y="6" width="16" height="6" rx="1" fill="#e4000f"/><rect x="5" y="14" width="3" height="3" fill="#fff"/><rect x="10" y="14" width="3" height="3" fill="#fff"/><rect x="15" y="14" width="3" height="3" fill="#fff"/><rect x="20" y="14" width="3" height="3" fill="#fff"/></svg>`;

function shell({ title, head, body, siteOrigin }) {
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex">
<meta name="theme-color" content="#15161a">
<title>${e(title)}</title>
${head}
<style>${CSS}</style>
</head>
<body>
<header class="top"><div class="wrap"><a class="brand" href="${e(siteOrigin)}">${LOGO}<span>Classic Cart Studio</span></a></div></header>
<main class="wrap">
${body}
</main>
<footer>Shared with Classic Cart Studio &middot; not affiliated with Nintendo</footer>
</body>
</html>
`;
}

export function fallbackDescription(count) {
  return count === 1
    ? '1 NES game collected in Classic Cart Studio.'
    : `${count} NES games collected in Classic Cart Studio.`;
}

function formatDate(iso) {
  try {
    return new Date(iso).toLocaleDateString('en-US', { year: 'numeric', month: 'long', day: 'numeric', timeZone: 'UTC' });
  } catch {
    return '';
  }
}

const SOURCE_LABELS = {
  personal: 'Personal game',
  unknown: 'Not in the Classic Cart Studio library',
};

function coverUrl(g, siteOrigin, pageUrl) {
  if (Number.isInteger(g.thumb) && g.thumb >= 0) return `${pageUrl}/t/${g.thumb}.jpg`;
  if (g.art) return `${siteOrigin}/${g.art}`;
  return '';
}

function gameItem(g, siteOrigin, pageUrl) {
  const sub = [g.year, g.publisher].filter(Boolean).join(' · ');
  const src = coverUrl(g, siteOrigin, pageUrl);
  const img = src ? `<img src="${e(src)}" alt="" loading="lazy" decoding="async">` : '';
  const label = SOURCE_LABELS[g.source];
  return `<li class="game"><div class="cover"><div class="fb" aria-hidden="true"><i></i>${e(g.title)}</div>${img}</div>` +
    `<div class="meta"><span class="t">${e(g.title)}</span>` +
    (g.version ? `<span class="v">${e(g.version)}</span>` : '') +
    (sub ? `<span class="y">${e(sub)}</span>` : '') +
    (label ? `<span class="s">${e(label)}</span>` : '') +
    `</div></li>`;
}

export function renderSharePage(snap, { siteOrigin, shareOrigin }) {
  const pageUrl = `${shareOrigin}/c/${snap.id}`;
  const imageUrl = `${pageUrl}/card.png`;
  const count = snap.games.length;
  const description = snap.description || fallbackDescription(count);
  const stats = snap.stats || {};
  const imageAlt = `Collection card for "${snap.name}" with ${count} NES game${count === 1 ? '' : 's'}`;

  const head = [
    `<meta name="description" content="${e(description)}">`,
    `<link rel="canonical" href="${e(pageUrl)}">`,
    `<meta property="og:type" content="website">`,
    `<meta property="og:site_name" content="Classic Cart Studio">`,
    `<meta property="og:title" content="${e(snap.name)}">`,
    `<meta property="og:description" content="${e(description)}">`,
    `<meta property="og:url" content="${e(pageUrl)}">`,
    `<meta property="og:image" content="${e(imageUrl)}">`,
    `<meta property="og:image:type" content="image/png">`,
    `<meta property="og:image:width" content="1200">`,
    `<meta property="og:image:height" content="630">`,
    `<meta property="og:image:alt" content="${e(imageAlt)}">`,
    `<meta name="twitter:card" content="summary_large_image">`,
    `<meta name="twitter:title" content="${e(snap.name)}">`,
    `<meta name="twitter:description" content="${e(description)}">`,
    `<meta name="twitter:image" content="${e(imageUrl)}">`,
    `<meta name="twitter:image:alt" content="${e(imageAlt)}">`,
  ].join('\n');

  // The games total is always the number of games actually shared.
  const statItems = [`<li><b>${count}</b>${count === 1 ? 'game' : 'games'}</li>`];
  if (Number.isInteger(stats.factory)) statItems.push(`<li><b>${stats.factory}</b>factory</li>`);
  if (Number.isInteger(stats.added)) statItems.push(`<li><b>${stats.added}</b>added</li>`);
  if (Number.isInteger(stats.personal) && stats.personal > 0) statItems.push(`<li><b>${stats.personal}</b>personal</li>`);
  const when = formatDate(snap.created);

  const body = `<section class="hero">
<div>
<p class="eyebrow">Shared NES collection</p>
<h1>${e(snap.name)}</h1>
<p class="desc">${e(description)}</p>
<ul class="stats">${statItems.join('')}</ul>
<div class="actions">
<a class="btn primary" href="${e(`${siteOrigin}/?share=${encodeURIComponent(snap.id)}`)}">Save to My Collections</a>
<a class="btn ghost" href="${e(siteOrigin)}">Open Classic Cart Studio</a>
</div>
${when ? `<p class="when">Shared ${e(when)}</p>` : ''}
</div>
<figure class="card"><img src="/c/${e(snap.id)}/card.png" width="1200" height="630" alt="${e(imageAlt)}"></figure>
</section>
<section class="games">
<h2>Games in this collection <span>${count}</span></h2>
<ul class="grid">
${snap.games.map((g) => gameItem(g, siteOrigin, pageUrl)).join('\n')}
</ul>
</section>`;

  return shell({ title: `${snap.name} · Classic Cart Studio`, head, body, siteOrigin });
}

export function renderMessagePage(status, { siteOrigin }) {
  const gone = status === 410;
  const heading = gone ? 'This collection was unpublished' : 'Collection not found';
  const text = gone
    ? 'The person who shared this collection has removed it.'
    : 'This share link does not exist. Check the link or ask for a new one.';
  const body = `<section class="msg">
<span class="code">${gone ? '410' : '404'}</span>
<h1>${e(heading)}</h1>
<p>${e(text)}</p>
<div class="actions"><a class="btn primary" href="${e(siteOrigin)}">Open Classic Cart Studio</a></div>
</section>`;
  return shell({ title: `${heading} · Classic Cart Studio`, head: '', body, siteOrigin });
}
