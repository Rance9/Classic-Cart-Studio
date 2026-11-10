/* Classic Cart Studio gameplay: the shared part of the player (Windows today, the console later).
 *
 * Shared, portable C11 with no operating-system calls outside gp_util.c's small file layer:
 *   identity   a game is its exact ROM content (SHA-256 of the bytes after any iNES header and trainer, the Studio's identity);
 *              a ROM is verified against that hash before it runs, and every save belongs to it
 *   saves      per game: the cartridge's own battery save and four manual slots (no automatic continuation: a slot is written only when the
 *              person saves to it), plus which slot was last used, in a per-user folder independent of the installation, collections,
 *              loadouts and HOME; each state is one file written by atomic replacement
 *   session    the game lifecycle (start choices, play, the held SELECT+START menu, saving to a slot, return to HOME) as a state machine
 *              driven by NES actions; the platform host only supplies a window, sound and controllers (host.h)
 * The emulator is FCEUmm (third_party/fceumm, libretro API), one game at a time. */
#ifndef CCS_GP_H
#define CCS_GP_H
#include <stdint.h>
#include <stddef.h>

#define GP_CORE_ID "fceumm"
#define GP_CORE_VERSION "ac0d00a7"          /* the pinned upstream commit; a state from another core version is never loaded */
#if defined(_WIN32)
#define GP_PLATFORM "windows-x86_64"
#elif defined(__wasm__)
#define GP_PLATFORM "web-wasm32"
#elif defined(__arm__)
#define GP_PLATFORM "linux-armv7"
#else
#define GP_PLATFORM "linux-x86_64"
#endif

/* ---- NES actions, per player ---- */
enum { GA_UP, GA_DOWN, GA_LEFT, GA_RIGHT, GA_A, GA_B, GA_SELECT, GA_START, GA_COUNT };
#define GP_PLAYERS 2
#define GP_PADQ 32
typedef struct { uint8_t down[GP_PLAYERS][GA_COUNT]; uint8_t nq, q[GP_PADQ]; } gp_pad;   /* what the controllers and keys hold right now; q: every press since the last poll in order, player << 4 | action, from a host that keeps them (the console's; nq 0 elsewhere) */
const char *gp_action_name(int a);

/* ---- utilities (gp_util.c): UTF-8 paths on every platform ---- */
void gp_sha256(const void *data, size_t len, char hex[65]);
uint32_t gp_crc32(const void *data, size_t len);
int gp_read_file(const char *path, uint8_t **buf, size_t *len, size_t max_len);   /* 0 ok */
int gp_write_atomic(const char *path, const void *data, size_t len);              /* 0 ok; on failure the old file is untouched */
int gp_mkdirs(const char *dir);
int gp_exists(const char *path);
int gp_remove(const char *path);
int gp_rename_new(const char *from, const char *to);                             /* 0 ok; never replaces an existing file */
/* asked before every write, new folder, rename and removal (kind 0 a file, 1 a folder, 2 a rename's target, 3 a removal); nonzero refuses it.
   NULL everywhere except the console's RAM test (host_dev.c --ram-only: every write must stay in the session's RAM folders) */
extern int (*gp_write_check)(const char *path, int kind);
#if !defined(_WIN32) && !defined(CCS_WEB)
/* the confined file layer (gp_util.c, P3-F1): no write follows a link. A RAM-only session registers its checked folders (every write is
   then reached from their handles one name at a time) and a check of each opened folder and file (nonzero refuses: kind 1 a folder,
   0 a file); refusals and a temporary name found taken (kind 4) are reported through gp_refused */
int gp_open_file(const char *path, int truncate);   /* a file written as it goes: the descriptor, or -1 */
int gp_confine_add(const char *dir);
void gp_confine_end(void);
extern int (*gp_object_check)(int fd, int kind);
extern void (*gp_refused)(const char *path, int kind, const char *why);
#endif
void gp_now_iso(char out[32]);
int gp_local_time(const char *iso, char *out, size_t n);   /* a stored UTC time as people read it, with the year, seconds and time zone; 0 ok */
void gp_path(char *out, size_t n, const char *dir, const char *name);
uint64_t gp_ms(void);
void gp_sleep_ms(int ms);
int gp_io_pending(void);                   /* writes not yet confirmed by the storage (the browser; 0 elsewhere: a write is done when it returns) */
int gp_io_failed(char *why, size_t n);     /* 1 when a write since the last call failed after all (its file is as it was before) */
extern int gp_fail_writes;                 /* tests: every save write fails (the controlled failed-write path) */
extern int gp_virtual;                     /* tests: virtual time (host_headless.c) */
extern int gp_home_on_change;              /* set by a host: the HOME is drawn only when its picture can differ (the console: an idle pass costs almost nothing) */
extern int gp_home_pass_delay_ms;          /* tests only (--home-pass-delay-ms): each pass of the HOME's loop takes this much longer, as on a slow console */
typedef struct { uint64_t passes, renders, extra_taps, responses, left_at_transition, titles, title_mismatch; double render_ms_sum, render_ms_max, pass_ms_max, resp_ms_sum, resp_ms_max, title_ms_sum, title_ms_max; } gp_home_stats;
extern int gp_home_check_titles;           /* tests only (--home-check-titles): every title-only picture is compared with a full picture of the same moment */
extern gp_home_stats gp_hs;                /* the HOME's loop, measured (the console's report) */

/* ---- ROM: the exact bytes, verified ---- */
typedef struct { uint8_t *data; size_t len; char content_sha256[65]; } gp_rom;
/* header_hex: the catalog's 16-byte iNES header for a headerless file ("" when the file has its own); want: the content SHA-256 */
int gp_rom_load(const char *path, const char *header_hex, const char *want_sha256, gp_rom *out, char *why, size_t whylen);
void gp_rom_free(gp_rom *r);

/* ---- the save library of one game ---- */
#define GP_SLOTS 4
typedef enum { GS_EMPTY, GS_OK, GS_INCOMPATIBLE, GS_INVALID } gp_state_status;
typedef struct { gp_state_status st; char created[32], platform[32], core_version[24], title[160], why[120], crc32[12]; uint64_t frames; size_t size; } gp_state_info;
typedef struct { char dir[1024], rom_sha[65], title[160], lock_path[1100]; void *lock; } gp_library;
int gp_lib_open(gp_library *L, const char *saves_root, const char *rom_sha, const char *title, char *why, size_t whylen);   /* takes the game's lock */
void gp_lib_close(gp_library *L);
void gp_state_info_read(gp_library *L, const char *name, gp_state_info *out);   /* "slot1".."slot4", or GP_LEGACY */
int gp_state_write(gp_library *L, const char *name, const void *data, size_t len, uint64_t frames, char *why, size_t whylen);
int gp_state_read(gp_library *L, const char *name, uint8_t **data, size_t *len, char *why, size_t whylen);
int gp_platform_ok(const char *platform);   /* a state written on that platform loads here (the same platform, or one proven identical) */
int gp_sram_load(gp_library *L, uint8_t *dst, size_t len, char *why, size_t whylen);   /* 1 loaded, 0 none, -1 refused */
int gp_sram_save(gp_library *L, const void *src, size_t len, char *why, size_t whylen);
int gp_sram_set_aside(gp_library *L, char *name, size_t nlen, char *why, size_t whylen);   /* an unusable cart.sav, renamed and kept beside it */
int gp_any_slot(gp_library *L);   /* any manual slot file exists (usable or not) */
/* the earlier automatic continuation (continue.ccsstate, written by test versions before the four-slot model): never written, never
   loaded on its own, never deleted; the person may copy it into a slot they choose (the file itself stays) */
#define GP_LEGACY "continue"
int gp_legacy_copy(gp_library *L, int slot, char *why, size_t whylen);   /* 0 copied (the slot keeps the earlier save's time) */
/* which slot was last used (last-used.json): metadata naming a slot, never a save of its own. Written only after a successful load of a
   slot or a successful save to one; a failed or canceled action leaves it as it was */
typedef enum { LU_NONE, LU_OK, LU_CHANGED, LU_UNUSABLE, LU_DAMAGED } gp_last_status;
typedef struct { gp_last_status st; int slot; char at[32], how[8], why[160]; gp_state_info info; } gp_last;
void gp_last_read(gp_library *L, gp_last *out);   /* LU_OK only when the named slot loads and is still the state that was used */
int gp_last_write(gp_library *L, int slot, const char *how);   /* how: "load" or "save"; 0 ok */
/* the person's optional slot names (slot-names.json): a label of the slot, kept when the slot is saved over; never a part of a save */
#define GP_NAME_MAX 24   /* characters */
void gp_slot_name(gp_library *L, int slot, char *out, size_t n);   /* "" when the slot has no name */
int gp_slot_name_set(gp_library *L, int slot, const char *name);   /* "" removes it; 0 written */

/* ---- generic controllers (gp_pads.c): a DirectInput-style controller's raw state, the same on every host ---- */
#define GP_AXES 6
typedef struct { uint8_t btn[32]; int pov; int axis[GP_AXES]; } gp_raw;   /* pov: hundredths of a degree, -1 centered; axes X Y Z Rx Ry Rz, -1000..1000 */
/* an input code: 0..31 a button; 100..103 the D-pad hat (POV) up/right/down/left; 200 + 2 * axis + (0 negative, 1 positive) an axis direction */
#define GP_IN_POV 100
#define GP_IN_AXIS 200
#define GP_IN_ELEMS (32 + 4 + 2 * GP_AXES)
int gp_input_active(const gp_raw *r, int code);
void gp_raw_actions(const gp_raw *r, const int map[GA_COUNT], uint8_t act[GA_COUNT]);   /* the mapping; directions also from the hat and X/Y */
void gp_input_name(int code, char *out, size_t n);   /* "button 3", "D-pad up", "stick up", "axis Z+" */
void gp_map_default(int map[GA_COUNT]);
int gp_known_map(int vid, int pid, int map[GA_COUNT], const char **source);   /* 1: a controller that plays without setup (F17); source "verified" or "sdl-gamecontrollerdb" */
typedef struct { uint8_t armed[GP_IN_ELEMS]; } gp_learn;
void gp_learn_begin(gp_learn *l, const gp_raw *r);   /* what is held now is not armed: it counts only after a release */
int gp_learn_poll(gp_learn *l, const gp_raw *r);     /* the input newly pressed, else -1 */
int gp_pads_load(const char *path, const char *key, int map[GA_COUNT]);          /* 1 found */
int gp_pads_save(const char *path, const char *key, const int map[GA_COUNT]);    /* 0 ok; other controllers' entries are kept */
void gp_keyboard_players(const int has[GP_PLAYERS], int *main_p, int *second_p);    /* has: a controller plays that player; second -1: off */
void gp_players_text(const char *ctl[GP_PLAYERS], char *out, size_t n);
/* who plays: n connected controllers in connection order; pick[p]: the controller chosen for player p (an index), GP_PICK_AUTO or
   GP_PICK_NONE (the keyboard only); with no choice, the first two in order (swapped on request), as before; who[i]: 0, 1 or -1 */
#define GP_PICK_AUTO -1
#define GP_PICK_NONE -2
void gp_assign_players(int n, const int pick[GP_PLAYERS], int swap, int *who);            /* ctl: each player's controller label or NULL */

/* ---- settings (player-settings.json; written by the Studio, read here) ---- */
typedef struct { char key[GP_PLAYERS][GA_COUNT][24]; int fullscreen, integer_scale, tv_aspect, win_w, win_h, swap_pads; char pad_maps[2048]; } gp_settings;
void gp_settings_default(gp_settings *s);
int gp_settings_load(gp_settings *s, const char *path);
int gp_settings_save_window(const char *path, int fullscreen, int win_w, int win_h);   /* the remembered display preference */

/* ---- game preferences: one record per exact game (game-settings/<ROM content SHA-256>.json) and the gameplay defaults
   (game-settings/defaults.json). Written by the Studio (Game Settings, Gameplay Defaults) and by the player's own Game Settings and
   HOME Settings. A record holds only what the person chose for that game: a field it lacks follows the default (Use Default), and
   false/Off is a choice of its own. One file per game, each replaced atomically, so the Studio and a player never overwrite each other's
   games. Picture choices inherit the Play on PC Settings; A/B swap inherits Off; rapid fire inherits the Gameplay Defaults. ---- */
#define GP_UNSET -1
enum { GP_SPEED_SLOW, GP_SPEED_MEDIUM, GP_SPEED_FAST };
typedef struct { int rapid[GP_PLAYERS][2]; int speed; } gp_defaults;                    /* rapid[p][0] A, [1] B: 0/1 */
typedef struct { int rapid[GP_PLAYERS][2]; int speed, swap_ab, tv_aspect, integer_scale; } gp_prefs;   /* GP_UNSET: Use Default */
typedef gp_prefs gp_effective;                                                          /* the same fields, every one resolved */
void gp_defaults_builtin(gp_defaults *d);                                  /* rapid fire off for both players, Medium */
int gp_defaults_load(const char *dir, gp_defaults *d);                     /* 0 read (or none: built-in); -1 unreadable (built-in kept) */
int gp_defaults_save(const char *dir, const gp_defaults *d);               /* 0 ok */
void gp_prefs_none(gp_prefs *p);
int gp_prefs_load(const char *dir, const char *sha, gp_prefs *p);          /* 0 read or none; -1 unreadable (nothing applied) */
int gp_prefs_save(const char *dir, const char *sha, const char *title, const char *version, const gp_prefs *p);   /* all Use Default: the record is removed */
void gp_resolve(const gp_defaults *d, const gp_prefs *p, const gp_settings *s, gp_effective *e);
int gp_rapid_half(int speed);   /* rapid fire: frames pressed, then as many released (Slow 5, Medium 3, Fast 2) */

/* ---- the session (gp_session.c): gp_run(session, host) is gp_open, gp_step until it returns 0, then gp_close. A host that cannot
   block (the browser) sets gp_coop and calls gp_step itself: one game frame per step while a game plays ---- */
extern int gp_coop;
enum { GPV_MENU, GPV_PLAY, GPV_HOLD, GPV_HOME, GPV_ENDED };
int gp_step(void);
int gp_close(void);
int gp_view(void);
void gp_redraw(void);   /* the open menu or message is drawn again at the next step */
#endif
