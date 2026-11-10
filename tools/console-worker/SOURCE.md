# Corresponding source and build: ccs-console-worker

## Upstream, exactly

| What | Where | Identity |
|---|---|---|
| Hakchi2 CE | https://github.com/TeamShinkansen/Hakchi2-CE | tag `v3.9.3`, commit `25bfcfd04b8ed12f7d513dc87c2a0f7096d38ebc` (GPL-3.0) |
| SSH.NET (TeamShinkansen fork) | Hakchi2 CE submodule `Libraries/SSH.NET` | commit `32181aeb4ce69e8f07e5e1d585fd592a44017bc1` (MIT) |
| LibUsbDotNet | NuGet `LibUsbDotNet` 2.2.29 (`lib/net45/LibUsbDotNet.LibUsbDotNet.dll`) | SHA-256 `b6de87ff6fdde6d0a245ef37fe81fac49523c35115c61bc002e4935d99875508`, byte-identical to the copy Hakchi2 CE 3.9.3 embeds (LGPL-3.0); its source is Hakchi2 CE submodule `Libraries/LibUsbDotNet`, commit `91e84390acd83ded475798488740961da9e3307c` |

Get the pinned tree with `get-hakchi-source.ps1 -Dest <new folder>`: it clones tag `v3.9.3` with its eight submodules at the commits
the tag records and verifies every commit and, byte for byte, every upstream file listed in `hakchi-inputs.sha256` (what the worker
compiles, links or derives from, their sources and licenses). Point `HakchiSource` at it. The same files, with this folder's source,
are in `console-worker-source.zip`, which accompanies every Classic Cart Studio release that contains the worker.

## What is derived, and how

| This file | From (Hakchi2 CE 3.9.3) | Changes |
|---|---|---|
| `CcsWorker/Shell/IConsoleShell.cs` | `ISystemShell.cs` | Only Execute (streams, timeout), IsOnline, Disconnect, Kind; typed timeout / connection-lost failures. |
| `CcsWorker/Shell/ClovershellShell.cs` | `Clovershell/ClovershellConnection.cs`, `Clovershell/ExecConnection.cs` | Same frame format, command codes, kill-all and drain at connect, PING/PONG, exec matched by command text, 8 KiB stdin frames with the flow-status back-off, empty frame as end of input, EXEC_RESULT. The USB device is behind `IBulkPipe`; a byte-stream packet parser replaces LibUsbDotNet's DataReceived event; no telnet shell listener and no background reconnect; a silent command and a silent console are told apart by a ping; a timed-out command is killed (EXEC_KILL). |
| `CcsWorker/Shell/LibUsbBulkPipe.cs` | `Clovershell/ClovershellConnection.cs` (`mainThreadLoop`, `writeUsb`) | Device search by VID 0x1F3A / PID 0xEFE8, configuration 1, interface 0, endpoints 0x81/0x01, 64 KiB reader, write with up to ten retries. Built only by `RealTransports` (closed by Gate G1). |
| `CcsWorker/Shell/SshShell.cs`, `SshNetSession.cs` | `SshClient/SshClientWrapper.cs` | One SSH command per call with output streams, input on the same channel, command timeout, exit status; root with an empty password on port 22. Session behind `ISshSession`; no mDNS/DNS listeners, no reconnect thread; the input is ended with the channel's EOF. Built only by `RealTransports` (closed by Gate G1). |
| `CcsWorker/Ops/ConsoleOps.cs` (`Identify`, `Version`) | `hakchi.cs` (`Shell_OnConnected`, `ConvertVersion`, `SystemRequiresReflash`, `SystemRequiresRootfsUpdate`) | The same commands in the same order, the same minimum versions and CanInteract rule; the running kernel from `/proc/sys/kernel/osrelease` (the stock BusyBox has no `uname`). |
| `Renci.SshNet/Renci.SshNet.csproj` | `Libraries/SSH.NET/src/Renci.SshNet/Renci.SshNet.csproj` | The same Compile list and Release `DefineConstants`, in an SDK-style net48 project that compiles the pinned files in place. No source file is copied or changed. |

Everything else (`Gate.cs`, `RealTransports.cs`, `IBulkPipe.cs`, `ConsolePath.cs`, the other operations, `Hmod.cs`, `WorkerServer.cs`,
the program and the tests) is new: Copyright (C) 2026 Rance9, GPL-3.0-or-later.

## Build

    powershell -File tools\console-worker\build-worker.ps1 -HakchiSource <pinned tree> -Out <new folder>

It verifies the tree, builds the worker and its tests (Release, the .NET SDK in `global.json`), runs the offline tests, builds the
worker again in a second folder and requires the same bytes, then writes the release folder: `ccs-console-worker.exe`,
`ccs-console-worker.exe.config`, `CcsWorker.dll`, `Renci.SshNet.dll`, `LibUsbDotNet.LibUsbDotNet.dll`, `NOTICE`, this file, `licenses/`,
`BUILD-INFO.json` (every shipped file with its size and SHA-256, the upstream commits and the toolchain) and
`console-worker-source.zip`. Release builds have no debug information and do not record the git checkout they were built in, so the
same source gives the same bytes in any folder; each release's `BUILD-INFO.json` names its files' SHA-256.

Distribution with the program: `console-worker-source.zip` (this folder's source and the upstream files), `NOTICE`, `licenses/` (GPL-3.0, SSH.NET's MIT notice, LibUsbDotNet's LGPL notice) and
this file. Nothing proprietary is in the source or the build: no Nintendo files, firmware, root filesystem, NAND image, ROM or BIOS.
