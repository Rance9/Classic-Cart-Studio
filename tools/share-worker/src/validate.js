// Strict validation of the ccs-share/1 publish payload.
import { HttpError } from './util.js';

export const SCHEMA = 'ccs-share/1';
export const MAX_GAMES = 500;
export const MAX_IMAGE_BYTES = 700 * 1024; // decoded PNG size limit
export const CARD_WIDTH = 1200;
export const CARD_HEIGHT = 630;

const TOP_KEYS = new Set(['schema', 'name', 'description', 'games', 'stats', 'image']);
const GAME_KEYS = new Set(['id', 'title', 'version', 'year', 'publisher', 'art']);
const STATS_KEYS = new Set(['games', 'factory', 'added']);

const GAME_ID = /^[0-9a-f]{16}$/;
const ART_PATH = /^art\/[0-9a-f]{16}\.jpg$/;
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

export function validatePublish(body) {
  if (!isPlainObject(body)) throw invalid('Body must be a JSON object.');
  checkKeys(body, TOP_KEYS, 'body');

  if (body.schema !== SCHEMA) throw invalid(`schema must be "${SCHEMA}".`, 'bad_schema');

  const name = text(body.name, 'name', 1, 80);
  const description = optionalText(body.description, 'description', 280);

  if (!Array.isArray(body.games)) throw invalid('games must be an array.');
  if (body.games.length < 1) throw invalid('games must contain at least 1 game.');
  if (body.games.length > MAX_GAMES) throw invalid(`games must contain at most ${MAX_GAMES} games.`, 'too_many_games');

  const games = body.games.map((g, i) => {
    const where = `games[${i}]`;
    if (!isPlainObject(g)) throw invalid(`${where} must be an object.`);
    checkKeys(g, GAME_KEYS, where);
    if (typeof g.id !== 'string' || !GAME_ID.test(g.id)) {
      throw invalid(`${where}.id must be 16 lowercase hex characters (personal games cannot be shared).`, 'bad_game_id');
    }
    let art = '';
    if (g.art !== undefined) {
      if (typeof g.art !== 'string' || (g.art !== '' && !ART_PATH.test(g.art))) {
        throw invalid(`${where}.art must be empty or "art/<16 hex>.jpg".`, 'bad_art_path');
      }
      art = g.art;
    }
    return {
      id: g.id,
      title: text(g.title, `${where}.title`, 1, 120),
      version: optionalText(g.version, `${where}.version`, 160),
      year: optionalText(g.year, `${where}.year`, 8),
      publisher: optionalText(g.publisher, `${where}.publisher`, 80),
      art,
    };
  });

  let stats;
  if (body.stats !== undefined) {
    if (!isPlainObject(body.stats)) throw invalid('stats must be an object.');
    checkKeys(body.stats, STATS_KEYS, 'stats');
    stats = {};
    for (const k of ['games', 'factory', 'added']) {
      const v = body.stats[k];
      if (v === undefined) continue;
      if (!Number.isInteger(v) || v < 0 || v > 1000) throw invalid(`stats.${k} must be an integer from 0 to 1000.`);
      stats[k] = v;
    }
  }

  const png = decodeCardImage(body.image);

  const snapshot = { schema: SCHEMA, name, description, games };
  if (stats) snapshot.stats = stats;
  return { snapshot, png };
}

export function decodeCardImage(image) {
  if (typeof image !== 'string' || image.length === 0) throw invalid('image must be a base64 PNG string.', 'bad_image');
  let b64 = image.replace(/^data:image\/png;base64,/i, '').replace(/\s+/g, '');
  const maxB64 = Math.ceil(MAX_IMAGE_BYTES / 3) * 4;
  if (b64.length > maxB64) throw invalid('image must be at most 700 KB.', 'image_too_large');
  if (b64.length % 4 !== 0 || !BASE64.test(b64)) throw invalid('image is not valid base64.', 'bad_image');
  let bin;
  try {
    bin = atob(b64);
  } catch {
    throw invalid('image is not valid base64.', 'bad_image');
  }
  if (bin.length > MAX_IMAGE_BYTES) throw invalid('image must be at most 700 KB.', 'image_too_large');
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
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
