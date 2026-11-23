/* Classic Cart Studio game player: HOME Music as MP3 (gp_mp3.h). Copyright (C) 2026 Rance9, GPL-3.0-or-later.
 * minimp3 (third_party/minimp3, commit 142823796ab8, CC0-1.0) is compiled here, only in builds made with -DGP_HOME_MP3. */
#ifdef GP_HOME_MP3
#define MINIMP3_IMPLEMENTATION
#define MINIMP3_NO_STDIO
#include "../third_party/minimp3/minimp3_ex.h"
#include "gp_mp3.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

struct gp_mp3 { mp3dec_ex_t d; uint32_t frames; };

/* the file's own Info/Xing tag (in its first frame, after any ID3v2 tag): its frame count and the encoder's delay and padding, read here
   independently of the decoder so the two can be compared. MPEG-1 Layer III only. 1 found; 0 absent or unusable */
static int tag_counts(const uint8_t *b, size_t n, uint32_t *frames, uint32_t *delay, uint32_t *padding) {
    size_t p = 0;
    if (n >= 10 && !memcmp(b, "ID3", 3)) p = 10 + ((size_t)(b[6] & 0x7f) << 21 | (size_t)(b[7] & 0x7f) << 14 | (size_t)(b[8] & 0x7f) << 7 | (size_t)(b[9] & 0x7f));
    if (p + 4 > n || b[p] != 0xff || (b[p + 1] & 0xfe) != 0xfa) return 0;   /* the first frame: MPEG-1 Layer III */
    size_t side = (b[p + 3] >> 6) == 3 ? 17 : 32, t = p + 4 + ((b[p + 1] & 1) ? 0 : 2) + side;
    if (t + 120 > n || (memcmp(b + t, "Xing", 4) && memcmp(b + t, "Info", 4))) return 0;
    uint32_t flags = (uint32_t)b[t + 7]; t += 8;
    if (!(flags & 1)) return 0;
    *frames = (uint32_t)b[t] << 24 | (uint32_t)b[t + 1] << 16 | (uint32_t)b[t + 2] << 8 | b[t + 3]; t += 4;
    if (flags & 2) t += 4;
    if (flags & 4) t += 100;
    if (flags & 8) t += 4;
    if (t + 24 > n || !b[t]) return 0;   /* no encoder extension: no delay or padding to trust */
    *delay = (uint32_t)b[t + 21] << 4 | b[t + 22] >> 4; *padding = (uint32_t)(b[t + 22] & 15) << 8 | b[t + 23];
    return 1; }

gp_mp3 *gp_mp3_open(const uint8_t *buf, size_t len, uint32_t *frames, char *why, size_t whylen) {
    uint32_t tf = 0, td = 0, tp = 0;
    if (!tag_counts(buf, len, &tf, &td, &tp)) { snprintf(why, whylen, "the MP3 file has no Info/Xing tag with its encoder delay and padding"); return NULL; }
    gp_mp3 *m = calloc(1, sizeof *m); if (!m) { snprintf(why, whylen, "out of memory"); return NULL; }
    int r = mp3dec_ex_open_buf(&m->d, buf, len, MP3D_SEEK_TO_SAMPLE);
    if (r) { free(m); snprintf(why, whylen, "the MP3 file could not be opened (%d)", r); return NULL; }
    uint64_t want = (uint64_t)tf * 1152;
    want = want > (uint64_t)td + tp ? want - td - tp : 0;
    if (m->d.info.layer != 3 || m->d.info.hz != 48000 || m->d.info.channels != 2) { snprintf(why, whylen, "the MP3 file is not stereo at 48000 Hz"); gp_mp3_close(m); return NULL; }
    if (!m->d.vbr_tag_found || !m->d.start_delay || m->d.samples != want * 2 || !want || want > 0xffffffffu) {
        snprintf(why, whylen, "the decoder's length (%llu frames) does not equal the tag's (%llu: %u frames, delay %u, padding %u)",
                 (unsigned long long)(m->d.samples / 2), (unsigned long long)want, tf, td, tp); gp_mp3_close(m); return NULL; }
    if (mp3dec_ex_seek(&m->d, 0)) { snprintf(why, whylen, "the MP3 file could not be indexed"); gp_mp3_close(m); return NULL; }   /* its frame index, built now (never during playing) */
    if (m->d.index.num_frames != tf) {   /* the frames really in the file, counted by the index, against the tag's count (which the decoder's length is made from) */
        snprintf(why, whylen, "the MP3 file holds %llu frames but its tag says %u", (unsigned long long)m->d.index.num_frames, tf); gp_mp3_close(m); return NULL; }
    m->frames = (uint32_t)want; *frames = m->frames; return m; }

uint32_t gp_mp3_read(gp_mp3 *m, int16_t *out, uint32_t frames) { return m ? (uint32_t)(mp3dec_ex_read(&m->d, out, (size_t)frames * 2) / 2) : 0; }
int gp_mp3_seek(gp_mp3 *m, uint32_t frame) { return !m || frame > m->frames || mp3dec_ex_seek(&m->d, (uint64_t)frame * 2) ? -1 : 0; }
void gp_mp3_close(gp_mp3 *m) { if (!m) return; mp3dec_ex_close(&m->d); free(m); }
size_t gp_mp3_heap(const gp_mp3 *m) { return m ? sizeof *m + m->d.index.capacity * sizeof(mp3dec_frame_t) : 0; }
#endif
