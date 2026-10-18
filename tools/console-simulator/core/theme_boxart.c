/* Box Art Showcase (N3): moonlit 3-D boxes, one game center stage; the top navigation, the title, the overview preview, the
 * facts row and the collection panel. home-themes.js hmBoxArt / hmBox3 + home.css .hm-box-art-showcase, geometry measured on
 * the master (home-art.json box-art-showcase: the eight box envelopes, the text boxes).
 *
 * The boxes are real 3-D: each face (front with the cover, the lit spine with the title, top and bottom) is drawn into a small
 * texture and mapped through the CSS perspective (1500 px, origin 50 % 38 %) after the box's rotateY (9 degrees for the focus
 * box, 26 for the others). Every spine shows on the box's left side, as on the approved master, and the reflection lies on the
 * floor below each box. The front takes the cover's own proportions (height from the envelope, width from the art), so the
 * whole cover shows and nothing is cut. Navigation: Left / Right through the carousel (no wrap), Up into the top navigation. */
#include "home_int.h"
#include "gfx3d.h"

#define COS9 0.98768834059513777
#define SIN9 0.15643446504023087
#define COS26 0.89879404629916704
#define SIN26 0.43837114678907740
static const char *NAVL[6] = { "GAMES", "BROWSE", "SEARCH", "FAVORITES", "RECENT", "SETTINGS" };
static const char *NAV_NAME[6] = { "All Games", "Browse", "Search", "Favorites", "Recently Played", "Settings" };

typedef struct { int d; box4 b; } envelope;
typedef struct {
    box4 nav, dots, title, blurb, rule, facts, pheading, prows[4], arrow_l, arrow_r, legend; int picon_x; double spine_ratio;
    envelope env[12]; int nenv;
    framebuffer frame; long key; int counts[3];
} boxart;
#define BX(h) ((boxart *)(h)->tstate)

static double dsqrt(double x) { if (x <= 0) return 0; double r = x > 1 ? x : 1; for (int i = 0; i < 40; i++) r = 0.5 * (r + x / r); return r; }

static int ba_init(home *h, char *err, size_t errlen) {
    boxart *a = calloc(1, sizeof *a); if (!a) { snprintf(err, errlen, "out of memory"); return -1; } h->tstate = a; a->key = -1;
    const jval *g = h->geo, *p = json_get(g, "panel"), *envs = json_get(g, "envelopes");
    if (gbox(g, "nav", &a->nav) || gbox(g, "dots", &a->dots) || gbox(g, "title", &a->title) || gbox(g, "blurb", &a->blurb) || gbox(g, "rule", &a->rule) || gbox(g, "facts", &a->facts)
        || gbox(g, "arrow_l", &a->arrow_l) || gbox(g, "arrow_r", &a->arrow_r) || !p || gbox(p, "heading", &a->pheading) || !envs || envs->t != J_ARR) { snprintf(err, errlen, "box-art-showcase geometry incomplete"); return -1; }
    const jval *rows = json_get(p, "rows"); for (int i = 0; i < 4; i++) if (jbox_at(rows, (size_t)i, &a->prows[i])) { snprintf(err, errlen, "box-art panel rows missing"); return -1; }
    a->picon_x = gnum(p, "icon_x", 706); const jval *sr = json_get(g, "spine_ratio"); a->spine_ratio = sr && sr->t == J_NUM ? sr->num : 0.1184;
    for (size_t i = 0; i < envs->n && a->nenv < 12; i++) { const jval *e = envs->items[i]; envelope *v = &a->env[a->nenv]; v->d = gnum(e, "d", 0); if (gbox(e, "box", &v->b)) continue; a->nenv++; }
    a->legend = (box4){ 200, 680, 1080, 712 };
    const jval *c = json_get(h->pk, "counts"); a->counts[0] = (int)h->nall; a->counts[1] = gnum(c, "folders", 0); a->counts[2] = gnum(c, "favorites", 0);
    if (fb_init(&a->frame, CCS_FB_W, CCS_FB_H)) { snprintf(err, errlen, "out of memory"); return -1; }
    return 0;
}
static void ba_fini(home *h) { boxart *a = BX(h); if (!a) return; if (a->frame.rgb) fb_free(&a->frame); free(a); h->tstate = NULL; }

/* ---------- one box ---------- */
static void tex_gradient_h(framebuffer *t, const stop *s, int n) { gfx_gradient(t, FULL, 0, 0, t->w, t->h, 0, s, n); }
static void scaled_sprite_at(framebuffer *t, const home *h, const char *name, double x, double ybottom, double w) {   /* width w, aspect kept, bottom at ybottom */
    const tsprite *s = tsp(h, name); if (!s || w < 2) return; int iw = iround(w), ih = iround(w * s->im.h / (double)s->im.w); image sc; if (ih < 1) return;
    if (!img_scale(&s->im, &sc, iw, ih)) { gfx_image(t, FULL, &sc, iround(x), iround(ybottom - ih), 255); img_free(&sc); }
}
static int build_front(home *h, int gi, int fw, int fh, int center, int left_spine, framebuffer *t) {
    if (fb_init(t, fw, fh)) return -1; fb_clear(t, 0x0D, 0x0D, 0x11);
    int ox, oy; const image *art = cover(h, gi, fw - 10, fh - 10, 0, &ox, &oy); gfx_image(t, FULL, art, 5, 5, 255);   /* the whole cover: the face has its proportions */
    { int tw = iround(fw * 0.28), tx = fw - tw + 2; gfx_shadow(t, FULL, tx, 11, tw, 7, 5, RGBA(RED, 178)); gfx_fill(t, FULL, tx, 11, tw, 7, RGBA(RED, 255)); }   /* .tab */
    { stop cr[2] = { { 0, 0xFFFFFF, .28 }, { 1, 0xFFFFFF, 0 } }; gfx_gradient(t, FULL, 0, 0, 4, fh, 0, cr, 2); }   /* .crease */
    { stop gl[6] = { { 0, 0xFFFFFF, .26 }, { .14, 0xFFFFFF, .10 }, { .32, 0xFFFFFF, 0 }, { .64, 0xFFFFFF, 0 }, { .84, 0xFFFFFF, .07 }, { 1, 0xFFFFFF, .16 } }; g3_angled_gradient(t, 0.97029572627599647, 0.24192189559966773, gl, 6); }   /* .gloss 104deg */
    if (center) { stop sh[2] = { { .70, 0, 0 }, { 1, 0, .18 } }; tex_gradient_h(t, sh, 2); }   /* .shade (focus box) */
    else if (left_spine) { stop sh[2] = { { 0, 0, .35 }, { .45, 0, 0 } }; tex_gradient_h(t, sh, 2); }   /* .lit-l: the spine side darker */
    else { stop sh[2] = { { .55, 0, 0 }, { 1, 0, .35 } }; tex_gradient_h(t, sh, 2); }                    /* .lit-r */
    scaled_sprite_at(t, h, "logo-mark", fw * 0.06, fh - fh * 0.05, fw * 0.14);
    scaled_sprite_at(t, h, "seal", fw - fw * 0.06 - fw * 0.13, fh - fh * 0.16, fw * 0.13);
    scaled_sprite_at(t, h, "badge-brand", fw - fw * 0.08 - fw * 0.30, fh - fh * 0.05, fw * 0.30);
    gfx_fill(t, FULL, 2, 2, 2, fh - 4, RGBA(0xFFFFFF, 36)); gfx_fill(t, FULL, fw - 4, 2, 2, fh - 4, RGBA(0, 128));   /* inset 2px lit left edge, dark right edge */
    gfx_rrect_ring(t, FULL, 2, 2, fw - 4, fh - 4, 0, 1, RGBA(0xFFFFFF, center ? 31 : 23)); gfx_rrect_ring(t, FULL, 0, 0, fw, fh, 3, 2, RGBA(0x050507, 255));
    return 0;
}
static int build_spine(home *h, int gi, int sw, int fh, int left_spine, framebuffer *t) {
    if (fb_init(t, sw, fh)) return -1;
    stop sg[4] = { { 0, 0x1C1C22, 1 }, { .30, 0x34343D, 1 }, { .62, 0x4A4A55, 1 }, { 1, 0x2B2B33, 1 } }, sr[4] = { { 0, 0x2B2B33, 1 }, { .38, 0x4A4A55, 1 }, { .70, 0x34343D, 1 }, { 1, 0x1C1C22, 1 } };
    tex_gradient_h(t, left_spine ? sg : sr, 4);   /* .spine 90deg; .spine.r 270deg */
    gfx_fill(t, FULL, 0, 0, sw, iround(fh * 0.05), RGBA(RED, 255));   /* .band */
    int icw = iround(sw * 0.64), ich = iround(icw * 1.6), icx = iround(sw * 0.18);
    int ys[2] = { iround(fh * 0.05), fh - iround(fh * 0.04) - ich };
    for (int k = 0; k < 2; k++) { gfx_rrect(t, FULL, icx, ys[k], icw, ich, 3, RGBA(0x2A2A30, 255)); gfx_rrect_ring(t, FULL, icx, ys[k], icw, ich, 3, 2, RGBA(0xD9D9DD, 255)); }
    int px = iround(sw * 0.46); if (px < 8) px = 8; int fid = px >= 16 ? F_CIN20 : px >= 10 ? F_CIN11 : F_CIN8;
    g3_vertical_text(t, &h->f[fid], h->g[gi].title, 0, iround(fh * 0.14), sw, iround(fh * 0.70), RGBA(0xF3EFE6, 255), 1);
    stop sh[4] = { { 0, 0xFFFFFF, .18 }, { .30, 0xFFFFFF, 0 }, { .75, 0xFFFFFF, 0 }, { 1, 0xFFFFFF, .08 } }; gfx_gradient(t, FULL, 0, 0, sw, fh, 1, sh, 4);
    gfx_rrect_ring(t, FULL, 2, 2, sw - 4, fh - 4, 0, 1, RGBA(0xFFFFFF, 18)); gfx_rrect_ring(t, FULL, 0, 0, sw, fh, 0, 2, RGBA(0x050507, 255));
    return 0;
}
static void face(framebuffer *fb, const g3cam *cam, const g3box *bx, const framebuffer *tex, const double P[4][3], int opacity, const uint8_t *rows, int cull) {
    double qx[4], qy[4]; for (int i = 0; i < 4; i++) { double X, Y, Z; g3_box_point(bx, P[i][0], P[i][1], P[i][2], &X, &Y, &Z); g3_project(cam, X, Y, Z, &qx[i], &qy[i]); }
    g3_quad(fb, FULL, tex, qx, qy, opacity, rows, cull);
}
static void floor_shadow(framebuffer *fb, double cx, double cy, double rx, double ry) {   /* .shadow: radial-gradient(ellipse at center, .95, .5 40%, 0 72%) */
    int x0 = (int)(cx - rx * 1.5), x1 = (int)(cx + rx * 1.5) + 1, y0 = (int)(cy - ry * 1.5), y1 = (int)(cy + ry * 1.5) + 1;
    for (int y = y0 < 0 ? 0 : y0; y < y1 && y < fb->h; y++) for (int x = x0 < 0 ? 0 : x0; x < x1 && x < fb->w; x++) {
        double dx = (x + 0.5 - cx) / (rx * 1.41421356), dy = (y + 0.5 - cy) / (ry * 1.41421356), t = dsqrt(dx * dx + dy * dy), a;
        if (t >= .72) continue; a = t < .4 ? .95 - (.95 - .5) * t / .4 : .5 - .5 * (t - .4) / .32;
        uint8_t *q = fb->rgb + ((size_t)y * fb->w + x) * 3; for (int c = 0; c < 3; c++) q[c] = (uint8_t)(q[c] * (1 - a) + 0.5);
    }
}
static void draw_box(home *h, framebuffer *fb, const envelope *e, int gi, int center) {
    boxart *a = BX(h); g3cam cam = { 1500, 640, 720 * 0.38 };
    int w = e->b.x1 - e->b.x0, fh = e->b.y1 - e->b.y0, spine = iround(w * a->spine_ratio * 1.25); if (spine < 10) spine = 10;
    /* hmBox3: the focus box stands at 9 degrees, boxes right of it at 26 show their left spine, boxes left of it turn the other
     * way (-26) and show their right spine, so every visible spine faces the viewer */
    int left_spine = e->d >= 0; double cs = center ? COS9 : COS26, sn = center ? SIN9 : SIN26;
    int fw0 = iround((w - spine * sn) / cs); double cx = left_spine ? e->b.x0 + spine * sn + fw0 * cs / 2 : e->b.x0 + fw0 * cs / 2;
    int ox, oy; const image *probe = cover(h, gi, 340, 466, 1, &ox, &oy); double aspect = probe->w / (double)probe->h;
    int fw = iround((fh - 10) * aspect) + 10; if (fw > fw0 * 1.25) fw = iround(fw0 * 1.25); if (fw < 24) fw = 24;
    g3box bx = { iround(cx - fw / 2.0), e->b.y0, 0, cs, left_spine ? sn : -sn }; bx.cx = bx.left + fw / 2.0;
    framebuffer front, sp; if (build_front(h, gi, fw, fh, center, left_spine, &front)) return; if (build_spine(h, gi, spine, fh, left_spine, &sp)) { fb_free(&front); return; }
    /* floor: the soft shadow under the box, then the reflection (the front's lower part mirrored, fading) */
    { double X, Y, Z, sx0, sy0, sx1, sy1; g3_box_point(&bx, -0.06 * fw, fh + 1, 0, &X, &Y, &Z); g3_project(&cam, X, Y, Z, &sx0, &sy0); g3_box_point(&bx, 1.06 * fw, fh + 1, 0, &X, &Y, &Z); g3_project(&cam, X, Y, Z, &sx1, &sy1);
      floor_shadow(fb, (sx0 + sx1) / 2, (sy0 + sy1) / 2, (sx1 - sx0) / 2, 11); }
    int rh = iround(fh * 0.46); framebuffer refl;
    if (!fb_init(&refl, fw, rh)) {
        for (int y = 0; y < rh; y++) memcpy(refl.rgb + (size_t)y * fw * 3, front.rgb + (size_t)(fh - 1 - y) * fw * 3, (size_t)fw * 3);
        uint8_t ra[1024]; for (int y = 0; y < rh && y < 1024; y++) ra[y] = (uint8_t)(255 * (0.95 - 0.95 * y / (double)rh) + 0.5);
        double P[4][3] = { { 0, fh, 0 }, { fw, fh, 0 }, { fw, fh + rh, 0 }, { 0, fh + rh, 0 } }; face(fb, &cam, &bx, &refl, P, 87, ra, 0);
        fb_free(&refl);
    }
    /* the box: its drop shadow (and the focus box's red glow), the spine (left side, receding), then the front */
    { double X, Y, Z, x0, y0, x1, y1; g3_box_point(&bx, 0, 0, 0, &X, &Y, &Z); g3_project(&cam, X, Y, Z, &x0, &y0); g3_box_point(&bx, fw, fh, 0, &X, &Y, &Z); g3_project(&cam, X, Y, Z, &x1, &y1);
      if (center) { gfx_shadow(fb, FULL, iround(x0), iround(y0), iround(x1 - x0), iround(y1 - y0), 22, RGBA(RED, 89)); gfx_shadow(fb, FULL, iround(x0), iround(y0) + 26, iround(x1 - x0), iround(y1 - y0), 30, RGBA(0, 191)); }
      else gfx_shadow(fb, FULL, iround(x0), iround(y0) + 22, iround(x1 - x0), iround(y1 - y0), 23, RGBA(0, 166));
      LR(h, center ? "box-focus" : "box", iround(x0), iround(y0), iround(x1), iround(y1)); }
    double PS[4][3] = { { 0, 0, -spine }, { 0, 0, 0 }, { 0, fh, 0 }, { 0, fh, -spine } }, PR[4][3] = { { fw, 0, 0 }, { fw, 0, -spine }, { fw, fh, -spine }, { fw, fh, 0 } };
    face(fb, &cam, &bx, &sp, left_spine ? PS : PR, 255, NULL, 1);
    framebuffer tb; if (!fb_init(&tb, fw, spine)) { stop tg[2] = { { 0, 0x4A4A55, 1 }, { 1, 0x2A2A32, 1 } }; gfx_gradient(&tb, FULL, 0, 0, fw, spine, 1, tg, 2); gfx_rrect_ring(&tb, FULL, 0, 0, fw, spine, 0, 2, RGBA(0x050507, 255));
        double PT[4][3] = { { 0, 0, -spine }, { fw, 0, -spine }, { fw, 0, 0 }, { 0, 0, 0 } }; face(fb, &cam, &bx, &tb, PT, 255, NULL, 1);
        fb_clear(&tb, 0x0A, 0x0A, 0x0D); gfx_rrect_ring(&tb, FULL, 0, 0, fw, spine, 0, 2, RGBA(0x050507, 255));
        double PB[4][3] = { { 0, fh, 0 }, { fw, fh, 0 }, { fw, fh, -spine }, { 0, fh, -spine } }; face(fb, &cam, &bx, &tb, PB, 255, NULL, 1); fb_free(&tb); }
    double PF[4][3] = { { 0, 0, 0 }, { fw, 0, 0 }, { fw, fh, 0 }, { 0, fh, 0 } }; face(fb, &cam, &bx, &front, PF, 255, NULL, 0);
    fb_free(&front); fb_free(&sp);
}

/* ---------- the page ---------- */
static void meta_list(const hgame *g, const char **k, const char **v, const char **ic, int *n) {
    *n = 0;
    if (g->year) { k[*n] = "YEAR"; v[*n] = g->year; ic[(*n)++] = "year"; } if (g->players) { k[*n] = "PLAYERS"; v[*n] = g->players; ic[(*n)++] = "players"; }
    if (g->genre) { k[*n] = "GENRE"; v[*n] = g->genre; ic[(*n)++] = "genre"; } if (g->publisher && *n < 4) { k[*n] = "PUBLISHER"; v[*n] = g->publisher; ic[(*n)++] = "publisher"; }
}
static void ba_draw(home *h, framebuffer *fb) {
    boxart *a = BX(h); memcpy(fb->rgb, h->scene.px, (size_t)fb->w * fb->h * 3);
    /* top navigation (flex, space-between, bottom aligned): GAMES active (red rule), the focused one glowing */
    { const font *f = &h->f[F_S400_11]; int wsum = 0, ws[6]; for (int i = 0; i < 6; i++) { ws[i] = text_w(f, NAVL[i], strlen(NAVL[i]), 1.6); wsum += ws[i]; }
      double gap = (a->nav.x1 - a->nav.x0 - wsum) / 5.0, x = a->nav.x0, top = a->nav.y1 - (lh_normal(f) + 6 + 2); int base = baseline_normal(f, top);
      for (int i = 0; i < 6; i++) { int act = i == home_active_nav(h), foc = h->nav.zone == Z_NAV && h->nav.nav == i;
          if (foc) draw_glow_text(fb, h, F_S400_11, x, base, NAVL[i], strlen(NAVL[i]), 6, RGBA(RED, 230), 1.6);
          T(fb, FULL, f, x, base, NAVL[i], act || foc ? 0xFFFFFF : 0xB9B7B2, 255, 1.6);
          if (act || foc) gfx_fill(fb, FULL, iround(x), a->nav.y1 - 2, ws[i], 2, RGBA(foc ? RED2 : RED, 255));
          LR(h, foc ? "nav-focus" : "nav", iround(x), iround(top), iround(x) + ws[i], a->nav.y1); x += ws[i] + gap; } }
    if (!h->n) return;
    int sel = h->nav.sel;
    /* the boxes, far ones first (the focus box last, on top) */
    for (int far = 4; far >= 0; far--) for (int k = 0; k < a->nenv; k++) { const envelope *e = &a->env[k]; int d = e->d < 0 ? -e->d : e->d; if (d != far) continue; int i = sel + e->d; if (i < 0 || i >= (int)h->n) continue; draw_box(h, fb, e, i, e->d == 0); }
    const tsprite *al = tsp(h, "arrow-l"), *ar = tsp(h, "arrow-r");
    if (al) gfx_image(fb, FULL, &al->im, al->box.x0, al->box.y0, sel == 0 ? 89 : 255); if (ar) gfx_image(fb, FULL, &ar->im, ar->box.x0, ar->box.y0, sel >= (int)h->n - 1 ? 89 : 255);
    { int n = h->n < 11 ? (int)h->n : 11, start = sel - 5; if (start > (int)h->n - n) start = (int)h->n - n; if (start < 0) start = 0;   /* .dots */
      int tw = n * 6 + (n - 1) * 12, x = (a->dots.x0 + a->dots.x1 - tw) / 2, cy = (a->dots.y0 + a->dots.y1) / 2;
      for (int i = 0; i < n; i++) { int on = start + i == sel; if (on) gfx_shadow(fb, FULL, x + i * 18, cy - 3, 6, 6, 5, RGBA(RED, 255)); gfx_rrect(fb, FULL, x + i * 18, cy - 3, 6, 6, 3, on ? RGBA(RED, 255) : RGBA(0xFFFFFF, 89)); } }
    hgame *g = &h->g[sel];
    { static const int TF[6] = { F_CIN46, F_CIN43, F_CIN40, F_CIN36, F_CIN32, F_CIN29 };   /* .title + homeFitText: 6 % steps down to 62 % of 46 px, then the ellipsis (N3.1) */
      int fid = fit_font(h, TF, 6, g->title, a->title.x1 - a->title.x0, 1); const font *f = &h->f[fid]; char t[400]; text_fit(f, g->title, a->title.x1 - a->title.x0, 1, t, sizeof t);
      double lh = 46 * 1.05; int base = baseline_in(f, a->title.y0, lh);   /* the line box keeps the 46 px line height, as in the browser */
      draw_glow_text(fb, h, fid, a->title.x0, base, t, strlen(t), 15, RGBA(0, 204), 1); T(fb, FULL, f, a->title.x0, base + 3, t, 0, 255, 1); T(fb, FULL, f, a->title.x0, base, t, 0xFFFFFF, 255, 1);
      LRtext(h, "title", f, a->title.x0, base, t, strlen(t), 1); }
    if (g->blurb) { const font *f = &h->f[F_S400_16B]; span ln[2]; char last[600]; size_t n = overview_clamp(f, g->blurb, a->blurb.x1 - a->blurb.x0, 2, .3, ln, last, sizeof last); double lh = 16 * 1.5;   /* .blurb (two lines) */
      for (size_t i = 0; i < n; i++) { int base = baseline_in(f, a->blurb.y0 + i * lh, lh); if (i == n - 1 && last[0]) T(fb, FULL, f, a->blurb.x0, base, last, 0xE0DED8, 255, .3); else text_draw(fb, NULL, FULL, f, a->blurb.x0, base, g->blurb + ln[i].start, ln[i].len, RGBA(0xE0DED8, 255), .3, 0); }
      LR(h, "blurb", a->blurb.x0, a->blurb.y0, a->blurb.x1, a->blurb.y0 + iround(n * lh)); }
    gfx_fill(fb, FULL, a->rule.x0, a->rule.y0, a->rule.x1 - a->rule.x0, a->rule.y1 - a->rule.y0, RGBA(0xFFFFFF, 15));
    { const char *k[4], *v[4], *ic[4]; int n; meta_list(g, k, v, ic, &n); double x = a->facts.x0; int cy = (a->facts.y0 + a->facts.y1) / 2; const font *fb7 = &h->f[F_S600_14], *fs = &h->f[F_S400_9];   /* .facts */
      if (!n && g->version_label) { k[0] = "VERSION"; v[0] = g->version_label; ic[0] = NULL; n = 1; }
      for (int i = 0; i < n; i++) {
          const image *im = ic[i] ? icon(h, ic[i], 26) : NULL; double x0 = x; if (im) { gfx_image(fb, FULL, im, iround(x), cy - 13, 255); x += 26 + 12; }
          char val[120]; text_fit(fb7, v[i], 150, 0, val, sizeof val); double th = lh_normal(fb7) + 3 + lh_normal(fs), ty = cy - th / 2;
          int xe1 = T(fb, FULL, fb7, x, baseline_normal(fb7, ty), val, 0xFFFFFF, 255, 0); int xe2 = T(fb, FULL, fs, x, baseline_normal(fs, ty + lh_normal(fb7) + 3), k[i], 0xCFCDC7, 255, 2);
          int xe = xe1 > xe2 ? xe1 : xe2; LR(h, "fact", iround(x0), a->facts.y0, xe, a->facts.y1); x = xe + 26;
          if (i < n - 1) { gfx_fill(fb, FULL, iround(x), a->facts.y0 + 2, 1, a->facts.y1 - a->facts.y0 - 4, RGBA(0xFFFFFF, 51)); x += 1 + 26; }
      } }
    { const font *fh = &h->f[F_S400_11]; T(fb, FULL, fh, a->pheading.x0, baseline_center(fh, a->pheading.y0, a->pheading.y1 - a->pheading.y0), "YOUR COLLECTION", 0xFFFFFF, 255, 2.5);   /* the collection panel */
      static const char *LB[4] = { "GAMES", "FOLDERS", "FAVORITES", "RECENT" }, *IC[4] = { "games", "folders", "favorites", "recent" }; int vals[4] = { a->counts[0], a->counts[1], a->counts[2], 0 };
      for (int i = 0; i < 4; i++) { box4 r = a->prows[i]; int cy = (r.y0 + r.y1) / 2; const image *im = icon(h, IC[i], 22); double x = r.x0; if (im) { gfx_image(fb, FULL, im, iround(x), cy - 11, 255); x += 22 + 22; }
          char num[16]; snprintf(num, sizeof num, "%d", vals[i]); const font *fv = &h->f[F_S700_18], *fl = &h->f[F_S400_10]; T(fb, FULL, fv, x, baseline_center(fv, r.y0, r.y1 - r.y0), num, 0xFFFFFF, 255, 0);
          int nw = text_w(fv, num, strlen(num), 0); x += (nw > 44 ? nw : 44) + 22; T(fb, FULL, fl, x, baseline_center(fl, r.y0, r.y1 - r.y0), LB[i], 0xD6D4CE, 255, 2); }
      char raw[200], foot[200]; snprintf(raw, sizeof raw, "%s \xC2\xB7 %d of %zu", home_section_title(h), sel + 1, h->n); upper(raw, foot, sizeof foot); const font *f9 = &h->f[F_S400_10]; box4 r = a->prows[3];   /* .pnl.r span: 10 px, uppercase, 2 px letter spacing */
      T(fb, FULL, f9, r.x0, baseline_center(f9, r.y1 + 3, 16), foot, 0x9C9A95, 255, 2); }
}
static void ba_render(home *h, framebuffer *fb, uint64_t now) {
    boxart *a = BX(h); (void)now;
    long key = (long)h->gen;   /* every input and section change bumps it */
    if (key != a->key || h->rec) { ba_draw(h, &a->frame); legend_items(h, &a->frame, a->legend, 1, TH_BOXART); a->key = h->rec ? -1 : key; }
    memcpy(fb->rgb, a->frame.rgb, (size_t)fb->w * fb->h * 3);
}
static void ba_input(home *h, button b) {
    if (h->nav.zone == Z_NAV) {
        if (b == B_LEFT && h->nav.nav > 0) h->nav.nav--; else if (b == B_RIGHT && h->nav.nav < 5) h->nav.nav++;
        else if (b == B_DOWN || b == B_B) h->nav.zone = home_section_zone(h);
        else if (b == B_A) { if (h->nav.nav == 0) home_go_all(h); else if (h->nav.nav == 1) home_go_browse(h, -1); else not_yet(h, NAV_NAME[h->nav.nav]); }
        return;
    }
    if (b == B_LEFT) { if (h->nav.sel > 0) h->nav.sel--; } else if (b == B_RIGHT) { if (h->nav.sel < (int)h->n - 1) h->nav.sel++; }
    else if (b == B_UP) { h->nav.zone = Z_NAV; h->nav.nav = home_active_nav(h); }
    else if (b == B_A) launch_unavailable(h, h->nav.sel);
    else if (b == B_SELECT) { extern int home_open_details_sel(home *h); home_open_details_sel(h); }
}
static const dstyle BOXART_DETAILS = {
    .border = 0, .inset = 0,
    .hz = { { 0, 0, .10 }, { .28, 0, .05 }, { .42, 0, .55 }, { 1, 0, .70 } }, .vt = { { 0, 0, .60 }, { .14, 0, 0 }, { .80, 0, 0 }, { 1, 0, .75 } }, .pixelated = 0,
    .title_font = F_CIN46, .title_lh = 1.12, .title_rgb = 0xFFFFFF, .title_spacing = 1,
    .ver_bold = F_S700_15, .ver_font = F_S400_15, .meta_key = F_S700_16, .meta_val = F_S400_16, .meta_rowh = 23.2,
    .act_font = F_S400_20, .act_radius = 999, .act_bw = 2, .act_bg = 0, .act_bg_a = 140, .act_border = 0xFFFFFF, .act_border_a = 77,
    .desc_font = F_S400_19, .desc_lh = 1.55, .desc_radius = 12, .desc_bw = 1, .desc_bg = 0, .desc_bg_a = 107, .desc_border = 0xFFFFFF, .desc_border_a = 71,
    .dh_font = F_S700_13, .dh_lh = 1.4, .dh_spacing = 1.6, .dh_bold = 0,
    .cover_radius = 3, .cover_ring = 1, .cover_ring_rgb = 0xFFFFFF, .cover_ring_a = 51, .note_font = F_S400_14 };
const theme_ops THEME_BOXART = { "box-art-showcase", "Box Art Showcase", ba_init, ba_fini, ba_render, ba_input, &BOXART_DETAILS, 0 };
