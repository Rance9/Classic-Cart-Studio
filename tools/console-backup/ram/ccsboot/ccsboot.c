/* ccsboot: the NES Classic's boot stages (boot0, boot1/u-boot), read, checked and written through the console's NAND driver.
 * Classic Cart Studio. GPL-3.0-or-later.
 *
 * Derived from sntool (hakchi, https://github.com/madmonkey1907/hakchi, sntool/sntool.c at fd525ac710988d12fb9e37213b5fc75651ba47c0,
 * last changed in 29b87d07c9ef29eb9ba31be69b7ba9edb53bfb97; Copyright madmonkey; GPL-3.0): the driver's ioctl interface
 * (struct burn_param_t, ioctl numbers 'v' 121-126), the boot-area constants, the eGON/uboot/nandinfo header rules and checksums, the
 * read_boot0/read_boot1 reads and the burn_boot1 logical write (compare first, skip equal blocks, a bad block moves the image to the next
 * block, never beyond the boot area). Classic Cart Studio builds this file itself, so the program it runs has known source; the flash
 * tool in the pinned hakchi package (sntool 5833d00, 2020) has no public source and is not used for these writes.
 *
 * Differences from sntool, all deliberate:
 *   - one command per run; no retry anywhere (hakchi's flashBoot writes a second time after a failed check; this never does);
 *   - burn1/burn0 take a file only (never stdin), refuse anything that is not a complete, valid boot image with a correct checksum
 *     (sntool's "check -f" fixes a checksum; this refuses instead), and refuse an image larger than its area;
 *   - burn0 is the driver's boot0 writer (ioctl 122) with the image once, without sntool's compare read;
 *   - no "default" input: the bytes written are always the file given;
 *   - a block that cannot be read before its write stops burn1 there (sntool, and this program up to its 2026-11-15 correction, wrote it
 *     anyway): an unexpected read or driver error is never a reason to write. A block the driver reports as bad is skipped, as the
 *     reader skips it, and nothing is written to it. Every block written is read back and compared before the next one;
 *   - burn1-unreadable is the only way to write a block that could not be read first: Console Rescue uses it, once acknowledged, for a
 *     bootloader its diagnosis found unreadable (ram/ccsrescue). Every block it writes must still read back exactly.
 *
 * Commands (exit 0 = done; anything else = refused or failed, a reason on stderr):
 *   ccsboot read0             boot0 to stdout, its header length, checksum checked
 *   ccsboot read1             boot1 (u-boot and a nandinfo block when present) to stdout, checksum checked
 *   ccsboot check FILE        "ok SIZE": a complete boot0/boot1 image with a correct checksum, nothing else
 *   ccsboot burn1 FILE        writes FILE as boot1 (after check); prints "written BLOCKS"
 *   ccsboot burn1-unreadable FILE   the same, except that a block that cannot be read first is written without a comparison; prints
 *                             "written BLOCKS unread UNREAD" (UNREAD of them were written without a prior read)
 *   ccsboot burn0 FILE        writes FILE as boot0 through the driver's boot0 writer (after check); prints "written 1"
 * Exit codes, typed so the caller never has to guess:
 *   read0/read1   0 read and valid; 2 the NAND driver could not be opened or did not answer its test; 3 a block could not be read (or a
 *                 bad boot0 block); 4 no recognizable boot image (no header, a length that does not fit); 5 read, but the image's checksum
 *                 or structure does not hold; 6 the output could not be written
 *   burn0/burn1   0 written; 2 the file or the driver; 7 the file is not a valid image (nothing written); 8 a write failed; 9 the image would
 *                 run past the boot area; 10 a block could not be read before its write: stopped there; 11 a block written did not read
 *                 back exactly: stopped there. After 8, 10 or 11, stdout says "stopped BLOCKS": that many blocks were written before the stop
 *                 (the stage is then partly written: its state is not known until it is read again).
 * The test build (-DCCSBOOT_TEST_DEVICE) reads and writes the file named by CCSBOOT_TEST_NAND instead of the driver, with faults for the
 * tests (below); the build for the console never contains that path. */
#include <errno.h>
#include <fcntl.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/ioctl.h>
#include <sys/stat.h>
#include <unistd.h>

#define SECTOR 0x20000u          /* one NAND block as the driver addresses the boot area */
#define UBOOT_BASE 0x100000u     /* boot1 (sntool uboot_base_f) */
#define BOOT_AREA (SECTOR * 0x80u)
#define BOOT0_MAX (32u * 1024u)

#pragma pack(push, 1)
struct burn_param_b { void *buffer; uint32_t length; };
struct burn_param_t
{
    void *buffer; uint32_t length; uint32_t offset;
    union { uint32_t flags; struct { uint32_t raw : 2; uint32_t getoob : 1; uint32_t unused : 29; } in; };
    struct burn_param_b oob; uint32_t badblocks;
};
#pragma pack(pop)

enum { IO_TEST = _IO('v', 121), IO_BURN_BOOT0 = _IO('v', 122), IO_READ = _IO('v', 124), IO_WRITE = _IO('v', 125), IO_READ_BOOT0 = _IO('v', 126) };

static uint32_t le32(const uint8_t *p) { return (uint32_t)p[0] | (uint32_t)p[1] << 8 | (uint32_t)p[2] << 16 | (uint32_t)p[3] << 24; }
static int is_nandinfo(const uint8_t *d) { return le32(d) == 0xaa55a5a5u; }
static int loffset(const uint8_t *d) { if (!memcmp(d + 4, "eGON.BT", 7)) return 4; if (!memcmp(d + 4, "uboot", 6)) return 5; if (is_nandinfo(d)) return 1; return 0; }
static int coffset(const uint8_t *d) { if (!memcmp(d + 4, "eGON.BT", 7)) return 3; if (!memcmp(d + 4, "uboot", 6)) return 3; if (is_nandinfo(d)) return 2; return 0; }
static uint32_t ubsize(const uint8_t *d) { int o = loffset(d); return o ? le32(d + 4 * o) : 0; }
static uint32_t ubsum(const uint8_t *d) { int o = coffset(d); return o ? le32(d + 4 * o) : 0; }
static uint32_t stamp(const uint8_t *d) { return is_nandinfo(d) ? 0xae15bc34u : 0x5f0a6c39u; }

/* the checksum of one header-described block (image or nandinfo): 0 when it holds */
static uint32_t block_sum(const uint8_t *d, uint32_t len)
{
    uint32_t c = stamp(d) - ubsum(d);
    for (uint32_t i = 0; i < len / 4; ++i) c += le32(d + 4 * i);
    return c - ubsum(d);
}

/* a complete image: header length = image, optional nandinfo block exactly to the end, every checksum correct; 0 = valid */
static int check_image(const uint8_t *d, size_t n, int boot0, const char **why)
{
    if (n < 32 || n % 4 || loffset(d) == 0 || is_nandinfo(d)) { *why = "not a boot image (no eGON.BT/uboot header)"; return 1; }
    if (boot0 != !memcmp(d + 4, "eGON.BT", 7)) { *why = boot0 ? "not a boot0 (eGON.BT) image" : "not a boot1 (uboot) image"; return 1; }
    uint32_t l = ubsize(d);
    if (l < 32 || l % 4 || l > n) { *why = "bad length in header"; return 1; }
    if (block_sum(d, l)) { *why = "checksum does not match"; return 1; }
    size_t t = l;
    if (t < n)
    {
        if (n - t < 16 || !is_nandinfo(d + t)) { *why = "bytes after the image that are not a nandinfo block"; return 1; }
        uint32_t nl = ubsize(d + t);
        if (nl < 16 || nl % 4 || t + nl != n) { *why = "nandinfo block length does not reach the end exactly"; return 1; }
        if (block_sum(d + t, nl)) { *why = "nandinfo checksum does not match"; return 1; }
    }
    if (boot0 ? n > BOOT0_MAX : n > BOOT_AREA - UBOOT_BASE) { *why = "larger than its area"; return 1; }
    return 0;
}

/* ---------------------------------------------------------------- the device */
#ifdef CCSBOOT_TEST_DEVICE
/* test build: CCSBOOT_TEST_NAND is the logical boot area (boot1 at UBOOT_BASE); CCSBOOT_TEST_NAND.boot0 holds boot0. Faults, each a list
 * of hex block numbers, comma-separated: CCSBOOT_TEST_BAD reports a bad block; CCSBOOT_TEST_EIO fails every read of the block (EIO);
 * CCSBOOT_TEST_EIO_UNTIL_WRITE fails its reads until it has been written once in this run (a damaged block a write repairs);
 * CCSBOOT_TEST_WFAIL fails its write; CCSBOOT_TEST_WCORRUPT stores its write with one byte changed. Never compiled into the console's
 * program. */
static int listed(const char *var, uint32_t offs) { const char *b = getenv(var); char k[16]; snprintf(k, sizeof k, "%x", offs / SECTOR); if (!b) return 0; const char *s = strstr(b, k); return s && (s == b || s[-1] == ',') && (s[strlen(k)] == 0 || s[strlen(k)] == ','); }
static int dev_open(void) { const char *p = getenv("CCSBOOT_TEST_NAND"); return p ? open(p, O_RDWR) : -1; }
static int is_bad(uint32_t offs) { return listed("CCSBOOT_TEST_BAD", offs); }
static uint8_t wrote_once[BOOT_AREA / SECTOR];
static int dev_ioctl(int fd, unsigned long cmd, struct burn_param_t *p)
{
    p->badblocks = 0;
    if (cmd == IO_READ && p->offset < BOOT_AREA && (listed("CCSBOOT_TEST_EIO", p->offset) || (listed("CCSBOOT_TEST_EIO_UNTIL_WRITE", p->offset) && !wrote_once[p->offset / SECTOR]))) { errno = EIO; return -1; }
    if (cmd == IO_WRITE && listed("CCSBOOT_TEST_WFAIL", p->offset)) { errno = EIO; return -1; }
    if (cmd == IO_WRITE && !is_bad(p->offset) && listed("CCSBOOT_TEST_WCORRUPT", p->offset))
    {
        ((uint8_t *)p->buffer)[p->length / 2] ^= 0x5a; ssize_t r = pwrite(fd, p->buffer, p->length, p->offset); ((uint8_t *)p->buffer)[p->length / 2] ^= 0x5a;
        if (r >= 0 && p->offset < BOOT_AREA) wrote_once[p->offset / SECTOR] = 1;
        return r < 0 ? -1 : 0;
    }
    if (cmd == IO_TEST) { memset(p->buffer, 0, p->length); memcpy(p->buffer, "hakchi", 7); return 0; }
    if (cmd == IO_READ_BOOT0 || cmd == IO_BURN_BOOT0)
    {
        char path[4096]; snprintf(path, sizeof path, "%s.boot0", getenv("CCSBOOT_TEST_NAND"));
        int f = open(path, cmd == IO_READ_BOOT0 ? O_RDONLY : O_WRONLY | O_CREAT | O_TRUNC, 0644); if (f < 0) return -1;
        ssize_t r = cmd == IO_READ_BOOT0 ? pread(f, p->buffer, p->length, 0) : pwrite(f, p->buffer, p->length, 0);
        if (cmd == IO_READ_BOOT0 && r >= 0 && (uint32_t)r < p->length) memset((uint8_t *)p->buffer + r, 0xff, p->length - r);
        close(f); return r < 0 ? -1 : 0;
    }
    if (is_bad(p->offset)) { p->badblocks = 1; return 0; }
    ssize_t r = cmd == IO_READ ? pread(fd, p->buffer, p->length, p->offset) : pwrite(fd, p->buffer, p->length, p->offset);
    if (cmd == IO_READ && r >= 0 && (uint32_t)r < p->length) memset((uint8_t *)p->buffer + r, 0xff, p->length - r);
    if (cmd == IO_WRITE && r >= 0 && p->offset < BOOT_AREA) wrote_once[p->offset / SECTOR] = 1;
    return r < 0 ? -1 : 0;
}
#else
static int dev_open(void) { return open("/dev/nanda", O_RDWR | O_NONBLOCK); }
static int dev_ioctl(int fd, unsigned long cmd, struct burn_param_t *p) { return ioctl(fd, cmd, p); }
#endif

static uint8_t sector[SECTOR], other[SECTOR];

static int dev(void)
{
    int fd = dev_open();
    if (fd < 0) { fprintf(stderr, "ccsboot: the NAND driver could not be opened (%s)\n", strerror(errno)); return -1; }
    struct burn_param_t t; memset(&t, 0, sizeof t); t.buffer = sector; t.length = SECTOR;
    if (dev_ioctl(fd, IO_TEST, &t) < 0 || memcmp(sector, "hakchi", 7))
    { fprintf(stderr, "ccsboot: the NAND driver did not answer its test\n"); close(fd); return -1; }
    return fd;
}

/* ---------------------------------------------------------------- reading */
static int read_stage(int boot0)
{
    int fd = dev(); if (fd < 0) return 2;
    static uint8_t img[BOOT_AREA]; size_t have = 0, want = 0; uint32_t offs = boot0 ? 0 : UBOOT_BASE; int header = 1, trailer = !boot0;
    while (header || have < want)
    {
        if (offs >= BOOT_AREA) { fprintf(stderr, "ccsboot: the image runs past the boot area\n"); close(fd); return 4; }
        struct burn_param_t p; memset(&p, 0, sizeof p); p.buffer = sector; p.length = boot0 ? BOOT0_MAX : SECTOR; p.offset = offs; p.in.raw = 1;
        if (dev_ioctl(fd, boot0 ? IO_READ_BOOT0 : IO_READ, &p) < 0) { fprintf(stderr, "ccsboot: nand read failed at block %x\n", offs / SECTOR); close(fd); return 3; }
        if (p.badblocks)
        {
            fprintf(stderr, "ccsboot: bad block at %x%s\n", offs / SECTOR, boot0 ? "" : ", the image continues in the next");
            if (boot0) { close(fd); return 3; }
            offs += SECTOR; continue;
        }
        size_t part = boot0 ? BOOT0_MAX : SECTOR;
        if (header)
        {
            header = 0;
            if (loffset(sector) == 0 || is_nandinfo(sector)) { fprintf(stderr, "ccsboot: no boot image header\n"); close(fd); return 4; }
            want = ubsize(sector);
            if (want < 32 || want > (boot0 ? BOOT0_MAX : BOOT_AREA - UBOOT_BASE)) { fprintf(stderr, "ccsboot: bad length in header\n"); close(fd); return 4; }
        }
        size_t r = want - have;
        if (trailer && r <= part)
        {   /* as sntool's read_boot1: a nandinfo block right after u-boot, in the same block, belongs to it */
            trailer = 0;
            if (r < part && part - r >= 16 && is_nandinfo(sector + r))
            {
                uint32_t nl = ubsize(sector + r);
                if (nl >= 16 && nl % 4 == 0 && want + nl <= BOOT_AREA - UBOOT_BASE) { want += nl; r = want - have; }
            }
        }
        size_t take = r < part ? r : part;
        memcpy(img + have, sector, take); have += take; offs += SECTOR;
        if (boot0) break;
    }
    close(fd);
    const char *why = 0;
    if (check_image(img, have, boot0, &why)) { fprintf(stderr, "ccsboot: the image read is not valid: %s\n", why); fwrite(img, 1, have, stdout); return 5; }
    return fwrite(img, 1, have, stdout) == have ? 0 : 6;
}

/* ---------------------------------------------------------------- writing */
static uint8_t *load(const char *path, size_t *n)
{
    int f = open(path, O_RDONLY); struct stat st; if (f < 0 || fstat(f, &st) || !S_ISREG(st.st_mode) || st.st_size <= 0 || st.st_size > (off_t)BOOT_AREA) { if (f >= 0) close(f); return 0; }
    uint8_t *b = malloc((size_t)st.st_size); if (!b) { close(f); return 0; }
    if (read(f, b, (size_t)st.st_size) != st.st_size) { free(b); close(f); return 0; }
    close(f); *n = (size_t)st.st_size; return b;
}

/* boot1: block by block from UBOOT_BASE. Before each block: read it. A read that fails stops here (exit 10), before this block and
 * every later one, unless unreadable_admitted (burn1-unreadable): then the block is written without a comparison. A block the driver
 * reports bad is skipped, unwritten, and the image continues in the next block (as the reader reads it). A block that already holds
 * the wanted bytes is left as it is. A block written is read back at once: a failed read or different bytes stop here (exit 11). */
static int burn(const char *path, int boot0, int unreadable_admitted)
{
    size_t n = 0; uint8_t *img = load(path, &n); const char *why = 0;
    if (!img) { fprintf(stderr, "ccsboot: %s could not be read as a file\n", path); return 2; }
    if (check_image(img, n, boot0, &why)) { fprintf(stderr, "ccsboot: refused, %s\n", why); free(img); return 7; }
    int fd = dev(); if (fd < 0) { free(img); return 2; }
    unsigned written = 0, unread = 0;
#define STOP(code, ...) do { fprintf(stderr, __VA_ARGS__); printf("stopped %u\n", written); close(fd); free(img); return code; } while (0)
    if (boot0)
    {
        memset(sector, 0xff, sizeof sector); memcpy(sector, img, n);
        struct burn_param_t p; memset(&p, 0, sizeof p); p.buffer = sector; p.length = (uint32_t)n; p.offset = 0; p.in.raw = 1;
        if (dev_ioctl(fd, IO_BURN_BOOT0, &p) < 0) STOP(8, "ccsboot: the boot0 write failed (%s); nothing is retried\n", strerror(errno));
        written = 1;
    }
    else
    {
        uint32_t offs = UBOOT_BASE; size_t done = 0;
        while (done < n)
        {
            if (offs >= BOOT_AREA) STOP(9, "ccsboot: refusing to write beyond the boot area\n");
            size_t part = n - done < SECTOR ? n - done : SECTOR;
            memset(sector, 0xff, SECTOR); memcpy(sector, img + done, part);
            struct burn_param_t r; memset(&r, 0, sizeof r); r.buffer = other; r.length = SECTOR; r.offset = offs; r.in.raw = 1;
            int was_read = dev_ioctl(fd, IO_READ, &r) == 0;
            if (!was_read && !unreadable_admitted)
                STOP(10, "ccsboot: nand read failed at block %x before writing it (%s); stopped there, nothing more is written or retried\n", offs / SECTOR, strerror(errno));
            if (was_read && r.badblocks) { fprintf(stderr, "ccsboot: bad block at %x, not written; the image continues in the next\n", offs / SECTOR); offs += SECTOR; continue; }
            if (!was_read || memcmp(sector, other, SECTOR) != 0)
            {
                struct burn_param_t w; memset(&w, 0, sizeof w); w.buffer = sector; w.length = SECTOR; w.offset = offs; w.in.raw = 1;
                if (dev_ioctl(fd, IO_WRITE, &w) < 0) STOP(8, "ccsboot: nand write failed at block %x; nothing is retried\n", offs / SECTOR);
                if (w.badblocks) { fprintf(stderr, "ccsboot: bad block at %x, the image continues in the next\n", offs / SECTOR); offs += SECTOR; continue; }
                ++written; if (!was_read) ++unread;
                struct burn_param_t b; memset(&b, 0, sizeof b); b.buffer = other; b.length = SECTOR; b.offset = offs; b.in.raw = 1;
                if (dev_ioctl(fd, IO_READ, &b) < 0 || b.badblocks || memcmp(sector, other, SECTOR) != 0)
                    STOP(11, "ccsboot: block %x does not read back as written; stopped there, nothing more is written or retried\n", offs / SECTOR);
            }
            done += part; offs += SECTOR;
        }
    }
#undef STOP
    close(fd); free(img);
    if (unreadable_admitted) printf("written %u unread %u\n", written, unread); else printf("written %u\n", written);
    return 0;
}

int main(int argc, char **argv)
{
    if (argc == 2 && !strcmp(argv[1], "read0")) return read_stage(1);
    if (argc == 2 && !strcmp(argv[1], "read1")) return read_stage(0);
    if (argc == 3 && !strcmp(argv[1], "check"))
    {
        size_t n = 0; uint8_t *img = load(argv[2], &n); const char *why = 0;
        if (!img) { fprintf(stderr, "ccsboot: %s could not be read\n", argv[2]); return 2; }
        int b0 = n >= 11 && !memcmp(img + 4, "eGON.BT", 7);
        int bad = check_image(img, n, b0, &why); free(img);
        if (bad) { fprintf(stderr, "ccsboot: %s\n", why); return 7; }
        printf("ok %zu %s\n", n, b0 ? "boot0" : "boot1"); return 0;
    }
    if (argc == 3 && !strcmp(argv[1], "burn1")) return burn(argv[2], 0, 0);
    if (argc == 3 && !strcmp(argv[1], "burn1-unreadable")) return burn(argv[2], 0, 1);
    if (argc == 3 && !strcmp(argv[1], "burn0")) return burn(argv[2], 1, 0);
    fprintf(stderr, "usage: ccsboot read0|read1|check FILE|burn1 FILE|burn1-unreadable FILE|burn0 FILE\n");
    return 1;
}
