# Restore and preparation: phase-by-phase interruption matrix

Restore Console Backup, Prepare Console and Return to Original Software write a console's storage. Nothing makes that safe against every interruption: a USB cable
pulled, a PC that loses power or a console that loses power in the middle of a write can leave the console partly changed. This document
says, for each phase, what known-good state remains, where cancelling is safe, what each kind of interruption does and what the next step
is. It describes the implemented engine (`CcsBackup/Engine/RestoreEngine.cs`, `Preparation.cs`, `WorkerHost.cs`; console side
`ram/ccsbr`, `ram/ccsprep`; `ReturnEngine.cs`). Every write stays closed in this version (`Gate.RestoreWrites`, `Gate.PreparationWrites`, `Gate.ReturnWrites`); these paths run on
simulated consoles only until their reviewed activation and a separately approved hardware qualification.

"Window closes" means Classic Cart Studio's or the Utility's window is closed by the person; "window ends" means its process crashes or is
ended (Task Manager); "worker ends" means the controlled worker process ends the same way; "PC" means the PC restarts or loses power;
"console power" means the console loses power or the USB cable is pulled. Before any write, the console runs a Classic Cart Studio program
from its memory and its normal system is not started, so the console's storage is only read.

## Restore Console Backup

| Phase | Known-good state kept | Safe cancel | Window closes | Window or worker ends | PC / console power | Next step |
|---|---|---|---|---|---|---|
| 1 validate (the backup file verified from scratch) | Console untouched; backup files unchanged (read only) | Yes, at once | Stops; nothing written | Nothing written | Nothing written | None needed |
| 2 admit (the console identified; same console, same storage table and bootloaders) | Console untouched | Yes | Stops | Nothing written | Nothing written | None needed |
| 3 protect (a complete backup of the console as it is now) | Console untouched (only read); the chosen backup unchanged; any unfinished protective file stays `.partial` and Incomplete | Yes | Stops; protective file Incomplete | Protective file Incomplete, never used | Same | Run the restore again (a new protective backup is made) |
| 4 plan (what differs, file by file) and 5 confirm (one-time code, 10 minutes) | Console untouched; the protective backup complete and verified | Yes | Stops | Nothing written | Nothing written | None needed |
| 6 write: other storage parts, then the data storage (`nandc`) | Every part not yet written; the chosen backup; the protective backup | Between parts only ("Stop After This Part"); a part in progress is finished, checked and read back first | Asks; if closed anyway: the part in progress finishes and is read back, then it stops | The part in progress may be partly written; the record stays `writing`, never `restored` | The part in progress may be partly written | Do not restart the console yet. Back up the console as it is now (read only), then decide on a new restore (its own confirmation) |
| 6 write: hakchi's spare startup slot, then the startup software (written last: until then the console starts its current startup software) | The startup software stays as it was until its own write | Between parts | As above | The startup image may be partly burned; the console may not start | Same | FEL recovery mode still works (it is in the console's boot ROM); the restore image can be loaded from RAM to read the console and to restore again under a new, separately approved operation |
| 7 readback (every written part read again by the console; the console identified again) | Every write already read back once | Yes (nothing more is written) | Stops; result "state not verified" | Record shows the last phase | Same | Back up the console as it is now to see its state |

A multi-part restore is not one atomic transaction. The order (other parts, the data storage, the spare slot, the startup software last)
means that a restore interrupted before its last write leaves the console starting its current startup software. Every first failure
stops every later write; nothing is written twice; there is no automatic rollback.

## Prepare Console

| Phase | Known-good state kept | Safe cancel | Interruption during it | Next step |
|---|---|---|---|---|
| admit (a recovery baseline AND a backup made now) | Console untouched | Yes | Nothing written | None |
| check, plan, confirm | Console untouched | Yes | Nothing written | None |
| `files` (data storage mounted read-write, hakchi's files installed, synced, unmounted) | Kernel, bootloader and spare slot unchanged: the console still starts Nintendo's system | Between steps | The data storage may be left mounted-unclean or with part of hakchi's files; Nintendo's saves are not removed by this step | Back up (read only). Restoring the backup made before preparing is offered only if the storage table is still the same (hakchi's mod_install may already have adjusted it: `sunxi-part tune`); otherwise prepare again or recover under a separate approval (`RECOVERY.md`) |
| `uboot` (hakchi's u-boot burned once, read back) | Kernel and spare slot unchanged | Between steps | The bootloader area may be partly burned | FEL recovery mode is in the boot ROM; recovery is a separately approved operation |
| `slot68` (the original kernel copied to the spare slot) | Running kernel unchanged | Between steps | The spare slot may be partly burned; the running kernel is unchanged | Prepare again (the step reports an invalid slot), or recover under a separate approval |
| `kernel` (hakchi's kernel with this console's key burned once, read back) | The original kernel is in the spare slot (checked before this step) | — | The startup software may be partly burned | The original-kernel route: from RAM, write the spare slot's original kernel back (`RECOVERY.md`), under a separate approval |

Derived corrections from Hakchi2 CE 3.9.3 (its pinned files are unchanged; `ccsprep` redefines the two functions after sourcing them):
upstream `flashBoot` burns a second time when its validity check fails, `ccsprep` reports and stops; upstream `installBoot2` continues
when the original kernel's copy fails, `ccsprep` makes that copy its own step and the kernel step refuses to run without it; upstream
`mount_base` reports success even when the data storage did not mount (its last command is a file test), `ccsprep` checks the mount
before installing anything, so hakchi is never "installed" into RAM and reported done. Everything else runs as hakchi 3.9.3 runs it: its
`/hakchi/init` environment (the system's `usr` and `sbin`, hakchi's `etc`, its PATH), its functions (`preinit`, `preinit.d`, which are
byte-identical to `hakchi.hmod`'s own, and `script/install`), its settings loaded as `script/transfer` loads them, then `mod_install` or
`mod_repair` and the hmod's own copy steps.

## Process lifecycle (both entry points)

- The window starts its worker (the same executable with `--backup-worker`) as an ordinary child process. Closing the window asks first;
  the worker then stops at its next safe boundary (a backup at once; a restore or preparation after the part or step in progress has
  finished and been read back) and exits. If the window ends without warning, the worker sees its input end and does the same. Nothing is
  left running: there is no service, startup task or updater.
- If the worker ends without warning, nothing it was writing on the PC is ever trusted afterwards: an unfinished backup keeps its
  `.partial` name or a record that is not `complete`, and every later use verifies the file from scratch.
- One console operation at a time on the PC (a named mutex shared by both programs and the HOME console worker).

## Return to Original Software (after Classic Cart Studio's own preparation)

Session A runs in the preparation image under `Gate.ReturnWrites`; session B is Restore Console Backup above, unchanged, after the
console restarted (its storage driver reads the storage table only when it starts).

| Phase | Known-good state kept | Safe cancel | Window closes | Window or worker ends | PC / console power | Next step |
|---|---|---|---|---|---|---|
| 1 find and 2 check (the preparation record; the pre-preparation snapshot verified; the console exactly as preparation left it) | Console untouched; every backup unchanged | Yes | Stops | Nothing written | Nothing written | None needed |
| 3 protect (a complete backup of the console as it is now, in this session) and 4 plan / confirm | Console untouched; the snapshot and the protective backup complete and verified | Yes | Stops | Nothing written | Nothing written | None needed |
| 5 ret-kernel (the spare slot's original kernel as the startup software) | Console still prepared (hakchi's kernel) until the burn; the spare slot's copy is only read | Between steps | Asks; the step in progress is finished and read back | The startup image may be partly written | The startup image may be partly written: the console may not start; FEL recovery mode is in its boot ROM | Recovery (RECOVERY.md): back up, then a separately approved recovery |
| 5 ret-stage (this console's own bootloader from the backup made before preparing, into the console's memory and checked) | Console untouched (nothing written) | Yes | Stops | Nothing written | Nothing written | None needed |
| 5 ret-uboot (that staged bootloader written once by ccsboot; both readers read it back) | The original kernel is the startup software | Between steps | As above | The bootloader may be partly written | The bootloader may be partly written: the console may not start | Console Rescue with this console's backup (its own boundary), or recovery |
| 5 ret-uboot reads back differently from the staged bytes | The console with Nintendo's kernel and a bootloader that is not verified; hakchi's files and the tuned table untouched | n/a | n/a | n/a | n/a | Stops and says so; nothing is written again; Console Rescue with this console's backup, in a new approved session |
| 5 ret-files (hakchi's files removed) and ret-table (hakchi's shrink) | The original kernel and bootloader | Between steps | As above | Files partly removed, or the table partly changed | Same: the data storage may not mount until the restore | Restore of the snapshot after the return, or recovery |
| 6 verify (kernel, bootloader and table read back and compared) | Every step already read back once | Yes | Stops; "state not verified" | Record shows the last phase | Same | Back up the console as it is, then decide |
| Session B: the restore of the snapshot | As in Restore Console Backup above | As above | As above | As above | As above | As above |
