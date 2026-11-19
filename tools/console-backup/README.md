# CCS Console Workbench (formerly Console Backup & Restore)

Copyright (C) 2026 Rance9 (Rance9 LLC). GPL-3.0-or-later. Parts are derived from Hakchi2 CE 3.9.3 and FelLib (GPL-3.0): see `NOTICE`.

One implementation behind two entry points, with the same sections and the same console programs in both:

- **Classic Cart Studio for Windows**: the Console Workbench button on the first screen and the Studio menu open this window (hosted,
  without the standalone's Classic Cart Studio introduction page).
- **CCS Console Workbench** (standalone): one executable, no installer, no AppData (`CCS-Console-Workbench.exe`; earlier versions were
  `CCS-Console-Backup-Restore-Utility.exe`, and backups and records they made are read as before).

The window opens on its Dashboard (the console's status, Back Up Console, the common tasks, recent backups). Sections: Console Backup,
Console Restore, Console Rescue, Console Composer, Console Browser (Expert Mode inside), Backup Browser, Backup Repair, Developer Mode;
then Settings (backup locations), Help and About (the illustrated guide) and Credits. In the standalone, the Classic Cart Studio introduction page (its Download button
opens classiccartstudio.com/app/) is built but its side-column entry is held back until the full app is released
(`BackupWorkspace.FullAppEntryReleased`). The console strip names the console family (`ConsoleProfile`; one family in this version) and
says "Console not checked" until an operation's own session identifies a console. Each worker is its own session (`NewSession`): it
starts unidentified, a console an earlier session identified is shown apart as "Earlier session", and a superseded session's late events
change nothing. The check-up's Condition is the status the file system records in its header, not a complete check.

The console family is **NES Classic Mini — North America / Europe** (`ConsoleProfile.NesClassicMini`; its saved id stays
`nes-classic-na`). About This Console (`WorkbenchAbout.cs`) is one modal window from every entry point, with its own draft console choice:
a different final choice asks once, in one Change console? question (Use Selection / Keep Current / Back to Help, its Use console list
starting at the draft), on every way of closing it, and Use Selection goes through the one guarded change
(`ApplyProfile`: refused while a console task or session is open). A family is never a console: it never identifies, relabels or admits one.
Preferences (`WorkbenchPrefs.cs`, one model, `Settings` in the engine: the backup locations and the console family; a newer version's fields
are kept): the standalone keeps them in memory unless the person saves a settings file of their own; inside Classic Cart Studio an accepted change
is saved at once, a failed save is shown with Try Again. Closing with unsaved work shows one review (`CloseReview`: unsaved settings, a
HOME design, a page's own unsaved work through `LeaveGuard`) with Save & Close / Close Without Saving / Cancel, before the task's safe stop
and the conditional USB driver question. Help (`WorkbenchHelp.cs`) is the guide in `CcsBackupUi/Help/help.txt` with pictures of the
actual pages (`Help/shots/`, sample data; `Help/SHOTS.json`), carried in the library and shown offline in the page (Help and About) or in
the Help window (Help on This Page, F1). Explained words (`WorkbenchGlossary.cs`) open a small panel by click, tap, Enter or Space.
Credits (`WorkbenchCredits.cs`) reads Classic Cart Studio's approved Credits from its own Help (`tools/rom-pipeline/studio/guide.html`,
carried as it is), this program's NOTICE and the CCS HOME package's license files. HOME Designer's starting-theme examples are
`CcsBackupUi/theme-examples/` (a manifest and four pictures; `build_pack.py` and `render.py` made them). The title bar has Minimize,
Maximize/Restore and Close; maximized, the window's content stays inside its monitor's work area (`WindowFrame.cs`).

| Project | What it is |
|---|---|
| `CcsBackup` | The engine: the console backup file (`Archive/`, `FORMAT.md`), the console facts and the typed recovery-session protocol (`Device/`), the data storage reader and the `.ccs` export (`Ext4/`), durable records and the history (`Library/`), the Backup Browser's engine (`Browse/`: the backup list with check dates, By game / By type / All files, search and checked export, also inside the system images a backup holds (SquashFS gzip/xz, `Device/SquashFs.cs`), the console's encrypted system part decrypted read-only with the key the backup's own startup images carry (`Device/XtsView.cs`), each startup image's kernel, ramdisk and ramdisk files (`CcsConsole/RamBoot/StartupFiles.cs`), the nested CCS HOME package and, through Backup Repair's part finder, a backup whose table of contents is damaged; each file with where its checksum comes from; read-only, no console involved), capture, verification, restore, preparation, Return to Original Software (after Classic Cart Studio's own preparation), the maintenance-write admission, the console lock and the controlled worker (`Engine/`), backup repair (the repair map, rebuilding a damaged table of contents, second copies and merging damaged copies, repair from other backups of the same console, salvage (also file by file from a damaged data storage image, shared with the Backup Browser), supplying a damaged or missing startup software part from a file accepted only when it is byte-for-byte that part as recorded (`KernelSupply`), and the repair report: `Repair/`), and the physical boundaries (`Gate.cs`: closed in this version). It also compiles the clovershell shell from `tools/console-worker/CcsWorker/Shell` (one copy shared with the console worker). The backup and restore recovery images are embedded in it when built with `-p:CcsRamImages=DIR`. |
| `CcsBackupUi` | The window (WPF), with Classic Cart Studio's theme (linked from `tools/desktop`) and wordmark. |
| `ccs-backup-utility` | The standalone CCS Console Workbench: its managed dependencies are embedded in the executable and loaded from memory; nothing is extracted. |
| `CcsBackup.Tests` | Offline tests (simulated consoles, fixtures from `tests/make_fixtures.py`, the built standalone for the process-lifecycle cases). Nothing opens a USB device. The `driver` group uses a simulated PC; `driver-real` reads this PC's driver state (reads only, counts printed) and runs only with `--real-driver-store`. |
| `ram/` | The console side: `ccsbr` (the fixed read verbs, including Console Browser's checked range reads, and the restore image's checked writes), `ccsprep` (first-time preparation steps, and Return to Original Software's steps: Hakchi2 CE 3.9.3's uninstall as checked steps, with this console's own bootloader), `ccsrescue` (Console Rescue's boot0/boot1 steps), `ccsfiles` (Expert and Developer Mode's checked file changes), `ccsboot/` (Classic Cart Studio's boot-stage reader/writer, built from published source with zig, pinned by hash), `ccs-init`/`ccs-idle` (the start that never mounts storage and never starts the normal system), `build_images.py` (the five recovery images from Hakchi2 CE 3.9.3's pinned recovery image) and `test_recovery_images.py`. |

## The physical boundaries

`CcsBackup/Gate.cs`: `ConsoleSessions` (any connection to a real console), `RestoreWrites` (G3) and `PreparationWrites` (G1) are `false`
compile-time constants. With them false the code that opens a FEL device or a clovershell link is compiled out. Opening one is a reviewed
code change released as its own milestone. A simulated console (`SimulatedConsole`: a folder of files standing in for a
console, for tests and demonstrations; `--simulated-console FOLDER`) is not behind them: it can only change files on the PC, and every
backup made from it is marked simulated and refused for a real console.

## Build

    sh tools/console-backup/ram/ccsboot/build.sh <out folder>                    (WSL, the project's zig 0.17.0)
    python tools\console-backup\ram\build_images.py --hakchi-release <hakchi2-ce-3.9.3-portable.zip, or a folder with its basehmods.tar and hakchi.exe> --ccsboot <out folder>\ccsboot --out <new folder>
    pwsh tools\console-backup\build_workbench_payload.ps1 -RamImages <images folder, with the CCS HOME package in ccs-home>
    bash tools/console-backup/native-host/build_host.sh --payload <the managed CCS-Console-Workbench.exe> --out <host folder> --arch all --version <WORKBENCH-VERSION>   (WSL)
    python tools\console-backup\check_shared_engine.py --host <host folder> --bundled <the Windows app's folder> --managed <the managed CCS-Console-Workbench.exe>

`build_images.py` checks every input by SHA-256 (ccsboot's source and build too) and builds `ccs-backup`, `ccs-restore`, `ccs-prepare`,
`ccs-rescue` and `ccs-files`. All five are embedded in the shared engine (`CcsBackup`). Since 0.11.4 there is one Workbench program:
Classic Cart Studio for Windows installs the single-file `CCS-Console-Workbench.exe` the standalone release publishes (byte for byte, x64 or
Arm64, as `workbench\CCS-Console-Workbench.exe` beside `ClassicCartStudio.exe`) and starts it as a program of its own with a checked
launch context (`ccs-backup-utility/InstalledLaunch.cs`: installed mode, one such window per user session). Both release workflows build
the managed program with `build_workbench_payload.ps1` (the same flags) and the hosts with `build_host.sh` and `WORKBENCH-VERSION`;
`check_shared_engine.py` then proves the copy in the app's folder is byte for byte the x64 host, the hosts carry the managed program
given, and the bundled program's `--engine-info` names the four shared libraries and the five console programs. The release workflow
(`.github/workflows/release.yml`) builds the images on the build Python, tests them on Linux, builds the Workbench hosts and puts them in
the installer.

## Tests

    python tools\console-backup\tests\make_fixtures.py <new folder>            (WSL with e2fsprogs)
    CcsBackup.Tests.exe --fixtures <it> --work <folder> --kernel <a genuine stock kernel> --python <python.exe> --utility <the built standalone>
    python tools\console-backup\ram\ccsboot\test_ccsboot.py <ccsboot out folder> [<pinned hakchi boot/uboot.bin>]   (WSL qemu-arm)
    python tools\console-backup\ram\test_recovery_images.py --images <images folder> --hakchi-release <the same package>   (WSL or Linux, with the project's qemu-arm)

Without a genuine stock kernel file the stock-console cases are skipped (a skip is never a pass).
