"""Tests of CCS Nucleus's start chooser, the native HOME program's --choose mode (core/chooser.c). Tests only: no console, no USB.
Copyright (C) 2026 Rance9, GPL-3.0-or-later.

  python test_chooser.py --build DIR [--fonts DIR] [--out DIR] [--only script,device]

DIR: a build of the core in scripts/wsl_build.sh's layout (windows-x86_64/ccs-sim-core.exe, linux-x86_64/ccs-sim-core, arm-musl/ccs-sim-core,
arm-musl-device/ccs-home-armv7: the stripped console build the CCS HOME package carries). --fonts: the player's menu fonts (silk17,
sans600_22, sans400_17 .ccsf; without them the chooser draws with the core's built-in letters, and both are tested when given).
1. --script (commands on stdin, the clock moved only by the commands) on Windows, on x86-64 Linux (WSL) and on 32-bit ARM Linux (the
   console's instruction set, under qemu-arm in WSL): what is chosen, by which button or by the countdown, focus, refusals, the settings
   panel, scrolling and invalid choices files; the same commands give the same events and the same picture bytes on all three.
2. The console mode (Linux only: the x86-64 build in WSL and ccs-home-armv7 under qemu-arm): a test framebuffer file and a FIFO of
   controller events in the target's own record format (16 bytes on 32-bit ARM, 24 on x86-64), real time: the controllers' settling after
   the open, a button held through it, the countdown, the wait for the choosing button to be let go, the stop signal, a missing screen.
QEMU is not the console: this shows the program's behavior on these inputs, not the console's hardware, its controllers or its timing."""
from __future__ import annotations
import argparse, json, os, re, shutil, subprocess, sys, tempfile, time

HERE = os.path.dirname(os.path.abspath(__file__))
passed = failed = 0; failures = []
def check(ok: bool, what: str):
    global passed, failed
    if ok: passed += 1
    else: failed += 1; failures.append(what)
    print(("ok   " if ok else "FAIL ") + what, flush=True)

def wpath(p: str) -> str:
    p = os.path.abspath(p); return "/mnt/" + p[0].lower() + p[2:].replace("\\", "/") if os.name == "nt" else p
def wsl(script: str, work: str, timeout: int = 300) -> str:
    """a bash script in WSL (written to a file: wsl.exe's own command line is not used for arguments); its stdout"""
    f = os.path.join(work, f"run-{time.time_ns()}.sh"); open(f, "w", newline="\n").write(script)
    cmd = ["wsl.exe", "-d", "Ubuntu", "--", "bash", wpath(f)] if os.name == "nt" else ["bash", f]
    r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout, stdin=subprocess.DEVNULL)
    return r.stdout

BUILD = WORK = OUT = FONTS = QEMU = None

# ---------------------------------------------------------------- choices files
NATIVE = "choice=native|native|same|ok|Your Console's Own Menu|"
HOME = "choice=ccs-home|ccs-home|same|ok|CCS HOME|"
SETTINGS = ["settings=Recovery and Settings", "setting=native|Start Your Console's Own Menu", "setting=clear|Clear Start Problems",
            "setting=show|Show This Screen at Start: On", "setting=start-with|Start With: Last Used", "setting=countdown|Countdown: 10 Seconds",
            "help=If your console does not start the way it should: turn it off, connect it to a computer with its USB cable, hold RESET, switch POWER on and keep holding RESET for about 5 seconds. Then open Console Rescue in the CCS Console Workbench."]
def choices(mode="boot", highlight="ccs-home", auto="ccs-home", timeout=10, notice="", panel="main", opts=None, settings=True, extra=()) -> str:
    opts = opts if opts is not None else [NATIVE, HOME,
            "choice=owner-1|owner-root|restart|unavailable|My SNES System|Its system image is on USB storage that is not connected.",
            "choice=owner-2|owner-root|restart|retry|Other System|It did not finish starting last time.",
            "choice=owner-3|owner-root|restart|ok|Third System|"]
    L = ["schema=ccs-choices/1", f"mode={mode}", "title=Choose a System", f"highlight={highlight}", f"auto={auto}", f"timeout={timeout}", f"notice={notice}", f"panel={panel}"]
    return "\n".join(L + list(opts) + (SETTINGS if settings else []) + list(extra)) + "\n"

def press(b): return [f"input press {b}", f"input release {b}"]
def dedup(xs): return [x for i, x in enumerate(xs) if i == 0 or xs[i - 1] != x]

# ---------------------------------------------------------------- 1. --script on the three paths
def run_script(path: str, ctext: str, cmds: list[str], name: str, fonts: bool) -> dict:
    """one run on one path: {"rc", "events", "frames": {frame name: bytes}}"""
    d = os.path.join(OUT, "script", path, name); os.makedirs(d, exist_ok=True)
    cf = os.path.join(d, "choices.txt"); open(cf, "w", newline="\n").write(ctext)
    lin = path != "windows"
    P = (lambda p: wpath(p)) if lin else (lambda p: p)
    sc = [re.sub(r"^frame (\S+)$", lambda m: "frame " + P(os.path.join(d, m.group(1))), c) for c in cmds]
    sf = os.path.join(d, "commands.txt"); open(sf, "w", newline="\n").write("\n".join(sc) + "\n")
    args = ["--choose", P(cf), "--script", "--final-ppm", P(os.path.join(d, "final.ppm"))] + (["--fonts", wpath(FONTS) if lin else FONTS] if fonts and FONTS else [])
    if path == "windows":
        with open(sf, "rb") as fi: r = subprocess.run([os.path.join(BUILD, "windows-x86_64", "ccs-sim-core.exe")] + args, stdin=fi, capture_output=True, timeout=60)
        rc, out = r.returncode, r.stdout.decode("utf-8", "replace")
    else:
        exe = wpath(os.path.join(BUILD, "linux-x86_64", "ccs-sim-core")) if path == "linux" else wpath(os.path.join(BUILD, "arm-musl", "ccs-sim-core"))
        pre = f"'{QEMU}' " if path == "arm" else ""
        q = " ".join("'" + a.replace("'", "'\\''") + "'" for a in args)
        o = wsl(f"{pre}'{exe}' {q} < '{wpath(sf)}'; echo \"RC=$?\"\n", WORK, 120)
        m = re.search(r"^RC=(\d+)$", o, re.M); rc = int(m.group(1)) if m else -1; out = o[: m.start()] if m else o
    ev = [json.loads(l) for l in out.splitlines() if l.startswith("{")]
    frames = {}
    for f in sorted(os.listdir(d)):
        if f.endswith(".ppm"): frames[f] = open(os.path.join(d, f), "rb").read()
    return {"rc": rc, "events": ev, "frames": frames}

def strip(ev):   # what must be equal on every path (the paths in frame events differ by operating system)
    return [{k: v for k, v in e.items() if k != "path"} for e in ev]

def scenarios() -> list[tuple]:
    """(name, choices, commands, check(events) -> (ok, detail))"""
    def ans(ev): return next((e["answer"] for e in ev if e["ev"] == "answer"), None)
    def last(ev): return [e for e in ev if e["ev"] == "chooser_state"][-1]
    S = []
    S.append(("countdown-default", choices(), ["state", "tick 9999", "state", "tick 1", "frame end.ppm"],
              lambda ev: (ans(ev) == "choice ccs-home timeout" and ev[1]["focus"] == "ccs-home" and ev[1]["countdown_ms"] == 10000
                          and "CCS HOME starts in 10 seconds" in ev[1]["status"] and [e for e in ev if e["ev"] == "chooser_state"][-1]["countdown_ms"] == 1,
                          "the default (highlighted, with the countdown on the TV) starts by itself when the countdown ends, not one millisecond before")))
    S.append(("countdown-stopped", choices(), ["tick 3000"] + press("down") + ["tick 60000", "state"] + press("up") + press("a"),
              lambda ev: (ans(ev) == "choice ccs-home a" and any(e["ev"] == "chooser_state" and e["countdown_ms"] == -1 and e["focus"] == "owner-1" for e in ev),
                          "any button stops the countdown: a minute later nothing has started; the person's own choice then starts")))
    S.append(("last-choice-menu", choices(mode="menu", highlight="owner-3", auto="", timeout=0), ["state", "tick 120000", "state"] + press("a"),
              lambda ev: (ans(ev) == "choice owner-3 a" and ev[1]["focus"] == "owner-3" and all(e.get("countdown_ms", -1) == -1 for e in ev if e["ev"] == "chooser_state"),
                          "the remembered last choice is focused first; without a countdown nothing starts by itself (two minutes)")))
    S.append(("refused-unavailable", choices(highlight="owner-1", auto="", timeout=0), ["state", "frame focus-unavailable.ppm"] + press("a") + ["state", "frame refused.ppm"] + press("start") + press("down") + press("a"),
              lambda ev: (ans(ev) == "choice owner-2 a" and "Its system image is on USB storage that is not connected." in ev[1]["status"]
                          and any(e["ev"] == "chooser_state" and e["refusal"] == "My SNES System cannot start now. Its system image is on USB storage that is not connected." for e in ev),
                          "an option that cannot start shows why; A and START on it are refused with that reason and nothing is chosen; one that failed before can still be chosen")))
    S.append(("auto-not-ok", choices(highlight="owner-1", auto="owner-1", timeout=5), ["state", "tick 30000", "state"] + press("up") + press("a"),
              lambda ev: (ans(ev) == "choice ccs-home a" and json.loads(json.dumps(ev[0]))["auto"] == -1 and all(e.get("countdown_ms", -1) == -1 for e in ev if e["ev"] == "chooser_state"),
                          "a countdown never runs for an option that cannot start")))
    S.append(("auto-retry", choices(highlight="owner-2", auto="owner-2", timeout=5), ["tick 30000", "state"] + press("b") + ["tick 1000", "state"] + press("select") + press("left") + press("right") + press("a"),
              lambda ev: (ans(ev) == "choice owner-2 a" and ev[0]["auto"] == -1,
                          "nor for one that did not start last time; in the start (not from the menu) B, SELECT, LEFT and RIGHT choose nothing")))
    S.append(("menu-back", choices(mode="menu", auto="", timeout=0), press("down") + press("b"),
              lambda ev: (ans(ev) == "back", "from the console's menu, B goes back there without a choice")))
    S.append(("start-button", choices(auto="", timeout=0), press("start"),
              lambda ev: (ans(ev) == "choice ccs-home start", "START chooses like A")))
    S.append(("wrap-and-settings", choices(highlight="native", auto="", timeout=0), press("up") + ["state", "frame settings-row.ppm"] + press("a") + ["state", "frame settings.ppm"] + press("down") + press("down") + ["state"] + press("a"),
              lambda ev: (ans(ev) == "action show" and any(e["ev"] == "chooser_state" and e["focus"] == "settings" and e["panel"] == "main" for e in ev)
                          and any(e["ev"] == "chooser_state" and e["panel"] == "settings" and e["focus"] == "native" for e in ev),
                          "UP from the first row goes to the last (Recovery and Settings); A opens its panel; a row there answers its action")))
    S.append(("settings-back", choices(highlight="native", auto="", timeout=0), press("up") + press("a") + press("b") + ["state"] + press("a") + press("up") + ["state"] + press("a") + press("down") + press("a"),
              lambda ev: (ans(ev) == "choice native a" and [e for e in ev if e["ev"] == "chooser_state" and e["panel"] == "settings" and e["focus"] == "back"] != [],
                          "B and the Back row return from the panel to the choices")))
    S.append(("settings-recovery-entry", choices(highlight="native", auto="", timeout=0), press("up") + press("a") + press("a"),
              lambda ev: (ans(ev) == "choice native a", "the recovery entry: Start Your Console's Own Menu, the first row of the panel, starts the console's own system")))
    S.append(("settings-reopened", choices(panel="settings", highlight="countdown", notice="Countdown: 20 seconds.", auto="ccs-home", timeout=10), ["state", "frame reopened.ppm", "tick 20000"] + press("a"),
              lambda ev: (ans(ev) == "action countdown" and ev[1]["panel"] == "settings" and ev[1]["focus"] == "countdown" and ev[1]["countdown_ms"] == -1,
                          "after an action the panel opens again on the same row, with what it did; no countdown runs there")))
    many = [NATIVE, HOME] + [f"choice=owner-{i}|owner-root|restart|ok|System Number {i}|" for i in range(1, 13)]
    S.append(("scroll", choices(opts=many, highlight="native", auto="", timeout=0), ["state", "frame scroll-top.ppm"] + press("up") + ["state", "frame scroll-end.ppm"] + press("up") + press("a"),
              lambda ev: (ans(ev) == "choice owner-12 a" and ev[1]["visible"] < 15 and last(ev)["top"] > 0 and last(ev)["focus"] == "owner-12",
                          "more choices than fit: the list scrolls with the focus")))
    S.append(("repeat", choices(highlight="native", auto="", timeout=0, settings=False, opts=many), ["input press down", "tick 399", "state", "tick 1", "state", "tick 220", "input release down", "state", "input press a", "tick 2000", "input release a", "state"],
              lambda ev: (dedup([e["focus"] for e in ev if e["ev"] == "chooser_state"]) == ["native", "ccs-home", "owner-1", "owner-3"] and ans(ev) == "choice owner-3 a",
                          "a held D-pad repeats after 400 ms, then every 110 ms (the HOME's own timing); a held A chooses once")))
    S.append(("notice", choices(notice="CCS HOME ended with a problem. Choose what to start.", highlight="native", auto="", timeout=0,
                                opts=[NATIVE, "choice=ccs-home|ccs-home|same|unavailable|CCS HOME|It ended with a problem just now. Turn the console off and on to try it again."]),
              ["frame notice.ppm"] + press("down") + press("a") + ["state", "frame notice-refused.ppm"] + press("up") + press("a"),
              lambda ev: (ans(ev) == "choice native a" and ev[1]["notice"].startswith("CCS HOME ended"), "the notice is shown; CCS HOME that just failed is refused for this start")))
    return S

INVALID = {
    "no schema line": "mode=boot\n" + NATIVE + "\n",
    "another schema": "schema=ccs-choices/2\n" + NATIVE + "\n",
    "the console's own system missing": "schema=ccs-choices/1\n" + HOME + "\n",
    "the console's own system unavailable": "schema=ccs-choices/1\nchoice=native|native|same|unavailable|Your Console's Own Menu|why\n",
    "an option listed twice": "schema=ccs-choices/1\n" + NATIVE + "\n" + HOME + "\n" + HOME + "\n",
    "an unknown kind": "schema=ccs-choices/1\n" + NATIVE + "\nchoice=x|other|same|ok|X|\n",
    "native under another id": "schema=ccs-choices/1\n" + NATIVE + "\nchoice=x|native|same|ok|X|\n",
    "an unknown root": "schema=ccs-choices/1\n" + NATIVE + "\nchoice=x|ccs-home|maybe|ok|X|\n",
    "an unknown state": "schema=ccs-choices/1\n" + NATIVE + "\nchoice=x|ccs-home|same|fine|X|\n",
    "a choice with missing fields": "schema=ccs-choices/1\n" + NATIVE + "\nchoice=x|ccs-home|same|ok\n",
    "an id with capitals": "schema=ccs-choices/1\n" + NATIVE + "\nchoice=X|ccs-home|same|ok|X|\n",
    "a control character": "schema=ccs-choices/1\n" + NATIVE + "\nnotice=a\tb\n",
    "a timeout over 99": "schema=ccs-choices/1\n" + NATIVE + "\ntimeout=100\n",
    "an empty label": "schema=ccs-choices/1\n" + NATIVE + "\nchoice=x|ccs-home|same|ok||\n",
    "a label too long for the screen": "schema=ccs-choices/1\n" + NATIVE + "\nchoice=x|ccs-home|same|ok|" + "A" * 64 + "|\n",
    "an empty file": "",
}

def part_script():
    print("== 1. --script on Windows, x86-64 Linux and 32-bit ARM (qemu-arm)")
    for fonts in ([False, True] if FONTS else [False]):
        tag = "fonts" if fonts else "builtin"
        for name, ct, cmds, chk in scenarios():
            runs = {p: run_script(p, ct, cmds, f"{name}-{tag}", fonts) for p in ("windows", "linux", "arm")}
            ev = runs["windows"]["events"]; ok, what = chk(ev)
            same = all(strip(runs[p]["events"]) == strip(ev) and runs[p]["frames"] == runs["windows"]["frames"] for p in ("linux", "arm"))
            fin = [e for e in ev if e["ev"] == "final_frame"]
            check(ok and same and all(r["rc"] == 0 for r in runs.values()) and (bool(fin) or not any(e["ev"] == "answer" for e in ev)),
                  f"script {name} ({tag}): {what}; identical events and pictures on Windows, Linux and ARM ({len(runs['windows']['frames'])} pictures"
                  + (f", answer {next((e['answer'] for e in ev if e['ev'] == 'answer'), None)!r}" if ok else f"; events {ev[-3:]}") + ")")
    for why, text in INVALID.items():
        r = {p: run_script(p, text, ["state"], "invalid-" + re.sub(r"[^a-z]+", "-", why), False) for p in ("windows", "linux", "arm")}
        e = r["windows"]["events"]
        check(all(x["rc"] == 3 for x in r.values()) and e and e[0]["ev"] == "choices_refused" and not any(x["ev"] == "answer" for x in e),
              f"script: a choices file with {why} is refused before anything is shown, on every path (exit 3: {e[0]['error'] if e else 'no event'})")
    r = run_script("windows", choices(), ["tick 1000", "quit"], "quit", False)
    check(r["rc"] == 0 and r["events"][-1]["ev"] == "quit" and not any(x["ev"] == "answer" for x in r["events"]), "script: quit ends without an answer")

# ---------------------------------------------------------------- 2. the console mode (framebuffer file, FIFO of controller events)
FEEDER = r'''import os, struct, sys, time
fifo, size, keys = sys.argv[1], int(sys.argv[2]), (sys.argv[3] if len(sys.argv) > 3 else "")
CODES = {"a": 304, "b": 305, "select": 314, "start": 315, "left": 704, "right": 705, "up": 706, "down": 707}
fd = os.open(fifo, os.O_WRONLY)   # blocks until the chooser has opened its end
t0 = time.monotonic()
for tok in keys.split():
    ms, btn, st = tok.split(":"); wait = t0 + int(ms) / 1000 - time.monotonic()
    if wait > 0: time.sleep(wait)
    now = time.time(); tv = struct.pack("<II" if size == 16 else "<QQ", int(now), int((now % 1) * 1e6))
    os.write(fd, tv + struct.pack("<HHi", 1, CODES[btn], 1 if st == "p" else 0))
time.sleep(60)
'''
def run_device(build: str, ctext: str, keys: str, extra: str = "", term_after: float = 0) -> dict:
    """the console mode of one build (lin: x86-64 in WSL; arm: ccs-home-armv7 under qemu-arm) on a test framebuffer and a FIFO"""
    exe = wpath(os.path.join(BUILD, "linux-x86_64", "ccs-sim-core")) if build == "lin" else wpath(os.path.join(BUILD, "arm-musl-device", "ccs-home-armv7"))
    pre = f"'{QEMU}' " if build == "arm" else ""; size = 24 if build == "lin" else 16
    cf = os.path.join(WORK, f"dev-{time.time_ns()}.txt"); open(cf, "w", newline="\n").write(ctext)
    ff = os.path.join(WORK, "feeder.py"); open(ff, "w", newline="\n").write(FEEDER)
    fonts = f"--fonts '{wpath(FONTS)}'" if FONTS else ""
    run = f"{'' if term_after else 'timeout -s TERM 25 '}{pre}'{exe}' --choose '{wpath(cf)}' {fonts} --fb-file \"$T/fb.raw\" --fb-geometry pan=1 --input \"$T/pad.fifo\" --dump-ppm \"$T/dump.ppm\" {extra} > \"$T/out\" 2> \"$T/err\""
    body = (f"{run} & cp=$!; sleep {term_after}; kill -TERM $cp; wait $cp; rc=$?" if term_after else f"{run}; rc=$?")
    o = wsl(f"""set -u
T=$(mktemp -d /tmp/ccs-chooser-XXXXXX)
mkfifo "$T/pad.fifo"; head -c 7372800 /dev/zero > "$T/fb.raw"
python3 '{wpath(ff)}' "$T/pad.fifo" {size} '{keys}' & fp=$!
t0=$(date +%s%N)
{body}
t1=$(date +%s%N)
kill $fp 2>/dev/null; wait $fp 2>/dev/null
echo "RC=$rc MS=$(( (t1 - t0) / 1000000 ))"
sed 's/^/OUT /' "$T/out"; sed 's/^/ERR /' "$T/err"
[ -f "$T/dump.ppm" ] && echo "DUMP $(sha256sum "$T/dump.ppm" | cut -c1-64) $(stat -c %s "$T/dump.ppm")"
echo "FB $(od -An -tu1 -j $((5120 * 300 + 640 * 4)) -N 4 "$T/fb.raw" | tr -s ' ')"
rm -rf "$T"
""", WORK, 120)
    m = re.search(r"^RC=(\d+) MS=(\d+)$", o, re.M)
    out = [l[4:] for l in o.splitlines() if l.startswith("OUT ")]; err = [l[4:] for l in o.splitlines() if l.startswith("ERR ")]
    trace = [json.loads(l) for l in err if l.startswith("{")]
    dump = re.search(r"^DUMP (\w+) (\d+)$", o, re.M); fbpx = re.search(r"^FB (.*)$", o, re.M)
    return {"rc": int(m.group(1)) if m else -1, "ms": int(m.group(2)) if m else -1, "out": out, "trace": trace, "dump": dump.group(1) if dump else None,
            "fb": fbpx.group(1).split() if fbpx else [], "raw": o}

def part_device():
    print("== 2. the console mode (x86-64 Linux build in WSL; the console build ccs-home-armv7 under qemu-arm)")
    import hashlib
    for b in ("lin", "arm"):
        name = "x86-64 Linux" if b == "lin" else "ccs-home-armv7 (ARM)"
        # pressed during the controllers' settling (400 ms after the open): never a press; then DOWN, A
        r = run_device(b, choices(highlight="native", auto="", timeout=0), "100:down:p 150:down:r 900:down:p 950:down:r 1300:a:p 1400:a:r")
        check(r["rc"] == 0 and r["out"] == ["choice ccs-home a"] and any(t["ev"] == "chooser_open" for t in r["trace"]) and r["fb"] != ["0", "0", "0", "0"],
              f"device {name}: a press while the controller settles is ignored; DOWN then A chooses CCS HOME (one answer line on stdout; the picture is on the framebuffer) ({r['out']}, {r['ms']} ms)")
        # the final picture is the same picture the script mode draws for this answer
        sr = run_script("windows", choices(highlight="native", auto="", timeout=0), press("down") + press("a"), f"device-final-{b}", bool(FONTS))
        fin = sr["frames"].get("final.ppm", b"")
        check(r["dump"] == hashlib.sha256(fin).hexdigest(), f"device {name}: the last picture (Starting CCS HOME...) is byte for byte the one the Windows script mode draws")
        # a button held from before the open through the settling counts only after it is let go
        # (times from the program's own clock in its trace: ms from the start of the countdown; QEMU's own start-up is not in them)
        r = run_device(b, choices(highlight="native", auto="ccs-home", timeout=2), "50:a:p 900:a:r")
        a = next((t for t in r["trace"] if t["ev"] == "answer"), {})
        check(r["rc"] == 0 and r["out"] == ["choice ccs-home timeout"] and 2000 <= a.get("answer_ms", -1) <= 3500,
              f"device {name}: A held through the settling and let go is not a press; the countdown then starts the default after 2 s (answered at {a.get('answer_ms')} ms)")
        # the press that chooses is let go before the program ends
        r = run_device(b, choices(highlight="native", auto="", timeout=0), "800:a:p 2300:a:r")
        a = next((t for t in r["trace"] if t["ev"] == "answer"), {})
        check(r["out"] == ["choice native a"] and a.get("end_ms", 0) - a.get("answer_ms", 0) >= 1300 and a.get("held_at_end") == 0,
              f"device {name}: after A chooses, the program waits until A is let go before it ends (A held 1,500 ms; it ended {a.get('end_ms', 0) - a.get('answer_ms', 0)} ms after the choice)")
        r = run_device(b, choices(highlight="native", auto="", timeout=0), "800:a:p", extra="--release-wait-ms 1000")
        a = next((t for t in r["trace"] if t["ev"] == "answer"), {})
        check(r["out"] == ["choice native a"] and a.get("held_at_end") == 1 and 950 <= a.get("end_ms", 0) - a.get("answer_ms", 0) <= 2500,
              f"device {name}: a button never let go keeps it waiting only its limit (1,000 ms: {a.get('end_ms', 0) - a.get('answer_ms', 0)} ms)")
        # refused with the reason (traced), then UP twice and A
        r = run_device(b, choices(highlight="owner-1", auto="", timeout=0), "800:a:p 850:a:r 1100:up:p 1150:up:r 1300:up:p 1350:up:r 1500:a:p 1550:a:r")
        check(r["out"] == ["choice native a"] and any(t["ev"] == "refused" and "USB storage" in t["text"] for t in r["trace"]),
              f"device {name}: A on an option that cannot start is refused with its reason; the console's own system is then chosen ({r['out']})")
        # stopped (the console is shutting down): no answer, exit 15
        r = run_device(b, choices(highlight="native", auto="", timeout=0), "", term_after=1.5)
        check(r["rc"] == 15 and r["out"] == [] and any(t["ev"] == "stopped" for t in r["trace"]), f"device {name}: asked to end (TERM), it ends at once without an answer (exit {r['rc']})")
        # no controller at all (the FIFO closed): the countdown still works
        r = run_device(b, choices(highlight="ccs-home", auto="ccs-home", timeout=1), "0:select:r", extra="")
        check(r["out"] == ["choice ccs-home timeout"], f"device {name}: with no controller the countdown still starts the default ({r['out']})")
        # no screen, an invalid file
        exe = wpath(os.path.join(BUILD, "linux-x86_64", "ccs-sim-core")) if b == "lin" else wpath(os.path.join(BUILD, "arm-musl-device", "ccs-home-armv7"))
        pre = f"'{QEMU}' " if b == "arm" else ""
        cf = os.path.join(WORK, "ok.txt"); open(cf, "w", newline="\n").write(choices()); bf = os.path.join(WORK, "bad.txt"); open(bf, "w", newline="\n").write("schema=ccs-choices/1\n" + HOME + "\n")
        o = wsl(f"{pre}'{exe}' --choose '{wpath(cf)}' --fb /nonexistent/fb0 > /tmp/ccs-ch-o 2>/dev/null; echo \"RC=$? OUT=$(cat /tmp/ccs-ch-o)\"; {pre}'{exe}' --choose '{wpath(bf)}' --fb /nonexistent/fb0 > /tmp/ccs-ch-o 2>/dev/null; echo \"RC2=$? OUT2=$(cat /tmp/ccs-ch-o)\"; rm -f /tmp/ccs-ch-o\n", WORK)
        check("RC=12 OUT=\n" in o + "\n" and "RC2=3 OUT2=" in o, f"device {name}: no screen: exit 12 and no answer; an invalid choices file: exit 3 and no answer ({o.strip()})")

def main() -> int:
    global BUILD, WORK, OUT, FONTS, QEMU
    ap = argparse.ArgumentParser(); ap.add_argument("--build", required=True); ap.add_argument("--fonts"); ap.add_argument("--out"); ap.add_argument("--only"); a = ap.parse_args()
    BUILD = os.path.abspath(a.build); FONTS = os.path.abspath(a.fonts) if a.fonts else None
    WORK = tempfile.mkdtemp(prefix="ccs-chooser-test-"); OUT = os.path.abspath(a.out) if a.out else os.path.join(WORK, "out")
    if os.path.isdir(OUT) and os.listdir(OUT): raise SystemExit(f"{OUT} is not empty")
    os.makedirs(OUT, exist_ok=True)
    QEMU = wsl("echo $HOME/.cache/ccs-sim/toolchain/deb/usr/bin/qemu-arm\n", WORK).strip()
    if wsl(f"test -x '{QEMU}' && echo yes\n", WORK).strip() != "yes": raise SystemExit("qemu-arm (the project's WSL toolchain) is not available")
    for p in ("windows-x86_64/ccs-sim-core.exe", "linux-x86_64/ccs-sim-core", "arm-musl/ccs-sim-core", "arm-musl-device/ccs-home-armv7"):
        if not os.path.isfile(os.path.join(BUILD, *p.split("/"))): raise SystemExit(f"{p} is not in {BUILD}")
    info = wsl(f"file -b '{wpath(os.path.join(BUILD, 'arm-musl-device', 'ccs-home-armv7'))}'\n", WORK).strip()
    check("ARM" in info and "statically linked" in info and "stripped" in info and "not stripped" not in info, f"build: ccs-home-armv7 is the console's static, stripped 32-bit ARM build ({info})")
    parts = {"script": part_script, "device": part_device}
    try:
        for k in (a.only.split(",") if a.only else parts): parts[k]()
    finally:
        shutil.rmtree(WORK, ignore_errors=True) if not a.out else None
    for f in failures: print("FAILED:", f)
    print(f"RESULT {passed}/{passed + failed}")
    return 0 if failed == 0 else 1

if __name__ == "__main__": sys.exit(main())
