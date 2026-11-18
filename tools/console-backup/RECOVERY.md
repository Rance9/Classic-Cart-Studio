# If a preparation or a restore stops

Read this before any recovery. Nothing here is automatic, and every step that writes the console needs its own, separate approval for
that incident. Recoverability is not guaranteed.

1. **Stop.** Do not run the same operation again and do not try another program yet. Classic Cart Studio has already stopped every later
   write and recorded where it stopped (`<backup folder>\.ccs-records\restore-*.json` or `prepare-*.json`).
2. **Read the console as it is now.** Connect it in recovery mode (USB in with POWER off, hold RESET, POWER on, hold about 5 seconds) and
   make a backup with Console Backup & Restore. A backup only reads the console. Its result says what the console's startup software is
   and lists its data storage.
3. **Decide with the evidence.** The options are:
   - **Restore a backup** of this console (Restore Console Backup): available when the console's storage table and bootloaders are the
     same as in that backup. It backs up the current state first and shows exactly what changes.
   - **Return to Original Software** (after Classic Cart Studio's own preparation, in Console Restore of either Workbench host):
     available when the console is exactly as that preparation left it (its preparation record says so: hakchi's kernel and u-boot, the
     original kernel in the spare slot, the storage table hakchi tuned). It backs the console up first in the same session, then does
     Hakchi2 CE 3.9.3's own uninstall as checked steps with one difference: the bootloader written is **this console's own**, taken from
     the backup made before preparing (`ret-stage` sends it to the console's memory and checks it; `ret-uboot` writes it once with
     `ccsboot` and both of the console's readers read it back). hakchi's uninstall pipes the word "default" into its flash tool (sntool
     5833d00, whose source is not public), so what it would write cannot be known before writing; the way back never uses it, and it is
     refused before anything changes when the backup made before preparing has no complete bootloader image. Both backups are bound to the
     plan by hash, held read-only and verified again at confirmation. After a restart, **Restore Console Backup** with the backup made
     before preparing puts back the storage exactly as it was then. A console modified otherwise, or prepared by an earlier version, is
     refused: that remains a separately reviewed and approved recovery, not a button. Writing to a real console is closed in this version
     (`Gate.ReturnWrites`).
   - **Console Rescue** (either Workbench host): when the console's startup stages read as damaged, this console's own boot0 and/or boot1
     from one of its verified backups, staged in the console's memory, written once by `ccsboot` and read back; the console as it is now
     is backed up first (a damaged stage kept as read, acknowledged in exact words). Its own boundary (`Gate.RescueWrites`), closed in this
     version. Whether the console's storage can be initialized without a valid stored boot0, and whether the driver's boot0 writer gives
     back exactly the console's earlier boot0, are hardware facts proved only by the owner's hardware runs (W10 for valid data, W16 for a
     damaged stage on the dedicated used console).
4. **If the console does not reach recovery mode at all**, FEL recovery mode is in the console's boot ROM and cannot be erased by
   software; check the cable, the USB port and the driver first.

Classic Cart Studio never flashes a whole NAND image, formats, erases or repartitions a console as a shortcut, never downloads firmware and
never retries a failed write by itself.
