/* gp_util.c: hashing, the small file layer (UTF-8 paths on every platform; durable atomic replacement), time. The browser build
 * (CCS_WEB) keeps files in the page runtime's store instead: a write is staged at once and reported done only when the browser's
 * storage confirmed it (gp_io_pending, gp_io_failed); the session waits for that before it says a save is written. */
#ifndef _WIN32
#define _DEFAULT_SOURCE   /* timegm */
#define _POSIX_C_SOURCE 200809L
#endif
#include "gp.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#ifdef _WIN32
#include <windows.h>
#else
#include <unistd.h>
#include <fcntl.h>
#include <errno.h>
#include <sys/stat.h>
#include <sys/types.h>
#endif
/* the NES Classic (Linux 3.4, static musl on ARMv7) and the ARM test builds: the classic 32-bit clock system call itself and access() instead
   of stat(). The console's kernel predates musl 1.2's 64-bit-time and statx calls; musl falls back when they answer ENOSYS, but the console
   binaries leave them out of the frequent paths (the H1 rule: a smaller untested surface). Local time is shown in UTC there (no time zone
   file is read; the console keeps no calendar time) */
#if defined(__linux__) && defined(__arm__) && !defined(__GLIBC__) && !defined(CCS_WEB)
#define GP_OLD_KERNEL 1
#include <sys/syscall.h>
#if defined(SYS_clock_gettime32)
#define GP_CGT32 SYS_clock_gettime32
#else
#define GP_CGT32 SYS_clock_gettime
#endif
_Static_assert(GP_CGT32 == 263, "ARM EABI clock_gettime is system call 263");
static int gp_clock32(int id, long t32[2]) { return (int)syscall(GP_CGT32, id, t32); }
#endif
int (*gp_write_check)(const char *path, int kind) = NULL;
#define GP_CHECK(p, k) (gp_write_check && gp_write_check((p), (k)))

int gp_fail_writes = 0;
int gp_virtual = 0; static uint64_t g_vclock;   /* the headless test host: sleeping advances a virtual clock */
static const char *ACT[GA_COUNT] = { "up", "down", "left", "right", "a", "b", "select", "start" };
const char *gp_action_name(int a) { return a >= 0 && a < GA_COUNT ? ACT[a] : "?"; }

/* ---- SHA-256 (FIPS 180-4) ---- */
typedef struct { uint32_t h[8]; uint64_t n; uint8_t b[64]; size_t k; } sha_ctx;
static const uint32_t K256[64] = {
    0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,
    0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
    0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,
    0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
    0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,
    0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2 };
#define ROR(x, n) (((x) >> (n)) | ((x) << (32 - (n))))
static void sha_block(sha_ctx *c, const uint8_t *p) {
    uint32_t w[64], a, b, d, e, f, g, h, cc, t1, t2;
    for (int i = 0; i < 16; i++) w[i] = (uint32_t)p[i * 4] << 24 | (uint32_t)p[i * 4 + 1] << 16 | (uint32_t)p[i * 4 + 2] << 8 | p[i * 4 + 3];
    for (int i = 16; i < 64; i++) { uint32_t s0 = ROR(w[i - 15], 7) ^ ROR(w[i - 15], 18) ^ (w[i - 15] >> 3), s1 = ROR(w[i - 2], 17) ^ ROR(w[i - 2], 19) ^ (w[i - 2] >> 10); w[i] = w[i - 16] + s0 + w[i - 7] + s1; }
    a = c->h[0]; b = c->h[1]; cc = c->h[2]; d = c->h[3]; e = c->h[4]; f = c->h[5]; g = c->h[6]; h = c->h[7];
    for (int i = 0; i < 64; i++) {
        t1 = h + (ROR(e, 6) ^ ROR(e, 11) ^ ROR(e, 25)) + ((e & f) ^ (~e & g)) + K256[i] + w[i];
        t2 = (ROR(a, 2) ^ ROR(a, 13) ^ ROR(a, 22)) + ((a & b) ^ (a & cc) ^ (b & cc));
        h = g; g = f; f = e; e = d + t1; d = cc; cc = b; b = a; a = t1 + t2; }
    c->h[0] += a; c->h[1] += b; c->h[2] += cc; c->h[3] += d; c->h[4] += e; c->h[5] += f; c->h[6] += g; c->h[7] += h; }
void gp_sha256(const void *data, size_t len, char hex[65]) {
    sha_ctx c = { { 0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19 }, 0, { 0 }, 0 };
    const uint8_t *p = data; c.n = (uint64_t)len * 8;
    while (len >= 64) { sha_block(&c, p); p += 64; len -= 64; }
    memcpy(c.b, p, len); c.k = len; c.b[c.k++] = 0x80;
    if (c.k > 56) { memset(c.b + c.k, 0, 64 - c.k); sha_block(&c, c.b); c.k = 0; }
    memset(c.b + c.k, 0, 56 - c.k); for (int i = 0; i < 8; i++) c.b[56 + i] = (uint8_t)(c.n >> (56 - 8 * i)); sha_block(&c, c.b);
    for (int i = 0; i < 8; i++) sprintf(hex + i * 8, "%08x", c.h[i]); hex[64] = 0; }
uint32_t gp_crc32(const void *data, size_t len) {
    static uint32_t t[256]; static int init; const uint8_t *p = data; uint32_t c = 0xFFFFFFFFu;
    if (!init) { for (uint32_t i = 0; i < 256; i++) { uint32_t x = i; for (int k = 0; k < 8; k++) x = (x & 1) ? 0xEDB88320u ^ (x >> 1) : x >> 1; t[i] = x; } init = 1; }
    while (len--) c = t[(c ^ *p++) & 0xFF] ^ (c >> 8);
    return c ^ 0xFFFFFFFFu; }

/* ---- files ---- */
void gp_path(char *out, size_t n, const char *dir, const char *name) {
#ifdef _WIN32
    snprintf(out, n, "%s\\%s", dir, name);
#else
    snprintf(out, n, "%s/%s", dir, name);
#endif
}
#ifdef _WIN32
static wchar_t *wpath(const char *u8) {   /* caller frees */
    int n = MultiByteToWideChar(CP_UTF8, 0, u8, -1, NULL, 0); if (n <= 0) return NULL;
    wchar_t *w = malloc((size_t)n * sizeof *w); if (w) MultiByteToWideChar(CP_UTF8, 0, u8, -1, w, n); return w; }
#endif
#if defined(CCS_WEB)
#define IMP(n) __attribute__((import_module("ccs"), import_name(n)))
IMP("fs_size") int js_fs_size(const char *path);                       /* -1: no such file */
IMP("fs_read") int js_fs_read(const char *path, void *dst, int n);      /* bytes copied, -1 failed */
IMP("fs_write") int js_fs_write(const char *path, const void *src, int n);   /* 0: staged (its commit is pending), -1 refused */
IMP("fs_remove") int js_fs_remove(const char *path);
IMP("fs_rename_new") int js_fs_rename_new(const char *from, const char *to);
IMP("io_pending") int js_io_pending(void);                             /* writes the browser has not confirmed yet */
IMP("io_failed") int js_io_failed(char *why, int n);                    /* 1 (and why) when a write since the last call failed; its file is as before */
IMP("now_ms") double js_now_ms(void);
int gp_read_file(const char *path, uint8_t **buf, size_t *len, size_t max_len) {
    *buf = NULL; *len = 0; int n = js_fs_size(path); if (n < 0) return -1; if ((size_t)n > max_len) return -2;
    uint8_t *b = malloc((size_t)n + 1); if (!b) return -1; if (js_fs_read(path, b, n) != n) { free(b); return -1; } *buf = b; *len = (size_t)n; return 0; }
int gp_write_atomic(const char *path, const void *data, size_t len) { if (gp_fail_writes) return -1; return js_fs_write(path, data, (int)len) ? -1 : 0; }
int gp_mkdirs(const char *dir) { (void)dir; return 0; }   /* the store has no folders: a path is a name */
int gp_exists(const char *path) { return js_fs_size(path) >= 0; }
int gp_remove(const char *path) { return js_fs_remove(path); }
int gp_rename_new(const char *from, const char *to) { if (gp_fail_writes) return -1; return js_fs_rename_new(from, to); }
int gp_io_pending(void) { return js_io_pending(); }
int gp_io_failed(char *why, size_t n) { return js_io_failed(why, (int)n); }
#else
int gp_io_pending(void) { return 0; }   /* every write here is complete when it returns */
int gp_io_failed(char *why, size_t n) { (void)why; (void)n; return 0; }
static int mkdirs_plain(const char *dir) {
    char p[1100]; size_t n = strlen(dir); if (!n || n >= sizeof p) return -1; memcpy(p, dir, n + 1);
    for (size_t i = 1; i <= n; i++) if (p[i] == '/' || p[i] == '\\' || p[i] == 0) {
        char c = p[i]; p[i] = 0;
        if (!(i == 2 && p[1] == ':')) {
#ifdef _WIN32
            wchar_t *w = wpath(p); if (w) { CreateDirectoryW(w, NULL); free(w); }
#else
            mkdir(p, 0755);
#endif
        }
        p[i] = c; }
    return gp_exists(dir) ? 0 : -1; }
#ifndef _WIN32
/* ---- the confined file layer (POSIX; P3-F1). A file is never written through a link. Its folder is opened first: in a RAM-only session
   one name at a time from the handle of a folder the session checked at its start (gp_confine_add), never following a link and
   creating a missing folder there; elsewhere as named. The file is then created in that folder without following a link (O_NOFOLLOW; a
   temporary name with O_EXCL), checked as it was opened (one regular file with one name; in a RAM-only session also in RAM:
   gp_object_check) and only then written or truncated. A link, a folder or a device where a file belongs is refused and left as it is,
   its target untouched. The calls are the console kernel's (openat, mkdirat, fstatat64, renameat, unlinkat, fstatfs64: Linux 2.6) ---- */
#define GP_MAXROOT 12
static struct { char path[1100]; int fd; } g_root[GP_MAXROOT]; static int g_nroot;
int (*gp_object_check)(int fd, int kind) = NULL;
void (*gp_refused)(const char *path, int kind, const char *why) = NULL;
static int refuse(const char *path, int kind, const char *why) { if (gp_refused) gp_refused(path, kind, why); errno = EPERM; return -1; }
int gp_confine_add(const char *dir) {
    if (!dir || dir[0] != '/' || strlen(dir) >= sizeof g_root[0].path) return -1;
    for (int i = 0; i < g_nroot; i++) if (!strcmp(g_root[i].path, dir)) return 0;
    if (g_nroot >= GP_MAXROOT) return -1;
    int fd = open(dir, O_RDONLY | O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC); if (fd < 0) return -1;
    if (gp_object_check && gp_object_check(fd, 1)) { close(fd); return -1; }
    snprintf(g_root[g_nroot].path, sizeof g_root[0].path, "%s", dir); g_root[g_nroot++].fd = fd; return 0; }
void gp_confine_end(void) { for (int i = 0; i < g_nroot; i++) close(g_root[i].fd); g_nroot = 0; }
static int split_path(const char *path, char *dir, size_t dn, const char **base) {
    const char *s = path ? strrchr(path, '/') : NULL; if (!s || !s[1] || !strcmp(s + 1, ".") || !strcmp(s + 1, "..")) return -1;
    size_t L = (size_t)(s - path); if (L + 2 > dn) return -1;
    if (!L) { dir[0] = '/'; dir[1] = 0; } else { memcpy(dir, path, L); dir[L] = 0; } *base = s + 1; return 0; }
/* the folder, opened (a handle, or -1 with errno set; refusals are reported) */
static int open_dir(const char *dir, int create, const char *path, int kind) {
    if (!g_nroot) {   /* not confined: the folder as named (links in it followed, as everywhere else on this system) */
        int fd = open(dir, O_RDONLY | O_DIRECTORY | O_CLOEXEC);
        if (fd < 0 && errno == ENOENT && create && !mkdirs_plain(dir)) fd = open(dir, O_RDONLY | O_DIRECTORY | O_CLOEXEC);
        return fd; }
    int best = -1; size_t bl = 0;
    for (int i = 0; i < g_nroot; i++) { size_t L = strlen(g_root[i].path); if (L == 1) L = 0;
        if (!strncmp(dir, g_root[i].path, L) && (dir[L] == 0 || dir[L] == '/') && (best < 0 || L >= bl)) { best = i; bl = L; } }
    if (best < 0) return refuse(path, kind, "outside the folders of this session");
    int fd = openat(g_root[best].fd, ".", O_RDONLY | O_DIRECTORY | O_CLOEXEC); if (fd < 0) return -1;   /* the checked folder itself, a handle of its own (portable: WASI has no F_DUPFD_CLOEXEC) */
    for (const char *p = dir + bl; *p; ) {
        while (*p == '/') p++; if (!*p) break;
        char comp[256]; size_t n = strcspn(p, "/"); if (n >= sizeof comp) { close(fd); return refuse(path, kind, "a name in the path is too long"); }
        memcpy(comp, p, n); comp[n] = 0; p += n;
        if (!strcmp(comp, ".") || !strcmp(comp, "..")) { close(fd); return refuse(path, kind, "the path leaves its folder"); }
        int nf = openat(fd, comp, O_RDONLY | O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC);
        if (nf < 0 && errno == ENOENT && create) {
            if (mkdirat(fd, comp, 0755) && errno != EEXIST) { int e = errno; close(fd); errno = e; return -1; }
            nf = openat(fd, comp, O_RDONLY | O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC); }
        if (nf < 0) { int e = errno; close(fd); if (e == ELOOP || e == ENOTDIR) return refuse(path, kind, "a link or a file stands where a folder belongs (not followed)"); errno = e; return -1; }
        close(fd); fd = nf;
        if (gp_object_check && gp_object_check(fd, 1)) { close(fd); return refuse(path, kind, "a folder on the way is not in RAM"); } }
    return fd; }
/* the final name in an opened folder: absent or a regular file; anything else is refused and left as it is */
static int final_ok(int dfd, const char *base, const char *path, int kind, int *exists) {
    struct stat st; *exists = 0;
    if (fstatat(dfd, base, &st, AT_SYMLINK_NOFOLLOW)) return errno == ENOENT ? 0 : -1;
    *exists = 1; if (S_ISREG(st.st_mode)) return 0;
    return refuse(path, kind, S_ISLNK(st.st_mode) ? "a link stands where this file belongs (not followed, left as it is)" : "something other than a file stands where this file belongs"); }
static int opened_ok(int fd, const char *path, int kind) {   /* the object really opened: a regular file with one name; in RAM when confined */
    struct stat st; if (fstat(fd, &st) || !S_ISREG(st.st_mode) || st.st_nlink > 1) return refuse(path, kind, "the opened file is not a plain file of its own");
    if (gp_object_check && gp_object_check(fd, 0)) return refuse(path, kind, "the opened file is not in RAM");
    return 0; }
static int write_atomic_posix(const char *path, const void *data, size_t len) {
    char dir[1100]; const char *base; if (split_path(path, dir, sizeof dir, &base)) return refuse(path, 0, "not a file name");
    int dfd = open_dir(dir, 0, path, 0), ex, fd = -1; if (dfd < 0) return -1;
    if (final_ok(dfd, base, path, 0, &ex)) { close(dfd); return -1; }
    char tmp[300];   /* this process's own temporary name: the Studio's backend saves some of the same files (HOME Music, Game Settings) with "<file>.tmp" */
    for (int k = 0; k < 8 && fd < 0; k++) {
        if (k) snprintf(tmp, sizeof tmp, "%.200s.%ld.%d.tmp", base, (long)getpid(), k); else snprintf(tmp, sizeof tmp, "%.200s.%ld.tmp", base, (long)getpid());
        fd = openat(dfd, tmp, O_WRONLY | O_CREAT | O_EXCL | O_NOFOLLOW | O_CLOEXEC, 0644);
        if (fd < 0 && errno != EEXIST) break;
        if (fd < 0 && gp_refused) gp_refused(path, 4, "something already stood at the temporary name (not followed, left as it is; another name used)"); }
    if (fd < 0) { close(dfd); return -1; }
    if (opened_ok(fd, path, 0)) { close(fd); unlinkat(dfd, tmp, 0); close(dfd); return -1; }
    size_t done = 0; while (done < len) { ssize_t w = write(fd, (const uint8_t *)data + done, len - done); if (w <= 0) { close(fd); unlinkat(dfd, tmp, 0); close(dfd); return -1; } done += (size_t)w; }
    if (fsync(fd) || close(fd)) { unlinkat(dfd, tmp, 0); close(dfd); return -1; }
    if (final_ok(dfd, base, path, 0, &ex) || renameat(dfd, tmp, dfd, base)) { unlinkat(dfd, tmp, 0); close(dfd); return -1; }   /* rename never follows a link at the name */
    fsync(dfd); close(dfd); return 0; }
/* a file written as it goes (a trace, a test seam's output, a lock): created or opened without following a link, checked as opened, and
   truncated only after that check. The descriptor (read and write), or -1 */
int gp_open_file(const char *path, int truncate) {
    if (GP_CHECK(path, 0)) return -1;
    char dir[1100]; const char *base; if (split_path(path, dir, sizeof dir, &base)) return refuse(path, 0, "not a file name");
    int dfd = open_dir(dir, 0, path, 0), ex; if (dfd < 0) return -1;
    if (final_ok(dfd, base, path, 0, &ex)) { close(dfd); return -1; }
    int fd = openat(dfd, base, O_RDWR | O_CREAT | O_NOFOLLOW | O_CLOEXEC, 0644), e = errno; close(dfd);
    if (fd < 0) { if (e == ELOOP) return refuse(path, 0, "a link stands where this file belongs (not followed, left as it is)"); errno = e; return -1; }
    if (opened_ok(fd, path, 0) || (truncate && ftruncate(fd, 0))) { close(fd); return -1; }
    return fd; }
#endif
int gp_read_file(const char *path, uint8_t **buf, size_t *len, size_t max_len) {
    *buf = NULL; *len = 0;
#ifdef _WIN32
    wchar_t *w = wpath(path); if (!w) return -1;
    HANDLE f = CreateFileW(w, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL); free(w);
    if (f == INVALID_HANDLE_VALUE) return -1;
    LARGE_INTEGER sz; if (!GetFileSizeEx(f, &sz) || sz.QuadPart < 0 || (uint64_t)sz.QuadPart > max_len) { CloseHandle(f); return -2; }
    uint8_t *b = malloc((size_t)sz.QuadPart + 1); DWORD got = 0; size_t have = 0;
    while (b && have < (size_t)sz.QuadPart && ReadFile(f, b + have, (DWORD)((size_t)sz.QuadPart - have > (1u << 30) ? (1u << 30) : (size_t)sz.QuadPart - have), &got, NULL) && got) have += got;
    CloseHandle(f); if (!b || have != (size_t)sz.QuadPart) { free(b); return -1; }
    *buf = b; *len = have; return 0;
#else
    FILE *f = fopen(path, "rb"); if (!f) return -1;
    if (fseek(f, 0, SEEK_END)) { fclose(f); return -1; } long n = ftell(f); if (n < 0 || (size_t)n > max_len) { fclose(f); return -2; } rewind(f);
    uint8_t *b = malloc((size_t)n + 1); if (!b || fread(b, 1, (size_t)n, f) != (size_t)n) { free(b); fclose(f); return -1; }
    fclose(f); *buf = b; *len = (size_t)n; return 0;
#endif
}
int gp_write_atomic(const char *path, const void *data, size_t len) {
    if (gp_fail_writes || GP_CHECK(path, 0)) return -1;
#ifdef _WIN32
    char tmp[1200];   /* this process's own temporary name: the Studio's backend saves some of the same files (HOME Music, Game Settings) with "<file>.tmp" */
    snprintf(tmp, sizeof tmp, "%s.%lu.tmp", path, (unsigned long)GetCurrentProcessId());
    wchar_t *wt = wpath(tmp), *wf = wpath(path); int ok = 0;
    if (wt && wf) {
        HANDLE f = CreateFileW(wt, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
        if (f != INVALID_HANDLE_VALUE) {
            DWORD put = 0; size_t done = 0; ok = 1;
            while (done < len) { if (!WriteFile(f, (const uint8_t *)data + done, (DWORD)(len - done > (1u << 30) ? (1u << 30) : len - done), &put, NULL) || !put) { ok = 0; break; } done += put; }
            if (ok && !FlushFileBuffers(f)) ok = 0;
            CloseHandle(f);
            if (ok) ok = MoveFileExW(wt, wf, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) != 0;
            if (!ok) DeleteFileW(wt);
        }
    }
    free(wt); free(wf); return ok ? 0 : -1;
#else
    return write_atomic_posix(path, data, len);
#endif
}
int gp_mkdirs(const char *dir) {
    size_t n = strlen(dir); if (!n || n >= 1100 || GP_CHECK(dir, 1)) return -1;
#ifdef _WIN32
    return mkdirs_plain(dir);
#else
    int fd = open_dir(dir, 1, dir, 1); if (fd < 0) return -1; close(fd); return 0;
#endif
}
int gp_exists(const char *path) {
#ifdef _WIN32
    wchar_t *w = wpath(path); if (!w) return 0; DWORD a = GetFileAttributesW(w); free(w); return a != INVALID_FILE_ATTRIBUTES;
#elif defined(GP_OLD_KERNEL)
    return access(path, F_OK) == 0;
#else
    struct stat st; return stat(path, &st) == 0;
#endif
}
int gp_remove(const char *path) {
    if (GP_CHECK(path, 3)) return -1;
#ifdef _WIN32
    wchar_t *w = wpath(path); int ok = w && DeleteFileW(w); free(w); return ok ? 0 : -1;
#else
    char dir[1100]; const char *base; if (split_path(path, dir, sizeof dir, &base)) return -1;
    int dfd = open_dir(dir, 0, path, 3); if (dfd < 0) return -1;
    int r = unlinkat(dfd, base, 0); close(dfd); return r ? -1 : 0;   /* a link is removed itself, never its target */
#endif
}
int gp_rename_new(const char *from, const char *to) {   /* a rename that never replaces an existing file */
    if (gp_fail_writes || GP_CHECK(from, 3) || GP_CHECK(to, 2)) return -1;
#ifdef _WIN32
    wchar_t *a = wpath(from), *b = wpath(to); int ok = a && b && MoveFileExW(a, b, MOVEFILE_WRITE_THROUGH); free(a); free(b); return ok ? 0 : -1;
#else
    char d1[1100], d2[1100]; const char *b1, *b2; if (split_path(from, d1, sizeof d1, &b1) || split_path(to, d2, sizeof d2, &b2)) return -1;
    int f1 = open_dir(d1, 0, from, 3); if (f1 < 0) return -1;
    int f2 = open_dir(d2, 0, to, 2); if (f2 < 0) { close(f1); return -1; }
    struct stat st; int r = -1, ex;
    if (fstatat(f1, b1, &st, AT_SYMLINK_NOFOLLOW)) r = -1;
    else if (!S_ISREG(st.st_mode)) refuse(from, 3, "only a file of its own is renamed (a link is not followed)");
    else if (!final_ok(f2, b2, to, 2, &ex) && !ex) r = renameat(f1, b1, f2, b2) ? -1 : 0;
    close(f1); close(f2); return r;
#endif
}
#endif   /* CCS_WEB */
/* "Nov 6, 2026, 4:38:16 AM UTC-6" for a stored UTC time ("2026-11-06T10:38:16Z"); 0 ok, -1 when iso is not such a time. Local time is
   this computer's; the browser's (CCS_WEB) comes from the page, the same text. CCS_PLAYER_UTC=1 (tests only): shown in UTC */
#if defined(CCS_WEB)
IMP("local_time") int js_local_time(const char *iso, char *out, int n);
#endif
int gp_local_time(const char *iso, char *out, size_t n) {
    struct tm g; memset(&g, 0, sizeof g);
    if (!iso || sscanf(iso, "%d-%d-%dT%d:%d:%d", &g.tm_year, &g.tm_mon, &g.tm_mday, &g.tm_hour, &g.tm_min, &g.tm_sec) != 6 || g.tm_year < 1970 || g.tm_mon < 1 || g.tm_mon > 12) return -1;
#if defined(CCS_WEB)
    if (!getenv("CCS_PLAYER_UTC")) return js_local_time(iso, out, (int)n);
#endif
    g.tm_year -= 1900; g.tm_mon -= 1; static const char *MON[12] = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
#ifdef _WIN32
    time_t t = _mkgmtime(&g);
#else
    time_t t = timegm(&g);
#endif
    struct tm l; long off = 0;
#if defined(CCS_CONSOLE)
    if (g.tm_year + 1900 < 2024) return -1;   /* the console's clock was not set when this was saved: no time is shown as if it were one */
#endif
#if defined(GP_OLD_KERNEL)
    if (1) l = g;
#else
    if (getenv("CCS_PLAYER_UTC")) l = g;
#endif
    else {
#ifdef _WIN32
        localtime_s(&l, &t); off = (long)(_mkgmtime(&l) - t) / 60;
#else
        localtime_r(&t, &l); off = (long)(timegm(&l) - t) / 60;
#endif
    }
    int h12 = l.tm_hour % 12 ? l.tm_hour % 12 : 12; char z[24];
    if (off % 60) snprintf(z, sizeof z, "UTC%c%ld:%02ld", off < 0 ? '-' : '+', (off < 0 ? -off : off) / 60, (off < 0 ? -off : off) % 60); else snprintf(z, sizeof z, "UTC%c%ld", off < 0 ? '-' : '+', (off < 0 ? -off : off) / 60);
    snprintf(out, n, "%s %d, %d, %d:%02d:%02d %s %s", MON[l.tm_mon], l.tm_mday, l.tm_year + 1900, h12, l.tm_min, l.tm_sec, l.tm_hour < 12 ? "AM" : "PM", z); return 0; }
void gp_now_iso(char out[32]) {
#if defined(GP_OLD_KERNEL)
    long t32[2] = { 0, 0 }; gp_clock32(CLOCK_REALTIME, t32); time_t t = (time_t)(unsigned long)t32[0]; struct tm g;
#else
    time_t t = time(NULL); struct tm g;
#endif
    { const char *fx = gp_virtual ? getenv("CCS_PLAYER_NOW") : NULL; struct tm b; memset(&b, 0, sizeof b);   /* tests (virtual time only): a fixed start, then the virtual clock */
      if (fx && sscanf(fx, "%d-%d-%dT%d:%d:%d", &b.tm_year, &b.tm_mon, &b.tm_mday, &b.tm_hour, &b.tm_min, &b.tm_sec) == 6) { b.tm_year -= 1900; b.tm_mon -= 1;
#ifdef _WIN32
          t = _mkgmtime(&b) + (time_t)(g_vclock / 1000);
#else
          t = timegm(&b) + (time_t)(g_vclock / 1000);
#endif
      } }
#ifdef _WIN32
    gmtime_s(&g, &t);
#else
    gmtime_r(&t, &g);
#endif
    strftime(out, 32, "%Y-%m-%dT%H:%M:%SZ", &g); }
uint64_t gp_ms(void) {
    if (gp_virtual) return g_vclock;
#if defined(CCS_WEB)
    return (uint64_t)js_now_ms();
#elif defined(_WIN32)
    static LARGE_INTEGER f; LARGE_INTEGER c; if (!f.QuadPart) QueryPerformanceFrequency(&f); QueryPerformanceCounter(&c); return (uint64_t)(c.QuadPart * 1000 / f.QuadPart);
#elif defined(GP_OLD_KERNEL)
    static uint64_t last; long t32[2]; if (!gp_clock32(CLOCK_MONOTONIC, t32)) last = (uint64_t)(unsigned long)t32[0] * 1000 + (uint64_t)t32[1] / 1000000; return last;
#else
    struct timespec ts; clock_gettime(CLOCK_MONOTONIC, &ts); return (uint64_t)ts.tv_sec * 1000 + (uint64_t)ts.tv_nsec / 1000000;
#endif
}
void gp_sleep_ms(int ms) {
    if (gp_virtual) { g_vclock += (uint64_t)(ms > 0 ? ms : 0); return; }
#if defined(CCS_WEB)
    (void)ms;   /* the page never blocks: the runtime paces the steps */
#elif defined(_WIN32)
    Sleep((DWORD)ms);
#else
    struct timespec ts = { ms / 1000, (long)(ms % 1000) * 1000000L }; nanosleep(&ts, NULL);
#endif
}

/* ---- ROM ---- */
static int hexval(char c) { return c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1; }
int gp_rom_load(const char *path, const char *header_hex, const char *want, gp_rom *out, char *why, size_t whylen) {
    memset(out, 0, sizeof *out); uint8_t *b; size_t n;
    int r = gp_read_file(path, &b, &n, 16u << 20);
    if (r) { snprintf(why, whylen, r == -2 ? "The ROM file is larger than any NES game." : "The ROM file could not be read."); return -1; }
    int headered = n >= 16 && b[0] == 'N' && b[1] == 'E' && b[2] == 'S' && b[3] == 0x1A;
    size_t skip = headered ? 16 + ((b[6] & 0x04) ? 512 : 0) : 0;
    if (skip > n) { free(b); snprintf(why, whylen, "The ROM file is damaged (it ends inside its header)."); return -1; }
    gp_sha256(b + skip, n - skip, out->content_sha256);
    if (want && *want && strcmp(want, out->content_sha256)) { free(b); snprintf(why, whylen, "The ROM file is not this version of the game any more (its content changed). Locate the right file in the Studio."); return -1; }
    if (headered) { out->data = b; out->len = n; return 0; }
    if (!header_hex || strlen(header_hex) != 32) { free(b); snprintf(why, whylen, "This ROM file has no header and the Game Library has no header information for it, so it cannot be played (nothing is guessed)."); return -1; }
    uint8_t *o = malloc(n + 16); if (!o) { free(b); snprintf(why, whylen, "Out of memory."); return -1; }
    for (int i = 0; i < 16; i++) { int hi = hexval(header_hex[2 * i]), lo = hexval(header_hex[2 * i + 1]); if (hi < 0 || lo < 0) { free(b); free(o); snprintf(why, whylen, "The header information for this game is not valid."); return -1; } o[i] = (uint8_t)(hi << 4 | lo); }
    if (!(o[0] == 'N' && o[1] == 'E' && o[2] == 'S' && o[3] == 0x1A)) { free(b); free(o); snprintf(why, whylen, "The header information for this game is not an iNES header."); return -1; }
    memcpy(o + 16, b, n); free(b); out->data = o; out->len = n + 16; return 0; }
void gp_rom_free(gp_rom *r) { free(r->data); memset(r, 0, sizeof *r); }
