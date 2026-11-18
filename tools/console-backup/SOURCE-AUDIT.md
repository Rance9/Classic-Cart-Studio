# Source and license audit: third-party binaries the CCS Console Workbench and Classic Cart Studio carry

Legally required notices are in `NOTICE` and `THIRD_PARTY_NOTICES.md`.

Dates: first pass 2026-11-15; this revision 2026-11-15 (the per-binary details, the trace of what each console program runs, the
xdelta3 identification, the corrected source pins and notices, and the four console programs trimmed of programs they never run). No console,
USB, FEL, driver or device was involved. Third-party binaries were read as data; the only programs run were the console programs' own
tools under qemu-arm in the existing test harness and CCS's own build and test scripts.

This document lists facts and gaps. It does **not** claim GPL or other license compliance, it does not decide what
may be distributed and it waives nothing.

## Evidence labels

- **verified**: I checked the bytes, the source tree or the page myself; the method is given.
- **believed**: taken from documentation, a commit message, a readme or another party's statement; the source is cited.
- **unknown**: not established.

Units are decimal (1 MB = 1,000,000 bytes). Hashes are SHA-256 unless marked otherwise. "Not identified" means not found in the places
listed; it never means that nobody has the source.

## 0. Status at a glance

| Item | What | Status after this revision |
|---|---|---|
| G1 | Linux kernel 3.4.113.29-madmonkey and its modules | **open**: exact source not identified (sections 8.1, 8.3) |
| G2 | `nand.ko` | **open**: 42 defined symbols in no examined source (code difference established; section 8.2) |
| G3 | `rtl8188fu.ko` | **open**: source not identified (section 8.3) |
| G4 | u-boot board-configuration block | **open**: rights/provenance question, separate from rebuilding (section 8.4) |
| G5 | u-boot exact build `32c3df5-dirty` | **open**: boot command differs from the public patch (config difference established; section 8.5) |
| G6 | fes1's Allwinner DRAM object | **open**: no source or license text found; inherent to fes1 (section 8.6) |
| G7 | sntool 5833d00 | **open**: functions absent from the public source (code difference established; section 8.7) |
| G8 | BusyBox v1.29.0.git | **open**: snapshot commit and configuration not identified; a CCS rebuild was tried and is not straightforward with the pinned toolchain (section 8.8) |
| G9 | xdelta3 | **identity closed** (section 3.14): xdelta 3.1.1, Apache-2.0, packed with UPX 3.94; now a notice item (section 8.9) |
| N1 | notices missing from `NOTICE` and `THIRD_PARTY_NOTICES.md` | **corrected** in this revision (sections 8.10, 9) |
| N2 | LibUsbDotNet source pin (`91e8439` named, `5dd91a2` is the package's source) | **corrected** in CCS's notices and release workflows; the worker's source zip still carries the submodule copy (section 8.11) |
| N3 | "source archive of tag v3.9.3 with its submodules" read as the source of the bundled package | **corrected** wording (sections 8.12, 9) |
| N4 | the Workbench's draft release attached an incomplete engine source archive | **corrected** in the draft workflow (sections 8.13, 9) |
| N5 | "or any later version" for parts derived from GPL-3.0 projects | **open** license question (section 8.14) |
| Z1 | the app's bundled `hakchi2-ce-3.9.3-portable.zip`: its own prebuilt files and non-software content | **open**: exact sources not identified; no license in the package for the non-software content (sections 3.21, 8.15) |

How the open gaps bear on releases is in section 10.

## 1. Scope: what carries these binaries

Both products embed the same five console programs: `CcsBackup.dll` carries them as resources (`CcsBackup.csproj`, `EmbeddedResource
Include="$(CcsRamImages)\ccs-*\*"`), the standalone `CCS-Console-Workbench.exe` embeds `CcsBackup.dll`, and Classic Cart Studio for
Windows installs it beside its host (`release.yml`, "Build Classic Cart Studio for Windows"). **Whatever any one console program carries,
both products carry.** **verified** (project files and workflow).

| Carrier | Contents | In the standalone | In the app | Evidence |
|---|---|---|---|---|
| Five console programs (`ccs-backup`, `ccs-restore`, `ccs-prepare`, `ccs-rescue`, `ccs-files`) | each: `ccs-<variant>-boot.img` (Hakchi2 CE 3.9.3's recovery kernel, byte-identical, with a changed ramdisk), `fes1.bin`, `uboot-sd.bin`, `MANIFEST.json`, `SHA256SUMS`; sent to the console's memory over FEL, never written as such | yes | yes | **verified**: `build_images.py`; every image's kernel `920ffa42…dbc801`, fes1 `d78688b7…3b491c`, u-boot (SD) `3bfc4a56…f86b5b` |
| `ccs-prepare` only | every file of the pinned `hakchi.hmod` except `install`/`uninstall` under `/ccs/prep/hmod/` (118 kernel modules, the Realtek firmware, `boot/boot.img`, `boot/uboot.bin`, `bin/*`, `lib/*`), CCS's `ccsprep`, `ccsboot` and the CCS Mod/Nucleus packages | yes | yes | **verified**: MANIFEST; section 7 shows what preparation **installs on the console** |
| `ccs-rescue` | adds `ccsrescue` and `ccsboot` | yes | yes | **verified** |
| CCS Mod / CCS Nucleus packages | CCS's own scripts and pictures only (section 3.20) | yes (inside `ccs-prepare`) | yes | **verified** |
| Windows engine | FelLib-derived C# (`CcsConsole.dll`), `LibUsbDotNet.LibUsbDotNet.dll` | embedded | installed beside the host | **verified** |
| Standalone native host | par2cmdline 1.4.0 (libpar2), the HOME renderer with stb_image, the mingw-w64 runtime, compiler-rt, libc++ | yes | `ccs-par2.dll` only | `NOTICE` |
| **Z**: `hakchi2-ce-3.9.3-portable.zip`, unmodified, in `_internal\packages` | Hakchi2 CE's program and every prebuilt file it ships, including `basehmods.tar` (with the same `hakchi.hmod`), `hakchi.exe` (with fes1 as a resource), `tools/arm/*.static`, `tools/mksquashfs`, `user_mods/*.hmod`, `driver/classic_driver.exe`, `data/*.tar` | **no** | yes | **verified**: `release.yml` fetch step; zip listing (section 3.21) |
| **A**: `hakchi2-ce-3.9.3-source.tar.gz` (`dc6e6a47…5038`), attached to each app release | Hakchi2 CE tag v3.9.3 with its submodules (its own `SOURCE-MANIFEST.txt`) | no | attached to the release | **verified**: hashed the local copy; listing (section 2) |
| Console transport worker (app only) | its own build of `CcsBackup.dll` **without** the console programs | no | yes | `tools/console-worker/SOURCE.md` |

Every image's ramdisk is Hakchi2 CE's recovery ramdisk with these changes: `/init` patched, `/hakchi/init` replaced by CCS's `ccs-init`,
`S92rndis` removed, CCS files added under `/ccs`, and (from this revision, backup/restore/rescue/files only) the programs those four never
run left out (section 7.3). **verified**: `build_images.py`, manifests.

## 2. Provenance chain of the pinned inputs

| Step | Fact | Label and method |
|---|---|---|
| Portable package | `packages\hakchi2-ce-3.9.3-portable.zip` = `46520c8b9a3c768b5ee7c2cb4878adc11e3da1869aae281000d75012b7e9f122` (75,518,132 bytes), GitHub release `v3.9.3` asset, published 2022-03-18 | **verified**: hashed (read-only); release API lists the asset and size |
| `basehmods.tar` | `7f7a6b8ef26783753b63f60bdf40ba8bd9278ae7ca9d89715b9103aa0b7dc77c`, built by the Hakchi2 CE pre-build step `busybox tar -cvf basehmods.tar -C mods/hmods .` | **verified**: hashed; `hakchi_gui.csproj` at `v3.9.3` (commit `25bfcfd04b8ed12f7d513dc87c2a0f7096d38ebc`) PreBuildEvent |
| `hakchi.hmod` | `003994b46504959895f40b0b9e91b16951ef4a638feb6312314c8fc4fdbff592` (9,340,382 bytes) is **not built from the Hakchi2 CE source**: its build downloads it (`tools/downloadfiles`) from `https://github.com/TeamShinkansen/Hakchi2-CE/releases/download/supplemental/hakchi-v1.0.4-126.Fri.03.Apr.2020.11_12_31.AM.GMT.hmod`; that asset is byte-identical to the pin | **verified** |
| Its version | `var/version`: `hakchiVersion='v1.0.4-126'`, `kernelVersion='3.4.113.29-madmonkey'`, `bootVersion='1.0.3'` | **verified** |
| `boot.img` | `9e921c326be65aa4a3100270e86fdb15fe4b8563d586941b9f6c640eef064e4c` (4,585,472 bytes; Android boot image v0, page 2,048, kernel 1,968,736 bytes at 0x40008000, xz ramdisk 2,612,272 bytes) | **verified**: parsed |
| `fes1` | 12,656 bytes `d78688b70e022c53e6a49a18994f59946fda7791d22bc0e6409ff630553b491c`, found once in `hakchi.exe` (`8e1fe795…391a2b`) and identical to Hakchi2 CE `data/fes1.bin` at `v3.9.3` (added 2017-01-07 by ClusterM, last changed 2018-01-26) | **verified** |
| `uboot.bin` / `uboot-sd.bin` | `5cfacb297b962a388d947e3be553692bb1e9df9edc1125ca5a769166d3a89564`; the SD variant (`3bfc4a56417aade7d3fa3afcf3d2344b07ea38d9f42de504eda3014844f86b5b`) has its last two 32-bit words swapped, as hakchi's memboot does | **verified** |
| Source archive **A** | `dc6e6a4704a9bdb1ca8a0deb746dd41f660b4076ba99b84160af5164f36e5038`, 4,646 entries: Hakchi2 CE's PC program and its submodules (CueSharp, FelLib, LibUsbDotNet `91e8439`, ProgressODoom, SSH.NET, SpineGen, tiny7z, `driver/source` libwdi `6d379a3`). It contains no `hakchi.hmod`, kernel, module, sntool, BusyBox or u-boot source; it does contain prebuilt binaries as Hakchi2 CE's repository does (for example `data/fes1.bin`, `tools/busybox`, `tools/busybox.exe`) | **verified**: hashed the local copy, listed it |

Consequence (**verified**): the attached archive **A** is the source of Hakchi2 CE's own program. It is not the source of `hakchi.hmod`, its
kernel, modules, `sntool`, BusyBox, u-boot or fes1, nor of the other prebuilt files the portable package ships. The earlier wording of
`THIRD_PARTY_NOTICES.md` ("a source archive of that tag with its submodules is attached") was true but read as more than it is; corrected in
section 9.

## 3. Per-binary findings

### 3.1 Linux kernel (zImage in every image's `boot.img`)

- What: ARM zImage `920ffa42a279c7f6e3388114b1e4295da23a9e4d1477dc7137d25ad472dbc801` (1,968,736 bytes), xz-compressed payload
  (decompressed 5,627,948 bytes, `8c11676d48b640ac14af8f7811e02dd3e5169b8b68cc7016ecb51c77d5bbd9d6`). Banner
  `Linux version 3.4.113.29-madmonkey (madmonkey@hakchi-buildbot) (gcc version 4.9.3 (Buildroot 2015.11.1) ) #1 SMP PREEMPT Fri Mar 20 11:15:28 GMT 2020`.
  No embedded config (`IKCFG_ST` absent). **verified**.
- License: GPL-2.0 (`COPYING` with Linus's syscall note). **verified**: `COPYING` in `madmonkey1907/sun-nontendocm-kernel`.
- Exact source: **not identified**.
  - `https://github.com/madmonkey1907/sun-nontendocm-kernel`, HEAD `f8c6e0bda942c0033fb87b87a4c33f486da82192` (2019-05-03), has
    `EXTRAVERSION = .24-madmonkey`; the repository's last push is 2019-05-03. **verified**.
  - `https://hakchi.net/hakchi/bin/sun-nontendocm-kernel.tar.gz` (343,392,103 bytes, `15d03dd4eb826d839351e0262e43d3ac038dddce47d40cfe1ccd63668cb10cd1`,
    streamed, not stored) is the same repository at `9dba1d9e38e97bb78c8eaf5b3b6cf5422a76380a` (2018-08-14), also `.24-madmonkey`. **verified**.
  - Nintendo's base kernel in `NintendoEntertainmentSystemNESClassicEdition_OSS.zip` (`linux-9ed0e6c8612113834e9af9d16a3e90b573c488ca.tar.gz`,
    member `a9ae0123473c2004955c3a5d45c97c1df724c0fb0ab79d0c1ab8348cf600bb2a`) is 3.4.112. **verified** (streamed).
  - No public tree, tag or archive of `3.4.113.29-madmonkey` (March 2020) was found (GitHub repository listings and commit search,
    hakchi.net listings, web search). **unknown** whether one exists elsewhere.
- What differs, as far as established: the version string (`.29` against `.24`) is a banner and proves nothing by itself. Code differences
  in the kernel image itself are **not established**. For the NAND driver module they are (section 3.2).

### 3.2 Kernel modules

Ramdisk of every image: `nand.ko` and `sd_mod.ko`. `ccs-prepare` (and the console after preparation): 118 modules under
`lib/modules/3.4.113.29-madmonkey/`. All 120 carry `vermagic=3.4.113.29-madmonkey SMP preempt mod_unload ARMv7`. License tags: 112
"GPL", 2 "GPL v2", 6 "Dual BSD/GPL". **verified**.

| Module group | Nearest public source | Label |
|---|---|---|
| In-tree drivers (USB storage, HID, Bluetooth, mac80211/cfg80211, rt2x00, rtlwifi, NFS/CIFS, sound, overlayfs, zram, `sd_mod`…) | `.24` tree `f8c6e0b` | **believed** close; exact `.29` source unknown |
| `nand.ko` (377,076 bytes, `5d67ca0977370a04f46edbf1c02aa1dcbb2fc964781775d906125c40eb7d29d5`; the NAND driver every image loads) | none matches. Of its 997 defined symbols, 42 (for example `nand_read_reclaim`, `write_panic_data`, `ZettaNandTbl`, `critical_dev_nand_read`, `NAND_WaitDmaFinishNoInt`) appear in **none** of: the `.24` tree's `drivers/block/nand` source, Allwinner's prebuilt `libnand_sun8iw5p1` archive in that tree, the hakchi.net `.24` snapshot, or Nintendo's 3.4.112 kernel. 708 of the blob's 748 symbols also occur in `nand.ko`, so whether the blob is linked is **unknown** | **verified** (symbol tables parsed; identifiers searched); an actual code difference against every examined source |
| `mali.ko` (DX910-SW-99002-r3p2-01rel1, "GPL") | `modules/mali/` in `.24` | **believed** |
| `cedar_ve.ko`, `ss.ko`, `sunxi-keyboard.ko` (Allwinner, "GPL") | `.24` tree | **believed** |
| `clovercon.ko` (Christophe Aguettaz, Nintendo NERD, "mod by Cluster") | `.24` tree `clovercon/`; Nintendo OSS zip lists `clovercon-km-6b883396add00817aa112e0ec4dbc112def803e8.zip` | **believed** |
| `8188eu.ko` (`v4.3.0.4_11485.20140519`) | `drivers/net/wireless/rtl8188eu` in `.24` and in Nintendo's tree | **believed** |
| `8192eu.ko` (`v4.4.1_17696.20160509_BTCOEX20160412-0042`) | `https://github.com/jeremyb31/rtl8192eu-linux-driver` `1addeae476fe3022395f638a95182c9be3135e1c` (nested in the hakchi.net snapshot; same version string) | **verified** for the 2018 snapshot; **believed** for the 2020 build |
| `8812au.ko` (`v4.3.20_16317.20160108`) | `https://github.com/diederikdehaas/rtl8812AU` `ee592f8c0685b290ad3160cbbefb1c32d4cc5d0d` (same version string) | same |
| `rtl8188fu.ko` (`v4.3.23.6_20964.20170110`, `7634c981…ca7f52`) | not in any examined tree; community copies of this vendor version exist (for example `kelebek333/rtl8188fu`), the exact source used is not identified | **gap G3** |

### 3.3 Firmware `lib/firmware/rtlwifi/rtl8188fufw.bin` (ccs-prepare; installed on the console)

- 21,020 bytes, `e6189d6d7032b83f0ed4284629c43db66262449a866f33fd329e611239f62e1f`, byte-identical to `rtlwifi/rtl8188fufw.bin` in
  `linux-firmware`. **verified**.
- License: Realtek's redistribution license (`LICENSES/LICENCE.rtlwifi_firmware.txt`, `a61351665b4f264f…`): redistribution without
  modification, reproduce the notice and disclaimer, no reverse engineering. **verified**. Reproduced in `NOTICE` (this revision).

### 3.4 U-Boot (`uboot.bin` burned to boot1 by preparation; `uboot-sd.bin` loaded over FEL by every session)

- What: Allwinner-format u-boot (total 761,856 bytes, code 0x90000 = 589,824 bytes, run address 0x47000000). Banner
  `U-Boot 2011.09-rc1-00000-g32c3df5-dirty (Nov 19 2019 - 02:26:33) Allwinner Technology`, compiler `arm-linux-gcc.br_real (Buildroot 2015.11.1) 4.9.3`.
  The add_hash slot at 0x500 holds `32c3df5dff315fd5bdca115aa85e5c4d6befa46b`. **verified**.
- License: GPL-2.0 for U-Boot (`COPYING` in Nintendo's `r16-uboot` tarball). Allwinner-authored files read (`fes1_main.c`, `fes_head.c`,
  `nand_phy.c`) carry only an Allwinner/SoftWinners copyright line. **verified**.
- Base source (public, **verified**): `r16-uboot-fc3061df4dbd4153819b2d2f141d82b88fea51cf.tar.gz` in Nintendo's
  `http://data.nintendo.co.jp/oss/NintendoEntertainmentSystemNESClassicEdition_OSS.zip` (member `3afd5abcebc7e18b4de2923c51f48eea2f4cca5d7bcba589f44dac51c7ae283f`).
  Hakchi's recipe (`r16-uboot/readme.md` and `r16-uboot.diff` in `madmonkey1907/hakchi`, still at master `fd525ac`): take that tarball,
  apply the patch, build, then "use sntool split/join to cut script.bin from stock uboot and paste into freshly compiled one".
- Exact source: **gap G5**. The build is from a working tree at `32c3df5d…` with local changes; that commit is in no repository a GitHub
  commit search finds (re-checked in this revision: full-hash search, 0 results). Its default boot command
  (`ext4load sunxi_flash 4:0 43800000 hakchi/boot/boot.img; …; efex`) differs from the public patch's
  (`sunxi_flash phy_read 43800000 30 20;boota 43800000`): an actual configuration difference, not only a banner. **verified**.
- Board-configuration block (bytes 0x90000-0xBA000, 172,032 bytes, `c0944183c90eb413ce003dfe796dfacf24ebd09557ec18762064d2956f74eca0`):
  Allwinner `script.bin` data (`product`, `dram_para`, `card0_boot_para`, `axp22_*`…). **verified**. Per the hakchi recipe it is cut from the
  console's stock u-boot (**believed**; not compared with a stock boot1 dump). No `.fex` source or license found; the r16-uboot tarball has
  none. **Gap G4**: a provenance/rights question about stock board data, separate from reproducing the build.
- Other contents: the top `Makefile` links prebuilt `openssl/libcrypto.a`, `libssl.a` (no version or source in the tree); no OpenSSL
  identifiers found in `uboot.bin` (**believed** not linked). The sun8iw5 NAND driver in u-boot is built from source. **verified**.

### 3.5 fes1 (`fes1.bin`, the FEL DRAM-initialization stage loaded by every session; never written to the console)

- What: Allwinner eGON.BT0 image, header version `3.0.0`, 12,656 bytes; embeds `fc3061df4dbd4153819b2d2f141d82b88fea51cf` (the r16-uboot
  tarball's commit). **verified**.
- License: tree `COPYING` GPL-2.0; Allwinner source files with a copyright line only. **verified**.
- Source: the public r16-uboot tarball plus hakchi's patch (**believed**: the embedded commit and the recipe agree). For `CONFIG_ARCH_R16`
  fes1 links `arch/arm/cpu/armv7/sun8iw5/dram/libdram-riot` (a prebuilt 43,350-byte ELF object, Linaro GCC 2012.02 4.6.3, no license
  text); 82 of its 152 printable strings occur in `fes1.bin`. **verified**. No source for that object exists in the tarball or anywhere
  found. **Gap G6 (inherent to fes1)**.
- Background: mainline U-Boot has an open-source A33 DRAM initializer (`arch/arm/mach-sunxi/dram_sun8i_a33.c`, GPL-2.0+). **verified**
  that it exists; **unknown** whether it works on the NES Classic's R16.

### 3.6 sntool (`/bin/sntool` in every ramdisk and in the hmod; links `sunxi-flash`, `sunxi-part`, `decodepng`, `clover-mcp`, `m2engage`)

- What: ARM PIE, 793,728 bytes, `f7eeecdeb94863364593fe9da55f499bfa13915a74fa2b2049e18209a65f57e9`, glibc-dynamic, Buildroot 2015.11.1
  GCC 4.9.3; banner `sntool 5833d00 (dirty) 2020-03-30 01:41:34 UTC`. The hmod's `bin/sntool` is the same file. **verified**.
- License: GPL-3.0 (hakchi `LICENSE`). **verified** for the repository; **believed** for this build.
- Source: **gap G7**. `5833d00` is in neither `madmonkey1907/hakchi` (both branches; last push 2019-05-16) nor `DanTheMan827/hakchi`. The
  public `sntool/sntool.c` (`fd525ac`, 2019-03-23) has `sunxi-flash` read/burn boot0/1/2, `phy_read`, `nandinfo`, `ramdisk`, `check`,
  `devmem`, but **no** `sunxi-part`, `decodepng`, `clover-mcp` or `m2engage`, which the 2020 binary contains: an actual code difference.
  **verified** (string comparison). `ccsboot.c` records the same gap.
- CCS's use: section 7.

### 3.7 BusyBox (`/bin/busybox` and `/bin/sh` in every ramdisk)

- What: `BusyBox v1.29.0.git (2018-04-10 20:38:05 GMT)`, 650,604 bytes, `d5c96520b02920b331ec8b7f232c662e53f97add208741f65500b0512bf87d44`,
  glibc-dynamic (libc, libm). Its applet list (329 applets, read with `busybox --list` under qemu-arm in this revision) is the observable
  part of its configuration. **verified**.
- License: GPL-2.0 only (`LICENSE` at upstream tag `1_29_0`). **verified**.
- Exact source: **gap G8**. "1.29.0.git" is an upstream development snapshot from before the 1.29.0 release; the commit and the `.config`
  are unknown. hakchi's public repository refers to a prebuilt `busybox-armv7l` 1.21.1 (`3rdparty/busybox.url`), not this binary.
  **verified**. No code difference is established (nothing to compare without the snapshot and configuration).

### 3.8 glibc 2.22, libgcc_s and util-linux libraries (every ramdisk, `lib32/`)

- glibc 2.22 (`ld-2.22.so`, `libc-2.22.so` "GNU C Library (Buildroot) stable release version 2.22", `libm`, `libdl`, `libpthread`,
  `librt`, `libresolv`, `libnss_dns`, `libnss_files`, `libcrypt`, `libutil`). **verified**. License LGPL-2.1-or-later with the per-file
  notices of its `LICENSES` file (reproduced in `NOTICE`). **verified**.
- `libblkid.so.1.1.0` and `libuuid.so.1.3.0` (util-linux 2.27.1): `libblkid` embeds the build path
  `/home/cis/workspace/Clover-release/5b32431f/output/clover-dp-hvc-final/build/util-linux-2.27.1/.libs`, that is Nintendo's own firmware
  build. **verified**. libblkid LGPL-2.1+; libuuid BSD-3-Clause (`libuuid/COPYING` at `v2.27.1`, notice reproduced in `NOTICE`). **verified**.
- `libgcc_s.so.1`: GPL-3.0 with the GCC Runtime Library Exception (text: `COPYING.RUNTIME` of GCC 4.9.3, in `NOTICE`). **believed** for
  the build, **verified** for the text.
- Source: Nintendo's OSS zip lists `glibc-2.22.tar.xz` and `util-linux-2.27.1.tar.xz`; the hakchi.net Buildroot SDK also has them.
  Which build and patches produced each library: **unknown** except libblkid. These are open-source binaries, some built by Nintendo; not
  proprietary.

### 3.9 e2fsprogs 1.44.1 (`e2fsck`, `mke2fs`, `resize2fs`, `tune2fs`, `e2initrd_helper`, `libext2fs`, `libe2p`, `libcom_err`, `libss`)

- In every ramdisk until this revision; now only in `ccs-prepare` (section 7.3). The hmod repeats the libraries and `e2initrd_helper`.
  Version `1.44.1`; Buildroot 2015.11.1 GCC 4.9.3. **verified**.
- License (`NOTICE` at `v1.44.1`): programs GPL-2.0; `lib/ext2fs`, `lib/e2p` LGPL-2.0; `lib/uuid` BSD-style; `lib/et`, `lib/ss` MIT-style
  (the notice in `lib/et/com_err.c` and `lib/ss/ss.h`, reproduced in `NOTICE`). **verified**.
- Source: upstream 1.44.1 public; build configuration and patches **unknown**.

### 3.10 cryptsetup 1.6.8 (every ramdisk)

- `cryptsetup 1.6.8`, kernel crypto backend, dynamic only on libc/libm/libgcc_s/libuuid (libcryptsetup, device-mapper and popt linked in).
  **verified**.
- License: GPL-2.0-or-later with an OpenSSL linking exception (`COPYING` at `v1_6_8`; the exception reproduced in `NOTICE`), LGPL-2.1+ for
  libcryptsetup. **verified**. device-mapper (LVM2) LGPL-2.1 **believed**; popt MIT-style (`COPYING` at `popt-1_16-release`, reproduced).
  **verified**.
- Source: Nintendo's OSS zip lists `cryptsetup-1.6.8.tar.xz`, `LVM2.2.02.132.tgz`, `popt-1.16.tar.gz`; whose build this is: **unknown**.

### 3.11 dropbear, rsync, kexec-tools, llmnrd, tinysvcmdns (every ramdisk until this revision; now only `ccs-prepare`; kexec also in the hmod)

| Binary | Identity (verified) | License | Source |
|---|---|---|---|
| `dropbear` `dffd8072…76fd7ca` | `dropbear_2018.76` | MIT-style; LibTomCrypt/LibTomMath public domain (`LICENSE` at `DROPBEAR_2018.76`, reproduced in `NOTICE`) | upstream tag; build options unknown |
| `rsync` `991408d0…85177f5` | `3.1.3` | GPL-3.0-or-later | upstream tag; build options unknown |
| `kexec` `ef39cba7…bc49bfaec` | `kexec-tools 2.0.15` | GPL-2.0 | upstream tag; build options unknown |
| `llmnrd` `db335626…dee8e` | `v0.5-4-g4dc24d0` | GPL-2.0 | `tklauser/llmnrd` `4dc24d0ecf7333ce139a8b8fb2e8477e14bfe092` (**verified** by clone and `git describe`) |
| `tinysvcmdns` `61577d83…fadcb6` | no version string | BSD-3-Clause, Copyright (c) 2011 Darell Tan (`LICENSE.txt` of `philippe44/tinysvcmdns`, reproduced) | version **unknown** |

### 3.12 sunxi-tools (`sunxi-fexc` with `fex2bin`/`bin2fex`, `sunxi-nand-part`: every ramdisk until this revision, now `ccs-prepare`; `sunxi-pio`: hmod)

- `sunxi-fexc v1.4.2-54-g5c19710-dirty`, `sunxi-nand-part v1.4.2-54-g5c19710-dirty`, `sunxi-pio v1.4.2-30-g1e219c0-dirty`; Linaro GCC
  4.8.3. **verified**. License GPL-2.0-or-later. **verified**.
- Source: `linux-sunxi/sunxi-tools` `5c1971040c6c44caefb98e371bfca9e18d511da9` and `1e219c0bb9051cb6252e7fb9e52c552e4aa21a21`, both
  public; hakchi's `3rdparty/sunxi-tools.diff` is the likely "-dirty" change for `5c19710` (**believed**); `1e219c0-dirty`'s changes
  **unknown**.

### 3.13 clovershell daemon (`/hakchi/shell/clovershell`, every ramdisk; ccs-init starts it)

- 10,004 bytes, `cb7f57d83bed71acecaf2086c7176dfb98ce8dde776ff2cdc6d4868a2d3e4d40`; banner `clovershell (c) cluster, 2017 (built time:
  Mar 19 2018 01:08:05)`. GPL-3.0 (`ClusterM/clovershell-daemon`). Source `5edb874` (2017-03-08) contains every distinctive string
  (**believed** corresponding; build not reproduced).

### 3.14 Files only in ccs-prepare (from the hmod; installed on the console)

| File | Identity | License | Source |
|---|---|---|---|
| `bin/aplay` `13d66b72…cf525c1e3` | alsa-utils `1.0.29`; links `libasound.so.2` (the console's own) | GPL-2.0 | upstream; build unknown |
| `bin/fbgrab` `eaff3378…4e4027` | version not recorded (Buildroot 2015.11.1's package is 1.2, **believed**) | GPL-2.0 | version **unknown** |
| `bin/xdelta3` `803fa945a21e8a98870b9c13dcf1c11043fdfa0984cceb13e7e39ebc633a8548` (114,712 bytes) | **identified in this revision.** A UPX 3.94 packed file (`$Id: UPX 3.94 Copyright (C) 1996-2017 the UPX Team`) of a glibc-dynamic ARM program (NEEDED `libm.so.6`, `libpthread.so.0`, `libc.so.6`, `libgcc_s.so.1`). Its three LZMA blocks, read with Python's `lzma` (nothing run; 270,920 bytes, `0bade432…f34dc0b`), print `Xdelta version 3.1.1, Copyright (C) Joshua MacDonald` and `Licensed under the Apache License, Version 2.0`, and carry liblzma `5.2.2` for secondary compression. **verified** | **Apache-2.0** (the binary's own notice; `xdelta3/LICENSE` on branch `release3_1_apl`). liblzma: public domain. The UPX stub: GPL-2.0+ with the UPX exception for compressed programs (`LICENSE` at `v3.94`). All reproduced in `NOTICE` | `jmacd/xdelta` branch `release3_1_apl`: `AC_INIT([Xdelta3], [3.1.1])` from `e39b1b2cd4f29855ef529f2e29b6320e2709a916` (2016-04-03, "changing the license in the 3.1.x release branch") to the branch head `7508fd2a823443b1f0173ca361620f21d62a7d37` (2017-08-21); the exact commit is **unknown**. Apache-2.0 asks for the license and notices, not for the source |
| `bin/detached-fallback` | shell script (1,873 bytes), differs from the public `kernel-hmod/bin/detached-fallback` | GPL-3.0 (hakchi), **believed** | its own source |
| `bin/sntool`, `bin/kexec`, `bin/sunxi-fexc`, `bin/sunxi-pio`, `lib/*` | as above | | |
| `boot/boot.img`, `boot/uboot.bin` | as 3.1-3.4 (the original ramdisk, with every program of sections 3.6-3.13, is inside this `boot.img`) | | |
| `etc/preinit.d/b0010_functions`, `b0050_boot` | hakchi scripts | GPL-3.0 (hakchi), **believed** | their own source |

### 3.15 Hakchi scripts and data in the ramdisk

The ramdisk's `init`, `hakchi/*` scripts and data (81 files) are hakchi's (GPL-3.0, **believed**). 40 are byte-identical to files in
`madmonkey1907/hakchi` history; 41 differ (newer). **verified**. Scripts are their own source; the GPL-3.0 text and notices travel with
them (`NOTICE`).

### 3.16 ccsboot (CCS's own; ccs-prepare and ccs-rescue)

- `6c67045275ba7c19857540149b23f70419f23109825c032db88955a7f62ab5b4` (source `6b18c2ea…`), static ARM (clang/LLD 22.1.8 via zig 0.17.0,
  `-target arm-linux-musleabihf`). **verified**.
- CCS GPL-3.0-or-later, derived from GPL-3.0 `sntool.c` at `fd525ac`; statically includes musl (MIT; its `COPYRIGHT` is now reproduced in
  `NOTICE`) and compiler-rt (Apache-2.0 with LLVM Exceptions, **believed**). Source: CCS repository.

### 3.17 Windows side

- **FelLib** (`tools/console-loader/CcsConsole/FelLib/*.cs`): derived from `TeamShinkansen/FelLib` `e28ddfed0e2dea29279fb37fa379329329fa6cca`
  (the Hakchi2 CE `v3.9.3` submodule pin), GPL-3.0. Modified files carry a dated notice. **verified**.
- **LibUsbDotNet 2.2.29** (`LibUsbDotNet.LibUsbDotNet.dll`, `b6de87ff6fdde6d0a245ef37fe81fac49523c35115c61bc002e4935d99875508`): the NuGet
  package's `lib/net45` DLL; Hakchi2 CE 3.9.3 uses the same package (`hakchi_gui.csproj` `PackageReference LibUsbDotNet 2.2.29`; FelLib's
  `HintPath` `packages\LibUsbDotNet.2.2.29\lib\net45`), not a build of its submodule. **verified**. The package's `.nuspec` names
  `https://github.com/LibUsbDotNet/LibUsbDotNet` commit `5dd91a2fda393cf11db2072f2b45c3aee2750388` (2018-09-25, "Enable strong name
  signing", tree `f9ad3eddc5d6b1dc7aceb4692e0852602cf46d2a`; the DLL is strong-name signed), publicly fetchable by that hash. **verified**
  (fetched in this revision). That commit's `LICENSE` is LGPL-3.0 (`ea7d049c…432c9`); its source files state "GPL version 2 or (at your
  option) any later version" (for example `stage/LibUsbDotNet/UsbDevice.cs`). **verified**. Hakchi2 CE's submodule
  `TeamShinkansen/LibUsbDotNet` `91e84390acd83ded475798488740961da9e3307c` (2018-07-26) is a different line: `5dd91a2` is not its ancestor.
  **verified**. See section 8.11.

### 3.18 What is not carried

No Nintendo key file and no stock Nintendo application binary (`clover-mcp` is a link to `sntool`; `clover-kachikachi-wr` is a hakchi
script). **verified**. The image's `decrypt` reads the key from the console at run time (**believed**). Nintendo-
built open-source libraries (libblkid, libuuid) and the stock board data in u-boot (G4) are listed above.

### 3.19 hakchi.net Buildroot SDK (context, not an exact source)

`https://hakchi.net/hakchi/bin/buildroot-2015.11.1.tar.xz` (1,961,531,048 bytes, `a9f61fbf040ea07cbf569b0218803216ddaeb9e47f3891366f12485854788715`)
is a full Buildroot 2015.11.1 tree with build output. Its versions do **not** match the ramdisk binaries (dropbear 2015.71, e2fsprogs
1.42.13, kexec-tools 2.0.9, BusyBox 1.24.1; no rsync, llmnrd or tinysvcmdns). **verified**. It matches the toolchain banner, so it is
**believed** to be the hakchi build environment, not the corresponding source of these binaries.

### 3.20 CCS Mod and CCS Nucleus packages (`tools/console-nucleus`)

Only listed CCS files: shell scripts and four PNG menu pictures, plus two generated records. No ELF and no hakchi or Nintendo file.
**verified**.

### 3.21 The app's bundled Hakchi2 CE package (**Z**): files not built by the attached source archive

Besides `basehmods.tar` (sections 2-3.15) and Hakchi2 CE's own program, the portable package ships (**verified**: zip listing and strings):

| File | Identity | License (believed from upstream) | Notes |
|---|---|---|---|
| `tools/arm/kexec.static` `b38abcb5…df7bda` | kexec-tools 2.0.15, Buildroot GCC 4.9.3, static | GPL-2.0 | used by Hakchi2 CE's memboot (`MembootTasks.cs`) |
| `tools/arm/mke2fs.static` `a44c8a48…bb8523` | UPX-packed; version not read | GPL-2.0 (e2fsprogs) | |
| `tools/arm/sfdisk.static` `91ac7020…24bd25` | util-linux 2.31.1, Ubuntu/Linaro GCC 7.3.0 | GPL-2.0+ | |
| `tools/arm/unpackbootimg.static` `87da1e7f…0dad4` | Ubuntu/Linaro GCC 5.4.1 | Apache-2.0 (Android mkbootimg tools, **believed**) | |
| `tools/mksquashfs` `7323340c…44cd5` | squashfs-tools 4.4 (2019/08/29), ARM | GPL-2.0+ | |
| `tools/putty/puttytel.exe` | PuTTY telnet client, with `tools/putty/LICENCE` | MIT | |
| `driver/classic_driver.exe` | libwdi-built installer | LGPL-3.0 (libwdi) + Microsoft WinUSB redistributables | its source is in **A** (`driver/source` submodule) |
| `user_mods/*.hmod`, `basehmods.tar` members other than `hakchi.hmod` | bluetooth, clovercon, copyleft, font_remount, gamepath-cleanup, ntfs-3g, tiny7zx | each its own (not examined in this revision) | not taken by `build_images.py` |
| `data/original_art.tar` | 347 cover images of the Sega Mega Drive Mini's built-in games (`CLV-M2-*_mdmini.png`) | no license in the package | non-software content |
| `data/desktop_entries.tar` | 403 `.desktop` entries for the built-in games of the NES, Famicom, SNES and Mega Drive mini consoles | no license in the package | non-software content |

These are carried by the app only. Their exact sources were not part of this audit's G1-G9 scope and are **not identified** here.

## 4. Summary table

| # | Binary (carriers) | Identity | License | Exact corresponding source | Status |
|---|---|---|---|---|---|
| 1 | Linux kernel zImage (all 5 images; installed by prepare) | 3.4.113.29-madmonkey, 2020-03-20 | GPL-2.0 | not identified; nearest public `.24` `f8c6e0b` | **G1** |
| 2 | `nand.ko` (all images; installed) | `.29`, 42 symbols in no known source | GPL tag | not identified | **G2** |
| 3 | 117 other modules (sd_mod: prepare only from this revision; rest prepare) | `.29` | GPL / GPL v2 / Dual BSD/GPL | `.24` tree (in-tree, mali, cedar, clovercon); rtl8192eu `1addeae`, rtl8812au `ee592f8` (believed); rtl8188fu not identified | **G1/G3** |
| 4 | `rtl8188fufw.bin` (prepare) | = linux-firmware file | Realtek license | n/a (firmware) | notice reproduced |
| 5 | u-boot (all; prepare burns it) | 2011.09-rc1-g32c3df5-dirty, 2019-11-19 | GPL-2.0; Allwinner files copyright-only | base public; exact not identified; board block from stock (believed) | **G4, G5** |
| 6 | `fes1.bin` (all; RAM only) | eGON 3.0.0, `fc3061df…` | GPL-2.0 tree; `libdram-riot` no license | r16-uboot + patch + prebuilt object | **G6** |
| 7 | `sntool` and links (all; installed) | 5833d00 (dirty), 2020-03-30 | GPL-3.0 | not identified | **G7** |
| 8 | BusyBox (all; installed) | v1.29.0.git 2018-04-10 | GPL-2.0 only | snapshot and config not identified | **G8** |
| 9 | glibc 2.22, libgcc_s (all) | Buildroot builds | LGPL-2.1+ / GCC RLE | release tarballs public; patches unknown | partial |
| 10 | libblkid (prepare only from this revision), libuuid (all) | util-linux 2.27.1, Nintendo build | LGPL-2.1+ / BSD-3 | Nintendo OSS | notices reproduced |
| 11 | e2fsprogs (prepare only from this revision) | 1.44.1 | GPL-2.0 / LGPL-2.0 / MIT-style | upstream tag; build unknown | partial |
| 12 | cryptsetup (all) | 1.6.8, static libs | GPL-2.0+ with exception / LGPL-2.1+ | upstream; Nintendo OSS same versions | partial |
| 13 | dropbear (prepare only) | 2018.76 | MIT-style + public domain | upstream tag | notice reproduced |
| 14 | rsync (prepare only) | 3.1.3 | GPL-3.0+ | upstream tag; build unknown | partial |
| 15 | kexec (prepare only) | 2.0.15 | GPL-2.0 | upstream tag; build unknown | partial |
| 16 | llmnrd (prepare only) | v0.5-4-g4dc24d0 | GPL-2.0 | commit `4dc24d0` | identified |
| 17 | tinysvcmdns (prepare only) | no version | BSD-3-Clause | version unknown | notice reproduced |
| 18 | sunxi-fexc, sunxi-nand-part (prepare only), sunxi-pio (prepare) | 5c19710-dirty, 1e219c0-dirty | GPL-2.0+ | commits public; diffs partly unknown | partial |
| 19 | clovershell (all) | 2018 build | GPL-3.0 | ClusterM `5edb874` (believed) | believed identified |
| 20 | aplay (prepare) | alsa-utils 1.0.29 | GPL-2.0 | upstream tag | partial |
| 21 | fbgrab (prepare) | version unknown | GPL-2.0 | unknown version | partial |
| 22 | xdelta3 (prepare) | **xdelta 3.1.1**, UPX 3.94, liblzma 5.2.2 | **Apache-2.0**; UPX stub GPL-2.0+ with exception | branch `release3_1_apl`, commit range known | **identified (G9 identity closed)**; notices reproduced |
| 23 | hakchi scripts/data (all; prepare) | v1.0.4-126 | GPL-3.0 | scripts are source | notices |
| 24 | ccsboot (prepare, rescue) | CCS build | GPL-3.0+; musl MIT; compiler-rt Apache-2.0 + LLVM | CCS repository | musl notice reproduced |
| 25 | FelLib (Windows) | from `e28ddfe`, modified | GPL-3.0 | CCS repository | identified |
| 26 | LibUsbDotNet DLL (Windows) | NuGet 2.2.29 | LGPL-3.0 (LICENSE) / GPL-2.0+ (file headers) | `LibUsbDotNet/LibUsbDotNet@5dd91a2` | **records corrected** |
| 27 | CCS Mod / CCS Nucleus | CCS only | GPL-3.0+ | CCS repository | no third-party binaries |
| 28 | app's Hakchi2 CE package **Z** (app only) | `46520c8b…f122` | GPL-3.0 (Hakchi2 CE) and each file's own | **A** for Hakchi2 CE's program; section 3.21 for the rest | **Z1** open |

## 5. Concrete gaps

- **G1 kernel and modules**: no public source of the `.29` tree or its `.config` was found. CCS conveys the kernel in every image and the
  modules in the preparation image, and preparation installs them on the console. Under GPL-2.0 §3 a distributor must accompany the binary
  with its complete corresponding source or a written offer; CCS does not currently have that source to offer.
- **G2 `nand.ko`**: the driver every CCS session loads contains code found in no examined public tree (42 symbols). Same consequence.
- **G3 `rtl8188fu.ko`**: driver source not identified (vendor version known; community copies exist).
- **G4 u-boot board-configuration block**: 172,032 bytes that hakchi's recipe copies from the stock Nintendo u-boot (believed). No license
  and no published source form. A provenance/rights question (whether this is proprietary Nintendo material); it is separate from
  rebuilding u-boot.
- **G5 u-boot exact revision**: the 2019 build is not public; only the 2016 base and the 2017 patch are; the boot command differs.
- **G6 fes1 DRAM object**: Allwinner's prebuilt `libdram-riot`, no source and no license text, inside a GPL-2.0 program. Inherent to fes1.
- **G7 sntool 5833d00**: GPL-3.0 program without public source; the public source lacks functions the binary has. CCS uses it in every image
  and preparation installs it, so removing it from the images alone would not end conveyance.
- **G8 BusyBox 1.29.0.git**: snapshot and configuration not identified (GPL-2.0 only).
- **G9 xdelta3**: identity and license now established (Apache-2.0); no source obligation from Apache-2.0; notices reproduced. Closed as a
  gap.
- **Partial (build information)**: for glibc, e2fsprogs, cryptsetup, rsync, kexec-tools, alsa-utils, fbgrab and sunxi-pio the upstream
  release is public but the exact build configuration, patches and scripts (part of GPL "complete corresponding source") are not.

## 6. What the licenses require CCS to ship or link (facts, not a compliance claim)

| License (components) | Requirement when CCS conveys the binaries |
|---|---|
| GPL-2.0 (kernel and modules, u-boot, fes1, BusyBox, e2fsprogs programs, cryptsetup, kexec-tools, sunxi-tools, llmnrd, aplay, fbgrab, Realtek drivers, par2cmdline) | the license text; notices; complete machine-readable corresponding source (with the scripts used to control compilation and installation) **or** a written offer valid at least three years (§3(a)/(b)); §3(c) passes along an offer received and is limited to noncommercial distribution |
| GPL-3.0 / GPL-3.0+ (hakchi scripts, sntool, clovershell, rsync, FelLib, Hakchi2 CE parts, CCS's own code) | the license text; notices; Corresponding Source by one of §6(a)-(e). §6 "Installation Information" concerns User Products in a transfer-of-possession transaction; CCS does not sell consoles, so it is **believed** not triggered (not confirmed) |
| LGPL-2.1+ / LGPL-2.0 (glibc, libblkid, libext2fs, libe2p, libcryptsetup, device-mapper) | license text, notices, the library's source or an offer |
| LGPL-3.0 (LibUsbDotNet's LICENSE) | LGPL-3.0 and GPL-3.0 texts; a notice that the library is used; the library's source (`5dd91a2`) or an offer. Embedding it in the GPL-3.0 Workbench whose full source is released is **believed** acceptable (§4(d)(0)); not confirmed |
| GCC Runtime Library Exception (libgcc_s) | GPL-3.0 text and the exception text |
| MIT / BSD / public domain (dropbear, LibTomCrypt/LibTomMath, tinysvcmdns, libcom_err, libss, libuuid, popt, musl in ccsboot, liblzma) | reproduce the copyright and permission notices in the documentation of a binary distribution |
| Apache-2.0 (xdelta 3.1.1; `unpackbootimg` in **Z**, believed) and Apache-2.0 with LLVM Exceptions (compiler-rt, libc++) | the license text and any NOTICE file; the LLVM exception waives the attribution conditions for compiled forms |
| UPX exception (the decompressor inside `xdelta3`) | the packed program may be distributed under its own license provided the stub is unmodified (UPX `LICENSE`) |
| Realtek firmware license (`rtl8188fufw.bin`) | redistribute unmodified; reproduce the notice and disclaimer; no reverse engineering |
| Allwinner `libdram-riot` in fes1; board-configuration block in u-boot | no license text found; no requirement can be stated; see G4/G6 |

## 7. What the console programs actually run (usage trace)

### 7.1 Method

- **The PC side** runs only CCS's own scripts on the console: every `shell.Execute` in `CcsBackup/Device/RecoveryConsole.cs` names
  `/ccs/bin/ccsbr`, `ccsprep`, `ccsrescue`, `ccsfiles` or `ccsdev` (lines 74-317). **verified** (search of every command string in the
  engine, the window and the loader).
- **The console side** of each image runs hakchi's `/init` (loads `nand.ko`, opens the system read-only with `cryptsetup`, mounts it,
  then runs `/hakchi/init`, which the builder replaced by `ccs-init`), `ccs-init` (marks storage read-only, starts the clovershell gadget
  and daemon, waits in `ccs-idle`), and the scripts above. `ccs-init` sources none of hakchi's scripts, starts no network and no service,
  and `/init`'s hand-over to the normal system (which would run `rcS` and hakchi's start scripts) is replaced by `ccs-idle`. **verified**:
  the scripts; `test_recovery_images.py` static audit (forbidden-step lists) and its qemu runs.
- Only `ccsprep` (preparation image) sources hakchi's functions (`preinit`, `preinit.d`, `script/install`) and runs hakchi's own
  `mod_install`/`mod_repair`. **verified**.
- Dynamic libraries: ELF `NEEDED` entries parsed for every ramdisk and hmod program (section 7.2 last column). **verified**.

### 7.2 Per program

| Program | Run in a console program (verified from the scripts) | Installed on the console by preparation | Used on a console running hakchi (verified from hakchi's scripts and Hakchi2 CE's source) | NEEDED |
|---|---|---|---|---|
| BusyBox | everywhere (`/init` links its applets into `/bin`; every script) | yes: `mod_repair` copies the RAM `/bin` (`copy "/bin/" ...`, then `copy "$modpath/rootfs/" "$rootfs/"`) | yes | libc, libm |
| sntool (`sunxi-flash`, `sunxi-part`, `decodepng`) | `/init` (`sunxi-flash cmdline`, `ramdisk`); ccsbr (every boot-area read, `phy_read`, `sunxi-part`, `devmem`, `nandinfo`, `decodepng` for the TV pictures); ccsprep (hakchi's `flashBoot`, `sunxi-part tune`/`shrink`, `sntool check`); ccsrescue (`sunxi-flash read_boot1`); ccsdev (`sntool check`, `burn_boot2`, `read_boot2`) | yes (`/bin` and hmod `bin/`; inside `boot.img`) | yes | libc, libgcc_s, libdl |
| cryptsetup | `/init` (`open --readonly` of the system); the Developer system-part step | yes (`/bin`) | yes (hakchi's `/init`) | libc, libm, libgcc_s, libuuid |
| clovershell | `ccs-init` (the PC's only transport) | inside `boot.img` | when hakchi's clovershell mode is on | libc, libgcc_s |
| `nand.ko` | `/init` `insmod` (every session) | inside `boot.img` | yes | kernel |
| `sd_mod.ko` | never: nothing loads it (no `insmod`, no `modprobe`, no `/sbin/modprobe`, the USB port is a device, not a host) | inside `boot.img`; hmod modules | USB storage (believed) | kernel |
| rsync | **only** ccsprep's files step: hakchi's `copy` is `rsync -ac` (`b0010_functions` lines 225, 235) | yes | yes (hakchi's `copy`; Hakchi2 CE's `SDFormat.cs` and `MembootTasks.cs` run `rsync`) | libc, libgcc_s |
| dropbear | never (no network in any image) | yes | yes (hakchi's `inetd.conf`, `S93inetd`) | libutil, libcrypt, libc, libgcc_s |
| llmnrd, tinysvcmdns | never | yes | yes (`S93llmnrd`, `S93mdnsd`) | libc (+ libpthread, libm, librt, libgcc_s) |
| kexec | never (forbidden in every CCS script audit) | yes (`/bin` and hmod `bin/kexec`) | **yes**: hakchi's `reboot_kexec` (`p0100_chainload`, `p7080_hmods` after its transfer folder is applied, which is how CCS Mod and CCS Nucleus are installed), `detached-fallback` | libc, libgcc_s |
| e2fsprogs (`e2fsck`, `mke2fs`, `resize2fs`, `tune2fs`, `e2initrd_helper`) | never | yes | hakchi's `script/reflash` only (`mke2fs`) | libext2fs, libcom_err, libblkid, libuuid, libe2p, libdl, libc |
| `sunxi-fexc`, `sunxi-nand-part` | never | yes | `sunxi-fexc`: hakchi's `script.bin` helpers; `sunxi-nand-part`: no reference found | libc |
| `xdelta3`, `aplay`, `fbgrab`, `sunxi-pio` (hmod only) | never | yes | `aplay`: hakchi's sound function; all four are listed in hakchi's `etc/bootcopy.d/hakchi`, which hakchi's recovery shell copies into RAM in USB host mode; no Hakchi2 CE PC reference to `xdelta3`, `fbgrab` or `sunxi-pio` | xdelta3: static (UPX) |
| u-boot (`uboot-sd.bin` / `uboot.bin`) | every session (FEL) | `uboot.bin` burned to boot1 (`ccsprep uboot`) | the console starts through it | n/a |
| fes1 | every session (FEL, DRAM set-up) | never | no | n/a |

### 7.3 Payload trimmed in this revision (dependency evidence above)

`build_images.py` `SESSION_UNUSED` leaves 35 ramdisk entries out of `ccs-backup`, `ccs-restore`, `ccs-rescue` and `ccs-files`: `dropbear`,
`rsync`, `llmnrd`, `tinysvcmdns`, `kexec`, `e2fsck` (and its three `fsck.ext*` links), `mke2fs` (and three `mkfs.ext*` links),
`resize2fs`, `tune2fs`, `sunxi-fexc` (and `fex2bin`, `bin2fex`), `sunxi-nand-part`, `e2initrd_helper`, the e2fsprogs and libblkid
libraries with their links (no remaining program needs them: cryptsetup keeps `libuuid`; every glibc library stays, including the NSS
modules loaded at run time) and `sd_mod.ko`. `ccs-prepare` is unchanged byte for byte, because hakchi's installation copies its `/bin` to
the console and its copy step runs `rsync`.

- The build stops if a script of a trimmed image names a left-out program (checked by a probe: an injected `rsync`, `e2fsck` or `fsck.ext4`
  line stops the build; a comment or `reboot_kexec` does not).
- The left-out names are part of each trimmed image's `image_id` (the console reports it; the PC refuses another program) and are listed in
  `MANIFEST.json` `ramdisk_changes.removed`.
- No engine code reads a backup's recorded `recovery_image` back, so existing backups restore as before. **verified** (search).
- Effect: each of the four images is 677,888 bytes smaller (ramdisk 6.07 MB to 4.48 MB uncompressed). Both products still carry every
  left-out program through `ccs-prepare` (and the app also through **Z**): **no license obligation changes**; what changes is that the four
  programs most sessions use carry only what they run.
- Built image identities (the integration build of the merged source). `ccs-prepare` and `ccs-files` differ from this revision's first
  build only through other changes in the merged source: the CCS HOME placement steps and the CCS Nucleus package with the start chooser
  (`ccs-prepare`), the Developer system-part commands (`ccs-files`); the trimmed payload is as above.

| Program | image_id | Bytes | SHA-256 |
|---|---|---|---|
| ccs-backup | `ccs-backup/1/97c6926c386b4ba2` | 3,983,360 | `3837642ccf30fd91f96538df837de030f2b36b8c844057ff1e589751b0e4eef7` |
| ccs-restore | `ccs-restore/1/c9c6f49819c67010` | 3,997,696 | `aa39685dec23b138baec891d9cf71f36092060d1b247fca508676ce883e94eff` |
| ccs-prepare | `ccs-prepare/1/5e04a4e7e01eef06` | 11,479,040 | `f63db0aae6683112169eeb6d070bf262b68116ca717514ac1fd8a88f7daf2091` |
| ccs-rescue | `ccs-rescue/1/04a83cae5390b955` | 4,012,032 | `32c81bb025062f77093ca47de8f35257c75c840f19e956493457acefe3a76ee3` |
| ccs-files | `ccs-files/1/02ecc5f525a3545c` | 3,999,744 | `b9db22ff7794667ae7e04e4a5047a25f866ca54a07b2fcd30169733fa526e5e3` |

## 8. Per-binary details

Abbreviations: **B R P S F** = the backup, restore, prepare, rescue and files console programs (all five are in both products, section 1);
**inst** = installed on the console by preparation; **Z** = the app's bundled Hakchi2 CE package; **A** = the Hakchi2 CE source archive
attached to app releases.

**Cross-cutting fact.** Removing a binary from one console program, or from the app's bundled package, does not stop either product from
carrying it while another carrier remains. The G1-G8 items are in every console program or are installed by preparation.

**Inputs.** Every input `build_images.py` uses at build time comes from the official `hakchi2-ce-3.9.3-portable.zip` (checked against
`46520c8b…f122`): `basehmods.tar` → `hakchi.hmod` → `boot.img`, `uboot.bin`; `hakchi.exe` → fes1. The preparation ramdisk is 22,257,664
bytes uncompressed.

### 8.1 Linux kernel 3.4.113.29-madmonkey (G1)

| | |
|---|---|
| Artifact | zImage `920ffa42a279c7f6e3388114b1e4295da23a9e4d1477dc7137d25ad472dbc801` (1,968,736 bytes) in `boot.img` `9e921c326be65aa4a3100270e86fdb15fe4b8563d586941b9f6c640eef064e4c` |
| Purpose | runs every console program; preparation installs hakchi's `boot.img` (this kernel, with the console's key added) as the startup software |
| Carriers | B R P S F (RAM, every session); P's hmod `boot/boot.img` (inst, boot2); Z (inside `hakchi.hmod`); not Mod/Nucleus |
| License evidence | GPL-2.0 with the syscall note (`COPYING`, `f8c6e0b`) |
| Located | `.24` tree `madmonkey1907/sun-nontendocm-kernel@f8c6e0b`; hakchi.net snapshot `9dba1d9`; Nintendo 3.4.112 (OSS zip) |
| Not located | the `.29` tree, its `.config`, its build steps |

### 8.2 `nand.ko` (G2)

| | |
|---|---|
| Artifact | `5d67ca0977370a04f46edbf1c02aa1dcbb2fc964781775d906125c40eb7d29d5` (377,076 bytes) |
| Purpose | the NAND driver: every read and write of the console's storage in every CCS session |
| Carriers | B R P S F (RAM); inside P's hmod `boot.img` (inst) and `lib/modules` (inst); Z |
| License evidence | module tag "GPL" |
| Located | `.24` tree's `drivers/block/nand` and Allwinner's `libnand_sun8iw5p1` archive (do not match) |
| Not located | the source of 42 defined symbols; whether the blob is linked |

### 8.3 The other kernel modules, `rtl8188fu.ko` (G1/G3)

| | |
|---|---|
| Artifact | 117 modules (list in `ccs-prepare/MANIFEST.json`), for example `rtl8188fu.ko` `7634c981617ef90d00ce7d666ecbdc29017392b645226bb3301ac7d7fca24f52` |
| Purpose | hakchi's drivers on a prepared console (USB storage, controllers, Wi-Fi, Bluetooth, sound, network file systems); CCS's own steps use none of them |
| Carriers | P (inst); `sd_mod.ko` also inside every `boot.img`; Z; **no longer** in the B R S F ramdisks (this revision) |
| License evidence | GPL / GPL v2 / Dual BSD/GPL tags |
| Located | `.24` tree for the in-tree drivers; rtl8192eu `1addeae`, rtl8812au `ee592f8` (believed) |
| Not located | the `.29` build of all of them; rtl8188fu's source |

### 8.4 u-boot board-configuration block (G4; rights question)

| | |
|---|---|
| Artifact | bytes 0x90000-0xBA000 of `uboot.bin` (`c0944183c90eb413ce003dfe796dfacf24ebd09557ec18762064d2956f74eca0`, 172,032 bytes) |
| Purpose | the board's parameters (DRAM, storage, power) u-boot needs on this console |
| Carriers | B R P S F (`uboot-sd.bin`, RAM); P's hmod `uboot.bin` (inst, boot1); Z |
| License evidence | none found |
| Located | hakchi's recipe saying it is cut from the console's stock u-boot (believed) |
| Not located | a source form (`.fex`), a license, a provenance statement |

### 8.5 u-boot build `g32c3df5-dirty` (G5)

| | |
|---|---|
| Artifact | `uboot.bin` `5cfacb297b962a388d947e3be553692bb1e9df9edc1125ca5a769166d3a89564`; `uboot-sd.bin` `3bfc4a56417aade7d3fa3afcf3d2344b07ea38d9f42de504eda3014844f86b5b` |
| Purpose | loads each console program's `boot.img` from RAM (FEL); on a prepared console, starts hakchi's kernel from storage |
| Carriers | B R P S F (RAM); P's hmod (inst, boot1); Z |
| License evidence | GPL-2.0 (tree `COPYING`) |
| Located | Nintendo's r16-uboot `fc3061df` (OSS zip); hakchi's `r16-uboot.diff` at `fd525ac` |
| Not located | commit `32c3df5dff315fd5bdca115aa85e5c4d6befa46b`, its local changes; the change behind the different boot command |

### 8.6 fes1's Allwinner DRAM object (G6)

| | |
|---|---|
| Artifact | `fes1.bin` `d78688b70e022c53e6a49a18994f59946fda7791d22bc0e6409ff630553b491c` (links `libdram-riot`, 43,350 bytes, in the public r16-uboot tarball) |
| Purpose | sets up the console's DRAM in FEL mode before u-boot runs (every session); never written to the console |
| Carriers | B R P S F (RAM); Z (resource in `hakchi.exe`); A (`data/fes1.bin`) |
| License evidence | tree GPL-2.0; the object has no license text |
| Located | everything but the object's own source (the tarball and the patch are public) |
| Not located | `libdram-riot`'s source or license |

### 8.7 sntool 5833d00 (G7)

| | |
|---|---|
| Artifact | `f7eeecdeb94863364593fe9da55f499bfa13915a74fa2b2049e18209a65f57e9` (793,728 bytes) and its five links |
| Purpose | every boot-area read and the boot2 writes, the storage table (`sunxi-part`), the TV pictures (`decodepng`), the console's identity (`devmem`) |
| Carriers | B R P S F (RAM); P (inst: `/bin` and hmod `bin/`; inside `boot.img`); Z |
| License evidence | GPL-3.0 (hakchi `LICENSE`) |
| Located | `madmonkey1907/hakchi@fd525ac` `sntool/sntool.c` (an older version: lacks `sunxi-part`, `decodepng`, `clover-mcp`, `m2engage`) |
| Not located | `5833d00`'s source |

### 8.8 BusyBox v1.29.0.git (G8)

| | |
|---|---|
| Artifact | `d5c96520b02920b331ec8b7f232c662e53f97add208741f65500b0512bf87d44` (650,604 bytes; 329 applets) |
| Purpose | the shell and every utility of every script in every console program |
| Carriers | B R P S F (RAM); P (inst: `/bin` copy; inside `boot.img`); Z |
| License evidence | GPL-2.0 only (`LICENSE` at `1_29_0`) |
| Located | upstream git (the snapshot lies between 1.28 and 1.29.0); its applet list (observed) |
| Not located | the commit and the `.config` |
| Rebuild tried (prototype, not shipped, nothing in the repository) | Release tarballs pinned by SHA-256 (`busybox-1.29.3.tar.bz2` `97648636…f7621efb7`; `busybox-1.36.1.tar.bz2` `b8cc24c9…0e47de314`), a configuration generated from hakchi's 329-applet list (every other applet off; the `busybox` applet itself kept, since hakchi's `/init` runs `busybox --list`), static armv7 hard-float with the pinned zig 0.17.0. Findings: (1) both versions need three deviations with this toolchain: libc's `clock_gettime` instead of the raw syscall (musl's 64-bit time has no `__NR_clock_gettime` on 32-bit ARM), no `tc` (its CBQ constants are gone from current kernel headers) and no `runsv` (its `timeval`/`timespec` size check fails with 64-bit time), plus three GNU ld options zig's linker does not take; (2) **1.29.3**, the release nearest hakchi's snapshot: its `ash` crashes (segmentation fault under qemu-arm on `sh -c`); (3) **1.36.1**: two builds byte-identical (`91780f71451f79995909d1707daf379e3c97a9988dcde2c60b7e823d40bb2653`), `ash` runs, but **`awk` crashes** on every program, so `ccsbr identify` loses its partition list and the console-program suite (`test_recovery_images.py`, qemu-arm) gives 304/308 with it in B R S F: the four failures are `ccsbr identify`'s partition list in each image (the same suite gives 308/308 with hakchi's BusyBox). Both crashes are **believed** to be the known problem of BusyBox's pointer-to-globals code under clang; a GCC cross toolchain is **believed** to avoid it (not tried). **Conclusion: not a straightforward rebuild with the repository's pinned toolchain**; it needs a pinned GCC/musl or GCC/glibc cross toolchain (none is pinned in this repository) and then the full suite and a hardware read session. |

### 8.9 xdelta3 (G9, identity closed)

| | |
|---|---|
| Artifact | `803fa945a21e8a98870b9c13dcf1c11043fdfa0984cceb13e7e39ebc633a8548` (UPX 3.94; inside: xdelta 3.1.1 with liblzma 5.2.2) |
| Purpose | hakchi provides it in its recovery shell (`bootcopy.d`); CCS never runs it; no Hakchi2 CE PC reference found |
| Carriers | P (inst, hmod `bin/`); Z |
| License evidence | Apache-2.0 (the binary's notice; `release3_1_apl` `LICENSE`); UPX stub GPL-2.0+ with exception; liblzma public domain |
| Located | `jmacd/xdelta` branch `release3_1_apl` (`e39b1b2` .. `7508fd2`) |
| Not located | the exact commit (not an Apache-2.0 requirement) |

### 8.10 Missing notices (N1, corrected)

| | |
|---|---|
| What was missing | `NOTICE` named BusyBox, the kernel and a few others only; it did not reproduce the notices that MIT/BSD-style, Realtek, glibc, popt, the cryptsetup exception, Apache-2.0, UPX and the GCC exception require, and gave the GPL only as a link |
| Done | `NOTICE` (embedded in `CcsBackupUi.dll` and shown by both hosts) now lists every component the console programs carry, with license, origin and source status, reproduces the required notices and ends with the full license texts (GPL-3.0, GPL-2.0, LGPL-2.1, LGPL-2.0, LGPL-3.0, GCC exception 3.1, Apache-2.0, LLVM exceptions). Each text's origin is in section 9 |
| Carriers | both products (one embedded file); the app also installs `THIRD_PARTY_NOTICES.md` (now pointing to it for the console programs) |
| Remaining | **Z**'s own files (section 3.21) are noticed only through the package's own `LICENSE` and `Licenses/`; see section 8.15 |

### 8.11 LibUsbDotNet source pin (N2)

| | |
|---|---|
| Artifact | `LibUsbDotNet.LibUsbDotNet.dll` `b6de87ff6fdde6d0a245ef37fe81fac49523c35115c61bc002e4935d99875508` |
| Carriers | the standalone (embedded), the app (beside the host and in the console worker) |
| Facts | the DLL is the NuGet package's; its source is `LibUsbDotNet/LibUsbDotNet@5dd91a2` (tree `f9ad3edd…f46d2a`); CCS's worker records named the Hakchi2 CE submodule `91e8439` |
| Done | `NOTICE`, `THIRD_PARTY_NOTICES.md` and the worker's `SOURCE.md` and `build-worker.ps1` text name `5dd91a2`; both release workflows attach `LibUsbDotNet-2.2.29-source-5dd91a2.tar` made from that commit, refusing any other tree |
| Remaining | `console-worker-source.zip` still carries the submodule's files (now labeled as Hakchi2 CE's copy, not the DLL's source) |

### 8.12 What archive **A** is (N3)

| | |
|---|---|
| Facts | section 2: **A** is Hakchi2 CE's program source with its submodules; it is not the source of the prebuilt files in the package |
| Done | `THIRD_PARTY_NOTICES.md`, the release notes text (`release_notes.py`) and `release.yml`'s comment say exactly that |

### 8.13 The Workbench's own source archive (N4)

| | |
|---|---|
| Facts | the draft `branch-root/workbench-release.yml` attached `git archive … tools/console-backup tools/console-simulator/core LICENSE THIRD_PARTY_NOTICES.md`, which misses code compiled into the program (`tools/console-loader` with FelLib, `tools/console-worker/CcsWorker/Shell`, `tools/platform`, the window theme and pictures) and the build inputs (the CCS Mod/Nucleus sources inside the preparation program, the TV pictures' fonts, the toolchain pins and the build-environment check) |
| Done | the draft attaches every folder and file the build reads (the official Hakchi2 CE package stays an input that `build_images.py` checks by SHA-256), plus the LibUsbDotNet source; its release text no longer claims every component's source is attached |

### 8.14 "or any later version" for derived parts (N5)

| | |
|---|---|
| Facts | Hakchi2 CE, FelLib and hakchi's `sntool.c` are published with the GPL-3.0 text and no "or any later version" statement (**verified**: their `LICENSE` files and the headers of `ClovershellConnection.cs`, FelLib's `Fel.cs`, `sntool.c`). CCS labels the Workbench, the console worker and `ccsboot` GPL-3.0-or-later. `THIRD_PARTY_NOTICES.md` said "No GPL-3.0-only code is compiled into Classic Cart Studio's own programs" |
| Done | that absolute sentence and `NOTICE` now state the facts (CCS offers its own code under GPL-3.0-or-later; the upstream files keep their own terms); nothing was relabeled |

### 8.15 The app's bundled Hakchi2 CE package (Z1)

| | |
|---|---|
| Artifact | `hakchi2-ce-3.9.3-portable.zip` `46520c8b9a3c768b5ee7c2cb4878adc11e3da1869aae281000d75012b7e9f122` |
| Purpose | Hakchi2 CE included unmodified so its setup and console driver need no download; input of the console programs' build |
| Carriers | the app only (installer `_internal\packages`); never the standalone |
| Contents beyond G1-G8 | section 3.21 (static ARM tools, user mods, the driver installer, 347 Mega Drive Mini cover images, 403 desktop entries) |
| Coverage | the app carries every file listed; **A** covers Hakchi2 CE's own program only |

## 9. Corrections made in this revision

| File | Correction |
|---|---|
| `tools/console-backup/NOTICE` | rewritten: every carried component with license, origin and source status; the trimmed programs; required notices reproduced; full license texts appended (sources below); LibUsbDotNet's actual source; the "or any later version" facts; "not published by their authors" replaced by "not identified in any public location searched" |
| `THIRD_PARTY_NOTICES.md` | the console programs' components (pointing to `NOTICE`); archive **A** described exactly; LibUsbDotNet's source commit and license facts; the "No GPL-3.0-only code" sentence replaced by the facts |
| `.github/workflows/release.yml` | the comment on archive **A**; the LibUsbDotNet source archive built from `5dd91a2` (tree checked) and attached; build-info records it |
| `tools/publish/release_notes.py` | the "Source code and licenses" paragraph: what archive **A** is and is not; the console programs' third-party binaries and where their notices and source status are |
| `branch-root/workbench-release.yml` (draft) | the complete engine source archive; the LibUsbDotNet source archive; the release text no longer claims every component's source is attached; its .NET pin points at `tools/console-backup/global.json` (the repository root has no `global.json`, so the draft's managed job could not have run) |
| `branch-root/README.md` (draft) | credits paragraph: what is attached, and that the console programs carry hakchi's prebuilt files under their own licenses |
| `tools/console-worker/SOURCE.md`, `NOTICE`, `build-worker.ps1` (text and the build record's `libusbdotnet.source` only) | LibUsbDotNet's source is `5dd91a2`; its license facts (LGPL-3.0 `LICENSE`, GPL-2.0-or-later file headers; the worker's `NOTICE` said LGPL v3 while its `licenses/LibUsbDotNet.txt` holds the GPL-2.0-or-later notice); the submodule copy is labeled as Hakchi2 CE's |

License texts in `NOTICE`, each fetched in this revision from the upstream location named (or copied from the repository's own zig 0.17.0
runtime files, `tools/player/third_party/zig-runtime`), SHA-256 of the file as fetched:

| Text | Source | SHA-256 (as fetched) |
|---|---|---|
| GPL-2.0 | `https://www.gnu.org/licenses/old-licenses/gpl-2.0.txt` | `edaef632cbb643e4…` |
| LGPL-2.0 | `https://www.gnu.org/licenses/old-licenses/lgpl-2.0.txt` | `cc535c21133c895b…` |
| LGPL-2.1 | `https://www.gnu.org/licenses/old-licenses/lgpl-2.1.txt` | `20e50fe7aae3e563…` |
| LGPL-3.0 | LibUsbDotNet `LICENSE` at `5dd91a2` | `ea7d049c7705dc13…` |
| GCC Runtime Library Exception 3.1 | `gcc-mirror/gcc` `releases/gcc-4.9.3` `COPYING.RUNTIME` | `9d6b43ce4d8de0c8…` |
| Apache-2.0 | `jmacd/xdelta` `7508fd2` `xdelta3/LICENSE` | `6cf63d87586c7c25…` |
| UPX license | `upx/upx` `v3.94` `LICENSE` | `905d4a66d5324e15…` |
| dropbear | `mkj/dropbear` `DROPBEAR_2018.76` `LICENSE` | `cfa32d49e9022265…` |
| tinysvcmdns | `philippe44/tinysvcmdns` `LICENSE.txt` | `31702bcb54a065a3…` |
| libuuid (BSD-3) | `util-linux` `v2.27.1` `Documentation/licenses/COPYING.BSD-3` (+ the `uuid.h` copyright line) | `9b718a9460fed595…` |
| libcom_err / libss | `e2fsprogs` `v1.44.1` `lib/et/com_err.c` header | `873d31d80fa2f930…` |
| popt | `rpm-software-management/popt` `popt-1_16-release` `COPYING` | `518d4f2a05064cb9…` |
| cryptsetup exception | `cryptsetup` `v1_6_8` `COPYING` | `45670cce8b6a0ddd…` |
| glibc 2.22 `LICENSES` | `sourceware.org` glibc `glibc-2.22` | `61abdd6930c9c599…` |
| Linux kernel note | `madmonkey1907/sun-nontendocm-kernel` `f8c6e0b` `COPYING` | `af8067302947c01f…` |
| Realtek firmware | `linux-firmware` `LICENSES/LICENCE.rtlwifi_firmware.txt` | `a61351665b4f264f…` |
| musl, mingw-w64, LLVM exceptions | `tools/player/third_party/zig-runtime` (`VENDORED.md` lists their hashes) | as listed there |
| GPL-3.0 | the repository's `LICENSE` | |

## 10. Releases and the open gaps

- No release of either product may state or imply that complete corresponding source is provided for the console programs while G1, G2, G3, G5, G7 or G8 is open. Both products carry
  them (section 1).
- G4 and G6 remain open for u-boot and fes1, which every release with console programs carries.
- The app's release with **Z** carries the package's other prebuilt files (section 8.15), whose exact sources are not identified
  (section 3.21).
- The notices in `NOTICE` and `THIRD_PARTY_NOTICES.md`, the attached archives (CCS source, par2cmdline, LibUsbDotNet, and for the
  app **A**) are necessary in every release; they are not sufficient while the gaps above are open.
- No legal waiver or upstream pin update is made or implied by this document.
