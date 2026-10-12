# Classic Cart Studio Console Simulator

A developer testbed for a **future** custom NES Classic HOME launcher. It lets you build, run, drive, break and
regression-test launcher logic on a Windows 11 workstation, and it proves that the same logic runs as real 32-bit ARM Linux
code under QEMU, without touching a console.

**What it is not.** It is not an NES Classic emulator, it does not boot Nintendo software, it contains no HOME screen
design, and it never talks to a physical console (Gate G1 is not approved; nothing here can communicate with, flash or
configure a device). The only picture it draws is a labeled diagnostic test pattern.

## Prerequisites (this workstation, already present)

| Side | Needed | Found |
| --- | --- | --- |
| Windows | Python 3 with tkinter and Pillow | Python 3.13, tkinter 8.6, Pillow 12 |
| Windows | WSL 2 with a Linux distribution (for the ARM track) | WSL 2.7, Ubuntu 26.04, WSLg |
| WSL | gcc, make, binutils, file, python3, apt (download only) | all present |
| WSL | zig 0.17.0 and qemu-arm 10.2.1 | installed by `sim.ps1 setup` into `~/.cache/ccs-sim/toolchain` (no root) |

If WSL is missing, `sim.ps1 setup` stops and prints the one owner action (`wsl --install -d Ubuntu`, elevated, may need a
restart). The Windows harness and the Windows core still work without WSL; the ARM track needs it.

## Everyday commands (PowerShell 7, from this folder)

```powershell
.\sim.ps1 doctor                      # non-destructive environment report (Windows + WSL)
.\sim.ps1 setup                       # zig (SHA-256 verified) + qemu-arm + armhf libc into a user prefix inside WSL
.\sim.ps1 build                       # all four cores: windows-x86_64, linux-x86_64, arm-musl, arm-glibc
.\sim.ps1 run-simulator               # interactive diagnostic viewer (keyboard, XInput gamepad if connected)
.\sim.ps1 run-simulator --script fixtures\scripts\demo.txt --out C:\NES\work\console-simulator\artifacts\demo   # headless replay
.\sim.ps1 run-arm-smoke               # the real ARM binary under qemu-arm: inspection, scripted session, stub, failures
.\sim.ps1 test                        # every suite on the Windows build; add --path arm or --path linux
.\sim.ps1 test-faults                 # fault injection + supervisor scenarios only
.\sim.ps1 supervise --scenario crash-loop
.\sim.ps1 clean --artifacts | --builds | --toolchain | --all
```

Generated files live only under `C:\NES\work\console-simulator\{build,artifacts}` and, inside WSL,
`~/.cache/ccs-sim/toolchain`. Nothing is written into this source tree, the Studio's data folder, the Hakchi tree or
any ROM location.

## Layout

```
core/        the shared launcher core, C11: json_min.c model.c input.c fb.c main.c ccs_sim.h  (one source, four builds)
host/        runner.py (platform adapter: windows | linux | arm execution paths; launches the dummy game stub)
             supervisor.py (external watchdog: restart, crash-loop detection, mock fallback, power loss, malformed data)
             viewer.py (tkinter diagnostic viewer; keyboard, XInput, screenshots, scripted replay)
adapter/     ccs_projection.py: Classic Cart Studio manifest (nes-classic-collection/4) -> ccs-sim-fixture/1
fixtures/    make_fixtures.py and the generated synthetic sets: factory-only, mixed, stress-5000, malformed, not-json, wrong-schema
scripts/     wsl_setup.sh wsl_doctor.sh wsl_build.sh wsl_arm_smoke.sh (run inside WSL by sim.ps1)
tests/       run_tests.py + test_core.py test_adapter.py test_supervisor.py test_arm.py test_env.py
toolchain.lock.json   pinned versions, URLs, SHA-256 of the zig archive, package versions
ARCHITECTURE.md       decision record, contracts, what stays mock, how the future design plugs in
```

## The protocol in one minute

The core is a process. You write commands to its stdin, it answers with JSON events on stdout. The same binary is the
launcher and (with `--game-stub`) the dummy game.

```
load <fixture.json>      -> fixture_loaded | fixture_rejected (+ record_rejected per bad record)
input press|release <up|down|left|right|a|b|start|select>   -> input {press|release|repeat|duplicate|forced-release|rejected}
tick <ms>                -> advances the simulation clock; emits repeats deterministically
focus lost|gained        device disconnect|connect           -> all buttons force-released
frame <out.ppm>          -> frame_written {1280x720, crc32}
launch                   -> launch_requested (then the adapter runs the stub)   game-exit <code> | game-failed <why> -> resumed | launch_failed
stats | shutdown
```

Faults: `--fault=startup-fail | startup-hang | slow-start=<ms> | crash-after-inputs=<n> | crash-on-launch | frame-fail`,
`--mem-budget-kb=<n>` (simulated allocation budget), `--repeat=<delay>,<rate>`.

## Keyboard mapping (simulator only)

Arrows = D-pad, Z = B, X = A, Enter = Start, Shift = Select, F2 screenshot, F5 reload fixture, F8 controller
disconnect/reconnect, F9 request a launch of the cursor entry (dummy stub), Esc quit. Losing window focus releases
everything. These mappings are a convenience of this harness; they say nothing about Clover's input stack or the
console's event codes.

## Feeding it a real collection (developer only)

```powershell
.\sim.ps1 project <exported-collection.json> C:\NES\work\console-simulator\artifacts\my-fixture.json
.\sim.ps1 run-simulator --fixture C:\NES\work\console-simulator\artifacts\my-fixture.json
```

The projection uses the same rules as Preview on TV (kind, hidden factory titles, title order). Readiness is "unknown"
for a website export and "ready/missing" only when the manifest was prepared on this computer. Nothing about the physical
console is ever asserted.

## Cleanup

`.\sim.ps1 clean --all` removes the builds, the artifacts and the WSL toolchain prefix (about 350 MB). Nothing else on the
machine was changed by setup: no PATH edits, no services, no Windows features, no packages installed system-wide.

## Status and limits

See `ARCHITECTURE.md` (sections "What is proven" and "What stays mock") and the engineering report in the private
workspace (`docs/console-simulator-report.md`). Short version: the launcher core, input, lifecycle and supervision are
tested on Windows, on x86-64 Linux and as real ARMv7 code under qemu-arm; the display path is a software framebuffer;
Clover, the Allwinner SoC, the Mali GPU, HDMI, USB, NAND and recovery are not modeled and not claimed.
