/* The console device adapter (H1): the same native HOME, drawn on a Linux framebuffer and driven by a Linux evdev controller.
 *
 *   ccs-home-armv7 --device --pack DIR --state-dir DIR [--fb /dev/fb0] [--input auto|PATH] [--map FILE] [--fps 30]
 *   ccs-home-armv7 --inspect-input [--map FILE] [--input auto|PATH] [--run-ms N]      prints raw controller events (read-only)
 *   ccs-home-armv7 --inspect-fb [--fb /dev/fb0]                                        prints the framebuffer mode (read-only)
 *
 * This file only translates: the HOME (home.c and the four themes) keeps navigation, focus, repeat and theme logic, and the
 * pixels presented are exactly the frame home_render draws for Preview on TV. Nothing here changes the video mode, launches a
 * game, or writes anywhere except the framebuffer and <state dir>/home-prefs.json (written by persist.c when the controller
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

/* ---------- monotonic clock ---------- */
#if defined(__arm__) && !defined(__GLIBC__)
#if defined(SYS_clock_gettime32)
#define CCS_CGT32 SYS_clock_gettime32
#elif defined(SYS_clock_gettime)
#define CCS_CGT32 SYS_clock_gettime
#endif
#ifdef CCS_CGT32
_Static_assert(CCS_CGT32 == 263, "ARM EABI clock_gettime is system call 263");
#endif
#endif
static uint64_t mono_us(void) {
#if defined(CCS_CGT32)
    /* the classic 32-bit clock_gettime (Linux 2.6+): no dependence on musl's 64-bit-time fallback on the console's 3.4 kernel */
    static uint64_t last; long t32[2];
    if (syscall(CCS_CGT32, CLOCK_MONOTONIC, t32) == 0) last = (uint64_t)(unsigned long)t32[0] * 1000000u + (uint64_t)t32[1] / 1000u;
    return last;   /* (cannot fail for CLOCK_MONOTONIC; libc's clock_gettime is not linked in, so no 64-bit-time call is in the binary) */
#else
    struct timespec ts; clock_gettime(CLOCK_MONOTONIC, &ts); return (uint64_t)ts.tv_sec * 1000000u + (uint64_t)ts.tv_nsec / 1000u;
#endif
}

static uint64_t mono_ms_now(void) { return mono_us() / 1000u; }

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

/* ---------- framebuffer: one seam, two back ends (the device via ioctl, or a test file with a declared mode) ---------- */
typedef struct {
    int fd, is_file, pan_mode, pan_fail_after, pans; uint8_t *map, *mem; size_t map_len;
    struct fb_var_screeninfo var; struct fb_fix_screeninfo fix;
    uint32_t orig_yoff, shown_yoff; int pages, flip;        /* flip: page flipping in use (else single-buffer drawing) */
    char path[256];
} dfb;

static int fb_validate(const dfb *f, char *err, size_t n) {
    const struct fb_var_screeninfo *v = &f->var; const struct fb_fix_screeninfo *x = &f->fix;
    if (v->xres != CCS_FB_W || v->yres != CCS_FB_H) { snprintf(err, n, "screen is %ux%u; the HOME draws 1280x720 and the mode is never changed", v->xres, v->yres); return -1; }
    if (v->bits_per_pixel != 32) { snprintf(err, n, "%u bits per pixel; only 32-bit XRGB/ARGB is supported", v->bits_per_pixel); return -1; }
    if (x->type != FB_TYPE_PACKED_PIXELS || x->visual != FB_VISUAL_TRUECOLOR || v->nonstd || v->grayscale) { snprintf(err, n, "not a packed true-color framebuffer (type %u visual %u nonstd %u grayscale %u)", x->type, x->visual, v->nonstd, v->grayscale); return -1; }
    const struct fb_bitfield *c[3] = { &v->red, &v->green, &v->blue }; unsigned used = 0;
    for (int i = 0; i < 3; i++) { if (c[i]->length != 8 || c[i]->msb_right || c[i]->offset % 8 || c[i]->offset > 24 || (used & (1u << (c[i]->offset / 8)))) { snprintf(err, n, "unsupported color fields r%u/%u g%u/%u b%u/%u", v->red.offset, v->red.length, v->green.offset, v->green.length, v->blue.offset, v->blue.length); return -1; } used |= 1u << (c[i]->offset / 8); }
    if (v->transp.length && (v->transp.length != 8 || (used & (1u << (v->transp.offset / 8))) || v->transp.offset % 8 || v->transp.offset > 24)) { snprintf(err, n, "unsupported alpha field %u/%u", v->transp.offset, v->transp.length); return -1; }
    if (x->line_length < v->xres * 4 || x->line_length % 4) { snprintf(err, n, "line length %u is too short or unaligned for %u pixels", x->line_length, v->xres); return -1; }
    if (v->xres_virtual < v->xres || v->yres_virtual < v->yres) { snprintf(err, n, "virtual size %ux%u is smaller than the screen", v->xres_virtual, v->yres_virtual); return -1; }
    if ((uint64_t)x->line_length * v->yres_virtual > x->smem_len) { snprintf(err, n, "framebuffer memory %u bytes is smaller than line %u x virtual height %u", x->smem_len, x->line_length, v->yres_virtual); return -1; }
    if (v->xoffset != 0 || v->yoffset + v->yres > v->yres_virtual) { snprintf(err, n, "display offset %u,%u is outside what the adapter supports", v->xoffset, v->yoffset); return -1; }
    return 0;
}

static void fb_describe(const dfb *f, char *o, size_t n) {
    const struct fb_var_screeninfo *v = &f->var;
    snprintf(o, n, "\"xres\":%u,\"yres\":%u,\"xres_virtual\":%u,\"yres_virtual\":%u,\"xoffset\":%u,\"yoffset\":%u,\"bits_per_pixel\":%u,\"line_length\":%u,\"smem_len\":%u,"
             "\"red\":[%u,%u],\"green\":[%u,%u],\"blue\":[%u,%u],\"transp\":[%u,%u],\"type\":%u,\"visual\":%u,\"ypanstep\":%u",
             v->xres, v->yres, v->xres_virtual, v->yres_virtual, v->xoffset, v->yoffset, v->bits_per_pixel, f->fix.line_length, f->fix.smem_len,
             v->red.offset, v->red.length, v->green.offset, v->green.length, v->blue.offset, v->blue.length, v->transp.offset, v->transp.length, f->fix.type, f->fix.visual, f->fix.ypanstep);
}

/* SPEC (tests): xres=1280,yres=720,vxres=1280,vyres=1440,bpp=32,line=5120,smem=7372800,r=16,g=8,b=0,a=-1,yoff=0,pan=1|0|failN */
static int fb_parse_geometry(dfb *f, const char *spec, char *err, size_t n) {
    struct fb_var_screeninfo *v = &f->var; struct fb_fix_screeninfo *x = &f->fix;
    v->xres = v->xres_virtual = 1280; v->yres = 720; v->yres_virtual = 1440; v->bits_per_pixel = 32; x->line_length = 5120; x->smem_len = 5120 * 1440;
    v->red.offset = 16; v->green.offset = 8; v->blue.offset = 0; v->red.length = v->green.length = v->blue.length = 8;
    x->type = FB_TYPE_PACKED_PIXELS; x->visual = FB_VISUAL_TRUECOLOR; x->ypanstep = 1; f->pan_mode = 1; f->pan_fail_after = -1;
    char buf[400]; snprintf(buf, sizeof buf, "%s", spec ? spec : "");
    for (char *t = strtok(buf, ","); t; t = strtok(NULL, ",")) {
        char *eq = strchr(t, '='); if (!eq) { snprintf(err, n, "bad geometry item %s", t); return -1; } *eq++ = 0; long val = strtol(eq, NULL, 10);
        if (!strcmp(t, "xres")) v->xres = (uint32_t)val; else if (!strcmp(t, "yres")) v->yres = (uint32_t)val; else if (!strcmp(t, "vxres")) v->xres_virtual = (uint32_t)val;
        else if (!strcmp(t, "vyres")) v->yres_virtual = (uint32_t)val; else if (!strcmp(t, "bpp")) v->bits_per_pixel = (uint32_t)val; else if (!strcmp(t, "line")) x->line_length = (uint32_t)val;
        else if (!strcmp(t, "smem")) x->smem_len = (uint32_t)val; else if (!strcmp(t, "r")) v->red.offset = (uint32_t)val; else if (!strcmp(t, "g")) v->green.offset = (uint32_t)val;
        else if (!strcmp(t, "b")) v->blue.offset = (uint32_t)val; else if (!strcmp(t, "a")) { if (val >= 0) { v->transp.offset = (uint32_t)val; v->transp.length = 8; } }
        else if (!strcmp(t, "yoff")) v->yoffset = (uint32_t)val; else if (!strcmp(t, "visual")) x->visual = (uint32_t)val;
        else if (!strcmp(t, "pan")) { if (!strncmp(eq, "fail", 4)) { f->pan_mode = 1; f->pan_fail_after = atoi(eq + 4); } else f->pan_mode = (int)val; }
        else { snprintf(err, n, "unknown geometry item %s", t); return -1; }
    }
    return 0;
}

static int fb_open(dfb *f, const char *path, const char *test_geometry, int map_it, char *err, size_t n) {
    memset(f, 0, sizeof *f); f->fd = -1; snprintf(f->path, sizeof f->path, "%s", path);
    if (test_geometry) {
        f->is_file = 1; if (fb_parse_geometry(f, test_geometry, err, n)) return -1;
        f->fd = open(path, map_it ? O_RDWR : O_RDONLY); if (f->fd < 0) { snprintf(err, n, "cannot open test framebuffer file %s: %s", path, strerror(errno)); return -1; }
        off_t size = lseek(f->fd, 0, SEEK_END); lseek(f->fd, 0, SEEK_SET);   /* (lseek, not fstat: musl's stat family tries statx, a 4.11 call) */
        if (size < 0 || (uint64_t)size < f->fix.smem_len) { snprintf(err, n, "test framebuffer file is smaller than smem %u", f->fix.smem_len); return -1; }
    } else {
        f->fd = open(path, map_it ? O_RDWR : O_RDONLY); if (f->fd < 0) { snprintf(err, n, "cannot open %s: %s", path, strerror(errno)); return -1; }
        if (ioctl(f->fd, FBIOGET_VSCREENINFO, &f->var)) { snprintf(err, n, "FBIOGET_VSCREENINFO failed on %s: %s", path, strerror(errno)); return -1; }
        if (ioctl(f->fd, FBIOGET_FSCREENINFO, &f->fix)) { snprintf(err, n, "FBIOGET_FSCREENINFO failed on %s: %s", path, strerror(errno)); return -1; }
    }
    f->orig_yoff = f->shown_yoff = f->var.yoffset;
    if (fb_validate(f, err, n)) return -1;
    if (!map_it) return 0;
    long pg = sysconf(_SC_PAGESIZE); size_t lead = f->is_file ? 0 : (size_t)(f->fix.smem_start & (unsigned long)(pg - 1));
    f->map_len = lead + f->fix.smem_len;
    void *m = mmap(NULL, f->map_len, PROT_READ | PROT_WRITE, MAP_SHARED, f->fd, 0);
    if (m == MAP_FAILED) { snprintf(err, n, "mmap of %zu bytes failed: %s", f->map_len, strerror(errno)); f->map = NULL; return -1; }
    f->map = m; f->mem = f->map + lead;
    /* page flipping needs two whole screens and a displayed page at 0 or yres; otherwise draw into the shown page */
    f->pages = (int)(f->var.yres_virtual / f->var.yres);
    f->flip = f->pages >= 2 && (f->var.yoffset == 0 || f->var.yoffset == f->var.yres) && (f->is_file ? f->pan_mode : 1);
    return 0;
}

static int fb_pan(dfb *f, uint32_t yoff) {
    if (f->is_file) {   /* emulated: the test declares whether panning works (and when it starts failing) */
        if (!f->pan_mode || (f->pan_fail_after >= 0 && f->pans >= f->pan_fail_after)) { errno = EINVAL; return -1; }
        f->pans++; f->shown_yoff = yoff; return 0;
    }
    struct fb_var_screeninfo v = f->var; v.xoffset = 0; v.yoffset = yoff;
    if (ioctl(f->fd, FBIOPAN_DISPLAY, &v)) return -1;
    f->pans++; f->shown_yoff = yoff; return 0;
}

/* RGB24 -> the framebuffer's 32-bit pixels at page row y0, using the reported channel offsets; only the visible 1280x720
 * rectangle of that page is written (row padding and the other page are never touched) */
static void fb_convert(const dfb *f, const framebuffer *src, uint32_t y0) {
    const struct fb_var_screeninfo *v = &f->var; uint32_t ro = v->red.offset, go = v->green.offset, bo = v->blue.offset;
    uint32_t alpha = v->transp.length == 8 ? (uint32_t)0xFF << v->transp.offset : 0;   /* opaque where the format has alpha */
    for (int y = 0; y < src->h; y++) {
        uint32_t *d = (uint32_t *)(void *)(f->mem + (size_t)(y0 + (uint32_t)y) * f->fix.line_length); const uint8_t *s = src->rgb + (size_t)y * src->w * 3;
        for (int x = 0; x < src->w; x++, s += 3) d[x] = ((uint32_t)s[0] << ro) | ((uint32_t)s[1] << go) | ((uint32_t)s[2] << bo) | alpha;
    }
}

/* present one frame: into the hidden page then pan; if panning fails, fall back for good to drawing into the shown page */
static int fb_present(dfb *f, const framebuffer *src, int *fell_back, char *why, size_t n) {
    if (f->flip) {
        uint32_t back = f->shown_yoff == 0 ? f->var.yres : 0; fb_convert(f, src, back);
        if (!fb_pan(f, back)) return 0;
        snprintf(why, n, "FBIOPAN_DISPLAY failed (%s): single-buffer drawing from now on", strerror(errno)); f->flip = 0; *fell_back = 1;
    }
    fb_convert(f, src, f->shown_yoff); return 0;
}

static void fb_close(dfb *f, int clear) {
    if (f->map) {
        if (clear) for (uint32_t y = 0; y < f->var.yres_virtual; y++) memset(f->mem + (size_t)y * f->fix.line_length, 0, (size_t)f->var.xres * 4);
        if (f->shown_yoff != f->orig_yoff && fb_pan(f, f->orig_yoff)) outf("{\"ev\":\"warning\",\"warning\":\"could not restore the original display offset %u\"}", f->orig_yoff);
        if (f->is_file) msync(f->map, f->map_len, MS_SYNC);
        munmap(f->map, f->map_len); f->map = NULL;
    }
    if (f->fd >= 0) close(f->fd); f->fd = -1;
}

/* ---------- controller mapping (a text file in the bundle, so codes can be corrected without rebuilding) ---------- */
typedef struct { char devices[MAX_DEV][128]; int ndev; signed char key[KEY_MAX_ + 1]; } cmap;   /* key[code] = button or -1 */
static const int DEFAULT_MAP[][2] = { { 304, B_A }, { 305, B_B }, { 314, B_SELECT }, { 315, B_START }, { 704, B_LEFT }, { 705, B_RIGHT }, { 706, B_UP }, { 707, B_DOWN } };

static void map_default(cmap *m) {
    memset(m, 0, sizeof *m); memset(m->key, -1, sizeof m->key); snprintf(m->devices[0], sizeof m->devices[0], "Nintendo Clovercon - controller1"); m->ndev = 1;
    for (size_t i = 0; i < sizeof DEFAULT_MAP / sizeof DEFAULT_MAP[0]; i++) m->key[DEFAULT_MAP[i][0]] = (signed char)DEFAULT_MAP[i][1];
}
/* format: '#' comments; "device <name>" (one per controller, up to 4); "key <code> <up|down|left|right|a|b|start|select>" */
static int map_load(cmap *m, const char *path, char *err, size_t n) {
    memset(m, 0, sizeof *m); memset(m->key, -1, sizeof m->key);
    FILE *fp = fopen(path, "r"); if (!fp) { snprintf(err, n, "cannot read the mapping file %s: %s", path, strerror(errno)); return -1; }
    char line[300]; int ln = 0, seen[B_COUNT] = { 0 };
    while (fgets(line, sizeof line, fp)) {
        ln++; char *s = line; while (*s == ' ' || *s == '\t') s++; size_t L = strlen(s); while (L && (s[L - 1] == '\n' || s[L - 1] == '\r' || s[L - 1] == ' ')) s[--L] = 0;
        if (!*s || *s == '#') continue;
        if (!strncmp(s, "device ", 7)) { if (m->ndev >= MAX_DEV || strlen(s + 7) >= sizeof m->devices[0]) { snprintf(err, n, "line %d: too many devices or name too long", ln); fclose(fp); return -1; } snprintf(m->devices[m->ndev++], sizeof m->devices[0], "%s", s + 7); continue; }
        int code; char bn[32], extra[8]; button b;
        if (sscanf(s, "key %d %31s %7s", &code, bn, extra) != 2) { snprintf(err, n, "line %d: expected \"key <code> <button>\" or \"device <name>\"", ln); fclose(fp); return -1; }
        if (code < 0 || code > KEY_MAX_) { snprintf(err, n, "line %d: key code %d is out of range 0..%d", ln, code, KEY_MAX_); fclose(fp); return -1; }
        if (!button_parse(bn, &b)) { snprintf(err, n, "line %d: unknown button %s", ln, bn); fclose(fp); return -1; }
        if (m->key[code] >= 0) { snprintf(err, n, "line %d: key code %d is mapped twice", ln, code); fclose(fp); return -1; }
        m->key[code] = (signed char)b; seen[b] = 1;
    }
    fclose(fp);
    if (!m->ndev) { snprintf(err, n, "no \"device\" line"); return -1; }
    for (int b = 0; b < B_COUNT; b++) if (!seen[b]) { snprintf(err, n, "button %s is not mapped", button_name((button)b)); return -1; }
    return 0;
}

/* ---------- evdev devices ---------- */
/* settle_until: mono ms until which this device's key events only update its button state (an "open-time settle"): the
 * clovercon driver re-initializes the controller when a program opens it (clovercon_open) and derives D-pad keys from the
 * analog stick bytes as well (clovercon_poll), so the first reports after an open can carry presses nobody made. A button
 * still down when the window ends counts as held: it gives no press until it has been released. */
typedef struct { int fd; char path[64], name[128]; uint8_t rem[24]; size_t nrem; int down[B_COUNT]; uint64_t settle_until; int settle_ignored; } edev;
#define INPUT_SETTLE_MS 400
static uint64_t g_settle_ms = INPUT_SETTLE_MS;
static uint64_t mono_ms_now(void);
typedef struct { edev d[MAX_DEV]; int n; const char *fixed; uint64_t next_scan_us; } inputs;

static int dev_name(int fd, char *name, size_t n) { memset(name, 0, n); return ioctl(fd, CCS_EVIOCGNAME(n - 1), name) < 0 ? -1 : 0; }
/* open every /dev/input/eventN whose registered name is one of the mapped controllers (never a fixed event number) */
static int inputs_scan(inputs *in, const cmap *m, int list_all) {
    for (int i = 0; i < 64 && in->n < MAX_DEV; i++) {
        char p[64]; snprintf(p, sizeof p, "/dev/input/event%d", i); int fd = open(p, O_RDONLY | O_NONBLOCK | O_CLOEXEC); if (fd < 0) continue;
        char name[128]; int ok = !dev_name(fd, name, sizeof name), match = 0;
        for (int k = 0; ok && k < m->ndev; k++) if (!strcmp(name, m->devices[k])) match = 1;
        for (int k = 0; k < in->n; k++) if (!strcmp(in->d[k].path, p)) match = 0;   /* already open */
        if (list_all) { char a[160], b[300]; jq(p, a, sizeof a); jq(ok ? name : "", b, sizeof b); outf("{\"ev\":\"input_device_seen\",\"path\":%s,\"name\":%s,\"mapped\":%s}", a, b, match ? "true" : "false"); }
        if (!match) { close(fd); continue; }
        edev *d = &in->d[in->n++]; memset(d, 0, sizeof *d); d->fd = fd; snprintf(d->path, sizeof d->path, "%s", p); snprintf(d->name, sizeof d->name, "%s", name); d->settle_until = mono_ms_now() + g_settle_ms;
        char a[160], b[300]; jq(p, a, sizeof a); jq(name, b, sizeof b); outf("{\"ev\":\"input_device\",\"path\":%s,\"name\":%s,\"event_size\":%zu}", a, b, (size_t)EVSIZE);
    }
    return in->n;
}
static int inputs_open_fixed(inputs *in, const char *path) {
    int fd = open(path, O_RDONLY | O_NONBLOCK | O_CLOEXEC); if (fd < 0) return -1;
    edev *d = &in->d[in->n++]; memset(d, 0, sizeof *d); d->fd = fd; snprintf(d->path, sizeof d->path, "%.63s", path); d->settle_until = mono_ms_now() + g_settle_ms;
    if (dev_name(fd, d->name, sizeof d->name)) snprintf(d->name, sizeof d->name, "(not an input device: test stream)");
    char a[160], b[300]; jq(d->path, a, sizeof a); jq(d->name, b, sizeof b); outf("{\"ev\":\"input_device\",\"path\":%s,\"name\":%s,\"event_size\":%zu}", a, b, (size_t)EVSIZE);
    return 0;
}

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
static int dev_read(edev *d, const cmap *m, void (*fn)(void *, edev *, int, int, int), void *ctx) {
    uint8_t buf[64 * 24];
    for (;;) {
        memcpy(buf, d->rem, d->nrem); ssize_t r = read(d->fd, buf + d->nrem, sizeof buf - d->nrem);
        if (r < 0) { if (errno == EAGAIN || errno == EINTR) return 0; return -1; }
        if (r == 0) { errno = 0; return -1; }       /* end of a test stream, or the device went away */
        size_t have = d->nrem + (size_t)r, off = 0;
        for (; off + EVSIZE <= have; off += EVSIZE) {
            uint16_t type, code; int32_t value; memcpy(&type, buf + off + 2 * sizeof(long), 2); memcpy(&code, buf + off + 2 * sizeof(long) + 2, 2); memcpy(&value, buf + off + 2 * sizeof(long) + 4, 4);
            fn(ctx, d, type, code, value);
        }
        d->nrem = have - off; memcpy(d->rem, buf + off, d->nrem);
    }
    (void)m;
}

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

static long rss_kb(void) { FILE *f = fopen("/proc/self/statm", "r"); if (!f) return -1; long a = 0, b = 0; if (fscanf(f, "%ld %ld", &a, &b) != 2) b = -1; fclose(f); return b < 0 ? -1 : b * (sysconf(_SC_PAGESIZE) / 1024); }

static int state_dir_check(const char *dir, int any_fs, char *err, size_t n, char *fsname, size_t fn) {
    if (!dir || !*dir) { snprintf(err, n, "--state-dir is required (H1 keeps the theme choice in RAM, e.g. /tmp/ccs/state); there is no default"); return -1; }
    if (dir[0] != '/' || strstr(dir, "/../") || strstr(dir, "/./") || (strlen(dir) >= 3 && !strcmp(dir + strlen(dir) - 3, "/.."))) { snprintf(err, n, "--state-dir must be an absolute path without . or .. components"); return -1; }
    static const char *deny[] = { "/var/lib", "/var/saves", "/newroot", "/etc", "/usr", "/bin", "/sbin", "/lib", "/media", "/mnt", "/boot" };
    for (size_t i = 0; i < sizeof deny / sizeof deny[0]; i++) { size_t L = strlen(deny[i]); if (!strncmp(dir, deny[i], L) && (dir[L] == 0 || dir[L] == '/')) { snprintf(err, n, "--state-dir %s is under %s, a persistent or system location", dir, deny[i]); return -1; } }
    int dfd = open(dir, O_RDONLY | O_DIRECTORY | O_CLOEXEC); if (dfd < 0) { snprintf(err, n, "--state-dir %s does not exist or is not a folder (the launcher creates it in RAM first)", dir); return -1; } close(dfd);
    if (access(dir, W_OK)) { snprintf(err, n, "--state-dir %s is not writable", dir); return -1; }
    struct statfs sf; if (statfs(dir, &sf)) { snprintf(err, n, "statfs(%s) failed: %s", dir, strerror(errno)); return -1; }
    unsigned long t = (unsigned long)sf.f_type; snprintf(fsname, fn, t == TMPFS_MAGIC_ ? "tmpfs" : t == RAMFS_MAGIC_ ? "ramfs" : "other (0x%lx)", t);
    if (t != TMPFS_MAGIC_ && t != RAMFS_MAGIC_ && !any_fs) { snprintf(err, n, "--state-dir %s is not on tmpfs or ramfs (filesystem 0x%lx): H1 must not write persistent storage", dir, t); return -1; }
    return 0;
}

static void usage(void) {
    fputs("usage: ccs-home-armv7 --device --pack DIR --state-dir DIR [--fb /dev/fb0] [--input auto|PATH] [--map FILE] [--fps N]\n"
          "         [--exit-hold-ms 3000] [--cover-budget N] [--watchdog-ms 15000] [--run-ms N] [--stats-ms 5000] [--input-settle-ms 400]\n"
          "       ccs-home-armv7 --inspect-input [--map FILE] [--input auto|PATH] [--run-ms N] [--input-settle-ms 400]\n"
          "       ccs-home-armv7 --inspect-fb [--fb /dev/fb0]\n"
          "test seams (host only): --fb-file PATH --fb-geometry SPEC, --test-allow-any-state-fs, --test-keep-frame, --test-dump-ppm PATH\n", stderr);
}

int device_main(int argc, char **argv) {
    const char *pack = NULL, *state = NULL, *fbpath = "/dev/fb0", *fbfile = NULL, *geom = NULL, *input = "auto", *mapf = NULL, *dump = NULL, *v;
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
        if (r.in.n > 0 && !fixed_file) poll(pf, (nfds_t)r.in.n, wait); else if (wait > 0) poll(NULL, 0, wait);
    }
    if (g_stop) { reason = g_signal == SIGTERM ? "SIGTERM" : g_signal == SIGINT ? "SIGINT" : g_signal == SIGHUP ? "SIGHUP" : "signal"; }
    if (dump) { uint32_t crc = 0; fb_write_ppm(&fr, dump, &crc); char d[400]; jq(dump, d, sizeof d); outf("{\"ev\":\"frame_dumped\",\"path\":%s,\"crc32\":\"%08x\",\"render_clock_ms\":%llu,\"shown_yoffset\":%u}", d, crc, (unsigned long long)rclock, f.shown_yoff); }
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
