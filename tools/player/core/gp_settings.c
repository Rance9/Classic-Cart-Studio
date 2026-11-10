/* gp_settings.c: the player's preferences.
 *   player-settings.json  written by the Studio (Play on PC Settings): keys per player and action ("Z|Enter": either key), display
 *                         {"aspect": "tv" | "square", "integer": true|false}, "swap_pads"
 *   player-display.json   written by the player: the remembered window size and full screen choice
 * Defaults follow the Studio's own keyboard language (Preview on TV): arrows, Z or Enter = A, X or Backspace = B, S = SELECT, M = START;
 * Player 2: I J K L, H = A, G = B, T = SELECT, Y = START. Controllers are assigned in connection order (host).
 *   game-settings/         per-game preferences and the gameplay defaults (see gp.h), shared with the Studio */
#include "gp.h"
#include "../../console-simulator/core/ccs_sim.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static const char *DEF[GP_PLAYERS][GA_COUNT] = { { "Up", "Down", "Left", "Right", "Z|Enter", "X|Backspace", "S", "M" }, { "I", "K", "J", "L", "H", "G", "T", "Y" } };
void gp_settings_default(gp_settings *s) {
    memset(s, 0, sizeof *s);
    for (int p = 0; p < GP_PLAYERS; p++) for (int a = 0; a < GA_COUNT; a++) snprintf(s->key[p][a], sizeof s->key[p][a], "%s", DEF[p][a]);
    s->tv_aspect = 1; s->integer_scale = 0; s->win_w = 1280; s->win_h = 800; s->fullscreen = 0; }
static int key_ok(const char *k) { size_t n = strlen(k); if (!n || n >= 24) return 0; for (size_t i = 0; i < n; i++) if (!((k[i] >= 'A' && k[i] <= 'Z') || (k[i] >= 'a' && k[i] <= 'z') || (k[i] >= '0' && k[i] <= '9') || k[i] == '|' || k[i] == '_')) return 0; return 1; }
int gp_settings_load(gp_settings *s, const char *path) {
    uint8_t *b; size_t n; if (gp_read_file(path, &b, &n, 1u << 20)) return -1;
    char err[120]; jval *j = json_parse((const char *)b, n, err, sizeof err); free(b); if (!j || j->t != J_OBJ) { json_free(j); return -1; }
    const jval *keys = json_get(j, "keys");
    if (keys && keys->t == J_OBJ) for (int p = 0; p < GP_PLAYERS; p++) { const jval *pk = json_get(keys, p ? "p2" : "p1"); if (!pk || pk->t != J_OBJ) continue;
        for (int a = 0; a < GA_COUNT; a++) { const jval *v = json_get(pk, gp_action_name(a)); if (v && v->t == J_STR && key_ok(v->s)) snprintf(s->key[p][a], sizeof s->key[p][a], "%s", v->s); else if (v && v->t == J_STR && !v->s[0]) s->key[p][a][0] = 0; } }
    const jval *d = json_get(j, "display");
    if (d && d->t == J_OBJ) { const jval *a = json_get(d, "aspect"), *i = json_get(d, "integer"); if (a && a->t == J_STR) s->tv_aspect = strcmp(a->s, "square") != 0; if (i && i->t == J_BOOL) s->integer_scale = i->b; }
    const jval *sw = json_get(j, "swap_pads"); if (sw && sw->t == J_BOOL) s->swap_pads = sw->b;
    const jval *fs = json_get(j, "fullscreen"), *w = json_get(j, "w"), *h = json_get(j, "h");
    if (fs && fs->t == J_BOOL) s->fullscreen = fs->b;
    if (w && w->t == J_NUM && h && h->t == J_NUM && w->num >= 320 && h->num >= 240 && w->num <= 16384 && h->num <= 16384) { s->win_w = (int)w->num; s->win_h = (int)h->num; }
    json_free(j); return 0; }
int gp_settings_save_window(const char *path, int fullscreen, int w, int h) {
    if (!path || !path[0]) return -1; char b[200]; int n = snprintf(b, sizeof b, "{\"schema\":\"ccs-player-display/1\",\"fullscreen\":%s,\"w\":%d,\"h\":%d}\n", fullscreen ? "true" : "false", w, h);
    return gp_write_atomic(path, b, (size_t)n); }

/* ---------------- game preferences (see gp.h) ---------------- */
static const char *SPEED[3] = { "slow", "medium", "fast" };
static int speed_of(const jval *v) { if (v && v->t == J_STR) for (int i = 0; i < 3; i++) if (!strcmp(v->s, SPEED[i])) return i; return GP_UNSET; }
static int bool_of(const jval *v) { return v && v->t == J_BOOL ? v->b : GP_UNSET; }
static int read_json(const char *path, jval **out) {   /* 0 read, 1 no file, -1 unreadable or not a JSON object */
    uint8_t *b; size_t n; *out = NULL; if (!gp_exists(path)) return 1; if (gp_read_file(path, &b, &n, 1u << 16)) return -1;
    char e[120]; jval *j = json_parse((const char *)b, n, e, sizeof e); free(b); if (!j || j->t != J_OBJ) { json_free(j); return -1; } *out = j; return 0; }
static void read_rapid(const jval *r, int out[GP_PLAYERS][2], int *speed) {
    if (!r || r->t != J_OBJ) return;
    for (int p = 0; p < GP_PLAYERS; p++) { const jval *pp = json_get(r, p ? "p2" : "p1"); if (!pp || pp->t != J_OBJ) continue;
        int a = bool_of(json_get(pp, "a")), b = bool_of(json_get(pp, "b")); if (a != GP_UNSET) out[p][0] = a; if (b != GP_UNSET) out[p][1] = b; }
    int sp = speed_of(json_get(r, "speed")); if (sp != GP_UNSET) *speed = sp; }
void gp_defaults_builtin(gp_defaults *d) { memset(d, 0, sizeof *d); d->speed = GP_SPEED_MEDIUM; }
int gp_defaults_load(const char *dir, gp_defaults *d) {
    gp_defaults_builtin(d); if (!dir || !dir[0]) return 0; char p[1200]; gp_path(p, sizeof p, dir, "defaults.json"); jval *j; int r = read_json(p, &j);
    if (r) return r > 0 ? 0 : -1;
    const jval *sc = json_get(j, "schema"); if (!sc || sc->t != J_STR || strcmp(sc->s, "ccs-game-defaults/1")) { json_free(j); return -1; }
    read_rapid(json_get(j, "rapid"), d->rapid, &d->speed); json_free(j); return 0; }
int gp_defaults_save(const char *dir, const gp_defaults *d) {
    char p[1200], b[400]; if (!dir || !dir[0] || gp_mkdirs(dir)) return -1; gp_path(p, sizeof p, dir, "defaults.json");
    int n = snprintf(b, sizeof b, "{\"schema\":\"ccs-game-defaults/1\",\"rapid\":{\"p1\":{\"a\":%s,\"b\":%s},\"p2\":{\"a\":%s,\"b\":%s},\"speed\":\"%s\"}}\n",
                     d->rapid[0][0] ? "true" : "false", d->rapid[0][1] ? "true" : "false", d->rapid[1][0] ? "true" : "false", d->rapid[1][1] ? "true" : "false", SPEED[d->speed >= 0 && d->speed < 3 ? d->speed : 1]);
    return gp_write_atomic(p, b, (size_t)n); }
void gp_prefs_none(gp_prefs *p) { for (int i = 0; i < GP_PLAYERS; i++) p->rapid[i][0] = p->rapid[i][1] = GP_UNSET; p->speed = p->swap_ab = p->tv_aspect = p->integer_scale = GP_UNSET; }
static int sha_ok(const char *s) { if (!s || strlen(s) != 64) return 0; for (int i = 0; i < 64; i++) if (!((s[i] >= '0' && s[i] <= '9') || (s[i] >= 'a' && s[i] <= 'f'))) return 0; return 1; }
static void prefs_path(const char *dir, const char *sha, char *out, size_t n) { char f[80]; snprintf(f, sizeof f, "%s.json", sha); gp_path(out, n, dir, f); }
int gp_prefs_load(const char *dir, const char *sha, gp_prefs *p) {
    gp_prefs_none(p); if (!dir || !dir[0] || !sha_ok(sha)) return 0; char path[1200]; prefs_path(dir, sha, path, sizeof path);
    jval *j; int r = read_json(path, &j); if (r) return r > 0 ? 0 : -1;
    const jval *sc = json_get(j, "schema"), *id = json_get(j, "rom_content_sha256");
    if (!sc || sc->t != J_STR || strcmp(sc->s, "ccs-game-prefs/1") || !id || id->t != J_STR || strcmp(id->s, sha)) { json_free(j); return -1; }   /* another game's record is never used */
    read_rapid(json_get(j, "rapid"), p->rapid, &p->speed);
    const jval *c = json_get(j, "controls"); if (c && c->t == J_OBJ) p->swap_ab = bool_of(json_get(c, "swap_ab"));
    const jval *pi = json_get(j, "picture");
    if (pi && pi->t == J_OBJ) { const jval *a = json_get(pi, "aspect"); if (a && a->t == J_STR && (!strcmp(a->s, "tv") || !strcmp(a->s, "square"))) p->tv_aspect = !strcmp(a->s, "tv"); p->integer_scale = bool_of(json_get(pi, "integer")); }
    json_free(j); return 0; }
int gp_prefs_save(const char *dir, const char *sha, const char *title, const char *version, const gp_prefs *p) {
    if (!dir || !dir[0] || !sha_ok(sha) || gp_mkdirs(dir)) return -1; char path[1200]; prefs_path(dir, sha, path, sizeof path);
    char rp[GP_PLAYERS][64], r[300] = "", c[64] = "", pic[96] = "", t[500], v[500]; int any = 0;
    for (int i = 0; i < GP_PLAYERS; i++) { char a[16] = "", b[16] = "";
        if (p->rapid[i][0] != GP_UNSET) snprintf(a, sizeof a, "\"a\":%s", p->rapid[i][0] ? "true" : "false");
        if (p->rapid[i][1] != GP_UNSET) snprintf(b, sizeof b, "\"b\":%s", p->rapid[i][1] ? "true" : "false");
        rp[i][0] = 0; if (a[0] || b[0]) snprintf(rp[i], sizeof rp[i], "\"p%d\":{%s%s%s}", i + 1, a, a[0] && b[0] ? "," : "", b); }
    { size_t k = 0; const char *parts[3]; int np = 0; char sp[32] = "";
      if (rp[0][0]) parts[np++] = rp[0]; if (rp[1][0]) parts[np++] = rp[1]; if (p->speed != GP_UNSET) { snprintf(sp, sizeof sp, "\"speed\":\"%s\"", SPEED[p->speed]); parts[np++] = sp; }
      if (np) { k += (size_t)snprintf(r + k, sizeof r - k, ",\"rapid\":{"); for (int i = 0; i < np; i++) k += (size_t)snprintf(r + k, sizeof r - k, "%s%s", i ? "," : "", parts[i]); snprintf(r + k, sizeof r - k, "}"); any = 1; } }
    if (p->swap_ab != GP_UNSET) { snprintf(c, sizeof c, ",\"controls\":{\"swap_ab\":%s}", p->swap_ab ? "true" : "false"); any = 1; }
    if (p->tv_aspect != GP_UNSET || p->integer_scale != GP_UNSET) { char a[32] = "", i[32] = "";
        if (p->tv_aspect != GP_UNSET) snprintf(a, sizeof a, "\"aspect\":\"%s\"", p->tv_aspect ? "tv" : "square");
        if (p->integer_scale != GP_UNSET) snprintf(i, sizeof i, "\"integer\":%s", p->integer_scale ? "true" : "false");
        snprintf(pic, sizeof pic, ",\"picture\":{%s%s%s}", a, a[0] && i[0] ? "," : "", i); any = 1; }
    if (!any) { if (!gp_exists(path)) return 0; return gp_remove(path); }   /* Reset: no record, the game follows every default again */
    json_escape(title ? title : "", t, sizeof t); json_escape(version ? version : "", v, sizeof v);
    char buf[2000]; int n = snprintf(buf, sizeof buf, "{\"schema\":\"ccs-game-prefs/1\",\"rom_content_sha256\":\"%s\",\"title\":%s,\"version\":%s%s%s%s}\n", sha, t, v, r, c, pic);
    return gp_write_atomic(path, buf, (size_t)n); }
void gp_resolve(const gp_defaults *d, const gp_prefs *p, const gp_settings *s, gp_effective *e) {
    for (int i = 0; i < GP_PLAYERS; i++) for (int b = 0; b < 2; b++) e->rapid[i][b] = p->rapid[i][b] != GP_UNSET ? p->rapid[i][b] : d->rapid[i][b];
    e->speed = p->speed != GP_UNSET ? p->speed : d->speed; e->swap_ab = p->swap_ab != GP_UNSET ? p->swap_ab : 0;
    e->tv_aspect = p->tv_aspect != GP_UNSET ? p->tv_aspect : s->tv_aspect; e->integer_scale = p->integer_scale != GP_UNSET ? p->integer_scale : s->integer_scale; }
int gp_rapid_half(int speed) { return speed == GP_SPEED_SLOW ? 5 : speed == GP_SPEED_FAST ? 2 : 3; }
