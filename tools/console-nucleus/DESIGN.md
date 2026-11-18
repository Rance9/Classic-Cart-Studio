# CCS Mod and CCS Nucleus: console-side design

Classic Cart Studio's two install policies for an NES Classic that runs Hakchi: **CCS Mod** (CCS HOME beside the existing setup) and
**CCS Nucleus** (a CCS-owned start chooser and boot registry around the normal Hakchi platform). Both share the CCS HOME package, its
saves, the ownership journal and this library; each owns its own start, entry, return and settings. Copyright (C) 2026 Rance9,
GPL-3.0-or-later.

Files (all under `tools/console-nucleus/`):

| File | What it is |
|---|---|
| `console/lib.sh` | Shared shell library (sealed records, ownership journal, CCS HOME launch, menu entry). Each package carries its own copy. |
| `console/mod/` | CCS Mod: `bin/ccs-mod`, the optional start script `S81ccs-mod` (a template, written only when turned on), `install`, `uninstall`, `readme.md` |
| `console/nucleus/` | CCS Nucleus: `bin/ccs-nucleus`, `p7060_ccsnucleus`, `p7071_ccsnucleus`, `S81ccs-nucleus`, `install`, `uninstall`, `readme.md` |
| `console/assets/` | The two menu-entry pictures (made from Classic Cart Studio's own app icon) |
| `build_packages.py` | Builds `ccs-mod.hmod` and `ccs-nucleus.hmod` reproducibly, with `MANIFEST.json` and `SHA256SUMS` |
| `build_home_package.py` | Builds the release's minimal CCS HOME package (the Included Games) for both layouts, deduplicated by SHA-256, into the console programs' folder as `ccs-home/` |
| `formats.py` | The PC side of the records: sealing, the registry from JSON, the journal reader |
| `capacity.py` | The room needed, from measured inputs only |
| `test_nucleus.py` | The tests (PC checks, then hakchi's own code on the pinned BusyBox under qemu-arm) |

Everything here is software only. Nothing was run on a console, and nothing here opens a USB, FEL or driver path.

## 1. Hakchi 3.9.3's start sequence and the insertion point (verified in the pinned files)

Source: the pinned `basehmods.tar` (SHA-256 `7f7a6b8e…dc77c`) → `hakchi.hmod` (`003994b4…dbff592`) → `boot/boot.img` (`9e921c32…064e4c`),
read with `tools/console-backup/ram/build_images.py` `read_inputs`. Hakchi's preparation flashes this `boot.img` (with the console's key
added to its command line), so its ramdisk is what runs at every start. Line numbers refer to those files. The installed overlay copies
of `etc/preinit` and `etc/preinit.d/*` are these same ramdisk files (`mod_repair` copies `$modpath/rootfs/` to `$rootfs/`), and
`hakchi.hmod`'s own `etc/preinit.d/b0010_functions` and `b0050_boot` are byte-identical to the ramdisk's.

| Step | Pinned file (SHA-256 prefix) and lines | What happens |
|---|---|---|
| 1 | `/init` (`80620788`) 9–80 | devtmpfs, proc, sys; NAND driver; key; the system image opened read-only and mounted at `/newroot` |
| 2 | `/init` 88 | `sh /hakchi/init` |
| 3 | `/hakchi/init` (`ede44382`) 19–27 | sources `script/config`, `script/base` (hakchi's functions), `script/shell`; then `script/transfer` **only** when `/hakchi/transfer` exists (a RAM boot from the PC), else `mount_base` (RAM `/newroot/var`, the data storage at `/newroot/var/lib`) |
| 4 | `/hakchi/init` 29–31 | `$rootfs/bin/busybox sh -c "source $preinit && preinit"` (`$rootfs` = `/newroot/var/lib/hakchi/rootfs`) |
| 5 | `etc/preinit` (`0e8ace84`) 72–75, 7–22 | `preinit()`: `source_base` (every `b????_*`), then `source_parts "$preinit.d/p????_*"`: **all p-files are joined into one script in name order and sourced in one shell** |
| 6 | `p7000_preinit` (`426d6ecf`) 3–9 | with `cfg_boot_stock=y`: sources `p????_*firmware*` only and `return`s: the rest of the p-files never run |
| 7 | `p7020_usb` (`28a48cb6`) 12 | `checkExtStorage`: USB storage mounted at `/newroot/media` (when it has a `hakchi` folder) |
| 8 | `p7070_firmware` (`643675f8`) 3–4 | `[ "$cfg_firmware" == "_nand_" ] \|\| loadFirmware`, then `mod_repair_etc` |
| 9 | `b0010_functions` (`0b912daf`) 172–191 | `loadFirmware`: `$mountpoint$cfg_firmware` if it is a SquashFS image; with `cfg_firmware='auto'` (the default) **the first `*.hsqs` found in `/media/hakchi/firmware/` or `/var/lib/hakchi/firmware/` is mounted**; `remount_root` loop-mounts it as the new root |
| 10 | `b0010_functions` 130–136 | `remount_root` when the loop mount fails: `cfg_firmware="_nand_"; save_config; shutdown` |
| 11 | `b0010_functions` 86–89, 79–84; `script/removed` (`f620d890`) 17 | `mod_repair_etc` → `remove_old "$rootfs"` sources `/hakchi/script/removed` at **every** start; line 17 is `rm -f "$rootfs/etc/init.d/S81clover-mcp"` |
| 12 | `p7072_overlayfs` (`6418e601`) 3–11 | overlayfs: the mounted system (lower) plus hakchi's `rootfs` (upper) become `/` |
| 13 | `p7080_hmods` (`4ecd3f47`) 3–31 | packages waiting in a transfer folder: `packs_install`, `save_config`, `reboot_kexec` |
| 14 | `pa100_games`, `pffff_config` (`ed77cf0f`) 3–6 | games bound over the menu's folder; **every `cfg_` variable of the shell is saved to `p0000_config`** |
| 15 | `/init` 90–96 | `switch_root /newroot /sbin/init` |
| 16 | overlay `etc/inittab` 10; `etc/init.d/rcS` (`537f43fa`) 8–27 | BusyBox init runs `rcS`: every `/etc/init.d/S??*` in name order, `start` |
| 17 | `b0010_functions` 421–428; `bin/remote-exec`; `bin/chmenu` 28 | later menu restarts: `uistart` runs `sh /etc/init.d/S81clover-mcp start` only; `remote-exec` (S82) runs `uistart` when `/var/startmcp.flag` appears (`chmenu`, `game_hangup_detect.sh` set it) |

### The insertion point

**CCS Nucleus's early dispatch is `etc/preinit.d/p7060_ccsnucleus`, and its second half `etc/preinit.d/p7071_ccsnucleus`, both in hakchi's
overlay, installed by hakchi's own `pack_install`.** In hakchi's own order (step 5) they run:

- `p7060_ccsnucleus` after `p7020_usb` (USB storage is mounted, so an option's external dependency can be checked) and **before**
  `p7070_firmware` (so the system image for this start is decided before `loadFirmware` reads `cfg_firmware`);
- `p7071_ccsnucleus` right **after** `p7070_firmware` and before `p7072_overlayfs`, `p7080_hmods` and `pffff_config` (so the owner's own
  `cfg_firmware` is back in the shell before anything can save hakchi's settings).

Neither name contains `firmware`, so with `cfg_boot_stock=y` (step 6) neither runs: hakchi's own "start stock" setting wins. Because all
p-files are joined into one sourced script (step 5), a p-file must never `exit`, `return` at its top, or contain a syntax error that
would cut the script short. Each of the two files therefore only defines one function and calls it once, and the decision itself is
made by a **separate** program (`ccs-nucleus early`, run with `sh`); anything but one complete, checked answer line leaves
`cfg_firmware` exactly as it was. The test runs the pinned preinit with these files among the pinned ones and with a third-party
`p8000` file after them, and checks the order by what each step leaves behind.

**The chooser on the TV (section 2a) runs from `etc/init.d/S81ccs-nucleus`**, a new start script that sorts after the `S80` drivers and before
`S81clover-mcp` in rcS's order (step 16). It replaces nothing: when the console's own system is chosen it returns, and rcS continues with
`S81clover-mcp` (the console's menu start, or whatever start the owner already has there). Later menu restarts (step 17) run
`S81clover-mcp` only, never this script, so a game or menu switch in the console's own system never comes back into CCS unexpectedly.
CCS Mod's optional start, `S81ccs-mod`, uses the same slot the same way.

**Finding (verified in the pinned files and in the test):** hakchi removes an overlay `/etc/init.d/S81clover-mcp` at every start
(step 11). Classic Cart Studio's earlier installer wrote its start-up module ("ccs-home") exactly there, so hakchi's own `p7070_firmware`
deleted it at the first start after installation. That route is gone: Classic Cart Studio's installer (`tools/companion/console_engine/
ccs_install.py`, through the transport worker's `module-install`) now installs the same pinned CCS Mod / CCS Nucleus package with hakchi's
own `pack_install` on a running console, and retires an earlier "ccs-home" module (hakchi's `pack_uninstall` of its own list) only when its
records show it is this program's. Neither method writes an `S81clover-mcp`.

### Same root and different roots

- **Same root** (the console's own system, CCS HOME): no restart. From the console's menu, the menu entry is started like a game: the
  menu ends itself and hands over the screen; CCS waits until nothing holds `/dev/fb0` (`lsof`, as hakchi's `chmenu` waits) and never
  stops a process by force. CCS HOME's player writes its own saves before it ends; everything is synced before the screen goes back.
  While CCS HOME runs from the menu, `/tmp/.heartbeat` is touched every second, as a game does, so hakchi's `game_hangup_detect.sh` does
  not restart the menu under it.
- **Different root** (an owner's system image): the native path is configuration plus restart (`bin/hsqs` 15–22: `cfg_firmware`,
  `save_config`, `reboot`). Nucleus keeps that path but does not change the owner's saved configuration: the chooser writes a sealed
  one-time request (`state/next`) and restarts through hakchi (`hakchi reboot`); `p7060` sets `cfg_firmware` to the chosen image **for that
  start only**; hakchi's own `loadFirmware` mounts it; `p7071` puts the owner's value back. Nucleus never calls `hsqs`, never writes
  `p0000_config`, and never invokes `script/transfer`'s firmware branch (`script/transfer` 27–38: it deletes every other image in
  `/var/lib/hakchi/firmware` with `find … -delete`).
- Images live in Nucleus's own folder `/var/lib/ccs/nucleus/roots/`, **not** in hakchi's firmware folder: with `cfg_firmware='auto'`,
  hakchi boots the first image it finds there by itself (step 9), which would silently change the owner's normal start.

### An SNES Classic system on this console (owner decision D-190)

The owner's own SNES Classic system is an owner image like any other, on the path above; nothing in Nucleus's start path is
specific to it, and nothing about it changes the console's identity, bootloader, startup software or own system.

- **Input: the owner's own file only.** The SNES Classic's decrypted system part as a SquashFS image (`.hsqs`, as hakchi's own dump
  writes it: hakchi 3.9.3 repacks it with xz), chosen on the PC (Console Composer, Your System Images). Classic Cart Studio supplies,
  links to and downloads no system image. A startup software (kernel) file such as a `kernel_snes.img` is not a system and is refused
  by name: it cannot start one.
- **Checked on the PC before anything is sent** (`CcsBackup/Engine/SystemImage.cs`, read-only, gzip or xz: `Device/Xz.cs`): a
  console system's root (`/etc/clover`, a start program `/sbin/init`) and which one, by hakchi's own `softwareCheck` rules
  (`b0010_functions` 298–329, quoted in the file's header): `/etc/clover/boardtype` `dp-shvc` is an SNES Classic system, `dp-hvcj` in
  `/etc/issue` a Famicom Mini one, otherwise an NES Classic one; `/etc/clover/REGION` `JPN` or `EUR` its region, anything else USA (a
  North American NES Classic records `EUR_USA`); a Mega Drive system (`/usr/game/version`, another console's hardware) and any other
  compression than gzip or xz are refused with the reason. The plan names the system, its region and the version the image records
  (`/etc/clover/VERSION`, for example `release-v1.0.7-0-g4ea4041` on an NES Classic), and the record keeps them; the name in the chooser
  defaults to My SNES System.
- **On the console** (unchanged steps, section 7a): `rput`/`rimport` into Nucleus's own folder, a registry choice; never hakchi's
  firmware folder or `script/transfer`'s firmware branch (it deletes the other images there); never a donor bootloader, raw NAND or the
  SNES console's kernel. At the chosen start, hakchi's own kernel and modules run the SNES system (`loadFirmware`, `remount_root`); its
  `softwareCheck` sets `sftype=snes`, the SNES games folder, and moves the HOME menu's `system-save.json` aside for the NES system (and
  back at the NES system's next start), so neither system's menu saves replace the other's. `p7071` puts the owner's `cfg_firmware` back,
  so the next start without a choice is the console's own NES system: the way back is a restart. Inside the SNES system the chooser's
  menu entry offers CCS HOME and the console's own system as restarts (section 2a).
- **Needs:** an SNES Classic controller (the NES Classic controller has no X, Y, L or R) and the room for the image beside 8,000,000 bytes.

## 2. The boot registry, `ccs-boot-registry/1`

A sealed record at `/var/lib/ccs/nucleus/registry`: LF lines `key=value`, first `schema=ccs-boot-registry/1`, last `end=<SHA-256 of
every byte before that line>`. A registry whose seal does not match counts as absent (then Nucleus starts nothing: the console's own
system starts). The PC writes it from JSON with `formats.registry_text` (same bytes); the console checks it again (`reg_ok`).

```
schema=ccs-boot-registry/1
generation=4                      raised on every change; registry-install refuses an older one
default=ccs-home                  a listed option
selection=last                    default | last  (last: the last option that started and was confirmed, while listed and not failed)
chooser=always                    always: the chooser on the TV at every start | never: the start without it (only an option in the console's own system)
timeout=10                        0-99 seconds of countdown on the TV at start (0: waits); the countdown is the only automatic start of an owner's image
options=native ccs-home owner-1   the order shown
opt.<id>.kind=native|ccs-home|owner-root
opt.<id>.label=...                plain words, 1-60 characters
opt.<id>.image=/var/lib/ccs/nucleus/roots/<id>.hsqs   owner-root: Nucleus's own folder, or by reference /var/lib/hakchi/firmware/*.hsqs or /media/hakchi/firmware/*.hsqs
opt.<id>.sha256=<64 hex>          owner-root: the image's SHA-256 (checked in full again whenever its size or time changed)
opt.<id>.bytes=<n>                owner-root: its exact size
opt.<id>.package=current          ccs-home: the CCS HOME copy /var/lib/ccs/pkg/current names
opt.<id>.kernel=3.4.113.29-madmonkey   the kernel the option needs (checked against uname -r)
opt.<id>.drivers=clovercon ...    modules that must exist under hakchi's lib/modules/<kernel>
opt.<id>.requires=/media/hakchi/games ...   external dependencies (absolute console paths that must exist at that start)
opt.<id>.overlay=hakchi           whether hakchi's overlay (its packages) applies: hakchi applies it to every root
opt.<id>.saves=/var/lib/clover/profiles/0   save scope (informational; hakchi swaps NES/SNES menu saves itself in softwareCheck)
opt.<id>.compat=nes-usa           the console model it is for (informational; the PC checks it at import)
opt.<id>.owner=ccs|foreign        imported by Nucleus (its data) or referenced (never moved or removed)
opt.<id>.fallback=native          every option falls back to the console's own system
opt.<id>.auto=yes|no              informational for the PC; Nucleus never starts an owner-root by itself without the TV countdown
end=<seal>
```

Rules (both `formats.validate` and the console's `reg_ok`): ids `[a-z0-9-]`, 1–32, stable; exactly one option `native`, kind `native`
(the owner's original environment: whatever hakchi starts without Nucleus, `cfg_firmware` untouched), which cannot be removed; an
`owner-root` needs an allowed image place, SHA-256, size and `fallback=native`; the default is listed; `chooser=never` with an
`owner-root` default is refused.

State (`/var/lib/ccs/nucleus/state/`, sealed `ccs-nucleus-state/1`): `next` (one-time request, used up by `p7060` before anything
else), `attempt` (an owner-root start in progress: `stage=selected` by p7060, `mounted` by p7071; removed by `S81ccs-nucleus` when that
system reaches its start scripts), `last`, `failed-<id>` (reason; an option that failed is not started automatically again until it
starts normally once), `verified-<id>` (the last full hash check), and a short `log`. This start's decision is kept in RAM at
`/var/ccs-nucleus/boot` (hakchi's start stage mounts RAM at `/var`, and the running system keeps it, as hakchi's own `/var/version`).

### Default, last choice, failure and return

1. `p7060`: an `attempt` left by the previous start was never confirmed → it is recorded as failed once (`not-loaded` or
   `did-not-start`) and removed; if hakchi's own failure path had saved `cfg_firmware='_nand_'` (step 10), the owner's value comes back.
2. A one-time `next` request is used (and removed) first; otherwise the registry's choice **in the console's own system**
   (`selection` default/last; an owner-root there gives the default; a failed one gives `native`).
3. An owner-root is started only when it passes every check (place, presence, size, `hsqs` magic, SHA-256, kernel, drivers, external
   dependencies) and the owner's `cfg_firmware` value can be restored; otherwise the console's own system starts and the reason is kept.
4. `S81ccs-nucleus`: in an owner's image, confirms the start and does nothing else; in the console's own system, the chooser on the TV
   (section 2a): the registry's choice can start by itself once (the countdown on the TV, or `chooser=never`); every later round waits for
   a choice, except after CCS HOME ended with a problem at start, when the chooser counts down to the console's own system (with
   `chooser=never` that start simply continues); at most 60 rounds. Leaving CCS HOME shows the chooser again, also with `chooser=never`
   (CCS HOME's leave item in the `nucleus` layout is "Choose Another System"); choosing the console's own system continues its start. A
   failure always ends in the console's own system. **The next power-on always begins in the console's own root** (an owner's image is
   never the automatic start of the start stage), which makes the return deterministic and rules out a start loop.

## 2a. The chooser on the TV

**The program.** The chooser is a mode of the native HOME program, `ccs-home-armv7 --choose FILE` (`tools/console-simulator/core/chooser.c`,
dispatched from `main.c`; built by `scripts/wsl_build.sh` into the same static ARM executable the CCS HOME package carries at
`home/bin/ccs-home-armv7`). `ccs-nucleus` finds it in the CCS HOME package `pkg/current` names (the same pointer checks as `ccs_home_dir`,
but only the program itself is needed, so a package whose player is missing still shows the chooser with CCS HOME as a choice that cannot
start), writes what to offer to `/tmp/ccs/run/choices` and acts on the one answer line the program prints. The program decides nothing
about the console, starts nothing and writes no file. It draws in the player's menu style with the package's menu fonts (`fonts/`, the
built-in letters when they cannot be read) and reads the controllers with the package's controller codes (`config/controller-map.txt`).
Its log lines go to `/tmp/ccs/logs/chooser.txt` (RAM).

**`ccs-choices/1`** (the full field list is in `chooser.h`): `mode` boot|menu, `title`, `highlight` (the row focused first), `auto` and
`timeout` (the countdown), `notice` (a line at the top), `panel` main|settings, `settings` (the label of the last row), and one line per
option, in the registry's order: `choice=ID|KIND|ROOT|STATE|LABEL|REASON`. ROOT is `same` (starts in the system running now) or
`restart` (the console restarts into it); STATE is `ok`, `retry` (did not start last time: can be chosen, never started by the countdown)
or `unavailable` (refused, with its REASON in plain words). The console's own system must be listed and `ok`, or the program refuses the
whole file (exit 3) and the console's own system starts. **Answers** (stdout, exit 0): `choice ID a|start|timeout`, `back` (from the
console's menu, B) or `action KEY` (a row of Recovery and Settings). Exit 15: asked to end (the console is shutting down), no answer;
exit 12: no screen; exit 3: the choices are not valid.

**What the person sees and does.** A panel titled "Choose a System" listing the console's own menu, CCS HOME and each owner's image by
its label; an option that cannot start now is shown as "(Not Available)" and the line under the list says why (for example "Its system
image is on USB storage that is not connected."). D-pad up/down moves (with the HOME's own repeat: 400 ms, then 110 ms); A or START
starts the focused option; on an option that cannot start, A is refused and the reason is shown at the top ("My SNES System cannot start
now. ..."): nothing is chosen. At start (the first round only), when the highlighted option can start, a countdown under the list says
"CCS HOME starts in 10 seconds. Press any button to stay here."; any button stops it. The countdown never runs for an option that cannot
start or that did not start last time; when the default or last choice cannot start now, the console's own system is highlighted and the
notice says why. When CCS HOME ends with a problem at start, the chooser comes back with "CCS HOME ended with a problem.", lists CCS HOME
as not available for the rest of that start and counts down to the console's own system, so an unattended console never stays there.
From the console's own menu, B goes back there. After a choice the TV keeps "Starting CCS HOME..." or "Restarting the
console into My SNES System..." until the next program draws.

**Default and last choice.** `selection=last`: the option last started and confirmed (also an owner's image, confirmed by its own start
scripts) is focused first and is the countdown's choice; `selection=default`: the registry's default. Failed options are skipped for both.
**Recovery and Settings** (the last row) opens a panel with the recovery entry **Start Your Console's Own Menu**, **Clear Start
Problems** (forgets the recorded failures, so a system that did not start before can start by itself again), **Show This Screen at Start: On/Off**
(`chooser`), **Start With: Last Used / <an option>** (`selection` and `default`, cycling; an owner's image only while this screen shows at
start) and **Countdown: 5/10/20/30 Seconds/Off** (`timeout`), plus how to reach Console Rescue (RESET held while switching POWER on with
the USB cable connected). Each row is carried out by `ccs-nucleus` itself (`set`: a new sealed registry checked before its one rename);
the panel then shows again on the same row with what was done (the recovery entry's row carries the console's own system's id, so the
program answers it as that choice). With the screen off at start, it still shows after leaving CCS HOME and from the menu entry CCS Nucleus
in the console's own menu, and from there it can be turned on again.

**Hand-over.** The program never opens the sound device. It opens the controllers without taking them from anyone, shows its first picture
before it opens them, ignores everything they report during the 400 ms after the open (decided after reading, so a report that waited
there is never a press), counts a button held through that time only after it is let go, and after a choice waits (at most 1.5 s) until
the choosing button is let go before it ends, so the next program does not see that press. It draws only into the page on the screen (no
page flipping, so ending never pans back to an older picture). Same root (CCS HOME or the console's own system from the console's own
root): the program ends, then CCS HOME starts (its player writes its saves before it ends; `ccs_run_home` syncs) or the console's start
continues. Different root (an owner's image, or from inside one back to the console's own system or CCS HOME): a sealed one-time request
`state/next`, `sync`, then hakchi's own `hakchi reboot`; the start stage uses the request up first. From an owner's image (if its menu shows
the entry CCS Nucleus) the chooser lists that image as the system running now (`same`: back to its menu) and everything else as restarts:
CCS HOME is never started inside another system's root. At shutdown, `S81ccs-nucleus stop` (`ui-stop`) leaves a flag in RAM so that
nothing more is started and asks a waiting chooser to end (TERM, never KILL); the program takes that signal from its first instruction
on (also while it is still loading) and ends without an answer. From the console's menu, hakchi's game-hang watch is fed while the
chooser waits, as for a game.

**Without the program** (no CCS HOME package, a package without `home/bin/ccs-home-armv7`, or a program that ends without an answer):
there is no chooser on the TV. At start, the registry's own choice in the console's own system starts (CCS HOME then fails, is recorded,
and the console's own system follows); otherwise the console's own system. The program and `ccs-nucleus` travel in different packages,
so a CCS HOME package from before the chooser (whose program answers `--choose` with an error) gives this same path, never a wrong start.

## 3. CCS Mod

| Owned file (journal `mod`) | Purpose |
|---|---|
| `/var/lib/hakchi/rootfs/bin/ccs-mod` | the launcher and its management commands |
| `/var/lib/hakchi/rootfs/etc/ccs/mod/{lib.sh,S81ccs-mod,entry.png,entry-small.png,VERSION,FILES}` | library, optional start template, menu pictures, records |
| `<menu folder>/CLV-H-CCSMD/{CLV-H-CCSMD.desktop,.png,_small.png}` | the menu entry **CCS HOME** (`Exec=/bin/sh /var/lib/hakchi/rootfs/bin/ccs-mod launch`) |
| `/var/lib/hakchi/rootfs/etc/init.d/S81ccs-mod` | only while the owner has the optional start on |
| `/var/lib/ccs/mod/config` (sealed, not journaled: settings) | `autostart=on|off` (`ccs-mod start-setting on|off` records it alone) |

The menu folder is the one hakchi's own `findGameStorage` names (its root menu `000` when present), or, without hakchi game storage,
the built-in games folder in hakchi's overlay. The optional start is explicit (`ccs-mod autostart on`), written only where nothing else
is, and delegates: CCS HOME first (with the startup picture), then rcS continues with the unchanged `S81clover-mcp` and every later
script, including other packages' autostarts. Windows offers it as an explicit choice, off by default, wherever CCS Mod is installed or
prepared, and turns it off the same way: Classic Cart Studio's install review runs the same command on the checked console (the transport
worker's `mod-start`, after CCS Mod's package is current and unchanged); the CCS Console Workbench (Prepare Console, and Console Restore's
change to CCS Mod) has no running console to ask, so its preparation image records the choice as CCS Mod's own setting (`ccsprep mstart`,
running the image's pinned `ccs-mod start-setting`), and CCS Mod's install, which hakchi runs at the next start, applies its setting with
that same `autostart on|off` (on a console where CCS Mod is already installed, a changed choice places CCS Mod's own package again for
that). CCS Nucleus consoles are not offered it: their start chooser decides what starts. Leaving CCS HOME always returns to the console's own menu ("Return to the previous
environment"); when CCS HOME fails, the console's start continues. On a console already running hakchi, installing CCS Mod writes files
to the data storage (hakchi's overlay, its game folder, `/var/lib/ccs`); it does not write a kernel, bootloader or the base system. A stock
console needs the separately guarded original backup and Hakchi preparation first (the existing preparation engine).

## 4. CCS Nucleus files

| Owned file (journal `nucleus`) | Purpose |
|---|---|
| `/var/lib/hakchi/rootfs/bin/ccs-nucleus` | dispatcher, chooser, import, registry and management |
| `/var/lib/hakchi/rootfs/etc/preinit.d/p7060_ccsnucleus`, `p7071_ccsnucleus` | the start stage (section 1) |
| `/var/lib/hakchi/rootfs/etc/init.d/S81ccs-nucleus` | the chooser at start |
| `/var/lib/hakchi/rootfs/etc/ccs/nucleus/{lib.sh,entry.png,entry-small.png,VERSION,FILES}` | library, pictures, records |
| `<menu folder>/CLV-H-CCSNU/…` | the menu entry **CCS Nucleus** (the chooser from the console's menu) |
| `data=/var/lib/ccs/nucleus/roots/<id>.hsqs` | the owner's imported images (data: kept unless removal is asked for) |
| `dir=/var/lib/ccs/nucleus{,/state,/staging,/roots}` | removed only when empty |

Import is additive: the PC uploads an image to `/var/lib/ccs/nucleus/staging/`, then `ccs-nucleus import` checks its size, `hsqs` magic
and SHA-256, moves it (one rename on the same storage: no second copy) to `roots/<id>.hsqs` (refused when anything else is there),
records it as data and adds the option to a new registry (checked before its one rename). `register-foreign` adds an image the owner
already keeps in hakchi's firmware folder **by reference** (hash and size): never moved, claimed or removed.

## 5. Ownership journal, `ccs-ownership/1`

`/var/lib/ccs/owned/<method>.own`, sealed:

```
schema=ccs-ownership/1
method=mod|nucleus
package=<the package's content id, from its VERSION>
file=<sha256> <mode> <console path>     a program, setting or menu file this method put there
data=<sha256> <console path>            the owner's data this method keeps (an imported image)
dir=<console path>                      a folder this method made
end=<seal>
```

Something is the method's only while a valid journal names it **and** its bytes still have the recorded SHA-256. Installing refuses
before hakchi copies anything when any target is someone else's or was changed (`preinstall`; then hakchi keeps no uninstall list for
the package: `no-uninstall`, b0030_package 59). After hakchi's own `transfer_default` copies the files, `postinstall` checks each copy
against the package's `FILES` list and records them. An update removes the earlier package's files that the new one no longer carries,
only while unchanged. Uninstall (`remove keep|remove-data`, also run by hakchi's `pack_uninstall` through the package's uninstall
script) removes only recorded, unchanged files; a changed one is kept and reported (`kept-changed`); data only with `remove-data` and
only while unchanged; folders only when empty. The package's uninstall script returns before the list hakchi appends to it
(b0030_package 65–82), because that list removes every file the package carried whether or not someone changed it.
Our own packages follow the same convention real packages use: `install` runs `transfer_default` itself and returns 1.

## 6. Transitions and removals (four distinct operations)

| Operation | Command | What changes | What never changes |
|---|---|---|---|
| Mod → Nucleus | install `ccs-nucleus.hmod` (not in charge while Mod is), then `ccs-nucleus take-over` | the registry starts as Mod behaved (`chooser=never`; default `ccs-home` when Mod's optional start was on, else `native`); one rename puts Nucleus in charge; Mod's own files go by its own remove; `hakchi pack_uninstall ccs-mod` then clears hakchi's list | games, saves, CCS HOME package and saves, hakchi settings, other packages, images |
| Nucleus → Mod | install `ccs-mod.hmod`, then `ccs-mod take-over` | one rename puts Mod in charge; Nucleus's files go by its own remove (keep): its p-files and start script disappear, so the start is the console's own again | the same, plus the registry, state and imported images (kept as data) |
| Remove the selector | `ccs-nucleus remove keep|remove-data` (or `hakchi pack_uninstall ccs-nucleus`, which keeps data) | Nucleus out of charge first (one rename), then its unchanged files | CCS HOME, Mod, everything of the owner and other packages |
| Remove the Mod | `ccs-mod remove keep|remove-data` (or `hakchi pack_uninstall ccs-mod`) | its unchanged files and menu entry | the same |
| Return the whole platform to its pre-preparation snapshot | **not here**: Console Restore (the existing preparation/return and restore engines) | | |

Only one method is in charge at a time (`/var/lib/ccs/policy`, sealed `ccs-policy/1`); both methods' start hooks do nothing unless
their own method is in charge, so a transition interrupted at any point leaves exactly one active policy (the rename is the commit
point) and at worst inactive files that a repeated transition finishes. Neither uninstall can promise byte-identical earlier state after
new gameplay, logs or saves; restoring a full selected snapshot is a separate operation.

CCS HOME's session text: its "leave" item is part of the CCS HOME package (`console_package.py` `LAYOUTS`). This branch adds layouts
`mod` ("Return to Your Console's Menu") and `nucleus` ("Choose Another System"), with the same console paths as `install`. The shared
installer's `build_plan` (`ccs_install.py`) takes the `mod` and `nucleus` layouts (the layout names the method it installs) and refuses the earlier `install` layout, whose leave item promised the start file hakchi deletes.
Classic Cart Studio stages a collection's package in both layouts (`console_package.derive_layout`: the same files but `session.json`),
reads at the check which method the console uses (`ccs_install.console_method`: CCS Mod, CCS Nucleus or neither; both, a changed or unknown
package, or an unreadable state are refused), and plans with that layout: CCS Mod on a console that has neither (its install policy). It
never adds CCS Nucleus or changes a console's method to install games; a console whose method changed after the check is refused.

## 7. Capacity inputs (`capacity.py`)

All measured: the built packages' exact files (`MANIFEST.json`), the menu entry, the CCS HOME package's `total_bytes`, each owner image's
file size, the data storage's available space and block size as the console reports them (`df`/`statfs` on `/var/lib`), every file
rounded to whole blocks. Rows: package, CCS HOME, each system image, staging (0: the .hmod is unpacked in RAM and images are moved, not
copied), rollback (the previous CCS HOME copy during an update) and the engine's save reserve (`ccs_install.RESERVE`, 8 MB). The result
says what is needed, what is available and any shortfall. It makes no claim about how many boot choices fit or how fast either method
starts; only final images measured on the console can show that.

## 7a. Delivery from the Workbench: hakchi's transfer folder

The Workbench never runs hakchi's package code itself and never writes into hakchi's overlay. In the preparation image's RAM session
(`tools/console-backup/ram/ccsprep`) it only places files in `$installpath/transfer/` on the data storage (`/hakchi/transfer/` there).
At the console's next normal start hakchi's own `p7080_hmods` (pinned `p7080_hmods` 3-31; `b0030_package` 43-128) applies the folder:
`packs_uninstall` on the names in `transfer/uninstall`, then `pack_install` on every `*.hmod`, then `transfer_path`, `rm -rf` of the
folder, `save_config` and `reboot_kexec`. So CCS's own install and uninstall scripts run exactly as for any hakchi package (the same
`transfer_default`/`return 1` convention), in hakchi's start stage (`$mountpoint` = `/newroot`, which the scripts already take as
`CCS_ROOT`).

| ccsprep step | What it does | Who uses it |
|---|---|---|
| `mstate` | read-only (data storage mounted `ro,noload`): hakchi's version, `hakchi/hmod/uninstall-*` names, CCS's method in charge (the sealed `ccs/policy`), each file waiting in `hakchi/transfer/` with its SHA-256 | `Preparation` (with a chosen method) and `MethodEngine`, at the plan and again at the confirmation |
| `method NAME SHA` | the image's own pinned `NAME.hmod` (ccs-mod or ccs-nucleus; `build_images.py` builds them from this folder and pins `METHOD_SHAS`) written to `transfer/NAME.hmod` (new name, rename, read back after mounting again read-only); the same bytes already there: `ok already` | Preparation (before the kernel step) and the change between methods |
| `unlist NAME` | `transfer/uninstall` containing `NAME` written the same way | removal and the change between methods |
| `mstart on|off` | CCS HOME at power-on, the owner's choice: the image's own pinned CCS Mod program (unpacked in RAM from its `ccs-mod.hmod`) records CCS Mod's setting (`start-setting`, in `ccs/mod/config` only, never hakchi's overlay); read back. CCS Mod's install at the next start applies it (`autostart on|off`) | `Preparation` (CCS Mod), `MethodEngine` change to CCS Mod (before the method step) |
| `rput ID SIZE SHA` | an owner's system image (Console Composer's Browse for Firmware) received straight to `/var/lib/ccs/nucleus/staging/ID.hsqs` under a new name, kept only when its size, SHA-256 and SquashFS magic match and it fits beside 8,000,000 bytes; read back | `FirmwareEngine` add |
| `rimport ID SHA SIZE LABEL` | the image's own pinned `ccs-nucleus import` (unpacked in RAM from its `ccs-nucleus.hmod`): one rename into `roots/ID.hsqs`, Nucleus's data record, a registry choice; read back | `FirmwareEngine` add |
| `rdrop ID SHA` | only when `roots/ID.hsqs` is Nucleus's own unchanged file with SHA: `ccs-nucleus forget ID`, then the file and its record (the pinned library); an image in hakchi's firmware folder (a referenced choice) is never touched | `FirmwareEngine` remove |
| `hput COPY SIZE SHA BYTES` | CCS HOME's package (`CcsBackup Engine/HomePackage.cs`): one archive of SIZE bytes with that SHA-256 received into RAM (refused unless RAM holds it and BYTES plus 8,000,000 bytes are free on the data storage; only plain `pkg/` and `rec/` members), the attempt's and the copy's records written first, the files unpacked into `ccs/staging/COPY` and moved in one rename to `ccs/pkg/COPY`, every file read back (storage mounted again read-only) against the copy's record; the copy in use is not touched | `Preparation` (with a method), `MethodEngine` change |
| `huse COPY PRIOR` | the switch: `ccs/pkg/current` replaced in one rename, only while it still names PRIOR and COPY reads back complete; the attempt's record removed; read back | the same |

Order of a change (`MethodEngine`, Console Restore): the new package first, then the removal list for the old one. An interruption between
the two leaves both installed with the old one still in charge (CCS's postinstall takes charge only when no method is), never neither; a
new connection sees the waiting package as its own and finishes with one write. Anything else waiting in the transfer folder is refused.
Removal of a method is the removal list alone; the method's own uninstall script removes only its unchanged files and keeps its data.
`test_nucleus.py --only transfer` runs hakchi's own preinit with these files in the transfer folder (install Mod; change to Nucleus;
remove Nucleus) under qemu-arm.

Edge cases from the pinned files: with `cfg_boot_stock=y` hakchi's `p7000_preinit` returns before `p7080_hmods`, so a waiting package
is installed only after the owner turns that off; a USB drive with a `hakchi/transfer` folder is used instead of the internal one for that
start (`p7080_hmods` line 4), and the internal one is then applied at a later start.

An owner's system image is NOT placed through the transfer folder: hakchi's `pack_install` unpacks a package in RAM (`$temppath`), and an
image can be larger than the console's memory; `rput` writes it straight to the data storage instead. It never goes to hakchi's
firmware folders, so it starts only when chosen (section 2).

**CCS HOME's own package is required for either method to show anything** (both launchers refuse without `/var/lib/ccs/pkg/current`).
It is not part of these packages: the Workbench sends it in the same session after the image started (`hput`, then `huse`; the FEL
image limit applies to the image only), in the layout Classic Cart Studio's installer uses on a running console (`pkg/<version>.<attempt>`,
`pkg/current`, `install/v-<copy>.json`, `install/attempt-<attempt>.json`, `data/IDENTITY.json`), so the app and the Workbench read each
other's copies the same way. The package is the release-built one each build carries for the method's layout (`build_home_package.py`:
the Included Games, the app's own console package builder, embedded beside the console programs as `ram/ccs-home/`), or a package
folder the window names. A change between methods places it again in the new layout (its leave item names where it goes back to).

## 8. Third-party packages

CCS never modifies, patches, blacklists or repackages another package. hakchi's `pack_install`/`pack_uninstall` run unchanged; the
pinned preinit, `p*` hooks, `chmenu`, `uistart`/`uistop`, `remote-exec` and every other start script run unchanged; CCS adds files with
its own names only. The tests install a stand-in package (written for the tests: `transfer_default` then `return 1`, a `p8000` preinit
file and an `S95` autostart) with hakchi's own code, with and without CCS installed, and compare the results.

**Real packages that remain hardware-unverified** (and are not downloaded, included or tested here; the owner obtains them from their
authors' own release pages, and public claims will name only the exact versions and hashes actually tested in W04/W05):

| Package | Where the owner gets it | Why it matters |
|---|---|---|
| KMFD RetroArch hmod with the FCEUmm core wrappers (exact release to be chosen by the owner) | KMFD Mod Hub / its release page | shared libraries, `uistop`/`uistart`, remote execution, menu return |
| KMFD Mod Hub: a few representative packages (exact list chosen at W04) | KMFD Mod Hub | overlay files, preinit hooks, autostarts |
| DefKorns Options Menu v2.1.1 | DefKorns' GitHub releases | controller daemon, chords, framebuffer pause (`uipause`), power input |
| DefKorns Theme Selector v3.0.0 | DefKorns' GitHub releases | the native menu's resources, `chmenu`, preinit hooks |
| any custom autostart package the owner already has | the owner | must keep starting unchanged after S81ccs-* |

## 9. Tests

```
python tools/console-nucleus/test_nucleus.py --hakchi-release <the pinned Hakchi2 CE 3.9.3 folder or its portable .zip> [--only ...] [--keep]
                                             [--chooser <ccs-home-armv7: scripts/wsl_build.sh arm-musl-device>] [--fonts <the player's menu fonts>]
python tools/console-simulator/test_chooser.py --build <a wsl_build.sh build folder> [--fonts DIR] [--out DIR]
```

`--only chooser` runs the start sequence with the **real** chooser program (the ARM build, under qemu-arm, on a test framebuffer file and
a controller FIFO in the console's 16-byte event records): countdown, refusal, restart into an owner's image and back, Recovery and
Settings, B from the console's menu, shutdown while it waits, no CCS HOME package. The other parts use a stand-in chooser that answers
from the test's list. `test_chooser.py` tests the program itself (section 2a).

Windows with WSL Ubuntu and the project's qemu-arm (`~/.cache/ccs-sim/toolchain`), or Linux. Part 1 runs on the PC (packages,
formats, static rules, sort order, capacity). Part 2 runs hakchi's own preinit, `pack_install`, `pack_uninstall`, rcS and `hakchi`
command, and every CCS script, on the pinned image's BusyBox 1.29 under qemu-arm, against a stand-in console built from the pinned
hakchi files (section 1); the console's storage, display and power tools are logging stand-ins and flash tools are tripwires. The test
copies of hakchi's files differ from the pinned ones only in RAM-root paths, checked by mapping them back. The qemu runs refuse to run as
root. A full run takes about 40 minutes (every console start runs hakchi's whole preinit under qemu); 99 checks on November 15, 2026.

`--only chain --chain-data TAR` runs the installation chain end to end: TAR is the data storage as the preparation image's own `mstart`,
`method`, `hput` and `huse` steps left it with the shared engine's own archive and the Workbench plan's choice of CCS HOME at power-on
(`CcsBackup.Tests --only home-chain` with `CCS_HOME_CHAIN_OUT`, driving the built Workbench's worker; then `test_recovery_images.py
--home-archive DIR --export-data TAR`). hakchi's own start installs CCS Mod from its transfer folder and CCS Mod's install applies the
recorded choice; CCS HOME's entry and CCS Mod's optional start each start the package's own 32-bit ARM console player
from `pkg/current` (binfmt_misc in a user namespace of the test's own; the player's documented test seams stand in for the display and
controllers), which shows the package's HOME. It is not part of a default run.

## 10. What is verified, believed and unknown

**Verified** (pinned files; tests on the pinned BusyBox under qemu-arm; not the console):
- Hakchi 3.9.3's start sequence and the line references in section 1; `p7060`/`p7071` run in hakchi's own joined preinit between
  `p7020_usb`/`p7070_firmware` and `p7070_firmware`/`p7072_overlayfs`; the owner's `cfg_firmware` is what `pffff_config` saves.
- hakchi removes an overlay `S81clover-mcp` at every start (`script/removed` 17); `S81ccs-*` are not in that list.
- Both packages install and uninstall through hakchi's own `pack_install`/`pack_uninstall`; the stand-in third-party package behaves the
  same with and without CCS.
- The chain (`--only chain`): the archive the built Workbench's worker sent, placed by the image's own steps, then hakchi's own start
  installing CCS Mod, and the real console player from that package showing its HOME (under qemu-arm, not the console).
- Every behavior listed in `test_nucleus.py` (failure, missing image, changed image, missing external dependency, hakchi's own
  unmountable-image path, interrupted registry/request update, deterministic return, no loop, transitions, removals, ownership).
- The packages are reproducible and contain only Classic Cart Studio's own files.

**Believed** (design reasoning; needs the owner's hardware runs):
- The native menu runs a `.desktop` entry's `Exec` line like a game and hands over the screen; `Icon=/var/games/<code>/<code>.png`
  resolves (hakchi's `linkGames`); hakchi2 CE's own PC-side game sync may replace the menu folder and drop CCS's entry (then `status`
  shows it missing and `entry add` puts it back).
- `/tmp/.heartbeat` keeps `game_hangup_detect.sh` from restarting the menu under CCS HOME; the native menu has saved before it hands
  over the screen.
- hakchi's `/var` RAM folder made at the start stage is the running system's `/var` (as hakchi's own `/var/version`).

**Unknown until the owner's W03–W05 runs on real hardware:**
- Everything at runtime on the console: the native menu's entry handling, screen/audio/input handover, the POWER switch and RESET while
  CCS HOME or the chooser is on the TV, the real timing, and whether the console's own `S81clover-mcp` start blocks rcS (while CCS HOME
  runs from `S81ccs-*` at start, the later start scripts, such as hakchi's USB network and `remote-exec`, wait until it is left; this
  matches the existing installer's behavior, and a measured alternative may be needed).
- The chooser on the real TV and controllers (section 2a): its drawing speed on the console's Cortex-A7, the controllers' settling and the
  hand-over to the console's own menu, CCS HOME's player and a restart, the menu entry CCS Nucleus inside an owner's image, and the
  countdown with the console's real start timing. Its logic, pictures and console mode are verified on the PC (`test_chooser.py`: the
  Windows, x86-64 Linux and 32-bit ARM builds under qemu-arm, and the console mode with a test framebuffer and controller FIFO) and in
  the start sequence under qemu (`test_nucleus.py --only chooser`: the real ARM program called by the pinned hakchi start scripts).
- The real third-party packages in section 8 and any combination of them; real owner system images, including the owner's SNES Classic
  system on this NES Classic (D-190): that hakchi 3.9.3's kernel mounts and starts an xz SNES system image, the SNES menu and games with
  an SNES Classic controller, hakchi's own HOME menu save swap, suspend points and battery saves, the chooser and the restart back
  (W05 and W05S in the qualification packet). The image itself is checked on the PC (the owner's own NES Classic xz dump and its gzip
  system part read the same; SNES fixtures built by squashfs-tools); no real SNES Classic image has been read yet.
- Capacity on the owner's console (its measured free space and block size are inputs; nothing is assumed).
