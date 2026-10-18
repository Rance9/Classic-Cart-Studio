/* Native HOME: images, baked fonts, drawing primitives and the HOME screen (Arcade Grid + Game Details).
 *
 * Everything here is portable C11 on top of the core's software framebuffer (ccs_sim.h): the same code builds for the
 * Windows simulator, x86-64 Linux and 32-bit ARM. Rendering is integer or plain IEEE arithmetic only (no libm, built with
 * -ffp-contract=off) so a frame is bit-identical on every platform. The display hand-off (framebuffer -> screen) and the
 * controller event source are the platform adapter's job and stay outside this module.
 */
#ifndef CCS_HOME_H
#define CCS_HOME_H
#include "ccs_sim.h"

/* ---------- images (decoded with the vendored stb_image: baseline JPEG and PNG) ---------- */
typedef struct { int w, h, ch; uint8_t *px; } image;          /* ch 3 = RGB, 4 = RGBA (straight alpha) */
typedef struct { int x0, y0, x1, y1; } rect;                   /* half-open clip rectangle */
int ccs_read_file(const char *path, uint8_t **buf, size_t *len, size_t max_len);
int img_load(const char *path, image *im, int want_alpha, char *err, size_t errlen);
int img_decode(const uint8_t *buf, size_t len, image *im, int want_alpha, char *err, size_t errlen);
void img_free(image *im);
int img_alloc(image *im, int w, int h, int ch);
int img_scale(const image *src, image *dst, int dw, int dh);                  /* area average down, bilinear up; premultiplied alpha */
int img_scale_nearest(const image *src, image *dst, int dw, int dh);
int img_cover(const image *src, image *dst, int dw, int dh);                  /* CSS object-fit: cover (centered crop) */
int img_contain(const image *src, image *dst, int dw, int dh, int *ox, int *oy);   /* CSS object-fit: contain: the fitted image + offset */

/* ---------- drawing on the framebuffer (RGB); colors are 0xRRGGBB plus an alpha 0..255 ---------- */
typedef struct { uint32_t rgb; uint8_t a; } color;
#define RGBA(hex, alpha) ((color){ (uint32_t)(hex), (uint8_t)(alpha) })
rect rect_make(int x, int y, int w, int h);
rect rect_isect(rect a, rect b);
void gfx_fill(framebuffer *fb, rect clip, int x, int y, int w, int h, color c);
void gfx_image(framebuffer *fb, rect clip, const image *im, int x, int y, int opacity);
void gfx_rrect(framebuffer *fb, rect clip, int x, int y, int w, int h, int radius, color c);
void gfx_rrect_ring(framebuffer *fb, rect clip, int x, int y, int w, int h, int radius, int thickness, color c);
void gfx_rrect_image(framebuffer *fb, rect clip, const image *im, int x, int y, int radius);
void gfx_shadow(framebuffer *fb, rect clip, int x, int y, int w, int h, double sigma, color c);   /* blurred rectangle (CSS box-shadow) */
typedef struct { double pos; uint32_t rgb; double a; } stop;
void gfx_gradient(framebuffer *fb, rect clip, int x, int y, int w, int h, int vertical, const stop *s, int n);
typedef struct { int x, y, w, h; uint8_t *a; } mask;          /* 8-bit coverage in stage coordinates */
int mask_alloc(mask *m, int x, int y, int w, int h);
void mask_free(mask *m);
void mask_blur(mask *m, double sigma);
void gfx_mask(framebuffer *fb, rect clip, const mask *m, color c);

/* ---------- baked fonts (font/<name>.ccsf from the pack exporter) ---------- */
typedef struct { uint32_t cp; int32_t adv; int16_t x0, y0; uint16_t w, h; uint32_t off; } glyph;
typedef struct { int px, ascent, descent, gap; glyph *g; uint32_t n; uint8_t *bits; uint32_t nbits; } font;   /* the browser's normal line metrics */
int font_load(font *f, const char *path, char *err, size_t errlen);
void font_free(font *f);
uint32_t utf8_next(const char **s);
int text_w(const font *f, const char *s, size_t len, double spacing);   /* advance width in px (rounded), CSS letter-spacing included */
/* draws len bytes of s with the pen at x, baseline y; shear > 0 slants (synthetic italic); target is the framebuffer or a mask */
int text_draw(framebuffer *fb, mask *m, rect clip, const font *f, double x, int y, const char *s, size_t len, color c, double spacing, double shear);
size_t text_fit(const font *f, const char *s, int maxw, double spacing, char *out, size_t outlen);   /* ellipsis */
typedef struct { size_t start, len; } span;
size_t text_wrap(const font *f, const char *s, int maxw, double spacing, span *out, size_t max);

/* ---------- the HOME (four themes: arcade-grid, box-art-showcase, featured-shelf, adventure-map) ---------- */
typedef struct home home;
typedef void (*home_emit_fn)(const char *json_line);
home *home_load(const char *pack_dir, home_emit_fn emit, char *err, size_t errlen);
/* state_dir: the writable folder where the console keeps its HOME theme choice (home-prefs.json), separate from the package */
home *home_load2(const char *pack_dir, const char *state_dir, home_emit_fn emit, char *err, size_t errlen);
int home_set_theme(home *h, const char *theme_id, int save);   /* the controller's theme picker does this; tooling may too */
const char *home_theme(const home *h);
void home_free(home *h);
void home_input(home *h, button b, in_kind k, uint64_t clock_ms);
void home_render(home *h, framebuffer *fb, uint64_t clock_ms);
void home_select(home *h, int index);   /* tooling: select a listing index directly (tests); not reachable from the controller */
void home_layout(home *h, framebuffer *fb, uint64_t clock_ms);   /* renders and emits layout_rect events (overlap checks) */
void home_emit_state(home *h);
void home_go(home *h, const char *kind, int arg);   /* tooling: all | browse [parent] | folder <i> | category <i> | back, as the controller would (tests) */
size_t home_bytes(const home *h);
void home_set_cover_budget(home *h, int per_frame);   /* interactive adapters: bound decoding per frame (0 = complete frames) */
int home_covers_pending(const home *h);
size_t home_games(const home *h);
const char *home_focus_id(const home *h);
#endif
