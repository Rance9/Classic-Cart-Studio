/* devio.h: the NES Classic's devices, shared by the native HOME runtime (device.c, ccs-home-armv7) and the game player's console host
 * (tools/player/core/host_dev.c, ccs-player-armv7): the monotonic clock, the 1280 x 720 framebuffer, the controllers through evdev, the
 * sound through the kernel's PCM interface, and the check that a folder is in RAM. Linux only (the console runs Linux 3.4).
 *
 * The console's kernel is older than musl 1.2's 64-bit-time and statx system calls, and on ARM an unknown system call stops the program
 * (SIGILL) instead of failing: so the clock is the classic 32-bit clock_gettime itself, and nothing here calls stat()/fstat(). */
#ifndef CCS_DEVIO_H
#define CCS_DEVIO_H
#if defined(__linux__)
#include "ccs_sim.h"
#include <stdint.h>
#include <stddef.h>
#include <linux/fb.h>

#define DIO_MAX_DEV 4
#define DIO_KEY_MAX 0x2ff
#define DIO_EV_SYN 0
#define DIO_EV_KEY 1
#define DIO_SYN_DROPPED 3
#define DIO_EVSIZE (2 * sizeof(long) + 8)       /* struct input_event in the kernel's ABI: 16 bytes on ARMv7, 24 on x86-64 */

uint64_t dio_mono_us(void);
static inline uint64_t dio_mono_ms(void) { return dio_mono_us() / 1000u; }

/* ---- framebuffer: the device via ioctl, or (tests) a file with a declared mode ---- */
typedef struct {
    int fd, is_file, pan_mode, pan_fail_after, pans; uint8_t *map, *mem; size_t map_len;
    struct fb_var_screeninfo var; struct fb_fix_screeninfo fix;
    uint32_t orig_yoff, shown_yoff; int pages, flip;        /* flip: page flipping in use (else single-buffer drawing) */
    char path[256];
} dio_fb;
int dio_fb_open(dio_fb *f, const char *path, const char *test_geometry, int map_it, char *err, size_t n);
int dio_fb_validate(const dio_fb *f, char *err, size_t n);
void dio_fb_describe(const dio_fb *f, char *o, size_t n);
int dio_fb_pan(dio_fb *f, uint32_t yoff);
void dio_fb_convert(const dio_fb *f, const framebuffer *src, uint32_t y0);   /* RGB24 1280 x 720 -> the page at row y0 */
int dio_fb_present(dio_fb *f, const framebuffer *src, int *fell_back, char *why, size_t n);
void dio_fb_present_rect(dio_fb *f, const framebuffer *src, int x, int y, int w, int h);   /* only that rectangle, into the page on the screen (no flip) */
uint32_t dio_fb_back_row(const dio_fb *f);                   /* the page row to draw the next frame into (the hidden page when flipping) */
int dio_fb_show(dio_fb *f, uint32_t y0, int *fell_back, char *why, size_t n);   /* make that page visible (pan), or keep drawing in place */
uint32_t dio_fb_pixel(const dio_fb *f, uint8_t r, uint8_t g, uint8_t b);         /* one pixel in the framebuffer's own format */
void dio_fb_close(dio_fb *f, int clear, char *warn, size_t n);

/* ---- controllers: a mapping file and evdev devices found by name ---- */
typedef struct { char devices[DIO_MAX_DEV][128]; int ndev; signed char key[DIO_KEY_MAX + 1]; } dio_map;   /* key[code] = button or -1 */
void dio_map_default(dio_map *m);
int dio_map_load(dio_map *m, const char *path, char *err, size_t n);
typedef struct { int fd; char path[64], name[128]; uint8_t rem[24]; size_t nrem; int down[B_COUNT]; uint64_t settle_until; int settle_ignored; } dio_dev;
typedef struct { dio_dev d[DIO_MAX_DEV]; int n; const char *fixed; uint64_t next_scan_us; } dio_inputs;
/* what a scan saw: kind 0 a device seen (list_all), 1 a device opened; ctx is the caller's */
typedef void (*dio_seen_fn)(void *ctx, int kind, const char *path, const char *name, int mapped);
int dio_inputs_scan(dio_inputs *in, const dio_map *m, int list_all, uint64_t settle_ms, dio_seen_fn seen, void *ctx);
int dio_inputs_open_fixed(dio_inputs *in, const char *path, uint64_t settle_ms, dio_seen_fn seen, void *ctx);
/* whatever is available, whole records only (partial ones wait); -1 when the device is gone */
int dio_dev_read(dio_dev *d, void (*fn)(void *, dio_dev *, int, int, int), void *ctx);
void dio_dev_close(dio_inputs *in, int i);   /* closed and removed (the last one takes its place) */

/* ---- sound: one playback device through the kernel's PCM interface (<sound/asound.h>), 16-bit stereo ---- */
typedef struct { int fd; unsigned rate; unsigned long buffer, period; uint32_t underruns; char dev[300]; } dio_pcm;
int dio_pcm_open(dio_pcm *p, const char *dev, unsigned rate, char *why, size_t n);
int dio_pcm_write(dio_pcm *p, const int16_t *b, uint32_t frames);   /* frames accepted (0: the buffer is full), -1 on a lasting error */
long dio_pcm_delay(dio_pcm *p);                                     /* frames written but not yet heard (0 when unknown) */
void dio_pcm_drop(dio_pcm *p);                                      /* silent at once */
int dio_pcm_prepare(dio_pcm *p);
void dio_pcm_close(dio_pcm *p);

/* ---- RAM folders ---- */
/* a folder for writing in a RAM-only session: an absolute path without . or .. parts, outside the console's persistent and system places,
   existing, writable and on tmpfs/ramfs (statfs follows a link to where it really is). any_fs: the RAM test is skipped (host tests only) */
int dio_ram_dir(const char *dir, int any_fs, char *err, size_t n, char *fsname, size_t fn);
long dio_rss_kb(void);
#endif
#endif
