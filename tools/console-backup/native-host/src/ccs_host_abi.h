/* CCS Console Workbench native host: the narrow ABI the host gives the managed Workbench. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
 *
 * The host passes ONE pointer to the managed code: the address of a ccs_host_abi table, stored as the AppDomain data item
 * CCS_HOST_ABI_DATA_NAME (a boxed Int64) before the managed entry point runs. The same table is also returned by the exported function
 * ccs_host_get_abi (GetProcAddress on the process executable), which the managed self-test uses as a cross-check. Nothing is read from
 * the environment, so a child process can never inherit a stale address.
 *
 * Everything here is plain C: fixed-size integers, UTF-16 paths, one report structure, one callback. Each function runs synchronously on
 * the calling thread (call it from a background thread, never the window's thread); calls are serialized inside the host (a second
 * concurrent call returns CCS_PAR2_BUSY). The PAR2 work is par2cmdline's libpar2 (GPL-2.0-or-later), linked into the host.
 *
 * Changing anything below means bumping CCS_HOST_ABI_VERSION; the managed side refuses a table whose version, size or magic differ. */
#ifndef CCS_HOST_ABI_H
#define CCS_HOST_ABI_H

#include <stdint.h>
#include <wchar.h>

#ifdef __cplusplus
extern "C" {
#endif

/* version 2 adds the HOME Designer's render check (tools/console-simulator/core/design_check.h, linked into the host); version 3 adds
 * home_design_screen (one screen of the HOME as it starts, for the HOME Designer's preview of the screen a change is for) */
#include "design_check.h"

#define CCS_HOST_ABI_VERSION   3u
#define CCS_HOST_ABI_MAGIC     0x3154534f48534343ull   /* "CCSHOST1" little-endian */
#define CCS_HOST_ABI_DATA_NAME L"CcsNativeHost.Abi"

/* status codes (the return value of every PAR2 call, also in report->status) */
#define CCS_PAR2_OK                      0   /* create: parity written and closed; verify: data intact; repair: new file written and checked */
#define CCS_PAR2_DAMAGED_REPAIRABLE      1   /* verify: the data is damaged and the intact parity is enough to rebuild it */
#define CCS_PAR2_DAMAGED_NOT_REPAIRABLE  2   /* verify/repair: the data is damaged and there is not enough intact parity */
#define CCS_PAR2_PARITY_UNUSABLE         3   /* the parity files could not be read well enough to check anything */
#define CCS_PAR2_REPAIR_FAILED           4   /* repair ran but its result did not check out; nothing was kept */
#define CCS_PAR2_CANCELED               5   /* the cancel flag was set or the callback asked to stop; partial output removed */
#define CCS_PAR2_BAD_ARGUMENT            6   /* a path or number was not acceptable (see message) */
#define CCS_PAR2_IO_ERROR                7   /* a file could not be read, created or written */
#define CCS_PAR2_OUT_OF_MEMORY           8
#define CCS_PAR2_INTERNAL_ERROR          9
#define CCS_PAR2_NOT_NEEDED             10   /* repair: the data was already intact; no output was written */
#define CCS_PAR2_BUSY                   11   /* another PAR2 call is running in this process */

/* progress stages (the callback's stage argument) */
#define CCS_PAR2_STAGE_READING_DATA      1   /* create: reading and hashing the protected file */
#define CCS_PAR2_STAGE_COMPUTING         2   /* create: computing recovery blocks */
#define CCS_PAR2_STAGE_WRITING           3   /* create: writing the parity files (no percentage) */
#define CCS_PAR2_STAGE_LOADING_PARITY    4   /* verify/repair: reading the parity files */
#define CCS_PAR2_STAGE_SCANNING_DATA     5   /* verify/repair: checking the protected file block by block */
#define CCS_PAR2_STAGE_REPAIRING         6   /* repair: rebuilding damaged blocks into the new file */
#define CCS_PAR2_STAGE_CHECKING_OUTPUT   7   /* repair: checking the rebuilt file */

/* Progress: stage as above, permille 0..1000 within the stage or -1 when the stage has no measure. Return nonzero to ask the operation
 * to stop (the same as setting the cancel flag). Called on the thread that made the call; must not throw or longjmp. */
typedef int32_t (*ccs_par2_progress_fn)(void *context, int32_t stage, int32_t permille);

typedef struct ccs_par2_report {
    uint32_t size;               /* in: the caller sets sizeof(ccs_par2_report); refused when different */
    int32_t  status;             /* out: CCS_PAR2_* (also the return value) */
    int32_t  library_result;     /* out: par2cmdline's own Result code, -1 when the library was not reached */
    uint32_t block_size;         /* out: bytes per block */
    uint32_t source_blocks;      /* out: data blocks in the protected file */
    uint32_t recovery_blocks;    /* out: create: recovery blocks written; verify/repair: intact recovery blocks found */
    uint32_t recovery_files;     /* out: create: recovery volume files written (besides the index file) */
    uint32_t damaged_blocks;     /* out: verify/repair: data blocks missing or damaged (0 when intact) */
    uint32_t available_blocks;   /* out: verify/repair: data blocks found intact */
    uint32_t reserved;
    uint64_t source_bytes;       /* out: size of the protected file */
    uint64_t parity_bytes;       /* out: create: bytes of all parity files written; verify/repair: bytes of the parity files read */
    wchar_t  message[1024];      /* out: plain-text outcome or the reason for a failure, NUL-terminated */
} ccs_par2_report;

/* Create parity for ONE finished file.
 *   data_path        the protected file (read only; held open without write/rename/delete sharing for the whole call)
 *   par2_path        the index file to create, ending in ".par2", in an existing folder; neither it nor any volume may exist yet.
 *                    The volumes are written beside it as <name>.volNNN+MMM.par2. Nothing else is written.
 *   redundancy_pct   1..100: recovery blocks = ceil-ish(source blocks x pct / 100), as par2cmdline's -r
 *   volume_count     1..32 recovery volume files (blocks spread uniformly), 0 = 1
 *   memory_limit     bytes par2cmdline may use for buffers, 0 = 256 MB (the library re-reads the data when this is smaller than needed)
 * On any failure or cancel, every file this call created is removed. */
typedef int32_t (*ccs_par2_create_fn)(const wchar_t *data_path, const wchar_t *par2_path, uint32_t redundancy_pct, uint32_t volume_count,
                                      uint64_t memory_limit, ccs_par2_progress_fn progress, void *context, volatile int32_t *cancel,
                                      ccs_par2_report *report);

/* Verify ONE protected file against its parity (reads only; the data file and parity files are held open read-only, unshared for write,
 * rename and delete). data_path must keep the file name recorded when the parity was created. */
typedef int32_t (*ccs_par2_verify_fn)(const wchar_t *par2_path, const wchar_t *data_path, uint64_t memory_limit,
                                      ccs_par2_progress_fn progress, void *context, volatile int32_t *cancel, ccs_par2_report *report);

/* Rebuild ONE damaged file into a NEW file. data_path and the parity are only read (held open as for verify); output_path must not exist
 * and its folder must exist. The library rebuilds into a fresh private folder beside output_path, checks the result against the parity,
 * and only then moves it to output_path; on any failure nothing is left behind. Returns CCS_PAR2_NOT_NEEDED (writing nothing) when the
 * data is intact. */
typedef int32_t (*ccs_par2_repair_fn)(const wchar_t *par2_path, const wchar_t *data_path, const wchar_t *output_path, uint64_t memory_limit,
                                      ccs_par2_progress_fn progress, void *context, volatile int32_t *cancel, ccs_par2_report *report);

typedef struct ccs_host_abi {
    uint64_t magic;              /* CCS_HOST_ABI_MAGIC */
    uint32_t version;            /* CCS_HOST_ABI_VERSION */
    uint32_t size;               /* sizeof(ccs_host_abi) */
    uint32_t report_size;        /* sizeof(ccs_par2_report) */
    uint32_t reserved;
    const wchar_t *host_version; /* e.g. L"1.0.0" */
    const wchar_t *host_arch;    /* L"x64" or L"arm64" */
    const wchar_t *par2_version; /* e.g. L"par2cmdline 1.4.0" */
    ccs_par2_create_fn par2_create;
    ccs_par2_verify_fn par2_verify;
    ccs_par2_repair_fn par2_repair;
    /* version 2 (offsets with 8-byte pointers: 72, 76, 80, 88; size 96). A table without the HOME renderer (ccs-par2.dll for Classic Cart
     * Studio for Windows, which runs ccs-sim-core.exe instead) has home_check_abi 0 and both pointers NULL. */
    uint32_t home_check_abi;     /* CCS_HOME_CHECK_ABI, or 0 */
    uint32_t home_reserved;
    int32_t (*home_render_check)(const ccs_home_check_request *req, char *report, uint32_t report_cap, uint32_t *report_len);
    int32_t (*home_design_boot)(const char *pack_dir, const char *design_root, const char *state_dir, const char *frame_ppm, char *report, uint32_t report_cap, uint32_t *report_len);
    /* version 3 (offset 96 with 8-byte pointers; size 104): ccs_home_design_screen, or NULL without the HOME renderer */
    int32_t (*home_design_screen)(const char *pack_dir, const char *design_root, const char *state_dir, const char *screen, const char *frame_ppm, char *report, uint32_t report_cap, uint32_t *report_len);
} ccs_host_abi;

/* the exported entry points (also reachable through the table) */
__declspec(dllexport) const ccs_host_abi *ccs_host_get_abi(void);

#ifdef __cplusplus
}
#endif

#endif
