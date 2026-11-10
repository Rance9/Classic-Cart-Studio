/* console_libc.c: two C-library calls in the form the console's kernel (Linux 3.4) has, linked in place of musl's own by the linker's
 * --wrap (scripts/build_player.sh, the console targets only). musl asks the kernel for the newer form first (statx, Linux 4.11;
 * clock_gettime64, Linux 5.1) and falls back when it is refused. The console's kernel has neither, and the H1 rule is that the console
 * binary makes no system call the console has not been seen to answer (the smaller untested surface): FCEUmm asks for a file's details at
 * every game load (its palette file), and libretro-common's time() reads the clock.
 *   __fstatat (stat, fstat, lstat, fstatat)        stat64, lstat64, fstat64 or fstatat64 (all Linux 2.6), the kernel's stat64 copied into
 *                                                  musl's struct stat field by field, as musl's own fallback does
 *   __clock_gettime (clock_gettime, time, ...)     clock_gettime with 32-bit seconds (the console's player clock since H1)
 * musl's other calls are unchanged. The device test (tests/console_libc_check.c) compares every answer with musl's own under qemu-arm, and
 * the system-call check finds no statx or clock_gettime64 left in ccs-player-armv7. Copyright (C) 2026 Rance9, GPL-3.0-or-later. */
#define _GNU_SOURCE
#include <errno.h>
#include <fcntl.h>
#include <string.h>
#include <sys/stat.h>
#include <sys/syscall.h>
#include <time.h>
#include <unistd.h>

#if !defined(__arm__) || defined(__GLIBC__)
#error "console_libc.c is for the console build only (ARM, static musl)"
#endif

struct ccs_kstat64 {   /* the ARM EABI kernel's struct stat64 (arch/arm/include/uapi/asm/stat.h; musl's arch/arm/kstat.h) */
    unsigned long long st_dev; int pad0; long st_ino32; unsigned st_mode, st_nlink; unsigned long st_uid, st_gid;
    unsigned long long st_rdev; int pad1; long long st_size; long st_blksize; long long st_blocks;
    long st_atime_sec, st_atime_nsec, st_mtime_sec, st_mtime_nsec, st_ctime_sec, st_ctime_nsec; unsigned long long st_ino;
};
_Static_assert(sizeof(struct ccs_kstat64) == 104, "the ARM kernel's stat64 is 104 bytes");
#ifdef SYS_clock_gettime32   /* musl's name for the 32-bit call where the 64-bit one exists */
#define CCS_CGT32 SYS_clock_gettime32
#else
#define CCS_CGT32 SYS_clock_gettime
#endif
_Static_assert(CCS_CGT32 == 263, "ARM EABI clock_gettime is system call 263");

int __wrap___fstatat(int fd, const char *restrict path, struct stat *restrict st, int flag) {
    struct ccs_kstat64 k; long r;
    if (flag == AT_EMPTY_PATH && fd >= 0 && !*path) r = syscall(SYS_fstat64, fd, &k);
    else if ((fd == AT_FDCWD || *path == '/') && flag == AT_SYMLINK_NOFOLLOW) r = syscall(SYS_lstat64, path, &k);
    else if ((fd == AT_FDCWD || *path == '/') && !flag) r = syscall(SYS_stat64, path, &k);
    else r = syscall(SYS_fstatat64, fd, path, &k, flag);
    if (r) return -1;   /* errno is set */
    *st = (struct stat){ .st_dev = k.st_dev, .st_ino = k.st_ino, .st_mode = k.st_mode, .st_nlink = k.st_nlink, .st_uid = k.st_uid,
        .st_gid = k.st_gid, .st_rdev = k.st_rdev, .st_size = k.st_size, .st_blksize = k.st_blksize, .st_blocks = k.st_blocks,
        .st_atim.tv_sec = k.st_atime_sec, .st_atim.tv_nsec = k.st_atime_nsec, .st_mtim.tv_sec = k.st_mtime_sec, .st_mtim.tv_nsec = k.st_mtime_nsec,
        .st_ctim.tv_sec = k.st_ctime_sec, .st_ctim.tv_nsec = k.st_ctime_nsec,
#if _REDIR_TIME64
        .__st_atim32.tv_sec = k.st_atime_sec, .__st_atim32.tv_nsec = k.st_atime_nsec, .__st_mtim32.tv_sec = k.st_mtime_sec,
        .__st_mtim32.tv_nsec = k.st_mtime_nsec, .__st_ctim32.tv_sec = k.st_ctime_sec, .__st_ctim32.tv_nsec = k.st_ctime_nsec,
#endif
    };
    return 0;
}
int __wrap___fstatat_time64(int fd, const char *restrict path, struct stat *restrict st, int flag) { return __wrap___fstatat(fd, path, st, flag); }

int __wrap___clock_gettime(clockid_t clk, struct timespec *ts) {
    long t32[2];
    if (syscall(CCS_CGT32, clk, t32)) return -1;   /* errno is set */
    ts->tv_sec = t32[0]; ts->tv_nsec = t32[1]; return 0;
}
int __wrap___clock_gettime64(clockid_t clk, struct timespec *ts) { return __wrap___clock_gettime(clk, ts); }
