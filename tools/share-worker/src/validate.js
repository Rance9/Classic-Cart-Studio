// Strict validation of the ccs-share/2 publish payload. Every game of a
// collection is accepted whatever its source (catalog, extended, personal,
// unknown); nothing is dropped or truncated. Over-limit requests are rejected.
import { HttpError } from './util.js';

export const SCHEMA = 'ccs-share/2';
export const MAX_GAMES = 2000;
export const MAX_THUMBS = 400;
export const MAX_THUMB_BYTES = 48 * 1024; // decoded JPEG size limit
export const MAX_FOLDERS = 100;
export const MAX_IMAGE_BYTES = 700 * 1024; // decoded PNG size limit
export const CARD_WIDTH = 1200;
export const CARD_HEIGHT = 630;
export const MAX_STAT = 5000;

const TOP_KEYS = new Set(['schema', 'name', 'description', 'games', 'stats', 'home', 'image', 'thumbs']);
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
  if (typeof value !== 'string') throw invalid(`${field} must be a string.`);
  const s = cleanString(value);
  if (s.length < min) throw invalid(`${field} must not be empty.`);
  if (s.length > max) throw invalid(`${field} must be at most ${max} characters.`);
  return s;
}

function optionalText(value, field, max) {
  if (value === undefined) return '';
  return text(value, field, 0, max);
}

function checkKeys(obj, allowed, where) {
  for (const k of Object.keys(obj)) {
    if (!allowed.has(k)) throw invalid(`Unknown field "${k.slice(0, 40)}" in ${where}.`, 'unknown_field');
  }
}

function optionalHash(value, field) {
  if (value === undefined || value === '') return '';
  if (typeof value !== 'string' || !SHA256.test(value)) {
    throw invalid(`${field} must be empty or 64 lowercase hex characters.`, 'bad_hash');
  }
  return value;
}

function validateGame(g, i, thumbCount) {
  const where = `games[${i}]`;
  if (!isPlainObject(g)) throw invalid(`${where} must be an object.`);
  checkKeys(g, GAME_KEYS, where);
  if (typeof g.id !== 'string' || !GAME_ID.test(g.id)) {
    throw invalid(`${where}.id must be 1 to 80 letters, digits, "_", ".", ":" or "-", starting with a letter or digit.`, 'bad_game_id');
  }
  let source = 'catalog';
  if (g.source !== undefined) {
    if (typeof g.source !== 'string' || !SOURCES.has(g.source)) {
      throw invalid(`${where}.source must be one of catalog, extended, personal, unknown.`, 'bad_source');
    }
    source = g.source;
  }
  let art = '';
  if (g.art !== undefined) {
    if (typeof g.art !== 'string' || (g.art !== '' && !ART_PATH.test(g.art))) {
      throw invalid(`${where}.art must be empty or "art/<16 hex>.jpg".`, 'bad_art_path');
    }
    art = g.art;
  }
  const out = {
    id: g.id,
    title: text(g.title, `${where}.title`, 1, 120),
    version: optionalText(g.version, `${where}.version`, 160),
    year: optionalText(g.year, `${where}.year`, 8),
    publisher: optionalText(g.publisher, `${where}.publisher`, 80),
    source,
    art,
    content_sha256: optionalHash(g.content_sha256, `${where}.content_sha256`),
    file_sha256: optionalHash(g.file_sha256, `${where}.file_sha256`),
  };
  if (g.size !== undefined) {
    if (!Number.isInteger(g.size) || g.size < 0 || g.size > MAX_SIZE) {
      throw invalid(`${where}.size must be an integer from 0 to ${MAX_SIZE}.`);
    }
    out.size = g.size;
  }
  if (g.thumb !== undefined && g.thumb !== -1) {
    if (!Number.isInteger(g.thumb) || g.thumb < 0 || g.thumb >= thumbCount) {
      throw invalid(`${where}.thumb must be -1 or the index of an entry in thumbs (there are ${thumbCount}).`, 'bad_thumb_index');
    }
    out.thumb = g.thumb;
  }
  return out;
}

function validateHome(home, ids) {
  if (!isPlainObject(home)) throw invalid('home must be an object.', 'bad_home');
  checkKeys(home, HOME_KEYS, 'home');
  let theme = '';
  if (home.theme !== undefined) {
    if (typeof home.theme !== 'string' || !THEMES.has(home.theme)) {
      throw invalid('home.theme must be "", arcade-grid, box-art-showcase, featured-shelf or adventure-map.', 'bad_home');
    }
    theme = home.theme;
  }
  const folders = [];
  if (home.folders !== undefined) {
    if (!Array.isArray(home.folders)) throw invalid('home.folders must be an array.', 'bad_home');
    if (home.folders.length > MAX_FOLDERS) throw invalid(`home.folders must contain at most ${MAX_FOLDERS} folders.`, 'bad_home');
    home.folders.forEach((f, i) => {
      const where = `home.folders[${i}]`;
      if (!isPlainObject(f)) throw invalid(`${where} must be an object.`, 'bad_home');
      checkKeys(f, FOLDER_KEYS, where);
      const name = text(f.name, `${where}.name`, 1, 60);
      const order = [];
      if (f.order !== undefined) {
        if (!Array.isArray(f.order)) throw invalid(`${where}.order must be an array.`, 'bad_home');
        if (f.order.length > MAX_GAMES) throw invalid(`${where}.order must contain at most ${MAX_GAMES} ids.`, 'bad_home');
        f.order.forEach((id, j) => {
          if (typeof id !== 'string' || !ids.has(id)) {
            throw invalid(`${where}.order[${j}] is not the id of a game in this collection.`, 'bad_home');
          }
          order.push(id);
        });
      }
      folders.push({ name, order });
    });
  }
  return { theme, folders };
}

export function validatePublish(body) {
  if (!isPlainObject(body)) throw invalid('Body must be a JSON object.');
  checkKeys(body, TOP_KEYS, 'body');

  if (body.schema !== SCHEMA) throw invalid(`schema must be "${SCHEMA}".`, 'bad_schema');

  const name = text(body.name, 'name', 1, 80);
  const description = optionalText(body.description, 'description', 280);

  let rawThumbs = [];
  if (body.thumbs !== undefined) {
    if (!Array.isArray(body.thumbs)) throw invalid('thumbs must be an array.', 'bad_thumb');
    if (body.thumbs.length > MAX_THUMBS) {
      throw invalid(`A shared collection can include at most ${MAX_THUMBS} cover thumbnails; this one has ${body.thumbs.length}.`, 'too_many_thumbs');
    }
    rawThumbs = body.thumbs;
  }

  if (!Array.isArray(body.games)) throw invalid('games must be an array.');
  if (body.games.length < 1) throw invalid('games must contain at least 1 game.');
  if (body.games.length > MAX_GAMES) {
    throw invalid(`A shared collection can hold at most ${MAX_GAMES} games; this one has ${body.games.length}.`, 'too_many_games');
  }

  const ids = new Set();
  const games = body.games.map((g, i) => {
    const game = validateGame(g, i, rawThumbs.length);
    if (ids.has(game.id)) throw invalid(`games[${i}].id "${game.id}" appears more than once.`, 'duplicate_game');
    ids.add(game.id);
    return game;
  });

  let home;
  if (body.home !== undefined) home = validateHome(body.home, ids);

  let stats;
  if (body.stats !== undefined) {
    if (!isPlainObject(body.stats)) throw invalid('stats must be an object.');
    checkKeys(body.stats, new Set(STATS_KEYS), 'stats');
    stats = {};
    for (const k of STATS_KEYS) {
      const v = body.stats[k];
      if (v === undefined) continue;
      if (!Number.isInteger(v) || v < 0 || v > MAX_STAT) throw invalid(`stats.${k} must be an integer from 0 to ${MAX_STAT}.`);
      stats[k] = v;
    }
  }

  const png = decodeCardImage(body.image);
  const thumbs = rawThumbs.map((t, i) => decodeThumb(t, i));

  const snapshot = { schema: SCHEMA, name, description, games };
  if (stats) snapshot.stats = stats;
  if (home) snapshot.home = home;
  return { snapshot, png, thumbs };
}

function decodeBase64(value, { prefix, maxBytes, field, code, tooLargeCode, tooLargeMessage }) {
  const b64 = value.replace(prefix, '').replace(/\s+/g, '');
  const maxB64 = Math.ceil(maxBytes / 3) * 4;
  if (b64.length > maxB64) throw invalid(tooLargeMessage, tooLargeCode);
  if (b64.length % 4 !== 0 || !BASE64.test(b64)) throw invalid(`${field} is not valid base64.`, code);
  let bin;
  try {
    bin = atob(b64);
  } catch {
    throw invalid(`${field} is not valid base64.`, code);
  }
  if (bin.length > maxBytes) throw invalid(tooLargeMessage, tooLargeCode);
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  return bytes;
}

export function decodeThumb(value, i) {
  const field = `thumbs[${i}]`;
  if (typeof value !== 'string' || value.length === 0) throw invalid(`${field} must be a base64 JPEG string.`, 'bad_thumb');
  const bytes = decodeBase64(value, {
    prefix: /^data:image\/jpeg;base64,/i,
    maxBytes: MAX_THUMB_BYTES,
    field,
    code: 'bad_thumb',
    tooLargeCode: 'thumb_too_large',
    tooLargeMessage: `${field} must be at most 48 KB.`,
  });
  const n = bytes.length;
  if (n < 5 || bytes[0] !== 0xff || bytes[1] !== 0xd8 || bytes[2] !== 0xff || bytes[n - 2] !== 0xff || bytes[n - 1] !== 0xd9) {
    throw invalid(`${field} must be a JPEG file.`, 'bad_thumb');
  }
  return bytes;
}

export function decodeCardImage(image) {
  if (typeof image !== 'string' || image.length === 0) throw invalid('image must be a base64 PNG string.', 'bad_image');
  const bytes = decodeBase64(image, {
    prefix: /^data:image\/png;base64,/i,
    maxBytes: MAX_IMAGE_BYTES,
    field: 'image',
    code: 'bad_image',
    tooLargeCode: 'image_too_large',
    tooLargeMessage: 'image must be at most 700 KB.',
  });
  checkPng(bytes);
  return bytes;
}

function ascii(bytes, start, len) {
  let s = '';
  for (let i = start; i < start + len; i++) s += String.fromCharCode(bytes[i]);
  return s;
}

function checkPng(bytes) {
  if (bytes.length < 8 + 25 + 12 || PNG_SIG.some((b, i) => bytes[i] !== b)) {
    throw invalid('image must be a PNG file.', 'bad_image');
  }
  const dv = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  if (dv.getUint32(8) !== 13 || ascii(bytes, 12, 4) !== 'IHDR') {
    throw invalid('image PNG header is malformed.', 'bad_image');
  }
  const width = dv.getUint32(16);
  const height = dv.getUint32(20);
  if (width !== CARD_WIDTH || height !== CARD_HEIGHT) {
    throw invalid(`image must be ${CARD_WIDTH}x${CARD_HEIGHT} (got ${width}x${height}).`, 'bad_image_size');
  }
  const end = bytes.length - 12;
  if (dv.getUint32(end) !== 0 || ascii(bytes, end + 4, 4) !== 'IEND') {
    throw invalid('image PNG is truncated.', 'bad_image');
  }
}

export function validateUnpublish(body) {
  if (!isPlainObject(body)) throw invalid('Body must be a JSON object.');
  checkKeys(body, new Set(['id', 'token']), 'body');
  if (typeof body.id !== 'string' || !/^[0-9A-Za-z]{10}$/.test(body.id)) throw invalid('id is invalid.', 'bad_id');
  if (typeof body.token !== 'string' || !/^[A-Za-z0-9_-]{16,128}$/.test(body.token)) throw invalid('token is invalid.', 'bad_token');
  return { id: body.id, token: body.token };
}
