r"""Explained words on the built website (the Windows app shows the same page), headless Edge, real pointer and key events through the
browser's input layer.
  words     Help's Install on NES Classic chapter and the Website App comparison show the shared glossary's words (studio/glossary.json, the
            same file CCS Console Workbench carries) as dotted-underlined words in their own sentences; legal text, the Installation Terms,
            Credits and About the Developer have none; Classic Cart Studio itself is never explained on its own site
  click     a click opens the word's explanation just under it, inside the window, with focus on its Close; Escape closes it, the focus is
            back on the word and Help stays open; a click elsewhere closes it; a second word replaces the first (one open at a time)
  keys      Tab to a word, then Enter or Space, opens it; Close closes it
  state     the chapter's scroll position and the open chapter are kept; nothing opens when the pointer only passes over a word
  hover     resting the mouse on a word opens it after 600 ms without moving the focus; a quick pass opens nothing; the pointer can cross into
            the explanation; leaving both closes it after 200 ms; a click keeps a hover-opened one open as if chosen; Escape leaves the focus;
            another word waits its own full delay; a touch tap opens at once; closing Help drops a waiting hover
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
            mouse(ed, p["x"], p["y"], "move"); ok("state: the pointer resting on a word for less than the hover delay opens nothing", ed.js(POP) is None)
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
            # freeze 16: delayed hover (GL_HOVER_OPEN_MS 600, GL_HOVER_LEAVE_MS 200), no focus taken, a click keeps it open, touch stays a tap
            ed.call("Emulation.setFocusEmulationEnabled", {"enabled": True}); ed.js("openGuide('backup')"); time.sleep(1.0)
            ok("hover: the two delays are named, 600 ms and 200 ms (the same as the Workbench window)", ed.js("[GL_HOVER_OPEN_MS,GL_HOVER_LEAVE_MS]") == [600, 200])
            def mv(x, y): ed.call("Input.dispatchMouseEvent", {"type": "mouseMoved", "x": x, "y": y})
            def wait_pop(sec):
                t0 = time.time()
                while time.time() - t0 < sec:
                    if ed.js(POP) is not None: return time.time() - t0
                    time.sleep(0.03)
                return None
            hb = at(ed, "document.querySelector('#guideArt section[data-ch=\"backup\"] .gterm[data-g=\"storage\"]')")
            hd = at(ed, "document.querySelector('#guideArt section[data-ch=\"backup\"] h4')")
            away = (hd["x"] + 60, hd["y"])
            ed.js("document.querySelector('#guideArt section[data-ch=\"backup\"] .gterm[data-g=\"mod\"]').focus()"); time.sleep(0.1)
            hb = at(ed, "document.querySelector('#guideArt section[data-ch=\"backup\"] .gterm[data-g=\"storage\"]')")
            focus0 = ed.js("document.activeElement.dataset.g")
            mv(*away); time.sleep(0.2); mv(hb["x"], hb["y"]); time.sleep(0.3); mv(*away); time.sleep(0.8)
            ok("hover: passing over a word (leaving before 600 ms) never opens it, not later either", ed.js(POP) is None)
            t0 = time.time(); mv(hb["x"], hb["y"]); time.sleep(0.25); early = ed.js(POP); got = wait_pop(2.0); el = time.time() - t0
            pop = ed.js(POP)
            ok("hover: resting on a word opens its explanation after the delay, not before (bounded: 0.55 s to 2 s)", early is None and got is not None and 0.55 <= el < 2.0 and pop and pop["title"] == "Built-in storage", (early, el, pop))
            ok("hover: the keyboard focus stays where it was; the word says it is expanded", ed.js("document.activeElement.dataset.g") == focus0 == "mod" and ed.js("document.querySelector('.gterm[data-g=\"storage\"]').getAttribute('aria-expanded')") == "true", (focus0, ed.js("document.activeElement.id||document.activeElement.dataset.g")))
            ok("hover: the explanation shows the meaning and the detail named for these consoles", ed.js("document.querySelector('#gpop .gdetail')?.textContent") == next(t for t in GLOSSARY["terms"] if t["key"] == "storage")["detail"])
            pr = ed.js("(()=>{const r=document.getElementById('gpop').getBoundingClientRect(); return {x:Math.round(r.left+r.width/2), y:Math.round(r.top+12)}})()")
            mv(hb["x"], hb["y"] + 8); mv(pr["x"], pr["y"]); time.sleep(0.5)
            ok("hover: the pointer can move from the word into its explanation, across the gap, and it stays open", ed.js(POP) is not None)
            mv(*away); time.sleep(0.08); still = ed.js(POP) is not None; time.sleep(0.5)
            ok("hover: leaving both closes it after the grace, not at once, and the focus stays where it was", still and ed.js(POP) is None and ed.js("document.activeElement.dataset.g") == "mod")
            mv(hb["x"], hb["y"]); wait_pop(2.0); mouse(ed, hb["x"], hb["y"]); popc = ed.js(POP)
            ok("hover then click: the same explanation stays open, now as if chosen (focus on its Close)", popc and popc["title"] == "Built-in storage" and popc["focus"] == "gpopClose", popc)
            mv(*away); time.sleep(0.6)
            ok("an explanation chosen on purpose does not close when the pointer leaves", ed.js(POP) is not None)
            key(ed, "Escape", "Escape", 27)
            ok("Escape closes the chosen explanation and puts the focus back on its word", ed.js(POP) is None and ed.js("document.activeElement.dataset.g") == "storage")
            ed.js("document.querySelector('#guideArt section[data-ch=\"backup\"] .gterm[data-g=\"mod\"]').focus()"); time.sleep(0.1)
            hb = at(ed, "document.querySelector('#guideArt section[data-ch=\"backup\"] .gterm[data-g=\"storage\"]')")   # focusing can scroll Help: measure again
            mv(hb["x"], hb["y"]); wait_pop(2.0); key(ed, "Escape", "Escape", 27)
            ok("Escape closes a hover-opened explanation and leaves the focus where it was", ed.js(POP) is None and ed.js("document.activeElement.dataset.g") == "mod")
            mv(*away); time.sleep(0.3)
            # two words of one list item, both measured after one scroll (the item with CCS Mod for Hakchi and CCS Nucleus)
            LI = "[...document.querySelectorAll('#guideArt section[data-ch=\"backup\"] li')].find(li=>li.querySelector('.gterm[data-g=\"mod\"]')&&li.querySelector('.gterm[data-g=\"nucleus\"]'))"
            hm = at(ed, LI + ".querySelector('.gterm[data-g=\"mod\"]')")
            hn = ed.js("(()=>{ const r=" + LI + ".querySelector('.gterm[data-g=\"nucleus\"]').getBoundingClientRect(); return {x:Math.round(r.left+Math.min(8,r.width/2)), y:Math.round(r.top+r.height/2)}; })()")
            mv(*away); time.sleep(0.3); mv(hm["x"], hm["y"]); time.sleep(0.4); mv(hn["x"], hn["y"]); time.sleep(0.4); waits = ed.js(POP) is None; got = wait_pop(1.5)
            ok("hover: another word starts its own full wait, then opens", waits and got is not None and ed.js(POP)["title"] == "CCS Nucleus", (waits, got, ed.js(POP), hm, hn))
            hb = at(ed, "document.querySelector('#guideArt section[data-ch=\"backup\"] .gterm[data-g=\"storage\"]')")
            mv(*away); time.sleep(0.5)
            ed.call("Input.dispatchTouchEvent", {"type": "touchStart", "touchPoints": [{"x": hb["x"], "y": hb["y"]}]}); ed.call("Input.dispatchTouchEvent", {"type": "touchEnd", "touchPoints": []}); time.sleep(0.35)
            popt = ed.js(POP)
            ok("touch: a tap opens the explanation at once (no hover needed, no double opening)", popt and popt["title"] == "Built-in storage" and ed.js("document.querySelectorAll('#gpop').length") == 1, popt)
            key(ed, "Escape", "Escape", 27)
            mv(hb["x"], hb["y"]); time.sleep(0.25); ed.js("closeGuide()"); time.sleep(0.9)
            ok("hover: closing Help while a word waits shows no explanation later", ed.js(POP) is None)
            ed.js("openGuide('backup')"); time.sleep(0.6); mv(*away)
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
