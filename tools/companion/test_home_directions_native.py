r"""Controller directions on the native HOME (the console's HOME; Preview on TV runs the same program), all four themes, the 30 built-in
games in a preview package with five folders. Every direction is pressed as the console's controller sends it (`input press` /
`input release`, the logical button the console's input layer decodes) and judged against what the frame DRAWS: the focus rectangles
the frame reports (home-layout) before and after the press.
  rule      after a direction the highlight is on the item drawn in that direction from where it was (its centre moved that way,
            mostly along that axis), or nothing moved (an edge); a row that scrolls under a fixed highlight (the Box Art carousel, a
            shelf at its end) moves the selection one step that way; the Arcade Grid's page turn keeps the column
  screens   the games (grid, carousel, shelf, map), the tabs/sidebar/signs, Game Details (buttons in a row, then the description),
            the START menu (a list), the theme picker (one row: Up and Down do nothing), Browse (folders, then the categories on
            their own rows)
  python test_home_directions_native.py [--core ccs-sim-core.exe] [--out results.json]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, json, os, sys, tempfile
HERE = os.path.dirname(os.path.abspath(__file__)); TOOLS = os.path.dirname(HERE)
for p in (HERE, os.path.join(TOOLS, "rom-pipeline")):
    if p not in sys.path: sys.path.insert(0, p)
import library_lookup as LL   # noqa: E402
from common import site_path   # noqa: E402
from native_home import package as P   # noqa: E402
import native_preview as NP   # noqa: E402
from test_artwork_consistency import Core, collection, THEMES, _jl   # noqa: E402

DBUTTON = {"play": "button-play", "fav": "button-fav", "game_settings": "button-gset", "desc": "desc"}
VEC = {"right": (1, 0), "left": (-1, 0), "down": (0, 1), "up": (0, -1)}

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--core", default=""); ap.add_argument("--out", default=""); a = ap.parse_args()
    exe = a.core or NP.core_binary(); res, rows = [], []
    def ok(n, c, d=""):
        res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:400]})" if d and not c else ""), flush=True)
    codes = [b["code"] for b in _jl(site_path("builtin.json"))["games"]]; ids = [LL.factory_row(c)["game_id"] for c in codes]
    root = tempfile.mkdtemp(prefix="ccs-dirs-")
    man, home = collection(ids, "arcade-grid")
    home["folders"] = [{"id": f"f{k}", "name": f"Folder {k + 1}", "parent": None, "pos": k, "order": ids[k * 3:k * 3 + 3]} for k in range(5)]
    r = P.build(man, home, mode="preview", root=root); ok("setup: a preview package with the 30 built-in games and five folders", r.get("ok"), r)
    if not r.get("ok"): return 1
    c = Core(exe)
    def state():
        c.send("home-state"); return c.wait("home")
    def layout():
        c.send("home-layout"); out = []
        while True:
            e = c.wait(("layout_rect", "layout_end"))
            if e["ev"] == "layout_end": return out
            out.append((e["name"], e["box"], e.get("idx", -1)))
    def focus(st, lay):
        names = [n for n, _, _ in lay]
        if st["overlay"] in ("start", "theme", "settings", "mapdest"):
            want = ("menu-focus", "theme-focus")
        elif st.get("footer", -1) >= 0: want = ("legend-focus",)
        elif st["screen"] == "details": want = (DBUTTON.get(st["focus"], "button-play"),)
        else: want = {"nav": ("tab-focus", "nav-focus", "side-focus"), "map": ("sign-focus",), "browse": ("browse-tile-focus",)}.get(st["zone"], ("focus-ring", "box-focus", "shelf-focus"))
        for w in want:
            if w in names:
                return w, next(bx for n, bx, _ in lay if n == w)
        return None, None
    def press(btn):
        c.send(f"input press {btn}"); c.send(f"input release {btn}"); c.send("tick 16"); c.wait("tick")
    def step(theme, screen, d):
        s0 = state(); l0 = layout(); n0, b0 = focus(s0, l0)
        press(d); s1 = state(); l1 = layout(); n1, b1 = focus(s1, l1)
        key = lambda s: (s["screen"], s["zone"], s["index"], s["nav"], s["landmark"], s["overlay"], s["oindex"], s["focus"], s["scroll"], s.get("browse"), s.get("footer"), s.get("page"))
        moved = key(s0) != key(s1); verdict, why = True, ""
        cen = lambda b: ((b[0] + b[2]) / 2, (b[1] + b[3]) / 2)
        # an item that names itself in the layout (a Browse tile, a shelf or carousel cover): where the destination is drawn relative to
        # where the highlight was, in ONE frame (before the press, or after it when it was not on screen yet), so a scroll cannot mislead
        grp = None
        if s0["zone"] == s1["zone"] == "browse" and s0["screen"] == s1["screen"] == "grid" and s0["overlay"] == s1["overlay"] == "none": grp, i0, i1 = ("browse-tile", "browse-tile-focus"), s0.get("browse"), s1.get("browse")
        elif s0["zone"] == s1["zone"] == "games" and s0["screen"] == s1["screen"] == "grid" and s0["overlay"] == s1["overlay"] == "none" and theme != "arcade-grid": grp, i0, i1 = ("shelf-art", "box", "box-focus"), s0["index"], s1["index"]
        pair = None
        if moved and grp and i0 != i1:
            for lay in (l0, l1):
                at = {ix: bx for n, bx, ix in lay if n in grp and ix >= 0}
                if i0 in at and i1 in at: pair = (at[i0], at[i1], [bx for n, bx, ix in lay if n in grp and ix >= 0]); break
        if pair:
            (x0, y0), (x1, y1) = cen(pair[0]), cen(pair[1]); dx, dy = x1 - x0, y1 - y0; vx, vy = VEC[d]; along = dx * vx + dy * vy
            if d in ("left", "right"): verdict = along > 0 and abs(dy) < along; why = f"item drawn at ({dx:+.0f},{dy:+.0f})"
            else:   # the row drawn above/below, and in it the item nearest the column the highlight was in
                ahead = [cen(bx)[1] for bx in pair[2] if (cen(bx)[1] - y0) * vy > 4]; nexty = min(ahead, key=lambda y: abs(y - y0)) if ahead else y1
                rowx = [cen(bx)[0] for bx in pair[2] if abs(cen(bx)[1] - y1) < 8]; near = min(abs(x - x0) for x in rowx) if rowx else 0
                verdict = along > 0 and abs(y1 - nexty) < 8 and abs(x1 - x0) <= near + 1; why = f"item drawn at ({dx:+.0f},{dy:+.0f}), nearest in its row {near:.0f}" + ("" if abs(y1 - nexty) < 8 else ", skipped the row drawn next")
        elif moved and b0 and b1:
            cx0, cy0 = cen(b0); cx1, cy1 = cen(b1)
            dx, dy = cx1 - cx0, cy1 - cy0; vx, vy = VEC[d]; along = dx * vx + dy * vy; across = abs(dx * vy) + abs(dy * vx)
            if abs(dx) < 2 and abs(dy) < 2:   # the highlight stayed put: the description scrolled under it
                sgn = 1 if d in ("right", "down") else -1
                if s1["scroll"] != s0["scroll"]: verdict = (s1["scroll"] - s0["scroll"]) * sgn > 0; why = "description scrolled"
                else: verdict = False; why = "the selection changed but nothing drawn moved"
            elif theme == "arcade-grid" and s0.get("page") is not None and s1.get("page") is not None and s1["page"] != s0["page"] and d in ("up", "down"):
                verdict = abs(dx) < 4 and (s1["page"] - s0["page"]) * (1 if d == "down" else -1) > 0; why = "page turn keeps the column"
            else:
                # within a row/list/grid: mostly along the pressed axis; into another area (the tabs above, the hints below, the
                # description under the buttons): that area lies in the pressed direction (it opens on its current entry)
                other = s1["zone"] != s0["zone"] or s1.get("footer") != s0.get("footer") or (s0["screen"] == "details" and "desc" in (s0["focus"], s1["focus"]))
                verdict = along > 0 and (other or across <= along * 1.2 + 4); why = f"moved ({dx:+.0f},{dy:+.0f})" + (" into another area" if other else "")
        elif moved and not (b0 and b1):
            verdict = True; why = f"focus {n0}->{n1} (not drawn as one rectangle)"
        rows.append({"runtime": "native", "theme": theme, "screen": screen, "raw": f"input press {d}", "logical": d, "from": [n0, b0, {k: s0.get(k) for k in ("zone", "index", "focus", "oindex", "browse", "page")}],
                     "to": [n1, b1, {k: s1.get(k) for k in ("zone", "index", "focus", "oindex", "browse", "page")}], "pass": verdict, "why": why})
        if not verdict: print(f"   {theme} {screen} {d}: {n0}{b0} -> {n1}{b1} {why}", flush=True)
        return verdict
    try:
        c.send(f"home {r['package_dir']}"); c.wait("home_loaded", 300); c.wait("home", 30)
        for t in THEMES:
            c.send(f"home-theme {t}"); c.wait("home")
            # the games: from the 6th game, a walk in every direction (into the tabs/sidebar at the top and back)
            c.send("home-select 5"); c.wait("home"); bad = []
            for d in ["right", "right", "down", "left", "left", "down", "right", "up", "up", "up", "up", "left", "right", "right", "down", "down", "down", "down", "down", "up"]:
                if not step(t, "games", d): bad.append(d)
            ok(f"{t}: the games and the tabs/sidebar move where the press points", not bad, bad)
            # Game Details: the buttons in a row, the description below
            c.send("home-select 2"); c.wait("home"); press("select"); s = state(); ok(f"{t}: SELECT opens Game Details", s["screen"] == "details", s["screen"]); bad = []
            for d in ["right", "right", "left", "down", "up", "up", "down", "right", "left", "left"]:
                if not step(t, "details", d): bad.append(d)
            ok(f"{t}: Game Details: Left/Right along the buttons, Down to the description and Up back", not bad, bad)
            s = state(); press("b"); state()
            # the START menu (a list) and the theme picker (one row)
            press("start"); bad = []
            for d in ["down", "down", "up", "down"]:
                if not step(t, "start menu", d): bad.append(d)
            s = state(); ok(f"{t}: the START menu moves Up/Down", not bad and s["overlay"] == "start" and s["oindex"] == 2, (bad, s["oindex"]))
            press("a"); s = state(); before = s["oindex"]; bad = []
            for d in ["up", "down"]:
                step(t, "theme picker", d)
            s2 = state(); ok(f"{t}: the theme picker (one row) ignores Up and Down", s2["overlay"] == "theme" and s2["oindex"] == before, (before, s2["oindex"]))
            for d in ["left", "right", "right", "left"]:
                if not step(t, "theme picker", d): bad.append(d)
            ok(f"{t}: the theme picker moves Left/Right to the card drawn beside it", not bad, bad)
            press("b"); state()
            # Browse: five folders (a row of four, then one), the categories from a new row
            press("start"); s = state()
            for _ in range(8): press("down")
            press("a"); s = state(); ok(f"{t}: Browse opened from the START menu", s.get("section") == "browse" and s["zone"] == "browse", {k: s.get(k) for k in ("section", "zone", "tiles")})
            bad = []
            for d in ["right", "right", "down", "down", "right", "up", "up", "left", "down", "down", "down", "up", "up", "up", "up"]:
                if not step(t, "browse", d): bad.append(d)
            ok(f"{t}: Browse moves along the rows as drawn (folders, then the categories' own rows)", not bad, bad)
            press("start"); state()
            for _ in range(7): press("down")
            press("a"); state()   # All Games again for the next theme
    finally:
        c.close()
    fails = [n for n, c_ in res if not c_]
    print(f"\n{len(res) - len(fails)}/{len(res)} PASS" + (f"; FAIL: {', '.join(fails)}" if fails else ""))
    if a.out: json.dump({"results": [{"name": n, "pass": c_} for n, c_ in res], "steps": rows}, open(a.out, "w", encoding="utf-8"), indent=1)
    return 0 if not fails else 1

if __name__ == "__main__": sys.exit(main())
