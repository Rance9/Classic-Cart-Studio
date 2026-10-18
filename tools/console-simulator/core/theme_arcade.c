/* Arcade Grid (N1, N1.1): the large selected card integrated in a 16-cell cover grid, the tab bar, the collection stats.
 * home-themes.js hmArcade + home.css .hm-arcade-grid; geometry measured on the master (home-art.json arcade-grid).
 *
 * Navigation (N1.1): a page shows 16 games in the 16 cover cells in reading order (row A five, row B five, row C six starting
 * under the card); the selection is a listing index and everything derives from it (page = sel / 16, slot = sel % 16, the
 * card previews the selected game). A move goes to the cell visually in that direction; Up/Down past the first or last row
 * turn the page; Up from the first row of the first page enters the tabs. No wrap. */
#include "home_int.h"

static const char *NAV_LABEL[6] = { "ALL GAMES", "BROWSE", "SEARCH", "FAVORITES", "RECENT", "SETTINGS" };
static const char *NAV_NAME[6] = { "All Games", "Browse", "Search", "Favorites", "Recently Played", "Settings" };
static const char *NAV_ICON[6] = { "icon-games", "icon-folder", NULL, "icon-star", "icon-clock", "icon-gear" };

typedef struct {
    box4 tabbar, card_frame, card_cover, card_caption, cells[MAX_CELLS], legend, stat_rows[3]; int ncells, tab_active_w, stat_icon_x, cell_row[MAX_CELLS];
    image badge_a, tab_icons[6], stat_icons[3]; framebuffer chrome; int chrome_key;
} arcade;
#define A(h) ((arcade *)(h)->tstate)

static void build_rows(arcade *a) {
    int tops[MAX_CELLS], nt = 0;
    for (int i = 0; i < a->ncells; i++) { int t = a->cells[i].y0, seen = 0; for (int k = 0; k < nt; k++) if (tops[k] == t) seen = 1; if (!seen) tops[nt++] = t; }
    for (int x = 0; x < nt; x++) for (int y = x + 1; y < nt; y++) if (tops[y] < tops[x]) { int t = tops[x]; tops[x] = tops[y]; tops[y] = t; }
    for (int i = 0; i < a->ncells; i++) for (int k = 0; k < nt; k++) if (tops[k] == a->cells[i].y0) a->cell_row[i] = k;
}
static int scaled(const home *h, const char *name, image *out, int w, int hh, int nearest) {
    const tsprite *s = tsp(h, name); if (!s) return -1; return nearest ? img_scale_nearest(&s->im, out, w, hh) : img_scale(&s->im, out, w, hh);
}
static int arcade_init(home *h, char *err, size_t errlen) {
    arcade *a = calloc(1, sizeof *a); if (!a) { snprintf(err, errlen, "out of memory"); return -1; } h->tstate = a; a->chrome_key = -1;
    const jval *g = h->geo, *card = json_get(g, "card"), *stats = json_get(g, "stats"), *cells = json_get(g, "cells");
    if (!card || !stats || !cells || cells->t != J_ARR || gbox(g, "tabbar", &a->tabbar) || gbox(g, "legend", &a->legend) || gbox(card, "frame", &a->card_frame) || gbox(card, "cover", &a->card_cover) || gbox(card, "caption", &a->card_caption)) { snprintf(err, errlen, "arcade-grid geometry incomplete"); return -1; }
    a->ncells = (int)(cells->n > MAX_CELLS ? MAX_CELLS : cells->n); for (int i = 0; i < a->ncells; i++) if (jbox_at(cells, (size_t)i, &a->cells[i])) { snprintf(err, errlen, "arcade cell %d malformed", i); return -1; }
    build_rows(a);
    const jval *rows = json_get(stats, "rows"); for (int i = 0; i < 3; i++) if (jbox_at(rows, (size_t)i, &a->stat_rows[i])) { snprintf(err, errlen, "arcade stats rows missing"); return -1; }
    a->stat_icon_x = gnum(stats, "icon_x", 35); a->tab_active_w = gnum(g, "tab_active_w", 158);
    if (!tsp(h, "card-frame") || !tsp(h, "tab-bar") || !tsp(h, "tab-active") || scaled(h, "badge-a", &a->badge_a, 30, 30, 0)) { snprintf(err, errlen, "arcade-grid sprites missing"); return -1; }
    for (int i = 0; i < 6; i++) if (NAV_ICON[i] && scaled(h, NAV_ICON[i], &a->tab_icons[i], 22, 22, 1)) { snprintf(err, errlen, "arcade sprite %s missing", NAV_ICON[i]); return -1; }
    static const char *SI[3] = { "stat-icon-games", "icon-folder", "icon-star" };
    for (int i = 0; i < 3; i++) if (scaled(h, SI[i], &a->stat_icons[i], 30, 28, 0)) { snprintf(err, errlen, "arcade sprite %s missing", SI[i]); return -1; }
    if (fb_init(&a->chrome, CCS_FB_W, CCS_FB_H)) { snprintf(err, errlen, "out of memory (chrome)"); return -1; }
    return 0;
}
static void arcade_fini(home *h) {
    arcade *a = A(h); if (!a) return; img_free(&a->badge_a); for (int i = 0; i < 6; i++) img_free(&a->tab_icons[i]); for (int i = 0; i < 3; i++) img_free(&a->stat_icons[i]);
    if (a->chrome.rgb) fb_free(&a->chrome); free(a); h->tstate = NULL;
}

static int row_count(const arcade *a) { int r = 0; for (int i = 0; i < a->ncells; i++) if (a->cell_row[i] + 1 > r) r = a->cell_row[i] + 1; return r; }
static int cx2(box4 b) { return b.x0 + b.x1; }
static int nearest_in_row(const arcade *a, int r, int rx2, int avail) {
    int best = -1, bd = 0;
    for (int k = 0; k < a->ncells && k < avail; k++) { if (a->cell_row[k] != r) continue; int d = cx2(a->cells[k]) - rx2; if (d < 0) d = -d; if (best < 0 || d < bd) { best = k; bd = d; } }
    return best;
}
static int page_size(const arcade *a) { return a->ncells; }
static int page_count(const home *h) { int S = page_size(A(h)); return ((int)h->n + S - 1) / S; }
static int page_avail(const home *h, int page) { int S = page_size(A(h)), x = (int)h->n - page * S; return x < 0 ? 0 : x > S ? S : x; }
static int grid_move(home *h, int dir, int *enter_tabs) {
    arcade *a = A(h); int S = page_size(a), page = h->nav.sel / S, slot = h->nav.sel % S, avail = page_avail(h, page), r = a->cell_row[slot], rows = row_count(a); box4 c = a->cells[slot];
    *enter_tabs = 0;
    if (dir == D_LEFT || dir == D_RIGHT) {
        int best = -1, bd = 0;
        for (int k = 0; k < avail; k++) { if (a->cell_row[k] != r || k == slot) continue; int d = cx2(a->cells[k]) - cx2(c); if ((dir == D_RIGHT && d <= 0) || (dir == D_LEFT && d >= 0)) continue; if (d < 0) d = -d; if (best < 0 || d < bd) { best = k; bd = d; } }
        if (best < 0) return 0; h->nav.sel = page * S + best; return 1;
    }
    int tr = r + (dir == D_DOWN ? 1 : -1), tpage = page;
    if (tr < 0) { if (page == 0) { *enter_tabs = 1; return 0; } tpage = page - 1; tr = rows - 1; }
    else if (tr >= rows) { if (page + 1 >= page_count(h)) return 0; tpage = page + 1; tr = 0; }
    int k = nearest_in_row(a, tr, cx2(c), page_avail(h, tpage)); if (k < 0) return 0;
    h->nav.sel = tpage * S + k; return 1;
}
static void arcade_input(home *h, button b) {
    if (h->nav.zone == Z_NAV) {   /* the tab bar: Left / Right between tabs; Down or B back to the selected game */
        if (b == B_LEFT && h->nav.nav > 0) h->nav.nav--; else if (b == B_RIGHT && h->nav.nav < 5) h->nav.nav++;
        else if (b == B_DOWN || b == B_B) h->nav.zone = home_section_zone(h);
        else if (b == B_A) { if (h->nav.nav == 0) home_go_all(h); else if (h->nav.nav == 1) home_go_browse(h, -1); else not_yet(h, NAV_NAME[h->nav.nav]); }
        return;
    }
    int dir = dir_of(b);
    if (dir >= 0) { int tabs; grid_move(h, dir, &tabs); if (tabs) { h->nav.zone = Z_NAV; h->nav.nav = home_active_nav(h); } }
    else if (b == B_A) launch_unavailable(h, h->nav.sel);
    else if (b == B_SELECT) { extern int home_open_details_sel(home *h); home_open_details_sel(h); }
}
void arcade_state_json(home *h, char *out, size_t n) {   /* the paging fields the N1.1 tests read */
    arcade *a = A(h); if (!a) { out[0] = 0; return; } int S = page_size(a), page = h->nav.sel / S, slot = h->nav.sel % S; size_t k = 0;
    k += (size_t)snprintf(out + k, n - k, ",\"page\":%d,\"pages\":%d,\"slot\":%d,\"row\":%d,\"page_start\":%d,\"tab\":%d,\"cells\":[", page, page_count(h), slot, a->cell_row[slot], page * S, h->nav.nav);
    for (int i = 0; i < page_avail(h, page) && k < n - 200; i++) { char e[160]; json_escape(h->g[page * S + i].id, e, sizeof e); k += (size_t)snprintf(out + k, n - k, "%s%s", i ? "," : "", e); }
    snprintf(out + k, n - k, "]");
}

static void build_chrome(home *h, int filled) {
    arcade *a = A(h); framebuffer *fb = &a->chrome; memcpy(fb->rgb, h->scene.px, (size_t)fb->w * fb->h * 3);
    const jval *cn = json_get(h->pk, "counts"); char v[3][16]; snprintf(v[0], 16, "%zu", h->nall); snprintf(v[1], 16, "%d", gnum(cn, "folders", 0)); snprintf(v[2], 16, "%d", gnum(cn, "favorites", 0));   /* the collection's own counts */ static const char *L[3] = { "GAMES", "FOLDERS", "FAVORITES" };
    for (int i = 0; i < 3; i++) {
        box4 r = a->stat_rows[i]; gfx_image(fb, FULL, &a->stat_icons[i], a->stat_icon_x + 2, r.y0 + 2, 255);
        const font *x = &h->f[F_SILK9], *y = &h->f[F_SILK17]; T(fb, FULL, x, r.x0, baseline_normal(x, r.y0), L[i], 0xDFE3EA, 255, .3); T(fb, FULL, y, r.x0, baseline_normal(y, r.y0 + lh_normal(x) + 5), v[i], 0xFFFFFF, 255, 0);
    }
    a->chrome_key = filled;
}
static void render_tabs(home *h, framebuffer *fb) {
    arcade *a = A(h); box4 tb = a->tabbar; const tsprite *bar = tsp(h, "tab-bar"), *act = tsp(h, "tab-active");
    gfx_image(fb, FULL, &bar->im, bar->box.x0, bar->box.y0, 255);
    double aw = a->tab_active_w, rest = (tb.x1 - tb.x0 - 6 - aw) / 5.0; int active = home_active_nav(h), H = tb.y1 - tb.y0;
    for (int i = 0; i < 6; i++) {
        double tw = i == 0 ? aw : rest, x = i == 0 ? tb.x0 + 3 : tb.x0 + 3 + aw + (i - 1) * rest; int isA = i == active, foc = h->nav.zone == Z_NAV && h->nav.screen == SCR_GRID && i == h->nav.nav;
        if (isA) { image s; if (act->im.w == iround(tw) && act->im.h == H - 6) gfx_image(fb, FULL, &act->im, iround(x), tb.y0 + 3, 255); else if (!img_scale(&act->im, &s, iround(tw), H - 6)) { gfx_image(fb, FULL, &s, iround(x), tb.y0 + 3, 255); img_free(&s); } }
        if (foc) { gfx_shadow(fb, FULL, iround(x) + 4, tb.y0 + 5, iround(tw) - 8, H - 10, 6, RGBA(RED, 150)); gfx_rrect_ring(fb, FULL, iround(x) + 4, tb.y0 + 5, iround(tw) - 8, H - 10, 7, 2, RGBA(0xFFFFFF, 255)); }
        LR(h, foc ? "tab-focus" : "tab", iround(x), tb.y0, iround(x + tw), tb.y1);
        const font *f = &h->f[F_SILK11]; int lw = text_w(f, NAV_LABEL[i], strlen(NAV_LABEL[i]), .3), iw = NAV_ICON[i] ? 22 : text_w(&h->f[F_SYM16], "\xE2\x8C\x95", 3, 0), cw = iw + 8 + lw;
        double cx = x + (tw - cw) / 2.0; int base = baseline_center(f, tb.y0, H); uint32_t col = isA || foc ? 0xFFFFFF : 0xCFD6E2;
        if (NAV_ICON[i]) gfx_image(fb, FULL, &a->tab_icons[i], iround(cx), tb.y0 + (H - 22) / 2, 255);
        else { const font *s = &h->f[F_SYM16]; if (foc) draw_glow_text(fb, h, F_SYM16, cx, baseline_center(s, tb.y0, H), "\xE2\x8C\x95", 3, 5, RGBA(RED, 230), 0); T(fb, FULL, s, cx, baseline_center(s, tb.y0, H), "\xE2\x8C\x95", col, 255, 0); }
        if (foc) draw_glow_text(fb, h, F_SILK11, cx + iw + 8, base, NAV_LABEL[i], strlen(NAV_LABEL[i]), 5, RGBA(RED, 230), .3);
        T(fb, FULL, f, cx + iw + 8, base, NAV_LABEL[i], col, 255, .3); LRtext(h, "tab-label", f, cx, base, NAV_LABEL[i], strlen(NAV_LABEL[i]), .3);
        if (i < 5 && !isA && active != i + 1 && !foc && !(h->nav.zone == Z_NAV && h->nav.nav == i + 1)) gfx_fill(fb, FULL, iround(x + tw - 1), tb.y0 + 10, 1, 28, RGBA(0xFFFFFF, 56));
    }
}
static void caption_small(home *h, const hgame *g, int w, char *out, size_t n) {
    const font *fs = &h->f[F_S400_10]; char ply[40] = "", pos[64], gen[160]; const char *SEP = " \xC2\xB7 ";
    if (g->players) snprintf(ply, sizeof ply, "%s player%s", g->players, strcmp(g->players, "1") ? "s" : "");
    for (int variant = 0; variant < 3; variant++) {
        snprintf(pos, sizeof pos, "%s%s%d/%zu", variant == 0 ? home_section_title(h) : "", variant == 0 ? " " : "", h->nav.sel + 1, h->n); snprintf(gen, sizeof gen, "%s", g->genre ? g->genre : "");
        const char *parts[4] = { g->players ? ply : NULL, gen[0] ? gen : NULL, g->year, pos };
        if (variant == 2 && gen[0]) {
            char rest[200]; size_t k = 0; rest[0] = 0; for (int i = 0; i < 4; i++) if (parts[i] && i != 1) k += (size_t)snprintf(rest + k, sizeof rest - k, "%s%s", k ? SEP : "", parts[i]);
            int left = w - text_w(fs, rest, strlen(rest), .3) - text_w(fs, SEP, strlen(SEP), .3); char fit[160];
            if (left < 24) gen[0] = 0; else { text_fit(fs, g->genre, left, .3, fit, sizeof fit); snprintf(gen, sizeof gen, "%s", fit); }
            parts[1] = gen[0] ? gen : NULL;
        }
        size_t k = 0; out[0] = 0;
        for (int i = 0; i < 4; i++) if (parts[i]) k += (size_t)snprintf(out + k, n > k ? n - k : 0, "%s%s", k ? SEP : "", parts[i]);
        if (text_w(fs, out, strlen(out), .3) <= w) return;
    }
    char fit[400]; text_fit(fs, out, w, .3, fit, sizeof fit); snprintf(out, n, "%s", fit);
}
static void render_caption(home *h, framebuffer *fb, uint64_t now) {
    arcade *a = A(h); hgame *g = &h->g[h->nav.sel]; box4 c = a->card_caption; int x = c.x0 + 14, w = c.x1 - c.x0 - 28;
    const font *ft = &h->f[F_S700_14], *fs = &h->f[F_S400_10];
    double inner0 = c.y0 + 1, inner1 = c.y1 - 3, content = lh_normal(ft) + 5 + lh_normal(fs); int top = iround(inner0 + (inner1 - inner0 - content) / 2);
    int base = baseline_normal(ft, top); rect clip = { x, top, x + w, top + iround(lh_normal(ft)) };
    int tw = text_w(ft, g->title, strlen(g->title), 0);
    if (tw <= w) { T(fb, clip, ft, x, base, g->title, 0xFFFFFF, 255, 0); LR(h, "caption-title", x, base - ft->ascent, x + tw, base + ft->descent); }
    else {
        char mq[600]; snprintf(mq, sizeof mq, "%s\xC2\xA0\xC2\xA0\xC2\xA0\xC2\xA0%s", g->title, g->title); int spanw = text_w(ft, mq, strlen(mq), 0) + 40;
        double dur = (double)strlen(g->title) / 4.0 * 1000; if (dur < 6000) dur = 6000; double t = (double)(now - h->changed_at) - 900, off = 0;
        if (t > 0) { double ph = t / dur; ph -= (double)(int64_t)ph; if (ph > 0.1) off = -(spanw / 2.0 + 20) * (ph - 0.1) / 0.9; }
        text_draw(fb, NULL, clip, ft, x + off, base, mq, strlen(mq), RGBA(0xFFFFFF, 255), 0, 0); LR(h, "caption-title", clip.x0, clip.y0, clip.x1, clip.y1);
    }
    char sm[400]; caption_small(h, g, w, sm, sizeof sm); double y2 = top + lh_normal(ft) + 5; int b2 = baseline_normal(fs, y2);
    text_draw(fb, NULL, FULL, fs, x, b2, sm, strlen(sm), RGBA(0xD8DBE3, 255), .3, 0); LRtext(h, "caption-meta", fs, x, b2, sm, strlen(sm), .3);
    LR(h, "caption-box", c.x0, c.y0, c.x1, c.y1);
}
static void prefetch(home *h) {
    if (!h->budget) return; int S = page_size(A(h)), page = h->nav.sel / S, pages = page_count(h);
    for (int dir = 1; dir >= -1; dir -= 2) {
        int p2 = page + dir; if (p2 < 0 || p2 >= pages) continue;
        for (int i = p2 * S; i < p2 * S + page_avail(h, p2); i++) { extern int home_prefetch_bytes(home *h, int gi); if (h->decoded >= h->budget * 2) return; if (home_prefetch_bytes(h, i)) h->decoded++; }   /* a file read costs about half a decode */
    }
}
static int box_hit(box4 x, box4 y) { return x.x0 < y.x1 && y.x0 < x.x1 && x.y0 < y.y1 && y.y0 < x.y1; }
static box4 focus_ring_box(const home *h, int slot, box4 c) {
    arcade *a = A(h); box4 r = c; const int d = 4;
    for (int side = 0; side < 4; side++) {
        box4 band = c; if (side == 0) { band.y1 = c.y0; band.y0 = c.y0 - d; } else if (side == 1) { band.y0 = c.y1; band.y1 = c.y1 + d; } else if (side == 2) { band.x1 = c.x0; band.x0 = c.x0 - d; } else { band.x0 = c.x1; band.x1 = c.x1 + d; }
        int blocked = box_hit(band, a->card_frame) || band.x0 < 0 || band.y0 < 0 || band.x1 > CCS_FB_W || band.y1 > CCS_FB_H;
        for (int k = 0; k < a->ncells && !blocked; k++) if (k != slot && box_hit(band, a->cells[k])) blocked = 1;
        if (!blocked) { if (side == 0) r.y0 -= d; else if (side == 1) r.y1 += d; else if (side == 2) r.x0 -= d; else r.x1 += d; }
    }
    return r;
}
static void arcade_render(home *h, framebuffer *fb, uint64_t now) {
    arcade *a = A(h); int S = page_size(a), page = h->nav.sel / S, slot = h->nav.sel % S, filled = page_avail(h, page), focus_games = h->nav.zone == Z_GAMES;
    if (filled != a->chrome_key) build_chrome(h, filled);
    memcpy(fb->rgb, a->chrome.rgb, (size_t)fb->w * fb->h * 3);
    render_tabs(h, fb);   /* no clock: the console cannot keep reliable calendar time across power cycles, so HOME shows none */
    if (h->n) {
        box4 cv = a->card_cover; int ox, oy; const image *im = cover(h, h->nav.sel, cv.x1 - cv.x0, cv.y1 - cv.y0, 1, &ox, &oy);
        gfx_image(fb, FULL, im, cv.x0 + ox, cv.y0 + oy, 255); LR(h, "card-art", cv.x0 + ox, cv.y0 + oy, cv.x0 + ox + im->w, cv.y0 + oy + im->h);
        const tsprite *fr = tsp(h, "card-frame"), *trim = tsp(h, "card-trim");
        gfx_image(fb, FULL, &fr->im, fr->box.x0, fr->box.y0, 255); if (trim) gfx_image(fb, FULL, &trim->im, trim->box.x0, trim->box.y0, 255);
        LR(h, "card", a->card_frame.x0, a->card_frame.y0, a->card_frame.x1, a->card_frame.y1); render_caption(h, fb, now);
        const image *arts[MAX_CELLS] = { 0 }; box4 art[MAX_CELLS];
        for (int k = 0; k < filled; k++) {
            int kk = k == 0 ? slot : (k <= slot ? k - 1 : k); box4 c = a->cells[kk];
            arts[kk] = cover_b(h, page * S + kk, c.x1 - c.x0, c.y1 - c.y0, 1, &ox, &oy, kk == slot);
            if (arts[kk]) { art[kk].x0 = c.x0 + ox; art[kk].y0 = c.y0 + oy; art[kk].x1 = art[kk].x0 + arts[kk]->w; art[kk].y1 = art[kk].y0 + arts[kk]->h; } else h->pending++;
        }
        for (int k = 0; k < filled; k++) if (arts[k]) gfx_shadow(fb, FULL, art[k].x0, art[k].y0 + 4, art[k].x1 - art[k].x0, art[k].y1 - art[k].y0, 5, RGBA(0, 128));
        box4 sa = arts[slot] ? art[slot] : a->cells[slot];
        if (focus_games) gfx_shadow(fb, FULL, sa.x0, sa.y0, sa.x1 - sa.x0, sa.y1 - sa.y0, 11, RGBA(RED, 204));
        for (int k = 0; k < filled; k++) { if (arts[k]) { gfx_image(fb, FULL, arts[k], art[k].x0, art[k].y0, 255); LR(h, "cell-art", art[k].x0, art[k].y0, art[k].x1, art[k].y1); } LR(h, "cell", a->cells[k].x0, a->cells[k].y0, a->cells[k].x1, a->cells[k].y1); }
        box4 rb = focus_ring_box(h, slot, sa);
        if (focus_games) {
            gfx_rrect_ring(fb, FULL, rb.x0, rb.y0, rb.x1 - rb.x0, rb.y1 - rb.y0, 0, 3, RGBA(RED, 255));
            gfx_rrect_ring(fb, FULL, rb.x0 + 3, rb.y0 + 3, rb.x1 - rb.x0 - 6, rb.y1 - rb.y0 - 6, 0, 1, RGBA(0x000000, 230));
            LR(h, "focus-ring", rb.x0, rb.y0, rb.x1, rb.y1);
        } else { gfx_rrect_ring(fb, FULL, rb.x0 + 2, rb.y0 + 2, rb.x1 - rb.x0 - 4, rb.y1 - rb.y0 - 4, 0, 2, RGBA(0xFFFFFF, 160)); LR(h, "selected-ring", rb.x0, rb.y0, rb.x1, rb.y1); }
    }
    legend_items(h, fb, a->legend, 0, TH_ARCADE);
    if (!h->pending) prefetch(h);
}

static const dstyle ARCADE_DETAILS = {
    .border = 4, .border_rgb = NAVY, .inset = 2, .inset_rgb = 0x070A22,
    .hz = { { 0, 0x06091E, .15 }, { .28, 0x06091E, .08 }, { .40, 0x06091E, .6 }, { 1, 0x06091E, .7 } },
    .vt = { { 0, 0x000000, .2 }, { .22, 0x000000, 0 }, { .72, 0x000000, 0 }, { 1, 0x06091E, .7 } }, .pixelated = 1,
    .title_font = F_S800_40, .title_lh = 1.12, .title_rgb = 0xFFFFFF, .title_spacing = 0,
    .ver_bold = F_S700_15, .ver_font = F_S400_15, .meta_key = F_S700_16, .meta_val = F_S400_16, .meta_rowh = 23.2,
    .act_font = F_S400_20, .act_radius = 4, .act_bw = 2, .act_bg = 0x0F1538, .act_bg_a = 255, .act_border = NAVY, .act_border_a = 255,
    .desc_font = F_S400_19, .desc_lh = 1.55, .desc_radius = 4, .desc_bw = 2, .desc_bg = 0x090D26, .desc_bg_a = 184, .desc_border = NAVY, .desc_border_a = 255,
    .dh_font = F_S700_13, .dh_lh = 1.4, .dh_spacing = 1.6, .dh_bold = 0,
    .cover_radius = 6, .cover_ring = 3, .cover_ring_rgb = NAVY, .cover_ring_a = 255, .note_font = F_S400_14 };
const theme_ops THEME_ARCADE = { "arcade-grid", "Arcade Grid", arcade_init, arcade_fini, arcade_render, arcade_input, &ARCADE_DETAILS, 0 };
