"""wb private (freeze 12): the four starting-theme example pictures, drawn by the native HOME renderer (ccs-sim-core.exe, the candidate's own)
from the pack build_theme_pack.py made (the console's 30 built-in games, most popular first, two folders, four favorites). Each theme is
drawn at the computer's full resolution (--scale=2: 2560 x 1440) with every slot filled.
python render_examples.py <ccs-sim-core.exe> <pack/current> <out folder>"""
import json, os, subprocess, sys, time
from PIL import Image
sim, pack, out = sys.argv[1], os.path.abspath(sys.argv[2]), os.path.abspath(sys.argv[3])
os.makedirs(out, exist_ok=True)
# the game in focus: the first (Super Mario Bros. 3) except Box Art Showcase, whose shelf shows the games before the focused one on its
# left: there the third (Super Mario Bros.), so both sides of the shelf are filled
FOCUS = {"arcade-grid": 0, "box-art-showcase": 2, "featured-shelf": 0, "adventure-map": 0}
state = os.path.join(out, "state"); os.makedirs(state, exist_ok=True)
p = subprocess.Popen([sim, "--scale=2", "--state-dir=" + state], stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, encoding="utf-8")
def send(line):
    p.stdin.write(line + "\n"); p.stdin.flush()
def until(pred, limit=60):
    end = time.time() + limit
    while time.time() < end:
        l = p.stdout.readline()
        if not l: raise SystemExit("renderer ended")
        try: ev = json.loads(l)
        except ValueError: continue
        if ev.get("ev") in ("error", "home_rejected", "frame_failed"): raise SystemExit(l)
        if pred(ev): return ev
    raise SystemExit("timed out")
until(lambda e: e.get("ev") == "state" and e.get("state") == "ready")
send("home " + pack); until(lambda e: e.get("ev") == "home_loaded")
clock = 1000
for theme, idx in FOCUS.items():
    send("home-theme " + theme); until(lambda e: e.get("ev") == "home_state" or e.get("ev") == "state" or "theme" in e, 20)
    send("home-select %d" % idx)
    ppm = os.path.join(out, theme + ".ppm")
    for _ in range(80):   # let the covers load (covers_pending reaches 0), then the frame that is kept
        clock += 50; send("tick %d" % clock); until(lambda e: e.get("ev") == "tick")
        send("frame " + ppm); ev = until(lambda e: e.get("ev") == "frame_written")
        if ev.get("covers_pending", 0) == 0: break
    for _ in range(40):   # animations settle (the focus glow): a few more ticks
        clock += 50; send("tick %d" % clock); until(lambda e: e.get("ev") == "tick")
    send("frame " + ppm); ev = until(lambda e: e.get("ev") == "frame_written")
    im = Image.open(ppm).convert("RGB"); im.save(os.path.join(out, theme + ".png")); os.remove(ppm)
    print(theme, idx, im.size, "covers pending", ev.get("covers_pending"))
send("quit"); p.wait(20)
