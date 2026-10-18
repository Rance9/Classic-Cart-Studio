/* Images and drawing primitives for the native HOME on the software framebuffer. Integer and plain IEEE arithmetic only
 * (no libm), so frames are bit-identical on Windows, Linux and ARM. Image decoding: the vendored stb_image (public domain /
 * MIT, v2.30, third_party/stb_image.h), built for baseline JPEG and PNG only, scalar code paths, no stdio. */
#include "home.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#if defined(__GNUC__) || defined(__clang__)
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-Wunused-function"
#pragma GCC diagnostic ignored "-Wsign-compare"
#pragma GCC diagnostic ignored "-Wimplicit-fallthrough"
#pragma GCC diagnostic ignored "-Wmissing-field-initializers"
#pragma GCC diagnostic ignored "-Wunused-but-set-variable"
#endif
#define STB_IMAGE_IMPLEMENTATION
#define STBI_ONLY_JPEG
#define STBI_ONLY_PNG
#define STBI_NO_SIMD
#define STBI_NO_STDIO
#define STBI_NO_LINEAR
#define STBI_NO_HDR
#define STBI_NO_FAILURE_STRINGS_X
#include "third_party/stb_image.h"
#if defined(__GNUC__) || defined(__clang__)
#pragma GCC diagnostic pop
#endif

static int iround(double v) { return v >= 0 ? (int)(v + 0.5) : -(int)(-v + 0.5); }
static int ifloor(double v) { int i = (int)v; return (v < 0 && (double)i != v) ? i - 1 : i; }
static uint8_t clamp8(int v) { return (uint8_t)(v < 0 ? 0 : v > 255 ? 255 : v); }

int ccs_read_file(const char *path, uint8_t **buf, size_t *len, size_t max_len) {
    *buf = NULL; *len = 0; FILE *f = fopen(path, "rb"); if (!f) return -1;
    size_t cap = 1 << 16, n = 0; uint8_t *b = malloc(cap); if (!b) { fclose(f); return -2; }
    for (;;) {
        if (n == cap) { if (cap >= max_len) { free(b); fclose(f); return -3; } cap *= 2; uint8_t *nb = realloc(b, cap); if (!nb) { free(b); fclose(f); return -2; } b = nb; }
        size_t r = fread(b + n, 1, cap - n, f); n += r; if (r == 0) break;
    }
    fclose(f); if (n > max_len) { free(b); return -3; }
    *buf = b; *len = n; return 0;
}

int img_alloc(image *im, int w, int h, int ch) { im->w = w; im->h = h; im->ch = ch; im->px = calloc((size_t)w * h * ch, 1); return im->px ? 0 : -1; }
void img_free(image *im) { free(im->px); im->px = NULL; im->w = im->h = 0; }

int img_decode(const uint8_t *buf, size_t len, image *im, int want_alpha, char *err, size_t errlen) {
    memset(im, 0, sizeof *im); int w, h, n; uint8_t *px = stbi_load_from_memory(buf, (int)len, &w, &h, &n, want_alpha ? 4 : 3);
    if (!px) { snprintf(err, errlen, "cannot decode image (%s)", stbi_failure_reason()); return -2; }
    im->w = w; im->h = h; im->ch = want_alpha ? 4 : 3; im->px = px; return 0;
}
int img_load(const char *path, image *im, int want_alpha, char *err, size_t errlen) {
    memset(im, 0, sizeof *im); uint8_t *buf; size_t len;
    int rc = ccs_read_file(path, &buf, &len, 32u << 20);
    if (rc) { snprintf(err, errlen, "cannot read %s", path); return -1; }
    rc = img_decode(buf, len, im, want_alpha, err, errlen); free(buf);
    if (rc) { size_t k = strlen(err); snprintf(err + k, errlen > k ? errlen - k : 0, ": %s", path); }
    return rc;
}

/* ---------- resampling: separable, fixed point (weights sum to 16384), premultiplied alpha ---------- */
typedef struct { int first, n; int32_t w[64]; } wspan;
static wspan *weights(int src0, int srcn, int dst) {
    wspan *ws = calloc((size_t)dst, sizeof *ws); if (!ws) return NULL;
    double scale = (double)srcn / dst;
    for (int i = 0; i < dst; i++) {
        double acc[64]; int first, n = 0; double tot = 0;
        if (scale >= 1.0) {   /* area average: each source pixel weighted by its overlap with the output pixel */
            double a = i * scale, b = (i + 1) * scale; first = ifloor(a); int last = ifloor(b - 1e-9); if (last >= srcn) last = srcn - 1;
            for (int j = first; j <= last && n < 64; j++) { double lo = j > a ? j : a, hi = j + 1 < b ? j + 1 : b; acc[n++] = hi - lo; tot += hi - lo; }
        } else {              /* bilinear */
            double c = (i + 0.5) * scale - 0.5; int j0 = ifloor(c); double t = c - j0; first = j0;
            acc[n++] = 1 - t; acc[n++] = t; tot = 1;
        }
        int32_t sum = 0, best = 0; ws[i].first = first; ws[i].n = n;
        for (int k = 0; k < n; k++) { ws[i].w[k] = iround(acc[k] / tot * 16384); sum += ws[i].w[k]; if (ws[i].w[k] > ws[i].w[best]) best = k; }
        ws[i].w[best] += 16384 - sum;
        (void)src0;
    }
    return ws;
}
static int clampi(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

static int scale_rect(const image *src, int sx, int sy, int sw, int sh, image *dst, int dw, int dh) {
    if (dw <= 0 || dh <= 0 || sw <= 0 || sh <= 0) return -1;
    int ch = src->ch; if (img_alloc(dst, dw, dh, ch)) return -1;
    const uint8_t *sp = src->px; uint8_t *pm = NULL;
    if (ch == 4) {   /* premultiply so transparent pixels never bleed their (meaningless) color */
        pm = malloc((size_t)src->w * src->h * 4); if (!pm) { img_free(dst); return -1; }
        for (size_t i = 0, n = (size_t)src->w * src->h; i < n; i++) { const uint8_t *p = src->px + i * 4; int a = p[3]; uint8_t *q = pm + i * 4; q[0] = (uint8_t)((p[0] * a + 127) / 255); q[1] = (uint8_t)((p[1] * a + 127) / 255); q[2] = (uint8_t)((p[2] * a + 127) / 255); q[3] = (uint8_t)a; }
        sp = pm;
    }
    wspan *wx = weights(sx, sw, dw), *wy = weights(sy, sh, dh); uint16_t *tmp = malloc((size_t)dw * sh * ch * sizeof *tmp);
    if (!wx || !wy || !tmp) { free(wx); free(wy); free(tmp); free(pm); img_free(dst); return -1; }
    for (int y = 0; y < sh; y++) {
        const uint8_t *row = sp + (size_t)(sy + y) * src->w * ch;
        for (int x = 0; x < dw; x++) for (int c = 0; c < ch; c++) {
            int32_t s = 0; for (int k = 0; k < wx[x].n; k++) { int j = clampi(sx + wx[x].first + k, sx, sx + sw - 1); s += wx[x].w[k] * row[j * ch + c]; }
            tmp[((size_t)y * dw + x) * ch + c] = (uint16_t)clampi((s + 32) >> 6, 0, 65535);
        }
    }
    for (int y = 0; y < dh; y++) for (int x = 0; x < dw; x++) for (int c = 0; c < ch; c++) {
        int32_t s = 0; for (int k = 0; k < wy[y].n; k++) { int j = clampi(wy[y].first + k, 0, sh - 1); s += wy[y].w[k] * tmp[((size_t)j * dw + x) * ch + c]; }
        dst->px[((size_t)y * dw + x) * ch + c] = clamp8((s + (1 << 21)) >> 22);
    }
    if (ch == 4) for (size_t i = 0, n = (size_t)dw * dh; i < n; i++) { uint8_t *q = dst->px + i * 4; int a = q[3]; if (a) { q[0] = clamp8((q[0] * 255 + a / 2) / a); q[1] = clamp8((q[1] * 255 + a / 2) / a); q[2] = clamp8((q[2] * 255 + a / 2) / a); } }
    free(wx); free(wy); free(tmp); free(pm); return 0;
}
int img_scale(const image *src, image *dst, int dw, int dh) { return scale_rect(src, 0, 0, src->w, src->h, dst, dw, dh); }
int img_scale_nearest(const image *src, image *dst, int dw, int dh) {
    if (img_alloc(dst, dw, dh, src->ch)) return -1;
    for (int y = 0; y < dh; y++) { int sy = (int)(((int64_t)y * src->h * 2 + src->h) / (2 * dh)); for (int x = 0; x < dw; x++) { int sx = (int)(((int64_t)x * src->w * 2 + src->w) / (2 * dw)); memcpy(dst->px + ((size_t)y * dw + x) * src->ch, src->px + ((size_t)sy * src->w + sx) * src->ch, (size_t)src->ch); } }
    return 0;
}
int img_cover(const image *src, image *dst, int dw, int dh) {
    double s = (double)dw / src->w > (double)dh / src->h ? (double)dw / src->w : (double)dh / src->h;
    int cw = clampi(iround(dw / s), 1, src->w), chh = clampi(iround(dh / s), 1, src->h);
    return scale_rect(src, (src->w - cw) / 2, (src->h - chh) / 2, cw, chh, dst, dw, dh);
}
int img_contain(const image *src, image *dst, int dw, int dh, int *ox, int *oy) {
    double s = (double)dw / src->w < (double)dh / src->h ? (double)dw / src->w : (double)dh / src->h;
    int fw = clampi(iround(src->w * s), 1, dw), fh = clampi(iround(src->h * s), 1, dh);
    *ox = (dw - fw) / 2; *oy = (dh - fh) / 2; return img_scale(src, dst, fw, fh);
}

/* ---------- drawing ---------- */
rect rect_make(int x, int y, int w, int h) { rect r = { x, y, x + w, y + h }; return r; }
rect rect_isect(rect a, rect b) { rect r = { a.x0 > b.x0 ? a.x0 : b.x0, a.y0 > b.y0 ? a.y0 : b.y0, a.x1 < b.x1 ? a.x1 : b.x1, a.y1 < b.y1 ? a.y1 : b.y1 }; return r; }
static rect fb_clip(const framebuffer *fb, rect clip, int x, int y, int w, int h) { rect r = rect_isect(clip, rect_make(x, y, w, h)); return rect_isect(r, rect_make(0, 0, fb->w, fb->h)); }
static inline void blend(uint8_t *p, uint32_t rgb, int a) {
    if (a <= 0) return; if (a >= 255) { p[0] = (uint8_t)(rgb >> 16); p[1] = (uint8_t)(rgb >> 8); p[2] = (uint8_t)rgb; return; }
    p[0] = (uint8_t)((p[0] * (255 - a) + ((rgb >> 16) & 255) * a + 127) / 255); p[1] = (uint8_t)((p[1] * (255 - a) + ((rgb >> 8) & 255) * a + 127) / 255); p[2] = (uint8_t)((p[2] * (255 - a) + (rgb & 255) * a + 127) / 255);
}
void gfx_fill(framebuffer *fb, rect clip, int x, int y, int w, int h, color c) {
    rect r = fb_clip(fb, clip, x, y, w, h);
    for (int yy = r.y0; yy < r.y1; yy++) for (int xx = r.x0; xx < r.x1; xx++) blend(fb->rgb + ((size_t)yy * fb->w + xx) * 3, c.rgb, c.a);
}
void gfx_image(framebuffer *fb, rect clip, const image *im, int x, int y, int opacity) {
    if (!im || !im->px) return; rect r = fb_clip(fb, clip, x, y, im->w, im->h);
    for (int yy = r.y0; yy < r.y1; yy++) {
        const uint8_t *s = im->px + ((size_t)(yy - y) * im->w + (r.x0 - x)) * im->ch; uint8_t *d = fb->rgb + ((size_t)yy * fb->w + r.x0) * 3;
        if (im->ch == 3 && opacity >= 255) { memcpy(d, s, (size_t)(r.x1 - r.x0) * 3); continue; }
        for (int xx = r.x0; xx < r.x1; xx++, s += im->ch, d += 3) { int a = im->ch == 4 ? s[3] * opacity / 255 : opacity; blend(d, ((uint32_t)s[0] << 16) | ((uint32_t)s[1] << 8) | s[2], a); }
    }
}
/* rounded-rectangle coverage 0..16 at pixel (px,py): 4 x 4 samples in the corner squares, exact integer geometry (1/8 px) */
static int rr_cov(int px, int py, int x, int y, int w, int h, int r) {
    if (px < x || py < y || px >= x + w || py >= y + h) return 0;
    if (r <= 0) return 16;
    int cx, cy;
    if (px < x + r) cx = x + r; else if (px >= x + w - r) cx = x + w - r; else return 16;
    if (py < y + r) cy = y + r; else if (py >= y + h - r) cy = y + h - r; else return 16;
    int n = 0; int64_t R = (int64_t)(8 * r) * (8 * r);
    for (int j = 0; j < 4; j++) for (int i = 0; i < 4; i++) { int64_t dx = 8 * px + 2 * i + 1 - 8 * (int64_t)cx, dy = 8 * py + 2 * j + 1 - 8 * (int64_t)cy; if (dx * dx + dy * dy <= R) n++; }
    return n;
}
void gfx_rrect(framebuffer *fb, rect clip, int x, int y, int w, int h, int radius, color c) {
    rect r = fb_clip(fb, clip, x, y, w, h);
    for (int yy = r.y0; yy < r.y1; yy++) for (int xx = r.x0; xx < r.x1; xx++) { int cv = rr_cov(xx, yy, x, y, w, h, radius); if (cv) blend(fb->rgb + ((size_t)yy * fb->w + xx) * 3, c.rgb, c.a * cv / 16); }
}
void gfx_rrect_ring(framebuffer *fb, rect clip, int x, int y, int w, int h, int radius, int t, color c) {
    rect r = fb_clip(fb, clip, x, y, w, h); int ir = radius - t > 0 ? radius - t : 0;
    for (int yy = r.y0; yy < r.y1; yy++) for (int xx = r.x0; xx < r.x1; xx++) { int cv = rr_cov(xx, yy, x, y, w, h, radius) - rr_cov(xx, yy, x + t, y + t, w - 2 * t, h - 2 * t, ir); if (cv > 0) blend(fb->rgb + ((size_t)yy * fb->w + xx) * 3, c.rgb, c.a * cv / 16); }
}
void gfx_rrect_image(framebuffer *fb, rect clip, const image *im, int x, int y, int radius) {
    if (!im || !im->px) return; rect r = fb_clip(fb, clip, x, y, im->w, im->h);
    for (int yy = r.y0; yy < r.y1; yy++) for (int xx = r.x0; xx < r.x1; xx++) {
        int cv = rr_cov(xx, yy, x, y, im->w, im->h, radius); if (!cv) continue; const uint8_t *s = im->px + ((size_t)(yy - y) * im->w + (xx - x)) * im->ch;
        int a = (im->ch == 4 ? s[3] : 255) * cv / 16; blend(fb->rgb + ((size_t)yy * fb->w + xx) * 3, ((uint32_t)s[0] << 16) | ((uint32_t)s[1] << 8) | s[2], a);
    }
}

/* ---------- blurred rectangles: the normal CDF from an erf series (plain arithmetic, so every platform builds the same table) ---------- */
#define PHI_N 2048
static double PHI[PHI_N + 1]; static int phi_ready = 0;
static void phi_init(void) {
    for (int i = 0; i <= PHI_N; i++) {
        double z = -4.0 + 8.0 * i / PHI_N, x = z / 1.4142135623730951, ax = x < 0 ? -x : x, e;
        if (ax > 3.0) e = 1.0;
        else { double term = ax, sum = ax, x2 = ax * ax; for (int n = 1; n < 80; n++) { term = -term * x2 / n; double t = term / (2 * n + 1); sum += t; if (t < 1e-17 && t > -1e-17) break; } e = 1.1283791670955126 * sum; if (e > 1) e = 1; }
        PHI[i] = 0.5 * (1.0 + (x < 0 ? -e : e));
    }
    phi_ready = 1;
}
static double phi(double z) { if (z <= -4.0) return 0; if (z >= 4.0) return 1; double f = (z + 4.0) / 8.0 * PHI_N; int i = (int)f; if (i >= PHI_N) return 1; double t = f - i; return PHI[i] + (PHI[i + 1] - PHI[i]) * t; }
void gfx_shadow(framebuffer *fb, rect clip, int x, int y, int w, int h, double sigma, color c) {
    if (!phi_ready) phi_init(); int m = (int)(sigma * 3) + 1; rect r = fb_clip(fb, clip, x - m, y - m, w + 2 * m, h + 2 * m);
    int W = r.x1 - r.x0; if (W <= 0 || r.y1 <= r.y0) return; double *px = malloc(sizeof(double) * W); if (!px) return;
    for (int xx = r.x0; xx < r.x1; xx++) px[xx - r.x0] = sigma > 0 ? phi((xx + 0.5 - x) / sigma) - phi((xx + 0.5 - x - w) / sigma) : (xx >= x && xx < x + w);
    for (int yy = r.y0; yy < r.y1; yy++) {
        double py = sigma > 0 ? phi((yy + 0.5 - y) / sigma) - phi((yy + 0.5 - y - h) / sigma) : (yy >= y && yy < y + h);
        for (int xx = r.x0; xx < r.x1; xx++) { int a = iround(c.a * px[xx - r.x0] * py); if (a > 0) blend(fb->rgb + ((size_t)yy * fb->w + xx) * 3, c.rgb, a); }
    }
    free(px);
}
/* CSS linear-gradient over a box: stops interpolated in premultiplied space, composited over the framebuffer */
void gfx_gradient(framebuffer *fb, rect clip, int x, int y, int w, int h, int vertical, const stop *s, int n) {
    rect r = fb_clip(fb, clip, x, y, w, h); int len = vertical ? h : w; if (len <= 0) return;
    int lo = vertical ? r.y0 : r.x0, hi = vertical ? r.y1 : r.x1, base = vertical ? y : x;
    for (int i = lo; i < hi; i++) {
        double t = (i - base + 0.5) / len, a, cr, cg, cb; int k = 0;
        while (k < n - 1 && t > s[k + 1].pos) k++;
        if (t <= s[0].pos) { a = s[0].a; cr = ((s[0].rgb >> 16) & 255) * a; cg = ((s[0].rgb >> 8) & 255) * a; cb = (s[0].rgb & 255) * a; }
        else if (k >= n - 1) { a = s[n - 1].a; cr = ((s[n - 1].rgb >> 16) & 255) * a; cg = ((s[n - 1].rgb >> 8) & 255) * a; cb = (s[n - 1].rgb & 255) * a; }
        else {
            double span_ = s[k + 1].pos - s[k].pos, u = span_ > 0 ? (t - s[k].pos) / span_ : 1, a0 = s[k].a, a1 = s[k + 1].a;
            a = a0 + (a1 - a0) * u;
            cr = ((s[k].rgb >> 16) & 255) * a0 + ((((s[k + 1].rgb >> 16) & 255) * a1) - ((s[k].rgb >> 16) & 255) * a0) * u;
            cg = ((s[k].rgb >> 8) & 255) * a0 + ((((s[k + 1].rgb >> 8) & 255) * a1) - ((s[k].rgb >> 8) & 255) * a0) * u;
            cb = (s[k].rgb & 255) * a0 + (((s[k + 1].rgb & 255) * a1) - (s[k].rgb & 255) * a0) * u;
        }
        int a8 = iround(a * 255); if (a8 <= 0) continue; int pr = iround(cr), pg = iround(cg), pb = iround(cb);
        for (int j = vertical ? r.x0 : r.y0; j < (vertical ? r.x1 : r.y1); j++) {
            uint8_t *p = fb->rgb + ((size_t)(vertical ? i : j) * fb->w + (vertical ? j : i)) * 3;
            p[0] = clamp8((p[0] * (255 - a8) + pr * 255 + 127) / 255); p[1] = clamp8((p[1] * (255 - a8) + pg * 255 + 127) / 255); p[2] = clamp8((p[2] * (255 - a8) + pb * 255 + 127) / 255);
        }
    }
}

/* ---------- coverage masks (text glows) ---------- */
int mask_alloc(mask *m, int x, int y, int w, int h) { m->x = x; m->y = y; m->w = w; m->h = h; m->a = calloc((size_t)w * h, 1); return m->a ? 0 : -1; }
void mask_free(mask *m) { free(m->a); m->a = NULL; }
static void box_pass(uint8_t *a, int n, int stride, int r, uint8_t *tmp) {
    int win = 2 * r + 1; int32_t s = 0;
    for (int i = 0; i < n; i++) tmp[i] = a[i * stride];
    for (int i = -r; i <= r; i++) if (i >= 0 && i < n) s += tmp[i];
    for (int i = 0; i < n; i++) { a[i * stride] = (uint8_t)((s + win / 2) / win); int out = i - r, in = i + r + 1; if (out >= 0) s -= tmp[out]; if (in < n) s += tmp[in]; }
}
void mask_blur(mask *m, double sigma) {   /* three box passes approximate a Gaussian: r(r+1) ~ sigma^2 */
    int r = 0; while ((double)(r + 1) * (r + 2) <= sigma * sigma) r++; if (r < 1) r = 1;
    uint8_t *tmp = malloc((size_t)(m->w > m->h ? m->w : m->h)); if (!tmp) return;
    for (int p = 0; p < 3; p++) { for (int y = 0; y < m->h; y++) box_pass(m->a + (size_t)y * m->w, m->w, 1, r, tmp); for (int x = 0; x < m->w; x++) box_pass(m->a + x, m->h, m->w, r, tmp); }
    free(tmp);
}
void gfx_mask(framebuffer *fb, rect clip, const mask *m, color c) {
    rect r = fb_clip(fb, clip, m->x, m->y, m->w, m->h);
    for (int yy = r.y0; yy < r.y1; yy++) for (int xx = r.x0; xx < r.x1; xx++) { int v = m->a[(size_t)(yy - m->y) * m->w + (xx - m->x)]; if (v) blend(fb->rgb + ((size_t)yy * fb->w + xx) * 3, c.rgb, c.a * v / 255); }
}
