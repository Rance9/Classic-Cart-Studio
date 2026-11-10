/* gp_save.c: one game's save library, <saves root>/<ROM content SHA-256>/:
 *   cart.sav                 the cartridge's battery save (exactly the bytes the game keeps; FCEUmm's .sav layout)
 *   slot1..slot4.ccsstate    the four manual slots, the only emulator saves: each written only when the person saves to it
 *   last-used.json           which slot was last loaded or saved (metadata naming a slot and the state it held, never a save itself)
 *   continue.ccsstate        legacy: the automatic continuation of earlier test versions; never written or loaded on its own and never
 *                            deleted here, only copied into a slot the person chooses (gp_legacy_copy)
 *   game.json                the last title it was played under (for people looking at the folder; never part of the key)
 *   slot-names.json          the person's optional names for the four slots (F4, D-90): a label of the slot, never part of a save; naming
 *                            or renaming writes only this file, so a state's bytes and its saved time never change by it
 *   session.lock             held while a player has the game open: a second session cannot write the same saves
 * A .ccsstate file is "CCSSTATE", a 32-bit little-endian header length, a JSON header, then the emulator's state. It is written by
 * atomic replacement, so a slot is always the old state or the new one. A state is loaded only when its header names this exact ROM,
 * this core version and this platform and its size and CRC-32 match: anything else is shown as incompatible or invalid, never
 * loaded, never deleted. Title, file location, collection, loadout and HOME settings are not part of the key. */
#ifndef _WIN32
#define _POSIX_C_SOURCE 200809L
#endif
#include "gp.h"
#include "../../console-simulator/core/ccs_sim.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#ifdef _WIN32
#include <windows.h>
#elif !defined(__wasi__)
#include <errno.h>
#include <fcntl.h>
#include <unistd.h>
#include <sys/file.h>
#endif

static const char MAGIC[8] = { 'C', 'C', 'S', 'S', 'T', 'A', 'T', 'E' };
#define MAX_STATE (8u << 20)

/* a state made by another build of the same pinned core loads when that build is one whose states were PROVEN identical to this one's: the
   Windows and the browser (WebAssembly) builds write byte-identical emulator states and play on identically from each other's (ws12
   t_wasm_parity P3/P6). Any other platform stays "made by another player version or platform" until such a check covers it */
int gp_platform_ok(const char *p) {
    static const char *SAME[] = { "windows-x86_64", "web-wasm32" };
    if (!strcmp(p, GP_PLATFORM)) return 1; int me = 0, it = 0;
    for (size_t i = 0; i < sizeof SAME / sizeof SAME[0]; i++) { me |= !strcmp(GP_PLATFORM, SAME[i]); it |= !strcmp(p, SAME[i]); }
    return me && it; }
static int valid_sha(const char *s) { if (!s || strlen(s) != 64) return 0; for (int i = 0; i < 64; i++) if (!((s[i] >= '0' && s[i] <= '9') || (s[i] >= 'a' && s[i] <= 'f'))) return 0; return 1; }
static void jstr_into(const char *s, char *out, size_t n) { json_escape(s ? s : "", out, n); }

int gp_lib_open(gp_library *L, const char *root, const char *rom_sha, const char *title, char *why, size_t whylen) {
    memset(L, 0, sizeof *L);
    if (!valid_sha(rom_sha)) { snprintf(why, whylen, "The game has no exact identity, so its saves cannot be kept."); return -1; }
    snprintf(L->rom_sha, sizeof L->rom_sha, "%s", rom_sha); snprintf(L->title, sizeof L->title, "%s", title ? title : "");
    gp_path(L->dir, sizeof L->dir, root, rom_sha);
    if (gp_mkdirs(L->dir)) { snprintf(why, whylen, "The folder for saved games could not be created. Check that the disk is not full or read-only."); return -1; }
    gp_path(L->lock_path, sizeof L->lock_path, L->dir, "session.lock");
#ifdef _WIN32
    int n = MultiByteToWideChar(CP_UTF8, 0, L->lock_path, -1, NULL, 0); wchar_t *w = malloc((size_t)n * sizeof *w); MultiByteToWideChar(CP_UTF8, 0, L->lock_path, -1, w, n);
    HANDLE h = CreateFileW(w, GENERIC_WRITE, 0, NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_DELETE_ON_CLOSE, NULL); free(w);
    if (h == INVALID_HANDLE_VALUE) { snprintf(why, whylen, "This game is already open in another player, so its saves are in use. Close that game first."); return -2; }
    L->lock = h;
#elif defined(__wasi__)
    /* one page, one thread: the browser runtime holds a lock per game (Web Locks) before it opens a session */
#else
    if (gp_write_check && gp_write_check(L->lock_path, 0)) { snprintf(why, whylen, "This game's saves cannot be opened here (outside the folders this session may write)."); return -1; }
    int fd = gp_open_file(L->lock_path, 0);   /* never through a link (P3-F1) */
    if (fd < 0 && errno == EPERM) { snprintf(why, whylen, "This game's saves cannot be opened here (something other than its own lock file stands in its saves folder)."); return -1; }
    if (fd < 0 || flock(fd, LOCK_EX | LOCK_NB)) { if (fd >= 0) close(fd); snprintf(why, whylen, "This game is already open in another player, so its saves are in use."); return -2; }
    L->lock = (void *)(intptr_t)(fd + 1);
#endif
    char p[1200], t[400], buf[800], now[32]; gp_path(p, sizeof p, L->dir, "game.json"); gp_now_iso(now); jstr_into(title, t, sizeof t);
    int len = snprintf(buf, sizeof buf, "{\"schema\":\"ccs-save-game/1\",\"rom_content_sha256\":\"%s\",\"last_title\":%s,\"updated\":\"%s\"}\n", rom_sha, t, now);
    gp_write_atomic(p, buf, (size_t)len);   /* informational only: a failure here never blocks play */
    return 0; }
void gp_lib_close(gp_library *L) {
#ifdef _WIN32
    if (L->lock) CloseHandle((HANDLE)L->lock);
#elif defined(__wasi__)
#else
    if (L->lock) { int fd = (int)(intptr_t)L->lock - 1; flock(fd, LOCK_UN); close(fd); }
#endif
    L->lock = NULL; }

static void state_path(gp_library *L, const char *name, char *out, size_t n) { char f[64]; snprintf(f, sizeof f, "%s.ccsstate", name); gp_path(out, n, L->dir, f); }
static const char *jstr(const jval *o, const char *k) { const jval *v = json_get(o, k); return v && v->t == J_STR ? v->s : ""; }
static double jnum(const jval *o, const char *k) { const jval *v = json_get(o, k); return v && v->t == J_NUM ? v->num : -1; }

/* reads and checks a state file; on GS_OK payload and plen point into whole (the caller frees whole) */
static gp_state_status check(gp_library *L, const char *name, gp_state_info *info, uint8_t **whole, uint8_t **payload, size_t *plen) {
    char p[1200]; state_path(L, name, p, sizeof p); memset(info, 0, sizeof *info); *whole = NULL;
    if (!gp_exists(p)) { info->st = GS_EMPTY; return GS_EMPTY; }
    uint8_t *b; size_t n;
    if (gp_read_file(p, &b, &n, MAX_STATE + 65536)) { info->st = GS_INVALID; snprintf(info->why, sizeof info->why, "the file cannot be read"); return GS_INVALID; }
    uint32_t hl = n >= 12 ? (uint32_t)b[8] | (uint32_t)b[9] << 8 | (uint32_t)b[10] << 16 | (uint32_t)b[11] << 24 : 0;
    if (n < 12 || memcmp(b, MAGIC, 8) || hl == 0 || hl > 65536 || 12 + (size_t)hl > n) { free(b); info->st = GS_INVALID; snprintf(info->why, sizeof info->why, "not a Classic Cart Studio save"); return GS_INVALID; }
    char err[120]; jval *h = json_parse((const char *)b + 12, hl, err, sizeof err);
    if (!h || h->t != J_OBJ || strcmp(jstr(h, "schema"), "ccs-save-state/1")) { json_free(h); free(b); info->st = GS_INVALID; snprintf(info->why, sizeof info->why, "the save's description is damaged"); return GS_INVALID; }
    snprintf(info->created, sizeof info->created, "%s", jstr(h, "created")); snprintf(info->platform, sizeof info->platform, "%s", jstr(h, "platform"));
    snprintf(info->core_version, sizeof info->core_version, "%s", jstr(h, "core_version")); snprintf(info->title, sizeof info->title, "%s", jstr(h, "title"));
    info->frames = (uint64_t)(jnum(h, "frames") > 0 ? jnum(h, "frames") : 0); double sz = jnum(h, "size"); info->size = sz > 0 ? (size_t)sz : 0;
    snprintf(info->crc32, sizeof info->crc32, "%s", jstr(h, "crc32"));
    gp_state_status st = GS_OK;
    if (strcmp(jstr(h, "rom_content_sha256"), L->rom_sha)) { st = GS_INVALID; snprintf(info->why, sizeof info->why, "it belongs to a different game file"); }
    else if (strcmp(jstr(h, "core"), GP_CORE_ID) || strcmp(info->core_version, GP_CORE_VERSION) || !gp_platform_ok(info->platform)) {
        st = GS_INCOMPATIBLE; snprintf(info->why, sizeof info->why, "made by another player version or platform (%s %s)", info->platform, info->core_version); }
    else {
        size_t pl = n - 12 - hl; char crc[16]; snprintf(crc, sizeof crc, "%08x", gp_crc32(b + 12 + hl, pl));
        if (pl != info->size || strcmp(crc, jstr(h, "crc32"))) { st = GS_INVALID; snprintf(info->why, sizeof info->why, "the save is damaged (its check does not match)"); }
        else { *payload = b + 12 + hl; *plen = pl; }
    }
    json_free(h); info->st = st;
    if (st == GS_OK) *whole = b; else free(b);
    return st; }
void gp_state_info_read(gp_library *L, const char *name, gp_state_info *out) { uint8_t *w = NULL, *p; size_t n; check(L, name, out, &w, &p, &n); free(w); }
int gp_state_read(gp_library *L, const char *name, uint8_t **data, size_t *len, char *why, size_t whylen) {
    gp_state_info info; uint8_t *w = NULL, *p = NULL; size_t n = 0; *data = NULL; *len = 0;
    if (check(L, name, &info, &w, &p, &n) != GS_OK) { snprintf(why, whylen, "%s", info.st == GS_EMPTY ? "This slot is empty." : info.why); free(w); return -1; }
    uint8_t *c = malloc(n); if (!c) { free(w); snprintf(why, whylen, "Out of memory."); return -1; }
    memcpy(c, p, n); free(w); *data = c; *len = n; return 0; }
static int slot_name(const char *name) { return !strncmp(name, "slot", 4) && name[4] >= '1' && name[4] < '1' + GP_SLOTS && !name[5]; }
/* created: the save's own time (NULL: now); origin: where a copied state came from (NULL: saved by playing) */
static int state_write(gp_library *L, const char *name, const void *data, size_t len, uint64_t frames, const char *created, const char *origin, char *why, size_t whylen) {
    if (!slot_name(name)) { snprintf(why, whylen, "Only the four save slots are written."); return -1; }   /* never a continuation */
    if (!data || !len || len > MAX_STATE) { snprintf(why, whylen, "The game could not be saved (the emulator gave no state)."); return -1; }
    char hdr[1200], t[400], now[32], og[64] = ""; if (created && created[0]) snprintf(now, sizeof now, "%s", created); else gp_now_iso(now); jstr_into(L->title, t, sizeof t);
    if (origin) snprintf(og, sizeof og, ",\"origin\":\"%s\"", origin);
    int hl = snprintf(hdr, sizeof hdr, "{\"schema\":\"ccs-save-state/1\",\"kind\":\"manual\",\"rom_content_sha256\":\"%s\",\"title\":%s,\"core\":\"%s\",\"core_version\":\"%s\",\"platform\":\"%s\",\"size\":%zu,\"crc32\":\"%08x\",\"frames\":%llu,\"created\":\"%s\"%s}",
                      L->rom_sha, t, GP_CORE_ID, GP_CORE_VERSION, GP_PLATFORM, len, gp_crc32(data, len), (unsigned long long)frames, now, og);
    size_t tot = 12 + (size_t)hl + len; uint8_t *b = malloc(tot); if (!b) { snprintf(why, whylen, "Out of memory."); return -1; }
    memcpy(b, MAGIC, 8); b[8] = (uint8_t)hl; b[9] = (uint8_t)(hl >> 8); b[10] = (uint8_t)(hl >> 16); b[11] = (uint8_t)(hl >> 24);
    memcpy(b + 12, hdr, (size_t)hl); memcpy(b + 12 + hl, data, len);
    char p[1200]; state_path(L, name, p, sizeof p); int r = gp_write_atomic(p, b, tot); free(b);
    if (r) { snprintf(why, whylen, "The save could not be written. The disk may be full or read-only; the previous save is unchanged."); return -1; }
    gp_state_info chk; gp_state_info_read(L, name, &chk);   /* read back: success is reported only for a save that loads */
    if (chk.st != GS_OK) { snprintf(why, whylen, "The save was written but cannot be read back (%s). The previous save may be lost; try another slot.", chk.why); return -1; }
    return 0; }
int gp_state_write(gp_library *L, const char *name, const void *data, size_t len, uint64_t frames, char *why, size_t whylen) { return state_write(L, name, data, len, frames, NULL, NULL, why, whylen); }
int gp_sram_load(gp_library *L, uint8_t *dst, size_t len, char *why, size_t whylen) {
    char p[1200]; gp_path(p, sizeof p, L->dir, "cart.sav"); if (!gp_exists(p)) return 0;
    uint8_t *b; size_t n; if (gp_read_file(p, &b, &n, 1u << 20)) { snprintf(why, whylen, "This game's cartridge save on this computer could not be read."); return -1; }
    if (n != len) { free(b); snprintf(why, whylen, "This game's cartridge save on this computer has an unexpected size (%zu bytes; the game uses %zu).", n, len); return -1; }
    memcpy(dst, b, len); free(b); return 1; }
int gp_sram_save(gp_library *L, const void *src, size_t len, char *why, size_t whylen) {
    char p[1200]; gp_path(p, sizeof p, L->dir, "cart.sav");
    if (gp_write_atomic(p, src, len)) { snprintf(why, whylen, "The cartridge save could not be written; the disk may be full, or the file read-only."); return -1; }
    return 0; }
int gp_sram_set_aside(gp_library *L, char *name, size_t nlen, char *why, size_t whylen) {   /* the unusable file keeps its bytes under a new name */
    char p[1200], q[1200], iso[32], stamp[24]; gp_path(p, sizeof p, L->dir, "cart.sav"); gp_now_iso(iso); int k = 0;
    for (const char *c = iso; *c && k < 14; c++) if (*c >= '0' && *c <= '9') stamp[k++] = *c; stamp[k] = 0;
    for (int i = 0; i < 100; i++) { if (i) snprintf(name, nlen, "cart-set-aside-%s-%d.sav", stamp, i); else snprintf(name, nlen, "cart-set-aside-%s.sav", stamp);
        gp_path(q, sizeof q, L->dir, name); if (gp_exists(q)) continue;
        if (gp_rename_new(p, q) == 0) return 0; break; }
    snprintf(why, whylen, "The file could not be renamed; it is unchanged."); return -1; }
int gp_any_slot(gp_library *L) { gp_state_info i;
    for (int s = 1; s <= GP_SLOTS; s++) { char n[8]; snprintf(n, sizeof n, "slot%d", s); gp_state_info_read(L, n, &i); if (i.st != GS_EMPTY) return 1; } return 0; }
int gp_legacy_copy(gp_library *L, int slot, char *why, size_t whylen) {
    gp_state_info li; uint8_t *w = NULL, *pl = NULL; size_t n = 0; char sn[8]; snprintf(sn, sizeof sn, "slot%d", slot);
    if (check(L, GP_LEGACY, &li, &w, &pl, &n) != GS_OK) { snprintf(why, whylen, "%s", li.st == GS_EMPTY ? "There is no earlier automatic save." : li.why); free(w); return -1; }
    int r = state_write(L, sn, pl, n, li.frames, li.created, "legacy_continuation", why, whylen); free(w); return r; }
/* ---- slot names: {"schema":"ccs-slot-names/1","rom_content_sha256":...,"names":{"1":"Before the castle",...}} ---- */
static void names_path(gp_library *L, char *p, size_t n) { gp_path(p, n, L->dir, "slot-names.json"); }
void gp_slot_name(gp_library *L, int slot, char *out, size_t n) {
    out[0] = 0; char p[1200]; names_path(L, p, sizeof p); uint8_t *b; size_t len; if (slot < 1 || slot > GP_SLOTS || gp_read_file(p, &b, &len, 65536)) return;
    char err[80], k[4]; jval *j = json_parse((const char *)b, len, err, sizeof err); free(b); snprintf(k, sizeof k, "%d", slot);
    const jval *nm = j && j->t == J_OBJ && !strcmp(jstr(j, "schema"), "ccs-slot-names/1") && !strcmp(jstr(j, "rom_content_sha256"), L->rom_sha) ? json_get(j, "names") : NULL;
    const jval *v = nm && nm->t == J_OBJ ? json_get(nm, k) : NULL; if (v && v->t == J_STR) snprintf(out, n, "%.*s", GP_NAME_MAX, v->s); json_free(j); }
int gp_slot_name_set(gp_library *L, int slot, const char *name) {   /* "" removes the name; 0 written */
    if (slot < 1 || slot > GP_SLOTS) return -1; char cur[GP_SLOTS][GP_NAME_MAX * 4 + 1]; for (int s = 1; s <= GP_SLOTS; s++) gp_slot_name(L, s, cur[s - 1], sizeof cur[s - 1]);
    snprintf(cur[slot - 1], sizeof cur[slot - 1], "%.*s", GP_NAME_MAX * 4, name ? name : "");
    char buf[2400], e[GP_NAME_MAX * 8 + 8]; size_t o = (size_t)snprintf(buf, sizeof buf, "{\"schema\":\"ccs-slot-names/1\",\"rom_content_sha256\":\"%s\",\"names\":{", L->rom_sha); int first = 1;
    for (int s = 1; s <= GP_SLOTS; s++) if (cur[s - 1][0]) { json_escape(cur[s - 1], e, sizeof e); o += (size_t)snprintf(buf + o, sizeof buf - o, "%s\"%d\":%s", first ? "" : ",", s, e); first = 0; }
    o += (size_t)snprintf(buf + o, sizeof buf - o, "}}\n"); char p[1200]; names_path(L, p, sizeof p); return gp_write_atomic(p, buf, o) ? -1 : 0; }
/* ---- the last-used slot ---- */
void gp_last_read(gp_library *L, gp_last *out) {
    memset(out, 0, sizeof *out); char p[1200]; gp_path(p, sizeof p, L->dir, "last-used.json");
    if (!gp_exists(p)) { out->st = LU_NONE; return; }
    uint8_t *b; size_t n; char err[120]; if (gp_read_file(p, &b, &n, 65536)) { out->st = LU_DAMAGED; snprintf(out->why, sizeof out->why, "the note of the last used save cannot be read"); return; }
    jval *j = json_parse((const char *)b, n, err, sizeof err); free(b);
    double sl = j && j->t == J_OBJ ? jnum(j, "slot") : -1;
    if (!j || j->t != J_OBJ || strcmp(jstr(j, "schema"), "ccs-last-used/1") || strcmp(jstr(j, "rom_content_sha256"), L->rom_sha) || sl < 1 || sl > GP_SLOTS || sl != (int)sl) {
        json_free(j); out->st = LU_DAMAGED; snprintf(out->why, sizeof out->why, "the note of the last used save is damaged or belongs to another game file"); return; }
    out->slot = (int)sl; snprintf(out->at, sizeof out->at, "%s", jstr(j, "at")); snprintf(out->how, sizeof out->how, "%s", jstr(j, "how"));
    char crc[16], created[32]; snprintf(crc, sizeof crc, "%s", jstr(j, "slot_crc32")); snprintf(created, sizeof created, "%s", jstr(j, "slot_created")); json_free(j);
    char sn[8]; snprintf(sn, sizeof sn, "slot%d", out->slot); gp_state_info_read(L, sn, &out->info);
    if (out->info.st != GS_OK) { out->st = LU_UNUSABLE; snprintf(out->why, sizeof out->why, "%s", out->info.st == GS_EMPTY ? "that slot is now empty" : out->info.why); return; }
    if (strcmp(crc, out->info.crc32) || strcmp(created, out->info.created)) { out->st = LU_CHANGED; snprintf(out->why, sizeof out->why, "that slot now holds a different save"); return; }
    out->st = LU_OK; }
int gp_last_write(gp_library *L, int slot, const char *how) {
    char sn[8]; snprintf(sn, sizeof sn, "slot%d", slot); gp_state_info i; gp_state_info_read(L, sn, &i); if (i.st != GS_OK) return -1;
    char p[1200], buf[600], now[32]; gp_path(p, sizeof p, L->dir, "last-used.json"); gp_now_iso(now);
    int len = snprintf(buf, sizeof buf, "{\"schema\":\"ccs-last-used/1\",\"rom_content_sha256\":\"%s\",\"slot\":%d,\"slot_created\":\"%s\",\"slot_crc32\":\"%s\",\"how\":\"%s\",\"at\":\"%s\"}\n",
                       L->rom_sha, slot, i.created, i.crc32, how, now);
    if (gp_write_atomic(p, buf, (size_t)len)) { gp_remove(p); return -1; }   /* never leave a note that names the wrong slot */
    return 0; }
