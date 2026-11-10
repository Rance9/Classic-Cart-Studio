# Classic Cart Studio console transport worker

Copyright (C) 2026 Rance9. GPL-3.0-or-later. Parts are derived from Hakchi2 CE 3.9.3 (GPL-3.0). See `NOTICE` and `SOURCE.md`.

`ccs-console-worker.exe` is the separate program Classic Cart Studio would use to talk to an NES Classic that runs hakchi: the clovershell
USB shell and the SSH shell from Hakchi2 CE, and the operations K3 needs (identification, free space, stat/list/hash/read (whole small files, or any file in bounded pieces), a guarded
write with readback, folders, rename, owned-file removal, the version pointer (optionally only from the state it expects), sync, and hakchi's own module install/uninstall). It speaks the
worker protocol (version 1, newline-delimited JSON on stdin/stdout) to the Classic Cart Studio backend, which starts it as a child process.

**The physical tiers are closed in code (Gate G1).** `Gate.PhysicalInspectEnabled` and `Gate.PhysicalWriteEnabled` are `false` compile-time
constants; `connect` always answers `physical-disabled`, before any USB enumeration, device handle, socket or discovery; with the constants
false the compiler leaves the device-opening code out of the program. There is no option, setting, environment variable or second
executable that changes this. Opening a physical tier is a reviewed code change released as its own owner-approved milestone.

| Project | What it is |
|---|---|
| `CcsWorker` | The library: `Gate`, `Shell/` (clovershell and SSH shells, the real USB pipe and SSH session that only `RealTransports` may build), `Ops/` (console paths, the operations, the module archive), `Protocol/WorkerServer`. |
| `ccs-console-worker` | The process: `ccs-console-worker [--package FOLDER]`. FOLDER is the console package the backend staged; `put` and `module-install` read their `source` files only inside it. |
| `Renci.SshNet` | SSH.NET compiled in place from the pinned Hakchi2 CE 3.9.3 submodule (no copy, no change). |
| `CcsWorker.Tests` | Offline tests: gate, protocol, clovershell framing on a simulated device, the SSH adapter on a fake session, the operations on scripted shells, and fidelity runs on the console's own BusyBox under qemu-arm. |

## Build

Needs Windows, the .NET SDK named in `global.json` (with the .NET Framework 4.8 reference assemblies), NuGet (LibUsbDotNet 2.2.29, checked
by its SHA-256) and the pinned Hakchi2 CE 3.9.3 source tree with its submodules (`SOURCE.md`). There is no default location for either:

    powershell -File tools\console-worker\get-hakchi-source.ps1 -Dest <new folder>
    powershell -File tools\console-worker\build-worker.ps1 -HakchiSource <that folder> -Out <new folder>

`get-hakchi-source.ps1` clones tag v3.9.3 with its eight submodules and checks every commit and, byte for byte, every upstream file the
worker compiles, links or derives from (`hakchi-inputs.sha256`). `build-worker.ps1` builds the worker and its tests, runs the offline
tests, builds the worker again in a second folder and requires the same bytes, then writes the release folder: the five program files,
`NOTICE`, `SOURCE.md`, `licenses\`, `BUILD-INFO.json` (every shipped file with its size and SHA-256, the upstream commits, the toolchain)
and `console-worker-source.zip` (the corresponding source). The Classic Cart Studio release build requires that folder
(`CCS_CONSOLE_WORKER_DIR`; `tools\companion\companion.spec` refuses to build without it or with any file that differs from its
`BUILD-INFO.json`). For development, `dotnet build ... -c Release -p:HakchiSource=<tree>` builds into the repository's ignored
`build\console-worker\` (property `CcsBuildRoot`).

Release builds carry no debug information, so the same source gives the same bytes in any build folder.

## Tests

`CcsWorker.Tests` prints one line per test and `RESULT passed/total`; exit 0 only when every test ran and passed (2 when some were
skipped: a skip is never a pass). `--only gate,protocol,clovershell,ssh,ops,fidelity` runs some categories.

The fidelity tests need WSL (Ubuntu) with the project's qemu-arm (`~/.cache/ccs-sim/toolchain`) and the extracted console files on
the development machine (stock root filesystem; hakchi's initramfs; `fidelity/setup.sh`; never distributed). They run each operation's real shell text with the console's stock BusyBox
1.24.1, and hakchi's own `pack_install`/`pack_uninstall` (from the pinned `hakchi.hmod`) with hakchi's BusyBox 1.29 and rsync, inside a
disposable WSL folder that is removed afterwards. QEMU is not a console: this shows what the console's tools do with the text, not the
console's storage, timing or USB/network transport.

`CcsWorker.Tests --serve-busybox [--package DIR]` (test only) serves the protocol with such a disposable folder already attached, for the
backend's own integration tests. `connect` stays closed there too.

## What the console's tools taught the design

The console's stock BusyBox 1.24.1 `mv` has no `-T`, and `mv a b` moves `a` into `b` when `b` resolves to a folder (a link to a folder
included). So no operation renames onto an existing path, the version in use is named by a small regular file (`pointer`, replaced by
`mv -f` after checking in the same command that it is a regular file), and every write checks that each folder on the way from
`/var/lib/ccs` (or `/tmp/ccs`) is a real folder, not a link. A write never replaces anything: `set -C` and an existence check refuse a
file, a link or a dangling link at the target. Removal is by expected SHA-256 only and never follows a link.
