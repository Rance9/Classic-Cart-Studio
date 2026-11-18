# CCS Console Workbench

> **Unsigned development preview:** console connections, console writes and driver actions are disabled. PC/offline features are available; real-console and Windows on Arm qualification remain incomplete.

**Back up, restore, repair and compose your NES Classic.**

CCS Console Workbench is a Windows program for the NES Classic Edition. It runs from one file without installing the application. The
same Workbench is built into Classic Cart Studio for Windows. This branch holds its developer-facing information and release files. Its
engine source lives in `tools/console-backup/` on `main` at the commit named in `ENGINE-PIN.txt`.

## What it does

| Section | What it does |
|---|---|
| Console Backup | a complete, checked backup of the console's built-in storage, one ZIP file per backup |
| Console Restore | a backup put back on the same console; the way back from Classic Cart Studio's preparation |
| Console Rescue | the console's own boot0/boot1 written back from one of its backups, when one is damaged |
| Console Composer | setting up a console from your own backups and files; HOME Designer |
| Console Browser | a check-up of the console and its files, read in a session that runs in the console's memory; Expert Mode |
| Backup Browser / Backup Repair | backup files by game, by type or as stored; checking, copying and repairing them without a console |
| Developer Mode | changing files anywhere on the console's data storage, under a restore point |

Console sessions use the console's own recovery mode (USB, RESET held at power-on). Each write has its own boundary in
`tools/console-backup/CcsBackup/Gate.cs`. Writes to a real console are turned on only after each kind has been checked on real hardware.

## Building

1. Check out `main` at the commit in `ENGINE-PIN.txt`. Use the .NET SDK and zig versions recorded there.
2. Build the console boot-stage tool: `tools/console-backup/ram/ccsboot/build.sh OUT` (zig, static armv7).
3. Build the recovery images from the pinned Hakchi2 CE 3.9.3 release:
   `python tools/console-backup/ram/build_images.py --hakchi-release <folder or portable ZIP> --ccsboot OUT/ccsboot --out <new folder>`.
4. Build the standalone: `dotnet build tools/console-backup/ccs-backup-utility -c Release -p:CcsRamImages=<images folder>`.

Pinned upstream binary inputs are checked by SHA-256; the engine pin records the exact source commit, tree and archive hash. The release workflow builds the native single-file host and attaches available source archives.

## Formats

- Console backup ZIP: `tools/console-backup/FORMAT.md`

## Credits and licenses

Classic Cart Studio's code in CCS Console Workbench is GPL-3.0-or-later; parts adapted from Hakchi2 CE, FelLib and hakchi's `sntool`
(GPL-3.0) keep those projects' terms. It builds its console programs from Hakchi2 CE 3.9.3's prebuilt hakchi package, whose programs (the
Linux kernel and its modules, u-boot, fes1, sntool, BusyBox and others) they carry unmodified under their own licenses. Credits: hakchi by
madmonkey; hakchi2 by ClusterM; Hakchi2 CE by Team Shinkansen and contributors. `ccsboot` is derived from hakchi's `sntool` source.
`tools/console-backup/NOTICE` lists every component with its license, its notices and the license texts. Each release attaches the source
of Classic Cart Studio's code with its build files, and the sources of the PAR2 and LibUsbDotNet libraries the program contains.
Independent; not affiliated with Nintendo. It does not include or offer Nintendo games or keys; the u-boot its console programs carry
includes the board configuration block hakchi's build takes from the NES Classic's stock u-boot (`NOTICE`).

## Upstream source gaps

Pinned upstream binaries remain included with unresolved source/redistribution questions. The component-by-component disclosure is [SOURCE-AUDIT.md](tools/console-backup/SOURCE-AUDIT.md) at the engine source commit in ENGINE-PIN.txt. Available source archives are not a claim of complete corresponding source.
