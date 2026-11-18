/* Durable small-file writes for runtime preferences (N3: the console's HOME theme choice).
 *
 * The preference lives in a writable state directory the launcher passes in, separate from the read-only HOME package (on the
 * console: a folder on its writable data partition; in the simulator: a state folder on the PC, one per profile). A write
 * goes to "<file>.tmp", is flushed to the device (fsync / _commit), then replaces the file in one rename, and on POSIX the
 * directory entry is synced too: after a power loss the file is either the old version or the new one, never a torn mix. */
#ifndef _WIN32
#define _POSIX_C_SOURCE 200809L
#endif
#include "ccs_sim.h"
#include <stdio.h>
#include <string.h>
#include <errno.h>
#ifdef _WIN32
#include <windows.h>
#include <io.h>
#include <direct.h>
#else
#include <unistd.h>
#include <fcntl.h>
#include <sys/stat.h>
#endif

#ifdef _WIN32   /* paths are UTF-8 (wide file names), as in gfx.c ccs_read_file */
static int wide(const char *s, wchar_t *w, int n) { return MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, s, -1, w, n) ? 0 : -1; }
#endif
int ccs_mkdirs(const char *dir) {
    char p[1024]; size_t n = strlen(dir); if (!n || n >= sizeof p) return -1; memcpy(p, dir, n + 1);
    for (size_t i = 1; i <= n; i++) {
        if (p[i] == '/' || p[i] == '\\' || p[i] == 0) {
            char c = p[i]; p[i] = 0;
            if (!(i == 2 && p[1] == ':')) {
#ifdef _WIN32
                wchar_t wp[1100]; if (wide(p, wp, 1100) || (!CreateDirectoryW(wp, NULL) && GetLastError() != ERROR_ALREADY_EXISTS)) { p[i] = c; return -1; }
#else
                if (mkdir(p, 0755) && errno != EEXIST) { p[i] = c; return -1; }
#endif
            }
            p[i] = c;
        }
    }
    return 0;
}

int ccs_write_atomic(const char *path, const void *data, size_t len) {
    char tmp[1100]; if (snprintf(tmp, sizeof tmp, "%s.tmp", path) >= (int)sizeof tmp) return -1;
#ifdef _WIN32
    wchar_t wt[1100], wpath[1100]; if (wide(tmp, wt, 1100) || wide(path, wpath, 1100)) return -1; FILE *f = _wfopen(wt, L"wb"); if (!f) return -1;
#else
    FILE *f = fopen(tmp, "wb"); if (!f) return -1;
#endif
#ifdef _WIN32
    if (fwrite(data, 1, len, f) != len || fflush(f)) { fclose(f); _wremove(wt); return -1; }
    _commit(_fileno(f));
    if (fclose(f)) { _wremove(wt); return -1; }
    if (!MoveFileExW(wt, wpath, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) { _wremove(wt); return -1; }
#else
    if (fwrite(data, 1, len, f) != len || fflush(f)) { fclose(f); remove(tmp); return -1; }
    fsync(fileno(f));
    if (fclose(f)) { remove(tmp); return -1; }
    if (rename(tmp, path)) { remove(tmp); return -1; }
    char dir[1100]; snprintf(dir, sizeof dir, "%s", path); char *s = strrchr(dir, '/');
    if (s) { *s = 0; int fd = open(dir[0] ? dir : "/", O_RDONLY); if (fd >= 0) { fsync(fd); close(fd); } }
#endif
    return 0;
}
