/* CCS Nucleus's start chooser on the TV (chooser.h): the choices, the controller, the countdown and the picture, the same on Windows,
 * Linux and 32-bit ARM (the core's own tests run it everywhere with --script), and on the console's framebuffer and controllers (devio.c).
 * Copyright (C) 2026 Rance9, GPL-3.0-or-later.
 *
 *   ccs-home-armv7 --choose FILE [--fonts DIR] [--map FILE] [--fb /dev/fb0] [--input auto|PATH[,PATH]] [--input-settle-ms 400]
 *                  [--release-wait-ms 1500] [--watchdog-ms 10000]
 *   ccs-sim-core --choose FILE --script [--fonts DIR] [--final-ppm FILE]   commands on stdin: tick MS | input press|release BUTTON |
 *                  frame FILE.ppm | state | quit; JSON events on stdout (the answer as {"ev":"answer"}, then the picture after it)
 *   test seams (host tests only, never on the console): --fb-file PATH --fb-geometry SPEC, --input PATH (a file or FIFO of input events),
 *   --dump-ppm PATH (the last picture at exit)
 *
 * Why it is careful about the hand-over: the program that runs next (CCS HOME's player, the console's own menu, or the restart) gets the
 * screen, the controllers and the sound as they were. The chooser never opens the sound device; it opens the controllers without taking
 * them from anyone, ignores what they report while they settle after the open (as the player does) and, after a choice, waits until the
 * buttons are let go before it ends, so the press that chose is not seen again by the next program. It draws only into the page on the
 * screen (no page flipping, so ending never pans the display back to an older picture) and leaves its last picture there: "Starting ..."
 * until the next program draws. */
#if defined(__linux__)
#define _GNU_SOURCE
#endif
#include "chooser.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdarg.h>

#define PANEL_W 720
#define ROW_H 58
#define FOOT_H 82
#define HEAD_H 112
#define TEXT_LH 26
#define C_RED 0xE4000F
#define C_PANEL 0x15131F
#define C_FOCUS 0x2A2340
#define C_DIM 0x7D788F
#define C_HINT 0x9A96AC
#define C_TEXT 0xC9C5D8
#define C_NOTE 0xE9E6F2
#define C_WARN 0xFF9AA2

/* ---------------- the choices (ccs-choices/1) ---------------- */
static int id_ok(const char *s) {
    size_t n = strlen(s); if (!n || n > 32 || s[0] == '-') return 0;
    for (; *s; s++) if (!((*s >= 'a' && *s <= 'z') || (*s >= '0' && *s <= '9') || *s == '-')) return 0;
    return 1; }
static int text_ok(const char *s) { for (const unsigned char *p = (const unsigned char *)s; *p; p++) if (*p < 0x20 || *p == 0x7F) return 0; return 1; }
static int put(char *d, size_t n, const char *s) { if (strlen(s) >= n) return -1; memcpy(d, s, strlen(s) + 1); return 0; }   /* never shortened silently */
static int fields(char *s, char **f, int max) {   /* splits at '|': the last field keeps the rest of the line */
    int k = 0; f[k++] = s;
    while (k < max) { char *b = strchr(s, '|'); if (!b) break; *b = 0; s = b + 1; f[k++] = s; }
    return k; }

int chooser_parse(chooser *c, const char *text, size_t len, char *err, size_t n) {
    memset(c, 0, sizeof *c); c->auto_idx = -1; snprintf(c->title, sizeof c->title, "Choose a System");
    char autoid[33] = "", panel[16] = "main"; int ln = 0, have_schema = 0;
#define BAD(...) do { snprintf(err, n, __VA_ARGS__); return -1; } while (0)
    for (const char *p = text, *e = text + len; p < e; ) {
        const char *q = memchr(p, '\n', (size_t)(e - p)); size_t L = q ? (size_t)(q - p) : (size_t)(e - p); char line[640];
        ln++; if (L >= sizeof line) BAD("line %d is too long", ln);
        memcpy(line, p, L); line[L] = 0; p = q ? q + 1 : e;
        if (strlen(line) != L) BAD("line %d holds a zero byte", ln);
        if (L && line[L - 1] == '\r') line[--L] = 0;
        if (ln == 1) { if (strcmp(line, "schema=ccs-choices/1")) BAD("not a ccs-choices/1 file"); have_schema = 1; continue; }
        if (!L) continue;
        char *eq = strchr(line, '='); if (!eq) BAD("line %d is not key=value", ln);
        *eq = 0; const char *k = line; char *v = eq + 1;
        if (!text_ok(v)) BAD("line %d holds a control character", ln);
        if (!strcmp(k, "mode")) { if (!strcmp(v, "menu")) c->menu_mode = 1; else if (strcmp(v, "boot")) BAD("mode is boot or menu"); }
        else if (!strcmp(k, "title")) { if (!*v || put(c->title, sizeof c->title, v)) BAD("title"); }
        else if (!strcmp(k, "highlight")) { if (*v && (!id_ok(v) || put(c->highlight, sizeof c->highlight, v))) BAD("highlight"); }
        else if (!strcmp(k, "auto")) { if (*v && (!id_ok(v) || put(autoid, sizeof autoid, v))) BAD("auto"); }
        else if (!strcmp(k, "timeout")) { char *x; long t = strtol(v, &x, 10); if (!*v || *x || t < 0 || t > 99) BAD("timeout is 0 to 99 seconds"); c->timeout_s = (int)t; }
        else if (!strcmp(k, "notice")) { if (put(c->notice, sizeof c->notice, v)) BAD("notice is too long"); }
        else if (!strcmp(k, "help")) { if (put(c->help, sizeof c->help, v)) BAD("help is too long"); }
        else if (!strcmp(k, "settings")) { if (put(c->settings_label, sizeof c->settings_label, v)) BAD("settings is too long"); }
        else if (!strcmp(k, "panel")) { if (put(panel, sizeof panel, v) || (strcmp(v, "main") && strcmp(v, "settings"))) BAD("panel is main or settings"); }
        else if (!strcmp(k, "choice")) {
            char *f[6]; if (c->nopt >= CH_MAX_OPT) BAD("more than %d choices", CH_MAX_OPT);
            if (fields(v, f, 6) != 6) BAD("line %d: a choice is ID|KIND|ROOT|STATE|LABEL|REASON", ln);
            ch_opt *o = &c->opt[c->nopt];
            if (!id_ok(f[0]) || put(o->id, sizeof o->id, f[0])) BAD("line %d: choice id", ln);
            for (int i = 0; i < c->nopt; i++) if (!strcmp(c->opt[i].id, o->id)) BAD("choice %s is listed twice", o->id);
            if (strcmp(f[1], "native") && strcmp(f[1], "ccs-home") && strcmp(f[1], "owner-root")) BAD("choice %s: kind", o->id);
            if (!strcmp(f[1], "native") != !strcmp(o->id, "native")) BAD("exactly one choice native, of kind native");
            snprintf(o->kind, sizeof o->kind, "%s", f[1]);
            if (!strcmp(f[2], "restart")) o->restart = 1; else if (strcmp(f[2], "same")) BAD("choice %s: root is same or restart", o->id);
            if (!strcmp(f[3], "ok")) o->state = CH_OK; else if (!strcmp(f[3], "retry")) o->state = CH_RETRY;
            else if (!strcmp(f[3], "unavailable")) o->state = CH_UNAVAILABLE; else BAD("choice %s: state", o->id);
            if (!*f[4] || put(o->label, sizeof o->label, f[4])) BAD("choice %s: label", o->id);
            if (put(o->reason, sizeof o->reason, f[5])) BAD("choice %s: reason is too long", o->id);
            if (o->state != CH_OK && !o->reason[0]) snprintf(o->reason, sizeof o->reason, "It cannot start now.");   /* refused, never without a reason */
            c->nopt++; }
        else if (!strcmp(k, "setting")) {
            char *f[2]; if (c->nset >= CH_MAX_SET) BAD("more than %d settings", CH_MAX_SET);
            if (fields(v, f, 2) != 2 || !id_ok(f[0]) || !*f[1]) BAD("line %d: a setting is KEY|LABEL", ln);
            ch_set *s = &c->set[c->nset]; if (put(s->key, sizeof s->key, f[0]) || put(s->label, sizeof s->label, f[1])) BAD("line %d: setting", ln);
            c->nset++; }
        /* any other key: something a later CCS Nucleus adds; this program shows what it knows */
    }
    if (!have_schema) BAD("empty file");
    int nat = -1; for (int i = 0; i < c->nopt; i++) if (!strcmp(c->opt[i].id, "native")) nat = i;
    if (nat < 0) BAD("the console's own system is not among the choices");
    if (c->opt[nat].state != CH_OK) BAD("the console's own system must always be a choice that can start");
    if (!c->nset) c->settings_label[0] = 0; else if (!c->settings_label[0]) snprintf(c->settings_label, sizeof c->settings_label, "Settings");
    if (autoid[0] && c->timeout_s > 0) for (int i = 0; i < c->nopt; i++) if (!strcmp(c->opt[i].id, autoid) && c->opt[i].state == CH_OK) c->auto_idx = i;
    c->focus = c->auto_idx >= 0 ? c->auto_idx : nat;
    for (int i = 0; i < c->nopt; i++) if (!strcmp(c->opt[i].id, c->highlight)) c->focus = i;
    if (!strcmp(panel, "settings") && c->nset) { c->panel = CH_SETTINGS; c->focus = c->nopt; for (int i = 0; i < c->nset; i++) if (!strcmp(c->set[i].key, c->highlight)) c->sfocus = i; c->auto_idx = -1; }
#undef BAD
    return 0; }

int chooser_fonts(chooser *c, const char *dir) {
    if (!dir || !*dir) return 0;
    const char *n[3] = { "silk17.ccsf", "sans600_22.ccsf", "sans400_17.ccsf" }; font *f[3] = { &c->f_title, &c->f_item, &c->f_text }; char p[1200], e[200]; int ok = 1;
    for (int i = 0; i < 3; i++) { snprintf(p, sizeof p, "%s/%s", dir, n[i]); if (font_load(f[i], p, e, sizeof e)) ok = 0; }
    if (!ok) for (int i = 0; i < 3; i++) font_free(f[i]);   /* all three or none: one picture, never a mixture */
    c->fonts = ok; c->gen++; return ok; }
void chooser_free(chooser *c) { if (c->fonts) { font_free(&c->f_title); font_free(&c->f_item); font_free(&c->f_text); c->fonts = 0; } }

/* ---------------- the controller and the countdown ---------------- */
static int main_rows(const chooser *c) { return c->nopt + (c->settings_label[0] ? 1 : 0); }
static void answer(chooser *c, const char *fmt, ...) { va_list a; va_start(a, fmt); vsnprintf(c->answer, sizeof c->answer, fmt, a); va_end(a); c->done = 1; c->counting = 0; c->gen++; }
static int secs_left(const chooser *c) { return c->counting ? (int)((c->deadline_ms - c->now + 999) / 1000) : -1; }
void chooser_start(chooser *c, uint64_t now) {
    c->now = now; c->counting = c->auto_idx >= 0 && c->timeout_s > 0; c->deadline_ms = now + (uint64_t)c->timeout_s * 1000u; c->gen++; }
int chooser_countdown_ms(const chooser *c) { return c->counting ? (int)(c->deadline_ms > c->now ? c->deadline_ms - c->now : 0) : -1; }
void chooser_tick(chooser *c, uint64_t now) {
    if (c->done) return;
    int before = secs_left(c); c->now = now;
    if (c->counting && now >= c->deadline_ms) { answer(c, "choice %s timeout", c->opt[c->auto_idx].id); return; }
    if (secs_left(c) != before) c->gen++; }
void chooser_input(chooser *c, button b, in_kind k, uint64_t now) {
    if (c->done || (k != IN_PRESS && k != IN_REPEAT)) return;
    c->now = now;
    if (c->counting) { c->counting = 0; c->gen++; }   /* any button: someone is here, so nothing starts by itself any more */
    int up = b == B_UP, down = b == B_DOWN;
    if (c->panel == CH_MAIN) {
        int rows = main_rows(c);
        if (up || down) { c->focus = (c->focus + rows + (down ? 1 : -1)) % rows; c->refusal[0] = 0; c->gen++; return; }
        if (k == IN_REPEAT) return;   /* a held A or B never repeats a choice */
        if (b == B_A || b == B_START) {
            if (c->focus >= c->nopt) { c->panel = CH_SETTINGS; c->sfocus = 0; c->refusal[0] = 0; c->gen++; return; }
            const ch_opt *o = &c->opt[c->focus];
            if (o->state == CH_UNAVAILABLE) { snprintf(c->refusal, sizeof c->refusal, "%s cannot start now. %s", o->label, o->reason); c->gen++; return; }   /* refused, with its reason */
            answer(c, "choice %s %s", o->id, b == B_A ? "a" : "start");
        } else if (b == B_B && c->menu_mode) answer(c, "back");
        return; }
    int rows = c->nset + 1;   /* the settings, then Back */
    if (up || down) { c->sfocus = (c->sfocus + rows + (down ? 1 : -1)) % rows; c->gen++; return; }
    if (k == IN_REPEAT) return;
    if ((b == B_A || b == B_START) && c->sfocus < c->nset) {
        for (int i = 0; i < c->nopt; i++) if (!strcmp(c->opt[i].id, c->set[c->sfocus].key) && c->opt[i].state != CH_UNAVAILABLE) { answer(c, "choice %s %s", c->opt[i].id, b == B_A ? "a" : "start"); return; }   /* a row named after an option starts it (the recovery entry) */
        answer(c, "action %s", c->set[c->sfocus].key); }
    else if (b == B_A || b == B_START || b == B_B) { c->panel = CH_MAIN; c->focus = c->nopt; c->gen++; } }

/* ---------------- the picture ---------------- */
static const rect FULL = { 0, 0, CCS_FB_W, CCS_FB_H };
enum { T_TITLE, T_ITEM, T_TEXT };
static const font *face(chooser *c, int role) { return role == T_TITLE ? &c->f_title : role == T_ITEM ? &c->f_item : &c->f_text; }
/* text with the baked fonts, or (no fonts) with the core's built-in capital letters at a size that fills the same line */
static void say(chooser *c, framebuffer *fb, int role, int x, int y, int maxw, const char *s, uint32_t rgb) {
    if (c->fonts) { char o[400]; text_fit(face(c, role), s, maxw, 0, o, sizeof o); text_draw(fb, NULL, FULL, face(c, role), x, y, o, strlen(o), RGBA(rgb, 255), 0, 0); return; }
    int sc = role == T_TEXT ? 2 : 3, max = maxw / (6 * sc); char o[400]; size_t k = 0;
    for (const unsigned char *p = (const unsigned char *)s; *p && k + 4 < sizeof o; p++) {   /* the built-in letters are ASCII: the middle dot and the ellipsis in ASCII */
        if (p[0] == 0xC2 && p[1] == 0xB7) { o[k++] = '-'; p++; } else if (p[0] == 0xE2 && p[1] == 0x80 && p[2] == 0xA6) { memcpy(o + k, "...", 3); k += 3; p += 2; } else if (*p < 0x80) o[k++] = (char)*p; }
    o[k] = 0; if ((int)k > max && max > 3) { o[max - 3] = 0; strcat(o, "..."); }
    fb_text(fb, x, y - 7 * sc, sc, o, (uint8_t)(rgb >> 16), (uint8_t)(rgb >> 8), (uint8_t)rgb); }
static int wrap(chooser *c, const char *s, int maxw, char out[][200], int max) {   /* lines of a paragraph */
    if (!s[0]) return 0;
    if (!c->fonts) { int per = maxw / 12, n = 0; size_t L = strlen(s), i = 0;
        while (i < L && n < max) { size_t take = L - i > (size_t)per ? (size_t)per : L - i; if (i + take < L) { size_t b = take; while (b > 0 && s[i + b] != ' ') b--; if (b) take = b; }
            snprintf(out[n++], 200, "%.*s", (int)take, s + i); i += take; while (s[i] == ' ') i++; }
        return n; }
    span sp[8]; size_t k = text_wrap(&c->f_text, s, maxw, 0, sp, 8); int n = 0;
    for (size_t i = 0; i < k && n < max; i++) { size_t L = sp[i].len < 199 ? sp[i].len : 199; memcpy(out[n], s + sp[i].start, L); out[n][L] = 0; n++; }
    return n; }
static void backdrop(framebuffer *fb) { stop g[2] = { { 0, 0x1B1830, 1 }, { 1, 0x0A0910, 1 } }; gfx_gradient(fb, FULL, 0, 0, CCS_FB_W, CCS_FB_H, 1, g, 2); }
static void panel(framebuffer *fb, int x, int y, int w, int h) {
    gfx_shadow(fb, FULL, x, y + 8, w, h, 18, RGBA(0x000000, 170));
    gfx_rrect(fb, FULL, x, y, w, h, 18, RGBA(C_PANEL, 246)); gfx_rrect_ring(fb, FULL, x, y, w, h, 18, 3, RGBA(C_RED, 255));
    gfx_fill(fb, FULL, x + 32, y + 30, 40, 4, RGBA(C_RED, 255)); }
static void arrow(framebuffer *fb, int x, int y, int dir) {   /* a small triangle: more rows above (dir -1) or below (1) */
    for (int i = 0; i < 7; i++) gfx_fill(fb, FULL, x - i, dir < 0 ? y + i : y + 6 - i, 2 * i + 1, 1, RGBA(C_HINT, 255)); }

/* the text under the title: a refusal (why the chosen option cannot start) or the notice, then in the settings panel its help */
typedef struct { char line[6][200]; int n, first; uint32_t col; } tblock;
static int text_block(chooser *c, tblock *t) {
    const char *s = c->refusal[0] ? c->refusal : c->notice; t->col = c->refusal[0] ? C_WARN : C_NOTE;
    t->n = t->first = wrap(c, s, PANEL_W - 72, t->line, 3);
    if (c->panel == CH_SETTINGS && c->help[0]) t->n += wrap(c, c->help, PANEL_W - 72, t->line + t->n, 6 - t->n);
    return t->n ? t->n * TEXT_LH + 12 : 0; }
static int rows_now(const chooser *c) { return c->panel == CH_SETTINGS ? c->nset + 1 : main_rows(c); }
static int scroll(chooser *c, int th) {   /* how many rows fit under the text; the focused row kept in view */
    int rows = rows_now(c), focus = c->panel == CH_SETTINGS ? c->sfocus : c->focus, vis = (CCS_FB_H - 32 - HEAD_H - th - FOOT_H) / ROW_H;
    if (vis > rows) vis = rows; if (vis < 1) vis = 1;
    if (focus < c->top) c->top = focus; if (focus >= c->top + vis) c->top = focus - vis + 1; if (c->top > rows - vis) c->top = rows - vis; if (c->top < 0) c->top = 0;
    return vis; }
/* the footer's first line: the countdown, or why the focused choice cannot start (or did not last time) */
static uint32_t status_text(const chooser *c, char *out, size_t n) {
    int s = secs_left(c); out[0] = 0; if (c->panel == CH_SETTINGS) return C_NOTE;
    if (s >= 0) { snprintf(out, n, "%s starts in %d second%s. Press any button to stay here.", c->opt[c->auto_idx].label, s, s == 1 ? "" : "s"); return C_NOTE; }
    if (c->focus < c->nopt && c->opt[c->focus].state != CH_OK && !c->refusal[0]) { snprintf(out, n, "%s", c->opt[c->focus].reason); return c->opt[c->focus].state == CH_UNAVAILABLE ? C_WARN : C_NOTE; }
    return C_NOTE; }

void chooser_render(chooser *c, framebuffer *fb) {
    backdrop(fb);
    int settings = c->panel == CH_SETTINGS, rows = rows_now(c), focus = settings ? c->sfocus : c->focus;
    tblock t; int th = text_block(c, &t), vis = scroll(c, th);
    int H = HEAD_H + th + vis * ROW_H + FOOT_H, x = (CCS_FB_W - PANEL_W) / 2, y = (CCS_FB_H - H) / 2;
    panel(fb, x, y, PANEL_W, H);
    say(c, fb, T_TITLE, x + 32, y + 62, PANEL_W - 64, settings ? c->settings_label : c->title, 0xFFFFFF);
    int cy = y + 92;
    for (int i = 0; i < t.n; i++) { say(c, fb, T_TEXT, x + 36, cy + 20, PANEL_W - 72, t.line[i], i < t.first ? t.col : C_TEXT); cy += TEXT_LH; }
    if (t.n) cy += 12;
    for (int r = c->top; r < c->top + vis; r++) {
        int fy = cy + (r - c->top) * ROW_H, foc = r == focus; char lab[120]; uint32_t col = 0xFFFFFF;
        if (settings) snprintf(lab, sizeof lab, "%s", r < c->nset ? c->set[r].label : "Back");
        else if (r < c->nopt) { const ch_opt *o = &c->opt[r]; if (o->state == CH_UNAVAILABLE) { snprintf(lab, sizeof lab, "%s (Not Available)", o->label); col = C_DIM; } else snprintf(lab, sizeof lab, "%s", o->label); }
        else snprintf(lab, sizeof lab, "%s", c->settings_label);
        if (foc) { gfx_rrect(fb, FULL, x + 22, fy, PANEL_W - 44, ROW_H - 8, 12, RGBA(C_FOCUS, 255)); gfx_rrect_ring(fb, FULL, x + 22, fy, PANEL_W - 44, ROW_H - 8, 12, 3, RGBA(C_RED, 255)); }
        say(c, fb, T_ITEM, x + 44, fy + 33, PANEL_W - 100, lab, col); }
    if (c->top > 0) arrow(fb, x + PANEL_W - 44, cy - 10, -1);
    if (c->top + vis < rows) arrow(fb, x + PANEL_W - 44, cy + vis * ROW_H - 2, 1);
    /* the footer: the countdown or the focused choice's reason; then what the buttons do */
    char status[400]; uint32_t scol = status_text(c, status, sizeof status);
    if (status[0]) say(c, fb, T_TEXT, x + 36, y + H - 52, PANEL_W - 72, status, scol);
    say(c, fb, T_TEXT, x + 36, y + H - 22, PANEL_W - 72, settings ? "D-pad: choose  \xC2\xB7  A: select  \xC2\xB7  B: back" : c->menu_mode ? "D-pad: choose  \xC2\xB7  A: start  \xC2\xB7  B: back" : "D-pad: choose  \xC2\xB7  A: start", C_HINT); }

void chooser_render_final(chooser *c, framebuffer *fb) {
    const ch_opt *o = NULL; char id[40] = "";
    if (!strncmp(c->answer, "choice ", 7)) { sscanf(c->answer + 7, "%32s", id); for (int i = 0; i < c->nopt; i++) if (!strcmp(c->opt[i].id, id)) o = &c->opt[i]; }
    if (!o) { chooser_render(c, fb); return; }
    char line[200]; snprintf(line, sizeof line, o->restart ? "Restarting the console into %s\xE2\x80\xA6" : "Starting %s\xE2\x80\xA6", o->label);
    backdrop(fb); int H = HEAD_H + 40, x = (CCS_FB_W - PANEL_W) / 2, y = (CCS_FB_H - H) / 2; panel(fb, x, y, PANEL_W, H);
    say(c, fb, T_TITLE, x + 32, y + 62, PANEL_W - 64, c->title, 0xFFFFFF); say(c, fb, T_ITEM, x + 36, y + 112, PANEL_W - 72, line, 0xFFFFFF); }

/* ---------------- the program ---------------- */
static int read_choices(chooser *c, const char *path, char *err, size_t n) {
    uint8_t *b; size_t len; if (ccs_read_file(path, &b, &len, 64u << 10)) { snprintf(err, n, "the choices file cannot be read"); return -1; }
    int r = chooser_parse(c, (const char *)b, len, err, n); free(b); return r; }
static const char *optv(int argc, char **argv, const char *k) { for (int i = 1; i + 1 < argc; i++) if (!strcmp(argv[i], k)) return argv[i + 1]; return NULL; }
static int flag(int argc, char **argv, const char *k) { for (int i = 1; i < argc; i++) if (!strcmp(argv[i], k)) return 1; return 0; }
static void on_button(void *ctx, button b, in_kind k, uint64_t t) { chooser_input((chooser *)ctx, b, k, t); }
static void repeat_like_home(input_state *s) { s->repeat_delay_ms = 400; s->repeat_rate_ms = 110; s->repeat_fast_after = 6; s->repeat_fast_rate = 55; }   /* HOME_REPEAT */

static void jemit(const char *fmt, ...) { char b[2048]; va_list a; va_start(a, fmt); vsnprintf(b, sizeof b, fmt, a); va_end(a); fputs(b, stdout); fputc('\n', stdout); fflush(stdout); }
static void emit_state(chooser *c) {
    char f[80], n[700], r[700], s[400], se[500]; const char *fid = c->panel == CH_SETTINGS ? (c->sfocus < c->nset ? c->set[c->sfocus].key : "back") : c->focus < c->nopt ? c->opt[c->focus].id : "settings";
    tblock t; int vis = scroll(c, text_block(c, &t)); status_text(c, s, sizeof s);
    json_escape(fid, f, sizeof f); json_escape(c->notice, n, sizeof n); json_escape(c->refusal, r, sizeof r); json_escape(s, se, sizeof se);
    jemit("{\"ev\":\"chooser_state\",\"panel\":\"%s\",\"focus\":%s,\"row\":%d,\"top\":%d,\"visible\":%d,\"countdown_ms\":%d,\"notice\":%s,\"refusal\":%s,\"status\":%s,\"fonts\":%s,\"gen\":%u}",
          c->panel == CH_SETTINGS ? "settings" : "main", f, c->panel == CH_SETTINGS ? c->sfocus : c->focus, c->top, vis, chooser_countdown_ms(c), n, r, se, c->fonts ? "true" : "false", c->gen); }
/* --script: the same chooser driven by commands, on any computer (the core's tests on Windows, Linux and 32-bit ARM under QEMU) */
static int script_main(chooser *c, const char *final) {   /* final: the picture after the answer (--final-ppm) */
    framebuffer fb; if (fb_init(&fb, CCS_FB_W, CCS_FB_H)) { jemit("{\"ev\":\"error\",\"error\":\"framebuffer allocation failed\"}"); return 5; }
    input_state in; input_init(&in); repeat_like_home(&in); chooser_start(c, 0);
    char ids[CH_MAX_OPT * 48] = ""; size_t L = 0; for (int i = 0; i < c->nopt && L < sizeof ids - 48; i++) L += (size_t)snprintf(ids + L, sizeof ids - L, "%s\"%s\"", i ? "," : "", c->opt[i].id);
    jemit("{\"ev\":\"chooser_ready\",\"options\":[%s],\"settings\":%d,\"menu_mode\":%s,\"auto\":%d,\"timeout_s\":%d}", ids, c->nset, c->menu_mode ? "true" : "false", c->auto_idx, c->timeout_s);
    emit_state(c); unsigned seen = c->gen; char line[1200], esc[1300];
    while (!c->done && fgets(line, sizeof line, stdin)) {
        size_t n = strlen(line); while (n && (line[n - 1] == '\n' || line[n - 1] == '\r')) line[--n] = 0;
        char *cmd = line, *arg = strchr(line, ' '); if (arg) *arg++ = 0;
        if (!strcmp(cmd, "tick") && arg) { uint64_t ms = strtoull(arg, NULL, 10); input_tick(&in, ms, on_button, c); chooser_tick(c, in.clock_ms); }
        else if (!strcmp(cmd, "input") && arg) { char *b = strchr(arg, ' '); button bt; if (!b || (*b++ = 0, !button_parse(b, &bt))) { jemit("{\"ev\":\"error\",\"error\":\"input press|release BUTTON\"}"); continue; }
            if (!strcmp(arg, "press")) input_press(&in, bt, on_button, c); else input_release(&in, bt, on_button, c); }
        else if (!strcmp(cmd, "frame") && arg) { uint32_t crc = 0; if (c->done) chooser_render_final(c, &fb); else chooser_render(c, &fb); int rc = fb_write_ppm(&fb, arg, &crc); json_escape(arg, esc, sizeof esc);
            jemit(rc ? "{\"ev\":\"frame_failed\",\"path\":%s}" : "{\"ev\":\"frame_written\",\"path\":%s,\"crc32\":\"%08x\"}", esc, crc); }
        else if (!strcmp(cmd, "state")) emit_state(c);
        else if (!strcmp(cmd, "quit")) { jemit("{\"ev\":\"quit\"}"); fb_free(&fb); return 0; }
        else { json_escape(cmd, esc, sizeof esc); jemit("{\"ev\":\"error\",\"error\":\"unknown command\",\"command\":%s}", esc); }
        if (c->gen != seen && !c->done) { seen = c->gen; emit_state(c); } }
    if (c->done) { json_escape(c->answer, esc, sizeof esc); jemit("{\"ev\":\"answer\",\"answer\":%s}", esc);
        if (final) { uint32_t crc = 0; chooser_render_final(c, &fb); int rc = fb_write_ppm(&fb, final, &crc); json_escape(final, esc, sizeof esc); jemit(rc ? "{\"ev\":\"frame_failed\",\"path\":%s}" : "{\"ev\":\"final_frame\",\"path\":%s,\"crc32\":\"%08x\"}", esc, crc); } }
    else jemit("{\"ev\":\"stdin_closed\"}");
    fb_free(&fb); return 0; }

int chooser_device(chooser *c, int argc, char **argv);
void chooser_signals(void);
int chooser_main(int argc, char **argv) {
    const char *file = optv(argc, argv, "--choose"); chooser c; char err[300];
    if (!file) { fprintf(stderr, "usage: --choose FILE [--script] [--fonts DIR] [device options]\n"); return 2; }
    if (!flag(argc, argv, "--script")) chooser_signals();   /* first of all: a stop while the choices and fonts load is still a stop (exit 15), never a death by signal */
    if (read_choices(&c, file, err, sizeof err)) {
        if (flag(argc, argv, "--script")) { char e[400]; json_escape(err, e, sizeof e); jemit("{\"ev\":\"choices_refused\",\"error\":%s}", e); }
        else fprintf(stderr, "{\"ev\":\"choices_refused\",\"error\":\"%s\"}\n", err);
        return 3; }
    chooser_fonts(&c, optv(argc, argv, "--fonts"));
    int rc = flag(argc, argv, "--script") ? script_main(&c, optv(argc, argv, "--final-ppm")) : chooser_device(&c, argc, argv);
    chooser_free(&c); return rc; }

#if !defined(__linux__)
void chooser_signals(void) {}
int chooser_device(chooser *c, int argc, char **argv) { (void)c; (void)argc; (void)argv; fputs("{\"ev\":\"error\",\"error\":\"the chooser on a screen is the console's (Linux); use --script here\"}\n", stderr); return 2; }
#else
/* ---------------- the console: its framebuffer and controllers (devio.c) ---------------- */
#include "devio.h"
#include <errno.h>
#include <signal.h>
#include <poll.h>
#include <unistd.h>
static volatile sig_atomic_t g_stop;
static void on_signal(int s) { if (g_stop) _exit(128 + s); g_stop = s; }
static void on_watchdog(int s) { static const char m[] = "{\"ev\":\"watchdog\",\"error\":\"a step took too long; the chooser stops without an answer\"}\n"; ssize_t r = write(2, m, sizeof m - 1); (void)r; (void)s; _exit(70); }
void chooser_signals(void) {
    struct sigaction sa; memset(&sa, 0, sizeof sa); sa.sa_handler = on_signal; sigemptyset(&sa.sa_mask); sigaction(SIGINT, &sa, NULL); sigaction(SIGTERM, &sa, NULL); sigaction(SIGHUP, &sa, NULL);
    signal(SIGPIPE, SIG_IGN); sa.sa_handler = on_watchdog; sigaction(SIGALRM, &sa, NULL); }
typedef struct { chooser *c; dio_map map; dio_inputs in; input_state st; int settled[DIO_MAX_DEV], mask[DIO_MAX_DEV][B_COUNT], held[B_COUNT], settle_ms; uint64_t t0; } chdev;
static void trace(const char *fmt, ...) { char b[1200]; va_list a; va_start(a, fmt); vsnprintf(b, sizeof b, fmt, a); va_end(a); fprintf(stderr, "%s\n", b); fflush(stderr); }
static uint64_t now_ms(const chdev *D) { return dio_mono_ms() - D->t0; }
/* one controller's button: the chooser sees a press when the first controller presses it and a release when the last one lets go */
static void down(chdev *D, button b) { if (D->held[b]++ == 0) input_press(&D->st, b, on_button, D->c); }
static void up(chdev *D, button b) { if (D->held[b] > 0 && --D->held[b] == 0) input_release(&D->st, b, on_button, D->c); }
static void ev(void *ctx, dio_dev *d, int type, int code, int value) {
    chdev *D = ctx; int i = (int)(d - D->in.d);
    if (type == DIO_EV_SYN && code == DIO_SYN_DROPPED) { for (int b = 0; b < B_COUNT; b++) { if (d->down[b] && !D->mask[i][b]) up(D, (button)b); d->down[b] = 0; D->mask[i][b] = 0; } return; }
    if (type != DIO_EV_KEY || code < 0 || code > DIO_KEY_MAX || D->map.key[code] < 0 || value == 2) return;   /* value 2: the kernel's own repeat */
    button b = (button)D->map.key[code];
    if (!D->settled[i]) { d->down[b] = value ? 1 : 0; d->settle_ignored++; return; }   /* settling: what the driver reports right after the open is not a press */
    if (value) { if (!d->down[b]) { d->down[b] = 1; if (!D->mask[i][b]) down(D, b); } }
    else if (d->down[b]) { d->down[b] = 0; if (D->mask[i][b]) D->mask[i][b] = 0; else up(D, b); } }   /* a button held through the settling counts only once let go */
static void lost(chdev *D, int i) {
    char p[160]; json_escape(D->in.d[i].path, p, sizeof p); trace("{\"ev\":\"pad_lost\",\"path\":%s}", p);
    for (int b = 0; b < B_COUNT; b++) if (D->in.d[i].down[b] && !D->mask[i][b]) up(D, (button)b);
    int last = D->in.n - 1; D->settled[i] = D->settled[last]; memcpy(D->mask[i], D->mask[last], sizeof D->mask[i]); dio_dev_close(&D->in, i); }
static void seen(void *ctx, int kind, const char *path, const char *name, int mapped) {
    chdev *D = ctx; (void)mapped; if (kind != 1) return; int i = D->in.n - 1; D->settled[i] = 0; memset(D->mask[i], 0, sizeof D->mask[i]);
    char a[160], b[300]; json_escape(path, a, sizeof a); json_escape(name, b, sizeof b); trace("{\"ev\":\"pad_open\",\"path\":%s,\"name\":%s}", a, b); }
static void pump(chdev *D) {
    uint64_t t = now_ms(D);
    if (t > D->st.clock_ms) input_tick(&D->st, t - D->st.clock_ms, on_button, D->c);   /* the clock first: a press is at the moment it is read */
    for (int i = 0; i < D->in.n; i++) if (dio_dev_read(&D->in.d[i], ev, D) < 0) { lost(D, i); i--; }
    /* settled only after reading: whatever waited since the open (also when it is read late, after a slow picture) is never a press, and
       a button still down then counts only once it has been let go */
    for (int i = 0; i < D->in.n; i++) if (!D->settled[i] && dio_mono_ms() >= D->in.d[i].settle_until) { D->settled[i] = 1; for (int b = 0; b < B_COUNT; b++) D->mask[i][b] = D->in.d[i].down[b]; }
    if (!D->in.fixed && D->in.n < 2 && dio_mono_us() >= D->in.next_scan_us) { D->in.next_scan_us = dio_mono_us() + 1000000u; dio_inputs_scan(&D->in, &D->map, 0, (uint64_t)D->settle_ms, seen, D); }   /* a controller plugged in later */
    chooser_tick(D->c, now_ms(D)); }
static void wait_ms(chdev *D, int ms) {
    struct pollfd pf[DIO_MAX_DEV]; int n = 0; for (int i = 0; i < D->in.n; i++) { pf[n].fd = D->in.d[i].fd; pf[n].events = POLLIN; pf[n].revents = 0; n++; }
    if (ms < 0) ms = 0; poll(n ? pf : NULL, (nfds_t)n, ms); }
static void show(dio_fb *f, const framebuffer *fb) { dio_fb_convert(f, fb, f->shown_yoff); }   /* the page on the screen, never a flip */

int chooser_device(chooser *c, int argc, char **argv) {
    const char *fbp = optv(argc, argv, "--fb"), *fbfile = optv(argc, argv, "--fb-file"), *geom = optv(argc, argv, "--fb-geometry"), *input = optv(argc, argv, "--input"), *mapf = optv(argc, argv, "--map"), *dump = optv(argc, argv, "--dump-ppm"), *v;
    int release_wait = (v = optv(argc, argv, "--release-wait-ms")) ? atoi(v) : 1500, wd = (v = optv(argc, argv, "--watchdog-ms")) ? (atoi(v) + 999) / 1000 : 10;
    chdev D; memset(&D, 0, sizeof D); D.c = c; D.settle_ms = (v = optv(argc, argv, "--input-settle-ms")) ? atoi(v) : 400;
    if (D.settle_ms < 0 || D.settle_ms > 5000 || release_wait < 0 || release_wait > 10000 || (fbfile && !geom)) { fprintf(stderr, "bad options (--input-settle-ms 0..5000, --release-wait-ms 0..10000; --fb-file needs --fb-geometry)\n"); return 2; }
    if (g_stop) { trace("{\"ev\":\"stopped\",\"signal\":%d}", (int)g_stop); return 15; }   /* asked to end while it was loading (chooser_signals, chooser_main) */
    dio_fb f; char e[400];
    if (dio_fb_open(&f, fbfile ? fbfile : fbp ? fbp : "/dev/fb0", fbfile ? geom : NULL, 1, e, sizeof e)) { char q[500]; json_escape(e, q, sizeof q); trace("{\"ev\":\"refused\",\"stage\":\"framebuffer\",\"error\":%s}", q); dio_fb_close(&f, 0, NULL, 0); return 12; }
    f.flip = 0;
    if (!mapf || dio_map_load(&D.map, mapf, e, sizeof e)) dio_map_default(&D.map);
    if (D.map.ndev < 2) { snprintf(D.map.devices[D.map.ndev], sizeof D.map.devices[0], "Nintendo Clovercon - controller2"); D.map.ndev++; }   /* either controller chooses */
    D.t0 = dio_mono_ms(); input_init(&D.st); repeat_like_home(&D.st);
    framebuffer fb; if (fb_init(&fb, CCS_FB_W, CCS_FB_H)) { dio_fb_close(&f, 0, NULL, 0); return 12; }
    chooser_start(c, now_ms(&D)); chooser_render(c, &fb); show(&f, &fb); unsigned drawn = c->gen;   /* the screen first; the controllers settle from then on */
    if (input && strcmp(input, "auto")) { char list[600]; snprintf(list, sizeof list, "%s", input); D.in.fixed = input; char *s = NULL;
        for (char *p = strtok_r(list, ",", &s); p; p = strtok_r(NULL, ",", &s)) if (dio_inputs_open_fixed(&D.in, p, (uint64_t)D.settle_ms, seen, &D)) { char q[700]; json_escape(p, q, sizeof q); trace("{\"ev\":\"input_missing\",\"path\":%s}", q); } }
    else dio_inputs_scan(&D.in, &D.map, 0, (uint64_t)D.settle_ms, seen, &D);
    trace("{\"ev\":\"chooser_open\",\"options\":%d,\"settings\":%d,\"auto\":%d,\"timeout_s\":%d,\"fonts\":%s,\"controllers\":%d}", c->nopt, c->nset, c->auto_idx, c->timeout_s, c->fonts ? "true" : "false", D.in.n);
    char said[300] = "";
    while (!g_stop && !c->done) {
        if (wd) alarm((unsigned)wd);
        pump(&D);
        if (c->done) break;
        if (strcmp(said, c->refusal)) { snprintf(said, sizeof said, "%s", c->refusal); if (said[0]) { char q[400]; json_escape(said, q, sizeof q); trace("{\"ev\":\"refused\",\"text\":%s}", q); } }
        if (c->gen != drawn) { drawn = c->gen; chooser_render(c, &fb); show(&f, &fb); }
        int w = 100, cd = chooser_countdown_ms(c); if (cd >= 0) { int to_sec = cd % 1000; if (to_sec + 1 < w) w = to_sec + 1; }
        for (int i = 0; i < D.in.n; i++) if (!D.settled[i]) { long due = (long)(D.in.d[i].settle_until - dio_mono_ms()); if (due + 1 < w) w = due < 0 ? 0 : (int)due + 1; }
        wait_ms(&D, w); }
    if (g_stop) { alarm(0); trace("{\"ev\":\"stopped\",\"signal\":%d}", (int)g_stop); for (int i = D.in.n - 1; i >= 0; i--) dio_dev_close(&D.in, i); dio_fb_close(&f, 0, NULL, 0); fb_free(&fb); return 15; }
    uint64_t t_answer = now_ms(&D);   /* (the trace's times: ms from the start of the countdown) */
    if (strncmp(c->answer, "action ", 7) && strcmp(c->answer, "back")) { chooser_render_final(c, &fb); show(&f, &fb); }   /* what starts now, until the next program draws */
    /* the press that chose is let go before the next program opens the controllers */
    uint64_t until = dio_mono_ms() + (uint64_t)release_wait; int held = 0;
    for (;;) { held = 0; for (int b = 0; b < B_COUNT; b++) held += D.held[b]; if (!held || g_stop || dio_mono_ms() >= until) break; if (wd) alarm((unsigned)wd); pump(&D); wait_ms(&D, 20); }
    alarm(0);
    char q[200]; json_escape(c->answer, q, sizeof q);
    trace("{\"ev\":\"answer\",\"answer\":%s,\"held_at_end\":%d,\"answer_ms\":%llu,\"end_ms\":%llu}", q, held, (unsigned long long)t_answer, (unsigned long long)now_ms(&D));
    if (dump) fb_write_ppm(&fb, dump, NULL);
    for (int i = D.in.n - 1; i >= 0; i--) dio_dev_close(&D.in, i);
    dio_fb_close(&f, 0, NULL, 0); fb_free(&fb);
    printf("%s\n", c->answer); fflush(stdout); return 0; }
#endif
