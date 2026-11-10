/* host_web.c: the player's browser host (WebAssembly, wasm32-wasi). The same session as Windows (gp_session.c, FCEUmm, the saves and
 * settings code); the page's runtime (tools/rom-pipeline/studio/ccs-play.js) supplies what a window would: the picture (a canvas), sound
 * (an AudioWorklet fed from here), the keyboard, touch controls and controllers (the Gamepad API), and storage. The browser cannot block,
 * so the runtime calls gp_step itself (gp_coop): one game frame per step while a game plays, paced by the sound clock.
 *   input     the runtime writes web_in (below) before each step: the keyboard's two sets of keys (and the touch controls) as held, every
 *             connected controller (one with the standard layout as its actions, as Windows reads an Xbox-compatible controller; any
 *             other as its raw state and mapping, which gp_pads.c maps and learns here, as on Windows), the mouse in stage pixels, and
 *             the window events (close, the menu key, focus). Who plays which player is decided here for all of them, as on Windows.
 *   files     gp_util.c's file layer goes to the runtime's file store (a memory copy of the browser's storage); a write is reported done
 *             only when the browser's own storage confirmed it (gp_io_pending / gp_io_failed)
 * Nothing here reaches a network. */
#include "host.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdarg.h>

#define IMP(n) __attribute__((import_module("ccs"), import_name(n)))
#define EXP(n) __attribute__((export_name(n)))
IMP("present") void js_present(int kind, const void *px, int w, int h, int stride);
IMP("audio_open") int js_audio_open(int rate);
IMP("audio_push") void js_audio_push(const int16_t *st, int frames);
IMP("audio_queued") int js_audio_queued(void);
IMP("audio_pause") void js_audio_pause(int paused);
IMP("audio_close") void js_audio_close(void);
IMP("trace") void js_trace(const char *s, int len);
IMP("set_title") void js_set_title(const char *s);
IMP("set_picture") void js_set_picture(int tv_aspect, int integer_scale);
IMP("cursor") void js_cursor(int visible);
IMP("fullscreen") int js_fullscreen(int toggle);
IMP("pad_name") void js_pad_name(int id, char *out, int n);
IMP("pad_saved") void js_pad_saved(int id, int ok);
IMP("pad_key") void js_pad_key(int id, char *out, int n);
IMP("text_input") void js_text_input(int on);

/* what the runtime writes before each step */
#define WP 4
typedef struct { int32_t id, connected, configured, kind; uint8_t btn[32]; int32_t pov, axis[GP_AXES], map[GA_COUNT]; uint8_t act[GA_COUNT]; } web_pad;   /* kind 1: the standard layout (act, read by the runtime; no setup); 2: raw state and its mapping */
typedef struct {
    uint8_t keys[GP_PLAYERS][GA_COUNT];              /* the keyboard as held, not yet routed: its main keys (and the touch controls), its second keys */
    int32_t close_requested, menu_key, focus_lost, focus_gained, layout, focused;
    int32_t npt, pt[16][4];                           /* the mouse: kind (PT_*), x, y (stage pixels; -1 -1 outside), delta */
    int32_t npads; web_pad pads[WP];                  /* every connected controller, in connection order */
    int32_t pads_changed;                             /* a controller was connected or removed */
    int32_t ntext; char text[32];                     /* typed while text input is on */
} web_in;
static web_in IN;
struct host { int fs, cursor_hidden, swap; char pick[GP_PLAYERS][64]; uint8_t pact[WP][GA_COUNT], pbtn[WP][32]; int hit[WP]; char pads_path[1100], assign_path[1100]; int ever[WP]; uint32_t rgba_n; };
static host HS;
static uint8_t ROUTED[GP_PLAYERS][GA_COUNT];   /* tests: what each player held at the last poll, after routing (web_routed) */
EXP("web_input") web_in *web_input(void) { return &IN; }
EXP("web_malloc") void *web_malloc(int n) { return malloc((size_t)(n > 0 ? n : 1)); }
EXP("web_free") void web_free(void *p) { free(p); }
EXP("web_open") int web_open(const char *session_path) { gp_coop = 1; host_config hc; memset(&hc, 0, sizeof hc); return gp_open(session_path, &hc); }
EXP("web_step") int web_step(void) { return gp_step(); }
EXP("web_close") int web_close(void) { return gp_close(); }
EXP("web_view") int web_view(void) { return gp_view(); }
EXP("web_redraw") void web_redraw(void) { gp_redraw(); }   /* the picture moved to another surface: the menu is drawn again */
EXP("web_routed") const uint8_t *web_routed(void) { return &ROUTED[0][0]; }

void host_trace(host *H, const char *fmt, ...) { (void)H; char b[2048]; va_list a; va_start(a, fmt); int n = vsnprintf(b, sizeof b, fmt, a); va_end(a); if (n > 0) js_trace(b, n < (int)sizeof b ? n : (int)sizeof b - 1); }
uint64_t host_now(host *H) { (void)H; return gp_ms(); }
static void dev_key(int i, char *out, size_t n) {   /* a controller's place among those connected, kept across a reconnection: its model, numbered */
    char k[96], o[96]; js_pad_key(IN.pads[i].id, k, sizeof k); int same = 1;
    for (int j = 0; j < i; j++) { js_pad_key(IN.pads[j].id, o, sizeof o); if (!strcmp(o, k)) same++; }
    snprintf(out, n, "%s#%d", k, same); }
static void assign_load(host *H) {   /* player-assign.json: {"p1":"<controller>","p2":"<controller>"} ("" automatic, "none" the keyboard only), as on Windows */
    uint8_t *b; size_t n; if (gp_read_file(H->assign_path, &b, &n, 1u << 16)) return; char err[80]; jval *j = json_parse((const char *)b, n, err, sizeof err); free(b); if (!j) return;
    for (int p = 0; p < GP_PLAYERS; p++) { const jval *e = j->t == J_OBJ ? json_get(j, p ? "p2" : "p1") : NULL; if (e && e->t == J_STR && e->s && strlen(e->s) < 64) snprintf(H->pick[p], 64, "%s", e->s); }
    json_free(j); }
static void assign_save(host *H) { char a[200], b[200], buf[480]; json_escape(H->pick[0], a, sizeof a); json_escape(H->pick[1], b, sizeof b);
    int n = snprintf(buf, sizeof buf, "{\"p1\":%s,\"p2\":%s}\n", a, b); int r = gp_write_atomic(H->assign_path, buf, (size_t)n); host_trace(H, "{\"ev\":\"pad_assign_saved\",\"saved\":%d}", r == 0); }
host *host_open(const host_config *c, const gp_settings *s, char *err, size_t n) {
    (void)err; (void)n; memset(&HS, 0, sizeof HS); HS.swap = s->swap_pads;
    snprintf(HS.pads_path, sizeof HS.pads_path, "%s", c->display_path && c->display_path[0] ? c->display_path : "/data/player-display.json");
    { char *sl = strrchr(HS.pads_path, '/'); snprintf(sl ? sl + 1 : HS.pads_path, sizeof HS.pads_path - (size_t)(sl ? sl + 1 - HS.pads_path : 0), "player-pads.json"); }
    snprintf(HS.assign_path, sizeof HS.assign_path, "%s", HS.pads_path);
    { char *sl = strrchr(HS.assign_path, '/'); snprintf(sl ? sl + 1 : HS.assign_path, sizeof HS.assign_path - (size_t)(sl ? sl + 1 - HS.assign_path : 0), "player-assign.json"); } assign_load(&HS);
    host_trace(&HS, "{\"ev\":\"host\",\"kind\":\"web\",\"platform\":\"%s\"}", GP_PLATFORM); return &HS; }
void host_close(host *H) { (void)H; js_audio_close(); host_trace(&HS, "{\"ev\":\"host_close\"}"); }
static void who_plays(host *H, int *who) {   /* gp_assign_players with the remembered choices that are connected now */
    int n = IN.npads < WP ? IN.npads : WP, pick[GP_PLAYERS];
    for (int p = 0; p < GP_PLAYERS; p++) { pick[p] = !H->pick[p][0] ? GP_PICK_AUTO : !strcmp(H->pick[p], "none") ? GP_PICK_NONE : GP_PICK_AUTO;
        for (int i = 0; i < n; i++) { char k[160]; dev_key(i, k, sizeof k); if (!strcmp(k, H->pick[p])) pick[p] = i; } }
    gp_assign_players(n, pick, H->swap, who); }
/* the keys and the controllers into Players 1 and 2, as Windows does (host_win.c route_inputs): each controller plays the player it was
   given; the keyboard's main keys (and the touch controls) play the player no controller plays, its second keys only with no controller
   (gp_keyboard_players). The runtime sends keys only while the page owns input; controllers count only then too. */
void host_poll(host *H, gp_pad *pad, host_events *ev) {
    memset(pad, 0, sizeof *pad); int who[WP], has[GP_PLAYERS] = { 0, 0 }, mp, sp; who_plays(H, who);
    for (int i = 0; i < IN.npads && i < WP; i++) { web_pad *d = &IN.pads[i]; uint8_t act[GA_COUNT]; H->hit[i] = 0;
        if (d->kind == 1) memcpy(act, d->act, sizeof act);
        else { gp_raw r; memset(&r, 0, sizeof r); memcpy(r.btn, d->btn, 32); r.pov = d->pov; memcpy(r.axis, d->axis, sizeof r.axis);
            int map[GA_COUNT]; for (int a = 0; a < GA_COUNT; a++) map[a] = d->map[a]; gp_raw_actions(&r, map, act);
            if (!d->configured) { act[GA_A] = act[GA_B] = act[GA_SELECT] = act[GA_START] = 0; } }   /* an unknown layout: directions only; its buttons open setup (gp_session) */
        for (int a = 0; a < GA_COUNT; a++) { if (act[a] && !H->pact[i][a] && !H->hit[i]) H->hit[i] = a + 1; H->pact[i][a] = act[a]; }
        for (int k = 0; k < 32; k++) { if (d->btn[k] && !H->pbtn[i][k] && !H->hit[i]) H->hit[i] = 9; H->pbtn[i][k] = d->btn[k]; }
        if (who[i] >= 0) { has[who[i]] = 1; if (IN.focused) for (int a = 0; a < GA_COUNT; a++) if (act[a]) pad->down[who[i]][a] = 1; } }
    gp_keyboard_players(has, &mp, &sp);
    for (int a = 0; a < GA_COUNT; a++) { if (IN.keys[0][a]) pad->down[mp][a] = 1; if (sp >= 0 && IN.keys[1][a]) pad->down[sp][a] = 1; }
    memcpy(ROUTED, pad->down, sizeof ROUTED);
    memset(ev, 0, sizeof *ev); ev->music = -1; ev->close_requested = IN.close_requested; ev->menu_key = IN.menu_key; ev->focus_lost = IN.focus_lost; ev->focus_gained = IN.focus_gained; ev->layout = IN.layout;
    ev->npt = IN.npt > 16 ? 16 : IN.npt; for (int i = 0; i < ev->npt; i++) ev->pt[i] = (host_pointer){ IN.pt[i][0], IN.pt[i][1], IN.pt[i][2], IN.pt[i][3] };
    if (IN.ntext > 0) { int k = IN.ntext < 31 ? IN.ntext : 31; memcpy(ev->text, IN.text, (size_t)k); ev->text[k] = 0; ev->ntext = k; }
    IN.close_requested = IN.menu_key = IN.focus_lost = IN.focus_gained = IN.layout = 0; IN.npt = 0; IN.ntext = 0; }
/* the picture: the game frame as RGB565 (row stride 256) or the canvas as RGB; the runtime converts and scales it */
void host_present_nes(host *H, const uint16_t *px, int w, int h) { (void)H; js_present(1, px, w, h, 256); }
int host_canvas_scale(void) { return 2; }   /* the menus at twice the stage (2560 x 1440), drawn with the computer's smooth fonts */
void host_home_pace(int *cover_budget, int *music_ahead) { *cover_budget = 4; *music_ahead = 6144; }   /* as before: four covers per pass, the song about 130 ms ahead */
void host_present_canvas(host *H, const framebuffer *fb) { (void)H; js_present(2, fb->rgb, fb->w, fb->h, fb->w * 3); }
int host_audio_open(host *H, int rate) { int r = js_audio_open(rate); host_trace(H, "{\"ev\":\"audio_open\",\"rate\":%d,\"device\":%s}", rate, r == 0 ? "true" : "false"); return r; }
void host_audio_push(host *H, const int16_t *st, size_t frames) { (void)H; if (frames) js_audio_push(st, (int)frames); }
void host_audio_pause(host *H, int paused) { (void)H; js_audio_pause(paused); }
size_t host_audio_queued(host *H) { (void)H; int q = js_audio_queued(); return q > 0 ? (size_t)q : 0; }
void host_audio_close(host *H) { (void)H; js_audio_close(); }
void host_set_title(host *H, const char *t) { (void)H; js_set_title(t); }
void host_set_picture(host *H, int tv_aspect, int integer_scale) { (void)H; js_set_picture(tv_aspect, integer_scale); }
int host_embedded(host *H) { (void)H; return 0; }   /* the browser page holds the session itself; Pop Out moves its picture (ccs-play.js) */
int host_popped(host *H) { (void)H; return 0; }
void host_popout(host *H, int out) { (void)H; (void)out; }
void host_send(host *H, const char *line) { (void)H; (void)line; }
void host_text_input(host *H, int on) { (void)H; js_text_input(on); }
void host_cursor(host *H, int visible) { if ((!visible) == H->cursor_hidden) return; H->cursor_hidden = !visible; js_cursor(visible); }
void host_return_focus(host *H) { (void)H; }
int host_fullscreen(host *H) { (void)H; return js_fullscreen(0); }
void host_toggle_fullscreen(host *H) { (void)H; js_fullscreen(1); }
/* controllers: every connected one plays, is listed and can be chosen or swapped (as on Windows); only the ones without the standard layout
   have a setup (as Windows sets up only controllers that are not Xbox-compatible): host_pad_list, host_pad_raw, host_pad_configured */
int host_pads(host *H, char *out, size_t n) { char nm[2][96] = { "", "" }; const char *ctl[2] = { NULL, NULL }; int who[WP]; who_plays(H, who);
    for (int i = 0; i < IN.npads && i < WP; i++) { int p = who[i]; if (p < 0 || ctl[p]) continue; js_pad_name(IN.pads[i].id, nm[p], sizeof nm[p]); ctl[p] = nm[p]; }
    gp_players_text(ctl, out, n); return host_pad_count(H); }
void host_swap_pads(host *H) {   /* with chosen controllers the two choices trade places (remembered); else connection order is swapped */
    if (H->pick[0][0] || H->pick[1][0]) { char t[64]; memcpy(t, H->pick[0], 64); memcpy(H->pick[0], H->pick[1], 64); memcpy(H->pick[1], t, 64); assign_save(H); } else H->swap ^= 1;
    host_trace(H, "{\"ev\":\"swap_pads\",\"swapped\":%d,\"chosen\":%d}", H->swap, H->pick[0][0] || H->pick[1][0]); }
int host_pad_count(host *H) { (void)H; return IN.npads < WP ? IN.npads : WP; }
void host_pad_info(host *H, int i, char *label, size_t n, int *player) { int who[WP]; who_plays(H, who); if (i < 0 || i >= host_pad_count(H)) { snprintf(label, n, "controller"); *player = -1; return; } js_pad_name(IN.pads[i].id, label, (int)n); *player = who[i]; }
void host_pad_choose(host *H, int player, int i) {
    if (player < 0 || player >= GP_PLAYERS) return; char k[64] = ""; if (i == GP_PICK_NONE) snprintf(k, sizeof k, "none"); else if (i >= 0 && i < host_pad_count(H)) dev_key(i, k, sizeof k);
    if (i == GP_PICK_AUTO) H->pick[0][0] = H->pick[1][0] = 0;   /* automatic: connection order for both */
    else { if (k[0] && strcmp(k, "none") && !strcmp(H->pick[1 - player], k)) snprintf(H->pick[1 - player], 64, "%s", H->pick[player]); snprintf(H->pick[player], 64, "%s", k); }   /* it played the other: they trade */
    assign_save(H); host_trace(H, "{\"ev\":\"pad_choose\",\"player\":%d,\"device\":%d,\"p1\":\"%s\",\"p2\":\"%s\"}", player + 1, i, H->pick[0], H->pick[1]); }
int host_pad_pressed(host *H, int *action) { for (int i = 0; i < host_pad_count(H); i++) if (H->hit[i]) { if (action) *action = H->hit[i] - 1; return i; } return -1; }
static web_pad *pad_by_id(int id) { for (int i = 0; i < IN.npads && i < WP; i++) if (IN.pads[i].id == id) return &IN.pads[i]; return NULL; }
int host_pad_list(host *H, int *ids, int max) { int n = 0; for (int i = 0; i < host_pad_count(H) && n < max; i++) if (IN.pads[i].kind == 2) ids[n++] = IN.pads[i].id; return n; }
int host_pad_raw(host *H, int id, gp_raw *raw) { (void)H; web_pad *d = pad_by_id(id); if (!d || !d->connected || d->kind != 2) return 0; memset(raw, 0, sizeof *raw); memcpy(raw->btn, d->btn, 32); raw->pov = d->pov; memcpy(raw->axis, d->axis, sizeof raw->axis); return 1; }
int host_pad_configured(host *H, int id) { (void)H; web_pad *d = pad_by_id(id); return d && d->kind == 2 && d->configured; }
void host_pad_name(host *H, int id, char *out, size_t n) { (void)H; js_pad_name(id, out, (int)n); }
int host_pad_set_map(host *H, int id, const int map[GA_COUNT]) {   /* player-pads.json in the browser's storage, by the controller's model (USB ids) */
    web_pad *d = pad_by_id(id); if (!d || d->kind != 2) return -1; for (int a = 0; a < GA_COUNT; a++) d->map[a] = map[a]; d->configured = 1; char key[96]; js_pad_key(id, key, sizeof key);
    int r = gp_pads_save(H->pads_path, key, map); host_trace(H, "{\"ev\":\"pad_map\",\"id\":%d,\"map\":[%d,%d,%d,%d,%d,%d,%d,%d],\"saved\":%d}", id, map[0], map[1], map[2], map[3], map[4], map[5], map[6], map[7], r == 0); js_pad_saved(id, r == 0); return r; }
