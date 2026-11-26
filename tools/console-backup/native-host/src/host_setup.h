/* CCS Console Workbench native host: the setup window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
 *
 * A small window in Workbench's own look (dark, the red accent, a dialog's red edge, a dark title bar), drawn with plain Win32 and GDI
 * because it is shown BEFORE .NET Framework or WPF is loaded: when this PC lacks the .NET Framework Workbench needs, when this copy of
 * Workbench is damaged, or when Workbench could not start. host_main.c decides what it says (the facts, the words, the address it opens);
 * this file only shows it, opens the address on request, and asks host_main.c to check again on Continue. It stays open until the person
 * closes it or the check finds everything Workbench needs; it never downloads, installs, restarts or changes anything on the PC. */
#ifndef CCS_HOST_SETUP_H
#define CCS_HOST_SETUP_H

#include <windows.h>

#define SETUP_TEXT    1400
#define SETUP_DETAILS 6000

enum { SETUP_PRIMARY_OPEN = 0, SETUP_PRIMARY_CONTINUE = 1, SETUP_PRIMARY_CLOSE = 2 };
enum { SETUP_CLOSED = 0, SETUP_READY = 1 };

typedef struct setup_view {
    wchar_t title[96];              /* the title bar */
    wchar_t heading[200];
    wchar_t body[SETUP_TEXT];
    wchar_t steps[SETUP_TEXT];      /* how to go on (may be empty) */
    wchar_t offline[SETUP_TEXT];    /* without internet on this PC (may be empty) */
    wchar_t status[SETUP_TEXT];     /* what this PC has, what remains, what just happened (may be empty) */
    int status_warn;                /* the status says something still stands in the way */
    wchar_t details[SETUP_DETAILS]; /* Technical details: the raw findings, lines separated by \n */
    wchar_t open_url[300];          /* the page the open button opens (empty: no open button) */
    wchar_t open_label[64];         /* that button's words, with its access key */
    wchar_t open_done[SETUP_TEXT];  /* the status once Windows has been asked to open the page */
    int can_continue;               /* a Continue button (checks again) */
    wchar_t close_label[32];        /* Cancel or Close */
    int primary;                    /* SETUP_PRIMARY_*: the button drawn in red, focused first, and pressed by Enter */
} setup_view;

/* Continue: check again and update v; 1 = everything is there (the window closes and the launch goes on), 0 = v says what remains */
typedef int (*setup_recheck_fn)(setup_view *v, void *ctx);
/* the open button: ask Windows to open url in the person's browser; 1 = Windows took the request, 0 = why (filled) */
typedef int (*setup_open_fn)(const wchar_t *url, wchar_t *why, size_t cap, void *ctx);

/* shows the window and returns SETUP_READY or SETUP_CLOSED; -1 if Windows could not create it. Call on a thread with COM initialized
 * (single-threaded apartment). The window carries the property SETUP_WINDOW_PROP, so a second start can bring it forward. */
#define SETUP_WINDOW_PROP L"CcsConsoleWorkbench.Setup"
int setup_window_run(setup_view *v, setup_recheck_fn recheck, setup_open_fn open, void *ctx);

/* brings an open setup window of another Workbench start forward; 1 if there was one */
int setup_window_bring_forward(void);

#endif
