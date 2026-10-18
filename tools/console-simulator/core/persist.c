/* Durable small-file writes for runtime preferences (N3: the console's HOME theme choice).
 *
 * The preference lives in a writable state directory the launcher passes in, separate from the read-only HOME package (on the
 * console: a folder on its writable data partition; in the simulator: C:\NES\work\console-simulator\state\<profile>). A write
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

int ccs_mkdirs(const char *dir) {
    char p[1024]; size_t n = strlen(dir); if (!n || n >= sizeof p) return -1; memcpy(p, dir, n + 1);
    for (size_t i = 1; i <= n; i++) {
        if (p[i] == '/' || p[i] == '\\' || p[i] == 0) {
            char c = p[i]; p[i] = 0;
            if (!(i == 2 && p[1] == ':')) {
#ifdef _WIN32
                if (_mkdir(p) && errno != EEXIST) { p[i] = c; return -1; }
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
    FILE *f = fopen(tmp, "wb"); if (!f) return -1;
    if (fwrite(data, 1, len, f) != len || fflush(f)) { fclose(f); remove(tmp); return -1; }
#ifdef _WIN32
    _commit(_fileno(f));
#else
    fsync(fileno(f));
#endif
    if (fclose(f)) { remove(tmp); return -1; }
#ifdef _WIN32
    if (!MoveFileExA(tmp, path, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) { remove(tmp); return -1; }
#else
    if (rename(tmp, path)) { remove(tmp); return -1; }
    char dir[1100]; snprintf(dir, sizeof dir, "%s", path); char *s = strrchr(dir, '/');
    if (s) { *s = 0; int fd = open(dir[0] ? dir : "/", O_RDONLY); if (fd >= 0) { fsync(fd); close(fd); } }
#endif
    return 0;
}
