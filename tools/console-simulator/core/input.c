/* Normalized controller input with deterministic press / release / repeat semantics driven by the simulation clock.
 * The mapping from keyboards or gamepads to these buttons lives in the platform adapters, never here. Repeat timing is a
 * simulator setting (defaults: 400 ms delay, 100 ms rate); it is not a claim about Clover's input stack. */
#include "ccs_sim.h"
#include <string.h>

static const char *NAMES[B_COUNT] = { "up", "down", "left", "right", "a", "b", "start", "select" };
static const char *KINDS[] = { "press", "release", "repeat", "duplicate", "forced-release", "rejected" };

void input_init(input_state *s) { memset(s, 0, sizeof *s); s->repeat_delay_ms = 400; s->repeat_rate_ms = 100; s->focused = 1; s->connected = 1; }
int button_parse(const char *name, button *out) { for (int i = 0; i < B_COUNT; i++) if (!strcmp(name, NAMES[i])) { *out = (button)i; return 1; } return 0; }
const char *button_name(button b) { return (b >= 0 && b < B_COUNT) ? NAMES[b] : "?"; }
const char *in_kind_name(in_kind k) { return KINDS[k]; }
int input_any_down(const input_state *s) { for (int i = 0; i < B_COUNT; i++) if (s->down[i]) return 1; return 0; }

void input_press(input_state *s, button b, input_cb cb, void *ctx) {
    if (!s->connected || !s->focused) { if (cb) cb(ctx, b, IN_REJECTED, s->clock_ms); return; }   /* no device or no focus: input cannot arrive */
    if (s->down[b]) { if (cb) cb(ctx, b, IN_DUPLICATE, s->clock_ms); return; }                     /* duplicate press suppressed */
    s->down[b] = 1; s->down_at[b] = s->clock_ms; s->next_repeat[b] = s->clock_ms + s->repeat_delay_ms;
    if (cb) cb(ctx, b, IN_PRESS, s->clock_ms);
}

void input_release(input_state *s, button b, input_cb cb, void *ctx) {
    if (!s->down[b]) { if (cb) cb(ctx, b, IN_DUPLICATE, s->clock_ms); return; }
    s->down[b] = 0; if (cb) cb(ctx, b, IN_RELEASE, s->clock_ms);
}

void input_tick(input_state *s, uint64_t ms, input_cb cb, void *ctx) {
    uint64_t target = s->clock_ms + ms;
    /* emit repeats in clock order so that two held buttons interleave deterministically */
    for (;;) {
        int best = -1; uint64_t when = target + 1;
        for (int i = 0; i < B_COUNT; i++) if (s->down[i] && s->next_repeat[i] <= target && s->next_repeat[i] < when) { when = s->next_repeat[i]; best = i; }
        if (best < 0) break;
        s->clock_ms = when; s->next_repeat[best] = when + s->repeat_rate_ms; if (cb) cb(ctx, (button)best, IN_REPEAT, when);
    }
    s->clock_ms = target;
}

static void release_all(input_state *s, input_cb cb, void *ctx) { for (int i = 0; i < B_COUNT; i++) if (s->down[i]) { s->down[i] = 0; if (cb) cb(ctx, (button)i, IN_FORCED_RELEASE, s->clock_ms); } }
void input_focus(input_state *s, int focused, input_cb cb, void *ctx) { if (!focused) release_all(s, cb, ctx); s->focused = focused ? 1 : 0; }
void input_device(input_state *s, int connected, input_cb cb, void *ctx) { if (!connected) release_all(s, cb, ctx); s->connected = connected ? 1 : 0; }
