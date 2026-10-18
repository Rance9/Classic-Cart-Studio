/* Native HOME internals shared by home.c (the HOME shell: package, fonts, covers, controller dispatch, START menu, theme picker,
 * persisted theme preference, Game Details) and the four theme renderers (theme_arcade.c, theme_boxart.c, theme_shelf.c,
 * theme_map.c). Not part of the core's public interface (home.h). */
#ifndef CCS_HOME_INT_H
#define CCS_HOME_INT_H
#include "home.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* ---------- fonts: every face, weight and size the four themes use (names match native_home/derive.py FONTS) ---------- */
#define FONT_LIST(X) \
    X(SILK9, "silk9") X(SILK11, "silk11") X(SILK14, "silk14") X(SILK17, "silk17") X(S400_10, "sans400_10") X(S700_14, "sans700_14") \
    X(S600_14, "sans600_14") X(S800_15, "sans800_15") X(S800_10, "sans800_10") X(S800_40, "sans800_40") X(S700_15, "sans700_15") \
    X(S400_15, "sans400_15") X(S700_16, "sans700_16") X(S400_16, "sans400_16") X(S400_17, "sans400_17") X(S400_19, "sans400_19") \
    X(S400_20, "sans400_20") X(S700_13, "sans700_13") X(SYM16, "sym16") X(S400_14, "sans400_14") \
    X(S700_20, "sans700_20") X(S400_11, "sans400_11") X(PX11, "px11") X(SYM13, "sym13") \
    X(CIN46, "cin700_46") X(CIN20, "cin700_20") X(CIN11, "cin700_11") X(CIN8, "cin700_8") X(S400_9, "sans400_9") X(S700_18, "sans700_18") X(S400_16B, "sans400_16b") \
    X(VT26, "vt26") X(VT23, "vt23") X(VT21, "vt21") X(VT19, "vt19") X(VT18, "vt18") X(VT17, "vt17") \
    X(SILK10, "silk10") X(SILK12, "silk12") X(SILK13, "silk13") X(SILK25, "silk25") X(SILK30, "silk30") \
    X(SILKH46, "silkh46") X(SILKH44, "silkh44") X(SILKH42, "silkh42") X(SILKH40, "silkh40") X(SILKH38, "silkh38") X(SILKH36, "silkh36") X(SILKH34, "silkh34") \
    X(SILKH32, "silkh32") X(SILKH30, "silkh30") X(SILKH28, "silkh28") X(SILKH26, "silkh26") X(SILKH24, "silkh24") X(SILKH22, "silkh22") X(SILKH20, "silkh20") \
    X(PX9, "px9") X(PX8, "px8") X(PX7, "px7") X(PX10, "px10") X(PX13, "px13") X(PX21, "px21") X(PX22, "px22") \
    X(MONO14, "mono14") X(MONO16, "mono16") X(MONO700_16, "mono700_16") X(MONO19, "mono19") X(MONO20, "mono20") X(MONO700_13, "mono700_13") \
    X(CIN43, "cin700_43") X(CIN40, "cin700_40") X(CIN36, "cin700_36") X(CIN32, "cin700_32") X(CIN29, "cin700_29") X(SILKT23, "silkt23") X(SILKT19, "silkt19") X(SILKT16, "silkt16") \
    X(PX19, "px19") X(PX17, "px17") X(PX15, "px15") X(SYM24, "sym24") \
    X(S600_15, "sans600_15") X(S400_12, "sans400_12") X(CIN16, "cin700_16")
#define FONT_ENUM(id, name) F_##id,
enum { FONT_LIST(FONT_ENUM) F_COUNT };
#undef FONT_ENUM

#define RED 0xE4000F
#define RED2 0xFF3B47
#define NAVY 0x2F3F8C
#define MAX_CELLS 24
#define MAX_SPRITES 24
#define MAX_REC 160
#define N_THEMES 4
enum { TH_ARCADE, TH_BOXART, TH_SHELF, TH_MAP };
enum { SCR_GRID, SCR_DETAILS };
enum { Z_GAMES, Z_NAV, Z_MAP, Z_BROWSE };
enum { SEC_ALL, SEC_BROWSE, SEC_FOLDER, SEC_CAT };   /* where the player is (home-state.js sections): All Games, Browse (folder / category tiles), a folder's or a category's games */
enum { OV_NONE, OV_START, OV_THEME, OV_MAPDEST };
enum { DF_PLAY, DF_FAV, DF_DESC };
enum { DK_TWO, DK_ONE, DK_NONE };
enum { D_UP, D_DOWN, D_LEFT, D_RIGHT };

typedef struct { int x0, y0, x1, y1; } box4;
typedef struct { char *id, *title, *version_label, *year, *players, *genre, *publisher, *developer, *cover, *cover_kind, *hero, *hero_kind, *hero_wide, *text, *blurb, *mblurb, *ftext;
                 double ht_x, ht_y, ht_w; int ht_right; } hgame;   /* ht_*: the measured title slot of the game's key art (Featured Shelf) */
typedef struct { char *id, *name, *art; int parent, *games, ng, nchild; image tile; } hfolder;   /* a Windows folder: games are indices into the package order; tile = its artwork at the current tile size */
typedef struct { char *label, *kind; int *games, ng; } hcat;                         /* an automatic category (genre, year, publisher, players) */
typedef struct { int kind, idx; } bitem;                                             /* a Browse tile: BI_FOLDER / BI_GAME (package index) / BI_CAT */
enum { BI_FOLDER, BI_GAME, BI_CAT };
typedef struct { int game, w, h, mode; image im; uint64_t used; } centry;
typedef struct { int game; uint8_t *buf; size_t len; uint64_t used; } bentry;
typedef struct { int font; uint32_t rgb; double y_top, line_h; char *s; size_t len; double spacing, shear; int bold; } dline;
typedef struct { char name[24]; box4 b; } layrec;
typedef struct { char name[24]; image im; box4 box; } tsprite;
typedef struct { char key[24]; image im; } ticon;

/* the Game Details look of one theme (home.css .hm-<theme> .details ...) */
typedef struct {
    int border; uint32_t border_rgb; int inset; uint32_t inset_rgb;   /* the frame around the whole stage */
    stop hz[4], vt[4]; int pixelated;                                 /* .dshade: horizontal column, vertical base; nearest-neighbor backdrop */
    int title_font; double title_lh; uint32_t title_rgb; double title_spacing;
    int ver_bold, ver_font, meta_key, meta_val, meta_icon; double meta_rowh;
    int act_font, act_radius, act_bw; uint32_t act_bg, act_border; int act_bg_a, act_border_a;
    int desc_font; double desc_lh; int desc_radius, desc_bw; uint32_t desc_bg, desc_border; int desc_bg_a, desc_border_a;
    int dh_font; double dh_lh, dh_spacing; int dh_bold;
    int cover_radius, cover_ring; uint32_t cover_ring_rgb; int cover_ring_a;
    int note_font;
} dstyle;

typedef struct {
    int open, game; framebuffer base;
    int kind; char *single; box4 note;
    char *official, *overview, *official_label; dline *lines; size_t nlines, cap; double content_h; int avail, over;
    int acts_y, act_h, play_x, play_w, fav_x, fav_w, desc_x, desc_y, desc_w, desc_h; double acts_top;
    layrec *recs; size_t nrecs, rcap;
} details;

typedef struct {
    int screen, zone;     /* SCR_GRID | SCR_DETAILS; on the HOME screen: Z_GAMES | Z_NAV (tabs, top nav, sidebar) | Z_MAP (landmarks) */
    int sel;              /* the selected game (listing index): the one identity every theme shows and keeps across a theme switch */
    int nav;              /* the focused navigation entry while zone == Z_NAV (0 = the active All Games) */
    int landmark;         /* Adventure Map: the focused landmark while zone == Z_MAP (0 = All Games) */
    int overlay, oindex;  /* the START menu, the theme picker, the map destinations: their focused entry */
    int dfocus, dbutton, dscroll;
    int notice, swallow, repeat_button;
    int sec, sec_id;      /* the section: SEC_ALL / SEC_BROWSE (sec_id = the parent folder, -1 at the top) / SEC_FOLDER / SEC_CAT (sec_id = its index) */
    int browse;           /* the focused Browse tile */
    int dzone;            /* the zone Game Details was opened from (B returns there) */
} navstate;

typedef struct home home;
typedef struct theme_ops {
    const char *id, *name;
    int (*init)(home *h, char *err, size_t errlen);      /* after the scene and sprites are loaded: geometry, derived sprites */
    void (*fini)(home *h);
    void (*render)(home *h, framebuffer *fb, uint64_t now);   /* the HOME screen (overlays and Details are drawn by home.c) */
    void (*input)(home *h, button b);                      /* the HOME screen's zones (games, navigation, landmarks) */
    const dstyle *details;
    int menu_rpg;                                          /* Adventure Map: the rpg START menu */
} theme_ops;
extern const theme_ops THEME_ARCADE, THEME_BOXART, THEME_SHELF, THEME_MAP;

struct home {
    char dir[900]; home_emit_fn emit; hgame *g; size_t n; jval *pk;
    const theme_ops *ops; int theme; const jval *tj, *geo;        /* the active theme: its pack entry and geometry */
    image scene; tsprite sp[MAX_SPRITES]; int nsp; void *tstate;  /* the active theme's resources (freed on a switch) */
    image thumbs[N_THEMES]; ticon icons[24]; int nicons;
    font f[F_COUNT]; centry cache[64]; uint64_t use_seq; size_t cache_bytes; bentry bytes[96]; size_t bytes_total;
    navstate nav; uint64_t changed_at; char notice_title[96], notice_text[600];
    layrec recs[MAX_REC]; int nrec, rec;
    int budget, decoded, pending;
    details d; size_t static_bytes;
    char state_dir[900]; char theme_default[32]; int apply_epoch, saved_epoch; char theme_source[16];
    /* Browse (N3.1): every game of the package (all/nall); the current listing g/n is All Games (g == all) or a folder's or a
     * category's games (shallow copies, vmap = package index of each); the cover caches are keyed by the package index */
    hgame *all; size_t nall; hgame *view; int *vmap;
    hfolder *fold; int nfold; hcat *cats; int ncat; int map_folder[2];
    int *memo_folder, *memo_cat, *memo_browse, memo_all;   /* the selection each listing / Browse level had when the player left it */
    bitem *bi; int nbi, bi_folders, bi_games;               /* the current Browse tiles: folders, then the parent's own games, then categories */
    unsigned gen;                                          /* bumped on every input and section change: themes with a cached frame redraw */
};

/* ---------- helpers shared by the themes (home.c) ---------- */
extern rect FULL;
char *sdup(const char *s);
int iround(double v);
double lh_normal(const font *f);
int baseline_in(const font *f, double top, double line_h);
int baseline_normal(const font *f, double top);
int baseline_center(const font *f, double top, double h);
int T(framebuffer *fb, rect clip, const font *f, double x, int base, const char *s, uint32_t rgb, int a, double sp);
int Tb(framebuffer *fb, rect clip, const font *f, double x, int base, const char *s, uint32_t rgb, int a, double sp);   /* synthetic bold (+1 px) */
void emit_json(home *h, const char *fmt, ...);
void LR(home *h, const char *name, int x0, int y0, int x1, int y1);
void LRtext(home *h, const char *name, const font *f, double x, int base, const char *s, size_t len, double sp);
int gbox(const jval *o, const char *k, box4 *b);             /* a [x0,y0,x1,y1] array member */
int jbox_at(const jval *arr, size_t i, box4 *b);
int gnum(const jval *o, const char *k, int def);
const tsprite *tsp(const home *h, const char *name);          /* the active theme's sprite by name (NULL if absent) */
const image *icon(const home *h, const char *kind, int size); /* "year", 18 ... */
const image *cover_b(home *h, int gi, int w, int hh, int mode, int *ox, int *oy, int must);   /* mode 1 = contain (whole cover) */
const image *cover(home *h, int gi, int w, int hh, int mode, int *ox, int *oy);
void draw_cover(home *h, framebuffer *fb, int gi, box4 b, int must, box4 *art);   /* the whole cover centered in b; art = its rectangle */
void notice(home *h, const char *title, const char *text);
void not_yet(home *h, const char *what);
void launch_unavailable(home *h, int gi);
int open_details(home *h, int gi);                            /* SELECT: Game Details of game gi */
void legend_items(home *h, framebuffer *fb, box4 box, int center, int theme_style);   /* the controller legend for the current state */
size_t overview_clamp(const font *f, const char *s, int maxw, int lines, double sp, span *out, char *last, size_t lastlen);   /* line-clamp with ellipsis */
void draw_glow_text(framebuffer *fb, home *h, int fid, double x, int base, const char *s, size_t len, double sigma, color glow, double sp);
int fit_font(const home *h, const int *fonts, int n, const char *s, int maxw, double sp);   /* the first face the text fits (homeFitText), else the last */
void upper(const char *s, char *out, size_t n);
const image *sprite_scaled(home *h, const char *name, int w, int hh, int nearest, image *tmp);   /* the sprite at a size (tmp holds a scaled copy) */
int dir_of(button b);
/* sections (home.c) */
int home_master(const home *h, int gi);                       /* package index of listing index gi */
int home_active_nav(const home *h);                           /* the active entry of a theme's navigation: 0 All Games, 1 Browse (folders, categories) */
int home_landmark_active(const home *h, int k);               /* Adventure Map: is landmark k the current section? */
const char *home_section_title(const home *h);                /* "All Games", the folder's name, the category's label */
int home_section_zone(const home *h);                         /* Z_BROWSE in Browse, else Z_GAMES */
void home_go_all(home *h);
void home_go_browse(home *h, int parent);
void home_go_folder(home *h, int f);
int home_back(home *h);                                       /* B in a listing or in Browse: one level up; 0 when already at the top */
#endif
