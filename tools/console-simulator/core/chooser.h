/* CCS Nucleus's start chooser on the TV: the screen where the console's own
 * system, CCS HOME and the owner's own system images are chosen. A mode of the native HOME program (ccs-home-armv7 --choose, dispatched
 * from main.c); the CCS Nucleus script (console/nucleus/bin/ccs-nucleus) decides what is offered, writes it to a ccs-choices/1 file and
 * acts on the one answer line this program prints. This program decides nothing about the console, starts nothing and writes no file: it
 * shows the choices, takes the controller and answers.
 *
 * ccs-choices/1 (LF lines key=value; the first schema=ccs-choices/1; unknown keys are ignored):
 *   mode=boot|menu          boot: the console has just started; menu: opened from the console's own menu (B goes back there)
 *   title=TEXT              the screen's title
 *   highlight=ID|KEY        the row focused first: an option (the last choice or the default) or, with panel=settings, a setting
 *   auto=ID timeout=N       the countdown: after N seconds (1-99) option ID starts by itself, unless a button is pressed first
 *   notice=TEXT help=TEXT   a line at the top (why the last choice did not start; what an action did) and the settings panel's text
 *   panel=main|settings     the panel shown first
 *   settings=LABEL          the last row of the main panel, opening the settings panel (absent: no such row)
 *   choice=ID|KIND|ROOT|STATE|LABEL|REASON   one option, in the order shown. KIND native|ccs-home|owner-root; ROOT same (starts in the
 *                           system running now) | restart (the console restarts into it); STATE ok | retry (did not start last time: never
 *                           started by the countdown) | unavailable (refused, with its REASON); exactly one option "native", always ok
 *   setting=KEY|LABEL       one row of the settings panel (the script names each action and its label); a row whose KEY is an option's ID
 *                           starts that option like the option itself (the recovery entry "Start Your Console's Own Menu")
 * Answer (stdout, one line, exit 0): "choice ID a|start|timeout", "back" (menu mode, B) or "action KEY" (a settings row). Nothing is
 * printed when the program is stopped (exit 15) or cannot run (2 usage, 3 the choices are not valid, 12 no framebuffer). Copyright (C) 2026
 * Rance9, GPL-3.0-or-later. */
#ifndef CCS_CHOOSER_H
#define CCS_CHOOSER_H
#include "ccs_sim.h"
#include "home.h"

#define CH_MAX_OPT 24
#define CH_MAX_SET 12
enum { CH_OK, CH_RETRY, CH_UNAVAILABLE };
enum { CH_MAIN, CH_SETTINGS };
typedef struct { char id[33], kind[12], label[64], reason[200]; int restart, state; } ch_opt;
typedef struct { char key[33], label[80]; } ch_set;
typedef struct chooser {
    char title[64], notice[300], help[300], settings_label[64], highlight[33]; int menu_mode;
    ch_opt opt[CH_MAX_OPT]; int nopt; ch_set set[CH_MAX_SET]; int nset;
    int panel, focus, top, sfocus, auto_idx, timeout_s, counting; uint64_t deadline_ms, now;   /* focus: a main row (options, then the settings row) */
    char answer[96], refusal[300]; int done; unsigned gen;   /* gen: changes whenever the picture changes */
    font f_title, f_item, f_text; int fonts;
} chooser;

int chooser_parse(chooser *c, const char *text, size_t len, char *err, size_t n);   /* 0, or -1 with why the choices are not valid */
int chooser_fonts(chooser *c, const char *dir);         /* the player's menu fonts from the CCS HOME package (1 loaded; 0: the built-in letters) */
void chooser_start(chooser *c, uint64_t now_ms);
void chooser_input(chooser *c, button b, in_kind k, uint64_t now_ms);   /* press and repeat move or choose; a release does nothing */
void chooser_tick(chooser *c, uint64_t now_ms);                       /* the countdown */
int chooser_countdown_ms(const chooser *c);                            /* -1 when no countdown runs */
void chooser_render(chooser *c, framebuffer *fb);                    /* the 1280 x 720 picture of the current state */
void chooser_render_final(chooser *c, framebuffer *fb);              /* after a choice: what starts now (shown until the next program draws) */
void chooser_free(chooser *c);
int chooser_main(int argc, char **argv);   /* --choose FILE ...: the console (Linux framebuffer and controllers), or --script (commands on stdin) */
#endif
