/* host_headless.c: the player without a window or sound device, for automated tests and the ARM feasibility build. Time is virtual
 * (gp_virtual: sleeping advances the clock), input comes from a script of INJECTED logical controller events, frames can be written
 * as PPM files on request, and every event goes to the trace. Nothing here is a hardware test.
 * Script lines: "<ms> press|release p1|p2 <action>", "<ms> close", "<ms> esc", "<ms> shot <name>", "<ms> failwrites on|off",
 *               "<ms> focus lost|gained", "<ms> fullscreen", "<ms> quit-after" (stop when the session ends; the default).
 * Embedded (CCS_PLAYER_EMBED_TEST=1), as the Studio sends them: "<ms> preview-close" (the Preview's own Close; "close" and "popin"
 *               are the pop-out window's X), "<ms> music on|off" (the Studio saved HOME Music).
 * The mouse (stage pixels, INJECTED): "<ms> mouse move|click|double|back|wheelup|wheeldown <x> <y>"; "<ms> layout" asks for the HOME's
 * layout rectangles (home_layout) or the open menu's item rectangles in the trace.
 * Generic (DirectInput-style) controllers, as RAW device state for the shared gp_pads.c (injected, not hardware): "<ms> pad add <name>",
 * "<ms> pad remove <name>", "<ms> pad use <name>", then for the controller in use "<ms> pad btn <0..31> <0|1>", "<ms> pad pov <hundredths
 * of a degree|-1>", "<ms> pad axis <0..5> <-1000..1000>". Controllers join Players 1 and 2 in the order they were added. */
#ifndef _WIN32
#define _POSIX_C_SOURCE 200809L   /* fileno and ftruncate (the test tap) in the strict C11 Linux / ARM build */
#endif
#include "host.h"
#ifdef _WIN32
#include <io.h>
#else
#include <unistd.h>
#endif
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdarg.h>

extern int gp_virtual;
typedef struct { uint64_t t; char cmd[16], a[64], b[64], c[64]; } sline;
typedef struct { char name[64]; int uid, map[GA_COUNT]; gp_raw raw; uint8_t pact[GA_COUNT], pbtn[32]; int hit, configured; } fake_pad;
struct host { char theme[48]; int embed, popped, studio_exit; int text_on; char text[32]; int ntext; sline *s; size_t n, i; FILE *trace; char shots[1024]; gp_pad pad; int fs, pending_close, pending_preview_close, pending_music, pending_esc, pending_lost, pending_gained, swapped; uint64_t audio_frames; int last_present_kind; char shot_name[64];
              fake_pad fp[4]; int nfp, use, next_uid; char pads_path[1100]; host_pointer pq[16]; int npq, pending_layout, cursor_hidden; char pick[GP_PLAYERS][64]; };   /* pick: the chosen controller by name ("" automatic, "none" the keyboard) */

static int action_of(const char *s) { for (int a = 0; a < GA_COUNT; a++) if (!strcmp(s, gp_action_name(a))) return a; return -1; }
static void who_plays(host *H, int *who) {
    int pick[GP_PLAYERS]; for (int p = 0; p < GP_PLAYERS; p++) { pick[p] = !H->pick[p][0] ? GP_PICK_AUTO : !strcmp(H->pick[p], "none") ? GP_PICK_NONE : GP_PICK_AUTO; for (int i = 0; i < H->nfp; i++) if (!strcmp(H->fp[i].name, H->pick[p])) pick[p] = i; }
    gp_assign_players(H->nfp, pick, H->swapped, who); }
host *host_open(const host_config *c, const gp_settings *s, char *err, size_t n) {
    (void)s; host *H = calloc(1, sizeof *H); gp_virtual = 1;
    if (c->display_path && c->display_path[0]) { snprintf(H->pads_path, sizeof H->pads_path, "%s", c->display_path); char *sl = strrchr(H->pads_path, '\\'); char *s2 = strrchr(H->pads_path, '/'); if (s2 > sl) sl = s2;
        if (sl) snprintf(sl + 1, sizeof H->pads_path - (size_t)(sl + 1 - H->pads_path), "player-pads.json"); }
    if (c->trace_path && c->trace_path[0]) H->trace = fopen(c->trace_path, "w");
    H->embed = getenv("CCS_PLAYER_EMBED_TEST") && atoi(getenv("CCS_PLAYER_EMBED_TEST")) == 1;   /* tests: the session behaves as in the Studio's Preview */
    if (c->shot_dir) snprintf(H->shots, sizeof H->shots, "%s", c->shot_dir);
    if (c->script_path && c->script_path[0]) {
        FILE *f = fopen(c->script_path, "r"); if (!f) { snprintf(err, n, "the test script cannot be read"); free(H); return NULL; }
        char line[256]; while (fgets(line, sizeof line, f)) { sline l = { 0 }; unsigned long long t; int k = sscanf(line, "%llu %15s %63s %63s %63s", &t, l.cmd, l.a, l.b, l.c); if (k < 2) continue; l.t = t;
            H->s = realloc(H->s, (H->n + 1) * sizeof *H->s); H->s[H->n++] = l; }
        fclose(f); }
    host_trace(H, "{\"ev\":\"host\",\"kind\":\"headless\",\"input\":\"injected-script\",\"platform\":\"%s\"}", GP_PLATFORM); return H; }
static void au_end(void);
void host_close(host *H) { if (!H) return; au_end(); host_trace(H, "{\"ev\":\"host_close\",\"audio_frames\":%llu}", (unsigned long long)H->audio_frames); if (H->trace) fclose(H->trace); free(H->s); free(H); }
void host_poll(host *H, gp_pad *pad, host_events *ev) {
    gp_sleep_ms(1); uint64_t now = gp_ms();
    while (H->i < H->n && H->s[H->i].t <= now) { sline *l = &H->s[H->i++];
        if (!strcmp(l->cmd, "press") || !strcmp(l->cmd, "release")) { int p = !strcmp(l->a, "p2"), a = action_of(l->b); if (a >= 0) H->pad.down[p][a] = !strcmp(l->cmd, "press"); }
        else if (!strcmp(l->cmd, "close")) H->pending_close = 1; else if (!strcmp(l->cmd, "esc")) H->pending_esc = 1;
        else if (!strcmp(l->cmd, "shot")) snprintf(H->shot_name, sizeof H->shot_name, "%s", l->a);
        else if (!strcmp(l->cmd, "failwrites")) gp_fail_writes = !strcmp(l->a, "on");
        else if (!strcmp(l->cmd, "focus")) { if (!strcmp(l->a, "lost")) H->pending_lost = 1; else H->pending_gained = 1; }
        else if (!strcmp(l->cmd, "fullscreen")) host_toggle_fullscreen(H);
        else if (!strcmp(l->cmd, "layout")) H->pending_layout = 1;
        else if (!strcmp(l->cmd, "theme")) snprintf(H->theme, sizeof H->theme, "%s", l->a);   /* the Preview's Theme menu, as the Studio sends it */
        else if (!strcmp(l->cmd, "popout")) host_popout(H, 1); else if (!strcmp(l->cmd, "popin")) { if (H->popped) H->pending_close = 1; }
        else if (!strcmp(l->cmd, "preview-close")) H->pending_preview_close = 1; else if (!strcmp(l->cmd, "music")) H->pending_music = !strcmp(l->a, "on") ? 2 : 1;
        else if (!strcmp(l->cmd, "studioexit")) H->studio_exit = !strcmp(l->a, "force") ? 2 : !strcmp(l->a, "gone") ? 3 : 1;
        else if (!strcmp(l->cmd, "text")) { const char *w = !strcmp(l->a, "BS") ? "\b" : !strcmp(l->a, "ENTER") ? "\n" : l->a; for (; *w && H->ntext < 31; w++) H->text[H->ntext++] = *w == '_' ? ' ' : *w; H->text[H->ntext] = 0; }
        else if (!strcmp(l->cmd, "mouse") && H->npq < 16) { static const char *K[] = { "move", "click", "double", "back", "wheelup", "wheeldown" };
            for (int k = 0; k < 6; k++) if (!strcmp(l->a, K[k])) H->pq[H->npq++] = (host_pointer){ k < 4 ? k : PT_WHEEL, atoi(l->b), atoi(l->c), k == 4 ? 1 : k == 5 ? -1 : 0 }; }
        else if (!strcmp(l->cmd, "pad")) {
            if (!strcmp(l->a, "add") && H->nfp < 4) { fake_pad *d = &H->fp[H->nfp++]; memset(d, 0, sizeof *d); snprintf(d->name, sizeof d->name, "%s", l->b); d->uid = ++H->next_uid; d->raw.pov = -1;
                char key[96]; snprintf(key, sizeof key, "headless-%s", d->name); gp_map_default(d->map); d->configured = gp_pads_load(H->pads_path, key, d->map); H->use = H->nfp - 1;
                unsigned vid = 0, pid = 0; if (!d->configured && sscanf(l->c, "%x:%x", &vid, &pid) == 2 && gp_known_map((int)vid, (int)pid, d->map, NULL)) d->configured = 1;   /* "pad add <name> <vid:pid>": a known controller */
                host_trace(H, "{\"ev\":\"pad_connected\",\"kind\":\"injected\",\"id\":%d,\"name\":\"%s\"}", d->uid, d->name); }
            else if (!strcmp(l->a, "remove")) { for (int i = 0; i < H->nfp; i++) if (!strcmp(H->fp[i].name, l->b)) { host_trace(H, "{\"ev\":\"pad_disconnected\",\"kind\":\"injected\",\"id\":%d}", H->fp[i].uid); H->fp[i] = H->fp[--H->nfp]; break; } if (H->use >= H->nfp) H->use = H->nfp - 1; }
            else if (!strcmp(l->a, "use")) { for (int i = 0; i < H->nfp; i++) if (!strcmp(H->fp[i].name, l->b)) H->use = i; }
            else if (H->use >= 0 && H->use < H->nfp) { gp_raw *r = &H->fp[H->use].raw; int k = atoi(l->b), v = atoi(l->c);
                if (!strcmp(l->a, "btn") && k >= 0 && k < 32) r->btn[k] = (uint8_t)(v != 0); else if (!strcmp(l->a, "pov")) r->pov = k; else if (!strcmp(l->a, "axis") && k >= 0 && k < GP_AXES) r->axis[k] = v; } }
        host_trace(H, "{\"ev\":\"script\",\"cmd\":\"%s\",\"a\":\"%s\",\"b\":\"%s\",\"c\":\"%s\"}", l->cmd, l->a, l->b, l->c); }
    *pad = H->pad;
    { int who[4]; who_plays(H, who);
      for (int i = 0; i < H->nfp; i++) { uint8_t act[GA_COUNT]; gp_raw_actions(&H->fp[i].raw, H->fp[i].map, act); fake_pad *d = &H->fp[i]; d->hit = 0;
          if (!d->configured) act[GA_A] = act[GA_B] = act[GA_SELECT] = act[GA_START] = 0;   /* as the Windows host (F17) */
          for (int a = 0; a < GA_COUNT; a++) { if (act[a] && !d->pact[a] && !d->hit) d->hit = a + 1; d->pact[a] = act[a]; }
          for (int k = 0; k < 32; k++) { if (d->raw.btn[k] && !d->pbtn[k] && !d->hit) d->hit = 9; d->pbtn[k] = d->raw.btn[k]; }   /* a button with no NES action still finds it */
          if (who[i] >= 0) for (int a = 0; a < GA_COUNT; a++) if (act[a]) pad->down[who[i]][a] = 1; } }
    memset(ev, 0, sizeof *ev);
    ev->close_requested = H->pending_close; ev->menu_key = H->pending_esc; ev->focus_lost = H->pending_lost; ev->focus_gained = H->pending_gained;
    memcpy(ev->pt, H->pq, sizeof ev->pt); ev->npt = H->npq; H->npq = 0; ev->layout = H->pending_layout; H->pending_layout = 0;
    if (H->text_on) { memcpy(ev->text, H->text, sizeof ev->text); ev->ntext = H->ntext; } H->ntext = 0; H->text[0] = 0;
    H->pending_close = H->pending_esc = H->pending_lost = H->pending_gained = 0; ev->studio_exit = H->studio_exit; H->studio_exit = 0; ev->music = H->pending_music ? H->pending_music - 1 : -1; H->pending_music = 0; ev->preview_close = H->pending_preview_close; H->pending_preview_close = 0; snprintf(ev->theme, sizeof ev->theme, "%s", H->theme); H->theme[0] = 0;
    if (H->i >= H->n && H->n && now > H->s[H->n - 1].t + 120000) { ev->close_requested = 1; }   /* a script that ends without closing: a close request 2 min later */
    if (H->i >= H->n && H->n && now > H->s[H->n - 1].t + 600000) { host_trace(H, "{\"ev\":\"forced_end\",\"note\":\"the script ended and the session never closed\"}"); if (H->trace) fclose(H->trace); exit(9); }
}
static void shot_ppm(host *H, const uint8_t *rgb, int w, int h, int stride3) {
    if (!H->shot_name[0] || !H->shots[0]) return; char p[1200]; snprintf(p, sizeof p, "%s/%s.ppm", H->shots, H->shot_name); FILE *f = fopen(p, "wb");
    if (f) { fprintf(f, "P6\n%d %d\n255\n", w, h); for (int y = 0; y < h; y++) fwrite(rgb + (size_t)y * stride3, 1, (size_t)w * 3, f); fclose(f); host_trace(H, "{\"ev\":\"shot\",\"name\":\"%s\",\"w\":%d,\"h\":%d}", H->shot_name, w, h); }
    H->shot_name[0] = 0; }
void host_present_nes(host *H, const uint16_t *px, int w, int h) {
    H->last_present_kind = 1; if (!H->shot_name[0]) return; uint8_t *rgb = malloc((size_t)w * h * 3);
    for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { uint16_t v = px[y * 256 + x]; uint8_t *o = rgb + ((size_t)y * w + x) * 3; o[0] = (uint8_t)((v >> 11) << 3); o[1] = (uint8_t)(((v >> 5) & 63) << 2); o[2] = (uint8_t)((v & 31) << 3); }
    shot_ppm(H, rgb, w, h, w * 3); free(rgb); }
int host_canvas_scale(void) { const char *e = getenv("CCS_PLAYER_SCALE"); return e && atoi(e) == 2 ? 2 : 1; }
void host_home_pace(int *cover_budget, int *music_ahead) { *cover_budget = 4; *music_ahead = 6144; }   /* as the window: four covers per pass, the song about 130 ms ahead */
void host_present_canvas(host *H, const framebuffer *fb) { H->last_present_kind = 2; shot_ppm(H, fb->rgb, fb->w, fb->h, fb->w * 3); }
/* sound: no device. Tests only: CCS_PLAYER_AUDIO_TAP names a file that receives what a device would play, consumed by the virtual clock
   (what was queued but not yet played when sound pauses is dropped, as waveOutReset drops it); the trace labels it */
static struct { FILE *tap; int on, paused, rate; uint64_t pushed, base, t0; } AU;
static uint64_t au_played(host *H) { uint64_t p = AU.base + (AU.paused ? 0 : (gp_ms() - AU.t0) * (uint64_t)AU.rate / 1000u); if (p > AU.pushed) { AU.base = AU.pushed; AU.t0 = gp_ms(); p = AU.pushed; } (void)H; return p; }
int host_audio_open(host *H, int rate) {
    const char *p = getenv("CCS_PLAYER_AUDIO_TAP"); if (!p || !*p) { host_trace(H, "{\"ev\":\"audio_open\",\"rate\":%d,\"device\":false}", rate); return -1; }
    if (!AU.tap) AU.tap = fopen(p, "wb"); if (!AU.tap) return -1; AU.on = 1; AU.paused = 0; AU.rate = rate; AU.t0 = gp_ms(); AU.base = AU.pushed;
    host_trace(H, "{\"ev\":\"audio_open\",\"rate\":%d,\"device\":false,\"tap\":true,\"note\":\"test only: what a device would play, by the virtual clock\"}", rate); return 0; }
void host_audio_push(host *H, const int16_t *st, size_t frames) { H->audio_frames += frames; if (AU.on && AU.tap) { fwrite(st, 4, frames, AU.tap); AU.pushed += frames; } }
void host_audio_pause(host *H, int paused) {
    if (!AU.on || paused == AU.paused) return;
    if (paused) { uint64_t pl = au_played(H), drop = AU.pushed - pl; if (drop) { fflush(AU.tap); fseek(AU.tap, (long)(pl * 4), SEEK_SET); AU.pushed = pl; } AU.base = pl; AU.paused = 1; host_trace(H, "{\"ev\":\"audio_pause\",\"dropped\":%llu}", (unsigned long long)drop); }
    else { AU.paused = 0; AU.t0 = gp_ms(); host_trace(H, "{\"ev\":\"audio_resume\"}"); } }
size_t host_audio_queued(host *H) { return AU.on ? (size_t)(AU.pushed - au_played(H)) : 0; }
void host_audio_close(host *H) { if (!AU.on) return; host_audio_pause(H, 1); AU.on = 0; host_trace(H, "{\"ev\":\"audio_close\"}"); }
static void au_end(void) { if (!AU.tap) return; fflush(AU.tap);
#ifdef _WIN32
    if (_chsize(_fileno(AU.tap), (long)(AU.pushed * 4))) { }
#else
    if (ftruncate(fileno(AU.tap), (off_t)(AU.pushed * 4))) { }
#endif
    fclose(AU.tap); AU.tap = NULL; }   /* the tap holds exactly what a device would have played */
void host_set_title(host *H, const char *t) { char e[400]; json_escape(t, e, sizeof e); host_trace(H, "{\"ev\":\"title\",\"title\":%s}", e); }
void host_set_picture(host *H, int tv_aspect, int integer_scale) { host_trace(H, "{\"ev\":\"picture\",\"tv_aspect\":%d,\"integer\":%d}", tv_aspect, integer_scale); }
void host_return_focus(host *H) { host_trace(H, "{\"ev\":\"return_focus\"}"); }
int host_embedded(host *H) { return H && H->embed; }
int host_popped(host *H) { return H && H->popped; }
void host_popout(host *H, int out) { if (!H->embed || out == H->popped) return; H->popped = out; host_trace(H, "{\"ev\":\"popped\",\"out\":%d}", out); }
void host_send(host *H, const char *line) { if (!H->embed) return; char e[4200]; json_escape(line, e, sizeof e); host_trace(H, "{\"ev\":\"to_studio\",\"line\":%s}", e); }
void host_text_input(host *H, int on) { H->text_on = on; host_trace(H, "{\"ev\":\"text_input\",\"on\":%d}", on); }
void host_cursor(host *H, int visible) { if (!H || !visible == H->cursor_hidden) return; H->cursor_hidden = !visible; host_trace(H, "{\"ev\":\"cursor\",\"visible\":%d}", visible); }
int host_pads(host *H, char *out, size_t n) { const char *nm[2] = { NULL, NULL }; int who[4]; who_plays(H, who); for (int i = 0; i < H->nfp; i++) if (who[i] >= 0 && !nm[who[i]]) nm[who[i]] = H->fp[i].name;
    gp_players_text(nm, out, n); return H->nfp; }
void host_swap_pads(host *H) { if (H->pick[0][0] || H->pick[1][0]) { char t[64]; memcpy(t, H->pick[0], 64); memcpy(H->pick[0], H->pick[1], 64); memcpy(H->pick[1], t, 64); } else H->swapped ^= 1;
    host_trace(H, "{\"ev\":\"swap_pads\",\"swapped\":%d,\"chosen\":%d}", H->swapped, H->pick[0][0] || H->pick[1][0]); }
int host_pad_count(host *H) { return H->nfp; }
void host_pad_info(host *H, int i, char *label, size_t n, int *player) { int who[4]; who_plays(H, who); snprintf(label, n, "%s", i >= 0 && i < H->nfp ? H->fp[i].name : "controller"); *player = i >= 0 && i < H->nfp ? who[i] : -1; }
void host_pad_choose(host *H, int player, int i) {
    if (player < 0 || player >= GP_PLAYERS) return; char k[64] = ""; if (i == GP_PICK_NONE) snprintf(k, sizeof k, "none"); else if (i >= 0 && i < H->nfp) snprintf(k, sizeof k, "%s", H->fp[i].name);
    if (i == GP_PICK_AUTO) H->pick[0][0] = H->pick[1][0] = 0;
    else { if (k[0] && strcmp(k, "none") && !strcmp(H->pick[1 - player], k)) snprintf(H->pick[1 - player], 64, "%s", H->pick[player]); snprintf(H->pick[player], 64, "%s", k); }
    host_trace(H, "{\"ev\":\"pad_choose\",\"player\":%d,\"device\":%d,\"p1\":\"%s\",\"p2\":\"%s\"}", player + 1, i, H->pick[0], H->pick[1]); }
int host_pad_pressed(host *H, int *action) { for (int i = 0; i < H->nfp; i++) if (H->fp[i].hit) { if (action) *action = H->fp[i].hit - 1; return i; } return -1; }
static fake_pad *fp_by_uid(host *H, int uid) { for (int i = 0; i < H->nfp; i++) if (H->fp[i].uid == uid) return &H->fp[i]; return NULL; }
int host_pad_list(host *H, int *ids, int max) { int n = 0; for (int i = 0; i < H->nfp && n < max; i++) ids[n++] = H->fp[i].uid; return n; }
int host_pad_raw(host *H, int id, gp_raw *raw) { fake_pad *d = fp_by_uid(H, id); if (!d) return 0; *raw = d->raw; return 1; }
int host_pad_configured(host *H, int id) { fake_pad *d = fp_by_uid(H, id); return d && d->configured; }
void host_pad_name(host *H, int id, char *out, size_t n) { fake_pad *d = fp_by_uid(H, id); snprintf(out, n, "%s", d ? d->name : "controller"); }
int host_pad_set_map(host *H, int id, const int map[GA_COUNT]) {
    fake_pad *d = fp_by_uid(H, id); if (!d) return -1; memcpy(d->map, map, sizeof d->map); d->configured = 1; char key[96]; snprintf(key, sizeof key, "headless-%s", d->name);
    int r = gp_pads_save(H->pads_path, key, d->map); host_trace(H, "{\"ev\":\"pad_map\",\"id\":%d,\"map\":[%d,%d,%d,%d,%d,%d,%d,%d],\"saved\":%d}", id, map[0], map[1], map[2], map[3], map[4], map[5], map[6], map[7], r == 0); return r; }
uint64_t host_now(host *H) { (void)H; return gp_ms(); }
void host_trace(host *H, const char *fmt, ...) { if (!H || !H->trace) return; char b[2048]; va_list a; va_start(a, fmt); vsnprintf(b, sizeof b, fmt, a); va_end(a);
    fprintf(H->trace, "{\"t\":%llu,%s\n", (unsigned long long)gp_ms(), b[0] == '{' ? b + 1 : b); fflush(H->trace); }
int host_fullscreen(host *H) { return H->fs; }
void host_toggle_fullscreen(host *H) { H->fs ^= 1; host_trace(H, "{\"ev\":\"fullscreen\",\"on\":%d}", H->fs); }
