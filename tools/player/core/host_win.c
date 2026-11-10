/* host_win.c: the player's Windows host. One window (resizable, borderless full screen with Alt+Enter or F11, the size and mode
 * remembered), crisp nearest-neighbor scaling with the NES's TV proportions (8:7 pixels) or square pixels and optional whole-number
 * scaling, waveOut sound (the session paces itself by the device clock), the keyboard (bindings from the Studio's settings), Xbox-
 * compatible controllers through XInput and other USB/Bluetooth game controllers through DirectInput (with a button setup the player
 * remembers per controller). Controllers join Players 1 and 2 in the order they connect (the Studio's settings can swap them); unplugging
 * one releases its buttons. The keyboard plays the player no controller plays (gp_keyboard_players). One player at a time (a named
 * mutex). Closing the window asks the session, never exits on its own.
 * EMBEDDED (D-104, F22): started for the Studio's Preview on TV, the player draws into the Studio instead of its window. The Studio window
 * serves a named pipe for this session (its name comes in the session file) and creates a shared frame buffer (WebView2's shared memory,
 * its handle duplicated into this process); each finished picture goes there (RGBA, two slots, a sequence number) and the page draws it in
 * the TV area. The page's keys, mouse (already in stage pixels), mock-pad buttons and input ownership come as lines on the pipe; the
 * controllers are read here as always, counting only while the Preview owns input. Pop Out shows this window (the same session, nothing
 * reloaded); Pop In hides it again. A broken pipe or a Studio process that is gone is a parent that went away: the session ends as when
 * the Studio closes it (the game's own save written, no slot written). */
#define WIN32_LEAN_AND_MEAN
#define DIRECTINPUT_VERSION 0x0800
#define COBJMACROS
#include <windows.h>
#include <mmsystem.h>
#include <xinput.h>
#include <dinput.h>
#include "host.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdarg.h>

#define NBUF 16
#define BUF_FRAMES 512
#define MAXDEV 8
typedef DWORD (WINAPI *xget_fn)(DWORD, XINPUT_STATE *);
typedef struct { int kind; int xi; LPDIRECTINPUTDEVICE8W dev; GUID guid; wchar_t name[128]; int map[GA_COUNT]; int connected; uint8_t btn[32]; uint8_t prevbtn[32]; uint8_t act[GA_COUNT];
                 int uid; gp_raw raw; char rawsig[96]; uint8_t pact[GA_COUNT]; int hit, configured; int vid, pid; const char *known; } pad_dev;   /* hit: a button went down in the last read (its action + 1, or 9) */   /* kind 1 XInput, 2 DirectInput (raw: its state for gp_pads.c; uid: stable while connected) */
#define SHM_HDR 4096
#define SHM_SLOT (2560 * 1440 * 4)
struct host {
    HANDLE pipe; int embed, popped, own, studio_exit, preview_close, music_cmd; char line[2048]; int nline; uint8_t *shm; size_t shm_size; uint32_t seq, slot; uint64_t last_hash; int vpad[GA_COUNT];
    char theme_cmd[48], select_cmd[200], reload_cmd[1100]; HANDLE studio_proc; uint64_t studio_check;   /* embedded: see the comment at the top */
    HWND hwnd; HANDLE mutex; int fs; WINDOWPLACEMENT wp; int cw, ch; FILE *trace; char display_path[1024], pads_path[1100]; int studio_pid;
    const gp_settings *s; int tv_aspect, integer_scale, swap; uint8_t keys[256], tapped[256]; int vk[GP_PLAYERS][GA_COUNT][2];
    int close_req, menu_key, lost, gained, focused;
    uint32_t *last; int lw, lh, lstride, last_kind; uint32_t nes32[256 * 240]; uint32_t *canvas32; int cvw, cvh;
    uint32_t *down32; int dnw, dnh, dn_gen, cv_gen;   /* the canvas reduced to the window (dnw x dnh) for canvas generation dn_gen */
    HWAVEOUT wo; WAVEHDR hdr[NBUF]; int16_t *pcm[NBUF]; int16_t stage[BUF_FRAMES * 2]; size_t staged; int audio_on, audio_paused;
    HMODULE xdll; xget_fn xget; LPDIRECTINPUT8W di; pad_dev dev[MAXDEV]; int ndev; uint64_t last_enum, xi_next[4]; int next_uid, posted_input, enum_due, dpi, ncw, nch, uw, uh, auto_size, fit_w, fit_h, sm_on, sm_dpi, sm_w, sm_h, sm_sized;   /* uw x uh: the chosen size in logical units */
    uint64_t pf_t0; double pf_msgs, pf_enum, pf_pads, pf_blit; int pf_polls, pf_blits;   /* time per second, with a trace */
    host_pointer pq[16]; int npq, ldown, dbl, wheel_acc, cursor_hidden;   /* the mouse: events in stage pixels until the next poll */
    POINT last_scr; int have_scr;                                          /* F3: where the mouse last really was on the screen */
    int text_on, ntext; char text[32];                                     /* naming a slot: typed characters */
    char pick_key[GP_PLAYERS][64]; char assign_path[1100];                 /* the chosen controller per player ("" automatic, "none" the keyboard), by device */
};
static host *G;
void host_trace(host *H, const char *fmt, ...) { if (!H || !H->trace) return; char b[2048]; va_list a; va_start(a, fmt); vsnprintf(b, sizeof b, fmt, a); va_end(a);
    fprintf(H->trace, "{\"t\":%llu,%s\n", (unsigned long long)gp_ms(), b[0] == '{' ? b + 1 : b); fflush(H->trace); }
uint64_t host_now(host *H) { (void)H; return gp_ms(); }

/* ---- keys by name (the Studio's settings use these names) ---- */
static const struct { const char *n; int vk; } KN[] = { { "Up", VK_UP }, { "Down", VK_DOWN }, { "Left", VK_LEFT }, { "Right", VK_RIGHT }, { "Enter", VK_RETURN },
    { "Backspace", VK_BACK }, { "Space", VK_SPACE }, { "Tab", VK_TAB }, { "Shift", VK_SHIFT }, { "LShift", VK_LSHIFT }, { "RShift", VK_RSHIFT }, { "Ctrl", VK_CONTROL },
    { "LCtrl", VK_LCONTROL }, { "RCtrl", VK_RCONTROL }, { "Home", VK_HOME }, { "End", VK_END }, { "PageUp", VK_PRIOR }, { "PageDown", VK_NEXT }, { "Insert", VK_INSERT },
    { "Delete", VK_DELETE }, { "Comma", VK_OEM_COMMA }, { "Period", VK_OEM_PERIOD }, { "Slash", VK_OEM_2 }, { "Semicolon", VK_OEM_1 }, { "Quote", VK_OEM_7 },
    { "LeftBracket", VK_OEM_4 }, { "RightBracket", VK_OEM_6 }, { "Minus", VK_OEM_MINUS }, { "Equals", VK_OEM_PLUS }, { "Backslash", VK_OEM_5 }, { "Backquote", VK_OEM_3 } };
static int vk_of(const char *n) {
    if (!n || !*n) return 0;
    if (strlen(n) == 1 && ((n[0] >= 'A' && n[0] <= 'Z') || (n[0] >= '0' && n[0] <= '9'))) return n[0];
    if (strlen(n) == 1 && n[0] >= 'a' && n[0] <= 'z') return n[0] - 32;
    if ((n[0] == 'F' || n[0] == 'f') && n[1] >= '1' && n[1] <= '9') { int k = atoi(n + 1); if (k >= 1 && k <= 12) return VK_F1 + k - 1; }
    if (!strncmp(n, "Numpad", 6) && n[6] >= '0' && n[6] <= '9' && !n[7]) return VK_NUMPAD0 + n[6] - '0';
    for (size_t i = 0; i < sizeof KN / sizeof KN[0]; i++) if (!_stricmp(n, KN[i].n)) return KN[i].vk;
    return 0; }
static void bind_keys(host *H) {
    for (int p = 0; p < GP_PLAYERS; p++) for (int a = 0; a < GA_COUNT; a++) { char t[24]; snprintf(t, sizeof t, "%s", H->s->key[p][a]); char *bar = strchr(t, '|');
        if (bar) { *bar = 0; H->vk[p][a][1] = vk_of(bar + 1); } else H->vk[p][a][1] = 0; H->vk[p][a][0] = vk_of(t);
        if (H->vk[p][a][0] == VK_ESCAPE || H->vk[p][a][1] == VK_ESCAPE) H->vk[p][a][0] = H->vk[p][a][1] = 0; } }   /* Esc is the menu key */

/* ---- display ---- */
static void dest_rect(host *H, int w, int h, double par, RECT *r) {
    int cw = H->cw > 0 ? H->cw : 1, ch = H->ch > 0 ? H->ch : 1; double sw = w * par, sh = h; int dw, dh;
    if (H->integer_scale && par > 0) { int k = (int)(ch / sh); if ((int)(k * sw) > cw) k = (int)(cw / sw); if (k < 1) k = 1; dw = (int)(sw * k + 0.5); dh = (int)(sh * k); if (dw > cw || dh > ch) { dw = cw; dh = ch; } }
    else { double s = (double)cw / sw < (double)ch / sh ? (double)cw / sw : (double)ch / sh; dw = (int)(sw * s + 0.5); dh = (int)(sh * s + 0.5); }
    r->left = (cw - dw) / 2; r->top = (ch - dh) / 2; r->right = r->left + dw; r->bottom = r->top + dh; }
static void blit_dc(host *H, HDC dc);
static void cap_to_work(HMONITOR mon, int *cw, int *ch) {   /* never larger than 90% of the display's work area, proportions kept */
    MONITORINFO mi = { sizeof mi, { 0 }, { 0 }, 0 }; if (!GetMonitorInfoW(mon, &mi)) return; int aw = (mi.rcWork.right - mi.rcWork.left) * 9 / 10, ah = (mi.rcWork.bottom - mi.rcWork.top) * 9 / 10;
    if (*cw > aw || *ch > ah) { double k = (double)aw / *cw < (double)ah / *ch ? (double)aw / *cw : (double)ah / *ch; *cw = (int)(*cw * k); *ch = (int)(*ch * k); } }
static void fit_client(HWND w, int tw, int th) {   /* resize so the CLIENT is exactly tw x th, measured, whatever the frame's size */
    for (int k = 0; k < 3; k++) { RECT cr, wr; GetClientRect(w, &cr); GetWindowRect(w, &wr); int dw = tw - cr.right, dh = th - cr.bottom; if (!dw && !dh) return;
        SetWindowPos(w, NULL, 0, 0, (wr.right - wr.left) + dw, (wr.bottom - wr.top) + dh, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE); } }
static double qms(void) { LARGE_INTEGER f, c; QueryPerformanceFrequency(&f); QueryPerformanceCounter(&c); return (double)c.QuadPart * 1000.0 / (double)f.QuadPart; }
static void blit(host *H) { if (!H->last || !H->hwnd) return; double t = H->trace ? qms() : 0; HDC dc = GetDC(H->hwnd); blit_dc(H, dc); ReleaseDC(H->hwnd, dc);
    if (H->trace) { H->pf_blit += qms() - t; H->pf_blits++; } }
/* resampling weights for n source -> m destination pixels: area average when reducing, bilinear when enlarging (fixed point, sum 4096) */
typedef struct { int first, n; int w[16]; } rspan;
static rspan *rweights(int n, int m) {
    rspan *ws = calloc((size_t)m, sizeof *ws); if (!ws) return NULL; double sc = (double)n / m;
    for (int i = 0; i < m; i++) { double acc[16]; int first, k = 0; double tot = 0;
        if (sc >= 1.0) { double a = i * sc, b = (i + 1) * sc; first = (int)a; int last = (int)(b - 1e-9); if (last >= n) last = n - 1;
            for (int j = first; j <= last && k < 16; j++) { double lo = j > a ? j : a, hi = j + 1 < b ? j + 1 : b; acc[k++] = hi - lo; tot += hi - lo; } }
        else { double c = (i + 0.5) * sc - 0.5; int j0 = c < 0 ? -1 : (int)c; double t = c - j0; if (j0 < 0) { j0 = 0; t = 0; } if (j0 >= n - 1) { j0 = n - 1; t = 0; }
            first = j0; acc[k++] = 1 - t; acc[k++] = t; tot = 1; if (first + 1 >= n) k = 1; }
        int sum = 0, best = 0; ws[i].first = first; ws[i].n = k;
        for (int q = 0; q < k; q++) { ws[i].w[q] = (int)(acc[q] / tot * 4096 + 0.5); sum += ws[i].w[q]; if (ws[i].w[q] > ws[i].w[best]) best = q; }
        ws[i].w[best] += 4096 - sum; }
    return ws; }
static void resample(const uint32_t *src, int sw, int sh, uint32_t *dst, int dw, int dh) {   /* separable: rows into tmp, then columns */
    static rspan *wx, *wy; static int kx[2], ky[2]; static uint32_t *tmp; static size_t tmpn;
    if (!wx || kx[0] != sw || kx[1] != dw) { free(wx); wx = rweights(sw, dw); kx[0] = sw; kx[1] = dw; }
    if (!wy || ky[0] != sh || ky[1] != dh) { free(wy); wy = rweights(sh, dh); ky[0] = sh; ky[1] = dh; }
    if (tmpn < (size_t)dw * sh) { free(tmp); tmp = malloc((size_t)dw * sh * 4); tmpn = tmp ? (size_t)dw * sh : 0; }
    if (!wx || !wy || !tmp) return;
    for (int y = 0; y < sh; y++) { const uint32_t *r = src + (size_t)y * sw; uint32_t *o = tmp + (size_t)y * dw;
        for (int x = 0; x < dw; x++) { const rspan *w = &wx[x]; uint32_t R = 0, G = 0, B = 0;
            for (int q = 0; q < w->n; q++) { uint32_t v = r[w->first + q]; int k = w->w[q]; R += (v >> 16 & 255) * k; G += (v >> 8 & 255) * k; B += (v & 255) * k; }
            o[x] = (R + 2048) >> 12 << 16 | (G + 2048) >> 12 << 8 | (B + 2048) >> 12; } }
    for (int y = 0; y < dh; y++) { const rspan *w = &wy[y]; uint32_t *o = dst + (size_t)y * dw;
        for (int x = 0; x < dw; x++) { uint32_t R = 0, G = 0, B = 0;
            for (int q = 0; q < w->n; q++) { uint32_t v = tmp[(size_t)(w->first + q) * dw + x]; int k = w->w[q]; R += (v >> 16 & 255) * k; G += (v >> 8 & 255) * k; B += (v & 255) * k; }
            o[x] = (R + 2048) >> 12 << 16 | (G + 2048) >> 12 << 8 | (B + 2048) >> 12; } } }
static void blit_dc(host *H, HDC dc) {
    if (!H->last) return; RECT d; int cs = H->last_kind == 2 && ccs_scale > 1 ? ccs_scale : 1;   /* a scaled canvas is placed as its 1280 x 720 stage */
    dest_rect(H, H->lw / cs, H->lh / cs, H->last_kind == 1 ? (H->tv_aspect ? 8.0 / 7.0 : 1.0) : 1.0, &d);
    BITMAPINFO bi; memset(&bi, 0, sizeof bi); bi.bmiHeader.biSize = sizeof bi.bmiHeader; bi.bmiHeader.biWidth = H->lstride; bi.bmiHeader.biHeight = -H->lh; bi.bmiHeader.biPlanes = 1; bi.bmiHeader.biBitCount = 32; bi.bmiHeader.biCompression = BI_RGB;
    SetStretchBltMode(dc, COLORONCOLOR);
    int tw = d.right - d.left, th = d.bottom - d.top;
    if (H->last_kind == 2 && tw > 0 && th > 0 && (tw != H->lw || th != H->lh)) {   /* the canvas resampled to the picture's exact size, shown 1:1 */
        if (!H->down32 || H->dnw != tw || H->dnh != th || H->dn_gen != H->cv_gen) {
            if (!H->down32 || H->dnw * H->dnh < tw * th) { free(H->down32); H->down32 = malloc((size_t)tw * th * 4); }
            if (H->down32) { resample(H->last, H->lw, H->lh, H->down32, tw, th); H->dnw = tw; H->dnh = th; H->dn_gen = H->cv_gen; } }
        if (H->down32) { bi.bmiHeader.biWidth = tw; bi.bmiHeader.biHeight = -th; StretchDIBits(dc, d.left, d.top, tw, th, 0, 0, tw, th, H->down32, &bi, DIB_RGB_COLORS, SRCCOPY); }
    } else StretchDIBits(dc, d.left, d.top, d.right - d.left, d.bottom - d.top, 0, 0, H->lw, H->lh, H->last, &bi, DIB_RGB_COLORS, SRCCOPY);
    HBRUSH k = (HBRUSH)GetStockObject(BLACK_BRUSH); RECT b;
    if (d.top > 0) { SetRect(&b, 0, 0, H->cw, d.top); FillRect(dc, &b, k); } if (d.bottom < H->ch) { SetRect(&b, 0, d.bottom, H->cw, H->ch); FillRect(dc, &b, k); }
    if (d.left > 0) { SetRect(&b, 0, d.top, d.left, d.bottom); FillRect(dc, &b, k); } if (d.right < H->cw) { SetRect(&b, d.right, d.top, H->cw, d.bottom); FillRect(dc, &b, k); } }
/* the shared frame buffer: header (magic "CCSF", seq, the slot of the newest picture, per slot w/h/kind, view, popped, picture shape)
   then two RGBA slots; the page reads the newest slot when seq changes. A canvas that did not change is not published again */
static void shm_hdr(host *H, uint32_t off, uint32_t v) { if (H->shm) *(volatile uint32_t *)(H->shm + off) = v; }
static int shm_on(host *H) { return H->embed && !H->popped && H->shm; }
static void shm_publish(host *H, int kind, int w, int h) {
    uint32_t s = H->slot; shm_hdr(H, 16 + s * 16, (uint32_t)w); shm_hdr(H, 20 + s * 16, (uint32_t)h); shm_hdr(H, 24 + s * 16, (uint32_t)kind);
    shm_hdr(H, 72, (uint32_t)H->tv_aspect); shm_hdr(H, 76, (uint32_t)H->integer_scale); MemoryBarrier(); shm_hdr(H, 8, s); MemoryBarrier(); H->seq++; shm_hdr(H, 4, H->seq); H->slot ^= 1; }
static void shm_nes(host *H, const uint16_t *px, int w, int h) {
    uint8_t *o = H->shm + SHM_HDR + (size_t)H->slot * SHM_SLOT;
    for (int y = 0; y < h; y++) for (int x = 0; x < w; x++, o += 4) { uint16_t v = px[y * 256 + x]; uint32_t r = (v >> 11) << 3, g = ((v >> 5) & 63) << 2, b = (v & 31) << 3; o[0] = (uint8_t)(r | r >> 5); o[1] = (uint8_t)(g | g >> 6); o[2] = (uint8_t)(b | b >> 5); o[3] = 255; }
    shm_publish(H, 1, w, h); }
static void shm_canvas(host *H, const framebuffer *fb) {
    if (fb->w * fb->h * 4 > SHM_SLOT) return; uint64_t hs = 1469598103934665603ull; const uint32_t *q = (const uint32_t *)fb->rgb; size_t nq = (size_t)fb->w * fb->h * 3 / 4;
    for (size_t i = 0; i < nq; i++) hs = (hs ^ q[i]) * 1099511628211ull;
    if (hs == H->last_hash) return; H->last_hash = hs;
    uint8_t *o = H->shm + SHM_HDR + (size_t)H->slot * SHM_SLOT; const uint8_t *s = fb->rgb;
    for (int i = 0; i < fb->w * fb->h; i++, s += 3, o += 4) { o[0] = s[0]; o[1] = s[1]; o[2] = s[2]; o[3] = 255; }
    shm_publish(H, 2, fb->w, fb->h); }
void host_present_nes(host *H, const uint16_t *px, int w, int h) {
    if (shm_on(H)) { H->last_kind = 1; shm_nes(H, px, w, h); return; }
    for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { uint16_t v = px[y * 256 + x]; uint32_t r = (v >> 11) << 3, g = ((v >> 5) & 63) << 2, b = (v & 31) << 3; H->nes32[y * 256 + x] = (r | r >> 5) << 16 | (g | g >> 6) << 8 | (b | b >> 5); }
    H->last = H->nes32; H->lw = w; H->lh = h; H->lstride = 256; H->last_kind = 1; blit(H); }
int host_canvas_scale(void) { const char *e = getenv("CCS_PLAYER_SCALE"); return e && atoi(e) == 1 ? 1 : 2; }
void host_home_pace(int *cover_budget, int *music_ahead) { *cover_budget = 4; *music_ahead = 6144; }   /* as before: four covers per pass, the song about 130 ms ahead */
void host_present_canvas(host *H, const framebuffer *fb) {
    if (shm_on(H)) { H->last_kind = 2; shm_canvas(H, fb); return; }
    if (!H->canvas32 || H->cvw != fb->w || H->cvh != fb->h) { free(H->canvas32); H->canvas32 = malloc((size_t)fb->w * fb->h * 4); H->cvw = fb->w; H->cvh = fb->h; if (!H->canvas32) return; }
    const uint8_t *s = fb->rgb; for (int i = 0; i < fb->w * fb->h; i++, s += 3) H->canvas32[i] = (uint32_t)s[0] << 16 | (uint32_t)s[1] << 8 | s[2];
    H->last = H->canvas32; H->lw = fb->w; H->lh = fb->h; H->lstride = fb->w; H->last_kind = 2; H->cv_gen++; blit(H); }
/* sizes are remembered in logical (96 dpi) units: a window on a 200% display and one on a 100% display have the same remembered size */
static int win_dpi(HWND w) { HMODULE u = GetModuleHandleW(L"user32.dll"); typedef UINT (WINAPI *f_t)(HWND); f_t f = u ? (f_t)(void *)GetProcAddress(u, "GetDpiForWindow") : NULL; UINT d = f && w ? f(w) : 96; return d ? (int)d : 96; }
static void save_display(host *H) {
    if (H->uw > 0 && H->uh > 0) gp_settings_save_window(H->display_path, H->fs, H->uw, H->uh); }   /* the chosen size, never a display's cap */
int host_fullscreen(host *H) { return H->fs; }
void host_toggle_fullscreen(host *H) {
    LONG st = GetWindowLongW(H->hwnd, GWL_STYLE);
    if (!H->fs) { MONITORINFO mi = { sizeof mi }; H->wp.length = sizeof H->wp; GetWindowPlacement(H->hwnd, &H->wp);
        if (GetMonitorInfoW(MonitorFromWindow(H->hwnd, MONITOR_DEFAULTTONEAREST), &mi)) { H->fs = 1;   /* set first: the resize below must not replace the normal size */
            SetWindowLongW(H->hwnd, GWL_STYLE, (st & ~WS_OVERLAPPEDWINDOW) | WS_POPUP);
            SetWindowPos(H->hwnd, HWND_TOP, mi.rcMonitor.left, mi.rcMonitor.top, mi.rcMonitor.right - mi.rcMonitor.left, mi.rcMonitor.bottom - mi.rcMonitor.top, SWP_NOOWNERZORDER | SWP_FRAMECHANGED); } }
    else { SetWindowLongW(H->hwnd, GWL_STYLE, (st & ~WS_POPUP) | WS_OVERLAPPEDWINDOW); SetWindowPlacement(H->hwnd, &H->wp);
        SetWindowPos(H->hwnd, NULL, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_FRAMECHANGED); H->fs = 0; }
    host_trace(H, "{\"ev\":\"fullscreen\",\"on\":%d}", H->fs); save_display(H); }

/* ---- the Studio link (embedded): one line per command or event ---- */
static void bring_forward(host *H);
void host_send(host *H, const char *line) {
    if (!H || !H->embed || !H->pipe) return; char b[4200]; int n = snprintf(b, sizeof b, "%s\n", line); DWORD put = 0;
    if (n > 0 && !WriteFile(H->pipe, b, (DWORD)(n < (int)sizeof b ? n : (int)sizeof b - 1), &put, NULL)) host_trace(H, "{\"ev\":\"studio_link_write_failed\"}"); }
int host_embedded(host *H) { return H && H->embed; }
int host_popped(host *H) { return H && H->popped; }
void host_popout(host *H, int out) {
    if (!H || !H->embed || out == H->popped) return; H->popped = out; shm_hdr(H, 68, (uint32_t)out); memset(H->keys, 0, sizeof H->keys); memset(H->vpad, 0, sizeof H->vpad);
    if (out) { ShowWindow(H->hwnd, SW_SHOW); bring_forward(H); H->focused = GetForegroundWindow() == H->hwnd; }
    else { ShowWindow(H->hwnd, SW_HIDE); H->focused = H->own; H->last_hash = 0; H->lost = !H->own; }
    host_trace(H, "{\"ev\":\"popped\",\"out\":%d}", out); char l[32]; snprintf(l, sizeof l, "popped %d", out); host_send(H, l); }
static void studio_cmd(host *H, char *l) {   /* one line from the Studio (the page's input and the Preview's controls) */
    int a = 0, b = 0, c = 0, d = 0; char w[1100];
    if (sscanf(l, "key d %d", &a) == 1 && a > 0 && a < 256) { if (a == VK_ESCAPE) H->menu_key = 1; else { H->keys[a] = 1; H->tapped[a] = 1; } }
    else if (sscanf(l, "key u %d", &a) == 1 && a > 0 && a < 256) H->keys[a] = 0;
    else if (!strncmp(l, "pad ", 4) && strlen(l) > 6) { for (int k = 0; k < GA_COUNT; k++) if (!strcmp(l + 6, gp_action_name(k))) H->vpad[k] = l[4] == 'd'; }
    else if (sscanf(l, "pt %d %d %d %d", &a, &b, &c, &d) == 4 && a >= PT_MOVE && a <= PT_WHEEL && H->npq < 16) { if (!(a == PT_MOVE && H->npq && H->pq[H->npq - 1].kind == PT_MOVE)) H->pq[H->npq++] = (host_pointer){ a, b, c, d }; else { H->pq[H->npq - 1].x = b; H->pq[H->npq - 1].y = c; } }
    else if (sscanf(l, "own %d", &a) == 1) { if (!H->popped && a != H->own) { H->own = a; H->focused = a; if (a) H->gained = 1; else { H->lost = 1; memset(H->keys, 0, sizeof H->keys); memset(H->vpad, 0, sizeof H->vpad); } } else H->own = a; }
    else if (!strncmp(l, "text ", 5) && H->text_on) { for (const char *t = l + 5; *t && H->ntext < 31; t++) if (*t >= 32 && *t < 127) H->text[H->ntext++] = *t; H->text[H->ntext] = 0; }
    else if (!strcmp(l, "text-bs") && H->text_on && H->ntext < 31) { H->text[H->ntext++] = 8; H->text[H->ntext] = 0; }
    else if (!strcmp(l, "text-enter") && H->text_on && H->ntext < 31) { H->text[H->ntext++] = 10; H->text[H->ntext] = 0; }
    else if (!strcmp(l, "menu")) H->menu_key = 1;
    else if (!strcmp(l, "close")) H->preview_close = 1;   /* the Preview's Close (not the pop-out window's X: WM_CLOSE) */
    else if (!strcmp(l, "popout")) host_popout(H, 1);
    else if (!strcmp(l, "popin")) { if (H->popped) { host_popout(H, 0); H->lost = 1; } }   /* Bring It Back Here: the picture returns; a running game waits paused (its window's own X asks first instead) */
    else if (!strcmp(l, "front")) { if (H->popped) { ShowWindow(H->hwnd, IsIconic(H->hwnd) ? SW_RESTORE : SW_SHOW); SetForegroundWindow(H->hwnd); } }
    else if (sscanf(l, "theme %47s", w) == 1) snprintf(H->theme_cmd, sizeof H->theme_cmd, "%s", w);
    else if (sscanf(l, "music %3s", w) == 1) H->music_cmd = !strcmp(w, "on") ? 1 : 0;
    else if (sscanf(l, "select %199s", w) == 1) snprintf(H->select_cmd, sizeof H->select_cmd, "%s", w);
    else if (!strcmp(l, "reload")) snprintf(H->reload_cmd, sizeof H->reload_cmd, "1");   /* the same package folder, rebuilt by the backend */
    else if (!strcmp(l, "studio-exit")) H->studio_exit = 1; else if (!strcmp(l, "studio-exit force")) H->studio_exit = 2;
    else if (!strncmp(l, "map ", 4) && !H->shm) { unsigned long long hv = 0; size_t sz = 0; if (sscanf(l + 4, "%llx %zu", &hv, &sz) != 2) return; H->shm_size = sz;
        HANDLE hm = (HANDLE)(uintptr_t)hv; void *v = MapViewOfFile(hm, FILE_MAP_WRITE, 0, 0, H->shm_size);
        if (v && H->shm_size >= SHM_HDR + 2 * (size_t)SHM_SLOT) { H->shm = v; memcpy(H->shm, "CCSF", 4); shm_hdr(H, 68, (uint32_t)H->popped); H->last_hash = 0; host_trace(H, "{\"ev\":\"frame_buffer\",\"bytes\":%zu}", H->shm_size); host_send(H, "mapped"); }
        else { host_trace(H, "{\"ev\":\"frame_buffer_failed\",\"error\":%lu}", GetLastError()); host_send(H, "map-failed"); } }
    host_trace(H, "{\"ev\":\"studio_cmd\",\"cmd\":\"%.20s\"}", l); }
static void studio_read(host *H) {
    if (!H->embed || !H->pipe) return; DWORD avail = 0;
    if (!PeekNamedPipe(H->pipe, NULL, 0, NULL, &avail, NULL)) { DWORD err = GetLastError(); host_trace(H, "{\"ev\":\"studio_gone\",\"how\":\"pipe\",\"error\":%lu}", err); CloseHandle(H->pipe); H->pipe = NULL; H->studio_exit = 3; return; }
    while (avail) { char b[1024]; DWORD got = 0, want = avail < sizeof b ? avail : (DWORD)sizeof b; if (!ReadFile(H->pipe, b, want, &got, NULL) || !got) break; avail -= got;
        for (DWORD i = 0; i < got; i++) { if (b[i] == '\n') { H->line[H->nline] = 0; if (H->nline) studio_cmd(H, H->line); H->nline = 0; } else if (b[i] != '\r' && H->nline < (int)sizeof H->line - 1) H->line[H->nline++] = b[i]; } } }
/* ---- the mouse: client pixels to the stage (the canvas's 1280 x 720 layout); only while the canvas (HOME, menus) is shown ---- */
static int to_stage(host *H, int cx, int cy, int *sx, int *sy) {
    if (!H->last || H->last_kind != 2) return 0; RECT d; int cs = ccs_scale > 1 ? ccs_scale : 1; dest_rect(H, H->lw / cs, H->lh / cs, 1.0, &d);
    int dw = d.right - d.left, dh = d.bottom - d.top; if (dw <= 0 || dh <= 0) return 0;
    if (cx < d.left || cx >= d.right || cy < d.top || cy >= d.bottom) { *sx = *sy = -1; return 1; }
    *sx = (cx - d.left) * CCS_FB_W / dw; *sy = (cy - d.top) * CCS_FB_H / dh; return 1; }
static void pq_add(host *H, int kind, int cx, int cy, int delta) {
    static const char *KN[] = { "move", "click", "double", "back", "wheel" }; int sx, sy; if (!to_stage(H, cx, cy, &sx, &sy)) return;
    if (kind == PT_MOVE && H->npq && H->pq[H->npq - 1].kind == PT_MOVE) { H->pq[H->npq - 1].x = sx; H->pq[H->npq - 1].y = sy; return; }
    if (H->npq >= 16) return; H->pq[H->npq++] = (host_pointer){ kind, sx, sy, delta };
    if (kind != PT_MOVE) host_trace(H, "{\"ev\":\"mouse\",\"kind\":\"%s\",\"client\":[%d,%d],\"stage\":[%d,%d],\"delta\":%d}", KN[kind], cx, cy, sx, sy, delta); }
void host_cursor(host *H, int visible) {
    int hide = !visible; if (!H || hide == H->cursor_hidden) return; H->cursor_hidden = hide; POINT p; RECT r;
    if (GetForegroundWindow() == H->hwnd && GetCursorPos(&p) && ScreenToClient(H->hwnd, &p) && GetClientRect(H->hwnd, &r) && PtInRect(&r, p)) SetCursor(hide ? NULL : LoadCursorW(NULL, (LPCWSTR)IDC_ARROW));
    host_trace(H, "{\"ev\":\"cursor\",\"visible\":%d}", visible); }

/* ---- window ---- */
static LRESULT CALLBACK wndproc(HWND w, UINT m, WPARAM wp, LPARAM lp) {
    host *H = G;
    switch (m) {
    case WM_CLOSE: if (H) H->close_req = 1; return 0;
    case WM_DEVICECHANGE: if (H) H->enum_due = 1; return TRUE;   /* a controller was plugged in or removed: list DirectInput devices again */
    case WM_SIZE: if (H) { H->cw = LOWORD(lp); H->ch = HIWORD(lp); if (!H->fs && wp == SIZE_RESTORED) { H->ncw = H->cw; H->nch = H->ch; } blit(H);
            host_trace(H, "{\"ev\":\"size\",\"client\":[%d,%d],\"kind\":%d,\"in_loop\":%d}", H->cw, H->ch, (int)wp, H->sm_on); } return 0;
    case WM_ENTERSIZEMOVE: if (H) { RECT cr; GetClientRect(w, &cr); H->sm_on = 1; H->sm_dpi = 0; H->sm_sized = 0; H->sm_w = cr.right; H->sm_h = cr.bottom; } return 0;
    case WM_SIZING: if (H) H->sm_sized = 1; return TRUE;   /* a person is dragging a border */
    case WM_EXITSIZEMOVE: if (H) { RECT cr; GetClientRect(w, &cr); H->sm_on = 0;   /* the person resized the window: that is the size to remember */
            host_trace(H, "{\"ev\":\"size_move_end\",\"client\":[%d,%d],\"start\":[%d,%d],\"dpi_changed\":%d}", (int)cr.right, (int)cr.bottom, H->sm_w, H->sm_h, H->sm_dpi);
            if (!H->fs && H->sm_sized && !H->sm_dpi && !IsZoomed(w) && !IsIconic(w) && H->dpi && (cr.right != H->sm_w || cr.bottom != H->sm_h)) { H->uw = (cr.right * 96 + H->dpi / 2) / H->dpi; H->uh = (cr.bottom * 96 + H->dpi / 2) / H->dpi; } } return 0;
    case WM_PAINT: { PAINTSTRUCT ps; BeginPaint(w, &ps); EndPaint(w, &ps); if (H) blit(H); return 0; }
    case WM_ERASEBKGND: return 1;
    case WM_PRINTCLIENT: if (H) blit_dc(H, (HDC)wp); return 0;   /* PrintWindow (window captures in tests) gets the current picture */
    case WM_SETFOCUS: if (H && !(H->embed && !H->popped)) { H->gained = 1; H->focused = 1; } return 0;
    case WM_KILLFOCUS: if (H && !(H->embed && !H->popped)) { H->lost = 1; H->focused = 0; memset(H->keys, 0, sizeof H->keys); } return 0;   /* no key stays held while the window is away; embedded: the Preview says who owns input */
    case WM_SYSKEYDOWN: if (wp == VK_RETURN && (lp & (1 << 29))) { if (H && !(lp & (1 << 30))) host_toggle_fullscreen(H); return 0; } if (wp == VK_F4) break; /* fall through */
    case WM_KEYDOWN: if (H && wp < 256) { if (wp == VK_ESCAPE && !(lp & (1 << 30))) H->menu_key = 1; else if (wp == VK_F11 && !(lp & (1 << 30))) host_toggle_fullscreen(H); H->keys[wp] = 1; H->tapped[wp] = 1;
            if (wp == VK_SHIFT || wp == VK_CONTROL) { H->keys[VK_LSHIFT] = (GetKeyState(VK_LSHIFT) & 0x8000) != 0; H->keys[VK_RSHIFT] = (GetKeyState(VK_RSHIFT) & 0x8000) != 0; H->keys[VK_LCONTROL] = (GetKeyState(VK_LCONTROL) & 0x8000) != 0; H->keys[VK_RCONTROL] = (GetKeyState(VK_RCONTROL) & 0x8000) != 0; } } return 0;
    case WM_SYSKEYUP: case WM_KEYUP: if (H && wp < 256) { H->keys[wp] = 0; if (wp == VK_SHIFT) H->keys[VK_LSHIFT] = H->keys[VK_RSHIFT] = 0; if (wp == VK_CONTROL) H->keys[VK_LCONTROL] = H->keys[VK_RCONTROL] = 0; } return 0;
    case WM_SYSCHAR: return 0;   /* no menu beep for Alt+Enter */
    case WM_CHAR: if (H && H->text_on && H->ntext < 31) { wchar_t c = (wchar_t)wp; char o = c == 8 ? 8 : c == 13 ? 10 : (c >= 32 && c < 127) ? (char)c : 0; if (o) { H->text[H->ntext++] = o; H->text[H->ntext] = 0; } } return 0;
    case WM_MOUSEACTIVATE: if (LOWORD(lp) == HTCLIENT && GetForegroundWindow() != w) return MA_ACTIVATEANDEAT; break;   /* the click that brings the window forward is not acted on */
    case WM_SETCURSOR: if (H && H->cursor_hidden && LOWORD(lp) == HTCLIENT) { SetCursor(NULL); return TRUE; } break;
    case WM_MOUSEMOVE: if (H) { POINT s = { (short)LOWORD(lp), (short)HIWORD(lp) }; ClientToScreen(w, &s);   /* F3: only a mouse that moved on the screen moves the highlight */
            if (H->have_scr && s.x == H->last_scr.x && s.y == H->last_scr.y) return 0; H->last_scr = s; H->have_scr = 1; pq_add(H, PT_MOVE, (short)LOWORD(lp), (short)HIWORD(lp), 0); } return 0;
    case WM_LBUTTONDOWN: if (H) H->ldown = 1; return 0;
    case WM_LBUTTONUP: if (H) { if (H->dbl) H->dbl = 0; else if (H->ldown) pq_add(H, PT_CLICK, (short)LOWORD(lp), (short)HIWORD(lp), 0); H->ldown = 0; } return 0;
    case WM_LBUTTONDBLCLK: if (H) { H->dbl = 1; H->ldown = 0; pq_add(H, PT_DOUBLE, (short)LOWORD(lp), (short)HIWORD(lp), 0); } return 0;   /* its button-up is not another click */
    case WM_RBUTTONUP: if (H) pq_add(H, PT_BACK, (short)LOWORD(lp), (short)HIWORD(lp), 0); return 0;
    case WM_XBUTTONUP: if (H && HIWORD(wp) == XBUTTON1) pq_add(H, PT_BACK, (short)LOWORD(lp), (short)HIWORD(lp), 0); return TRUE;   /* the mouse's back button */
    case WM_MOUSEWHEEL: if (H) { POINT p = { (short)LOWORD(lp), (short)HIWORD(lp) }; ScreenToClient(w, &p); H->wheel_acc += (short)HIWORD(wp);
            while (H->wheel_acc >= 120) { pq_add(H, PT_WHEEL, p.x, p.y, 1); H->wheel_acc -= 120; } while (H->wheel_acc <= -120) { pq_add(H, PT_WHEEL, p.x, p.y, -1); H->wheel_acc += 120; } } return 0;
    case WM_GETMINMAXINFO: { MINMAXINFO *mm = (MINMAXINFO *)lp; mm->ptMinTrackSize.x = 400; mm->ptMinTrackSize.y = 300; return 0; }
    case 0x02E0: { /* WM_DPICHANGED: moved to a display with another scale: take the size Windows suggests (the same size to the eye) */
        RECT *s = (RECT *)lp; int nd = (int)LOWORD(wp) ? (int)LOWORD(wp) : 96, od = H && H->dpi ? H->dpi : 96; if (H && H->sm_on) H->sm_dpi = 1;
        (void)od; int tw = H && H->uw > 0 ? H->uw * nd / 96 : 0, th = H && H->uh > 0 ? H->uh * nd / 96 : 0;   /* the chosen logical size on the new display */
        if (H && !H->fs) { H->auto_size = 1; SetWindowPos(w, NULL, s->left, s->top, s->right - s->left, s->bottom - s->top, SWP_NOZORDER | SWP_NOACTIVATE);
            if (tw > 0 && th > 0) cap_to_work(MonitorFromRect(s, MONITOR_DEFAULTTONEAREST), &tw, &th); H->fit_w = tw; H->fit_h = th; PostMessageW(w, WM_APP + 7, 0, 0); }
        if (H) H->dpi = nd; return 0; }
    case WM_APP + 7: if (H) {   /* the scale change is complete: now the client can be fitted exactly */
            if (!H->fs && H->fit_w > 0 && H->fit_h > 0) fit_client(w, H->fit_w, H->fit_h);
            RECT cr; GetClientRect(w, &cr); host_trace(H, "{\"ev\":\"dpi_changed\",\"dpi\":%d,\"target\":[%d,%d],\"client\":[%d,%d]}", H->dpi, H->fit_w, H->fit_h, (int)cr.right, (int)cr.bottom);
            H->fit_w = H->fit_h = 0; H->auto_size = 0; } return 0;
    }
    return DefWindowProcW(w, m, wp, lp); }
/* the Classic Cart Studio icon (app.ico, embedded as resource 1): its exact frame at the small-icon size for the SYSTEM dpi (the taskbar's
   display), used as both the big and the small window icon, as the Studio's own window does (Native.cs ApplyTaskbarIcon) */
static HICON app_icon_exact(HINSTANCE hi) {
    HMODULE u = GetModuleHandleW(L"user32.dll"); typedef UINT (WINAPI *sd_t)(void); typedef int (WINAPI *gm_t)(int, UINT);
    sd_t sd = u ? (sd_t)(void *)GetProcAddress(u, "GetDpiForSystem") : NULL; gm_t gm = u ? (gm_t)(void *)GetProcAddress(u, "GetSystemMetricsForDpi") : NULL;
    UINT dpi = sd ? sd() : 96; int size = gm ? gm(SM_CXSMICON, dpi ? dpi : 96) : GetSystemMetrics(SM_CXSMICON); if (size <= 0) size = 16;
    return (HICON)LoadImageW(hi, MAKEINTRESOURCEW(1), IMAGE_ICON, size, size, 0); }
static void dpi_aware(void) { HMODULE u = GetModuleHandleW(L"user32.dll"); typedef BOOL (WINAPI *f_t)(HANDLE); f_t f = u ? (f_t)(void *)GetProcAddress(u, "SetProcessDpiAwarenessContext") : NULL; if (f) f((HANDLE)-4); else SetProcessDPIAware(); }
static wchar_t *wide(const char *u8) { int n = MultiByteToWideChar(CP_UTF8, 0, u8, -1, NULL, 0); wchar_t *w = malloc((size_t)n * sizeof *w); MultiByteToWideChar(CP_UTF8, 0, u8, -1, w, n); return w; }
static void bring_forward(host *H) {   /* only when the window in front belongs to the Studio that asked for this game: never takes focus from another app */
    HWND fg = GetForegroundWindow(); DWORD pid = 0, ft = fg ? GetWindowThreadProcessId(fg, &pid) : 0;
    if (fg == H->hwnd) return;
    if (H->studio_pid && (int)pid == H->studio_pid && ft) { DWORD me = GetCurrentThreadId(); AttachThreadInput(ft, me, TRUE); SetForegroundWindow(H->hwnd); BringWindowToTop(H->hwnd); SetFocus(H->hwnd); AttachThreadInput(ft, me, FALSE); }
    else SetForegroundWindow(H->hwnd);
    host_trace(H, "{\"ev\":\"foreground\",\"ok\":%d,\"front_was_studio\":%d}", GetForegroundWindow() == H->hwnd, H->studio_pid && (int)pid == H->studio_pid); }

/* ---- controllers ---- */
static void pad_key(const pad_dev *d, char *g, size_t n) { snprintf(g, n, "%08lx-%04x-%04x", (unsigned long)d->guid.Data1, d->guid.Data2, d->guid.Data3); }
static void pads_load(host *H, pad_dev *d) {   /* the remembered setup of a DirectInput controller (player-pads.json: its id -> 8 input codes), else a known
    controller's layout (F17), else none: its directions work, its buttons open its setup (gp_session) instead of guessing A and B */
    char g[64]; pad_key(d, g, sizeof g); gp_map_default(d->map); d->known = NULL; d->configured = gp_pads_load(H->pads_path, g, d->map);
    if (!d->configured && gp_known_map(d->vid, d->pid, d->map, &d->known)) d->configured = 1; }
/* what the person reads for a controller: how it connects and "game controller" (Windows' own word for the class), numbered when two
   read the same; never the product's own name or model code (that stays in the private trace) */
static void dev_key(const pad_dev *d, char *out, size_t n) {   /* stays the same for the device in the same port: DirectInput's instance, the XInput slot */
    if (d->kind == 1) { snprintf(out, n, "xinput-%d", d->xi); return; } wchar_t g[64]; StringFromGUID2(&d->guid, g, 64); WideCharToMultiByte(CP_UTF8, 0, g, -1, out, (int)n, NULL, NULL); }
static void who_plays(host *H, int *who) {   /* gp_assign_players with the remembered choices that are connected now */
    int pick[GP_PLAYERS]; for (int p = 0; p < GP_PLAYERS; p++) { pick[p] = !H->pick_key[p][0] ? GP_PICK_AUTO : !strcmp(H->pick_key[p], "none") ? GP_PICK_NONE : GP_PICK_AUTO;
        for (int i = 0; i < H->ndev; i++) { char k[64]; dev_key(&H->dev[i], k, sizeof k); if (!strcmp(k, H->pick_key[p])) pick[p] = i; } }
    gp_assign_players(H->ndev, pick, H->swap, who); }
static void assign_load(host *H) {   /* player-assign.json: {"p1":"<device>","p2":"<device>"} */
    FILE *f = fopen(H->assign_path, "rb"); if (!f) return; char b[512]; size_t k = fread(b, 1, sizeof b - 1, f); fclose(f); b[k] = 0;
    for (int p = 0; p < GP_PLAYERS; p++) { char tag[8]; snprintf(tag, sizeof tag, "\"p%d\":\"", p + 1); char *s = strstr(b, tag); if (!s) continue; s += strlen(tag); char *e = strchr(s, '"');
        if (e && e - s < 63) { memcpy(H->pick_key[p], s, (size_t)(e - s)); H->pick_key[p][e - s] = 0; } } }
static void assign_save(host *H) { FILE *f = fopen(H->assign_path, "wb"); if (!f) return; fprintf(f, "{\"p1\":\"%s\",\"p2\":\"%s\"}\n", H->pick_key[0], H->pick_key[1]); fclose(f); }
static void pad_label(host *H, const wchar_t *path, wchar_t *out, size_t n) {
    wchar_t lp[MAX_PATH] = L""; if (path) { wcsncpy(lp, path, MAX_PATH - 1); _wcslwr(lp); }
    const wchar_t *bus = wcsstr(lp, L"{00001124-") || wcsstr(lp, L"{00001812-") ? L"Bluetooth " : wcsstr(lp, L"vid_") ? L"USB " : L"";
    _snwprintf(out, n, L"%lsgame controller", bus); out[n - 1] = 0; size_t L = wcslen(out); int same = 0;
    for (int i = 0; i < H->ndev; i++) if (!wcsncmp(H->dev[i].name, out, L) && (!H->dev[i].name[L] || H->dev[i].name[L] == L' ')) same++;
    if (same) { _snwprintf(out + L, n - L, L" %d", same + 1); out[n - 1] = 0; } }
static BOOL CALLBACK enum_cb(LPCDIDEVICEINSTANCEW inst, LPVOID ctx) {
    host *H = ctx; if (H->ndev >= MAXDEV) return DIENUM_STOP;
    for (int i = 0; i < H->ndev; i++) if (H->dev[i].kind == 2 && IsEqualGUID(&H->dev[i].guid, &inst->guidInstance)) { H->dev[i].connected = 2; return DIENUM_CONTINUE; }
    /* Xbox-compatible controllers are read through XInput; their DirectInput twin is skipped (its product name says XINPUT or Xbox) */
    if (wcsstr(inst->tszProductName, L"XINPUT") || wcsstr(inst->tszProductName, L"Xbox") || wcsstr(inst->tszProductName, L"XBOX")) return DIENUM_CONTINUE;
    LPDIRECTINPUTDEVICE8W dv; if (FAILED(IDirectInput8_CreateDevice(H->di, &inst->guidInstance, &dv, NULL))) return DIENUM_CONTINUE;
    if (FAILED(IDirectInputDevice8_SetDataFormat(dv, &c_dfDIJoystick2)) || FAILED(IDirectInputDevice8_SetCooperativeLevel(dv, H->hwnd, DISCL_BACKGROUND | DISCL_NONEXCLUSIVE))) { IDirectInputDevice8_Release(dv); return DIENUM_CONTINUE; }
    DIPROPRANGE pr; memset(&pr, 0, sizeof pr); pr.diph.dwSize = sizeof pr; pr.diph.dwHeaderSize = sizeof pr.diph; pr.diph.dwHow = DIPH_BYOFFSET; pr.lMin = -1000; pr.lMax = 1000;
    static const DWORD AXO[GP_AXES] = { DIJOFS_X, DIJOFS_Y, DIJOFS_Z, DIJOFS_RX, DIJOFS_RY, DIJOFS_RZ };   /* every axis -1000..1000 (absent ones read 0) */
    for (int a = 0; a < GP_AXES; a++) { pr.diph.dwObj = AXO[a]; IDirectInputDevice8_SetProperty(dv, DIPROP_RANGE, &pr.diph); }
    IDirectInputDevice8_Acquire(dv);
    DIPROPGUIDANDPATH gp; memset(&gp, 0, sizeof gp); gp.diph.dwSize = sizeof gp; gp.diph.dwHeaderSize = sizeof gp.diph; gp.diph.dwHow = DIPH_DEVICE;
    wchar_t label[128]; pad_label(H, SUCCEEDED(IDirectInputDevice8_GetProperty(dv, DIPROP_GUIDANDPATH, &gp.diph)) ? gp.wszPath : NULL, label, 128);
    pad_dev *d = &H->dev[H->ndev++]; memset(d, 0, sizeof *d); d->kind = 2; d->dev = dv; d->guid = inst->guidInstance; d->connected = 2; wcscpy(d->name, label);
    d->vid = (int)LOWORD(inst->guidProduct.Data1); d->pid = (int)HIWORD(inst->guidProduct.Data1); pads_load(H, d);   /* a USB HID device's product GUID carries its vendor and product ids */
    d->uid = ++H->next_uid; d->raw.pov = -1;
    char n8[256], p8[256]; WideCharToMultiByte(CP_UTF8, 0, d->name, -1, n8, sizeof n8, NULL, NULL); WideCharToMultiByte(CP_UTF8, 0, inst->tszProductName, -1, p8, sizeof p8, NULL, NULL);
    char e[300], pe[300]; json_escape(n8, e, sizeof e); json_escape(p8, pe, sizeof pe);
    host_trace(H, "{\"ev\":\"pad_connected\",\"kind\":\"directinput\",\"id\":%d,\"name\":%s,\"product_name\":%s,\"product\":\"%08lx\",\"type\":%lu,\"vid\":\"%04x\",\"pid\":\"%04x\",\"setup\":\"%s\"}", d->uid, e, pe, (unsigned long)inst->guidProduct.Data1, (unsigned long)inst->dwDevType, d->vid, d->pid, d->known ? d->known : d->configured ? "saved" : "none");
    return DIENUM_CONTINUE; }
static void enum_devices(host *H) {
    if (!H->di) return; for (int i = 0; i < H->ndev; i++) if (H->dev[i].kind == 2) H->dev[i].connected = 0;
    IDirectInput8_EnumDevices(H->di, DI8DEVCLASS_GAMECTRL, enum_cb, H, DIEDFL_ATTACHEDONLY);
    for (int i = 0; i < H->ndev; i++) if (H->dev[i].kind == 2 && !H->dev[i].connected) { host_trace(H, "{\"ev\":\"pad_disconnected\",\"kind\":\"directinput\"}"); IDirectInputDevice8_Release(H->dev[i].dev); H->dev[i] = H->dev[--H->ndev]; i--; } }
static void read_pads(host *H) {
    /* XInput slots are devices 0..3 of their own; DirectInput devices follow; each fills act[] */
    for (int x = 0; x < 4; x++) { pad_dev *d = NULL; for (int i = 0; i < H->ndev; i++) if (H->dev[i].kind == 1 && H->dev[i].xi == x) d = &H->dev[i];
        if (!d && gp_ms() < H->xi_next[x]) continue;   /* an empty slot: asked again about once a second */
        XINPUT_STATE st; int on = H->xget && H->xget((DWORD)x, &st) == ERROR_SUCCESS; if (!on) H->xi_next[x] = gp_ms() + 1000;
        if (on && !d && H->ndev < MAXDEV) { d = &H->dev[H->ndev++]; memset(d, 0, sizeof *d); d->kind = 1; d->xi = x; d->uid = ++H->next_uid; { int same = 0; for (int j = 0; j < H->ndev - 1; j++) if (H->dev[j].kind == 1) same++; if (same) _snwprintf(d->name, 128, L"Xbox-compatible controller %d", same + 1); else wcscpy(d->name, L"Xbox-compatible controller"); } host_trace(H, "{\"ev\":\"pad_connected\",\"kind\":\"xinput\",\"slot\":%d,\"id\":%d}", x, d->uid); }
        if (!on && d) { host_trace(H, "{\"ev\":\"pad_disconnected\",\"kind\":\"xinput\",\"slot\":%d}", x); *d = H->dev[--H->ndev]; continue; }
        if (!on) continue; WORD b = st.Gamepad.wButtons; SHORT lx = st.Gamepad.sThumbLX, ly = st.Gamepad.sThumbLY; const SHORT DZ = 12000;
        memcpy(d->prevbtn, d->btn, sizeof d->btn); for (int k = 0; k < 16; k++) d->btn[k] = (b >> k) & 1;
        if (H->trace) { char sig[96]; snprintf(sig, sizeof sig, "%04x %d %d", b, lx / 8192, ly / 8192); if (strcmp(sig, d->rawsig)) { strcpy(d->rawsig, sig); host_trace(H, "{\"ev\":\"pad_raw\",\"id\":%d,\"kind\":\"xinput\",\"buttons\":\"%04x\",\"lx\":%d,\"ly\":%d}", d->uid, b, lx, ly); } }
        d->act[GA_UP] = (b & XINPUT_GAMEPAD_DPAD_UP) || ly > DZ; d->act[GA_DOWN] = (b & XINPUT_GAMEPAD_DPAD_DOWN) || ly < -DZ;
        d->act[GA_LEFT] = (b & XINPUT_GAMEPAD_DPAD_LEFT) || lx < -DZ; d->act[GA_RIGHT] = (b & XINPUT_GAMEPAD_DPAD_RIGHT) || lx > DZ;
        d->act[GA_A] = (b & (XINPUT_GAMEPAD_A | XINPUT_GAMEPAD_Y)) != 0; d->act[GA_B] = (b & (XINPUT_GAMEPAD_B | XINPUT_GAMEPAD_X)) != 0;   /* Xbox A = NES A, Xbox B = NES B (X and Y as the turbo-free alternates) */
        d->act[GA_SELECT] = (b & XINPUT_GAMEPAD_BACK) != 0; d->act[GA_START] = (b & XINPUT_GAMEPAD_START) != 0; }
    for (int i = 0; i < H->ndev; i++) { pad_dev *d = &H->dev[i]; if (d->kind != 1) continue; d->hit = 0; for (int a = 0; a < GA_COUNT; a++) { if (d->act[a] && !d->pact[a] && !d->hit) d->hit = a + 1; d->pact[a] = d->act[a]; } }
    for (int i = 0; i < H->ndev; i++) { pad_dev *d = &H->dev[i]; if (d->kind != 2) continue; d->hit = 0; DIJOYSTATE2 js;
        if (FAILED(IDirectInputDevice8_Poll(d->dev))) IDirectInputDevice8_Acquire(d->dev);
        if (FAILED(IDirectInputDevice8_GetDeviceState(d->dev, sizeof js, &js))) { memset(d->act, 0, sizeof d->act); memset(&d->raw, 0, sizeof d->raw); d->raw.pov = -1; continue; }   /* unplugged: nothing stays held */
        memcpy(d->prevbtn, d->btn, sizeof d->btn); for (int k = 0; k < 32; k++) d->btn[k] = d->raw.btn[k] = (js.rgbButtons[k] & 0x80) != 0;
        d->raw.pov = LOWORD(js.rgdwPOV[0]) == 0xFFFF ? -1 : (int)js.rgdwPOV[0];
        const LONG ax[GP_AXES] = { js.lX, js.lY, js.lZ, js.lRx, js.lRy, js.lRz }; for (int a = 0; a < GP_AXES; a++) d->raw.axis[a] = (int)ax[a];
        gp_raw_actions(&d->raw, d->map, d->act);   /* gp_pads.c: the controller's mapping; directions also from the hat and the stick */
        if (!d->configured) d->act[GA_A] = d->act[GA_B] = d->act[GA_SELECT] = d->act[GA_START] = 0;   /* F17: no guessed buttons; a press opens its setup */
        d->hit = 0; for (int a = 0; a < GA_COUNT; a++) { if (d->act[a] && !d->pact[a] && !d->hit) d->hit = a + 1; d->pact[a] = d->act[a]; }
        if (!d->hit) for (int k = 0; k < 32; k++) if (d->btn[k] && !d->prevbtn[k]) { d->hit = 9; break; }   /* a button with no NES action still finds the controller */
        if (H->trace) {   /* tests and the controller session: each change of what the controller reports */
            char sig[96]; unsigned long bits = 0; for (int k = 0; k < 32; k++) if (d->raw.btn[k]) bits |= 1ul << k; int q[GP_AXES]; for (int a = 0; a < GP_AXES; a++) q[a] = d->raw.axis[a] / 250;
            snprintf(sig, sizeof sig, "%08lx %d %d %d %d %d %d %d", bits, d->raw.pov, q[0], q[1], q[2], q[3], q[4], q[5]);
            if (strcmp(sig, d->rawsig)) { strcpy(d->rawsig, sig); host_trace(H, "{\"ev\":\"pad_raw\",\"id\":%d,\"kind\":\"directinput\",\"buttons\":\"%08lx\",\"pov\":%d,\"axes\":[%d,%d,%d,%d,%d,%d],\"act\":\"%d%d%d%d%d%d%d%d\"}",
                d->uid, bits, d->raw.pov, d->raw.axis[0], d->raw.axis[1], d->raw.axis[2], d->raw.axis[3], d->raw.axis[4], d->raw.axis[5], d->act[0], d->act[1], d->act[2], d->act[3], d->act[4], d->act[5], d->act[6], d->act[7]); } } } }
int host_pads(host *H, char *out, size_t n) {   /* who plays (who_plays: the chosen controllers, else the first two in connection order) */
    char nm[2][200] = { "", "" }; const char *ctl[2] = { NULL, NULL }; int who[MAXDEV]; who_plays(H, who);
    for (int i = 0; i < H->ndev; i++) { int p = who[i]; if (p < 0 || ctl[p]) continue; WideCharToMultiByte(CP_UTF8, 0, H->dev[i].name, -1, nm[p], sizeof nm[p], NULL, NULL); ctl[p] = nm[p]; }
    gp_players_text(ctl, out, n); return H->ndev; }
void host_swap_pads(host *H) {   /* with chosen controllers the two choices trade places; else connection order is swapped */
    if (H->pick_key[0][0] || H->pick_key[1][0]) { char t[64]; memcpy(t, H->pick_key[0], 64); memcpy(H->pick_key[0], H->pick_key[1], 64); memcpy(H->pick_key[1], t, 64); assign_save(H); }
    else H->swap ^= 1;
    host_trace(H, "{\"ev\":\"swap_pads\",\"swapped\":%d,\"chosen\":%d}", H->swap, H->pick_key[0][0] || H->pick_key[1][0]); }
int host_pad_count(host *H) { return H->ndev; }
void host_pad_info(host *H, int i, char *label, size_t n, int *player) {
    if (i < 0 || i >= H->ndev) { snprintf(label, n, "controller"); *player = -1; return; } int who[MAXDEV]; who_plays(H, who);
    WideCharToMultiByte(CP_UTF8, 0, H->dev[i].name, -1, label, (int)n, NULL, NULL); *player = who[i]; }
void host_pad_choose(host *H, int player, int i) {
    if (player < 0 || player >= GP_PLAYERS) return; char k[64] = "";
    if (i == GP_PICK_NONE) snprintf(k, sizeof k, "none"); else if (i >= 0 && i < H->ndev) dev_key(&H->dev[i], k, sizeof k);
    if (i == GP_PICK_AUTO) { H->pick_key[0][0] = H->pick_key[1][0] = 0; }   /* automatic: connection order for both, as before */
    else { if (k[0] && strcmp(k, "none") && !strcmp(H->pick_key[1 - player], k)) snprintf(H->pick_key[1 - player], 64, "%s", H->pick_key[player][0] ? H->pick_key[player] : "");   /* it played the other: they trade */
        snprintf(H->pick_key[player], 64, "%s", k); }
    assign_save(H); host_trace(H, "{\"ev\":\"pad_choose\",\"player\":%d,\"device\":%d,\"p1\":\"%s\",\"p2\":\"%s\"}", player + 1, i, H->pick_key[0], H->pick_key[1]); }
int host_pad_pressed(host *H, int *action) { for (int i = 0; i < H->ndev; i++) if (H->dev[i].hit) { if (action) *action = H->dev[i].hit - 1; return i; } return -1; }
static pad_dev *pad_by_uid(host *H, int uid) { for (int i = 0; i < H->ndev; i++) if (H->dev[i].uid == uid) return &H->dev[i]; return NULL; }
int host_pad_list(host *H, int *ids, int max) { int n = 0; for (int i = 0; i < H->ndev && n < max; i++) if (H->dev[i].kind == 2) ids[n++] = H->dev[i].uid; return n; }
int host_pad_raw(host *H, int id, gp_raw *raw) { pad_dev *d = pad_by_uid(H, id); if (!d || d->kind != 2) return 0; *raw = d->raw; return 1; }
int host_pad_configured(host *H, int id) { pad_dev *d = pad_by_uid(H, id); return d && d->kind == 2 && d->configured; }
void host_pad_name(host *H, int id, char *out, size_t n) { pad_dev *d = pad_by_uid(H, id); if (!d) { snprintf(out, n, "controller"); return; } WideCharToMultiByte(CP_UTF8, 0, d->name, -1, out, (int)n, NULL, NULL); }
int host_pad_set_map(host *H, int id, const int map[GA_COUNT]) {
    pad_dev *d = pad_by_uid(H, id); if (!d || d->kind != 2) return -1; memcpy(d->map, map, sizeof d->map); d->configured = 1;
    char g[64]; pad_key(d, g, sizeof g); int r = gp_pads_save(H->pads_path, g, d->map);
    host_trace(H, "{\"ev\":\"pad_map\",\"id\":%d,\"map\":[%d,%d,%d,%d,%d,%d,%d,%d],\"saved\":%d}", id, map[0], map[1], map[2], map[3], map[4], map[5], map[6], map[7], r == 0); return r; }

/* ---- sound (waveOut) ---- */
int host_audio_open(host *H, int rate) {
    if (getenv("CCS_PLAYER_SILENT") && atoi(getenv("CCS_PLAYER_SILENT")) == 1) { host_trace(H, "{\"ev\":\"audio_open\",\"rate\":%d,\"device\":false,\"note\":\"test only: CCS_PLAYER_SILENT\"}", rate); return -1; }   /* tests: layout-only runs make no sound */
    WAVEFORMATEX f; memset(&f, 0, sizeof f); f.wFormatTag = WAVE_FORMAT_PCM; f.nChannels = 2; f.nSamplesPerSec = (DWORD)rate; f.wBitsPerSample = 16; f.nBlockAlign = 4; f.nAvgBytesPerSec = (DWORD)rate * 4;
    if (waveOutOpen(&H->wo, WAVE_MAPPER, &f, 0, 0, CALLBACK_NULL) != MMSYSERR_NOERROR) { H->wo = NULL; host_trace(H, "{\"ev\":\"audio_open\",\"rate\":%d,\"device\":false}", rate); return -1; }
    for (int i = 0; i < NBUF; i++) { H->pcm[i] = calloc(BUF_FRAMES * 2, sizeof(int16_t)); memset(&H->hdr[i], 0, sizeof H->hdr[i]); H->hdr[i].lpData = (LPSTR)H->pcm[i]; H->hdr[i].dwBufferLength = BUF_FRAMES * 4;
        waveOutPrepareHeader(H->wo, &H->hdr[i], sizeof H->hdr[i]); H->hdr[i].dwFlags |= WHDR_DONE; }
    H->audio_on = 1; host_trace(H, "{\"ev\":\"audio_open\",\"rate\":%d,\"device\":true}", rate); return 0; }
static FILE *g_tap; static int g_tap_tried;   /* tests only: CCS_PLAYER_AUDIO_TAP names a file that receives a copy of each block handed to the device */
static void tap_block(host *H, const int16_t *b) { if (!g_tap_tried) { g_tap_tried = 1; const char *p = getenv("CCS_PLAYER_AUDIO_TAP"); if (p && *p) { g_tap = fopen(p, "wb"); host_trace(H, "{\"ev\":\"audio_tap\",\"open\":%s,\"note\":\"test only: a copy of each block handed to the sound device\"}", g_tap ? "true" : "false"); } } if (g_tap) { fwrite(b, 4, BUF_FRAMES, g_tap); fflush(g_tap); } }
static void submit(host *H) {
    for (int i = 0; i < NBUF; i++) if (H->hdr[i].dwFlags & WHDR_DONE) { memcpy(H->pcm[i], H->stage, BUF_FRAMES * 4); H->hdr[i].dwFlags &= ~WHDR_DONE; waveOutWrite(H->wo, &H->hdr[i], sizeof H->hdr[i]); tap_block(H, H->pcm[i]); H->staged = 0; return; }
    H->staged = 0; }   /* every buffer queued: the oldest data plays, this block is dropped (the session's pacing keeps this from happening) */
void host_audio_push(host *H, const int16_t *st, size_t frames) {
    if (!H->audio_on) return;
    while (frames) { size_t k = BUF_FRAMES - H->staged; if (k > frames) k = frames; memcpy(H->stage + H->staged * 2, st, k * 4); H->staged += k; st += k * 2; frames -= k; if (H->staged == BUF_FRAMES) submit(H); } }
size_t host_audio_queued(host *H) { if (!H->audio_on) return 0; size_t q = H->staged; for (int i = 0; i < NBUF; i++) if (!(H->hdr[i].dwFlags & WHDR_DONE)) q += BUF_FRAMES; return q; }
void host_audio_close(host *H) {
    if (!H->audio_on) return; waveOutReset(H->wo); for (int i = 0; i < NBUF; i++) { waveOutUnprepareHeader(H->wo, &H->hdr[i], sizeof H->hdr[i]); free(H->pcm[i]); H->pcm[i] = NULL; }
    waveOutClose(H->wo); H->wo = NULL; H->audio_on = 0; H->audio_paused = 0; H->staged = 0; host_trace(H, "{\"ev\":\"audio_close\"}"); }
void host_audio_pause(host *H, int paused) {
    if (!H->audio_on || paused == H->audio_paused) return; H->audio_paused = paused;
    if (paused) waveOutReset(H->wo);   /* stop at once; buffers come back done */
    else { memset(H->stage, 0, sizeof H->stage); H->staged = BUF_FRAMES; submit(H); H->staged = BUF_FRAMES; submit(H); }   /* two blocks of silence: no crackle at the start */
    host_trace(H, "{\"ev\":\"audio_%s\"}", paused ? "pause" : "resume"); }

/* ---- the host ---- */
host *host_open(const host_config *c, const gp_settings *s, char *err, size_t n) {
    HANDLE pipe = NULL;
    if (c->embed_pipe && c->embed_pipe[0]) {   /* the Studio's Preview: its pipe for this session (it may need a moment to listen) */
        wchar_t *wp = wide(c->embed_pipe); uint64_t until = gp_ms() + 8000;
        while (wp) { pipe = CreateFileW(wp, GENERIC_READ | GENERIC_WRITE, 0, NULL, OPEN_EXISTING, 0, NULL); if (pipe != INVALID_HANDLE_VALUE) break; pipe = NULL;
            if (gp_ms() > until) break; if (GetLastError() == ERROR_PIPE_BUSY) WaitNamedPipeW(wp, 500); else Sleep(100); }
        free(wp); if (!pipe) { snprintf(err, n, "The Studio's Preview could not be reached, so the HOME was not opened there."); return NULL; } }
    HANDLE mx = CreateMutexW(NULL, TRUE, L"Local\\ClassicCartStudio.Player");
    if (mx && GetLastError() == ERROR_ALREADY_EXISTS) { CloseHandle(mx); snprintf(err, n, "A game is already open in Classic Cart Studio's player. Close it first."); return NULL; }
    host *H = calloc(1, sizeof *H); G = H; H->mutex = mx; H->pipe = pipe; H->embed = pipe != NULL; H->music_cmd = -1;
    if (c->studio_pid > 0) H->studio_proc = OpenProcess(SYNCHRONIZE, FALSE, (DWORD)c->studio_pid);   /* the parent: when it is gone, the session ends */ H->s = s; H->tv_aspect = s->tv_aspect; H->integer_scale = s->integer_scale; H->swap = s->swap_pads; H->studio_pid = c->studio_pid; H->posted_input = c->posted_input;   /* tests: keys posted to this window count without focus */
   
    if (c->trace_path && c->trace_path[0]) H->trace = fopen(c->trace_path, "w");
    snprintf(H->display_path, sizeof H->display_path, "%s", c->display_path ? c->display_path : "");
    snprintf(H->pads_path, sizeof H->pads_path, "%s", H->display_path); { char *sl = strrchr(H->pads_path, '\\'); if (sl) snprintf(sl + 1, sizeof H->pads_path - (size_t)(sl + 1 - H->pads_path), "player-pads.json"); else snprintf(H->pads_path, sizeof H->pads_path, "player-pads.json"); }
    snprintf(H->assign_path, sizeof H->assign_path, "%s", H->pads_path); { char *sl = strrchr(H->assign_path, '\\'); snprintf(sl ? sl + 1 : H->assign_path, sizeof H->assign_path - (size_t)(sl ? sl + 1 - H->assign_path : 0), "player-assign.json"); } assign_load(H);
    bind_keys(H); timeBeginPeriod(1); dpi_aware();
    WNDCLASSEXW wc; memset(&wc, 0, sizeof wc); wc.cbSize = sizeof wc; wc.lpfnWndProc = wndproc; wc.hInstance = GetModuleHandleW(NULL); wc.hCursor = LoadCursorW(NULL, (LPCWSTR)IDC_ARROW); wc.lpszClassName = L"ClassicCartStudioPlayer"; wc.style = CS_DBLCLKS;
    HICON app_icon = app_icon_exact(wc.hInstance); wc.hIcon = app_icon; wc.hIconSm = app_icon;   /* the Classic Cart Studio icon, embedded */
    RegisterClassExW(&wc);
    /* the window opens centered on the display that shows the Studio (else the primary display), at the remembered logical size scaled for
       that display, never larger than its work area */
    HMONITOR mon = MonitorFromPoint((POINT){ 0, 0 }, MONITOR_DEFAULTTOPRIMARY); HWND studio = NULL;
    if (H->studio_pid) { HWND w = NULL; while ((w = FindWindowExW(NULL, w, NULL, NULL)) != NULL) { DWORD pid = 0; GetWindowThreadProcessId(w, &pid); if ((int)pid == H->studio_pid && IsWindowVisible(w) && !GetWindow(w, GW_OWNER)) { studio = w; break; } } }
    if (studio) mon = MonitorFromWindow(studio, MONITOR_DEFAULTTONEAREST);
    MONITORINFO mi = { sizeof mi, { 0 }, { 0 }, 0 }; GetMonitorInfoW(mon, &mi); RECT wa = mi.rcWork;
    int mdpi = 96; { HMODULE sh = LoadLibraryW(L"shcore.dll"); typedef HRESULT (WINAPI *g_t)(HMONITOR, int, UINT *, UINT *); g_t g = sh ? (g_t)(void *)GetProcAddress(sh, "GetDpiForMonitor") : NULL; UINT dx = 96, dy = 96; if (g && g(mon, 0, &dx, &dy) == S_OK) mdpi = (int)dx; }
    int cw = s->win_w * mdpi / 96, ch = s->win_h * mdpi / 96; cap_to_work(mon, &cw, &ch); (void)wa;
    RECT r = { 0, 0, cw, ch }; AdjustWindowRect(&r, WS_OVERLAPPEDWINDOW, FALSE);
    int ww = r.right - r.left, wh = r.bottom - r.top, wx = wa.left + ((wa.right - wa.left) - ww) / 2, wy = wa.top + ((wa.bottom - wa.top) - wh) / 2;
    wchar_t *t = wide(c->title ? c->title : "Classic Cart Studio");
    H->hwnd = CreateWindowExW(0, wc.lpszClassName, t, WS_OVERLAPPEDWINDOW, wx, wy, ww, wh, NULL, NULL, wc.hInstance, NULL); free(t);
    if (!H->hwnd) { snprintf(err, n, "The game window could not be opened."); free(H); G = NULL; return NULL; }
    H->uw = s->win_w; H->uh = s->win_h; H->auto_size = 1; fit_client(H->hwnd, cw, ch); H->auto_size = 0;   /* exactly that client size, whatever the frame */
    if (app_icon) { SendMessageW(H->hwnd, WM_SETICON, ICON_BIG, (LPARAM)app_icon); SendMessageW(H->hwnd, WM_SETICON, ICON_SMALL, (LPARAM)app_icon); }   /* taskbar and title bar */
    if (!H->embed) { ShowWindow(H->hwnd, SW_SHOW); UpdateWindow(H->hwnd); bring_forward(H); }   /* embedded: shown only by Pop Out */
    RECT cr; GetClientRect(H->hwnd, &cr); H->cw = H->ncw = cr.right; H->ch = H->nch = cr.bottom; H->dpi = win_dpi(H->hwnd); H->wp.length = sizeof H->wp; GetWindowPlacement(H->hwnd, &H->wp);
    if (s->fullscreen && !H->embed) host_toggle_fullscreen(H);
    H->xdll = LoadLibraryW(L"xinput1_4.dll"); if (!H->xdll) H->xdll = LoadLibraryW(L"xinput1_3.dll"); if (!H->xdll) H->xdll = LoadLibraryW(L"xinput9_1_0.dll");
    if (H->xdll) H->xget = (xget_fn)(void *)GetProcAddress(H->xdll, "XInputGetState");
    CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    if (FAILED(DirectInput8Create(GetModuleHandleW(NULL), DIRECTINPUT_VERSION, &IID_IDirectInput8W, (void **)&H->di, NULL))) H->di = NULL;
    enum_devices(H); H->last_enum = gp_ms(); H->focused = H->embed ? 0 : GetForegroundWindow() == H->hwnd;
    if (H->embed) { char hi[64]; snprintf(hi, sizeof hi, "hello %lu", GetCurrentProcessId()); host_send(H, hi); }
    host_trace(H, "{\"ev\":\"host\",\"kind\":\"windows\",\"client\":[%d,%d],\"xinput\":%d,\"directinput\":%d,\"focused\":%d,\"input\":\"%s\"}", H->cw, H->ch, H->xget != NULL, H->di != NULL, H->focused, H->posted_input ? "posted-window-messages (test)" : "keyboard and controllers");
    return H; }
void host_close(host *H) {
    if (!H) return; save_display(H); if (H->audio_on) { waveOutReset(H->wo); for (int i = 0; i < NBUF; i++) { waveOutUnprepareHeader(H->wo, &H->hdr[i], sizeof H->hdr[i]); free(H->pcm[i]); } waveOutClose(H->wo); }
    for (int i = 0; i < H->ndev; i++) if (H->dev[i].kind == 2) { IDirectInputDevice8_Unacquire(H->dev[i].dev); IDirectInputDevice8_Release(H->dev[i].dev); }
    if (H->di) IDirectInput8_Release(H->di); if (H->xdll) FreeLibrary(H->xdll);
    DestroyWindow(H->hwnd); timeEndPeriod(1); host_trace(H, "{\"ev\":\"host_close\"}"); if (H->trace) fclose(H->trace);
    if (H->mutex) { ReleaseMutex(H->mutex); CloseHandle(H->mutex); } if (H->shm) UnmapViewOfFile(H->shm); if (H->pipe) CloseHandle(H->pipe); if (H->studio_proc) CloseHandle(H->studio_proc);
    free(H->canvas32); free(H); G = NULL; }
/* the keys and the first two controllers into Players 1 and 2: controllers in connection order (swapped on request); the keyboard's keys
   (the settings' "p1" set) play the player no controller plays, its second set ("p2") only with no controller (gp_keyboard_players).
   Keys count while the window has focus (or test input is posted); controllers only while it has focus. */
static void route_inputs(host *H, gp_pad *pad) {
    int has[GP_PLAYERS] = { 0, 0 }, mp, sp, who[MAXDEV]; who_plays(H, who); for (int i = 0; i < H->ndev; i++) if (who[i] >= 0) has[who[i]] = 1; gp_keyboard_players(has, &mp, &sp);
    if (H->focused || H->posted_input) for (int a = 0; a < GA_COUNT; a++) for (int k = 0; k < 2; k++) {
        int v = H->vk[0][a][k]; if (H->text_on && v != VK_UP && v != VK_DOWN && v != VK_LEFT && v != VK_RIGHT) continue;   /* naming: typed keys are text */
        if (v && (H->keys[v] || H->tapped[v])) pad->down[mp][a] = 1;
        v = H->vk[1][a][k]; if (sp >= 0 && v && (H->keys[v] || H->tapped[v])) pad->down[sp][a] = 1; }
    memset(H->tapped, 0, sizeof H->tapped);
    if (H->focused) for (int a = 0; a < GA_COUNT; a++) if (H->vpad[a]) pad->down[mp][a] = 1;   /* the Preview's mock pad plays as the keyboard does */
    for (int i = 0; i < H->ndev; i++) { int p = who[i]; if (p < 0 || !H->focused) continue; for (int a = 0; a < GA_COUNT; a++) if (H->dev[i].act[a]) pad->down[p][a] = 1; } }
void host_poll(host *H, gp_pad *pad, host_events *ev) {
    double t0 = H->trace ? qms() : 0, t1, t2;
    MSG m; while (PeekMessageW(&m, NULL, 0, 0, PM_REMOVE)) { TranslateMessage(&m); DispatchMessageW(&m); }
    studio_read(H);
    if (H->studio_proc && gp_ms() - H->studio_check > 1000) { H->studio_check = gp_ms(); if (WaitForSingleObject(H->studio_proc, 0) == WAIT_OBJECT_0) { host_trace(H, "{\"ev\":\"studio_gone\",\"how\":\"process\"}"); CloseHandle(H->studio_proc); H->studio_proc = NULL; H->studio_exit = 3; } }
    t1 = H->trace ? qms() : 0; if (H->enum_due && gp_ms() - H->last_enum > 500) { H->enum_due = 0; enum_devices(H); H->last_enum = gp_ms(); }
    t2 = H->trace ? qms() : 0; read_pads(H); memset(pad, 0, sizeof *pad);
    if (H->trace) { double t3 = qms(); H->pf_msgs += t1 - t0; H->pf_enum += t2 - t1; H->pf_pads += t3 - t2; H->pf_polls++;
        if (!H->pf_t0) H->pf_t0 = gp_ms();
        else if (gp_ms() - H->pf_t0 >= 1000) { host_trace(H, "{\"ev\":\"host_perf\",\"polls\":%d,\"blits\":%d,\"msgs_ms\":%.1f,\"enum_ms\":%.1f,\"pads_ms\":%.1f,\"blit_ms\":%.1f,\"client\":[%d,%d]}",
            H->pf_polls, H->pf_blits, H->pf_msgs, H->pf_enum, H->pf_pads, H->pf_blit, H->cw, H->ch); H->pf_t0 = gp_ms(); H->pf_msgs = H->pf_enum = H->pf_pads = H->pf_blit = 0; H->pf_polls = H->pf_blits = 0; } }
    route_inputs(H, pad);
    memset(ev, 0, sizeof *ev); ev->close_requested = H->close_req; ev->menu_key = H->menu_key; ev->focus_lost = H->lost; ev->focus_gained = H->gained;
    memcpy(ev->pt, H->pq, sizeof ev->pt); ev->npt = H->npq; H->npq = 0;
    if (H->ntext) { memcpy(ev->text, H->text, sizeof ev->text); ev->ntext = H->ntext; H->ntext = 0; H->text[0] = 0; }
    ev->studio_exit = H->studio_exit; H->studio_exit = 0; ev->preview_close = H->preview_close; H->preview_close = 0; ev->music = H->music_cmd; H->music_cmd = -1;
    snprintf(ev->theme, sizeof ev->theme, "%s", H->theme_cmd); H->theme_cmd[0] = 0; snprintf(ev->select, sizeof ev->select, "%s", H->select_cmd); H->select_cmd[0] = 0; snprintf(ev->reload, sizeof ev->reload, "%s", H->reload_cmd); H->reload_cmd[0] = 0;
    H->close_req = H->menu_key = H->lost = H->gained = 0; }
void host_text_input(host *H, int on) { H->text_on = on; H->ntext = 0; host_trace(H, "{\"ev\":\"text_input\",\"on\":%d}", on); }
void host_set_title(host *H, const char *t) { wchar_t *w = wide(t); SetWindowTextW(H->hwnd, w); free(w); }
void host_set_picture(host *H, int tv_aspect, int integer_scale) { if (!H) return; H->tv_aspect = tv_aspect; H->integer_scale = integer_scale; InvalidateRect(H->hwnd, NULL, FALSE); }
static BOOL CALLBACK find_studio(HWND w, LPARAM lp) { host *H = (host *)lp; DWORD pid = 0; GetWindowThreadProcessId(w, &pid);
    if ((int)pid == H->studio_pid && IsWindowVisible(w) && !GetWindow(w, GW_OWNER)) { if (IsIconic(w)) ShowWindow(w, SW_RESTORE); SetForegroundWindow(w); host_trace(H, "{\"ev\":\"return_focus\",\"ok\":%d}", GetForegroundWindow() == w); return FALSE; } return TRUE; }
void host_return_focus(host *H) { if (H->studio_pid && (!H->embed || H->popped)) EnumWindows(find_studio, (LPARAM)H); }
