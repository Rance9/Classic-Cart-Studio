/* Featured Shelf (N3): the master's left sidebar, the wide hero (the game's key art with the two-tone Silkscreen lettering), the
 * right information column and the cover shelf. home-themes.js hmShelf / hmHero / hmHeroFit / hmHeroTag + home.css
 * .hm-featured-shelf, geometry measured on the master (home-art.json featured-shelf).
 *
 * The hero shows the game's own approved key art (Featured Shelf derivative of its hero illustration) or the universal fallback
 * scene; the lettering sits in the art's measured title slot, fitted from 46 px down in 2 px steps (two lines without a widow,
 * else up to three at 30 px or less), the last line red, with the red rule and the year / genre / publisher line under it.
 * Shelf covers are shown whole (N1.1: no cropped box art). Navigation: Left / Right along the shelf; Up, or Left from the
 * first game, into the sidebar (Up / Down there, Right or B back). */
#include "home_int.h"

static const char *SIDE_LABEL[6] = { "All Games", "Browse", "Search", "Favorites", "Recent", "Settings" };
static const char *SIDE_NAME[6] = { "All Games", "Browse", "Search", "Favorites", "Recently Played", "Settings" };
static const char *SIDE_ICON[6] = { "icon-home", "icon-folder", NULL, "icon-star", NULL, "icon-gear" };
typedef struct {
    box4 plate, hero, label, title, rows[3], text, cells[12], legend; int ncells, pitch, icon_x, text_x, first_y;
    framebuffer frame; long key; image wide; int wide_game;
} shelf;
#define SH(h) ((shelf *)(h)->tstate)
static const int HERO_FONTS[14] = { F_SILKH46, F_SILKH44, F_SILKH42, F_SILKH40, F_SILKH38, F_SILKH36, F_SILKH34, F_SILKH32, F_SILKH30, F_SILKH28, F_SILKH26, F_SILKH24, F_SILKH22, F_SILKH20 };

static int sh_init(home *h, char *err, size_t errlen) {
    shelf *a = calloc(1, sizeof *a); if (!a) { snprintf(err, errlen, "out of memory"); return -1; } h->tstate = a; a->key = -1; a->wide_game = -1;
    const jval *g = h->geo, *sd = json_get(g, "side"), *hero = json_get(g, "hero"), *info = json_get(g, "info"), *sf = json_get(g, "shelf");
    if (!sd || !hero || !info || !sf || gbox(sd, "plate", &a->plate) || gbox(hero, "inner", &a->hero) || gbox(info, "label", &a->label) || gbox(info, "title", &a->title) || gbox(info, "text", &a->text) || gbox(g, "legend", &a->legend)) { snprintf(err, errlen, "featured-shelf geometry incomplete"); return -1; }
    a->pitch = gnum(sd, "pitch", 52); a->icon_x = gnum(sd, "icon_x", 44); a->text_x = gnum(sd, "text_x", 100); a->first_y = gnum(sd, "first_y", 112);
    const jval *rows = json_get(info, "rows"); for (int i = 0; i < 3; i++) if (jbox_at(rows, (size_t)i, &a->rows[i])) { snprintf(err, errlen, "featured-shelf rows missing"); return -1; }
    const jval *cells = json_get(sf, "cells"); for (size_t i = 0; cells && i < cells->n && a->ncells < 12; i++) if (!jbox_at(cells, i, &a->cells[a->ncells])) a->ncells++;
    if (a->ncells < 1) { snprintf(err, errlen, "featured-shelf cells missing"); return -1; }
    if (fb_init(&a->frame, CCS_FB_W, CCS_FB_H)) { snprintf(err, errlen, "out of memory"); return -1; }
    return 0;
}
static void sh_fini(home *h) { shelf *a = SH(h); if (!a) return; if (a->frame.rgb) fb_free(&a->frame); img_free(&a->wide); free(a); h->tstate = NULL; }

/* ---------- the hero lettering (hmHeroLines / hmHeroFit / hmHeroText / hmHeroTag) ---------- */
typedef struct { span ln[8]; size_t n; int over; } lines;
static void hero_lines(const font *f, const char *s, int maxw, lines *L) {   /* greedy word wrap measured without letter spacing (canvas) */
    L->n = 0; L->over = 0; size_t len = strlen(s), i = 0, cs = 0, ce = 0; int have = 0;
    while (i <= len) {
        size_t ws = i; while (i < len && s[i] != ' ') i++; size_t we = i;
        if (!have) { cs = ws; ce = we; have = 1; }
        else { if (text_w(f, s + cs, we - cs, 0) <= maxw) ce = we; else { if (L->n < 8) { L->ln[L->n].start = cs; L->ln[L->n].len = ce - cs; L->n++; } cs = ws; ce = we; } }
        i++;
    }
    if (have && L->n < 8) { L->ln[L->n].start = cs; L->ln[L->n].len = ce - cs; L->n++; }
    for (size_t k = 0; k < L->n; k++) if (text_w(f, s + L->ln[k].start, L->ln[k].len, 0) > maxw) L->over = 1;
}
static int hero_fit(const home *h, const char *t, int maxw, lines *best) {
    int found = -1; lines L, keep; int keep_fi = -1;
    for (int k = 0; k < 14; k++) {
        int fs = 46 - 2 * k; hero_lines(&h->f[HERO_FONTS[k]], t, maxw, &L);
        int widow = L.n > 1 && L.ln[L.n - 1].len <= 2; if (L.over) continue;
        if (L.n <= 2 && !widow) { *best = L; found = k; break; }
        if (keep_fi < 0 && L.n <= 3 && fs <= 30) { keep = L; keep_fi = k; }
    }
    if (found >= 0) return HERO_FONTS[found];
    if (keep_fi >= 0) { *best = keep; return HERO_FONTS[keep_fi]; }
    hero_lines(&h->f[F_SILKH20], t, maxw, best); return F_SILKH20;
}
static double dsqrt(double x) { if (x <= 0) return 0; double r = x > 1 ? x : 1; for (int i = 0; i < 40; i++) r = 0.5 * (r + x / r); return r; }
static void radial_box(framebuffer *fb, rect clip, int x, int y, int w, int hh, int radius) {   /* .lbg: radial-gradient(ellipse at 50% 50%, .62, .42 45%, 0 72%) */
    double cx = x + w / 2.0, cy = y + hh / 2.0, rx = w / 2.0 * 1.41421356, ry = hh / 2.0 * 1.41421356; (void)radius;
    for (int yy = y < clip.y0 ? clip.y0 : y; yy < y + hh && yy < clip.y1; yy++) for (int xx = x < clip.x0 ? clip.x0 : x; xx < x + w && xx < clip.x1; xx++) {
        if (xx < 0 || yy < 0 || xx >= fb->w || yy >= fb->h) continue;
        double dx = (xx + 0.5 - cx) / rx, dy = (yy + 0.5 - cy) / ry, t = dsqrt(dx * dx + dy * dy), a; if (t >= .72) continue;
        a = t < .45 ? .62 - (.62 - .42) * t / .45 : .42 - .42 * (t - .45) / .27;
        uint8_t *q = fb->rgb + ((size_t)yy * fb->w + xx) * 3; const uint8_t c[3] = { 4, 5, 12 }; for (int k = 0; k < 3; k++) q[k] = (uint8_t)(q[k] + (c[k] - q[k]) * a + 0.5);
    }
}
static void hero_letter(framebuffer *fb, home *h, rect clip, int fid, double x, int base, const char *s, size_t len, uint32_t rgb) {
    const font *f = &h->f[fid]; int w = text_w(f, s, len, 1), m = 36; mask mk;   /* text-shadow: 3px 4px 0 #000, 1 px outline, 0 0 22px rgba(0,0,0,.95) */
    if (!mask_alloc(&mk, (int)x - m, base - f->ascent - m, w + 2 * m, f->ascent + f->descent + 2 * m)) { text_draw(NULL, &mk, FULL, f, x, base, s, len, RGBA(0, 255), 1, 0); mask_blur(&mk, 11); gfx_mask(fb, clip, &mk, RGBA(0, 242)); mask_free(&mk); }
    static const int OFF[4][2] = { { 3, 4 }, { -1, -1 }, { 1, -1 }, { -1, 1 } };
    for (int i = 0; i < 4; i++) text_draw(fb, NULL, clip, f, x + OFF[i][0], base + OFF[i][1], s, len, RGBA(0, 255), 1, 0);
    text_draw(fb, NULL, clip, f, x, base, s, len, RGBA(rgb, 255), 1, 0);
}
static void draw_hero(home *h, framebuffer *fb, int gi) {
    shelf *a = SH(h); hgame *g = &h->g[gi]; box4 B = a->hero; int W = B.x1 - B.x0, H = B.y1 - B.y0; rect clip = { B.x0, B.y0, B.x1, B.y1 };
    gfx_fill(fb, clip, B.x0, B.y0, W, H, RGBA(0x07070A, 255));
    if (a->wide_game != gi) { img_free(&a->wide); a->wide_game = gi; char p[1200], err[200]; if (g->hero_wide) { snprintf(p, sizeof p, "%s/%s", h->dir, g->hero_wide); if (img_load(p, &a->wide, 0, err, sizeof err)) a->wide.px = NULL; } }
    if (a->wide.px) { image sc; if (a->wide.w == W && a->wide.h == H) gfx_image(fb, clip, &a->wide, B.x0, B.y0, 255); else if (!img_cover(&a->wide, &sc, W, H)) { gfx_image(fb, clip, &sc, B.x0, B.y0, 255); img_free(&sc); } }
    stop hz[3] = { { .46, 0x04050C, 0 }, { .66, 0x04050C, .42 }, { 1, 0x04050C, .62 } }, vt[4] = { { 0, 0, .22 }, { .34, 0, 0 }, { .78, 0, 0 }, { 1, 0, .45 } };   /* .shade.art */
    gfx_gradient(fb, clip, B.x0, B.y0, W, H, 1, vt, 4); gfx_gradient(fb, clip, B.x0, B.y0, W, H, 0, hz, 3);
    /* the title slot measured on the art (or the master's top right) and the fitted lettering */
    double tx = g->hero_kind && !strcmp(g->hero_kind, "hero") ? g->ht_x : 0.5, ty = g->hero_kind && !strcmp(g->hero_kind, "hero") ? g->ht_y : 0.09, tw = g->hero_kind && !strcmp(g->hero_kind, "hero") ? g->ht_w : 0; int right = g->hero_kind && !strcmp(g->hero_kind, "hero") && g->ht_right;
    int lx = iround(W * tx), margin = iround(W * 0.035), lw = (tw > 0 && tw <= 1) ? (iround(W * tw) < W - lx - margin ? iround(W * tw) : W - lx - margin) : W - lx - margin;
    char title[400]; upper(g->title, title, sizeof title); lines L; int fid = hero_fit(h, title, lw, &L); const font *f = &h->f[fid]; int fs = f->px, lh = iround(fs * 1.18), y0 = iround(H * ty);
    int X0 = B.x0 + lx, Y0 = B.y0;
    radial_box(fb, clip, X0 + (right ? -10 : -34), Y0 + y0 - 26, lw + (right ? 10 + 34 : 34 + 30), (int)L.n * lh + 78, 18);
    for (size_t i = 0; i < L.n; i++) {
        int w = text_w(f, title + L.ln[i].start, L.ln[i].len, 1); double x = right ? X0 + lw - w : X0; int base = baseline_normal(f, Y0 + y0 + (int)i * lh);
        hero_letter(fb, h, clip, fid, x, base, title + L.ln[i].start, L.ln[i].len, i == L.n - 1 && L.n > 1 ? RED2 : 0xF5F3EE);
        LR(h, "hero-title", iround(x), base - f->ascent, iround(x) + w, base + f->descent);
    }
    int rule_y = Y0 + y0 + (int)L.n * lh + 4, tag_y = Y0 + y0 + (int)L.n * lh + 14;
    gfx_fill(fb, clip, right ? X0 + lw - 64 : X0, rule_y, 64, 3, RGBA(RED, 255));
    /* hmHeroTag: as many of year / genre / publisher as fit, 13 down to 10 px */
    const char *parts[3]; int np = 0; if (g->year) parts[np++] = g->year; if (g->genre) parts[np++] = g->genre; if (g->publisher) parts[np++] = g->publisher;
    static const int TAGF[4] = { F_SILK13, F_SILK12, F_SILK11, F_SILK10 }; char tag[300] = ""; int tf = F_SILK10;
    for (int k = 0; k < 4 && !tag[0]; k++) for (int n = np; n >= 1; n--) { char t[300]; size_t o = 0; t[0] = 0; for (int i = 0; i < n; i++) o += (size_t)snprintf(t + o, sizeof t - o, "%s%s", i ? " \xC2\xB7 " : "", parts[i]);
        if (text_w(&h->f[TAGF[k]], t, strlen(t), 0) <= lw) { snprintf(tag, sizeof tag, "%s", t); tf = TAGF[k]; break; } }
    if (!tag[0] && np) snprintf(tag, sizeof tag, "%s", parts[0]);
    if (tag[0]) { const font *ft = &h->f[tf]; int w = text_w(ft, tag, strlen(tag), 1); double x = right ? X0 + lw - w : X0; int base = baseline_normal(ft, tag_y);
        text_draw(fb, NULL, clip, ft, x + 1, base + 1, tag, strlen(tag), RGBA(0, 255), 1, 0); text_draw(fb, NULL, clip, ft, x, base, tag, strlen(tag), RGBA(0xC8C4BC, 255), 1, 0); }
    LR(h, "hero", B.x0, B.y0, B.x1, B.y1);
}

/* ---------- the page ---------- */
static void sh_draw(home *h, framebuffer *fb) {
    shelf *a = SH(h); memcpy(fb->rgb, h->scene.px, (size_t)fb->w * fb->h * 3);
    int ph = a->plate.y1 - a->plate.y0;
    for (int i = 0; i < 6; i++) {   /* the sidebar: All Games active (the authored plate), the focused entry glowing */
        int y = a->first_y + i * a->pitch, act = i == home_active_nav(h), foc = h->nav.zone == Z_NAV && h->nav.nav == i;
        if (act) { const tsprite *s = tsp(h, "side-active"); image tmp; const image *im = sprite_scaled(h, "side-active", a->plate.x1 - a->plate.x0, ph, 0, &tmp); if (s && im) gfx_image(fb, FULL, im, a->plate.x0, y, 255); img_free(&tmp); }
        if (SIDE_ICON[i]) { image tmp; const image *im = sprite_scaled(h, SIDE_ICON[i], 35, 35, 1, &tmp); if (im) gfx_image(fb, FULL, im, a->icon_x, y + (ph - 35) / 2, 255); img_free(&tmp); }
        else { const font *s = &h->f[F_SYM24]; const char *gl = i == 2 ? "\xE2\x8C\x95" : "\xE2\x97\xB7"; T(fb, FULL, s, a->icon_x + 4, baseline_center(s, y, ph), gl, 0xF1EFE9, 255, 0); }
        const font *f = &h->f[F_VT26]; int base = baseline_center(f, y, ph);
        if (foc) draw_glow_text(fb, h, F_VT26, a->text_x, base, SIDE_LABEL[i], strlen(SIDE_LABEL[i]), 5, RGBA(RED, 204), .5);
        T(fb, FULL, f, a->text_x, base, SIDE_LABEL[i], foc ? 0xFFFFFF : act ? 0xFF5B63 : 0xF1EFE9, 255, .5);
        LR(h, foc ? "side-focus" : "side", a->icon_x, y, a->plate.x1, y + ph);
    }
    if (!h->n) return;
    int sel = h->nav.sel; hgame *g = &h->g[sel];
    draw_hero(h, fb, sel);
    { const tsprite *fr = tsp(h, "hero-frame"); if (fr) gfx_image(fb, FULL, &fr->im, fr->box.x0, fr->box.y0, 255); }
    { char lab[300], st[200]; upper(home_section_title(h), st, sizeof st); snprintf(lab, sizeof lab, "%s \xC2\xB7 %d OF %zu", st, sel + 1, h->n); const font *f = &h->f[F_SILK10]; T(fb, FULL, f, a->label.x0, baseline_normal(f, a->label.y0), lab, 0xFF4A54, 255, 1); }
    { char t[400], fit[400]; upper(g->title, t, sizeof t); static const int TF[6] = { F_SILK25, F_SILKT23, F_SILKH22, F_SILKH20, F_SILKT19, F_SILKT16 };   /* .ftitle + homeFitText */
      int fid = fit_font(h, TF, 6, t, a->title.x1 - a->title.x0, .5); const font *f = &h->f[fid]; text_fit(f, t, a->title.x1 - a->title.x0, .5, fit, sizeof fit);
      int base = baseline_in(f, a->title.y0, f->px * 1.2); T(fb, FULL, f, a->title.x0, base + 2, fit, 0, 255, .5); T(fb, FULL, f, a->title.x0, base, fit, 0xFFFFFF, 255, .5); LRtext(h, "ftitle", f, a->title.x0, base, fit, strlen(fit), .5); }
    { const char *K[5] = { "YEAR", "PLAYERS", "GENRE", "PUBLISHER", "DEVELOPER" }, *IK[5] = { "year", "players", "genre", "publisher", "developer" }; const char *V[5] = { g->year, g->players, g->genre, g->publisher, g->developer };
      const font *f = &h->f[F_VT18]; int r = 0;
      for (int i = 0; i < 5 && r < 3; i++) { if (!V[i]) continue; box4 b = a->rows[r++]; int ch = b.y1 - b.y0 - 5, base = baseline_center(f, b.y0, ch);
          const image *ic = icon(h, IK[i], 24); if (ic) gfx_image(fb, FULL, ic, b.x0, b.y0, 255);
          T(fb, FULL, f, b.x0 + 46, base, K[i], 0xE6E4DE, 255, 1.5); char v[120]; text_fit(f, V[i], b.x1 - b.x0 - 46 - text_w(f, K[i], strlen(K[i]), 1.5) - 12, 1.5, v, sizeof v);
          int vw = text_w(f, v, strlen(v), 1.5); T(fb, FULL, f, b.x1 - vw, base, v, 0xFFFFFF, 255, 1.5); gfx_fill(fb, FULL, b.x0, b.y1 - 1, b.x1 - b.x0, 1, RGBA(0xFFFFFF, 56)); LR(h, "frow", b.x0, b.y0, b.x1, b.y1); } }
    if (g->ftext) { const font *f = &h->f[F_VT21]; span ln[16]; double lh = 21 * 1.4; rect clip = { a->text.x0, a->text.y0, a->text.x1, a->text.y1 }; size_t n = text_wrap(f, g->ftext, a->text.x1 - a->text.x0, 0, ln, 16);
      for (size_t i = 0; i < n; i++) text_draw(fb, NULL, clip, f, a->text.x0, baseline_in(f, a->text.y0 + i * lh, lh), g->ftext + ln[i].start, ln[i].len, RGBA(0xEFEDE7, 255), 0, 0); }
    int start = sel - 3; if (start > (int)h->n - a->ncells) start = (int)h->n - a->ncells; if (start < 0) start = 0;
    const tsprite *al = tsp(h, "arrow-l"), *ar = tsp(h, "arrow-r");
    if (al) gfx_image(fb, FULL, &al->im, al->box.x0, al->box.y0, start == 0 ? 89 : 255); if (ar) gfx_image(fb, FULL, &ar->im, ar->box.x0, ar->box.y0, start + a->ncells >= (int)h->n ? 89 : 255);
    for (int k = 0; k < a->ncells; k++) {   /* the shelf: whole covers, the selected one in the authored focus frame */
        int i = start + k; if (i >= (int)h->n) break; box4 c = a->cells[k], art; draw_cover(h, fb, i, c, i == sel, &art); LR(h, "shelf-art", art.x0, art.y0, art.x1, art.y1);
        if (i == sel) { box4 f = { c.x0 - 10, c.y0 - 13, c.x1 + 10, c.y1 + 11 }; image tmp; const image *im = sprite_scaled(h, "shelf-focus", f.x1 - f.x0, f.y1 - f.y0, 0, &tmp);
            if (im) gfx_image(fb, FULL, im, f.x0, f.y0, h->nav.zone == Z_GAMES ? 255 : 140); img_free(&tmp); LR(h, h->nav.zone == Z_GAMES ? "shelf-focus" : "shelf-selected", f.x0, f.y0, f.x1, f.y1); }
    }
}
static void sh_render(home *h, framebuffer *fb, uint64_t now) {
    shelf *a = SH(h); (void)now;
    long key = (long)h->gen;   /* every input and section change bumps it */
    if (key != a->key || h->rec) { sh_draw(h, &a->frame); legend_items(h, &a->frame, a->legend, 1, TH_SHELF); a->key = h->rec ? -1 : key; }
    memcpy(fb->rgb, a->frame.rgb, (size_t)fb->w * fb->h * 3);
}
static void sh_input(home *h, button b) {
    if (h->nav.zone == Z_NAV) {   /* the sidebar (vertical) */
        if (b == B_UP && h->nav.nav > 0) h->nav.nav--; else if (b == B_DOWN && h->nav.nav < 5) h->nav.nav++;
        else if (b == B_RIGHT || b == B_B) h->nav.zone = home_section_zone(h);
        else if (b == B_A) { if (h->nav.nav == 0) home_go_all(h); else if (h->nav.nav == 1) home_go_browse(h, -1); else not_yet(h, SIDE_NAME[h->nav.nav]); }
        return;
    }
    if (b == B_LEFT) { if (h->nav.sel > 0) h->nav.sel--; else { h->nav.zone = Z_NAV; h->nav.nav = home_active_nav(h); } }
    else if (b == B_RIGHT) { if (h->nav.sel < (int)h->n - 1) h->nav.sel++; }
    else if (b == B_UP) { h->nav.zone = Z_NAV; h->nav.nav = home_active_nav(h); }
    else if (b == B_A) launch_unavailable(h, h->nav.sel);
    else if (b == B_SELECT) { extern int home_open_details_sel(home *h); home_open_details_sel(h); }
}
static const dstyle SHELF_DETAILS = {
    .border = 3, .border_rgb = 0x5B1A21, .inset = 0,
    .hz = { { 0, 0x11080A, .14 }, { .28, 0x11080A, .06 }, { .40, 0x11080A, .58 }, { 1, 0x11080A, .70 } }, .vt = { { 0, 0, .20 }, { .22, 0, 0 }, { .72, 0, 0 }, { 1, 0x11080A, .72 } }, .pixelated = 0,
    .title_font = F_SILK30, .title_lh = 1.25, .title_rgb = 0xFFFFFF, .title_spacing = 0,
    .ver_bold = F_S700_15, .ver_font = F_S400_15, .meta_key = F_VT19, .meta_val = F_VT19, .meta_rowh = 27.6,
    .act_font = F_VT23, .act_radius = 6, .act_bw = 2, .act_bg = 0x1B0F12, .act_bg_a = 255, .act_border = 0x5B1A21, .act_border_a = 255,
    .desc_font = F_VT23, .desc_lh = 1.55, .desc_radius = 8, .desc_bw = 2, .desc_bg = 0x11080A, .desc_bg_a = 184, .desc_border = 0x5B1A21, .desc_border_a = 255,
    .dh_font = F_VT17, .dh_lh = 1.4, .dh_spacing = 1, .dh_bold = 1,
    .cover_radius = 6, .cover_ring = 1, .cover_ring_rgb = 0xFFFFFF, .cover_ring_a = 31, .note_font = F_S400_14 };
const theme_ops THEME_SHELF = { "featured-shelf", "Featured Shelf", sh_init, sh_fini, sh_render, sh_input, &SHELF_DETAILS, 0 };
