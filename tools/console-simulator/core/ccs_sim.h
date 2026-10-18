/* Classic Cart Studio Console Simulator: shared launcher core (C11, portable).
 *
 * One source of truth for model loading, normalized input, the launcher lifecycle and the software framebuffer. The same
 * sources are built for the Windows host, for x86-64 Linux and for 32-bit ARM Linux (run under QEMU user mode). The HOME
 * design lives in home.c (milestone N1: the native Arcade Grid HOME, see home.h); without a HOME pack the core draws a labeled
 * diagnostic test pattern over a linear cursor, kept so that input -> state -> frame can be proven end to end.
 */
#ifndef CCS_SIM_H
#define CCS_SIM_H
#include <stddef.h>
#include <stdint.h>

#define CCS_SIM_PROTOCOL "ccs-sim-core/1"
#define CCS_FIXTURE_SCHEMA "ccs-sim-fixture/1"
#define CCS_FB_W 1280
#define CCS_FB_H 720

/* ---------- minimal strict JSON (RFC 8259 subset: no comments, no trailing commas, UTF-8 passthrough, \u escapes) ---------- */
typedef enum { J_NULL, J_BOOL, J_NUM, J_STR, J_ARR, J_OBJ } jtype;
typedef struct jval { jtype t; double num; int b; char *s; struct jval **items; char **keys; size_t n; } jval;
jval *json_parse(const char *text, size_t len, char *err, size_t errlen);
void json_free(jval *v);
const jval *json_get(const jval *obj, const char *key);
size_t json_escape(const char *s, char *out, size_t outlen);   /* writes a quoted, escaped JSON string; returns length */

/* ---------- collection model (fixture ccs-sim-fixture/1) ---------- */
typedef enum { K_FACTORY_VISIBLE, K_FACTORY_HIDDEN, K_ADDED, K_PERSONAL, K_ALTERNATE } entry_kind;
typedef enum { R_READY, R_MISSING, R_UNKNOWN, R_NA } ready_state;
typedef struct {
    char *id, *version_id, *title, *sort_key, *group, *art_ref, *source_hash;
    entry_kind kind; ready_state ready; int order; int visible;
} entry;
typedef struct { char *record; char *reason; } rejection;
typedef struct {
    char *name, *source, *source_hash;
    entry *entries; size_t n;             /* accepted records, fixture order */
    size_t *menu; size_t menu_n;          /* indices of visible entries in deterministic order */
    rejection *rejected; size_t rejected_n;
    size_t bytes;                         /* bytes charged against the memory budget */
} model;
int model_load(model *m, const char *path, size_t mem_budget_bytes, char *err, size_t errlen);   /* 0 ok, else error code */
void model_free(model *m);
const char *kind_name(entry_kind k);
const char *ready_name(ready_state r);

/* ---------- normalized input ---------- */
typedef enum { B_UP, B_DOWN, B_LEFT, B_RIGHT, B_A, B_B, B_START, B_SELECT, B_COUNT } button;
typedef enum { IN_PRESS, IN_RELEASE, IN_REPEAT, IN_DUPLICATE, IN_FORCED_RELEASE, IN_REJECTED } in_kind;
typedef struct {
    int down[B_COUNT]; uint64_t down_at[B_COUNT]; uint64_t next_repeat[B_COUNT];
    uint64_t clock_ms;                    /* simulation clock: advanced only by tick, never by wall time */
    uint32_t repeat_delay_ms, repeat_rate_ms;
    uint32_t repeat_fast_after, repeat_fast_rate, repeat_count[B_COUNT];   /* optional acceleration (0 = off): the HOME uses 400 / 110 ms, 55 ms after six repeats */
    int focused, connected;
} input_state;
typedef void (*input_cb)(void *ctx, button b, in_kind k, uint64_t clock_ms);
void input_init(input_state *s);
int button_parse(const char *name, button *out);
const char *button_name(button b);
const char *in_kind_name(in_kind k);
void input_press(input_state *s, button b, input_cb cb, void *ctx);
void input_release(input_state *s, button b, input_cb cb, void *ctx);
void input_tick(input_state *s, uint64_t ms, input_cb cb, void *ctx);
void input_focus(input_state *s, int focused, input_cb cb, void *ctx);
void input_device(input_state *s, int connected, input_cb cb, void *ctx);
int input_any_down(const input_state *s);

/* ---------- launcher lifecycle ---------- */
typedef enum { S_BOOT, S_READY, S_LAUNCHING, S_SUSPENDED, S_SHUTDOWN } lstate;
const char *lstate_name(lstate s);

/* ---------- diagnostic framebuffer (software RGB, PPM output) ---------- */
typedef struct { int w, h; uint8_t *rgb; } framebuffer;
int fb_init(framebuffer *fb, int w, int h);
void fb_free(framebuffer *fb);
void fb_clear(framebuffer *fb, uint8_t r, uint8_t g, uint8_t b);
void fb_rect(framebuffer *fb, int x, int y, int w, int h, uint8_t r, uint8_t g, uint8_t b);
void fb_text(framebuffer *fb, int x, int y, int scale, const char *s, uint8_t r, uint8_t g, uint8_t b);
int fb_write_ppm(const framebuffer *fb, const char *path, uint32_t *crc_out);
uint32_t crc32_bytes(uint32_t seed, const uint8_t *p, size_t n);

#endif
