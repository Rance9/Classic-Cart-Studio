/* gp_pads.c: generic (DirectInput-style) controllers, the same on every host. A host reports each controller's raw state (32 buttons,
 * the D-pad hat (POV) and six axes); this file turns it into NES actions with the controller's mapping, learns a mapping from what the
 * person presses (a button, a hat direction or an axis direction), names inputs for the screen, and keeps each controller's mapping in
 * player-pads.json. Xbox-compatible controllers are read through XInput by the Windows host and never pass through here. */
#include "gp.h"
#include "../../console-simulator/core/ccs_sim.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define AXIS_ON 500   /* of -1000..1000: past half way */
#include "gp_pad_db.h"

/* controllers that play without setup (F17, D-99), by USB vendor and product id. First the ones checked on real hardware here, then the
   SDL_GameControllerDB table (gp_pad_db.h). A setup the person saved always wins over both. */
static const struct { unsigned short vid, pid; signed char map[8]; const char *what; } VERIFIED[] = {
    /* the owner's Nintendo NES Classic Edition controller (CLV-002) through its USB adapter (Elecom JC-W01U, Windows' built-in HID driver):
       A = button 4, B = button 3, SELECT = 9, START = 10, the D-pad as the hat (final8 physical session, MILESTONES-2-3-FINAL16-CORRECTIONS.md;
       the same mapping the owner set up and verified in hm4 qualification) */
    { 0x056e, 0x2007, { 100, 102, 103, 101, 3, 2, 8, 9 }, "verified" },
};
int gp_known_map(int vid, int pid, int map[GA_COUNT], const char **source) {
    for (size_t i = 0; i < sizeof VERIFIED / sizeof VERIFIED[0]; i++) if (VERIFIED[i].vid == vid && VERIFIED[i].pid == pid) {
        for (int a = 0; a < GA_COUNT; a++) map[a] = VERIFIED[i].map[a]; if (source) *source = VERIFIED[i].what; return 1; }
    size_t lo = 0, hi = sizeof GP_PAD_DB / sizeof GP_PAD_DB[0];
    while (lo < hi) { size_t m = (lo + hi) / 2; long k = (long)GP_PAD_DB[m].vid << 16 | GP_PAD_DB[m].pid, w = (long)vid << 16 | pid;
        if (k == w) { for (int a = 0; a < GA_COUNT; a++) map[a] = GP_PAD_DB[m].map[a]; if (source) *source = "sdl-gamecontrollerdb"; return 1; }
        if (k < w) lo = m + 1; else hi = m; }
    return 0; }

static int pov_dir(int pov, int d) {   /* d: 0 up, 1 right, 2 down, 3 left; a diagonal counts for both of its directions */
    if (pov < 0 || pov >= 36000) return 0;
    switch (d) { case 0: return pov >= 31500 || pov <= 4500; case 1: return pov >= 4500 && pov <= 13500; case 2: return pov >= 13500 && pov <= 22500; default: return pov >= 22500 && pov <= 31500; } }
int gp_input_active(const gp_raw *r, int code) {
    if (code >= 0 && code < 32) return r->btn[code] != 0;
    if (code >= GP_IN_POV && code < GP_IN_POV + 4) return pov_dir(r->pov, code - GP_IN_POV);
    if (code >= GP_IN_AXIS && code < GP_IN_AXIS + 2 * GP_AXES) { int a = (code - GP_IN_AXIS) / 2, pos = (code - GP_IN_AXIS) % 2; return pos ? r->axis[a] > AXIS_ON : r->axis[a] < -AXIS_ON; }
    return 0; }
void gp_map_default(int map[GA_COUNT]) {   /* the common generic layout: 1 = A, 0 = B, 8 = SELECT, 9 = START; directions from the hat and the stick */
    static const int DEF[GA_COUNT] = { -1, -1, -1, -1, 1, 0, 8, 9 }; memcpy(map, DEF, sizeof DEF); }
void gp_raw_actions(const gp_raw *r, const int map[GA_COUNT], uint8_t act[GA_COUNT]) {
    static const int HAT[4] = { GP_IN_POV + 0, GP_IN_POV + 2, GP_IN_POV + 3, GP_IN_POV + 1 };   /* GA_UP, GA_DOWN, GA_LEFT, GA_RIGHT */
    static const int STICK[4] = { GP_IN_AXIS + 2, GP_IN_AXIS + 3, GP_IN_AXIS + 0, GP_IN_AXIS + 1 };   /* Y-, Y+, X-, X+ */
    for (int a = 0; a < GA_COUNT; a++) {
        int on = map[a] >= 0 && gp_input_active(r, map[a]);
        if (a <= GA_RIGHT) on |= gp_input_active(r, HAT[a]) || gp_input_active(r, STICK[a]);   /* directions always work from the hat and the stick */
        act[a] = (uint8_t)on; } }
void gp_input_name(int code, char *out, size_t n) {
    static const char *D[4] = { "up", "right", "down", "left" }, *AX[GP_AXES] = { "X", "Y", "Z", "Rx", "Ry", "Rz" };
    if (code >= 0 && code < 32) snprintf(out, n, "button %d", code + 1);
    else if (code >= GP_IN_POV && code < GP_IN_POV + 4) snprintf(out, n, "D-pad %s", D[code - GP_IN_POV]);
    else if (code >= GP_IN_AXIS && code < GP_IN_AXIS + 2 * GP_AXES) { int a = (code - GP_IN_AXIS) / 2, pos = (code - GP_IN_AXIS) % 2;
        if (a == 0) snprintf(out, n, "stick %s", pos ? "right" : "left"); else if (a == 1) snprintf(out, n, "stick %s", pos ? "down" : "up"); else snprintf(out, n, "axis %s%s", AX[a], pos ? "+" : "-"); }
    else snprintf(out, n, "none"); }
static int elem_code(int e) { return e < 32 ? e : e < 36 ? GP_IN_POV + (e - 32) : GP_IN_AXIS + (e - 36); }
void gp_learn_begin(gp_learn *l, const gp_raw *r) { for (int e = 0; e < GP_IN_ELEMS; e++) l->armed[e] = !gp_input_active(r, elem_code(e)); }
int gp_learn_poll(gp_learn *l, const gp_raw *r) {
    int found = -1;
    for (int e = 0; e < GP_IN_ELEMS; e++) { int on = gp_input_active(r, elem_code(e));
        if (!on) l->armed[e] = 1;                                   /* released (or never held): it may count next time */
        else if (l->armed[e] && found < 0) { found = elem_code(e); l->armed[e] = 0; } }   /* a press: one at a time, then it must be released */
    return found; }

/* player-pads.json: { "<controller id>": [up, down, left, right, A, B, SELECT, START], ... } with input codes; other entries are kept */
int gp_pads_load(const char *path, const char *key, int map[GA_COUNT]) {
    uint8_t *b; size_t n; if (!path || !path[0] || gp_read_file(path, &b, &n, 1u << 20)) return 0;
    char err[80]; jval *j = json_parse((const char *)b, n, err, sizeof err); free(b); if (!j) return 0;
    int found = 0; const jval *e = j->t == J_OBJ ? json_get(j, key) : NULL;
    if (e && e->t == J_ARR && e->n == GA_COUNT) { found = 1; for (int a = 0; a < GA_COUNT; a++) if (e->items[a]->t == J_NUM) map[a] = (int)e->items[a]->num; }
    json_free(j); return found; }
int gp_pads_save(const char *path, const char *key, const int map[GA_COUNT]) {
    if (!path || !path[0]) return -1;
    char *buf = malloc(65536); if (!buf) return -1; size_t o = 0; o += (size_t)snprintf(buf + o, 65536 - o, "{");
    char ek[200]; json_escape(key, ek, sizeof ek);
    o += (size_t)snprintf(buf + o, 65536 - o, "%s:[%d,%d,%d,%d,%d,%d,%d,%d]", ek, map[0], map[1], map[2], map[3], map[4], map[5], map[6], map[7]);
    uint8_t *b; size_t n;
    if (!gp_read_file(path, &b, &n, 1u << 20)) { char err[80]; jval *j = json_parse((const char *)b, n, err, sizeof err); free(b);   /* the other controllers' entries stay */
        if (j && j->t == J_OBJ) for (size_t i = 0; i < j->n && o < 60000; i++) { const jval *e = j->items[i];
            if (!strcmp(j->keys[i], key) || e->t != J_ARR || e->n != GA_COUNT) continue; char k2[200]; json_escape(j->keys[i], k2, sizeof k2);
            o += (size_t)snprintf(buf + o, 65536 - o, ",%s:[", k2);
            for (int a = 0; a < GA_COUNT; a++) o += (size_t)snprintf(buf + o, 65536 - o, "%s%d", a ? "," : "", e->items[a]->t == J_NUM ? (int)e->items[a]->num : -1);
            o += (size_t)snprintf(buf + o, 65536 - o, "]"); }
        json_free(j); }
    o += (size_t)snprintf(buf + o, 65536 - o, "}\n"); int r = gp_write_atomic(path, buf, o); free(buf); return r; }

/* who plays which player. Controllers play Players 1 and 2 (the host's order). The keyboard's keys play the player no controller plays:
   Player 1 with no controller, Player 2 when one controller plays Player 1, Player 1 when it plays Player 2; with two controllers they
   still play Player 1. Its second set of keys is only for two people sharing the keyboard: Player 2, and only with no controller. */
void gp_keyboard_players(const int has[GP_PLAYERS], int *main_p, int *second_p) {
    int n = (has[0] != 0) + (has[1] != 0);
    if (!n) { *main_p = 0; *second_p = 1; } else if (n == 1) { *main_p = has[0] ? 1 : 0; *second_p = -1; } else { *main_p = 0; *second_p = -1; } }
void gp_assign_players(int n, const int pick[GP_PLAYERS], int swap, int *who) {
    for (int i = 0; i < n; i++) who[i] = -1;
    if (pick[0] == GP_PICK_AUTO && pick[1] == GP_PICK_AUTO) { for (int i = 0; i < n && i < GP_PLAYERS; i++) who[i] = (i + swap) % GP_PLAYERS; return; }
    int taken[GP_PLAYERS] = { 0, 0 };
    for (int p = 0; p < GP_PLAYERS; p++) { if (pick[p] == GP_PICK_NONE) taken[p] = 1; else if (pick[p] >= 0 && pick[p] < n && who[pick[p]] < 0) { who[pick[p]] = p; taken[p] = 1; } }
    for (int i = 0; i < n; i++) if (who[i] < 0) for (int p = 0; p < GP_PLAYERS; p++) if (!taken[p]) { who[i] = p; taken[p] = 1; break; } }   /* a player without a choice: the next controller in order */
void gp_players_text(const char *ctl[GP_PLAYERS], char *out, size_t n) {
    int has[GP_PLAYERS] = { ctl[0] != NULL, ctl[1] != NULL }, mp, sp; gp_keyboard_players(has, &mp, &sp); char who[GP_PLAYERS][240];
    for (int p = 0; p < GP_PLAYERS; p++) {
#if defined(CCS_CONSOLE)   /* the console has no keyboard */
        (void)mp; (void)sp; if (ctl[p]) snprintf(who[p], sizeof who[p], "the %s", ctl[p]); else snprintf(who[p], sizeof who[p], "no one"); }
    const char *none = " No controller is connected; plug one into port 1 or 2 on the front of the console.";
#else
        if (ctl[p]) snprintf(who[p], sizeof who[p], "the %s%s", ctl[p], mp == p ? " and the keyboard" : "");
        else snprintf(who[p], sizeof who[p], "%s", mp == p ? "the keyboard" : sp == p ? "the keyboard's second set of keys" : "no one"); }
#endif
#if defined(CCS_CONSOLE)
#elif defined(__wasm__)   /* the browser shows a controller to a page only after one of its buttons is pressed */
    const char *none = " No controller is connected; a USB or Bluetooth controller joins when this browser sees it (press one of its buttons).";
#else
    const char *none = " No controller is connected; Xbox-compatible and other USB or Bluetooth controllers join when Windows sees them.";
#endif
    snprintf(out, n, "Player 1: %s. Player 2: %s.%s", who[0], who[1], has[0] || has[1] ? "" : none); }
