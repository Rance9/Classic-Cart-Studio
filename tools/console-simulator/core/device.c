/* The console device adapter (H1): the same native HOME, drawn on a Linux framebuffer and driven by a Linux evdev controller.
 *
 *   ccs-home-armv7 --device --pack DIR --state-dir DIR [--fb /dev/fb0] [--input auto|PATH] [--map FILE] [--fps 30]
 *   ccs-home-armv7 --inspect-input [--map FILE] [--input auto|PATH] [--run-ms N]      prints raw controller events (read-only)
 *   ccs-home-armv7 --inspect-fb [--fb /dev/fb0]                                        prints the framebuffer mode (read-only)
 *
 * This file only translates: the HOME (home.c and the four themes) keeps navigation, focus, repeat and theme logic, and the
 * pixels presented are exactly the frame home_render draws for Preview on TV. Nothing here changes the video mode, launches a
 * game, or writes anywhere except the framebuffer, the sound device (HOME Music, D-81) and <state dir>/home-prefs.json and home-music.json (written by persist.c when the controller
 * picks a theme). The state folder must be on tmpfs/ramfs: H1 keeps everything in RAM.
 *
 * Test seams (host-side verification only; never used on the console): --fb-file PATH --fb-geometry SPEC maps a regular file
 * as a framebuffer with a declared mode (FBIOPAN_DISPLAY is then emulated, not the kernel's), and --input PATH reads a file or
 * FIFO of input_event records in the target's wire format. They exercise conversion, stride, page and pacing logic, not ioctls.
 *
 * Wire format: struct input_event on a 32-bit kernel is 16 bytes (u32 seconds, u32 microseconds, u16 type, u16 code, s32
 * value); on 64-bit it is 24. musl 1.2 defines a 64-bit time_t on 32-bit ARM, so the libc struct is never used: the layout is
 * declared here (2 longs + 8 bytes, the kernel's own ABI for this architecture). */
#if defined(__linux__)
#define _GNU_SOURCE
#endif
#include "ccs_sim.h"
#include "home.h"
#include <stdio.h>
#include <string.h>

int device_main(int argc, char **argv);
int device_inspect_input(int argc, char **argv);
int device_inspect_fb(int argc, char **argv);

#if !defined(__linux__)
static int linux_only(void) { fputs("{\"ev\":\"error\",\"error\":\"the device adapter is Linux-only (the console build); use the command mode here\"}\n", stdout); return 69; }
int device_main(int argc, char **argv) { (void)argc; (void)argv; return linux_only(); }
int device_inspect_input(int argc, char **argv) { (void)argc; (void)argv; return linux_only(); }
int device_inspect_fb(int argc, char **argv) { (void)argc; (void)argv; return linux_only(); }
#else
#include <stdlib.h>
#include <stdarg.h>
#include <errno.h>
#include <fcntl.h>
#include <unistd.h>
#include <signal.h>
#include <poll.h>
#include <time.h>
#include <sys/ioctl.h>
#include <sys/mman.h>
#include <sys/statfs.h>
#include <sys/syscall.h>
#include <sys/resource.h>
#include <linux/fb.h>
#include "devio.h"

#define EXIT_USAGE 2
#define EXIT_STATE 10
#define EXIT_MAP 11
#define EXIT_FB 12
#define EXIT_INPUT 13
#define EXIT_PACK 14
#define EXIT_WATCHDOG 70
#define TMPFS_MAGIC_ 0x01021994
#define RAMFS_MAGIC_ 0x858458f6
#define EV_SYN_ 0
#define EV_KEY_ 1
#define SYN_DROPPED_ 3
#define KEY_MAX_ 0x2ff
#define CCS_EVIOCGNAME(len) _IOC(_IOC_READ, 'E', 0x06, len)
#define EVSIZE (2 * sizeof(long) + 8)       /* struct input_event in the kernel's ABI: 16 bytes on ARMv7, 24 on x86-64 */
#define MAX_DEV 4
#define COMBO_GRACE_MS 150

static void out(const char *s) { fputs(s, stdout); fputc('\n', stdout); fflush(stdout); }
static void outf(const char *fmt, ...) { char b[2048]; va_list ap; va_start(ap, fmt); vsnprintf(b, sizeof b, fmt, ap); va_end(ap); out(b); }
static void jq(const char *s, char *o, size_t n) { json_escape(s ? s : "", o, n); }
static const char *arg_val(int argc, char **argv, int *i, const char *name) {   /* --name VALUE or --name=VALUE */
    size_t n = strlen(name);
    if (!strncmp(argv[*i], name, n) && argv[*i][n] == '=') return argv[*i] + n + 1;
    if (!strcmp(argv[*i], name) && *i + 1 < argc) return argv[++*i];
    return NULL;
}

/* ---------- the devices: devio.c (shared with the game player's console host, tools/player/core/host_dev.c) ---------- */
static uint64_t mono_us(void) { return dio_mono_us(); }
static uint64_t mono_ms_now(void) { return dio_mono_us() / 1000u; }

/* ---------- signals ---------- */
static volatile sig_atomic_t g_stop, g_signal;
static void on_signal(int s) { if (g_stop) _exit(128 + s); g_stop = 1; g_signal = s; }   /* a second signal forces the exit */
static void on_watchdog(int s) { static const char m[] = "{\"ev\":\"watchdog\",\"error\":\"a frame took too long; exiting (the last frame stays on screen: run restore.sh)\"}\n"; ssize_t r = write(1, m, sizeof m - 1); (void)r; (void)s; _exit(EXIT_WATCHDOG); }
static void install_signals(void) {
    struct sigaction sa; memset(&sa, 0, sizeof sa); sa.sa_handler = on_signal; sigemptyset(&sa.sa_mask);
    sigaction(SIGINT, &sa, NULL); sigaction(SIGTERM, &sa, NULL); sigaction(SIGHUP, &sa, NULL);
    sa.sa_handler = on_watchdog; sigaction(SIGALRM, &sa, NULL);
    sa.sa_handler = SIG_IGN; sigaction(SIGPIPE, &sa, NULL);
}

typedef dio_fb dfb;
#define fb_open dio_fb_open
#define fb_describe dio_fb_describe
#define fb_present dio_fb_present
static void fb_close(dfb *f, int clear) { char w[200]; dio_fb_close(f, clear, w, sizeof w); if (w[0]) outf("{\"ev\":\"warning\",\"warning\":\"%s\"}", w); }

/* ---------- controller mapping (a text file in the bundle, so codes can be corrected without rebuilding): devio.c ---------- */
typedef dio_map cmap;
#define map_default dio_map_default
#define map_load dio_map_load

/* ---------- evdev devices (devio.c; the open-time settle is explained there) ---------- */
typedef dio_dev edev;
#define INPUT_SETTLE_MS 400
static uint64_t g_settle_ms = INPUT_SETTLE_MS;
typedef dio_inputs inputs;
static void on_seen(void *ctx, int kind, const char *path, const char *name, int mapped) { (void)ctx; char a[160], b[300]; jq(path, a, sizeof a); jq(name, b, sizeof b);
    if (kind == 0) outf("{\"ev\":\"input_device_seen\",\"path\":%s,\"name\":%s,\"mapped\":%s}", a, b, mapped ? "true" : "false");
    else outf("{\"ev\":\"input_device\",\"path\":%s,\"name\":%s,\"event_size\":%zu}", a, b, (size_t)EVSIZE); }
static int inputs_scan(inputs *in, const cmap *m, int list_all) { return dio_inputs_scan(in, m, list_all, g_settle_ms, on_seen, NULL); }
static int inputs_open_fixed(inputs *in, const char *path) { return dio_inputs_open_fixed(in, path, g_settle_ms, on_seen, NULL); }

/* ---------- SELECT+START arbiter: the H1 emergency exit is recognized before SELECT/START reach the HOME ---------- */
typedef struct {
    int phys[B_COUNT];                 /* how many controllers hold the button */
    int fwd[B_COUNT], pend[B_COUNT];   /* press forwarded to the HOME / held back waiting for the partner */
    uint64_t pend_at[B_COUNT];
    int combo; uint64_t combo_at; uint32_t hold_ms, grace_ms; int fired;
} arbiter;

typedef struct { input_state in; home *hm; uint64_t presses; int activity; } dstate;   /* activity: something reached the HOME (draw now) */
static void on_input(void *ctx, button b, in_kind k, uint64_t t) {
    dstate *s = ctx;
    if (k == IN_DUPLICATE || k == IN_REJECTED) return;
    if (s->hm) home_input(s->hm, b, k, t);
    s->activity = 1;
    if (k == IN_PRESS || k == IN_REPEAT) s->presses++;
    outf("{\"ev\":\"input\",\"button\":\"%s\",\"kind\":\"%s\",\"clock_ms\":%llu}", button_name(b), in_kind_name(k), (unsigned long long)t);
}
static int is_ss(button b) { return b == B_SELECT || b == B_START; }
static void arb_press(arbiter *a, dstate *s, button b, uint64_t now) {
    if (a->phys[b]++) return;                      /* another controller already holds it */
    if (!is_ss(b)) { input_press(&s->in, b, on_input, s); return; }
    button o = b == B_SELECT ? B_START : B_SELECT;
    if (a->phys[o]) {                              /* the partner is held: this is the combination, not a menu action */
        a->combo = 1; a->combo_at = now; a->pend[b] = a->pend[o] = 0;
        outf("{\"ev\":\"exit_combo\",\"state\":\"held\",\"hold_ms\":%u,\"select_forwarded\":%s,\"start_forwarded\":%s}", a->hold_ms, a->fwd[B_SELECT] ? "true" : "false", a->fwd[B_START] ? "true" : "false");
        return;
    }
    a->pend[b] = 1; a->pend_at[b] = now;           /* wait briefly for the partner before it becomes an ordinary press */
}
static void arb_release(arbiter *a, dstate *s, button b, uint64_t now) {
    if (!a->phys[b] || --a->phys[b]) return;
    if (is_ss(b) && a->combo) { a->combo = 0; outf("{\"ev\":\"exit_combo\",\"state\":\"cancelled\",\"held_ms\":%llu}", (unsigned long long)(now - a->combo_at)); }
    if (a->pend[b]) { a->pend[b] = 0; input_press(&s->in, b, on_input, s); input_release(&s->in, b, on_input, s); return; }   /* a quick tap */
    if (a->fwd[b] || !is_ss(b)) { a->fwd[b] = 0; input_release(&s->in, b, on_input, s); }
}
static void arb_tick(arbiter *a, dstate *s, uint64_t now) {
    for (int b = B_START; b <= B_SELECT; b++) if (a->pend[b] && now - a->pend_at[b] >= a->grace_ms) { a->pend[b] = 0; a->fwd[b] = 1; input_press(&s->in, (button)b, on_input, s); }
    if (a->combo && !a->fired && now - a->combo_at >= a->hold_ms) { a->fired = 1; outf("{\"ev\":\"exit_combo\",\"state\":\"exit\",\"held_ms\":%llu}", (unsigned long long)(now - a->combo_at)); }
}
static void arb_reset(arbiter *a) { memset(a->phys, 0, sizeof a->phys); memset(a->fwd, 0, sizeof a->fwd); memset(a->pend, 0, sizeof a->pend); a->combo = 0; }

/* read whatever is available: whole records only, partial records kept for the next read; returns -1 when the device is gone */
static int dev_read(edev *d, const cmap *m, void (*fn)(void *, edev *, int, int, int), void *ctx) { (void)m; return dio_dev_read(d, fn, ctx); }

/* ---------- --device ---------- */
typedef struct { dstate s; arbiter a; cmap m; inputs in; uint64_t t0; int dropped; } rt;
static uint64_t now_ms(const rt *r) { return (mono_us() - r->t0) / 1000u; }
static void rt_event(void *ctx, edev *d, int type, int code, int value) {
    rt *r = ctx;
    if (type == EV_SYN_ && code == SYN_DROPPED_) { r->dropped = 1; return; }
    if (type != EV_KEY_ || code < 0 || code > KEY_MAX_ || r->m.key[code] < 0 || value == 2) return;   /* value 2: kernel autorepeat (the HOME repeats itself) */
    button b = (button)r->m.key[code]; uint64_t t = now_ms(r);
    if (mono_ms_now() < d->settle_until) { d->down[b] = value ? 1 : 0; d->settle_ignored++; return; }   /* settling: state only, never a press */
    if (value && !d->down[b]) { d->down[b] = 1; arb_press(&r->a, &r->s, b, t); }
    else if (!value && d->down[b]) { d->down[b] = 0; arb_release(&r->a, &r->s, b, t); }
}
static void rt_device_lost(rt *r, int i, const char *why) {
    char a[160]; jq(r->in.d[i].path, a, sizeof a); outf("{\"ev\":\"input_disconnected\",\"path\":%s,\"reason\":\"%s\"}", a, why);
    close(r->in.d[i].fd); r->in.d[i] = r->in.d[--r->in.n];
    /* the simplest safe rule: everything held is released (forced), the arbiter forgets pending SELECT/START presses */
    arb_reset(&r->a); for (int k = 0; k < r->in.n; k++) memset(r->in.d[k].down, 0, sizeof r->in.d[k].down);
    input_device(&r->s.in, 0, on_input, &r->s); input_device(&r->s.in, r->in.n > 0, NULL, NULL);   /* a rescan reconnects */
}

static long rss_kb(void) { return dio_rss_kb(); }

static int state_dir_check(const char *dir, int any_fs, char *err, size_t n, char *fsname, size_t fn) {
    if (!dir || !*dir) { snprintf(err, n, "--state-dir is required (H1 keeps the theme choice in RAM, e.g. /tmp/ccs/state); there is no default"); return -1; }
    char e[400]; if (dio_ram_dir(dir, any_fs, e, sizeof e, fsname, fn)) { snprintf(err, n, "--state-dir: %s", e); return -1; } return 0; }

/* ---------- HOME Music (D-81): the console's sound ----------
 * The console's ALSA device hw:0,0 takes S16_LE, 2 channels, 48,000 Hz (its /etc/asound.conf), which is the HOME Music file's own format,
 * so the samples go to the device unchanged. The static musl runtime cannot load the console's glibc libasound, so this speaks the
 * kernel's PCM interface directly (the kernel's <sound/asound.h>: HW_PARAMS, SW_PARAMS, PREPARE, WRITEI_FRAMES, DELAY, DROP). The
 * file is read in pieces (never held whole); non-blocking writes top up the device's buffer between frames; Off and exit stop the
 * sound at once (DROP). The whole song plays, then it starts again from the beginning. A device that cannot be opened never stops
 * the HOME: it reports audio_unavailable and the HOME runs silently.
 * Test seam (host-side verification only; never used on the console): --audio-file PATH writes the samples the device would get,
 * paced by the clock, to a file instead. It exercises the reading, looping, pause and resume logic, not the kernel's PCM interface. */
#define SND_CHUNK 1024
typedef struct {
    int on; dio_pcm pcm; FILE *wav, *sink; long data_off; uint32_t frames, pos, loops; int playing; char dev[300];
    int16_t chunk[SND_CHUNK * 2]; uint32_t chunk_n, chunk_off;   /* read from the file, not yet written */
    uint64_t sink_resume_us, sink_resume_written, sink_written;
} dsnd;
static int wav_open(dsnd *s, const char *path, char *why, size_t n) {
    s->wav = fopen(path, "rb"); if (!s->wav) { snprintf(why, n, "cannot open %s", path); return -1; }
    uint8_t h[12]; if (fread(h, 1, 12, s->wav) != 12 || memcmp(h, "RIFF", 4) || memcmp(h + 8, "WAVE", 4)) { snprintf(why, n, "not a WAV file"); return -1; }
    int fmt_ok = 0;
    for (;;) {
        uint8_t c[8]; if (fread(c, 1, 8, s->wav) != 8) { snprintf(why, n, "no data chunk"); return -1; }
        uint32_t sz = (uint32_t)c[4] | (uint32_t)c[5] << 8 | (uint32_t)c[6] << 16 | (uint32_t)c[7] << 24;
        if (!memcmp(c, "fmt ", 4)) { uint8_t f[16]; if (sz < 16 || fread(f, 1, 16, s->wav) != 16) { snprintf(why, n, "short fmt chunk"); return -1; }
            unsigned tag = f[0] | f[1] << 8, ch = f[2] | f[3] << 8, bits = f[14] | f[15] << 8; uint32_t rate = (uint32_t)f[4] | (uint32_t)f[5] << 8 | (uint32_t)f[6] << 16 | (uint32_t)f[7] << 24;
            if (tag != 1 || ch != 2 || rate != 48000 || bits != 16) { snprintf(why, n, "the music is not 16-bit stereo PCM at 48000 Hz"); return -1; }
            fmt_ok = 1; if (sz > 16 && fseek(s->wav, (long)(sz - 16 + (sz & 1)), SEEK_CUR)) { snprintf(why, n, "bad fmt chunk"); return -1; } }
        else if (!memcmp(c, "data", 4)) { if (!fmt_ok) { snprintf(why, n, "data before fmt"); return -1; } s->data_off = ftell(s->wav); s->frames = sz / 4; break; }
        else if (fseek(s->wav, (long)(sz + (sz & 1)), SEEK_CUR)) { snprintf(why, n, "bad chunk"); return -1; }
    }
    if (!s->frames) { snprintf(why, n, "the music has no samples"); return -1; }
    return 0;
}
static int pcm_open(dsnd *s, char *why, size_t n) { return dio_pcm_open(&s->pcm, s->dev, 48000, why, n); }
/* frames accepted (0 when the device's buffer is full), -1 on a lasting error */
static int pcm_write(dsnd *s, const int16_t *b, uint32_t frames) {
    if (s->sink) {
        uint64_t due = s->sink_resume_written + (mono_us() - s->sink_resume_us) * 48000u / 1000000u + 4800u;   /* 100 ms ahead, as a device buffer would be */
        if (s->sink_written >= due) return 0; uint64_t k = due - s->sink_written; if (k > frames) k = frames;
        if (fwrite(b, 4, (size_t)k, s->sink) != (size_t)k) return -1; fflush(s->sink); s->sink_written += k; return (int)k;
    }
    return dio_pcm_write(&s->pcm, b, frames);   /* a slow frame that emptied the buffer starts it again there */
}
static void snd_stop(dsnd *s) {   /* silent at once; the playhead steps back to what was actually heard */
    uint32_t unplayed = s->chunk_n - s->chunk_off;
    if (s->sink) { uint64_t played = s->sink_resume_written + (mono_us() - s->sink_resume_us) * 48000u / 1000000u;   /* the test file keeps only what was heard */
        if (s->sink_written > played) { unplayed += (uint32_t)(s->sink_written - played); s->sink_written = played; fflush(s->sink); fseek(s->sink, (long)(played * 4), SEEK_SET); } }
    else if (s->pcm.fd >= 0) { unplayed += (uint32_t)dio_pcm_delay(&s->pcm); dio_pcm_drop(&s->pcm); }
    unplayed %= s->frames; s->pos = (s->pos + s->frames - unplayed) % s->frames; s->chunk_n = s->chunk_off = 0; s->playing = 0;
    outf("{\"ev\":\"music_pause\",\"pos\":%u,\"loops\":%u,\"underruns\":%u}", s->pos, s->loops, s->pcm.underruns);
}
static void snd_tick(dsnd *s, int want) {
    if (!s->on) return;
    if (!want) { if (s->playing) snd_stop(s); return; }
    if (!s->playing) {
        if (s->sink) { s->sink_resume_us = mono_us(); s->sink_resume_written = s->sink_written; } else if (dio_pcm_prepare(&s->pcm)) { outf("{\"ev\":\"audio_unavailable\",\"error\":\"the sound device could not be prepared again\"}"); s->on = 0; return; }
        if (fseek(s->wav, s->data_off + (long)s->pos * 4, SEEK_SET)) { s->on = 0; return; }
        s->playing = 1; outf("{\"ev\":\"music_play\",\"pos\":%u,\"frames\":%u}", s->pos, s->frames);
    }
    for (int guard = 0; guard < 64; guard++) {
        if (s->chunk_off == s->chunk_n) {
            if (s->pos >= s->frames) { s->pos = 0; s->loops++; fseek(s->wav, s->data_off, SEEK_SET); outf("{\"ev\":\"music_loop\",\"loops\":%u}", s->loops); }   /* the whole song played: from its beginning */
            uint32_t k = s->frames - s->pos; if (k > SND_CHUNK) k = SND_CHUNK;
            if (fread(s->chunk, 4, k, s->wav) != k) { outf("{\"ev\":\"audio_unavailable\",\"error\":\"the music file could not be read\"}"); snd_stop(s); s->on = 0; return; }
            s->chunk_n = k; s->chunk_off = 0; s->pos += k;
        }
        int w = pcm_write(s, s->chunk + s->chunk_off * 2, s->chunk_n - s->chunk_off);
        if (w < 0) { outf("{\"ev\":\"audio_unavailable\",\"error\":\"the sound device stopped: %s\"}", strerror(errno)); snd_stop(s); s->on = 0; return; }
        if (w == 0) break;
        s->chunk_off += (uint32_t)w;
    }
}
static void snd_init(dsnd *s, const char *music, const char *mode, const char *dev, const char *file) {
    memset(s, 0, sizeof *s); s->pcm.fd = -1; snprintf(s->dev, sizeof s->dev, "%s", dev); char why[400] = "", w2[600];
    if (!strcmp(mode, "off")) { out("{\"ev\":\"audio_off\",\"note\":\"--audio off\"}"); return; }
    if (wav_open(s, music, why, sizeof why)) { jq(why, w2, sizeof w2); outf("{\"ev\":\"audio_unavailable\",\"error\":%s}", w2); if (s->wav) fclose(s->wav); s->wav = NULL; return; }
    if (file) { s->sink = fopen(file, "wb"); if (!s->sink) { jq(file, w2, sizeof w2); outf("{\"ev\":\"audio_unavailable\",\"error\":\"cannot write the test sink\",\"path\":%s}", w2); fclose(s->wav); s->wav = NULL; return; } }
    else if (pcm_open(s, why, sizeof why)) { jq(why, w2, sizeof w2); outf("{\"ev\":\"audio_unavailable\",\"error\":%s}", w2); fclose(s->wav); s->wav = NULL; return; }
    s->on = 1; jq(file ? file : s->dev, w2, sizeof w2);
    outf("{\"ev\":\"audio_ready\",\"output\":\"%s\",\"path\":%s,\"rate\":48000,\"channels\":2,\"format\":\"S16_LE\",\"frames\":%u,\"buffer\":%lu,\"period\":%lu}", file ? "test-file" : "alsa", w2, s->frames, s->pcm.buffer, s->pcm.period);
}
static void snd_close(dsnd *s) {
    if (s->playing) snd_stop(s);
    dio_pcm_close(&s->pcm); if (s->wav) { fclose(s->wav); s->wav = NULL; }
    if (s->sink) { fflush(s->sink); if (ftruncate(fileno(s->sink), (off_t)(s->sink_written * 4))) { } fclose(s->sink); s->sink = NULL; }
    if (s->on) outf("{\"ev\":\"audio_closed\",\"loops\":%u,\"underruns\":%u,\"written\":%llu}", s->loops, s->pcm.underruns, (unsigned long long)s->sink_written);
    s->on = 0;
}

static void usage(void) {
    fputs("usage: ccs-home-armv7 --device --pack DIR --state-dir DIR [--fb /dev/fb0] [--input auto|PATH] [--map FILE] [--fps N]\n"
          "         [--exit-hold-ms 3000] [--cover-budget N] [--watchdog-ms 15000] [--run-ms N] [--stats-ms 5000] [--input-settle-ms 400]\n"
          "       ccs-home-armv7 --inspect-input [--map FILE] [--input auto|PATH] [--run-ms N] [--input-settle-ms 400]\n"
          "       ccs-home-armv7 --inspect-fb [--fb /dev/fb0]\n"
          "         [--audio auto|off] [--audio-device /dev/snd/pcmC0D0p]   (HOME Music, when the package has it)\n"
          "test seams (host only): --fb-file PATH --fb-geometry SPEC, --test-allow-any-state-fs, --test-keep-frame, --test-dump-ppm PATH, --audio-file PATH\n", stderr);
}

int device_main(int argc, char **argv) {
    const char *pack = NULL, *state = NULL, *fbpath = "/dev/fb0", *fbfile = NULL, *geom = NULL, *input = "auto", *mapf = NULL, *dump = NULL, *v;
    const char *audio = "auto", *audio_dev = "/dev/snd/pcmC0D0p", *audio_file = NULL;   /* HOME Music (D-81) */
    int fps = 30, idle_fps = 5, cover_budget = 2, any_fs = 0, keep = 0; long run_ms = -1, watchdog_ms = 15000, stats_ms = 5000; uint32_t hold = 3000, grace = COMBO_GRACE_MS;
    for (int i = 1; i < argc; i++) {
        if (!strcmp(argv[i], "--device")) continue;
        else if ((v = arg_val(argc, argv, &i, "--pack"))) pack = v; else if ((v = arg_val(argc, argv, &i, "--state-dir"))) state = v;
        else if ((v = arg_val(argc, argv, &i, "--fb"))) fbpath = v; else if ((v = arg_val(argc, argv, &i, "--fb-file"))) fbfile = v; else if ((v = arg_val(argc, argv, &i, "--fb-geometry"))) geom = v;
        else if ((v = arg_val(argc, argv, &i, "--input"))) input = v; else if ((v = arg_val(argc, argv, &i, "--map"))) mapf = v;
        else if ((v = arg_val(argc, argv, &i, "--fps"))) fps = atoi(v); else if ((v = arg_val(argc, argv, &i, "--idle-fps"))) idle_fps = atoi(v); else if ((v = arg_val(argc, argv, &i, "--cover-budget"))) cover_budget = atoi(v);
        else if ((v = arg_val(argc, argv, &i, "--input-settle-ms"))) g_settle_ms = (uint64_t)atol(v);
        else if ((v = arg_val(argc, argv, &i, "--exit-hold-ms"))) hold = (uint32_t)atol(v); else if ((v = arg_val(argc, argv, &i, "--combo-grace-ms"))) grace = (uint32_t)atol(v);
        else if ((v = arg_val(argc, argv, &i, "--run-ms"))) run_ms = atol(v); else if ((v = arg_val(argc, argv, &i, "--watchdog-ms"))) watchdog_ms = atol(v); else if ((v = arg_val(argc, argv, &i, "--stats-ms"))) stats_ms = atol(v);
        else if ((v = arg_val(argc, argv, &i, "--test-dump-ppm"))) dump = v;
        else if ((v = arg_val(argc, argv, &i, "--audio"))) audio = v; else if ((v = arg_val(argc, argv, &i, "--audio-device"))) audio_dev = v; else if ((v = arg_val(argc, argv, &i, "--audio-file"))) audio_file = v;
        else if (!strcmp(argv[i], "--test-allow-any-state-fs")) any_fs = 1; else if (!strcmp(argv[i], "--test-keep-frame")) keep = 1;
        else { fprintf(stderr, "unknown argument: %s\n", argv[i]); usage(); return EXIT_USAGE; }
    }
    if (!pack || fps < 1 || fps > 60 || idle_fps < 1 || idle_fps > fps || hold < 500 || hold > 30000 || grace > 1000) { usage(); return EXIT_USAGE; }
    if (fbfile && !geom) { fputs("--fb-file needs --fb-geometry\n", stderr); return EXIT_USAGE; }
    char err[400], e2[600], fsn[40];
    static rt r; memset(&r, 0, sizeof r); r.a.hold_ms = hold; r.a.grace_ms = grace; r.t0 = mono_us();
    /* 1. the controller mapping */
    if (mapf) { if (map_load(&r.m, mapf, err, sizeof err)) { jq(err, e2, sizeof e2); outf("{\"ev\":\"refused\",\"stage\":\"map\",\"error\":%s}", e2); return EXIT_MAP; } } else map_default(&r.m);
    /* 2. the RAM state folder */
    if (state_dir_check(state, any_fs, err, sizeof err, fsn, sizeof fsn)) { jq(err, e2, sizeof e2); outf("{\"ev\":\"refused\",\"stage\":\"state-dir\",\"error\":%s}", e2); return EXIT_STATE; }
    /* 3. the framebuffer: queried and validated before anything is drawn; the mode is never changed */
    static dfb f; if (fb_open(&f, fbfile ? fbfile : fbpath, fbfile ? geom : NULL, 1, err, sizeof err)) { jq(err, e2, sizeof e2); outf("{\"ev\":\"refused\",\"stage\":\"framebuffer\",\"error\":%s}", e2); fb_close(&f, 0); return EXIT_FB; }
    /* 4. the controller(s), found by name */
    r.in.fixed = strcmp(input, "auto") ? input : NULL;
    if (r.in.fixed ? inputs_open_fixed(&r.in, r.in.fixed) : (inputs_scan(&r.in, &r.m, 0) ? 0 : -1)) {
        snprintf(err, sizeof err, r.in.fixed ? "cannot open the input %s" : "no input device named \"%s\" (is clovercon loaded?)", r.in.fixed ? r.in.fixed : r.m.devices[0]);
        jq(err, e2, sizeof e2); outf("{\"ev\":\"refused\",\"stage\":\"input\",\"error\":%s}", e2); fb_close(&f, 0); return EXIT_INPUT;
    }
    /* 5. the HOME package (read-only) */
    input_init(&r.s.in); r.s.in.repeat_delay_ms = 400; r.s.in.repeat_rate_ms = 110; r.s.in.repeat_fast_after = 6; r.s.in.repeat_fast_rate = 55;   /* HOME_REPEAT, as main.c */
    r.s.hm = home_load2(pack, state, out, err, sizeof err);
    if (!r.s.hm) { jq(err, e2, sizeof e2); outf("{\"ev\":\"refused\",\"stage\":\"pack\",\"error\":%s}", e2); fb_close(&f, 0); return EXIT_PACK; }
    home_set_cover_budget(r.s.hm, cover_budget);
    framebuffer fr; if (fb_init(&fr, CCS_FB_W, CCS_FB_H)) { out("{\"ev\":\"refused\",\"stage\":\"memory\",\"error\":\"frame allocation failed\"}"); home_free(r.s.hm); fb_close(&f, 0); return EXIT_FB; }
    uint8_t *prev = malloc((size_t)CCS_FB_W * CCS_FB_H * 3); if (!prev) { out("{\"ev\":\"refused\",\"stage\":\"memory\",\"error\":\"frame allocation failed\"}"); return EXIT_FB; }
    install_signals();
    char desc[700], sd[300]; fb_describe(&f, desc, sizeof desc); jq(state, sd, sizeof sd);
    outf("{\"ev\":\"device_ready\",\"protocol\":\"%s\",\"fb\":{%s,\"pages\":%d,\"page_flip\":%s,\"backend\":\"%s\"},\"state_dir\":%s,\"state_fs\":\"%s\",\"fps\":%d,\"idle_fps\":%d,\"exit_hold_ms\":%u,\"combo_grace_ms\":%u,\"event_size\":%zu,\"theme\":\"%s\",\"games\":%zu,\"note\":\"H1 device test: games are never launched\"}",
         CCS_SIM_PROTOCOL, desc, f.pages, f.flip ? "true" : "false", f.is_file ? "test-file (pan emulated)" : "fbdev", sd, fsn, fps, idle_fps, hold, grace, (size_t)EVSIZE, home_theme(r.s.hm), home_games(r.s.hm));
    home_emit_state(r.s.hm);
    static dsnd snd; { char mf[1100]; int mon = home_music(r.s.hm, mf, sizeof mf); if (mon >= 0 && mf[0]) snd_init(&snd, mf, audio, audio_dev, audio_file); else { memset(&snd, 0, sizeof snd); snd.pcm.fd = -1; } }   /* HOME Music, when the package has it */

    uint64_t period = 1000000u / (unsigned)fps, idle_period = 1000000u / (unsigned)idle_fps, next = mono_us(), rclock = 0, still = 0, frames = 0, presented = 0, skipped = 0, rsum = 0, rmax = 0, psum = 0, pmax = 0, last_stats = next, late = 0;
    int have_prev = 0, rc = 0, settled = 0; const char *reason = "signal";
    for (int i = 0; i < r.in.n; i++) r.in.d[i].settle_until = mono_ms_now() + g_settle_ms;   /* whatever the controller reported while the HOME loaded is settling too */
    while (!g_stop) {
        /* input: first what arrived (a release waiting behind a slow frame ends the press before any repeat is due), then
         * the repeat clock advances to now */
        for (int i = 0; i < r.in.n; i++) {
            if (dev_read(&r.in.d[i], &r.m, rt_event, &r) < 0) { rt_device_lost(&r, i, errno == ENODEV ? "device removed" : "end of input"); i--; continue; }
            if (r.dropped) { r.dropped = 0; out("{\"ev\":\"input_dropped\",\"note\":\"the kernel dropped events (SYN_DROPPED): every button is released\"}"); arb_reset(&r.a); for (int k = 0; k < r.in.n; k++) memset(r.in.d[k].down, 0, sizeof r.in.d[k].down); input_device(&r.s.in, 0, on_input, &r.s); input_device(&r.s.in, 1, NULL, NULL); }
        }
        uint64_t t = now_ms(&r);
        if (t > r.s.in.clock_ms + 120) for (int b = 0; b < B_COUNT; b++) if (r.s.in.down[b] && r.s.in.next_repeat[b] + 120 < t) r.s.in.next_repeat[b] = t - 120;   /* after a slow frame: at most a couple of catch-up repeats */
        input_tick(&r.s.in, t - r.s.in.clock_ms, on_input, &r.s);
        if (!settled && r.in.n > 0) {
            int all = 1; for (int i = 0; i < r.in.n; i++) if (mono_ms_now() < r.in.d[i].settle_until) all = 0;
            if (all) {
                settled = 1; char held[120] = ""; size_t L = 0; int ign = 0;
                for (int i = 0; i < r.in.n; i++) { ign += r.in.d[i].settle_ignored; for (int b = 0; b < B_COUNT; b++) if (r.in.d[i].down[b] && L < sizeof held - 12) L += (size_t)snprintf(held + L, sizeof held - L, "%s\"%s\"", L ? "," : "", button_name((button)b)); }
                outf("{\"ev\":\"input_ready\",\"settle_ms\":%llu,\"ignored_events\":%d,\"held\":[%s]}", (unsigned long long)g_settle_ms, ign, held);
            }
        }
        arb_tick(&r.a, &r.s, now_ms(&r));
        snd_tick(&snd, home_music(r.s.hm, NULL, 0) == 1 && !r.a.fired);   /* HOME Music: plays while it is On; Off is silent at once */
        if (r.s.activity) { r.s.activity = 0; still = 0; if (next > mono_us()) next = mono_us(); }   /* respond to the controller at once */
        if (r.a.fired) { reason = "emergency-exit"; break; }
        if (run_ms >= 0 && (long)now_ms(&r) >= run_ms) { reason = "run-ms"; break; }
        if (!r.in.fixed && r.in.n == 0 && mono_us() >= r.in.next_scan_us) { r.in.next_scan_us = mono_us() + 1000000u; if (inputs_scan(&r.in, &r.m, 0)) input_device(&r.s.in, 1, NULL, NULL); }
        /* a frame when one is due: render (the same frame Preview on TV draws), present only if it changed */
        uint64_t nowu = mono_us();
        if (nowu >= next) {
            if (watchdog_ms > 0) alarm((unsigned)((watchdog_ms + 999) / 1000));
            uint64_t a0 = mono_us(); rclock = r.s.in.clock_ms; home_render(r.s.hm, &fr, rclock); uint64_t a1 = mono_us();
            size_t nb = (size_t)fr.w * fr.h * 3;
            if (have_prev && !memcmp(prev, fr.rgb, nb)) { skipped++; still++; }
            else {
                int fell = 0; char why[200] = ""; fb_present(&f, &fr, &fell, why, sizeof why); memcpy(prev, fr.rgb, nb); have_prev = 1; presented++; still = 0;
                if (fell) { char w[300]; jq(why, w, sizeof w); outf("{\"ev\":\"page_flip_fallback\",\"reason\":%s,\"after_pans\":%d}", w, f.pans); }
                uint64_t a2 = mono_us(); psum += a2 - a1; if (a2 - a1 > pmax) pmax = a2 - a1;
            }
            if (watchdog_ms > 0) alarm(0);
            frames++; rsum += a1 - a0; if (a1 - a0 > rmax) rmax = a1 - a0;
            /* a still screen (3 identical frames, nothing pressed) is redrawn at the idle rate until something changes */
            uint64_t per = still >= 3 && !home_covers_pending(r.s.hm) ? idle_period : period;
            next += per; if (next < mono_us()) { next = mono_us() + per; if (per == period) late++; }   /* a slow frame drops frames, it never queues them */
            if (stats_ms > 0 && mono_us() - last_stats >= (uint64_t)stats_ms * 1000u) {
                struct rusage ru; getrusage(RUSAGE_SELF, &ru); double el = (double)(mono_us() - last_stats) / 1e6; last_stats = mono_us();
                outf("{\"ev\":\"stats\",\"clock_ms\":%llu,\"frames\":%llu,\"presented\":%llu,\"unchanged\":%llu,\"late\":%llu,\"render_ms_avg\":%.1f,\"render_ms_max\":%.1f,\"present_ms_avg\":%.1f,\"present_ms_max\":%.1f,\"cpu_s\":%.2f,\"window_s\":%.2f,\"rss_kb\":%ld,\"page_flip\":%s,\"pans\":%d,\"covers_pending\":%d}",
                     (unsigned long long)r.s.in.clock_ms, (unsigned long long)frames, (unsigned long long)presented, (unsigned long long)skipped, (unsigned long long)late, frames ? rsum / 1000.0 / frames : 0.0, rmax / 1000.0,
                     presented ? psum / 1000.0 / presented : 0.0, pmax / 1000.0, ru.ru_utime.tv_sec + ru.ru_stime.tv_sec + (ru.ru_utime.tv_usec + ru.ru_stime.tv_usec) / 1e6, el, rss_kb(), f.flip ? "true" : "false", f.pans, home_covers_pending(r.s.hm));
            }
        }
        /* sleep in poll() until the next frame is due or a controller event arrives: no busy loop */
        struct pollfd pf[MAX_DEV]; for (int i = 0; i < r.in.n; i++) { pf[i].fd = r.in.d[i].fd; pf[i].events = POLLIN; pf[i].revents = 0; }
        uint64_t nu = mono_us(); int wait = next > nu ? (int)((next - nu + 999) / 1000) : 0;
        for (int b = B_START; b <= B_SELECT; b++) if (r.a.pend[b]) { long due = (long)(r.a.pend_at[b] + r.a.grace_ms) - (long)now_ms(&r); if (due < wait) wait = due < 0 ? 0 : (int)due; }
        if (r.a.combo) { long due = (long)(r.a.combo_at + r.a.hold_ms) - (long)now_ms(&r); if (due < wait) wait = due < 0 ? 0 : (int)due; }
        if (!settled) for (int i = 0; i < r.in.n; i++) { long due = (long)(r.in.d[i].settle_until - mono_ms_now()); if (due < wait) wait = due < 0 ? 0 : (int)due + 1; }
        int fixed_file = r.in.fixed && r.in.n == 0;   /* the test stream ended: just pace frames */
        if (snd.playing && wait > 40) wait = 40;   /* keep the sound device's buffer topped up */
        if (r.in.n > 0 && !fixed_file) poll(pf, (nfds_t)r.in.n, wait); else if (wait > 0) poll(NULL, 0, wait);
    }
    if (g_stop) { reason = g_signal == SIGTERM ? "SIGTERM" : g_signal == SIGINT ? "SIGINT" : g_signal == SIGHUP ? "SIGHUP" : "signal"; }
    if (dump) { uint32_t crc = 0; fb_write_ppm(&fr, dump, &crc); char d[400]; jq(dump, d, sizeof d); outf("{\"ev\":\"frame_dumped\",\"path\":%s,\"crc32\":\"%08x\",\"render_clock_ms\":%llu,\"shown_yoffset\":%u}", d, crc, (unsigned long long)rclock, f.shown_yoff); }
    snd_close(&snd);   /* HOME Music stops with the HOME */
    /* shutdown: release everything held (the HOME sees releases), clear the screen, restore the original display offset */
    input_device(&r.s.in, 0, on_input, &r.s);
    for (int i = 0; i < r.in.n; i++) close(r.in.d[i].fd);
    uint32_t shown = f.shown_yoff; fb_close(&f, !keep);
    outf("{\"ev\":\"device_exit\",\"reason\":\"%s\",\"frames\":%llu,\"presented\":%llu,\"unchanged\":%llu,\"late\":%llu,\"clock_ms\":%llu,\"last_shown_yoffset\":%u,\"restored_yoffset\":%u,\"screen\":\"%s\",\"clean\":true}",
         reason, (unsigned long long)frames, (unsigned long long)presented, (unsigned long long)skipped, (unsigned long long)late, (unsigned long long)r.s.in.clock_ms, shown, f.orig_yoff, keep ? "kept (test)" : "cleared");
    home_free(r.s.hm); fb_free(&fr); free(prev);
    return rc;
}

/* ---------- --inspect-input: the H1 code check (prints raw events with what the mapping makes of them; changes nothing) ---------- */
typedef struct { const cmap *m; uint64_t t0; int pressed[32], np; } ictx;
static void inspect_event(void *ctx, edev *d, int type, int code, int value) {
    ictx *c = ctx; if (type == EV_SYN_ && code != SYN_DROPPED_) return;
    if (mono_ms_now() < d->settle_until) { d->settle_ignored++; return; }   /* open-time settle: what the driver reports right after the open is not a press */
    if (type == EV_KEY_ && value == 1 && c->np < 32) c->pressed[c->np++] = code;
    const char *bn = type == EV_KEY_ && code >= 0 && code <= KEY_MAX_ && c->m->key[code] >= 0 ? button_name((button)c->m->key[code]) : "(unmapped)";
    char a[160]; jq(d->path, a, sizeof a);
    outf("{\"ev\":\"raw_event\",\"path\":%s,\"type\":%d,\"code\":%d,\"value\":%d,\"mapped\":\"%s\",\"ms\":%llu}", a, type, code, value, type == EV_KEY_ ? bn : "(not a key)", (unsigned long long)((mono_us() - c->t0) / 1000u));
}
int device_inspect_input(int argc, char **argv) {
    const char *input = "auto", *mapf = NULL, *v; long run_ms = 30000; char err[400], e2[600];
    for (int i = 1; i < argc; i++) {
        if (!strcmp(argv[i], "--inspect-input")) continue; else if ((v = arg_val(argc, argv, &i, "--input"))) input = v; else if ((v = arg_val(argc, argv, &i, "--map"))) mapf = v;
        else if ((v = arg_val(argc, argv, &i, "--run-ms"))) run_ms = atol(v); else if ((v = arg_val(argc, argv, &i, "--input-settle-ms"))) g_settle_ms = (uint64_t)atol(v);
        else { fprintf(stderr, "unknown argument: %s\n", argv[i]); usage(); return EXIT_USAGE; }
    }
    static cmap m; if (mapf) { if (map_load(&m, mapf, err, sizeof err)) { jq(err, e2, sizeof e2); outf("{\"ev\":\"refused\",\"stage\":\"map\",\"error\":%s}", e2); return EXIT_MAP; } } else map_default(&m);
    static inputs in; memset(&in, 0, sizeof in);
    if (strcmp(input, "auto") ? inputs_open_fixed(&in, input) : (inputs_scan(&in, &m, 1) ? 0 : -1)) { outf("{\"ev\":\"refused\",\"stage\":\"input\",\"error\":\"no matching input device\"}"); return EXIT_INPUT; }
    install_signals(); static ictx c; c.m = &m; c.t0 = mono_us();
    outf("{\"ev\":\"inspect_ready\",\"devices\":%d,\"event_size\":%zu,\"run_ms\":%ld,\"note\":\"press each button and each D-pad direction once; nothing is changed\"}", in.n, (size_t)EVSIZE, run_ms);
    while (!g_stop && in.n > 0 && (long)((mono_us() - c.t0) / 1000u) < run_ms) {
        struct pollfd pf[MAX_DEV]; for (int i = 0; i < in.n; i++) { pf[i].fd = in.d[i].fd; pf[i].events = POLLIN; pf[i].revents = 0; }
        poll(pf, (nfds_t)in.n, 200);
        for (int i = 0; i < in.n; i++) if (dev_read(&in.d[i], &m, inspect_event, &c) < 0) { char a[160]; jq(in.d[i].path, a, sizeof a); outf("{\"ev\":\"input_disconnected\",\"path\":%s}", a); close(in.d[i].fd); in.d[i] = in.d[--in.n]; i--; }
    }
    for (int i = 0; i < in.n; i++) close(in.d[i].fd);
    char list[400] = ""; size_t L = 0; for (int i = 0; i < c.np && L < sizeof list - 8; i++) L += (size_t)snprintf(list + L, sizeof list - L, "%s%d", i ? "," : "", c.pressed[i]);
    outf("{\"ev\":\"inspect_end\",\"pressed_in_order\":[%s],\"note\":\"key codes in the order they were first pressed (for write-map.sh)\"}", list); return 0;
}

/* ---------- --inspect-fb: the framebuffer mode and the adapter's verdict, read-only (no mapping, no drawing) ---------- */
int device_inspect_fb(int argc, char **argv) {
    const char *fbpath = "/dev/fb0", *fbfile = NULL, *geom = NULL, *v; char err[400] = "", e2[600], desc[700];
    for (int i = 1; i < argc; i++) {
        if (!strcmp(argv[i], "--inspect-fb")) continue; else if ((v = arg_val(argc, argv, &i, "--fb"))) fbpath = v; else if ((v = arg_val(argc, argv, &i, "--fb-file"))) fbfile = v;
        else if ((v = arg_val(argc, argv, &i, "--fb-geometry"))) geom = v; else { fprintf(stderr, "unknown argument: %s\n", argv[i]); usage(); return EXIT_USAGE; }
    }
    static dfb f; int bad = fb_open(&f, fbfile ? fbfile : fbpath, fbfile ? geom : NULL, 0, err, sizeof err);
    fb_describe(&f, desc, sizeof desc); jq(err, e2, sizeof e2);
    char pq[300]; jq(fbfile ? "test-file" : fbpath, pq, sizeof pq);
    outf("{\"ev\":\"fb_info\",\"path\":%s,%s,\"pages\":%u,\"supported\":%s,\"error\":%s}", pq, desc, f.var.yres ? f.var.yres_virtual / f.var.yres : 0, bad ? "false" : "true", e2);
    fb_close(&f, 0); return bad ? EXIT_FB : 0;
}
#endif
