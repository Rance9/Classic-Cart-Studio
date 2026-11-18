/* CCS HOME Designer: loading, checking and resolving a compiled design on the console (design.h; HOME-DESIGNER.md).
 * Everything here reads only; nothing is ever written. Integer arithmetic only (frames stay bit-identical on every platform). */
#include "home_int.h"
#include "design.h"
#include <errno.h>

int stbi_info_from_memory(unsigned char const *buffer, int len, int *x, int *y, int *comp);

/* ---------- SHA-256 (FIPS 180-4), small and portable ---------- */
typedef struct { uint32_t h[8]; uint64_t n; uint8_t b[64]; size_t k; } sha_ctx;
static const uint32_t K256[64] = {
    0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
    0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
    0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967, 0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
    0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
    0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
    0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2 };
#define ROR(x, n) (((x) >> (n)) | ((x) << (32 - (n))))
static void sha_block(sha_ctx *c, const uint8_t *p) {
    uint32_t w[64], a, b, d, e, f, g, hh, cc, t1, t2;
    for (int i = 0; i < 16; i++) w[i] = (uint32_t)p[i * 4] << 24 | (uint32_t)p[i * 4 + 1] << 16 | (uint32_t)p[i * 4 + 2] << 8 | p[i * 4 + 3];
    for (int i = 16; i < 64; i++) { uint32_t s0 = ROR(w[i - 15], 7) ^ ROR(w[i - 15], 18) ^ (w[i - 15] >> 3), s1 = ROR(w[i - 2], 17) ^ ROR(w[i - 2], 19) ^ (w[i - 2] >> 10); w[i] = w[i - 16] + s0 + w[i - 7] + s1; }
    a = c->h[0]; b = c->h[1]; cc = c->h[2]; d = c->h[3]; e = c->h[4]; f = c->h[5]; g = c->h[6]; hh = c->h[7];
    for (int i = 0; i < 64; i++) {
        t1 = hh + (ROR(e, 6) ^ ROR(e, 11) ^ ROR(e, 25)) + ((e & f) ^ (~e & g)) + K256[i] + w[i];
        t2 = (ROR(a, 2) ^ ROR(a, 13) ^ ROR(a, 22)) + ((a & b) ^ (a & cc) ^ (b & cc));
        hh = g; g = f; f = e; e = d + t1; d = cc; cc = b; b = a; a = t1 + t2;
    }
    c->h[0] += a; c->h[1] += b; c->h[2] += cc; c->h[3] += d; c->h[4] += e; c->h[5] += f; c->h[6] += g; c->h[7] += hh;
}
static void sha_init(sha_ctx *c) { static const uint32_t H0[8] = { 0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19 }; memcpy(c->h, H0, sizeof H0); c->n = 0; c->k = 0; }
static void sha_add(sha_ctx *c, const uint8_t *p, size_t n) {
    c->n += n;
    while (n) { size_t t = 64 - c->k; if (t > n) t = n; memcpy(c->b + c->k, p, t); c->k += t; p += t; n -= t; if (c->k == 64) { sha_block(c, c->b); c->k = 0; } }
}
static void sha_end(sha_ctx *c, uint8_t out[32]) {
    uint64_t bits = c->n * 8; uint8_t pad = 0x80; sha_add(c, &pad, 1); uint8_t z = 0; while (c->k != 56) sha_add(c, &z, 1);
    uint8_t L[8]; for (int i = 0; i < 8; i++) L[i] = (uint8_t)(bits >> (56 - 8 * i)); sha_add(c, L, 8);
    for (int i = 0; i < 8; i++) { out[i * 4] = (uint8_t)(c->h[i] >> 24); out[i * 4 + 1] = (uint8_t)(c->h[i] >> 16); out[i * 4 + 2] = (uint8_t)(c->h[i] >> 8); out[i * 4 + 3] = (uint8_t)c->h[i]; }
}
void ccs_sha256(const void *data, size_t len, uint8_t out[32]) { sha_ctx c; sha_init(&c); sha_add(&c, (const uint8_t *)data, len); sha_end(&c, out); }
void ccs_sha256_hex(const void *data, size_t len, char out[65]) { uint8_t d[32]; ccs_sha256(data, len, d); for (int i = 0; i < 32; i++) snprintf(out + i * 2, 3, "%02x", d[i]); out[64] = 0; }

/* ---------- small checks ---------- */
static int is_hex64(const char *s) { if (!s || strlen(s) != 64) return 0; for (int i = 0; i < 64; i++) if (!((s[i] >= '0' && s[i] <= '9') || (s[i] >= 'a' && s[i] <= 'f'))) return 0; return 1; }
uint32_t design_color(const char *s, int *ok) {
    *ok = 0; if (!s || strlen(s) != 7 || s[0] != '#') return 0; uint32_t v = 0;
    for (int i = 1; i < 7; i++) { char c = s[i]; int d = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : -1; if (d < 0) return 0; v = v << 4 | (uint32_t)d; }
    *ok = 1; return v;
}
static const char *jstr(const jval *o, const char *k) { const jval *v = o ? json_get(o, k) : NULL; return v && v->t == J_STR ? v->s : NULL; }
static int jint(const jval *o, const char *k, long lo, long hi, long *out) {   /* a whole number in range */
    const jval *v = o ? json_get(o, k) : NULL; if (!v || v->t != J_NUM) return -1; double d = v->num; if (d < (double)lo || d > (double)hi) return -1;
    long l = (long)d; if ((double)l != d) return -1; *out = l; return 0; }
static int font_index(const char *name) {
    static const char *N[F_COUNT] = {
#define FN(id, nm) nm,
        FONT_LIST(FN)
#undef FN
    };
    if (!name) return -1; if (!strncmp(name, "sym", 3)) return -1;   /* the symbol faces draw the HOME's own marks only */
    for (int i = 0; i < F_COUNT; i++) if (!strcmp(N[i], name)) return i; return -1;
}
static const char *THEMES4[4] = { "arcade-grid", "box-art-showcase", "featured-shelf", "adventure-map" };
const char *const DESIGN_UI[UI_COUNT] = { "games", "details", "title", "status", "hints" };
static const char *BUILTINS[5] = { "forgotten-keep", "galactic-frontier", "kingdoms-beyond", "neon-highway", "pixel-worlds" };
static int file_of(const hdesign *d, const char *name) { if (!name) return -1; for (int i = 0; i < d->nf; i++) if (!strcmp(d->f[i].name, name)) return i; return -2; }
static int name_ok(const char *s, size_t max) { if (!s) return 0; size_t n = strlen(s); if (!n || n > max) return 0; for (size_t i = 0; i < n; i++) if ((unsigned char)s[i] < 0x20 || s[i] == 0x7f) return 0; return 1; }

int design_path(const hdesign *d, int file, char *out, size_t n) { if (!d || file < 0 || file >= d->nf) return -1; snprintf(out, n, "%s/%s", d->dir, d->f[file].name); return 0; }

void design_free(hdesign *d) {
    if (!d) return;
    for (int i = 0; i < d->nlayer; i++) free(d->layer[i].text); free(d->layer);
    for (int i = 0; i < d->nfold; i++) { free(d->fold[i].id); free(d->fold[i].name); for (int k = 0; k < d->fold[i].ng; k++) free(d->fold[i].games[k]); free(d->fold[i].games); }
    free(d->fold); for (int i = 0; i < d->norder; i++) free(d->order[i]); free(d->order); free(d);
}

/* every listed picture: there, its size, its SHA-256, its kind from its first bytes and its header size (no pixel decoded here) */
static int check_files(hdesign *d, char *why, size_t n) {
    uint64_t total = 0;
    for (int i = 0; i < d->nf; i++) {
        dfile *f = &d->f[i]; char p[1100]; uint8_t *b; size_t len; snprintf(p, sizeof p, "%s/%s", d->dir, f->name);
        int rc = ccs_read_file(p, &b, &len, DESIGN_MAX_FILE_BYTES + 1);
        if (rc) { snprintf(why, n, "picture %s is missing or unreadable", f->name); return -1; }
        if (len != f->bytes) { free(b); snprintf(why, n, "picture %s is not its listed size", f->name); return -1; }
        char hex[65]; ccs_sha256_hex(b, len, hex);
        if (strcmp(hex, f->sha)) { free(b); snprintf(why, n, "picture %s is not the picture its fingerprint names (changed or damaged)", f->name); return -1; }
        int png = len >= 8 && !memcmp(b, "\x89PNG\r\n\x1a\n", 8), jpg = len >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF, w = 0, h = 0, c = 0;
        if ((f->png && !png) || (!f->png && !jpg)) { free(b); snprintf(why, n, "picture %s is not a %s", f->name, f->png ? "PNG" : "JPEG"); return -1; }
        if (!stbi_info_from_memory(b, (int)len, &w, &h, &c) || w != f->w || h != f->h) { free(b); snprintf(why, n, "picture %s is not the size its listing says", f->name); return -1; }
        free(b); total += len;
    }
    if (total > DESIGN_MAX_TOTAL_BYTES) { snprintf(why, n, "the pictures are larger than %u bytes together", DESIGN_MAX_TOTAL_BYTES); return -1; }
    d->file_bytes = total; return 0;
}

static int slot_rule(const hdesign *d, int fi, int exact, int mw, int mh, int png_only, int jpeg_only, uint32_t max_bytes) {
    if (fi < 0 || fi >= d->nf) return 0; const dfile *f = &d->f[fi];
    if (exact ? (f->w != mw || f->h != mh) : (f->w > mw || f->h > mh)) return 0;
    if ((png_only && !f->png) || (jpeg_only && f->png) || f->bytes > max_bytes) return 0; return 1;
}

int design_load(const char *dir, const char *expect_id, hdesign **out, char *why, size_t n) {
    *out = NULL; why[0] = 0; char p[1100]; uint8_t *buf; size_t len; char jerr[160];
    if (strlen(dir) >= 880) { snprintf(why, n, "the design folder name is too long"); return -1; }
    snprintf(p, sizeof p, "%s/design.json", dir);
    if (ccs_read_file(p, &buf, &len, DESIGN_MAX_JSON_BYTES)) { snprintf(why, n, "design.json is missing or too large"); return -1; }
    char id[65]; ccs_sha256_hex(buf, len, id);
    if (expect_id && strcmp(expect_id, id)) { free(buf); snprintf(why, n, "design.json is not the one its folder names (changed or damaged)"); return -1; }
    jval *j = json_parse((const char *)buf, len, jerr, sizeof jerr); free(buf);
    hdesign *d = calloc(1, sizeof *d); if (!d) { json_free(j); snprintf(why, n, "out of memory"); return -1; }
    snprintf(d->dir, sizeof d->dir, "%s", dir); snprintf(d->id, sizeof d->id, "%s", id); for (int i = 0; i < DS_COUNT; i++) d->slot[i] = -1;
    d->title_font = d->text_font = -1; d->reduced_motion = d->music = -1; d->map_folder[0] = d->map_folder[1] = -1; d->startup_file = -1;
    #define BAD(...) do { snprintf(why, n, __VA_ARGS__); goto fail; } while (0)
    if (!j || j->t != J_OBJ) BAD("design.json is not valid JSON (%s)", jerr);
    int v2 = 0; { const char *s = jstr(j, "schema"); if (s && !strcmp(s, DESIGN_SCHEMA2)) v2 = 1; else if (!s || strcmp(s, DESIGN_SCHEMA)) BAD("design.json is not %s", DESIGN_SCHEMA); }
    { const char *s = jstr(j, "profile"); if (!s || strcmp(s, DESIGN_PROFILE)) BAD("the design was made for another screen profile"); }
    { const char *t = jstr(j, "theme"); d->theme = -1; for (int i = 0; i < 4; i++) if (t && !strcmp(t, THEMES4[i])) d->theme = i; if (d->theme < 0) BAD("the design's theme is not one of the four"); }
    { const jval *s = json_get(j, "source"); const char *nm = jstr(s, "name"); snprintf(d->name, sizeof d->name, "%s", nm && name_ok(nm, 190) ? nm : "HOME design"); }
    /* pictures */
    const jval *fs = json_get(j, "files");
    if (!fs || fs->t != J_ARR || fs->n > DESIGN_MAX_FILES) BAD("the picture list is missing or too long");
    for (size_t i = 0; i < fs->n; i++) {
        const jval *o = fs->items[i]; dfile *f = &d->f[d->nf]; const char *nm = jstr(o, "name"), *sha = jstr(o, "sha256"), *kind = jstr(o, "kind"); long b, w, h;
        if (!nm || !sha || !kind || !is_hex64(sha) || jint(o, "bytes", 1, DESIGN_MAX_FILE_BYTES, &b) || jint(o, "w", 1, CCS_FB_W, &w) || jint(o, "h", 1, CCS_FB_H, &h)) BAD("picture %zu is not described correctly", i + 1);
        int png = !strcmp(kind, "png"); if (!png && strcmp(kind, "jpeg")) BAD("picture %zu is neither PNG nor JPEG", i + 1);
        char want[80]; snprintf(want, sizeof want, "img/%s.%s", sha, png ? "png" : "jpg"); if (strcmp(nm, want)) BAD("picture %zu is not named by its fingerprint", i + 1);
        for (int k = 0; k < d->nf; k++) if (!strcmp(d->f[k].sha, sha)) BAD("picture %zu is listed twice", i + 1);
        snprintf(f->sha, sizeof f->sha, "%s", sha); snprintf(f->name, sizeof f->name, "%s", nm); f->png = png; f->w = (int)w; f->h = (int)h; f->bytes = (uint32_t)b; d->nf++;
    }
    /* slots: each its own rule (COMPOSER-FORMATS.md picture slots) */
    const jval *sl = json_get(j, "slots");
    if (sl && sl->t == J_OBJ) for (size_t i = 0; i < sl->n; i++) {
        static const char *SN[DS_COUNT] = { "scene", "details-backdrop", "plate", "icon-games", "icon-folder", "icon-star", "icon-clock", "icon-gear" };
        int s = -1; for (int k = 0; k < DS_COUNT; k++) if (!strcmp(sl->keys[i], SN[k])) s = k; if (s < 0) BAD("the design uses a picture place this HOME does not have");
        int fi = sl->items[i]->t == J_STR ? file_of(d, sl->items[i]->s) : -2; if (fi < 0) BAD("a picture place names a picture the design does not carry");
        int ok = s == DS_SCENE ? slot_rule(d, fi, 1, 1280, 720, 0, 0, DESIGN_MAX_FILE_BYTES) : s == DS_BACKDROP ? slot_rule(d, fi, 1, 1272, 712, 0, 0, DESIGN_MAX_FILE_BYTES)
               : s == DS_PLATE ? slot_rule(d, fi, 0, 1256, 696, 1, 0, DESIGN_MAX_FILE_BYTES) : slot_rule(d, fi, 0, 128, 128, 1, 0, 200000);
        if (!ok) BAD("the picture for %s is not within its place's rules", SN[s]);
        d->slot[s] = fi;
    }
    /* appearance */
    const jval *ap = json_get(j, "appearance");
    if (ap && ap->t == J_OBJ) {
        int ok; const char *c;
        if ((c = jstr(ap, "accent"))) { d->accent = design_color(c, &ok); if (!ok) BAD("the focus color is not a color"); d->has_accent = 1; }
        if ((c = jstr(ap, "panel"))) { d->panel = design_color(c, &ok); if (!ok) BAD("the panel color is not a color"); d->has_panel = 1; long a; if (jint(ap, "panel_opacity", 0, 100, &a)) a = 100; d->panel_a = (int)(a * 255 / 100); }
        if ((c = jstr(ap, "text"))) { d->text = design_color(c, &ok); if (!ok) BAD("the text color is not a color"); d->has_text = 1; }
        if ((c = jstr(ap, "title_font")) && (d->title_font = font_index(c)) < 0) BAD("the title lettering is not one of the HOME's fonts");
        if ((c = jstr(ap, "text_font")) && (d->text_font = font_index(c)) < 0) BAD("the text lettering is not one of the HOME's fonts");
    }
    /* decorations, in drawing order (the compiler sorted them by layer) */
    const jval *ls = json_get(j, "layers");
    if (ls && ls->t == J_ARR) {
        if (ls->n > DESIGN_MAX_LAYERS) BAD("the design has more than %d decorations", DESIGN_MAX_LAYERS);
        d->layer = calloc(ls->n ? ls->n : 1, sizeof *d->layer); if (!d->layer) BAD("out of memory");
        for (size_t i = 0; i < ls->n; i++) {
            const jval *o = ls->items[i]; dlayer *L = &d->layer[d->nlayer]; const char *k = jstr(o, "kind"); long x, y, w, h, op;
            if (!k || jint(o, "x", 12, 1268, &x) || jint(o, "y", 12, 708, &y) || jint(o, "w", 1, 1256, &w) || jint(o, "h", 1, 696, &h) || x + w > CCS_FB_W - 12 || y + h > CCS_FB_H - 12) BAD("decoration %zu is not inside the screen's safe area", i + 1);
            if (jint(o, "opacity", 0, 100, &op)) op = 100;
            L->x = (int)x; L->y = (int)y; L->w = (int)w; L->h = (int)h; L->opacity = (int)op; L->file = -1; L->font = -1;
            int ok; const char *c = jstr(o, "color"); if (c) { L->color = design_color(c, &ok); if (!ok) BAD("decoration %zu has a color that is not a color", i + 1); L->has_color = 1; }
            if (!strcmp(k, "image")) { L->kind = DL_IMAGE; L->file = file_of(d, jstr(o, "image")); if (L->file < 0 || d->f[L->file].w > 1256 || d->f[L->file].h > 696) BAD("decoration %zu names a picture the design does not carry", i + 1); }
            else if (!strcmp(k, "panel")) { L->kind = DL_PANEL; if (!L->has_color) BAD("panel %zu has no color", i + 1); }
            else if (!strcmp(k, "label")) {
                L->kind = DL_LABEL; const char *t = jstr(o, "text"), *fn = jstr(o, "font"), *al = jstr(o, "align");
                if (!name_ok(t, 240)) BAD("label %zu has no usable text", i + 1);
                if ((L->font = font_index(fn)) < 0) BAD("label %zu uses lettering the HOME does not have", i + 1);
                L->align = al && !strcmp(al, "center") ? 1 : al && !strcmp(al, "right") ? 2 : 0; L->text = sdup(t); if (!L->text) BAD("out of memory");
            } else BAD("decoration %zu is of a kind this HOME does not draw", i + 1);
            d->nlayer++;
        }
    }
    /* control regions (schema /2 only): each group at most once, whole pixels, inside the screen's safe area; whether the base theme has
       the group, its size and the places of the others are checked against the package's own layout when the HOME loads (design_layout) */
    const jval *rg = json_get(j, "regions");
    if (rg && !v2) BAD("design.json moves the HOME's controls but is not %s", DESIGN_SCHEMA2);
    if (v2 && (!rg || rg->t != J_ARR || !rg->n || rg->n > UI_COUNT)) BAD("the design's control regions are missing or too many");
    for (size_t i = 0; rg && i < rg->n; i++) {
        const jval *o = rg->items[i]; const char *u = jstr(o, "ui"); int ui = -1; long x, y, w, hh;
        for (int k = 0; k < UI_COUNT; k++) if (u && !strcmp(u, DESIGN_UI[k])) ui = k;
        if (ui < 0 || d->reg[ui].set) BAD("control region %zu is not one of the HOME's groups, or is listed twice", i + 1);
        if (jint(o, "x", 12, 1268, &x) || jint(o, "y", 12, 708, &y) || jint(o, "w", 1, 1256, &w) || jint(o, "h", 1, 696, &hh) || x + w > CCS_FB_W - 12 || y + hh > CCS_FB_H - 12) BAD("control region %s is not inside the screen's safe area", DESIGN_UI[ui]);
        d->reg[ui] = (dregion){ 1, (int)x, (int)y, (int)w, (int)hh }; d->nreg++;
    }
    /* folders (null: the collection's own stay), in the order the compiler wrote them (each parent before its folders) */
    const jval *fo = json_get(j, "folders");
    if (fo && fo->t == J_ARR) {
        if (fo->n > DESIGN_MAX_FOLDERS) BAD("the design has more than %d folders", DESIGN_MAX_FOLDERS);
        d->has_folders = 1; d->fold = calloc(fo->n ? fo->n : 1, sizeof *d->fold); if (!d->fold) BAD("out of memory");
        for (size_t i = 0; i < fo->n; i++) {
            const jval *o = fo->items[i]; dfolder *F = &d->fold[d->nfold]; long par; const char *id = jstr(o, "id"), *nm = jstr(o, "name"), *art = jstr(o, "art");
            if (!name_ok(id, 40) || !name_ok(nm, 160) || jint(o, "parent", -1, (long)i - 1, &par)) BAD("folder %zu is not described correctly", i + 1);
            F->id = sdup(id); F->name = sdup(nm); F->parent = (int)par; F->art = -1; d->nfold++;
            if (art) { F->art = file_of(d, art); if (F->art < 0 || d->f[F->art].w > 544 || d->f[F->art].h > 148) BAD("folder %zu names a picture the design does not carry", i + 1); }
            const jval *g = json_get(o, "games"); if (g && g->t != J_ARR) BAD("folder %zu has no game list", i + 1);
            if (g && g->n > DESIGN_MAX_FOLDER_GAMES) BAD("folder %zu has more than %d games", i + 1, DESIGN_MAX_FOLDER_GAMES);
            if (g && g->n) { F->games = calloc(g->n, sizeof *F->games); if (!F->games) BAD("out of memory");
                for (size_t k = 0; k < g->n; k++) { if (g->items[k]->t != J_STR || !name_ok(g->items[k]->s, 64)) BAD("folder %zu names a game wrongly", i + 1); F->games[F->ng++] = sdup(g->items[k]->s); } }
        }
        const jval *ms = json_get(j, "map_shortcuts");
        for (int k = 0; k < 2; k++) { const jval *v = ms && ms->t == J_ARR && (size_t)k < ms->n ? ms->items[k] : NULL; d->map_folder[k] = v && v->t == J_NUM && v->num >= 0 && v->num < d->nfold ? (int)v->num : -1; }
    }
    const jval *od = json_get(j, "order");
    if (od && od->t == J_ARR) {
        if (od->n > DESIGN_MAX_ORDER) BAD("the game order is longer than %d", DESIGN_MAX_ORDER);
        d->order = calloc(od->n ? od->n : 1, sizeof *d->order); if (!d->order) BAD("out of memory");
        for (size_t k = 0; k < od->n; k++) { if (od->items[k]->t != J_STR || !name_ok(od->items[k]->s, 64)) BAD("the game order names a game wrongly"); d->order[d->norder++] = sdup(od->items[k]->s); }
    }
    const jval *st = json_get(j, "settings");
    if (st && st->t == J_OBJ) { const jval *v; if ((v = json_get(st, "reduced_motion")) && v->t == J_BOOL) d->reduced_motion = v->b; if ((v = json_get(st, "music")) && v->t == J_BOOL) d->music = v->b; }
    const jval *su = json_get(j, "startup");
    if (su && su->t == J_OBJ) {
        const char *m = jstr(su, "mode");
        if (m && !strcmp(m, "random")) d->startup_mode = DSU_RANDOM;
        else if (m && !strcmp(m, "builtin")) { const char *b = jstr(su, "builtin"); for (int k = 0; k < 5; k++) if (b && !strcmp(b, BUILTINS[k])) { d->startup_mode = DSU_BUILTIN; snprintf(d->startup_builtin, sizeof d->startup_builtin, "%s", b); } if (d->startup_mode != DSU_BUILTIN) BAD("the startup screen is not one of the five"); }
        else if (m && !strcmp(m, "image")) { d->startup_file = file_of(d, jstr(su, "image")); if (d->startup_file < 0 || !slot_rule(d, d->startup_file, 1, 1280, 720, 0, 1, DESIGN_MAX_STARTUP_BYTES)) BAD("the startup picture is not a 1280x720 JPEG of at most %u bytes", DESIGN_MAX_STARTUP_BYTES); d->startup_mode = DSU_IMAGE; }
        else if (m) BAD("the startup choice is not known");
    }
    /* the decoded-picture budget: every distinct picture the HOME draws (startup apart), width x height x 4 */
    { uint8_t used[DESIGN_MAX_FILES] = { 0 };
      for (int s = 0; s < DS_COUNT; s++) if (d->slot[s] >= 0) used[d->slot[s]] = 1;
      for (int i = 0; i < d->nlayer; i++) if (d->layer[i].file >= 0) used[d->layer[i].file] = 1;
      for (int i = 0; i < d->nfold; i++) if (d->fold[i].art >= 0) used[d->fold[i].art] = 1;
      for (int i = 0; i < d->nf; i++) if (used[i]) d->decoded_bytes += (uint64_t)d->f[i].w * (uint64_t)d->f[i].h * 4u;
      if (d->decoded_bytes > DESIGN_DECODED_BUDGET) BAD("the pictures need %llu bytes of decoded memory (at most %u)", (unsigned long long)d->decoded_bytes, DESIGN_DECODED_BUDGET); }
    if (check_files(d, why, n)) goto fail;
    #undef BAD
    json_free(j); *out = d; return 0;
fail:
    json_free(j); design_free(d); return -1;
}

static int read_pointer(const char *path, char *out, size_t n) {   /* "none" or a design id; anything else: -1 */
    uint8_t *b; size_t len; out[0] = 0; if (ccs_read_file(path, &b, &len, 200)) return -2;
    size_t k = 0; while (k < len && k < n - 1 && b[k] != '\n' && b[k] != '\r' && b[k] != ' ') { out[k] = (char)b[k]; k++; } out[k] = 0; free(b);
    return !strcmp(out, "none") || is_hex64(out) ? 0 : -1;
}

int design_resolve(const char *root, hdesign **out, char *src, size_t sn, char *why, size_t n) { return design_resolve2(root, NULL, NULL, out, src, sn, why, n); }
int design_resolve2(const char *root, design_check_fn check, void *ctx, hdesign **out, char *src, size_t sn, char *why, size_t n) {
    *out = NULL; why[0] = 0; snprintf(src, sn, "none"); if (!root || !*root) return 0;
    static const char *STEP[2] = { "current", "known-good" }; size_t w = 0;
    for (int s = 0; s < 2; s++) {
        char p[1100], id[80], e[400] = ""; snprintf(p, sizeof p, "%s/%s", root, STEP[s]); int rc = read_pointer(p, id, sizeof id);
        if (rc == -2) { if (s == 0) { snprintf(src, sn, "none"); return 0; } continue; }   /* no current: no design was ever applied */
        if (rc) { w += (size_t)snprintf(why + w, w < n ? n - w : 0, "%s%s is not readable", w ? "; " : "", STEP[s]); continue; }
        if (!strcmp(id, "none")) { if (s == 0) { snprintf(src, sn, "chosen-defaults"); return 0; } continue; }
        char dir[1100]; snprintf(dir, sizeof dir, "%s/d/%s", root, id); hdesign *d;
        if (!design_load(dir, id, &d, e, sizeof e)) {
            if (check && check(ctx, d, e, sizeof e)) design_free(d);
            else { snprintf(d->src, sizeof d->src, "%s", STEP[s]); snprintf(src, sn, "%s", STEP[s]); *out = d; return 0; } }
        if (w < n) w += (size_t)snprintf(why + w, n - w, "%s%s design set aside: %s", w ? "; " : "", STEP[s], e);
    }
    snprintf(src, sn, "defaults"); return 0;
}

int design_decode(const hdesign *d, int file, image *im, int alpha, char *why, size_t n) {
    char p[1100]; uint8_t *b; size_t len; memset(im, 0, sizeof *im);
    if (design_path(d, file, p, sizeof p) || ccs_read_file(p, &b, &len, DESIGN_MAX_FILE_BYTES + 1)) { snprintf(why, n, "picture unreadable"); return -1; }
    char hex[65]; ccs_sha256_hex(b, len, hex); if (strcmp(hex, d->f[file].sha)) { free(b); snprintf(why, n, "picture changed since the design was checked"); return -1; }
    int rc = img_decode(b, len, im, alpha, why, n); free(b);
    if (!rc && (im->w != d->f[file].w || im->h != d->f[file].h)) { img_free(im); snprintf(why, n, "picture decodes to another size"); return -1; }
    return rc;
}

int design_peek_startup(const char *root, char *path, size_t pn, char *sha, char *builtin, size_t bn) {
    if (path && pn) path[0] = 0; if (sha) sha[0] = 0; if (builtin && bn) builtin[0] = 0;
    static const char *STEP[2] = { "current", "known-good" };
    for (int s = 0; s < 2; s++) {
        char p[1100], id[80]; snprintf(p, sizeof p, "%s/%s", root, STEP[s]); int rc = read_pointer(p, id, sizeof id);
        if (rc == -2) return DSU_NONE; if (rc) continue; if (!strcmp(id, "none")) return s == 0 ? DSU_NONE : DSU_NONE;
        uint8_t *b; size_t len; char hx[65], jerr[80]; snprintf(p, sizeof p, "%s/d/%s/design.json", root, id);
        if (ccs_read_file(p, &b, &len, DESIGN_MAX_JSON_BYTES)) continue; ccs_sha256_hex(b, len, hx); if (strcmp(hx, id)) { free(b); continue; }
        jval *j = json_parse((const char *)b, len, jerr, sizeof jerr); free(b); if (!j) continue;
        const jval *su = json_get(j, "startup"); const char *m = jstr(su, "mode"); int r = DSU_NONE;
        if (m && !strcmp(m, "random")) r = DSU_RANDOM;
        else if (m && !strcmp(m, "builtin")) { const char *bi = jstr(su, "builtin"); for (int k = 0; k < 5; k++) if (bi && !strcmp(bi, BUILTINS[k])) { r = DSU_BUILTIN; if (builtin) snprintf(builtin, bn, "%s", bi); } }
        else if (m && !strcmp(m, "image")) { const char *nm = jstr(su, "image"); const jval *fs = json_get(j, "files");
            for (size_t i = 0; nm && fs && fs->t == J_ARR && i < fs->n; i++) { const char *fn = jstr(fs->items[i], "name"), *fsh = jstr(fs->items[i], "sha256");
                if (fn && fsh && !strcmp(fn, nm) && is_hex64(fsh) && !strncmp(fn, "img/", 4) && !strchr(fn + 4, '/') && !strstr(fn, "..")) { r = DSU_IMAGE; if (path) snprintf(path, pn, "%s/d/%s/%s", root, id, fn); if (sha) snprintf(sha, 65, "%s", fsh); } } }
        json_free(j); return r;
    }
    return DSU_NONE;
}
int design_startup(const hdesign *d, char *path, size_t pn, char *sha, char *builtin, size_t bn) {
    if (path && pn) path[0] = 0; if (sha) sha[0] = 0; if (builtin && bn) builtin[0] = 0;
    if (!d) return DSU_NONE;
    if (d->startup_mode == DSU_IMAGE) { design_path(d, d->startup_file, path, pn); if (sha) snprintf(sha, 65, "%s", d->f[d->startup_file].sha); }
    if (d->startup_mode == DSU_BUILTIN && builtin) snprintf(builtin, bn, "%s", d->startup_builtin);
    return d->startup_mode;
}
