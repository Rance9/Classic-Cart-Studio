/* Baked font atlases (font/<name>.ccsf, written by adapter/native_home_pack.py): per-glyph 8-bit coverage, bearings and
 * advances in 1/64 px at one exact pixel size, plus the line metrics the browser uses for line-height: normal. Text is UTF-8; a codepoint without a glyph draws as '?'. Letter spacing and
 * the synthetic italic follow the CSS the browser HOME uses. */
#include "home.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static uint32_t rd32(const uint8_t *p) { return (uint32_t)p[0] | ((uint32_t)p[1] << 8) | ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24); }
static uint16_t rd16(const uint8_t *p) { return (uint16_t)(p[0] | (p[1] << 8)); }

int font_load(font *f, const char *path, char *err, size_t errlen) {
    memset(f, 0, sizeof *f); uint8_t *b; size_t n;
    if (ccs_read_file(path, &b, &n, 16u << 20)) { snprintf(err, errlen, "cannot read font %s", path); return -1; }
    if (n < 34 || memcmp(b, "CCSF2\0", 6)) { free(b); snprintf(err, errlen, "not a CCSF2 font: %s", path); return -2; }
    const uint8_t *p = b + 6; f->px = (int)rd32(p); f->ascent = (int)rd32(p + 4); f->descent = (int)rd32(p + 8); f->gap = (int)rd32(p + 12); uint32_t ng = rd32(p + 16), nk = rd32(p + 20); p += 24;
    if ((size_t)(p - b) + (size_t)ng * 20 + (size_t)nk * 12 + 4 > n) { free(b); snprintf(err, errlen, "truncated font %s", path); return -3; }
    f->g = calloc(ng ? ng : 1, sizeof *f->g); f->n = ng;
    for (uint32_t i = 0; i < ng; i++, p += 20) { glyph *g = &f->g[i]; g->cp = rd32(p); g->adv = (int32_t)rd32(p + 4); g->x0 = (int16_t)rd16(p + 8); g->y0 = (int16_t)rd16(p + 10); g->w = rd16(p + 12); g->h = rd16(p + 14); g->off = rd32(p + 16); }
    p += (size_t)nk * 12;   /* kerning pairs: none baked yet */
    f->nbits = rd32(p); p += 4;
    if ((size_t)(p - b) + f->nbits > n) { free(b); free(f->g); f->g = NULL; snprintf(err, errlen, "truncated glyph data in %s", path); return -3; }
    f->bits = malloc(f->nbits ? f->nbits : 1); memcpy(f->bits, p, f->nbits); free(b);
    for (uint32_t i = 0; i < ng; i++) if ((size_t)f->g[i].off + (size_t)f->g[i].w * f->g[i].h > f->nbits) { snprintf(err, errlen, "glyph outside the atlas in %s", path); font_free(f); return -4; }
    return 0;
}
void font_free(font *f) { free(f->g); free(f->bits); f->g = NULL; f->bits = NULL; }

uint32_t utf8_next(const char **s) {
    const uint8_t *p = (const uint8_t *)*s; uint32_t c = *p;
    if (c < 0x80) { *s += 1; return c; }
    int n = c >= 0xF0 ? 3 : c >= 0xE0 ? 2 : c >= 0xC0 ? 1 : 0; if (!n) { *s += 1; return 0xFFFD; }
    c &= 0x3F >> n; for (int i = 1; i <= n; i++) { if ((p[i] & 0xC0) != 0x80) { *s += i; return 0xFFFD; } c = (c << 6) | (p[i] & 0x3F); }
    *s += n + 1; return c;
}
static const glyph *find(const font *f, uint32_t cp) {
    size_t lo = 0, hi = f->n; while (lo < hi) { size_t m = (lo + hi) / 2; if (f->g[m].cp < cp) lo = m + 1; else hi = m; }
    if (lo < f->n && f->g[lo].cp == cp) return &f->g[lo];
    if (cp == 0xA0) return find(f, ' ');
    return cp == '?' ? NULL : find(f, '?');
}
int text_w(const font *f, const char *s, size_t len, double spacing) {
    const char *e = s + len; int64_t pen = 0; int ls = (int)(spacing * 64 + 0.5);
    while (s < e && *s) { const glyph *g = find(f, utf8_next(&s)); if (g) pen += g->adv; pen += ls; }
    return (int)((pen + 32) >> 6);
}
int text_draw(framebuffer *fb, mask *m, rect clip, const font *f, double x, int y, const char *s, size_t len, color c, double spacing, double shear) {
    const char *e = s + len; int64_t pen = (int64_t)(x * 64 + (x >= 0 ? 0.5 : -0.5)); int ls = (int)(spacing * 64 + 0.5);
    while (s < e && *s) {
        const glyph *g = find(f, utf8_next(&s)); if (!g) continue;
        int gx = (int)((pen + 32) >> 6) + g->x0, gy = y + g->y0; const uint8_t *src = f->bits + g->off;
        for (int r = 0; r < g->h; r++) {
            int yy = gy + r; if (yy < clip.y0 || yy >= clip.y1) continue;
            int sh = shear > 0 ? (int)(shear * (y - yy) + (y - yy >= 0 ? 0.5 : -0.5)) : 0;
            for (int q = 0; q < g->w; q++) {
                int v = src[r * g->w + q]; if (!v) continue; int xx = gx + q + sh; if (xx < clip.x0 || xx >= clip.x1) continue;
                if (m) { if (xx >= m->x && xx < m->x + m->w && yy >= m->y && yy < m->y + m->h) { uint8_t *d = m->a + (size_t)(yy - m->y) * m->w + (xx - m->x); int nv = *d + v * (255 - *d) / 255; *d = (uint8_t)(nv > 255 ? 255 : nv); } }
                else if (xx >= 0 && yy >= 0 && xx < fb->w && yy < fb->h) {
                    uint8_t *d = fb->rgb + ((size_t)yy * fb->w + xx) * 3; int a = c.a * v / 255;
                    d[0] = (uint8_t)((d[0] * (255 - a) + ((c.rgb >> 16) & 255) * a + 127) / 255); d[1] = (uint8_t)((d[1] * (255 - a) + ((c.rgb >> 8) & 255) * a + 127) / 255); d[2] = (uint8_t)((d[2] * (255 - a) + (c.rgb & 255) * a + 127) / 255);
                }
            }
        }
        pen += g->adv + ls;
    }
    return (int)((pen + 32) >> 6);
}
size_t text_fit(const font *f, const char *s, int maxw, double spacing, char *out, size_t outlen) {
    size_t n = strlen(s); if (text_w(f, s, n, spacing) <= maxw || outlen < 8) { snprintf(out, outlen, "%s", s); return strlen(out); }
    const char *ell = "\xE2\x80\xA6"; int ew = text_w(f, ell, 3, spacing); size_t keep = 0; const char *p = s;
    while (*p) { const char *q = p; utf8_next(&q); if (text_w(f, s, (size_t)(q - s), spacing) + ew > maxw) break; p = q; keep = (size_t)(p - s); }
    while (keep && s[keep - 1] == ' ') keep--;
    if (keep + 4 > outlen) keep = outlen - 4; memcpy(out, s, keep); memcpy(out + keep, ell, 3); out[keep + 3] = 0; return keep + 3;
}
/* greedy word wrap (CSS normal white-space: spaces collapse at line ends; a word wider than the line breaks by character) */
size_t text_wrap(const font *f, const char *s, int maxw, double spacing, span *out, size_t max) {
    size_t n = 0, len = strlen(s), i = 0;
    while (i < len && n < max) {
        while (i < len && s[i] == ' ') i++; if (i >= len) break;
        size_t start = i, best = 0, j = i;
        for (;;) {
            size_t k = j; while (k < len && s[k] != ' ') k++;           /* end of the next word */
            if (text_w(f, s + start, k - start, spacing) <= maxw) { best = k; j = k; if (k >= len) break; while (j < len && s[j] == ' ') j++; if (j >= len) break; continue; }
            if (!best) { const char *p = s + start; const char *q = p; while (q < s + k) { const char *r = q; utf8_next(&r); if (text_w(f, p, (size_t)(r - p), spacing) > maxw && q > p) break; q = r; } best = (size_t)(q - s); }
            break;
        }
        out[n].start = start; out[n].len = best - start; n++; i = best;
    }
    return n;
}
