// Strict validation of the ccs-share/2 publish payload. Every game of a
// collection is accepted whatever its source (catalog, extended, personal,
// unknown); nothing is dropped or truncated. Over-limit requests are rejected.
// The limits are resource safeguards for the public service, not collection
// sizes: the request body limit (worker.js) is the real bound; the counts below
// sit far above any real NES collection (the whole known NES library is about
// 4,500 versions) so that a complete library with personal games always fits.
// A collection's NES Classic Loadouts (named subsets planned for a console)
// travel with it: each lists ids of games in this snapshot.
import { HttpError } from './util.js';

export const SCHEMA = 'ccs-share/2';
export const MAX_GAMES = 50000;
export const MAX_THUMBS = 10000;
export const MAX_THUMB_BYTES = 48 * 1024; // decoded JPEG size limit
export const MAX_FOLDERS = 100;
export const MAX_IMAGE_BYTES = 3 * 1024 * 1024; // decoded PNG size limit (above any 1200x630 card with real box art)
export const CARD_WIDTH = 1200;
export const CARD_HEIGHT = 630;
export const MAX_STAT = 50000;
export const MAX_LOADOUTS = 100;

const TOP_KEYS = new Set(['schema', 'name', 'description', 'games', 'stats', 'home', 'image', 'thumbs', 'loadouts']);
const LOADOUT_KEYS = new Set(['name', 'games', 'home']);
const GAME_KEYS = new Set(['id', 'title', 'version', 'year', 'publisher', 'source', 'art', 'thumb', 'content_sha256', 'file_sha256', 'size']);
const STATS_KEYS = ['games', 'factory', 'added', 'personal', 'unavailable'];
const HOME_KEYS = new Set(['theme', 'folders']);
const FOLDER_KEYS = new Set(['name', 'order']);
const SOURCES = new Set(['catalog', 'extended', 'personal', 'unknown']);
const THEMES = new Set(['', 'arcade-grid', 'box-art-showcase', 'featured-shelf', 'adventure-map']);

const GAME_ID = /^[A-Za-z0-9][A-Za-z0-9_.:-]{0,79}$/;
const ART_PATH = /^art\/[0-9a-f]{16}\.jpg$/;
const SHA256 = /^[0-9a-f]{64}$/;
const MAX_SIZE = 16_777_216;
// C0/C1 controls, bidi embedding/override/isolate controls, and the BOM.
const CONTROL = /[\u0000-\u001F\u007F-\u009F‪-‮⁦-⁩﻿]/g;
const BASE64 = /^[A-Za-z0-9+/]*={0,2}$/;
const PNG_SIG = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

function invalid(message, code = 'invalid_payload') {
  return new HttpError(400, code, message);
}

function isPlainObject(v) {
  return v !== null && typeof v === 'object' && !Array.isArray(v);
}

export function cleanString(s) {
  let out = typeof s.toWellFormed === 'function' ? s.toWellFormed() : s;
  out = out.replace(/[\t\n\r]+/g, ' ').replace(CONTROL, '');
  return out.trim();
}

function text(value, field, min, max) {
  if (typeof value !== 'string') throw invalid(`Could not read ${field}. Reload the page and try again.`);
  const s = cleanString(value);
  if (s.length < min) throw invalid(`${field} is empty. Fill it in and publish again.`);
  if (s.length > max) throw invalid(`${field} is longer than ${max} characters. Shorten it and publish again.`);
  return s;
}

function optionalText(value, field, max) {
  if (value === undefined) return '';
  return text(value, field, 0, max);
}

function checkKeys(obj, allowed, where) {
  for (const k of Object.keys(obj)) {
    if (!allowed.has(k)) throw invalid(`The sharing service does not recognize "${k.slice(0, 40)}" in ${where}. Reload the page and try again.`, 'unknown_field');
  }
}

function optionalHash(value, field) {
  if (value === undefined || value === '') return '';
  if (typeof value !== 'string' || !SHA256.test(value)) {
    throw invalid(`Could not read ${field}. Reload the page and try again.`, 'bad_hash');
  }
  return value;
}

function validateGame(g, i, thumbCount) {
  const where = `game ${i + 1}`;
  if (!isPlainObject(g)) throw invalid(`Could not read ${where}. Reload the page and try again.`);
  checkKeys(g, GAME_KEYS, where);
  if (typeof g.id !== 'string' || !GAME_ID.test(g.id)) {
    throw invalid(`Could not read the ID of ${where}. Reload the page and try again.`, 'bad_game_id');
  }
  let source = 'catalog';
  if (g.source !== undefined) {
    if (typeof g.source !== 'string' || !SOURCES.has(g.source)) {
      throw invalid(`Could not tell where ${where} comes from. Reload the page and try again.`, 'bad_source');
    }
    source = g.source;
  }
  let art = '';
  if (g.art !== undefined) {
    if (typeof g.art !== 'string' || (g.art !== '' && !ART_PATH.test(g.art))) {
      throw invalid(`Could not read the cover of ${where}. Reload the page and try again.`, 'bad_art_path');
    }
    art = g.art;
  }
  const out = {
    id: g.id,
    title: text(g.title, `The title of ${where}`, 1, 120),
    version: optionalText(g.version, `The version name of ${where}`, 160),
    year: optionalText(g.year, `The year of ${where}`, 8),
    publisher: optionalText(g.publisher, `The publisher of ${where}`, 80),
    source,
    art,
    content_sha256: optionalHash(g.content_sha256, `the content checksum of ${where}`),
    file_sha256: optionalHash(g.file_sha256, `the file checksum of ${where}`),
  };
  if (g.size !== undefined) {
    if (!Number.isInteger(g.size) || g.size < 0 || g.size > MAX_SIZE) {
      throw invalid(`Could not read the file size of ${where}. Reload the page and try again.`);
    }
    out.size = g.size;
  }
  if (g.thumb !== undefined && g.thumb !== -1) {
    if (!Number.isInteger(g.thumb) || g.thumb < 0 || g.thumb >= thumbCount) {
      throw invalid(`Could not match the cover image of ${where}. Reload the page and try again.`, 'bad_thumb_index');
    }
    out.thumb = g.thumb;
  }
  return out;
}

function validateHome(home, ids, at = 'the HOME setup') {
  if (!isPlainObject(home)) throw invalid(`Could not read ${at}. Reload the page and try again.`, 'bad_home');
  checkKeys(home, HOME_KEYS, at);
  let theme = '';
  if (home.theme !== undefined) {
    if (typeof home.theme !== 'string' || !THEMES.has(home.theme)) {
      throw invalid(`The HOME theme in ${at} is not one this sharing service knows. Reload the page and try again.`, 'bad_home');
    }
    theme = home.theme;
  }
  const folders = [];
  if (home.folders !== undefined) {
    if (!Array.isArray(home.folders)) throw invalid(`Could not read the folders in ${at}. Reload the page and try again.`, 'bad_home');
    if (home.folders.length > MAX_FOLDERS) throw invalid(`Too many folders in ${at}: a shared collection can have at most ${MAX_FOLDERS}. Remove some and publish again.`, 'bad_home');
    home.folders.forEach((f, i) => {
      const where = `folder ${i + 1} in ${at}`;
      if (!isPlainObject(f)) throw invalid(`Could not read ${where}. Reload the page and try again.`, 'bad_home');
      checkKeys(f, FOLDER_KEYS, where);
      const name = text(f.name, `The name of ${where}`, 1, 60);
      const order = [];
      if (f.order !== undefined) {
        if (!Array.isArray(f.order)) throw invalid(`Could not read the game order of ${where}. Reload the page and try again.`, 'bad_home');
        if (f.order.length > MAX_GAMES) throw invalid(`The game order of ${where} lists more than ${MAX_GAMES} games. Reload the page and try again.`, 'bad_home');
        f.order.forEach((id, j) => {
          if (typeof id !== 'string' || !ids.has(id)) {
            throw invalid(`A game in ${where} is not in this collection. Reload the page and try again.`, 'bad_home');
          }
          order.push(id);
        });
      }
      folders.push({ name, order });
    });
  }
  return { theme, folders };
}

function validateLoadouts(list, ids) {
  if (!Array.isArray(list)) throw invalid('Could not read the NES Classic Loadouts. Reload the page and try again.', 'bad_loadout');
  if (list.length > MAX_LOADOUTS) throw invalid(`A shared collection can include at most ${MAX_LOADOUTS} NES Classic Loadouts; this one has ${list.length}. Remove some and publish again.`, 'too_many_loadouts');
  return list.map((l, i) => {
    const where = `loadout ${i + 1}`;
    if (!isPlainObject(l)) throw invalid(`Could not read ${where}. Reload the page and try again.`, 'bad_loadout');
    checkKeys(l, LOADOUT_KEYS, where);
    const name = text(l.name, `The name of ${where}`, 1, 60);
    if (!Array.isArray(l.games)) throw invalid(`Could not read the games of ${where}. Reload the page and try again.`, 'bad_loadout');
    const seen = new Set();
    l.games.forEach((id, j) => {
      if (typeof id !== 'string' || !ids.has(id)) throw invalid(`A game in ${where} is not in this collection. Reload the page and try again.`, 'bad_loadout');
      if (seen.has(id)) throw invalid(`A game appears twice in ${where}. Check that loadout and publish again.`, 'bad_loadout');
      seen.add(id);
    });
    const out = { name, games: l.games.slice() };
    if (l.home !== undefined) out.home = validateHome(l.home, ids, `the HOME setup of ${where}`);
    return out;
  });
}

export function validatePublish(body) {
  if (!isPlainObject(body)) throw invalid('The sharing service could not read the collection. Reload the page and try again.');
  checkKeys(body, TOP_KEYS, 'the collection');

  if (body.schema !== SCHEMA) throw invalid('This page is out of date for the sharing service. Reload the page and try again.', 'bad_schema');

  const name = text(body.name, 'The collection name', 1, 80);
  const description = optionalText(body.description, 'The collection description', 280);

  let rawThumbs = [];
  if (body.thumbs !== undefined) {
    if (!Array.isArray(body.thumbs)) throw invalid('Could not read the cover images. Reload the page and try again.', 'bad_thumb');
    if (body.thumbs.length > MAX_THUMBS) {
      throw invalid(`A shared collection can include at most ${MAX_THUMBS} cover images; this one has ${body.thumbs.length}.`, 'too_many_thumbs');
    }
    rawThumbs = body.thumbs;
  }

  if (!Array.isArray(body.games)) throw invalid('Could not read the game list. Reload the page and try again.');
  if (body.games.length < 1) throw invalid('The collection has no games. Add at least 1 game and publish again.');
  if (body.games.length > MAX_GAMES) {
    throw invalid(`A shared collection can hold at most ${MAX_GAMES} games; this one has ${body.games.length}.`, 'too_many_games');
  }

  const ids = new Set();
  const games = body.games.map((g, i) => {
    const game = validateGame(g, i, rawThumbs.length);
    if (ids.has(game.id)) throw invalid(`"${game.title}" (${game.id}) appears twice in the collection. Reload the page and try again.`, 'duplicate_game');
    ids.add(game.id);
    return game;
  });

  let home;
  if (body.home !== undefined) home = validateHome(body.home, ids);

  let loadouts;
  if (body.loadouts !== undefined) loadouts = validateLoadouts(body.loadouts, ids);

  let stats;
  if (body.stats !== undefined) {
    if (!isPlainObject(body.stats)) throw invalid('Could not read the game counts. Reload the page and try again.');
    checkKeys(body.stats, new Set(STATS_KEYS), 'the game counts');
    stats = {};
    for (const k of STATS_KEYS) {
      const v = body.stats[k];
      if (v === undefined) continue;
      if (!Number.isInteger(v) || v < 0 || v > MAX_STAT) throw invalid(`Could not read the game counts (${k}). Reload the page and try again.`);
      stats[k] = v;
    }
  }

  const png = decodeCardImage(body.image);
  const thumbs = rawThumbs.map((t, i) => decodeThumb(t, i));

  const snapshot = { schema: SCHEMA, name, description, games };
  if (stats) snapshot.stats = stats;
  if (home) snapshot.home = home;
  if (loadouts && loadouts.length) snapshot.loadouts = loadouts;
  return { snapshot, png, thumbs };
}

function decodeBase64(value, { prefix, maxBytes, field, code, tooLargeCode, tooLargeMessage }) {
  const b64 = value.replace(prefix, '').replace(/\s+/g, '');
  const maxB64 = Math.ceil(maxBytes / 3) * 4;
  if (b64.length > maxB64) throw invalid(tooLargeMessage, tooLargeCode);
  if (b64.length % 4 !== 0 || !BASE64.test(b64)) throw invalid(`Could not read ${field}. Reload the page and try again.`, code);
  let bin;
  try {
    bin = atob(b64);
  } catch {
    throw invalid(`Could not read ${field}. Reload the page and try again.`, code);
  }
  if (bin.length > maxBytes) throw invalid(tooLargeMessage, tooLargeCode);
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  return bytes;
}

// Game files never belong in a share: images that carry an iNES or FDS game header are refused at the service boundary too, whatever
// the client did. (A share is metadata: every game field is a short text or a hash; images are the only binary content.)
function carriesGameFile(bytes) {
  for (let i = 0; i + 4 <= bytes.length; i++) {
    if (bytes[i] === 0x4e && bytes[i + 1] === 0x45 && bytes[i + 2] === 0x53 && bytes[i + 3] === 0x1a) return true;   // "NES\x1A"
    if (bytes[i] === 0x46 && bytes[i + 1] === 0x44 && bytes[i + 2] === 0x53 && bytes[i + 3] === 0x1a) return true;   // "FDS\x1A"
  }
  return false;
}

export function decodeThumb(value, i) {
  const field = `cover image ${i + 1}`;
  if (typeof value !== 'string' || value.length === 0) throw invalid(`Could not read ${field}. Reload the page and try again.`, 'bad_thumb');
  const bytes = decodeBase64(value, {
    prefix: /^data:image\/jpeg;base64,/i,
    maxBytes: MAX_THUMB_BYTES,
    field,
    code: 'bad_thumb',
    tooLargeCode: 'thumb_too_large',
    tooLargeMessage: `Cover image ${i + 1} is larger than 48 KB. Reload the page and try again.`,
  });
  const n = bytes.length;
  if (n < 5 || bytes[0] !== 0xff || bytes[1] !== 0xd8 || bytes[2] !== 0xff || bytes[n - 2] !== 0xff || bytes[n - 1] !== 0xd9) {
    throw invalid(`Cover image ${i + 1} is not a JPEG image. Reload the page and try again.`, 'bad_thumb');
  }
  if (carriesGameFile(bytes)) throw invalid(`Cover image ${i + 1} contains ROM data, so it was refused. Shared collections never include ROM files.`, 'game_data_refused');
  return bytes;
}

export function decodeCardImage(image) {
  if (typeof image !== 'string' || image.length === 0) throw invalid('Could not read the collection card image. Reload the page and try again.', 'bad_image');
  const bytes = decodeBase64(image, {
    prefix: /^data:image\/png;base64,/i,
    maxBytes: MAX_IMAGE_BYTES,
    field: 'the collection card image',
    code: 'bad_image',
    tooLargeCode: 'image_too_large',
    tooLargeMessage: 'The collection card image is larger than 3 MB. Reload the page and try again.',
  });
  checkPng(bytes);
  if (carriesGameFile(bytes)) throw invalid('The collection card image contains ROM data, so it was refused. Shared collections never include ROM files.', 'game_data_refused');
  return bytes;
}

function ascii(bytes, start, len) {
  let s = '';
  for (let i = start; i < start + len; i++) s += String.fromCharCode(bytes[i]);
  return s;
}

function checkPng(bytes) {
  if (bytes.length < 8 + 25 + 12 || PNG_SIG.some((b, i) => bytes[i] !== b)) {
    throw invalid('The collection card image is not a PNG image. Reload the page and try again.', 'bad_image');
  }
  const dv = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  if (dv.getUint32(8) !== 13 || ascii(bytes, 12, 4) !== 'IHDR') {
    throw invalid('Could not read the collection card image. Reload the page and try again.', 'bad_image');
  }
  const width = dv.getUint32(16);
  const height = dv.getUint32(20);
  if (width !== CARD_WIDTH || height !== CARD_HEIGHT) {
    throw invalid(`The collection card image must be ${CARD_WIDTH} × ${CARD_HEIGHT} pixels; this one is ${width} × ${height}. Reload the page and try again.`, 'bad_image_size');
  }
  const end = bytes.length - 12;
  if (dv.getUint32(end) !== 0 || ascii(bytes, end + 4, 4) !== 'IEND') {
    throw invalid('The collection card image is incomplete. Reload the page and try again.', 'bad_image');
  }
}

export function validateUnpublish(body) {
  if (!isPlainObject(body)) throw invalid('The sharing service could not read the request. Reload the page and try again.');
  checkKeys(body, new Set(['id', 'token']), 'the request');
  if (typeof body.id !== 'string' || !/^[0-9A-Za-z]{10}$/.test(body.id)) throw invalid('This is not a valid shared collection link, so it was not unpublished.', 'bad_id');
  if (typeof body.token !== 'string' || !/^[A-Za-z0-9_-]{16,128}$/.test(body.token)) throw invalid('The key saved for this link is not valid, so the link was not unpublished.', 'bad_token');
  return { id: body.id, token: body.token };
}
