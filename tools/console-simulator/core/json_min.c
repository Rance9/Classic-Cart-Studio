/* Minimal strict JSON parser for the simulator core (fixtures are small, trusted-format developer files; every parse error
 * is reported with a position so malformed fixtures fail loudly rather than silently). */
#include "ccs_sim.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>

typedef struct { const char *start, *p, *end; char *err; size_t errlen; int depth; } parser;

static void perr(parser *ps, const char *msg) { if (ps->err && ps->errlen && !ps->err[0]) snprintf(ps->err, ps->errlen, "%s at offset %ld", msg, (long)(ps->p - ps->start)); }
static void skip_ws(parser *ps) { while (ps->p < ps->end && (*ps->p == ' ' || *ps->p == '\t' || *ps->p == '\n' || *ps->p == '\r')) ps->p++; }
static jval *jnew(jtype t) { jval *v = calloc(1, sizeof *v); if (v) v->t = t; return v; }

static int hexval(char c) { if (c >= '0' && c <= '9') return c - '0'; if (c >= 'a' && c <= 'f') return c - 'a' + 10; if (c >= 'A' && c <= 'F') return c - 'A' + 10; return -1; }

static char *parse_string(parser *ps) {
    if (ps->p >= ps->end || *ps->p != '"') { perr(ps, "expected string"); return NULL; }
    ps->p++; size_t cap = 32, n = 0; char *out = malloc(cap); if (!out) { perr(ps, "out of memory"); return NULL; }
#define PUT(c) do { if (n + 4 >= cap) { cap *= 2; char *t = realloc(out, cap); if (!t) { free(out); perr(ps, "out of memory"); return NULL; } out = t; } out[n++] = (char)(c); } while (0)
    while (ps->p < ps->end) {
        unsigned char c = (unsigned char)*ps->p++;
        if (c == '"') { out[n] = 0; return out; }
        if (c < 0x20) { free(out); perr(ps, "control character in string"); return NULL; }
        if (c != '\\') { PUT(c); continue; }
        if (ps->p >= ps->end) break;
        char e = *ps->p++;
        switch (e) {
            case '"': PUT('"'); break; case '\\': PUT('\\'); break; case '/': PUT('/'); break;
            case 'b': PUT('\b'); break; case 'f': PUT('\f'); break; case 'n': PUT('\n'); break; case 'r': PUT('\r'); break; case 't': PUT('\t'); break;
            case 'u': {
                if (ps->end - ps->p < 4) { free(out); perr(ps, "truncated \\u escape"); return NULL; }
                unsigned cp = 0; for (int i = 0; i < 4; i++) { int h = hexval(ps->p[i]); if (h < 0) { free(out); perr(ps, "bad \\u escape"); return NULL; } cp = cp * 16 + (unsigned)h; } ps->p += 4;
                if (cp >= 0xD800 && cp <= 0xDBFF && ps->end - ps->p >= 6 && ps->p[0] == '\\' && ps->p[1] == 'u') {
                    unsigned lo = 0; int ok = 1; for (int i = 0; i < 4; i++) { int h = hexval(ps->p[2 + i]); if (h < 0) ok = 0; else lo = lo * 16 + (unsigned)h; }
                    if (ok && lo >= 0xDC00 && lo <= 0xDFFF) { cp = 0x10000 + ((cp - 0xD800) << 10) + (lo - 0xDC00); ps->p += 6; }
                }
                if (cp < 0x80) PUT(cp);
                else if (cp < 0x800) { PUT(0xC0 | (cp >> 6)); PUT(0x80 | (cp & 0x3F)); }
                else if (cp < 0x10000) { PUT(0xE0 | (cp >> 12)); PUT(0x80 | ((cp >> 6) & 0x3F)); PUT(0x80 | (cp & 0x3F)); }
                else { PUT(0xF0 | (cp >> 18)); PUT(0x80 | ((cp >> 12) & 0x3F)); PUT(0x80 | ((cp >> 6) & 0x3F)); PUT(0x80 | (cp & 0x3F)); }
                break; }
            default: free(out); perr(ps, "bad escape"); return NULL;
        }
    }
    free(out); perr(ps, "unterminated string"); return NULL;
#undef PUT
}

static jval *parse_value(parser *ps);

static jval *parse_array(parser *ps) {
    jval *v = jnew(J_ARR); if (!v) { perr(ps, "out of memory"); return NULL; } ps->p++; skip_ws(ps);
    if (ps->p < ps->end && *ps->p == ']') { ps->p++; return v; }
    size_t cap = 8;
    v->items = malloc(cap * sizeof *v->items); if (!v->items) { json_free(v); perr(ps, "out of memory"); return NULL; }
    for (;;) {
        skip_ws(ps); jval *it = parse_value(ps); if (!it) { json_free(v); return NULL; }
        if (v->n == cap) { cap *= 2; jval **t = realloc(v->items, cap * sizeof *t); if (!t) { json_free(it); json_free(v); perr(ps, "out of memory"); return NULL; } v->items = t; }
        v->items[v->n++] = it; skip_ws(ps);
        if (ps->p < ps->end && *ps->p == ',') { ps->p++; continue; }
        if (ps->p < ps->end && *ps->p == ']') { ps->p++; return v; }
        json_free(v); perr(ps, "expected , or ] in array"); return NULL;
    }
}

static jval *parse_object(parser *ps) {
    jval *v = jnew(J_OBJ); if (!v) { perr(ps, "out of memory"); return NULL; } ps->p++; skip_ws(ps);
    if (ps->p < ps->end && *ps->p == '}') { ps->p++; return v; }
    size_t cap = 8;
    v->items = malloc(cap * sizeof *v->items); v->keys = malloc(cap * sizeof *v->keys);
    if (!v->items || !v->keys) { json_free(v); perr(ps, "out of memory"); return NULL; }
    for (;;) {
        skip_ws(ps); char *k = parse_string(ps); if (!k) { json_free(v); return NULL; }
        skip_ws(ps); if (ps->p >= ps->end || *ps->p != ':') { free(k); json_free(v); perr(ps, "expected : after key"); return NULL; } ps->p++;
        skip_ws(ps); jval *it = parse_value(ps); if (!it) { free(k); json_free(v); return NULL; }
        if (v->n == cap) { cap *= 2; jval **t = realloc(v->items, cap * sizeof *t); char **tk = realloc(v->keys, cap * sizeof *tk); if (!t || !tk) { free(k); json_free(it); json_free(v); perr(ps, "out of memory"); return NULL; } v->items = t; v->keys = tk; }
        v->keys[v->n] = k; v->items[v->n] = it; v->n++; skip_ws(ps);
        if (ps->p < ps->end && *ps->p == ',') { ps->p++; continue; }
        if (ps->p < ps->end && *ps->p == '}') { ps->p++; return v; }
        json_free(v); perr(ps, "expected , or } in object"); return NULL;
    }
}

static jval *parse_value(parser *ps) {
    if (++ps->depth > 64) { perr(ps, "nesting too deep"); return NULL; }
    skip_ws(ps); jval *r = NULL;
    if (ps->p >= ps->end) { perr(ps, "unexpected end of input"); ps->depth--; return NULL; }
    char c = *ps->p;
    if (c == '{') r = parse_object(ps);
    else if (c == '[') r = parse_array(ps);
    else if (c == '"') { char *s = parse_string(ps); if (s) { r = jnew(J_STR); if (r) r->s = s; else free(s); } }
    else if (!strncmp(ps->p, "true", 4) && ps->end - ps->p >= 4) { ps->p += 4; r = jnew(J_BOOL); if (r) r->b = 1; }
    else if (!strncmp(ps->p, "false", 5) && ps->end - ps->p >= 5) { ps->p += 5; r = jnew(J_BOOL); }
    else if (!strncmp(ps->p, "null", 4) && ps->end - ps->p >= 4) { ps->p += 4; r = jnew(J_NULL); }
    else if (c == '-' || (c >= '0' && c <= '9')) {
        const char *s = ps->p; if (*ps->p == '-') ps->p++;
        if (ps->p >= ps->end || *ps->p < '0' || *ps->p > '9') { perr(ps, "bad number"); ps->depth--; return NULL; }
        if (*ps->p == '0') ps->p++; else while (ps->p < ps->end && *ps->p >= '0' && *ps->p <= '9') ps->p++;
        if (ps->p < ps->end && *ps->p == '.') { ps->p++; if (ps->p >= ps->end || *ps->p < '0' || *ps->p > '9') { perr(ps, "bad fraction"); ps->depth--; return NULL; } while (ps->p < ps->end && *ps->p >= '0' && *ps->p <= '9') ps->p++; }
        if (ps->p < ps->end && (*ps->p == 'e' || *ps->p == 'E')) { ps->p++; if (ps->p < ps->end && (*ps->p == '+' || *ps->p == '-')) ps->p++; if (ps->p >= ps->end || *ps->p < '0' || *ps->p > '9') { perr(ps, "bad exponent"); ps->depth--; return NULL; } while (ps->p < ps->end && *ps->p >= '0' && *ps->p <= '9') ps->p++; }
        char buf[64]; size_t n = (size_t)(ps->p - s); if (n >= sizeof buf) { perr(ps, "number too long"); ps->depth--; return NULL; }
        memcpy(buf, s, n); buf[n] = 0; r = jnew(J_NUM); if (r) r->num = strtod(buf, NULL);
    }
    else perr(ps, "unexpected character");
    if (!r && ps->err && !ps->err[0]) perr(ps, "out of memory");
    ps->depth--; return r;
}

jval *json_parse(const char *text, size_t len, char *err, size_t errlen) {
    if (err && errlen) err[0] = 0;
    parser ps = { text, text, text + len, err, errlen, 0 };
    jval *v = parse_value(&ps); if (!v) return NULL;
    skip_ws(&ps); if (ps.p != ps.end) { json_free(v); if (err && errlen) snprintf(err, errlen, "trailing characters at offset %ld", (long)(ps.p - text)); return NULL; }
    return v;
}

void json_free(jval *v) {
    if (!v) return;
    if (v->t == J_STR) free(v->s);
    if (v->t == J_ARR || v->t == J_OBJ) { for (size_t i = 0; i < v->n; i++) { json_free(v->items[i]); if (v->keys) free(v->keys[i]); } free(v->items); free(v->keys); }
    free(v);
}

const jval *json_get(const jval *obj, const char *key) {
    if (!obj || obj->t != J_OBJ) return NULL;
    for (size_t i = 0; i < obj->n; i++) if (!strcmp(obj->keys[i], key)) return obj->items[i];
    return NULL;
}

size_t json_escape(const char *s, char *out, size_t outlen) {
    size_t n = 0;
#define PUTC(c) do { if (n + 1 < outlen) out[n] = (char)(c); n++; } while (0)
    PUTC('"');
    for (const unsigned char *p = (const unsigned char *)(s ? s : ""); *p; p++) {
        if (*p == '"' || *p == '\\') { PUTC('\\'); PUTC(*p); }
        else if (*p < 0x20) { char b[8]; snprintf(b, sizeof b, "\\u%04x", *p); for (char *q = b; *q; q++) PUTC(*q); }
        else PUTC(*p);
    }
    PUTC('"'); if (outlen) out[n < outlen ? n : outlen - 1] = 0; return n;
#undef PUTC
}
