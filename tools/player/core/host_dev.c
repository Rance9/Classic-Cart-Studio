/* host_dev.c: the player on the NES Classic itself (Linux 3.4, ARMv7, static musl: ccs-player-armv7; also built for x86-64 Linux for host
 * tests). The session (gp_session.c) is the same as on the computer and in the browser; this file supplies the console's devices, through
 * devio.c (shared with the native HOME runtime that ran on the console in H1):
 *   picture   the 1280 x 720 32-bit framebuffer (/dev/fb0): the HOME and the menus at the console's own resolution (the canvas pixel for
 *             pixel), a game nearest-scaled to the full height in its proportions (TV or square pixels, the Windows player's rectangle) on
 *             black, drawn into the hidden page and shown by FBIOPAN_DISPLAY (drawing into the shown page if panning fails); the video mode
 *             is never changed. When a game falls behind its sound (a slow frame), one picture is skipped so the sound never stops
 *   sound     the console's playback device (hw:0,0 in its /etc/asound.conf: 16-bit stereo, 48,000 Hz) through the kernel's PCM interface,
 *             fed from a fixed ring without ever blocking; the session paces each game by what is queued there. A thread of its own moves
 *             the ring to the device every 10 ms (K2 v3: fed only between passes, a slow pass of the HOME emptied the device)
 *   controls  the console's controllers (clovercon through evdev), each found by its registered name: port 1 plays Player 1 and port 2
 *             Player 2 unless the person chooses otherwise in Controllers (remembered in the state folder); D-pad diagonals are held as
 *             they are; kernel repeats are ignored (the HOME repeats itself). A controller is settled when it is opened and again when the
 *             HOME first reads it (the H1 lesson: clovercon reports presses nobody made right after an open); a button held through the
 *             settling counts only after it has been released. A controller unplugged releases what it held
 *   startup   the startup image (D-113): Random among the five or a fixed one (--startup: the package's startup.json), shown first and
 *             kept at least its minimum (five seconds) by the clock from the moment it was shown, while the HOME loads behind it; the
 *             HOME's sound waits for it. A missing, damaged or unexpected image falls back visibly to another one, then to a plain card
 *   RAM test  --ram-only: every folder the session writes must be in RAM (tmpfs/ramfs, nothing linked out of it), checked before anything
 *             starts and again at every write (gp_write_check); each write then reaches its folder from the handle of a checked folder one
 *             name at a time and creates its file without following a link, both checked as opened (gp_util.c's confined file layer:
 *             the trace, the report, saves, slots, settings, locks and the test seams' files alike); nothing else is ever written
 * Nothing here starts a program, mounts anything or changes a kernel setting. Copyright (C) 2026 Rance9, GPL-3.0-or-later.
 *
 *   ccs-player-armv7 --session FILE [--trace FILE] [--report FILE] [--ram-only] [--startup FILE] [--map FILE] [--fb /dev/fb0]
 *                    [--input auto|PATH] [--audio auto|off] [--audio-device /dev/snd/pcmC0D0p] [--input-settle-ms 400] [--watchdog-ms 30000]
 *   ccs-player-armv7 --card FILE [--map FILE] [--fb /dev/fb0] [--card-ms N]       a text file on the TV until A is pressed (reports)
 *   test seams (host tests only, never on the console): --fb-file PATH --fb-geometry SPEC, --input PATH (a file or FIFO of input events),
 *   --audio-file PATH (what the sound device would get, paced by the clock), --test-dump-ppm PATH (the last frame at exit),
 *   --home-pass-delay-ms N (each pass of the HOME's loop N ms slower, as on a slow console), --home-draw-every-pass (the HOME as before K2 v3),
 *   --home-check-titles (every title-only picture compared with a full one of the same moment) */
#define _GNU_SOURCE
#include "host.h"
#include "../../console-simulator/core/devio.h"
#include "../../console-simulator/core/home.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdarg.h>
#include <errno.h>
#include <fcntl.h>
#include <unistd.h>
#include <signal.h>
#include <poll.h>
#include <limits.h>
#include <sys/statfs.h>
#include <sys/resource.h>
#include <pthread.h>

#define TMPFS_MAGIC_ 0x01021994
#define RAMFS_MAGIC_ 0x858458f6
#define RING_FRAMES 32768u          /* about 680 ms at 48 kHz: the sound waiting for the device (the HOME Music's lead fits in it) */
#define ROOTS 12
static const int GA_OF[B_COUNT] = { GA_UP, GA_DOWN, GA_LEFT, GA_RIGHT, GA_A, GA_B, GA_START, GA_SELECT };   /* button (ccs_sim.h) -> NES action */

typedef struct { uint64_t n; double sum, max; } acc;
static void acc_add(acc *a, double v) { a->n++; a->sum += v; if (v > a->max) a->max = v; }
struct host {
    FILE *trace; char report[1100], state_dir[1100], assign_path[1100], dump[1100];
    /* picture */
    dio_fb fb; framebuffer last; uint8_t *prev; int have_prev, tv_aspect, integer_scale, page_kind[2], rx, ry, rw, rh, rsw, rsh, skipped_last; uint32_t *lut; uint16_t xmap[CCS_FB_W];
    /* controls */
    dio_map map; dio_inputs in; int mask[DIO_MAX_DEV][B_COUNT], settled[DIO_MAX_DEV], port[DIO_MAX_DEV], hit_dev, hit_act, settle_ms, input_restarted;
    uint8_t tap[DIO_MAX_DEV][B_COUNT];   /* the presses since the last pump: reported down for that pump even when already released (K2-F1), and counted (K2 v3) */
    uint8_t nq, q[GP_PADQ][2];            /* and in order: device, button (K2 v3) */
    char pick[GP_PLAYERS][128]; int swapped; uint64_t presses, events, settle_ignored, disconnects, dropped, latched, multi;
    /* sound */
    int au_on, au_paused, au_off, au_failed; unsigned rate; dio_pcm pcm; char au_dev[300]; int16_t *ring; uint32_t ring_head, ring_len;
    FILE *sink; uint64_t sink_t0, sink_base, sink_written, sink_dry; uint64_t au_written, au_overflow; int au_started;
    pthread_mutex_t au_mx; pthread_t au_thr; volatile int au_thr_on; int au_thr_seen; gp_audio_fill src; void *src_ctx; size_t src_lead; uint64_t src_frames;   /* src: a source the thread produces (K3D1-F2) */   /* the ring and the device: the session's thread and the feeding thread, one at a time */
    /* startup image */
    int splash, splash_min; uint64_t splash_at; char splash_id[64], splash_src[16], splash_why[600]; uint64_t splash_visible;
    /* the RAM test */
    int ram_only; char root[ROOTS][1100]; int nroot;
    /* measurements */
    acc emu_gap, conv, pan, canvas, region; uint64_t game_frames, game_skipped, canvas_frames, canvas_same, region_frames, t_open, t_stats, last_present; long rss_max; int fell_back, watchdog_s;
};
static host *g_h;             /* the one host (signals, the write check) */
static volatile sig_atomic_t g_stop;
static int g_trace_fd = -1;
static void on_signal(int s) { if (g_stop) _exit(128 + s); g_stop = s; }   /* a second signal forces the exit */
static void on_watchdog(int s) { static const char m[] = "{\"ev\":\"watchdog\",\"error\":\"a step took too long; the player stops (the launcher shows the stop card and returns to Nintendo's HOME)\"}\n";
    if (g_trace_fd >= 0) { ssize_t r = write(g_trace_fd, m, sizeof m - 1); (void)r; } ssize_t r = write(2, m, sizeof m - 1); (void)r; (void)s; _exit(70); }

void host_trace(host *H, const char *fmt, ...) { if (!H || !H->trace) return; char b[2048]; va_list a; va_start(a, fmt); vsnprintf(b, sizeof b, fmt, a); va_end(a);
    fprintf(H->trace, "{\"t\":%llu,%s\n", (unsigned long long)(dio_mono_ms() - H->t_open), b[0] == '{' ? b + 1 : b); fflush(H->trace); }
static const char *arg(int argc, char **argv, int *i, const char *name) { size_t n = strlen(name);
    if (!strncmp(argv[*i], name, n) && argv[*i][n] == '=') return argv[*i] + n + 1; if (!strcmp(argv[*i], name) && *i + 1 < argc) return argv[++*i]; return NULL; }

/* ---------------- the RAM test: every place this session writes is in RAM ---------------- */
static void dir_of(const char *path, char *out, size_t n) { snprintf(out, n, "%s", path); char *s = strrchr(out, '/'); if (s && s != out) *s = 0; else if (s) s[1] = 0; else snprintf(out, n, "."); }
static int in_root(host *H, const char *real) { for (int i = 0; i < H->nroot; i++) { size_t L = strlen(H->root[i]); if (!strncmp(real, H->root[i], L) && (real[L] == 0 || real[L] == '/')) return 1; } return 0; }
static int ram_fs(const char *dir) { struct statfs sf; if (statfs(dir, &sf)) return 0; unsigned long t = (unsigned long)sf.f_type; return t == TMPFS_MAGIC_ || t == RAMFS_MAGIC_; }
static int ram_obj(int fd, int kind) { (void)kind; struct statfs sf; if (fstatfs(fd, &sf)) return -1; unsigned long t = (unsigned long)sf.f_type; return t == TMPFS_MAGIC_ || t == RAMFS_MAGIC_ ? 0 : -1; }   /* the object opened, not its name */
static void on_refused(const char *path, int kind, const char *why) { host *H = g_h; char e[1300], w[400]; json_escape(path ? path : "", e, sizeof e); json_escape(why ? why : "", w, sizeof w);
    host_trace(H, "{\"ev\":\"%s\",\"kind\":%d,\"path\":%s,\"why\":%s}", kind == 4 ? "link_not_followed" : "write_refused", kind, e, w); }
static void unconfine(void) { gp_write_check = NULL; gp_object_check = NULL; gp_refused = NULL; gp_confine_end(); }
static int add_root(host *H, const char *label, const char *dir, char *why, size_t n) {
    char e[500], fs[40], real[PATH_MAX]; if (!dir || !*dir) return 0;
    if (dio_ram_dir(dir, 0, e, sizeof e, fs, sizeof fs)) { snprintf(why, n, "%s: %s", label, e); return -1; }
    if (!realpath(dir, real) || !ram_fs(real)) { snprintf(why, n, "%s: %s could not be resolved to a folder in RAM", label, dir); return -1; }
    if (strcmp(real, dir)) { snprintf(why, n, "%s: %s is a link (to %s); a RAM-only session writes only into real folders in RAM", label, dir, real); return -1; }
    if (gp_confine_add(real)) { snprintf(why, n, "%s: %s could not be opened as a real folder in RAM", label, dir); return -1; }   /* every later write starts from this handle */
    if (H->nroot < ROOTS) snprintf(H->root[H->nroot++], sizeof H->root[0], "%s", real); host_trace(H, "{\"ev\":\"ram_dir\",\"what\":\"%s\",\"fs\":\"%s\"}", label, fs); return 0; }
/* gp_util.c asks before every write, folder, rename or removal: the path must lexically stay in a checked folder, and the folder it is
   written in must still resolve there and be in RAM */
static int write_check(const char *path, int kind) {
    host *H = g_h; if (!H || !H->ram_only) return 0; char d[1100], real[PATH_MAX];
    int bad = !path || path[0] != '/' || strstr(path, "/../") || strstr(path, "/./") || (strlen(path) >= 3 && !strcmp(path + strlen(path) - 3, "/.."));
    if (!bad && !in_root(H, path)) bad = 1;
    if (!bad) { dir_of(path, d, sizeof d); if (kind == 1) snprintf(d, sizeof d, "%s", path);   /* a folder: its nearest existing part */
        while (!bad && access(d, F_OK)) { char *s = strrchr(d, '/'); if (!s || s == d) { bad = 1; break; } *s = 0; }
        if (!bad && (!realpath(d, real) || !in_root(H, real) || !ram_fs(real))) bad = 1; }
    if (bad) { char e[1300]; json_escape(path ? path : "", e, sizeof e); host_trace(H, "{\"ev\":\"write_refused\",\"kind\":%d,\"path\":%s,\"why\":\"outside the RAM folders of this session\"}", kind, e); }
    return bad ? -1 : 0; }
static int ram_check_session(host *H, const host_config *c, char *why, size_t n) {
    uint8_t *b; size_t len; if (!c->session_path || gp_read_file(c->session_path, &b, &len, 32u << 20)) { snprintf(why, n, "the session file cannot be read for the RAM check"); return -1; }
    char err[160]; jval *j = json_parse((const char *)b, len, err, sizeof err); free(b); if (!j || j->t != J_OBJ) { json_free(j); snprintf(why, n, "the session file is not valid"); return -1; }
    const char *K[] = { "saves_root", "scratch_dir", "game_settings_dir" }; int r = 0; char d[1100];
    for (size_t i = 0; i < 3 && !r; i++) { const jval *v = json_get(j, K[i]); if (v && v->t == J_STR) r = add_root(H, K[i], v->s, why, n); }
    const char *F[] = { "result_path", "display_path" };   /* settings_path is only read here (the package's picture settings): not a place to write */
    for (size_t i = 0; i < 2 && !r; i++) { const jval *v = json_get(j, F[i]); if (v && v->t == J_STR && v->s[0]) { dir_of(v->s, d, sizeof d); r = add_root(H, F[i], d, why, n); } }
    const jval *h = json_get(j, "home"); if (!r && h && h->t == J_OBJ) { const jval *s = json_get(h, "state_dir"); if (s && s->t == J_STR) r = add_root(H, "home.state_dir", s->s, why, n);
        const jval *mu = json_get(h, "music"); const jval *p = mu && mu->t == J_OBJ ? json_get(mu, "pref") : NULL; if (!r && p && p->t == J_STR && p->s[0]) { dir_of(p->s, d, sizeof d); r = add_root(H, "home.music.pref", d, why, n); } }
    const jval *sv = json_get(j, "saves_root"); if (!r && !(sv && sv->t == J_STR && sv->s[0])) { snprintf(why, n, "the session names no folder for saved games"); r = -1; }
    json_free(j); return r; }

/* ---------------- picture ---------------- */
static void lut_build(host *H) { if (H->lut) return; H->lut = malloc(65536 * sizeof *H->lut); if (!H->lut) return;
    for (uint32_t v = 0; v < 65536; v++) { uint8_t r = (uint8_t)((v >> 11) << 3), g = (uint8_t)(((v >> 5) & 63) << 2), b = (uint8_t)((v & 31) << 3); H->lut[v] = dio_fb_pixel(&H->fb, (uint8_t)(r | r >> 5), (uint8_t)(g | g >> 6), (uint8_t)(b | b >> 5)); } }
static void game_rect(host *H, int w, int h) {   /* the Windows player's rectangle (host_win.c dest_rect) in the console's 1280 x 720 */
    double par = H->tv_aspect ? 8.0 / 7.0 : 1.0, sw = w * par, sh = h; int dw, dh, cw = CCS_FB_W, ch = CCS_FB_H;
    if (H->integer_scale) { int k = (int)(ch / sh); if ((int)(k * sw) > cw) k = (int)(cw / sw); if (k < 1) k = 1; dw = (int)(sw * k + 0.5); dh = (int)(sh * k); if (dw > cw || dh > ch) { dw = cw; dh = ch; } }
    else { double s = (double)cw / sw < (double)ch / sh ? (double)cw / sw : (double)ch / sh; dw = (int)(sw * s + 0.5); dh = (int)(sh * s + 0.5); }
    int x = (cw - dw) / 2, y = (ch - dh) / 2;
    if (x != H->rx || y != H->ry || dw != H->rw || dh != H->rh || w != H->rsw || h != H->rsh) { H->rx = x; H->ry = y; H->rw = dw; H->rh = dh; H->rsw = w; H->rsh = h; H->page_kind[0] = H->page_kind[1] = 0;
        for (int i = 0; i < dw; i++) H->xmap[i] = (uint16_t)((long)i * w / dw); host_trace(H, "{\"ev\":\"game_rect\",\"x\":%d,\"y\":%d,\"w\":%d,\"h\":%d,\"src\":[%d,%d],\"tv\":%d,\"integer\":%d}", x, y, dw, dh, w, h, H->tv_aspect, H->integer_scale); } }
static void fell(host *H, int f, const char *why) { if (!f) return; H->fell_back = 1; char e[400]; json_escape(why, e, sizeof e); host_trace(H, "{\"ev\":\"page_flip_fallback\",\"reason\":%s,\"after_pans\":%d}", e, H->fb.pans); }
static size_t au_queued(host *H);
void host_present_nes(host *H, const uint16_t *px, int w, int h) {
    uint64_t t0 = dio_mono_us(); if (H->last_present) acc_add(&H->emu_gap, (double)(t0 - H->last_present) / 1000.0);
    pthread_mutex_lock(&H->au_mx); int behind = H->au_on && !H->au_paused && H->rate && au_queued(H) < H->rate / 60; pthread_mutex_unlock(&H->au_mx);
    if (behind && !H->skipped_last) { H->skipped_last = 1; H->game_skipped++; H->last_present = t0; return; }   /* behind its sound: this picture is skipped */
    H->skipped_last = 0; if (!H->fb.map || w <= 0 || h <= 0 || w > 256 || h > 240) return; lut_build(H); if (!H->lut) return;
    game_rect(H, w, h); uint32_t y0 = dio_fb_back_row(&H->fb); int page = y0 ? 1 : 0; size_t line = H->fb.fix.line_length;
    if (H->page_kind[page] != 1) { uint32_t black = dio_fb_pixel(&H->fb, 0, 0, 0);   /* black around the picture, once per page (opaque where the format has alpha) */
        for (int y = 0; y < CCS_FB_H; y++) { uint32_t *r = (uint32_t *)(void *)(H->fb.mem + (size_t)(y0 + (uint32_t)y) * line); if (!black) memset(r, 0, (size_t)CCS_FB_W * 4); else for (int x = 0; x < CCS_FB_W; x++) r[x] = black; }
        H->page_kind[page] = 1; }
    int last_sy = -1; uint32_t *prow = NULL;
    for (int dy = 0; dy < H->rh; dy++) {
        int sy = (int)((long)dy * h / H->rh); uint32_t *d = (uint32_t *)(void *)(H->fb.mem + (size_t)(y0 + (uint32_t)(H->ry + dy)) * line) + H->rx;
        if (sy == last_sy && prow) { memcpy(d, prow, (size_t)H->rw * 4); continue; }   /* the same source row: copied */
        const uint16_t *s = px + (size_t)sy * 256; for (int x = 0; x < H->rw; x++) d[x] = H->lut[s[H->xmap[x]]]; last_sy = sy; prow = d; }
    uint64_t t1 = dio_mono_us(); int f = 0; char why[200] = ""; dio_fb_show(&H->fb, y0, &f, why, sizeof why); fell(H, f, why); uint64_t t2 = dio_mono_us();
    acc_add(&H->conv, (double)(t1 - t0) / 1000.0); acc_add(&H->pan, (double)(t2 - t1) / 1000.0); H->game_frames++; H->last_present = t2; H->have_prev = 0; }
static void splash_wait(host *H);
void host_present_canvas(host *H, const framebuffer *fb) {
    if (H->splash) splash_wait(H);
    if (!H->fb.map || fb->w != CCS_FB_W || fb->h != CCS_FB_H) return; size_t nb = (size_t)fb->w * fb->h * 3;
    if (H->have_prev && H->prev && !memcmp(H->prev, fb->rgb, nb)) { H->canvas_same++; return; }   /* the same picture: nothing to show */
    uint64_t t0 = dio_mono_us(); int f = 0; char why[200] = ""; dio_fb_present(&H->fb, fb, &f, why, sizeof why); fell(H, f, why);
    H->page_kind[H->fb.shown_yoff ? 1 : 0] = 2; if (H->prev) { memcpy(H->prev, fb->rgb, nb); H->have_prev = 1; }
    acc_add(&H->canvas, (double)(dio_mono_us() - t0) / 1000.0); H->canvas_frames++; H->last_present = 0; }
/* K3D1-F3: one changed rectangle (the scrolling title) shown alone: kept in the last picture and written into the page on the screen */
static void present_region(host *H, const framebuffer *fb, int x, int y, int w, int h) {
    if (H->splash || !H->fb.map || !H->have_prev || !H->prev || fb->w != CCS_FB_W || fb->h != CCS_FB_H) { host_present_canvas(H, fb); return; }
    if (x < 0 || y < 0 || w <= 0 || h <= 0 || x + w > fb->w || y + h > fb->h) { host_present_canvas(H, fb); return; }
    uint64_t t0 = dio_mono_us();
    for (int yy = y; yy < y + h; yy++) memcpy(H->prev + ((size_t)yy * fb->w + (size_t)x) * 3, fb->rgb + ((size_t)yy * fb->w + (size_t)x) * 3, (size_t)w * 3);
    dio_fb_present_rect(&H->fb, fb, x, y, w, h); acc_add(&H->region, (double)(dio_mono_us() - t0) / 1000.0); H->region_frames++; }
int host_canvas_scale(void) { return 1; }   /* the console's own resolution */
/* K2 (2026-11-09): at the console's 600 MHz, four cover decodes in one pass of the HOME's loop made passes long enough that taps fell between them
   and HOME Music, kept 130 ms ahead, ran dry (594 underruns). One cover per pass (the focused one is always drawn). K2 v3: still 1,298
   underruns at 300 ms ahead, the device being fed only between passes; now a thread feeds it and the song is kept 600 ms ahead, longer than
   any pass that draws */
void host_home_pace(int *cover_budget, int *music_ahead) { *cover_budget = 1; *music_ahead = 28800; }
void host_set_picture(host *H, int tv_aspect, int integer_scale) { if (!H) return; H->tv_aspect = tv_aspect; H->integer_scale = integer_scale; H->rw = 0; }

/* ---------------- the startup image ---------------- */
static uint32_t rnd32(void) { uint32_t r = 0; int fd = open("/dev/urandom", O_RDONLY | O_CLOEXEC); if (fd >= 0) { if (read(fd, &r, sizeof r) != (ssize_t)sizeof r) r = 0; close(fd); }
    if (!r) { uint64_t t = dio_mono_us() ^ ((uint64_t)getpid() << 32); r = (uint32_t)(t ^ (t >> 29)) * 2654435761u; } return r; }   /* the kernel's randomness; the clock only when it cannot be read */
static void card_text(framebuffer *fb, const char *title, const char *const *lines, int n) {   /* a plain card: dark, a red rule, uppercase text */
    fb_clear(fb, 16, 16, 24); fb_rect(fb, 80, 128, 1120, 4, 214, 40, 40); fb_text(fb, 80, 72, 5, title, 240, 240, 240);
    int y0 = n > 14 ? 152 : 168, dy = n > 14 ? 27 : 34;   /* a longer report closer together, still above PRESS A TO GO ON */
    for (int i = 0; i < n; i++) fb_text(fb, 80, y0 + i * dy, 3, lines[i], 220, 220, 220); }
static int splash_show(host *H, const char *path, const char *want_sha, char *why, size_t n) {
    uint8_t *b; size_t len; char hex[65]; if (gp_read_file(path, &b, &len, 16u << 20)) { snprintf(why, n, "the file cannot be read"); return -1; }
    gp_sha256(b, len, hex); if (want_sha && *want_sha && strcmp(hex, want_sha)) { free(b); snprintf(why, n, "the file is not the image the package lists (its content differs)"); return -1; }
    image im; char e[200]; int r = img_decode(b, len, &im, 0, e, sizeof e); free(b); if (r) { snprintf(why, n, "the image cannot be decoded (%s)", e); return -1; }
    if (im.w != CCS_FB_W || im.h != CCS_FB_H || im.ch != 3) { snprintf(why, n, "the image is %dx%d, not 1280x720", im.w, im.h); img_free(&im); return -1; }
    framebuffer f = { CCS_FB_W, CCS_FB_H, im.px }; int fl = 0; char w2[200] = ""; dio_fb_present(&H->fb, &f, &fl, w2, sizeof w2); fell(H, fl, w2); img_free(&im); return 0; }
static void splash_start(host *H, const char *cfg_path) {
    char why[300] = ""; H->splash_min = 5000; uint8_t *b; size_t len; jval *j = NULL; char err[160];
    if (gp_read_file(cfg_path, &b, &len, 1u << 20)) snprintf(why, sizeof why, "the startup settings cannot be read");
    else { j = json_parse((const char *)b, len, err, sizeof err); free(b); if (!j || j->t != J_OBJ || !json_get(j, "schema") || json_get(j, "schema")->t != J_STR || strcmp(json_get(j, "schema")->s, "ccs-startup/1")) { snprintf(why, sizeof why, "the startup settings are not valid"); json_free(j); j = NULL; } }
    char dir[1100]; dir_of(cfg_path, dir, sizeof dir); const jval *ims = j ? json_get(j, "images") : NULL; int ni = ims && ims->t == J_ARR ? (int)ims->n : 0; if (ni > 16) ni = 16;
    const jval *mv = j ? json_get(j, "min_visible_ms") : NULL; if (mv && mv->t == J_NUM && mv->num >= 5000 && mv->num <= 60000) H->splash_min = (int)mv->num;   /* never less than five seconds */
    const jval *cv = j ? json_get(j, "choice") : NULL; const char *choice = cv && cv->t == J_STR ? cv->s : "random"; int pick = -1, fixed = 0;
    for (int i = 0; i < ni; i++) { const jval *id = json_get(ims->items[i], "id"); if (id && id->t == J_STR && !strcmp(id->s, choice)) { pick = i; fixed = 1; } }
    if (j && strcmp(choice, "random") && pick < 0) { size_t L = strlen(why); snprintf(why + L, sizeof why - L, "%sthe chosen image \"%.60s\" is not one of the five", L ? "; " : "", choice); }
    if (pick < 0 && ni) pick = (int)(rnd32() % (uint32_t)ni);
    snprintf(H->splash_src, sizeof H->splash_src, "%s", fixed ? "fixed" : "random"); size_t wl = 0; H->splash_why[0] = 0;
    if (why[0]) wl += (size_t)snprintf(H->splash_why + wl, sizeof H->splash_why - wl, "%s", why);
    for (int k = 0; k < ni; k++) { int i = (pick + k) % ni; const jval *x = ims->items[i]; const jval *id = json_get(x, "id"), *f = json_get(x, "file"), *s = json_get(x, "sha256");
        if (!id || id->t != J_STR || !f || f->t != J_STR || strchr(f->s, '/') || strstr(f->s, "..")) continue; char p[1300], w[300]; snprintf(p, sizeof p, "%s/%s", dir, f->s);
        if (!splash_show(H, p, s && s->t == J_STR ? s->s : "", w, sizeof w)) { snprintf(H->splash_id, sizeof H->splash_id, "%s", id->s); if (k) snprintf(H->splash_src, sizeof H->splash_src, "fallback"); break; }
        if (wl < sizeof H->splash_why - 2) wl += (size_t)snprintf(H->splash_why + wl, sizeof H->splash_why - wl, "%s%s: %s", wl ? "; " : "", id->s, w); }
    if (!H->splash_id[0]) {   /* no image could be shown: a plain card says so (never a broken picture) */
        framebuffer fb; if (!fb_init(&fb, CCS_FB_W, CCS_FB_H)) { const char *L[] = { "THE STARTUP IMAGE COULD NOT BE SHOWN.", "THE HOME STARTS IN A MOMENT." }; card_text(&fb, "CLASSIC CART STUDIO", L, 2); int fl = 0; char w2[200] = ""; dio_fb_present(&H->fb, &fb, &fl, w2, sizeof w2); fell(H, fl, w2); fb_free(&fb); }
        snprintf(H->splash_id, sizeof H->splash_id, "none"); snprintf(H->splash_src, sizeof H->splash_src, "fallback"); }
    H->splash_at = dio_mono_ms(); H->splash = 1; json_free(j);
    char e[1300], ch[200], id[200]; json_escape(H->splash_why, e, sizeof e); json_escape(choice, ch, sizeof ch); json_escape(H->splash_id, id, sizeof id);
    host_trace(H, "{\"ev\":\"startup_shown\",\"id\":%s,\"source\":\"%s\",\"choice\":%s,\"min_ms\":%d,\"problems\":%s}", id, H->splash_src, ch, H->splash_min, e); }
static void input_pump(host *H, gp_pad *pad);
static void au_pump(host *H);
static void splash_wait(host *H) {   /* the HOME is ready: it shows once the image has been on the screen for its minimum, counted from when it was shown */
    uint64_t ready = dio_mono_ms(); while (!g_stop && dio_mono_ms() < H->splash_at + (uint64_t)H->splash_min) { if (H->watchdog_s) alarm((unsigned)H->watchdog_s); input_pump(H, NULL); gp_sleep_ms(20); }
    pthread_mutex_lock(&H->au_mx); H->splash = 0; pthread_mutex_unlock(&H->au_mx); H->splash_visible = dio_mono_ms() - H->splash_at;
    host_trace(H, "{\"ev\":\"startup_released\",\"id\":\"%s\",\"visible_ms\":%llu,\"home_ready_after_ms\":%llu}", H->splash_id, (unsigned long long)H->splash_visible, (unsigned long long)(ready - H->splash_at));
    for (int i = 0; i < H->in.n; i++) { H->in.d[i].settle_until = dio_mono_ms() + (uint64_t)H->settle_ms; H->settled[i] = 0; memset(H->tap[i], 0, sizeof H->tap[i]); } H->nq = 0;   /* the HOME reads the controllers from now on: they settle again */
    pthread_mutex_lock(&H->au_mx); H->input_restarted = 1; if (H->sink && H->au_on) { H->sink_t0 = dio_mono_us(); H->sink_base = H->sink_written; }   /* the test sink starts its clock with the first sound, as a device would */
    au_pump(H); pthread_mutex_unlock(&H->au_mx); }

/* ---------------- controls ---------------- */
static int port_of(const char *name) { const char *k = strstr(name, "controller"); if (!k) return 0; int p = atoi(k + 10); return p >= 1 && p <= 4 ? p : 0; }
static void seen_dev(void *ctx, int kind, const char *path, const char *name, int mapped) { host *H = ctx; if (kind != 1) return; (void)mapped; char a[160], b[300]; json_escape(path, a, sizeof a); json_escape(name, b, sizeof b);
    int i = H->in.n - 1; memset(H->mask[i], 0, sizeof H->mask[i]); memset(H->tap[i], 0, sizeof H->tap[i]); H->settled[i] = 0; H->port[i] = H->in.fixed ? i + 1 : port_of(name);   /* a test stream's port is its place in --input */ host_trace(H, "{\"ev\":\"pad_connected\",\"path\":%s,\"name\":%s,\"port\":%d}", a, b, H->port[i]); }
static void dev_lost(host *H, int i, const char *why) { char a[160]; json_escape(H->in.d[i].path, a, sizeof a); host_trace(H, "{\"ev\":\"pad_disconnected\",\"path\":%s,\"why\":\"%s\"}", a, why); H->disconnects++;
    int last = H->in.n - 1; memcpy(H->mask[i], H->mask[last], sizeof H->mask[i]); memcpy(H->tap[i], H->tap[last], sizeof H->tap[i]); H->settled[i] = H->settled[last]; H->port[i] = H->port[last]; dio_dev_close(&H->in, i); H->nq = 0; }   /* what it held is released */
static void on_event(void *ctx, dio_dev *d, int type, int code, int value) {
    host *H = ctx; int i = (int)(d - H->in.d); H->events++;
    if (type == DIO_EV_SYN && code == DIO_SYN_DROPPED) { memset(d->down, 0, sizeof d->down); memset(H->mask[i], 0, sizeof H->mask[i]); memset(H->tap[i], 0, sizeof H->tap[i]); H->dropped++; H->nq = 0; host_trace(H, "{\"ev\":\"input_dropped\",\"note\":\"the kernel dropped events: this controller's buttons are released\"}"); return; }
    if (type != DIO_EV_KEY || code < 0 || code > DIO_KEY_MAX || H->map.key[code] < 0 || value == 2) return;   /* value 2: the kernel's repeat (the HOME repeats itself) */
    button b = (button)H->map.key[code];
    if (!H->settled[i]) { d->down[b] = value ? 1 : 0; d->settle_ignored++; H->settle_ignored++; return; }   /* settling: state only, never a press */
    if (value && !d->down[b]) { d->down[b] = 1; if (H->tap[i][b] < 255) H->tap[i][b]++; if (H->nq < GP_PADQ) { H->q[H->nq][0] = (uint8_t)i; H->q[H->nq][1] = (uint8_t)b; H->nq++; } H->presses++; if (H->hit_dev < 0) { H->hit_dev = i; H->hit_act = GA_OF[b]; } }
    else if (!value) { d->down[b] = 0; H->mask[i][b] = 0; } }   /* a release ends a hold that began during settling too */
static void who_plays(host *H, int *who);
static void input_pump(host *H, gp_pad *pad) {
    uint64_t now = dio_mono_ms();
    for (int i = 0; i < H->in.n; i++) if (!H->settled[i] && now >= H->in.d[i].settle_until) {   /* decided before reading: a press after the window is a press */   /* settled: a button still down counts only after it is released */
        H->settled[i] = 1; char held[120] = ""; size_t L = 0; for (int b = 0; b < B_COUNT; b++) { H->mask[i][b] = H->in.d[i].down[b]; if (H->in.d[i].down[b] && L < sizeof held - 12) L += (size_t)snprintf(held + L, sizeof held - L, "%s\"%s\"", L ? "," : "", button_name((button)b)); }
        host_trace(H, "{\"ev\":\"input_ready\",\"port\":%d,\"settle_ms\":%d,\"ignored_events\":%d,\"held\":[%s],\"after_home\":%d}", H->port[i], H->settle_ms, H->in.d[i].settle_ignored, held, H->input_restarted); }
    for (int i = 0; i < H->in.n; i++) if (dio_dev_read(&H->in.d[i], on_event, H) < 0) { dev_lost(H, i, errno == ENODEV ? "removed" : "end of input"); i--; }
    if (!H->in.fixed && H->in.n < 2 && dio_mono_us() >= H->in.next_scan_us) { H->in.next_scan_us = dio_mono_us() + 1000000u; dio_inputs_scan(&H->in, &H->map, 0, (uint64_t)H->settle_ms, seen_dev, H); }   /* a controller plugged in later */
    if (!pad) return; int who[DIO_MAX_DEV]; who_plays(H, who);
    /* down now, or pressed since the last pump (K2-F1: a tap shorter than one pass of a slow loop was never seen): the session gets it down
       for this pump and released at the next; and every press in order (K2 v3: two taps within one pass of the HOME counted once, and a
       tap right after one the session still held down was lost) */
    for (int i = 0; i < H->in.n; i++) if (who[i] >= 0 && H->settled[i]) for (int b = 0; b < B_COUNT; b++) if ((H->in.d[i].down[b] || H->tap[i][b]) && !H->mask[i][b]) {
        pad->down[who[i]][GA_OF[b]] = 1;
        if (H->tap[i][b] > 1) { H->multi++; host_trace(H, "{\"ev\":\"taps_counted\",\"port\":%d,\"button\":\"%s\",\"taps\":%d}", H->port[i], button_name((button)b), H->tap[i][b]); }
        if (!H->in.d[i].down[b]) { H->latched++; host_trace(H, "{\"ev\":\"tap_latched\",\"port\":%d,\"button\":\"%s\"}", H->port[i], button_name((button)b)); } }
    for (int k = 0; k < H->nq; k++) { int i = H->q[k][0], b = H->q[k][1]; if (i < H->in.n && who[i] >= 0 && H->settled[i] && !H->mask[i][b] && pad->nq < GP_PADQ) pad->q[pad->nq++] = (uint8_t)(who[i] << 4 | GA_OF[b]); }
    memset(H->tap, 0, sizeof H->tap); H->nq = 0; }
static int order(host *H, int *ord) {   /* the controllers in port order (menus list them so; unknown ports after) */
    int n = H->in.n; for (int i = 0; i < n; i++) ord[i] = i;
    for (int a = 0; a < n; a++) for (int b = a + 1; b < n; b++) { int pa = H->port[ord[a]] ? H->port[ord[a]] : 99, pb = H->port[ord[b]] ? H->port[ord[b]] : 99; if (pb < pa) { int t = ord[a]; ord[a] = ord[b]; ord[b] = t; } }
    return n; }
static void who_plays(host *H, int *who) {   /* who[i] for H->in.d[i]: port 1 -> Player 1, port 2 -> Player 2, unless chosen (Controllers) */
    int ord[DIO_MAX_DEV], n = order(H, ord); for (int i = 0; i < DIO_MAX_DEV; i++) who[i] = -1;
    if (!H->pick[0][0] && !H->pick[1][0]) { int taken[GP_PLAYERS] = { 0, 0 };
        for (int k = 0; k < n; k++) { int i = ord[k], p = H->port[i] >= 1 && H->port[i] <= GP_PLAYERS ? H->port[i] - 1 : -1; if (p >= 0 && H->swapped) p = GP_PLAYERS - 1 - p; if (p >= 0 && !taken[p]) { who[i] = p; taken[p] = 1; } }
        for (int k = 0; k < n; k++) { int i = ord[k]; if (who[i] < 0) for (int p = 0; p < GP_PLAYERS; p++) if (!taken[p]) { who[i] = p; taken[p] = 1; break; } } return; }
    int pick[GP_PLAYERS], w[DIO_MAX_DEV]; for (int p = 0; p < GP_PLAYERS; p++) { pick[p] = !H->pick[p][0] ? GP_PICK_AUTO : !strcmp(H->pick[p], "none") ? GP_PICK_NONE : GP_PICK_AUTO;
        for (int k = 0; k < n; k++) if (!strcmp(H->in.d[ord[k]].name, H->pick[p])) pick[p] = k; }
    gp_assign_players(n, pick, 0, w); for (int k = 0; k < n; k++) who[ord[k]] = w[k]; }
static void assign_save(host *H) { if (!H->assign_path[0]) return; char a[300], b[300], buf[800]; json_escape(H->pick[0], a, sizeof a); json_escape(H->pick[1], b, sizeof b);
    int n = snprintf(buf, sizeof buf, "{\"schema\":\"ccs-player-assign/1\",\"p1\":%s,\"p2\":%s,\"swapped\":%d}\n", a, b, H->swapped); int r = gp_write_atomic(H->assign_path, buf, (size_t)n);
    host_trace(H, "{\"ev\":\"pad_assign_saved\",\"ok\":%d,\"p1\":%s,\"p2\":%s,\"swapped\":%d}", r == 0, a, b, H->swapped); }
static void assign_load(host *H) { uint8_t *b; size_t len; if (!H->assign_path[0] || gp_read_file(H->assign_path, &b, &len, 4096)) return; char e[80]; jval *j = json_parse((const char *)b, len, e, sizeof e); free(b);
    if (j && j->t == J_OBJ) { const jval *s = json_get(j, "schema"); if (s && s->t == J_STR && !strcmp(s->s, "ccs-player-assign/1")) { const jval *p1 = json_get(j, "p1"), *p2 = json_get(j, "p2"), *sw = json_get(j, "swapped");
        if (p1 && p1->t == J_STR) snprintf(H->pick[0], sizeof H->pick[0], "%s", p1->s); if (p2 && p2->t == J_STR) snprintf(H->pick[1], sizeof H->pick[1], "%s", p2->s); H->swapped = sw && sw->t == J_NUM && sw->num != 0; } } json_free(j); }
static void label_of(host *H, int i, char *out, size_t n) { if (H->port[i]) snprintf(out, n, "controller in port %d", H->port[i]); else snprintf(out, n, "controller"); }
int host_pads(host *H, char *out, size_t n) { int who[DIO_MAX_DEV]; who_plays(H, who); char l[GP_PLAYERS][64]; const char *ctl[GP_PLAYERS] = { NULL, NULL };
    for (int i = 0; i < H->in.n; i++) if (who[i] >= 0 && !ctl[who[i]]) { label_of(H, i, l[who[i]], sizeof l[0]); ctl[who[i]] = l[who[i]]; }
    gp_players_text(ctl, out, n); return H->in.n; }
void host_swap_pads(host *H) { if (H->pick[0][0] || H->pick[1][0]) { char t[128]; memcpy(t, H->pick[0], sizeof t); memcpy(H->pick[0], H->pick[1], sizeof t); memcpy(H->pick[1], t, sizeof t); } else H->swapped ^= 1; assign_save(H); }
int host_pad_count(host *H) { return H->in.n; }
void host_pad_info(host *H, int k, char *label, size_t n, int *player) { int ord[DIO_MAX_DEV], c = order(H, ord), who[DIO_MAX_DEV]; who_plays(H, who);
    if (k < 0 || k >= c) { snprintf(label, n, "controller"); *player = -1; return; } char l[64]; label_of(H, ord[k], l, sizeof l); snprintf(label, n, "%s", l); *player = who[ord[k]]; }
void host_pad_choose(host *H, int player, int k) {
    if (player < 0 || player >= GP_PLAYERS) return; int ord[DIO_MAX_DEV], c = order(H, ord); char name[128] = "";
    if (k == GP_PICK_NONE) snprintf(name, sizeof name, "none"); else if (k >= 0 && k < c) snprintf(name, sizeof name, "%s", H->in.d[ord[k]].name);
    if (k == GP_PICK_AUTO) { H->pick[0][0] = H->pick[1][0] = 0; H->swapped = 0; }
    else { if (name[0] && strcmp(name, "none") && !strcmp(H->pick[1 - player], name)) snprintf(H->pick[1 - player], sizeof H->pick[0], "%s", H->pick[player]); snprintf(H->pick[player], sizeof H->pick[0], "%s", name); }
    assign_save(H); }
int host_pad_pressed(host *H, int *action) { if (H->hit_dev < 0 || H->hit_dev >= H->in.n) return -1; int ord[DIO_MAX_DEV], c = order(H, ord); for (int k = 0; k < c; k++) if (ord[k] == H->hit_dev) { if (action) *action = H->hit_act; return k; } return -1; }
/* the console's controllers are known controllers: none needs (or offers) a setup */
int host_pad_list(host *H, int *ids, int max) { (void)H; (void)ids; (void)max; return 0; }
int host_pad_raw(host *H, int id, gp_raw *raw) { (void)H; (void)id; (void)raw; return 0; }
void host_pad_name(host *H, int id, char *out, size_t n) { (void)H; (void)id; snprintf(out, n, "controller"); }
int host_pad_set_map(host *H, int id, const int map[GA_COUNT]) { (void)H; (void)id; (void)map; return -1; }
int host_pad_configured(host *H, int id) { (void)H; (void)id; return 1; }

/* ---------------- sound ---------------- */
static size_t au_queued(host *H) { if (!H->au_on) return 0; uint64_t dev = 0;
    if (H->sink) { uint64_t played = H->au_paused ? H->sink_base : H->sink_base + (dio_mono_us() - H->sink_t0) * H->rate / 1000000u; if (played > H->sink_written) played = H->sink_written; dev = H->sink_written - played; }
    else dev = (uint64_t)dio_pcm_delay(&H->pcm); return (size_t)(dev + H->ring_len); }
static int dev_write(host *H, const int16_t *b, uint32_t frames) {
    if (H->sink) { uint64_t played = H->sink_base + (dio_mono_us() - H->sink_t0) * H->rate / 1000000u, due = played + H->rate / 10; if (H->sink_written >= due) return 0;   /* a device buffer of 100 ms */
        if (H->au_started && played > H->sink_written + H->rate / 50) H->sink_dry++;   /* a device would have run dry (more than 20 ms): an underrun */
        uint64_t k = due - H->sink_written; if (k > frames) k = frames; if (fwrite(b, 4, (size_t)k, H->sink) != (size_t)k) return -1; H->sink_written += k; return (int)k; }
    return dio_pcm_write(&H->pcm, b, frames); }
static void au_pump(host *H) { if (!H->au_on || H->au_paused || H->splash) return;
    while (H->ring_len) { uint32_t n = H->ring_len, tail = RING_FRAMES - H->ring_head; if (n > tail) n = tail;
        int w = dev_write(H, H->ring + (size_t)H->ring_head * 2, n);
        if (w < 0) { char e[300]; json_escape(strerror(errno), e, sizeof e); host_trace(H, "{\"ev\":\"audio_unavailable\",\"error\":%s}", e); H->au_failed = 1; dio_pcm_close(&H->pcm); H->au_on = 0; H->ring_len = 0; return; }
        if (w == 0) break; H->ring_head = (H->ring_head + (uint32_t)w) % RING_FRAMES; H->ring_len -= (uint32_t)w; H->au_written += (uint64_t)w;
        if (!H->au_started) { H->au_started = 1; host_trace(H, "{\"ev\":\"audio_started\",\"rate\":%u}", H->rate); } } }   /* the first sound reaches the device (after the startup image) */
static void proc_note(host *H, const char *path, const char *ev) { FILE *f = fopen(path, "r"); char b[900]; size_t n = f ? fread(b, 1, sizeof b - 1, f) : 0; if (f) fclose(f); b[n] = 0; char e[2000]; json_escape(b, e, sizeof e); host_trace(H, "{\"ev\":\"%s\",\"text\":%s}", ev, e); }
static int au_open_l(host *H, int rate) {
    if (H->au_off || H->au_failed || rate <= 0) { host_trace(H, "{\"ev\":\"audio_open\",\"rate\":%d,\"device\":false,\"why\":\"%s\"}", rate, H->au_off ? "--audio off" : "the sound device failed earlier"); return -1; }
    H->rate = (unsigned)rate; H->ring_head = H->ring_len = 0; H->au_paused = 0;
    if (H->sink) { H->sink_t0 = dio_mono_us(); H->sink_base = H->sink_written; H->au_on = 1; host_trace(H, "{\"ev\":\"audio_open\",\"rate\":%d,\"device\":false,\"sink\":true,\"note\":\"test only: what the device would get, paced by the clock\"}", rate); return 0; }
    static int noted; if (!noted) { noted = 1; proc_note(H, "/proc/asound/cards", "sound_cards"); proc_note(H, "/proc/asound/pcm", "sound_pcm"); }
    char why[300]; if (dio_pcm_open(&H->pcm, H->au_dev, (unsigned)rate, why, sizeof why)) { char e[600]; json_escape(why, e, sizeof e); host_trace(H, "{\"ev\":\"audio_unavailable\",\"rate\":%d,\"error\":%s}", rate, e); H->au_failed = 1; return -1; }
    H->au_on = 1; host_trace(H, "{\"ev\":\"audio_open\",\"rate\":%d,\"device\":true,\"path\":\"%s\",\"buffer\":%lu,\"period\":%lu}", rate, H->au_dev, H->pcm.buffer, H->pcm.period); return 0; }
int host_audio_open(host *H, int rate) { pthread_mutex_lock(&H->au_mx); int r = au_open_l(H, rate); pthread_mutex_unlock(&H->au_mx); return r; }
static void ring_put(host *H, const int16_t *st, size_t frames) {   /* (the lock is held) */
    for (size_t i = 0; i < frames; i++) { if (H->ring_len >= RING_FRAMES) { H->au_overflow += frames - i; break; }
        uint32_t at = (H->ring_head + H->ring_len) % RING_FRAMES; H->ring[at * 2] = st[i * 2]; H->ring[at * 2 + 1] = st[i * 2 + 1]; H->ring_len++; } }
void host_audio_push(host *H, const int16_t *st, size_t frames) {
    pthread_mutex_lock(&H->au_mx); if (H->au_on) { ring_put(H, st, frames); au_pump(H); } pthread_mutex_unlock(&H->au_mx); }
/* K3D1-F2: the source the session handed over (HOME Music) is read here, by whichever thread holds the lock, up to its lead: the song goes on
   while the session's thread is busy (a slow picture), not only what was already queued */
static void au_produce(host *H) {   /* (the lock is held) */
    if (!H->src || !H->au_on || H->au_paused || H->splash) return;
    int16_t b[1024 * 2];
    for (int guard = 0; guard < 64 && au_queued(H) < H->src_lead && H->ring_len + 1024 <= RING_FRAMES; guard++) { uint32_t n = H->src(H->src_ctx, b, 1024); if (!n) break; ring_put(H, b, n); H->src_frames += n; }
    au_pump(H); }
static int au_source(host *H, gp_audio_fill fill, void *ctx, size_t lead) {
    pthread_mutex_lock(&H->au_mx); int was = H->src != NULL; H->src = fill; H->src_ctx = ctx; H->src_lead = lead < RING_FRAMES ? lead : RING_FRAMES - 1024; if (fill) au_produce(H); pthread_mutex_unlock(&H->au_mx);
    if (was != (fill != NULL)) host_trace(H, "{\"ev\":\"audio_source\",\"thread\":%s,\"lead\":%lu}", fill ? "true" : "false", (unsigned long)(fill ? H->src_lead : 0));
    return 0; }
static void au_pause_l(host *H, int paused) {
    if (!H->au_on || paused == H->au_paused) return; H->au_paused = paused;
    if (paused) { uint64_t drop = H->ring_len; H->ring_len = 0;   /* silent at once: what was not heard is dropped (the session counted it before) */
        if (H->sink) { uint64_t played = H->sink_base + (dio_mono_us() - H->sink_t0) * H->rate / 1000000u; if (played < H->sink_written) { drop += H->sink_written - played; fflush(H->sink); if (fseek(H->sink, (long)(played * 4), SEEK_SET)) { } H->sink_written = played; } H->sink_base = H->sink_written; }
        else { drop += (uint64_t)dio_pcm_delay(&H->pcm); dio_pcm_drop(&H->pcm); }
        host_trace(H, "{\"ev\":\"audio_pause\",\"dropped\":%llu}", (unsigned long long)drop); }
    else { if (H->sink) H->sink_t0 = dio_mono_us(); else dio_pcm_prepare(&H->pcm); host_trace(H, "{\"ev\":\"audio_resume\"}"); au_pump(H); } }
void host_audio_pause(host *H, int paused) { pthread_mutex_lock(&H->au_mx); au_pause_l(H, paused); pthread_mutex_unlock(&H->au_mx); }
size_t host_audio_queued(host *H) { pthread_mutex_lock(&H->au_mx); size_t q = au_queued(H); pthread_mutex_unlock(&H->au_mx); return q; }
void host_audio_close(host *H) { pthread_mutex_lock(&H->au_mx); H->src = NULL;   /* a source ends with its device */
    if (H->au_on) { au_pause_l(H, 1); if (!H->sink) dio_pcm_close(&H->pcm); H->au_on = 0; host_trace(H, "{\"ev\":\"audio_close\",\"underruns\":%u,\"dry\":%llu}", H->pcm.underruns, (unsigned long long)H->sink_dry); }
    pthread_mutex_unlock(&H->au_mx); }
/* the feeding thread: every 10 ms, the source (HOME Music) into the ring and the ring to the device, whatever the session's thread is doing
   (signals stay with the session's thread). It cannot outlast every stall: if this thread itself is not run for longer than the queue
   (the lead plus the device's buffer), the device still runs dry */
static void *au_feed(void *arg) { host *H = arg; sigset_t all; sigfillset(&all); pthread_sigmask(SIG_BLOCK, &all, NULL);
    while (H->au_thr_on) { pthread_mutex_lock(&H->au_mx); au_produce(H); au_pump(H); pthread_mutex_unlock(&H->au_mx); gp_sleep_ms(10); }
    return NULL; }
static void au_feed_start(host *H) { H->au_thr_on = 1; if (pthread_create(&H->au_thr, NULL, au_feed, H)) { H->au_thr_on = 0; host_trace(H, "{\"ev\":\"audio_feed\",\"thread\":false,\"note\":\"fed between passes only\"}"); }
    else { H->au_thr_seen = 1; gp_host_audio_source = au_source; host_trace(H, "{\"ev\":\"audio_feed\",\"thread\":true,\"every_ms\":10,\"produces\":true}"); } }
static void au_feed_stop(host *H) { if (!H->au_thr_on) return; H->au_thr_on = 0; pthread_join(H->au_thr, NULL); gp_host_audio_source = NULL; }

/* ---------------- the session's other needs (the console has no window, keyboard or Studio) ---------------- */
void host_set_title(host *H, const char *t) { char e[400]; json_escape(t, e, sizeof e); host_trace(H, "{\"ev\":\"title\",\"title\":%s}", e); }
void host_text_input(host *H, int on) { (void)H; (void)on; }   /* slot names are typed with the controller on the screen's keyboard */
void host_cursor(host *H, int visible) { (void)H; (void)visible; }
void host_return_focus(host *H) { (void)H; }
int host_embedded(host *H) { (void)H; return 0; }
int host_popped(host *H) { (void)H; return 0; }
void host_popout(host *H, int out) { (void)H; (void)out; }
void host_send(host *H, const char *line) { (void)H; (void)line; }
int host_fullscreen(host *H) { (void)H; return 1; }
void host_toggle_fullscreen(host *H) { (void)H; }
uint64_t host_now(host *H) { (void)H; return dio_mono_ms(); }

static void stats(host *H, int final) {
    struct rusage ru; getrusage(RUSAGE_SELF, &ru); long rss = dio_rss_kb(); if (rss > H->rss_max) H->rss_max = rss;
    host_trace(H, "{\"ev\":\"%s\",\"game_frames\":%llu,\"game_skipped\":%llu,\"frame_gap_ms\":[%.1f,%.1f],\"convert_ms\":[%.2f,%.2f],\"pan_ms\":[%.2f,%.2f],\"canvas_frames\":%llu,\"canvas_same\":%llu,\"canvas_ms\":[%.2f,%.2f],"
               "\"audio_written\":%llu,\"audio_overflow\":%llu,\"underruns\":%u,\"presses\":%llu,\"events\":%llu,\"disconnects\":%llu,\"cpu_s\":%.2f,\"rss_kb\":%ld,\"page_flip\":%s}",
               final ? "device_final" : "device_stats", (unsigned long long)H->game_frames, (unsigned long long)H->game_skipped, H->emu_gap.n ? H->emu_gap.sum / (double)H->emu_gap.n : 0.0, H->emu_gap.max,
               H->conv.n ? H->conv.sum / (double)H->conv.n : 0.0, H->conv.max, H->pan.n ? H->pan.sum / (double)H->pan.n : 0.0, H->pan.max, (unsigned long long)H->canvas_frames, (unsigned long long)H->canvas_same,
               H->canvas.n ? H->canvas.sum / (double)H->canvas.n : 0.0, H->canvas.max, (unsigned long long)H->au_written, (unsigned long long)H->au_overflow, H->pcm.underruns, (unsigned long long)H->presses,
               (unsigned long long)H->events, (unsigned long long)H->disconnects, (double)(ru.ru_utime.tv_sec + ru.ru_stime.tv_sec) + (double)(ru.ru_utime.tv_usec + ru.ru_stime.tv_usec) / 1e6, rss, H->fb.flip ? "true" : "false"); }
void host_poll(host *H, gp_pad *pad, host_events *ev) {
    memset(ev, 0, sizeof *ev); ev->music = -1; memset(pad, 0, sizeof *pad); H->hit_dev = -1;
    if (H->watchdog_s) alarm((unsigned)H->watchdog_s);   /* a step that never ends stops the player */
    input_pump(H, pad); pthread_mutex_lock(&H->au_mx); au_pump(H); pthread_mutex_unlock(&H->au_mx);
    if (g_stop) ev->studio_exit = 2;   /* told to stop (the launcher): the game's own save is written, no slot */
    uint64_t now = dio_mono_ms(); if (now - H->t_stats >= 5000) { H->t_stats = now; stats(H, 0); }
    if (H->in.n == 0 && H->in.fixed) { }   /* a test stream that ended */
}

/* ---------------- open / close ---------------- */
host *host_open(const host_config *c, const gp_settings *s, char *err, size_t n) {
    host *H = calloc(1, sizeof *H); if (!H) { snprintf(err, n, "out of memory"); return NULL; } g_h = H; H->t_open = dio_mono_ms(); H->hit_dev = -1; H->settle_ms = 400; H->watchdog_s = 30;
    pthread_mutex_init(&H->au_mx, NULL); gp_home_on_change = 1; gp_host_present_region = present_region;   /* K2 v3: the HOME is drawn only when its picture can differ; K3D1-F3: a title alone */
    const char *fbp = "/dev/fb0", *fbfile = NULL, *geom = NULL, *input = "auto", *mapf = NULL, *startup = NULL, *audio = "auto", *audiof = NULL, *v; snprintf(H->au_dev, sizeof H->au_dev, "/dev/snd/pcmC0D0p");
    for (int i = 0; i < c->nargs; i++) {
        if ((v = arg(c->nargs, c->args, &i, "--fb"))) fbp = v; else if ((v = arg(c->nargs, c->args, &i, "--fb-file"))) fbfile = v; else if ((v = arg(c->nargs, c->args, &i, "--fb-geometry"))) geom = v;
        else if ((v = arg(c->nargs, c->args, &i, "--input"))) input = v; else if ((v = arg(c->nargs, c->args, &i, "--map"))) mapf = v; else if ((v = arg(c->nargs, c->args, &i, "--startup"))) startup = v;
        else if ((v = arg(c->nargs, c->args, &i, "--audio"))) audio = v; else if ((v = arg(c->nargs, c->args, &i, "--audio-device"))) snprintf(H->au_dev, sizeof H->au_dev, "%s", v); else if ((v = arg(c->nargs, c->args, &i, "--audio-file"))) audiof = v;
        else if ((v = arg(c->nargs, c->args, &i, "--report"))) snprintf(H->report, sizeof H->report, "%s", v); else if ((v = arg(c->nargs, c->args, &i, "--test-dump-ppm"))) snprintf(H->dump, sizeof H->dump, "%s", v);
        else if ((v = arg(c->nargs, c->args, &i, "--input-settle-ms"))) H->settle_ms = atoi(v); else if ((v = arg(c->nargs, c->args, &i, "--watchdog-ms"))) H->watchdog_s = (atoi(v) + 999) / 1000;
        else if (!strcmp(c->args[i], "--ram-only")) H->ram_only = 1;
        else if ((v = arg(c->nargs, c->args, &i, "--home-pass-delay-ms"))) { int d = atoi(v); gp_home_pass_delay_ms = d < 0 ? 0 : d > 5000 ? 5000 : d; } else if (!strcmp(c->args[i], "--home-draw-every-pass")) gp_home_on_change = 0; else if (!strcmp(c->args[i], "--home-check-titles")) gp_home_check_titles = 1;   /* tests only */
        else { snprintf(err, n, "unknown option %s", c->args[i]); free(H); g_h = NULL; return NULL; } }
    if (H->settle_ms < 0 || H->settle_ms > 5000 || (fbfile && !geom)) { snprintf(err, n, "bad options (--input-settle-ms 0..5000; --fb-file needs --fb-geometry)"); free(H); g_h = NULL; return NULL; }
    /* 1. the RAM test: before anything is opened or written (the trace too) */
    if (H->ram_only) { char why[600] = "", d[1100]; int bad = 0; gp_object_check = ram_obj; gp_refused = on_refused;
        const char *own[5][2] = { { "trace", c->trace_path }, { "report", H->report }, { "test framebuffer file", fbfile }, { "test sound file", audiof }, { "test frame dump", H->dump } };
        for (int k = 0; k < 5 && !bad; k++) if (own[k][1] && own[k][1][0]) { dir_of(own[k][1], d, sizeof d); bad = add_root(H, own[k][0], d, why, sizeof why); }
        if (!bad) bad = ram_check_session(H, c, why, sizeof why);
        if (bad) { snprintf(err, n, "RAM-only session refused: %s", why); fprintf(stderr, "{\"ev\":\"refused\",\"stage\":\"ram\"}\n"); unconfine(); free(H); g_h = NULL; return NULL; }
        gp_write_check = write_check; }
    if (c->trace_path && c->trace_path[0]) { int fd = gp_open_file(c->trace_path, 1); H->trace = fd >= 0 ? fdopen(fd, "w") : NULL; if (!H->trace && fd >= 0) close(fd);
        if (H->trace) g_trace_fd = fileno(H->trace);
        else if (H->ram_only) { snprintf(err, n, "RAM-only session refused: the trace %s cannot be created as a file of its own in RAM", c->trace_path); fprintf(stderr, "{\"ev\":\"refused\",\"stage\":\"trace\"}\n"); unconfine(); free(H); g_h = NULL; return NULL; } }
    host_trace(H, "{\"ev\":\"host\",\"kind\":\"console\",\"platform\":\"%s\",\"ram_only\":%d,\"input\":\"%s\",\"framebuffer\":\"%s\"}", GP_PLATFORM, H->ram_only, strcmp(input, "auto") ? "test-stream" : "evdev", fbfile ? "test-file" : "fbdev");
    for (int k = 0; k < H->nroot; k++) { char e[1300]; json_escape(H->root[k], e, sizeof e); host_trace(H, "{\"ev\":\"ram_dir\",\"path\":%s}", e); }
    /* 2. the controller mapping */
    { char e[300]; if (mapf) { if (dio_map_load(&H->map, mapf, e, sizeof e)) { snprintf(err, n, "controller mapping: %s", e); host_trace(H, "{\"ev\":\"refused\",\"stage\":\"map\"}"); if (H->trace) fclose(H->trace); unconfine(); free(H); g_h = NULL; return NULL; } }
      else { dio_map_default(&H->map); snprintf(H->map.devices[1], sizeof H->map.devices[1], "Nintendo Clovercon - controller2"); H->map.ndev = 2; } }
    /* 3. the framebuffer (queried and checked before anything is drawn; the mode is never changed) */
    { char e[400]; if (dio_fb_open(&H->fb, fbfile ? fbfile : fbp, fbfile ? geom : NULL, 1, e, sizeof e)) { snprintf(err, n, "framebuffer: %s", e); char q[600]; json_escape(e, q, sizeof q); host_trace(H, "{\"ev\":\"refused\",\"stage\":\"framebuffer\",\"error\":%s}", q); dio_fb_close(&H->fb, 0, NULL, 0); if (H->trace) fclose(H->trace); unconfine(); free(H); g_h = NULL; return NULL; }
      char d[700]; dio_fb_describe(&H->fb, d, sizeof d); host_trace(H, "{\"ev\":\"fb\",%s,\"pages\":%d,\"page_flip\":%s}", d, H->fb.pages, H->fb.flip ? "true" : "false"); }
    H->prev = malloc((size_t)CCS_FB_W * CCS_FB_H * 3); H->ring = malloc((size_t)RING_FRAMES * 4);   /* fixed, once: nothing is allocated per frame */
    if (!H->prev || !H->ring) { snprintf(err, n, "out of memory"); dio_fb_close(&H->fb, 0, NULL, 0); if (H->trace) fclose(H->trace); free(H->prev); free(H->ring); free(H); g_h = NULL; unconfine(); return NULL; }
    /* 4. the startup image, at once: the HOME loads behind it */
    if (startup) splash_start(H, startup);
    /* 5. the controllers, found by name; the state folder keeps who plays */
    if (strcmp(input, "auto")) { H->in.fixed = input; char list[600], *save = NULL; snprintf(list, sizeof list, "%s", input);   /* tests: one or two streams (ports 1, 2) */
        for (char *p = strtok_r(list, ",", &save); p; p = strtok_r(NULL, ",", &save)) if (dio_inputs_open_fixed(&H->in, p, (uint64_t)H->settle_ms, seen_dev, H)) { char e[700]; json_escape(p, e, sizeof e); host_trace(H, "{\"ev\":\"input_missing\",\"path\":%s}", e); } }
    else if (!dio_inputs_scan(&H->in, &H->map, 0, (uint64_t)H->settle_ms, seen_dev, H)) host_trace(H, "{\"ev\":\"input_missing\",\"note\":\"no controller yet (one plugged in later is found within a second)\"}");
    H->in.next_scan_us = dio_mono_us() + 1000000u;
    { uint8_t *b; size_t len; if (c->session_path && !gp_read_file(c->session_path, &b, &len, 32u << 20)) { char e[80]; jval *j = json_parse((const char *)b, len, e, sizeof e); free(b);
        const jval *h = j ? json_get(j, "home") : NULL, *sd = h && h->t == J_OBJ ? json_get(h, "state_dir") : NULL; if (sd && sd->t == J_STR && sd->s[0]) { snprintf(H->state_dir, sizeof H->state_dir, "%s", sd->s); gp_path(H->assign_path, sizeof H->assign_path, sd->s, "player-assign.json"); }
        json_free(j); } }
    assign_load(H);
    /* 6. the sound */
    if (!strcmp(audio, "off")) H->au_off = 1; else if (audiof) { int fd = gp_open_file(audiof, 1); H->sink = fd >= 0 ? fdopen(fd, "wb") : NULL; if (!H->sink && fd >= 0) close(fd); if (!H->sink) host_trace(H, "{\"ev\":\"audio_unavailable\",\"error\":\"the test sink cannot be written\"}"); }
    H->tv_aspect = s ? s->tv_aspect : 1; H->integer_scale = s ? s->integer_scale : 0;
    struct sigaction sa; memset(&sa, 0, sizeof sa); sa.sa_handler = on_signal; sigemptyset(&sa.sa_mask); sigaction(SIGINT, &sa, NULL); sigaction(SIGTERM, &sa, NULL); sigaction(SIGHUP, &sa, NULL);
    sa.sa_handler = on_watchdog; sigaction(SIGALRM, &sa, NULL); sa.sa_handler = SIG_IGN; sigaction(SIGPIPE, &sa, NULL);
    if (!H->au_off) au_feed_start(H);   /* after the handlers: it blocks every signal for itself */
    host_trace(H, "{\"ev\":\"home_drawing\",\"on_change\":%d,\"pass_delay_ms\":%d}", gp_home_on_change, gp_home_pass_delay_ms);
    H->t_stats = dio_mono_ms(); return H; }
static uint32_t dump_ppm(const framebuffer *f, const char *path) {   /* the test frame dump (fb_write_ppm's file and CRC) through the confined file layer */
    int fd = gp_open_file(path, 1); FILE *o = fd >= 0 ? fdopen(fd, "wb") : NULL; if (!o) { if (fd >= 0) close(fd); return 0; }
    size_t nb = (size_t)f->w * f->h * 3; fprintf(o, "P6\n%d %d\n255\n", f->w, f->h); size_t put = fwrite(f->rgb, 1, nb, o); fclose(o); return put == nb ? gp_crc32(f->rgb, nb) : 0; }
static void report(host *H) {
    if (!H->report[0]) return; char buf[4096], e1[1300], e2[200], e3[400]; json_escape(H->splash_why, e1, sizeof e1); json_escape(H->splash_id, e2, sizeof e2); json_escape(H->au_dev, e3, sizeof e3);
    struct rusage ru; getrusage(RUSAGE_SELF, &ru); double cpu_u = (double)ru.ru_utime.tv_sec + (double)ru.ru_utime.tv_usec / 1e6, cpu_s = (double)ru.ru_stime.tv_sec + (double)ru.ru_stime.tv_usec / 1e6; char pads[600] = ""; size_t L = 0; for (int i = 0; i < H->in.n && L < sizeof pads - 80; i++) L += (size_t)snprintf(pads + L, sizeof pads - L, "%s{\"port\":%d,\"settle_ignored\":%d}", i ? "," : "", H->port[i], H->in.d[i].settle_ignored);
    int k = snprintf(buf, sizeof buf, "{\"schema\":\"ccs-console-session-report/1\",\"platform\":\"%s\",\"uptime_ms\":%llu,\"ram_only\":%d,\"startup\":{\"id\":%s,\"source\":\"%s\",\"visible_ms\":%llu,\"problems\":%s},"
        "\"picture\":{\"page_flip\":%s,\"fell_back\":%d,\"game_frames\":%llu,\"game_skipped\":%llu,\"convert_ms_avg\":%.2f,\"convert_ms_max\":%.2f,\"pan_ms_avg\":%.2f,\"pan_ms_max\":%.2f,\"frame_gap_ms_avg\":%.2f,\"frame_gap_ms_max\":%.2f,\"canvas_frames\":%llu,\"canvas_ms_avg\":%.2f,\"canvas_ms_max\":%.2f},"
        "\"sound\":{\"device\":%s,\"opened\":%d,\"failed\":%d,\"written\":%llu,\"underruns\":%u,\"overflow\":%llu,\"buffer\":%lu,\"period\":%lu,\"feed_thread\":%d,\"thread_produced\":%llu,\"test_dry\":%llu},\"controls\":{\"connected\":[%s],\"presses\":%llu,\"latched\":%llu,\"multi\":%llu,\"events\":%llu,\"settle_ignored\":%llu,\"disconnects\":%llu,\"dropped\":%llu},"
        "\"home\":{\"on_change\":%d,\"passes\":%llu,\"renders\":%llu,\"render_ms_avg\":%.1f,\"render_ms_max\":%.1f,\"pass_ms_max\":%.1f,\"responses\":%llu,\"response_ms_avg\":%.1f,\"response_ms_max\":%.1f,\"response_measures\":\"from the pass that read a press to its picture shown (not from the button itself)\",\"extra_taps\":%llu,\"left_at_transition\":%llu,\"titles\":%llu,\"title_ms_avg\":%.2f,\"title_ms_max\":%.2f,\"title_mismatch\":%llu,\"title_present_ms_avg\":%.2f},"
        "\"cpu_s\":%.2f,\"cpu_user_s\":%.2f,\"cpu_system_s\":%.2f,\"rss_kb_max\":%ld}\n",
        GP_PLATFORM, (unsigned long long)(dio_mono_ms() - H->t_open), H->ram_only, e2, H->splash_src, (unsigned long long)H->splash_visible, e1,
        H->fb.flip ? "true" : "false", H->fell_back, (unsigned long long)H->game_frames, (unsigned long long)H->game_skipped, H->conv.n ? H->conv.sum / (double)H->conv.n : 0.0, H->conv.max, H->pan.n ? H->pan.sum / (double)H->pan.n : 0.0, H->pan.max,
        H->emu_gap.n ? H->emu_gap.sum / (double)H->emu_gap.n : 0.0, H->emu_gap.max, (unsigned long long)H->canvas_frames, H->canvas.n ? H->canvas.sum / (double)H->canvas.n : 0.0, H->canvas.max,
        e3, H->rate != 0, H->au_failed, (unsigned long long)H->au_written, H->pcm.underruns, (unsigned long long)H->au_overflow, H->pcm.buffer, H->pcm.period, H->au_thr_seen, (unsigned long long)H->src_frames, (unsigned long long)H->sink_dry,
        pads, (unsigned long long)H->presses, (unsigned long long)H->latched, (unsigned long long)H->multi, (unsigned long long)H->events, (unsigned long long)H->settle_ignored,
        (unsigned long long)H->disconnects, (unsigned long long)H->dropped,
        gp_home_on_change, (unsigned long long)gp_hs.passes, (unsigned long long)gp_hs.renders, gp_hs.renders ? gp_hs.render_ms_sum / (double)gp_hs.renders : 0.0, gp_hs.render_ms_max, gp_hs.pass_ms_max,
        (unsigned long long)gp_hs.responses, gp_hs.responses ? gp_hs.resp_ms_sum / (double)gp_hs.responses : 0.0, gp_hs.resp_ms_max, (unsigned long long)gp_hs.extra_taps, (unsigned long long)gp_hs.left_at_transition,
        (unsigned long long)gp_hs.titles, gp_hs.titles ? gp_hs.title_ms_sum / (double)gp_hs.titles : 0.0, gp_hs.title_ms_max, (unsigned long long)gp_hs.title_mismatch, H->region.n ? H->region.sum / (double)H->region.n : 0.0,
        cpu_u + cpu_s, cpu_u, cpu_s, H->rss_max);
    int r = gp_write_atomic(H->report, buf, (size_t)(k > 0 ? k : 0)); host_trace(H, "{\"ev\":\"report_written\",\"ok\":%d}", r == 0);
    /* the same in a few lines for the TV (the RAM test's report card, photographed: RAM logs are lost at the reboot) */
    char txt[1600], tp[1200]; snprintf(tp, sizeof tp, "%s.txt", H->report);
    int t = snprintf(txt, sizeof txt, "STARTUP IMAGE: %s (%s), SHOWN %.1f S\nPICTURE: %llu GAME FRAMES, %llu SKIPPED, GAP %.1f MS AVG %.1f MAX\nDRAW %.1f MS AVG, PAN %.1f MS AVG %.1f MAX, HOME %llu\n"
                     "HOME: %llu PASSES, %llu FULL %.0f/%.0f MS, %llu TITLE %.0f MS MAX\nPOLL TO PICTURE: %llu, AVG %.0f MS, MAX %.0f MS; PASS MAX %.0f MS\n"
                     "SOUND: %s, %llu FR, %u UNDERRUNS, BUF %lu/%lu%s\nCONTROLLERS: %d, %llu PRESSES, %llu EXTRA, %llu LATE, %llu UNPLUGGED\nCPU %.1f+%.1f S (USER+SYSTEM), MEMORY %ld KB MAX, UP %.0f S\n",
                     H->splash_id[0] ? H->splash_id : "none", H->splash_src[0] ? H->splash_src : "-", (double)H->splash_visible / 1000.0, (unsigned long long)H->game_frames, (unsigned long long)H->game_skipped,
                     H->emu_gap.n ? H->emu_gap.sum / (double)H->emu_gap.n : 0.0, H->emu_gap.max, H->conv.n ? H->conv.sum / (double)H->conv.n : 0.0, H->pan.n ? H->pan.sum / (double)H->pan.n : 0.0, H->pan.max,
                     (unsigned long long)H->canvas_frames, (unsigned long long)gp_hs.passes, (unsigned long long)gp_hs.renders, gp_hs.renders ? gp_hs.render_ms_sum / (double)gp_hs.renders : 0.0, gp_hs.render_ms_max, (unsigned long long)gp_hs.titles, gp_hs.title_ms_max,
                     (unsigned long long)gp_hs.responses, gp_hs.responses ? gp_hs.resp_ms_sum / (double)gp_hs.responses : 0.0, gp_hs.resp_ms_max, gp_hs.pass_ms_max,
                     H->au_failed ? "FAILED" : H->rate ? "PLAYED" : "NOT USED", (unsigned long long)H->au_written, H->pcm.underruns, H->pcm.buffer, H->pcm.period, H->au_thr_seen ? "" : ", NO FEED",
                     H->in.n, (unsigned long long)H->presses, (unsigned long long)gp_hs.extra_taps, (unsigned long long)H->latched, (unsigned long long)H->disconnects, cpu_u, cpu_s, H->rss_max, (double)(dio_mono_ms() - H->t_open) / 1000.0);
    gp_write_atomic(tp, txt, (size_t)(t > 0 ? t : 0)); }
void host_close(host *H) {
    if (!H) return; alarm(0); au_feed_stop(H); stats(H, 1); if (H->splash) { H->splash = 0; H->splash_visible = dio_mono_ms() - H->splash_at; }
    host_audio_close(H); if (H->sink) { fflush(H->sink); if (ftruncate(fileno(H->sink), (off_t)(H->sink_written * 4))) { } fclose(H->sink); }
    if (H->dump[0] && H->fb.map) { framebuffer f; if (!fb_init(&f, CCS_FB_W, CCS_FB_H)) { uint32_t y0 = H->fb.shown_yoff; for (int y = 0; y < CCS_FB_H; y++) for (int x = 0; x < CCS_FB_W; x++) { uint32_t p = ((uint32_t *)(void *)(H->fb.mem + (size_t)(y0 + (uint32_t)y) * H->fb.fix.line_length))[x];
            uint8_t *o = f.rgb + ((size_t)y * CCS_FB_W + x) * 3; o[0] = (uint8_t)(p >> H->fb.var.red.offset); o[1] = (uint8_t)(p >> H->fb.var.green.offset); o[2] = (uint8_t)(p >> H->fb.var.blue.offset); }
        uint32_t crc = dump_ppm(&f, H->dump); host_trace(H, "{\"ev\":\"frame_dumped\",\"crc32\":\"%08x\"}", crc); fb_free(&f); } }
    report(H); for (int i = H->in.n - 1; i >= 0; i--) dio_dev_close(&H->in, i);
    char w[200]; dio_fb_close(&H->fb, H->dump[0] ? 0 : 1, w, sizeof w); if (w[0]) host_trace(H, "{\"ev\":\"warning\",\"warning\":\"%s\"}", w);
    host_trace(H, "{\"ev\":\"host_close\",\"signal\":%d}", (int)g_stop); if (H->trace) fclose(H->trace); g_trace_fd = -1;
    free(H->prev); free(H->ring); free(H->lut); unconfine(); pthread_mutex_destroy(&H->au_mx); g_h = NULL; free(H); }

/* ---------------- --card: a text file on the TV (the RAM test's report and stop cards), until A is pressed ---------------- */
int host_dev_card(int argc, char **argv) {
    const char *file = NULL, *fbp = "/dev/fb0", *fbfile = NULL, *geom = NULL, *mapf = NULL, *input = "auto", *v; long ms = 0;
    for (int i = 1; i < argc; i++) { if ((v = arg(argc, argv, &i, "--card"))) file = v; else if ((v = arg(argc, argv, &i, "--fb"))) fbp = v; else if ((v = arg(argc, argv, &i, "--fb-file"))) fbfile = v;
        else if ((v = arg(argc, argv, &i, "--fb-geometry"))) geom = v; else if ((v = arg(argc, argv, &i, "--map"))) mapf = v; else if ((v = arg(argc, argv, &i, "--input"))) input = v; else if ((v = arg(argc, argv, &i, "--card-ms"))) ms = atol(v);
        else { fprintf(stderr, "ccs-player: unknown option %s\n", argv[i]); return 2; } }
    if (!file || ms < 0 || ms > 600000) { fprintf(stderr, "usage: --card FILE [--card-ms N]\n"); return 2; }
    FILE *f = fopen(file, "r"); if (!f) { fprintf(stderr, "ccs-player: %s cannot be read\n", file); return 2; }
    char lines[18][80], title[60] = "CLASSIC CART STUDIO"; int nl = 0; char l[200];
    while (fgets(l, sizeof l, f) && nl < 18) { size_t L = strlen(l); while (L && (l[L - 1] == '\n' || l[L - 1] == '\r')) l[--L] = 0; if (!strncmp(l, "# ", 2)) { snprintf(title, sizeof title, "%.59s", l + 2); continue; } snprintf(lines[nl++], sizeof lines[0], "%.66s", l); }
    fclose(f); host H; memset(&H, 0, sizeof H); char e[400];
    if (dio_fb_open(&H.fb, fbfile ? fbfile : fbp, fbfile ? geom : NULL, 1, e, sizeof e)) { fprintf(stderr, "ccs-player: framebuffer: %s\n", e); return 12; }
    framebuffer fb; if (fb_init(&fb, CCS_FB_W, CCS_FB_H)) return 12; const char *L[18]; for (int i = 0; i < nl; i++) L[i] = lines[i]; card_text(&fb, title, L, nl);
    fb_text(&fb, 80, 660, 2, "PRESS A TO GO ON", 160, 160, 170); int fl = 0; char w2[200]; dio_fb_present(&H.fb, &fb, &fl, w2, sizeof w2); fb_free(&fb);
    if (mapf) { if (dio_map_load(&H.map, mapf, e, sizeof e)) dio_map_default(&H.map); } else dio_map_default(&H.map);
    if (H.map.ndev < 2) { snprintf(H.map.devices[H.map.ndev], sizeof H.map.devices[0], "Nintendo Clovercon - controller2"); H.map.ndev++; }
    H.settle_ms = 400; if (strcmp(input, "auto")) { H.in.fixed = input; dio_inputs_open_fixed(&H.in, input, 400, NULL, NULL); } else dio_inputs_scan(&H.in, &H.map, 0, 400, NULL, NULL);
    struct sigaction sa; memset(&sa, 0, sizeof sa); sa.sa_handler = on_signal; sigemptyset(&sa.sa_mask); sigaction(SIGINT, &sa, NULL); sigaction(SIGTERM, &sa, NULL);
    uint64_t t0 = dio_mono_ms(); int done = 0;
    while (!g_stop && !done && (!ms || dio_mono_ms() - t0 < (uint64_t)ms)) { H.hit_dev = -1; gp_pad p; memset(&p, 0, sizeof p); for (int i = 0; i < H.in.n; i++) H.settled[i] = H.settled[i] || dio_mono_ms() >= H.in.d[i].settle_until;
        for (int i = 0; i < H.in.n; i++) if (dio_dev_read(&H.in.d[i], on_event, &H) < 0) { dio_dev_close(&H.in, i); i--; }
        if (H.hit_dev >= 0 && H.hit_act == GA_A) done = 1; struct pollfd pf[DIO_MAX_DEV]; for (int i = 0; i < H.in.n; i++) { pf[i].fd = H.in.d[i].fd; pf[i].events = POLLIN; pf[i].revents = 0; }
        if (H.in.n) poll(pf, (nfds_t)H.in.n, 100); else gp_sleep_ms(100); }
    for (int i = H.in.n - 1; i >= 0; i--) dio_dev_close(&H.in, i); dio_fb_close(&H.fb, 0, NULL, 0); printf("{\"ev\":\"card_end\",\"by\":\"%s\"}\n", done ? "a" : g_stop ? "signal" : "time"); return 0; }
