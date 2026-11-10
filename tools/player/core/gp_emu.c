/* gp_emu.c: FCEUmm through its libretro API (statically linked; one game at a time, as the core keeps global state). The player
 * owns timing, video, sound and input: this file only runs frames and moves bytes. Core options keep FCEUmm's defaults. */
#include "gp_emu.h"
#include "libretro.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdarg.h>

static struct {
    int inited, loaded; struct retro_system_av_info av; uint16_t frame[256 * 240]; int fw, fh;
    int16_t audio[2 * 4096]; size_t an; uint8_t in[GP_PLAYERS][GA_COUNT]; uint64_t frames; char log[256];
} E;
static const unsigned RID[GA_COUNT] = { RETRO_DEVICE_ID_JOYPAD_UP, RETRO_DEVICE_ID_JOYPAD_DOWN, RETRO_DEVICE_ID_JOYPAD_LEFT, RETRO_DEVICE_ID_JOYPAD_RIGHT,
                                        RETRO_DEVICE_ID_JOYPAD_A, RETRO_DEVICE_ID_JOYPAD_B, RETRO_DEVICE_ID_JOYPAD_SELECT, RETRO_DEVICE_ID_JOYPAD_START };

static void video_cb(const void *data, unsigned w, unsigned h, size_t pitch) {
    if (!data) return;   /* a duplicated frame: keep the last one */
    if (w > 256) w = 256; if (h > 240) h = 240; E.fw = (int)w; E.fh = (int)h;
    for (unsigned y = 0; y < h; y++) memcpy(E.frame + y * 256, (const uint8_t *)data + y * pitch, w * 2); }
static size_t audio_batch_cb(const int16_t *d, size_t n) { size_t room = (sizeof E.audio / sizeof E.audio[0] - E.an) / 2; if (n > room) n = room; memcpy(E.audio + E.an, d, n * 4); E.an += n * 2; return n; }
static void audio_cb(int16_t l, int16_t r) { int16_t s[2] = { l, r }; audio_batch_cb(s, 1); }
static void poll_cb(void) {}
static int16_t state_cb(unsigned port, unsigned dev, unsigned idx, unsigned id) {
    if (dev != RETRO_DEVICE_JOYPAD || idx || port >= GP_PLAYERS) return 0;
    for (int a = 0; a < GA_COUNT; a++) if (RID[a] == id) return E.in[port][a];
    return 0; }
static void log_cb(enum retro_log_level l, const char *fmt, ...) { if (l < RETRO_LOG_WARN) return; va_list a; va_start(a, fmt); vsnprintf(E.log, sizeof E.log, fmt, a); va_end(a); }
static const char *g_dir = ".";
/* the verified bytes go to the core from memory (it never opens a file); the name carries the exact version's No-Intro name, which
   FCEUmm reads for the region of an iNES 1.0 file ("(Europe)" = PAL) */
static struct retro_game_info_ext g_ext; static char g_name[256], g_full[300];
static bool env_cb(unsigned cmd, void *data) {
    switch (cmd) {
    case RETRO_ENVIRONMENT_GET_LOG_INTERFACE: ((struct retro_log_callback *)data)->log = log_cb; return true;
    case RETRO_ENVIRONMENT_SET_PIXEL_FORMAT: return *(enum retro_pixel_format *)data == RETRO_PIXEL_FORMAT_RGB565;
    case RETRO_ENVIRONMENT_GET_SYSTEM_DIRECTORY: case RETRO_ENVIRONMENT_GET_SAVE_DIRECTORY: *(const char **)data = g_dir; return true;
    case RETRO_ENVIRONMENT_GET_CAN_DUPE: *(bool *)data = true; return true;
    case RETRO_ENVIRONMENT_GET_GAME_INFO_EXT: if (!g_ext.data) return false; *(const struct retro_game_info_ext **)data = &g_ext; return true;
    default: return false; } }

int emu_init(const char *scratch_dir) {
    if (E.inited) return 0; if (scratch_dir) g_dir = scratch_dir;
    retro_set_environment(env_cb); retro_set_video_refresh(video_cb); retro_set_audio_sample(audio_cb); retro_set_audio_sample_batch(audio_batch_cb);
    retro_set_input_poll(poll_cb); retro_set_input_state(state_cb); retro_init(); E.inited = 1; return 0; }
int emu_load(const gp_rom *r, const char *name, char *why, size_t whylen) {
    if (E.loaded) emu_unload();
    snprintf(g_name, sizeof g_name, "%s", name && *name ? name : "game"); for (char *c = g_name; *c; c++) if (*c == '/' || *c == '\\' || *c == ':') *c = '_';
    snprintf(g_full, sizeof g_full, "%s.nes", g_name); memset(&g_ext, 0, sizeof g_ext); g_ext.full_path = g_full; g_ext.dir = "."; g_ext.name = g_name; g_ext.ext = "nes";
    g_ext.data = r->data; g_ext.size = r->len; g_ext.file_in_archive = false; g_ext.persistent_data = false; E.log[0] = 0; E.an = 0; E.frames = 0; memset(E.in, 0, sizeof E.in); memset(E.frame, 0, sizeof E.frame); E.fw = 256; E.fh = 240;
    struct retro_game_info gi = { g_full, r->data, r->len, NULL };
    if (!retro_load_game(&gi)) { snprintf(why, whylen, "The emulator could not start this game%s%s.", E.log[0] ? ": " : "", E.log); return -1; }
    retro_set_controller_port_device(0, RETRO_DEVICE_JOYPAD); retro_set_controller_port_device(1, RETRO_DEVICE_JOYPAD);
    retro_get_system_av_info(&E.av); E.loaded = 1; return 0; }
void emu_unload(void) { if (E.loaded) retro_unload_game(); E.loaded = 0; memset(&g_ext, 0, sizeof g_ext); }
void emu_run(const uint8_t in[GP_PLAYERS][GA_COUNT]) { memcpy(E.in, in, sizeof E.in); E.an = 0; retro_run(); E.frames++; }
const uint16_t *emu_frame(int *w, int *h) { *w = E.fw; *h = E.fh; return E.frame; }
const int16_t *emu_audio(size_t *frames) { *frames = E.an / 2; return E.audio; }
double emu_fps(void) { return E.loaded ? E.av.timing.fps : 60.0988; }
double emu_rate(void) { return E.loaded && E.av.timing.sample_rate > 0 ? E.av.timing.sample_rate : 48000.0; }
uint64_t emu_frames(void) { return E.frames; }
size_t emu_state_size(void) { return E.loaded ? retro_serialize_size() : 0; }
int emu_save_state(void *buf, size_t n) { return E.loaded && retro_serialize(buf, n) ? 0 : -1; }
int emu_load_state(const void *buf, size_t n) { return E.loaded && n == retro_serialize_size() && retro_unserialize(buf, n) ? 0 : -1; }
uint8_t *emu_sram(size_t *len) { *len = E.loaded ? retro_get_memory_size(RETRO_MEMORY_SAVE_RAM) : 0; return *len ? retro_get_memory_data(RETRO_MEMORY_SAVE_RAM) : NULL; }
void emu_reset(void) { if (E.loaded) retro_reset(); }
const char *emu_core_name(void) { static struct retro_system_info si; retro_get_system_info(&si); return si.library_name; }
