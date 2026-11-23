r"""ClassicCartStudio.exe --play FILE (tools/desktop/ClassicCartStudio/PlayFile.cs, companion_app.py --play-file, play.play_file): the built
launcher started for real, with its backend (the installed ClassicCartStudioBackend.exe beside it, or, for a launcher built in a checkout,
this source through CCS_DEV/CCS_BACKEND_SOURCE) and the game player given here. A synthetic original game file (an iNES file made here, not
a real game, at a path with spaces and letters outside ASCII); a new data folder (NES_STUDIO_DATA); no console, no network.
  play        only the game player's window opens (no Classic Cart Studio window), in direct mode (no HOME), running a copy this run owns of the
              file's current bytes; its saves are the content's own saves in the data folder; closing the window ends the command with 0; the
              file is unchanged, nothing is added to the library or a collection, and the run's folders are gone
  rebuilt     the same file name with new bytes: the next run plays the new bytes
  busy        a second --play while a game is open ends with 4 at once and leaves the open game as it is
  studio open with a Classic Cart Studio open (its single-instance lock taken): a real one is never touched and its window is not brought
              forward; otherwise a stand-in holds the lock with its link pipe listening, and --play neither waits for it nor sends it anything
  refusals    no such file: 3; --play without a file: 2; a file without a header whose content no record knows: 3; no game player: 5 (a
              launcher built in a checkout only: an installed layout always has its own player)
  python test_play_file.py --launcher <ClassicCartStudio.exe> --player <ccs-player.exe> --fonts <player fonts folder> [--site <built site>]
  Exit 0 = PASS."""
from __future__ import annotations
import argparse, ctypes, ctypes.wintypes as wt, hashlib, json, os, shutil, subprocess, sys, tempfile, threading, time
HERE = os.path.dirname(os.path.abspath(__file__))
ap = argparse.ArgumentParser(); ap.add_argument("--launcher", required=True); ap.add_argument("--player", required=True); ap.add_argument("--fonts", required=True); ap.add_argument("--site")
A = ap.parse_args()
checks = []
def ok(k, v, n=""): checks.append((k, bool(v))); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:300]})" if n and not v else ""), flush=True)
def sha(b): return hashlib.sha256(b).hexdigest()
def game(seed: str) -> bytes:   # an iNES file made here: 32 KB of program and 8 KB of picture data from a hash chain (not a game; the player runs it as one)
    body = bytearray(); h = hashlib.sha256(("CCS-PLAY-FILE-TEST " + seed).encode()).digest()
    while len(body) < 40960: h = hashlib.sha256(h).digest(); body += h
    body = bytearray(body[:40960]); body[0x7FFC:0x7FFE] = b"\x00\x80"   # the reset vector points into the program
    return bytes([0x4E, 0x45, 0x53, 0x1A, 2, 1, 0, 0]) + bytes(8) + bytes(body)
T = tempfile.mkdtemp(prefix="ccs-playfile-"); DATA = os.path.join(T, "data")
FOLDER = os.path.join(T, "play test", "Ünïcödé gämes ✓"); os.makedirs(FOLDER); ROM = os.path.join(FOLDER, "Test Game.nes")
ENV = dict(os.environ, NES_STUDIO_DATA=DATA, CCS_PLAYER_EXE=A.player, CCS_PLAYER_FONTS=A.fonts, CCS_DEV="1", CCS_BACKEND_SOURCE=HERE, NES_WORK=os.path.join(T, "work"),
           PATH=os.path.dirname(sys.executable) + os.pathsep + os.environ.get("PATH", ""))
if A.site: ENV["NES_STUDIO_SITE"] = A.site
ENV.pop("CCS_HOST_TOKEN", None)
INSTALLED = os.path.isfile(os.path.join(os.path.dirname(os.path.abspath(A.launcher)), "ClassicCartStudioBackend.exe"))   # the launcher's own backend beside it
if INSTALLED: ENV.pop("CCS_DEV", None); ENV.pop("CCS_BACKEND_SOURCE", None)
U = ctypes.windll.user32
def windows_of(pid):
    out = []
    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(h, l):
        p = wt.DWORD(); U.GetWindowThreadProcessId(h, ctypes.byref(p))
        if p.value == pid and U.IsWindowVisible(h):
            n = U.GetWindowTextLengthW(h); b = ctypes.create_unicode_buffer(n + 1); U.GetWindowTextW(h, b, n + 1); out.append((h, b.value))
        return True
    U.EnumWindows(cb, 0); return out
def players():
    r = subprocess.run(["tasklist", "/FI", "IMAGENAME eq " + os.path.basename(A.player), "/FO", "CSV", "/NH"], capture_output=True, text=True)
    return [int(l.split('","')[1]) for l in r.stdout.splitlines() if l.startswith('"')]
def launch(*args): return subprocess.Popen([A.launcher] + list(args), env=ENV)
def wait_player(timeout=60):
    t0 = time.time()
    while time.time() - t0 < timeout:
        for pid in players():
            w = windows_of(pid)
            if w: return pid, w
        time.sleep(0.25)
    return None, []
def sessions(): d = os.path.join(DATA, "play"); return sorted(os.listdir(d)) if os.path.isdir(d) else []
def close_player(pid):
    """the window's close button (WM_CLOSE), again every 2 s until the player has ended (a window still starting up may not take it yet)"""
    t0 = time.time()
    while pid in players() and time.time() - t0 < 30:
        for h, _ in windows_of(pid): U.PostMessageW(h, 0x0010, 0, 0)
        for _ in range(8):
            if pid not in players(): return
            time.sleep(0.25)
try:
    if players(): raise SystemExit("a game player is already running on this PC; close it first (this test never touches it)")
    rom1 = game("one"); open(ROM, "wb").write(rom1); before = sha(open(ROM, "rb").read())
    # ---- play
    p = launch("--play", ROM); pid, wins = wait_player()
    ok("play_opens_only_the_player_window", pid is not None and not windows_of(p.pid), f"player {pid} {wins}; launcher windows {windows_of(p.pid)}")
    ss = [s for s in sessions() if s.startswith("session-")]; sess = json.load(open(os.path.join(DATA, "play", ss[0], "session.json"), encoding="utf-8")) if ss else {}
    g = (sess.get("games") or [{}])[0]
    ok("play_direct_mode_no_home", sess.get("mode") == "direct" and not (sess.get("home") or {}).get("pack") and g.get("content_sha256") == sha(rom1[16:]) and g.get("header_hex") == "", sess)
    ok("play_runs_a_copy_this_run_owns", os.path.isfile(g.get("rom", "")) and os.path.normcase(os.path.dirname(os.path.dirname(g["rom"]))) == os.path.normcase(os.path.join(DATA, "play")) and open(g["rom"], "rb").read() == rom1, g.get("rom"))
    ok("play_saves_are_the_contents_own_in_the_data_folder", os.path.normcase(sess.get("saves_root", "")) == os.path.normcase(os.path.join(DATA, "saves")), sess.get("saves_root"))
    ok("play_title_is_the_file_name", g.get("title") == "Test Game", g.get("title"))
    # ---- busy, while the first is open
    t0 = time.time(); q = subprocess.run([A.launcher, "--play", ROM, "--quiet"], env=ENV); took = time.time() - t0
    ok("busy_second_play_ends_with_4_at_once", q.returncode == 4 and took < 30, (q.returncode, took))
    ok("busy_the_open_game_is_left_as_it_is", players() == [pid] and p.poll() is None)
    close_player(pid); rc = p.wait(60)
    ok("play_close_ends_with_0", rc == 0, rc)
    ok("play_file_unchanged", sha(open(ROM, "rb").read()) == before)
    ok("play_nothing_added_to_the_library", not os.path.exists(os.path.join(DATA, "personal", "index.json")) and not os.path.exists(os.path.join(DATA, "personal", "library-store.json")))
    ok("play_runs_folders_removed", sessions() == [], sessions())
    ok("play_saves_folder_for_the_content", os.path.isdir(os.path.join(DATA, "saves", sha(rom1[16:]))), os.listdir(os.path.join(DATA, "saves")) if os.path.isdir(os.path.join(DATA, "saves")) else None)
    # ---- rebuilt under the same name: the new bytes
    rom2 = game("two"); open(ROM, "wb").write(rom2)
    p = launch("--play", ROM); pid, wins = wait_player()
    ss = [s for s in sessions() if s.startswith("session-")]; g2 = (json.load(open(os.path.join(DATA, "play", ss[0], "session.json"), encoding="utf-8")).get("games") or [{}])[0] if ss else {}
    ok("rebuilt_file_plays_its_new_bytes", g2.get("content_sha256") == sha(rom2[16:]) and open(g2["rom"], "rb").read() == rom2)
    close_player(pid); ok("rebuilt_close_ends_with_0", p.wait(60) == 0)
    # ---- a running Classic Cart Studio (its single-instance lock and link pipe): --play neither uses nor contacts it
    K = ctypes.windll.kernel32; K.CreateMutexW.restype = wt.HANDLE; K.CreateNamedPipeW.restype = wt.HANDLE
    mx = K.CreateMutexW(None, True, "Local\\ClassicCartStudio.Main"); real = K.GetLastError() == 183   # ERROR_ALREADY_EXISTS: a real Classic Cart Studio is open
    if real:   # the real one: never touched; --play must not bring its window forward
        U.GetForegroundWindow.restype = wt.HWND
        def fg_pid():
            q = wt.DWORD(); U.GetWindowThreadProcessId(U.GetForegroundWindow(), ctypes.byref(q)); return q.value
        before = fg_pid(); p = launch("--play", ROM); pid, wins = wait_player()
        ok("studio_open_play_still_opens_the_player", pid is not None)
        studio_fg = fg_pid() not in (before, pid, p.pid, 0) and "ClassicCartStudio" in subprocess.run(["tasklist", "/FI", f"PID eq {fg_pid()}", "/FO", "CSV", "/NH"], capture_output=True, text=True).stdout
        close_player(pid); rc = p.wait(60)
        ok("studio_open_its_window_not_brought_forward", rc == 0 and not studio_fg, (rc, studio_fg))
        K.CloseHandle(mx)
    else:   # a stand-in: a Classic Cart Studio of this data folder, its locks held and its link pipe listening (Program.cs: one window per data folder)
        got = []; rid = hashlib.sha256(os.path.abspath(DATA).rstrip("\\/").upper().encode("utf-8")).digest()[:8].hex()
        mx2 = K.CreateMutexW(None, True, "Local\\ClassicCartStudio.Main." + rid)
        PIPE = K.CreateNamedPipeW("\\\\.\\pipe\\ClassicCartStudio.ipc." + rid, 0x00000001, 0, 1, 8192, 8192, 0, None)   # PIPE_ACCESS_INBOUND
        def listen():
            if K.ConnectNamedPipe(PIPE, None) or K.GetLastError() == 535: got.append("connected")
        th = threading.Thread(target=listen, daemon=True); th.start()
        p = launch("--play", ROM); pid, wins = wait_player()
        ok("studio_open_play_still_opens_the_player", pid is not None)
        close_player(pid); rc = p.wait(60); time.sleep(0.5)
        ok("studio_open_nothing_sent_to_studio", rc == 0 and got == [], (rc, got))
        # the listener still waits in ConnectNamedPipe: connect once so it returns (closing the pipe under it would wait forever)
        K.CreateFileW.restype = wt.HANDLE
        c = K.CreateFileW("\\\\.\\pipe\\ClassicCartStudio.ipc." + rid, 0x40000000, 0, None, 3, 0, None)   # GENERIC_WRITE, OPEN_EXISTING
        th.join(10)
        if c and c != wt.HANDLE(-1).value: K.CloseHandle(c)
        K.CloseHandle(PIPE); K.ReleaseMutex(mx2); K.CloseHandle(mx2); K.ReleaseMutex(mx); K.CloseHandle(mx)
    # ---- refusals (--quiet: no message window)
    ok("missing_file_3", subprocess.run([A.launcher, "--play", os.path.join(FOLDER, "not here.nes"), "--quiet"], env=ENV).returncode == 3)
    ok("usage_2", subprocess.run([A.launcher, "--play", "--quiet"], env=ENV).returncode == 2)
    unknown = os.path.join(FOLDER, "headerless unknown.unh"); open(unknown, "wb").write(rom1[16:])
    ok("headerless_unknown_3", subprocess.run([A.launcher, "--play", unknown, "--quiet"], env=ENV).returncode == 3)
    if not INSTALLED:   # a launcher built in a checkout: its player is the one given here, so a missing one can be shown
        env5 = dict(ENV); env5["CCS_PLAYER_EXE"] = os.path.join(T, "no player.exe")
        ok("no_player_5", subprocess.run([A.launcher, "--play", ROM, "--quiet"], env=env5).returncode == 5)
    else: print("no_player_5=SKIPPED (an installed layout always has its own player; not a pass)", flush=True)
    rel = subprocess.run([A.launcher, "--play", os.path.join("Ünïcödé gämes ✓", "not here either.nes"), "--quiet"], env=ENV, cwd=os.path.dirname(FOLDER)).returncode
    ok("relative_path_from_the_command_folder", rel == 3)
    ok("refusals_leave_no_run_folders", sessions() == [], sessions())
finally:
    for pid in players():
        if pid: subprocess.run(["taskkill", "/PID", str(pid), "/F"], capture_output=True)
    shutil.rmtree(T, ignore_errors=True)
n = sum(1 for _, v in checks if v); print(f"RESULT: {'PASS' if n == len(checks) else 'FAIL'} ({n}/{len(checks)} checks)")
sys.exit(0 if n == len(checks) else 1)
