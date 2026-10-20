// Server-rendered HTML for share pages. No third-party requests; the only script is the game search below, allowed by its hash.
import { escapeHtml as e } from './util.js';

// img-src: 'self' (cards, thumbs), the share origin when it is addressed by
// another host name, and SITE_ORIGIN (catalog cover art).
// the game search on a share page: the only script, allowed by its SHA-256 (the tests check the hash matches the text)
export const SEARCH_JS = "(function(){var q=document.getElementById('q'),n=document.getElementById('qn');if(!q)return;var li=[].slice.call(document.querySelectorAll('.grid .game'));q.addEventListener('input',function(){var s=q.value.toLowerCase().trim(),c=0;li.forEach(function(x){var h=s&&x.getAttribute('data-s').indexOf(s)<0;x.hidden=h;if(!h)c++;});n.textContent=s?c+' of '+li.length:'';});})();";
export const SEARCH_JS_SHA256 = 'x31ZaD1xRsX5QN6tQ9f5YXUdcSwU3onVqLy1CouG6I4=';

export function htmlHeaders(siteOrigin, shareOrigin) {
  const extra = shareOrigin && shareOrigin !== siteOrigin ? ` ${shareOrigin}` : '';
  return {
    'Content-Type': 'text/html; charset=utf-8',
    'Content-Security-Policy':
      `default-src 'none'; img-src 'self'${extra} ${siteOrigin} data:; style-src 'unsafe-inline'; script-src 'sha256-${SEARCH_JS_SHA256}'; ` +
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
.hero{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1.05fr);gap:32px;align-items:center;padding:36px 0 28px}
.eyebrow{margin:0 0 8px;color:var(--red2);font-weight:700;font-size:13px;letter-spacing:.14em;text-transform:uppercase}
h1{margin:0 0 10px;font-size:clamp(28px,4.4vw,44px);line-height:1.1;overflow-wrap:anywhere}
.sum{margin:0 0 6px;color:var(--muted);font-size:15px}.sum b{color:var(--text)}
.desc{margin:0;color:#d9dbe1;font-size:16px;overflow-wrap:anywhere}
.lead{margin:0 0 20px}
.loadouts{margin:0 0 24px}.loadouts h2{font-size:17px;margin:0 0 2px}.loadouts h2 span{color:var(--muted);font-weight:500}.loadouts .note{color:var(--muted);margin:0 0 6px;font-size:13px}
.loadouts details{margin:2px 0}.loadouts summary{cursor:pointer}.loadouts ol{margin:6px 0 10px;padding-left:24px;color:var(--muted);columns:2 260px}
.appnote{display:flex;flex-wrap:wrap;gap:4px 14px;align-items:baseline;color:var(--muted);font-size:13px;margin:12px 0 0;max-width:620px}.appnote a{color:var(--text)}
.appnote summary{cursor:pointer;color:var(--text);text-decoration:underline;text-underline-offset:2px}.appnote details[open]{flex-basis:100%}.appnote details p{margin:4px 0 0}
.search{display:flex;gap:10px;align-items:center;margin:0 0 14px}.search input{flex:1;max-width:420px;background:var(--panel2);border:1px solid var(--line);border-radius:8px;color:var(--text);padding:9px 12px;font:inherit}#qn{color:var(--muted);font-size:13px}.game[hidden]{display:none}
.actions{display:flex;flex-wrap:wrap;gap:12px;align-items:center}
.btn{display:inline-flex;align-items:center;justify-content:center;min-height:46px;padding:0 20px;border-radius:8px;font-weight:700;text-decoration:none}
.btn.primary{background:var(--red);color:#fff;box-shadow:0 4px 0 #8a0009}
.btn.primary:hover,.btn.primary:focus-visible{background:var(--red2)}
.btn.primary:active{transform:translateY(2px);box-shadow:0 2px 0 #8a0009}
.btn.ghost{border:2px solid var(--line);color:#e6e7ea}
.btn.ghost:hover,.btn.ghost:focus-visible{border-color:var(--muted)}
.card{margin:0}
.card img{display:block;width:100%;height:auto;aspect-ratio:1200/630;border-radius:10px;border:1px solid var(--line);background:var(--panel);box-shadow:0 18px 40px rgba(0,0,0,.45)}
.games{padding:8px 0 40px}
.games h2{margin:0 0 12px;font-size:20px}
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
@media (max-width:820px){.hero{gap:20px;padding-top:24px}.grid{grid-template-columns:repeat(auto-fill,minmax(104px,1fr));gap:12px}}
@media (max-width:640px){.hero{display:block;padding:20px 0 20px}.card{display:none}h1{margin-bottom:6px}.lead{margin-bottom:16px}.actions{gap:10px}.actions .btn{flex:1 1 200px;padding:0 14px}.loadouts{margin-bottom:20px}}
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
${body.includes('id="q"') ? `<script>${SEARCH_JS}</script>` : ''}
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
  unknown: 'Not in the Game Library',
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
  return `<li class="game" data-s="${e([g.title, g.version, g.publisher, g.year].filter(Boolean).join(' ').toLowerCase())}"><div class="cover"><div class="fb" aria-hidden="true"><i></i>${e(g.title)}</div>${img}</div>` +
    `<div class="meta"><span class="t">${e(g.title)}</span>` +
    (g.version ? `<span class="v">${e(g.version)}</span>` : '') +
    (sub ? `<span class="y">${e(sub)}</span>` : '') +
    (label ? `<span class="s">${e(label)}</span>` : '') +
    `</div></li>`;
}

// the collection's NES Classic Loadouts: named sets of its games planned for an NES Classic
function loadoutsBlock(snap) {
  const list = Array.isArray(snap.loadouts) ? snap.loadouts : [];
  if (!list.length) return '';
  const title = new Map(snap.games.map((g) => [g.id, g.version ? `${g.title} (${g.version})` : g.title]));
  return `<section class="loadouts"><h2>NES Classic Loadouts <span>${list.length}</span></h2><p class="note">Plans for which games go on an NES Classic. Saving the collection saves them too.</p>` +
    list.map((l) => `<details><summary><b>${e(l.name)}</b> · ${l.games.length} game${l.games.length === 1 ? '' : 's'}</summary><ol>${l.games.map((id) => `<li>${e(title.get(id) || id)}</li>`).join('')}</ol></details>`).join('') + '</section>\n';
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

  // One summary line. The games total is always the number of games actually shared; built-in and added appear only when
  // the collection mixes both, personal only when there are some.
  const factory = Number.isInteger(stats.factory) ? stats.factory : 0;
  const parts = [`<b>${count} ${count === 1 ? 'game' : 'games'}</b>`];
  if (factory > 0) parts.push(`${factory} built-in`);
  if (factory > 0 && Number.isInteger(stats.added) && stats.added > 0) parts.push(`${stats.added} added`);
  if (Number.isInteger(stats.personal) && stats.personal > 0) parts.push(`${stats.personal} personal`);
  const when = formatDate(snap.created);
  if (when) parts.push(`Shared ${e(when)}`);

  const body = `<section class="hero">
<div>
<p class="eyebrow">Shared NES collection</p>
<h1>${e(snap.name)}</h1>
<div class="lead"><p class="sum">${parts.join(' · ')}</p>
${snap.description ? `<p class="desc">${e(snap.description)}</p>` : ''}</div>
<div class="actions">
<a class="btn primary" href="${e(`${siteOrigin}/?share=${encodeURIComponent(snap.id)}`)}">Save to My Collections</a>
<a class="btn ghost" href="${e(`classiccartstudio://share/${encodeURIComponent(snap.id)}`)}">Open in Windows App</a>
</div>
<div class="appnote"><span>Don't have the Windows app? <a href="${e(`${siteOrigin}/app`)}">Download Classic Cart Studio</a></span>
<details><summary>Nothing happened?</summary><p>Your browser asks before it opens Classic Cart Studio for Windows. If nothing opened, download and install the Windows app, then choose Open in Windows App again. Shared collections never include ROM files.</p></details></div>
</div>
<figure class="card"><img src="/c/${e(snap.id)}/card.png" width="1200" height="630" alt="${e(imageAlt)}" loading="lazy" decoding="async"></figure>
</section>
${loadoutsBlock(snap)}<section class="games">
<h2>Games in this collection</h2>
<div class="search"><input id="q" type="search" placeholder="Search these games" aria-label="Search the games in this collection"><span id="qn" aria-live="polite"></span></div>
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
    ? 'The person who shared it removed the link. Ask them for a new one if you still want it.'
    : 'This link does not match a shared collection. Check the link, or ask for a new one.';
  const body = `<section class="msg">
<span class="code">${gone ? 'Link removed' : 'Link not found'}</span>
<h1>${e(heading)}</h1>
<p>${e(text)}</p>
<div class="actions"><a class="btn primary" href="${e(siteOrigin)}">Go to Classic Cart Studio</a></div>
</section>`;
  return shell({ title: `${heading} · Classic Cart Studio`, head: '', body, siteOrigin });
}
