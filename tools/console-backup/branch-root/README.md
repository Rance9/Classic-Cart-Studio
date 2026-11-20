# CCS Console Workbench

Current preview: **0.11.5**, aligned with Classic Cart Studio and the shared engine; Classic Cart Studio 0.11.5 installs this same program. Earlier previews (0.11.4, 0.11.3, 0.11.2, 0.11.1, 0.11.0, and 1.0.0, which was not a production release) are superseded and stay available as they were.

> **Unsigned development preview:** console connections, console writes and driver actions are disabled. PC/offline features are available; real-console and Windows on Arm qualification remain incomplete.

**Back up, restore, repair and compose your NES Classic.**

CCS Console Workbench is a Windows program for Nintendo's Classic Mini consoles: the NES Classic Edition, and the Super NES Classic Edition, whose
support is being built (its console tasks are not in this preview). It runs from one file without installing the application. The
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

## Downloads and source

Windows preview downloads and checksums are available at [ClassicCartStudio.com/app/](https://classiccartstudio.com/app/).
The x64 file is the default Windows download. The Arm64 build has not been run on Arm64 hardware.

## Building

`ENGINE-PIN.txt` names the exact public source commit, tree and SHA-256 of its uncompressed git archive.
The release workflow checks all three before building. It builds the console HOME/player, five recovery programs and HOME package,
then the managed engine using the app's VERSION, and the single-file x64 and Arm64 hosts using Workbench's own version.
See [.github/workflows/workbench-release.yml](.github/workflows/workbench-release.yml) for the complete pinned build steps.
A Workbench tag names the product version; it never changes the shared engine's assembly version.

The release includes engine-source.zip and available third-party source archives. Classic Cart Studio for Windows installs this
same single-file program (byte for byte) and starts it as its own program; `tools/console-backup/check_shared_engine.py` checks the
bundled copy against the hosts its build made, and the app's `build-info.json` records the bundled files' hashes.
The pin proves source identity; the hashes prove byte identity for the released pair.

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
