/* Classic Cart Studio game player: HOME Music as MP3 (builds made with -DGP_HOME_MP3 only). Copyright (C) 2026 Rance9, GPL-3.0-or-later.
 * The song is held in memory and decoded a piece at a time by minimp3's extended decoder (third_party/minimp3, CC0-1.0), from its first
 * real sample to its last: the encoder's delay at the start and its padding at the end are left out by the counts the file's own
 * Info/Xing tag gives (LAME's form, as an encoder writes it). A file without those counts, or whose decoded length does not equal them,
 * is refused: its length is never guessed. Stereo at 48,000 Hz only, the same as the PCM file. */
#ifndef GP_MP3_H
#define GP_MP3_H
#include <stddef.h>
#include <stdint.h>

typedef struct gp_mp3 gp_mp3;
/* buf: the whole file (it stays the caller's and must outlive the decoder). frames: the song's length in stereo frames. NULL with why. */
gp_mp3 *gp_mp3_open(const uint8_t *buf, size_t len, uint32_t *frames, char *why, size_t whylen);
/* up to frames stereo frames into out (interleaved); fewer only at the song's end or on a decoding error */
uint32_t gp_mp3_read(gp_mp3 *m, int16_t *out, uint32_t frames);
/* to this frame of the song (0: its beginning); the decoder rebuilds its state from the frames before it. 0 ok */
int gp_mp3_seek(gp_mp3 *m, uint32_t frame);
void gp_mp3_close(gp_mp3 *m);
/* the memory the decoder holds besides the file: its state and its frame index */
size_t gp_mp3_heap(const gp_mp3 *m);
#endif
