/* ccs-player: Classic Cart Studio's game player (one window, Direct Game Mode and HOME Mode). The Studio's Background Helper writes a
 * session file (validated games, exact ROM identities, the folders to use) and starts this program with it; nothing else is accepted
 * from the command line, and nothing is ever run from it.
 *   ccs-player --session <session.json> [--trace <file>] [--script <file> --shots <dir>]   (--script/--shots: tests, headless build) */
#include "host.h"
#include <stdio.h>
#include <string.h>
#include <stdlib.h>
#ifdef _WIN32
#include <windows.h>
#include <shellapi.h>
#endif
int gp_run(const char *session_path, const host_config *hc);
#if defined(CCS_CONSOLE)
int host_dev_card(int argc, char **argv);
#endif
static int run(int argc, char **argv) {
    const char *session = NULL; host_config hc; memset(&hc, 0, sizeof hc);
#if defined(CCS_CONSOLE)   /* the console player: its own options go to its host (host_dev.c), which refuses any it does not know; --card shows a text file */
    for (int i = 1; i < argc; i++) if (!strcmp(argv[i], "--card")) return host_dev_card(argc, argv);
    char **dev = calloc((size_t)argc + 1, sizeof *dev); int nd = 0;
    for (int i = 1; i < argc; i++) { if (!strcmp(argv[i], "--session") || !strcmp(argv[i], "--trace")) { if (i + 1 < argc) { if (!strcmp(argv[i], "--session")) session = argv[i + 1]; else hc.trace_path = argv[i + 1]; } i++; } else if (dev) dev[nd++] = argv[i]; }
    hc.args = dev; hc.nargs = nd;
    if (!session) { fprintf(stderr, "usage: ccs-player-armv7 --session <session.json> [--trace <file>] [console options]\n"); return 2; }
    return gp_run(session, &hc);
#endif
    for (int i = 1; i < argc; i++) if (!strcmp(argv[i], "--test-posted-input")) { hc.posted_input = 1; for (int k = i; k + 1 < argc; k++) argv[k] = argv[k + 1]; argc--; break; }   /* tests: keyboard messages posted to the window count without focus (labeled in the trace) */
    for (int i = 1; i + 1 < argc; i += 2) {
        if (!strcmp(argv[i], "--session")) session = argv[i + 1]; else if (!strcmp(argv[i], "--trace")) hc.trace_path = argv[i + 1];
        else if (!strcmp(argv[i], "--script")) hc.script_path = argv[i + 1]; else if (!strcmp(argv[i], "--shots")) hc.shot_dir = argv[i + 1];
        else { fprintf(stderr, "ccs-player: unknown option %s\n", argv[i]); return 2; } }
    if (!session) { fprintf(stderr, "usage: ccs-player --session <session.json>\n"); return 2; }
    return gp_run(session, &hc); }
#ifdef _WIN32
int main(void) {   /* UTF-8 arguments from the wide command line (user names and folders need not be ASCII) */
    int n; LPWSTR *w = CommandLineToArgvW(GetCommandLineW(), &n); char **a = calloc((size_t)n + 1, sizeof *a);
    for (int i = 0; i < n; i++) { int k = WideCharToMultiByte(CP_UTF8, 0, w[i], -1, NULL, 0, NULL, NULL); a[i] = malloc((size_t)k); WideCharToMultiByte(CP_UTF8, 0, w[i], -1, a[i], k, NULL, NULL); }
    LocalFree(w); return run(n, a); }
#else
int main(int argc, char **argv) { return run(argc, argv); }
#endif
