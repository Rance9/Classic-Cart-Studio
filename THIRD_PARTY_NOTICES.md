# Open Source Credits & Licenses

## Classic Cart Studio

Copyright (C) 2026 Rance9 (Classic Cart Studio).

The project's own code (Python tools, the backend, the Windows application host in C#, the website's HTML/CSS/JavaScript, and the native HOME renderer and runtime) is free software: you can redistribute it and/or modify it under the terms of the **GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version** (`LICENSE`). It is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.

The project's license does **not** cover the third-party material below, which remains under its owners' rights and licenses. This file records what is bundled, what is downloaded separately, and where each item comes from.

## Code incorporated from other projects

| Code | Where | License | Source |
|---|---|---|---|
| stb_image v2.30 | image decoding in the native HOME renderer (`tools/console-simulator/core/third_party/stb_image.h`) | public domain (Unlicense) or MIT, at the user's choice | https://github.com/nothings/stb |

No GPL-3.0-only code is compiled into Classic Cart Studio's own programs, so they are distributed under GPL-3.0-or-later. The Hakchi2 CE package carried by the Windows installer is a separate, unmodified program under its own license (see below).

## Bundled and referenced material

| Item | What it is | Bundled? | License / status | Source |
|---|---|---|---|---|
| Hakchi2 CE 3.9.3 | Console-side software (custom kernel, user storage, game sync). Classic Cart Studio prepares collections in its format and starts it. It is a separate program, included unmodified with its own license. | **Yes**, in the Windows installer: the official `hakchi2-ce-3.9.3-portable.zip` (with its `LICENSE`) in `_internal\packages`, verified by size and SHA-256 before use, so Hakchi setup and its console driver need no download. Thank you to Team Shinkansen and everyone behind hakchi2. | GNU GPL v3 (the package includes its license); its components keep their own licenses | Source: https://github.com/TeamShinkansen/Hakchi2-CE (tag `v3.9.3`); a source archive of that tag with its submodules is attached to each Classic Cart Studio release. Original hakchi2 by madmonkey and ClusterM |
| Console USB driver | WinUSB driver bundled inside the Hakchi2 CE package (`driver\classic_driver.exe`, a libwdi-generated installer); installed only on request with administrator approval. | Inside the bundled Hakchi package (see above) | libwdi: LGPL v3; WinUSB: Microsoft redistributable | https://github.com/pbatard/libwdi — thank you to Pete Batard |
| Press Start 2P | Pixel font used for labels | Yes (`site-data/brand/fonts`) | SIL Open Font License 1.1 (`OFL-PressStart2P.txt`) | Copyright 2012 The Press Start 2P Project Authors |
| Firmware sprite crops and console renderings | `site-data/brand/oem`: pixel characters, interface icons, console/controller images and the NES wordmark taken from the NES Classic Edition system software | Yes | Nintendo's works; used to identify and present the games; no license claimed | NES Classic Edition system software |
| Community character sprites | `site-data/brand/web`: 19 character sprites (origins per entry in `provenance.json`) | Yes | Nintendo's works, ripped by the community; no license claimed | listed in `site-data/brand/web/provenance.json` |
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

The firmware sprite crops, community character sprites and box art are shown to identify and present the games and are attributed as far as their origins are known. They remain the property of their owners and are not covered by this project's license. A rights holder's request to remove material will be honored. Game identification data (names and hashes) comes from the No-Intro, NesCartDB and Mesen databases, under their respective terms.

## Application icon

The Classic Cart Studio icon (`tools/publish/assets/app.ico`, the website favicon set and the installer wizard images) is original artwork generated by `tools/publish/make_icon.py`: a simplified drawing of the NES Classic Edition controller's shape (gray body, dark panel, cross D-pad, two pills, two red buttons) on a transparent background, made from photographs. It contains no Nintendo wordmark or logo. The website's hero drawings of the console and controller are likewise original drawings made from photographs. Nintendo and NES are trademarks of Nintendo; this project is independent and not affiliated with or endorsed by Nintendo.

## “AS SEEN ON TV” badge

`tools/publish/assets/as-seen-on-tv.png` (served as `brand/public/as-seen-on-tv.png`, 320 × 250) is the Wikimedia Commons file `As_seen_on_TV.png` (https://commons.wikimedia.org/wiki/File:As_seen_on_TV.png, original file https://commons.wikimedia.org/wiki/Special:Redirect/file/As_seen_on_TV.png), which its creator released into the public domain. The only change is that the white area outside the red shape was made transparent so the badge sits on the dark toolbar; the red shape and its white lettering are the original pixels. It is shown scaled proportionally, at most 48 px wide, as the **Preview on TV** button in the catalog toolbar (to the right of List / Grid) and nowhere else. The public-domain status of this reproduction does not resolve every possible trademark question, and its use here is not a claim that Classic Cart Studio was advertised on television or is sponsored or endorsed by any television marketer.

## Photographs of the NES Classic Edition in the Help & User Guide

`tools/publish/assets/help/photo-nes-classic-front.jpg` and `photo-nes-classic-back.jpg` (served as `brand/help/`) are the Wikimedia Commons files "NES Classic Edition front right corner view" and "NES Classic Edition back right corner view" by **DemonDays64**, licensed [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) (https://commons.wikimedia.org/wiki/File:NES_Classic_Edition_front_right_corner_view.jpg, https://commons.wikimedia.org/wiki/File:NES_Classic_Edition_back_right_corner_view.jpg). Changes: resized to 1600 px and re-encoded as JPEG. The photographs show Nintendo's product; Nintendo, Nintendo Entertainment System and NES Classic Edition are trademarks of Nintendo, and the use here is identification only.
