/* host.h: what a platform gives the player (host_win.c: a Windows window, waveOut sound, keyboard, XInput and DirectInput controllers;
 * host_web.c: the browser; host_dev.c: the NES Classic's framebuffer, controllers and sound; host_headless.c: no window, scripted input, for
 * tests). Everything else is shared (gp_session.c). */
#ifndef CCS_HOST_H
#define CCS_HOST_H
#include "gp.h"
#include "../../console-simulator/core/ccs_sim.h"
typedef struct host host;
extern void (*gp_host_present_region)(host *H, const framebuffer *fb, int x, int y, int w, int h);   /* set by a host that can show one changed rectangle (the console's) */
typedef uint32_t (*gp_audio_fill)(void *ctx, int16_t *st, uint32_t frames);   /* the next frames of a sound source; fewer (0) at a failure */
extern int (*gp_host_audio_source)(host *H, gp_audio_fill fill, void *ctx, size_t lead);   /* set by a host with a sound thread of its own (the console's): that
    thread keeps `lead` frames queued from fill, whatever the session's thread is doing; NULL stops it (once it returns, fill neither runs nor will run) */
typedef struct { const char *title, *settings_path, *display_path, *script_path, *trace_path, *shot_dir; int studio_pid; int headless_frames; int posted_input; const char *embed_pipe;
                 const char *session_path; int nargs; char **args; } host_config;   /* embed_pipe: the Studio's Preview (D-104); session_path: the session file; args: the console host's own options (host_dev.c) */
enum { PT_MOVE, PT_CLICK, PT_DOUBLE, PT_BACK, PT_WHEEL };   /* the mouse (the same numbers as home.h's HP_*) */
typedef struct { int kind, x, y, delta; } host_pointer;     /* x, y in stage pixels (-1, -1: outside the picture); delta: wheel notches, +1 away */
typedef struct { int close_requested, menu_key, focus_lost, focus_gained, fullscreen_toggled, layout; host_pointer pt[16]; int npt; char text[32]; int ntext;
                 int studio_exit, preview_close; char theme[48]; int music; char select[200]; char reload[1100]; } host_events;   /* from the Studio (embedded): studio_exit 1 asked, 2 forced, 3 the Studio is gone; preview_close: the Preview's own Close (the pop-out window's X is close_requested); music -1 none */   /* text: typed characters while text input is on ("\b" Backspace, "\n" Enter) */
host *host_open(const host_config *cfg, const gp_settings *s, char *err, size_t errlen);
void host_close(host *H);
void host_poll(host *H, gp_pad *pad, host_events *ev);          /* window messages and devices; pad = what is held now (keyboard + controllers) */
void host_present_nes(host *H, const uint16_t *rgb565, int w, int h);   /* the game frame, scaled by the display settings */
void host_present_canvas(host *H, const framebuffer *fb);       /* the canvas: HOME, menus, the paused game (the 1280 x 720 stage x ccs_scale) */
int host_canvas_scale(void);                                     /* the canvas scale this host wants: the window 2 (CCS_PLAYER_SCALE=1: 1), headless 1 */
void host_home_pace(int *cover_budget, int *music_ahead);        /* the HOME's pace on this host: covers decoded per loop pass, HOME Music frames kept ahead */
int host_audio_open(host *H, int rate);                          /* 0: sound plays; -1: no sound device (the player keeps time by the clock) */
void host_audio_push(host *H, const int16_t *stereo, size_t frames);
void host_audio_pause(host *H, int paused);
size_t host_audio_queued(host *H);
void host_audio_close(host *H);                                  /* the device closed (to open it again at another rate) */                               /* frames queued but not yet played */
void host_set_title(host *H, const char *title);
void host_set_picture(host *H, int tv_aspect, int integer_scale);   /* this game's picture choice (Game Settings), until the next call */
void host_text_input(host *H, int on);                         /* naming a slot: typed keys are text (arrow keys still move), not NES buttons */
void host_cursor(host *H, int visible);                          /* the mouse pointer over the window: hidden while a game plays */
void host_return_focus(host *H);                                 /* hands the foreground back to the Studio window */
int host_pads(host *H, char *out, size_t n);                     /* connected controllers and their players, for the menu */
void host_swap_pads(host *H);
/* every connected controller (Xbox-compatible, USB, Bluetooth), for choosing who plays */
int host_pad_count(host *H);
void host_pad_info(host *H, int i, char *label, size_t n, int *player);   /* its label and the player it plays now (0, 1; -1: none) */
void host_pad_choose(host *H, int player, int i);                         /* controller i plays this player (GP_PICK_NONE: the keyboard; GP_PICK_AUTO: connection order); remembered */
int host_pad_pressed(host *H, int *action);                               /* the controller whose button went down in the last poll (and its action), else -1 */
/* generic (DirectInput-style) controllers, for their setup; each has an id that stays the same while it is connected */
int host_pad_list(host *H, int *ids, int max);                   /* the connected generic controllers */
int host_pad_raw(host *H, int id, gp_raw *raw);                  /* 1 and its raw state while it is connected; 0 when gone */
void host_pad_name(host *H, int id, char *out, size_t n);
int host_pad_set_map(host *H, int id, const int map[GA_COUNT]);  /* uses and saves its whole mapping (player-pads.json); 0 saved */
int host_pad_configured(host *H, int id);                         /* 1 when it has its own setup (remembered or set now), 0 while it uses the common layout */
uint64_t host_now(host *H);                                      /* milliseconds; the headless host advances it by the frame */
void host_trace(host *H, const char *fmt, ...);                  /* one JSON line to the trace file (tests); nothing without one */
int gp_open(const char *session_path, const host_config *hc);   /* gp_session.c: 0 ok; 2 session file, 3 host, 4 HOME */
/* embedded in the Studio's Preview (D-104): the picture goes to the Studio until Pop Out shows this player's own window */
int host_embedded(host *H);
int host_popped(host *H);
void host_popout(host *H, int out);      /* 1: its own window; 0: back into the Studio */
void host_send(host *H, const char *line); /* a line to the Studio (embedded only) */
int host_fullscreen(host *H);
void host_toggle_fullscreen(host *H);                            /* Alt+Enter, F11 and the menu item: the choice is remembered */
#endif
