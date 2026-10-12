"""Interactive diagnostic harness (Windows, tkinter): shows the core's 1280 x 720 diagnostic frames scaled to fit, maps the
keyboard (and an XInput gamepad when one is connected) to the normalized NES-style buttons, logs every event, saves
screenshots and can replay a scripted input file headlessly. It is deliberately plain: a viewer for a test pattern, not a
proposal for the future HOME interface.

Keyboard mapping (simulator only; configurable in KEYMAP): arrows = D-pad, Z = B, X = A, Enter = Start, Shift = Select,
F2 = screenshot, F5 = reload fixture, F8 = simulate controller disconnect / reconnect, F9 = request a launch of the
cursor entry (the adapter runs the dummy game stub and the core resumes), Esc = quit. Window focus loss is
forwarded to the core as focus lost (all buttons released).

  python viewer.py [--path windows|linux|arm] [--fixture file.json] [--scale 0.5] [--script inputs.txt --out dir]
  script lines: press <b> | release <b> | tick <ms> | frame <name.png> | focus lost|gained | device disconnect|connect | launch | wait <ms>
"""
from __future__ import annotations
import argparse, ctypes, json, os, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from runner import Core, ARTIFACTS, SIM, ppm_to_png

KEYMAP = {"Up": "up", "Down": "down", "Left": "left", "Right": "right", "z": "b", "x": "a", "Return": "start", "Shift_L": "select", "Shift_R": "select"}
XINPUT_BUTTONS = {0x0001: "up", 0x0002: "down", 0x0004: "left", 0x0008: "right", 0x0010: "start", 0x0020: "select", 0x1000: "a", 0x2000: "b"}

class XInput:
    """optional gamepad through the XInput API already present in Windows (no extra library); polled, not event driven"""
    def __init__(self):
        self.dll = None
        for name in ("xinput1_4.dll", "xinput9_1_0.dll"):
            try: self.dll = ctypes.windll.LoadLibrary(name); break
            except OSError: continue
        self.last = 0; self.connected = False
    def poll(self) -> tuple[bool, int]:
        if not self.dll: return False, 0
        class GP(ctypes.Structure): _fields_ = [("buttons", ctypes.c_ushort), ("lt", ctypes.c_ubyte), ("rt", ctypes.c_ubyte), ("lx", ctypes.c_short), ("ly", ctypes.c_short), ("rx", ctypes.c_short), ("ry", ctypes.c_short)]
        class ST(ctypes.Structure): _fields_ = [("packet", ctypes.c_uint), ("gp", GP)]
        st = ST(); ok = self.dll.XInputGetState(0, ctypes.byref(st)) == 0
        b = st.gp.buttons if ok else 0
        if ok and abs(st.gp.lx) > 16000: b |= 0x0004 if st.gp.lx < 0 else 0x0008
        if ok and abs(st.gp.ly) > 16000: b |= 0x0001 if st.gp.ly > 0 else 0x0002
        return ok, b

def run_script(core: Core, script: str, out: str, log) -> list[dict]:
    """deterministic replay of an input script (used by tests and by `sim.ps1 run-simulator --script`)"""
    os.makedirs(out, exist_ok=True); shots = []
    for raw in open(script, encoding="utf-8"):
        line = raw.strip()
        if not line or line.startswith("#"): continue
        op, _, arg = line.partition(" ")
        if op == "press": core.press(arg)
        elif op == "release": core.release(arg)
        elif op == "tick": core.tick(int(arg))
        elif op == "focus": core.send(f"focus {arg}")
        elif op == "device": core.send(f"device {arg}")
        elif op == "launch": log(core.launch_cycle())
        elif op == "wait": time.sleep(int(arg) / 1000)
        elif op == "frame":
            ppm = os.path.join(out, arg.replace(".png", ".ppm")); ev = core.frame(ppm)
            if ev and ev["ev"] == "frame_written": png = os.path.join(out, arg); ppm_to_png(ppm, png); os.remove(ppm); shots.append({"png": png, "crc32": ev["crc32"], "w": ev["w"], "h": ev["h"]})
            else: shots.append({"error": ev})
        core.drain(0.05)
    return shots

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--path", default="windows"); ap.add_argument("--fixture", default=os.path.join(SIM, "fixtures", "mixed.json")); ap.add_argument("--scale", type=float, default=0.6)
    ap.add_argument("--geometry", default="+40+40", help="window position on the primary monitor"); ap.add_argument("--demo-keys", action="store_true", help="inject a short key sequence through the window event path after start (evidence capture)"); ap.add_argument("--script"); ap.add_argument("--out", default=os.path.join(ARTIFACTS, "viewer")); ap.add_argument("--core-arg", action="append", default=[])
    a = ap.parse_args(); os.makedirs(a.out, exist_ok=True)
    evlog = open(os.path.join(a.out, "events.jsonl"), "w", encoding="utf-8")
    def log(ev): evlog.write(json.dumps(ev, default=str) + "\n"); evlog.flush()
    core = Core(a.path, a.core_arg); ready = core.ready(15); log(ready or {"ev": "no_ready"})
    if not ready: print("core did not become ready", core.stderr[-300:]); sys.exit(3)
    log(core.load(a.fixture))
    if a.script:
        shots = run_script(core, a.script, a.out, log); st = core.stats(); log(st); code = core.shutdown()
        for ev in core.events: log(ev)
        print(json.dumps({"schema": "ccs-sim-viewer-script/1", "path": a.path, "shots": shots, "events": len(core.events), "exit": code, "stats": {k: st.get(k) for k in ("inputs", "frames", "launches", "clock_ms", "rss_kb")} if st else None})); return
    import tkinter as tk
    from PIL import Image, ImageTk
    root = tk.Tk(); root.title(f"CCS Console Simulator: DIAGNOSTIC VIEWER ({a.path}) - not a HOME screen design"); root.configure(bg="#111"); root.geometry(a.geometry)
    W, H = int(1280 * a.scale), int(720 * a.scale)
    canvas = tk.Canvas(root, width=W, height=H, bg="#000", highlightthickness=0); canvas.pack()
    status = tk.StringVar(value="ready"); tk.Label(root, textvariable=status, anchor="w", bg="#111", fg="#ddd", font=("Consolas", 10)).pack(fill="x")
    evbox = tk.Text(root, height=8, bg="#0b0b0f", fg="#9ee", font=("Consolas", 9)); evbox.pack(fill="x")
    held: set[str] = set(); frame_n = [0]; img_ref = [None]; last_tick = [time.time()]; pad = XInput(); pad_prev = [0]; pad_conn = [False]
    def show(ev):
        evbox.insert("end", json.dumps(ev)[:160] + "\n"); evbox.see("end"); log(ev)
    def render():
        ppm = os.path.join(a.out, "live.ppm"); ev = core.frame(ppm)
        if ev and ev["ev"] == "frame_written":
            im = Image.open(ppm).resize((W, H), Image.NEAREST); img_ref[0] = ImageTk.PhotoImage(im); canvas.create_image(0, 0, anchor="nw", image=img_ref[0]); frame_n[0] += 1
        return ev
    def pump():
        now = time.time(); ms = int((now - last_tick[0]) * 1000); last_tick[0] = now
        if ms > 0: core.send(f"tick {ms}")
        ok, b = pad.poll()
        if ok != pad_conn[0]: pad_conn[0] = ok; core.send("device connect" if ok else "device disconnect"); show({"ev": "viewer_gamepad", "connected": ok})
        if ok:
            for mask, name in XINPUT_BUTTONS.items():
                was, is_ = bool(pad_prev[0] & mask), bool(b & mask)
                if is_ and not was: core.press(name)
                if was and not is_: core.release(name)
            pad_prev[0] = b
        for ev in core.drain(0.01): show(ev)
        ev = render(); status.set(f"frame {frame_n[0]}  held {sorted(held)}  crc {ev.get('crc32') if ev else '?'}  F2 shot  F5 reload  F8 disconnect  F9 launch stub  Esc quit")
        root.after(50, pump)
    def key_down(e):
        b = KEYMAP.get(e.keysym)
        if b and b not in held: held.add(b); core.press(b)
        elif e.keysym == "F2":
            png = os.path.join(a.out, f"screenshot-{int(time.time())}.png"); ppm = png.replace(".png", ".ppm"); ev = core.frame(ppm)
            if ev and ev["ev"] == "frame_written": ppm_to_png(ppm, png); os.remove(ppm); show({"ev": "viewer_screenshot", "png": png, "crc32": ev["crc32"]})
        elif e.keysym == "F5": show(core.load(a.fixture))
        elif e.keysym == "F8": pad_conn[0] = not pad_conn[0]; core.send("device connect" if pad_conn[0] else "device disconnect")
        elif e.keysym == "F9": show({"ev": "viewer_launch_request", "note": "diagnostic: ask the core to launch the cursor entry; the adapter runs the DUMMY stub"}); show(core.launch_cycle(stub_exit=0, stub_delay_ms=300))
        elif e.keysym == "Escape": root.destroy()
    def key_up(e):
        b = KEYMAP.get(e.keysym)
        if b in held: held.discard(b); core.release(b)
    root.bind("<KeyPress>", key_down); root.bind("<KeyRelease>", key_up)
    root.bind("<FocusOut>", lambda e: (held.clear(), core.send("focus lost"))); root.bind("<FocusIn>", lambda e: core.send("focus gained"))
    if a.demo_keys:
        def demo():
            seq = [("Down", 0), ("Down", 200), ("Down", 400), ("x", 700), ("F9", 1000), ("F2", 2200)]
            for ks, t in seq: root.after(1500 + t, lambda ks=ks: root.event_generate("<KeyPress>", keysym=ks)); root.after(1500 + t + 80, lambda ks=ks: root.event_generate("<KeyRelease>", keysym=ks))
        root.after(100, demo)
    root.after(50, pump); root.mainloop()
    code = core.shutdown(); log({"ev": "viewer_closed", "exit": code}); evlog.close()

if __name__ == "__main__": main()
