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
void font_free(font *f) { free(f->g); free(f->bits); f->g = NULL; f->bits = NULL; if (f->hi) { font_free(f->hi); free(f->hi); f->hi = NULL; } }

uint32_t utf8_next(const char **s) {
    const uint8_t *p = (const uint8_t *)*s; uint32_t c = *p;
    if (c < 0x80) { *s += 1; return c; }
    int n = c >= 0xF0 ? 3 : c >= 0xE0 ? 2 : c >= 0xC0 ? 1 : 0; if (!n) { *s += 1; return 0xFFFD; }
    c &= 0x3F >> n; for (int i = 1; i <= n; i++) { if ((p[i] & 0xC0) != 0x80) { *s += i; return 0xFFFD; } c = (c << 6) | (p[i] & 0x3F); }
    *s += n + 1; return c;
}
/* the render check (design_check.c) only: text left out of the picture, each drawn text measured against the picture without text,
   characters a face has no glyph for reported. NULL / 0 everywhere else: nothing here changes a frame */
int ccs_text_off, ccs_text_label; const uint8_t *ccs_text_bg; ccs_text_rec_fn ccs_text_rec; ccs_missing_glyph_fn ccs_missing_glyph; void (*ccs_overlay_begin)(void);
static const uint16_t LIN[256] = {0,20,40,60,80,99,119,139,159,179,199,219,241,264,288,313,340,367,396,427,458,491,526,562,599,637,677,718,761,805,851,898,947,997,1048,1101,1156,1212,1270,1330,1391,1453,1517,1583,1651,1720,1790,1863,1937,2013,2090,2170,2250,2333,2418,2504,2592,2681,2773,2866,2961,3058,3157,3258,3360,3464,3570,3678,3788,3900,4014,4129,4247,4366,4488,4611,4736,4864,4993,5124,5257,5392,5530,5669,5810,5953,6099,6246,6395,6547,6700,6856,7014,7174,7335,7500,7666,7834,8004,8177,8352,8528,8708,8889,9072,9258,9445,9635,9828,10022,10219,10417,10619,10822,11028,11235,11446,11658,11873,12090,12309,12530,12754,12980,13209,13440,13673,13909,14146,14387,14629,14874,15122,15371,15623,15878,16135,16394,16656,16920,17187,17456,17727,18001,18277,18556,18837,19121,19407,19696,19987,20281,20577,20876,21177,21481,21787,22096,22407,22721,23038,23357,23678,24002,24329,24658,24990,25325,25662,26001,26344,26688,27036,27386,27739,28094,28452,28813,29176,29542,29911,30282,30656,31033,31412,31794,32179,32567,32957,33350,33745,34143,34544,34948,35355,35764,36176,36591,37008,37429,37852,38278,38706,39138,39572,40009,40449,40891,41337,41785,42236,42690,43147,43606,44069,44534,45002,45473,45947,46423,46903,47385,47871,48359,48850,49344,49841,50341,50844,51349,51858,52369,52884,53401,53921,54445,54971,55500,56032,56567,57105,57646,58190,58737,59287,59840,60396,60955,61517,62082,62650,63221,63795,64372,64952,65535};
uint32_t ccs_luminance(uint32_t rgb) { return (13933u * LIN[(rgb >> 16) & 255] + 46871u * LIN[(rgb >> 8) & 255] + 4732u * LIN[rgb & 255]) >> 16; }   /* 0.2126, 0.7152, 0.0722 of 65536 */
int ccs_contrast10(uint32_t a, uint32_t b) { uint32_t x = ccs_luminance(a), y = ccs_luminance(b), hi = x > y ? x : y, lo = x > y ? y : x; return (int)(((uint64_t)hi + 3277u) * 10u / ((uint64_t)lo + 3277u)); }   /* (L1 + 0.05) / (L2 + 0.05), times ten */
static const glyph *find(const font *f, uint32_t cp) {
    size_t lo = 0, hi = f->n; while (lo < hi) { size_t m = (lo + hi) / 2; if (f->g[m].cp < cp) lo = m + 1; else hi = m; }
    if (lo < f->n && f->g[lo].cp == cp) return &f->g[lo];
    if (cp == 0xA0) return find(f, ' ');
    if (ccs_missing_glyph && cp != '?' && cp >= 0x20) ccs_missing_glyph(f, cp);
    return cp == '?' ? NULL : find(f, '?');
}
int font_has(const font *f, uint32_t cp) { size_t lo = 0, hi = f->n; while (lo < hi) { size_t m = (lo + hi) / 2; if (f->g[m].cp < cp) lo = m + 1; else hi = m; } return lo < f->n && f->g[lo].cp == cp; }
int text_w(const font *f, const char *s, size_t len, double spacing) {
    const char *e = s + len; int64_t pen = 0; int ls = (int)(spacing * 64 + 0.5);
    while (s < e && *s) { const glyph *g = find(f, utf8_next(&s)); if (g) pen += g->adv; pen += ls; }
    return (int)((pen + 32) >> 6);
}
static void put(framebuffer *fb, mask *m, int xx, int yy, int v, color c) {
    if (m) { if (xx >= m->x && xx < m->x + m->w && yy >= m->y && yy < m->y + m->h) { uint8_t *d = m->a + (size_t)(yy - m->y) * m->w + (xx - m->x); int nv = *d + v * (255 - *d) / 255; *d = (uint8_t)(nv > 255 ? 255 : nv); } }
    else if (xx >= 0 && yy >= 0 && xx < fb->w && yy < fb->h) {
        uint8_t *d = fb->rgb + ((size_t)yy * fb->w + xx) * 3; int a = c.a * v / 255;
        d[0] = (uint8_t)((d[0] * (255 - a) + ((c.rgb >> 16) & 255) * a + 127) / 255); d[1] = (uint8_t)((d[1] * (255 - a) + ((c.rgb >> 8) & 255) * a + 127) / 255); d[2] = (uint8_t)((d[2] * (255 - a) + (c.rgb & 255) * a + 127) / 255);
    } }
/* ccs_scale > 1: the pen moves by this font's advances (the stage layout), each glyph is drawn from the high-resolution atlas at the
   pen times the scale, or this font's own glyph with every pixel doubled when there is none */
static int text_draw_scaled(framebuffer *fb, mask *m, rect clip, const font *f, double x, int y, const char *s, size_t len, color c, double spacing, double shear) {
    const int S = ccs_scale; const char *e = s + len; int64_t pen = (int64_t)(x * 64 + (x >= 0 ? 0.5 : -0.5)); int ls = (int)(spacing * 64 + 0.5), Y = y * S;
    rect cl = { clip.x0 * S, clip.y0 * S, clip.x1 * S, clip.y1 * S };
    while (s < e && *s) {
        uint32_t cp = utf8_next(&s); const glyph *g = find(f, cp); if (!g) continue;
        const glyph *hg = f->hi ? find(f->hi, cp) : NULL;
        if (hg) {
            int gx = (int)((pen * S + 32) >> 6) + hg->x0, gy = Y + hg->y0; const uint8_t *src = f->hi->bits + hg->off;
            for (int r = 0; r < hg->h; r++) { int yy = gy + r; if (yy < cl.y0 || yy >= cl.y1) continue; int sh = shear > 0 ? (int)(shear * (Y - yy) + (Y - yy >= 0 ? 0.5 : -0.5)) : 0;
                for (int q = 0; q < hg->w; q++) { int v = src[r * hg->w + q]; if (!v) continue; int xx = gx + q + sh; if (xx < cl.x0 || xx >= cl.x1) continue; put(fb, m, xx, yy, v, c); } }
        } else {
            int gx = (int)((pen + 32) >> 6) + g->x0, gy = y + g->y0; const uint8_t *src = f->bits + g->off;
            for (int r = 0; r < g->h; r++) for (int q = 0; q < g->w; q++) { int v = src[r * g->w + q]; if (!v) continue;
                for (int j = 0; j < S; j++) { int yy = (gy + r) * S + j; if (yy < cl.y0 || yy >= cl.y1) continue; int sh = shear > 0 ? (int)(shear * (Y - yy) + (Y - yy >= 0 ? 0.5 : -0.5)) : 0;
                    for (int i = 0; i < S; i++) { int xx = (gx + q) * S + i + sh; if (xx < cl.x0 || xx >= cl.x1) continue; put(fb, m, xx, yy, v, c); } } }
        }
        pen += g->adv + ls;
    }
    return (int)((pen + 32) >> 6);
}
int text_draw(framebuffer *fb, mask *m, rect clip, const font *f, double x, int y, const char *s, size_t len, color c, double spacing, double shear) {
    if (ccs_scale > 1) return text_draw_scaled(fb, m, clip, f, x, y, s, len, c, spacing, shear);
    const char *s0 = s, *e = s + len; int64_t pen = (int64_t)(x * 64 + (x >= 0 ? 0.5 : -0.5)); int ls = (int)(spacing * 64 + 0.5);
    if (ccs_text_off) { while (s < e && *s) { const glyph *g = find(f, utf8_next(&s)); if (g) pen += g->adv + ls; } return (int)((pen + 32) >> 6); }
    int probe = ccs_text_bg && ccs_text_rec && !m && fb && fb->w == CCS_FB_W && fb->h == CCS_FB_H && c.a >= 128 && c.rgb != 0;
    uint16_t hist[64]; int npx = 0, bx0 = 1 << 20, by0 = 1 << 20, bx1 = -1, by1 = -1; if (probe) memset(hist, 0, sizeof hist);
    while (s < e && *s) {
        const glyph *g = find(f, utf8_next(&s)); if (!g) continue;
        if (probe) { int gx = (int)((pen + 32) >> 6) + g->x0, gy = y + g->y0; const uint8_t *src = f->bits + g->off;
            for (int r = 0; r < g->h; r++) { int yy = gy + r; if (yy < clip.y0 || yy >= clip.y1 || yy < 0 || yy >= CCS_FB_H) continue;
                for (int q = 0; q < g->w; q++) { int xx = gx + q; if (src[r * g->w + q] < 128 || xx < clip.x0 || xx >= clip.x1 || xx < 0 || xx >= CCS_FB_W) continue;
                    const uint8_t *bg = ccs_text_bg + ((size_t)yy * CCS_FB_W + xx) * 3; int k = ccs_contrast10(c.rgb, (uint32_t)bg[0] << 16 | (uint32_t)bg[1] << 8 | bg[2]); hist[k > 63 ? 63 : k]++; npx++;
                    if (xx < bx0) bx0 = xx; if (yy < by0) by0 = yy; if (xx > bx1) bx1 = xx; if (yy > by1) by1 = yy; } } }
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
    if (probe && npx) { int box[4] = { bx0, by0, bx1 + 1, by1 + 1 }; ccs_text_rec(s0, (size_t)(e - s0), f, c, box, hist, npx); }
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
