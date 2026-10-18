/* Native HOME (milestones N1, N1.1, N3): the HOME shell shared by the four approved themes.
 *
 * Package (pack.json, ccs-native-home-pack/2, built by tools/companion/native_home): all four themes (scene, sprites, measured
 * geometry, picker thumbnail), the fonts and icons they draw, and the selected games (games.json: effective cover, Details
 * backdrop, Featured Shelf key art, effective texts and previews). The shell owns everything the themes share: the game
 * records, the cover cache, the fonts, the controller dispatch (notices, the START menu, the theme picker, Adventure Map
 * destinations, Game Details), the controller legend and the persisted theme preference. A theme (theme_*.c) draws its HOME
 * screen and moves focus inside it; only the active theme's scene and sprites are resident, so a switch frees the previous one.
 *
 * Design source: the approved browser HOME (tools/rom-pipeline/studio/home-themes.js, home.css, home-state.js; geometry measured
 * on the masters in home-art.json). Controls: A launches, SELECT opens Game Details, B goes back one level, START opens the
 * menu (Return to Games, Change Theme, ...), no wrap, held D-pad repeat 400 / 110 / 55 ms after six.
 *
 * Theme preference (N3 addendum): the package carries the Windows-chosen default theme and, when the user asked Windows to
 * apply it, an apply number. The console keeps its own choice in <state dir>/home-prefs.json (written atomically, flushed to
 * storage, outside the package). Precedence at start: an apply number not yet applied > a valid saved choice > the package
 * default; an unreadable or invalid saved choice falls back to the package default. Changing the theme on the console applies
 * at once and is saved immediately.
 *
 * Browse (N3.1): the package carries the collection's folders (Windows order and nesting, their games, the custom folder
 * artwork as a tile image) and the automatic categories. Browse shows them as tiles in each theme's panel style (home-themes.js
 * hmBrowse); A opens a folder (its subfolders and own games in Browse, or its games in the theme's listing), a category, or
 * launches a game tile; B goes one level up; a listing remembers its selection when the player leaves it. Every theme shows
 * the same folders; Adventure Map's two folder signs open their folders directly.
 *
 * Boundaries, by design: no game is ever launched (A says launching is not available here), Favorites are not connected, and
 * Search, Favorites, Recently Played and Settings say they are not available yet.
 */
#include "home_int.h"
#include <stdarg.h>

int ccs_mkdirs(const char *dir);
int ccs_write_atomic(const char *path, const void *data, size_t len);

#define FONT_NAME(id, name) name,
static const char *FONT_NAMES[F_COUNT] = { FONT_LIST(FONT_NAME) };
#undef FONT_NAME
#define CACHE_N 64
#define CACHE_BYTES (16u << 20)
#define BYTES_N 96
#define BYTES_CAP (8u << 20)
#define DESC_STEP 112                       /* home-themes.js HM_DESC_STEP: four 28 px lines per scroll step */
static const char *THEME_IDS[N_THEMES] = { "arcade-grid", "box-art-showcase", "featured-shelf", "adventure-map" };
static const char *THEME_NAMES[N_THEMES] = { "Arcade Grid", "Box Art Showcase", "Featured Shelf", "Adventure Map" };
static const theme_ops *THEME_OPS[N_THEMES] = { &THEME_ARCADE, &THEME_BOXART, &THEME_SHELF, &THEME_MAP };
static const char *NODESC = "No description is available for this game.";
/* home-state.js HOME_START_MENU (Sort Games applies to All Games); HOME_NAV for the Adventure Map destinations */
static const char *START_ID[9] = { "return", "theme", "sort", "favorites", "recent", "settings", "allgames", "browse", "search" };
static const char *START_LABEL[9] = { "Return to Games", "Change Theme", "Sort Games", "Favorites", "Recently Played", "Settings", "All Games", "Browse", "Search" };
static const char *DEST_LABEL[6] = { "All Games", "Browse", "Search", "Favorites", "Recently Played", "Settings" };

/* ---------- small helpers ---------- */
rect FULL = { 0, 0, 1 << 20, 1 << 20 };
char *sdup(const char *s) { if (!s) return NULL; size_t n = strlen(s) + 1; char *d = malloc(n); if (d) memcpy(d, s, n); return d; }
static char *jdup(const jval *o, const char *k) { const jval *v = json_get(o, k); return v && v->t == J_STR && v->s[0] ? sdup(v->s) : NULL; }
int gnum(const jval *o, const char *k, int def) { const jval *v = o ? json_get(o, k) : NULL; return v && v->t == J_NUM ? (int)v->num : def; }
static int jbox(const jval *v, box4 *b) { if (!v || v->t != J_ARR || v->n != 4) return -1; for (size_t i = 0; i < 4; i++) if (v->items[i]->t != J_NUM) return -1; b->x0 = (int)v->items[0]->num; b->y0 = (int)v->items[1]->num; b->x1 = (int)v->items[2]->num; b->y1 = (int)v->items[3]->num; return 0; }
int gbox(const jval *o, const char *k, box4 *b) { return jbox(o ? json_get(o, k) : NULL, b); }
int jbox_at(const jval *arr, size_t i, box4 *b) { return !arr || arr->t != J_ARR || i >= arr->n ? -1 : jbox(arr->items[i], b); }
static int safe_rel(const char *p) { if (!p || !*p || p[0] == '/' || p[0] == '\\' || strstr(p, "..") || strchr(p, ':')) return 0; for (const char *c = p; *c; c++) if (!((*c >= 'a' && *c <= 'z') || (*c >= 'A' && *c <= 'Z') || (*c >= '0' && *c <= '9') || *c == '_' || *c == '-' || *c == '.' || *c == '/')) return 0; return 1; }
static void pjoin(const home *h, const char *rel, char *out, size_t n) { snprintf(out, n, "%s/%s", h->dir, rel); }
int iround(double v) { return v >= 0 ? (int)(v + 0.5) : -(int)(-v + 0.5); }
/* line boxes as Chromium lays them out (measured in Edge): half the leading (floored to 1/64 px) above the ascent; HOME text
 * without its own line-height inherits the page's 1.45 */
double lh_normal(const font *f) { return f->px * 1.45; }
static double floor64(double v) { int64_t i = (int64_t)(v * 64); if ((double)i > v * 64) i--; return i / 64.0; }
int baseline_in(const font *f, double top, double line_h) { return iround(top + floor64((line_h - f->ascent - f->descent) / 2.0) + f->ascent); }
int baseline_normal(const font *f, double top) { return baseline_in(f, top, lh_normal(f)); }
int baseline_center(const font *f, double top, double h) { return baseline_normal(f, top + (h - lh_normal(f)) / 2.0); }
int T(framebuffer *fb, rect clip, const font *f, double x, int base, const char *s, uint32_t rgb, int a, double sp) { return text_draw(fb, NULL, clip, f, x, base, s, strlen(s), RGBA(rgb, a), sp, 0); }
int Tb(framebuffer *fb, rect clip, const font *f, double x, int base, const char *s, uint32_t rgb, int a, double sp) { text_draw(fb, NULL, clip, f, x + 1, base, s, strlen(s), RGBA(rgb, a), sp, 0); return text_draw(fb, NULL, clip, f, x, base, s, strlen(s), RGBA(rgb, a), sp, 0) + 1; }
void emit_json(home *h, const char *fmt, ...) { char buf[4096]; va_list ap; va_start(ap, fmt); vsnprintf(buf, sizeof buf, fmt, ap); va_end(ap); if (h->emit) h->emit(buf); }
static void jesc(const char *s, char *out, size_t n) { json_escape(s ? s : "", out, n); }
void LR(home *h, const char *name, int x0, int y0, int x1, int y1) { if (!h->rec || h->nrec >= MAX_REC) return; layrec *r = &h->recs[h->nrec++]; snprintf(r->name, sizeof r->name, "%s", name); r->b.x0 = x0; r->b.y0 = y0; r->b.x1 = x1; r->b.y1 = y1; }
void LRtext(home *h, const char *name, const font *f, double x, int base, const char *s, size_t len, double sp) { LR(h, name, iround(x), base - f->ascent, iround(x) + text_w(f, s, len, sp), base + f->descent); }
int dir_of(button b) { return b == B_UP ? D_UP : b == B_DOWN ? D_DOWN : b == B_LEFT ? D_LEFT : b == B_RIGHT ? D_RIGHT : -1; }
void draw_glow_text(framebuffer *fb, home *h, int fid, double x, int base, const char *s, size_t len, double sigma, color glow, double sp) {
    const font *f = &h->f[fid]; int w = text_w(f, s, len, sp), m = (int)(sigma * 3) + 2; mask mk;
    if (mask_alloc(&mk, (int)x - m, base - f->ascent - m, w + 2 * m, f->ascent + f->descent + 2 * m)) return;
    text_draw(NULL, &mk, FULL, f, x, base, s, len, RGBA(0, 255), sp, 0); mask_blur(&mk, sigma); gfx_mask(fb, FULL, &mk, glow); mask_free(&mk);
}
/* CSS -webkit-line-clamp: up to `lines` lines; when the text is longer, the last one ends in an ellipsis (copied into `last`) */
size_t overview_clamp(const font *f, const char *s, int maxw, int lines, double sp, span *out, char *last, size_t lastlen) {
    span ln[64]; size_t n = text_wrap(f, s, maxw, sp, ln, 64); last[0] = 0;
    if (n <= (size_t)lines) { for (size_t i = 0; i < n; i++) out[i] = ln[i]; return n; }
    for (int i = 0; i < lines; i++) out[i] = ln[i];
    char buf[1200]; size_t k = strlen(s + ln[lines - 1].start); if (k > sizeof buf - 8) k = sizeof buf - 8; memcpy(buf, s + ln[lines - 1].start, k); buf[k] = 0;
    char fit[1200]; snprintf(buf + k, sizeof buf - k, "%s", "");
    /* the clamped line: as much of the remaining text as fits with the ellipsis */
    size_t len = ln[lines - 1].len; char tmp[1200]; size_t m = 0; for (size_t i = 0; i < k && m < sizeof tmp - 4; i++) tmp[m++] = buf[i]; tmp[m] = 0;
    (void)len; text_fit(f, tmp, maxw, sp, fit, sizeof fit); snprintf(last, lastlen, "%s", fit); return (size_t)lines;
}

int fit_font(const home *h, const int *fonts, int n, const char *s, int maxw, double sp) { for (int i = 0; i < n; i++) if (text_w(&h->f[fonts[i]], s, strlen(s), sp) <= maxw) return fonts[i]; return fonts[n - 1]; }
void upper(const char *s, char *out, size_t n) { size_t i = 0; for (; s && s[i] && i < n - 1; i++) out[i] = (char)((s[i] >= 'a' && s[i] <= 'z') ? s[i] - 32 : s[i]); out[i] = 0; }
const image *sprite_scaled(home *h, const char *name, int w, int hh, int nearest, image *tmp) {
    const tsprite *s = tsp(h, name); tmp->px = NULL; if (!s) return NULL; if (s->im.w == w && s->im.h == hh) return &s->im;
    if (nearest ? img_scale_nearest(&s->im, tmp, w, hh) : img_scale(&s->im, tmp, w, hh)) return NULL; return tmp;
}

/* ---------- sprites, icons, thumbs ---------- */
const tsprite *tsp(const home *h, const char *name) { for (int i = 0; i < h->nsp; i++) if (!strcmp(h->sp[i].name, name)) return &h->sp[i]; return NULL; }
const image *icon(const home *h, const char *kind, int size) { char k[24]; snprintf(k, sizeof k, "%s-%d", kind, size); for (int i = 0; i < h->nicons; i++) if (!strcmp(h->icons[i].key, k)) return &h->icons[i].im; return NULL; }
int home_master(const home *h, int gi) { return h->vmap && gi >= 0 && gi < (int)h->n ? h->vmap[gi] : gi; }
static void theme_unload(home *h) {
    if (h->ops && h->ops->fini) h->ops->fini(h);
    img_free(&h->scene); for (int i = 0; i < h->nsp; i++) img_free(&h->sp[i].im); h->nsp = 0; h->ops = NULL; h->tj = h->geo = NULL;
}
static int theme_index(const char *id) { for (int i = 0; i < N_THEMES; i++) if (id && !strcmp(id, THEME_IDS[i])) return i; return -1; }
/* load one theme's scene and sprites from the package and let its renderer prepare; the previous theme is freed first */
static int theme_activate(home *h, int t, char *err, size_t errlen) {
    const jval *themes = json_get(h->pk, "themes"), *tj = themes ? json_get(themes, THEME_IDS[t]) : NULL; char p[1200];
    if (!tj) { snprintf(err, errlen, "the package has no %s theme", THEME_IDS[t]); return -1; }
    theme_unload(h); h->theme = t; h->tj = tj; h->geo = json_get(tj, "geometry");
    const jval *sc = json_get(tj, "scene"); if (!sc || sc->t != J_STR || !safe_rel(sc->s)) { snprintf(err, errlen, "%s scene missing", THEME_IDS[t]); return -1; }
    pjoin(h, sc->s, p, sizeof p); if (img_load(p, &h->scene, 0, err, errlen)) return -1;
    if (h->scene.w != CCS_FB_W || h->scene.h != CCS_FB_H) { snprintf(err, errlen, "scene is %dx%d", h->scene.w, h->scene.h); return -1; }
    const jval *sps = json_get(tj, "sprites");
    if (sps && sps->t == J_OBJ) for (size_t i = 0; i < sps->n && h->nsp < MAX_SPRITES; i++) {
        const jval *sp = sps->items[i], *f = json_get(sp, "file"); tsprite *s = &h->sp[h->nsp];
        if (!f || f->t != J_STR || !safe_rel(f->s) || gbox(sp, "box", &s->box)) { snprintf(err, errlen, "sprite %s malformed", sps->keys[i]); return -1; }
        snprintf(s->name, sizeof s->name, "%s", sps->keys[i]); pjoin(h, f->s, p, sizeof p); if (img_load(p, &s->im, 1, err, errlen)) return -1; h->nsp++;
    }
    h->ops = THEME_OPS[t]; if (!h->geo) { snprintf(err, errlen, "%s geometry missing", THEME_IDS[t]); return -1; }
    return h->ops->init ? h->ops->init(h, err, errlen) : 0;
}

/* ---------- theme preference (persisted outside the package) ---------- */
static void prefs_path(const home *h, char *out, size_t n) { snprintf(out, n, "%s/home-prefs.json", h->state_dir); }
static int prefs_save(home *h, const char *theme_id, int epoch) {
    if (!h->state_dir[0]) return 0; char p[1100], body[200]; prefs_path(h, p, sizeof p); ccs_mkdirs(h->state_dir);
    int n = snprintf(body, sizeof body, "{\"schema\":\"ccs-home-prefs/1\",\"theme\":\"%s\",\"applied_epoch\":%d}\n", theme_id, epoch);
    int rc = ccs_write_atomic(p, body, (size_t)n); h->saved_epoch = epoch;
    emit_json(h, "{\"ev\":\"prefs_saved\",\"theme\":\"%s\",\"applied_epoch\":%d,\"ok\":%s}", theme_id, epoch, rc ? "false" : "true"); return rc;
}
/* the start theme: an apply number not yet applied > a valid saved choice > the package default (invalid saved data -> default) */
static int prefs_resolve(home *h) {
    int def = theme_index(h->theme_default); if (def < 0) def = 0;
    int saved = -1, saved_epoch = 0, invalid = 0; char p[1100]; uint8_t *buf; size_t len;
    if (h->state_dir[0]) { prefs_path(h, p, sizeof p);
        if (!ccs_read_file(p, &buf, &len, 1u << 16)) { char jerr[120]; jval *o = json_parse((const char *)buf, len, jerr, sizeof jerr); free(buf);
            const jval *t = o ? json_get(o, "theme") : NULL, *s = o ? json_get(o, "schema") : NULL;
            if (o && o->t == J_OBJ && s && s->t == J_STR && !strcmp(s->s, "ccs-home-prefs/1") && t && t->t == J_STR && theme_index(t->s) >= 0) { saved = theme_index(t->s); saved_epoch = gnum(o, "applied_epoch", 0); }
            else invalid = 1; json_free(o); } }
    h->saved_epoch = saved_epoch;
    if (h->apply_epoch > 0 && h->apply_epoch != saved_epoch) { snprintf(h->theme_source, sizeof h->theme_source, "applied"); prefs_save(h, THEME_IDS[def], h->apply_epoch); return def; }
    if (saved >= 0) { snprintf(h->theme_source, sizeof h->theme_source, "saved"); return saved; }
    snprintf(h->theme_source, sizeof h->theme_source, invalid ? "fallback" : "default"); return def;
}

/* ---------- loading ---------- */
static int *jints(const jval *a, int *n, int max) {   /* valid package indices of a JSON array */
    *n = 0; if (!a || a->t != J_ARR) return NULL; int *v = calloc(a->n ? a->n : 1, sizeof *v); if (!v) return NULL;
    for (size_t i = 0; i < a->n; i++) { const jval *x = a->items[i]; if (x->t == J_NUM && x->num >= 0 && x->num < max) v[(*n)++] = (int)x->num; }
    return v;
}
static int load_browse(home *h, char *err, size_t errlen) {
    const jval *b = json_get(h->pk, "browse"), *fs = b ? json_get(b, "folders") : NULL, *cs = b ? json_get(b, "categories") : NULL;
    h->map_folder[0] = h->map_folder[1] = -1; h->nav.sec = SEC_ALL; h->nav.sec_id = -1;
    if (fs && fs->t == J_ARR && fs->n) {
        if (!(h->fold = calloc(fs->n, sizeof *h->fold))) { snprintf(err, errlen, "out of memory"); return -1; }
        for (size_t i = 0; i < fs->n; i++) {
            const jval *o = fs->items[i]; hfolder *f = &h->fold[h->nfold]; if (o->t != J_OBJ) continue;
            f->id = jdup(o, "id"); f->name = jdup(o, "name"); f->art = jdup(o, "art"); if (f->art && !safe_rel(f->art)) { free(f->art); f->art = NULL; }
            if (!f->name) f->name = sdup("Folder"); f->parent = gnum(o, "parent", -1); f->games = jints(json_get(o, "games"), &f->ng, (int)h->nall); h->nfold++;
        }
        for (int i = 0; i < h->nfold; i++) { hfolder *f = &h->fold[i]; if (f->parent < 0 || f->parent >= h->nfold || f->parent == i) f->parent = -1; else h->fold[f->parent].nchild++; }
    }
    if (cs && cs->t == J_ARR && cs->n) {
        if (!(h->cats = calloc(cs->n, sizeof *h->cats))) { snprintf(err, errlen, "out of memory"); return -1; }
        for (size_t i = 0; i < cs->n; i++) {
            const jval *o = cs->items[i]; hcat *c = &h->cats[h->ncat]; if (o->t != J_OBJ) continue;
            c->label = jdup(o, "label"); c->kind = jdup(o, "kind"); if (!c->label) { free(c->kind); memset(c, 0, sizeof *c); continue; }
            c->games = jints(json_get(o, "games"), &c->ng, (int)h->nall); h->ncat++;
        }
    }
    const jval *mf = json_get(h->pk, "map_folders");
    for (int k = 0; k < 2; k++) { const jval *v = mf && mf->t == J_ARR && (size_t)k < mf->n ? mf->items[k] : NULL; int x = v && v->t == J_NUM ? (int)v->num : -1; h->map_folder[k] = x >= 0 && x < h->nfold ? x : -1; }
    h->memo_folder = calloc((size_t)h->nfold + 1, sizeof(int)); h->memo_cat = calloc((size_t)h->ncat + 1, sizeof(int)); h->memo_browse = calloc((size_t)h->nfold + 1, sizeof(int));
    if (!h->memo_folder || !h->memo_cat || !h->memo_browse) { snprintf(err, errlen, "out of memory"); return -1; }
    return 0;
}
home *home_load(const char *dir, home_emit_fn emit, char *err, size_t errlen) { return home_load2(dir, NULL, emit, err, errlen); }
home *home_load2(const char *dir, const char *state_dir, home_emit_fn emit, char *err, size_t errlen) {
    home *h = calloc(1, sizeof *h); if (!h) { snprintf(err, errlen, "out of memory"); return NULL; }
    snprintf(h->dir, sizeof h->dir, "%s", dir); snprintf(h->state_dir, sizeof h->state_dir, "%s", state_dir ? state_dir : ""); h->emit = emit; h->nav.swallow = -1; h->nav.repeat_button = -1;
    char p[1200]; uint8_t *buf = NULL; size_t len; jval *gm = NULL; char jerr[200];
    pjoin(h, "pack.json", p, sizeof p);
    if (ccs_read_file(p, &buf, &len, 4u << 20)) { snprintf(err, errlen, "cannot read %s", p); goto fail; }
    h->pk = json_parse((const char *)buf, len, jerr, sizeof jerr); free(buf); buf = NULL;
    if (!h->pk || h->pk->t != J_OBJ) { snprintf(err, errlen, "pack.json is not valid JSON: %s", jerr); goto fail; }
    const jval *schema = json_get(h->pk, "schema"); if (!schema || schema->t != J_STR || strcmp(schema->s, "ccs-native-home-pack/2")) { snprintf(err, errlen, "pack schema is not ccs-native-home-pack/2 (rebuild the package)"); goto fail; }
    const jval *themes = json_get(h->pk, "themes"); for (int t = 0; t < N_THEMES; t++) if (!themes || !json_get(themes, THEME_IDS[t])) { snprintf(err, errlen, "the package lacks the %s theme (all four themes are required)", THEME_IDS[t]); goto fail; }
    const jval *td = json_get(h->pk, "theme_default"); snprintf(h->theme_default, sizeof h->theme_default, "%s", td && td->t == J_STR ? td->s : "arcade-grid");
    h->apply_epoch = gnum(h->pk, "theme_apply", 0);
    const jval *fonts = json_get(h->pk, "fonts");
    for (int i = 0; i < F_COUNT; i++) { const jval *fo = fonts ? json_get(fonts, FONT_NAMES[i]) : NULL; const jval *f = fo ? json_get(fo, "file") : NULL; if (!f || f->t != J_STR || !safe_rel(f->s)) { snprintf(err, errlen, "pack font %s missing", FONT_NAMES[i]); goto fail; } pjoin(h, f->s, p, sizeof p); if (font_load(&h->f[i], p, err, errlen)) goto fail; }
    const jval *icons = json_get(h->pk, "icons");
    if (icons && icons->t == J_OBJ) for (size_t i = 0; i < icons->n && h->nicons < 24; i++) { const jval *f = icons->items[i]; if (f->t != J_STR || !safe_rel(f->s)) continue; ticon *ic = &h->icons[h->nicons]; snprintf(ic->key, sizeof ic->key, "%s", icons->keys[i]); pjoin(h, f->s, p, sizeof p); if (img_load(p, &ic->im, 1, err, errlen)) goto fail; h->nicons++; }
    for (int t = 0; t < N_THEMES; t++) { const jval *th = json_get(themes, THEME_IDS[t]), *f = json_get(th, "thumb"); if (f && f->t == J_STR && safe_rel(f->s)) { pjoin(h, f->s, p, sizeof p); if (img_load(p, &h->thumbs[t], 0, err, errlen)) goto fail; } }
    /* games: compact records in display order; long texts stay on disk until Details opens */
    const jval *gf = json_get(h->pk, "games"); if (!gf || gf->t != J_STR || !safe_rel(gf->s)) { snprintf(err, errlen, "pack games file missing"); goto fail; }
    pjoin(h, gf->s, p, sizeof p); if (ccs_read_file(p, &buf, &len, 32u << 20)) { snprintf(err, errlen, "cannot read %s", p); goto fail; }
    gm = json_parse((const char *)buf, len, jerr, sizeof jerr); free(buf); buf = NULL;
    const jval *gs = gm ? json_get(gm, "games") : NULL, *gsch = gm ? json_get(gm, "schema") : NULL;
    if (!gs || gs->t != J_ARR || !gsch || gsch->t != J_STR || strcmp(gsch->s, "ccs-native-home-games/2")) { snprintf(err, errlen, "games file is not ccs-native-home-games/2 (%s)", gm ? "schema" : jerr); goto fail; }
    h->g = calloc(gs->n ? gs->n : 1, sizeof *h->g);   /* sized by the package: any number of selected games */
    for (size_t i = 0; i < gs->n; i++) {
        const jval *o = gs->items[i]; hgame *g = &h->g[h->n]; if (o->t != J_OBJ) continue;
        g->id = jdup(o, "id"); g->title = jdup(o, "title"); if (!g->id || !g->title) { free(g->id); free(g->title); memset(g, 0, sizeof *g); continue; }
        g->version_label = jdup(o, "version_label"); g->year = jdup(o, "year"); g->players = jdup(o, "players"); g->genre = jdup(o, "genre"); g->publisher = jdup(o, "publisher"); g->developer = jdup(o, "developer");
        g->cover = jdup(o, "cover"); g->cover_kind = jdup(o, "cover_kind"); g->hero = jdup(o, "hero"); g->hero_kind = jdup(o, "hero_kind"); g->hero_wide = jdup(o, "hero_wide"); g->text = jdup(o, "text");
        g->blurb = jdup(o, "blurb"); g->mblurb = jdup(o, "mblurb"); g->ftext = jdup(o, "ftext");
        { const jval *ht = json_get(o, "hero_title"); const jval *v; g->ht_x = 0.5; g->ht_y = 0.09; g->ht_w = 0; g->ht_right = 0;
          if (ht && ht->t == J_OBJ) { if ((v = json_get(ht, "x")) && v->t == J_NUM) g->ht_x = v->num; if ((v = json_get(ht, "y")) && v->t == J_NUM) g->ht_y = v->num; if ((v = json_get(ht, "w")) && v->t == J_NUM) g->ht_w = v->num; if ((v = json_get(ht, "align")) && v->t == J_STR) g->ht_right = !strcmp(v->s, "right"); } }
        char **paths[4] = { &g->cover, &g->hero, &g->hero_wide, &g->text };
        for (int k = 0; k < 4; k++) if (*paths[k] && !safe_rel(*paths[k])) { free(*paths[k]); *paths[k] = NULL; }
        h->n++;
    }
    json_free(gm); gm = NULL;
    h->all = h->g; h->nall = h->n;
    if (load_browse(h, err, errlen)) goto fail;
    int t = prefs_resolve(h);
    if (theme_activate(h, t, err, errlen)) goto fail;
    h->static_bytes = 0; for (int i = 0; i < F_COUNT; i++) h->static_bytes += h->f[i].nbits + h->f[i].n * sizeof(glyph);
    emit_json(h, "{\"ev\":\"theme_resolved\",\"theme\":\"%s\",\"source\":\"%s\",\"default\":\"%s\",\"apply_epoch\":%d}", THEME_IDS[h->theme], h->theme_source, h->theme_default, h->apply_epoch);
    return h;
fail:
    free(buf); json_free(gm); home_free(h); return NULL;
}

static void details_close(home *h);
static void home_go_cat(home *h, int c);
void home_free(home *h) {
    if (!h) return; details_close(h); theme_unload(h);
    hgame *AG = h->all ? h->all : h->g; size_t NA = h->all ? h->nall : h->n;
    for (size_t i = 0; i < NA; i++) { hgame *g = &AG[i]; char *f[] = { g->id, g->title, g->version_label, g->year, g->players, g->genre, g->publisher, g->developer, g->cover, g->cover_kind, g->hero, g->hero_kind, g->hero_wide, g->text, g->blurb, g->mblurb, g->ftext }; for (size_t k = 0; k < sizeof f / sizeof *f; k++) free(f[k]); }
    free(AG); free(h->view); free(h->vmap);
    for (int i = 0; i < h->nfold; i++) { free(h->fold[i].id); free(h->fold[i].name); free(h->fold[i].art); free(h->fold[i].games); img_free(&h->fold[i].tile); }
    for (int i = 0; i < h->ncat; i++) { free(h->cats[i].label); free(h->cats[i].kind); free(h->cats[i].games); }
    free(h->fold); free(h->cats); free(h->memo_folder); free(h->memo_cat); free(h->memo_browse); free(h->bi);
    for (int i = 0; i < N_THEMES; i++) img_free(&h->thumbs[i]); for (int i = 0; i < h->nicons; i++) img_free(&h->icons[i].im);
    for (int i = 0; i < F_COUNT; i++) font_free(&h->f[i]); for (int i = 0; i < CACHE_N; i++) img_free(&h->cache[i].im); for (int i = 0; i < BYTES_N; i++) free(h->bytes[i].buf);
    json_free(h->pk); free(h);
}
size_t home_bytes(const home *h) { return h->static_bytes + h->cache_bytes + h->bytes_total + (size_t)CCS_FB_W * CCS_FB_H * 3 + (h->d.open ? (size_t)CCS_FB_W * CCS_FB_H * 3 : 0); }
size_t home_games(const home *h) { return h->nall; }
const char *home_focus_id(const home *h) { if (h->nav.sec == SEC_BROWSE) return h->nav.browse < h->nbi && h->bi[h->nav.browse].kind == BI_GAME ? h->all[h->bi[h->nav.browse].idx].id : ""; return h->n ? h->g[h->nav.sel].id : ""; }
/* tooling: go to a section the way the controller would (tests); kind: all | browse [parent] | folder <i> | category <i> | back */
void home_go(home *h, const char *kind, int arg) {
    if (!strcmp(kind, "all")) home_go_all(h); else if (!strcmp(kind, "browse")) home_go_browse(h, arg); else if (!strcmp(kind, "folder")) home_go_folder(h, arg);
    else if (!strcmp(kind, "category")) home_go_cat(h, arg); else if (!strcmp(kind, "back")) home_back(h);
    home_emit_state(h);
}
const char *home_theme(const home *h) { return h->ops ? h->ops->id : ""; }

/* ---------- covers: decoded on demand, scaled to the exact box, kept in a bounded LRU ---------- */
static void noart(home *h, int gi, image *im, int w, int hh) {   /* only when a cover file cannot be read: the title on a dark box */
    if (img_alloc(im, w, hh, 3)) return;
    for (int y = 0; y < hh; y++) for (int x = 0; x < w; x++) { int t = (x + y) * 255 / (w + hh); uint8_t *q = im->px + ((size_t)y * w + x) * 3; q[0] = (uint8_t)(0x2a - (0x2a - 0x15) * t / 255); q[1] = q[0]; q[2] = (uint8_t)(0x33 - (0x33 - 0x1b) * t / 255); }
    framebuffer tmp = { w, hh, im->px }; span ln[6]; const char *t = h->g[gi].title; size_t n = text_wrap(&h->f[F_SILK9], t, w - 16, 0, ln, 6); int lh = 14, y0 = (hh - (int)n * lh) / 2;
    for (size_t i = 0; i < n; i++) { int tw = text_w(&h->f[F_SILK9], t + ln[i].start, ln[i].len, 0); text_draw(&tmp, NULL, FULL, &h->f[F_SILK9], (w - tw) / 2.0, baseline_normal(&h->f[F_SILK9], y0 + (int)i * lh), t + ln[i].start, ln[i].len, RGBA(0xE8E6E0, 255), 0, 0); }
}
static bentry *cover_bytes(home *h, int gi) {   /* gi: a listing index; the caches key by the package index (shared by every listing) */
    int mi = home_master(h, gi);
    int victim = -1; for (int i = 0; i < BYTES_N; i++) { if (h->bytes[i].buf && h->bytes[i].game == mi) { h->bytes[i].used = ++h->use_seq; return &h->bytes[i]; } if (!h->bytes[i].buf && victim < 0) victim = i; }
    if (!h->all[mi].cover) return NULL; char p[1200]; uint8_t *b; size_t len; pjoin(h, h->all[mi].cover, p, sizeof p); if (ccs_read_file(p, &b, &len, 4u << 20)) return NULL;
    for (;;) { int lru = -1, used = 0; for (int i = 0; i < BYTES_N; i++) if (h->bytes[i].buf) { used++; if (lru < 0 || h->bytes[i].used < h->bytes[lru].used) lru = i; }
        if (lru < 0 || (used < BYTES_N && h->bytes_total + len <= BYTES_CAP)) break; h->bytes_total -= h->bytes[lru].len; free(h->bytes[lru].buf); h->bytes[lru].buf = NULL; victim = lru; }
    if (victim < 0) for (int i = 0; i < BYTES_N; i++) if (!h->bytes[i].buf) { victim = i; break; }
    bentry *e = &h->bytes[victim]; e->game = mi; e->buf = b; e->len = len; e->used = ++h->use_seq; h->bytes_total += len; return e;
}
const image *cover_b(home *h, int gi, int w, int hh, int mode, int *ox, int *oy, int must) {
    *ox = *oy = 0; int victim = 0, mi = home_master(h, gi);
    for (int i = 0; i < CACHE_N; i++) { centry *c = &h->cache[i]; if (c->im.px && c->game == mi && c->w == w && c->h == hh && c->mode == mode) { c->used = ++h->use_seq; if (mode == 1) { *ox = (w - c->im.w) / 2; *oy = (hh - c->im.h) / 2; } return &c->im; } if (!c->im.px) victim = i; }
    if (!must && h->budget && h->decoded >= h->budget) return NULL;
    h->decoded++;
    for (;;) {
        int lru = -1, used = 0; for (int i = 0; i < CACHE_N; i++) if (h->cache[i].im.px) { used++; if (lru < 0 || h->cache[i].used < h->cache[lru].used) lru = i; }
        if (lru < 0 || (used < CACHE_N && h->cache_bytes + (size_t)w * hh * 3 <= CACHE_BYTES)) break;
        h->cache_bytes -= (size_t)h->cache[lru].im.w * h->cache[lru].im.h * h->cache[lru].im.ch; img_free(&h->cache[lru].im); victim = lru;
    }
    for (int i = 0; i < CACHE_N; i++) if (!h->cache[i].im.px) { victim = i; break; }
    centry *c = &h->cache[victim]; image src; char err[256]; int ok = 0;
    bentry *be = cover_bytes(h, gi);
    if (be && !img_decode(be->buf, be->len, &src, 0, err, sizeof err)) { ok = mode == 1 ? !img_contain(&src, &c->im, w, hh, ox, oy) : !img_cover(&src, &c->im, w, hh); img_free(&src); }
    if (!ok) { noart(h, gi, &c->im, w, hh); *ox = *oy = 0; }
    c->game = mi; c->w = w; c->h = hh; c->mode = mode; c->used = ++h->use_seq; h->cache_bytes += (size_t)c->im.w * c->im.h * c->im.ch; return &c->im;
}
const image *cover(home *h, int gi, int w, int hh, int mode, int *ox, int *oy) { return cover_b(h, gi, w, hh, mode, ox, oy, 1); }
void draw_cover(home *h, framebuffer *fb, int gi, box4 b, int must, box4 *art) {
    int ox, oy; const image *im = cover_b(h, gi, b.x1 - b.x0, b.y1 - b.y0, 1, &ox, &oy, must);
    if (!im) { h->pending++; if (art) *art = b; return; }
    gfx_image(fb, FULL, im, b.x0 + ox, b.y0 + oy, 255); if (art) { art->x0 = b.x0 + ox; art->y0 = b.y0 + oy; art->x1 = art->x0 + im->w; art->y1 = art->y0 + im->h; }
}
int home_prefetch_bytes(home *h, int gi) {   /* reads a cover file into the byte cache ahead of need: 1 if it read, 0 if cached */
    int mi = home_master(h, gi); for (int i = 0; i < BYTES_N; i++) if (h->bytes[i].buf && h->bytes[i].game == mi) return 0;
    cover_bytes(h, gi); return 1;
}
void home_set_cover_budget(home *h, int per_frame) { h->budget = per_frame > 0 ? per_frame : 0; }
int home_covers_pending(const home *h) { return h->pending; }

/* ---------- notices ---------- */
void notice(home *h, const char *title, const char *text) { h->nav.notice = 1; snprintf(h->notice_title, sizeof h->notice_title, "%s", title); snprintf(h->notice_text, sizeof h->notice_text, "%s", text); }
void launch_unavailable(home *h, int gi) {
    char id[160], t[400], txt[600]; jesc(h->g[gi].id, id, sizeof id); jesc(h->g[gi].title, t, sizeof t);
    emit_json(h, "{\"ev\":\"launch_unavailable\",\"id\":%s,\"title\":%s,\"note\":\"native HOME prototype: launching is not connected; no game runs\"}", id, t);
    snprintf(txt, sizeof txt, "%s would start now on the console. This native HOME prototype does not launch games yet; nothing was run.", h->g[gi].title);
    notice(h, "LAUNCHING IS NOT AVAILABLE IN THIS PROTOTYPE", txt);
}
void not_yet(home *h, const char *what) { char txt[300]; snprintf(txt, sizeof txt, "%s is not part of this native HOME prototype yet.", what); emit_json(h, "{\"ev\":\"not_available\",\"what\":\"%s\"}", what); notice(h, "NOT YET AVAILABLE", txt); }

/* ---------- state report ---------- */
static int desc_max(const home *h) { return h->d.open ? (h->d.over + DESC_STEP - 1) / DESC_STEP : 0; }
static const char *SEC_NAME[4] = { "allgames", "browse", "folder", "category" };
void home_emit_state(home *h) {
    char id[160], extra[2400] = ""; static const char *FOC[3] = { "play", "fav", "desc" }, *ZN[4] = { "games", "nav", "map", "browse" }, *OV[4] = { "none", "start", "theme", "mapdest" };
    jesc(h->n ? h->g[h->nav.sel].id : "", id, sizeof id);
    if (h->ops && h->ops->id && h->theme == TH_ARCADE && h->nav.sec != SEC_BROWSE) { extern void arcade_state_json(home *h, char *out, size_t n); arcade_state_json(h, extra, sizeof extra); }
    {   /* the section, its title and the focused Browse tile (folder:<id> / game:<id> / category:<label>) */
        size_t k = strlen(extra); char title[300], sid[200] = "null", tile[400] = "";
        jesc(home_section_title(h), title, sizeof title);
        if ((h->nav.sec == SEC_FOLDER || (h->nav.sec == SEC_BROWSE && h->nav.sec_id >= 0)) && h->nav.sec_id < h->nfold) jesc(h->fold[h->nav.sec_id].id, sid, sizeof sid);
        else if (h->nav.sec == SEC_CAT) jesc(h->cats[h->nav.sec_id].label, sid, sizeof sid);
        if (h->nav.sec == SEC_BROWSE && h->nav.browse < h->nbi) { const bitem *it = &h->bi[h->nav.browse]; char raw[300];
            snprintf(raw, sizeof raw, "%s:%s", it->kind == BI_FOLDER ? "folder" : it->kind == BI_GAME ? "game" : "category", it->kind == BI_FOLDER ? h->fold[it->idx].id : it->kind == BI_GAME ? h->all[it->idx].id : h->cats[it->idx].label); jesc(raw, tile, sizeof tile); }
        snprintf(extra + k, sizeof extra - k, ",\"section\":\"%s\",\"section_id\":%s,\"title\":%s,\"browse\":%d,\"tiles\":%d,\"tile\":%s,\"total\":%zu",
                 SEC_NAME[h->nav.sec], sid, title, h->nav.browse, h->nbi, tile[0] ? tile : "null", h->nall);
    }
    emit_json(h, "{\"ev\":\"home\",\"theme\":\"%s\",\"screen\":\"%s\",\"zone\":\"%s\",\"index\":%d,\"count\":%zu,\"id\":%s,\"nav\":%d,\"landmark\":%d,\"overlay\":\"%s\",\"oindex\":%d,\"focus\":\"%s\",\"scroll\":%d,\"scroll_max\":%d,\"notice\":%s%s}",
              THEME_IDS[h->theme], h->nav.screen == SCR_DETAILS ? "details" : "grid", ZN[h->nav.zone], h->nav.sel, h->n, id, h->nav.nav, h->nav.landmark, OV[h->nav.overlay], h->nav.oindex,
              FOC[h->nav.dfocus], h->nav.dscroll, desc_max(h), h->nav.notice ? "true" : "false", extra);
}

/* ---------- Game Details (home-themes.js hmDetails + hmDescSections, home.css .details, each theme's variant) ---------- */
static void details_close(home *h) {
    details *d = &h->d; if (!d->open) return;
    if (d->base.rgb) fb_free(&d->base); free(d->official); free(d->overview); free(d->official_label); free(d->single); free(d->lines); free(d->recs); memset(d, 0, sizeof *d); h->nav.dfocus = DF_PLAY; h->nav.dscroll = 0;
}
static void add_line(details *d, int font, uint32_t rgb, double top, double lh, char *s, size_t len, double sp, double shear, int bold) {
    if (d->nlines == d->cap) { d->cap = d->cap ? d->cap * 2 : 64; d->lines = realloc(d->lines, d->cap * sizeof *d->lines); }
    dline *l = &d->lines[d->nlines++]; l->font = font; l->rgb = rgb; l->y_top = top; l->line_h = lh; l->s = s; l->len = len; l->spacing = sp; l->shear = shear; l->bold = bold;
}
static char *read_text_field(const jval *o, const char *k) { const jval *v = json_get(o, k); return v && v->t == J_STR && v->s[0] ? sdup(v->s) : NULL; }
static double layout_paras(home *h, details *d, char *text, double y, int width) {
    const dstyle *S = h->ops->details; const font *f = &h->f[S->desc_font]; double lh = f->px * S->desc_lh; span ln[256]; int first = 1; char *p = text;
    while (p && *p) {
        char *e = strstr(p, "\n\n"); size_t plen = e ? (size_t)(e - p) : strlen(p);
        while (plen && (p[plen - 1] == ' ' || p[plen - 1] == '\n')) plen--; while (plen && (*p == ' ' || *p == '\n')) { p++; plen--; }
        if (plen) {
            for (size_t i = 0; i < plen; i++) if (p[i] == '\n') p[i] = ' ';
            char save = p[plen]; p[plen] = 0; size_t n = text_wrap(f, p, width, 0, ln, 256); p[plen] = save;
            if (!first) y += 8; first = 0;
            for (size_t i = 0; i < n; i++) { add_line(d, S->desc_font, 0xE4E2DC, y, lh, p + ln[i].start, ln[i].len, 0, 0, 0); y += lh; }
        }
        if (!e) break; p = e + 2;
    }
    return y;
}
static void drec(details *d, const char *name, int x0, int y0, int x1, int y1) {
    if (d->nrecs == d->rcap) { d->rcap = d->rcap ? d->rcap * 2 : 32; d->recs = realloc(d->recs, d->rcap * sizeof *d->recs); }
    layrec *r = &d->recs[d->nrecs++]; snprintf(r->name, sizeof r->name, "%s", name); r->b.x0 = x0; r->b.y0 = y0; r->b.x1 = x1; r->b.y1 = y1;
}
int open_details(home *h, int gi) {
    details *d = &h->d; details_close(h); hgame *g = &h->g[gi]; const dstyle *S = h->ops->details; char p[1200], err[256]; uint8_t *buf; size_t len; char jerr[160];
    if (fb_init(&d->base, CCS_FB_W, CCS_FB_H)) return -1;
    d->open = 1; d->game = gi; int B = S->border, bottom = CCS_FB_H - B - 60;
    /* texts (home-model.js + N3 effective content): the dataset's sections, a personal single block, or the quiet note; an empty
     * Official description shows no section at all (the Game overview stands alone); an edited or custom one says so */
    d->kind = DK_NONE;
    if (g->text) { pjoin(h, g->text, p, sizeof p); if (!ccs_read_file(p, &buf, &len, 1u << 20)) { jval *o = json_parse((const char *)buf, len, jerr, sizeof jerr); free(buf);
        if (o && o->t == J_OBJ) { d->official = read_text_field(o, "official"); d->overview = read_text_field(o, "overview"); d->official_label = read_text_field(o, "official_label"); d->single = read_text_field(o, "description");
            d->kind = d->official || d->overview ? DK_TWO : d->single ? DK_ONE : DK_NONE; } json_free(o); } }
    framebuffer *fb = &d->base; fb_clear(fb, 0x07, 0x07, 0x0A);
    image bg; if (g->hero) { pjoin(h, g->hero, p, sizeof p); if (!img_load(p, &bg, 0, err, sizeof err)) {   /* the backdrop was composed for a 4 px frame */
        int aw = CCS_FB_W - 2 * B, ah = CCS_FB_H - 2 * B; if (bg.w == aw && bg.h == ah) gfx_image(fb, FULL, &bg, B, B, 255);
        else { image sc; if (!(S->pixelated ? img_scale_nearest(&bg, &sc, aw, ah) : img_scale(&bg, &sc, aw, ah))) { gfx_image(fb, FULL, &sc, B, B, 255); img_free(&sc); } }
        img_free(&bg); } }
    gfx_gradient(fb, FULL, B, B, CCS_FB_W - 2 * B, CCS_FB_H - 2 * B, 1, S->vt, 4); gfx_gradient(fb, FULL, B, B, CCS_FB_W - 2 * B, CCS_FB_H - 2 * B, 0, S->hz, 4);
    if (B) gfx_rrect_ring(fb, FULL, 0, 0, CCS_FB_W, CCS_FB_H, 0, B, RGBA(S->border_rgb, 255));
    if (S->inset) gfx_rrect_ring(fb, FULL, B, B, CCS_FB_W - 2 * B, CCS_FB_H - 2 * B, 0, S->inset, RGBA(S->inset_rgb, 255));
    int cx = B + 70, cy = B + 56, cw = 340, ch = 466;
    {   /* the whole cover at the top of its column, centered across it; shadow, ring and corners hug the art */
        int ox, oy; const image *im = cover(h, gi, cw, ch, 1, &ox, &oy); int ax = cx + ox, ay = cy, aw = im->w, ah = im->h, rr = S->cover_ring;
        gfx_shadow(fb, FULL, ax, ay + 24, aw, ah, 30, RGBA(0, 217));
        if (rr) gfx_rrect(fb, FULL, ax - rr, ay - rr, aw + 2 * rr, ah + 2 * rr, S->cover_radius + rr, RGBA(S->cover_ring_rgb, S->cover_ring_a));
        gfx_rrect_image(fb, FULL, im, ax, ay, S->cover_radius); drec(d, "cover", ax - rr, ay - rr, ax + aw + rr, ay + ah + rr);
    }
    int X = B + 70 + 340 + 44, W = CCS_FB_W - B - 70 - X; double y = cy; span ln[8];
    const font *fh = &h->f[S->title_font]; size_t n = text_wrap(fh, g->title, W, S->title_spacing, ln, 8); double lh = fh->px * S->title_lh;
    for (size_t i = 0; i < n; i++) { int base = baseline_in(fh, y + i * lh, lh); draw_glow_text(fb, h, S->title_font, X, base, g->title + ln[i].start, ln[i].len, 12, RGBA(0, 230), S->title_spacing); }
    for (size_t i = 0; i < n; i++) { int base = baseline_in(fh, y + i * lh, lh); text_draw(fb, NULL, FULL, fh, X, base + 2, g->title + ln[i].start, ln[i].len, RGBA(0, 255), S->title_spacing, 0); text_draw(fb, NULL, FULL, fh, X, base, g->title + ln[i].start, ln[i].len, RGBA(S->title_rgb, 255), S->title_spacing, 0);
        drec(d, "title", X, base - fh->ascent, X + text_w(fh, g->title + ln[i].start, ln[i].len, S->title_spacing), base + fh->descent); }
    y += n * lh; double mb = 6;
    if (g->version_label) {
        y += mb; const font *fb7 = &h->f[S->ver_bold], *fr = &h->f[S->ver_font]; int base = baseline_normal(fb7, y); char lab[300];
        text_draw(fb, NULL, FULL, fb7, X, base + 1, "Version", 7, RGBA(0, 255), 0, 0); int xe = text_draw(fb, NULL, FULL, fb7, X, base, "Version", 7, RGBA(0xD6D4CE, 255), 0, 0);
        xe += text_w(fr, " ", 1, 0); text_fit(fr, g->version_label, X + W - xe, 0, lab, sizeof lab); T(fb, FULL, fr, xe, base + 1, lab, 0, 255, 0); int xr = T(fb, FULL, fr, xe, base, lab, 0xD6D4CE, 255, 0);
        drec(d, "version", X, base - fb7->ascent, xr, base + fb7->descent); y += lh_normal(fb7); mb = 8;
    }
    y += mb > 10 ? mb : 10;
    { static const char *K[5] = { "Year", "Players", "Genre", "Publisher", "Developer" }, *IK[5] = { "year", "players", "genre", "publisher", "developer" }; const char *V[5] = { g->year, g->players, g->genre, g->publisher, g->developer };
      const font *fk = &h->f[S->meta_key], *fv = &h->f[S->meta_val]; double x = X, rowy = y, rowh = S->meta_rowh > 18 ? S->meta_rowh : 18; int any = 0;
      for (int i = 0; i < 5; i++) {
          if (!V[i]) continue; any = 1; char val[200]; int kw = text_w(fk, K[i], strlen(K[i]), 0) + (S->meta_key == S->meta_val ? 1 : 0);
          text_fit(fv, V[i], W - (18 + 6 + kw + 2 + 6), 0, val, sizeof val);
          int w = 18 + 6 + kw + 2 + 6 + text_w(fv, val, strlen(val), 0);
          if (x > X && x + w > X + W) { x = X; rowy += rowh + 8; }
          int base = baseline_center(fk, rowy, rowh); const image *ic = icon(h, IK[i], 18);
          if (ic) gfx_image(fb, FULL, ic, iround(x), iround(rowy + (rowh - 18) / 2), 255); double tx = x + 18 + 6;
          if (S->meta_key == S->meta_val) { Tb(fb, FULL, fk, tx, base + 1, K[i], 0, 255, 0); tx = Tb(fb, FULL, fk, tx, base, K[i], 0xFFFFFF, 255, 0) + 2 + 6; }
          else { T(fb, FULL, fk, tx, base + 1, K[i], 0, 255, 0); tx = T(fb, FULL, fk, tx, base, K[i], 0xFFFFFF, 255, 0) + 2 + 6; }
          T(fb, FULL, fv, tx, base + 1, val, 0, 255, 0); T(fb, FULL, fv, tx, base, val, 0xE6E4DE, 255, 0);
          drec(d, "meta", iround(x), iround(rowy), iround(x) + w, iround(rowy + rowh)); x += w + 18;
      }
      if (!any) { int base = baseline_normal(fv, rowy); const char *t = "No further details are available for this game."; text_draw(fb, NULL, FULL, fv, X, base, t, strlen(t), RGBA(0xA9A7A2, 255), 0, 0.25); drec(d, "meta", X, iround(rowy), X + text_w(fv, t, strlen(t), 0) + 4, iround(rowy + rowh)); }
      y = rowy + rowh + 18; }
    const font *fa = &h->f[S->act_font]; double acth = S->act_bw + 12 + lh_normal(fa) + 12 + S->act_bw; d->acts_y = iround(y); d->act_h = iround(acth); d->acts_top = y;
    d->play_x = X; d->play_w = S->act_bw * 2 + 48 + text_w(fa, "\xE2\x96\xB6 Play Game", 13, 0); d->fav_x = X + d->play_w + 16; d->fav_w = S->act_bw * 2 + 48 + text_w(fa, "\xE2\x98\x86 Add Favorite", 16, 0);
    d->desc_x = X; d->desc_y = iround(y + acth + 18); d->desc_w = W; d->desc_h = bottom - d->desc_y; if (d->desc_h < 120) d->desc_h = 120;
    d->avail = d->desc_h - 2 * S->desc_bw - 28;
    int tw = W - 2 * S->desc_bw - 36 - 12; double ty = 0; const font *fd = &h->f[S->dh_font]; double hl = fd->px * S->dh_lh;
    if (d->kind == DK_TWO) {
        int first = 1;
        if (d->official) { const char *lab = d->official_label ? d->official_label : "OFFICIAL DESCRIPTION"; add_line(d, S->dh_font, 0xBDBAB3, ty, hl, (char *)lab, strlen(lab), S->dh_spacing, 0, S->dh_bold); ty += hl + 4; ty = layout_paras(h, d, d->official, ty, tw); first = 0; }
        if (d->overview) { if (!first) ty += 14; add_line(d, S->dh_font, 0xBDBAB3, ty, hl, (char *)"GAME OVERVIEW", 13, S->dh_spacing, 0, S->dh_bold); ty += hl + 4; ty = layout_paras(h, d, d->overview, ty, tw); }
    } else if (d->kind == DK_ONE) {
        char *w = d->single; int sp = 0; for (char *r = d->single; *r; r++) { int ws = *r == ' ' || *r == '\n' || *r == '\r' || *r == '\t'; if (ws) { if (!sp && w != d->single) *w++ = ' '; sp = 1; } else { *w++ = *r; sp = 0; } }
        while (w > d->single && w[-1] == ' ') w--; *w = 0; ty = layout_paras(h, d, d->single, ty, tw);
    } else {
        const font *fn = &h->f[S->note_font]; int nw = text_w(fn, NODESC, strlen(NODESC), 0);
        d->note.x0 = X; d->note.y0 = d->desc_y + 4; d->note.x1 = X + 1 + 12 + nw + 12 + 1; d->note.y1 = d->note.y0 + 1 + 6 + iround(lh_normal(fn)) + 6 + 1;
    }
    d->content_h = ty; { double o = ty - d->avail; d->over = o > 0 ? (int)o + ((double)(int)o < o) : 0; }
    char id[160]; jesc(g->id, id, sizeof id);
    emit_json(h, "{\"ev\":\"details_opened\",\"id\":%s,\"theme\":\"%s\",\"desc\":\"%s\",\"official\":%s,\"official_label\":\"%s\",\"overview\":%s,\"hero\":\"%s\",\"lines\":%zu,\"scroll_max\":%d}", id, THEME_IDS[h->theme],
              d->kind == DK_TWO ? "two" : d->kind == DK_ONE ? "one" : "none", d->official ? "true" : "false", d->official_label ? d->official_label : "", d->overview ? "true" : "false", g->hero_kind ? g->hero_kind : "none", d->nlines, (d->over + DESC_STEP - 1) / DESC_STEP);
    return 0;
}
static void draw_button(framebuffer *fb, home *h, int x, int y, int w, int hh, const char *label, int focus) {
    const dstyle *S = h->ops->details; int r = S->act_radius > hh / 2 ? hh / 2 : S->act_radius;
    if (focus) gfx_shadow(fb, FULL, x, y, w, hh, 11, RGBA(RED, 153));
    gfx_rrect(fb, FULL, x, y, w, hh, r, focus ? RGBA(RED, 255) : RGBA(S->act_bg, S->act_bg_a)); gfx_rrect_ring(fb, FULL, x, y, w, hh, r, S->act_bw, focus ? RGBA(RED, 255) : RGBA(S->act_border, S->act_border_a));
    const font *f = &h->f[S->act_font]; T(fb, FULL, f, x + S->act_bw + 24, baseline_center(f, y + S->act_bw, hh - 2 * S->act_bw), label, focus ? 0xFFFFFF : 0xE9E7E1, 255, 0);
}
static void render_details(home *h, framebuffer *fb) {
    details *d = &h->d; const dstyle *S = h->ops->details; memcpy(fb->rgb, d->base.rgb, (size_t)fb->w * fb->h * 3);
    for (size_t i = 0; i < d->nrecs; i++) LR(h, d->recs[i].name, d->recs[i].b.x0, d->recs[i].b.y0, d->recs[i].b.x1, d->recs[i].b.y1);
    draw_button(fb, h, d->play_x, d->acts_y, d->play_w, d->act_h, "\xE2\x96\xB6 Play Game", h->nav.dfocus == DF_PLAY); LR(h, "button-play", d->play_x, d->acts_y, d->play_x + d->play_w, d->acts_y + d->act_h);
    draw_button(fb, h, d->fav_x, d->acts_y, d->fav_w, d->act_h, "\xE2\x98\x86 Add Favorite", h->nav.dfocus == DF_FAV); LR(h, "button-fav", d->fav_x, d->acts_y, d->fav_x + d->fav_w, d->acts_y + d->act_h);
    if (d->kind == DK_NONE) {
        box4 n = d->note; const font *fn = &h->f[S->note_font];
        gfx_rrect(fb, FULL, n.x0, n.y0, n.x1 - n.x0, n.y1 - n.y0, S->desc_radius > 6 ? 6 : S->desc_radius, RGBA(S->desc_bg, S->desc_bg_a)); gfx_rrect_ring(fb, FULL, n.x0, n.y0, n.x1 - n.x0, n.y1 - n.y0, S->desc_radius > 6 ? 6 : S->desc_radius, 1, RGBA(S->desc_border, S->desc_border_a));
        text_draw(fb, NULL, FULL, fn, n.x0 + 13, baseline_normal(fn, n.y0 + 7), NODESC, strlen(NODESC), RGBA(0xCFCDC7, 255), 0, 0.25); LR(h, "nodesc", n.x0, n.y0, n.x1, n.y1);
        return;
    }
    int x = d->desc_x, y = d->desc_y, w = d->desc_w, hh = d->desc_h, foc = h->nav.dfocus == DF_DESC, bw = S->desc_bw;
    gfx_rrect(fb, FULL, x, y, w, hh, S->desc_radius, RGBA(S->desc_bg, S->desc_bg_a)); gfx_rrect_ring(fb, FULL, x, y, w, hh, S->desc_radius, bw, foc ? RGBA(RED, 255) : RGBA(S->desc_border, S->desc_border_a)); LR(h, "desc", x, y, x + w, y + hh);
    int sy = h->nav.dscroll * DESC_STEP; if (sy > d->over) sy = d->over;
    rect clip = { x + bw, y + bw, x + w - bw, y + hh - bw }; double top = y + bw + 14 - sy;
    for (size_t i = 0; i < d->nlines; i++) { dline *l = &d->lines[i]; const font *f = &h->f[l->font]; int base = baseline_in(f, top + l->y_top, l->line_h); if (base - f->ascent > clip.y1 || base + f->descent < clip.y0) continue;
        if (l->bold) text_draw(fb, NULL, clip, f, x + bw + 18 + 1, base, l->s, l->len, RGBA(l->rgb, 255), l->spacing, l->shear);
        text_draw(fb, NULL, clip, f, x + bw + 18, base, l->s, l->len, RGBA(l->rgb, 255), l->spacing, l->shear); }
    if (d->over > 0) {
        int bx = x + w - 12, by = y + bw + 6, bh = hh - 2 * bw - 12; gfx_rrect(fb, FULL, bx, by, 6, bh, 3, RGBA(0xFFFFFF, foc ? 51 : 31));
        double hp = d->avail / d->content_h * 100; if (hp > 100) hp = 100; if (hp < 14) hp = 14; double tp = (double)sy / d->over * (100 - hp);
        gfx_rrect(fb, FULL, bx, by + iround(bh * tp / 100), 6, iround(bh * hp / 100), 3, RGBA(0xFFFFFF, foc ? 200 : 140));
    }
}

/* ---------- controller legend (each theme's badge style; the items say what each button does right now) ---------- */
typedef struct { const char *key, *label; } litem;
static int legend_list(home *h, litem *it) {
    int n = 0;
    #define L(k, t) do { it[n].key = k; it[n].label = t; n++; } while (0)
    if (h->nav.overlay != OV_NONE) { L("dpad", "Move"); L("A", "Choose"); L("B", "Close"); return n; }
    if (h->nav.screen == SCR_DETAILS) {
        if (h->nav.dfocus == DF_DESC) { L("dpad", "Scroll"); L("B", "Back"); } else { L("dpad", "Move"); L("A", h->nav.dfocus == DF_PLAY ? "Play Game" : "Add Favorite"); L("B", "Back"); }
        L("START", "Menu"); return n;
    }
    if (h->nav.zone == Z_BROWSE) {   /* hmLegendItems: a game tile inside a folder launches; folders and categories open */
        const bitem *bt = h->nav.browse < h->nbi ? &h->bi[h->nav.browse] : NULL;
        if (bt && bt->kind == BI_GAME) { L("A", "Launch"); L("SELECT", "Details"); L("B", "Back"); L("START", "Menu"); } else { L("dpad", "Move"); L("A", "Open"); L("B", "Back"); L("START", "Menu"); }
        return n;
    }
    if (h->nav.zone == Z_MAP) { L("dpad", "Travel"); L("A", "Enter"); L("SELECT", "Destinations"); L("START", "Menu"); return n; }
    if (h->nav.zone == Z_NAV) { L("dpad", "Move"); L("A", "Open"); L("B", "Back to games"); L("START", "Menu"); return n; }
    if (h->theme == TH_MAP) L("dpad", "Navigate");
    L("A", "Launch"); L("SELECT", "Details"); L("START", "Menu");   /* B has nothing to go back to at the top level */
    if (h->nav.sec != SEC_ALL) L("B", "Back");                      /* a folder's or a category's games: B returns to Browse */
    #undef L
    return n;
}
/* style: 0 Arcade Grid, 1 Box Art Showcase, 2 Featured Shelf, 3 Adventure Map (hmLegend + home.css .legend per theme) */
static int legend_item(framebuffer *fb, home *h, int x, int cy, const char *key, const char *label, int style, int draw) {
    int x0 = x; char up[80];
    const tsprite *sp = NULL;
    if (style == 3 && !strcmp(key, "dpad")) sp = tsp(h, "badge-dpad"); else if (style == 3 && (!strcmp(key, "A") || !strcmp(key, "B"))) sp = tsp(h, !strcmp(key, "A") ? "badge-a" : "badge-b");
    else if ((style == 0 || style == 2) && !strcmp(key, "A")) sp = tsp(h, "badge-a");
    if (sp) { if (draw) { image sc; if (sp->im.w == 30 && sp->im.h == 30) gfx_image(fb, FULL, &sp->im, x, cy - 15, 255); else if (!(style == 3 ? img_scale_nearest(&sp->im, &sc, 30, 30) : img_scale(&sp->im, &sc, 30, 30))) { gfx_image(fb, FULL, &sc, x, cy - 15, 255); img_free(&sc); } } x += 30; }
    else if (!strcmp(key, "A") || !strcmp(key, "B") || !strcmp(key, "dpad")) {
        if (draw) { gfx_rrect(fb, FULL, x, cy - 15 + 2, 30, 30, 15, RGBA(0, 153)); gfx_rrect(fb, FULL, x, cy - 15, 30, 30, 15, RGBA(RED, 255));
            const font *f = !strcmp(key, "dpad") ? &h->f[F_SYM16] : &h->f[F_S800_15]; const char *t = !strcmp(key, "dpad") ? "\xE2\x9C\x9A" : key; int tw = text_w(f, t, strlen(t), 0);
            T(fb, FULL, f, x + (30 - tw) / 2.0, baseline_center(f, cy - 15, 30), t, 0xFFFFFF, 255, 0); } x += 30;
    } else {
        const font *f = &h->f[F_S800_10]; int tw = text_w(f, key, strlen(key), .5), w = tw + 18;
        if (draw) { gfx_rrect(fb, FULL, x, cy - 13 + 2, w, 26, 7, RGBA(0, 153)); gfx_rrect(fb, FULL, x, cy - 13, w, 26, 7, RGBA(0x55565C, 255)); T(fb, FULL, f, x + 9, baseline_center(f, cy - 13, 26), key, 0xFFFFFF, 255, .5); }
        x += w;
    }
    x += 10;
    const font *f; uint32_t col; double sp2 = 0; const char *lab = label;
    if (style == 0) { f = &h->f[F_S600_14]; col = 0xF0EEF0; }
    else if (style == 1) { f = &h->f[F_S400_14]; col = 0xF2F0EA; }
    else if (style == 2) { f = &h->f[F_SILK10]; col = 0xF1EFE9; sp2 = .5; }
    else { f = &h->f[F_PX9]; col = 0xF3E7D3; sp2 = .3; }
    if (style >= 2) { size_t i = 0; for (; label[i] && i < sizeof up - 1; i++) up[i] = (char)((label[i] >= 'a' && label[i] <= 'z') ? label[i] - 32 : label[i]); up[i] = 0; lab = up; }
    int xe = draw ? T(fb, FULL, f, x, baseline_center(f, cy - 25, 50), lab, col, 255, sp2) : x + text_w(f, lab, strlen(lab), sp2);
    if (draw) LR(h, "legend", x0, cy - 15, xe, cy + 17);
    return xe + 46;
}
void legend_items(home *h, framebuffer *fb, box4 box, int center, int style) {
    litem it[6]; int n = legend_list(h, it), cy = (box.y0 + box.y1) / 2, x = box.x0;
    if (center) { int w = 0, xx = 0; for (int i = 0; i < n; i++) xx = legend_item(fb, h, xx, cy, it[i].key, it[i].label, style, 0); w = xx - 46; x = box.x0 + ((box.x1 - box.x0) - w) / 2; }
    for (int i = 0; i < n; i++) x = legend_item(fb, h, x, cy, it[i].key, it[i].label, style, 1);
    LR(h, "legend-box", box.x0, box.y0, box.x1, box.y1);
}
static void details_legend(home *h, framebuffer *fb) {
    int B = h->ops->details->border; box4 b = { B + 70, CCS_FB_H - B - 50, CCS_FB_W - B - 70, CCS_FB_H - B - 18 };
    if (h->theme == TH_ARCADE) { b.x0 = 74; b.y0 = 686 - 16; b.y1 = 686 + 16; }
    legend_items(h, fb, b, 0, h->theme);
}

/* ---------- sections (home-state.js go / dest / B): All Games, Browse, a folder's or a category's games ---------- */
int home_active_nav(const home *h) { return h->nav.sec == SEC_ALL ? 0 : 1; }
int home_landmark_active(const home *h, int k) {
    if (k == 0) return h->nav.sec == SEC_ALL; if (k == 1) return h->nav.sec == SEC_BROWSE || h->nav.sec == SEC_CAT;
    if (k == 3 || k == 4) return h->nav.sec == SEC_FOLDER && h->map_folder[k - 3] >= 0 && h->nav.sec_id == h->map_folder[k - 3];
    return 0;
}
const char *home_section_title(const home *h) {
    if (h->nav.sec == SEC_FOLDER && h->nav.sec_id >= 0 && h->nav.sec_id < h->nfold) return h->fold[h->nav.sec_id].name;
    if (h->nav.sec == SEC_CAT && h->nav.sec_id >= 0 && h->nav.sec_id < h->ncat) return h->cats[h->nav.sec_id].label;
    return h->nav.sec == SEC_BROWSE ? "Browse" : "All Games";
}
int home_section_zone(const home *h) { return h->nav.sec == SEC_BROWSE ? Z_BROWSE : Z_GAMES; }
static void view_set(home *h, const int *ids, int n) {   /* the current listing: All Games (ids NULL) or shallow copies of the chosen games */
    free(h->view); free(h->vmap); h->view = NULL; h->vmap = NULL; h->g = h->all; h->n = h->nall;
    if (!ids) return;
    h->view = calloc(n > 0 ? (size_t)n : 1, sizeof *h->view); h->vmap = calloc(n > 0 ? (size_t)n : 1, sizeof *h->vmap);
    if (!h->view || !h->vmap) { free(h->view); free(h->vmap); h->view = NULL; h->vmap = NULL; return; }
    for (int i = 0; i < n; i++) { h->view[i] = h->all[ids[i]]; h->vmap[i] = ids[i]; }
    h->g = h->view; h->n = (size_t)n;
}
static void browse_items(home *h) {   /* home-state.js homeBrowseItems: folders first, then (inside a folder) its own games, then (at the top) the categories */
    int parent = h->nav.sec_id, nf = 0, ng = parent >= 0 ? h->fold[parent].ng : 0, nc = parent >= 0 ? 0 : h->ncat;
    for (int i = 0; i < h->nfold; i++) if (h->fold[i].parent == parent) nf++;
    free(h->bi); h->bi = calloc((size_t)(nf + ng + nc) + 1, sizeof *h->bi); h->nbi = 0; h->bi_folders = nf; h->bi_games = ng; if (!h->bi) return;
    for (int i = 0; i < h->nfold; i++) if (h->fold[i].parent == parent) h->bi[h->nbi++] = (bitem){ BI_FOLDER, i };
    for (int i = 0; i < ng; i++) h->bi[h->nbi++] = (bitem){ BI_GAME, h->fold[parent].games[i] };
    for (int i = 0; i < nc; i++) h->bi[h->nbi++] = (bitem){ BI_CAT, i };
}
static void remember(home *h) {
    navstate *N = &h->nav;
    if (N->sec == SEC_ALL) h->memo_all = N->sel; else if (N->sec == SEC_FOLDER) h->memo_folder[N->sec_id] = N->sel;
    else if (N->sec == SEC_CAT) h->memo_cat[N->sec_id] = N->sel; else h->memo_browse[N->sec_id + 1] = N->browse;
}
static void theme_reset(home *h) {   /* a listing change: the theme's own caches (pages, key art, frames) start over */
    char err[200]; if (!h->ops) return; if (h->ops->fini) h->ops->fini(h);
    if (h->ops->init && h->ops->init(h, err, sizeof err)) emit_json(h, "{\"ev\":\"theme_reset_failed\",\"error\":\"%s\"}", err);
}
static void section_set(home *h, int sec, int id) {
    navstate *N = &h->nav; remember(h); details_close(h);
    N->sec = sec; N->sec_id = id;
    if (sec == SEC_FOLDER) view_set(h, h->fold[id].games, h->fold[id].ng); else if (sec == SEC_CAT) view_set(h, h->cats[id].games, h->cats[id].ng); else view_set(h, NULL, 0);
    if (sec == SEC_ALL) N->sel = h->memo_all; else if (sec == SEC_FOLDER) N->sel = h->memo_folder[id]; else if (sec == SEC_CAT) N->sel = h->memo_cat[id];
    if (sec == SEC_BROWSE) { browse_items(h); N->browse = h->memo_browse[id + 1]; if (N->browse >= h->nbi) N->browse = h->nbi ? h->nbi - 1 : 0; if (N->browse < 0) N->browse = 0; }
    if (N->sel >= (int)h->n) N->sel = h->n ? (int)h->n - 1 : 0; if (N->sel < 0) N->sel = 0;
    N->screen = SCR_GRID; N->overlay = OV_NONE; N->zone = home_section_zone(h); N->nav = home_active_nav(h); h->gen++;
    theme_reset(h);
    char t[300]; jesc(home_section_title(h), t, sizeof t);
    emit_json(h, "{\"ev\":\"section\",\"section\":\"%s\",\"title\":%s,\"count\":%zu,\"tiles\":%d}", SEC_NAME[sec], t, sec == SEC_BROWSE ? 0 : h->n, sec == SEC_BROWSE ? h->nbi : 0);
}
void home_go_all(home *h) { section_set(h, SEC_ALL, -1); }
void home_go_browse(home *h, int parent) { section_set(h, SEC_BROWSE, parent >= 0 && parent < h->nfold ? parent : -1); }
void home_go_folder(home *h, int f) { if (f >= 0 && f < h->nfold) section_set(h, SEC_FOLDER, f); }
static void home_go_cat(home *h, int c) { if (c >= 0 && c < h->ncat) section_set(h, SEC_CAT, c); }
static void focus_tile(home *h, int kind, int idx) { for (int i = 0; i < h->nbi; i++) if (h->bi[i].kind == kind && h->bi[i].idx == idx) h->nav.browse = i; }
int home_back(home *h) {
    navstate *N = &h->nav; int id = N->sec_id;
    if (N->sec == SEC_FOLDER) { section_set(h, SEC_BROWSE, h->fold[id].parent); focus_tile(h, BI_FOLDER, id); return 1; }
    if (N->sec == SEC_CAT) { section_set(h, SEC_BROWSE, -1); focus_tile(h, BI_CAT, id); return 1; }
    if (N->sec == SEC_BROWSE) { if (id >= 0) { section_set(h, SEC_BROWSE, h->fold[id].parent); focus_tile(h, BI_FOLDER, id); } else section_set(h, SEC_ALL, -1); return 1; }
    return 0;
}
static int grid4(int i, int n, int dir) {   /* home-state.js homeGridMove, four columns, no wrap; a short last row clamps */
    if (!n) return 0; int r = i / 4, c = i % 4, rows = (n + 3) / 4;
    if (dir == D_LEFT) return c > 0 ? i - 1 : i; if (dir == D_RIGHT) return c < 3 && i + 1 < n ? i + 1 : i;
    if (dir == D_UP) return r > 0 ? i - 4 : i; if (dir == D_DOWN) { if (r >= rows - 1) return i; return i + 4 < n ? i + 4 : n - 1; }
    return i;
}
static void browse_input(home *h, button b) {
    navstate *N = &h->nav; int n = h->nbi, dir = dir_of(b);
    if (N->browse >= n) N->browse = n ? n - 1 : 0;
    if (dir >= 0) { int before = N->browse; N->browse = grid4(N->browse, n, dir); if (dir == D_UP && N->browse == before && before / 4 == 0) { N->zone = h->theme == TH_MAP ? Z_MAP : Z_NAV; N->nav = home_active_nav(h); } return; }
    if (b == B_B) { home_back(h); return; }
    if (!n) return;
    const bitem it = h->bi[N->browse];
    if (b == B_A) { if (it.kind == BI_GAME) launch_unavailable(h, it.idx); else if (it.kind == BI_FOLDER) { if (h->fold[it.idx].nchild) home_go_browse(h, it.idx); else home_go_folder(h, it.idx); } else home_go_cat(h, it.idx); }
    else if (b == B_SELECT && it.kind == BI_GAME) { if (!open_details(h, it.idx)) { N->screen = SCR_DETAILS; N->dfocus = DF_PLAY; N->dbutton = DF_PLAY; N->dscroll = 0; N->dzone = Z_BROWSE; } }
}

/* the Browse panel (home-themes.js hmBrowse + home.css .section / .tile / .tiles-h, each theme's variant) */
typedef struct { box4 box; int bw; uint32_t border; int border_a, radius; uint32_t bg; int bg_a, inset; uint32_t inset_rgb; int h3_font; uint32_t h3_rgb; double h3_sp;
                 uint32_t tile_bg; int tile_bg_a; uint32_t tile_border; int tile_border_a, tile_radius; uint32_t tcap; int tcap_a, nm_font, sm_font; uint32_t sm_rgb, focus; } bstyle;
static const bstyle BSTYLE[N_THEMES] = {
    { { 246, 101, 1232, 622 }, 3, 0x2F3F8C, 255, 8, 0x090D26, 240, 2, 0x070A22, F_PX11, 0xFFB3B7, 1, 0x0F1538, 255, 0x273576, 255, 8, 0x0B1030, 255, F_S600_15, F_S400_12, 0xC9C7C1, RED },
    { { 60, 90, 1220, 660 }, 1, 0xFFFFFF, 66, 14, 0x07070A, 230, 0, 0, F_CIN16, 0xF3EFE6, 2, 0x16161C, 235, 0xFFFFFF, 46, 10, 0x0C0C10, 242, F_S600_15, F_S400_12, 0xC9C7C1, RED },
    { { 250, 96, 1254, 656 }, 3, 0x5B1A21, 255, 10, 0x11080A, 242, 0, 0, F_SILK13, 0xFF9AA0, 1, 0x1B0F12, 255, 0x4A1C22, 255, 8, 0x140A0C, 255, F_S600_15, F_S400_12, 0xC9C7C1, RED },
    { { 120, 110, 1160, 530 }, 4, 0x6B4A2B, 255, 4, 0x100A08, 245, 2, RED2, F_PX11, 0xFFD27A, 1, 0x20140F, 255, 0x6B4A2B, 255, 4, 0x160D0A, 255, F_PX11, F_PX8, 0xD8C9A8, RED2 } };
static const image *folder_tile_art(home *h, int f, int w, int hh) {   /* the folder's custom artwork at the tile size (object-fit: cover), kept until the size changes */
    hfolder *fo = &h->fold[f]; if (!fo->art) return NULL; if (fo->tile.px && fo->tile.w == w && fo->tile.h == hh) return &fo->tile;
    img_free(&fo->tile); char p[1200], err[200]; image src; pjoin(h, fo->art, p, sizeof p); if (img_load(p, &src, 0, err, sizeof err)) return NULL;
    int bad = img_cover(&src, &fo->tile, w, hh); img_free(&src); return bad ? NULL : &fo->tile;
}
static void tile_caption(home *h, framebuffer *fb, rect clip, const bstyle *S, int x, int y, int w, int hh, int pad_top, const char *name, const char *sub) {
    const font *fn = &h->f[S->nm_font], *fs = &h->f[S->sm_font]; char a[300], b2[300]; int iw = w - 20;
    text_fit(fn, name, iw, 0, a, sizeof a); text_fit(fs, sub, iw, 0, b2, sizeof b2);
    double l1 = lh_normal(fn), l2 = lh_normal(fs), content = l1 + 2 + l2, top = y + pad_top + (hh - pad_top - 6 - content) / 2.0;
    T(fb, clip, fn, x + 10, baseline_normal(fn, top), a, 0xFFFFFF, 255, 0); T(fb, clip, fs, x + 10, baseline_normal(fs, top + l1 + 2), b2, S->sm_rgb, 255, 0);
}
static void render_browse(home *h, framebuffer *fb) {
    const bstyle *S = &BSTYLE[h->theme]; box4 B = S->box; int W = B.x1 - B.x0, H = B.y1 - B.y0, parent = h->nav.sec_id;
    if (S->bg_a) gfx_rrect(fb, FULL, B.x0, B.y0, W, H, S->radius, RGBA(S->bg, S->bg_a));
    gfx_rrect_ring(fb, FULL, B.x0, B.y0, W, H, S->radius, S->bw, RGBA(S->border, S->border_a));
    if (S->inset) gfx_rrect_ring(fb, FULL, B.x0 + S->bw, B.y0 + S->bw, W - 2 * S->bw, H - 2 * S->bw, S->radius > S->bw ? S->radius - S->bw : 0, S->inset, RGBA(S->inset_rgb, 255));
    LR(h, "browse-panel", B.x0, B.y0, B.x1, B.y1);
    int cx = B.x0 + S->bw + 20, cw = W - 2 * S->bw - 40; double y = B.y0 + S->bw + 16;
    const font *f3 = &h->f[S->h3_font]; T(fb, FULL, f3, cx, baseline_normal(f3, y), "BROWSE", S->h3_rgb, 255, S->h3_sp); LRtext(h, "browse-h3", f3, cx, baseline_normal(f3, y), "BROWSE", 6, S->h3_sp); y += lh_normal(f3) + 10;
    int crumb = parent >= 0;
    if (crumb) {   /* Browse > the folder path */
        char path[600] = "Browse"; int chain[12], nc = 0; for (int f = parent; f >= 0 && nc < 12; f = h->fold[f].parent) chain[nc++] = f;
        for (int i = nc - 1; i >= 0; i--) { size_t k = strlen(path); snprintf(path + k, sizeof path - k, " \xE2\x80\xBA %s", h->fold[chain[i]].name); }
        const font *fc = &h->f[F_S400_14]; char fit[600]; text_fit(fc, path, cw, 0, fit, sizeof fit); y -= 4; T(fb, FULL, fc, cx, baseline_normal(fc, y), fit, 0xCFCDC7, 255, 0); LRtext(h, "browse-crumb", fc, cx, baseline_normal(fc, y), fit, strlen(fit), 0); y += lh_normal(fc) + 10;
    }
    int inner = H - 70 - (crumb ? 26 : 0), top = iround(y); rect clip = { cx - 12, top, cx + cw + 12, top + inner };   /* overflow hidden (the focus glow may spill sideways) */
    double tw = (cw - 36) / 4.0; const int TH = 128, PITCH = 140, HDR = 50;
    int n = h->nbi, nfg = h->bi_folders + h->bi_games, rows_fg = (nfg + 3) / 4;
    #define TILE_Y(i) ((i) < nfg ? ((i) / 4) * PITCH : (rows_fg ? rows_fg * PITCH : 0) + HDR + 12 + (((i) - nfg) / 4) * PITCH)
    #define TILE_X(i) (cx + ((i) < nfg ? (i) % 4 : ((i) - nfg) % 4) * (tw + 12))
    int vis = inner / PITCH; if (vis < 1) vis = 1;
    int shift = n ? TILE_Y(h->nav.browse) - (vis - 1) * PITCH : 0; if (shift < 0) shift = 0;   /* hmBrowse: the focused row stays inside the panel */
    if (!n) {   /* .tile.msg spanning the row */
        const char *msg = parent >= 0 ? "This folder is empty. Add games using Classic Cart Studio for Windows." : "No folders yet. Create folders in Classic Cart Studio for Windows; automatic categories appear once installed games carry metadata.";
        gfx_rrect(fb, clip, cx, top, cw, TH, S->tile_radius, RGBA(S->tile_bg, S->tile_bg_a)); gfx_rrect_ring(fb, clip, cx, top, cw, TH, S->tile_radius, 2, RGBA(S->tile_border, S->tile_border_a));
        const font *fn = &h->f[S->nm_font == F_PX11 ? F_PX11 : F_S400_15]; span ln[6]; size_t k = text_wrap(fn, msg, cw - 24, 0, ln, 6); double lh = lh_normal(fn), t0 = top + (TH - k * lh) / 2;
        for (size_t i = 0; i < k; i++) { int lw = text_w(fn, msg + ln[i].start, ln[i].len, 0); text_draw(fb, NULL, clip, fn, cx + (cw - lw) / 2.0, baseline_normal(fn, t0 + i * lh), msg + ln[i].start, ln[i].len, RGBA(0xFFFFFF, 255), 0, 0); }
        LR(h, "browse-empty", cx, top, cx + cw, top + TH); return;
    }
    if (h->bi_folders + h->bi_games < n) {   /* .tiles-h */
        int hy = top + (rows_fg ? rows_fg * PITCH : 0) + 12 - shift; const font *fp = &h->f[F_PX9];
        T(fb, clip, fp, cx, baseline_in(fp, hy, 10), "AUTOMATIC CATEGORIES", 0xBDBBB5, 255, .5); if (hy >= top && hy < top + inner) LR(h, "browse-tiles-h", cx, hy, cx + cw, hy + 30);
    }
    for (int i = 0; i < n; i++) {
        int x = iround(TILE_X(i)), ty = top + TILE_Y(i) - shift, w = iround(TILE_X(i) + tw) - x, foc = h->nav.zone == Z_BROWSE && i == h->nav.browse;
        if (ty + TH < top || ty > top + inner) continue;
        const bitem *it = &h->bi[i]; char sub[200]; const char *name; int cat = it->kind == BI_CAT;
        if (foc) gfx_shadow(fb, clip, x, ty, w, TH, 10, RGBA(RED, 153));
        gfx_rrect(fb, clip, x, ty, w, TH, S->tile_radius, RGBA(cat ? (h->theme == TH_ARCADE ? 0x0C1130 : h->theme == TH_BOXART ? 0x121218 : S->tile_bg) : S->tile_bg, S->tile_bg_a));
        rect inner_clip = rect_isect(clip, rect_make(x + 2, ty + 2, w - 4, TH - 4)); int capy = ty + 2 + 72;
        if (!cat) {   /* .tcol: the custom image, or the collage of up to four covers, or the empty gradient; a game tile shows its whole cover */
            int ax = x + 2, aw = w - 4;
            if (it->kind == BI_GAME) { gfx_fill(fb, inner_clip, ax, ty + 2, aw, 72, RGBA(0x0C0C10, 255)); int ox, oy; const image *im = cover_b(h, it->idx, aw, 72, 1, &ox, &oy, 1); if (im) gfx_image(fb, inner_clip, im, ax + ox, ty + 2 + oy, 255); }
            else {
                const hfolder *fo = &h->fold[it->idx]; const image *art = folder_tile_art(h, it->idx, aw, 72);
                if (art) gfx_image(fb, inner_clip, art, ax, ty + 2, 255);
                else if (fo->ng) { gfx_fill(fb, inner_clip, ax, ty + 2, aw, 72, RGBA(0x0C0C10, 255)); int cw2 = (aw - 1) / 2, ch2 = (72 - 1) / 2;
                    for (int k = 0; k < 4 && k < fo->ng; k++) { int ox, oy, gx = ax + (k % 2) * (cw2 + 1), gy = ty + 2 + (k / 2) * (ch2 + 1), gw = k % 2 ? aw - cw2 - 1 : cw2, gh = k / 2 ? 72 - ch2 - 1 : ch2;
                        const image *im = cover_b(h, fo->games[k], gw, gh, 0, &ox, &oy, 1); if (im) gfx_image(fb, inner_clip, im, gx, gy, 255); } }
                else { stop g[2] = { { 0, 0x1D1D25, 1 }, { 1, 0x101016, 1 } }; gfx_gradient(fb, inner_clip, ax, ty + 2, aw, 72, 1, g, 2); }
            }
            gfx_fill(fb, inner_clip, x + 2, capy, w - 4, TH - 4 - 72, RGBA(S->tcap, S->tcap_a)); gfx_fill(fb, inner_clip, x + 2, capy, w - 4, 1, RGBA(0xFFFFFF, 20));
        } else gfx_fill(fb, inner_clip, x + 2, ty + 2, w - 4, TH - 4, RGBA(cat && h->theme == TH_ARCADE ? 0x0C1130 : S->tcap, cat ? 255 : S->tcap_a));
        if (it->kind == BI_FOLDER) { const hfolder *fo = &h->fold[it->idx]; name = fo->name; if (fo->nchild) snprintf(sub, sizeof sub, "%d game%s \xC2\xB7 %d folder%s", fo->ng, fo->ng == 1 ? "" : "s", fo->nchild, fo->nchild == 1 ? "" : "s"); else snprintf(sub, sizeof sub, "%d game%s", fo->ng, fo->ng == 1 ? "" : "s"); }
        else if (it->kind == BI_GAME) { const hgame *g = &h->all[it->idx]; name = g->title; snprintf(sub, sizeof sub, "%s%s%s", g->year ? g->year : "", g->year && g->genre ? " \xC2\xB7 " : "", g->genre ? g->genre : ""); if (!sub[0]) snprintf(sub, sizeof sub, " "); }
        else { const hcat *c = &h->cats[it->idx]; name = c->label; snprintf(sub, sizeof sub, "%s \xC2\xB7 %d game%s", c->kind ? c->kind : "", c->ng, c->ng == 1 ? "" : "s"); }
        if (cat) tile_caption(h, fb, inner_clip, S, x, ty + 2, w, TH - 4, 12, name, sub); else tile_caption(h, fb, inner_clip, S, x, capy, w, TH - 2 - 72 - 2, 6, name, sub);
        gfx_rrect_ring(fb, clip, x, ty, w, TH, S->tile_radius, 2, foc ? RGBA(S->focus, 255) : RGBA(S->tile_border, S->tile_border_a));
        LR(h, foc ? "browse-tile-focus" : "browse-tile", x, ty < top ? top : ty, x + w, ty + TH > top + inner ? top + inner : ty + TH);
    }
    #undef TILE_Y
    #undef TILE_X
}
/* hmEmpty: an empty folder or category, in the theme's games area */
static void render_empty(home *h, framebuffer *fb) {
    box4 b = { 140, 180, 1140, 400 }; const jval *g = h->geo;
    if (h->theme == TH_ARCADE) { const jval *card = json_get(g, "card"), *cells = json_get(g, "cells"); box4 fr, c4; if (card && !gbox(card, "frame", &fr) && !jbox_at(cells, 4, &c4)) b = (box4){ fr.x0, fr.y0 + 80, c4.x1, fr.y0 + 280 }; }
    else if (h->theme == TH_SHELF) { const jval *hero = json_get(g, "hero"); box4 x; if (hero && !gbox(hero, "inner", &x)) b = x; }
    else if (h->theme == TH_MAP) { const jval *p = json_get(g, "panel"); box4 x; if (p && !gbox(p, "frame", &x)) b = x; }
    const char *msg = h->nav.sec == SEC_CAT ? "No installed games match this category." : "This folder is empty. Add games using Classic Cart Studio for Windows.";
    int W = b.x1 - b.x0, H = b.y1 - b.y0; gfx_rrect(fb, FULL, b.x0, b.y0, W, H, 8, RGBA(0x08080C, 219)); gfx_rrect_ring(fb, FULL, b.x0, b.y0, W, H, 8, 2, RGBA(RED, 255));
    const font *f = &h->f[F_S400_20]; span ln[8]; size_t n = text_wrap(f, msg, W - 48, 0, ln, 8); double lh = 30, t0 = b.y0 + (H - n * lh) / 2;
    for (size_t i = 0; i < n; i++) { int lw = text_w(f, msg + ln[i].start, ln[i].len, 0); double x = b.x0 + (W - lw) / 2.0; int base = baseline_in(f, t0 + i * lh, lh);
        text_draw(fb, NULL, FULL, f, x, base + 2, msg + ln[i].start, ln[i].len, RGBA(0, 255), 0, 0); text_draw(fb, NULL, FULL, f, x, base, msg + ln[i].start, ln[i].len, RGBA(0xF4F2EC, 255), 0, 0); }
    LR(h, "empty", b.x0, b.y0, b.x1, b.y1);
}

/* ---------- overlays: notice, START menu, theme picker, Adventure Map destinations ---------- */
static void render_notice(home *h, framebuffer *fb) {
    gfx_fill(fb, FULL, 0, 0, fb->w, fb->h, RGBA(0, 140));
    const font *ft = &h->f[F_SILK11], *fx = &h->f[F_S400_17], *fh = &h->f[F_S400_15]; int W = 560, inner = W - 44; span ln[12];
    size_t n = text_wrap(fx, h->notice_text, inner, 0, ln, 12); double lh = 17 * 1.5;
    int H = iround(2 + 14 + lh_normal(ft) + 10 + 1 + 8 + 6 + n * lh + 14 + lh_normal(fh) + 14 + 2), x = (fb->w - W) / 2, y = (fb->h - H) / 2;
    gfx_shadow(fb, FULL, x, y + 20, W, H, 30, RGBA(0, 204)); gfx_rrect(fb, FULL, x, y, W, H, 10, RGBA(0x111115, 255)); gfx_rrect_ring(fb, FULL, x, y, W, H, 10, 2, RGBA(RED, 255));
    double yy = y + 2 + 14; T(fb, FULL, ft, x + 22, baseline_normal(ft, yy), h->notice_title, 0xFFB3B7, 255, 1); yy += lh_normal(ft) + 10;
    gfx_fill(fb, FULL, x + 2, iround(yy), W - 4, 1, RGBA(0xFFFFFF, 36)); yy += 1 + 8 + 6;
    for (size_t i = 0; i < n; i++) text_draw(fb, NULL, FULL, fx, x + 22, baseline_in(fx, yy + i * lh, lh), h->notice_text + ln[i].start, ln[i].len, RGBA(0xE2E0DA, 255), 0, 0);
    yy += n * lh + 14; T(fb, FULL, fh, x + 22, baseline_normal(fh, yy), "Press any button to return.", 0xCFCDC7, 255, 0);
    LR(h, "notice", x, y, x + W, y + H);
}
/* .menu (home.css): min-width 360, padding 14 0, h3 Press Start 11 px #ffb3b7 with a rule, items 20 px with 11/22 px padding;
 * Adventure Map: .menu.rpg (double border, Press Start 13 px items, a red triangle before the focused one) */
static void render_menu(home *h, framebuffer *fb, const char *title, const char **items, int n, int focus) {
    int rpg = h->ops->menu_rpg; gfx_fill(fb, FULL, 0, 0, fb->w, fb->h, RGBA(0, 140));
    const font *fh = rpg ? &h->f[F_PX10] : &h->f[F_PX11], *fi = rpg ? &h->f[F_PX13] : &h->f[F_S400_20], *fb2 = rpg ? &h->f[F_PX13] : &h->f[F_S700_20];
    int ih = rpg ? iround(10 + lh_normal(fi) + 10) : iround(11 + lh_normal(fi) + 11), W = 360;
    for (int i = 0; i < n; i++) { int w = text_w(fb2, items[i], strlen(items[i]), 0) + 44 + (rpg ? 30 : 0); if (w > W) W = w; } if (W > 560) W = 560;
    int H = 14 + iround(lh_normal(fh)) + 10 + 8 + n * ih + 14 + (rpg ? 8 : 4), x = (fb->w - W) / 2, y = (fb->h - H) / 2;
    gfx_shadow(fb, FULL, x, y + 20, W, H, 30, RGBA(0, 204));
    if (rpg) { gfx_rrect(fb, FULL, x - 2, y - 2, W + 4, H + 4, 4, RGBA(0, 255)); gfx_rrect(fb, FULL, x, y, W, H, 4, RGBA(0x0E0B0A, 255)); gfx_shadow(fb, FULL, x, y, W, H, 15, RGBA(RED, 90));
        gfx_rrect(fb, FULL, x, y, W, H, 4, RGBA(0x0E0B0A, 255)); gfx_rrect_ring(fb, FULL, x, y, W, H, 4, 1, RGBA(0xFFB3B7, 255)); gfx_rrect_ring(fb, FULL, x + 3, y + 3, W - 6, H - 6, 2, 1, RGBA(0xFFB3B7, 255)); }
    else { gfx_rrect(fb, FULL, x, y, W, H, 10, RGBA(0x111115, 255)); gfx_rrect_ring(fb, FULL, x, y, W, H, 10, 2, RGBA(RED, 255)); }
    int yy = y + 14 + (rpg ? 4 : 2); T(fb, FULL, fh, x + 22, baseline_normal(fh, yy), title, rpg ? 0xFFD166 : 0xFFB3B7, 255, 1); yy += iround(lh_normal(fh)) + 10;
    gfx_fill(fb, FULL, x + (rpg ? 4 : 2), yy, W - (rpg ? 8 : 4), 1, RGBA(0xFFFFFF, 36)); yy += 8;
    for (int i = 0; i < n; i++) {
        int foc = i == focus; const font *f = foc ? fb2 : fi;
        if (rpg) { if (foc) { const font *s = &h->f[F_SYM13]; T(fb, FULL, s, x + 22, baseline_center(s, yy, ih), "\xE2\x96\xB6", RED2, 255, 0); } T(fb, FULL, f, x + 22 + (foc ? 22 : 0), baseline_center(f, yy, ih), items[i], foc ? 0xFFD166 : 0xE9E7E1, 255, 0); }
        else { if (foc) gfx_fill(fb, FULL, x + 2, yy, W - 4, ih, RGBA(RED, 255)); if (foc) Tb(fb, FULL, fi, x + 22, baseline_center(fi, yy, ih), items[i], 0xFFFFFF, 255, 0); else T(fb, FULL, fi, x + 22, baseline_center(fi, yy, ih), items[i], 0xE9E7E1, 255, 0); }
        LR(h, foc ? "menu-focus" : "menu-item", x, yy, x + W, yy + ih); yy += ih;
    }
    LR(h, "menu", x, y, x + W, y + H);
}
/* the theme picker (hmOverlay 'theme'): four 200 px cards with the theme's thumbnail and name; the current one is marked */
static void render_theme_picker(home *h, framebuffer *fb) {
    gfx_fill(fb, FULL, 0, 0, fb->w, fb->h, RGBA(0, 140));
    const font *fh = &h->f[F_PX11], *fn = &h->f[F_S400_14], *fc = &h->f[F_S400_11]; const char *title = "CHANGE THEME \xC2\xB7 A applies at once";
    int cw = 200, gap = 18, th = 113, ch = 6 + th + 6 + iround(lh_normal(fn)) + iround(lh_normal(fc)) + 4 + 6, W = 22 + 4 * cw + 3 * gap + 22, H = 14 + iround(lh_normal(fh)) + 10 + 1 + 10 + ch + 14 + 14;
    int x = (fb->w - W) / 2, y = (fb->h - H) / 2;
    gfx_shadow(fb, FULL, x, y + 20, W, H, 30, RGBA(0, 204)); gfx_rrect(fb, FULL, x, y, W, H, 10, RGBA(0x111115, 255)); gfx_rrect_ring(fb, FULL, x, y, W, H, 10, 2, RGBA(RED, 255));
    int yy = y + 16; T(fb, FULL, fh, x + 22, baseline_normal(fh, yy), title, 0xFFB3B7, 255, 1); yy += iround(lh_normal(fh)) + 10; gfx_fill(fb, FULL, x + 2, yy, W - 4, 1, RGBA(0xFFFFFF, 36)); yy += 11;
    for (int t = 0; t < N_THEMES; t++) {
        int cx = x + 22 + t * (cw + gap), foc = t == h->nav.oindex, cur = t == h->theme;
        if (foc) gfx_shadow(fb, FULL, cx, yy, cw, ch, 11, RGBA(RED, 153));
        gfx_rrect(fb, FULL, cx, yy, cw, ch, 8, RGBA(0x1A1A20, 255)); gfx_rrect_ring(fb, FULL, cx, yy, cw, ch, 8, 3, foc ? RGBA(RED, 255) : RGBA(0x1A1A20, 255));
        if (h->thumbs[t].px) { image sc; if (!img_scale(&h->thumbs[t], &sc, cw - 12, th)) { gfx_rrect_image(fb, FULL, &sc, cx + 6, yy + 6, 4); img_free(&sc); } }
        const char *nm = THEME_NAMES[t]; int nw = text_w(fn, nm, strlen(nm), 0), ny = yy + 6 + th + 6;
        T(fb, FULL, fn, cx + (cw - nw) / 2.0, baseline_normal(fn, ny), nm, foc ? 0xFFFFFF : 0xE4E2DC, 255, 0);
        if (cur) { const char *c = "current"; int w = text_w(fc, c, 7, 0); T(fb, FULL, fc, cx + (cw - w) / 2.0, baseline_normal(fc, ny + lh_normal(fn) + 4), c, 0xFFB3B7, 255, 0); }
        LR(h, foc ? "theme-focus" : "theme-card", cx, yy, cx + cw, yy + ch);
    }
    LR(h, "theme-picker", x, y, x + W, y + H);
}
static int start_count(void) { return 9; }

/* ---------- theme switching (on the console, offline: everything is in the package) ---------- */
int home_set_theme(home *h, const char *id, int save) {
    int t = theme_index(id); if (t < 0) return -1; if (t == h->theme && h->ops) { if (save) prefs_save(h, THEME_IDS[t], h->saved_epoch); return 0; }
    int prev = h->theme; char err[256]; details_close(h);
    if (theme_activate(h, t, err, sizeof err)) { emit_json(h, "{\"ev\":\"theme_failed\",\"theme\":\"%s\",\"error\":\"%s\"}", id, err); char e2[256]; theme_activate(h, prev, e2, sizeof e2); return -1; }
    /* the section and the selected game stay; focus returns to the section (a theme's navigation has a different layout) */
    h->nav.screen = SCR_GRID; h->nav.zone = home_section_zone(h); h->nav.nav = home_active_nav(h); h->nav.landmark = 0; h->nav.overlay = OV_NONE; h->gen++;
    for (int i = 0; i < h->nfold; i++) img_free(&h->fold[i].tile);   /* tile sizes differ per theme */
    if (save) prefs_save(h, THEME_IDS[t], h->saved_epoch);
    char gid[160]; jesc(h->n ? h->g[h->nav.sel].id : "", gid, sizeof gid);
    emit_json(h, "{\"ev\":\"theme_changed\",\"theme\":\"%s\",\"from\":\"%s\",\"id\":%s,\"saved\":%s}", THEME_IDS[t], THEME_IDS[prev], gid, save ? "true" : "false");
    return 0;
}

/* ---------- input ---------- */
void home_input(home *h, button b, in_kind k, uint64_t clock_ms) {
    if (!h->nall) return;
    int dir = dir_of(b);
    if (k == IN_RELEASE || k == IN_FORCED_RELEASE) { if ((int)b == h->nav.swallow) h->nav.swallow = -1; if (dir >= 0 && (int)b == h->nav.repeat_button) h->nav.repeat_button = -1; return; }
    if (k == IN_REPEAT) { if (dir < 0 || (int)b != h->nav.repeat_button || h->nav.notice) return; }
    else if (k == IN_PRESS) { if (dir >= 0) h->nav.repeat_button = (int)b; }
    else return;
    h->changed_at = clock_ms; h->gen++;
    if (h->nav.notice) { h->nav.notice = 0; h->nav.swallow = (int)b; h->nav.repeat_button = -1; home_emit_state(h); return; }
    if ((int)b == h->nav.swallow) return;
    navstate *N = &h->nav;
    if (N->overlay == OV_START) {
        int n = start_count();
        if (b == B_B || b == B_START) N->overlay = OV_NONE;
        else if (b == B_UP) { if (N->oindex > 0) N->oindex--; } else if (b == B_DOWN) { if (N->oindex < n - 1) N->oindex++; }
        else if (b == B_A) { const char *id = START_ID[N->oindex];
            if (!strcmp(id, "return")) N->overlay = OV_NONE;
            else if (!strcmp(id, "theme")) { N->overlay = OV_THEME; N->oindex = h->theme; }
            else if (!strcmp(id, "allgames")) { N->overlay = OV_NONE; home_go_all(h); }
            else if (!strcmp(id, "browse")) { N->overlay = OV_NONE; home_go_browse(h, -1); }
            else { N->overlay = OV_NONE; not_yet(h, START_LABEL[N->oindex]); } }
        home_emit_state(h); return;
    }
    if (N->overlay == OV_THEME) {
        if (b == B_B || b == B_START) N->overlay = OV_NONE;
        else if (b == B_LEFT || b == B_UP) { if (N->oindex > 0) N->oindex--; } else if (b == B_RIGHT || b == B_DOWN) { if (N->oindex < N_THEMES - 1) N->oindex++; }
        else if (b == B_A) { N->overlay = OV_NONE; home_set_theme(h, THEME_IDS[N->oindex], 1); }
        home_emit_state(h); return;
    }
    if (N->overlay == OV_MAPDEST) {
        if (b == B_B || b == B_SELECT) N->overlay = OV_NONE;
        else if (b == B_START) { N->overlay = OV_START; N->oindex = 0; }
        else if (b == B_UP) { if (N->oindex > 0) N->oindex--; } else if (b == B_DOWN) { if (N->oindex < 5) N->oindex++; }
        else if (b == B_A) { N->overlay = OV_NONE; if (N->oindex == 0) { home_go_all(h); N->landmark = 0; } else if (N->oindex == 1) { home_go_browse(h, -1); N->landmark = 1; } else not_yet(h, DEST_LABEL[N->oindex]); }
        home_emit_state(h); return;
    }
    if (N->screen == SCR_DETAILS) {
        int max = desc_max(h), *f = &N->dfocus;
        if (b == B_B) { details_close(h); N->screen = SCR_GRID; N->zone = N->dzone == Z_BROWSE && N->sec == SEC_BROWSE ? Z_BROWSE : home_section_zone(h); }
        else if (b == B_START) { N->overlay = OV_START; N->oindex = 0; }
        else if (b == B_LEFT) { if (*f == DF_FAV) *f = DF_PLAY; }
        else if (b == B_RIGHT) { if (*f == DF_PLAY) *f = DF_FAV; }
        else if (b == B_DOWN) { if (*f == DF_DESC) { if (N->dscroll < max) N->dscroll++; } else if (max > 0) { N->dbutton = *f; *f = DF_DESC; } }
        else if (b == B_UP) { if (*f == DF_DESC) { if (N->dscroll > 0) N->dscroll--; else *f = N->dbutton; } }
        else if (b == B_A) { if (*f == DF_PLAY) launch_unavailable(h, h->d.game); else if (*f == DF_FAV) not_yet(h, "Favorites"); }
        home_emit_state(h); return;
    }
    if (b == B_START) { N->overlay = OV_START; N->oindex = 0; home_emit_state(h); return; }
    if (N->zone == Z_BROWSE) { browse_input(h, b); home_emit_state(h); return; }
    if (N->zone == Z_GAMES && N->sec != SEC_ALL && b == B_B) { home_back(h); home_emit_state(h); return; }
    if (N->zone == Z_GAMES && !h->n) {   /* an empty folder or category: only the way out (the theme's navigation) */
        if (b == B_UP || (b == B_LEFT && h->theme == TH_SHELF)) { N->zone = h->theme == TH_MAP ? Z_MAP : Z_NAV; N->nav = home_active_nav(h); }
        home_emit_state(h); return;
    }
    h->ops->input(h, b);
    home_emit_state(h);
}
/* SELECT on a game: Game Details (shared by the themes) */
int home_open_details_sel(home *h) {
    if (!h->n) return -1;
    if (open_details(h, h->nav.sel)) { not_yet(h, "Details for this game"); return -1; }
    h->nav.screen = SCR_DETAILS; h->nav.dfocus = DF_PLAY; h->nav.dbutton = DF_PLAY; h->nav.dscroll = 0; h->nav.dzone = Z_GAMES; return 0;
}
void home_open_mapdest(home *h) { h->nav.overlay = OV_MAPDEST; h->nav.oindex = 0; }

/* ---------- render ---------- */
static void render_overlays(home *h, framebuffer *fb) {
    if (h->nav.overlay == OV_START) render_menu(h, fb, "MENU", START_LABEL, start_count(), h->nav.oindex);
    else if (h->nav.overlay == OV_THEME) render_theme_picker(h, fb);
    else if (h->nav.overlay == OV_MAPDEST) render_menu(h, fb, "WHERE TO?", DEST_LABEL, 6, h->nav.oindex);
    if (h->nav.notice) render_notice(h, fb);
}
void home_render(home *h, framebuffer *fb, uint64_t now) {
    h->decoded = 0; h->pending = 0; h->nrec = 0;
    if (h->nav.screen == SCR_DETAILS && h->d.open) { render_details(h, fb); details_legend(h, fb); render_overlays(h, fb); return; }
    if (h->nav.sec == SEC_BROWSE) { size_t keep = h->n; h->n = 0; h->ops->render(h, fb, now); h->n = keep; render_browse(h, fb); }   /* the theme's chrome, then the Browse panel */
    else { h->ops->render(h, fb, now); if (!h->n) render_empty(h, fb); }
    render_overlays(h, fb);
}
void home_select(home *h, int index) {
    if (!h->n) return; if (index < 0) index = 0; if (index >= (int)h->n) index = (int)h->n - 1;
    if (h->nav.sec == SEC_BROWSE) home_go_all(h);
    details_close(h); h->nav.screen = SCR_GRID; h->nav.zone = Z_GAMES; h->gen++; h->nav.notice = 0; h->nav.overlay = OV_NONE; h->nav.sel = index; home_emit_state(h);
}
void home_layout(home *h, framebuffer *fb, uint64_t now) {
    h->rec = 1; home_render(h, fb, now); h->rec = 0; char buf[200];
    for (int i = 0; i < h->nrec; i++) { layrec *r = &h->recs[i]; snprintf(buf, sizeof buf, "{\"ev\":\"layout_rect\",\"name\":\"%s\",\"box\":[%d,%d,%d,%d]}", r->name, r->b.x0, r->b.y0, r->b.x1, r->b.y1); if (h->emit) h->emit(buf); }
    emit_json(h, "{\"ev\":\"layout_end\",\"rects\":%d,\"screen\":\"%s\",\"theme\":\"%s\"}", h->nrec, h->nav.screen == SCR_DETAILS ? "details" : "grid", THEME_IDS[h->theme]);
}
