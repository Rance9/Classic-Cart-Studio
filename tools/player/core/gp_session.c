/* gp_session.c: the player's game lifecycle, the same for both entry modes (Direct Game Mode and HOME Mode).
 *
 *   HOME      (HOME mode) the configured HOME, drawn by the native HOME core; A on a game asks gp_launch()
 *   START     every launch, before the game runs: Load Last Used Save (the slot and its time) / Load a Save Slot / Start New Game /
 *             back (and, when an earlier test version left one, Recover an Earlier Automatic Save). Starting a new game writes nothing
 *   PLAY      the game runs; holding SELECT+START for 3 seconds (any player) opens the menu, so does Esc or closing the window; while
 *             both are held the game waits (frozen), and goes on if they are released sooner
 *   MENU      paused: Keep Playing / Save to a Slot (then keep playing or leave) / Load a Save Slot / Exit Without Saving (HOME mode:
 *             Return to HOME Without Saving), then Controllers, Full Screen. Nothing saves by itself: the four slots are the only
 *             emulator saves, each written only when the person chooses it (the game's own cartridge save works as on the console)
 *   SLOTS     the four manual slots (save, load, or copying the earlier automatic save into one), with an overwrite confirmation
 *   GSET      Game Settings for one exact game (from the game menu, the HOME's Game Details, or HOME Settings > Selected Game Settings):
 *             Rapid Fire (Turbo), Controls (swap A and B), Picture; Use Default follows the Gameplay Defaults / Play on PC Settings.
 *             HOME Settings also holds Controllers & Keyboard and the Gameplay Defaults (rapid fire for every game without its own choice)
 *   MESSAGE   a result or a failure, with the choices that keep the person safe (a failed save never exits)
 * Inputs settle at every transition: a button held while a screen changes counts only after it is released, so a confirmation never
 * reaches the game and a launch from HOME never presses anything in it (the H1 lesson: ignore what is held when input starts). */
#ifndef _WIN32
#define _DEFAULT_SOURCE   /* timegm, localtime_r */
#endif
#include "gp.h"
#include <errno.h>
#include "gp_emu.h"
#include "host.h"
#include "../../console-simulator/core/ccs_sim.h"
#include "../../console-simulator/core/home.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdarg.h>
#include <time.h>

#define HOLD_MS 3000
#define HOLD_SHOW_MS 300
#define SETUP_HOLD_MS 3000   /* a button that does nothing on a controller not set up yet, held this long: its setup opens */
#define NOTE_MS 9000         /* how long the note about it stays */
#define COMBO_GRACE_MS 100   /* SELECT or START alone reaches the game this late: time for the other to join (see gate_input) */
typedef struct { int down, sent; uint64_t at, until; } ss_gate;   /* one player's SELECT or START on its way to the game */
typedef struct { char id[200], title[200], version[200], rom[1024], header[40], sha[65], why[400]; int available; } gp_game;
typedef struct { char mode[8], saves[1024], fonts[1024], scratch[1024], result[1024], settings[1024], display[1024], pack[1024], state[1024], direct[200], gset[1024], music[1100], music_pref[1100], embed[300], select[200];
                 char leave_label[96], leave_sub[200], leave_title[96], leave_text[400]; int studio_pid; gp_game *g; int ng; } gp_cfg;   /* leave_*: home.leave (a console session) */
enum { ST_HOME, ST_START, ST_PLAY, ST_MENU, ST_SLOTS, ST_CONFIRM, ST_MESSAGE, ST_PADS, ST_LEARN, ST_HOMEMENU, ST_QUIT, ST_PICK, ST_GSET, ST_HSET, ST_GDEF, ST_WAIT, ST_NAME };
enum { A_NONE, A_LAST, A_TITLE, A_LOADSLOTS, A_BACK, A_KEEP, A_EXIT_SAVED, A_EXIT_NOSAVE, A_SAVESLOTS, A_PADS, A_FULLSCREEN, A_SLOT1, A_SLOT4 = A_SLOT1 + 3,
       A_YES, A_NO, A_RETRY, A_OK, A_SWAP, A_LEARN, A_CLOSE_HOME, A_KEEP_BROWSING,
       A_CART_PLAY, A_CART_ASIDE, A_CART_CLOSE, A_CART_PROMPT, A_CART_RETRY, A_EXIT_ANYWAY, A_START_CHOICE, A_GAME_MENU, A_PICK1, A_PICK2, A_PICK_NONE, A_PICK_AUTO, A_PICK_DEV /* + the controller (0..4) */, A_PICK_END = A_PICK_DEV + 5,
       A_LEGACY, A_RETRY_SLOT, A_GSET, A_GSET_SAVE, A_GSET_RESET, A_GSET_RESET_YES, A_GSET_CANCEL, A_GSET_PAGE, A_GSET_FIELD /* + the field (0..15) */,
       A_GSET_FIELD_END = A_GSET_FIELD + 16, A_HSET, A_HSET_PADS, A_HSET_DEFAULTS, A_HSET_GAME, A_GDEF_SAVE, A_GDEF_BUILTIN, A_GDEF_CANCEL, A_HSET_MUSIC,
       A_NAMESLOTS, A_NAME_SAVED, A_LEAVE_YES };   /* numbered as before: tests read the chosen action numbers */
/* the words for where a session runs and how it is left: the computer (the Studio's player) and the browser share them; the NES Classic
   (CCS_CONSOLE, host_dev.c) has no keyboard, no window and no Studio, and what leaving its HOME does (the RAM test ends; an installed HOME
   switches to Nintendo's) is named by its session file (home.leave) */
#if defined(CCS_CONSOLE)
#define TX_HERE "this console"
#define TX_WINDOW 0              /* no Full Screen / Window item */
#define TX_PAD_SETUP 0           /* the console's controllers are known controllers: no setup */
#define TX_NO_PAD "Now: no controller"
#define TX_PICK_NONE "No Controller"
#define TX_HOME_MENU "B closes this menu."
#define TX_SETTINGS_PADS "Controllers\xE2\x80\xA6"
#define TX_SETTINGS_PADS_SUB "Who plays Players 1 and 2"
#define TX_RAPID_KEYS "Works with each player's controller."
#define TX_PIC_DEFAULT "this console's picture setting"
#define TX_KEYS ""
#define TX_NAME_ESC ""
#define TX_LEAVE "Leave the HOME"
#define TX_LEAVE_SUB ""
#define TX_LEAVE_SET_SUB ""
#else
#define TX_HERE "this computer"
#define TX_WINDOW 1
#define TX_PAD_SETUP 1
#define TX_NO_PAD "Now: the keyboard"
#define TX_PICK_NONE "The Keyboard Only"
#define TX_HOME_MENU "B or Esc closes this menu."
#define TX_SETTINGS_PADS "Controllers & Keyboard\xE2\x80\xA6"
#define TX_SETTINGS_PADS_SUB "Who plays Players 1 and 2, controller setup"
#define TX_RAPID_KEYS "Works with your controller or keyboard."
#define TX_PIC_DEFAULT "Play on PC Settings in Classic Cart Studio"
#define TX_KEYS " Keys are set in Play on PC Settings in Classic Cart Studio."
#define TX_NAME_ESC "  \xC2\xB7  Esc: cancel"
#define TX_LEAVE "Close and Return to Studio"
#define TX_LEAVE_SUB ""
#define TX_LEAVE_SET_SUB "Closes this HOME; Classic Cart Studio stays open"
#endif
typedef struct { char label[96], sub[160]; int act, enabled; } item;
typedef struct { char title[96], text[600], hint[160]; item it[12]; int n, focus; } menu;

static struct {
    gp_cfg c; gp_settings s; host *H; framebuffer fb; font f_lab, f_title, f_item, f_sub; int fonts_ok; home *hm;
    int st, prev_st, slot_mode; menu m; gp_game *cur; gp_library L; int lib_open; gp_rom rom; int game_on;
    gp_pad pad, prevpad; uint8_t mask[GP_PLAYERS][GA_COUNT]; input_state hin;
    uint8_t gin[GP_PLAYERS][GA_COUNT], gprev[GP_PLAYERS][GA_COUNT]; ss_gate ssg[GP_PLAYERS][2];   /* what the game or HOME gets */
    uint64_t hold_t0; int holding, hold_frozen; uint64_t last_sram; uint32_t sram_crc; int focus_modal; uint64_t au_frames; int au_peak, au_n;
    char result_games[8192]; size_t rg; int pending_slot, pending_load; char msg_ret; int confirm_act; int msg_back_st;
    int learn_action, learn_uid, learn_map[GA_COUNT], learn_uids[8], learn_n; gp_learn learn[8]; char learn_hint[160]; int audio_ok, audio_rate; double rate; uint64_t t_next; char err[400];
    int cart_refused, cart_failed, cart_fail_shown, exit_pending; char cart_why[300], cart_fail_why[300];   /* the cartridge save: see sram_flush */
    int mrx, mrw, mrh, mry[12], mpx, mpy, mpw, mph;   /* the menu as last drawn (stage pixels): its items and its panel, for the mouse */
    int pick_player, pads_from_settings, settle_next;   /* the chooser's player; the HOME's Settings opened Controllers; who plays changed: settle */
    int slots_from_start, saved_slot;   /* the slot list was opened from the start choices; the slot this session last saved to (0 none) */
    int su_uid, su_code, pads_from_start, noted[8], n_noted; uint64_t su_t0, note_until;   /* the setup hold (su_uid -1: none) and its note */
    gp_defaults gdef; gp_prefs gpref; gp_effective eff; int prefs_bad;   /* the running game's preferences, resolved at its start */
    struct { int held, n; } rf[GP_PLAYERS][2];                            /* rapid fire: A/B held since n frames (0: released) */
    gp_game *gs_game; int gs_from, gs_page; gp_prefs gs_draft; gp_defaults gd_draft;   /* Game Settings: its game, where it opened, the page, the edits */
} P;
/* ws12: waiting for storage (the browser), naming a slot, who owns a menu's highlight (F3) */
enum { W_NONE, W_SLOT, W_GSET, W_GDEF, W_EXIT, W_NAME, W_PAD };
static struct {
    int wait, warg; char wwhy[300];                                       /* ST_WAIT: what is being written (W_*), and for which slot/action */
    int name_slot, name_ret, name_ret_mode, name_caps, name_focus, name_len; char name_buf[GP_NAME_MAX + 1];   /* ST_NAME */
    int kx[64], ky[64], kw[64], kh[64], nk;                                /* the naming keyboard's keys as last drawn (stage pixels) */
    int kb_owner, pt_seen, pt_x, pt_y, ref_x, ref_y;                       /* F3: the controller/keyboard owns the highlight until the mouse really moves */
    int declined[8], ndeclined;                                            /* controllers whose setup was canceled: back to the 3-second hold */
    gp_prefs gs_orig; gp_defaults gd_orig;                                 /* Game Settings / Gameplay Defaults as they were saved: unsaved changes show */
} X;
static struct { int popin, quit, studio, reload; } XA;   /* what waits for the running game to end (embedded): back into the Studio, close, the Studio closing, a new HOME */
typedef char pt_matches_hp[(PT_MOVE == HP_MOVE && PT_CLICK == HP_CLICK && PT_DOUBLE == HP_DOUBLE && PT_BACK == HP_BACK && PT_WHEEL == HP_WHEEL) ? 1 : -1];

/* ---------------- config ---------------- */
static const char *js(const jval *o, const char *k) { const jval *v = json_get(o, k); return v && v->t == J_STR ? v->s : ""; }
static int load_cfg(const char *path, char *why, size_t n) {
    uint8_t *b; size_t len; if (gp_read_file(path, &b, &len, 32u << 20)) { snprintf(why, n, "the session file cannot be read"); return -1; }
    char err[160]; jval *j = json_parse((const char *)b, len, err, sizeof err); free(b);
    if (!j || j->t != J_OBJ || strcmp(js(j, "schema"), "ccs-play-session/1")) { json_free(j); snprintf(why, n, "the session file is not valid (%s)", err); return -1; }
    snprintf(P.c.mode, sizeof P.c.mode, "%s", js(j, "mode")); snprintf(P.c.saves, sizeof P.c.saves, "%s", js(j, "saves_root")); snprintf(P.c.fonts, sizeof P.c.fonts, "%s", js(j, "fonts_dir"));
    snprintf(P.c.scratch, sizeof P.c.scratch, "%s", js(j, "scratch_dir")); snprintf(P.c.result, sizeof P.c.result, "%s", js(j, "result_path"));
    snprintf(P.c.settings, sizeof P.c.settings, "%s", js(j, "settings_path")); snprintf(P.c.display, sizeof P.c.display, "%s", js(j, "display_path"));
    snprintf(P.c.direct, sizeof P.c.direct, "%s", js(j, "direct_id")); const jval *sp = json_get(j, "studio_pid"); P.c.studio_pid = sp && sp->t == J_NUM ? (int)sp->num : 0;
    snprintf(P.c.gset, sizeof P.c.gset, "%s", js(j, "game_settings_dir"));
    { const jval *em = json_get(j, "embed"); if (em && em->t == J_OBJ) snprintf(P.c.embed, sizeof P.c.embed, "%s", js(em, "pipe")); }   /* the Studio's Preview (D-104) */
    const jval *h = json_get(j, "home"); if (h && h->t == J_OBJ) { snprintf(P.c.pack, sizeof P.c.pack, "%s", js(h, "pack")); snprintf(P.c.state, sizeof P.c.state, "%s", js(h, "state_dir"));
        snprintf(P.c.select, sizeof P.c.select, "%s", js(h, "select"));   /* the game highlighted in the Studio's Preview, kept when the HOME opens */
        const jval *mu = json_get(h, "music"); if (mu && mu->t == J_OBJ) { snprintf(P.c.music, sizeof P.c.music, "%s", js(mu, "asset")); snprintf(P.c.music_pref, sizeof P.c.music_pref, "%s", js(mu, "pref")); }
        const jval *lv = json_get(h, "leave"); if (lv && lv->t == J_OBJ) { snprintf(P.c.leave_label, sizeof P.c.leave_label, "%s", js(lv, "label")); snprintf(P.c.leave_sub, sizeof P.c.leave_sub, "%s", js(lv, "sub"));
            snprintf(P.c.leave_title, sizeof P.c.leave_title, "%s", js(lv, "title")); snprintf(P.c.leave_text, sizeof P.c.leave_text, "%s", js(lv, "text")); } }   /* what leaving this HOME does, in its own words (a console session) */
    const jval *g = json_get(j, "games");
    if (!g || g->t != J_ARR || !g->n || g->n > 20000) { json_free(j); snprintf(why, n, "the session lists no games"); return -1; }
    P.c.g = calloc(g->n, sizeof *P.c.g); P.c.ng = (int)g->n;
    for (size_t i = 0; i < g->n; i++) { const jval *x = g->items[i]; gp_game *e = &P.c.g[i]; if (x->t != J_OBJ) continue;
        snprintf(e->id, sizeof e->id, "%s", js(x, "id")); snprintf(e->title, sizeof e->title, "%s", js(x, "title")); snprintf(e->version, sizeof e->version, "%s", js(x, "version_label"));
        snprintf(e->rom, sizeof e->rom, "%s", js(x, "rom")); snprintf(e->header, sizeof e->header, "%s", js(x, "header_hex")); snprintf(e->sha, sizeof e->sha, "%s", js(x, "content_sha256"));
        snprintf(e->why, sizeof e->why, "%s", js(x, "unavailable")); e->available = !e->why[0] && e->rom[0] && strlen(e->sha) == 64; }
    json_free(j);
    if (!P.c.saves[0]) { snprintf(why, n, "the session names no folder for saved games"); return -1; }
    if (strcmp(P.c.mode, "direct") && strcmp(P.c.mode, "home")) { snprintf(why, n, "unknown mode"); return -1; }
    return 0; }
static gp_game *game_by_id(const char *id) { for (int i = 0; i < P.c.ng; i++) if (!strcmp(P.c.g[i].id, id)) return &P.c.g[i]; return NULL; }

/* ---------------- result (what the Studio reads after the player closes) ---------------- */
static void result_add(const char *fmt, ...) { va_list a; va_start(a, fmt); if (P.rg < sizeof P.result_games - 2) { int k = vsnprintf(P.result_games + P.rg, sizeof P.result_games - P.rg, fmt, a); if (k > 0) P.rg += (size_t)k; } va_end(a); }
static void write_result(const char *exit_kind) {
    if (!P.c.result[0]) return; char buf[9000], e[500]; json_escape(P.err, e, sizeof e);
    int n = snprintf(buf, sizeof buf, "{\"schema\":\"ccs-play-result/1\",\"mode\":\"%s\",\"exit\":\"%s\",\"error\":%s,\"events\":[%s]}\n", P.c.mode, exit_kind, e, P.result_games);
    int f = gp_fail_writes; gp_fail_writes = 0; gp_write_atomic(P.c.result, buf, (size_t)(n > 0 ? n : 0)); gp_fail_writes = f; }   /* the test switch fails saves, not this report */

/* ---------------- drawing ---------------- */
static int fonts_load(void) {
    char p[1200], err[160]; int ok = 1;
    const char *n[4] = { "silk17.ccsf", "sans700_30.ccsf", "sans600_22.ccsf", "sans400_17.ccsf" }; font *f[4] = { &P.f_lab, &P.f_title, &P.f_item, &P.f_sub };
    for (int i = 0; i < 4; i++) { gp_path(p, sizeof p, P.c.fonts, n[i]); if (font_load(f[i], p, err, sizeof err)) ok = 0;
        if (ccs_scale == 2) { char n2[64]; snprintf(n2, sizeof n2, "%.*s-2x.ccsf", (int)(strlen(n[i]) - 5), n[i]); gp_path(p, sizeof p, P.c.fonts, n2);   /* the same face at twice the size */
            font *hf = calloc(1, sizeof *hf); if (hf && !font_load(hf, p, err, sizeof err)) f[i]->hi = hf; else free(hf); } }
    return ok; }
static void txt(const font *f, int x, int y, const char *s, uint32_t rgb, int a) {
    rect clip = { 0, 0, CCS_FB_W, CCS_FB_H };   /* stage pixels: the primitives scale to the canvas */
    if (P.fonts_ok) text_draw(&P.fb, NULL, clip, f, x, y, s, strlen(s), RGBA(rgb, a), 0, 0);
    else fb_text(&P.fb, x * ccs_scale, (y - 14) * ccs_scale, 2 * ccs_scale, s, (uint8_t)(rgb >> 16), (uint8_t)(rgb >> 8), (uint8_t)rgb); }
static void txt_fit(const font *f, int x, int y, int maxw, const char *s, uint32_t rgb, int a) {
    char o[300]; if (P.fonts_ok) { text_fit(f, s, maxw, 0, o, sizeof o); txt(f, x, y, o, rgb, a); } else txt(f, x, y, s, rgb, a); }
/* the game paused behind a menu: its last frame, aspect-correct and nearest-scaled into the canvas, dimmed */
static void draw_game_frame(int dim) {
    int w, h; const uint16_t *px = emu_frame(&w, &h); double par = P.eff.tv_aspect ? 8.0 / 7.0 : 1.0;
    int dh = P.fb.h, dw = (int)(w * par * dh / h + 0.5); if (dw > P.fb.w) { dw = P.fb.w; dh = (int)(h * dw / (w * par) + 0.5); }
    int ox = (P.fb.w - dw) / 2, oy = (P.fb.h - dh) / 2; memset(P.fb.rgb, 0, (size_t)P.fb.w * P.fb.h * 3);
    for (int y = 0; y < dh; y++) { const uint16_t *row = px + (y * h / dh) * 256; uint8_t *o = P.fb.rgb + ((size_t)(oy + y) * P.fb.w + ox) * 3;
        for (int x = 0; x < dw; x++, o += 3) { uint16_t v = row[x * w / dw]; int r = (v >> 11) << 3, g = ((v >> 5) & 63) << 2, b = (v & 31) << 3;
            o[0] = (uint8_t)(r * (256 - dim) >> 8); o[1] = (uint8_t)(g * (256 - dim) >> 8); o[2] = (uint8_t)(b * (256 - dim) >> 8); } } }
/* a popup stands out from the picture behind it (owner, 2026-11-07): that picture is dimmed and blurred (a box blur, horizontal then
   vertical, edges repeated; integer only, so every host draws the same pixels) and kept while the same screen stays open: the blur is
   made once per screen (go() starts a new one), not every frame */
static uint8_t *g_back; static int g_back_st = -1, g_back_w, g_back_h;
static void blur_rgb(uint8_t *p, int w, int h, int r) {
    size_t rs = (size_t)w * 3; int n = 2 * r + 1; uint8_t *t = malloc(rs * (size_t)h); int *acc = malloc(rs * sizeof *acc);
    if (!t || !acc) { free(t); free(acc); return; }
    for (int y = 0; y < h; y++) { const uint8_t *s = p + rs * y; uint8_t *d = t + rs * y;
        for (int c = 0; c < 3; c++) { int a = 0; for (int i = -r; i <= r; i++) a += s[(i < 0 ? 0 : i >= w ? w - 1 : i) * 3 + c];
            for (int x = 0; x < w; x++) { d[x * 3 + c] = (uint8_t)(a / n); int xa = x + r + 1 > w - 1 ? w - 1 : x + r + 1, xs = x - r < 0 ? 0 : x - r; a += s[xa * 3 + c] - s[xs * 3 + c]; } } }
    for (size_t i = 0; i < rs; i++) { int a = 0; for (int k = -r; k <= r; k++) a += t[rs * (size_t)(k < 0 ? 0 : k >= h ? h - 1 : k) + i]; acc[i] = a; }
    for (int y = 0; y < h; y++) { uint8_t *d = p + rs * y; const uint8_t *add = t + rs * (size_t)(y + r + 1 > h - 1 ? h - 1 : y + r + 1), *sub = t + rs * (size_t)(y - r < 0 ? 0 : y - r);
        for (size_t i = 0; i < rs; i++) { d[i] = (uint8_t)(acc[i] / n); acc[i] += add[i] - sub[i]; } }
    free(t); free(acc); }
static void draw_back(void) {
    size_t sz = (size_t)P.fb.w * P.fb.h * 3;
    if (g_back && g_back_st == P.st && g_back_w == P.fb.w && g_back_h == P.fb.h) { memcpy(P.fb.rgb, g_back, sz); return; }
    if (P.game_on) draw_game_frame(150);
    else if (P.hm) { home_render(P.hm, &P.fb, host_now(P.H)); uint8_t *o = P.fb.rgb; for (size_t i = 0; i < sz; i++) o[i] = (uint8_t)(o[i] * 106 >> 8); }
    else { memset(P.fb.rgb, 0, sz); return; }
    blur_rgb(P.fb.rgb, P.fb.w, P.fb.h, P.fb.w >= 2000 ? 12 : 6);
    if (!g_back || g_back_w != P.fb.w || g_back_h != P.fb.h) { free(g_back); g_back = malloc(sz); }
    if (g_back) { memcpy(g_back, P.fb.rgb, sz); g_back_st = P.st; g_back_w = P.fb.w; g_back_h = P.fb.h; } }
static int b_acts(void) { return P.st == ST_MENU || P.st == ST_START || P.st == ST_SLOTS || P.st == ST_PADS || P.st == ST_PICK || P.st == ST_CONFIRM || (P.st == ST_MESSAGE && P.m.n == 1) || P.st == ST_HOMEMENU || P.st == ST_GSET || P.st == ST_HSET || P.st == ST_GDEF; }   /* as menu_input */
static int setup_drawn(uint64_t now);
static void draw_menu(void) {
    menu *m = &P.m; draw_back(); rect clip = { 0, 0, CCS_FB_W, CCS_FB_H };
    int W = 660, ih = 58, th = 0; char lines[8][200]; int nl = 0;
    if (m->text[0] && P.fonts_ok) { span sp[8]; size_t k = text_wrap(&P.f_sub, m->text, W - 72, 0, sp, 8); for (size_t i = 0; i < k && nl < 8; i++) { size_t L = sp[i].len < 199 ? sp[i].len : 199; memcpy(lines[nl], m->text + sp[i].start, L); lines[nl][L] = 0; nl++; } th = nl * 26 + 12; }
    else if (m->text[0]) { snprintf(lines[0], sizeof lines[0], "%.199s", m->text); nl = 1; th = 38; }
    if (112 + th + m->n * ih + 64 > CCS_FB_H - 16) { ih = (CCS_FB_H - 16 - 112 - th - 64) / (m->n ? m->n : 1); if (ih < 46) ih = 46; }
    int H = 112 + th + m->n * ih + 64, x = (CCS_FB_W - W) / 2, y = (CCS_FB_H - H) / 2; P.mpx = x; P.mpy = y; P.mpw = W; P.mph = H; P.mrx = x + 22; P.mrw = W - 44; P.mrh = ih - 8;
    gfx_shadow(&P.fb, clip, x, y + 8, W, H, 18, RGBA(0x000000, 170));
    gfx_rrect(&P.fb, clip, x, y, W, H, 18, RGBA(0x15131F, 246)); gfx_rrect_ring(&P.fb, clip, x, y, W, H, 18, 3, RGBA(0xE4000F, 255));   /* a popup's red border (owner, 2026-11-07) */
    gfx_fill(&P.fb, clip, x + 32, y + 30, 40, 4, RGBA(0xE4000F, 255));
    txt(&P.f_lab, x + 32, y + 62, m->title, 0xFFFFFF, 255);
    int cy = y + 92; for (int i = 0; i < nl; i++) { txt(&P.f_sub, x + 36, cy + 20, lines[i], 0xC9C5D8, 255); cy += 26; } if (nl) cy += 12;
    for (int i = 0; i < m->n; i++) { item *it = &m->it[i]; int fy = cy + i * ih, foc = i == m->focus; P.mry[i] = fy;
        if (foc) { gfx_rrect(&P.fb, clip, x + 22, fy, W - 44, ih - 8, 12, RGBA(0x2A2340, 255)); gfx_rrect_ring(&P.fb, clip, x + 22, fy, W - 44, ih - 8, 12, 3, RGBA(0xE4000F, 255)); }
        uint32_t col = it->enabled ? 0xFFFFFF : 0x7D788F;
        int cmp = ih < 58 ? (58 - ih) / 2 : 0; txt_fit(&P.f_item, x + 44, fy + (it->sub[0] ? 25 - cmp / 2 : 33 - cmp), W - 100, it->label, col, 255);
        if (it->sub[0]) txt_fit(&P.f_sub, x + 44, fy + 44 - cmp, W - 100, it->sub, it->enabled ? 0xB9B6C8 : 0x6E6A80, 255); }
    { uint64_t now = host_now(P.H); int su = setup_drawn(now) && P.st != ST_LEARN && P.st != ST_MESSAGE && (P.su_uid >= 0 || P.st != ST_START);   /* a controller not set up: the note (the start choices say it in their text), or the hold's progress */
      txt_fit(&P.f_sub, x + 36, y + H - 26, W - 72, su ? (P.su_uid >= 0 ? "Keep holding to set up this controller" : "A controller is not set up yet: press one of its buttons to set it up")
              : m->hint[0] ? m->hint : b_acts() ? "D-pad: choose  ·  A: select  ·  B: back" : "D-pad: choose  ·  A: select", su ? 0xFFFFFF : 0x9A96AC, 255); }
    host_present_canvas(P.H, &P.fb); }
static void hold_bar(int ms) {   /* holding SELECT+START: the game waits (the HOME keeps running); a bar shows how long is left */
    rect clip = { 0, 0, CCS_FB_W, CCS_FB_H }; int W = 520, x = (CCS_FB_W - W) / 2, y = CCS_FB_H - 130;
    gfx_rrect(&P.fb, clip, x, y, W, 92, 14, RGBA(0x15131F, 230)); txt(&P.f_item, x + 24, y + 36, "Keep holding SELECT + START for the menu", 0xFFFFFF, 255);
    gfx_rrect(&P.fb, clip, x + 24, y + 56, W - 48, 12, 6, RGBA(0x3A3550, 255)); int fw = (W - 48) * (ms > HOLD_MS ? HOLD_MS : ms) / HOLD_MS; if (fw > 12) gfx_rrect(&P.fb, clip, x + 24, y + 56, fw, 12, 6, RGBA(0xE4000F, 255)); }
static void draw_hold(int ms) { draw_game_frame(0); hold_bar(ms); host_present_canvas(P.H, &P.fb); }
static int setup_drawn(uint64_t now) { return P.su_uid >= 0 ? now - P.su_t0 >= HOLD_SHOW_MS : now < P.note_until; }
static void setup_bar(uint64_t now) {   /* a controller that is not set up: how to set it up, or how long to keep holding */
    if (!setup_drawn(now) || !P.fonts_ok) return; rect clip = { 0, 0, CCS_FB_W, CCS_FB_H }; int W = 860, x = (CCS_FB_W - W) / 2, y = 28, holding = P.su_uid >= 0;
    gfx_rrect(&P.fb, clip, x, y, W, holding ? 92 : 64, 14, RGBA(0x15131F, 232)); gfx_rrect_ring(&P.fb, clip, x, y, W, holding ? 92 : 64, 14, 2, RGBA(0x3A3550, 255));
    txt_fit(&P.f_sub, x + 24, y + 38, W - 48, holding ? "Keep holding to set up this controller" : "This controller is not set up yet: press one of its buttons to set it up.", 0xFFFFFF, 255);
    if (holding) { int ms = (int)(now - P.su_t0); gfx_rrect(&P.fb, clip, x + 24, y + 58, W - 48, 12, 6, RGBA(0x3A3550, 255)); int fw = (W - 48) * (ms > SETUP_HOLD_MS ? SETUP_HOLD_MS : ms) / SETUP_HOLD_MS;
        if (fw > 12) gfx_rrect(&P.fb, clip, x + 24, y + 58, fw, 12, 6, RGBA(0xE4000F, 255)); } }

/* ---------------- menus ---------------- */
static int g_dirty = 1;
static void kb_take(void) { X.kb_owner = 1; X.ref_x = X.pt_x; X.ref_y = X.pt_y; }   /* the highlight is the controller's/keyboard's from here (F3) */
static void m_begin(const char *title, const char *text) { g_dirty = 1; kb_take(); memset(&P.m, 0, sizeof P.m); snprintf(P.m.title, sizeof P.m.title, "%s", title); snprintf(P.m.text, sizeof P.m.text, "%s", text ? text : "");
    if (P.H) { char a[200], b[1300]; json_escape(P.m.title, a, sizeof a); json_escape(P.m.text, b, sizeof b); host_trace(P.H, "{\"ev\":\"menu\",\"title\":%s,\"text\":%s}", a, b); } }   /* tests read what each screen says */
static void m_add(const char *label, const char *sub, int act, int enabled) { if (P.m.n >= 12) return; item *i = &P.m.it[P.m.n++]; snprintf(i->label, sizeof i->label, "%s", label); snprintf(i->sub, sizeof i->sub, "%s", sub ? sub : ""); i->act = act; i->enabled = enabled; }
static void m_focus(int act) { for (int i = 0; i < P.m.n; i++) if (P.m.it[i].act == act) { P.m.focus = i; return; } }
static void settle(void) { memcpy(P.mask, P.pad.down, sizeof P.mask); memset(P.ssg, 0, sizeof P.ssg); memset(P.rf, 0, sizeof P.rf); }   /* everything held now counts only after release (rapid fire starts over) */
static void home_cb(void *ctx, button b, in_kind k, uint64_t clock);
static void go(int st) { g_dirty = 1; g_back_st = -1;   /* a new screen: its own blurred picture behind */
    if (P.st == ST_HOME && st != ST_HOME && P.hm) { input_focus(&P.hin, 0, home_cb, NULL); input_focus(&P.hin, 1, home_cb, NULL); }   /* the HOME stops listening: a button pressed there (the A that opened Settings) is released there, or the HOME would take its next press for the same one */
    P.prev_st = P.st; P.st = st; settle(); P.holding = P.hold_frozen = P.focus_modal = 0; host_trace(P.H, "{\"ev\":\"state\",\"to\":%d}", st);
    if (P.H && host_embedded(P.H)) { char l[64]; snprintf(l, sizeof l, "state %d %d", st, P.game_on); host_send(P.H, l); } }   /* the Preview's toolbar knows whether a game is open */
/* a save's time as people read it (F4, D-90): the date with its year, the time with seconds and the time zone as an offset from UTC, in
   this device's local time ("Nov 6, 2026, 4:38:16 AM UTC-6"); stored in UTC as the moment the save was written. gp_local_time (gp_util.c)
   does the conversion: the browser's own time zone in the browser build */
static void when(const char *iso, char *out, size_t n) { if (gp_local_time(iso, out, n)) snprintf(out, n, "at an unknown time"); }
static int home_mode(void) { return !strcmp(P.c.mode, "home"); }

/* Slot 2 or Slot 2 · Before the castle; and Saved Nov 6, 2026, 4:38:16 AM UTC-6 (a save without a recorded time says so: F4) */
static void slot_label(int s, char *out, size_t n) { char nm[GP_NAME_MAX * 4 + 1]; gp_slot_name(&P.L, s, nm, sizeof nm); if (nm[0]) snprintf(out, n, "Slot %d \xC2\xB7 %s", s, nm); else snprintf(out, n, "Slot %d", s); }
static void slot_named(int s, char *out, size_t n) { char nm[GP_NAME_MAX * 4 + 1]; gp_slot_name(&P.L, s, nm, sizeof nm); if (nm[0]) snprintf(out, n, "Slot %d (\xE2\x80\x9C%s\xE2\x80\x9D)", s, nm); else snprintf(out, n, "Slot %d", s); }
static void saved_at(const gp_state_info *i, char *out, size_t n) { char w[64]; if (gp_local_time(i->created, w, sizeof w)) snprintf(out, n, "saved time not recorded"); else snprintf(out, n, "saved %s", w); }
static void show_message(const char *title, const char *text, int back_st) { m_begin(title, text); m_add("OK", "", A_OK, 1); P.msg_back_st = back_st; go(ST_MESSAGE); }
static const char *slot_n(int k) { static char n[GP_SLOTS][8]; snprintf(n[k - 1], 8, "slot%d", k); return n[k - 1]; }
static int slots_valid(void) { int v = 0; for (int k = 1; k <= GP_SLOTS; k++) { gp_state_info i; gp_state_info_read(&P.L, slot_n(k), &i); v += i.st == GS_OK; } return v; }
/* every launch, before any game frame: the person chooses. Nothing here writes a save; Start New Game boots the game as switched on */
static void open_start(void) {
    gp_last lu; gp_last_read(&P.L, &lu); int valid = slots_valid(), any = gp_any_slot(&P.L); gp_state_info li; gp_state_info_read(&P.L, GP_LEGACY, &li);
    char sub[200], w[48];
    { int ids[8], n = host_pad_list(P.H, ids, 8), un = 0; for (int i = 0; i < n; i++) un |= !host_pad_configured(P.H, ids[i]);
      char lt[300] = ""; if (lu.st == LU_OK) { char nm[120], at[96]; slot_named(lu.slot, nm, sizeof nm); saved_at(&lu.info, at, sizeof at); snprintf(lt, sizeof lt, " Last used: %s, %s.", nm, at); }
      char tx[600]; snprintf(tx, sizeof tx, "Choose how to start. Start New Game keeps every save as it is.%s%s", lt, un ? " A controller is not set up yet: press one of its buttons to set it up." : "");
      m_begin(P.cur->title, tx); }
    if (lu.st == LU_OK) { char lb[120], at[96]; slot_label(lu.slot, lb, sizeof lb); saved_at(&lu.info, at, sizeof at); snprintf(sub, sizeof sub, "%s \xC2\xB7 %s", lb, at); }
    else if (lu.st == LU_NONE) snprintf(sub, sizeof sub, "%s", valid ? "Not known for these saves: use Load a Save Slot" : "No save used yet");
    else if (lu.st == LU_DAMAGED) snprintf(sub, sizeof sub, "Not known: %s", lu.why);
    else snprintf(sub, sizeof sub, "Slot %d cannot be used: %s", lu.slot, lu.why);
    m_add("Load Last Used Save", sub, A_LAST, lu.st == LU_OK);
    if (valid) snprintf(sub, sizeof sub, "%d of %d slots saved", valid, GP_SLOTS); else snprintf(sub, sizeof sub, "%s", any ? "No slot can be loaded on " TX_HERE : "No slot saved yet");
    m_add("Load a Save Slot\xE2\x80\xA6", sub, A_LOADSLOTS, valid > 0);
    m_add("Start New Game", "From the title screen \xC2\xB7 every save is kept", A_TITLE, 1);
    if (li.st != GS_EMPTY) { when(li.created, w, sizeof w); if (li.st == GS_OK) snprintf(sub, sizeof sub, "From %s \xC2\xB7 copy it into a slot you choose", w); else snprintf(sub, sizeof sub, "Cannot be used: %s (the file is kept)", li.why);
        m_add("Recover an Earlier Automatic Save\xE2\x80\xA6", sub, A_LEGACY, li.st == GS_OK); }
    m_add("Game Settings\xE2\x80\xA6", "Rapid fire, controls and picture for this game, before it starts", A_GSET, 1);   /* F8: the same record and screens as in the game menu */
    m_add(home_mode() ? "Back to HOME" : "Close Game", "Nothing is changed", A_BACK, 1);
    m_focus(lu.st == LU_OK ? A_LAST : A_TITLE); go(ST_START);
    host_trace(P.H, "{\"ev\":\"start_menu\",\"last\":%d,\"last_slot\":%d,\"valid\":%d,\"legacy\":%d}", (int)lu.st, lu.slot, valid, (int)li.st); }
static void open_menu(void) {
    host_audio_pause(P.H, 1); int valid = slots_valid();
    m_begin("Game Menu", "The game is paused. Nothing is saved unless you save to a slot.");
    m_add("Keep Playing", "", A_KEEP, 1);
    m_add("Save to a Slot\xE2\x80\xA6", "Choose one of four slots, then keep playing or leave", A_SAVESLOTS, 1);
    m_add("Load a Save Slot\xE2\x80\xA6", valid ? "" : "No slot saved yet", A_LOADSLOTS, valid > 0);
    m_add(home_mode() ? "Return to HOME Without Saving" : "Exit Without Saving", "No slot is written (the game's own save is kept)", A_EXIT_NOSAVE, 1);
    m_add("Game Settings\xE2\x80\xA6", "Rapid fire, controls and picture for this game", A_GSET, 1);
    m_add("Controllers\xE2\x80\xA6", "Players 1 and 2, controller setup", A_PADS, 1);
    if (TX_WINDOW) m_add(host_fullscreen(P.H) ? "Window" : "Full Screen", "Alt+Enter also switches", A_FULLSCREEN, 1);
    m_focus(A_KEEP); go(ST_MENU); host_trace(P.H, "{\"ev\":\"menu_open\",\"frames\":%llu}", (unsigned long long)emu_frames()); }
/* mode 0 load, 1 save, 2 copy the earlier automatic save into a slot */
static void open_slots(int mode) {
    if (P.st == ST_START) P.slots_from_start = 1; else if (P.st == ST_MENU) P.slots_from_start = 0;
    gp_last lu; gp_last_read(&P.L, &lu); gp_state_info li; char lw[48] = ""; if (mode == 2) { gp_state_info_read(&P.L, GP_LEGACY, &li); when(li.created, lw, sizeof lw); }
    P.slot_mode = mode; char t[300];
    if (mode == 3) snprintf(t, sizeof t, "Choose the slot to name. A name only labels the slot: its save and the time it was saved stay as they are.");
    else if (mode == 1) snprintf(t, sizeof t, "Choose a slot for this moment. Saving over a slot asks first; the other slots are not changed.");
    else if (mode == 2) snprintf(t, sizeof t, "The earlier automatic save from %s is copied into the slot you choose; the earlier file itself is kept. Copying over a slot asks first.", lw);
    else snprintf(t, sizeof t, "%s", P.slots_from_start ? "Choose the slot to load." : "Loading replaces the game's current moment with the slot's.");
    m_begin(mode == 1 ? "Save to a Slot" : mode == 2 ? "Copy the Earlier Save to a Slot" : mode == 3 ? "Name a Slot" : "Load a Save Slot", t);
    for (int s = 1; s <= GP_SLOTS; s++) { gp_state_info i; char lab[120], sub[160], at[96]; gp_state_info_read(&P.L, slot_n(s), &i); saved_at(&i, at, sizeof at); at[0] = (char)(at[0] - 32); slot_label(s, lab, sizeof lab);
        if (i.st == GS_EMPTY) snprintf(sub, sizeof sub, "Empty"); else if (i.st == GS_OK) snprintf(sub, sizeof sub, "%s%s", at, lu.st == LU_OK && lu.slot == s ? " \xC2\xB7 last used" : "");
        else snprintf(sub, sizeof sub, "%s: %s", i.st == GS_INCOMPATIBLE ? "Cannot be used" : "Damaged", i.why);
        m_add(lab, sub, A_SLOT1 + s - 1, mode ? 1 : i.st == GS_OK); }
    if (mode == 0 || mode == 1) m_add("Name a Slot\xE2\x80\xA6", "Optional: a name you choose, shown with the slot number", A_NAMESLOTS, 1);
    m_add("Back", "", A_BACK, 1); m_focus(A_SLOT1); if (!mode) for (int s = 1; s <= GP_SLOTS; s++) if (P.m.it[s - 1].enabled) { m_focus(A_SLOT1 + s - 1); break; }
    go(ST_SLOTS);
    { gp_state_info i[GP_SLOTS]; for (int s = 0; s < GP_SLOTS; s++) gp_state_info_read(&P.L, slot_n(s + 1), &i[s]);
      host_trace(P.H, "{\"ev\":\"slots\",\"mode\":\"%s\",\"status\":[%d,%d,%d,%d]}", mode == 1 ? "save" : mode == 2 ? "legacy" : mode == 3 ? "name" : "load", i[0].st, i[1].st, i[2].st, i[3].st); } }
static void open_focus_paused(void) {   /* the game window lost focus while the game ran: it waits until the person chooses */
    host_audio_pause(P.H, 1); host_trace(P.H, "{\"ev\":\"focus_pause\",\"frames\":%llu}", (unsigned long long)emu_frames());
    m_begin("Game Paused", "The game window is no longer in front, so the game paused. Choose Keep Playing to go on.");
    m_add("Keep Playing", "", A_KEEP, 1); m_add("Game Menu…", "Save, exit, slots, controllers", A_GAME_MENU, 1); m_focus(A_KEEP); go(ST_MESSAGE); P.msg_back_st = -1; P.focus_modal = 1; }
static const char *leave_label(void) { return P.c.leave_label[0] ? P.c.leave_label : TX_LEAVE; }
static void open_home_menu(void) {   /* the 3-second SELECT + START or Esc on a HOME: everything a controller needs there, closing included */
    m_begin("HOME Menu", TX_HOME_MENU); m_add("Keep Browsing", "", A_KEEP_BROWSING, 1); m_add("Controllers…", "Players 1 and 2, controller setup", A_PADS, 1);
    if (TX_WINDOW) m_add(host_fullscreen(P.H) ? "Window" : "Full Screen", "Alt+Enter also switches", A_FULLSCREEN, 1); m_add(leave_label(), P.c.leave_label[0] ? P.c.leave_sub : TX_LEAVE_SUB, A_CLOSE_HOME, 1); go(ST_HOMEMENU); }
#ifdef __wasm__   /* the browser host sets up only controllers without the standard layout (host_web.c), as Windows sets up only the ones that are not Xbox-compatible */
#define PAD_SETUP_FOR "For controllers this browser does not already know"
#define PAD_NO_SETUP "Controllers this browser already knows need no setup."
#else
#define PAD_SETUP_FOR "For controllers that are not Xbox-compatible"
#define PAD_NO_SETUP "Xbox-compatible controllers need no setup."
#endif
static void open_pads(void) {
    char t[600]; host_pads(P.H, t, sizeof t); m_begin("Controllers", t); int n = host_pad_count(P.H);
    if (n) for (int p = 0; p < GP_PLAYERS; p++) {   /* who plays each player, and the way to choose (every connected controller) */
        char sub[160] = TX_NO_PAD, l[128]; for (int i = 0; i < n; i++) { int who; host_pad_info(P.H, i, l, sizeof l, &who); if (who == p) { snprintf(sub, sizeof sub, "Now: the %s", l); break; } }
        m_add(p ? "Choose Player 2's Controller\xE2\x80\xA6" : "Choose Player 1's Controller\xE2\x80\xA6", sub, p ? A_PICK2 : A_PICK1, 1); }
    m_add("Swap Players 1 and 2", "The two first controllers trade places", A_SWAP, 1);
    if (TX_PAD_SETUP) m_add("Set Up a Controller…", PAD_SETUP_FOR, A_LEARN, 1);
    m_add("Back", "", A_BACK, 1); m_focus(A_BACK); go(ST_PADS); }

static void home_cb(void *ctx, button b, in_kind k, uint64_t clock);
#define PICK_MAX 5   /* controllers listed in the chooser (the menu fits the screen) */
static void act(int a);
static void wait_begin(int kind, int arg, const char *title);
static int io_async(void);
static void open_pick(int p) {   /* every connected controller; a button on one highlights it here and its A chooses it */
    char ti[64], t[300]; P.pick_player = p; snprintf(ti, sizeof ti, "Player %d's Controller", p + 1);
    snprintf(t, sizeof t, "Press a button on a controller to find it in this list. A on that controller chooses it for Player %d.", p + 1);
    m_begin(ti, t); int n = host_pad_count(P.H), cur = -1;
    for (int i = 0; i < n && i < PICK_MAX; i++) { char l[128]; int who; host_pad_info(P.H, i, l, sizeof l, &who);
        m_add(l, who == 0 ? "Plays Player 1" : who == 1 ? "Plays Player 2" : "Not playing", A_PICK_DEV + i, 1); if (who == p) cur = i; }
    m_add(TX_PICK_NONE, "No controller plays this player", A_PICK_NONE, 1);
    m_add("Connection Order", "The first two controllers, in the order they were connected", A_PICK_AUTO, 1);
    m_add("Back", "", A_BACK, 1); if (cur >= 0) m_focus(A_PICK_DEV + cur); else m_focus(A_BACK); go(ST_PICK); }
static void pick_by_controller(void) {   /* in the chooser: the controller whose button went down is highlighted; its A chooses it */
    int a, d = host_pad_pressed(P.H, &a); if (d < 0 || d >= PICK_MAX) return;
    host_trace(P.H, "{\"ev\":\"pick_press\",\"device\":%d,\"action\":%d}", d, a);
    if (a == GA_A) act(A_PICK_DEV + d); else { m_focus(A_PICK_DEV + d); g_dirty = 1; } }
/* ---------------- Game Settings, HOME Settings, Gameplay Defaults ----------------
   One model (gp.h): a record per exact game and the defaults. Every way in edits the same record: the game menu (the running game), the
   HOME's Game Details (that game), HOME Settings > Selected Game Settings (the game highlighted when Settings opened). Edits are a draft
   until Save Settings; Cancel or B drops them. A running game uses them when Keep Playing resumes it; a game started later reads them. */
enum { GS_FROM_GAME, GS_FROM_DETAILS, GS_FROM_HSET, GS_FROM_START };
enum { GP_TOP, GP_RAPID, GP_CTRL, GP_PIC };
static const char *onoff(int v) { return v ? "On" : "Off"; }
static const char *SPEED_LABEL[3] = { "Slow", "Medium", "Fast" };
static void gs_current(gp_defaults *d, gp_effective *e) {   /* the defaults on disk and what the draft resolves to */
    gp_defaults_load(P.c.gset, d); gp_resolve(d, &P.gs_draft, &P.s, e); }
static int gs_changed(void) { return memcmp(&P.gs_draft, &X.gs_orig, sizeof P.gs_draft) != 0; }
static void field_sub(char *out, size_t n, int v, const char *on, const char *off, int eff) {
    if (v == GP_UNSET) snprintf(out, n, "Use Default (%s)", eff ? on : off); else snprintf(out, n, "%s", v ? on : off); }
static void open_gset(void);
static void open_hset(void);
static void open_start(void);
static void gs_back_to_caller(void) {   /* where Game Settings was opened from */
    if (P.gs_from == GS_FROM_START && P.game_on) { open_start(); m_focus(A_GSET); }   /* F8: back to the start choices; nothing starts by itself */
    else if (P.gs_from == GS_FROM_GAME && P.game_on) open_menu(); else if (P.gs_from == GS_FROM_HSET) open_hset(); else go(ST_HOME); }
static void open_gset(void) {
    gp_game *g = P.gs_game; if (!g) { gs_back_to_caller(); return; }
    int keep = P.st == ST_GSET ? P.m.focus : 0; gp_defaults d; gp_effective e; gs_current(&d, &e); char t[600], sub[200];
    if (P.gs_page == GP_TOP) {
        snprintf(t, sizeof t, "%s%s%s. %sChanges are used only after Save Settings; Discard Changes keeps the settings saved before. Use Default follows the Gameplay Defaults in HOME Settings (rapid fire) and " TX_PIC_DEFAULT " (picture).",
                 g->title, g->version[0] ? " \xC2\xB7 " : "", g->version, gs_changed() ? "You have changes that are not saved yet. " : "");
        m_begin("Game Settings", t);
        snprintf(sub, sizeof sub, "Player 1: A %s, B %s \xC2\xB7 Player 2: A %s, B %s \xC2\xB7 %s", onoff(e.rapid[0][0]), onoff(e.rapid[0][1]), onoff(e.rapid[1][0]), onoff(e.rapid[1][1]), SPEED_LABEL[e.speed]);
        m_add("Rapid Fire (Turbo)\xE2\x80\xA6", sub, A_GSET_FIELD + 0, 1);
        field_sub(sub, sizeof sub, P.gs_draft.swap_ab, "On", "Off", e.swap_ab); { char s2[200]; snprintf(s2, sizeof s2, "Swap A and B: %s", sub); m_add("Controls\xE2\x80\xA6", s2, A_GSET_FIELD + 1, 1); }
        snprintf(sub, sizeof sub, "%s \xC2\xB7 whole-number scaling %s", e.tv_aspect ? "TV proportions" : "Square pixels", e.integer_scale ? "on" : "off"); m_add("Picture\xE2\x80\xA6", sub, A_GSET_FIELD + 2, 1);
        m_add("Save Settings", P.gs_from == GS_FROM_GAME ? "Keep these choices for this game; used when it goes on" : "Keep these choices for this game; used when it starts", A_GSET_SAVE, 1);
        m_add("Reset Settings for This Game", "Every choice back to Use Default; saves are kept", A_GSET_RESET, 1);
        m_add(gs_changed() ? "Discard Changes" : "Back", gs_changed() ? "Go back with the settings saved before" : "Nothing was changed", A_GSET_CANCEL, 1);
    } else if (P.gs_page == GP_RAPID) {
        m_begin("Rapid Fire (Turbo)", "Hold A or B and it is pressed again and again for you. Speed is how often the press repeats; how many shots that gives depends on the game. " TX_RAPID_KEYS " A changes a choice; Left and Right too.");
        static const char *LAB[4] = { "Player 1 \xC2\xB7 Rapid Fire A", "Player 1 \xC2\xB7 Rapid Fire B", "Player 2 \xC2\xB7 Rapid Fire A", "Player 2 \xC2\xB7 Rapid Fire B" };
        for (int i = 0; i < 4; i++) { field_sub(sub, sizeof sub, P.gs_draft.rapid[i / 2][i % 2], "On", "Off", e.rapid[i / 2][i % 2]); m_add(LAB[i], sub, A_GSET_FIELD + 3 + i, 1); }
        if (P.gs_draft.speed == GP_UNSET) snprintf(sub, sizeof sub, "Use Default (%s)", SPEED_LABEL[e.speed]); else snprintf(sub, sizeof sub, "%s", SPEED_LABEL[P.gs_draft.speed]);
        m_add("Speed", sub, A_GSET_FIELD + 7, 1); m_add("Back to Game Settings", "Your changes wait there for Save Settings", A_GSET_FIELD + 15, 1);
    } else if (P.gs_page == GP_CTRL) {
        m_begin("Controls", "Swap A and B changes the two buttons in this game only; menus and the HOME keep them as they are." TX_KEYS);
        field_sub(sub, sizeof sub, P.gs_draft.swap_ab, "On", "Off", e.swap_ab); m_add("Swap A and B", sub, A_GSET_FIELD + 8, 1);
        m_add("Back to Game Settings", "Your change waits there for Save Settings", A_GSET_FIELD + 15, 1);
    } else {
        m_begin("Picture", "How this game's picture is shaped. Use Default follows " TX_PIC_DEFAULT ".");
        field_sub(sub, sizeof sub, P.gs_draft.tv_aspect, "TV proportions", "Square pixels", e.tv_aspect); m_add("Proportions", sub, A_GSET_FIELD + 9, 1);
        field_sub(sub, sizeof sub, P.gs_draft.integer_scale, "On", "Off", e.integer_scale); m_add("Whole-Number Scaling", sub, A_GSET_FIELD + 10, 1);
        m_add("Back to Game Settings", "Your changes wait there for Save Settings", A_GSET_FIELD + 15, 1);
    }
    if (keep < P.m.n) P.m.focus = keep; snprintf(P.m.hint, sizeof P.m.hint, "D-pad: choose  \xC2\xB7  A: select or change  \xC2\xB7  B: %s", P.gs_page != GP_TOP ? "back (not saved yet)" : gs_changed() ? "discard changes" : "back");
    if (P.st != ST_GSET) go(ST_GSET); else g_dirty = 1;
    char id[400]; json_escape(g->id, id, sizeof id); host_trace(P.H, "{\"ev\":\"game_settings\",\"id\":%s,\"page\":%d,\"from\":%d}", id, P.gs_page, P.gs_from); }
static void gset_begin(gp_game *g, int from) {   /* a fresh draft from the game's record on disk */
    P.gs_game = g; P.gs_from = from; P.gs_page = GP_TOP; gp_prefs_none(&P.gs_draft);
    if (g && gp_prefs_load(P.c.gset, g->sha, &P.gs_draft)) { gp_prefs_none(&P.gs_draft); host_trace(P.H, "{\"ev\":\"game_prefs_unreadable\"}"); }
    X.gs_orig = P.gs_draft;
    if (P.st == ST_GSET) P.st = ST_MENU;   /* (re)entered: the focus starts at the top */
    open_gset(); }
static int cyc3(int v, int dir) { int o[3] = { GP_UNSET, 1, 0 }, k = v == GP_UNSET ? 0 : v ? 1 : 2; k = (k + (dir < 0 ? 2 : 1)) % 3; return o[k]; }   /* Use Default -> On -> Off */
static int cyc_speed(int v, int dir) { int o[4] = { GP_UNSET, GP_SPEED_SLOW, GP_SPEED_MEDIUM, GP_SPEED_FAST }, k = v == GP_UNSET ? 0 : v + 1; k = (k + (dir < 0 ? 3 : 1)) % 4; return o[k]; }
static void gset_field(int f, int dir) {   /* A or Left/Right on a field */
    int keep = P.m.focus;
    if (f >= 0 && f <= 2) { P.gs_page = f == 0 ? GP_RAPID : f == 1 ? GP_CTRL : GP_PIC; P.m.focus = 0; P.st = ST_MENU; open_gset(); return; }
    if (f == 15) { int back = P.gs_page == GP_RAPID ? 0 : P.gs_page == GP_CTRL ? 1 : 2; P.gs_page = GP_TOP; P.st = ST_MENU; open_gset(); P.m.focus = back; return; }
    if (f >= 3 && f <= 6) P.gs_draft.rapid[(f - 3) / 2][(f - 3) % 2] = cyc3(P.gs_draft.rapid[(f - 3) / 2][(f - 3) % 2], dir);
    else if (f == 7) P.gs_draft.speed = cyc_speed(P.gs_draft.speed, dir);
    else if (f == 8) P.gs_draft.swap_ab = cyc3(P.gs_draft.swap_ab, dir);
    else if (f == 9) P.gs_draft.tv_aspect = cyc3(P.gs_draft.tv_aspect, dir);
    else if (f == 10) P.gs_draft.integer_scale = cyc3(P.gs_draft.integer_scale, dir);
    open_gset(); P.m.focus = keep; host_trace(P.H, "{\"ev\":\"game_setting_changed\",\"field\":%d}", f); }
static void apply_prefs(void) {   /* the running game: resolve again and show the picture as chosen */
    if (!P.cur) return; if (gp_defaults_load(P.c.gset, &P.gdef)) gp_defaults_builtin(&P.gdef);
    P.prefs_bad = gp_prefs_load(P.c.gset, P.cur->sha, &P.gpref) != 0; if (P.prefs_bad) gp_prefs_none(&P.gpref);
    gp_resolve(&P.gdef, &P.gpref, &P.s, &P.eff); host_set_picture(P.H, P.eff.tv_aspect, P.eff.integer_scale); memset(P.rf, 0, sizeof P.rf);
    host_trace(P.H, "{\"ev\":\"game_prefs\",\"rapid\":[[%d,%d],[%d,%d]],\"speed\":%d,\"swap_ab\":%d,\"tv_aspect\":%d,\"integer\":%d,\"unreadable\":%d}",
               P.eff.rapid[0][0], P.eff.rapid[0][1], P.eff.rapid[1][0], P.eff.rapid[1][1], P.eff.speed, P.eff.swap_ab, P.eff.tv_aspect, P.eff.integer_scale, P.prefs_bad); }
/* ---------------- HOME Music (D-81) ----------------
 * The owner-selected song plays while the HOME is in front: the whole song, then again from its beginning. It pauses at once while
 * a game starts or runs (the start choices included), while the window is not in front and when the HOME closes, and goes on from
 * where it was. The choice is this computer's (home-music.json beside the Studio's data; absent: On; unreadable: Off until chosen
 * again). The file is PCM, 16-bit stereo at 48,000 Hz, read in pieces into the same sound device the games use, never both at once. */
static struct { FILE *f; long off; uint32_t frames, pos, loops; int avail, on, playing, focused, source, session, seq, noout, threaded; } MU = { .focused = 1 };   /* threaded: the host's thread reads the song (K3D1-F2) */   /* noout: no sound output opened (said once) */
static const char *MU_SRC[3] = { "default", "saved", "unreadable" };
static void mu_trace(const char *ev) { host_trace(P.H, "{\"ev\":\"%s\",\"on\":%s,\"pos\":%u,\"frames\":%u,\"loops\":%u,\"source\":\"%s\"}", ev, MU.on ? "true" : "false", MU.pos, MU.frames, MU.loops, MU_SRC[MU.source]);
    if (P.H && host_embedded(P.H)) { char l[96]; snprintf(l, sizeof l, "music %s %s %s %s", ev, MU.on ? "on" : "off", MU_SRC[MU.source], MU.focused ? "front" : "away"); host_send(P.H, l); } }   /* the Preview says what really plays (F12) */
static void mu_pref_load(void) {
    MU.on = 1; MU.source = 0; if (!P.c.music_pref[0]) return; uint8_t *b; size_t len; FILE *t = fopen(P.c.music_pref, "rb"); if (!t) { MU.source = errno == ENOENT ? 0 : 2; MU.on = MU.source == 0; return; } fclose(t);
    if (gp_read_file(P.c.music_pref, &b, &len, 4096)) { MU.on = 0; MU.source = 2; return; }
    char err[80]; jval *j = json_parse((const char *)b, len, err, sizeof err); free(b); const char *v = j && j->t == J_OBJ && !strcmp(js(j, "schema"), "ccs-home-music/1") ? js(j, "music") : "";
    if (!strcmp(v, "on") || !strcmp(v, "off")) { MU.on = !strcmp(v, "on"); MU.source = 1; } else { MU.on = 0; MU.source = 2; } json_free(j); }
static int mu_pref_save(void) { char b[80]; int n = snprintf(b, sizeof b, "{\"schema\":\"ccs-home-music/1\",\"music\":\"%s\"}", MU.on ? "on" : "off"); return P.c.music_pref[0] ? gp_write_atomic(P.c.music_pref, b, (size_t)n) : -1; }
static void mu_open(void) {   /* the WAV header: PCM, 2 channels, 48,000 Hz, 16 bits; then where its samples start */
    if (!home_mode() || !P.c.music[0]) return; mu_pref_load(); FILE *f = fopen(P.c.music, "rb"); uint8_t h[12], c[8], fm[16]; int fmt = 0;
    if (!f || fread(h, 1, 12, f) != 12 || memcmp(h, "RIFF", 4) || memcmp(h + 8, "WAVE", 4)) { if (f) fclose(f); host_trace(P.H, "{\"ev\":\"music_unavailable\",\"why\":\"the HOME Music file cannot be read\"}"); return; }
    for (;;) { if (fread(c, 1, 8, f) != 8) break; uint32_t sz = (uint32_t)c[4] | (uint32_t)c[5] << 8 | (uint32_t)c[6] << 16 | (uint32_t)c[7] << 24;
        if (!memcmp(c, "fmt ", 4) && sz >= 16 && fread(fm, 1, 16, f) == 16) { fmt = (fm[0] | fm[1] << 8) == 1 && (fm[2] | fm[3] << 8) == 2 && ((uint32_t)fm[4] | (uint32_t)fm[5] << 8 | (uint32_t)fm[6] << 16) == 48000 && (fm[14] | fm[15] << 8) == 16; if (sz > 16) fseek(f, (long)(sz - 16 + (sz & 1)), SEEK_CUR); }
        else if (!memcmp(c, "data", 4)) { if (fmt && sz >= 4) { MU.f = f; MU.off = ftell(f); MU.frames = sz / 4; MU.avail = 1; } break; }
        else fseek(f, (long)(sz + (sz & 1)), SEEK_CUR); }
    if (!MU.avail) { fclose(f); host_trace(P.H, "{\"ev\":\"music_unavailable\",\"why\":\"the HOME Music file is not 16-bit stereo PCM at 48000 Hz\"}"); return; }
    mu_trace("music_ready"); }
static int audio_at(int rate) {   /* the sound device at this rate (the HOME Music and a game use the same device, one after the other) */
    if (P.audio_ok == 1 && P.audio_rate != rate) { host_audio_close(P.H); P.audio_ok = 0; }
    if (P.audio_ok == 0) { P.audio_ok = host_audio_open(P.H, rate) == 0 ? 1 : -1; P.audio_rate = rate; } return P.audio_ok == 1; }
static void mu_offer(void) { if (P.hm && MU.avail) { int was = home_music(P.hm, NULL, 0); home_music_offer(P.hm, MU.on); MU.seq = home_music_seq(P.hm); if (was >= 0 && was != MU.on) home_emit_state(P.hm); } }   /* the HOME's badge shows this window's choice; a changed one is reported (the Preview reads the HOME's state from it) */
static int mu_wanted(void) { return MU.avail && MU.on && MU.focused && home_mode() && !P.game_on && P.st != ST_START && P.st != ST_PLAY && P.st != ST_QUIT; }
static int home_covers(void) { int cb, ahead; host_home_pace(&cb, &ahead); return cb; }   /* covers decoded per pass of the HOME's loop, by host */
/* the song's next frames from where it is, from its beginning again after its end (whole song, then loop); 0 when the file cannot be read.
   Run by the session's thread, or, handed over (MU.threaded), by the host's sound thread under its lock: the session touches the file and the
   position again only after taking it back (gp_host_audio_source(NULL) returns once no fill runs) */
static uint32_t mu_fill(void *ctx, int16_t *b, uint32_t frames) { (void)ctx;
    uint32_t k = MU.frames - MU.pos; if (k > frames) k = frames;
    if (!MU.avail || fread(b, 4, k, MU.f) != k) { MU.avail = 0; host_trace(P.H, "{\"ev\":\"music_unavailable\",\"why\":\"the HOME Music file could not be read\"}"); return 0; }
    MU.pos += k; if (MU.pos >= MU.frames) { MU.pos = 0; MU.loops++; fseek(MU.f, MU.off, SEEK_SET); mu_trace("music_loop"); }   /* the whole song played: from its beginning */
    return k; }
static void mu_take_back(void) { if (MU.threaded) { gp_host_audio_source(P.H, NULL, NULL, 0); MU.threaded = 0; } }
static void mu_tick(void) {
    if (!mu_wanted()) { if (MU.playing) { mu_take_back(); size_t q = host_audio_queued(P.H); host_audio_pause(P.H, 1); MU.pos = (uint32_t)((MU.pos + MU.frames - q % MU.frames) % MU.frames); MU.playing = 0; mu_trace("music_pause"); } return; }   /* silent at once; it goes on from what was heard */
    int cb, ahead; host_home_pace(&cb, &ahead); (void)cb;
    if (!MU.playing) { if (!audio_at(48000)) { if (!MU.noout) { MU.noout = 1; mu_trace("music_no_output"); } return; } MU.noout = 0; fseek(MU.f, MU.off + (long)MU.pos * 4, SEEK_SET); host_audio_pause(P.H, 0); MU.playing = 1; mu_trace("music_play");
        if (gp_host_audio_source && !gp_host_audio_source(P.H, mu_fill, NULL, (size_t)ahead)) MU.threaded = 1; }   /* K3D1-F2: the host's thread reads the song from here on */
    if (MU.threaded) { if (!MU.avail) { mu_take_back(); host_audio_pause(P.H, 1); MU.playing = 0; } return; }   /* (a read failure there: silent, as below) */
    for (int guard = 0; guard < 32 && host_audio_queued(P.H) < (size_t)ahead; guard++) {   /* the host's lead: about 130 ms on the PC, 600 ms on the console */
        int16_t b[1024 * 2]; uint32_t k = mu_fill(NULL, b, 1024);
        if (!k) { host_audio_pause(P.H, 1); MU.playing = 0; return; }
        host_audio_push(P.H, b, k); } }

static void open_hset(void) {   /* the HOME's own Settings on the computer */
    char sub[260]; m_begin("HOME Settings", "Controllers, HOME Music, the defaults for every game, and the settings of the game you highlighted.");
    m_add(TX_SETTINGS_PADS, TX_SETTINGS_PADS_SUB, A_HSET_PADS, 1);
    m_add("Gameplay Defaults\xE2\x80\xA6", "Rapid fire for every game without its own choice", A_HSET_DEFAULTS, 1);
    if (P.gs_game) snprintf(sub, sizeof sub, "%s", P.gs_game->title); else snprintf(sub, sizeof sub, "Highlight a game on the HOME first, then open Settings");
    m_add("Selected Game Settings\xE2\x80\xA6", sub, A_HSET_GAME, P.gs_game != NULL);
    m_add(MU.on ? "HOME Music: On" : "HOME Music: Off", !MU.avail ? "Not part of this installation" : MU.source == 2 ? "The saved choice could not be read; choose On or Off" : "The HOME's theme music; games keep their own sound", A_HSET_MUSIC, MU.avail);
    m_add("Back to HOME", "", A_KEEP_BROWSING, 1); m_add(leave_label(), P.c.leave_label[0] ? P.c.leave_sub : TX_LEAVE_SET_SUB, A_CLOSE_HOME, 1); m_focus(A_HSET_PADS); go(ST_HSET); }
static void mu_pref_reload(void) {   /* in front again: the saved choice may have changed meanwhile (the Preview's toggle saves the same file) */
    if (!MU.avail || MU.session) return; int on = MU.on, src = MU.source; mu_pref_load(); if (MU.on == on && MU.source == src) return;
    mu_trace("music_pref_changed"); mu_offer(); if (P.st == ST_HSET) { int fa = P.m.it[P.m.focus].act; open_hset(); m_focus(fa); } }   /* its Settings and the badge show it at once */
static void mu_badge(void) {   /* a click on the HOME's music badge: the HOME toggled it; saved here, as the Settings item saves it */
    if (!P.hm || !MU.avail) return; int sq = home_music_seq(P.hm); if (sq == MU.seq) return; MU.seq = sq; int on = home_music(P.hm, NULL, 0) == 1; if (on == MU.on) return;
    MU.on = on; MU.source = 1; int r = mu_pref_save(); MU.session = r != 0; host_trace(P.H, "{\"ev\":\"home_music\",\"on\":%s,\"saved\":%s,\"via\":\"badge\"}", MU.on ? "true" : "false", r ? "false" : "true");
    if (P.st == ST_HSET) { int fa = P.m.it[P.m.focus].act; open_hset(); m_focus(fa); } }
static void open_gdef(void) {
    char sub[80]; int keep = P.st == ST_GDEF ? P.m.focus : 0;
    m_begin("Gameplay Defaults", "These defaults apply to all games unless you\xE2\x80\x99ve set rapid fire separately for that game. To customize one game, open its Game Details and choose Game Settings.");
    static const char *LAB[4] = { "Player 1 \xC2\xB7 Rapid Fire A", "Player 1 \xC2\xB7 Rapid Fire B", "Player 2 \xC2\xB7 Rapid Fire A", "Player 2 \xC2\xB7 Rapid Fire B" };
    for (int i = 0; i < 4; i++) m_add(LAB[i], onoff(P.gd_draft.rapid[i / 2][i % 2]), A_GSET_FIELD + 3 + i, 1);
    snprintf(sub, sizeof sub, "%s", SPEED_LABEL[P.gd_draft.speed]); m_add("Rapid Fire Speed", sub, A_GSET_FIELD + 7, 1);
    int ch = memcmp(&P.gd_draft, &X.gd_orig, sizeof P.gd_draft) != 0;
    m_add("Save Defaults", "Keep these defaults; games with their own choices keep them", A_GDEF_SAVE, 1);
    m_add("Restore Built-In Defaults", "Rapid fire off, Medium (Save Defaults keeps it)", A_GDEF_BUILTIN, 1);
    m_add(ch ? "Discard Changes" : "Back", ch ? "Go back with the defaults saved before" : "Nothing was changed", A_GDEF_CANCEL, 1);
    if (keep < P.m.n) P.m.focus = keep; snprintf(P.m.hint, sizeof P.m.hint, "D-pad: choose  \xC2\xB7  A: select or change  \xC2\xB7  B: %s", ch ? "discard changes" : "back");
    if (P.st != ST_GDEF) go(ST_GDEF); else g_dirty = 1; host_trace(P.H, "{\"ev\":\"gameplay_defaults\"}"); }
static void gdef_field(int f, int dir) {
    if (f >= 3 && f <= 6) P.gd_draft.rapid[(f - 3) / 2][(f - 3) % 2] = !P.gd_draft.rapid[(f - 3) / 2][(f - 3) % 2];
    else if (f == 7) P.gd_draft.speed = (P.gd_draft.speed + (dir < 0 ? 2 : 1)) % 3;
    open_gdef(); }
/* a new HOME package for the same session (the Studio's collection, loadout or HOME settings changed): the game list stays the session's */
static void on_settings(void *ctx); static void on_game_settings(void *ctx, const char *id); static void home_trace(const char *j);
static int on_launch(void *ctx, const char *id, const char *title, char *why, size_t whylen);
static void reload_home(void) { g_back_st = -1;   /* the HOME behind a popup changed */
    XA.reload = 0; if (!P.hm) return; char err[300]; const char *keep = home_focus_id(P.hm); char kid[200]; snprintf(kid, sizeof kid, "%s", keep ? keep : "");
    home *n = home_load2(P.c.pack, P.c.state[0] ? P.c.state : NULL, home_trace, err, sizeof err);
    if (!n) { char l[400], e2[340]; json_escape(err, e2, sizeof e2); snprintf(l, sizeof l, "reload-failed %s", e2); host_send(P.H, l); return; }
    home_free(P.hm); P.hm = n; home_set_launch(P.hm, on_launch, NULL); home_set_settings(P.hm, on_settings, NULL); home_set_game_settings(P.hm, on_game_settings, NULL); home_set_cover_budget(P.hm, home_covers()); mu_offer();
    for (int i = 0; i < P.c.ng && kid[0]; i++) if (!strcmp(P.c.g[i].id, kid)) { home_select(P.hm, i); break; }
    home_emit_state(P.hm); g_dirty = 1; host_send(P.H, "reloaded"); }
static void on_settings(void *ctx) {   /* the HOME's own Settings: the game highlighted now is the one Selected Game Settings edits */
    (void)ctx; const char *id = P.hm ? home_focus_id(P.hm) : ""; P.gs_game = id && id[0] ? game_by_id(id) : NULL; P.pads_from_settings = 0; open_hset(); }
static void on_game_settings(void *ctx, const char *id) { (void)ctx; gp_game *g = game_by_id(id); if (g) gset_begin(g, GS_FROM_DETAILS); }   /* Game Details > Game Settings */
/* ---------------- game start / stop ---------------- */
/* the cartridge save (cart.sav, the game's own battery save) is written only when that is safe. A file that could not be used is never
   written over: the person plays without it, sets it aside (kept under a new name) to start a new one, or closes the game. A write that
   fails is said: the first failure while playing pauses the game once, and every exit that needs the save written offers Try Again,
   Keep Playing or leaving without it, and is never reported as saved */
static int sram_flush(int force) {   /* 0 written or nothing to write; 1 not written over the unusable file; -1 failed (P.cart_fail_why) */
    size_t n; uint8_t *s = emu_sram(&n); if (!s || !n || !P.lib_open) return 0;
    uint32_t c = gp_crc32(s, n); (void)force; if (c == P.sram_crc && !P.cart_failed) return 0;   /* the file already holds this (loaded or last written): nothing to write */
    if (P.cart_refused) { if (c != P.sram_crc) { P.sram_crc = c; host_trace(P.H, "{\"ev\":\"cart_kept\",\"bytes\":%zu}", n); } return 1; }
    char why[300]; if (gp_sram_save(&P.L, s, n, why, sizeof why) == 0) { P.sram_crc = c; P.cart_failed = P.cart_fail_shown = 0; host_trace(P.H, "{\"ev\":\"cart_saved\",\"bytes\":%zu}", n); return 0; }
    P.cart_failed = 1; snprintf(P.cart_fail_why, sizeof P.cart_fail_why, "%s", why); char e[400]; json_escape(why, e, sizeof e); host_trace(P.H, "{\"ev\":\"cart_save_failed\",\"why\":%s}", e); return -1; }
static const char *cart_status(void) { size_t n; uint8_t *s = emu_sram(&n); return !s || !n ? "none" : P.cart_refused ? "kept_unusable_file" : P.cart_failed ? "not_written" : "ok"; }
static void start_choice(void) { open_start(); }   /* every launch asks, saves or not (no remembered choice skips it) */
static void open_cart_refused(void) {
    char t[600]; snprintf(t, sizeof t, "%s It is kept as it is; nothing is written over it.", P.cart_why); host_audio_pause(P.H, 1);
    m_begin("The Cartridge Save Cannot Be Used", t);
    m_add("Play Without It", "What the game saves on its cartridge this time is not kept", A_CART_PLAY, 1);
    m_add("Set It Aside and Start a New One", "The file is kept in the same folder under a new name", A_CART_ASIDE, 1);
    m_add(home_mode() ? "Back to HOME" : "Close Game", "Nothing is changed", A_CART_CLOSE, 1);
    m_focus(A_CART_PLAY); P.msg_back_st = -1; go(ST_MESSAGE); host_trace(P.H, "{\"ev\":\"cart_refused_shown\"}"); }
static void open_cart_failed(int at_exit, int saved_slot) {
    char t[600], pre[120] = ""; host_audio_pause(P.H, 1); if (saved_slot) snprintf(pre, sizeof pre, "Slot %d is saved, but the game's own cartridge save is not. ", saved_slot);
    if (at_exit) snprintf(t, sizeof t, "%s%s The cartridge save from before is unchanged.", pre, P.cart_fail_why);
    else snprintf(t, sizeof t, "%s The game is paused. What it saved this session stays in memory and is written as soon as that works.", P.cart_fail_why);
    m_begin("The Cartridge Save Was Not Written", t);
    m_add("Try Again", "", at_exit ? A_RETRY : A_CART_RETRY, 1);
    m_add("Keep Playing", "", A_KEEP, 1);
    if (at_exit) m_add(home_mode() ? "Return Without It" : "Exit Without It", "What the game saved on its cartridge this session is not kept", A_EXIT_ANYWAY, 1);
    m_focus(A_KEEP); P.msg_back_st = -1; go(ST_MESSAGE); host_trace(P.H, "{\"ev\":\"cart_failed_shown\",\"at_exit\":%d}", at_exit); }
static void stop_game(const char *how) {
    if (!P.game_on) return; char t[400]; json_escape(P.cur->id, t, sizeof t); const char *cs = cart_status();
    result_add("%s{\"id\":%s,\"ended\":\"%s\",\"cart\":\"%s\",\"slot\":%d,\"frames\":%llu}", P.rg ? "," : "", t, how, cs, P.saved_slot, (unsigned long long)emu_frames());
    host_trace(P.H, "{\"ev\":\"game_stop\",\"how\":\"%s\",\"cart\":\"%s\",\"frames\":%llu}", how, cs, (unsigned long long)emu_frames());
    emu_unload(); gp_rom_free(&P.rom); gp_lib_close(&P.L); P.lib_open = 0; P.game_on = 0; P.cur = NULL; host_audio_pause(P.H, 1); P.cart_refused = P.cart_failed = P.cart_fail_shown = 0;
    gp_prefs_none(&P.gpref); gp_resolve(&P.gdef, &P.gpref, &P.s, &P.eff); host_set_picture(P.H, P.s.tv_aspect, P.s.integer_scale);
    host_set_title(P.H, home_mode() ? "Classic Cart Studio · HOME" : "Classic Cart Studio"); }
/* back to HOME: its controller state starts clean (nothing it saw before the launch is still "held") and what is held now settles */
static void reload_home(void);
static void leave_game(void) {
    if (XA.studio || XA.quit) { go(ST_QUIT); return; }
    if (home_mode()) { input_focus(&P.hin, 0, home_cb, NULL); input_focus(&P.hin, 1, home_cb, NULL); go(ST_HOME); if (XA.popin) { XA.popin = 0; host_popout(P.H, 0); } if (XA.reload) reload_home(); } else go(ST_QUIT); }
/* 1: started (START or PLAY); 0: refused (why) */
static int start_game(gp_game *g, char *why, size_t n) {
    if (!g->available) { snprintf(why, n, "%s", g->why[0] ? g->why : "The ROM file of this game is not on " TX_HERE "."); return 0; }
    if (gp_rom_load(g->rom, g->header, g->sha, &P.rom, why, n)) return 0;
    if (gp_lib_open(&P.L, P.c.saves, g->sha, g->title, why, n)) { gp_rom_free(&P.rom); return 0; }
    P.lib_open = 1;
    if (emu_load(&P.rom, g->version[0] ? g->version : g->title, why, n)) { gp_lib_close(&P.L); P.lib_open = 0; gp_rom_free(&P.rom); return 0; }
    P.cur = g; P.game_on = 1; size_t sn; uint8_t *s = emu_sram(&sn); char sw[300]; P.cart_refused = P.cart_failed = P.cart_fail_shown = 0; P.saved_slot = 0; apply_prefs();
    if (s && sn) { int r = gp_sram_load(&P.L, s, sn, sw, sizeof sw);
        if (r < 0) { P.cart_refused = 1; snprintf(P.cart_why, sizeof P.cart_why, "%s", sw); char e[400]; json_escape(sw, e, sizeof e); host_trace(P.H, "{\"ev\":\"cart_refused\",\"why\":%s}", e); }
        if (r == 1) emu_reset(); P.sram_crc = gp_crc32(s, sn); }
    P.last_sram = host_now(P.H); char t[260]; snprintf(t, sizeof t, "%s · Classic Cart Studio", g->title); host_set_title(P.H, t);
    char tid[400]; json_escape(g->id, tid, sizeof tid); host_trace(P.H, "{\"ev\":\"game_start\",\"id\":%s,\"sram\":%zu,\"state_size\":%zu,\"slots\":%d}", tid, sn, emu_state_size(), gp_any_slot(&P.L));
    audio_at((int)emu_rate());   /* the game's rate (HOME Music may have opened the device at 48,000 Hz) */
    if (P.cart_refused) open_cart_refused(); else start_choice();
    return 1; }
static int save_state(const char *name, char *why, size_t n) {
    size_t sz = emu_state_size(); if (!sz) { snprintf(why, n, "The emulator gave no state to save."); return -1; }
    uint8_t *b = malloc(sz); if (!b) { snprintf(why, n, "Out of memory."); return -1; }
    int r; if (emu_save_state(b, sz)) { snprintf(why, n, "The emulator could not save the game."); r = -1; } else r = gp_state_write(&P.L, name, b, sz, emu_frames(), why, n);
    host_trace(P.H, "{\"ev\":\"%s\",\"name\":\"%s\",\"bytes\":%zu,\"crc\":\"%08x\",\"frames\":%llu}", r ? "save_failed" : "saved", name, sz, gp_crc32(b, sz), (unsigned long long)emu_frames()); free(b); return r; }
static int load_state(const char *name, char *why, size_t n) {
    uint8_t *b; size_t len; if (gp_state_read(&P.L, name, &b, &len, why, n)) return -1;
    int r = emu_load_state(b, len); uint32_t crc = gp_crc32(b, len); free(b); if (r) snprintf(why, n, "The emulator could not use this save."); memset(P.rf, 0, sizeof P.rf);
    host_trace(P.H, "{\"ev\":\"%s\",\"name\":\"%s\",\"crc\":\"%08x\"}", r ? "load_failed" : "loaded", name, crc); return r; }

/* HOME mode: A on a game */
/* the HOME's own events (theme, launches, refusals, its state report without the per-tile detail) reach the trace when one is kept */
static int g_layout;   /* a test asked for the layout: its rectangles reach the trace */
static void home_trace(const char *j) {
    if (!g_layout && !strncmp(j, "{\"ev\":\"layout_rect\"", 19)) return;
    if (P.H && host_embedded(P.H) && !strncmp(j, "{\"ev\":\"home\"", 12) && strlen(j) < 3900) { char l[4000]; snprintf(l, sizeof l, "home %s", j); host_send(P.H, l); }   /* theme, highlight, music: the Preview's toolbar follows */
    if (strlen(j) < 1900) { host_trace(P.H, "{\"ev\":\"home_event\",\"e\":%s}", j); return; }
    const char *x = strstr(j, "\"notice\":");
    if (x) { x += 9; x += *x == 't' ? 4 : 5; host_trace(P.H, "{\"ev\":\"home_event\",\"e\":%.*s}}", (int)(x - j), j); } }
static int on_launch(void *ctx, const char *id, const char *title, char *why, size_t whylen) {
    (void)ctx; (void)title; gp_game *g = game_by_id(id); char t[400]; json_escape(id, t, sizeof t);
    if (!g) { snprintf(why, whylen, "This game is not part of this HOME's game list."); host_trace(P.H, "{\"ev\":\"launch_refused\",\"id\":%s}", t); return 0; }
    if (!start_game(g, why, whylen)) { host_trace(P.H, "{\"ev\":\"launch_refused\",\"id\":%s}", t); return 0; }
    return 1; }

/* ---------------- generic controller setup ----------------
   A, B, SELECT, START and the four directions, on one controller; each may be a button, a D-pad (hat) direction or a stick/axis
   direction. An input counts only once it has been released (nothing held carries into the next step), an input is used once,
   nothing is saved before the last step, and only the keyboard's Esc or closing the window cancels: every controller input, B included,
   is something to learn. Xbox-compatible controllers need no setup (XInput). */
static const int LEARN_ORDER[GA_COUNT] = { GA_A, GA_B, GA_SELECT, GA_START, GA_UP, GA_DOWN, GA_LEFT, GA_RIGHT };
static const char *ACT_LABEL[GA_COUNT] = { "Up", "Down", "Left", "Right", "A", "B", "SELECT", "START" };
static void learn_draw(void) {
    char t[600], name[160] = "", done[400] = ""; size_t o = 0;
    if (P.learn_uid >= 0) { char nm[100]; host_pad_name(P.H, P.learn_uid, nm, sizeof nm); snprintf(name, sizeof name, "the %s", nm); }
    for (int k = 0; k < P.learn_action && o < sizeof done - 60; k++) { char in[48]; gp_input_name(P.learn_map[LEARN_ORDER[k]], in, sizeof in); o += (size_t)snprintf(done + o, sizeof done - o, "%s%s %s", k ? ", " : "", ACT_LABEL[LEARN_ORDER[k]], in); }
    if (P.learn_uid < 0) snprintf(t, sizeof t, "Press A on the controller you want to set up.%s%s", P.learn_hint[0] ? " " : "", P.learn_hint);
    else snprintf(t, sizeof t, "Step %d of %d on %s: press %s.%s%s%s%s", P.learn_action + 1, GA_COUNT, name[0] ? name : "this controller", ACT_LABEL[LEARN_ORDER[P.learn_action]],
                  P.learn_hint[0] ? " " : "", P.learn_hint, done[0] ? " Set so far: " : "", done);
    m_begin("Set Up a Controller", t); snprintf(P.m.hint, sizeof P.m.hint, "Esc on the keyboard cancels  ·  nothing is saved until the last step"); }
static void learn_cancel(const char *why) { host_trace(P.H, "{\"ev\":\"pad_learn_cancel\",\"step\":%d}", P.learn_action);
    if (P.learn_uid >= 0 && X.ndeclined < 8) X.declined[X.ndeclined++] = P.learn_uid;   /* not offered again by a single press: the 3-second hold still opens it */
    show_message("Controller Setup Canceled", why, ST_PADS); }
static void learn_begin(void) {
    P.learn_action = 0; P.learn_uid = -1; P.learn_hint[0] = 0; for (int a = 0; a < GA_COUNT; a++) P.learn_map[a] = -1;
    P.learn_n = host_pad_list(P.H, P.learn_uids, 8);
    for (int i = 0; i < P.learn_n; i++) { gp_raw r; memset(&r, 0, sizeof r); r.pov = -1; host_pad_raw(P.H, P.learn_uids[i], &r); gp_learn_begin(&P.learn[i], &r); }
    if (!P.learn_n) { show_message("No Controller to Set Up", "Connect the controller, then choose Set Up a Controller again. " PAD_NO_SETUP, ST_PADS); return; }
    host_trace(P.H, "{\"ev\":\"pad_learn_begin\",\"controllers\":%d}", P.learn_n); learn_draw(); go(ST_LEARN); }
static void learn_begin_for(int uid) {   /* the setup hold: this controller from the first step; the button still held counts only after release */
    learn_begin(); if (P.st != ST_LEARN) return;
    for (int i = 0; i < P.learn_n; i++) if (P.learn_uids[i] == uid) { P.learn_uid = uid; P.learn[0] = P.learn[i]; host_trace(P.H, "{\"ev\":\"pad_learn_target\",\"id\":%d}", uid); learn_draw(); return; } }
static void setup_watch(uint64_t now) {   /* controllers not set up: the note when one is first seen, and the 3-second hold of a button that does nothing */
    int ids[8], n = host_pad_list(P.H, ids, 8), map[GA_COUNT], seen = 0; gp_map_default(map);
    int watch = (P.st == ST_PLAY || P.st == ST_HOME || P.st == ST_START || P.st == ST_MENU || P.st == ST_HOMEMENU || P.st == ST_PADS) && !P.holding;
    for (int i = 0; i < n; i++) { if (host_pad_configured(P.H, ids[i])) continue;
        int k = 0; while (k < P.n_noted && P.noted[k] != ids[i]) k++;
        if (k == P.n_noted && P.n_noted < 8) { P.noted[P.n_noted++] = ids[i]; P.note_until = now + NOTE_MS; host_trace(P.H, "{\"ev\":\"pad_setup_note\",\"id\":%d}", ids[i]); }
        if (!watch || seen) continue; gp_raw r; memset(&r, 0, sizeof r); r.pov = -1; if (!host_pad_raw(P.H, ids[i], &r)) continue;
        { int dec = 0; for (int d = 0; d < X.ndeclined; d++) dec |= X.declined[d] == ids[i]; int any = -1; for (int b = 0; b < 32 && any < 0; b++) if (r.btn[b]) any = b;
          if (!dec && any >= 0) {   /* F17: one press of any button opens the setup for this controller (it counts only after its release) */
            int from = P.st; P.su_uid = -1; P.note_until = 0; host_trace(P.H, "{\"ev\":\"pad_setup_press\",\"id\":%d,\"code\":%d,\"state\":%d}", ids[i], any, from);
            if (from == ST_PLAY) open_menu(); P.pads_from_settings = 0; P.pads_from_start = from == ST_START; open_pads(); learn_begin_for(ids[i]); return; } }
        int held = -1; for (int b = 0; b < 32 && held < 0; b++) { int used = 0; for (int a = 0; a < GA_COUNT; a++) used |= map[a] == b; if (!used && r.btn[b]) held = b; }
        if (held < 0) { if (P.su_uid == ids[i]) P.su_uid = -1; continue; }
        seen = 1; if (P.su_uid != ids[i] || P.su_code != held) { P.su_uid = ids[i]; P.su_code = held; P.su_t0 = now; continue; }
        if (now - P.su_t0 < SETUP_HOLD_MS) continue;
        int from = P.st; P.su_uid = -1; P.note_until = 0; host_trace(P.H, "{\"ev\":\"pad_setup_hold\",\"id\":%d,\"code\":%d,\"state\":%d}", ids[i], held, from);
        if (from == ST_PLAY) open_menu();   /* the game waits; after the setup, Back returns to the game menu */
        P.pads_from_settings = 0; P.pads_from_start = from == ST_START; open_pads(); learn_begin_for(ids[i]); return; }
    if (!seen) P.su_uid = -1; }
static void learn_step(void) {
    int n = P.learn_uid < 0 ? P.learn_n : 1;
    for (int i = 0; i < n; i++) {
        int uid = P.learn_uid < 0 ? P.learn_uids[i] : P.learn_uid; gp_learn *l = P.learn_uid < 0 ? &P.learn[i] : &P.learn[0];
        gp_raw r; memset(&r, 0, sizeof r); r.pov = -1;
        if (!host_pad_raw(P.H, uid, &r)) { if (P.learn_uid >= 0) { learn_cancel("The controller was disconnected. Nothing was changed."); return; } continue; }
        int code = gp_learn_poll(l, &r); if (code < 0) continue;
        char in[48]; gp_input_name(code, in, sizeof in); int dup = -1;
        if (P.learn_uid >= 0 || uid == P.learn_uid) for (int k = 0; k < P.learn_action; k++) if (P.learn_map[LEARN_ORDER[k]] == code) dup = LEARN_ORDER[k];
        if (dup >= 0) { snprintf(P.learn_hint, sizeof P.learn_hint, "(%s is already %s; press another.)", in, ACT_LABEL[dup]); host_trace(P.H, "{\"ev\":\"pad_learn_duplicate\",\"code\":%d}", code); learn_draw(); return; }
        if (P.learn_uid < 0) { P.learn_uid = uid; if (i) P.learn[0] = *l; }   /* this controller from now on; what it still holds stays unarmed */
        P.learn_map[LEARN_ORDER[P.learn_action]] = code; host_trace(P.H, "{\"ev\":\"pad_learned\",\"action\":\"%s\",\"code\":%d}", gp_action_name(LEARN_ORDER[P.learn_action]), code);
        P.learn_action++; P.learn_hint[0] = 0;
        if (P.learn_action >= GA_COUNT) { int ok = host_pad_set_map(P.H, P.learn_uid, P.learn_map) == 0; host_trace(P.H, "{\"ev\":\"pad_learn_done\",\"saved\":%d}", ok);
            if (ok && io_async()) { wait_begin(W_PAD, 0, "Saving the Controller Setup\xE2\x80\xA6"); return; }
            show_message(ok ? "Controller Set Up" : "The Setup Was Not Saved", ok ? "Its buttons are saved for this controller." : "Its buttons work now but could not be saved on this computer.", ST_PADS); return; }
        learn_draw(); return; } }

/* ---------------- waiting for storage (the browser build: a write is complete only when the browser confirms it) ---------------- */
static void wait_begin(int kind, int arg, const char *title) {
    X.wait = kind; X.warg = arg; m_begin(title, "Writing it to this browser\xE2\x80\x99s storage. Keep this page open.");
    snprintf(P.m.hint, sizeof P.m.hint, "One moment"); go(ST_WAIT); host_trace(P.H, "{\"ev\":\"io_wait\",\"kind\":%d,\"arg\":%d}", kind, arg); }
static int io_async(void) { return gp_io_pending() > 0; }
static void slot_saved(int s);
static void slot_save_failed(int s, const char *why) {
    char ti[64]; snprintf(ti, sizeof ti, "Slot %d Was Not Saved", s); m_begin(ti, why); m_add("Try Again", "", A_RETRY_SLOT, 1); m_add("Choose Another Slot\xE2\x80\xA6", "", A_SAVESLOTS, 1);
    m_add("Keep Playing", "", A_KEEP, 1); m_add(home_mode() ? "Return to HOME Without Saving" : "Exit Without Saving", "No slot is written", A_EXIT_NOSAVE, 1); m_focus(A_KEEP); P.msg_back_st = -1; go(ST_MESSAGE); }
static void open_name(int s, int ret);
static void wait_done(void) {
    char why[300]; int failed = gp_io_failed(why, sizeof why); int k = X.wait, a = X.warg; X.wait = W_NONE;
    host_trace(P.H, "{\"ev\":\"io_done\",\"kind\":%d,\"failed\":%d}", k, failed);
    if (k == W_SLOT) { if (failed) { char t[400]; snprintf(t, sizeof t, "%s The previous save in this slot is unchanged.", why); P.saved_slot = 0; slot_save_failed(a, t); } else slot_saved(a); return; }
    if (k == W_GSET) { if (failed) { m_begin("Game Settings Were Not Saved", why); m_add("Try Again", "", A_GSET_SAVE, 1); m_add("Back to Game Settings", "Your changes are kept here", A_GSET_FIELD + 14, 1); m_add("Discard Changes", "", A_GSET_CANCEL, 1); m_focus(A_GSET_FIELD + 14); P.msg_back_st = -1; go(ST_MESSAGE); return; }
        gp_game *g = P.gs_game; host_trace(P.H, "{\"ev\":\"game_settings_saved\"}"); if (g && P.game_on && P.cur && !strcmp(P.cur->sha, g->sha)) apply_prefs(); gs_back_to_caller(); return; }
    if (k == W_GDEF) { if (failed) { show_message("The Defaults Were Not Saved", why, ST_HSET); return; } host_trace(P.H, "{\"ev\":\"defaults_saved\"}"); open_hset(); return; }
    if (k == W_NAME) { if (failed) { show_message("The Name Was Not Saved", why, -1); return; } if (X.name_ret == 1) slot_saved(a); else open_slots(X.name_ret_mode); return; }
    if (k == W_PAD) { show_message(failed ? "The Setup Was Not Saved" : "Controller Set Up", failed ? "Its buttons work now but could not be saved in this browser." : "Its buttons are saved for this controller.", ST_PADS); return; }
    if (k == W_EXIT) { if (failed) { P.cart_failed = 1; snprintf(P.cart_fail_why, sizeof P.cart_fail_why, "%s", why); open_cart_failed(1, P.saved_slot); return; }
        stop_game(a == A_EXIT_SAVED ? "saved_slot" : "not_saved"); leave_game(); return; } }
static void slot_saved(int s) {   /* F4: the slot, its name and the exact time it now holds */
    gp_state_info i; gp_state_info_read(&P.L, slot_n(s), &i); char t[500], ti[64], nm[120], at[96]; slot_named(s, nm, sizeof nm); saved_at(&i, at, sizeof at);
    snprintf(ti, sizeof ti, "Saved to Slot %d", s);
    snprintf(t, sizeof t, "%s now holds this moment, %s.%s", nm, at, P.pending_load == 2 ? " It could not be noted as the last used save; Load a Save Slot still finds it." : "");
    m_begin(ti, t); m_add("Keep Playing", "", A_KEEP, 1); m_add("Name This Slot\xE2\x80\xA6", "Optional: a name shown with Slot number and time", A_NAME_SAVED, 1);
    m_add(home_mode() ? "Return to HOME" : "Exit Game", "Slot saved", A_EXIT_SAVED, 1); m_focus(A_KEEP); P.msg_back_st = -1; go(ST_MESSAGE); }

/* ---------------- naming a slot (F4): an on-screen keyboard for the controller, the mouse and arrow keys; typing works too ---------------- */
static const char *NAME_ROWS[4] = { "ABCDEFGHIJ", "KLMNOPQRST", "UVWXYZ-'!?", "0123456789" };
#define NK_LETTERS 40
enum { NK_CASE = NK_LETTERS, NK_SPACE, NK_DEL, NK_DONE, NK_CANCEL, NK_COUNT };
static void open_name(int s, int ret) {
    X.name_slot = s; X.name_ret = ret; X.name_caps = 1; X.name_focus = 0; gp_slot_name(&P.L, s, X.name_buf, sizeof X.name_buf); X.name_len = (int)strlen(X.name_buf);
    char ti[64]; snprintf(ti, sizeof ti, "Name Slot %d", s); m_begin(ti, ""); go(ST_NAME); host_text_input(P.H, 1); host_trace(P.H, "{\"ev\":\"name_open\",\"slot\":%d}", s); }
static void name_close(void) { host_text_input(P.H, 0); }
static void name_key(int k) {
    if (k < NK_LETTERS) { if (X.name_len < GP_NAME_MAX) { char c = NAME_ROWS[k / 10][k % 10]; if (!X.name_caps && c >= 'A' && c <= 'Z') c = (char)(c + 32); X.name_buf[X.name_len++] = c; X.name_buf[X.name_len] = 0; } }
    else if (k == NK_CASE) X.name_caps ^= 1;
    else if (k == NK_SPACE) { if (X.name_len < GP_NAME_MAX && X.name_len) { X.name_buf[X.name_len++] = ' '; X.name_buf[X.name_len] = 0; } }
    else if (k == NK_DEL) { if (X.name_len) X.name_buf[--X.name_len] = 0; }
    else if (k == NK_CANCEL) { name_close(); host_trace(P.H, "{\"ev\":\"name_cancel\"}"); if (X.name_ret == 1) slot_saved(X.name_slot); else open_slots(X.name_ret_mode); return; }
    else if (k == NK_DONE) { while (X.name_len && X.name_buf[X.name_len - 1] == ' ') X.name_buf[--X.name_len] = 0; name_close();
        int r = gp_slot_name_set(&P.L, X.name_slot, X.name_buf); char e2[120]; json_escape(X.name_buf, e2, sizeof e2); host_trace(P.H, "{\"ev\":\"slot_named\",\"slot\":%d,\"name\":%s,\"saved\":%d}", X.name_slot, e2, r == 0);
        if (r) { show_message("The Name Was Not Saved", "It could not be written on this device. The slot and its save are unchanged.", -1); return; }
        if (io_async()) { wait_begin(W_NAME, X.name_slot, "Saving the Name\xE2\x80\xA6"); return; }
        if (X.name_ret == 1) slot_saved(X.name_slot); else open_slots(X.name_ret_mode); return; }
    g_dirty = 1; }
static void name_text(const char *t) {   /* typed characters (the host's text input): letters, digits, space and - ' ! ? . , & ( ); Backspace and Enter */
    for (; *t; t++) { unsigned char c = (unsigned char)*t;
        if (c == '\b') name_key(NK_DEL); else if (c == '\n') { name_key(NK_DONE); return; }
        else if (c >= 32 && c < 127 && (strchr(" -'!?.,&()", (int)c) || (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) && X.name_len < GP_NAME_MAX && !(c == ' ' && !X.name_len)) { X.name_buf[X.name_len++] = (char)c; X.name_buf[X.name_len] = 0; g_dirty = 1; } } }
static void name_move(int dx, int dy) {   /* the letter grid (rows 0..3) and the action row (row 4: five keys) */
    int k = X.name_focus, r = k < NK_LETTERS ? k / 10 : 4, c = k < NK_LETTERS ? k % 10 : (k - NK_LETTERS) * 2;
    r = (r + dy + 5) % 5; c = (c + dx + 10) % 10; X.name_focus = r < 4 ? r * 10 + c : NK_LETTERS + c / 2; g_dirty = 1; }
static void draw_name(void) {
    draw_back(); rect clip = { 0, 0, CCS_FB_W, CCS_FB_H }; int W = 940, H = 600, x = (CCS_FB_W - W) / 2, y = (CCS_FB_H - H) / 2;
    gfx_shadow(&P.fb, clip, x, y + 8, W, H, 18, RGBA(0x000000, 170)); gfx_rrect(&P.fb, clip, x, y, W, H, 18, RGBA(0x15131F, 246)); gfx_rrect_ring(&P.fb, clip, x, y, W, H, 18, 3, RGBA(0xE4000F, 255));   /* a popup's red border (owner, 2026-11-07) */
    gfx_fill(&P.fb, clip, x + 32, y + 30, 40, 4, RGBA(0xE4000F, 255)); txt(&P.f_lab, x + 32, y + 62, P.m.title, 0xFFFFFF, 255);
    txt_fit(&P.f_sub, x + 36, y + 92, W - 72, "A name only labels the slot: the save and its time do not change. Leave it empty for no name.", 0xC9C5D8, 255);
    gfx_rrect(&P.fb, clip, x + 32, y + 110, W - 64, 56, 10, RGBA(0x0B0A12, 255)); gfx_rrect_ring(&P.fb, clip, x + 32, y + 110, W - 64, 56, 10, 2, RGBA(0x4A4560, 255));
    char shown[GP_NAME_MAX + 4]; snprintf(shown, sizeof shown, "%s_", X.name_buf); txt(&P.f_item, x + 52, y + 147, shown, 0xFFFFFF, 255);
    char cnt[24]; snprintf(cnt, sizeof cnt, "%d / %d", X.name_len, GP_NAME_MAX); txt(&P.f_sub, x + W - 120, y + 146, cnt, 0x9A96AC, 255);
    X.nk = 0; int gx = x + 40, gy = y + 186, kw = 80, kh = 56, gap = 6;
    for (int k = 0; k < NK_COUNT; k++) { int kx, ky, w2; char lab[16];
        if (k < NK_LETTERS) { kx = gx + (k % 10) * (kw + gap); ky = gy + (k / 10) * (kh + gap); w2 = kw; char c = NAME_ROWS[k / 10][k % 10]; if (!X.name_caps && c >= 'A' && c <= 'Z') c = (char)(c + 32); snprintf(lab, sizeof lab, "%c", c); }
        else { int j = k - NK_LETTERS; w2 = kw * 2 + gap; kx = gx + j * (w2 + gap); ky = gy + 4 * (kh + gap) + 8; static const char *AL[5] = { "abc", "Space", "Delete", "Done", "Cancel" }; snprintf(lab, sizeof lab, "%s", j == 0 && !X.name_caps ? "ABC" : AL[j]); }
        int foc = k == X.name_focus; gfx_rrect(&P.fb, clip, kx, ky, w2, kh, 10, RGBA(foc ? 0x2A2340 : 0x221F2E, 255)); if (foc) gfx_rrect_ring(&P.fb, clip, kx, ky, w2, kh, 10, 3, RGBA(0xE4000F, 255));
        char fit[16]; if (P.fonts_ok) text_fit(&P.f_item, lab, w2 - 12, 0, fit, sizeof fit); else snprintf(fit, sizeof fit, "%s", lab);
        int tw = P.fonts_ok ? text_w(&P.f_item, fit, strlen(fit), 0) : 0; txt(&P.f_item, kx + (w2 - tw) / 2, ky + 37, fit, 0xFFFFFF, 255);
        X.kx[k] = kx; X.ky[k] = ky; X.kw[k] = w2; X.kh[k] = kh; X.nk = k + 1; }
    txt_fit(&P.f_sub, x + 36, y + H - 26, W - 72, "D-pad: choose  \xC2\xB7  A: type  \xC2\xB7  B: delete  \xC2\xB7  START: done  \xC2\xB7  SELECT: abc / ABC" TX_NAME_ESC, 0x9A96AC, 255);
    host_present_canvas(P.H, &P.fb); }

/* ---------------- actions ---------------- */
static void act(int a) {
    char why[400];
    switch (a) {
    case A_LAST: { gp_last lu; gp_last_read(&P.L, &lu); if (lu.st != LU_OK) { show_message("The Last Used Save Cannot Be Loaded", lu.why[0] ? lu.why : "It is not known.", ST_START); return; }
        P.pending_slot = lu.slot; P.pending_load = 1; P.slots_from_start = 1; act(A_YES); return; }
    case A_LEGACY: open_slots(2); return;
    case A_TITLE: go(ST_PLAY); host_audio_pause(P.H, 0); return;
    case A_LOADSLOTS: open_slots(0); return;
    case A_SAVESLOTS: open_slots(1); return;
    case A_KEEP: if (XA.popin || XA.quit || XA.studio) { if (XA.studio) host_send(P.H, "exit-canceled"); XA.popin = XA.quit = XA.studio = 0; } go(ST_PLAY); host_audio_pause(P.H, 0); return;   /* a close that asked first was canceled */
    case A_PADS: P.pads_from_settings = 0; P.pads_from_start = 0; open_pads(); return;
    case A_PICK1: open_pick(0); return;
    case A_PICK2: open_pick(1); return;
    case A_PICK_NONE: host_pad_choose(P.H, P.pick_player, GP_PICK_NONE); P.settle_next = 1; open_pads(); return;
    case A_PICK_AUTO: host_pad_choose(P.H, P.pick_player, GP_PICK_AUTO); P.settle_next = 1; open_pads(); return;
    case A_GAME_MENU: open_menu(); return;
    case A_FULLSCREEN: host_toggle_fullscreen(P.H); if (P.game_on) open_menu(); else open_home_menu(); return;
    case A_EXIT_SAVED:   /* leaving after a successful save to a slot: the game's own cartridge save is written as at every exit */
        P.exit_pending = A_EXIT_SAVED; if (sram_flush(1) < 0) { open_cart_failed(1, P.saved_slot); return; } if (io_async()) { wait_begin(W_EXIT, A_EXIT_SAVED, "Saving the Game\xE2\x80\x99s Own Save\xE2\x80\xA6"); return; } stop_game("saved_slot"); leave_game(); return;
    case A_RETRY: act(P.exit_pending ? P.exit_pending : A_EXIT_NOSAVE); return;
    case A_RETRY_SLOT: P.pending_load = 0; P.slot_mode = 1; act(A_YES); return;
    case A_EXIT_NOSAVE: P.exit_pending = A_EXIT_NOSAVE; if (sram_flush(1) < 0) { open_cart_failed(1, 0); return; } if (io_async()) { wait_begin(W_EXIT, A_EXIT_NOSAVE, "Saving the Game\xE2\x80\x99s Own Save\xE2\x80\xA6"); return; } stop_game("not_saved"); leave_game(); return;
    case A_EXIT_ANYWAY: stop_game(P.exit_pending == A_EXIT_SAVED ? "saved_slot_without_cartridge_save" : "not_saved_without_cartridge_save"); leave_game(); return;
    case A_CART_RETRY: if (sram_flush(1) < 0) { open_cart_failed(0, 0); return; } show_message("Cartridge Save Written", "The game's cartridge save is up to date.", ST_PLAY); return;
    case A_CART_PLAY: host_trace(P.H, "{\"ev\":\"cart_play_without\"}"); start_choice(); return;
    case A_CART_PROMPT: open_cart_refused(); return;
    case A_START_CHOICE: start_choice(); return;
    case A_CART_CLOSE: stop_game("closed_unusable_cartridge_save"); leave_game(); return;
    case A_CART_ASIDE: { char nm[160];
        if (gp_sram_set_aside(&P.L, nm, sizeof nm, why, sizeof why)) { m_begin("The File Was Not Set Aside", why); m_add("OK", "", A_CART_PROMPT, 1); P.msg_back_st = -1; go(ST_MESSAGE); return; }
        P.cart_refused = 0; char e[200]; json_escape(nm, e, sizeof e); host_trace(P.H, "{\"ev\":\"cart_set_aside\",\"name\":%s}", e);
        char t[400]; snprintf(t, sizeof t, "The unusable file is kept in this game's save folder as %s. A new cartridge save starts now.", nm);
        m_begin("Cartridge Save Set Aside", t); m_add("OK", "", A_START_CHOICE, 1); P.msg_back_st = -1; go(ST_MESSAGE); return; }
    case A_BACK:
        if (P.st == ST_PICK) { open_pads(); return; }
        if (P.st == ST_PADS && P.pads_from_settings && !P.game_on) { P.pads_from_settings = 0; open_hset(); return; }   /* opened from HOME Settings */
        if (P.st == ST_GSET) { if (P.gs_page != GP_TOP) gset_field(15, 1); else act(A_GSET_CANCEL); return; }
        if (P.st == ST_HSET) { go(ST_HOME); return; }
        if (P.st == ST_GDEF) { act(A_GDEF_CANCEL); return; }
        if (P.st == ST_START) { stop_game("closed_at_start"); leave_game(); return; }
        if (P.st == ST_SLOTS && P.slot_mode == 3) { open_slots(X.name_ret_mode); return; }   /* Name a Slot: back to the list it came from */
        if (P.st == ST_SLOTS && P.game_on && (P.slots_from_start || P.slot_mode == 2)) { open_start(); return; }
        if (P.st == ST_PADS && P.pads_from_start) { P.pads_from_start = 0; open_start(); return; }   /* the setup hold at the start choices */
        if (P.st == ST_PADS && !P.game_on && home_mode()) { open_home_menu(); return; }   /* Controllers… opened from the HOME menu */
        if (P.st == ST_SLOTS || P.st == ST_PADS) { open_menu(); return; }
        return;
    case A_SWAP: host_swap_pads(P.H); P.settle_next = 1; open_pads(); return;
    case A_LEARN: learn_begin(); return;
    case A_CLOSE_HOME:
#if defined(CCS_CONSOLE)
        {   /* the console: leaving its HOME ends the session there (the RAM test restarts the console): asked first */
            m_begin(P.c.leave_title[0] ? P.c.leave_title : "Leave the HOME?", P.c.leave_text); m_add(leave_label(), "", A_LEAVE_YES, 1); m_add("Keep Browsing", "", A_KEEP_BROWSING, 1); m_focus(A_KEEP_BROWSING); go(ST_HOMEMENU); return; }
    case A_LEAVE_YES: host_trace(P.H, "{\"ev\":\"leave_home\"}"); go(ST_QUIT); return;
#endif
        if (host_embedded(P.H)) host_send(P.H, "close-preview"); go(ST_QUIT); return;   /* embedded: the Preview closes with it */
    case A_KEEP_BROWSING: go(ST_HOME); return;
    case A_OK: if (P.msg_back_st == ST_START) open_start(); else if (P.msg_back_st == ST_MENU) open_menu(); else if (P.msg_back_st == ST_HOME) go(ST_HOME); else if (P.msg_back_st == ST_QUIT) go(ST_QUIT); else if (P.msg_back_st == ST_HSET) open_hset();
        else if (P.msg_back_st == ST_PLAY) { go(ST_PLAY); host_audio_pause(P.H, 0); } else if (P.msg_back_st == ST_PADS) open_pads(); else open_menu(); return;
    case A_YES: {
        int s = P.pending_slot; const char *n = slot_n(s); char t[400], ti[64], w[48];
        if (P.pending_load) {   /* an explicit load: only the running game's moment changes; no file is written but the last-used note */
            if (load_state(n, why, sizeof why)) { size_t L = strlen(why); snprintf(why + L, sizeof why - L, " Nothing was changed."); show_message("The Slot Was Not Loaded", why, P.slots_from_start ? ST_START : ST_MENU); return; }
            if (gp_last_write(&P.L, s, "load")) host_trace(P.H, "{\"ev\":\"last_used_not_noted\",\"slot\":%d}", s);
            go(ST_PLAY); host_audio_pause(P.H, 0); return; }
        if (P.slot_mode == 2) {   /* the earlier automatic save, copied into the chosen slot; the earlier file stays */
            if (gp_legacy_copy(&P.L, s, why, sizeof why)) { host_trace(P.H, "{\"ev\":\"legacy_copy_failed\",\"slot\":%d}", s); show_message("The Earlier Save Was Not Copied", why, ST_START); return; }
            host_trace(P.H, "{\"ev\":\"legacy_copied\",\"slot\":%d}", s); snprintf(ti, sizeof ti, "Copied to Slot %d", s);
            snprintf(t, sizeof t, "Slot %d now holds the earlier automatic save. The earlier file is kept as it was. Choose Load a Save Slot to play it.", s); show_message(ti, t, ST_START); return; }
        if (save_state(n, why, sizeof why)) { slot_save_failed(s, why); return; }   /* failed: the slot is as it was; nothing leaves */
        P.saved_slot = s; int noted = gp_last_write(&P.L, s, "save") == 0; (void)t; (void)w;
        if (!noted) host_trace(P.H, "{\"ev\":\"last_used_not_noted\",\"slot\":%d}", s);
        P.pending_load = noted ? 0 : 2;   /* slot_saved says when the last-used note could not be written */
        if (io_async()) { snprintf(ti, sizeof ti, "Saving to Slot %d\xE2\x80\xA6", s); wait_begin(W_SLOT, s, ti); return; }
        slot_saved(s); return; }
    case A_NO: if (P.st == ST_CONFIRM && P.confirm_act == A_GSET_RESET_YES) { P.confirm_act = 0; P.st = ST_MENU; open_gset(); return; } open_slots(P.slot_mode); return;
    case A_GSET: if (P.cur) gset_begin(P.cur, P.st == ST_START ? GS_FROM_START : GS_FROM_GAME); return;
    case A_NAMESLOTS: X.name_ret_mode = P.slot_mode; open_slots(3); return;
    case A_NAME_SAVED: open_name(P.pending_slot, 1); return;
    case A_HSET: open_hset(); return;
    case A_HSET_PADS: P.pads_from_settings = 1; open_pads(); return;
    case A_HSET_DEFAULTS: if (gp_defaults_load(P.c.gset, &P.gd_draft)) gp_defaults_builtin(&P.gd_draft); X.gd_orig = P.gd_draft; open_gdef(); return;
    case A_HSET_GAME: if (P.gs_game) gset_begin(P.gs_game, GS_FROM_HSET); return;
    case A_GSET_CANCEL: host_trace(P.H, "{\"ev\":\"game_settings_canceled\"}"); gs_back_to_caller(); return;
    case A_GSET_SAVE: {
        gp_game *g = P.gs_game; if (!g) { gs_back_to_caller(); return; }
        if (gp_prefs_save(P.c.gset, g->sha, g->title, g->version, &P.gs_draft)) {
            host_trace(P.H, "{\"ev\":\"game_settings_save_failed\"}"); m_begin("Game Settings Were Not Saved", "They could not be written on " TX_HERE " (the disk may be full or read-only). The earlier settings are unchanged.");
            m_add("Try Again", "", A_GSET_SAVE, 1); m_add("Back to Game Settings", "Your changes are kept here", A_GSET_FIELD + 14, 1); m_add("Discard Changes", "", A_GSET_CANCEL, 1); m_focus(A_GSET_FIELD + 14); P.msg_back_st = -1; go(ST_MESSAGE); return; }
        if (io_async()) { wait_begin(W_GSET, 0, "Saving Game Settings\xE2\x80\xA6"); return; }
        host_trace(P.H, "{\"ev\":\"game_settings_saved\"}"); if (P.game_on && P.cur && !strcmp(P.cur->sha, g->sha)) apply_prefs(); gs_back_to_caller(); return; }
    case A_GSET_RESET: { char t[400]; snprintf(t, sizeof t, "Every choice for %s goes back to Use Default. Saved games, save slots and the cartridge save are kept.", P.gs_game ? P.gs_game->title : "this game");
        m_begin("Reset Settings for This Game?", t); m_add("Reset Settings", "", A_GSET_RESET_YES, 1); m_add("Cancel", "", A_NO, 1); m_focus(A_NO); P.confirm_act = A_GSET_RESET_YES; go(ST_CONFIRM); return; }
    case A_GSET_RESET_YES: { P.confirm_act = 0; gp_prefs_none(&P.gs_draft); act(A_GSET_SAVE); return; }
    case A_GDEF_SAVE:
        if (gp_defaults_save(P.c.gset, &P.gd_draft)) { host_trace(P.H, "{\"ev\":\"defaults_save_failed\"}"); show_message("The Defaults Were Not Saved", "They could not be written on " TX_HERE ". The earlier defaults are unchanged.", ST_HSET); return; }
        if (io_async()) { wait_begin(W_GDEF, 0, "Saving the Defaults\xE2\x80\xA6"); return; }
        host_trace(P.H, "{\"ev\":\"defaults_saved\"}"); open_hset(); return;
    case A_GDEF_BUILTIN: gp_defaults_builtin(&P.gd_draft); open_gdef(); return;
    case A_GDEF_CANCEL: open_hset(); return;
    case A_HSET_MUSIC: { MU.on = !MU.on; MU.source = 1; int r = mu_pref_save(); MU.session = r != 0; mu_offer(); host_trace(P.H, "{\"ev\":\"home_music\",\"on\":%s,\"saved\":%s}", MU.on ? "true" : "false", r ? "false" : "true");
        mu_trace("music_choice"); mu_tick();   /* the Studio's Preview shows the choice made here (its music button and note) */
        if (r) { m_begin("HOME Music Was Not Saved", "It stays as you chose until this window closes, but it could not be saved on " TX_HERE "."); m_add("OK", "", A_HSET, 1); go(ST_MESSAGE); return; }
        open_hset(); m_focus(A_HSET_MUSIC); return; }
    default:
        if (a >= A_PICK_DEV && a < A_PICK_DEV + PICK_MAX) { host_pad_choose(P.H, P.pick_player, a - A_PICK_DEV); P.settle_next = 1; open_pads(); return; }
        if (a >= A_GSET_FIELD && a < A_GSET_FIELD_END) { int f = a - A_GSET_FIELD; if (f == 14) { P.st = ST_MENU; open_gset(); return; } if (P.st == ST_GDEF) gdef_field(f, 1); else gset_field(f, 1); return; }
        if (a >= A_SLOT1 && a <= A_SLOT4) {
            int s = a - A_SLOT1 + 1; gp_state_info i; gp_state_info_read(&P.L, slot_n(s), &i); P.pending_slot = s; char t[500], ti[64], yes[48], w[96], nm[120]; saved_at(&i, w, sizeof w); slot_named(s, nm, sizeof nm);
            if (P.slot_mode == 3) { open_name(s, 0); return; }   /* Name a Slot */
            if (P.slot_mode) { P.pending_load = 0; if (i.st == GS_EMPTY) { act(A_YES); return; }   /* saving over (or copying over) a slot asks first */
                if (P.slot_mode == 2) { snprintf(ti, sizeof ti, "Copy Over Slot %d?", s); snprintf(t, sizeof t, "%s holds a save, %s. Copying replaces it with the earlier automatic save; the other slots are not changed.", nm, w); snprintf(yes, sizeof yes, "Copy Over Slot %d", s); }
                else { snprintf(ti, sizeof ti, "Save Over Slot %d?", s); snprintf(t, sizeof t, "%s holds a save, %s. Saving replaces it with this moment; the other slots are not changed.", nm, w); snprintf(yes, sizeof yes, "Save Over Slot %d", s); }
                m_begin(ti, t); m_add(yes, "", A_YES, 1); m_add("Cancel", "Slot unchanged", A_NO, 1); m_focus(A_NO); go(ST_CONFIRM); return; }
            P.pending_load = 1;
            if (P.slots_from_start || !P.game_on) { act(A_YES); return; }   /* chosen from the start choices: nothing has been played yet */
            snprintf(ti, sizeof ti, "Load Slot %d?", s); snprintf(t, sizeof t, "The game goes back to the moment in %s, %s. What you played since your last save is lost unless you save it to a slot first.", nm, w); snprintf(yes, sizeof yes, "Load Slot %d", s);
            m_begin(ti, t); m_add(yes, "", A_YES, 1); m_add("Cancel", "", A_NO, 1); m_focus(A_NO); go(ST_CONFIRM); return; }
    } }

/* ---------------- input ---------------- */
static int pressed(int p, int a) { return P.pad.down[p][a] && !P.prevpad.down[p][a] && !P.mask[p][a]; }
static int any_pressed(int a) { for (int p = 0; p < GP_PLAYERS; p++) if (pressed(p, a)) return 1; return 0; }
static void unmask(void) { for (int p = 0; p < GP_PLAYERS; p++) for (int a = 0; a < GA_COUNT; a++) if (!P.pad.down[p][a]) P.mask[p][a] = 0; }
/* P.gin: what the game or the HOME gets. SELECT + START is the player's own menu combination, so it never reaches them: once one player
   holds both, neither counts there until each is released (the mask). To tell the combination from a single press, SELECT or START
   alone arrives COMBO_GRACE_MS late and is released as late, so the game sees it for as long as it was held; a tap shorter than the
   grace still arrives, for its own length (at least 50 ms). When the other joins, the first one ends at once and neither counts. */
static void gate_input(uint64_t now) {
    static const int SS[2] = { GA_SELECT, GA_START }; memcpy(P.gprev, P.gin, sizeof P.gin);
    for (int p = 0; p < GP_PLAYERS; p++) {
        if (P.pad.down[p][GA_SELECT] && P.pad.down[p][GA_START]) {
            if (!P.mask[p][GA_SELECT] || !P.mask[p][GA_START]) host_trace(P.H, "{\"ev\":\"combo\",\"player\":%d}", p + 1);
            P.mask[p][GA_SELECT] = P.mask[p][GA_START] = 1; memset(P.ssg[p], 0, sizeof P.ssg[p]); }
        for (int a = 0; a < GA_COUNT; a++) P.gin[p][a] = P.pad.down[p][a] && !P.mask[p][a];
        for (int k = 0; k < 2; k++) { int a = SS[k]; ss_gate *g = &P.ssg[p][k];
            if (P.gin[p][a]) { if (!g->down) { g->down = 1; g->sent = 0; g->at = now; } if (now - g->at >= COMBO_GRACE_MS) g->sent = 1; P.gin[p][a] = g->sent; }
            else { if (g->down) { uint64_t len = now - g->at; g->until = now + (g->sent ? COMBO_GRACE_MS : len < 50 ? 50 : len); } g->down = 0; P.gin[p][a] = now < g->until; }
            if (P.gin[p][a] != P.gprev[p][a]) host_trace(P.H, "{\"ev\":\"game_input\",\"player\":%d,\"button\":\"%s\",\"down\":%d}", p + 1, k ? "start" : "select", P.gin[p][a]); } } }
static void menu_step(int dir) { menu *m = &P.m; g_dirty = 1; for (int k = 0; k < m->n; k++) { m->focus = (m->focus + m->n + dir) % m->n; if (m->it[m->focus].enabled) break; } }
static void menu_choose(int i) { item *it = &P.m.it[i]; if (it->enabled) { host_trace(P.H, "{\"ev\":\"choose\",\"act\":%d}", it->act); act(it->act); } }
static void menu_back(void) { int st = P.st; host_trace(P.H, "{\"ev\":\"back\",\"state\":%d}", st);
    if (st == ST_MENU) act(A_KEEP); else if (st == ST_START) act(A_BACK); else if (st == ST_SLOTS || st == ST_PADS || st == ST_PICK || st == ST_GSET || st == ST_HSET || st == ST_GDEF) act(A_BACK); else if (st == ST_CONFIRM) act(A_NO); else if (st == ST_MESSAGE && P.m.n == 1) act(P.m.it[0].act); else if (st == ST_HOMEMENU) act(A_KEEP_BROWSING); }
static void menu_input(void) {
    menu *m = &P.m; if (!m->n) return;
    for (int a = 0; a < GA_COUNT; a++) if (any_pressed(a)) { kb_take(); break; }   /* F3: the controller or keyboard chose: the parked mouse does not take it back */
    if (any_pressed(GA_UP)) menu_step(-1);
    if (any_pressed(GA_DOWN)) menu_step(1);
    if ((P.st == ST_GSET || P.st == ST_GDEF) && (any_pressed(GA_LEFT) || any_pressed(GA_RIGHT))) {   /* a choice changes in place */
        int a = m->it[m->focus].act, f = a - A_GSET_FIELD, dir = any_pressed(GA_LEFT) ? -1 : 1;
        if (a >= A_GSET_FIELD && a < A_GSET_FIELD_END && f >= 3 && f <= 10) { if (P.st == ST_GDEF) gdef_field(f, dir); else gset_field(f, dir); } return; }
    if (any_pressed(GA_A) || any_pressed(GA_START)) menu_choose(m->focus);
    else if (any_pressed(GA_B)) menu_back(); }
/* the mouse in the player's menus (the computer): an item highlights under the pointer and a click chooses it; a click outside the menu
   and the back button do what B does there; the wheel moves; a double-click is ignored (its first click already chose) */
static void menu_pointer(const host_pointer *e) {
    menu *m = &P.m; if (!m->n) return; int hit = -1;
    for (int i = 0; i < m->n; i++) if (e->x >= P.mrx && e->x < P.mrx + P.mrw && e->y >= P.mry[i] && e->y < P.mry[i] + P.mrh) hit = i;
    int inside = e->x >= P.mpx && e->x < P.mpx + P.mpw && e->y >= P.mpy && e->y < P.mpy + P.mph;
    if (e->kind == PT_MOVE) {
        if (X.kb_owner) { if (!X.pt_seen) { X.ref_x = e->x; X.ref_y = e->y; }
            int dx = e->x - X.ref_x, dy = e->y - X.ref_y; if (!X.pt_seen || dx * dx + dy * dy < 12 * 12) { X.pt_seen = 1; X.pt_x = e->x; X.pt_y = e->y; return; }   /* not yet a deliberate move */
            X.kb_owner = 0; host_trace(P.H, "{\"ev\":\"mouse_takes_highlight\",\"at\":[%d,%d]}", e->x, e->y); }
        X.pt_seen = 1; X.pt_x = e->x; X.pt_y = e->y;
        if (hit >= 0 && hit != m->focus && m->it[hit].enabled) { m->focus = hit; g_dirty = 1; } return; }
    X.pt_seen = 1; X.pt_x = e->x; X.pt_y = e->y; X.kb_owner = 0;
    host_trace(P.H, "{\"ev\":\"menu_mouse\",\"kind\":%d,\"at\":[%d,%d],\"item\":%d,\"inside\":%d,\"state\":%d}", e->kind, e->x, e->y, hit, inside, P.st);
    if (e->kind == PT_CLICK) { if (hit >= 0) { if (m->it[hit].enabled) { m->focus = hit; g_dirty = 1; menu_choose(hit); } } else if (!inside && b_acts()) menu_back(); }
    else if (e->kind == PT_BACK) { if (b_acts()) menu_back(); }
    else if (e->kind == PT_WHEEL) menu_step(e->delta > 0 ? -1 : 1); }
static const button HB[GA_COUNT] = { B_UP, B_DOWN, B_LEFT, B_RIGHT, B_A, B_B, B_SELECT, B_START };
static void home_cb(void *ctx, button b, in_kind k, uint64_t clock) { (void)ctx; if (P.hm && (k == IN_PRESS || k == IN_REPEAT || k == IN_RELEASE || k == IN_FORCED_RELEASE)) home_input(P.hm, b, k, clock); }
#define HOME_OPEN() (P.st == ST_HOME && !P.game_on)   /* the HOME still has the controllers (a press can open another screen or a game) */
static void home_feed(void) {   /* both players drive HOME (the console's two controllers do) */
    uint64_t now = host_now(P.H);
    if (now < P.hin.clock_ms || now - P.hin.clock_ms > 250) P.hin.clock_ms = now;   /* the HOME was not running: no repeats for that time */
    else input_tick(&P.hin, now - P.hin.clock_ms, home_cb, NULL);                 /* held directions repeat at 400 / 110 / 55 ms */
    /* K2 v3: the presses since the last pass, in the order they came (a host that keeps them: the console's). Read as states alone, two
       taps within one pass counted once and a tap right after one the HOME still held was lost. A press ends the one before it of the same
       button; every press but the last of its button ends at once. SELECT and START keep their own gate (gate_input) */
    int nq[GA_COUNT] = { 0 }, last[GA_COUNT], left = 0;
    for (int k = 0; k < P.pad.nq && k < GP_PADQ; k++) { int p = P.pad.q[k] >> 4, a = P.pad.q[k] & 15; if (p < GP_PLAYERS && a < GA_COUNT && a != GA_SELECT && a != GA_START && P.gin[p][a]) { nq[a]++; last[a] = k; } }
    for (int k = 0; k < P.pad.nq && k < GP_PADQ; k++) { int p = P.pad.q[k] >> 4, a = P.pad.q[k] & 15; if (p >= GP_PLAYERS || a >= GA_COUNT || a == GA_SELECT || a == GA_START || !P.gin[p][a]) continue;
        if (!HOME_OPEN()) { left++; continue; }   /* K3D1-F1: an earlier press of this pass left the HOME */
        if (P.hin.down[HB[a]]) input_release(&P.hin, HB[a], home_cb, NULL);
        input_press(&P.hin, HB[a], home_cb, NULL); if (k != last[a]) input_release(&P.hin, HB[a], home_cb, NULL); }
    if (left) { gp_hs.left_at_transition += (uint64_t)left; host_trace(P.H, "{\"ev\":\"presses_after_leaving_home\",\"count\":%d,\"state\":%d}", left, P.st); }
    for (int a = 0; a < GA_COUNT && HOME_OPEN(); a++) { int d = 0, pd = 0; for (int p = 0; p < GP_PLAYERS; p++) { d |= P.gin[p][a]; pd |= P.gprev[p][a]; }
        if (nq[a]) gp_hs.extra_taps += (uint64_t)(nq[a] - (d && !pd ? 1 : 0));   /* the presses read as states alone would have lost */
        if (d && !pd && !nq[a]) input_press(&P.hin, HB[a], home_cb, NULL); else if (!d && pd) input_release(&P.hin, HB[a], home_cb, NULL); } }
static int hold_step(uint64_t now) {   /* SELECT + START held by any player: 1 when the 3 seconds are reached (the menu then opens) */
    int hold = 0; for (int p = 0; p < GP_PLAYERS; p++) hold |= P.pad.down[p][GA_SELECT] && P.pad.down[p][GA_START];
    unsigned long long fr = (unsigned long long)(P.game_on ? emu_frames() : 0);
    if (hold) { if (!P.holding) { P.holding = 1; P.hold_t0 = now; host_trace(P.H, "{\"ev\":\"hold_start\",\"frames\":%llu}", fr); } } else if (P.holding) { P.holding = 0; host_trace(P.H, "{\"ev\":\"hold_cancel\",\"ms\":%llu,\"frames\":%llu}", (unsigned long long)(now - P.hold_t0), fr); }
    if (P.holding && now - P.hold_t0 >= HOLD_MS) { P.holding = 0; host_trace(P.H, "{\"ev\":\"hold_menu\",\"ms\":%llu,\"frames\":%llu}", (unsigned long long)(now - P.hold_t0), fr); return 1; }
    return 0; }

/* the game's own input, after the mapping and the SELECT + START gate: the A/B swap, then rapid fire. Rapid fire is counted in emulated
   frames (the game's own clock: 60 a second NTSC, 50 PAL): a held A or B is pressed for gp_rapid_half() frames, released as many, and
   so on; the first frame of a hold is a press, a release ends it at once, and every state change starts it over (settle). The core's
   own turbo inputs are not used (gp_emu.c answers 0 for them), so nothing is applied twice. */
static void game_policy(uint8_t in[GP_PLAYERS][GA_COUNT]) {
    static const char *BN[2] = { "a", "b" };
    for (int p = 0; p < GP_PLAYERS; p++) {
        if (P.eff.swap_ab) { uint8_t t = in[p][GA_A]; in[p][GA_A] = in[p][GA_B]; in[p][GA_B] = t; }
        for (int k = 0; k < 2; k++) { int a = k ? GA_B : GA_A; if (!P.eff.rapid[p][k]) { P.rf[p][k].held = 0; continue; }
            if (!in[p][a]) { if (P.rf[p][k].held) host_trace(P.H, "{\"ev\":\"rapid\",\"player\":%d,\"button\":\"%s\",\"down\":0,\"frame\":%llu,\"released\":1}", p + 1, BN[k], (unsigned long long)emu_frames()); P.rf[p][k].held = 0; P.rf[p][k].n = 0; continue; }
            int half = gp_rapid_half(P.eff.speed), on = (P.rf[p][k].n / half) % 2 == 0, was = P.rf[p][k].held ? ((P.rf[p][k].n - 1) / half) % 2 == 0 : -1;
            if (on != was) host_trace(P.H, "{\"ev\":\"rapid\",\"player\":%d,\"button\":\"%s\",\"down\":%d,\"frame\":%llu}", p + 1, BN[k], on, (unsigned long long)emu_frames());
            P.rf[p][k].held = 1; P.rf[p][k].n++; in[p][a] = (uint8_t)on; } } }

/* ---------------- the loop ---------------- */
static void present_play(void) { int w, h; const uint16_t *px = emu_frame(&w, &h); host_present_nes(P.H, px, w, h); }
/* the session as three calls (gp_run loops them): gp_open, gp_step until it returns 0, gp_close. A host that cannot block (the browser)
   calls gp_step itself, once per frame it wants; gp_coop tells the session never to wait inside a step */
int gp_coop = 0;
static uint64_t g_next;
int gp_home_on_change = 0, gp_home_pass_delay_ms = 0, gp_home_check_titles = 0; gp_home_stats gp_hs; void (*gp_host_present_region)(host *H, const framebuffer *fb, int x, int y, int w, int h) = NULL; int (*gp_host_audio_source)(host *H, gp_audio_fill fill, void *ctx, size_t lead) = NULL;
#define HOME_REFRESH_MS 3000   /* a HOME drawn only on change is drawn again at least this often anyway */
static struct { int drawn, bars; unsigned gen; uint64_t at, resp_t0, last, rn, pn; double rsum, pmax, resp_max; } HL;   /* bars: the last full picture had the hold or setup bar */   /* the HOME's loop: the last picture and this second's measurements */
int gp_open(const char *session_path, const host_config *hc_in) {
    char why[400]; P.su_uid = -1;
    if (load_cfg(session_path, why, sizeof why)) { fprintf(stderr, "ccs-player: %s\n", why); return 2; }
    gp_settings_default(&P.s); if (P.c.settings[0]) gp_settings_load(&P.s, P.c.settings); if (P.c.display[0]) gp_settings_load(&P.s, P.c.display);
    gp_defaults_builtin(&P.gdef); gp_prefs_none(&P.gpref); gp_resolve(&P.gdef, &P.gpref, &P.s, &P.eff);
    host_config hc = *hc_in; hc.session_path = session_path; hc.settings_path = P.c.settings; hc.display_path = P.c.display; hc.studio_pid = P.c.studio_pid; if (P.c.embed[0]) hc.embed_pipe = P.c.embed; if (!hc.title) hc.title = home_mode() ? "Classic Cart Studio · HOME" : "Classic Cart Studio";
    P.H = host_open(&hc, &P.s, why, sizeof why); if (!P.H) { snprintf(P.err, sizeof P.err, "%s", why); write_result("error"); fprintf(stderr, "ccs-player: %s\n", why); return 3; }
    ccs_scale = host_canvas_scale(); fb_init(&P.fb, CCS_FB_W * ccs_scale, CCS_FB_H * ccs_scale); host_trace(P.H, "{\"ev\":\"canvas\",\"w\":%d,\"h\":%d,\"scale\":%d}", P.fb.w, P.fb.h, ccs_scale);
    P.fonts_ok = fonts_load(); if (!P.fonts_ok) host_trace(P.H, "{\"ev\":\"fonts_missing\"}");
    gp_mkdirs(P.c.saves); emu_init(P.c.scratch[0] ? P.c.scratch : NULL); input_init(&P.hin); P.hin.repeat_delay_ms = 400; P.hin.repeat_rate_ms = 110; P.hin.repeat_fast_after = 6; P.hin.repeat_fast_rate = 55;
    host_poll(P.H, &P.pad, &(host_events){ 0 }); settle();   /* whatever is held when the player opens counts only after release */
    if (home_mode()) {
        char err[300]; P.hm = home_load2(P.c.pack, P.c.state[0] ? P.c.state : NULL, home_trace, err, sizeof err);
        if (!P.hm) { snprintf(P.err, sizeof P.err, "The HOME could not be loaded: %s", err); write_result("error"); host_close(P.H); return 4; }
        home_set_launch(P.hm, on_launch, NULL); home_set_settings(P.hm, on_settings, NULL); home_set_game_settings(P.hm, on_game_settings, NULL); home_set_cover_budget(P.hm, home_covers()); P.st = ST_HOME; mu_open(); mu_offer();
        if (P.c.select[0]) for (int i = 0; i < P.c.ng; i++) if (!strcmp(P.c.g[i].id, P.c.select)) { home_select(P.hm, i); break; }
    } else {
        gp_game *g = game_by_id(P.c.direct);
        if (!g || !start_game(g, why, sizeof why)) { snprintf(P.err, sizeof P.err, "%s", g ? why : "The game is not in the session."); m_begin("This Game Cannot Start", P.err); m_add("Close", "", A_OK, 1); P.msg_back_st = ST_QUIT; go(ST_MESSAGE); }
    }
    g_next = host_now(P.H); return 0; }
/* one pass: the window, input, the state; 1 while the session runs, 0 once it ended */
int gp_step(void) {
    P.prevpad = P.pad; host_events ev = { 0 }; host_poll(P.H, &P.pad, &ev); if (P.settle_next) { P.settle_next = 0; settle(); }   /* who plays changed: what is held counts after release */
    unmask(); gate_input(host_now(P.H)); { int was = P.st; uint64_t sw = host_now(P.H); int vis = setup_drawn(sw); setup_watch(sw); if (P.st != was || vis != setup_drawn(sw)) g_dirty = 1; }
    if (ev.focus_lost && P.st == ST_PLAY) open_focus_paused();   /* at once, with a message that stays until the person chooses */
    if (ev.focus_gained) settle();                                /* keys held while away count only after release */
    if (ev.focus_lost) MU.focused = 0; if (ev.focus_gained) { MU.focused = 1; mu_pref_reload(); }   /* HOME Music pauses while the window is not in front */
    for (int i = 0; i < ev.npt; i++) {   /* the mouse: the HOME and the menus, never the game */
        if (P.st == ST_HOME && P.hm) home_pointer(P.hm, ev.pt[i].kind, ev.pt[i].x, ev.pt[i].y, ev.pt[i].delta, host_now(P.H));
        else if (P.st == ST_NAME) { const host_pointer *e = &ev.pt[i]; if (e->kind == PT_CLICK) for (int k = 0; k < X.nk; k++) if (e->x >= X.kx[k] && e->x < X.kx[k] + X.kw[k] && e->y >= X.ky[k] && e->y < X.ky[k] + X.kh[k]) { X.name_focus = k; name_key(k); break; } }
        else if (P.st != ST_PLAY && P.st != ST_LEARN && P.st != ST_QUIT && P.st != ST_WAIT) menu_pointer(&ev.pt[i]); }
    if (P.st == ST_NAME && ev.ntext) name_text(ev.text);
    host_cursor(P.H, P.st != ST_PLAY);
    if (ev.layout) {   /* tests: where things are */
        if (P.st == ST_HOME && P.hm) { g_layout = 1; home_layout(P.hm, &P.fb, host_now(P.H)); g_layout = 0; }
        else if (P.m.n) { char b[1400]; size_t k = (size_t)snprintf(b, sizeof b, "{\"ev\":\"menu_layout\",\"state\":%d,\"panel\":[%d,%d,%d,%d],\"items\":[", P.st, P.mpx, P.mpy, P.mpw, P.mph);
            for (int i = 0; i < P.m.n && k < sizeof b - 40; i++) k += (size_t)snprintf(b + k, sizeof b - k, "%s[%d,%d,%d,%d]", i ? "," : "", P.mrx, P.mry[i], P.mrw, P.mrh);
            snprintf(b + k, sizeof b - k, "]}"); host_trace(P.H, "%s", b); } }
    if (ev.studio_exit) {   /* F6: the Studio closes (1, 2 forced) or is gone (3): the game's own save is written; no slot is ever written */
        host_trace(P.H, "{\"ev\":\"studio_exit\",\"kind\":%d,\"state\":%d,\"game\":%d}", ev.studio_exit, P.st, P.game_on);
        if (P.game_on) { int cr = sram_flush(1);
            if (cr < 0 && ev.studio_exit == 1) { char e2[400], l[600]; json_escape(P.cart_fail_why, e2, sizeof e2); snprintf(l, sizeof l, "exit-failed %s", e2); host_send(P.H, l); XA.studio = 1; open_cart_failed(1, P.saved_slot); }
            else { stop_game(cr < 0 ? "studio_closed_without_cartridge_save" : "studio_closed"); go(ST_QUIT); } }
        else go(ST_QUIT);
        ev.close_requested = ev.preview_close = 0; }
    if (ev.theme[0] && P.hm) { g_back_st = -1; home_set_theme(P.hm, ev.theme, 0); host_trace(P.H, "{\"ev\":\"studio_theme\",\"theme\":\"%s\"}", home_theme(P.hm)); home_emit_state(P.hm); g_dirty = 1; }
    if (ev.music >= 0 && MU.avail) { MU.on = ev.music; MU.source = 1; MU.session = 0; mu_offer(); if (P.st == ST_HSET) { int fa = P.m.it[P.m.focus].act; open_hset(); m_focus(fa); } }   /* the Studio saved it; this window follows */
    if (ev.select[0] && P.hm && !P.game_on) for (int i = 0; i < P.c.ng; i++) if (!strcmp(P.c.g[i].id, ev.select)) { home_select(P.hm, i); home_emit_state(P.hm); break; }
    if (ev.reload[0]) { if (P.game_on) XA.reload = 1; else reload_home(); }   /* the package folder was rebuilt (the Studio's collection or HOME changed) */
    if ((ev.close_requested || ev.preview_close) && host_embedded(P.H)) {   /* embedded: the pop-out window's X returns the HOME to the Studio; the Preview's Close closes it, popped out or not */
        int pop = host_popped(P.H), back = pop && !ev.preview_close;
        host_trace(P.H, "{\"ev\":\"close_requested\",\"state\":%d,\"popped\":%d,\"preview\":%d}", P.st, pop, ev.preview_close);
        ev.close_requested = ev.preview_close = 0;
        if (!P.game_on) { if (back) { host_popout(P.H, 0); if (P.st == ST_HOMEMENU || P.st == ST_HSET) go(ST_HOME); } else { if (pop) host_popout(P.H, 0); go(ST_QUIT); } }
        else { if (back) XA.popin = 1; else { if (pop) host_popout(P.H, 0); XA.quit = 1; }   /* a game is open: its own choices first (Keep Playing cancels); the Preview's Close asks in the Preview */
            if (P.st == ST_PLAY) open_menu(); else if (P.st == ST_START) act(A_BACK); else if (P.st == ST_MESSAGE && P.focus_modal) open_menu(); else if (P.st == ST_LEARN) learn_cancel("Nothing was changed."); } }
    if (ev.close_requested) {
        host_trace(P.H, "{\"ev\":\"close_requested\",\"state\":%d}", P.st);
        if (P.st == ST_HOME) { m_begin("Close the HOME?", "You return to Classic Cart Studio."); m_add("Close and Return to Studio", "", A_CLOSE_HOME, 1); m_add("Keep Browsing", "", A_KEEP_BROWSING, 1); m_focus(A_KEEP_BROWSING); go(ST_HOMEMENU); }
        else if (P.st == ST_PLAY) open_menu();
        else if (!P.game_on && P.st == ST_MESSAGE) go(ST_QUIT);
        else if (P.st == ST_MESSAGE && P.focus_modal) open_menu();
        else if (P.st == ST_START) act(A_BACK);
        else if (P.st == ST_LEARN) learn_cancel("Nothing was changed.");
    }
    if (ev.menu_key && P.st == ST_NAME) { name_key(NK_CANCEL); ev.menu_key = 0; }
    if (ev.close_requested && P.st == ST_NAME) name_key(NK_CANCEL);
    if (ev.menu_key) { if (P.st == ST_PLAY) open_menu(); else if (P.st == ST_HOME) open_home_menu(); else if (P.st == ST_MENU) act(A_KEEP); else if (P.st == ST_GSET || P.st == ST_HSET || P.st == ST_GDEF) menu_back(); else if (P.st == ST_MESSAGE && P.focus_modal) open_menu(); else if (P.st == ST_LEARN) learn_cancel("Nothing was changed."); }
    mu_badge(); mu_tick();   /* HOME Music: the badge's choice, then plays, pauses or goes on, as the HOME's state says */
    if (P.st == ST_QUIT) return 0;
    switch (P.st) {
    case ST_HOME: {
        uint64_t p0 = host_now(P.H);
        if (hold_step(p0)) { HL.resp_t0 = 0; open_home_menu(); g_next = host_now(P.H); break; }   /* the same 3 seconds: close the HOME with a controller */
        unsigned g0 = home_frame_gen(P.hm); home_feed();
        if (P.game_on) { HL.resp_t0 = 0; input_focus(&P.hin, 0, home_cb, NULL); input_focus(&P.hin, 1, home_cb, NULL); g_next = host_now(P.H); break; }   /* launched: HOME's held buttons end here */
        if (P.st != ST_HOME) { HL.resp_t0 = 0; g_next = host_now(P.H); break; }   /* K3D1-F1: another screen opened; it draws its own first picture (g_dirty) at once */
        if (gp_home_pass_delay_ms > 0) gp_sleep_ms(gp_home_pass_delay_ms);   /* tests only: a pass as slow as a slow console's */
        if (home_frame_gen(P.hm) != g0 && !HL.resp_t0) HL.resp_t0 = p0 ? p0 : 1;   /* a press changed the HOME: its answer is the next picture */
        /* K2 v3 (V3-F1): on the console every pass drew the whole 1280 x 720 HOME again, about a second each at 600 MHz, so presses waited
           and merged. With gp_home_on_change (the console's host) a pass draws only when the picture can differ: a change since the last
           picture (gen), something still moving or decoding (live), the hold or setup bar, a new screen (g_dirty); and every few seconds anyway */
        int due = !gp_home_on_change || g_dirty || !HL.drawn || home_frame_gen(P.hm) != HL.gen || home_frame_live(P.hm) || P.holding || setup_drawn(p0) || p0 - HL.at >= HOME_REFRESH_MS;
        uint64_t r0 = host_now(P.H); int box[4];
        /* K3D1-F3: when only the Arcade Grid's scrolling title moves (the same frame gen, nothing over the last full picture, no refresh due), only
           the title is drawn into the last picture and only its rectangle is shown (the console's host); the result equals a full picture */
        if (due && gp_home_on_change && gp_host_present_region && HL.drawn && !HL.bars && !g_dirty && home_frame_gen(P.hm) == HL.gen && !P.holding && !setup_drawn(r0)
            && r0 - HL.at < HOME_REFRESH_MS && home_render_caption(P.hm, &P.fb, r0, box)) {
            gp_host_present_region(P.H, &P.fb, box[0], box[1], box[2], box[3]); double ms = (double)(host_now(P.H) - r0); gp_hs.titles++; gp_hs.title_ms_sum += ms; if (ms > gp_hs.title_ms_max) gp_hs.title_ms_max = ms;
            if (gp_home_check_titles) { framebuffer t; if (!fb_init(&t, P.fb.w, P.fb.h)) { home_render(P.hm, &t, r0); size_t nb = (size_t)t.w * t.h * 3; if (memcmp(t.rgb, P.fb.rgb, nb)) { gp_hs.title_mismatch++; host_trace(P.H, "{\"ev\":\"title_mismatch\",\"t\":%llu}", (unsigned long long)r0); } fb_free(&t); } }
            due = 0; }
        if (due) { home_render(P.hm, &P.fb, r0); int bar = P.holding && r0 - P.hold_t0 >= HOLD_SHOW_MS; if (bar) hold_bar((int)(r0 - P.hold_t0)); setup_bar(r0); host_present_canvas(P.H, &P.fb);
            uint64_t r1 = host_now(P.H); double ms = (double)(r1 - r0); if (P.st == ST_HOME) g_dirty = 0;   /* a screen the mouse opened while the HOME was drawn keeps its own redraw (K3D1-F1) */
            HL.drawn = 1; HL.bars = bar || setup_drawn(r0); HL.gen = home_frame_gen(P.hm); HL.at = r1; HL.rn++; HL.rsum += ms; gp_hs.renders++; gp_hs.render_ms_sum += ms; if (ms > gp_hs.render_ms_max) gp_hs.render_ms_max = ms;
            if (HL.resp_t0) { double rs = (double)(r1 - HL.resp_t0); gp_hs.responses++; gp_hs.resp_ms_sum += rs; if (rs > gp_hs.resp_ms_max) gp_hs.resp_ms_max = rs; if (rs > HL.resp_max) HL.resp_max = rs; HL.resp_t0 = 0; } }
        { uint64_t e = host_now(P.H); double pm = (double)(e - p0); gp_hs.passes++; HL.pn++; if (pm > gp_hs.pass_ms_max) gp_hs.pass_ms_max = pm; if (pm > HL.pmax) HL.pmax = pm;
          if (e - HL.last >= 1000) { host_trace(P.H, "{\"ev\":\"home_second\",\"frames\":%llu,\"avg_ms\":%llu,\"covers_pending\":%d,\"passes\":%llu,\"pass_max_ms\":%.0f,\"response_max_ms\":%.0f,\"titles\":%llu}", (unsigned long long)HL.rn, (unsigned long long)(HL.rn ? HL.rsum / (double)HL.rn : 0), home_covers_pending(P.hm), (unsigned long long)HL.pn, HL.pmax, HL.resp_max, (unsigned long long)gp_hs.titles);
            HL.rsum = 0; HL.rn = HL.pn = 0; HL.pmax = HL.resp_max = 0; HL.last = e; } }
        uint64_t now = host_now(P.H); g_next += 33; if (g_next > now) gp_sleep_ms((int)(g_next - now)); else g_next = now; break; }
    case ST_PLAY: {
        uint8_t in[GP_PLAYERS][GA_COUNT]; memcpy(in, P.gin, sizeof in);   /* SELECT + START held together never reach the game (gate_input) */
        uint64_t now = host_now(P.H);
        if (hold_step(now)) { open_menu(); break; }
        if (P.holding) {   /* SELECT + START held: the game waits, frozen and silent, with the hold bar after HOLD_SHOW_MS */
            if (!P.hold_frozen) { P.hold_frozen = 1; host_audio_pause(P.H, 1); }
            if (now - P.hold_t0 >= HOLD_SHOW_MS) draw_hold((int)(now - P.hold_t0)); else present_play();
            gp_sleep_ms(16); g_next = host_now(P.H); break; }
        if (P.hold_frozen) { P.hold_frozen = 0; host_audio_pause(P.H, 0); }   /* released sooner: the game goes on */
        game_policy(in); emu_run(in); size_t af; const int16_t *au = emu_audio(&af); if (P.audio_ok == 1) host_audio_push(P.H, au, af);
        P.au_frames += af; for (size_t i = 0; i < af * 2; i++) { int v = au[i] < 0 ? -au[i] : au[i]; if (v > P.au_peak) P.au_peak = v; }   /* what the core produced, per second */
        if (++P.au_n >= 60) { host_trace(P.H, "{\"ev\":\"second\",\"frames\":%llu,\"audio_frames\":%llu,\"peak\":%d,\"queued\":%zu}", (unsigned long long)emu_frames(), (unsigned long long)P.au_frames, P.au_peak, host_audio_queued(P.H)); P.au_n = 0; P.au_frames = 0; P.au_peak = 0; }
        if (setup_drawn(now)) { draw_game_frame(0); setup_bar(now); host_present_canvas(P.H, &P.fb); } else present_play();   /* the note over the game */
        if (now - P.last_sram > 5000) { P.last_sram = now; if (sram_flush(0) < 0 && !P.cart_fail_shown) { P.cart_fail_shown = 1; open_cart_failed(0, 0); break; } }
        /* pacing: by the sound device's clock when it plays (no drift, no crackle), else by the frame clock */
        if (gp_coop) { }   /* the host paces the steps (the browser: by its sound clock) */
        else if (P.audio_ok == 1) { size_t lim = (size_t)(emu_rate() / emu_fps() * 3); uint64_t t0 = host_now(P.H); while (host_audio_queued(P.H) > lim && host_now(P.H) - t0 < 100) gp_sleep_ms(1); }
        else { double ft = 1000.0 / emu_fps(); static double acc; acc += ft; g_next += (uint64_t)acc; acc -= (uint64_t)acc; now = host_now(P.H); if (g_next > now) gp_sleep_ms((int)(g_next - now)); else g_next = now; }
        break; }
    case ST_LEARN: learn_step(); if (P.st == ST_LEARN && g_dirty) { draw_menu(); g_dirty = 0; } gp_sleep_ms(16); g_next = host_now(P.H); break;   /* Esc or closing cancels (above) */
    case ST_WAIT: if (!gp_io_pending()) wait_done(); if (P.st == ST_WAIT && g_dirty) { draw_menu(); g_dirty = 0; } else if (P.st != ST_WAIT && P.st != ST_QUIT && P.st != ST_PLAY && P.st != ST_HOME) { draw_menu(); g_dirty = 0; } gp_sleep_ms(16); g_next = host_now(P.H); break;
    case ST_NAME: {
        if (any_pressed(GA_UP)) name_move(0, -1); if (any_pressed(GA_DOWN)) name_move(0, 1); if (any_pressed(GA_LEFT)) name_move(-1, 0); if (any_pressed(GA_RIGHT)) name_move(1, 0);
        if (any_pressed(GA_A)) name_key(X.name_focus); else if (any_pressed(GA_B)) name_key(NK_DEL); else if (any_pressed(GA_START)) name_key(NK_DONE); else if (any_pressed(GA_SELECT)) name_key(NK_CASE);
        if (P.st == ST_NAME && g_dirty) { draw_name(); g_dirty = 0; } else if (P.st != ST_NAME && P.st != ST_QUIT && P.st != ST_PLAY && P.st != ST_HOME && g_dirty) { draw_menu(); g_dirty = 0; }
        gp_sleep_ms(16); g_next = host_now(P.H); break; }
    default: if (P.st == ST_PICK) pick_by_controller(); menu_input(); if (P.st != ST_QUIT && P.st != ST_PLAY && P.st != ST_HOME && g_dirty) { draw_menu(); g_dirty = 0; } gp_sleep_ms(16); g_next = host_now(P.H); break;
    }
    return P.st != ST_QUIT; }
int gp_close(void) {
    int quit_kind = 0;
    if (P.game_on) { int cr = sram_flush(1); stop_game(cr < 0 ? "window_closed_without_cartridge_save" : "window_closed"); quit_kind = 1; }
    write_result(P.err[0] && !P.rg ? "error" : "normal");
    if (host_embedded(P.H)) { char l[600], e2[500]; json_escape(P.result_games, e2, sizeof e2); snprintf(l, sizeof l, "bye %s", e2); host_send(P.H, l); }
    if (MU.playing) { mu_take_back(); host_audio_pause(P.H, 1); MU.playing = 0; mu_trace("music_pause"); } if (MU.f) { fclose(MU.f); MU.f = NULL; }   /* HOME Music ends with the window */
    host_return_focus(P.H); if (P.hm) home_free(P.hm); host_close(P.H); fb_free(&P.fb); (void)quit_kind;
    return 0; }
/* for a host that paces the steps itself: what the session shows now (GPV_*: a game playing, SELECT + START held, a menu or message, the HOME) */
int gp_view(void) { return P.st == ST_PLAY ? (P.holding ? GPV_HOLD : GPV_PLAY) : P.st == ST_HOME ? GPV_HOME : P.st == ST_QUIT ? GPV_ENDED : GPV_MENU; }
void gp_redraw(void) { g_dirty = 1; }
int gp_run(const char *session_path, const host_config *hc) { int r = gp_open(session_path, hc); if (r) return r; while (gp_step()) { } return gp_close(); }
