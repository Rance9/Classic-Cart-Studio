# Open Source Credits & Licenses

## Classic Cart Studio

Copyright (C) 2026 Rance9 (Classic Cart Studio).

The project's own code (Python tools, the backend, the Windows application host in C#, the website's HTML/CSS/JavaScript, and the native HOME renderer and runtime) is free software: you can redistribute it and/or modify it under the terms of the **GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version** (`LICENSE`). It is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.

The project's license does **not** cover the third-party material below, which remains under its owners' rights and licenses. This file records what is bundled, what is downloaded separately, and where each item comes from.

## Code incorporated from other projects

| Code | Where | License | Source |
|---|---|---|---|
| stb_image v2.30 | image decoding in the native HOME renderer (`tools/console-simulator/core/third_party/stb_image.h`) | public domain (Unlicense) or MIT, at the user's choice | https://github.com/nothings/stb |
| FCEUmm (libretro-fceumm), commit `ac0d00a7fc01166f84c7bac01d1084ebfd2b865e` | the NES emulator inside the game player for Play on PC (`tools/player/third_party/fceumm`, linked into `ccs-player.exe`; `VENDORED.md` lists what is compiled) | GNU GPL version 2 or (at your option) any later version; the combined player is distributed under GPL version 3 | https://github.com/libretro/libretro-fceumm |
| libretro-common (the parts FCEUmm compiles) | inside the FCEUmm sources (`src/drivers/libretro/libretro-common`) | MIT-style permission notice in each file | https://github.com/libretro/libretro-common |
| pic16c5x | inside the FCEUmm sources (`src/boards/pic16c5x.c`) | BSD-3-Clause (Tony La Porta, from MAME) | https://github.com/mamedev/mame |
| emu2413 0.60, as modified for FCE Ultra and FCEUmm | inside the FCEUmm sources (`src/boards/emu2413.c`, `emu2413.h`), the YM2413 (VRC7) sound of a few cartridges | MIT License, Copyright (c) 2001-2019 Mitsutaka Okazaki: the license of the author's emu2413 project (full text below and in `tools/player/third_party/fceumm/LICENSE.emu2413.txt`); the FCE Ultra/FCEUmm changes come with FCEUmm (GPL v2 or later) | https://github.com/digital-sound-antiques/emu2413 (LICENSE) |
| SDL_GameControllerDB, commit `07af1d50187e94c861c9f9f5d8a55b148f11306b` | the layouts of known game controllers, as data: `tools/player/core/gp_pad_db.h` in the game player and `play/pad-db.json` on the website (`tools/player/third_party/sdl_gamecontrollerdb`, `VENDORED.md`); no SDL code | zlib License (`tools/player/third_party/sdl_gamecontrollerdb/LICENSE`) | https://github.com/mdqinc/SDL_GameControllerDB |
| wasi-libc, with its musl, cloudlibc and dlmalloc parts (as carried by zig 0.17.0) | the C library inside the website's game player (`play/ccs-player.wasm`) | Apache-2.0 with LLVM Exceptions, or Apache-2.0, or MIT, at your choice; musl parts MIT; cloudlibc parts BSD-2-Clause; dlmalloc CC0 (texts in `tools/player/third_party/zig-runtime`) | https://github.com/WebAssembly/wasi-libc |
| mingw-w64 runtime (as carried by zig 0.17.0) | the C runtime inside the Windows game player (`ccs-player.exe`) | Zope Public License 2.1, except parts marked public domain, BSD or LGPL; Copyright (c) 2009 - 2013 by the mingw-w64 project (text in `tools/player/third_party/zig-runtime/mingw-w64-COPYING`) | https://www.mingw-w64.org |
| compiler-rt (as carried by zig 0.17.0) | low-level arithmetic routines inside both game players | Apache-2.0 with LLVM Exceptions | https://github.com/llvm/llvm-project |
| Hakchi2 CE 3.9.3 (tag `v3.9.3`, commit `25bfcfd04b8ed12f7d513dc87c2a0f7096d38ebc`): its clovershell connection, its SSH connection and its console identification | adapted into the console transport worker (`tools/console-worker`, `ccs-console-worker.exe` in the Windows app's `_internal\console-worker`), which Classic Cart Studio would use to reach a console; its real connections are closed in its own code in this version. `tools/console-worker/SOURCE.md` lists each adapted file and the changes | GNU GPL v3; the worker is distributed under GPL-3.0-or-later with its own `NOTICE` | https://github.com/TeamShinkansen/Hakchi2-CE (tag `v3.9.3`). Original hakchi2 by madmonkey and ClusterM; thank you to Team Shinkansen |
| SSH.NET, Team Shinkansen's fork at commit `32181aeb4ce69e8f07e5e1d585fd592a44017bc1` (the Hakchi2 CE 3.9.3 submodule) | compiled unmodified from that source into `Renci.SshNet.dll` beside the console transport worker | MIT License (text in `tools/console-worker/licenses`) | https://github.com/TeamShinkansen/SSH.NET |
| LibUsbDotNet 2.2.29 | `LibUsbDotNet.LibUsbDotNet.dll` beside the console transport worker (byte-identical to the copy Hakchi2 CE 3.9.3 embeds) | GNU LGPL v3 (text in `tools/console-worker/licenses`) | https://github.com/LibUsbDotNet/LibUsbDotNet |

No GPL-3.0-only code is compiled into Classic Cart Studio's own programs, so they are distributed under GPL-3.0-or-later. The Hakchi2 CE package carried by the Windows installer is a separate, unmodified program under its own license (see below).

## emu2413 license (MIT)

The game player compiles emu2413 as carried in FCEUmm (Mitsutaka Okazaki's version 0.60, modified in 2004 by xodnizel for FCE Ultra and
since then in FCEUmm). The author's emu2413 project grants:

> The MIT License (MIT)
>
> Copyright (c) 2001-2019 Mitsutaka Okazaki
>
> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the
> "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge,
> publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
> subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
> MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR
> ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH
> THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## Bundled and referenced material

| Item | What it is | Bundled? | License / status | Source |
|---|---|---|---|---|
| Hakchi2 CE 3.9.3 | Console-side software (custom kernel, user storage, game sync). Classic Cart Studio prepares collections in its format and starts it. It is a separate program, included unmodified with its own license. | **Yes**, in the Windows installer: the official `hakchi2-ce-3.9.3-portable.zip` (with its `LICENSE`) in `_internal\packages`, verified by size and SHA-256 before use, so Hakchi setup and its console driver need no download. Thank you to Team Shinkansen and everyone behind hakchi2. | GNU GPL v3 (the package includes its license); its components keep their own licenses | Source: https://github.com/TeamShinkansen/Hakchi2-CE (tag `v3.9.3`); a source archive of that tag with its submodules is attached to each Classic Cart Studio release. Original hakchi2 by madmonkey and ClusterM |
| Console USB driver | WinUSB driver bundled inside the Hakchi2 CE package (`driver\classic_driver.exe`, a libwdi-generated installer); installed only on request with administrator approval. | Inside the bundled Hakchi package (see above) | libwdi: LGPL v3; WinUSB: Microsoft redistributable | https://github.com/pbatard/libwdi — thank you to Pete Batard |
| Press Start 2P | Pixel font used for labels | Yes (`site-data/brand/fonts`) | SIL Open Font License 1.1 (`OFL-PressStart2P.txt`) | Copyright 2012 The Press Start 2P Project Authors |
| Barlow, Silkscreen, Cinzel, VT323, Pixelify Sans, Space Mono | Fonts of the console HOME (Preview on TV and the console runtime), the high-resolution Browser HOME (Barlow, `tools/publish/assets/home/hd`) and the game player's menus (Play on PC; atlases baked from Barlow and Silkscreen) | Yes (`tools/publish/assets/home/fonts`, `tools/publish/assets/home/hd`, `tools/companion/native_home/fonts`, the player's `player/fonts`) | SIL Open Font License 1.1 (the `OFL-*.txt` beside each font) | their authors, named in each OFL file |
| Classic Cart Studio cast | `site-data/brand/ccs`: the Studio's pixel characters (two walking frames each), items, interface icons and walkway tile | Yes | Original artwork of this project, drawn in code by `tools/publish/make_ccs_cast.py`; GPL-3.0-or-later with the rest of the project | this repository |
| HOME theme art and game illustrations | `tools/publish/assets/home` (the four HOME theme scenes, overlays and thumbnails, cut from image-generator mockups made for this project) and `site-data/art/hero` (102 game illustrations made with image generators for this project) | Yes | Kept as fan art: the game characters and worlds they depict belong to their owners; no license over them is claimed and this project's license does not cover them | this project (generation records kept privately) |
| Game box art | `site-data/art`: cover thumbnails (the website carries the published set; the Windows installer carries every optimized display image, never the original scans) | Yes | publishers' works; community scans from libretro-thumbnails (Game Details names the source and links the source file); no license claimed | https://github.com/libretro-thumbnails/Nintendo_-_Nintendo_Entertainment_System — thank you to the libretro-thumbnails contributors |
| No-Intro database | Names and hashes used to identify verified releases | Identification data only (`site-data/*.json`) | No-Intro DAT files are provided for identification; see their site for terms | https://datomatic.no-intro.org |
| NesCartDB / Mesen database | Cartridge board, mapper and header data | Identification data only | community databases; see their sites | NesCartDB (bootgod); Mesen (SourMesen) |
| Ranking sources | Third-party "best NES games" lists cited by name and link in each game's details | Links and positions only | each list belongs to its publisher | linked in the Studio |
| Microsoft Edge WebView2 | Runtime that displays the website inside Classic Cart Studio for Windows; the SDK (`Microsoft.Web.WebView2` NuGet package: `Microsoft.Web.WebView2.Core.dll`, `Microsoft.Web.WebView2.WinForms.dll`, `WebView2Loader.dll`) is bundled with the application | SDK yes (in the installer); runtime no (part of Windows 10/11 with Microsoft Edge) | Microsoft Software License Terms for the WebView2 SDK; runtime under the Microsoft Edge license | https://developer.microsoft.com/microsoft-edge/webview2/ |
| .NET Framework 4.8 | Runs the Windows application host (`ClassicCartStudio.exe`) | No (part of Windows 10 1903+ and Windows 11) | Microsoft | https://dotnet.microsoft.com |
| Python 3 and the standard library | Companion runtime (frozen with PyInstaller) | Yes, in the installer | PSF license | https://www.python.org |
| PyInstaller bootloader | Starts the frozen companion | Yes, in the installer | GPL with the bootloader exception that permits distributing frozen programs under their own license | https://pyinstaller.org |
| Inno Setup | Builds the installer | No (build tool only) | Inno Setup license | https://jrsoftware.org/isinfo.php |
| GitHub Actions actions | Build and deploy workflows (`actions/checkout`, `setup-python`, `upload-pages-artifact`, `deploy-pages`, `attest-build-provenance`, `upload-artifact`) | No (CI only) | MIT | https://github.com/actions |

The website uses no JavaScript frameworks or external scripts; everything in the page is this project's code plus the assets above.

## Artwork and data

Box art is shown to identify the games and is attributed as far as its origins are known; the HOME theme art and game illustrations are fan art made for this project. They remain the property of their owners and are not covered by this project's license. A rights holder's request to remove material will be honored. Game identification data (names and hashes) comes from the No-Intro, NesCartDB and Mesen databases, under their respective terms.

## Console startup images

The five images a console can show while it starts (Forgotten Keep, Galactic Frontier, Kingdoms Beyond, Neon Highway and Pixel Worlds; `tools/publish/assets/startup`, and in the Windows app the console packages it builds) were made for Classic Cart Studio and chosen by the project owner (Rance9). They are creative assets shipped with Classic Cart Studio, not part of the software: this project's GPL-3.0-or-later license does not apply to them. Each is a 1280 x 720 copy of the owner's master (the whole picture, resampled; nothing else changed); the masters themselves are not published. The website carries only small preview copies.

## HOME Music

The theme every HOME plays (`brand/home/music/ccs-home-theme-7d07857f25e3.m4a` on the website; `home-music/ccs-home-theme.wav` in the Windows app and in each console HOME package) was created by the Classic Cart Studio project owner (Rance9) with Suno, under the owner's own Suno subscription, and chosen by the owner for the HOME. It is a creative asset shipped with Classic Cart Studio, not part of the software: this project's GPL-3.0-or-later license does not apply to it. The website file carries the song's original AAC stream unchanged; the native file is the same stream decoded once to 16-bit stereo PCM at 48,000 Hz. Neither is shortened, faded or remixed. Its identity is recorded in `tools/publish/assets/home-music/music.json`.

## Application icon

The Classic Cart Studio icon (`tools/publish/assets/app.ico`, the website favicon set and the installer wizard images) is original artwork generated by `tools/publish/make_icon.py`: a simplified drawing of the NES Classic Edition controller's shape (gray body, dark panel, cross D-pad, two pills, two red buttons) on a transparent background, made from photographs. It contains no Nintendo wordmark or logo. The website's hero drawings of the console and controller are likewise original drawings made from photographs. Nintendo and NES are trademarks of Nintendo; this project is independent and not affiliated with or endorsed by Nintendo.

## “AS SEEN ON TV” badge

`tools/publish/assets/as-seen-on-tv.png` (served as `brand/public/as-seen-on-tv.png`, 320 × 250) is the Wikimedia Commons file `As_seen_on_TV.png` (https://commons.wikimedia.org/wiki/File:As_seen_on_TV.png, original file https://commons.wikimedia.org/wiki/Special:Redirect/file/As_seen_on_TV.png), which its creator released into the public domain. The only change is that the white area outside the red shape was made transparent so the badge sits on the dark toolbar; the red shape and its white lettering are the original pixels. It is shown scaled proportionally, at most 48 px wide, as the **Preview on TV** button in the catalog toolbar (to the right of List / Grid) and nowhere else. The public-domain status of this reproduction does not resolve every possible trademark question, and its use here is not a claim that Classic Cart Studio was advertised on television or is sponsored or endorsed by any television marketer.

## Photographs of the NES Classic Edition in the Help & User Guide

`tools/publish/assets/help/photo-nes-classic-front.jpg` and `photo-nes-classic-back.jpg` (served as `brand/help/`) are the Wikimedia Commons files "NES Classic Edition front right corner view" and "NES Classic Edition back right corner view" by **DemonDays64**, licensed [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) (https://commons.wikimedia.org/wiki/File:NES_Classic_Edition_front_right_corner_view.jpg, https://commons.wikimedia.org/wiki/File:NES_Classic_Edition_back_right_corner_view.jpg). Changes: resized to 1600 px and re-encoded as JPEG. The photographs show Nintendo's product; Nintendo, Nintendo Entertainment System and NES Classic Edition are trademarks of Nintendo, and the use here is identification only.

## mingw-w64 runtime license (ZPL 2.1)

The Windows game player (`ccs-player.exe`) contains parts of the mingw-w64 runtime that zig links into Windows programs. Its license, as
carried in zig 0.17.0 (`lib/libc/mingw/COPYING`):

```
With exception of certain parts that are prominently marked as being
in the Public Domain, BSD, or LGPL this Software is provided under the
Zope Public License (ZPL) Version 2.1.

Copyright (c) 2009 - 2013 by the mingw-w64 project

See the AUTHORS file for the list of contributors to the mingw-w64 project.

This license has been certified as open source. It has also been designated
as GPL compatible by the Free Software Foundation (FSF).

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

   1. Redistributions in source code must retain the accompanying copyright
      notice, this list of conditions, and the following disclaimer.
   2. Redistributions in binary form must reproduce the accompanying
      copyright notice, this list of conditions, and the following disclaimer
      in the documentation and/or other materials provided with the
      distribution.
   3. Names of the copyright holders must not be used to endorse or promote
      products derived from this software without prior written permission
      from the copyright holders.
   4. The right to distribute this software or to use it for any purpose does
      not give you the right to use Servicemarks (sm) or Trademarks (tm) of
      the copyright holders.  Use of them is covered by separate agreement
      with the copyright holders.
   5. If any files are modified, you must cause the modified files to carry
      prominent notices stating that you changed the files and the date of
      any change.

Disclaimer

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS ``AS IS'' AND ANY EXPRESSED
OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO
EVENT SHALL THE COPYRIGHT HOLDERS BE LIABLE FOR ANY DIRECT, INDIRECT,
INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE,
EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```
