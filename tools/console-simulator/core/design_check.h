/* The HOME Designer's render check: before any write, a compiled design is loaded, decoded and drawn with this native HOME in every
 * theme and every required screen and state, next to the same screens without it (tools/console-backup/HOME-DESIGNER.md).
 *
 * Stable entry point (plain C; static-linkable into the standalone Workbench's native host and reachable through its ABI table, and
 * built into ccs-sim-core --render-check for Classic Cart Studio for Windows and the tests). One call at a time per process (the
 * renderer's probes are process-wide); call it from a background thread. Paths are UTF-8 (wide file names on Windows). */
#ifndef CCS_DESIGN_CHECK_H
#define CCS_DESIGN_CHECK_H
#include <stdint.h>

#define CCS_HOME_CHECK_ABI 1u
#define CCS_HOME_CHECK_FRAMES 1u           /* call on_frame (and write frames_dir) for every state drawn with the design */
#define CCS_HOME_CHECK_DEFAULT_FRAMES 2u   /* also for every state drawn with the theme defaults */
/* results */
#define CCS_HOME_CHECK_PASSED 0
#define CCS_HOME_CHECK_FAILED 1           /* the design must not be applied: the report lists each problem */
#define CCS_HOME_CHECK_ERROR 2            /* the check could not run (a package or folder that cannot be read; out of memory) */
#define CCS_HOME_CHECK_SMALL 3            /* report_cap too small: *report_len says how many bytes are needed */
#define CCS_HOME_CHECK_CANCELLED 4
#define CCS_HOME_CHECK_BAD_REQUEST 5

/* one drawn state: theme and state ids (ASCII), the frame (RGB, 3 bytes a pixel, w x h); return nonzero to stop the check */
typedef int32_t (*ccs_home_frame_fn)(void *ctx, const char *theme, const char *state, int32_t with_design, const uint8_t *rgb, int32_t w, int32_t h);

typedef struct ccs_home_check_request {
    uint32_t size;              /* sizeof(ccs_home_check_request) */
    uint32_t flags;             /* CCS_HOME_CHECK_* */
    const char *pack_dir;       /* the HOME package the console runs: its themes, fonts and games */
    const char *design_dir;     /* the compiled design; NULL: only the theme defaults are drawn and checked */
    const char *work_dir;       /* an existing empty folder the check writes into (the HOME's choice files, the missing-picture copy) */
    const char *frames_dir;     /* NULL, or an existing folder for <with|without>-<theme>-<state>.ppm */
    ccs_home_frame_fn on_frame; void *ctx;
    volatile int32_t *cancel;   /* NULL, or set nonzero to stop between states */
} ccs_home_check_request;

/* report: UTF-8 JSON (schema ccs-home-render-check/1), NUL-terminated, at most report_cap bytes; *report_len its length */
int32_t ccs_home_render_check(const ccs_home_check_request *req, char *report, uint32_t report_cap, uint32_t *report_len);
/* a cold start with the console's design folder (current -> known-good -> defaults): which design came up and its first frame's
   SHA-256, as JSON (schema ccs-home-design-boot/1) */
int32_t ccs_home_design_boot(const char *pack_dir, const char *design_root, const char *state_dir, const char *frame_ppm, char *report, uint32_t report_cap, uint32_t *report_len);
#endif
