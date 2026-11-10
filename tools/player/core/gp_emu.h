/* gp_emu.h: the emulator as the player uses it (gp_emu.c, FCEUmm through libretro) */
#ifndef CCS_GP_EMU_H
#define CCS_GP_EMU_H
#include "gp.h"
int emu_init(const char *scratch_dir);
int emu_load(const gp_rom *r, const char *name, char *why, size_t whylen);   /* name: the exact version's name (region detection) */
void emu_unload(void);
void emu_run(const uint8_t in[GP_PLAYERS][GA_COUNT]);          /* one video frame */
const uint16_t *emu_frame(int *w, int *h);                       /* RGB565, row stride 256 pixels */
const int16_t *emu_audio(size_t *frames);                        /* the last frame's stereo samples */
double emu_fps(void);
double emu_rate(void);
uint64_t emu_frames(void);
size_t emu_state_size(void);
int emu_save_state(void *buf, size_t n);
int emu_load_state(const void *buf, size_t n);
uint8_t *emu_sram(size_t *len);                                  /* the battery RAM (NULL when the cartridge has none) */
void emu_reset(void);
const char *emu_core_name(void);
#endif
