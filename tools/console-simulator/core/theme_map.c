/* Adventure Map (N3): the illustrated overworld untouched, live labels in the authored sign plates, the marker and the walker at
 * the focused landmark, the landmark card, the information panel and the cover shelf. home-themes.js hmMap + home.css
 * .hm-adventure-map, geometry measured on the master (home-art.json adventure-map: landmarks, card, panel, shelf).
 *
 * Two zones: the shelf (Left / Right through the games, Up onto the map) and the map (Left / Right along the path between the
 * landmarks, Down back to the shelf, A enters a landmark, SELECT lists the destinations). Covers are shown whole (N1.1); the
 * Windows map shortcuts (two folders) label their signs. */
#include "home_int.h"

#define NLM 6
static const char *LM_ID[NLM] = { "allgames", "browse", "favorites", "folder1", "folder2", "settings" };
typedef struct { box4 sign; int cx, cy, sx, sy; } landmark;
typedef struct {
    landmark lm[NLM]; box4 card_cover, pframe, pcover, ptitle, ptags, ptext, prule, cells[12], legend; int ncells;
    char shortcut[2][80]; framebuffer frame; long key;
} map;
#define MP(h) ((map *)(h)->tstate)

static int mp_init(home *h, char *err, size_t errlen) {
    map *a = calloc(1, sizeof *a); if (!a) { snprintf(err, errlen, "out of memory"); return -1; } h->tstate = a; a->key = -1;
    const jval *g = h->geo, *lms = json_get(g, "landmarks"), *card = json_get(g, "card"), *p = json_get(g, "panel"), *sf = json_get(g, "shelf");
    if (!lms || lms->t != J_ARR || !card || !p || !sf || gbox(card, "cover", &a->card_cover) || gbox(p, "frame", &a->pframe) || gbox(p, "cover", &a->pcover) || gbox(p, "title", &a->ptitle)
        || gbox(p, "tags", &a->ptags) || gbox(p, "text", &a->ptext) || gbox(p, "rule", &a->prule) || gbox(g, "legend", &a->legend)) { snprintf(err, errlen, "adventure-map geometry incomplete"); return -1; }
    for (int k = 0; k < NLM; k++) {
        const jval *l = NULL; for (size_t i = 0; i < lms->n; i++) { const jval *id = json_get(lms->items[i], "id"); if (id && id->t == J_STR && !strcmp(id->s, LM_ID[k])) l = lms->items[i]; }
        box4 c; if (!l || gbox(l, "sign", &a->lm[k].sign)) { snprintf(err, errlen, "landmark %s missing", LM_ID[k]); return -1; }
        const jval *ce = json_get(l, "center"); a->lm[k].cx = ce && ce->n == 2 ? (int)ce->items[0]->num : (a->lm[k].sign.x0 + a->lm[k].sign.x1) / 2; a->lm[k].cy = ce && ce->n == 2 ? (int)ce->items[1]->num : a->lm[k].sign.y1 + 8;
        const jval *st = json_get(l, "stand");   /* hmStand: on the path left of the marker, below the sign (or the master's own spot) */
        if (st && st->t == J_ARR && st->n == 2) { a->lm[k].sx = (int)st->items[0]->num; a->lm[k].sy = (int)st->items[1]->num; } else { a->lm[k].sx = a->lm[k].cx - 104; a->lm[k].sy = a->lm[k].sign.y1 - 6; }
        (void)c;
    }
    const jval *cells = json_get(sf, "cells"); for (size_t i = 0; cells && i < cells->n && a->ncells < 12; i++) if (!jbox_at(cells, i, &a->cells[a->ncells])) a->ncells++;
    const jval *ms = json_get(h->pk, "map_shortcuts"); for (int i = 0; i < 2; i++) { const jval *v = ms && ms->t == J_ARR && (size_t)i < ms->n ? ms->items[i] : NULL; snprintf(a->shortcut[i], sizeof a->shortcut[i], "%s", v && v->t == J_STR ? v->s : ""); }
    if (fb_init(&a->frame, CCS_FB_W, CCS_FB_H)) { snprintf(err, errlen, "out of memory"); return -1; }
    return 0;
}
static void mp_fini(home *h) { map *a = MP(h); if (!a) return; if (a->frame.rgb) fb_free(&a->frame); free(a); h->tstate = NULL; }

static const char *lm_label(map *a, int k) {
    static const char *L[NLM] = { "All Games", "Browse", "Favorites", NULL, NULL, "Settings" };
    if (k == 3 || k == 4) return a->shortcut[k - 3][0] ? a->shortcut[k - 3] : "No folder set"; return L[k];
}
static void sprite_box(home *h, framebuffer *fb, const char *name, box4 b, int opacity, int nearest) {
    image tmp; const image *im = sprite_scaled(h, name, b.x1 - b.x0, b.y1 - b.y0, nearest, &tmp); if (im) gfx_image(fb, FULL, im, b.x0, b.y0, opacity); img_free(&tmp);
}
static void mp_draw(home *h, framebuffer *fb) {
    map *a = MP(h); memcpy(fb->rgb, h->scene.px, (size_t)fb->w * fb->h * 3);
    for (int k = 0; k < NLM; k++) {   /* the signs: live labels (cut at 18 characters), plates widened for long labels */
        const char *full = lm_label(a, k); char lab[64], up[64]; size_t L = strlen(full);
        if (L > 18) { snprintf(lab, sizeof lab, "%.17s", full); size_t e = strlen(lab); while (e && lab[e - 1] == ' ') lab[--e] = 0; snprintf(lab + e, sizeof lab - e, "\xE2\x80\xA6"); } else snprintf(lab, sizeof lab, "%s", full);
        upper(lab, up, sizeof up); int chars = 0; for (const char *c = lab; *c; c++) if ((*c & 0xC0) != 0x80) chars++;
        int fs = chars > 10 ? 8 : 9; box4 b = a->lm[k].sign; int need = (int)(chars * (fs + 0.4) + 0.999999) + 26 - (b.x1 - b.x0); if (need < 0) need = 0;
        box4 bb = { b.x0 - iround(need / 2.0), b.y0, b.x1 + iround(need / 2.0), b.y1 };
        int foc = h->nav.zone == Z_MAP && h->nav.landmark == k, act = home_landmark_active(h, k);
        sprite_box(h, fb, k == 0 ? "sign-start" : "sign", bb, 255, 1);
        if (foc) { gfx_shadow(fb, FULL, bb.x0, bb.y0, bb.x1 - bb.x0, bb.y1 - bb.y0, 11, RGBA(RED, 230)); gfx_rrect_ring(fb, FULL, bb.x0 - 3, bb.y0 - 3, bb.x1 - bb.x0 + 6, bb.y1 - bb.y0 + 6, 9, 3, RGBA(RED2, 217)); }
        const font *f = &h->f[fs == 8 ? F_PX8 : F_PX9]; int tw = text_w(f, up, strlen(up), .3), x0 = bb.x0 + 8, w = bb.x1 - bb.x0 - 16; double x = x0 + (w - tw) / 2.0; int base = baseline_center(f, bb.y0, bb.y1 - bb.y0);
        rect clip = { x0, bb.y0, x0 + w, bb.y1 };
        draw_glow_text(fb, h, fs == 8 ? F_PX8 : F_PX9, x, base, up, strlen(up), 3, RGBA(0, 255), .3);
        if (foc) draw_glow_text(fb, h, fs == 8 ? F_PX8 : F_PX9, x, base, up, strlen(up), 5, RGBA(0xFFD166, 230), .3);
        text_draw(fb, NULL, clip, f, x, base + 1, up, strlen(up), RGBA(0, 255), .3, 0);
        text_draw(fb, NULL, clip, f, x, base, up, strlen(up), RGBA(act ? 0xFFB3B7 : foc ? 0xFFD166 : 0xF6EFE2, 255), .3, 0);
        LR(h, foc ? "sign-focus" : "sign", bb.x0, bb.y0, bb.x1, bb.y1);
    }
    landmark *cur = &a->lm[h->nav.landmark];
    sprite_box(h, fb, "marker", (box4){ cur->cx - 21, cur->cy - 12, cur->cx + 21, cur->cy + 12 }, 255, 1);
    { const tsprite *w = tsp(h, "hero"); if (w) { gfx_image(fb, FULL, &w->im, cur->sx, cur->sy, 255); LR(h, "walker", cur->sx, cur->sy, cur->sx + w->im.w, cur->sy + w->im.h); } }
    if (!h->n) return;
    int sel = h->nav.sel; hgame *g = &h->g[sel];
    { box4 art; draw_cover(h, fb, sel, a->card_cover, 1, &art); LR(h, "card-art", art.x0, art.y0, art.x1, art.y1); }
    { const tsprite *s = tsp(h, "card-frame"); if (s) gfx_image(fb, FULL, &s->im, s->box.x0, s->box.y0, 255); s = tsp(h, "card-pointer"); if (s) gfx_image(fb, FULL, &s->im, s->box.x0, s->box.y0, 255); }
    gfx_fill(fb, FULL, a->pframe.x0, a->pframe.y0, a->pframe.x1 - a->pframe.x0, a->pframe.y1 - a->pframe.y0, RGBA(0x0A0808, 230));
    { const tsprite *s = tsp(h, "panel-frame"); if (s) gfx_image(fb, FULL, &s->im, s->box.x0, s->box.y0, 255); }
    { box4 art; draw_cover(h, fb, sel, a->pcover, 1, &art); LR(h, "panel-art", art.x0, art.y0, art.x1, art.y1); }
    { char t[400], fit[400]; upper(g->title, t, sizeof t); static const int TF[5] = { F_PX21, F_PX19, F_PX17, F_PX15, F_PX13 };   /* .mtitle + homeFitText */
      int fid = fit_font(h, TF, 5, t, a->ptitle.x1 - a->ptitle.x0, 1); const font *f = &h->f[fid]; text_fit(f, t, a->ptitle.x1 - a->ptitle.x0, 1, fit, sizeof fit);
      int base = baseline_center(f, a->ptitle.y0, a->ptitle.y1 - a->ptitle.y0); T(fb, FULL, f, a->ptitle.x0, base + 2, fit, 0, 255, 1); T(fb, FULL, f, a->ptitle.x0, base, fit, 0xF3E7D3, 255, 1); LRtext(h, "mtitle", f, a->ptitle.x0, base, fit, strlen(fit), 1); }
    { const char *tags[3]; char ply[40]; int n = 0; if (g->genre) tags[n++] = g->genre; if (g->players) { snprintf(ply, sizeof ply, "%s player%s", g->players, strcmp(g->players, "1") ? "s" : ""); tags[n++] = ply; } if (g->year) tags[n++] = g->year;
      const font *f = &h->f[F_PX7]; double x = a->ptags.x0; int hh = iround(lh_normal(f)) + 8 + 2;
      for (int i = 0; i < n; i++) { char up[120]; upper(tags[i], up, sizeof up); int w = text_w(f, up, strlen(up), .3) + 16 + 2; if (x + w > a->ptags.x1 + 40) break;
          gfx_rrect_ring(fb, FULL, iround(x), a->ptags.y0, w, hh, 0, 1, RGBA(RED2, 255)); T(fb, FULL, f, x + 9, baseline_center(f, a->ptags.y0 + 1, hh - 2), up, 0xFFB3B7, 255, .3); LR(h, "mtag", iround(x), a->ptags.y0, iround(x) + w, a->ptags.y0 + hh); x += w + 10; } }
    if (g->mblurb) { const font *f = &h->f[F_MONO14]; span ln[2]; char last[600]; size_t n = overview_clamp(f, g->mblurb, a->ptext.x1 - a->ptext.x0, 2, 0, ln, last, sizeof last); double lh = 14 * 1.5;
      for (size_t i = 0; i < n; i++) { int base = baseline_in(f, a->ptext.y0 + i * lh, lh); if (i == n - 1 && last[0]) T(fb, FULL, f, a->ptext.x0, base, last, 0xEFE7DA, 255, 0); else text_draw(fb, NULL, FULL, f, a->ptext.x0, base, g->mblurb + ln[i].start, ln[i].len, RGBA(0xEFE7DA, 255), 0, 0); } }
    gfx_fill(fb, FULL, a->prule.x0, a->prule.y0, a->prule.x1 - a->prule.x0, 2, RGBA(RED, 255));
    { const font *s = &h->f[F_SYM13]; int sw = text_w(s, "\xE2\x98\x85", 3, 0); T(fb, FULL, s, (a->prule.x0 + a->prule.x1) / 2.0 - sw / 2.0, baseline_normal(s, a->prule.y0 - 9), "\xE2\x98\x85", RED2, 255, 0); }
    int start = sel - 3; if (start > (int)h->n - a->ncells) start = (int)h->n - a->ncells; if (start < 0) start = 0;
    const tsprite *al = tsp(h, "arrow-l"), *ar = tsp(h, "arrow-r");
    if (al) gfx_image(fb, FULL, &al->im, al->box.x0, al->box.y0, start == 0 ? 89 : 255); if (ar) gfx_image(fb, FULL, &ar->im, ar->box.x0, ar->box.y0, start + a->ncells >= (int)h->n ? 89 : 255);
    for (int k = 0; k < a->ncells; k++) {   /* the shelf: whole covers with the title band, the selected one in the authored focus frame */
        int i = start + k; if (i >= (int)h->n) break; box4 c = a->cells[k], art; draw_cover(h, fb, i, c, i == sel, &art);
        const font *f = &h->f[F_PX7]; char up[300], fit[300]; upper(h->g[i].title, up, sizeof up); text_fit(f, up, c.x1 - c.x0 - 14, 0, fit, sizeof fit); int bandh = 5 + iround(f->px * 1.5) + 5;
        stop band[2] = { { 0, 0, .78 }, { 1, 0, 0 } }; gfx_gradient(fb, FULL, c.x0, c.y0, c.x1 - c.x0, bandh, 1, band, 2);
        int base = baseline_in(f, c.y0 + 5, f->px * 1.5); T(fb, FULL, f, c.x0 + 7, base + 1, fit, 0, 255, 0); T(fb, FULL, f, c.x0 + 7, base, fit, 0xFFFFFF, 255, 0);
        box4 fr = { c.x0 - 10, c.y0 - 16, c.x1 + 10, c.y1 + 16 };
        if (i == sel) { sprite_box(h, fb, "shelf-focus", fr, h->nav.zone == Z_GAMES ? 255 : 153, 1); LR(h, h->nav.zone == Z_GAMES ? "shelf-focus" : "shelf-selected", fr.x0, fr.y0, fr.x1, fr.y1); }
        else sprite_box(h, fb, "shelf-frame", fr, 255, 1);
        LR(h, "shelf-art", art.x0, art.y0, art.x1, art.y1);
    }
}
static void mp_render(home *h, framebuffer *fb, uint64_t now) {
    map *a = MP(h); (void)now;
    long key = (long)h->gen;   /* every input and section change bumps it */
    if (key != a->key || h->rec) { mp_draw(h, &a->frame); legend_items(h, &a->frame, a->legend, 1, TH_MAP); a->key = h->rec ? -1 : key; }
    memcpy(fb->rgb, a->frame.rgb, (size_t)fb->w * fb->h * 3);
}
static void mp_input(home *h, button b) {
    if (h->nav.zone == Z_MAP) {   /* HOME_MAP_NEIGHBORS: the path runs left to right through the six landmarks */
        if (b == B_LEFT && h->nav.landmark > 0) h->nav.landmark--; else if (b == B_RIGHT && h->nav.landmark < NLM - 1) h->nav.landmark++;
        else if (b == B_DOWN || b == B_B) h->nav.zone = home_section_zone(h);
        else if (b == B_SELECT) { extern void home_open_mapdest(home *h); home_open_mapdest(h); }
        else if (b == B_A) { int k = h->nav.landmark;
            if (k == 0) home_go_all(h);
            else if (k == 1) home_go_browse(h, -1);
            else if (k == 3 || k == 4) { if (h->map_folder[k - 3] >= 0) home_go_folder(h, h->map_folder[k - 3]); else notice(h, "NO FOLDER SET", "No folder is assigned to this shortcut. Assign one in Classic Cart Studio for Windows."); }
            else not_yet(h, k == 2 ? "Favorites" : "Settings");
            h->nav.landmark = k; }
        return;
    }
    if (b == B_LEFT) { if (h->nav.sel > 0) h->nav.sel--; } else if (b == B_RIGHT) { if (h->nav.sel < (int)h->n - 1) h->nav.sel++; }
    else if (b == B_UP) h->nav.zone = Z_MAP;
    else if (b == B_A) launch_unavailable(h, h->nav.sel);
    else if (b == B_SELECT) { extern int home_open_details_sel(home *h); home_open_details_sel(h); }
}
static const dstyle MAP_DETAILS = {
    .border = 4, .border_rgb = 0x6B4A2B, .inset = 2, .inset_rgb = RED2,
    .hz = { { 0, 0x100A08, .14 }, { .28, 0x100A08, .06 }, { .40, 0x100A08, .60 }, { 1, 0x100A08, .72 } }, .vt = { { 0, 0, .20 }, { .22, 0, 0 }, { .72, 0, 0 }, { 1, 0x100A08, .72 } }, .pixelated = 1,
    .title_font = F_PX22, .title_lh = 1.4, .title_rgb = 0xF4E9D6, .title_spacing = 0,
    .ver_bold = F_S700_15, .ver_font = F_S400_15, .meta_key = F_MONO700_16, .meta_val = F_MONO16, .meta_rowh = 23.2,
    .act_font = F_MONO20, .act_radius = 3, .act_bw = 3, .act_bg = 0x100A08, .act_bg_a = 230, .act_border = 0x6B4A2B, .act_border_a = 255,
    .desc_font = F_MONO19, .desc_lh = 1.55, .desc_radius = 3, .desc_bw = 3, .desc_bg = 0x100A08, .desc_bg_a = 204, .desc_border = 0x6B4A2B, .desc_border_a = 255,
    .dh_font = F_MONO700_13, .dh_lh = 1.4, .dh_spacing = 1.6, .dh_bold = 0,
    .cover_radius = 2, .cover_ring = 3, .cover_ring_rgb = 0x6B4A2B, .cover_ring_a = 255, .note_font = F_S400_14 };
const theme_ops THEME_MAP = { "adventure-map", "Adventure Map", mp_init, mp_fini, mp_render, mp_input, &MAP_DETAILS, 1 };
