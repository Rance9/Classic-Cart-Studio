# Console Simulator: architecture decision record

Status: implemented 2026-10-12. Scope: development and emulation infrastructure only. Gate G1 (physical console
operations) is NOT approved and nothing here depends on it or opens it.

## 1. Decisions

| Topic | Decision | Why |
| --- | --- | --- |
| Shared core language | C11, no dependencies beyond libc | Cross-compiles to the 32-bit ARM target with a static musl build, tiny footprint for a 256 MB device class, trivially testable through a line protocol, no runtime to port later |
| Host harness | tkinter viewer in the Windows Python already installed, driving the native Windows build of the same core | No Electron, no browser, no second frontend; the harness is a viewer and an input adapter, nothing more |
| Linux x86-64 build | gcc inside the existing WSL Ubuntu | Already present; serves as the second host test runner |
| ARM cross build | zig cc (`arm-linux-musleabihf`, `-mcpu=cortex_a7`, static) plus an optional `arm-linux-gnueabihf.2.31` dynamic build | One pinned, SHA-256 verified archive gives a complete cross toolchain without root; the dynamic variant exercises the ARM runtime-library failure path |
| ARM runtime | `qemu-arm` user mode (Ubuntu's qemu-user package, extracted into a user prefix) | Proves real ARMv7 code executes; `qemu-system-arm` (generic `virt` guest) is optional and was not built: user mode fully demonstrates the ARM binary and the system board would still not be an NES Classic |
| Toolchain install | `apt-get download` + `dpkg-deb -x` and a tarball into `~/.cache/ccs-sim/toolchain`; nothing system-wide | The owner session is non-elevated and corporate-managed; the simulator must not need sudo, PATH changes or Windows features |
| Process boundaries | The core never spawns processes, opens sockets or reads anything but the fixture; the platform adapter (Python) launches the dummy game stub and the supervisor runs outside the launcher | A fallback that depends on the broken launcher would be meaningless; a core with no process or network ability cannot reach a device by construction |
| Data contract | `ccs-sim-fixture/1`, a projection of the Studio's `nes-classic-collection/4` manifest with the same rules as Preview on TV | One collection model; the simulator consumes a projection, it does not invent a second list of names |
| Rendering | Software RGB framebuffer 1280 x 720, PPM output, 5x7 font; a test pattern with labels | Deterministic, hashable, portable; the real device display stack is unknown and not promised |
| Time | Simulation clock advanced only by `tick` | Deterministic repeat timing and replay on every platform |
| Where generated files go | `C:\NES\work\console-simulator\{build,artifacts}` and the WSL toolchain prefix | Outside OneDrive and outside the repository; one `clean` removes them |

## 2. Components

```
                 Windows                               WSL (Ubuntu 26.04)
 +----------------------------+        +--------------------------------------------+
 | viewer.py (tkinter)        |        | gcc  -> build/linux-x86_64/ccs-sim-core     |
 |   keyboard / XInput        |        | zig  -> build/windows-x86_64/ccs-sim-core.exe|
 |   frames (PPM -> PNG)      |        | zig  -> build/arm-musl/ccs-sim-core (static) |
 | supervisor.py (watchdog)   |        | zig  -> build/arm-glibc/ccs-sim-core (dyn)   |
 | tests/ (unittest)          |        | qemu-arm  runs the ARM cores                 |
 +------------+---------------+        +---------------------+----------------------+
              |  runner.py: path = windows | linux | arm      |
              +----------------+-----------------------------+
                               v
                    ccs-sim-core (stdin commands, stdout JSON events)
                    model.c  input.c  fb.c  main.c (lifecycle, fake console services, faults, game-stub mode)
```

### Neutral contracts (the surface a future design attaches to)

| Prompt name | Here | Notes |
| --- | --- | --- |
| LauncherModel | `model` in `ccs_sim.h` (`entry`: id, version_id, title, sort_key, group, art_ref, source_hash, kind, ready, order, visible; `menu[]` deterministic order; `rejected[]`) | Identity and exact version are required; hidden factory titles stay in the model but out of the menu |
| InputAction | `button` (up, down, left, right, a, b, start, select) and `in_kind` (press, release, repeat, duplicate, forced-release, rejected) | Mapping lives in adapters; repeat timing is a simulator setting |
| LauncherState | `lstate` (boot, ready, launching, suspended, shutdown) plus the diagnostic linear cursor | The cursor is scaffolding, not a layout |
| ConsoleServices | inside `main.c`: ready/stats reports labeled `simulation: true`, launch request event, resume/failed commands, faults | Fake by construction; the request/response shape is NOT a claim about Clover MCP |
| Renderer | `framebuffer` + `render()` in `main.c` | Replace `render()` entirely for the real design; nothing else references its layout |
| SimulationClock | `input_state.clock_ms`, advanced by `tick` | Wall time is never read by the core |
| PlatformAdapter | `host/runner.py` (execution paths, stub launch), `host/viewer.py` (keyboard, XInput), `scripts/*.sh` | The future device adapter is a separate, unapproved task |

## 3. What is proven (and the evidence)

- The same C sources build and pass the same suites on Windows (x86-64), x86-64 Linux and 32-bit ARM under qemu-arm. The
  ARM-versus-Windows test replays one script on both and asserts identical event streams (`tests/test_arm.py`).
- The ARM binary is `ELF32, ARM, EABI5, hard-float, Cortex-A7, VFPv4`, statically linked (musl); the optional glibc build
  carries `/lib/ld-linux-armhf.so.3` and `libc.so.6` and fails understandably without its runtime
  (`artifacts/arm-smoke/inspect.txt`, `summary.json`).
- Input semantics (press, release, repeat at 400/100 ms, duplicate suppression, focus loss, disconnect) are deterministic
  and replayable; rapid bursts of 2,000 presses complete in a few seconds.
- Lifecycle: launch request, dummy stub, resume, stub failure, stub timeout, repeated cycles, clean shutdown and
  stdin-closed shutdown.
- Supervision outside the launcher: startup failure, startup hang with timeout, crash mid-operation with clean restart,
  crash-loop detection with fallback to the labeled stock-launcher stub, simulated power loss with recovery, malformed
  data recovery, failed game stub handled.
- Display: 1280 x 720 frames, valid images, deterministic content, state changes visible, render failure reported.
- Isolation: no sockets, no process execution in the core, fixture commands rejected, device endpoints refused by the
  adapter, production folders unchanged by runs, generated files only in designated directories, clean removes only them.

## 4. What stays mock and must be replaced for real hardware

| Mock today | Real counterpart (future, unapproved) |
| --- | --- |
| `--game-stub` dummy process | the emulator launch command on the device (unknown invocation and lifecycle) |
| ready/stats "simulation" reports, storage never claimed | Clover/MCP system information (undocumented; needs capture on hardware) |
| `launch_requested` -> adapter runs the stub | the real launch path (process model, input release, video handover) |
| supervisor.py restart / fallback | a boot-time supervisor on the device (init integration, bind mounts, Clover fallback): a dedicated hardware phase |
| software framebuffer | the device's display stack (Mali-400, HDMI, framebuffer or DRM availability unknown) |
| keyboard / XInput mapping | the console controller's Linux input events (codes unverified) |
| `--mem-budget-kb` self-imposed budget | the real 256 MB system with Clover and the kernel resident |

Not emulated at all: the Allwinner R16 SoC, NAND layout, FEL and USB, the bootloader, the recovery kernel, save states,
and Nintendo's software. QEMU user mode runs our instructions; it does not run the console.

## 5. QEMU user mode versus system mode

User mode (`qemu-arm`) executes an ARM Linux process on the x86-64 host kernel: it is enough to prove the compiled code
is ARM and behaves identically, it starts in milliseconds and it needs no kernel or root filesystem. System mode
(`qemu-system-arm -M virt`) would boot a generic ARM kernel and root filesystem with bounded RAM; it would still not be
an NES Classic board. It was left optional: the acceptance criteria are met by user mode, and a `virt` guest would add
image provenance and maintenance without adding confidence about the device.

## 6. Adding the future approved design

1. Keep `ccs-sim-fixture/1` (or add `/2` with an adapter) as the data contract; do not add a parallel list.
2. Implement the design as a new renderer and navigation module that consume `model`, `input` events and the lifecycle
   state; replace `render()` and the diagnostic cursor in `main.c` (they are the only layout-bearing code).
3. Keep the protocol events: the tests, the supervisor and the viewer continue to work unchanged.
4. Build with `sim.ps1 build`, run `sim.ps1 test --path arm` to confirm the design logic still runs as ARM code.
5. Hardware questions to settle before any device test: display API, input event codes, launch command and lifecycle,
   boot integration and fallback, memory available next to Clover. Each is a separate approved step; none is implied by
   simulator results.

## 7. Confidence matrix

| Behavior | Simulator | Hardware |
| --- | --- | --- |
| model parsing, identities, ordering | proven (3 platforms) | same code; file system and paths unverified |
| input semantics | proven | controller event codes, timing and focus model unverified |
| lifecycle and supervision | proven (mock stub, external supervisor) | launch path, Clover handover, boot supervision unverified |
| rendering | proven (software frames) | display stack unverified |
| memory and timing | software budget estimates only | not measured |
| ARM ABI | ELF32 ARMv7 hard-float, static musl runs; glibc variant runs with its runtime | device libc version and kernel ABI unverified |
