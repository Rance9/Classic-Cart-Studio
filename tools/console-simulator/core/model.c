/* Collection model: loads a ccs-sim-fixture/1 file. Every malformed record is rejected with a reason and reported; nothing
 * is silently substituted. The menu order is deterministic (order, then sort key, then id). A memory budget (bytes) can be
 * imposed so that low-memory behavior is testable on every platform; it is a self-imposed, simulated budget. */
#include "ccs_sim.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>

const char *kind_name(entry_kind k) { static const char *n[] = { "factory-visible", "factory-hidden", "added", "personal", "alternate" }; return n[k]; }
const char *ready_name(ready_state r) { static const char *n[] = { "ready", "missing", "unknown", "n/a" }; return n[r]; }
const char *lstate_name(lstate s) { static const char *n[] = { "boot", "ready", "launching", "suspended", "shutdown" }; return n[s]; }

static int kind_parse(const char *s, entry_kind *out) {
    static const char *n[] = { "factory-visible", "factory-hidden", "added", "personal", "alternate" };
    for (int i = 0; i < 5; i++) if (!strcmp(s, n[i])) { *out = (entry_kind)i; return 1; }
    return 0;
}
static int ready_parse(const char *s, ready_state *out) {
    static const char *n[] = { "ready", "missing", "unknown", "n/a" };
    for (int i = 0; i < 4; i++) if (!strcmp(s, n[i])) { *out = (ready_state)i; return 1; }
    return 0;
}

static char *dup_charged(model *m, const char *s) { size_t n = strlen(s) + 1; char *d = malloc(n); if (d) { memcpy(d, s, n); m->bytes += n; } return d; }

static void reject(model *m, size_t *cap, const char *record, const char *reason) {
    if (m->rejected_n == *cap) { size_t nc = *cap ? *cap * 2 : 8; rejection *t = realloc(m->rejected, nc * sizeof *t); if (!t) return; m->rejected = t; *cap = nc; }
    m->rejected[m->rejected_n].record = dup_charged(m, record); m->rejected[m->rejected_n].reason = dup_charged(m, reason); m->rejected_n++;
}

static const char *str_field(const jval *o, const char *k) { const jval *v = json_get(o, k); return (v && v->t == J_STR) ? v->s : NULL; }

/* sort key: lowercase ASCII, leading "the " dropped; non-ASCII bytes kept as-is (deterministic byte order) */
static char *make_sort_key(model *m, const char *title) {
    const char *s = title; if (strlen(s) > 4 && tolower((unsigned char)s[0]) == 't' && tolower((unsigned char)s[1]) == 'h' && tolower((unsigned char)s[2]) == 'e' && s[3] == ' ') s += 4;
    char *k = dup_charged(m, s); if (!k) return NULL;
    for (char *p = k; *p; p++) if ((unsigned char)*p < 0x80) *p = (char)tolower((unsigned char)*p);
    return k;
}

static int cmp_menu(const void *a, const void *b, void *ctx) {
    const model *m = ctx; const entry *x = &m->entries[*(const size_t *)a], *y = &m->entries[*(const size_t *)b];
    if (x->order != y->order) return x->order < y->order ? -1 : 1;
    int c = strcmp(x->sort_key, y->sort_key); if (c) return c;
    return strcmp(x->id, y->id);
}
/* portable stable sort (insertion into a merge is overkill: fixtures up to a few thousand entries; use simple merge sort) */
static void msort(size_t *a, size_t n, size_t *tmp, const model *m) {
    if (n < 2) return;
    size_t h = n / 2; msort(a, h, tmp, m); msort(a + h, n - h, tmp, m);
    size_t i = 0, j = h, k = 0;
    while (i < h && j < n) tmp[k++] = (cmp_menu(&a[i], &a[j], (void *)m) <= 0) ? a[i++] : a[j++];
    while (i < h) tmp[k++] = a[i++];
    while (j < n) tmp[k++] = a[j++];
    memcpy(a, tmp, n * sizeof *a);
}

static int id_seen(const model *m, size_t upto, const char *id) { for (size_t i = 0; i < upto; i++) if (!strcmp(m->entries[i].id, id)) return 1; return 0; }

int model_load(model *m, const char *path, size_t budget, char *err, size_t errlen) {
    memset(m, 0, sizeof *m); if (err && errlen) err[0] = 0;
    FILE *f = fopen(path, "rb"); if (!f) { snprintf(err, errlen, "cannot open fixture: %s", path); return 2; }
    fseek(f, 0, SEEK_END); long sz = ftell(f); fseek(f, 0, SEEK_SET);
    if (sz < 0) { fclose(f); snprintf(err, errlen, "cannot size fixture"); return 2; }
    if (budget && (size_t)sz * 3 > budget) { fclose(f); snprintf(err, errlen, "memory budget exceeded: fixture needs about %ld bytes, budget %zu", sz * 3, budget); return 3; }
    char *text = malloc((size_t)sz + 1); if (!text) { fclose(f); snprintf(err, errlen, "out of memory reading fixture"); return 3; }
    size_t got = fread(text, 1, (size_t)sz, f); fclose(f); text[got] = 0; m->bytes += got;
    char jerr[160]; jval *root = json_parse(text, got, jerr, sizeof jerr); free(text);
    if (!root) { snprintf(err, errlen, "fixture is not valid JSON: %s", jerr); return 4; }
    const char *schema = str_field(root, "schema");
    if (!schema || strcmp(schema, CCS_FIXTURE_SCHEMA)) { snprintf(err, errlen, "fixture schema %s is not %s", schema ? schema : "(missing)", CCS_FIXTURE_SCHEMA); json_free(root); return 5; }
    const jval *entries = json_get(root, "entries");
    if (!entries || entries->t != J_ARR) { snprintf(err, errlen, "fixture has no entries array"); json_free(root); return 5; }
    const char *nm = str_field(root, "name"); m->name = dup_charged(m, nm ? nm : "(unnamed)");
    const char *src = str_field(root, "source"); m->source = dup_charged(m, src ? src : "synthetic");
    const char *sh = str_field(root, "source_hash"); m->source_hash = dup_charged(m, sh ? sh : "");
    m->entries = calloc(entries->n ? entries->n : 1, sizeof *m->entries); m->bytes += entries->n * sizeof *m->entries;
    if (!m->entries) { snprintf(err, errlen, "out of memory for entries"); json_free(root); return 3; }
    size_t rcap = 0; char rec[96];
    for (size_t i = 0; i < entries->n; i++) {
        const jval *o = entries->items[i]; snprintf(rec, sizeof rec, "entries[%zu]", i);
        if (o->t != J_OBJ) { reject(m, &rcap, rec, "record is not an object"); continue; }
        const char *id = str_field(o, "id"), *title = str_field(o, "title"), *ver = str_field(o, "version_id"), *kind = str_field(o, "kind");
        if (!id || !*id) { reject(m, &rcap, rec, "missing or empty id"); continue; }
        snprintf(rec, sizeof rec, "entries[%zu] id=%.40s", i, id);
        if (!title || !*title) { reject(m, &rcap, rec, "missing or empty title"); continue; }
        if (!ver || !*ver) { reject(m, &rcap, rec, "missing or empty version_id (exact version identity is required)"); continue; }
        entry e; memset(&e, 0, sizeof e);
        if (!kind || !kind_parse(kind, &e.kind)) { reject(m, &rcap, rec, "kind is not one of factory-visible, factory-hidden, added, personal, alternate"); continue; }
        if (id_seen(m, m->n, id)) { reject(m, &rcap, rec, "duplicate id (first occurrence kept)"); continue; }
        if (json_get(o, "launch_command") || json_get(o, "command") || json_get(o, "exec") || json_get(o, "path")) { reject(m, &rcap, rec, "fixture records may not carry commands or paths (security guardrail)"); continue; }
        const jval *vis = json_get(o, "visible"); e.visible = (vis && vis->t == J_BOOL) ? vis->b : 1;
        if (vis && vis->t != J_BOOL) { reject(m, &rcap, rec, "visible is not a boolean"); continue; }
        const jval *ord = json_get(o, "order"); e.order = (ord && ord->t == J_NUM) ? (int)ord->num : (int)i;
        if (ord && ord->t != J_NUM) { reject(m, &rcap, rec, "order is not a number"); continue; }
        const char *rd = str_field(o, "ready"); e.ready = R_UNKNOWN;
        if (rd && !ready_parse(rd, &e.ready)) { reject(m, &rcap, rec, "ready is not one of ready, missing, unknown, n/a"); continue; }
        if (e.kind == K_FACTORY_HIDDEN) e.visible = 0;
        e.id = dup_charged(m, id); e.title = dup_charged(m, title); e.version_id = dup_charged(m, ver);
        const char *sk = str_field(o, "sort_key"); e.sort_key = sk ? dup_charged(m, sk) : make_sort_key(m, title);
        const char *g = str_field(o, "group"); e.group = dup_charged(m, g ? g : "");
        const char *art = str_field(o, "art_ref"); e.art_ref = dup_charged(m, art ? art : "");
        const char *hsh = str_field(o, "source_hash"); e.source_hash = dup_charged(m, hsh ? hsh : "");
        if (!e.id || !e.title || !e.version_id || !e.sort_key || !e.group || !e.art_ref || !e.source_hash) { snprintf(err, errlen, "out of memory at record %zu", i); json_free(root); return 3; }
        if (budget && m->bytes > budget) { snprintf(err, errlen, "memory budget exceeded after %zu records (%zu bytes used, budget %zu)", m->n, m->bytes, budget); json_free(root); return 3; }
        m->entries[m->n++] = e;
    }
    json_free(root);
    m->menu = malloc((m->n ? m->n : 1) * sizeof *m->menu); size_t *tmp = malloc((m->n ? m->n : 1) * sizeof *tmp);
    if (!m->menu || !tmp) { free(tmp); snprintf(err, errlen, "out of memory for menu"); return 3; }
    for (size_t i = 0; i < m->n; i++) if (m->entries[i].visible) m->menu[m->menu_n++] = i;
    msort(m->menu, m->menu_n, tmp, m); free(tmp); m->bytes += 2 * m->n * sizeof(size_t);
    return 0;
}

void model_free(model *m) {
    for (size_t i = 0; i < m->n; i++) { entry *e = &m->entries[i]; free(e->id); free(e->title); free(e->version_id); free(e->sort_key); free(e->group); free(e->art_ref); free(e->source_hash); }
    for (size_t i = 0; i < m->rejected_n; i++) { free(m->rejected[i].record); free(m->rejected[i].reason); }
    free(m->entries); free(m->menu); free(m->rejected); free(m->name); free(m->source); free(m->source_hash); memset(m, 0, sizeof *m);
}
