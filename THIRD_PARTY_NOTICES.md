# Third-party notices

The project's own code (Python tools, the backend, the Windows application host in C#, the website's HTML/CSS/JavaScript) is under the MIT license in `LICENSE`. That license does **not** cover the third-party material below, which remains under its owners' rights and licenses. This file records what is bundled, what is downloaded separately, and where each item comes from.

| Item | What it is | Bundled? | License / status | Source |
|---|---|---|---|---|
| Hakchi2 CE 3.9.3 | Console-side software (custom kernel, user storage, game sync). The companion prepares collections in its format and launches it. | **No.** Downloaded on request from its official releases page and verified by size and SHA-256 (`tools/companion/hakchi_bridge.py`). | GNU GPL v3 (the package includes its license) | https://github.com/TeamShinkansen/Hakchi2-CE — original hakchi2 by madmonkey and ClusterM |
| Console USB driver | WinUSB driver bundled inside the Hakchi2 CE package (`driver\classic_driver.exe`, a libwdi-generated installer); installed only on request with administrator approval. | No (part of the Hakchi package) | libwdi: LGPL v3; WinUSB: Microsoft | https://github.com/pbatard/libwdi |
| Press Start 2P | Pixel font used for labels | Yes (`site-data/brand/fonts`) | SIL Open Font License 1.1 (`OFL-PressStart2P.txt`) | Copyright 2012 The Press Start 2P Project Authors |
| Firmware sprite crops and console renderings | `site-data/brand/oem`: pixel characters, interface icons, console/controller images and the NES wordmark taken from the NES Classic Edition system software | Yes | Nintendo's works; used to identify and present the games; no license claimed | NES Classic Edition system software |
| Community character sprites | `site-data/brand/web`: 19 character sprites (origins per entry in `provenance.json`) | Yes | Nintendo's works, ripped by the community; no license claimed | listed in `site-data/brand/web/provenance.json` |
| Game box art | `site-data/art`: cover thumbnails for 745 curated games | Yes | publishers' works; community scans (libretro-thumbnails style collections); no license claimed | community thumbnail collections |
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

## Open questions recorded for review

- The firmware sprite crops, community character sprites and box art are shown as identification and presentation of the games and are attributed as far as their origins are known; no license to redistribute them has been granted by their owners. Whether that use is acceptable in a given jurisdiction is a question for qualified legal review, and a rights holder's request to remove material will be honored.
- The No-Intro database terms for derived identification data (names and hashes) should be confirmed before any redistribution beyond this site.

## Application icon

The Classic Cart Studio icon (`tools/publish/assets/app.ico`, the website favicon set and the installer wizard images) is original artwork generated by `tools/publish/make_icon.py`: a generic gamepad silhouette on a dark tile. It depicts no console, character or trademark.

## “AS SEEN ON TV” badge

`tools/publish/assets/as-seen-on-tv.svg` (served as `brand/public/as-seen-on-tv.svg`) is the Wikimedia Commons reproduction of the generic red-and-white television-shaped “As seen on TV” graphic, file `As_seen_on_TV.svg` (https://commons.wikimedia.org/wiki/File:As_seen_on_TV.svg), whose file description identifies the contributed drawing as public domain. It is used only as the **Preview on TV** button (accessible name “Preview on TV — proposed NES Classic menu”). It is not a statement that Classic Cart Studio was advertised on television or is sponsored or endorsed by any television marketer, and no other seller’s or retailer’s logo is used.
