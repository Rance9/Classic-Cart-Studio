/* CCS HOME Designer on the console: a compiled design (tools/console-backup/HOME-DESIGNER.md), checked, resolved and drawn.
 *
 * A compiled design is one folder: design.json (schema ccs-home-design-compiled/1, written byte for byte by the PC's one compiler,
 * CcsBackup.Compose.DesignCompiler) and its pictures img/<sha256>.png|.jpg, each named by its own SHA-256. On the console the folders
 * live under the design root (<data>/home-design: d/<SHA-256 of design.json>/...), with two small pointer files written by the PC in
 * one rename each: "current" and "known-good" (a design id, or "none").
 *
 * Read order at every start (design_resolve): current -> known-good -> the theme defaults. "none" in current means the person chose the
 * theme defaults (Revert Design): known-good is then not used. A design is used only when ALL of it checks out (design_load): its
 * design.json is the one its folder names, every picture is there with its size and SHA-256, its kind and header size, every value is
 * within the profile and the budgets below. Anything else and the whole design is set aside for the next step: a design is shown
 * completely or not at all (never a half-applied picture set, never a blank or crashed HOME). Saves, games and the person's own
 * choices are never part of a design. Integer arithmetic only, like the rest of the core. */
#ifndef CCS_DESIGN_H
#define CCS_DESIGN_H
#include "home.h"

#define DESIGN_SCHEMA "ccs-home-design-compiled/1"
#define DESIGN_SCHEMA2 "ccs-home-design-compiled/2"   /* the same, with control regions (a HOME that reads only /1 sets such a design aside) */
#define DESIGN_PROFILE "nes-classic-home-1280x720/1"
#define DESIGN_MAX_FILES 48
#define DESIGN_MAX_LAYERS 32
#define DESIGN_MAX_FOLDERS 200
#define DESIGN_MAX_FOLDER_GAMES 2000
#define DESIGN_MAX_ORDER 5000
#define DESIGN_MAX_FILE_BYTES 4000000u        /* one picture */
#define DESIGN_MAX_TOTAL_BYTES 64000000u      /* every picture of a design together */
#define DESIGN_MAX_JSON_BYTES 2000000u
#define DESIGN_MAX_STARTUP_BYTES 450000u
/* The enforced decoded-picture budget (HOME-DESIGNER.md, "Budgets"): the sum, over the distinct pictures a design uses (the startup
 * picture apart), of width x height x 4 bytes. It bounds the decoding work a start does; what stays resident is far less (the
 * pictures are drawn into the HOME's one background frame and freed). Measured with this renderer and set to the measured worst case
 * of a design at its file limits. Kept equal to CcsBackup.Compose.DesignProfile.ActiveDecodedBudget. */
#define DESIGN_DECODED_BUDGET 29491200u       /* 8 full 1280 x 720 RGBA frames */
/* The measured peak budget (HOME-DESIGNER.md "Budgets"): the most decoded-picture memory (pictures plus the decoder's and resampler's
 * working memory, design.h accounting) a design may add, at its worst moment, over the same HOME without it. The render check refuses a
 * design over it. Kept equal to CcsBackup.Compose.DesignProfile.ActiveDecodedBudget. */
#define DESIGN_PEAK_BUDGET 16000000u

enum { DS_SCENE, DS_BACKDROP, DS_PLATE, DS_ICON_GAMES, DS_ICON_FOLDER, DS_ICON_STAR, DS_ICON_CLOCK, DS_ICON_GEAR, DS_COUNT };
enum { DL_IMAGE, DL_PANEL, DL_LABEL };
enum { DSU_NONE, DSU_RANDOM, DSU_BUILTIN, DSU_IMAGE };
/* the HOME's own controls in the groups a design may move in its base theme (HOME-DESIGNER.md "Control regions"): the game list, the
   selected game's details, its title, the collection status, the controller hints. Each group keeps its theme's size and moves whole. */
enum { UI_GAMES, UI_DETAILS, UI_TITLE, UI_STATUS, UI_HINTS, UI_COUNT };
extern const char *const DESIGN_UI[UI_COUNT];
typedef struct { char sha[65]; char name[80]; int png, w, h; uint32_t bytes; } dfile;
typedef struct { int kind, x, y, w, h, opacity, file, font, align, has_color; uint32_t color; char *text; } dlayer;   /* align 0 left, 1 center, 2 right */
typedef struct { int set, x, y, w, h; } dregion;   /* a group's place in the base theme (set 0: the theme's own place) */
typedef struct { char *id, *name; int parent, art; char **games; int ng; } dfolder;
typedef struct hdesign {
    char dir[900], id[65], src[16];          /* src: where it came from: "current", "known-good", "explicit" */
    char name[200]; int theme;               /* the base theme (index in the four) */
    dfile f[DESIGN_MAX_FILES]; int nf; int slot[DS_COUNT];   /* slot -> file index, -1 none */
    dlayer *layer; int nlayer;
    dregion reg[UI_COUNT]; int nreg;         /* the control groups this design moves (checked against the package's own layout: design_layout) */
    int has_accent, has_panel, has_text; uint32_t accent, panel, text; int panel_a, title_font, text_font;   /* fonts: index in FONT_LIST, -1 */
    dfolder *fold; int nfold, has_folders;   /* has_folders 0: the collection's own folders stay */
    char **order; int norder;                /* 0: the package's own All Games order stays */
    int map_folder[2];
    int reduced_motion, music;               /* -1: not set */
    int startup_mode, startup_file; char startup_builtin[32];
    uint64_t decoded_bytes, file_bytes;
} hdesign;

/* dir: a compiled design folder; expect_id: the SHA-256 its design.json must have (NULL: any). 0 ok (*out set), -1 with why */
int design_load(const char *dir, const char *expect_id, hdesign **out, char *why, size_t n);
/* root: the design root (current, known-good, d/<id>/). 0 with *out (a design) or NULL (the theme defaults); src says which step was
   used ("current", "known-good", "defaults", "chosen-defaults" or "none"), why lists what was set aside on the way */
int design_resolve(const char *root, hdesign **out, char *src, size_t sn, char *why, size_t n);
/* the same, with one more check a design must pass at each step (the HOME's: its control regions against the package's layout); NULL none */
typedef int (*design_check_fn)(void *ctx, const hdesign *d, char *why, size_t n);
int design_resolve2(const char *root, design_check_fn check, void *ctx, hdesign **out, char *src, size_t sn, char *why, size_t n);
void design_free(hdesign *d);
int design_path(const hdesign *d, int file, char *out, size_t n);
/* decodes one of the design's pictures (its SHA-256 checked again on the bytes read); alpha: 4 channels */
int design_decode(const hdesign *d, int file, image *im, int alpha, char *why, size_t n);
/* the startup picture the design chooses: DSU_NONE (the package's own choice), DSU_RANDOM, DSU_BUILTIN (builtin id) or DSU_IMAGE
   (path and its SHA-256) */
int design_startup(const hdesign *d, char *path, size_t pn, char *sha, char *builtin, size_t bn);
/* the startup choice of the console's design, read at once before the startup image (the game player's first step): current, else
   known-good; only its design.json is checked here (its SHA-256 against its folder name); the picture's own SHA-256 is checked by the
   player as it reads it. DSU_NONE when no design is chosen or none can be read (the package's own startup choice is then used) */
int design_peek_startup(const char *root, char *path, size_t pn, char *sha, char *builtin, size_t bn);
uint32_t design_color(const char *hex, int *ok);

void ccs_sha256(const void *data, size_t len, uint8_t out[32]);
void ccs_sha256_hex(const void *data, size_t len, char out[65]);

/* decoded picture accounting (gfx.c): bytes of decoded pictures alive and the decoder's working memory, with their peaks */
extern size_t ccs_mem_img_live, ccs_mem_img_peak, ccs_mem_stb_live, ccs_mem_stb_peak, ccs_mem_total_peak;
void ccs_mem_reset_peaks(void);
void ccs_mem_img_add(size_t n);
void ccs_mem_img_sub(size_t n);
#endif
