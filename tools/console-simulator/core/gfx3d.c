/* Perspective drawing for the Box Art Showcase boxes (N3): CSS 3D transforms done in software.
 *
 * A box face is drawn into a small RGB texture (framebuffer) with the ordinary 2D primitives, then mapped onto its projected
 * quad with a projective (homography) mapping: perspective-correct, bilinear sampled, optionally with a per-texture-row alpha
 * (the floor reflection's fade). Pure IEEE double arithmetic without libm, so frames stay bit-identical on every platform. */
#include "home_int.h"
#include "gfx3d.h"

/* the CSS perspective: eye at distance P in front of the stage, looking at the perspective origin (ox, oy) */
void g3_project(const g3cam *c, double X, double Y, double Z, double *sx, double *sy) {
    double s = c->p / (c->p - Z); *sx = c->ox + (X - c->ox) * s; *sy = c->oy + (Y - c->oy) * s;
}
/* a point given in a box element's own coordinates (x, y from its top-left, z toward the viewer), after rotateY(a) about the
 * element's center (CSS transform-origin 50% 50%) */
void g3_box_point(const g3box *b, double x, double y, double z, double *X, double *Y, double *Z) {
    double dx = b->left + x - b->cx; *X = b->cx + dx * b->cs + z * b->sn; *Z = -dx * b->sn + z * b->cs; *Y = b->top + y;
}
static double det2(double a, double b, double c, double d) { return a * d - b * c; }
/* maps the texture onto the screen quad P0 (tex 0,0), P1 (W,0), P2 (W,H), P3 (0,H); returns 0 when drawn, 1 when culled (the
 * face shows its back: CSS backface-visibility: hidden) */
int g3_quad(framebuffer *fb, rect clip, const framebuffer *tex, const double qx[4], const double qy[4], int opacity, const uint8_t *row_alpha, int cull) {
    double area = (qx[1] - qx[0]) * (qy[2] - qy[1]) - (qy[1] - qy[0]) * (qx[2] - qx[1]);
    if (cull && area <= 0) return 1;
    /* unit square -> quad (Heckbert) */
    double a, b, c = qx[0], d, e, f = qy[0], g, h;
    double dx1 = qx[1] - qx[2], dx2 = qx[3] - qx[2], dx3 = qx[0] - qx[1] + qx[2] - qx[3], dy1 = qy[1] - qy[2], dy2 = qy[3] - qy[2], dy3 = qy[0] - qy[1] + qy[2] - qy[3];
    if (dx3 == 0 && dy3 == 0) { g = h = 0; a = qx[1] - qx[0]; b = qx[2] - qx[1]; d = qy[1] - qy[0]; e = qy[2] - qy[1]; }
    else {
        double den = det2(dx1, dx2, dy1, dy2); if (den == 0) return 1;
        g = det2(dx3, dx2, dy3, dy2) / den; h = det2(dx1, dx3, dy1, dy3) / den;
        a = qx[1] - qx[0] + g * qx[1]; b = qx[3] - qx[0] + h * qx[3]; d = qy[1] - qy[0] + g * qy[1]; e = qy[3] - qy[0] + h * qy[3];
    }
    /* inverse (adjugate) */
    double A = e - f * h, B = c * h - b, C = b * f - c * e, D = f * g - d, E = a - c * g, F = c * d - a * f, G = d * h - e * g, H = b * g - a * h, I = a * e - b * d;
    double minx = qx[0], maxx = qx[0], miny = qy[0], maxy = qy[0];
    for (int i = 1; i < 4; i++) { if (qx[i] < minx) minx = qx[i]; if (qx[i] > maxx) maxx = qx[i]; if (qy[i] < miny) miny = qy[i]; if (qy[i] > maxy) maxy = qy[i]; }
    int x0 = (int)minx - 1, x1 = (int)maxx + 2, y0 = (int)miny - 1, y1 = (int)maxy + 2;
    rect r = rect_isect(clip, (rect){ 0, 0, fb->w, fb->h }); if (x0 < r.x0) x0 = r.x0; if (y0 < r.y0) y0 = r.y0; if (x1 > r.x1) x1 = r.x1; if (y1 > r.y1) y1 = r.y1;
    int TW = tex->w, TH = tex->h;
    for (int y = y0; y < y1; y++) {
        double py = y + 0.5;
        for (int x = x0; x < x1; x++) {
            double px = x + 0.5, w = G * px + H * py + I; if (w == 0) continue;
            double u = (A * px + B * py + C) / w, v = (D * px + E * py + F) / w;
            if (u < 0 || u >= 1 || v < 0 || v >= 1) continue;
            double tu = u * TW - 0.5, tv = v * TH - 0.5; int iu = (int)(tu < 0 ? -1 : tu), iv = (int)(tv < 0 ? -1 : tv);
            if (tu < 0) iu = -1; if (tv < 0) iv = -1;
            double fu = tu - iu, fv = tv - iv; int u0 = iu < 0 ? 0 : iu, v0 = iv < 0 ? 0 : iv, u1 = iu + 1 >= TW ? TW - 1 : iu + 1, v1 = iv + 1 >= TH ? TH - 1 : iv + 1;
            const uint8_t *p00 = tex->rgb + ((size_t)v0 * TW + u0) * 3, *p10 = tex->rgb + ((size_t)v0 * TW + u1) * 3, *p01 = tex->rgb + ((size_t)v1 * TW + u0) * 3, *p11 = tex->rgb + ((size_t)v1 * TW + u1) * 3;
            int wa = (int)((1 - fu) * (1 - fv) * 256 + 0.5), wb = (int)(fu * (1 - fv) * 256 + 0.5), wc = (int)((1 - fu) * fv * 256 + 0.5), wd = 256 - wa - wb - wc;
            int al = opacity; if (row_alpha) { int ty = (int)(v * TH); if (ty >= TH) ty = TH - 1; al = al * row_alpha[ty] / 255; }
            if (al <= 0) continue;
            uint8_t *q = fb->rgb + ((size_t)y * fb->w + x) * 3;
            for (int k = 0; k < 3; k++) { int s = (p00[k] * wa + p10[k] * wb + p01[k] * wc + p11[k] * wd + 128) >> 8; q[k] = (uint8_t)(q[k] + ((s - q[k]) * al + 127) / 255); }
        }
    }
    return 0;
}
/* an angled linear gradient over a texture (CSS linear-gradient(<deg>, ...)); dir = the unit direction as (dx, dy) */
void g3_angled_gradient(framebuffer *t, double dx, double dy, const stop *s, int n) {
    double half = (t->w * (dx < 0 ? -dx : dx) + t->h * (dy < 0 ? -dy : dy)) / 2, cx = t->w / 2.0, cy = t->h / 2.0;
    for (int y = 0; y < t->h; y++) for (int x = 0; x < t->w; x++) {
        double p = ((x + 0.5 - cx) * dx + (y + 0.5 - cy) * dy) / (2 * half) + 0.5; if (p < 0) p = 0; if (p > 1) p = 1;
        int i = 0; while (i < n - 1 && s[i + 1].pos < p) i++;
        double r0, g0, b0, a0; if (i >= n - 1) { r0 = (s[n - 1].rgb >> 16) & 255; g0 = (s[n - 1].rgb >> 8) & 255; b0 = s[n - 1].rgb & 255; a0 = s[n - 1].a; }
        else { double k = s[i + 1].pos > s[i].pos ? (p - s[i].pos) / (s[i + 1].pos - s[i].pos) : 0; if (p < s[i].pos) k = 0;
            r0 = ((s[i].rgb >> 16) & 255) * (1 - k) + ((s[i + 1].rgb >> 16) & 255) * k; g0 = ((s[i].rgb >> 8) & 255) * (1 - k) + ((s[i + 1].rgb >> 8) & 255) * k; b0 = (s[i].rgb & 255) * (1 - k) + (s[i + 1].rgb & 255) * k; a0 = s[i].a * (1 - k) + s[i + 1].a * k; }
        uint8_t *q = t->rgb + ((size_t)y * t->w + x) * 3; double cc[3] = { r0, g0, b0 };
        for (int c = 0; c < 3; c++) q[c] = (uint8_t)(q[c] + (cc[c] - q[c]) * a0 + 0.5);
    }
}
/* text read bottom to top (CSS writing-mode: vertical-rl + rotate(180deg)), centered in the box (x, y, w, h) of a texture */
void g3_vertical_text(framebuffer *t, const font *f, const char *s, int x, int y, int w, int h, color col, double sp) {
    char fit[300]; text_fit(f, s, h, sp, fit, sizeof fit); int tw = text_w(f, fit, strlen(fit), sp), th = f->ascent + f->descent; mask m;
    if (tw <= 0 || mask_alloc(&m, 0, 0, tw, th)) return;
    text_draw(NULL, &m, (rect){ 0, 0, tw, th }, f, 0, f->ascent, fit, strlen(fit), RGBA(0, 255), sp, 0);
    int ox = x + (w - th) / 2, oy = y + (h - tw) / 2;   /* rotated -90 degrees: mask (mx, my) -> texture (ox + my, oy + tw - 1 - mx) */
    for (int my = 0; my < th; my++) for (int mx = 0; mx < tw; mx++) {
        int a = m.a[(size_t)my * tw + mx]; if (!a) continue; int tx = ox + my, ty = oy + tw - 1 - mx; if (tx < 0 || ty < 0 || tx >= t->w || ty >= t->h) continue;
        a = a * col.a / 255; uint8_t *q = t->rgb + ((size_t)ty * t->w + tx) * 3; uint8_t cc[3] = { (uint8_t)(col.rgb >> 16), (uint8_t)(col.rgb >> 8), (uint8_t)col.rgb };
        for (int c = 0; c < 3; c++) q[c] = (uint8_t)(q[c] + ((cc[c] - q[c]) * a + 127) / 255);
    }
    mask_free(&m);
}
