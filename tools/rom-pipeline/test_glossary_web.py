r"""Explained words on the built website (the Windows app shows the same page), headless Edge, real pointer and key events through the
browser's input layer.
  words     Help's Install on NES Classic chapter and the Website App comparison show the shared glossary's words (studio/glossary.json, the
            same file CCS Console Workbench carries) as dotted-underlined words in their own sentences; legal text, the Installation Terms,
            Credits and About the Developer have none; Classic Cart Studio itself is never explained on its own site
  click     a click opens the word's explanation just under it, inside the window, with focus on its Close; Escape closes it, the focus is
            back on the word and Help stays open; a click elsewhere closes it; a second word replaces the first (one open at a time)
  keys      Tab to a word, then Enter or Space, opens it; Close closes it
  state     the chapter's scroll position and the open chapter are kept; nothing opens when the pointer only passes over a word
  phone     at 390 px wide the explanation stays inside the screen
  python test_glossary_web.py <built site folder> [--page preview/index.html] [--out results.json]      Exit 0 = PASS (--page: the public
  build's app page; the Windows build's is index.html)."""
from __future__ import annotations
import argparse, json, os, sys, tempfile, time
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from studio_harness import EdgeSession, READY, CALM, serve

GLOSSARY = json.load(open(os.path.join(HERE, "studio", "glossary.json"), encoding="utf-8"))
POP = "(()=>{ const p=document.getElementById('gpop'); if(!p||p.hidden) return null; const r=p.getBoundingClientRect(); const a=document.activeElement; return {title:p.querySelector('#gpopT').textContent, text:p.querySelector('p').textContent, l:r.left, t:r.top, r:r.right, b:r.bottom, vw:document.documentElement.clientWidth, vh:document.documentElement.clientHeight, focus:a&&a.id, inDialog:!!p.closest('[role=dialog]:not(#gpop)')}; })()"
TERMS = "(sel=>[...document.querySelectorAll(sel+' .gterm')].map(b=>({g:b.dataset.g, t:b.textContent, vis:b.getBoundingClientRect().width>0, deco:getComputedStyle(b).textDecorationLine, style:getComputedStyle(b).textDecorationStyle, exp:b.getAttribute('aria-expanded')})))"

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--page", default="index.html"); ap.add_argument("--out", default=""); a = ap.parse_args()
    srv = serve(a.site); url = f"http://127.0.0.1:{srv.server_address[1]}/{a.page}"; res = []
    def ok(n, c, d=""):
        res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:400]})" if d and not c else ""), flush=True)
    def mouse(ed, x, y, kind="click"):
        ed.call("Input.dispatchMouseEvent", {"type": "mouseMoved", "x": x, "y": y})
        if kind == "move": time.sleep(0.4); return
        ed.call("Input.dispatchMouseEvent", {"type": "mousePressed", "x": x, "y": y, "button": "left", "clickCount": 1})
        ed.call("Input.dispatchMouseEvent", {"type": "mouseReleased", "x": x, "y": y, "button": "left", "clickCount": 1}); time.sleep(0.35)
    def key(ed, k, code, vk, shift=False, text=None):
        mods = 8 if shift else 0; down = {"type": "keyDown" if text else "rawKeyDown", "key": k, "code": code, "windowsVirtualKeyCode": vk, "modifiers": mods}
        if text: down["text"] = text
        ed.call("Input.dispatchKeyEvent", down); ed.call("Input.dispatchKeyEvent", {"type": "keyUp", "key": k, "code": code, "windowsVirtualKeyCode": vk, "modifiers": mods}); time.sleep(0.35)
    def at(ed, js):
        return ed.js("(()=>{ const e=" + js + "; if(!e) return null; e.scrollIntoView({block:'center', behavior:'instant'}); const r=e.getBoundingClientRect(); return {x:Math.round(r.left+Math.min(8,r.width/2)), y:Math.round(r.top+r.height/2)}; })()") if ed.js("1") else None
    try:
        with EdgeSession(profile=tempfile.mkdtemp(prefix="ccs-gloss-"), width=1440, height=950) as ed:
            ed.navigate(url); ed.wait_for(READY, timeout=120); ed.js(CALM)
            ed.wait_for("typeof glApply==='function'", timeout=30); time.sleep(0.5)
            ed.js("openGuide('install')"); time.sleep(1.2)
            inst = {w["g"] for w in ed.js(TERMS + "(" + json.dumps("#guideArt section[data-ch=\"install\"]") + ")") or []}
            ok("words: Install on NES Classic explains Hakchi2 CE where it appears", "hakchi" in inst, inst)
            ed.js("openGuide('backup')"); time.sleep(1.2)
            words = ed.js(TERMS + "(" + json.dumps("#guideArt section[data-ch=\"backup\"]") + ")")
            keys = {w["g"] for w in words or []}
            ok("words: the console backup chapter explains CCS HOME, CCS Mod, CCS Nucleus, startup software and built-in storage where they appear", {"home", "mod", "nucleus", "startup", "storage"} <= keys, keys)
            ok("words: every explained word is visible with an always-visible dotted underline", words and all(w["vis"] and "underline" in w["deco"] and w["style"] == "dotted" for w in words), words[:4] if words else words)
            ok("words: each word is the glossary's own form, in its sentence", words and all(any(w["t"] in (f, f + "s") for t in GLOSSARY["terms"] if t["key"] == w["g"] for f in t["forms"]) for w in words), words[:6] if words else words)
            legal = ed.js("['legal','terms','credits','about'].map(k=>document.querySelectorAll('#guideArt section[data-ch=\"'+k+'\"] .gterm').length)")
            ok("words: legal text, the Installation Terms, Credits and About the Developer have no explained words", legal == [0, 0, 0, 0], legal)
            ok("words: Classic Cart Studio is never explained on its own site", ed.js("document.querySelectorAll('.gterm[data-g=\"ccs\"]').length") == 0)
            ok("words: no explained word sits inside a link, a button or a heading", ed.js("[...document.querySelectorAll('.gterm')].every(b=>!b.parentElement.closest('a,button,h1,h2,h3,h4,h5,h6,label'))") is True)
            # click: under the word, inside the window, focus on Close; Escape closes it and returns the focus; Help stays open
            y0 = ed.js("document.getElementById('guideArt').scrollTop")
            p = at(ed, "document.querySelector('#guideArt section[data-ch=\"backup\"] .gterm[data-g=\"nucleus\"]')")
            y0 = ed.js("document.getElementById('guideArt').scrollTop")
            mouse(ed, p["x"], p["y"], "move"); ok("state: the pointer passing over a word opens nothing", ed.js(POP) is None)
            mouse(ed, p["x"], p["y"]); pop = ed.js(POP); t = next(t for t in GLOSSARY["terms"] if t["key"] == "nucleus")
            ok("click: the word's explanation opens with the glossary's text", pop and pop["title"] == t["word"] and pop["text"] == t["text"], pop)
            ok("click: it is just under the word and inside the window", pop and pop["t"] >= p["y"] - 2 and pop["l"] >= 8 and pop["r"] <= pop["vw"] - 8 and pop["b"] <= pop["vh"] - 4, pop)
            ok("click: focus is on its Close, inside the same dialog as the word", pop and pop["focus"] == "gpopClose" and pop["inDialog"], pop)
            ok("click: the word says it is expanded", ed.js("document.querySelector('.gterm[aria-expanded=\"true\"]')?.dataset.g") == "nucleus")
            key(ed, "Escape", "Escape", 27)
            ok("click: Escape closes it and puts the focus back on the word", ed.js(POP) is None and ed.js("document.activeElement.classList.contains('gterm')&&document.activeElement.dataset.g") == "nucleus")
            ok("state: Help is still open on the same chapter, at the same place", ed.js("document.getElementById('guide').classList.contains('open')&&document.querySelector('#guideArt section.on').dataset.ch") == "backup" and abs(ed.js("document.getElementById('guideArt').scrollTop") - y0) < 4)
            # one at a time, and a click elsewhere closes it
            mouse(ed, p["x"], p["y"]); q = at(ed, "document.querySelector('#guideArt section[data-ch=\"backup\"] .gterm:not([data-g=\"nucleus\"])')")
            mouse(ed, q["x"], q["y"]); n = ed.js("document.querySelectorAll('#gpop').length"); pop2 = ed.js(POP)
            ok("click: a second word replaces the first (one explanation open)", n == 1 and pop2 and pop2["title"] != t["word"], (n, pop2))
            h = at(ed, "document.querySelector('#guideArt section[data-ch=\"backup\"] h3')"); mouse(ed, h["x"] + 40, h["y"])   # elsewhere in Help (not outside it, which closes Help)
            ok("click: a click elsewhere closes it, and Help stays open", ed.js(POP) is None and ed.js("document.getElementById('guide').classList.contains('open')") is True)
            # keys: Tab to a word, then Enter; Space; Close
            ed.js("document.querySelector('#guideArt section[data-ch=\"backup\"] .gterm[data-g=\"mod\"]').focus()"); time.sleep(0.2)
            key(ed, "Enter", "Enter", 13, text="\r"); popk = ed.js(POP)
            ok("keys: Enter on a focused word opens its explanation", popk and popk["title"] == next(t for t in GLOSSARY["terms"] if t["key"] == "mod")["word"], popk)
            key(ed, "Enter", "Enter", 13, text="\r")
            ok("keys: Close closes it and the focus is back on the word", ed.js(POP) is None and ed.js("document.activeElement.dataset.g") == "mod")
            key(ed, " ", "Space", 32, text=" ")
            ok("keys: Space on a focused word opens it too", ed.js(POP) is not None); key(ed, "Escape", "Escape", 27)
            ed.js("document.querySelector('#guideArt section[data-ch=\"install\"] .gterm').blur()")
            tab = ed.js("(()=>{ const b=document.querySelector('#guideArt section[data-ch=\"install\"] .gterm'); return b.tabIndex>=0&&getComputedStyle(b).display; })()")
            ok("keys: a word is reached with Tab like any other button", tab == "inline", tab)
            # the Website App comparison (a Studio dialog): Hakchi2 CE in its own row
            ed.js("closeGuide()"); time.sleep(0.5); ed.js("pwaDialog()"); time.sleep(0.8)   # the Studio menu's Install the Website App…
            comp = ed.js(TERMS + "(" + json.dumps("#collBody") + ")")
            ok("words: the Website App comparison explains Hakchi2 CE in its own row", comp is not None and any(w["g"] == "hakchi" for w in comp or []), comp)
        with EdgeSession(profile=tempfile.mkdtemp(prefix="ccs-gloss-phone-"), width=390, height=800) as ed:
            ed.navigate(url); ed.wait_for(READY, timeout=120); ed.js(CALM); ed.wait_for("typeof glApply==='function'", timeout=30)
            ed.js("openGuide('install')"); time.sleep(1.2)
            p = at(ed, "document.querySelector('#guideArt section[data-ch=\"install\"] .gterm')"); mouse(ed, p["x"], p["y"]); pop = ed.js(POP)
            ok("phone: at 390 px wide the explanation stays inside the screen", pop and pop["l"] >= 8 and pop["r"] <= pop["vw"] - 8 and pop["t"] >= 0 and pop["b"] <= pop["vh"], pop)
    finally:
        srv.shutdown()
    fails = [n for n, c in res if not c]
    print(f"{len(res) - len(fails)}/{len(res)} passed")
    if a.out: json.dump({"checks": [{"name": n, "ok": c} for n, c in res]}, open(a.out, "w", encoding="utf-8"), indent=1)
    return 0 if not fails else 1

if __name__ == "__main__":
    sys.exit(main())
