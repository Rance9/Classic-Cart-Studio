/* The HOME Designer's render check (design_check.h; tools/console-backup/HOME-DESIGNER.md "The render check").
 *
 * Two HOMEs are loaded from the same package: one with the theme defaults, one with the compiled design. In each of the four themes
 * every required screen and state is set up through the HOME's own controls (All Games, the longest title, the last game, Game
 * Details and a long title's Details, the START menu and its focus, the theme picker, Browse and a focused tile, a folder, an empty
 * folder, the navigation focus, a notice) and drawn twice: without any text (the picture under the text) and normally. Each drawn
 * text is measured against the picture under its own glyph pixels (the 10th percentile of their contrast ratios), and compared with
 * the same theme's own text: a design must not make the HOME's text harder to read than the theme itself has it (and never below
 * the theme's own weakest text), its labels need 4.5:1 (3:1 at 24 px and larger), its lettering must have every character it is
 * asked to draw, the shared menus must still fit their panels inside the screen's safe area, a design whose picture is missing must
 * come up exactly as the theme defaults, and the startup picture must decode at 1280x720. Memory: the decoded-picture and decoder
 * peaks of the start and of every state (design.h accounting). Frames: SHA-256 of each, and the frames themselves on request. */
#ifdef _WIN32
#include <windows.h>
#endif
#include "home_int.h"
#include "design.h"
#include "design_check.h"
#include <stdarg.h>
#include <errno.h>

int ccs_mkdirs(const char *dir);
extern int ccs_text_label;

/* ---------- a growing UTF-8 buffer ---------- */
typedef struct { char *b; size_t n, cap; int oom; } sb;
static void sbf(sb *s, const char *fmt, ...) {
    if (s->oom) return; va_list ap, ap2; va_start(ap, fmt); va_copy(ap2, ap); int k = vsnprintf(NULL, 0, fmt, ap); va_end(ap); if (k < 0) { va_end(ap2); return; }
    if (s->n + (size_t)k + 1 > s->cap) { size_t c = s->cap ? s->cap * 2 : 8192; while (c < s->n + (size_t)k + 1) c *= 2; char *nb = realloc(s->b, c); if (!nb) { s->oom = 1; va_end(ap2); return; } s->b = nb; s->cap = c; }
    vsnprintf(s->b + s->n, (size_t)k + 1, fmt, ap2); va_end(ap2); s->n += (size_t)k;
}
static void sbs(sb *s, const char *str) { char e[1300]; json_escape(str ? str : "", e, sizeof e); sbf(s, "%s", e); }

/* ---------- files (UTF-8 paths; wide on Windows) ---------- */
static FILE *ufopen(const char *path, const char *mode) {
#ifdef _WIN32
    wchar_t wp[1100], wm[8]; if (!MultiByteToWideChar(CP_UTF8, 0, path, -1, wp, 1100) || !MultiByteToWideChar(CP_UTF8, 0, mode, -1, wm, 8)) return NULL; return _wfopen(wp, wm);
#else
    return fopen(path, mode);
#endif
}
static int copy_file(const char *from, const char *to) {
    uint8_t *b; size_t len; if (ccs_read_file(from, &b, &len, DESIGN_MAX_JSON_BYTES > DESIGN_MAX_FILE_BYTES ? DESIGN_MAX_JSON_BYTES : DESIGN_MAX_FILE_BYTES + 1)) return -1;
    FILE *f = ufopen(to, "wb"); int ok = f && fwrite(b, 1, len, f) == len; if (f && fclose(f)) ok = 0; free(b); return ok ? 0 : -1;
}
static int write_ppm(const char *path, const uint8_t *rgb, int w, int h) {
    FILE *f = ufopen(path, "wb"); if (!f) return -1; fprintf(f, "P6\n%d %d\n255\n", w, h); int ok = fwrite(rgb, 1, (size_t)w * h * 3, f) == (size_t)w * h * 3; if (fclose(f)) ok = 0; return ok ? 0 : -1;
}

/* ---------- what the probes saw ---------- */
typedef struct { char s[64]; int px, p10, npx, label; uint32_t rgb; int box[4]; } trec;
typedef struct { trec *r; int n, cap; uint32_t miss[32]; int miss_px[32], nmiss; } tlog;
static tlog *g_log;
static void rec_text(const char *s, size_t len, const font *f, color c, const int box[4], const uint16_t *hist, int npx) {
    tlog *L = g_log; if (!L || npx <= 0) return;
    if (L->n == L->cap) { int nc = L->cap ? L->cap * 2 : 256; trec *nr = realloc(L->r, (size_t)nc * sizeof *nr); if (!nr) return; L->r = nr; L->cap = nc; }
    trec *r = &L->r[L->n++]; size_t k = len < sizeof r->s - 1 ? len : sizeof r->s - 1; memcpy(r->s, s, k); r->s[k] = 0;
    if (k < len) { while (k && ((unsigned char)r->s[k - 1] & 0xC0) == 0x80) r->s[--k] = 0; if (k && (unsigned char)r->s[k - 1] >= 0xC0) r->s[--k] = 0; }   /* cut: whole characters only */
    int need = (npx + 9) / 10, acc = 0, p = 63; for (int i = 0; i < 64; i++) { acc += hist[i]; if (acc >= need) { p = i; break; } }
    r->px = f->px; r->p10 = p; r->npx = npx; r->label = ccs_text_label; r->rgb = c.rgb; memcpy(r->box, box, sizeof r->box);
}
static void rec_missing(const font *f, uint32_t cp) { tlog *L = g_log; if (!L) return; for (int i = 0; i < L->nmiss; i++) if (L->miss[i] == cp && L->miss_px[i] == f->px) return; if (L->nmiss < 32) { L->miss[L->nmiss] = cp; L->miss_px[L->nmiss++] = f->px; } }
static int g_overlay_from = -1;
static void mark_overlay(void) { if (g_log && g_overlay_from < 0) g_overlay_from = g_log->n; }
static void probes_on(tlog *L, const uint8_t *bg) { g_log = L; ccs_text_rec = rec_text; ccs_missing_glyph = rec_missing; ccs_text_bg = bg; g_overlay_from = -1; ccs_overlay_begin = mark_overlay; }
static void probes_off(void) { g_log = NULL; ccs_text_rec = NULL; ccs_missing_glyph = NULL; ccs_text_bg = NULL; ccs_text_off = 0; ccs_overlay_begin = NULL; }

/* ---------- the states, set up through the HOME's own controls ---------- */
enum { ST_ALL, ST_LONG, ST_LAST, ST_DETAILS, ST_DETAILS_LONG, ST_MENU, ST_MENU_FOCUS, ST_THEME, ST_BROWSE, ST_BROWSE_FOCUS, ST_FOLDER, ST_FOLDER_EMPTY, ST_NAV, ST_NOTICE, ST_COUNT };
static const char *ST_ID[ST_COUNT] = { "all-games", "long-title", "last-game", "details", "details-long-title", "menu", "menu-focus", "theme-picker", "browse", "browse-focus", "folder", "folder-empty", "navigation-focus", "notice" };
static const char *TH_ID[4] = { "arcade-grid", "box-art-showcase", "featured-shelf", "adventure-map" };
static uint64_t g_clock;
static void tap(home *h, button b) { g_clock += 50; home_input(h, b, IN_PRESS, g_clock); g_clock += 50; home_input(h, b, IN_RELEASE, g_clock); }
/* the same games in both HOMEs (a design may reorder them): the package's first and last game, and its longest title (ties: the smaller id) */
static char g_first[200], g_last[200], g_long[200];
static void pick_games(const home *h) {
    snprintf(g_first, sizeof g_first, "%s", h->nall ? h->all[0].id : ""); snprintf(g_last, sizeof g_last, "%s", h->nall ? h->all[h->nall - 1].id : ""); size_t bl = 0; g_long[0] = 0;
    for (size_t i = 0; i < h->nall; i++) { size_t l = strlen(h->all[i].title); if (l > bl || (l == bl && strcmp(h->all[i].id, g_long) < 0)) { bl = l; snprintf(g_long, sizeof g_long, "%s", h->all[i].id); } } }
static int index_of(const home *h, const char *id) { for (size_t i = 0; i < h->n; i++) if (!strcmp(h->g[i].id, id)) return (int)i; return 0; }
static int empty_folder(const home *h) { for (int i = 0; i < h->nfold; i++) if (!h->fold[i].ng && !h->fold[i].nchild) return i; return -1; }
static int first_folder(const home *h) { for (int i = 0; i < h->nfold; i++) if (h->fold[i].ng) return i; return h->nfold ? 0 : -1; }
/* 1: set up; 0: this state does not exist for this HOME (no folders, no empty folder) */
static int setup(home *h, int theme, int st) {
    home_set_theme(h, TH_ID[theme], 0); home_go(h, "all", 0); home_select(h, index_of(h, g_first)); home_invalidate(h);
    switch (st) {
    case ST_ALL: return 1;
    case ST_LONG: home_select(h, index_of(h, g_long)); return 1;
    case ST_LAST: home_select(h, index_of(h, g_last)); return 1;
    case ST_DETAILS: tap(h, B_SELECT); return h->nav.screen == SCR_DETAILS;
    case ST_DETAILS_LONG: home_select(h, index_of(h, g_long)); tap(h, B_SELECT); return h->nav.screen == SCR_DETAILS;
    case ST_MENU: tap(h, B_START); return 1;
    case ST_MENU_FOCUS: tap(h, B_START); tap(h, B_DOWN); tap(h, B_DOWN); tap(h, B_DOWN); return 1;
    case ST_THEME: tap(h, B_START); tap(h, B_DOWN); tap(h, B_DOWN); tap(h, B_A); return h->nav.overlay == OV_THEME;
    case ST_BROWSE: home_go(h, "browse", -1); return 1;
    case ST_BROWSE_FOCUS: home_go(h, "browse", -1); tap(h, B_RIGHT); return 1;
    case ST_FOLDER: { int f = first_folder(h); if (f < 0) return 0; home_go(h, "folder", f); return 1; }
    case ST_FOLDER_EMPTY: { int f = empty_folder(h); if (f < 0) return 0; home_go(h, "folder", f); return 1; }
    case ST_NAV: tap(h, B_UP); return 1;
    case ST_NOTICE: tap(h, B_START); for (int i = 0; i < 9; i++) tap(h, B_DOWN); tap(h, B_A); return h->nav.notice;
    }
    return 0;
}

typedef struct { int ok_setup; char sha[65]; tlog text; int panel[4], have_panel, overlay_from; size_t peak, live, stb_peak; sb controls; } shot;
static void shot_free(shot *s) { free(s->text.r); free(s->controls.b); memset(s, 0, sizeof *s); }
static void emitnull(const char *l) { (void)l; }

/* draws one state twice (no text, then normally with the probes) and keeps what the probes and the layout saw */
static int take(home *h, int theme, int st, framebuffer *fb, uint8_t *bg, shot *out) {
    memset(out, 0, sizeof *out);
    ccs_text_off = 1; if (!setup(h, theme, st)) { ccs_text_off = 0; return 0; }
    home_render(h, fb, g_clock + 100); memcpy(bg, fb->rgb, (size_t)CCS_FB_W * CCS_FB_H * 3); ccs_text_off = 0;
    ccs_mem_reset_peaks(); size_t live0 = ccs_mem_img_live;
    probes_on(&out->text, bg); setup(h, theme, st); g_overlay_from = -1; h->rec = 1; home_render(h, fb, g_clock + 100); h->rec = 0; out->overlay_from = g_overlay_from < 0 ? out->text.n : g_overlay_from; probes_off();
    out->peak = ccs_mem_total_peak; out->stb_peak = ccs_mem_stb_peak; out->live = ccs_mem_img_live; (void)live0;
    for (int i = 0; i < h->nrec; i++) { const char *n = h->recs[i].name; box4 b = h->recs[i].b; if (!strcmp(n, "menu") || !strcmp(n, "notice") || !strcmp(n, "theme-picker")) { out->panel[0] = b.x0; out->panel[1] = b.y0; out->panel[2] = b.x1; out->panel[3] = b.y1; out->have_panel = 1; }
        sbf(&out->controls, "%s[\"%s\",%d,%d,%d,%d]", i ? "," : "", n, b.x0, b.y0, b.x1, b.y1); }   /* where the HOME draws its own controls (the editor shows them) */
    ccs_sha256_hex(fb->rgb, (size_t)CCS_FB_W * CCS_FB_H * 3, out->sha); out->ok_setup = 1; return 1;
}
static const trec *match(const tlog *L, const trec *r) { for (int i = 0; i < L->n; i++) { const trec *d = &L->r[i]; if (!strcmp(d->s, r->s) && !memcmp(d->box, r->box, sizeof d->box)) return d; } return NULL; }
/* the same text where the theme draws it, for a text the design moved with its control group (off: each group's move; NULL none) */
static const trec *match_moved(const tlog *L, const trec *r, const int (*off)[2]) {
    if (!off) return NULL;
    for (int u = 0; u < UI_COUNT; u++) { if (!off[u][0] && !off[u][1]) continue;
        for (int i = 0; i < L->n; i++) { const trec *d = &L->r[i];
            if (!strcmp(d->s, r->s) && d->box[0] + off[u][0] == r->box[0] && d->box[1] + off[u][1] == r->box[1] && d->box[2] + off[u][0] == r->box[2] && d->box[3] + off[u][1] == r->box[3]) return d; } }
    return NULL;
}
static int floor_of(const tlog *L) { int m = 45; for (int i = 0; i < L->n; i++) if (!L->r[i].label && L->r[i].p10 < m) m = L->r[i].p10; return m; }
static void cp_utf8(uint32_t cp, char *o) { if (cp < 0x80) { o[0] = (char)cp; o[1] = 0; } else if (cp < 0x800) { o[0] = (char)(0xC0 | cp >> 6); o[1] = (char)(0x80 | (cp & 63)); o[2] = 0; } else if (cp < 0x10000) { o[0] = (char)(0xE0 | cp >> 12); o[1] = (char)(0x80 | (cp >> 6 & 63)); o[2] = (char)(0x80 | (cp & 63)); o[3] = 0; } else { o[0] = (char)(0xF0 | cp >> 18); o[1] = (char)(0x80 | (cp >> 12 & 63)); o[2] = (char)(0x80 | (cp >> 6 & 63)); o[3] = (char)(0x80 | (cp & 63)); o[4] = 0; } }

/* the problems of one state of the design next to the same state of the defaults; returns how many */
static int judge(const shot *D, const shot *X, const char *theme, const char *state, sb *prob, int *first, sb *hard, const int (*off)[2]) {
    int n = 0, fl = floor_of(&D->text), nh = 0; char msg[700];
    #define P(...) do { snprintf(msg, sizeof msg, __VA_ARGS__); sbf(prob, "%s", *first ? "" : ","); *first = 0; sbs(prob, msg); n++; } while (0)
    for (int i = 0; i < X->text.n; i++) {
        const trec *r = &X->text.r[i]; if (r->label) continue;   /* the design's own labels are judged once, where they are composed */
        if (X->have_panel && i < X->overlay_from) continue;       /* the screen dimmed under a menu, a notice or the theme picker: read in its own states, not through the dimmer */
        const trec *d = match(&D->text, r); if (!d) d = match_moved(&D->text, r, off); int need = d ? (d->p10 < 45 ? d->p10 : 45) : (fl < 45 ? fl : 45);
        if (r->p10 + 1 < need) {   /* where, for the editor: the text's box on the 1280 x 720 screen */
            sbf(hard, "%s{\"text\":", nh++ ? "," : ""); sbs(hard, r->s); sbf(hard, ",\"box\":[%d,%d,%d,%d],\"contrast\":%d.%d,\"need\":%d.%d}", r->box[0], r->box[1], r->box[2], r->box[3], r->p10 / 10, r->p10 % 10, need / 10, need % 10);
            if (n < 6) P("%s, %s: the text \"%s\" is hard to read over the design (contrast %d.%d:1; the theme's own is %d.%d:1)", theme, state, r->s, r->p10 / 10, r->p10 % 10, need / 10, need % 10); }
    }
    for (int i = 0; i < X->text.nmiss; i++) {
        int seen = 0; for (int k = 0; k < D->text.nmiss; k++) if (D->text.miss[k] == X->text.miss[i] && D->text.miss_px[k] == X->text.miss_px[i]) seen = 1;
        if (!seen) { char c[8]; cp_utf8(X->text.miss[i], c); P("%s, %s: the %d px lettering has no character \"%s\" (it would show as ?)", theme, state, X->text.miss_px[i], c); }
    }
    if (X->have_panel) {
        const int *p = X->panel; if (p[0] < 12 || p[1] < 12 || p[2] > CCS_FB_W - 12 || p[3] > CCS_FB_H - 12) P("%s, %s: the panel reaches the screen's edge with this lettering", theme, state);
        for (int i = X->overlay_from; i < X->text.n; i++) { const trec *r = &X->text.r[i]; if (r->box[0] < p[0] || r->box[2] > p[2] || r->box[1] < p[1] || r->box[3] > p[3]) { P("%s, %s: the text \"%s\" does not fit its panel with this lettering", theme, state, r->s); break; } }
    }
    #undef P
    return n;
}

static int cancelled(const ccs_home_check_request *q) { return q->cancel && *q->cancel; }

/* each theme's control groups as this package lays them out, the controls that never move and the Browse panel (the PC's validator,
   DesignProfile, publishes the same numbers; the tests compare them) */
static void layout_json(sb *R, const jval *pk) {
    const jval *themes = json_get(pk, "themes"); sbf(R, ",\"layout\":{");
    for (int t = 0; t < 4; t++) {
        const jval *tj = themes ? json_get(themes, TH_ID[t]) : NULL, *g = tj ? json_get(tj, "geometry") : NULL; box4 b, fx[8]; const char *fn[8]; int k = 0;
        sbf(R, "%s\"%s\":{\"regions\":{", t ? "," : "", TH_ID[t]);
        for (int u = 0; u < UI_COUNT; u++) if (!region_default(g, t, u, &b)) sbf(R, "%s\"%s\":[%d,%d,%d,%d]", k++ ? "," : "", DESIGN_UI[u], b.x0, b.y0, b.x1, b.y1);
        int nf = region_fixed(g, t, fx, fn, 8); sbf(R, "},\"fixed\":[");
        for (int i = 0; i < nf; i++) { sbf(R, "%s[", i ? "," : ""); sbs(R, fn[i]); sbf(R, ",%d,%d,%d,%d]", fx[i].x0, fx[i].y0, fx[i].x1, fx[i].y1); }
        b = region_browse(t); sbf(R, "],\"browse\":[%d,%d,%d,%d]}", b.x0, b.y0, b.x1, b.y1);
    }
    sbf(R, "}");
}

int32_t ccs_home_render_check(const ccs_home_check_request *q, char *report, uint32_t cap, uint32_t *rlen) {
    if (rlen) *rlen = 0;
    if (!q || q->size != sizeof *q || !q->pack_dir || !q->work_dir || !report || cap < 2) return CCS_HOME_CHECK_BAD_REQUEST;
    int sv_scale = ccs_scale; ccs_scale = 1; probes_off(); g_clock = 1000;
    sb R = { 0 }, prob = { 0 }; int first = 1, nprob = 0, rc = CCS_HOME_CHECK_PASSED; char err[600] = "", sd[1100], sx[1100];
    framebuffer fb = { 0 }; uint8_t *bg = malloc((size_t)CCS_FB_W * CCS_FB_H * 3); home *D = NULL, *X = NULL; hdesign *probe = NULL;
    sbf(&R, "{\"schema\":\"ccs-home-render-check/1\",\"abi\":%u,\"profile\":\"%s\"", CCS_HOME_CHECK_ABI, DESIGN_PROFILE);
    if (!bg || fb_init(&fb, CCS_FB_W, CCS_FB_H)) { snprintf(err, sizeof err, "out of memory"); rc = CCS_HOME_CHECK_ERROR; goto done; }
    { char pj[1100]; uint8_t *b; size_t len; snprintf(pj, sizeof pj, "%s/pack.json", q->pack_dir); if (!ccs_read_file(pj, &b, &len, 4u << 20)) { char hx[65]; ccs_sha256_hex(b, len, hx); free(b); sbf(&R, ",\"pack_json_sha256\":\"%s\"", hx); } }
    snprintf(sd, sizeof sd, "%s/state-defaults", q->work_dir); snprintf(sx, sizeof sx, "%s/state-design", q->work_dir); ccs_mkdirs(sd); ccs_mkdirs(sx);
    ccs_mem_reset_peaks(); size_t l0 = ccs_mem_img_live;
    D = home_load3(q->pack_dir, sd, NULL, NULL, emitnull, err, sizeof err);
    if (!D) { rc = CCS_HOME_CHECK_ERROR; goto done; }
    pick_games(D); layout_json(&R, D->pk);
    size_t load_def_peak = ccs_mem_total_peak - (l0 < ccs_mem_total_peak ? l0 : 0);
    size_t design_peak_load = 0; int xoff[UI_COUNT][2] = { { 0 } }; int moves = 0;   /* the design's control groups' moves in its base theme */
    if (q->design_dir) {
        char e2[600]; if (design_load(q->design_dir, NULL, &probe, e2, sizeof e2)) { sbf(&prob, "%s", first ? "" : ","); first = 0; char m[700]; snprintf(m, sizeof m, "the design does not check out: %s", e2); sbs(&prob, m); nprob++; goto finish; }
        sbf(&R, ",\"design\":\"%s\",\"design_theme\":\"%s\",\"design_decoded_bytes\":%llu,\"design_file_bytes\":%llu", probe->id, TH_ID[probe->theme], (unsigned long long)probe->decoded_bytes, (unsigned long long)probe->file_bytes);
        ccs_mem_reset_peaks(); size_t l1 = ccs_mem_img_live;
        X = home_load3(q->pack_dir, sx, NULL, q->design_dir, emitnull, err, sizeof err);
        if (!X) { rc = CCS_HOME_CHECK_ERROR; goto done; }
        design_peak_load = ccs_mem_total_peak - (l1 < ccs_mem_total_peak ? l1 : 0);
        if (!X->design) { const char *why; home_design_source(X, &why); char m[700]; snprintf(m, sizeof m, "the HOME set the design aside: %s", why); sbf(&prob, "%s", first ? "" : ","); first = 0; sbs(&prob, m); nprob++; goto finish; }
        if (X->design->nreg) {   /* where the design puts the HOME's own control groups in its base theme, and how far each moved */
            int (*off)[2] = xoff; char e3[300]; design_layout(X->pk, X->design, xoff, e3, sizeof e3); sbf(&R, ",\"design_regions\":["); int k = 0;
            for (int u = 0; u < UI_COUNT; u++) if (X->design->reg[u].set) { const dregion *g = &X->design->reg[u]; sbf(&R, "%s{\"ui\":\"%s\",\"box\":[%d,%d,%d,%d],\"move\":[%d,%d]}", k++ ? "," : "", DESIGN_UI[u], g->x, g->y, g->x + g->w, g->y + g->h, off[u][0], off[u][1]); }
            sbf(&R, "]"); moves = 1; }
        /* the design's own labels, measured against what is under them as they are composed (its base theme activated again) */
        { tlog labels = { 0 }; int other = (X->design->theme + 1) % 4; home_set_theme(X, TH_ID[other], 0); probes_on(&labels, NULL); ccs_mem_reset_peaks(); size_t l2 = ccs_mem_img_live;
          home_set_theme(X, TH_ID[X->design->theme], 0); probes_off(); size_t act = ccs_mem_total_peak - (l2 < ccs_mem_total_peak ? l2 : 0); if (act > design_peak_load) design_peak_load = act;
          for (int i = 0; i < labels.n; i++) { const trec *r = &labels.r[i]; int need = r->px >= 24 ? 30 : 45;
              if (r->p10 < need) { char m[400]; snprintf(m, sizeof m, "the label \"%s\" is hard to read over what is under it (contrast %d.%d:1; at least %d.%d:1)", r->s, r->p10 / 10, r->p10 % 10, need / 10, need % 10); sbf(&prob, "%s", first ? "" : ","); first = 0; sbs(&prob, m); nprob++; } }
          for (int i = 0; i < labels.nmiss; i++) { char c[8], m[300]; cp_utf8(labels.miss[i], c); snprintf(m, sizeof m, "a label's %d px lettering has no character \"%s\" (it would show as ?)", labels.miss_px[i], c); sbf(&prob, "%s", first ? "" : ","); first = 0; sbs(&prob, m); nprob++; }
          free(labels.r); }
    }
    sbf(&R, ",\"states\":[");
    size_t peak_def = 0, peak_des = 0, live_def = 0, live_des = 0, stb_des = 0; int nst = 0;
    for (int t = 0; t < 4; t++) for (int st = 0; st < ST_COUNT; st++) {
        if (cancelled(q)) { rc = CCS_HOME_CHECK_CANCELLED; goto done; }
        shot sD, sX; int have = take(D, t, st, &fb, bg, &sD);
        if (have && (q->flags & CCS_HOME_CHECK_DEFAULT_FRAMES)) { if (q->frames_dir) { char p[1300]; snprintf(p, sizeof p, "%s/without-%s-%s.ppm", q->frames_dir, TH_ID[t], ST_ID[st]); write_ppm(p, fb.rgb, fb.w, fb.h); }
            if (q->on_frame && q->on_frame(q->ctx, TH_ID[t], ST_ID[st], 0, fb.rgb, fb.w, fb.h)) { shot_free(&sD); rc = CCS_HOME_CHECK_CANCELLED; goto done; } }
        if (have) { if (sD.peak > peak_def) peak_def = sD.peak; if (sD.live > live_def) live_def = sD.live; }
        int haveX = X ? take(X, t, st, &fb, bg, &sX) : 0;
        if (haveX && (q->flags & CCS_HOME_CHECK_FRAMES)) { if (q->frames_dir) { char p[1300]; snprintf(p, sizeof p, "%s/with-%s-%s.ppm", q->frames_dir, TH_ID[t], ST_ID[st]); write_ppm(p, fb.rgb, fb.w, fb.h); }
            if (q->on_frame && q->on_frame(q->ctx, TH_ID[t], ST_ID[st], 1, fb.rgb, fb.w, fb.h)) { shot_free(&sD); shot_free(&sX); rc = CCS_HOME_CHECK_CANCELLED; goto done; } }
        if (!have && !haveX) continue;
        int pn = 0; sb sp = { 0 }, sh = { 0 }; int f2 = 1;
        if (haveX && have) pn = judge(&sD, &sX, TH_ID[t], ST_ID[st], &sp, &f2, &sh, moves && X->design->theme == t ? (const int (*)[2])xoff : NULL);
        if (haveX) { if (sX.peak > peak_des) peak_des = sX.peak; if (sX.live > live_des) live_des = sX.live; if (sX.stb_peak > stb_des) stb_des = sX.stb_peak; }
        int minc = 63; const tlog *L = haveX ? &sX.text : &sD.text; for (int i = 0; i < L->n; i++) if (L->r[i].p10 < minc) minc = L->r[i].p10;
        int moved = haveX && have && X->design && X->design->nreg && X->design->theme == t;   /* its controls where the design moved them, next to the theme's own */
        sbf(&R, "%s{\"theme\":\"%s\",\"state\":\"%s\",\"ok\":%s,\"default_sha256\":\"%s\",\"design_sha256\":\"%s\",\"texts\":%d,\"min_contrast\":%d.%d,\"peak_bytes\":%zu,\"problems\":[%s],\"hard_to_read\":[%s],\"controls\":[%s]%s%s%s}",
            nst++ ? "," : "", TH_ID[t], ST_ID[st], pn ? "false" : "true", have ? sD.sha : "", haveX ? sX.sha : "", L->n, minc / 10, minc % 10, haveX ? sX.peak : sD.peak, sp.b ? sp.b : "", sh.b ? sh.b : "", haveX ? (sX.controls.b ? sX.controls.b : "") : (sD.controls.b ? sD.controls.b : ""),
            moved ? ",\"default_controls\":[" : "", moved && sD.controls.b ? sD.controls.b : "", moved ? "]" : "");
        if (pn) { sbf(&prob, "%s%s", first ? "" : ",", sp.b); first = 0; nprob += pn; }
        free(sp.b); free(sh.b); if (have) shot_free(&sD); if (haveX) shot_free(&sX);
    }
    sbf(&R, "]");
    /* fallback: the same design with one picture missing must come up exactly as the theme defaults */
    if (X && probe) {
        char fd[1100], p1[1100], p2[1100]; snprintf(fd, sizeof fd, "%s/missing-picture", q->work_dir); snprintf(p1, sizeof p1, "%s/img", fd); ccs_mkdirs(p1);
        snprintf(p1, sizeof p1, "%s/design.json", q->design_dir); snprintf(p2, sizeof p2, "%s/design.json", fd); int ok = !copy_file(p1, p2);
        int drop = -1; for (int i = 0; i < probe->nf; i++) if (drop < 0 || probe->f[i].bytes > probe->f[drop].bytes) drop = i;
        for (int i = 0; i < probe->nf && ok; i++) { if (i == drop) continue; snprintf(p1, sizeof p1, "%s/%s", q->design_dir, probe->f[i].name); snprintf(p2, sizeof p2, "%s/%s", fd, probe->f[i].name); ok = !copy_file(p1, p2); }
        char fsd[1100]; snprintf(fsd, sizeof fsd, "%s/state-missing", q->work_dir); ccs_mkdirs(fsd);
        home *F = ok ? home_load3(q->pack_dir, fsd, NULL, fd, emitnull, err, sizeof err) : NULL; shot sF = { 0 }, sD2 = { 0 }; int same = 0;
        if (F && !F->design && drop >= 0) { take(F, probe->theme, ST_ALL, &fb, bg, &sF); take(D, probe->theme, ST_ALL, &fb, bg, &sD2); same = !strcmp(sF.sha, sD2.sha); }
        else if (drop < 0) same = 1;   /* a design without pictures has nothing to go missing */
        const char *fw = ""; if (F) home_design_source(F, &fw);
        sbf(&R, ",\"fallback\":{\"missing_picture\":"); sbs(&R, drop >= 0 ? probe->f[drop].name : ""); sbf(&R, ",\"set_aside\":%s,\"same_as_defaults\":%s,\"why\":", F && !F->design ? "true" : "false", same ? "true" : "false"); sbs(&R, fw); sbf(&R, ",\"frame_sha256\":\"%s\"}", sF.sha);
        if (!same) { sbf(&prob, "%s", first ? "" : ","); first = 0; sbs(&prob, "with one of its pictures missing, the design did not fall back exactly to the theme defaults"); nprob++; }
        shot_free(&sF); shot_free(&sD2); home_free(F);
    }
    /* the startup picture */
    if (probe) {
        char path[1100], sha[65], b[40]; int m = design_startup(probe, path, sizeof path, sha, b, sizeof b);
        sbf(&R, ",\"startup\":{\"mode\":\"%s\"", m == DSU_IMAGE ? "image" : m == DSU_BUILTIN ? "builtin" : m == DSU_RANDOM ? "random" : "console");
        if (m == DSU_IMAGE) { image im; char e2[200]; ccs_mem_reset_peaks(); size_t l3 = ccs_mem_img_live; int okd = !design_decode(probe, probe->startup_file, &im, 0, e2, sizeof e2) && im.w == CCS_FB_W && im.h == CCS_FB_H;
            size_t sp = ccs_mem_total_peak - (l3 < ccs_mem_total_peak ? l3 : 0); char hx[65] = ""; if (okd) ccs_sha256_hex(im.px, (size_t)im.w * im.h * 3, hx);
            if (okd && (q->flags & CCS_HOME_CHECK_FRAMES)) { if (q->frames_dir) { char p[1300]; snprintf(p, sizeof p, "%s/with-startup.ppm", q->frames_dir); write_ppm(p, im.px, im.w, im.h); } if (q->on_frame) q->on_frame(q->ctx, "startup", "startup", 1, im.px, im.w, im.h); }
            if (im.px) img_free(&im);
            sbf(&R, ",\"ok\":%s,\"frame_sha256\":\"%s\",\"peak_bytes\":%zu", okd ? "true" : "false", hx, sp);
            if (!okd) { sbf(&prob, "%s", first ? "" : ","); first = 0; sbs(&prob, "the startup picture does not decode at 1280x720"); nprob++; } }
        else if (m == DSU_BUILTIN) { sbf(&R, ",\"builtin\":"); sbs(&R, b); }
        sbf(&R, "}");
    }
    size_t added = 0; if (X) { size_t a1 = design_peak_load > load_def_peak ? design_peak_load - load_def_peak : 0, a2 = peak_des > peak_def ? peak_des - peak_def : 0; added = a1 > a2 ? a1 : a2; }
    sbf(&R, ",\"memory\":{\"defaults_load_peak_bytes\":%zu,\"design_load_peak_bytes\":%zu,\"defaults_state_peak_bytes\":%zu,\"design_state_peak_bytes\":%zu,\"defaults_resident_bytes\":%zu,\"design_resident_bytes\":%zu,\"design_decoder_peak_bytes\":%zu,\"added_peak_bytes\":%zu,\"peak_budget_bytes\":%u,\"decode_work_budget_bytes\":%u}",
        load_def_peak, design_peak_load, peak_def, peak_des, live_def, live_des, stb_des, added, DESIGN_PEAK_BUDGET, DESIGN_DECODED_BUDGET);
    if (added > DESIGN_PEAK_BUDGET) { char m[300]; snprintf(m, sizeof m, "the design makes the HOME hold %zu more bytes of decoded pictures at once (at most %u)", added, DESIGN_PEAK_BUDGET); sbf(&prob, "%s", first ? "" : ","); first = 0; sbs(&prob, m); nprob++; }
finish:
    if (nprob) rc = CCS_HOME_CHECK_FAILED;
done:
    probes_off(); ccs_scale = sv_scale;
    if (rc == CCS_HOME_CHECK_ERROR || rc == CCS_HOME_CHECK_CANCELLED) { sbf(&prob, "%s", first ? "" : ","); first = 0; sbs(&prob, rc == CCS_HOME_CHECK_CANCELLED ? "the check was stopped" : err[0] ? err : "the check could not run"); }
    sbf(&R, ",\"ok\":%s,\"result\":%d,\"problems\":[%s]}", rc == CCS_HOME_CHECK_PASSED ? "true" : "false", rc, prob.b ? prob.b : "");
    home_free(X); home_free(D); design_free(probe); fb_free(&fb); free(bg); free(prob.b);
    if (R.oom || !R.b) { free(R.b); return CCS_HOME_CHECK_ERROR; }
    if (rlen) *rlen = (uint32_t)R.n;
    if (R.n + 1 > cap) { free(R.b); return CCS_HOME_CHECK_SMALL; }
    memcpy(report, R.b, R.n + 1); free(R.b); return rc;
}

int32_t ccs_home_design_boot(const char *pack_dir, const char *design_root, const char *state_dir, const char *frame_ppm, char *report, uint32_t cap, uint32_t *rlen) {
    if (rlen) *rlen = 0; if (!pack_dir || !report || cap < 2) return CCS_HOME_CHECK_BAD_REQUEST;
    int sv = ccs_scale; ccs_scale = 1; char err[600] = ""; sb R = { 0 }; framebuffer fb = { 0 }; int rc = CCS_HOME_CHECK_PASSED;
    if (state_dir) ccs_mkdirs(state_dir);
    home *h = home_load3(pack_dir, state_dir, design_root, NULL, emitnull, err, sizeof err);
    sbf(&R, "{\"schema\":\"ccs-home-design-boot/1\"");
    if (!h || fb_init(&fb, CCS_FB_W, CCS_FB_H)) { rc = CCS_HOME_CHECK_ERROR; sbf(&R, ",\"ok\":false,\"error\":"); sbs(&R, err[0] ? err : "out of memory"); }
    else {
        const char *why; const char *src = home_design_source(h, &why); home_render(h, &fb, 1000); char hx[65]; ccs_sha256_hex(fb.rgb, (size_t)CCS_FB_W * CCS_FB_H * 3, hx);
        if (frame_ppm) write_ppm(frame_ppm, fb.rgb, fb.w, fb.h);
        sbf(&R, ",\"ok\":true,\"source\":\"%s\",\"design\":\"%s\",\"theme\":\"%s\",\"frame_sha256\":\"%s\",\"why\":", src, home_design_id(h), home_theme(h), hx); sbs(&R, why);
    }
    sbf(&R, "}"); home_free(h); fb_free(&fb); ccs_scale = sv;
    if (R.oom || !R.b) { free(R.b); return CCS_HOME_CHECK_ERROR; }
    if (rlen) *rlen = (uint32_t)R.n; if (R.n + 1 > cap) { free(R.b); return CCS_HOME_CHECK_SMALL; }
    memcpy(report, R.b, R.n + 1); free(R.b); return rc;
}
