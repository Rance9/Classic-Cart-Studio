/* CCS Console Workbench native host: the setup window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
 *
 * What it is (host_setup.h): a dialog box (the Windows dialog class, so screen readers, Tab, Enter, Escape and access keys work as in
 * every Windows dialog), built from an empty template and filled with standard controls:
 *   heading, body, steps, offline note   static text, wrapped to the window's width
 *   status                               static text, a live region (screen readers announce it when it changes)
 *   Copy Address                         only after the browser could not be opened: copies the page's address
 *   Show/Hide technical details          shows a read-only box with the raw findings, and Copy Details
 *   open button, Continue, Cancel/Close  owner-drawn in Workbench's style; the primary one red
 * Look: Workbench's theme colors (Theme.xaml), its red dialog edge (2 pixels at 100%, all four sides, WindowFrame.cs) and, where Windows
 * has them, the dark title bar, the title bar color and the red outer edge of Windows 11. Size and fonts follow the display's scale
 * (per-monitor, also after a move to another display); the window is not resizable and grows when the details are shown.
 * DLLs: user32, gdi32, kernel32, shell32 (ShellExecuteExW), ole32/oleaut32 (the live region's property service); dwmapi and uxtheme are
 * looked up at run time (missing ones are skipped), so nothing here stops the host from starting on any Windows 10 or later. */
#define COBJMACROS
#include <windows.h>
#include <shellapi.h>
#include <oleacc.h>
#include <wchar.h>
#include "host_setup.h"

/* Workbench's theme (tools/desktop/ClassicCartStudio/Theme.xaml and the Workbench's buttons, Workbench.xaml), as GDI colors; the
 * translucent lines are mixed onto the color they sit on */
#define C_BG       RGB(0x10, 0x10, 0x14)
#define C_SURFACE  RGB(0x18, 0x18, 0x1E)
#define C_SURFACE2 RGB(0x1F, 0x1F, 0x26)
#define C_LINE2    RGB(0x49, 0x49, 0x4F)   /* #30FFFFFF on Surface2 */
#define C_BOXLINE  RGB(0x3D, 0x3D, 0x41)   /* #30FFFFFF on the background */
#define C_INK      RGB(0xF3, 0xF1, 0xEC)
#define C_INK2     RGB(0xD6, 0xD4, 0xCE)
#define C_MUTED    RGB(0x9C, 0x9A, 0x95)
#define C_RED      RGB(0xE4, 0x00, 0x0F)
#define C_RED2     RGB(0xB1, 0x00, 0x0C)
#define C_RED3     RGB(0xFF, 0x3B, 0x45)
#define C_REDLINE  RGB(0xFF, 0x5A, 0x63)
#define C_WARN     RGB(0xF0, 0xA0, 0x30)
#define C_HOVER    RGB(0x26, 0x26, 0x2E)
#define C_HOVERLN  RGB(0x8F, 0x8F, 0x92)   /* #80FFFFFF on the hover color */
#define C_PRESSED  RGB(0x14, 0x14, 0x1A)
#define CAPTION_COLORREF 0x00202024        /* the title bar color the Workbench's windows ask for */
#define EDGE_COLORREF    0x000F00E4        /* B.Red as COLORREF */

#define IDC_HEADING  2001
#define IDC_BODY     2002
#define IDC_STEPS    2003
#define IDC_OFFLINE  2004
#define IDC_STATUS   2005
#define IDC_COPYURL  2006
#define IDC_TOGGLE   2007
#define IDC_DETAILS  2008
#define IDC_COPY     2009
#define IDC_OPEN     2010
#define IDC_CONTINUE 2011
#define TIMER_COPIED 1

/* the live region and accessible-name property GUIDs (uiautomationcoreapi.h, oleacc.h), declared here so nothing extra is linked */
static const GUID LIVE_SETTING = { 0xc12bcd8e, 0x2a8e, 0x4950, { 0x8a, 0xe7, 0x36, 0x25, 0x11, 0x1d, 0x58, 0xeb } };
static const GUID ACC_NAME = { 0x608d3df8, 0x8128, 0x4aa7, { 0xa4, 0x28, 0xf5, 0x5e, 0x49, 0x26, 0x72, 0x91 } };
static const GUID CLSID_ACCPROP = { 0xb5f8350b, 0x0548, 0x48b1, { 0xa6, 0xee, 0x88, 0xbd, 0x00, 0xb4, 0xa5, 0xe7 } };
static const GUID IID_ACCPROP = { 0x6e26e776, 0x04f0, 0x495d, { 0x80, 0xe4, 0x33, 0x30, 0x35, 0x2e, 0x31, 0x69 } };

typedef struct setup_state {
    setup_view *v; setup_recheck_fn recheck; setup_open_fn open; void *ctx;
    HWND dlg, heading, body, steps, offline, status, copy_url, toggle, details, copy, b_open, b_continue, b_close;
    HFONT f_heading, f_body, f_small, f_status, f_button, f_mono;
    HBRUSH br_bg, br_surface, br_red, br_boxline;
    HICON icon_big, icon_small;
    UINT dpi;
    int details_open, busy, open_failed, placed, result;
    DWORD last_open;
    RECT box;            /* the line around the details box (client coordinates) */
    IAccPropServices *acc;
    HWND hover;
    wchar_t *details_crlf;
} setup_state;

static WNDPROC g_button_proc;

/* ---------- run-time lookups (a missing one is skipped) ---------- */

typedef UINT (WINAPI *PFN_GetDpiForWindow)(HWND);
typedef BOOL (WINAPI *PFN_AdjustWindowRectExForDpi)(LPRECT, DWORD, BOOL, DWORD, UINT);
typedef int (WINAPI *PFN_GetSystemMetricsForDpi)(int, UINT);
typedef BOOL (WINAPI *PFN_SetDialogDpiChangeBehavior)(HWND, int, int);
typedef BOOL (WINAPI *PFN_SetDialogControlDpiChangeBehavior)(HWND, int, int);
typedef HRESULT (WINAPI *PFN_DwmSetWindowAttribute)(HWND, DWORD, LPCVOID, DWORD);
typedef HRESULT (WINAPI *PFN_SetWindowTheme)(HWND, LPCWSTR, LPCWSTR);

static FARPROC user32_fn(const char *name) { return GetProcAddress(GetModuleHandleW(L"user32.dll"), name); }
static FARPROC system_fn(const wchar_t *dll, const char *name)
{
    HMODULE m = LoadLibraryExW(dll, NULL, LOAD_LIBRARY_SEARCH_SYSTEM32);
    return m ? GetProcAddress(m, name) : NULL;
}

static UINT window_dpi(HWND h)
{
    PFN_GetDpiForWindow f = (PFN_GetDpiForWindow)(void *)user32_fn("GetDpiForWindow");
    UINT d = f ? f(h) : 0;
    if (!d) { HDC dc = GetDC(NULL); d = (UINT)GetDeviceCaps(dc, LOGPIXELSX); ReleaseDC(NULL, dc); }
    return d ? d : 96;
}

static int px(const setup_state *s, int dip) { return MulDiv(dip, (int)s->dpi, 96); }

/* ---------- fonts ---------- */

static HFONT make_font(UINT dpi, int tenths, int weight, const wchar_t *face, const wchar_t *fallback)
{
    LOGFONTW lf; ZeroMemory(&lf, sizeof lf);
    lf.lfHeight = -MulDiv(tenths, (int)dpi, 960); lf.lfWeight = weight; lf.lfCharSet = DEFAULT_CHARSET;
    lf.lfOutPrecision = OUT_TT_PRECIS; lf.lfQuality = CLEARTYPE_QUALITY;
    lstrcpynW(lf.lfFaceName, face, LF_FACESIZE);
    HFONT f = CreateFontIndirectW(&lf);
    wchar_t got[LF_FACESIZE] = L"";
    HDC dc = GetDC(NULL); HGDIOBJ old = SelectObject(dc, f); GetTextFaceW(dc, LF_FACESIZE, got); SelectObject(dc, old); ReleaseDC(NULL, dc);
    if (_wcsicmp(got, face) != 0 && fallback)
    {   /* Windows substituted another face: use the named fallback instead (Segoe UI Variable is Windows 11's) */
        DeleteObject(f); lstrcpynW(lf.lfFaceName, fallback, LF_FACESIZE); f = CreateFontIndirectW(&lf);
    }
    return f;
}

static void free_fonts(setup_state *s)
{
    HFONT *all[] = { &s->f_heading, &s->f_body, &s->f_small, &s->f_status, &s->f_button, &s->f_mono };
    for (size_t i = 0; i < sizeof all / sizeof all[0]; i++) if (*all[i]) { DeleteObject(*all[i]); *all[i] = NULL; }
}

static void make_fonts(setup_state *s)
{
    free_fonts(s);
    s->f_heading = make_font(s->dpi, 170, FW_SEMIBOLD, L"Segoe UI Variable Display", L"Segoe UI");
    s->f_body = make_font(s->dpi, 135, FW_NORMAL, L"Segoe UI Variable Text", L"Segoe UI");
    s->f_small = make_font(s->dpi, 130, FW_NORMAL, L"Segoe UI Variable Text", L"Segoe UI");
    s->f_status = make_font(s->dpi, 130, s->v->status_warn ? FW_SEMIBOLD : FW_NORMAL, L"Segoe UI Variable Text", L"Segoe UI");
    s->f_button = make_font(s->dpi, 130, FW_SEMIBOLD, L"Segoe UI Variable Text", L"Segoe UI");
    s->f_mono = make_font(s->dpi, 120, FW_NORMAL, L"Cascadia Mono", L"Consolas");
    HWND text[] = { s->heading, s->body, s->steps, s->offline, s->status, s->details };
    HFONT font[] = { s->f_heading, s->f_body, s->f_small, s->f_small, s->f_status, s->f_mono };
    for (size_t i = 0; i < sizeof text / sizeof text[0]; i++) if (text[i]) SendMessageW(text[i], WM_SETFONT, (WPARAM)font[i], FALSE);
    HWND btn[] = { s->copy_url, s->toggle, s->copy, s->b_open, s->b_continue, s->b_close };
    for (size_t i = 0; i < sizeof btn / sizeof btn[0]; i++) if (btn[i]) SendMessageW(btn[i], WM_SETFONT, (WPARAM)s->f_button, FALSE);
    if (s->details) SendMessageW(s->details, EM_SETMARGINS, EC_LEFTMARGIN | EC_RIGHTMARGIN, MAKELPARAM(px(s, 8), px(s, 8)));
}

/* ---------- the title bar, the icon and the frame ---------- */

static void apply_frame(setup_state *s)
{
    PFN_DwmSetWindowAttribute dwm = (PFN_DwmSetWindowAttribute)(void *)system_fn(L"dwmapi.dll", "DwmSetWindowAttribute");
    if (dwm)
    {
        BOOL on = TRUE; int caption = CAPTION_COLORREF, edge = EDGE_COLORREF;
        if (FAILED(dwm(s->dlg, 20, &on, sizeof on))) dwm(s->dlg, 19, &on, sizeof on);   /* DWMWA_USE_IMMERSIVE_DARK_MODE (19 before 20H1) */
        dwm(s->dlg, 35, &caption, sizeof caption);                                       /* DWMWA_CAPTION_COLOR (Windows 11) */
        dwm(s->dlg, 34, &edge, sizeof edge);                                             /* DWMWA_BORDER_COLOR (Windows 11) */
    }
    PFN_GetSystemMetricsForDpi gsm = (PFN_GetSystemMetricsForDpi)(void *)user32_fn("GetSystemMetricsForDpi");
    int big = gsm ? gsm(SM_CXICON, s->dpi) : GetSystemMetrics(SM_CXICON), small = gsm ? gsm(SM_CXSMICON, s->dpi) : GetSystemMetrics(SM_CXSMICON);
    HICON ib = (HICON)LoadImageW(GetModuleHandleW(NULL), MAKEINTRESOURCEW(1), IMAGE_ICON, big, big, LR_DEFAULTCOLOR);
    HICON is = (HICON)LoadImageW(GetModuleHandleW(NULL), MAKEINTRESOURCEW(1), IMAGE_ICON, small, small, LR_DEFAULTCOLOR);
    if (ib) { SendMessageW(s->dlg, WM_SETICON, ICON_BIG, (LPARAM)ib); if (s->icon_big) DestroyIcon(s->icon_big); s->icon_big = ib; }
    if (is) { SendMessageW(s->dlg, WM_SETICON, ICON_SMALL, (LPARAM)is); if (s->icon_small) DestroyIcon(s->icon_small); s->icon_small = is; }
}

/* ---------- text and layout ---------- */

static void set_crlf_details(setup_state *s)
{
    const wchar_t *d = s->v->details;
    size_t n = wcslen(d), extra = 0;
    for (size_t i = 0; i < n; i++) if (d[i] == L'\n') extra++;
    wchar_t *b = (wchar_t *)HeapAlloc(GetProcessHeap(), 0, (n + extra + 1) * sizeof(wchar_t));
    if (!b) return;
    size_t o = 0;
    for (size_t i = 0; i < n; i++) { if (d[i] == L'\n' && (i == 0 || d[i - 1] != L'\r')) b[o++] = L'\r'; b[o++] = d[i]; }
    b[o] = 0;
    if (s->details_crlf) HeapFree(GetProcessHeap(), 0, s->details_crlf);
    s->details_crlf = b;
    if (s->details) SetWindowTextW(s->details, b);
}

static void set_texts(setup_state *s)
{
    setup_view *v = s->v;
    SetWindowTextW(s->dlg, v->title);
    SetWindowTextW(s->heading, v->heading);
    SetWindowTextW(s->body, v->body);
    SetWindowTextW(s->steps, v->steps);
    SetWindowTextW(s->offline, v->offline);
    SetWindowTextW(s->status, v->status);
    SetWindowTextW(s->b_open, v->open_label);
    SetWindowTextW(s->b_close, v->close_label);
    set_crlf_details(s);
    /* the status's weight follows whether it says something still stands in the way */
    if (s->f_status) DeleteObject(s->f_status);
    s->f_status = make_font(s->dpi, 130, v->status_warn ? FW_SEMIBOLD : FW_NORMAL, L"Segoe UI Variable Text", L"Segoe UI");
    SendMessageW(s->status, WM_SETFONT, (WPARAM)s->f_status, FALSE);
}

static int text_height(HDC dc, HFONT f, const wchar_t *t, int w)
{
    RECT r = { 0, 0, w, 0 };
    HGDIOBJ old = SelectObject(dc, f);
    DrawTextW(dc, t && *t ? t : L" ", -1, &r, DT_CALCRECT | DT_WORDBREAK | DT_EXPANDTABS | DT_NOPREFIX | DT_EDITCONTROL);
    SelectObject(dc, old);
    return r.bottom - r.top;
}

static int text_width(HDC dc, HFONT f, const wchar_t *t)
{
    RECT r = { 0, 0, 0, 0 };
    HGDIOBJ old = SelectObject(dc, f);
    DrawTextW(dc, t, -1, &r, DT_CALCRECT | DT_SINGLELINE);   /* an access key's & takes no room */
    SelectObject(dc, old);
    return r.right - r.left;
}

static int place_text(HDC dc, HWND h, HFONT f, const wchar_t *t, int x, int y, int w)
{
    if (!t || !*t) { ShowWindow(h, SW_HIDE); return y; }
    int ht = text_height(dc, f, t, w);
    SetWindowPos(h, NULL, x, y, w, ht, SWP_NOZORDER | SWP_NOACTIVATE);
    ShowWindow(h, SW_SHOWNA);
    return y + ht;
}

static int button_width(setup_state *s, HDC dc, HWND b, int min_dip)
{
    wchar_t t[128]; GetWindowTextW(b, t, 128);
    int w = text_width(dc, s->f_button, t) + 2 * px(s, 16);
    return w < px(s, min_dip) ? px(s, min_dip) : w;
}

static HWND primary_button(setup_state *s)
{
    switch (s->v->primary)
    {
    case SETUP_PRIMARY_OPEN: if (s->v->open_url[0]) return s->b_open; break;
    case SETUP_PRIMARY_CONTINUE: if (s->v->can_continue) return s->b_continue; break;
    default: break;
    }
    return s->b_close;
}

/* the window's monitor work area */
static RECT work_area(HMONITOR m)
{
    MONITORINFO mi; mi.cbSize = sizeof mi;
    RECT r = { 0, 0, 1024, 768 };
    if (m && GetMonitorInfoW(m, &mi)) r = mi.rcWork;
    return r;
}

/* the display Windows named when it started this program (File Explorer does), else the main display (as WindowFrame.cs places the
 * Workbench's main window) */
static HMONITOR start_monitor(void)
{
    STARTUPINFOW si; ZeroMemory(&si, sizeof si); si.cb = sizeof si; GetStartupInfoW(&si);
    MONITORINFO mi; mi.cbSize = sizeof mi;
    if ((si.dwFlags & 0x400 /* STARTF_USEMONITOR */) && si.hStdOutput && GetMonitorInfoW((HMONITOR)si.hStdOutput, &mi)) return (HMONITOR)si.hStdOutput;
    POINT o = { 0, 0 };
    return MonitorFromPoint(o, MONITOR_DEFAULTTOPRIMARY);
}

/* lays the controls out for the current texts and scale, sizes the window to them and keeps it inside its display: centered the first
 * time (and on center_on, after a move to another display), else the top stays where it is */
static void layout(setup_state *s, HMONITOR center_on)
{
    setup_view *v = s->v;
    HDC dc = GetDC(s->dlg);
    int W = px(s, 560), m = px(s, 24), w = W - 2 * m, y = px(s, 22);
    y = place_text(dc, s->heading, s->f_heading, v->heading, m, y, w) + px(s, 10);
    y = place_text(dc, s->body, s->f_body, v->body, m, y, w) + px(s, 12);
    if (v->steps[0]) y = place_text(dc, s->steps, s->f_small, v->steps, m, y, w) + px(s, 10); else ShowWindow(s->steps, SW_HIDE);
    if (v->offline[0]) y = place_text(dc, s->offline, s->f_small, v->offline, m, y, w) + px(s, 12); else ShowWindow(s->offline, SW_HIDE);
    if (v->status[0]) y = place_text(dc, s->status, s->f_status, v->status, m, y, w) + px(s, 10); else ShowWindow(s->status, SW_HIDE);
    int sh = px(s, 30);
    if (s->open_failed && v->open_url[0])
    {
        SetWindowPos(s->copy_url, NULL, m, y, button_width(s, dc, s->copy_url, 96), sh, SWP_NOZORDER | SWP_NOACTIVATE); ShowWindow(s->copy_url, SW_SHOWNA);
        y += sh + px(s, 10);
    }
    else ShowWindow(s->copy_url, SW_HIDE);
    /* the details switch: its words plus room for the chevron drawn before them */
    {
        wchar_t t[64]; GetWindowTextW(s->toggle, t, 64);
        int tw = text_width(dc, s->f_button, t) + px(s, 30);
        SetWindowPos(s->toggle, NULL, m - px(s, 6), y, tw, sh, SWP_NOZORDER | SWP_NOACTIVATE); ShowWindow(s->toggle, SW_SHOWNA);
        y += sh + px(s, 6);
    }
    if (s->details_open)
    {
        int bh = px(s, 150), e = 1 > px(s, 1) ? 1 : px(s, 1);
        SetRect(&s->box, m, y, m + w, y + bh);
        SetWindowPos(s->details, NULL, m + e, y + e, w - 2 * e, bh - 2 * e, SWP_NOZORDER | SWP_NOACTIVATE); ShowWindow(s->details, SW_SHOWNA);
        y += bh + px(s, 8);
        SetWindowPos(s->copy, NULL, m, y, button_width(s, dc, s->copy, 96), sh, SWP_NOZORDER | SWP_NOACTIVATE); ShowWindow(s->copy, SW_SHOWNA);
        y += sh + px(s, 6);
    }
    else { ShowWindow(s->details, SW_HIDE); ShowWindow(s->copy, SW_HIDE); SetRectEmpty(&s->box); }
    /* the buttons, at the right */
    y += px(s, 10);
    int bh = px(s, 36), gap = px(s, 8), x = W - m;
    HWND row[3] = { s->b_close, s->b_continue, s->b_open };
    int shown[3] = { 1, v->can_continue, v->open_url[0] != 0 };
    for (int i = 0; i < 3; i++)
    {
        if (!shown[i]) { ShowWindow(row[i], SW_HIDE); continue; }
        int bw = button_width(s, dc, row[i], 96);
        x -= bw;
        SetWindowPos(row[i], NULL, x, y, bw, bh, SWP_NOZORDER | SWP_NOACTIVATE); ShowWindow(row[i], SW_SHOWNA);
        x -= gap;
    }
    y += bh + px(s, 22);
    ReleaseDC(s->dlg, dc);

    /* the window around that client area */
    RECT r = { 0, 0, W, y };
    DWORD style = (DWORD)GetWindowLongW(s->dlg, GWL_STYLE), ex = (DWORD)GetWindowLongW(s->dlg, GWL_EXSTYLE);
    PFN_AdjustWindowRectExForDpi adj = (PFN_AdjustWindowRectExForDpi)(void *)user32_fn("AdjustWindowRectExForDpi");
    if (!adj || !adj(&r, style, FALSE, ex, s->dpi)) AdjustWindowRectEx(&r, style, FALSE, ex);
    int ww = r.right - r.left, wh = r.bottom - r.top;
    RECT now; GetWindowRect(s->dlg, &now);
    HMONITOR mon = center_on ? center_on : MonitorFromWindow(s->dlg, MONITOR_DEFAULTTONEAREST);
    RECT wa = work_area(mon);
    int nx, ny;
    if (center_on || !s->placed) { nx = wa.left + (wa.right - wa.left - ww) / 2; ny = wa.top + (wa.bottom - wa.top - wh) / 2; }
    else { nx = now.left; ny = now.top; }
    if (nx + ww > wa.right) nx = wa.right - ww;
    if (ny + wh > wa.bottom) ny = wa.bottom - wh;
    if (nx < wa.left) nx = wa.left;
    if (ny < wa.top) ny = wa.top;
    s->placed = 1;
    SetWindowPos(s->dlg, NULL, nx, ny, ww, wh, SWP_NOZORDER | SWP_NOACTIVATE);
    InvalidateRect(s->dlg, NULL, TRUE);
}

/* ---------- the status: a live region, so a screen reader says it when it changes ---------- */

static void announce(setup_state *s)
{
    if (s->v->status[0]) NotifyWinEvent(EVENT_OBJECT_LIVEREGIONCHANGED, s->status, OBJID_CLIENT, CHILDID_SELF);
}

static void accessibility(setup_state *s)
{
    if (FAILED(CoCreateInstance(&CLSID_ACCPROP, NULL, CLSCTX_INPROC_SERVER, &IID_ACCPROP, (void **)&s->acc)) || !s->acc) { s->acc = NULL; return; }
    VARIANT polite; VariantInit(&polite); polite.vt = VT_I4; polite.lVal = 1;   /* LiveSetting: Polite */
    IAccPropServices_SetHwndProp(s->acc, s->status, (DWORD)OBJID_CLIENT, CHILDID_SELF, LIVE_SETTING, polite);
    IAccPropServices_SetHwndPropStr(s->acc, s->details, (DWORD)OBJID_CLIENT, CHILDID_SELF, ACC_NAME, L"Technical details");
}

static void accessibility_end(setup_state *s)
{
    if (!s->acc) return;
    MSAAPROPID live = LIVE_SETTING, name = ACC_NAME;
    IAccPropServices_ClearHwndProps(s->acc, s->status, (DWORD)OBJID_CLIENT, CHILDID_SELF, &live, 1);
    IAccPropServices_ClearHwndProps(s->acc, s->details, (DWORD)OBJID_CLIENT, CHILDID_SELF, &name, 1);
    IAccPropServices_Release(s->acc); s->acc = NULL;
}

/* ---------- the clipboard ---------- */

static int to_clipboard(HWND owner, const wchar_t *text)
{
    size_t n = wcslen(text) + 1;
    HGLOBAL g = GlobalAlloc(GMEM_MOVEABLE, n * sizeof(wchar_t));
    if (!g) return 0;
    void *p = GlobalLock(g); if (!p) { GlobalFree(g); return 0; }
    memcpy(p, text, n * sizeof(wchar_t)); GlobalUnlock(g);
    if (!OpenClipboard(owner)) { GlobalFree(g); return 0; }
    EmptyClipboard();
    if (!SetClipboardData(CF_UNICODETEXT, g)) { CloseClipboard(); GlobalFree(g); return 0; }
    CloseClipboard();
    return 1;
}

static void copy_details(setup_state *s)
{
    setup_view *v = s->v;
    size_t cap = wcslen(v->heading) + wcslen(v->body) + wcslen(v->status) + (s->details_crlf ? wcslen(s->details_crlf) : 0) + 16;
    wchar_t *t = (wchar_t *)HeapAlloc(GetProcessHeap(), 0, cap * sizeof(wchar_t));
    if (!t) return;
    t[0] = 0;
    wcscat(t, v->heading); wcscat(t, L"\r\n"); wcscat(t, v->body); wcscat(t, L"\r\n");
    if (v->status[0]) { wcscat(t, v->status); wcscat(t, L"\r\n"); }
    wcscat(t, L"\r\n"); if (s->details_crlf) wcscat(t, s->details_crlf);
    if (to_clipboard(s->dlg, t)) { SetWindowTextW(s->copy, L"Copied"); SetTimer(s->dlg, TIMER_COPIED, 1500, NULL); }
    HeapFree(GetProcessHeap(), 0, t);
}

/* ---------- drawing ---------- */

static COLORREF mix(COLORREF a, COLORREF b, int pct_a)
{
    return RGB((GetRValue(a) * pct_a + GetRValue(b) * (100 - pct_a)) / 100, (GetGValue(a) * pct_a + GetGValue(b) * (100 - pct_a)) / 100,
               (GetBValue(a) * pct_a + GetBValue(b) * (100 - pct_a)) / 100);
}

static void round_box(HDC dc, RECT r, COLORREF fill, COLORREF line, int line_w, int radius, int filled)
{
    HPEN pen = CreatePen(PS_INSIDEFRAME, line_w, line);   /* the line stays inside the button's own rectangle */
    HBRUSH br = filled ? CreateSolidBrush(fill) : (HBRUSH)GetStockObject(NULL_BRUSH);
    HGDIOBJ op = SelectObject(dc, pen), ob = SelectObject(dc, br);
    RoundRect(dc, r.left, r.top, r.right, r.bottom, radius * 2, radius * 2);
    SelectObject(dc, op); SelectObject(dc, ob);
    DeleteObject(pen); if (filled) DeleteObject(br);
}

static void draw_item(setup_state *s, const DRAWITEMSTRUCT *d)
{
    HDC dc = d->hDC; RECT r = d->rcItem;
    int down = (d->itemState & ODS_SELECTED) != 0, disabled = (d->itemState & ODS_DISABLED) != 0;
    int focus = (d->itemState & ODS_FOCUS) && !(d->itemState & ODS_NOFOCUSRECT);
    int hot = s->hover == d->hwndItem && !disabled;
    UINT keys = (d->itemState & ODS_NOACCEL) ? DT_HIDEPREFIX : 0;
    wchar_t t[128]; GetWindowTextW(d->hwndItem, t, 128);
    FillRect(dc, &r, s->br_bg);
    SetBkMode(dc, TRANSPARENT);
    int line_w = px(s, 1) < 1 ? 1 : px(s, 1), focus_w = px(s, 2) < 2 ? 2 : px(s, 2);
    if (d->CtlID == IDC_TOGGLE)
    {   /* a text switch with a chevron: right when the details are hidden, down when shown */
        COLORREF ink = hot ? C_INK : C_INK2;
        int cx = r.left + px(s, 14), cy = (r.top + r.bottom) / 2, a = px(s, 4);
        POINT tri[3];
        if (s->details_open) { tri[0].x = cx - a; tri[0].y = cy - a / 2; tri[1].x = cx + a; tri[1].y = cy - a / 2; tri[2].x = cx; tri[2].y = cy + a; }
        else { tri[0].x = cx - a / 2; tri[0].y = cy - a; tri[1].x = cx - a / 2; tri[1].y = cy + a; tri[2].x = cx + a; tri[2].y = cy; }
        HBRUSH br = CreateSolidBrush(ink); HPEN pen = CreatePen(PS_SOLID, 1, ink);
        HGDIOBJ ob = SelectObject(dc, br), op = SelectObject(dc, pen);
        Polygon(dc, tri, 3);
        SelectObject(dc, ob); SelectObject(dc, op); DeleteObject(br); DeleteObject(pen);
        RECT tr = r; tr.left += px(s, 26);
        HGDIOBJ of = SelectObject(dc, s->f_button); SetTextColor(dc, ink);
        DrawTextW(dc, t, -1, &tr, DT_LEFT | DT_VCENTER | DT_SINGLELINE | keys);
        SelectObject(dc, of);
        if (focus) round_box(dc, r, 0, C_RED3, focus_w, px(s, 6), 0);
        return;
    }
    int primary = d->hwndItem == primary_button(s);
    COLORREF fill, line, ink;
    if (primary) { fill = down ? C_RED2 : hot ? C_RED3 : C_RED; line = C_REDLINE; ink = RGB(0xFF, 0xFF, 0xFF); }
    else { fill = down ? C_PRESSED : hot ? C_HOVER : C_SURFACE2; line = hot ? C_HOVERLN : C_LINE2; ink = C_INK; }
    if (disabled) { fill = mix(fill, C_BG, 45); line = mix(line, C_BG, 45); ink = mix(ink, C_BG, 45); }
    if (focus) line = primary ? RGB(0xFF, 0xFF, 0xFF) : C_RED3;
    round_box(dc, r, fill, line, focus ? focus_w : line_w, px(s, 8), 1);
    HGDIOBJ of = SelectObject(dc, s->f_button); SetTextColor(dc, ink);
    DrawTextW(dc, t, -1, &r, DT_CENTER | DT_VCENTER | DT_SINGLELINE | keys);
    SelectObject(dc, of);
}

static void paint(setup_state *s)
{
    PAINTSTRUCT ps; HDC dc = BeginPaint(s->dlg, &ps);
    RECT c; GetClientRect(s->dlg, &c);
    int e = px(s, 2) < 1 ? 1 : px(s, 2);   /* the dialog's red edge, all four sides */
    RECT t = { 0, 0, c.right, e }, b = { 0, c.bottom - e, c.right, c.bottom }, l = { 0, 0, e, c.bottom }, r = { c.right - e, 0, c.right, c.bottom };
    FillRect(dc, &t, s->br_red); FillRect(dc, &b, s->br_red); FillRect(dc, &l, s->br_red); FillRect(dc, &r, s->br_red);
    if (!IsRectEmpty(&s->box)) FrameRect(dc, &s->box, s->br_boxline);
    EndPaint(s->dlg, &ps);
}

/* ---------- buttons: hover ---------- */

static LRESULT CALLBACK button_proc(HWND h, UINT msg, WPARAM wp, LPARAM lp)
{
    setup_state *s = (setup_state *)GetWindowLongPtrW(GetParent(h), DWLP_USER);
    if (s && msg == WM_MOUSEMOVE && s->hover != h)
    {
        HWND was = s->hover; s->hover = h;
        TRACKMOUSEEVENT tme; tme.cbSize = sizeof tme; tme.dwFlags = TME_LEAVE; tme.hwndTrack = h; tme.dwHoverTime = 0; TrackMouseEvent(&tme);
        InvalidateRect(h, NULL, FALSE); if (was) InvalidateRect(was, NULL, FALSE);
    }
    else if (s && msg == WM_MOUSELEAVE && s->hover == h) { s->hover = NULL; InvalidateRect(h, NULL, FALSE); }
    return CallWindowProcW(g_button_proc, h, msg, wp, lp);
}

static HWND make_button(setup_state *s, const wchar_t *text, int id)
{
    HWND b = CreateWindowExW(0, L"BUTTON", text, WS_CHILD | WS_TABSTOP | BS_OWNERDRAW, 0, 0, 0, 0, s->dlg, (HMENU)(INT_PTR)id, GetModuleHandleW(NULL), NULL);
    if (b) { WNDPROC old = (WNDPROC)SetWindowLongPtrW(b, GWLP_WNDPROC, (LONG_PTR)button_proc); if (!g_button_proc) g_button_proc = old; }
    return b;
}

static HWND make_text(setup_state *s, int id)
{
    return CreateWindowExW(0, L"STATIC", L"", WS_CHILD | SS_LEFT | SS_NOPREFIX | SS_EDITCONTROL, 0, 0, 0, 0, s->dlg, (HMENU)(INT_PTR)id, GetModuleHandleW(NULL), NULL);
}

/* ---------- actions ---------- */

static void enable_actions(setup_state *s, BOOL on)
{
    HWND all[] = { s->b_open, s->b_continue, s->b_close, s->toggle, s->copy, s->copy_url };
    for (size_t i = 0; i < sizeof all / sizeof all[0]; i++) EnableWindow(all[i], on);
}

static void focus_on(setup_state *s, HWND h) { SendMessageW(s->dlg, WM_NEXTDLGCTL, (WPARAM)h, TRUE); }

static void refresh(setup_state *s, int announce_status)
{
    set_texts(s);
    layout(s, NULL);
    HWND f = GetFocus();
    if (!f || !IsWindowVisible(f) || !IsWindowEnabled(f)) focus_on(s, primary_button(s));
    InvalidateRect(s->b_open, NULL, FALSE); InvalidateRect(s->b_continue, NULL, FALSE); InvalidateRect(s->b_close, NULL, FALSE);
    if (announce_status) announce(s);
}

static void do_continue(setup_state *s)
{
    if (s->busy || !s->recheck) return;
    s->busy = 1;
    enable_actions(s, FALSE);
    lstrcpynW(s->v->status, L"Checking this PC again\x2026", SETUP_TEXT); s->v->status_warn = 0;
    set_texts(s); layout(s, NULL); UpdateWindow(s->dlg);
    int ready = s->recheck(s->v, s->ctx);
    s->busy = 0;
    if (ready) { s->result = SETUP_READY; EndDialog(s->dlg, SETUP_READY); return; }   /* the launch goes on, once */
    s->open_failed = 0;
    enable_actions(s, TRUE);
    refresh(s, 1);
    focus_on(s, primary_button(s));
}

static void do_open(setup_state *s)
{
    if (!s->v->open_url[0] || !s->open) return;
    DWORD now = GetTickCount();
    if (s->last_open && now - s->last_open < 1500) return;   /* one request per press, not one per bounce */
    s->last_open = now;
    wchar_t why[300] = L"";
    if (s->open(s->v->open_url, why, 300, s->ctx))
    {
        s->open_failed = 0;
        lstrcpynW(s->v->status, s->v->open_done, SETUP_TEXT); s->v->status_warn = 0;
        refresh(s, 1);
        if (s->v->can_continue) focus_on(s, s->b_continue);
    }
    else
    {
        s->open_failed = 1;
        swprintf(s->v->status, SETUP_TEXT, L"Your web browser could not be opened (%ls). Open this address in a web browser yourself: %ls "
                 L"Copy Address copies it.", why[0] ? why : L"Windows gave no reason", s->v->open_url);
        s->v->status_warn = 1;
        refresh(s, 1);
        focus_on(s, s->copy_url);
    }
}

static void do_toggle(setup_state *s)
{
    s->details_open = !s->details_open;
    SetWindowTextW(s->toggle, s->details_open ? L"Hide &technical details" : L"Show &technical details");
    layout(s, NULL);
    InvalidateRect(s->toggle, NULL, FALSE);
}

/* ---------- the dialog ---------- */

static void init(setup_state *s, HWND dlg)
{
    s->dlg = dlg;
    SetWindowLongPtrW(dlg, DWLP_USER, (LONG_PTR)s);
    SetPropW(dlg, SETUP_WINDOW_PROP, (HANDLE)1);
    /* this layout is the window's own at every scale: the dialog manager's automatic rescaling is turned off (Windows 10 1703 and later) */
    PFN_SetDialogDpiChangeBehavior ddc = (PFN_SetDialogDpiChangeBehavior)(void *)user32_fn("SetDialogDpiChangeBehavior");
    if (ddc) ddc(dlg, 7, 7);   /* DDC_DISABLE_ALL | DDC_DISABLE_RESIZE | DDC_DISABLE_CONTROL_RELAYOUT */
    s->dpi = window_dpi(dlg);
    s->br_bg = CreateSolidBrush(C_BG); s->br_surface = CreateSolidBrush(C_SURFACE); s->br_red = CreateSolidBrush(C_RED); s->br_boxline = CreateSolidBrush(C_BOXLINE);
    s->heading = make_text(s, IDC_HEADING); s->body = make_text(s, IDC_BODY); s->steps = make_text(s, IDC_STEPS);
    s->offline = make_text(s, IDC_OFFLINE); s->status = make_text(s, IDC_STATUS);
    s->copy_url = make_button(s, L"Copy &Address", IDC_COPYURL);
    s->toggle = make_button(s, L"Show &technical details", IDC_TOGGLE);
    s->details = CreateWindowExW(0, L"EDIT", L"", WS_CHILD | WS_TABSTOP | WS_VSCROLL | ES_MULTILINE | ES_READONLY | ES_AUTOVSCROLL, 0, 0, 0, 0,
                                 dlg, (HMENU)(INT_PTR)IDC_DETAILS, GetModuleHandleW(NULL), NULL);
    s->copy = make_button(s, L"Cop&y Details", IDC_COPY);
    s->b_open = make_button(s, L"", IDC_OPEN);
    s->b_continue = make_button(s, L"&Continue", IDC_CONTINUE);
    s->b_close = make_button(s, L"", IDCANCEL);
    PFN_SetDialogControlDpiChangeBehavior dcc = (PFN_SetDialogControlDpiChangeBehavior)(void *)user32_fn("SetDialogControlDpiChangeBehavior");
    if (dcc) { HWND all[] = { s->heading, s->body, s->steps, s->offline, s->status, s->copy_url, s->toggle, s->details, s->copy, s->b_open, s->b_continue, s->b_close };
               for (size_t i = 0; i < sizeof all / sizeof all[0]; i++) dcc(all[i], 3 /* DCDC_DISABLE_FONTUPDATE | DCDC_DISABLE_RELAYOUT */, 3); }
    PFN_SetWindowTheme theme = (PFN_SetWindowTheme)(void *)system_fn(L"uxtheme.dll", "SetWindowTheme");
    if (theme) theme(s->details, L"DarkMode_Explorer", NULL);   /* a dark scroll bar where Windows has one */
    make_fonts(s);
    set_texts(s);
    apply_frame(s);
    accessibility(s);
    layout(s, start_monitor());   /* a move to a display with another scale comes back through WM_DPICHANGED */
    focus_on(s, primary_button(s));
    announce(s);
}

static void finish(setup_state *s)
{
    KillTimer(s->dlg, TIMER_COPIED);
    accessibility_end(s);
    RemovePropW(s->dlg, SETUP_WINDOW_PROP);
    free_fonts(s);
    HBRUSH b[] = { s->br_bg, s->br_surface, s->br_red, s->br_boxline };
    for (size_t i = 0; i < sizeof b / sizeof b[0]; i++) if (b[i]) DeleteObject(b[i]);
    if (s->icon_big) DestroyIcon(s->icon_big);
    if (s->icon_small) DestroyIcon(s->icon_small);
    if (s->details_crlf) HeapFree(GetProcessHeap(), 0, s->details_crlf);
}

static INT_PTR CALLBACK setup_proc(HWND dlg, UINT msg, WPARAM wp, LPARAM lp)
{
    setup_state *s = (setup_state *)GetWindowLongPtrW(dlg, DWLP_USER);
    switch (msg)
    {
    case WM_INITDIALOG: init((setup_state *)lp, dlg); return FALSE;   /* the focus is set */
    case WM_CTLCOLORDLG: return s ? (INT_PTR)s->br_bg : 0;
    case WM_CTLCOLORSTATIC:
    {
        if (!s) return 0;
        HDC dc = (HDC)wp; HWND c = (HWND)lp;
        if (c == s->details) { SetTextColor(dc, C_INK2); SetBkColor(dc, C_SURFACE); return (INT_PTR)s->br_surface; }
        SetTextColor(dc, c == s->status ? (s->v->status_warn ? C_WARN : C_INK2) : c == s->steps ? C_INK2 : c == s->offline ? C_MUTED : C_INK);
        SetBkColor(dc, C_BG);
        return (INT_PTR)s->br_bg;
    }
    case WM_DRAWITEM: if (s) { draw_item(s, (const DRAWITEMSTRUCT *)lp); return TRUE; } return FALSE;
    case WM_PAINT: if (s) { paint(s); return TRUE; } return FALSE;
    case WM_COMMAND:
    {
        if (!s) return FALSE;
        int id = LOWORD(wp);
        if (id == IDOK)
        {   /* Enter: the focused button, else the primary one */
            HWND f = GetFocus();
            HWND btn[] = { s->b_open, s->b_continue, s->b_close, s->toggle, s->copy, s->copy_url };
            HWND target = primary_button(s);
            for (size_t i = 0; i < sizeof btn / sizeof btn[0]; i++) if (f == btn[i]) target = f;
            if (IsWindowVisible(target) && IsWindowEnabled(target)) id = GetDlgCtrlID(target); else return TRUE;
        }
        if (HIWORD(wp) != BN_CLICKED && LOWORD(wp) != IDOK && LOWORD(wp) != IDCANCEL) return FALSE;
        switch (id)
        {
        case IDC_OPEN: do_open(s); return TRUE;
        case IDC_CONTINUE: do_continue(s); return TRUE;
        case IDCANCEL: if (!s->busy) { s->result = SETUP_CLOSED; EndDialog(dlg, SETUP_CLOSED); } return TRUE;
        case IDC_TOGGLE: do_toggle(s); return TRUE;
        case IDC_COPY: copy_details(s); return TRUE;
        case IDC_COPYURL: if (to_clipboard(dlg, s->v->open_url)) { SetWindowTextW(s->copy_url, L"Copied"); SetTimer(dlg, TIMER_COPIED, 1500, NULL); } return TRUE;
        }
        return FALSE;
    }
    case WM_TIMER:
        if (s && wp == TIMER_COPIED) { KillTimer(dlg, TIMER_COPIED); SetWindowTextW(s->copy, L"Cop&y Details"); SetWindowTextW(s->copy_url, L"Copy &Address");
                                       InvalidateRect(s->copy, NULL, FALSE); InvalidateRect(s->copy_url, NULL, FALSE); return TRUE; }
        return FALSE;
    case WM_DPICHANGED:
        if (s)
        {
            s->dpi = HIWORD(wp);
            const RECT *suggested = (const RECT *)lp;
            make_fonts(s); apply_frame(s);
            layout(s, MonitorFromRect(suggested, MONITOR_DEFAULTTONEAREST));
            return TRUE;
        }
        return FALSE;
    case WM_SETTINGCHANGE: case WM_THEMECHANGED: case 0x031E /* WM_DWMCOMPOSITIONCHANGED */: case 0x0320 /* WM_DWMCOLORIZATIONCOLORCHANGED */:
        if (s) apply_frame(s);
        return FALSE;
    case WM_DESTROY: if (s) finish(s); return FALSE;
    }
    return FALSE;
}

int setup_window_run(setup_view *v, setup_recheck_fn recheck, setup_open_fn open, void *ctx)
{
    /* an empty dialog template: the window is a standard dialog box with no controls of the template's own (they are made in init) */
    static WORD tmpl[16] __attribute__((aligned(4)));
    ZeroMemory(tmpl, sizeof tmpl);
    DLGTEMPLATE *t = (DLGTEMPLATE *)tmpl;
    t->style = WS_POPUP | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX | DS_SETFOREGROUND;
    t->dwExtendedStyle = WS_EX_APPWINDOW | WS_EX_CONTROLPARENT;
    t->cdit = 0;   /* then: no menu, the standard dialog class, an empty title (set from the view) */
    setup_state s; ZeroMemory(&s, sizeof s);
    s.v = v; s.recheck = recheck; s.open = open; s.ctx = ctx; s.result = SETUP_CLOSED;
    INT_PTR r = DialogBoxIndirectParamW(GetModuleHandleW(NULL), t, NULL, setup_proc, (LPARAM)&s);
    if (r == -1) return -1;
    return r == SETUP_READY ? SETUP_READY : SETUP_CLOSED;
}

static BOOL CALLBACK find_setup(HWND h, LPARAM lp)
{
    if (GetPropW(h, SETUP_WINDOW_PROP)) { *(HWND *)lp = h; return FALSE; }
    return TRUE;
}

int setup_window_bring_forward(void)
{
    HWND h = NULL;
    EnumWindows(find_setup, (LPARAM)&h);
    if (!h) return 0;
    if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
    SetForegroundWindow(h);
    return 1;
}
