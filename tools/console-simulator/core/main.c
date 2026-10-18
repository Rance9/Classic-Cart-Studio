/* ccs-sim-core: the launcher core as a process speaking a line protocol (commands on stdin, JSON events on stdout).
 *
 * The same program is the diagnostic launcher on Windows, on x86-64 Linux and on 32-bit ARM Linux under QEMU, and (with
 * --game-stub) the dummy game process. It never opens sockets, never execs anything, never touches a device: process
 * management (launching the game stub, supervision, fallback) is the platform adapter's job outside this process.
 *
 * Commands:  load <fixture.json> | tick <ms> | input press|release <up|down|left|right|a|b|start|select>
 *            focus lost|gained | device disconnect|connect | frame <out.ppm> | launch | game-exit <code> | game-failed <reason>
 *            stats | shutdown | quit
 * HOME:      home <pack dir> (the native HOME from a native HOME package, all four themes; frames then show the HOME, not
 *            the test pattern) | home-state | state-dir <dir> (where the console keeps its theme choice; before `home`)
 *            | home-layout (the rectangles the current frame draws, for overlap checks) | home-select <index> (tests only)
 *            | home-theme <id> [save] (tooling: switch the theme as the controller would; `save` also stores the choice)
 * Faults:    --fault=startup-fail | --fault=startup-hang | --fault=slow-start=<ms> | --fault=crash-after-inputs=<n>
 *            --fault=crash-on-launch | --fault=frame-fail      --mem-budget-kb=<n>  --repeat=<delay>,<rate>
 * Stub:      --game-stub [--exit-code=<n>] [--delay-ms=<n>] [--crash]
 * Device:    --device --pack <dir> --state-dir <dir> ... | --inspect-input | --inspect-fb   (Linux only: device.c, the H1 console adapter)
 */
#ifndef _WIN32
#define _POSIX_C_SOURCE 200809L
#endif
#include "ccs_sim.h"
#include "home.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <signal.h>
#ifdef _WIN32
#include <windows.h>
#include <psapi.h>
#include <fcntl.h>
#include <io.h>
#define SLEEP_MS(ms) Sleep(ms)
#else
#include <unistd.h>
#include <time.h>
static void SLEEP_MS(unsigned ms) { struct timespec ts = { ms / 1000, (long)(ms % 1000) * 1000000L }; nanosleep(&ts, NULL); }
#endif

#ifndef CCS_BUILD_TARGET
#define CCS_BUILD_TARGET "unspecified"
#endif
#if defined(__arm__)
#define ARCH "arm"
#elif defined(__aarch64__)
#define ARCH "aarch64"
#elif defined(__x86_64__) || defined(_M_X64)
#define ARCH "x86_64"
#else
#define ARCH "other"
#endif
#if defined(_WIN32)
#define ABI "windows-gnu"
#elif defined(__GLIBC__)
#define ABI "linux-glibc"
#elif defined(__linux__)
#define ABI "linux-musl"
#else
#define ABI "unknown"
#endif
#if defined(__ARM_PCS_VFP)
#define FLOAT_ABI "hard"
#elif defined(__arm__)
#define FLOAT_ABI "soft"
#else
#define FLOAT_ABI "n/a"
#endif

typedef struct {
    model m; int loaded; input_state in; lstate st; size_t cursor; uint64_t frames, inputs, launches;
    uint32_t last_crc; size_t mem_budget; int fault_crash_after_inputs, fault_crash_on_launch, fault_frame_fail; framebuffer fb;
    home *hm; int repeat_set, cover_budget; char state_dir[900];   /* the native HOME, when a pack is loaded; --cover-budget for interactive viewers; the theme-choice folder */
} core;

static void emit(const char *json_line) { fputs(json_line, stdout); fputc('\n', stdout); fflush(stdout); }
static void emitf(const char *fmt, ...);
#include <stdarg.h>
static void emitf(const char *fmt, ...) { char buf[2048]; va_list ap; va_start(ap, fmt); vsnprintf(buf, sizeof buf, fmt, ap); va_end(ap); emit(buf); }
static void jstr(const char *s, char *out, size_t n) { json_escape(s, out, n); }

static long rss_kb(void) {
#ifdef _WIN32
    PROCESS_MEMORY_COUNTERS pmc; if (GetProcessMemoryInfo(GetCurrentProcess(), &pmc, sizeof pmc)) return (long)(pmc.WorkingSetSize / 1024); return -1;
#else
    FILE *f = fopen("/proc/self/statm", "r"); if (!f) return -1; long pages = 0, res = 0; if (fscanf(f, "%ld %ld", &pages, &res) != 2) res = -1; fclose(f);
    return res < 0 ? -1 : res * (long)(sysconf(_SC_PAGESIZE) / 1024);
#endif
}

static void on_input(void *ctx, button b, in_kind k, uint64_t t) {
    core *c = ctx;
    emitf("{\"ev\":\"input\",\"button\":\"%s\",\"kind\":\"%s\",\"clock_ms\":%llu}", button_name(b), in_kind_name(k), (unsigned long long)t);
    if (c->hm && (k == IN_FORCED_RELEASE || k == IN_RELEASE)) home_input(c->hm, b, k, t);
    if (k == IN_DUPLICATE || k == IN_REJECTED || k == IN_FORCED_RELEASE || k == IN_RELEASE) return;
    c->inputs++;
    if (c->fault_crash_after_inputs > 0 && (long)c->inputs >= c->fault_crash_after_inputs) { emit("{\"ev\":\"fault\",\"name\":\"crash-after-inputs\",\"note\":\"simulated crash (abort)\"}"); fflush(stdout); abort(); }
    if (c->hm) { if (c->st == S_READY) home_input(c->hm, b, k, t); return; }   /* the HOME owns navigation while it is loaded */
    if (c->st != S_READY || !c->loaded || c->m.menu_n == 0) return;
    /* diagnostic cursor over the ordered entry list: the only navigation the core knows (not a layout decision) */
    size_t before = c->cursor;
    if (b == B_DOWN) c->cursor = (c->cursor + 1) % c->m.menu_n;
    else if (b == B_UP) c->cursor = (c->cursor + c->m.menu_n - 1) % c->m.menu_n;
    if (c->cursor != before) { char id[256]; jstr(c->m.entries[c->m.menu[c->cursor]].id, id, sizeof id); emitf("{\"ev\":\"cursor\",\"index\":%zu,\"id\":%s}", c->cursor, id); }
}

static void render(core *c) {
    if (c->hm) { home_render(c->hm, &c->fb, c->in.clock_ms); return; }   /* the native HOME replaces the diagnostic test pattern */
    framebuffer *fb = &c->fb; fb_clear(fb, 12, 12, 16);
    static const uint8_t bars[8][3] = { {235,235,235},{235,235,0},{0,235,235},{0,235,0},{235,0,235},{235,0,0},{0,0,235},{40,40,40} };
    for (int i = 0; i < 8; i++) fb_rect(fb, i * 160, 0, 160, 60, bars[i][0], bars[i][1], bars[i][2]);
    for (int x = 0; x < fb->w; x += 160) fb_rect(fb, x, 60, 1, fb->h - 60, 40, 40, 48);
    for (int y = 60; y < fb->h; y += 90) fb_rect(fb, 0, y, fb->w, 1, 40, 40, 48);
    fb_rect(fb, 0, 0, fb->w, 2, 255, 255, 255); fb_rect(fb, 0, fb->h - 2, fb->w, 2, 255, 255, 255); fb_rect(fb, 0, 0, 2, fb->h, 255, 255, 255); fb_rect(fb, fb->w - 2, 0, 2, fb->h, 255, 255, 255);
    fb_text(fb, 40, 90, 4, "CCS CONSOLE SIMULATOR - DIAGNOSTIC FIXTURE", 255, 255, 255);
    fb_text(fb, 40, 130, 3, "NOT A HOME SCREEN DESIGN. TEST PATTERN ONLY.", 255, 90, 90);
    char line[256];
    snprintf(line, sizeof line, "STATE %s  FRAME %llu  CLOCK %llu MS", lstate_name(c->st), (unsigned long long)c->frames + 1, (unsigned long long)c->in.clock_ms); fb_text(fb, 40, 200, 3, line, 200, 200, 200);
    snprintf(line, sizeof line, "BUILD %s  ARCH %s  ABI %s", CCS_BUILD_TARGET, ARCH, ABI); fb_text(fb, 40, 240, 3, line, 200, 200, 200);
    if (c->loaded) {
        snprintf(line, sizeof line, "FIXTURE %.40s  ENTRIES %zu  MENU %zu  REJECTED %zu", c->m.name, c->m.n, c->m.menu_n, c->m.rejected_n); fb_text(fb, 40, 300, 3, line, 200, 200, 200);
        if (c->m.menu_n) { const entry *e = &c->m.entries[c->m.menu[c->cursor]]; snprintf(line, sizeof line, "CURSOR %zu/%zu  ID %.30s", c->cursor + 1, c->m.menu_n, e->id); fb_text(fb, 40, 340, 3, line, 255, 255, 255);
            snprintf(line, sizeof line, "TITLE %.44s", e->title); fb_text(fb, 40, 380, 3, line, 255, 255, 255);
            snprintf(line, sizeof line, "VERSION %.28s  KIND %s  READY %s", e->version_id, kind_name(e->kind), ready_name(e->ready)); fb_text(fb, 40, 420, 3, line, 200, 200, 200);
            /* cursor position marker: a vertical track, proportional position (diagnostic, not a menu rendering) */
            fb_rect(fb, 1200, 100, 20, 560, 50, 50, 60); fb_rect(fb, 1196, 100 + (int)((560 - 12) * (double)c->cursor / (c->m.menu_n > 1 ? c->m.menu_n - 1 : 1)), 28, 12, 255, 255, 255);
        }
    } else fb_text(fb, 40, 300, 3, "NO FIXTURE LOADED", 255, 200, 90);
    for (int i = 0; i < B_COUNT; i++) { int x = 40 + i * 140; fb_rect(fb, x, 560, 120, 60, c->in.down[i] ? 60 : 30, c->in.down[i] ? 200 : 30, c->in.down[i] ? 60 : 40); fb_text(fb, x + 10, 580, 3, button_name((button)i), 255, 255, 255); }
    snprintf(line, sizeof line, "FOCUS %s  DEVICE %s  INPUTS %llu  LAUNCHES %llu", c->in.focused ? "YES" : "LOST", c->in.connected ? "CONNECTED" : "DISCONNECTED", (unsigned long long)c->inputs, (unsigned long long)c->launches); fb_text(fb, 40, 650, 3, line, 200, 200, 200);
}

int device_main(int argc, char **argv); int device_inspect_input(int argc, char **argv); int device_inspect_fb(int argc, char **argv);

static int game_stub(int argc, char **argv) {
    int code = 0, delay = 50, crash = 0;
    for (int i = 1; i < argc; i++) { if (!strncmp(argv[i], "--exit-code=", 12)) code = atoi(argv[i] + 12); else if (!strncmp(argv[i], "--delay-ms=", 11)) delay = atoi(argv[i] + 11); else if (!strcmp(argv[i], "--crash")) crash = 1; }
    emitf("{\"ev\":\"game_stub_started\",\"arch\":\"%s\",\"abi\":\"%s\",\"build\":\"%s\",\"note\":\"DUMMY GAME PROCESS (simulation): no game, no emulator\"}", ARCH, ABI, CCS_BUILD_TARGET);
    if (delay > 0) SLEEP_MS((unsigned)delay);
    if (crash) { emit("{\"ev\":\"game_stub_crash\",\"note\":\"simulated crash (abort)\"}"); fflush(stdout); abort(); }
    emitf("{\"ev\":\"game_stub_exit\",\"exit_code\":%d}", code); return code;
}

int main(int argc, char **argv) {
#ifdef _WIN32
    _setmode(_fileno(stdout), _O_BINARY); _setmode(_fileno(stdin), _O_BINARY);
#endif
    for (int i = 1; i < argc; i++) if (!strcmp(argv[i], "--game-stub")) return game_stub(argc, argv);
    for (int i = 1; i < argc; i++) {   /* the console device adapter (device.c): the same HOME on a Linux framebuffer and evdev controller */
        if (!strcmp(argv[i], "--device")) return device_main(argc, argv);
        if (!strcmp(argv[i], "--inspect-input")) return device_inspect_input(argc, argv);
        if (!strcmp(argv[i], "--inspect-fb")) return device_inspect_fb(argc, argv);
    }
    core c; memset(&c, 0, sizeof c); input_init(&c.in); c.st = S_BOOT;
    int startup_fail = 0, startup_hang = 0, slow_start = 0;
    for (int i = 1; i < argc; i++) {
        const char *a = argv[i];
        if (!strcmp(a, "--fault=startup-fail")) startup_fail = 1; else if (!strcmp(a, "--fault=startup-hang")) startup_hang = 1;
        else if (!strncmp(a, "--fault=slow-start=", 19)) slow_start = atoi(a + 19); else if (!strncmp(a, "--fault=crash-after-inputs=", 27)) c.fault_crash_after_inputs = atoi(a + 27);
        else if (!strcmp(a, "--fault=crash-on-launch")) c.fault_crash_on_launch = 1; else if (!strcmp(a, "--fault=frame-fail")) c.fault_frame_fail = 1;
        else if (!strncmp(a, "--mem-budget-kb=", 16)) c.mem_budget = (size_t)atol(a + 16) * 1024;
        else if (!strncmp(a, "--repeat=", 9)) { unsigned d = 400, r = 100; if (sscanf(a + 9, "%u,%u", &d, &r) >= 1) { c.in.repeat_delay_ms = d; c.in.repeat_rate_ms = r; c.repeat_set = 1; } }
        else if (!strncmp(a, "--cover-budget=", 15)) c.cover_budget = atoi(a + 15);
        else if (!strncmp(a, "--state-dir=", 12)) snprintf(c.state_dir, sizeof c.state_dir, "%s", a + 12);
        else if (!strcmp(a, "--version")) { printf("%s %s %s %s\n", CCS_SIM_PROTOCOL, CCS_BUILD_TARGET, ARCH, ABI); return 0; }
        else { fprintf(stderr, "unknown argument: %s\n", a); return 64; }
    }
    if (startup_fail) { emit("{\"ev\":\"fault\",\"name\":\"startup-fail\",\"note\":\"simulated startup failure\"}"); return 3; }
    if (slow_start > 0) SLEEP_MS((unsigned)slow_start);
    if (startup_hang) { for (;;) SLEEP_MS(1000); }
    if (fb_init(&c.fb, CCS_FB_W, CCS_FB_H)) { emit("{\"ev\":\"error\",\"error\":\"framebuffer allocation failed\"}"); return 5; }
    emitf("{\"ev\":\"ready\",\"protocol\":\"%s\",\"build\":\"%s\",\"arch\":\"%s\",\"abi\":\"%s\",\"float_abi\":\"%s\",\"fb\":[%d,%d],\"pointer_bits\":%d,\"mem_budget_bytes\":%zu,\"simulation\":true}", CCS_SIM_PROTOCOL, CCS_BUILD_TARGET, ARCH, ABI, FLOAT_ABI, CCS_FB_W, CCS_FB_H, (int)(sizeof(void *) * 8), c.mem_budget);
    c.st = S_READY; emit("{\"ev\":\"state\",\"state\":\"ready\"}");
    char line[4096], esc[1024];
    while (fgets(line, sizeof line, stdin)) {
        size_t n = strlen(line); while (n && (line[n - 1] == '\n' || line[n - 1] == '\r')) line[--n] = 0;
        if (!n) continue;
        char *cmd = line, *arg = strchr(line, ' '); if (arg) { *arg++ = 0; }
        if (!strcmp(cmd, "load") && arg) {
            if (c.loaded) { model_free(&c.m); c.loaded = 0; }
            char err[256]; int rc = model_load(&c.m, arg, c.mem_budget, err, sizeof err);
            if (rc) { jstr(err, esc, sizeof esc); emitf("{\"ev\":\"fixture_rejected\",\"code\":%d,\"error\":%s}", rc, esc); model_free(&c.m); continue; }
            c.loaded = 1; c.cursor = 0; jstr(c.m.name, esc, sizeof esc);
            emitf("{\"ev\":\"fixture_loaded\",\"name\":%s,\"schema\":\"%s\",\"entries\":%zu,\"menu\":%zu,\"rejected\":%zu,\"bytes\":%zu}", esc, CCS_FIXTURE_SCHEMA, c.m.n, c.m.menu_n, c.m.rejected_n, c.m.bytes);
            for (size_t i = 0; i < c.m.rejected_n; i++) { char r1[256], r2[512]; jstr(c.m.rejected[i].record, r1, sizeof r1); jstr(c.m.rejected[i].reason, r2, sizeof r2); emitf("{\"ev\":\"record_rejected\",\"record\":%s,\"reason\":%s}", r1, r2); }
        } else if (!strcmp(cmd, "home") && arg) {
            if (c.hm) { home_free(c.hm); c.hm = NULL; }
            char err[300]; c.hm = home_load2(arg, c.state_dir[0] ? c.state_dir : NULL, emit, err, sizeof err);
            if (!c.hm) { jstr(err, esc, sizeof esc); emitf("{\"ev\":\"home_rejected\",\"error\":%s}", esc); continue; }
            home_set_cover_budget(c.hm, c.cover_budget);
            if (!c.repeat_set) { c.in.repeat_delay_ms = 400; c.in.repeat_rate_ms = 110; c.in.repeat_fast_after = 6; c.in.repeat_fast_rate = 55; }   /* HOME_REPEAT */
            emitf("{\"ev\":\"home_loaded\",\"theme\":\"%s\",\"games\":%zu,\"bytes\":%zu,\"repeat\":[%u,%u,%u,%u]}", home_theme(c.hm), home_games(c.hm), home_bytes(c.hm), c.in.repeat_delay_ms, c.in.repeat_rate_ms, c.in.repeat_fast_after, c.in.repeat_fast_rate);
            home_emit_state(c.hm);
        } else if (!strcmp(cmd, "state-dir") && arg) { snprintf(c.state_dir, sizeof c.state_dir, "%s", arg); jstr(arg, esc, sizeof esc); emitf("{\"ev\":\"state_dir\",\"path\":%s}", esc); }
        else if (!strcmp(cmd, "home-theme") && arg) {
            if (!c.hm) { emit("{\"ev\":\"error\",\"error\":\"no HOME loaded\"}"); continue; }
            char *sv = strchr(arg, ' '); int save = 0; if (sv) { *sv++ = 0; save = !strcmp(sv, "save"); }
            if (home_set_theme(c.hm, arg, save)) { jstr(arg, esc, sizeof esc); emitf("{\"ev\":\"error\",\"error\":\"unknown theme\",\"theme\":%s}", esc); } else home_emit_state(c.hm);
        } else if (!strcmp(cmd, "home-select") && arg) { if (c.hm) home_select(c.hm, atoi(arg)); else emit("{\"ev\":\"error\",\"error\":\"no HOME loaded\"}"); }
        else if (!strcmp(cmd, "home-layout")) { if (c.hm) home_layout(c.hm, &c.fb, c.in.clock_ms); else emit("{\"ev\":\"error\",\"error\":\"no HOME loaded\"}"); }
        else if (!strcmp(cmd, "home-go") && arg) { if (c.hm) { char kind[24] = ""; int v = -1; sscanf(arg, "%23s %d", kind, &v); home_go(c.hm, kind, v); } else emit("{\"ev\":\"error\",\"error\":\"no HOME loaded\"}"); }
        else if (!strcmp(cmd, "home-state")) { if (c.hm) home_emit_state(c.hm); else emit("{\"ev\":\"error\",\"error\":\"no HOME loaded\"}"); }
        else if (!strcmp(cmd, "menu")) {
            if (!c.loaded) { emit("{\"ev\":\"error\",\"error\":\"no fixture loaded\"}"); continue; }
            for (size_t i = 0; i < c.m.menu_n; i++) { const entry *e = &c.m.entries[c.m.menu[i]]; char a1[256], a2[256]; jstr(e->id, a1, sizeof a1); jstr(e->version_id, a2, sizeof a2); emitf("{\"ev\":\"menu_entry\",\"index\":%zu,\"id\":%s,\"version_id\":%s,\"kind\":\"%s\",\"ready\":\"%s\",\"order\":%d}", i, a1, a2, kind_name(e->kind), ready_name(e->ready), e->order); }
            emitf("{\"ev\":\"menu_end\",\"count\":%zu}", c.m.menu_n);
        } else if (!strcmp(cmd, "tick") && arg) { input_tick(&c.in, (uint64_t)strtoull(arg, NULL, 10), on_input, &c); emitf("{\"ev\":\"tick\",\"clock_ms\":%llu}", (unsigned long long)c.in.clock_ms); }
        else if (!strcmp(cmd, "input") && arg) {
            char *b = strchr(arg, ' '); button bt; if (!b) { emit("{\"ev\":\"error\",\"error\":\"input needs press|release and a button\"}"); continue; } *b++ = 0;
            if (!button_parse(b, &bt)) { jstr(b, esc, sizeof esc); emitf("{\"ev\":\"error\",\"error\":\"unknown button\",\"button\":%s}", esc); continue; }
            if (!strcmp(arg, "press")) input_press(&c.in, bt, on_input, &c); else if (!strcmp(arg, "release")) input_release(&c.in, bt, on_input, &c); else emit("{\"ev\":\"error\",\"error\":\"input needs press|release\"}");
        } else if (!strcmp(cmd, "focus") && arg) { input_focus(&c.in, !strcmp(arg, "gained"), on_input, &c); emitf("{\"ev\":\"focus\",\"focused\":%s}", c.in.focused ? "true" : "false"); }
        else if (!strcmp(cmd, "device") && arg) { input_device(&c.in, !strcmp(arg, "connect"), on_input, &c); emitf("{\"ev\":\"device\",\"connected\":%s}", c.in.connected ? "true" : "false"); }
        else if (!strcmp(cmd, "frame") && arg) {
            if (c.fault_frame_fail) { emit("{\"ev\":\"frame_failed\",\"error\":\"simulated render failure\"}"); continue; }
            render(&c); uint32_t crc = 0; int rc = fb_write_ppm(&c.fb, arg, &crc);
            if (rc) { jstr(arg, esc, sizeof esc); emitf("{\"ev\":\"frame_failed\",\"error\":\"cannot write frame\",\"path\":%s,\"code\":%d}", esc, rc); continue; }
            c.frames++; c.last_crc = crc; jstr(arg, esc, sizeof esc); emitf("{\"ev\":\"frame_written\",\"path\":%s,\"w\":%d,\"h\":%d,\"bytes\":%zu,\"crc32\":\"%08x\",\"frame\":%llu,\"covers_pending\":%d}", esc, c.fb.w, c.fb.h, (size_t)c.fb.w * c.fb.h * 3, crc, (unsigned long long)c.frames, c.hm ? home_covers_pending(c.hm) : 0);
        } else if (!strcmp(cmd, "launch")) {
            if (c.hm) { emit("{\"ev\":\"launch_refused\",\"reason\":\"native HOME prototype (N1): launching is not connected\"}"); continue; }
            if (c.st != S_READY) { emitf("{\"ev\":\"launch_refused\",\"reason\":\"state is %s\"}", lstate_name(c.st)); continue; }
            if (!c.loaded || !c.m.menu_n) { emit("{\"ev\":\"launch_refused\",\"reason\":\"no entry under the cursor\"}"); continue; }
            const entry *e = &c.m.entries[c.m.menu[c.cursor]];
            if (e->ready == R_MISSING) { jstr(e->id, esc, sizeof esc); emitf("{\"ev\":\"launch_refused\",\"reason\":\"game file missing\",\"id\":%s}", esc); continue; }
            if (c.fault_crash_on_launch) { emit("{\"ev\":\"fault\",\"name\":\"crash-on-launch\",\"note\":\"simulated crash (abort)\"}"); fflush(stdout); abort(); }
            c.st = S_LAUNCHING; c.launches++; char a1[256], a2[256], a3[512]; jstr(e->id, a1, sizeof a1); jstr(e->version_id, a2, sizeof a2); jstr(e->title, a3, sizeof a3);
            emitf("{\"ev\":\"launch_requested\",\"id\":%s,\"version_id\":%s,\"title\":%s,\"kind\":\"%s\",\"note\":\"the platform adapter starts a DUMMY stub, never a game\"}", a1, a2, a3, kind_name(e->kind));
            c.st = S_SUSPENDED; input_focus(&c.in, 0, on_input, &c); emit("{\"ev\":\"state\",\"state\":\"suspended\"}");
        } else if (!strcmp(cmd, "game-exit") && arg) {
            if (c.st != S_SUSPENDED) { emit("{\"ev\":\"error\",\"error\":\"game-exit while not suspended\"}"); continue; }
            c.st = S_READY; input_focus(&c.in, 1, on_input, &c); emitf("{\"ev\":\"resumed\",\"exit_code\":%d}", atoi(arg)); emit("{\"ev\":\"state\",\"state\":\"ready\"}");
        } else if (!strcmp(cmd, "game-failed")) {
            if (c.st != S_SUSPENDED) { emit("{\"ev\":\"error\",\"error\":\"game-failed while not suspended\"}"); continue; }
            c.st = S_READY; input_focus(&c.in, 1, on_input, &c); jstr(arg ? arg : "unknown", esc, sizeof esc); emitf("{\"ev\":\"launch_failed\",\"reason\":%s}", esc); emit("{\"ev\":\"state\",\"state\":\"ready\"}");
        } else if (!strcmp(cmd, "stats")) {
            emitf("{\"ev\":\"stats\",\"entries\":%zu,\"menu\":%zu,\"frames\":%llu,\"inputs\":%llu,\"launches\":%llu,\"clock_ms\":%llu,\"rss_kb\":%ld,\"model_bytes\":%zu,\"fb_bytes\":%zu,\"arch\":\"%s\",\"abi\":\"%s\",\"build\":\"%s\",\"home_games\":%zu,\"home_bytes\":%zu,\"rss_note\":\"under QEMU user mode rss_kb is the emulator process, not a device figure\"}",
                  c.loaded ? c.m.n : 0, c.loaded ? c.m.menu_n : 0, (unsigned long long)c.frames, (unsigned long long)c.inputs, (unsigned long long)c.launches, (unsigned long long)c.in.clock_ms, rss_kb(), c.loaded ? c.m.bytes : 0, (size_t)c.fb.w * c.fb.h * 3, ARCH, ABI, CCS_BUILD_TARGET, c.hm ? home_games(c.hm) : 0, c.hm ? home_bytes(c.hm) : 0);
        } else if (!strcmp(cmd, "shutdown") || !strcmp(cmd, "quit")) { c.st = S_SHUTDOWN; emit("{\"ev\":\"shutdown\",\"clean\":true}"); break; }
        else { jstr(cmd, esc, sizeof esc); emitf("{\"ev\":\"error\",\"error\":\"unknown command\",\"command\":%s}", esc); }
    }
    if (c.st != S_SHUTDOWN) emit("{\"ev\":\"shutdown\",\"clean\":false,\"reason\":\"stdin closed\"}");
    if (c.loaded) model_free(&c.m);
    home_free(c.hm); fb_free(&c.fb); return 0;
}
