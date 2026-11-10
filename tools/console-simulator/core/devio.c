/* devio.c: the NES Classic's devices (see devio.h), moved here from device.c unchanged in behavior so that the native HOME runtime and the
 * game player's console host use one implementation. Copyright (C) 2026 Rance9, GPL-3.0-or-later. */
#if defined(__linux__)
#define _GNU_SOURCE
#include "devio.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <errno.h>
#include <fcntl.h>
#include <unistd.h>
#include <limits.h>
#include <time.h>
#include <sys/ioctl.h>
#include <sys/mman.h>
#include <sys/statfs.h>
#include <sys/syscall.h>
#include <sound/asound.h>

#define TMPFS_MAGIC_ 0x01021994
#define RAMFS_MAGIC_ 0x858458f6
#define CCS_EVIOCGNAME(len) _IOC(_IOC_READ, 'E', 0x06, len)

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
uint64_t dio_mono_us(void) {
#if defined(CCS_CGT32)
    /* the classic 32-bit clock_gettime (Linux 2.6+): no dependence on musl's 64-bit-time fallback on the console's 3.4 kernel */
    static uint64_t last; long t32[2];
    if (syscall(CCS_CGT32, CLOCK_MONOTONIC, t32) == 0) last = (uint64_t)(unsigned long)t32[0] * 1000000u + (uint64_t)t32[1] / 1000u;
    return last;   /* (cannot fail for CLOCK_MONOTONIC; libc's clock_gettime is not linked in, so no 64-bit-time call is in the binary) */
#else
    struct timespec ts; clock_gettime(CLOCK_MONOTONIC, &ts); return (uint64_t)ts.tv_sec * 1000000u + (uint64_t)ts.tv_nsec / 1000u;
#endif
}

/* ---------- framebuffer: one seam, two back ends (the device via ioctl, or a test file with a declared mode) ---------- */
int dio_fb_validate(const dio_fb *f, char *err, size_t n) {
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

void dio_fb_describe(const dio_fb *f, char *o, size_t n) {
    const struct fb_var_screeninfo *v = &f->var;
    snprintf(o, n, "\"xres\":%u,\"yres\":%u,\"xres_virtual\":%u,\"yres_virtual\":%u,\"xoffset\":%u,\"yoffset\":%u,\"bits_per_pixel\":%u,\"line_length\":%u,\"smem_len\":%u,"
             "\"red\":[%u,%u],\"green\":[%u,%u],\"blue\":[%u,%u],\"transp\":[%u,%u],\"type\":%u,\"visual\":%u,\"ypanstep\":%u",
             v->xres, v->yres, v->xres_virtual, v->yres_virtual, v->xoffset, v->yoffset, v->bits_per_pixel, f->fix.line_length, f->fix.smem_len,
             v->red.offset, v->red.length, v->green.offset, v->green.length, v->blue.offset, v->blue.length, v->transp.offset, v->transp.length, f->fix.type, f->fix.visual, f->fix.ypanstep);
}

/* SPEC (tests): xres=1280,yres=720,vxres=1280,vyres=1440,bpp=32,line=5120,smem=7372800,r=16,g=8,b=0,a=-1,yoff=0,pan=1|0|failN */
static int fb_parse_geometry(dio_fb *f, const char *spec, char *err, size_t n) {
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

int dio_fb_open(dio_fb *f, const char *path, const char *test_geometry, int map_it, char *err, size_t n) {
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
    if (dio_fb_validate(f, err, n)) return -1;
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

int dio_fb_pan(dio_fb *f, uint32_t yoff) {
    if (f->is_file) {   /* emulated: the test declares whether panning works (and when it starts failing) */
        if (!f->pan_mode || (f->pan_fail_after >= 0 && f->pans >= f->pan_fail_after)) { errno = EINVAL; return -1; }
        f->pans++; f->shown_yoff = yoff; return 0;
    }
    struct fb_var_screeninfo v = f->var; v.xoffset = 0; v.yoffset = yoff;
    if (ioctl(f->fd, FBIOPAN_DISPLAY, &v)) return -1;
    f->pans++; f->shown_yoff = yoff; return 0;
}

uint32_t dio_fb_pixel(const dio_fb *f, uint8_t r, uint8_t g, uint8_t b) {
    const struct fb_var_screeninfo *v = &f->var; uint32_t alpha = v->transp.length == 8 ? (uint32_t)0xFF << v->transp.offset : 0;   /* opaque where the format has alpha */
    return ((uint32_t)r << v->red.offset) | ((uint32_t)g << v->green.offset) | ((uint32_t)b << v->blue.offset) | alpha; }

/* RGB24 -> the framebuffer's 32-bit pixels at page row y0, using the reported channel offsets; only the visible 1280x720
 * rectangle of that page is written (row padding and the other page are never touched) */
void dio_fb_convert(const dio_fb *f, const framebuffer *src, uint32_t y0) {
    const struct fb_var_screeninfo *v = &f->var; uint32_t ro = v->red.offset, go = v->green.offset, bo = v->blue.offset;
    uint32_t alpha = v->transp.length == 8 ? (uint32_t)0xFF << v->transp.offset : 0;   /* opaque where the format has alpha */
    for (int y = 0; y < src->h; y++) {
        uint32_t *d = (uint32_t *)(void *)(f->mem + (size_t)(y0 + (uint32_t)y) * f->fix.line_length); const uint8_t *s = src->rgb + (size_t)y * src->w * 3;
        for (int x = 0; x < src->w; x++, s += 3) d[x] = ((uint32_t)s[0] << ro) | ((uint32_t)s[1] << go) | ((uint32_t)s[2] << bo) | alpha;
    }
}

uint32_t dio_fb_back_row(const dio_fb *f) { return f->flip ? (f->shown_yoff == 0 ? f->var.yres : 0) : f->shown_yoff; }
int dio_fb_show(dio_fb *f, uint32_t y0, int *fell_back, char *why, size_t n) {
    if (!f->flip || y0 == f->shown_yoff) return 0;
    if (!dio_fb_pan(f, y0)) return 0;
    snprintf(why, n, "FBIOPAN_DISPLAY failed (%s): single-buffer drawing from now on", strerror(errno)); f->flip = 0; *fell_back = 1; return -1;
}

/* present one frame: into the hidden page then pan; if panning fails, fall back for good to drawing into the shown page */
/* K3D1-F3: a small change (the Arcade Grid's scrolling title) written into the page being shown, so it needs no flip; the hidden page is
 * rewritten whole by the next full picture, which then flips as before. Clipped to the picture */
void dio_fb_present_rect(dio_fb *f, const framebuffer *src, int x, int y, int w, int h) {
    if (x < 0) { w += x; x = 0; } if (y < 0) { h += y; y = 0; } if (x + w > src->w) w = src->w - x; if (y + h > src->h) h = src->h - y; if (w <= 0 || h <= 0) return;
    const struct fb_var_screeninfo *v = &f->var; uint32_t ro = v->red.offset, go = v->green.offset, bo = v->blue.offset;
    uint32_t alpha = v->transp.length == 8 ? (uint32_t)0xFF << v->transp.offset : 0;
    for (int yy = y; yy < y + h; yy++) {
        uint32_t *d = (uint32_t *)(void *)(f->mem + (size_t)(f->shown_yoff + (uint32_t)yy) * f->fix.line_length) + x; const uint8_t *s = src->rgb + ((size_t)yy * src->w + (size_t)x) * 3;
        for (int k = 0; k < w; k++, s += 3) d[k] = ((uint32_t)s[0] << ro) | ((uint32_t)s[1] << go) | ((uint32_t)s[2] << bo) | alpha;
    }
}
int dio_fb_present(dio_fb *f, const framebuffer *src, int *fell_back, char *why, size_t n) {
    if (f->flip) {
        uint32_t back = f->shown_yoff == 0 ? f->var.yres : 0; dio_fb_convert(f, src, back);
        if (!dio_fb_pan(f, back)) return 0;
        snprintf(why, n, "FBIOPAN_DISPLAY failed (%s): single-buffer drawing from now on", strerror(errno)); f->flip = 0; *fell_back = 1;
    }
    dio_fb_convert(f, src, f->shown_yoff); return 0;
}

void dio_fb_close(dio_fb *f, int clear, char *warn, size_t n) {
    if (warn && n) warn[0] = 0;
    if (f->map) {
        if (clear) for (uint32_t y = 0; y < f->var.yres_virtual; y++) memset(f->mem + (size_t)y * f->fix.line_length, 0, (size_t)f->var.xres * 4);
        if (f->shown_yoff != f->orig_yoff && dio_fb_pan(f, f->orig_yoff) && warn) snprintf(warn, n, "could not restore the original display offset %u", f->orig_yoff);
        if (f->is_file) msync(f->map, f->map_len, MS_SYNC);
        munmap(f->map, f->map_len); f->map = NULL;
    }
    if (f->fd >= 0) close(f->fd); f->fd = -1;
}

/* ---------- controller mapping (a text file, so codes can be corrected without rebuilding) ---------- */
static const int DEFAULT_MAP[][2] = { { 304, B_A }, { 305, B_B }, { 314, B_SELECT }, { 315, B_START }, { 704, B_LEFT }, { 705, B_RIGHT }, { 706, B_UP }, { 707, B_DOWN } };
void dio_map_default(dio_map *m) {
    memset(m, 0, sizeof *m); memset(m->key, -1, sizeof m->key); snprintf(m->devices[0], sizeof m->devices[0], "Nintendo Clovercon - controller1"); m->ndev = 1;
    for (size_t i = 0; i < sizeof DEFAULT_MAP / sizeof DEFAULT_MAP[0]; i++) m->key[DEFAULT_MAP[i][0]] = (signed char)DEFAULT_MAP[i][1];
}
/* format: '#' comments; "device <name>" (one per controller, up to 4); "key <code> <up|down|left|right|a|b|start|select>" */
int dio_map_load(dio_map *m, const char *path, char *err, size_t n) {
    memset(m, 0, sizeof *m); memset(m->key, -1, sizeof m->key);
    FILE *fp = fopen(path, "r"); if (!fp) { snprintf(err, n, "cannot read the mapping file %s: %s", path, strerror(errno)); return -1; }
    char line[300]; int ln = 0, seen[B_COUNT] = { 0 };
    while (fgets(line, sizeof line, fp)) {
        ln++; char *s = line; while (*s == ' ' || *s == '\t') s++; size_t L = strlen(s); while (L && (s[L - 1] == '\n' || s[L - 1] == '\r' || s[L - 1] == ' ')) s[--L] = 0;
        if (!*s || *s == '#') continue;
        if (!strncmp(s, "device ", 7)) { if (m->ndev >= DIO_MAX_DEV || strlen(s + 7) >= sizeof m->devices[0]) { snprintf(err, n, "line %d: too many devices or name too long", ln); fclose(fp); return -1; } snprintf(m->devices[m->ndev++], sizeof m->devices[0], "%s", s + 7); continue; }
        int code; char bn[32], extra[8]; button b;
        if (sscanf(s, "key %d %31s %7s", &code, bn, extra) != 2) { snprintf(err, n, "line %d: expected \"key <code> <button>\" or \"device <name>\"", ln); fclose(fp); return -1; }
        if (code < 0 || code > DIO_KEY_MAX) { snprintf(err, n, "line %d: key code %d is out of range 0..%d", ln, code, DIO_KEY_MAX); fclose(fp); return -1; }
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
static int dev_name(int fd, char *name, size_t n) { memset(name, 0, n); return ioctl(fd, CCS_EVIOCGNAME(n - 1), name) < 0 ? -1 : 0; }
/* open every /dev/input/eventN whose registered name is one of the mapped controllers (never a fixed event number) */
int dio_inputs_scan(dio_inputs *in, const dio_map *m, int list_all, uint64_t settle_ms, dio_seen_fn seen, void *ctx) {
    for (int i = 0; i < 64 && in->n < DIO_MAX_DEV; i++) {
        char p[64]; snprintf(p, sizeof p, "/dev/input/event%d", i); int fd = open(p, O_RDONLY | O_NONBLOCK | O_CLOEXEC); if (fd < 0) continue;
        char name[128]; int ok = !dev_name(fd, name, sizeof name), match = 0;
        for (int k = 0; ok && k < m->ndev; k++) if (!strcmp(name, m->devices[k])) match = 1;
        for (int k = 0; k < in->n; k++) if (!strcmp(in->d[k].path, p)) match = 0;   /* already open */
        if (list_all && seen) seen(ctx, 0, p, ok ? name : "", match);
        if (!match) { close(fd); continue; }
        dio_dev *d = &in->d[in->n++]; memset(d, 0, sizeof *d); d->fd = fd; snprintf(d->path, sizeof d->path, "%s", p); snprintf(d->name, sizeof d->name, "%s", name); d->settle_until = dio_mono_ms() + settle_ms;
        if (seen) seen(ctx, 1, p, name, 1);
    }
    return in->n;
}
int dio_inputs_open_fixed(dio_inputs *in, const char *path, uint64_t settle_ms, dio_seen_fn seen, void *ctx) {
    if (in->n >= DIO_MAX_DEV) return -1;
    int fd = open(path, O_RDONLY | O_NONBLOCK | O_CLOEXEC); if (fd < 0) return -1;
    dio_dev *d = &in->d[in->n++]; memset(d, 0, sizeof *d); d->fd = fd; snprintf(d->path, sizeof d->path, "%.63s", path); d->settle_until = dio_mono_ms() + settle_ms;
    if (dev_name(fd, d->name, sizeof d->name)) snprintf(d->name, sizeof d->name, "(not an input device: test stream)");
    if (seen) seen(ctx, 1, d->path, d->name, 1);
    return 0;
}
int dio_dev_read(dio_dev *d, void (*fn)(void *, dio_dev *, int, int, int), void *ctx) {
    uint8_t buf[64 * 24];
    for (;;) {
        memcpy(buf, d->rem, d->nrem); ssize_t r = read(d->fd, buf + d->nrem, sizeof buf - d->nrem);
        if (r < 0) { if (errno == EAGAIN || errno == EINTR) return 0; return -1; }
        if (r == 0) { errno = 0; return -1; }       /* end of a test stream, or the device went away */
        size_t have = d->nrem + (size_t)r, off = 0;
        for (; off + DIO_EVSIZE <= have; off += DIO_EVSIZE) {
            uint16_t type, code; int32_t value; memcpy(&type, buf + off + 2 * sizeof(long), 2); memcpy(&code, buf + off + 2 * sizeof(long) + 2, 2); memcpy(&value, buf + off + 2 * sizeof(long) + 4, 4);
            fn(ctx, d, type, code, value);
        }
        d->nrem = have - off; memcpy(d->rem, buf + off, d->nrem);
    }
}
void dio_dev_close(dio_inputs *in, int i) { if (i < 0 || i >= in->n) return; close(in->d[i].fd); in->d[i] = in->d[--in->n]; }

/* ---------- sound: the kernel's PCM interface ----------
 * The console's ALSA device hw:0,0 takes S16_LE, 2 channels, 48,000 Hz (its /etc/asound.conf). The static musl runtime cannot load the
 * console's glibc libasound, so this speaks the kernel's PCM interface directly (HW_PARAMS, SW_PARAMS, PREPARE, WRITEI_FRAMES, DELAY,
 * DROP). Non-blocking: a write that does not fit returns 0 and the caller tops the buffer up later. */
#define SND_CHUNK 1024
static struct snd_interval *snd_iv(struct snd_pcm_hw_params *p, int k) { return &p->intervals[k - SNDRV_PCM_HW_PARAM_FIRST_INTERVAL]; }
static void snd_mask1(struct snd_pcm_hw_params *p, int k, unsigned v) { struct snd_mask *m = &p->masks[k - SNDRV_PCM_HW_PARAM_FIRST_MASK]; memset(m, 0, sizeof *m); m->bits[v >> 5] |= 1u << (v & 31); }
static void snd_int1(struct snd_pcm_hw_params *p, int k, unsigned v) { struct snd_interval *i = snd_iv(p, k); i->min = i->max = v; i->openmin = i->openmax = 0; i->integer = 1; }
static void snd_any(struct snd_pcm_hw_params *p) {
    memset(p, 0, sizeof *p);
    for (int k = SNDRV_PCM_HW_PARAM_FIRST_MASK; k <= SNDRV_PCM_HW_PARAM_LAST_MASK; k++) memset(&p->masks[k - SNDRV_PCM_HW_PARAM_FIRST_MASK], 0xff, sizeof(struct snd_mask));
    for (int k = SNDRV_PCM_HW_PARAM_FIRST_INTERVAL; k <= SNDRV_PCM_HW_PARAM_LAST_INTERVAL; k++) { struct snd_interval *i = snd_iv(p, k); i->min = 0; i->max = ~0u; }
    p->rmask = ~0u; p->cmask = 0; p->info = ~0u;
}
int dio_pcm_open(dio_pcm *s, const char *dev, unsigned rate, char *why, size_t n) {
    memset(s, 0, sizeof *s); s->fd = -1; s->rate = rate; snprintf(s->dev, sizeof s->dev, "%s", dev);
    s->fd = open(s->dev, O_RDWR | O_NONBLOCK | O_CLOEXEC); if (s->fd < 0) { snprintf(why, n, "cannot open %s: %s", s->dev, strerror(errno)); return -1; }
    static const unsigned PERIODS[3] = { 16, 8, 4 }; int ok = 0;   /* about 340 ms of sound at 48 kHz, else less */
    for (int t = 0; t < 3 && !ok; t++) {
        struct snd_pcm_hw_params p; snd_any(&p);
        snd_mask1(&p, SNDRV_PCM_HW_PARAM_ACCESS, SNDRV_PCM_ACCESS_RW_INTERLEAVED); snd_mask1(&p, SNDRV_PCM_HW_PARAM_FORMAT, SNDRV_PCM_FORMAT_S16_LE); snd_mask1(&p, SNDRV_PCM_HW_PARAM_SUBFORMAT, SNDRV_PCM_SUBFORMAT_STD);
        snd_int1(&p, SNDRV_PCM_HW_PARAM_SAMPLE_BITS, 16); snd_int1(&p, SNDRV_PCM_HW_PARAM_FRAME_BITS, 32); snd_int1(&p, SNDRV_PCM_HW_PARAM_CHANNELS, 2); snd_int1(&p, SNDRV_PCM_HW_PARAM_RATE, rate);
        snd_int1(&p, SNDRV_PCM_HW_PARAM_PERIOD_SIZE, SND_CHUNK); snd_int1(&p, SNDRV_PCM_HW_PARAM_PERIODS, PERIODS[t]);
        if (!ioctl(s->fd, (int)SNDRV_PCM_IOCTL_HW_PARAMS, &p)) { s->period = snd_iv(&p, SNDRV_PCM_HW_PARAM_PERIOD_SIZE)->min; s->buffer = snd_iv(&p, SNDRV_PCM_HW_PARAM_BUFFER_SIZE)->min; ok = 1; }
    }
    if (!ok) { snprintf(why, n, "the sound device refused 16-bit stereo at %u Hz: %s", rate, strerror(errno)); close(s->fd); s->fd = -1; return -1; }
    struct snd_pcm_sw_params w; memset(&w, 0, sizeof w); w.tstamp_mode = SNDRV_PCM_TSTAMP_NONE; w.period_step = 1; w.avail_min = s->period;
    w.start_threshold = s->period * 2; w.stop_threshold = s->buffer; unsigned long b = s->buffer; while (b * 2 <= (unsigned long)INT_MAX - s->buffer) b *= 2; w.boundary = b;
    if (ioctl(s->fd, (int)SNDRV_PCM_IOCTL_SW_PARAMS, &w) || ioctl(s->fd, (int)SNDRV_PCM_IOCTL_PREPARE)) { snprintf(why, n, "the sound device could not be prepared: %s", strerror(errno)); close(s->fd); s->fd = -1; return -1; }
    return 0;
}
int dio_pcm_write(dio_pcm *s, const int16_t *b, uint32_t frames) {
    struct snd_xferi x; memset(&x, 0, sizeof x); x.buf = (void *)b; x.frames = frames;
    if (!ioctl(s->fd, (int)SNDRV_PCM_IOCTL_WRITEI_FRAMES, &x)) return (int)x.result;
    if (errno == EAGAIN) return 0;
    if (errno == EPIPE || errno == ESTRPIPE) { s->underruns++; if (!ioctl(s->fd, (int)SNDRV_PCM_IOCTL_PREPARE)) return 0; }   /* a slow frame emptied the buffer: start again */
    return -1;
}
long dio_pcm_delay(dio_pcm *s) { snd_pcm_sframes_t d = 0; if (s->fd < 0 || ioctl(s->fd, (int)SNDRV_PCM_IOCTL_DELAY, &d) || d < 0) return 0; return (long)d; }
void dio_pcm_drop(dio_pcm *s) { if (s->fd >= 0) ioctl(s->fd, (int)SNDRV_PCM_IOCTL_DROP); }
int dio_pcm_prepare(dio_pcm *s) { return s->fd >= 0 ? ioctl(s->fd, (int)SNDRV_PCM_IOCTL_PREPARE) : -1; }
void dio_pcm_close(dio_pcm *s) { if (s->fd >= 0) close(s->fd); s->fd = -1; }

/* ---------- RAM folders ---------- */
int dio_ram_dir(const char *dir, int any_fs, char *err, size_t n, char *fsname, size_t fn) {
    if (!dir || !*dir) { snprintf(err, n, "no folder given (a RAM-only session keeps everything in RAM, e.g. /tmp/ccs/state); there is no default"); return -1; }
    if (dir[0] != '/' || strstr(dir, "/../") || strstr(dir, "/./") || (strlen(dir) >= 3 && !strcmp(dir + strlen(dir) - 3, "/.."))) { snprintf(err, n, "%s must be an absolute path without . or .. components", dir); return -1; }
    static const char *deny[] = { "/var/lib", "/var/saves", "/newroot", "/etc", "/usr", "/bin", "/sbin", "/lib", "/media", "/mnt", "/boot" };
    for (size_t i = 0; i < sizeof deny / sizeof deny[0]; i++) { size_t L = strlen(deny[i]); if (!strncmp(dir, deny[i], L) && (dir[L] == 0 || dir[L] == '/')) { snprintf(err, n, "%s is under %s, a persistent or system location", dir, deny[i]); return -1; } }
    int dfd = open(dir, O_RDONLY | O_DIRECTORY | O_CLOEXEC); if (dfd < 0) { snprintf(err, n, "%s does not exist or is not a folder (the launcher creates it in RAM first)", dir); return -1; } close(dfd);
    if (access(dir, W_OK)) { snprintf(err, n, "%s is not writable", dir); return -1; }
    struct statfs sf; if (statfs(dir, &sf)) { snprintf(err, n, "statfs(%s) failed: %s", dir, strerror(errno)); return -1; }
    unsigned long t = (unsigned long)sf.f_type; snprintf(fsname, fn, t == TMPFS_MAGIC_ ? "tmpfs" : t == RAMFS_MAGIC_ ? "ramfs" : "other (0x%lx)", t);
    if (t != TMPFS_MAGIC_ && t != RAMFS_MAGIC_ && !any_fs) { snprintf(err, n, "%s is not on tmpfs or ramfs (filesystem 0x%lx): a RAM-only session must not write persistent storage", dir, t); return -1; }
    return 0;
}
long dio_rss_kb(void) { FILE *f = fopen("/proc/self/statm", "r"); if (!f) return -1; long a = 0, b = 0; if (fscanf(f, "%ld %ld", &a, &b) != 2) b = -1; fclose(f); return b < 0 ? -1 : b * (sysconf(_SC_PAGESIZE) / 1024); }
#endif
