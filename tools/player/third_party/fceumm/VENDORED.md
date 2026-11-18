# FCEUmm (libretro-fceumm), vendored

- Upstream: https://github.com/libretro/libretro-fceumm
- Commit: `ac0d00a7fc01166f84c7bac01d1084ebfd2b865e` (2026-10-11, "Merge pull request #658 from LilaQ/fix/onebus-separate-chr-rom")
- Copied: `src/`, `Copying` (GNU GPL version 2 text), `Authors`, `README.md`. Not copied: the build files, `jni/`, `tests/`, `intl/`.
- Removed from `src/` because Classic Cart Studio does not compile them: `hdpack/`, `ntsc/` (optional NTSC filter),
  `drivers/libretro/libretro-common/formats/` and `include/formats/` (HD-pack image and audio decoders),
  `include/compat/msvc/`, `streams/trans_stream*.c`, `include/streams/trans_stream.h`, `encodings/encoding_deflate.c`.
- Patches: none. Every file has the same content as at the upstream commit: 597 are byte-identical to its blobs, and 23 that upstream
  stores with CRLF line endings are stored with LF by this repository (.gitattributes: text=auto eol=lf); a working checkout may again
  show CRLF or LF. Nothing else differs. Added here: this file, `LICENSE.emu2413.txt` and `LICENSE.pic16c5x.txt`.

## Licenses of the compiled files

- The FCEUmm sources carry the GNU GPL "version 2 of the License, or (at your option) any later version" notice; no compiled file
  says version 2 only. Classic Cart Studio is GPL-3.0-or-later, so the combined player is distributed under GPL version 3.
- `boards/pic16c5x.c`: BSD-3-Clause (copyright Tony La Porta, from MAME). The file names its license (`license:BSD-3-Clause`,
  `copyright-holders:Tony La Porta`) without the text; the text is carried in `LICENSE.pic16c5x.txt`, MAME's own copy of the license
  (`docs/legal/BSD-3-Clause` at MAME commit `ff7d968ebd027ace692a2e91e48632f1c6156238`, unchanged since commit `11ac7454`; 1,481 bytes,
  SHA-256 `a27554938d3dd6569931e5e18b93214a57f47bc1bb8b75fc32fdd18abe042a07`).
- `drivers/libretro/libretro-common/`: the RetroArch team's MIT-style permission notice in each compiled file.
- `boards/emu2413.c` and `emu2413.h`: Mitsutaka Okazaki's YM2413 emulator, version 0.60 (2002) per the file's header, modified in
  2004 by xodnizel for FCE Ultra and since then in FCEUmm (it arrived in libretro-fceumm with its initial import of FCEUmm
  SourceForge revision 160; 8 commits touch it up to `ac0d00a7`). The file carries no license text of its own. Primary license
  evidence: the author's own emu2413 repository (https://github.com/digital-sound-antiques/emu2413), whose CHANGELOG runs from
  version 0.10 (2001-01-08) through 0.61 (2004) to 1.x, is licensed by its LICENSE file under the MIT License, "Copyright (c)
  2001-2019 Mitsutaka Okazaki", covering the years of the 0.60 code. The notice is carried in `LICENSE.emu2413.txt` (beside
  this file) and in THIRD_PARTY_NOTICES.md. The FCE Ultra/FCEUmm modifications come with FCEUmm under the GNU GPL version 2 or
  later.
- Files with no notice of their own (`boards/eeprom_93Cx6.c`, `flashrom.c`, `vrcirq.c`, `__serial.c` and some headers) are part of
  FCEUmm and covered by its `Copying` (GNU GPL version 2; with no version stated for those files, GPL section 9 allows any version).

## Builds

Compiled with `-D__LIBRETRO__ -DPATH_MAX=1024 -DFCEU_VERSION_NUMERIC=9900 -DFRONTEND_SUPPORTS_RGB565 -DLSB_FIRST -DLOCAL_LE=1`,
through the libretro API (`retro_*`) only, from these same sources for three shipped targets:

- `windows-x86_64` (tools/console-simulator/scripts/wsl_build.sh): with the player (`tools/player/core`) and the native HOME core
  into one executable, `ccs-player.exe`, the Windows app's game window.
- `arm-musl-device` (`arm-linux-musleabihf`, Cortex-A7; the same script): `ccs-player-armv7`, the NES Classic game player. It ships
  inside the Windows installer (`native/`) for the console packages the app prepares; it reaches a console only through the app's
  gated console paths.
- WebAssembly (tools/publish/build_web_player.py): `ccs-player.wasm`, the website's and website app's game player.
