/* Perspective drawing for the Box Art Showcase boxes (gfx3d.c) */
#ifndef CCS_GFX3D_H
#define CCS_GFX3D_H
#include "home_int.h"
typedef struct { double p, ox, oy; } g3cam;                                   /* CSS perspective + perspective-origin */
typedef struct { double left, top, cx, cs, sn; } g3box;                       /* a box element: position, center x, cos / sin of its rotateY */
void g3_project(const g3cam *c, double X, double Y, double Z, double *sx, double *sy);
void g3_box_point(const g3box *b, double x, double y, double z, double *X, double *Y, double *Z);
int g3_quad(framebuffer *fb, rect clip, const framebuffer *tex, const double qx[4], const double qy[4], int opacity, const uint8_t *row_alpha, int cull);
void g3_angled_gradient(framebuffer *t, double dx, double dy, const stop *s, int n);
void g3_vertical_text(framebuffer *t, const font *f, const char *s, int x, int y, int w, int h, color col, double sp);
#endif
