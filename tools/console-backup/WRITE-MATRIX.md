# CCS Console Workbench: what each mode may write

Every console session runs a Classic Cart Studio recovery program in the console's memory (FEL USB boot). Each program can do only its
own fixed steps. The one exception is Developer Mode's test boot, which starts a startup image that is not a Classic Cart Studio program
(see its row and its limits below). Each kind of write has its own boundary constant in `CcsBackup/Gate.cs`. **In this version every
boundary is `false`.**
No path reaches real hardware. Simulated consoles are folders on the PC; their backups are marked simulated, and those can never be
restored to a real console.

## Programs and boundaries

| Program (image) | Profile | What it can write | Steps (script) | Boundary | Evidence policy |
|---|---|---|---|---|---|
| ccs-backup | backup | nothing (its helper has no write section) | `ccsbr` identify, read, range, digest, sum, card, finish | `ConsoleSessions` | none (reads only) |
| ccs-restore | restore | the logical storage parts and startup slots a confirmed plan names | `ccsbr write` | `RestoreWrites` | **strict**: plan binds the backup and a protective backup made in this session; both held read-only and verified again at confirmation |
| ccs-prepare | prepare | hakchi's files, u-boot, spare kernel slot and kernel (preparation); the way back (return) | `ccsprep` | `PreparationWrites`; `ReturnWrites` for the way back | **strict**: baseline admission plus a capture in this session (preparation); the pre-preparation snapshot plus a protective capture in this session (return) |
| ccs-rescue | rescue | this console's own boot0 and/or boot1, from its verified backup | `ccsrescue` diag, stage0/1, burn0/1 (via `ccsboot`) | `RescueWrites` | **strict**: source backup of this console plus a protective capture in this session; a damaged stage is kept as read and acknowledged in exact words |
| ccs-files | files | regular files on the data storage only | `ccsfiles` stat, put, del | `ExpertWrites` (scope user), `DeveloperWrites` (scope dev) | **session restore point** (owner policy): a verified backup of this console chosen once per connection; later changes are knowingly unprotected |
| ccs-files | files | the startup software (slot 30) only | `ccsdev` kstage, kburn | `DeveloperWrites` | session restore point that holds this console's own startup software |
| ccs-files | files | the system part (`rootfs` on nandb) only: through the console's own encryption mapping when it is encrypted | `ccsdev` sinfo (reads), sstage, sburn | `DeveloperWrites` | session restore point that holds the system part, with the console's storage table unchanged |
| none (the console's boot ROM; the first-party FEL loader's `TestBootSession`) | test boot | Workbench writes nothing to storage; the started image's own code is not contained | FEL: board id and SID read, fes1, the image at 0x47400000, `boota 47400000` | `ConsoleSessions` **and** `DeveloperTestBoot` | session restore point of this console (by its hardware id) holding its startup software, system part, files and startup stages |
| ccs-files | files | chosen saves, settings, hakchi games and CCS data | `ccsfiles` (scope compose) | `ComposerWrites` | **strict**: every source bound and verified again; a backup of the console in this session |
| ccs-prepare | prepare | the chosen method package (CCS Mod or CCS Nucleus) into hakchi's own package folder, installed by hakchi at the next start | `ccsprep method` | `PreparationWrites` | **strict**: as preparation |
| ccs-prepare | method | removing or changing CCS Mod / CCS Nucleus: a package and/or hakchi's removal list in its package folder | `ccsprep method`, `unlist`, `mstate` | `MethodWrites` | **strict**: a backup of the console in this session; the console's method state read again at confirmation |
| ccs-prepare | prepare, method | CCS HOME at power-on, the owner's explicit choice with CCS Mod (off by default): CCS Mod's own setting `/var/lib/ccs/mod/config`, written by the image's pinned CCS Mod program (`start-setting`), never hakchi's overlay; CCS Mod's install at the next start applies it with its own `autostart` command | `ccsprep mstart` (read back; the same setting already there is not written again) | `PreparationWrites` (preparation), `MethodWrites` (a change to CCS Mod) | **strict**: as preparation or the method change; CCS Mod's start state (in `mstate`) read again at confirmation |
| ccs-prepare | prepare, method | CCS HOME's package for the method (`Engine/HomePackage`): a new copy in `/var/lib/ccs/pkg/<copy>` beside the one in use (the attempt's and the copy's records first), then the pointer `pkg/current` in one rename; the saves' identity only when absent | `ccsprep hput` (one archive, its size and SHA-256 bound in the plan, received into RAM, every file read back), `huse` (the switch) | `PreparationWrites` (preparation), `MethodWrites` (a change) | **strict**: as preparation or the method change; the console's HOME state (in `mstate`) read again at confirmation |
| ccs-prepare | firmware | an owner's system image (.hsqs: an NES Classic, Famicom Mini or SNES Classic system, D-190, identified on the PC by hakchi's own rules; a kernel file or another console's system refused) into CCS Nucleus's own folder and its start choices (never hakchi's firmware folders) | `ccsprep rput`, `rimport`, `rdrop` | `FirmwareWrites` | **strict**: the image and a backup of the console in this session bound and checked again |

Shared rules:
- Every console write needs `ConsoleSessions` **and** its own boundary. One boundary never opens another.
- Every write is in a reviewed plan, bound by a digest, with a one-time confirmation that expires after 10 minutes.
- The operation record is saved before the first write. With no record, there is no write.
- The first failure revokes the operation's write authority. Nothing is retried, rolled back or written automatically afterwards.

## Modes by console type

| Mode | Stock console | Hakchi console | CCS Mod / CCS Nucleus console | Written | Never written |
|---|---|---|---|---|---|
| Console Backup / Console Browser check-up | ✓ | ✓ | ✓ | nothing | everything |
| Console Restore: a backup | ✓ (same console) | ✓ | ✓ | the storage parts and startup slots the backup holds and the plan names | boot0, boot1, storage table, physical NAND |
| Console Restore: the way back from CCS preparation | — | only after CCS's own preparation, unchanged since | — | Nintendo's kernel from the spare slot, this console's own boot1 (from its pre-preparation snapshot), removal of hakchi's files, hakchi's table shrink | boot0, any other bootloader |
| Console Rescue | ✓ | ✓ | ✓ | this console's own boot0 and/or boot1 | kernel, table, data storage, another console's stages, custom boot code |
| Console Composer: fresh CCS setup (preparation) | ✓ (after a baseline) | ✓ if already prepared by the pinned hakchi | — | as preparation | boot0, physical NAND |
| Expert Mode (inside Console Browser) | ✓ user files | ✓ user files | ✓ user files | `clover/profiles/`, `hakchi/games/`, `ccs/data/` regular files | system files, overlays, kernel, boot stages, table, devices, links |
| Developer Mode: files | stock base SquashFS has no writable overlay: data storage files only | ✓ including `hakchi/rootfs/` | ✓ | any regular file on the data storage | devices, links, folders, `/proc`, `/sys`, boot stages, table, keys |
| Developer Mode: startup software | ✓ | ✓ | ✓ | an Android boot image in the console's own startup layout as the startup software, after a command-line comparison | spare slot, boot stages, table, data |
| Developer Mode: system part | ✓ (encrypted: with the key its own startup software holds) | ✓ | ✓ | a SquashFS system of the console's own class (its current compression, gzip contents checked, its board type, a start program) | startup software, spare slot, boot stages, table, data, keys |
| Developer Mode: test boot in memory | ✓ | ✓ | ✓ | nothing, by Workbench; what the started code does is up to it | everything, by Workbench |
| Console Composer: rebuild / migration | ✓ | ✓ | ✓ | chosen files under clover/profiles/, hakchi/games/, ccs/ from verified backups (migration: from another console's backup) | startup software, boot stages, table, identity |

Rows marked ✓ are implemented and exercised only on simulated consoles. None has run on a real console.

## Developer Mode's supported classes and status

Status words: **implemented** (the code is in this version), **software-verified** (the engine group `developer`, the console scripts under
qemu-arm in `ram/test_recovery_images.py`, the FEL protocol model; never a real console), **hardware-pending** (needs a reviewed run on a
real console), **disabled by gate** (its `Gate.cs` boundary is `false`).

| Operation | Supported class (what is admitted) | Checks before anything is sent | Way back | Status |
|---|---|---|---|---|
| Files | regular files anywhere on the data storage, including hakchi's system overlay | path rule on the PC and the console, current content, no links | Console Restore with the restore point | implemented, software-verified, hardware-pending, disabled by gate |
| Startup software (slot 30) | Android boot image, header version 0, 2,048-byte pages, kernel at 0x40008000, ramdisk at 0x41000000, tags at 0x40000100, no second stage, an ARM zImage kernel that ends below the ramdisk, its own mkbootimg checksum, a plain command line, a gzip/xz/LZO/LZMA/LZ4/bzip2/cpio ramdisk, no padding (the slot reads back only what the header describes), at most 7,340,032 bytes; the layout of the console's genuine kernel and hakchi 3.9.3's (both checked by the tests) | the above (`StartupImage.Inspect`); a ramdisk that asks to open the system part (`decrypt`) but positively holds no key and does not read one is refused; command-line differences shown (a key on it never shown) | Console Restore (the restore point's `restore/boot2-active.img`) | implemented, software-verified, hardware-pending, disabled by gate |
| System part (nandb) | SquashFS 4.0 in the compression of the console's current system part (the one its startup software is known to read), gzip contents read on the PC, a start program (`/sbin/init`), the console's board type (`/etc/clover/boardtype`), whole 4,096-byte blocks, no larger than the part; written through plain dm-crypt `aes-xts-plain` with the console's own key file (hakchi 3.9.3's "flash system partition" parameters) or directly when the part is a plain SquashFS | the above on the PC; on the console (`sinfo`, read-only): its key in the session, the part's size and current compression, its free memory for staging; the startup software starts from `root=/dev/nandb`; the part's raw MD5 at the review and again at the confirmation; staged in RAM and checked there (`sstage`) | Console Restore (the restore point's `restore/nandb.img`; the tests restore it byte for byte) | implemented, software-verified, hardware-pending, disabled by gate |
| Test boot in memory | own startup software: the restore point's recorded `restore/boot2-active.img`; startup image file: the same layout as the startup software (above), at most 32 MiB padded | the console's SID must be the restore point's console before anything is sent; the restore point holds startup software, system part, files and startup stages; the image hashed again at the confirmation; a record saved first | nothing to undo from Workbench; for what the image's code changes: Console Restore and Console Rescue with the restore point | implemented, software-verified (simulated console and FEL protocol model), hardware-pending, disabled by gate (`DeveloperTestBoot`) |

The phrase "nothing installed on your console" belongs to Classic Cart Studio's own read-only session only (`ClassifyRamImage`). A test
boot is never described that way: the plan and the window say that the started code controls the console and can write its storage,
startup stages or keys.

## Not supported in this version, with the exact dependency

- Custom boot0, boot1 and partition-table writes. These are excluded. A future design would need everything listed in the assignment
  (provenance, board/DRAM/NAND parameters, normalization, bounded targets, recovery that does not depend on boot0, staged payload,
  expected readback, a demonstrated failed-stage return). Console Rescue writes only this console's own recorded stages.
- A "nothing written" promise for any started code other than Classic Cart Studio's own read-only session: impossible to enforce from the
  loader (a started kernel controls the console). Stated; never claimed.
- Test-booting a system part (a root file system) on its own from memory: needs a startup image that mounts its root from RAM (an initrd
  or a RAM-backed block device, `root=/dev/ram0`); neither the console's genuine kernel nor hakchi 3.9.3's is established to do that. A
  developer can put a system in their own startup image's ramdisk and test-boot that image (the startup-image class).
- Developer Mode's system part in another compression than the console's current one (gzip): an xz image is now read and checked on the
  PC (Device/Xz.cs), but the console's own startup software is established only for its current system part's compression; lzma, lzo,
  lz4 and zstd are not read at all. Needs evidence of the startup kernel's SquashFS compressors (and a decoder for the last four).
  (Owner system images for CCS Nucleus are different: hakchi's own kernel mounts them, and hakchi's own dumps are xz.)
- System images without the console's board type (roots that are not the console's own system class): no evidence they are built for
  this console; not admitted.
- Hakchi's spare startup slot (68): never written by Developer Mode. It holds the original kernel that Return to Original Software
  depends on; replacing it would remove that route. Offering it needs an owner decision and its own protected return.
- Hardware facts the software cannot settle (hardware-pending): that the boot ROM's FEL read of the SID registers (0x01C23800) equals what
  the running console reports as its hardware id; that u-boot's `boota` starts an image that is not a Classic Cart Studio program with its
  own command line (plus u-boot's board additions); that `umount /newroot`, `cryptsetup close` and a read-write `cryptsetup open` of the
  system part succeed in the files session; that `MemFree` (the console runs Linux 3.4, without `MemAvailable`) is a sufficient staging
  budget.

## What software cannot guarantee to undo

- Code that runs on the console after it starts (a developer kernel or program) controls the console. It can write storage, startup
  stages or keys. A restore point, a hash check or read-only block devices in the loader do not contain it. "Nothing written" is said
  only for Classic Cart Studio's own read-only sessions.
- A write that the hardware does not complete (power loss, a failing NAND block, a USB drop mid-command) can leave a part partly
  written. The engine stops and reports, and reconciliation needs a new, separately approved read-only session. There is no automatic
  repair.
- Without its own recorded copies, a console cannot get back startup stages it never had backed up. A backup of another console is
  never used.
- Failed hardware: NAND cells, controller, RAM, SoC, power or USB.
- Saves made after a restore point are not protected by it. That is the stated policy of Expert and Developer Mode.

## Persistence on the PC

| What | Where | When |
|---|---|---|
| Backups, second copies, operation records and journals (`.ccs-records`), partial files | the backup folders the person chooses | each operation |
| Standalone preferences | in memory; a settings file only where the person saves one | on request |
| Classic Cart Studio's preferences | `%LOCALAPPDATA%\CCS Console Backup\settings.json` (the app's existing profile) | the app only |
| A USB driver association, if one is set up | Windows (driver store) | only with the person's approval and UAC |
| An optional scheduled check | a per-user Windows task | only if the person turns it on |

Windows may keep its own records of programs it runs: prefetch, Defender, crash reports and paging. No promise of "no trace on the PC"
is made.
