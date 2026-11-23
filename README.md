# Classic Cart Studio

**Free, open-source tools for the NES Classic and the SNES Classic.** Classic Cart Studio collects, organizes and plays your own NES games, in your web browser, in the installable website app or in Classic Cart Studio for Windows, with the open-source FCEUmm emulator built in, and prepares collections for a Nintendo NES Classic Edition. Its game player is an NES emulator; it does not play SNES games.

**CCS Console Workbench**, part of the same project, is a Windows program for the consoles themselves: backing up, restoring, repairing and composing an NES Classic, with SNES Classic support being built, and browsing, checking and repairing those backups on a PC. Classic Cart Studio for Windows includes it, and it is also available on its own as a single file that runs without installation. Its source is in [`tools/console-backup`](tools/console-backup).

**Downloads:** Classic Cart Studio for Windows and CCS Console Workbench are both at [ClassicCartStudio.com/app/](https://ClassicCartStudio.com/app/), with their checksums. The website is [ClassicCartStudio.com](https://ClassicCartStudio.com).

**License summary**

- Classic Cart Studio's own code, CCS Console Workbench included, is licensed under the GNU General Public License version 3 or later ([LICENSE](LICENSE)). Code adapted from other projects keeps its own license; [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) lists each one.
- The names Classic Cart Studio, CCS Console Workbench and Rance9 are not licensed for use by other projects.
- The HOME theme illustrations, the console startup pictures and the HOME theme music, made for this project with image generators and with Suno and already excluded from the GPL in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md), remain the property of Rance9 LLC; all rights reserved. The characters, icons and walkway drawn in code are covered by the GPL, and Luis Zuno's scene backgrounds keep their own licenses (Magic Cliffs CC BY 3.0, the other four CC0).
- Game covers, screenshots and identification data remain the property of their respective owners; their public availability does not grant permission to use them.

> **Current development preview:** unsigned. CCS Console Workbench reads a real console (Back Up Console and the Console Browser check-up); every console write and driver action is turned off in this version, and Classic Cart Studio's own console connections are turned off. The standalone Workbench is a separate preview. Pinned upstream binaries remain bundled with unresolved source/redistribution questions disclosed in [SOURCE-AUDIT.md](tools/console-backup/SOURCE-AUDIT.md); available source archives do not establish complete corresponding source.

Classic Cart Studio is one Studio in two places. The **website** lets you browse the NES game library, choose games and the exact versions you prefer, build and save collections, and read the complete Help & User Guide, with nothing to install. **Classic Cart Studio for Windows** shows the same Studio in its own application window, installed with it so it works offline, and adds everything that needs your computer: reading the games already set up on your PC, importing your own game files (ZIP archives included), keeping managed copies, and preparing the collection for Hakchi2 CE. A collection you build on the website continues in the Windows application with **Continue in Desktop App**; your selections, including the exact versions you chose, come with you.

> Classic Cart Studio is an independent, open-source project and is not produced, licensed, sponsored, approved, or endorsed by Nintendo. Nintendo, Nintendo Entertainment System, NES, NES Classic Edition, Super Nintendo Entertainment System, Super NES, SNES and Super NES Classic Edition are trademarks of Nintendo. Game titles, publisher names, artwork and box art are used to identify and present games; all rights remain with their owners. **No original commercial game files, Nintendo firmware or Nintendo software are distributed by this project;** the only game files it supplies are its Included Games, homebrew whose authors license or permit it to share them. The Windows installer includes the separate open-source Hakchi2 CE 3.9.3 package, unmodified, with its own license (see below).

## What you can do

- Browse a library of 773 curated North American releases plus an extended index of regional releases, revisions, translations and other versions, with version chips, technical details (mapper, header, hashes, emulator) and controller-compatibility notes.
- Preview on TV: the AS SEEN ON TV badge opens an interactive 16:9 preview of the HOME menu your collection would give the NES Classic.
- Build a collection: the 30 games that came with the console are already there; choose what stays visible and what you add. Back it up as a complete `.ccs` file (every game with its game file) or back up everything at once, keep automatic snapshots, and restore.
- Continue in the Windows application: the collection you built on the website is handed over as it is (names, exact game versions, factory-game choices). The application then helps you find the matching game files on your computer: individual files, several files, ZIP archives or whole folders, every file identified by its exact content, never by file name.
- Add your own homebrew with artwork, load the games already set up on your computer (an existing Hakchi2 CE library), and prepare the collection for Hakchi2 CE.
- Installation on the console is done by Hakchi2 CE under your control. This version prepares everything on the PC and shows the console steps honestly as not automated.

## Website vs Windows application

| | Website only | Classic Cart Studio for Windows |
|---|---|---|
| Browse, filter, plan, save/load collections, read the guide | yes | yes (the same Studio, installed with it) |
| Continue a website collection without starting over | — | yes (Continue in Desktop App) |
| Read the games already on your computer | no | yes |
| Add games from files / folders / ZIPs | no | yes |
| Prepare a collection for Hakchi2 CE, open Hakchi | no | yes |
| Anything sent to the console by this software | never | only CCS Console Workbench's read program, to the console's memory, when you start Back Up Console or the check-up; nothing is written to the console's storage |

The Windows application starts a small backend that listens only on your own computer (`127.0.0.1`). The application window talks to it over a session that exists only while the application runs; there is no pairing step inside the application. The public website never connects to that backend on its own: when you choose something that needs your computer, it hands your collection to the application (Edge asks once whether the website may access apps on your device; see the guide chapter *Continuing in the Desktop App*), or sends you to [ClassicCartStudio.com/app](https://ClassicCartStudio.com/app) to install it.

## Installation (Windows 10/11)

1. On the website, choose **Continue in Desktop App** (My Collection menu or Install on NES Classic) or go to the download page, [ClassicCartStudio.com/app/](https://ClassicCartStudio.com/app/).
2. Download `ClassicCartStudio-Setup-<version>.exe`.
3. **Check it** (recommended): in PowerShell run
   `Get-FileHash -Algorithm SHA256 "$env:USERPROFILE\Downloads\ClassicCartStudio-Setup-<version>.exe"`
   and compare the result with `SHA256SUMS.txt` on the release (the website shows the same value with a Copy button).
4. Run the installer. It shows the Installation Terms, the GNU General Public License and the third-party notices, and installs only after you check the agreement box. It installs for your user account only (no administrator rights), under `%LOCALAPPDATA%\Programs\ClassicCartStudio`, adds a Start-menu entry **Classic Cart Studio**, and keeps a copy of those documents in the program's `legal` folder. A silent installation (`/SILENT`, `/VERYSILENT`) shows no pages and records no agreement.
5. Start Classic Cart Studio from the Start menu. It opens the Studio in its own window with the backend already connected. If you came from the website with Continue in Desktop App, the application shows the collection and asks before opening it.

Classic Cart Studio needs .NET Framework 4.8, which is part of Windows 10 (version 1903 and later) and Windows 11, and the Microsoft Edge WebView2 Runtime, a separate Microsoft component (not the Microsoft Edge browser) that Windows 11 includes and most Windows 10 computers already have. The installer checks for a usable Runtime before it installs anything; when there is none, it adds it from its own copy of Microsoft's offline installer, silently and without an internet connection, and stops with an explanation if that does not work. Removing Classic Cart Studio leaves the Runtime in place, because other apps share it. No internet connection is needed to use it (it checks classiccartstudio.com for updates when it can, which you can turn off): its interface, library and artwork are installed with it.

**Upgrading from an earlier release**: from 0.11.0 on, the app offers new versions itself (**Update Now**); or run the new installer; there is one installation per Windows account, and it replaces the program and keeps your data (imported games, personal games, collections, Hakchi setup) in `%LOCALAPPDATA%\ClassicCartStudio`. **Uninstalling**: Windows Settings → Apps. Your data folder stays until you delete it yourself.

### Why the installer is not signed

A signed installer names its publisher; an installer without a signature shows "unknown publisher", and Windows SmartScreen can warn about a new publisher or a new download either way, until Windows has seen it often enough. Each release says whether its installer is signed. What you get instead: every release is built by a public GitHub Actions workflow from the tagged source, the workflow run is linked from the release notes, a build-provenance attestation is attached, and the SHA-256 of the installer is published. If the hash you compute does not match, do not run the file. A matching hash proves the download is the file the workflow built; it does not by itself prove who published it, which is what the attestation and the public workflow are for.

### Hakchi2 CE and the console driver

Preparing a collection needs [Hakchi2 CE](https://github.com/TeamShinkansen/Hakchi2-CE) 3.9.3 (GPL v3, a separate program by Team Shinkansen; thank you). The installer includes the unmodified official release package with its license, verified by size and SHA-256 against the values pinned in `tools/companion/hakchi_bridge.py`, so the application sets up the portable build without downloading anything. It can also run Hakchi's bundled console USB driver installer (that one step asks for administrator rights, because installing a driver requires it). Nothing is written to the console by this project.

## Your game files

Apart from its Included Games (homebrew it hosts and ships under each author's license or permission, fetched only from ClassicCartStudio.com or the installed app when you play or add one), the Studio never downloads or ships games. It works with files **you already own**, for example dumps of your own cartridges. When you add files, the application keeps its own copy and never modifies, moves or deletes your original. Games that are in the library but have no file on your computer are shown as **missing**: use **Find My Game Files** or **Locate Game File…** to point the Studio at your copy; it accepts the file only if its content is exactly that version (no substitutions). The Internet Archive and similar sites are referenced in the documentation only as *provenance* of public identification data (hashes, names); this project includes no downloader and does not encourage downloading copyrighted games.

### Play one game file from the command line

Classic Cart Studio for Windows can open one game file straight in its game player, for example a homebrew game you are building:

```
ClassicCartStudio.exe --play "C:\Games\My Game.nes"
```

- **The file**: one `.nes` file with its header (or a file without one whose content the Game Library knows). Quote a path that has spaces; any letters work. A relative path is read from the folder the command runs in.
- **What opens**: only the game player's window, with that game. No Classic Cart Studio window opens; one that is already open is not brought forward or changed. There is no HOME screen.
- **How it starts**: like every launch, the player first offers its start choice (**Load Last Used Save**, **Load a Save Slot…**, **Start New Game**); the game runs once you choose.
- **The file itself**: read as it is at that moment, every time, so a file rebuilt under the same name runs its new bytes. It is never written, moved, held open or added to the Game Library or a collection.
- **Saves and settings**: the game's saves and its game and controller settings are the same ones every way of playing that game uses (they follow the game's content, not its file name), in `%LOCALAPPDATA%\ClassicCartStudio` (`saves`, `game-settings`, `player-settings.json`), or in the folder `CCS_STUDIO_DATA` names when it is set. The run's own working folder under `play` is removed when the player closes.
- **Which file ran**: the file's full SHA-256 identifies its exact bytes. Saves follow the game's content (the file without its header), so two files that differ only in their header share saves, and a save shows which game ran, not which of those files.
- **A separate data folder**: set `CCS_STUDIO_DATA` to a full path for that command only (`set CCS_STUDIO_DATA=D:\Test\CCS` in that command window, or `$env:CCS_STUDIO_DATA='D:\Test\CCS'` in that PowerShell window) and the player, its saves, settings and working folder use that folder; nothing else on the computer changes. A Classic Cart Studio window started the same way uses that folder for everything: its library, its saves and its Hakchi2 CE preparation (with its own copy of the included Hakchi2 CE). It never hands a file or a link to a window that uses another folder; when that other window already holds the app's connection on this computer, it says so, and you close the other window first. Keep that folder's path to about 65 characters or fewer: Hakchi2 CE's own files inside it need up to about 195 more within Windows' 260-character path limit. `NES_STUDIO_DATA` is not read: while it is set, the command and the app refuse to start (exit code `2`) rather than use the normal folder, so a command that sets it must set `CCS_STUDIO_DATA` instead.
- **Waiting**: the command ends when the player window closes. It is a Windows program, so in a command window use `start "" /wait ClassicCartStudio.exe --play "C:\Games\My Game.nes"` and read `%ERRORLEVEL%`; in PowerShell, `(Start-Process ClassicCartStudio.exe -ArgumentList '--play','"C:\Games\My Game.nes"' -Wait -PassThru).ExitCode`.
- **Exit codes**: `0` the player closed normally, including when it is closed at its start choice (this is not a report that the game ran correctly); `1` the player ended with an error; `2` the command was not used as above (including while `NES_STUDIO_DATA` is set: nothing runs); `3` the file cannot be played (not there, not readable, or not an NES game file the player can start); `4` a game is already open in the player (it is left as it is: close it first); `5` the player could not start (a missing or damaged installation).
- **Messages**: a failure is shown in a small message window and written to the command window when there is one; add `--quiet` to leave out the message window.

## Privacy

The website keeps your collections and settings, and the games you import or choose to play with their saved games, in your browser's storage for this site; the browser can clear it, and on an iPhone Safari and the Home Screen app keep separate stores, so keep your `.ccs` backups. The Windows application keeps its data in `%LOCALAPPDATA%\ClassicCartStudio` on your PC. Game files and backups are never uploaded. The website loads its own files (GitHub Pages, delivered through Cloudflare), asks GitHub for the latest release, and counts visits and page performance with Cloudflare Web Analytics (no cookies); the Windows app's own interface does not include it. Optional **Usage Reporting** sends a few counts, such as "a game started" with the release version and browser family, to the project's Cloudflare collector, never game files, names, saves or identifiers. On the website nothing is sent until you turn it on. The Windows setup asks on its own page, separate from the Installation Terms: the box starts checked (unchecked in the EU, the EEA, the UK and Switzerland; uncheck it to send nothing), and a choice already saved on the computer is kept. Change it anytime in the Studio menu; every feature works either way. Turning it off does not cover the website's Cloudflare Web Analytics or the ordinary delivery of the website and downloads by GitHub and Cloudflare, which process connection information under their own terms. There is no account and no advertising. Details: [PRIVACY.md](PRIVACY.md).

## Building

This repository holds what is needed to build the application and the website; the two workflows in `.github/workflows/` are the build. `release.yml` builds Classic Cart Studio for Windows in order, with its checks: the native HOME renderer and game player, the console transport worker, the console programs, CCS Console Workbench (which the installer carries), the backend, the Windows application and the installer, then the release layout, desktop self-test, content scans, checksums and build attestation. `pages.yml` builds, checks and deploys the website. CCS Console Workbench's own release workflow is on the `CCS-Console-Workbench` branch. Every build uses Python 3.13.17 with the exact packages in `tools/publish/requirements-build.txt`:

```
python -m pip install -r tools/publish/requirements-build.txt
python tools/publish/build_site_ci.py build/site                     # the website (and the copy the application bundles) from site-data/
```

Layout: `tools/rom-pipeline/` (data model, page generator, the Studio front end in `studio/`), `tools/companion/` (the backend, Hakchi bridge, native HOME packager, installer script), `tools/desktop/ClassicCartStudio/` (the Windows application), `tools/console-simulator/` (the native HOME renderer: C sources and the pinned toolchain), `tools/player/` (the game player), `tools/console-backup/` (CCS Console Workbench), `tools/console-worker/` (the console transport worker), `tools/console-nucleus/` (CCS Mod, CCS Nucleus and the CCS HOME package), `tools/publish/` (website build, content scanner, release helpers, assets), `site-data/` (library data and artwork).

## Support

Classic Cart Studio is free and open source. If you've found it useful, optional contributions through [GitHub Sponsors](https://github.com/sponsors/Rance9) are always appreciated and help support continued independent development. Nothing in the Studio depends on it: no paid features, no gates, no prompts.

The console the creator used was bought from an eBay seller recommended in the guide's "Getting an NES Classic" chapter. That is a personal recommendation, not a sponsored listing or affiliate relationship: no commission or referral compensation is received, and it is independent of GitHub Sponsors.

## How Classic Cart Studio is made

Classic Cart Studio runs on FCEUmm, an open-source NES emulator written by its own authors. I build it from commit [ac0d00a7](https://github.com/libretro/libretro-fceumm/commit/ac0d00a7fc01166f84c7bac01d1084ebfd2b865e), with two Mapper 30 fixes prepared for upstream submission. The included homebrew games are their creators' builds, unchanged, and the games you add are your own files. The underlying emulator and the games are the work of their respective authors.

The app around them is what I build: the collection tools, the menus, the player, the installer and the help. I build it with AI agents. They write the code, and I direct the work. I set each task, review what comes back, send it back until it's right, and decide what ships. It's been a second job since day one, part-time every week on top of my full-time one.

Anything that touches a console stays switched off until it has passed on real hardware. Automated checks back that up: a pinned emulator version, game identification by file contents, and a public build record for every release.

The pixel characters and many of the icons and diagrams are drawn in code by the AI agents. Artwork by human artists is credited separately and kept apart from those characters, and these Credits point out any generated imagery that remains. Game covers and screenshots are scans and captures of the original games. The HOME music was made with Suno, and I spent several days shaping it. The console transfer component is adapted from Hakchi2 CE source, credited here. Its license notices ship with the app, and its source is published alongside each release.

Classic Cart Studio is developed by Rance9 and published by Rance9 LLC.

## Credits and licenses

- This project's code: GNU General Public License v3 or later, `GPL-3.0-or-later` (see `LICENSE` and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)).
- The Studio's pixel characters, interface icons and walkway are original Classic Cart Studio artwork drawn in code (`tools/publish/make_ccs_cast.py`). The four HOME theme designs and the console startup pictures were made with image generators for this project and show Classic Cart Studio's own characters, not characters from the games. The five scene backgrounds behind the HOME and on game details are pixel art by Luis Zuno (Ansimuz): Magic Cliffs under CC BY 3.0, the others under CC0 (each composed from the artist's own layers, cropped and enlarged by whole pixels; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)). The HOME music was made with Suno.
- Included Games: Thwaite and Concentration Room by Damian Yerrick, GNU GPL v3 or later, the author's own builds with their source (`site-data/included`).
- [FCEUmm](https://github.com/libretro/libretro-fceumm) (libretro-fceumm, GPL v2 or later) is the emulator inside the game player, built from upstream commit `ac0d00a7fc01166f84c7bac01d1084ebfd2b865e` with two Mapper 30 fixes prepared for upstream submission (the patches and what they change: [VENDORED.md](tools/player/third_party/fceumm/VENDORED.md)). Box art from community thumbnail collections identifies the games. All rights in the games remain with their owners; third-party material is not covered by this project's license (see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)).
- [Hakchi2 CE](https://github.com/TeamShinkansen/Hakchi2-CE) 3.9.3 by TeamShinkansen and contributors (GPL v3) does the console side. The Windows installer includes its official, unmodified portable package so that everything works offline; its license and source information are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
- Microsoft Edge WebView2 (the SDK, and Microsoft's Runtime installers for computers without the Runtime) displays the Studio's interface inside the Windows application; .NET Framework 4.8 runs the host.
- "Press Start 2P" font: SIL Open Font License.
- Game identification data compiled from No-Intro, NesCartDB and Mesen database releases.

## Legal, privacy and third-party notices

- [LICENSE](LICENSE): GNU General Public License v3 for this project's own code (GPL-3.0-or-later).
- [Installation Terms](tools/publish/assets/legal/INSTALLATION-TERMS.md) (version 1.3): the terms and risk notices the Windows setup shows before it installs. They do not replace or restrict the GPL; they are also in the Studio's Help & User Guide → Installation Terms (EULA).
- [PRIVACY.md](PRIVACY.md): what the website and the Windows application store and transmit.
- Game files, backups and copyright: Classic Cart Studio supplies no game files except its Included Games (homebrew shared under each author's license or permission), and it never uploads your game files or backups or hosts them. Users supply their own game files and are responsible for having the rights to use and back them up; creating a backup does not grant permission to distribute it. The full statement is in the Help & User Guide → Legal, Privacy & Disclaimers.
- [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md): Hakchi2 CE, WebView2, fonts, identification databases, artwork and build tools, with what is bundled and what is downloaded separately.
- The same information, in plain words, is in the Studio's Help & User Guide → Legal, Privacy & Disclaimers.

## Security

See `SECURITY.md` for what the Windows application and its backend do, how to verify downloads and how to report a problem.
